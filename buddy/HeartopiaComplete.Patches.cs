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

        // NOTE: the Transform.position / Transform.rotation setter prefixes that used to live
        // here (anti-cheat surface #4 — module .text writes, Themis-hashable) are GONE. Player
        // teleport/noclip drives the game's own PlayerMoveComponent (NoclipFeature.cs) and the
        // camera drives the game camera controller's axis (HeartopiaComplete.CameraRig.cs), both
        // embedded-Mono via AuraMono. Only the Input.GetKey* postfixes (surface #1, separate
        // cleanup) still Harmony-patch IL2CPP code.
        private void MaybeUnpatchIdleHotPathPatches(float now)
        {
            if (this.inputSimPatched && now - this.inputSimPatchLastNeededAt > HotPathPatchIdleUnpatchSeconds)
            {
                this.UnpatchInputSim();
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
