using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Self → Main "Use Game Teleport" (default on) picks the warp behind every mod teleport.
    // On: EntityHelper.AutoMoveTransfer / TransferAndLookAtForEditor (TryGameTeleportAuraMono).
    // Off: do not call those. Pin the local move component with two embedded-Mono NativeDetours.
    // No Harmony, no Transform.set_position / set_rotation patch, no IL2CPP .text patch.
    //
    // A normal player reads entity.position from the hierarchy matrix. Locomotion ends by assigning
    // localRotation, which copies that matrix back onto the unity transform, so a skeleton write
    // is gone the same tick. PlayerMoveComponent.Move then assigns entity.position from the
    // controller. The Move detour swallows that integrate while the pin is up. BaseLocomotion.Tick
    // rewrites the authority pose before and after the trampoline, because a pre-write alone loses
    // to WorldPlaceTo / entity.position += MoveDelta / the yaw sync inside the tick.
    //
    // Foot noclip and the teleport share one pose. The entity is written before noclipFootHoldValid
    // is cleared. A field-load hold is the only time the pin stays up under foot noclip; otherwise
    // the one-shot write is the whole mod teleport and noclip flies from the next tick.
    // Detours install once on the world-ready gate and stay applied. A second NativeDetour on an
    // applied slot, or Undo on a world change, corrupts the heap.
    public partial class HeartopiaComplete
    {
        private bool useGameTeleport = true;

        // Off (default): one WarpSelfTo. On: a foot teleport longer than the jump length is hops
        // of at most that many metres, with the delay between hops. 45 m stays under the server
        // delete (~50 m). Old configs keep these initializers.
        private bool interpolateTeleportJumps;
        private float interpolateTeleportJumpMeters = 15f;
        private float interpolateTeleportJumpDelay = 0.5f;

        private static volatile bool modTeleportPinActive;
        private static float modTeleportPinX;
        private static float modTeleportPinY;
        private static float modTeleportPinZ;
        private static float modTeleportPinYaw;
        private static volatile bool modTeleportPinHasRot;
        private static IntPtr modTeleportPinnedMove;
        private static uint modTeleportPinHandle;
        private static int modTeleportMoveComponentOffset = -1;

        private static readonly string[] ModTeleportLevelImages =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll"
        };

        private static readonly string[] ModTeleportEngineImages =
        {
            "EngineWrapper", "EngineWrapper.dll", "XDTBaseService", "XDTBaseService.dll"
        };

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ModTeleportMoveHookDelegate(IntPtr self, IntPtr motion);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ModTeleportTickHookDelegate(IntPtr self, float time, float deltaTime);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ModTeleportCompileMethodDelegate(IntPtr method);

        private static MonoMod.RuntimeDetour.NativeDetour modTeleportMoveDetour;
        private static ModTeleportMoveHookDelegate modTeleportMoveKeepAlive;
        private static ModTeleportMoveHookDelegate modTeleportMoveTrampoline;
        private static bool modTeleportMoveDetourApplied;

        private static MonoMod.RuntimeDetour.NativeDetour modTeleportTickDetour;
        private static ModTeleportTickHookDelegate modTeleportTickKeepAlive;
        private static ModTeleportTickHookDelegate modTeleportTickTrampoline;
        private static bool modTeleportTickDetourApplied;

        private static IntPtr modTeleportHierarchySetMethod;
        // PlayerMoveComponent.SetPositionAndRotation(Vector3, Quaternion, bool) — the call noclip
        // already makes. The hierarchy `in` setter and XDCollider.set_position stay resolved for
        // install, and the write does not invoke them: that icall killed the process
        // (ExecutionEngineException, empty managed stack, 2026-11-09 dump).
        private static IntPtr modTeleportMoveSetPosRotMethod;
        private static IntPtr modTeleportColliderSetPositionMethod;
        private static int modTeleportControllerOffset = -1;
        private static int modTeleportCurrYawOffset = -1;
        private static int modTeleportVelocityOffset = -1;
        private static int modTeleportRealVelocityOffset = -1;
        private static int modTeleportAccelerationOffset = -1;
        private static int modTeleportRealSpeedOffset = -1;
        private static int modTeleportMoveSpeedOffset = -1;
        private static int modTeleportAccOffset = -1;
        private static int modTeleportMovingBlockRateOffset = -1;
        private static int modTeleportGravityOffset = -1;

        private bool modTeleportCallbacksRegistered;
        private bool modTeleportMoveMissingLogged;
        private static bool modTeleportAuthorityFailLogged;
        private static bool modTeleportSetterMissingLogged;
        private static bool modTeleportMoveOffsetMissingLogged;
        private static bool modTeleportVelocityGapLogged;
        private static bool modTeleportInstallFailLogged;
        private static bool modTeleportSignatureMissLogged;

        // Registered once from OnUpdate. The ready callback retries itself until both detours are
        // applied. The loading callback is append-only, so a second register would disarm twice.
        private void RequestModTeleportHooks()
        {
            if (this.modTeleportCallbacksRegistered)
            {
                return;
            }

            this.modTeleportCallbacksRegistered = true;
            this.RegisterWorldReadyCallback("ModTeleport", this.TryInstallModTeleportOnWorldReady);
            this.RegisterWorldLoadingStartedCallback(this.OnModTeleportWorldLoadingStarted);
        }

        private void OnModTeleportWorldLoadingStarted()
        {
            this.DisarmModTeleportPin();
            // A load drops the chain in place. No warp to the stored target, and no courier End:
            // the ride belongs to the world that is going away.
            this.DropInstantTeleportDeferredTransferEnd();
            this.FinishTeleportJumpChain();
        }

        // True only when both detours are applied. A slot that is already applied is not compiled
        // and not detoured again. Missing pieces return false so the gate retries. Never Undo a
        // detour that already has a trampoline.
        private bool TryInstallModTeleportOnWorldReady()
        {
            if (modTeleportMoveDetourApplied && modTeleportTickDetourApplied)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoFieldGetOffset == null)
                {
                    return false;
                }

                if (!this.TryResolveModTeleportInstallTargets())
                {
                    return false;
                }

                // Each slot is independent. A retry compiles only a slot that is not applied yet.
                bool moveReady = modTeleportMoveDetourApplied || this.TryInstallModTeleportMoveDetour();
                bool tickReady = false;
                if (modTeleportMoveComponentOffset >= 0)
                {
                    tickReady = modTeleportTickDetourApplied || this.TryInstallModTeleportTickDetour();
                }

                return moveReady && tickReady;
            }
            catch (Exception ex)
            {
                if (!modTeleportInstallFailLogged)
                {
                    modTeleportInstallFailLogged = true;
                    ModLogger.Msg("[ModTeleport] detour install failed: " + ex.Message);
                }

                return false;
            }
        }

        private bool TryResolveModTeleportInstallTargets()
        {
            bool authorityReady = modTeleportHierarchySetMethod != IntPtr.Zero
                && modTeleportColliderSetPositionMethod != IntPtr.Zero
                && modTeleportCurrYawOffset >= 0
                && modTeleportControllerOffset >= 0;
            if (authorityReady && modTeleportMoveComponentOffset >= 0)
            {
                return true;
            }

            IntPtr moveClass = this.FindModTeleportClass(
                "XDTLevelAndEntity.Gameplay.Component.Player.PlayerMoveComponent",
                "XDTLevelAndEntity.Gameplay.Component.Player",
                "PlayerMoveComponent",
                ModTeleportLevelImages);
            IntPtr locomotionClass = this.FindModTeleportClass(
                "XDTLevelAndEntity.Gameplay.Locomotion.BaseLocomotion",
                "XDTLevelAndEntity.Gameplay.Locomotion",
                "BaseLocomotion",
                ModTeleportLevelImages);
            IntPtr hierarchyClass = this.FindModTeleportClass(
                "XDTGame.Core.Hierarchy",
                "XDTGame.Core",
                "Hierarchy",
                new[] { "XDTBaseService", "XDTBaseService.dll", "XDTLevelAndEntity", "XDTLevelAndEntity.dll" });
            IntPtr colliderClass = this.FindModTeleportClass(
                "XDT.Physics.XDCollider",
                "XDT.Physics",
                "XDCollider",
                ModTeleportEngineImages);
            IntPtr movementClass = this.FindModTeleportClass(
                "XDTLevelAndEntity.Gameplay.Component.MovementComponent",
                "XDTLevelAndEntity.Gameplay.Component",
                "MovementComponent",
                ModTeleportLevelImages);
            if (moveClass == IntPtr.Zero || locomotionClass == IntPtr.Zero
                || hierarchyClass == IntPtr.Zero || colliderClass == IntPtr.Zero)
            {
                return authorityReady;
            }

            if (modTeleportHierarchySetMethod == IntPtr.Zero)
            {
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(hierarchyClass, "SetPositionAndRotation", 2);
                if (method != IntPtr.Zero && AuraMonoMethodParamCountIs(method, 2))
                {
                    modTeleportHierarchySetMethod = method;
                }
            }

            if (modTeleportColliderSetPositionMethod == IntPtr.Zero)
            {
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(colliderClass, "set_position", 1);
                if (method != IntPtr.Zero && AuraMonoMethodParamCountIs(method, 1))
                {
                    modTeleportColliderSetPositionMethod = method;
                }
            }

            if (modTeleportHierarchySetMethod == IntPtr.Zero || modTeleportColliderSetPositionMethod == IntPtr.Zero)
            {
                if (!modTeleportSetterMissingLogged)
                {
                    modTeleportSetterMissingLogged = true;
                    ModLogger.Msg("[ModTeleport] hierarchy SetPositionAndRotation or XDCollider.set_position"
                        + " is missing — detours stay down until both resolve.");
                }

                return false;
            }

            if (modTeleportControllerOffset < 0)
            {
                modTeleportControllerOffset = this.ReadModTeleportFieldOffset(moveClass, "_controller");
            }

            if (modTeleportCurrYawOffset < 0)
            {
                IntPtr yawClass = movementClass != IntPtr.Zero ? movementClass : moveClass;
                modTeleportCurrYawOffset = this.ReadModTeleportFieldOffset(yawClass, "_currYaw");
            }

            if (modTeleportControllerOffset < 0 || modTeleportCurrYawOffset < 0)
            {
                if (!modTeleportSetterMissingLogged)
                {
                    modTeleportSetterMissingLogged = true;
                    ModLogger.Msg("[ModTeleport] _controller or _currYaw offset is missing — detours stay down.");
                }

                return false;
            }

            if (modTeleportMoveComponentOffset < 0)
            {
                modTeleportMoveComponentOffset = this.ReadModTeleportFieldOffset(
                    locomotionClass, "<moveComponent>k__BackingField");
                if (modTeleportMoveComponentOffset < 0 && !modTeleportMoveOffsetMissingLogged)
                {
                    modTeleportMoveOffsetMissingLogged = true;
                    ModLogger.Msg("[ModTeleport] BaseLocomotion.<moveComponent>k__BackingField offset is missing"
                        + " — Tick detour stays down.");
                }
            }

            IntPtr velocityClass = movementClass != IntPtr.Zero ? movementClass : moveClass;
            this.ResolveModTeleportVelocityOffsets(velocityClass);
            return modTeleportHierarchySetMethod != IntPtr.Zero
                && modTeleportColliderSetPositionMethod != IntPtr.Zero
                && modTeleportCurrYawOffset >= 0
                && modTeleportControllerOffset >= 0;
        }

        private void ResolveModTeleportVelocityOffsets(IntPtr movementClass)
        {
            if (movementClass == IntPtr.Zero || modTeleportVelocityGapLogged)
            {
                return;
            }

            modTeleportVelocityOffset = this.KeepModTeleportOffset(modTeleportVelocityOffset, movementClass, "_velocity");
            modTeleportRealVelocityOffset = this.KeepModTeleportOffset(modTeleportRealVelocityOffset, movementClass, "_realVelocity");
            modTeleportAccelerationOffset = this.KeepModTeleportOffset(modTeleportAccelerationOffset, movementClass, "_acceleration");
            modTeleportRealSpeedOffset = this.KeepModTeleportOffset(modTeleportRealSpeedOffset, movementClass, "_realSpeed");
            modTeleportMoveSpeedOffset = this.KeepModTeleportOffset(modTeleportMoveSpeedOffset, movementClass, "_moveSpeed");
            modTeleportAccOffset = this.KeepModTeleportOffset(modTeleportAccOffset, movementClass, "_acc");
            modTeleportMovingBlockRateOffset = this.KeepModTeleportOffset(modTeleportMovingBlockRateOffset, movementClass, "_movingBlockRate");
            modTeleportGravityOffset = this.KeepModTeleportOffset(modTeleportGravityOffset, movementClass, "_gravity");

            bool anyMissing = modTeleportVelocityOffset < 0 || modTeleportRealVelocityOffset < 0
                || modTeleportAccelerationOffset < 0 || modTeleportRealSpeedOffset < 0
                || modTeleportMoveSpeedOffset < 0 || modTeleportAccOffset < 0
                || modTeleportMovingBlockRateOffset < 0 || modTeleportGravityOffset < 0;
            if (!anyMissing)
            {
                return;
            }

            // A missing velocity field is skipped at write time. It does not fail the install.
            modTeleportVelocityGapLogged = true;
            ModLogger.Msg("[ModTeleport] one or more ResetProperties velocity fields are missing"
                + " — those fields are left untouched.");
        }

        private int KeepModTeleportOffset(int current, IntPtr classPtr, string fieldName)
        {
            if (current >= 0)
            {
                return current;
            }

            return this.ReadModTeleportFieldOffset(classPtr, fieldName);
        }

        private int ReadModTeleportFieldOffset(IntPtr classPtr, string fieldName)
        {
            if (classPtr == IntPtr.Zero || auraMonoFieldGetOffset == null)
            {
                return -1;
            }

            IntPtr field = this.FindAuraMonoFieldOnHierarchy(classPtr, fieldName);
            if (field == IntPtr.Zero)
            {
                return -1;
            }

            // mono_field_get_offset includes the object header. The hook holds the object pointer,
            // so the offset is used as-is. The 2*IntPtr subtraction is only for unboxed structs.
            int offset = (int)auraMonoFieldGetOffset(field);
            return offset > 0 ? offset : -1;
        }

        private IntPtr FindModTeleportClass(string fullName, string nameSpace, string className, string[] images)
        {
            IntPtr cls = this.FindAuraMonoClassByFullName(fullName);
            if (cls != IntPtr.Zero)
            {
                return cls;
            }

            return this.FindAuraMonoClassInImages(nameSpace, className, images);
        }

        private bool TryInstallModTeleportMoveDetour()
        {
            if (modTeleportMoveDetourApplied)
            {
                return true;
            }

            IntPtr moveClass = this.FindModTeleportClass(
                "XDTLevelAndEntity.Gameplay.Component.Player.PlayerMoveComponent",
                "XDTLevelAndEntity.Gameplay.Component.Player",
                "PlayerMoveComponent",
                ModTeleportLevelImages);
            if (moveClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(moveClass, "Move", 1);
            if (method == IntPtr.Zero || !AuraMonoMethodParamCountIs(method, 1))
            {
                this.LogModTeleportSignatureOnce("PlayerMoveComponent.Move(1)");
                return false;
            }

            IntPtr nativePtr = this.CompileModTeleportMethod(method);
            if (nativePtr == IntPtr.Zero)
            {
                return false;
            }

            modTeleportMoveKeepAlive = ModTeleportMoveDetourBody;
            if (!this.TryCreateModTeleportDetour(nativePtr, modTeleportMoveKeepAlive, true, out ModTeleportMoveHookDelegate trampoline))
            {
                return false;
            }

            modTeleportMoveTrampoline = trampoline;
            ModLogger.Msg("[ModTeleport] Hooked PlayerMoveComponent.Move @0x" + nativePtr.ToInt64().ToString("X"));
            return true;
        }

        private bool TryInstallModTeleportTickDetour()
        {
            if (modTeleportTickDetourApplied)
            {
                return true;
            }

            if (modTeleportMoveComponentOffset < 0)
            {
                return false;
            }

            IntPtr locomotionClass = this.FindModTeleportClass(
                "XDTLevelAndEntity.Gameplay.Locomotion.BaseLocomotion",
                "XDTLevelAndEntity.Gameplay.Locomotion",
                "BaseLocomotion",
                ModTeleportLevelImages);
            if (locomotionClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(locomotionClass, "Tick", 2);
            if (method == IntPtr.Zero || !AuraMonoMethodParamCountIs(method, 2))
            {
                this.LogModTeleportSignatureOnce("BaseLocomotion.Tick(2)");
                return false;
            }

            IntPtr nativePtr = this.CompileModTeleportMethod(method);
            if (nativePtr == IntPtr.Zero)
            {
                return false;
            }

            modTeleportTickKeepAlive = ModTeleportTickDetourBody;
            if (!this.TryCreateModTeleportDetour(nativePtr, modTeleportTickKeepAlive, false, out ModTeleportTickHookDelegate trampoline))
            {
                return false;
            }

            modTeleportTickTrampoline = trampoline;
            ModLogger.Msg("[ModTeleport] Hooked BaseLocomotion.Tick @0x" + nativePtr.ToInt64().ToString("X")
                + " (moveComponent@" + modTeleportMoveComponentOffset + ")");
            return true;
        }

        // Install rollback only. The applied flag is set after the trampoline exists. If the
        // constructor ran and the trampoline did not, Undo that not-yet-live slot so a later
        // attempt can compile it. Undo failure marks the slot applied: a second NativeDetour on
        // it is the same heap corruption as Undo on a world change. A live trampoline is never undone.
        private bool TryCreateModTeleportDetour<T>(IntPtr nativePtr, Delegate keepAlive, bool moveSlot, out T trampoline)
            where T : Delegate
        {
            trampoline = default(T);
            MonoMod.RuntimeDetour.NativeDetour detour = null;
            try
            {
                detour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, keepAlive);
                trampoline = detour.GenerateTrampoline<T>();
            }
            catch (Exception ex)
            {
                trampoline = default(T);
                if (!this.UndoUnappliedModTeleportDetour(detour, moveSlot))
                {
                    return false;
                }

                if (!modTeleportInstallFailLogged)
                {
                    modTeleportInstallFailLogged = true;
                    ModLogger.Msg("[ModTeleport] detour install failed: " + ex.Message);
                }

                return false;
            }

            if (trampoline == null)
            {
                bool reverted = this.UndoUnappliedModTeleportDetour(detour, moveSlot);
                if (reverted && !modTeleportInstallFailLogged)
                {
                    modTeleportInstallFailLogged = true;
                    ModLogger.Msg("[ModTeleport] trampoline unavailable — detour reverted, retrying the open slot.");
                }

                return false;
            }

            if (moveSlot)
            {
                modTeleportMoveDetour = detour;
                modTeleportMoveDetourApplied = true;
            }
            else
            {
                modTeleportTickDetour = detour;
                modTeleportTickDetourApplied = true;
            }

            return true;
        }

        private bool UndoUnappliedModTeleportDetour(MonoMod.RuntimeDetour.NativeDetour detour, bool moveSlot)
        {
            if (detour == null)
            {
                return true;
            }

            try
            {
                detour.Undo();
                return true;
            }
            catch
            {
                if (moveSlot)
                {
                    modTeleportMoveDetour = detour;
                    modTeleportMoveDetourApplied = true;
                }
                else
                {
                    modTeleportTickDetour = detour;
                    modTeleportTickDetourApplied = true;
                }

                ModLogger.Msg("[ModTeleport] trampoline missing and Undo failed — that slot will not be detoured again.");
                return false;
            }
        }

        private void LogModTeleportSignatureOnce(string what)
        {
            if (modTeleportSignatureMissLogged)
            {
                return;
            }

            modTeleportSignatureMissLogged = true;
            ModLogger.Msg("[ModTeleport] " + what + " not found — that detour stays down.");
        }

        private IntPtr CompileModTeleportMethod(IntPtr method)
        {
            IntPtr monoModule = this.GetAuraMonoModuleHandle();
            ModTeleportCompileMethodDelegate compile = monoModule != IntPtr.Zero
                ? this.GetAuraMonoExport<ModTeleportCompileMethodDelegate>(monoModule, "mono_compile_method")
                : null;
            if (compile == null || method == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return compile(method);
        }

        // Foot hops. Scheduled from OnUpdate (unscaled time). Hops call WarpSelfTo only —
        // TeleportToLocation would recurse. The field-hold reassert does not run while a chain
        // is active. TeleportTo stays one shot and does not open a settle over the chain.
        private bool teleportJumpChainActive;
        private bool teleportJumpChainFinishAfterGap;
        private Vector3 teleportJumpChainTarget;
        private bool teleportJumpChainHasRot;
        private Quaternion teleportJumpChainRot;
        private Vector3 teleportJumpChainLast;
        private float teleportJumpChainNextAt;
        // Courier End owed by an instant teleport whose first hop started a chain. Sent when that
        // chain actually ends. A world load drops it without sending.
        private bool instantTeleportDeferredTransferEnd;

        private bool WarpSelfTo(Vector3 target, bool hasRot, Quaternion rot)
        {
            if (this.useGameTeleport)
            {
                return this.TryGameTeleportAuraMono(target, hasRot, rot);
            }

            return this.ArmModTeleportPin(target, hasRot, rot);
        }

        // False: the caller does today's one WarpSelfTo plus settle. True: hop 0 already ran.
        // A TeleportToLocation that does not start a chain drops any chain already in flight so
        // the single warp is the only move (a second teleport replaces the previous target).
        private bool TryStartTeleportJumpChain(Vector3 target, bool hasRot, Quaternion rot)
        {
            if (!this.interpolateTeleportJumps || this.useGameTeleport || this.IsPlayerDrivingVehicle())
            {
                this.AbandonTeleportJumpChainForSingleWarp();
                return false;
            }

            if (!this.TryGetNoclipSelfAnchorPose(out Vector3 self, out _, out _, out _))
            {
                this.AbandonTeleportJumpChainForSingleWarp();
                return false;
            }

            float jump = Mathf.Clamp(this.interpolateTeleportJumpMeters, 1f, 45f);
            if (Vector3.Distance(self, target) <= jump)
            {
                this.AbandonTeleportJumpChainForSingleWarp();
                return false;
            }

            this.teleportJumpChainTarget = target;
            this.teleportJumpChainHasRot = hasRot;
            this.teleportJumpChainRot = rot;
            this.teleportJumpChainLast = self;
            this.teleportJumpChainFinishAfterGap = false;
            this.teleportJumpChainActive = true;
            // A settle left by an earlier warp would keep writing that point and, at zero,
            // disarm the pin under the hops. The last hop opens a new settle when it lands.
            this.teleportFramesRemaining = 0;
            this.playerRotationFramesRemaining = 0;
            this.FireTeleportJumpHop();
            return true;
        }

        private void AbandonTeleportJumpChainForSingleWarp()
        {
            if (this.teleportJumpChainActive)
            {
                this.FinishTeleportJumpChain();
            }
        }

        private void ProcessTeleportJumpChainOnUpdate()
        {
            if (!this.teleportJumpChainActive)
            {
                return;
            }

            // Mounting replaces the foot chain. No foot warp onto the vehicle.
            if (this.IsPlayerDrivingVehicle())
            {
                this.FinishTeleportJumpChain();
                return;
            }

            // The server may still be deleting the player. Do not warp, and do not burn the delay.
            // The next update after the gap closes continues toward the same target.
            if (IsSelfPlayerRespawnGap)
            {
                return;
            }

            // Toggle-off, or Use Game Teleport flipped on, during the gap: one warp to the stored
            // final target once the player exists. Game teleport is a single server warp, so a
            // chain still in flight must not keep hopping after that switch is on.
            if (this.useGameTeleport || this.teleportJumpChainFinishAfterGap)
            {
                this.LandTeleportJumpChainAtTarget();
                return;
            }

            if (Time.unscaledTime < this.teleportJumpChainNextAt)
            {
                return;
            }

            this.FireTeleportJumpHop();
        }

        private void FireTeleportJumpHop()
        {
            float jump = Mathf.Clamp(this.interpolateTeleportJumpMeters, 1f, 45f);
            float delay = Mathf.Clamp(this.interpolateTeleportJumpDelay, 0.1f, 5f);
            Vector3 position = this.teleportJumpChainLast;
            if (this.TryGetNoclipSelfAnchorPose(out Vector3 self, out _, out _, out _))
            {
                position = self;
            }

            Vector3 target = this.teleportJumpChainTarget;
            Vector3 toTarget = target - position;
            float dist = toTarget.magnitude;
            if (float.IsNaN(dist) || float.IsInfinity(dist) || dist <= jump + 0.05f)
            {
                this.LandTeleportJumpChainAtTarget();
                return;
            }

            Vector3 hop = position + (toTarget / dist) * jump;
            this.WarpSelfTo(hop, false, Quaternion.identity);
            this.teleportJumpChainLast = hop;
            this.teleportJumpChainNextAt = Time.unscaledTime + delay;
        }

        // Last hop and the toggle-off landing. Settle matches TeleportToLocation, then the chain
        // ends. Stealth re-pins after that clear: FarmTeleportTo pins the dive at hop 0, and the
        // clear would otherwise drop it before noclip runs.
        private void LandTeleportJumpChainAtTarget()
        {
            Vector3 target = this.teleportJumpChainTarget;
            bool hasRot = this.teleportJumpChainHasRot;
            Quaternion rot = this.teleportJumpChainRot;
            // Captured before the warp: afterwards the 2.5 m check can reject our own body.
            GameObject skeleton = this.GetVerifiedLocalPlayerSkeleton();
            this.WarpSelfTo(target, hasRot, rot);
            if (skeleton != null && !this.FootNoclipOwnsTeleportLanding())
            {
                this.teleportSyncPosition = target;
                this.teleportSyncSkeleton = skeleton;
                this.noclipFootHoldValid = false;
                CharacterController component = skeleton.GetComponent<CharacterController>();
                if (component != null)
                {
                    component.enabled = false;
                }

                skeleton.transform.position = target;
                this.teleportFramesRemaining = 30;
                if (hasRot)
                {
                    this.teleportSyncRotation = rot;
                    skeleton.transform.rotation = rot;
                    this.playerRotationFramesRemaining = 30;
                }

                if (component != null)
                {
                    component.enabled = true;
                }
            }
            else if (skeleton != null)
            {
                this.teleportFramesRemaining = 0;
                this.playerRotationFramesRemaining = 0;
            }

            this.FinishTeleportJumpChain();
            if (this.StealthForagingActive)
            {
                this.PinStealthForagingNoclipHold(target);
            }
        }

        private void FinishTeleportJumpChain()
        {
            bool wasActive = this.teleportJumpChainActive;
            this.teleportJumpChainActive = false;
            this.teleportJumpChainFinishAfterGap = false;
            if (!wasActive)
            {
                return;
            }

            if (this.noclipEnabled)
            {
                this.noclipFootHoldValid = false;
            }

            this.FlushInstantTeleportDeferredTransferEnd();
        }

        private void FlushInstantTeleportDeferredTransferEnd()
        {
            if (!this.instantTeleportDeferredTransferEnd)
            {
                return;
            }

            this.instantTeleportDeferredTransferEnd = false;
            this.SendInstantTeleportTransferEnd();
        }

        private void DropInstantTeleportDeferredTransferEnd()
        {
            this.instantTeleportDeferredTransferEnd = false;
        }

        // Foot noclip owns the landing unless the instant-teleport field hold is keeping the
        // player on an unloaded field. Vehicle noclip is not this path.
        private bool FootNoclipOwnsTeleportLanding()
        {
            return this.noclipEnabled && !this.IsPlayerDrivingVehicle() && !this.instantTeleportHoldActive;
        }

        private bool ArmModTeleportPin(Vector3 target, bool hasRot, Quaternion rot)
        {
            if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj)
                || playerObj == IntPtr.Zero
                || !this.TryGetBunnyHopMonoMoveComponent(playerObj, out IntPtr move)
                || move == IntPtr.Zero)
            {
                if (!this.modTeleportMoveMissingLogged)
                {
                    this.modTeleportMoveMissingLogged = true;
                    ModLogger.Msg("[ModTeleport] local move component unavailable");
                }

                return false;
            }

            // Coordinates, then the object pin, then the flag. The flag is set only after the
            // authority write below, and only when the pin must hold across ticks.
            modTeleportPinX = target.x;
            modTeleportPinY = target.y;
            modTeleportPinZ = target.z;
            modTeleportPinYaw = hasRot ? rot.eulerAngles.y : 0f;
            modTeleportPinHasRot = hasRot;

            if (move != modTeleportPinnedMove)
            {
                // Drop the flag before freeing the previous object. The new pin is published
                // only after AuraMonoPinNew returns a handle.
                ReleaseModTeleportPin();
                modTeleportPinX = target.x;
                modTeleportPinY = target.y;
                modTeleportPinZ = target.z;
                modTeleportPinYaw = hasRot ? rot.eulerAngles.y : 0f;
                modTeleportPinHasRot = hasRot;

                uint handle = AuraMonoPinNew(move);
                if (handle == 0U)
                {
                    LogModTeleportAuthorityOnce();
                    return false;
                }

                modTeleportPinHandle = handle;
                modTeleportPinnedMove = move;
            }

            bool wrote;
            try
            {
                wrote = WriteModTeleportAuthority(move);
            }
            catch
            {
                wrote = false;
            }

            if (!wrote)
            {
                ReleaseModTeleportPin();
                LogModTeleportAuthorityOnce();
                return false;
            }

            if (this.FootNoclipOwnsTeleportLanding())
            {
                modTeleportPinActive = false;
                return true;
            }

            modTeleportPinActive = true;
            return true;
        }

        private void DisarmModTeleportPin()
        {
            ReleaseModTeleportPin();
            if (this.noclipEnabled)
            {
                this.noclipFootHoldValid = false;
            }
        }

        // Clear the flag before freeing the pin. Hooks read the flag first.
        private static void ReleaseModTeleportPin()
        {
            modTeleportPinActive = false;
            uint handle = modTeleportPinHandle;
            modTeleportPinHandle = 0U;
            modTeleportPinnedMove = IntPtr.Zero;
            if (handle != 0U)
            {
                AuraMonoPinFree(handle);
            }
        }

        private static void LogModTeleportAuthorityOnce()
        {
            if (modTeleportAuthorityFailLogged)
            {
                return;
            }

            modTeleportAuthorityFailLogged = true;
            ModLogger.Msg("[ModTeleport] authority write failed");
        }

        // One managed call, the noclip rung: PlayerMoveComponent.SetPositionAndRotation disables the
        // native controller, writes the hierarchy, re-enables the controller, and ResetMove copies
        // yaw and zeroes velocity. Called from the main thread before the pin flag is set, never
        // from a detour body (that re-enters mono_runtime_invoke from inside the hooked tick).
        private static bool WriteModTeleportAuthority(IntPtr move)
        {
            if (move == IntPtr.Zero || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return false;
            }

            HeartopiaComplete host = HeartopiaComplete.Instance;
            if (host == null)
            {
                return false;
            }

            if (modTeleportMoveSetPosRotMethod == IntPtr.Zero)
            {
                IntPtr moveClass = auraMonoObjectGetClass(move);
                if (moveClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr method = host.FindAuraMonoMethodOnHierarchy(moveClass, "SetPositionAndRotation", 3);
                if (method == IntPtr.Zero || !AuraMonoMethodParamCountIs(method, 3))
                {
                    return false;
                }

                modTeleportMoveSetPosRotMethod = method;
            }

            if (!TryReadModTeleportYaw(host, move, out float yaw))
            {
                return false;
            }

            return InvokeModTeleportMoveSetPosRot(
                move,
                new Vector3(modTeleportPinX, modTeleportPinY, modTeleportPinZ),
                Quaternion.Euler(0f, yaw, 0f));
        }

        private static unsafe bool InvokeModTeleportMoveSetPosRot(IntPtr move, Vector3 position, Quaternion rotation)
        {
            Vector3 pos = position;
            Quaternion rot = rotation;
            bool worldSpace = true;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&pos);
            args[1] = (IntPtr)(&rot);
            args[2] = (IntPtr)(&worldSpace);
            auraMonoRuntimeInvoke(modTeleportMoveSetPosRotMethod, move, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        private static bool TryReadModTeleportYaw(HeartopiaComplete host, IntPtr move, out float yaw)
        {
            if (modTeleportPinHasRot)
            {
                yaw = modTeleportPinYaw;
                return true;
            }

            yaw = 0f;
            if (host.TryGetMonoObjectMember(move, "entity", out IntPtr entity)
                && entity != IntPtr.Zero
                && host.TryGetMonoQuaternionMember(entity, "rotation", out Quaternion rotation))
            {
                yaw = rotation.eulerAngles.y;
                return true;
            }

            return TryReadModTeleportFloat(move, modTeleportCurrYawOffset, out yaw);
        }

        private static unsafe bool TryReadModTeleportFloat(IntPtr obj, int offset, out float value)
        {
            value = 0f;
            if (obj == IntPtr.Zero || offset < 0)
            {
                return false;
            }

            value = *(float*)((byte*)obj + offset);
            return true;
        }

        private static void ModTeleportMoveDetourBody(IntPtr self, IntPtr motion)
        {
            if (!modTeleportPinActive || self == IntPtr.Zero || self != modTeleportPinnedMove)
            {
                ModTeleportMoveHookDelegate trampoline = modTeleportMoveTrampoline;
                if (trampoline != null)
                {
                    trampoline(self, motion);
                }

                return;
            }

            // Pin frame. Do not integrate. Move would controller.Move(motion)
            // and then assign entity.position = controller.position.
        }

        private static void ModTeleportTickDetourBody(IntPtr self, float time, float deltaTime)
        {
            if (!modTeleportPinActive || self == IntPtr.Zero || modTeleportMoveComponentOffset < 0)
            {
                ModTeleportTickForward(self, time, deltaTime);
                return;
            }

            IntPtr move = Marshal.ReadIntPtr(self, modTeleportMoveComponentOffset);
            if (move != modTeleportPinnedMove)
            {
                ModTeleportTickForward(self, time, deltaTime);
                return;
            }

            // The landing was written on the main thread before the flag was raised.
            // Do not invoke from here: this body runs inside the hooked tick, and a
            // mono_runtime_invoke from that point re-enters the runtime.
            ModTeleportTickForward(self, time, deltaTime);
        }

        private static void ModTeleportTickForward(IntPtr self, float time, float deltaTime)
        {
            ModTeleportTickHookDelegate trampoline = modTeleportTickTrampoline;
            if (trampoline != null)
            {
                trampoline(self, time, deltaTime);
            }
        }
    }
}
