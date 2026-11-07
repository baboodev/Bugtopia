using UnityEngine;

namespace HeartopiaMod
{
    // Single owner of the handhold slot while Combined Farming is active.
    // Plan: docs/plans/2026-07-27-combined-farm-coordinator.md §3.2
    //
    // The problem it solves is NOT "who calls EquipHandTool" — with the coordinator suspending every
    // farm but one, only the active farm ever asks for a tool, and it does so through its own tested
    // equip path (rod/scanner/net each have their own confirmation reader and retry cadence). What
    // needs a single owner is the pair around that: the "previous tool" capture and restore.
    //
    // Standalone farms leave the held tool unchanged when stopped. The broker owns the optional
    // player-tool snapshot for coordinated runs: taken once on acquire and restored on release
    // only when requested.
    //
    // The capture is DEFERRED by design. Reading the current tool goes through
    // TryGetCurrentToolInfo → a cold AuraMono ToolSystem resolve if the module is not warm yet, which
    // is the enumeration that native-AVs when it races the GC — the crash that made BirdNetFarm stop
    // capturing on its enable frame. So activation only arms the capture; the tick performs it once
    // the deferral has elapsed, and skips it entirely if the read fails.
    public static class FarmToolBroker
    {
        private const float CaptureDeferralSeconds = 0.75f;

        // Tools the coordinator itself rotates through — capturing one of these would just restore a
        // farm's own tool later. Only a tool the PLAYER had out is worth putting back.
        private static bool IsFarmTool(int toolId)
        {
            return toolId == CombinedFarmFeature.ToolIdRod
                || toolId == CombinedFarmFeature.ToolIdBirdScanner
                || toolId == CombinedFarmFeature.ToolIdNet;
        }

        private static bool active;
        private static string owner = string.Empty;
        private static bool captureArmed;
        private static float captureAt = -999f;
        private static bool captureDone;
        private static int capturedToolId;

        public static bool IsActive => active;
        public static string Owner => owner;
        public static int CapturedToolId => capturedToolId;

        public static void Acquire(string ownerName)
        {
            if (active)
            {
                return;
            }

            active = true;
            owner = ownerName ?? string.Empty;
            captureArmed = true;
            captureDone = false;
            capturedToolId = 0;
            captureAt = Time.unscaledTime + CaptureDeferralSeconds;
            FeatureLog.Life("FarmToolBroker", "handhold acquired by " + owner + " — player tool capture armed");
        }

        // Called every coordinator tick while active. Cheap after the one capture has happened.
        public static void Tick(HeartopiaComplete host)
        {
            if (!active || !captureArmed || host == null || Time.unscaledTime < captureAt)
            {
                return;
            }

            captureArmed = false;
            captureDone = true;

            if (!host.TryGetCurrentToolInfo(out int toolId, out string toolName, out string status))
            {
                capturedToolId = 0;
                Log("Player tool capture skipped — tool state unreadable (" + status + ").");
                return;
            }

            if (IsFarmTool(toolId))
            {
                // A farm tool was already out (the farms were enabled before the coordinator took
                // over). Restoring it later would be meaningless, so leave the held tool unchanged
                // on release.
                capturedToolId = 0;
                Log("Player tool capture: farm tool " + toolId + " was equipped — nothing to restore.");
                return;
            }

            capturedToolId = toolId;
            Log("Player tool captured: " + toolId + (string.IsNullOrEmpty(toolName) ? string.Empty : "/" + toolName) + ".");
        }

        // restoreTool=false when a farm is STILL enabled after the coordinator lets go: that farm is
        // about to equip its own tool on its next tick, so putting the player's tool back first would
        // only add a round-trip of churn. The capture is dropped either way — the coordinator is no
        // longer the owner.
        public static void Release(HeartopiaComplete host, bool restoreTool)
        {
            if (!active)
            {
                return;
            }

            int restoreToolId = restoreTool ? capturedToolId : 0;
            bool hadCapture = captureDone;
            active = false;
            owner = string.Empty;
            captureArmed = false;
            captureDone = false;
            captureAt = -999f;
            capturedToolId = 0;

            if (host == null)
            {
                return;
            }

            // Clearing `active` BEFORE the restore is deliberate: any farm still enabled is free to
            // take the handhold back on its next tick.
            if (restoreToolId != 0)
            {
                host.EquipHandTool(restoreToolId);
                FeatureLog.Life("FarmToolBroker", "handhold released — restored player tool " + restoreToolId);
                return;
            }

            // Nothing worth restoring. Leave whatever the last slice held; any farm that is still
            // enabled will re-equip what it needs.
            FeatureLog.Life("FarmToolBroker", "handhold released — no player tool to restore" + (hadCapture ? string.Empty : " (capture never ran)"));
        }

        private static void Log(string message)
        {
            if (!HeartopiaComplete.MasterLogCombinedFarm)
            {
                return;
            }

            ModLogger.Msg("[FarmToolBroker] " + message);
        }
    }
}
