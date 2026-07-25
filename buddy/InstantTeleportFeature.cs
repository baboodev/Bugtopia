using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Instant in-game teleport: skip the game's animated transfer sequence (splash + fixed waits +
    // uninterruptible landing action) and warp straight to the destination.
    //
    // Vanilla funnel for EVERY same-level teleport (teleport point, dialogue, quest, UGC):
    //
    //   PlayerTeleportation (EventCenter, server)                    XDTDataAndProtocol.Events
    //     -> TeleportModule.PlayerTeleportation                      TeleportModule.cs:204
    //     -> EntityHelper.TransferToPosWithAnim                      EntityHelper.cs:109
    //     -> PlayerInteraction.ExecuteEventCommand<TransferCommandEvent>   (interact bus)
    //     -> TransferCommand.OnExecuteAsync                          TransferCommand.cs:20
    //     -> TeleportModule.TeleportAsync                            TeleportModule.cs:244
    //          SwitchScenePanel.Open  +  await 2s (TransferAnimationLimitTime)
    //          -> EntityHelper.TransferToPos (the actual warp)
    //          +  await 1s (TransferLoadingLimitTime - TransferAnimationLimitTime)
    //          +  await WaitUntilRendered (EntityUtil.IsFieldLoaded)
    //          -> second TransferToPos, panel close, PlayerSwitchSceneOff
    //     and, in parallel, Cast(PlayerSwitchSceneOnArgs) — an action configured
    //     InterruptFlags.Null, i.e. UNINTERRUPTIBLE, which is what freezes movement for ~3 s.
    //
    // Interception point (the cheap, surgical one):
    //
    //   EventCommand<T>.ExecuteAsync (EventCommand.cs):
    //       int num = IsExecutable(in arg);
    //       if (num == 0) return new InputResult(num, OnExecuteAsync(in arg));
    //       return new InputResult(num, null);
    //
    // A Mono NativeDetour on TransferCommand.IsExecutable returning non-zero makes the interact
    // system refuse the command through its OWN well-defined path: OnExecuteAsync never runs, so
    // there is no panel, no fixed waits, no uninterruptible action and no delayed second warp.
    // The refusal code is -1 = InteractErrorCode.Invalid, which PlayerInteraction.ToastInteractError
    // explicitly skips — so the refusal is SILENT (no error tip).
    //
    // Cross-level teleports are untouched by construction: TeleportModule.PlayerTeleportation sends
    // those to TransferToRoom -> LoginSystem.TeleportToRoomLevel, which never goes through
    // TransferCommand. This detour therefore cannot break room/world transfers — the reason it was
    // preferred over suppressing the PlayerTeleportation event (suppression is per event TYPE, with
    // no way to tell the two branches apart).
    //
    // What the skipped OnExecuteAsync also did, and what we do about it:
    //   * FullScreenCloseRequestedEvent — closed the fullscreen panel the teleport was triggered
    //     from (the map). We close the shell's own overlay if it is up; a game panel left open is
    //     cosmetic and the player closes it as usual.
    //   * Exit2FreeStateTask + WaitStateTask(Free) — left the current player state first. We warp
    //     as-is, exactly like every other mod teleport does (aura farm, foraging, fishing routes).
    //   * await WaitUntilRendered — waited for the destination field to stream in. THIS one is real
    //     protection (warping into an unloaded field can drop you through the terrain), so it is the
    //     second toggle: "Wait For Field Load" re-asserts the target position until
    //     EntityUtil.IsFieldLoaded(inFieldOwnerId) turns true (bounded), instead of waiting a fixed
    //     3 s. With it off the warp is a single write and you are free to move immediately.
    public partial class HeartopiaComplete
    {
        // Toggles (persisted; instant OFF = vanilla animated teleport). Drawn in Self -> Main.
        private bool instantTeleportEnabled;
        private bool instantTeleportWaitFieldLoaded = true;

        // Written by ProcessInstantTeleportOnUpdate (main thread); read by the native hook body.
        // Only true once the detour is actually live, so the event handler never double-warps while
        // the vanilla sequence is still the one running.
        private static volatile bool instantTeleportActive;

        // InteractErrorCode.Invalid — the one non-zero code ToastInteractError does not toast.
        private const int InstantTeleportRefuseCode = -1;

        // The destination is read from the COMMAND ARGUMENT, not from an event.
        //
        // First attempt sourced it from the PlayerTeleportation event and teleports silently
        // vanished: TransferCommandEvent has FOUR producers and that event only covers one of them.
        //   * TeleportModule.PlayerTeleportation            <- PlayerTeleportation event
        //   * DefaultModule.PlayerCommonTeleportation       <- CommonTeleportation event
        //     (EntityHelper.TransferToPosById — the ordinary teleport-point round trip:
        //      SendTeleportCommand -> server -> CommonTeleportation)
        //   * DefaultModule.OnPlayerFieldTeleportation      <- PlayerFieldTeleportation event
        //   * SettingPanel "reset position" and PlayerFishingShipComponent's OOB rescue — no event
        //     at all, they build the command directly.
        // The `in TransferCommandEvent` pointer the detour already receives is the one place every
        // producer funnels through, so it is the single source of truth.
        //
        // struct TransferCommandEvent (sequential, natural alignment):
        //   SwitchSceneType switchSceneType @0 (int)
        //   Vector3         targetPos       @4  (x@4, y@8, z@12)
        //   float           angle           @16   (yaw, degrees)
        //   Action<Vector3> action          @24   (pointer — NOT read; everything we read sits
        //                                          before the first padding, all 4-byte aligned)
        //   AnimType        animType        @32
        //   TeleportReason  reason          @36
        // The three offsets are RESOLVED AT RUNTIME from the struct's own metadata
        // (mono_field_get_offset minus the 2-pointer boxed header, the codebase idiom) rather than
        // hardcoded, so a field added to the struct by a game update cannot silently turn into a
        // warp to garbage coordinates. If they do not resolve, the feature refuses to arm.
        private static int instantTeleportArgSwitchSceneTypeOffset = -1;
        private static int instantTeleportArgTargetPosOffset = -1;
        private static int instantTeleportArgAngleOffset = -1;
        private bool instantTeleportArgOffsetsResolved;

        private const int InstantTeleportSwitchSceneTypeStory = 1; // SwitchSceneType.Story
        private const float InstantTeleportMaxCoordinate = 100000f;

        // Field-load hold: how long we are willing to re-assert the target position, and how often.
        private const float InstantTeleportHoldMaxSeconds = 6f;
        private const float InstantTeleportReassertInterval = 0.15f;
        // Used when EntityUtil.IsFieldLoaded / inFieldOwnerId cannot be resolved on this build:
        // hold blind for a short moment instead of not protecting at all.
        private const float InstantTeleportBlindHoldSeconds = 0.6f;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InstantTeleportIsExecutableHookDelegate(IntPtr self, IntPtr arg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr InstantTeleportCompileMethodDelegate(IntPtr method);

        private static MonoMod.RuntimeDetour.NativeDetour instantTeleportDetour;
        private static InstantTeleportIsExecutableHookDelegate instantTeleportHookKeepAlive; // anti-GC
        private static InstantTeleportIsExecutableHookDelegate instantTeleportTrampoline;
        private static volatile bool instantTeleportRefusedOnce;

        private bool instantTeleportHookTried;
        private float instantTeleportNextAttemptAt = -999f;
        private bool instantTeleportRefuseLogged;
        private bool instantTeleportWarpLogged;

        // Handoff from the native body to the main-thread tick. Both run on the Unity main thread
        // (the interact command is executed from game code on it), so no interlocking is needed —
        // the body only writes four floats and raises the flag last.
        private static volatile bool instantTeleportPendingWarp;
        private static float instantTeleportPendingX;
        private static float instantTeleportPendingY;
        private static float instantTeleportPendingZ;
        private static float instantTeleportPendingAngle;

        // Pending warp / field-load hold state (main thread only).
        private bool instantTeleportHoldActive;
        private Vector3 instantTeleportTargetPos;
        private Quaternion instantTeleportTargetRot;
        private bool instantTeleportHasRot;
        private float instantTeleportHoldUntil;
        private float instantTeleportNextReassertAt;

        private IntPtr instantTeleportEntityUtilClass = IntPtr.Zero;
        private IntPtr instantTeleportIsFieldLoadedMethod = IntPtr.Zero;
        private IntPtr instantTeleportGetSelfPlayerMethod = IntPtr.Zero;
        private bool instantTeleportFieldProbeUnavailable;

        private static readonly string[] InstantTeleportImageNames =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "Client", "Client.dll"
        };

        internal bool InstantTeleportActive => instantTeleportActive;

        private void ProcessInstantTeleportOnUpdate()
        {
            if (!this.instantTeleportEnabled)
            {
                instantTeleportActive = false; // installed hook (if any) forwards -> vanilla sequence
                instantTeleportPendingWarp = false;
                this.instantTeleportHoldActive = false;
                return;
            }

            this.EnsureInstantTeleportHook();
            instantTeleportActive = instantTeleportTrampoline != null;

            if (instantTeleportActive && !this.instantTeleportRefuseLogged && instantTeleportRefusedOnce)
            {
                this.instantTeleportRefuseLogged = true;
                ModLogger.Msg("[InstantTeleport] vanilla transfer command refused — the detour is live"
                    + " (no splash, no 3s wait, no movement lock).");
            }

            this.ConsumeInstantTeleportPendingWarp();
            this.TickInstantTeleportFieldHold();
        }

        private void EnsureInstantTeleportHook()
        {
            if (instantTeleportTrampoline != null || this.instantTeleportHookTried)
            {
                return;
            }

            // World-ready gate: XDTLevelAndEntity has no TransferCommand to resolve before a world.
            if (!this.IsWorldReady)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.instantTeleportNextAttemptAt)
            {
                return;
            }
            this.instantTeleportNextAttemptAt = now + 5f;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // AuraMono not up yet — retry on the cadence
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                InstantTeleportCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<InstantTeleportCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.instantTeleportHookTried = true;
                    ModLogger.Msg("[InstantTeleport] mono_compile_method unavailable — feature off.");
                    return;
                }

                // Layout first: without it the body cannot read the destination, and refusing the
                // command without warping would make teleports vanish.
                if (!this.TryResolveInstantTeleportArgOffsets())
                {
                    return; // retries on the cadence; a hard miss burns the tried-flag below
                }

                IntPtr cls = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Gameplay.Interaction", "TransferCommand", InstantTeleportImageNames);
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Interaction.TransferCommand");
                }
                if (cls == IntPtr.Zero)
                {
                    return; // image not loaded yet — retry on the cadence
                }

                // protected override int IsExecutable(in TransferCommandEvent arg) -> int(self, ptr).
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "IsExecutable", 1);
                if (method == IntPtr.Zero)
                {
                    this.instantTeleportHookTried = true;
                    ModLogger.Msg("[InstantTeleport] TransferCommand.IsExecutable(1) not found — feature off (game update?).");
                    return;
                }

                IntPtr nativePtr = compile(method);
                if (nativePtr == IntPtr.Zero)
                {
                    return; // JIT entry not ready — retry on the cadence
                }

                this.instantTeleportHookTried = true;
                instantTeleportHookKeepAlive = InstantTeleportIsExecutableDetourBody;
                instantTeleportDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, instantTeleportHookKeepAlive);
                instantTeleportTrampoline = instantTeleportDetour.GenerateTrampoline<InstantTeleportIsExecutableHookDelegate>();
                if (instantTeleportTrampoline == null)
                {
                    try { instantTeleportDetour?.Undo(); } catch { }
                    instantTeleportDetour = null;
                    instantTeleportHookKeepAlive = null;
                    ModLogger.Msg("[InstantTeleport] trampoline unavailable for IsExecutable; detour reverted — feature off.");
                    return;
                }

                ModLogger.Msg("[InstantTeleport] Hooked TransferCommand.IsExecutable @0x" + nativePtr.ToInt64().ToString("X")
                    + " — animated transfers are refused silently while the toggle is on.");
            }
            catch (Exception ex)
            {
                this.instantTeleportHookTried = true;
                try { instantTeleportDetour?.Undo(); } catch { }
                instantTeleportDetour = null;
                instantTeleportHookKeepAlive = null;
                instantTeleportTrampoline = null;
                ModLogger.Msg("[InstantTeleport] IsExecutable hook install failed: " + ex.Message + " — feature off.");
            }
        }

        // Reads the three offsets we need out of TransferCommandEvent's metadata. Every offset is
        // sanity-checked (non-negative, inside a plausible struct, 4-byte aligned) so a metadata
        // surprise disarms the feature instead of producing a wild pointer read in the hook body.
        private bool TryResolveInstantTeleportArgOffsets()
        {
            if (this.instantTeleportArgOffsetsResolved)
            {
                return true;
            }

            if (auraMonoFieldGetOffset == null)
            {
                return false;
            }

            IntPtr argClass = this.FindAuraMonoClassInImages(
                "XDTLevelAndEntity.Gameplay.Interaction", "TransferCommandEvent", InstantTeleportImageNames);
            if (argClass == IntPtr.Zero)
            {
                argClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Interaction.TransferCommandEvent");
            }
            if (argClass == IntPtr.Zero)
            {
                return false; // image not up yet
            }

            IntPtr typeField = this.FindAuraMonoFieldOnHierarchy(argClass, "switchSceneType");
            IntPtr posField = this.FindAuraMonoFieldOnHierarchy(argClass, "targetPos");
            IntPtr angleField = this.FindAuraMonoFieldOnHierarchy(argClass, "angle");
            if (typeField == IntPtr.Zero || posField == IntPtr.Zero || angleField == IntPtr.Zero)
            {
                this.instantTeleportHookTried = true;
                ModLogger.Msg("[InstantTeleport] TransferCommandEvent fields (switchSceneType/targetPos/angle)"
                    + " not found — feature off (game update?).");
                return false;
            }

            // mono_field_get_offset counts from the boxed object start; the `in` pointer we get is
            // the bare struct, so drop the 2-pointer header (codebase idiom).
            int header = 2 * IntPtr.Size;
            int typeOffset = (int)auraMonoFieldGetOffset(typeField) - header;
            int posOffset = (int)auraMonoFieldGetOffset(posField) - header;
            int angleOffset = (int)auraMonoFieldGetOffset(angleField) - header;

            const int maxStructSpan = 256;
            bool sane = typeOffset >= 0 && posOffset >= 0 && angleOffset >= 0
                && typeOffset + 4 <= maxStructSpan
                && posOffset + 12 <= maxStructSpan
                && angleOffset + 4 <= maxStructSpan
                && (typeOffset % 4) == 0 && (posOffset % 4) == 0 && (angleOffset % 4) == 0;
            if (!sane)
            {
                this.instantTeleportHookTried = true;
                ModLogger.Msg("[InstantTeleport] TransferCommandEvent layout looks wrong (type=" + typeOffset
                    + " pos=" + posOffset + " angle=" + angleOffset + ") — feature off.");
                return false;
            }

            instantTeleportArgSwitchSceneTypeOffset = typeOffset;
            instantTeleportArgTargetPosOffset = posOffset;
            instantTeleportArgAngleOffset = angleOffset;
            this.instantTeleportArgOffsetsResolved = true;
            ModLogger.Msg("[InstantTeleport] TransferCommandEvent layout: switchSceneType@" + typeOffset
                + " targetPos@" + posOffset + " angle@" + angleOffset + ".");
            return true;
        }

        // Reverse-pinvoke body. Allocation-free: a volatile bool, four raw float reads off the `in`
        // argument, then either the refusal code or a plain forward of the untouched (self, arg)
        // pair. No Mono calls, no throw — the warp itself is queued for the main-thread tick.
        //
        // Refuses ONLY a Story transfer with a usable target. Everything else forwards to the
        // original, so the Settings "reset position" button (SwitchSceneType.Setting, no target)
        // and the fishing-ship OOB rescue keep their vanilla behaviour, and a layout surprise
        // degrades to "vanilla teleport" instead of "teleport into garbage coordinates".
        private static unsafe int InstantTeleportIsExecutableDetourBody(IntPtr self, IntPtr arg)
        {
            if (instantTeleportActive && arg != IntPtr.Zero
                && instantTeleportArgSwitchSceneTypeOffset >= 0
                && instantTeleportArgTargetPosOffset >= 0
                && instantTeleportArgAngleOffset >= 0)
            {
                byte* p = (byte*)arg;
                int switchSceneType = *(int*)(p + instantTeleportArgSwitchSceneTypeOffset);
                if (switchSceneType == InstantTeleportSwitchSceneTypeStory)
                {
                    float x = *(float*)(p + instantTeleportArgTargetPosOffset);
                    float y = *(float*)(p + instantTeleportArgTargetPosOffset + 4);
                    float z = *(float*)(p + instantTeleportArgTargetPosOffset + 8);
                    float angle = *(float*)(p + instantTeleportArgAngleOffset);
                    if (InstantTeleportIsUsableCoordinate(x)
                        && InstantTeleportIsUsableCoordinate(y)
                        && InstantTeleportIsUsableCoordinate(z)
                        && (x != 0f || y != 0f || z != 0f)
                        && !float.IsNaN(angle) && !float.IsInfinity(angle))
                    {
                        instantTeleportPendingX = x;
                        instantTeleportPendingY = y;
                        instantTeleportPendingZ = z;
                        instantTeleportPendingAngle = angle;
                        instantTeleportPendingWarp = true; // raised last — the tick reads it first
                        instantTeleportRefusedOnce = true;
                        return InstantTeleportRefuseCode; // InteractErrorCode.Invalid — silent, no tip
                    }
                }
            }

            InstantTeleportIsExecutableHookDelegate trampoline = instantTeleportTrampoline;
            if (trampoline != null)
            {
                return trampoline(self, arg);
            }

            return 0;
        }

        private static bool InstantTeleportIsUsableCoordinate(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v)
                && v > -InstantTeleportMaxCoordinate && v < InstantTeleportMaxCoordinate;
        }

        // Consumes the warp the detour body queued (same frame it refused the command).
        private void ConsumeInstantTeleportPendingWarp()
        {
            if (!instantTeleportPendingWarp)
            {
                return;
            }
            instantTeleportPendingWarp = false;

            Vector3 pos = new Vector3(instantTeleportPendingX, instantTeleportPendingY, instantTeleportPendingZ);
            // The command carries a yaw in degrees (the game's TransferToPos(pos, angle) argument).
            Quaternion rot = Quaternion.Euler(0f, instantTeleportPendingAngle, 0f);
            bool hasRot = true;

            this.instantTeleportTargetPos = pos;
            this.instantTeleportTargetRot = rot;
            this.instantTeleportHasRot = hasRot;

            if (!this.instantTeleportWarpLogged)
            {
                this.instantTeleportWarpLogged = true;
                ModLogger.Msg("[InstantTeleport] warping to " + pos.ToString("F1")
                    + " yaw=" + instantTeleportPendingAngle.ToString("F0")
                    + (this.instantTeleportWaitFieldLoaded ? " (holding until the field loads)" : " (no field wait)"));
            }

            // The mod's own warp path: game-native AuraMono transfer + CharacterController-safe
            // direct write + the short frame settle, and it redirects to the vehicle when the
            // player is driving one (a teleport point used from a car must move the car too).
            if (hasRot)
            {
                this.TeleportToLocation(pos, this.instantTeleportTargetRot);
            }
            else
            {
                this.TeleportToLocation(pos);
            }

            if (this.instantTeleportWaitFieldLoaded)
            {
                float now = Time.unscaledTime;
                this.instantTeleportHoldActive = true;
                this.instantTeleportHoldUntil = now + (this.instantTeleportFieldProbeUnavailable
                    ? InstantTeleportBlindHoldSeconds
                    : InstantTeleportHoldMaxSeconds);
                this.instantTeleportNextReassertAt = now + InstantTeleportReassertInterval;
            }
            else
            {
                this.instantTeleportHoldActive = false;
            }
        }

        // "Wait For Field Load": hold the player on the target position until the destination field
        // reports loaded (or the bound expires). Vanilla did the same thing behind its splash — it
        // warped, waited for IsFieldLoaded, then re-applied the position. The difference is that we
        // wait for the REAL signal instead of a fixed 3 s, so this is normally a fraction of a second.
        private void TickInstantTeleportFieldHold()
        {
            if (!this.instantTeleportHoldActive)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now >= this.instantTeleportHoldUntil)
            {
                this.instantTeleportHoldActive = false;
                return;
            }

            if (now < this.instantTeleportNextReassertAt)
            {
                return;
            }
            this.instantTeleportNextReassertAt = now + InstantTeleportReassertInterval;

            if (this.TryInstantTeleportIsFieldLoaded(out bool loaded) && loaded)
            {
                // Final re-assert on the frame the field came up — vanilla's second TransferToPos,
                // with the full settle this time — then release the player.
                if (this.instantTeleportHasRot)
                {
                    this.TeleportToLocation(this.instantTeleportTargetPos, this.instantTeleportTargetRot);
                }
                else
                {
                    this.TeleportToLocation(this.instantTeleportTargetPos);
                }

                this.instantTeleportHoldActive = false;
                return;
            }

            // Hold: the bare game-native warp, no frame settle — re-arming a 30-frame direct-write
            // window every 0.15 s would keep writing the transform after the hold is released.
            this.TryGameTeleportAuraMono(this.instantTeleportTargetPos,
                this.instantTeleportHasRot, this.instantTeleportTargetRot);
        }

        // EntityUtil.IsFieldLoaded(EntityUtil.GetSelfPlayer().inFieldOwnerId) — the exact pair
        // TeleportModule.WaitUntilRendered uses. Returns false (with loaded=false) when the probe
        // cannot be resolved; the caller then falls back to the blind hold.
        private unsafe bool TryInstantTeleportIsFieldLoaded(out bool loaded)
        {
            loaded = false;
            if (this.instantTeleportFieldProbeUnavailable)
            {
                return false;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                if (this.instantTeleportEntityUtilClass == IntPtr.Zero)
                {
                    this.instantTeleportEntityUtilClass = this.FindAuraMonoClassInImages(
                        "XDTLevelAndEntity.BaseSystem.EntitiesManager", "EntityUtil", InstantTeleportImageNames);
                    if (this.instantTeleportEntityUtilClass == IntPtr.Zero)
                    {
                        this.instantTeleportEntityUtilClass = this.FindAuraMonoClassByFullName(
                            "XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil");
                    }
                }
                if (this.instantTeleportEntityUtilClass == IntPtr.Zero)
                {
                    return false;
                }

                if (this.instantTeleportGetSelfPlayerMethod == IntPtr.Zero)
                {
                    this.instantTeleportGetSelfPlayerMethod = this.FindAuraMonoMethodOnHierarchy(
                        this.instantTeleportEntityUtilClass, "GetSelfPlayer", 0);
                }
                if (this.instantTeleportIsFieldLoadedMethod == IntPtr.Zero)
                {
                    this.instantTeleportIsFieldLoadedMethod = this.FindAuraMonoMethodOnHierarchy(
                        this.instantTeleportEntityUtilClass, "IsFieldLoaded", 1);
                }
                if (this.instantTeleportGetSelfPlayerMethod == IntPtr.Zero
                    || this.instantTeleportIsFieldLoadedMethod == IntPtr.Zero)
                {
                    this.instantTeleportFieldProbeUnavailable = true;
                    ModLogger.Msg("[InstantTeleport] EntityUtil.GetSelfPlayer/IsFieldLoaded unresolved —"
                        + " field-load wait falls back to a fixed " + InstantTeleportBlindHoldSeconds.ToString("0.0") + "s hold.");
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr selfPlayer = auraMonoRuntimeInvoke(this.instantTeleportGetSelfPlayerMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || selfPlayer == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetMonoUInt64Member(selfPlayer, "inFieldOwnerId", out ulong ownerId) || ownerId == 0UL)
                {
                    // No field owner (open world chunk) — nothing to wait for.
                    loaded = true;
                    return true;
                }

                uint ownerNetId = (uint)ownerId;
                exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&ownerNetId);
                IntPtr boxed = auraMonoRuntimeInvoke(this.instantTeleportIsFieldLoadedMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
                {
                    return false;
                }

                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero)
                {
                    return false;
                }

                loaded = *(byte*)raw != 0;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
