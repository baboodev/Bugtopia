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
        private int GetJsonInt(string line, string key)
        {
            int startIdx = line.IndexOf(key);
            if (startIdx == -1) return 0;
            startIdx += key.Length;
            while (startIdx < line.Length && (line[startIdx] == ' ' || line[startIdx] == ':')) startIdx++;
            int endIdx = startIdx;
            while (endIdx < line.Length && line[endIdx] != ',' && line[endIdx] != '}') endIdx++;
            string valStr = line.Substring(startIdx, endIdx - startIdx).Trim();
            int result;
            if (int.TryParse(valStr, out result)) return result;
            return 0;
        }

        private string GetJsonString(string line, string key)
        {
            int startIdx = line.IndexOf(key);
            if (startIdx == -1) return "Unknown";
            startIdx += key.Length;
            
            // Find opening quote
            int quoteStart = line.IndexOf("\"", startIdx);
            if (quoteStart == -1) return "Unknown";
            
            // Find closing quote
            int quoteEnd = line.IndexOf("\"", quoteStart + 1);
            if (quoteEnd == -1) return "Unknown";
            
            return line.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        private float GetJsonFloat(string line, string key)
        {
            int startIdx = line.IndexOf(key);
            if (startIdx == -1) return 0f;
            startIdx += key.Length;
            
            // Find value start (skip spaces)
            while (startIdx < line.Length && (line[startIdx] == ' ' || line[startIdx] == ':')) startIdx++;
            
            // Find value end (comma or brace)
            int endIdx = startIdx;
            while (endIdx < line.Length && line[endIdx] != ',' && line[endIdx] != '}') endIdx++;
            
            string valStr = line.Substring(startIdx, endIdx - startIdx).Trim();
            float result;
            if (float.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result))
            {
                return result;
            }
            return 0f;
        }

    }
}
