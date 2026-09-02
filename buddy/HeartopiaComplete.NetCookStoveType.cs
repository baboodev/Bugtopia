using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // MASS COOK — STOVE TYPE PICKER
    //
    // Problem this solves: capture used to decide the stove type by MAJORITY VOTE and then drop
    // every other stove silently (RemoveIncompatibleNetCookTargets → IsCompatibleNetCookCooker,
    // HeartopiaComplete.NetCook.cs). Standing in a mixed kitchen (a plain stove + a hot pot + a
    // grill) meant the minority kinds simply vanished with no way to reach their recipes.
    //
    // The two "cooker type" quantities — do not confuse them:
    //  - COOKWARE type: CookingComponent.cookerwareType = (CookwareType)TableCooker.cookwareType.
    //    This is what the live capture resolves per burner and what `netCookCookerType` holds; it
    //    drives the legacy target-compatibility comparison and the prepare command. It is NOT the
    //    recipe key: cookware "Boil" covers the ordinary stove, the elephant food truck, the
    //    penguin stove … each with a DIFFERENT recipe list.
    //  - RECIPE cooker type: TableCooker.cookerType. CookingSystem.GetAllRecipes(cookerStaticId)
    //    resolves staticId → TableCooker.cookerType → _cookingRecipes[cookerType], so the recipe
    //    list is a pure function of THIS value. 16 recipe types across 31 cooker staticIds; type 1
    //    alone covers 18 stove models sharing one 141-recipe list.
    //  (The pre-existing TryGetCookerTypeForStaticId also reads TableCooker.cookerType, but through
    //   managed reflection, which is dead on IL2CPP — see the note at NetCook.cs:6104. This file
    //   resolves the same value over AuraMono instead, which is the live channel.)
    //
    // Therefore the picker groups by RECIPE cooker type: one entry per distinct recipe list found
    // in range, which is exactly "pick a stove type → see its recipes".
    //
    // Design:
    //  - SNAPSHOT: every candidate the scan resolved, all types, is copied into netCookScannedTargets
    //    BEFORE the desired-type vote prunes the working set. Purely additive — the existing capture
    //    pipeline and its filter order are untouched.
    //  - CENSUS: netCookScannedCookerTypes, built from the snapshot through the SAME range/own
    //    filters the working set passes, so "found in radius" means the same thing on both sides.
    //    Cook-build owners the deferred expansion rejected before enumerating their burners are
    //    folded in as ObservedCount, so a type only that pass can see still shows up.
    //  - PICK: netCookPreferredCookerType (0 = Auto/majority). Session-only on purpose — a saved
    //    special-cooker type would make the next homeland capture 0 stoves for no visible reason.
    //    While a pick is active IsCompatibleNetCookCooker compares by recipe type instead of
    //    cookware, and both preferred-getters vote only inside the pinned group.
    //  - SWITCH: rebuilds netCookTargets from the snapshot (no re-scan, no server traffic), rebuilds
    //    the recipe cache, then kicks the normal deferred expansion for the new type. If the snapshot
    //    has nothing live for that type it falls back to a full capture with the pick applied.
    //
    // Fail-closed: a type whose recipe cooker type cannot be resolved never enters the census, so
    // the picker offers only types it can actually pin (and says so in the log).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // Snapshot cap. The working set has NetCookMaxCaptureTargets; the snapshot also carries the
        // types we are NOT cooking on, so it gets its own (larger) bound. Snapshot entries are inert
        // copies — never status-polled, never pinned, never registered.
        private const int NetCookMaxScannedTargets = 256;

        // Live `TableCooker.cookerType` value marking a cooker the CLIENT HAS SWITCHED OFF. Established
        // in-world 2026-09-02 (see docs/FEATURES.md for the full write-up):
        //  - The table file is fine: parsing cn.bytes row by row gives 370006→2, 370010→3, 370014→5,
        //    370019→6, matching the offline dump. The live client reports 999 for the contiguous id
        //    range 370006-370023, while 370001-5 → 1, 370024 → 11 … 370033 → 1 all read correctly.
        //  - Not a decode bug: the live object IS that row (370006's prefabPath is
        //    `…/p_cooker_season_sandshop_cooker_1`) and its cookwareType still reads 2 — a parse
        //    desync would corrupt the adjacent field too. Exactly one field is overwritten, on
        //    cookers only; TableCookingRecipe is untouched (live GetCookingRecipe still returns
        //    cookerType 2/3/5/6/15 for 45129/45216/45254/45274/45532).
        // So the menu buckets exist and are populated while their cookers point at an empty 999
        // bucket: GetAllRecipes returns 0 for every gated cooker and the game itself will not serve
        // their menu. Two things follow, both load-bearing:
        //  - 999 is a SHARED bucket across cookers that have nothing to do with each other, so
        //    grouping by it would merge a crucible, a grill and a food cart into one "type".
        //  - Re-deriving the real type from the offline table would NOT help — the recipe list still
        //    comes from the client's own bucket lookup.
        // Hence 999 is treated exactly like an unresolvable type: no census entry, no merge, no pin.
        private const int NetCookUnusableRecipeCookerType = 999;

        private sealed class NetCookCookerTypeGroup
        {
            public int RecipeCookerType;   // TableCooker.cookerType — the key GetAllRecipes groups by
            public int StaticId;           // dominant staticId in the group; drives the recipe cache
            public int CookwareType;       // that staticId's cookware type (the legacy netCookCookerType)
            public int Count;              // live burner targets the scan resolved
            public int ObservedCount;      // cook-build owners seen but never enumerated
            public float NearestDistance = -1f;
            public string DisplayName;
        }

        private sealed class NetCookObservedCooker
        {
            public int StaticId;
            public int CookwareType;
            public int Count;
            public float NearestDistance = -1f;
        }

        // ---- Picker state (all session-only) ----
        private readonly List<NetCookTargetContext> netCookScannedTargets = new List<NetCookTargetContext>(64);
        private readonly HashSet<string> netCookScannedTargetKeys = new HashSet<string>();
        private readonly Dictionary<uint, NetCookObservedCooker> netCookObservedCookers = new Dictionary<uint, NetCookObservedCooker>(16);
        private readonly List<NetCookCookerTypeGroup> netCookScannedCookerTypes = new List<NetCookCookerTypeGroup>(8);
        // Bumped on every census rebuild; the UGUI rows rebind only when it moves (no per-frame work).
        private int netCookScannedCookerTypesVersion = 0;
        private int netCookPreferredCookerType = 0;
        private bool netCookCookerTypeDropdownOpen = false;
        // Set for the duration of one scan when nothing it resolved belongs to the pinned type (the
        // player walked away from that kitchen). Without it the pick would prune every candidate and
        // the capture would fail outright; instead the scan behaves as if no pick existed and the
        // census rebuild afterwards clears the pick with an explanation.
        private bool netCookPinnedCookerTypeSuppressed = false;

        // ---- TableCooker.cookerType / TableEntity.name over AuraMono ----
        private readonly Dictionary<int, int> netCookRecipeCookerTypeCache = new Dictionary<int, int>(32);
        private readonly HashSet<int> netCookRecipeCookerTypeFailedStaticIds = new HashSet<int>();
        private readonly Dictionary<int, string> netCookCookerNameCache = new Dictionary<int, string>(32);
        // recipeCookerType -> the recipe last selected for that MENU. Switching type used to reset the
        // selection to the first entry of the new list, which is not necessarily cookable: field log
        // showed the clay-stove menu defaulting to 45519 and the start dying on "Missing ingredients
        // for Prickly Pear & Apple Juice" every single time the type was picked, forcing a manual
        // re-pick of 45524. Remembering per menu makes a round trip (menu A -> B -> A) restore what
        // the user actually chose.
        private readonly Dictionary<int, int> netCookRecipeByCookerType = new Dictionary<int, int>(8);
        private IntPtr netCookTableDataAuraClass = IntPtr.Zero;
        private IntPtr netCookGetCookerAuraMethod = IntPtr.Zero;
        private IntPtr netCookGetEntityAuraMethod = IntPtr.Zero;
        private bool netCookTableDataAuraResolveTried = false;
        private bool netCookRecipeCookerTypeUnavailableLogged = false;

        // ----------------------------------------------------------------------------------------
        // Pin accessor
        // ----------------------------------------------------------------------------------------

        // Mini Game Assist only relieves danger and collects finished food — cooker-type agnostic by
        // design (IsCompatibleNetCookCooker returns true for everything there), so the pick must not
        // narrow that mode.
        private int GetNetCookPinnedCookerType()
        {
            return (this.netCookMiniGameOnly || this.netCookPinnedCookerTypeSuppressed)
                ? 0
                : this.netCookPreferredCookerType;
        }

        // Called once per scan, right after the resolve loop: decides whether the pick can apply at
        // all to what this scan found. Never mutates the pick itself — that is ValidateNetCook…'s job.
        private void EvaluateNetCookPinnedCookerTypeSuppression(List<NetCookTargetContext> targets)
        {
            this.netCookPinnedCookerTypeSuppressed = false;
            int pinned = this.GetNetCookPinnedCookerType();
            if (pinned <= 0 || targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (this.NetCookTargetMatchesPinnedCookerType(targets[i]))
                {
                    return;
                }
            }

            this.netCookPinnedCookerTypeSuppressed = true;
            this.NetCookLog("Stove-type pick " + pinned + " suppressed for this scan: none of the "
                + targets.Count + " resolved cooker(s) belong to it.");
        }

        private bool NetCookTargetMatchesPinnedCookerType(NetCookTargetContext target)
        {
            int pinned = this.GetNetCookPinnedCookerType();
            if (pinned <= 0)
            {
                return true;
            }

            if (target == null || target.CookerStaticId <= 0)
            {
                return false;
            }

            return this.TryGetNetCookRecipeCookerTypeCached(target.CookerStaticId, out int recipeCookerType)
                && recipeCookerType == pinned;
        }

        // Cache-ONLY lookup. The compatibility predicate runs inside AuraMono enumeration walks that
        // hold pinned collection pointers; a nested mono_runtime_invoke from there is exactly the
        // class of thing that AVs on this sgen build, so the invoking resolver
        // (TryGetNetCookRecipeCookerType) is confined to top-level code and this variant is what the
        // hot paths use. PrimeNetCookRecipeCookerTypes warms it from safe points.
        private bool TryGetNetCookRecipeCookerTypeCached(int cookerStaticId, out int recipeCookerType)
        {
            recipeCookerType = 0;
            return cookerStaticId > 0
                && this.netCookRecipeCookerTypeCache.TryGetValue(cookerStaticId, out recipeCookerType)
                && recipeCookerType > 0;
        }

        // Resolve (and cache) the recipe cooker type for everything in a freshly scanned set. MUST be
        // called from top-level scan code only — never from inside an AuraMono walk.
        private void PrimeNetCookRecipeCookerTypes(List<NetCookTargetContext> targets)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                NetCookTargetContext target = targets[i];
                if (target != null && target.CookerStaticId > 0)
                {
                    this.TryGetNetCookRecipeCookerType(target.CookerStaticId, out _);
                }
            }
        }

        // Same, for the two session registries — they feed candidates the live scan never resolves.
        private void PrimeNetCookRecipeCookerTypesFromRegistries()
        {
            foreach (NetCookTargetContext registeredTarget in this.netCookRegisteredTargets.Values)
            {
                if (registeredTarget != null && registeredTarget.CookerStaticId > 0)
                {
                    this.TryGetNetCookRecipeCookerType(registeredTarget.CookerStaticId, out _);
                }
            }

            foreach (NetCookRegisteredWorldCooker registeredCooker in this.netCookRegisteredWorldCookers.Values)
            {
                if (registeredCooker != null && registeredCooker.StaticId > 0)
                {
                    this.TryGetNetCookRecipeCookerType(registeredCooker.StaticId, out _);
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Snapshot
        // ----------------------------------------------------------------------------------------

        private void ClearNetCookScanSnapshot()
        {
            this.netCookScannedTargets.Clear();
            this.netCookScannedTargetKeys.Clear();
            this.netCookObservedCookers.Clear();
        }

        private void ClearNetCookCookerTypeCensus()
        {
            this.ClearNetCookScanSnapshot();
            if (this.netCookScannedCookerTypes.Count > 0)
            {
                this.netCookScannedCookerTypes.Clear();
            }
            this.netCookScannedCookerTypesVersion++;
        }

        // Called wherever the scan has resolved candidates but has not yet applied the desired-type
        // prune. Copies only — the snapshot must never alias live targets (they carry per-run phase
        // state the cook loop mutates).
        private void SnapshotNetCookScannedTargets(List<NetCookTargetContext> source)
        {
            if (source == null || source.Count <= 0)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (!this.SnapshotNetCookScannedTarget(source[i]))
                {
                    return; // cap reached
                }
            }
        }

        // Returns false only when the snapshot is full (so list callers can stop early).
        private bool SnapshotNetCookScannedTarget(NetCookTargetContext target)
        {
            try
            {
                if (target == null
                    || target.CookerNetId == 0U
                    || target.LevelObjectNetId == 0UL
                    || target.CookerStaticId <= 0)
                {
                    return true;
                }

                if (this.netCookScannedTargets.Count >= NetCookMaxScannedTargets)
                {
                    return false;
                }

                string key = target.CookerNetId + ":" + target.LevelObjectNetId;
                if (!this.netCookScannedTargetKeys.Add(key))
                {
                    return true;
                }

                this.netCookScannedTargets.Add(this.CloneNetCookTargetContext(target));
                return true;
            }
            catch (Exception ex)
            {
                this.NetCookLog("Stove-type snapshot failed: " + ex.Message);
                return true;
            }
        }

        // A cook-build owner the expansion rejected on type before enumerating its burners: we know
        // the kind but not the individual stoves. Recorded so the picker still lists that type (and
        // switching to it falls back to a real capture).
        private void NoteNetCookObservedCooker(uint ownerNetId, int cookerStaticId, int cookwareType, Vector3 position, bool hasPosition)
        {
            if (cookerStaticId <= 0)
            {
                return;
            }

            try
            {
                float distance = -1f;
                if (hasPosition && position != Vector3.zero && this.TryGetNetCookScanOrigin(out Vector3 origin, out _))
                {
                    distance = Vector3.Distance(origin, position);
                }

                if (!this.netCookObservedCookers.TryGetValue(ownerNetId, out NetCookObservedCooker observed) || observed == null)
                {
                    observed = new NetCookObservedCooker { StaticId = cookerStaticId, CookwareType = cookwareType, Count = 1, NearestDistance = distance };
                    this.netCookObservedCookers[ownerNetId] = observed;
                    return;
                }

                observed.StaticId = cookerStaticId;
                observed.CookwareType = cookwareType > 0 ? cookwareType : observed.CookwareType;
                if (distance >= 0f && (observed.NearestDistance < 0f || distance < observed.NearestDistance))
                {
                    observed.NearestDistance = distance;
                }
            }
            catch (Exception ex)
            {
                this.NetCookLog("Stove-type observation failed: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Census
        // ----------------------------------------------------------------------------------------

        // skipRangeCull mirrors the working set's own exception: a Remember-Stoves restart keeps the
        // remembered stoves at any distance, so the census must not cull them either.
        private void RebuildNetCookCookerTypeCensus(bool skipRangeCull)
        {
            try
            {
                this.netCookScannedCookerTypes.Clear();

                List<NetCookTargetContext> scratch = new List<NetCookTargetContext>(this.netCookScannedTargets.Count);
                for (int i = 0; i < this.netCookScannedTargets.Count; i++)
                {
                    NetCookTargetContext copy = this.CloneNetCookTargetContext(this.netCookScannedTargets[i]);
                    if (copy != null)
                    {
                        scratch.Add(copy);
                    }
                }

                if (!skipRangeCull)
                {
                    this.RemoveOutOfRangeNetCookTargets(scratch, null, null);
                }

                if (this.netCookCaptureOwnOnly)
                {
                    string ownStatus = null;
                    this.ApplyNetCookCaptureOwnFilter(scratch, ref ownStatus);
                }

                bool hasOrigin = this.TryGetNetCookScanOrigin(out Vector3 scanOrigin, out _);
                Dictionary<int, NetCookCookerTypeGroup> groups = new Dictionary<int, NetCookCookerTypeGroup>(8);
                Dictionary<int, Dictionary<int, int>> staticIdVotes = new Dictionary<int, Dictionary<int, int>>(8);
                int unresolved = 0;

                for (int i = 0; i < scratch.Count; i++)
                {
                    NetCookTargetContext target = scratch[i];
                    if (target == null || target.CookerStaticId <= 0)
                    {
                        continue;
                    }

                    if (!this.TryGetNetCookRecipeCookerType(target.CookerStaticId, out int recipeCookerType) || recipeCookerType <= 0)
                    {
                        unresolved++;
                        continue;
                    }

                    NetCookCookerTypeGroup group = this.GetOrCreateNetCookCookerTypeGroup(groups, recipeCookerType);
                    group.Count++;
                    if (target.CookerType > 0 && group.CookwareType <= 0)
                    {
                        group.CookwareType = target.CookerType;
                    }

                    if (hasOrigin && target.HasWorldPosition && target.WorldPosition != Vector3.zero)
                    {
                        float distance = Vector3.Distance(scanOrigin, target.WorldPosition);
                        if (group.NearestDistance < 0f || distance < group.NearestDistance)
                        {
                            group.NearestDistance = distance;
                        }
                    }

                    this.VoteNetCookGroupStaticId(staticIdVotes, recipeCookerType, target.CookerStaticId);
                }

                foreach (NetCookObservedCooker observed in this.netCookObservedCookers.Values)
                {
                    if (observed == null || observed.StaticId <= 0)
                    {
                        continue;
                    }

                    if (!this.TryGetNetCookRecipeCookerType(observed.StaticId, out int recipeCookerType) || recipeCookerType <= 0)
                    {
                        unresolved++;
                        continue;
                    }

                    NetCookCookerTypeGroup group = this.GetOrCreateNetCookCookerTypeGroup(groups, recipeCookerType);
                    // ObservedCount is a LOWER BOUND for kinds whose burners were never enumerated.
                    // Once the snapshot holds live targets for this menu the observation adds nothing
                    // but double counting: with a pick active the deferred expansion rejects every
                    // stove of the other menu and would report them a second time (field log:
                    // `live=54 observed=54` for one and the same 54 stoves).
                    if (group.Count <= 0)
                    {
                        group.ObservedCount += Math.Max(1, observed.Count);
                    }
                    if (observed.CookwareType > 0 && group.CookwareType <= 0)
                    {
                        group.CookwareType = observed.CookwareType;
                    }
                    if (observed.NearestDistance >= 0f && (group.NearestDistance < 0f || observed.NearestDistance < group.NearestDistance))
                    {
                        group.NearestDistance = observed.NearestDistance;
                    }

                    this.VoteNetCookGroupStaticId(staticIdVotes, recipeCookerType, observed.StaticId);
                }

                foreach (NetCookCookerTypeGroup group in groups.Values)
                {
                    group.StaticId = this.ResolveNetCookGroupStaticId(staticIdVotes, group.RecipeCookerType);
                    group.DisplayName = this.GetNetCookCookerDisplayName(group.StaticId, group.RecipeCookerType);
                    this.netCookScannedCookerTypes.Add(group);
                }

                this.netCookScannedCookerTypes.Sort((a, b) =>
                {
                    int byCount = (b.Count + b.ObservedCount).CompareTo(a.Count + a.ObservedCount);
                    if (byCount != 0)
                    {
                        return byCount;
                    }

                    float distanceA = a.NearestDistance < 0f ? float.MaxValue : a.NearestDistance;
                    float distanceB = b.NearestDistance < 0f ? float.MaxValue : b.NearestDistance;
                    int byDistance = distanceA.CompareTo(distanceB);
                    if (byDistance != 0)
                    {
                        return byDistance;
                    }

                    return a.RecipeCookerType.CompareTo(b.RecipeCookerType);
                });

                this.netCookScannedCookerTypesVersion++;
                this.NetCookLog("Stove-type census: " + this.FormatNetCookCookerTypeCensus()
                    + (unresolved > 0 ? " unresolvedRecipeType=" + unresolved : string.Empty)
                    + " pinned=" + this.netCookPreferredCookerType
                    + " snapshot=" + this.netCookScannedTargets.Count
                    + " observed=" + this.netCookObservedCookers.Count + ".");
            }
            catch (Exception ex)
            {
                this.netCookScannedCookerTypes.Clear();
                this.netCookScannedCookerTypesVersion++;
                this.NetCookLog("Stove-type census failed: " + ex);
            }
        }

        private NetCookCookerTypeGroup GetOrCreateNetCookCookerTypeGroup(Dictionary<int, NetCookCookerTypeGroup> groups, int recipeCookerType)
        {
            if (!groups.TryGetValue(recipeCookerType, out NetCookCookerTypeGroup group) || group == null)
            {
                group = new NetCookCookerTypeGroup { RecipeCookerType = recipeCookerType };
                groups[recipeCookerType] = group;
            }

            return group;
        }

        private void VoteNetCookGroupStaticId(Dictionary<int, Dictionary<int, int>> votes, int recipeCookerType, int staticId)
        {
            if (staticId <= 0)
            {
                return;
            }

            if (!votes.TryGetValue(recipeCookerType, out Dictionary<int, int> byStaticId) || byStaticId == null)
            {
                byStaticId = new Dictionary<int, int>(4);
                votes[recipeCookerType] = byStaticId;
            }

            byStaticId.TryGetValue(staticId, out int count);
            byStaticId[staticId] = count + 1;
        }

        private int ResolveNetCookGroupStaticId(Dictionary<int, Dictionary<int, int>> votes, int recipeCookerType)
        {
            if (!votes.TryGetValue(recipeCookerType, out Dictionary<int, int> byStaticId) || byStaticId == null)
            {
                return 0;
            }

            int bestStaticId = 0;
            int bestCount = -1;
            foreach (KeyValuePair<int, int> entry in byStaticId)
            {
                // Ties resolve toward the currently captured cooker so switching back to the type we
                // are already on cannot silently swap the recipe cache to a sibling staticId.
                if (entry.Value > bestCount
                    || (entry.Value == bestCount && entry.Key == this.netCookCookerStaticId))
                {
                    bestStaticId = entry.Key;
                    bestCount = entry.Value;
                }
            }

            return bestStaticId;
        }

        private string FormatNetCookCookerTypeCensus()
        {
            if (this.netCookScannedCookerTypes.Count <= 0)
            {
                return "<none>";
            }

            List<string> parts = new List<string>(this.netCookScannedCookerTypes.Count);
            for (int i = 0; i < this.netCookScannedCookerTypes.Count; i++)
            {
                NetCookCookerTypeGroup group = this.netCookScannedCookerTypes[i];
                parts.Add("type=" + group.RecipeCookerType
                    + " static=" + group.StaticId
                    + " cookware=" + group.CookwareType
                    + " live=" + group.Count
                    + " observed=" + group.ObservedCount
                    + " nearest=" + (group.NearestDistance >= 0f ? group.NearestDistance.ToString("F1") + "m" : "?")
                    + " name=" + (group.DisplayName ?? "?"));
            }

            return string.Join(" | ", parts.ToArray());
        }

        private bool NetCookCensusHasCookerType(int recipeCookerType)
        {
            if (recipeCookerType <= 0)
            {
                return false;
            }

            for (int i = 0; i < this.netCookScannedCookerTypes.Count; i++)
            {
                if (this.netCookScannedCookerTypes[i].RecipeCookerType == recipeCookerType)
                {
                    return true;
                }
            }

            return false;
        }

        // A pick that the latest census no longer contains would prune every target on the next
        // capture. Clear it explicitly (never silently inside the vote) so the status line can say so.
        private void ValidateNetCookPreferredCookerType()
        {
            int pinned = this.netCookPreferredCookerType;
            if (pinned <= 0 || this.netCookScannedCookerTypes.Count <= 0 || this.NetCookCensusHasCookerType(pinned))
            {
                return;
            }

            this.netCookPreferredCookerType = 0;
            this.netCookPinnedCookerTypeSuppressed = false;
            string fallback = this.netCookScannedCookerTypes.Count > 0
                ? this.GetNetCookCookerTypeGroupLabel(this.netCookScannedCookerTypes[0])
                : "auto";
            this.netCookStatus = "Stove type " + pinned + " is no longer nearby; switched to " + fallback + ".";
            this.NetCookLog("Stove-type pick " + pinned + " cleared: not present in the new census (" + this.FormatNetCookCookerTypeCensus() + ").");
        }

        // ----------------------------------------------------------------------------------------
        // TableCooker.cookerType / TableEntity.name over AuraMono
        // ----------------------------------------------------------------------------------------

        private void EnsureNetCookTableDataAuraMethods()
        {
            if (this.netCookTableDataAuraResolveTried)
            {
                return;
            }

            this.netCookTableDataAuraResolveTried = true;
            try
            {
                IntPtr tableDataClass = this.FindAuraMonoClassByFullName("TableData");
                if (tableDataClass == IntPtr.Zero)
                {
                    tableDataClass = this.FindAuraMonoClassInImages(string.Empty, "TableData", new[] { "EcsClient", "EcsClient.dll" });
                }

                if (tableDataClass == IntPtr.Zero)
                {
                    this.netCookTableDataAuraResolveTried = false; // image may not be loaded yet — retry later
                    return;
                }

                this.netCookTableDataAuraClass = tableDataClass;
                // TableData.GetCooker(int id, bool needException = false) -> TableCooker
                this.netCookGetCookerAuraMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetCooker", 2);
                // TableData.GetEntity(int id, bool needException = false) -> TableEntity (.name)
                this.netCookGetEntityAuraMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntity", 2);
                this.NetCookLog("Stove-type TableData resolved getCooker=" + (this.netCookGetCookerAuraMethod != IntPtr.Zero)
                    + " getEntity=" + (this.netCookGetEntityAuraMethod != IntPtr.Zero) + ".");
            }
            catch (Exception ex)
            {
                this.NetCookLog("Stove-type TableData resolve failed: " + ex.Message);
            }
        }

        // TableCooker.cookerType — the value CookingSystem.GetAllRecipes groups its recipe lists by.
        private unsafe bool TryGetNetCookRecipeCookerType(int cookerStaticId, out int recipeCookerType)
        {
            recipeCookerType = 0;
            if (cookerStaticId <= 0)
            {
                return false;
            }

            if (this.netCookRecipeCookerTypeCache.TryGetValue(cookerStaticId, out recipeCookerType))
            {
                return recipeCookerType > 0;
            }

            if (this.netCookRecipeCookerTypeFailedStaticIds.Contains(cookerStaticId))
            {
                return false;
            }

            try
            {
                this.EnsureNetCookTableDataAuraMethods();
                if (this.netCookGetCookerAuraMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    if (!this.netCookRecipeCookerTypeUnavailableLogged)
                    {
                        this.netCookRecipeCookerTypeUnavailableLogged = true;
                        this.NetCookLog("Stove-type picker disabled: TableData.GetCooker unavailable over AuraMono.");
                    }
                    return false;
                }

                int id = cookerStaticId;
                byte needException = 0;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                IntPtr exc = IntPtr.Zero;
                IntPtr cookerObj = auraMonoRuntimeInvoke(this.netCookGetCookerAuraMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || cookerObj == IntPtr.Zero)
                {
                    this.netCookRecipeCookerTypeFailedStaticIds.Add(cookerStaticId);
                    return false;
                }

                // Pin across the getter invoke: this sgen build moves objects and the getter call can
                // trigger a collection while cookerObj is only a raw pointer (AuraMono house rule).
                uint pin = AuraMonoPinNew(cookerObj);
                try
                {
                    // The property getter returns int (TableCooker.cookerType => _cookerType, a ushort
                    // FIELD — reading that field raw would over-read 4 bytes, so go through the getter).
                    if (!this.TryInvokeAuraMonoZeroArgInt(cookerObj, out recipeCookerType, "get_cookerType")
                        || recipeCookerType <= 0
                        || recipeCookerType == NetCookUnusableRecipeCookerType)
                    {
                        this.NetCookLog("Stove-type staticId=" + cookerStaticId + " has no usable menu (cookerType="
                            + recipeCookerType + "); excluded from grouping.");
                        this.netCookRecipeCookerTypeFailedStaticIds.Add(cookerStaticId);
                        recipeCookerType = 0;
                        return false;
                    }
                }
                finally
                {
                    AuraMonoPinFree(pin);
                }

                this.netCookRecipeCookerTypeCache[cookerStaticId] = recipeCookerType;
                this.NetCookLog("Stove-type staticId=" + cookerStaticId + " => recipeCookerType=" + recipeCookerType + ".");
                return true;
            }
            catch (Exception ex)
            {
                this.netCookRecipeCookerTypeFailedStaticIds.Add(cookerStaticId);
                recipeCookerType = 0;
                this.NetCookLog("Stove-type resolve exception for staticId=" + cookerStaticId + ": " + ex.Message);
                return false;
            }
        }

        private unsafe string GetNetCookCookerDisplayName(int cookerStaticId, int recipeCookerType)
        {
            string fallback = "Type " + recipeCookerType;
            if (cookerStaticId <= 0)
            {
                return fallback;
            }

            if (this.netCookCookerNameCache.TryGetValue(cookerStaticId, out string cached))
            {
                return string.IsNullOrWhiteSpace(cached) ? fallback : cached;
            }

            string resolved = null;
            try
            {
                this.EnsureNetCookTableDataAuraMethods();
                if (this.netCookGetEntityAuraMethod != IntPtr.Zero && auraMonoRuntimeInvoke != null)
                {
                    int id = cookerStaticId;
                    byte needException = 0;
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)(&id);
                    args[1] = (IntPtr)(&needException);
                    IntPtr exc = IntPtr.Zero;
                    IntPtr entityObj = auraMonoRuntimeInvoke(this.netCookGetEntityAuraMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc == IntPtr.Zero && entityObj != IntPtr.Zero)
                    {
                        uint pin = AuraMonoPinNew(entityObj);
                        try
                        {
                            if (this.TryGetMonoObjectMember(entityObj, "name", out IntPtr strObj) && strObj != IntPtr.Zero)
                            {
                                this.TryReadMonoString(strObj, out resolved);
                            }
                        }
                        finally
                        {
                            AuraMonoPinFree(pin);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.NetCookLog("Stove-type name resolve failed for staticId=" + cookerStaticId + ": " + ex.Message);
            }

            if (resolved != null)
            {
                resolved = resolved.Trim();
            }

            this.netCookCookerNameCache[cookerStaticId] = resolved ?? string.Empty;
            return string.IsNullOrWhiteSpace(resolved) ? fallback : resolved;
        }

        // ----------------------------------------------------------------------------------------
        // Labels (UI + status strings)
        // ----------------------------------------------------------------------------------------

        private string GetNetCookCookerTypeGroupLabel(NetCookCookerTypeGroup group)
        {
            if (group == null)
            {
                return "Auto";
            }

            string name = string.IsNullOrWhiteSpace(group.DisplayName) ? ("Type " + group.RecipeCookerType) : group.DisplayName;
            if (group.Count > 0)
            {
                return name + " x" + group.Count;
            }

            // Observed-only: the expansion saw the cook build but never enumerated its burners, so
            // the count is a lower bound — say so rather than pretending it is exact.
            return group.ObservedCount > 0 ? (name + " x~" + group.ObservedCount) : name;
        }

        private NetCookCookerTypeGroup GetNetCookCookerTypeGroup(int recipeCookerType)
        {
            if (recipeCookerType <= 0)
            {
                return null;
            }

            for (int i = 0; i < this.netCookScannedCookerTypes.Count; i++)
            {
                if (this.netCookScannedCookerTypes[i].RecipeCookerType == recipeCookerType)
                {
                    return this.netCookScannedCookerTypes[i];
                }
            }

            return null;
        }

        // The recipe cooker type the capture actually settled on (what the recipe list belongs to),
        // regardless of whether the user pinned it or the majority vote picked it.
        private int GetNetCookActiveRecipeCookerType()
        {
            // Display/label path: the RAW pick, not the scan-suppressed one — the header must agree
            // with the highlighted row until Validate actually clears the pick.
            int pinned = this.netCookMiniGameOnly ? 0 : this.netCookPreferredCookerType;
            if (pinned > 0)
            {
                return pinned;
            }

            if (this.netCookCookerStaticId > 0
                && this.TryGetNetCookRecipeCookerType(this.netCookCookerStaticId, out int recipeCookerType))
            {
                return recipeCookerType;
            }

            return 0;
        }

        // Call whenever netCookRecipeId holds a deliberate choice: an explicit pick, or the outgoing
        // selection just before a type switch.
        private void RememberNetCookRecipeForActiveMenu()
        {
            int menu = this.GetNetCookActiveRecipeCookerType();
            if (menu > 0 && this.netCookRecipeId > 0)
            {
                this.netCookRecipeByCookerType[menu] = this.netCookRecipeId;
            }
        }

        private bool TryGetRememberedNetCookRecipe(int recipeCookerType, out int recipeId)
        {
            recipeId = 0;
            return recipeCookerType > 0
                && this.netCookRecipeByCookerType.TryGetValue(recipeCookerType, out recipeId)
                && recipeId > 0;
        }

        private string GetNetCookSelectedCookerTypeLabel()
        {
            int active = this.GetNetCookActiveRecipeCookerType();
            NetCookCookerTypeGroup group = this.GetNetCookCookerTypeGroup(active);
            if (this.netCookPreferredCookerType > 0)
            {
                return group != null ? this.GetNetCookCookerTypeGroupLabel(group) : ("Type " + this.netCookPreferredCookerType);
            }

            return group != null
                ? ("Auto (" + this.GetNetCookCookerTypeGroupLabel(group) + ")")
                : "Auto";
        }

        // The picker is dead weight with a single kind in range — and meaningless in Mini Game Only,
        // where every type is assisted as one set.
        //
        // Exception: while a type is PINNED it stays visible even at one census entry. Hiding it
        // there strands the pin — the header is the only place the narrowing is visible and the Auto
        // row the only way back, so a census that shrinks to one kind (walked away, or a re-capture
        // that found less) would otherwise silently keep filtering with no control to undo it.
        private bool ShouldShowNetCookCookerTypePicker()
        {
            if (this.netCookMiniGameOnly || this.netCookScannedCookerTypes.Count <= 0)
            {
                return false;
            }

            return this.netCookScannedCookerTypes.Count >= 2 || this.netCookPreferredCookerType > 0;
        }

        // ----------------------------------------------------------------------------------------
        // Applying a pick
        // ----------------------------------------------------------------------------------------

        private int GetDominantNetCookRecipeCookerType(List<NetCookTargetContext> targets)
        {
            if (targets == null || targets.Count <= 0)
            {
                return 0;
            }

            Dictionary<int, int> counts = new Dictionary<int, int>(8);
            int bestType = 0;
            int bestCount = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                NetCookTargetContext target = targets[i];
                if (target == null || target.CookerStaticId <= 0)
                {
                    continue;
                }

                if (!this.TryGetNetCookRecipeCookerType(target.CookerStaticId, out int recipeCookerType) || recipeCookerType <= 0)
                {
                    continue;
                }

                counts.TryGetValue(recipeCookerType, out int count);
                count++;
                counts[recipeCookerType] = count;
                if (count > bestCount || (count == bestCount && recipeCookerType == this.GetNetCookActiveRecipeCookerType()))
                {
                    bestType = recipeCookerType;
                    bestCount = count;
                }
            }

            return bestType;
        }

        // recipeCookerType: 0 = Auto (majority over the snapshot).
        private bool ApplyNetCookPreferredCookerType(int recipeCookerType, out string status)
        {
            status = string.Empty;
            if (recipeCookerType < 0)
            {
                recipeCookerType = 0;
            }

            if (this.netCookEnabled)
            {
                // A mid-run retarget would leave the abandoned stoves cooking with nobody collecting,
                // and EnsureNetCookRecipeCache is frozen while a run is active.
                status = "Stop Mass Cook before changing the stove type.";
                this.netCookStatus = status;
                return false;
            }

            if (this.netCookMiniGameOnly)
            {
                status = "Mini Game Only assists every stove type — no type to pick.";
                this.netCookStatus = status;
                return false;
            }

            // Save the outgoing menu's selection before the context moves, so coming back restores it.
            this.RememberNetCookRecipeForActiveMenu();

            int previous = this.netCookPreferredCookerType;
            this.netCookPreferredCookerType = recipeCookerType;
            // The pick came out of the current census, so whatever an earlier scan decided about a
            // stale pin no longer applies.
            this.netCookPinnedCookerTypeSuppressed = false;
            this.netCookCookerTypeDropdownOpen = false;

            int wanted = recipeCookerType > 0 ? recipeCookerType : this.GetDominantNetCookRecipeCookerType(this.netCookScannedTargets);
            List<NetCookTargetContext> rebuilt = new List<NetCookTargetContext>(this.netCookScannedTargets.Count);
            for (int i = 0; i < this.netCookScannedTargets.Count; i++)
            {
                NetCookTargetContext target = this.netCookScannedTargets[i];
                if (target == null
                    || target.CookerNetId == 0U
                    || target.LevelObjectNetId == 0UL
                    || target.CookerStaticId <= 0)
                {
                    continue;
                }

                if (wanted > 0)
                {
                    if (!this.TryGetNetCookRecipeCookerType(target.CookerStaticId, out int targetType) || targetType != wanted)
                    {
                        continue;
                    }
                }

                rebuilt.Add(this.CloneNetCookTargetContext(target));
            }

            if (rebuilt.Count <= 0)
            {
                // The snapshot has nothing live for this type (an expansion-only observation, or it
                // went stale). Run a real capture with the pick applied rather than leaving mass cook
                // with zero stoves — a deliberate pick is not capture spam, so drop the cooldown.
                this.NetCookLog("Stove-type pick " + recipeCookerType + ": snapshot has no live target, falling back to a full capture.");
                this.nextNetCookCaptureAllowedAt = 0f;
                if (this.TryCaptureNetCookFromCurrentTarget())
                {
                    status = this.netCookStatus;
                    return true;
                }

                this.netCookPreferredCookerType = previous;
                status = string.IsNullOrWhiteSpace(this.netCookStatus) ? "No stoves of that type nearby." : this.netCookStatus;
                this.NetCookLog("Stove-type pick " + recipeCookerType + " reverted to " + previous + ": " + status);
                return false;
            }

            try
            {
                if (this.netCookStatusDiagEnabled)
                {
                    this.NetCookDiagLog("stove-type switch clearing targets=" + this.netCookTargets.Count);
                }

                this.netCookTargets.Clear();
                this.LogNetCookStatusCacheClear("stove-type-switch", this.netCookStatusCache.Count);
                this.netCookStatusCache.Clear();
                this.netCookStatusByLevelObject.Clear();
                this.netCookTargets.AddRange(rebuilt);
                this.RemoveNetCookDuplicateLevelObjectTargets(this.netCookTargets);
                this.SortNetCookTargetsByDistanceFromScanOrigin(this.netCookTargets);
                this.TrimNetCookTargetsToClosest(this.netCookTargets, "stove(s)");
                this.RegisterNetCookTargets(this.netCookTargets);
                this.ApplyNetCookTargetContext(this.netCookTargets[0]);
                this.netCookSentCount = 0;
                if (this.netCookCookerStaticId > 0)
                {
                    this.netCookLastCapturedCookerStaticId = this.netCookCookerStaticId;
                }
                if (this.netCookCookerType > 0)
                {
                    this.netCookLastCapturedCookerType = this.netCookCookerType;
                }

                // Recipes follow the cooker: GetAllRecipes(staticId) maps staticId -> cookerType -> list.
                // Clear the search FIRST so the keep-or-default decision below sees the whole list.
                this.netCookRecipeSearchText = string.Empty;
                this.netCookRecipeScrollPos = Vector2.zero;
                this.netCookRecipeDropdownOpen = false;
                this.InvalidateNetCookRecipeCache();
                bool recipeCacheReady = this.EnsureNetCookRecipeCache();

                // Recipe for the incoming menu, in order of preference: what the user last chose FOR
                // THAT MENU, then the current selection if it happens to be in the new list, then the
                // list's first entry. The first rung is what keeps a menu round trip from silently
                // handing back an uncookable default.
                int previousRecipeId = this.netCookRecipeId;
                this.TryGetRememberedNetCookRecipe(wanted, out int rememberedRecipeId);
                int chosenRecipeId = 0;
                bool chosenFromMemory = false;
                {
                    // GetVisibleNetCookRecipeEntries returns a REUSED list — read it immediately.
                    List<KeyValuePair<int, string>> visible = this.GetVisibleNetCookRecipeEntries();
                    for (int i = 0; i < visible.Count; i++)
                    {
                        int key = visible[i].Key;
                        if (rememberedRecipeId > 0 && key == rememberedRecipeId)
                        {
                            chosenRecipeId = key;
                            chosenFromMemory = true;
                            break;
                        }
                        if (previousRecipeId > 0 && key == previousRecipeId)
                        {
                            chosenRecipeId = key; // keep looking — a remembered match still wins
                        }
                    }
                }

                bool keptRecipe = chosenRecipeId > 0;
                if (keptRecipe)
                {
                    this.netCookRecipeId = chosenRecipeId;
                }
                else
                {
                    this.netCookRecipeId = 0;
                    this.TrySelectDefaultNetCookRecipeForCooker();
                }
                this.RememberNetCookRecipeForActiveMenu();

                this.netCookCookQuantity = 1;
                this.netCookCookQuantityInput = "1";
                this.nextNetCookMaxRefreshAt = 0f;
                this.SyncNetCookCaptureDebugEsp();

                NetCookCookerTypeGroup group = this.GetNetCookCookerTypeGroup(wanted);
                string label = group != null ? this.GetNetCookCookerTypeGroupLabel(group) : ("Type " + wanted);
                status = "Stove type: " + label + " — " + this.netCookTargets.Count + " stove(s).";
                this.netCookStatus = status;
                this.NetCookLog("Stove-type pick=" + recipeCookerType + " resolved=" + wanted
                    + " targets=" + this.netCookTargets.Count
                    + " cookerStaticId=" + this.netCookCookerStaticId
                    + " cookware=" + this.netCookCookerType
                    + " recipeCacheReady=" + recipeCacheReady
                    + " recipe=" + this.netCookRecipeId
                    + " keptRecipe=" + keptRecipe
                    + " fromMemory=" + chosenFromMemory
                    + " remembered=" + rememberedRecipeId + ".");
                this.LogNetCookTargetSummary(this.netCookTargets);

                // Same broadening pass a real capture kicks — the fast snapshot only holds what the
                // first pass resolved, the expansion finds the rest of this type.
                bool forceBroad = this.ShouldForceNetCookDeferredBroadRefresh(out string deferredReason);
                this.NetCookLog("Stove-type deferred refresh forceBroad=" + forceBroad + " reason=" + deferredReason + ".");
                this.StartNetCookDeferredOwnerWindowExpansion(this.netCookCookerStaticId, this.netCookCookerType, forceBroad);
                return true;
            }
            catch (Exception ex)
            {
                this.netCookPreferredCookerType = previous;
                status = "Stove type switch failed: " + ex.Message;
                this.netCookStatus = status;
                this.NetCookLog("Stove-type switch exception: " + ex);
                return false;
            }
        }
    }
}
