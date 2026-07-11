using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private sealed class PuzzlePieceContext
        {
            public uint PieceNetId;
            public bool IsInBag;
            public bool IsInDraft;
            public uint OccupiedPlayerNetId;
            public int SortOrder;
            public int FirstIndex;
            public int MaskCount;
            public short TargetX;
            public short TargetY;
        }

        private static bool PuzzleLogsEnabled => MasterLogPuzzle;
        private const float PuzzleNetworkSettleSeconds = 0.12f;
        private const float PuzzleAutoRetrySeconds = 8.00f;
        private const int PuzzleMaxSolvePasses = 5;
        private const bool PuzzleAllowBroadAuraEntityScan = false;
        private const bool PuzzleAllowAuraInteractProbe = false;
        private const bool PuzzleAllowWidgetPointerScan = false;

        private bool puzzleAutoEnabled = false;
        private bool puzzleSolveRunning = false;
        private float puzzleNextAutoAttemptAt = 0f;
        private uint puzzleBoardNetId = 0U;
        private int puzzleStaticId = 0;
        private string puzzleStatus = "Capture a puzzle board first.";
        private int puzzleSentCount = 0;
        private object puzzleSolveCoroutine = null;
        private bool puzzleUiOpenLogged = false;
        private bool puzzleResolverProbeLogged = false;
        private readonly List<PuzzlePieceContext> puzzlePieces = new List<PuzzlePieceContext>(128);
        private MethodInfo puzzleJoinMethod = null;
        private MethodInfo puzzleLeaveMethod = null;
        private MethodInfo puzzleLockMethod = null;
        private MethodInfo puzzleUnlockMethod = null;
        private MethodInfo puzzleMoveMethod = null;
        private MethodInfo puzzleBingoMethod = null;
        private IntPtr puzzleAuraJoinMethod = IntPtr.Zero;
        private IntPtr puzzleAuraLeaveMethod = IntPtr.Zero;
        private IntPtr puzzleAuraLockMethod = IntPtr.Zero;
        private IntPtr puzzleAuraUnlockMethod = IntPtr.Zero;
        private IntPtr puzzleAuraMoveMethod = IntPtr.Zero;
        private IntPtr puzzleAuraBingoMethod = IntPtr.Zero;

        private float DrawPuzzleTab(int startY)
        {
            return this.DrawPuzzleSection(startY);
        }

        private float DrawPuzzleSection(int startY)
        {
            if (!this.puzzleUiOpenLogged)
            {
                this.puzzleUiOpenLogged = true;
                this.PuzzleLog("Puzzle UI opened. logsEnabled=" + PuzzleLogsEnabled);
            }

            int num = startY;
            const float left = 40f;
            const float width = 520f;
            Color mutedTextColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.78f);

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            headerStyle.normal.textColor = Color.white;

            GUIStyle smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
            smallStyle.normal.textColor = mutedTextColor;

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            statusStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            GUIStyle statLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            statLabelStyle.normal.textColor = mutedTextColor;

            GUIStyle statValueStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            statValueStyle.normal.textColor = Color.white;

            Rect headerRect = new Rect(left, num, width, 30f);
            GUI.Label(headerRect, "PUZZLE", headerStyle);
            num += 42;

            Rect toggleRect = new Rect(left, num, width, 52f);
            GUI.Box(toggleRect, "", this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(toggleRect, 1f);

            bool previousAutoPuzzle = this.puzzleAutoEnabled;
            this.puzzleAutoEnabled = this.DrawSwitchToggle(new Rect(toggleRect.x + 14f, toggleRect.y + 14f, 250f, 26f), this.puzzleAutoEnabled, "Auto Puzzle");
            GUI.Label(new Rect(toggleRect.x + 284f, toggleRect.y + 17f, toggleRect.width - 300f, 22f), this.puzzleSolveRunning ? "Solving..." : (this.puzzleAutoEnabled ? "Waiting for puzzle target..." : "Disabled"), statusStyle);
            if (this.puzzleAutoEnabled != previousAutoPuzzle)
            {
                this.SetPuzzleAutoEnabled(this.puzzleAutoEnabled, true);
            }

            num += 70;

            Rect statusRect = new Rect(left, num, width, 164f);
            GUI.Box(statusRect, "", this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(statusRect, 1f);
            GUI.Label(new Rect(statusRect.x + 12f, statusRect.y + 8f, statusRect.width - 24f, 18f), "STATUS", smallStyle);

            float statTop = statusRect.y + 34f;
            float statWidth = (statusRect.width - 36f) / 2f;
            this.DrawPuzzleStatBox(new Rect(statusRect.x + 12f, statTop, statWidth, 44f), "PIECES", this.puzzlePieces.Count.ToString(), statLabelStyle, statValueStyle);
            this.DrawPuzzleStatBox(new Rect(statusRect.x + 24f + statWidth, statTop, statWidth, 44f), "SENT", this.puzzleSentCount.ToString(), statLabelStyle, statValueStyle);

            GUI.Label(new Rect(statusRect.x + 12f, statusRect.y + 88f, statusRect.width - 24f, 44f), this.puzzleStatus, statusStyle);

            num += 170;
            return num + 20f;
        }

        private void SetPuzzleAutoEnabled(bool value, bool notify)
        {
            if (this.puzzleAutoEnabled == value && !notify)
            {
                return;
            }

            this.puzzleAutoEnabled = value;
            this.puzzleNextAutoAttemptAt = -999f;
            this.puzzleResolverProbeLogged = false;

            if (value)
            {
                this.puzzleStatus = "Auto Puzzle enabled. Face or open a puzzle board.";
                this.PuzzleLog("Toggle changed: enabled");
                if (notify)
                {
                    this.AddMenuNotification("Auto Puzzle Enabled", new Color(0.45f, 1f, 0.55f));
                }
            }
            else
            {
                this.PuzzleLog("Toggle changed: disabled");
                this.StopPuzzleSolve("Auto Puzzle disabled.");
                if (notify)
                {
                    this.AddMenuNotification("Auto Puzzle Disabled", new Color(1f, 0.55f, 0.55f));
                }
            }
        }

        private void ForceStopPuzzleAuto()
        {
            this.SetPuzzleAutoEnabled(false, false);
            this.ResetPuzzleContext("Auto Puzzle stopped.");
        }

        private void DrawPuzzleStatBox(Rect rect, string label, string value, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            GUI.Box(rect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
            GUI.Label(new Rect(rect.x, rect.y + 4f, rect.width, 16f), label, labelStyle);
            GUI.Label(new Rect(rect.x, rect.y + 21f, rect.width, 18f), value, valueStyle);
        }

        private bool TryCapturePuzzleFromCurrentTarget()
        {
            try
            {
                this.PuzzleLog("Capture started.");
                this.LogPuzzleResolverProbeOnce();

                if (this.TryCapturePuzzleFromOpenPanels())
                {
                    return true;
                }

                if (this.TryCapturePuzzleFromAuraMonoPlayerJigsawStatus())
                {
                    return true;
                }

                if (this.TryCapturePuzzleFromPuzzleWidgets())
                {
                    return true;
                }

                if (PuzzleAllowBroadAuraEntityScan && this.TryCapturePuzzleFromAuraMonoBoardScan())
                {
                    return true;
                }

                List<ulong> candidates = new List<ulong>(16);
                HashSet<ulong> candidateSet = new HashSet<ulong>();

                bool focusedOk = false;
                ulong focused = 0UL;
                string focusedStatus = string.Empty;
                try
                {
                    focusedOk = this.TryGetCurrentFocusedLevelObjectNetId(out focused, out focusedStatus) && focused != 0UL;
                }
                catch (Exception ex)
                {
                    focusedStatus = "exception: " + ex.GetType().Name + ": " + ex.Message;
                }

                this.PuzzleLog("Resolver focus: ok=" + focusedOk + " levelObject=" + focused + " status=" + focusedStatus);
                if (focusedOk)
                {
                    AddNetCookCandidateLevelObject(candidates, candidateSet, focused);
                }

                int beforeInteract = candidates.Count;
                string interactStatus = string.Empty;
                bool interactOk = false;
                try
                {
                    interactOk = this.TryGetCurrentInteractTargetLevelObjects(candidates, out interactStatus, candidateSet);
                }
                catch (Exception ex)
                {
                    interactStatus = "exception: " + ex.GetType().Name + ": " + ex.Message;
                }
                this.PuzzleLog("Resolver interact: ok=" + interactOk + " added=" + (candidates.Count - beforeInteract) + " total=" + candidates.Count + " status=" + interactStatus);

                if (PuzzleAllowAuraInteractProbe)
                {
                    int beforeAura = candidates.Count;
                    string auraStatus = string.Empty;
                    bool auraOk = false;
                    try
                    {
                        auraOk = this.TryGetCurrentInteractTargetLevelObjectsViaAuraMono(candidates, out auraStatus, candidateSet);
                    }
                    catch (Exception ex)
                    {
                        auraStatus = "exception: " + ex.GetType().Name + ": " + ex.Message;
                    }
                    this.PuzzleLog("Resolver aura-mono interact: ok=" + auraOk + " added=" + (candidates.Count - beforeAura) + " total=" + candidates.Count + " status=" + auraStatus);
                }
                else
                {
                    this.PuzzleLog("Resolver aura-mono interact skipped: disabled for puzzle safety.");
                }

                if (candidates.Count <= 0)
                {
                    this.puzzleStatus = "No puzzle resolver candidates found.";
                    this.PuzzleLog(this.puzzleStatus);
                    return false;
                }

                this.PuzzleLog("Capture candidates=" + candidates.Count + " first=[" + string.Join(", ", candidates.Take(8).Select(c => c.ToString()).ToArray()) + "]");
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (this.TryResolvePuzzleFromLevelObject(candidates[i], out uint boardNetId, out int staticId, out Vector3 worldPosition, out string status))
                    {
                        this.puzzleBoardNetId = boardNetId;
                        this.puzzleStaticId = staticId;
                        this.puzzleSentCount = 0;
                        this.puzzlePieces.Clear();
                        this.puzzleStatus = "Captured puzzle board " + boardNetId + " staticId=" + staticId + ".";
                        this.PuzzleLog(this.puzzleStatus);
                        this.RefreshPuzzlePieces(false);
                        return true;
                    }

                    this.PuzzleLog("Puzzle candidate rejected: " + candidates[i] + " " + status);
                }

                this.puzzleStatus = "Focused target is not a jigsaw puzzle.";
                this.PuzzleLog(this.puzzleStatus);
                return false;
            }
            catch (Exception ex)
            {
                this.puzzleStatus = "Puzzle capture exception: " + ex.Message;
                this.PuzzleLog(this.puzzleStatus);
                return false;
            }
        }

        private bool TryResolvePuzzleFromLevelObject(ulong levelObjectNetId, out uint boardNetId, out int staticId, out Vector3 worldPosition, out string status)
        {
            boardNetId = 0U;
            staticId = 0;
            worldPosition = Vector3.zero;
            status = "Not a puzzle level object.";

            if (this.TryResolvePuzzleFromLevelObjectManaged(levelObjectNetId, out boardNetId, out staticId, out worldPosition, out status))
            {
                this.PuzzleLog("Resolver managed success: levelObject=" + levelObjectNetId + " board=" + boardNetId + " staticId=" + staticId + " pos=" + worldPosition);
                return true;
            }
            this.PuzzleLog("Resolver managed failed: levelObject=" + levelObjectNetId + " status=" + status);

            uint ownerNetId = ExtractNetCookOwnerNetId(levelObjectNetId);
            this.PuzzleLog("Resolver owner fallback: levelObject=" + levelObjectNetId + " ownerNetId=" + ownerNetId);
            if (ownerNetId != 0U && this.TryGetJigsawPuzzleComponentData(ownerNetId, out object componentData, out status))
            {
                boardNetId = ownerNetId;
                this.TryReadManagedInt32Member(componentData, "staticId", out staticId);
                if (this.TryGetAuraMonoEntityObjectByNetId(boardNetId, out IntPtr entityObj) && entityObj != IntPtr.Zero)
                {
                    this.TryGetAuraMonoEntityPosition(entityObj, out worldPosition);
                }
                if (worldPosition == Vector3.zero)
                {
                    this.TryGetEntityPositionByNetId(boardNetId, out worldPosition);
                }
                status = "Puzzle context ready.";
                this.PuzzleLog("Resolver owner fallback success: board=" + boardNetId + " staticId=" + staticId + " pos=" + worldPosition);
                return staticId > 0;
            }
            this.PuzzleLog("Resolver owner fallback failed: ownerNetId=" + ownerNetId + " status=" + status);

            return false;
        }

        private bool TryCapturePuzzleFromAuraMonoBoardScan()
        {
            try
            {
                this.PuzzleLog("Resolver board scan started.");
                if (!this.TryGetNetCookScanOrigin(out Vector3 origin, out string originStatus))
                {
                    origin = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                }

                if (!this.TryEnumerateAuraMonoLoadedEntityObjects(out List<IntPtr> entities, out string status) || entities.Count <= 0)
                {
                    this.PuzzleLog("Resolver board scan unavailable: " + status);
                    return false;
                }

                int inspected = 0;
                int boardComponents = 0;
                int puzzleNamedComponents = 0;
                int pieceBelongComponents = 0;
                uint bestBoardNetId = 0U;
                int bestStaticId = 0;
                float bestDistance = float.MaxValue;
                List<string> puzzleComponentSamples = new List<string>(8);
                for (int i = 0; i < entities.Count && i < 4096; i++)
                {
                    IntPtr entityObj = entities[i];
                    if (entityObj == IntPtr.Zero || !this.TryGetAuraMonoEntityNetId(entityObj, out uint entityNetId) || entityNetId == 0U)
                    {
                        continue;
                    }

                    inspected++;
                    if (!this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr componentsObj, "GetAllComponents") || componentsObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    List<IntPtr> components = new List<IntPtr>(32);
                    if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components) || components.Count <= 0)
                    {
                        continue;
                    }

                    bool hasBoard = false;
                    bool hasPiece = false;
                    uint belongBoardNetId = 0U;
                    int staticId = 0;
                    for (int c = 0; c < components.Count && c < 128; c++)
                    {
                        IntPtr componentObj = components[c];
                        if (componentObj == IntPtr.Zero || auraMonoObjectGetClass == null)
                        {
                            continue;
                        }

                        string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(componentObj));
                        if (string.IsNullOrEmpty(className))
                        {
                            continue;
                        }

                        if (this.LooksLikePuzzleComponentName(className))
                        {
                            puzzleNamedComponents++;
                            if (puzzleComponentSamples.Count < 8 && !puzzleComponentSamples.Contains(className))
                            {
                                puzzleComponentSamples.Add(className);
                            }
                        }

                        if (this.LooksLikePuzzleBoardComponentName(className))
                        {
                            hasBoard = true;
                            continue;
                        }

                        if (this.LooksLikePuzzlePieceComponentName(className))
                        {
                            hasPiece = true;
                            continue;
                        }

                        if (this.LooksLikePuzzlePieceBelongComponentName(className))
                        {
                            if (this.TryGetPuzzleBelongBoardNetId(componentObj, out uint rawBelongBoardNetId) && rawBelongBoardNetId != 0U)
                            {
                                belongBoardNetId = rawBelongBoardNetId;
                                pieceBelongComponents++;
                            }
                            continue;
                        }

                        if (this.LooksLikeHomelandPuzzleComponentName(className))
                        {
                            if (this.TryGetMonoObjectMember(componentObj, "componentData", out IntPtr componentDataObj) || this.TryGetMonoObjectMember(componentObj, "ComponentData", out componentDataObj) || this.TryGetMonoObjectMember(componentObj, "_componentData", out componentDataObj))
                            {
                                this.TryGetMonoInt32Member(componentDataObj, "staticId", out staticId);
                            }
                            if (staticId <= 0)
                            {
                                this.TryGetMonoInt32Member(componentObj, "staticId", out staticId);
                            }
                        }
                    }

                    if (!hasBoard && (!hasPiece || belongBoardNetId == 0U))
                    {
                        continue;
                    }

                    if (hasBoard)
                    {
                        boardComponents++;
                    }
                    Vector3 position = origin;
                    this.TryGetAuraMonoEntityPosition(entityObj, out position);
                    if (position == Vector3.zero)
                    {
                        this.TryExtractHomePositionMonoObject(entityObj, out position);
                    }

                    float distance = position != Vector3.zero && origin != Vector3.zero ? Vector3.Distance(origin, position) : boardComponents;
                    uint candidateBoardNetId = hasBoard ? entityNetId : belongBoardNetId;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestBoardNetId = candidateBoardNetId;
                        bestStaticId = staticId;
                    }
                }

                this.PuzzleLog("Resolver board scan: origin=" + origin + " originStatus=" + originStatus + " entities=" + entities.Count + " inspected=" + inspected + " puzzleComponents=" + puzzleNamedComponents + " boards=" + boardComponents + " pieceBelongs=" + pieceBelongComponents + " best=" + bestBoardNetId + " staticId=" + bestStaticId + " d=" + (bestDistance < float.MaxValue ? bestDistance.ToString("F1") : "-") + " samples=[" + string.Join("; ", puzzleComponentSamples.ToArray()) + "]");
                if (bestBoardNetId == 0U)
                {
                    return false;
                }

                this.puzzleBoardNetId = bestBoardNetId;
                this.puzzleStaticId = Mathf.Max(0, bestStaticId);
                this.puzzleSentCount = 0;
                this.puzzlePieces.Clear();
                this.puzzleStatus = "Captured puzzle board " + bestBoardNetId + (bestStaticId > 0 ? " staticId=" + bestStaticId : string.Empty) + ".";
                this.PuzzleLog(this.puzzleStatus);
                this.RefreshPuzzlePieces(false);
                return true;
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Resolver board scan exception: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private bool LooksLikePuzzleComponentName(string className)
        {
            return !string.IsNullOrEmpty(className)
                && className.IndexOf("JigsawPuzzle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool LooksLikePuzzleBoardComponentName(string className)
        {
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }

            return className.IndexOf("JigsawPuzzleBoardComponent", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("JigsawPuzzleBoar", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("JPBdC", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool LooksLikePuzzlePieceComponentName(string className)
        {
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }

            if (className.IndexOf("Belong", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Position", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Lock", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Bingo", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Network", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Event", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Bag", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Attach", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Return", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Remove", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return className.IndexOf("JigsawPuzzlePieceComponent", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("JigsawPuzzlePiec", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool LooksLikePuzzlePieceBelongComponentName(string className)
        {
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }

            return className.IndexOf("JigsawPuzzlePieceBelongComponent", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("JigsawPuzzlePieceBelong", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool LooksLikeHomelandPuzzleComponentName(string className)
        {
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }

            return className.IndexOf("JigsawPuzzleComponent", StringComparison.OrdinalIgnoreCase) >= 0
                && className.IndexOf("Board", StringComparison.OrdinalIgnoreCase) < 0
                && className.IndexOf("Piece", StringComparison.OrdinalIgnoreCase) < 0
                && className.IndexOf("Belong", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private bool TryGetPuzzleBelongBoardNetId(IntPtr belongComponentObj, out uint boardNetId)
        {
            boardNetId = 0U;
            if (belongComponentObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoObjectMember(belongComponentObj, "Value", out IntPtr valueObj) && valueObj != IntPtr.Zero)
            {
                if (this.TryGetMonoUInt32Member(valueObj, "NetId", out boardNetId) && boardNetId != 0U)
                {
                    return true;
                }

                if (this.TryGetMonoUInt32Member(valueObj, "_netId", out boardNetId) && boardNetId != 0U)
                {
                    return true;
                }

                if (this.TryInvokeAuraMonoZeroArg(valueObj, out IntPtr boxedNetId, "get_NetId", "GetNetId") && boxedNetId != IntPtr.Zero && this.TryUnboxMonoUInt32(boxedNetId, out boardNetId) && boardNetId != 0U)
                {
                    return true;
                }
            }

            return this.TryGetMonoUInt32Member(belongComponentObj, "boardNetId", out boardNetId)
                || this.TryGetMonoUInt32Member(belongComponentObj, "jigsawPuzzleBoardNetId", out boardNetId);
        }

        private bool TryCapturePuzzleFromOpenPanels()
        {
            int inspected = 0;
            int matched = 0;
            try
            {
                Component[] components = Resources.FindObjectsOfTypeAll<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null)
                    {
                        continue;
                    }

                    Type type = component.GetType();
                    string typeName = type != null ? type.FullName ?? type.Name : string.Empty;
                    if (string.IsNullOrEmpty(typeName) || typeName.IndexOf("JigsawPuzzle", StringComparison.OrdinalIgnoreCase) < 0 || typeName.IndexOf("Panel", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    inspected++;
                    uint boardNetId = 0U;
                    int staticId = 0;
                    this.TryReadManagedUInt32Member(component, "_jigsawPuzzleNetId", out boardNetId);
                    if (boardNetId == 0U)
                    {
                        this.TryReadManagedUInt32Member(component, "jigsawPuzzleNetId", out boardNetId);
                    }
                    this.TryReadManagedInt32Member(component, "_staticId", out staticId);
                    if (staticId <= 0)
                    {
                        this.TryReadManagedInt32Member(component, "staticId", out staticId);
                    }

                    if ((boardNetId == 0U || staticId <= 0) && this.TryGetIl2CppObjectPointer(component, out IntPtr panelObj) && panelObj != IntPtr.Zero)
                    {
                        if (boardNetId == 0U)
                        {
                            this.TryGetMonoUInt32Member(panelObj, "_jigsawPuzzleNetId", out boardNetId);
                            if (boardNetId == 0U)
                            {
                                this.TryGetMonoUInt32Member(panelObj, "jigsawPuzzleNetId", out boardNetId);
                            }
                        }
                        if (staticId <= 0)
                        {
                            this.TryGetMonoInt32Member(panelObj, "_staticId", out staticId);
                            if (staticId <= 0)
                            {
                                this.TryGetMonoInt32Member(panelObj, "staticId", out staticId);
                            }
                        }
                    }

                    this.PuzzleLog("Resolver open-panel candidate: type=" + typeName + " board=" + boardNetId + " staticId=" + staticId);
                    if (boardNetId == 0U)
                    {
                        continue;
                    }

                    matched++;
                    this.puzzleBoardNetId = boardNetId;
                    this.puzzleStaticId = Mathf.Max(0, staticId);
                    this.puzzleSentCount = 0;
                    this.puzzlePieces.Clear();
                    this.puzzleStatus = "Captured open puzzle panel board " + boardNetId + (staticId > 0 ? " staticId=" + staticId : string.Empty) + ".";
                    this.PuzzleLog(this.puzzleStatus);
                    this.RefreshPuzzlePieces(false);
                    return true;
                }

                this.PuzzleLog("Resolver open-panel scan: inspected=" + inspected + " matched=" + matched + ".");
                this.LogPuzzleNamedGameObjects();
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Resolver open-panel scan exception: " + ex.GetType().Name + ": " + ex.Message);
            }

            return false;
        }

        private void LogPuzzleNamedGameObjects()
        {
            try
            {
                GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
                int inspected = 0;
                int matched = 0;
                List<string> samples = new List<string>(8);
                List<string> pathSamples = new List<string>(6);
                List<string> componentSamples = new List<string>(8);
                List<string> childSamples = new List<string>(6);
                for (int i = 0; i < objects.Length && i < 20000; i++)
                {
                    GameObject obj = objects[i];
                    if (obj == null)
                    {
                        continue;
                    }

                    inspected++;
                    string name = obj.name ?? string.Empty;
                    if (name.IndexOf("Jigsaw", StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("Puzzle", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    matched++;
                    if (samples.Count < 8)
                    {
                        samples.Add(name + "/active=" + obj.activeInHierarchy);
                    }
                    if (pathSamples.Count < 6)
                    {
                        pathSamples.Add(this.GetSafeHierarchyPath(obj.transform, 7));
                    }
                    if (componentSamples.Count < 8)
                    {
                        componentSamples.Add(name + " comps=[" + this.GetSafeComponentTypeList(obj, 8) + "]");
                    }
                    if (childSamples.Count < 6)
                    {
                        childSamples.Add(name + " children=[" + this.GetSafeChildNameList(obj.transform, 8) + "]");
                    }
                }

                this.PuzzleLog("Resolver puzzle GameObject scan: inspected=" + inspected
                    + " matched=" + matched
                    + " first=[" + string.Join("; ", samples.ToArray()) + "]"
                    + " paths=[" + string.Join("; ", pathSamples.ToArray()) + "]"
                    + " components=[" + string.Join("; ", componentSamples.ToArray()) + "]"
                    + " children=[" + string.Join("; ", childSamples.ToArray()) + "]");
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Resolver puzzle GameObject scan exception: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private string GetSafeComponentTypeList(GameObject obj, int limit)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            try
            {
                Component[] components = obj.GetComponents<Component>();
                if (components == null || components.Length <= 0)
                {
                    return string.Empty;
                }

                List<string> names = new List<string>(limit);
                for (int i = 0; i < components.Length && names.Count < limit; i++)
                {
                    Component component = components[i];
                    if (component == null)
                    {
                        names.Add("<missing>");
                        continue;
                    }

                    Type type = component.GetType();
                    names.Add(type != null ? type.FullName ?? type.Name : "<unknown>");
                }

                return string.Join(",", names.ToArray());
            }
            catch (Exception ex)
            {
                return "ex:" + ex.GetType().Name;
            }
        }

        private string GetSafeHierarchyPath(Transform transform, int limit)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            try
            {
                List<string> parts = new List<string>(limit);
                Transform current = transform;
                int depth = 0;
                while (current != null && depth < limit)
                {
                    parts.Add(current.name ?? string.Empty);
                    current = current.parent;
                    depth++;
                }

                parts.Reverse();
                return string.Join("/", parts.ToArray());
            }
            catch (Exception ex)
            {
                return "ex:" + ex.GetType().Name;
            }
        }

        private string GetSafeChildNameList(Transform transform, int limit)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            try
            {
                List<string> names = new List<string>(limit);
                int count = Mathf.Min(transform.childCount, limit);
                for (int i = 0; i < count; i++)
                {
                    Transform child = transform.GetChild(i);
                    names.Add(child != null ? child.name ?? string.Empty : "<null>");
                }

                return string.Join(",", names.ToArray());
            }
            catch (Exception ex)
            {
                return "ex:" + ex.GetType().Name;
            }
        }

        private bool TryCapturePuzzleFromPuzzleWidgets()
        {
            if (!PuzzleAllowWidgetPointerScan)
            {
                this.PuzzleLog("Resolver puzzle widget pointer scan skipped: disabled for crash safety.");
                return false;
            }

            int inspectedObjects = 0;
            int matchedObjects = 0;
            int inspectedComponents = 0;
            int scrollDataCount = 0;
            Dictionary<uint, List<PuzzlePieceContext>> piecesByBoard = new Dictionary<uint, List<PuzzlePieceContext>>();
            Dictionary<uint, int> staticByBoard = new Dictionary<uint, int>();
            List<string> objectSamples = new List<string>(8);
            List<string> componentSamples = new List<string>(8);
            List<string> dataSamples = new List<string>(10);

            try
            {
                GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
                for (int i = 0; i < objects.Length && i < 25000; i++)
                {
                    GameObject obj = objects[i];
                    if (obj == null)
                    {
                        continue;
                    }

                    inspectedObjects++;
                    string objectName = obj.name ?? string.Empty;
                    if (objectName.IndexOf("JigsawPuzzleScrollWidget", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    matchedObjects++;
                    if (objectSamples.Count < 8)
                    {
                        objectSamples.Add(objectName + "/active=" + obj.activeInHierarchy);
                    }

                    Component[] components = null;
                    try
                    {
                        components = obj.GetComponents<Component>();
                    }
                    catch
                    {
                        components = null;
                    }

                    if (components == null)
                    {
                        continue;
                    }

                    for (int c = 0; c < components.Length; c++)
                    {
                        Component component = components[c];
                        if (component == null)
                        {
                            continue;
                        }

                        inspectedComponents++;
                        string componentName = component.GetType() != null ? component.GetType().FullName ?? component.GetType().Name : "<unknown>";
                        if (componentSamples.Count < 8)
                        {
                            componentSamples.Add(componentName);
                        }

                        if (!this.TryReadPuzzleWidgetScrollData(component, out uint boardNetId, out int staticId, out PuzzlePieceContext piece, out string source))
                        {
                            continue;
                        }

                        scrollDataCount++;
                        if (!piecesByBoard.TryGetValue(boardNetId, out List<PuzzlePieceContext> pieces))
                        {
                            pieces = new List<PuzzlePieceContext>(32);
                            piecesByBoard[boardNetId] = pieces;
                        }

                        if (piece.PieceNetId != 0U && !pieces.Any(existing => existing.PieceNetId == piece.PieceNetId))
                        {
                            pieces.Add(piece);
                        }

                        if (staticId > 0)
                        {
                            staticByBoard[boardNetId] = staticId;
                        }

                        if (dataSamples.Count < 10)
                        {
                            dataSamples.Add(piece.PieceNetId + "@idx=" + piece.FirstIndex + "/board=" + boardNetId + "/static=" + staticId + "/" + source);
                        }
                    }
                }

                this.PuzzleLog("Resolver puzzle widget scan: objects=" + inspectedObjects
                    + " matchedObjects=" + matchedObjects
                    + " components=" + inspectedComponents
                    + " scrollData=" + scrollDataCount
                    + " objectSamples=[" + string.Join("; ", objectSamples.ToArray()) + "]"
                    + " componentSamples=[" + string.Join("; ", componentSamples.Distinct().Take(8).ToArray()) + "]"
                    + " dataSamples=[" + string.Join("; ", dataSamples.ToArray()) + "]");

                if (piecesByBoard.Count <= 0)
                {
                    return false;
                }

                KeyValuePair<uint, List<PuzzlePieceContext>> best = piecesByBoard.OrderByDescending(pair => pair.Value.Count).First();
                if (best.Key == 0U || best.Value.Count <= 0)
                {
                    return false;
                }

                this.puzzleBoardNetId = best.Key;
                this.puzzleStaticId = staticByBoard.TryGetValue(best.Key, out int capturedStaticId) ? Mathf.Max(0, capturedStaticId) : 0;
                this.puzzleSentCount = 0;
                this.puzzlePieces.Clear();
                this.puzzlePieces.AddRange(best.Value.OrderBy(p => p.FirstIndex).ThenBy(p => p.SortOrder));

                this.UpdatePuzzleTargetSlots();
                this.puzzleStatus = "Captured puzzle widgets board " + this.puzzleBoardNetId + " pieces=" + this.puzzlePieces.Count + ".";
                this.PuzzleLog(this.puzzleStatus + " staticId=" + this.puzzleStaticId);

                List<PuzzlePieceContext> widgetPieces = new List<PuzzlePieceContext>(this.puzzlePieces);
                if (!this.RefreshPuzzlePieces(false) || this.puzzlePieces.Count <= 0)
                {
                    this.puzzlePieces.Clear();
                    this.puzzlePieces.AddRange(widgetPieces);
                    this.UpdatePuzzleTargetSlots();
                    this.puzzleStatus = "Using widget piece data: " + this.puzzlePieces.Count + ".";
                    this.PuzzleLog(this.puzzleStatus);
                }

                return true;
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Resolver puzzle widget scan exception: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private bool TryReadPuzzleWidgetScrollData(Component component, out uint boardNetId, out int staticId, out PuzzlePieceContext piece, out string source)
        {
            boardNetId = 0U;
            staticId = 0;
            piece = null;
            source = string.Empty;

            if (component == null || !this.TryGetIl2CppObjectPointer(component, out IntPtr componentObj) || componentObj == IntPtr.Zero)
            {
                return false;
            }

            HashSet<IntPtr> visited = new HashSet<IntPtr>();
            return this.TryReadPuzzleScrollDataFromObject(componentObj, 0, "component", visited, out boardNetId, out staticId, out piece, out source);
        }

        private bool TryReadPuzzleScrollDataFromObject(IntPtr obj, int depth, string path, HashSet<IntPtr> visited, out uint boardNetId, out int staticId, out PuzzlePieceContext piece, out string source)
        {
            boardNetId = 0U;
            staticId = 0;
            piece = null;
            source = path;

            if (obj == IntPtr.Zero || depth > 2 || visited.Contains(obj))
            {
                return false;
            }

            visited.Add(obj);

            foreach (string scrollMember in new[] { "ScrollData", "_scrollData", "scrollData", "Data", "_data", "data" })
            {
                if (this.TryGetMonoObjectMember(obj, scrollMember, out IntPtr scrollDataObj) && this.TryReadPuzzleScrollDataObject(scrollDataObj, out boardNetId, out staticId, out piece))
                {
                    source = path + "." + scrollMember;
                    return true;
                }
            }

            if (this.TryReadPuzzleScrollDataObject(obj, out boardNetId, out staticId, out piece))
            {
                source = path;
                return true;
            }

            foreach (string nestedMember in new[] { "Widget", "_widget", "widget", "View", "_view", "view", "Owner", "_owner", "owner", "Target", "_target", "target", "Binder", "_binder", "binder", "m_Panel", "panel" })
            {
                if (!this.TryGetMonoObjectMember(obj, nestedMember, out IntPtr nestedObj) || nestedObj == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryReadPuzzleScrollDataFromObject(nestedObj, depth + 1, path + "." + nestedMember, visited, out boardNetId, out staticId, out piece, out source))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryReadPuzzleScrollDataObject(IntPtr scrollDataObj, out uint boardNetId, out int staticId, out PuzzlePieceContext piece)
        {
            boardNetId = 0U;
            staticId = 0;
            piece = null;

            if (scrollDataObj == IntPtr.Zero)
            {
                return false;
            }

            uint pieceNetId = 0U;
            int index = -1;
            this.TryGetMonoUInt32Member(scrollDataObj, "jigsawPuzzleBoardNetId", out boardNetId);
            if (boardNetId == 0U)
            {
                this.TryGetMonoUInt32Member(scrollDataObj, "_jigsawPuzzleBoardNetId", out boardNetId);
            }

            this.TryGetMonoUInt32Member(scrollDataObj, "pieceNetId", out pieceNetId);
            if (pieceNetId == 0U)
            {
                this.TryGetMonoUInt32Member(scrollDataObj, "_pieceNetId", out pieceNetId);
            }

            this.TryGetMonoInt32Member(scrollDataObj, "staticId", out staticId);
            if (staticId <= 0)
            {
                this.TryGetMonoInt32Member(scrollDataObj, "_staticId", out staticId);
            }

            if (!this.TryGetMonoInt32Member(scrollDataObj, "index", out index))
            {
                this.TryGetMonoInt32Member(scrollDataObj, "_index", out index);
            }

            if (boardNetId == 0U || pieceNetId == 0U)
            {
                return false;
            }

            piece = new PuzzlePieceContext
            {
                PieceNetId = pieceNetId,
                IsInBag = true,
                IsInDraft = false,
                OccupiedPlayerNetId = 0U,
                SortOrder = Mathf.Max(0, index),
                FirstIndex = Mathf.Max(0, index),
                MaskCount = 1
            };
            return true;
        }

        private bool TryCapturePuzzleFromAuraMonoPlayerJigsawStatus()
        {
            try
            {
                if (auraMonoRuntimeInvoke == null)
                {
                    this.PuzzleLog("Resolver player jigsaw status skipped: AuraMono invoke unavailable.");
                    return false;
                }

                IntPtr characterClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Game.GameMode.Character");
                IntPtr getCharacterMethod = characterClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(characterClass, "get_character", 0) : IntPtr.Zero;
                if (characterClass == IntPtr.Zero || getCharacterMethod == IntPtr.Zero)
                {
                    this.PuzzleLog("Resolver player jigsaw status unavailable: characterClass=0x" + characterClass.ToInt64().ToString("X") + " get_character=0x" + getCharacterMethod.ToInt64().ToString("X"));
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr characterObj = auraMonoRuntimeInvoke(getCharacterMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || characterObj == IntPtr.Zero)
                {
                    this.PuzzleLog("Resolver player jigsaw status unavailable: character object missing.");
                    return false;
                }

                IntPtr playerObj;
                if (!this.TryGetMonoObjectMember(characterObj, "Player", out playerObj) && !this.TryGetMonoObjectMember(characterObj, "player", out playerObj))
                {
                    this.PuzzleLog("Resolver player jigsaw status unavailable: Character.player missing.");
                    return false;
                }

                IntPtr statusObj;
                if (!this.TryGetMonoObjectMember(playerObj, "Status", out statusObj) && !this.TryGetMonoObjectMember(playerObj, "status", out statusObj))
                {
                    this.PuzzleLog("Resolver player jigsaw status unavailable: Player.Status missing.");
                    return false;
                }

                IntPtr jigsawStatusObj;
                if (!this.TryGetMonoObjectMember(statusObj, "JigsawPuzzleStatus", out jigsawStatusObj) && !this.TryGetMonoObjectMember(statusObj, "jigsawPuzzleStatus", out jigsawStatusObj))
                {
                    this.PuzzleLog("Resolver player jigsaw status unavailable: JigsawPuzzleStatus missing.");
                    return false;
                }

                ulong levelObjectNetId = 0UL;
                uint boardNetId = 0U;
                this.TryGetMonoUInt64Member(jigsawStatusObj, "JigsawPuzzleObjectNetId", out levelObjectNetId);
                if (levelObjectNetId == 0UL)
                {
                    this.TryGetMonoUInt64Member(jigsawStatusObj, "jigsawPuzzleObjectNetId", out levelObjectNetId);
                }

                this.TryGetMonoUInt32Member(jigsawStatusObj, "JigsawPuzzleNetId", out boardNetId);
                if (boardNetId == 0U)
                {
                    this.TryGetMonoUInt32Member(jigsawStatusObj, "jigsawPuzzleNetId", out boardNetId);
                }

                this.PuzzleLog("Resolver player jigsaw status: levelObject=" + levelObjectNetId + " board=" + boardNetId + ".");
                if (boardNetId == 0U)
                {
                    return false;
                }

                if (levelObjectNetId != 0UL && this.TryResolvePuzzleFromLevelObject(levelObjectNetId, out uint resolvedBoardNetId, out int staticId, out Vector3 worldPosition, out string resolveStatus))
                {
                    this.puzzleBoardNetId = resolvedBoardNetId != 0U ? resolvedBoardNetId : boardNetId;
                    this.puzzleStaticId = Mathf.Max(0, staticId);
                    this.PuzzleLog("Resolver player jigsaw level object resolved: " + resolveStatus);
                }
                else
                {
                    this.puzzleBoardNetId = boardNetId;
                    this.puzzleStaticId = 0;
                    if (levelObjectNetId != 0UL)
                    {
                        this.PuzzleLog("Resolver player jigsaw level object not resolved; using board id only.");
                    }
                }

                this.puzzleSentCount = 0;
                this.puzzlePieces.Clear();
                this.puzzleStatus = "Captured active puzzle board " + this.puzzleBoardNetId + ".";
                this.PuzzleLog(this.puzzleStatus + " levelObject=" + levelObjectNetId + " staticId=" + this.puzzleStaticId);
                this.RefreshPuzzlePieces(false);
                return true;
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Resolver player jigsaw status exception: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private bool TryResolvePuzzleFromLevelObjectManaged(ulong levelObjectNetId, out uint boardNetId, out int staticId, out Vector3 worldPosition, out string status)
        {
            boardNetId = 0U;
            staticId = 0;
            worldPosition = Vector3.zero;
            status = "Managed LevelObject unavailable.";

            try
            {
                Type levelObjectManagerType = this.FindLevelObjectManagerRuntimeType();
                if (levelObjectManagerType == null)
                {
                    status = "LevelObjectManager type unavailable.";
                    return false;
                }

                PropertyInfo instanceProperty = levelObjectManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object levelObjectManager = instanceProperty != null ? instanceProperty.GetValue(null, null) : null;
                if (levelObjectManager == null)
                {
                    status = "LevelObjectManager.Instance unavailable.";
                    return false;
                }

                MethodInfo getLevelObjectMethod = this.GetMethodQuiet(
                        levelObjectManagerType,
                        "GetLevelObject",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        new Type[] { typeof(ulong) })
                    ?? this.GetMethodQuiet(
                        levelObjectManagerType,
                        "GetLevelObject",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        new Type[] { typeof(ulong), typeof(int) });
                if (getLevelObjectMethod == null)
                {
                    status = "LevelObjectManager.GetLevelObject unavailable.";
                    return false;
                }

                object levelObject = getLevelObjectMethod.GetParameters().Length == 1
                    ? getLevelObjectMethod.Invoke(levelObjectManager, new object[] { levelObjectNetId })
                    : getLevelObjectMethod.Invoke(levelObjectManager, new object[] { levelObjectNetId, 0 });
                if (levelObject == null)
                {
                    status = "Level object missing.";
                    return false;
                }

                this.TryGetNetCookLevelObjectPosition(levelObject, out worldPosition);
                if (!this.TryReadManagedUInt32Member(levelObject, "ownerNetId", out boardNetId) || boardNetId == 0U)
                {
                    status = "Puzzle owner netId missing.";
                    return false;
                }

                if (!this.TryGetJigsawPuzzleComponentData(boardNetId, out object componentData, out status))
                {
                    status = "Puzzle component lookup failed for owner " + boardNetId + ": " + status;
                    return false;
                }

                if (!this.TryReadManagedInt32Member(componentData, "staticId", out staticId) || staticId <= 0)
                {
                    status = "Puzzle staticId missing.";
                    return false;
                }

                status = "Puzzle context ready.";
                return true;
            }
            catch (Exception ex)
            {
                status = "Puzzle managed resolve exception: " + ex.Message;
                return false;
            }
        }

        private bool TryGetJigsawPuzzleComponentData(uint boardNetId, out object componentData, out string status)
        {
            componentData = null;
            status = "JigsawPuzzleComponentData unavailable.";
            if (boardNetId == 0U)
            {
                return false;
            }

            try
            {
                Type dataCenterType = this.FindLoadedType("XDTDataAndProtocol.ComponentsData.DataCenter", "DataCenter");
                Type componentDataType = this.FindLoadedType("XDTDataAndProtocol.ComponentsData.JigsawPuzzleComponentData", "JigsawPuzzleComponentData");
                Type netIdType = this.FindLoadedType("EcsClient.XDT.Scene.Shared.Data.SharedData.NetId", "XDT.Scene.Shared.NetId", "NetId");
                if (dataCenterType == null || componentDataType == null || netIdType == null)
                {
                    status = "Puzzle component types unavailable.";
                    return false;
                }

                MethodInfo tryGetMethod = null;
                foreach (MethodInfo method in dataCenterType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method == null || method.Name != "TryGetComponentData" || !method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 2 && parameters[0].ParameterType == netIdType)
                    {
                        tryGetMethod = method.MakeGenericMethod(componentDataType);
                        break;
                    }
                }

                if (tryGetMethod == null)
                {
                    status = "DataCenter.TryGetComponentData unavailable.";
                    return false;
                }

                object netIdArg = this.CreateNetCookNetIdArgument(netIdType, boardNetId);
                object dataBox = Activator.CreateInstance(componentDataType);
                object[] args = new object[] { netIdArg, dataBox };
                bool found = tryGetMethod.Invoke(null, args) is bool ok && ok;
                if (!found)
                {
                    status = "Puzzle component missing for board " + boardNetId + ".";
                    return false;
                }

                componentData = args[1] ?? dataBox;
                status = "Puzzle component ready.";
                return componentData != null;
            }
            catch (Exception ex)
            {
                status = "Puzzle component exception: " + ex.Message;
                return false;
            }
        }

        private bool RefreshPuzzlePieces(bool notify)
        {
            this.PuzzleLog("Refresh pieces requested. boardNetId=" + this.puzzleBoardNetId);
            if (this.puzzleBoardNetId == 0U)
            {
                this.puzzleStatus = "Capture a puzzle board first.";
                this.PuzzleLog(this.puzzleStatus);
                return false;
            }

            try
            {
                object puzzleSystem = this.GetJigsawPuzzleSystemInstance();
                if (puzzleSystem == null)
                {
                    if (this.RefreshPuzzlePiecesViaAuraMono())
                    {
                        return true;
                    }

                    this.puzzleStatus = "JigsawPuzzleSystem unavailable. Join/open the puzzle once, then refresh.";
                    this.PuzzleLog(this.puzzleStatus);
                    return false;
                }

                Type systemType = puzzleSystem.GetType();
                MethodInfo getBagMethod = this.GetMethodQuiet(
                    systemType,
                    "GetAllBagPieceItem",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    new Type[] { typeof(uint) });
                MethodInfo getDraftMethod = this.GetMethodQuiet(
                    systemType,
                    "GetAllDraftPieceItem",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    new Type[] { typeof(uint) });
                if (getBagMethod == null || getDraftMethod == null)
                {
                    this.puzzleStatus = "Puzzle piece methods unavailable.";
                    this.PuzzleLog(this.puzzleStatus);
                    return false;
                }

                this.puzzlePieces.Clear();
                this.AddPuzzlePiecesFromCollection(getBagMethod.Invoke(puzzleSystem, new object[] { this.puzzleBoardNetId }));
                this.AddPuzzlePiecesFromCollection(getDraftMethod.Invoke(puzzleSystem, new object[] { this.puzzleBoardNetId }));
                this.puzzlePieces.Sort((a, b) => a.FirstIndex != b.FirstIndex ? a.FirstIndex.CompareTo(b.FirstIndex) : a.SortOrder.CompareTo(b.SortOrder));

                this.UpdatePuzzleTargetSlots();
                this.puzzleStatus = "Pieces ready: " + this.puzzlePieces.Count + ".";
                this.PuzzleLog(this.puzzleStatus + " " + string.Join(", ", this.puzzlePieces.Take(16).Select(p => p.PieceNetId + "@" + p.FirstIndex).ToArray()));
                return this.puzzlePieces.Count > 0 || !notify;
            }
            catch (Exception ex)
            {
                this.puzzleStatus = "Refresh pieces exception: " + ex.Message;
                this.PuzzleLog(this.puzzleStatus);
                return false;
            }
        }

        private bool RefreshPuzzlePiecesViaAuraMono()
        {
            try
            {
                if (this.puzzleBoardNetId == 0U)
                {
                    return false;
                }

                IntPtr puzzleSystemObj = this.GetJigsawPuzzleSystemAuraMonoInstance();
                if (puzzleSystemObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    this.PuzzleLog("Aura pieces unavailable: JigsawPuzzleSystem instance missing.");
                    return false;
                }

                IntPtr systemClass = auraMonoObjectGetClass(puzzleSystemObj);
                IntPtr getBagMethod = this.FindAuraMonoMethodOnHierarchy(systemClass, "GetAllBagPieceItem", 1);
                IntPtr getDraftMethod = this.FindAuraMonoMethodOnHierarchy(systemClass, "GetAllDraftPieceItem", 1);
                if (getBagMethod == IntPtr.Zero || getDraftMethod == IntPtr.Zero)
                {
                    this.PuzzleLog("Aura pieces unavailable: methods bag/draft=0x" + getBagMethod.ToInt64().ToString("X") + "/0x" + getDraftMethod.ToInt64().ToString("X"));
                    return false;
                }

                this.puzzlePieces.Clear();
                unsafe
                {
                    uint boardNetId = this.puzzleBoardNetId;
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&boardNetId);
                    IntPtr exc = IntPtr.Zero;
                    IntPtr bagObj = auraMonoRuntimeInvoke(getBagMethod, puzzleSystemObj, (IntPtr)args, ref exc);
                    if (exc == IntPtr.Zero && bagObj != IntPtr.Zero)
                    {
                        this.AddPuzzlePiecesFromAuraMonoCollection(bagObj);
                    }

                    exc = IntPtr.Zero;
                    IntPtr draftObj = auraMonoRuntimeInvoke(getDraftMethod, puzzleSystemObj, (IntPtr)args, ref exc);
                    if (exc == IntPtr.Zero && draftObj != IntPtr.Zero)
                    {
                        this.AddPuzzlePiecesFromAuraMonoCollection(draftObj);
                    }
                }

                this.puzzlePieces.Sort((a, b) => a.FirstIndex != b.FirstIndex ? a.FirstIndex.CompareTo(b.FirstIndex) : a.SortOrder.CompareTo(b.SortOrder));
                this.UpdatePuzzleTargetSlots();
                this.puzzleStatus = "Pieces ready: " + this.puzzlePieces.Count + ".";
                this.PuzzleLog("Aura " + this.puzzleStatus + " " + string.Join(", ", this.puzzlePieces.Take(16).Select(p => p.PieceNetId + "@" + p.FirstIndex).ToArray()));
                return this.puzzlePieces.Count > 0;
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Aura refresh pieces exception: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private bool RefreshPuzzlePiecesFromAuraMonoEntityScan()
        {
            try
            {
                if (!PuzzleAllowBroadAuraEntityScan)
                {
                    this.PuzzleLog("Aura entity pieces skipped: broad entity scan disabled.");
                    return false;
                }

                if (this.puzzleBoardNetId == 0U)
                {
                    return false;
                }

                if (!this.TryEnumerateAuraMonoLoadedEntityObjects(out List<IntPtr> entities, out string status) || entities.Count <= 0)
                {
                    this.PuzzleLog("Aura entity pieces unavailable: " + status);
                    return false;
                }

                this.puzzlePieces.Clear();
                int inspected = 0;
                int pieceComponents = 0;
                int belongMatches = 0;
                for (int i = 0; i < entities.Count && i < 4096; i++)
                {
                    IntPtr entityObj = entities[i];
                    if (entityObj == IntPtr.Zero || !this.TryGetAuraMonoEntityNetId(entityObj, out uint entityNetId) || entityNetId == 0U)
                    {
                        continue;
                    }

                    inspected++;
                    if (!this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr componentsObj, "GetAllComponents") || componentsObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    List<IntPtr> components = new List<IntPtr>(32);
                    if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components) || components.Count <= 0)
                    {
                        continue;
                    }

                    IntPtr pieceComponentObj = IntPtr.Zero;
                    uint belongBoardNetId = 0U;
                    for (int c = 0; c < components.Count && c < 128; c++)
                    {
                        IntPtr componentObj = components[c];
                        if (componentObj == IntPtr.Zero || auraMonoObjectGetClass == null)
                        {
                            continue;
                        }

                        string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(componentObj));
                        if (this.LooksLikePuzzlePieceBelongComponentName(className))
                        {
                            this.TryGetPuzzleBelongBoardNetId(componentObj, out belongBoardNetId);
                            continue;
                        }

                        if (this.LooksLikePuzzlePieceComponentName(className))
                        {
                            pieceComponentObj = componentObj;
                            continue;
                        }

                    }

                    if (pieceComponentObj == IntPtr.Zero || belongBoardNetId != this.puzzleBoardNetId)
                    {
                        continue;
                    }

                    pieceComponents++;
                    belongMatches++;
                    PuzzlePieceContext piece = new PuzzlePieceContext
                    {
                        PieceNetId = entityNetId,
                        IsInBag = true,
                        IsInDraft = false,
                        OccupiedPlayerNetId = 0U,
                        SortOrder = 0,
                        FirstIndex = -1,
                        MaskCount = 0
                    };

                    this.TryGetPuzzlePieceIndexMono(pieceComponentObj, out piece.SortOrder);
                    if (this.TryGetMonoObjectMember(pieceComponentObj, "Mask", out IntPtr maskObj) && maskObj != IntPtr.Zero)
                    {
                        piece.FirstIndex = this.GetPuzzleMaskFirstIndexMono(maskObj);
                        piece.MaskCount = this.GetPuzzleMaskCountMono(maskObj);
                    }

                    if (!this.puzzlePieces.Any(existing => existing.PieceNetId == piece.PieceNetId))
                    {
                        this.puzzlePieces.Add(piece);
                    }
                }

                this.puzzlePieces.Sort((a, b) => a.FirstIndex != b.FirstIndex ? a.FirstIndex.CompareTo(b.FirstIndex) : a.SortOrder.CompareTo(b.SortOrder));
                this.UpdatePuzzleTargetSlots();
                this.puzzleStatus = "Pieces ready: " + this.puzzlePieces.Count + ".";
                this.PuzzleLog("Aura entity pieces: inspected=" + inspected + " pieceComponents=" + pieceComponents + " belongMatches=" + belongMatches + " " + string.Join(", ", this.puzzlePieces.Take(16).Select(p => p.PieceNetId + "@" + p.FirstIndex).ToArray()));
                return this.puzzlePieces.Count > 0;
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Aura entity pieces exception: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private bool TryGetPuzzlePieceIndexMono(IntPtr pieceComponentObj, out int index)
        {
            index = 0;
            if (pieceComponentObj == IntPtr.Zero)
            {
                return false;
            }

            foreach (string memberName in new[] { "PieceIdx", "pieceIdx", "_pieceIdx" })
            {
                if (this.TryGetMonoObjectMember(pieceComponentObj, memberName, out IntPtr boxed) && boxed != IntPtr.Zero)
                {
                    ulong value = this.TryReadMonoUnsignedIntegral(boxed);
                    if (value <= int.MaxValue)
                    {
                        index = (int)value;
                        return true;
                    }
                }
            }

            return false;
        }

        private IntPtr GetJigsawPuzzleSystemAuraMonoInstance()
        {
            if (this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.JigsawPuzzle.JigsawPuzzleSystem", out IntPtr moduleObj) && moduleObj != IntPtr.Zero)
            {
                return moduleObj;
            }

            IntPtr classPtr = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.JigsawPuzzle.JigsawPuzzleSystem");
            if (classPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return IntPtr.Zero;
            }

            foreach (string getterName in new[] { "get_Instance", "GetInstance" })
            {
                IntPtr getter = this.FindAuraMonoMethodOnHierarchy(classPtr, getterName, 0);
                if (getter == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr value = auraMonoRuntimeInvoke(getter, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && value != IntPtr.Zero)
                {
                    return value;
                }
            }

            foreach (string fieldName in new[] { "Instance", "instance", "_instance" })
            {
                if (this.TryGetAuraMonoStaticObjectField(classPtr, fieldName, out IntPtr value) && value != IntPtr.Zero)
                {
                    return value;
                }
            }

            return IntPtr.Zero;
        }

        private void AddPuzzlePiecesFromAuraMonoCollection(IntPtr collectionObj)
        {
            List<IntPtr> items = new List<IntPtr>(128);
            if (!this.TryEnumerateAuraMonoCollectionItems(collectionObj, items) || items.Count <= 0)
            {
                return;
            }

            for (int i = 0; i < items.Count && i < 512; i++)
            {
                IntPtr itemObj = items[i];
                if (itemObj == IntPtr.Zero)
                {
                    continue;
                }

                PuzzlePieceContext piece = new PuzzlePieceContext();
                this.TryGetMonoUInt32Member(itemObj, "pieceNetId", out piece.PieceNetId);
                this.TryGetMonoBoolMember(itemObj, "isInBag", out piece.IsInBag);
                this.TryGetMonoBoolMember(itemObj, "isInDraft", out piece.IsInDraft);
                this.TryGetMonoUInt32Member(itemObj, "occupiedPlayerNetId", out piece.OccupiedPlayerNetId);
                this.TryGetMonoInt32Member(itemObj, "sortOrder", out piece.SortOrder);
                if (this.TryGetMonoObjectMember(itemObj, "mask", out IntPtr maskObj) && maskObj != IntPtr.Zero)
                {
                    piece.FirstIndex = this.GetPuzzleMaskFirstIndexMono(maskObj);
                    piece.MaskCount = this.GetPuzzleMaskCountMono(maskObj);
                }
                else
                {
                    piece.FirstIndex = -1;
                    piece.MaskCount = 0;
                }

                if (piece.PieceNetId != 0U && !this.puzzlePieces.Any(existing => existing.PieceNetId == piece.PieceNetId))
                {
                    this.puzzlePieces.Add(piece);
                }
            }
        }

        private int GetPuzzleMaskFirstIndexMono(IntPtr maskObj)
        {
            if (maskObj == IntPtr.Zero)
            {
                return -1;
            }

            ulong mask1 = this.ReadPuzzleMaskUlongMono(maskObj, "Mask1");
            if (mask1 == 0UL)
            {
                mask1 = this.ReadPuzzleMaskUlongMono(maskObj, "mask1");
            }
            if (mask1 != 0UL)
            {
                for (int i = 0; i < 64; i++)
                {
                    if ((mask1 & (1UL << i)) != 0UL)
                    {
                        return i;
                    }
                }
            }

            ulong mask2 = this.ReadPuzzleMaskUlongMono(maskObj, "Mask2");
            if (mask2 == 0UL)
            {
                mask2 = this.ReadPuzzleMaskUlongMono(maskObj, "mask2");
            }
            if (mask2 != 0UL)
            {
                for (int i = 0; i < 64; i++)
                {
                    if ((mask2 & (1UL << i)) != 0UL)
                    {
                        return 64 + i;
                    }
                }
            }

            return -1;
        }

        private int GetPuzzleMaskCountMono(IntPtr maskObj)
        {
            if (maskObj == IntPtr.Zero)
            {
                return 0;
            }

            return this.CountPuzzleBits(this.ReadPuzzleMaskUlongMono(maskObj, "Mask1") | this.ReadPuzzleMaskUlongMono(maskObj, "mask1"))
                + this.CountPuzzleBits(this.ReadPuzzleMaskUlongMono(maskObj, "Mask2") | this.ReadPuzzleMaskUlongMono(maskObj, "mask2"));
        }

        private ulong ReadPuzzleMaskUlongMono(IntPtr maskObj, string memberName)
        {
            return this.TryGetMonoUInt64Member(maskObj, memberName, out ulong value) ? value : 0UL;
        }

        private object GetJigsawPuzzleSystemInstance()
        {
            Type systemType = this.FindLoadedType("XDTGameSystem.GameplaySystem.JigsawPuzzle.JigsawPuzzleSystem", "JigsawPuzzleSystem");
            if (systemType == null)
            {
                return null;
            }

            Type current = systemType;
            while (current != null)
            {
                PropertyInfo instanceProperty = current.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                if (instanceProperty != null)
                {
                    try
                    {
                        object value = instanceProperty.GetValue(null, null);
                        if (value != null)
                        {
                            return value;
                        }
                    }
                    catch
                    {
                    }
                }
                current = current.BaseType;
            }

            return null;
        }

        private void LogPuzzleResolverProbeOnce()
        {
            if (this.puzzleResolverProbeLogged)
            {
                return;
            }

            this.puzzleResolverProbeLogged = true;
            try
            {
                this.LogPuzzleManagedTypeScan();
                Type dataCenterType = this.FindLoadedType("XDTDataAndProtocol.ComponentsData.DataCenter", "DataCenter");
                Type puzzleComponentType = this.FindLoadedType("XDTDataAndProtocol.ComponentsData.JigsawPuzzleComponentData", "JigsawPuzzleComponentData");
                Type netIdType = this.FindLoadedType("EcsClient.XDT.Scene.Shared.Data.SharedData.NetId", "XDT.Scene.Shared.NetId", "NetId");
                Type puzzleSystemType = this.FindLoadedType("XDTGameSystem.GameplaySystem.JigsawPuzzle.JigsawPuzzleSystem", "JigsawPuzzleSystem");
                Type protocolType = this.FindPuzzleProtocolType();
                Type levelObjectManagerType = this.FindLevelObjectManagerRuntimeType();
                object puzzleSystem = this.GetJigsawPuzzleSystemInstance();
                IntPtr auraProtocolClass = this.FindAuraMonoPuzzleProtocolClass();
                IntPtr auraJoin = auraProtocolClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(auraProtocolClass, "JoinJigsawPuzzle", 1) : IntPtr.Zero;
                IntPtr auraLock = auraProtocolClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(auraProtocolClass, "LockJigsawPuzzlePiece", 2) : IntPtr.Zero;
                IntPtr auraMove = auraProtocolClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(auraProtocolClass, "MoveJigsawPuzzlePiecePos", 3) : IntPtr.Zero;
                IntPtr auraBingo = auraProtocolClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(auraProtocolClass, "SetJigsawPuzzlePieceBingo", 1) : IntPtr.Zero;
                IntPtr auraBoardComponent = this.FindAuraMonoClassByFullName("EcsClient.XDT.Scene.Shared.Modules.MiniGame.JigsawPuzzleBoardComponent");
                IntPtr auraPieceComponent = this.FindAuraMonoClassByFullName("EcsClient.XDT.Scene.Shared.Modules.MiniGame.JigsawPuzzlePieceComponent");
                IntPtr auraJoinCommand = this.FindAuraMonoClassByFullName("EcsClient.XDT.Scene.Shared.Modules.MiniGame.JigsawPuzzleJoinNetworkCommand");
                IntPtr auraMoveCommand = this.FindAuraMonoClassByFullName("EcsClient.XDT.Scene.Shared.Modules.MiniGame.JigsawPuzzleMovePieceNetworkCommand");
                IntPtr auraBingoCommand = this.FindAuraMonoClassByFullName("EcsClient.XDT.Scene.Shared.Modules.MiniGame.JigsawPuzzlePieceBingoNetworkCommand");

                bool hasBag = puzzleSystemType != null && this.GetMethodQuiet(
                    puzzleSystemType,
                    "GetAllBagPieceItem",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    new Type[] { typeof(uint) }) != null;
                bool hasDraft = puzzleSystemType != null && this.GetMethodQuiet(
                    puzzleSystemType,
                    "GetAllDraftPieceItem",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    new Type[] { typeof(uint) }) != null;
                bool hasJoin = protocolType != null && this.FindPuzzleProtocolMethod(protocolType, "JoinJigsawPuzzle", typeof(uint)) != null;
                bool hasLock = protocolType != null && this.FindPuzzleProtocolMethod(protocolType, "LockJigsawPuzzlePiece", typeof(uint), typeof(Action)) != null;
                bool hasMove = protocolType != null && this.FindPuzzleProtocolMethod(protocolType, "MoveJigsawPuzzlePiecePos", typeof(uint), typeof(short), typeof(short)) != null;
                bool hasBingo = protocolType != null && this.FindPuzzleProtocolMethod(protocolType, "SetJigsawPuzzlePieceBingo", typeof(uint)) != null;

                this.PuzzleLog("Resolver probe: DataCenter=" + (dataCenterType != null)
                    + " JigsawComponent=" + (puzzleComponentType != null)
                    + " NetId=" + (netIdType != null)
                    + " LevelObjectManager=" + (levelObjectManagerType != null)
                    + " JigsawSystemType=" + (puzzleSystemType != null)
                    + " JigsawSystemInstance=" + (puzzleSystem != null)
                    + " piecesMethods=" + hasBag + "/" + hasDraft
                    + " Protocol=" + (protocolType != null)
                    + " methods join/lock/move/bingo=" + hasJoin + "/" + hasLock + "/" + hasMove + "/" + hasBingo
                    + " AuraProtocol=0x" + auraProtocolClass.ToInt64().ToString("X")
                    + " aura join/lock/move/bingo=0x" + auraJoin.ToInt64().ToString("X") + "/0x" + auraLock.ToInt64().ToString("X") + "/0x" + auraMove.ToInt64().ToString("X") + "/0x" + auraBingo.ToInt64().ToString("X")
                    + " aura ecs board/piece/cmd=0x" + auraBoardComponent.ToInt64().ToString("X") + "/0x" + auraPieceComponent.ToInt64().ToString("X") + "/0x" + auraJoinCommand.ToInt64().ToString("X") + "/0x" + auraMoveCommand.ToInt64().ToString("X") + "/0x" + auraBingoCommand.ToInt64().ToString("X"));
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Resolver probe exception: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void LogPuzzleManagedTypeScan()
        {
            try
            {
                int assemblyCount = 0;
                int inspectedTypes = 0;
                int matchCount = 0;
                List<string> firstMatches = new List<string>(32);
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly == null)
                    {
                        continue;
                    }

                    assemblyCount++;
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types.Where(t => t != null).ToArray();
                    }
                    catch
                    {
                        continue;
                    }

                    string assemblyName = assembly.GetName().Name;
                    for (int i = 0; i < types.Length; i++)
                    {
                        Type type = types[i];
                        if (type == null)
                        {
                            continue;
                        }

                        inspectedTypes++;
                        string fullName = type.FullName ?? type.Name;
                        if (fullName.IndexOf("Jigsaw", StringComparison.OrdinalIgnoreCase) < 0
                            && fullName.IndexOf("Puzzle", StringComparison.OrdinalIgnoreCase) < 0
                            && fullName.IndexOf("MiniGame", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        matchCount++;
                        if (firstMatches.Count < 24)
                        {
                            firstMatches.Add(assemblyName + ":" + fullName);
                        }
                    }
                }

                this.PuzzleLog("Managed type scan: assemblies=" + assemblyCount
                    + " types=" + inspectedTypes
                    + " puzzleMatches=" + matchCount
                    + " first=[" + string.Join("; ", firstMatches.ToArray()) + "]");
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Managed type scan exception: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void AddPuzzlePiecesFromCollection(object collection)
        {
            if (!(collection is IEnumerable enumerable))
            {
                return;
            }

            foreach (object item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }

                PuzzlePieceContext piece = new PuzzlePieceContext();
                this.TryReadManagedUInt32Member(item, "pieceNetId", out piece.PieceNetId);
                this.TryReadManagedBoolMember(item, "isInBag", out piece.IsInBag);
                this.TryReadManagedBoolMember(item, "isInDraft", out piece.IsInDraft);
                this.TryReadManagedUInt32Member(item, "occupiedPlayerNetId", out piece.OccupiedPlayerNetId);
                this.TryReadManagedInt32Member(item, "sortOrder", out piece.SortOrder);

                object mask = this.TryGetManagedMemberValue(item, "mask");
                piece.FirstIndex = this.GetPuzzleMaskFirstIndex(mask);
                piece.MaskCount = this.GetPuzzleMaskCount(mask);

                if (piece.PieceNetId != 0U && !this.puzzlePieces.Any(existing => existing.PieceNetId == piece.PieceNetId))
                {
                    this.puzzlePieces.Add(piece);
                }
            }
        }

        private int GetPuzzleMaskFirstIndex(object mask)
        {
            if (mask == null)
            {
                return -1;
            }

            try
            {
                MethodInfo trailing = mask.GetType().GetMethod("TrailingZeroCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (trailing != null)
                {
                    return Convert.ToInt32(trailing.Invoke(mask, null));
                }
            }
            catch
            {
            }

            ulong mask1 = this.ReadPuzzleMaskUlong(mask, "Mask1");
            if (mask1 != 0UL)
            {
                for (int i = 0; i < 64; i++)
                {
                    if ((mask1 & (1UL << i)) != 0UL)
                    {
                        return i;
                    }
                }
            }

            ulong mask2 = this.ReadPuzzleMaskUlong(mask, "Mask2");
            if (mask2 != 0UL)
            {
                for (int i = 0; i < 64; i++)
                {
                    if ((mask2 & (1UL << i)) != 0UL)
                    {
                        return 64 + i;
                    }
                }
            }

            return -1;
        }

        private int GetPuzzleMaskCount(object mask)
        {
            if (mask == null)
            {
                return 0;
            }

            try
            {
                MethodInfo count = mask.GetType().GetMethod("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (count != null)
                {
                    return Convert.ToInt32(count.Invoke(mask, null));
                }
            }
            catch
            {
            }

            return this.CountPuzzleBits(this.ReadPuzzleMaskUlong(mask, "Mask1")) + this.CountPuzzleBits(this.ReadPuzzleMaskUlong(mask, "Mask2"));
        }

        private ulong ReadPuzzleMaskUlong(object mask, string fieldName)
        {
            try
            {
                FieldInfo field = mask.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return Convert.ToUInt64(field.GetValue(mask));
                }
            }
            catch
            {
            }

            return 0UL;
        }

        private int CountPuzzleBits(ulong value)
        {
            int count = 0;
            while (value != 0UL)
            {
                value &= value - 1UL;
                count++;
            }
            return count;
        }

        private void UpdatePuzzleTargetSlots()
        {
            int row;
            int col;
            if (!this.TryGetPuzzleBoardRowCol(out row, out col))
            {
                return;
            }

            for (int i = 0; i < this.puzzlePieces.Count; i++)
            {
                PuzzlePieceContext piece = this.puzzlePieces[i];
                int index = Mathf.Max(0, piece.FirstIndex);
                int r = index / col;
                int c = index % col;
                piece.TargetX = (short)Mathf.RoundToInt(-400f + (c + 0.5f) * (800f / col));
                piece.TargetY = (short)Mathf.RoundToInt(400f - (r + 0.5f) * (800f / row));
            }
        }

        private bool TryGetPuzzleBoardRowCol(out int row, out int col)
        {
            row = 0;
            col = 0;
            if (this.puzzleStaticId > 0)
            {
                try
                {
                    Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                    object table = tableDataType != null ? tableDataType.GetField("TableJigsawpuzzles", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) : null;
                    if (table is IDictionary dictionary && dictionary.Contains(this.puzzleStaticId))
                    {
                        object data = dictionary[this.puzzleStaticId];
                        this.TryReadManagedInt32Member(data, "row", out row);
                        this.TryReadManagedInt32Member(data, "column", out col);
                    }
                }
                catch
                {
                }
            }

            if (row > 0 && col > 0)
            {
                return true;
            }

            int maxIndex = this.puzzlePieces.Count > 0 ? this.puzzlePieces.Max(p => p.FirstIndex) : -1;
            int total = maxIndex + 1;
            int size = Mathf.RoundToInt(Mathf.Sqrt(Mathf.Max(1, total)));
            if (size > 0 && size * size >= total)
            {
                row = size;
                col = size;
                return true;
            }

            return false;
        }

        private void StartPuzzleSolve()
        {
            this.PuzzleLog("Start solve requested. boardNetId=" + this.puzzleBoardNetId + " pieces=" + this.puzzlePieces.Count + ".");
            this.puzzleSentCount = 0;
            uint previousBoardNetId = this.puzzleBoardNetId;
            bool capturedActivePuzzle = this.TryCapturePuzzleFromCurrentTarget();
            if (capturedActivePuzzle)
            {
                if (previousBoardNetId != 0U && previousBoardNetId != this.puzzleBoardNetId)
                {
                    this.PuzzleLog("Puzzle board refreshed: " + previousBoardNetId + " -> " + this.puzzleBoardNetId + ".");
                }
            }
            else if (this.puzzleBoardNetId == 0U)
            {
                this.PuzzleLog("Start solve capture failed: " + this.puzzleStatus);
                return;
            }
            else
            {
                this.PuzzleLog("Active puzzle refresh failed; using cached board " + this.puzzleBoardNetId + ".");
            }

            List<PuzzlePieceContext> capturedPieces = this.puzzlePieces.Count > 0 ? new List<PuzzlePieceContext>(this.puzzlePieces) : null;
            if (!this.RefreshPuzzlePieces(false) || this.puzzlePieces.Count <= 0)
            {
                if (capturedPieces != null && capturedPieces.Count > 0)
                {
                    this.puzzlePieces.Clear();
                    this.puzzlePieces.AddRange(capturedPieces);
                    this.UpdatePuzzleTargetSlots();
                    this.PuzzleLog("Using captured widget pieces before join: " + this.puzzlePieces.Count + ".");
                }
                else
                {
                    this.PuzzleLog("Pieces unavailable before join; will join/open puzzle and retry inside solve pass.");
                }
            }

            if (!this.EnsurePuzzleProtocolMethods())
            {
                this.PuzzleLog("Start solve protocol unavailable: " + this.puzzleStatus);
                return;
            }

            this.puzzleSolveRunning = true;
            this.puzzleSolveCoroutine = ModCoroutines.Start(this.PuzzleSolveRoutine());
            this.PuzzleLog("Puzzle solve coroutine started. pieces=" + this.puzzlePieces.Count);
        }

        private void UpdatePuzzleAutomation()
        {
            if (!this.puzzleAutoEnabled || this.puzzleSolveRunning)
            {
                return;
            }

            if (Time.unscaledTime < this.puzzleNextAutoAttemptAt)
            {
                return;
            }

            this.puzzleNextAutoAttemptAt = Time.unscaledTime + PuzzleAutoRetrySeconds;
            this.PuzzleLog("Auto tick. boardNetId=" + this.puzzleBoardNetId + " pieces=" + this.puzzlePieces.Count + " status=" + this.puzzleStatus);
            this.StartPuzzleSolve();
        }

        private void StopPuzzleSolve(string reason)
        {
            this.puzzleSolveRunning = false;
            if (this.puzzleSolveCoroutine != null)
            {
                try { ModCoroutines.Stop(this.puzzleSolveCoroutine); } catch { }
                this.puzzleSolveCoroutine = null;
            }
            this.puzzleStatus = string.IsNullOrWhiteSpace(reason) ? "Stopped." : reason;
            this.PuzzleLog("Puzzle solve stopped: " + this.puzzleStatus);
        }

        private System.Collections.IEnumerator PuzzleSolveRoutine()
        {
            List<PuzzlePieceContext> capturedPieces = this.puzzlePieces.Count > 0 ? new List<PuzzlePieceContext>(this.puzzlePieces) : null;
            if (this.puzzlePieces.Count <= 0)
            {
                this.TryInvokePuzzleJoin();
                yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, PuzzleNetworkSettleSeconds));
            }
            else
            {
                this.PuzzleLog("Join skipped: active puzzle already has readable pieces.");
            }

            this.RefreshPuzzlePieces(false);
            if (this.puzzlePieces.Count <= 0 && capturedPieces != null && capturedPieces.Count > 0)
            {
                this.puzzlePieces.Clear();
                this.puzzlePieces.AddRange(capturedPieces);
                this.UpdatePuzzleTargetSlots();
                this.PuzzleLog("Using captured widget pieces after join: " + this.puzzlePieces.Count + ".");
            }

            if (this.puzzlePieces.Count <= 0)
            {
                this.puzzleSolveRunning = false;
                this.puzzleSolveCoroutine = null;
                this.puzzleStatus = "Joined puzzle, but no pieces were readable yet.";
                this.PuzzleLog(this.puzzleStatus);
                yield break;
            }

            int pass = 0;
            int previousRemaining = this.puzzlePieces.Count;
            while (this.puzzleSolveRunning && this.puzzlePieces.Count > 0 && pass < PuzzleMaxSolvePasses)
            {
                pass++;
                List<PuzzlePieceContext> passPieces = new List<PuzzlePieceContext>(this.puzzlePieces);
                this.PuzzleLog("Puzzle solve pass " + pass + "/" + PuzzleMaxSolvePasses + " pieces=" + passPieces.Count + ".");

                for (int i = 0; i < passPieces.Count && this.puzzleSolveRunning; i++)
                {
                    PuzzlePieceContext piece = passPieces[i];
                    if (piece == null || piece.PieceNetId == 0U || piece.OccupiedPlayerNetId != 0U)
                    {
                        continue;
                    }

                    this.puzzleStatus = "Solving piece " + (i + 1) + "/" + passPieces.Count + " netId=" + piece.PieceNetId + ".";
                    this.PuzzleLog(this.puzzleStatus + " index=" + piece.FirstIndex + " pos=(" + piece.TargetX + "," + piece.TargetY + ")");

                    this.TryInvokePuzzleLock(piece.PieceNetId);
                    yield return new WaitForSecondsRealtime(Mathf.Max(0.03f, PuzzleNetworkSettleSeconds * 0.25f));

                    if (piece.IsInBag || piece.MaskCount == 1)
                    {
                        if (this.TryInvokePuzzleBingo(piece.PieceNetId))
                        {
                            this.puzzleSentCount++;
                        }

                        yield return new WaitForSecondsRealtime(Mathf.Max(0.04f, PuzzleNetworkSettleSeconds * 0.35f));
                        continue;
                    }

                    this.TryInvokePuzzleMove(piece.PieceNetId, piece.TargetX, piece.TargetY);
                    yield return new WaitForSecondsRealtime(Mathf.Max(0.06f, PuzzleNetworkSettleSeconds * 0.5f));

                    if (this.TryInvokePuzzleBingo(piece.PieceNetId))
                    {
                        this.puzzleSentCount++;
                    }

                    yield return new WaitForSecondsRealtime(PuzzleNetworkSettleSeconds);
                }

                yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, PuzzleNetworkSettleSeconds * 2f));
                this.RefreshPuzzlePieces(false);
                int remaining = this.puzzlePieces.Count;
                this.PuzzleLog("Puzzle solve pass " + pass + " refresh: remaining=" + remaining + " previous=" + previousRemaining + ".");
                if (remaining <= 0)
                {
                    break;
                }

                if (remaining >= previousRemaining)
                {
                    this.PuzzleLog("Puzzle solve made no new progress; leaving remaining pieces for next retry.");
                    break;
                }

                previousRemaining = remaining;
            }

            this.puzzleSolveRunning = false;
            this.puzzleSolveCoroutine = null;
            this.puzzleStatus = "Puzzle solve finished. Sent " + this.puzzleSentCount + " piece action(s), remaining=" + this.puzzlePieces.Count + ".";
            this.PuzzleLog(this.puzzleStatus);
            if (this.puzzlePieces.Count <= 0 && this.puzzleSentCount > 0)
            {
                this.puzzleAutoEnabled = false;
                this.PuzzleLog("Auto Puzzle disabled after puzzle cleared.");
                this.puzzleBoardNetId = 0U;
                this.puzzleStaticId = 0;
            }
            else if (this.puzzleAutoEnabled && this.puzzlePieces.Count > 0)
            {
                this.puzzleNextAutoAttemptAt = Time.unscaledTime + PuzzleAutoRetrySeconds;
                this.PuzzleLog("Auto Puzzle will retry remaining pieces in " + PuzzleAutoRetrySeconds.ToString("F1") + "s.");
            }
        }

        private bool EnsurePuzzleProtocolMethods()
        {
            if (this.puzzleJoinMethod != null && this.puzzleLockMethod != null && this.puzzleMoveMethod != null && this.puzzleBingoMethod != null)
            {
                return true;
            }

            Type protocolType = this.FindPuzzleProtocolType();
            if (protocolType == null)
            {
                if (this.EnsureAuraMonoPuzzleProtocolMethods(out string auraStatus))
                {
                    this.PuzzleLog(auraStatus);
                    return true;
                }

                this.puzzleStatus = "JigsawPuzzleProtocolManager unavailable. " + auraStatus;
                this.PuzzleLog(this.puzzleStatus);
                return false;
            }

            this.puzzleJoinMethod = this.FindPuzzleProtocolMethod(protocolType, "JoinJigsawPuzzle", typeof(uint));
            this.puzzleLeaveMethod = this.FindPuzzleProtocolMethod(protocolType, "LeaveJigsawPuzzle", typeof(uint));
            this.puzzleLockMethod = this.FindPuzzleProtocolMethod(protocolType, "LockJigsawPuzzlePiece", typeof(uint), typeof(Action));
            this.puzzleUnlockMethod = this.FindPuzzleProtocolMethod(protocolType, "UnlockJigsawPuzzlePiece", typeof(uint));
            this.puzzleMoveMethod = this.FindPuzzleProtocolMethod(protocolType, "MoveJigsawPuzzlePiecePos", typeof(uint), typeof(short), typeof(short));
            this.puzzleBingoMethod = this.FindPuzzleProtocolMethod(protocolType, "SetJigsawPuzzlePieceBingo", typeof(uint));

            if (this.puzzleJoinMethod == null || this.puzzleLockMethod == null || this.puzzleMoveMethod == null || this.puzzleBingoMethod == null)
            {
                if (this.EnsureAuraMonoPuzzleProtocolMethods(out string auraStatus))
                {
                    this.PuzzleLog("Managed puzzle methods incomplete; using AuraMono. " + auraStatus);
                    return true;
                }

                this.puzzleStatus = "Puzzle protocol methods missing.";
                this.PuzzleLog(this.puzzleStatus + " join=" + (this.puzzleJoinMethod != null) + " lock=" + (this.puzzleLockMethod != null) + " move=" + (this.puzzleMoveMethod != null) + " bingo=" + (this.puzzleBingoMethod != null));
                return false;
            }

            this.PuzzleLog("Puzzle protocol methods ready.");
            return true;
        }

        private Type FindPuzzleProtocolType()
        {
            return this.FindLoadedType(
                "XDTDataAndProtocol.ProtocolService.JigsawPuzzle.JigsawPuzzleProtocolManager",
                "XDTDataAndProtocol.ProtocolService.MiniGame.JigsawPuzzleProtocolManager");
        }

        private IntPtr FindAuraMonoPuzzleProtocolClass()
        {
            IntPtr classPtr = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.JigsawPuzzle.JigsawPuzzleProtocolManager");
            if (classPtr == IntPtr.Zero)
            {
                classPtr = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.MiniGame.JigsawPuzzleProtocolManager");
            }
            return classPtr;
        }

        private bool EnsureAuraMonoPuzzleProtocolMethods(out string status)
        {
            status = "AuraMono puzzle protocol unavailable.";
            if (this.puzzleAuraJoinMethod != IntPtr.Zero && this.puzzleAuraLockMethod != IntPtr.Zero && this.puzzleAuraMoveMethod != IntPtr.Zero && this.puzzleAuraBingoMethod != IntPtr.Zero)
            {
                status = "AuraMono puzzle protocol ready.";
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    status = "AuraMono API not ready.";
                    return false;
                }

                IntPtr protocolClass = this.FindAuraMonoPuzzleProtocolClass();
                if (protocolClass == IntPtr.Zero)
                {
                    status = "AuraMono JigsawPuzzleProtocolManager class unavailable.";
                    return false;
                }

                this.puzzleAuraJoinMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "JoinJigsawPuzzle", 1);
                this.puzzleAuraLeaveMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "LeaveJigsawPuzzle", 1);
                this.puzzleAuraLockMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "LockJigsawPuzzlePiece", 2);
                this.puzzleAuraUnlockMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "UnlockJigsawPuzzlePiece", 1);
                this.puzzleAuraMoveMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "MoveJigsawPuzzlePiecePos", 3);
                this.puzzleAuraBingoMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "SetJigsawPuzzlePieceBingo", 1);

                status = "AuraMono puzzle protocol class=0x" + protocolClass.ToInt64().ToString("X")
                    + " join/lock/move/bingo=0x" + this.puzzleAuraJoinMethod.ToInt64().ToString("X")
                    + "/0x" + this.puzzleAuraLockMethod.ToInt64().ToString("X")
                    + "/0x" + this.puzzleAuraMoveMethod.ToInt64().ToString("X")
                    + "/0x" + this.puzzleAuraBingoMethod.ToInt64().ToString("X");
                return this.puzzleAuraJoinMethod != IntPtr.Zero && this.puzzleAuraLockMethod != IntPtr.Zero && this.puzzleAuraMoveMethod != IntPtr.Zero && this.puzzleAuraBingoMethod != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                status = "AuraMono puzzle protocol exception: " + ex.Message;
                return false;
            }
        }

        private MethodInfo FindPuzzleProtocolMethod(Type protocolType, string methodName, params Type[] parameterTypes)
        {
            if (protocolType == null || string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            return this.GetMethodQuiet(
                protocolType,
                methodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                parameterTypes ?? Type.EmptyTypes);
        }

        private bool TryInvokePuzzleJoin()
        {
            if (this.puzzleBoardNetId == 0U)
            {
                this.puzzleStatus = "Capture a puzzle board first.";
                return false;
            }

            if (this.TryInvokeAuraMonoPuzzleJoin(this.puzzleBoardNetId))
            {
                return true;
            }

            if (!this.EnsurePuzzleProtocolMethods())
            {
                return false;
            }
            if (this.puzzleJoinMethod == null)
            {
                return false;
            }

            try
            {
                this.puzzleJoinMethod.Invoke(null, new object[] { this.puzzleBoardNetId });
                return true;
            }
            catch (Exception ex)
            {
                this.puzzleStatus = "Join puzzle failed: " + ex.Message;
                this.PuzzleLog(this.puzzleStatus);
            }

            return false;
        }

        private bool TryInvokePuzzleLock(uint pieceNetId)
        {
            if (this.TryInvokeAuraMonoPuzzleLock(pieceNetId, true))
            {
                return true;
            }

            if (!this.EnsurePuzzleProtocolMethods())
            {
                return false;
            }
            if (this.puzzleLockMethod == null)
            {
                return false;
            }

            try
            {
                this.puzzleLockMethod.Invoke(null, new object[] { pieceNetId, null });
                return true;
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Lock piece failed " + pieceNetId + ": " + ex.Message);
            }

            return false;
        }

        private bool TryInvokePuzzleMove(uint pieceNetId, short x, short y)
        {
            if (this.TryInvokeAuraMonoPuzzleMove(pieceNetId, x, y))
            {
                return true;
            }

            if (!this.EnsurePuzzleProtocolMethods())
            {
                return false;
            }
            if (this.puzzleMoveMethod == null)
            {
                return false;
            }

            try
            {
                this.puzzleMoveMethod.Invoke(null, new object[] { pieceNetId, x, y });
                return true;
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Move piece failed " + pieceNetId + ": " + ex.Message);
            }

            return false;
        }

        private bool TryInvokePuzzleBingo(uint pieceNetId)
        {
            if (this.TryInvokeAuraMonoPuzzleBingo(pieceNetId))
            {
                return true;
            }

            if (!this.EnsurePuzzleProtocolMethods())
            {
                return false;
            }
            if (this.puzzleBingoMethod == null)
            {
                return false;
            }

            try
            {
                this.puzzleBingoMethod.Invoke(null, new object[] { pieceNetId });
                return true;
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Bingo piece failed " + pieceNetId + ": " + ex.Message);
            }

            return false;
        }

        private unsafe bool TryInvokeAuraMonoPuzzleJoin(uint boardNetId)
        {
            if (!this.EnsureAuraMonoPuzzleProtocolMethods(out string status) || this.puzzleAuraJoinMethod == IntPtr.Zero)
            {
                this.PuzzleLog("Aura join unavailable: " + status);
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&boardNetId);
            auraMonoRuntimeInvoke(this.puzzleAuraJoinMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            bool ok = exc == IntPtr.Zero;
            this.PuzzleLog("Aura join board=" + boardNetId + " ok=" + ok + (ok ? string.Empty : " exc=0x" + exc.ToInt64().ToString("X")));
            return ok;
        }

        private unsafe bool TryInvokeAuraMonoPuzzleLock(uint pieceNetId, bool isLock)
        {
            if (!this.EnsureAuraMonoPuzzleProtocolMethods(out string status) || this.puzzleAuraLockMethod == IntPtr.Zero)
            {
                this.PuzzleLog("Aura lock unavailable: " + status);
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&pieceNetId);
            args[1] = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.puzzleAuraLockMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            bool ok = exc == IntPtr.Zero;
            this.PuzzleLog("Aura lock piece=" + pieceNetId + " ok=" + ok + (ok ? string.Empty : " exc=0x" + exc.ToInt64().ToString("X")));
            return ok;
        }

        private unsafe bool TryInvokeAuraMonoPuzzleMove(uint pieceNetId, short x, short y)
        {
            if (!this.EnsureAuraMonoPuzzleProtocolMethods(out string status) || this.puzzleAuraMoveMethod == IntPtr.Zero)
            {
                this.PuzzleLog("Aura move unavailable: " + status);
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&pieceNetId);
            args[1] = (IntPtr)(&x);
            args[2] = (IntPtr)(&y);
            auraMonoRuntimeInvoke(this.puzzleAuraMoveMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            bool ok = exc == IntPtr.Zero;
            this.PuzzleLog("Aura move piece=" + pieceNetId + " pos=(" + x + "," + y + ") ok=" + ok + (ok ? string.Empty : " exc=0x" + exc.ToInt64().ToString("X")));
            return ok;
        }

        private unsafe bool TryInvokeAuraMonoPuzzleBingo(uint pieceNetId)
        {
            if (!this.EnsureAuraMonoPuzzleProtocolMethods(out string status) || this.puzzleAuraBingoMethod == IntPtr.Zero)
            {
                this.PuzzleLog("Aura bingo unavailable: " + status);
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&pieceNetId);
            auraMonoRuntimeInvoke(this.puzzleAuraBingoMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            bool ok = exc == IntPtr.Zero;
            this.PuzzleLog("Aura bingo piece=" + pieceNetId + " ok=" + ok + (ok ? string.Empty : " exc=0x" + exc.ToInt64().ToString("X")));
            return ok;
        }

        private void ResetPuzzleContext(string status)
        {
            this.StopPuzzleSolve(status);
            this.puzzleBoardNetId = 0U;
            this.puzzleStaticId = 0;
            this.puzzleSentCount = 0;
            this.puzzlePieces.Clear();
        }

        private void PuzzleLog(string message)
        {
            if (!PuzzleLogsEnabled)
            {
                return;
            }

            try
            {
                ModLogger.Msg("[PuzzleNet] " + message);
            }
            catch
            {
            }
        }
    }
}
