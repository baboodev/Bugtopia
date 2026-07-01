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
        private const int QuestAssistantStateAccepted = 3;   // GameTaskState.Accepted
        private const int QuestAssistantStateCanSubmit = 4;  // GameTaskState.CanSubmit
        private const int QuestAssistantCheckTypeKindAccumulate = 4; // CompleteConditionCheckType.KindAccumulate

        private enum QuestObjectiveKind
        {
            Unknown = 0,
            Collect,
            Cook,
            Craft,
            CatchFish,
            CatchInsect,
            CatchBird,
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
        private string questAssistantLastStatus = "Idle. Load into a town, then click the button below.";

        // ===== Entry point — purely button-triggered. No OnUpdate/background tick: this must never
        // run before the user explicitly asks for it (world/session data isn't guaranteed to exist
        // at mod load / main menu — AGENTS.md §5, "many assemblies load only after entering a town"). =====

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
            int activeCount = 0;
            int totalCount = 0;

            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, taskItems, taskPins))
                {
                    this.questAssistantLastStatus = "GetAllTasks: 0 tasks";
                    this.questAssistantSnapshot = resolved;
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

                    if (!this.QuestAssistantTryResolveTask(boxedGameTaskItem, verboseDump, out QuestSnapshot snapshot, out bool isActive))
                    {
                        continue;
                    }

                    if (isActive)
                    {
                        activeCount++;
                        resolved.Add(snapshot);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(taskPins);
            }

            this.questAssistantSnapshot = resolved;
            this.questAssistantLastStatus = "OK: " + activeCount + "/" + totalCount + " active";
            this.QuestAssistantLog("=== resolve pass done: " + activeCount + " active of " + totalCount + " total ===");
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
        private unsafe bool QuestAssistantTryResolveTask(IntPtr boxedGameTaskItem, bool verboseDump, out QuestSnapshot snapshot, out bool isActive)
        {
            snapshot = null;
            isActive = false;

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

                    // Vanilla active-visibility filter (GameTaskModule.GetTasks) — state + displayType
                    // gate only. Sub-task cross-exclusion (FilterSubTask) is NOT replicated yet; see
                    // Phase 1 scope note in docs/plans/2026-07-02-quest-assistant.md.
                    bool activeByState =
                        (stateInt == QuestAssistantStateAccepted && (displayType == 0 || displayType == 4))
                        || (stateInt == QuestAssistantStateCanSubmit && (displayType == 0 || (uint)(displayType - 3) > 1u));

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
                    };

                    this.QuestAssistantClassify(trackMarks, snapshot);
                    isActive = activeByState && !isFailed;

                    if (verboseDump)
                    {
                        this.QuestAssistantLogTaskDetail(snapshot, trackMarks, activeByState);
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

        // ===== Classification (Tier 1: track marks, Tier 2: recipe-id fallback) =====

        private void QuestAssistantClassify(List<TrackMarkSnapshot> trackMarks, QuestSnapshot snapshot)
        {
            snapshot.ObjectiveKind = QuestObjectiveKind.Unknown;
            snapshot.ObjectiveTargetId = 0;

            for (int i = 0; i < trackMarks.Count; i++)
            {
                TrackMarkSnapshot mark = trackMarks[i];
                QuestObjectiveKind kind = QuestAssistantMapTrackMarkCategory(mark.MarkCategory);
                if (kind != QuestObjectiveKind.Unknown)
                {
                    snapshot.ObjectiveKind = kind;
                    snapshot.ObjectiveTargetId = mark.Id;
                    this.QuestAssistantLog("  classify taskId=" + snapshot.TaskId + ": tier1 trackMark category=" + mark.MarkCategory + " id=" + mark.Id + " -> " + kind);
                    return;
                }
            }

            List<int> candidates = new List<int>();
            for (int i = 0; i < snapshot.Conditions.Count; i++)
            {
                ConditionSnapshot cond = snapshot.Conditions[i];
                if (cond.CheckParam > 0 && !candidates.Contains(cond.CheckParam))
                {
                    candidates.Add(cond.CheckParam);
                }

                if (int.TryParse(cond.CheckParamString, out int p1) && p1 > 0 && !candidates.Contains(p1))
                {
                    candidates.Add(p1);
                }

                if (int.TryParse(cond.TypeParam, out int p2) && p2 > 0 && !candidates.Contains(p2))
                {
                    candidates.Add(p2);
                }
            }

            if (candidates.Count == 0)
            {
                this.QuestAssistantLog("  classify taskId=" + snapshot.TaskId + ": no track mark, no numeric candidate -> Unknown");
                return;
            }

            this.QuestAssistantLog("  classify taskId=" + snapshot.TaskId + ": tier2 candidates=[" + string.Join(",", candidates) + "]");

            for (int i = 0; i < candidates.Count; i++)
            {
                if (this.QuestAssistantTryProbeIdInTable("GetCookingRecipe", candidates[i]))
                {
                    snapshot.ObjectiveKind = QuestObjectiveKind.Cook;
                    snapshot.ObjectiveTargetId = candidates[i];
                    return;
                }
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (this.QuestAssistantTryProbeIdInTable("GetRecipe", candidates[i]))
                {
                    snapshot.ObjectiveKind = QuestObjectiveKind.Craft;
                    snapshot.ObjectiveTargetId = candidates[i];
                    return;
                }
            }

            this.QuestAssistantLog("  classify taskId=" + snapshot.TaskId + ": tier2 no match -> Unknown");
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

        private void QuestAssistantLogTaskDetail(QuestSnapshot q, List<TrackMarkSnapshot> trackMarks, bool activeByState)
        {
            this.QuestAssistantLog(
                "task taskId=" + q.TaskId + " netId=" + q.TaskNetId
                + " state=" + q.State + " category=" + q.Category + " displayType=" + q.DisplayType
                + " active=" + activeByState + " failed=" + q.IsFailed
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
