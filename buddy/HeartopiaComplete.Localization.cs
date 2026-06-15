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
        private void InitializeLocalization()
        {
            string preferredLanguage = "en";
            try
            {
                UnifiedConfigData config = this.LoadUnifiedConfig();
                if (config != null && !string.IsNullOrWhiteSpace(config.Language))
                {
                    preferredLanguage = config.Language;
                }
            }
            catch
            {
            }

            LocalizationManager.Initialize(AppDomain.CurrentDomain.BaseDirectory, preferredLanguage);
            this.selectedLanguage = LocalizationManager.CurrentLanguage;
        }

        private string L(string text)
        {
            return LocalizationManager.Translate(text);
        }

        private string LF(string format, params object[] args)
        {
            return LocalizationManager.Format(format, args);
        }

        private void SetLocalizationLanguage(string languageCode, bool showNotification = true)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                languageCode = "en";
            }

            LocalizationManager.SetLanguage(languageCode);
            this.selectedLanguage = LocalizationManager.CurrentLanguage;
            this.SaveKeybinds(false);

            if (showNotification)
            {
                this.AddMenuNotification(this.LF("Language switched to {0}", LocalizationManager.GetLanguageDisplayName(this.selectedLanguage)), new Color(0.55f, 0.88f, 1f));
            }
        }

        private bool TryInvokeStaticTableGetter(Type tableDataType, string methodName, int id, out object row)
        {
            row = null;
            if (tableDataType == null || string.IsNullOrEmpty(methodName) || id <= 0)
            {
                return false;
            }

            try
            {
                MethodInfo[] methods = tableDataType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                    {
                        row = method.Invoke(null, new object[] { id });
                    }
                    else if (parameters.Length == 2 && parameters[0].ParameterType == typeof(int) && parameters[1].ParameterType == typeof(bool))
                    {
                        row = method.Invoke(null, new object[] { id, false });
                    }
                    else
                    {
                        continue;
                    }

                    return row != null;
                }
            }
            catch
            {
            }

            return false;
        }

        private MethodInfo FindTableLocalizationMethod(Type tableDataType)
        {
            if (tableDataType == null)
            {
                return null;
            }

            foreach (MethodInfo method in tableDataType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "GetLocalizationText", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                {
                    return method;
                }
            }

            return null;
        }

        private unsafe string TryGetLocalizationTextMono(IntPtr tableDataClass, int localizationId)
        {
            if (tableDataClass == IntPtr.Zero || localizationId <= 0 || auraMonoRuntimeInvoke == null)
            {
                return string.Empty;
            }

            try
            {
                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetLocalizationText", 1);
                int argCount = 1;
                if (methodPtr == IntPtr.Zero)
                {
                    methodPtr = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetLocalizationText", 2);
                    argCount = methodPtr != IntPtr.Zero ? 2 : 0;
                }

                if (methodPtr == IntPtr.Zero)
                {
                    return string.Empty;
                }

                IntPtr exc = IntPtr.Zero;
                int locIdArg = localizationId;
                bool needExceptionArg = false;
                if (argCount == 1)
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&locIdArg);
                    IntPtr resultObj = auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc == IntPtr.Zero && resultObj != IntPtr.Zero && this.TryReadMonoString(resultObj, out string value))
                    {
                        return value.Trim();
                    }
                }
                else if (argCount == 2)
                {
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)(&locIdArg);
                    args[1] = (IntPtr)(&needExceptionArg);
                    IntPtr resultObj = auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc == IntPtr.Zero && resultObj != IntPtr.Zero && this.TryReadMonoString(resultObj, out string value))
                    {
                        return value.Trim();
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private Type FindManagedTableDataType()
        {
            string[] fullNames = new[]
            {
                "EcsClient.TableData",
                "TableData",
                "Il2CppEcsClient.TableData",
                "Il2Cpp.TableData"
            };

            for (int i = 0; i < fullNames.Length; i++)
            {
                Type type = this.FindLoadedType(fullNames[i]);
                if (type != null)
                {
                    return type;
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null)
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                for (int i = 0; i < types.Length; i++)
                {
                    Type type = types[i];
                    if (type != null && string.Equals(type.Name, "TableData", StringComparison.Ordinal))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

    }
}
