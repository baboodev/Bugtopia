using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Stealth Block — the privacy half of Stealth Foraging.
    //
    // WHAT IT DOES: while armed, every OTHER player in the town is on our block list, which the
    // game turns into MUTUAL invisibility (RemotePlayerLogicComponent sets ProxyFlags.Block from
    // BlockState, and the server mirrors our block to them as BeBlockComponent -> their client
    // hides us too). That is what keeps an under-terrain farm run from being watched.
    //
    // LIFECYCLE: the checkbox is a setting, not the trigger. Blocking begins when Start Foraging is
    // pressed (StealthBlockEnabled && autoFarmActive) and every block we issued is released when
    // the run ends, however it ends. An idle farm blocks nobody.
    //
    // WHY IT IS A STATE MACHINE AND NOT A TOGGLE:
    //   * Arming — the farm must NOT dive until every stranger is CONFIRMED blocked and no friend
    //     is in the room. Confirmation is the server-synced BlockListProtocolManager
    //     .IsPlayerInBlockList(shortId), never "we sent the command" (sends can be rejected
    //     silently, exactly like the insect-catch ACK).
    //   * Friends are excluded UNCONDITIONALLY. Blocking a friend DELETES the friendship
    //     server-side and unblocking does NOT restore it (the game's own ShieldPanel calls it
    //     "Delete and block", loc 200002362/200002425). A friend in the room therefore blocks
    //     arming instead of being blocked.
    //   * Unblock on ROSTER LEAVE (not on PlayerUnSpawnEvent, which is proximity streaming and
    //     would storm the server on every stroll) keeps the list bounded: the town holds 12
    //     (MapPanel "X/12", 24 in SeaWorld), so the steady state stays far below the block-list
    //     cap (LevelScriptableConfig.shieldplayconfig.maxshieldcount / ErrorCode.BlockMaxNum=425).
    //
    // ROSTER SOURCE: MapSpotsSystem, the client's room-wide player roster (MaxMapDataComponent —
    // synced for everyone in the room regardless of distance, unlike PlayerComponent/the view
    // layer). GetPlayerCount()/GetFriendsCount() are 0-arg scalars used as the cheap change
    // detector; the expensive GetMapSpots() walk only runs when a count moves or on the slow
    // safety reconcile.
    //
    // THE EMPTY-ROSTER TRAP: a failed/blank roster read is NOT "everyone left" — during a world
    // change it would fan out an unblock for every entry and re-block them right after. Self is
    // always present in the roster as a SpotEnum.Player spot, so "our own netId is missing" means
    // the read is untrustworthy and the whole diff is skipped.
    public partial class HeartopiaComplete
    {
        private enum StealthBlockPhase
        {
            Off,
            Arming,
            Armed,
            Blocked      // cannot arm (friend present / block-list full / resolve failure)
        }

        // ---- persisted ----
        public bool stealthBlockEnabled;          // master: mass-block the town while farming
        public bool stealthBlockNotifyFriends;    // friend joins -> surface + stop the farm
        // Registry of the blocks WE issued. Persisted because unblock-on-leave makes this the only
        // record: a crash mid-run would otherwise leave the server list dirty with no way to know
        // which entries were ours vs the user's own manual blocks.
        public readonly List<long> stealthBlockOwnedShortIds = new List<long>();

        // ---- runtime ----
        private StealthBlockPhase stealthBlockPhase = StealthBlockPhase.Off;
        private string stealthBlockStatus = "Off";
        private FeatureBreakerState stealthBlockBreaker;

        private readonly HashSet<uint> stealthBlockRoster = new HashSet<uint>();
        private readonly Dictionary<uint, long> stealthBlockShortIdByNetId = new Dictionary<uint, long>();
        private readonly Dictionary<uint, float> stealthBlockUnblockDueAt = new Dictionary<uint, float>();
        private readonly List<uint> stealthBlockScanNetIds = new List<uint>();
        private readonly List<uint> stealthBlockTempNetIds = new List<uint>();

        private float stealthBlockNextPollAt;
        private float stealthBlockNextReconcileAt;
        private float stealthBlockNextSendAt;
        private float stealthBlockArmingStartedAt = -1f;
        private int stealthBlockLastPlayerCount = -1;
        private int stealthBlockLastFriendCount = -1;
        private bool stealthBlockFriendHoldLatched;
        private bool stealthBlockCleanupDone;

        internal static int stealthBlockIssuedCount;
        internal static int stealthBlockReleasedCount;

        private const float StealthBlockPollSeconds = 1f;
        private const float StealthBlockReconcileSeconds = 10f;
        // While arming, one command leaves per pass (the send throttle is shared with the release
        // queue), so the roster has to be walked repeatedly — a 10 s pass would blow the arm
        // timeout on a full town long before everyone is blocked.
        private const float StealthBlockArmingReconcileSeconds = 0.3f;
        private const float StealthBlockSendThrottleSeconds = 0.25f;
        private const float StealthBlockUnblockGraceSeconds = 45f;
        private const float StealthBlockArmTimeoutSeconds = 30f;
        private const int StealthBlockReasonDefault = 3;   // BlockReason.Default — least accusatory
        private const int StealthBlockSpotEnumPlayer = 3;  // SpotEnum.Player

        private const string StealthBlockBlockCommand =
            "XDT.Scene.Shared.Modules.Social.BlockList.BlockPlayerCommand";
        private const string StealthBlockUnblockCommand =
            "XDT.Scene.Shared.Modules.Social.BlockList.UnblockPlayerCommand";

        private static readonly string[] StealthBlockGameSystemImages =
        {
            "XDTGameSystem", "XDTGameSystem.dll", "Client", "Client.dll"
        };

        private static readonly string[] StealthBlockProtocolImages =
        {
            "XDTDataAndProtocol", "XDTDataAndProtocol.dll", "Client", "Client.dll"
        };

        // Resolved once per session (class/method IntPtrs stay raw — image lifetime).
        private IntPtr stealthBlockMapSpotsClass = IntPtr.Zero;
        private IntPtr stealthBlockGetPlayerCountMethod = IntPtr.Zero;
        private IntPtr stealthBlockGetFriendsCountMethod = IntPtr.Zero;
        private IntPtr stealthBlockGetMapSpotsMethod = IntPtr.Zero;
        private IntPtr stealthBlockTryGetShortIdMethod = IntPtr.Zero;
        private IntPtr stealthBlockFriendLevelMethod = IntPtr.Zero;
        private IntPtr stealthBlockIsBlockedMethod = IntPtr.Zero;

        // Both sends go through the shared HeartopiaComplete.TryAuraSendCommand; this records that
        // TryValidateAuraCommand has proved both command types and their fields resolve.
        private bool stealthBlockSendValidated;
        private int stealthBlockSpotCategoryOffset = -1;
        private int stealthBlockSpotUsageIdOffset = -1;
        private bool stealthBlockResolveFailedLogged;

        internal string GetStealthBlockStatus()
        {
            if (!this.stealthBlockEnabled && !this.stealthBlockNotifyFriends)
            {
                return "Off";
            }
            if (!this.autoFarmActive)
            {
                if (this.stealthBlockOwnedShortIds.Count > 0)
                {
                    return "Releasing " + this.stealthBlockOwnedShortIds.Count + " block(s)...";
                }
                return this.stealthBlockEnabled ? "Arms on Start Foraging" : "Watches on Start Foraging";
            }

            return this.stealthBlockStatus;
        }

        internal bool IsStealthBlockArmed => this.stealthBlockPhase == StealthBlockPhase.Armed;

        // The farm's per-frame gate (only ever reached with autoFarmActive == true). True = free to
        // run. Right after Start Foraging the phase is still Arming, so the whole state machine
        // holds until every stranger is confirmed blocked — that is what "block on start" means in
        // practice — and it holds again mid-run when a stranger walks in unblocked.
        private bool IsStealthBlockFarmHoldClear(out string holdReason)
        {
            holdReason = null;
            if (!this.stealthBlockEnabled || this.stealthBlockPhase == StealthBlockPhase.Armed)
            {
                return true;
            }

            holdReason = this.stealthBlockPhase == StealthBlockPhase.Blocked
                ? "Stealth blocked: " + this.stealthBlockStatus
                : "Arming stealth: " + this.stealthBlockStatus;
            return false;
        }

        // ------------------------------------------------------------------------------------
        // Tick
        // ------------------------------------------------------------------------------------

        private void ProcessStealthBlockOnUpdate()
        {
            float now = Time.unscaledTime;

            // Same contract as Stealth Foraging: the checkboxes are SETTINGS, the farm run is the
            // trigger. Blocking starts when Start Foraging is pressed and every block we issued is
            // released when the run ends — by the button, the auto-stop timer, Disable All, or the
            // friend-join stop. Nothing is blocked while the farm sits idle.
            //
            // The two toggles are independent. Stop When Friend Joins drives the SAME roster scan
            // but skips the block pass entirely, so it works as a plain safety net with Hide from
            // radar off — nothing gets blocked, the farm just stops when a friend turns up.
            bool blockActive = this.stealthBlockEnabled && this.autoFarmActive;
            bool watchActive = this.stealthBlockNotifyFriends && this.autoFarmActive;

            // Release keys off BLOCKING, not off the tick: turning Hide from radar off mid-run must
            // hand the blocks back even though the friend watch keeps the tick alive.
            if (!blockActive && this.stealthBlockOwnedShortIds.Count > 0 && this.IsWorldReady)
            {
                this.DrainStealthBlockReleases(now, releaseAll: true);
            }

            if (!blockActive && !watchActive)
            {
                if (this.stealthBlockPhase != StealthBlockPhase.Off)
                {
                    this.DisarmStealthBlock(this.autoFarmActive ? "toggles off" : "farm stopped");
                }
                return;
            }

            if (!this.IsWorldReady)
            {
                this.stealthBlockStatus = "Waiting for world...";
                return;
            }

            if (!this.stealthBlockBreaker.ShouldRun(now))
            {
                return;
            }

            try
            {
                // One-shot: reconcile the persisted registry against the live block list after a
                // crash/exit left entries behind.
                if (!this.stealthBlockCleanupDone)
                {
                    this.stealthBlockCleanupDone = true;
                    this.PruneStealthBlockRegistry();
                }

                if (this.stealthBlockPhase == StealthBlockPhase.Off)
                {
                    this.stealthBlockPhase = StealthBlockPhase.Arming;
                    this.stealthBlockArmingStartedAt = now;
                    this.stealthBlockNextReconcileAt = 0f;
                    this.stealthBlockStatus = "Scanning town...";
                }

                if (now >= this.stealthBlockNextPollAt)
                {
                    this.stealthBlockNextPollAt = now + StealthBlockPollSeconds;
                    this.PollStealthBlockCounters(now);
                }

                if (now >= this.stealthBlockNextReconcileAt)
                {
                    // Reconcile schedules its own next pass: fast while there is still someone to
                    // block (one command per pass through the shared send throttle, so the whole
                    // town must be walked several times to arm), slow once armed.
                    this.stealthBlockNextReconcileAt = now + StealthBlockReconcileSeconds;
                    this.ReconcileStealthBlockRoster(now, blockActive);
                }

                if (blockActive)
                {
                    this.DrainStealthBlockReleases(now, releaseAll: false);
                }
                this.stealthBlockBreaker.Success();
            }
            catch (Exception ex)
            {
                this.stealthBlockBreaker.Failure("StealthBlock", ex, now);
                this.stealthBlockStatus = "Error: " + ex.Message;
            }
        }

        // Cheap change detector: two scalar invokes. A moved count forces the expensive roster
        // walk on the next line of the tick instead of waiting for the slow reconcile.
        private void PollStealthBlockCounters(float now)
        {
            if (!this.TryStealthBlockInvokeScalar(this.stealthBlockMapSpotsClass, this.stealthBlockGetPlayerCountMethod, out int playerCount)
                || !this.TryStealthBlockInvokeScalar(this.stealthBlockMapSpotsClass, this.stealthBlockGetFriendsCountMethod, out int friendCount))
            {
                return;
            }

            if (playerCount != this.stealthBlockLastPlayerCount || friendCount != this.stealthBlockLastFriendCount)
            {
                this.stealthBlockLastPlayerCount = playerCount;
                this.stealthBlockLastFriendCount = friendCount;
                this.stealthBlockNextReconcileAt = 0f; // roster moved — reconcile now
            }
        }

        // blockActive == false means "watch only": scan the roster and report friends, but issue no
        // block commands and never hold the farm.
        private void ReconcileStealthBlockRoster(float now, bool blockActive)
        {
            if (!this.TryResolveStealthBlockBindings())
            {
                this.stealthBlockPhase = StealthBlockPhase.Blocked;
                this.stealthBlockStatus = "Game API unavailable";
                return;
            }

            if (!this.TryScanStealthBlockRoster(out bool selfSeen) || !selfSeen)
            {
                // Untrustworthy read (world change / service down). Leave the roster and the
                // release queue exactly as they are — see the empty-roster trap in the header.
                this.stealthBlockStatus = "Roster unavailable — holding";
                return;
            }

            // --- leave detection: netIds that vanished go into the grace queue ---
            this.stealthBlockTempNetIds.Clear();
            foreach (uint knownNetId in this.stealthBlockRoster)
            {
                if (!this.stealthBlockScanNetIds.Contains(knownNetId))
                {
                    this.stealthBlockTempNetIds.Add(knownNetId);
                }
            }

            for (int i = 0; i < this.stealthBlockTempNetIds.Count; i++)
            {
                uint goneNetId = this.stealthBlockTempNetIds[i];
                this.stealthBlockRoster.Remove(goneNetId);
                if (this.stealthBlockShortIdByNetId.ContainsKey(goneNetId) && !this.stealthBlockUnblockDueAt.ContainsKey(goneNetId))
                {
                    // Grace period absorbs town<->home bouncing: a return before it expires
                    // cancels the release instead of paying two commands per round trip.
                    this.stealthBlockUnblockDueAt[goneNetId] = now + StealthBlockUnblockGraceSeconds;
                }
            }

            // --- join detection + block pass ---
            int pending = 0;
            int confirmed = 0;
            bool friendPresent = false;
            int identifiedFriends = 0;

            for (int i = 0; i < this.stealthBlockScanNetIds.Count; i++)
            {
                uint netId = this.stealthBlockScanNetIds[i];
                this.stealthBlockRoster.Add(netId);
                this.stealthBlockUnblockDueAt.Remove(netId); // came back within the grace window

                if (this.TryStealthBlockIsFriend(netId))
                {
                    identifiedFriends++;
                    friendPresent = true;
                    continue; // NEVER block a friend — it deletes the friendship
                }

                if (!blockActive)
                {
                    // Watch-only: friends are the whole point, strangers are none of our business,
                    // so no shortId resolve and no block traffic at all.
                    continue;
                }

                if (!this.TryStealthBlockResolveShortId(netId, out long shortId) || shortId == 0L)
                {
                    pending++;
                    continue;
                }

                this.stealthBlockShortIdByNetId[netId] = shortId;

                if (this.TryStealthBlockIsBlocked(shortId, out bool blocked) && blocked)
                {
                    // Counts toward arming either way — stealth only cares that they cannot see
                    // us. A block that was already there is NOT adopted into the registry, so the
                    // release pass can never undo a decision that was not ours.
                    confirmed++;
                    continue;
                }

                pending++;
                if (now >= this.stealthBlockNextSendAt)
                {
                    this.stealthBlockNextSendAt = now + StealthBlockSendThrottleSeconds;
                    if (this.TryStealthBlockSendBlock(shortId))
                    {
                        stealthBlockIssuedCount++;
                        if (!this.stealthBlockOwnedShortIds.Contains(shortId))
                        {
                            this.stealthBlockOwnedShortIds.Add(shortId);
                            try { this.SaveKeybinds(false); } catch { }
                        }
                    }
                }
            }

            // Fail-closed friend check: the level cache can lag a fresh arrival, so if the game
            // counts more friends in the room than we could identify, treat it as "a friend is
            // here" rather than diving in front of one.
            if (this.stealthBlockLastFriendCount > identifiedFriends)
            {
                friendPresent = true;
            }

            if (friendPresent)
            {
                bool wasClear = !this.stealthBlockFriendHoldLatched;
                this.stealthBlockFriendHoldLatched = true;
                this.stealthBlockPhase = StealthBlockPhase.Blocked;
                this.stealthBlockStatus = blockActive ? "Friend in town — holding" : "Friend in town";
                this.stealthBlockNextReconcileAt = now + StealthBlockArmingReconcileSeconds;
                if (wasClear)
                {
                    // Edge-triggered: the reconcile keeps reporting the friend every pass, and
                    // re-teleporting on each one would fight the player for control.
                    this.OnStealthBlockFriendDetected();
                }
                return;
            }

            this.stealthBlockFriendHoldLatched = false;

            if (pending == 0)
            {
                if (this.stealthBlockPhase != StealthBlockPhase.Armed && blockActive)
                {
                    ModLogger.Msg("[StealthBlock] Armed — " + confirmed + " player(s) blocked, no friends in town.");
                }
                this.stealthBlockPhase = StealthBlockPhase.Armed;
                this.stealthBlockArmingStartedAt = -1f;
                this.stealthBlockStatus = !blockActive
                    ? "Watching for friends"
                    : (confirmed == 0 ? "Armed (town empty)" : "Armed (" + confirmed + " blocked)");
                return;
            }

            if (this.stealthBlockArmingStartedAt < 0f)
            {
                this.stealthBlockArmingStartedAt = now; // fell out of Armed — restart the clock
            }

            this.stealthBlockPhase = StealthBlockPhase.Arming;
            this.stealthBlockStatus = "Blocking " + confirmed + "/" + (confirmed + pending) + "...";
            this.stealthBlockNextReconcileAt = now + StealthBlockArmingReconcileSeconds;

            if (now - this.stealthBlockArmingStartedAt >= StealthBlockArmTimeoutSeconds)
            {
                this.stealthBlockPhase = StealthBlockPhase.Blocked;
                this.stealthBlockStatus = "Could not block " + pending + " player(s) — block list full?";
                ModLogger.Msg("[StealthBlock] " + this.stealthBlockStatus
                    + " (confirmed=" + confirmed + ", issued=" + stealthBlockIssuedCount + ")");
            }
        }

        // Friend arrived while the farm runs: surface first, THEN stop. Order matters — the noclip
        // restore rides the next frame's StealthForagingActive edge, so by the time gravity comes
        // back the player is already at the real node position instead of inside terrain.
        private void OnStealthBlockFriendDetected()
        {
            if (!this.stealthBlockNotifyFriends || !this.autoFarmActive)
            {
                return;
            }

            this.SurfaceFromStealthForaging("friend joined");
            this.autoFarmActive = false;
            this.farmState = HeartopiaComplete.AutoFarmState.Idle;
            this.autoFarmAutoStopAt = -1f;
            this.SetGameSpeed(1f);
            this.autoFarmStatus = "Stopped: friend entered town";
            ModLogger.Msg("[StealthBlock] Friend entered the town — surfaced and stopped the farm.");
            this.AddMenuNotification("Friend joined — farm stopped", new Color(1f, 0.75f, 0.45f));
        }

        // Releases whose grace expired (or everything, when the feature is switched off).
        private void DrainStealthBlockReleases(float now, bool releaseAll)
        {
            if (releaseAll)
            {
                if (now < this.stealthBlockNextSendAt || this.stealthBlockOwnedShortIds.Count == 0)
                {
                    return;
                }
                if (!this.TryResolveStealthBlockBindings())
                {
                    return;
                }

                this.stealthBlockNextSendAt = now + StealthBlockSendThrottleSeconds;
                long shortId = this.stealthBlockOwnedShortIds[this.stealthBlockOwnedShortIds.Count - 1];
                if (this.TryStealthBlockSendUnblock(shortId))
                {
                    stealthBlockReleasedCount++;
                }

                // Dropped either way: a send that never lands would otherwise wedge the queue
                // forever. PruneStealthBlockRegistry re-adopts anything still blocked next session.
                this.stealthBlockOwnedShortIds.RemoveAt(this.stealthBlockOwnedShortIds.Count - 1);
                try { this.SaveKeybinds(false); } catch { }
                this.stealthBlockStatus = "Releasing blocks (" + this.stealthBlockOwnedShortIds.Count + " left)...";
                return;
            }

            if (this.stealthBlockUnblockDueAt.Count == 0 || now < this.stealthBlockNextSendAt)
            {
                return;
            }

            uint dueNetId = 0u;
            foreach (KeyValuePair<uint, float> pair in this.stealthBlockUnblockDueAt)
            {
                if (now >= pair.Value)
                {
                    dueNetId = pair.Key;
                    break;
                }
            }

            if (dueNetId == 0u)
            {
                return;
            }

            this.stealthBlockUnblockDueAt.Remove(dueNetId);
            if (!this.stealthBlockShortIdByNetId.TryGetValue(dueNetId, out long shortIdToFree))
            {
                return;
            }

            this.stealthBlockShortIdByNetId.Remove(dueNetId);
            if (!this.stealthBlockOwnedShortIds.Contains(shortIdToFree))
            {
                return; // not ours (pre-existing manual block) — leave it alone
            }

            this.stealthBlockNextSendAt = now + StealthBlockSendThrottleSeconds;
            if (this.TryStealthBlockSendUnblock(shortIdToFree))
            {
                stealthBlockReleasedCount++;
            }

            this.stealthBlockOwnedShortIds.Remove(shortIdToFree);
            try { this.SaveKeybinds(false); } catch { }
        }

        private void DisarmStealthBlock(string reason)
        {
            this.stealthBlockPhase = StealthBlockPhase.Off;
            this.stealthBlockStatus = "Off";
            this.stealthBlockRoster.Clear();
            this.stealthBlockUnblockDueAt.Clear();
            this.stealthBlockShortIdByNetId.Clear();
            this.stealthBlockLastPlayerCount = -1;
            this.stealthBlockLastFriendCount = -1;
            this.stealthBlockArmingStartedAt = -1f;
            this.stealthBlockFriendHoldLatched = false;
            ModLogger.Msg("[StealthBlock] Disarmed (" + reason + "); releasing "
                + this.stealthBlockOwnedShortIds.Count + " block(s).");
        }

        // Registry hygiene at world entry: drop entries the server no longer holds (the user
        // unblocked them by hand, or the block never landed) so the release pass cannot fire
        // unblocks for people we are not actually blocking.
        private void PruneStealthBlockRegistry()
        {
            if (this.stealthBlockOwnedShortIds.Count == 0 || !this.TryResolveStealthBlockBindings())
            {
                return;
            }

            int removed = 0;
            for (int i = this.stealthBlockOwnedShortIds.Count - 1; i >= 0; i--)
            {
                long shortId = this.stealthBlockOwnedShortIds[i];
                if (this.TryStealthBlockIsBlocked(shortId, out bool blocked) && !blocked)
                {
                    this.stealthBlockOwnedShortIds.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                try { this.SaveKeybinds(false); } catch { }
                ModLogger.Msg("[StealthBlock] Registry pruned: " + removed + " stale entry(ies), "
                    + this.stealthBlockOwnedShortIds.Count + " still ours.");
            }
        }
    }
}
