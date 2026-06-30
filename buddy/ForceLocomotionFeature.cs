using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Force Swim / Skate on land.
    //
    // Locomotion is a client FSM (PlayerFsMachine on LocalPlayerComponent). The Free<->Swim and
    // Free<->Skate transitions are gated purely by two synced status fields:
    //   * PlayerStateSwim.IsStateSatisfy()  => Status.SwimStatus.IsSwimming
    //   * PlayerStateSkate.IsStateSatisfy() => Status.SkateStatus.Enable (Mode != None)
    // (see ilspy-dumps/.../Component/Player/PlayerStateSwim.cs, PlayerStateSkate.cs, *Transition*.cs)
    //
    // This is the SERVER-going variant: we write the field through its real struct setter
    // (set_IsSwimming / set_Mode), which raises the status DirtyMask, then call
    // LocalPlayerComponent.ForceSyncStatus() so PlayerSyncStatus.SendSyncStatus pushes the dirtied
    // field to the server (and thus to other players). Setters are no-ops when the value is
    // unchanged, so periodic re-assertion costs nothing on a steady state.
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
        private IntPtr forceLocomotionSkateSetModeMethod;  // SkateStatus.set_Mode(SkateMode)
        private IntPtr forceLocomotionForceSyncMethod;     // LocalPlayerComponent.ForceSyncStatus()
        private IntPtr forceLocomotionForceSyncClass;
        private IntPtr forceLocomotionStatusClassResolvedFor;
        private int forceLocomotionSwimFieldOffset = -1;   // offset of SwimStatus field in PlayerSyncStatus
        private int forceLocomotionSkateFieldOffset = -1;  // offset of SkateStatus field in PlayerSyncStatus

        private void ProcessForceLocomotionOnUpdate()
        {
            bool togglesChanged = this.forceSwimEnabled != this.forceLocomotionPrevSwim
                || this.forceSkateEnabled != this.forceLocomotionPrevSkate;

            if (!togglesChanged && !this.forceSwimEnabled && !this.forceSkateEnabled)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (!togglesChanged && now < this.forceLocomotionNextReassertAt)
            {
                return;
            }

            if (!this.forceLocomotionBreaker.ShouldRun(now))
            {
                return;
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

                bool skateOk = this.forceLocomotionSkateSetModeMethod != IntPtr.Zero
                    && this.forceLocomotionSkateFieldOffset >= 0
                    && this.TryForceLocomotionSetStructInt(
                        statusObj,
                        this.forceLocomotionSkateFieldOffset,
                        this.forceLocomotionSkateSetModeMethod,
                        this.forceSkateEnabled ? ForceSkateModeFree : ForceSkateModeNone);

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

            if (this.forceLocomotionSkateSetModeMethod == IntPtr.Zero && this.forceLocomotionSkateStatusClass != IntPtr.Zero)
            {
                this.forceLocomotionSkateSetModeMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.forceLocomotionSkateStatusClass, "set_Mode", 1);
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
            bool skateReady = this.forceLocomotionSkateSetModeMethod != IntPtr.Zero && this.forceLocomotionSkateFieldOffset >= 0;
            if (swimReady && skateReady && this.forceLocomotionForceSyncMethod != IntPtr.Zero)
            {
                return true;
            }

            detail = "swimClass=" + (this.forceLocomotionSwimStatusClass != IntPtr.Zero)
                + " swimSet=" + (this.forceLocomotionSwimSetMethod != IntPtr.Zero)
                + " swimOff=" + this.forceLocomotionSwimFieldOffset
                + " skateClass=" + (this.forceLocomotionSkateStatusClass != IntPtr.Zero)
                + " skateSet=" + (this.forceLocomotionSkateSetModeMethod != IntPtr.Zero)
                + " skateOff=" + this.forceLocomotionSkateFieldOffset
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

        private unsafe bool TryForceLocomotionSetStructInt(IntPtr statusObj, int fieldOffset, IntPtr setMethod, int value)
        {
            IntPtr structAddr = (IntPtr)((byte*)statusObj + fieldOffset);
            IntPtr exc = IntPtr.Zero;
            int arg = value;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&arg);
            auraMonoRuntimeInvoke(setMethod, structAddr, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
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

            if (this.showMenu || ShouldBlockGameplayInput())
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
