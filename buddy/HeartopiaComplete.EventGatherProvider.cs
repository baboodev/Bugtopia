using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        // Festival Dynamicbush nodes (Honey Mushroom, Hollow Tree, and whatever the next
        // battle pass adds). Discovery is the live TableData join, not a staticId list:
        //   Dynamicbush ⋈ AdvCollect WHERE itemTypeId > 1042
        // Show while a BpActivity.unlockDateId window contains the game clock, or a loaded
        // task's closeCondition DuringPeriod is live. Runtime reads are AuraMono TableData /
        // GameTimeUtility — the offline sqlite dump is research only.
        //
        // Enumeration is the static dictionaries (AdvCollect is ~44 rows, Dynamicbush ~30,
        // BpActivity ~299). GameTask rows are inflated only for battle passes whose unlock
        // window is live, then cached for the world. A 30 s period recheck re-filters that
        // cache; it does not walk the tables again and it is not an OnUpdate poll.

        internal sealed class EventGatherNode
        {
            public int StaticId;
            public string NameZh;
            public string LabelEn;
            public string PrefabStep0;
            public string MeshHint;
            public string DisplayIcon;
            public int PeriodId;
            public long PeriodEndTicks;
            public bool HasLootItem;
            public int LootItemId;
        }

        private struct EventGatherBushRow
        {
            public int Id;
            public int BushType;
            public string Name;
            public string Stem;
            public string Step0;
            public string Step1;
            public string DisplayIcon;
            public string ButtonIcon;
            public string BubbleIcon;
            public bool PathBShape;
        }

        private sealed class EventGatherTaskBind
        {
            public bool HollowDesc;
            public int[] ClosePeriodIds;
            public int[] CollectItemIds;
        }

        private sealed class EventGatherBpBind
        {
            public int PeriodId;
            public int[] TaskIds;
            public bool TasksLoaded;
            public readonly List<EventGatherTaskBind> Tasks = new List<EventGatherTaskBind>();
        }

        private const int EventGatherMinItemTypeId = 1042;
        private const float EventGatherPeriodCheckSeconds = 30f;
        private const string EventGatherWorldReadyName = "EventGatherProvider";

        private readonly List<EventGatherNode> eventGatherActive = new List<EventGatherNode>();
        private readonly Dictionary<int, EventGatherNode> eventGatherActiveById = new Dictionary<int, EventGatherNode>();
        private readonly HashSet<int> eventGatherCandidateIds = new HashSet<int>();
        private readonly HashSet<int> eventGatherDynamicBushIds = new HashSet<int>();
        private readonly List<EventGatherBushRow> eventGatherBushes = new List<EventGatherBushRow>();
        private readonly Dictionary<int, string> eventGatherItemStem = new Dictionary<int, string>();
        private readonly List<EventGatherBpBind> eventGatherBinds = new List<EventGatherBpBind>();
        private readonly Dictionary<int, bool> eventGatherRadarEnabled = new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> eventGatherPeriodCache = new Dictionary<int, bool>();

        private bool eventGatherReady;
        private int eventGatherBuiltEpoch = -1;
        private int eventGatherClearedEpoch = -1;
        private int eventGatherApisEpoch = -1;
        private int eventGatherUiStamp;
        private int eventGatherUiAppliedStamp = -1;
        private float eventGatherNextPeriodCheckAt;
        private string eventGatherLoggedIds;
        private bool eventGatherFailureLogged;

        private IntPtr eventGatherCheckPeriod;
        private IntPtr eventGatherGetPeriodFromExpr;
        private IntPtr eventGatherLeftSeconds;
        private IntPtr eventGatherGameTime;
        private IntPtr eventGatherGetGameTask;

        internal bool TryGetActiveEventGatherNodes(out IReadOnlyList<EventGatherNode> nodes)
        {
            nodes = this.eventGatherActive;
            return this.eventGatherReady;
        }

        internal bool IsActiveEventGatherStaticId(int staticId)
        {
            return staticId > 0 && this.eventGatherActiveById.ContainsKey(staticId);
        }

        // Candidate pool (itemTypeId > 1042), including bushes whose festival has ended.
        internal bool IsEventGatherStaticId(int staticId)
        {
            return staticId > 0 && this.eventGatherCandidateIds.Contains(staticId);
        }

        internal bool IsEventGatherRadarEnabled(int staticId)
        {
            if (this.eventGatherRadarEnabled.TryGetValue(staticId, out bool enabled))
            {
                return enabled;
            }

            // Session-only like mushrooms / berries: unknown ids start off until the user checks them.
            return false;
        }

        internal void InvalidateEventGatherProvider()
        {
            this.ClearEventGatherSnapshot();
            this.eventGatherClearedEpoch = -1;
            this.ResetWorldReadyCallback(EventGatherWorldReadyName);
        }

        private void SetEventGatherRadarEnabled(int staticId, bool enabled)
        {
            if (staticId <= 0)
            {
                return;
            }

            // Session-only, like mushroom / berry category toggles — not in RadarConfigData.
            this.eventGatherRadarEnabled[staticId] = enabled;
        }

        private void SetAllEventGatherRadarEnabled(bool enabled)
        {
            for (int i = 0; i < this.eventGatherActive.Count; i++)
            {
                int id = this.eventGatherActive[i].StaticId;
                if (id > 0)
                {
                    this.eventGatherRadarEnabled[id] = enabled;
                }
            }
        }

        private bool IsAnyEventGatherRadarEnabled()
        {
            for (int i = 0; i < this.eventGatherActive.Count; i++)
            {
                if (this.IsEventGatherRadarEnabled(this.eventGatherActive[i].StaticId))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsEventGatherMarkerEnabled(string markerLabel)
        {
            if (string.IsNullOrEmpty(markerLabel))
            {
                return false;
            }

            for (int i = 0; i < this.eventGatherActive.Count; i++)
            {
                EventGatherNode node = this.eventGatherActive[i];
                if (node == null || string.IsNullOrEmpty(node.LabelEn))
                {
                    continue;
                }

                if (!this.IsEventGatherRadarEnabled(node.StaticId))
                {
                    continue;
                }

                // "Honey Mushroom" historically matched by substring; every other event
                // label is the whole marker name so "Hollow Tree" is not also "Tree".
                bool hit = string.Equals(node.LabelEn, "Honey Mushroom", StringComparison.Ordinal)
                    ? markerLabel.IndexOf(node.LabelEn, StringComparison.Ordinal) >= 0
                    : string.Equals(markerLabel, node.LabelEn, StringComparison.Ordinal);
                if (hit)
                {
                    return true;
                }
            }

            return false;
        }

        internal bool IsActiveEventGatherLabel(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return false;
            }

            for (int i = 0; i < this.eventGatherActive.Count; i++)
            {
                string nodeLabel = this.eventGatherActive[i].LabelEn;
                if (!string.IsNullOrEmpty(nodeLabel)
                    && string.Equals(label, nodeLabel, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private string EventGatherMeshForLabel(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return null;
            }

            for (int i = 0; i < this.eventGatherActive.Count; i++)
            {
                EventGatherNode node = this.eventGatherActive[i];
                if (node != null
                    && string.Equals(label, node.LabelEn, StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(node.MeshHint))
                {
                    return node.MeshHint;
                }
            }

            return null;
        }

        private bool TryMatchEventGatherMeshLabel(string meshName, out string label)
        {
            label = null;
            if (string.IsNullOrEmpty(meshName))
            {
                return false;
            }

            for (int i = 0; i < this.eventGatherActive.Count; i++)
            {
                EventGatherNode node = this.eventGatherActive[i];
                if (node == null || string.IsNullOrEmpty(node.MeshHint) || string.IsNullOrEmpty(node.LabelEn))
                {
                    continue;
                }

                if (string.Equals(meshName, node.MeshHint, StringComparison.Ordinal))
                {
                    label = node.LabelEn;
                    return true;
                }
            }

            return false;
        }

        private string EventGatherMeshHintFor(int staticId)
        {
            if (this.eventGatherActiveById.TryGetValue(staticId, out EventGatherNode node)
                && node != null
                && !string.IsNullOrEmpty(node.MeshHint))
            {
                return node.MeshHint;
            }

            return null;
        }

        private void ClearEventGatherSnapshot()
        {
            this.eventGatherReady = false;
            this.eventGatherBuiltEpoch = -1;
            this.eventGatherActive.Clear();
            this.eventGatherActiveById.Clear();
            this.eventGatherCandidateIds.Clear();
            this.eventGatherDynamicBushIds.Clear();
            this.eventGatherBushes.Clear();
            this.eventGatherItemStem.Clear();
            this.eventGatherBinds.Clear();
            this.eventGatherPeriodCache.Clear();
            this.eventGatherLoggedIds = null;
            this.eventGatherUiStamp++;
        }

        private bool OnEventGatherWorldReady()
        {
            if (this.eventGatherReady
                && this.eventGatherBuiltEpoch == this.worldReadyEpoch
                && this.eventGatherApisEpoch == AuraMonoWorldEpoch)
            {
                return true;
            }

            if (this.eventGatherClearedEpoch != this.worldReadyEpoch)
            {
                this.ClearEventGatherSnapshot();
                this.eventGatherClearedEpoch = this.worldReadyEpoch;
            }

            return this.RefreshEventGatherSnapshot();
        }

        // Cheap period gate. Called from the existing land-gather scan, the aura kind
        // lookup, and the radar Events header — never from a new OnUpdate Ensure* loop.
        private void TouchEventGatherPeriodGate(bool force)
        {
            if (!this.eventGatherReady || this.eventGatherBuiltEpoch != this.worldReadyEpoch)
            {
                return;
            }

            if (!force && Time.unscaledTime < this.eventGatherNextPeriodCheckAt)
            {
                return;
            }

            if (!this.AttachAuraMonoThread())
            {
                this.eventGatherNextPeriodCheckAt = Time.unscaledTime + 2f;
                return;
            }

            this.eventGatherNextPeriodCheckAt = Time.unscaledTime + EventGatherPeriodCheckSeconds;
            this.eventGatherPeriodCache.Clear();
            try
            {
                this.RebuildEventGatherActiveFromBinds();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("EventGatherProvider: period recheck failed: " + ex.Message);
            }
        }

        private bool EnsureEventGatherApis()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            this.EnsureRadarSpawnGateApis();
            if (this.radarGateTableDataClass == IntPtr.Zero)
            {
                return false;
            }

            if (this.eventGatherApisEpoch == AuraMonoWorldEpoch
                && this.eventGatherGetGameTask != IntPtr.Zero
                && this.eventGatherCheckPeriod != IntPtr.Zero)
            {
                return true;
            }

            this.eventGatherApisEpoch = AuraMonoWorldEpoch;
            this.eventGatherCheckPeriod = IntPtr.Zero;
            this.eventGatherGetPeriodFromExpr = IntPtr.Zero;
            this.eventGatherLeftSeconds = IntPtr.Zero;
            this.eventGatherGameTime = IntPtr.Zero;
            this.eventGatherGetGameTask = IntPtr.Zero;

            this.eventGatherGetGameTask = this.FindAuraMonoMethodOnHierarchy(this.radarGateTableDataClass, "GetGameTask", 2);
            IntPtr timeClass = this.FindAuraMonoClassAnySpelling("XDTDataAndProtocol.ProtocolService.GameTimeUtility");
            if (timeClass != IntPtr.Zero)
            {
                this.eventGatherCheckPeriod = this.FindAuraMonoMethodOnHierarchy(timeClass, "CheckInSpecifiedTimePeriod", 1);
                this.eventGatherGetPeriodFromExpr = this.FindAuraMonoMethodOnHierarchy(timeClass, "GetPeriodFromDateExpr", 1);
                this.eventGatherLeftSeconds = this.FindAuraMonoMethodOnHierarchy(timeClass, "GetLeftSecondsByPeriod", 1);
                this.eventGatherGameTime = this.FindAuraMonoMethodOnHierarchy(timeClass, "GetCurrentGameTime", 0);
            }

            if (this.eventGatherGetGameTask == IntPtr.Zero
                || !AuraMonoMethodParamCountIs(this.eventGatherGetGameTask, 2U)
                || this.eventGatherCheckPeriod == IntPtr.Zero
                || !AuraMonoMethodParamCountIs(this.eventGatherCheckPeriod, 1U))
            {
                return false;
            }

            return true;
        }

        private bool RefreshEventGatherSnapshot()
        {
            if (!this.EnsureEventGatherApis())
            {
                this.LogEventGatherFailureOnce("apis not ready");
                return false;
            }

            List<IntPtr> rows = new List<IntPtr>(64);
            List<uint> pins = new List<uint>(128);
            List<EventGatherBushRow> bushes = new List<EventGatherBushRow>(16);
            Dictionary<int, int> advItemType = new Dictionary<int, int>();
            Dictionary<string, int> prefabToItem = new Dictionary<string, int>(StringComparer.Ordinal);
            List<EventGatherBpBind> binds = new List<EventGatherBpBind>(32);
            try
            {
                if (!this.TryEventGatherReadRows("TableDynamicbushs", rows, pins) || rows.Count == 0)
                {
                    this.LogEventGatherFailureOnce("TableDynamicbushs empty");
                    return false;
                }

                HashSet<int> allBushIds = new HashSet<int>();
                for (int i = 0; i < rows.Count; i++)
                {
                    IntPtr row = this.EventGatherUnwrapRow(rows[i], pins);
                    if (!this.TryGetMonoInt32Member(row, "id", out int id) || id <= 0)
                    {
                        continue;
                    }

                    allBushIds.Add(id);
                    if (!this.TryGetMonoStringMember(row, "normalStep0PrefabId", out string step0))
                    {
                        step0 = string.Empty;
                    }

                    this.TryGetMonoStringMember(row, "name", out string name);
                    this.TryGetMonoStringMember(row, "normalStep1PrefabId", out string step1);
                    this.TryGetMonoStringMember(row, "displayIcon", out string display);
                    this.TryGetMonoStringMember(row, "interactionButtonIcon", out string button);
                    this.TryGetMonoStringMember(row, "bubbleInteractionIcon", out string bubble);
                    int bushType = 0;
                    this.TryGetMonoInt32Member(row, "dynamicBushType", out bushType);
                    bushes.Add(new EventGatherBushRow
                    {
                        Id = id,
                        BushType = bushType,
                        Name = name ?? string.Empty,
                        Stem = EventGatherStemFromPrefab(step0),
                        Step0 = step0 ?? string.Empty,
                        Step1 = step1 ?? string.Empty,
                        DisplayIcon = display ?? string.Empty,
                        ButtonIcon = button ?? string.Empty,
                        BubbleIcon = bubble ?? string.Empty,
                        PathBShape = EventGatherIsPathBShape(display, button, bubble)
                    });
                }

                rows.Clear();
                if (!this.TryEventGatherReadRows("TableAdvCollects", rows, pins) || rows.Count == 0)
                {
                    this.LogEventGatherFailureOnce("TableAdvCollects empty");
                    return false;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    IntPtr row = this.EventGatherUnwrapRow(rows[i], pins);
                    if (!this.TryGetMonoInt32Member(row, "id", out int id) || id <= 0)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoInt32Member(row, "itemTypeId", out int itemTypeId))
                    {
                        continue;
                    }

                    advItemType[id] = itemTypeId;
                }

                this.EventGatherReadPrefabMap("TableNormalmushrooms", rows, pins, prefabToItem);
                this.EventGatherReadPrefabMap("TableMaterials", rows, pins, prefabToItem);

                rows.Clear();
                if (!this.TryEventGatherReadRows("TableBpActivitys", rows, pins) || rows.Count == 0)
                {
                    this.LogEventGatherFailureOnce("TableBpActivitys empty");
                    return false;
                }

                List<int> taskScratch = new List<int>(8);
                for (int i = 0; i < rows.Count; i++)
                {
                    IntPtr row = this.EventGatherUnwrapRow(rows[i], pins);
                    int periodId = this.EventGatherReadUnlockPeriodId(row, pins);
                    if (periodId <= 0)
                    {
                        continue;
                    }

                    taskScratch.Clear();
                    if (this.TryGetMonoObjectMember(row, "taskGroup", out IntPtr taskArr) && taskArr != IntPtr.Zero)
                    {
                        uint arrPin = AuraMonoPinNew(taskArr);
                        if (arrPin != 0U)
                        {
                            pins.Add(arrPin);
                        }

                        this.ReadEventGatherIntArray(taskArr, taskScratch, 64);
                    }

                    if (taskScratch.Count == 0)
                    {
                        continue;
                    }

                    binds.Add(new EventGatherBpBind
                    {
                        PeriodId = periodId,
                        TaskIds = taskScratch.ToArray()
                    });
                }

                if (allBushIds.Count == 0 || advItemType.Count == 0)
                {
                    this.LogEventGatherFailureOnce("join inputs empty");
                    return false;
                }

                this.eventGatherDynamicBushIds.Clear();
                foreach (int id in allBushIds)
                {
                    this.eventGatherDynamicBushIds.Add(id);
                }

                this.eventGatherCandidateIds.Clear();
                this.eventGatherBushes.Clear();
                this.eventGatherItemStem.Clear();
                for (int i = 0; i < bushes.Count; i++)
                {
                    EventGatherBushRow bush = bushes[i];
                    if (!advItemType.TryGetValue(bush.Id, out int itemTypeId) || itemTypeId <= EventGatherMinItemTypeId)
                    {
                        continue;
                    }

                    this.eventGatherCandidateIds.Add(bush.Id);
                    this.eventGatherBushes.Add(bush);
                }

                for (int i = 0; i < this.eventGatherBushes.Count; i++)
                {
                    string stem = this.eventGatherBushes[i].Stem;
                    if (string.IsNullOrEmpty(stem))
                    {
                        continue;
                    }

                    string prefab = "p_gather_" + stem + "_00";
                    if (prefabToItem.TryGetValue(prefab, out int itemId) && itemId > 0)
                    {
                        this.eventGatherItemStem[itemId] = stem;
                    }
                }

                this.eventGatherBinds.Clear();
                this.eventGatherBinds.AddRange(binds);
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            this.eventGatherPeriodCache.Clear();
            this.eventGatherFailureLogged = false;
            this.RebuildEventGatherActiveFromBinds();
            this.eventGatherNextPeriodCheckAt = Time.unscaledTime + EventGatherPeriodCheckSeconds;
            return this.eventGatherReady;
        }

        private void LogEventGatherFailureOnce(string reason)
        {
            if (this.eventGatherFailureLogged)
            {
                return;
            }

            this.eventGatherFailureLogged = true;
            ModLogger.Msg("EventGatherProvider: table read failed (" + reason + "), will retry.");
        }

        private void RebuildEventGatherActiveFromBinds()
        {
            bool hollowPeriod = false;
            int hollowPeriodId = 0;
            Dictionary<string, int> stemItem = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> stemPeriod = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int b = 0; b < this.eventGatherBinds.Count; b++)
            {
                EventGatherBpBind bind = this.eventGatherBinds[b];
                bool bpActive = this.EventGatherPeriodIsActive(bind.PeriodId);
                if (bpActive && !bind.TasksLoaded)
                {
                    this.LoadEventGatherTasks(bind);
                }

                if (!bind.TasksLoaded)
                {
                    continue;
                }

                for (int t = 0; t < bind.Tasks.Count; t++)
                {
                    EventGatherTaskBind task = bind.Tasks[t];
                    int closePeriod = 0;
                    bool closeActive = false;
                    if (task.ClosePeriodIds != null)
                    {
                        for (int p = 0; p < task.ClosePeriodIds.Length; p++)
                        {
                            int periodId = task.ClosePeriodIds[p];
                            if (periodId > 0 && this.EventGatherPeriodIsActive(periodId))
                            {
                                closeActive = true;
                                closePeriod = periodId;
                                break;
                            }
                        }
                    }

                    if (!bpActive && !closeActive)
                    {
                        continue;
                    }

                    int gating = bpActive ? bind.PeriodId : closePeriod;
                    if (task.HollowDesc && gating > 0)
                    {
                        hollowPeriod = true;
                        if (hollowPeriodId == 0)
                        {
                            hollowPeriodId = gating;
                        }
                    }

                    if (task.CollectItemIds == null)
                    {
                        continue;
                    }

                    for (int c = 0; c < task.CollectItemIds.Length; c++)
                    {
                        int itemId = task.CollectItemIds[c];
                        if (!this.eventGatherItemStem.TryGetValue(itemId, out string stem) || string.IsNullOrEmpty(stem))
                        {
                            continue;
                        }

                        if (!stemItem.ContainsKey(stem))
                        {
                            stemItem[stem] = itemId;
                            stemPeriod[stem] = gating;
                        }
                    }
                }
            }

            Dictionary<int, EventGatherNode> built = new Dictionary<int, EventGatherNode>();
            for (int i = 0; i < this.eventGatherBushes.Count; i++)
            {
                EventGatherBushRow bush = this.eventGatherBushes[i];
                bool pathA = bush.BushType != 4
                    && !string.IsNullOrEmpty(bush.Stem)
                    && stemItem.ContainsKey(bush.Stem);
                bool pathB = bush.PathBShape && hollowPeriod;
                if (!pathA && !pathB)
                {
                    continue;
                }

                if (built.ContainsKey(bush.Id))
                {
                    continue;
                }

                int periodId = pathA && stemPeriod.TryGetValue(bush.Stem, out int stemGate) ? stemGate : hollowPeriodId;
                int loot = 0;
                if (pathA)
                {
                    stemItem.TryGetValue(bush.Stem, out loot);
                }

                built[bush.Id] = new EventGatherNode
                {
                    StaticId = bush.Id,
                    NameZh = bush.Name,
                    LabelEn = EventGatherLabelEn(bush.Id, bush.Stem, bush.Name),
                    PrefabStep0 = bush.Step0,
                    MeshHint = EventGatherMeshHint(pathA, bush.Stem, bush.Step1, bush.DisplayIcon),
                    DisplayIcon = bush.DisplayIcon,
                    PeriodId = periodId,
                    PeriodEndTicks = this.EventGatherPeriodEndTicks(periodId),
                    HasLootItem = pathA,
                    LootItemId = loot
                };
            }

            List<EventGatherNode> next = new List<EventGatherNode>(built.Values);
            next.Sort((a, b) => a.StaticId.CompareTo(b.StaticId));
            this.PublishEventGatherActive(next);
        }

        private void PublishEventGatherActive(List<EventGatherNode> next)
        {
            this.eventGatherActive.Clear();
            this.eventGatherActiveById.Clear();
            StringBuilder ids = new StringBuilder();
            for (int i = 0; i < next.Count; i++)
            {
                EventGatherNode node = next[i];
                this.eventGatherActive.Add(node);
                this.eventGatherActiveById[node.StaticId] = node;
                if (ids.Length > 0)
                {
                    ids.Append(',');
                }

                ids.Append(node.StaticId.ToString());
            }

            string signature = ids.ToString();
            if (!string.Equals(signature, this.eventGatherLoggedIds, StringComparison.Ordinal))
            {
                this.eventGatherLoggedIds = signature;
                this.eventGatherUiStamp++;
                ModLogger.Msg("EventGatherProvider: " + next.Count + " active (ids="
                    + (signature.Length == 0 ? "none" : signature) + ")");
            }

            this.eventGatherReady = true;
            this.eventGatherBuiltEpoch = this.worldReadyEpoch;
        }

        private void LoadEventGatherTasks(EventGatherBpBind bind)
        {
            bind.TasksLoaded = true;
            if (bind.TaskIds == null)
            {
                return;
            }

            for (int i = 0; i < bind.TaskIds.Length; i++)
            {
                EventGatherTaskBind task = this.ReadEventGatherTask(bind.TaskIds[i]);
                if (task != null)
                {
                    bind.Tasks.Add(task);
                }
            }
        }

        private EventGatherTaskBind ReadEventGatherTask(int taskId)
        {
            if (taskId <= 0)
            {
                return null;
            }

            List<uint> pins = new List<uint>(8);
            try
            {
                if (!this.TryEventGatherInvokeGameTask(taskId, out IntPtr task))
                {
                    return null;
                }

                uint taskPin = AuraMonoPinNew(task);
                if (taskPin == 0U)
                {
                    return null;
                }

                pins.Add(taskPin);
                EventGatherTaskBind bind = new EventGatherTaskBind();
                if (this.TryGetMonoStringMember(task, "_taskDesc", out string desc))
                {
                    bind.HollowDesc = EventGatherDescIsHollow(desc);
                }

                if (this.TryGetMonoObjectMember(task, "closeCondition", out IntPtr expr) && expr != IntPtr.Zero)
                {
                    uint exprPin = AuraMonoPinNew(expr);
                    if (exprPin != 0U)
                    {
                        pins.Add(exprPin);
                    }

                    bind.ClosePeriodIds = this.EventGatherReadPeriodIds(expr, pins);
                }

                if (this.TryGetMonoObjectMember(task, "completeConditions", out IntPtr conds) && conds != IntPtr.Zero)
                {
                    uint condPin = AuraMonoPinNew(conds);
                    if (condPin != 0U)
                    {
                        pins.Add(condPin);
                    }

                    bind.CollectItemIds = this.EventGatherReadCollectItems(conds, pins);
                }

                return bind;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private unsafe bool TryEventGatherInvokeGameTask(int taskId, out IntPtr task)
        {
            task = IntPtr.Zero;
            int id = taskId;
            byte needException = 0;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&id);
            args[1] = (IntPtr)(&needException);
            return TryAuraInvoke(this.eventGatherGetGameTask, IntPtr.Zero, (IntPtr)args, out task, out _)
                && task != IntPtr.Zero;
        }

        private unsafe int[] EventGatherReadPeriodIds(IntPtr expression, List<uint> pins)
        {
            if (expression == IntPtr.Zero
                || this.eventGatherGetPeriodFromExpr == IntPtr.Zero
                || !AuraMonoMethodParamCountIs(this.eventGatherGetPeriodFromExpr, 1U))
            {
                return null;
            }

            IntPtr expr = expression;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&expr);
            if (!TryAuraInvoke(this.eventGatherGetPeriodFromExpr, IntPtr.Zero, (IntPtr)args, out IntPtr arr, out _)
                || arr == IntPtr.Zero)
            {
                return null;
            }

            uint pin = AuraMonoPinNew(arr);
            if (pin != 0U)
            {
                pins.Add(pin);
            }

            List<int> scratch = new List<int>(4);
            this.ReadEventGatherIntArray(arr, scratch, 8);
            return scratch.Count == 0 ? null : scratch.ToArray();
        }

        private int[] EventGatherReadCollectItems(IntPtr conditions, List<uint> pins)
        {
            if (conditions == IntPtr.Zero
                || !this.IsAuraMonoArrayObject(conditions)
                || auraMonoArrayLength == null
                || auraMonoArrayAddrWithSize == null)
            {
                return null;
            }

            ulong len = auraMonoArrayLength(conditions).ToUInt64();
            if (len == 0UL || len > 8UL)
            {
                if (len == 0UL)
                {
                    return null;
                }

                len = 8UL;
            }

            IntPtr basePtr = auraMonoArrayAddrWithSize(conditions, IntPtr.Size, UIntPtr.Zero);
            if (basePtr == IntPtr.Zero)
            {
                return null;
            }

            List<int> items = null;
            for (ulong i = 0UL; i < len; i++)
            {
                IntPtr cond = System.Runtime.InteropServices.Marshal.ReadIntPtr(basePtr, (int)(i * (ulong)IntPtr.Size));
                if (cond == IntPtr.Zero)
                {
                    continue;
                }

                uint pin = AuraMonoPinNew(cond);
                if (pin != 0U)
                {
                    pins.Add(pin);
                }

                if (!this.TryGetMonoStringMember(cond, "checkParamString", out string kind)
                    || !string.Equals(kind, "CollectItem", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!this.TryGetMonoStringMember(cond, "typeParam", out string typeParam))
                {
                    continue;
                }

                EventGatherParseItemIds(typeParam, ref items);
            }

            return items == null || items.Count == 0 ? null : items.ToArray();
        }

        private static void EventGatherParseItemIds(string typeParam, ref List<int> items)
        {
            if (string.IsNullOrEmpty(typeParam))
            {
                return;
            }

            int start = 0;
            for (int i = 0; i <= typeParam.Length; i++)
            {
                if (i != typeParam.Length && typeParam[i] != ',')
                {
                    continue;
                }

                if (i > start
                    && int.TryParse(typeParam.Substring(start, i - start), out int itemId)
                    && itemId > 0)
                {
                    if (items == null)
                    {
                        items = new List<int>(4);
                    }

                    items.Add(itemId);
                }

                start = i + 1;
            }
        }

        private int EventGatherReadUnlockPeriodId(IntPtr activity, List<uint> pins)
        {
            if (activity == IntPtr.Zero
                || !this.TryGetMonoObjectMember(activity, "unlockDateId", out IntPtr dateObj)
                || dateObj == IntPtr.Zero)
            {
                return 0;
            }

            uint pin = AuraMonoPinNew(dateObj);
            if (pin != 0U)
            {
                pins.Add(pin);
            }

            if (!this.TryGetMonoInt32Member(dateObj, "id", out int periodId))
            {
                return 0;
            }

            return periodId;
        }

        private void EventGatherReadPrefabMap(string fieldName, List<IntPtr> rows, List<uint> pins, Dictionary<string, int> prefabToItem)
        {
            rows.Clear();
            if (!this.TryEventGatherReadRows(fieldName, rows, pins))
            {
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                IntPtr row = this.EventGatherUnwrapRow(rows[i], pins);
                if (!this.TryGetMonoInt32Member(row, "id", out int id) || id <= 0)
                {
                    continue;
                }

                if (!this.TryGetMonoStringMember(row, "normalPrefabId", out string prefab)
                    || string.IsNullOrEmpty(prefab))
                {
                    continue;
                }

                prefabToItem[prefab] = id;
            }
        }

        private bool TryEventGatherReadRows(string fieldName, List<IntPtr> rows, List<uint> pins)
        {
            rows.Clear();
            if (!this.TryGetAuraMonoStaticObjectField(this.radarGateTableDataClass, fieldName, out IntPtr dict)
                || dict == IntPtr.Zero
                || auraMonoObjectGetClass == null
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            uint dictPin = AuraMonoPinNew(dict);
            if (dictPin == 0U)
            {
                return false;
            }

            pins.Add(dictPin);
            IntPtr dictClass = auraMonoObjectGetClass(dict);
            IntPtr getValues = this.FindAuraMonoMethodOnHierarchy(dictClass, "get_Values", 0);
            if (getValues == IntPtr.Zero)
            {
                return false;
            }

            if (!TryAuraInvoke(getValues, dict, IntPtr.Zero, out IntPtr values, out _) || values == IntPtr.Zero)
            {
                return false;
            }

            uint valuesPin = AuraMonoPinNew(values);
            if (valuesPin == 0U)
            {
                return false;
            }

            pins.Add(valuesPin);
            return this.TryEnumerateAuraMonoCollectionItems(values, rows, pins);
        }

        private IntPtr EventGatherUnwrapRow(IntPtr item, List<uint> pins)
        {
            if (item == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (this.TryGetMonoInt32Member(item, "id", out _))
            {
                return item;
            }

            if (this.TryGetMonoObjectMember(item, "Value", out IntPtr value) && value != IntPtr.Zero)
            {
                uint pin = AuraMonoPinNew(value);
                if (pin != 0U)
                {
                    pins.Add(pin);
                }

                return value;
            }

            if (this.TryGetMonoObjectMember(item, "value", out value) && value != IntPtr.Zero)
            {
                uint pin = AuraMonoPinNew(value);
                if (pin != 0U)
                {
                    pins.Add(pin);
                }

                return value;
            }

            return item;
        }

        private unsafe void ReadEventGatherIntArray(IntPtr arrayObj, List<int> dest, int max)
        {
            dest.Clear();
            if (arrayObj == IntPtr.Zero
                || max <= 0
                || !this.IsAuraMonoArrayObject(arrayObj)
                || auraMonoArrayLength == null
                || auraMonoArrayAddrWithSize == null)
            {
                return;
            }

            ulong len = auraMonoArrayLength(arrayObj).ToUInt64();
            if (len == 0UL)
            {
                return;
            }

            if (len > (ulong)max)
            {
                len = (ulong)max;
            }

            IntPtr basePtr = auraMonoArrayAddrWithSize(arrayObj, 4, UIntPtr.Zero);
            if (basePtr == IntPtr.Zero)
            {
                return;
            }

            for (ulong i = 0UL; i < len; i++)
            {
                dest.Add(*(int*)((byte*)basePtr + (i * 4UL)));
            }
        }

        private unsafe bool EventGatherPeriodIsActive(int periodId)
        {
            if (periodId <= 0 || this.eventGatherCheckPeriod == IntPtr.Zero)
            {
                return false;
            }

            if (this.eventGatherPeriodCache.TryGetValue(periodId, out bool cached))
            {
                return cached;
            }

            int id = periodId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);
            bool active = false;
            if (TryAuraInvoke(this.eventGatherCheckPeriod, IntPtr.Zero, (IntPtr)args, out IntPtr boxed, out _)
                && boxed != IntPtr.Zero)
            {
                uint pin = AuraMonoPinNew(boxed);
                try
                {
                    active = this.TryUnboxMonoBoolean(boxed, out bool value) && value;
                }
                finally
                {
                    AuraMonoPinFree(pin);
                }
            }

            this.eventGatherPeriodCache[periodId] = active;
            return active;
        }

        private unsafe long EventGatherPeriodEndTicks(int periodId)
        {
            if (periodId <= 0
                || this.eventGatherLeftSeconds == IntPtr.Zero
                || !AuraMonoMethodParamCountIs(this.eventGatherLeftSeconds, 1U))
            {
                return 0L;
            }

            int id = periodId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);
            if (!TryAuraInvoke(this.eventGatherLeftSeconds, IntPtr.Zero, (IntPtr)args, out IntPtr boxed, out _)
                || boxed == IntPtr.Zero
                || auraMonoObjectUnbox == null)
            {
                return 0L;
            }

            uint pin = AuraMonoPinNew(boxed);
            try
            {
                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero)
                {
                    return 0L;
                }

                long left = *(long*)raw;
                if (left <= 0L)
                {
                    return 0L;
                }

                long now = this.EventGatherGameTimeTicks();
                if (now <= 0L)
                {
                    return 0L;
                }

                return now + (left * TimeSpan.TicksPerSecond);
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private unsafe long EventGatherGameTimeTicks()
        {
            if (this.eventGatherGameTime == IntPtr.Zero
                || !AuraMonoMethodParamCountIs(this.eventGatherGameTime, 0U))
            {
                return 0L;
            }

            if (!TryAuraInvoke(this.eventGatherGameTime, IntPtr.Zero, IntPtr.Zero, out IntPtr boxed, out _)
                || boxed == IntPtr.Zero)
            {
                return 0L;
            }

            uint pin = AuraMonoPinNew(boxed);
            try
            {
                if (!this.TryGetMonoObjectMember(boxed, "Ticks", out IntPtr boxedTicks) || boxedTicks == IntPtr.Zero)
                {
                    return 0L;
                }

                uint ticksPin = AuraMonoPinNew(boxedTicks);
                try
                {
                    if (auraMonoObjectUnbox == null)
                    {
                        return 0L;
                    }

                    IntPtr raw = auraMonoObjectUnbox(boxedTicks);
                    if (raw == IntPtr.Zero)
                    {
                        return 0L;
                    }

                    return *(long*)raw;
                }
                finally
                {
                    AuraMonoPinFree(ticksPin);
                }
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private static string EventGatherStemFromPrefab(string prefab)
        {
            const string prefix = "p_dynamicbush_";
            const string marker = "_00_step";
            if (string.IsNullOrEmpty(prefab) || !prefab.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }

            int cut = prefab.IndexOf(marker, prefix.Length, StringComparison.Ordinal);
            if (cut <= prefix.Length)
            {
                return null;
            }

            return prefab.Substring(prefix.Length, cut - prefix.Length);
        }

        private static bool EventGatherIsPathBShape(string display, string button, string bubble)
        {
            return string.Equals(display, "treehole", StringComparison.OrdinalIgnoreCase)
                || string.Equals(button, "perch", StringComparison.OrdinalIgnoreCase)
                || string.Equals(bubble, "treehole", StringComparison.OrdinalIgnoreCase);
        }

        private static bool EventGatherDescIsHollow(string desc)
        {
            if (string.IsNullOrEmpty(desc))
            {
                return false;
            }

            return desc.IndexOf("空心", StringComparison.Ordinal) >= 0
                || desc.IndexOf("翻找", StringComparison.Ordinal) >= 0;
        }

        // Stable English labels for the two stems the forager already matches by string.
        // Later festivals keep the prefab stem (or the Chinese table name when the stem is missing).
        private static string EventGatherLabelEn(int staticId, string stem, string nameZh)
        {
            if (staticId == 130030 || string.Equals(stem, "honeyfungus", StringComparison.Ordinal))
            {
                return "Honey Mushroom";
            }

            if (staticId == 130029 || string.Equals(stem, "deadtree", StringComparison.Ordinal))
            {
                return "Hollow Tree";
            }

            if (!string.IsNullOrEmpty(stem))
            {
                return stem;
            }

            return string.IsNullOrEmpty(nameZh) ? ("Event " + staticId.ToString()) : nameZh;
        }

        private static string EventGatherMeshHint(bool hasLoot, string stem, string step1, string displayIcon)
        {
            if (hasLoot && !string.IsNullOrEmpty(stem))
            {
                return "p_gather_" + stem + "_00";
            }

            if (!string.IsNullOrEmpty(step1))
            {
                return step1;
            }

            return displayIcon ?? string.Empty;
        }
    }
}
