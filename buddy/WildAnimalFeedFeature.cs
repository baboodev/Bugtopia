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
        private static bool WildAnimalFeedLogsEnabled => MasterLogWildAnimalFeed;
        private const int WildAnimalFeedMaxDetailLogsPerPlan = 12;
        private const int WildAnimalFeedFullnessCacheMiss = -1;
        private const float WildAnimalFeedActionCooldownSeconds = 1.5f;
        private const int WildAnimalFeedMaxItemsPerCommand = 100;
        private const float WildAnimalFeedMinEmptyRatio = 0.02f;
        private const int WildAnimalFeedShopEggStaticId = 46102;
        // Guard against the GetFoods GC-storm hang: each candidate inspected boxes ~7 Mono-heap
        // objects (mono_field_get_value_object) + a gchandle pin. A group whose food category is
        // stockpiled by the thousands (dolphin -> fish for an active fisher) would enumerate every
        // stack in Backpack+Warehouse synchronously on the main thread -> tens of thousands of
        // allocations -> SGen GC storm -> 13s main-thread freeze -> hang watchdog kill. Bound the
        // per-group AuraMono candidate collection two ways: WildAnimalFeedGroupBudgetMs is the HARD
        // freeze bound (shared across all of a group's passes); WildAnimalFeedMaxItemsPerGroup is a
        // secondary per-pass item allowance (reset between passes, see ResetWildAnimalFeedGroupItemBudget).
        // Both sit far above what any trough actually needs (a trough fills from a handful of
        // high-value foods), so normal feeding is unaffected; only pathological inventories truncate.
        private const int WildAnimalFeedMaxItemsPerGroup = 1500;
        private const int WildAnimalFeedGroupBudgetMs = 1500;
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

        // Per-group AuraMono candidate-collection budget (see WildAnimalFeedMaxItemsPerGroup). Set at
        // the top of CollectWildAnimalFeedCandidatesAuraMono and read in the item loop. Safe as shared
        // instance state: feed-all is a single coroutine and this collection runs synchronously on the
        // main thread with no reentrancy.
        private int wildAnimalFeedGroupItemsInspected;
        private int wildAnimalFeedGroupDeadlineTick;
        private bool wildAnimalFeedGroupBudgetTripped;

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
        private Type wildAnimalFeedTableDataType = null;
        private MethodInfo wildAnimalFeedGetAnimalGroupMethod = null;
        private MethodInfo wildAnimalFeedGetEntityMethod = null;
        private bool wildAnimalFeedTableDataReflectionResolved = false;
        private IntPtr wildAnimalFeedAuraTableDataClass = IntPtr.Zero;
        private IntPtr wildAnimalFeedAuraGetAnimalFoodThoughMethod = IntPtr.Zero;
        private IntPtr wildAnimalFeedAuraGetAnimalGroupMethod = IntPtr.Zero;
        private bool wildAnimalFeedAuraTableDataCacheAttempted = false;
        private IntPtr wildAnimalFeedAuraGameTimeClass = IntPtr.Zero;
        private IntPtr wildAnimalFeedAuraGameTimeCheckPeriodMethod = IntPtr.Zero;
        private IntPtr wildAnimalFeedAuraGetCurrentGameTimeMsMethod = IntPtr.Zero;
        private IntPtr wildAnimalFeedAuraIsTimeInPeriodMethod = IntPtr.Zero;
        private IntPtr wildAnimalFeedAuraGetDateMethod = IntPtr.Zero;

        private sealed class WildAnimalFeedAuraInventorySnapshot
        {
            public readonly Dictionary<string, List<IntPtr>> ItemsByStorage = new Dictionary<string, List<IntPtr>>(StringComparer.OrdinalIgnoreCase);

            // Pinned gchandles rooting every pointer in ItemsByStorage. Taken DURING the
            // enumeration walk (see TryBuildWildAnimalFeedAuraInventorySnapshot) — that is the only
            // moment the pointers are known good. Released by ReleaseWildAnimalFeedAuraPlanContext.
            public readonly List<uint> Pins = new List<uint>();
            public bool Ready;
        }

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

            // Pinned gchandles rooting WildAnimalSystemObj and every GroupItems entry, taken while
            // the objects are still where they were found. Pinning AFTER the walk is a no-op — the
            // rows have already moved — which is what killed the roster (WER xdt.exe.21532,
            // 2026-08-28: mono_object_get_class on a recycled nursery block). Release with
            // ReleaseWildAnimalFeedAuraPlanContext.
            public readonly List<uint> Pins = new List<uint>();
            public Dictionary<uint, int> ReservedCountsByNetId = new Dictionary<uint, int>();
            public int CheckedGroups;
            public int HungryGroups;
            public WildAnimalFeedAuraInventorySnapshot Inventory;
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
                FeatureLog.Fail("WildAnimalFeed", "plan failed: " + status);
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
                        FeatureLog.Fail("WildAnimalFeed", "feed failed group=" + plan.GroupId + " (" + plan.GroupName + "): " + status);
                        yield return ModWait.Realtime(0.35f);
                        continue;
                    }

                    fed++;
                    this.WildAnimalFeedLog("Fed group=" + plan.GroupId + " " + plan.GroupName
                        + " +" + plan.FoodNetIds.Count + " items"
                        + " fullness " + plan.CurrentFullness + "/" + plan.MaxFullness
                        + " need=" + plan.NeededFullness);
                    yield return ModWait.Realtime(0.55f);
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

        private bool EnsureWildAnimalFeedTableDataReflection()
        {
            if (this.wildAnimalFeedTableDataReflectionResolved)
            {
                return this.wildAnimalFeedTableDataType != null;
            }

            this.wildAnimalFeedTableDataReflectionResolved = true;
            this.wildAnimalFeedTableDataType = this.FindLoadedTypeByFullName("EcsClient.TableData")
                ?? this.FindLoadedType("EcsClient.TableData", "TableData")
                ?? this.FindLoadedTypeByFullName("TableData");
            if (this.wildAnimalFeedTableDataType == null)
            {
                return false;
            }

            if (this.wildAnimalFeedGetAnimalFoodThoughMethod == null)
            {
                this.wildAnimalFeedGetAnimalFoodThoughMethod = this.wildAnimalFeedTableDataType.GetMethod(
                    "GetAnimalFoodThough",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int) },
                    null);
                if (this.wildAnimalFeedGetAnimalFoodThoughMethod == null)
                {
                    this.wildAnimalFeedGetAnimalFoodThoughMethod = this.wildAnimalFeedTableDataType.GetMethod(
                        "GetAnimalFoodThough",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(int), typeof(bool) },
                        null);
                }
            }

            if (this.wildAnimalFeedGetAnimalGroupMethod == null)
            {
                this.wildAnimalFeedGetAnimalGroupMethod = this.wildAnimalFeedTableDataType.GetMethod(
                    "GetAnimalGroup",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int), typeof(bool) },
                    null)
                    ?? this.wildAnimalFeedTableDataType.GetMethod(
                        "GetAnimalGroup",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(int) },
                        null);
            }

            if (this.wildAnimalFeedGetEntityMethod == null)
            {
                this.wildAnimalFeedGetEntityMethod = this.wildAnimalFeedTableDataType.GetMethod(
                    "GetEntity",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int) },
                    null);
            }

            return true;
        }

        private unsafe bool EnsureWildAnimalFeedAuraTableDataCache()
        {
            if (this.wildAnimalFeedAuraGetAnimalFoodThoughMethod != IntPtr.Zero)
            {
                return true;
            }

            if (this.wildAnimalFeedAuraTableDataCacheAttempted)
            {
                return false;
            }

            this.wildAnimalFeedAuraTableDataCacheAttempted = true;
            this.wildAnimalFeedAuraTableDataClass = this.ResolveWildAnimalFeedAuraMonoTableDataClassUncached();
            if (this.wildAnimalFeedAuraTableDataClass == IntPtr.Zero)
            {
                return false;
            }

            this.wildAnimalFeedAuraGetAnimalFoodThoughMethod = this.FindAuraMonoMethodOnHierarchy(
                this.wildAnimalFeedAuraTableDataClass,
                "GetAnimalFoodThough",
                1);
            if (this.wildAnimalFeedAuraGetAnimalFoodThoughMethod == IntPtr.Zero)
            {
                this.wildAnimalFeedAuraGetAnimalFoodThoughMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.wildAnimalFeedAuraTableDataClass,
                    "GetAnimalFoodThough",
                    2);
            }

            this.TryResolveWildAnimalFeedAuraGetAnimalGroupMethod();
            this.TryResolveWildAnimalFeedAuraGetDateMethod();
            return this.wildAnimalFeedAuraGetAnimalFoodThoughMethod != IntPtr.Zero;
        }

        private bool EnsureWildAnimalFeedAuraTableDataClassResolved()
        {
            if (this.wildAnimalFeedAuraTableDataClass != IntPtr.Zero)
            {
                return true;
            }

            if (!this.wildAnimalFeedAuraTableDataCacheAttempted)
            {
                this.EnsureWildAnimalFeedAuraTableDataCache();
            }

            if (this.wildAnimalFeedAuraTableDataClass != IntPtr.Zero)
            {
                return true;
            }

            if (this.wildAnimalFeedAuraTableDataCacheAttempted)
            {
                return false;
            }

            this.wildAnimalFeedAuraTableDataCacheAttempted = true;
            this.wildAnimalFeedAuraTableDataClass = this.ResolveWildAnimalFeedAuraMonoTableDataClassUncached();
            if (this.wildAnimalFeedAuraTableDataClass != IntPtr.Zero)
            {
                this.TryResolveWildAnimalFeedAuraGetAnimalGroupMethod();
            }

            return this.wildAnimalFeedAuraTableDataClass != IntPtr.Zero;
        }

        private bool TryResolveWildAnimalFeedAuraGetAnimalGroupMethod()
        {
            if (this.wildAnimalFeedAuraGetAnimalGroupMethod != IntPtr.Zero
                || this.wildAnimalFeedAuraTableDataClass == IntPtr.Zero)
            {
                return this.wildAnimalFeedAuraGetAnimalGroupMethod != IntPtr.Zero;
            }

            this.wildAnimalFeedAuraGetAnimalGroupMethod = this.FindAuraMonoMethodOnHierarchy(
                this.wildAnimalFeedAuraTableDataClass,
                "GetAnimalGroup",
                2);
            if (this.wildAnimalFeedAuraGetAnimalGroupMethod == IntPtr.Zero)
            {
                this.wildAnimalFeedAuraGetAnimalGroupMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.wildAnimalFeedAuraTableDataClass,
                    "GetAnimalGroup",
                    1);
            }

            this.TryResolveWildAnimalFeedAuraGetDateMethod();
            return this.wildAnimalFeedAuraGetAnimalGroupMethod != IntPtr.Zero;
        }

        private bool EnsureWildAnimalFeedAuraGetAnimalGroupMethod()
        {
            if (this.wildAnimalFeedAuraGetAnimalGroupMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureWildAnimalFeedAuraTableDataClassResolved())
            {
                return false;
            }

            return this.TryResolveWildAnimalFeedAuraGetAnimalGroupMethod();
        }

        private unsafe bool TryInvokeWildAnimalFeedGetAnimalGroupAuraMono(int groupId, out IntPtr groupObj)
        {
            groupObj = IntPtr.Zero;
            if (groupId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null
                || !this.EnsureWildAnimalFeedAuraGetAnimalGroupMethod())
            {
                return false;
            }

            bool needException = false;
            IntPtr exc = IntPtr.Zero;
            if (this.wildAnimalFeedAuraGetAnimalGroupMethod != IntPtr.Zero)
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&groupId);
                args[1] = (IntPtr)(&needException);
                groupObj = auraMonoRuntimeInvoke(this.wildAnimalFeedAuraGetAnimalGroupMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            if (exc == IntPtr.Zero && groupObj != IntPtr.Zero)
            {
                return true;
            }

            IntPtr oneArgMethod = this.FindAuraMonoMethodOnHierarchy(this.wildAnimalFeedAuraTableDataClass, "GetAnimalGroup", 1);
            if (oneArgMethod == IntPtr.Zero)
            {
                groupObj = IntPtr.Zero;
                return false;
            }

            exc = IntPtr.Zero;
            IntPtr* oneArg = stackalloc IntPtr[1];
            oneArg[0] = (IntPtr)(&groupId);
            groupObj = auraMonoRuntimeInvoke(oneArgMethod, IntPtr.Zero, (IntPtr)oneArg, ref exc);
            return exc == IntPtr.Zero && groupObj != IntPtr.Zero;
        }


        private unsafe bool TryInvokeWildAnimalFeedGetAnimalFoodThoughAuraMono(int staticId, out IntPtr rowObj)
        {
            rowObj = IntPtr.Zero;
            if (staticId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null
                || !this.EnsureWildAnimalFeedAuraTableDataCache())
            {
                return false;
            }

            try
            {
                bool needException = false;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&staticId);
                args[1] = (IntPtr)(&needException);
                IntPtr exc = IntPtr.Zero;
                rowObj = auraMonoRuntimeInvoke(this.wildAnimalFeedAuraGetAnimalFoodThoughMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || rowObj == IntPtr.Zero)
                {
                    IntPtr* argsOne = stackalloc IntPtr[1];
                    argsOne[0] = (IntPtr)(&staticId);
                    exc = IntPtr.Zero;
                    rowObj = auraMonoRuntimeInvoke(this.wildAnimalFeedAuraGetAnimalFoodThoughMethod, IntPtr.Zero, (IntPtr)argsOne, ref exc);
                }

                return exc == IntPtr.Zero && rowObj != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }


        private unsafe bool TryBuildWildAnimalFeedAuraInventorySnapshot(WildAnimalFeedAuraInventorySnapshot snapshot)
        {
            snapshot.ItemsByStorage.Clear();
            snapshot.Ready = false;
            if (snapshot == null || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackObj)
                || backPackObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr backPackClass = auraMonoObjectGetClass(backPackObj);
            IntPtr getAllItemMethodWithStorage = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
            IntPtr getAllItemMethodNoArgs = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
            if (getAllItemMethodWithStorage == IntPtr.Zero && getAllItemMethodNoArgs == IntPtr.Zero)
            {
                return false;
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
                    continue;
                }

                // Pin as the walk appends: the snapshot outlives this method (it is read per group,
                // across yields), and the get_Item invokes that build it allocate on the Mono heap,
                // so unpinned entries can already be stale by the time the list is returned.
                List<IntPtr> items = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(itemsObj, items, snapshot.Pins))
                {
                    continue;
                }

                snapshot.ItemsByStorage[storageName] = items;
            }

            snapshot.Ready = snapshot.ItemsByStorage.Count > 0;
            return snapshot.Ready;
        }


        private unsafe void AppendWildAnimalFeedCandidatesFromPreScannedBackpackAuraMono(
            int groupId,
            HashSet<int> favoriteIds,
            List<int> staticFavorites,
            int favoriteAddition,
            List<WildAnimalFeedFoodCandidate> candidates,
            HashSet<uint> seenNetIds,
            WildAnimalFeedCollectStats stats,
            WildAnimalFeedAuraInventorySnapshot inventory,
            bool favoritesOnly)
        {
            if (inventory == null || !inventory.Ready)
            {
                return;
            }

            foreach (string storageName in WildAnimalFeedStorageNames)
            {
                if (!inventory.ItemsByStorage.TryGetValue(storageName, out List<IntPtr> backpackItems) || backpackItems == null)
                {
                    continue;
                }

                this.WildAnimalFeedLogDetail("AuraMono PreScan " + storageName + " items=" + backpackItems.Count);
                string source = "AuraPreScan/" + storageName + (favoritesOnly ? "/favOnly" : "/all");
                foreach (IntPtr item in backpackItems)
                {
                    // Snapshot items are pinned, but the per-item member reads still box on the Mono
                    // heap — same GC-storm bound as the live paths.
                    if (this.WildAnimalFeedGroupBudgetExhausted())
                    {
                        break;
                    }

                    this.wildAnimalFeedGroupItemsInspected++;
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

        private static long WildAnimalFeedFullnessCacheKey(int staticId, int groupId, int starRate)
        {
            return ((long)staticId << 24) | ((long)groupId << 8) | (uint)(starRate & 0xFF);
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

            // Everything this loop holds across a yield is already rooted: the context pinned the
            // system object and the group rows during the GetUnlockedAnimals walk, and the snapshot
            // pins its items as it builds them. Both are released in the finally below. (Pinning
            // here instead — after the walks — was the bug: by then the rows had already moved.)
            try
            {
                context.Inventory = new WildAnimalFeedAuraInventorySnapshot();
                if (this.TryBuildWildAnimalFeedAuraInventorySnapshot(context.Inventory))
                {
                    this.WildAnimalFeedLog("AuraMono inventory snapshot: "
                        + string.Join(", ", WildAnimalFeedStorageNames.Select(name =>
                            name + "=" + (context.Inventory.ItemsByStorage.TryGetValue(name, out List<IntPtr> bucket) ? bucket.Count : 0)).ToArray()));
                }
                else
                {
                    this.WildAnimalFeedLog("AuraMono inventory snapshot unavailable");
                }

                yield return null;

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
            }
            finally
            {
                this.ReleaseWildAnimalFeedAuraPlanContext(context);
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
            // Pin BEFORE the walk, never after it. GetUnlockedAnimals returns boxed group ids, so
            // every get_Item / get_Current inside the enumerate allocates on the Mono heap, and that
            // allocation can trigger an sgen collection which relocates the rows already appended to
            // GroupItems. A pin taken afterwards roots an address the object has left, and the first
            // member read on it is a native AV.
            context.Pins.Add(AuraMonoPinNew(wildAnimalSystemObj));
            if (!this.TryEnumerateAuraMonoCollectionItems(groupsObj, context.GroupItems, context.Pins) || context.GroupItems.Count == 0)
            {
                status = "AuraMono unlocked groups empty";
                this.ReleaseWildAnimalFeedAuraPlanContext(context);
                context = null;
                return false;
            }

            status = string.Empty;
            return true;
        }

        // Frees every gchandle the context and its inventory snapshot hold. Idempotent (FreeAuraMonoPins
        // clears the list), so it is safe to call from a finally that may run after an early release.
        private void ReleaseWildAnimalFeedAuraPlanContext(WildAnimalFeedAuraPlanContext context)
        {
            if (context == null)
            {
                return;
            }

            FreeAuraMonoPins(context.Pins);
            if (context.Inventory != null)
            {
                FreeAuraMonoPins(context.Inventory.Pins);
            }
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

            if (this.ShouldSkipWildAnimalFeedGroupOffIsland(groupId))
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
                collectStats,
                context.Inventory);
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

        // Starts a group's budget. The wall-clock deadline is the HARD freeze bound (shared across
        // every pass for this group, so total main-thread stall stays <= WildAnimalFeedGroupBudgetMs);
        // the item counter is a secondary per-pass fairness cap, reset between passes so the favOnly
        // pass can't burn the whole allowance rejecting non-favorites and starve the "all"/backpack
        // fallbacks. The fallbacks still only run while wall-clock time remains, so the hang can't
        // return.
        private void BeginWildAnimalFeedGroupBudget()
        {
            this.wildAnimalFeedGroupItemsInspected = 0;
            this.wildAnimalFeedGroupDeadlineTick = unchecked(Environment.TickCount + WildAnimalFeedGroupBudgetMs);
            this.wildAnimalFeedGroupBudgetTripped = false;
        }

        // Reset only the per-pass item allowance; the shared wall-clock deadline (the real freeze
        // bound) is left intact, so an exhausted deadline still short-circuits the next pass.
        private void ResetWildAnimalFeedGroupItemBudget()
        {
            this.wildAnimalFeedGroupItemsInspected = 0;
        }

        // True once this group has inspected its item cap or blown its wall-clock budget. Checked in
        // the per-item extraction loop so a stockpiled food category (dolphin -> fish) can never
        // freeze the frame past ~1.5s. Environment.TickCount wraps at ~24.9 days; the subtraction is
        // wrap-safe because the deadline is only ~1.5s ahead.
        private bool WildAnimalFeedGroupBudgetExhausted()
        {
            if (this.wildAnimalFeedGroupItemsInspected >= WildAnimalFeedMaxItemsPerGroup
                || unchecked(Environment.TickCount - this.wildAnimalFeedGroupDeadlineTick) >= 0)
            {
                if (!this.wildAnimalFeedGroupBudgetTripped)
                {
                    this.wildAnimalFeedGroupBudgetTripped = true;
                    this.WildAnimalFeedLogDetail("AuraMono candidate budget hit after "
                        + this.wildAnimalFeedGroupItemsInspected + " items — truncating (large food inventory).");
                }

                return true;
            }

            return false;
        }

        private int WildAnimalFeedGroupRemainingItemBudget()
        {
            int remaining = WildAnimalFeedMaxItemsPerGroup - this.wildAnimalFeedGroupItemsInspected;
            return remaining < 0 ? 0 : remaining;
        }

        private unsafe List<WildAnimalFeedFoodCandidate> CollectWildAnimalFeedCandidatesAuraMono(
            IntPtr wildAnimalSystemObj,
            IntPtr getFoodsMethod,
            int groupId,
            HashSet<int> favoriteIds,
            WildAnimalFeedCollectStats stats,
            WildAnimalFeedAuraInventorySnapshot inventory)
        {
            List<WildAnimalFeedFoodCandidate> candidates = new List<WildAnimalFeedFoodCandidate>();
            HashSet<uint> seenNetIds = new HashSet<uint>();
            this.TryGetWildAnimalGroupMeta(groupId, out List<int> staticFavorites, out int favoriteAddition, out _);
            if (stats == null)
            {
                stats = new WildAnimalFeedCollectStats();
            }

            this.BeginWildAnimalFeedGroupBudget();
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
                this.ResetWildAnimalFeedGroupItemBudget();
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
                this.ResetWildAnimalFeedGroupItemBudget();
                this.AppendWildAnimalFeedCandidatesFromBackpackAuraMono(
                    groupId,
                    favoriteIds,
                    staticFavorites,
                    favoriteAddition,
                    candidates,
                    seenNetIds,
                    stats,
                    inventory,
                    favoritesOnly: this.wildAnimalFeedPreferFavorites);
                this.WildAnimalFeedLogDetail("AuraMono group " + groupId + " Backpack: +" + (candidates.Count - before));

                if (candidates.Count == 0 && this.wildAnimalFeedPreferFavorites)
                {
                    before = candidates.Count;
                    this.ResetWildAnimalFeedGroupItemBudget();
                    this.AppendWildAnimalFeedCandidatesFromBackpackAuraMono(
                        groupId,
                        favoriteIds,
                        staticFavorites,
                        favoriteAddition,
                        candidates,
                        seenNetIds,
                        stats,
                        inventory,
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
            WildAnimalFeedAuraInventorySnapshot inventory,
            bool favoritesOnly)
        {
            if (inventory != null && inventory.Ready)
            {
                this.AppendWildAnimalFeedCandidatesFromPreScannedBackpackAuraMono(
                    groupId,
                    favoriteIds,
                    staticFavorites,
                    favoriteAddition,
                    candidates,
                    seenNetIds,
                    stats,
                    inventory,
                    favoritesOnly);
                return;
            }

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

                // Same GC-storm guard as the GetFoods path: this enumerates the WHOLE inventory
                // (GetAllItem, not just foods), so a big warehouse would freeze the frame outright.
                if (this.WildAnimalFeedGroupBudgetExhausted())
                {
                    break;
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

                // Pin as the walk appends. The enumerate itself is what moves things: one get_Item
                // invoke per element, each boxing on the Mono heap, so an sgen collection partway
                // through relocates the entries already in the list. The read loop below then
                // dereferences them, which is a native AV.
                List<IntPtr> backpackItems = new List<IntPtr>();
                List<uint> backpackPins = new List<uint>();
                try
                {
                    if (!this.TryEnumerateAuraMonoCollectionItems(itemsObj, backpackItems, backpackPins, this.WildAnimalFeedGroupRemainingItemBudget()))
                    {
                        this.WildAnimalFeedLogDetail("AuraMono Backpack " + storageName + " enumerate failed");
                        continue;
                    }

                    this.WildAnimalFeedLogDetail("AuraMono Backpack " + storageName + " items=" + backpackItems.Count);
                    string source = "AuraBackpack/" + storageName + (favoritesOnly ? "/favOnly" : "/all");
                    foreach (IntPtr item in backpackItems)
                    {
                        if (this.WildAnimalFeedGroupBudgetExhausted())
                        {
                            break;
                        }

                        this.wildAnimalFeedGroupItemsInspected++;
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
                finally
                {
                    FreeAuraMonoPins(backpackPins);
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
            // Already spent this group's per-pass allowance (or the shared wall-clock deadline) on an
            // earlier storage — don't even enumerate, just bail.
            if (this.WildAnimalFeedGroupBudgetExhausted())
            {
                return;
            }

            // Cap the enumerate at the remaining item budget so the enumerate loop itself (a get_Item
            // invoke per element) can't run the full 8192 on a stockpiled category. Pins are taken
            // as the walk appends: each get_Item boxes on the Mono heap, so a collection partway
            // through would relocate the entries already listed and the read loop below would
            // dereference stale pointers (native AV). Staying synchronous is still required — do
            // not add yields inside the loop — but it was never sufficient on its own.
            List<IntPtr> foodItems = new List<IntPtr>();
            List<uint> foodPins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(foodsObj, foodItems, foodPins, this.WildAnimalFeedGroupRemainingItemBudget()))
                {
                    this.WildAnimalFeedLogDetail("AuraMono " + source + " enumerate FAILED");
                    return;
                }

                foreach (IntPtr foodObj in foodItems)
                {
                    if (this.WildAnimalFeedGroupBudgetExhausted())
                    {
                        break;
                    }

                    this.wildAnimalFeedGroupItemsInspected++;
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
            finally
            {
                FreeAuraMonoPins(foodPins);
            }
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
            if (staticId <= 0 || groupIds == null)
            {
                return false;
            }

            try
            {
                if (!this.TryInvokeWildAnimalFeedGetAnimalFoodThoughAuraMono(staticId, out IntPtr rowObj))
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
            if (staticId <= 0 || groupId <= 0)
            {
                return false;
            }

            try
            {
                if (!this.TryInvokeWildAnimalFeedGetAnimalFoodThoughAuraMono(staticId, out IntPtr rowObj))
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

            // bondExp feeds ComputeWildAnimalFeedSortScore below. Its only reader was a managed
            // TableData.GetAnimalFoodThough reflection path, which never resolved on this build, so
            // this has always been 0 in practice. Left at 0 rather than pretending otherwise; an
            // AuraMono reader for the row's "exp" array (mirror TryGetWildAnimalFoodFullnessFor-
            // GroupAuraMono, which already reads "feedValue" from the same row) would restore it.
            int bondExp = 0;
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

            // bondExp feeds ComputeWildAnimalFeedSortScore below. Its only reader was a managed
            // TableData.GetAnimalFoodThough reflection path, which never resolved on this build, so
            // this has always been 0 in practice. Left at 0 rather than pretending otherwise; an
            // AuraMono reader for the row's "exp" array (mirror TryGetWildAnimalFoodFullnessFor-
            // GroupAuraMono, which already reads "feedValue" from the same row) would restore it.
            int bondExp = 0;
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

            this.wildAnimalFeedProtocolFeedMethod = this.GetMethodQuiet(
                protocolType,
                "Feed",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                new[] { this.wildAnimalFeedAnimalGroupType, typeof(IEnumerable<uint>) });
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
            this.wildAnimalFeedGetUnlockedAnimalsMethod = this.GetMethodQuiet(
                this.wildAnimalFeedWildAnimalSystemType,
                "GetUnlockedAnimals",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                Type.EmptyTypes);
            this.wildAnimalFeedGetFullnessMethod = this.GetMethodQuiet(
                this.wildAnimalFeedWildAnimalSystemType,
                "GetFullness",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                Type.EmptyTypes);
            this.wildAnimalFeedGetFeedTroughCapacityMethod = this.GetMethodQuiet(
                this.wildAnimalFeedWildAnimalSystemType,
                "GetFeedTroughCapacity",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                Type.EmptyTypes);
            this.wildAnimalFeedGetFavoriteFoodMethod = this.GetMethodQuiet(
                this.wildAnimalFeedWildAnimalSystemType,
                "GetFavoriteFood",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                Type.EmptyTypes);
            this.wildAnimalFeedGetFoodsMethod = this.GetMethodQuiet(
                this.wildAnimalFeedWildAnimalSystemType,
                "GetFoods",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                new[] { this.wildAnimalFeedAnimalGroupType, this.wildAnimalFeedStorageTypeType });

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


        private bool TryIsWildAnimalGroupOnIslandForFeed(int groupId, out bool onIsland)
        {
            onIsland = true;
            if (!this.TryGetWildAnimalGroupAppearTime(groupId, out int appearTime))
            {
                return false;
            }

            if (appearTime <= 0)
            {
                return true;
            }

            if (this.TryGameTimeCheckInSpecifiedTimePeriod(appearTime, out bool inPeriod))
            {
                onIsland = inPeriod;
                return true;
            }

            onIsland = false;
            return true;
        }

        private bool ShouldSkipWildAnimalFeedGroupOffIsland(int groupId)
        {
            return this.TryIsWildAnimalGroupOnIslandForFeed(groupId, out bool onIsland) && !onIsland;
        }

        private bool TryGetWildAnimalGroupAppearTime(int groupId, out int appearTime)
        {
            return this.TryGetWildAnimalGroupAppearTimeAuraMono(groupId, out appearTime);
        }


        private unsafe bool TryGetWildAnimalGroupAppearTimeAuraMono(int groupId, out int appearTime)
        {
            appearTime = 0;
            if (!this.TryInvokeWildAnimalFeedGetAnimalGroupAuraMono(groupId, out IntPtr groupObj))
            {
                return false;
            }

            this.TryGetMonoIntMember(groupObj, "appearTime", out appearTime);
            return true;
        }

        private bool TryGameTimeCheckInSpecifiedTimePeriod(int periodId, out bool inPeriod)
        {
            inPeriod = false;
            if (periodId <= 0)
            {
                inPeriod = true;
                return true;
            }

            if (this.TryGameTimeCheckInSpecifiedTimePeriodAuraMono(periodId, out inPeriod))
            {
                return true;
            }

            return this.TryGameTimeCheckInSpecifiedTimePeriodAuraMonoFallback(periodId, out inPeriod);
        }


        private bool EnsureWildAnimalFeedAuraGameTimeClass()
        {
            if (this.wildAnimalFeedAuraGameTimeClass != IntPtr.Zero)
            {
                return true;
            }

            this.wildAnimalFeedAuraGameTimeClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.GameTimeUtility");
            if (this.wildAnimalFeedAuraGameTimeClass != IntPtr.Zero)
            {
                return true;
            }

            IntPtr dataImage = this.FindAuraMonoImage(new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
            if (dataImage != IntPtr.Zero)
            {
                this.wildAnimalFeedAuraGameTimeClass = auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService", "GameTimeUtility");
                if (this.wildAnimalFeedAuraGameTimeClass == IntPtr.Zero)
                {
                    this.wildAnimalFeedAuraGameTimeClass = auraMonoClassFromName(dataImage, string.Empty, "GameTimeUtility");
                }
            }

            if (this.wildAnimalFeedAuraGameTimeClass == IntPtr.Zero)
            {
                this.wildAnimalFeedAuraGameTimeClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                    "XDTDataAndProtocol.ProtocolService",
                    "GameTimeUtility");
            }

            return this.wildAnimalFeedAuraGameTimeClass != IntPtr.Zero;
        }

        private bool EnsureWildAnimalFeedAuraGameTimeCheckPeriodMethod()
        {
            if (this.wildAnimalFeedAuraGameTimeCheckPeriodMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureWildAnimalFeedAuraGameTimeClass())
            {
                return false;
            }

            this.wildAnimalFeedAuraGameTimeCheckPeriodMethod = this.FindAuraMonoMethodOnHierarchy(
                this.wildAnimalFeedAuraGameTimeClass,
                "CheckInSpecifiedTimePeriod",
                1);
            return this.wildAnimalFeedAuraGameTimeCheckPeriodMethod != IntPtr.Zero;
        }

        private bool TryResolveWildAnimalFeedAuraGetDateMethod()
        {
            if (this.wildAnimalFeedAuraGetDateMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureWildAnimalFeedAuraTableDataClassResolved())
            {
                return false;
            }

            this.wildAnimalFeedAuraGetDateMethod = this.FindAuraMonoMethodOnHierarchy(
                this.wildAnimalFeedAuraTableDataClass,
                "GetDate",
                2);
            if (this.wildAnimalFeedAuraGetDateMethod == IntPtr.Zero)
            {
                this.wildAnimalFeedAuraGetDateMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.wildAnimalFeedAuraTableDataClass,
                    "GetDate",
                    1);
            }

            return this.wildAnimalFeedAuraGetDateMethod != IntPtr.Zero;
        }

        private unsafe bool TryGameTimeCheckInSpecifiedTimePeriodAuraMono(int periodId, out bool inPeriod)
        {
            inPeriod = false;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null
                || !this.EnsureWildAnimalFeedAuraGameTimeCheckPeriodMethod())
            {
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&periodId);
            IntPtr exc = IntPtr.Zero;
            IntPtr resultObj = auraMonoRuntimeInvoke(this.wildAnimalFeedAuraGameTimeCheckPeriodMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || resultObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(resultObj, out inPeriod);
        }

        private unsafe bool TryGameTimeCheckInSpecifiedTimePeriodAuraMonoFallback(int periodId, out bool inPeriod)
        {
            inPeriod = false;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null
                || !this.EnsureWildAnimalFeedAuraGameTimeClass()
                || !this.TryResolveWildAnimalFeedAuraGetDateMethod())
            {
                return false;
            }

            if (this.wildAnimalFeedAuraGetCurrentGameTimeMsMethod == IntPtr.Zero)
            {
                this.wildAnimalFeedAuraGetCurrentGameTimeMsMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.wildAnimalFeedAuraGameTimeClass,
                    "GetCurrentGameTimeMs",
                    0);
            }

            if (this.wildAnimalFeedAuraIsTimeInPeriodMethod == IntPtr.Zero)
            {
                this.wildAnimalFeedAuraIsTimeInPeriodMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.wildAnimalFeedAuraGameTimeClass,
                    "IsTimeInPeriod",
                    2);
            }

            if (this.wildAnimalFeedAuraGetCurrentGameTimeMsMethod == IntPtr.Zero
                || this.wildAnimalFeedAuraIsTimeInPeriodMethod == IntPtr.Zero)
            {
                return false;
            }

            bool needException = false;
            IntPtr* dateArgs = stackalloc IntPtr[2];
            dateArgs[0] = (IntPtr)(&periodId);
            dateArgs[1] = (IntPtr)(&needException);
            IntPtr exc = IntPtr.Zero;
            IntPtr tableDateObj = auraMonoRuntimeInvoke(this.wildAnimalFeedAuraGetDateMethod, IntPtr.Zero, (IntPtr)dateArgs, ref exc);
            if (exc != IntPtr.Zero || tableDateObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(tableDateObj, "date", out IntPtr dateArrayObj) || dateArrayObj == IntPtr.Zero)
            {
                return false;
            }

            exc = IntPtr.Zero;
            IntPtr gameTimeObj = auraMonoRuntimeInvoke(this.wildAnimalFeedAuraGetCurrentGameTimeMsMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || gameTimeObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr* periodArgs = stackalloc IntPtr[2];
            periodArgs[0] = dateArrayObj;
            periodArgs[1] = gameTimeObj;
            exc = IntPtr.Zero;
            IntPtr resultObj = auraMonoRuntimeInvoke(this.wildAnimalFeedAuraIsTimeInPeriodMethod, IntPtr.Zero, (IntPtr)periodArgs, ref exc);
            if (exc != IntPtr.Zero || resultObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(resultObj, out inPeriod);
        }


        private bool TryGetWildAnimalGroupMeta(int groupId, out List<int> favoriteFoods, out int favoriteAddition, out string groupName)
        {
            return this.TryGetWildAnimalGroupMetaAuraMono(groupId, out favoriteFoods, out favoriteAddition, out groupName);
        }


        private unsafe bool TryGetWildAnimalGroupMetaAuraMono(int groupId, out List<int> favoriteFoods, out int favoriteAddition, out string groupName)
        {
            favoriteFoods = null;
            favoriteAddition = 0;
            groupName = string.Empty;
            if (!this.TryInvokeWildAnimalFeedGetAnimalGroupAuraMono(groupId, out IntPtr groupObj))
            {
                return false;
            }

            if (this.TryReadMonoIntListMember(groupObj, "favoriteFood", out List<int> favorites))
            {
                favoriteFoods = favorites;
            }

            this.TryGetMonoIntMember(groupObj, "favoriteFoodAddition", out favoriteAddition);
            if (this.TryGetMonoStringMember(groupObj, "groupName", out string name) && !string.IsNullOrWhiteSpace(name))
            {
                groupName = name;
            }

            return true;
        }




        private bool EnsureWildAnimalFeedBackpackReflection()
        {
            if (this.wildAnimalFeedBackPackGetAllItemMethod != null && this.wildAnimalFeedGetAnimalFoodThoughMethod != null)
            {
                return true;
            }

            this.wildAnimalFeedBackPackSystemType = this.FindLoadedTypeByFullName("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem")
                ?? this.FindLoadedType("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", "BackPackSystem");
            if (this.wildAnimalFeedBackPackSystemType == null || this.wildAnimalFeedStorageTypeType == null
                || !this.EnsureWildAnimalFeedTableDataReflection())
            {
                return false;
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

        private IntPtr ResolveWildAnimalFeedAuraMonoTableDataClassUncached()
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

    }
}
