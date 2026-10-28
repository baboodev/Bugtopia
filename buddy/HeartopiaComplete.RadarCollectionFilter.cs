using System;
using System.Collections.Generic;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        // Positive id that is not an insect or bird staticId. A quick filter that matches
        // nothing still has to be a non-empty allow-list: empty means show every species.
        private const int RadarSpeciesFilterNoneId = 1;
        private const int RadarPictorialStateNotGet = 0;
        private const int RadarPictorialStateGot = 2;

        private static bool RadarCollectionFilterIsHidden(int staticId)
        {
            return staticId == 51305 || staticId == 51406 || staticId == 51503 || staticId == 63001;
        }

        // Certificcation._nextLevel where _level == 3. Rank 4 (_nextLevel 0) starts at this count.
        private static readonly Dictionary<int, int> RadarMasterThresholdByType = new Dictionary<int, int>
        {
            { 8001, 1200 }, { 8002, 1200 }, { 8003, 720 }, { 8004, 480 }, { 8005, 300 },
            { 7001, 2400 }, { 7002, 2400 }, { 7003, 1440 }, { 7004, 1080 }, { 7005, 600 },
        };

        // CollectCertification.openCertification == 1 only. Value is certificationType.
        // Regenerate from the SQL in docs/superpowers/plans/2026-10-28-radar-collection-quick-filters.md
        // after a table export. Hidden book ids are absent.
        private static readonly Dictionary<int, int> RadarOpenCertificationTypeById = BuildRadarOpenCertificationTypeById();

        private readonly HashSet<int> radarInsectQuickUnopened = new HashSet<int>();
        private readonly HashSet<int> radarInsectQuickBelow5 = new HashSet<int>();
        private readonly HashSet<int> radarInsectQuickNotMaster = new HashSet<int>();
        private readonly HashSet<int> radarBirdQuickUnopened = new HashSet<int>();
        private readonly HashSet<int> radarBirdQuickBelow5 = new HashSet<int>();
        private readonly HashSet<int> radarBirdQuickNotMaster = new HashSet<int>();

        private IntPtr radarPictorialTryGetMethod = IntPtr.Zero;
        private int radarCollectionFilterEpoch = -1;
        private bool radarCollectionFilterReady;
        private bool radarCollectionFilterDirty = true;
        private bool radarCollectionFilterScanFailedLogged;
        private bool radarCollectionFilterHooksRegistered;

        private static Dictionary<int, int> BuildRadarOpenCertificationTypeById()
        {
            Dictionary<int, int> map = new Dictionary<int, int>(152);
            AddRadarCertIds(map, 8001, new int[] { 51101, 51104, 51107, 51113, 51121, 51123, 51203, 51301, 51401, 51501 });
            AddRadarCertIds(map, 8002, new int[] { 51103, 51118, 51202, 51205, 51206, 51209, 51302, 51303, 51307, 51405, 51502, 51506, 51702, 51705 });
            AddRadarCertIds(map, 8003, new int[] { 51105, 51106, 51108, 51109, 51117, 51122, 51126, 51208, 51212, 51304, 51402, 51404, 51407, 51413, 51505, 51602, 51604, 51703, 51704 });
            AddRadarCertIds(map, 8004, new int[] { 51102, 51112, 51116, 51120, 51201, 51204, 51210, 51213, 51215, 51403, 51504 });
            AddRadarCertIds(map, 8005, new int[] { 51111, 51114, 51115, 51119, 51124, 51125, 51130, 51131, 51211, 51214, 51216, 51220, 51221, 51306, 51408, 51507, 51508, 51601, 51603, 51701, 51801, 51936 });
            AddRadarCertIds(map, 7001, new int[] { 61101, 61103, 61105, 61116, 61118, 61138, 61201, 61202, 61203, 61301, 61307, 61401 });
            AddRadarCertIds(map, 7002, new int[] { 61109, 61111, 61113, 61136, 61137, 61139, 61204, 61205, 61402, 61405, 61406, 61501, 61504, 61701 });
            AddRadarCertIds(map, 7003, new int[] { 61102, 61104, 61106, 61107, 61110, 61114, 61119, 61140, 61215, 61226, 61303, 61404, 61503, 61702 });
            AddRadarCertIds(map, 7004, new int[] { 61112, 61117, 61122, 61141, 61206, 61207, 61208, 61209, 61502, 61801, 61901 });
            AddRadarCertIds(map, 7005, new int[] { 61108, 61115, 61120, 61121, 61124, 61125, 61210, 61302, 61306, 61308, 61309, 61310, 61403, 61407, 61505, 61506, 61703, 61704, 61709, 61710, 61802, 61803, 61902, 61903, 61904 });
            return map;
        }

        private static void AddRadarCertIds(Dictionary<int, int> map, int certificationType, int[] ids)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                map[ids[i]] = certificationType;
            }
        }

        private static void ClassifyRadarCollectionSpecies(
            int staticId, bool hasEntry, int state, bool starKnown, int star, bool countKnown, int count,
            out bool unopened, out bool below5, out bool notMaster)
        {
            unopened = false;
            below5 = false;
            notMaster = false;
            if (RadarCollectionFilterIsHidden(staticId) || !hasEntry)
            {
                return;
            }

            unopened = state == RadarPictorialStateNotGet;
            below5 = state == RadarPictorialStateGot && starKnown && star < 5;
            int certificationType;
            int threshold;
            notMaster = RadarOpenCertificationTypeById.TryGetValue(staticId, out certificationType)
                && RadarMasterThresholdByType.TryGetValue(certificationType, out threshold)
                && countKnown
                && count < threshold;
        }

        private int CountDisplayedRadarSpeciesFilter(HashSet<int> set)
        {
            if (set == null || set.Count == 0)
            {
                return 0;
            }

            int count = set.Count;
            if (set.Contains(RadarSpeciesFilterNoneId))
            {
                count--;
            }

            return count;
        }

        private static bool RadarSpeciesFilterEquals(HashSet<int> current, HashSet<int> quick)
        {
            if (current == null)
            {
                return false;
            }

            if (quick == null || quick.Count == 0)
            {
                return current.Count == 1 && current.Contains(RadarSpeciesFilterNoneId);
            }

            if (current.Count != quick.Count || current.Contains(RadarSpeciesFilterNoneId))
            {
                return false;
            }

            foreach (int id in quick)
            {
                if (!current.Contains(id))
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyRadarSpeciesFilter(bool insects, HashSet<int> ids)
        {
            HashSet<int> set = insects ? this.radarInsectSpeciesFilter : this.radarBirdSpeciesFilter;
            List<int> list = insects ? this.radarInsectSpeciesFilterList : this.radarBirdSpeciesFilterList;
            set.Clear();
            list.Clear();
            if (ids != null)
            {
                foreach (int id in ids)
                {
                    if (id <= 0 || id == RadarSpeciesFilterNoneId || !set.Add(id))
                    {
                        continue;
                    }

                    list.Add(id);
                }
            }

            if (set.Count == 0)
            {
                set.Add(RadarSpeciesFilterNoneId);
                list.Add(RadarSpeciesFilterNoneId);
            }

            this.QueueRadarSettingsSave();
            this.DropRadarMarkersRejectedBySpeciesFilter(insects);
            UguiRadarFilterHandle handle = this.uguiRadarFilter;
            if (handle != null)
            {
                this.RebuildUguiRadarFilterVisible(handle, false);
                this.SyncUguiRadarFilterChrome(handle);
                this.SyncUguiRadarFilterGrid(handle);
            }
        }

        private bool TryRebuildRadarCollectionQuickSets()
        {
            try
            {
                this.EnsureRadarSpeciesCatalog();
                int epoch = AuraMonoWorldEpoch;
                if (epoch != this.radarCollectionFilterEpoch)
                {
                    this.radarCollectionFilterEpoch = epoch;
                    this.radarPictorialTryGetMethod = IntPtr.Zero;
                    this.radarCollectionFilterReady = false;
                }

                this.radarInsectQuickUnopened.Clear();
                this.radarInsectQuickBelow5.Clear();
                this.radarInsectQuickNotMaster.Clear();
                this.radarBirdQuickUnopened.Clear();
                this.radarBirdQuickBelow5.Clear();
                this.radarBirdQuickNotMaster.Clear();

                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
                {
                    this.radarCollectionFilterReady = false;
                    return false;
                }

                IntPtr systemObj;
                if (!this.TryResolveAuraMonoModule(
                        "XDTGameSystem.GameplaySystem.Pictorial.PictorialSystem", out systemObj)
                    || systemObj == IntPtr.Zero)
                {
                    this.radarCollectionFilterReady = false;
                    return false;
                }

                uint sysPin = AuraMonoPinNew(systemObj);
                if (sysPin == 0U)
                {
                    this.radarCollectionFilterReady = false;
                    return false;
                }

                try
                {
                    if (!this.ScanRadarCollectionCatalog(systemObj, this.radarInsectCatalog, true)
                        || !this.ScanRadarCollectionCatalog(systemObj, this.radarBirdCatalog, false))
                    {
                        this.radarCollectionFilterReady = false;
                        return false;
                    }
                }
                finally
                {
                    AuraMonoPinFree(sysPin);
                }

                this.radarCollectionFilterReady = true;
                this.radarCollectionFilterScanFailedLogged = false;
                return true;
            }
            catch (Exception ex)
            {
                this.radarCollectionFilterReady = false;
                if (!this.radarCollectionFilterScanFailedLogged)
                {
                    this.radarCollectionFilterScanFailedLogged = true;
                    ModLogger.Msg("[RadarFilter] pictorial scan failed: " + ex.Message);
                }

                return false;
            }
        }

        private bool ScanRadarCollectionCatalog(IntPtr systemObj, List<RadarSpeciesFilterEntry> catalog, bool insects)
        {
            if (catalog == null)
            {
                return true;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                if (!this.TryClassifyRadarCatalogEntry(systemObj, catalog[i], insects))
                {
                    return false;
                }
            }

            return true;
        }

        private unsafe bool TryClassifyRadarCatalogEntry(IntPtr systemObj, RadarSpeciesFilterEntry entry, bool insects)
        {
            if (entry == null || RadarCollectionFilterIsHidden(entry.Id))
            {
                return true;
            }

            if (this.radarPictorialTryGetMethod == IntPtr.Zero)
            {
                IntPtr sysClass = auraMonoObjectGetClass(systemObj);
                this.radarPictorialTryGetMethod = this.FindAuraMonoMethodOnHierarchy(sysClass, "TryGetPictorialData", 2);
            }

            if (this.radarPictorialTryGetMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            int id = entry.Id;
            IntPtr outSlot = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&id);
            args[1] = (IntPtr)(&outSlot);
            IntPtr boxedOk = auraMonoRuntimeInvoke(this.radarPictorialTryGetMethod, systemObj, (IntPtr)args, ref exc);
            bool hasEntry = exc == IntPtr.Zero && outSlot != IntPtr.Zero
                && (boxedOk == IntPtr.Zero || !this.TryUnboxMonoBoolean(boxedOk, out bool ok) || ok);
            int state = 0;
            bool starKnown = false;
            int star = 0;
            bool countKnown = false;
            int count = 0;
            if (hasEntry)
            {
                uint pin = AuraMonoPinNew(outSlot);
                if (pin == 0U)
                {
                    return false;
                }

                try
                {
                    if (!this.TryGetMonoInt32Member(outSlot, "_state", out state))
                    {
                        hasEntry = false;
                    }
                    else
                    {
                        countKnown = this.TryInvokeAuraMonoZeroArgInt(outSlot, out count, "GetCount");
                        if (state == RadarPictorialStateGot)
                        {
                            // Lookup starts at the instance class, so birds hit PictorialBird.GetStarRate
                            // (maxScore) instead of the base field, which stays 0.
                            starKnown = this.TryInvokeAuraMonoZeroArgInt(outSlot, out star, "GetStarRate");
                        }
                    }
                }
                finally
                {
                    AuraMonoPinFree(pin);
                }
            }

            bool unopened;
            bool below5;
            bool notMaster;
            ClassifyRadarCollectionSpecies(entry.Id, hasEntry, state, starKnown, star, countKnown, count,
                out unopened, out below5, out notMaster);
            HashSet<int> unopenedSet = insects ? this.radarInsectQuickUnopened : this.radarBirdQuickUnopened;
            HashSet<int> below5Set = insects ? this.radarInsectQuickBelow5 : this.radarBirdQuickBelow5;
            HashSet<int> masterSet = insects ? this.radarInsectQuickNotMaster : this.radarBirdQuickNotMaster;
            if (unopened)
            {
                unopenedSet.Add(entry.Id);
            }

            if (below5)
            {
                below5Set.Add(entry.Id);
            }

            if (notMaster)
            {
                masterSet.Add(entry.Id);
            }

            return true;
        }

        private void EnsureRadarCollectionFilterHooks()
        {
            if (this.radarCollectionFilterHooksRegistered)
            {
                return;
            }

            this.radarCollectionFilterHooksRegistered = true;
            this.RegisterGameEventHook(
                "XDTDataAndProtocol.Events.UpdatePictorialEvent", 0, this.OnRadarCollectionFilterPictorialDirty);
            this.RegisterGameEventHook(
                "XDTDataAndProtocol.Events.UpdatePictorialDetailEvent", 0, this.OnRadarCollectionFilterPictorialDirty);
            this.RegisterGameEventHook(
                "XDTDataAndProtocol.Events.UpdatePictorialExtraEvent", 0, this.OnRadarCollectionFilterPictorialDirty);
            this.RegisterWorldReadyCallback("RadarCollectionFilter", this.OnRadarCollectionFilterWorldReady);
        }

        private void OnRadarCollectionFilterPictorialDirty(GameEventSnapshot snapshot)
        {
            this.radarCollectionFilterDirty = true;
        }

        private bool OnRadarCollectionFilterWorldReady()
        {
            this.radarCollectionFilterDirty = true;
            return true;
        }
    }
}
