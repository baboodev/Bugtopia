using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // Privacy pause — hold Homeland Auto Farm while another player stands near the home plot.
    //
    // ── WHAT "NEAR" MEANS ───────────────────────────────────────────────────────────────────────
    // The distance is measured to the nearest EDGE of the plot rectangle, not to a point. The
    // rectangle is the game's own: FieldComponent.minCorner/maxCorner, which RefreshLevelObjectRect
    // derives from fieldZone.rect.center ± size/2 — the same rectangle OutOfBoundsTesting uses to
    // decide whether a building may be placed. Those corners live in the plot's LOCAL frame, so a
    // player position is transformed by LevelObject.localToWorldMatrix.inverse first; that is what
    // makes a ROTATED plot measure correctly.
    //
    //     local = worldToLocal * playerWorldPos
    //     dx    = max(0, min.x - local.x, local.x - max.x)
    //     dz    = max(0, min.z - local.z, local.z - max.z)
    //     dist  = sqrt(dx² + dz²)                       // 0 = the player is standing on the plot
    //
    // The game ships FieldComponent.CheckInArea(position, safeDis) and it is deliberately NOT used:
    // it widens the rectangle per axis (Chebyshev), so a player off a CORNER counts as near at up
    // to radius·√2 — 41 % further than asked for. It also returns a bare bool, and the metres are
    // what make the status line and the log readable.
    //
    // ── WHERE IT RUNS ───────────────────────────────────────────────────────────────────────────
    // The scan is AuraMono (Entities.GetComponents<RemotePlayerComponent> + two invokes for the
    // plot), so it runs ONLY from inside the auto-farm coroutine, sequentially with the farm's own
    // Aura passes — interleaving them crashes the process (HomelandFarmFeature.cs, the water+weed
    // hotkey pause). It publishes a cached verdict; the event-driven weeder in
    // HomelandFarmEventDiagFeature reads that flag, never the scan.
    //
    // Freshness comes from CHUNKING the loop's sleep (HomelandFarmAutoPrivacyAwareSleepRoutine),
    // not from shortening it: the loop deliberately sleeps up to 60 s, and waking it early re-runs
    // the empty-slot radius scan — the steady-growth hitch that was removed on purpose. A chunk
    // wakes, polls players only, and either sleeps on or breaks out.
    //
    // ── WHEN IT DOES NOT APPLY ──────────────────────────────────────────────────────────────────
    // ECS components stream by proximity to the LOCAL player, so away from the field the scan sees
    // players near US, not near the plot — "nobody seen" would be a false all-clear. The gate is
    // therefore inert while the loop reports we are away, and says so once in the log. Auto-farm
    // keeps running there on purpose: remote weeding and sowing are the point of being away.
    //
    // Manual tab buttons and the water+weed hotkey are NOT gated — they are an explicit user act.
    //
    // Failure is fail-OPEN (farm keeps running) and always logged: fail-closed would brick auto
    // farming on any resolution miss, which is far worse than one unpaused tick.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string HomelandFarmPrivacyTag = "HomelandFarmPrivacy";

        // One slider, 0 = off (the autoBubbleCollectRadius precedent) — no separate toggle.
        private const float HomelandFarmPrivacyMinRadius = 0f;
        private const float HomelandFarmPrivacyMaxRadius = 100f;
        private const float HomelandFarmPrivacyDefaultRadius = 0f;

        // Hysteresis on the BOOLEAN state, as the foraging witness check does: once paused, the
        // player has to walk 5 m further out than they walked in, so someone loitering on the
        // boundary cannot flap the farm on and off.
        private const float HomelandFarmPrivacyHysteresisMeters = 5f;

        // Poll cadence. Also the sleep-chunk size, so a chunk boundary and a due poll coincide.
        private const float HomelandFarmPrivacyPollSeconds = 5f;

        // Persisted radius in metres; < 1 means the gate is off.
        private float homelandFarmPrivacyRadius = HomelandFarmPrivacyDefaultRadius;

        // UI debounce, the same three-field machine the farm-radius slider uses.
        private float homelandFarmPrivacyRadiusLastSeen = -1f;
        private bool homelandFarmPrivacyRadiusSavePending;
        private float homelandFarmPrivacyRadiusSaveAt;

        // Published verdict. Written only by RefreshHomelandFarmPrivacyState (coroutine side),
        // read by the event-drain weeder, which may not be on the same thread.
        private volatile bool homelandFarmPrivacyBlocked;

        private float homelandFarmPrivacyNextPollAt;
        private float homelandFarmPrivacyNearestMeters = -1f;
        private int homelandFarmPrivacySeenPlayers;
        private bool homelandFarmPrivacyAwayNoticed;

        // Cached class/method pointers. Class and method IntPtrs may stay raw (image lifetime);
        // no OBJECT pointer is ever cached across frames — FieldComponent.OnBeforeDestroy nulls
        // fieldZone, and a stale field object would answer "clear" forever.
        private IntPtr homelandFarmPrivacyRemotePlayerClass;
        private IntPtr homelandFarmPrivacyEntitiesFieldSystemGetter;
        private IntPtr homelandFarmPrivacyGetFieldByOwnerIdMethod;
        private bool homelandFarmPrivacyMethodsResolved;

        // The plot rectangle, resolved fresh on every poll.
        private struct HomelandFarmPrivacyPlot
        {
            public Matrix4x4 WorldToLocal;
            public Vector3 Min;
            public Vector3 Max;
        }

        private bool HomelandFarmPrivacyEnabled => this.homelandFarmPrivacyRadius >= 1f;

        // The cheap read. This is what the event-driven weeder calls; it must never scan.
        internal bool IsHomelandFarmPrivacyBlocking => this.HomelandFarmPrivacyEnabled && this.homelandFarmPrivacyBlocked;

        // Status line for the farm tab while the gate holds the loop.
        private string HomelandFarmPrivacyStatusText()
        {
            string distance = this.homelandFarmPrivacyNearestMeters >= 0f
                ? this.homelandFarmPrivacyNearestMeters.ToString("F0") + "m from the plot"
                : "nearby";
            return "Auto: paused — player " + distance + " (privacy "
                + this.homelandFarmPrivacyRadius.ToString("F0") + "m).";
        }

        // Called from the auto-farm coroutine's finally: a stopped farm must never leave the event
        // weeder gated off.
        private void ResetHomelandFarmPrivacyState()
        {
            this.homelandFarmPrivacyBlocked = false;
            this.homelandFarmPrivacyNextPollAt = 0f;
            this.homelandFarmPrivacyNearestMeters = -1f;
            this.homelandFarmPrivacySeenPlayers = 0;
            this.homelandFarmPrivacyAwayNoticed = false;
        }

        // Re-evaluate the verdict. Self-throttled to HomelandFarmPrivacyPollSeconds, so the loop
        // top and a sleep chunk can both call it without paying for two scans.
        //
        // MUST be called from the auto-farm coroutine only (AuraMono scan — see the file header).
        private void RefreshHomelandFarmPrivacyState(bool inHomeland)
        {
            if (!this.HomelandFarmPrivacyEnabled)
            {
                if (this.homelandFarmPrivacyBlocked)
                {
                    this.homelandFarmPrivacyBlocked = false;
                    FeatureLog.Life(HomelandFarmPrivacyTag, "RESUMED (privacy pause switched off).");
                }

                this.homelandFarmPrivacyNearestMeters = -1f;
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < this.homelandFarmPrivacyNextPollAt)
            {
                return;
            }
            this.homelandFarmPrivacyNextPollAt = now + HomelandFarmPrivacyPollSeconds;

            if (!inHomeland)
            {
                // Away: the component scan reports on our own surroundings, not the plot's, so it
                // cannot answer the question. Say so once instead of publishing a false all-clear.
                if (!this.homelandFarmPrivacyAwayNoticed)
                {
                    this.homelandFarmPrivacyAwayNoticed = true;
                    FeatureLog.Life(HomelandFarmPrivacyTag,
                        "inactive while away from the field (entities stream around the player, not the plot).");
                }

                this.SetHomelandFarmPrivacyBlocked(false, -1f, 0);
                return;
            }

            this.homelandFarmPrivacyAwayNoticed = false;

            if (!this.TryResolveHomelandFarmPrivacyPlot(out HomelandFarmPrivacyPlot plot, out string plotError))
            {
                // Fail open, loudly. FeatureLog.Fail dedupes by text, so a persistent miss is one
                // line, not one per poll.
                FeatureLog.Fail(HomelandFarmPrivacyTag, "plot rectangle unavailable — gate inactive: " + plotError);
                this.SetHomelandFarmPrivacyBlocked(false, -1f, 0);
                return;
            }

            if (!this.TryMeasureHomelandFarmPrivacyNearestPlayer(plot, out float nearest, out int seen, out string scanError))
            {
                FeatureLog.Fail(HomelandFarmPrivacyTag, "player scan unavailable — gate inactive: " + scanError);
                this.SetHomelandFarmPrivacyBlocked(false, -1f, 0);
                return;
            }

            // Hysteresis: leaving needs 5 m more than entering did.
            float threshold = this.homelandFarmPrivacyBlocked
                ? this.homelandFarmPrivacyRadius + HomelandFarmPrivacyHysteresisMeters
                : this.homelandFarmPrivacyRadius;
            bool blocked = nearest >= 0f && nearest <= threshold;

            this.HomelandFarmLog("Privacy poll: players=" + seen
                + " nearest=" + (nearest >= 0f ? nearest.ToString("F1") + "m" : "none")
                + " threshold=" + threshold.ToString("F0") + "m blocked=" + blocked);

            this.SetHomelandFarmPrivacyBlocked(blocked, nearest, seen);
        }

        // Publish the verdict and log the TRANSITIONS only (Tier 1 — a per-poll line would flood).
        private void SetHomelandFarmPrivacyBlocked(bool blocked, float nearest, int seen)
        {
            this.homelandFarmPrivacyNearestMeters = nearest;
            this.homelandFarmPrivacySeenPlayers = seen;
            if (blocked == this.homelandFarmPrivacyBlocked)
            {
                return;
            }

            this.homelandFarmPrivacyBlocked = blocked;
            FeatureLog.Life(HomelandFarmPrivacyTag, blocked
                ? "PAUSED — player " + (nearest >= 0f ? nearest.ToString("F1") + "m" : "?")
                    + " from the plot (radius " + this.homelandFarmPrivacyRadius.ToString("F0") + "m, "
                    + seen + " remote player(s) loaded)."
                : "RESUMED — nearest player "
                    + (nearest >= 0f ? nearest.ToString("F1") + "m" : "none in range") + " from the plot.");
        }

        // Nearest remote player, in metres from the plot RECTANGLE (0 = standing on it). Returns
        // false only when the scan machinery itself could not run; an empty world is nearest = -1.
        private bool TryMeasureHomelandFarmPrivacyNearestPlayer(
            HomelandFarmPrivacyPlot plot,
            out float nearest,
            out int seen,
            out string error)
        {
            nearest = -1f;
            seen = 0;
            error = string.Empty;

            if (this.homelandFarmPrivacyRemotePlayerClass == IntPtr.Zero)
            {
                this.homelandFarmPrivacyRemotePlayerClass = this.FindAuraMonoClassInAllLoadedImages(
                    "RemotePlayerComponent", "XDTLevelAndEntity.Gameplay.Component.Player");
                if (this.homelandFarmPrivacyRemotePlayerClass == IntPtr.Zero)
                {
                    error = "RemotePlayerComponent class not resolved.";
                    return false;
                }
            }

            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(
                        this.homelandFarmPrivacyRemotePlayerClass,
                        out List<IntPtr> players,
                        out bool infrastructureOk,
                        pins))
                {
                    // infrastructureOk separates "the query ran and the world is empty" (fine — no
                    // witnesses) from "the query could not run" (report it).
                    if (!infrastructureOk)
                    {
                        error = "Entities.GetComponents<RemotePlayerComponent> did not run.";
                        return false;
                    }

                    return true; // nobody loaded
                }

                if (players == null)
                {
                    return true;
                }

                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i] == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetAuraMonoEntityPositionFromComponent(players[i], out Vector3 pos))
                    {
                        continue;
                    }

                    seen++;
                    float distance = HomelandFarmPrivacyPlotDistance(plot, pos);
                    if (nearest < 0f || distance < nearest)
                    {
                        nearest = distance;
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            return true;
        }

        // Distance from a world position to the plot rectangle, in the plot's own local frame.
        // Inside the rectangle is 0; outside is the true euclidean distance to the nearest edge
        // (or corner), which is what "N metres from the boundary" means.
        private static float HomelandFarmPrivacyPlotDistance(HomelandFarmPrivacyPlot plot, Vector3 worldPos)
        {
            Vector3 local = plot.WorldToLocal.MultiplyPoint3x4(worldPos);
            float dx = Mathf.Max(0f, Mathf.Max(plot.Min.x - local.x, local.x - plot.Max.x));
            float dz = Mathf.Max(0f, Mathf.Max(plot.Min.z - local.z, local.z - plot.Max.z));
            if (dx <= 0f && dz <= 0f)
            {
                return 0f;
            }

            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }

        // Entities.fieldSystem -> GetFieldByOwnerId(owner) -> { minCorner, maxCorner, fieldZone }.
        // Resolved fresh every poll: the field object is torn down and rebuilt (build mode, world
        // change), and a cached pointer would keep answering after fieldZone was nulled.
        private unsafe bool TryResolveHomelandFarmPrivacyPlot(out HomelandFarmPrivacyPlot plot, out string error)
        {
            plot = default(HomelandFarmPrivacyPlot);
            error = string.Empty;

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                error = "AuraMono unavailable.";
                return false;
            }

            if (!AuraMonoPinningAvailable)
            {
                // Every read below allocates on the mono side; without pinning the field object can
                // move under us. Fail closed on the SCAN (which fails open on the gate).
                error = "AuraMono pinning unavailable.";
                return false;
            }

            if (!this.TryResolveHomelandFarmPrivacyMethods(out error))
            {
                return false;
            }

            uint ownerNetId = this.homelandFarmAutoFieldOwnerNetId;
            if (ownerNetId == 0U && this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint currentOwner))
            {
                ownerNetId = currentOwner;
            }

            if (ownerNetId == 0U && this.TryResolveSelfPlayerNetId(out uint selfNetId))
            {
                ownerNetId = selfNetId; // what GameplayApi.IsPlayerNearSelfHomeLand passes
            }

            if (ownerNetId == 0U)
            {
                error = "field owner netId unknown.";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr fieldSystem = auraMonoRuntimeInvoke(
                this.homelandFarmPrivacyEntitiesFieldSystemGetter, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || fieldSystem == IntPtr.Zero)
            {
                error = "Entities.fieldSystem is null.";
                return false;
            }

            uint systemPin = AuraMonoPinNew(fieldSystem);
            try
            {
                exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&ownerNetId);
                IntPtr field = auraMonoRuntimeInvoke(
                    this.homelandFarmPrivacyGetFieldByOwnerIdMethod, fieldSystem, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || field == IntPtr.Zero)
                {
                    error = "GetFieldByOwnerId(" + ownerNetId + ") returned null.";
                    return false;
                }

                uint fieldPin = AuraMonoPinNew(field);
                try
                {
                    // fieldZone is null when FieldComponent.OnSpawned bailed early (no build world)
                    // or after OnBeforeDestroy. The corners are stale then, and the game's own
                    // CheckInArea would silently answer "outside" — so treat it as no answer.
                    if (!this.TryGetMonoObjectMember(field, "fieldZone", out IntPtr zone) || zone == IntPtr.Zero)
                    {
                        error = "FieldComponent.fieldZone is null (field not spawned).";
                        return false;
                    }

                    uint zonePin = AuraMonoPinNew(zone);
                    try
                    {
                        if (!this.TryGetMonoVector3Member(field, "minCorner", out Vector3 min)
                            || !this.TryGetMonoVector3Member(field, "maxCorner", out Vector3 max))
                        {
                            error = "minCorner/maxCorner unreadable.";
                            return false;
                        }

                        if (max.x - min.x < 0.1f || max.z - min.z < 0.1f)
                        {
                            error = "plot rectangle is degenerate (RefreshLevelObjectRect has not run).";
                            return false;
                        }

                        // CheckInArea uses localToWorldMatrix, NOT the zoneMatrix — the corners are
                        // in that frame, and mixing the two silently shifts the whole rectangle.
                        if (!this.TryGetMonoMatrix4x4Member(zone, "localToWorldMatrix", out Matrix4x4 localToWorld))
                        {
                            error = "LevelObject.localToWorldMatrix unreadable.";
                            return false;
                        }

                        plot.WorldToLocal = localToWorld.inverse;
                        plot.Min = min;
                        plot.Max = max;

                        FeatureLog.Once(HomelandFarmPrivacyTag, "plot",
                            "plot rectangle resolved for owner " + ownerNetId + ": "
                            + (max.x - min.x).ToString("F1") + " x " + (max.z - min.z).ToString("F1") + " m.");
                        return true;
                    }
                    finally
                    {
                        AuraMonoPinFree(zonePin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(fieldPin);
                }
            }
            finally
            {
                AuraMonoPinFree(systemPin);
            }
        }

        // Class and method pointers only — safe to cache for the image's lifetime.
        private bool TryResolveHomelandFarmPrivacyMethods(out string error)
        {
            error = string.Empty;
            if (this.homelandFarmPrivacyMethodsResolved)
            {
                if (this.homelandFarmPrivacyEntitiesFieldSystemGetter == IntPtr.Zero
                    || this.homelandFarmPrivacyGetFieldByOwnerIdMethod == IntPtr.Zero)
                {
                    error = "field-system methods not resolved.";
                    return false;
                }

                return true;
            }

            IntPtr entitiesClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities");
            if (entitiesClass == IntPtr.Zero)
            {
                entitiesClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager", "Entities");
            }

            IntPtr fieldSystemClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GameplaySystem.CraftingSystem.FieldComponentSystem");
            if (fieldSystemClass == IntPtr.Zero)
            {
                fieldSystemClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                    "XDTLevelAndEntity.GameplaySystem.CraftingSystem", "FieldComponentSystem");
            }

            if (entitiesClass == IntPtr.Zero || fieldSystemClass == IntPtr.Zero)
            {
                // Images load with the world; do not latch the miss.
                error = "Entities / FieldComponentSystem class not loaded yet.";
                return false;
            }

            this.homelandFarmPrivacyEntitiesFieldSystemGetter =
                this.FindAuraMonoMethodOnHierarchy(entitiesClass, "get_fieldSystem", 0);
            this.homelandFarmPrivacyGetFieldByOwnerIdMethod =
                this.FindAuraMonoMethodOnHierarchy(fieldSystemClass, "GetFieldByOwnerId", 1);
            this.homelandFarmPrivacyMethodsResolved = true;

            if (this.homelandFarmPrivacyEntitiesFieldSystemGetter == IntPtr.Zero
                || this.homelandFarmPrivacyGetFieldByOwnerIdMethod == IntPtr.Zero)
            {
                error = "Entities.get_fieldSystem / FieldComponentSystem.GetFieldByOwnerId(1) missing.";
                return false;
            }

            return true;
        }

        // The loop's long sleep, sliced so the cached verdict cannot go stale for a whole minute.
        // With the gate off this is one plain wait — the loop keeps its original cadence, and the
        // steady-growth rescan it was tuned to avoid stays avoided.
        private IEnumerator HomelandFarmAutoPrivacyAwareSleepRoutine(float seconds, bool inHomeland)
        {
            // Away the gate is inert (it cannot see the plot), so slicing there would wake the
            // coroutine twelve times an idle minute to learn nothing.
            if (!this.HomelandFarmPrivacyEnabled || !inHomeland)
            {
                yield return ModWait.Realtime(seconds);
                yield break;
            }

            float remaining = seconds;
            while (remaining > 0f)
            {
                float slice = Mathf.Min(remaining, HomelandFarmPrivacyPollSeconds);
                remaining -= slice;
                yield return ModWait.Realtime(slice);

                // Only the player poll runs here — none of the tick's scans. Breaking out early
                // returns to the loop top, which parks in the pause branch before doing any work.
                this.RefreshHomelandFarmPrivacyState(inHomeland);
                if (this.IsHomelandFarmPrivacyBlocking)
                {
                    yield break;
                }
            }
        }

        private void PersistHomelandFarmPrivacyRadius()
        {
            try
            {
                UnifiedConfigData data = this.LoadOrCreateUnifiedConfig();
                this.PopulateAllConfigSections(data);
                this.SaveUnifiedConfig(data);
                this.HomelandFarmLog("Saved privacy pause radius="
                    + this.homelandFarmPrivacyRadius.ToString("F0") + "m to config.");
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(HomelandFarmPrivacyTag, "failed to save privacy radius: " + ex.Message);
            }
        }
    }
}
