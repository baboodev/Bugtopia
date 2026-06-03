using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const int BunnyHopVkSpace = 0x20;

        private bool bunnyHopEnabled;
        private bool bunnyHopWasAirborne;
        private bool bunnyHopFallSeen;
        private float bunnyHopLastPlayerY = float.NaN;
        private float bunnyHopLastJumpAt;

        private IntPtr bunnyHopMonoSetJumpInputMethod;
        private IntPtr bunnyHopMonoOnJumpButtonMethod;
        private bool bunnyHopMonoJumpMethodsResolved;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        private static bool IsPhysicalSpaceHeld()
        {
            try
            {
                if ((GetAsyncKeyState(BunnyHopVkSpace) & 0x8000) != 0)
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                byte[] state = new byte[256];
                if (GetKeyboardState(state) && (state[BunnyHopVkSpace] & 0x80) != 0)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private void ProcessBunnyHopOnUpdate()
        {
            if (!this.bunnyHopEnabled)
            {
                this.ResetBunnyHopState();
                return;
            }

            if (this.noclipEnabled || ShouldBlockGameplayInput())
            {
                return;
            }

            if (!IsPhysicalSpaceHeld())
            {
                this.bunnyHopWasAirborne = false;
                this.bunnyHopFallSeen = false;
                this.bunnyHopLastPlayerY = float.NaN;
                return;
            }

            GameObject skeleton = GetLocalPlayer();
            if (skeleton == null)
            {
                return;
            }

            float y = skeleton.transform.position.y;
            float verticalDelta = float.IsNaN(this.bunnyHopLastPlayerY) ? 0f : y - this.bunnyHopLastPlayerY;
            this.bunnyHopLastPlayerY = y;

            if (verticalDelta < -0.04f)
            {
                this.bunnyHopWasAirborne = true;
                this.bunnyHopFallSeen = true;
            }

            bool landing = this.bunnyHopFallSeen
                && this.bunnyHopWasAirborne
                && verticalDelta >= -0.012f
                && verticalDelta <= 0.02f;

            if (!landing
                || Time.unscaledTime - this.bunnyHopLastJumpAt < 0.35f
                || !IsPhysicalSpaceHeld())
            {
                return;
            }

            this.bunnyHopLastJumpAt = Time.unscaledTime;
            this.bunnyHopWasAirborne = false;
            this.bunnyHopFallSeen = false;
            this.TryBunnyHopJumpViaMono();
        }

        private void ResetBunnyHopState()
        {
            this.bunnyHopWasAirborne = false;
            this.bunnyHopFallSeen = false;
            this.bunnyHopLastPlayerY = float.NaN;
            this.bunnyHopMonoJumpMethodsResolved = false;
        }

        private unsafe bool TryBunnyHopJumpViaMono()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryResolveBunnyHopMonoJumpMethods(playerObj))
            {
                return false;
            }

            bool any = false;

            if (this.bunnyHopMonoOnJumpButtonMethod != IntPtr.Zero
                && this.TryInvokeAuraMonoBool1PulseOnPlayerState(playerObj, this.bunnyHopMonoOnJumpButtonMethod))
            {
                any = true;
            }

            if (this.bunnyHopMonoSetJumpInputMethod != IntPtr.Zero
                && this.TryGetBunnyHopMonoMoveComponent(playerObj, out IntPtr move)
                && move != IntPtr.Zero
                && this.TryInvokeAuraMonoSetJumpInputPulse(move, this.bunnyHopMonoSetJumpInputMethod))
            {
                any = true;
            }

            return any;
        }

        private bool TryResolveBunnyHopMonoJumpMethods(IntPtr playerObj)
        {
            if (this.bunnyHopMonoJumpMethodsResolved)
            {
                return this.bunnyHopMonoSetJumpInputMethod != IntPtr.Zero || this.bunnyHopMonoOnJumpButtonMethod != IntPtr.Zero;
            }

            this.bunnyHopMonoJumpMethodsResolved = true;

            if (playerObj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return false;
            }

            if (this.TryGetBunnyHopMonoMoveComponent(playerObj, out IntPtr moveObj) && moveObj != IntPtr.Zero)
            {
                this.bunnyHopMonoSetJumpInputMethod = this.FindAuraMonoMethodOnHierarchy(
                    auraMonoObjectGetClass(moveObj),
                    "SetJumpInput",
                    1);
            }

            string[] stateSuffixes = { "PlayerStateGround", "PlayerStateMove", "PlayerStateBase", "PlayerStateIdle", "PlayerState" };
            for (int i = 0; i < stateSuffixes.Length; i++)
            {
                if (!this.TryFindAuraMonoPlayerState(stateSuffixes[i], out IntPtr stateObj) || stateObj == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr onJump = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(stateObj), "OnJumpButton", 1);
                if (onJump != IntPtr.Zero)
                {
                    this.bunnyHopMonoOnJumpButtonMethod = onJump;
                    break;
                }
            }

            return this.bunnyHopMonoSetJumpInputMethod != IntPtr.Zero || this.bunnyHopMonoOnJumpButtonMethod != IntPtr.Zero;
        }

        private bool TryGetBunnyHopMonoMoveComponent(IntPtr playerObj, out IntPtr moveObj)
        {
            moveObj = IntPtr.Zero;
            if (playerObj == IntPtr.Zero)
            {
                return false;
            }

            string[] members = { "moveComponent", "_moveComponent" };
            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetMonoObjectMember(playerObj, members[i], out moveObj) && moveObj != IntPtr.Zero)
                {
                    return true;
                }
            }

            return this.TryInvokeAuraMonoZeroArg(playerObj, out moveObj, "get_moveComponent", "GetMoveComponent") && moveObj != IntPtr.Zero;
        }

        private unsafe bool TryInvokeAuraMonoBool1(IntPtr instance, IntPtr method, bool value)
        {
            if (instance == IntPtr.Zero || method == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&value);
            auraMonoRuntimeInvoke(method, instance, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        private bool TryInvokeAuraMonoBool1PulseOnPlayerState(IntPtr playerObj, IntPtr method)
        {
            string[] stateMembers = { "currState", "_currState", "currentState", "fsmState", "state", "CurrState" };
            for (int i = 0; i < stateMembers.Length; i++)
            {
                if (!this.TryGetMonoObjectMember(playerObj, stateMembers[i], out IntPtr stateObj) || stateObj == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryInvokeAuraMonoBool1(stateObj, method, false)
                    && this.TryInvokeAuraMonoBool1(stateObj, method, true))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryInvokeAuraMonoSetJumpInputPulse(IntPtr move, IntPtr method)
        {
            return this.TryInvokeAuraMonoBool1(move, method, false)
                && this.TryInvokeAuraMonoBool1(move, method, true);
        }
    }
}
