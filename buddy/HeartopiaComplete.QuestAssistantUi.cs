using System.Collections.Generic;
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
        private Rect questAssistantWindowRect = new Rect(160f, 160f, 420f, 400f);
        private bool questAssistantWindowMouseOver = false;
        private int questAssistantFocusedTaskId = 0;
        private Vector2 questAssistantScrollPos = Vector2.zero;

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
            GUI.Label(new Rect(12f, 4f, w - 90f, 20f), "Quest Assistant", title);

            if (GUI.Button(new Rect(w - 70f, 4f, 58f, 20f), "Refresh", GUI.skin.button))
            {
                this.QuestAssistantOnDumpButtonClicked();
            }

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true };
            statusStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.8f);
            GUI.Label(new Rect(12f, 26f, w - 24f, 16f), this.questAssistantLastStatus ?? string.Empty, statusStyle);

            const float listTop = 46f;
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

                for (int c = 0; c < focused.Conditions.Count && dy < detailRect.yMax - 16f; c++)
                {
                    ConditionSnapshot cond = focused.Conditions[c];
                    string line = "- " + cond.Description + " (" + cond.Current + "/" + cond.Needed + ")" + (cond.Complete ? " (done)" : string.Empty);
                    GUI.Label(new Rect(detailRect.x + 6f, dy, detailRect.width - 12f, 32f), line, statusStyle);
                    dy += 32f;
                }

                // Phase 3-5 contextual action buttons attach here once implemented — the classifier
                // (focused.ObjectiveKind / ObjectiveTargetId) already has everything they need.
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
                case QuestObjectiveKind.Craft: return "Craft an item";
                case QuestObjectiveKind.CatchFish: return "Catch fish";
                case QuestObjectiveKind.CatchInsect: return "Catch an insect";
                case QuestObjectiveKind.CatchBird: return "Catch/photograph a bird";
                case QuestObjectiveKind.HomelandFarm: return "Homeland farming";
                default: return "(not automatable yet)";
            }
        }
    }
}
