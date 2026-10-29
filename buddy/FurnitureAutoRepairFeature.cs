using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Automatic furniture repair. Sends HomelandProtocolManager.Repair(netId) via AuraMono — the
    // same static the in-game Repair button uses; the manager builds and dispatches the
    // RepairBuildItemNetworkCommand itself. No repair-kit item, no proximity, no interaction.
    // The button is IsBroken plus house-permission type Self (Interaction 21, not in the settings
    // UI), so it is hidden for every other player. OwnerId 0 still shows the button: a zero owner
    // is treated as Everyone. This feature skips it and sends only when OwnerId equals the local
    // player net id. While enabled it scans every FurnitureBaseComponent about every 2.5 s. A send
    // happens once per piece, and stops retrying when the server ack lands (IsBroken clears and
    // OwnerId is still the local player) or refuses (RepairResultEvent with a non-zero error code —
    // the netId moves to a refused set that blocks re-sends until a world change or the toggle flips
    // off). If no ack arrives at all within the retry window, the entry is dropped and the repair
    // re-sent.
    //
    // The server ACK is what decides success: RepairResultEvent (dispatched by HomelandSyncSystem
    // from RepairBuildItemNetworkEvent) carries buildItemNetId + errorCode. A non-zero error
    // (RepairBuildItemFailed = 91) means the server refused — that netId moves into the refused
    // set so it is never re-sent while present, and the "Auto fixed furniture" toast fires only
    // when IsBroken clears on a piece that is still ours, never on a clean send. The scan is the
    // only heal observer:
    // SetRepaired is the client write behind that clear and must never be called or detoured.
    public partial class HeartopiaComplete
    {
        internal static bool MasterLogFurnitureAutoRepair = false;

        private const float FurnitureRepairScanIntervalSeconds = 2.5f;
        private const float FurnitureRepairScanBackoffSeconds = 5f;
        // A pending entry with no ack (neither heal nor refusal) after this window is dropped and
        // the repair re-sent — covers the case where the RepairResultEvent hook was refused and
        // the server never answers. Refusals are dropped immediately by the event handler, so
        // they never reach this timeout.
        private const float FurnitureRepairResendTimeoutSeconds = 12f;
        // One scan must not put every broken piece in the town on the wire in a single frame.
        // Heals and refusals are still applied past this cap; the leftover broken pieces go out
        // on the next pass.
        private const int FurnitureRepairMaxSendsPerScan = 8;

        private static readonly string[] FurnitureRepairProtocolTypeNames =
        {
            "XDTDataAndProtocol.ProtocolService.Homeland.HomelandProtocolManager",
        };

        // Own copies of the image filters (the pumpkin feature's arrays are its own; narrowing them
        // for pumpkin carving would silently break this feature's fallback resolution).
        private static readonly string[] FurnitureRepairProtocolImages =
        {
            "XDTDataAndProtocol", "XDTDataAndProtocol.dll",
        };

        private static readonly string[] FurnitureRepairViewImages =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
        };

        // RepairResultEvent { uint buildItemNetId; uint murdererNetId; ErrorCode errorCode } —
        // 12 bytes, global dispatch (HomelandSyncSystem). ErrorCode.Success == 0.
        private const string FurnitureRepairResultEventName = "XDTDataAndProtocol.Events.RepairResultEvent";
        private const int FurnitureRepairResultEventBytes = 12;

        private bool autoFurnitureRepairEnabled;
        private KeyCode autoFurnitureRepairHotkey = KeyCode.None;

        private bool furnitureRepairRegistrationsDone;
        private bool furnitureRepairResolveReady;
        private IntPtr furnitureRepairProtocolClass;
        private IntPtr furnitureRepairComponentClass;
        private IntPtr furnitureRepairMethod;
        // FurnitureBaseComponent.get_OwnerId → LevelEntityComponentData.ownerId. Cached with the
        // Repair method; the scan does not run until this resolves, so a missing getter cannot
        // fall through into repairing every loaded piece.
        private IntPtr furnitureRepairOwnerGetter;
        private float furnitureRepairNextScanAt;
        private string furnitureRepairStatus = "idle";
        private string furnitureRepairLastLoggedStatus = string.Empty;
        // Repairs actually confirmed by the server (ack cleared IsBroken). The scan-side "sent N"
        // status reports commands that went out but does not touch this counter.
        private int furnitureRepairSentCount;
        // netId -> send time (unscaledTime). Holds only items awaiting a server ack: entries are
        // removed when the ack clears IsBroken (heal) or when no ack arrives at all within the
        // retry window (drop + re-send). A REFUSED repair is moved to furnitureRepairRefused
        // instead — dropping it here would let the scan send it again on the very next pass.
        // Cleared on world epoch change (netIds are room-scoped) and when the toggle flips off.
        private readonly Dictionary<uint, float> furnitureRepairPending = new Dictionary<uint, float>();
        // netIds the server refused (RepairResultEvent with a non-zero errorCode) for a send this
        // feature itself made. The scan must never send for these while the entry is present;
        // IsBroken stays true after a refusal, so without this set the "broken and not pending"
        // predicate would re-send every 2.5 s for as long as the toggle is on. Cleared with the
        // pending set (world epoch change, toggle off).
        private readonly HashSet<uint> furnitureRepairRefused = new HashSet<uint>();
        private int furnitureRepairWorldEpoch;
        private FeatureBreakerState furnitureRepairBreaker;

        // Single entry point for the enable flag (Self → Main checkbox AND hotkey): persist, clear
        // in-flight state when switching off, and notify. Clearing on OFF matters — a leftover
        // pending netId would survive to the next town and either suppress repair of whatever
        // reused that id or be toasted as fixed by the next scan.
        private void SetFurnitureRepairEnabled(bool value)
        {
            if (value == this.autoFurnitureRepairEnabled)
            {
                return;
            }

            this.autoFurnitureRepairEnabled = value;
            if (!value)
            {
                this.ClearFurnitureRepairState();
            }

            try { this.SaveKeybinds(false); } catch { }
            this.AddMenuNotification(
                this.L("Auto Furniture Repair") + ": " + (this.autoFurnitureRepairEnabled ? this.L("On") : this.L("Off")),
                this.autoFurnitureRepairEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
        }

        // Forget per-world feature state. Called on world-epoch change and when the toggle flips off.
        private void ClearFurnitureRepairState()
        {
            this.furnitureRepairPending.Clear();
            this.furnitureRepairRefused.Clear();
        }

        private void EnsureFurnitureRepairRegistrations()
        {
            if (this.furnitureRepairRegistrationsDone)
            {
                return;
            }

            this.furnitureRepairRegistrationsDone = true;
            // Event-hook registration is metadata-only and safe pre-world (the install pass runs
            // on the world-ready gate); a refused hook degrades to scan-only, which still works.
            bool resultHook = this.RegisterGameEventHook(
                FurnitureRepairResultEventName, FurnitureRepairResultEventBytes,
                this.OnFurnitureRepairResultEvent);
            if (!resultHook)
            {
                ModLogger.Warning("[FurnitureAutoRepair] RepairResultEvent hook refused — a server refusal cannot be detected, so a refused repair is re-sent after the retry timeout instead of being dropped.");
            }

            this.RegisterWorldReadyCallback("furniture-auto-repair", this.TryResolveFurnitureAutoRepair);
        }

        private bool TryResolveFurnitureAutoRepair()
        {
            if (this.furnitureRepairResolveReady)
            {
                return true;
            }

            this.ResolveAuraFarmRuntimeMethods();
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (this.furnitureRepairComponentClass == IntPtr.Zero)
            {
                this.furnitureRepairComponentClass = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Gameplay.Component.Homeland",
                    "FurnitureBaseComponent",
                    FurnitureRepairViewImages);
            }

            if (this.furnitureRepairProtocolClass == IntPtr.Zero)
            {
                for (int i = 0; i < FurnitureRepairProtocolTypeNames.Length; i++)
                {
                    this.furnitureRepairProtocolClass = this.FindAuraMonoClassByFullName(FurnitureRepairProtocolTypeNames[i]);
                    if (this.furnitureRepairProtocolClass != IntPtr.Zero)
                    {
                        break;
                    }
                }
            }

            if (this.furnitureRepairProtocolClass == IntPtr.Zero)
            {
                this.furnitureRepairProtocolClass = this.FindAuraMonoClassInImages(
                    "XDTDataAndProtocol.ProtocolService.Homeland",
                    "HomelandProtocolManager",
                    FurnitureRepairProtocolImages);
            }

            if (this.furnitureRepairComponentClass == IntPtr.Zero || this.furnitureRepairProtocolClass == IntPtr.Zero)
            {
                return false;
            }

            // Repair(uint itemNetId): one static, arity 1. SetRepaired is the server-ack state
            // writer and must never be called from here.
            if (this.furnitureRepairMethod == IntPtr.Zero)
            {
                this.furnitureRepairMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.furnitureRepairProtocolClass, "Repair", 1);
            }

            if (this.furnitureRepairOwnerGetter == IntPtr.Zero)
            {
                this.furnitureRepairOwnerGetter = this.FindAuraMonoMethodOnHierarchy(
                    this.furnitureRepairComponentClass, "get_OwnerId", 0);
            }

            this.furnitureRepairResolveReady = this.furnitureRepairMethod != IntPtr.Zero
                && this.furnitureRepairOwnerGetter != IntPtr.Zero;
            if (this.furnitureRepairResolveReady && MasterLogFurnitureAutoRepair)
            {
                ModLogger.Msg("[FurnitureAutoRepair] resolve ready protocol=0x" + this.furnitureRepairProtocolClass.ToInt64().ToString("X")
                    + " component=0x" + this.furnitureRepairComponentClass.ToInt64().ToString("X")
                    + " method=0x" + this.furnitureRepairMethod.ToInt64().ToString("X")
                    + " owner=0x" + this.furnitureRepairOwnerGetter.ToInt64().ToString("X"));
            }

            return this.furnitureRepairResolveReady;
        }

        private void ProcessFurnitureAutoRepairOnUpdate()
        {
            this.EnsureFurnitureRepairRegistrations();
            if (!this.autoFurnitureRepairEnabled)
            {
                if (this.furnitureRepairStatus != "idle")
                {
                    this.FurnitureRepairSetStatus("idle");
                }

                return;
            }

            // World change: netIds are room-scoped, so per-world state from the previous world is
            // meaningless here — a pending entry would either suppress repair of whatever reused
            // that id or be toasted as fixed on the next scan; a refused entry would block repair
            // of a reused id forever. Forget it (ChatForceTranslateFeature idiom).
            int epoch = AuraMonoWorldEpoch;
            if (epoch != this.furnitureRepairWorldEpoch)
            {
                this.furnitureRepairWorldEpoch = epoch;
                this.ClearFurnitureRepairState();
            }

            float now = Time.unscaledTime;
            if (!this.furnitureRepairBreaker.ShouldRun(now))
            {
                return;
            }

            if (!this.furnitureRepairResolveReady || now < this.furnitureRepairNextScanAt)
            {
                return;
            }

            this.furnitureRepairNextScanAt = now + FurnitureRepairScanIntervalSeconds;
            try
            {
                this.FurnitureRepairTick();
                this.furnitureRepairBreaker.Success();
            }
            catch (Exception ex)
            {
                this.furnitureRepairBreaker.Failure("FurnitureAutoRepair", ex, now);
                this.furnitureRepairNextScanAt = now + FurnitureRepairScanBackoffSeconds;
            }
        }

        private void FurnitureRepairTick()
        {
            float now = Time.unscaledTime;

            // Pinned enumeration: the sgen GC moves the still-held component objects as each getter
            // invoke boxes a value, so every element is pinned at collection time and freed in
            // finally. The enumeration itself runs INSIDE the try (TryPumpkinRoughViewPresent idiom):
            // an exception inside it must still release whatever pins were collected before the
            // throw — otherwise they leak until session end. Never the entity-graph walk
            // (see TryAuraMonoGetComponentObjects).
            List<IntPtr> components;
            List<uint> pins = new List<uint>();
            try
            {
                // The bool return is false both when the query could not run and when it ran and
                // found nothing. Only the first is "scan unavailable"; an empty world is the same
                // as a scan that saw no broken furniture.
                if (!this.TryAuraMonoGetComponentObjects(
                        this.furnitureRepairComponentClass, out components, out bool infrastructureOk, pins))
                {
                    this.FurnitureRepairSetStatus(infrastructureOk ? "no broken furniture" : "scan unavailable");
                    return;
                }

                // Same id the Repair button compares against (PlayerDataCenter.GetSelfNetPlayerId).
                // Without it every broken piece would be skipped rather than sent.
                if (!this.TryResolveSelfPlayerNetId(out uint selfNetId) || selfNetId == 0U)
                {
                    this.FurnitureRepairSetStatus("self player netId unavailable");
                    return;
                }

                int sent = 0;
                int waiting = 0;
                int refused = 0;
                string lastFailure = null;

                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr obj = components[i];
                    uint netId;
                    if (!this.TryHomelandFarmTryReadAuraMonoComponentNetId(obj, out netId) || netId == 0U)
                    {
                        continue;
                    }

                    bool broken;
                    if (!this.TryInvokeAuraMonoBoolGetter(obj, this.furnitureRepairComponentClass, out broken, "get_IsBroken"))
                    {
                        // Cannot tell — skip this piece for this scan. A failed read must never be
                        // treated as a heal (the out value is not trustworthy on failure).
                        continue;
                    }

                    if (!broken)
                    {
                        // Not broken. A pending entry means we sent a repair for this netId. Toast
                        // only when OwnerId is still the local player: a netId reused inside the
                        // same scene onto someone else's intact piece must not be reported as
                        // fixed. A failed owner read leaves the entry for the next scan.
                        if (!this.furnitureRepairPending.ContainsKey(netId))
                        {
                            continue;
                        }

                        uint healedOwnerId;
                        if (!this.TryReadFurnitureRepairOwnerId(obj, out healedOwnerId))
                        {
                            continue;
                        }

                        this.furnitureRepairPending.Remove(netId);
                        if (healedOwnerId == selfNetId)
                        {
                            this.OnFurnitureRepairedHealed(netId);
                        }

                        continue;
                    }

                    // OwnerId is the player who placed the piece. HomeNetId is the house instance
                    // and murdererNetId is who broke it — neither is the owner. A failed read
                    // (including OwnerId 0, which the uint unbox reports as failure) skips the
                    // piece and leaves an in-flight entry alone. Any other id drops a leftover
                    // pending entry so it cannot toast or be re-sent.
                    uint ownerId;
                    if (!this.TryReadFurnitureRepairOwnerId(obj, out ownerId))
                    {
                        continue;
                    }

                    if (ownerId != selfNetId)
                    {
                        this.furnitureRepairPending.Remove(netId);
                        continue;
                    }

                    if (this.furnitureRepairRefused.Contains(netId))
                    {
                        // The server refused this repair — IsBroken stays true, so without this
                        // check the "broken and not pending" predicate would re-send every scan
                        // for as long as the toggle is on. Never send again while the entry is
                        // present (cleared only by a world change or the toggle flipping off).
                        // Counted separately below so a fully-refused town does not report
                        // "no broken furniture" when pieces are still broken.
                        refused++;
                        continue;
                    }

                    float lastSentAt;
                    if (this.furnitureRepairPending.TryGetValue(netId, out lastSentAt))
                    {
                        // Already sent for this piece and the ack has not arrived yet. If no ack
                        // shows up within the retry window (e.g. the RepairResultEvent hook was
                        // refused and the server never answers), drop the entry and re-send —
                        // a stuck entry would otherwise block this netId forever.
                        if (now - lastSentAt >= FurnitureRepairResendTimeoutSeconds)
                        {
                            this.furnitureRepairPending.Remove(netId);
                        }
                        else
                        {
                            waiting++;
                            continue;
                        }
                    }

                    if (sent >= FurnitureRepairMaxSendsPerScan)
                    {
                        continue;
                    }

                    string status;
                    if (!this.TrySendFurnitureRepair(netId, out status))
                    {
                        lastFailure = status;
                        continue;
                    }

                    // The pending entry is removed by the ack paths: a heal clears IsBroken, a
                    // refusal moves it to furnitureRepairRefused, and a missing ack hits the
                    // retry timeout above.
                    this.furnitureRepairPending[netId] = now;
                    sent++;
                }

                // Status order: a send failure (only when nothing went out) > sends > in-flight
                // waits > refused pieces. Refused must come before the "no broken" fallback —
                // otherwise a town where every broken piece was refused would report none.
                if (lastFailure != null && sent == 0)
                {
                    this.FurnitureRepairSetStatus(lastFailure);
                }
                else if (sent > 0)
                {
                    // The toast reports the FIXED count and fires on the ack — see
                    // OnFurnitureRepairedHealed. Here we only report that commands went out.
                    this.FurnitureRepairSetStatus("sent " + sent);
                }
                else if (waiting > 0)
                {
                    this.FurnitureRepairSetStatus("waiting x" + waiting);
                }
                else if (refused > 0)
                {
                    this.FurnitureRepairSetStatus("refused x" + refused);
                }
                else
                {
                    this.FurnitureRepairSetStatus("no broken furniture");
                }

                if (MasterLogFurnitureAutoRepair && sent > 0)
                {
                    ModLogger.Msg("[FurnitureAutoRepair] scan sent=" + sent + " total=" + this.furnitureRepairSentCount);
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        // get_OwnerId returns LevelEntityComponentData.ownerId. TryUnboxMonoUInt32 rejects 0, so an
        // unowned piece fails this read and is skipped — it is not the local player.
        private bool TryReadFurnitureRepairOwnerId(IntPtr obj, out uint ownerId)
        {
            ownerId = 0U;
            if (obj == IntPtr.Zero || this.furnitureRepairOwnerGetter == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.furnitureRepairOwnerGetter, obj, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero && boxed != IntPtr.Zero && this.TryUnboxMonoUInt32(boxed, out ownerId);
        }

        // RepairResultEvent (HomelandSyncSystem, from the server's RepairBuildItemNetworkEvent):
        // buildItemNetId @ 0, murdererNetId @ 4, errorCode @ 8. ErrorCode.Success == 0. A non-zero
        // code means the server REFUSED this repair (RepairBuildItemFailed = 91) — move the netId
        // from the pending set into the refused set so the scan never sends it again while the
        // entry is present. The success case does nothing: the ack that matters is IsBroken
        // clearing (OnFurnitureRepairedHealed), which is what toasts.
        //
        // Only a refusal of OUR send is recorded — Remove returning true proves we sent for this
        // netId and the entry survived until now. The hook stays installed while the toggle is
        // off, and ClearFurnitureRepairState forgets in-flight entries on toggle off / scene
        // change: a late packet after that clear must not be recorded (it would block auto-repair
        // of whatever reused the id), and neither may a refusal of another player's or a manual
        // repair. Ignoring those means the scan re-sends its own send for the netId normally —
        // which is correct, since it is still broken.
        private void OnFurnitureRepairResultEvent(GameEventSnapshot e)
        {
            uint netId = e.ReadUInt32(0);
            int errorCode = e.ReadInt32(8);
            if (netId == 0U || errorCode == 0)
            {
                return;
            }

            if (!this.furnitureRepairPending.Remove(netId))
            {
                // Not our in-flight send (never sent, or already cleared by a state reset) — ignore.
                return;
            }

            this.furnitureRepairRefused.Add(netId);
            ModLogger.Msg("[FurnitureAutoRepair] repair refused netId=" + netId + " errorCode=" + errorCode);
        }

        // The piece is healed: the scan saw IsBroken clear on an in-flight netId. This is the only
        // place a repair can be called finished, so the toast fires here — never on send. The
        // pending entry is already removed by the caller.
        private void OnFurnitureRepairedHealed(uint netId)
        {
            this.furnitureRepairSentCount++;
            ModLogger.Msg("[FurnitureAutoRepair] repaired netId=" + netId);
            this.AddMenuNotification(
                this.LF("Auto fixed furniture: {0}", 1),
                new Color(0.45f, 1f, 0.55f));
        }

        // One static AuraMono invoke of HomelandProtocolManager.Repair(uint). The manager builds
        // the RepairBuildItemNetworkCommand itself (GetNetworkEntity inside), so this is exactly
        // what the in-game Repair button puts on the wire. uint goes through as a pointer to the
        // local (the same convention TryEatFoodDirectNoAnimation uses for EatFood(uint)).
        private unsafe bool TrySendFurnitureRepair(uint netId, out string status)
        {
            status = "AuraMono unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr method = this.furnitureRepairMethod;
            if (method == IntPtr.Zero)
            {
                status = "HomelandProtocolManager.Repair unresolved";
                return false;
            }

            uint localNetId = netId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&localNetId);

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "Repair exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "ok";
            return true;
        }

        private void FurnitureRepairSetStatus(string message)
        {
            this.furnitureRepairStatus = message ?? string.Empty;
            if (this.furnitureRepairStatus.Length == 0 || this.furnitureRepairStatus == this.furnitureRepairLastLoggedStatus)
            {
                return;
            }

            this.furnitureRepairLastLoggedStatus = this.furnitureRepairStatus;
            ModLogger.Msg("[FurnitureAutoRepair] " + this.furnitureRepairStatus);
        }
    }
}
