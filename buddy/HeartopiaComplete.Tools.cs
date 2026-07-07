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
        public bool TryGetCurrentToolInfo(out int toolId, out string toolName, out string status)
        {
            toolId = 0;
            toolName = string.Empty;
            status = "Unknown";

            if (!this.TryGetCurrentToolDurability(out toolId, out _, out _, out status))
            {
                return false;
            }

            toolName = this.GetAutoRepairSupportedToolName(toolId);
            return true;
        }

        private Type FindLoadedToolServiceType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    bool nameMatch = string.Equals(type.Name, "IToolService", StringComparison.Ordinal)
                        || string.Equals(type.Name, "ToolService", StringComparison.Ordinal);
                    if (!nameMatch)
                    {
                        continue;
                    }

                    MethodInfo tryGetTakenToolMethod = type.GetMethod("TryGetTakenTool", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    MethodInfo getToolDurabilityMethod = type.GetMethod("GetToolDurability", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    MethodInfo tryGetToolComponentMethod = type.GetMethod("TryGetToolComponent", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (tryGetTakenToolMethod != null && (getToolDurabilityMethod != null || tryGetToolComponentMethod != null))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private bool TryGetCurrentHandholdObject(out object handholdObj, out string source)
        {
            handholdObj = null;
            source = "none";

            try
            {
                if (this.TryGetManagedInteractSystemObject(out object interactSystemObj, out string interactSource) && interactSystemObj != null)
                {
                    foreach (string memberName in new string[] { "_handhold", "handhold" })
                    {
                        if (this.TryGetObjectMember(interactSystemObj, memberName, out handholdObj) && handholdObj != null)
                        {
                            source = interactSource + " -> " + interactSystemObj.GetType().Name + "." + memberName;
                            return true;
                        }
                    }

                    source = interactSource + " -> handhold";
                }

                object playerObj = null;
                string playerSource = string.Empty;
                if (!this.TryGetManagedSelfPlayerObject(out playerObj, out playerSource) || playerObj == null)
                {
                    if (!this.TryGetManagedInteractPlayerObject(interactSystemObj, out playerObj, out playerSource) || playerObj == null)
                    {
                        source = interactSystemObj != null ? interactSource + " -> player" : "Self player unavailable";
                        return false;
                    }
                }

                object equipComponent;
                if (!(this.TryInvokeZeroArgMember(playerObj, out equipComponent, "get_equipComponent", "GetEquipComponent")
                    || this.TryGetObjectMember(playerObj, "equipComponent", out equipComponent)
                    || this.TryGetObjectMember(playerObj, "_equipComponent", out equipComponent))
                    || equipComponent == null)
                {
                    source = playerSource + " -> equipComponent";
                    return false;
                }

                if ((this.TryInvokeZeroArgMember(equipComponent, out handholdObj, "get_handhold", "GetHandhold")
                    || this.TryGetObjectMember(equipComponent, "handhold", out handholdObj)
                    || this.TryGetObjectMember(equipComponent, "_handhold", out handholdObj)) && handholdObj != null)
                {
                    source = playerSource + " -> " + equipComponent.GetType().Name + ".handhold";
                    return true;
                }

                foreach (string memberName in new string[] { "_handhold", "handhold" })
                {
                    if (this.TryGetObjectMember(playerObj, memberName, out handholdObj) && handholdObj != null)
                    {
                        source = playerSource + " -> " + playerObj.GetType().Name + "." + memberName;
                        return true;
                    }
                }

                source = playerSource + " -> " + equipComponent.GetType().Name + ".handhold";
                return false;
            }
            catch (Exception ex)
            {
                source = "exception: " + ex.Message;
                return false;
            }
        }

        private bool IsSelectedToolInUse()
        {
            GameObject equippedGo = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ToolsPanel(Clone)/content/infoBar/layout/equiped@go");
            if (equippedGo == null || !equippedGo.activeInHierarchy)
            {
                return false;
            }

            Text txt = equippedGo.GetComponent<Text>();
            if (txt == null)
            {
                txt = equippedGo.GetComponentInChildren<Text>(true);
            }
            if (txt == null || string.IsNullOrEmpty(txt.text))
            {
                return true; // Active badge with no readable text still indicates equipped.
            }

            string label = txt.text.Trim().ToLowerInvariant();
            return label.Contains("in use") || label.Contains("equipped");
        }

        private void WithdrawHeldTools()
        {
            try
            {
                GameObject statusPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)");
                if (statusPanel != null && statusPanel.activeInHierarchy)
                {
                    Button[] buttons = statusPanel.GetComponentsInChildren<Button>(true);
                    if (buttons != null && buttons.Length > 0)
                    {
                        foreach (Button btn in buttons)
                        {
                            if (btn == null || btn.gameObject == null || !btn.gameObject.activeInHierarchy || !btn.interactable)
                            {
                                continue;
                            }

                            string fullPath = this.GetHierarchyPath(btn.transform);
                            if (!string.IsNullOrEmpty(fullPath) &&
                                fullPath.Contains("/CommonIconForTool(Clone)/") &&
                                fullPath.Contains("/icon@img@btn"))
                            {
                                btn.onClick.Invoke();
                                return; // Withdraw one tool at a time
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private unsafe bool TryInvokeToolRestorerProtocol(uint netId)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol unavailable: Mono API not ready.");
                    return false;
                }

                int staticId = this.lastDirectBackpackMatchedStaticId;
                if (staticId == 0)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol unavailable: staticId missing for netId=" + netId);
                    return false;
                }

                IntPtr dataImage = this.FindAuraMonoImage(new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll", "Client", "Client.dll" });
                IntPtr protocolClass = dataImage != IntPtr.Zero ? auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.ItemDisplay", "ToolRestorerProtocolManager") : IntPtr.Zero;
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.ItemDisplay", "ToolRestorerProtocolManager");
                }

                IntPtr method = protocolClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(protocolClass, "CanPutRestorer", 2) : IntPtr.Zero;
                this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol lookup. class=0x" + protocolClass.ToString("X") + " method=0x" + method.ToString("X") + " netId=" + netId + " staticId=" + staticId);
                if (method == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&netId);
                args[1] = (IntPtr)(&staticId);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol raised exception.");
                    return false;
                }

                this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol sent. netId=" + netId + " staticId=" + staticId);
                return true;
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol exception: " + ex.Message);
                return false;
            }
        }

    }
}
