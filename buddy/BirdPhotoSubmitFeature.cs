using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private static bool BirdPhotoSubmitLogsEnabled => MasterLogBirdPhotoSubmit;
        private const float BirdPhotoSubmitDelaySeconds = 0.65f;
        private const int BirdPhotoEntityTypeValue = 504;
        private const int BirdPhotoExchangeListMaxItems = 50;

        private object birdPhotoSubmitCoroutine = null;
        private string birdPhotoSubmitLastStatus = "Idle.";

        private IntPtr birdPhotoExchangeDataClass = IntPtr.Zero;
        private IntPtr birdPhotoExchangeItemNetIdField = IntPtr.Zero;
        private IntPtr birdPhotoExchangeNumField = IntPtr.Zero;
        private IntPtr birdPhotoExchangeListClass = IntPtr.Zero;
        private IntPtr birdPhotoExchangeListAddMethod = IntPtr.Zero;
        private IntPtr birdPhotoExchangeMethod = IntPtr.Zero;
        private IntPtr birdPhotoExchangeLimitMethod = IntPtr.Zero;

        private void StartBirdPhotoAutoSubmit(bool silent)
        {
            if (this.birdPhotoSubmitCoroutine != null || this.dailyQuestSubmitCoroutine != null || this.dailyClaimsCoroutine != null)
            {
                if (!silent)
                {
                    this.AddMenuNotification("Submit already running", new Color(0.45f, 0.88f, 1f));
                }

                return;
            }

            this.birdPhotoSubmitLastStatus = "Submitting bird info cards...";
            this.birdPhotoSubmitCoroutine = ModCoroutines.Start(this.BirdPhotoAutoSubmitRoutine(silent));
        }

        private IEnumerator BirdPhotoAutoSubmitRoutine(bool silent)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                this.birdPhotoSubmitLastStatus = "AuraMono unavailable";
                if (!silent)
                {
                    this.AddMenuNotification(this.birdPhotoSubmitLastStatus, new Color(1f, 0.55f, 0.45f));
                }

                this.birdPhotoSubmitCoroutine = null;
                yield break;
            }

            int batches = 0;
            int totalCards = 0;

            while (true)
            {
                if (!this.TryGetBirdPhotoExchangeLimitAura(out int limit, out string limitStatus))
                {
                    this.birdPhotoSubmitLastStatus = limitStatus;
                    if (!silent)
                    {
                        this.AddMenuNotification("Bird photo: " + limitStatus, new Color(1f, 0.55f, 0.45f));
                    }

                    break;
                }

                if (limit <= 0)
                {
                    this.birdPhotoSubmitLastStatus = batches > 0
                        ? "Done: " + totalCards + " card(s) in " + batches + " batch(es); daily limit reached"
                        : "No submissions left today";
                    if (!silent || totalCards > 0)
                    {
                        this.AddMenuNotification(
                            totalCards > 0 ? "Bird photos: " + totalCards + " submitted" : this.birdPhotoSubmitLastStatus,
                            totalCards > 0 ? new Color(0.45f, 1f, 0.55f) : new Color(0.45f, 0.88f, 1f));
                    }

                    break;
                }

                if (!this.TryBuildBirdPhotoExchangePairsAura(limit, out List<BirdPhotoSubmitPair> pairs, out string buildStatus))
                {
                    this.birdPhotoSubmitLastStatus = buildStatus;
                    if (!silent)
                    {
                        this.AddMenuNotification("Bird photo: " + buildStatus, new Color(1f, 0.55f, 0.45f));
                    }

                    break;
                }

                if (!this.TrySendBirdPhotoExchangeAura(pairs, out string sendStatus))
                {
                    this.birdPhotoSubmitLastStatus = sendStatus;
                    this.BirdPhotoSubmitLog(sendStatus);
                    if (!silent)
                    {
                        this.AddMenuNotification("Bird photo failed: " + sendStatus, new Color(1f, 0.55f, 0.45f));
                    }

                    break;
                }

                int batchCards = 0;
                for (int i = 0; i < pairs.Count; i++)
                {
                    batchCards += pairs[i].Num;
                }

                batches++;
                totalCards += batchCards;
                this.birdPhotoSubmitLastStatus = "Sent " + batchCards + " card(s), waiting...";
                this.BirdPhotoSubmitLog("batch " + batches + " cards=" + batchCards + " " + sendStatus);
                yield return new WaitForSecondsRealtime(BirdPhotoSubmitDelaySeconds);

                if (!this.TryGetBirdPhotoExchangeLimitAura(out int limitAfter, out _))
                {
                    break;
                }

                if (limitAfter >= limit)
                {
                    this.birdPhotoSubmitLastStatus = "Done: " + totalCards + " card(s); limit unchanged (server?)";
                    break;
                }

                limit = limitAfter;
            }

            if (batches > 0 && (this.birdPhotoSubmitLastStatus == null || this.birdPhotoSubmitLastStatus.StartsWith("Sent ", StringComparison.Ordinal)))
            {
                this.birdPhotoSubmitLastStatus = "Done: " + totalCards + " card(s) in " + batches + " batch(es)";
            }

            this.BirdPhotoSubmitLog(this.birdPhotoSubmitLastStatus);
            this.birdPhotoSubmitCoroutine = null;
        }

        private bool TryBuildBirdPhotoExchangePairsAura(int limit, out List<BirdPhotoSubmitPair> pairs, out string status)
        {
            pairs = new List<BirdPhotoSubmitPair>();
            status = string.Empty;
            if (limit <= 0)
            {
                status = "no submissions left today";
                return false;
            }

            if (!this.TryEnumerateDailyQuestStorageItemsAura(out List<IntPtr> items, out List<uint> itemPins, out string itemsStatus))
            {
                FreeAuraMonoPins(itemPins);
                status = itemsStatus;
                return false;
            }

            int remaining = limit;
            for (int i = 0; i < items.Count && remaining > 0; i++)
            {
                IntPtr itemObj = items[i];
                if (itemObj == IntPtr.Zero)
                {
                    continue;
                }

                if (!this.TryGetDirectBackpackItemEntityType(itemObj, out int entityType) || entityType != BirdPhotoEntityTypeValue)
                {
                    continue;
                }

                if (this.TryGetDirectBackpackItemIsLocked(itemObj, out bool isLocked) && isLocked)
                {
                    continue;
                }

                if (!this.TryGetDirectBackpackItemNetId(itemObj, out uint netId) || netId == 0U)
                {
                    continue;
                }

                if (!this.TryGetDirectBackpackItemCount(itemObj, out int count) || count <= 0)
                {
                    continue;
                }

                int take = Math.Min(count, remaining);
                if (take <= 0)
                {
                    continue;
                }

                pairs.Add(new BirdPhotoSubmitPair { ItemNetId = netId, Num = take });
                remaining -= take;
            }

            FreeAuraMonoPins(itemPins);

            if (pairs.Count == 0)
            {
                status = "no bird info cards in backpack/warehouse";
                return false;
            }

            this.BirdPhotoMergePairsByNetId(pairs);
            status = "ready " + pairs.Count + " stack(s), slots=" + (limit - remaining);
            return true;
        }

        private void BirdPhotoMergePairsByNetId(List<BirdPhotoSubmitPair> pairs)
        {
            if (pairs == null || pairs.Count <= 1)
            {
                return;
            }

            Dictionary<uint, int> merged = new Dictionary<uint, int>();
            for (int i = 0; i < pairs.Count; i++)
            {
                BirdPhotoSubmitPair pair = pairs[i];
                if (pair.ItemNetId == 0U || pair.Num <= 0)
                {
                    continue;
                }

                if (merged.TryGetValue(pair.ItemNetId, out int existing))
                {
                    merged[pair.ItemNetId] = existing + pair.Num;
                }
                else
                {
                    merged[pair.ItemNetId] = pair.Num;
                }
            }

            pairs.Clear();
            foreach (KeyValuePair<uint, int> entry in merged)
            {
                pairs.Add(new BirdPhotoSubmitPair { ItemNetId = entry.Key, Num = entry.Value });
            }
        }

        private bool TryGetBirdPhotoExchangeLimitAura(out int limit, out string status)
        {
            limit = 0;
            status = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono unavailable";
                return false;
            }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BirdWatching.BirdStandSystem", out IntPtr birdStandObj)
                || birdStandObj == IntPtr.Zero)
            {
                status = "BirdStandSystem unavailable";
                return false;
            }

            if (this.birdPhotoExchangeLimitMethod == IntPtr.Zero)
            {
                IntPtr birdStandClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(birdStandObj) : IntPtr.Zero;
                this.birdPhotoExchangeLimitMethod = birdStandClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(birdStandClass, "GetBirdPhotoExchangeLimit", 0)
                    : IntPtr.Zero;
            }

            if (this.birdPhotoExchangeLimitMethod == IntPtr.Zero)
            {
                status = "GetBirdPhotoExchangeLimit missing";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(this.birdPhotoExchangeLimitMethod, birdStandObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || result == IntPtr.Zero || !this.TryUnboxMonoInt32(result, out limit))
            {
                status = "GetBirdPhotoExchangeLimit failed";
                return false;
            }

            status = "limit=" + limit;
            return true;
        }

        private bool TrySendBirdPhotoExchangeAura(List<BirdPhotoSubmitPair> pairs, out string status)
        {
            status = string.Empty;
            if (pairs == null || pairs.Count == 0)
            {
                status = "empty exchange list";
                return false;
            }

            if (pairs.Count > BirdPhotoExchangeListMaxItems)
            {
                status = "too many stacks (max " + BirdPhotoExchangeListMaxItems + ")";
                return false;
            }

            if (!this.TryCreateBirdPhotoExchangeListAuraMonoNative(pairs, out IntPtr listObj, out string listStatus))
            {
                status = listStatus;
                return false;
            }

            if (!this.TryInvokeBirdPhotoExchangeAura(listObj, out string invokeStatus))
            {
                status = listStatus + "; " + invokeStatus;
                return false;
            }

            status = listStatus + "; " + invokeStatus;
            return true;
        }

        private unsafe bool TryInvokeBirdPhotoExchangeAura(IntPtr listObj, out string status)
        {
            status = "unavailable";
            if (listObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.birdPhotoExchangeMethod == IntPtr.Zero)
            {
                IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.GamePlay.Bird.BirdProtocolManager");
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.GamePlay.Bird", "BirdProtocolManager");
                }

                if (protocolClass == IntPtr.Zero)
                {
                    status = "BirdProtocolManager missing";
                    return false;
                }

                this.birdPhotoExchangeMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "BirdPhotoExchange", 1);
            }

            if (this.birdPhotoExchangeMethod == IntPtr.Zero)
            {
                status = "BirdPhotoExchange missing";
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = listObj;
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.birdPhotoExchangeMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "BirdPhotoExchange exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "BirdPhotoExchange sent";
            return true;
        }

        private unsafe bool TryCreateBirdPhotoExchangeListAuraMonoNative(List<BirdPhotoSubmitPair> pairs, out IntPtr listObj, out string status)
        {
            listObj = IntPtr.Zero;
            status = string.Empty;
            if (pairs == null || pairs.Count == 0)
            {
                status = "empty pairs";
                return false;
            }

            this.ResolveAuraFarmRuntimeMethodsViaMono();
            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoStringNew == null
                || auraMonoObjectGetClass == null
                || auraMonoObjectNew == null
                || auraMonoFieldSetValue == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                status = "AuraMono list prerequisites unavailable";
                return false;
            }

            if (!this.TryResolveBirdPhotoExchangeDataMembers(out IntPtr pairClass, out IntPtr netIdField, out IntPtr numField))
            {
                status = "BirdPhotoExchangeData class/fields missing";
                return false;
            }

            if (this.birdPhotoExchangeListClass != IntPtr.Zero && auraMonoObjectNew != null)
            {
                listObj = auraMonoObjectNew(this.auraMonoRootDomain, this.birdPhotoExchangeListClass);
                if (listObj != IntPtr.Zero && auraMonoRuntimeObjectInit != null)
                {
                    auraMonoRuntimeObjectInit(listObj);
                }
            }

            string[] listTypeCandidates = new[]
            {
                "System.Collections.Generic.List`1[[EcsClient.XDT.Scene.Shared.Modules.ItemExchange.BirdPhotoExchangeData, EcsClient]]",
                "System.Collections.Generic.List`1[[EcsClient.XDT.Scene.Shared.Modules.ItemExchange.BirdPhotoExchangeData, Client]]"
            };

            IntPtr* typeArgs = stackalloc IntPtr[1];
            IntPtr* createArgs = stackalloc IntPtr[1];
            for (int i = 0; i < listTypeCandidates.Length && listObj == IntPtr.Zero; i++)
            {
                IntPtr typeNameObj = auraMonoStringNew(this.auraMonoRootDomain, listTypeCandidates[i]);
                if (typeNameObj == IntPtr.Zero)
                {
                    continue;
                }

                typeArgs[0] = typeNameObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                {
                    continue;
                }

                createArgs[0] = typeObj;
                exc = IntPtr.Zero;
                listObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)createArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    listObj = IntPtr.Zero;
                }
            }

            if (listObj == IntPtr.Zero)
            {
                status = "List<BirdPhotoExchangeData> create failed";
                return false;
            }

            IntPtr listClass = auraMonoObjectGetClass(listObj);
            if (this.birdPhotoExchangeListClass == IntPtr.Zero && listClass != IntPtr.Zero)
            {
                this.birdPhotoExchangeListClass = listClass;
            }

            IntPtr addMethod = this.birdPhotoExchangeListAddMethod;
            if (addMethod == IntPtr.Zero && listClass != IntPtr.Zero)
            {
                addMethod = this.FindAuraMonoMethodOnHierarchy(listClass, "Add", 1);
                this.birdPhotoExchangeListAddMethod = addMethod;
            }

            if (addMethod == IntPtr.Zero)
            {
                status = "List.Add missing";
                return false;
            }

            IntPtr* addArgs = stackalloc IntPtr[1];
            bool pairIsValueType = auraMonoClassIsValueType != null && auraMonoClassIsValueType(pairClass) != 0;
            const int structSize = 8;
            for (int i = 0; i < pairs.Count; i++)
            {
                uint netId = pairs[i].ItemNetId;
                int num = pairs[i].Num;
                IntPtr exc = IntPtr.Zero;

                if (pairIsValueType)
                {
                    byte* structData = stackalloc byte[structSize];
                    *(uint*)structData = netId;
                    *(int*)(structData + sizeof(uint)) = num;
                    addArgs[0] = (IntPtr)structData;
                    auraMonoRuntimeInvoke(addMethod, listObj, (IntPtr)addArgs, ref exc);
                }
                else
                {
                    IntPtr pairObj = auraMonoObjectNew(this.auraMonoRootDomain, pairClass);
                    if (pairObj == IntPtr.Zero)
                    {
                        status = "BirdPhotoExchangeData alloc failed";
                        return false;
                    }

                    auraMonoFieldSetValue(pairObj, netIdField, (IntPtr)(&netId));
                    auraMonoFieldSetValue(pairObj, numField, (IntPtr)(&num));
                    addArgs[0] = pairObj;
                    auraMonoRuntimeInvoke(addMethod, listObj, (IntPtr)addArgs, ref exc);
                }

                if (exc != IntPtr.Zero)
                {
                    status = "List.Add failed exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }
            }

            if (!this.TryGetAuraMonoListCount(listObj, listClass, out int monoListCount) || monoListCount != pairs.Count)
            {
                status = "List count mismatch expected=" + pairs.Count + " actual=" + monoListCount;
                return false;
            }

            status = "List<BirdPhotoExchangeData> count=" + monoListCount;
            return true;
        }

        private bool TryResolveBirdPhotoExchangeDataMembers(out IntPtr pairClass, out IntPtr netIdField, out IntPtr numField)
        {
            pairClass = this.birdPhotoExchangeDataClass;
            netIdField = this.birdPhotoExchangeItemNetIdField;
            numField = this.birdPhotoExchangeNumField;
            if (pairClass != IntPtr.Zero && netIdField != IntPtr.Zero && numField != IntPtr.Zero)
            {
                return true;
            }

            pairClass = this.FindAuraMonoClassByFullName("EcsClient.XDT.Scene.Shared.Modules.ItemExchange.BirdPhotoExchangeData");
            if (pairClass == IntPtr.Zero)
            {
                pairClass = this.FindAuraMonoClassAcrossLoadedAssemblies("EcsClient.XDT.Scene.Shared.Modules.ItemExchange", "BirdPhotoExchangeData");
            }

            if (pairClass == IntPtr.Zero)
            {
                return false;
            }

            netIdField = this.FindAuraMonoFieldOnHierarchy(pairClass, "itemNetId");
            if (netIdField == IntPtr.Zero)
            {
                netIdField = this.FindAuraMonoFieldOnHierarchy(pairClass, "ItemNetId");
            }

            numField = this.FindAuraMonoFieldOnHierarchy(pairClass, "num");
            if (numField == IntPtr.Zero)
            {
                numField = this.FindAuraMonoFieldOnHierarchy(pairClass, "Num");
            }

            if (netIdField == IntPtr.Zero || numField == IntPtr.Zero)
            {
                return false;
            }

            this.birdPhotoExchangeDataClass = pairClass;
            this.birdPhotoExchangeItemNetIdField = netIdField;
            this.birdPhotoExchangeNumField = numField;
            return true;
        }


        private void BirdPhotoSubmitLog(string message)
        {
            if (!BirdPhotoSubmitLogsEnabled || string.IsNullOrEmpty(message))
            {
                return;
            }

            ModLogger.Msg("[BirdPhotoSubmit] " + message);
        }

        private struct BirdPhotoSubmitPair
        {
            public uint ItemNetId;
            public int Num;
        }
    }
}
