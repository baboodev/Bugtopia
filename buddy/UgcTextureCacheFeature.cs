using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGC TEXTURE CACHE — diagnostics + fix for photo frames / movie screens / UGC-photo furniture
    // (custom jigsaw puzzles, display shelves, etc.) rendering blank/white.
    //
    // Root cause chain (docs/FEATURES.md has the full writeup): every one of those surfaces routes
    // through the SAME chokepoint — XDTViewBase.Loader.DownLoadTexture2dAdvancedLoader — which first
    // checks XDTBaseService.Services.Cache.LocalTextureCacheService's on-disk LRU cache, then falls
    // back to an OBS cloud download. Confirmed call sites: PhotoFrameComponent (frames),
    // MovieScreenComponent (screens), UGCPhotoComponent (generic UGC-photo furniture incl. custom
    // puzzles/decals), BrgDisplayComponent (display shelves) — all construct their loader via
    // DownLoadTexture2dAdvancedLoader.Create(this, photoId, w, h, ImageEnum, ...).
    //
    // Two of the game's own LocalTextureCacheService pools (Limit/, Announcement/, Draw/ — the ones
    // frames/screens/puzzles/decals actually use) are capped at a hardcoded 100 entries
    // (LocalTextureCacheService.OnCreate() -> Init(100)); Photo/Head/NoBgPhoto are effectively
    // unbounded (_unLimited = 100000). Hitting the 100-cap evicts the LRU tail AND DELETES its file
    // on disk (Texture2DCache.Dispose()), so a town with more than ~100 distinct UGC photos visible
    // over a session churns through re-downloads — and any download hiccup during that churn shows
    // as a permanently blank texture until the next successful fetch.
    //
    // The fix — raise the LRU capacity on the three capped pools via AuraMono (LRUPool<T,T1>.capacity
    // is a plain private int compared against size in Put() — not baked into any fixed-size array —
    // so raising it is a single field write, no cache reconstruction needed). Verified every ~5s (not
    // fire-and-forget): the Game LOD PC_LODBIAS saga proved a value the game's own code can touch
    // needs re-assertion, and while nothing else in the dumps writes LRUPool.capacity after
    // construction, a service recreation would silently undo a fire-once write — so we read back and
    // only write when it actually drifted.
    //
    // Independent of that toggle: "Purge Texture Cache" deletes every file under ScreenCapture's
    // Photo/, Head/, Limit/, Announcement/, NoBgPhoto/ subfolders (pure re-downloadable caches of
    // OTHER players' content). Deliberately excludes Draw/ — that folder is the Pictures feature's
    // own domain (PicturesDecryptFeature.cs manages it with its own manifest/extract/upload flow)
    // and ScreenCaptureDecrypted (the user's own decrypted library) is never touched. Coroutine-
    // driven (ModCoroutines, Pictures' own idiom) so a large cache doesn't hitch a frame.
    //
    // RETIRED (2026-07-26): a third control used to live here — a Mono NativeDetour on
    // DownLoadTexture2dAdvancedLoader.OnDownLoadFailed (mono_compile_method -> native entry ->
    // NativeDetour) that logged the failing objectId/pool/size per failure. It crashed the game on
    // THREE separate live hits (WER dumps coreclr_32708, coreclr_31016, coreclr_24268), each time
    // immediately after the hook's own log line printed successfully. Two targeted fixes were tried
    // in between (pin the raw `self` pointer before reading its fields — a real bug, SGen moving GC
    // could relocate it mid-read; then a main-thread-only guard, in case the async download-callback
    // fired off-thread) — neither stopped the recurrence, and the second dump proved the callback
    // WAS on the expected thread, ruling out the thread hypothesis outright.
    // [[auramono-native-hook-and-settings-gotchas]] records that this exact technique — detouring the
    // address mono_compile_method returns for a JITed method — has ALREADY hard-crashed this build
    // for two unrelated methods (InstrumentPanel.OnStart/OnStop, ActivityEventModule.
    // CreateActivityBubble): a RIP-relative instruction in the prologue breaks once MonoMod's
    // trampoline relocates it. Three independently-discovered methods failing the same way is a
    // pattern, not a coincidence — this specific detour target is very likely unsafe at the
    // instruction level regardless of how careful the C# side is. The team's own prior resolution to
    // the same situation (Bubble feature, June 2026) was to drop the hook rather than keep patching
    // around it, so that's what happened here too. The two controls above need no hook and are
    // unaffected.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Config-backed state (persisted via UnifiedConfigData; see HeartopiaComplete.Config.cs)
        // ----------------------------------------------------------------------------------------
        internal bool ugcCacheRaiseLimitEnabled = false;
        internal int ugcCacheTargetCapacity = 500;   // 100..2000, see UgcCacheMinCapacity/MaxCapacity

        // ----------------------------------------------------------------------------------------
        // Runtime-only state
        // ----------------------------------------------------------------------------------------
        private const int UgcCacheDefaultCapacity = 100; // LocalTextureCacheService.OnCreate() -> Init(100)
        private const int UgcCacheMinCapacity = 100;
        private const int UgcCacheMaxCapacity = 2000;
        private const float UgcCacheApplyIntervalSeconds = 5f;

        private static readonly string[] UgcCacheManagerFieldNames =
        {
            "_texture2dCacheManagerForLimited",
            "_texture2dCacheManagerForAnnouncement",
            "_texture2dCacheManagerForDraw"
        };

        // Folders under ScreenCapture that are pure re-downloadable caches of OTHER players' UGC
        // content. Draw/ (the user's own drawings, managed by PicturesDecryptFeature) and
        // ScreenCaptureDecrypted (Pictures' decrypted output) are deliberately never touched here.
        private static readonly string[] UgcCachePurgeSubfolders = { "Photo", "Head", "Limit", "Announcement", "NoBgPhoto" };

        internal string ugcCacheApplyStatus = "";
        internal string ugcCachePurgeStatus = "";

        private IntPtr ugcCacheServiceClass = IntPtr.Zero;
        private int ugcCacheAppliedCapacity = -1; // -1 = not verified yet this session
        private float nextUgcCacheApplyAt;
        private FeatureBreakerState ugcCacheApplyBreaker;

        private object ugcCachePurgeCoroutine;

        // ----------------------------------------------------------------------------------------
        // OnUpdate tick — called from HeartopiaComplete.cs's central OnUpdate chain
        // ----------------------------------------------------------------------------------------
        private void ProcessUgcTextureCacheFeatureOnUpdate()
        {
            float now = Time.unscaledTime;
            if (now < this.nextUgcCacheApplyAt || !this.ugcCacheApplyBreaker.ShouldRun(now))
            {
                return;
            }

            this.nextUgcCacheApplyAt = now + UgcCacheApplyIntervalSeconds;

            try
            {
                if (this.IsUgcCacheAuraReady())
                {
                    int target = this.ugcCacheRaiseLimitEnabled
                        ? Mathf.Clamp(this.ugcCacheTargetCapacity, UgcCacheMinCapacity, UgcCacheMaxCapacity)
                        : UgcCacheDefaultCapacity;
                    this.TickUgcCacheCapacity(target);
                }

                this.ugcCacheApplyBreaker.Success();
            }
            catch (Exception ex)
            {
                this.ugcCacheApplyBreaker.Failure("UgcCache", ex, now);
            }
        }

        private bool IsUgcCacheAuraReady()
        {
            return this.EnsureAuraMonoApiReady() && this.AttachAuraMonoThread()
                && AuraMonoStaticFieldReadsAllowed() && auraMonoRuntimeInvoke != null;
        }

        // ----------------------------------------------------------------------------------------
        // Capacity raise/revert — verified every tick (re-read, only write on drift), not
        // fire-and-forget. See file header for why: nothing else writes LRUPool.capacity after
        // construction, but a service recreation would silently undo a stale one-shot write.
        // ----------------------------------------------------------------------------------------
        private unsafe void TickUgcCacheCapacity(int target)
        {
            if (this.ugcCacheServiceClass == IntPtr.Zero)
            {
                this.ugcCacheServiceClass = this.FindAuraMonoClassByFullName(
                    "XDTBaseService.Services.Cache.LocalTextureCacheService");
            }

            if (this.ugcCacheServiceClass == IntPtr.Zero)
            {
                this.ugcCacheApplyStatus = "LocalTextureCacheService class unresolved";
                return;
            }

            int seen = 0;
            int changed = 0;
            string firstMissing = null;

            for (int i = 0; i < UgcCacheManagerFieldNames.Length; i++)
            {
                string fieldName = UgcCacheManagerFieldNames[i];
                if (!this.TryGetAuraMonoStaticObjectField(this.ugcCacheServiceClass, fieldName, out IntPtr managerObj)
                    || managerObj == IntPtr.Zero)
                {
                    firstMissing = firstMissing ?? (fieldName + " not initialized yet (world not loaded?)");
                    continue;
                }

                seen++;
                uint managerPin = AuraMonoPinNew(managerObj);
                try
                {
                    if (!this.TryGetMonoObjectMember(managerObj, "_lruPool", out IntPtr lruPoolObj) || lruPoolObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint poolPin = AuraMonoPinNew(lruPoolObj);
                    try
                    {
                        if (!this.TryGetMonoInt32Member(lruPoolObj, "capacity", out int current) || current == target)
                        {
                            continue;
                        }

                        IntPtr poolClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(lruPoolObj) : IntPtr.Zero;
                        IntPtr capField = poolClass != IntPtr.Zero
                            ? this.FindAuraMonoFieldOnHierarchy(poolClass, "capacity")
                            : IntPtr.Zero;
                        if (capField == IntPtr.Zero || auraMonoFieldSetValue == null)
                        {
                            continue;
                        }

                        int value = target;
                        auraMonoFieldSetValue(lruPoolObj, capField, (IntPtr)(&value));
                        changed++;
                    }
                    finally
                    {
                        AuraMonoPinFree(poolPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(managerPin);
                }
            }

            if (seen == 0)
            {
                this.ugcCacheApplyStatus = firstMissing ?? "cache managers not initialized yet";
                return;
            }

            if (changed > 0 || this.ugcCacheAppliedCapacity != target)
            {
                this.ugcCacheAppliedCapacity = target;
                this.ugcCacheApplyStatus = target == UgcCacheDefaultCapacity
                    ? this.L("Reverted to game default (100 slots).")
                    : this.LF("Applied: {0} cache slots (Limit/Announcement/Draw).", target);
                if (changed > 0)
                {
                    ModLogger.Msg("[UgcCache] capacity -> " + target + " (" + changed + "/" + seen + " pools updated)");
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Purge — deletes on-disk cache files only (never touches live in-memory Texture2D objects;
        // GetObj() re-checks File.Exists on next access, so this is a safe "force re-download").
        // ----------------------------------------------------------------------------------------
        internal void StartUgcCachePurge()
        {
            if (this.ugcCachePurgeCoroutine != null)
            {
                return;
            }

            this.ugcCachePurgeStatus = this.L("Purging…");
            this.ugcCachePurgeCoroutine = ModCoroutines.Start(this.UgcCachePurgeRoutine());
        }

        private IEnumerator UgcCachePurgeRoutine()
        {
            string root = this.TryGetScreenCaptureRootPath();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                this.ugcCachePurgeStatus = "ScreenCapture folder not found.";
                this.ugcCachePurgeCoroutine = null;
                yield break;
            }

            int deleted = 0;
            int failed = 0;
            long bytes = 0;
            int processed = 0;

            for (int s = 0; s < UgcCachePurgeSubfolders.Length; s++)
            {
                string dir = Path.Combine(root, UgcCachePurgeSubfolders[s]);
                string[] files;
                try
                {
                    files = Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>();
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[UgcCache] purge: listing " + dir + " failed: " + ex.Message);
                    continue;
                }

                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        FileInfo fi = new FileInfo(files[i]);
                        long len = fi.Length;
                        fi.Delete();
                        deleted++;
                        bytes += len;
                    }
                    catch
                    {
                        failed++;
                    }

                    processed++;
                    if (processed % 200 == 0)
                    {
                        this.ugcCachePurgeStatus = this.LF("Purging… {0} deleted", deleted);
                        yield return null;
                    }
                }
            }

            this.ugcCachePurgeStatus = failed > 0
                ? this.LF("Purged {0} file(s) ({1:0.0} MB); {2} could not be deleted (in use).", deleted, bytes / 1048576.0, failed)
                : this.LF("Purged {0} file(s) ({1:0.0} MB). They will re-download as needed.", deleted, bytes / 1048576.0);
            ModLogger.Msg("[UgcCache] purge complete: deleted=" + deleted + " failed=" + failed + " bytes=" + bytes);
            this.ugcCachePurgeCoroutine = null;
        }

        internal bool IsUgcCachePurgeBusy()
        {
            return this.ugcCachePurgeCoroutine != null;
        }

        // Config-load hook (Config.cs): clamp the persisted target and force an immediate
        // apply/revert check instead of waiting up to UgcCacheApplyIntervalSeconds.
        private void SyncUgcCacheAfterConfigLoad()
        {
            this.ugcCacheTargetCapacity = Mathf.Clamp(
                this.ugcCacheTargetCapacity <= 0 ? 500 : this.ugcCacheTargetCapacity,
                UgcCacheMinCapacity, UgcCacheMaxCapacity);
            this.nextUgcCacheApplyAt = 0f;
        }
    }
}
