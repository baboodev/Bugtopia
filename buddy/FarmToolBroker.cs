using UnityEngine;

namespace HeartopiaMod
{
    // Single owner of the player-tool snapshot. Clients are Combined Farming and foraging
    // animations (Acquire("ForagingAnim")). The three standalone farms do not snapshot or restore.
    // Plan: docs/plans/2026-07-27-combined-farm-coordinator.md §3.2
    //
    // The problem it solves is NOT "who calls EquipHandTool" — with the coordinator suspending every
    // farm but one, only the active farm ever asks for a tool, and it does so through its own tested
    // equip path (rod/scanner/net each have their own confirmation reader and retry cadence). What
    // needs a single owner is the pair around that: the player-tool capture and restore.
    //
    // A client Acquires once. Tick captures once, after the deferral. Release replays that id only
    // when restoreTool is true and the captured id is non-zero. A stopped standalone farm does not
    // write the handhold. Foraging animations Acquire before taking the axe, Release with
    // restoreTool false when the take fails, and Release with restoreTool true when a run that
    // took the axe stops.
    //
    // The capture is DEFERRED by design. Reading the current tool goes through
    // TryGetCurrentToolInfo → a cold AuraMono ToolSystem resolve if the module is not warm yet, which
    // is the enumeration that native-AVs when it races the GC — a cold resolve on an enable frame.
    // Activation only arms the capture; the tick performs it once the deferral has elapsed, and
    // skips it entirely if the read fails.
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
                // A farm tool (rod, scanner, or net) is not a player tool. capturedToolId stays 0,
                // and Release leaves the handhold unchanged.
                capturedToolId = 0;
                Log("Player tool capture: farm tool " + toolId + " was equipped — nothing to restore.");
                return;
            }

            capturedToolId = toolId;
            Log("Player tool captured: " + toolId + (string.IsNullOrEmpty(toolName) ? string.Empty : "/" + toolName) + ".");
        }

        // restoreTool false drops the snapshot and does not equip or unequip. Combined Farming
        // passes false while a farm is still enabled: that farm re-equips later through its own
        // Ensure* path. Foraging animations pass false when the axe take fails, and true when a
        // run that took the axe stops. There is no per-farm snapshot to revive.
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

            // Clearing `active` BEFORE the restore is deliberate: a still-enabled farm may take
            // the handhold back on its next tick.
            if (restoreToolId != 0)
            {
                host.EquipHandTool(restoreToolId);
                FeatureLog.Life("FarmToolBroker", "handhold released — restored player tool " + restoreToolId);
                return;
            }

            // Nothing worth restoring, so leave the handhold as the last writer left it. A
            // still-enabled farm re-equips on its next tick. A stopped farm keeps the rod, net,
            // or scanner. Foraging's failed axe take lands here too. Do not unequip.
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
