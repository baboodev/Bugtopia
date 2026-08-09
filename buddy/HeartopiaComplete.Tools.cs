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

    }
}
