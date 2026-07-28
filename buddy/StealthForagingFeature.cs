using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Stealth Foraging — a Foraging-tab companion mode for the Aura Farm (toggle lives in
    // Resource Gathering -> Foraging -> SETTINGS, persisted).
    //
    // While the toggle is on AND the farm is actually RUNNING ("engaged", StealthForagingActive):
    //
    //   1. Noclip is force-enabled. The farm parks the player BELOW the node, which nothing but
    //      the noclip hover can hold: ProcessNoclipMovementOnUpdate drives the game's own
    //      PlayerMoveComponent every frame (collision sweep disabled, gravity zeroed), so the
    //      player stays where the hop put them instead of falling / being pushed out of the
    //      terrain. Only the RUNTIME field is written (noclipEnabled is not persisted), and only
    //      when it was off — a noclip the user already had on survives the farm stop untouched.
    //
    //   2. The client-side out-of-bounds rescue is suppressed for the duration of the run. The
    //      OOB detect box has a FLOOR as well as a ceiling (GameSceneConfig.detectMin, e.g.
    //      StarTown y>=9), so an under-terrain hop can trip the watchdog and get warped to a safe
    //      point mid-collect. This borrows the existing OutOfBoundsGuardFeature hooks through
    //      IsOutOfBoundsGuardRequested() instead of writing disableOobTeleportEnabled — that flag
    //      IS persisted, and a temporary write could leak into Config.xml via any unrelated
    //      SaveKeybinds during the run.
    //
    //   3. Every RESOURCE-NODE hop lands below the node instead of on it: contamination -5m,
    //      every other resource -1.5m (ApplyForagingNodeTeleportOffset, HeartopiaComplete.Farm.cs).
    //      WORLD-LOAD CHECKPOINTS keep their exact Y — the farm-location waypoints
    //      (MovingToLocation -> LoadingArea), the priority-area anchors (WaitingForPriorityArea)
    //      and the cleansing-coral hop are area arrivals that must stream the world in normally,
    //      not resource arrivals.
    //
    // The offsets apply to the TELEPORT ARGUMENT ONLY. lastNodePosition and every marker /
    // cooldown / dwell check keep the true node position, so radar matching and the collect
    // confirmation behave exactly as they do with the mode off.
    //
    // Everything here is scalar state on the main thread — no AuraMono pointers, no coroutines.
    public partial class HeartopiaComplete
    {
        // Persisted toggle. OFF = vanilla farm hops (default).
        private bool stealthForagingEnabled;

        // Did WE turn noclip on for this run? Only then is it turned back off, and only while it
        // is still on (Disable All / a manual toggle mid-run wins).
        private bool stealthForagingForcedNoclip;

        // Edge tracker for the noclip force/restore only. The Y offsets and the OOB request read
        // StealthForagingActive directly, so they are live the instant ToggleAutoFarm flips
        // autoFarmActive — a hop in the same frame is already stealthed.
        private bool stealthForagingNoclipEngaged;

        // Dive depth below the resource, in metres. Contamination sits on the sea floor and is
        // worked by the sea-clean sweep from further out, so it dives deeper than land nodes.
        private const float StealthForagingContaminationDepth = 5f;
        private const float StealthForagingNodeDepth = 1.5f;

        // Stealth is only ever "on" while the farm is running: a configured-but-idle farm must
        // change nothing about noclip, the OOB guard or manual teleports.
        private bool StealthForagingActive => this.stealthForagingEnabled && this.autoFarmActive;

        // Called every frame from OnUpdate, before the OOB guard and the noclip mover. Two bool
        // compares in the steady state.
        private void ProcessStealthForagingOnUpdate()
        {
            bool active = this.StealthForagingActive;
            if (active == this.stealthForagingNoclipEngaged)
            {
                return;
            }

            this.stealthForagingNoclipEngaged = active;
            if (active)
            {
                this.EngageStealthForagingNoclip();
            }
            else
            {
                this.RestoreStealthForagingNoclip();
            }
        }

        // Same edge init the Self-tab toggle performs (UguiSelfContent.OnUguiSelfNoclipToggled);
        // the steady state is owned by ProcessNoclipMovementOnUpdate.
        private void EngageStealthForagingNoclip()
        {
            if (!this.noclipEnabled)
            {
                this.noclipEnabled = true;
                this.stealthForagingForcedNoclip = true;
                this.InitializeNoclipDriveState();
            }

            ModLogger.Msg("[StealthForaging] Engaged (noclip="
                + (this.stealthForagingForcedNoclip ? "forced" : "already on")
                + ", OOB rescue suppressed, node hops dive -"
                + StealthForagingNodeDepth.ToString("0.#") + "m / contamination -"
                + StealthForagingContaminationDepth.ToString("0.#") + "m).");
        }

        private void RestoreStealthForagingNoclip()
        {
            bool restored = false;
            if (this.stealthForagingForcedNoclip && this.noclipEnabled)
            {
                this.noclipEnabled = false;
                this.ClearNoclipVehicleOverride();
                restored = true;
            }

            this.stealthForagingForcedNoclip = false;
            ModLogger.Msg("[StealthForaging] Disengaged (noclip="
                + (restored ? "restored off" : "left as the user set it") + ").");
        }

        // Pins the noclip hover at the dive target. TeleportToLocation clears the hold so it
        // re-seeds from the live player, which is right for a normal warp but loses the dive if
        // the game's own transfer snaps the arrival to the surface — this makes the farm hop
        // deterministic. No-op unless stealth is engaged and the foot hover is the active path.
        private void PinStealthForagingNoclipHold(Vector3 position)
        {
            if (!this.StealthForagingActive || !this.noclipEnabled)
            {
                return;
            }

            GameObject player = GetPlayer();
            if (player != null)
            {
                this.noclipFootHoldRotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
            }

            this.noclipFootHoldPosition = position;
            this.noclipFootHoldValid = true;
        }
    }
}
