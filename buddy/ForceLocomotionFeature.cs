using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Force Swim / Skate on land.
    //
    // Locomotion is a client FSM (PlayerFsMachine on LocalPlayerComponent). The Free<->Swim and
    // Free<->Skate transitions are gated purely by two status fields:
    //   * PlayerStateSwim.IsStateSatisfy()  => Status.SwimStatus.IsSwimming
    //   * PlayerStateSkate.IsStateSatisfy() => Status.SkateStatus.Enable (Mode != None)
    // (see ilspy-dumps/.../Component/Player/PlayerStateSwim.cs, PlayerStateSkate.cs, *Transition*.cs)
    //
    // SWIM is the server-going variant: written through SwimStatus.set_IsSwimming (raises DirtyMask)
    // + LocalPlayerComponent.ForceSyncStatus() so the server and other players see it.
    //
    // SKATE is server-authoritative: FsmStatus.Mode=Skate (set by the game when the FSM enters
    // PlayerStateSkate) replicates, and WITHOUT a server-sanctioned skate session the server reverts
    // it (~1 Hz vs our reassert => the flicker). Diagnostics proved the server ACCEPTS
    // SkateProtocolManager.SendStartSkateCommand even on land (SkateStartedEvent isSucceed=True). So we
    // mirror the game's own SkateCommand flow: on enable send Start and only write SkateStatus.mode=Free
    // once SkateStartedEvent(isSucceed) confirms the session; on disable write mode=None so
    // GameSkateMode.OnModeFinish sends the paired End. No native detour (hooking SendExitSkateCommand
    // crashed at world-teardown — coreclr heap corruption).
    public partial class HeartopiaComplete
    {
        private const string ForceSwimStatusFullName = "XDTLevelAndEntity.Gameplay.Component.Player.SwimStatus";
        private const string ForceSkateStatusFullName = "XDTLevelAndEntity.Gameplay.Component.Player.SkateStatus";
        private const string ForceLocomotionStatusNamespace = "XDTLevelAndEntity.Gameplay.Component.Player";
        private const int ForceSkateModeNone = 0; // SkateMode.None
        private const int ForceSkateModeFree = 1; // SkateMode.Free
        private const float ForceLocomotionReassertInterval = 1f;

        private bool forceSwimEnabled;
        private bool forceSkateEnabled;
        private bool forceLocomotionPrevSwim;
        private bool forceLocomotionPrevSkate;
        private string forceLocomotionLastStatus = "Idle.";
        private float forceLocomotionNextReassertAt = -999f;
        private FeatureBreakerState forceLocomotionBreaker;

        // Image-lifetime AuraMono handles (class/method pointers are safe to keep raw).
        private IntPtr forceLocomotionSwimStatusClass;
        private IntPtr forceLocomotionSkateStatusClass;
        private IntPtr forceLocomotionSwimSetMethod;       // SwimStatus.set_IsSwimming(bool)
        private IntPtr forceLocomotionForceSyncMethod;     // LocalPlayerComponent.ForceSyncStatus()
        private IntPtr forceLocomotionForceSyncClass;
        private IntPtr forceLocomotionStatusClassResolvedFor;
        private int forceLocomotionSwimFieldOffset = -1;   // offset of SwimStatus field in PlayerSyncStatus
        private int forceLocomotionSkateFieldOffset = -1;  // offset of SkateStatus field in PlayerSyncStatus
        private int forceLocomotionSkateModeInnerOffset = -1; // offset of SkateStatus.mode inside the struct

        // Skate server-session state. Enter skate only once the server confirms our Start.
        private bool forceSkateAwaitingStart;     // Start sent, waiting for SkateStartedEvent
        private bool forceSkateServerSanctioned;  // server confirmed session -> safe to hold mode=Free

        // GameSkateMode exits ~1 Hz on non-ice land via its ground-timeout (CheckExitCondition:
        // _leaveSkateGroundTime + _skateConfig.ExitTime). We pin _leaveSkateGroundTime=-1 each frame so
        // that exit never fires. Field ptr cached per class (image lifetime).
        private IntPtr forceSkateGroundFieldClass;
        private IntPtr forceSkateLeaveGroundField;

        private void ProcessForceLocomotionOnUpdate()
        {
            this.EnsureForceSkateHooksRegistered();
            this.ForceSkateHoldGround();

            float now = Time.unscaledTime;

            bool togglesChanged = this.forceSwimEnabled != this.forceLocomotionPrevSwim
                || this.forceSkateEnabled != this.forceLocomotionPrevSkate;

            if (!togglesChanged && !this.forceSwimEnabled && !this.forceSkateEnabled)
            {
                return;
            }

            if (!togglesChanged && now < this.forceLocomotionNextReassertAt)
            {
                return;
            }

            if (!this.forceLocomotionBreaker.ShouldRun(now))
            {
                return;
            }

            bool skateRising = this.forceSkateEnabled && !this.forceLocomotionPrevSkate;
            bool skateFalling = !this.forceSkateEnabled && this.forceLocomotionPrevSkate;

            // ENABLE: open a server-sanctioned skate session. We DON'T enter skate (write mode=Free)
            // until SkateStartedEvent confirms — otherwise the server reverts the unsanctioned skate
            // (the flicker). The reply is handled in OnForceSkateStartedEvent.
            if (skateRising)
            {
                this.forceSkateServerSanctioned = false;
                this.forceSkateAwaitingStart = true;
                try
                {
                    if (this.EnsureIceSkatingSequenceSendResolver(out _))
                    {
                        this.TrySendIceSkatingStart(out string startStatus);
                        if (ForceSkateDiagLogs)
                        {
                            ModLogger.Msg("[ForceSkateDiag] ENABLE: Start sent, awaiting server (" + startStatus + ")");
                        }
                    }
                }
                catch
                {
                }
            }
            else if (skateFalling)
            {
                // If Start was sent but we never entered skate (reply pending / refused), close the
                // orphan session ourselves — GameSkateMode.OnModeFinish only sends End if it activated.
                if (this.forceSkateAwaitingStart && !this.forceSkateServerSanctioned)
                {
                    try
                    {
                        if (this.EnsureIceSkatingSequenceSendResolver(out _))
                        {
                            this.TrySendIceSkatingEnd(out _);
                        }
                    }
                    catch
                    {
                    }
                }

                this.forceSkateAwaitingStart = false;
                this.forceSkateServerSanctioned = false;
                if (ForceSkateDiagLogs)
                {
                    ModLogger.Msg("[ForceSkateDiag] DISABLE: mode=None (game sends paired End if it was skating)");
                }
            }

            this.forceLocomotionPrevSwim = this.forceSwimEnabled;
            this.forceLocomotionPrevSkate = this.forceSkateEnabled;
            this.forceLocomotionNextReassertAt = now + ForceLocomotionReassertInterval;

            try
            {
                this.ApplyForceLocomotion();
                this.forceLocomotionBreaker.Success();
            }
            catch (Exception ex)
            {
                this.forceLocomotionBreaker.Failure("ForceLocomotion", ex, now);
                this.forceLocomotionLastStatus = "Error: " + ex.Message;
            }
        }

        private unsafe void ApplyForceLocomotion()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                this.forceLocomotionLastStatus = "AuraMono not ready.";
                return;
            }

            if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero)
            {
                this.forceLocomotionLastStatus = "Player not found (enter world).";
                return;
            }

            if (!this.TryGetMonoObjectMember(playerObj, "Status", out IntPtr statusObj) || statusObj == IntPtr.Zero)
            {
                this.forceLocomotionLastStatus = "Status unavailable.";
                return;
            }

            if (!this.EnsureForceLocomotionResolved(playerObj, statusObj, out string resolveDetail))
            {
                this.forceLocomotionLastStatus = "Resolve failed (" + resolveDetail + ").";
                return;
            }

            // Pin the heap objects: set_IsSwimming / set_Mode receive an interior pointer into the
            // Status object, and ForceSyncStatus may allocate (moving SGen GC could relocate them).
            uint statusPin = AuraMonoPinNew(statusObj);
            uint playerPin = AuraMonoPinNew(playerObj);
            try
            {
                bool swimOk = this.forceLocomotionSwimSetMethod != IntPtr.Zero
                    && this.forceLocomotionSwimFieldOffset >= 0
                    && this.TryForceLocomotionSetStructBool(
                        statusObj,
                        this.forceLocomotionSwimFieldOffset,
                        this.forceLocomotionSwimSetMethod,
                        this.forceSwimEnabled);

                // Enter skate (mode=Free) ONLY once the server has sanctioned the session; otherwise
                // hold None so we don't fight the server. Raw write (no DirtyMask); FsmStatus.Mode=Skate
                // replicates to others and is now accepted because a real session is open.
                bool skateActive = this.forceSkateEnabled && this.forceSkateServerSanctioned;
                bool skateOk = this.forceLocomotionSkateFieldOffset >= 0
                    && this.forceLocomotionSkateModeInnerOffset >= 0
                    && this.TryForceLocomotionSetSkateModeRaw(
                        statusObj,
                        skateActive ? ForceSkateModeFree : ForceSkateModeNone);

                // Pushes only the swim field (skate stays undirty / client-only).
                bool synced = this.TryForceLocomotionSyncStatus(playerObj);

                this.forceLocomotionLastStatus =
                    "swim=" + this.forceSwimEnabled + (swimOk ? "" : "(set-fail)")
                    + " skate=" + this.forceSkateEnabled + (skateOk ? "" : "(set-fail)")
                    + (synced ? " synced." : " sync-fail.");
            }
            finally
            {
                AuraMonoPinFree(statusPin);
                AuraMonoPinFree(playerPin);
            }
        }

        private bool EnsureForceLocomotionResolved(IntPtr playerObj, IntPtr statusObj, out string detail)
        {
            detail = string.Empty;

            if (this.forceLocomotionSwimStatusClass == IntPtr.Zero)
            {
                this.forceLocomotionSwimStatusClass = this.FindAuraMonoClassByFullName(ForceSwimStatusFullName);
                if (this.forceLocomotionSwimStatusClass == IntPtr.Zero)
                {
                    this.forceLocomotionSwimStatusClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        ForceLocomotionStatusNamespace, "SwimStatus");
                }
            }

            if (this.forceLocomotionSkateStatusClass == IntPtr.Zero)
            {
                this.forceLocomotionSkateStatusClass = this.FindAuraMonoClassByFullName(ForceSkateStatusFullName);
                if (this.forceLocomotionSkateStatusClass == IntPtr.Zero)
                {
                    this.forceLocomotionSkateStatusClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        ForceLocomotionStatusNamespace, "SkateStatus");
                }
            }

            if (this.forceLocomotionSwimSetMethod == IntPtr.Zero && this.forceLocomotionSwimStatusClass != IntPtr.Zero)
            {
                this.forceLocomotionSwimSetMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.forceLocomotionSwimStatusClass, "set_IsSwimming", 1);
            }

            // SkateStatus is written CLIENT-ONLY (raw `mode` field, no DirtyMask) so it never syncs to
            // the server: skate-on-land has no server session, and any synced Mode=Free is rejected /
            // corrected (constant flicker). The raw write keeps the FSM (Free->Skate) satisfied locally
            // without the server fighting it. mono_field_get_offset includes the boxed 2-ptr header, so
            // the inline offset inside the struct subtracts it (cf. AuraFarm SelectPriorityInfo.shape).
            if (this.forceLocomotionSkateModeInnerOffset < 0
                && this.forceLocomotionSkateStatusClass != IntPtr.Zero
                && auraMonoFieldGetOffset != null)
            {
                IntPtr modeField = this.FindAuraMonoFieldOnHierarchy(this.forceLocomotionSkateStatusClass, "mode");
                if (modeField != IntPtr.Zero)
                {
                    this.forceLocomotionSkateModeInnerOffset = (int)auraMonoFieldGetOffset(modeField) - 2 * IntPtr.Size;
                }
            }

            IntPtr playerClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(playerObj) : IntPtr.Zero;
            if (this.forceLocomotionForceSyncMethod == IntPtr.Zero || this.forceLocomotionForceSyncClass != playerClass)
            {
                this.forceLocomotionForceSyncClass = playerClass;
                this.forceLocomotionForceSyncMethod = playerClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(playerClass, "ForceSyncStatus", 0)
                    : IntPtr.Zero;
            }

            // Field offsets depend on the concrete PlayerSyncStatus class; recompute if it changes.
            IntPtr statusClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(statusObj) : IntPtr.Zero;
            if (statusClass != IntPtr.Zero
                && (this.forceLocomotionStatusClassResolvedFor != statusClass
                    || this.forceLocomotionSwimFieldOffset < 0
                    || this.forceLocomotionSkateFieldOffset < 0))
            {
                IntPtr swimField = this.FindAuraMonoFieldOnHierarchy(statusClass, "SwimStatus");
                IntPtr skateField = this.FindAuraMonoFieldOnHierarchy(statusClass, "SkateStatus");
                if (swimField != IntPtr.Zero && skateField != IntPtr.Zero && auraMonoFieldGetOffset != null)
                {
                    // mono_field_get_offset is measured from the MonoObject* (includes the header),
                    // so statusObj + offset points straight at the struct field — no subtraction here
                    // (unlike inline/boxed array elements, cf. AuraFarm SelectPriorityInfo.shape).
                    this.forceLocomotionSwimFieldOffset = (int)auraMonoFieldGetOffset(swimField);
                    this.forceLocomotionSkateFieldOffset = (int)auraMonoFieldGetOffset(skateField);
                    this.forceLocomotionStatusClassResolvedFor = statusClass;
                }
            }

            bool swimReady = this.forceLocomotionSwimSetMethod != IntPtr.Zero && this.forceLocomotionSwimFieldOffset >= 0;
            bool skateReady = this.forceLocomotionSkateFieldOffset >= 0 && this.forceLocomotionSkateModeInnerOffset >= 0;
            if (swimReady && skateReady && this.forceLocomotionForceSyncMethod != IntPtr.Zero)
            {
                return true;
            }

            detail = "swimClass=" + (this.forceLocomotionSwimStatusClass != IntPtr.Zero)
                + " swimSet=" + (this.forceLocomotionSwimSetMethod != IntPtr.Zero)
                + " swimOff=" + this.forceLocomotionSwimFieldOffset
                + " skateClass=" + (this.forceLocomotionSkateStatusClass != IntPtr.Zero)
                + " skateOff=" + this.forceLocomotionSkateFieldOffset
                + " skateModeOff=" + this.forceLocomotionSkateModeInnerOffset
                + " forceSync=" + (this.forceLocomotionForceSyncMethod != IntPtr.Zero);
            return false;
        }

        private unsafe bool TryForceLocomotionSetStructBool(IntPtr statusObj, int fieldOffset, IntPtr setMethod, bool value)
        {
            IntPtr structAddr = (IntPtr)((byte*)statusObj + fieldOffset);
            IntPtr exc = IntPtr.Zero;
            bool arg = value;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&arg);
            auraMonoRuntimeInvoke(setMethod, structAddr, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        // Writes SkateStatus.mode directly (no property setter => DirtyMask stays 0 => never synced).
        private unsafe bool TryForceLocomotionSetSkateModeRaw(IntPtr statusObj, int skateMode)
        {
            int* modeAddr = (int*)((byte*)statusObj
                + this.forceLocomotionSkateFieldOffset
                + this.forceLocomotionSkateModeInnerOffset);
            *modeAddr = skateMode;
            return true;
        }

        private bool TryForceLocomotionSyncStatus(IntPtr playerObj)
        {
            if (this.forceLocomotionForceSyncMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.forceLocomotionForceSyncMethod, playerObj, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero;
        }

        // Diagnostics. Off (skate confirmed stable — server reverts mode=None only once per session
        // start, the per-frame hold wins thereafter). The SkateStartedEvent hook itself is functional
        // (drives session sanctioning) and is registered regardless. Flip on to re-trace event flow.
        private const bool ForceSkateDiagLogs = false;
        private float forceSkateModeLogAt = -999f;
        private int forceSkateLastLoggedMode = int.MinValue;
        private bool forceSkateHooksRegistered;

        private void EnsureForceSkateHooksRegistered()
        {
            if (this.forceSkateHooksRegistered)
            {
                return;
            }

            this.forceSkateHooksRegistered = true;

            // FUNCTIONAL: server's verdict on our Start. Gates entering skate (no flicker).
            this.RegisterGameEventHook("XDTDataAndProtocol.Events.SkateStartedEvent", 4, this.OnForceSkateStartedEvent);

            if (ForceSkateDiagLogs)
            {
                this.RegisterGameEventHook("XDTDataAndProtocol.Events.SkateModeChangedEvent", 20, snap =>
                    ModLogger.Msg("[ForceSkateDiag] SkateModeChangedEvent challengeEnd=" + snap.ReadBool(0)
                        + " score=" + snap.ReadInt32(4) + " target=" + snap.ReadInt32(8) + " high=" + snap.ReadInt32(12)));
                this.RegisterGameEventHook("XDTLevelAndEntity.Game.GameMode.GameModeEnterEvent", 1, snap =>
                    ModLogger.Msg("[ForceSkateDiag] GameModeEnter mode=" + snap.ReadByte(0)));
                this.RegisterGameEventHook("XDTLevelAndEntity.Game.GameMode.GameModeExitEvent", 1, snap =>
                    ModLogger.Msg("[ForceSkateDiag] GameModeExit mode=" + snap.ReadByte(0)));
            }

            ModLogger.Msg("[ForceSkate] skate event hooks registered");
        }

        // Per-frame skate hold while a session is open:
        //   (1) re-assert SkateStatus.mode=Free every frame — the server pushes mode=None back to the
        //       owner (SkateStatus_Field_0.OnHandle) ~1 Hz; the 1 s reassert lost that race (flicker).
        //   (2) hold GameSkateMode._leaveSkateGroundTime=-1 so the ground-timeout exit never fires.
        private unsafe void ForceSkateHoldGround()
        {
            if (!this.forceSkateEnabled || !this.forceSkateServerSanctioned)
            {
                return;
            }

            try
            {
                // (1) Hold mode=Free against the server's None corrections (raw write, no DirtyMask).
                if (this.forceLocomotionSkateFieldOffset >= 0 && this.forceLocomotionSkateModeInnerOffset >= 0
                    && this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) && playerObj != IntPtr.Zero
                    && this.TryGetMonoObjectMember(playerObj, "Status", out IntPtr statusObj) && statusObj != IntPtr.Zero)
                {
                    int* modeAddr = (int*)((byte*)statusObj
                        + this.forceLocomotionSkateFieldOffset
                        + this.forceLocomotionSkateModeInnerOffset);
                    int curMode = *modeAddr;
                    if (ForceSkateDiagLogs && (curMode != this.forceSkateLastLoggedMode || Time.unscaledTime >= this.forceSkateModeLogAt))
                    {
                        bool changed = curMode != this.forceSkateLastLoggedMode;
                        this.forceSkateLastLoggedMode = curMode;
                        this.forceSkateModeLogAt = Time.unscaledTime + 1f;
                        ModLogger.Msg("[ForceSkateDiag] SkateStatus.mode=" + curMode + (changed ? " <CHANGED>" : string.Empty));
                    }

                    *modeAddr = ForceSkateModeFree;
                }

                // (2) Hold the ground-exit timer off (only meaningful while the mode is active).
                if (this.TryResolveAutoIceSkatingReflection(out _)
                    && this.TryGetAutoIceSkatingAuraMode(out IntPtr skateMode, out _) && skateMode != IntPtr.Zero
                    && this.TryGetMonoBoolMember(skateMode, "actived", out bool actived) && actived
                    && auraMonoFieldSetValue != null)
                {
                    IntPtr cls = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(skateMode) : IntPtr.Zero;
                    if (cls != this.forceSkateGroundFieldClass)
                    {
                        this.forceSkateGroundFieldClass = cls;
                        this.forceSkateLeaveGroundField = cls != IntPtr.Zero
                            ? this.FindAuraMonoFieldOnHierarchy(cls, "_leaveSkateGroundTime")
                            : IntPtr.Zero;
                    }

                    if (this.forceSkateLeaveGroundField != IntPtr.Zero)
                    {
                        float minusOne = -1f;
                        auraMonoFieldSetValue(skateMode, this.forceSkateLeaveGroundField, (IntPtr)(&minusOne));
                    }
                }
            }
            catch
            {
            }
        }

        private void OnForceSkateStartedEvent(GameEventSnapshot snap)
        {
            bool ok = snap.ReadBool(0);
            if (ForceSkateDiagLogs)
            {
                ModLogger.Msg("[ForceSkateDiag] SkateStartedEvent isSucceed=" + ok);
            }

            if (this.forceSkateEnabled && this.forceSkateAwaitingStart && ok)
            {
                this.forceSkateServerSanctioned = true;
                this.forceSkateAwaitingStart = false;
                this.forceLocomotionNextReassertAt = 0f; // apply mode=Free on the next tick
                if (ForceSkateDiagLogs)
                {
                    ModLogger.Msg("[ForceSkate] server sanctioned session — entering skate");
                }
            }
        }

        // --- Force Swim hotkeys (space = ascend, ctrl = dive, shift = sprint) ---------------------
        //
        // Drives SwimLocomotion.SetSwimVerticalInput(isAscending, isPressed) and TryStartSprint()
        // on the local player's active locomotion (LocalPlayerComponent.moveComponent.Locomotion),
        // mirroring the button-down/up events the game itself feeds the swim controller.

        private const int ForceSwimVkSpace = 0x20;
        private const int ForceSwimVkControl = 0x11;
        private const int ForceSwimVkShift = 0x10;

        private bool forceSwimAscendActive;
        private bool forceSwimDescendActive;
        private bool forceSwimPrevShift;
        private IntPtr forceSwimLocoResolvedClass;
        private IntPtr forceSwimSetVerticalMethod;   // SwimLocomotion.SetSwimVerticalInput(bool,bool)
        private IntPtr forceSwimTryStartSprintMethod; // SwimLocomotion.TryStartSprint()

        private static bool ForceSwimKeyHeld(int vKey)
        {
            try
            {
                return (GetAsyncKeyState(vKey) & 0x8000) != 0;
            }
            catch
            {
                return false;
            }
        }

        private void ProcessForceSwimInputOnUpdate()
        {
            if (!this.forceSwimEnabled || this.noclipEnabled)
            {
                this.forceSwimPrevShift = false;
                this.forceSwimAscendActive = false;
                this.forceSwimDescendActive = false;
                return;
            }

            // GetAsyncKeyState reads the GLOBAL keyboard, so we must gate on window focus ourselves
            // (keys typed into other apps otherwise still drive the swimmer), and on a focused game
            // text field (chat/rename/search — same uGUI InputField check the hotkey guard uses).
            // First clause: any MODAL registry surface open (the UGUI shell) — showMenu is
            // retired. Kept separate from ShouldBlockGameplayInput on purpose: that gate is
            // further conditioned on the user's blockGameUiWhenMenuOpen setting.
            if (this.IsAnyModalInputSurfaceOpen() || ShouldBlockGameplayInput()
                || !IsGameWindowFocused()
                || this.IsGameTextInputFocused())
            {
                // Release any held vertical intent so the swimmer doesn't keep climbing while typing.
                this.TryReleaseForceSwimVertical();
                this.forceSwimPrevShift = false;
                return;
            }

            try
            {
                this.TickForceSwimInput();
            }
            catch
            {
                // Never let a per-frame input hiccup escalate; swim state is re-asserted elsewhere.
            }
        }

        // Shared by every feature that reads input outside Unity's focus-aware path (GetAsyncKeyState,
        // XInput): swim hotkeys, bunny hop, the analog move bridge.
        internal static bool IsGameWindowFocused()
        {
            try
            {
                return Application.isFocused;
            }
            catch
            {
                // Interop hiccup — fail open so the feature keeps working in the foreground case.
                return true;
            }
        }

        private void TickForceSwimInput()
        {
            if (!this.TryResolveForceSwimLocomotion(out IntPtr locoObj) || locoObj == IntPtr.Zero)
            {
                // Not in swim locomotion (yet): drop cached intent, keep toggles as-is.
                this.forceSwimAscendActive = false;
                this.forceSwimDescendActive = false;
                this.forceSwimPrevShift = false;
                return;
            }

            bool space = ForceSwimKeyHeld(ForceSwimVkSpace);
            bool ctrl = ForceSwimKeyHeld(ForceSwimVkControl);
            bool shift = ForceSwimKeyHeld(ForceSwimVkShift);

            bool wantAscend = space;            // space rises
            bool wantDescend = ctrl && !space;  // ctrl dives (space wins the tie)

            uint pin = AuraMonoPinNew(locoObj);
            try
            {
                if (wantAscend && !this.forceSwimAscendActive)
                {
                    this.TryInvokeForceSwimVertical(locoObj, true, true);
                    this.forceSwimAscendActive = true;
                }
                else if (!wantAscend && this.forceSwimAscendActive)
                {
                    this.TryInvokeForceSwimVertical(locoObj, true, false);
                    this.forceSwimAscendActive = false;
                }

                if (wantDescend && !this.forceSwimDescendActive)
                {
                    this.TryInvokeForceSwimVertical(locoObj, false, true);
                    this.forceSwimDescendActive = true;
                }
                else if (!wantDescend && this.forceSwimDescendActive)
                {
                    this.TryInvokeForceSwimVertical(locoObj, false, false);
                    this.forceSwimDescendActive = false;
                }

                if (shift && !this.forceSwimPrevShift && this.forceSwimTryStartSprintMethod != IntPtr.Zero)
                {
                    IntPtr exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(this.forceSwimTryStartSprintMethod, locoObj, IntPtr.Zero, ref exc);
                }
            }
            finally
            {
                AuraMonoPinFree(pin);
            }

            this.forceSwimPrevShift = shift;
        }

        private void TryReleaseForceSwimVertical()
        {
            if (!this.forceSwimAscendActive && !this.forceSwimDescendActive)
            {
                return;
            }

            if (this.TryResolveForceSwimLocomotion(out IntPtr locoObj) && locoObj != IntPtr.Zero)
            {
                uint pin = AuraMonoPinNew(locoObj);
                try
                {
                    if (this.forceSwimAscendActive)
                    {
                        this.TryInvokeForceSwimVertical(locoObj, true, false);
                    }

                    if (this.forceSwimDescendActive)
                    {
                        this.TryInvokeForceSwimVertical(locoObj, false, false);
                    }
                }
                finally
                {
                    AuraMonoPinFree(pin);
                }
            }

            this.forceSwimAscendActive = false;
            this.forceSwimDescendActive = false;
        }

        private bool TryResolveForceSwimLocomotion(out IntPtr locoObj)
        {
            locoObj = IntPtr.Zero;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr moveObj = IntPtr.Zero;
            string[] moveMembers = { "moveComponent", "_moveComponent" };
            for (int i = 0; i < moveMembers.Length && moveObj == IntPtr.Zero; i++)
            {
                this.TryGetMonoObjectMember(playerObj, moveMembers[i], out moveObj);
            }

            if (moveObj == IntPtr.Zero
                && (!this.TryInvokeAuraMonoZeroArg(playerObj, out moveObj, "get_moveComponent", "GetMoveComponent") || moveObj == IntPtr.Zero))
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(moveObj, "Locomotion", out locoObj) || locoObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr locoClass = auraMonoObjectGetClass(locoObj);
            if (locoClass != this.forceSwimLocoResolvedClass)
            {
                this.forceSwimLocoResolvedClass = locoClass;
                this.forceSwimSetVerticalMethod = locoClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(locoClass, "SetSwimVerticalInput", 2)
                    : IntPtr.Zero;
                this.forceSwimTryStartSprintMethod = this.forceSwimSetVerticalMethod != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(locoClass, "TryStartSprint", 0)
                    : IntPtr.Zero;
            }

            // Only a SwimLocomotion exposes SetSwimVerticalInput — otherwise this is Free/Stand/etc.
            return this.forceSwimSetVerticalMethod != IntPtr.Zero;
        }

        private unsafe void TryInvokeForceSwimVertical(IntPtr locoObj, bool ascending, bool pressed)
        {
            if (this.forceSwimSetVerticalMethod == IntPtr.Zero)
            {
                return;
            }

            bool a = ascending;
            bool p = pressed;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&a);
            args[1] = (IntPtr)(&p);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.forceSwimSetVerticalMethod, locoObj, (IntPtr)args, ref exc);
        }
    }
}
