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
        // Piece lock/move/bingo are fire-and-forget protocol sends — no per-action settle.
        // One frame yield per piece keeps the main thread from hitching on a 100-piece board.
        private const float PuzzleAutoRetrySeconds = 0.25f;
        private const float PuzzleNextBoardSeconds = 0.15f;
        private const int PuzzleMaxSolvePasses = 5;
        private const bool PuzzleAllowBroadAuraEntityScan = false;
        private const bool PuzzleAllowAuraInteractProbe = false;
        private const bool PuzzleAllowWidgetPointerScan = false;

        private bool puzzleAutoEnabled = false;
        private bool puzzleAutoPlace = false;
        private bool puzzleAutoCollect = false;
        // Suppress JigsawPuzzleBeginEvent / JigsawPuzzleRewardShowEvent so Join/complete do not open
        // BeginStatusPanel / RewardPanel (QuietPopups idiom). Session-only.
        private bool puzzleSuppressUiPanels = false;
        private bool puzzleUiSuppressHooksRegistered = false;
        private float puzzleScanRadiusMeters = 3f;
        private const float PuzzleScanRadiusMin = 1f;
        private const float PuzzleScanRadiusMax = 30f;

        private const string PuzzleBeginEventName = "XDTGameSystem.UI.JigsawPuzzleBeginEvent";
        private const int PuzzleBeginEventBytes = 4; // uint jigsawNetId
        private const string PuzzleRewardShowEventName = "XDTGameSystem.UI.JigsawPuzzleRewardShowEvent";
        private const int PuzzleRewardShowEventBytes = 4; // uint puzzleNetId

        private string puzzleLastPlaceStatus = string.Empty;
        private string puzzleLastCollectStatus = string.Empty;
        private string puzzleLastCollectLoggedStatus = string.Empty;
        private string puzzleMoveBoxesStatus = string.Empty;
        private float puzzlePlaceNextAt = 0f;
        private float puzzleCollectNextAt = 0f;
        private int puzzlePlaceAttempts = 0;
        private int puzzleCollectedCount = 0;
        // Board just cleared by Auto Puzzle — poll it tightly until PoseDeleteBuild lands (or timeout).
        private uint puzzlePendingCollectNetId = 0U;
        private float puzzlePendingCollectUntil = 0f;
        // PoseDeleteBuild sets CharacterProtocolManager._optionType=3 until BuildOptionRespondEvent.
        // If that event never arrives (shop UI, desync, lost listener), every later take returns busy
        // forever and Auto Puzzle piles Completed boards on the floor (MCP 2026-11-15).
        private IntPtr puzzleCollectOptionTypeField = IntPtr.Zero;
        private float puzzleCollectAwaitRespondUntil = 0f;
        private float puzzleCollectBusySince = 0f;

        private bool puzzleSolveRunning = false;
        private float puzzleNextAutoAttemptAt = 0f;
        private uint puzzleBoardNetId = 0U;
        private int puzzleStaticId = 0;
        private string puzzleStatus = "Capture a puzzle board first.";
        private int puzzleSentCount = 0;
        private object puzzleSolveCoroutine = null;
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


        private float GetPuzzleScanRadiusMeters()
        {
            return Mathf.Clamp(this.puzzleScanRadiusMeters, PuzzleScanRadiusMin, PuzzleScanRadiusMax);
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
                FeatureLog.Toggle("PuzzleNet", true);
                if (notify)
                {
                    this.AddMenuNotification("Auto Puzzle Enabled", new Color(0.45f, 1f, 0.55f));
                }
            }
            else
            {
                FeatureLog.Toggle("PuzzleNet", false);
                // Must Leave + exit GameJigsawPuzzleMode — StopPuzzleSolve alone left the player
                // stuck in the jigsaw session (Join without Leave) with no way to enter/exit.
                this.AbortActivePuzzleSession("Auto Puzzle disabled.");
                if (notify)
                {
                    this.AddMenuNotification("Auto Puzzle Disabled", new Color(1f, 0.55f, 0.55f));
                }
            }
        }

        private void ForceStopPuzzleAuto()
        {
            this.SetPuzzleAutoEnabled(false, false);
            this.puzzleAutoPlace = false;
            this.puzzleAutoCollect = false;
            this.puzzlePlaceAttempts = 0;
            this.puzzleCollectNextAt = 0f;
            this.puzzlePendingCollectNetId = 0U;
            this.puzzlePendingCollectUntil = 0f;
            this.puzzleCollectAwaitRespondUntil = 0f;
            this.puzzleCollectBusySince = 0f;
            // Leave puzzleSuppressUiPanels alone — independent QuietPopups-style toggle.
            // SetPuzzleAutoEnabled(false) already aborted the live session.
        }

        // QuietPopups idiom: register once, then re-assert suppress-forward every tick while the
        // toggle is on. Begin opens JigsawPuzzleBeginStatusPanel; Reward opens
        // JigsawPuzzleRewardPanel (the panel that sticks after auto-solve). StatusPanel only opens
        // from Begin's Start button, so suppressing Begin is enough for that path.
        private void UpdatePuzzleUiPanelSuppress()
        {
            bool on = this.puzzleSuppressUiPanels;
            if (!on && !this.puzzleUiSuppressHooksRegistered)
            {
                return;
            }

            this.EnsurePuzzleUiSuppressEventHooks();
            this.SetGameEventHookSuppressForward(PuzzleBeginEventName, on);
            this.SetGameEventHookSuppressForward(PuzzleRewardShowEventName, on);
        }

        private void EnsurePuzzleUiSuppressEventHooks()
        {
            if (this.puzzleUiSuppressHooksRegistered)
            {
                return;
            }

            this.puzzleUiSuppressHooksRegistered = true;
            bool beginOk = this.RegisterGameEventHook(
                PuzzleBeginEventName, PuzzleBeginEventBytes, this.OnPuzzleBeginEventHook);
            bool rewardOk = this.RegisterGameEventHook(
                PuzzleRewardShowEventName, PuzzleRewardShowEventBytes, this.OnPuzzleRewardShowEventHook);
            if (!beginOk || !rewardOk)
            {
                ModLogger.Warning("[PuzzleNet] UI suppress hooks refused — panels may still open:"
                    + " begin=" + beginOk + " reward=" + rewardOk);
            }
            else
            {
                this.PuzzleLog("UI suppress hooks registered (Begin + RewardShow).");
            }
        }

        private void OnPuzzleBeginEventHook(GameEventSnapshot e)
        {
            // Occupies the hook slot for suppress-forward; no payload work needed.
        }

        private void OnPuzzleRewardShowEventHook(GameEventSnapshot e)
        {
            // Occupies the hook slot for suppress-forward; no payload work needed.
        }

        private void SetPuzzleSuppressUiPanels(bool value, bool notify)
        {
            if (this.puzzleSuppressUiPanels == value && !notify)
            {
                return;
            }

            this.puzzleSuppressUiPanels = value;
            if (value)
            {
                this.EnsurePuzzleUiSuppressEventHooks();
                this.SetGameEventHookSuppressForward(PuzzleBeginEventName, true);
                this.SetGameEventHookSuppressForward(PuzzleRewardShowEventName, true);
            }
            else if (this.puzzleUiSuppressHooksRegistered)
            {
                this.SetGameEventHookSuppressForward(PuzzleBeginEventName, false);
                this.SetGameEventHookSuppressForward(PuzzleRewardShowEventName, false);
            }

            if (notify)
            {
                this.AddMenuNotification(
                    this.L("Suppress puzzle UI panels") + ": " + (value ? this.L("On") : this.L("Off")),
                    value ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
            }
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

                // The focused-level-object probe walked the managed self player and never resolved
                // on this build; interact targets below are the live source of candidates.

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
            bool capturedActivePuzzle = false;
            if (this.puzzleAutoEnabled)
            {
                // Radius scan first; panel / interact capture only when no unfinished board is in range.
                if (this.TryFindNearestUnfinishedPuzzleBoard(out uint scanBoardNetId, out int scanStaticId, out string scanStatus))
                {
                    this.puzzleBoardNetId = scanBoardNetId;
                    this.puzzleStaticId = scanStaticId;
                    this.puzzlePieces.Clear();
                    this.puzzleStatus = "Radius scan: board " + scanBoardNetId + " staticId=" + scanStaticId + ".";
                    this.PuzzleLog(this.puzzleStatus + " " + scanStatus);
                    capturedActivePuzzle = true;
                }
                else
                {
                    this.PuzzleLog("Radius scan: " + scanStatus);
                    capturedActivePuzzle = this.TryCapturePuzzleFromCurrentTarget();
                }
            }
            else
            {
                capturedActivePuzzle = this.TryCapturePuzzleFromCurrentTarget();
            }

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

            // Only hold the next solve while a take was just accepted (fixed await) or the
            // build-option gate is mid-unstick — not for the whole pending window (that made
            // "time to start a placed board" jump between ~0.3 s and several seconds).
            if (this.puzzleAutoCollect && this.IsPuzzleCollectTakeBlocking())
            {
                this.puzzleNextAutoAttemptAt = Time.unscaledTime + 0.1f;
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

        private bool IsPuzzleCollectTakeBlocking()
        {
            float now = Time.unscaledTime;
            if (this.puzzleCollectAwaitRespondUntil > now)
            {
                return true;
            }

            // Mid force-clear only — do not treat "busy for 0.1 s" as a multi-second solve gate.
            return this.puzzleCollectBusySince > 0f
                   && (now - this.puzzleCollectBusySince) >= PuzzleCollectBusyStuckSeconds * 0.5f
                   && (now - this.puzzleCollectBusySince) < PuzzleCollectBusyStuckSeconds + 0.5f;
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

        // Stop coroutine, Leave any joined board (mod + player status), exit GameJigsawPuzzleMode,
        // clear local context. Safe to call when already idle.
        private void AbortActivePuzzleSession(string reason)
        {
            uint boardNetId = this.puzzleBoardNetId;
            List<uint> lockedPieces = null;
            if (this.puzzlePieces.Count > 0)
            {
                lockedPieces = new List<uint>(this.puzzlePieces.Count);
                for (int i = 0; i < this.puzzlePieces.Count; i++)
                {
                    PuzzlePieceContext piece = this.puzzlePieces[i];
                    if (piece != null && piece.PieceNetId != 0U)
                    {
                        lockedPieces.Add(piece.PieceNetId);
                    }
                }
            }

            this.StopPuzzleSolve(reason);

            if (lockedPieces != null)
            {
                for (int i = 0; i < lockedPieces.Count; i++)
                {
                    this.TryInvokePuzzleUnlock(lockedPieces[i]);
                }
            }

            if (boardNetId != 0U)
            {
                this.TryInvokePuzzleLeave(boardNetId);
            }

            uint statusBoard = 0U;
            if (this.TryReadPlayerJigsawBoardNetId(out statusBoard) && statusBoard != 0U && statusBoard != boardNetId)
            {
                this.TryInvokePuzzleLeave(statusBoard);
            }

            this.TryExitJigsawGameplayMode();

            this.puzzleBoardNetId = 0U;
            this.puzzleStaticId = 0;
            this.puzzleSentCount = 0;
            this.puzzlePieces.Clear();
            this.puzzlePendingCollectNetId = 0U;
            this.puzzlePendingCollectUntil = 0f;
            this.puzzleStatus = string.IsNullOrWhiteSpace(reason) ? "Stopped." : reason;
            this.PuzzleLog("Puzzle session aborted: " + this.puzzleStatus
                + " leftBoard=" + boardNetId
                + (statusBoard != 0U ? " statusBoard=" + statusBoard : string.Empty));
        }

        private bool TryReadPlayerJigsawBoardNetId(out uint boardNetId)
        {
            boardNetId = 0U;
            try
            {
                if (!this.EnsureAuraMonoApiReady() || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                IntPtr characterClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Game.GameMode.Character");
                IntPtr getCharacterMethod = characterClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(characterClass, "get_character", 0)
                    : IntPtr.Zero;
                if (characterClass == IntPtr.Zero || getCharacterMethod == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr characterObj = auraMonoRuntimeInvoke(getCharacterMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || characterObj == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetMonoObjectMember(characterObj, "Player", out IntPtr playerObj)
                    && !this.TryGetMonoObjectMember(characterObj, "player", out playerObj))
                {
                    return false;
                }

                if (!this.TryGetMonoObjectMember(playerObj, "Status", out IntPtr statusObj)
                    && !this.TryGetMonoObjectMember(playerObj, "status", out statusObj))
                {
                    return false;
                }

                if (!this.TryGetMonoObjectMember(statusObj, "JigsawPuzzleStatus", out IntPtr jigsawStatusObj)
                    && !this.TryGetMonoObjectMember(statusObj, "jigsawPuzzleStatus", out jigsawStatusObj))
                {
                    return false;
                }

                return this.TryGetMonoUInt32Member(jigsawStatusObj, "jigsawPuzzleNetId", out boardNetId)
                       || this.TryGetMonoUInt32Member(jigsawStatusObj, "JigsawPuzzleNetId", out boardNetId);
            }
            catch
            {
                boardNetId = 0U;
                return false;
            }
        }

        // If Character.currMode is GameJigsawPuzzleMode (even with a cleared target), ExitMode it.
        // Leaving Join alone is not enough — the mode can stay active and block re-entry.
        // NEVER ExitMode(Free/Photo/…) by ordinal guess: that corrupts the mode stack (HUD gone, jump dead).
        private unsafe bool TryExitJigsawGameplayMode()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                IntPtr characterClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Game.GameMode.Character");
                IntPtr getCharacterMethod = characterClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(characterClass, "get_character", 0)
                    : IntPtr.Zero;
                IntPtr getCurrMode = characterClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(characterClass, "get_currMode", 0)
                    : IntPtr.Zero;
                IntPtr exitMode = characterClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(characterClass, "ExitMode", 1)
                    : IntPtr.Zero;
                if (characterClass == IntPtr.Zero || getCharacterMethod == IntPtr.Zero
                    || getCurrMode == IntPtr.Zero || exitMode == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr characterObj = auraMonoRuntimeInvoke(getCharacterMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || characterObj == IntPtr.Zero)
                {
                    return false;
                }

                exc = IntPtr.Zero;
                IntPtr currMode = auraMonoRuntimeInvoke(getCurrMode, characterObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || currMode == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr jigsawModeClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.Game.GameMode.GameJigsawPuzzleMode");
                IntPtr freeModeClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.Game.GameMode.GameFreeMode");
                if (jigsawModeClass == IntPtr.Zero || auraMonoObjectGetClass == null)
                {
                    return false;
                }

                IntPtr currClass = auraMonoObjectGetClass(currMode);
                if (currClass == IntPtr.Zero)
                {
                    return false;
                }

                // Already Free (or anything else): do not touch ExitMode.
                if (freeModeClass != IntPtr.Zero && this.IsAuraMonoClassAssignableTo(currClass, freeModeClass))
                {
                    return false;
                }

                if (!this.IsAuraMonoClassAssignableTo(currClass, jigsawModeClass))
                {
                    return false;
                }

                IntPtr getMode = this.FindAuraMonoMethodOnHierarchy(jigsawModeClass, "get_Mode", 0);
                if (getMode == IntPtr.Zero)
                {
                    getMode = this.FindAuraMonoMethodOnHierarchy(
                        this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Game.GameMode.GameBaseMode"),
                        "get_Mode",
                        0);
                }

                if (getMode == IntPtr.Zero)
                {
                    return false;
                }

                exc = IntPtr.Zero;
                IntPtr modeBoxed = auraMonoRuntimeInvoke(getMode, currMode, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || modeBoxed == IntPtr.Zero || auraMonoObjectUnbox == null)
                {
                    return false;
                }

                // EGameplayMode is a byte enum — value MUST come from this jigsaw instance, never a guess.
                byte modeValue = *(byte*)auraMonoObjectUnbox(modeBoxed);
                if (modeValue == 0)
                {
                    // Free == 0. Refusing protects against a bad get_Mode read wiping Free.
                    this.PuzzleLog("Exit jigsaw mode skipped: get_Mode returned Free(0).");
                    return false;
                }

                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&modeValue);
                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(exitMode, characterObj, (IntPtr)args, ref exc);
                bool ok = exc == IntPtr.Zero;
                this.PuzzleLog(ok
                    ? "Exited GameJigsawPuzzleMode (EGameplayMode=" + modeValue + ")."
                    : "ExitMode GameJigsawPuzzleMode failed.");
                return ok;
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Exit jigsaw mode exception: " + ex.Message);
                return false;
            }
        }

        private System.Collections.IEnumerator PuzzleSolveRoutine()
        {
            List<PuzzlePieceContext> capturedPieces = this.puzzlePieces.Count > 0 ? new List<PuzzlePieceContext>(this.puzzlePieces) : null;
            if (this.puzzlePieces.Count <= 0)
            {
                this.TryInvokePuzzleJoin();
                yield return null; // one frame for join side-effects / piece bags to appear
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

                    if (piece.IsInBag || piece.MaskCount == 1)
                    {
                        if (this.TryInvokePuzzleBingo(piece.PieceNetId))
                        {
                            this.puzzleSentCount++;
                        }
                    }
                    else
                    {
                        this.TryInvokePuzzleMove(piece.PieceNetId, piece.TargetX, piece.TargetY);
                        if (this.TryInvokePuzzleBingo(piece.PieceNetId))
                        {
                            this.puzzleSentCount++;
                        }
                    }

                    // No network settle — lock/move/bingo are independent sends. One frame keeps
                    // a large board from stalling the player loop for the whole pass.
                    yield return null;
                }

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
                yield return null;
            }

            this.puzzleSolveRunning = false;
            this.puzzleSolveCoroutine = null;
            this.puzzleStatus = "Puzzle solve finished. Sent " + this.puzzleSentCount + " piece action(s), remaining=" + this.puzzlePieces.Count + ".";
            FeatureLog.Life("PuzzleNet", this.puzzleStatus);
            if (this.puzzlePieces.Count <= 0 && this.puzzleSentCount > 0)
            {
                uint clearedBoard = this.puzzleBoardNetId;
                this.puzzleBoardNetId = 0U;
                this.puzzleStaticId = 0;
                this.puzzlePieces.Clear();
                this.puzzleStatus = "Puzzle cleared. Looking for next...";

                // Immediate take while the board netId is still known. Server may still be converting
                // 900xxx → 902; if busy/not ready, pending + tight poll picks it up.
                if (this.puzzleAutoCollect && clearedBoard != 0U)
                {
                    this.puzzlePendingCollectNetId = clearedBoard;
                    this.puzzlePendingCollectUntil = Time.unscaledTime + PuzzleCollectPendingSeconds;
                    this.puzzleCollectNextAt = 0f;
                    // Skip immediate take while a prior PoseDeleteBuild still owns _optionType —
                    // firing another accept-path that fails leaves pending for the poller instead.
                    if (!this.IsPuzzleCollectTakeBlocking()
                        && this.EnsurePuzzleCollectResolved(out _)
                        && this.TryPuzzlePoseDeleteBuild(clearedBoard, out string takeStatus))
                    {
                        this.puzzleCollectedCount++;
                        this.puzzlePendingCollectNetId = 0U;
                        this.puzzlePendingCollectUntil = 0f;
                        this.puzzleLastCollectStatus = "collected #" + this.puzzleCollectedCount
                            + " netId=" + clearedBoard + " (immediate " + takeStatus + ")";
                        this.PuzzleLog("collect: " + this.puzzleLastCollectStatus);
                        FeatureLog.Life("PuzzleNet", "collect: " + this.puzzleLastCollectStatus);
                        this.AddMenuNotification(this.L("Puzzle collected"), new Color(0.45f, 1f, 0.55f));
                        this.puzzleCollectNextAt = Time.unscaledTime + PuzzleCollectTakeBackoffSeconds;
                    }
                }
                else
                {
                    this.puzzleCollectNextAt = 0f;
                }

                if (clearedBoard != 0U)
                {
                    this.TryInvokePuzzleLeave(clearedBoard);
                }

                if (this.puzzleAutoEnabled)
                {
                    this.puzzleNextAutoAttemptAt = Time.unscaledTime + PuzzleNextBoardSeconds;
                    this.PuzzleLog("Auto Puzzle will look for the next board shortly.");
                }
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

        private bool TryInvokePuzzleLeave(uint boardNetId)
        {
            if (boardNetId == 0U)
            {
                return false;
            }

            if (this.TryInvokeAuraMonoPuzzleLeave(boardNetId))
            {
                return true;
            }

            if (!this.EnsurePuzzleProtocolMethods() || this.puzzleLeaveMethod == null)
            {
                return false;
            }

            try
            {
                this.puzzleLeaveMethod.Invoke(null, new object[] { boardNetId });
                return true;
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Leave puzzle failed " + boardNetId + ": " + ex.Message);
            }

            return false;
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

        private bool TryInvokePuzzleUnlock(uint pieceNetId)
        {
            if (pieceNetId == 0U)
            {
                return false;
            }

            if (this.TryInvokeAuraMonoPuzzleUnlock(pieceNetId))
            {
                return true;
            }

            if (!this.EnsurePuzzleProtocolMethods() || this.puzzleUnlockMethod == null)
            {
                return false;
            }

            try
            {
                this.puzzleUnlockMethod.Invoke(null, new object[] { pieceNetId });
                return true;
            }
            catch (Exception ex)
            {
                this.PuzzleLog("Unlock piece failed " + pieceNetId + ": " + ex.Message);
            }

            return false;
        }

        private unsafe bool TryInvokeAuraMonoPuzzleUnlock(uint pieceNetId)
        {
            if (!this.EnsureAuraMonoPuzzleProtocolMethods(out string status) || this.puzzleAuraUnlockMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&pieceNetId);
            auraMonoRuntimeInvoke(this.puzzleAuraUnlockMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
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

        private unsafe bool TryInvokeAuraMonoPuzzleLeave(uint boardNetId)
        {
            if (!this.EnsureAuraMonoPuzzleProtocolMethods(out string status) || this.puzzleAuraLeaveMethod == IntPtr.Zero)
            {
                this.PuzzleLog("Aura leave unavailable: " + status);
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&boardNetId);
            auraMonoRuntimeInvoke(this.puzzleAuraLeaveMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            bool ok = exc == IntPtr.Zero;
            this.PuzzleLog("Aura leave board=" + boardNetId + " ok=" + ok + (ok ? string.Empty : " exc=0x" + exc.ToInt64().ToString("X")));
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

        // ===== radius board scan =============================================================
        private static readonly string[] PuzzleAuraBoardComponentTypeNames =
        {
            "XDTLevelAndEntity.Gameplay.Component.Homeland.JigsawPuzzleComponent",
            "XDTLevelAndEntity.GamePlay.Component.Homeland.JigsawPuzzleComponent",
        };
        private const string PuzzleAuraSystemTypeName = "XDTGameSystem.GameplaySystem.JigsawPuzzle.JigsawPuzzleSystem";
        private IntPtr puzzleAuraBoardComponentClass = IntPtr.Zero;
        private IntPtr puzzleAuraIsBoardFinishedMethod = IntPtr.Zero;

        // Nearest unfinished placed box (JigsawPuzzleComponent view) within scan radius.
        // Unfinished = staticId is a box row (900001..900669 + TableData row) AND
        // JigsawPuzzleComponentData.isFinished == false AND JigsawPuzzleSystem.IsJigsawPuzzleBoardFinished(netId) == false
        // (server-mirrored board mask == row*col). Scalars only escape the pinned scope.
        // radiusOverrideMeters > 0 replaces GetPuzzleScanRadiusMeters (used by auto-place so a small
        // slider still sees a box placed at PuzzlePlaceDistanceMeters).
        private unsafe bool TryFindNearestUnfinishedPuzzleBoard(out uint boardNetId, out int staticId, out string status)
        {
            return this.TryFindNearestUnfinishedPuzzleBoard(out boardNetId, out staticId, out status, 0f);
        }

        private unsafe bool TryFindNearestUnfinishedPuzzleBoard(
            out uint boardNetId, out int staticId, out string status, float radiusOverrideMeters)
        {
            boardNetId = 0U;
            staticId = 0;
            status = string.Empty;
            List<uint> pins = new List<uint>();
            try
            {
                if (!AuraMonoPinningAvailable)
                {
                    status = "pinning unavailable";
                    return false;
                }

                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    status = "aura api not ready";
                    return false;
                }

                if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
                {
                    status = "GetComponents not ready";
                    return false;
                }

                if (this.puzzleAuraBoardComponentClass == IntPtr.Zero)
                {
                    for (int i = 0; i < PuzzleAuraBoardComponentTypeNames.Length && this.puzzleAuraBoardComponentClass == IntPtr.Zero; i++)
                    {
                        this.puzzleAuraBoardComponentClass = this.FindAuraMonoClassByFullName(PuzzleAuraBoardComponentTypeNames[i]);
                    }
                }

                if (this.puzzleAuraBoardComponentClass == IntPtr.Zero)
                {
                    status = "JigsawPuzzleComponent class unavailable";
                    return false;
                }

                if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos))
                {
                    status = "no player position";
                    return false;
                }

                if (!this.TryAuraMonoGetComponentObjects(this.puzzleAuraBoardComponentClass, out List<IntPtr> comps, pins)
                    || comps == null || comps.Count == 0)
                {
                    status = "no JigsawPuzzleComponent instances";
                    return false;
                }

                float radius = radiusOverrideMeters > 0f
                    ? radiusOverrideMeters
                    : this.GetPuzzleScanRadiusMeters();
                float bestDistance = radius;
                int inRange = 0;
                int finished = 0;
                int unreadable = 0;
                IntPtr systemObj = IntPtr.Zero;
                bool systemResolved = false;
                IntPtr* argSlot = stackalloc IntPtr[1];
                for (int i = 0; i < comps.Count; i++)
                {
                    IntPtr compObj = comps[i];
                    if (compObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(compObj, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero
                        || !this.TryGetAuraMonoEntityNetId(entityObj, out uint netId) || netId == 0U
                        || !this.TryGetAuraMonoEntityPosition(entityObj, out Vector3 pos))
                    {
                        unreadable++;
                        continue;
                    }

                    float distance = Vector3.Distance(playerPos, pos);
                    if (distance > radius)
                    {
                        continue;
                    }

                    inRange++;
                    if (!this.TryReadPuzzleBoardComponentData(compObj, out int candidateStaticId, out bool isFinished))
                    {
                        unreadable++;
                        continue;
                    }

                    if (isFinished || !this.PuzzleIsUnfinishedBox(candidateStaticId))
                    {
                        finished++;
                        continue;
                    }

                    if (distance > bestDistance)
                    {
                        continue;
                    }

                    if (!systemResolved)
                    {
                        systemResolved = true;
                        if (this.TryResolveAuraMonoModule(PuzzleAuraSystemTypeName, out systemObj) && systemObj != IntPtr.Zero)
                        {
                            pins.Add(AuraMonoPinNew(systemObj));
                            IntPtr systemClass = auraMonoObjectGetClass(systemObj);
                            if (systemClass != IntPtr.Zero)
                            {
                                this.puzzleAuraIsBoardFinishedMethod = this.FindAuraMonoMethodOnHierarchy(systemClass, "IsJigsawPuzzleBoardFinished", 1);
                            }
                        }
                    }

                    if (systemObj == IntPtr.Zero || this.puzzleAuraIsBoardFinishedMethod == IntPtr.Zero)
                    {
                        status = "JigsawPuzzleSystem.IsJigsawPuzzleBoardFinished unavailable";
                        return false;
                    }

                    IntPtr exc = IntPtr.Zero;
                    uint arg = netId;
                    argSlot[0] = (IntPtr)(&arg);
                    IntPtr boxed = auraMonoRuntimeInvoke(this.puzzleAuraIsBoardFinishedMethod, systemObj, (IntPtr)argSlot, ref exc);
                    if (exc != IntPtr.Zero || boxed == IntPtr.Zero || !this.TryUnboxMonoBoolean(boxed, out bool boardDone))
                    {
                        unreadable++;
                        continue;
                    }

                    if (boardDone)
                    {
                        finished++;
                        continue;
                    }

                    bestDistance = distance;
                    boardNetId = netId;
                    staticId = candidateStaticId;
                }

                if (boardNetId == 0U)
                {
                    status = "no unfinished board in " + radius.ToString("F1") + "m (components=" + comps.Count
                             + " inRange=" + inRange + " finished=" + finished + " unreadable=" + unreadable + ")";
                    return false;
                }

                status = "dist=" + bestDistance.ToString("F1") + "m (components=" + comps.Count
                         + " inRange=" + inRange + " finished=" + finished + ")";
                return true;
            }
            catch (Exception ex)
            {
                boardNetId = 0U;
                staticId = 0;
                status = "board scan exception: " + ex.Message;
                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        // JigsawPuzzleComponentData { int staticId; bool isFinished; ... } read from the boxed struct via real
        // field offsets (header-inclusive; subtract 2*IntPtr.Size). Mask fields are intentionally not read here.
        private unsafe bool TryReadPuzzleBoardComponentData(IntPtr componentObj, out int staticId, out bool isFinished)
        {
            staticId = 0;
            isFinished = false;
            if (componentObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoObjectUnbox == null
                || auraMonoClassGetFieldFromName == null || auraMonoFieldGetOffset == null)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(componentObj, "_componentData", out IntPtr boxedData) || boxedData == IntPtr.Zero)
            {
                return false;
            }

            uint boxPin = AuraMonoPinNew(boxedData);
            try
            {
                IntPtr dataClass = auraMonoObjectGetClass(boxedData);
                if (dataClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr staticField = auraMonoClassGetFieldFromName(dataClass, "staticId");
                IntPtr finishedField = auraMonoClassGetFieldFromName(dataClass, "isFinished");
                IntPtr raw = auraMonoObjectUnbox(boxedData);
                if (staticField == IntPtr.Zero || finishedField == IntPtr.Zero || raw == IntPtr.Zero)
                {
                    return false;
                }

                int header = 2 * IntPtr.Size;
                staticId = System.Runtime.InteropServices.Marshal.ReadInt32(raw, (int)auraMonoFieldGetOffset(staticField) - header);
                isFinished = System.Runtime.InteropServices.Marshal.ReadByte(raw, (int)auraMonoFieldGetOffset(finishedField) - header) != 0;
                return true;
            }
            catch (Exception ex)
            {
                this.PuzzleLog("board data read exception: " + ex.Message);
                return false;
            }
            finally
            {
                AuraMonoPinFree(boxPin);
            }
        }

        // ===== move unfinished puzzle boxes: warehouse -> backpack ==========================
        // EntityType 901 (ids 900001..900669). Finished 902 and custom 903 are out of scope.
        // Mirrors TryMoveSandBasesWarehouseToBackpack: PINNED enumeration, locked stacks skipped,
        // filter = TableData jigsaw-puzzle row (not a hard-coded id list).
        private const int PuzzleBoxStaticIdMin = 900001;
        private const int PuzzleBoxStaticIdMax = 900669;
        private IntPtr puzzleTableDataClass = IntPtr.Zero;
        private IntPtr puzzleGetJigsawpuzzleMethod = IntPtr.Zero;
        private bool puzzleGetJigsawpuzzleResolved = false;

        private void EnsurePuzzleGetJigsawpuzzleResolved()
        {
            if (this.puzzleGetJigsawpuzzleMethod != IntPtr.Zero)
            {
                return;
            }

            // Class/method pointers stay valid for the image lifetime. Retry on each call until resolved
            // (this runs from a button press, not a per-frame poll).
            if (this.puzzleTableDataClass == IntPtr.Zero)
            {
                this.puzzleTableDataClass = this.FindAuraMonoTableDataClass();
                if (this.puzzleTableDataClass == IntPtr.Zero)
                {
                    this.puzzleTableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
                }
            }

            if (this.puzzleTableDataClass == IntPtr.Zero)
            {
                return;
            }

            this.puzzleGetJigsawpuzzleMethod = this.FindAuraMonoMethodOnHierarchy(this.puzzleTableDataClass, "GetJigsawpuzzle", 2);
            if (this.puzzleGetJigsawpuzzleMethod == IntPtr.Zero)
            {
                this.puzzleGetJigsawpuzzleMethod = this.FindAuraMonoMethodOnHierarchy(this.puzzleTableDataClass, "GetJigsawpuzzle", 1);
            }

            this.puzzleGetJigsawpuzzleResolved = this.puzzleGetJigsawpuzzleMethod != IntPtr.Zero;
        }

        // True iff a TableData jigsaw-puzzle row exists for staticId (unfinished box, EntityType 901).
        private unsafe bool PuzzleIsUnfinishedBox(int staticId)
        {
            if (staticId < PuzzleBoxStaticIdMin || staticId > PuzzleBoxStaticIdMax)
            {
                return false;
            }

            this.EnsurePuzzleGetJigsawpuzzleResolved();
            if (this.puzzleGetJigsawpuzzleMethod != IntPtr.Zero && auraMonoRuntimeInvoke != null)
            {
                IntPtr exc = IntPtr.Zero;
                int id = staticId;
                IntPtr result;
                if (AuraMonoMethodParamCountIs(this.puzzleGetJigsawpuzzleMethod, 2))
                {
                    byte needException = 0;
                    IntPtr* a = stackalloc IntPtr[2];
                    a[0] = (IntPtr)(&id);
                    a[1] = (IntPtr)(&needException);
                    result = auraMonoRuntimeInvoke(this.puzzleGetJigsawpuzzleMethod, IntPtr.Zero, (IntPtr)a, ref exc);
                }
                else
                {
                    IntPtr* a = stackalloc IntPtr[1];
                    a[0] = (IntPtr)(&id);
                    result = auraMonoRuntimeInvoke(this.puzzleGetJigsawpuzzleMethod, IntPtr.Zero, (IntPtr)a, ref exc);
                }

                return exc == IntPtr.Zero && result != IntPtr.Zero;
            }

            // Fallback: TableJigsawpuzzles contains-key (same lookup as TryGetPuzzleBoardRowCol).
            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                object table = tableDataType != null
                    ? tableDataType.GetField("TableJigsawpuzzles", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                    : null;
                return table is IDictionary dictionary && dictionary.Contains(staticId);
            }
            catch
            {
                return false;
            }
        }

        // Finished display frame (EntityType 902): TableJigsawpuzzlefinisheds / GetJigsawpuzzlefinished.
        // staticId lives in the 901xxx band — outside PuzzleIsUnfinishedBox (900001..900669).
        private IntPtr puzzleGetJigsawpuzzlefinishedMethod = IntPtr.Zero;
        private bool puzzleGetJigsawpuzzlefinishedResolved = false;

        private void EnsurePuzzleGetJigsawpuzzlefinishedResolved()
        {
            if (this.puzzleGetJigsawpuzzlefinishedMethod != IntPtr.Zero)
            {
                return;
            }

            if (this.puzzleTableDataClass == IntPtr.Zero)
            {
                this.puzzleTableDataClass = this.FindAuraMonoTableDataClass();
                if (this.puzzleTableDataClass == IntPtr.Zero)
                {
                    this.puzzleTableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
                }
            }

            if (this.puzzleTableDataClass == IntPtr.Zero)
            {
                return;
            }

            this.puzzleGetJigsawpuzzlefinishedMethod = this.FindAuraMonoMethodOnHierarchy(
                this.puzzleTableDataClass, "GetJigsawpuzzlefinished", 2);
            if (this.puzzleGetJigsawpuzzlefinishedMethod == IntPtr.Zero)
            {
                this.puzzleGetJigsawpuzzlefinishedMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.puzzleTableDataClass, "GetJigsawpuzzlefinished", 1);
            }

            this.puzzleGetJigsawpuzzlefinishedResolved = this.puzzleGetJigsawpuzzlefinishedMethod != IntPtr.Zero;
        }

        private unsafe bool PuzzleIsFinishedFrame(int staticId)
        {
            if (staticId <= 0)
            {
                return false;
            }

            // Fast band check from live backpack samples (901013..901654, entityType 902).
            if (staticId >= 901000 && staticId <= 901999)
            {
                return true;
            }

            this.EnsurePuzzleGetJigsawpuzzlefinishedResolved();
            if (this.puzzleGetJigsawpuzzlefinishedMethod != IntPtr.Zero && auraMonoRuntimeInvoke != null)
            {
                IntPtr exc = IntPtr.Zero;
                int id = staticId;
                IntPtr result;
                if (AuraMonoMethodParamCountIs(this.puzzleGetJigsawpuzzlefinishedMethod, 2))
                {
                    byte needException = 0;
                    IntPtr* a = stackalloc IntPtr[2];
                    a[0] = (IntPtr)(&id);
                    a[1] = (IntPtr)(&needException);
                    result = auraMonoRuntimeInvoke(this.puzzleGetJigsawpuzzlefinishedMethod, IntPtr.Zero, (IntPtr)a, ref exc);
                }
                else
                {
                    IntPtr* a = stackalloc IntPtr[1];
                    a[0] = (IntPtr)(&id);
                    result = auraMonoRuntimeInvoke(this.puzzleGetJigsawpuzzlefinishedMethod, IntPtr.Zero, (IntPtr)a, ref exc);
                }

                return exc == IntPtr.Zero && result != IntPtr.Zero;
            }

            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                object table = tableDataType != null
                    ? tableDataType.GetField("TableJigsawpuzzlefinisheds", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                    : null;
                return table is IDictionary dictionary && dictionary.Contains(staticId);
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryMoveUnfinishedPuzzleBoxesWarehouseToBackpack(out string status)
        {
            status = string.Empty;
            try
            {
                if (!AuraMonoPinningAvailable)
                {
                    status = "pinning unavailable";
                    return false;
                }

                this.EnsurePuzzleGetJigsawpuzzleResolved();
                if (!this.puzzleGetJigsawpuzzleResolved)
                {
                    status = "TableData.GetJigsawpuzzle unavailable";
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPack)
                    || backPack == IntPtr.Zero)
                {
                    status = "BackPackSystem unavailable";
                    return false;
                }

                IntPtr getAllItem = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(backPack), "GetAllItem", 1);
                if (getAllItem == IntPtr.Zero)
                {
                    getAllItem = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(backPack), "GetAllItem", 0);
                }

                if (getAllItem == IntPtr.Zero)
                {
                    status = "GetAllItem missing";
                    return false;
                }

                int storageWarehouse = 2; // EStorageType.Warehouse
                IntPtr listObj;
                IntPtr exc = IntPtr.Zero;
                if (AuraMonoMethodParamCountIs(getAllItem, 1))
                {
                    IntPtr* a = stackalloc IntPtr[1];
                    a[0] = (IntPtr)(&storageWarehouse);
                    listObj = auraMonoRuntimeInvoke(getAllItem, backPack, (IntPtr)a, ref exc);
                }
                else
                {
                    listObj = auraMonoRuntimeInvoke(getAllItem, backPack, IntPtr.Zero, ref exc);
                }

                if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
                {
                    status = "warehouse read failed";
                    return false;
                }

                Dictionary<uint, int> map = new Dictionary<uint, int>();
                List<uint> pins = new List<uint>();
                List<IntPtr> items = new List<IntPtr>();
                int boxStacks = 0;
                int boxQty = 0;
                try
                {
                    if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items, pins) || items.Count == 0)
                    {
                        status = "warehouse empty";
                        return false;
                    }

                    for (int i = 0; i < items.Count; i++)
                    {
                        IntPtr itemObj = items[i];
                        if (itemObj == IntPtr.Zero
                            || !this.TryGetDirectBackpackItemNetId(itemObj, out uint netId)
                            || netId == 0U)
                        {
                            continue;
                        }

                        if (this.TryGetDirectBackpackItemIsLocked(itemObj, out bool isLocked) && isLocked)
                        {
                            continue;
                        }

                        if (!this.TryGetDirectBackpackItemStaticId(itemObj, out int staticId) || staticId <= 0
                            || !this.PuzzleIsUnfinishedBox(staticId))
                        {
                            continue;
                        }

                        if (!this.TryGetDirectBackpackItemCount(itemObj, out int count) || count <= 0)
                        {
                            count = 1;
                        }

                        map[netId] = count;
                        boxStacks++;
                        boxQty += count;
                    }
                }
                finally
                {
                    FreeAuraMonoPins(pins);
                }

                if (map.Count == 0)
                {
                    status = "no puzzle boxes in warehouse";
                    return false;
                }

                if (!this.TrySendWarehouseStacksToBagChunked(map, out int movedStacks, out int movedQty, out string error))
                {
                    status = string.IsNullOrEmpty(error) ? "MoveBatchBackpackItems failed" : error;
                    return false;
                }

                status = "Moved " + movedStacks + " puzzle box stack(s), qty " + movedQty + " -> Bag";
                this.PuzzleLog(status);
                return true;
            }
            catch (Exception ex)
            {
                status = "warehouse scan exception: " + ex.Message;
                return false;
            }
            finally
            {
                this.puzzleMoveBoxesStatus = status;
            }
        }

        // ===== auto-place unfinished puzzle boxes from the backpack ==========================
        // Same BuildPlaceData pipeline as the sand base (EnsureSandPlaceResolved / TryResolveSandPlaceTarget /
        // TryCheckSandFieldLoadAllows / TrySendSandBasePlace are shared); only the bag-item filter differs.
        private const int PuzzlePlaceMaxAttempts = 3;
        private const float PuzzlePlaceBackoffSeconds = 2.5f;          // after a FAILED place
        private const float PuzzlePlaceBoardPresentBackoffSeconds = 0.25f; // unfinished board already near — retry soon
        private const float PuzzlePlaceRescanSeconds = 0.15f;          // polling for the just-placed box
        private const float PuzzlePlacePendingWindowSeconds = 2.0f;    // spawn wait before placing another
        private const float PuzzlePlaceDistanceMeters = 0.7f;          // ahead of player (sand uses 1.5 m)
        private const int PuzzlePlaceMaxBackpackScan = 4096;
        private const string PuzzleNoBoxInBackpackPrefix = "no puzzle box in backpack";
        private float puzzlePlaceSentAt = 0f;

        // Scan backpack (EStorageType.Backpack == 1), first item whose staticId is an unfinished puzzle box.
        private unsafe bool TryResolvePuzzleBoxBackpackItem(out uint bagItemNetId, out int staticId, out string status)
        {
            bagItemNetId = 0U;
            staticId = 0;
            status = string.Empty;

            if (!AuraMonoPinningAvailable)
            {
                status = "pinning unavailable";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                status = "aura not ready";
                return false;
            }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPack)
                || backPack == IntPtr.Zero)
            {
                status = "BackPackSystem unavailable";
                return false;
            }

            IntPtr getAllItem = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(backPack), "GetAllItem", 1);
            if (getAllItem == IntPtr.Zero)
            {
                getAllItem = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(backPack), "GetAllItem", 0);
            }

            if (getAllItem == IntPtr.Zero)
            {
                status = "GetAllItem missing";
                return false;
            }

            IntPtr listObj;
            IntPtr exc = IntPtr.Zero;
            int storageBackpack = 1; // EStorageType.Backpack
            if (AuraMonoMethodParamCountIs(getAllItem, 1))
            {
                IntPtr* a = stackalloc IntPtr[1];
                a[0] = (IntPtr)(&storageBackpack);
                listObj = auraMonoRuntimeInvoke(getAllItem, backPack, (IntPtr)a, ref exc);
            }
            else
            {
                listObj = auraMonoRuntimeInvoke(getAllItem, backPack, IntPtr.Zero, ref exc);
            }

            if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
            {
                status = "GetAllItem failed";
                return false;
            }

            List<uint> pins = new List<uint>();
            List<IntPtr> items = new List<IntPtr>(256);
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items, pins) || items.Count == 0)
                {
                    status = "backpack empty";
                    return false;
                }

                for (int i = 0; i < items.Count && i < PuzzlePlaceMaxBackpackScan; i++)
                {
                    IntPtr itemObj = items[i];
                    if (itemObj == IntPtr.Zero
                        || !this.TryGetDirectBackpackItemNetId(itemObj, out uint candNetId) || candNetId == 0U
                        || !this.TryGetDirectBackpackItemStaticId(itemObj, out int candStatic) || candStatic <= 0
                        || !this.PuzzleIsUnfinishedBox(candStatic))
                    {
                        continue;
                    }

                    bagItemNetId = candNetId;
                    staticId = candStatic;
                    status = "item netId=" + candNetId + " staticId=" + candStatic + " (scanned " + (i + 1) + ")";
                    return true;
                }

                status = "no puzzle box among " + items.Count + " items";
                return false;
            }
            catch (Exception ex)
            {
                status = "scan exception: " + ex.Message;
                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private bool TryPlaceUnfinishedPuzzleBoxFromBackpack(out string status)
        {
            status = "puzzle place unavailable";

            if (!this.EnsureSandPlaceResolved(out string placeResolve))
            {
                status = "place types not resolved: " + placeResolve;
                this.PuzzleLog("place: " + status);
                return false;
            }

            if (!this.TryResolvePuzzleBoxBackpackItem(out uint bagItemNetId, out int staticId, out string itemStatus))
            {
                status = PuzzleNoBoxInBackpackPrefix + ": " + itemStatus;
                this.PuzzleLog("place: " + status);
                return false;
            }

            if (!this.TryResolveSandPlaceTarget(out uint buildRootNetId, out ulong putZoneId,
                                                out Vector3 localPos, out int angle, out string targetStatus,
                                                PuzzlePlaceDistanceMeters))
            {
                status = "target unresolved: " + targetStatus;
                this.PuzzleLog("place: " + status);
                return false;
            }

            // Fail-open game gate (BuildModule.CheckCanPut); an explicit refusal blocks the put.
            if (!this.TryCheckSandFieldLoadAllows(staticId, out string loadStatus))
            {
                status = loadStatus;
                this.PuzzleLog("place: " + status);
                return false;
            }

            if (!this.TrySendSandBasePlace(bagItemNetId, buildRootNetId, putZoneId, localPos, angle, out string sendStatus))
            {
                status = "send failed: " + sendStatus;
                this.PuzzleLog("place: " + status);
                return false;
            }

            status = "placed puzzle box staticId=" + staticId + " item=" + bagItemNetId
                     + " root=" + buildRootNetId + " zone=0x" + putZoneId.ToString("X")
                     + " localPos=" + localPos + " (" + sendStatus + ")";
            this.PuzzleLog("place OK: " + status);
            return true;
        }

        private void UpdatePuzzleAutoPlace()
        {
            if (!this.puzzleAutoPlace)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.puzzlePlaceNextAt)
            {
                return;
            }

            // Detect with at least placeDistance+0.5 so a small slider still sees the just-placed box.
            float placeDetectRadius = Mathf.Max(this.GetPuzzleScanRadiusMeters(), PuzzlePlaceDistanceMeters + 0.5f);

            // An unfinished board near the player already exists: nothing to place; reset the guards.
            if (this.TryFindNearestUnfinishedPuzzleBoard(out _, out _, out string scanStatus, placeDetectRadius))
            {
                this.puzzlePlaceAttempts = 0;
                this.puzzlePlaceSentAt = 0f;
                this.puzzlePlaceNextAt = now + PuzzlePlaceBoardPresentBackoffSeconds;
                return;
            }

            // Only a definite "no board" answer allows placing; a not-ready scan must not cause a duplicate place.
            bool definitelyNoBoard = scanStatus.StartsWith("no unfinished board", StringComparison.Ordinal)
                                     || scanStatus.StartsWith("no JigsawPuzzleComponent instances", StringComparison.Ordinal);
            if (!definitelyNoBoard)
            {
                this.puzzlePlaceNextAt = now + PuzzlePlaceBackoffSeconds;
                return;
            }

            // Just placed: wait for the box to stream in before placing another.
            if (this.puzzlePlaceSentAt > 0f && now - this.puzzlePlaceSentAt < PuzzlePlacePendingWindowSeconds)
            {
                this.puzzlePlaceNextAt = now + PuzzlePlaceRescanSeconds;
                return;
            }

            this.puzzlePlaceSentAt = 0f;
            if (this.puzzlePlaceAttempts >= PuzzlePlaceMaxAttempts)
            {
                // Placed repeatedly but never saw a board within the scan radius.
                this.puzzleAutoPlace = false;
                this.puzzleLastPlaceStatus = "auto-place disabled: placed box not detected within scan radius";
                this.AddMenuNotification(this.L("Auto-place puzzle from backpack") + ": " + this.L("Off"), new Color(1f, 0.55f, 0.55f));
                return;
            }

            this.puzzlePlaceAttempts++;
            bool ok = this.TryPlaceUnfinishedPuzzleBoxFromBackpack(out string placeStatus);
            this.puzzleLastPlaceStatus = placeStatus;
            if (ok)
            {
                this.puzzlePlaceSentAt = now;
                this.puzzlePlaceNextAt = now + PuzzlePlaceRescanSeconds;
                return;
            }

            if (placeStatus.StartsWith(PuzzleNoBoxInBackpackPrefix, StringComparison.Ordinal))
            {
                this.puzzleAutoPlace = false;
                this.puzzleLastPlaceStatus = "stopped: no puzzle boxes left in backpack";
                this.AddMenuNotification(this.L("Auto-place puzzle from backpack") + ": " + this.L("no puzzle boxes left"), new Color(1f, 0.55f, 0.55f));
                return;
            }

            if (this.puzzlePlaceAttempts >= PuzzlePlaceMaxAttempts)
            {
                this.puzzleAutoPlace = false;
                this.puzzleLastPlaceStatus = "auto-place disabled: " + placeStatus;
                this.AddMenuNotification(this.L("Auto-place puzzle from backpack") + ": " + this.L("Off"), new Color(1f, 0.55f, 0.55f));
                return;
            }

            this.puzzlePlaceNextAt = now + PuzzlePlaceBackoffSeconds;
        }

        // ===== auto-collect finished / mask-complete puzzle boards ============================
        // Live (2026-11-15): after solve the entity becomes EntityType 902 (staticId 901xxx,
        // JigsawPuzzleComponentData.isFinished == true). PoseDeleteBuild takes that into the bag
        // (proven MCP: 901650 → backpack entityType 902). There is also a short mask-complete
        // window on the 900xxx box (isFinished == false, IsJigsawPuzzleBoardFinished == true) —
        // collect either. PoseDeleteBuild is server-authoritative on ownership; when LevelEntity
        // ownerId is readable and foreign, skip; when unread, still attempt the take.
        private const float PuzzleCollectScanBackoffSeconds = 0.25f;   // idle: nothing to collect
        private const float PuzzleCollectPendingScanSeconds = 0.08f;  // waiting for just-cleared board to become takeable
        private const float PuzzleCollectBusyBackoffSeconds = 0.25f;  // PoseDeleteBuild in-flight — retry soon
        private const float PuzzleCollectTakeBackoffSeconds = 1.0f;   // after accepted take: wait BuildOptionRespond
        private const float PuzzleCollectPendingSeconds = 8.0f;       // prefer cleared board until 902 lands / take accepts
        private const float PuzzleCollectAwaitRespondSeconds = 1.0f;  // fixed hold before next take / next solve
        private const float PuzzleCollectBusyStuckSeconds = 2.0f;     // force-clear _optionType after this

        // Resolves CharacterProtocolManager.PoseDeleteBuild(uint) into the sand collect fields so that
        // TryInvokeSandPoseDeleteBuild can be reused; does not require the sand finish component class.
        private bool EnsurePuzzleCollectResolved(out string status)
        {
            status = "cached";
            if (this.sandCollectPoseDeleteMethod != IntPtr.Zero)
            {
                if (this.puzzleCollectOptionTypeField == IntPtr.Zero && this.sandCollectCharacterProtocolClass != IntPtr.Zero)
                {
                    this.puzzleCollectOptionTypeField = this.FindAuraMonoFieldOnHierarchy(
                        this.sandCollectCharacterProtocolClass, "_optionType");
                }

                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura api not ready";
                return false;
            }

            if (this.sandCollectCharacterProtocolClass == IntPtr.Zero)
            {
                for (int i = 0; i < SandCharacterProtocolTypeNames.Length; i++)
                {
                    this.sandCollectCharacterProtocolClass = this.FindAuraMonoClassByFullName(SandCharacterProtocolTypeNames[i]);
                    if (this.sandCollectCharacterProtocolClass != IntPtr.Zero)
                    {
                        break;
                    }
                }

                if (this.sandCollectCharacterProtocolClass == IntPtr.Zero)
                {
                    this.sandCollectCharacterProtocolClass = this.FindAuraMonoClassInImages(
                        "XDTDataAndProtocol.ProtocolService.GamePlay.Character", "CharacterProtocolManager",
                        SandAuraProtocolImages);
                }
            }

            if (this.sandCollectCharacterProtocolClass != IntPtr.Zero)
            {
                this.sandCollectPoseDeleteMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.sandCollectCharacterProtocolClass, "PoseDeleteBuild", 1);
                if (this.puzzleCollectOptionTypeField == IntPtr.Zero)
                {
                    this.puzzleCollectOptionTypeField = this.FindAuraMonoFieldOnHierarchy(
                        this.sandCollectCharacterProtocolClass, "_optionType");
                }
            }

            if (this.sandCollectPoseDeleteMethod == IntPtr.Zero)
            {
                status = "PoseDeleteBuild unresolved";
                return false;
            }

            status = "resolved";
            return true;
        }

        private delegate void PuzzleMonoFieldStaticSetValueDelegate(IntPtr vtable, IntPtr field, IntPtr valuePtr);
        private static PuzzleMonoFieldStaticSetValueDelegate puzzleMonoFieldStaticSetValue;

        // CharacterProtocolManager._optionType > 0 blocks every PoseDeleteBuild. Force to 0 when
        // BuildOptionRespondEvent never arrives (live MCP: busy forever after a few takes).
        private unsafe bool TryClearStuckPuzzleBuildOption(out string detail)
        {
            detail = string.Empty;
            if (this.puzzleCollectOptionTypeField == IntPtr.Zero
                || this.sandCollectCharacterProtocolClass == IntPtr.Zero
                || auraMonoClassVtable == null
                || auraMonoFieldStaticGetValue == null
                || this.auraMonoRootDomain == IntPtr.Zero
                || !AuraMonoStaticFieldReadsAllowed())
            {
                detail = "no _optionType static access";
                return false;
            }

            try
            {
                IntPtr vtable = auraMonoClassVtable(this.auraMonoRootDomain, this.sandCollectCharacterProtocolClass);
                if (vtable == IntPtr.Zero)
                {
                    detail = "no vtable";
                    return false;
                }

                int current = 0;
                auraMonoFieldStaticGetValue(vtable, this.puzzleCollectOptionTypeField, (IntPtr)(&current));
                if (current == 0)
                {
                    detail = "_optionType already 0";
                    this.puzzleCollectAwaitRespondUntil = 0f;
                    this.puzzleCollectBusySince = 0f;
                    return true;
                }

                if (puzzleMonoFieldStaticSetValue == null)
                {
                    IntPtr monoModule = this.GetAuraMonoModuleHandle();
                    if (monoModule != IntPtr.Zero)
                    {
                        puzzleMonoFieldStaticSetValue = this.GetAuraMonoExport<PuzzleMonoFieldStaticSetValueDelegate>(
                            monoModule, "mono_field_static_set_value");
                    }
                }

                if (puzzleMonoFieldStaticSetValue == null)
                {
                    detail = "mono_field_static_set_value missing (was " + current + ")";
                    return false;
                }

                int zero = 0;
                puzzleMonoFieldStaticSetValue(vtable, this.puzzleCollectOptionTypeField, (IntPtr)(&zero));
                int after = -1;
                auraMonoFieldStaticGetValue(vtable, this.puzzleCollectOptionTypeField, (IntPtr)(&after));
                detail = "cleared _optionType " + current + "→" + after;
                return after == 0;
            }
            catch (Exception ex)
            {
                detail = "clear ex: " + ex.Message;
                return false;
            }
        }

        private bool TryPuzzlePoseDeleteBuild(uint netId, out string status)
        {
            if (!this.TryInvokeSandPoseDeleteBuild(netId, out status))
            {
                return false;
            }

            this.puzzleCollectAwaitRespondUntil = Time.unscaledTime + PuzzleCollectAwaitRespondSeconds;
            this.puzzleCollectBusySince = 0f;
            return true;
        }

        private void UpdatePuzzleAutoCollect()
        {
            if (!this.puzzleAutoCollect)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.puzzleCollectNextAt)
            {
                return;
            }

            if (this.puzzlePendingCollectNetId != 0U && now > this.puzzlePendingCollectUntil)
            {
                this.puzzlePendingCollectNetId = 0U;
                this.puzzlePendingCollectUntil = 0f;
            }

            if (!this.TryCollectOneSolvedPuzzleBoard(out bool tookOne, out bool busy, out string status))
            {
                this.puzzleLastCollectStatus = status;
                this.puzzleCollectNextAt = now + (this.puzzlePendingCollectNetId != 0U
                    ? PuzzleCollectPendingScanSeconds
                    : PuzzleCollectScanBackoffSeconds);
                return;
            }

            this.puzzleLastCollectStatus = status;
            if (!string.IsNullOrEmpty(status)
                && !string.Equals(status, this.puzzleLastCollectLoggedStatus, StringComparison.Ordinal))
            {
                this.puzzleLastCollectLoggedStatus = status;
                FeatureLog.Life("PuzzleNet", "collect: " + status);
            }

            if (tookOne)
            {
                this.puzzleCollectBusySince = 0f;
                this.puzzleCollectNextAt = now + PuzzleCollectTakeBackoffSeconds;
            }
            else if (busy)
            {
                if (this.puzzleCollectBusySince <= 0f)
                {
                    this.puzzleCollectBusySince = now;
                }

                if ((now - this.puzzleCollectBusySince) >= PuzzleCollectBusyStuckSeconds
                    || (this.puzzleCollectAwaitRespondUntil > 0f && now >= this.puzzleCollectAwaitRespondUntil))
                {
                    if (this.EnsurePuzzleCollectResolved(out _)
                        && this.TryClearStuckPuzzleBuildOption(out string clearDetail))
                    {
                        this.puzzleLastCollectStatus = "build-option gate stuck — " + clearDetail + "; retrying";
                        this.puzzleLastCollectLoggedStatus = string.Empty;
                        FeatureLog.Life("PuzzleNet", "collect: " + this.puzzleLastCollectStatus);
                        this.puzzleCollectAwaitRespondUntil = 0f;
                        this.puzzleCollectBusySince = 0f;
                        this.puzzleCollectNextAt = now + 0.1f;
                        return;
                    }
                }

                this.puzzleCollectNextAt = now + PuzzleCollectBusyBackoffSeconds;
            }
            else
            {
                this.puzzleCollectBusySince = 0f;
                if (this.puzzlePendingCollectNetId != 0U)
                {
                    this.puzzleCollectNextAt = now + PuzzleCollectPendingScanSeconds;
                }
                else
                {
                    this.puzzleCollectNextAt = now + PuzzleCollectScanBackoffSeconds;
                }
            }
        }

        // tookOne=false + busy=false: nothing to collect. busy=true: PoseDeleteBuild in flight — retry soon.
        private unsafe bool TryCollectOneSolvedPuzzleBoard(out bool tookOne, out bool busy, out string status)
        {
            tookOne = false;
            busy = false;
            status = string.Empty;
            List<uint> pins = new List<uint>();
            try
            {
                if (!AuraMonoPinningAvailable)
                {
                    status = "pinning unavailable";
                    return false;
                }

                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    status = "aura api not ready";
                    return false;
                }

                if (!this.EnsurePuzzleCollectResolved(out string resolveStatus))
                {
                    status = "collect types not resolved: " + resolveStatus;
                    return false;
                }

                if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
                {
                    status = "GetComponents not ready";
                    return false;
                }

                if (this.puzzleAuraBoardComponentClass == IntPtr.Zero)
                {
                    for (int i = 0; i < PuzzleAuraBoardComponentTypeNames.Length && this.puzzleAuraBoardComponentClass == IntPtr.Zero; i++)
                    {
                        this.puzzleAuraBoardComponentClass = this.FindAuraMonoClassByFullName(PuzzleAuraBoardComponentTypeNames[i]);
                    }
                }

                if (this.puzzleAuraBoardComponentClass == IntPtr.Zero)
                {
                    status = "JigsawPuzzleComponent class unavailable";
                    return false;
                }

                if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos))
                {
                    status = "no player position";
                    return false;
                }

                if (!this.TryAuraMonoGetComponentObjects(this.puzzleAuraBoardComponentClass, out List<IntPtr> comps, pins)
                    || comps == null || comps.Count == 0)
                {
                    status = "no JigsawPuzzleComponent instances";
                    return true;
                }

                // Prefer the just-cleared board: walk it first so a yard full of old 902 frames
                // does not steal the take slot / backoff before the board we just solved.
                uint pendingNetId = this.puzzlePendingCollectNetId;
                if (pendingNetId != 0U)
                {
                    for (int p = 0; p < comps.Count; p++)
                    {
                        IntPtr pendingComp = comps[p];
                        if (pendingComp == IntPtr.Zero
                            || !this.TryGetMonoObjectMember(pendingComp, "entity", out IntPtr pendingEntity)
                            || pendingEntity == IntPtr.Zero
                            || !this.TryGetAuraMonoEntityNetId(pendingEntity, out uint pendingId)
                            || pendingId != pendingNetId)
                        {
                            continue;
                        }

                        // Swap pending to front for the main loop.
                        if (p != 0)
                        {
                            comps[p] = comps[0];
                            comps[0] = pendingComp;
                        }

                        break;
                    }
                }

                float radius = this.GetPuzzleScanRadiusMeters();
                int inRange = 0;
                int solved = 0;
                int notOwned = 0;
                int ownerUnread = 0;
                int unreadable = 0;
                IntPtr systemObj = IntPtr.Zero;
                bool systemResolved = false;
                uint selfNetId = 0U;
                bool selfResolved = false;
                IntPtr* argSlot = stackalloc IntPtr[1];
                for (int i = 0; i < comps.Count; i++)
                {
                    IntPtr compObj = comps[i];
                    if (compObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(compObj, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero
                        || !this.TryGetAuraMonoEntityNetId(entityObj, out uint netId) || netId == 0U
                        || !this.TryGetAuraMonoEntityPosition(entityObj, out Vector3 pos))
                    {
                        unreadable++;
                        continue;
                    }

                    bool isPending = pendingNetId != 0U && netId == pendingNetId;
                    if (!isPending && Vector3.Distance(playerPos, pos) > radius)
                    {
                        continue;
                    }

                    inRange++;
                    if (!this.TryReadPuzzleBoardComponentData(compObj, out int candidateStaticId, out bool isFinished))
                    {
                        unreadable++;
                        continue;
                    }

                    // Collectable:
                    //  1) componentData.isFinished (902 frame flag), OR
                    //  2) staticId is a finished-frame row (901xxx / TableJigsawpuzzlefinisheds) —
                    //     the old gate required PuzzleIsUnfinishedBox (900xxx only), so after the
                    //     server flipped staticId to 901xxx with isFinished still false / unread,
                    //     boards sat on the floor forever (MCP: Completed UI, no PoseDelete), OR
                    //  3) mask-complete unfinished box (IsJigsawPuzzleBoardFinished), OR pending.
                    bool collectable = isFinished || this.PuzzleIsFinishedFrame(candidateStaticId);
                    if (!collectable)
                    {
                        if (!systemResolved)
                        {
                            systemResolved = true;
                            if (this.TryResolveAuraMonoModule(PuzzleAuraSystemTypeName, out systemObj) && systemObj != IntPtr.Zero)
                            {
                                pins.Add(AuraMonoPinNew(systemObj));
                                if (this.puzzleAuraIsBoardFinishedMethod == IntPtr.Zero)
                                {
                                    IntPtr systemClass = auraMonoObjectGetClass(systemObj);
                                    if (systemClass != IntPtr.Zero)
                                    {
                                        this.puzzleAuraIsBoardFinishedMethod = this.FindAuraMonoMethodOnHierarchy(systemClass, "IsJigsawPuzzleBoardFinished", 1);
                                    }
                                }
                            }
                        }

                        if (systemObj == IntPtr.Zero || this.puzzleAuraIsBoardFinishedMethod == IntPtr.Zero)
                        {
                            if (!isPending)
                            {
                                status = "JigsawPuzzleSystem.IsJigsawPuzzleBoardFinished unavailable";
                                return false;
                            }
                        }
                        else
                        {
                            IntPtr exc = IntPtr.Zero;
                            uint arg = netId;
                            argSlot[0] = (IntPtr)(&arg);
                            IntPtr boxed = auraMonoRuntimeInvoke(this.puzzleAuraIsBoardFinishedMethod, systemObj, (IntPtr)argSlot, ref exc);
                            if (exc != IntPtr.Zero || boxed == IntPtr.Zero || !this.TryUnboxMonoBoolean(boxed, out bool boardDone))
                            {
                                if (!isPending)
                                {
                                    unreadable++;
                                    continue;
                                }
                            }
                            else
                            {
                                collectable = boardDone;
                            }
                        }

                        // Pending netId: still attempt PoseDeleteBuild once (server may accept
                        // before isFinished / finished-table row is visible client-side).
                        if (!collectable && isPending)
                        {
                            collectable = true;
                        }
                    }

                    if (!collectable)
                    {
                        continue;
                    }

                    solved++;
                    if (!selfResolved)
                    {
                        selfResolved = true;
                        this.TryResolveSelfPlayerNetId(out selfNetId);
                    }

                    // Owner gate: only skip when ownerId is positively known and foreign.
                    // TryHomelandFarmTryReadOwnerId often misses puzzle boards (crop/level-object
                    // cache); treating that miss as notOwned left solved boards on the floor forever.
                    uint ownerId = 0U;
                    bool ownerKnown = selfNetId != 0U
                        && this.TryHomelandFarmTryReadOwnerId(netId, out ownerId)
                        && ownerId != 0U;
                    if (ownerKnown && ownerId != selfNetId)
                    {
                        notOwned++;
                        if (isPending)
                        {
                            this.puzzlePendingCollectNetId = 0U;
                            this.puzzlePendingCollectUntil = 0f;
                        }

                        continue;
                    }

                    if (!ownerKnown)
                    {
                        ownerUnread++;
                    }

                    if (this.TryPuzzlePoseDeleteBuild(netId, out string takeStatus))
                    {
                        tookOne = true;
                        this.puzzleCollectedCount++;
                        if (isPending || netId == this.puzzlePendingCollectNetId)
                        {
                            this.puzzlePendingCollectNetId = 0U;
                            this.puzzlePendingCollectUntil = 0f;
                        }

                        status = "collected #" + this.puzzleCollectedCount + " netId=" + netId
                                 + " staticId=" + candidateStaticId
                                 + (isFinished ? " fin" : string.Empty)
                                 + (this.PuzzleIsFinishedFrame(candidateStaticId) ? " frame902" : string.Empty)
                                 + (isPending ? " pending" : string.Empty)
                                 + (ownerKnown ? string.Empty : " ownerUnread")
                                 + " (" + takeStatus + ")";
                        this.PuzzleLog("collect: " + status);
                        this.AddMenuNotification(this.L("Puzzle collected"), new Color(0.45f, 1f, 0.55f));
                        return true;
                    }

                    // Prior build option still in flight — short busy retry, not the 1.5 s take gate.
                    status = "take not ready: " + takeStatus;
                    busy = true;
                    return true;
                }

                status = "no own solved board in " + radius.ToString("F1") + "m (components=" + comps.Count
                         + " inRange=" + inRange + " solved=" + solved + " notOwned=" + notOwned
                         + " ownerUnread=" + ownerUnread + " unreadable=" + unreadable
                         + (pendingNetId != 0U ? " pending=" + pendingNetId : string.Empty) + ")";
                return true;
            }
            catch (Exception ex)
            {
                status = "collect scan exception: " + ex.Message;
                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
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
