using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        // ===== Auto Sand Sculpture ======================================================
        // Pure-protocol solver for the beach sand-sculpting QTE. The whole swing QTE is
        // client-authoritative (SandSwingTrackCellModel simulates the needle locally and
        // only reports totals), so the mod skips the UI entirely:
        //   1. scan SandSculpturesComponent views -> nearest sand base (owner entity netId)
        //   2. CanStartSandSculpture(base) + StartMakingSandSculpture(base)
        //   3. rounds = TableData.GetSandrough(roughStaticId).interval.Length
        //   4. FinishMakingSandSculpture(base, successCount, perfectCount)
        //      (all-perfect when FeatureOpenEnum.SandSculptingQuality=300041 is open,
        //       otherwise the single no-perfect round the vanilla QTE would produce)
        //   5. server spawns the rough -> scan SandSculptureRoughComponent for
        //      ownerNetId == self, read options[] from its bound component data
        //   6. ChooseSandSculptureProduct(roughNetId, productId from options)
        // All game access is AuraMono-only: the protocol manager / components live in
        // Mono-only images (managed FindLoadedType is dead for them on this build).

        private const bool MasterLogSandSculpture = true; // verbose logging for live debugging
        private const float SandSculptureResolveRetrySeconds = 2f;
        private const float SandApiTickIntervalSeconds = 0.1f;
        private const float SandApiRetryBackoffSeconds = 1.5f;
        private const float SandApiScanBackoffSeconds = 3f;
        private const float SandApiWaitRoughPollSeconds = 0.5f;
        private const float SandApiWaitRoughTimeoutSeconds = 12f;
        private const float SandApiPostChooseBackoffSeconds = 1.5f;
        private const float SandApiCloseDialogPollSeconds = 0.25f;
        private const float SandApiCloseDialogTimeoutSeconds = 3f;
        private const float SandApiPlaceBaseBackoffSeconds = 2.5f;
        private const int SandPlaceBaseMaxAttempts = 3;
        private const float SandCollectScanBackoffSeconds = 2.5f;   // idle: no finished sculpture found
        private const float SandCollectTakeBackoffSeconds = 1.5f;   // after a take: wait for the server BuildOptionRespond
        private const int SandCollectMaxScan = 4096;
        private const float SandBaseScanRadius = 60f;
        private const int SandFeatureOpenQualityId = 300041; // FeatureOpenEnum.SandSculptingQuality
        // SandfinishedItem ids 1..14 are the real sculptures; ids >14 are decoy answers that
        // produce sandfinished 600299 (a spoiled/failure sculpture). Verified vs cn_tables.db.
        private const int SandValidModelMaxId = 14;
        private const int SandMaxBaseFailures = 3;
        private const int SandMaxOptionCount = 64;

        private enum SandApiState
        {
            FindBase = 0,
            StartSculpt = 1,
            SendFinish = 2,
            WaitRough = 3,
            CloseDialog = 4,
            PlaceBase = 5
        }

        private bool autoSandEnabled;
        private bool autoSandWasEnabled;
        // When FindBase turns up nothing, place a fresh base from the backpack (direct
        // BuildBatchOperationProCommand; no build-mode UI) in front of the player, then rescan.
        // OFF by default: only run this (and the backpack/module scans it drives) while the user
        // wants it — polling BackPackSystem every tick regardless of world state caught a mono
        // teardown window and AV'd (2026-07-09).
        private bool sandAutoPlaceBase;
        private int sandPlaceAttempts;
        // Auto-collect finished sculptures (own, freshly-made) into the backpack via
        // CharacterProtocolManager.PoseDeleteBuild — runs independently of the sculpt FSM. OFF by
        // default (same continuous-scan crash reason as sandAutoPlaceBase).
        private bool sandAutoCollect;
        private float sandCollectNextAt;
        private int sandCollectedCount;
        private string sandLastCollectStatus = string.Empty;
        private SandApiState sandApiState = SandApiState.FindBase;
        private float sandApiNextActionAt;
        private float sandApiRoughDeadlineAt;
        private float sandApiDialogDeadlineAt;
        private uint sandTargetBaseNetId;
        private uint sandLastBaseNetId;
        private int sandTargetRoughStaticId;
        private int sandQteRoundCount;
        private bool sandQualityFeatureOpen;
        private int sandPlannedSuccessCount;
        private int sandPlannedPerfectCount;
        private int sandSculpturesDone;
        private float sandFinishDelaySeconds = 3f;
        private readonly Dictionary<uint, int> sandBaseFailCounts = new Dictionary<uint, int>();
        private readonly HashSet<uint> sandBaseBlacklist = new HashSet<uint>();

        private string sandResolveStatus = "not resolved";
        private float sandNextResolveAt;
        private string sandLastActionStatus = string.Empty;
        private string sandLastLoggedStatus = string.Empty;
        private string sandLastScanStatus = string.Empty;
        private string sandLastRoughStatus = string.Empty;
        private string sandLastPlaceStatus = string.Empty;

        // Class/method descriptors are image-lifetime and safe to cache raw; live object
        // pointers are never cached across frames (moving sgen GC).
        private IntPtr sandAuraProtocolClass;
        private IntPtr sandAuraStartMethod;
        private IntPtr sandAuraFinishMethod;
        private IntPtr sandAuraChooseMethod;
        private IntPtr sandAuraTableDataClass;
        private IntPtr sandAuraGetSandroughMethod;
        private IntPtr sandAuraBaseComponentClass;
        private IntPtr sandAuraRoughComponentClass;
        private IntPtr sandAuraFeatureOpenMethod;
        private IntPtr sandAuraDialogPanelClass;
        private IntPtr sandCollectFinishComponentClass; // SandFinishComponent
        private IntPtr sandCollectCharacterProtocolClass; // CharacterProtocolManager
        private IntPtr sandCollectPoseDeleteMethod;     // PoseDeleteBuild(uint) — 1 param

        private static readonly string[] SandAuraProtocolTypeNames =
        {
            "XDTDataAndProtocol.ProtocolService.SandSculpture.SandSculptureProtocolManager",
            "SandSculptureProtocolManager"
        };

        private static readonly string[] SandAuraProtocolImages =
        {
            "XDTDataAndProtocol",
            "XDTDataAndProtocol.dll",
            "Client",
            "Client.dll",
            "EcsClient",
            "EcsClient.dll"
        };

        private static readonly string[] SandAuraBaseComponentTypeNames =
        {
            "XDTLevelAndEntity.Gameplay.Component.SandSculpturesComponent",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.SandSculpturesComponent",
            "SandSculpturesComponent"
        };

        private static readonly string[] SandAuraRoughComponentTypeNames =
        {
            "XDTLevelAndEntity.Gameplay.Component.SandSculptureRoughComponent",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.SandSculptureRoughComponent",
            "SandSculptureRoughComponent"
        };

        private static readonly string[] SandAuraFinishComponentTypeNames =
        {
            "XDTLevelAndEntity.Gameplay.Component.SandFinishComponent",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.SandFinishComponent",
            "SandFinishComponent"
        };

        private static readonly string[] SandCharacterProtocolTypeNames =
        {
            "XDTDataAndProtocol.ProtocolService.GamePlay.Character.CharacterProtocolManager",
            "CharacterProtocolManager"
        };

        // XDTGame.UI.Panel.DialogueSimplePanel — the vanilla "choose model" dialog.
        private static readonly string[] SandDialogPanelImages =
        {
            "XDTGameUI", "XDTGameUI.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll"
        };

        private void SandLog(string message)
        {
            if (!MasterLogSandSculpture)
            {
                return;
            }

            ModLogger.Msg("[SandSculpture] " + message);
        }

        // Deduped status logger: always writes to the log (independent of MasterLog), but a
        // stuck per-tick state (backoff/idle scans) is logged once instead of spamming.
        private void SandLogStatus(string message)
        {
            if (string.IsNullOrEmpty(message) || message == this.sandLastLoggedStatus)
            {
                return;
            }

            this.sandLastLoggedStatus = message;
            ModLogger.Msg("[SandSculpture] " + message);
        }

        private void SandSetAction(string message)
        {
            this.sandLastActionStatus = message;
            this.SandLogStatus(message);
        }

        private void ProcessSandSculptureOnUpdate()
        {
            if (this.autoSandEnabled && !this.autoSandWasEnabled)
            {
                this.ResetSandApiProgress(clearBlacklist: true);
                this.SandLogStatus("enabled");
            }
            else if (!this.autoSandEnabled && this.autoSandWasEnabled)
            {
                this.SandLogStatus("disabled");
            }

            this.autoSandWasEnabled = this.autoSandEnabled;

            if (this.autoSandEnabled)
            {
                this.RunAutoSandSculptureApi();
            }

            // Auto-collect is independent of the sculpt FSM (it catches finished sculptures made
            // by the auto-loop or by hand), so it runs on its own throttle.
            if (this.sandAutoCollect)
            {
                this.RunSandAutoCollect();
            }
        }

        private void ResetSandApiProgress(bool clearBlacklist)
        {
            this.sandApiState = SandApiState.FindBase;
            this.sandApiNextActionAt = 0f;
            this.sandApiRoughDeadlineAt = 0f;
            this.sandApiDialogDeadlineAt = 0f;
            this.sandTargetBaseNetId = 0;
            this.sandTargetRoughStaticId = 0;
            this.sandQteRoundCount = 0;
            this.sandPlannedSuccessCount = 0;
            this.sandPlannedPerfectCount = 0;
            this.sandPlaceAttempts = 0;
            this.sandLastLoggedStatus = string.Empty;
            if (clearBlacklist)
            {
                this.sandBaseFailCounts.Clear();
                this.sandBaseBlacklist.Clear();
            }
        }

        // Marks the current base attempt as failed; SandMaxBaseFailures strikes -> blacklist
        // for this enable-session so a server-rejected base cannot spin the loop forever.
        private void SandRegisterBaseFailure(uint baseNetId, string reason)
        {
            if (baseNetId != 0)
            {
                this.sandBaseFailCounts.TryGetValue(baseNetId, out int fails);
                fails++;
                this.sandBaseFailCounts[baseNetId] = fails;
                this.SandLogStatus("base " + baseNetId + " failure " + fails + "/" + SandMaxBaseFailures + ": " + reason);
                if (fails >= SandMaxBaseFailures)
                {
                    this.sandBaseBlacklist.Add(baseNetId);
                    this.AddMenuNotification("Sand base " + baseNetId + " blacklisted: " + reason, new Color(1f, 0.55f, 0.55f));
                }
            }

            this.sandTargetBaseNetId = 0;
            this.sandTargetRoughStaticId = 0;
            this.sandQteRoundCount = 0;
            this.sandApiState = SandApiState.FindBase;
        }

        private bool SandSculptureIsResolved()
        {
            return this.sandAuraFinishMethod != IntPtr.Zero
                   && this.sandAuraStartMethod != IntPtr.Zero
                   && this.sandAuraChooseMethod != IntPtr.Zero;
        }

        private bool EnsureSandSculptureResolved(out string status)
        {
            status = this.sandResolveStatus;
            if (this.SandSculptureIsResolved())
            {
                status = "cached";
                return true;
            }

            float now = Time.unscaledTime;
            if (now < this.sandNextResolveAt)
            {
                return false;
            }

            this.sandNextResolveAt = now + SandSculptureResolveRetrySeconds;

            try
            {
                this.ResolveAuraFarmRuntimeMethods();
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    status = "aura api not ready";
                    this.sandResolveStatus = status;
                    return false;
                }

                if (this.sandAuraProtocolClass == IntPtr.Zero)
                {
                    for (int i = 0; i < SandAuraProtocolTypeNames.Length; i++)
                    {
                        this.sandAuraProtocolClass = this.FindAuraMonoClassByFullName(SandAuraProtocolTypeNames[i]);
                        if (this.sandAuraProtocolClass != IntPtr.Zero)
                        {
                            break;
                        }
                    }
                }

                if (this.sandAuraProtocolClass == IntPtr.Zero)
                {
                    this.sandAuraProtocolClass = this.FindAuraMonoClassInImages(
                        "XDTDataAndProtocol.ProtocolService.SandSculpture",
                        "SandSculptureProtocolManager",
                        SandAuraProtocolImages);
                }

                if (this.sandAuraProtocolClass != IntPtr.Zero)
                {
                    if (this.sandAuraStartMethod == IntPtr.Zero)
                    {
                        this.sandAuraStartMethod = this.FindAuraMonoMethodOnHierarchy(
                            this.sandAuraProtocolClass, "StartMakingSandSculpture", 1);
                    }

                    if (this.sandAuraFinishMethod == IntPtr.Zero)
                    {
                        this.sandAuraFinishMethod = this.FindAuraMonoMethodOnHierarchy(
                            this.sandAuraProtocolClass, "FinishMakingSandSculpture", 3);
                    }

                    if (this.sandAuraChooseMethod == IntPtr.Zero)
                    {
                        this.sandAuraChooseMethod = this.FindAuraMonoMethodOnHierarchy(
                            this.sandAuraProtocolClass, "ChooseSandSculptureProduct", 2);
                    }
                }

                if (this.sandAuraTableDataClass == IntPtr.Zero)
                {
                    // TableData lives in the GLOBAL namespace on the EcsClient image.
                    this.sandAuraTableDataClass = this.FindAuraMonoTableDataClass();
                    if (this.sandAuraTableDataClass == IntPtr.Zero)
                    {
                        this.sandAuraTableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
                    }
                }

                if (this.sandAuraTableDataClass != IntPtr.Zero && this.sandAuraGetSandroughMethod == IntPtr.Zero)
                {
                    // GetSandrough(int id, bool needException = false) -> 2 params.
                    this.sandAuraGetSandroughMethod = this.FindAuraMonoMethodOnHierarchy(
                        this.sandAuraTableDataClass, "GetSandrough", 2);
                    if (this.sandAuraGetSandroughMethod == IntPtr.Zero)
                    {
                        this.sandAuraGetSandroughMethod = this.FindAuraMonoMethodOnHierarchy(
                            this.sandAuraTableDataClass, "GetSandrough", 1);
                    }
                }

                if (this.sandAuraBaseComponentClass == IntPtr.Zero)
                {
                    for (int i = 0; i < SandAuraBaseComponentTypeNames.Length; i++)
                    {
                        this.sandAuraBaseComponentClass = this.FindAuraMonoClassByFullName(SandAuraBaseComponentTypeNames[i]);
                        if (this.sandAuraBaseComponentClass != IntPtr.Zero)
                        {
                            break;
                        }
                    }
                }

                if (this.sandAuraRoughComponentClass == IntPtr.Zero)
                {
                    for (int i = 0; i < SandAuraRoughComponentTypeNames.Length; i++)
                    {
                        this.sandAuraRoughComponentClass = this.FindAuraMonoClassByFullName(SandAuraRoughComponentTypeNames[i]);
                        if (this.sandAuraRoughComponentClass != IntPtr.Zero)
                        {
                            break;
                        }
                    }
                }

                bool ok = this.SandSculptureIsResolved();
                status = (ok ? "resolved" : "unavailable")
                         + " protocol=0x" + this.sandAuraProtocolClass.ToInt64().ToString("X")
                         + " start=" + (this.sandAuraStartMethod != IntPtr.Zero)
                         + " finish=" + (this.sandAuraFinishMethod != IntPtr.Zero)
                         + " choose=" + (this.sandAuraChooseMethod != IntPtr.Zero)
                         + " getSandrough=" + (this.sandAuraGetSandroughMethod != IntPtr.Zero)
                         + " baseComp=0x" + this.sandAuraBaseComponentClass.ToInt64().ToString("X")
                         + " roughComp=0x" + this.sandAuraRoughComponentClass.ToInt64().ToString("X");
                this.sandResolveStatus = status;
                this.SandLogStatus("resolve: " + status);
                return ok;
            }
            catch (Exception ex)
            {
                status = "resolve error: " + ex.Message;
                this.sandResolveStatus = status;
                this.SandLogStatus(status);
                return false;
            }
        }

        private void RunAutoSandSculptureApi()
        {
            float now = Time.unscaledTime;
            if (now < this.sandApiNextActionAt)
            {
                return;
            }

            this.sandApiNextActionAt = now + SandApiTickIntervalSeconds;

            if (!this.EnsureSandSculptureResolved(out string resolveStatus))
            {
                this.SandSetAction("resolving types: " + resolveStatus);
                this.sandApiNextActionAt = now + SandApiRetryBackoffSeconds;
                return;
            }

            switch (this.sandApiState)
            {
                case SandApiState.FindBase:
                    this.SandTickFindBase(now);
                    break;
                case SandApiState.StartSculpt:
                    this.SandTickStartSculpt(now);
                    break;
                case SandApiState.SendFinish:
                    this.SandTickSendFinish(now);
                    break;
                case SandApiState.WaitRough:
                    this.SandTickWaitRough(now);
                    break;
                case SandApiState.CloseDialog:
                    this.SandTickCloseDialog(now);
                    break;
                case SandApiState.PlaceBase:
                    this.SandTickPlaceBase(now);
                    break;
            }
        }

        private void SandTickFindBase(float now)
        {
            if (!this.TryFindNearestSandBase(out uint baseNetId, out int roughStaticId, out string scanStatus))
            {
                this.sandLastScanStatus = scanStatus;
                // No base in range: optionally place one from the backpack, else keep scanning.
                if (this.sandAutoPlaceBase && this.sandPlaceAttempts < SandPlaceBaseMaxAttempts)
                {
                    this.SandSetAction("no base -> placing from backpack (attempt " + (this.sandPlaceAttempts + 1) + "/" + SandPlaceBaseMaxAttempts + ")");
                    this.sandApiState = SandApiState.PlaceBase;
                    this.sandApiNextActionAt = now;
                    return;
                }

                this.SandSetAction("no base: " + scanStatus);
                this.sandApiNextActionAt = now + SandApiScanBackoffSeconds;
                return;
            }

            this.sandLastScanStatus = scanStatus;
            this.sandPlaceAttempts = 0; // a base exists now — reset the place-attempt guard
            this.sandTargetBaseNetId = baseNetId;
            this.sandLastBaseNetId = baseNetId;
            this.sandTargetRoughStaticId = roughStaticId;
            this.sandApiState = SandApiState.StartSculpt;
            this.SandSetAction("base found netId=" + baseNetId + " roughStaticId=" + roughStaticId + " (" + scanStatus + ")");
        }

        private void SandTickStartSculpt(float now)
        {
            // Round count comes from the rough's QTE config table; without it the finish
            // counts would be a guess the server may reject.
            if (!this.TryGetSandQteRoundCount(this.sandTargetRoughStaticId, out int rounds, out string roundStatus))
            {
                this.SandSetAction("rounds unavailable: " + roundStatus);
                this.SandRegisterBaseFailure(this.sandTargetBaseNetId, "rounds unavailable: " + roundStatus);
                this.sandApiNextActionAt = now + SandApiRetryBackoffSeconds;
                return;
            }

            this.sandQteRoundCount = rounds;
            this.sandQualityFeatureOpen = this.IsSandQualityFeatureOpen(out string qualityStatus);

            // Mirror what the vanilla QTE reports: with SandSculptingQuality open every
            // round can be Perfect (success=0, perfect=rounds); with it closed the track
            // ends after one no-perfect round (success=1, perfect=0).
            if (this.sandQualityFeatureOpen)
            {
                this.sandPlannedSuccessCount = 0;
                this.sandPlannedPerfectCount = rounds;
            }
            else
            {
                this.sandPlannedSuccessCount = 1;
                this.sandPlannedPerfectCount = 0;
            }

            this.SandLog("config rounds=" + rounds + " qualityOpen=" + this.sandQualityFeatureOpen
                         + " (" + qualityStatus + ") -> plan success=" + this.sandPlannedSuccessCount
                         + " perfect=" + this.sandPlannedPerfectCount);

            // Pure protocol: Start + Finish only. We never send CanStartSandSculpture (PreStart) —
            // its PreStartSandSculptureEvent(Ok) makes the vanilla client join the sculpt mode and
            // fire a duplicate Start (in-game "Error" tip + a stuck QTE track). The server accepts
            // Start+Finish without it.
            bool startOk = this.TryInvokeSandUIntArgMethod(this.sandAuraStartMethod, "StartMakingSandSculpture", this.sandTargetBaseNetId, out string startStatus);
            this.SandLog("start: " + startStatus);
            if (!startOk)
            {
                this.SandSetAction("start failed: " + startStatus);
                this.SandRegisterBaseFailure(this.sandTargetBaseNetId, "start failed");
                this.sandApiNextActionAt = now + SandApiRetryBackoffSeconds;
                return;
            }

            this.sandApiState = SandApiState.SendFinish;
            float delay = Mathf.Max(0.2f, this.sandFinishDelaySeconds);
            this.SandSetAction("sculpting base=" + this.sandTargetBaseNetId + ", finish in " + delay.ToString("F1") + "s");
            this.sandApiNextActionAt = now + delay;
        }

        private void SandTickSendFinish(float now)
        {
            if (!this.TryInvokeSandFinish(this.sandTargetBaseNetId, this.sandPlannedSuccessCount, this.sandPlannedPerfectCount, out string finishStatus))
            {
                this.SandSetAction("finish failed: " + finishStatus);
                this.SandRegisterBaseFailure(this.sandTargetBaseNetId, "finish failed");
                this.sandApiNextActionAt = now + SandApiRetryBackoffSeconds;
                return;
            }

            this.SandSetAction("finish sent (" + finishStatus + "), waiting for rough...");
            this.sandApiState = SandApiState.WaitRough;
            this.sandApiRoughDeadlineAt = now + SandApiWaitRoughTimeoutSeconds;
            this.sandApiNextActionAt = now + SandApiWaitRoughPollSeconds;
        }

        private void SandTickWaitRough(float now)
        {
            if (this.TryFindOwnSandRough(out uint roughNetId, out int[] options, out string roughStatus))
            {
                this.sandLastRoughStatus = roughStatus;
                if (options == null || options.Length == 0)
                {
                    // Rough entity exists but the option list has not synced yet — keep
                    // polling until the deadline.
                    this.SandSetAction("rough " + roughNetId + " found, waiting for options (" + roughStatus + ")");
                }
                else
                {
                    int optionIndex = this.SandPickModelOptionIndex(options);
                    int productId = options[optionIndex];
                    this.SandLog("rough " + roughNetId + " options=[" + string.Join(",", options) + "] pick index=" + optionIndex
                                 + " productId=" + productId);
                    if (this.TryInvokeSandChoose(roughNetId, productId, out string chooseStatus))
                    {
                        this.TryClearSelfSandRoughData(out string clearStatus);
                        this.SandLog("clear SandRoughData: " + clearStatus);
                        this.sandSculpturesDone++;
                        this.sandBaseFailCounts.Remove(this.sandTargetBaseNetId);
                        this.SandSetAction("sculpture #" + this.sandSculpturesDone + " complete: base=" + this.sandTargetBaseNetId
                                           + " model=" + productId + " (" + chooseStatus + ")");
                        this.AddMenuNotification(this.L("Sand sculpture complete"), new Color(0.45f, 1f, 0.55f));
                        this.sandTargetBaseNetId = 0;
                        this.sandTargetRoughStaticId = 0;
                        // The rough spawned next to the player, so the vanilla FSM opened the
                        // DialogueSimplePanel "choose model" dialog. Our protocol Choose bypassed
                        // its callback, so nothing closes it — sweep it shut.
                        this.sandApiState = SandApiState.CloseDialog;
                        this.sandApiDialogDeadlineAt = now + SandApiCloseDialogTimeoutSeconds;
                        this.sandApiNextActionAt = now;
                        return;
                    }

                    this.SandSetAction("choose failed: " + chooseStatus);
                    // Do not fail the base for a choose error: the sculpture itself is
                    // already made; the rough expires server-side on its own.
                    this.sandApiState = SandApiState.FindBase;
                    this.sandApiNextActionAt = now + SandApiRetryBackoffSeconds;
                    return;
                }
            }
            else
            {
                this.sandLastRoughStatus = roughStatus;
                this.SandSetAction("waiting for rough: " + roughStatus);
            }

            if (now >= this.sandApiRoughDeadlineAt)
            {
                // Finish was likely rejected (stamina / not started / stale base).
                this.SandSetAction("rough never appeared (finish rejected? stamina?)");

                this.SandRegisterBaseFailure(this.sandTargetBaseNetId, "rough timeout");
                this.sandApiNextActionAt = now + SandApiRetryBackoffSeconds;
                return;
            }

            this.sandApiNextActionAt = now + SandApiWaitRoughPollSeconds;
        }

        // Picks which model option to choose. The rough's option list is [one real model + 2
        // decoys]: the 14 real sculptures are SandfinishedItem ids 1..14, decoys are >14, and
        // choosing a decoy yields sandfinished 600299 = a SPOILED sculpture (confirmed 2026-07-09
        // by cross-referencing the QTE log with cn_tables.db, agent-verified). The correct option
        // is the smallest id (the only id <= SandValidModelMaxId).
        private int SandPickModelOptionIndex(int[] options)
        {
            if (options == null || options.Length == 0)
            {
                return 0;
            }

            // Prefer a real model (id <= SandValidModelMaxId); among those (normally exactly one)
            // take the smallest. Fall back to the overall smallest id if none look like a model.
            int bestValid = -1;
            int bestValidId = int.MaxValue;
            int bestAny = 0;
            int validCount = 0;
            for (int i = 0; i < options.Length; i++)
            {
                int id = options[i];
                if (id < options[bestAny])
                {
                    bestAny = i;
                }
                if (id >= 1 && id <= SandValidModelMaxId)
                {
                    validCount++;
                    if (id < bestValidId)
                    {
                        bestValidId = id;
                        bestValid = i;
                    }
                }
            }

            // The server always sends exactly one valid model (id<=14) + 2 decoys. Anything else is
            // an anomaly worth surfacing (game data changed / more models added / a table update
            // moved the id boundary). We still pick the smallest valid id; if there is none we are
            // forced onto a decoy (spoil) — log loudly so it is visible in bugtopia.log.
            if (validCount != 1)
            {
                this.SandLogStatus("model-pick anomaly: validCount=" + validCount + " (expected 1) options=[" + string.Join(",", options) + "]"
                                   + (bestValid < 0 ? " -> NO valid model, forced decoy (will spoil)" : " -> picking smallest valid id=" + bestValidId));
            }

            return bestValid >= 0 ? bestValid : bestAny;
        }

        private void SandTickCloseDialog(float now)
        {
            bool closed = this.TryCloseSandModelDialog(out bool wasOpen, out string closeStatus);
            if (closed && wasOpen)
            {
                this.SandLog("close model dialog: " + closeStatus);
                this.SandSetAction("model dialog closed; waiting for next base");
                this.sandApiState = SandApiState.FindBase;
                this.sandApiNextActionAt = now + SandApiPostChooseBackoffSeconds;
                return;
            }

            if (now >= this.sandApiDialogDeadlineAt)
            {
                // Dialog never appeared (player not adjacent / already dismissed) or could not be
                // resolved — stop waiting. Not an error: the sculpture is already made.
                this.SandLog("close model dialog: giving up (" + closeStatus + ")");
                this.SandSetAction("waiting for next base");
                this.sandApiState = SandApiState.FindBase;
                this.sandApiNextActionAt = now + SandApiPostChooseBackoffSeconds;
                return;
            }

            this.sandApiNextActionAt = now + SandApiCloseDialogPollSeconds;
        }

        private void SandTickPlaceBase(float now)
        {
            this.sandPlaceAttempts++;
            bool ok = this.TryPlaceSandBaseFromBackpack(out string placeStatus);
            this.sandLastPlaceStatus = placeStatus;
            if (ok)
            {
                this.SandSetAction("placed base (attempt " + this.sandPlaceAttempts + "): " + placeStatus);
                this.SandLog("place base: " + placeStatus);
                // Give the server a moment to spawn the base entity, then rescan.
                this.sandApiState = SandApiState.FindBase;
                this.sandApiNextActionAt = now + SandApiPlaceBaseBackoffSeconds;
                return;
            }

            this.SandLog("place base failed (attempt " + this.sandPlaceAttempts + "): " + placeStatus);

            // Out of sand bases in the backpack is terminal — no amount of retrying makes one
            // appear, and without a base the whole auto-sculpt loop has nothing to do. Stop the
            // feature entirely (not just auto-place) on the first such result.
            if (placeStatus.StartsWith("no sand base in backpack", StringComparison.Ordinal))
            {
                this.autoSandEnabled = false;
                this.sandAutoPlaceBase = false;
                this.SandSetAction("stopped: no sand bases left in backpack");
                this.SandLogStatus("stopped: no sand bases left in backpack");
                this.AddMenuNotification(this.L("Auto Sand Sculpture") + ": " + this.L("no sand bases left"), new Color(1f, 0.55f, 0.55f));
                return;
            }

            if (this.sandPlaceAttempts >= SandPlaceBaseMaxAttempts)
            {
                // Repeated non-terminal failures (send rejected / no field) — stop trying to place
                // this session so the loop does not spin; keep scanning for a manually-placed base.
                this.sandAutoPlaceBase = false;
                this.SandSetAction("auto-place disabled: " + placeStatus);
                this.AddMenuNotification(this.L("Auto-place base from backpack") + ": " + this.L("Off"), new Color(1f, 0.55f, 0.55f));
                this.sandApiState = SandApiState.FindBase;
                this.sandApiNextActionAt = now + SandApiScanBackoffSeconds;
                return;
            }

            this.SandSetAction("place failed (attempt " + this.sandPlaceAttempts + "): " + placeStatus);
            this.sandApiState = SandApiState.FindBase;
            this.sandApiNextActionAt = now + SandApiPlaceBaseBackoffSeconds;
        }

        // --- base / rough component scans -------------------------------------------------

        // Scans streamed-in SandSculpturesComponent views and returns the nearest base's
        // owner-entity netId + its roughStaticId (QTE config id). Scalars only escape the
        // pinned scan scope.
        private bool TryFindNearestSandBase(out uint baseNetId, out int roughStaticId, out string status)
        {
            baseNetId = 0;
            roughStaticId = 0;
            status = string.Empty;

            if (!AuraMonoPinningAvailable)
            {
                status = "pinning unavailable";
                return false;
            }

            if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                status = "GetComponents not ready";
                return false;
            }

            if (this.sandAuraBaseComponentClass == IntPtr.Zero)
            {
                status = "SandSculpturesComponent class unavailable";
                return false;
            }

            bool hasPlayer = this.TryGetLocalPlayerPosition(out Vector3 playerPos);
            if (!hasPlayer)
            {
                status = "no player position";
                return false;
            }

            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.sandAuraBaseComponentClass, out List<IntPtr> components, pins)
                    || components == null || components.Count == 0)
                {
                    status = "no SandSculpturesComponent instances";
                    return false;
                }

                int inRange = 0;
                int blacklisted = 0;
                int unreadable = 0;
                float bestDistance = SandBaseScanRadius;
                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr compObj = components[i];
                    if (compObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(compObj, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero
                        || !this.TryGetAuraMonoEntityNetId(entityObj, out uint netId) || netId == 0)
                    {
                        unreadable++;
                        continue;
                    }

                    if (this.sandBaseBlacklist.Contains(netId))
                    {
                        blacklisted++;
                        continue;
                    }

                    if (!this.TryGetAuraMonoEntityPosition(entityObj, out Vector3 pos))
                    {
                        unreadable++;
                        continue;
                    }

                    float distance = Vector3.Distance(playerPos, pos);
                    if (distance > SandBaseScanRadius)
                    {
                        continue;
                    }

                    inRange++;
                    if (!this.TryReadSandBaseComponentData(compObj, out int baseStaticId, out int candidateRoughId) || candidateRoughId <= 0)
                    {
                        unreadable++;
                        this.SandLog("base " + netId + " componentData unreadable (baseStaticId=" + baseStaticId + " roughStaticId=" + candidateRoughId + ")");
                        continue;
                    }

                    if (distance <= bestDistance)
                    {
                        bestDistance = distance;
                        baseNetId = netId;
                        roughStaticId = candidateRoughId;
                    }
                }

                if (baseNetId == 0)
                {
                    status = "no base in " + SandBaseScanRadius + "m (components=" + components.Count
                             + " inRange=" + inRange + " blacklisted=" + blacklisted + " unreadable=" + unreadable + ")";
                    return false;
                }

                status = "dist=" + bestDistance.ToString("F1") + "m (components=" + components.Count
                         + " inRange=" + inRange + " blacklisted=" + blacklisted + ")";
                return true;
            }
            catch (Exception ex)
            {
                status = "scan exception: " + ex.Message;
                this.SandLogStatus(status);
                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        // Scans SandSculptureRoughComponent views for the rough owned by the local player
        // and copies its option list into a managed array (no mono pointers escape).
        private bool TryFindOwnSandRough(out uint roughNetId, out int[] options, out string status)
        {
            roughNetId = 0;
            options = null;
            status = string.Empty;

            if (!AuraMonoPinningAvailable)
            {
                status = "pinning unavailable";
                return false;
            }

            if (this.sandAuraRoughComponentClass == IntPtr.Zero)
            {
                status = "SandSculptureRoughComponent class unavailable";
                return false;
            }

            if (!this.TryResolveSelfPlayerNetId(out uint selfNetId) || selfNetId == 0)
            {
                status = "self netId unavailable";
                return false;
            }

            // Secondary signal for the log: the game writes the own rough's netId into
            // PlayerDataComponent when the rough view spawns.
            this.TryReadSelfSandRoughNetIdField(out uint playerDataRoughNetId);

            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.sandAuraRoughComponentClass, out List<IntPtr> components, pins)
                    || components == null || components.Count == 0)
                {
                    status = "no rough components (playerData=" + playerDataRoughNetId + ")";
                    return false;
                }

                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr compObj = components[i];
                    if (compObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryReadSandRoughComponentData(compObj, out int staticId, out uint ownerNetId, out int[] candidateOptions))
                    {
                        continue;
                    }

                    if (ownerNetId != selfNetId)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(compObj, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero
                        || !this.TryGetAuraMonoEntityNetId(entityObj, out roughNetId) || roughNetId == 0)
                    {
                        // Own rough but its entity netId is unreadable — fall back to the
                        // player-data field if it matches this spawn.
                        roughNetId = playerDataRoughNetId;
                    }

                    if (roughNetId == 0)
                    {
                        continue;
                    }

                    options = candidateOptions;
                    status = "rough=" + roughNetId + " staticId=" + staticId
                             + " options=" + (options != null ? options.Length.ToString() : "null")
                             + " playerData=" + playerDataRoughNetId
                             + " (scanned " + components.Count + ")";
                    return true;
                }

                status = "no own rough among " + components.Count + " (self=" + selfNetId + " playerData=" + playerDataRoughNetId + ")";
                return false;
            }
            catch (Exception ex)
            {
                status = "rough scan exception: " + ex.Message;
                this.SandLogStatus(status);
                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        // --- boxed component-data readers --------------------------------------------------

        // SandSculpturesComponentData { int baseStaticId; int roughStaticId; } — read from the
        // boxed copy of the view component's _componentData field via real field offsets
        // (mono_field_get_offset includes the 2-pointer box header).
        private unsafe bool TryReadSandBaseComponentData(IntPtr componentObj, out int baseStaticId, out int roughStaticId)
        {
            baseStaticId = 0;
            roughStaticId = 0;
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

                IntPtr baseField = auraMonoClassGetFieldFromName(dataClass, "baseStaticId");
                IntPtr roughField = auraMonoClassGetFieldFromName(dataClass, "roughStaticId");
                if (baseField == IntPtr.Zero || roughField == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr raw = auraMonoObjectUnbox(boxedData);
                if (raw == IntPtr.Zero)
                {
                    return false;
                }

                int header = 2 * IntPtr.Size;
                baseStaticId = Marshal.ReadInt32(raw, (int)auraMonoFieldGetOffset(baseField) - header);
                roughStaticId = Marshal.ReadInt32(raw, (int)auraMonoFieldGetOffset(roughField) - header);
                return true;
            }
            catch (Exception ex)
            {
                this.SandLog("base data read exception: " + ex.Message);
                return false;
            }
            finally
            {
                AuraMonoPinFree(boxPin);
            }
        }

        // SandSculptureRoughComponentData { int staticId; uint ownerNetId; int[] options; }.
        // The options reference is read straight out of the unboxed struct bytes and the
        // array elements are copied while both the box and the array are pinned.
        private unsafe bool TryReadSandRoughComponentData(IntPtr componentObj, out int staticId, out uint ownerNetId, out int[] options)
        {
            staticId = 0;
            ownerNetId = 0;
            options = null;
            if (componentObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoObjectUnbox == null
                || auraMonoClassGetFieldFromName == null || auraMonoFieldGetOffset == null
                || auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null)
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
                IntPtr ownerField = auraMonoClassGetFieldFromName(dataClass, "ownerNetId");
                IntPtr optionsField = auraMonoClassGetFieldFromName(dataClass, "options");
                if (staticField == IntPtr.Zero || ownerField == IntPtr.Zero || optionsField == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr raw = auraMonoObjectUnbox(boxedData);
                if (raw == IntPtr.Zero)
                {
                    return false;
                }

                int header = 2 * IntPtr.Size;
                staticId = Marshal.ReadInt32(raw, (int)auraMonoFieldGetOffset(staticField) - header);
                ownerNetId = (uint)Marshal.ReadInt32(raw, (int)auraMonoFieldGetOffset(ownerField) - header);
                IntPtr arrObj = Marshal.ReadIntPtr(raw, (int)auraMonoFieldGetOffset(optionsField) - header);
                if (arrObj == IntPtr.Zero)
                {
                    return true; // valid rough, options not synced yet
                }

                uint arrPin = AuraMonoPinNew(arrObj);
                try
                {
                    int len = (int)auraMonoArrayLength(arrObj);
                    if (len <= 0 || len > SandMaxOptionCount)
                    {
                        return true;
                    }

                    IntPtr basePtr = auraMonoArrayAddrWithSize(arrObj, 4, UIntPtr.Zero);
                    if (basePtr == IntPtr.Zero)
                    {
                        return true;
                    }

                    int[] result = new int[len];
                    for (int i = 0; i < len; i++)
                    {
                        result[i] = Marshal.ReadInt32(basePtr, i * 4);
                    }

                    options = result;
                }
                finally
                {
                    AuraMonoPinFree(arrPin);
                }

                return true;
            }
            catch (Exception ex)
            {
                this.SandLog("rough data read exception: " + ex.Message);
                return false;
            }
            finally
            {
                AuraMonoPinFree(boxPin);
            }
        }

        // --- table / feature-open reads ----------------------------------------------------

        // rounds = TableData.GetSandrough(roughStaticId).interval.Length — the QTE round
        // count the vanilla track would play (SandSwingTrackCellModel.StartStep).
        private unsafe bool TryGetSandQteRoundCount(int roughStaticId, out int rounds, out string status)
        {
            rounds = 0;
            status = string.Empty;

            if (roughStaticId <= 0)
            {
                status = "roughStaticId invalid (" + roughStaticId + ")";
                return false;
            }

            if (this.sandAuraGetSandroughMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null || auraMonoArrayLength == null)
            {
                status = "GetSandrough unavailable";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura not ready";
                return false;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                int id = roughStaticId;
                byte needException = 0; // mono bool == 1 byte
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                IntPtr rowObj = auraMonoRuntimeInvoke(this.sandAuraGetSandroughMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "GetSandrough exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                if (rowObj == IntPtr.Zero)
                {
                    status = "no TableSandrough row for " + roughStaticId;
                    return false;
                }

                uint rowPin = AuraMonoPinNew(rowObj);
                try
                {
                    if (!this.TryGetMonoObjectMember(rowObj, "interval", out IntPtr intervalArr) || intervalArr == IntPtr.Zero)
                    {
                        status = "interval array unavailable";
                        return false;
                    }

                    rounds = (int)auraMonoArrayLength(intervalArr);
                }
                finally
                {
                    AuraMonoPinFree(rowPin);
                }

                if (rounds <= 0)
                {
                    status = "interval empty";
                    return false;
                }

                status = "rounds=" + rounds;
                return true;
            }
            catch (Exception ex)
            {
                status = "rounds exception: " + ex.Message;
                return false;
            }
        }

        // FeatureOpenSystem.IsFeatureOpen(FeatureOpenEnum.SandSculptingQuality=300041):
        // gates whether the vanilla QTE plays all rounds with a perfect zone. Defaults to
        // closed (the conservative 1-round report) when the module is unreachable.
        private unsafe bool IsSandQualityFeatureOpen(out string status)
        {
            status = string.Empty;
            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.FeatureOpen.FeatureOpenSystem", out IntPtr moduleObj)
                    || moduleObj == IntPtr.Zero)
                {
                    status = "FeatureOpenSystem unavailable";
                    return false;
                }

                uint modulePin = AuraMonoPinNew(moduleObj);
                try
                {
                    if (this.sandAuraFeatureOpenMethod == IntPtr.Zero)
                    {
                        IntPtr moduleClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(moduleObj) : IntPtr.Zero;
                        this.sandAuraFeatureOpenMethod = this.FindAuraMonoMethodOnHierarchy(moduleClass, "IsFeatureOpen", 1);
                    }

                    if (this.sandAuraFeatureOpenMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
                    {
                        status = "IsFeatureOpen unavailable";
                        return false;
                    }

                    IntPtr exc = IntPtr.Zero;
                    int featureId = SandFeatureOpenQualityId; // enum arg passes as its 4-byte value
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&featureId);
                    IntPtr boxed = auraMonoRuntimeInvoke(this.sandAuraFeatureOpenMethod, moduleObj, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                    {
                        status = "IsFeatureOpen exc/null";
                        return false;
                    }

                    IntPtr raw = auraMonoObjectUnbox(boxed);
                    bool open = raw != IntPtr.Zero && Marshal.ReadByte(raw) != 0;
                    status = "IsFeatureOpen(" + SandFeatureOpenQualityId + ")=" + open;
                    return open;
                }
                finally
                {
                    AuraMonoPinFree(modulePin);
                }
            }
            catch (Exception ex)
            {
                status = "feature-open exception: " + ex.Message;
                return false;
            }
        }

        // --- protocol invokes ----------------------------------------------------------------

        private unsafe bool TryInvokeSandUIntArgMethod(IntPtr method, string methodName, uint netId, out string status)
        {
            status = string.Empty;
            if (method == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                status = methodName + " method missing";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura not ready";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            uint arg = netId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&arg);
            auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = methodName + " exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = methodName + "(" + netId + ")";
            return true;
        }

        private unsafe bool TryInvokeSandFinish(uint baseNetId, int successCount, int perfectCount, out string status)
        {
            status = string.Empty;
            if (this.sandAuraFinishMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                status = "finish method missing";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura not ready";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            uint netId = baseNetId;
            int success = successCount;
            int perfect = perfectCount;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&netId);
            args[1] = (IntPtr)(&success);
            args[2] = (IntPtr)(&perfect);
            auraMonoRuntimeInvoke(this.sandAuraFinishMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "FinishMakingSandSculpture exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "FinishMakingSandSculpture(" + baseNetId + "," + successCount + "," + perfectCount + ")";
            return true;
        }

        private unsafe bool TryInvokeSandChoose(uint roughNetId, int productId, out string status)
        {
            status = string.Empty;
            if (this.sandAuraChooseMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                status = "choose method missing";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura not ready";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            uint netId = roughNetId;
            int model = productId;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&netId);
            args[1] = (IntPtr)(&model);
            auraMonoRuntimeInvoke(this.sandAuraChooseMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "ChooseSandSculptureProduct exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "ChooseSandSculptureProduct(" + roughNetId + "," + productId + ")";
            return true;
        }

        // --- player-data helpers ---------------------------------------------------------------

        // Reads PlayerDataComponent._sandRoughNetId (set by the game when the own rough view
        // spawns) — used as a debug/log signal and as a rough-netId fallback.
        private bool TryReadSelfSandRoughNetIdField(out uint roughNetId)
        {
            roughNetId = 0;
            try
            {
                if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetMonoObjectMember(playerObj, "dataComponent", out IntPtr dataObj) || dataObj == IntPtr.Zero)
                {
                    return false;
                }

                return this.TryGetMonoUInt32Member(dataObj, "_sandRoughNetId", out roughNetId) && roughNetId != 0;
            }
            catch
            {
                return false;
            }
        }

        // Mirrors PlayerStateSelectSandRough.OnOptionCallback: after choosing a product the
        // game clears the player-data rough pointer so its FSM leaves the select state.
        private unsafe bool TryClearSelfSandRoughData(out string status)
        {
            status = string.Empty;
            try
            {
                if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero)
                {
                    status = "player unavailable";
                    return false;
                }

                uint playerPin = AuraMonoPinNew(playerObj);
                try
                {
                    if (!this.TryGetMonoObjectMember(playerObj, "dataComponent", out IntPtr dataObj) || dataObj == IntPtr.Zero)
                    {
                        status = "dataComponent unavailable";
                        return false;
                    }

                    uint dataPin = AuraMonoPinNew(dataObj);
                    try
                    {
                        IntPtr dataClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(dataObj) : IntPtr.Zero;
                        IntPtr setMethod = this.FindAuraMonoMethodOnHierarchy(dataClass, "SetSandRoughData", 1);
                        if (setMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                        {
                            status = "SetSandRoughData missing";
                            return false;
                        }

                        IntPtr exc = IntPtr.Zero;
                        uint zero = 0;
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = (IntPtr)(&zero);
                        auraMonoRuntimeInvoke(setMethod, dataObj, (IntPtr)args, ref exc);
                        if (exc != IntPtr.Zero)
                        {
                            status = "SetSandRoughData exc=0x" + exc.ToInt64().ToString("X");
                            return false;
                        }

                        status = "SetSandRoughData(0)";
                        return true;
                    }
                    finally
                    {
                        AuraMonoPinFree(dataPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(playerPin);
                }
            }
            catch (Exception ex)
            {
                status = "clear exception: " + ex.Message;
                return false;
            }
        }

        // --- stuck "choose model" dialog cleanup ------------------------------------------------

        // Closes the vanilla "choose model" dialog (DialogueSimplePanel), opened by
        // PlayerStateSelectSandRough when the own rough spawns next to the player. Our protocol
        // Choose bypasses the dialog callback so nothing closes it. Resolve the panel via
        // UIManager.GetView(Type) (Type from the class pointer, never Type.GetType) and invoke
        // its protected CloseSelf(). wasOpen distinguishes "closed one" from "none was open".
        private unsafe bool TryCloseSandModelDialog(out bool wasOpen, out string status)
        {
            wasOpen = false;
            status = string.Empty;
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null
                    || auraMonoClassGetType == null || auraMonoTypeGetObject == null
                    || this.auraMonoRootDomain == IntPtr.Zero)
                {
                    status = "aura not ready";
                    return false;
                }

                if (this.sandAuraDialogPanelClass == IntPtr.Zero)
                {
                    this.sandAuraDialogPanelClass = this.FindAuraMonoClassInImages(
                        "XDTGame.UI.Panel", "DialogueSimplePanel", SandDialogPanelImages);
                }

                if (this.sandAuraDialogPanelClass == IntPtr.Zero)
                {
                    status = "DialogueSimplePanel class not found";
                    return false;
                }

                if (!this.ModTryResolveAuraMonoUIManager(out IntPtr uiManagerObj, out IntPtr uiManagerClass))
                {
                    status = "UIManager unavailable";
                    return false;
                }

                IntPtr monoType = auraMonoClassGetType(this.sandAuraDialogPanelClass);
                IntPtr typeObj = monoType != IntPtr.Zero ? auraMonoTypeGetObject(this.auraMonoRootDomain, monoType) : IntPtr.Zero;
                if (typeObj == IntPtr.Zero)
                {
                    status = "dialog Type object unavailable";
                    return false;
                }

                IntPtr getView = this.FindAuraMonoMethodOnHierarchy(uiManagerClass, "GetView", 1);
                if (getView == IntPtr.Zero)
                {
                    status = "GetView missing";
                    return false;
                }

                IntPtr getExc = IntPtr.Zero;
                IntPtr* getArgs = stackalloc IntPtr[1];
                getArgs[0] = typeObj;
                IntPtr panelObj = auraMonoRuntimeInvoke(getView, uiManagerObj, (IntPtr)getArgs, ref getExc);
                if (getExc != IntPtr.Zero)
                {
                    status = "GetView exc=0x" + getExc.ToInt64().ToString("X");
                    return false;
                }

                if (panelObj == IntPtr.Zero)
                {
                    status = "dialog not open";
                    return true; // nothing to close — success, wasOpen stays false
                }

                uint panelPin = AuraMonoPinNew(panelObj);
                try
                {
                    IntPtr panelClass = auraMonoObjectGetClass(panelObj);
                    IntPtr closeSelf = this.FindAuraMonoMethodOnHierarchy(panelClass, "CloseSelf", 0);
                    if (closeSelf == IntPtr.Zero)
                    {
                        status = "CloseSelf missing";
                        return false;
                    }

                    IntPtr exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(closeSelf, panelObj, IntPtr.Zero, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        status = "CloseSelf exc=0x" + exc.ToInt64().ToString("X");
                        return false;
                    }

                    wasOpen = true;
                    status = "CloseSelf";
                    return true;
                }
                finally
                {
                    AuraMonoPinFree(panelPin);
                }
            }
            catch (Exception ex)
            {
                status = "close dialog exception: " + ex.Message;
                return false;
            }
        }

        // ===== place a sand base from the backpack (direct build command) =====================
        // Sends the game's generic build-place command directly (no build UI / camera):
        //   HomelandProtocolManager.SendBuildBatchOperation(netId, buildRootNetId, IBuildData)
        // with a hand-built BuildPlaceData. Verified against BuildSaveOption.CreateTransformData /
        // GetOperationIdentity / ProcessSaveSnapshot (see .research-record/sand-base-placement-helper.md).
        // AuraMono-only; fail-closed. No server position validation (build-overlap bypassable), so
        // the base is placed ~2 m in front of the player.

        private const int SandPlaceMaxBackpackScan = 4096;
        private const float SandPlaceDistance = 0.8f; // metres in front of the player (close, within interact range)

        // Cached descriptors (image-lifetime; safe to cache raw).
        private IntPtr sandPlaceHomelandProtocolClass;   // XDTDataAndProtocol.ProtocolService.Homeland.HomelandProtocolManager
        private IntPtr sandPlaceSendBuildBatchMethod;    // SendBuildBatchOperation(uint,uint,IBuildData) — 3 params
        private IntPtr sandPlaceBuildPlaceDataClass;     // XDT.Scene.Shared.Modules.Build.BuildPlaceData
        private IntPtr sandPlaceBuildTransformDataClass; // XDT.Scene.Shared.Modules.Build.BuildTransformData
        private IntPtr sandPlaceFldBuildType;            // BuildPlaceData.BuildType (byte)
        private IntPtr sandPlaceFldTransformData;        // BuildPlaceData.TransformData (BuildTransformData)
        private IntPtr sandPlaceFldLevelObjectNetId;     // BuildTransformData.LevelObjectNetId (ulong)
        private IntPtr sandPlaceFldLocalPos;             // BuildTransformData.LocalPos (Vector3)
        private IntPtr sandPlaceFldAngle;                // BuildTransformData.Angle (int)
        private IntPtr sandPlaceFldExt;                  // BuildTransformData.Ext (ulong)
        private IntPtr sandPlaceFldCurve;                // BuildTransformData.Curve (byte)
        private IntPtr sandPlaceFldVLinkHasChange;       // BuildTransformData.VirtualLinkHasChange (bool)
        private IntPtr sandPlaceFldVLink;                // BuildTransformData.VirtualLinkLevelObjectNetId (ulong[])
        private IntPtr sandPlaceGetSandbaseMethod;       // TableData.GetSandbase(int,bool) — 2 params
        private IntPtr sandPlaceBackpackGetItemNetIdMethod; // BackPackSystem.GetItemNetId(int) — 1 param
        private IntPtr sandPlaceInFieldNetIdMethod;      // LocalPlayerComponent.get_inFieldNetId — 0 params

        private static readonly string[] SandPlaceBuildImages =
        {
            "EcsClient", "EcsClient.dll", "Client", "Client.dll"
        };
        private static readonly string[] SandPlaceProtocolImages =
        {
            "XDTDataAndProtocol", "XDTDataAndProtocol.dll", "Client", "Client.dll"
        };

        private bool TryPlaceSandBaseFromBackpack(out string status)
        {
            status = "sand place unavailable";

            if (!this.EnsureSandSculptureResolved(out string resolveStatus))
            {
                status = "types not resolved: " + resolveStatus;
                this.SandLogStatus("place: " + status);
                return false;
            }
            if (!this.EnsureSandPlaceResolved(out string placeResolve))
            {
                status = "place types not resolved: " + placeResolve;
                this.SandLogStatus("place: " + status);
                return false;
            }

            // 1) pick a sand-base item we own
            if (!this.TryResolveSandBaseBackpackItem(out uint bagItemNetId, out int staticId, out string itemStatus))
            {
                status = "no sand base in backpack: " + itemStatus;
                this.SandLogStatus("place: " + status);
                return false;
            }

            // 2) resolve target field + local position
            if (!this.TryResolveSandPlaceTarget(out uint buildRootNetId, out ulong putZoneId,
                                                out Vector3 localPos, out int angle, out string targetStatus))
            {
                status = "target unresolved: " + targetStatus;
                this.SandLogStatus("place: " + status);
                return false;
            }

            // 3) build BuildPlaceData and send
            if (!this.TrySendSandBasePlace(bagItemNetId, buildRootNetId, putZoneId, localPos, angle,
                                           out string sendStatus))
            {
                status = "send failed: " + sendStatus;
                this.SandLogStatus("place: " + status);
                return false;
            }

            status = "placed sand base staticId=" + staticId + " item=" + bagItemNetId
                     + " root=" + buildRootNetId + " zone=0x" + putZoneId.ToString("X")
                     + " localPos=" + localPos + " (" + sendStatus + ")";
            this.SandLog("place OK: " + status);
            this.AddMenuNotification(this.L("Sand base placed"), new Color(0.45f, 1f, 0.55f));
            return true;
        }

        private bool EnsureSandPlaceResolved(out string status)
        {
            status = "cached";
            if (this.sandPlaceSendBuildBatchMethod != IntPtr.Zero
                && this.sandPlaceBuildPlaceDataClass != IntPtr.Zero
                && this.sandPlaceBuildTransformDataClass != IntPtr.Zero
                && this.sandPlaceFldTransformData != IntPtr.Zero
                && this.sandPlaceFldVLink != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura api not ready";
                return false;
            }

            // HomelandProtocolManager (image XDTDataAndProtocol)
            if (this.sandPlaceHomelandProtocolClass == IntPtr.Zero)
            {
                this.sandPlaceHomelandProtocolClass = this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.Homeland.HomelandProtocolManager");
                if (this.sandPlaceHomelandProtocolClass == IntPtr.Zero)
                {
                    this.sandPlaceHomelandProtocolClass = this.FindAuraMonoClassInImages(
                        "XDTDataAndProtocol.ProtocolService.Homeland", "HomelandProtocolManager",
                        SandPlaceProtocolImages);
                }
            }
            if (this.sandPlaceHomelandProtocolClass != IntPtr.Zero && this.sandPlaceSendBuildBatchMethod == IntPtr.Zero)
            {
                // ONLY the 3-param overload exists; (uint,uint,IBuildData).
                this.sandPlaceSendBuildBatchMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.sandPlaceHomelandProtocolClass, "SendBuildBatchOperation", 3);
            }

            // BuildPlaceData / BuildTransformData (image EcsClient)
            if (this.sandPlaceBuildPlaceDataClass == IntPtr.Zero)
            {
                this.sandPlaceBuildPlaceDataClass = this.FindAuraMonoClassByFullName(
                    "XDT.Scene.Shared.Modules.Build.BuildPlaceData");
                if (this.sandPlaceBuildPlaceDataClass == IntPtr.Zero)
                {
                    this.sandPlaceBuildPlaceDataClass = this.FindAuraMonoClassInImages(
                        "XDT.Scene.Shared.Modules.Build", "BuildPlaceData", SandPlaceBuildImages);
                }
            }
            if (this.sandPlaceBuildTransformDataClass == IntPtr.Zero)
            {
                this.sandPlaceBuildTransformDataClass = this.FindAuraMonoClassByFullName(
                    "XDT.Scene.Shared.Modules.Build.BuildTransformData");
                if (this.sandPlaceBuildTransformDataClass == IntPtr.Zero)
                {
                    this.sandPlaceBuildTransformDataClass = this.FindAuraMonoClassInImages(
                        "XDT.Scene.Shared.Modules.Build", "BuildTransformData", SandPlaceBuildImages);
                }
            }

            if (this.sandPlaceBuildPlaceDataClass != IntPtr.Zero && this.sandPlaceFldBuildType == IntPtr.Zero)
            {
                this.sandPlaceFldBuildType     = this.FindAuraMonoFieldOnHierarchy(this.sandPlaceBuildPlaceDataClass, "BuildType");
                this.sandPlaceFldTransformData = this.FindAuraMonoFieldOnHierarchy(this.sandPlaceBuildPlaceDataClass, "TransformData");
            }
            if (this.sandPlaceBuildTransformDataClass != IntPtr.Zero && this.sandPlaceFldLevelObjectNetId == IntPtr.Zero)
            {
                this.sandPlaceFldLevelObjectNetId = this.FindAuraMonoFieldOnHierarchy(this.sandPlaceBuildTransformDataClass, "LevelObjectNetId");
                this.sandPlaceFldLocalPos         = this.FindAuraMonoFieldOnHierarchy(this.sandPlaceBuildTransformDataClass, "LocalPos");
                this.sandPlaceFldAngle            = this.FindAuraMonoFieldOnHierarchy(this.sandPlaceBuildTransformDataClass, "Angle");
                this.sandPlaceFldExt              = this.FindAuraMonoFieldOnHierarchy(this.sandPlaceBuildTransformDataClass, "Ext");
                this.sandPlaceFldCurve            = this.FindAuraMonoFieldOnHierarchy(this.sandPlaceBuildTransformDataClass, "Curve");
                this.sandPlaceFldVLinkHasChange   = this.FindAuraMonoFieldOnHierarchy(this.sandPlaceBuildTransformDataClass, "VirtualLinkHasChange");
                this.sandPlaceFldVLink            = this.FindAuraMonoFieldOnHierarchy(this.sandPlaceBuildTransformDataClass, "VirtualLinkLevelObjectNetId");
            }

            // TableData.GetSandbase(int,bool) for the item filter (sandAuraTableDataClass already resolved)
            if (this.sandPlaceGetSandbaseMethod == IntPtr.Zero && this.sandAuraTableDataClass != IntPtr.Zero)
            {
                this.sandPlaceGetSandbaseMethod = this.FindAuraMonoMethodOnHierarchy(this.sandAuraTableDataClass, "GetSandbase", 2);
                if (this.sandPlaceGetSandbaseMethod == IntPtr.Zero)
                {
                    this.sandPlaceGetSandbaseMethod = this.FindAuraMonoMethodOnHierarchy(this.sandAuraTableDataClass, "GetSandbase", 1);
                }
            }

            bool ok = this.sandPlaceSendBuildBatchMethod != IntPtr.Zero
                      && this.sandPlaceBuildPlaceDataClass != IntPtr.Zero
                      && this.sandPlaceBuildTransformDataClass != IntPtr.Zero
                      && this.sandPlaceFldBuildType != IntPtr.Zero
                      && this.sandPlaceFldTransformData != IntPtr.Zero
                      && this.sandPlaceFldLevelObjectNetId != IntPtr.Zero
                      && this.sandPlaceFldLocalPos != IntPtr.Zero
                      && this.sandPlaceFldAngle != IntPtr.Zero
                      && this.sandPlaceFldVLinkHasChange != IntPtr.Zero
                      && this.sandPlaceFldVLink != IntPtr.Zero;

            status = ok ? "resolved"
                        : ("missing send=" + (this.sandPlaceSendBuildBatchMethod != IntPtr.Zero)
                           + " place=" + (this.sandPlaceBuildPlaceDataClass != IntPtr.Zero)
                           + " td=" + (this.sandPlaceBuildTransformDataClass != IntPtr.Zero)
                           + " fldTd=" + (this.sandPlaceFldTransformData != IntPtr.Zero)
                           + " fldVLink=" + (this.sandPlaceFldVLink != IntPtr.Zero));
            return ok;
        }

        // Scan backpack, first item that is a TableSandbase (List enum is image-safe).
        private unsafe bool TryResolveSandBaseBackpackItem(out uint bagItemNetId, out int staticId, out string status)
        {
            bagItemNetId = 0; staticId = 0; status = string.Empty;

            if (!AuraMonoPinningAvailable) { status = "pinning unavailable"; return false; }
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            { status = "aura not ready"; return false; }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPack)
                || backPack == IntPtr.Zero)
            { status = "BackPackSystem unavailable"; return false; }

            // Cache GetItemNetId(int) once (used only for the BuildType=1 fallback / re-query).
            if (this.sandPlaceBackpackGetItemNetIdMethod == IntPtr.Zero)
            {
                IntPtr bpClass = auraMonoObjectGetClass(backPack);
                this.sandPlaceBackpackGetItemNetIdMethod = this.FindAuraMonoMethodOnHierarchy(bpClass, "GetItemNetId", 1);
            }

            // GetAllItem(EStorageType.Backpack==1) -> List<BackpackItem>
            IntPtr getAllItem = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(backPack), "GetAllItem", 1);
            if (getAllItem == IntPtr.Zero)
            {
                getAllItem = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(backPack), "GetAllItem", 0);
            }
            if (getAllItem == IntPtr.Zero) { status = "GetAllItem missing"; return false; }

            IntPtr listObj;
            IntPtr exc = IntPtr.Zero;
            int storageBackpack = 1; // EStorageType.Backpack = 1
            if (AuraMonoMethodParamCountIs(getAllItem, 1))
            {
                IntPtr* a = stackalloc IntPtr[1]; a[0] = (IntPtr)(&storageBackpack);
                listObj = auraMonoRuntimeInvoke(getAllItem, backPack, (IntPtr)a, ref exc);
            }
            else
            {
                listObj = auraMonoRuntimeInvoke(getAllItem, backPack, IntPtr.Zero, ref exc);
            }
            if (exc != IntPtr.Zero || listObj == IntPtr.Zero) { status = "GetAllItem failed"; return false; }

            List<uint> pins = new List<uint>();
            List<IntPtr> items = new List<IntPtr>(256);
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items, pins) || items.Count == 0)
                { status = "backpack empty"; return false; }

                int scanned = 0;
                for (int i = 0; i < items.Count && scanned < SandPlaceMaxBackpackScan; i++, scanned++)
                {
                    IntPtr itemObj = items[i];
                    if (itemObj == IntPtr.Zero) continue;

                    // BackpackItem { uint netId; int staticId; ... } — read scalars only.
                    if (!this.TryGetMonoUInt32Member(itemObj, "netId", out uint candNetId) || candNetId == 0) continue;
                    if (!this.TryGetMonoInt32Member(itemObj, "staticId", out int candStatic) || candStatic <= 0) continue;

                    if (!this.SandPlaceIsSandbase(candStatic)) continue;

                    bagItemNetId = candNetId;
                    staticId = candStatic;
                    status = "item netId=" + candNetId + " staticId=" + candStatic + " (scanned " + scanned + ")";
                    return true;
                }

                status = "no sandbase among " + items.Count + " items";
                return false;
            }
            catch (Exception ex) { status = "scan exception: " + ex.Message; return false; }
            finally { FreeAuraMonoPins(pins); }
        }

        // TableData.GetSandbase(staticId) != null  ->  it is a sand base
        private unsafe bool SandPlaceIsSandbase(int staticId)
        {
            if (this.sandPlaceGetSandbaseMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null) return false;
            IntPtr exc = IntPtr.Zero;
            int id = staticId;
            IntPtr result;
            if (AuraMonoMethodParamCountIs(this.sandPlaceGetSandbaseMethod, 2))
            {
                byte needException = 0;
                IntPtr* a = stackalloc IntPtr[2];
                a[0] = (IntPtr)(&id); a[1] = (IntPtr)(&needException);
                result = auraMonoRuntimeInvoke(this.sandPlaceGetSandbaseMethod, IntPtr.Zero, (IntPtr)a, ref exc);
            }
            else
            {
                IntPtr* a = stackalloc IntPtr[1]; a[0] = (IntPtr)(&id);
                result = auraMonoRuntimeInvoke(this.sandPlaceGetSandbaseMethod, IntPtr.Zero, (IntPtr)a, ref exc);
            }
            return exc == IntPtr.Zero && result != IntPtr.Zero;
        }

        // Field root, put-zone, root-local position, angle.
        private bool TryResolveSandPlaceTarget(out uint buildRootNetId, out ulong putZoneId,
                                               out Vector3 localPos, out int angle, out string status)
        {
            buildRootNetId = 0; putZoneId = 0; localPos = Vector3.zero; angle = 0; status = string.Empty;

            // buildRootNetId = LocalPlayerComponent.inFieldNetId
            if (!this.TryReadSelfInFieldNetId(out buildRootNetId) || buildRootNetId == 0)
            { status = "inFieldNetId == 0 (player not in a field)"; return false; }

            // putZoneId = LevelObjectId(buildRootNetId, 1) = root | (1<<32)
            putZoneId = (ulong)buildRootNetId | (1UL << 32);

            // target world position = player pos + flat forward * distance
            if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos)) { status = "no player position"; return false; }
            Vector3 fwd = Vector3.forward;
            try
            {
                GameObject p = GetLocalPlayer();
                if (p != null) fwd = p.transform.forward;
            }
            catch { }
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 1e-4f ? Vector3.forward : fwd.normalized;
            Vector3 targetWorld = playerPos + fwd * SandPlaceDistance;

            // world -> root-local via field-root entity localToWorldMatrix (fallbacks inside)
            if (this.TryGetFieldRootWorldToLocal(buildRootNetId, out Matrix4x4 w2l))
            {
                localPos = w2l.MultiplyPoint3x4(targetWorld);
            }
            else
            {
                // FALLBACK: assume field root ~ identity (true for the main StarTown / public beach).
                localPos = targetWorld;
                this.SandLog("place: field matrix unavailable — using WORLD position as local (identity fallback)");
            }

            // Angle from facing (mirrors BuildingCalExtensions.ToBuildingRotValue); 0 is also acceptable.
            Vector3 e = Quaternion.LookRotation(fwd, Vector3.up).eulerAngles;
            angle = (Mathf.RoundToInt(e.x) << 20) | (Mathf.RoundToInt(e.z) << 10) | Mathf.RoundToInt(e.y);

            status = "root=" + buildRootNetId + " zone=0x" + putZoneId.ToString("X") + " local=" + localPos;
            return true;
        }

        // LocalPlayerComponent.get_inFieldNetId (0 params) -> uint
        private unsafe bool TryReadSelfInFieldNetId(out uint inFieldNetId)
        {
            inFieldNetId = 0;
            try
            {
                if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero) return false;

                uint pin = AuraMonoPinNew(playerObj);
                try
                {
                    if (this.sandPlaceInFieldNetIdMethod == IntPtr.Zero)
                    {
                        IntPtr cls = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(playerObj) : IntPtr.Zero;
                        this.sandPlaceInFieldNetIdMethod = this.FindAuraMonoMethodOnHierarchy(cls, "get_inFieldNetId", 0);
                    }
                    if (this.sandPlaceInFieldNetIdMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null) return false;

                    IntPtr exc = IntPtr.Zero;
                    IntPtr boxed = auraMonoRuntimeInvoke(this.sandPlaceInFieldNetIdMethod, playerObj, IntPtr.Zero, ref exc);
                    if (exc != IntPtr.Zero || boxed == IntPtr.Zero) return false;
                    return this.TryUnboxMonoUInt32(boxed, out inFieldNetId) && inFieldNetId != 0;
                }
                finally { AuraMonoPinFree(pin); }
            }
            catch { return false; }
        }

        // field-root entity localToWorldMatrix.inverse  (Entities.GetEntity(root) -> get_localToWorldMatrix)
        private unsafe bool TryGetFieldRootWorldToLocal(uint rootNetId, out Matrix4x4 worldToLocal)
        {
            worldToLocal = Matrix4x4.identity;
            try
            {
                if (!this.TryGetAuraMonoEntityObjectByNetId(rootNetId, out IntPtr entityObj) || entityObj == IntPtr.Zero)
                    return false;

                uint pin = AuraMonoPinNew(entityObj);
                try
                {
                    IntPtr cls = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(entityObj) : IntPtr.Zero;
                    IntPtr m = this.FindAuraMonoMethodOnHierarchy(cls, "get_localToWorldMatrix", 0);
                    if (m != IntPtr.Zero && auraMonoRuntimeInvoke != null && auraMonoObjectUnbox != null)
                    {
                        IntPtr exc = IntPtr.Zero;
                        IntPtr boxed = auraMonoRuntimeInvoke(m, entityObj, IntPtr.Zero, ref exc);
                        if (exc == IntPtr.Zero && boxed != IntPtr.Zero)
                        {
                            IntPtr raw = auraMonoObjectUnbox(boxed);
                            if (raw != IntPtr.Zero)
                            {
                                Matrix4x4 l2w = *(Matrix4x4*)raw;
                                worldToLocal = l2w.inverse;
                                return true;
                            }
                        }
                    }

                    // fallback: TRS from get_position + get_rotation
                    if (this.TryGetAuraMonoEntityPosition(entityObj, out Vector3 pos)
                        && this.SandPlaceTryGetEntityRotation(entityObj, cls, out Quaternion rot))
                    {
                        worldToLocal = Matrix4x4.TRS(pos, rot, Vector3.one).inverse;
                        return true;
                    }
                    return false;
                }
                finally { AuraMonoPinFree(pin); }
            }
            catch { return false; }
        }

        private unsafe bool SandPlaceTryGetEntityRotation(IntPtr entityObj, IntPtr cls, out Quaternion rot)
        {
            rot = Quaternion.identity;
            if (auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null) return false;
            foreach (string name in new[] { "get_rotation", "GetRotation" })
            {
                IntPtr m = this.FindAuraMonoMethodOnHierarchy(cls, name, 0);
                if (m == IntPtr.Zero) continue;
                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(m, entityObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero) continue;
                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero) continue;
                rot = *(Quaternion*)raw;
                return true;
            }
            return false;
        }

        // Build BuildPlaceData (BuildType=0, NetId=bagItemNetId — consume from backpack) and send.
        private unsafe bool TrySendSandBasePlace(uint bagItemNetId, uint buildRootNetId, ulong putZoneId,
                                                 Vector3 localPos, int angle, out string status)
        {
            status = "send unavailable";

            if (!AuraMonoPinningAvailable) { status = "pinning unavailable"; return false; }
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoObjectNew == null || auraMonoFieldSetValue == null
                || auraMonoObjectUnbox == null || auraMonoRuntimeInvoke == null)
            { status = "aura mono api unavailable"; return false; }

            IntPtr domain = this.auraMonoRootDomain;

            // (a) ulong[1] { putZoneId }
            IntPtr arrObj = this.CreateAuraMonoUInt64ArrayObject(1);
            if (arrObj == IntPtr.Zero) { status = "ulong[] alloc failed (UInt64 class not resolved?)"; return false; }
            uint arrPin = AuraMonoPinNew(arrObj);

            IntPtr tdObj = IntPtr.Zero, placeObj = IntPtr.Zero;
            uint tdPin = 0, placePin = 0;
            try
            {
                IntPtr elem = auraMonoArrayAddrWithSize(arrObj, 8, UIntPtr.Zero);
                if (elem == IntPtr.Zero) { status = "array elem addr failed"; return false; }
                Marshal.WriteInt64(elem, (long)putZoneId);

                // (b) BuildTransformData (boxed struct) + fields
                tdObj = auraMonoObjectNew(domain, this.sandPlaceBuildTransformDataClass);
                if (tdObj == IntPtr.Zero) { status = "BuildTransformData alloc failed"; return false; }
                tdPin = AuraMonoPinNew(tdObj);

                ulong lvlId = putZoneId; auraMonoFieldSetValue(tdObj, this.sandPlaceFldLevelObjectNetId, (IntPtr)(&lvlId));
                Vector3 lp = localPos;   auraMonoFieldSetValue(tdObj, this.sandPlaceFldLocalPos,         (IntPtr)(&lp));
                int ang = angle;         auraMonoFieldSetValue(tdObj, this.sandPlaceFldAngle,            (IntPtr)(&ang));
                ulong ext = 0UL;         if (this.sandPlaceFldExt != IntPtr.Zero)   auraMonoFieldSetValue(tdObj, this.sandPlaceFldExt,   (IntPtr)(&ext));
                byte curve = 0;          if (this.sandPlaceFldCurve != IntPtr.Zero) auraMonoFieldSetValue(tdObj, this.sandPlaceFldCurve, (IntPtr)(&curve));
                byte vlhc = 1;           auraMonoFieldSetValue(tdObj, this.sandPlaceFldVLinkHasChange,   (IntPtr)(&vlhc));
                // REFERENCE field -> pass the array object pointer DIRECTLY (not &arrObj):
                auraMonoFieldSetValue(tdObj, this.sandPlaceFldVLink, arrObj);

                // (c) BuildPlaceData (boxed struct)
                placeObj = auraMonoObjectNew(domain, this.sandPlaceBuildPlaceDataClass);
                if (placeObj == IntPtr.Zero) { status = "BuildPlaceData alloc failed"; return false; }
                placePin = AuraMonoPinNew(placeObj);

                byte buildType = 0; // 0 = consume bag item (NetId = bagItemNetId)
                auraMonoFieldSetValue(placeObj, this.sandPlaceFldBuildType, (IntPtr)(&buildType));
                // nested VALUE-type field -> copy the whole struct from tdObj's UNBOXED pointer:
                IntPtr tdRaw = auraMonoObjectUnbox(tdObj);
                if (tdRaw == IntPtr.Zero) { status = "transform unbox failed"; return false; }
                auraMonoFieldSetValue(placeObj, this.sandPlaceFldTransformData, tdRaw);

                // (d) SendBuildBatchOperation(uint netId, uint buildRootNetId, IBuildData data)
                uint netIdArg = bagItemNetId;
                uint rootArg  = buildRootNetId;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&netIdArg);   // value type -> &value
                args[1] = (IntPtr)(&rootArg);    // value type -> &value
                args[2] = placeObj;              // IBuildData (reference) -> boxed object pointer DIRECTLY

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.sandPlaceSendBuildBatchMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "SendBuildBatchOperation exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                status = "sent BuildType=0 netId=" + bagItemNetId + " root=" + buildRootNetId;
                return true;
            }
            catch (Exception ex) { status = "send exception: " + ex.Message; return false; }
            finally
            {
                AuraMonoPinFree(placePin);
                AuraMonoPinFree(tdPin);
                AuraMonoPinFree(arrPin);
            }
        }

        // ===== collect finished sculptures into the backpack ==================================
        // A finished sculpture is a build entity carrying SandFinishComponentData { onCreate,
        // onNewMake }. The vanilla GardenSandCommand (interact 85) takes it when onNewMake==true
        // and the player owns it, by calling CharacterProtocolManager.PoseDeleteBuild(netId)
        // (-> TakeItemNetworkCommand, into the backpack). We do the same purely via protocol:
        // scan SandFinishComponent, find one with onNewMake==true, PoseDeleteBuild its entity
        // netId. One at a time (PoseDeleteBuild gates on a single in-flight build option that the
        // server clears via BuildOptionRespondEvent), throttled between takes.

        private void RunSandAutoCollect()
        {
            float now = Time.unscaledTime;
            if (now < this.sandCollectNextAt)
            {
                return;
            }

            if (!this.TryCollectOneFinishedSculpture(out bool tookOne, out string status))
            {
                this.sandLastCollectStatus = status;
                this.sandCollectNextAt = now + SandCollectScanBackoffSeconds;
                return;
            }

            this.sandLastCollectStatus = status;
            if (tookOne)
            {
                // Wait for the server to process the take (clears the build-option gate) before
                // the next one.
                this.sandCollectNextAt = now + SandCollectTakeBackoffSeconds;
            }
            else
            {
                // Nothing to collect right now.
                this.sandCollectNextAt = now + SandCollectScanBackoffSeconds;
            }
        }

        // Scans SandFinishComponent views, takes the first freshly-made sculpture. tookOne=false
        // means "resolved fine, but nothing to collect".
        private bool TryCollectOneFinishedSculpture(out bool tookOne, out string status)
        {
            tookOne = false;
            status = string.Empty;

            if (!this.EnsureSandSculptureResolved(out string resolveStatus))
            {
                status = "types not resolved: " + resolveStatus;
                return false;
            }
            if (!this.EnsureSandCollectResolved(out string collectResolve))
            {
                status = "collect types not resolved: " + collectResolve;
                return false;
            }

            if (!AuraMonoPinningAvailable)
            {
                status = "pinning unavailable";
                return false;
            }
            if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                status = "GetComponents not ready";
                return false;
            }

            // We do NOT pre-check backpack free slots here: resolving BackPackSystem every tick is
            // what caught a mono teardown window and AV'd. If the bag is full, PoseDeleteBuild is
            // simply rejected server-side (vanilla GardenSandCommand toasts tipId 80045) — a
            // harmless no-op, not a crash.
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.sandCollectFinishComponentClass, out List<IntPtr> comps, pins)
                    || comps == null || comps.Count == 0)
                {
                    status = "no finished sculptures";
                    return true; // resolved OK, nothing present
                }

                int fresh = 0;
                int scanned = 0;
                for (int i = 0; i < comps.Count && scanned < SandCollectMaxScan; i++, scanned++)
                {
                    IntPtr compObj = comps[i];
                    if (compObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryReadSandFinishOnNewMake(compObj, out bool onNewMake) || !onNewMake)
                    {
                        continue;
                    }

                    fresh++;
                    if (!this.TryGetMonoObjectMember(compObj, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero
                        || !this.TryGetAuraMonoEntityNetId(entityObj, out uint netId) || netId == 0)
                    {
                        continue;
                    }

                    if (this.TryInvokeSandPoseDeleteBuild(netId, out string takeStatus))
                    {
                        tookOne = true;
                        this.sandCollectedCount++;
                        status = "collected #" + this.sandCollectedCount + " netId=" + netId + " (" + takeStatus + ")";
                        this.SandLog("collect: " + status);
                        this.AddMenuNotification(this.L("Sculpture collected"), new Color(0.45f, 1f, 0.55f));
                        return true;
                    }

                    // PoseDeleteBuild refused (a prior take is still in flight — _optionType != 0):
                    // stop this tick and retry next throttle.
                    status = "take not ready: " + takeStatus;
                    return true;
                }

                status = fresh > 0
                    ? "found " + fresh + " fresh but netId unreadable (scanned " + comps.Count + ")"
                    : "no fresh sculptures (scanned " + comps.Count + ")";
                return true;
            }
            catch (Exception ex)
            {
                status = "collect scan exception: " + ex.Message;
                this.SandLogStatus(status);
                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private bool EnsureSandCollectResolved(out string status)
        {
            status = "cached";
            if (this.sandCollectFinishComponentClass != IntPtr.Zero
                && this.sandCollectPoseDeleteMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura api not ready";
                return false;
            }

            if (this.sandCollectFinishComponentClass == IntPtr.Zero)
            {
                for (int i = 0; i < SandAuraFinishComponentTypeNames.Length; i++)
                {
                    this.sandCollectFinishComponentClass = this.FindAuraMonoClassByFullName(SandAuraFinishComponentTypeNames[i]);
                    if (this.sandCollectFinishComponentClass != IntPtr.Zero)
                    {
                        break;
                    }
                }
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

            if (this.sandCollectCharacterProtocolClass != IntPtr.Zero && this.sandCollectPoseDeleteMethod == IntPtr.Zero)
            {
                this.sandCollectPoseDeleteMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.sandCollectCharacterProtocolClass, "PoseDeleteBuild", 1);
            }

            bool ok = this.sandCollectFinishComponentClass != IntPtr.Zero
                      && this.sandCollectPoseDeleteMethod != IntPtr.Zero;
            status = ok ? "resolved"
                        : ("missing finishComp=" + (this.sandCollectFinishComponentClass != IntPtr.Zero)
                           + " poseDelete=" + (this.sandCollectPoseDeleteMethod != IntPtr.Zero));
            return ok;
        }

        // SandFinishComponentData { bool onCreate; bool onNewMake; } — read the onNewMake byte
        // from the boxed struct via real field offsets (header-inclusive; subtract 2*IntPtr).
        private unsafe bool TryReadSandFinishOnNewMake(IntPtr componentObj, out bool onNewMake)
        {
            onNewMake = false;
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

                IntPtr field = auraMonoClassGetFieldFromName(dataClass, "onNewMake");
                if (field == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr raw = auraMonoObjectUnbox(boxedData);
                if (raw == IntPtr.Zero)
                {
                    return false;
                }

                onNewMake = Marshal.ReadByte(raw, (int)auraMonoFieldGetOffset(field) - 2 * IntPtr.Size) != 0;
                return true;
            }
            catch (Exception ex)
            {
                this.SandLog("finish data read exception: " + ex.Message);
                return false;
            }
            finally
            {
                AuraMonoPinFree(boxPin);
            }
        }

        // CharacterProtocolManager.PoseDeleteBuild(uint netId) -> bool (false if a build option is
        // already in flight). Sends TakeItemNetworkCommand internally.
        private unsafe bool TryInvokeSandPoseDeleteBuild(uint netId, out string status)
        {
            status = string.Empty;
            if (this.sandCollectPoseDeleteMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                status = "PoseDeleteBuild method missing";
                return false;
            }
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "aura not ready";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            uint arg = netId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&arg);
            IntPtr boxed = auraMonoRuntimeInvoke(this.sandCollectPoseDeleteMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "PoseDeleteBuild exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            // Returns bool: false = a prior build option is still pending (not yet a success).
            if (boxed != IntPtr.Zero && this.TryUnboxMonoBoolean(boxed, out bool accepted) && !accepted)
            {
                status = "PoseDeleteBuild(" + netId + ") busy (prior option pending)";
                return false;
            }

            status = "PoseDeleteBuild(" + netId + ")";
            return true;
        }

        // --- GUI (New Features -> Sand Sculpture sub-tab) ---------------------------------------

        private float DrawSandSculptureTab(int startY)
        {
            const float left = 40f;
            float y = startY;

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 1f);
            GUIStyle bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            bodyStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.95f);
            GUIStyle mutedStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            mutedStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);

            Color okColor = new Color(0.45f, 1f, 0.55f);
            Color offColor = new Color(1f, 0.55f, 0.55f);

            GUI.Label(new Rect(left, y, 460f, 24f), this.L("Auto Sand Sculpture"), headerStyle);
            y += 34f;

            bool prevAutoSand = this.autoSandEnabled;
            this.autoSandEnabled = this.DrawSwitchToggle(new Rect(left, y, 360f, 30f), this.autoSandEnabled, this.L("Auto Sand Sculpture"));
            if (this.autoSandEnabled != prevAutoSand)
            {
                this.AddMenuNotification(this.L("Auto Sand Sculpture") + ": " + (this.autoSandEnabled ? this.L("On") : this.L("Off")),
                    this.autoSandEnabled ? okColor : offColor);
            }
            y += 38f;
            GUI.Label(new Rect(left, y, 540f, 32f), this.L("Places a base, sculpts the correct model, and repeats — fully automatic."), mutedStyle);
            y += 38f;

            bool prevAutoPlace = this.sandAutoPlaceBase;
            this.sandAutoPlaceBase = this.DrawSwitchToggle(new Rect(left, y, 420f, 30f), this.sandAutoPlaceBase, this.L("Auto-place base from backpack"));
            if (this.sandAutoPlaceBase != prevAutoPlace)
            {
                this.sandPlaceAttempts = 0;
                this.AddMenuNotification(this.L("Auto-place base from backpack") + ": " + (this.sandAutoPlaceBase ? this.L("On") : this.L("Off")),
                    this.sandAutoPlaceBase ? okColor : offColor);
            }
            y += 38f;

            bool prevAutoCollect = this.sandAutoCollect;
            this.sandAutoCollect = this.DrawSwitchToggle(new Rect(left, y, 420f, 30f), this.sandAutoCollect, this.L("Auto-collect finished sculptures"));
            if (this.sandAutoCollect != prevAutoCollect)
            {
                this.sandCollectNextAt = 0f;
                this.AddMenuNotification(this.L("Auto-collect finished sculptures") + ": " + (this.sandAutoCollect ? this.L("On") : this.L("Off")),
                    this.sandAutoCollect ? okColor : offColor);
            }
            y += 38f;

            GUI.Label(new Rect(left, y, 220f, 22f), this.L("Finish delay") + $": {this.sandFinishDelaySeconds:F1}s", bodyStyle);
            this.sandFinishDelaySeconds = Mathf.Round(
                GUI.HorizontalSlider(new Rect(left + 240f, y + 6f, 200f, 18f), this.sandFinishDelaySeconds, 0f, 30f) * 2f) / 2f;
            y += 40f;

            GUI.Box(new Rect(left, y, 560f, 130f), string.Empty);
            float boxTextY = y + 8f;
            GUI.Label(new Rect(left + 10f, boxTextY, 540f, 20f),
                this.L("State") + $": {this.sandApiState}   " + this.L("Done") + $": {this.sandSculpturesDone}   " + this.L("Collected") + $": {this.sandCollectedCount}",
                bodyStyle);
            boxTextY += 24f;
            GUI.Label(new Rect(left + 10f, boxTextY, 540f, 34f), this.L("Status") + $": {this.sandLastActionStatus}", mutedStyle);
            boxTextY += 34f;
            if (!string.IsNullOrEmpty(this.sandLastPlaceStatus))
            {
                GUI.Label(new Rect(left + 10f, boxTextY, 540f, 30f), this.L("Place") + $": {this.sandLastPlaceStatus}", mutedStyle);
                boxTextY += 28f;
            }
            if (!string.IsNullOrEmpty(this.sandLastCollectStatus))
            {
                GUI.Label(new Rect(left + 10f, boxTextY, 540f, 30f), this.L("Collect") + $": {this.sandLastCollectStatus}", mutedStyle);
            }
            y += 140f;

            if (GUI.Button(new Rect(left, y, 220f, 30f), this.L("Reset base blacklist"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.sandBaseFailCounts.Clear();
                this.sandBaseBlacklist.Clear();
                this.sandPlaceAttempts = 0;
                this.AddMenuNotification(this.L("Reset base blacklist"), okColor);
            }

            if (GUI.Button(new Rect(left + 230f, y, 220f, 30f), this.L("Close stuck dialog"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                bool dialogClosed = this.TryCloseSandModelDialog(out bool dialogWasOpen, out string dialogStatus);
                this.SandLogStatus("manual close dialog: " + dialogStatus);
                this.AddMenuNotification(this.L("Close stuck dialog"), (dialogClosed && dialogWasOpen) ? okColor : new Color(1f, 0.75f, 0.45f));
            }

            y += 40f;
            return y + 20f;
        }
    }
}
