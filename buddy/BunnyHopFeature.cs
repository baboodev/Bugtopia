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
        private IntPtr bunnyHopMonoIsGroundedMethod;
        private IntPtr bunnyHopMonoIsSlidingMethod;
        private float bunnyHopGroundedRetryAt;

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

            // GetAsyncKeyState reads the GLOBAL keyboard: without these gates, Space typed into
            // another app (game unfocused) or into a game chat/text field still triggers jumps.
            if (this.noclipEnabled || ShouldBlockGameplayInput()
                || !IsGameWindowFocused()
                || this.IsGameTextInputFocused())
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

            bool landing;
            if (this.TryReadBunnyHopSurfaceState(out bool grounded, out bool sliding))
            {
                // MovementComponent.IsGrounded is the flag the game's own PreJumpButton gate reads
                // (ground distance, recomputed every locomotion tick). Unlike the height heuristic it
                // registers landings ON elevations, where touchdown happens near the jump apex and the
                // fall phase is too slow to cross a fall-speed threshold.
                // Steep (near-vertical) surfaces never report grounded (CantStand) — the game puts the
                // player into SlideLocomotion instead, and jumping out of a slide is officially
                // supported: FreeLocomotion.SetJumpData kicks Slide->Stand and forwards the jump. So a
                // slide contact counts as a hoppable landing too.
                if (!grounded)
                {
                    this.bunnyHopWasAirborne = true;
                }

                bool onHoppableSurface = grounded || sliding;

                // The airborne->surface transition gives the snappy cadence, but it is not enough on
                // its own: hopping up a slope pressed into it never breaks ground contact
                // (GroundAirbornThreshold is 0.15 m, the feet stay within it the whole hop), and any
                // Stand<->Slide flip wipes StandLocomotion's queued jump input — either way one missed
                // hop would end the chain with Space still held. The stall retry replicates what a
                // manual Space re-press does: an unconditional fresh input edge on a hoppable surface.
                // Free fall stays pulse-free: a mid-air SetJumpInput(false) would zero _jumpStartTime
                // and cut hold-jump gravity.
                landing = onHoppableSurface
                    && (this.bunnyHopWasAirborne
                        || Time.unscaledTime - this.bunnyHopLastJumpAt >= 0.6f);
                this.bunnyHopLastPlayerY = float.NaN;
            }
            else
            {
                landing = this.DetectBunnyHopLandingByHeight();
            }

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

        private bool TryReadBunnyHopSurfaceState(out bool grounded, out bool sliding)
        {
            grounded = false;
            sliding = false;

            float now = Time.unscaledTime;
            if (now < this.bunnyHopGroundedRetryAt)
            {
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                this.bunnyHopGroundedRetryAt = now + 5f;
                return false;
            }

            if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero
                || !this.TryGetBunnyHopMonoMoveComponent(playerObj, out IntPtr moveObj) || moveObj == IntPtr.Zero)
            {
                // Normal transient while loading/changing worlds — retry soon, not every frame.
                this.bunnyHopGroundedRetryAt = now + 1f;
                return false;
            }

            if (this.bunnyHopMonoIsGroundedMethod == IntPtr.Zero)
            {
                IntPtr moveClass = auraMonoObjectGetClass(moveObj);
                this.bunnyHopMonoIsGroundedMethod = this.FindAuraMonoMethodOnHierarchy(moveClass, "get_IsGrounded", 0);
                if (this.bunnyHopMonoIsGroundedMethod == IntPtr.Zero)
                {
                    this.bunnyHopGroundedRetryAt = now + 10f;
                    return false;
                }

                // Optional: SlideLocomotion.OnEnter sets it, StandLocomotion clears it. Missing on a
                // build just means steep-surface landings degrade to grounded-only.
                this.bunnyHopMonoIsSlidingMethod = this.FindAuraMonoMethodOnHierarchy(moveClass, "get_IsSliding", 0);
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.bunnyHopMonoIsGroundedMethod, moveObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero || !this.TryUnboxMonoBoolean(boxed, out grounded))
            {
                this.bunnyHopMonoIsGroundedMethod = IntPtr.Zero;
                this.bunnyHopGroundedRetryAt = now + 5f;
                return false;
            }

            if (this.bunnyHopMonoIsSlidingMethod != IntPtr.Zero)
            {
                exc = IntPtr.Zero;
                boxed = auraMonoRuntimeInvoke(this.bunnyHopMonoIsSlidingMethod, moveObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero || !this.TryUnboxMonoBoolean(boxed, out sliding))
                {
                    sliding = false;
                    this.bunnyHopMonoIsSlidingMethod = IntPtr.Zero;
                }
            }

            return true;
        }

        // Fallback when the game's grounded flag is unavailable. Thresholds are velocities (m/s),
        // not per-frame deltas: per-frame constants assumed ~60 fps and stopped seeing falls at
        // higher frame rates. Still misses near-apex landings on elevations — the grounded flag is
        // the fix for those.
        private bool DetectBunnyHopLandingByHeight()
        {
            GameObject skeleton = GetLocalPlayer();
            if (skeleton == null)
            {
                return false;
            }

            float dt = Time.deltaTime;
            if (dt <= 0.0001f)
            {
                return false;
            }

            float y = skeleton.transform.position.y;
            float verticalSpeed = float.IsNaN(this.bunnyHopLastPlayerY) ? 0f : (y - this.bunnyHopLastPlayerY) / dt;
            this.bunnyHopLastPlayerY = y;

            if (verticalSpeed < -2.4f)
            {
                this.bunnyHopWasAirborne = true;
                this.bunnyHopFallSeen = true;
            }

            return this.bunnyHopFallSeen
                && this.bunnyHopWasAirborne
                && verticalSpeed >= -0.72f
                && verticalSpeed <= 1.2f;
        }

        private void ResetBunnyHopState()
        {
            this.bunnyHopWasAirborne = false;
            this.bunnyHopFallSeen = false;
            this.bunnyHopLastPlayerY = float.NaN;
            this.bunnyHopMonoJumpMethodsResolved = false;
            this.bunnyHopMonoIsGroundedMethod = IntPtr.Zero;
            this.bunnyHopMonoIsSlidingMethod = IntPtr.Zero;
            this.bunnyHopGroundedRetryAt = 0f;
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
