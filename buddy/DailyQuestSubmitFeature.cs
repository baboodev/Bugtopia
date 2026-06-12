using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const bool DailyQuestSubmitLogsEnabled = MasterLogDailyQuestSubmit;
        private const float DailyQuestSubmitDelaySeconds = 0.65f;
        private const int DailyQuestSubmitStateCanAccept = 2;
        private const int DailyQuestSubmitStateCanSubmit = 4;
        private const int DailyQuestBackpackStorageType = 1;
        private const int DailyQuestWarehouseStorageType = 2;
        private const int DailyQuestRefreshTypeDaily = 2;
        // TableTaskOrder.specialId of the "daily personal goals" group on the daily board
        // (DailyOrderPanel splits specialId==1 into its own section; DailyOrderSystem.GetOrderType
        // maps 1 -> Story). Accepted first in the auto-submit accept phase.
        private const int DailyQuestOrderSpecialIdPersonalGoal = 1;
        private static readonly int[] DailyQuestSubmitStorageTypes = { DailyQuestBackpackStorageType, DailyQuestWarehouseStorageType };

        private object dailyQuestSubmitCoroutine = null;
        private string dailyQuestSubmitLastStatus = "Idle.";

        private IntPtr dailyQuestSubmitItemNetPairClass = IntPtr.Zero;
        private IntPtr dailyQuestSubmitItemNetPairNetIdField = IntPtr.Zero;
        private IntPtr dailyQuestSubmitItemNetPairCountField = IntPtr.Zero;
        private IntPtr dailyQuestSubmitItemNetPairListClass = IntPtr.Zero;
        private IntPtr dailyQuestSubmitAuraItemNetPairListAddMethod = IntPtr.Zero;
        private IntPtr dailyQuestSubmitClientSubmitMethod = IntPtr.Zero;
        private IntPtr dailyQuestSubmitClientSubmitNpcMethod = IntPtr.Zero;
        private IntPtr dailyQuestSubmitClientAcceptMethod = IntPtr.Zero;
        private int dailyQuestSubmitClientSubmitTaskItemParamCount = 0;

        private void StartDailyQuestAutoSubmitItems(bool silent)
        {
            if (this.dailyQuestSubmitCoroutine != null || this.birdPhotoSubmitCoroutine != null || this.dailyClaimsCoroutine != null)
            {
                if (!silent)
                {
                    this.AddMenuNotification("Submit already running", new Color(0.45f, 0.88f, 1f));
                }

                return;
            }

            this.DailyQuestSubmitLog(
                "=== Auto Submit Daily Items started"
                + " skip5star=" + this.dailyQuestSubmitSkipFiveStar
                + " silent=" + silent
                + " ===");
            this.dailyQuestSubmitLastStatus = "Submitting daily item orders...";
            this.dailyQuestSubmitCoroutine = ModCoroutines.Start(this.DailyQuestAutoSubmitItemsRoutine(silent));
        }

        private IEnumerator DailyQuestAutoSubmitItemsRoutine(bool silent)
        {
            yield return null;

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                this.dailyQuestSubmitLastStatus = "AuraMono unavailable";
                if (!silent)
                {
                    this.AddMenuNotification(this.dailyQuestSubmitLastStatus, new Color(1f, 0.55f, 0.45f));
                }

                this.dailyQuestSubmitCoroutine = null;
                yield break;
            }

            if (!this.TryCollectDailyQuestOrdersForSubmit(out List<IntPtr> orders, out _, out string prepStatus))
            {
                this.dailyQuestSubmitLastStatus = prepStatus;
                this.DailyQuestSubmitLog("collect orders failed: " + prepStatus);
                if (!silent)
                {
                    this.AddMenuNotification("Daily submit: " + prepStatus, new Color(1f, 0.55f, 0.45f));
                }

                this.dailyQuestSubmitCoroutine = null;
                yield break;
            }

            // Resolve every order's task id / order key up front, with the mono GC suspended, so we
            // never touch the raw game-object pointers in `orders` again. Holding those pointers across
            // the WaitForSeconds yields below would let the GC collect/move them mid-run, which crashed
            // the game with a native AV partway through the order list (same class of bug as the
            // homeland sow-all native AV that the GC-disable guard already fixes).
            List<DailyQuestResolvedOrder> resolved = this.ResolveDailyQuestOrdersForSubmit(orders);

            // Accept daily personal goals (the specialId == 1 group on the daily board) before
            // everything else: actions the rest of this routine performs (accepting/submitting
            // NPC orders) only count toward a goal's progress once that goal task is accepted.
            // GetOrders sorts by specialId DESCENDING, so without this reorder the goals would
            // be accepted nearly last.
            List<int> acceptOrderIndices = new List<int>(resolved.Count);
            List<int> acceptOrderIndicesTail = new List<int>(resolved.Count);
            for (int i = 0; i < resolved.Count; i++)
            {
                int orderSpecialIdForAccept = 0;
                if (resolved[i].Resolved)
                {
                    this.TryResolveDailyQuestTaskFromOrderKey(resolved[i].OrderKey, out _, out _, out orderSpecialIdForAccept, out _, out _);
                }

                if (orderSpecialIdForAccept == DailyQuestOrderSpecialIdPersonalGoal)
                {
                    acceptOrderIndices.Add(i);
                }
                else
                {
                    acceptOrderIndicesTail.Add(i);
                }
            }
            if (acceptOrderIndices.Count > 0)
            {
                this.DailyQuestSubmitLog("accept phase: " + acceptOrderIndices.Count + " personal goal(s) first, then " + acceptOrderIndicesTail.Count + " other order(s)");
            }
            acceptOrderIndices.AddRange(acceptOrderIndicesTail);

            int accepted = 0;
            for (int acceptPass = 0; acceptPass < acceptOrderIndices.Count; acceptPass++)
            {
                int i = acceptOrderIndices[acceptPass];
                DailyQuestResolvedOrder acceptEntry = resolved[i];
                int acceptTaskId = acceptEntry.TaskId;
                int acceptOrderKey = acceptEntry.OrderKey;
                string acceptOrderFields = acceptEntry.Fields;
                if (!acceptEntry.Resolved || acceptTaskId <= 0)
                {
                    this.DailyQuestSubmitLog(
                        "accept order[" + i + "] skip: unresolved"
                        + (string.IsNullOrEmpty(acceptOrderFields) ? string.Empty : " | " + acceptOrderFields));
                    continue;
                }

                if (!this.TryGetGameTaskStateAura(acceptTaskId, out int acceptState, out string acceptStateStatus))
                {
                    this.DailyQuestSubmitLog("accept order[" + i + "] taskId=" + acceptTaskId + " GetTaskState failed: " + acceptStateStatus);
                    continue;
                }

                if (acceptState != DailyQuestSubmitStateCanAccept)
                {
                    continue;
                }

                this.DailyQuestSubmitLog(
                    "accept order[" + i + "] orderKey=" + acceptOrderKey
                    + " taskId=" + acceptTaskId
                    + " state=CanAccept"
                    + " | " + acceptOrderFields);
                if (this.TryInvokeDailyQuestClientAcceptTaskAura(acceptTaskId, out string acceptStatus))
                {
                    accepted++;
                    this.DailyQuestSubmitLog("accept ok taskId=" + acceptTaskId + " " + acceptStatus);
                }
                else
                {
                    this.DailyQuestSubmitLog("accept failed taskId=" + acceptTaskId + ": " + acceptStatus);
                }

                yield return new WaitForSecondsRealtime(DailyQuestSubmitDelaySeconds * 0.35f);
            }

            if (accepted > 0)
            {
                this.DailyQuestSubmitLog("accepted " + accepted + " CanAccept order(s), waiting for server sync");
                yield return new WaitForSecondsRealtime(DailyQuestSubmitDelaySeconds);
            }

            int attempted = 0;
            int submitted = 0;
            int skipped = 0;

            for (int i = 0; i < resolved.Count; i++)
            {
                DailyQuestResolvedOrder entry = resolved[i];
                int taskId = entry.TaskId;
                int orderKey = entry.OrderKey;
                string orderFields = entry.Fields;
                if (!entry.Resolved || taskId <= 0)
                {
                    this.DailyQuestSubmitLog(
                        "process order[" + i + "] skip: invalid TaskOrderId"
                        + (string.IsNullOrEmpty(orderFields) ? string.Empty : " | " + orderFields));
                    skipped++;
                    continue;
                }

                this.TryResolveDailyQuestTaskFromOrderKey(
                    orderKey,
                    out _,
                    out int orderRefreshType,
                    out int orderSpecialId,
                    out int orderLevel,
                    out string resolveNote);

                this.DailyQuestSubmitLog(
                    "process order[" + i + "] orderKey=" + orderKey
                    + " taskId=" + taskId
                    + " resolve=" + resolveNote
                    + " refreshType=" + orderRefreshType
                    + " specialId=" + orderSpecialId
                    + " level=" + orderLevel
                    + " | " + orderFields);

                if (!this.TryGetGameTaskStateAura(taskId, out int state, out string stateStatus))
                {
                    this.DailyQuestSubmitLog("skip taskId=" + taskId + " GetTaskState failed: " + stateStatus);
                    skipped++;
                    continue;
                }

                if (state == DailyQuestSubmitStateCanAccept)
                {
                    this.DailyQuestSubmitLog("retry accept taskId=" + taskId + " still CanAccept before submit");
                    if (this.TryInvokeDailyQuestClientAcceptTaskAura(taskId, out string retryAcceptStatus))
                    {
                        accepted++;
                        this.DailyQuestSubmitLog("accept ok taskId=" + taskId + " " + retryAcceptStatus);
                        yield return new WaitForSecondsRealtime(DailyQuestSubmitDelaySeconds);
                        if (!this.TryGetGameTaskStateAura(taskId, out state, out stateStatus))
                        {
                            this.DailyQuestSubmitLog("skip taskId=" + taskId + " GetTaskState failed after accept: " + stateStatus);
                            skipped++;
                            continue;
                        }
                    }
                    else
                    {
                        this.DailyQuestSubmitLog("accept failed taskId=" + taskId + ": " + retryAcceptStatus);
                        skipped++;
                        continue;
                    }
                }

                if (state != DailyQuestSubmitStateCanSubmit)
                {
                    this.DailyQuestSubmitLog(
                        "skip taskId=" + taskId
                        + " state=" + this.FormatGameTaskState(state)
                        + " (need CanSubmit="
                        + DailyQuestSubmitStateCanSubmit
                        + ")");
                    skipped++;
                    continue;
                }

                if (!this.TryGetTableGameTaskRowAura(taskId, out DailyQuestGameTaskInfo info, out string configNote)
                    || info.SubmitTargetCount <= 0)
                {
                    this.DailyQuestSubmitLog(
                        "skip taskId=" + taskId
                        + " no submitTargetItem targets="
                        + info.SubmitTargetCount
                        + " (" + configNote + ")");
                    skipped++;
                    continue;
                }

                this.DailyQuestSubmitLog(
                    "attempt taskId=" + taskId
                    + " submitNpc=" + info.SubmitNpc
                    + " submitType=" + info.SubmitType
                    + " submitParam=" + info.SubmitParam
                    + " targets=" + info.SubmitTargetCount);
                this.DailyQuestSubmitLogSubmitTargets(taskId);

                attempted++;
                string submitStatus;
                bool sent = this.TrySubmitDailyQuestCheapestItemsAura(
                    taskId,
                    info.SubmitNpc,
                    info.SubmitType,
                    info.SubmitParam,
                    out submitStatus);
                yield return new WaitForSecondsRealtime(DailyQuestSubmitDelaySeconds);

                if (sent && this.TryGetGameTaskStateAura(taskId, out int stateAfter, out _)
                    && stateAfter == DailyQuestSubmitStateCanSubmit)
                {
                    sent = false;
                    submitStatus += "; still CanSubmit after delay";
                }

                if (sent)
                {
                    submitted++;
                    this.DailyQuestSubmitLog("Submit ok taskId=" + taskId + " npc=" + info.SubmitNpc + " " + submitStatus);
                }
                else
                {
                    this.DailyQuestSubmitLog("Submit failed taskId=" + taskId + ": " + submitStatus);
                }

                yield return new WaitForSecondsRealtime(DailyQuestSubmitDelaySeconds * 0.5f);
            }

            this.dailyQuestSubmitLastStatus = "Done: " + submitted + "/" + attempted
                + " submitted, " + accepted + " accepted, " + skipped + " skipped";
            this.DailyQuestSubmitLog(this.dailyQuestSubmitLastStatus);
            if (!silent || submitted > 0)
            {
                this.AddMenuNotification(
                    "Daily items: " + submitted + " submitted",
                    submitted > 0 ? new Color(0.45f, 1f, 0.55f) : new Color(0.45f, 0.88f, 1f));
            }

            this.dailyQuestSubmitCoroutine = null;
        }

        // Reads the task id / order key off every raw order pointer in one synchronous pass with the
        // mono GC suspended, capturing them into managed value types. After this the coroutine drives
        // its loops from ints only and never dereferences the raw `orders` pointers across a yield,
        // where the GC could otherwise collect/move them (native AV mid-order, see caller comment).
        private List<DailyQuestResolvedOrder> ResolveDailyQuestOrdersForSubmit(List<IntPtr> orders)
        {
            List<DailyQuestResolvedOrder> resolved = new List<DailyQuestResolvedOrder>(orders != null ? orders.Count : 0);
            if (orders == null)
            {
                return resolved;
            }

            this.DailyQuestSubmitAuraGcDisable();
            try
            {
                this.DailyQuestSubmitLogOrderSnapshot(orders);

                for (int i = 0; i < orders.Count; i++)
                {
                    DailyQuestResolvedOrder entry = default(DailyQuestResolvedOrder);
                    entry.Resolved = this.TryResolveDailyQuestTaskIdFromOrder(
                        orders[i],
                        out int taskId,
                        out int orderKey,
                        out string fields);
                    entry.TaskId = taskId;
                    entry.OrderKey = orderKey;
                    entry.Fields = fields;
                    resolved.Add(entry);
                }
            }
            finally
            {
                this.DailyQuestSubmitAuraGcEnable();
            }

            return resolved;
        }

        // Suspend/resume the mono GC so raw order pointers held during the synchronous resolve pass are
        // not collected/moved mid-sequence. Paired in a finally. No-op if the export is unavailable.
        private void DailyQuestSubmitAuraGcDisable()
        {
            try
            {
                auraMonoGcDisable?.Invoke();
            }
            catch
            {
            }
        }

        private void DailyQuestSubmitAuraGcEnable()
        {
            try
            {
                auraMonoGcEnable?.Invoke();
            }
            catch
            {
            }
        }

        private bool TrySubmitDailyQuestCheapestItemsAura(
            int taskId,
            int submitNpc,
            int submitType,
            int submitParam,
            out string status)
        {
            status = string.Empty;
            if (!this.TryBuildDailyQuestCheapestSubmitPairsAura(taskId, out List<DailyQuestSubmitNetPair> pairs, out string buildStatus))
            {
                status = "build: " + buildStatus;
                return false;
            }

            if (!this.TrySendDailyQuestNpcSubmitViaGameAuraMono(taskId, submitNpc, submitType, submitParam, pairs, out string sendStatus))
            {
                status = buildStatus + "; " + sendStatus;
                return false;
            }

            status = buildStatus + "; " + sendStatus;
            return true;
        }

        private bool TrySendDailyQuestNpcSubmitViaGameAuraMono(
            int taskId,
            int submitNpc,
            int submitType,
            int submitParam,
            List<DailyQuestSubmitNetPair> pairs,
            out string status)
        {
            status = string.Empty;
            string validateStatus = string.Empty;
            if (taskId <= 0 || pairs == null || pairs.Count == 0)
            {
                status = "aura mono invalid args";
                return false;
            }

            this.DailyQuestMergeSubmitPairsByNetId(pairs);

            if (!this.TryGetDailyQuestGameTaskRowPtrAura(taskId, out IntPtr gameTaskRow, out string rowStatus)
                || !this.TryGetDailyQuestSubmitTargetsAura(gameTaskRow, out IntPtr submitTargetsArray, out List<IntPtr> targets)
                || !this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj)
                || backPackSystemObj == IntPtr.Zero)
            {
                status = "pre-submit row/backpack: " + rowStatus;
                return false;
            }

            IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
            IntPtr checkSubmitMethod = backPackClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(backPackClass, "CheckSubmitItem", 2)
                : IntPtr.Zero;
            IntPtr checkSubmitItemsMethod = backPackClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(backPackClass, "CheckSubmitItems", 2)
                : IntPtr.Zero;
            if (checkSubmitMethod == IntPtr.Zero
                || !this.TryValidateDailyQuestSubmitPairsAura(
                    taskId,
                    gameTaskRow,
                    checkSubmitMethod,
                    checkSubmitItemsMethod,
                    backPackSystemObj,
                    submitTargetsArray,
                    targets,
                    pairs,
                    out validateStatus))
            {
                status = validateStatus;
                return false;
            }

            if (!this.TryCreateDailyQuestItemNetPairListAuraMonoNative(pairs, out IntPtr listObj, out string listStatus))
            {
                status = validateStatus + "; " + listStatus;
                return false;
            }

            string taskItemStatus = string.Empty;
            if (this.TryInvokeDailyQuestClientSubmitTaskItemAura(taskId, submitType, submitParam, listObj, out taskItemStatus))
            {
                status = listStatus + "; " + taskItemStatus;
                return true;
            }

            if (submitNpc <= 0)
            {
                status = validateStatus + "; " + listStatus + "; taskItem=" + taskItemStatus + " (no submitNpc)";
                return false;
            }

            if (this.TryInvokeDailyQuestClientSubmitNpcAura(taskId, submitNpc, listObj, out string npcStatus))
            {
                status = listStatus + "; " + npcStatus;
                return true;
            }

            status = validateStatus + "; " + listStatus + "; taskItem=" + taskItemStatus + "; npc=" + npcStatus;
            return false;
        }






        private bool TryBuildDailyQuestCheapestSubmitPairsAura(int taskId, out List<DailyQuestSubmitNetPair> pairs, out string status)
        {
            return this.TryBuildDailyQuestCheapestSubmitPairsAuraMono(taskId, out pairs, out status);
        }

        private bool TryBuildDailyQuestCheapestSubmitPairsAuraMono(int taskId, out List<DailyQuestSubmitNetPair> pairs, out string status)
        {
            pairs = new List<DailyQuestSubmitNetPair>();
            status = string.Empty;

            if (!this.TryGetDailyQuestGameTaskRowPtrAura(taskId, out IntPtr gameTaskRow, out string rowStatus))
            {
                status = rowStatus;
                return false;
            }

            if (!this.TryGetDailyQuestSubmitTargetsAura(gameTaskRow, out IntPtr submitTargetsArray, out List<IntPtr> targets) || targets.Count == 0)
            {
                status = "no submit targets";
                return false;
            }

            int tableSubmitWay = this.TryGetMonoIntMember(gameTaskRow, "submitWay", out int way) ? way : 0;

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj) || backPackSystemObj == IntPtr.Zero)
            {
                status = "BackPackSystem unavailable";
                return false;
            }

            if (!this.TryEnumerateDailyQuestStorageItemsAura(out List<IntPtr> storageItems, out string itemsStatus))
            {
                status = itemsStatus;
                return false;
            }

            IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
            IntPtr checkSubmitMethod = backPackClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(backPackClass, "CheckSubmitItem", 2)
                : IntPtr.Zero;
            IntPtr checkSubmitItemsMethod = backPackClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(backPackClass, "CheckSubmitItems", 2)
                : IntPtr.Zero;
            if (checkSubmitMethod == IntPtr.Zero)
            {
                status = "CheckSubmitItem missing";
                return false;
            }

            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                IntPtr targetItem = targets[targetIndex];
                if (targetItem == IntPtr.Zero)
                {
                    status = "null submit target";
                    return false;
                }

                int needNum = this.TryGetMonoIntMember(targetItem, "needNum", out int need) ? need : 0;
                int targetQuality = this.TryGetMonoIntMember(targetItem, "quality", out int quality) ? quality : 0;
                if (needNum <= 0)
                {
                    status = "invalid needNum";
                    return false;
                }

                List<DailyQuestSubmitCandidate> candidates = new List<DailyQuestSubmitCandidate>();
                for (int itemIndex = 0; itemIndex < storageItems.Count; itemIndex++)
                {
                    IntPtr itemObj = storageItems[itemIndex];
                    if (itemObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetDailyQuestItemSubmitSortKey(itemObj, out uint netId, out int count, out int price, out int starRate)
                        || netId == 0U
                        || count <= 0)
                    {
                        continue;
                    }

                    if (this.TryGetDirectBackpackItemIsLocked(itemObj, out bool isLocked) && isLocked)
                    {
                        continue;
                    }

                    if (this.ShouldSkipDailyQuestSubmitItem(starRate))
                    {
                        continue;
                    }

                    if (checkSubmitItemsMethod != IntPtr.Zero && submitTargetsArray != IntPtr.Zero
                        && (!this.TryInvokeBackPackCheckSubmitItemsAura(
                                checkSubmitItemsMethod,
                                backPackSystemObj,
                                submitTargetsArray,
                                netId,
                                out bool matchesAll)
                            || !matchesAll))
                    {
                        continue;
                    }

                    if (!this.TryInvokeBackPackCheckSubmitItemAura(checkSubmitMethod, backPackSystemObj, targetItem, netId, out bool matches)
                        || !matches)
                    {
                        continue;
                    }

                    candidates.Add(new DailyQuestSubmitCandidate
                    {
                        NetId = netId,
                        Count = count,
                        Price = price,
                        StarRate = starRate
                    });
                }

                if (candidates.Count == 0)
                {
                    this.LogDailyQuestTargetCandidateDiagnostics(
                        taskId,
                        targetIndex,
                        targetItem,
                        targetQuality,
                        submitTargetsArray,
                        checkSubmitMethod,
                        checkSubmitItemsMethod,
                        backPackSystemObj,
                        storageItems);
                    status = "no matching items for target " + (targetIndex + 1);
                    return false;
                }

                candidates.Sort(this.CompareDailyQuestSubmitCandidates);

                int remaining = needNum;
                for (int candidateIndex = 0; candidateIndex < candidates.Count && remaining > 0; candidateIndex++)
                {
                    DailyQuestSubmitCandidate candidate = candidates[candidateIndex];
                    if (candidate.Count <= 0)
                    {
                        continue;
                    }

                    int take = candidate.Count < remaining ? candidate.Count : remaining;
                    pairs.Add(new DailyQuestSubmitNetPair { NetId = candidate.NetId, Count = take });
                    this.DailyQuestSubmitLog(
                        "pick netId=" + candidate.NetId
                        + " x" + take
                        + " price=" + candidate.Price
                        + " star=" + candidate.StarRate
                        + " taskId=" + taskId
                        + " target=" + (targetIndex + 1)
                        + " targetQuality=" + targetQuality
                        + " submitWay=" + tableSubmitWay);

                    remaining -= take;
                    if (remaining <= 0)
                    {
                        break;
                    }
                }

                if (remaining > 0)
                {
                    status = "not enough items for target " + (targetIndex + 1) + " (short " + remaining + ")";
                    return false;
                }
            }

            this.DailyQuestMergeSubmitPairsByNetId(pairs);
            status = "pairs=" + pairs.Count;
            return pairs.Count > 0;
        }

        private void DailyQuestMergeSubmitPairsByNetId(List<DailyQuestSubmitNetPair> pairs)
        {
            if (pairs == null || pairs.Count <= 1)
            {
                return;
            }

            Dictionary<uint, int> merged = new Dictionary<uint, int>();
            for (int i = 0; i < pairs.Count; i++)
            {
                uint netId = pairs[i].NetId;
                int count = pairs[i].Count;
                if (netId == 0U || count <= 0)
                {
                    continue;
                }

                if (merged.ContainsKey(netId))
                {
                    merged[netId] += count;
                }
                else
                {
                    merged[netId] = count;
                }
            }

            if (merged.Count == pairs.Count)
            {
                return;
            }

            pairs.Clear();
            foreach (KeyValuePair<uint, int> entry in merged)
            {
                pairs.Add(new DailyQuestSubmitNetPair { NetId = entry.Key, Count = entry.Value });
            }
        }

        private int CompareDailyQuestSubmitCandidates(DailyQuestSubmitCandidate left, DailyQuestSubmitCandidate right)
        {
            int priceCompare = left.Price.CompareTo(right.Price);
            if (priceCompare != 0)
            {
                return priceCompare;
            }

            return left.StarRate.CompareTo(right.StarRate);
        }

        private bool TryGetDailyQuestItemSubmitSortKey(IntPtr itemObj, out uint netId, out int count, out int price, out int starRate)
        {
            netId = 0U;
            count = 0;
            price = int.MaxValue;
            starRate = int.MaxValue;

            if (!this.TryGetDirectBackpackItemNetId(itemObj, out netId) || netId == 0U)
            {
                return false;
            }

            if (!this.TryGetDirectBackpackItemCount(itemObj, out count) || count <= 0)
            {
                return false;
            }

            if (this.TryInvokeAuraMonoZeroArgInt(itemObj, out int itemPrice, "GetItemPrice", "get_GetItemPrice"))
            {
                price = itemPrice;
            }
            else if (this.TryGetMonoInt32Member(itemObj, "staticId", out int staticId) && staticId > 0
                && this.TryGetDailyQuestQuickSalePriceAura(staticId, out int tablePrice))
            {
                price = tablePrice;
            }

            if (!this.TryGetDirectBackpackItemStarRate(itemObj, out starRate))
            {
                starRate = 0;
            }

            starRate = this.NormalizeAutoSellStarRate(starRate);
            return true;
        }

        private unsafe bool TryInvokeBackPackCheckSubmitItemAura(
            IntPtr checkSubmitMethod,
            IntPtr backPackSystemObj,
            IntPtr targetItem,
            uint netId,
            out bool matches)
        {
            matches = false;
            if (checkSubmitMethod == IntPtr.Zero || backPackSystemObj == IntPtr.Zero || targetItem == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            uint localNetId = netId;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&localNetId);
            args[1] = targetItem;
            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(checkSubmitMethod, backPackSystemObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || result == IntPtr.Zero || !this.TryUnboxMonoBoolean(result, out matches))
            {
                return false;
            }

            return true;
        }

        private unsafe bool TryInvokeBackPackCheckSubmitItemsAura(
            IntPtr checkSubmitItemsMethod,
            IntPtr backPackSystemObj,
            IntPtr submitTargetsArray,
            uint netId,
            out bool matches)
        {
            matches = false;
            if (checkSubmitItemsMethod == IntPtr.Zero
                || backPackSystemObj == IntPtr.Zero
                || submitTargetsArray == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            uint localNetId = netId;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&localNetId);
            args[1] = submitTargetsArray;
            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(checkSubmitItemsMethod, backPackSystemObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || result == IntPtr.Zero || !this.TryUnboxMonoBoolean(result, out matches))
            {
                return false;
            }

            return true;
        }

        private bool TryValidateDailyQuestSubmitPairsAura(
            int taskId,
            IntPtr gameTaskRow,
            IntPtr checkSubmitMethod,
            IntPtr checkSubmitItemsMethod,
            IntPtr backPackSystemObj,
            IntPtr submitTargetsArray,
            List<IntPtr> targets,
            List<DailyQuestSubmitNetPair> pairs,
            out string status)
        {
            status = string.Empty;
            if (pairs == null || pairs.Count == 0 || targets == null || targets.Count == 0)
            {
                status = "validate: empty";
                return false;
            }

            for (int i = 0; i < pairs.Count; i++)
            {
                uint netId = pairs[i].NetId;
                if (netId == 0U || pairs[i].Count <= 0)
                {
                    status = "validate: invalid pair";
                    return false;
                }

                if (checkSubmitItemsMethod != IntPtr.Zero && submitTargetsArray != IntPtr.Zero
                    && (!this.TryInvokeBackPackCheckSubmitItemsAura(
                            checkSubmitItemsMethod,
                            backPackSystemObj,
                            submitTargetsArray,
                            netId,
                            out bool allTargets)
                        || !allTargets))
                {
                    status = "validate: CheckSubmitItems failed netId=" + netId;
                    return false;
                }

                bool matchesAnyTarget = false;
                for (int t = 0; t < targets.Count; t++)
                {
                    if (targets[t] == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.TryInvokeBackPackCheckSubmitItemAura(checkSubmitMethod, backPackSystemObj, targets[t], netId, out bool oneTarget)
                        && oneTarget)
                    {
                        matchesAnyTarget = true;
                        break;
                    }
                }

                if (!matchesAnyTarget)
                {
                    status = "validate: CheckSubmitItem failed netId=" + netId;
                    return false;
                }
            }

            status = "validate ok taskId=" + taskId + " pairs=" + pairs.Count;
            return true;
        }

        private unsafe bool TryEnumerateDailyQuestStorageItemsAura(out List<IntPtr> items, out string status)
        {
            items = new List<IntPtr>();
            status = string.Empty;
            HashSet<uint> seenNetIds = new HashSet<uint>();

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj) || backPackSystemObj == IntPtr.Zero)
            {
                status = "BackPackSystem unavailable";
                return false;
            }

            IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
            IntPtr getAllItemMethod = backPackClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1)
                : IntPtr.Zero;
            bool needsStorage = true;
            if (getAllItemMethod == IntPtr.Zero && backPackClass != IntPtr.Zero)
            {
                getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                needsStorage = false;
            }

            if (getAllItemMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                status = "GetAllItem missing";
                return false;
            }

            int backpackCount = 0;
            int warehouseCount = 0;
            if (needsStorage)
            {
                for (int storageIndex = 0; storageIndex < DailyQuestSubmitStorageTypes.Length; storageIndex++)
                {
                    int storageType = DailyQuestSubmitStorageTypes[storageIndex];
                    if (!this.TryAppendDailyQuestStorageItemsAura(
                            getAllItemMethod,
                            backPackSystemObj,
                            storageType,
                            items,
                            seenNetIds,
                            out int appended))
                    {
                        continue;
                    }

                    if (storageType == DailyQuestBackpackStorageType)
                    {
                        backpackCount = appended;
                    }
                    else if (storageType == DailyQuestWarehouseStorageType)
                    {
                        warehouseCount = appended;
                    }
                }
            }
            else
            {
                if (!this.TryAppendDailyQuestStorageItemsAura(getAllItemMethod, backPackSystemObj, 0, items, seenNetIds, out backpackCount))
                {
                    status = "backpack read failed";
                    return false;
                }
            }

            if (items.Count == 0)
            {
                status = "backpack+warehouse empty";
                return false;
            }

            status = "items=" + items.Count + " backpack=" + backpackCount + " warehouse=" + warehouseCount;
            return true;
        }

        private unsafe bool TryAppendDailyQuestStorageItemsAura(
            IntPtr getAllItemMethod,
            IntPtr backPackSystemObj,
            int storageType,
            List<IntPtr> items,
            HashSet<uint> seenNetIds,
            out int appended)
        {
            appended = 0;
            if (getAllItemMethod == IntPtr.Zero || backPackSystemObj == IntPtr.Zero || items == null || seenNetIds == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr itemListObj;
            if (storageType != 0)
            {
                int localStorageType = storageType;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&localStorageType);
                itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, (IntPtr)args, ref exc);
            }
            else
            {
                itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, IntPtr.Zero, ref exc);
            }

            if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> storageItems = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, storageItems) || storageItems.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < storageItems.Count; i++)
            {
                IntPtr itemObj = storageItems[i];
                if (itemObj == IntPtr.Zero)
                {
                    continue;
                }

                if (!this.TryGetDirectBackpackItemNetId(itemObj, out uint netId) || netId == 0U || !seenNetIds.Add(netId))
                {
                    continue;
                }

                items.Add(itemObj);
                appended++;
            }

            return appended > 0;
        }

        private bool TryGetDailyQuestSubmitTargetsAura(IntPtr gameTaskRow, out IntPtr submitTargetsArray, out List<IntPtr> targets)
        {
            submitTargetsArray = IntPtr.Zero;
            targets = new List<IntPtr>();
            if (gameTaskRow == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(gameTaskRow, "submitTargetItem", out submitTargetsArray) || submitTargetsArray == IntPtr.Zero)
            {
                return false;
            }

            return this.TryEnumerateAuraMonoCollectionItems(submitTargetsArray, targets) && targets.Count > 0;
        }

        private unsafe bool TryGetDailyQuestGameTaskRowPtrAura(int taskId, out IntPtr row, out string status)
        {
            row = IntPtr.Zero;
            status = "aura unavailable";
            IntPtr tableDataClass = this.TryGetDailyQuestProbeTableDataClass(out string classStatus);
            if (tableDataClass == IntPtr.Zero || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = classStatus;
                return false;
            }

            IntPtr getMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetGameTask", 1);
            if (getMethod == IntPtr.Zero)
            {
                getMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetGameTask", 2);
            }

            if (getMethod == IntPtr.Zero)
            {
                status = "GetGameTask missing";
                return false;
            }

            int id = taskId;
            bool needException = false;
            IntPtr exc = IntPtr.Zero;
            if (this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetGameTask", 2) == getMethod)
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                row = auraMonoRuntimeInvoke(getMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                row = auraMonoRuntimeInvoke(getMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            if (exc != IntPtr.Zero || row == IntPtr.Zero)
            {
                status = "GetGameTask failed";
                return false;
            }

            status = "ok";
            return true;
        }

        private unsafe bool TryInvokeDailyQuestClientSubmitTaskItemAura(
            int taskId,
            int submitType,
            int submitParam,
            IntPtr listObj,
            out string status)
        {
            status = "unavailable";
            if (taskId <= 0 || listObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryResolveDailyQuestClientSubmitTaskItemMethod(out IntPtr submitMethod, out int paramCount))
            {
                status = "ClientSubmitTaskItem missing";
                return false;
            }

            int gameTaskId = taskId;
            int type = submitType;
            int param = submitParam;
            IntPtr exc = IntPtr.Zero;
            IntPtr result = IntPtr.Zero;
            if (paramCount >= 6)
            {
                uint playerNetId = 0U;
                int targetIndex = -1;
                IntPtr* args = stackalloc IntPtr[6];
                args[0] = (IntPtr)(&gameTaskId);
                args[1] = (IntPtr)(&type);
                args[2] = (IntPtr)(&param);
                args[3] = listObj;
                args[4] = (IntPtr)(&playerNetId);
                args[5] = (IntPtr)(&targetIndex);
                result = auraMonoRuntimeInvoke(submitMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[4];
                args[0] = (IntPtr)(&gameTaskId);
                args[1] = (IntPtr)(&type);
                args[2] = (IntPtr)(&param);
                args[3] = listObj;
                result = auraMonoRuntimeInvoke(submitMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            if (exc != IntPtr.Zero)
            {
                status = "ClientSubmitTaskItem exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            if (result != IntPtr.Zero && this.TryUnboxMonoBoolean(result, out bool ok) && !ok)
            {
                status = "ClientSubmitTaskItem returned false";
                return false;
            }

            status = "aura ClientSubmitTaskItem List=0x" + listObj.ToInt64().ToString("X") + " params=" + paramCount;
            return true;
        }


        private bool TryResolveDailyQuestTaskIdFromOrder(IntPtr orderComponent, out int taskId, out int orderKey, out string orderFields)
        {
            taskId = 0;
            orderKey = 0;
            orderFields = string.Empty;
            if (orderComponent == IntPtr.Zero)
            {
                return false;
            }

            orderFields = this.FormatDailyQuestOrderComponentFields(orderComponent);
            orderKey = this.TryGetMonoIntMember(orderComponent, "TaskOrderId", out int key) ? key : 0;
            if (orderKey <= 0)
            {
                return false;
            }

            this.TryResolveDailyQuestTaskFromOrderKey(orderKey, out taskId, out _, out _, out _, out _);
            if (taskId <= 0)
            {
                taskId = orderKey;
            }

            return taskId > 0;
        }

        private unsafe bool TryInvokeDailyQuestClientAcceptTaskAura(int taskId, out string status)
        {
            status = string.Empty;
            if (taskId <= 0 || auraMonoRuntimeInvoke == null)
            {
                status = "accept args invalid";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "AuraMono attach failed";
                return false;
            }

            if (!this.TryResolveDailyQuestClientAcceptMethod(out IntPtr acceptMethod))
            {
                status = "ClientAcceptTask method missing";
                return false;
            }

            int gameTaskId = taskId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&gameTaskId);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(acceptMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "ClientAcceptTask exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "aura ClientAcceptTask";
            return true;
        }

        private bool TryResolveDailyQuestClientAcceptMethod(out IntPtr acceptMethod)
        {
            acceptMethod = this.dailyQuestSubmitClientAcceptMethod;
            if (acceptMethod != IntPtr.Zero)
            {
                return true;
            }

            IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Task.TaskProtocolManager");
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.Task", "TaskProtocolManager");
            }

            if (protocolClass == IntPtr.Zero)
            {
                return false;
            }

            acceptMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "ClientAcceptTask", 1);
            this.dailyQuestSubmitClientAcceptMethod = acceptMethod;
            return acceptMethod != IntPtr.Zero;
        }

        private bool TryResolveDailyQuestClientSubmitNpcMethod(out IntPtr submitMethod)
        {
            submitMethod = this.dailyQuestSubmitClientSubmitNpcMethod;
            if (submitMethod != IntPtr.Zero)
            {
                return true;
            }

            IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Task.TaskProtocolManager");
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.Task", "TaskProtocolManager");
            }

            if (protocolClass == IntPtr.Zero)
            {
                return false;
            }

            submitMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "ClientSubmitNpcTaskItem", 3);
            this.dailyQuestSubmitClientSubmitNpcMethod = submitMethod;
            return submitMethod != IntPtr.Zero;
        }

        private bool TryResolveDailyQuestClientSubmitTaskItemMethod(out IntPtr submitMethod, out int paramCount)
        {
            submitMethod = this.dailyQuestSubmitClientSubmitMethod;
            paramCount = this.dailyQuestSubmitClientSubmitTaskItemParamCount;
            if (submitMethod != IntPtr.Zero && paramCount > 0)
            {
                return true;
            }

            IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Task.TaskProtocolManager");
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.Task", "TaskProtocolManager");
            }

            if (protocolClass == IntPtr.Zero)
            {
                paramCount = 0;
                return false;
            }

            submitMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "ClientSubmitTaskItem", 6);
            paramCount = 6;
            if (submitMethod == IntPtr.Zero)
            {
                submitMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "ClientSubmitTaskItem", 4);
                paramCount = 4;
            }

            this.dailyQuestSubmitClientSubmitMethod = submitMethod;
            this.dailyQuestSubmitClientSubmitTaskItemParamCount = paramCount;
            return submitMethod != IntPtr.Zero;
        }

        /// <summary>
        /// Native list for AuraMono / Il2Cpp invokes. Does not call System.Type.GetType through embedded mono.
        /// </summary>

        /// <summary>
        /// Legacy: ItemNetPair[] is not used (game APIs take List&lt;struct ItemNetPair&gt;).
        /// </summary>




        /// <summary>
        /// Build List&lt;ItemNetPair&gt; in game Mono without Il2Cpp interop types (uses MonoDump/EcsClient image).
        /// </summary>
        private unsafe bool TryCreateDailyQuestItemNetPairListAuraMonoNative(
            List<DailyQuestSubmitNetPair> pairs,
            out IntPtr listObj,
            out string status)
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
                status = "AuraMono ItemNetPair list prerequisites unavailable";
                return false;
            }

            if (!this.TryResolveDailyQuestItemNetPairMembers(out IntPtr pairClass, out IntPtr netIdField, out IntPtr countField))
            {
                status = "ItemNetPair Aura class/fields missing";
                return false;
            }

            if (this.dailyQuestSubmitItemNetPairListClass != IntPtr.Zero && auraMonoObjectNew != null)
            {
                listObj = auraMonoObjectNew(this.auraMonoRootDomain, this.dailyQuestSubmitItemNetPairListClass);
                if (listObj != IntPtr.Zero && auraMonoRuntimeObjectInit != null)
                {
                    auraMonoRuntimeObjectInit(listObj);
                }
            }

            string[] listTypeCandidates = new[]
            {
                "System.Collections.Generic.List`1[[XDT.Scene.Shared.Modules.Backpack.ItemNetPair, EcsClient]]",
                "System.Collections.Generic.List`1[[XDT.Scene.Shared.Modules.Backpack.ItemNetPair, Client]]"
            };

            IntPtr* typeArgs = stackalloc IntPtr[1];
            IntPtr* createArgs = stackalloc IntPtr[1];
            for (int i = 0; i < listTypeCandidates.Length && listObj == IntPtr.Zero
                && this.auraMonoTypeGetTypeMethodPtr != IntPtr.Zero
                && this.auraMonoActivatorCreateInstanceMethodPtr != IntPtr.Zero; i++)
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
                status = "AuraMono List<ItemNetPair> create failed";
                return false;
            }

            IntPtr listClass = auraMonoObjectGetClass(listObj);
            if (this.dailyQuestSubmitItemNetPairListClass == IntPtr.Zero && listClass != IntPtr.Zero)
            {
                this.dailyQuestSubmitItemNetPairListClass = listClass;
            }

            IntPtr addMethod = this.dailyQuestSubmitAuraItemNetPairListAddMethod;
            if (addMethod == IntPtr.Zero && listClass != IntPtr.Zero)
            {
                addMethod = this.FindAuraMonoMethodOnHierarchy(listClass, "Add", 1);
                this.dailyQuestSubmitAuraItemNetPairListAddMethod = addMethod;
            }

            if (addMethod == IntPtr.Zero)
            {
                status = "AuraMono List<ItemNetPair>.Add missing";
                return false;
            }

            IntPtr* addArgs = stackalloc IntPtr[1];
            bool pairIsValueType = auraMonoClassIsValueType != null && auraMonoClassIsValueType(pairClass) != 0;
            const int itemNetPairStructSize = 8;
            for (int i = 0; i < pairs.Count; i++)
            {
                uint netId = pairs[i].NetId;
                int count = pairs[i].Count;
                IntPtr exc = IntPtr.Zero;

                if (pairIsValueType)
                {
                    byte* structData = stackalloc byte[itemNetPairStructSize];
                    *(uint*)structData = netId;
                    *(int*)(structData + sizeof(uint)) = count;
                    addArgs[0] = (IntPtr)structData;
                    auraMonoRuntimeInvoke(addMethod, listObj, (IntPtr)addArgs, ref exc);
                }
                else
                {
                    IntPtr pairObj = auraMonoObjectNew(this.auraMonoRootDomain, pairClass);
                    if (pairObj == IntPtr.Zero)
                    {
                        status = "ItemNetPair mono alloc failed";
                        return false;
                    }

                    auraMonoFieldSetValue(pairObj, netIdField, (IntPtr)(&netId));
                    auraMonoFieldSetValue(pairObj, countField, (IntPtr)(&count));
                    addArgs[0] = pairObj;
                    auraMonoRuntimeInvoke(addMethod, listObj, (IntPtr)addArgs, ref exc);
                }

                if (exc != IntPtr.Zero)
                {
                    status = "AuraMono List<ItemNetPair>.Add failed exc=0x" + exc.ToInt64().ToString("X")
                        + (pairIsValueType ? " (struct)" : string.Empty);
                    return false;
                }
            }

            if (!this.TryGetAuraMonoListCount(listObj, listClass, out int monoListCount))
            {
                status = "AuraMono List<ItemNetPair> count unreadable";
                return false;
            }

            if (monoListCount != pairs.Count)
            {
                status = "AuraMono List count mismatch expected=" + pairs.Count + " actual=" + monoListCount;
                return false;
            }

            status = "AuraMono List<ItemNetPair> count=" + monoListCount + (pairIsValueType ? " struct" : string.Empty);
            return true;
        }

        private unsafe bool TryGetAuraMonoListCount(IntPtr listObj, IntPtr listClass, out int count)
        {
            count = 0;
            if (listObj == IntPtr.Zero || listClass == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr countMethod = this.FindAuraMonoMethodOnHierarchy(listClass, "get_Count", 0);
            if (countMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(countMethod, listObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || result == IntPtr.Zero || !this.TryUnboxMonoInt32(result, out count))
            {
                count = 0;
                return false;
            }

            return true;
        }


        private bool TryResolveDailyQuestItemNetPairMembers(out IntPtr pairClass, out IntPtr netIdField, out IntPtr countField)
        {
            pairClass = this.dailyQuestSubmitItemNetPairClass;
            netIdField = this.dailyQuestSubmitItemNetPairNetIdField;
            countField = this.dailyQuestSubmitItemNetPairCountField;
            if (pairClass != IntPtr.Zero && netIdField != IntPtr.Zero && countField != IntPtr.Zero)
            {
                return true;
            }

            pairClass = this.FindAuraMonoClassInImages(
                "XDT.Scene.Shared.Modules.Backpack",
                "ItemNetPair",
                new[] { "EcsClient", "EcsClient.dll", "Client", "Client.dll", "Assembly-CSharp", "Assembly-CSharp.dll" });
            if (pairClass == IntPtr.Zero)
            {
                pairClass = this.FindAuraMonoClassByFullName("XDT.Scene.Shared.Modules.Backpack.ItemNetPair");
            }

            if (pairClass == IntPtr.Zero)
            {
                pairClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.Backpack", "ItemNetPair");
            }

            if (pairClass == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryResolveDailyQuestItemNetPairAuraFields(pairClass, out netIdField, out countField))
            {
                return false;
            }

            this.dailyQuestSubmitItemNetPairClass = pairClass;
            this.dailyQuestSubmitItemNetPairNetIdField = netIdField;
            this.dailyQuestSubmitItemNetPairCountField = countField;
            return true;
        }

        private bool TryResolveDailyQuestItemNetPairAuraFields(IntPtr pairClass, out IntPtr netIdField, out IntPtr countField)
        {
            netIdField = IntPtr.Zero;
            countField = IntPtr.Zero;
            if (pairClass == IntPtr.Zero)
            {
                return false;
            }

            string[] netIdNames = { "NetId", "netId", "_netId" };
            string[] countNames = { "Count", "count", "_count" };
            for (int i = 0; i < netIdNames.Length && netIdField == IntPtr.Zero; i++)
            {
                netIdField = this.FindAuraMonoFieldOnHierarchy(pairClass, netIdNames[i]);
            }

            for (int i = 0; i < countNames.Length && countField == IntPtr.Zero; i++)
            {
                countField = this.FindAuraMonoFieldOnHierarchy(pairClass, countNames[i]);
            }

            return netIdField != IntPtr.Zero && countField != IntPtr.Zero;
        }






























        private unsafe bool TryInvokeDailyQuestClientSubmitNpcAura(int taskId, int submitNpc, IntPtr listPtr, out string status)
        {
            status = string.Empty;
            if (taskId <= 0 || submitNpc <= 0 || listPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                status = "aura npc args invalid";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "AuraMono attach failed";
                return false;
            }

            if (!this.TryResolveDailyQuestClientSubmitNpcMethod(out IntPtr submitMethod))
            {
                status = "ClientSubmitNpcTaskItem method missing";
                return false;
            }

            int gameTaskId = taskId;
            int npcId = submitNpc;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&gameTaskId);
            args[1] = (IntPtr)(&npcId);
            args[2] = listPtr;
            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(submitMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "aura invoke exception exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            if (result != IntPtr.Zero && this.TryUnboxMonoBoolean(result, out bool ok) && !ok)
            {
                status = "ClientSubmitNpcTaskItem returned false";
                return false;
            }

            status = "aura ClientSubmitNpcTaskItem gameMono list=0x" + listPtr.ToInt64().ToString("X") + " (void)";
            return true;
        }

        private unsafe bool TryGetDailyQuestQuickSalePriceAura(int staticId, out int price)
        {
            price = int.MaxValue;
            IntPtr tableDataClass = this.TryGetDailyQuestProbeTableDataClass(out _);
            if (tableDataClass == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr getEntityMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntity", 1);
            if (getEntityMethod == IntPtr.Zero)
            {
                return false;
            }

            int id = staticId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);
            IntPtr exc = IntPtr.Zero;
            IntPtr entityRow = auraMonoRuntimeInvoke(getEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || entityRow == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoInt32Member(entityRow, "quickSalePrice", out int quickSale) || quickSale <= 0)
            {
                return false;
            }

            price = quickSale;
            return true;
        }



        private unsafe bool TryCollectDailyQuestOrdersForSubmit(out List<IntPtr> orders, out IntPtr unused, out string status)
        {
            orders = new List<IntPtr>();
            unused = IntPtr.Zero;
            status = string.Empty;

            if (!this.TryGetDailyQuestProbeDailyOrderSystemAura(out IntPtr dailyOrderSystemObj, out IntPtr dailyOrderSystemClass, out string systemStatus))
            {
                status = systemStatus;
                return false;
            }

            IntPtr getOrdersMethod = this.FindAuraMonoMethodOnHierarchy(dailyOrderSystemClass, "GetOrders", 1);
            if (getOrdersMethod == IntPtr.Zero)
            {
                status = "GetOrders missing";
                return false;
            }

            int refresh = DailyQuestRefreshTypeDaily;
            IntPtr* refreshArgs = stackalloc IntPtr[1];
            refreshArgs[0] = (IntPtr)(&refresh);
            IntPtr exc = IntPtr.Zero;
            IntPtr ordersObj = auraMonoRuntimeInvoke(getOrdersMethod, dailyOrderSystemObj, (IntPtr)refreshArgs, ref exc);
            if (exc != IntPtr.Zero || ordersObj == IntPtr.Zero)
            {
                status = "GetOrders failed";
                return false;
            }

            if (!this.TryEnumerateAuraMonoCollectionItems(ordersObj, orders) || orders.Count == 0)
            {
                status = "No daily orders";
                return false;
            }

            status = "ok";
            return true;
        }

        private bool ShouldSkipDailyQuestSubmitItem(int starRate)
        {
            if (!this.dailyQuestSubmitSkipFiveStar)
            {
                return false;
            }

            return this.NormalizeAutoSellStarRate(starRate) >= 5;
        }

        private float DrawDailyQuestSubmitControls(float startY)
        {
            float y = startY;
            const float left = 40f;
            const float width = 520f;

            bool busy = this.dailyQuestSubmitCoroutine != null || this.birdPhotoSubmitCoroutine != null || this.dailyClaimsCoroutine != null;
            GUI.enabled = !busy;
            if (GUI.Button(new Rect(left, y, 240f, 32f), this.L("Auto Submit Daily Items"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartDailyQuestAutoSubmitItems(silent: false);
            }

            GUI.enabled = true;
            y += 40f;

            bool nextSkipFiveStar = this.DrawSwitchToggle(
                new Rect(left, y, 300f, 28f),
                this.dailyQuestSubmitSkipFiveStar,
                this.L("Skip 5 Star Items"));
            if (nextSkipFiveStar != this.dailyQuestSubmitSkipFiveStar)
            {
                this.dailyQuestSubmitSkipFiveStar = nextSkipFiveStar;
                try
                {
                    this.SaveKeybinds(false);
                }
                catch
                {
                }
            }

            y += 34f;

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            statusStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.82f);
            GUI.Label(new Rect(left, y, width, 28f), this.dailyQuestSubmitLastStatus ?? string.Empty, statusStyle);
            return y + 36f;
        }

        private void DailyQuestSubmitLog(string message)
        {
            if (!DailyQuestSubmitLogsEnabled || string.IsNullOrEmpty(message))
            {
                return;
            }

            ModLogger.Msg("[DailyQuestSubmit] " + message);
        }

        private void DailyQuestSubmitLogOrderSnapshot(List<IntPtr> orders)
        {
            if (orders == null)
            {
                this.DailyQuestSubmitLog("order snapshot: null list");
                return;
            }

            this.DailyQuestSubmitLog(
                "--- daily orders snapshot count=" + orders.Count
                + " refreshType=Daily(" + DailyQuestRefreshTypeDaily + ")"
                + " skip5star=" + this.dailyQuestSubmitSkipFiveStar
                + " ---");

            for (int i = 0; i < orders.Count; i++)
            {
                IntPtr orderComponent = orders[i];
                if (orderComponent == IntPtr.Zero)
                {
                    this.DailyQuestSubmitLog("order[" + i + "] null component");
                    continue;
                }

                int orderKey = this.TryGetMonoIntMember(orderComponent, "TaskOrderId", out int key) ? key : 0;
                bool resolved = this.TryResolveDailyQuestTaskFromOrderKey(
                    orderKey,
                    out int taskId,
                    out int refreshType,
                    out int specialId,
                    out int level,
                    out string resolveNote);
                if (!resolved || taskId <= 0)
                {
                    taskId = orderKey;
                }

                string stateLine;
                string stateStatus = "n/a";
                if (taskId > 0 && this.TryGetGameTaskStateAura(taskId, out int state, out stateStatus))
                {
                    stateLine = this.FormatGameTaskState(state) + "(" + state + ")";
                }
                else
                {
                    stateLine = "unreadable(" + stateStatus + ")";
                }

                string configLine;
                DailyQuestGameTaskInfo info = default(DailyQuestGameTaskInfo);
                string configNote = "n/a";
                if (taskId > 0 && this.TryGetTableGameTaskRowAura(taskId, out info, out configNote))
                {
                    configLine = "submitNpc=" + info.SubmitNpc
                        + " submitType=" + info.SubmitType
                        + " submitParam=" + info.SubmitParam
                        + " targets=" + info.SubmitTargetCount
                        + " (" + configNote + ")";
                }
                else
                {
                    configLine = "config missing (" + configNote + ")";
                }

                this.DailyQuestSubmitLog(
                    "order[" + i + "] orderKey=" + orderKey
                    + " taskId=" + taskId
                    + " resolve=" + resolveNote
                    + " tableRefreshType=" + refreshType
                    + " specialId=" + specialId
                    + " level=" + level
                    + " state=" + stateLine
                    + " | " + configLine
                    + " | " + this.FormatDailyQuestOrderComponentFields(orderComponent));

                if (taskId > 0)
                {
                    this.DailyQuestSubmitLogSubmitTargets(taskId);
                }
            }

            this.DailyQuestSubmitLog("--- end order snapshot ---");
        }

        private void DailyQuestSubmitLogSubmitTargets(int taskId)
        {
            string rowStatus = "n/a";
            if (taskId <= 0
                || !this.TryGetDailyQuestGameTaskRowPtrAura(taskId, out IntPtr gameTaskRow, out rowStatus))
            {
                this.DailyQuestSubmitLog("  targets taskId=" + taskId + " unreadable: " + rowStatus);
                return;
            }

            int submitWay = this.TryGetMonoIntMember(gameTaskRow, "submitWay", out int way) ? way : 0;
            if (!this.TryGetDailyQuestSubmitTargetsAura(gameTaskRow, out _, out List<IntPtr> targets) || targets.Count == 0)
            {
                this.DailyQuestSubmitLog("  targets taskId=" + taskId + " submitWay=" + submitWay + " empty");
                return;
            }

            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                this.DailyQuestSubmitLog(
                    "  "
                    + this.FormatDailyQuestSubmitTargetLine(targetIndex, targets[targetIndex])
                    + " submitWay=" + submitWay);
            }
        }

        private string FormatDailyQuestOrderComponentFields(IntPtr orderComponent)
        {
            if (orderComponent == IntPtr.Zero)
            {
                return "component=null";
            }

            string[] intMembers =
            {
                "TaskOrderId", "taskId", "state", "refreshType", "specialId", "level",
                "progress", "isFinish", "isFinished", "finish", "orderId", "id"
            };

            List<string> parts = new List<string>();
            for (int i = 0; i < intMembers.Length; i++)
            {
                if (this.TryGetMonoIntMember(orderComponent, intMembers[i], out int value))
                {
                    parts.Add(intMembers[i] + "=" + value);
                }
            }

            return parts.Count > 0 ? string.Join(" ", parts) : "no readable int fields";
        }

        private string FormatDailyQuestSubmitTargetLine(int targetIndex, IntPtr targetItem)
        {
            if (targetItem == IntPtr.Zero)
            {
                return "target[" + targetIndex + "] null";
            }

            int needNum = this.TryGetMonoIntMember(targetItem, "needNum", out int need) ? need : -1;
            int quality = this.TryGetMonoIntMember(targetItem, "quality", out int q) ? q : 0;
            int staticId = this.TryGetDailyQuestSubmitTargetStaticId(targetItem);
            return "target[" + targetIndex + "] needNum=" + needNum
                + " targetQuality=" + quality
                + " staticId=" + staticId;
        }

        private int TryGetDailyQuestSubmitTargetStaticId(IntPtr targetItem)
        {
            string[] idMembers = { "staticId", "itemId", "id", "type", "itemType", "targetId", "itemStaticId" };
            for (int i = 0; i < idMembers.Length; i++)
            {
                if (this.TryGetMonoIntMember(targetItem, idMembers[i], out int value) && value > 0)
                {
                    return value;
                }
            }

            return 0;
        }

        private void LogDailyQuestTargetCandidateDiagnostics(
            int taskId,
            int targetIndex,
            IntPtr targetItem,
            int targetQuality,
            IntPtr submitTargetsArray,
            IntPtr checkSubmitMethod,
            IntPtr checkSubmitItemsMethod,
            IntPtr backPackSystemObj,
            List<IntPtr> storageItems)
        {
            int total = storageItems != null ? storageItems.Count : 0;
            int invalidItem = 0;
            int locked = 0;
            int skipFiveStar = 0;
            int checkSubmitItemsFail = 0;
            int checkSubmitItemFail = 0;
            int passed = 0;

            for (int itemIndex = 0; itemIndex < total; itemIndex++)
            {
                IntPtr itemObj = storageItems[itemIndex];
                if (itemObj == IntPtr.Zero)
                {
                    invalidItem++;
                    continue;
                }

                if (!this.TryGetDailyQuestItemSubmitSortKey(itemObj, out uint netId, out int count, out int price, out int starRate)
                    || netId == 0U
                    || count <= 0)
                {
                    invalidItem++;
                    continue;
                }

                if (this.TryGetDirectBackpackItemIsLocked(itemObj, out bool isLocked) && isLocked)
                {
                    locked++;
                    continue;
                }

                if (this.ShouldSkipDailyQuestSubmitItem(starRate))
                {
                    skipFiveStar++;
                    continue;
                }

                if (checkSubmitItemsMethod != IntPtr.Zero && submitTargetsArray != IntPtr.Zero
                    && (!this.TryInvokeBackPackCheckSubmitItemsAura(
                            checkSubmitItemsMethod,
                            backPackSystemObj,
                            submitTargetsArray,
                            netId,
                            out bool matchesAll)
                        || !matchesAll))
                {
                    checkSubmitItemsFail++;
                    continue;
                }

                if (!this.TryInvokeBackPackCheckSubmitItemAura(checkSubmitMethod, backPackSystemObj, targetItem, netId, out bool matches)
                    || !matches)
                {
                    checkSubmitItemFail++;
                    continue;
                }

                passed++;
            }

            this.DailyQuestSubmitLog(
                "candidate diagnostics taskId=" + taskId
                + " " + this.FormatDailyQuestSubmitTargetLine(targetIndex, targetItem)
                + " storage=" + total
                + " targetQuality=" + targetQuality
                + " passed=" + passed
                + " locked=" + locked
                + " skip5star=" + skipFiveStar
                + " CheckSubmitItems fail=" + checkSubmitItemsFail
                + " CheckSubmitItem fail=" + checkSubmitItemFail
                + " invalid=" + invalidItem
                + " skip5starEnabled=" + this.dailyQuestSubmitSkipFiveStar);
        }

        private bool TryResolveDailyQuestTaskFromOrderKey(
            int orderKey,
            out int taskId,
            out int refreshType,
            out int specialId,
            out int level,
            out string note)
        {
            if (this.TryGetTableTaskOrderRowAura(orderKey, out taskId, out refreshType, out specialId, out level, out note))
            {
                return true;
            }

            if (orderKey <= 0)
            {
                note = "invalid order key";
                return false;
            }

            if (this.TryGetTableGameTaskRowAura(orderKey, out _, out string gameTaskNote) && gameTaskNote.Contains("aura ok"))
            {
                taskId = orderKey;
                refreshType = 0;
                specialId = 0;
                level = 0;
                note = "order key is game task id";
                return true;
            }

            note = string.IsNullOrEmpty(note) ? "unresolved" : note;
            return false;
        }

        private string FormatGameTaskState(int state)
        {
            switch (state)
            {
                case 0: return "NotOpen";
                case 1: return "OpenedCannotAccept";
                case 2: return "CanAccept";
                case 3: return "Accepted";
                case 4: return "CanSubmit";
                case 5: return "Finished";
                case 6: return "Close";
                default: return "state?" + state;
            }
        }

        private bool TryGetDailyQuestProbeDailyOrderSystemAura(out IntPtr systemObj, out IntPtr systemClass, out string status)
        {
            systemObj = IntPtr.Zero;
            systemClass = IntPtr.Zero;
            status = "AuraMono unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null)
            {
                return false;
            }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Quest.DailyOrderSystem", out systemObj) || systemObj == IntPtr.Zero)
            {
                status = "DailyOrderSystem module resolve failed";
                return false;
            }

            systemClass = auraMonoObjectGetClass(systemObj);
            status = "ok";
            return true;
        }

        private IntPtr TryGetDailyQuestProbeTableDataClass(out string status)
        {
            status = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || auraMonoClassFromName == null)
            {
                status = "AuraMono API unavailable";
                return IntPtr.Zero;
            }

            IntPtr ecsImage = this.FindAuraMonoImage(new[] { "EcsClient", "EcsClient.dll" });
            IntPtr tableDataClass = ecsImage != IntPtr.Zero ? auraMonoClassFromName(ecsImage, string.Empty, "TableData") : IntPtr.Zero;
            if (tableDataClass == IntPtr.Zero && ecsImage != IntPtr.Zero)
            {
                tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
            }

            if (tableDataClass == IntPtr.Zero)
            {
                tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
            }

            if (tableDataClass == IntPtr.Zero)
            {
                tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies("EcsClient", "TableData");
            }

            if (tableDataClass == IntPtr.Zero)
            {
                status = "TableData class missing";
            }

            return tableDataClass;
        }

        private unsafe bool TryGetTableTaskOrderRowAura(int taskOrderId, out int taskId, out int refreshType, out int specialId, out int level, out string note)
        {
            taskId = 0;
            refreshType = 0;
            specialId = 0;
            level = 0;
            note = "aura unavailable";
            IntPtr tableDataClass = this.TryGetDailyQuestProbeTableDataClass(out string classStatus);
            if (tableDataClass == IntPtr.Zero || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                note = classStatus;
                return false;
            }

            IntPtr getMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetTaskOrder", 2);
            if (getMethod == IntPtr.Zero)
            {
                getMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetTaskOrder", 1);
            }

            if (getMethod == IntPtr.Zero)
            {
                note = "GetTaskOrder missing";
                return false;
            }

            int id = taskOrderId;
            bool needException = false;
            IntPtr exc = IntPtr.Zero;
            IntPtr row;
            if (this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetTaskOrder", 2) == getMethod)
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                row = auraMonoRuntimeInvoke(getMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                row = auraMonoRuntimeInvoke(getMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            if (exc != IntPtr.Zero || row == IntPtr.Zero)
            {
                note = "GetTaskOrder null";
                return false;
            }

            taskId = this.TryGetMonoIntMember(row, "taskId", out int tid) ? tid : 0;
            refreshType = this.TryGetMonoIntMember(row, "refreshType", out int rt) ? rt : 0;
            specialId = this.TryGetMonoIntMember(row, "specialId", out int sid) ? sid : 0;
            level = this.TryGetMonoIntMember(row, "level", out int lv) ? lv : 0;
            note = "aura ok";
            return taskId > 0;
        }

        private unsafe bool TryGetTableGameTaskRowAura(int taskId, out DailyQuestGameTaskInfo info, out string note)
        {
            info = default(DailyQuestGameTaskInfo);
            note = "aura unavailable";
            IntPtr tableDataClass = this.TryGetDailyQuestProbeTableDataClass(out string classStatus);
            if (tableDataClass == IntPtr.Zero || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                note = classStatus;
                return false;
            }

            IntPtr getMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetGameTask", 1);
            if (getMethod == IntPtr.Zero)
            {
                getMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetGameTask", 2);
            }

            if (getMethod == IntPtr.Zero)
            {
                note = "GetGameTask missing";
                return false;
            }

            int id = taskId;
            bool needException = false;
            IntPtr exc = IntPtr.Zero;
            IntPtr row;
            if (this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetGameTask", 2) == getMethod)
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                row = auraMonoRuntimeInvoke(getMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                row = auraMonoRuntimeInvoke(getMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            if (exc != IntPtr.Zero || row == IntPtr.Zero)
            {
                note = "GetGameTask failed";
                return false;
            }

            info.SubmitNpc = this.TryGetMonoIntMember(row, "submitNpc", out int npc) ? npc : 0;
            info.SubmitType = this.TryGetMonoIntMember(row, "submitType", out int st) ? st : 0;
            info.SubmitParam = this.TryGetMonoIntMember(row, "submitParam", out int sp) ? sp : 0;
            if (this.TryGetMonoObjectMember(row, "submitTargetItem", out IntPtr targetsObj) && targetsObj != IntPtr.Zero)
            {
                List<IntPtr> targets = new List<IntPtr>();
                if (this.TryEnumerateAuraMonoCollectionItems(targetsObj, targets))
                {
                    info.SubmitTargetCount = targets.Count;
                }
            }

            note = "aura ok";
            return true;
        }

        private unsafe bool TryGetGameTaskStateAura(int taskId, out int stateValue, out string status)
        {
            stateValue = -1;
            status = "unavailable";
            IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Task.TaskProtocolManager");
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.Task", "TaskProtocolManager");
            }

            if (protocolClass == IntPtr.Zero || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr getStateMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "GetTaskState", 1);
            if (getStateMethod == IntPtr.Zero)
            {
                status = "GetTaskState missing";
                return false;
            }

            int id = taskId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);
            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(getStateMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || result == IntPtr.Zero || !this.TryUnboxMonoInt32(result, out stateValue))
            {
                status = "invoke failed";
                return false;
            }

            status = "ok";
            return true;
        }

        private struct DailyQuestResolvedOrder
        {
            public bool Resolved;
            public int TaskId;
            public int OrderKey;
            public string Fields;
        }

        private struct DailyQuestGameTaskInfo
        {
            public int SubmitNpc;
            public int SubmitType;
            public int SubmitParam;
            public int SubmitTargetCount;
        }

        private struct DailyQuestSubmitNetPair
        {
            public uint NetId;
            public int Count;
        }

        private struct DailyQuestSubmitCandidate
        {
            public uint NetId;
            public int Count;
            public int Price;
            public int StarRate;
        }
    }
}
