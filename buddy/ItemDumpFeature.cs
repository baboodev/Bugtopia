using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private string TryResolveItemDumpEntityName(int staticId, object entityObj, MethodInfo localizeMethod)
        {
            if (staticId > 0
                && this.TryGetResolvedFoodNameFromStaticId(staticId, out string resolved)
                && !this.IsPoorBagItemDisplayName(resolved, staticId))
            {
                return resolved;
            }

            string legacy = this.TryReadTableEntityNameManaged(entityObj, localizeMethod);
            if (!this.IsPoorBagItemDisplayName(legacy, staticId))
            {
                return legacy;
            }

            return string.Empty;
        }

        private string TryReadTableEntityNameManaged(object entityObj, MethodInfo localizeMethod)
        {
            if (this.TryGetObjectMember(entityObj, "name", out object nameObj) && nameObj != null)
            {
                string localized = Convert.ToString(nameObj);
                if (!string.IsNullOrWhiteSpace(localized))
                {
                    return localized;
                }
            }

            string rawName = this.TryReadObjectString(entityObj, "_name");
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            if (localizeMethod != null)
            {
                try
                {
                    object localizedObj = localizeMethod.Invoke(null, new object[] { rawName });
                    if (localizedObj != null)
                    {
                        return Convert.ToString(localizedObj) ?? rawName;
                    }
                }
                catch
                {
                }
            }

            return rawName;
        }

        private unsafe string TryReadTableEntityNameAura(IntPtr entityObj, IntPtr localizeMethod)
        {
            string rawName = this.TryReadMonoStringMemberOrEmpty(entityObj, "_name");
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            if (localizeMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null || auraMonoStringNew == null)
            {
                return rawName;
            }

            IntPtr rawNameObj = auraMonoStringNew(this.auraMonoRootDomain, rawName);
            if (rawNameObj == IntPtr.Zero)
            {
                return rawName;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = rawNameObj;
            IntPtr exc = IntPtr.Zero;
            IntPtr resultObj = auraMonoRuntimeInvoke(localizeMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || resultObj == IntPtr.Zero)
            {
                return rawName;
            }

            if (this.TryReadMonoString(resultObj, out string localized) && !string.IsNullOrWhiteSpace(localized))
            {
                return localized;
            }

            return rawName;
        }

        private ItemDumpEntityNameResolver CreateItemDumpEntityNameResolver()
        {
            ItemDumpEntityNameResolver resolver = new ItemDumpEntityNameResolver();
            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType != null)
                {
                    resolver.LocalizeMethod = tableDataType.GetMethod(
                        "Localize",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(string) },
                        null);
                    resolver.GetEntityMethod = null;
                    foreach (MethodInfo method in tableDataType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (!string.Equals(method.Name, "GetEntity", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                        {
                            resolver.GetEntityMethod = method;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }

            if (this.EnsureAuraMonoApiReady())
            {
                IntPtr tableDataClass = this.FindAuraMonoTableDataClass();
                if (tableDataClass != IntPtr.Zero)
                {
                    resolver.AuraLocalizeMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "Localize", 1);
                    resolver.AuraGetEntityMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntity", 1);
                    if (resolver.AuraGetEntityMethod == IntPtr.Zero)
                    {
                        resolver.AuraGetEntityMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntity", 2);
                    }
                }
            }

            return resolver;
        }

        private sealed class ItemDumpEntityNameResolver
        {
            private readonly Dictionary<int, string> cache = new Dictionary<int, string>();

            public MethodInfo LocalizeMethod;
            public MethodInfo GetEntityMethod;
            public IntPtr AuraLocalizeMethod;
            public IntPtr AuraGetEntityMethod;

            public string Resolve(int staticId)
            {
                if (staticId <= 0)
                {
                    return string.Empty;
                }

                if (this.cache.TryGetValue(staticId, out string cached))
                {
                    return cached;
                }

                string name = this.ResolveUncached(staticId);
                this.cache[staticId] = name ?? string.Empty;
                return name ?? string.Empty;
            }

            private string ResolveUncached(int staticId)
            {
                HeartopiaComplete host = HeartopiaComplete.Instance;
                if (host == null)
                {
                    return string.Empty;
                }

                if (host.TryGetResolvedFoodNameFromStaticId(staticId, out string resolved)
                    && !host.IsPoorBagItemDisplayName(resolved, staticId))
                {
                    return resolved;
                }

                if (this.GetEntityMethod != null && this.LocalizeMethod != null)
                {
                    try
                    {
                        object[] args = this.GetEntityMethod.GetParameters().Length >= 2
                            ? new object[] { staticId, false }
                            : new object[] { staticId };
                        if (this.GetEntityMethod.Invoke(null, args) is object entityObj)
                        {
                            string legacy = host.TryResolveItemDumpEntityName(staticId, entityObj, this.LocalizeMethod);
                            if (!host.IsPoorBagItemDisplayName(legacy, staticId))
                            {
                                return legacy;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                string auraLegacy = host.TryResolveEntityNameAura(staticId, this.AuraGetEntityMethod, this.AuraLocalizeMethod);
                if (!host.IsPoorBagItemDisplayName(auraLegacy, staticId))
                {
                    return auraLegacy;
                }

                return string.Empty;
            }
        }

        private unsafe string TryResolveEntityNameAura(int staticId, IntPtr getEntityMethod, IntPtr localizeMethod)
        {
            if (staticId <= 0 || getEntityMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return string.Empty;
            }

            int idValue = staticId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&idValue);
            IntPtr exc = IntPtr.Zero;
            IntPtr entityObj = auraMonoRuntimeInvoke(getEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || entityObj == IntPtr.Zero)
            {
                bool needException = false;
                IntPtr* args2 = stackalloc IntPtr[2];
                args2[0] = (IntPtr)(&idValue);
                args2[1] = (IntPtr)(&needException);
                exc = IntPtr.Zero;
                entityObj = auraMonoRuntimeInvoke(getEntityMethod, IntPtr.Zero, (IntPtr)args2, ref exc);
                if (exc != IntPtr.Zero || entityObj == IntPtr.Zero)
                {
                    return string.Empty;
                }
            }

            string rawName = this.TryReadMonoStringMemberOrEmpty(entityObj, "_name");
            if (string.IsNullOrWhiteSpace(rawName))
            {
                if (this.TryGetMonoStringMember(entityObj, "name", out string propertyName))
                {
                    return propertyName ?? string.Empty;
                }

                return string.Empty;
            }

            string legacy = this.TryReadTableEntityNameAura(entityObj, localizeMethod);
            if (!this.IsPoorBagItemDisplayName(legacy, staticId))
            {
                return legacy;
            }

            if (this.TryGetResolvedFoodNameFromStaticId(staticId, out string resolved)
                && !this.IsPoorBagItemDisplayName(resolved, staticId))
            {
                return resolved;
            }

            return string.Empty;
        }
    }
}
