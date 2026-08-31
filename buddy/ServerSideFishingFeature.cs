using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Server-Side Fishing (EXPERIMENTAL) — fish by talking to the server directly, with no game
    // mode and no player FSM state involved at all.
    //
    // WHY IT EXISTS. Normal fishing is a full client construct: GameplayApi.EnterFishing ->
    // GameFishingMode -> PlayerStateFishing, and the buoy activation is emitted from that state's
    // OnStateEnter. That is why fishing is impossible from a vehicle seat, why it hijacks the
    // camera and the HUD, and why it replicates a fishing animation to everyone. Insect catching
    // has none of that — InsectProtocolManager.CatchingInsect is one fire-and-forget command — and
    // that is exactly why the insect farm keeps working in situations fishing cannot. This feature
    // gives fishing the same shape.
    //
    // THE PROTOCOL, and it is only four calls (all FishingProtocolManager, all plain
    // WebRequestUtility.SendCommand):
    //   1. CastRod()                                   -> CastRodNetworkCommand
    //      server answers CmdCastRodResult(bool) and creates the buoy entity; FishShadowSyncSystem
    //      turns ComponentAdded<BuoyComponent> into CmdAddRodBuoy, which fills
    //      PlayerDataComponent._floatData.floatNetId — that netId is our handshake.
    //   2. NotifyFloatInWater(floatNetId, basePos, direction, successLength, failureLength)
    //                                                  -> ActivateRodBuoyNetworkCommand
    //      server answers CmdActivateRodBuoyResult(bool); the buoy is now live and fish approach.
    //   3. FishingRodPull(true) on CmdOnFishBait        -> PullRodNetworkCommand
    //   4. server answers CmdFishBattleResult(result, fishId, failReason); the reward is granted
    //      server-side, exactly as in the normal flow.
    // Nothing else in the vanilla path carries information the server needs — the rest is the
    // client's own presentation.
    //
    // ARGUMENT VALUES follow FishHelper.ComputeFloatInWaterData, which the FSM path would have run:
    // basePos IS the cast target, and direction is `playerPos - tarPos`. successLength is the
    // Instant Catch spoof constant — with that feature on, its NotifyFloatInWater detour rewrites
    // this argument at the source anyway, so the two compose rather than fight. failureLength is a
    // fixed 30: the local PlayerFloatData.failLength reads garbage for self (float.MaxValue has
    // been seen) and the server rejects it — see the fishing-instant-catch note.
    //
    // ⚠️ EXPERIMENTAL, AND THE CORE ASSUMPTION IS UNVERIFIED. Whether the server creates the buoy
    // and accepts the activation for a player who never entered the client-side fishing state can
    // only be answered by running it — the server code is not in any dump. Everything here is
    // therefore built to report loudly rather than to look like it worked: every phase logs its
    // transition and its timeout, and a refusal at step 1 or 2 ends the attempt instead of leaving
    // a half-open session. Off by default.
    //
    // KNOWN CONSEQUENCES, by design rather than by accident:
    //   * ThemisManager.SetPlayerState(Fish) is never set — that call lives in
    //     PlayerStateFishing.OnStateEnter, which never runs. The tag stays Idle while fishing
    //     commands flow. This is the same mismatch the insect farm already has (see the
    //     themis-player-state-tags note), but it is a real difference from vanilla fishing.
    //   * Nothing replicates: no FishingStatus, no fishing motion, no throw. Other players see you
    //     standing still. That makes this inherently stealthy, and it makes the Hide Fishing From
    //     Others toggle redundant while it is on.
    //   * No local FSM means no rod/line/buoy visuals and no fishing camera or panel for you either.
    //   * There is no client-side battle state to poll, so the session is driven purely by the
    //     server events this feature hooks.
    public partial class HeartopiaComplete
    {
        private enum ServerFishPhase
        {
            Idle,
            CastSent,     // waiting for the server to create the buoy (floatNetId != 0)
            Activating,   // NotifyFloatInWater sent, waiting for CmdActivateRodBuoyResult
            Waiting,      // buoy live, waiting for CmdOnFishBait
            Battle,       // bite seen, pull pressed, waiting for CmdFishBattleResult
        }

        // failureLength MUST be this fixed value — self PlayerFloatData.failLength reads garbage
        // (float.MaxValue observed) and the server rejects the command outright.
        private const float ServerFishFailureLength = 30f;

        // CastRod and the activation are two separate Unreliable commands, and on the vanilla path
        // the throw clip sits between them (~1.5-2 s; Skip Cast Animation already compresses that to
        // almost nothing and still works). A short settle beat costs nothing and keeps the ordering
        // sane rather than firing both in the same frame.
        private const float ServerFishActivateDelaySeconds = 0.25f;
        private const float ServerFishBuoyWaitSeconds = 4f;    // CastRod -> a usable floatNetId
        private const float ServerFishActivateWaitSeconds = 4f; // NotifyFloatInWater -> result
        // Measured over 56 live casts (Ideapad, 2026-08-31): the server assigns a fish
        // (SetOnBaitFishShadowId) at median 1.3 s, p90 6.2 s, max 9.7 s, and the bite
        // (FishBaitActionResult) lands at median 3.1 s, max 10.4 s. So a cast with no fish assigned
        // by 12 s is dead, and an assigned one with no bite by 20 s is dead too — both well clear of
        // the observed maxima. The original flat 45 s burned ~30 s per miss for nothing (7 of the 56
        // casts missed).
        private const float ServerFishNoFishAssignedSeconds = 12f; // buoy live -> fish assigned
        private const float ServerFishBiteWaitSeconds = 20f;       // fish assigned -> bite
        private const float ServerFishBattleWaitSeconds = 20f;  // bite -> battle result

        private static bool serverFishEnabledStatic;

        private bool serverFishEnabled;
        private bool serverFishHooksRegistered;

        private ServerFishPhase serverFishPhase;
        private float serverFishPhaseSince;
        private Vector3 serverFishTargetPos;
        private uint serverFishBuoyNetId;
        private uint serverFishBaitingFishNetId;
        private int serverFishCastSeq;
        private string serverFishStatus = "idle";

        // Event-side flags, written from the game-event hooks and drained on the next tick so all
        // protocol sends stay on the mod's own update.
        private static bool serverFishCastRefused;
        private static bool serverFishActivateAnswered;
        private static bool serverFishActivateOk;
        private static bool serverFishBiteSeen;
        private static uint serverFishBiteFishNetId;
        private static bool serverFishResultSeen;
        private static bool serverFishResultOk;
        private static int serverFishResultFishId;
        private static int serverFishResultFailReason;
        private static bool serverFishResetSeen;

        // Diagnostics for the "activation accepted but no bite" case. The server tells us what it
        // thinks of the buoy through PlayerFloatData (CmdUpdateRodBuoyData) and whether it picked a
        // fish through CmdSetOnBaitFishShadowId — without those two, a bite timeout says nothing
        // about WHY.
        private bool serverFishWaitProbed;
        private static uint serverFishSelectedFishNetId;
        private static bool serverFishSelectedSeen;
        private static float serverFishEventT0;

        // ---- public surface (UI + config + the farm) --------------------------------------------

        public bool GetServerSideFishingEnabled()
        {
            return this.serverFishEnabled;
        }

        public void SetServerSideFishingEnabled(bool value)
        {
            if (this.serverFishEnabled == value)
            {
                return;
            }

            this.serverFishEnabled = value;
            serverFishEnabledStatic = value;
            FeatureLog.Toggle("ServerFish", value);

            if (value)
            {
                FeatureLog.Life("ServerFish",
                    "EXPERIMENTAL: fishing will run as raw protocol — no fishing mode, no FSM state,"
                    + " no Themis Fish tag, no visuals for you and nothing replicated to others.");
            }
            else
            {
                this.ResetServerSideFishingSession("toggle off", cancelOnServer: true);
            }
        }

        public string GetServerSideFishingStatus()
        {
            return this.serverFishStatus;
        }

        // True while a protocol session is in flight — the farm must not start another cast.
        public bool IsServerSideFishingSessionActive(out string status)
        {
            status = this.serverFishStatus;
            return this.serverFishPhase != ServerFishPhase.Idle;
        }

        // ---- the cast ----------------------------------------------------------------------------

        // Replaces TryEnterFishingAtTarget while the toggle is on. Only fires step 1; the tick below
        // carries the session from there.
        public bool TryServerSideFishingCast(Vector3 targetPos, out string status)
        {
            if (this.serverFishPhase != ServerFishPhase.Idle)
            {
                status = "Server-side session already running (" + this.serverFishPhase + ")";
                return false;
            }

            this.EnsureServerSideFishingHooks();

            // NOT a handshake baseline. The buoy is the ROD'S OWN float: HandHoldFishingRod
            // .OnAttached creates it at equip time and CmdAddRodBuoy fills PlayerFloatData, so
            // floatNetId is already populated before the cast and does NOT change per cast. Vanilla
            // agrees — PlayerStateFishing.FloatInWater just reads FloatData.floatNetId as-is and
            // sends. Waiting for a *different* id (v1 did) can therefore never succeed.
            this.TryReadFishingFloatData(out uint rodBuoyNetId, out _, out _, out _, out _);
            this.serverFishBuoyNetId = 0u;

            serverFishCastRefused = false;
            serverFishActivateAnswered = false;
            serverFishBiteSeen = false;
            serverFishResultSeen = false;
            serverFishResetSeen = false;

            if (!this.TryInvokeFishingCastRodMono(out string castStatus))
            {
                status = "Server-side CastRod failed: " + castStatus;
                this.serverFishStatus = status;
                FeatureLog.Fail("ServerFish", status);
                return false;
            }

            this.serverFishCastSeq++;
            serverFishEventT0 = Time.unscaledTime;
            this.serverFishTargetPos = targetPos;
            this.SetServerFishPhase(ServerFishPhase.CastSent);
            this.AutoFishLog("=== SERVER-SIDE CAST #" + this.serverFishCastSeq + " === target=" + targetPos
                + " rodBuoy=" + rodBuoyNetId);
            status = "Server-side cast sent";
            return true;
        }

        // ---- per-frame session tick ---------------------------------------------------------------

        private void ProcessServerSideFishingOnUpdate()
        {
            if (!this.serverFishEnabled)
            {
                serverFishEnabledStatic = false;
                return;
            }

            serverFishEnabledStatic = true;

            if (this.serverFishPhase == ServerFishPhase.Idle)
            {
                return;
            }

            float now = Time.unscaledTime;
            float inPhase = now - this.serverFishPhaseSince;

            // The server's own reset ends any phase — same meaning as in the FSM path.
            if (serverFishResetSeen)
            {
                serverFishResetSeen = false;
                this.ResetServerSideFishingSession("server ResetFishState", cancelOnServer: false);
                return;
            }

            switch (this.serverFishPhase)
            {
                case ServerFishPhase.CastSent:
                    if (serverFishCastRefused)
                    {
                        serverFishCastRefused = false;
                        // A refusal at step 1 is the server saying "you may not cast right now", and
                        // it says nothing about why. FishingCommand gates the vanilla button on
                        // stamina (GetStaminaCurrValue > TableInteractions[600].staminaCost) and on
                        // tool durability (ToolSystem.GetToolCanUse), so report both — the mod
                        // already caches them off PlayerStaminaUpdatedEvent / HandHoldUpdatedEvent.
                        this.ResetServerSideFishingSession(
                            "server refused the cast (CmdCastRodResult=false) — " + this.DescribeServerFishResources(),
                            cancelOnServer: false);
                        return;
                    }

                    if (inPhase < ServerFishActivateDelaySeconds)
                    {
                        return; // let CastRod land first
                    }

                    // Use whatever buoy the rod currently has — exactly what FloatInWater does.
                    if (this.TryReadFishingFloatData(out uint netId, out _, out _, out _, out _)
                        && netId != 0u)
                    {
                        this.serverFishBuoyNetId = netId;
                        this.SendServerSideFishingActivate(netId);
                        return;
                    }

                    if (inPhase >= ServerFishBuoyWaitSeconds)
                    {
                        // floatNetId still 0 = the rod has no float registered at all (no rod
                        // equipped, or CmdAddRodBuoy never arrived) — not a verdict on the server
                        // accepting a headless cast.
                        this.ResetServerSideFishingSession(
                            "FloatData.floatNetId is still 0 after " + inPhase.ToString("F1")
                            + "s — the rod has no registered float",
                            cancelOnServer: true);
                    }
                    return;

                case ServerFishPhase.Activating:
                    if (serverFishActivateAnswered)
                    {
                        serverFishActivateAnswered = false;
                        if (!serverFishActivateOk)
                        {
                            this.ResetServerSideFishingSession("server refused the buoy (CmdActivateRodBuoyResult=false)", cancelOnServer: true);
                            return;
                        }

                        this.SetServerFishPhase(ServerFishPhase.Waiting);
                        this.AutoFishLog("SERVER-SIDE buoy active, waiting for a bite (buoy=" + this.serverFishBuoyNetId + ")");
                        return;
                    }

                    // No answer is not necessarily a refusal — the channel is Unreliable. Fall
                    // through to Waiting and let the bite timeout be the real verdict.
                    if (inPhase >= ServerFishActivateWaitSeconds)
                    {
                        this.SetServerFishPhase(ServerFishPhase.Waiting);
                        this.AutoFishLog("SERVER-SIDE no activation answer in " + inPhase.ToString("F1")
                            + "s — waiting for a bite anyway (the command channel is Unreliable)");
                    }
                    return;

                case ServerFishPhase.Waiting:
                    if (serverFishBiteSeen)
                    {
                        serverFishBiteSeen = false;
                        // CmdOnFishBait does not fire in practice (its vanilla listener is empty and
                        // the bite arrives as FishBaitActionResult, which carries no id), so the fish
                        // id comes from the server's earlier SetOnBaitFishShadowId.
                        this.serverFishBaitingFishNetId = serverFishBiteFishNetId != 0u
                            ? serverFishBiteFishNetId
                            : serverFishSelectedFishNetId;
                        this.SetServerFishPhase(ServerFishPhase.Battle);
                        this.TryInvokeFishingPullProtocolMono(true, out string pullStatus);
                        this.AutoFishLog("SERVER-SIDE bite after " + inPhase.ToString("F1") + "s fish="
                            + this.serverFishBaitingFishNetId + " -> pull(true) " + pullStatus);
                        return;
                    }

                    // One probe a second in: by then CmdUpdateRodBuoyData has had time to land, so
                    // available/basePos are the server's own opinion of the buoy rather than ours.
                    if (!this.serverFishWaitProbed && inPhase >= 1f)
                    {
                        this.serverFishWaitProbed = true;
                        this.AutoFishLog("SERVER-SIDE buoy state 1s after activation: " + this.DescribeServerFishBuoyState());
                    }

                    // Two distinct dead ends, worth telling apart: the server never picked a fish
                    // for this buoy at all, or it picked one that then never bit.
                    if (!serverFishSelectedSeen && inPhase >= ServerFishNoFishAssignedSeconds)
                    {
                        this.AutoFishLog("SERVER-SIDE no fish assigned — final buoy state: " + this.DescribeServerFishBuoyState());
                        this.ResetServerSideFishingSession(
                            "server assigned no fish in " + inPhase.ToString("F0") + "s", cancelOnServer: true);
                        return;
                    }

                    if (inPhase >= ServerFishBiteWaitSeconds)
                    {
                        this.AutoFishLog("SERVER-SIDE no bite — final buoy state: " + this.DescribeServerFishBuoyState());
                        this.ResetServerSideFishingSession(
                            "fish " + serverFishSelectedFishNetId + " assigned but no bite in "
                            + inPhase.ToString("F0") + "s", cancelOnServer: true);
                    }
                    return;

                case ServerFishPhase.Battle:
                    if (serverFishResultSeen)
                    {
                        serverFishResultSeen = false;
                        this.TryInvokeFishingPullProtocolMono(false, out _);
                        this.AutoFishLog("SERVER-SIDE result after " + inPhase.ToString("F1") + "s success="
                            + serverFishResultOk + " fishId=" + serverFishResultFishId
                            + " reason=" + ServerFishFailReasonName(serverFishResultFailReason));
                        this.ResetServerSideFishingSession(
                            serverFishResultOk ? "caught " + serverFishResultFishId : "battle lost",
                            cancelOnServer: false);
                        return;
                    }

                    if (inPhase >= ServerFishBattleWaitSeconds)
                    {
                        this.TryInvokeFishingPullProtocolMono(false, out _);
                        this.ResetServerSideFishingSession("no battle result in " + inPhase.ToString("F0") + "s", cancelOnServer: true);
                    }
                    return;
            }
        }

        private void SetServerFishPhase(ServerFishPhase phase)
        {
            this.serverFishPhase = phase;
            this.serverFishPhaseSince = Time.unscaledTime;
            this.serverFishStatus = "Server-side: " + phase;
        }

        private void ResetServerSideFishingSession(string reason, bool cancelOnServer)
        {
            if (this.serverFishPhase != ServerFishPhase.Idle)
            {
                this.AutoFishLog("SERVER-SIDE session end (" + this.serverFishPhase + "): " + reason);
                if (cancelOnServer)
                {
                    // The server still believes a session is open; CancelFishing is its own tear-down.
                    this.TryCancelFishingProtocolMono(out _);
                }
            }

            // One server-side session == one cast cycle, and this is its falling edge. AutoFishingFarm
            // normally requests the durability check on `IsInFishingSession` going false, but that
            // flag is driven by the polled FSM state, which stays false forever in this mode — so the
            // rod wore all the way to 2/200 and every cast came back CmdCastRodResult=false with no
            // repair ever triggered. Internally throttled to 1/s and a no-op when auto-repair is off.
            this.RequestDurabilityCheck();

            this.serverFishPhase = ServerFishPhase.Idle;
            this.serverFishPhaseSince = Time.unscaledTime;
            this.serverFishBaitingFishNetId = 0u;
            this.serverFishStatus = "Server-side idle (" + reason + ")";

            serverFishCastRefused = false;
            serverFishActivateAnswered = false;
            serverFishBiteSeen = false;
            serverFishResultSeen = false;
            serverFishResetSeen = false;
            serverFishSelectedSeen = false;
            serverFishSelectedFishNetId = 0u;
            this.serverFishWaitProbed = false;
        }

        // The two client-side gates the vanilla cast checks, for attributing a step-1 refusal.
        private string DescribeServerFishResources()
        {
            string energy = "energy=?";
            try
            {
                if (this.cachedEnergyMax > 0)
                {
                    energy = "energy=" + this.cachedEnergyCurrent + "/" + this.cachedEnergyMax;
                }
            }
            catch
            {
            }

            string durability = "durability=?";
            try
            {
                if (this.TryGetCurrentToolDurability(out int toolId, out int dur, out int maxDur, out string durStatus))
                {
                    durability = "durability=" + dur + "/" + maxDur + " tool=" + toolId;
                }
                else
                {
                    durability = "durability=? (" + durStatus + ")";
                }
            }
            catch
            {
            }

            return energy + " " + durability;
        }

        // What the server currently believes about our buoy — the verdict line for a bite timeout.
        private string DescribeServerFishBuoyState()
        {
            if (!this.TryReadFishingFloatData(out uint netId, out Vector3 dir, out float failLen,
                    out Vector3 basePos, out bool available, out float successLen, out string readStatus))
            {
                return "float data unreadable (" + readStatus + ")";
            }

            return "buoy=" + netId + " available=" + available + " base=" + basePos
                + " success=" + successLen.ToString("F2") + " fail=" + failLen.ToString("F1")
                + " dir=" + dir
                + " serverPickedFish=" + (serverFishSelectedSeen ? serverFishSelectedFishNetId.ToString() : "none");
        }

        private static string ServerFishFailReasonName(int reason)
        {
            switch (reason)
            {
                case 0: return "None";
                case 1: return "Distance";
                case 2: return "LineBreak";
                case 3: return "TimeOut";
                default: return reason.ToString();
            }
        }

        // ---- step 2 ------------------------------------------------------------------------------

        private void SendServerSideFishingActivate(uint floatNetId)
        {
            // FishHelper.ComputeFloatInWaterData: basePos IS the target, direction is playerPos-tarPos.
            Vector3 basePos = this.serverFishTargetPos;
            Vector3 playerPos = basePos;
            if (this.TryGetCastFacingSelfPositionMono(out Vector3 selfPos, out _))
            {
                playerPos = selfPos;
            }

            Vector3 direction = playerPos - basePos;
            float successLength = InstantCatchSpoofedSuccessLength;

            if (this.TryInvokeFishingNotifyFloatInWaterMono(floatNetId, basePos, direction, successLength,
                    ServerFishFailureLength, out string activateStatus))
            {
                this.SetServerFishPhase(ServerFishPhase.Activating);
                this.AutoFishLog("SERVER-SIDE buoy=" + floatNetId + " activate sent base=" + basePos
                    + " dir=" + direction + " success=" + successLength.ToString("F2")
                    + " fail=" + ServerFishFailureLength.ToString("F0") + " (" + activateStatus + ")");
                return;
            }

            this.ResetServerSideFishingSession("NotifyFloatInWater failed: " + activateStatus, cancelOnServer: true);
        }

        // ---- Mono senders ------------------------------------------------------------------------

        // FishingProtocolManager.CastRod() — static, no arguments.
        private unsafe bool TryInvokeFishingCastRodMono(out string status)
        {
            status = "CastRod Mono unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "CastRod Mono runtime unavailable";
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Fishing.FishingProtocolManager");
                if (classPtr == IntPtr.Zero)
                {
                    status = "FishingProtocolManager Mono class unavailable";
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, "CastRod", 0);
                if (methodPtr == IntPtr.Zero)
                {
                    status = "FishingProtocolManager.CastRod(0) Mono method unavailable";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "CastRod Mono exception";
                    return false;
                }

                status = "CastRod sent";
                return true;
            }
            catch (Exception ex)
            {
                status = "CastRod Mono failed: " + ex.Message;
                return false;
            }
        }

        // FishingProtocolManager.NotifyFloatInWater(uint, Vector3, Vector3, float, float) — static.
        // Value-type arguments go in as pointers to the raw values, the same shape the Transfer
        // invoke in TrySyncLocalPlayerCastFacingMono uses. NOTE this is the very method the Instant
        // Catch detour wraps: calling it here goes THROUGH that detour, so successLength is rewritten
        // at the source exactly as it is on the vanilla path. That is intended.
        private unsafe bool TryInvokeFishingNotifyFloatInWaterMono(uint floatNetId, Vector3 buoyPos,
            Vector3 direction, float successLength, float failureLength, out string status)
        {
            status = "NotifyFloatInWater Mono unavailable";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "NotifyFloatInWater Mono runtime unavailable";
                    return false;
                }

                IntPtr classPtr = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Fishing.FishingProtocolManager");
                if (classPtr == IntPtr.Zero)
                {
                    status = "FishingProtocolManager Mono class unavailable";
                    return false;
                }

                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, "NotifyFloatInWater", 5);
                if (methodPtr == IntPtr.Zero)
                {
                    status = "FishingProtocolManager.NotifyFloatInWater(5) Mono method unavailable";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[5];
                uint netIdArg = floatNetId;
                Vector3 posArg = buoyPos;
                Vector3 dirArg = direction;
                float successArg = successLength;
                float failArg = failureLength;
                args[0] = (IntPtr)(&netIdArg);
                args[1] = (IntPtr)(&posArg);
                args[2] = (IntPtr)(&dirArg);
                args[3] = (IntPtr)(&successArg);
                args[4] = (IntPtr)(&failArg);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "NotifyFloatInWater Mono exception";
                    return false;
                }

                status = "NotifyFloatInWater sent";
                return true;
            }
            catch (Exception ex)
            {
                status = "NotifyFloatInWater Mono failed: " + ex.Message;
                return false;
            }
        }

        // ---- server events -----------------------------------------------------------------------

        // Registered separately from AutoFishingFarm's own hooks — RegisterGameEventHook simply adds
        // another handler to the shared dispatch detour, so both surfaces observe the same events.
        private void EnsureServerSideFishingHooks()
        {
            if (this.serverFishHooksRegistered)
            {
                return;
            }

            this.serverFishHooksRegistered = true;
            this.RegisterGameEventHook("XDTDataAndProtocol.Events.CmdCastRodResult", 4, OnServerFishCastResultEvent);
            this.RegisterGameEventHook("XDTDataAndProtocol.Events.CmdActivateRodBuoyResult", 4, OnServerFishActivateResultEvent);
            this.RegisterGameEventHook("XDTDataAndProtocol.Events.CmdOnFishBait", 4, OnServerFishBaitEvent);
            this.RegisterGameEventHook("XDTDataAndProtocol.Events.CmdSetOnBaitFishShadowId", 8, OnServerFishFishSelectedEvent);  // {uint fishShadowNetId@0; bool needShowOff@4}
            this.RegisterGameEventHook("XDTDataAndProtocol.Events.CmdFishBaitActionResult", 4, OnServerFishBaitActionResultEvent);  // {bool result@0}
            this.RegisterGameEventHook("XDTDataAndProtocol.Events.CmdFishBattleResult", 12, OnServerFishBattleResultEvent);
            this.RegisterGameEventHook("XDTDataAndProtocol.Events.ResetFishState", 0, OnServerFishResetEvent);
            FeatureLog.Life("ServerFish", "protocol event hooks registered");
        }

        // Relative timeline for the whole server→client fishing fan-out. Without it a bite timeout
        // says nothing about which step the server actually reached.
        private static void ServerFishEventLog(string message)
        {
            if (!serverFishEnabledStatic)
            {
                return;
            }

            float t;
            try { t = Time.unscaledTime - serverFishEventT0; } catch { t = 0f; }
            FeatureLog.Life("ServerFish", "+" + t.ToString("F1") + "s " + message);
        }

        private static void OnServerFishCastResultEvent(GameEventSnapshot e)
        {
            bool ok = e.ReadBool(0);
            ServerFishEventLog("CastRodResult=" + ok);
            if (serverFishEnabledStatic && !ok)
            {
                serverFishCastRefused = true;
            }
        }

        private static void OnServerFishActivateResultEvent(GameEventSnapshot e)
        {
            ServerFishEventLog("ActivateRodBuoyResult=" + e.ReadBool(0));
            if (serverFishEnabledStatic)
            {
                serverFishActivateOk = e.ReadBool(0);
                serverFishActivateAnswered = true;
            }
        }

        // The REAL battle-start signal in vanilla: PlayerDataComponent routes
        // CmdFishBaitActionResult into PlayerStateFishing.SetFishingBaitState, which is what flips
        // FishState to Battle. CmdOnFishBait's own listener (PlayerDataComponent.FishOnBait) has an
        // EMPTY body — it is a notification, not the trigger. So accept either, whichever lands.
        private static void OnServerFishBaitActionResultEvent(GameEventSnapshot e)
        {
            bool ok = e.ReadBool(0);
            ServerFishEventLog("FishBaitActionResult=" + ok);
            if (serverFishEnabledStatic && ok)
            {
                serverFishBiteSeen = true;
            }
        }

        // Server→client "this fish is now on your hook line". Arrives BEFORE CmdOnFishBait and is
        // the proof that the server actually assigned a fish to our buoy.
        private static void OnServerFishFishSelectedEvent(GameEventSnapshot e)
        {
            if (serverFishEnabledStatic)
            {
                serverFishSelectedFishNetId = e.ReadUInt32(0);
                serverFishSelectedSeen = true;
            }

            ServerFishEventLog("SetOnBaitFishShadowId fish=" + e.ReadUInt32(0) + " showOff=" + e.ReadBool(4));
        }

        private static void OnServerFishBaitEvent(GameEventSnapshot e)
        {
            if (serverFishEnabledStatic)
            {
                serverFishBiteFishNetId = e.ReadUInt32(0);
                serverFishBiteSeen = true;
            }

            ServerFishEventLog("OnFishBait fish=" + e.ReadUInt32(0));
        }

        private static void OnServerFishBattleResultEvent(GameEventSnapshot e)
        {
            if (serverFishEnabledStatic)
            {
                serverFishResultOk = e.ReadBool(0);
                serverFishResultFishId = e.ReadInt32(4);
                serverFishResultFailReason = e.ReadInt32(8);
                serverFishResultSeen = true;
            }

            ServerFishEventLog("FishBattleResult ok=" + e.ReadBool(0) + " fishId=" + e.ReadInt32(4)
                + " reason=" + ServerFishFailReasonName(e.ReadInt32(8)));
        }

        private static void OnServerFishResetEvent(GameEventSnapshot e)
        {
            ServerFishEventLog("ResetFishState");
            if (serverFishEnabledStatic)
            {
                serverFishResetSeen = true;
            }
        }
    }
}
