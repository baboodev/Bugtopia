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
        private unsafe bool ModTryAuraMonoBagPanelObjectIsBagPanel(IntPtr bagPanelObj)
        {
            if (bagPanelObj == IntPtr.Zero || !this.TryInvokeAuraMonoZeroArg(bagPanelObj, out IntPtr typeObj, "GetType"))
            {
                return false;
            }

            if (typeObj == IntPtr.Zero || !this.TryInvokeAuraMonoZeroArg(typeObj, out IntPtr nameObj, "get_Name"))
            {
                return false;
            }

            if (!this.TryReadMonoString(nameObj, out string name))
            {
                return false;
            }

            return string.Equals(name, "BagPanel", StringComparison.Ordinal);
        }

        private unsafe bool ModTryAuraMonoReadBagPanelLifeCycle(IntPtr bagPanelObj, out int lifeCycle)
        {
            lifeCycle = -1;
            if (bagPanelObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoFieldGetValueObject == null)
            {
                return false;
            }

            IntPtr bagPanelClass = auraMonoObjectGetClass(bagPanelObj);
            IntPtr lifeCycleField = this.FindAuraMonoFieldOnHierarchy(bagPanelClass, "lifeCycleState");
            if (lifeCycleField == IntPtr.Zero)
            {
                return false;
            }

            IntPtr boxedState = auraMonoFieldGetValueObject(this.auraMonoRootDomain, lifeCycleField, bagPanelObj);
            if (boxedState == IntPtr.Zero || !this.TryUnboxMonoInt32(boxedState, out lifeCycle))
            {
                return false;
            }

            return true;
        }

        private unsafe bool ModTryAuraMonoBagPanelUiIsVisible(IntPtr bagPanelObj)
        {
            if (bagPanelObj == IntPtr.Zero
                || !this.TryReadAuraMonoObjectField(bagPanelObj, out IntPtr uiObj, "ui")
                || uiObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(uiObj, out IntPtr goObj, "get_gameObject") || goObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.ModTryAuraMonoReadBoolProperty(goObj, "get_activeInHierarchy", out bool activeInHierarchy) || !activeInHierarchy)
            {
                return false;
            }

            if (this.TryReadAuraMonoObjectField(uiObj, out IntPtr canvasObj, "canvas") && canvasObj != IntPtr.Zero)
            {
                if (!this.ModTryAuraMonoReadBoolProperty(canvasObj, "get_enabled", out bool canvasEnabled) || !canvasEnabled)
                {
                    return false;
                }
            }

            if (this.TryReadAuraMonoObjectField(uiObj, out IntPtr canvasGroupObj, "canvasGroup") && canvasGroupObj != IntPtr.Zero)
            {
                if (!this.ModTryAuraMonoReadBoolProperty(canvasGroupObj, "get_interactable", out bool interactable) || !interactable)
                {
                    return false;
                }
            }

            if (this.TryInvokeAuraMonoZeroArg(bagPanelObj, out IntPtr nodesObj, "get_nodes") && nodesObj != IntPtr.Zero
                && this.TryReadAuraMonoObjectField(nodesObj, out IntPtr rootGroupObj, "root_group") && rootGroupObj != IntPtr.Zero)
            {
                if (!this.ModTryAuraMonoReadBoolProperty(rootGroupObj, "get_interactable", out bool rootInteractable) || !rootInteractable)
                {
                    return false;
                }
            }

            IntPtr bagPanelClass = auraMonoObjectGetClass(bagPanelObj);
            IntPtr getIsGotFocus = this.FindAuraMonoMethodOnHierarchy(bagPanelClass, "get_IsGotFocus", 0);
            if (getIsGotFocus == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr focusBoxed = auraMonoRuntimeInvoke(getIsGotFocus, bagPanelObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || focusBoxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr focusRaw = auraMonoObjectUnbox(focusBoxed);
            return focusRaw != IntPtr.Zero && (*(byte*)focusRaw) != 0;
        }

        internal static bool ModTryUnityBagPanelRootGroupInteractable(GameObject bag, out bool interactable)
        {
            interactable = true;
            if (bag == null)
            {
                return false;
            }

            CanvasGroup[] groups = bag.GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                CanvasGroup group = groups[i];
                if (group == null)
                {
                    continue;
                }

                string nodeName = group.gameObject.name;
                if (nodeName == "root_group" || nodeName.StartsWith("root_group@", StringComparison.Ordinal))
                {
                    interactable = group.interactable;
                    return true;
                }
            }

            return false;
        }

        internal static bool ModTryUnityFindActiveBagPanel(out GameObject bag)
        {
            bag = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/BagPanel(Clone)");
            if (bag == null)
            {
                bag = GameObject.Find("BagPanel(Clone)");
            }

            if (bag == null || !bag.activeInHierarchy)
            {
                bag = null;
                return false;
            }

            return true;
        }

        internal static bool ModTryUnityBagPanelLooksStaleClosed(GameObject bag)
        {
            return bag != null
                && ModTryUnityBagPanelRootGroupInteractable(bag, out bool rootInteractable)
                && !rootInteractable;
        }

        private unsafe bool ModTryResolveAuraMonoUIManager(out IntPtr uiManagerObj, out IntPtr uiManagerClass)
        {
            uiManagerObj = IntPtr.Zero;
            uiManagerClass = IntPtr.Zero;
            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoClassFromName == null
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr uiImage = this.FindAuraMonoImage(new string[] { "XDTGameUI", "XDTGameUI.dll" });
            uiManagerClass = uiImage != IntPtr.Zero
                ? auraMonoClassFromName(uiImage, "XDTGame.Core", "UIManager")
                : IntPtr.Zero;
            if (uiManagerClass == IntPtr.Zero)
            {
                uiManagerClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.Core", "UIManager");
            }

            IntPtr getInstance = uiManagerClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(uiManagerClass, "get_Instance", 0) : IntPtr.Zero;
            if (getInstance == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            uiManagerObj = auraMonoRuntimeInvoke(getInstance, IntPtr.Zero, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero && uiManagerObj != IntPtr.Zero;
        }

        internal unsafe bool ModTryResolveAuraMonoBagPanel(out IntPtr bagPanelObj)
        {
            bagPanelObj = IntPtr.Zero;
            if (!this.ModTryResolveAuraMonoUIManager(out IntPtr uiManagerObj, out IntPtr uiManagerClass)
                || auraMonoStringNew == null
                || this.auraMonoRootDomain == IntPtr.Zero
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getView = this.FindAuraMonoMethodOnHierarchy(uiManagerClass, "GetView", 1);
            if (getView == IntPtr.Zero)
            {
                return false;
            }

            IntPtr bagPanelTypeObj = this.BuildAuraMonoBagPanelType();
            if (bagPanelTypeObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* viewArgs = stackalloc IntPtr[1];
            viewArgs[0] = bagPanelTypeObj;
            bagPanelObj = auraMonoRuntimeInvoke(getView, uiManagerObj, (IntPtr)viewArgs, ref exc);
            return exc == IntPtr.Zero && bagPanelObj != IntPtr.Zero;
        }

        private unsafe IntPtr BuildAuraMonoBagPanelType()
        {
            if (this.warehouseAuraBagPanelTypeObj != IntPtr.Zero)
            {
                return this.warehouseAuraBagPanelTypeObj;
            }

            if (auraMonoStringNew == null || auraMonoRuntimeInvoke == null
                || this.auraMonoRootDomain == IntPtr.Zero || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            string[] typeNameCandidates = new string[]
            {
                "XDTGame.UI.Panel.BagPanel, XDTGameUI",
                "XDTGame.UI.Panel.BagPanel, XDTGameUI.dll",
                "XDTGame.UI.Panel.BagPanel, XDTLevelAndEntity",
                "XDTGame.UI.Panel.BagPanel"
            };

            for (int i = 0; i < typeNameCandidates.Length; i++)
            {
                IntPtr typeNameStr = auraMonoStringNew(this.auraMonoRootDomain, typeNameCandidates[i]);
                if (typeNameStr == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* typeArgs = stackalloc IntPtr[1];
                typeArgs[0] = typeNameStr;
                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                if (exc == IntPtr.Zero && typeObj != IntPtr.Zero)
                {
                    this.warehouseAuraBagPanelTypeObj = typeObj;
                    return typeObj;
                }
            }

            return IntPtr.Zero;
        }

        private bool TryGetUiManagerInstance(out object uiManager, out Type uiManagerType)
        {
            uiManager = null;
            uiManagerType = this.FindLoadedType("XDTGame.Core.UIManager", "UIManager");
            if (uiManagerType == null)
            {
                this.forceOpenShopStatus = "UIManager type not found.";
                this.LogForceOpenShop(this.forceOpenShopStatus);
                return false;
            }

            try
            {
                PropertyInfo instanceProperty = uiManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                uiManager = instanceProperty?.GetValue(null, null);
                if (uiManager == null)
                {
                    this.forceOpenShopStatus = "UIManager instance unavailable.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "UIManager resolve failed: " + ex.Message;
                this.LogForceOpenShop("UIManager resolve exception: " + ex);
                return false;
            }
        }

        private Image[] GetBagPanelImages()
        {
            GameObject bag = GameObject.Find(BAG_PANEL_PATH);
            if (bag == null || !bag.activeInHierarchy) return System.Array.Empty<Image>();
            return bag.GetComponentsInChildren<Image>(true);
        }

    }
}
