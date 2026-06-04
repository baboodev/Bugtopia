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
        private const bool WildAnimalFeedLogsEnabled = MasterLogWildAnimalFeed;
        private const int WildAnimalFeedMaxDetailLogsPerPlan = 12;
        private const int WildAnimalFeedFullnessCacheMiss = -1;
        private const float WildAnimalFeedActionCooldownSeconds = 1.5f;
        private const int WildAnimalFeedMaxItemsPerCommand = 100;
        private const float WildAnimalFeedMinEmptyRatio = 0.02f;
        private const int WildAnimalFeedShopEggStaticId = 46102;
        private static readonly string[] WildAnimalFeedStorageNames = { "Backpack", "Warehouse" };
        private static readonly HashSet<int> WildAnimalFeedEggStaticIds = new HashSet<int>
        {
            WildAnimalFeedShopEggStaticId
        };

        private bool wildAnimalFeedPreferFavorites = true;
        private bool wildAnimalFeedSkipFiveStarFood = true;
        private bool wildAnimalFeedSkipEgg = true;
        private object wildAnimalFeedCoroutine = null;
        private float wildAnimalFeedBusyUntil = 0f;
        private string wildAnimalFeedLastStatus = "Idle.";
        private int wildAnimalFeedDetailLogBudget;
        private bool wildAnimalFeedAuraStorageValuesResolved;
        private readonly Dictionary<string, int> wildAnimalFeedAuraStorageByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Backpack", 1 },
            { "Warehouse", 2 }
        };
        private readonly Dictionary<long, int> wildAnimalFeedFullnessByKeyCache = new Dictionary<long, int>();
        private readonly Dictionary<int, HashSet<int>> wildAnimalFeedGroupIdsByStaticIdCache = new Dictionary<int, HashSet<int>>();

        private MethodInfo wildAnimalFeedProtocolFeedMethod = null;
        private Type wildAnimalFeedAnimalGroupType = null;
        private Type wildAnimalFeedStorageTypeType = null;
        private Type wildAnimalFeedWildAnimalSystemType = null;
        private PropertyInfo wildAnimalFeedWildAnimalSystemInstanceProperty = null;
        private MethodInfo wildAnimalFeedGetUnlockedAnimalsMethod = null;
        private MethodInfo wildAnimalFeedGetFullnessMethod = null;
        private MethodInfo wildAnimalFeedGetFeedTroughCapacityMethod = null;
        private MethodInfo wildAnimalFeedGetFavoriteFoodMethod = null;
        private MethodInfo wildAnimalFeedGetFoodsMethod = null;
        private bool wildAnimalFeedManagedReflectionUnavailable = false;
        private string wildAnimalFeedManagedReflectionUnavailableStatus = string.Empty;
        private IntPtr wildAnimalFeedAuraFeedMethod = IntPtr.Zero;
        private Type wildAnimalFeedBackPackSystemType = null;
        private MethodInfo wildAnimalFeedBackPackGetAllItemMethod = null;
        private MethodInfo wildAnimalFeedGetAnimalFoodThoughMethod = null;

        private sealed class WildAnimalFeedCollectStats
        {
            public int RawItems;
            public int Accepted;
            public int SkippedStar;
            public int SkippedEgg;
            public int SkippedFavorite;
            public int SkippedLock;
            public int SkippedInvalid;
        }

        private sealed class WildAnimalFeedFoodCandidate
        {
            public int StaticId;
            public uint NetId;
            public int Count;
            public int Fullness;
            public int BondExp;
            public bool IsFavorite;
            public bool IsLock;
            public int SortScore;
        }

        private sealed class WildAnimalFeedGroupPlan
        {
            public int GroupId;
            public string GroupName;
            public int CurrentFullness;
            public int MaxFullness;
            public int NeededFullness;
            public List<uint> FoodNetIds = new List<uint>();
        }

        private sealed class WildAnimalFeedAuraPlanContext
        {
            public IntPtr WildAnimalSystemObj;
            public IntPtr GetFullnessMethod;
            public IntPtr GetCapacityMethod;
            public IntPtr GetFavoriteFoodMethod;
            public IntPtr GetFoodsMethod;
            public List<IntPtr> GroupItems = new List<IntPtr>();
            public Dictionary<uint, int> ReservedCountsByNetId = new Dictionary<uint, int>();
            public int CheckedGroups;
            public int HungryGroups;
        }

        private void StopWildAnimalFeedCoroutine()
        {
            if (this.wildAnimalFeedCoroutine == null)
            {
                return;
            }

            try
            {
                ModCoroutines.Stop(this.wildAnimalFeedCoroutine);
            }
            catch
            {
            }

            this.wildAnimalFeedCoroutine = null;
            this.wildAnimalFeedBusyUntil = Time.realtimeSinceStartup + WildAnimalFeedActionCooldownSeconds;
        }

        private void StartWildAnimalFeedAll(bool silent)
        {
            if (this.wildAnimalFeedCoroutine != null)
            {
                if (!silent)
                {
                    this.AddMenuNotification("Wild trough feed already running", new Color(0.45f, 0.88f, 1f));
                }
                return;
            }

            if (Time.realtimeSinceStartup < this.wildAnimalFeedBusyUntil)
            {
                if (!silent)
                {
                    float remaining = Mathf.Max(0f, this.wildAnimalFeedBusyUntil - Time.realtimeSinceStartup);
                    this.AddMenuNotification("Wild trough feed: wait " + remaining.ToString("F1") + "s", new Color(0.45f, 0.88f, 1f));
                }
                return;
            }

            this.wildAnimalFeedBusyUntil = Time.realtimeSinceStartup + WildAnimalFeedActionCooldownSeconds;
            this.wildAnimalFeedLastStatus = "Planning wild trough feed...";
            this.wildAnimalFeedCoroutine = ModCoroutines.Start(this.WildAnimalFeedAllStartRoutine(silent));
        }

        private IEnumerator WildAnimalFeedAllStartRoutine(bool silent)
        {
            yield return null;

            List<WildAnimalFeedGroupPlan> plans = null;
            string status = string.Empty;
            IEnumerator planBuildRoutine = this.BuildWildAnimalFeedPlansRoutine(
                (builtPlans, buildStatus) =>
                {
                    plans = builtPlans;
                    status = buildStatus;
                });
            while (planBuildRoutine.MoveNext())
            {
                yield return planBuildRoutine.Current;
            }

            if (plans == null)
            {
                status = status ?? "plan build failed";
                this.wildAnimalFeedLastStatus = status;
                if (!silent)
                {
                    this.AddMenuNotification("Wild feed: " + status, new Color(1f, 0.58f, 0.42f));
                }
                this.WildAnimalFeedLog("Plan failed: " + status);
                this.wildAnimalFeedCoroutine = null;
                this.wildAnimalFeedBusyUntil = Time.realtimeSinceStartup + WildAnimalFeedActionCooldownSeconds;
                yield break;
            }

            if (plans.Count == 0)
            {
                this.wildAnimalFeedLastStatus = status;
                if (!silent)
                {
                    this.AddMenuNotification("Wild feed: " + status, new Color(0.45f, 0.88f, 1f));
                }
                this.wildAnimalFeedCoroutine = null;
                this.wildAnimalFeedBusyUntil = Time.realtimeSinceStartup + WildAnimalFeedActionCooldownSeconds;
                yield break;
            }

            IEnumerator routine = this.WildAnimalFeedAllRoutine(plans, silent);
            while (routine.MoveNext())
            {
                yield return routine.Current;
            }
        }

        private IEnumerator WildAnimalFeedAllRoutine(List<WildAnimalFeedGroupPlan> plans, bool silent)
        {
            int fed = 0;
            int skipped = 0;
            try
            {
                foreach (WildAnimalFeedGroupPlan plan in plans)
                {
                    if (plan == null || plan.GroupId <= 0 || plan.FoodNetIds == null || plan.FoodNetIds.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    if (!this.TryInvokeWildAnimalFeed(plan.GroupId, plan.FoodNetIds, out string status))
                    {
                        skipped++;
                        this.WildAnimalFeedLog("Feed failed group=" + plan.GroupId + " (" + plan.GroupName + "): " + status);
                        yield return new WaitForSecondsRealtime(0.35f);
                        continue;
                    }

                    fed++;
                    this.WildAnimalFeedLog("Fed group=" + plan.GroupId + " " + plan.GroupName
                        + " +" + plan.FoodNetIds.Count + " items"
                        + " fullness " + plan.CurrentFullness + "/" + plan.MaxFullness
                        + " need=" + plan.NeededFullness);
                    yield return new WaitForSecondsRealtime(0.55f);
                }

                this.wildAnimalFeedLastStatus = "Fed " + fed + " trough(s), skipped " + skipped + ".";
                if (!silent || fed > 0)
                {
                    this.AddMenuNotification(
                        "Wild trough feed: " + fed + " filled" + (skipped > 0 ? ", " + skipped + " skipped" : string.Empty),
                        fed > 0 ? new Color(0.45f, 1f, 0.55f) : new Color(0.45f, 0.88f, 1f));
                }
            }
            finally
            {
                this.wildAnimalFeedCoroutine = null;
                this.wildAnimalFeedBusyUntil = Time.realtimeSinceStartup + WildAnimalFeedActionCooldownSeconds;
            }
        }

        private IEnumerator BuildWildAnimalFeedPlansRoutine(Action<List<WildAnimalFeedGroupPlan>, string> complete)
        {
            this.ClearWildAnimalFeedPlanCaches();
            this.wildAnimalFeedDetailLogBudget = WildAnimalFeedMaxDetailLogsPerPlan;
            yield return null;

            List<WildAnimalFeedGroupPlan> plans = new List<WildAnimalFeedGroupPlan>();
            string status = string.Empty;
            bool managedReady = !this.wildAnimalFeedManagedReflectionUnavailable
                && this.EnsureWildAnimalFeedReflection(out status);
            if (managedReady)
            {
                object wildAnimalSystem = this.wildAnimalFeedWildAnimalSystemInstanceProperty.GetValue(null, null);
                if (wildAnimalSystem != null && this.TryBuildWildAnimalFeedPlans(out plans, out status) && plans.Count > 0)
                {
                    complete(plans, status);
                    yield break;
                }
            }

            plans.Clear();
            IEnumerator auraRoutine = this.BuildWildAnimalFeedPlansAuraMonoRoutine(plans, builtStatus => status = builtStatus);
            while (auraRoutine.MoveNext())
            {
                yield return auraRoutine.Current;
            }

            complete(plans, status);
        }

        private void ClearWildAnimalFeedPlanCaches()
        {
            this.wildAnimalFeedFullnessByKeyCache.Clear();
            this.wildAnimalFeedGroupIdsByStaticIdCache.Clear();
        }

        private static long WildAnimalFeedFullnessCacheKey(int staticId, int groupId, int starRate)
        {
            return ((long)staticId << 24) | ((long)groupId << 8) | (uint)(starRate & 0xFF);
        }

        private bool TryBuildWildAnimalFeedPlans(out List<WildAnimalFeedGroupPlan> plans, out string status)
        {
            plans = new List<WildAnimalFeedGroupPlan>();
            status = string.Empty;

            try
            {
                this.WildAnimalFeedLog("=== Plan build start (managed path) ===");
                this.WildAnimalFeedLogToggles();

                if (!this.EnsureWildAnimalFeedReflection(out status))
                {
                    this.WildAnimalFeedLog("Managed reflection failed: " + status);
                    return false;
                }

                this.WildAnimalFeedLog("Managed reflection OK");

                object wildAnimalSystem = this.wildAnimalFeedWildAnimalSystemInstanceProperty.GetValue(null, null);
                if (wildAnimalSystem == null)
                {
                    status = "WildAnimalSystem unavailable";
                    this.WildAnimalFeedLog(status);
                    return false;
                }

                this.WildAnimalFeedLog("WildAnimalSystem=" + wildAnimalSystem.GetType().FullName);

                object groupsObj = this.wildAnimalFeedGetUnlockedAnimalsMethod.Invoke(wildAnimalSystem, null);
                if (groupsObj == null)
                {
                    status = "no unlocked animal groups";
                    this.WildAnimalFeedLog(status);
                    return false;
                }

                int checkedGroups = 0;
                int hungryGroups = 0;
                int totalRawFoods = 0;
                int totalAcceptedFoods = 0;
                int totalSkippedStar = 0;
                int totalSkippedEgg = 0;
                int totalSkippedFavorite = 0;
                int totalSkippedLock = 0;
                int totalSkippedInvalid = 0;
                Dictionary<uint, int> reservedCountsByNetId = new Dictionary<uint, int>();
                List<object> unlockedGroups = new List<object>();
                if (!this.TryEnumerateManagedCollectionItems(groupsObj, unlockedGroups))
                {
                    status = "cannot enumerate unlocked groups";
                    this.WildAnimalFeedLog(status + " collectionType=" + (groupsObj?.GetType().FullName ?? "null"));
                    return false;
                }

                this.WildAnimalFeedLog("Unlocked groups enumerated: count=" + unlockedGroups.Count
                    + " collectionType=" + groupsObj.GetType().FullName);

                foreach (object groupObj in unlockedGroups)
                {
                    if (!this.TryGetAnimalGroupId(groupObj, out int groupId) || groupId <= 0)
                    {
                        continue;
                    }

                    checkedGroups++;
                    object groupEnum = Enum.ToObject(this.wildAnimalFeedAnimalGroupType, groupId);
                    int fullness = Convert.ToInt32(this.wildAnimalFeedGetFullnessMethod.Invoke(wildAnimalSystem, new[] { groupEnum }));
                    int capacity = Convert.ToInt32(this.wildAnimalFeedGetFeedTroughCapacityMethod.Invoke(wildAnimalSystem, new[] { groupEnum }));
                    if (capacity <= 0)
                    {
                        continue;
                    }

                    int needed = capacity - fullness;
                    if (needed <= Mathf.CeilToInt(capacity * WildAnimalFeedMinEmptyRatio))
                    {
                        continue;
                    }

                    hungryGroups++;
                    string groupName = this.GetWildAnimalGroupDisplayName(groupId);
                    if (!this.TryGetWildAnimalFavoriteFoodIds(wildAnimalSystem, groupEnum, groupId, out HashSet<int> favoriteIds))
                    {
                        favoriteIds = new HashSet<int>();
                    }

                    this.WildAnimalFeedLog("Hungry group id=" + groupId + " name=" + groupName
                        + " fullness=" + fullness + "/" + capacity + " need=" + needed
                        + " favorites=" + favoriteIds.Count);

                    WildAnimalFeedCollectStats collectStats = new WildAnimalFeedCollectStats();
                    List<WildAnimalFeedFoodCandidate> candidates = this.CollectWildAnimalFeedCandidatesManaged(
                        wildAnimalSystem,
                        groupEnum,
                        groupId,
                        favoriteIds,
                        collectStats);
                    totalRawFoods += collectStats.RawItems;
                    totalAcceptedFoods += collectStats.Accepted;
                    totalSkippedStar += collectStats.SkippedStar;
                    totalSkippedEgg += collectStats.SkippedEgg;
                    totalSkippedFavorite += collectStats.SkippedFavorite;
                    totalSkippedLock += collectStats.SkippedLock;
                    totalSkippedInvalid += collectStats.SkippedInvalid;
                    this.WildAnimalFeedLog("Group " + groupId + " collect: " + this.FormatWildAnimalFeedCollectStats(collectStats)
                        + " candidates=" + candidates.Count);
                    this.ApplyWildAnimalFeedReservation(candidates, reservedCountsByNetId);
                    if (candidates.Count == 0)
                    {
                        this.WildAnimalFeedLog("Group " + groupId + " skip: no candidates after reservation");
                        continue;
                    }

                    List<uint> foodNetIds = this.SelectWildAnimalFeedFoodNetIds(candidates, needed);
                    this.ReserveWildAnimalFeedNetIds(foodNetIds, reservedCountsByNetId);
                    if (foodNetIds.Count == 0)
                    {
                        this.WildAnimalFeedLog("Group " + groupId + " skip: SelectFood returned 0 (candidates=" + candidates.Count + " need=" + needed + ")");
                        continue;
                    }

                    this.WildAnimalFeedLog("Group " + groupId + " plan OK: items=" + foodNetIds.Count);
                    plans.Add(new WildAnimalFeedGroupPlan
                    {
                        GroupId = groupId,
                        GroupName = groupName,
                        CurrentFullness = fullness,
                        MaxFullness = capacity,
                        NeededFullness = needed,
                        FoodNetIds = foodNetIds
                    });
                }

                status = "groups=" + checkedGroups + " hungry=" + hungryGroups + " feedable=" + plans.Count
                    + " foods=" + totalAcceptedFoods + "/" + totalRawFoods;
                if (totalSkippedStar > 0)
                {
                    status += " skip5*=" + totalSkippedStar;
                }
                if (totalSkippedEgg > 0)
                {
                    status += " skipEgg=" + totalSkippedEgg;
                }
                if (totalSkippedFavorite > 0)
                {
                    status += " skipFav=" + totalSkippedFavorite;
                }
                if (totalSkippedLock > 0)
                {
                    status += " skipLock=" + totalSkippedLock;
                }
                if (totalSkippedInvalid > 0)
                {
                    status += " skipInv=" + totalSkippedInvalid;
                }
                if (plans.Count == 0 && hungryGroups > 0 && totalRawFoods == 0)
                {
                    status += " (no food found in bag/warehouse)";
                }
                else if (plans.Count == 0 && hungryGroups > 0 && totalAcceptedFoods == 0 && totalRawFoods > 0)
                {
                    status += " (all food filtered by toggles)";
                }

                this.WildAnimalFeedLog("=== Plan result: " + status + " ===");
                return true;
            }
            catch (Exception ex)
            {
                status = (ex.InnerException ?? ex).Message;
                this.WildAnimalFeedLog("Plan exception: " + ex);
                return false;
            }
        }

        private IEnumerator BuildWildAnimalFeedPlansAuraMonoRoutine(
            List<WildAnimalFeedGroupPlan> plans,
            Action<string> setStatus)
        {
            string status = "AuraMono wild feed unavailable";
            if (plans == null)
            {
                setStatus?.Invoke(status);
                yield break;
            }

            plans.Clear();
            this.WildAnimalFeedLog("=== Plan build start (AuraMono path) ===");
            this.WildAnimalFeedLogToggles();

            WildAnimalFeedAuraPlanContext context;
            if (!this.TryCreateWildAnimalFeedAuraPlanContext(out context, out status))
            {
                this.WildAnimalFeedLog(status);
                setStatus?.Invoke(status);
                yield break;
            }

            for (int i = 0; i < context.GroupItems.Count; i++)
            {
                try
                {
                    this.TryAppendWildAnimalFeedAuraMonoGroupPlan(context, plans, context.GroupItems[i]);
                }
                catch (Exception ex)
                {
                    this.WildAnimalFeedLog("AuraMono group exception: " + ex.Message);
                }

                yield return null;
            }

            status = "AuraMono groups=" + context.CheckedGroups + " hungry=" + context.HungryGroups + " feedable=" + plans.Count;
            this.WildAnimalFeedLog("=== AuraMono plan result: " + status + " ===");
            setStatus?.Invoke(status);
        }

        private unsafe bool TryCreateWildAnimalFeedAuraPlanContext(out WildAnimalFeedAuraPlanContext context, out string status)
        {
            context = null;
            status = "AuraMono wild feed unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono API unavailable";
                return false;
            }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.WildAnimal.WildAnimalSystem", out IntPtr wildAnimalSystemObj)
                || wildAnimalSystemObj == IntPtr.Zero)
            {
                status = "AuraMono WildAnimalSystem unavailable";
                return false;
            }

            IntPtr wildAnimalSystemClass = auraMonoObjectGetClass(wildAnimalSystemObj);
            IntPtr getUnlockedMethod = this.FindAuraMonoMethodOnHierarchy(wildAnimalSystemClass, "GetUnlockedAnimals", 0);
            IntPtr getFullnessMethod = this.FindAuraMonoMethodOnHierarchy(wildAnimalSystemClass, "GetFullness", 1);
            IntPtr getCapacityMethod = this.FindAuraMonoMethodOnHierarchy(wildAnimalSystemClass, "GetFeedTroughCapacity", 1);
            IntPtr getFavoriteFoodMethod = this.FindAuraMonoMethodOnHierarchy(wildAnimalSystemClass, "GetFavoriteFood", 1);
            IntPtr getFoodsMethod = this.FindAuraMonoMethodOnHierarchy(wildAnimalSystemClass, "GetFoods", 2);
            if (getUnlockedMethod == IntPtr.Zero || getFullnessMethod == IntPtr.Zero || getCapacityMethod == IntPtr.Zero || getFoodsMethod == IntPtr.Zero)
            {
                status = "AuraMono WildAnimalSystem methods unavailable";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr groupsObj = auraMonoRuntimeInvoke(getUnlockedMethod, wildAnimalSystemObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || groupsObj == IntPtr.Zero)
            {
                status = "AuraMono GetUnlockedAnimals failed";
                return false;
            }

            context = new WildAnimalFeedAuraPlanContext
            {
                WildAnimalSystemObj = wildAnimalSystemObj,
                GetFullnessMethod = getFullnessMethod,
                GetCapacityMethod = getCapacityMethod,
                GetFavoriteFoodMethod = getFavoriteFoodMethod,
                GetFoodsMethod = getFoodsMethod
            };
            if (!this.TryEnumerateAuraMonoCollectionItems(groupsObj, context.GroupItems) || context.GroupItems.Count == 0)
            {
                status = "AuraMono unlocked groups empty";
                context = null;
                return false;
            }

            status = string.Empty;
            return true;
        }

        private unsafe void TryAppendWildAnimalFeedAuraMonoGroupPlan(
            WildAnimalFeedAuraPlanContext context,
            List<WildAnimalFeedGroupPlan> plans,
            IntPtr groupObj)
        {
            if (context == null || plans == null || groupObj == IntPtr.Zero)
            {
                return;
            }

            if (!this.TryGetWildAnimalFeedGroupIdAuraMono(groupObj, out int groupId) || groupId <= 0)
            {
                return;
            }

            context.CheckedGroups++;
            IntPtr* groupArgs = stackalloc IntPtr[1];
            groupArgs[0] = (IntPtr)(&groupId);
            IntPtr exc = IntPtr.Zero;
            int fullness = 0;
            IntPtr fullnessObj = auraMonoRuntimeInvoke(context.GetFullnessMethod, context.WildAnimalSystemObj, (IntPtr)groupArgs, ref exc);
            if (exc == IntPtr.Zero && fullnessObj != IntPtr.Zero)
            {
                this.TryUnboxMonoInt32(fullnessObj, out fullness);
            }

            exc = IntPtr.Zero;
            int capacity = 0;
            IntPtr capacityObj = auraMonoRuntimeInvoke(context.GetCapacityMethod, context.WildAnimalSystemObj, (IntPtr)groupArgs, ref exc);
            if (exc == IntPtr.Zero && capacityObj != IntPtr.Zero)
            {
                this.TryUnboxMonoInt32(capacityObj, out capacity);
            }

            if (capacity <= 0)
            {
                return;
            }

            int needed = capacity - fullness;
            if (needed <= Mathf.CeilToInt(capacity * WildAnimalFeedMinEmptyRatio))
            {
                return;
            }

            context.HungryGroups++;
            HashSet<int> favoriteIds = new HashSet<int>();
            if (context.GetFavoriteFoodMethod != IntPtr.Zero)
            {
                exc = IntPtr.Zero;
                IntPtr favoritesObj = auraMonoRuntimeInvoke(context.GetFavoriteFoodMethod, context.WildAnimalSystemObj, (IntPtr)groupArgs, ref exc);
                if (exc == IntPtr.Zero && favoritesObj != IntPtr.Zero && this.TryReadMonoIntListObject(favoritesObj, out List<int> favorites))
                {
                    foreach (int favorite in favorites)
                    {
                        favoriteIds.Add(favorite);
                    }
                }
            }

            string groupName = this.GetWildAnimalGroupDisplayName(groupId);
            WildAnimalFeedCollectStats collectStats = new WildAnimalFeedCollectStats();
            List<WildAnimalFeedFoodCandidate> candidates = this.CollectWildAnimalFeedCandidatesAuraMono(
                context.WildAnimalSystemObj,
                context.GetFoodsMethod,
                groupId,
                favoriteIds,
                collectStats);
            if (candidates.Count == 0)
            {
                return;
            }

            this.ApplyWildAnimalFeedReservation(candidates, context.ReservedCountsByNetId);
            List<uint> foodNetIds = this.SelectWildAnimalFeedFoodNetIds(candidates, needed);
            this.ReserveWildAnimalFeedNetIds(foodNetIds, context.ReservedCountsByNetId);
            if (foodNetIds.Count == 0)
            {
                return;
            }

            plans.Add(new WildAnimalFeedGroupPlan
            {
                GroupId = groupId,
                GroupName = groupName,
                CurrentFullness = fullness,
                MaxFullness = capacity,
                NeededFullness = needed,
                FoodNetIds = foodNetIds
            });
        }

        private List<WildAnimalFeedFoodCandidate> CollectWildAnimalFeedCandidatesManaged(
            object wildAnimalSystem,
            object groupEnum,
            int groupId,
            HashSet<int> favoriteIds,
            WildAnimalFeedCollectStats stats)
        {
            List<WildAnimalFeedFoodCandidate> candidates = new List<WildAnimalFeedFoodCandidate>();
            HashSet<uint> seenNetIds = new HashSet<uint>();
            this.TryGetWildAnimalGroupMeta(groupId, out List<int> staticFavorites, out int favoriteAddition, out _);
            if (stats == null)
            {
                stats = new WildAnimalFeedCollectStats();
            }

            int before = candidates.Count;
            this.AppendWildAnimalFeedCandidatesFromGetFoodsManaged(
                wildAnimalSystem,
                groupEnum,
                groupId,
                favoriteIds,
                staticFavorites,
                favoriteAddition,
                candidates,
                seenNetIds,
                stats,
                favoritesOnly: this.wildAnimalFeedPreferFavorites);
            this.WildAnimalFeedLogDetail("Group " + groupId + " GetFoods favOnly: +" + (candidates.Count - before) + " total=" + candidates.Count);

            if (candidates.Count == 0 && this.wildAnimalFeedPreferFavorites)
            {
                before = candidates.Count;
                this.AppendWildAnimalFeedCandidatesFromGetFoodsManaged(
                    wildAnimalSystem,
                    groupEnum,
                    groupId,
                    favoriteIds,
                    staticFavorites,
                    favoriteAddition,
                    candidates,
                    seenNetIds,
                    stats,
                    favoritesOnly: false);
                this.WildAnimalFeedLogDetail("Group " + groupId + " GetFoods allFood: +" + (candidates.Count - before));
            }

            if (candidates.Count == 0)
            {
                before = candidates.Count;
                this.AppendWildAnimalFeedCandidatesFromBackpackManaged(
                    groupId,
                    favoriteIds,
                    staticFavorites,
                    favoriteAddition,
                    candidates,
                    seenNetIds,
                    stats,
                    favoritesOnly: this.wildAnimalFeedPreferFavorites);
                this.WildAnimalFeedLogDetail("Group " + groupId + " Backpack scan: +" + (candidates.Count - before));

                if (candidates.Count == 0 && this.wildAnimalFeedPreferFavorites)
                {
                    before = candidates.Count;
                    this.AppendWildAnimalFeedCandidatesFromBackpackManaged(
                        groupId,
                        favoriteIds,
                        staticFavorites,
                        favoriteAddition,
                        candidates,
                        seenNetIds,
                        stats,
                        favoritesOnly: false);
                    this.WildAnimalFeedLogDetail("Group " + groupId + " Backpack allFood: +" + (candidates.Count - before));
                }
            }

            return candidates;
        }

        private void AppendWildAnimalFeedCandidatesFromGetFoodsManaged(
            object wildAnimalSystem,
            object groupEnum,
            int groupId,
            HashSet<int> favoriteIds,
            List<int> staticFavorites,
            int favoriteAddition,
            List<WildAnimalFeedFoodCandidate> candidates,
            HashSet<uint> seenNetIds,
            WildAnimalFeedCollectStats stats,
            bool favoritesOnly)
        {
            foreach (string storageName in WildAnimalFeedStorageNames)
            {
                if (!this.TryGetWildAnimalFeedStorageObject(storageName, out object storage))
                {
                    this.WildAnimalFeedLogDetail("Group " + groupId + " GetFoods storage=" + storageName + " enum parse failed");
                    continue;
                }

                object foodsObj;
                try
                {
                    foodsObj = this.wildAnimalFeedGetFoodsMethod.Invoke(wildAnimalSystem, new[] { groupEnum, storage });
                }
                catch (Exception ex)
                {
                    this.WildAnimalFeedLogDetail("Group " + groupId + " GetFoods " + storageName + " invoke error: " + ex.Message);
                    continue;
                }

                if (foodsObj == null)
                {
                    this.WildAnimalFeedLogDetail("Group " + groupId + " GetFoods " + storageName + " returned null");
                    continue;
                }

                int before = candidates.Count;
                this.AppendWildAnimalFeedCandidatesManaged(
                    candidates,
                    seenNetIds,
                    foodsObj,
                    groupId,
                    favoriteIds,
                    staticFavorites,
                    favoriteAddition,
                    stats,
                    favoritesOnly,
                    "GetFoods/" + storageName + (favoritesOnly ? "/favOnly" : "/all"));
                this.WildAnimalFeedLogDetail("Group " + groupId + " GetFoods " + storageName
                    + (favoritesOnly ? " favOnly" : " all")
                    + " listType=" + foodsObj.GetType().FullName
                    + " added=" + (candidates.Count - before));
            }
        }

        private void AppendWildAnimalFeedCandidatesManaged(
            List<WildAnimalFeedFoodCandidate> candidates,
            HashSet<uint> seenNetIds,
            object foodsObj,
            int groupId,
            HashSet<int> favoriteIds,
            List<int> staticFavorites,
            int favoriteAddition,
            WildAnimalFeedCollectStats stats,
            bool favoritesOnly,
            string source)
        {
            List<object> foodItems = new List<object>();
            if (!this.TryEnumerateWildAnimalFeedObjects(foodsObj, foodItems))
            {
                this.WildAnimalFeedLogDetail("Group " + groupId + " " + source + " enumerate FAILED type=" + foodsObj.GetType().FullName);
                return;
            }

            if (foodItems.Count == 0)
            {
                this.WildAnimalFeedLogDetail("Group " + groupId + " " + source + " enumerate OK but count=0");
            }

            foreach (object foodObj in foodItems)
            {
                stats.RawItems++;
                if (!this.TryBuildWildAnimalFeedFoodCandidate(
                    foodObj,
                    groupId,
                    favoriteIds,
                    staticFavorites,
                    favoriteAddition,
                    out WildAnimalFeedFoodCandidate candidate,
                    out WildAnimalFeedSkipReason skipReason))
                {
                    this.IncrementWildAnimalFeedSkip(stats, skipReason);
                    this.WildAnimalFeedLogRejectManaged(groupId, source, foodObj, skipReason);
                    continue;
                }

                if (favoritesOnly && this.wildAnimalFeedPreferFavorites && !candidate.IsFavorite)
                {
                    stats.SkippedFavorite++;
                    this.WildAnimalFeedLogRejectManaged(groupId, source, foodObj, WildAnimalFeedSkipReason.None, "not-favorite");
                    continue;
                }

                if (!seenNetIds.Add(candidate.NetId))
                {
                    this.WildAnimalFeedLogDetail("Group " + groupId + " " + source + " dup netId=" + candidate.NetId);
                    continue;
                }

                stats.Accepted++;
                candidates.Add(candidate);
                this.WildAnimalFeedLogDetail("Group " + groupId + " " + source + " ACCEPT staticId=" + candidate.StaticId
                    + " netId=" + candidate.NetId + " fullness=" + candidate.Fullness + " star? count=" + candidate.Count);
            }
        }

        private void AppendWildAnimalFeedCandidatesFromBackpackManaged(
            int groupId,
            HashSet<int> favoriteIds,
            List<int> staticFavorites,
            int favoriteAddition,
            List<WildAnimalFeedFoodCandidate> candidates,
            HashSet<uint> seenNetIds,
            WildAnimalFeedCollectStats stats,
            bool favoritesOnly)
        {
            if (!this.EnsureWildAnimalFeedBackpackReflection())
            {
                this.WildAnimalFeedLogDetail("Group " + groupId + " Backpack reflection unavailable");
                return;
            }

            object backPackObj = this.GetWildAnimalFeedBackPackSystemInstance();
            if (backPackObj == null)
            {
                this.WildAnimalFeedLogDetail("Group " + groupId + " BackPackSystem instance null");
                return;
            }

            this.WildAnimalFeedLogDetail("Group " + groupId + " BackPackSystem=" + backPackObj.GetType().FullName);

            foreach (string storageName in WildAnimalFeedStorageNames)
            {
                if (!this.TryGetWildAnimalFeedStorageObject(storageName, out object storage))
                {
                    continue;
                }

                object itemListObj;
                try
                {
                    ParameterInfo[] parameters = this.wildAnimalFeedBackPackGetAllItemMethod.GetParameters();
                    itemListObj = parameters.Length == 1
                        ? this.wildAnimalFeedBackPackGetAllItemMethod.Invoke(backPackObj, new[] { storage })
                        : this.wildAnimalFeedBackPackGetAllItemMethod.Invoke(backPackObj, null);
                }
                catch (Exception ex)
                {
                    this.WildAnimalFeedLogDetail("Group " + groupId + " Backpack " + storageName + " GetAllItem error: " + ex.Message);
                    continue;
                }

                if (itemListObj == null)
                {
                    this.WildAnimalFeedLogDetail("Group " + groupId + " Backpack " + storageName + " GetAllItem null");
                    continue;
                }

                List<object> backpackItems = new List<object>();
                if (!this.TryEnumerateWildAnimalFeedObjects(itemListObj, backpackItems))
                {
                    this.WildAnimalFeedLogDetail("Group " + groupId + " Backpack " + storageName + " enumerate FAILED type=" + itemListObj.GetType().FullName);
                    continue;
                }

                this.WildAnimalFeedLogDetail("Group " + groupId + " Backpack " + storageName
                    + (favoritesOnly ? " favOnly" : " all")
                    + " items=" + backpackItems.Count
                    + " listType=" + itemListObj.GetType().FullName);

                string source = "Backpack/" + storageName + (favoritesOnly ? "/favOnly" : "/all");
                foreach (object item in backpackItems)
                {
                    stats.RawItems++;
                    if (!this.TryBuildWildAnimalFeedFoodCandidateFromBackpackItem(
                        item,
                        groupId,
                        favoriteIds,
                        staticFavorites,
                        favoriteAddition,
                        out WildAnimalFeedFoodCandidate candidate,
                        out WildAnimalFeedSkipReason skipReason))
                    {
                        this.IncrementWildAnimalFeedSkip(stats, skipReason);
                        this.WildAnimalFeedLogRejectManaged(groupId, source, item, skipReason);
                        continue;
                    }

                    if (favoritesOnly && this.wildAnimalFeedPreferFavorites && !candidate.IsFavorite)
                    {
                        stats.SkippedFavorite++;
                        this.WildAnimalFeedLogRejectManaged(groupId, source, item, WildAnimalFeedSkipReason.None, "not-favorite");
                        continue;
                    }

                    if (!seenNetIds.Add(candidate.NetId))
                    {
                        continue;
                    }

                    stats.Accepted++;
                    candidates.Add(candidate);
                    this.WildAnimalFeedLogDetail("Group " + groupId + " " + source + " ACCEPT staticId=" + candidate.StaticId
                        + " netId=" + candidate.NetId + " fullness=" + candidate.Fullness);
                }
            }
        }

        private enum WildAnimalFeedSkipReason
        {
            None,
            Invalid,
            Lock,
            Star,
            Egg,
            NoGroup
        }

        private void IncrementWildAnimalFeedSkip(WildAnimalFeedCollectStats stats, WildAnimalFeedSkipReason reason)
        {
            if (stats == null)
            {
                return;
            }

            switch (reason)
            {
                case WildAnimalFeedSkipReason.Star:
                    stats.SkippedStar++;
                    break;
                case WildAnimalFeedSkipReason.Egg:
                    stats.SkippedEgg++;
                    break;
                case WildAnimalFeedSkipReason.Lock:
                    stats.SkippedLock++;
                    break;
                case WildAnimalFeedSkipReason.NoGroup:
                case WildAnimalFeedSkipReason.Invalid:
                    stats.SkippedInvalid++;
                    break;
            }
        }

        private unsafe List<WildAnimalFeedFoodCandidate> CollectWildAnimalFeedCandidatesAuraMono(
            IntPtr wildAnimalSystemObj,
            IntPtr getFoodsMethod,
            int groupId,
            HashSet<int> favoriteIds,
            WildAnimalFeedCollectStats stats)
        {
            List<WildAnimalFeedFoodCandidate> candidates = new List<WildAnimalFeedFoodCandidate>();
            HashSet<uint> seenNetIds = new HashSet<uint>();
            this.TryGetWildAnimalGroupMeta(groupId, out List<int> staticFavorites, out int favoriteAddition, out _);
            if (stats == null)
            {
                stats = new WildAnimalFeedCollectStats();
            }

            this.TryEnsureWildAnimalFeedAuraStorageValues();
            int before = candidates.Count;
            this.AppendWildAnimalFeedCandidatesFromGetFoodsAuraMono(
                wildAnimalSystemObj,
                getFoodsMethod,
                groupId,
                favoriteIds,
                staticFavorites,
                favoriteAddition,
                candidates,
                seenNetIds,
                stats,
                favoritesOnly: this.wildAnimalFeedPreferFavorites);
            this.WildAnimalFeedLogDetail("AuraMono group " + groupId + " GetFoods favOnly: +" + (candidates.Count - before));

            if (candidates.Count == 0 && this.wildAnimalFeedPreferFavorites)
            {
                before = candidates.Count;
                this.AppendWildAnimalFeedCandidatesFromGetFoodsAuraMono(
                    wildAnimalSystemObj,
                    getFoodsMethod,
                    groupId,
                    favoriteIds,
                    staticFavorites,
                    favoriteAddition,
                    candidates,
                    seenNetIds,
                    stats,
                    favoritesOnly: false);
                this.WildAnimalFeedLogDetail("AuraMono group " + groupId + " GetFoods allFood: +" + (candidates.Count - before));
            }

            if (candidates.Count == 0)
            {
                before = candidates.Count;
                this.AppendWildAnimalFeedCandidatesFromBackpackAuraMono(
                    groupId,
                    favoriteIds,
                    staticFavorites,
                    favoriteAddition,
                    candidates,
                    seenNetIds,
                    stats,
                    favoritesOnly: this.wildAnimalFeedPreferFavorites);
                this.WildAnimalFeedLogDetail("AuraMono group " + groupId + " Backpack: +" + (candidates.Count - before));

                if (candidates.Count == 0 && this.wildAnimalFeedPreferFavorites)
                {
                    before = candidates.Count;
                    this.AppendWildAnimalFeedCandidatesFromBackpackAuraMono(
                        groupId,
                        favoriteIds,
                        staticFavorites,
                        favoriteAddition,
                        candidates,
                        seenNetIds,
                        stats,
                        favoritesOnly: false);
                    this.WildAnimalFeedLogDetail("AuraMono group " + groupId + " Backpack allFood: +" + (candidates.Count - before));
                }
            }

            return candidates;
        }

        private unsafe void AppendWildAnimalFeedCandidatesFromGetFoodsAuraMono(
            IntPtr wildAnimalSystemObj,
            IntPtr getFoodsMethod,
            int groupId,
            HashSet<int> favoriteIds,
            List<int> staticFavorites,
            int favoriteAddition,
            List<WildAnimalFeedFoodCandidate> candidates,
            HashSet<uint> seenNetIds,
            WildAnimalFeedCollectStats stats,
            bool favoritesOnly)
        {
            foreach (string storageName in WildAnimalFeedStorageNames)
            {
                if (!this.TryGetWildAnimalFeedStorageValue(storageName, out int storageValue))
                {
                    this.WildAnimalFeedLogDetail("AuraMono group " + groupId + " storage=" + storageName + " unresolved");
                    continue;
                }

                IntPtr* foodArgs = stackalloc IntPtr[2];
                foodArgs[0] = (IntPtr)(&groupId);
                foodArgs[1] = (IntPtr)(&storageValue);
                IntPtr exc = IntPtr.Zero;
                IntPtr foodsObj = auraMonoRuntimeInvoke(getFoodsMethod, wildAnimalSystemObj, (IntPtr)foodArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.WildAnimalFeedLogDetail("AuraMono GetFoods " + storageName + "(" + storageValue + ") exc=0x" + exc.ToInt64().ToString("X"));
                    continue;
                }

                if (foodsObj == IntPtr.Zero)
                {
                    this.WildAnimalFeedLogDetail("AuraMono GetFoods " + storageName + "(" + storageValue + ") null");
                    continue;
                }

                int before = candidates.Count;
                this.AppendWildAnimalFeedCandidatesAuraMono(
                    candidates,
                    seenNetIds,
                    foodsObj,
                    groupId,
                    favoriteIds,
                    staticFavorites,
                    favoriteAddition,
                    stats,
                    favoritesOnly,
                    "AuraGetFoods/" + storageName + (favoritesOnly ? "/favOnly" : "/all"));
                this.WildAnimalFeedLogDetail("AuraMono GetFoods " + storageName + "=" + storageValue
                    + " added=" + (candidates.Count - before));
            }
        }

        private unsafe void AppendWildAnimalFeedCandidatesFromBackpackAuraMono(
            int groupId,
            HashSet<int> favoriteIds,
            List<int> staticFavorites,
            int favoriteAddition,
            List<WildAnimalFeedFoodCandidate> candidates,
            HashSet<uint> seenNetIds,
            WildAnimalFeedCollectStats stats,
            bool favoritesOnly)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return;
            }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackObj)
                || backPackObj == IntPtr.Zero)
            {
                this.WildAnimalFeedLogDetail("AuraMono BackPackSystem unavailable");
                return;
            }

            IntPtr backPackClass = auraMonoObjectGetClass(backPackObj);
            IntPtr getAllItemMethodWithStorage = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
            IntPtr getAllItemMethodNoArgs = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
            if (getAllItemMethodWithStorage == IntPtr.Zero && getAllItemMethodNoArgs == IntPtr.Zero)
            {
                this.WildAnimalFeedLogDetail("AuraMono BackPackSystem.GetAllItem unavailable");
                return;
            }

            foreach (string storageName in WildAnimalFeedStorageNames)
            {
                if (!this.TryGetWildAnimalFeedStorageValue(storageName, out int storageValue))
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr itemsObj;
                if (getAllItemMethodWithStorage != IntPtr.Zero)
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&storageValue);
                    itemsObj = auraMonoRuntimeInvoke(getAllItemMethodWithStorage, backPackObj, (IntPtr)args, ref exc);
                }
                else
                {
                    itemsObj = auraMonoRuntimeInvoke(getAllItemMethodNoArgs, backPackObj, IntPtr.Zero, ref exc);
                }

                if (exc != IntPtr.Zero || itemsObj == IntPtr.Zero)
                {
                    this.WildAnimalFeedLogDetail("AuraMono Backpack " + storageName + " GetAllItem failed exc=0x" + exc.ToInt64().ToString("X"));
                    continue;
                }

                List<IntPtr> backpackItems = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(itemsObj, backpackItems))
                {
                    this.WildAnimalFeedLogDetail("AuraMono Backpack " + storageName + " enumerate failed");
                    continue;
                }

                this.WildAnimalFeedLogDetail("AuraMono Backpack " + storageName + " items=" + backpackItems.Count);
                string source = "AuraBackpack/" + storageName + (favoritesOnly ? "/favOnly" : "/all");
                foreach (IntPtr item in backpackItems)
                {
                    stats.RawItems++;
                    if (!this.TryBuildWildAnimalFeedFoodCandidateAuraMono(
                        item,
                        groupId,
                        favoriteIds,
                        staticFavorites,
                        favoriteAddition,
                        out WildAnimalFeedFoodCandidate candidate,
                        out WildAnimalFeedSkipReason skipReason))
                    {
                        this.IncrementWildAnimalFeedSkip(stats, skipReason);
                        this.WildAnimalFeedLogRejectAuraMono(groupId, source, item, skipReason);
                        continue;
                    }

                    if (favoritesOnly && this.wildAnimalFeedPreferFavorites && !candidate.IsFavorite)
                    {
                        stats.SkippedFavorite++;
                        continue;
                    }

                    if (!seenNetIds.Add(candidate.NetId))
                    {
                        continue;
                    }

                    stats.Accepted++;
                    candidates.Add(candidate);
                    this.WildAnimalFeedLogDetail("AuraMono ACCEPT group=" + groupId + " staticId=" + candidate.StaticId + " netId=" + candidate.NetId);
                }
            }
        }

        private void AppendWildAnimalFeedCandidatesAuraMono(
            List<WildAnimalFeedFoodCandidate> candidates,
            HashSet<uint> seenNetIds,
            IntPtr foodsObj,
            int groupId,
            HashSet<int> favoriteIds,
            List<int> staticFavorites,
            int favoriteAddition,
            WildAnimalFeedCollectStats stats,
            bool favoritesOnly,
            string source)
        {
            List<IntPtr> foodItems = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(foodsObj, foodItems))
            {
                this.WildAnimalFeedLogDetail("AuraMono " + source + " enumerate FAILED");
                return;
            }

            foreach (IntPtr foodObj in foodItems)
            {
                stats.RawItems++;
                if (!this.TryGetWildAnimalFoodCandidateAuraMono(foodObj, groupId, favoriteIds, staticFavorites, favoriteAddition, out WildAnimalFeedFoodCandidate candidate, out WildAnimalFeedSkipReason skipReason))
                {
                    this.IncrementWildAnimalFeedSkip(stats, skipReason);
                    this.WildAnimalFeedLogRejectAuraMono(groupId, source, foodObj, skipReason);
                    continue;
                }

                if (favoritesOnly && this.wildAnimalFeedPreferFavorites && !candidate.IsFavorite)
                {
                    stats.SkippedFavorite++;
                    continue;
                }

                if (!seenNetIds.Add(candidate.NetId))
                {
                    continue;
                }

                stats.Accepted++;
                candidates.Add(candidate);
            }
        }

        private bool TryBuildWildAnimalFeedFoodCandidate(
            object foodObj,
            int groupId,
            HashSet<int> favoriteIds,
            List<int> staticFavorites,
            int favoriteAddition,
            out WildAnimalFeedFoodCandidate candidate,
            out WildAnimalFeedSkipReason skipReason)
        {
            candidate = null;
            skipReason = WildAnimalFeedSkipReason.Invalid;
            if (foodObj == null)
            {
                return false;
            }

            if (!this.TryReadIntFromMember(foodObj, "staticId", out int staticId))
            {
                this.TryReadIntFromMember(foodObj, "StaticId", out staticId);
            }

            if (!this.TryReadUIntFromMember(foodObj, "netId", out uint netId))
            {
                this.TryReadUIntFromMember(foodObj, "NetId", out netId);
            }

            if (!this.TryReadIntFromMember(foodObj, "count", out int count))
            {
                this.TryReadIntFromMember(foodObj, "Count", out count);
            }

            count = Math.Max(1, count);
            int fullness = 0;
            if (!this.TryReadIntFromMember(foodObj, "foodFullness", out fullness))
            {
                this.TryReadIntFromMember(foodObj, "FoodFullness", out fullness);
            }

            bool isLock = false;
            if (this.TryGetObjectMember(foodObj, "isLock", out object lockObj) && lockObj != null)
            {
                try
                {
                    isLock = Convert.ToBoolean(lockObj);
                }
                catch
                {
                }
            }

            int starRate = 0;
            this.TryReadIntFromMember(foodObj, "starRate", out starRate);
            if (starRate == 0)
            {
                this.TryReadIntFromMember(foodObj, "StarRate", out starRate);
            }

            string itemName = string.Empty;
            if (this.TryGetObjectMember(foodObj, "name", out object nameObj) && nameObj != null)
            {
                itemName = Convert.ToString(nameObj) ?? string.Empty;
            }
            else if (this.TryGetObjectMember(foodObj, "Name", out nameObj) && nameObj != null)
            {
                itemName = Convert.ToString(nameObj) ?? string.Empty;
            }

            return this.TryFinalizeWildAnimalFeedFoodCandidate(
                staticId,
                netId,
                count,
                fullness,
                starRate,
                isLock,
                itemName,
                groupId,
                favoriteIds,
                staticFavorites,
                favoriteAddition,
                out candidate,
                out skipReason);
        }

        private bool TryBuildWildAnimalFeedFoodCandidateFromBackpackItem(
            object item,
            int groupId,
            HashSet<int> favoriteIds,
            List<int> staticFavorites,
            int favoriteAddition,
            out WildAnimalFeedFoodCandidate candidate,
            out WildAnimalFeedSkipReason skipReason)
        {
            candidate = null;
            skipReason = WildAnimalFeedSkipReason.Invalid;
            if (item == null)
            {
                return false;
            }

            if (!this.TryReadIntFromMember(item, "staticId", out int staticId))
            {
                this.TryReadIntFromMember(item, "StaticId", out staticId);
            }

            if (!this.TryReadUIntFromMember(item, "netId", out uint netId))
            {
                this.TryReadUIntFromMember(item, "NetId", out netId);
            }

            if (!this.TryReadIntFromMember(item, "count", out int count))
            {
                this.TryReadIntFromMember(item, "Count", out count);
            }

            count = Math.Max(1, count);
            int starRate = 0;
            this.TryReadIntFromMember(item, "starRate", out starRate);
            if (starRate == 0)
            {
                this.TryReadIntFromMember(item, "StarRate", out starRate);
            }

            bool isLock = false;
            if (this.TryGetObjectMember(item, "isLock", out object lockObj) && lockObj != null)
            {
                try
                {
                    isLock = Convert.ToBoolean(lockObj);
                }
                catch
                {
                }
            }

            if (!this.TryGetWildAnimalFoodGroupIdsForStaticId(staticId, out HashSet<int> allowedGroups)
                || allowedGroups == null
                || !allowedGroups.Contains(groupId))
            {
                skipReason = WildAnimalFeedSkipReason.NoGroup;
                return false;
            }

            if (!this.TryGetWildAnimalFoodFullnessForGroup(staticId, starRate, groupId, out int fullness))
            {
                skipReason = WildAnimalFeedSkipReason.NoGroup;
                return false;
            }

            string itemName = string.Empty;
            if (this.TryGetObjectMember(item, "Name", out object nameObj) && nameObj != null)
            {
                itemName = Convert.ToString(nameObj) ?? string.Empty;
            }
            else if (this.TryGetObjectMember(item, "name", out nameObj) && nameObj != null)
            {
                itemName = Convert.ToString(nameObj) ?? string.Empty;
            }

            return this.TryFinalizeWildAnimalFeedFoodCandidate(
                staticId,
                netId,
                count,
                fullness,
                starRate,
                isLock,
                itemName,
                groupId,
                favoriteIds,
                staticFavorites,
                favoriteAddition,
                out candidate,
                out skipReason);
        }

        private bool TryFinalizeWildAnimalFeedFoodCandidate(
            int staticId,
            uint netId,
            int count,
            int fullness,
            int starRate,
            bool isLock,
            string itemName,
            int groupId,
            HashSet<int> favoriteIds,
            List<int> staticFavorites,
            int favoriteAddition,
            out WildAnimalFeedFoodCandidate candidate,
            out WildAnimalFeedSkipReason skipReason)
        {
            candidate = null;
            skipReason = WildAnimalFeedSkipReason.Invalid;
            if (staticId <= 0 || netId == 0U || fullness <= 0)
            {
                return false;
            }

            if (isLock)
            {
                skipReason = WildAnimalFeedSkipReason.Lock;
                return false;
            }

            if (!this.IsWildAnimalFeedFoodAllowed(staticId, starRate, itemName, out skipReason))
            {
                return false;
            }

            int bondExp = 0;
            this.TryGetWildAnimalFoodBondExp(staticId, starRate, out bondExp);
            bool isFavorite = (favoriteIds != null && favoriteIds.Contains(staticId))
                || (staticFavorites != null && staticFavorites.Contains(staticId));
            candidate = new WildAnimalFeedFoodCandidate
            {
                StaticId = staticId,
                NetId = netId,
                Count = count,
                Fullness = fullness,
                BondExp = bondExp,
                IsFavorite = isFavorite,
                IsLock = isLock,
                SortScore = this.ComputeWildAnimalFeedSortScore(bondExp, fullness, isFavorite, favoriteAddition)
            };
            skipReason = WildAnimalFeedSkipReason.None;
            return true;
        }

        private bool TryGetWildAnimalFoodGroupIdsForStaticId(int staticId, out HashSet<int> groupIds)
        {
            groupIds = null;
            if (staticId <= 0)
            {
                return false;
            }

            if (this.wildAnimalFeedGroupIdsByStaticIdCache.TryGetValue(staticId, out groupIds))
            {
                return groupIds != null && groupIds.Count > 0;
            }

            groupIds = new HashSet<int>();
            if (this.TryGetWildAnimalFoodGroupIdsForStaticIdAuraMono(staticId, groupIds) && groupIds.Count > 0)
            {
                this.wildAnimalFeedGroupIdsByStaticIdCache[staticId] = groupIds;
                return true;
            }

            this.wildAnimalFeedGroupIdsByStaticIdCache[staticId] = groupIds;
            return false;
        }

        private bool TryGetWildAnimalFoodFullnessForGroup(int staticId, int starRate, int groupId, out int fullness)
        {
            fullness = 0;
            if (staticId <= 0 || groupId <= 0)
            {
                return false;
            }

            long cacheKey = WildAnimalFeedFullnessCacheKey(staticId, groupId, starRate);
            if (this.wildAnimalFeedFullnessByKeyCache.TryGetValue(cacheKey, out int cached))
            {
                if (cached <= WildAnimalFeedFullnessCacheMiss)
                {
                    return false;
                }

                fullness = cached;
                return true;
            }

            if (!this.TryGetWildAnimalFoodGroupIdsForStaticId(staticId, out HashSet<int> allowedGroups)
                || allowedGroups == null
                || !allowedGroups.Contains(groupId))
            {
                this.wildAnimalFeedFullnessByKeyCache[cacheKey] = WildAnimalFeedFullnessCacheMiss;
                return false;
            }

            try
            {
                if (this.EnsureWildAnimalFeedBackpackReflection())
                {
                    object row = this.wildAnimalFeedGetAnimalFoodThoughMethod.Invoke(null, new object[] { staticId });
                    if (row != null
                        && this.TryReadIntArrayFromMember(row, "feedValue", out int[] feedValues)
                        && feedValues != null
                        && feedValues.Length > 0)
                    {
                        int index = starRate > 0 && starRate <= 5 ? starRate - 1 : 0;
                        index = Mathf.Clamp(index, 0, feedValues.Length - 1);
                        fullness = feedValues[index];
                        if (fullness > 0)
                        {
                            this.wildAnimalFeedFullnessByKeyCache[cacheKey] = fullness;
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            if (this.TryGetWildAnimalFoodFullnessForGroupAuraMono(staticId, starRate, groupId, out fullness) && fullness > 0)
            {
                this.wildAnimalFeedFullnessByKeyCache[cacheKey] = fullness;
                return true;
            }

            this.wildAnimalFeedFullnessByKeyCache[cacheKey] = WildAnimalFeedFullnessCacheMiss;
            return false;
        }

        private unsafe bool TryGetWildAnimalFoodGroupIdsForStaticIdAuraMono(int staticId, HashSet<int> groupIds)
        {
            if (staticId <= 0 || groupIds == null || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr tableDataClass = this.TryGetWildAnimalFeedAuraMonoTableDataClass();
                if (tableDataClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getFoodMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetAnimalFoodThough", 1);
                if (getFoodMethod == IntPtr.Zero)
                {
                    getFoodMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetAnimalFoodThough", 2);
                }

                if (getFoodMethod == IntPtr.Zero)
                {
                    return false;
                }

                bool needException = false;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&staticId);
                args[1] = (IntPtr)(&needException);
                IntPtr exc = IntPtr.Zero;
                IntPtr rowObj = auraMonoRuntimeInvoke(getFoodMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || rowObj == IntPtr.Zero)
                {
                    IntPtr* argsOne = stackalloc IntPtr[1];
                    argsOne[0] = (IntPtr)(&staticId);
                    exc = IntPtr.Zero;
                    rowObj = auraMonoRuntimeInvoke(getFoodMethod, IntPtr.Zero, (IntPtr)argsOne, ref exc);
                }

                if (exc != IntPtr.Zero || rowObj == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryReadMonoIntListMember(rowObj, "groupId", out List<int> ids) || ids == null)
                {
                    return false;
                }

                for (int i = 0; i < ids.Count; i++)
                {
                    groupIds.Add(ids[i]);
                }

                return groupIds.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryGetWildAnimalFoodFullnessForGroupAuraMono(int staticId, int starRate, int groupId, out int fullness)
        {
            fullness = 0;
            if (staticId <= 0 || groupId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr tableDataClass = this.TryGetWildAnimalFeedAuraMonoTableDataClass();
                if (tableDataClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getFoodMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetAnimalFoodThough", 1);
                if (getFoodMethod == IntPtr.Zero)
                {
                    getFoodMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetAnimalFoodThough", 2);
                }

                if (getFoodMethod == IntPtr.Zero)
                {
                    return false;
                }

                bool needException = false;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&staticId);
                args[1] = (IntPtr)(&needException);
                IntPtr exc = IntPtr.Zero;
                IntPtr rowObj = auraMonoRuntimeInvoke(getFoodMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || rowObj == IntPtr.Zero)
                {
                    IntPtr* argsOne = stackalloc IntPtr[1];
                    argsOne[0] = (IntPtr)(&staticId);
                    exc = IntPtr.Zero;
                    rowObj = auraMonoRuntimeInvoke(getFoodMethod, IntPtr.Zero, (IntPtr)argsOne, ref exc);
                }

                if (exc != IntPtr.Zero || rowObj == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryReadMonoIntListMember(rowObj, "groupId", out List<int> groupIds)
                    || groupIds == null
                    || !groupIds.Contains(groupId))
                {
                    return false;
                }

                if (!this.TryReadMonoIntListMember(rowObj, "feedValue", out List<int> feedValues)
                    || feedValues == null
                    || feedValues.Count == 0)
                {
                    return false;
                }

                int index = starRate > 0 && starRate <= 5 ? starRate - 1 : 0;
                index = Mathf.Clamp(index, 0, feedValues.Count - 1);
                fullness = feedValues[index];
                return fullness > 0;
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryGetWildAnimalFoodCandidateAuraMono(
            IntPtr foodObj,
            int groupId,
            HashSet<int> favoriteIds,
            List<int> staticFavorites,
            int favoriteAddition,
            out WildAnimalFeedFoodCandidate candidate,
            out WildAnimalFeedSkipReason skipReason)
        {
            candidate = null;
            skipReason = WildAnimalFeedSkipReason.Invalid;
            if (foodObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoUInt32Member(foodObj, "netId", out uint netId)
                && !this.TryGetMonoUInt32Member(foodObj, "_netId", out netId)
                && !this.TryGetMonoUInt32Member(foodObj, "NetId", out netId))
            {
                return false;
            }

            if (!this.TryGetMonoIntMember(foodObj, "count", out int count)
                && !this.TryGetMonoIntMember(foodObj, "_count", out count)
                && !this.TryGetMonoIntMember(foodObj, "Count", out count))
            {
                return false;
            }

            if (!this.TryGetMonoIntMember(foodObj, "foodFullness", out int fullness)
                && !this.TryGetMonoIntMember(foodObj, "_foodFullness", out fullness)
                && !this.TryGetMonoIntMember(foodObj, "FoodFullness", out fullness))
            {
                return false;
            }

            count = Math.Max(1, count);
            int staticId = 0;
            this.TryGetMonoIntMember(foodObj, "staticId", out staticId);
            if (staticId == 0)
            {
                this.TryGetMonoIntMember(foodObj, "_staticId", out staticId);
            }

            bool isLock = false;
            this.TryGetMonoBoolMember(foodObj, "isLock", out isLock);
            if (!isLock)
            {
                this.TryGetMonoBoolMember(foodObj, "_isLock", out isLock);
            }

            if (isLock)
            {
                skipReason = WildAnimalFeedSkipReason.Lock;
                return false;
            }

            if (staticId <= 0 || netId == 0U || fullness <= 0)
            {
                return false;
            }

            int starRate = 0;
            this.TryGetMonoIntMember(foodObj, "starRate", out starRate);
            if (starRate == 0)
            {
                this.TryGetMonoIntMember(foodObj, "_starRate", out starRate);
            }

            if (!this.IsWildAnimalFeedFoodAllowed(staticId, starRate, string.Empty, out skipReason))
            {
                return false;
            }

            int bondExp = 0;
            this.TryGetWildAnimalFoodBondExp(staticId, starRate, out bondExp);
            bool isFavorite = (favoriteIds != null && favoriteIds.Contains(staticId))
                || (staticFavorites != null && staticFavorites.Contains(staticId));
            candidate = new WildAnimalFeedFoodCandidate
            {
                StaticId = staticId,
                NetId = netId,
                Count = count,
                Fullness = fullness,
                BondExp = bondExp,
                IsFavorite = isFavorite,
                IsLock = isLock,
                SortScore = this.ComputeWildAnimalFeedSortScore(bondExp, fullness, isFavorite, favoriteAddition)
            };
            skipReason = WildAnimalFeedSkipReason.None;
            return true;
        }

        private unsafe bool TryBuildWildAnimalFeedFoodCandidateAuraMono(
            IntPtr item,
            int groupId,
            HashSet<int> favoriteIds,
            List<int> staticFavorites,
            int favoriteAddition,
            out WildAnimalFeedFoodCandidate candidate,
            out WildAnimalFeedSkipReason skipReason)
        {
            candidate = null;
            skipReason = WildAnimalFeedSkipReason.Invalid;
            if (item == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoIntMember(item, "staticId", out int staticId) && !this.TryGetMonoIntMember(item, "_staticId", out staticId))
            {
                return false;
            }

            if (!this.TryGetMonoUInt32Member(item, "netId", out uint netId) && !this.TryGetMonoUInt32Member(item, "_netId", out netId))
            {
                return false;
            }

            int count = 1;
            this.TryGetMonoIntMember(item, "count", out count);
            count = Math.Max(1, count);
            int starRate = 0;
            this.TryGetMonoIntMember(item, "starRate", out starRate);
            if (starRate == 0)
            {
                this.TryGetMonoIntMember(item, "_starRate", out starRate);
            }

            bool isLock = false;
            this.TryGetMonoBoolMember(item, "isLock", out isLock);
            if (!isLock)
            {
                this.TryGetMonoBoolMember(item, "_isLock", out isLock);
            }

            if (isLock)
            {
                skipReason = WildAnimalFeedSkipReason.Lock;
                return false;
            }

            if (!this.TryGetWildAnimalFoodGroupIdsForStaticId(staticId, out HashSet<int> allowedGroups)
                || allowedGroups == null
                || !allowedGroups.Contains(groupId))
            {
                skipReason = WildAnimalFeedSkipReason.NoGroup;
                return false;
            }

            if (!this.TryGetWildAnimalFoodFullnessForGroup(staticId, starRate, groupId, out int fullness))
            {
                skipReason = WildAnimalFeedSkipReason.NoGroup;
                return false;
            }

            if (!this.IsWildAnimalFeedFoodAllowed(staticId, starRate, string.Empty, out skipReason))
            {
                return false;
            }

            int bondExp = 0;
            this.TryGetWildAnimalFoodBondExp(staticId, starRate, out bondExp);
            bool isFavorite = (favoriteIds != null && favoriteIds.Contains(staticId))
                || (staticFavorites != null && staticFavorites.Contains(staticId));
            candidate = new WildAnimalFeedFoodCandidate
            {
                StaticId = staticId,
                NetId = netId,
                Count = count,
                Fullness = fullness,
                BondExp = bondExp,
                IsFavorite = isFavorite,
                IsLock = isLock,
                SortScore = this.ComputeWildAnimalFeedSortScore(bondExp, fullness, isFavorite, favoriteAddition)
            };
            skipReason = WildAnimalFeedSkipReason.None;
            return true;
        }

        private bool IsWildAnimalFeedFoodAllowed(int staticId, int starRate, string itemName, out WildAnimalFeedSkipReason skipReason)
        {
            skipReason = WildAnimalFeedSkipReason.None;
            if (this.wildAnimalFeedSkipFiveStarFood && starRate >= 5)
            {
                skipReason = WildAnimalFeedSkipReason.Star;
                return false;
            }

            if (this.wildAnimalFeedSkipEgg && this.IsWildAnimalFeedEggStaticId(staticId))
            {
                skipReason = WildAnimalFeedSkipReason.Egg;
                return false;
            }

            return true;
        }

        private bool IsWildAnimalFeedEggStaticId(int staticId)
        {
            return staticId > 0 && WildAnimalFeedEggStaticIds.Contains(staticId);
        }

        private string GetWildAnimalFeedEntityDisplayName(int staticId)
        {
            if (staticId <= 0)
            {
                return string.Empty;
            }

            try
            {
                Type tableDataType = this.FindLoadedTypeByFullName("TableData");
                if (tableDataType == null)
                {
                    return string.Empty;
                }

                MethodInfo getEntity = tableDataType.GetMethod("GetEntity", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
                if (getEntity == null)
                {
                    return string.Empty;
                }

                object entity = getEntity.Invoke(null, new object[] { staticId });
                if (entity == null)
                {
                    return string.Empty;
                }

                if (this.TryGetObjectMember(entity, "name", out object nameObj) && nameObj != null)
                {
                    return Convert.ToString(nameObj) ?? string.Empty;
                }

                if (this.TryGetObjectMember(entity, "Name", out nameObj) && nameObj != null)
                {
                    return Convert.ToString(nameObj) ?? string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private string ResolveWildAnimalFeedFoodNameAuraMono(IntPtr foodObj, int staticId)
        {
            if (foodObj != IntPtr.Zero)
            {
                if (this.TryGetMonoStringMember(foodObj, "name", out string name) && !string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }

                if (this.TryGetMonoStringMember(foodObj, "Name", out name) && !string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }

            return this.GetWildAnimalFeedEntityDisplayName(staticId);
        }

        private int ComputeWildAnimalFeedSortScore(int bondExp, int fullness, bool isFavorite, int favoriteAddition)
        {
            int adjustedBond = bondExp;
            if (isFavorite && favoriteAddition > 0)
            {
                adjustedBond = Mathf.RoundToInt(bondExp * (1f + favoriteAddition / 100f));
            }

            return adjustedBond * 10000 + fullness;
        }

        private void ApplyWildAnimalFeedReservation(List<WildAnimalFeedFoodCandidate> candidates, Dictionary<uint, int> reservedCountsByNetId)
        {
            if (candidates == null || candidates.Count == 0 || reservedCountsByNetId == null || reservedCountsByNetId.Count == 0)
            {
                return;
            }

            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                WildAnimalFeedFoodCandidate candidate = candidates[i];
                if (candidate == null || candidate.NetId == 0U)
                {
                    candidates.RemoveAt(i);
                    continue;
                }

                if (reservedCountsByNetId.TryGetValue(candidate.NetId, out int reserved) && reserved >= candidate.Count)
                {
                    candidates.RemoveAt(i);
                    continue;
                }

                if (reserved > 0)
                {
                    candidate.Count = Math.Max(0, candidate.Count - reserved);
                    if (candidate.Count <= 0)
                    {
                        candidates.RemoveAt(i);
                    }
                }
            }
        }

        private void ReserveWildAnimalFeedNetIds(List<uint> foodNetIds, Dictionary<uint, int> reservedCountsByNetId)
        {
            if (foodNetIds == null || reservedCountsByNetId == null)
            {
                return;
            }

            foreach (uint netId in foodNetIds)
            {
                if (netId == 0U)
                {
                    continue;
                }

                if (!reservedCountsByNetId.TryGetValue(netId, out int count))
                {
                    count = 0;
                }

                reservedCountsByNetId[netId] = count + 1;
            }
        }

        private List<uint> SelectWildAnimalFeedFoodNetIds(List<WildAnimalFeedFoodCandidate> candidates, int neededFullness)
        {
            List<uint> netIds = new List<uint>();
            if (candidates == null || candidates.Count == 0 || neededFullness <= 0)
            {
                return netIds;
            }

            candidates.Sort((a, b) => b.SortScore.CompareTo(a.SortScore));
            int remaining = neededFullness;
            foreach (WildAnimalFeedFoodCandidate candidate in candidates)
            {
                if (candidate == null || candidate.NetId == 0U || candidate.Fullness <= 0 || candidate.Count <= 0)
                {
                    continue;
                }

                int uses = Math.Min(candidate.Count, Mathf.CeilToInt(remaining / (float)Math.Max(1, candidate.Fullness)));
                uses = Math.Min(uses, candidate.Count);
                for (int i = 0; i < uses && netIds.Count < WildAnimalFeedMaxItemsPerCommand; i++)
                {
                    netIds.Add(candidate.NetId);
                    remaining -= candidate.Fullness;
                    if (remaining <= 0)
                    {
                        break;
                    }
                }

                if (remaining <= 0 || netIds.Count >= WildAnimalFeedMaxItemsPerCommand)
                {
                    break;
                }
            }

            return netIds;
        }

        private bool TryInvokeWildAnimalFeed(int groupId, List<uint> foodNetIds, out string status)
        {
            status = string.Empty;
            if (groupId <= 0 || foodNetIds == null || foodNetIds.Count == 0)
            {
                status = "empty feed request";
                return false;
            }

            try
            {
                if (!this.EnsureWildAnimalFeedReflection(out status))
                {
                    string managedStatus = status;
                    if (this.TryInvokeWildAnimalFeedAuraMono(groupId, foodNetIds, out status))
                    {
                        return true;
                    }

                    status = managedStatus + ". " + status;
                    return false;
                }

                object groupEnum = Enum.ToObject(this.wildAnimalFeedAnimalGroupType, groupId);
                this.wildAnimalFeedProtocolFeedMethod.Invoke(null, new object[] { groupEnum, foodNetIds });
                return true;
            }
            catch (Exception ex)
            {
                status = (ex.InnerException ?? ex).Message;
                if (this.TryInvokeWildAnimalFeedAuraMono(groupId, foodNetIds, out string auraStatus))
                {
                    return true;
                }

                status = status + ". " + auraStatus;
                return false;
            }
        }

        private unsafe bool TryInvokeWildAnimalFeedAuraMono(int groupId, List<uint> foodNetIds, out string status)
        {
            status = "AuraMono wild Feed unavailable";
            try
            {
                if (!this.TryResolveWildAnimalFeedAuraProtocol(out status))
                {
                    return false;
                }

                if (!this.TryCreatePetFeedAuraUIntList(foodNetIds, out IntPtr foodListObj, out status) || foodListObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&groupId);
                args[1] = foodListObj;
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.wildAnimalFeedAuraFeedMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "AuraMono Feed failed exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                status = "AuraMono Feed ok";
                return true;
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool EnsureWildAnimalFeedReflection(out string status)
        {
            status = string.Empty;
            if (this.wildAnimalFeedProtocolFeedMethod != null
                && this.wildAnimalFeedWildAnimalSystemInstanceProperty != null
                && this.wildAnimalFeedGetUnlockedAnimalsMethod != null
                && this.wildAnimalFeedGetFoodsMethod != null
                && this.wildAnimalFeedAnimalGroupType != null)
            {
                return true;
            }

            if (this.wildAnimalFeedManagedReflectionUnavailable)
            {
                status = string.IsNullOrEmpty(this.wildAnimalFeedManagedReflectionUnavailableStatus)
                    ? "managed wild feed resolver unavailable"
                    : this.wildAnimalFeedManagedReflectionUnavailableStatus;
                return false;
            }

            Type protocolType = this.FindLoadedTypeByFullName("XDTDataAndProtocol.ProtocolService.WildAnimal.WildAnimalProtocolManager")
                ?? this.FindLoadedType("XDTDataAndProtocol.ProtocolService.WildAnimal.WildAnimalProtocolManager", "WildAnimalProtocolManager");
            this.wildAnimalFeedWildAnimalSystemType = this.FindLoadedTypeByFullName("XDTGameSystem.GameplaySystem.WildAnimal.WildAnimalSystem")
                ?? this.FindLoadedType("XDTGameSystem.GameplaySystem.WildAnimal.WildAnimalSystem", "WildAnimalSystem");
            this.wildAnimalFeedAnimalGroupType = this.FindLoadedTypeByFullName("XDT.Scene.Shared.Modules.Animal.AnimalGroup")
                ?? this.FindLoadedType("XDT.Scene.Shared.Modules.Animal.AnimalGroup", "AnimalGroup");
            this.wildAnimalFeedStorageTypeType = this.FindLoadedTypeByFullName("EcsClient.XDT.Scene.Shared.Data.StaticPartial.EStorageType")
                ?? this.FindLoadedType("EcsClient.XDT.Scene.Shared.Data.StaticPartial.EStorageType", "EStorageType");

            if (protocolType == null || this.wildAnimalFeedWildAnimalSystemType == null || this.wildAnimalFeedAnimalGroupType == null || this.wildAnimalFeedStorageTypeType == null)
            {
                List<string> missing = new List<string>();
                if (protocolType == null) missing.Add("WildAnimalProtocolManager");
                if (this.wildAnimalFeedWildAnimalSystemType == null) missing.Add("WildAnimalSystem");
                if (this.wildAnimalFeedAnimalGroupType == null) missing.Add("AnimalGroup");
                if (this.wildAnimalFeedStorageTypeType == null) missing.Add("EStorageType");
                status = "missing type(s): " + string.Join(", ", missing.ToArray());
                this.WildAnimalFeedLog(status);
                this.wildAnimalFeedManagedReflectionUnavailable = true;
                this.wildAnimalFeedManagedReflectionUnavailableStatus = status;
                return false;
            }

            this.wildAnimalFeedProtocolFeedMethod = protocolType.GetMethod(
                "Feed",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { this.wildAnimalFeedAnimalGroupType, typeof(IEnumerable<uint>) },
                null);
            if (this.wildAnimalFeedProtocolFeedMethod == null)
            {
                foreach (MethodInfo method in protocolType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(method.Name, "Feed", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 2
                        && parameters[0].ParameterType == this.wildAnimalFeedAnimalGroupType
                        && parameters[1].ParameterType.IsAssignableFrom(typeof(List<uint>)))
                    {
                        this.wildAnimalFeedProtocolFeedMethod = method;
                        break;
                    }
                }
            }

            this.wildAnimalFeedWildAnimalSystemInstanceProperty = this.wildAnimalFeedWildAnimalSystemType.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                ?? this.GetDataModuleInstanceProperty(this.wildAnimalFeedWildAnimalSystemType);
            this.wildAnimalFeedGetUnlockedAnimalsMethod = this.wildAnimalFeedWildAnimalSystemType.GetMethod("GetUnlockedAnimals", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            this.wildAnimalFeedGetFullnessMethod = this.wildAnimalFeedWildAnimalSystemType.GetMethod("GetFullness", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            this.wildAnimalFeedGetFeedTroughCapacityMethod = this.wildAnimalFeedWildAnimalSystemType.GetMethod("GetFeedTroughCapacity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            this.wildAnimalFeedGetFavoriteFoodMethod = this.wildAnimalFeedWildAnimalSystemType.GetMethod("GetFavoriteFood", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            this.wildAnimalFeedGetFoodsMethod = this.wildAnimalFeedWildAnimalSystemType.GetMethod(
                "GetFoods",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { this.wildAnimalFeedAnimalGroupType, this.wildAnimalFeedStorageTypeType },
                null);

            if (this.wildAnimalFeedProtocolFeedMethod == null
                || this.wildAnimalFeedWildAnimalSystemInstanceProperty == null
                || this.wildAnimalFeedGetUnlockedAnimalsMethod == null
                || this.wildAnimalFeedGetFullnessMethod == null
                || this.wildAnimalFeedGetFeedTroughCapacityMethod == null
                || this.wildAnimalFeedGetFoodsMethod == null)
            {
                List<string> missingMethods = new List<string>();
                if (this.wildAnimalFeedProtocolFeedMethod == null) missingMethods.Add("WildAnimalProtocolManager.Feed");
                if (this.wildAnimalFeedWildAnimalSystemInstanceProperty == null) missingMethods.Add("WildAnimalSystem.Instance");
                if (this.wildAnimalFeedGetUnlockedAnimalsMethod == null) missingMethods.Add("GetUnlockedAnimals");
                if (this.wildAnimalFeedGetFullnessMethod == null) missingMethods.Add("GetFullness");
                if (this.wildAnimalFeedGetFeedTroughCapacityMethod == null) missingMethods.Add("GetFeedTroughCapacity");
                if (this.wildAnimalFeedGetFoodsMethod == null) missingMethods.Add("GetFoods");
                status = "missing method(s): " + string.Join(", ", missingMethods.ToArray());
                this.WildAnimalFeedLog(status);
                this.wildAnimalFeedManagedReflectionUnavailable = true;
                this.wildAnimalFeedManagedReflectionUnavailableStatus = status;
                return false;
            }

            bool backpackOk = this.EnsureWildAnimalFeedBackpackReflection();
            this.WildAnimalFeedLog("Reflection ready. BackpackScan=" + backpackOk
                + " BackPackType=" + (this.wildAnimalFeedBackPackSystemType?.FullName ?? "null"));
            return true;
        }

        private bool TryResolveWildAnimalFeedAuraProtocol(out string status)
        {
            status = string.Empty;
            if (this.wildAnimalFeedAuraFeedMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono protocol API unavailable";
                return false;
            }

            IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.WildAnimal.WildAnimalProtocolManager");
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.WildAnimal", "WildAnimalProtocolManager");
            }

            if (protocolClass == IntPtr.Zero)
            {
                status = "AuraMono WildAnimalProtocolManager unavailable";
                return false;
            }

            this.wildAnimalFeedAuraFeedMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "Feed", 2);
            if (this.wildAnimalFeedAuraFeedMethod == IntPtr.Zero)
            {
                status = "AuraMono WildAnimalProtocolManager.Feed unavailable";
                return false;
            }

            return true;
        }

        private bool TryGetWildAnimalFavoriteFoodIds(object wildAnimalSystem, object groupEnum, int groupId, out HashSet<int> favoriteIds)
        {
            favoriteIds = new HashSet<int>();
            if (this.wildAnimalFeedGetFavoriteFoodMethod == null)
            {
                return false;
            }

            try
            {
                object favoritesObj = this.wildAnimalFeedGetFavoriteFoodMethod.Invoke(wildAnimalSystem, new[] { groupEnum });
                if (favoritesObj != null && this.TryReadIntListObject(favoritesObj, out List<int> favorites))
                {
                    foreach (int favorite in favorites)
                    {
                        favoriteIds.Add(favorite);
                    }
                }
            }
            catch
            {
            }

            this.TryGetWildAnimalGroupMeta(groupId, out List<int> staticFavorites, out _, out _);
            if (staticFavorites != null)
            {
                foreach (int favorite in staticFavorites)
                {
                    favoriteIds.Add(favorite);
                }
            }

            return favoriteIds.Count > 0;
        }

        private bool TryGetWildAnimalGroupMeta(int groupId, out List<int> favoriteFoods, out int favoriteAddition, out string groupName)
        {
            favoriteFoods = null;
            favoriteAddition = 0;
            groupName = string.Empty;
            try
            {
                Type tableDataType = this.FindLoadedTypeByFullName("TableData");
                if (tableDataType == null)
                {
                    return false;
                }

                MethodInfo getAnimalGroup = tableDataType.GetMethod("GetAnimalGroup", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(bool) }, null)
                    ?? tableDataType.GetMethod("GetAnimalGroup", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
                if (getAnimalGroup == null)
                {
                    return false;
                }

                object[] args = getAnimalGroup.GetParameters().Length == 2
                    ? new object[] { groupId, false }
                    : new object[] { groupId };
                object group = getAnimalGroup.Invoke(null, args);
                if (group == null)
                {
                    return false;
                }

                if (this.TryReadIntListFromMember(group, "favoriteFood", out List<int> favorites))
                {
                    favoriteFoods = favorites;
                }

                if (this.TryGetObjectMember(group, "favoriteFoodAddition", out object additionObj) && additionObj != null)
                {
                    favoriteAddition = Convert.ToInt32(additionObj);
                }

                if (this.TryGetObjectMember(group, "groupName", out object nameObj))
                {
                    groupName = Convert.ToString(nameObj) ?? string.Empty;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetWildAnimalFoodBondExp(int staticId, int starRate, out int bondExp)
        {
            bondExp = 0;
            try
            {
                Type tableDataType = this.FindLoadedTypeByFullName("TableData");
                if (tableDataType == null)
                {
                    return false;
                }

                MethodInfo getFood = tableDataType.GetMethod("GetAnimalFoodThough", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
                if (getFood == null)
                {
                    return false;
                }

                object row = getFood.Invoke(null, new object[] { staticId });
                if (row == null)
                {
                    return false;
                }

                if (!this.TryReadIntArrayFromMember(row, "exp", out int[] expValues))
                {
                    return false;
                }

                int index = 0;
                if (starRate > 0 && starRate <= 5)
                {
                    index = starRate - 1;
                }

                if (expValues == null || expValues.Length == 0)
                {
                    return false;
                }

                index = Mathf.Clamp(index, 0, expValues.Length - 1);
                bondExp = expValues[index];
                return bondExp > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadIntArrayFromMember(object target, string memberName, out int[] values)
        {
            values = null;
            if (target == null || !this.TryGetObjectMember(target, memberName, out object raw) || raw == null)
            {
                return false;
            }

            if (raw is int[] direct)
            {
                values = direct;
                return values.Length > 0;
            }

            if (raw is IEnumerable enumerable)
            {
                List<int> list = new List<int>();
                foreach (object item in enumerable)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    list.Add(Convert.ToInt32(item));
                }

                values = list.ToArray();
                return values.Length > 0;
            }

            return false;
        }

        private bool TryEnumerateWildAnimalFeedObjects(object collection, List<object> items)
        {
            if (collection == null || items == null)
            {
                return false;
            }

            items.Clear();
            return this.TryEnumerateManagedCollectionItems(collection, items);
        }

        private bool EnsureWildAnimalFeedBackpackReflection()
        {
            if (this.wildAnimalFeedBackPackGetAllItemMethod != null && this.wildAnimalFeedGetAnimalFoodThoughMethod != null)
            {
                return true;
            }

            this.wildAnimalFeedBackPackSystemType = this.FindLoadedTypeByFullName("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem")
                ?? this.FindLoadedType("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", "BackPackSystem");
            Type tableDataType = this.FindLoadedTypeByFullName("TableData");
            if (this.wildAnimalFeedBackPackSystemType == null || tableDataType == null || this.wildAnimalFeedStorageTypeType == null)
            {
                return false;
            }

            this.wildAnimalFeedGetAnimalFoodThoughMethod = tableDataType.GetMethod(
                "GetAnimalFoodThough",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(int) },
                null);
            if (this.wildAnimalFeedGetAnimalFoodThoughMethod == null)
            {
                this.wildAnimalFeedGetAnimalFoodThoughMethod = tableDataType.GetMethod(
                    "GetAnimalFoodThough",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int), typeof(bool) },
                    null);
            }

            object storageProbe = Enum.Parse(this.wildAnimalFeedStorageTypeType, "Backpack");
            this.wildAnimalFeedBackPackGetAllItemMethod = this.wildAnimalFeedBackPackSystemType.GetMethod(
                "GetAllItem",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { storageProbe.GetType() },
                null);
            if (this.wildAnimalFeedBackPackGetAllItemMethod == null)
            {
                this.wildAnimalFeedBackPackGetAllItemMethod = this.wildAnimalFeedBackPackSystemType.GetMethod(
                    "GetAllItem",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
            }

            return this.wildAnimalFeedGetAnimalFoodThoughMethod != null && this.wildAnimalFeedBackPackGetAllItemMethod != null;
        }

        private object GetWildAnimalFeedBackPackSystemInstance()
        {
            if (this.wildAnimalFeedBackPackSystemType == null)
            {
                return null;
            }

            try
            {
                if (this.TryGetManagedModule(this.wildAnimalFeedBackPackSystemType, out object instance) && instance != null)
                {
                    return instance;
                }

                return this.TryGetStaticObjectAcrossHierarchy(this.wildAnimalFeedBackPackSystemType, "Instance", "_instance");
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetAnimalGroupId(object groupObj, out int groupId)
        {
            groupId = 0;
            if (groupObj == null)
            {
                return false;
            }

            try
            {
                if (groupObj is Enum)
                {
                    groupId = Convert.ToInt32(groupObj);
                    return groupId > 0;
                }

                groupId = Convert.ToInt32(groupObj);
                return groupId > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetWildAnimalFeedGroupIdAuraMono(IntPtr groupObj, out int groupId)
        {
            groupId = 0;
            if (groupObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryUnboxMonoInt32(groupObj, out groupId) && groupId > 0)
            {
                return true;
            }

            if (this.TryGetMonoIntMember(groupObj, "value__", out groupId) && groupId > 0)
            {
                return true;
            }

            return this.TryGetMonoIntMember(groupObj, "_value", out groupId) && groupId > 0;
        }

        private bool TryGetWildAnimalFeedStorageObject(string storageName, out object storage)
        {
            storage = null;
            if (string.IsNullOrEmpty(storageName))
            {
                return false;
            }

            try
            {
                Type storageType = this.wildAnimalFeedStorageTypeType ?? this.FindLoadedTypeByFullName("EcsClient.XDT.Scene.Shared.Data.StaticPartial.EStorageType");
                if (storageType == null || !storageType.IsEnum)
                {
                    return false;
                }

                storage = Enum.Parse(storageType, storageName);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryEnsureWildAnimalFeedAuraStorageValues()
        {
            if (this.wildAnimalFeedAuraStorageValuesResolved)
            {
                return true;
            }

            this.wildAnimalFeedAuraStorageValuesResolved = true;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                this.WildAnimalFeedLog("Storage fallback Backpack=1 Warehouse=2 (AuraMono API unavailable)");
                return true;
            }

            IntPtr storageClass = this.FindAuraMonoClassByFullName("EcsClient.XDT.Scene.Shared.Data.StaticPartial.EStorageType");
            if (storageClass == IntPtr.Zero)
            {
                storageClass = this.FindAuraMonoClassAcrossLoadedAssemblies("EcsClient.XDT.Scene.Shared.Data.StaticPartial", "EStorageType");
            }

            if (storageClass == IntPtr.Zero)
            {
                this.WildAnimalFeedLog("Storage fallback Backpack=1 Warehouse=2 (EStorageType class not found)");
                return true;
            }

            foreach (string storageName in WildAnimalFeedStorageNames)
            {
                if (this.TryReadAuraMonoStaticIntField(storageClass, new[] { storageName }, out int value))
                {
                    this.wildAnimalFeedAuraStorageByName[storageName] = value;
                    this.WildAnimalFeedLog("EStorageType." + storageName + "=" + value);
                }
            }

            return true;
        }

        private bool TryGetWildAnimalFeedStorageValue(string storageName, out int storageValue)
        {
            storageValue = 0;
            if (string.IsNullOrEmpty(storageName))
            {
                return false;
            }

            if (this.TryGetWildAnimalFeedStorageObject(storageName, out object storage) && storage != null)
            {
                try
                {
                    storageValue = Convert.ToInt32(storage);
                    return true;
                }
                catch
                {
                }
            }

            this.TryEnsureWildAnimalFeedAuraStorageValues();
            return this.wildAnimalFeedAuraStorageByName.TryGetValue(storageName, out storageValue);
        }

        private IntPtr TryGetWildAnimalFeedAuraMonoTableDataClass()
        {
            IntPtr ecsImage = this.FindAuraMonoImage(new[] { "EcsClient", "EcsClient.dll" });
            IntPtr tableDataClass = ecsImage != IntPtr.Zero ? auraMonoClassFromName(ecsImage, string.Empty, "TableData") : IntPtr.Zero;
            if (tableDataClass == IntPtr.Zero && ecsImage != IntPtr.Zero)
            {
                tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
            }

            if (tableDataClass == IntPtr.Zero)
            {
                tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
            }

            if (tableDataClass == IntPtr.Zero)
            {
                tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies("EcsClient", "TableData");
            }

            return tableDataClass;
        }

        private string GetWildAnimalGroupDisplayName(int groupId)
        {
            if (this.TryGetWildAnimalGroupMeta(groupId, out _, out _, out string groupName) && !string.IsNullOrWhiteSpace(groupName))
            {
                return groupName;
            }

            return "Group #" + groupId;
        }

        private float DrawWildAnimalFeedSection(float startY)
        {
            float num = startY;
            const float left = 40f;
            const float width = 520f;

            Color textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            labelStyle.normal.textColor = textColor;

            Rect wildRect = new Rect(left, num, width, 198f);
            GUI.Box(wildRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(wildRect, 1f);
            GUI.Label(new Rect(wildRect.x + 16f, wildRect.y + 12f, 220f, 20f), "WILD ANIMAL TROUGHS", labelStyle);

            float rowY = wildRect.y + 40f;
            bool nextFav = this.DrawSwitchToggle(new Rect(wildRect.x + 16f, rowY, 300f, 28f), this.wildAnimalFeedPreferFavorites, this.L("Prefer Favorite Food"));
            if (nextFav != this.wildAnimalFeedPreferFavorites)
            {
                this.wildAnimalFeedPreferFavorites = nextFav;
            }

            rowY += 42f;
            bool nextSkipStar = this.DrawSwitchToggle(new Rect(wildRect.x + 16f, rowY, 300f, 28f), this.wildAnimalFeedSkipFiveStarFood, this.L("Skip 5 Star Food"));
            if (nextSkipStar != this.wildAnimalFeedSkipFiveStarFood)
            {
                this.wildAnimalFeedSkipFiveStarFood = nextSkipStar;
            }

            rowY += 42f;
            bool nextSkipEgg = this.DrawSwitchToggle(new Rect(wildRect.x + 16f, rowY, 280f, 28f), this.wildAnimalFeedSkipEgg, this.L("Skip Egg"));
            if (nextSkipEgg != this.wildAnimalFeedSkipEgg)
            {
                this.wildAnimalFeedSkipEgg = nextSkipEgg;
            }

            num += 210f;
            Rect actionRect = new Rect(left, num, width, 74f);
            GUI.Box(actionRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(actionRect, 1f);

            bool busy = this.wildAnimalFeedCoroutine != null || Time.realtimeSinceStartup < this.wildAnimalFeedBusyUntil;
            GUI.enabled = !busy;
            if (GUI.Button(new Rect(actionRect.x + 16f, actionRect.y + 22f, 200f, 32f), this.L("Feed All Troughs"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartWildAnimalFeedAll(silent: false);
            }
            GUI.enabled = true;

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            statusStyle.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, 0.82f);
            num += 84f;
            GUI.Label(new Rect(left, num, width, 36f), this.wildAnimalFeedLastStatus ?? string.Empty, statusStyle);
            num += 44f;
            return num;
        }

        private void WildAnimalFeedLog(string message)
        {
            if (!WildAnimalFeedLogsEnabled || string.IsNullOrEmpty(message))
            {
                return;
            }

            ModLogger.Msg("[WildAnimalFeed] " + message);
        }

        private void WildAnimalFeedLogDetail(string message)
        {
            if (!WildAnimalFeedLogsEnabled || string.IsNullOrEmpty(message) || this.wildAnimalFeedDetailLogBudget <= 0)
            {
                return;
            }

            this.wildAnimalFeedDetailLogBudget--;
            ModLogger.Msg("[WildAnimalFeed] " + message);
        }

        private void WildAnimalFeedLogToggles()
        {
            this.WildAnimalFeedLog("Toggles preferFav=" + this.wildAnimalFeedPreferFavorites
                + " skip5star=" + this.wildAnimalFeedSkipFiveStarFood
                + " skipEgg=" + this.wildAnimalFeedSkipEgg
                + " eggIds=" + string.Join(",", WildAnimalFeedEggStaticIds.Select(id => id.ToString()).ToArray()));
        }

        private string FormatWildAnimalFeedCollectStats(WildAnimalFeedCollectStats stats)
        {
            if (stats == null)
            {
                return "stats=null";
            }

            return "raw=" + stats.RawItems
                + " ok=" + stats.Accepted
                + " skip5*=" + stats.SkippedStar
                + " skipEgg=" + stats.SkippedEgg
                + " skipFav=" + stats.SkippedFavorite
                + " skipLock=" + stats.SkippedLock
                + " skipInv=" + stats.SkippedInvalid;
        }

        private static string FormatWildAnimalFeedSkipReason(WildAnimalFeedSkipReason reason)
        {
            switch (reason)
            {
                case WildAnimalFeedSkipReason.Star: return "5star";
                case WildAnimalFeedSkipReason.Egg: return "egg";
                case WildAnimalFeedSkipReason.Lock: return "locked";
                case WildAnimalFeedSkipReason.NoGroup: return "wrong-group";
                case WildAnimalFeedSkipReason.Invalid: return "invalid";
                default: return reason.ToString();
            }
        }

        private unsafe void WildAnimalFeedLogRejectAuraMono(int groupId, string source, IntPtr itemObj, WildAnimalFeedSkipReason reason, string extra = null)
        {
            if (!WildAnimalFeedLogsEnabled || this.wildAnimalFeedDetailLogBudget <= 0 || itemObj == IntPtr.Zero)
            {
                return;
            }

            int staticId = 0;
            uint netId = 0;
            int count = 0;
            int fullness = 0;
            int starRate = 0;
            this.TryGetMonoIntMember(itemObj, "staticId", out staticId);
            this.TryGetMonoUInt32Member(itemObj, "netId", out netId);
            this.TryGetMonoIntMember(itemObj, "count", out count);
            this.TryGetMonoIntMember(itemObj, "foodFullness", out fullness);
            this.TryGetMonoIntMember(itemObj, "starRate", out starRate);
            string reasonText = string.IsNullOrEmpty(extra) ? FormatWildAnimalFeedSkipReason(reason) : extra;
            this.WildAnimalFeedLogDetail("REJECT AuraMono group=" + groupId + " src=" + source
                + " reason=" + reasonText
                + " staticId=" + staticId
                + " netId=" + netId
                + " count=" + count
                + " fullness=" + fullness
                + " star=" + starRate);
        }

        private void WildAnimalFeedLogRejectManaged(int groupId, string source, object itemObj, WildAnimalFeedSkipReason reason, string extra = null)
        {
            if (!WildAnimalFeedLogsEnabled || this.wildAnimalFeedDetailLogBudget <= 0 || itemObj == null)
            {
                return;
            }

            int staticId = 0;
            uint netId = 0;
            int count = 0;
            int fullness = 0;
            int starRate = 0;
            this.TryReadIntFromMember(itemObj, "staticId", out staticId);
            if (staticId == 0)
            {
                this.TryReadIntFromMember(itemObj, "StaticId", out staticId);
            }

            this.TryReadUIntFromMember(itemObj, "netId", out netId);
            if (netId == 0U)
            {
                this.TryReadUIntFromMember(itemObj, "NetId", out netId);
            }

            this.TryReadIntFromMember(itemObj, "count", out count);
            this.TryReadIntFromMember(itemObj, "foodFullness", out fullness);
            if (fullness == 0)
            {
                this.TryReadIntFromMember(itemObj, "FoodFullness", out fullness);
            }

            this.TryReadIntFromMember(itemObj, "starRate", out starRate);
            string reasonText = string.IsNullOrEmpty(extra) ? FormatWildAnimalFeedSkipReason(reason) : extra;
            this.WildAnimalFeedLogDetail("REJECT group=" + groupId + " src=" + source
                + " reason=" + reasonText
                + " staticId=" + staticId
                + " netId=" + netId
                + " count=" + count
                + " fullness=" + fullness
                + " star=" + starRate
                + " type=" + itemObj.GetType().Name);
        }
    }
}
