using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    // Radar -> Filter spawn-window gate - docs/plans/2026-11-01-radar-filter-spawn-expiry.md.
    // Hides catalog entries whose game spawn window is currently closed:
    //  - event/festival window (plan section 11.4): the game's own verdict, read live off the
    //    PictorialBase the quick-filter scan already pins (PictorialBase.get_SeasonType =
    //    PictorialSystem.GetSeasonType(pedia.BpPeriod, resurface); GetUnlockState evaluates the
    //    Pedia.unlockCondition DuringPeriod[Date] expression) - zero content drift;
    //  - month mask + day-period schedule (plan sections 6.2/6.3): TableBird/TableInsect
    //    appearMonth / timePeriod columns, same semantics as BaseAIClientService.IsSatisfyForCreateAi.
    // Everything fails open: unresolved API, missing row, pre-world, unreadable config -> visible.
    public partial class HeartopiaComplete
    {
        // EPictorialSeasonType (XDT.Scene.Shared.Modules.Pictorial), values verified against the
        // loaded image 2026-11-02. Daily = ActiveSeason 0 (no festival gate on the entry).
        private const int RadarSeasonReOpen = 3;
        private const int RadarSeasonDaily = 4;
        private const int RadarSeasonPastSeason = 5;
        private const int RadarSeasonPastMiniSeason = 6;
        private const int RadarSeasonNotOpenOrEngage = 7;

        // TableBird/TableInsect timePeriod ids are 1..4 (Morning/Day/Evening/Night, plan section 1).
        // Masks use bit (1 << id); monthMask stays the raw appearMonth bitmask (bit0 = January).
        private const int RadarTimePeriodMin = 1;
        private const int RadarTimePeriodMax = 4;

        private const int RadarSpawnGateBatchPerTick = 16;
        private const float RadarSpawnGateBatchRetrySeconds = 0.5f;
        private const float RadarSpawnGateResortSeconds = 2f;
        // A row that reads null right after a world load is usually "level tables not filled
        // yet", not "catalog id missing from the table" (plan 7 fail-open row). Retry a few
        // batch ticks before stamping it resolved-unknown for the epoch.
        private const int RadarSpawnGateRowTries = 3;
        // dayTimePeriods config walk: one warning after this many failed attempts, then stop
        // the whole Managers walk for this world (systematic failures cool down, AGENTS 9).
        private const int RadarSpawnGateDayPeriodWarnTries = 3;
        private const int RadarSpawnGateDayPeriodMaxTries = 5;
        // Cached game-clock snapshot for rebuild-triggered paths (plan 6.5: recompute at most
        // every ~10 s). The 2 s resort tick reads forced - that IS the change detector.
        private const float RadarSpawnGateSnapshotSeconds = 10f;

        private bool radarSpawnGateHooksRegistered;
        private bool radarSpawnGatePending;
        private float radarSpawnGateRetryAt;
        private float radarSpawnGateRecompareTo;
        private bool radarSpawnGateWarnedNoPeriod;
        private int radarSpawnGateLoggedEpoch = -1;

        // One snapshot per rebuild (plan section 6.5): month 1..12 (-1 = unknown -> month gate
        // skipped), periodId 1..4 (-1 = unknown -> daily gate skipped). Never local/UTC clocks:
        // the game calendar is its own (memory: game clock is not UTC).
        private int radarSpawnGateMonth = -1;
        private int radarSpawnGatePeriodId = -1;
        private float radarSpawnGateSnapshotAt;

        // TableData resolve cache, invalidated by world epoch (never kept across a world change).
        private IntPtr radarGateTableDataClass = IntPtr.Zero;
        private IntPtr radarGateGetBirdMethod = IntPtr.Zero;
        private IntPtr radarGateGetInsectMethod = IntPtr.Zero;
        private int radarGateApisEpoch = -1;

        // GameConfig.dayTimePeriods ring (periodId, beginHour) pairs - TimeExtension.GetTimePeriod
        // is an ITimeService extension, so resolve the config manager and run the same ring by hand.
        private readonly List<int[]> radarGateDayPeriods = new List<int[]>();
        private int radarGateDayPeriodsEpoch = -1;
        private int radarGateDayPeriodsAttempts;
        private float radarGateDayPeriodsRetryAt;

        // Registration runs on the world-ready gate only (AGENTS hard rule): the callback sets the
        // pending flag, the batch itself ticks in the already-existing radar-filter UI tick and
        // only while IsWorldReady. Called from EnsureRadarCollectionFilterHooks.
        private void EnsureRadarSpawnGateHooks()
        {
            if (this.radarSpawnGateHooksRegistered)
            {
                return;
            }

            this.radarSpawnGateHooksRegistered = true;
            this.RegisterWorldReadyCallback("RadarSpawnGate", () =>
            {
                this.radarSpawnGatePending = true;
                this.radarGateDayPeriods.Clear();
                this.radarGateDayPeriodsEpoch = -1;
                this.radarGateDayPeriodsAttempts = 0;
                this.radarGateDayPeriodsRetryAt = 0f;
                this.radarSpawnGateWarnedNoPeriod = false;
                // Catalog entries outlive worlds (they are per-language): re-arm per-row read
                // retries so a fresh level gets the bounded retry again.
                this.ResetRadarSpawnGateEntryState(this.radarInsectCatalog);
                this.ResetRadarSpawnGateEntryState(this.radarBirdCatalog);
                return true;
            });
        }

        private void ResetRadarSpawnGateEntryState(List<RadarSpeciesFilterEntry> catalog)
        {
            if (catalog == null)
            {
                return;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                RadarSpeciesFilterEntry entry = catalog[i];
                if (entry == null)
                {
                    continue;
                }

                entry.GateReadTries = 0;
                entry.TableGateEpoch = -1;
                entry.TableGateKnown = false;
                entry.EventGateKnown = false;
                entry.EventSeasonType = -1;
                entry.EventGateClosed = false;
            }
        }

        private void EnsureRadarSpawnGateApis()
        {
            if (this.radarGateApisEpoch == AuraMonoWorldEpoch && this.radarGateTableDataClass != IntPtr.Zero)
            {
                return;
            }

            this.radarGateApisEpoch = AuraMonoWorldEpoch;
            this.radarGateTableDataClass = IntPtr.Zero;
            this.radarGateGetBirdMethod = IntPtr.Zero;
            this.radarGateGetInsectMethod = IntPtr.Zero;
            if (!this.EnsureAuraMonoApiReady() || auraMonoClassFromName == null)
            {
                return;
            }

            IntPtr image = this.FindAuraMonoImage(new[] { "EcsClient", "EcsClient.dll" });
            if (image == IntPtr.Zero)
            {
                return;
            }

            IntPtr cls = auraMonoClassFromName(image, string.Empty, "TableData");
            if (cls == IntPtr.Zero)
            {
                cls = auraMonoClassFromName(image, "EcsClient", "TableData");
            }

            if (cls == IntPtr.Zero)
            {
                return;
            }

            this.radarGateTableDataClass = cls;
            // TableData.GetBird(int, bool=false) is 2-param (exact arity filter), with the
            // legacy 1-param shape as fallback - same discipline as GetFlowerplant.
            this.radarGateGetBirdMethod = this.FindAuraMonoMethodOnHierarchy(cls, "GetBird", 2);
            if (this.radarGateGetBirdMethod == IntPtr.Zero)
            {
                this.radarGateGetBirdMethod = this.FindAuraMonoMethodOnHierarchy(cls, "GetBird", 1);
            }

            this.radarGateGetInsectMethod = this.FindAuraMonoMethodOnHierarchy(cls, "GetInsect", 2);
            if (this.radarGateGetInsectMethod == IntPtr.Zero)
            {
                this.radarGateGetInsectMethod = this.FindAuraMonoMethodOnHierarchy(cls, "GetInsect", 1);
            }
        }

        // Static TableData.Get{Bird,Insect}(id, needException=false). Caller pins the returned row;
        // the row object must not outlive the pinning scope (SGen moving GC).
        private unsafe IntPtr GetRadarSpawnGateRow(int id, bool insects)
        {
            this.EnsureRadarSpawnGateApis();
            if (auraMonoRuntimeInvoke == null || this.radarGateTableDataClass == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr method = insects ? this.radarGateGetInsectMethod : this.radarGateGetBirdMethod;
            if (method == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            byte needException = 0;
            IntPtr exc = IntPtr.Zero;
            if (AuraMonoMethodParamCountIs(method, 2U))
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                return auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            IntPtr single = (IntPtr)(&id);
            return auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)(&single), ref exc);
        }

        // Row read once per world epoch; only scalar masks leave the pinning scope - no raw mono
        // row/array IntPtr may survive the call (SGen moving GC, CI lint E3/W1).
        private unsafe bool TryReadRadarSpawnGate(int id, bool insects,
            out int monthMask, out int periodMask)
        {
            monthMask = 0;
            periodMask = 0;
            if (id <= 0 || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr row = this.GetRadarSpawnGateRow(id, insects);
            if (row == IntPtr.Zero)
            {
                return false;
            }

            uint pin = AuraMonoPinNew(row);
            if (pin == 0U)
            {
                return false;
            }

            try
            {
                if (!this.TryGetMonoInt32Member(row, "appearMonth", out monthMask))
                {
                    return false;
                }

                periodMask = this.ReadRadarTimePeriodMask(row);
                return true;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        // TableBird/TableInsect timePeriod int[] - direct element reads (no unbox), ids 1..4.
        // Empty/missing array = column unused = mask 0 (conditional_spawns.py semantics).
        private unsafe int ReadRadarTimePeriodMask(IntPtr row)
        {
            if (!this.TryGetMonoObjectMember(row, "timePeriod", out IntPtr arr)
                || arr == IntPtr.Zero
                || !this.IsAuraMonoArrayObject(arr)
                || auraMonoArrayLength == null
                || auraMonoArrayAddrWithSize == null)
            {
                return 0;
            }

            ulong len = auraMonoArrayLength(arr).ToUInt64();
            IntPtr basePtr = auraMonoArrayAddrWithSize(arr, 4, UIntPtr.Zero);
            int mask = 0;
            for (ulong i = 0UL; i < len && i < 16UL && basePtr != IntPtr.Zero; i++)
            {
                int id = *(int*)((byte*)basePtr + i * 4UL);
                if (id >= RadarTimePeriodMin && id <= RadarTimePeriodMax)
                {
                    mask |= 1 << id;
                }
            }

            return mask;
        }

        // Budgeted table reads inside the already-active radar-filter UI tick (the
        // RefreshRadarSpeciesNames precedent): one lazy pass per world epoch, never a warm-up
        // before a world exists (world-ready callback sets the pending flag only).
        private void TickRadarSpawnGateBatch(UguiRadarFilterHandle handle, float now)
        {
            if (!this.radarSpawnGatePending || !this.IsWorldReady
                || now < this.radarSpawnGateRetryAt)
            {
                return;
            }

            this.radarSpawnGateRetryAt = now + RadarSpawnGateBatchRetrySeconds;
            int budget = RadarSpawnGateBatchPerTick;
            int resolved = 0;
            bool more = this.ResolveRadarSpawnGateCatalog(this.radarInsectCatalog, true, ref budget, ref resolved);
            if (this.ResolveRadarSpawnGateCatalog(this.radarBirdCatalog, false, ref budget, ref resolved))
            {
                more = true;
            }

            if (!more)
            {
                this.radarSpawnGatePending = false;
                this.RefreshRadarSpawnGateSnapshot(true);
                if (handle != null && resolved > 0)
                {
                    this.RebuildUguiRadarFilterVisible(handle, false);
                }

                this.LogRadarSpawnGateOnce();
                return;
            }

            if (handle != null && resolved > 0)
            {
                // Rebuild only when entries actually flipped gate state this tick; unknown
                // ones still fail open.
                this.RebuildUguiRadarFilterVisible(handle, false);
            }
        }

        private bool ResolveRadarSpawnGateCatalog(
            List<RadarSpeciesFilterEntry> catalog, bool insects, ref int budget, ref int resolved)
        {
            if (catalog == null)
            {
                return false;
            }

            int epoch = AuraMonoWorldEpoch;
            bool more = false;
            for (int i = 0; i < catalog.Count; i++)
            {
                RadarSpeciesFilterEntry entry = catalog[i];
                if (entry == null || entry.TableGateEpoch == epoch)
                {
                    continue;
                }

                if (budget <= 0)
                {
                    more = true;
                    continue;
                }

                budget--;
                bool known = this.TryReadRadarSpawnGate(entry.Id, insects,
                    out entry.AppearMonthMask, out entry.TimePeriodMask);
                if (known || entry.GateReadTries >= RadarSpawnGateRowTries)
                {
                    entry.TableGateEpoch = epoch;
                    entry.TableGateKnown = known;
                    resolved++;
                }
                else
                {
                    // Transient pre-table-load miss: keep the entry unstampeded and retry on a
                    // later batch tick (bounded); fail-open meanwhile.
                    entry.GateReadTries++;
                    more = true;
                }
            }

            return more;
        }

        // Month/period snapshot from the game's own clock - GameTimeUtility.GetCurrentGameTime,
        // the exact tick read the research ledger already uses. Never local/UTC clocks.
        // Rebuild-triggered paths use the cached value (month/period move on game-hour
        // granularity); the resort tick and batch completion pass force=true.
        private bool RefreshRadarSpawnGateSnapshot(bool force = false)
        {
            float now = Time.unscaledTime;
            if (!force
                && this.radarSpawnGateMonth >= 1
                && now < this.radarSpawnGateSnapshotAt + RadarSpawnGateSnapshotSeconds)
            {
                return true;
            }

            long ticks = this.GetResearchGameTimeTicks();
            if (ticks == 0L)
            {
                return false;
            }

            DateTime gameNow = new DateTime(ticks);
            this.radarSpawnGateMonth = gameNow.Month;
            this.radarSpawnGatePeriodId = this.GetRadarSpawnGatePeriodId(gameNow.Hour);
            this.radarSpawnGateSnapshotAt = now;
            return true;
        }

        // Same beginHour ring as TimeExtension.GetTimePeriod (EcsSystem.Time lines 14-33);
        // -1 = unknown -> the daily gate is skipped, the month gate still runs.
        private int GetRadarSpawnGatePeriodId(int hour)
        {
            this.EnsureRadarDayPeriods();
            int count = this.radarGateDayPeriods.Count;
            if (count == 0)
            {
                if (!this.radarSpawnGateWarnedNoPeriod
                    && this.radarGateDayPeriodsAttempts >= RadarSpawnGateDayPeriodWarnTries)
                {
                    this.radarSpawnGateWarnedNoPeriod = true;
                    ModLogger.Msg("[RadarFilter] dayTimePeriods unreadable - daily gate skipped");
                }

                return -1;
            }

            for (int i = 0; i < count; i++)
            {
                int begin = this.radarGateDayPeriods[i][1];
                int next = this.radarGateDayPeriods[(i + 1) % count][1];
                if (begin < next)
                {
                    if (hour >= begin && hour < next)
                    {
                        return this.radarGateDayPeriods[i][0];
                    }
                }
                else if (hour >= begin || (hour >= 0 && hour < next))
                {
                    return this.radarGateDayPeriods[i][0];
                }
            }

            return -1;
        }

        // Month/period flip while the sub-tab is open (plan section 6.6): cheap compare on the
        // tick that already exists - a flip re-dirties the pictorial scan (event windows move at
        // hour boundaries too) and rebuilds Visible without resetting scroll.
        private void TickRadarSpawnGateResort(UguiRadarFilterHandle handle, float now)
        {
            if (handle == null || now < this.radarSpawnGateRecompareTo)
            {
                return;
            }

            this.radarSpawnGateRecompareTo = now + RadarSpawnGateResortSeconds;
            int prevMonth = this.radarSpawnGateMonth;
            int prevPeriod = this.radarSpawnGatePeriodId;
            // Forced: this tick IS the change detector (the cached snapshot would hide flips).
            if (!this.RefreshRadarSpawnGateSnapshot(true))
            {
                return;
            }

            if (this.radarSpawnGateMonth != prevMonth || this.radarSpawnGatePeriodId != prevPeriod)
            {
                this.radarCollectionFilterDirty = true;
                this.RebuildUguiRadarFilterVisible(handle, false);
            }
        }

        // GameConfig.dayTimePeriods via the shipped Managers._serviceDic[IConfigManager].manager
        // walk (TryResolveCorruptionConfigManager contract: caller frees managerPin). The game's
        // own GetTimePeriod is an ITimeService extension, so the config is read and the ring is
        // run by hand (GetRadarSpawnGatePeriodId).
        private unsafe void EnsureRadarDayPeriods()
        {
            int epoch = AuraMonoWorldEpoch;
            if (this.radarGateDayPeriods.Count > 0 && this.radarGateDayPeriodsEpoch == epoch)
            {
                return;
            }

            if (!this.IsWorldReady || Time.unscaledTime < this.radarGateDayPeriodsRetryAt
                || this.radarGateDayPeriodsAttempts >= RadarSpawnGateDayPeriodMaxTries)
            {
                return;
            }

            this.radarGateDayPeriodsAttempts++;
            this.radarGateDayPeriodsRetryAt = Time.unscaledTime + RadarSpawnGateBatchRetrySeconds;
            this.radarGateDayPeriods.Clear();
            if (!this.TryResolveCorruptionConfigManager(
                    out IntPtr configManagerObj, out uint managerPin, out string _))
            {
                return;
            }

            try
            {
                if (!this.TryInvokeAuraMonoZeroArg(configManagerObj, out IntPtr gameConfigObj,
                        "get_GameConfig")
                    || gameConfigObj == IntPtr.Zero)
                {
                    return;
                }

                uint configPin = AuraMonoPinNew(gameConfigObj);
                if (configPin == 0U)
                {
                    return;
                }

                try
                {
                    this.ReadRadarDayPeriodsArray(gameConfigObj);
                }
                finally
                {
                    AuraMonoPinFree(configPin);
                }

                // Only stamp the epoch on success; a failed read retries on later snapshot
                // refreshes (bounded by the attempt count used for the warning).
                if (this.radarGateDayPeriods.Count > 0)
                {
                    this.radarGateDayPeriodsEpoch = epoch;
                }
            }
            finally
            {
                AuraMonoPinFree(managerPin);
            }
        }

        // DayTimePeriod struct[] read with the layout RESOLVED from metadata (the shipping
        // AuraFarm SelectPriorityInfo precedent): element class from the array class, field
        // offsets from mono_class_get_field_from_name minus the object header that
        // mono_field_get_offset counts (inline array elements have none), stride from
        // mono_array_element_size. A patch that grows the struct then follows itself instead
        // of silently mis-gating; sanity guards remain as the second net.
        private unsafe void ReadRadarDayPeriodsArray(IntPtr gameConfigObj)
        {
            if (!this.TryGetMonoObjectMember(gameConfigObj, "dayTimePeriods", out IntPtr arr)
                || arr == IntPtr.Zero
                || !this.IsAuraMonoArrayObject(arr)
                || auraMonoArrayLength == null
                || auraMonoArrayAddrWithSize == null
                || auraMonoObjectGetClass == null
                || auraMonoClassGetElementClass == null
                || auraMonoClassGetFieldFromName == null
                || auraMonoFieldGetOffset == null
                || auraMonoArrayElementSize == null)
            {
                return;
            }

            ulong len = auraMonoArrayLength(arr).ToUInt64();
            if (len == 0UL || len > 12UL)
            {
                return;
            }

            IntPtr arrayClass = auraMonoObjectGetClass(arr);
            IntPtr elemClass = arrayClass != IntPtr.Zero
                ? auraMonoClassGetElementClass(arrayClass)
                : IntPtr.Zero;
            IntPtr idField = elemClass != IntPtr.Zero
                ? auraMonoClassGetFieldFromName(elemClass, "periodId")
                : IntPtr.Zero;
            IntPtr hourField = elemClass != IntPtr.Zero
                ? auraMonoClassGetFieldFromName(elemClass, "beginHour")
                : IntPtr.Zero;
            int elementSize = arrayClass != IntPtr.Zero
                ? auraMonoArrayElementSize(arrayClass)
                : 0;
            if (idField == IntPtr.Zero || hourField == IntPtr.Zero || elementSize <= 0)
            {
                return;
            }

            int idOffset = (int)auraMonoFieldGetOffset(idField) - 2 * IntPtr.Size;
            int hourOffset = (int)auraMonoFieldGetOffset(hourField) - 2 * IntPtr.Size;
            if (idOffset < 0 || hourOffset < 0
                || idOffset >= elementSize || hourOffset >= elementSize)
            {
                return;
            }

            for (ulong i = 0UL; i < len; i++)
            {
                IntPtr elemAddr = auraMonoArrayAddrWithSize(arr, elementSize, (UIntPtr)i);
                if (elemAddr == IntPtr.Zero)
                {
                    continue;
                }

                byte* elem = (byte*)elemAddr;
                int periodId = *(int*)(elem + idOffset);
                int beginHour = *(int*)(elem + hourOffset);
                if (periodId >= RadarTimePeriodMin && periodId <= 8
                    && beginHour >= 0 && beginHour <= 23)
                {
                    this.radarGateDayPeriods.Add(new int[] { periodId, beginHour });
                }
            }
        }

        // The single hide predicate (plan section 6.3). Semantics copied from the game:
        // Pictorial season verdict first (festival window, section 11.4), then the month bit
        // mask and the day-period schedule (IsSatisfyForCreateAi). Unknown -> fail open.
        private bool RadarSpeciesInSpawnWindow(RadarSpeciesFilterEntry entry)
        {
            if (entry == null)
            {
                return true;
            }

            if (entry.EventGateKnown && entry.EventGateClosed)
            {
                return false;
            }

            if (entry.TableGateEpoch != AuraMonoWorldEpoch || !entry.TableGateKnown)
            {
                return true;
            }

            int month = this.radarSpawnGateMonth;
            if (month >= 1 && month <= 12 && ((1 << (month - 1)) & entry.AppearMonthMask) == 0)
            {
                return false;
            }

            int period = this.radarSpawnGatePeriodId;
            if (entry.TimePeriodMask != 0 && period > 0
                && ((1 << period) & entry.TimePeriodMask) == 0)
            {
                return false;
            }

            return true;
        }

        // Quick-set apply intersection (plan section 6.4): a quick button must not NEWLY select
        // ids the grid hides, or the Summary count and the visible grid disagree. Ids the user
        // had already selected before the click stay in the new list even while window-closed -
        // dropping them would silently delete saved-config entries (plan section 7).
        private HashSet<int> FilterRadarSpeciesToSpawnWindow(HashSet<int> ids, bool insects)
        {
            if (ids == null || ids.Count == 0)
            {
                return ids;
            }

            this.RefreshRadarSpawnGateSnapshot();
            HashSet<int> current = insects ? this.radarInsectSpeciesFilter : this.radarBirdSpeciesFilter;
            HashSet<int> kept = new HashSet<int>();
            foreach (int id in ids)
            {
                RadarSpeciesFilterEntry entry = this.GetRadarSpeciesFilterEntry(id);
                if (current.Contains(id) || entry == null || this.RadarSpeciesInSpawnWindow(entry))
                {
                    kept.Add(id);
                }
            }

            return kept;
        }

        // id -> entry lookup for the quick-set intersection; rebuilt together with the
        // per-language catalog (EnsureRadarSpeciesCatalog owns catalog lifetime).
        private readonly Dictionary<int, RadarSpeciesFilterEntry> radarSpeciesEntryById =
            new Dictionary<int, RadarSpeciesFilterEntry>();
        private string radarSpeciesEntryMapLanguage = string.Empty;

        private RadarSpeciesFilterEntry GetRadarSpeciesFilterEntry(int id)
        {
            string lang = this.selectedLanguage ?? string.Empty;
            if (this.radarSpeciesEntryById.Count == 0
                || !string.Equals(lang, this.radarSpeciesEntryMapLanguage, StringComparison.Ordinal))
            {
                this.EnsureRadarSpeciesCatalog();
                this.radarSpeciesEntryById.Clear();
                this.AddRadarEntriesToMap(this.radarInsectCatalog);
                this.AddRadarEntriesToMap(this.radarBirdCatalog);
                this.radarSpeciesEntryMapLanguage = lang;
            }

            this.radarSpeciesEntryById.TryGetValue(id, out RadarSpeciesFilterEntry entry);
            return entry;
        }

        private void AddRadarEntriesToMap(List<RadarSpeciesFilterEntry> catalog)
        {
            if (catalog == null)
            {
                return;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                RadarSpeciesFilterEntry entry = catalog[i];
                if (entry != null)
                {
                    this.radarSpeciesEntryById[entry.Id] = entry;
                }
            }
        }

        // Event/festival gate (plan section 11.4, live-verdict variant): the game's own verdict
        // off the pinned PictorialBase. get_SeasonType = PictorialSystem.GetSeasonType(
        // pedia.BpPeriod, resurface). Daily(4) -> no festival gate. NotOpenOrEngage/Past* ->
        // window closed -> hide. ReOpen (resurface festivals) -> the weekly Pedia.unlockCondition
        // DuringPeriod[Date] window decides via the game's own GetUnlockState(). This is what
        // drops the expired Dreamlight bees while live BP209 species stay visible.
        private bool TryApplyRadarSpawnEventGate(IntPtr dataObj, RadarSpeciesFilterEntry entry)
        {
            if (!this.TryInvokeAuraMonoZeroArgInt(dataObj, out int seasonType, "get_SeasonType"))
            {
                return false;
            }

            bool closed;
            if (seasonType == RadarSeasonNotOpenOrEngage
                || seasonType == RadarSeasonPastSeason
                || seasonType == RadarSeasonPastMiniSeason)
            {
                closed = true;
            }
            else if (seasonType != RadarSeasonReOpen)
            {
                // Daily / CurrentSeason / CurrentMiniSeason: gate absent or currently open.
                closed = false;
            }
            else if (this.TryInvokeAuraMonoZeroArg(dataObj, out IntPtr boxedUnlock, "GetUnlockState")
                && boxedUnlock != IntPtr.Zero
                && this.TryUnboxMonoBoolean(boxedUnlock, out bool unlocked))
            {
                closed = !unlocked;
            }
            else
            {
                // ReOpen without the weekly-window verdict: leave the entry UNKNOWN (fail open,
                // re-tried on the next scan). A read failure must never stamp "known open" -
                // that would also silence the entry in the verification log (plan 11.4.1).
                return false;
            }

            entry.EventGateKnown = true;
            entry.EventSeasonType = seasonType;
            entry.EventGateClosed = closed;
            return true;
        }

        // One line per world epoch (plan section 6.7), pattern of radarCollectionFilterScanFailedLogged.
        private void LogRadarSpawnGateOnce()
        {
            int epoch = AuraMonoWorldEpoch;
            if (this.radarSpawnGateLoggedEpoch == epoch)
            {
                return;
            }

            this.radarSpawnGateLoggedEpoch = epoch;
            int insTot = this.radarInsectCatalog != null ? this.radarInsectCatalog.Count : 0;
            int birdTot = this.radarBirdCatalog != null ? this.radarBirdCatalog.Count : 0;
            ModLogger.Msg("[RadarFilter] spawn gate month=" + this.radarSpawnGateMonth.ToString()
                + " period=" + this.radarSpawnGatePeriodId.ToString()
                + ", insects hidden " + this.CountRadarWindowHidden(this.radarInsectCatalog).ToString()
                + "/" + insTot.ToString()
                + ", birds hidden " + this.CountRadarWindowHidden(this.radarBirdCatalog).ToString()
                + "/" + birdTot.ToString());

            // Verification material for the live event verdict (plan 11.4.1): every season-gated
            // entry as id:seasonType + x (hidden) / o (open). Expired festival species must read
            // x, the current-season ones o, until the window moves.
            StringBuilder sb = new StringBuilder("[RadarFilter] event gates:");
            int gated = 0;
            this.AppendRadarEventGateLog(this.radarInsectCatalog, sb, ref gated);
            this.AppendRadarEventGateLog(this.radarBirdCatalog, sb, ref gated);
            if (gated > 0)
            {
                ModLogger.Msg(sb.ToString());
            }
        }

        private void AppendRadarEventGateLog(
            List<RadarSpeciesFilterEntry> catalog, StringBuilder sb, ref int gated)
        {
            if (catalog == null)
            {
                return;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                RadarSpeciesFilterEntry entry = catalog[i];
                // Log every season-tagged entry (current, ReOpen, past, not-yet-open);
                // Daily(-tagless) entries are the bulk and stay out of the line.
                if (entry == null
                    || !entry.EventGateKnown
                    || entry.EventSeasonType < 0
                    || entry.EventSeasonType == RadarSeasonDaily)
                {
                    continue;
                }

                gated++;
                if (sb.Length < 1600)
                {
                    sb.Append(' ')
                        .Append(entry.Id.ToString())
                        .Append(':')
                        .Append(entry.EventSeasonType.ToString())
                        .Append(entry.EventGateClosed ? 'x' : 'o');
                }
            }
        }

        private int CountRadarWindowHidden(List<RadarSpeciesFilterEntry> catalog)
        {
            if (catalog == null)
            {
                return 0;
            }

            int hidden = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i] != null && !this.RadarSpeciesInSpawnWindow(catalog[i]))
                {
                    hidden++;
                }
            }

            return hidden;
        }
    }
}
