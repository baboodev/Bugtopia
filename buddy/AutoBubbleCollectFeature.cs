using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Auto Bubble Collect (detour B of the bubble pair; detour A = BubbleSpawnRewriteFeature.cs).
    //
    // Detection: NativeDetour on the Mono entity-sync chokepoint
    //   BubbleProtocolManager.CreateBubble(bubbleCreateStruct, LevelData, BubbleRefreshType, bool)
    // — the 4-arg overload EVERY server-synced bubble flows through (BubbleSyncSystem's
    // ComponentAdded<BubbleIdComponent> handler), regardless of who requested the spawn.
    // The old BubbleFeature.cs header's "detours crashed" verdict was about the abandoned
    // 14-byte BubbleMonoNativeHook steal / unmanaged-thunk path; this uses the proven
    // mono_compile_method + MonoMod NativeDetour (Iced reloc) technique (EventHook engine,
    // building cell-patch, player-name detours).
    //
    // Collection: the game collects a bubble when the player's capsule trigger touches it
    // (PlayerTriggerBubbleCase) by calling BubbleProtocolManager.GetBubbleAward(entity.netId).
    // The wire command carries ONLY the netId ([VerifyEntity(VerifyOwner=false)]) — no position —
    // so we invoke the same static NON-generic wrapper via mono_runtime_invoke (never invoke
    // generics — CraftProtocolManager.MakeItem precedent).
    //
    // Win64 ABI of the hooked method: void return (no sret), 4 integer-class args:
    //   RCX = bubbleCreateStruct* (48-byte struct passed by-ref as caller copy)
    //   RDX = LevelData object ptr, R8d = BubbleRefreshType, R9b = bool isEmptyAward
    // bubbleCreateStruct layout: parentNetID@0, bubbleId@4, netId@8, position@12,
    //   bornPosition@24, lootItemID@36, bubbleLocationId@40, bornAnim@44.
    public partial class HeartopiaComplete
    {
        // -- config (persisted; GUI in the Automation tab bubble block) --
        private bool autoBubbleCollectEnabled = false;
        private float autoBubbleCollectRadius = 0f; // meters; 0 = unlimited

        // -- shared compile helper for the bubble detour pair (A + B) --
        private delegate IntPtr BubbleDetourCompileMethodDelegate(IntPtr method);
        private static BubbleDetourCompileMethodDelegate bubbleDetourCompileMethod;

        // mono_compile_method → JIT-compiled native entry (NOT mono_method_get_unmanaged_thunk,
        // which never fired for the old bubble hooks — see memory auramono-native-hook gotchas).
        private IntPtr CompileBubbleDetourTarget(IntPtr method)
        {
            if (method == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (bubbleDetourCompileMethod == null)
            {
                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                if (monoModule != IntPtr.Zero)
                {
                    bubbleDetourCompileMethod = this.GetAuraMonoExport<BubbleDetourCompileMethodDelegate>(
                        monoModule, "mono_compile_method");
                }
            }

            return bubbleDetourCompileMethod != null ? bubbleDetourCompileMethod(method) : IntPtr.Zero;
        }

        // -- detour state --
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void BubbleCreateHookDelegate(
            IntPtr bubbleDataPtr, IntPtr levelDataPtr, int refreshType, byte isEmptyAward);

        private static MonoMod.RuntimeDetour.NativeDetour bubbleCreateDetour;
        private static BubbleCreateHookDelegate bubbleCreateHookKeepAlive; // anti-GC
        private static BubbleCreateHookDelegate bubbleCreateTrampoline;
        private bool bubbleCreateDetourHardFailed;
        private float bubbleCreateNextInstallAttemptAt = -999f;
        private const float BubbleCollectInstallRetrySeconds = 5f;

        // -- ring buffer: produced by the native body, drained in OnUpdate. Both run on the Unity
        // main thread (bubble sync happens in main-thread game logic), so single-threaded in
        // practice; buffers preallocated so the native-boundary body never allocates. --
        private const int BubbleClaimRingSize = 64; // power of two
        private static readonly uint[] bubbleRingNetId = new uint[BubbleClaimRingSize];
        private static readonly float[] bubbleRingX = new float[BubbleClaimRingSize];
        private static readonly float[] bubbleRingY = new float[BubbleClaimRingSize];
        private static readonly float[] bubbleRingZ = new float[BubbleClaimRingSize];
        private static readonly int[] bubbleRingType = new int[BubbleClaimRingSize];
        private static readonly byte[] bubbleRingEmpty = new byte[BubbleClaimRingSize];
        private static int bubbleRingWrite;
        private static int bubbleRingRead;

        // -- claim pipeline --
        private struct PendingBubbleClaim
        {
            public uint NetId;
            public Vector3 Position;
            public int RefreshType;
            public bool EmptyAward;
            public float QueuedAt;
        }

        private readonly List<PendingBubbleClaim> pendingBubbleClaims = new List<PendingBubbleClaim>();
        private readonly Dictionary<uint, float> bubbleRecentlyClaimedAt = new Dictionary<uint, float>();
        private readonly List<uint> bubbleClaimPruneScratch = new List<uint>();
        private IntPtr bubbleGetAwardMethodPtr = IntPtr.Zero;
        private float bubbleNextClaimAllowedAt;
        private const int BubblePendingClaimCap = 128;
        private const float BubbleClaimDripSeconds = 0.30f;   // ~3 claims/s max (world-load bursts)
        private const float BubbleClaimDedupeTtl = 15f;       // weather-unhide re-add guard
        private const float BubbleClaimPendingTtl = 10f;      // drop stale queue entries

        // GUI: retry the install immediately when the toggle is flipped on.
        private void RequestAutoBubbleCollectImmediateInstall()
        {
            this.bubbleCreateNextInstallAttemptAt = -999f;
        }

        // ================= detour install =================

        private void EnsureBubbleCreateDetourInstalled()
        {
            if (bubbleCreateDetour != null || this.bubbleCreateDetourHardFailed)
            {
                return;
            }

            if (Time.unscaledTime < this.bubbleCreateNextInstallAttemptAt)
            {
                return;
            }
            this.bubbleCreateNextInstallAttemptAt = Time.unscaledTime + BubbleCollectInstallRetrySeconds;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // AuraMono not up yet — retry on a later frame
                }

                IntPtr cls = this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.Bubble.BubbleProtocolManager");
                if (cls == IntPtr.Zero)
                {
                    return; // image not loaded yet — retry later
                }

                // 4-arg overload = the render-side sync chokepoint. The 1-arg overload is the
                // SENDER (CreateBubbleNetworkCommand) — that one belongs to detour A.
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "CreateBubble", 4);
                if (method == IntPtr.Zero)
                {
                    this.bubbleCreateDetourHardFailed = true;
                    ModLogger.Msg("[AutoBubble] CreateBubble(4-arg) not found — auto collect disabled");
                    return;
                }

                IntPtr nativePtr = this.CompileBubbleDetourTarget(method);
                if (nativePtr == IntPtr.Zero)
                {
                    this.bubbleCreateDetourHardFailed = true;
                    ModLogger.Msg("[AutoBubble] mono_compile_method unavailable/null — auto collect disabled");
                    return;
                }

                bubbleCreateHookKeepAlive = BubbleCreateNative;
                bubbleCreateDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, bubbleCreateHookKeepAlive);
                bubbleCreateTrampoline = bubbleCreateDetour.GenerateTrampoline<BubbleCreateHookDelegate>();
                if (bubbleCreateTrampoline == null)
                {
                    // Without the trampoline the game would stop rendering bubbles — revert.
                    try { bubbleCreateDetour.Undo(); } catch { }
                    bubbleCreateDetour = null;
                    bubbleCreateHookKeepAlive = null;
                    this.bubbleCreateDetourHardFailed = true;
                    ModLogger.Msg("[AutoBubble] trampoline unavailable; detour reverted");
                    return;
                }

                ModLogger.Msg("[AutoBubble] NativeDetour installed on CreateBubble(4-arg) @0x"
                    + nativePtr.ToInt64().ToString("X"));
            }
            catch (Exception ex)
            {
                this.bubbleCreateDetourHardFailed = true; // never crash-loop
                ModLogger.Msg("[AutoBubble] install failed: " + ex.Message);
            }
        }

        // Native detour body. Boundary rules (same discipline as EventSlotBody*): no allocation,
        // no throw across the boundary, no calls into Mono/Il2Cpp/Unity. Copies scalars out of the
        // caller's struct copy (dead after the call) into the reused ring and forwards.
        private static unsafe void BubbleCreateNative(
            IntPtr bubbleDataPtr, IntPtr levelDataPtr, int refreshType, byte isEmptyAward)
        {
            try
            {
                if (bubbleDataPtr != IntPtr.Zero)
                {
                    byte* p = (byte*)bubbleDataPtr;
                    int w = bubbleRingWrite;
                    int idx = w & (BubbleClaimRingSize - 1);
                    bubbleRingNetId[idx] = *(uint*)(p + 8);    // bubbleCreateStruct.netId
                    bubbleRingX[idx] = *(float*)(p + 12);      // .position
                    bubbleRingY[idx] = *(float*)(p + 16);
                    bubbleRingZ[idx] = *(float*)(p + 20);
                    bubbleRingType[idx] = refreshType;
                    bubbleRingEmpty[idx] = isEmptyAward;
                    bubbleRingWrite = w + 1;
                }
            }
            catch
            {
            }

            BubbleCreateHookDelegate orig = bubbleCreateTrampoline;
            if (orig != null)
            {
                orig(bubbleDataPtr, levelDataPtr, refreshType, isEmptyAward);
            }
        }

        // ================= OnUpdate drain + claim =================

        private void ProcessAutoBubbleCollectOnUpdate()
        {
            if (!this.autoBubbleCollectEnabled)
            {
                // Discard while off so a later enable doesn't claim stale/expired entities.
                bubbleRingRead = bubbleRingWrite;
                if (this.pendingBubbleClaims.Count > 0)
                {
                    this.pendingBubbleClaims.Clear();
                }
                return;
            }

            this.EnsureBubbleCreateDetourInstalled();
            this.DrainBubbleRingIntoPendingClaims();
            this.TryIssueOneBubbleClaim();
        }

        private void DrainBubbleRingIntoPendingClaims()
        {
            // If the ring overflowed between drains (write raced past read by more than the ring
            // size), the oldest entries were overwritten — snap read forward so we never mix
            // half-overwritten slots into claims.
            if (bubbleRingWrite - bubbleRingRead > BubbleClaimRingSize)
            {
                bubbleRingRead = bubbleRingWrite - BubbleClaimRingSize;
            }

            float now = Time.unscaledTime;
            int guard = 0;
            while (bubbleRingRead != bubbleRingWrite && guard++ < BubbleClaimRingSize)
            {
                int idx = bubbleRingRead & (BubbleClaimRingSize - 1);
                uint netId = bubbleRingNetId[idx];
                Vector3 pos = new Vector3(bubbleRingX[idx], bubbleRingY[idx], bubbleRingZ[idx]);
                int type = bubbleRingType[idx];
                bool empty = bubbleRingEmpty[idx] != 0;
                bubbleRingRead++;

                if (netId == 0u || this.pendingBubbleClaims.Count >= BubblePendingClaimCap)
                {
                    continue;
                }

                // Re-adds fire the same chokepoint again for the same netId (weather-bubble
                // unhide re-dispatches ComponentAdded) — don't double-claim.
                if (this.bubbleRecentlyClaimedAt.TryGetValue(netId, out float claimedAt)
                    && now - claimedAt < BubbleClaimDedupeTtl)
                {
                    continue;
                }

                this.pendingBubbleClaims.Add(new PendingBubbleClaim
                {
                    NetId = netId,
                    Position = pos,
                    RefreshType = type,
                    EmptyAward = empty,
                    QueuedAt = now
                });
            }
        }

        private void TryIssueOneBubbleClaim()
        {
            if (this.pendingBubbleClaims.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.bubbleNextClaimAllowedAt)
            {
                return;
            }

            this.PruneBubbleClaimDedupe(now);

            PendingBubbleClaim claim = this.pendingBubbleClaims[0];
            this.pendingBubbleClaims.RemoveAt(0);

            if (now - claim.QueuedAt > BubbleClaimPendingTtl)
            {
                return; // stale (e.g. queued right before a world switch)
            }

            // Radius gate (0 = unlimited). Skeleton-first player position — transform.root is
            // the SHIP while sea-fishing, so never use the root for distance checks.
            if (this.autoBubbleCollectRadius > 0.01f
                && this.TryGetLocalPlayerPosition(out Vector3 playerPos))
            {
                float r = this.autoBubbleCollectRadius;
                if ((claim.Position - playerPos).sqrMagnitude > r * r)
                {
                    return; // too far — skip this bubble entirely
                }
            }

            if (this.TryInvokeBubbleGetAwardAura(claim.NetId))
            {
                this.bubbleRecentlyClaimedAt[claim.NetId] = now;
                this.bubbleNextClaimAllowedAt = now + BubbleClaimDripSeconds
                    + UnityEngine.Random.Range(0f, 0.15f);
                ModLogger.Msg("[AutoBubble] claimed netId=" + claim.NetId
                    + " type=" + claim.RefreshType + (claim.EmptyAward ? " (empty)" : string.Empty));
            }
        }

        private void PruneBubbleClaimDedupe(float now)
        {
            if (this.bubbleRecentlyClaimedAt.Count <= 256)
            {
                return;
            }

            this.bubbleClaimPruneScratch.Clear();
            foreach (KeyValuePair<uint, float> kv in this.bubbleRecentlyClaimedAt)
            {
                if (now - kv.Value >= BubbleClaimDedupeTtl)
                {
                    this.bubbleClaimPruneScratch.Add(kv.Key);
                }
            }

            for (int i = 0; i < this.bubbleClaimPruneScratch.Count; i++)
            {
                this.bubbleRecentlyClaimedAt.Remove(this.bubbleClaimPruneScratch[i]);
            }
            this.bubbleClaimPruneScratch.Clear();
        }

        // BubbleProtocolManager.GetBubbleAward(uint) — static NON-generic wrapper; it builds the
        // GetBubbleAward network command and calls WebRequestUtility.SendCommand<T> internally in
        // normally-JIT'd game code (never mono_runtime_invoke a generic ourselves).
        private unsafe bool TryInvokeBubbleGetAwardAura(uint netId)
        {
            if (netId == 0u
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.bubbleGetAwardMethodPtr == IntPtr.Zero)
            {
                IntPtr cls = this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.Bubble.BubbleProtocolManager");
                if (cls == IntPtr.Zero)
                {
                    return false;
                }

                this.bubbleGetAwardMethodPtr = this.FindAuraMonoMethodOnHierarchy(cls, "GetBubbleAward", 1);
                if (this.bubbleGetAwardMethodPtr == IntPtr.Zero)
                {
                    ModLogger.Msg("[AutoBubble] GetBubbleAward(1) not found");
                    return false;
                }
            }

            try
            {
                uint localNetId = netId;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&localNetId); // value-type arg = pointer to the raw value
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.bubbleGetAwardMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                return exc == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }
    }
}
