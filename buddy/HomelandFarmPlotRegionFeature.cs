using System;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // Whole-homeland scan region — farm the player's OWN plot edge to edge instead of a sphere.
    //
    // ── WHY A RECTANGLE, NOT A BIGGER RADIUS ────────────────────────────────────────────────────
    // Every Homeland Farm operation (capture, auto farm, the manual radius buttons, the hotkeys)
    // selects its targets spatially: a sphere of Farm Radius metres around the player (around the
    // captured centre while auto farm runs). Raising that radius to "the whole map" would NOT give
    // "only my homeland": in shared towns the neighbours' plots sit a few metres away, and the
    // owner reads the sow path relies on are soft (a box whose ownerId has not resolved yet passes
    // as own). So the region in this mode is the game's own plot rectangle — FieldComponent
    // .minCorner/.maxCorner for Entities.fieldSystem.GetFieldByOwnerId(selfNetId), the rectangle
    // OutOfBoundsTesting validates every furniture placement against — which contains exactly the
    // player's planters and nothing of anyone else's. The resolve is shared with the privacy pause
    // (TryResolveHomelandFarmPlotForOwner, HomelandFarmPrivacyPauseFeature.cs).
    //
    // ── HOW IT PLUGS IN ─────────────────────────────────────────────────────────────────────────
    // The scan funnel keeps its centre+radius plumbing: in this mode the centre is the plot's world
    // centre and the radius is the plot's bounding radius, so every spatial source still runs and
    // still logs a radius. The precise cut is one predicate, HomelandFarmIsOutsideScanRegion,
    // which every former "sqrMagnitude > radiusSq" test now calls: a sphere test with the mode
    // off, a rectangle test (in the plot's local frame, rotation-correct) with it on. A target
    // whose position cannot be resolved is treated exactly as before (kept, then re-checked by
    // the per-action gates).
    //
    // ── WHEN THE PLOT IS NOT HERE ───────────────────────────────────────────────────────────────
    // GetFieldByOwnerId(self) returns null in a level that does not contain the player's plot
    // (visiting a friend's homeland, the wild). With the mode on, an action is then BLOCKED with a
    // notification that names the mode — it must never silently fall back to a sphere around the
    // player on someone else's field. The auto loop, once captured, keeps the last resolved
    // rectangle (plain values, no pointers) and re-resolves whenever it is back on the field.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // A planter's pivot sits inside the rectangle by at least half its footprint, so the only
        // slack needed is for float error between the position sources. Half a metre keeps a
        // neighbour's box on the far side of a shared boundary out.
        private const float HomelandFarmPlotEdgeMarginMeters = 0.5f;

        // Padding on the bounding radius handed to the funnel, mirroring the +2 m the sphere scans
        // add before their own precise cut.
        private const float HomelandFarmPlotBoundingPaddingMeters = 2f;

        // Capture needs the planters LOADED, and ECS entities stream around the player, not the
        // plot. Standing on the plot (distance 0) or right beside it is required; farther than this
        // from the nearest edge the capture would just report zero planters.
        private const float HomelandFarmPlotCaptureMaxEdgeDistanceMeters = 40f;

        // The auto loop re-resolves the rectangle at this cadence while on the field (two invokes
        // and three member reads — cheap), so a plot rebuilt by the game is picked up.
        private const float HomelandFarmPlotRegionAutoRefreshSeconds = 30f;

        // Lazy resolve from the Farm Radius value label after a restart, while nothing else runs.
        private const float HomelandFarmPlotRegionUiRetrySeconds = 5f;

        // Persisted toggle. Off = the classic Farm Radius sphere, byte-for-byte the old behaviour.
        private bool homelandFarmWholePlotEnabled;

        // Last resolved own plot. Values only — matrices and corners — never a mono pointer.
        private struct HomelandFarmPlotRegion
        {
            public HomelandFarmPrivacyPlot Plot;
            public Vector3 WorldCenter;
            public float BoundingRadius;
            public float Width;
            public float Depth;
            public uint OwnerNetId;
        }

        private HomelandFarmPlotRegion homelandFarmPlotRegion;
        private bool homelandFarmPlotRegionValid;
        private float homelandFarmPlotRegionResolvedAt = -999f;
        private float homelandFarmPlotRegionAttemptedAt = -999f;

        // The mode is active only once the plot has actually been resolved; until then every
        // spatial path keeps behaving as the sphere so a resolve miss can never widen the region.
        private bool HomelandFarmPlotModeActive => this.homelandFarmWholePlotEnabled && this.homelandFarmPlotRegionValid;

        // Radius every scan path reads instead of the slider: the plot's bounding radius in plot
        // mode (so the funnel's coarse sphere covers the whole rectangle), the slider otherwise.
        private float HomelandFarmScanRadius => this.HomelandFarmPlotModeActive
            ? this.homelandFarmPlotRegion.BoundingRadius
            : this.homelandFarmWaterRadius;

        // Centre every radius scan is anchored on: the plot centre in plot mode (the player may be
        // anywhere on the plot), the player otherwise.
        private bool TryGetHomelandFarmRegionCenter(out Vector3 center)
        {
            if (this.HomelandFarmPlotModeActive)
            {
                center = this.homelandFarmPlotRegion.WorldCenter;
                return true;
            }

            return this.TryGetHomelandFarmPlayerPosition(out center);
        }

        // THE spatial cut. `center`/`radiusSq` are the sphere the caller would have tested; they
        // are ignored in plot mode, where the rectangle decides.
        private bool HomelandFarmIsOutsideScanRegion(Vector3 position, Vector3 center, float radiusSq)
        {
            if (this.HomelandFarmPlotModeActive)
            {
                return this.HomelandFarmPlotEdgeDistance(position) > HomelandFarmPlotEdgeMarginMeters;
            }

            return (position - center).sqrMagnitude > radiusSq;
        }

        // Metres from a world position to the nearest edge of the resolved plot; 0 inside.
        private float HomelandFarmPlotEdgeDistance(Vector3 worldPos)
        {
            return HomelandFarmPrivacyPlotDistance(this.homelandFarmPlotRegion.Plot, worldPos);
        }

        // Resolve the own plot now. Keeps the previous rectangle on a miss (the plot does not move;
        // a miss means the field is not loaded here) and reports the miss in `error`.
        private bool TryRefreshHomelandFarmPlotRegion(out string error)
        {
            error = string.Empty;
            this.homelandFarmPlotRegionAttemptedAt = Time.realtimeSinceStartup;
            if (!this.TryResolveSelfPlayerNetId(out uint selfNetId) || selfNetId == 0U)
            {
                error = "self player netId unavailable.";
                return false;
            }

            if (!this.TryResolveHomelandFarmPlotForOwner(selfNetId, out HomelandFarmPrivacyPlot plot, out error))
            {
                return false;
            }

            Vector3 localCenter = new Vector3(
                (plot.Min.x + plot.Max.x) * 0.5f,
                (plot.Min.y + plot.Max.y) * 0.5f,
                (plot.Min.z + plot.Max.z) * 0.5f);
            Vector3 worldCenter = plot.LocalToWorld.MultiplyPoint3x4(localCenter);
            if (worldCenter == Vector3.zero)
            {
                // Vector3.zero is the funnel's "no centre" sentinel; a plot centred exactly on the
                // origin would otherwise disable every spatial source.
                worldCenter = new Vector3(0f, 0.001f, 0f);
            }

            float width = plot.Max.x - plot.Min.x;
            float depth = plot.Max.z - plot.Min.z;
            float halfDiagonal = 0.5f * Mathf.Sqrt((width * width) + (depth * depth));

            bool changed = !this.homelandFarmPlotRegionValid
                || this.homelandFarmPlotRegion.OwnerNetId != selfNetId
                || (this.homelandFarmPlotRegion.WorldCenter - worldCenter).sqrMagnitude > 0.01f
                || Mathf.Abs(this.homelandFarmPlotRegion.Width - width) > 0.05f
                || Mathf.Abs(this.homelandFarmPlotRegion.Depth - depth) > 0.05f;

            this.homelandFarmPlotRegion = new HomelandFarmPlotRegion
            {
                Plot = plot,
                WorldCenter = worldCenter,
                BoundingRadius = halfDiagonal + HomelandFarmPlotBoundingPaddingMeters,
                Width = width,
                Depth = depth,
                OwnerNetId = selfNetId,
            };
            this.homelandFarmPlotRegionValid = true;
            this.homelandFarmPlotRegionResolvedAt = Time.realtimeSinceStartup;

            if (changed)
            {
                this.HomelandFarmLog("Whole homeland: own plot resolved owner=" + selfNetId
                    + " size=" + width.ToString("F1") + "x" + depth.ToString("F1") + "m"
                    + " center=" + worldCenter
                    + " boundingRadius=" + this.homelandFarmPlotRegion.BoundingRadius.ToString("F1") + "m.");
            }

            return true;
        }

        // Action gate shared by TryBeginHomelandFarmAction and the water+weed hotkey: with the mode
        // on, the own plot must resolve in THIS level or the action is refused (never a silent
        // sphere fallback on someone else's field). With the mode off this is a no-op.
        private bool TryHomelandFarmEnsurePlotRegionForAction(out string blockReason)
        {
            blockReason = string.Empty;
            if (!this.homelandFarmWholePlotEnabled)
            {
                return true;
            }

            if (this.TryRefreshHomelandFarmPlotRegion(out string error))
            {
                return true;
            }

            blockReason = "homeland_farm.whole_plot_unavailable";
            this.HomelandFarmLog("Whole homeland: own plot unavailable here (" + error + ").");
            return false;
        }

        // Capture in plot mode also needs the player ON (or right beside) the plot — see the
        // constant. Returns the edge distance for the notification.
        private bool TryHomelandFarmIsPlayerNearOwnPlotForCapture(out float edgeDistance)
        {
            edgeDistance = -1f;
            if (!this.HomelandFarmPlotModeActive || !this.TryGetHomelandFarmPlayerPosition(out Vector3 playerPos))
            {
                return true;
            }

            edgeDistance = this.HomelandFarmPlotEdgeDistance(playerPos);
            return edgeDistance <= HomelandFarmPlotCaptureMaxEdgeDistanceMeters;
        }

        // Auto-loop refresh while on the field, throttled (attempts too, so a field that will not
        // resolve is not probed every one-second weeding tick). A changed centre is pushed into the
        // captured centre/override so the loop's scans follow the plot, not a stale snapshot.
        private void HomelandFarmAutoRefreshPlotRegion()
        {
            if (!this.homelandFarmWholePlotEnabled)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now - this.homelandFarmPlotRegionResolvedAt < HomelandFarmPlotRegionAutoRefreshSeconds
                || now - this.homelandFarmPlotRegionAttemptedAt < HomelandFarmPlotRegionAutoRefreshSeconds)
            {
                return;
            }

            if (!this.TryRefreshHomelandFarmPlotRegion(out string error))
            {
                FeatureLog.Fail(HomelandFarmTag, "Whole homeland: plot re-resolve failed, keeping the captured rectangle: " + error);
                return;
            }

            if (this.homelandFarmAutoCaptured
                && (this.homelandFarmAutoCenter - this.homelandFarmPlotRegion.WorldCenter).sqrMagnitude > 0.01f)
            {
                this.homelandFarmAutoCenter = this.homelandFarmPlotRegion.WorldCenter;
                this.homelandFarmAutoCaptureRadius = this.homelandFarmPlotRegion.BoundingRadius;
                if (this.homelandFarmScanCenterOverride.HasValue)
                {
                    this.homelandFarmScanCenterOverride = this.homelandFarmAutoCenter;
                }
            }
        }

        // Value label for the Farm Radius card while the mode is on. Also the UI's one chance to
        // resolve the plot lazily after a restart (the toggle is persisted, the rectangle is not):
        // throttled, only after the farm warm-up (world-ready) and never while a farm coroutine
        // is mid-pass, so it adds no Aura call next to one already running.
        private string HomelandFarmPlotRegionStatusText()
        {
            if (!this.homelandFarmPlotRegionValid
                && this.IsHomelandFarmWarmupReady()
                && this.homelandFarmCoroutine == null
                && this.homelandFarmHotkeyCoroutine == null
                && Time.realtimeSinceStartup - this.homelandFarmPlotRegionAttemptedAt >= HomelandFarmPlotRegionUiRetrySeconds)
            {
                this.TryRefreshHomelandFarmPlotRegion(out _);
            }

            if (this.homelandFarmPlotRegionValid)
            {
                return this.LF("homeland_farm.whole_plot_size",
                    this.homelandFarmPlotRegion.Width.ToString("F0"),
                    this.homelandFarmPlotRegion.Depth.ToString("F0"));
            }

            return this.L("homeland_farm.whole_plot_pending");
        }

        private void OnHomelandFarmWholePlotToggled(bool enabled)
        {
            if (this.homelandFarmWholePlotEnabled == enabled)
            {
                return;
            }

            this.homelandFarmWholePlotEnabled = enabled;
            this.HomelandFarmLog("Whole homeland " + (enabled ? "enabled" : "disabled") + ".");
            if (enabled && this.IsHomelandFarmWarmupReady())
            {
                // Resolve eagerly so the label and the first action already have the rectangle
                // when the player is at home; a miss here is informational, the action gate retries.
                this.TryRefreshHomelandFarmPlotRegion(out _);
            }

            // A capture made under the other region (a sphere around where the player stood, or the
            // plot) no longer describes the zone the loop would farm; ask for a fresh one. A RUNNING
            // auto farm is left alone — it picks the new region up on its next on-field scan.
            if (this.homelandFarmAutoCaptured && !this.homelandFarmAutoRunning)
            {
                this.homelandFarmAutoCaptured = false;
                this.homelandFarmAutoPlanterCount = 0;
                this.homelandFarmLastStatus = "homeland_farm.whole_plot_recapture";
                this.AddMenuNotification(this.L("homeland_farm.whole_plot_recapture"), new Color(1f, 0.85f, 0.4f));
            }

            this.PersistHomelandFarmWholePlotSetting();
        }

        private void PersistHomelandFarmWholePlotSetting()
        {
            try
            {
                UnifiedConfigData data = this.LoadOrCreateUnifiedConfig();
                this.PopulateAllConfigSections(data);
                this.SaveUnifiedConfig(data);
                this.HomelandFarmLog("Saved whole homeland=" + this.homelandFarmWholePlotEnabled + " to config.");
            }
            catch (Exception ex)
            {
                this.HomelandFarmLog("Failed to save whole homeland setting: " + ex.Message);
            }
        }
    }
}
