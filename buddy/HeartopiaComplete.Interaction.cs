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
        private bool ToastContainsLocalizedPhrase(string message, string phraseKey)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(phraseKey))
            {
                return false;
            }

            foreach (string candidate in LocalizationManager.GetTranslationCandidates(phraseKey))
            {
                if (message.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryOpenAuraPanelByTypeName(string fullTypeName, string successStatus)
        {
            if (this.TryOpenPanelByResolvedTypeName(fullTypeName, null, successStatus))
            {
                return true;
            }

            return this.TryOpenAuraPanelByTypeNameViaMono(fullTypeName, successStatus);
        }

        // AuraMono-only UIManager.OpenView(Type, null-Intent). Split out of TryOpenAuraPanelByTypeName so
        // callers that already attempted the managed path (or want the Mono path only) can reuse it.
        private unsafe bool TryOpenAuraPanelByTypeNameViaMono(string fullTypeName, string successStatus)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    this.forceOpenShopStatus = "Aura mono runtime not ready.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr uiManagerClass = this.FindAuraMonoClassByFullName("XDTGame.Core.UIManager");
                if (uiManagerClass == IntPtr.Zero)
                {
                    // UIManager is XDTGame.Core.UIManager but compiled into the XDTGameUI assembly
                    // (namespace != assembly); probe XDTGameUI first, then other likely images.
                    uiManagerClass = this.FindAuraMonoClassInImages(
                        "XDTGame.Core",
                        "UIManager",
                        new string[] { "XDTGameUI", "XDTGameUI.dll", "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "Client", "Client.dll" });
                }
                if (uiManagerClass == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura UIManager class not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr getInstanceMethod = this.FindAuraMonoMethodOnHierarchy(uiManagerClass, "get_Instance", 0);
                if (getInstanceMethod == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura UIManager.Instance getter not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr uiManagerObj = IntPtr.Zero;
                IntPtr exc = IntPtr.Zero;
                uiManagerObj = auraMonoRuntimeInvoke(getInstanceMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || uiManagerObj == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura UIManager instance unavailable.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                if (!this.TryCreateAuraMonoSystemTypeObject(fullTypeName, out IntPtr typeObj) || typeObj == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura panel Type object not found: " + fullTypeName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr openViewMethod = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(uiManagerObj), "OpenView", 2);
                if (openViewMethod == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura UIManager.OpenView(Type, Intent) not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Invoking aura UIManager.OpenView for " + fullTypeName);
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = typeObj;
                args[1] = IntPtr.Zero;
                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(openViewMethod, uiManagerObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura UIManager.OpenView exception: " + fullTypeName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("Aura UIManager.OpenView succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Aura UIManager.OpenView failed: " + ex.Message;
                this.LogForceOpenShop("Aura panel open exception: " + ex);
                return false;
            }
        }

        private bool TryOpenPanelByResolvedTypeName(string fullTypeName, Action<object> configureIntent, string successStatus)
        {
            try
            {
                string shortName = fullTypeName;
                int lastDot = fullTypeName.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < fullTypeName.Length - 1)
                {
                    shortName = fullTypeName.Substring(lastDot + 1);
                }

                Type panelType = this.FindLoadedType(fullTypeName, shortName);
                if (panelType == null)
                {
                    this.LogForceOpenShop("Managed panel type not found: " + fullTypeName);
                    return false;
                }

                if (!this.TryCreateUiIntent(out object intent, out _))
                {
                    return false;
                }

                configureIntent?.Invoke(intent);

                if (!this.TryGetUiManagerInstance(out object uiManager, out Type uiManagerType))
                {
                    return false;
                }

                MethodInfo openViewMethod = null;
                foreach (MethodInfo candidate in uiManagerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!string.Equals(candidate.Name, "OpenView", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (parameters.Length == 2 && parameters[0].ParameterType == typeof(Type))
                    {
                        openViewMethod = candidate;
                        break;
                    }
                }

                if (openViewMethod == null)
                {
                    this.forceOpenShopStatus = "UIManager.OpenView(Type, Intent) not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Opening managed panel type: " + panelType.FullName);
                openViewMethod.Invoke(uiManager, new object[] { panelType, intent });
                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("Managed panel open succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.LogForceOpenShop("Managed panel open exception: " + ex);
                return false;
            }
        }

        private bool TryOpenPanelByRegistry(string panelName, Action<object> configureIntent, string successStatus)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(panelName))
                {
                    this.forceOpenShopStatus = "Panel name missing.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                if (!this.TryResolvePanelTypeByName(panelName, out Type panelType))
                {
                    return false;
                }

                if (!this.TryCreateUiIntent(out object intent, out Type intentType))
                {
                    return false;
                }

                configureIntent?.Invoke(intent);

                if (!this.TryGetUiManagerInstance(out object uiManager, out Type uiManagerType))
                {
                    return false;
                }

                MethodInfo openViewMethod = uiManagerType.GetMethod("OpenView", new Type[] { typeof(Type), intentType });
                if (openViewMethod == null)
                {
                    foreach (MethodInfo candidate in uiManagerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!string.Equals(candidate.Name, "OpenView", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        ParameterInfo[] parameters = candidate.GetParameters();
                        if (parameters.Length == 2 && parameters[0].ParameterType == typeof(Type))
                        {
                            openViewMethod = candidate;
                            break;
                        }
                    }
                }

                if (openViewMethod == null)
                {
                    this.forceOpenShopStatus = "UIManager.OpenView(Type, Intent) not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Opening panel via UIManager registry path: " + panelType.FullName);
                openViewMethod.Invoke(uiManager, new object[] { panelType, intent });
                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("Registry open succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Open failed: " + ex.Message;
                this.LogForceOpenShop("Registry open exception: " + ex);
                return false;
            }
        }

        private bool TryResolvePanelTypeByName(string panelName, out Type panelType)
        {
            panelType = null;

            try
            {
                Type panelDefType = this.FindLoadedType("XDTGame.Auto.Manager.PanelDef", "PanelDef");
                if (panelDefType == null)
                {
                    this.forceOpenShopStatus = "PanelDef type not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                PropertyInfo instanceProperty = panelDefType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                object panelDefInstance = instanceProperty?.GetValue(null, null);
                if (panelDefInstance == null)
                {
                    this.forceOpenShopStatus = "PanelDef instance unavailable.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                MethodInfo resolveMethod = panelDefType.GetMethod("TryGetPanelTypeByName", BindingFlags.Public | BindingFlags.Instance);
                if (resolveMethod == null)
                {
                    this.forceOpenShopStatus = "PanelDef.TryGetPanelTypeByName missing.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                object[] invokeArgs = new object[] { panelName, null };
                bool found = false;
                object result = resolveMethod.Invoke(panelDefInstance, invokeArgs);
                if (result is bool ok)
                {
                    found = ok;
                }

                panelType = invokeArgs[1] as Type;
                if (!found || panelType == null)
                {
                    this.forceOpenShopStatus = "Panel '" + panelName + "' not found in PanelDef.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Resolved panel '" + panelName + "' via PanelDef: " + panelType.FullName);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Panel resolve failed: " + ex.Message;
                this.LogForceOpenShop("PanelDef resolve exception: " + ex);
                return false;
            }
        }

        private bool HasAnyVisibleInteractPrompt()
        {
            string[] paths = new string[]
            {
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_chop@list/IconsBarWidget(Clone)/root_visible@go@group/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_mine@list/IconsBarWidget(Clone)/root_visible@go@group/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go@group/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_harvest@list/IconsBarWidget(Clone)/root_visible@go@group/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"
            };

            for (int i = 0; i < paths.Length; i++)
            {
                GameObject go = GameObject.Find(paths[i]);
                if (go != null && go.activeInHierarchy)
                {
                    Image img = go.GetComponent<Image>();
                    if (img != null && img.sprite != null)
                        return true;
                }
            }

            try
            {
                GameObject trackingPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)");
                if (trackingPanel != null && trackingPanel.activeInHierarchy)
                {
                    Image[] images = trackingPanel.GetComponentsInChildren<Image>(true);
                    if (images != null)
                    {
                        foreach (Image img in images)
                        {
                            if (img == null || img.gameObject == null || !img.gameObject.activeInHierarchy || img.sprite == null)
                                continue;
                            string fullPath = this.GetHierarchyPath(img.transform);
                            if (!string.IsNullOrEmpty(fullPath) &&
                                fullPath.Contains("/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"))
                                return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private bool ClickButtonIfExistsReturn(string path)
        {
            GameObject gameObject = GameObject.Find(path);
            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                return false;
            }

            Button component = this.ResolveClickableButton(gameObject);
            if (component != null && component.interactable)
            {
                component.onClick.Invoke();
                return true;
            }

            return this.SimulateClick(gameObject);
        }

        private void CloseAnnouncementPanelIfPresent()
        {
            if (!this.autoCloseAnnouncementEnabled) return;
            float now = Time.unscaledTime;
            if (now < this.nextAnnouncementCloseCheckAt)
            {
                return;
            }

            this.nextAnnouncementCloseCheckAt = now + 0.5f;
            try
            {
                GameObject btnObj = GameObject.Find(ANNOUNCEMENT_CLOSE_BUTTON_PATH);
                if (btnObj != null && btnObj.activeInHierarchy)
                {
                    bool clicked = this.ClickButtonIfExistsReturn(ANNOUNCEMENT_CLOSE_BUTTON_PATH);
                }
            }
            catch { }
        }

        private Button ResolveClickableButton(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            try
            {
                Button direct = target.GetComponent<Button>();
                if (direct != null)
                {
                    return direct;
                }

                Button child = target.GetComponentInChildren<Button>(true);
                if (child != null)
                {
                    return child;
                }

                Button parent = target.GetComponentInParent<Button>();
                if (parent != null)
                {
                    return parent;
                }

                Transform[] chain = new Transform[]
                {
                    target.transform,
                    target.transform.parent,
                    target.transform.parent != null ? target.transform.parent.parent : null
                };

                for (int i = 0; i < chain.Length; i++)
                {
                    Transform tr = chain[i];
                    if (tr == null) continue;
                    Component[] components = tr.GetComponents<Component>();
                    for (int j = 0; j < components.Length; j++)
                    {
                        Component comp = components[j];
                        if (comp == null) continue;
                        Type type = comp.GetType();

                        PropertyInfo unityComponentProp = type.GetProperty("unityComponent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (unityComponentProp != null && typeof(Button).IsAssignableFrom(unityComponentProp.PropertyType))
                        {
                            Button btn = unityComponentProp.GetValue(comp, null) as Button;
                            if (btn != null) return btn;
                        }

                        FieldInfo unityComponentField = type.GetField("unityComponent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (unityComponentField != null && typeof(Button).IsAssignableFrom(unityComponentField.FieldType))
                        {
                            Button btn = unityComponentField.GetValue(comp) as Button;
                            if (btn != null) return btn;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private void FinishCollectingCycle()
        {
            if (this.autoCollectClickedSinceArrival)
            {
                this.AutoFarmLog("Collect cycle stamped cooldown for node " + this.lastNodePosition);
                this.TryStampVisitedResourceNodeCooldown(this.lastNodePosition);
            }

            // Priority flow:
            // If no collect happened in a priority cycle, cooldown that priority location immediately.
            if (this.lastTeleportWasPriorityLocation && this.currentPriorityLocation.HasValue)
            {
                bool priorityLocationStillHasNodes = this.HasAvailablePriorityNodeForLocation(this.currentPriorityLocation.Value);
                if (priorityLocationStillHasNodes)
                {
                    this.AutoFarmLog("Priority location remains active: " + this.currentPriorityLocation.Value);
                    this.priorityLocationCooldowns.Remove(this.currentPriorityLocation.Value);
                }
                else
                {
                    this.AutoFarmLog("Priority location exhausted -> cooldown start: " + this.currentPriorityLocation.Value);
                    this.priorityLocationCooldowns[this.currentPriorityLocation.Value] = Time.unscaledTime;
                    this.currentPriorityLocation = null;
                }
            }

            this.lastTeleportWasPriorityLocation = false;
            this.farmState = HeartopiaComplete.AutoFarmState.ScanningForNodes;
            this.autoFarmTimer = 0f;
        }

        public void OnToastDetected(string msg)
        {
            this.OnToastDetected(msg, 0);
        }

        private void OnToastDetected(string msg, int toastObjectId)
        {
            try
            {
                if (string.IsNullOrEmpty(msg)) return;
                string s = msg.Trim();
                float now = Time.unscaledTime;

                // Avoid duplicate handling when both the UI hook and panel scanner see the same toast,
                // but still allow a new toast instance with identical text later.
                bool sameToastObject = toastObjectId != 0
                    && toastObjectId == this.lastDetectedToastObjectId
                    && string.Equals(s, this.lastDetectedToast, StringComparison.Ordinal);
                bool sameHookToastBurst = toastObjectId == 0
                    && string.Equals(s, this.lastDetectedToast, StringComparison.Ordinal)
                    && now - this.lastDetectedToastAt < 0.75f;
                if (sameToastObject || sameHookToastBurst) return;

                this.lastDetectedToast = s;
                this.lastDetectedToastObjectId = toastObjectId;
                this.lastDetectedToastAt = now;

                if (BirdNetFarm.IsAutoScareMaxPhotoEnabled && this.IsBirdFarmMaxPhotoToastMessage(s))
                {
                    this.BirdFarmNetLog("[MaxPhotoFallback] Limit toast observed: " + s);
                    this.TryHandleBirdFarmMaxPhotoAutoScare("toast fallback");
                    return;
                }

                // Durability notifications
                if (this.autoRepairOnToastEnabled && this.IsDurabilityToastMessage(s))
                {
                    this.AutoEatRepairLog("[AutoRepair] Durability toast observed; using toast fallback trigger.");
                    this.TryHandleDurabilityAutoRepairTrigger("toast fallback");
                    return;
                }

            }
            catch (Exception ex)
            {
                ModLogger.Msg("[OnToastDetected] Error: " + ex.Message);
            }
        }

        private void CheckToastPanel()
        {
            // For auto repair the toast text scan is only the last-resort trigger: skipped while
            // the HandHoldUpdatedEvent channel is live and durability reads are fresh (the event
            // path reacts faster and without UI scraping). Bird farm keeps its own need for it.
            bool repairNeedsToastScan = this.autoRepairOnToastEnabled && !this.IsDurabilityEventChannelHealthy();
            if (!repairNeedsToastScan && !BirdNetFarm.IsAutoScareMaxPhotoEnabled)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - this.lastToastCheckAt < TOAST_CHECK_INTERVAL) return;
            this.lastToastCheckAt = now;
            try
            {
                GameObject toastsRoot = this.cachedToastRootObj;
                if (toastsRoot == null || !toastsRoot.activeInHierarchy)
                {
                    this.cachedToastRootObj = null;
                    if (now < this.nextToastRootPathScanAt)
                    {
                        return;
                    }

                    this.nextToastRootPathScanAt = now + 2f;
                    toastsRoot = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Tip/TipPanel(Clone)/ToastPanel(Clone)/toasts@t");
                    if (toastsRoot == null)
                    {
                        return;
                    }

                    this.cachedToastRootObj = toastsRoot;
                }

                int childCount = toastsRoot.transform.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    Transform child = toastsRoot.transform.GetChild(i);
                    if (child == null || !child.gameObject.activeInHierarchy) continue;
                    Transform txtTransform = child.Find("AniRoot@ani/root_visible@go/root_visible/value@txt");
                    if (txtTransform == null) continue;
                    GameObject txtObj = txtTransform.gameObject;
                    if (txtObj == null || !txtObj.activeInHierarchy) continue;

                    string text = null;
                    var uiText = txtObj.GetComponent<UnityEngine.UI.Text>();
                    if (uiText != null) text = uiText.text;
                    else
                    {
                        foreach (Component comp in txtObj.GetComponents<Component>())
                        {
                            if (comp == null) continue;
                            try
                            {
                                var ilType = comp.GetIl2CppType();
                                if (ilType != null && ilType.Name == "XDText")
                                {
                                    var prop = ilType.GetProperty("text");
                                    if (prop != null)
                                    {
                                        var val = prop.GetValue(comp);
                                        text = (val != null) ? val.ToString() : null;
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        string s = text.Trim();
                        int toastObjectId = child.gameObject.GetInstanceID();
                        if (s != this.lastDetectedToast || toastObjectId != this.lastDetectedToastObjectId)
                        {
                            this.OnToastDetected(s, toastObjectId);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[CheckToastPanel] error: " + ex.Message);
            }
        }

    }
}
