using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Quest Assistant — Phase 2 (floating window + focus selection) per
    // docs/plans/2026-07-02-quest-assistant.md. Floating-window pattern copied from
    // BuildingFreeRotateFeature.DrawBuildingMovePanel/DrawBuildingMovePanelWindow (own Rect/window-id,
    // GetUiScale() matrix, screen clamp, click-blocker integration, GUI.DragWindow strip).
    //
    // Design decision: "focus" here is a PURE LOCAL selection (questAssistantFocusedTaskId), not a
    // call into TaskSystem.TrackTask. TaskSystem has TWO same-arity overloads —
    // TrackTask(uint taskNetId) and TrackTask(int taskId) — which this codebase's
    // FindAuraMonoMethodOnHierarchy(class, name, arity) helper cannot disambiguate (it matches by
    // arity only, not parameter type); calling the wrong one with the wrong id space would silently
    // track nothing or the wrong quest. Since "let the player pick which quest to see buttons for"
    // doesn't require moving the game's own on-screen tracker, avoiding that native call entirely
    // removes the risk for no loss of the feature's actual value. Revisit only if the game's HUD
    // tracker itself needs to follow the assistant's selection.
    //
    // Window position/visibility are NOT persisted to config this pass (in-memory only, resets to
    // hidden/default position each session) — deliberately deferred to keep this change's surface
    // small; follow the RadarConfigData pattern (HeartopiaComplete.ConfigTypes.cs) if wanted later.
    public partial class HeartopiaComplete
    {
        private const int QuestAssistantWindowId = 0x5B20;

        private bool questAssistantWindowVisible = false;
        private Rect questAssistantWindowRect = new Rect(160f, 160f, 420f, 450f);
        private bool questAssistantWindowMouseOver = false;
        private int questAssistantFocusedTaskId = 0;
        private Vector2 questAssistantScrollPos = Vector2.zero;
        // Collapse-to-title-bar: when collapsed the window shrinks to just its header row; the full
        // height is remembered so expanding restores it. Toggled by the header collapse button.
        private const float QuestAssistantCollapsedHeight = 30f;
        private bool questAssistantWindowCollapsed = false;
        private float questAssistantWindowExpandedHeight = 450f;

        // Collect auto-stop monitor (Phase 3 follow-up, 2026-07-02): tracks the one quest the
        // "start farming" button was pressed for and auto-stops Aura Farm/Foraging once its first
        // condition's progress reaches Needed (or the quest leaves the active list, e.g. submitted).
        // Gated entirely behind questAssistantCollectMonitorActive, which is ONLY ever set true from
        // QuestAssistantStartCollect (an explicit button click) — never from OnUpdate on its own, so
        // this can't run before the world/session is ready (same reasoning as the Phase 1 crash fix:
        // reaching this state requires an already-successful manual resolve in this session).
        private const float QuestAssistantCollectMonitorIntervalSeconds = 5f;
        private bool questAssistantCollectMonitorActive = false;
        private int questAssistantCollectMonitorTaskId = 0;
        private float questAssistantCollectMonitorNextCheckAt = 0f;

        // TalkToNpc auto-conclude watcher (2026-07-02 follow-up): after "Open dialogue" is clicked and
        // OpenTaskDialogue succeeds, watches the focused condition's progress for a short bounded window to
        // answer empirically whether interacting with the dialogue actually counts toward
        // InteractWithNpc/EnterDialogNode progress (open question -- no server source available, see class
        // comment on QuestAssistantOnTalkToNpcClicked). Separate fields from the Collect monitor --
        // independent state, independent lifetime, so a Collect watch and a TalkToNpc watch can run
        // concurrently without interfering (edge case, not the primary case, but no reason to forbid it).
        // Same safety invariant as the Collect monitor: questAssistantTalkToNpcMonitorActive is ONLY ever
        // set true from QuestAssistantOnTalkToNpcClicked (explicit button click, itself only reachable
        // after an already-successful manual resolve this session) -- never from OnUpdate on its own.
        private const float QuestAssistantTalkToNpcMonitorIntervalSeconds = 2f;
        private const float QuestAssistantTalkToNpcMonitorTimeoutSeconds = 30f;
        private bool questAssistantTalkToNpcMonitorActive = false;
        private int questAssistantTalkToNpcMonitorTaskId = 0;
        private int questAssistantTalkToNpcMonitorBeforeCurrent = 0;
        private int questAssistantTalkToNpcMonitorBeforeNeeded = 0;
        private float questAssistantTalkToNpcMonitorNextCheckAt = 0f;
        private float questAssistantTalkToNpcMonitorDeadlineAt = 0f;
        // Live NPC netId of the in-flight talk (0 = none): the conclude step sends the paired
        // SendTalkWithNpc(netId, start:false) so the server-side talk session doesn't stay open —
        // same paired start/end discipline as the skate sessions ([[force-swim-skate-locomotion]]).
        private uint questAssistantTalkToNpcMonitorNpcNetId = 0U;

        // PurchaseItem buy coroutine (§49): non-null while a quest-driven shop purchase run is in
        // flight (button disabled meanwhile). Started only from the explicit button click.
        private object questAssistantPurchaseCoroutine = null;

        // staticId → decoded EntityType (0 = decode failed). Per-session cache for the sow-button
        // crop-vs-flower check (§50): the decode (managed table / AuraMono DecodeTypeEntityData) must
        // run once per item, not every OnGUI frame.
        private readonly Dictionary<int, int> questAssistantEntityTypeCache = new Dictionary<int, int>();

        private void QuestAssistantToggleCollapse()
        {
            if (!this.questAssistantWindowCollapsed)
            {
                this.questAssistantWindowExpandedHeight = this.questAssistantWindowRect.height;
                this.questAssistantWindowRect.height = QuestAssistantCollapsedHeight;
                this.questAssistantWindowCollapsed = true;
            }
            else
            {
                this.questAssistantWindowRect.height = this.questAssistantWindowExpandedHeight;
                this.questAssistantWindowCollapsed = false;
            }
        }

        private void QuestAssistantToggleWindow()
        {
            this.questAssistantWindowVisible = !this.questAssistantWindowVisible;
            if (!this.questAssistantWindowVisible)
            {
                this.questAssistantWindowMouseOver = false;
            }
        }

        private void DrawQuestAssistantWindow()
        {
            if (!this.questAssistantWindowVisible)
            {
                this.questAssistantWindowMouseOver = false;
                return;
            }

            float scale = this.GetUiScale();
            Matrix4x4 prevMatrix = GUI.matrix;
            Color pc = GUI.color, pb = GUI.backgroundColor, pcc = GUI.contentColor;
            try
            {
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;
                GUI.contentColor = Color.white;
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
                this.questAssistantWindowRect = GUI.Window(
                    QuestAssistantWindowId, this.questAssistantWindowRect, (GUI.WindowFunction)this.DrawQuestAssistantWindowContents,
                    GUIContent.none, this.themeWindowStyle ?? GUI.skin.window);

                float maxX = Mathf.Max(0f, (Screen.width / Mathf.Max(scale, 0.001f)) - 80f);
                float maxY = Mathf.Max(0f, (Screen.height / Mathf.Max(scale, 0.001f)) - 40f);
                this.questAssistantWindowRect.x = Mathf.Clamp(this.questAssistantWindowRect.x, 0f, maxX);
                this.questAssistantWindowRect.y = Mathf.Clamp(this.questAssistantWindowRect.y, 0f, maxY);
            }
            finally
            {
                GUI.matrix = prevMatrix;
                GUI.color = pc;
                GUI.backgroundColor = pb;
                GUI.contentColor = pcc;
            }

            Vector2 sp = new Vector2(Input.mousePosition.x, (float)Screen.height - Input.mousePosition.y);
            Vector2 lp = scale > 0.001f ? sp / scale : sp;
            this.questAssistantWindowMouseOver = this.questAssistantWindowRect.Contains(lp);
            if (this.questAssistantWindowMouseOver)
            {
                Event e = Event.current;
                if (e != null && e.isMouse && e.type != EventType.Used)
                {
                    e.Use();
                }
            }
        }

        private void DrawQuestAssistantWindowContents(int id)
        {
            float w = this.questAssistantWindowRect.width;
            float h = this.questAssistantWindowRect.height;

            Color basePanel = new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Max(this.uiPanelAlpha, 0.9f));
            this.DrawRoundedPanel(new Rect(0f, 0f, w, h), 10f, basePanel, Color.clear, 0f, Color.clear);

            GUIStyle title = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 1f);
            GUI.Label(new Rect(12f, 4f, w - 132f, 20f), "Quest Assistant", title);

            // Hide the whole panel (far right). Re-open it from the mod menu's "Show Floating Window"
            // button. Drawn before the collapsed early-return so it also works while collapsed.
            if (GUI.Button(new Rect(w - 28f, 4f, 20f, 20f), "×", GUI.skin.button))
            {
                this.QuestAssistantToggleWindow();
            }

            // Collapse/expand toggle. When collapsed only this header row shows.
            if (GUI.Button(new Rect(w - 52f, 4f, 20f, 20f), this.questAssistantWindowCollapsed ? "+" : "–", GUI.skin.button))
            {
                this.QuestAssistantToggleCollapse();
            }

            if (this.questAssistantWindowCollapsed)
            {
                // Thin title bar only — skip Refresh/status/lists/detail. Keep it draggable.
                GUI.DragWindow(new Rect(0f, 0f, w, QuestAssistantCollapsedHeight));
                return;
            }

            // Width 70 (was 58): GUI.skin.button's font is Segoe UI now, wider than the old
            // default IMGUI font "Refresh" was originally sized for — it no longer fit.
            if (GUI.Button(new Rect(w - 128f, 4f, 70f, 20f), "Refresh", GUI.skin.button))
            {
                this.QuestAssistantOnDumpButtonClicked();
            }

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true };
            statusStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.8f);
            GUI.Label(new Rect(12f, 26f, w - 24f, 16f), this.questAssistantLastStatus ?? string.Empty, statusStyle);

            float extraRowTop = 44f;
            int availableCount = this.questAssistantAvailable != null ? this.questAssistantAvailable.Count : 0;
            if (availableCount > 0)
            {
                GUI.Label(new Rect(12f, extraRowTop, w - 150f, 22f), "Available to accept: " + availableCount, statusStyle);
                GUI.enabled = this.questAssistantAcceptAllCoroutine == null;
                if (GUI.Button(new Rect(w - 130f, extraRowTop - 2f, 118f, 22f), "Accept All", GUI.skin.button))
                {
                    this.QuestAssistantOnAcceptAllClicked();
                }

                GUI.enabled = true;
                extraRowTop += 24f;
            }

            int readyToSubmitCount = this.QuestAssistantCountReadyToSubmit();
            if (readyToSubmitCount > 0)
            {
                GUI.Label(new Rect(12f, extraRowTop, w - 150f, 22f), "Ready to submit: " + readyToSubmitCount, statusStyle);
                GUI.enabled = !this.QuestAssistantIsDailyQuestSubmitBusy();
                if (GUI.Button(new Rect(w - 130f, extraRowTop - 2f, 118f, 22f), "Submit Items", GUI.skin.button))
                {
                    this.StartDailyQuestAutoSubmitItems(false);
                }

                GUI.enabled = true;
                extraRowTop += 24f;
            }

            float listTop = extraRowTop == 44f ? 46f : extraRowTop;
            const float detailHeight = 150f;
            float listHeight = Mathf.Max(40f, h - listTop - detailHeight - 12f);

            List<QuestSnapshot> quests = this.questAssistantSnapshot;
            Rect listOuter = new Rect(12f, listTop, w - 24f, listHeight);
            const float rowHeight = 22f;
            float contentHeight = quests != null ? quests.Count * rowHeight : 0f;
            Rect listInner = new Rect(0f, 0f, listOuter.width - 16f, Mathf.Max(contentHeight, listOuter.height));

            this.questAssistantScrollPos = GUI.BeginScrollView(listOuter, this.questAssistantScrollPos, listInner);
            if (quests == null || quests.Count == 0)
            {
                GUI.Label(new Rect(2f, 2f, listInner.width - 4f, 20f), "(no active quests — click Refresh)", statusStyle);
            }
            else
            {
                bool focusValid = false;
                for (int i = 0; i < quests.Count; i++)
                {
                    if (quests[i].TaskId == this.questAssistantFocusedTaskId)
                    {
                        focusValid = true;
                        break;
                    }
                }

                if (!focusValid)
                {
                    this.questAssistantFocusedTaskId = quests[0].TaskId;
                }

                for (int i = 0; i < quests.Count; i++)
                {
                    QuestSnapshot q = quests[i];
                    bool isFocused = q.TaskId == this.questAssistantFocusedTaskId;
                    Rect rowRect = new Rect(2f, i * rowHeight, listInner.width - 4f, rowHeight - 2f);

                    GUIStyle rowStyle = new GUIStyle(GUI.skin.button) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
                    Color prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = isFocused
                        ? new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 0.85f)
                        : new Color(1f, 1f, 1f, 0.12f);

                    if (GUI.Button(rowRect, "  " + q.Name + "   " + QuestAssistantSummarizeProgress(q), rowStyle))
                    {
                        this.questAssistantFocusedTaskId = q.TaskId;
                    }

                    GUI.backgroundColor = prevBg;
                }
            }

            GUI.EndScrollView();

            float detailTop = listTop + listHeight + 8f;
            Rect detailRect = new Rect(12f, detailTop, w - 24f, detailHeight);
            this.DrawCardOutline(detailRect);

            QuestSnapshot focused = null;
            if (quests != null)
            {
                for (int i = 0; i < quests.Count; i++)
                {
                    if (quests[i].TaskId == this.questAssistantFocusedTaskId)
                    {
                        focused = quests[i];
                        break;
                    }
                }
            }

            if (focused == null)
            {
                GUI.Label(new Rect(detailRect.x + 6f, detailRect.y + 4f, detailRect.width - 12f, 20f), "(select a quest above)", statusStyle);
            }
            else
            {
                float dy = detailRect.y + 4f;
                GUIStyle detailTitle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = true };
                detailTitle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 1f);
                GUI.Label(new Rect(detailRect.x + 6f, dy, detailRect.width - 12f, 20f), focused.Name, detailTitle);
                dy += 20f;

                GUI.Label(new Rect(detailRect.x + 6f, dy, detailRect.width - 12f, 16f), "Objective: " + QuestAssistantDescribeObjectiveKind(focused.ObjectiveKind), statusStyle);
                dy += 18f;

                float actionButtonY = detailRect.yMax - 30f;
                for (int c = 0; c < focused.Conditions.Count && dy < actionButtonY - 4f; c++)
                {
                    ConditionSnapshot cond = focused.Conditions[c];
                    string line = "- " + cond.Description + " (" + cond.Current + "/" + cond.Needed + ")" + (cond.Complete ? " (done)" : string.Empty);
                    GUI.Label(new Rect(detailRect.x + 6f, dy, detailRect.width - 12f, 32f), line, statusStyle);
                    dy += 32f;
                }

                if (focused.ObjectiveKind == QuestObjectiveKind.Collect)
                {
                    List<int> targetIds = focused.ObjectiveTargetIds != null && focused.ObjectiveTargetIds.Count > 0
                        ? focused.ObjectiveTargetIds
                        : new List<int> { focused.ObjectiveTargetId };
                    bool monitoringThis = this.questAssistantCollectMonitorActive && this.questAssistantCollectMonitorTaskId == focused.TaskId;
                    string itemSummary = QuestAssistantSummarizeItemNames(targetIds);
                    string label = monitoringThis ? ("Stop farming " + itemSummary) : ("Enable radar for " + itemSummary + " + start farming");
                    if (GUI.Button(new Rect(detailRect.x + 6f, actionButtonY, detailRect.width - 12f, 26f), label, this.themePrimaryButtonStyle ?? GUI.skin.button))
                    {
                        if (monitoringThis)
                        {
                            this.QuestAssistantStopCollectMonitor("stopped manually");
                        }
                        else
                        {
                            this.QuestAssistantStartCollect(targetIds, focused.TaskId);
                        }
                    }
                }

                else if (focused.ObjectiveKind == QuestObjectiveKind.TalkToNpc)
                {
                    int npcStaticId = focused.ObjectiveTargetIds != null && focused.ObjectiveTargetIds.Count > 0
                        ? focused.ObjectiveTargetIds[0]
                        : focused.ObjectiveTargetId;
                    string label = "Talk to NPC #" + npcStaticId + " (auto-teleport)";
                    if (GUI.Button(new Rect(detailRect.x + 6f, actionButtonY, detailRect.width - 12f, 26f), label, this.themePrimaryButtonStyle ?? GUI.skin.button))
                    {
                        this.QuestAssistantOnTalkToNpcClicked(focused, npcStaticId);
                    }
                }

                else if (focused.ObjectiveKind == QuestObjectiveKind.SubmitToNpc)
                {
                    // Deliberately NOT worded "Submit items" — some CanSubmit-at-an-NPC quests need no
                    // item hand-off at all (talk/flag-only completion, e.g. "Naughty's Treasure",
                    // progress doc §29), and whether items are involved isn't known without an
                    // AuraMono read the button label can't afford to do every OnGUI frame.
                    string label = "Complete via NPC #" + focused.SubmitNpcId + " — no teleport";
                    if (GUI.Button(new Rect(detailRect.x + 6f, actionButtonY, detailRect.width - 12f, 26f), label, this.themePrimaryButtonStyle ?? GUI.skin.button))
                    {
                        this.QuestAssistantOnSubmitToNpcClicked(focused);
                    }
                }

                else if (focused.ObjectiveKind == QuestObjectiveKind.CatchBird)
                {
                    bool monitoringThis = this.questAssistantBirdMonitorActive && this.questAssistantBirdMonitorTaskId == focused.TaskId;
                    string label = monitoringThis ? "Stop bird farm" : "Start bird farm (auto-catch + auto-exchange)";
                    if (GUI.Button(new Rect(detailRect.x + 6f, actionButtonY, detailRect.width - 12f, 26f), label, this.themePrimaryButtonStyle ?? GUI.skin.button))
                    {
                        if (monitoringThis)
                        {
                            this.QuestAssistantStopBirdMonitor("stopped manually");
                        }
                        else
                        {
                            this.QuestAssistantStartCatchBird(focused.TaskId);
                        }
                    }
                }

                else if (focused.ObjectiveKind == QuestObjectiveKind.Craft)
                {
                    int recipeId = focused.ObjectiveTargetId;
                    string label = "Craft recipe #" + recipeId + " (remote, no Workbench trip)";
                    if (GUI.Button(new Rect(detailRect.x + 6f, actionButtonY, detailRect.width - 12f, 26f), label, this.themePrimaryButtonStyle ?? GUI.skin.button))
                    {
                        this.QuestAssistantOnCraftClicked(focused, recipeId);
                    }
                }

                else if (focused.ObjectiveKind == QuestObjectiveKind.GoToArea)
                {
                    int areaId = focused.ObjectiveTargetId;
                    string label = "Teleport to area #" + areaId + " (Go there)";
                    if (GUI.Button(new Rect(detailRect.x + 6f, actionButtonY, detailRect.width - 12f, 26f), label, this.themePrimaryButtonStyle ?? GUI.skin.button))
                    {
                        this.QuestAssistantOnGoToAreaClicked(focused, areaId);
                    }
                }

                else if (focused.ObjectiveKind == QuestObjectiveKind.EnterArea)
                {
                    int areaId = focused.ObjectiveTargetId;
                    string label = "Enter area #" + areaId + " (report arrival)";
                    if (GUI.Button(new Rect(detailRect.x + 6f, actionButtonY, detailRect.width - 12f, 26f), label, this.themePrimaryButtonStyle ?? GUI.skin.button))
                    {
                        this.QuestAssistantOnEnterAreaClicked(focused, areaId);
                    }
                }

                else if (focused.ObjectiveKind == QuestObjectiveKind.HomelandFarm)
                {
                    bool fertilize = this.QuestAssistantActiveHomelandFarmIsFertilize(focused);
                    bool booster = fertilize && this.QuestAssistantActiveHomelandFarmEffectType(focused) == HomelandFarmFertilizerEffectGrowthRate;
                    // Flower seeds can't be planted by the crop sow command (separate flower-bed
                    // system) — show a disabled button explaining that instead of a dud action (§50).
                    bool sowBlocked = !fertilize && !this.QuestAssistantActiveSowSeedsAreCropSeeds(focused, out _, out _);
                    if (sowBlocked)
                    {
                        GUI.enabled = false;
                        GUI.Button(new Rect(detailRect.x + 6f, actionButtonY, detailRect.width - 12f, 26f), "Flower seeds — plant manually (auto-sow is crops-only)", this.themePrimaryButtonStyle ?? GUI.skin.button);
                        GUI.enabled = true;
                    }
                    else
                    {
                        string label = booster
                            ? "Apply Growth Booster in radius"
                            : (fertilize ? "Fertilize crops in radius" : "Sow crops in radius");
                        if (GUI.Button(new Rect(detailRect.x + 6f, actionButtonY, detailRect.width - 12f, 26f), label, this.themePrimaryButtonStyle ?? GUI.skin.button))
                        {
                            this.QuestAssistantOnHomelandFarmClicked(focused, fertilize);
                        }
                    }
                }

                else if (focused.ObjectiveKind == QuestObjectiveKind.PurchaseItem)
                {
                    GUI.enabled = this.questAssistantPurchaseCoroutine == null;
                    if (GUI.Button(new Rect(detailRect.x + 6f, actionButtonY, detailRect.width - 12f, 26f), "Buy quest items from shop", this.themePrimaryButtonStyle ?? GUI.skin.button))
                    {
                        this.QuestAssistantOnPurchaseItemClicked(focused);
                    }

                    GUI.enabled = true;
                }

                else if (focused.ObjectiveKind == QuestObjectiveKind.CatchFish && focused.ObjectiveAreaId > 0)
                {
                    // Location-bound fishing quest ("Catch N fish at <place>", Area trackMark): one
                    // click teleports into the zone, turns Auto Fish Shadow Net on, and the fish
                    // monitor turns it back off when the count is reached / the quest completes. §55.
                    string label = this.questAssistantFishMonitorActive
                        ? "Auto-fishing for this quest... (click to stop)"
                        : "Teleport to fishing spot & auto-fish";
                    if (GUI.Button(new Rect(detailRect.x + 6f, actionButtonY, detailRect.width - 12f, 26f), label, this.themePrimaryButtonStyle ?? GUI.skin.button))
                    {
                        this.QuestAssistantOnCatchFishClicked(focused);
                    }
                }

                // Phase 5 contextual action buttons (cook) attach the same way once implemented —
                // the classifier already has everything they need.
            }

            GUI.DragWindow(new Rect(0f, 0f, w, 22f));
        }

        private static string QuestAssistantSummarizeProgress(QuestSnapshot q)
        {
            if (q.Conditions == null || q.Conditions.Count == 0)
            {
                return string.Empty;
            }

            ConditionSnapshot first = q.Conditions[0];
            return "(" + first.Current + "/" + first.Needed + ")";
        }

        private static string QuestAssistantDescribeObjectiveKind(QuestObjectiveKind kind)
        {
            switch (kind)
            {
                case QuestObjectiveKind.Collect: return "Collect materials";
                case QuestObjectiveKind.Cook: return "Cook a dish";
                case QuestObjectiveKind.Craft: return "Craft an item at a Workbench";
                case QuestObjectiveKind.CatchFish: return "Catch fish";
                case QuestObjectiveKind.CatchInsect: return "Catch an insect";
                case QuestObjectiveKind.CatchBird: return "Catch/photograph a bird";
                case QuestObjectiveKind.HomelandFarm: return "Homeland farming";
                case QuestObjectiveKind.TalkToNpc: return "Talk to an NPC";
                case QuestObjectiveKind.SubmitToNpc: return "Complete via an NPC (talk and/or submit)";
                case QuestObjectiveKind.GoToArea: return "Go to an area";
                case QuestObjectiveKind.PurchaseItem: return "Purchase items from a shop";
                case QuestObjectiveKind.EnterArea: return "Enter an area";
                default: return "(not automatable yet)";
            }
        }

        // ===== Phase 3 — Collect button: best-effort radar category + Aura Farm + Foraging =====
        //
        // Radar integration is deliberately best-effort (per plan §4/Phase 3): HeartopiaComplete.Radar.cs
        // has ~179 individual showXRadar-style fields, not a dictionary keyed by item id, and only the
        // known collectable-atlas ids (docs/RADAR_GAME_MAP.md) are mapped here. Unmapped items still
        // start Aura Farm + Foraging — no radar category is skipped silently, never blocked/warned loudly.
        // No new AuraMono calls here: everything is a direct field write, reusing this project's
        // existing radar/farm toggles exactly as the Radar tab UI itself would set them.
        //
        // isRadarActive is a SEPARATE master switch from the per-category showXRadar flags —
        // ToggleRadar()/CheckRadarAutoToggle() (HeartopiaComplete.Radar.cs) deliberately do NOT
        // auto-enable it when a category is checked ("You must manually press the ENABLE RADAR
        // button to activate"), confirmed in-game 2026-07-02 (button alone did nothing without this).
        //
        // The Foraging state machine only starts inside ToggleAutoFarm() (Farm.cs), which the
        // real "START FORAGING" button calls, and which itself enforces preconditions (a loot toggle,
        // Aura Farm enabled, isRadarActive) before flipping autoFarmActive.
        private void QuestAssistantStartCollect(List<int> itemStaticIds, int taskId)
        {
            if (!this.isRadarActive)
            {
                this.ToggleRadar();
            }

            // Bug fix (2026-07-02, progress doc §33): QuestAssistantTryEnableRadarForItem only ever
            // turns categories ON — switching from quest A to quest B left A's categories (and
            // therefore A's items) still selected, so Aura Farm/Foraging kept collecting BOTH quests'
            // items (in practice, everything ever selected across the whole session). Clear exactly
            // the categories this method can ever set before enabling the current quest's — never a
            // blanket "Clear All Loots" (that also touches bird/insect/fish/other-players/meteor/
            // bubble toggles Quest Assistant has no business resetting).
            this.QuestAssistantClearOwnedRadarLootToggles();

            int mappedCount = 0;
            for (int i = 0; i < itemStaticIds.Count; i++)
            {
                if (this.QuestAssistantTryEnableRadarForItem(itemStaticIds[i]))
                {
                    mappedCount++;
                }
            }

            this.SetAuraFarmEnabled(true);
            if (!this.autoFarmActive)
            {
                this.ToggleAutoFarm();
            }

            this.questAssistantCollectMonitorActive = true;
            this.questAssistantCollectMonitorTaskId = taskId;
            this.questAssistantCollectMonitorNextCheckAt = Time.unscaledTime + QuestAssistantCollectMonitorIntervalSeconds;

            string itemSummary = QuestAssistantSummarizeItemNames(itemStaticIds);
            string foragingText = this.autoFarmActive ? "ON" : ("FAILED (" + this.autoFarmStatus + ")");
            this.questAssistantLastStatus =
                "Radar: " + itemSummary + " (" + mappedCount + "/" + itemStaticIds.Count + " mapped)"
                + " | Aura Farm: ON | Foraging: " + foragingText + " (auto-stops when done)";
            this.QuestAssistantLog(
                "Collect action: items=[" + string.Join(",", itemStaticIds) + "] taskId=" + taskId
                + " mapped=" + mappedCount + "/" + itemStaticIds.Count + " autoFarmActive=" + this.autoFarmActive
                + " autoFarmStatus=" + this.autoFarmStatus);
        }

        // Checked from OnUpdate but a no-op unless questAssistantCollectMonitorActive — see field
        // comment above for why this is safe (only reachable after an explicit button click).
        private void QuestAssistantCollectMonitorTick()
        {
            if (!this.questAssistantCollectMonitorActive)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.questAssistantCollectMonitorNextCheckAt)
            {
                return;
            }

            this.questAssistantCollectMonitorNextCheckAt = now + QuestAssistantCollectMonitorIntervalSeconds;

            try
            {
                this.QuestAssistantResolveSnapshot(false);
            }
            catch (Exception ex)
            {
                this.QuestAssistantLog("Collect monitor EXCEPTION, stopping monitor: " + ex);
                this.questAssistantCollectMonitorActive = false;
                return;
            }

            QuestSnapshot stillActive = null;
            for (int i = 0; i < this.questAssistantSnapshot.Count; i++)
            {
                if (this.questAssistantSnapshot[i].TaskId == this.questAssistantCollectMonitorTaskId)
                {
                    stillActive = this.questAssistantSnapshot[i];
                    break;
                }
            }

            // No longer in the active list at all (submitted/completed/removed) counts as done too.
            bool done = stillActive == null;
            if (!done && stillActive.Conditions.Count > 0)
            {
                ConditionSnapshot c = stillActive.Conditions[0];
                done = c.Needed > 0 && c.Current >= c.Needed;
            }

            if (done)
            {
                this.QuestAssistantStopCollectMonitor("target reached");
                this.QuestAssistantTryAutoSubmitIfReady(this.questAssistantCollectMonitorTaskId, "Collect monitor");
            }
        }

        // Shared by every monitor's "target reached" branch (Collect, Bird, ...): reaching the
        // condition target stops the farm/catch, but the order-board quest itself only flips to
        // CanSubmit — it still needs a real submit call to actually finish (confirmed 2026-07-02,
        // Bird Info Card test: the order flipped to CanSubmit on its own but the mod still had to
        // fire the same call the manual "Complete via NPC" button uses). This fires that call
        // automatically so "target reached" means the whole order completes, not just "farm off,
        // wait for a click." Re-reads the JUST-refreshed questAssistantSnapshot (the caller always
        // resolves immediately before checking "done") rather than resolving again. If the task
        // already left the active list entirely (state=Finished/gone) there's nothing to submit — the
        // quest already completed on its own (e.g. non-item catch-tracked quests). If it's CanSubmit
        // but has no submitNpc (the rarer world-object submit path, TaskProtocolManager's "else"
        // branch), this intentionally does nothing — that path isn't wired to any button yet either,
        // so leave it for a manual submit rather than guessing. See progress doc §32.
        private void QuestAssistantTryAutoSubmitIfReady(int taskId, string monitorName)
        {
            QuestSnapshot snap = null;
            for (int i = 0; i < this.questAssistantSnapshot.Count; i++)
            {
                if (this.questAssistantSnapshot[i].TaskId == taskId)
                {
                    snap = this.questAssistantSnapshot[i];
                    break;
                }
            }

            if (snap == null)
            {
                this.QuestAssistantLog(monitorName + ": auto-submit check taskId=" + taskId + " — already off the active list, nothing to submit");
                return;
            }

            if (snap.State != QuestAssistantStateCanSubmit || snap.SubmitNpcId <= 0)
            {
                this.QuestAssistantLog(monitorName + ": auto-submit check taskId=" + taskId + " state=" + snap.State + " submitNpc=" + snap.SubmitNpcId + " — not auto-submittable, leaving for manual submit");
                return;
            }

            this.QuestAssistantLog(monitorName + ": auto-submitting taskId=" + taskId + " submitNpc=" + snap.SubmitNpcId);
            this.QuestAssistantOnSubmitToNpcClicked(snap);
        }

        private void QuestAssistantStopCollectMonitor(string reason)
        {
            int taskId = this.questAssistantCollectMonitorTaskId;
            this.questAssistantCollectMonitorActive = false;
            this.SetAuraFarmEnabled(false);
            if (this.autoFarmActive)
            {
                this.ToggleAutoFarm(); // real STOP FORAGING — also resets game speed to 1x, clears farm state
            }

            this.questAssistantLastStatus = "Collect done (" + reason + ") — Aura Farm/Foraging stopped.";
            this.QuestAssistantLog("Collect monitor stop: taskId=" + taskId + " reason=" + reason);
        }

        // ===== CatchBird button: run BirdNetFarm + auto-exchange photos, auto-stop when done =====
        //
        // Unlike Collect, catching birds alone isn't enough for order-board "Request: Bird Info Card"
        // quests (progress doc §31): BirdNetFarm's photo catches land as raw entityType=504 backpack
        // items, and the quest's condition (checkParamString="TaskSubmitState", no trackMark) counts
        // "Bird Info Card" — a DIFFERENT item produced by exchanging those photos via
        // BirdProtocolManager.BirdPhotoExchange (BirdPhotoSubmitFeature.cs's already-shipped "Submit
        // Bird Photo" button/coroutine, subject to BirdStandSystem's own daily exchange limit). So
        // this monitor does two independent things on its own cadence: periodically calls the
        // existing StartBirdPhotoAutoSubmit(silent:true) (safe to call repeatedly — it no-ops
        // gracefully if the bag is empty, the daily limit is hit, or a submit is already running),
        // and periodically re-resolves the snapshot to see if the quest's own progress (which reflects
        // live "Bird Info Card" ownership, not catch count) has reached its target.
        private const float QuestAssistantBirdMonitorIntervalSeconds = 5f;   // matches the Collect monitor's cadence
        private const float QuestAssistantBirdExchangeIntervalSeconds = 15f; // don't hammer BirdPhotoExchange every tick
        private bool questAssistantBirdMonitorActive = false;
        private int questAssistantBirdMonitorTaskId = 0;
        private float questAssistantBirdMonitorNextCheckAt = 0f;
        private float questAssistantBirdMonitorNextExchangeAt = 0f;

        private void QuestAssistantStartCatchBird(int taskId)
        {
            BirdNetFarm.SetEnabled(true, this);
            this.questAssistantBirdMonitorActive = true;
            this.questAssistantBirdMonitorTaskId = taskId;
            float now = Time.unscaledTime;
            this.questAssistantBirdMonitorNextCheckAt = now + QuestAssistantBirdMonitorIntervalSeconds;
            this.questAssistantBirdMonitorNextExchangeAt = now; // exchange immediately if photos are already sitting in the bag
            this.questAssistantLastStatus = "Bird farm: ON — auto-catching + auto-exchanging photos for Bird Info Cards (auto-stops when done).";
            this.QuestAssistantLog("CatchBird: started taskId=" + taskId);
        }

        // Same reachable-only-after-an-explicit-click shape as the other monitors — see field comment
        // precedent on questAssistantCollectMonitorActive.
        private void QuestAssistantBirdMonitorTick()
        {
            if (!this.questAssistantBirdMonitorActive)
            {
                return;
            }

            float now = Time.unscaledTime;

            if (now >= this.questAssistantBirdMonitorNextExchangeAt)
            {
                this.questAssistantBirdMonitorNextExchangeAt = now + QuestAssistantBirdExchangeIntervalSeconds;
                this.StartBirdPhotoAutoSubmit(silent: true);
            }

            if (now < this.questAssistantBirdMonitorNextCheckAt)
            {
                return;
            }

            this.questAssistantBirdMonitorNextCheckAt = now + QuestAssistantBirdMonitorIntervalSeconds;

            try
            {
                this.QuestAssistantResolveSnapshot(false);
            }
            catch (Exception ex)
            {
                this.QuestAssistantLog("Bird monitor EXCEPTION, stopping monitor: " + ex);
                this.questAssistantBirdMonitorActive = false;
                return;
            }

            QuestSnapshot stillActive = null;
            for (int i = 0; i < this.questAssistantSnapshot.Count; i++)
            {
                if (this.questAssistantSnapshot[i].TaskId == this.questAssistantBirdMonitorTaskId)
                {
                    stillActive = this.questAssistantSnapshot[i];
                    break;
                }
            }

            // No longer in the active list at all (submitted/completed) counts as done too.
            bool done = stillActive == null;
            if (!done && stillActive.Conditions.Count > 0)
            {
                ConditionSnapshot c = stillActive.Conditions[0];
                done = c.Needed > 0 && c.Current >= c.Needed;
            }

            if (done)
            {
                this.QuestAssistantStopBirdMonitor("target reached");
                this.QuestAssistantTryAutoSubmitIfReady(this.questAssistantBirdMonitorTaskId, "Bird monitor");
            }
        }

        private void QuestAssistantStopBirdMonitor(string reason)
        {
            int taskId = this.questAssistantBirdMonitorTaskId;
            this.questAssistantBirdMonitorActive = false;
            BirdNetFarm.SetEnabled(false, this);
            this.questAssistantLastStatus = "Bird farm done (" + reason + ") — stopped.";
            this.QuestAssistantLog("Bird monitor stop: taskId=" + taskId + " reason=" + reason);
        }

        // ===== Craft button: make the quest's recipe remotely, then watch for completion =====
        //
        // "Fishing: Bait" (progress doc §34): condition checkParamString="ItemMade" (checkType=2
        // EventAccumulate — progress increments from a game EVENT, not a live state-check like
        // TaskSubmitState), typeParam="20511", which resolves via TableData.GetRecipe — the
        // classifier now prefers this over the Furniture(12) trackMark's id (330001 recurs across
        // MANY craft quests — it's a generic "at a Workbench" location marker, not the recipe).
        //
        // MakeItemCommand (the client's own InteractSetting=10020 UIClickCommand wrapper,
        // ilspy-dumps/XDTLevelAndEntity/.../MakeItemCommand.cs) requires
        // FocusUIStatus.FocusLevelObject != 0 before it even calls CraftProtocolManager.MakeItem —
        // but that's a CLIENT-SIDE UX gate on the wrapper, not something the wire command itself
        // carries: MakeItemNetworkCommand (ilspy-dumps/EcsClient/XDT.Scene.Shared.Modules.
        // CraftingManual/MakeItemNetworkCommand.cs) is a plain [NetworkCommand] with NO
        // [VerifyEntity] and no position/netId field at all — same self-report shape as
        // TalkWithPlayerCommand/SubmitGameTaskItem2NpcCommand, both already confirmed
        // position-independent this session. So this calls CraftProtocolManager.MakeItem(recipeId,
        // 1, null) directly, bypassing MakeItemCommand's gate entirely (same approach that already
        // worked for Talk/Submit). Whether the SERVER independently validates Workbench proximity is
        // the one thing that can't be determined from client decompiles alone — the watcher below
        // (mirrors QuestAssistantArmTalkToNpcMonitor) makes the first test conclusive.
        //
        // Materials are checked BEFORE attempting the craft (TableRecipe.materials[i] = [materialId,
        // materialCount] pairs — CraftSystem.cs:116 — read via BackPackSystem.GetUsableItemCount,
        // the SAME call CraftRecipeData.CanMake itself uses client-side to grey out the craft
        // button) so a doomed attempt reports "missing materials" instead of silently no-op'ing.
        private const float QuestAssistantCraftMonitorIntervalSeconds = 2f;
        private const float QuestAssistantCraftMonitorTimeoutSeconds = 30f;
        private bool questAssistantCraftMonitorActive = false;
        private int questAssistantCraftMonitorTaskId = 0;
        private int questAssistantCraftMonitorBeforeCurrent = 0;
        private float questAssistantCraftMonitorNextCheckAt = 0f;
        private float questAssistantCraftMonitorDeadlineAt = 0f;

        private void QuestAssistantOnCraftClicked(QuestSnapshot focused, int recipeId)
        {
            if (recipeId <= 0)
            {
                this.questAssistantLastStatus = "No recipe id resolved for this quest.";
                return;
            }

            this.QuestAssistantLog(
                "Craft: taskId=" + focused.TaskId + " recipeId=" + recipeId
                + " progressBefore=" + QuestAssistantSummarizeProgress(focused));

            if (!this.QuestAssistantTryGetRecipeMaterialsAura(recipeId, out List<(int itemId, int count)> materials, out string matStatus))
            {
                this.questAssistantLastStatus = "Could not read recipe #" + recipeId + " materials: " + matStatus;
                this.QuestAssistantLog("Craft: recipe read FAILED recipeId=" + recipeId + " status=" + matStatus);
                return;
            }

            string haveNeed = string.Empty;
            bool allSatisfied = true;
            for (int i = 0; i < materials.Count; i++)
            {
                int need = materials[i].count;
                this.QuestAssistantTryGetUsableItemCountAura(materials[i].itemId, out int have, out string countStatus);
                bool ok = have >= need;
                allSatisfied = allSatisfied && ok;
                haveNeed += (haveNeed.Length > 0 ? ", " : string.Empty) + "item#" + materials[i].itemId + " " + have + "/" + need + (ok ? " OK" : " SHORT");
            }

            this.QuestAssistantLog("Craft: recipeId=" + recipeId + " materials=[" + haveNeed + "]");

            if (!allSatisfied)
            {
                this.questAssistantLastStatus = "Missing materials for recipe #" + recipeId + ": " + haveNeed;
                return;
            }

            if (!this.QuestAssistantTryMakeItemAura(recipeId, 1, out string makeStatus))
            {
                this.questAssistantLastStatus = "Craft failed: " + makeStatus;
                this.QuestAssistantLog("Craft: MakeItem FAILED recipeId=" + recipeId + " status=" + makeStatus);
                return;
            }

            this.QuestAssistantLog("Craft: MakeItem invoked OK recipeId=" + recipeId + " — arming watcher");
            this.QuestAssistantArmCraftMonitor(focused);
            this.questAssistantLastStatus =
                "Craft sent for recipe #" + recipeId + " (materials: " + haveNeed + ") — checking progress for "
                + (int)QuestAssistantCraftMonitorTimeoutSeconds + "s.";
        }

        private void QuestAssistantArmCraftMonitor(QuestSnapshot focused)
        {
            int before = 0;
            if (focused.Conditions != null && focused.Conditions.Count > 0)
            {
                before = focused.Conditions[0].Current;
            }

            this.questAssistantCraftMonitorActive = true;
            this.questAssistantCraftMonitorTaskId = focused.TaskId;
            this.questAssistantCraftMonitorBeforeCurrent = before;
            float now = Time.unscaledTime;
            this.questAssistantCraftMonitorNextCheckAt = now + QuestAssistantCraftMonitorIntervalSeconds;
            this.questAssistantCraftMonitorDeadlineAt = now + QuestAssistantCraftMonitorTimeoutSeconds;
            this.QuestAssistantLog("Craft monitor: armed taskId=" + focused.TaskId + " before=" + before);
        }

        // Same reachable-only-after-an-explicit-click shape as the other monitors.
        private void QuestAssistantCraftMonitorTick()
        {
            if (!this.questAssistantCraftMonitorActive)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now >= this.questAssistantCraftMonitorDeadlineAt)
            {
                this.QuestAssistantConcludeCraftMonitor(false, this.questAssistantCraftMonitorBeforeCurrent, "timeout");
                return;
            }

            if (now < this.questAssistantCraftMonitorNextCheckAt)
            {
                return;
            }

            this.questAssistantCraftMonitorNextCheckAt = now + QuestAssistantCraftMonitorIntervalSeconds;

            try
            {
                this.QuestAssistantResolveSnapshot(false);
            }
            catch (Exception ex)
            {
                this.QuestAssistantLog("Craft monitor EXCEPTION, stopping monitor: " + ex);
                this.questAssistantCraftMonitorActive = false;
                return;
            }

            QuestSnapshot stillActive = null;
            for (int i = 0; i < this.questAssistantSnapshot.Count; i++)
            {
                if (this.questAssistantSnapshot[i].TaskId == this.questAssistantCraftMonitorTaskId)
                {
                    stillActive = this.questAssistantSnapshot[i];
                    break;
                }
            }

            if (stillActive == null)
            {
                this.QuestAssistantConcludeCraftMonitor(true, this.questAssistantCraftMonitorBeforeCurrent, "task left active list (submitted/completed)");
                return;
            }

            if (stillActive.Conditions != null && stillActive.Conditions.Count > 0)
            {
                ConditionSnapshot c = stillActive.Conditions[0];
                bool changed = c.Current != this.questAssistantCraftMonitorBeforeCurrent;
                bool satisfied = c.Needed > 0 && c.Current >= c.Needed;
                if (changed || satisfied)
                {
                    this.QuestAssistantConcludeCraftMonitor(true, c.Current, satisfied ? "condition satisfied" : "progress changed");
                }
            }
        }

        // Single disarm/log/status point, same "RESULT" prefix convention as TalkToNpc's watcher —
        // grep "Craft RESULT" surfaces exactly the possible conclusions.
        private void QuestAssistantConcludeCraftMonitor(bool changed, int afterCurrent, string reason)
        {
            int taskId = this.questAssistantCraftMonitorTaskId;
            int before = this.questAssistantCraftMonitorBeforeCurrent;
            this.questAssistantCraftMonitorActive = false;

            if (changed)
            {
                this.questAssistantLastStatus =
                    "Craft RESULT: progress CHANGED (before=" + before + " after=" + afterCurrent + ") "
                    + "— the craft DID count toward the quest. (" + reason + ")";
                this.QuestAssistantLog(
                    "Craft RESULT: progress CHANGED (before=" + before + " after=" + afterCurrent
                    + ") — the craft DID count toward the quest. taskId=" + taskId + " reason=" + reason);
            }
            else
            {
                this.questAssistantLastStatus =
                    "Craft RESULT: NO progress change after " + (int)QuestAssistantCraftMonitorTimeoutSeconds
                    + "s — either the craft failed silently (materials/recipe mismatch), or the server "
                    + "requires being physically at a Workbench.";
                this.QuestAssistantLog(
                    "Craft RESULT: NO progress change after " + QuestAssistantCraftMonitorTimeoutSeconds
                    + "s taskId=" + taskId + " before=" + before);
            }
        }

        // ===== GoToArea button: teleport to a "be in area X" quest's navigation point =====
        //
        // "Astralis in Fishing Village" (progress doc §40-§43): condition checkParamString=
        // "PlayerInSpecificArea", typeParam="144" (a NAVIGATION-POINT id, matching the NaviPoint(1)
        // trackMark id). Satisfied by the player being physically IN that area — the game runs its own
        // client-side area check off the player's live position, so teleporting there and letting the
        // check fire is the natural completion, no direct wire command needed.
        //
        // Position source (§43, after three wrong turns): the ONLY correct position is
        // NavigationPointConfig.GetNavigationPoint(144).position. Map-spot scanning was wrong — the
        // quest's own TrackTask Navigation spot is dropped on scene reload and not re-added (§40/§42,
        // and force-tracking doesn't re-add it — tracking dispatches AddOrUpdateTrackTaskEvent, not the
        // TaskUpdated that UpdateTaskMapSpot needs), while the only present usageId=144 spot is a
        // RegionName in a DIFFERENT id space (map-element id 144 ≠ navpoint id 144, coincidental
        // collision → teleported to an unrelated region, §41/§42). So read the navpoint config
        // directly: the config lives behind the generic Managers.Get<IConfigManager>() (a crash trap
        // to invoke — §37), so instead read its static backing dictionary Managers._serviceDic
        // (Dictionary<Type,ServiceObject>) with a Type key built via the proven
        // TryCreateAuraMonoSystemTypeObjectFromClass (mono_class_get_type + mono_type_get_object — NOT
        // the Type.GetType string parser that crashed in §35), then field-walk:
        // ServiceObject.manager (ConfigManager) → _mainGameLvlConf (GameLevelBaseConfig) →
        // NavigationPointConfig (public field) → navigationPoints (List<NavigationPointData{int id;
        // Vector3 position}>) → the entry with id==144. Uses only proven primitives (static-field read,
        // TryGetValue on an ALREADY-INSTANTIATED generic type — safe, unlike the §37 runtime-inflated
        // case — list enumeration, field reads); no generic-method inflation, no Nullable, no
        // Type.GetType string.
        private const float QuestAssistantGoToAreaMonitorIntervalSeconds = 2f;
        private const float QuestAssistantGoToAreaMonitorTimeoutSeconds = 20f;
        private bool questAssistantGoToAreaMonitorActive = false;
        private int questAssistantGoToAreaMonitorTaskId = 0;
        private int questAssistantGoToAreaMonitorBeforeCurrent = 0;
        private float questAssistantGoToAreaMonitorNextCheckAt = 0f;
        private float questAssistantGoToAreaMonitorDeadlineAt = 0f;

        private void QuestAssistantOnGoToAreaClicked(QuestSnapshot focused, int areaId)
        {
            if (areaId <= 0)
            {
                this.questAssistantLastStatus = "No area id resolved for this quest.";
                return;
            }

            this.QuestAssistantLog(
                "GoToArea: taskId=" + focused.TaskId + " areaId=" + areaId
                + " progressBefore=" + QuestAssistantSummarizeProgress(focused));

            if (!this.QuestAssistantTryGetNavPointConfigPosition(areaId, out Vector3 areaPos, out string posStatus))
            {
                this.questAssistantLastStatus = "Could not resolve navpoint #" + areaId + " position: " + posStatus;
                this.QuestAssistantLog("GoToArea: navpoint resolve FAILED areaId=" + areaId + " status=" + posStatus);
                return;
            }

            this.QuestAssistantLog("GoToArea: teleporting to navpoint #" + areaId + " at " + areaPos.ToString("F1"));
            this.TeleportToLocation(areaPos);

            this.QuestAssistantArmGoToAreaMonitor(focused);
            this.questAssistantLastStatus =
                "Teleported to area #" + areaId + " (" + areaPos.ToString("F0") + "). Auto-checking progress for "
                + (int)QuestAssistantGoToAreaMonitorTimeoutSeconds + "s (the game credits it once you're in the area).";
        }

        // ===== HomelandFarm button: run the existing Homeland Farm auto-fertilize / auto-sow =====
        //
        // "Gardening: Fertilizer" (progress doc §45): the active step is a UseCropFertilizer condition
        // ("Apply fertilizer to crops", 0/N) — classified HomelandFarm now that the classifier skips
        // the already-done Craft condition. HomelandFarm also covers SowPlant quests. Both just reuse
        // the ready Homeland Farm feature entry points (StartHomelandFarmFertilizeAll /
        // StartHomelandFarmSowAll) — they scan crops in the configured radius and apply the selected
        // fertilizer/seed, exactly what the quest needs. No new farm logic; the quest's progress then
        // updates in the panel via the §30 auto-refresh as applications register.
        private bool QuestAssistantActiveHomelandFarmIsFertilize(QuestSnapshot focused)
        {
            // Default to fertilize. Pick sow only if the first INCOMPLETE condition is a SowPlant.
            if (focused.Conditions != null)
            {
                for (int i = 0; i < focused.Conditions.Count; i++)
                {
                    ConditionSnapshot c = focused.Conditions[i];
                    if (c.Needed > 0 && c.Current >= c.Needed)
                    {
                        continue; // skip completed step
                    }

                    if (string.Equals(c.CheckParamString, "SowPlant", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (string.Equals(c.CheckParamString, "UseCropFertilizer", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return true;
        }

        // How many applications the active (first incomplete) sow/fertilize step still needs, so the
        // quest button consumes only what the quest requires instead of every crop in radius. Returns
        // int.MaxValue when it can't tell — that leaves the Homeland Farm "do everything" behavior.
        private int QuestAssistantActiveHomelandFarmRemaining(QuestSnapshot focused)
        {
            if (focused.Conditions != null)
            {
                for (int i = 0; i < focused.Conditions.Count; i++)
                {
                    ConditionSnapshot c = focused.Conditions[i];
                    if (c.Needed > 0 && c.Current >= c.Needed)
                    {
                        continue; // skip completed step
                    }

                    if (string.Equals(c.CheckParamString, "SowPlant", StringComparison.Ordinal)
                        || string.Equals(c.CheckParamString, "UseCropFertilizer", StringComparison.Ordinal))
                    {
                        int remaining = c.Needed - c.Current;
                        return remaining > 0 ? remaining : 1;
                    }
                }
            }

            return int.MaxValue;
        }

        // Which FertilizerEffectTypeEnum the active step wants applied. Returns
        // HomelandFarmFertilizerEffectGrowthRate (1) for an "Apply Growth Booster" step, else -1 (no
        // override → the button uses the user's Homeland Farm dropdown selection). A booster fires the
        // SAME UseCropFertilizer event and shares its checkParamString, so the objective text is what
        // distinguishes the two steps; checkParam/typeParam are logged (once, on click) so we can
        // switch to a language-independent discriminator later if the text match ever proves fragile.
        // NOTE: called every OnGUI frame from the button label — must NOT log unless log==true.
        private int QuestAssistantActiveHomelandFarmEffectType(QuestSnapshot focused, bool log = false)
        {
            if (focused.Conditions == null)
            {
                return -1;
            }

            for (int i = 0; i < focused.Conditions.Count; i++)
            {
                ConditionSnapshot c = focused.Conditions[i];
                if (c.Needed > 0 && c.Current >= c.Needed)
                {
                    continue; // skip completed step
                }

                if (!string.Equals(c.CheckParamString, "UseCropFertilizer", StringComparison.Ordinal))
                {
                    continue;
                }

                if (log)
                {
                    this.QuestAssistantLog("HomelandFarm effect probe: desc=\"" + (c.Description ?? string.Empty)
                        + "\" checkParam=" + c.CheckParam + " typeParam=\"" + (c.TypeParam ?? string.Empty) + "\"");
                }

                string desc = (c.Description ?? string.Empty).ToLowerInvariant();
                if (desc.Contains("booster") || desc.Contains("growth boost"))
                {
                    return HomelandFarmFertilizerEffectGrowthRate; // 1 = GrowthSpeedPromote
                }

                return -1;
            }

            return -1;
        }

        // The exact fertilizer item id the active UseCropFertilizer step requires, read from the
        // condition's typeParam (confirmed 2026-07-03: typeParam="770201" carried the growth-booster
        // item id, matching the scanned inventory item exactly). 0 = none/unparseable → the apply path
        // falls back to effect-type/dropdown selection. This is the quest's ground-truth,
        // language-independent discriminator and works even when the fertilizer table can't resolve the
        // item's effect type.
        private int QuestAssistantActiveHomelandFarmRequiredStaticId(QuestSnapshot focused)
        {
            if (focused.Conditions == null)
            {
                return 0;
            }

            for (int i = 0; i < focused.Conditions.Count; i++)
            {
                ConditionSnapshot c = focused.Conditions[i];
                if (c.Needed > 0 && c.Current >= c.Needed)
                {
                    continue; // skip completed step
                }

                if (!string.Equals(c.CheckParamString, "UseCropFertilizer", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(c.TypeParam) && int.TryParse(c.TypeParam.Trim(), out int id) && id > 0)
                {
                    return id;
                }

                return 0;
            }

            return 0;
        }

        private int QuestAssistantGetEntityTypeCached(int staticId)
        {
            if (staticId <= 0)
            {
                return 0;
            }

            if (this.questAssistantEntityTypeCache.TryGetValue(staticId, out int cached))
            {
                return cached;
            }

            int decoded = 0;
            try
            {
                if (!this.TryHomelandFarmGetEntityTypeForStaticId(staticId, out decoded))
                {
                    decoded = 0;
                }
            }
            catch
            {
                decoded = 0;
            }

            this.questAssistantEntityTypeCache[staticId] = decoded;
            this.QuestAssistantLog("entityType decode: staticId=" + staticId + " -> " + decoded);
            return decoded;
        }

        // Whether the active SowPlant step's seeds are CROP seeds — the only thing the sow-in-radius
        // command (CropProtocolManager.CropSeeding into crop planters) can plant. Flower seeds decode
        // to a different EntityType (seedbag=26 vs cropseed=2801) and belong to the separate
        // flower-bed system, so the sow button is disabled for them (§50). Fail-open: if the seed id
        // or the entity types can't be resolved, the button stays enabled and the sow run reports its
        // own errors as before.
        private bool QuestAssistantActiveSowSeedsAreCropSeeds(QuestSnapshot focused, out int blockedSeedId, out int blockedType)
        {
            blockedSeedId = 0;
            blockedType = 0;
            if (focused.Conditions == null)
            {
                return true;
            }

            for (int i = 0; i < focused.Conditions.Count; i++)
            {
                ConditionSnapshot c = focused.Conditions[i];
                if (c.Needed > 0 && c.Current >= c.Needed)
                {
                    continue; // skip completed step
                }

                if (!string.Equals(c.CheckParamString, "SowPlant", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!this.TryResolveHomelandFarmCropSeedEntityType(out int cropSeedType))
                {
                    return true; // can't resolve the enum — fail open
                }

                List<int> seedIds = QuestAssistantNumericCandidates(c);
                bool sawDecoded = false;
                for (int s = 0; s < seedIds.Count; s++)
                {
                    int decoded = this.QuestAssistantGetEntityTypeCached(seedIds[s]);
                    if (decoded == 0)
                    {
                        continue; // decode failed for this id — ignore it
                    }

                    sawDecoded = true;
                    if (decoded == cropSeedType)
                    {
                        return true; // at least one required seed is a crop seed — sowable
                    }

                    blockedSeedId = seedIds[s];
                    blockedType = decoded;
                }

                // Every decodable required seed is a non-crop type (flowers) → block; nothing
                // decodable → fail open.
                return !sawDecoded;
            }

            return true; // no active SowPlant condition — nothing to block
        }

        private void QuestAssistantOnHomelandFarmClicked(QuestSnapshot focused, bool fertilize)
        {
            int remaining = this.QuestAssistantActiveHomelandFarmRemaining(focused);
            int effectType = fertilize ? this.QuestAssistantActiveHomelandFarmEffectType(focused, log: true) : -1;
            int requiredStaticId = fertilize ? this.QuestAssistantActiveHomelandFarmRequiredStaticId(focused) : 0;
            bool booster = effectType == HomelandFarmFertilizerEffectGrowthRate;
            this.QuestAssistantLog(
                "HomelandFarm: taskId=" + focused.TaskId + " action=" + (booster ? "growth-booster" : (fertilize ? "fertilize" : "sow"))
                + " item=" + requiredStaticId
                + " cap=" + (remaining == int.MaxValue ? "none" : remaining.ToString())
                + " progressBefore=" + QuestAssistantSummarizeProgress(focused));

            if (fertilize)
            {
                this.StartHomelandFarmFertilizeAll(silent: false, maxCount: remaining, requiredEffectType: effectType, requiredStaticId: requiredStaticId);
            }
            else
            {
                this.StartHomelandFarmSowAll(silent: false, maxCount: remaining);
            }

            string countText = remaining == int.MaxValue ? "crops" : remaining + " crop(s)";
            string verb = booster ? "Applying growth booster to " : (fertilize ? "Fertilizing " : "Sowing ");
            this.questAssistantLastStatus =
                verb + countText + " in the Homeland Farm radius (only what the quest still needs) — watch the "
                + "quest progress update here (panel auto-refreshes).";
        }

        // ===== PurchaseItem — buy the exact quest-required items from whichever store sells them (§49)
        //
        // A PurchaseItem condition's typeParam is the EXACT item staticId to buy (same ground truth as
        // the fertilizer steps, §47.1) and Needed-Current is how many. One task can carry several such
        // conditions at once ("Gardening: Flowers" wants 2× item 700201 AND 2× item 700202), so the
        // button processes every incomplete PurchaseItem condition in one run.
        //
        // Mechanism (all reused from the shipped Auto Buy / Force Shop machinery, zero new game paths):
        //   TryGetAllStoreIdsMono          — every storeId from static TableData.TableStoreInfos
        //   TryFindShopCandidateByStaticIdAura — local ShopSystem.GetStoreGoodsData(storeId) listing,
        //                                    matched on rewardData.staticId (no UI, no server call)
        //   TryInvokeShopBuyItem           — managed by-netId buy, falling back to the
        //                                    ShopShelfProtocolManager.BuyItem server RPC
        // The buy is server-authoritative and fires the same PurchaseItem game event the quest listens
        // for, so progress updates arrive via the TaskUpdated auto-refresh (§30).

        private void QuestAssistantOnPurchaseItemClicked(QuestSnapshot focused)
        {
            if (this.questAssistantPurchaseCoroutine != null)
            {
                this.questAssistantLastStatus = "A quest purchase run is already in progress.";
                return;
            }

            // (staticId, remaining) per incomplete PurchaseItem condition.
            List<KeyValuePair<int, int>> wanted = new List<KeyValuePair<int, int>>();
            System.Text.StringBuilder wantedText = new System.Text.StringBuilder();
            if (focused.Conditions != null)
            {
                for (int i = 0; i < focused.Conditions.Count; i++)
                {
                    ConditionSnapshot c = focused.Conditions[i];
                    if (!string.Equals(c.CheckParamString, "PurchaseItem", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (c.Needed > 0 && c.Current >= c.Needed)
                    {
                        continue; // step already complete
                    }

                    if (string.IsNullOrWhiteSpace(c.TypeParam) || !int.TryParse(c.TypeParam.Trim(), out int staticId) || staticId <= 0)
                    {
                        this.QuestAssistantLog("PurchaseItem: condition[" + i + "] has no parseable typeParam (\"" + (c.TypeParam ?? string.Empty) + "\") — skipped");
                        continue;
                    }

                    int remaining = Math.Max(1, c.Needed - c.Current);
                    wanted.Add(new KeyValuePair<int, int>(staticId, remaining));
                    if (wantedText.Length > 0)
                    {
                        wantedText.Append(", ");
                    }

                    wantedText.Append(staticId).Append("x").Append(remaining);
                }
            }

            this.QuestAssistantLog("PurchaseItem: taskId=" + focused.TaskId + " wanted=[" + wantedText + "] progressBefore=" + QuestAssistantSummarizeProgress(focused));
            if (wanted.Count == 0)
            {
                this.questAssistantLastStatus = "No incomplete purchase steps with an item id.";
                return;
            }

            this.questAssistantLastStatus = "Searching shops for " + wanted.Count + " quest item(s)...";
            this.questAssistantPurchaseCoroutine = ModCoroutines.Start(this.QuestAssistantPurchaseItemsRoutine(wanted));
        }

        private IEnumerator QuestAssistantPurchaseItemsRoutine(List<KeyValuePair<int, int>> wanted)
        {
            yield return null;

            try
            {
                List<int> storeIds = new List<int>();
                bool storesOk = false;
                try
                {
                    storesOk = this.TryGetAllStoreIdsMono(storeIds);
                }
                catch (Exception ex)
                {
                    this.QuestAssistantLog("PurchaseItem: store table enumeration threw: " + ex.Message);
                }

                if (!storesOk || storeIds.Count == 0)
                {
                    this.questAssistantLastStatus = "Shop table unavailable — cannot search stores.";
                    yield break;
                }

                this.QuestAssistantLog("PurchaseItem: searching " + storeIds.Count + " store(s) for " + wanted.Count + " item(s)");

                int boughtKinds = 0;
                int failedKinds = 0;
                for (int wIdx = 0; wIdx < wanted.Count; wIdx++)
                {
                    int staticId = wanted[wIdx].Key;
                    int count = wanted[wIdx].Value;
                    bool resolved = false;

                    for (int sIdx = 0; sIdx < storeIds.Count; sIdx++)
                    {
                        bool found = false;
                        ShopBuyAllCandidate candidate = default(ShopBuyAllCandidate);
                        try
                        {
                            found = this.TryFindShopCandidateByStaticIdAura(storeIds[sIdx], staticId, out candidate);
                        }
                        catch (Exception ex)
                        {
                            this.QuestAssistantLog("PurchaseItem: goods read failed store=" + storeIds[sIdx] + ": " + ex.Message);
                        }

                        if (!found)
                        {
                            // Listing reads are local but not free — spread the store sweep over frames.
                            if ((sIdx & 3) == 3)
                            {
                                yield return null;
                            }

                            continue;
                        }

                        int buyCount = candidate.LeftCount > 0 ? Math.Min(count, candidate.LeftCount) : count;
                        bool ok = false;
                        string buyError = null;
                        try
                        {
                            ok = this.TryInvokeShopBuyItem(candidate, buyCount, out buyError);
                        }
                        catch (Exception ex)
                        {
                            buyError = ex.Message;
                        }

                        if (ok)
                        {
                            boughtKinds++;
                            this.QuestAssistantLog("PurchaseItem: bought item=" + staticId + " x" + buyCount
                                + " store=" + candidate.StoreId + " slot=" + candidate.SlotId + " price=" + candidate.Price
                                + (buyCount < count ? " (capped by stock, wanted " + count + ")" : string.Empty));
                            this.questAssistantLastStatus = "Bought item #" + staticId + " x" + buyCount + " (store " + candidate.StoreId + ").";
                        }
                        else
                        {
                            failedKinds++;
                            this.QuestAssistantLog("PurchaseItem: buy FAILED item=" + staticId + " store=" + candidate.StoreId + ": " + (buyError ?? "unknown"));
                            this.questAssistantLastStatus = "Buy failed for item #" + staticId + ": " + (buyError ?? "unknown");
                        }

                        resolved = true;
                        yield return new WaitForSecondsRealtime(0.4f);
                        break; // next wanted item
                    }

                    if (!resolved)
                    {
                        failedKinds++;
                        this.QuestAssistantLog("PurchaseItem: item=" + staticId + " not found (unlocked) in any of " + storeIds.Count + " store(s)");
                        this.questAssistantLastStatus = "Item #" + staticId + " not sold in any known store.";
                    }
                }

                if (failedKinds == 0)
                {
                    this.questAssistantLastStatus = "Quest purchases done (" + boughtKinds + " item type(s)) — progress updates as the server registers them.";
                }
                else if (boughtKinds > 0)
                {
                    this.questAssistantLastStatus = "Quest purchases: " + boughtKinds + " bought, " + failedKinds + " failed — see log.";
                }
            }
            finally
            {
                this.questAssistantPurchaseCoroutine = null;
            }
        }

        // Read NavigationPointConfig.navigationPoints[i where id==navpointId].position — see the
        // class comment above for why this is the only correct source and why it's read via the static
        // Managers._serviceDic backing dictionary rather than the generic Managers.Get<IConfigManager>().
        private unsafe bool QuestAssistantTryGetNavPointConfigPosition(int navpointId, out Vector3 position, out string status)
        {
            position = Vector3.zero;
            status = "AuraMono unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null
                || auraMonoObjectGetClass == null || auraMonoFieldGetValueObject == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            // 1) System.Type object for IConfigManager (the Dictionary<Type,ServiceObject> key).
            if (!this.TryCreateAuraMonoSystemTypeObjectFromClass("XDTDataAndProtocol.Config.IConfigManager", out IntPtr configTypeObj) || configTypeObj == IntPtr.Zero)
            {
                status = "IConfigManager Type object unresolved";
                return false;
            }

            // 2) static Managers._serviceDic (Dictionary<Type, ServiceObject>).
            IntPtr managersClass = this.FindAuraMonoClassByFullName("XDTGame.Framework.Managers");
            if (managersClass == IntPtr.Zero)
            {
                managersClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.Framework", "Managers");
            }

            IntPtr serviceDicField = managersClass != IntPtr.Zero ? this.FindAuraMonoFieldOnHierarchy(managersClass, "_serviceDic") : IntPtr.Zero;
            if (serviceDicField == IntPtr.Zero)
            {
                status = "Managers._serviceDic field unresolved";
                return false;
            }

            IntPtr serviceDicObj = auraMonoFieldGetValueObject(this.auraMonoRootDomain, serviceDicField, IntPtr.Zero);
            if (serviceDicObj == IntPtr.Zero)
            {
                status = "Managers._serviceDic is null (Managers not started?)";
                return false;
            }

            // 3) _serviceDic.TryGetValue(configTypeObj, out ServiceObject serviceObj). Instance method
            //    of an ALREADY-INSTANTIATED generic type — safe to invoke (unlike a runtime-inflated
            //    generic method, §37). out is a reference type ⇒ pointer-to-local-IntPtr is safe.
            uint dicPin = AuraMonoPinNew(serviceDicObj);
            IntPtr serviceObj;
            try
            {
                IntPtr dicClass = auraMonoObjectGetClass(serviceDicObj);
                IntPtr tryGetValueMethod = dicClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(dicClass, "TryGetValue", 2) : IntPtr.Zero;
                if (tryGetValueMethod == IntPtr.Zero)
                {
                    status = "Dictionary.TryGetValue method missing";
                    return false;
                }

                IntPtr localServiceObj = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = configTypeObj;                 // Type key (reference) — pass object directly.
                args[1] = (IntPtr)(&localServiceObj);    // out ServiceObject (reference out) — ptr to local.
                IntPtr exc = IntPtr.Zero;
                IntPtr result = auraMonoRuntimeInvoke(tryGetValueMethod, serviceDicObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "TryGetValue invoke exception";
                    return false;
                }

                bool got = result != IntPtr.Zero && this.TryUnboxMonoBoolean(result, out bool b) && b;
                if (!got || localServiceObj == IntPtr.Zero)
                {
                    status = "IConfigManager not registered in _serviceDic";
                    return false;
                }

                serviceObj = localServiceObj;
            }
            finally
            {
                AuraMonoPinFree(dicPin);
            }

            // 4) ServiceObject.manager (ConfigManager) → _mainGameLvlConf → NavigationPointConfig →
            //    navigationPoints (List<NavigationPointData>).
            if (!this.TryGetMonoObjectMember(serviceObj, "manager", out IntPtr configManagerObj) || configManagerObj == IntPtr.Zero)
            {
                status = "ServiceObject.manager (ConfigManager) null";
                return false;
            }

            if (!this.TryGetMonoObjectMember(configManagerObj, "_mainGameLvlConf", out IntPtr levelConfObj) || levelConfObj == IntPtr.Zero)
            {
                status = "ConfigManager._mainGameLvlConf null (no scene config loaded?)";
                return false;
            }

            if (!this.TryGetMonoObjectMember(levelConfObj, "NavigationPointConfig", out IntPtr navConfObj) || navConfObj == IntPtr.Zero)
            {
                status = "GameLevelBaseConfig.NavigationPointConfig null";
                return false;
            }

            if (!this.TryGetMonoObjectMember(navConfObj, "navigationPoints", out IntPtr navListObj) || navListObj == IntPtr.Zero)
            {
                status = "NavigationPointConfig.navigationPoints null";
                return false;
            }

            // 5) find the NavigationPointData with id==navpointId, read its position (Vector3).
            List<IntPtr> items = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(navListObj, items, pins))
                {
                    status = "navigationPoints enumerate failed";
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] == IntPtr.Zero
                        || !this.TryGetMonoIntMember(items[i], "id", out int id)
                        || id != navpointId)
                    {
                        continue;
                    }

                    if (this.TryGetMonoVector3Member(items[i], "position", out position) && position != Vector3.zero)
                    {
                        status = "ok";
                        this.QuestAssistantLog("  navpointConfig MATCH id=" + navpointId + " pos=" + position.ToString("F1"));
                        return true;
                    }

                    status = "navpoint " + navpointId + " found but position zero/unreadable";
                    return false;
                }

                status = "no navigation point with id=" + navpointId + " (" + items.Count + " navpoints scanned)";
                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private void QuestAssistantArmGoToAreaMonitor(QuestSnapshot focused)
        {
            int before = 0;
            if (focused.Conditions != null && focused.Conditions.Count > 0)
            {
                before = focused.Conditions[0].Current;
            }

            this.questAssistantGoToAreaMonitorActive = true;
            this.questAssistantGoToAreaMonitorTaskId = focused.TaskId;
            this.questAssistantGoToAreaMonitorBeforeCurrent = before;
            float now = Time.unscaledTime;
            this.questAssistantGoToAreaMonitorNextCheckAt = now + QuestAssistantGoToAreaMonitorIntervalSeconds;
            this.questAssistantGoToAreaMonitorDeadlineAt = now + QuestAssistantGoToAreaMonitorTimeoutSeconds;
            this.QuestAssistantLog("GoToArea monitor: armed taskId=" + focused.TaskId + " before=" + before);
        }

        // Same reachable-only-after-an-explicit-click shape as the other monitors.
        private void QuestAssistantGoToAreaMonitorTick()
        {
            if (!this.questAssistantGoToAreaMonitorActive)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now >= this.questAssistantGoToAreaMonitorDeadlineAt)
            {
                this.QuestAssistantConcludeGoToAreaMonitor(false, this.questAssistantGoToAreaMonitorBeforeCurrent, "timeout");
                return;
            }

            if (now < this.questAssistantGoToAreaMonitorNextCheckAt)
            {
                return;
            }

            this.questAssistantGoToAreaMonitorNextCheckAt = now + QuestAssistantGoToAreaMonitorIntervalSeconds;

            try
            {
                this.QuestAssistantResolveSnapshot(false);
            }
            catch (Exception ex)
            {
                this.QuestAssistantLog("GoToArea monitor EXCEPTION, stopping monitor: " + ex);
                this.questAssistantGoToAreaMonitorActive = false;
                return;
            }

            QuestSnapshot stillActive = null;
            for (int i = 0; i < this.questAssistantSnapshot.Count; i++)
            {
                if (this.questAssistantSnapshot[i].TaskId == this.questAssistantGoToAreaMonitorTaskId)
                {
                    stillActive = this.questAssistantSnapshot[i];
                    break;
                }
            }

            if (stillActive == null)
            {
                this.QuestAssistantConcludeGoToAreaMonitor(true, this.questAssistantGoToAreaMonitorBeforeCurrent, "task left active list (completed)");
                return;
            }

            if (stillActive.Conditions != null && stillActive.Conditions.Count > 0)
            {
                ConditionSnapshot c = stillActive.Conditions[0];
                bool changed = c.Current != this.questAssistantGoToAreaMonitorBeforeCurrent;
                bool satisfied = c.Needed > 0 && c.Current >= c.Needed;
                if (changed || satisfied)
                {
                    this.QuestAssistantConcludeGoToAreaMonitor(true, c.Current, satisfied ? "condition satisfied" : "progress changed");
                }
            }
        }

        private void QuestAssistantConcludeGoToAreaMonitor(bool changed, int afterCurrent, string reason)
        {
            int taskId = this.questAssistantGoToAreaMonitorTaskId;
            int before = this.questAssistantGoToAreaMonitorBeforeCurrent;
            this.questAssistantGoToAreaMonitorActive = false;

            if (changed)
            {
                this.questAssistantLastStatus =
                    "GoToArea RESULT: progress CHANGED (before=" + before + " after=" + afterCurrent + ") "
                    + "— being in the area DID count toward the quest. (" + reason + ")";
                this.QuestAssistantLog(
                    "GoToArea RESULT: progress CHANGED (before=" + before + " after=" + afterCurrent
                    + ") — being in the area DID count toward the quest. taskId=" + taskId + " reason=" + reason);
            }
            else
            {
                this.questAssistantLastStatus =
                    "GoToArea RESULT: NO progress change after " + (int)QuestAssistantGoToAreaMonitorTimeoutSeconds
                    + "s — teleport landed but the area check didn't credit it (wrong spot position, or the "
                    + "server validates differently). Try walking a step inside the marked area.";
                this.QuestAssistantLog(
                    "GoToArea RESULT: NO progress change after " + QuestAssistantGoToAreaMonitorTimeoutSeconds
                    + "s taskId=" + taskId + " before=" + before);
            }
        }

        // TableData.GetRecipe(recipeId[, needException]) row -> .materials (int[][], jagged: each
        // inner array is [materialId, materialCount] — CraftSystem.cs:116 confirms this exact shape).
        private unsafe bool QuestAssistantTryGetRecipeMaterialsAura(int recipeId, out List<(int itemId, int count)> materials, out string status)
        {
            materials = new List<(int, int)>();
            status = "AuraMono unavailable";
            IntPtr tableDataClass = this.TryGetDailyQuestProbeTableDataClass(out string classStatus);
            if (tableDataClass == IntPtr.Zero || auraMonoRuntimeInvoke == null || !this.AttachAuraMonoThread())
            {
                status = classStatus;
                return false;
            }

            IntPtr method1 = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetRecipe", 1);
            IntPtr method2 = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetRecipe", 2);
            IntPtr method = method1 != IntPtr.Zero ? method1 : method2;
            if (method == IntPtr.Zero)
            {
                status = "GetRecipe method missing";
                return false;
            }

            int localId = recipeId;
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

            if (exc != IntPtr.Zero || row == IntPtr.Zero)
            {
                status = "GetRecipe(" + recipeId + ") not found";
                return false;
            }

            if (!this.TryGetMonoObjectMember(row, "materials", out IntPtr materialsArrayObj) || materialsArrayObj == IntPtr.Zero)
            {
                status = "recipe has no materials array";
                return false;
            }

            List<IntPtr> outerItems = new List<IntPtr>();
            List<uint> outerPins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(materialsArrayObj, outerItems, outerPins))
                {
                    status = "materials array enumerate failed";
                    return false;
                }

                for (int i = 0; i < outerItems.Count; i++)
                {
                    IntPtr innerArrayObj = outerItems[i];
                    if (innerArrayObj == IntPtr.Zero || auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null)
                    {
                        continue;
                    }

                    ulong innerLen = auraMonoArrayLength(innerArrayObj).ToUInt64();
                    if (innerLen < 2)
                    {
                        continue;
                    }

                    IntPtr idAddr = auraMonoArrayAddrWithSize(innerArrayObj, sizeof(int), (UIntPtr)0);
                    IntPtr countAddr = auraMonoArrayAddrWithSize(innerArrayObj, sizeof(int), (UIntPtr)1);
                    if (idAddr == IntPtr.Zero || countAddr == IntPtr.Zero)
                    {
                        continue;
                    }

                    materials.Add((Marshal.ReadInt32(idAddr), Marshal.ReadInt32(countAddr)));
                }
            }
            finally
            {
                FreeAuraMonoPins(outerPins);
            }

            status = "ok, " + materials.Count + " material(s)";
            return true;
        }

        // BackPackSystem.GetUsableItemCount(int staticId, bool inSelfField=false) — the SAME call
        // CraftSystem.CheckCanMake (which backs CraftRecipeData.CanMake, the client's own "grey out
        // the craft button" logic) uses per material. Arity 2 and unambiguous (only one
        // GetUsableItemCount overload exists, unlike GetItemCount which has two conflicting arity-2
        // overloads — deliberately avoided for that reason).
        private unsafe bool QuestAssistantTryGetUsableItemCountAura(int itemStaticId, out int count, out string status)
        {
            count = 0;
            status = "AuraMono unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackObj) || backPackObj == IntPtr.Zero)
            {
                status = "BackPackSystem unavailable";
                return false;
            }

            IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackObj) : IntPtr.Zero;
            IntPtr method = backPackClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetUsableItemCount", 2) : IntPtr.Zero;
            if (method == IntPtr.Zero)
            {
                status = "GetUsableItemCount method missing";
                return false;
            }

            int localId = itemStaticId;
            bool inSelfField = false;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&localId);
            args[1] = (IntPtr)(&inSelfField);
            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(method, backPackObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || result == IntPtr.Zero || !this.TryUnboxMonoInt32(result, out count))
            {
                status = "GetUsableItemCount invoke failed";
                return false;
            }

            status = "ok";
            return true;
        }

        // CraftProtocolManager.MakeItem(int recipeId, int count, int? themeId) — static, NON-generic,
        // arity 3. This is the same "call the game's own ProtocolManager static wrapper" pattern every
        // other working network send in this mod uses (TalkProtocolManager.SendTalkWithNpc,
        // TaskProtocolManager.ClientSubmitTaskItem, BirdProtocolManager.BirdPhotoExchange) — the
        // wrapper builds MakeItemNetworkCommand and calls WebRequestUtility.SendCommand<T> INTERNALLY,
        // inside normally-JIT'd game code, so we never touch a generic method ourselves.
        //
        // Two earlier attempts crashed and are DELIBERATELY abandoned (progress doc §35/§36/§37):
        //   §35 — called MakeItem but built the int? via Type.GetType("System.Nullable`1[[...]]") +
        //         Activator.CreateInstance → native crash in RuntimeTypeHandle:internal_from_name
        //         (icall.c) — Type.GetType string-parsing a BCL-generic-over-BCL type crashes this
        //         Mono build. See [[auramono-bcl-generic-typegettype-crash]].
        //   §36/§37 — bypassed MakeItem, built MakeItemNetworkCommand and invoked the INFLATED generic
        //         WebRequestUtility.SendCommand<T> directly via mono_runtime_invoke → crashed instantly
        //         even after pinning. Root cause: this mod has NEVER mono_runtime_invoke'd a
        //         runtime-inflated generic method — TryInflateDispatchForEvent's output is only ever
        //         fed to mono_compile_method + NativeDetour (never invoked). Directly invoking an
        //         inflated generic instance is unsafe on this build (no valid runtime-invoke wrapper /
        //         missing generic sharing context). DO NOT mono_runtime_invoke inflated generic methods.
        //
        // The int? themeId — the ONLY reason the §35/§36 detours were attempted — needs no type
        // construction at all, BUT the naive "pass a pointer to the raw Nullable struct bytes" is ALSO
        // wrong and crashes (§37): Nullable<T> parameters are SPECIAL-CASED in mono's runtime-invoke
        // wrapper. Unlike a normal value type (params[i] = pointer to raw bytes), a Nullable<T> arg is
        // passed as a BOXED MonoObject* — NULL for the null case, a boxed T otherwise — and mono calls
        // mono_nullable_init(buf, (MonoObject*)params[i], klass) internally. §37 passed a pointer to 8
        // stack bytes; mono treated it as a MonoObject*, did mono_object_unbox on stack garbage, and
        // crashed with an EMPTY managed stack (native fault inside nullable_init, below any managed
        // frame — exactly the dump signature seen). So for themeId=null the correct argument is simply
        // IntPtr.Zero (NULL). No allocation, no boxing, no pinning, no generics, no Type.GetType. (This
        // convention — Nullable = boxed-or-NULL, NOT pointer-to-struct — is the one BCL-generic-value
        // gotcha the mod's other value-type args never exercised, since none took a Nullable.)
        private IntPtr questAssistantMakeItemMethod = IntPtr.Zero;

        private unsafe bool QuestAssistantTryMakeItemAura(int recipeId, int count, out string status)
        {
            status = "AuraMono unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.questAssistantMakeItemMethod == IntPtr.Zero)
            {
                IntPtr cls = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Craft.CraftProtocolManager");
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.Craft", "CraftProtocolManager");
                }

                if (cls == IntPtr.Zero)
                {
                    status = "CraftProtocolManager class unresolved";
                    return false;
                }

                this.questAssistantMakeItemMethod = this.FindAuraMonoMethodOnHierarchy(cls, "MakeItem", 3);
                if (this.questAssistantMakeItemMethod == IntPtr.Zero)
                {
                    status = "MakeItem method missing";
                    return false;
                }

                this.QuestAssistantLog("Craft: CraftProtocolManager.MakeItem resolved OK");
            }

            // int recipeId, int count: value-type args = pointer to the raw value.
            // int? themeId = null: Nullable<T> arg = boxed MonoObject* / NULL (see class comment) →
            // IntPtr.Zero.
            int localRecipeId = recipeId;
            int localCount = count;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&localRecipeId);
            args[1] = (IntPtr)(&localCount);
            args[2] = IntPtr.Zero;

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.questAssistantMakeItemMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "MakeItem invoke exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            this.QuestAssistantLog("Craft: MakeItem(recipeId=" + recipeId + ", count=" + count + ", themeId=null) sent");
            status = "ok";
            return true;
        }

        // Arms the TalkToNpc watcher — called only from QuestAssistantOnTalkToNpcClicked's success
        // branch (after OpenTaskDialogue actually succeeded). Re-arming while a watch is already
        // active intentionally OVERWRITES it rather than stacking or refusing: a second successful
        // OpenTaskDialogue call means the old watch's premise (one dialogue-open action, wait for its
        // effect) is already stale, so continuing to time the OLD watch would attribute any later
        // change to the wrong click. Matches the Collect monitor's own precedent (no re-arm guard).
        private void QuestAssistantArmTalkToNpcMonitor(QuestSnapshot focused)
        {
            if (this.questAssistantTalkToNpcMonitorActive)
            {
                this.QuestAssistantLog(
                    "TalkToNpc monitor: re-arming, discarding previous watch for taskId="
                    + this.questAssistantTalkToNpcMonitorTaskId + " (was still active)");
            }

            int before = 0, needed = 0;
            if (focused.Conditions != null && focused.Conditions.Count > 0)
            {
                before = focused.Conditions[0].Current;
                needed = focused.Conditions[0].Needed;
            }

            this.questAssistantTalkToNpcMonitorActive = true;
            this.questAssistantTalkToNpcMonitorTaskId = focused.TaskId;
            this.questAssistantTalkToNpcMonitorBeforeCurrent = before;
            this.questAssistantTalkToNpcMonitorBeforeNeeded = needed;
            float now = Time.unscaledTime;
            this.questAssistantTalkToNpcMonitorNextCheckAt = now + QuestAssistantTalkToNpcMonitorIntervalSeconds;
            this.questAssistantTalkToNpcMonitorDeadlineAt = now + QuestAssistantTalkToNpcMonitorTimeoutSeconds;

            this.QuestAssistantLog(
                "TalkToNpc monitor: armed taskId=" + focused.TaskId + " before=" + before + "/" + needed
                + " interval=" + QuestAssistantTalkToNpcMonitorIntervalSeconds + "s timeout="
                + QuestAssistantTalkToNpcMonitorTimeoutSeconds + "s");
        }

        // Checked from OnUpdate but a no-op unless questAssistantTalkToNpcMonitorActive — see field
        // comment above for why this is safe (only reachable after an explicit button click + a
        // successful OpenTaskDialogue call this session). Independent of questAssistantCollectMonitorActive
        // and of questAssistantWindowVisible — this deliberately keeps running even if the Quest Assistant
        // window is hidden mid-watch (OnUpdate gating has zero dependency on window visibility; the only
        // other reader of questAssistantWindowVisible is HeartopiaComplete.CameraInput.cs's unrelated
        // click-blocker check).
        private void QuestAssistantTalkToNpcMonitorTick()
        {
            if (!this.questAssistantTalkToNpcMonitorActive)
            {
                return;
            }

            float now = Time.unscaledTime;

            if (now >= this.questAssistantTalkToNpcMonitorDeadlineAt)
            {
                this.QuestAssistantConcludeTalkToNpcMonitor(false, this.questAssistantTalkToNpcMonitorBeforeCurrent, "timeout");
                return;
            }

            if (now < this.questAssistantTalkToNpcMonitorNextCheckAt)
            {
                return;
            }

            this.questAssistantTalkToNpcMonitorNextCheckAt = now + QuestAssistantTalkToNpcMonitorIntervalSeconds;

            try
            {
                this.QuestAssistantResolveSnapshot(false);
            }
            catch (Exception ex)
            {
                this.QuestAssistantLog("TalkToNpc monitor EXCEPTION, stopping monitor: " + ex);
                this.questAssistantTalkToNpcMonitorActive = false;
                return;
            }

            QuestSnapshot stillActive = null;
            for (int i = 0; i < this.questAssistantSnapshot.Count; i++)
            {
                if (this.questAssistantSnapshot[i].TaskId == this.questAssistantTalkToNpcMonitorTaskId)
                {
                    stillActive = this.questAssistantSnapshot[i];
                    break;
                }
            }

            // Left the active list entirely = submitted/completed/removed -- counts as a change, same as
            // the Collect monitor's "done" check.
            if (stillActive == null)
            {
                this.QuestAssistantConcludeTalkToNpcMonitor(true, this.questAssistantTalkToNpcMonitorBeforeCurrent, "task left active list (submitted/completed)");
                return;
            }

            if (stillActive.Conditions != null && stillActive.Conditions.Count > 0)
            {
                ConditionSnapshot c = stillActive.Conditions[0];
                // Deliberately != not >: a decrease would be unexpected but should still register as
                // "definitely not stagnant, something happened" — nothing about server behavior is
                // assumed yet, so the comparison shouldn't silently assume monotonic progress either.
                bool changed = c.Current != this.questAssistantTalkToNpcMonitorBeforeCurrent;
                bool satisfied = c.Needed > 0 && c.Current >= c.Needed;
                if (changed || satisfied)
                {
                    this.QuestAssistantConcludeTalkToNpcMonitor(true, c.Current, satisfied ? "condition satisfied" : "progress changed");
                }
            }
            // else: condition list empty this pass -- treat as "no change yet", keep polling until timeout.
        }

        // Single disarm/log/status point for both TalkToNpc watcher outcomes. Both status/log lines start
        // with the literal, greppable prefix "TalkToNpc RESULT: " so `grep "TalkToNpc RESULT"` on the log
        // surfaces exactly the possible conclusions and nothing else.
        private void QuestAssistantConcludeTalkToNpcMonitor(bool changed, int afterCurrent, string reason)
        {
            int taskId = this.questAssistantTalkToNpcMonitorTaskId;
            int before = this.questAssistantTalkToNpcMonitorBeforeCurrent;
            this.questAssistantTalkToNpcMonitorActive = false;

            // Close the server-side talk session opened by the start RPC (paired start/end).
            if (this.questAssistantTalkToNpcMonitorNpcNetId != 0U)
            {
                this.QuestAssistantTrySendTalkWithNpc(this.questAssistantTalkToNpcMonitorNpcNetId, false, out _);
                this.questAssistantTalkToNpcMonitorNpcNetId = 0U;
            }

            if (changed)
            {
                this.questAssistantLastStatus =
                    "TalkToNpc RESULT: progress CHANGED (before=" + before + " after=" + afterCurrent + ") "
                    + "— dialogue interaction DID count toward the quest. (" + reason + ")";
                this.QuestAssistantLog(
                    "TalkToNpc RESULT: progress CHANGED (before=" + before + " after=" + afterCurrent
                    + ") — dialogue interaction DID count toward the quest. taskId=" + taskId + " reason=" + reason);
            }
            else
            {
                this.questAssistantLastStatus =
                    "TalkToNpc RESULT: NO progress change after " + (int)QuestAssistantTalkToNpcMonitorTimeoutSeconds
                    + "s — either the dialogue did not get interacted with, or the server rejected/ignored it "
                    + "(possibly proximity-gated). Did you actually click through the in-game dialogue panel? "
                    + "If yes, this points to a server-side rejection.";
                this.QuestAssistantLog(
                    "TalkToNpc RESULT: NO progress change after " + QuestAssistantTalkToNpcMonitorTimeoutSeconds
                    + "s — either the dialogue did not get interacted with, or the server rejected/ignored it "
                    + "(possibly proximity-gated). taskId=" + taskId + " before=" + before);
            }
        }

        // Resets exactly the categories QuestAssistantTryEnableRadarForItem can ever set — the exact
        // mirror of that method's switch cases, kept in sync with it deliberately (not a call into
        // the "Clear All Loots" button's broader reset, which also zeroes bird/insect/fish/other-
        // players/meteor/bubble toggles that have nothing to do with Quest Assistant). See progress
        // doc §33 — called at the top of QuestAssistantStartCollect, before enabling the new quest's
        // items, so switching quests doesn't leave the previous quest's categories (and therefore its
        // items) still selected.
        private void QuestAssistantClearOwnedRadarLootToggles()
        {
            this.showTreeRadar = false;
            this.showRareTreeRadar = false;
            this.showStoneRadar = false;
            this.showOreRadar = false;
            this.showAppleTreeRadar = false;
            this.showOrangeTreeRadar = false;
            this.showBlueberryRadar = false;
            this.showRaspberryRadar = false;
            this.showOysterMushroomRadar = false;
            this.showShiitakeRadar = false;
            this.showButtonMushroomRadar = false;
            this.showPennyBunRadar = false;
            this.showTruffleRadar = false;
            this.showMushroomRadar = false;
            this.showGlasswortRadar = false;
            this.showSeaGrapeRadar = false;
            this.showWakameRadar = false;
        }

        // Ids/fields verified against docs/RADAR_GAME_MAP.md's known collectable atlas and the actual
        // showXRadar field declarations in HeartopiaComplete.cs. 40001/40002 (Branch/Timber) and
        // 40003/40004/40006 (Quality/Rare/Roaming Oak Timber) don't have per-item fields — they share
        // the coarser showTreeRadar/showRareTreeRadar toggles (multiple ids -> one category, still
        // correct, just not per-item granular). 40026 Flawless Fluorite, 40033 Bamboo, 40301 Coconut,
        // and 48006 Matsutake have no dedicated field found — left unmapped (best-effort).
        private bool QuestAssistantTryEnableRadarForItem(int itemStaticId)
        {
            switch (itemStaticId)
            {
                case 40001:
                case 40002:
                    this.showTreeRadar = true;
                    return true;
                case 40003:
                case 40004:
                case 40006:
                    this.showRareTreeRadar = true;
                    return true;
                case 40021:
                    this.showStoneRadar = true;
                    return true;
                case 40022:
                    this.showOreRadar = true;
                    return true;
                case 40101:
                    this.showAppleTreeRadar = true;
                    return true;
                case 40201:
                    this.showOrangeTreeRadar = true;
                    return true;
                case 40501:
                    this.showBlueberryRadar = true;
                    return true;
                case 40502:
                    this.showRaspberryRadar = true;
                    return true;
                case 48001:
                    this.showOysterMushroomRadar = true;
                    this.showMushroomRadar = this.AreAllMushroomRadarsEnabled();
                    return true;
                case 48002:
                    this.showShiitakeRadar = true;
                    this.showMushroomRadar = this.AreAllMushroomRadarsEnabled();
                    return true;
                case 48003:
                    this.showButtonMushroomRadar = true;
                    this.showMushroomRadar = this.AreAllMushroomRadarsEnabled();
                    return true;
                case 48004:
                    this.showPennyBunRadar = true;
                    this.showMushroomRadar = this.AreAllMushroomRadarsEnabled();
                    return true;
                case 48005:
                    this.showTruffleRadar = true;
                    this.showMushroomRadar = this.AreAllMushroomRadarsEnabled();
                    return true;
                case 40601:
                    this.showGlasswortRadar = true;
                    return true;
                case 40602:
                    this.showSeaGrapeRadar = true;
                    return true;
                case 40603:
                    this.showWakameRadar = true;
                    return true;
                default:
                    return false;
            }
        }

        // Short label for a multi-target Collect quest (e.g. "Collect 15 Common Mushrooms" carries 6
        // ids) — first name plus a "+N more" suffix so the button/status text stays a manageable
        // length regardless of how many ids the condition listed.
        private static string QuestAssistantSummarizeItemNames(List<int> itemStaticIds)
        {
            if (itemStaticIds == null || itemStaticIds.Count == 0)
            {
                return "item";
            }

            string first = QuestAssistantResolveItemName(itemStaticIds[0]);
            return itemStaticIds.Count == 1 ? first : (first + " (+" + (itemStaticIds.Count - 1) + " more)");
        }

        // Deliberately NOT an AuraMono TableData.GetEntity lookup — these are fixed, well-known ids
        // from the same atlas as the radar mapping above, so a hardcoded label is zero-risk versus
        // adding a new native call path just for a cosmetic button label. Unknown ids show their raw
        // static id instead of guessing a name.
        private static string QuestAssistantResolveItemName(int itemStaticId)
        {
            switch (itemStaticId)
            {
                case 40001: return "Branch";
                case 40002: return "Timber";
                case 40003: return "Quality Timber";
                case 40004: return "Rare Timber";
                case 40006: return "Roaming Oak Timber";
                case 40021: return "Stone";
                case 40022: return "Ore";
                case 40101: return "Apple";
                case 40201: return "Mandarin";
                case 40501: return "Blueberry";
                case 40502: return "Raspberry";
                case 48001: return "Oyster Mushroom";
                case 48002: return "Shiitake";
                case 48003: return "Button Mushroom";
                case 48004: return "Penny Bun";
                case 48005: return "Black Truffle";
                case 40601: return "Glasswort";
                case 40602: return "Sea Grape";
                case 40603: return "Wakame";
                default: return "item #" + itemStaticId;
            }
        }

        // ===== TalkToNpc button: complete a "go talk to NPC X" quest step =====
        //
        // Mechanism history (progress doc §13-§20). The 5th-test conclusion REVERSED the original
        // premise: a purely client-side DialoguePanel.OpenTaskDialogue is only UI — the
        // InteractWithNpc(30011) condition progress is credited SERVER-side when the server
        // processes TalkWithPlayerCommand (TalkProtocolManager.SendTalkWithNpc(npcNetId, start)),
        // which is what the game's real interact flow sends BEFORE opening any dialogue UI
        // (TalkWithTaskNpcCommand.cs: RPCAsyncTask(SendTalkWithNpc) -> wait NpcTalkStartEvent ->
        // PlayerStateDialogue.StartTalk -> NpcComponent.OpenTalkUI). TalkWithPlayerCommand is a
        // plain [NetworkCommand] with NO [VerifyEntity] (same self-report shape as the dragonboat
        // PlayerEnterAreaCommand that was confirmed server-trusted) — but it needs the NPC's live
        // netId, which only exists client-side while the NPC is streamed in. NpcClientService's
        // TryGetNpcNetId/TryGetNpcEntity iterate the client ECS filter — there is NO API to ask the
        // server for a far NPC's netId. So the flow below: resolve netId (service, then live
        // component scan); if the NPC isn't streamed, teleport to its map-spot position (the same
        // MapSpotsSystem source the NPC-teleport feature already uses — spots exist for non-streamed
        // NPCs), wait for it to stream in, THEN send the real talk RPC + open the dialogue UI.
        // Whether the server additionally validates proximity for the RPC is the remaining empirical
        // question — the watcher (§17) answers it per-click.
        private object questAssistantTalkToNpcCoroutine = null;

        // SubmitToNpc button (progress doc §24-§27): a CanSubmit quest completes via the wire command
        // SubmitGameTaskItem2NpcCommand{GameTaskId, NpcId(static), ItemNetPairs} — plain
        // [NetworkCommand], no [VerifyEntity], NpcId is the STATIC id (confirmed in the decompile,
        // ilspy-dumps/.../SubmitGameTaskItem2NpcCommand.cs). §25/§26 routed this through the full
        // TalkToNpc flow (teleport + SendTalkWithNpc + OpenTaskDialogue) on the wrong assumption that
        // the item had already been delivered — that flow DID complete the quest (§26 log confirmed
        // the submit call itself was what did it: "task left active list"), but it also left a
        // DialoguePanel open forever, since nothing ever drove it through its own tap-to-close state
        // machine (opening the panel and then submitting out from under it via a separate AuraMono
        // call never dispatches the TalkEndEvent the panel listens for to close itself — see
        // DialoguePanel.OnStart/_Close, DialogueNodeTask.TapHandler). §27: removed teleport/talk-RPC/
        // dialogue entirely — same direct TrySubmitDailyQuestCheapestItemsAura call the "Submit Ready
        // Items" daily-order button already uses successfully with zero teleport, which is exactly
        // consistent with the wire command carrying no netId/position at all. Synchronous, no
        // coroutine, no dialogue panel opened — nothing to hang.
        private void QuestAssistantOnSubmitToNpcClicked(QuestSnapshot focused)
        {
            if (focused.SubmitNpcId <= 0)
            {
                this.questAssistantLastStatus = "No submit NPC resolved for this quest.";
                return;
            }

            this.QuestAssistantLog(
                "SubmitToNpc: taskId=" + focused.TaskId + " submitNpc=" + focused.SubmitNpcId
                + " progressBefore=" + QuestAssistantSummarizeProgress(focused));

            // Some CanSubmit-at-an-NPC quests need no item hand-off at all — e.g. "Naughty's
            // Treasure": condition is "Tell Naughty about the found treasure"
            // (checkParamString="PlayerFeatureOpen"), a talk/flag completion, and
            // TableGameTask.submitTargetItem is empty. Detect that up front (same check
            // TrySubmitDailyQuestCheapestItemsAura's own build step makes internally) so we route to
            // the right wire call instead of failing with "no submit targets". See progress doc §29.
            bool hasSubmitTargets =
                this.TryGetDailyQuestGameTaskRowPtrAura(focused.TaskId, out IntPtr gameTaskRow, out string rowStatus)
                && this.TryGetDailyQuestSubmitTargetsAura(gameTaskRow, out _, out List<IntPtr> targets)
                && targets.Count > 0;

            bool ok;
            string status;
            if (hasSubmitTargets)
            {
                ok = this.TrySubmitDailyQuestCheapestItemsAura(focused.TaskId, focused.SubmitNpcId, 0, focused.SubmitNpcId, out status);
            }
            else
            {
                this.QuestAssistantLog("SubmitToNpc: no submitTargetItem for taskId=" + focused.TaskId + " (" + rowStatus + ") — sending empty-item submit (talk/flag-only completion)");
                ok = this.QuestAssistantTrySubmitNoItemsAura(focused.TaskId, focused.SubmitNpcId, out status);
            }

            if (ok)
            {
                this.questAssistantLastStatus =
                    "Submit sent to NPC #" + focused.SubmitNpcId + " for \"" + focused.Name + "\". Click Refresh to confirm it completed.";
                this.QuestAssistantLog("SubmitToNpc: OK taskId=" + focused.TaskId + " status=" + status);
            }
            else
            {
                this.questAssistantLastStatus = "Submit failed: " + status;
                this.QuestAssistantLog("SubmitToNpc: FAILED taskId=" + focused.TaskId + " status=" + status);
            }
        }

        // No-items CanSubmit completion (progress doc §29): mirrors vanilla's own
        // AutoSubmitNpcTaskItem, which — when TableGameTask.submitTargetItem is empty — skips item
        // matching/validation entirely and dispatches ClientSubmitTaskItem with an EMPTY
        // ItemNetPairs list. submitType/submitParam are irrelevant here: ClientSubmitTaskItem routes
        // to ClientSubmitNpcTaskItem(gameTaskId, TableGameTask.submitNpc, item) whenever submitNpc>0
        // (confirmed in the decompile) — which is always true for anything QuestAssistant classifies
        // as SubmitToNpc — so the type/param values are read from the CLIENT'S OWN table, not from
        // what we pass. Builds the empty list via TryCreateDailyQuestItemNetPairListAuraMonoNative
        // (DailyQuestSubmitFeature.cs — its "pairs==null" guard was relaxed to allow this; an empty
        // non-null list was previously rejected as "empty pairs").
        private bool QuestAssistantTrySubmitNoItemsAura(int taskId, int submitNpc, out string status)
        {
            if (!this.TryCreateDailyQuestItemNetPairListAuraMonoNative(new List<DailyQuestSubmitNetPair>(), out IntPtr listObj, out string listStatus))
            {
                status = "empty list build: " + listStatus;
                return false;
            }

            if (!this.TryInvokeDailyQuestClientSubmitTaskItemAura(taskId, 0, submitNpc, listObj, out string submitStatus))
            {
                status = listStatus + "; " + submitStatus;
                return false;
            }

            status = listStatus + "; " + submitStatus;
            return true;
        }

        private void QuestAssistantOnTalkToNpcClicked(QuestSnapshot focused, int npcStaticId)
        {
            if (npcStaticId <= 0)
            {
                this.questAssistantLastStatus = "No NPC id resolved for this quest condition.";
                return;
            }

            if (this.questAssistantTalkToNpcCoroutine != null)
            {
                this.questAssistantLastStatus = "Talk-to-NPC already in progress — wait for it to finish.";
                return;
            }

            this.questAssistantTalkToNpcCoroutine = ModCoroutines.Start(this.QuestAssistantTalkToNpcRoutine(focused, npcStaticId));
        }

        // Coroutine safety: only scalars (ids, netIds, Vector3) are held across yields — every
        // AuraMono call below resolves and frees its own pins inside the call, per the project rule
        // ([[auramono-raw-pointers-across-yields]]). Used by TalkToNpc only — SubmitToNpc no longer
        // routes through here, see QuestAssistantOnSubmitToNpcClicked (§27).
        private System.Collections.IEnumerator QuestAssistantTalkToNpcRoutine(QuestSnapshot focused, int npcStaticId)
        {
            this.QuestAssistantLog(
                "TalkToNpc: start taskId=" + focused.TaskId + " taskNetId=" + focused.TaskNetId
                + " npcStaticId=" + npcStaticId
                + " checkParamString=" + (focused.Conditions != null && focused.Conditions.Count > 0 ? focused.Conditions[0].CheckParamString : "?")
                + " conditionProgressBefore=" + QuestAssistantSummarizeProgress(focused));

            // Dialogue-line id for the UI panel is SEPARATE from the NPC id: the Accepted-state
            // panel picks lines strictly by "wipItems[i].id == staticIdOrResId" (DialoguePanel.cs:
            // 298-307; for "Passionate Girl" wipItems=[{type=0,id=0}], so the panel wants 0 while
            // the NPC itself is 302). The §19 version conflated the two and broke netId resolution
            // by scanning for staticId=0 — see progress doc §20.
            int dialogueStaticIdOrResId = npcStaticId;
            bool dialogueIsStaticId = true;
            if (this.QuestAssistantTryReadTaskWipItems(focused.TaskId, out List<KeyValuePair<int, int>> wipItems, out string wipStatus) && wipItems.Count > 0)
            {
                string wipDump = "";
                bool matched = false;
                for (int i = 0; i < wipItems.Count; i++)
                {
                    wipDump += (i > 0 ? ", " : "") + "type=" + wipItems[i].Key + " id=" + wipItems[i].Value;
                    if (!matched && wipItems[i].Value == npcStaticId)
                    {
                        dialogueIsStaticId = wipItems[i].Key == 0; // DialogueTargetType.EntityType
                        matched = true;
                    }
                }

                if (!matched)
                {
                    int pick = 0;
                    for (int i = 0; i < wipItems.Count; i++)
                    {
                        if (wipItems[i].Key == 0)
                        {
                            pick = i;
                            break;
                        }
                    }

                    dialogueStaticIdOrResId = wipItems[pick].Value;
                    dialogueIsStaticId = wipItems[pick].Key == 0;
                }

                this.QuestAssistantLog("TalkToNpc: wipItems=[" + wipDump + "] -> dialogue staticIdOrResId=" + dialogueStaticIdOrResId + " isStaticId=" + dialogueIsStaticId + " (npc stays " + npcStaticId + ")");
            }
            else
            {
                this.QuestAssistantLog("TalkToNpc: wipItems unavailable (" + wipStatus + ") — dialogue id = npc id " + npcStaticId);
            }

            // Step 1: live netId (service cache, then live component scan) — both keyed by the REAL
            // npc static id.
            if (!this.QuestAssistantTryGetNpcNetIdAuraMono(npcStaticId, out uint npcNetId, out string netIdStatus)
                && !this.QuestAssistantTryGetNpcNetIdViaComponentScan(npcStaticId, out npcNetId, out netIdStatus))
            {
                npcNetId = 0U;
            }

            // Step 2: NPC not streamed -> teleport to its map-spot position and wait for streaming.
            // Position source: TryGetLiveNpcPositionByIdMono (Teleport.cs — the SAME proven helper
            // the NPC-teleport page uses for its list): AuraMono static
            // MapSpotProtocolManager.TryGetMapSpotPosition(SpotEnum.Npc=2, npcId, out Vector3,
            // GameSceneId) — since the 2026-07-09 update spots are keyed per scene; the helper
            // resolves the current scene itself (StarTown fallback). Reads the server-synced
            // map-spot entity, works for non-streamed NPCs. See docs/NPC_ACCESS.md before adding
            // any new NPC position/netId path.
            bool teleported = false;
            if (npcNetId == 0U)
            {
                if (!this.TryGetLiveNpcPositionByIdMono(npcStaticId, out Vector3 npcPos))
                {
                    this.questAssistantLastStatus = "NPC #" + npcStaticId + " is not loaded and its map-spot position is unavailable — cannot reach it.";
                    this.QuestAssistantLog("TalkToNpc: ABORT — no netId and no map-spot position (TryGetLiveNpcPositionByIdMono failed)");
                    this.questAssistantTalkToNpcCoroutine = null;
                    yield break;
                }

                this.QuestAssistantLog("TalkToNpc: NPC not streamed — teleporting to its map spot " + npcPos.ToString("F1"));
                this.questAssistantLastStatus = "NPC #" + npcStaticId + " not loaded — teleporting to it...";
                this.TeleportToLocation(npcPos);
                teleported = true;

                // Streaming poll: netId appears once the entity spawns around the new position.
                for (int attempt = 0; attempt < 16 && npcNetId == 0U; attempt++)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (!this.QuestAssistantTryGetNpcNetIdAuraMono(npcStaticId, out npcNetId, out netIdStatus)
                        && !this.QuestAssistantTryGetNpcNetIdViaComponentScan(npcStaticId, out npcNetId, out netIdStatus))
                    {
                        npcNetId = 0U;
                    }
                }

                if (npcNetId == 0U)
                {
                    this.questAssistantLastStatus = "Teleported to NPC #" + npcStaticId + "'s spot but it never streamed in (" + netIdStatus + ").";
                    this.QuestAssistantLog("TalkToNpc: ABORT — teleported but NPC never streamed in (" + netIdStatus + ")");
                    this.questAssistantTalkToNpcCoroutine = null;
                    yield break;
                }
            }

            this.QuestAssistantLog("TalkToNpc: npcNetId=" + npcNetId + " (teleported=" + teleported + ") — sending TalkWithPlayerCommand(start)");

            // Step 3: the REAL interaction — server-credited talk RPC (what actually moves
            // InteractWithNpc progress), then the dialogue UI on top.
            if (!this.QuestAssistantTrySendTalkWithNpc(npcNetId, true, out string rpcStatus))
            {
                this.questAssistantLastStatus = "SendTalkWithNpc failed: " + rpcStatus;
                this.QuestAssistantLog("TalkToNpc: SendTalkWithNpc(start) FAILED: " + rpcStatus);
                this.questAssistantTalkToNpcCoroutine = null;
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.3f);

            // EnterDialogNode (Gossip) quests are credited by the server processing the
            // EnterDialogNode{NodeId} command — the game itself fires it from DialogueNodeNormal as the
            // player taps a dialogue node. Send it directly with the node id from the condition's
            // typeParam (§52). For these we SKIP OpenTaskDialogue entirely: the credit is the command,
            // and opening the panel out-of-band risks the known DialoguePanel-hang (NPC_ACCESS.md).
            int dialogNodeId = this.QuestAssistantActiveDialogNodeId(focused);
            if (dialogNodeId > 0)
            {
                if (this.QuestAssistantTrySendEnterDialogNode(dialogNodeId, out string nodeStatus))
                {
                    this.QuestAssistantLog("TalkToNpc: EnterDialogNode(NodeId=" + dialogNodeId + ") sent");
                }
                else
                {
                    this.QuestAssistantLog("TalkToNpc: EnterDialogNode(NodeId=" + dialogNodeId + ") FAILED: " + nodeStatus);
                }
            }
            else
            {
                string targetName = focused.Conditions != null && focused.Conditions.Count > 0 && !string.IsNullOrEmpty(focused.Conditions[0].Description)
                    ? focused.Conditions[0].Description
                    : ("NPC #" + npcStaticId);
                if (this.QuestAssistantTryOpenNpcDialogue(focused.TaskNetId, npcNetId, dialogueIsStaticId, dialogueStaticIdOrResId, targetName, out string uiStatus))
                {
                    this.QuestAssistantLog("TalkToNpc: OpenTaskDialogue OK dialogueId=" + dialogueStaticIdOrResId + " npcNetId=" + npcNetId + " taskNetId=" + focused.TaskNetId);
                }
                else
                {
                    // Non-fatal: the RPC (the part that credits progress) is already sent.
                    this.QuestAssistantLog("TalkToNpc: OpenTaskDialogue FAILED (" + uiStatus + ") — talk RPC already sent, watching progress anyway");
                }
            }

            this.questAssistantTalkToNpcMonitorNpcNetId = npcNetId;
            this.QuestAssistantArmTalkToNpcMonitor(focused);
            this.questAssistantLastStatus =
                (dialogNodeId > 0 ? "Sent talk + dialogue-node " + dialogNodeId : "Talk RPC sent") + " to NPC #" + npcStaticId
                + (teleported ? " (auto-teleported to it)" : "")
                + ". Auto-checking progress for " + (int)QuestAssistantTalkToNpcMonitorTimeoutSeconds + "s.";
            this.questAssistantTalkToNpcCoroutine = null;
        }

        // (NPC position helper deliberately NOT duplicated here: Teleport.cs already ships
        // TryGetLiveNpcPositionByIdMono — AuraMono MapSpotProtocolManager.TryGetMapSpotPosition —
        // discovered AFTER reimplementing it once (progress doc §22-§23). docs/NPC_ACCESS.md is the
        // map of every NPC access mechanism; check it before writing a new one.)

        // TalkProtocolManager.SendTalkWithNpc(uint npcNetId, bool startOrEnd, int talkParam=0) —
        // static, sends TalkWithPlayerCommand (plain [NetworkCommand], no [VerifyEntity]) — the
        // game's real "player interacts with NPC" wire message (TalkWithTaskNpcCommand.cs:45).
        private IntPtr questAssistantSendTalkWithNpcMethod = IntPtr.Zero;

        private unsafe bool QuestAssistantTrySendTalkWithNpc(uint npcNetId, bool start, out string status)
        {
            status = "AuraMono unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.questAssistantSendTalkWithNpcMethod == IntPtr.Zero)
            {
                IntPtr cls = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Npc.TalkProtocolManager");
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.Npc", "TalkProtocolManager");
                }

                if (cls == IntPtr.Zero)
                {
                    status = "TalkProtocolManager class unresolved";
                    return false;
                }

                this.questAssistantSendTalkWithNpcMethod = this.FindAuraMonoMethodOnHierarchy(cls, "SendTalkWithNpc", 3);
                if (this.questAssistantSendTalkWithNpcMethod == IntPtr.Zero)
                {
                    status = "SendTalkWithNpc method missing";
                    return false;
                }

                this.QuestAssistantLog("TalkToNpc: TalkProtocolManager.SendTalkWithNpc resolved OK");
            }

            uint localNetId = npcNetId;
            bool localStart = start;
            int localTalkParam = 0;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&localNetId);
            args[1] = (IntPtr)(&localStart);
            args[2] = (IntPtr)(&localTalkParam);

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.questAssistantSendTalkWithNpcMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "SendTalkWithNpc exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            this.QuestAssistantLog("TalkToNpc: SendTalkWithNpc(netId=" + npcNetId + ", start=" + start + ") sent");
            status = "ok";
            return true;
        }

        // ===== EnterDialogNode — the real credit for Gossip/"reach dialogue node" quests (§52) =====
        //
        // An EnterDialogNode condition (checkParam=GameEventEnum.EnterDialogNode=30501, typeParam =
        // dialogue NODE id) is credited by the server processing the EnterDialogNode{NodeId} wire
        // command — the game fires it from DialogueNodeNormal.SendEnterNodeCommand
        // (WebRequestUtility.SendCommand(new EnterDialogNode{NodeId=nodeData.Id})) as the player taps
        // through the dialogue tree. The talk RPC and the client panel do NOT move this progress.
        //
        // WebRequestUtility + EnterDialogNode are embedded-Mono only (like the fishing buoy command),
        // and SendCommand<T> is a generic method — so this reuses the shipped AuraMono generic-inflation
        // path VERBATIM: TryInstantCatchInflateAuraSendCommand (mono_class_inflate_generic_method +
        // mono_compile_method — the SAFE inflation, not the runtime-inflate crash trap) then invoke with
        // (command, needAuthed=1, channel=Reliable), exactly as TrySendBuoyUpdateReliable does.
        private IntPtr questAssistantWebRequestClass = IntPtr.Zero;
        private IntPtr questAssistantEnterDialogNodeClass = IntPtr.Zero;
        private IntPtr questAssistantSendCommandOpenMethod = IntPtr.Zero;
        private IntPtr questAssistantEnterDialogNodeInflatedSend = IntPtr.Zero;
        private IntPtr questAssistantEnterDialogNodeFieldNodeId = IntPtr.Zero;
        private const int QuestAssistantChannelReliable = 1; // ChannelType.Reliable

        // The dialogue-node id an active EnterDialogNode step requires (its typeParam), or 0 when the
        // active TalkToNpc step is a plain InteractWithNpc (talk RPC alone credits it — no node command).
        private int QuestAssistantActiveDialogNodeId(QuestSnapshot focused)
        {
            if (focused.Conditions == null)
            {
                return 0;
            }

            for (int i = 0; i < focused.Conditions.Count; i++)
            {
                ConditionSnapshot c = focused.Conditions[i];
                if (c.Needed > 0 && c.Current >= c.Needed)
                {
                    continue; // skip completed step
                }

                if (!string.Equals(c.CheckParamString, "EnterDialogNode", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(c.TypeParam) && int.TryParse(c.TypeParam.Trim(), out int id) && id > 0)
                {
                    return id;
                }

                return 0;
            }

            return 0;
        }

        private unsafe bool QuestAssistantTrySendEnterDialogNode(int nodeId, out string status)
        {
            status = "AuraMono unavailable";
            if (nodeId <= 0)
            {
                status = "no node id";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectNew == null
                || auraMonoFieldSetValue == null || auraMonoObjectUnbox == null)
            {
                return false;
            }

            if (this.questAssistantWebRequestClass == IntPtr.Zero)
            {
                this.questAssistantWebRequestClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.WebRequestUtility");
            }

            if (this.questAssistantEnterDialogNodeClass == IntPtr.Zero)
            {
                this.questAssistantEnterDialogNodeClass = this.FindAuraMonoClassByFullName("XDT.Scene.Shared.Modules.Dialog.EnterDialogNode");
                if (this.questAssistantEnterDialogNodeClass == IntPtr.Zero)
                {
                    this.questAssistantEnterDialogNodeClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.Dialog", "EnterDialogNode");
                }
            }

            if (this.questAssistantWebRequestClass == IntPtr.Zero || this.questAssistantEnterDialogNodeClass == IntPtr.Zero)
            {
                status = "class missing web=" + (this.questAssistantWebRequestClass != IntPtr.Zero) + " cmd=" + (this.questAssistantEnterDialogNodeClass != IntPtr.Zero);
                return false;
            }

            if (this.questAssistantSendCommandOpenMethod == IntPtr.Zero)
            {
                this.questAssistantSendCommandOpenMethod = this.FindAuraMonoMethodOnHierarchy(this.questAssistantWebRequestClass, "SendCommand", 3);
            }

            if (this.questAssistantSendCommandOpenMethod == IntPtr.Zero)
            {
                status = "SendCommand(3) missing on WebRequestUtility";
                return false;
            }

            if (this.questAssistantEnterDialogNodeInflatedSend == IntPtr.Zero
                && !this.TryInstantCatchInflateAuraSendCommand(this.questAssistantSendCommandOpenMethod, this.questAssistantEnterDialogNodeClass, out this.questAssistantEnterDialogNodeInflatedSend))
            {
                status = "SendCommand<EnterDialogNode> inflate failed";
                return false;
            }

            if (this.questAssistantEnterDialogNodeFieldNodeId == IntPtr.Zero)
            {
                this.questAssistantEnterDialogNodeFieldNodeId = this.FindAuraMonoFieldOnHierarchy(this.questAssistantEnterDialogNodeClass, "NodeId");
            }

            if (this.questAssistantEnterDialogNodeFieldNodeId == IntPtr.Zero)
            {
                status = "NodeId field missing";
                return false;
            }

            IntPtr cmdObj = auraMonoObjectNew(this.auraMonoRootDomain, this.questAssistantEnterDialogNodeClass);
            if (cmdObj == IntPtr.Zero)
            {
                status = "cmd alloc failed";
                return false;
            }

            int nodeIdValue = nodeId;
            auraMonoFieldSetValue(cmdObj, this.questAssistantEnterDialogNodeFieldNodeId, (IntPtr)(&nodeIdValue));

            IntPtr cmdPtr = auraMonoObjectUnbox(cmdObj);
            if (cmdPtr == IntPtr.Zero)
            {
                status = "cmd unbox failed";
                return false;
            }

            int needAuthed = 1;
            int channel = QuestAssistantChannelReliable;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = cmdPtr;
            args[1] = (IntPtr)(&needAuthed);
            args[2] = (IntPtr)(&channel);

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.questAssistantEnterDialogNodeInflatedSend, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "SendCommand exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "ok";
            return true;
        }

        // ===== EnterArea — "Head to <area>" quests via the self-reported area-enter command (§53) =====
        //
        // A PlayerEnterArea condition (checkParam=GameEventEnum.PlayerEnterArea=20006, typeParam = area
        // trigger id) is credited server-side by the PlayerEnterAreaCommand{PlayerNetId, AreaTriggerId}
        // wire command — exactly what MetaAreaClientService.SendPlayerEnterArea sends when the player's
        // position enters an area. It carries NO position (self-reported) and was confirmed
        // server-trusted (the dragon-boat checkpoints advanced with zero movement, see
        // [[dragonboat-qte-vehicle-system]]), so sending it directly with the quest's area id credits
        // the step without moving. Same AuraMono generic-SendCommand path as EnterDialogNode. (The
        // game ALSO calls PlayerProtocolManager.PlayerEnterArea(id), but that only dispatches a LOCAL
        // client EventCenter event — not the server credit — so it's not needed here.)
        private IntPtr questAssistantPlayerEnterAreaClass = IntPtr.Zero;
        private IntPtr questAssistantPlayerEnterAreaInflatedSend = IntPtr.Zero;
        private IntPtr questAssistantPlayerEnterAreaFieldNetId = IntPtr.Zero;
        private IntPtr questAssistantPlayerEnterAreaFieldAreaId = IntPtr.Zero;

        private void QuestAssistantOnEnterAreaClicked(QuestSnapshot focused, int areaTriggerId)
        {
            if (areaTriggerId <= 0)
            {
                this.questAssistantLastStatus = "No area id resolved for this quest condition.";
                return;
            }

            if (!this.TryResolveSelfPlayerNetIdMono(out uint selfNetId) || selfNetId == 0U)
            {
                this.questAssistantLastStatus = "Could not resolve your player netId — cannot report area entry.";
                this.QuestAssistantLog("EnterArea: self netId unavailable for area " + areaTriggerId);
                return;
            }

            this.QuestAssistantLog("EnterArea: taskId=" + focused.TaskId + " area=" + areaTriggerId + " selfNetId=" + selfNetId + " progressBefore=" + QuestAssistantSummarizeProgress(focused));
            if (this.QuestAssistantTrySendPlayerEnterArea(selfNetId, areaTriggerId, out string status))
            {
                this.QuestAssistantLog("EnterArea: PlayerEnterAreaCommand(netId=" + selfNetId + ", area=" + areaTriggerId + ") sent");
                this.questAssistantLastStatus = "Reported entering area #" + areaTriggerId + " — watch the quest progress update here (panel auto-refreshes).";
            }
            else
            {
                this.QuestAssistantLog("EnterArea: PlayerEnterAreaCommand FAILED area=" + areaTriggerId + ": " + status);
                this.questAssistantLastStatus = "Failed to report area entry: " + status;
            }
        }

        private unsafe bool QuestAssistantTrySendPlayerEnterArea(uint playerNetId, int areaTriggerId, out string status)
        {
            status = "AuraMono unavailable";
            if (areaTriggerId <= 0)
            {
                status = "no area id";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectNew == null
                || auraMonoFieldSetValue == null || auraMonoObjectUnbox == null)
            {
                return false;
            }

            if (this.questAssistantWebRequestClass == IntPtr.Zero)
            {
                this.questAssistantWebRequestClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.WebRequestUtility");
            }

            if (this.questAssistantPlayerEnterAreaClass == IntPtr.Zero)
            {
                this.questAssistantPlayerEnterAreaClass = this.FindAuraMonoClassByFullName("XDT.Scene.Shared.Modules.Meta.Area.PlayerEnterAreaCommand");
                if (this.questAssistantPlayerEnterAreaClass == IntPtr.Zero)
                {
                    this.questAssistantPlayerEnterAreaClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.Meta.Area", "PlayerEnterAreaCommand");
                }
            }

            if (this.questAssistantWebRequestClass == IntPtr.Zero || this.questAssistantPlayerEnterAreaClass == IntPtr.Zero)
            {
                status = "class missing web=" + (this.questAssistantWebRequestClass != IntPtr.Zero) + " cmd=" + (this.questAssistantPlayerEnterAreaClass != IntPtr.Zero);
                return false;
            }

            if (this.questAssistantSendCommandOpenMethod == IntPtr.Zero)
            {
                this.questAssistantSendCommandOpenMethod = this.FindAuraMonoMethodOnHierarchy(this.questAssistantWebRequestClass, "SendCommand", 3);
            }

            if (this.questAssistantSendCommandOpenMethod == IntPtr.Zero)
            {
                status = "SendCommand(3) missing on WebRequestUtility";
                return false;
            }

            if (this.questAssistantPlayerEnterAreaInflatedSend == IntPtr.Zero
                && !this.TryInstantCatchInflateAuraSendCommand(this.questAssistantSendCommandOpenMethod, this.questAssistantPlayerEnterAreaClass, out this.questAssistantPlayerEnterAreaInflatedSend))
            {
                status = "SendCommand<PlayerEnterAreaCommand> inflate failed";
                return false;
            }

            if (this.questAssistantPlayerEnterAreaFieldNetId == IntPtr.Zero)
            {
                this.questAssistantPlayerEnterAreaFieldNetId = this.FindAuraMonoFieldOnHierarchy(this.questAssistantPlayerEnterAreaClass, "PlayerNetId");
                this.questAssistantPlayerEnterAreaFieldAreaId = this.FindAuraMonoFieldOnHierarchy(this.questAssistantPlayerEnterAreaClass, "AreaTriggerId");
            }

            if (this.questAssistantPlayerEnterAreaFieldNetId == IntPtr.Zero || this.questAssistantPlayerEnterAreaFieldAreaId == IntPtr.Zero)
            {
                status = "PlayerEnterAreaCommand fields missing";
                return false;
            }

            IntPtr cmdObj = auraMonoObjectNew(this.auraMonoRootDomain, this.questAssistantPlayerEnterAreaClass);
            if (cmdObj == IntPtr.Zero)
            {
                status = "cmd alloc failed";
                return false;
            }

            uint netIdValue = playerNetId;
            int areaIdValue = areaTriggerId;
            auraMonoFieldSetValue(cmdObj, this.questAssistantPlayerEnterAreaFieldNetId, (IntPtr)(&netIdValue));
            auraMonoFieldSetValue(cmdObj, this.questAssistantPlayerEnterAreaFieldAreaId, (IntPtr)(&areaIdValue));

            IntPtr cmdPtr = auraMonoObjectUnbox(cmdObj);
            if (cmdPtr == IntPtr.Zero)
            {
                status = "cmd unbox failed";
                return false;
            }

            int needAuthed = 1;
            int channel = QuestAssistantChannelReliable;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = cmdPtr;
            args[1] = (IntPtr)(&needAuthed);
            args[2] = (IntPtr)(&channel);

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.questAssistantPlayerEnterAreaInflatedSend, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "SendCommand exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "ok";
            return true;
        }

        // ===== CatchFish (location-bound) — teleport to the quest's fishing area + auto-fish (§55) =====
        //
        // "Catch N fish at <place>" quests (CaughtFish condition + Area(14) trackMark, e.g. "Seabed
        // Sediment Rising" area 5277): one click teleports INTO the area and enables the existing Auto
        // Fish Shadow Net feature (AutoFishingFarm.SetEnabled — it equips the rod and runs the whole
        // cast/reel loop itself). The fish monitor below (same shape as the Collect monitor) polls the
        // quest every 5s and turns auto-fishing off when the condition target is reached or the quest
        // leaves the active list, then tries the shared auto-submit.
        //
        // Area position source: EcsService.TryGet<IMetaAreaService> →
        // GetRandomPositionInArea(areaTriggerId, isTriggerId: true) — the service maps triggerId →
        // LevelObjectId internally and returns one of its 200 precomputed random points inside the
        // polygon (MetaAreaClientService, EcsSystem). Deliberately NOT TriggerId2ObjectId.TryGetValue +
        // GetAreaCenterPos: that needs an `out int` through mono_runtime_invoke — a value-type out slot
        // is stack corruption ([[auramono-invoke-out-params]]). Vector3.zero return = area unknown in
        // the current scene (config not loaded / different world).
        private const float QuestAssistantFishMonitorIntervalSeconds = 5f;
        private bool questAssistantFishMonitorActive = false;
        private int questAssistantFishMonitorTaskId = 0;
        private float questAssistantFishMonitorNextCheckAt = 0f;
        private IntPtr questAssistantMetaAreaServiceClass = IntPtr.Zero;

        private void QuestAssistantOnCatchFishClicked(QuestSnapshot focused)
        {
            if (this.questAssistantFishMonitorActive)
            {
                this.QuestAssistantStopFishMonitor("stopped by user");
                return;
            }

            int areaId = focused.ObjectiveAreaId;
            this.QuestAssistantLog("CatchFish: taskId=" + focused.TaskId + " area=" + areaId + " progressBefore=" + QuestAssistantSummarizeProgress(focused));
            if (!this.QuestAssistantTryGetAreaPositionMono(areaId, out Vector3 areaPos, out string posStatus))
            {
                this.questAssistantLastStatus = "Fishing area #" + areaId + " position unavailable: " + posStatus;
                this.QuestAssistantLog("CatchFish: area position FAILED: " + posStatus);
                return;
            }

            this.QuestAssistantLog("CatchFish: teleporting to area pos " + areaPos.ToString("F1"));
            this.TeleportToLocation(areaPos);
            AutoFishingFarm.SetEnabled(true, this);

            this.questAssistantFishMonitorActive = true;
            this.questAssistantFishMonitorTaskId = focused.TaskId;
            this.questAssistantFishMonitorNextCheckAt = Time.unscaledTime + QuestAssistantFishMonitorIntervalSeconds;
            this.questAssistantLastStatus =
                "Teleported to fishing area #" + areaId + " — Auto Fish Shadow Net ON, auto-stops when the "
                + "quest count is reached (or click the button again to stop).";
        }

        private unsafe bool QuestAssistantTryGetAreaPositionMono(int areaTriggerId, out Vector3 position, out string status)
        {
            position = Vector3.zero;
            status = "AuraMono unavailable";
            if (areaTriggerId <= 0)
            {
                status = "no area id";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null || auraMonoObjectUnbox == null)
            {
                return false;
            }

            if (this.questAssistantMetaAreaServiceClass == IntPtr.Zero)
            {
                this.questAssistantMetaAreaServiceClass = this.FindAuraMonoClassByFullName("XDT.Scene.Shared.Modules.Meta.IMetaAreaService");
                if (this.questAssistantMetaAreaServiceClass == IntPtr.Zero)
                {
                    this.questAssistantMetaAreaServiceClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.Meta", "IMetaAreaService");
                }

                if (this.questAssistantMetaAreaServiceClass == IntPtr.Zero)
                {
                    this.questAssistantMetaAreaServiceClass = this.FindAuraMonoClassAcrossLoadedAssemblies("ClientSystem.Area", "MetaAreaClientService");
                }

                this.QuestAssistantLog("  areaPos service class=" + (this.questAssistantMetaAreaServiceClass != IntPtr.Zero ? "resolved" : "NULL"));
            }

            if (this.questAssistantMetaAreaServiceClass == IntPtr.Zero)
            {
                status = "IMetaAreaService/MetaAreaClientService class unresolved";
                return false;
            }

            if (!this.TryDailyClaimsAuraMonoEcsTryGet(this.questAssistantMetaAreaServiceClass, true, out IntPtr areaServiceObj, out string ecsStatus)
                || areaServiceObj == IntPtr.Zero)
            {
                status = "EcsService.TryGet<IMetaAreaService> failed: " + ecsStatus;
                return false;
            }

            IntPtr runtimeClass = auraMonoObjectGetClass(areaServiceObj);
            IntPtr method = runtimeClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(runtimeClass, "GetRandomPositionInArea", 2)
                : IntPtr.Zero;
            if (method == IntPtr.Zero)
            {
                status = "GetRandomPositionInArea(2) missing on resolved service";
                return false;
            }

            int areaIdValue = areaTriggerId;
            bool isTriggerId = true;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&areaIdValue);
            args[1] = (IntPtr)(&isTriggerId);

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, areaServiceObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                status = "GetRandomPositionInArea invoke failed" + (exc != IntPtr.Zero ? " exc=0x" + exc.ToInt64().ToString("X") : string.Empty);
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                status = "result unbox failed";
                return false;
            }

            position = *(Vector3*)raw;
            if (position == Vector3.zero)
            {
                status = "area " + areaTriggerId + " unknown in current scene (zero position)";
                return false;
            }

            status = "ok " + position.ToString("F1");
            return true;
        }

        // Same shape as the Collect monitor: armed only by the explicit button click, no-op otherwise,
        // 5s poll. Also disarms silently if the user turns the Auto Fish toggle off by hand.
        private void QuestAssistantFishMonitorTick()
        {
            if (!this.questAssistantFishMonitorActive)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.questAssistantFishMonitorNextCheckAt)
            {
                return;
            }

            this.questAssistantFishMonitorNextCheckAt = now + QuestAssistantFishMonitorIntervalSeconds;

            if (!AutoFishingFarm.IsEnabled)
            {
                this.questAssistantFishMonitorActive = false;
                this.QuestAssistantLog("Fish monitor: Auto Fish Shadow Net was disabled manually — monitor disarmed");
                return;
            }

            try
            {
                this.QuestAssistantResolveSnapshot(false);
            }
            catch (Exception ex)
            {
                this.QuestAssistantLog("Fish monitor EXCEPTION, stopping monitor: " + ex);
                this.questAssistantFishMonitorActive = false;
                return;
            }

            QuestSnapshot stillActive = null;
            for (int i = 0; i < this.questAssistantSnapshot.Count; i++)
            {
                if (this.questAssistantSnapshot[i].TaskId == this.questAssistantFishMonitorTaskId)
                {
                    stillActive = this.questAssistantSnapshot[i];
                    break;
                }
            }

            // No longer in the active list (submitted/completed/removed) counts as done too.
            bool done = stillActive == null;
            if (!done && stillActive.Conditions.Count > 0)
            {
                ConditionSnapshot c = stillActive.Conditions[0];
                done = c.Needed > 0 && c.Current >= c.Needed;
            }

            if (done)
            {
                this.QuestAssistantStopFishMonitor("target reached");
                this.QuestAssistantTryAutoSubmitIfReady(this.questAssistantFishMonitorTaskId, "Fish monitor");
            }
        }

        private void QuestAssistantStopFishMonitor(string reason)
        {
            int taskId = this.questAssistantFishMonitorTaskId;
            this.questAssistantFishMonitorActive = false;
            try
            {
                AutoFishingFarm.SetEnabled(false, this);
            }
            catch (Exception ex)
            {
                this.QuestAssistantLog("Fish monitor: SetEnabled(false) threw: " + ex.Message);
            }

            this.questAssistantLastStatus = "Fishing done (" + reason + ") — Auto Fish Shadow Net stopped.";
            this.QuestAssistantLog("Fish monitor stop: taskId=" + taskId + " reason=" + reason);
        }

        // AuraMono NPC-netId resolver — replaces an earlier managed-reflection diagnostic clone that
        // confirmed EcsService/INpcClientService are Mono-only types FindLoadedType cannot see on
        // this build (both resolved NULL at the first step, 2026-07-02 test with npcStaticId=302).
        // Per this project's rule, no managed fallback is kept for a confirmed-dead path — see
        // progress doc §14/§15. This reuses DailyClaimsFeature.cs's EcsService.TryGet<T> AuraMono
        // generic-method-inflation machinery (TryDailyClaimsAuraMonoEcsTryGet) VERBATIM — same
        // partial class, zero duplication — only the target service class + the final
        // TryGetNpcNetId instance-method invoke are new.
        private IntPtr questAssistantNpcClientServiceClass = IntPtr.Zero;

        private unsafe bool QuestAssistantTryGetNpcNetIdAuraMono(int npcId, out uint netId, out string status)
        {
            netId = 0U;
            status = "AuraMono unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.questAssistantNpcClientServiceClass == IntPtr.Zero)
            {
                this.questAssistantNpcClientServiceClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Npc.INpcClientService");
                if (this.questAssistantNpcClientServiceClass == IntPtr.Zero)
                {
                    this.questAssistantNpcClientServiceClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.Npc", "INpcClientService");
                }

                if (this.questAssistantNpcClientServiceClass == IntPtr.Zero)
                {
                    this.questAssistantNpcClientServiceClass = this.FindAuraMonoClassByFullName("ClientSystem.Npc.NpcClientService");
                }

                if (this.questAssistantNpcClientServiceClass == IntPtr.Zero)
                {
                    this.questAssistantNpcClientServiceClass = this.FindAuraMonoClassAcrossLoadedAssemblies("ClientSystem.Npc", "NpcClientService");
                }

                this.QuestAssistantLog("  npcNetIdAura serviceClass=" + (this.questAssistantNpcClientServiceClass != IntPtr.Zero ? "resolved" : "NULL"));
            }

            if (this.questAssistantNpcClientServiceClass == IntPtr.Zero)
            {
                status = "INpcClientService/NpcClientService AuraMono class unresolved";
                return false;
            }

            if (!this.TryDailyClaimsAuraMonoEcsTryGet(this.questAssistantNpcClientServiceClass, true, out IntPtr npcServiceObj, out string ecsStatus)
                || npcServiceObj == IntPtr.Zero)
            {
                status = "EcsService.TryGet<NpcClientService> failed: " + ecsStatus;
                this.QuestAssistantLog("  npcNetIdAura EcsService.TryGet failed: " + ecsStatus);
                return false;
            }

            this.QuestAssistantLog("  npcNetIdAura EcsService.TryGet OK, resolving TryGetNpcNetId method");

            IntPtr serviceRuntimeClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(npcServiceObj) : IntPtr.Zero;
            IntPtr tryGetNetIdMethod = serviceRuntimeClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(serviceRuntimeClass, "TryGetNpcNetId", 2)
                : IntPtr.Zero;
            if (tryGetNetIdMethod == IntPtr.Zero)
            {
                status = "TryGetNpcNetId method missing on resolved service";
                this.QuestAssistantLog("  npcNetIdAura TryGetNpcNetId method NOT FOUND on runtime class");
                return false;
            }

            int localNpcId = npcId;
            uint localNetId = 0U;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&localNpcId);
            args[1] = (IntPtr)(&localNetId);

            IntPtr exc = IntPtr.Zero;
            IntPtr result = auraMonoRuntimeInvoke(tryGetNetIdMethod, npcServiceObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "TryGetNpcNetId invoke exception";
                this.QuestAssistantLog("  npcNetIdAura TryGetNpcNetId invoke EXCEPTION");
                return false;
            }

            bool ok = result != IntPtr.Zero && this.TryUnboxMonoBoolean(result, out bool boolResult) && boolResult;
            this.QuestAssistantLog("  npcNetIdAura TryGetNpcNetId(" + npcId + ") ok=" + ok + " netId=" + localNetId);
            if (!ok || localNetId == 0U)
            {
                status = "TryGetNpcNetId returned false/0 for npcId=" + npcId;
                return false;
            }

            netId = localNetId;
            status = "ok";
            return true;
        }

        // Second AuraMono strategy (2026-07-02, see progress doc §16): scan live NpcComponents
        // directly via Entities.GetComponents<T> (TryAuraMonoGetComponentObjects,
        // HomelandFarmFeature.cs — the same "preferred over entity-graph walk" reusable helper
        // AGENTS.md §8 documents, already cross-adopted by farm/cook/bird/insect) and match by
        // staticId, reading netId off the matched component's `entity` field via the existing
        // TryHomelandFarmTryReadAuraMonoComponentNetId helper. Both helpers reused verbatim — only
        // the NpcComponent class resolution + staticId match loop are new.
        private IntPtr questAssistantNpcComponentClass = IntPtr.Zero;

        private bool QuestAssistantTryGetNpcNetIdViaComponentScan(int npcStaticId, out uint netId, out string status)
        {
            netId = 0U;
            status = "unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (this.questAssistantNpcComponentClass == IntPtr.Zero)
            {
                this.questAssistantNpcComponentClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.Npc.NpcComponent");
                if (this.questAssistantNpcComponentClass == IntPtr.Zero)
                {
                    this.questAssistantNpcComponentClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.Gameplay.Component.Npc", "NpcComponent");
                }

                this.QuestAssistantLog("  npcNetIdScan NpcComponent class=" + (this.questAssistantNpcComponentClass != IntPtr.Zero ? "resolved" : "NULL"));
            }

            if (this.questAssistantNpcComponentClass == IntPtr.Zero)
            {
                status = "NpcComponent AuraMono class unresolved";
                return false;
            }

            List<uint> pins = new List<uint>();
            if (!this.TryAuraMonoGetComponentObjects(this.questAssistantNpcComponentClass, out List<IntPtr> components, pins) || components == null)
            {
                status = "Entities.GetComponents<NpcComponent> returned nothing (NPC not currently loaded anywhere in this session?)";
                this.QuestAssistantLog("  npcNetIdScan GetComponents<NpcComponent> found 0 / failed");
                FreeAuraMonoPins(pins);
                return false;
            }

            try
            {
                this.QuestAssistantLog("  npcNetIdScan scanning " + components.Count + " live NpcComponent(s) for staticId=" + npcStaticId);
                List<int> seenStaticIds = new List<int>();
                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr npcComponentObj = components[i];
                    if (npcComponentObj == IntPtr.Zero
                        || !this.TryGetMonoObjectMember(npcComponentObj, "_componentData", out IntPtr boxedComponentData)
                        || boxedComponentData == IntPtr.Zero)
                    {
                        seenStaticIds.Add(-1); // _componentData unreadable/null on this instance
                        continue;
                    }

                    if (!this.TryGetMonoIntMember(boxedComponentData, "staticId", out int liveStaticId))
                    {
                        seenStaticIds.Add(-2); // staticId field unreadable
                        continue;
                    }

                    seenStaticIds.Add(liveStaticId);
                    if (liveStaticId != npcStaticId)
                    {
                        continue;
                    }

                    if (this.TryHomelandFarmTryReadAuraMonoComponentNetId(npcComponentObj, out netId) && netId != 0U)
                    {
                        status = "ok";
                        this.QuestAssistantLog("  npcNetIdScan MATCH staticId=" + npcStaticId + " -> netId=" + netId);
                        return true;
                    }

                    this.QuestAssistantLog("  npcNetIdScan matched staticId=" + npcStaticId + " but netId read failed");
                }

                // -1 = _componentData unreadable, -2 = staticId unreadable (see loop above).
                this.QuestAssistantLog("  npcNetIdScan MISS: staticIds seen=[" + string.Join(",", seenStaticIds) + "], wanted=" + npcStaticId);
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            status = "no loaded NpcComponent with staticId=" + npcStaticId + " (" + components.Count + " scanned)";
            return false;
        }

        // Reads TableGameTask.wipItems (TableWipItem { int type; int id; }) for a task — the array
        // DialoguePanel's Accepted branch matches staticIdOrResId against to pick dialogue lines
        // (tableWipItem.type == (int)options.targetType && tableWipItem.id == options.staticIdOrResId,
        // DialoguePanel.cs:298-307). If our id isn't in this array the panel silently gets no
        // dialogue lines (SetTaskDialogues never called -> null _dialogues -> panel dies during
        // init with no visible panel and no exception at our invoke site) — exactly the 4th-test
        // failure, see progress doc §19. Reuses TryGetDailyQuestGameTaskRowPtrAura (row resolve) +
        // the same enumerate/pin pattern as QuestAssistantReadTrackMarks.
        private bool QuestAssistantTryReadTaskWipItems(int taskId, out List<KeyValuePair<int, int>> wipItems, out string status)
        {
            wipItems = new List<KeyValuePair<int, int>>();
            if (!this.TryGetDailyQuestGameTaskRowPtrAura(taskId, out IntPtr gameTaskRow, out status) || gameTaskRow == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(gameTaskRow, "wipItems", out IntPtr arrayObj) || arrayObj == IntPtr.Zero)
            {
                status = "wipItems array null/unreadable";
                return false;
            }

            List<IntPtr> items = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(arrayObj, items, pins))
                {
                    status = "wipItems enumerate failed";
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] == IntPtr.Zero)
                    {
                        continue;
                    }

                    int type = this.TryGetMonoIntMember(items[i], "type", out int t) ? t : -1;
                    int id = this.TryGetMonoIntMember(items[i], "id", out int v) ? v : -1;
                    wipItems.Add(new KeyValuePair<int, int>(type, id));
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            status = "ok";
            return true;
        }

        // Class/method pointers only — safe to cache raw (image lifetime). DialoguePanel is under
        // XDTGame.UI.* — per AGENTS.md §12 ("Harmony on FindLoadedType(XDTGame.UI.*) — UI types often
        // AuraMono-only"), resolved via AuraMono like the existing craft-panel opener
        // (AnimalCareFeature.TryOpenCraftPanelViaAuraMonoInteract), not managed FindLoadedType.
        private IntPtr questAssistantDialoguePanelClass = IntPtr.Zero;
        private IntPtr questAssistantOpenTaskDialogueMethod = IntPtr.Zero;

        private unsafe bool QuestAssistantTryOpenNpcDialogue(uint taskNetId, uint npcNetId, bool isStaticIdArg, int npcStaticId, string targetName, out string status)
        {
            status = "ok";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null || auraMonoStringNew == null)
            {
                status = "AuraMono unavailable";
                return false;
            }

            if (this.questAssistantOpenTaskDialogueMethod == IntPtr.Zero)
            {
                IntPtr cls = this.FindAuraMonoClassByFullName("XDTGame.UI.Panel.DialoguePanel");
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.UI.Panel", "DialoguePanel");
                }

                if (cls == IntPtr.Zero)
                {
                    status = "DialoguePanel class unresolved";
                    return false;
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "OpenTaskDialogue", 5);
                if (method == IntPtr.Zero)
                {
                    status = "OpenTaskDialogue method missing";
                    return false;
                }

                this.questAssistantDialoguePanelClass = cls;
                this.questAssistantOpenTaskDialogueMethod = method;
                this.QuestAssistantLog("TalkToNpc: DialoguePanel.OpenTaskDialogue resolved OK");
            }

            IntPtr nameObj = auraMonoStringNew(this.auraMonoRootDomain, targetName ?? string.Empty);
            if (nameObj == IntPtr.Zero)
            {
                status = "targetName string alloc failed";
                return false;
            }

            // Argument order/types verified against DialoguePanel.OpenTaskDialogue's real signature
            // (ilspy-dumps/XDTGameUI/XDTGame.UI.Panel/DialoguePanel.cs) and against the real caller
            // (NpcComponent.OpenTalkUI, TalkType.task branch) — see class-level comment above.
            uint localTaskNetId = taskNetId;
            uint localNpcNetId = npcNetId;
            bool isStaticId = isStaticIdArg;
            int localStaticId = npcStaticId;
            IntPtr* args = stackalloc IntPtr[5];
            args[0] = (IntPtr)(&localTaskNetId);
            args[1] = (IntPtr)(&localNpcNetId);
            args[2] = (IntPtr)(&isStaticId);
            args[3] = (IntPtr)(&localStaticId);
            args[4] = nameObj;

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.questAssistantOpenTaskDialogueMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "OpenTaskDialogue exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            // OpenTaskDialogue itself no-ops (does nothing, no exception) if a DialoguePanel is
            // already open (M<IUIManager>.Inst.GetView<DialoguePanel>() == null guard) — can't detect
            // that case from here; if nothing visibly happens in-game, that's the likely reason.
            status = "invoked, no exception";
            return true;
        }
    }
}
