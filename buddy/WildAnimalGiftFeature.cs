using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const bool WildAnimalGiftLogsEnabled = true;
        private const float WildAnimalGiftActionCooldownSeconds = 1.25f;
        private const float WildAnimalGiftDelayBetweenTakesSeconds = 0.45f;

        // Claim is a one-shot button press (not a per-frame hot path), so opt out of the default 4096 entity
        // truncation: in dense towns gift-box level entities can sit past index 4096 and never get inspected.
        private const int WildAnimalGiftEntityScanCap = 65536;

        private IntPtr wildAnimalGiftAuraWildAnimalProtocolClass = IntPtr.Zero;
        private IntPtr wildAnimalGiftAuraHaveGiftGroupsMethod = IntPtr.Zero;
        private IntPtr wildAnimalGiftAuraHaveGiftEntityMethod = IntPtr.Zero;
        private IntPtr wildAnimalGiftAuraGetNetworkEntityMethod = IntPtr.Zero;
        private IntPtr wildAnimalGiftAuraAnimalUtilIsGiftBoxMethod = IntPtr.Zero;
        private IntPtr wildAnimalGiftAuraAnimalUtilGetGroupMethod = IntPtr.Zero;
        private IntPtr wildAnimalGiftAuraTakeGiftMethod = IntPtr.Zero;

        private object wildAnimalGiftCoroutine = null;
        private float wildAnimalGiftBusyUntil = 0f;
        private string wildAnimalGiftLastStatus = "Idle.";
        private HashSet<int> wildAnimalGiftActiveTargetGroupIds = null;

        private void StartWildAnimalClaimAllGifts(bool silent)
        {
            if (this.wildAnimalGiftCoroutine != null)
            {
                if (!silent)
                {
                    this.AddMenuNotification("Wild gift claim already running", new Color(0.45f, 0.88f, 1f));
                }

                return;
            }

            if (Time.realtimeSinceStartup < this.wildAnimalGiftBusyUntil)
            {
                if (!silent)
                {
                    float remaining = Mathf.Max(0f, this.wildAnimalGiftBusyUntil - Time.realtimeSinceStartup);
                    this.AddMenuNotification("Wild gifts: wait " + remaining.ToString("F1") + "s", new Color(0.45f, 0.88f, 1f));
                }

                return;
            }

            this.wildAnimalGiftBusyUntil = Time.realtimeSinceStartup + WildAnimalGiftActionCooldownSeconds;
            this.wildAnimalGiftLastStatus = "Scanning wild animal gifts...";
            this.WildAnimalGiftLog("Claim all started");
            this.wildAnimalGiftCoroutine = ModCoroutines.Start(this.WildAnimalClaimAllGiftsRoutine(silent));
        }

        private IEnumerator WildAnimalClaimAllGiftsRoutine(bool silent)
        {
            yield return null;

            List<uint> netIds = new List<uint>();
            string collectStatus = string.Empty;
            IEnumerator collectRoutine = this.CollectWildAnimalGiftNetIdsRoutine(netIds, status => collectStatus = status);
            while (collectRoutine.MoveNext())
            {
                yield return collectRoutine.Current;
            }

            if (netIds.Count == 0)
            {
                this.wildAnimalGiftLastStatus = collectStatus;
                this.WildAnimalGiftLog("No targets. " + collectStatus);
                if (!silent)
                {
                    this.AddMenuNotification("Wild gifts: " + collectStatus, new Color(0.45f, 0.88f, 1f));
                }

                this.wildAnimalGiftActiveTargetGroupIds = null;
                this.wildAnimalGiftCoroutine = null;
                this.wildAnimalGiftBusyUntil = Time.realtimeSinceStartup + WildAnimalGiftActionCooldownSeconds;
                yield break;
            }

            int claimed = 0;
            int failed = 0;
            try
            {
                for (int i = 0; i < netIds.Count; i++)
                {
                    uint netId = netIds[i];
                    if (netId == 0U)
                    {
                        continue;
                    }

                    if (!this.TryInvokeWildAnimalTakeGiftAuraMono(netId, out string takeStatus))
                    {
                        failed++;
                        this.WildAnimalGiftLog("TakeGift failed netId=" + netId + ": " + takeStatus);
                    }
                    else
                    {
                        claimed++;
                        this.WildAnimalGiftLog("TakeGift ok netId=" + netId);
                    }

                    yield return new WaitForSecondsRealtime(WildAnimalGiftDelayBetweenTakesSeconds);
                }

                this.wildAnimalGiftLastStatus = "Claimed " + claimed + "/" + netIds.Count
                    + (failed > 0 ? ", failed " + failed : string.Empty);
                if (!silent || claimed > 0)
                {
                    this.AddMenuNotification(
                        "Wild gifts: " + claimed + " claimed" + (failed > 0 ? ", " + failed + " failed" : string.Empty),
                        claimed > 0 ? new Color(0.45f, 1f, 0.55f) : new Color(0.45f, 0.88f, 1f));
                }
            }
            finally
            {
                this.wildAnimalGiftActiveTargetGroupIds = null;
                this.wildAnimalGiftCoroutine = null;
                this.wildAnimalGiftBusyUntil = Time.realtimeSinceStartup + WildAnimalGiftActionCooldownSeconds;
            }
        }

        private IEnumerator CollectWildAnimalGiftNetIdsRoutine(List<uint> netIds, Action<string> complete)
        {
            netIds?.Clear();
            if (netIds == null)
            {
                complete?.Invoke("target list unavailable");
                yield break;
            }

            yield return null;

            List<IntPtr> haveGiftGroups = new List<IntPtr>();
            if (!this.TryCollectWildAnimalHaveGiftGroupsAuraMono(haveGiftGroups, out int giftCount, out string groupNote) || giftCount <= 0)
            {
                this.WildAnimalGiftLog("HaveGift: " + groupNote);
                complete?.Invoke("no wild gifts available");
                yield break;
            }

            this.WildAnimalGiftLog("HaveGift: " + groupNote + ", groups=" + haveGiftGroups.Count);

            yield return null;

            HashSet<uint> seen = new HashSet<uint>();
            HashSet<int> targetGroupIds = this.BuildWildAnimalGiftTargetGroupIds(haveGiftGroups);
            this.wildAnimalGiftActiveTargetGroupIds = targetGroupIds;

            if (this.TryCollectWildAnimalGiftNetIdsFromEntityScanAuraMono(targetGroupIds, seen, netIds, giftCount, out string entityScanNote))
            {
                this.WildAnimalGiftLog("Entity scan: " + entityScanNote);
            }
            else
            {
                this.WildAnimalGiftLog("Entity scan failed: " + entityScanNote);
            }

            this.WildAnimalGiftLog("Collect done targets=" + netIds.Count + "/" + giftCount
                + (netIds.Count > 0 ? " netIds=[" + string.Join(",", netIds) + "]" : string.Empty));

            string status = netIds.Count > 0
                ? netIds.Count + " target(s), pending=" + giftCount
                : "no claimable wild gifts found, pending=" + giftCount;
            complete?.Invoke(status);
        }

        private unsafe bool TryCollectWildAnimalHaveGiftGroupsAuraMono(List<IntPtr> groupItems, out int giftCount, out string status)
        {
            giftCount = 0;
            status = "unavailable";
            groupItems?.Clear();

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono API unavailable";
                return false;
            }

            if (!this.TryEnsureWildAnimalGiftAuraWildAnimalProtocolClass(out IntPtr protocolClass))
            {
                status = "WildAnimalProtocolManager missing";
                return false;
            }

            if (this.wildAnimalGiftAuraHaveGiftGroupsMethod == IntPtr.Zero)
            {
                this.wildAnimalGiftAuraHaveGiftGroupsMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "HaveGift", 0);
                if (this.wildAnimalGiftAuraHaveGiftGroupsMethod == IntPtr.Zero)
                {
                    status = "HaveGift() missing";
                    return false;
                }
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr groupsObj = auraMonoRuntimeInvoke(this.wildAnimalGiftAuraHaveGiftGroupsMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "HaveGift invoke failed";
                return false;
            }

            if (groupsObj == IntPtr.Zero)
            {
                status = "count=0";
                return true;
            }

            List<IntPtr> items = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(groupsObj, items))
            {
                status = "collection enumerate failed";
                return false;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == IntPtr.Zero)
                {
                    continue;
                }

                giftCount++;
                if (groupItems != null)
                {
                    groupItems.Add(items[i]);
                    if (this.TryGetWildAnimalFeedGroupIdAuraMono(items[i], out int groupId))
                    {
                        this.WildAnimalGiftLog("HaveGift group[" + i + "] id=" + groupId);
                    }
                    else
                    {
                        this.WildAnimalGiftLog("HaveGift group[" + i + "] id unreadable");
                    }
                }
            }

            status = "count=" + giftCount;
            return true;
        }

        private HashSet<int> BuildWildAnimalGiftTargetGroupIds(List<IntPtr> haveGiftGroups)
        {
            HashSet<int> targetGroupIds = new HashSet<int>();
            if (haveGiftGroups == null)
            {
                return targetGroupIds;
            }

            for (int i = 0; i < haveGiftGroups.Count; i++)
            {
                if (this.TryGetWildAnimalFeedGroupIdAuraMono(haveGiftGroups[i], out int groupId) && groupId > 0)
                {
                    targetGroupIds.Add(groupId);
                }
            }

            return targetGroupIds;
        }

        private bool TryCollectWildAnimalGiftNetIdsFromEntityScanAuraMono(
            HashSet<int> targetGroupIds,
            HashSet<uint> seen,
            List<uint> netIds,
            int targetCount,
            out string status)
        {
            status = "entity scan skipped";
            if (targetGroupIds == null || targetGroupIds.Count <= 0 || seen == null || netIds == null || targetCount <= 0)
            {
                status = "entity scan inputs empty";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "AuraMono unavailable";
                return false;
            }

            // Primary: gift boxes are spawned with WildAnimalGiftComponentData (see
            // WildAnimalProtocolManager.SpawnGift), so Entities.GetComponents<WildAnimalGiftComponent>
            // returns every claimable gift directly — no full entity-graph walk and no 4096-entity
            // truncation that previously made the claim scan miss gifts in dense towns. Falls back to
            // the legacy walk only when the component query is unavailable on this build.
            List<IntPtr> entities;
            string enumerateStatus;
            if (!this.TryCollectWildAnimalGiftComponentEntities(out entities, out enumerateStatus))
            {
                int previousCap = this.auraMonoEntityEnumerationCapOverride;
                this.auraMonoEntityEnumerationCapOverride = WildAnimalGiftEntityScanCap;
                try
                {
                    if (!this.TryEnumerateAuraMonoLoadedEntityObjects(out entities, out enumerateStatus) || entities.Count <= 0)
                    {
                        status = enumerateStatus;
                        return false;
                    }
                }
                finally
                {
                    this.auraMonoEntityEnumerationCapOverride = previousCap;
                }
            }

            int inspected = 0;
            int netResolved = 0;
            int giftBoxes = 0;
            int animalGifts = 0;
            int added = 0;
            this.WildAnimalGiftLog("Entity scan: entities=" + entities.Count + " targetGroups=" + targetGroupIds.Count);

            for (int i = 0; i < entities.Count && netIds.Count < targetCount; i++)
            {
                IntPtr entityObj = entities[i];
                if (entityObj == IntPtr.Zero || !this.TryGetAuraMonoEntityNetId(entityObj, out uint entityNetId) || entityNetId == 0U)
                {
                    continue;
                }

                inspected++;
                if (!this.TryGetNetworkEntityAuraMono(entityNetId, out IntPtr networkEntityObj) || networkEntityObj == IntPtr.Zero)
                {
                    continue;
                }

                netResolved++;

                bool matched = false;
                bool isGiftBox = false;
                int groupId = 0;
                if (this.TryAuraMonoAnimalUtilIsGiftBox(networkEntityObj, out isGiftBox) && isGiftBox)
                {
                    giftBoxes++;
                    if (this.TryAuraMonoAnimalUtilGetGroup(networkEntityObj, out groupId)
                        && groupId > 0
                        && targetGroupIds.Contains(groupId))
                    {
                        matched = true;
                    }
                }

                if (!matched
                    && this.TryAuraMonoWildAnimalHaveGiftEntity(networkEntityObj)
                    && this.TryAuraMonoAnimalUtilGetGroup(networkEntityObj, out groupId)
                    && groupId > 0
                    && targetGroupIds.Contains(groupId))
                {
                    animalGifts++;
                    matched = true;
                }

                if (!matched)
                {
                    continue;
                }

                this.WildAnimalGiftLog("Entity gift netId=" + entityNetId + " group=" + groupId
                    + (isGiftBox ? " giftBox" : " animalGift"));

                if (this.TryAddWildAnimalGiftNetId(entityNetId, seen, netIds))
                {
                    added++;
                }
            }

            status = added + " from entity scan (inspected=" + inspected
                + " netResolved=" + netResolved
                + " giftBoxes=" + giftBoxes + " animalGifts=" + animalGifts
                + " methods[getNetworkEntity=" + (this.wildAnimalGiftAuraGetNetworkEntityMethod != IntPtr.Zero)
                + " isGiftBox=" + (this.wildAnimalGiftAuraAnimalUtilIsGiftBoxMethod != IntPtr.Zero)
                + " haveGiftEntity=" + (this.wildAnimalGiftAuraHaveGiftEntityMethod != IntPtr.Zero)
                + " getGroup=" + (this.wildAnimalGiftAuraAnimalUtilGetGroupMethod != IntPtr.Zero) + "])";
            return true;
        }

        // Returns the owner entities of all WildAnimalGiftComponent instances via the safe direct-ECS
        // GetComponents path. Gated on GetComponents readiness: once ready it is authoritative (returns
        // true even with 0 gifts, so the caller never falls back to the crash-prone / truncation-prone
        // entity-graph walk). Returns false only when GetComponents is unavailable on this build.
        // Returned gift entity pointers are pinned into wildAnimalGiftScanEntityPins so they survive the
        // moving sgen GC until the caller reads them; released at the start of the next scan.
        private readonly List<uint> wildAnimalGiftScanEntityPins = new List<uint>();

        private bool TryCollectWildAnimalGiftComponentEntities(out List<IntPtr> giftEntities, out string status)
        {
            giftEntities = null;
            status = string.Empty;
            FreeAuraMonoPins(this.wildAnimalGiftScanEntityPins);

            if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                status = "GetComponents not ready";
                return false;
            }

            IntPtr giftClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.WildAnimal.WildAnimalGiftComponent");
            if (giftClass == IntPtr.Zero)
            {
                giftClass = this.FindAuraMonoClassByFullName("ScriptsRefactory.LevelAndEntity.Gameplay.Component.WildAnimal.WildAnimalGiftComponent");
            }

            if (giftClass == IntPtr.Zero)
            {
                status = "WildAnimalGiftComponent class unavailable";
                return false;
            }

            List<IntPtr> result = new List<IntPtr>(32);
            // Pin components for the loop (the "entity" member read touches the component); each accepted
            // gift entity is pinned for the caller's lifetime against the moving sgen GC.
            List<uint> compPins = new List<uint>();
            if (this.TryAuraMonoGetComponentObjects(giftClass, out List<IntPtr> components, compPins) && components != null)
            {
                HashSet<IntPtr> seenObj = new HashSet<IntPtr>();
                try
                {
                    for (int i = 0; i < components.Count; i++)
                    {
                        IntPtr componentObj = components[i];
                        if (componentObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        // Gift-box entity is the component's back-reference.
                        IntPtr entityObj = IntPtr.Zero;
                        if ((!this.TryGetMonoObjectMember(componentObj, "entity", out entityObj) || entityObj == IntPtr.Zero)
                            && (!this.TryGetMonoObjectMember(componentObj, "_entity", out entityObj) || entityObj == IntPtr.Zero))
                        {
                            continue;
                        }

                        if (seenObj.Add(entityObj))
                        {
                            result.Add(entityObj);
                            uint entityPin = AuraMonoPinNew(entityObj);
                            if (entityPin != 0U)
                            {
                                this.wildAnimalGiftScanEntityPins.Add(entityPin);
                            }
                        }
                    }
                }
                finally
                {
                    FreeAuraMonoPins(compPins);
                }
            }
            else
            {
                FreeAuraMonoPins(compPins);
            }

            giftEntities = result;
            status = "Gift components via GetComponents=" + result.Count;
            this.WildAnimalGiftLog("Gift scan via GetComponents<WildAnimalGiftComponent>: entities=" + result.Count);
            return true;
        }

        private bool TryEnsureWildAnimalGiftAuraWildAnimalProtocolClass(out IntPtr protocolClass)
        {
            protocolClass = this.wildAnimalGiftAuraWildAnimalProtocolClass;
            if (protocolClass != IntPtr.Zero)
            {
                return true;
            }

            protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.WildAnimal.WildAnimalProtocolManager");
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                    "XDTDataAndProtocol.ProtocolService.WildAnimal",
                    "WildAnimalProtocolManager");
            }

            if (protocolClass != IntPtr.Zero)
            {
                this.wildAnimalGiftAuraWildAnimalProtocolClass = protocolClass;
            }

            return protocolClass != IntPtr.Zero;
        }

        private unsafe bool TryGetNetworkEntityAuraMono(uint netId, out IntPtr entityObj)
        {
            entityObj = IntPtr.Zero;
            if (netId == 0U || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.wildAnimalGiftAuraGetNetworkEntityMethod == IntPtr.Zero)
            {
                IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Animal.AnimalProtocolManager");
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTDataAndProtocol.ProtocolService.Animal",
                        "AnimalProtocolManager");
                }

                if (protocolClass != IntPtr.Zero)
                {
                    this.wildAnimalGiftAuraGetNetworkEntityMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "GetNetworkEntity", 1);
                }
            }

            if (this.wildAnimalGiftAuraGetNetworkEntityMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&netId);
            entityObj = auraMonoRuntimeInvoke(this.wildAnimalGiftAuraGetNetworkEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero && entityObj != IntPtr.Zero;
        }

        private unsafe bool TryAuraMonoAnimalUtilIsGiftBox(IntPtr entityObj, out bool isGiftBox)
        {
            isGiftBox = false;
            if (entityObj == IntPtr.Zero || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.wildAnimalGiftAuraAnimalUtilIsGiftBoxMethod == IntPtr.Zero)
            {
                IntPtr utilClass = this.FindAuraMonoClassByFullName("XDT.Scene.Shared.Modules.Animal.AnimalUtil");
                if (utilClass == IntPtr.Zero)
                {
                    utilClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.Animal", "AnimalUtil");
                }

                if (utilClass != IntPtr.Zero)
                {
                    this.wildAnimalGiftAuraAnimalUtilIsGiftBoxMethod = this.FindAuraMonoMethodOnHierarchy(utilClass, "IsGiftBox", 1);
                }
            }

            if (this.wildAnimalGiftAuraAnimalUtilIsGiftBoxMethod == IntPtr.Zero)
            {
                return false;
            }

            return this.TryInvokeAuraMonoStaticBoolMethod(
                this.wildAnimalGiftAuraAnimalUtilIsGiftBoxMethod,
                entityObj,
                out isGiftBox)
                && isGiftBox;
        }

        private unsafe bool TryAuraMonoAnimalUtilGetGroup(IntPtr entityObj, out int groupId)
        {
            groupId = 0;
            if (entityObj == IntPtr.Zero || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.wildAnimalGiftAuraAnimalUtilGetGroupMethod == IntPtr.Zero)
            {
                IntPtr utilClass = this.FindAuraMonoClassByFullName("XDT.Scene.Shared.Modules.Animal.AnimalUtil");
                if (utilClass == IntPtr.Zero)
                {
                    utilClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.Animal", "AnimalUtil");
                }

                if (utilClass != IntPtr.Zero)
                {
                    this.wildAnimalGiftAuraAnimalUtilGetGroupMethod = this.FindAuraMonoMethodOnHierarchy(utilClass, "GetGroup", 1);
                }
            }

            if (this.wildAnimalGiftAuraAnimalUtilGetGroupMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = this.TryUnboxEntityArgForAuraMonoInvoke(entityObj);
            IntPtr boxedGroup = auraMonoRuntimeInvoke(this.wildAnimalGiftAuraAnimalUtilGetGroupMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxedGroup == IntPtr.Zero)
            {
                args[0] = entityObj;
                boxedGroup = auraMonoRuntimeInvoke(this.wildAnimalGiftAuraAnimalUtilGetGroupMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            return exc == IntPtr.Zero
                && boxedGroup != IntPtr.Zero
                && this.TryGetWildAnimalFeedGroupIdAuraMono(boxedGroup, out groupId)
                && groupId > 0;
        }

        private unsafe bool TryAuraMonoWildAnimalHaveGiftEntity(IntPtr entityObj)
        {
            if (entityObj == IntPtr.Zero || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.wildAnimalGiftAuraHaveGiftEntityMethod == IntPtr.Zero)
            {
                if (!this.TryEnsureWildAnimalGiftAuraWildAnimalProtocolClass(out IntPtr protocolClass))
                {
                    return false;
                }

                this.wildAnimalGiftAuraHaveGiftEntityMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "HaveGift", 1);
            }

            if (this.wildAnimalGiftAuraHaveGiftEntityMethod == IntPtr.Zero)
            {
                return false;
            }

            return this.TryInvokeAuraMonoStaticBoolMethod(this.wildAnimalGiftAuraHaveGiftEntityMethod, entityObj, out bool hasGift) && hasGift;
        }

        private unsafe bool TryInvokeAuraMonoStaticBoolMethod(IntPtr method, IntPtr entityObj, out bool value)
        {
            value = false;
            if (method == IntPtr.Zero || entityObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = this.TryUnboxEntityArgForAuraMonoInvoke(entityObj);
            IntPtr boxedResult = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxedResult == IntPtr.Zero)
            {
                args[0] = entityObj;
                boxedResult = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            return exc == IntPtr.Zero && boxedResult != IntPtr.Zero && this.TryUnboxMonoBoolean(boxedResult, out value);
        }

        private IntPtr TryUnboxEntityArgForAuraMonoInvoke(IntPtr entityObj)
        {
            if (entityObj == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return entityObj;
            }

            IntPtr unboxed = auraMonoObjectUnbox(entityObj);
            return unboxed != IntPtr.Zero ? unboxed : entityObj;
        }

        private bool TryOwnerNetIdHasClaimableWildGift(uint ownerNetId, HashSet<int> targetGroupIds, out string note)
        {
            note = "not claimable";
            if (ownerNetId == 0U)
            {
                return false;
            }

            if (!this.TryGetNetworkEntityAuraMono(ownerNetId, out IntPtr networkEntityObj) || networkEntityObj == IntPtr.Zero)
            {
                note = "network entity missing";
                return false;
            }

            if (this.TryAuraMonoAnimalUtilIsGiftBox(networkEntityObj, out bool isGiftBox) && isGiftBox)
            {
                if (this.TryAuraMonoAnimalUtilGetGroup(networkEntityObj, out int giftBoxGroupId)
                    && giftBoxGroupId > 0
                    && (targetGroupIds == null || targetGroupIds.Count <= 0 || targetGroupIds.Contains(giftBoxGroupId)))
                {
                    note = "GiftBox group=" + giftBoxGroupId;
                    return true;
                }

                note = "gift box wrong group";
                return false;
            }

            if (this.TryAuraMonoWildAnimalHaveGiftEntity(networkEntityObj)
                && this.TryAuraMonoAnimalUtilGetGroup(networkEntityObj, out int animalGroupId)
                && animalGroupId > 0
                && (targetGroupIds == null || targetGroupIds.Count <= 0 || targetGroupIds.Contains(animalGroupId)))
            {
                note = "AnimalGift group=" + animalGroupId;
                return true;
            }

            note = "no gift component";
            return false;
        }

        private bool TryAddWildAnimalGiftNetId(uint netId, HashSet<uint> seen, List<uint> netIds)
        {
            if (netId == 0U || seen == null || netIds == null || !seen.Add(netId))
            {
                return false;
            }

            netIds.Add(netId);
            return true;
        }

        private unsafe bool TryInvokeWildAnimalTakeGiftAuraMono(uint netId, out string status)
        {
            status = "TakeGift unavailable";
            if (netId == 0U || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.wildAnimalGiftAuraTakeGiftMethod == IntPtr.Zero)
            {
                IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Animal.AnimalProtocolManager");
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTDataAndProtocol.ProtocolService.Animal",
                        "AnimalProtocolManager");
                }

                if (protocolClass == IntPtr.Zero)
                {
                    status = "AnimalProtocolManager missing";
                    return false;
                }

                this.wildAnimalGiftAuraTakeGiftMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "TakeGift", 1);
                if (this.wildAnimalGiftAuraTakeGiftMethod == IntPtr.Zero)
                {
                    status = "AnimalProtocolManager.TakeGift missing";
                    return false;
                }
            }

            if (!this.TryGetNetworkEntityAuraMono(netId, out _))
            {
                status = "gift entity unavailable";
                return false;
            }

            if (!this.TryOwnerNetIdHasClaimableWildGift(netId, this.wildAnimalGiftActiveTargetGroupIds, out string claimNote))
            {
                status = claimNote;
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&netId);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.wildAnimalGiftAuraTakeGiftMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "TakeGift failed";
                return false;
            }

            status = "ok";
            return true;
        }


        private void WildAnimalGiftLog(string message)
        {
            if (!WildAnimalGiftLogsEnabled || string.IsNullOrEmpty(message))
            {
                return;
            }

            ModLogger.Msg("[WildAnimalGift] " + message);
        }
    }
}
