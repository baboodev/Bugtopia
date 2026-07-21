using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Sanrio Gacha Machine Finder — locate every SANRIO CHARACTERS event gacha machine around
    // the player and highlight them on the game map.
    //
    // Mechanic (cn tables, 2026-07-17): the "SANRIO CHARACTERS Gacha Machine" event
    // (ThematicActivity 1112, period 50233 = 2026-07-17..2026-08-23) has FOUR machine entities,
    // all MechanismItem ugctype 22 (same object class as the stampede carpets):
    //   302505 — the distributable machine (pretask 7000571 reward) — DYNAMIC FURNITURE that
    //            players place in their own homes; prefab p_mechanism_sanrio_gashaponmachine;
    //   302506/302507/302508 — scene-only variants ("不投放"), dropped into Star Town as
    //            server dynamic objects: MapDynamicResource 11305/11306/11307 at fixed level
    //            markers mapPosId 10520/10521/10522.
    // Touching a machine (UGC skill 100100028-31 -> InteractionDrop 103-106) gives a capsule
    // drop: 1/day per machine, 5/day total — so finding many placed machines matters. The game
    // itself never shows machines on the map (no TableMapElement row; the activity panel offers
    // ONE tracking pointer, nav point 1000002, Star Town only).
    // Full research: .research-record/SANRIO_GACHA_MACHINES.md.
    //
    // Three position sources, merged:
    //   SCAN   — UGCWorld._uActors walk (the CarpetStamp scan pattern): every UGC mechanism
    //            actor on the map -> StaticId filter 302505-302508 -> _entity position. This is
    //            what finds PLAYER-PLACED machines (302505) wherever we roam; found machines are
    //            remembered for the rest of the world session (streaming may drop the actor).
    //   PROBE  — DynamicObjectManager.GetDynamicObject(11305..11307) for the three scene
    //            machines (the Little Whale probe path; AOI/streaming-bound).
    //   CONFIG — MainGameLvlConf.MapEntityPointsAsset walk (the CorruptionCleanse config-walk
    //            pattern) -> marker ids 10520..10522 -> .pos. Whole-level data: resolves the
    //            scene machines from anywhere in Star Town; other levels lack the ids.
    // While the toggle is on, every known machine carries a Furniture map track pin with the
    // distributed machine item's icon/name (302505 — the scene variants are never distributed
    // and may lack a NormalItem sprite). Teleport buttons mirror the whale feature.
    public partial class HeartopiaComplete
    {
        private const int SanrioSceneMachineCount = 3;
        // MapDynamicResource ids (= DynamicObjectManager dynamicConfigIds), scene machines 1-3.
        private static readonly int[] SanrioMachineConfigIds = { 11305, 11306, 11307 };
        // MapEntityPointsAsset marker ids for the same machines (Star Town level config).
        private static readonly int[] SanrioMachineMapPosIds = { 10520, 10521, 10522 };
        // Scene machine entity ids, index-aligned with the arrays above.
        private static readonly int[] SanrioSceneMachineStaticIds = { 302506, 302507, 302508 };
        // The distributable machine item — player-placed dynamic furniture; also the pin icon
        // for every machine (guaranteed NormalItem sprite + localized name).
        private const int SanrioMachinePlacedStaticId = 302505;
        private const float SanrioGachaPollInterval = 3f;
        private const float SanrioConfigWalkRetryInterval = 15f;
        private const int SanrioPlacedRowsShown = 8;
        // Reserved pin tokens: fixed scene pins = base+0..2; placed pins = tag | netId
        // (byte 5 differs, so the two families can never collide; both stay clear of the radar
        // marker tag 0x50... and the whale pin 0x4C57...).
        private const ulong SanrioGachaPinTokenBase = 0x53414E52494F0001UL;
        private const ulong SanrioPlacedPinTokenTag = 0x53414E5250000000UL;

        // Toggle (persisted; default OFF). Drawn in the Extra features tab.
        private bool sanrioGachaFinderEnabled;

        private struct SanrioMachineSlot
        {
            public bool Present;
            public bool Live;        // position from a live entity (else: level-config marker)
            public Vector3 Pos;
        }

        // A player-placed machine (302505) seen in UGCWorld._uActors this world session.
        // Entries persist per world epoch: the actor streams out when we walk away, but the
        // furniture itself does not move — keep the position (and its pin) once discovered.
        private sealed class SanrioPlacedMachine
        {
            public uint NetId;
            public Vector3 Pos;
            public bool PinActive;
            public Vector3 PinPos;
            public float LastSeenAt;
            public bool DoneToday;   // a tracked capsule-drop success happened at this machine today
        }

        // --- Daily drop tracking -------------------------------------------------------------
        // The per-machine daily counters (InteractionDropRecordComponent: AllTotalCount 5/day,
        // AlreadyInteraction*Ids 1/day per machine) are [Persistent] SERVER data with no client
        // reader — the client only learns results via Event<InteractionDropNetworkEvent>
        // (dropId+ErrorCode). That wrapper is a constructed generic (not hookable by name), but
        // on every SUCCESS InteractDropSystem re-dispatches the plain GLOBAL PlayerTakeCandyEvent
        // — we hook that and attribute the success to the nearest known machine within 6 m
        // (UGC interact range is ~3 m; other interaction drops fire far from any machine).
        // Tracking is therefore "what the mod observed": interactions done before enabling the
        // finder (or placed-machine marks after a relog — netIds are per-session) stay unknown.
        // Scene marks + the daily total persist in the config, keyed to the 06:00 game-day.
        private const string SanrioTakeCandyEventName = "ScriptsRefactory.DataAndProtocol.Events.PlayerTakeCandyEvent";
        private const float SanrioDropMatchRadius = 6f;
        private const int SanrioDropDailyCap = 5;

        private bool sanrioDropHookRegistered;
        private long sanrioDropDayStamp;      // persisted: game-day index of the counters below
        private int sanrioDropTotalToday;     // persisted: successes observed today (cap 5)
        private int sanrioDropSceneDoneMask;  // persisted: bits 0-2 = scene machine collected
        private float sanrioNextDayCheckAt;
        private IntPtr sanrioGameTimeMethod = IntPtr.Zero;

        private readonly SanrioMachineSlot[] sanrioMachines = new SanrioMachineSlot[SanrioSceneMachineCount];
        private readonly bool[] sanrioPinActive = new bool[SanrioSceneMachineCount];
        private readonly Vector3[] sanrioPinPos = new Vector3[SanrioSceneMachineCount];
        private readonly Dictionary<uint, SanrioPlacedMachine> sanrioPlacedMachines = new Dictionary<uint, SanrioPlacedMachine>(16);
        private readonly List<SanrioPlacedMachine> sanrioPlacedSorted = new List<SanrioPlacedMachine>(16);
        private int sanrioWorldEpoch;
        private float sanrioNextPollAt;
        private int sanrioLocatedCount;      // scene machines located
        private bool sanrioLocatedNotified;
        private int sanrioPlacedNotifiedCount;
        private string sanrioLastStatus = string.Empty;
        private IntPtr sanrioUgcWorldClass = IntPtr.Zero;

        // Level-config marker positions (mapPosId -> pos), cached per world epoch. A walk that
        // READS fine but finds nothing (any non-StarTown level) is terminal for the epoch; only
        // resolve failures (config manager/scene config not up yet during load) retry on a timer.
        private readonly Dictionary<int, Vector3> sanrioConfigPos = new Dictionary<int, Vector3>(4);
        private int sanrioConfigWalkEpoch = -1;
        private bool sanrioConfigWalkDone;
        private float sanrioNextConfigWalkAt;

        // Called every frame from OnUpdate; self-throttled to one pass per 3 s.
        private void ProcessSanrioGachaFinderOnUpdate()
        {
            if (!this.sanrioGachaFinderEnabled)
            {
                if (this.sanrioLocatedCount > 0 || this.sanrioLocatedNotified || this.sanrioPlacedMachines.Count > 0)
                {
                    this.RemoveSanrioGachaPins();
                    this.sanrioLocatedCount = 0;
                    this.sanrioLocatedNotified = false;
                    this.sanrioPlacedNotifiedCount = 0;
                    this.sanrioPlacedMachines.Clear();
                    Array.Clear(this.sanrioMachines, 0, SanrioSceneMachineCount);
                }
                return;
            }

            // World change wipes the game's track list and invalidates every position — forget
            // pins/slots/registry, never dispatch into a torn-down world (whale pattern).
            if (this.sanrioWorldEpoch != HeartopiaComplete.AuraMonoWorldEpoch)
            {
                this.sanrioWorldEpoch = HeartopiaComplete.AuraMonoWorldEpoch;
                for (int i = 0; i < SanrioSceneMachineCount; i++)
                {
                    this.sanrioPinActive[i] = false;
                }
                Array.Clear(this.sanrioMachines, 0, SanrioSceneMachineCount);
                this.sanrioPlacedMachines.Clear();
                this.sanrioLocatedCount = 0;
                this.sanrioLocatedNotified = false;
                this.sanrioPlacedNotifiedCount = 0;
            }

            float now = Time.unscaledTime;
            if (now < this.sanrioNextPollAt)
            {
                return;
            }
            this.sanrioNextPollAt = now + SanrioGachaPollInterval;

            // World gate (same startup-crash class as the whale finder): never touch the module
            // registry / config manager from the main menu — no local player = no world.
            if (this.GetPlayer() == null)
            {
                return;
            }

            this.EnsureSanrioDropHook();
            this.EnsureSanrioDropDay();

            try
            {
                this.LocateSanrioMachines(out this.sanrioLastStatus);
            }
            catch (Exception ex)
            {
                this.sanrioLastStatus = "probe exception: " + ex.GetType().Name;
                return; // keep previous state; retry next poll
            }

            if (this.sanrioLocatedCount > 0 && !this.sanrioLocatedNotified)
            {
                this.sanrioLocatedNotified = true;
                this.AddMenuNotification(this.LF("Sanrio gacha machines located: {0}", this.sanrioLocatedCount), new Color(1f, 0.65f, 0.85f));
                ModLogger.Msg("[SanrioGacha] scene machines located: " + this.sanrioLocatedCount + "/" + SanrioSceneMachineCount
                    + " (" + this.sanrioLastStatus + ")");
            }
            if (this.sanrioPlacedMachines.Count > this.sanrioPlacedNotifiedCount)
            {
                this.sanrioPlacedNotifiedCount = this.sanrioPlacedMachines.Count;
                this.AddMenuNotification(this.LF("Placed Sanrio machines found: {0}", this.sanrioPlacedMachines.Count), new Color(1f, 0.65f, 0.85f));
                ModLogger.Msg("[SanrioGacha] placed machines known this session: " + this.sanrioPlacedMachines.Count);
            }

            this.SyncSanrioGachaPins();
        }

        // One locate pass: config markers (whole-level, cached) + scene-machine probe (AOI-bound)
        // + UGC actor scan (live scene positions + player-placed machines).
        private void LocateSanrioMachines(out string status)
        {
            this.EnsureSanrioConfigPositions();

            // Reset scene slots to the config baseline; live sources upgrade them below.
            for (int i = 0; i < SanrioSceneMachineCount; i++)
            {
                SanrioMachineSlot slot = default(SanrioMachineSlot);
                if (this.sanrioConfigPos.TryGetValue(SanrioMachineMapPosIds[i], out Vector3 cfgPos))
                {
                    slot.Present = true;
                    slot.Pos = cfgPos;
                }
                this.sanrioMachines[i] = slot;
            }

            // DynamicObjectManager probe (scene machines only).
            bool managerOk = this.TryResolveDynamicObjectManagerAura(out IntPtr managerObj, out uint managerPin, out string managerStatus);
            try
            {
                IntPtr getMethod = IntPtr.Zero;
                if (managerOk)
                {
                    IntPtr managerClass = auraMonoObjectGetClass(managerObj);
                    getMethod = managerClass != IntPtr.Zero
                        ? this.FindAuraMonoMethodOnHierarchy(managerClass, "GetDynamicObject", 1)
                        : IntPtr.Zero;
                }

                if (getMethod != IntPtr.Zero)
                {
                    for (int i = 0; i < SanrioSceneMachineCount; i++)
                    {
                        if (this.TryProbeLittleWhaleConfig(managerObj, getMethod, SanrioMachineConfigIds[i], out Vector3 livePos))
                        {
                            this.sanrioMachines[i].Present = true;
                            this.sanrioMachines[i].Live = true;
                            this.sanrioMachines[i].Pos = livePos;
                        }
                    }
                }
            }
            finally
            {
                if (managerPin != 0U)
                {
                    AuraMonoPinFree(managerPin);
                }
            }

            // UGC actor scan — the only source that sees PLAYER-PLACED machines (302505).
            int actorsSeen = this.ScanSanrioUgcActors();

            int located = 0;
            int liveCount = 0;
            for (int i = 0; i < SanrioSceneMachineCount; i++)
            {
                if (this.sanrioMachines[i].Present)
                {
                    located++;
                    if (this.sanrioMachines[i].Live)
                    {
                        liveCount++;
                    }
                }
            }
            this.sanrioLocatedCount = located;

            status = "scene " + located + "/" + SanrioSceneMachineCount + " (" + liveCount + " live)"
                + ", placed " + this.sanrioPlacedMachines.Count
                + ", ugcActors " + actorsSeen
                + (located == 0 && !this.sanrioConfigWalkDone ? ", config pending; manager: " + managerStatus : string.Empty);
        }

        // UGCWorld._uActors walk (pin-for-pin mirror of the CarpetStamp scan, without the
        // per-actor logging): StaticId 302505 -> placed-machine registry (persists for the
        // world session); 302506-08 -> live position for the matching scene slot.
        // Returns the number of actors enumerated (-1 = registry unavailable).
        private int ScanSanrioUgcActors()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return -1;
            }

            if (this.sanrioUgcWorldClass == IntPtr.Zero)
            {
                // UGCWorld lives in the XDTDataAndProtocol image despite the XDTGame.UGC namespace.
                IntPtr dataImage = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
                if (dataImage != IntPtr.Zero && auraMonoClassFromName != null)
                {
                    this.sanrioUgcWorldClass = auraMonoClassFromName(dataImage, "XDTGame.UGC", "UGCWorld");
                }
                if (this.sanrioUgcWorldClass == IntPtr.Zero)
                {
                    this.sanrioUgcWorldClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.UGC", "UGCWorld");
                }
            }
            if (this.sanrioUgcWorldClass == IntPtr.Zero)
            {
                return -1;
            }

            if (!this.TryGetAuraMonoStaticObjectField(this.sanrioUgcWorldClass, "_uActors", out IntPtr actorsDict)
                || actorsDict == IntPtr.Zero)
            {
                return -1;
            }

            List<IntPtr> entries = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            int seen = 0;
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(actorsDict, entries, pins))
                {
                    return -1;
                }

                float now = Time.unscaledTime;
                for (int i = 0; i < entries.Count; i++)
                {
                    IntPtr entry = entries[i];
                    if (entry == IntPtr.Zero)
                    {
                        continue;
                    }
                    seen++;

                    uint netId = 0U;
                    if (!this.TryGetMonoUInt32Member(entry, "Key", out netId) && !this.TryGetMonoUInt32Member(entry, "key", out netId))
                    {
                        continue;
                    }

                    IntPtr actorObj = IntPtr.Zero;
                    if ((!this.TryGetMonoObjectMember(entry, "Value", out actorObj) || actorObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(entry, "value", out actorObj) || actorObj == IntPtr.Zero))
                    {
                        continue;
                    }

                    // The pins list only covers the boxed KVP entries; the UActor / entity objects
                    // read out of them are separate heap objects — pin them across their member
                    // reads (each read allocates mono-side; SGen moves unpinned objects mid-loop).
                    int staticId = 0;
                    Vector3 pos = Vector3.zero;
                    bool hasPos = false;
                    uint actorPin = AuraMonoPinNew(actorObj);
                    try
                    {
                        if (!this.TryGetMonoInt32Member(actorObj, "StaticId", out staticId))
                        {
                            continue;
                        }
                        if (staticId != SanrioMachinePlacedStaticId
                            && staticId != SanrioSceneMachineStaticIds[0]
                            && staticId != SanrioSceneMachineStaticIds[1]
                            && staticId != SanrioSceneMachineStaticIds[2])
                        {
                            continue;
                        }

                        if (this.TryGetMonoObjectMember(actorObj, "_entity", out IntPtr entityObj) && entityObj != IntPtr.Zero)
                        {
                            uint entityPin = AuraMonoPinNew(entityObj);
                            try
                            {
                                hasPos = this.TryGetAuraMonoEntityPosition(entityObj, out pos);
                            }
                            finally
                            {
                                AuraMonoPinFree(entityPin);
                            }
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(actorPin);
                    }

                    if (!hasPos)
                    {
                        continue;
                    }

                    if (staticId == SanrioMachinePlacedStaticId)
                    {
                        if (!this.sanrioPlacedMachines.TryGetValue(netId, out SanrioPlacedMachine placed))
                        {
                            placed = new SanrioPlacedMachine { NetId = netId };
                            this.sanrioPlacedMachines[netId] = placed;
                            ModLogger.Msg("[SanrioGacha] placed machine found: netId=" + netId
                                + " pos=(" + pos.x.ToString("F1") + ", " + pos.y.ToString("F1") + ", " + pos.z.ToString("F1") + ")");
                        }
                        placed.Pos = pos;
                        placed.LastSeenAt = now;
                    }
                    else
                    {
                        for (int s = 0; s < SanrioSceneMachineCount; s++)
                        {
                            if (SanrioSceneMachineStaticIds[s] == staticId)
                            {
                                this.sanrioMachines[s].Present = true;
                                this.sanrioMachines[s].Live = true;
                                this.sanrioMachines[s].Pos = pos;
                                break;
                            }
                        }
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            return seen;
        }

        // ConfigManager -> _mainGameLvlConf -> MapEntityPointsAsset.entityPoints[].asset[] ->
        // ids 10520-10522 -> pos. Pin-for-pin mirror of the CorruptionCleanse config walk
        // (reference-type lists only; no value-type array enumeration). Runs once per world
        // epoch on success; resolve failures retry every 15 s.
        private void EnsureSanrioConfigPositions()
        {
            int epoch = HeartopiaComplete.AuraMonoWorldEpoch;
            if (this.sanrioConfigWalkEpoch != epoch)
            {
                this.sanrioConfigWalkEpoch = epoch;
                this.sanrioConfigWalkDone = false;
                this.sanrioConfigPos.Clear();
                this.sanrioNextConfigWalkAt = 0f;
            }
            if (this.sanrioConfigWalkDone)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.sanrioNextConfigWalkAt)
            {
                return;
            }
            this.sanrioNextConfigWalkAt = now + SanrioConfigWalkRetryInterval;

            uint managerPin = 0U;
            try
            {
                if (!this.TryResolveCorruptionConfigManager(out IntPtr configManagerObj, out managerPin, out string _))
                {
                    return; // config manager not registered yet — retry on the timer
                }

                if (!this.TryGetMonoObjectMember(configManagerObj, "_mainGameLvlConf", out IntPtr levelConfObj) || levelConfObj == IntPtr.Zero)
                {
                    return; // scene config not loaded yet — retry
                }

                uint levelPin = AuraMonoPinNew(levelConfObj);
                try
                {
                    if (!this.TryGetMonoObjectMember(levelConfObj, "MapEntityPointsAsset", out IntPtr pointsAssetObj) || pointsAssetObj == IntPtr.Zero)
                    {
                        this.sanrioConfigWalkDone = true; // level ships no marker asset — terminal
                        return;
                    }

                    uint assetPin = AuraMonoPinNew(pointsAssetObj);
                    try
                    {
                        if (!this.TryGetMonoObjectMember(pointsAssetObj, "entityPoints", out IntPtr pairListObj) || pairListObj == IntPtr.Zero)
                        {
                            this.sanrioConfigWalkDone = true;
                            return;
                        }

                        List<IntPtr> pairs = new List<IntPtr>();
                        List<uint> pairPins = new List<uint>();
                        try
                        {
                            if (!this.TryEnumerateAuraMonoCollectionItems(pairListObj, pairs, pairPins))
                            {
                                this.sanrioConfigWalkDone = true;
                                return;
                            }

                            for (int p = 0; p < pairs.Count && this.sanrioConfigPos.Count < SanrioSceneMachineCount; p++)
                            {
                                if (pairs[p] == IntPtr.Zero
                                    || !this.TryGetMonoObjectMember(pairs[p], "asset", out IntPtr pointListObj)
                                    || pointListObj == IntPtr.Zero)
                                {
                                    continue;
                                }

                                uint pointListPin = AuraMonoPinNew(pointListObj);
                                try
                                {
                                    List<IntPtr> points = new List<IntPtr>();
                                    List<uint> pointPins = new List<uint>();
                                    try
                                    {
                                        if (!this.TryEnumerateAuraMonoCollectionItems(pointListObj, points, pointPins))
                                        {
                                            continue;
                                        }

                                        for (int i = 0; i < points.Count && this.sanrioConfigPos.Count < SanrioSceneMachineCount; i++)
                                        {
                                            if (points[i] == IntPtr.Zero
                                                || !this.TryGetMonoIntMember(points[i], "id", out int pointId))
                                            {
                                                continue;
                                            }

                                            bool wanted = false;
                                            for (int w = 0; w < SanrioMachineMapPosIds.Length; w++)
                                            {
                                                if (SanrioMachineMapPosIds[w] == pointId)
                                                {
                                                    wanted = true;
                                                    break;
                                                }
                                            }
                                            if (!wanted)
                                            {
                                                continue;
                                            }

                                            if (this.TryGetMonoVector3Member(points[i], "pos", out Vector3 pos))
                                            {
                                                this.sanrioConfigPos[pointId] = pos;
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        FreeAuraMonoPins(pointPins);
                                    }
                                }
                                finally
                                {
                                    AuraMonoPinFree(pointListPin);
                                }
                            }

                            this.sanrioConfigWalkDone = true;
                            if (this.sanrioConfigPos.Count > 0)
                            {
                                ModLogger.Msg("[SanrioGacha] level-config markers resolved: " + this.sanrioConfigPos.Count + "/" + SanrioSceneMachineCount);
                            }
                        }
                        finally
                        {
                            FreeAuraMonoPins(pairPins);
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(assetPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(levelPin);
                }
            }
            catch
            {
                // resolve/read failure — retry on the timer
            }
            finally
            {
                if (managerPin != 0U)
                {
                    AuraMonoPinFree(managerPin);
                }
            }
        }

        // Auto-pin every known machine while the toggle is on: Furniture map tracks carrying the
        // distributed machine item's staticId (icon + localized name on mini/big map).
        // Re-dispatch on >1 m drift (a live entity refines a config marker position).
        private void SyncSanrioGachaPins()
        {
            bool anyKnown = this.sanrioPlacedMachines.Count > 0;
            for (int i = 0; i < SanrioSceneMachineCount && !anyKnown; i++)
            {
                anyKnown = this.sanrioMachines[i].Present;
            }
            if (anyKnown && (!this.EnsureMapTrackReady() || !this.AttachAuraMonoThread()))
            {
                return; // track system not up yet — try again next poll
            }

            for (int i = 0; i < SanrioSceneMachineCount; i++)
            {
                ulong token = SanrioGachaPinTokenBase + (ulong)i;
                if (this.sanrioMachines[i].Present)
                {
                    if (!this.sanrioPinActive[i] || (this.sanrioMachines[i].Pos - this.sanrioPinPos[i]).sqrMagnitude > 1f)
                    {
                        if (this.DispatchStartTrack(token, this.sanrioMachines[i].Pos, MapTrackTypeFurniture, SanrioMachinePlacedStaticId, 0u))
                        {
                            this.sanrioPinActive[i] = true;
                            this.sanrioPinPos[i] = this.sanrioMachines[i].Pos;
                        }
                    }
                }
                else if (this.sanrioPinActive[i])
                {
                    this.sanrioPinActive[i] = false;
                    try { this.DispatchStopTrack(token); } catch { }
                }
            }

            foreach (KeyValuePair<uint, SanrioPlacedMachine> kv in this.sanrioPlacedMachines)
            {
                SanrioPlacedMachine placed = kv.Value;
                if (!placed.PinActive || (placed.Pos - placed.PinPos).sqrMagnitude > 1f)
                {
                    if (this.DispatchStartTrack(SanrioPlacedPinTokenTag | placed.NetId, placed.Pos, MapTrackTypeFurniture, SanrioMachinePlacedStaticId, 0u))
                    {
                        placed.PinActive = true;
                        placed.PinPos = placed.Pos;
                    }
                }
            }
        }

        private void RemoveSanrioGachaPins()
        {
            bool sameWorld = this.sanrioWorldEpoch == HeartopiaComplete.AuraMonoWorldEpoch;
            for (int i = 0; i < SanrioSceneMachineCount; i++)
            {
                if (!this.sanrioPinActive[i])
                {
                    continue;
                }
                this.sanrioPinActive[i] = false;
                try
                {
                    // Only dispatch into the world the pins were placed in (whale pattern).
                    if (sameWorld && this.AttachAuraMonoThread())
                    {
                        this.DispatchStopTrack(SanrioGachaPinTokenBase + (ulong)i);
                    }
                }
                catch
                {
                }
            }

            foreach (KeyValuePair<uint, SanrioPlacedMachine> kv in this.sanrioPlacedMachines)
            {
                if (!kv.Value.PinActive)
                {
                    continue;
                }
                kv.Value.PinActive = false;
                try
                {
                    if (sameWorld && this.AttachAuraMonoThread())
                    {
                        this.DispatchStopTrack(SanrioPlacedPinTokenTag | kv.Key);
                    }
                }
                catch
                {
                }
            }
        }

        // One extra hook slot; registered lazily on the first enabled tick, then the handler
        // self-gates on the toggle (the shared-detour engine keeps hooks for the session).
        private void EnsureSanrioDropHook()
        {
            if (!this.sanrioDropHookRegistered)
            {
                this.sanrioDropHookRegistered = this.RegisterGameEventHook(SanrioTakeCandyEventName, 0, this.OnSanrioTakeCandyEvent);
            }
        }

        // Roll the persisted counters when the game day (06:00 boundary) changes. Throttled.
        private void EnsureSanrioDropDay()
        {
            float now = Time.unscaledTime;
            if (now < this.sanrioNextDayCheckAt)
            {
                return;
            }
            this.sanrioNextDayCheckAt = now + 30f;

            long day = this.GetSanrioGameDayIndex();
            if (day > 0 && this.sanrioDropDayStamp != day)
            {
                bool hadCounts = this.sanrioDropTotalToday > 0 || this.sanrioDropSceneDoneMask != 0;
                this.sanrioDropDayStamp = day;
                this.sanrioDropTotalToday = 0;
                this.sanrioDropSceneDoneMask = 0;
                foreach (KeyValuePair<uint, SanrioPlacedMachine> kv in this.sanrioPlacedMachines)
                {
                    kv.Value.DoneToday = false;
                }
                if (hadCounts)
                {
                    ModLogger.Msg("[SanrioGacha] daily drop counters reset (game day " + day + ")");
                }
                try { this.SaveKeybinds(false); } catch { }
            }
        }

        // Game-day index with the 06:00 boundary (the game's daily-refresh convention, cf.
        // DayIndexFrom2024060106 conditions). Primary: GameTimeUtility.GetCurrentGameTime via
        // AuraMono — boxed DateTime, unboxed as the raw 8-byte _dateData (kind bits masked off;
        // never read via the uint member helpers, they truncate boxed longs). Fallback: the
        // local clock with the same boundary (only day-CHANGE detection matters, so a fixed
        // offset from server time is harmless as long as it is stable).
        private unsafe long GetSanrioGameDayIndex()
        {
            try
            {
                if (this.EnsureAuraMonoApiReady() && this.AttachAuraMonoThread()
                    && auraMonoRuntimeInvoke != null && auraMonoObjectUnbox != null)
                {
                    if (this.sanrioGameTimeMethod == IntPtr.Zero)
                    {
                        IntPtr cls = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.GameTimeUtility");
                        if (cls == IntPtr.Zero)
                        {
                            cls = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService", "GameTimeUtility");
                        }
                        if (cls != IntPtr.Zero)
                        {
                            this.sanrioGameTimeMethod = this.FindAuraMonoMethodOnHierarchy(cls, "GetCurrentGameTime", 0);
                        }
                    }

                    if (this.sanrioGameTimeMethod != IntPtr.Zero)
                    {
                        IntPtr exc = IntPtr.Zero;
                        IntPtr boxed = auraMonoRuntimeInvoke(this.sanrioGameTimeMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                        if (exc == IntPtr.Zero && boxed != IntPtr.Zero)
                        {
                            IntPtr raw = auraMonoObjectUnbox(boxed);
                            if (raw != IntPtr.Zero)
                            {
                                long ticks = (long)((*(ulong*)raw) & 0x3FFFFFFFFFFFFFFFUL);
                                if (ticks > 0)
                                {
                                    return (ticks - 6L * TimeSpan.TicksPerHour) / TimeSpan.TicksPerDay;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return (DateTime.Now.Ticks - 6L * TimeSpan.TicksPerHour) / TimeSpan.TicksPerDay;
        }

        // PlayerTakeCandyEvent = an interaction drop SUCCEEDED just now. Attribute it to the
        // nearest known machine within 6 m; no machine nearby = some other drop (candy piles,
        // other events) — ignore. Runs on the main thread (event-hook engine contract).
        private void OnSanrioTakeCandyEvent(GameEventSnapshot snap)
        {
            if (!this.sanrioGachaFinderEnabled)
            {
                return;
            }

            try
            {
                if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos))
                {
                    return;
                }

                float bestSqr = SanrioDropMatchRadius * SanrioDropMatchRadius;
                int bestScene = -1;
                SanrioPlacedMachine bestPlaced = null;
                for (int i = 0; i < SanrioSceneMachineCount; i++)
                {
                    if (!this.sanrioMachines[i].Present)
                    {
                        continue;
                    }
                    float d = (this.sanrioMachines[i].Pos - playerPos).sqrMagnitude;
                    if (d < bestSqr)
                    {
                        bestSqr = d;
                        bestScene = i;
                        bestPlaced = null;
                    }
                }
                foreach (KeyValuePair<uint, SanrioPlacedMachine> kv in this.sanrioPlacedMachines)
                {
                    float d = (kv.Value.Pos - playerPos).sqrMagnitude;
                    if (d < bestSqr)
                    {
                        bestSqr = d;
                        bestScene = -1;
                        bestPlaced = kv.Value;
                    }
                }

                if (bestScene < 0 && bestPlaced == null)
                {
                    return; // unrelated interaction drop
                }

                this.EnsureSanrioDropDay();
                if (bestScene >= 0)
                {
                    this.sanrioDropSceneDoneMask |= 1 << bestScene;
                }
                else
                {
                    bestPlaced.DoneToday = true;
                }
                this.sanrioDropTotalToday = Math.Min(SanrioDropDailyCap, this.sanrioDropTotalToday + 1);
                try { this.SaveKeybinds(false); } catch { }

                string what = bestScene >= 0 ? ("Star Town machine " + (bestScene + 1)) : ("placed machine net=" + bestPlaced.NetId);
                ModLogger.Msg("[SanrioGacha] capsule drop tracked: " + what + " (" + this.sanrioDropTotalToday + "/" + SanrioDropDailyCap + " today)");
                this.AddMenuNotification(this.LF("Capsule collected — {0}/{1} today", this.sanrioDropTotalToday, SanrioDropDailyCap), new Color(1f, 0.65f, 0.85f));
            }
            catch
            {
            }
        }

        // Teleport helpers (buttons in the Extra tab).
        private void StartSanrioGachaTeleport(Vector3 pos, string label)
        {
            this.TeleportToLocation(pos);
            this.AddMenuNotification(this.LF("Teleported to {0}.", label), new Color(1f, 0.65f, 0.85f));
        }

        // Extra-tab section (called from DrawExtraFeaturesTab).
        private float DrawSanrioGachaSection(float y)
        {
            const float left = 40f;

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 1f);
            GUIStyle bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            bodyStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);

            GUI.Label(new Rect(left, y, 460f, 24f), this.L("Sanrio Gacha Machines"), headerStyle);
            y += 30f;

            bool prev = this.sanrioGachaFinderEnabled;
            this.sanrioGachaFinderEnabled = this.DrawSwitchToggle(new Rect(left, y, 360f, 30f), this.sanrioGachaFinderEnabled, "Sanrio Gacha Finder");
            if (this.sanrioGachaFinderEnabled != prev)
            {
                try { this.SaveKeybinds(false); } catch { }
            }
            y += 36f;

            if (!this.sanrioGachaFinderEnabled)
            {
                return y + 4f;
            }

            GUI.Label(new Rect(left, y, 500f, 62f),
                this.L("Finds every SANRIO gacha machine around you and pins it on the game map: the three event machines in Star Town plus machines placed by players in their homes (found while you roam; remembered for the session). Touching each machine drops a capsule reward once per day — up to 5 per day."),
                bodyStyle);
            y += 66f;

            // Daily tracker: only what the mod itself observed (drops made before enabling the
            // finder — or placed-machine marks from a previous login — are unknown to us).
            GUIStyle counterStyle = new GUIStyle(bodyStyle) { fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(left, y, 500f, 22f),
                this.LF("Capsule drops today (tracked): {0}/{1}", this.sanrioDropTotalToday, SanrioDropDailyCap), counterStyle);
            y += 26f;

            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;

            for (int i = 0; i < SanrioSceneMachineCount; i++)
            {
                string text;
                if (this.sanrioMachines[i].Present)
                {
                    float dist = cam != null ? Vector3.Distance(camPos, this.sanrioMachines[i].Pos) : -1f;
                    string src = this.sanrioMachines[i].Live ? this.L("live") : this.L("map point");
                    text = dist >= 0f
                        ? this.LF("Star Town machine {0}: located ({1}) — {2}m", i + 1, src, (int)dist)
                        : this.LF("Star Town machine {0}: located ({1})", i + 1, src);
                }
                else
                {
                    text = this.LF("Star Town machine {0}: not found", i + 1);
                }
                if ((this.sanrioDropSceneDoneMask & (1 << i)) != 0)
                {
                    text += this.L("  ✓ collected today");
                }
                GUI.Label(new Rect(left, y, 330f, 22f), text, bodyStyle);
                if (this.sanrioMachines[i].Present
                    && this.DrawSecondaryActionButton(new Rect(left + 340f, y - 4f, 130f, 26f), this.L("Teleport")))
                {
                    this.StartSanrioGachaTeleport(this.sanrioMachines[i].Pos, this.LF("Star Town machine {0}", i + 1));
                }
                y += 28f;
            }

            // Player-placed machines (nearest first).
            this.sanrioPlacedSorted.Clear();
            foreach (KeyValuePair<uint, SanrioPlacedMachine> kv in this.sanrioPlacedMachines)
            {
                this.sanrioPlacedSorted.Add(kv.Value);
            }
            if (cam != null)
            {
                this.sanrioPlacedSorted.Sort((a, b) =>
                    (a.Pos - camPos).sqrMagnitude.CompareTo((b.Pos - camPos).sqrMagnitude));
            }

            GUI.Label(new Rect(left, y, 500f, 22f),
                this.LF("Placed machines found this session: {0}", this.sanrioPlacedSorted.Count), bodyStyle);
            y += 26f;

            int rows = Math.Min(this.sanrioPlacedSorted.Count, SanrioPlacedRowsShown);
            for (int i = 0; i < rows; i++)
            {
                SanrioPlacedMachine placed = this.sanrioPlacedSorted[i];
                float dist = cam != null ? Vector3.Distance(camPos, placed.Pos) : -1f;
                string text = dist >= 0f
                    ? this.LF("Placed machine: {0}m", (int)dist)
                    : this.L("Placed machine");
                text += "  (net=" + placed.NetId + ")";
                if (placed.DoneToday)
                {
                    text += this.L("  ✓ collected today");
                }
                GUI.Label(new Rect(left, y, 330f, 22f), text, bodyStyle);
                if (this.DrawSecondaryActionButton(new Rect(left + 340f, y - 4f, 130f, 26f), this.L("Teleport")))
                {
                    this.StartSanrioGachaTeleport(placed.Pos, this.L("placed machine"));
                }
                y += 28f;
            }
            if (this.sanrioPlacedSorted.Count > rows)
            {
                GUI.Label(new Rect(left, y, 460f, 20f),
                    this.LF("...and {0} more (all pinned on the map).", this.sanrioPlacedSorted.Count - rows), bodyStyle);
                y += 24f;
            }

            if (this.sanrioLocatedCount == 0 && this.sanrioPlacedSorted.Count == 0)
            {
                GUI.Label(new Rect(left, y, 500f, 34f),
                    this.L("Event machines stand in Star Town (event runs 2026-07-17 – 2026-08-23); player-placed ones are discovered as you roam homes and plazas."),
                    bodyStyle);
                y += 38f;
            }

            return y + 8f;
        }
    }
}
