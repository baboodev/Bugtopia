using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        // InteractId.OpenCraftMainPanel — level objects with this interact are craft tables.
        private const int CraftOpenInteractId = 3001;

        private bool TryOpenCraftPanel(out string status)
        {
            uint tablePin = 0U;
            try
            {
                if (!this.TryFindHomelandCraftTableNetId(out ulong tableNetId, out IntPtr tableLevelObjectObj, out tablePin, out string findKey))
                {
                    status = this.L(findKey);
                    return false;
                }

                if (!this.TryOpenCraftPanelViaAuraMonoInteract(tableNetId, tableLevelObjectObj, out string openKey))
                {
                    status = this.L(openKey);
                    return false;
                }

                status = this.L(openKey);
                return true;
            }
            finally
            {
                if (tablePin != 0U)
                {
                    AuraMonoPinFree(tablePin);
                }
            }
        }

        private bool TryResolveCraftAuraLocalPlayerObject(out IntPtr playerObj, out string source)
        {
            playerObj = IntPtr.Zero;
            source = string.Empty;
            if (this.TryHomelandFarmTryGetAuraLocalPlayerObject(out playerObj, out source) && playerObj != IntPtr.Zero)
            {
                return true;
            }

            if (this.TryGetAuraMonoLocalPlayerObject(out playerObj) && playerObj != IntPtr.Zero)
            {
                source = "Character.player";
                return true;
            }

            return false;
        }

        // LocalPlayerComponent.OpenInteractPanel — sets FocusLevelObject and opens the craft panel.
        private unsafe bool TryOpenCraftPanelViaAuraMonoInteract(ulong tableNetId, IntPtr cachedLevelObjectObj, out string statusKey)
        {
            _ = tableNetId;
            statusKey = "craft.open_interact_unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null
                || auraMonoStringNew == null || auraMonoObjectGetClass == null)
            {
                statusKey = "craft.aura_unavailable";
                return false;
            }

            if (!this.TryResolveCraftAuraLocalPlayerObject(out IntPtr playerObj, out _) || playerObj == IntPtr.Zero)
            {
                statusKey = "craft.player_unavailable";
                return false;
            }

            if (cachedLevelObjectObj == IntPtr.Zero)
            {
                statusKey = "craft.level_object_unavailable";
                return false;
            }

            IntPtr playerClass = auraMonoObjectGetClass(playerObj);
            IntPtr openMethod = this.FindAuraMonoMethodOnHierarchy(playerClass, "OpenInteractPanel", 3);
            if (openMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr panelNameObj = auraMonoStringNew(this.auraMonoRootDomain, "CraftCompositeMainPanel");
            if (panelNameObj == IntPtr.Zero)
            {
                statusKey = "craft.open_interact_threw";
                return false;
            }

            uint pinPlayer = AuraMonoPinNew(playerObj);
            try
            {
                int interactId = CraftOpenInteractId;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = panelNameObj;
                args[1] = cachedLevelObjectObj;
                args[2] = (IntPtr)(&interactId);
                auraMonoRuntimeInvoke(openMethod, playerObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    statusKey = "craft.open_interact_threw";
                    return false;
                }
            }
            finally
            {
                AuraMonoPinFree(pinPlayer);
            }

            statusKey = "craft.open_ok";
            return true;
        }

        // Scans LevelObjectManager._dictionary for the nearest active craft table (interact 3001).
        private unsafe bool TryFindHomelandCraftTableNetId(out ulong netId, out IntPtr levelObjectObj, out uint levelObjectPin, out string statusKey)
        {
            netId = 0UL;
            levelObjectObj = IntPtr.Zero;
            levelObjectPin = 0U;

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                statusKey = "craft.aura_unavailable";
                return false;
            }

            if (!this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out _, out _)
                || managerObj == IntPtr.Zero)
            {
                statusKey = "craft.level_manager_unavailable";
                return false;
            }

            IntPtr dictionaryObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(managerObj, "_dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(managerObj, "dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero))
            {
                statusKey = "craft.dictionary_unavailable";
                return false;
            }

            List<IntPtr> entries = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(dictionaryObj, entries, pins, 131072) || entries.Count == 0)
                {
                    statusKey = "craft.no_level_objects";
                    return false;
                }

                bool hasPlayer = this.TryGetLocalPlayerPosition(out Vector3 playerPos);
                int craftTables = 0;
                int inactiveCraftTables = 0;
                List<(ulong netId, IntPtr levelObjectObj, float distSq)> activeTables = new List<(ulong, IntPtr, float)>();
                for (int i = 0; i < entries.Count; i++)
                {
                    IntPtr entry = entries[i];
                    if (entry == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr candidateLevelObjectObj = IntPtr.Zero;
                    if ((!this.TryGetMonoObjectMember(entry, "Value", out candidateLevelObjectObj) || candidateLevelObjectObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(entry, "value", out candidateLevelObjectObj) || candidateLevelObjectObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(entry, "_value", out candidateLevelObjectObj) || candidateLevelObjectObj == IntPtr.Zero))
                    {
                        candidateLevelObjectObj = entry;
                    }

                    if (!this.TryAuraMonoReadLevelObjectInteractIds(candidateLevelObjectObj, out int[] interactIds)
                        || interactIds == null
                        || interactIds.Length == 0
                        || Array.IndexOf(interactIds, CraftOpenInteractId) < 0)
                    {
                        continue;
                    }

                    craftTables++;
                    bool isActive = true;
                    if (this.TryGetMonoBoolMember(candidateLevelObjectObj, "isActive", out bool activeFlag))
                    {
                        isActive = activeFlag;
                    }

                    // The level object's own netId is the dictionary key.
                    ulong candidate = 0UL;
                    if ((!this.TryGetMonoUInt64Member(entry, "Key", out candidate) || candidate == 0UL)
                        && (!this.TryGetMonoUInt64Member(entry, "key", out candidate) || candidate == 0UL))
                    {
                        this.TryGetMonoUInt64Member(candidateLevelObjectObj, "netId", out candidate);
                    }

                    if (candidate == 0UL)
                    {
                        continue;
                    }

                    float distSq = (hasPlayer
                            && this.TryExtractHomePositionMonoObject(candidateLevelObjectObj, out Vector3 pos)
                            && pos != Vector3.zero)
                        ? (pos - playerPos).sqrMagnitude
                        : float.MaxValue;

                    if (!isActive)
                    {
                        inactiveCraftTables++;
                        continue;
                    }

                    activeTables.Add((candidate, candidateLevelObjectObj, distSq));
                }

                if (activeTables.Count > 0)
                {
                    activeTables.Sort((a, b) =>
                    {
                        int byDist = a.distSq.CompareTo(b.distSq);
                        return byDist != 0 ? byDist : a.netId.CompareTo(b.netId);
                    });

                    netId = activeTables[0].netId;
                    levelObjectObj = activeTables[0].levelObjectObj;
                }

                if (netId != 0UL)
                {
                    if (levelObjectObj != IntPtr.Zero)
                    {
                        levelObjectPin = AuraMonoPinNew(levelObjectObj);
                    }

                    statusKey = string.Empty;
                    return true;
                }

                if (craftTables > 0 && inactiveCraftTables == craftTables)
                {
                    statusKey = "craft.all_tables_stored";
                    return false;
                }

                statusKey = "craft.no_table";
                return false;
            }
            finally
            {
                for (int i = 0; i < pins.Count; i++)
                {
                    AuraMonoPinFree(pins[i]);
                }
            }
        }

        // Reads a LevelObject's _interactIdArray (a plain int[]) from its mono pointer.
        private unsafe bool TryAuraMonoReadLevelObjectInteractIds(IntPtr levelObjectObj, out int[] interactIds)
        {
            interactIds = null;
            if (levelObjectObj == IntPtr.Zero || auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(levelObjectObj, "_interactIdArray", out IntPtr arrObj) || arrObj == IntPtr.Zero)
            {
                return false;
            }

            int len = (int)auraMonoArrayLength(arrObj);
            if (len <= 0 || len > 256)
            {
                return false;
            }

            IntPtr basePtr = auraMonoArrayAddrWithSize(arrObj, 4, UIntPtr.Zero);
            if (basePtr == IntPtr.Zero)
            {
                return false;
            }

            int[] result = new int[len];
            for (int i = 0; i < len; i++)
            {
                result[i] = Marshal.ReadInt32(basePtr, i * 4);
            }

            interactIds = result;
            return true;
        }


    }
}
