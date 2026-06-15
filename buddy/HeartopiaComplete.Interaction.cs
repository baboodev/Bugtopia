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

        private bool ToastContainsAllLocalizedPhrases(string message, params string[] phraseKeys)
        {
            if (string.IsNullOrWhiteSpace(message) || phraseKeys == null || phraseKeys.Length == 0)
            {
                return false;
            }

            foreach (string phraseKey in phraseKeys)
            {
                if (!this.ToastContainsLocalizedPhrase(message, phraseKey))
                {
                    return false;
                }
            }

            return true;
        }

        private unsafe bool TryOpenAuraPanelByTypeName(string fullTypeName, string successStatus)
        {
            if (this.TryOpenPanelByResolvedTypeName(fullTypeName, null, successStatus))
            {
                return true;
            }

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
                    uiManagerClass = this.FindAuraMonoClassInImages(
                        "XDTGame.Core",
                        "UIManager",
                        new string[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "Client", "Client.dll" });
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

        private bool TryInvokeStaticPanelOpen(string[] typeNames, string methodName, object[] args, string successStatus)
        {
            try
            {
                this.LogForceOpenShop("Resolving panel type for method '" + methodName + "' from candidates: " + string.Join(", ", typeNames ?? Array.Empty<string>()));
                Type panelType = this.FindLoadedType(typeNames);
                if (panelType == null)
                {
                    this.LogForceOpenShop("Managed type resolve failed. Trying IL2CPP type fallback.");
                    return this.TryInvokeIl2CppStaticPanelOpen(typeNames, methodName, args, successStatus);
                }

                this.LogForceOpenShop("Resolved panel type: " + panelType.FullName);
                MethodInfo[] methods = panelType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                foreach (MethodInfo method in methods)
                {
                    if (method == null || !string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != args.Length)
                    {
                        this.LogForceOpenShop("Skipping method '" + method.Name + "' on " + panelType.Name + " due to parameter count mismatch. Expected " + args.Length + ", found " + parameters.Length + ".");
                        continue;
                    }

                    this.LogForceOpenShop("Invoking " + panelType.Name + "." + method.Name + "(" + args.Length + " args)");
                    method.Invoke(null, args);
                    this.forceOpenShopStatus = successStatus;
                    this.LogForceOpenShop("Invoke succeeded: " + successStatus);
                    return true;
                }

                this.forceOpenShopStatus = methodName + " not found on " + panelType.Name + ".";
                this.LogForceOpenShop(this.forceOpenShopStatus);
                this.LogForceOpenShop("Falling back to IL2CPP static resolve after managed method lookup failure.");
                return this.TryInvokeIl2CppStaticPanelOpen(typeNames, methodName, args, successStatus);
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Open failed: " + ex.Message;
                this.LogForceOpenShop("Exception while opening panel: " + ex);
                return false;
            }
        }

        private bool TryInvokeIl2CppStaticPanelOpen(string[] typeNames, string methodName, object[] args, string successStatus)
        {
            try
            {
                Il2CppType il2CppType = this.TryGetNpcTeleportIl2CppType(typeNames);
                if (il2CppType == null)
                {
                    this.forceOpenShopStatus = "Panel type not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus + " IL2CPP fallback also failed.");
                    return false;
                }

                this.LogForceOpenShop("Resolved IL2CPP panel type: " + il2CppType.FullName);
                Il2CppMethodInfo method = il2CppType.GetMethod(methodName);
                if (method == null)
                {
                    this.forceOpenShopStatus = methodName + " not found on " + il2CppType.FullName + ".";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                Il2CppReferenceArray<Il2CppObject> invokeArgs = this.BuildIl2CppInvokeArgs(args);
                this.LogForceOpenShop("Invoking IL2CPP " + il2CppType.Name + "." + methodName + "(" + (args == null ? 0 : args.Length) + " args)");
                method.Invoke(null, invokeArgs);
                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("IL2CPP invoke succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Open failed: " + ex.Message;
                this.LogForceOpenShop("IL2CPP fallback exception: " + ex);
                return false;
            }
        }

        // Token: 0x06000010 RID: 16 RVA: 0x00003C60 File Offset: 0x00001E60
        private void RunAutoCollectLogic()
        {
            GameObject gameObject = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn");
            bool flag = gameObject == null || !gameObject.activeInHierarchy;
            if (!flag)
            {
                Image component = gameObject.GetComponent<Image>();
                bool flag2 = component == null || component.sprite == null;
                if (!flag2)
                {
                    string text = component.sprite.name.ToLower();
                    if (this.ShouldAutoCollectBySprite(text))
                    {
                        Button component2 = gameObject.GetComponent<Button>();
                        bool flag3 = component2 != null && component2.interactable;
                        if (flag3)
                        {
                            component2.onClick.Invoke();
                            this.autoCollectClickedSinceArrival = true;
                            this.TryMarkNearestNodeCollectedFromPrompt();
                            this.ClickButtonIfExists("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)/AniRoot@go@ani/exit@btn@go");
                        }
                    }
                }
            }
        }

        private bool IsPromptButtonReady(string path)
        {
            GameObject buttonObj = GameObject.Find(path);
            if (buttonObj == null || !buttonObj.activeInHierarchy)
            {
                return false;
            }

            Button btn = buttonObj.GetComponent<Button>();
            return btn != null && btn.interactable;
        }

        private bool HasReadyAutoCollectPrompt()
        {
            string[] paths = new string[]
            {
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_chop@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_mine@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_harvest@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"
            };

            for (int i = 0; i < paths.Length; i++)
            {
                if (this.IsReadyAutoCollectPromptButton(GameObject.Find(paths[i])))
                {
                    return true;
                }
            }

            try
            {
                GameObject trackingPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)");
                if (trackingPanel != null && trackingPanel.activeInHierarchy)
                {
                    Button[] buttons = trackingPanel.GetComponentsInChildren<Button>(true);
                    if (buttons != null)
                    {
                        foreach (Button btn in buttons)
                        {
                            if (btn == null || btn.gameObject == null)
                            {
                                continue;
                            }

                            string fullPath = this.GetHierarchyPath(btn.transform);
                            if (string.IsNullOrEmpty(fullPath) ||
                                !fullPath.Contains("/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"))
                            {
                                continue;
                            }

                            if (this.IsReadyAutoCollectPromptButton(btn.gameObject))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool HasAnyVisibleInteractPrompt()
        {
            string[] paths = new string[]
            {
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_chop@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_mine@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_harvest@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"
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

        private bool IsReadyAutoCollectPromptButton(GameObject gameObject)
        {
            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                return false;
            }

            Image component = gameObject.GetComponent<Image>();
            if (component == null || component.sprite == null)
            {
                return false;
            }

            string text = component.sprite.name.ToLowerInvariant();
            if (!this.ShouldAutoCollectBySprite(text))
            {
                return false;
            }

            Button component2 = gameObject.GetComponent<Button>();
            return component2 != null && component2.interactable;
        }

        private void DebugLogCurrentInteractSprite()
        {
            if (Time.unscaledTime < this.nextInteractSpriteDebugAt)
            {
                return;
            }

            this.nextInteractSpriteDebugAt = Time.unscaledTime + 0.2f;

            GameObject interactObj = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn");
            if (interactObj == null || !interactObj.activeInHierarchy)
            {
                return;
            }

            Image image = interactObj.GetComponent<Image>();
            if (image == null || image.sprite == null)
            {
                return;
            }

            string spriteName = image.sprite.name ?? string.Empty;
            if (string.IsNullOrEmpty(spriteName) || spriteName == this.lastLoggedInteractSpriteName)
            {
                return;
            }

            this.lastLoggedInteractSpriteName = spriteName;
            ModLogger.Msg("[AutoCollectDebug] Interact sprite: " + spriteName);
        }

        private bool ShouldAutoCollectBySprite(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                return false;
            }

            string text = spriteName.ToLowerInvariant();
            if (text.Contains("mushroom"))
            {
                return this.collectMushrooms;
            }

            if (text.Contains("interaction_8"))
            {
                string nearestLabel = this.GetNearestRadarNodeLabel(6f);
                if (!string.IsNullOrEmpty(nearestLabel))
                {
                    if (nearestLabel.Contains("Fiddlehead") || nearestLabel.Contains("Tall Mustard") || nearestLabel.Contains("Burdock") || nearestLabel.Contains("Mustard Greens"))
                    {
                        return this.collectEventResources;
                    }
                }

                return this.collectBerries;
            }

            if (text.Contains("wildvegetables"))
            {
                return this.collectEventResources;
            }

            return this.collectOther;
        }

        private bool IsAddButtonVisible()
        {
            GameObject gameObject = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)/AniRoot@queueanimation/detail@t/material@list");
            if (gameObject == null)
            {
                return false;
            }
            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                Transform transform = gameObject.transform.GetChild(i).Find("Root/cornerButton@frame/add@btn");
                if (transform != null && transform.gameObject.activeInHierarchy)
                {
                    return true;
                }
            }
            return false;
        }

        // Token: 0x06000012 RID: 18 RVA: 0x00003DE0 File Offset: 0x00001FE0
        private void ClickButtonIfExists(string path)
        {
            try
            {
                GameObject gameObject = GameObject.Find(path);
                if (gameObject == null) return;
                Button component = gameObject.GetComponent<Button>();
                if (component != null && gameObject.activeInHierarchy && component.interactable)
                {
                    component.onClick.Invoke();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg($"[ClickButtonIfExists] Error clicking path '{path}': {ex.Message}");
                this.lastAutoCookException = ex.Message;
            }
        }

        private bool ClickButtonIfExistsWithParent(string path)
        {
            GameObject gameObject = GameObject.Find(path);
            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                return false;
            }

            Button button = gameObject.GetComponent<Button>();
            if (button == null)
            {
                button = gameObject.GetComponentInParent<Button>();
            }
            if (button != null && button.interactable && button.gameObject.activeInHierarchy)
            {
                button.onClick.Invoke();
                return true;
            }

            return false;
        }

        private bool IsGatherWidgetVisible()
        {
            try
            {
                GameObject g = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/middle_center_layout@go/gather@go/GatherSelectWidget(Clone)");
                return g != null && g.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        private bool TryClickInteractPrompt()
        {
            // Try to trigger known in-game joystick/trigger objects first (most reliable)
            try
            {
                string[] triggerCandidates = new string[] { "GatherSelectWidget", "skill_main_hold@go@w", "main_joy@go@w" };
                foreach (string candidate in triggerCandidates)
                {
                    if (this.TryActivateTriggerByName(candidate))
                    {
                        ModLogger.Msg($"[TreeFarm] Activated trigger '{candidate}'");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg($"[TreeFarm] Trigger scan error: {ex.Message}");
            }

            // Fallback: try the tracking panel interact button
            try
            {
                GameObject trackingPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)");
                if (trackingPanel != null && trackingPanel.activeInHierarchy)
                {
                    Button[] buttons = trackingPanel.GetComponentsInChildren<Button>(true);
                    if (buttons != null && buttons.Length > 0)
                    {
                        foreach (Button btn in buttons)
                        {
                            if (btn == null || btn.gameObject == null || !btn.gameObject.activeInHierarchy || !btn.interactable)
                                continue;
                            string fullPath = this.GetHierarchyPath(btn.transform);
                            if (!string.IsNullOrEmpty(fullPath) &&
                                fullPath.Contains("/tracking_common@list/IconsBarWidget(Clone)/") &&
                                fullPath.Contains("/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"))
                            {
                                ModLogger.Msg("[TreeFarm] Found interact button in tracking panel, clicking");
                                btn.onClick.Invoke();
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg($"[TreeFarm] Error searching tracking panel: {ex.Message}");
            }

            // Try fixed path interact button
            if (this.ClickButtonIfExistsReturn(INTERACT_PROMPT_BUTTON_PATH))
            {
                ModLogger.Msg("[TreeFarm] Clicked interact button via path");
                return true;
            }

            // Try swing button
            if (this.ClickButtonIfExistsReturn("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_sand_swing@go@w/root_visible@go/swing@btn"))
            {
                ModLogger.Msg("[TreeFarm] Clicked swing button for interaction");
                return true;
            }

            // Last resort: send the F key simulation
            ModLogger.Msg("[TreeFarm] No UI trigger found, sending F key");
            this.SendFMessage();
            return true;
        }

        private bool TryActivateTriggerByName(string partialName)
        {
            // Use EventSystem.current; if none exists, we cannot simulate UI clicks safely
            if (EventSystem.current == null)
            {
                ModLogger.Msg("[Trigger] EventSystem.current is null; cannot activate UI triggers.");
                return false;
            }

            try
            {
                var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    if (!obj.activeInHierarchy) continue;
                    if (obj.name == null) continue;
                    if (!obj.name.Contains(partialName)) continue;

                    ModLogger.Msg($"[Trigger] Found object matching '{partialName}': {obj.name} - attempting activation");

                    // If object has a Button component, invoke it directly
                    Button btn = obj.GetComponent<Button>();
                    if (btn == null) btn = obj.GetComponentInParent<Button>();
                    if (btn != null && btn.interactable && btn.gameObject.activeInHierarchy)
                    {
                        try { btn.onClick.Invoke(); }
                        catch { }
                        return true;
                    }

                    // Otherwise simulate pointer events
                    var pointer = new PointerEventData(EventSystem.current);
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerEnterHandler);
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerClickHandler);
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.beginDragHandler);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg($"[Trigger] Error activating '{partialName}': {ex.Message}");
            }
            return false;
        }

        private bool PerformAutoSwing()
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<GameObject>();
                foreach (var obj in all)
                {
                    if (obj == null) continue;
                    if (!obj.activeInHierarchy) continue;
                    if (string.IsNullOrEmpty(obj.name)) continue;
                    string n = obj.name.ToLowerInvariant();
                    if (!(n.Contains("main_joy@go@w") || n.Contains("skill_main_hold@go@w") || n.Contains("gatherselectwidget"))) continue;

                    ModLogger.Msg($"[TreeFarm] PerformAutoSwing found trigger object: {obj.name}");

                    Button btn = obj.GetComponent<Button>();
                    if (btn == null) btn = obj.GetComponentInParent<Button>();
                    if (btn != null && btn.interactable && btn.gameObject.activeInHierarchy)
                    {
                        try { btn.onClick.Invoke(); } catch { }
                        return true;
                    }

                    if (EventSystem.current != null)
                    {
                        var pointer = new PointerEventData(EventSystem.current);
                        ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerEnterHandler);
                        ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerDownHandler);
                        ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerUpHandler);
                        ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerClickHandler);
                        ExecuteEvents.Execute(obj, pointer, ExecuteEvents.beginDragHandler);
                        return true;
                    }
                }

                // If no UI trigger was found, always try swing button or F key as fallback
                // Try swing button path
                if (this.ClickButtonIfExistsReturn(this.swingButtonPath))
                {
                    ModLogger.Msg("[TreeFarm] Performed fallback swing by clicking swing button");
                    return true;
                }
                // Last resort: send F
                ModLogger.Msg("[TreeFarm] No UI trigger - sending F key as fallback");
                this.SendFMessage();
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[TreeFarm] PerformAutoSwing error: " + ex.Message);
            }
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
            this.StopMeteorAutoInteractSequence();

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

        // Token: 0x06000019 RID: 25 RVA: 0x00004A34 File Offset: 0x00002C34
        private void SetAutoCollectEnabled(bool enabled, bool notify = true)
        {
            if (this.autoFarmEnabled == enabled)
            {
                return;
            }

            this.autoFarmEnabled = enabled;
            if (enabled && this.auraFarmEnabled)
            {
                this.SetAuraFarmEnabled(false);
            }

            ModLogger.Msg("Auto Collect " + (this.autoFarmEnabled ? "Enabled" : "Disabled"));
            if (notify)
            {
                this.AddMenuNotification($"Auto Collect {(this.autoFarmEnabled ? "enabled" : "disabled")}", this.autoFarmEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
            }
        }

        private void RunSpamClicker()
        {
            // Click buttons by path
            foreach (string path in workPaths)
            {
                ClickButtonIfExists(path);
            }
            ClickCookingCleanupThrottled(0.45f);
        }

        private void ForceCloseMenuIfOpen()
        {
            try
            {
                GameObject cookPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)");

                if (cookPanel != null && cookPanel.activeInHierarchy)
                {
                    // Method 1: Find UI Button
                    bool buttonFound = false;
                    Button[] buttons = cookPanel.GetComponentsInChildren<Button>(true);
                    if (buttons != null)
                    {
                        foreach (Button btn in buttons)
                        {
                            if (btn == null) continue;
                            try
                            {
                                string n = btn.name.ToLower();
                                if (n.Contains("close") || n.Contains("back") || n.Contains("exit") || n.Contains("return"))
                                {
                                    if (btn.interactable)
                                    {
                                        btn.onClick.Invoke();
                                        buttonFound = true;
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    // Method 2: Send ESC if button fail
                    if (!buttonFound)
                    {
                        SendEscMessage();
                    }
                }
            }
            catch { }
        }

        private void UpdateBottomDialogAutoClicker()
        {
            bool automationWantsDialogClick =
                this.autoBuyEnabled ||
                this.autoBuyBirdEnabled ||
                this.autoBuyGardenEnabled ||
                this.autoBuyFishingEnabled ||
                this.autoCookEnabled;
            GameObject bottomDialog = this.cachedBottomDialogObject;
            if (bottomDialog == null && !automationWantsDialogClick)
            {
                this.bottomDialogClickTimer = 0f;
                return;
            }

            if (bottomDialog == null && Time.unscaledTime >= this.nextBottomDialogLookupAt)
            {
                this.nextBottomDialogLookupAt = Time.unscaledTime + 0.5f;
                bottomDialog = GameObject.Find(BOTTOM_DIALOG_PATH);
                this.cachedBottomDialogObject = bottomDialog;
            }

            if (bottomDialog == null || !bottomDialog.activeInHierarchy)
            {
                if (bottomDialog != null && !bottomDialog.activeInHierarchy)
                {
                    this.cachedBottomDialogObject = null;
                }
                this.bottomDialogClickTimer = 0f;
                return;
            }

            this.bottomDialogClickTimer += Time.unscaledDeltaTime;
            if (this.bottomDialogClickTimer < BOTTOM_DIALOG_CLICK_INTERVAL)
            {
                return;
            }

            this.bottomDialogClickTimer = 0f;
            this.TryExecuteUiPointerClick(new Vector2((float)Screen.width / 2f, (float)Screen.height * 0.92f));
        }

        private bool TryExecuteUiPointerClick(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || !eventSystem.enabled)
            {
                return false;
            }

            PointerEventData pointerData = new PointerEventData(eventSystem);
            pointerData.button = PointerEventData.InputButton.Left;
            pointerData.position = screenPosition;

            Il2CppSystem.Collections.Generic.List<RaycastResult> hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, hits);
            if (hits.Count <= 0)
            {
                return false;
            }

            ExecuteEvents.Execute<IPointerClickHandler>(hits[0].gameObject, pointerData, ExecuteEvents.pointerClickHandler);
            return true;
        }

        private void DirectClickInteractButton()
        {
            try
            {
                try
                {
                    IntPtr hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                    if (hwnd == IntPtr.Zero)
                    {
                        hwnd = FindWindow("UnityWndClass", null);
                    }
                    if (hwnd != IntPtr.Zero)
                    {
                        IntPtr lParamDown = new IntPtr(2162689);
                        IntPtr lParamUp = new IntPtr(-1071579135);
                        PostMessage(hwnd, 256U, new IntPtr(70), lParamDown);
                        PostMessage(hwnd, 257U, new IntPtr(70), lParamUp);
                    }
                }
                catch {}

                string[] paths = new string[] {
                    "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/middle_right_layout@go/skill_bar@w@go/skill_bar@go/main_joy@go@w/Joy@ani",
                    "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/middle_right_layout@go/skill_bar@w@go/skill_bar@go/main_joy@go@w/Joy@ani/stick@frame/normal",
                    "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/middle_right_layout@go/skill_bar@w@go/skill_bar@go/main_joy@go@w",
                    "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_chop@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                    "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_mine@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                    "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                    "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/quick_action@btn",
                    "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_harvest@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"
                };
                foreach (string p in paths)
                {
                    GameObject btn = GameObject.Find(p);
                    if (btn != null && btn.activeInHierarchy)
                    {
                        DirectClickGameButton(btn);
                    }
                }
            }
            catch {}
        }

        private void DirectClickGameButton(GameObject buttonObj)
        {
            try
            {
                RectTransform rt = buttonObj.GetComponent<RectTransform>();
                Vector2 pos = Vector2.zero;
                if (rt != null)
                {
                    Vector3 worldPos = rt.position;
                    pos = new Vector2(worldPos.x, worldPos.y);
                }
                var pointer = new PointerEventData(EventSystem.current);
                pointer.button = PointerEventData.InputButton.Left;
                pointer.position = pos;
                pointer.pressPosition = pos;
                pointer.pointerPress = buttonObj;
                pointer.rawPointerPress = buttonObj;
                pointer.pointerEnter = buttonObj;
                pointer.clickCount = 1;
                pointer.eligibleForClick = true;
                ExecuteEvents.Execute<IPointerEnterHandler>(buttonObj, pointer, ExecuteEvents.pointerEnterHandler);
                ExecuteEvents.Execute<IPointerDownHandler>(buttonObj, pointer, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute<IPointerUpHandler>(buttonObj, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute<IPointerClickHandler>(buttonObj, pointer, ExecuteEvents.pointerClickHandler);
                Button b = buttonObj.GetComponent<Button>();
                if (b != null && b.interactable)
                {
                    try { b.onClick.Invoke(); } catch {}
                }
            }
            catch {}
        }

        private bool TryCloseAlertRewardPopupViaTipManager()
        {
            try
            {
                if (this.TryCloseAlertRewardPopupViaTipManagerMono())
                {
                    return true;
                }

                // Resolve types and methods once and cache them (avoids repeated FindLoadedType + GetMethods per 0.12s call)
                if (!this.tipManagerReflectionInitialized)
                {
                    this.tipManagerReflectionInitialized = true;
                    this.cachedTipManagerType = this.FindLoadedType("XDTGame.UI.Panel.Tip.TipManager", "TipManager");
                    this.cachedAlertRewardPanelType = this.FindLoadedType("XDTGame.UI.Panel.AlertRewardPanel", "AlertRewardPanel");
                    if (this.cachedTipManagerType != null)
                    {
                        this.cachedTipInstanceProp = this.cachedTipManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                        this.cachedGetTipByTypeMethod = this.cachedTipManagerType.GetMethod("GetTip", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(Type) }, null);
                        this.cachedTipPanelField = this.cachedTipManagerType.GetField("_tipPanel", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (this.cachedAlertRewardPanelType != null)
                        {
                            foreach (MethodInfo method in this.cachedTipManagerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                            {
                                if (method.Name == "GetTip" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                                    this.cachedGetTipGenericMethod = method.MakeGenericMethod(this.cachedAlertRewardPanelType);
                                else if (method.Name == "CloseTip" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                                    this.cachedCloseTipGenericMethod = method.MakeGenericMethod(this.cachedAlertRewardPanelType);
                            }
                            this.cachedAlertPanelClearMethod = this.cachedAlertRewardPanelType.GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
                        }
                    }
                }

                Type tipManagerType = this.cachedTipManagerType;
                Type alertRewardPanelType = this.cachedAlertRewardPanelType;
                if (tipManagerType == null || alertRewardPanelType == null)
                {
                    return false;
                }

                PropertyInfo instanceProp = this.cachedTipInstanceProp;
                object tipManager = instanceProp != null ? instanceProp.GetValue(null, null) : null;
                if (tipManager == null)
                {
                    return false;
                }

                MethodInfo getTipByTypeMethod = this.cachedGetTipByTypeMethod;
                object liveTip = getTipByTypeMethod != null ? getTipByTypeMethod.Invoke(tipManager, new object[] { alertRewardPanelType }) : null;
                if (liveTip != null)
                {
                    MethodInfo closeSelfGeneric = liveTip.GetType().GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (closeSelfGeneric != null && closeSelfGeneric.IsGenericMethodDefinition)
                    {
                        closeSelfGeneric.MakeGenericMethod(alertRewardPanelType).Invoke(liveTip, null);
                        return true;
                    }
                }

                MethodInfo getTipMethod = this.cachedGetTipGenericMethod;
                MethodInfo closeTipMethod = this.cachedCloseTipGenericMethod;

                if (getTipMethod != null)
                {
                    object tip = getTipMethod.Invoke(tipManager, null);
                    if (tip != null)
                    {
                        MethodInfo clearMethod = this.cachedAlertPanelClearMethod;
                        if (clearMethod != null)
                        {
                            clearMethod.Invoke(tip, null);
                        }
                    }
                }

                if (closeTipMethod != null)
                {
                    closeTipMethod.Invoke(tipManager, null);
                    return true;
                }

                FieldInfo tipPanelField = this.cachedTipPanelField;
                object tipPanel = tipPanelField != null ? tipPanelField.GetValue(tipManager) : null;
                if (tipPanel != null)
                {
                    FieldInfo tipClassifiesField = tipPanel.GetType().GetField("_tipClassifies", BindingFlags.Instance | BindingFlags.NonPublic);
                    System.Collections.IDictionary tipClassifies = tipClassifiesField != null ? tipClassifiesField.GetValue(tipPanel) as System.Collections.IDictionary : null;
                    if (tipClassifies != null)
                    {
                        foreach (object classifyObj in tipClassifies.Values)
                        {
                            if (classifyObj == null) continue;
                            MethodInfo closeTipByType = classifyObj.GetType().GetMethod("CloseTip", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(Type) }, null);
                            if (closeTipByType == null) continue;
                            closeTipByType.Invoke(classifyObj, new object[] { alertRewardPanelType });
                        }

                        if (getTipByTypeMethod != null)
                        {
                            object stillOpen = getTipByTypeMethod.Invoke(tipManager, new object[] { alertRewardPanelType });
                            if (stillOpen == null)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private unsafe bool TryCloseAlertRewardPopupViaTipManagerMono()
        {
            try
            {
                this.ResolveAuraFarmRuntimeMethodsViaMono();
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null || auraMonoStringNew == null || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr levelImage = this.FindAuraMonoImage(new string[]
                {
                    "XDTLevelAndEntity",
                    "XDTLevelAndEntity.dll"
                });
                IntPtr tipManagerClass = levelImage != IntPtr.Zero ? auraMonoClassFromName(levelImage, "XDTGame.UI.Panel.Tip", "TipManager") : IntPtr.Zero;
                if (tipManagerClass == IntPtr.Zero)
                {
                    tipManagerClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.UI.Panel.Tip", "TipManager");
                }

                IntPtr alertRewardPanelClass = levelImage != IntPtr.Zero ? auraMonoClassFromName(levelImage, "XDTGame.UI.Panel", "AlertRewardPanel") : IntPtr.Zero;
                if (alertRewardPanelClass == IntPtr.Zero)
                {
                    alertRewardPanelClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.UI.Panel", "AlertRewardPanel");
                }

                if (tipManagerClass == IntPtr.Zero || alertRewardPanelClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getInstanceMethod = auraMonoClassGetMethodFromName(tipManagerClass, "get_Instance", 0);
                IntPtr getTipByTypeMethod = auraMonoClassGetMethodFromName(tipManagerClass, "GetTip", 1);
                IntPtr clearMethod = auraMonoClassGetMethodFromName(alertRewardPanelClass, "Clear", 0);
                IntPtr displayMethod = auraMonoClassGetMethodFromName(alertRewardPanelClass, "Display", 0);
                if (getInstanceMethod == IntPtr.Zero || getTipByTypeMethod == IntPtr.Zero || clearMethod == IntPtr.Zero || displayMethod == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr tipManagerObj = IntPtr.Zero;
                IntPtr exc = IntPtr.Zero;
                tipManagerObj = auraMonoRuntimeInvoke(getInstanceMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || tipManagerObj == IntPtr.Zero)
                {
                    return false;
                }

                string[] typeNameCandidates = new string[]
                {
                    "XDTGame.UI.Panel.AlertRewardPanel, XDTLevelAndEntity",
                    "XDTGame.UI.Panel.AlertRewardPanel, XDTLevelAndEntity.dll"
                };

                IntPtr typeObj = IntPtr.Zero;
                for (int i = 0; i < typeNameCandidates.Length && typeObj == IntPtr.Zero; i++)
                {
                    IntPtr typeNameStr = auraMonoStringNew(this.auraMonoRootDomain, typeNameCandidates[i]);
                    if (typeNameStr == IntPtr.Zero)
                    {
                        continue;
                    }

                    exc = IntPtr.Zero;
                    IntPtr* typeArgs = stackalloc IntPtr[1];
                    typeArgs[0] = typeNameStr;
                    typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        typeObj = IntPtr.Zero;
                    }
                }

                if (typeObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr tipObj = IntPtr.Zero;
                exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = typeObj;
                tipObj = auraMonoRuntimeInvoke(getTipByTypeMethod, tipManagerObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || tipObj == IntPtr.Zero)
                {
                    return false;
                }

                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(clearMethod, tipObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    return false;
                }

                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(displayMethod, tipObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    return false;
                }

                exc = IntPtr.Zero;
                IntPtr stillOpen = auraMonoRuntimeInvoke(getTipByTypeMethod, tipManagerObj, (IntPtr)args, ref exc);
                return exc == IntPtr.Zero && stillOpen == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private bool DirectClickGameButtonReturn(GameObject buttonObj)
        {
            if (buttonObj == null || !buttonObj.activeInHierarchy)
            {
                return false;
            }

            try
            {
                this.DirectClickGameButton(buttonObj);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryInvokeAlertRewardPanelConfirm(GameObject rewardPanel)
        {
            if (rewardPanel == null)
            {
                return false;
            }

            try
            {
                Transform[] chain = new Transform[]
                {
                    rewardPanel.transform,
                    rewardPanel.transform.parent,
                    rewardPanel.transform.parent != null ? rewardPanel.transform.parent.parent : null
                };

                for (int i = 0; i < chain.Length; i++)
                {
                    Transform tr = chain[i];
                    if (tr == null) continue;
                    Component[] components = tr.GetComponents<Component>();
                    for (int j = 0; j < components.Length; j++)
                    {
                        Component component = components[j];
                        if (component == null) continue;
                        Type type = component.GetType();
                        string typeName = type.Name ?? string.Empty;
                        if (!typeName.Equals("AlertRewardPanel", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        MethodInfo clickConfirm = type.GetMethod("ClickConfirm", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (clickConfirm != null)
                        {
                            ParameterInfo[] parameters = clickConfirm.GetParameters();
                            object[] args = parameters.Length == 1 ? new object[] { null } : null;
                            clickConfirm.Invoke(component, args);
                            return true;
                        }

                        MethodInfo display = type.GetMethod("Display", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (display != null)
                        {
                            display.Invoke(component, null);
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private bool ClickDialogueOptionByTitle(string title)
        {
            try
            {
                GameObject panel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                if (panel == null || !panel.activeInHierarchy) return false;
                Transform viewportContent = panel.transform.Find("AniRoot@go@ani/option@w/option@list/Viewport/Content");
                if (viewportContent == null)
                {
                    var allChildren = panel.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < allChildren.Length; i++)
                    {
                        var child = allChildren[i];
                        if (child != null && child.name != null && child.name.Contains("ImageTextBtnWidget")) { viewportContent = child.parent; break; }
                    }
                    if (viewportContent == null) return false;
                }
                string lower = title.ToLowerInvariant();
                int childCount = viewportContent.childCount;
                for (int ci = 0; ci < childCount; ci++)
                {
                    Transform cell = viewportContent.GetChild(ci);
                    if (cell == null) continue;
                    var titleTxtTransform = cell.Find("AniRoot@go@ani/cell@btn/title@txt");
                    Text titleTxt = null;
                    if (titleTxtTransform != null) titleTxt = titleTxtTransform.GetComponent<Text>();
                    if (titleTxt == null) titleTxt = cell.GetComponentInChildren<Text>(true);
                    if (titleTxt != null && titleTxt.text != null && titleTxt.text.ToLowerInvariant().Contains(lower))
                    {
                        var btn = cell.GetComponentInChildren<Button>(true);
                        if (btn != null && btn.interactable) { btn.onClick.Invoke(); if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] Selected dialog option \"{titleTxt.text}\""); } return true; }
                        if (SimulateClick(cell.gameObject)) { if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] SimClicked dialog option \"{titleTxt.text}\""); } return true; }
                    }
                }
            }
            catch (Exception ex) { LogAutoBuy(" ClickDialogueOptionByTitle error: " + ex.Message); }
            return false;
        }

        private bool ClickDialogueOptionByKeywords(string[] keywords)
        {
            try
            {
                GameObject panel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                if (panel == null || !panel.activeInHierarchy) return false;

                Transform viewportContent = panel.transform.Find("AniRoot@go@ani/option@w/option@list/Viewport/Content");
                if (viewportContent == null || viewportContent.childCount <= 0) return false;

                int bestIndex = -1;
                int bestScore = int.MinValue;

                for (int ci = 0; ci < viewportContent.childCount; ci++)
                {
                    Transform cell = viewportContent.GetChild(ci);
                    if (cell == null || !cell.gameObject.activeInHierarchy) continue;

                    string textLower = string.Empty;
                    Text titleTxt = cell.Find("AniRoot@go@ani/cell@btn/title@txt")?.GetComponent<Text>() ?? cell.GetComponentInChildren<Text>(true);
                    if (titleTxt != null && !string.IsNullOrEmpty(titleTxt.text))
                    {
                        textLower = titleTxt.text.ToLowerInvariant();
                    }

                    int score = 0;
                    if (!string.IsNullOrEmpty(textLower))
                    {
                        for (int k = 0; k < keywords.Length; k++)
                        {
                            string kw = keywords[k];
                            if (string.IsNullOrEmpty(kw)) continue;
                            string kwLower = kw.ToLowerInvariant();
                            if (textLower.Contains(kwLower)) score += 20;
                        }

                        if (textLower.Contains("cooking store")) score += 100;
                        else if (textLower.Contains("cook") && textLower.Contains("store")) score += 80;
                    }

                    // Icon fallback: shopping/cart-like option often has a cart/store icon.
                    Image[] imgs = cell.GetComponentsInChildren<Image>(true);
                    for (int i = 0; i < imgs.Length; i++)
                    {
                        Image im = imgs[i];
                        if (im == null || im.sprite == null) continue;
                        string sp = im.sprite.name.ToLowerInvariant();
                        if (sp.Contains("shop") || sp.Contains("store") || sp.Contains("cart")) score += 25;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = ci;
                    }
                }

                if (bestIndex < 0 || bestScore <= 0) return false;

                Transform bestCell = viewportContent.GetChild(bestIndex);
                if (bestCell == null) return false;

                Button btn = bestCell.GetComponentInChildren<Button>(true);
                if (btn != null && btn.interactable)
                {
                    btn.onClick.Invoke();
                    LogAutoBuy(" Selected dialog option by keyword score=" + bestScore);
                    return true;
                }

                if (SimulateClick(bestCell.gameObject))
                {
                    LogAutoBuy(" SimClicked dialog option by keyword score=" + bestScore);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogAutoBuy(" ClickDialogueOptionByKeywords error: " + ex.Message);
            }

            return false;
        }

        private bool HasDialogueOptionsVisible()
        {
            try
            {
                GameObject panel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                if (panel == null || !panel.activeInHierarchy) return false;

                Transform viewportContent = panel.transform.Find("AniRoot@go@ani/option@w/option@list/Viewport/Content");
                if (viewportContent == null) return false;
                if (viewportContent.childCount <= 0) return false;

                for (int i = 0; i < viewportContent.childCount; i++)
                {
                    Transform cell = viewportContent.GetChild(i);
                    if (cell == null || !cell.gameObject.activeInHierarchy) continue;
                    Text t = cell.GetComponentInChildren<Text>(true);
                    if (t != null && !string.IsNullOrWhiteSpace(t.text)) return true;
                }
            }
            catch { }

            return false;
        }

        private bool TryAdvanceDialogueText()
        {
            try
            {
                GameObject panel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                if (panel == null || !panel.activeInHierarchy) return false;

                // First try explicit next/continue/skip style buttons.
                Button[] buttons = panel.GetComponentsInChildren<Button>(true);
                for (int i = 0; i < buttons.Length; i++)
                {
                    Button b = buttons[i];
                    if (b == null || !b.interactable || !b.gameObject.activeInHierarchy) continue;
                    string n = (b.name ?? string.Empty).ToLowerInvariant();
                    if (n.Contains("next") || n.Contains("continue") || n.Contains("skip") || n.Contains("content"))
                    {
                        b.onClick.Invoke();
                        return true;
                    }
                }

                // Fallback: click likely dialogue content regions to finish typewriter text.
                string[] clickPaths = new string[]
                {
                    // Exact paths from UI inspector (most reliable for skipping typewriter text)
                    "AniRoot@go@ani/main@go/DialogMsgWidget@go@w/content@go/text@list@t/Viewport/textContent@t/DialogueTextWidget(Clone)/content@txt",
                    "AniRoot@go@ani/main@go/DialogMsgWidget@go@w/content@go/bg",
                    "AniRoot@go@ani/content@w",
                    "AniRoot@go@ani/content@w/content@txt",
                    "AniRoot@go@ani/content@w/content@go",
                    "AniRoot@go@ani/main@go/DialogMsgWidget@go@w/content@go",
                    "AniRoot@go@ani"
                };

                bool clickedAny = false;
                for (int i = 0; i < clickPaths.Length; i++)
                {
                    Transform t = panel.transform.Find(clickPaths[i]);
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    if (SimulateClick(t.gameObject)) clickedAny = true;
                }

                if (clickedAny) return true;

                // Broad fallback: find the first interactable button that is NOT inside the options
                // list AND not a back/exit/close button. Catches transparent click-to-advance buttons
                // used by simple farewell dialogues like "Thank you for your patronage."
                Transform optionContent = panel.transform.Find("AniRoot@go@ani/option@w/option@list/Viewport/Content");
                Button[] allButtons = panel.GetComponentsInChildren<Button>(true);
                for (int i = 0; i < allButtons.Length; i++)
                {
                    Button b = allButtons[i];
                    if (b == null || !b.interactable || !b.gameObject.activeInHierarchy) continue;
                    if (optionContent != null && b.transform.IsChildOf(optionContent)) continue;
                    string n = (b.name ?? string.Empty).ToLowerInvariant();
                    if (n.Contains("back") || n.Contains("exit") || n.Contains("close") || n.Contains("cancel")) continue;
                    b.onClick.Invoke();
                    return true;
                }

                return SimulateClick(panel);
            }
            catch { }

            return false;
        }

        private int GetSalePanelCurrentCount(GameObject sale)
        {
            try
            {
                var countControl = sale.transform.Find("AniRoot/popup/content/bottom/countControl@w@go/countControl@go");
                if (countControl == null) countControl = sale.transform.Find("AniRoot/popup/content/bottom/countControl@go");
                if (countControl != null)
                {
                    var texts = countControl.GetComponentsInChildren<Text>(true);
                    foreach (var t in texts)
                    {
                        if (t == null || string.IsNullOrEmpty(t.text)) continue;
                        string s = t.text.Trim();
                        // try parse int
                        int val;
                        if (int.TryParse(s, out val)) return val;
                        // sometimes label shows '1' with spaces
                        string digits = new string(s.Where(c => char.IsDigit(c)).ToArray());
                        if (digits.Length > 0 && int.TryParse(digits, out val)) return val;
                    }
                }
            }
            catch { }
            return -1;
        }

        private int GetSalePanelRemainingStock(GameObject sale)
        {
            try
            {
                // Try the remain@txt@go path first (shows "Remaining: X")
                var remainGo = sale.transform.Find("AniRoot/popup/content/bottom/remain@txt@go");
                if (remainGo != null)
                {
                    var texts = remainGo.GetComponentsInChildren<Text>(true);
                    foreach (var t in texts)
                    {
                        if (t == null || string.IsNullOrEmpty(t.text)) continue;
                        string s = t.text.Trim();
                        // Extract number from "Remaining: 39" format
                        string digits = new string(s.Where(c => char.IsDigit(c)).ToArray());
                        if (digits.Length > 0 && int.TryParse(digits, out int val)) return val;
                    }
                }
            }
            catch { }
            return -1; // Unknown remaining stock
        }

        private bool ClickSaleAddMore(GameObject sale)
        {
            try
            {
                var btn = sale.transform.Find("AniRoot/popup/content/bottom/countControl@w@go/countControl@go/addMore@btn")
                          ?? sale.transform.Find("AniRoot/popup/content/bottom/countControl@go/addMore@btn");
                if (btn != null)
                {
                    var b = btn.GetComponent<Button>();
                    if (b != null && b.interactable) { b.onClick.Invoke(); LogAutoBuy(" Clicked +10"); return true; }
                    if (SimulateClick(btn.gameObject)) { LogAutoBuy(" SimClicked +10"); return true; }
                }
            }
            catch { }
            return false;
        }

        private bool ClickSalePurchase(GameObject sale)
        {
            try
            {
                var buy = sale.transform.Find("AniRoot/popup/operators/buy@btn") ?? sale.transform.Find("AniRoot/popup/operators/buy@btn/buy@btn");
                if (buy != null)
                {
                    var b = buy.GetComponent<Button>() ?? buy.GetComponentInChildren<Button>(true);
                    if (b != null && b.interactable) { b.onClick.Invoke(); LogAutoBuy(" Clicked Purchase"); return true; }
                    if (SimulateClick(buy.gameObject)) { LogAutoBuy(" SimClicked Purchase"); return true; }
                }
            }
            catch { }
            return false;
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
            if (!this.autoRepairOnToastEnabled && !BirdNetFarm.IsAutoScareMaxPhotoEnabled)
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
                    this.cachedToastTextObj = null;
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
