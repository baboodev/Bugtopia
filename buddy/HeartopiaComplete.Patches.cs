﻿using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using UnityObject = UnityEngine.Object;
using Il2CppType = Il2CppSystem.Type;
using Il2CppFieldInfo = Il2CppSystem.Reflection.FieldInfo;
using Il2CppMethodInfo = Il2CppSystem.Reflection.MethodInfo;
using Il2CppPropertyInfo = Il2CppSystem.Reflection.PropertyInfo;
using Il2CppBindingFlags = Il2CppSystem.Reflection.BindingFlags;
using Il2CppObject = Il2CppSystem.Object;
using Object = UnityEngine.Object;


namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private void ApplyMasterConsoleVisibility()
        {
            if (!MasterHideLoaderConsole)
            {
                return;
            }

            ModCoroutines.Start(this.HideLoaderConsoleRoutine());
        }

        private void SetGameSpeed(float speed)
        {
            this.gameSpeed = Mathf.Clamp(speed, 1f, 10f);
            this.ApplyGameSpeed(true);
        }

        private void ApplyGameSpeed(bool force = false)
        {
            float speed = Mathf.Clamp(this.gameSpeed, 1f, 10f);
            if (!this.gameTimingCaptured)
            {
                this.baseFixedDeltaTime = Mathf.Max(0.001f, Time.fixedDeltaTime);
                this.baseMaximumDeltaTime = Mathf.Max(this.baseFixedDeltaTime, Time.maximumDeltaTime);
                this.gameTimingCaptured = true;
            }

            if (!force && Math.Abs(Time.timeScale - speed) <= 0.05f && Math.Abs(this.lastAppliedGameSpeed - speed) <= 0.001f)
            {
                return;
            }

            this.gameSpeed = speed;
            Time.timeScale = speed;

            // Keep real-time physics frequency stable at high speed instead of multiplying fixed updates per frame.
            Time.fixedDeltaTime = this.baseFixedDeltaTime * speed;
            Time.maximumDeltaTime = Mathf.Max(this.baseMaximumDeltaTime, Time.fixedDeltaTime * 2f);
            this.lastAppliedGameSpeed = speed;
        }

        private void UpdateHotPathOverrideTargetIds()
        {
            try
            {
                if (HeartopiaComplete.OverridePlayerPosition || HeartopiaComplete.OverridePlayerRotation || this.noclipEnabled)
                {
                    GameObject local = HeartopiaComplete.GetLocalPlayer();
                    HeartopiaComplete.OverridePlayerTransformId = local != null ? local.transform.GetInstanceID() : 0;
                }
                else
                {
                    HeartopiaComplete.OverridePlayerTransformId = 0;
                }

                if (HeartopiaComplete.OverrideCameraPosition || this.mouseLookEnabled)
                {
                    Camera cam = Camera.main;
                    HeartopiaComplete.OverrideCameraTransformId = cam != null ? cam.transform.GetInstanceID() : 0;
                }
                else
                {
                    HeartopiaComplete.OverrideCameraTransformId = 0;
                }
            }
            catch
            {
            }
        }

        private void MaybeUnpatchIdleHotPathPatches(float now)
        {
            if (this.positionOverridePatched && now - this.positionOverridePatchLastNeededAt > HotPathPatchIdleUnpatchSeconds)
            {
                this.UnpatchPositionOverride();
            }
            if (this.rotationOverridePatched && now - this.rotationOverridePatchLastNeededAt > HotPathPatchIdleUnpatchSeconds)
            {
                this.UnpatchRotationOverride();
            }
            if (this.inputSimPatched && now - this.inputSimPatchLastNeededAt > HotPathPatchIdleUnpatchSeconds)
            {
                this.UnpatchInputSim();
            }
        }

        private void UnpatchPositionOverride()
        {
            this.positionOverridePatched = false;
            try
            {
                var harmony = HeartopiaComplete.harmonyInstance;
                if (harmony == null) return;
                MethodInfo posSetter = typeof(Transform).GetProperty("position").GetSetMethod();
                MethodInfo posPrefix = typeof(TransformPositionPatch).GetMethod("SetPositionPrefix");
                if (posSetter != null && posPrefix != null)
                {
                    harmony.Unpatch(posSetter, posPrefix);
                }
                ModLogger.Msg("[Patch] Position override removed (idle).");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Patch] Position override unpatch failed: " + ex.Message);
            }
        }

        private void UnpatchRotationOverride()
        {
            this.rotationOverridePatched = false;
            try
            {
                var harmony = HeartopiaComplete.harmonyInstance;
                if (harmony == null) return;
                MethodInfo rotSetter = typeof(Transform).GetProperty("rotation").GetSetMethod();
                MethodInfo camRotPrefix = typeof(TransformRotationPatch).GetMethod("SetRotationPrefix");
                MethodInfo playerRotPrefix = typeof(CharacterRotationPatch).GetMethod("SetRotationPrefix");
                if (rotSetter != null && camRotPrefix != null)
                {
                    harmony.Unpatch(rotSetter, camRotPrefix);
                }
                if (rotSetter != null && playerRotPrefix != null)
                {
                    harmony.Unpatch(rotSetter, playerRotPrefix);
                }
                ModLogger.Msg("[Patch] Rotation override removed (idle).");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Patch] Rotation override unpatch failed: " + ex.Message);
            }
        }

        private void EnsurePositionOverridePatched()
        {
            if (this.positionOverridePatched) return;
            this.positionOverridePatched = true; // set first so a failed attempt is not retried every frame
            try
            {
                var harmony = HeartopiaComplete.harmonyInstance;
                if (harmony == null) { this.positionOverridePatched = false; return; }

                MethodInfo posSetter = typeof(Transform).GetProperty("position").GetSetMethod();
                MethodInfo posPrefix = typeof(TransformPositionPatch).GetMethod("SetPositionPrefix");
                if (posSetter != null && posPrefix != null)
                {
                    harmony.Patch(posSetter, new HarmonyMethod(posPrefix), null, null, null, null);
                }

                ModLogger.Msg("[Patch] Position override installed (Transform.position setter).");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Patch] Position override patch failed: " + ex.Message);
            }
        }

        private void EnsureRotationOverridePatched()
        {
            if (this.rotationOverridePatched) return;
            this.rotationOverridePatched = true;
            try
            {
                var harmony = HeartopiaComplete.harmonyInstance;
                if (harmony == null) { this.rotationOverridePatched = false; return; }

                MethodInfo rotSetter = typeof(Transform).GetProperty("rotation").GetSetMethod();
                MethodInfo camRotPrefix = typeof(TransformRotationPatch).GetMethod("SetRotationPrefix");
                MethodInfo playerRotPrefix = typeof(CharacterRotationPatch).GetMethod("SetRotationPrefix");
                if (rotSetter != null && camRotPrefix != null)
                {
                    harmony.Patch(rotSetter, new HarmonyMethod(camRotPrefix), null, null, null, null);
                }
                if (rotSetter != null && playerRotPrefix != null)
                {
                    harmony.Patch(rotSetter, new HarmonyMethod(playerRotPrefix), null, null, null, null);
                }

                ModLogger.Msg("[Patch] Rotation override installed (Transform.rotation setter).");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Patch] Rotation override patch failed: " + ex.Message);
            }
        }

        // Token: 0x06000013 RID: 19 RVA: 0x00003E34 File Offset: 0x00002034
        private void RunBypassLogic(bool shouldHide)
        {
            if (!shouldHide && !this.bypassObjectsHidden)
            {
                return;
            }

            bool targetState = !shouldHide;
            this.ManageObject(ref this.cacheStatusAnim, "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation", targetState);
            this.ManageObject(ref this.cacheCookUI, "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_cook_normal@list", targetState);
            this.ManageObject(ref this.cacheSkeletonBody, "p_player_skeleton(Clone)/sk_player_player_skeleton", targetState);
            this.bypassObjectsHidden = shouldHide;
        }

        private void ApplyFpsBypass(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    if (this.fpsBypassWasApplied)
                    {
                        QualitySettings.vSyncCount = this.fpsBypassOriginalVSyncCount;
                        Application.targetFrameRate = this.fpsBypassOriginalTargetFrameRate;
                        this.fpsBypassWasApplied = false;
                    }
                    this.fpsBypassCompOffset = 0f;
                    this.fpsBypassObservedFps = 0f;
                    return;
                }

                if (!this.fpsBypassWasApplied)
                {
                    this.fpsBypassOriginalVSyncCount = QualitySettings.vSyncCount;
                    this.fpsBypassOriginalTargetFrameRate = Application.targetFrameRate;
                    this.fpsBypassWasApplied = true;
                }

                int target = Mathf.Clamp(Mathf.RoundToInt((float)this.fpsBypassTarget + this.fpsBypassCompOffset), 30, 360);
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = target;
            }
            catch
            {
            }
        }

    }
}
