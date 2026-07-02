using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Quest Assistant — Phase 0 (verification) + Phase 1 (safe read-only snapshot) per
    // docs/plans/2026-07-02-quest-assistant.md. Resolves TaskSystem (AuraMono only, matching the
    // BackPackSystem access pattern already used by DailyQuestSubmitFeature.cs), builds a plain
    // managed snapshot of active quests, and classifies each one's objective from
    // completeConditionTrackMarks[] (Tier 1) / completeConditions[] param probing against
    // TableRecipe/TableCookingRecipe (Tier 2, cook-vs-craft fallback).
    //
    // MasterLogQuestAssistant is left ON (see HeartopiaComplete.cs) so a single in-game pass with
    // an active "collect"/"cook"/"craft"/"catch" quest dumps everything Phase 0 needs to confirm:
    // per-task track marks, per-condition checkParam/typeParam/checkParamString, and which Tier-2
    // table (if any) a candidate id matched.
    public partial class HeartopiaComplete
    {
        private const float QuestAssistantMinSecondsBetweenDumps = 1f; // guards accidental rapid re-click, not a background timer
        private const int QuestAssistantStateCanAccept = 2;  // GameTaskState.CanAccept
        private const int QuestAssistantStateAccepted = 3;   // GameTaskState.Accepted
        private const int QuestAssistantStateCanSubmit = 4;  // GameTaskState.CanSubmit
        private const int QuestAssistantCheckTypeKindAccumulate = 4; // CompleteConditionCheckType.KindAccumulate
        private const int QuestAssistantCategoryTaskOrder = 5; // GameTaskType.TaskOrder (order board "Request: X" quests)
        private const int QuestAssistantMarkCategoryNaviPoint = 1; // CompleteConditionTrackMarkType.NaviPoint

        private enum QuestObjectiveKind
        {
            Unknown = 0,
            Collect,
            Cook,
            Craft,
            CatchFish,
            CatchInsect,
            CatchBird,
            HomelandFarm, // SowPlant / UseCropFertilizer — confirmed 2026-07-02, see progress doc §5.5
            TalkToNpc, // InteractWithNpc / EnterDialogNode — added 2026-07-02, see progress doc §13
            SubmitToNpc, // CanSubmit + TableGameTask.submitNpc>0 — added 2026-07-02, see progress doc §24
            GoToArea, // PlayerInSpecificArea — added 2026-07-03, see progress doc §40
        }

        private sealed class ConditionSnapshot
        {
            public string Description;
            public int CheckType;
            public int CheckParam;
            public string CheckParamString;
            public string TypeParam;
            public int Current;
            public int Needed;
            public bool Complete;
        }

        private sealed class TrackMarkSnapshot
        {
            public int MarkCategory;
            public int Id;
            public int GroupId;
            public int LevelId;
        }

        private sealed class QuestSnapshot
        {
            public uint TaskNetId;
            public int TaskId;
            public int State;
            public int Category;
            public int DisplayType;
            public bool IsFailed;
            public string Name;
            public List<ConditionSnapshot> Conditions = new List<ConditionSnapshot>();
            public QuestObjectiveKind ObjectiveKind;
            public int ObjectiveTargetId;
            // Every id that qualifies for ObjectiveKind (usually just [ObjectiveTargetId], but a
            // "collect any of these N" condition can list several — e.g. "Collect 15 Common
            // Mushrooms" carries all 6 mushroom item ids in one typeParam). Action buttons should
            // loop this list (e.g. enable radar for every matched id), not just the first. Added
            // 2026-07-02 after that exact quest only enabled Oyster Mushroom instead of all mushroom
            // types — see progress doc §10.
            public List<int> ObjectiveTargetIds = new List<int>();
            // 0 unless classified via Tier 1 (trackMark). Collection(5) ids are in the radar's known
            // collectable-atlas space; DynamicMapResource(13) ids are a DIFFERENT space not covered by
            // that atlas — a future radar-mapping step (plan Phase 3) must branch on this, not just on
            // ObjectiveKind==Collect. See progress doc §1/§5.4.
            public int ObjectiveTrackMarkCategory;
            // TableGameTask.submitNpc — the NPC a CanSubmit quest hands its items to (0 if none).
            // Drives ObjectiveKind.SubmitToNpc; the submit wire command carries this STATIC id (not a
            // netId), so no streaming/teleport needed. See progress doc §24.
            public int SubmitNpcId;
        }

        // Class/method pointers only — safe to cache raw (image lifetime, per AGENTS.md §9). The
        // TaskSystem *instance* is never cached across ticks (CI lint E3): it's re-resolved via
        // TryResolveAuraMonoModule every pass and used-and-discarded within one synchronous call,
        // matching how DailyQuestSubmitFeature.cs handles BackPackSystem.
        private IntPtr questAssistantTaskSystemClass = IntPtr.Zero;
        private IntPtr questAssistantGetAllTasksMethod = IntPtr.Zero;

        private bool questAssistantBusy = false;
        private float questAssistantLastDumpAt = -999f;

        private List<QuestSnapshot> questAssistantSnapshot = new List<QuestSnapshot>();
        private List<QuestSnapshot> questAssistantAvailable = new List<QuestSnapshot>();
        private string questAssistantLastStatus = "Idle. Load into a town, then click the button below.";

        // ===== Entry point — purely button-triggered. No OnUpdate TIMER: this must never run before
        // the user explicitly asks for it (world/session data isn't guaranteed to exist at mod load /
        // main menu — AGENTS.md §5, "many assemblies load only after entering a town"). The auto-
        // refresh below is a SEPARATE, event-driven mechanism (not a timer) — see its own comment for
        // why it doesn't reopen that risk. =====

        private void QuestAssistantOnDumpButtonClicked()
        {
            float now = Time.unscaledTime;
            if (this.questAssistantBusy || now - this.questAssistantLastDumpAt < QuestAssistantMinSecondsBetweenDumps)
            {
                return;
            }

            this.questAssistantBusy = true;
            this.questAssistantLastDumpAt = now;
            try
            {
                this.QuestAssistantResolveSnapshot(true);
            }
            catch (Exception ex)
            {
                this.questAssistantLastStatus = "Error: " + ex.Message;
                this.QuestAssistantLog("EXCEPTION during resolve: " + ex);
            }
            finally
            {
                this.questAssistantBusy = false;
            }
        }

        // ===== Auto-refresh (2026-07-02, progress doc §30) — event-driven, NOT a timer. =====
        //
        // Hooks XDTDataAndProtocol.Events.TaskUpdated{uint taskNetId; int taskStaticId} (8 bytes).
        // TaskSystem dispatches this from ALL THREE of its GameTaskItem lifecycle handlers
        // (OnCreateTaskItem/OnUpdateTaskItem/OnRemoveTaskItem — i.e. accept, any state change,
        // submit, and removal all go through here — ilspy-dumps/.../TaskSystem.cs), so it's a single,
        // comprehensive "something about the quest list changed" signal — no need to also hook
        // TaskAccepted/TaskStateChanged/TaskSubmitted separately.
        //
        // Why this does NOT reopen the startup-crash risk the button-only rule above guards against:
        // RegisterGameEventHook only records handler metadata — it touches no AuraMono API at
        // registration time. The actual native detour installs lazily from OnUpdate, gated behind
        // EnsureAuraMonoApiReady() (HeartopiaComplete.EventHook.cs); and TaskUpdated can only fire
        // from TaskSystem's OWN already-running code, which is strictly stronger proof the world/
        // session exists than an unconditional timer tick ever gave.
        //
        // Burst safety: on every town/session load, TaskSystem.InitTaskData() replays the ENTIRE
        // quest list through OnUpdateTaskItem in one synchronous pass (confirmed in the decompile) —
        // i.e. 600-700+ TaskUpdated dispatches in a single frame. The handler below only flips a bool
        // (near-zero cost per event, and the fixed-size event ring naturally coalesces a same-frame
        // burst down to whatever the next OnUpdate drain sees); the actual resolve stays on a
        // throttled tick, mirroring the Collect/TalkToNpc monitor pattern, so a burst — whether from
        // world load or from this mod's own Accept-All — causes at most one resolve per throttle
        // window, not one per event.
        private const string QuestAssistantTaskUpdatedEventName = "XDTDataAndProtocol.Events.TaskUpdated";
        private const int QuestAssistantTaskUpdatedEventBytes = 8; // uint taskNetId@0, int taskStaticId@4
        private const float QuestAssistantAutoRefreshMinIntervalSeconds = 1.5f;

        private bool questAssistantAutoRefreshHookRegistered = false;
        private bool questAssistantAutoRefreshDirty = false;
        private float questAssistantAutoRefreshNextAllowedAt = 0f;

        // Checked from OnUpdate but cheap every frame it isn't dirty (one bool read) — same
        // reachable-only-after-the-mod-is-already-running shape as the Collect/TalkToNpc monitors.
        private void QuestAssistantAutoRefreshOnUpdate()
        {
            if (!this.questAssistantAutoRefreshHookRegistered)
            {
                this.questAssistantAutoRefreshHookRegistered = true;
                this.RegisterGameEventHook(QuestAssistantTaskUpdatedEventName, QuestAssistantTaskUpdatedEventBytes, this.QuestAssistantOnTaskUpdatedEvent);
            }

            if (!this.questAssistantAutoRefreshDirty || this.questAssistantBusy)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.questAssistantAutoRefreshNextAllowedAt)
            {
                return;
            }

            this.questAssistantAutoRefreshDirty = false;
            this.questAssistantAutoRefreshNextAllowedAt = now + QuestAssistantAutoRefreshMinIntervalSeconds;

            // No point silently rescanning 600+ tasks in the background when nobody's looking — only
            // refresh if the floating window is open or a manual dump has already happened this
            // session (the first-ever resolve of a session still always originates from the explicit
            // button click, never from this tick alone).
            if (!this.questAssistantWindowVisible && this.questAssistantLastDumpAt < 0f)
            {
                return;
            }

            try
            {
                this.QuestAssistantResolveSnapshot(false);
                this.QuestAssistantLog("Auto-refresh: resolved after TaskUpdated event(s)");
            }
            catch (Exception ex)
            {
                this.QuestAssistantLog("Auto-refresh EXCEPTION: " + ex);
            }
        }

        // Handler runs on the Unity main thread (EventHook engine's OnUpdate drain) — deliberately
        // does nothing but flip a flag; see the burst-safety note above for why.
        private void QuestAssistantOnTaskUpdatedEvent(GameEventSnapshot e)
        {
            this.questAssistantAutoRefreshDirty = true;
        }

        private object questAssistantAcceptAllCoroutine = null;

        // Reuses TryInvokeDailyQuestClientAcceptTaskAura (DailyQuestSubmitFeature.cs) as-is — despite
        // the name it just resolves/invokes TaskProtocolManager.ClientAcceptTask(taskId), which is
        // generic (not scoped to the daily order board). Confirmed 2026-07-02: GetAllTasks() already
        // lists CanAccept(2) quests for any quest type, so this works for the same set this file's
        // "available" list already resolves — no separate daily-order-specific API needed.
        private void QuestAssistantOnAcceptAllClicked()
        {
            if (this.questAssistantAcceptAllCoroutine != null || this.questAssistantAvailable == null || this.questAssistantAvailable.Count == 0)
            {
                return;
            }

            this.questAssistantAcceptAllCoroutine = ModCoroutines.Start(this.QuestAssistantAcceptAllRoutine());
        }

        private System.Collections.IEnumerator QuestAssistantAcceptAllRoutine()
        {
            List<QuestSnapshot> targets = new List<QuestSnapshot>(this.questAssistantAvailable);
            this.questAssistantLastStatus = "Accepting " + targets.Count + " quest(s)...";
            int accepted = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                int taskId = targets[i].TaskId;
                if (this.TryInvokeDailyQuestClientAcceptTaskAura(taskId, out string status))
                {
                    accepted++;
                    this.QuestAssistantLog("Accept ok taskId=" + taskId + " " + status);
                }
                else
                {
                    this.QuestAssistantLog("Accept failed taskId=" + taskId + ": " + status);
                }

                yield return new WaitForSecondsRealtime(0.3f);
            }

            this.questAssistantLastStatus = "Accepted " + accepted + "/" + targets.Count + " quest(s)";
            yield return new WaitForSecondsRealtime(0.5f);
            this.QuestAssistantResolveSnapshot(false);
            this.questAssistantAcceptAllCoroutine = null;
        }

        // "Submit Ready Items" reuses DailyQuestSubmitFeature.StartDailyQuestAutoSubmitItems as-is —
        // no new submit logic. It already: finds every order in CanSubmit state (not just the
        // Quest Assistant active list — it re-derives its own order list from DailyOrderSystem), fills
        // from bag/warehouse, honors the existing "Skip 5 Star Items" setting
        // (dailyQuestSubmitSkipFiveStar, configured on the Daily Quests tab), and shows its own
        // AddMenuNotification on completion. This is a plain wiring call, requested 2026-07-02 after
        // the user pointed at that exact feature to reuse rather than duplicate.
        private int QuestAssistantCountReadyToSubmit()
        {
            int count = 0;
            if (this.questAssistantSnapshot != null)
            {
                for (int i = 0; i < this.questAssistantSnapshot.Count; i++)
                {
                    if (this.questAssistantSnapshot[i].State == QuestAssistantStateCanSubmit)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private bool QuestAssistantIsDailyQuestSubmitBusy()
        {
            return this.dailyQuestSubmitCoroutine != null || this.birdPhotoSubmitCoroutine != null || this.dailyClaimsCoroutine != null;
        }

        private void QuestAssistantLog(string message)
        {
            if (!MasterLogQuestAssistant || string.IsNullOrEmpty(message))
            {
                return;
            }

            ModLogger.Msg("[QuestAssistant] " + message);
        }

        // ===== Resolution =====

        private void QuestAssistantResolveSnapshot(bool verboseDump)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                this.questAssistantLastStatus = "AuraMono unavailable";
                return;
            }

            if (!this.QuestAssistantEnsureTaskSystem(out IntPtr taskSystemObj, out string taskSysStatus) || taskSystemObj == IntPtr.Zero)
            {
                this.questAssistantLastStatus = "TaskSystem: " + taskSysStatus;
                this.QuestAssistantLog("TaskSystem unavailable: " + taskSysStatus);
                return;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr listObj = auraMonoRuntimeInvoke(this.questAssistantGetAllTasksMethod, taskSystemObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
            {
                this.questAssistantLastStatus = "GetAllTasks failed";
                this.QuestAssistantLog("GetAllTasks invoke failed exc=" + (exc != IntPtr.Zero ? "0x" + exc.ToInt64().ToString("X") : "(null result)"));
                return;
            }

            List<IntPtr> taskItems = new List<IntPtr>();
            List<uint> taskPins = new List<uint>();
            List<QuestSnapshot> resolved = new List<QuestSnapshot>();
            List<QuestSnapshot> available = new List<QuestSnapshot>();
            int activeCount = 0;
            int availableCount = 0;
            int totalCount = 0;

            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, taskItems, taskPins))
                {
                    this.questAssistantLastStatus = "GetAllTasks: 0 tasks";
                    this.questAssistantSnapshot = resolved;
                    this.questAssistantAvailable = available;
                    return;
                }

                totalCount = taskItems.Count;
                this.QuestAssistantLog("=== resolve pass: " + totalCount + " raw task item(s), verboseDump=" + verboseDump + " ===");

                for (int i = 0; i < taskItems.Count; i++)
                {
                    IntPtr boxedGameTaskItem = taskItems[i];
                    if (boxedGameTaskItem == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.QuestAssistantTryResolveTask(boxedGameTaskItem, verboseDump, out QuestSnapshot snapshot, out bool isActive, out bool isAvailable))
                    {
                        continue;
                    }

                    if (isActive)
                    {
                        activeCount++;
                        resolved.Add(snapshot);
                    }
                    else if (isAvailable)
                    {
                        availableCount++;
                        available.Add(snapshot);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(taskPins);
            }

            this.questAssistantSnapshot = resolved;
            this.questAssistantAvailable = available;
            this.questAssistantLastStatus = "OK: " + activeCount + "/" + totalCount + " active, " + availableCount + " available to accept";
            this.QuestAssistantLog("=== resolve pass done: " + activeCount + " active, " + availableCount + " available, of " + totalCount + " total ===");
        }

        // Re-resolves the TaskSystem *instance* every call (cheap DataModule&lt;T&gt;.Instance lookup) —
        // the returned pointer must be used-and-discarded within this same synchronous call, never
        // stored in a field (CI lint E3: a cross-frame MonoObject* raw field is collected by bdwgc
        // once the game drops its own reference). Only the class/method pointers are cached.
        private bool QuestAssistantEnsureTaskSystem(out IntPtr taskSystemObj, out string status)
        {
            status = "ok";
            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Quest.TaskSystem", out taskSystemObj) || taskSystemObj == IntPtr.Zero)
            {
                status = "TaskSystem module unresolved";
                return false;
            }

            if (this.questAssistantGetAllTasksMethod != IntPtr.Zero)
            {
                return true;
            }

            IntPtr taskSystemClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(taskSystemObj) : IntPtr.Zero;
            if (taskSystemClass == IntPtr.Zero)
            {
                status = "TaskSystem class unresolved";
                return false;
            }

            IntPtr getAllTasksMethod = this.FindAuraMonoMethodOnHierarchy(taskSystemClass, "GetAllTasks", 0);
            if (getAllTasksMethod == IntPtr.Zero)
            {
                status = "GetAllTasks method missing";
                return false;
            }

            this.questAssistantTaskSystemClass = taskSystemClass;
            this.questAssistantGetAllTasksMethod = getAllTasksMethod;
            this.QuestAssistantLog("TaskSystem resolved OK");
            return true;
        }

        // taskItemComponent.Progresses is read via the raw mono array API rather than invoking
        // GameTaskItemComponent.CalcProgresses, to avoid the value-type-instance-method invoke
        // calling convention question (mono_runtime_invoke on a value-type method expects the
        // unboxed data pointer as `obj`, not the boxed object — untested here, so sidestepped).
        // KindAccumulate's popcount is trivial pure C# and reimplemented in QuestAssistantPopCount.
        private unsafe bool QuestAssistantTryResolveTask(IntPtr boxedGameTaskItem, bool verboseDump, out QuestSnapshot snapshot, out bool isActive, out bool isAvailable)
        {
            snapshot = null;
            isActive = false;
            isAvailable = false;

            if (!this.TryGetMonoUInt32Member(boxedGameTaskItem, "taskNetId", out uint taskNetId))
            {
                this.QuestAssistantLog("  skip: taskNetId unreadable");
                return false;
            }

            if (!this.TryGetMonoObjectMember(boxedGameTaskItem, "taskItemComponent", out IntPtr boxedComponent) || boxedComponent == IntPtr.Zero)
            {
                this.QuestAssistantLog("  skip taskNetId=" + taskNetId + ": taskItemComponent unreadable");
                return false;
            }

            uint componentPin = AuraMonoPinNew(boxedComponent);
            try
            {
                if (!this.TryGetMonoIntMember(boxedComponent, "TaskId", out int taskId) || taskId <= 0)
                {
                    this.QuestAssistantLog("  skip taskNetId=" + taskNetId + ": TaskId unreadable");
                    return false;
                }

                int stateInt = this.TryGetMonoIntMember(boxedComponent, "TaskState", out int s) ? s : -1;
                bool isFailed = this.TryGetMonoBoolMember(boxedComponent, "IsFailed", out bool failed) && failed;

                // Cheap pre-filter on state alone (displayType isn't known yet — that needs the row
                // resolve below). This only rules out Finished/Closed/etc. CanAccept(2) is included
                // 2026-07-02: confirmed via a real dump that GetAllTasks() already lists not-yet-
                // accepted quests (total count didn't change across accept), so no separate
                // "available quests" discovery API is needed — see progress doc §10.
                bool stateIsCandidate =
                    (stateInt == QuestAssistantStateCanAccept || stateInt == QuestAssistantStateAccepted || stateInt == QuestAssistantStateCanSubmit)
                    && !isFailed;
                if (!stateIsCandidate)
                {
                    if (verboseDump)
                    {
                        this.QuestAssistantLog("  skip taskId=" + taskId + " netId=" + taskNetId + " state=" + stateInt + " failed=" + isFailed + ": not active");
                    }

                    return false;
                }

                this.TryGetMonoObjectMember(boxedComponent, "Progresses", out IntPtr progressesArrayObj);
                int progressesLength = 0;
                if (progressesArrayObj != IntPtr.Zero && auraMonoArrayLength != null)
                {
                    progressesLength = (int)Math.Min(auraMonoArrayLength(progressesArrayObj).ToUInt64(), 256UL);
                }

                if (!this.TryGetDailyQuestGameTaskRowPtrAura(taskId, out IntPtr gameTaskRow, out string rowStatus) || gameTaskRow == IntPtr.Zero)
                {
                    this.QuestAssistantLog("  skip taskId=" + taskId + " netId=" + taskNetId + ": GetGameTask failed (" + rowStatus + ")");
                    return false;
                }

                uint rowPin = AuraMonoPinNew(gameTaskRow);
                try
                {
                    int category = this.TryGetMonoIntMember(gameTaskRow, "type", out int t) ? t : -1;
                    int displayType = this.TryGetMonoIntMember(gameTaskRow, "displayType", out int d) ? d : -1;
                    string name = this.TryGetMonoStringMember(gameTaskRow, "taskName", out string n) ? n : ("task#" + taskId);

                    if (stateInt == QuestAssistantStateCanAccept)
                    {
                        // Available-to-accept bucket: no displayType filter yet (unverified whether
                        // CanAccept suffers the same noise problem Accepted did — ship the simple
                        // version, correct if the next dump shows junk entries here too). Skip the
                        // conditions/trackMarks read entirely — not needed for an Accept button, and
                        // cuts native-call volume for what's typically a short list.
                        isAvailable = true;
                        snapshot = new QuestSnapshot
                        {
                            TaskNetId = taskNetId,
                            TaskId = taskId,
                            State = stateInt,
                            IsFailed = isFailed,
                            Name = name,
                            Category = category,
                            DisplayType = displayType,
                        };

                        if (verboseDump)
                        {
                            this.QuestAssistantLog("  available taskId=" + taskId + " netId=" + taskNetId + " category=" + category + " name=\"" + name + "\"");
                        }

                        return true;
                    }

                    // Active-quest filter — vanilla rule (GameTaskModule.GetTasks: state + displayType
                    // gate). The CanSubmit term below was inverted since §6 (bug, not a game-state
                    // edge case) — see progress doc §28 for the re-derivation. Vanilla's actual code is
                    // written as an EXCLUDE condition ("continue" = skip):
                    //   if ((displayType != 0 && (uint)(displayType - 3) > 1u) || 1==0) continue;
                    // so the INCLUDE set is the negation: displayType==0 || (uint)(displayType-3) <= 1u
                    // (i.e. displayType ∈ {0,3,4}) — NOT "> 1u", which is what this line had (silently
                    // wrong since no CanSubmit quest with displayType∈{3,4} had been tested until
                    // "Naughty's Treasure" — every prior CanSubmit test quest happened to have
                    // displayType=0, which passes either way and never exercised this term).
                    isActive =
                        (stateInt == QuestAssistantStateAccepted && (displayType == 0 || displayType == 4))
                        || (stateInt == QuestAssistantStateCanSubmit && (displayType == 0 || (uint)(displayType - 3) <= 1u));

                    if (!isActive)
                    {
                        if (verboseDump)
                        {
                            // name/category logged HERE (unlike the plain "not active" skip above,
                            // which never reads the row at all) so a gate-excluded-but-Accepted/
                            // CanSubmit task can be identified by name from one dump — added 2026-07-02
                            // to check whether "Naughty's Treasure" (visible in-game under Gossips,
                            // missing from Quest Assistant) is being cut here. See progress doc §28.
                            this.QuestAssistantLog("  skip taskId=" + taskId + " netId=" + taskNetId + " state=" + stateInt + " category=" + category + " displayType=" + displayType + " name=\"" + name + "\": not active (displayType gate)");
                        }

                        return false;
                    }

                    List<ConditionSnapshot> conditions = this.QuestAssistantReadConditions(gameTaskRow, progressesArrayObj, progressesLength);
                    List<TrackMarkSnapshot> trackMarks = this.QuestAssistantReadTrackMarks(gameTaskRow);

                    snapshot = new QuestSnapshot
                    {
                        TaskNetId = taskNetId,
                        TaskId = taskId,
                        State = stateInt,
                        IsFailed = isFailed,
                        Name = name,
                        Category = category,
                        DisplayType = displayType,
                        Conditions = conditions,
                        SubmitNpcId = this.TryGetMonoIntMember(gameTaskRow, "submitNpc", out int submitNpc) ? submitNpc : 0,
                    };

                    this.QuestAssistantClassify(trackMarks, snapshot);

                    if (verboseDump)
                    {
                        this.QuestAssistantLogTaskDetail(snapshot, trackMarks);
                    }

                    return true;
                }
                finally
                {
                    AuraMonoPinFree(rowPin);
                }
            }
            finally
            {
                AuraMonoPinFree(componentPin);
            }
        }

        private unsafe List<ConditionSnapshot> QuestAssistantReadConditions(IntPtr gameTaskRow, IntPtr progressesArrayObj, int progressesLength)
        {
            List<ConditionSnapshot> result = new List<ConditionSnapshot>();

            if (!this.TryGetMonoObjectMember(gameTaskRow, "completeConditions", out IntPtr condArrayObj) || condArrayObj == IntPtr.Zero)
            {
                return result;
            }

            List<string> descriptions = this.QuestAssistantReadStringArrayProperty(gameTaskRow, "completeConditionDescription");

            List<IntPtr> condItems = new List<IntPtr>();
            List<uint> condPins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(condArrayObj, condItems, condPins))
                {
                    return result;
                }

                for (int i = 0; i < condItems.Count; i++)
                {
                    IntPtr condObj = condItems[i];
                    if (condObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    int checkType = this.TryGetMonoIntMember(condObj, "checkType", out int ct) ? ct : 0;
                    int checkParam = this.TryGetMonoIntMember(condObj, "checkParam", out int cp) ? cp : 0;
                    string checkParamString = this.TryGetMonoStringMember(condObj, "checkParamString", out string cps) ? cps : string.Empty;
                    string typeParam = this.TryGetMonoStringMember(condObj, "typeParam", out string tp) ? tp : string.Empty;
                    int neededProgress = this.TryGetMonoIntMember(condObj, "neededProgress", out int np) ? np : 0;

                    int current = 0;
                    if (progressesArrayObj != IntPtr.Zero && i < progressesLength && auraMonoArrayAddrWithSize != null)
                    {
                        IntPtr elemAddr = auraMonoArrayAddrWithSize(progressesArrayObj, sizeof(int), (UIntPtr)i);
                        int raw = elemAddr != IntPtr.Zero ? Marshal.ReadInt32(elemAddr) : 0;
                        current = checkType == QuestAssistantCheckTypeKindAccumulate ? QuestAssistantPopCount((uint)raw) : raw;
                    }

                    result.Add(new ConditionSnapshot
                    {
                        Description = i < descriptions.Count ? descriptions[i] : string.Empty,
                        CheckType = checkType,
                        CheckParam = checkParam,
                        CheckParamString = checkParamString,
                        TypeParam = typeParam,
                        Current = current,
                        Needed = neededProgress,
                        Complete = neededProgress > 0 && current >= neededProgress,
                    });
                }
            }
            finally
            {
                FreeAuraMonoPins(condPins);
            }

            return result;
        }

        private List<TrackMarkSnapshot> QuestAssistantReadTrackMarks(IntPtr gameTaskRow)
        {
            List<TrackMarkSnapshot> result = new List<TrackMarkSnapshot>();
            if (!this.TryGetMonoObjectMember(gameTaskRow, "completeConditionTrackMarks", out IntPtr arrayObj) || arrayObj == IntPtr.Zero)
            {
                return result;
            }

            List<IntPtr> items = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(arrayObj, items, pins))
                {
                    return result;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr markObj = items[i];
                    if (markObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    result.Add(new TrackMarkSnapshot
                    {
                        MarkCategory = this.TryGetMonoIntMember(markObj, "markCategory", out int mc) ? mc : 0,
                        Id = this.TryGetMonoIntMember(markObj, "id", out int id) ? id : 0,
                        GroupId = this.TryGetMonoIntMember(markObj, "groupId", out int gid) ? gid : 0,
                        LevelId = this.TryGetMonoIntMember(markObj, "levelId", out int lid) ? lid : 0,
                    });
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            return result;
        }

        private List<string> QuestAssistantReadStringArrayProperty(IntPtr obj, string propertyName)
        {
            List<string> result = new List<string>();
            if (!this.TryGetMonoObjectMember(obj, propertyName, out IntPtr arrayObj) || arrayObj == IntPtr.Zero)
            {
                return result;
            }

            List<IntPtr> items = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(arrayObj, items, pins))
                {
                    return result;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    result.Add(this.TryReadMonoString(items[i], out string s) ? s : string.Empty);
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            return result;
        }

        // ===== Classification (Tier 1: track marks, Tier 2: checkParamString allowlist) =====
        //
        // Redesigned 2026-07-02 after a real-account dump (669 quests) showed the original Tier 2
        // (blindly testing every condition's checkParam/typeParam as a candidate id against
        // TableCookingRecipe/TableRecipe) produced false positives: e.g. "Enable Friend Chat"
        // (checkParamString="SocialFriendLevelUpgrade") was wrongly classified Craft because its
        // checkParam (30204) coincidentally matched a real TableRecipe id. See progress doc §2/§3.
        //
        // Fix: Tier 2 now keys off checkParamString (the condition's event name) FIRST via an
        // allowlist, and only probes recipe tables for event names empirically confirmed to carry a
        // real item/recipe id. Unlisted event names contribute no candidates at all (default-deny,
        // not a blocklist — new/unseen event names should NOT be probed blind).

        // Unambiguous event names -> direct ObjectiveKind, no table probe needed (confirmed via the
        // 2026-07-02 checkParamString frequency census across all 669 quests).
        private static readonly Dictionary<string, QuestObjectiveKind> QuestAssistantDirectEventKinds =
            new Dictionary<string, QuestObjectiveKind>(StringComparer.Ordinal)
        {
            { "CollectItem", QuestObjectiveKind.Collect },
            { "CaughtFish", QuestObjectiveKind.CatchFish },
            { "CatchingInsect", QuestObjectiveKind.CatchInsect },
            { "BirdWatchingSuccessPhoto", QuestObjectiveKind.CatchBird },
            { "CookOutFood", QuestObjectiveKind.Cook },
            { "CookWithGreatIngredient", QuestObjectiveKind.Cook },
            { "CookWithMagicFlavour", QuestObjectiveKind.Cook },
            { "SowPlant", QuestObjectiveKind.HomelandFarm },
            { "UseCropFertilizer", QuestObjectiveKind.HomelandFarm },
            { "InteractWithNpc", QuestObjectiveKind.TalkToNpc },
            { "EnterDialogNode", QuestObjectiveKind.TalkToNpc },
            // "Go to <area>" quests (e.g. "Astralis in Fishing Village") — typeParam is the area /
            // navigation-point id (matches the NaviPoint(1) trackMark id). Satisfied by being
            // physically in the area, so the action teleports to the marked map spot. See §40.
            { "PlayerInSpecificArea", QuestObjectiveKind.GoToArea },
        };

        // Event names whose numeric params were CONFIRMED (not just plausible) to be a real item/
        // recipe id in the 2026-07-02 dump — safe to probe against TableCookingRecipe/TableRecipe.
        // Anything not in this set is skipped entirely (see redesign note above).
        private static readonly HashSet<string> QuestAssistantItemBackedEventNames =
            new HashSet<string>(StringComparer.Ordinal)
        {
            "BagItemCount",
            "ItemGain",
            "FurnitureSpecificCheck",
            "RecipeState",
            "Place",
            "BuildMaterialState",
            "PictorialState",
        };

        // A condition (by 0-based index) counts as complete when Current >= Needed (Needed>0). Used
        // to skip finished steps of a multi-step quest so classification targets the ACTIVE step (§45).
        private static bool QuestAssistantConditionComplete(QuestSnapshot snapshot, int conditionIndex)
        {
            if (snapshot.Conditions == null || conditionIndex < 0 || conditionIndex >= snapshot.Conditions.Count)
            {
                return false;
            }

            ConditionSnapshot c = snapshot.Conditions[conditionIndex];
            return c.Needed > 0 && c.Current >= c.Needed;
        }

        private void QuestAssistantClassify(List<TrackMarkSnapshot> trackMarks, QuestSnapshot snapshot)
        {
            snapshot.ObjectiveKind = QuestObjectiveKind.Unknown;
            snapshot.ObjectiveTargetId = 0;
            snapshot.ObjectiveTargetIds = new List<int>();
            snapshot.ObjectiveTrackMarkCategory = 0;

            // CanSubmit + an NPC to hand items to => the actionable step is "submit items to that
            // NPC", regardless of the original objective (already satisfied at CanSubmit). submitNpc
            // is a TableGameTask field read up front (not from a condition), and the submit wire
            // command carries it as a STATIC id, so this needs no NPC streaming/teleport (unlike
            // TalkToNpc). Takes priority precisely because it's the one remaining action. §24.
            if (snapshot.State == QuestAssistantStateCanSubmit && snapshot.SubmitNpcId > 0)
            {
                snapshot.ObjectiveKind = QuestObjectiveKind.SubmitToNpc;
                snapshot.ObjectiveTargetId = snapshot.SubmitNpcId;
                snapshot.ObjectiveTargetIds = new List<int> { snapshot.SubmitNpcId };
                this.QuestAssistantLog("  classify taskId=" + snapshot.TaskId + ": CanSubmit + submitNpc=" + snapshot.SubmitNpcId + " -> SubmitToNpc");
                return;
            }

            for (int i = 0; i < trackMarks.Count; i++)
            {
                TrackMarkSnapshot mark = trackMarks[i];

                // Skip a trackMark whose condition (groupId-1, 1-based) is already COMPLETE — for a
                // multi-step quest we want the ACTIVE step, not a finished earlier one. E.g.
                // "Gardening: Fertilizer": the Furniture trackMark belongs to the done "Make fertilizer
                // at the Workbench" condition (2/2), so classifying Craft off it is wrong — the active
                // step is "Apply fertilizer" (condition[1], UseCropFertilizer → HomelandFarm), which
                // Tier 2 then picks up. §45.
                if (QuestAssistantConditionComplete(snapshot, mark.GroupId - 1))
                {
                    continue;
                }

                QuestObjectiveKind kind = QuestAssistantMapTrackMarkCategory(mark.MarkCategory);
                if (kind == QuestObjectiveKind.Unknown)
                {
                    continue;
                }

                // A "collect/catch any of these" objective can carry ONE track mark PER valid target
                // (e.g. "Collect 15 Common Mushrooms" has 6 separate Collection track marks, one per
                // mushroom species, all groupId=1) — collect every mark that resolves to the SAME
                // kind, not just the first. 2026-07-02 fix: the earlier version returned right after
                // the first match, so the button only ever enabled Oyster Mushroom's radar category
                // instead of all 6 — see progress doc §11 (this is the Tier-1 counterpart to the
                // Tier-2 multi-id fix from §10.2, which only covered the direct-event path).
                List<int> ids = new List<int>();
                for (int j = 0; j < trackMarks.Count; j++)
                {
                    if (QuestAssistantMapTrackMarkCategory(trackMarks[j].MarkCategory) == kind && !ids.Contains(trackMarks[j].Id))
                    {
                        ids.Add(trackMarks[j].Id);
                    }
                }

                snapshot.ObjectiveKind = kind;
                snapshot.ObjectiveTargetId = ids.Count > 0 ? ids[0] : mark.Id;
                snapshot.ObjectiveTargetIds = ids;
                snapshot.ObjectiveTrackMarkCategory = mark.MarkCategory;

                // Craft-specific correction (2026-07-02, progress doc §34): Furniture(12) trackMarks
                // recur across MANY different craft quests with the SAME id (330001) — confirmed via
                // dump this is the WORKBENCH furniture id (a "craft at this station" location marker),
                // not the recipe/output item to make. The real target is the condition's own
                // typeParam/checkParam (e.g. "Fishing: Bait" has typeParam="20511", checkParamString=
                // "ItemMade", and 20511 resolves via TableData.GetRecipe — confirmed the SAME id space
                // TableRecipe.id/output-item share). Prefer that over the trackMark id whenever it
                // resolves as a real recipe, so the eventual Craft action targets the right recipe.
                if (kind == QuestObjectiveKind.Craft)
                {
                    for (int c = 0; c < snapshot.Conditions.Count; c++)
                    {
                        List<int> recipeCandidates = QuestAssistantNumericCandidates(snapshot.Conditions[c]);
                        for (int r = 0; r < recipeCandidates.Count; r++)
                        {
                            if (this.QuestAssistantTryProbeIdInTable("GetRecipe", recipeCandidates[r]))
                            {
                                snapshot.ObjectiveTargetId = recipeCandidates[r];
                                snapshot.ObjectiveTargetIds = new List<int> { recipeCandidates[r] };
                                this.QuestAssistantLog("  classify taskId=" + snapshot.TaskId + ": Craft target corrected trackMark id=" + mark.Id + " (workbench) -> recipe id=" + recipeCandidates[r]);
                                break;
                            }
                        }

                        if (snapshot.ObjectiveTargetId != mark.Id)
                        {
                            break;
                        }
                    }
                }

                this.QuestAssistantLog("  classify taskId=" + snapshot.TaskId + ": tier1 trackMark category=" + mark.MarkCategory + " ids=[" + string.Join(",", ids) + "] -> " + kind);
                return;
            }

            for (int i = 0; i < snapshot.Conditions.Count; i++)
            {
                ConditionSnapshot cond = snapshot.Conditions[i];
                // Prefer the active step: skip a condition that's already complete (§45).
                if (QuestAssistantConditionComplete(snapshot, i))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(cond.CheckParamString)
                    || !QuestAssistantDirectEventKinds.TryGetValue(cond.CheckParamString, out QuestObjectiveKind directKind))
                {
                    continue;
                }

                // GoToArea target = the NaviPoint(1) trackMark's id, NOT the condition's typeParam
                // (2026-07-03, progress doc §44). The game teleport marker uses
                // GetNavigationPoint(trackMark.id) (MapSpotsSystem.UpdateTaskMapSpot), while the
                // PlayerInSpecificArea condition's typeParam is the area/trigger id for the CHECK — a
                // DIFFERENT id space. They coincided for "Fishing Village" (both 144, hiding the
                // distinction), but "Onsen Mountain" has trackMark id=5001003 ≠ typeParam=1231, and
                // navpoint config has no 1231. Prefer the NaviPoint trackMark id; fall back to
                // typeParam only if there's no NaviPoint trackMark.
                if (directKind == QuestObjectiveKind.GoToArea)
                {
                    int navId = 0;
                    for (int t = 0; t < trackMarks.Count; t++)
                    {
                        if (trackMarks[t].MarkCategory == QuestAssistantMarkCategoryNaviPoint && trackMarks[t].Id > 0)
                        {
                            navId = trackMarks[t].Id;
                            break;
                        }
                    }

                    if (navId > 0)
                    {
                        snapshot.ObjectiveKind = QuestObjectiveKind.GoToArea;
                        snapshot.ObjectiveTargetId = navId;
                        snapshot.ObjectiveTargetIds = new List<int> { navId };
                        this.QuestAssistantLog("  classify taskId=" + snapshot.TaskId + ": tier2 \"PlayerInSpecificArea\" -> GoToArea navpoint(trackMark) id=" + navId);
                        return;
                    }
                }

                // A "collect any of these N" condition lists every qualifying id in typeParam (e.g.
                // "Collect 15 Common Mushrooms" carries all 6 mushroom ids) — keep all of them, not
                // just the first, so action buttons can act on every one (2026-07-02 fix, progress
                // doc §10; previously only the first/Oyster Mushroom got its radar category enabled).
                List<int> directCandidates = QuestAssistantNumericCandidates(cond);
                snapshot.ObjectiveKind = directKind;
                snapshot.ObjectiveTargetId = directCandidates.Count > 0 ? directCandidates[0] : 0;
                snapshot.ObjectiveTargetIds = directCandidates;
                this.QuestAssistantLog("  classify taskId=" + snapshot.TaskId + ": tier2 direct event \"" + cond.CheckParamString + "\" -> " + directKind + " targets=[" + string.Join(",", directCandidates) + "]");
                return;
            }

            for (int i = 0; i < snapshot.Conditions.Count; i++)
            {
                ConditionSnapshot cond = snapshot.Conditions[i];
                if (string.IsNullOrEmpty(cond.CheckParamString) || !QuestAssistantItemBackedEventNames.Contains(cond.CheckParamString))
                {
                    continue;
                }

                List<int> candidates = QuestAssistantNumericCandidates(cond);
                if (candidates.Count == 0)
                {
                    continue;
                }

                this.QuestAssistantLog("  classify taskId=" + snapshot.TaskId + ": tier2 item-backed event \"" + cond.CheckParamString + "\" candidates=[" + string.Join(",", candidates) + "]");

                for (int c = 0; c < candidates.Count; c++)
                {
                    if (this.QuestAssistantTryProbeIdInTable("GetCookingRecipe", candidates[c]))
                    {
                        snapshot.ObjectiveKind = QuestObjectiveKind.Cook;
                        snapshot.ObjectiveTargetId = candidates[c];
                        snapshot.ObjectiveTargetIds.Add(candidates[c]);
                        return;
                    }
                }

                for (int c = 0; c < candidates.Count; c++)
                {
                    if (this.QuestAssistantTryProbeIdInTable("GetRecipe", candidates[c]))
                    {
                        snapshot.ObjectiveKind = QuestObjectiveKind.Craft;
                        snapshot.ObjectiveTargetId = candidates[c];
                        snapshot.ObjectiveTargetIds.Add(candidates[c]);
                        return;
                    }
                }
            }

            // Tier 3 (narrow, text-matched fallback): TaskOrder-category board requests for "Bird
            // Info Card" have NO trackMark and checkParamString="TaskSubmitState" (a live inventory
            // check, not an event name — same shape as "Too Much of a Good Thing"'s item-submit
            // condition, just not yet CanSubmit). "Bird Info Card" isn't a raw catch reward — it comes
            // from exchanging BirdNetFarm's photo drops via BirdPhotoExchange (BirdPhotoSubmitFeature,
            // "Submit Bird Photo"), so CatchBird's action button must ALSO trigger that exchange, not
            // just run the farm — see QuestAssistantStartCatchBird. 2026-07-02, progress doc §31.
            // Matched on desc text (not just checkParamString, which "Too Much of a Good Thing" also
            // has) to avoid misclassifying unrelated TaskSubmitState conditions as bird quests; also
            // requires TaskOrder category as a second guard against false positives elsewhere.
            for (int i = 0; i < snapshot.Conditions.Count; i++)
            {
                ConditionSnapshot cond = snapshot.Conditions[i];
                if (snapshot.Category == QuestAssistantCategoryTaskOrder
                    && string.Equals(cond.CheckParamString, "TaskSubmitState", StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(cond.Description)
                    && cond.Description.IndexOf("Bird", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    snapshot.ObjectiveKind = QuestObjectiveKind.CatchBird;
                    this.QuestAssistantLog("  classify taskId=" + snapshot.TaskId + ": tier3 TaskSubmitState desc=\"" + cond.Description + "\" -> CatchBird (order board, needs BirdNetFarm + photo exchange)");
                    return;
                }
            }

            this.QuestAssistantLog("  classify taskId=" + snapshot.TaskId + ": no track mark, no allowlisted event match -> Unknown");
        }

        // typeParam is sometimes a comma-separated list (e.g. "45352,45353,45354" for dish quality
        // variants, or "324,325" for alternate items) — split and parse every token. typeParam is
        // checked before checkParam because it was the field observed to carry the real item/recipe
        // id in every confirmed true positive from the 2026-07-02 dump.
        private static List<int> QuestAssistantNumericCandidates(ConditionSnapshot cond)
        {
            List<int> candidates = new List<int>();
            QuestAssistantAddNumericTokens(cond.TypeParam, candidates);
            if (cond.CheckParam > 0 && !candidates.Contains(cond.CheckParam))
            {
                candidates.Add(cond.CheckParam);
            }

            return candidates;
        }

        private static void QuestAssistantAddNumericTokens(string raw, List<int> candidates)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i].Trim(), out int value) && value > 0 && !candidates.Contains(value))
                {
                    candidates.Add(value);
                }
            }
        }

        private static QuestObjectiveKind QuestAssistantMapTrackMarkCategory(int markCategory)
        {
            // CompleteConditionTrackMarkType: Collection=5, Bird=6, Insect=7, Fish=11, Furniture=12,
            // DynamicMapResource=13 (ilspy-dumps/EcsClient/.../CompleteConditionTrackMarkType.cs).
            switch (markCategory)
            {
                case 5:
                case 13:
                    return QuestObjectiveKind.Collect;
                case 11:
                    return QuestObjectiveKind.CatchFish;
                case 7:
                    return QuestObjectiveKind.CatchInsect;
                case 6:
                    return QuestObjectiveKind.CatchBird;
                case 12:
                    return QuestObjectiveKind.Craft;
                default:
                    return QuestObjectiveKind.Unknown;
            }
        }

        private static int QuestAssistantPopCount(uint bits)
        {
            int count = 0;
            while (bits != 0)
            {
                bits &= bits - 1;
                count++;
            }

            return count;
        }

        // TableData.GetRecipe(int,bool=false) / GetCookingRecipe(int,bool=false) share GetGameTask's
        // dual-compiled-overload shape; mirrors TryGetDailyQuestGameTaskRowPtrAura's arity probe.
        private unsafe bool QuestAssistantTryProbeIdInTable(string methodName, int id)
        {
            if (id <= 0)
            {
                return false;
            }

            IntPtr tableDataClass = this.TryGetDailyQuestProbeTableDataClass(out _);
            if (tableDataClass == IntPtr.Zero || auraMonoRuntimeInvoke == null || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr method1 = this.FindAuraMonoMethodOnHierarchy(tableDataClass, methodName, 1);
            IntPtr method2 = this.FindAuraMonoMethodOnHierarchy(tableDataClass, methodName, 2);
            IntPtr method = method1 != IntPtr.Zero ? method1 : method2;
            if (method == IntPtr.Zero)
            {
                this.QuestAssistantLog("    probe " + methodName + "(" + id + "): method missing");
                return false;
            }

            int localId = id;
            bool needException = false;
            IntPtr exc = IntPtr.Zero;
            IntPtr row;
            if (method2 == method)
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&localId);
                args[1] = (IntPtr)(&needException);
                row = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&localId);
                row = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            bool found = exc == IntPtr.Zero && row != IntPtr.Zero;
            this.QuestAssistantLog("    probe " + methodName + "(" + id + "): " + (found ? "FOUND" : "not found"));
            return found;
        }

        // ===== Logging =====

        private void QuestAssistantLogTaskDetail(QuestSnapshot q, List<TrackMarkSnapshot> trackMarks)
        {
            this.QuestAssistantLog(
                "task taskId=" + q.TaskId + " netId=" + q.TaskNetId
                + " state=" + q.State + " category=" + q.Category + " displayType=" + q.DisplayType
                + " failed=" + q.IsFailed
                + " name=\"" + q.Name + "\"");

            for (int i = 0; i < trackMarks.Count; i++)
            {
                TrackMarkSnapshot m = trackMarks[i];
                this.QuestAssistantLog("  trackMark[" + i + "] markCategory=" + m.MarkCategory + " id=" + m.Id + " groupId=" + m.GroupId + " levelId=" + m.LevelId);
            }

            for (int i = 0; i < q.Conditions.Count; i++)
            {
                ConditionSnapshot c = q.Conditions[i];
                this.QuestAssistantLog(
                    "  condition[" + i + "] checkType=" + c.CheckType
                    + " checkParam=" + c.CheckParam
                    + " checkParamString=\"" + c.CheckParamString + "\""
                    + " typeParam=\"" + c.TypeParam + "\""
                    + " progress=" + c.Current + "/" + c.Needed
                    + " desc=\"" + c.Description + "\"");
            }

            this.QuestAssistantLog("  -> classified ObjectiveKind=" + q.ObjectiveKind + " targetId=" + q.ObjectiveTargetId);
        }

        // ===== Temporary Phase 0/1 debug tab (replaced by the floating window in Phase 2) =====

        private float DrawQuestAssistantTab(float startY)
        {
            const float left = 40f;
            float y = startY;

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 1f);
            GUI.Label(new Rect(left, y, 460f, 24f), "Quest Assistant (Phase 0/1 debug)", headerStyle);
            y += 34f;

            GUI.enabled = !this.questAssistantBusy;
            if (GUI.Button(new Rect(left, y, 200f, 32f), "Dump Active Quests", this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.QuestAssistantOnDumpButtonClicked();
            }

            GUI.enabled = true;

            if (GUI.Button(new Rect(left + 210f, y, 220f, 32f), this.questAssistantWindowVisible ? "Hide Floating Window" : "Show Floating Window", GUI.skin.button))
            {
                this.QuestAssistantToggleWindow();
            }

            if (this.questAssistantAvailable != null && this.questAssistantAvailable.Count > 0)
            {
                GUI.enabled = this.questAssistantAcceptAllCoroutine == null;
                if (GUI.Button(new Rect(left + 440f, y, 180f, 32f), "Accept All (" + this.questAssistantAvailable.Count + ")", GUI.skin.button))
                {
                    this.QuestAssistantOnAcceptAllClicked();
                }

                GUI.enabled = true;
            }

            int readyToSubmit = this.QuestAssistantCountReadyToSubmit();
            if (readyToSubmit > 0)
            {
                GUI.enabled = !this.QuestAssistantIsDailyQuestSubmitBusy();
                if (GUI.Button(new Rect(left + 630f, y, 200f, 32f), "Submit Ready Items (" + readyToSubmit + ")", GUI.skin.button))
                {
                    this.StartDailyQuestAutoSubmitItems(false);
                }

                GUI.enabled = true;
            }

            y += 40f;

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            statusStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.82f);
            GUI.Label(new Rect(left, y, 620f, 22f), this.questAssistantLastStatus ?? string.Empty, statusStyle);
            y += 28f;

            if (this.questAssistantSnapshot == null || this.questAssistantSnapshot.Count == 0)
            {
                GUI.Label(
                    new Rect(left, y, 620f, 40f),
                    "(no active quests resolved yet — click Refresh, then check bugtopia.log for [QuestAssistant] lines)",
                    statusStyle);
                return y + 40f;
            }

            for (int i = 0; i < this.questAssistantSnapshot.Count; i++)
            {
                QuestSnapshot q = this.questAssistantSnapshot[i];
                string line = q.Name + "  [state=" + q.State + " cat=" + q.Category + "]  kind=" + q.ObjectiveKind + " target=" + q.ObjectiveTargetId;
                GUI.Label(new Rect(left, y, 640f, 20f), line, statusStyle);
                y += 20f;

                for (int c = 0; c < q.Conditions.Count; c++)
                {
                    ConditionSnapshot cond = q.Conditions[c];
                    string condLine = "    - " + cond.Description + " (" + cond.Current + "/" + cond.Needed + ")";
                    GUI.Label(new Rect(left, y, 640f, 18f), condLine, statusStyle);
                    y += 18f;
                }
            }

            return y + 20f;
        }
    }
}
