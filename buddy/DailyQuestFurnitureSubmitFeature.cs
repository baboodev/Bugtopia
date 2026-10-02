using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // Daily furniture requests — "Skip Furniture" for Auto Submit, and a hand-picked submit through
    // the game's own item-selection panel.
    //
    // ── WHICH ORDERS ARE "FURNITURE" ────────────────────────────────────────────────────────────
    // The daily "Request: Furniture" orders (8010911..8010951, 8020611..8020651 on this build) ask
    // for any item carrying EntityTag 20 ("Furniture"): TableGameTask.submitTargetItem[] holds
    // `submitCondition = "(ItemTag[20] = 1)"`, quality 0. Auto Submit fills those with the cheapest
    // matching items, which is how a player loses furniture they meant to keep — hence the opt-out,
    // and the hand-picked path below for when they do want to hand some over.
    //
    // ── THE PANEL ───────────────────────────────────────────────────────────────────────────────
    // XDTGame.UI.Panel.NewSubmitItemPanel is the screen the NPC dialogue opens for a CanSubmit
    // order (DialogueNodeTask). Its static Open(taskId, submitTargetType, staticIdOrResId,
    // SubmitTypeOption, prefabName) works from anywhere: with a null SubmitTypeOption it runs in
    // NpcSubmit mode and lists exactly the items BackPackSystem.CheckSubmitItems accepts.
    //
    // What does NOT work from anywhere is its Submit button. In NpcSubmit mode _Submit() does not
    // send: it closes the panel and dispatches SubmitItemDialogueEvent { callback = () =>
    // ClientSubmitTaskItem(...) }, and only the dialogue node that opened the panel listens for it
    // and runs the callback. Opened by us, nobody listens and the confirm is a silent no-op.
    //
    // So the confirm is completed here instead:
    //   1. SubmitItemDialogueEvent is hooked through the EventCenter dispatch-detour engine — the
    //      event only serves as the "Submit was pressed" signal (callback != null; Cancel dispatches
    //      the same event with a null callback).
    //   2. The player's selection is read off the panel object, which is pinned from the moment it
    //      opens: List<SlotDisplayData> _slotDisplayDatas, each with Dictionary<uint, int>
    //      submitItem (item netId -> count). OnStop clears _submitItems but leaves the slots alone,
    //      so they still hold the selection after the panel closed itself.
    //   3. The pairs go out through the same validated path Auto Submit uses
    //      (TrySendDailyQuestNpcSubmitViaGameAuraMono -> TaskProtocolManager.ClientSubmitTaskItem).
    // The delegate in the event is never touched: by the time the drain runs it is unrooted, and a
    // NativeDetour on the panel method that could call it in place is the crash-prone kind of hook
    // this project avoids.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string DailyFurnitureTag = "DailyFurniture";
        private const string DailyFurniturePanelTypeName = "XDTGame.UI.Panel.NewSubmitItemPanel";
        private const string DailyFurnitureSubmitEventName = "XDTDataAndProtocol.Events.SubmitItemDialogueEvent";
        // SubmitItemDialogueEvent as a bare struct: long[] textId @0, bool showDialogue @8,
        // Action callback @16.
        private const int DailyFurnitureSubmitEventBytes = 24;
        private const int DailyFurnitureSubmitEventCallbackOffset = 16;
        private const string DailyFurnitureConditionTag = "ItemTag[20]";

        private const float DailyFurnitureHookWaitSeconds = 3f;
        private const float DailyFurniturePanelAppearSeconds = 5f;
        private const float DailyFurniturePollSeconds = 0.25f;
        // The confirm event is dispatched right after the panel closes itself; the drain may run a
        // frame or two later than the poll that sees the panel gone.
        private const float DailyFurnitureCloseGraceSeconds = 1.5f;

        private bool dailyQuestSubmitSkipFurniture = false;

        // Button visibility: is there a furniture order in CanSubmit state right now? Answered by a
        // full order scan, re-run only when TaskStateChanged touches a furniture task (or our own
        // submits finish), on a world change, and as a slow fallback for what no event reports -
        // the daily reset removes orders without a state change.
        private const string DailyFurnitureTaskStateEventName = "XDTDataAndProtocol.Events.TaskStateChanged";
        // TaskStateChanged: int taskStaticId @0, uint taskNetId @4, GameTaskState TaskState @8.
        private const int DailyFurnitureTaskStateEventBytes = 12;
        private const float DailyFurnitureAvailabilityMinInterval = 1f;
        private const float DailyFurnitureAvailabilityMaxAge = 30f;
        private const float DailyFurnitureAvailabilityNoHookMaxAge = 5f;

        private bool dailyFurnitureAvailable;
        private bool dailyFurnitureAvailabilityDirty = true;
        private float dailyFurnitureAvailabilityCheckedAt = -999f;
        private int dailyFurnitureAvailabilityEpoch = -1;
        private bool dailyFurnitureStateHookRegistered;

        private readonly Dictionary<int, bool> dailyFurnitureTaskVerdicts = new Dictionary<int, bool>();

        private bool dailyFurnitureHookRegistered;
        private object dailyFurnitureCoroutine;
        private IntPtr dailyFurnitureGetViewMethod;
        private AuraMonoObjectCache dailyFurniturePanelType;
        // The panel we opened, pinned until its confirm/cancel has been handled.
        private AuraMonoObjectCache dailyFurniturePanel;

        // The order the open panel belongs to — scalars only, captured before it opened.
        private bool dailyFurniturePending;
        private int dailyFurniturePendingTaskId;
        private DailyQuestGameTaskInfo dailyFurniturePendingInfo;

        // ----------------------------------------------------------------------------------------
        // Classification
        // ----------------------------------------------------------------------------------------

        // True when any submit target of the task is the Furniture tag. Table rows never change at
        // runtime, so the verdict is cached per task id; a failed read is not cached.
        private bool IsDailyQuestFurnitureTask(int taskId)
        {
            if (taskId <= 0)
            {
                return false;
            }

            if (this.dailyFurnitureTaskVerdicts.TryGetValue(taskId, out bool cached))
            {
                return cached;
            }

            if (!this.TryGetDailyQuestGameTaskRowPtrAura(taskId, out IntPtr row, out _) || row == IntPtr.Zero)
            {
                return false;
            }

            uint rowPin = AuraMonoPinNew(row);
            List<uint> targetPins = new List<uint>();
            try
            {
                if (!this.TryGetMonoObjectMember(row, "submitTargetItem", out IntPtr targetsArray) || targetsArray == IntPtr.Zero)
                {
                    // No submit targets at all: not an item order, and that is a stable answer.
                    this.dailyFurnitureTaskVerdicts[taskId] = false;
                    return false;
                }

                List<IntPtr> targets = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(targetsArray, targets, targetPins))
                {
                    return false;
                }

                bool furniture = false;
                for (int i = 0; i < targets.Count && !furniture; i++)
                {
                    if (targets[i] != IntPtr.Zero
                        && this.TryGetMonoStringMember(targets[i], "submitCondition", out string condition)
                        && condition.IndexOf(DailyFurnitureConditionTag, StringComparison.Ordinal) >= 0)
                    {
                        furniture = true;
                    }
                }

                this.dailyFurnitureTaskVerdicts[taskId] = furniture;
                return furniture;
            }
            finally
            {
                FreeAuraMonoPins(targetPins);
                if (rowPin != 0U) { AuraMonoPinFree(rowPin); }
            }
        }

        // ----------------------------------------------------------------------------------------
        // "Submit Furniture..." — open the game's selection panel for the furniture order
        // ----------------------------------------------------------------------------------------

        private bool IsDailyFurnitureSubmitBusy()
        {
            return this.dailyFurnitureCoroutine != null;
        }

        // Polled by the Daily Quests page while it is visible; returns the cached answer and
        // refreshes it only when something says it may have changed.
        private bool IsDailyFurnitureSubmitAvailable()
        {
            if (this.dailyFurnitureCoroutine != null)
            {
                return true; // keep the button while its own flow runs
            }

            if (!this.IsWorldReady)
            {
                this.dailyFurnitureAvailable = false;
                this.dailyFurnitureAvailabilityDirty = true;
                return false;
            }

            // Registered here rather than at startup: only players who open this page pay the slot.
            if (!this.dailyFurnitureStateHookRegistered)
            {
                this.dailyFurnitureStateHookRegistered = this.RegisterGameEventHook(
                    DailyFurnitureTaskStateEventName, DailyFurnitureTaskStateEventBytes, this.OnDailyFurnitureTaskStateChanged);
                if (!this.dailyFurnitureStateHookRegistered)
                {
                    FeatureLog.Once(DailyFurnitureTag, "state-hook-refused",
                        "TaskStateChanged hook refused - the Submit Furniture button falls back to a "
                        + DailyFurnitureAvailabilityNoHookMaxAge + " s poll");
                }
            }

            if (this.dailyFurnitureAvailabilityEpoch != auraMonoWorldEpoch)
            {
                this.dailyFurnitureAvailabilityEpoch = auraMonoWorldEpoch;
                this.dailyFurnitureAvailabilityDirty = true;
            }

            float now = Time.realtimeSinceStartup;
            float maxAge = this.IsGameEventHookInstalled(DailyFurnitureTaskStateEventName)
                ? DailyFurnitureAvailabilityMaxAge
                : DailyFurnitureAvailabilityNoHookMaxAge;
            float age = now - this.dailyFurnitureAvailabilityCheckedAt;
            if ((!this.dailyFurnitureAvailabilityDirty && age < maxAge) || age < DailyFurnitureAvailabilityMinInterval)
            {
                return this.dailyFurnitureAvailable;
            }

            this.dailyFurnitureAvailabilityDirty = false;
            this.dailyFurnitureAvailabilityCheckedAt = now;
            try
            {
                this.dailyFurnitureAvailable = this.EnsureAuraMonoApiReady()
                    && this.AttachAuraMonoThread()
                    && this.TryFindDailyFurnitureTask(out _, out _, out _, quiet: true);
            }
            catch (Exception ex)
            {
                this.dailyFurnitureAvailable = false;
                FeatureLog.Once(DailyFurnitureTag, "availability-threw", "availability check threw: " + ex.Message);
            }

            return this.dailyFurnitureAvailable;
        }

        private void OnDailyFurnitureTaskStateChanged(GameEventSnapshot e)
        {
            int taskStaticId = e.ReadInt32(0);
            if (taskStaticId > 0 && this.IsDailyQuestFurnitureTask(taskStaticId))
            {
                this.dailyFurnitureAvailabilityDirty = true;
            }
        }

        private void StartDailyFurnitureSubmitPanel()
        {
            if (this.dailyFurnitureCoroutine != null || this.dailyQuestSubmitCoroutine != null)
            {
                this.AddMenuNotification("Submit already running", new Color(0.45f, 0.88f, 1f));
                return;
            }

            this.dailyFurnitureCoroutine = ModCoroutines.Start(this.DailyFurnitureSubmitPanelRoutine());
        }

        private IEnumerator DailyFurnitureSubmitPanelRoutine()
        {
            try
            {
                yield return null;

                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || !AuraMonoPinningAvailable)
                {
                    this.DailyFurnitureReport("AuraMono unavailable", false);
                    FeatureLog.Fail(DailyFurnitureTag, "AuraMono not ready (or pinning unavailable) — cannot open the submit panel");
                    yield break;
                }

                if (!this.TryFindDailyFurnitureTask(out int taskId, out DailyQuestGameTaskInfo info, out string findStatus, quiet: false))
                {
                    this.DailyFurnitureReport(findStatus, false);
                    yield break;
                }

                // Lazy: the hook costs one of the engine's never-released slots, so only players who
                // use this button pay for it. Installation follows within one throttled pass.
                if (!this.dailyFurnitureHookRegistered)
                {
                    this.dailyFurnitureHookRegistered = this.RegisterGameEventHook(
                        DailyFurnitureSubmitEventName, DailyFurnitureSubmitEventBytes, this.OnDailyFurnitureSubmitEvent);
                    if (!this.dailyFurnitureHookRegistered)
                    {
                        this.DailyFurnitureReport("Could not prepare the submit panel", false);
                        FeatureLog.Fail(DailyFurnitureTag, "RegisterGameEventHook(" + DailyFurnitureSubmitEventName + ") refused");
                        yield break;
                    }
                }

                float hookDeadline = Time.realtimeSinceStartup + DailyFurnitureHookWaitSeconds;
                while (!this.IsGameEventHookInstalled(DailyFurnitureSubmitEventName))
                {
                    if (Time.realtimeSinceStartup > hookDeadline)
                    {
                        this.DailyFurnitureReport("Could not prepare the submit panel", false);
                        FeatureLog.Fail(DailyFurnitureTag, "event hook " + DailyFurnitureSubmitEventName
                            + " did not install within " + DailyFurnitureHookWaitSeconds + " s");
                        yield break;
                    }
                    yield return null;
                }

                if (!this.TryOpenDailyFurniturePanel(taskId, info, out string openError))
                {
                    this.DailyFurnitureReport("Could not open the submit panel", false);
                    FeatureLog.Fail(DailyFurnitureTag, "open failed for taskId " + taskId + ": " + openError);
                    yield break;
                }

                this.dailyFurniturePendingTaskId = taskId;
                this.dailyFurniturePendingInfo = info;
                this.dailyFurniturePending = true;
                this.dailyQuestSubmitLastStatus = "Pick the furniture to hand over, then press Submit.";
                FeatureLog.Life(DailyFurnitureTag, "submit panel opened for taskId " + taskId
                    + " (npc " + info.SubmitNpc + ", type " + info.SubmitType + ", param " + info.SubmitParam + ")");

                // Capture the panel object (OpenView may build it a frame or more later), then watch
                // it until the player confirms or backs out.
                float appearDeadline = Time.realtimeSinceStartup + DailyFurniturePanelAppearSeconds;
                bool captured = false;
                float closedAt = -1f;
                while (this.dailyFurniturePending)
                {
                    bool open = this.TryGetOpenDailyFurniturePanel(out IntPtr panel) && panel != IntPtr.Zero;
                    if (open)
                    {
                        if (!captured)
                        {
                            this.dailyFurniturePanel.Set(panel);
                            captured = this.dailyFurniturePanel.TryGet(out _);
                        }
                        closedAt = -1f;
                    }
                    else if (!captured)
                    {
                        if (Time.realtimeSinceStartup > appearDeadline)
                        {
                            this.DailyFurnitureReport("The submit panel did not appear", false);
                            FeatureLog.Fail(DailyFurnitureTag, "NewSubmitItemPanel never became visible for taskId " + taskId);
                            break;
                        }
                    }
                    else if (closedAt < 0f)
                    {
                        closedAt = Time.realtimeSinceStartup;
                    }
                    else if (Time.realtimeSinceStartup - closedAt > DailyFurnitureCloseGraceSeconds)
                    {
                        // Closed without a confirm reaching us: the player backed out.
                        this.dailyQuestSubmitLastStatus = "Furniture submit cancelled.";
                        FeatureLog.Life(DailyFurnitureTag, "panel closed without Submit (taskId " + taskId + ")");
                        break;
                    }

                    yield return ModWait.Realtime(DailyFurniturePollSeconds);
                }
            }
            finally
            {
                this.dailyFurniturePending = false;
                this.dailyFurniturePanel.Clear();
                this.dailyFurnitureCoroutine = null;
                this.dailyFurnitureAvailabilityDirty = true;
            }
        }

        // The first daily order that is a furniture request in CanSubmit state. `quiet` is the
        // background availability check: its failures log once per kind instead of every scan.
        private bool TryFindDailyFurnitureTask(out int taskId, out DailyQuestGameTaskInfo info, out string status, bool quiet)
        {
            taskId = 0;
            info = default(DailyQuestGameTaskInfo);

            if (!this.TryCollectDailyQuestOrdersForSubmit(out List<IntPtr> orders, out _, out string collectStatus))
            {
                status = "No daily requests found";
                this.DailyFurnitureFail(quiet, "collect", "collect daily orders failed: " + collectStatus);
                return false;
            }

            List<DailyQuestResolvedOrder> resolved = this.ResolveDailyQuestOrdersForSubmit(orders);
            int furnitureSeen = 0;
            int lastState = -1;
            for (int i = 0; i < resolved.Count; i++)
            {
                int candidate = resolved[i].TaskId;
                if (!resolved[i].Resolved || candidate <= 0 || !this.IsDailyQuestFurnitureTask(candidate))
                {
                    continue;
                }

                furnitureSeen++;
                if (!this.TryGetGameTaskStateAura(candidate, out int state, out string stateStatus))
                {
                    this.DailyFurnitureFail(quiet, "state", "GetTaskState failed for taskId " + candidate + ": " + stateStatus);
                    continue;
                }

                lastState = state;
                if (state != DailyQuestSubmitStateCanSubmit)
                {
                    continue;
                }

                if (!this.TryGetTableGameTaskRowAura(candidate, out info, out string rowNote) || info.SubmitTargetCount <= 0)
                {
                    this.DailyFurnitureFail(quiet, "row", "task row unreadable for taskId " + candidate + ": " + rowNote);
                    continue;
                }

                taskId = candidate;
                status = "ok";
                return true;
            }

            status = "Furniture request is not ready to submit";
            if (furnitureSeen == 0)
            {
                status = "No furniture request today";
            }
            else if (!quiet)
            {
                FeatureLog.Once(DailyFurnitureTag, "not-ready:" + lastState,
                    furnitureSeen + " furniture request(s), none CanSubmit (last state " + this.FormatGameTaskState(lastState) + ")");
            }

            return false;
        }

        // NewSubmitItemPanel.Open(int taskId, int submitTargetType, int staticIdOrResId,
        // SubmitTypeOption submitType, string prefabName): a null SubmitTypeOption selects NpcSubmit,
        // a null prefabName the default prefab.
        private unsafe bool TryOpenDailyFurniturePanel(int taskId, DailyQuestGameTaskInfo info, out string error)
        {
            error = null;
            IntPtr panelClass = this.FindAuraMonoClassByFullName(DailyFurniturePanelTypeName);
            if (panelClass == IntPtr.Zero)
            {
                panelClass = this.FindAuraMonoClassInImages("XDTGame.UI.Panel", "NewSubmitItemPanel",
                    new string[] { "XDTGameUI", "XDTGameUI.dll" });
            }
            if (panelClass == IntPtr.Zero)
            {
                error = "class " + DailyFurniturePanelTypeName + " not found";
                return false;
            }

            IntPtr openMethod = this.FindAuraMonoMethodOnHierarchy(panelClass, "Open", 5);
            if (openMethod == IntPtr.Zero)
            {
                error = "NewSubmitItemPanel.Open/5 not found";
                return false;
            }

            int localTaskId = taskId;
            int submitTargetType = info.SubmitType;
            int staticIdOrResId = info.SubmitParam;
            IntPtr* args = stackalloc IntPtr[5];
            args[0] = (IntPtr)(&localTaskId);
            args[1] = (IntPtr)(&submitTargetType);
            args[2] = (IntPtr)(&staticIdOrResId);
            args[3] = IntPtr.Zero;
            args[4] = IntPtr.Zero;
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(openMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                error = "NewSubmitItemPanel.Open raised an exception";
                return false;
            }

            return true;
        }

        // UIManager.GetView(Type) — non-null only while the panel is open and not closing.
        private unsafe bool TryGetOpenDailyFurniturePanel(out IntPtr panel)
        {
            panel = IntPtr.Zero;
            if (!this.TryPersistentHudResolveUiManager(out IntPtr uiManagerObj)
                || uiManagerObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.dailyFurnitureGetViewMethod == IntPtr.Zero)
            {
                this.dailyFurnitureGetViewMethod = this.persistentHudGetViewMethod;
            }
            if (this.dailyFurnitureGetViewMethod == IntPtr.Zero)
            {
                return false;
            }

            if (!this.dailyFurniturePanelType.TryGet(out IntPtr typeObj))
            {
                if (!this.TryCreateAuraMonoSystemTypeObject(DailyFurniturePanelTypeName, out typeObj) || typeObj == IntPtr.Zero)
                {
                    FeatureLog.Once(DailyFurnitureTag, "type-unresolved",
                        "System.Type for " + DailyFurniturePanelTypeName + " unresolved — cannot see the submit panel");
                    return false;
                }
                this.dailyFurniturePanelType.Set(typeObj);
                if (!this.dailyFurniturePanelType.TryGet(out typeObj))
                {
                    return false;
                }
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = typeObj;
            IntPtr view = auraMonoRuntimeInvoke(this.dailyFurnitureGetViewMethod, uiManagerObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || view == IntPtr.Zero)
            {
                return false;
            }

            panel = view;
            return true;
        }

        // ----------------------------------------------------------------------------------------
        // Confirm — SubmitItemDialogueEvent
        // ----------------------------------------------------------------------------------------

        private void OnDailyFurnitureSubmitEvent(GameEventSnapshot e)
        {
            // Not ours: the NPC dialogue opened the panel and its own listener completes it.
            if (!this.dailyFurniturePending)
            {
                return;
            }

            this.dailyFurniturePending = false;
            int taskId = this.dailyFurniturePendingTaskId;

            // Cancel dispatches the same event without a callback.
            if (e.ReadUInt64(DailyFurnitureSubmitEventCallbackOffset) == 0UL)
            {
                this.dailyQuestSubmitLastStatus = "Furniture submit cancelled.";
                FeatureLog.Life(DailyFurnitureTag, "panel returned without Submit (taskId " + taskId + ")");
                return;
            }

            try
            {
                if (!this.dailyFurniturePanel.TryGet(out IntPtr panel) || panel == IntPtr.Zero)
                {
                    this.DailyFurnitureReport("Could not read the selection", false);
                    FeatureLog.Fail(DailyFurnitureTag, "Submit pressed but the panel object was never captured (taskId " + taskId + ")");
                    return;
                }

                if (!this.TryReadDailyFurnitureSelection(panel, out List<DailyQuestSubmitNetPair> pairs, out string readStatus))
                {
                    this.DailyFurnitureReport("Could not read the selection", false);
                    FeatureLog.Fail(DailyFurnitureTag, "selection read failed (taskId " + taskId + "): " + readStatus);
                    return;
                }

                DailyQuestGameTaskInfo info = this.dailyFurniturePendingInfo;
                if (!this.TrySendDailyQuestNpcSubmitViaGameAuraMono(
                        taskId, info.SubmitNpc, info.SubmitType, info.SubmitParam, pairs, out string sendStatus))
                {
                    this.DailyFurnitureReport("Furniture submit failed", false);
                    FeatureLog.Fail(DailyFurnitureTag, "submit failed (taskId " + taskId + ", " + pairs.Count + " stack(s)): " + sendStatus);
                    return;
                }

                int total = 0;
                for (int i = 0; i < pairs.Count; i++)
                {
                    total += pairs[i].Count;
                }

                FeatureLog.Life(DailyFurnitureTag, "submitted taskId " + taskId + ": " + total + " item(s) in "
                    + pairs.Count + " stack(s); " + sendStatus);
                ModCoroutines.Start(this.DailyFurnitureConfirmRoutine(taskId, total));
            }
            catch (Exception ex)
            {
                this.DailyFurnitureReport("Furniture submit failed", false);
                FeatureLog.Fail(DailyFurnitureTag, "confirm handler threw: " + ex);
            }
            finally
            {
                this.dailyFurniturePanel.Clear();
            }
        }

        // The server's answer arrives as a task-state change; same wait Auto Submit uses.
        private IEnumerator DailyFurnitureConfirmRoutine(int taskId, int total)
        {
            yield return ModWait.Realtime(DailyQuestSubmitDelaySeconds);

            if (this.TryGetGameTaskStateAura(taskId, out int state, out _) && state == DailyQuestSubmitStateCanSubmit)
            {
                this.DailyFurnitureReport("Furniture submit was not accepted", false);
                FeatureLog.Fail(DailyFurnitureTag, "taskId " + taskId + " still CanSubmit " + DailyQuestSubmitDelaySeconds + " s after submit");
                yield break;
            }

            this.dailyFurnitureAvailabilityDirty = true;
            this.DailyFurnitureReport("Furniture submitted: " + total + " item(s)", true);
        }

        // Merge every slot's submitItem (netId -> count). A Dictionary enumerates as boxed
        // KeyValuePair<uint, int>: { uint key @0, int value @4 } once unboxed.
        private bool TryReadDailyFurnitureSelection(IntPtr panel, out List<DailyQuestSubmitNetPair> pairs, out string status)
        {
            pairs = new List<DailyQuestSubmitNetPair>();
            status = string.Empty;
            if (auraMonoObjectUnbox == null)
            {
                status = "mono_object_unbox unavailable";
                return false;
            }

            if (!this.TryGetMonoObjectMember(panel, "_slotDisplayDatas", out IntPtr slotList) || slotList == IntPtr.Zero)
            {
                status = "_slotDisplayDatas unavailable (game update?)";
                return false;
            }

            uint listPin = AuraMonoPinNew(slotList);
            List<uint> slotPins = new List<uint>();
            try
            {
                List<IntPtr> slots = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(slotList, slots, slotPins) || slots.Count == 0)
                {
                    status = "no slots";
                    return false;
                }

                for (int s = 0; s < slots.Count; s++)
                {
                    if (slots[s] == IntPtr.Zero
                        || !this.TryGetMonoObjectMember(slots[s], "submitItem", out IntPtr dict)
                        || dict == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint dictPin = AuraMonoPinNew(dict);
                    List<uint> entryPins = new List<uint>();
                    try
                    {
                        // Count first: an EMPTY dictionary makes the enumerator walk fall through to
                        // its raw `_entries` array, whose Entry structs ({hashCode, next, key, value})
                        // would read here as garbage netId/count pairs.
                        IntPtr dictClass = auraMonoObjectGetClass(dict);
                        IntPtr getCount = dictClass != IntPtr.Zero
                            ? this.FindAuraMonoMethodOnHierarchy(dictClass, "get_Count", 0)
                            : IntPtr.Zero;
                        if (getCount == IntPtr.Zero)
                        {
                            status = "submitItem.get_Count unresolved";
                            return false;
                        }

                        int expected = this.GetAuraMonoIntCount(dict, getCount);
                        if (expected <= 0)
                        {
                            continue; // empty slot
                        }

                        List<IntPtr> entries = new List<IntPtr>();
                        if (!this.TryEnumerateAuraMonoCollectionItems(dict, entries, entryPins) || entries.Count != expected)
                        {
                            status = "slot " + s + " enumerated " + entries.Count + " of " + expected + " entries";
                            return false;
                        }

                        for (int k = 0; k < entries.Count; k++)
                        {
                            IntPtr raw = entries[k] != IntPtr.Zero ? auraMonoObjectUnbox(entries[k]) : IntPtr.Zero;
                            if (raw == IntPtr.Zero)
                            {
                                continue;
                            }

                            uint netId = unchecked((uint)Marshal.ReadInt32(raw, 0));
                            int count = Marshal.ReadInt32(raw, 4);
                            if (netId != 0U && count > 0)
                            {
                                pairs.Add(new DailyQuestSubmitNetPair { NetId = netId, Count = count });
                            }
                        }
                    }
                    finally
                    {
                        FreeAuraMonoPins(entryPins);
                        if (dictPin != 0U) { AuraMonoPinFree(dictPin); }
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(slotPins);
                if (listPin != 0U) { AuraMonoPinFree(listPin); }
            }

            this.DailyQuestMergeSubmitPairsByNetId(pairs);
            if (pairs.Count == 0)
            {
                status = "selection is empty";
                return false;
            }

            status = "pairs=" + pairs.Count;
            return true;
        }

        private void DailyFurnitureFail(bool quiet, string kind, string message)
        {
            if (quiet)
            {
                FeatureLog.Once(DailyFurnitureTag, "quiet-" + kind, message);
            }
            else
            {
                FeatureLog.Fail(DailyFurnitureTag, message);
            }
        }

        private void DailyFurnitureReport(string message, bool success)
        {
            this.dailyQuestSubmitLastStatus = message;
            this.AddMenuNotification(message, success ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.45f));
        }
    }
}
