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
        private int TryReadLevelEntityStaticIdViaAuraMonoEntity(IntPtr entityObj)
        {
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return 0;
            }

            try
            {
                if (!this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr componentsObj, "GetAllComponents") || componentsObj == IntPtr.Zero)
                {
                    return 0;
                }

                List<IntPtr> components = this.birdFarmAuraLevelEntityComponentsBuffer;
                components.Clear();
                if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components))
                {
                    return 0;
                }

                for (int i = 0; i < components.Count && i < 64; i++)
                {
                    IntPtr componentObj = components[i];
                    if (componentObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(componentObj));
                    if (className.EndsWith(".LevelEntityComponent", StringComparison.OrdinalIgnoreCase)
                        || className.IndexOf("LevelEntityComponent", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int value = this.TryReadBirdStaticIdViaAuraMonoObject(componentObj);
                        if (value > 0)
                        {
                            return value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.BirdFarmNetLog("TryReadLevelEntityStaticIdViaAuraMonoEntity error: " + ex.Message);
            }

            return 0;
        }

        private Type FindEntitiesRuntimeType()
        {
            Type resolved = this.FindLoadedType(
                "XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities",
                "ScriptsRefactory.LevelAndEntity.BaseSystem.EntitiesManager.Entities",
                "Entities");
            if (resolved != null)
            {
                return resolved;
            }

            try
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

                        if (!string.Equals(type.Name, "Entities", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Any(m => m.Name == "GetComponents" || m.Name == "SphereQueryEntities"))
                        {
                            return type;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private Type FindEntityRuntimeType()
        {
            Type resolved = this.FindLoadedType(
                "XDTLevelAndEntity.Core.World.Entity",
                "ScriptsRefactory.LevelAndEntity.Core.World.Entity",
                "Entity");
            if (resolved != null)
            {
                return resolved;
            }

            try
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
                        if (type == null || !string.Equals(type.Name, "Entity", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (type.GetMethod("GetAllComponents", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null)
                        {
                            return type;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private int TryGetEntityStaticId(uint netId)
        {
            try
            {
                Type entityUtilType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil", "EntityUtil");
                if (entityUtilType == null)
                {
                    return 0;
                }

                MethodInfo getEntityResIdMethod = entityUtilType.GetMethod("GetEntityResId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(uint) }, null);
                if (getEntityResIdMethod == null)
                {
                    return 0;
                }

                object rawStaticId = getEntityResIdMethod.Invoke(null, new object[] { netId });
                return Convert.ToInt32(rawStaticId);
            }
            catch
            {
                return 0;
            }
        }

        private bool TryGetEntityLevelEntityComponent(object entityObj, out object levelEntityComponent)
        {
            levelEntityComponent = null;

            try
            {
                if (!this.TryInvokeZeroArgMember(entityObj, out object componentsObj, "GetAllComponents") || componentsObj == null || !(componentsObj is IEnumerable components))
                {
                    return false;
                }

                foreach (object component in components)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    string typeName = component.GetType().FullName ?? component.GetType().Name ?? string.Empty;
                    if (typeName.IndexOf("LevelEntityComponent", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    levelEntityComponent = component;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        // Accepted entity pointers are pinned here so they survive the moving sgen GC after the scan
        // returns, until the caller finishes reading them (netId / position). Released at the start of
        // the next scan: by then the previous caller has consumed its entities. This is per-scan
        // (button / throttled), not per-frame, so the pin churn is fine.
        private readonly List<uint> _loadedEntityScanPins = new List<uint>();

        private bool TryEnumerateAuraMonoLoadedEntityObjects(out List<IntPtr> entityObjects, out string status)
        {
            entityObjects = new List<IntPtr>();
            status = "Aura mono entities unavailable";

            FreeAuraMonoPins(this._loadedEntityScanPins);

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "Aura mono API unavailable";
                    return false;
                }

                IntPtr entitiesClass = IntPtr.Zero;
                IntPtr levelImage = this.FindAuraMonoImage(new string[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll" });
                if (levelImage != IntPtr.Zero && auraMonoClassFromName != null)
                {
                    entitiesClass = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.BaseSystem.EntitiesManager", "Entities");
                    if (entitiesClass == IntPtr.Zero)
                    {
                        entitiesClass = auraMonoClassFromName(levelImage, "ScriptsRefactory.LevelAndEntity.BaseSystem.EntitiesManager", "Entities");
                    }
                }
                if (entitiesClass == IntPtr.Zero)
                {
                    entitiesClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.BaseSystem.EntitiesManager", "Entities");
                }
                if (entitiesClass == IntPtr.Zero)
                {
                    entitiesClass = this.FindAuraMonoClassAcrossLoadedAssemblies("ScriptsRefactory.LevelAndEntity.BaseSystem.EntitiesManager", "Entities");
                }
                if (entitiesClass == IntPtr.Zero)
                {
                    status = "Aura mono Entities class unavailable";
                    this.BirdFarmNetLog("Aura mono entity enumeration: Entities class not found.");
                    return false;
                }

                IntPtr getInstanceMethod = this.FindAuraMonoMethodOnHierarchy(entitiesClass, "get_Instance", 0);
                if (getInstanceMethod == IntPtr.Zero)
                {
                    getInstanceMethod = this.FindAuraMonoMethodOnHierarchy(entitiesClass, "GetInstance", 0);
                }
                if (getInstanceMethod == IntPtr.Zero)
                {
                    status = "Aura mono Entities.get_Instance unavailable";
                    this.BirdFarmNetLog("Aura mono entity enumeration: Entities.get_Instance not found.");
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr entitiesObj = auraMonoRuntimeInvoke(getInstanceMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || entitiesObj == IntPtr.Zero)
                {
                    status = "Aura mono Entities instance unavailable";
                    this.BirdFarmNetLog("Aura mono entity enumeration: Entities instance invoke failed.");
                    return false;
                }
                // Verbose logging disabled to reduce GC pressure
                // this.BirdFarmNetLog($"Aura mono entity enumeration: Entities instance=0x{entitiesObj.ToInt64():X}");

                IntPtr loadedCollection = this.TryGetAuraMonoLoadedEntitiesCollectionObject(entitiesObj);
                HashSet<long> seen = new HashSet<long>();
                HashSet<long> seenSourcePtrs = new HashSet<long>();
                HashSet<long> seenTraversalPtrs = new HashSet<long>();
                bool foundAny = false;
                if (loadedCollection != IntPtr.Zero)
                {
                    // Verbose logging disabled to reduce GC pressure
                    // this.BirdFarmNetLog($"Aura mono entity enumeration: loaded collection=0x{loadedCollection.ToInt64():X}");
                    foundAny = this.TryCollectAuraMonoEntityObjectsFromUnknownContainer(loadedCollection, entityObjects, seen, seenTraversalPtrs, 0);
                }

                foundAny = this.TryCollectAuraMonoEntityObjectsFromEntitySource(entitiesObj, entityObjects, seen, seenSourcePtrs, seenTraversalPtrs, 0) || foundAny;
                // Only log if we found entities to reduce spam
                if (foundAny && entityObjects.Count > 0)
                {
                    this.BirdFarmNetLog($"Aura mono entity enumeration: count={entityObjects.Count}");
                }
                if (!foundAny || entityObjects.Count == 0)
                {
                    status = "Aura mono entity enumeration returned no entities";
                    return false;
                }

                status = "Aura mono entity enumeration ready";
                return true;
            }
            catch (Exception ex)
            {
                status = "Aura mono entity enumeration exception: " + ex.Message;
                return false;
            }
        }

        private IntPtr TryGetAuraMonoLoadedEntitiesCollectionObject(IntPtr entitiesObj)
        {
            if (entitiesObj == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            foreach (string memberName in new string[]
            {
                "Entities",
                "All",
                "AllEntities",
                "LoadedEntities",
                "EntityDic",
                "EntityMap",
                "EntityList",
                "_entities",
                "entities",
                "_allEntities",
                "allEntities",
                "_loadedEntities",
                "loadedEntities",
                "_items",
                "items"
            })
            {
                if (this.TryGetMonoObjectMember(entitiesObj, memberName, out IntPtr valueObj) && valueObj != IntPtr.Zero)
                {
                    // Verbose logging disabled to reduce GC pressure
                    // this.BirdFarmNetLog($"Aura mono entity enumeration: entities.{memberName}=0x{valueObj.ToInt64():X}");
                    return valueObj;
                }
            }

            return IntPtr.Zero;
        }

        private bool TryCollectAuraMonoEntityObjectsFromEntitySource(IntPtr sourceObj, List<IntPtr> output, HashSet<long> seenEntityPtrs, HashSet<long> seenSourcePtrs, HashSet<long> seenTraversalPtrs, int depth = 0)
        {
            if (sourceObj == IntPtr.Zero || output == null || seenEntityPtrs == null || seenSourcePtrs == null || seenTraversalPtrs == null || auraMonoObjectGetClass == null || depth > MaxEntitySourceDepth)
            {
                return false;
            }

            long sourceKey = sourceObj.ToInt64();
            if (!seenSourcePtrs.Add(sourceKey))
            {
                return false;
            }
            seenTraversalPtrs.Add(sourceKey);

            bool found = false;
            IntPtr sourceClass = auraMonoObjectGetClass(sourceObj);
            if (sourceClass == IntPtr.Zero)
            {
                return false;
            }

            foreach (string methodName in new string[] { "get_All", "GetAll", "get_AllEntities", "GetAllEntities", "get_LoadedEntities", "GetLoadedEntities", "get_Entities", "GetEntities", "get_WorldEntities", "GetWorldEntities", "get_EntityWorld" })
            {
                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(sourceClass, methodName, 0);
                if (methodPtr == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr nested = auraMonoRuntimeInvoke(methodPtr, sourceObj, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && nested != IntPtr.Zero)
                {
                    // Verbose logging disabled to reduce GC pressure
                    // this.BirdFarmNetLog($"Aura mono entity source method {methodName} -> 0x{nested.ToInt64():X}");
                    found = this.TryCollectAuraMonoEntityObjectsFromUnknownContainer(nested, output, seenEntityPtrs, seenTraversalPtrs, 0) || found;
                }
            }

            foreach (string memberName in new string[] { "_levelEntityWorld", "levelEntityWorld", "entityWorld", "_entityWorld", "_entityWorlds", "entityWorlds", "_entities", "entities", "_allEntities", "allEntities", "_loadedEntities", "loadedEntities", "_entityDic", "_entityMap", "_entityList", "_entitySet", "_items", "items" })
            {
                if (this.TryGetMonoObjectMember(sourceObj, memberName, out IntPtr nested) && nested != IntPtr.Zero)
                {
                    // Verbose logging disabled to reduce GC pressure
                    // this.BirdFarmNetLog($"Aura mono entity source member {memberName} -> 0x{nested.ToInt64():X}");

                    bool isEntityCollectionMember = string.Equals(memberName, "_entities", StringComparison.Ordinal)
                        || string.Equals(memberName, "entities", StringComparison.Ordinal)
                        || string.Equals(memberName, "_allEntities", StringComparison.Ordinal)
                        || string.Equals(memberName, "allEntities", StringComparison.Ordinal)
                        || string.Equals(memberName, "_loadedEntities", StringComparison.Ordinal)
                        || string.Equals(memberName, "loadedEntities", StringComparison.Ordinal);

                    bool isWorldObjectMember = string.Equals(memberName, "_levelEntityWorld", StringComparison.Ordinal)
                        || string.Equals(memberName, "levelEntityWorld", StringComparison.Ordinal)
                        || string.Equals(memberName, "entityWorld", StringComparison.Ordinal)
                        || string.Equals(memberName, "_entityWorld", StringComparison.Ordinal);

                    bool isWorldCollectionMember = string.Equals(memberName, "_entityWorlds", StringComparison.Ordinal)
                        || string.Equals(memberName, "entityWorlds", StringComparison.Ordinal);

                    if (isEntityCollectionMember)
                    {
                        found = this.TryCollectAuraMonoEntityObjectsFromCollection(nested, memberName, output, seenEntityPtrs, seenTraversalPtrs) || found;
                    }

                    if (isWorldObjectMember)
                    {
                        found = this.TryCollectAuraMonoEntityWorldObject(nested, memberName, output, seenEntityPtrs, seenSourcePtrs, seenTraversalPtrs) || found;
                    }

                    if (isWorldCollectionMember)
                    {
                        found = this.TryCollectAuraMonoEntityWorldCollection(nested, memberName, output, seenEntityPtrs, seenSourcePtrs, seenTraversalPtrs) || found;
                    }

                    if (!isEntityCollectionMember && !isWorldObjectMember && !isWorldCollectionMember)
                    {
                        found = this.TryCollectAuraMonoEntityObjectsFromUnknownContainer(nested, output, seenEntityPtrs, seenTraversalPtrs, 0) || found;
                    }
                }
            }

            return found;
        }

        private bool TryCollectAuraMonoEntityObjectsFromCollection(IntPtr collectionObj, string label, List<IntPtr> output, HashSet<long> seenEntityPtrs, HashSet<long> seenTraversalPtrs)
        {
            if (collectionObj == IntPtr.Zero || output == null || seenEntityPtrs == null || seenTraversalPtrs == null)
            {
                return false;
            }

            long collectionKey = collectionObj.ToInt64();
            if (!seenTraversalPtrs.Add(collectionKey))
            {
                return false;
            }

            List<IntPtr> items = new List<IntPtr>();
            // Pin enumerated items for the loop below: each item is read field-by-field (LooksLike /
            // recursion) and the moving sgen GC would otherwise relocate it mid-read -> native AV.
            List<uint> itemPins = new List<uint>();
            if (!this.TryEnumerateAuraMonoCollectionItems(collectionObj, items, itemPins) || items.Count == 0)
            {
                FreeAuraMonoPins(itemPins);
                this.BirdFarmNetLog($"Aura mono entity collection {label}: no items via direct enumeration; probing nested storage");
                return this.TryCollectAuraMonoEntityObjectsFromCollectionFallback(collectionObj, label, output, seenEntityPtrs, seenTraversalPtrs);
            }

            this.BirdFarmNetLog($"Aura mono entity collection {label}: items={items.Count}");
            bool found = false;
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr item = items[i];
                    if (item == IntPtr.Zero || item == collectionObj)
                    {
                        continue;
                    }

                    if (this.TryAddAuraMonoEntityObject(item, output, seenEntityPtrs))
                    {
                        found = true;
                        continue;
                    }

                    found = this.TryCollectAuraMonoEntityObjectsFromUnknownContainer(item, output, seenEntityPtrs, seenTraversalPtrs, 1) || found;
                }
            }
            finally
            {
                FreeAuraMonoPins(itemPins);
            }

            return found;
        }

        private bool TryCollectAuraMonoEntityObjectsFromCollectionFallback(IntPtr collectionObj, string label, List<IntPtr> output, HashSet<long> seenEntityPtrs, HashSet<long> seenTraversalPtrs)
        {
            if (collectionObj == IntPtr.Zero || output == null || seenEntityPtrs == null || seenTraversalPtrs == null || auraMonoObjectGetClass == null)
            {
                return false;
            }

            bool found = false;
            IntPtr collectionClass = auraMonoObjectGetClass(collectionObj);
            if (collectionClass == IntPtr.Zero)
            {
                return false;
            }

            foreach (string memberName in new string[] { "_entries", "entries", "_items", "items", "_values", "values", "Values", "_dictionary", "dictionary", "_list", "list", "_source", "source" })
            {
                if (this.TryGetMonoObjectMember(collectionObj, memberName, out IntPtr nested) && nested != IntPtr.Zero && nested != collectionObj)
                {
                    // Verbose logging disabled to reduce GC pressure
                    // this.BirdFarmNetLog($"Aura mono entity collection {label} nested {memberName} -> 0x{nested.ToInt64():X}");
                    bool isNestedCollection = string.Equals(memberName, "_entries", StringComparison.Ordinal)
                        || string.Equals(memberName, "entries", StringComparison.Ordinal)
                        || string.Equals(memberName, "_items", StringComparison.Ordinal)
                        || string.Equals(memberName, "items", StringComparison.Ordinal)
                        || string.Equals(memberName, "_values", StringComparison.Ordinal)
                        || string.Equals(memberName, "values", StringComparison.Ordinal)
                        || string.Equals(memberName, "Values", StringComparison.Ordinal)
                        || string.Equals(memberName, "_list", StringComparison.Ordinal)
                        || string.Equals(memberName, "list", StringComparison.Ordinal);

                    if (isNestedCollection)
                    {
                        found = this.TryCollectAuraMonoEntityObjectsFromCollection(nested, label + "." + memberName, output, seenEntityPtrs, seenTraversalPtrs) || found;
                    }

                    found = this.TryCollectAuraMonoEntityObjectsFromUnknownContainer(nested, output, seenEntityPtrs, seenTraversalPtrs, 1) || found;
                }
            }

            foreach (string methodName in new string[] { "get_Values", "GetValues", "get_Items", "GetItems", "get_Entries", "GetEntries", "get_List", "GetList", "get_Source", "GetSource" })
            {
                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(collectionClass, methodName, 0);
                if (methodPtr == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr nested = auraMonoRuntimeInvoke(methodPtr, collectionObj, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && nested != IntPtr.Zero && nested != collectionObj)
                {
                    // Verbose logging disabled to reduce GC pressure
                    // this.BirdFarmNetLog($"Aura mono entity collection {label} method {methodName} -> 0x{nested.ToInt64():X}");

                    bool isMethodCollection = string.Equals(methodName, "get_Values", StringComparison.Ordinal)
                        || string.Equals(methodName, "GetValues", StringComparison.Ordinal)
                        || string.Equals(methodName, "get_Items", StringComparison.Ordinal)
                        || string.Equals(methodName, "GetItems", StringComparison.Ordinal)
                        || string.Equals(methodName, "get_Entries", StringComparison.Ordinal)
                        || string.Equals(methodName, "GetEntries", StringComparison.Ordinal)
                        || string.Equals(methodName, "get_List", StringComparison.Ordinal)
                        || string.Equals(methodName, "GetList", StringComparison.Ordinal);

                    if (isMethodCollection)
                    {
                        found = this.TryCollectAuraMonoEntityObjectsFromCollection(nested, label + "." + methodName, output, seenEntityPtrs, seenTraversalPtrs) || found;
                    }

                    found = this.TryCollectAuraMonoEntityObjectsFromUnknownContainer(nested, output, seenEntityPtrs, seenTraversalPtrs, 1) || found;
                }
            }

            return found;
        }

        private bool TryCollectAuraMonoEntityWorldObject(IntPtr worldObj, string label, List<IntPtr> output, HashSet<long> seenEntityPtrs, HashSet<long> seenSourcePtrs, HashSet<long> seenTraversalPtrs, int depth = 0)
        {
            if (worldObj == IntPtr.Zero || output == null || seenEntityPtrs == null || seenSourcePtrs == null || seenTraversalPtrs == null)
            {
                return false;
            }

            // Verbose logging disabled to reduce GC pressure
            // this.BirdFarmNetLog($"Aura mono entity world source {label} -> 0x{worldObj.ToInt64():X}");
            bool found = this.TryCollectAuraMonoEntityObjectsFromEntitySource(worldObj, output, seenEntityPtrs, seenSourcePtrs, seenTraversalPtrs, depth + 1);

            if (this.TryGetMonoObjectMember(worldObj, "_entityWorlds", out IntPtr worldCollection) && worldCollection != IntPtr.Zero)
            {
                // Verbose logging disabled to reduce GC pressure
                // this.BirdFarmNetLog($"Aura mono entity world {label}._entityWorlds -> 0x{worldCollection.ToInt64():X}");
                found = this.TryCollectAuraMonoEntityWorldCollection(worldCollection, label + "._entityWorlds", output, seenEntityPtrs, seenSourcePtrs, seenTraversalPtrs, depth + 1) || found;
            }

            return found;
        }

        private bool TryCollectAuraMonoEntityWorldCollection(IntPtr worldsCollectionObj, string label, List<IntPtr> output, HashSet<long> seenEntityPtrs, HashSet<long> seenSourcePtrs, HashSet<long> seenTraversalPtrs, int depth = 0)
        {
            if (worldsCollectionObj == IntPtr.Zero || output == null || seenEntityPtrs == null || seenSourcePtrs == null || seenTraversalPtrs == null)
            {
                return false;
            }

            long collectionKey = worldsCollectionObj.ToInt64();
            if (!seenTraversalPtrs.Add(collectionKey))
            {
                return false;
            }

            bool found = false;
            List<IntPtr> items = new List<IntPtr>();
            // Pin enumerated items: each is traversed/read below; unpinned the moving sgen GC could
            // relocate it between enumeration and the recursive read -> native AV.
            List<uint> itemPins = new List<uint>();
            if (!this.TryEnumerateAuraMonoCollectionItems(worldsCollectionObj, items, itemPins) || items.Count == 0)
            {
                FreeAuraMonoPins(itemPins);
                return false;
            }

            // Verbose logging disabled to reduce GC pressure
            // this.BirdFarmNetLog($"Aura mono entity world collection {label}: items={items.Count}");
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr item = items[i];
                    if (item == IntPtr.Zero || item == worldsCollectionObj)
                    {
                        continue;
                    }

                    // Verbose logging disabled to reduce GC pressure
                    // this.BirdFarmNetLog($"Aura mono entity world collection {label}[{i}] -> 0x{item.ToInt64():X}");
                    found = this.TryCollectAuraMonoEntityObjectsFromEntitySource(item, output, seenEntityPtrs, seenSourcePtrs, seenTraversalPtrs, depth + 1) || found;
                    found = this.TryCollectAuraMonoEntityObjectsFromUnknownContainer(item, output, seenEntityPtrs, seenTraversalPtrs, 1) || found;
                }
            }
            finally
            {
                FreeAuraMonoPins(itemPins);
            }

            return found;
        }

        private bool TryCollectAuraMonoEntityObjectsFromUnknownContainer(IntPtr containerObj, List<IntPtr> output, HashSet<long> seenEntityPtrs, HashSet<long> seenTraversalPtrs, int depth)
        {
            if (containerObj == IntPtr.Zero || output == null || seenEntityPtrs == null || seenTraversalPtrs == null || auraMonoObjectGetClass == null || depth > 6)
            {
                return false;
            }

            long containerKey = containerObj.ToInt64();
            if (!seenTraversalPtrs.Add(containerKey))
            {
                return false;
            }

            bool found = this.TryAddAuraMonoEntityObject(containerObj, output, seenEntityPtrs);
            IntPtr containerClass = auraMonoObjectGetClass(containerObj);
            if (containerClass == IntPtr.Zero)
            {
                return found;
            }

            IntPtr getCountMethod = this.FindAuraMonoMethodOnHierarchy(containerClass, "get_Count", 0);
            if (getCountMethod != IntPtr.Zero)
            {
                this.GetAuraMonoIntCount(containerObj, getCountMethod);
            }

            foreach (string memberName in new string[] { "Values", "Keys", "_entries", "entries", "_values", "values", "_dictionary", "dictionary", "_source", "source", "_list", "list", "_items", "items", "entity", "_entity", "Entity", "owner", "_owner", "Owner", "target", "_target", "Target", "value", "_value", "Value", "key", "_key", "Key" })
            {
                if (this.TryGetMonoObjectMember(containerObj, memberName, out IntPtr nested) && nested != IntPtr.Zero && nested != containerObj)
                {
                    found = this.TryCollectAuraMonoEntityObjectsFromUnknownContainer(nested, output, seenEntityPtrs, seenTraversalPtrs, depth + 1) || found;
                }
            }

            List<IntPtr> items = new List<IntPtr>();
            // Pin enumerated items across the recursive reads below (moving sgen GC stale-pointer guard).
            List<uint> itemPins = new List<uint>();
            if (this.TryEnumerateAuraMonoCollectionItems(containerObj, items, itemPins))
            {
                try
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        found = this.TryCollectAuraMonoEntityObjectsFromUnknownContainer(items[i], output, seenEntityPtrs, seenTraversalPtrs, depth + 1) || found;
                    }
                }
                finally
                {
                    FreeAuraMonoPins(itemPins);
                }
            }
            else
            {
                FreeAuraMonoPins(itemPins);
            }

            foreach (string methodName in new string[] { "get_entity", "GetEntity", "get_owner", "GetOwner", "get_target", "GetTarget", "get_Value", "get_Key", "get_Source", "GetSource" })
            {
                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(containerClass, methodName, 0);
                if (methodPtr == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr nested = auraMonoRuntimeInvoke(methodPtr, containerObj, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && nested != IntPtr.Zero && nested != containerObj)
                {
                    found = this.TryCollectAuraMonoEntityObjectsFromUnknownContainer(nested, output, seenEntityPtrs, seenTraversalPtrs, depth + 1) || found;
                }
            }

            return found;
        }

        private bool TryAddAuraMonoEntityObject(IntPtr candidateObj, List<IntPtr> output, HashSet<long> seenEntityPtrs)
        {
            if (candidateObj == IntPtr.Zero || output == null || seenEntityPtrs == null)
            {
                return false;
            }

            // Prevent memory issues - limit max entities
            if (output.Count >= this.AuraMonoEntityEnumerationCap)
            {
                return false;
            }

            if (!this.LooksLikeAuraMonoEntityObject(candidateObj))
            {
                return false;
            }

            long key = candidateObj.ToInt64();
            if (!seenEntityPtrs.Add(key))
            {
                return false;
            }

            output.Add(candidateObj);
            // Keep this entity pinned past the scan: the caller reads its fields after we return, and
            // the moving sgen GC would otherwise relocate/collect it (no mono_gc_disable on this build).
            // candidateObj is valid right now (its source items list is pinned during enumeration).
            uint entityPin = AuraMonoPinNew(candidateObj);
            if (entityPin != 0U)
            {
                this._loadedEntityScanPins.Add(entityPin);
            }
            return true;
        }

        private bool LooksLikeAuraMonoEntityObject(IntPtr obj)
        {
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }

            return this.FindAuraMonoMethodOnHierarchy(classPtr, "GetNetId", 0) != IntPtr.Zero
                || this.FindAuraMonoMethodOnHierarchy(classPtr, "get_netId", 0) != IntPtr.Zero;
        }

        private bool TryGetAuraMonoEntityNetId(IntPtr entityObj, out uint netId)
        {
            netId = 0U;
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(entityObj);
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }

            foreach (string methodName in new string[] { "GetNetId", "get_netId" })
            {
                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 0);
                if (methodPtr == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(methodPtr, entityObj, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && boxed != IntPtr.Zero)
                {
                    if (this.TryUnboxMonoUInt32(boxed, out netId))
                    {
                        return netId != 0U;
                    }

                    ulong fallback = this.TryReadMonoUnsignedIntegral(boxed);
                    if (fallback > 0UL && fallback <= uint.MaxValue)
                    {
                        netId = (uint)fallback;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryGetAuraMonoEntityPosition(IntPtr entityObj, out Vector3 position)
        {
            position = Vector3.zero;
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(entityObj);
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }

            foreach (string methodName in new string[] { "get_position", "GetPosition" })
            {
                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 0);
                if (methodPtr == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(methodPtr, entityObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero)
                {
                    continue;
                }

                unsafe
                {
                    position = *(Vector3*)raw;
                }

                return true;
            }

            return false;
        }

        private bool TryGetManagedSelfPlayerEntityObject(out object entityObj, out string source)
        {
            entityObj = null;
            source = "none";

            try
            {
                Type entityUtilType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil", "EntityUtil");
                if (entityUtilType != null)
                {
                    MethodInfo getSelfPlayerEntityMethod = entityUtilType.GetMethod("GetSelfPlayerEntity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (getSelfPlayerEntityMethod != null)
                    {
                        entityObj = getSelfPlayerEntityMethod.Invoke(null, null);
                        if (entityObj != null)
                        {
                            source = "EntityUtil.GetSelfPlayerEntity()";
                            return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                Type characterType = this.FindLoadedType("XDTLevelAndEntity.Game.GameMode.Character", "Character");
                if (characterType != null)
                {
                    PropertyInfo entityProperty = characterType.GetProperty("entity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        ?? characterType.GetProperty("Entity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (entityProperty != null)
                    {
                        entityObj = entityProperty.GetValue(null, null);
                        if (entityObj != null)
                        {
                            source = "Character.entity";
                            return true;
                        }
                    }

                    FieldInfo entityField = characterType.GetField("entity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        ?? characterType.GetField("_entity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (entityField != null)
                    {
                        entityObj = entityField.GetValue(null);
                        if (entityObj != null)
                        {
                            source = "Character.entity[field]";
                            return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (this.TryGetManagedViewModuleSelfPlayerEntityObject(out entityObj, out source))
                {
                    return true;
                }
            }
            catch { }

            try
            {
                if (this.TryGetManagedSelfPlayerObject(out object playerObj, out string playerSource)
                    && this.TryGetManagedPlayerEntityObject(playerObj, out entityObj, out string nestedSource))
                {
                    source = playerSource + " -> " + nestedSource;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool TryGetManagedPlayerEntityObject(object playerObj, out object entityObj, out string source)
        {
            entityObj = null;
            source = "none";
            if (playerObj == null)
            {
                return false;
            }

            foreach (string memberName in new string[] { "entity", "Entity", "_entity" })
            {
                if (this.TryGetObjectMember(playerObj, memberName, out entityObj) && entityObj != null)
                {
                    source = playerObj.GetType().Name + "." + memberName;
                    return true;
                }
            }

            if (this.TryGetObjectMember(playerObj, "viewEntityArg", out object viewEntityArg) && viewEntityArg != null)
            {
                if (this.TryGetObjectMember(viewEntityArg, "entity", out entityObj) && entityObj != null)
                {
                    source = playerObj.GetType().Name + ".viewEntityArg.entity";
                    return true;
                }
            }

            try
            {
                MethodInfo getEntityMethod = playerObj.GetType().GetMethod("get_entity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? playerObj.GetType().GetMethod("GetEntity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (getEntityMethod != null)
                {
                    entityObj = getEntityMethod.Invoke(playerObj, null);
                    if (entityObj != null)
                    {
                        source = playerObj.GetType().Name + "." + getEntityMethod.Name + "()";
                        return true;
                    }
                }
            }
            catch { }

            Type currentType = playerObj.GetType();
            while (currentType != null)
            {
                try
                {
                    PropertyInfo property = currentType.GetProperty("entity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? currentType.GetProperty("Entity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (property != null)
                    {
                        entityObj = property.GetValue(playerObj, null);
                        if (entityObj != null)
                        {
                            source = currentType.Name + "." + property.Name;
                            return true;
                        }
                    }
                }
                catch { }

                try
                {
                    FieldInfo field = currentType.GetField("entity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? currentType.GetField("_entity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        entityObj = field.GetValue(playerObj);
                        if (entityObj != null)
                        {
                            source = currentType.Name + "." + field.Name;
                            return true;
                        }
                    }
                }
                catch { }

                try
                {
                    foreach (MethodInfo method in currentType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (method == null || method.GetParameters().Length != 0)
                        {
                            continue;
                        }

                        string methodName = method.Name ?? string.Empty;
                        if (!methodName.Equals("get_entity", StringComparison.OrdinalIgnoreCase)
                            && !methodName.Equals("GetEntity", StringComparison.OrdinalIgnoreCase)
                            && !methodName.EndsWith(".get_entity", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        entityObj = method.Invoke(playerObj, null);
                        if (entityObj != null)
                        {
                            source = currentType.Name + "." + methodName + "()";
                            return true;
                        }
                    }
                }
                catch { }

                currentType = currentType.BaseType;
            }

            try
            {
                foreach (Type iface in playerObj.GetType().GetInterfaces())
                {
                    if (iface == null)
                    {
                        continue;
                    }

                    PropertyInfo ifaceProperty = iface.GetProperty("entity") ?? iface.GetProperty("Entity");
                    if (ifaceProperty != null)
                    {
                        entityObj = ifaceProperty.GetValue(playerObj, null);
                        if (entityObj != null)
                        {
                            source = iface.Name + "." + ifaceProperty.Name;
                            return true;
                        }
                    }

                    MethodInfo ifaceMethod = iface.GetMethod("get_entity") ?? iface.GetMethod("GetEntity");
                    if (ifaceMethod != null)
                    {
                        entityObj = ifaceMethod.Invoke(playerObj, null);
                        if (entityObj != null)
                        {
                            source = iface.Name + "." + ifaceMethod.Name + "()";
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private int TryMarkManagerFindEntitiesSelectedAuraMono(IntPtr managerObj, IntPtr bugDictObj)
        {
            if (managerObj == IntPtr.Zero || bugDictObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            try
            {
                if (!this.TryReadAuraMonoObjectField(managerObj, out IntPtr findEntitiesObj, "_findEntities", "findEntities") || findEntitiesObj == IntPtr.Zero)
                {
                    return 0;
                }

                IntPtr listClass = auraMonoObjectGetClass(findEntitiesObj);
                IntPtr getCountMethod = this.FindAuraMonoMethodOnHierarchy(listClass, "get_Count", 0);
                IntPtr getItemMethod = this.FindAuraMonoMethodOnHierarchy(listClass, "get_Item", 1);
                IntPtr dictClass = auraMonoObjectGetClass(bugDictObj);
                IntPtr setItemMethod = this.FindAuraMonoMethodOnHierarchy(dictClass, "set_Item", 2);
                if (getCountMethod == IntPtr.Zero || getItemMethod == IntPtr.Zero || setItemMethod == IntPtr.Zero)
                {
                    return 0;
                }

                int count = this.GetAuraMonoIntCount(findEntitiesObj, getCountMethod);
                int marked = 0;
                for (int i = 0; i < count && i < 64; i++)
                {
                    IntPtr itemObj;
                    unsafe
                    {
                        IntPtr exc = IntPtr.Zero;
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = (IntPtr)(&i);
                        itemObj = auraMonoRuntimeInvoke(getItemMethod, findEntitiesObj, (IntPtr)args, ref exc);
                        if (exc != IntPtr.Zero || itemObj == IntPtr.Zero)
                        {
                            continue;
                        }
                    }

                    unsafe
                    {
                        bool selected = true;
                        IntPtr exc = IntPtr.Zero;
                        IntPtr* args = stackalloc IntPtr[2];
                        args[0] = itemObj;
                        args[1] = (IntPtr)(&selected);
                        auraMonoRuntimeInvoke(setItemMethod, bugDictObj, (IntPtr)args, ref exc);
                        if (exc == IntPtr.Zero)
                        {
                            marked++;
                        }
                    }
                }

                return marked;
            }
            catch (Exception ex)
            {
                this.InsectFarmNetLog("TryMarkManagerFindEntitiesSelectedAuraMono error: " + ex.Message);
                return 0;
            }
        }

        private int TryMarkManagerFindEntitiesSelectedManaged(object managerObj, object bugDictObj)
        {
            if (managerObj == null || bugDictObj == null)
            {
                return 0;
            }

            try
            {
                if (!(this.TryGetObjectMember(managerObj, "_findEntities", out object findEntities) || this.TryGetObjectMember(managerObj, "findEntities", out findEntities)) || findEntities == null)
                {
                    return 0;
                }

                Type listType = findEntities.GetType();
                MethodInfo getCountMethod = listType.GetMethod("get_Count", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo getItemMethod = listType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo setItemMethod = bugDictObj.GetType().GetMethod("set_Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (getCountMethod == null || getItemMethod == null || setItemMethod == null)
                {
                    return 0;
                }

                int count = Convert.ToInt32(getCountMethod.Invoke(findEntities, null));
                int marked = 0;
                for (int i = 0; i < count && i < 64; i++)
                {
                    object item = getItemMethod.Invoke(findEntities, new object[] { i });
                    if (item == null)
                    {
                        continue;
                    }

                    setItemMethod.Invoke(bugDictObj, new object[] { item, true });
                    marked++;
                }

                return marked;
            }
            catch (Exception ex)
            {
                this.InsectFarmNetLog("TryMarkManagerFindEntitiesSelectedManaged error: " + ex.Message);
                return 0;
            }
        }

        private bool TryFindPlayerEntityObject(GameObject player, out Il2CppObject entityObj, out string source)
        {
            entityObj = null;
            source = "none";
            if (player == null)
            {
                return false;
            }

            Component[] allComponents = player.GetComponentsInChildren<Component>(true);
            foreach (Component comp in allComponents)
            {
                if (comp == null) continue;
                var ilType = comp.GetIl2CppType();
                if (ilType == null) continue;
                foreach (string member in new string[] { "entity", "Entity", "_entity" })
                {
                    if (this.TryReadObjectMember(ilType, comp.TryCast<Il2CppObject>(), member, out entityObj) && entityObj != null)
                    {
                        source = ilType.Name + "." + member;
                        return true;
                    }
                }

                try
                {
                    var getEntityMethod = ilType.GetMethod("get_entity") ?? ilType.GetMethod("GetEntity");
                    if (getEntityMethod != null)
                    {
                        Il2CppObject maybeEntity = getEntityMethod.Invoke(comp.TryCast<Il2CppObject>(), null);
                        if (maybeEntity != null)
                        {
                            source = ilType.Name + "." + getEntityMethod.Name + "()";
                            entityObj = maybeEntity;
                            return true;
                        }
                    }
                }
                catch { }
            }

            Transform parent = player.transform.parent;
            int parentDepth = 0;
            while (parent != null && parentDepth < 3)
            {
                Component[] parentComponents = parent.GetComponents<Component>();
                foreach (Component comp in parentComponents)
                {
                    if (comp == null) continue;
                    var ilType = comp.GetIl2CppType();
                    if (ilType == null) continue;
                    foreach (string member in new string[] { "entity", "Entity", "_entity" })
                    {
                        if (this.TryReadObjectMember(ilType, comp.TryCast<Il2CppObject>(), member, out entityObj) && entityObj != null)
                        {
                            source = "parent/" + ilType.Name + "." + member;
                            return true;
                        }
                    }

                    try
                    {
                        var getEntityMethod = ilType.GetMethod("get_entity") ?? ilType.GetMethod("GetEntity");
                        if (getEntityMethod != null)
                        {
                            Il2CppObject maybeEntity = getEntityMethod.Invoke(comp.TryCast<Il2CppObject>(), null);
                            if (maybeEntity != null)
                            {
                                source = "parent/" + ilType.Name + "." + getEntityMethod.Name + "()";
                                entityObj = maybeEntity;
                                return true;
                            }
                        }
                    }
                    catch { }
                }

                parent = parent.parent;
                parentDepth++;
            }

            return false;
        }

        private int TryMarkManagerFindEntitiesSelected(Il2CppObject managerObj, Il2CppObject bugDictObj)
        {
            if (managerObj == null || bugDictObj == null)
            {
                return 0;
            }

            try
            {
                Il2CppObject findEntities = this.ReadIl2CppObjectMember(managerObj, "_findEntities");
                if (findEntities == null)
                {
                    return 0;
                }

                var listType = findEntities.GetIl2CppType();
                var countProp = listType.GetProperty("Count");
                var itemMethod = listType.GetMethod("get_Item");
                var setItemMethod = bugDictObj.GetIl2CppType().GetMethod("set_Item");
                if (countProp == null || itemMethod == null || setItemMethod == null)
                {
                    return 0;
                }

                int count = countProp.GetValue(findEntities).Unbox<int>();
                int marked = 0;
                for (int i = 0; i < count && i < 64; i++)
                {
                    Il2CppObject item = itemMethod.Invoke(findEntities, new Il2CppReferenceArray<Il2CppObject>(new Il2CppObject[] { this.BoxInt(i) }));
                    if (item == null)
                    {
                        continue;
                    }

                    setItemMethod.Invoke(bugDictObj, new Il2CppReferenceArray<Il2CppObject>(new Il2CppObject[] { item, this.BoxBool(true) }));
                    marked++;
                }

                return marked;
            }
            catch (Exception ex)
            {
                this.InsectFarmNetLog("TryMarkManagerFindEntitiesSelected error: " + ex.Message);
                return 0;
            }
        }

        private bool TryGetManagedViewModuleSelfPlayerEntityObject(out object entityObj, out string source)
        {
            entityObj = null;
            source = "none";

            try
            {
                Type entityManagerType = this.FindLoadedType(
                    "ScriptsRefactory.LevelAndEntity.BaseSystem.EntityManager",
                    "EntityManager");
                if (entityManagerType == null)
                {
                    return false;
                }

                PropertyInfo instanceProperty = entityManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object entityManager = instanceProperty != null ? instanceProperty.GetValue(null, null) : null;
                if (entityManager == null)
                {
                    return false;
                }

                if (this.TryGetObjectMember(entityManager, "selfPlayer", out entityObj) && entityObj != null)
                {
                    source = "EntityManager.Instance.selfPlayer";
                    return true;
                }
            }
            catch { }

            return false;
        }

        private unsafe bool TryGetCurrentInteractTargetLevelObjectsViaAuraMono(List<ulong> candidateLevelObjects, out string status, HashSet<ulong> candidateLevelObjectSet = null)
        {
            status = "AuraMono interact lookup unavailable.";
            if (candidateLevelObjects == null)
            {
                return false;
            }

            try
            {
                this.ResolveAuraFarmRuntimeMethods();
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoFieldGetValue == null || auraMonoRuntimeInvoke == null)
                {
                    status = "AuraMono API unavailable.";
                    return false;
                }

                IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
                if (interactObj == IntPtr.Zero)
                {
                    status = "AuraMono interact instance unavailable.";
                    return false;
                }

                int added = 0;

                if (this.auraMonoInteractCurrentTargetFieldPtr != IntPtr.Zero)
                {
                    ulong currentTargetRaw = 0UL;
                    auraMonoFieldGetValue(interactObj, this.auraMonoInteractCurrentTargetFieldPtr, (IntPtr)(&currentTargetRaw));
                    if (currentTargetRaw > uint.MaxValue && AddNetCookCandidateLevelObject(candidateLevelObjects, candidateLevelObjectSet, currentTargetRaw))
                    {
                        added++;
                        this.NetCookLog("AuraMono current target levelObject=" + currentTargetRaw);
                    }
                }

                if (this.auraMonoInteractFocusLevelObjectsFieldPtr != IntPtr.Zero)
                {
                    IntPtr setObj = IntPtr.Zero;
                    auraMonoFieldGetValue(interactObj, this.auraMonoInteractFocusLevelObjectsFieldPtr, (IntPtr)(&setObj));
                    if (setObj != IntPtr.Zero && this.EnsureAuraMonoFocusSetAccessors(setObj))
                    {
                        int count = this.GetAuraMonoIntCount(setObj, this.auraMonoFocusSetCountMethodPtr);
                        if (count > 0)
                        {
                            IntPtr arrayObj = this.CreateAuraMonoUInt64ArrayObject(count);
                            if (arrayObj != IntPtr.Zero)
                            {
                                IntPtr exc = IntPtr.Zero;
                                IntPtr* args = stackalloc IntPtr[1];
                                args[0] = arrayObj;
                                auraMonoRuntimeInvoke(this.auraMonoFocusSetCopyToMethodPtr, setObj, (IntPtr)args, ref exc);
                                if (exc == IntPtr.Zero && auraMonoArrayLength != null && auraMonoArrayAddrWithSize != null)
                                {
                                    ulong length = auraMonoArrayLength(arrayObj).ToUInt64();
                                    for (int i = 0; i < (int)length; i++)
                                    {
                                        IntPtr raw = auraMonoArrayAddrWithSize(arrayObj, sizeof(ulong), (UIntPtr)i);
                                        if (raw == IntPtr.Zero)
                                        {
                                            continue;
                                        }

                                        ulong levelObjectId = (ulong)Marshal.ReadInt64(raw);
                                        if (!AddNetCookCandidateLevelObject(candidateLevelObjects, candidateLevelObjectSet, levelObjectId))
                                        {
                                            continue;
                                        }

                                        added++;
                                    }
                                }
                            }
                        }
                    }
                }

                if (this.auraMonoInteractSelectedFieldPtr != IntPtr.Zero)
                {
                    IntPtr mapObj = IntPtr.Zero;
                    auraMonoFieldGetValue(interactObj, this.auraMonoInteractSelectedFieldPtr, (IntPtr)(&mapObj));
                    if (mapObj != IntPtr.Zero && this.EnsureAuraMonoSelectedKeyAccessors(mapObj, out IntPtr keysObj) && keysObj != IntPtr.Zero)
                    {
                        int count = this.GetAuraMonoIntCount(keysObj, this.auraMonoSelectedKeysCountMethodPtr);
                        if (count > 0)
                        {
                            IntPtr arrayObj = this.CreateAuraMonoUInt64ArrayObject(count);
                            if (arrayObj != IntPtr.Zero)
                            {
                                int startIndex = 0;
                                IntPtr exc = IntPtr.Zero;
                                IntPtr* args = stackalloc IntPtr[2];
                                args[0] = arrayObj;
                                args[1] = (IntPtr)(&startIndex);
                                auraMonoRuntimeInvoke(this.auraMonoSelectedKeysCopyToMethodPtr, keysObj, (IntPtr)args, ref exc);
                                if (exc == IntPtr.Zero && auraMonoArrayLength != null && auraMonoArrayAddrWithSize != null)
                                {
                                    ulong length = auraMonoArrayLength(arrayObj).ToUInt64();
                                    for (int i = 0; i < (int)length; i++)
                                    {
                                        IntPtr raw = auraMonoArrayAddrWithSize(arrayObj, sizeof(ulong), (UIntPtr)i);
                                        if (raw == IntPtr.Zero)
                                        {
                                            continue;
                                        }

                                        ulong levelObjectId = (ulong)Marshal.ReadInt64(raw);
                                        if (!AddNetCookCandidateLevelObject(candidateLevelObjects, candidateLevelObjectSet, levelObjectId))
                                        {
                                            continue;
                                        }

                                        added++;
                                    }
                                }
                            }
                        }
                    }
                }

                IntPtr listObj = this.GetAuraMonoUInt64ListObject();
                if (listObj != IntPtr.Zero)
                {
                    if (this.auraMonoInteractGetTargetListMethodViaClass(out IntPtr getTargetListMethod))
                    {
                        IntPtr exc = IntPtr.Zero;
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = listObj;
                        auraMonoRuntimeInvoke(getTargetListMethod, interactObj, (IntPtr)args, ref exc);
                        if (exc == IntPtr.Zero)
                        {
                            this.CacheAuraMonoUInt64ListAccessors(listObj);
                            int listCount = this.GetAuraMonoUInt64ListCount(listObj);
                            for (int i = 0; i < listCount; i++)
                            {
                                ulong levelObjectId = this.GetAuraMonoUInt64ListItem(listObj, i);
                                if (!AddNetCookCandidateLevelObject(candidateLevelObjects, candidateLevelObjectSet, levelObjectId))
                                {
                                    continue;
                                }

                                added++;
                            }
                        }
                    }

                    if (this.auraMonoEntityHelperGetTargetListMethodViaClass(out IntPtr entityHelperTargetListMethod))
                    {
                        IntPtr exc = IntPtr.Zero;
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = listObj;
                        auraMonoRuntimeInvoke(entityHelperTargetListMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                        if (exc == IntPtr.Zero)
                        {
                            this.CacheAuraMonoUInt64ListAccessors(listObj);
                            int listCount = this.GetAuraMonoUInt64ListCount(listObj);
                            for (int i = 0; i < listCount; i++)
                            {
                                ulong levelObjectId = this.GetAuraMonoUInt64ListItem(listObj, i);
                                if (!AddNetCookCandidateLevelObject(candidateLevelObjects, candidateLevelObjectSet, levelObjectId))
                                {
                                    continue;
                                }

                                added++;
                            }
                        }
                    }
                }

                status = added > 0 ? "AuraMono interact added " + added + " candidate level objects." : "AuraMono interact found no candidate level objects.";
                return added > 0;
            }
            catch (Exception ex)
            {
                status = "AuraMono interact exception: " + ex.Message;
                this.NetCookLog(status);
                return false;
            }
        }

        private unsafe bool TryGetAuraMonoEntityObjectByNetId(uint netId, out IntPtr entityObj)
        {
            entityObj = IntPtr.Zero;
            if (netId == 0U || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr entitiesClass = this.cachedAuraMonoEntitiesManagerClass;
            if (entitiesClass == IntPtr.Zero)
            {
                IntPtr levelImage = this.FindAuraMonoImage(new string[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "Client", "Client.dll" });
                entitiesClass = levelImage != IntPtr.Zero ? auraMonoClassFromName(levelImage, "XDTLevelAndEntity.BaseSystem.EntitiesManager", "Entities") : IntPtr.Zero;
                if (entitiesClass == IntPtr.Zero)
                {
                    entitiesClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.BaseSystem.EntitiesManager", "Entities");
                }
                if (entitiesClass == IntPtr.Zero)
                {
                    return false;
                }

                this.cachedAuraMonoEntitiesManagerClass = entitiesClass;
            }

            IntPtr getEntityMethod = this.cachedAuraMonoEntitiesGetEntityMethod;
            if (getEntityMethod == IntPtr.Zero)
            {
                getEntityMethod = this.FindAuraMonoMethodOnHierarchy(entitiesClass, "GetEntity", 1);
                if (getEntityMethod == IntPtr.Zero)
                {
                    getEntityMethod = this.FindAuraMonoMethodOnHierarchy(entitiesClass, "GetAnyEntity", 1);
                }
                if (getEntityMethod == IntPtr.Zero)
                {
                    return false;
                }

                this.cachedAuraMonoEntitiesGetEntityMethod = getEntityMethod;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&netId);
            IntPtr exc = IntPtr.Zero;
            entityObj = auraMonoRuntimeInvoke(getEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero && entityObj != IntPtr.Zero;
        }

        private bool TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out IntPtr classPtr, out string status)
        {
            managerObj = IntPtr.Zero;
            classPtr = IntPtr.Zero;
            status = "AuraMono API unavailable.";

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr image = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll", "XDTLevelAndEntity", "XDTLevelAndEntity.dll" });
            if (image != IntPtr.Zero)
            {
                classPtr = auraMonoClassFromName(image, "ScriptsRefactory.LevelAndEntity", "LevelObjectManager");
            }

            if (classPtr == IntPtr.Zero)
            {
                classPtr = this.FindAuraMonoClassAcrossLoadedAssemblies("ScriptsRefactory.LevelAndEntity", "LevelObjectManager");
            }

            if (classPtr == IntPtr.Zero)
            {
                classPtr = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ScriptsRefactory.LevelAndEntity", "LevelObjectManager");
            }

            if (classPtr == IntPtr.Zero)
            {
                status = "LevelObjectManager class unavailable.";
                return false;
            }

            if (!this.TryGetAuraMonoStaticObjectField(classPtr, "_instance", out managerObj) || managerObj == IntPtr.Zero)
            {
                IntPtr getInstanceMethod = this.FindAuraMonoMethodOnHierarchy(classPtr, "get_Instance", 0);
                if (getInstanceMethod != IntPtr.Zero)
                {
                    IntPtr exc = IntPtr.Zero;
                    managerObj = auraMonoRuntimeInvoke(getInstanceMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        managerObj = IntPtr.Zero;
                    }
                }
            }

            if (managerObj == IntPtr.Zero)
            {
                status = "LevelObjectManager instance unavailable.";
                return false;
            }

            status = "AuraMono LevelObjectManager ready.";
            return true;
        }

        private bool TryGetCurrentFocusedLevelObjectNetId(out ulong levelObjectNetId, out string status)
        {
            levelObjectNetId = 0UL;
            status = "Focused target unavailable.";

            try
            {
                if (!this.TryGetManagedSelfPlayerObject(out object playerObj, out _)
                    && !this.TryGetManagedSelfPlayerEntityObject(out playerObj, out _))
                {
                    status = "Self player unavailable.";
                    this.NetCookLog(status);
                    return false;
                }

                object playerStatus = this.TryGetManagedMemberValue(playerObj, "Status") ?? this.TryGetManagedMemberValue(playerObj, "status");
                if (playerStatus == null)
                {
                    status = "Player status unavailable.";
                    this.NetCookLog(status);
                    return false;
                }

                object focusUiStatus = this.TryGetManagedMemberValue(playerStatus, "FocusUIStatus") ?? this.TryGetManagedMemberValue(playerStatus, "focusUIStatus");
                if (focusUiStatus == null)
                {
                    status = "Focus UI status unavailable.";
                    this.NetCookLog(status);
                    return false;
                }

                if (!this.TryReadManagedUInt64Member(focusUiStatus, "FocusLevelObject", out levelObjectNetId)
                    && !this.TryReadManagedUInt64Member(focusUiStatus, "focusLevelObject", out levelObjectNetId))
                {
                    status = "Focus level object unavailable.";
                    this.NetCookLog(status);
                    return false;
                }

                if (levelObjectNetId == 0UL)
                {
                    status = "No focused level object.";
                    this.NetCookLog(status);
                    return false;
                }

                status = "Focused target ready.";
                this.NetCookLog("FocusLevelObject=" + levelObjectNetId);
                return true;
            }
            catch (Exception ex)
            {
                status = "Focused target exception: " + ex.Message;
                this.NetCookLog(status);
                return false;
            }
        }

        private bool TryGetCurrentInteractTargetLevelObjects(List<ulong> candidateLevelObjects, out string status, HashSet<ulong> candidateLevelObjectSet = null)
        {
            status = "Interact targets unavailable.";
            if (candidateLevelObjects == null)
            {
                this.NetCookLog("Candidate target list was null.");
                return false;
            }

            try
            {
                if (!this.TryGetManagedInteractSystemObject(out object interactSystemObj, out _))
                {
                    this.NetCookLog("InteractSystem unavailable. Trying static interact target helpers...");
                    return this.TryGetCurrentInteractTargetLevelObjectsViaStaticHelper(candidateLevelObjects, out status, candidateLevelObjectSet);
                }

                MethodInfo getInteractTargetListMethod = null;
                if (interactSystemObj is Type interactStaticType)
                {
                    getInteractTargetListMethod = interactStaticType.GetMethod("GetInteractTargetList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(List<ulong>) }, null);
                }
                else
                {
                    getInteractTargetListMethod = interactSystemObj.GetType().GetMethod("GetInteractTargetList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(List<ulong>) }, null);
                }
                if (getInteractTargetListMethod == null)
                {
                    this.NetCookLog("GetInteractTargetList unavailable on primary object. Trying static interact target helpers...");
                    return this.TryGetCurrentInteractTargetLevelObjectsViaStaticHelper(candidateLevelObjects, out status, candidateLevelObjectSet);
                }

                List<ulong> interactTargets = new List<ulong>(8);
                object invokeResult = getInteractTargetListMethod.Invoke(interactSystemObj is Type ? null : interactSystemObj, new object[] { interactTargets });
                int targetCount = 0;
                if (invokeResult is int count)
                {
                    targetCount = count;
                }
                else
                {
                    targetCount = interactTargets.Count;
                }

                if (NetCookLogsEnabled)
                {
                    this.NetCookLog("GetInteractTargetList returned count=" + targetCount + " targets=[" + string.Join(", ", interactTargets) + "]");
                }

                for (int i = 0; i < interactTargets.Count; i++)
                {
                    ulong targetLevelObjectNetId = interactTargets[i];
                    if (!AddNetCookCandidateLevelObject(candidateLevelObjects, candidateLevelObjectSet, targetLevelObjectNetId))
                    {
                        continue;
                    }
                }

                status = "Interact targets ready: " + targetCount;
                return targetCount > 0;
            }
            catch (Exception ex)
            {
                status = "Interact target exception: " + ex.Message;
                this.NetCookLog(status);
                return false;
            }
        }

        private bool TryGetCurrentInteractTargetLevelObjectsViaStaticHelper(List<ulong> candidateLevelObjects, out string status, HashSet<ulong> candidateLevelObjectSet = null)
        {
            status = "Static interact target helper unavailable.";
            if (candidateLevelObjects == null)
            {
                return false;
            }

            try
            {
                List<ulong> interactTargets = new List<ulong>(8);
                object invokeResult = null;

                Type entityHelperType = this.FindLoadedType("XDTLevelAndEntity.Utils.EntityHelper", "EntityHelper");
                MethodInfo helperMethod = entityHelperType != null
                    ? entityHelperType.GetMethod("GetPlayerInteractTargetList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(List<ulong>) }, null)
                    : null;
                if (helperMethod != null)
                {
                    invokeResult = helperMethod.Invoke(null, new object[] { interactTargets });
                    this.NetCookLog("EntityHelper.GetPlayerInteractTargetList used.");
                }
                else
                {
                    Type playerInteractionType = this.FindLoadedType(
                        "XDTLevelAndEntity.Gameplay.Interaction.PlayerInteraction",
                        "PlayerInteraction");
                    MethodInfo directMethod = playerInteractionType != null
                        ? playerInteractionType.GetMethod("GetInteractTargetList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(List<ulong>) }, null)
                        : null;
                    if (directMethod == null)
                    {
                        this.NetCookLog(status);
                        return false;
                    }

                    invokeResult = directMethod.Invoke(null, new object[] { interactTargets });
                    this.NetCookLog("PlayerInteraction.GetInteractTargetList used.");
                }

                int targetCount = 0;
                if (invokeResult is int count)
                {
                    targetCount = count;
                }
                else
                {
                    targetCount = interactTargets.Count;
                }

                if (NetCookLogsEnabled)
                {
                    this.NetCookLog("Static interact helper returned count=" + targetCount + " targets=[" + string.Join(", ", interactTargets) + "]");
                }

                for (int i = 0; i < interactTargets.Count; i++)
                {
                    ulong targetLevelObjectNetId = interactTargets[i];
                    if (!AddNetCookCandidateLevelObject(candidateLevelObjects, candidateLevelObjectSet, targetLevelObjectNetId))
                    {
                        continue;
                    }
                }

                status = "Static interact targets ready: " + targetCount;
                return targetCount > 0;
            }
            catch (Exception ex)
            {
                status = "Static interact target exception: " + ex.Message;
                this.NetCookLog(status);
                return false;
            }
        }

        private bool TryGetEntityPositionByNetId(uint netId, out Vector3 position)
        {
            position = Vector3.zero;
            if (netId == 0U)
            {
                return false;
            }

            try
            {
                Type entitiesType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities", "Entities");
                if (entitiesType == null)
                {
                    return false;
                }

                MethodInfo getEntityRefMethod = entitiesType.GetMethod("GetEntityRef", BindingFlags.Public | BindingFlags.Static);
                if (getEntityRefMethod == null)
                {
                    return false;
                }

                object entityRef = getEntityRefMethod.Invoke(null, new object[] { netId });
                if (entityRef == null)
                {
                    return false;
                }

                Type extensionType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.LevelResHandleExtension", "LevelResHandleExtension");
                MethodInfo tryGetMethod = extensionType != null
                    ? extensionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "TryGet" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == entityRef.GetType())
                    : null;
                if (tryGetMethod == null)
                {
                    return false;
                }

                object[] args = new object[] { entityRef, null };
                object result = tryGetMethod.Invoke(null, args);
                if (!(result is bool) || !(bool)result || args[1] == null)
                {
                    return false;
                }

                object entity = args[1];
                object positionObj;
                if (this.TryGetObjectMember(entity, "position", out positionObj) && positionObj is Vector3)
                {
                    position = (Vector3)positionObj;
                    return position != Vector3.zero;
                }

                object transformObj;
                return this.TryGetObjectMember(entity, "transform", out transformObj) && this.TryExtractHomePosition(transformObj, out position) && position != Vector3.zero;
            }
            catch
            {
                position = Vector3.zero;
            }

            return false;
        }

        private unsafe bool TryGetEntityPositionByNetIdMono(uint netId, out Vector3 position)
        {
            position = Vector3.zero;
            if (netId == 0U || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr entitiesClass = this.cachedAuraMonoEntitiesManagerClass;
            if (entitiesClass == IntPtr.Zero)
            {
                IntPtr levelImage = this.FindAuraMonoImage(new string[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "Client", "Client.dll" });
                entitiesClass = levelImage != IntPtr.Zero ? auraMonoClassFromName(levelImage, "XDTLevelAndEntity.BaseSystem.EntitiesManager", "Entities") : IntPtr.Zero;
                if (entitiesClass == IntPtr.Zero)
                {
                    entitiesClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.BaseSystem.EntitiesManager", "Entities");
                }
                if (entitiesClass == IntPtr.Zero)
                {
                    return false;
                }

                this.cachedAuraMonoEntitiesManagerClass = entitiesClass;
            }

            IntPtr getEntityMethod = this.cachedAuraMonoEntitiesGetEntityMethod;
            if (getEntityMethod == IntPtr.Zero)
            {
                getEntityMethod = this.FindAuraMonoMethodOnHierarchy(entitiesClass, "GetEntity", 1);
                if (getEntityMethod == IntPtr.Zero)
                {
                    getEntityMethod = this.FindAuraMonoMethodOnHierarchy(entitiesClass, "GetAnyEntity", 1);
                }
                if (getEntityMethod == IntPtr.Zero)
                {
                    return false;
                }

                this.cachedAuraMonoEntitiesGetEntityMethod = getEntityMethod;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&netId);
            IntPtr exc = IntPtr.Zero;
            IntPtr entityObj = auraMonoRuntimeInvoke(getEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || entityObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryExtractHomePositionMonoObject(entityObj, out position) && position != Vector3.zero;
        }

        private bool TryGetEntityObjectByNetId(uint netId, out object entity)
        {
            entity = null;
            if (netId == 0U)
            {
                return false;
            }

            try
            {
                Type entitiesType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities", "Entities");
                if (entitiesType == null)
                {
                    return false;
                }

                MethodInfo getEntityRefMethod = entitiesType.GetMethod("GetEntityRef", BindingFlags.Public | BindingFlags.Static);
                if (getEntityRefMethod == null)
                {
                    return false;
                }

                object entityRef = getEntityRefMethod.Invoke(null, new object[] { netId });
                if (entityRef == null)
                {
                    return false;
                }

                Type extensionType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.LevelResHandleExtension", "LevelResHandleExtension");
                MethodInfo tryGetMethod = extensionType != null
                    ? extensionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "TryGet" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == entityRef.GetType())
                    : null;
                if (tryGetMethod == null)
                {
                    return false;
                }

                object[] args = new object[] { entityRef, null };
                object result = tryGetMethod.Invoke(null, args);
                if (result is bool && (bool)result && args[1] != null)
                {
                    entity = args[1];
                    return true;
                }
            }
            catch
            {
            }

            entity = null;
            return false;
        }

        private Type FindLevelObjectManagerRuntimeType()
        {
            Type resolved = this.FindLoadedType(
                "ScriptsRefactory.LevelAndEntity.LevelObjectManager",
                "XDTDataAndProtocol.ScriptsRefactory.LevelAndEntity.LevelObjectManager",
                "LevelObjectManager");
            if (resolved != null)
            {
                return resolved;
            }

            try
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

                        string fullName = type.FullName ?? type.Name ?? string.Empty;
                        if (fullName.IndexOf("LevelObjectManager", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        PropertyInfo instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        MethodInfo getLevelObjectMethod = type.GetMethod("GetLevelObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ulong) }, null);
                        if (instanceProperty != null && getLevelObjectMethod != null)
                        {
                            return type;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private bool TryDecodeEntityTypeIdFromStaticId(int staticId, out int entityTypeId)
        {
            entityTypeId = 0;
            if (staticId <= 0)
            {
                return false;
            }

            try
            {
                Type tableDataType = this.FindManagedTableDataType();
                if (tableDataType == null)
                {
                    return false;
                }

                MethodInfo getEntityTypeId = tableDataType.GetMethod("GetEntityTypeID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(int) }, null);
                if (getEntityTypeId == null)
                {
                    return false;
                }

                entityTypeId = Convert.ToInt32(getEntityTypeId.Invoke(null, new object[] { staticId }));
                return entityTypeId > 0;
            }
            catch
            {
                return false;
            }
        }

    }
}
