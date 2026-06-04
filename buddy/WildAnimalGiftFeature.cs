using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const bool WildAnimalGiftLogsEnabled = MasterLogWildAnimalGift;
        private const float WildAnimalGiftActionCooldownSeconds = 1.25f;
        private const float WildAnimalGiftDelayBetweenTakesSeconds = 0.45f;
        private const int WildAnimalGiftLevelScanChunkSize = 24;

        private object wildAnimalGiftCoroutine = null;
        private float wildAnimalGiftBusyUntil = 0f;
        private string wildAnimalGiftLastStatus = "Idle.";

        private IntPtr wildAnimalGiftAuraTakeGiftMethod = IntPtr.Zero;
        private IntPtr wildAnimalGiftAuraHaveGiftGroupsMethod = IntPtr.Zero;

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

            if (!this.TryGetWildAnimalHaveGiftCountAuraMono(out int giftCount, out string groupNote) || giftCount <= 0)
            {
                this.WildAnimalGiftLog("HaveGift: " + groupNote);
                complete?.Invoke("no wild gifts available");
                yield break;
            }

            this.WildAnimalGiftLog("HaveGift: " + groupNote);

            yield return null;

            HashSet<uint> seen = new HashSet<uint>();
            IEnumerator levelOwnersRoutine = this.CollectWildAnimalGiftNetIdsFromLevelOwnersRoutine(seen, netIds, giftCount);
            while (levelOwnersRoutine.MoveNext())
            {
                yield return levelOwnersRoutine.Current;
            }

            string status = netIds.Count > 0
                ? netIds.Count + " target(s), pending=" + giftCount
                : "no claimable wild gifts found, pending=" + giftCount;
            complete?.Invoke(status);
        }

        private unsafe bool TryGetWildAnimalHaveGiftCountAuraMono(out int giftCount, out string status)
        {
            giftCount = 0;
            status = "unavailable";

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono API unavailable";
                return false;
            }

            if (this.wildAnimalGiftAuraHaveGiftGroupsMethod == IntPtr.Zero)
            {
                IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.WildAnimal.WildAnimalProtocolManager");
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTDataAndProtocol.ProtocolService.WildAnimal",
                        "WildAnimalProtocolManager");
                }

                if (protocolClass == IntPtr.Zero)
                {
                    status = "WildAnimalProtocolManager missing";
                    return false;
                }

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
                if (items[i] != IntPtr.Zero)
                {
                    giftCount++;
                }
            }

            status = "count=" + giftCount;
            return true;
        }

        private IEnumerator CollectWildAnimalGiftNetIdsFromLevelOwnersRoutine(HashSet<uint> seen, List<uint> netIds, int targetCount)
        {
            if (seen == null || netIds == null || targetCount <= 0)
            {
                yield break;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                yield break;
            }

            if (!this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out _, out _))
            {
                yield break;
            }

            IntPtr dictionaryObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(managerObj, "_dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(managerObj, "dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero))
            {
                yield break;
            }

            List<IntPtr> entries = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(dictionaryObj, entries) || entries.Count <= 0)
            {
                yield break;
            }

            int processed = 0;
            for (int i = 0; i < entries.Count && netIds.Count < targetCount; i++)
            {
                processed++;
                IntPtr entryObj = entries[i];
                if (entryObj == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr levelObjectObj = IntPtr.Zero;
                if ((!this.TryGetMonoObjectMember(entryObj, "Value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entryObj, "value", out levelObjectObj) || levelObjectObj == IntPtr.Zero))
                {
                    levelObjectObj = entryObj;
                }

                if (levelObjectObj == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryGetMonoBoolMember(levelObjectObj, "isActive", out bool isActive) && !isActive)
                {
                    continue;
                }

                ulong levelObjectNetId = 0UL;
                if (!this.TryGetMonoUInt64Member(levelObjectObj, "netId", out levelObjectNetId) || levelObjectNetId == 0UL)
                {
                    if (!this.TryGetMonoUInt64Member(entryObj, "Key", out levelObjectNetId))
                    {
                        continue;
                    }
                }

                uint ownerNetId = 0U;
                if (!this.TryResolveOwnerIdFromLevelObjectIdMono(levelObjectNetId, out ownerNetId) || ownerNetId == 0U)
                {
                    if (!this.TryInvokeAuraMonoZeroArg(levelObjectObj, out IntPtr boxedOwner, "get_ownerNetId")
                        || !this.TryUnboxMonoUInt32(boxedOwner, out ownerNetId)
                        || ownerNetId == 0U)
                    {
                        if (levelObjectNetId <= uint.MaxValue)
                        {
                            ownerNetId = (uint)levelObjectNetId;
                        }
                        else
                        {
                            continue;
                        }
                    }
                }

                if (!this.TryGetAuraMonoEntityObjectByNetId(ownerNetId, out IntPtr entityObj) || entityObj == IntPtr.Zero)
                {
                    continue;
                }

                if (!this.TryEntityHasWildAnimalComponentAuraMono(entityObj))
                {
                    continue;
                }

                if (this.TryAddWildAnimalGiftNetId(ownerNetId, seen, netIds))
                {
                    this.WildAnimalGiftLog("Level owner netId=" + ownerNetId);
                }

                if (processed % WildAnimalGiftLevelScanChunkSize == 0)
                {
                    yield return null;
                }
            }
        }

        private bool TryEntityHasWildAnimalComponentAuraMono(IntPtr entityObj)
        {
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr entityClass = auraMonoObjectGetClass(entityObj);
            IntPtr getAllComponentsMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "GetAllComponents", 0);
            if (getAllComponentsMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr componentsObj = auraMonoRuntimeInvoke(getAllComponentsMethod, entityObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || componentsObj == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components))
            {
                return false;
            }

            for (int i = 0; i < components.Count && i < 128; i++)
            {
                IntPtr componentObj = components[i];
                if (componentObj == IntPtr.Zero)
                {
                    continue;
                }

                string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(componentObj));
                if (IsWildAnimalGameplayComponentClassName(className))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWildAnimalGameplayComponentClassName(string className)
        {
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }

            if (className.IndexOf("FeedTrough", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return className.IndexOf("WildAnimal.WildAnimalComponent", StringComparison.OrdinalIgnoreCase) >= 0
                || className.EndsWith(".WildAnimalComponent", StringComparison.Ordinal);
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

        private float DrawWildAnimalGiftSection(float startY)
        {
            float num = startY;
            const float left = 40f;
            const float width = 520f;

            Color textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            labelStyle.normal.textColor = textColor;

            Rect actionRect = new Rect(left, num, width, 74f);
            GUI.Box(actionRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(actionRect, 1f);
            GUI.Label(new Rect(actionRect.x + 16f, actionRect.y + 12f, 240f, 20f), "WILD ANIMAL GIFTS", labelStyle);

            bool busy = this.wildAnimalGiftCoroutine != null || Time.realtimeSinceStartup < this.wildAnimalGiftBusyUntil;
            GUI.enabled = !busy;
            if (GUI.Button(new Rect(actionRect.x + 16f, actionRect.y + 34f, 220f, 32f), this.L("Claim All Wild Gifts"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartWildAnimalClaimAllGifts(silent: false);
            }

            GUI.enabled = true;

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            statusStyle.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, 0.82f);
            num += 84f;
            GUI.Label(new Rect(left, num, width, 36f), this.wildAnimalGiftLastStatus ?? string.Empty, statusStyle);
            num += 44f;
            return num;
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
