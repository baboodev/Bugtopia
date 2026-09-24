using System;
using UnityEngine;

namespace HeartopiaMod
{
    // BuildModule access shared by the build-mode features (BuildingFreeRotateFeature, the move
    // panel, FurnitureDyeFeature, the build text-input guard).
    //
    // This file used to also host the "Pad build hotkeys" (Pad Confirm / Cancel / Rotate / Move /
    // Delete). They were removed on 2026-09-24: the game's own build panel now has PC shortcuts
    // for every one of them (Enter, Esc, R, M, Delete — BuildStatusPanel.BindPcInput), and those
    // are rebindable in Settings→Game Keys, so the mod's copies only doubled the action. What is
    // left is the module resolution the rest of the build code depends on; the file keeps its name
    // because the docs cite it as the worked example for Managers.GetModule(Type).
    //
    // BuildModule resolution (docs/TYPE_RESOLUTION.md): find the BuildModule class in the
    // XDTLevelAndEntity image (NOTE: the XDTGUI.Module.Build namespace lives there, NOT in
    // XDTGameUI — FindAuraMonoClassByFullName picks the wrong image for it, hence
    // FindAuraMonoClassInImages with an explicit list), build a System.Type via
    // mono_type_get_object, invoke Managers.GetModule(Type). No Type.GetType(string) (hard-crashes
    // the runtime) and no _moduleDic enumeration (ValueCollection yields 0 via AuraMono).
    public partial class HeartopiaComplete
    {
        private static bool PadBuildHotkeyLogsEnabled => MasterLogPadBuild;
        private const float PadBuildAuraResolveRetrySeconds = 5f;

        // BuildModule (namespace XDTGUI.Module.Build) is compiled into XDTLevelAndEntity.
        private static readonly string[] PadBuildModuleImageNames =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
            "XDTGameUI", "XDTGameUI.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll"
        };

        // XDTGame.Framework.Managers lives in XDTBaseService. Pin the namespace+image so we do not
        // grab an unrelated "Managers" class (a wrong pick here makes GetModule return null).
        private static readonly string[] PadBuildManagersImageNames =
        {
            "XDTBaseService", "XDTBaseService.dll",
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll"
        };

        // Module object is dropped on any invoke failure (pointer can go stale after GC/level
        // switch); class + method ptrs are stable for the process lifetime.
        private AuraMonoObjectCache padBuildAuraModuleObj;
        private IntPtr padBuildAuraModuleClass = IntPtr.Zero;
        private IntPtr padBuildAuraGetSubStateMethod = IntPtr.Zero; // read by TryGetPadBuildAuraSubState
        private float nextPadBuildAuraResolveAt = -999f;

        private static readonly string[] PadBuildPanelRootPaths =
        {
            "GameApp/startup_root(Clone)/XDUIRoot/Bottom/BuildStatusPanel(Clone)",
            "GameApp/startup_root(Clone)/XDUIRoot/Status/BuildStatusPanel(Clone)",
            "GameApp/startup_root(Clone)/XDUIRoot/Full/BuildStatusPanel(Clone)",
            "BuildStatusPanel(Clone)"
        };

        private unsafe bool TryGetPadBuildAuraModule(out IntPtr moduleObj)
        {
            if (this.padBuildAuraModuleObj.TryGet(out moduleObj))
            {
                return true;
            }

            float now = Time.unscaledTime;
            if (now < this.nextPadBuildAuraResolveAt)
            {
                return false;
            }
            this.nextPadBuildAuraResolveAt = now + PadBuildAuraResolveRetrySeconds;

            try
            {
                this.ResolveAuraFarmRuntimeMethods();
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null
                    || auraMonoClassGetType == null || auraMonoTypeGetObject == null
                    || this.auraMonoRootDomain == IntPtr.Zero)
                {
                    FeatureLog.Fail("PadBuild", "aura: mono api unavailable");
                    return false;
                }

                IntPtr moduleClass = this.FindAuraMonoClassInImages("XDTGUI.Module.Build", "BuildModule", PadBuildModuleImageNames);
                if (moduleClass == IntPtr.Zero)
                {
                    FeatureLog.Fail("PadBuild", "aura: BuildModule class not found in images");
                    return false;
                }

                IntPtr monoType = auraMonoClassGetType(moduleClass);
                IntPtr typeObj = monoType != IntPtr.Zero ? auraMonoTypeGetObject(this.auraMonoRootDomain, monoType) : IntPtr.Zero;
                if (typeObj == IntPtr.Zero)
                {
                    this.PadBuildHotkeyLog("aura: Type object unavailable");
                    return false;
                }

                IntPtr managersClass = this.FindAuraMonoClassInImages("XDTGame.Framework", "Managers", PadBuildManagersImageNames);
                if (managersClass == IntPtr.Zero)
                {
                    FeatureLog.Fail("PadBuild", "aura: Managers class not found");
                    return false;
                }

                IntPtr getModuleMethod = this.FindAuraMonoMethodOnHierarchy(managersClass, "GetModule", 1);
                if (getModuleMethod == IntPtr.Zero)
                {
                    this.PadBuildHotkeyLog("aura: GetModule(Type) not found");
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = typeObj;
                moduleObj = auraMonoRuntimeInvoke(getModuleMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || moduleObj == IntPtr.Zero)
                {
                    this.PadBuildHotkeyLog("aura: GetModule returned null/exc");
                    moduleObj = IntPtr.Zero;
                    return false;
                }

                if (moduleClass != this.padBuildAuraModuleClass)
                {
                    this.padBuildAuraModuleClass = moduleClass;
                    this.padBuildAuraGetSubStateMethod = this.FindAuraMonoMethodOnHierarchy(moduleClass, "get_SubState", 0);
                }

                if (this.padBuildAuraGetSubStateMethod == IntPtr.Zero)
                {
                    this.PadBuildHotkeyLog("aura: BuildModule.get_SubState missing");
                    moduleObj = IntPtr.Zero;
                    return false;
                }

                this.padBuildAuraModuleObj.Set(moduleObj);
                this.PadBuildHotkeyLog("aura: BuildModule resolved via Managers.GetModule(Type)");
                return true;
            }
            catch (Exception ex)
            {
                this.padBuildAuraModuleObj.Clear();
                moduleObj = IntPtr.Zero;
                this.PadBuildHotkeyLog("aura: resolve exception: " + ex.Message);
                return false;
            }
        }

        private GameObject TryFindPadBuildPanelRoot()
        {
            for (int i = 0; i < PadBuildPanelRootPaths.Length; i++)
            {
                GameObject candidate = GameObject.Find(PadBuildPanelRootPaths[i]);
                if (candidate != null && candidate.activeInHierarchy)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void PadBuildHotkeyLog(string message)
        {
            if (!PadBuildHotkeyLogsEnabled)
            {
                return;
            }

            ModLogger.Msg("[PadBuild] " + message);
        }
    }
}
