using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const bool PadBuildHotkeyLogsEnabled = MasterLogPadBuild;
        private const float PadBuildRotateDebounceSeconds = 0.25f;
        private const float PadBuildManagedResolveRetrySeconds = 5f;
        private const float PadBuildAuraResolveRetrySeconds = 5f;
        private const int PadBuildCraftStateFree = 1;  // CraftState.Free — pad roam, interact move/delete
        private const int PadBuildCraftStateFocus = 2; // CraftState.Focus — placing confirm/cancel/rotate

        // BuildModule resolution, three tiers (docs/TYPE_RESOLUTION.md):
        //  1. Managed reflection — FindLoadedType + TryGetManagedModule (Managers.GetModule(Type)).
        //     Dead on the current build (interop never stubbed BuildModule) but kept first: it is
        //     miss-cached, future-proof, and the cleanest if a later interop regen includes it.
        //  2. AuraMono — find the BuildModule class in the XDTLevelAndEntity image (NOTE: the
        //     XDTGUI.Module.Build namespace lives there, NOT in XDTGameUI — FindAuraMonoClassByFullName
        //     picks the wrong image for it, hence FindAuraMonoClassInImages with an explicit list),
        //     build a System.Type via mono_type_get_object, invoke Managers.GetModule(Type).
        //     No Type.GetType(string) (hard-crashes the runtime) and no _moduleDic enumeration
        //     (ValueCollection yields 0 via AuraMono).
        //  3. UI fallback — click the BuildStatusPanel buttons via GameObject.Find.

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

        // Tier 1 (managed) cache.
        private object padBuildManagedModule;
        private PropertyInfo padBuildManagedSubStateProp;
        private MethodInfo padBuildManagedConfirmMethod;  // ConfirmPlacing(bool)
        private MethodInfo padBuildManagedCancelMethod;   // CancelPlacing()
        private MethodInfo padBuildManagedRotateMethod;   // RotateAround()
        private MethodInfo padBuildManagedMoveMethod;     // InteractExecuteMove()
        private MethodInfo padBuildManagedPickupMethod;   // InteractExecutePickup() — "pack furniture"
        private MethodInfo padBuildManagedDeleteMethod;   // InteractExecuteDelete() — wreck (god mode)
        private float nextPadBuildManagedResolveAt = -999f;

        // Tier 2 (AuraMono) cache. Module object is dropped on any invoke failure (pointer can go
        // stale after GC/level switch); class + method ptrs are stable for the process lifetime.
        private AuraMonoObjectCache padBuildAuraModuleObj;
        private IntPtr padBuildAuraModuleClass = IntPtr.Zero;
        private IntPtr padBuildAuraConfirmMethod = IntPtr.Zero;
        private IntPtr padBuildAuraCancelMethod = IntPtr.Zero;
        private IntPtr padBuildAuraRotateMethod = IntPtr.Zero;
        private IntPtr padBuildAuraGetSubStateMethod = IntPtr.Zero;
        private IntPtr padBuildAuraMoveMethod = IntPtr.Zero;
        private IntPtr padBuildAuraPickupMethod = IntPtr.Zero;
        private IntPtr padBuildAuraDeleteMethod = IntPtr.Zero;
        private float nextPadBuildAuraResolveAt = -999f;

        private static readonly string[] PadBuildPanelRootPaths =
        {
            "GameApp/startup_root(Clone)/XDUIRoot/Bottom/BuildStatusPanel(Clone)",
            "GameApp/startup_root(Clone)/XDUIRoot/Status/BuildStatusPanel(Clone)",
            "GameApp/startup_root(Clone)/XDUIRoot/Full/BuildStatusPanel(Clone)",
            "BuildStatusPanel(Clone)"
        };

        private static readonly string[] PadBuildConfirmRelativePaths =
        {
            "AniRoot@ani@queueanimation/Bottom/confirm_bar@go/confirm@swap@go",
            "AniRoot@ani@queueanimation/Bottom/confirm_bar@go/confirm@swap"
        };

        private static readonly string[] PadBuildCancelRelativePaths =
        {
            "AniRoot@ani@queueanimation/Bottom/confirm_bar@go/cancel@btn"
        };

        private static readonly string[] PadBuildRotateRelativePaths =
        {
            "AniRoot@ani@queueanimation/Bottom/skills@go/interact_rotate@btn"
        };

        private static readonly string[] PadBuildMoveRelativePaths =
        {
            "AniRoot@ani@queueanimation/Bottom/skills@go/interact_move@btn"
        };

        // Delete = remove the focused object. For furniture that's "pack furniture" (move to
        // backpack); "wreck stable" only applies to structures (walls/floors, god mode).
        private static readonly string[] PadBuildDeleteRelativePaths =
        {
            "AniRoot@ani@queueanimation/Bottom/skills@go/interact_pack_furniture@btn",
            "AniRoot@ani@queueanimation/Bottom/skills@go/interact_wreck_stable@btn"
        };

        private float padBuildRotateLastAt = -999f;

        private void ProcessPadBuildHotkeysOnUpdate()
        {
            if (this.TryGetModHotkeyDown(this.keyPadConfirm))
            {
                if (!this.TryPadBuildConfirm(out string confirmStatus))
                {
                    this.PadBuildHotkeyLog("confirm skipped: " + confirmStatus);
                }
            }

            if (this.TryGetModHotkeyDown(this.keyPadCancel))
            {
                if (!this.TryPadBuildCancel(out string cancelStatus))
                {
                    this.PadBuildHotkeyLog("cancel skipped: " + cancelStatus);
                }
            }

            if (this.TryGetModHotkeyDown(this.keyPadRotate))
            {
                if (!this.TryPadBuildRotate(out string rotateStatus))
                {
                    this.PadBuildHotkeyLog("rotate skipped: " + rotateStatus);
                }
            }

            if (this.TryGetModHotkeyDown(this.keyPadMove))
            {
                if (!this.TryPadBuildMove(out string moveStatus))
                {
                    this.PadBuildHotkeyLog("move skipped: " + moveStatus);
                }
            }

            if (this.TryGetModHotkeyDown(this.keyPadDelete))
            {
                if (!this.TryPadBuildDelete(out string deleteStatus))
                {
                    this.PadBuildHotkeyLog("delete skipped: " + deleteStatus);
                }
            }
        }

        // --- Dispatchers: managed → AuraMono → UI ----------------------------------------------

        private bool TryPadBuildConfirm(out string status)
        {
            if (this.TryGetPadBuildManagedModule(out object managed))
            {
                if (!this.IsPadBuildManagedFocus(managed, out status))
                {
                    return false;
                }

                return this.InvokePadBuildManaged(managed, this.padBuildManagedConfirmMethod, new object[] { false }, "confirm", out status);
            }

            if (this.TryGetPadBuildAuraModule(out IntPtr aura))
            {
                if (!this.IsPadBuildAuraFocus(aura, out status))
                {
                    return false;
                }

                return this.InvokePadBuildAura(aura, this.padBuildAuraConfirmMethod, isConfirm: true, "confirm", out status);
            }

            return this.TryPadBuildConfirmViaUi(out status);
        }

        private bool TryPadBuildCancel(out string status)
        {
            if (this.TryGetPadBuildManagedModule(out object managed))
            {
                if (!this.IsPadBuildManagedFocus(managed, out status))
                {
                    return false;
                }

                return this.InvokePadBuildManaged(managed, this.padBuildManagedCancelMethod, null, "cancel", out status);
            }

            if (this.TryGetPadBuildAuraModule(out IntPtr aura))
            {
                if (!this.IsPadBuildAuraFocus(aura, out status))
                {
                    return false;
                }

                return this.InvokePadBuildAura(aura, this.padBuildAuraCancelMethod, isConfirm: false, "cancel", out status);
            }

            return this.TryPadBuildCancelViaUi(out status);
        }

        private bool TryPadBuildRotate(out string status)
        {
            status = string.Empty;
            float now = Time.unscaledTime;
            if (now - this.padBuildRotateLastAt < PadBuildRotateDebounceSeconds)
            {
                return false;
            }

            bool ok;
            if (this.TryGetPadBuildManagedModule(out object managed))
            {
                ok = this.IsPadBuildManagedFocus(managed, out status)
                    && this.InvokePadBuildManaged(managed, this.padBuildManagedRotateMethod, null, "rotate", out status);
            }
            else if (this.TryGetPadBuildAuraModule(out IntPtr aura))
            {
                ok = this.IsPadBuildAuraFocus(aura, out status)
                    && this.InvokePadBuildAura(aura, this.padBuildAuraRotateMethod, isConfirm: false, "rotate", out status);
            }
            else
            {
                ok = this.TryPadBuildRotateViaUi(out status);
            }

            if (ok)
            {
                this.padBuildRotateLastAt = now;
            }

            return ok;
        }

        private bool TryPadBuildMove(out string status)
        {
            // Free gate — InteractExecuteMove is the BuildControl interact path (not placing/Focus).
            if (this.TryGetPadBuildManagedModule(out object managed))
            {
                if (!this.IsPadBuildManagedFree(managed, out status))
                {
                    return false;
                }

                if (this.IsPadBuildManagedGodMode(managed))
                {
                    status = "move: grab by clicking in god mode";
                    return false;
                }

                return this.InvokePadBuildManaged(managed, this.padBuildManagedMoveMethod, null, "move", out status);
            }

            if (this.TryGetPadBuildAuraModule(out IntPtr aura))
            {
                if (!this.IsPadBuildAuraFree(aura, out status))
                {
                    return false;
                }

                if (this.IsPadBuildAuraGodMode(aura))
                {
                    status = "move: grab by clicking in god mode";
                    return false;
                }

                return this.InvokePadBuildAura(aura, this.padBuildAuraMoveMethod, isConfirm: false, "move", out status);
            }

            return this.TryPadBuildMoveViaUi(out status);
        }

        private bool TryPadBuildDelete(out string status)
        {
            // Panel parity: god mode wrecks the focused item (InteractExecuteDelete →
            // GodControl.Focus_Delete); Pad mode packs furniture back to the backpack
            // (InteractExecutePickup), which is the furniture "delete".
            if (this.TryGetPadBuildManagedModule(out object managed))
            {
                if (!this.IsPadBuildManagedFree(managed, out status))
                {
                    return false;
                }

                MethodInfo method = this.IsPadBuildManagedGodMode(managed)
                    ? this.padBuildManagedDeleteMethod
                    : this.padBuildManagedPickupMethod;
                return this.InvokePadBuildManaged(managed, method, null, "delete", out status);
            }

            if (this.TryGetPadBuildAuraModule(out IntPtr aura))
            {
                if (!this.IsPadBuildAuraFree(aura, out status))
                {
                    return false;
                }

                IntPtr method = this.IsPadBuildAuraGodMode(aura)
                    ? this.padBuildAuraDeleteMethod
                    : this.padBuildAuraPickupMethod;
                return this.InvokePadBuildAura(aura, method, isConfirm: false, "delete", out status);
            }

            return this.TryPadBuildDeleteViaUi(out status);
        }

        // --- Tier 1: managed module resolution & invocation ------------------------------------

        private bool TryGetPadBuildManagedModule(out object module)
        {
            module = this.padBuildManagedModule;
            if (module != null)
            {
                return true;
            }

            float now = Time.unscaledTime;
            if (now < this.nextPadBuildManagedResolveAt)
            {
                return false;
            }
            this.nextPadBuildManagedResolveAt = now + PadBuildManagedResolveRetrySeconds;

            try
            {
                Type moduleType = this.FindLoadedType(
                    "XDTGUI.Module.Build.BuildModule",
                    "Il2CppXDTGUI.Module.Build.BuildModule",
                    "BuildModule");
                if (moduleType == null)
                {
                    return false; // expected on this build (no interop stub) — aura tier takes over
                }

                if (!this.TryGetManagedModule(moduleType, out object resolved) || resolved == null)
                {
                    this.PadBuildHotkeyLog("managed: Managers.GetModule returned null");
                    return false;
                }

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                this.padBuildManagedSubStateProp = moduleType.GetProperty("SubState", flags);
                this.padBuildManagedConfirmMethod = moduleType.GetMethod("ConfirmPlacing", flags, null, new[] { typeof(bool) }, null);
                this.padBuildManagedCancelMethod = moduleType.GetMethod("CancelPlacing", flags, null, Type.EmptyTypes, null);
                this.padBuildManagedRotateMethod = moduleType.GetMethod("RotateAround", flags, null, Type.EmptyTypes, null);
                this.padBuildManagedMoveMethod = moduleType.GetMethod("InteractExecuteMove", flags, null, Type.EmptyTypes, null);
                this.padBuildManagedPickupMethod = moduleType.GetMethod("InteractExecutePickup", flags, null, Type.EmptyTypes, null);
                this.padBuildManagedDeleteMethod = moduleType.GetMethod("InteractExecuteDelete", flags, null, Type.EmptyTypes, null);

                if (this.padBuildManagedSubStateProp == null
                    || this.padBuildManagedConfirmMethod == null
                    || this.padBuildManagedCancelMethod == null
                    || this.padBuildManagedRotateMethod == null)
                {
                    this.PadBuildHotkeyLog("managed: BuildModule members missing");
                    return false;
                }

                this.padBuildManagedModule = resolved;
                module = resolved;
                this.PadBuildHotkeyLog("managed: BuildModule resolved via Managers.GetModule");
                return true;
            }
            catch (Exception ex)
            {
                this.padBuildManagedModule = null;
                this.PadBuildHotkeyLog("managed: resolve exception: " + ex.Message);
                return false;
            }
        }

        private bool IsPadBuildManagedFocus(object module, out string status)
        {
            return this.IsPadBuildManagedSubState(module, PadBuildCraftStateFocus, "focus active", out status);
        }

        private bool IsPadBuildManagedFree(object module, out string status)
        {
            return this.IsPadBuildManagedSubState(module, PadBuildCraftStateFree, "free active", out status);
        }

        private bool IsPadBuildManagedSubState(object module, int requiredState, string okStatus, out string status)
        {
            status = "build inactive";
            try
            {
                object boxed = this.padBuildManagedSubStateProp.GetValue(module, null);
                int subState = boxed != null ? Convert.ToInt32(boxed) : -1;
                if (subState != requiredState)
                {
                    status = "sub state " + subState;
                    return false;
                }

                status = okStatus;
                return true;
            }
            catch (Exception ex)
            {
                // Stale interop object (module re-created on level switch) — drop and re-resolve.
                this.padBuildManagedModule = null;
                status = "sub state exc: " + (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        private bool IsPadBuildManagedGodMode(object module)
        {
            try
            {
                Type moduleType = module.GetType();
                // Il2CppInterop surfaces fields as properties; plain field on managed builds.
                PropertyInfo prop = moduleType.GetProperty("InGodMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    return Convert.ToBoolean(prop.GetValue(module, null));
                }

                FieldInfo field = moduleType.GetField("InGodMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field != null && Convert.ToBoolean(field.GetValue(module));
            }
            catch
            {
                return false;
            }
        }

        private bool InvokePadBuildManaged(object module, MethodInfo method, object[] args, string op, out string status)
        {
            if (method == null)
            {
                status = op + " method unavailable";
                return false;
            }

            try
            {
                method.Invoke(module, args);
                status = "managed " + op;
                this.PadBuildHotkeyLog(status);
                return true;
            }
            catch (Exception ex)
            {
                this.padBuildManagedModule = null;
                status = op + " invoke exc: " + (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        // --- Tier 2: AuraMono module resolution & invocation ------------------------------------

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
                    this.PadBuildHotkeyLog("aura: mono api unavailable");
                    return false;
                }

                IntPtr moduleClass = this.FindAuraMonoClassInImages("XDTGUI.Module.Build", "BuildModule", PadBuildModuleImageNames);
                if (moduleClass == IntPtr.Zero)
                {
                    this.PadBuildHotkeyLog("aura: BuildModule class not found in images");
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
                    this.PadBuildHotkeyLog("aura: Managers class not found");
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
                    this.padBuildAuraConfirmMethod = this.FindAuraMonoMethodOnHierarchy(moduleClass, "ConfirmPlacing", 1);
                    this.padBuildAuraCancelMethod = this.FindAuraMonoMethodOnHierarchy(moduleClass, "CancelPlacing", 0);
                    this.padBuildAuraRotateMethod = this.FindAuraMonoMethodOnHierarchy(moduleClass, "RotateAround", 0);
                    this.padBuildAuraGetSubStateMethod = this.FindAuraMonoMethodOnHierarchy(moduleClass, "get_SubState", 0);
                    this.padBuildAuraMoveMethod = this.FindAuraMonoMethodOnHierarchy(moduleClass, "InteractExecuteMove", 0);
                    this.padBuildAuraPickupMethod = this.FindAuraMonoMethodOnHierarchy(moduleClass, "InteractExecutePickup", 0);
                    this.padBuildAuraDeleteMethod = this.FindAuraMonoMethodOnHierarchy(moduleClass, "InteractExecuteDelete", 0);
                }

                if (this.padBuildAuraConfirmMethod == IntPtr.Zero || this.padBuildAuraCancelMethod == IntPtr.Zero
                    || this.padBuildAuraRotateMethod == IntPtr.Zero || this.padBuildAuraGetSubStateMethod == IntPtr.Zero)
                {
                    this.PadBuildHotkeyLog("aura: BuildModule methods missing");
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

        private unsafe bool IsPadBuildAuraFocus(IntPtr moduleObj, out string status)
        {
            return this.IsPadBuildAuraSubState(moduleObj, PadBuildCraftStateFocus, "focus active", out status);
        }

        private unsafe bool IsPadBuildAuraFree(IntPtr moduleObj, out string status)
        {
            return this.IsPadBuildAuraSubState(moduleObj, PadBuildCraftStateFree, "free active", out status);
        }

        private unsafe bool IsPadBuildAuraSubState(IntPtr moduleObj, int requiredState, string okStatus, out string status)
        {
            status = "build inactive";
            if (moduleObj == IntPtr.Zero || this.padBuildAuraGetSubStateMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return false;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(this.padBuildAuraGetSubStateMethod, moduleObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    this.padBuildAuraModuleObj.Clear(); // possibly stale — re-resolve next press
                    status = "sub state unavailable";
                    return false;
                }

                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero)
                {
                    status = "sub state unbox failed";
                    return false;
                }

                int subState = *(byte*)raw;
                if (subState != requiredState)
                {
                    status = "sub state " + subState;
                    return false;
                }

                status = okStatus;
                return true;
            }
            catch (Exception ex)
            {
                this.padBuildAuraModuleObj.Clear();
                status = "sub state exc: " + ex.Message;
                return false;
            }
        }

        private bool IsPadBuildAuraGodMode(IntPtr moduleObj)
        {
            return moduleObj != IntPtr.Zero
                && this.TryGetMonoBoolMember(moduleObj, "InGodMode", out bool inGodMode)
                && inGodMode;
        }

        private unsafe bool InvokePadBuildAura(IntPtr moduleObj, IntPtr method, bool isConfirm, string op, out string status)
        {
            status = op + " method unavailable";
            if (moduleObj == IntPtr.Zero || method == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                if (isConfirm)
                {
                    // ConfirmPlacing(bool down): mono_runtime_invoke wants a pointer to the raw value.
                    byte down = 0;
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&down);
                    auraMonoRuntimeInvoke(method, moduleObj, (IntPtr)args, ref exc);
                }
                else
                {
                    auraMonoRuntimeInvoke(method, moduleObj, IntPtr.Zero, ref exc);
                }

                if (exc != IntPtr.Zero)
                {
                    this.padBuildAuraModuleObj.Clear();
                    status = op + " invoke exc";
                    return false;
                }

                status = "aura " + op;
                this.PadBuildHotkeyLog(status);
                return true;
            }
            catch (Exception ex)
            {
                this.padBuildAuraModuleObj.Clear();
                status = op + " invoke exception: " + ex.Message;
                return false;
            }
        }

        // --- Tier 3: UI fallback (BuildStatusPanel button clicks) -------------------------------

        private bool TryPadBuildConfirmViaUi(out string status)
        {
            status = string.Empty;
            if (!this.TryIsPadBuildUiActive(out status))
            {
                return false;
            }

            GameObject confirmObj = this.TryFindPadBuildUiObject(PadBuildConfirmRelativePaths);
            if (confirmObj == null)
            {
                status = "confirm button not found";
                return false;
            }

            if (!this.TrySimulatePadBuildSwapConfirm(confirmObj))
            {
                status = "confirm simulate failed";
                return false;
            }

            status = "ui confirm";
            this.PadBuildHotkeyLog(status);
            return true;
        }

        private bool TryPadBuildCancelViaUi(out string status)
        {
            status = string.Empty;
            if (!this.TryIsPadBuildUiActive(out status))
            {
                return false;
            }

            if (!this.TryClickPadBuildUiButton(PadBuildCancelRelativePaths, out status))
            {
                if (status.Length == 0)
                {
                    status = "cancel button not found";
                }

                return false;
            }

            this.PadBuildHotkeyLog(status);
            return true;
        }

        private bool TryPadBuildRotateViaUi(out string status)
        {
            status = string.Empty;
            if (!this.TryIsPadBuildUiActive(out status))
            {
                return false;
            }

            if (!this.TryClickPadBuildUiButton(PadBuildRotateRelativePaths, out status))
            {
                if (status.Length == 0)
                {
                    status = "rotate button not found";
                }

                return false;
            }

            this.PadBuildHotkeyLog(status);
            return true;
        }

        private bool TryPadBuildMoveViaUi(out string status)
        {
            status = string.Empty;
            if (!this.TryClickPadBuildUiButton(PadBuildMoveRelativePaths, out status))
            {
                if (status.Length == 0)
                {
                    status = "move button not active";
                }

                return false;
            }

            this.PadBuildHotkeyLog(status);
            return true;
        }

        private bool TryPadBuildDeleteViaUi(out string status)
        {
            status = string.Empty;
            if (!this.TryClickPadBuildUiButton(PadBuildDeleteRelativePaths, out status))
            {
                if (status.Length == 0)
                {
                    status = "delete button not active";
                }

                return false;
            }

            this.PadBuildHotkeyLog(status);
            return true;
        }

        private bool TryIsPadBuildUiActive(out string status)
        {
            status = "build ui inactive";
            GameObject panelRoot = this.TryFindPadBuildPanelRoot();
            if (panelRoot == null)
            {
                return false;
            }

            GameObject confirmObj = this.TryFindPadBuildUiObject(PadBuildConfirmRelativePaths);
            if (confirmObj != null && confirmObj.activeInHierarchy)
            {
                status = "confirm visible";
                return true;
            }

            GameObject rotateObj = this.TryFindPadBuildUiObject(PadBuildRotateRelativePaths);
            if (rotateObj != null && rotateObj.activeInHierarchy)
            {
                status = "rotate visible";
                return true;
            }

            return false;
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

        private GameObject TryFindPadBuildUiObject(string[] relativePaths)
        {
            if (relativePaths == null || relativePaths.Length == 0)
            {
                return null;
            }

            GameObject panelRoot = this.TryFindPadBuildPanelRoot();
            if (panelRoot != null)
            {
                for (int i = 0; i < relativePaths.Length; i++)
                {
                    Transform child = panelRoot.transform.Find(relativePaths[i]);
                    if (child != null)
                    {
                        return child.gameObject;
                    }
                }
            }

            for (int i = 0; i < PadBuildPanelRootPaths.Length; i++)
            {
                string panelRootPath = PadBuildPanelRootPaths[i];
                for (int j = 0; j < relativePaths.Length; j++)
                {
                    GameObject candidate = GameObject.Find(panelRootPath + "/" + relativePaths[j]);
                    if (candidate != null)
                    {
                        return candidate;
                    }
                }
            }

            for (int j = 0; j < relativePaths.Length; j++)
            {
                string leafName = relativePaths[j];
                int slash = leafName.LastIndexOf('/');
                if (slash >= 0 && slash < leafName.Length - 1)
                {
                    leafName = leafName.Substring(slash + 1);
                }

                GameObject byName = GameObject.Find(leafName);
                if (byName != null && byName.activeInHierarchy)
                {
                    return byName;
                }
            }

            return null;
        }

        // Clicks the first active+clickable button among the given paths. Iterating per path (rather
        // than TryFindPadBuildUiObject over the whole set) matters when several candidates exist in
        // the hierarchy but only one is active — e.g. delete's pack/wreck pair.
        private bool TryClickPadBuildUiButton(string[] relativePaths, out string status)
        {
            status = string.Empty;
            if (relativePaths == null)
            {
                return false;
            }

            for (int i = 0; i < relativePaths.Length; i++)
            {
                GameObject target = this.TryFindPadBuildUiObject(new[] { relativePaths[i] });
                if (target == null || !target.activeInHierarchy)
                {
                    continue;
                }

                Button button = this.ResolveClickableButton(target);
                if (button != null && button.interactable)
                {
                    button.onClick.Invoke();
                    status = "ui click " + target.name;
                    return true;
                }

                if (this.SimulateClick(target))
                {
                    status = "ui simulate " + target.name;
                    return true;
                }
            }

            return false;
        }

        private bool TrySimulatePadBuildSwapConfirm(GameObject target)
        {
            if (target == null || !target.activeInHierarchy)
            {
                return false;
            }

            try
            {
                EventSystem eventSystem = this.EnsureGameplayEventSystemAvailable();
                PointerEventData eventData = new PointerEventData(eventSystem)
                {
                    position = RectTransformUtility.WorldToScreenPoint(null, target.transform.position)
                };

                ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerUpHandler);
                return true;
            }
            catch (Exception ex)
            {
                this.PadBuildHotkeyLog("confirm simulate error: " + ex.Message);
                return false;
            }
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
