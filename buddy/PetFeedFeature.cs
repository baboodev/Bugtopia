using MelonLoader;
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
        private const bool PetFeedLogsEnabled = MasterLogPetFeed;
        private const float PetFeedWorldScanRadius = 55f;
        private const float PetFeedProbeCooldownSeconds = 4f;
        private const float PetFeedActionCooldownSeconds = 1.25f;
        private const int PetFeedFoodVisibleRows = 6;
        private const int PetFeedPetVisibleRows = 4;
        private const int PetFeedEntityScanLimit = 650;
        private object petFeedAllCoroutine = null;
        private float petFeedAllBusyUntil = 0f;
        private string petFeedAllActiveLabel = string.Empty;
        private MethodInfo petFeedPrepareMethod = null;
        private MethodInfo petFeedBeginMethod = null;
        private Type petFeedEntityTypeType = null;
        private Type petFeedPetTypeType = null;
        private Type petFeedStorageTypeType = null;
        private Type petFeedPetSystemType = null;
        private PropertyInfo petFeedPetSystemInstanceProperty = null;
        private MethodInfo petFeedGetPetComponentDatasMethod = null;
        private MethodInfo petFeedInitFoodsMethod = null;
        private MethodInfo petFeedInitFoodsForPickerMethod = null;
        private MethodInfo petFeedGetFoodsMethod = null;
        private MethodInfo petFeedGetFoodBackpackItemsMethod = null;
        private MethodInfo petFeedGetEatenFavoriteFoodsMethod = null;
        private bool petFeedManagedReflectionUnavailable = false;
        private string petFeedManagedReflectionUnavailableStatus = string.Empty;
        private IntPtr petFeedAuraPrepareMethod = IntPtr.Zero;
        private IntPtr petFeedAuraBeginMethod = IntPtr.Zero;
        private IntPtr petFeedAuraUIntListClass = IntPtr.Zero;
        private IntPtr petFeedAuraUIntListAddMethod = IntPtr.Zero;
        private int petFeedAuraCatEntityTypeValue = int.MinValue;
        private int petFeedAuraDogEntityTypeValue = int.MinValue;
        private readonly Dictionary<int, string> petFeedFoodNameByStaticId = new Dictionary<int, string>();
        private readonly Dictionary<int, Texture2D> petFeedFoodIconByStaticId = new Dictionary<int, Texture2D>();
        private readonly HashSet<int> petFeedFoodIconLoadAttempted = new HashSet<int>();
        private readonly Dictionary<uint, float> petFeedProbeAttemptedAt = new Dictionary<uint, float>();
        private readonly Dictionary<int, int> petFeedEntityTypeByStaticId = new Dictionary<int, int>();
        private List<PetFeedFoodOption> petFeedFoodOptions = null;
        private bool petFeedFoodDropdownOpen = false;
        private int petFeedFoodDropdownScrollIndex = 0;
        private string petFeedFoodSearchText = string.Empty;
        private bool petFeedFoodScrollbarDragging = false;
        private float petFeedFoodScrollbarDragOffset = 0f;
        private bool petFeedFoodScanInProgress = false;
        private float petFeedNextFoodScanAllowedAt = 0f;
        private float petFeedNextFullBackpackFoodScanAt = 0f;
        private readonly Dictionary<int, int> petFeedFoodFullnessCache = new Dictionary<int, int>();
        private int petFeedSelectedFoodStaticId = 0;
        private string petFeedSelectedFoodName = "Any Food";
        private readonly List<PetFeedTarget> petFeedDetectedPets = new List<PetFeedTarget>();
        private int petFeedPetListScrollIndex = 0;
        private string petFeedPetListStatus = "Scan pets to list cats and dogs.";

        private sealed class PetFeedFoodOption
        {
            public int StaticId;
            public string Name;
            public int Count;
            public int Fullness;
        }

        private sealed class PetFeedFoodSupply
        {
            public uint NetId;
            public int Count;
            public int Fullness;
            public int StaticId;
            public string Name;
            public bool IsLock;
        }

        private sealed class PetFeedUsedFood
        {
            public uint NetId;
            public int Fullness;
            public int StaticId;
            public string Name;
        }

        private sealed class PetFeedTarget
        {
            public uint NetId;
            public int CurrentFullness;
            public int MaxFullness;
            public bool? IsMine;
            public int EntityType;
            public string Source;
            public List<int> FavoriteFoods;
            public List<int> DislikeFoods;
            public string FavoriteSource;
            public bool IsDog;
            public string Name;
            public int BreedId;
            public int FavoriteGroupId;
            public string PetTextureId;
            public string PetAvatarIconKey;
            public Texture2D PetTexture;
            public bool PetTextureLoadAttempted;
        }

        private void StartPetFeedAll(bool dog)
        {
            string label = dog ? "dogs" : "cats";
            if (this.petFeedAllCoroutine != null)
            {
                string activeLabel = string.IsNullOrWhiteSpace(this.petFeedAllActiveLabel) ? "pets" : this.petFeedAllActiveLabel;
                this.AddMenuNotification("Feed all " + activeLabel + " is already running", new Color(0.45f, 0.88f, 1f));
                return;
            }

            if (Time.realtimeSinceStartup < this.petFeedAllBusyUntil)
            {
                float remaining = Mathf.Max(0f, this.petFeedAllBusyUntil - Time.realtimeSinceStartup);
                this.AddMenuNotification("Feed all " + label + ": wait " + remaining.ToString("F1") + "s", new Color(0.45f, 0.88f, 1f));
                return;
            }

            this.petFeedAllActiveLabel = label;
            this.petFeedAllBusyUntil = Time.realtimeSinceStartup + PetFeedActionCooldownSeconds;
            this.petFeedAllCoroutine = MelonCoroutines.Start(this.PetFeedAllStartRoutine(dog));
        }

        private IEnumerator PetFeedAllStartRoutine(bool dog)
        {
            string label = dog ? "dogs" : "cats";
            yield return null;

            if (!this.TryBuildPetFeedPlan(dog, out List<PetFeedTarget> targets, out List<PetFeedFoodSupply> foods, out int visibleCount, out string status))
            {
                this.AddMenuNotification("Feed all " + label + ": " + status, new Color(1f, 0.58f, 0.42f));
                this.PetFeedLog("Plan failed: " + status);
                this.petFeedAllCoroutine = null;
                this.petFeedAllActiveLabel = string.Empty;
                this.petFeedAllBusyUntil = Time.realtimeSinceStartup + PetFeedActionCooldownSeconds;
                yield break;
            }

            if (targets.Count == 0)
            {
                this.AddMenuNotification("Feed all " + label + ": no feedable pets (" + visibleCount + " visible)", new Color(0.45f, 0.88f, 1f));
                this.PetFeedLog("Feed all " + label + " complete: visible=" + visibleCount + " hungry=0 fed=0 skipped=0");
                this.petFeedAllCoroutine = null;
                this.petFeedAllActiveLabel = string.Empty;
                this.petFeedAllBusyUntil = Time.realtimeSinceStartup + PetFeedActionCooldownSeconds;
                yield break;
            }

            IEnumerator routine = this.PetFeedAllRoutine(dog, targets, foods, visibleCount);
            while (routine.MoveNext())
            {
                yield return routine.Current;
            }
        }

        private IEnumerator PetFeedAllRoutine(bool dog, List<PetFeedTarget> targets, List<PetFeedFoodSupply> foods, int visibleCount)
        {
            string label = dog ? "dogs" : "cats";
            string petKind = dog ? "dog" : "cat";
            string status;

            int fed = 0;
            int probed = 0;
            int skipped = 0;
            try
            {
                foreach (PetFeedTarget target in targets)
                {
                    List<PetFeedFoodSupply> orderedFoods = this.GetPetFeedFoodsForTarget(foods, target);
                    int neededFullness = this.GetPetFeedNeededFullness(target, orderedFoods);
                    List<PetFeedUsedFood> usedFoods = this.TakePetFeedFood(orderedFoods, neededFullness);
                    if (usedFoods.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    List<uint> foodNetIds = usedFoods.Select(food => food.NetId).ToList();

                    if (!this.TryInvokePetFeedPrepare(target.NetId, out status))
                    {
                        skipped++;
                        this.PetFeedLog("Feed prepare failed netId=" + target.NetId + ": " + status);
                        yield return new WaitForSecondsRealtime(0.25f);
                        continue;
                    }

                    yield return new WaitForSecondsRealtime(0.18f);

                    if (!this.TryInvokePetFeedBegin(target.NetId, foodNetIds, out status))
                    {
                        skipped++;
                        this.PetFeedLog("Feed begin failed netId=" + target.NetId + ": " + status);
                        yield return new WaitForSecondsRealtime(0.25f);
                        continue;
                    }

                    bool isProbeAttempt = target.IsMine != true && target.CurrentFullness >= target.MaxFullness;
                    if (isProbeAttempt)
                    {
                        this.petFeedProbeAttemptedAt[target.NetId] = Time.realtimeSinceStartup;
                        probed++;
                    }
                    else
                    {
                        fed++;
                    }

                    this.PetFeedLog((isProbeAttempt ? "Probe-fed " : "Fed ") + petKind + " netId=" + target.NetId
                        + " fullness=" + target.CurrentFullness + "/" + target.MaxFullness
                        + this.FormatPetFeedTargetPreferenceStatus(target, usedFoods)
                        + " usedFoods=" + this.FormatPetFeedUsedFoods(usedFoods));
                    yield return new WaitForSecondsRealtime(0.45f);
                }

                this.PetFeedLog("Feed all " + label + " complete: visible=" + visibleCount + " hungry=" + targets.Count + " fed=" + fed + " probed=" + probed + " skipped=" + skipped);
                this.AddMenuNotification(
                    "Feed all " + label + ": fed " + (fed + probed)
                    + (skipped > 0 ? ", skipped " + skipped : string.Empty),
                    new Color(0.45f, 1f, 0.55f));
            }
            finally
            {
                this.petFeedAllCoroutine = null;
                this.petFeedAllActiveLabel = string.Empty;
                this.petFeedAllBusyUntil = Time.realtimeSinceStartup + PetFeedActionCooldownSeconds;
            }
        }

        private bool TryBuildPetFeedPlan(bool dog, out List<PetFeedTarget> targets, out List<PetFeedFoodSupply> foods, out int visibleCount, out string status)
        {
            targets = new List<PetFeedTarget>();
            foods = new List<PetFeedFoodSupply>();
            visibleCount = 0;
            status = string.Empty;

            try
            {
                if (!this.EnsurePetFeedReflection(out status))
                {
                    string managedStatus = status;
                    if (this.TryBuildPetFeedPlanAuraMono(dog, targets, foods, out visibleCount, out status))
                    {
                        return true;
                    }

                    status = managedStatus + ". " + status;
                    return false;
                }

                object petSystem = this.petFeedPetSystemInstanceProperty.GetValue(null, null);
                if (petSystem == null)
                {
                    status = "PetSystem unavailable";
                    return false;
                }

                object entityTypeValue = Enum.Parse(this.petFeedEntityTypeType, dog ? "dog" : "cat");
                int maxFullness = this.GetPetFeedMaxFullness(dog);
                if (maxFullness <= 0)
                {
                    status = "fullness limit unavailable";
                    return false;
                }

                object petListObj = this.petFeedGetPetComponentDatasMethod.Invoke(petSystem, new object[] { entityTypeValue });
                int mineCount = 0;
                int otherCount = 0;
                int unknownOwnerCount = 0;
                if (petListObj is IEnumerable petList)
                {
                    foreach (object petData in petList)
                    {
                        if (!this.TryGetPetFeedTarget(petData, maxFullness, out PetFeedTarget target))
                        {
                            continue;
                        }

                        target.IsDog = dog;
                        this.TryPopulatePetFeedKnownFavoriteFoodsManaged(petSystem, target);
                        visibleCount++;
                        this.CountPetFeedOwner(target, ref mineCount, ref otherCount, ref unknownOwnerCount);
                        if (this.CanAttemptPetFeedTarget(target))
                        {
                            targets.Add(target);
                        }
                    }
                }

                this.petFeedInitFoodsMethod.Invoke(petSystem, new object[] { entityTypeValue });
                object foodListObj = this.petFeedGetFoodsMethod.Invoke(petSystem, null);
                if (foodListObj is IEnumerable foodList)
                {
                    foreach (object foodObj in foodList)
                    {
                        if (this.TryGetPetFeedFoodSupply(foodObj, out PetFeedFoodSupply food) && food.Count > 0 && food.Fullness > 0 && food.NetId != 0U && !food.IsLock)
                        {
                            foods.Add(food);
                        }
                    }
                }

                foods.Sort((a, b) =>
                {
                    int cmp = a.Fullness.CompareTo(b.Fullness);
                    if (cmp != 0) return cmp;
                    return a.StaticId.CompareTo(b.StaticId);
                });
                this.RegisterPetFeedFoodOptions(foods);

                if (targets.Count > 0 && foods.Count == 0)
                {
                    status = "no usable pet food";
                    return false;
                }
                if (!this.ApplyPetFeedSelectedFoodFilter(foods, targets.Count > 0, out string filterStatus))
                {
                    status = filterStatus;
                    return false;
                }

                status = "visible=" + visibleCount + this.FormatPetFeedOwnerCounts(mineCount, otherCount, unknownOwnerCount) + " hungry=" + targets.Count + " foods=" + foods.Count + this.FormatPetFeedSelectedFoodStatus();
                this.PetFeedLog("Plan " + (dog ? "dog" : "cat") + " " + status);
                return true;
            }
            catch (Exception ex)
            {
                status = (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        private unsafe bool TryBuildPetFeedPlanAuraMono(bool dog, List<PetFeedTarget> targets, List<PetFeedFoodSupply> foods, out int visibleCount, out string status)
        {
            visibleCount = 0;
            status = "AuraMono pet feed unavailable";
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    status = "AuraMono API unavailable";
                    return false;
                }

                if (!this.TryGetPetFeedAuraEntityTypeValue(dog, out int entityTypeValue, out status))
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Pet.PetSystem", out IntPtr petSystemObj) || petSystemObj == IntPtr.Zero)
                {
                    status = "AuraMono PetSystem instance unavailable";
                    return false;
                }

                IntPtr petSystemClass = auraMonoObjectGetClass(petSystemObj);
                IntPtr getPetsMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "GetPetComponentDatas", 1);
                IntPtr initFoodsMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "InitFoods", 1);
                IntPtr getFoodsMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "GetFoods", 0);
                IntPtr getEatenFavoriteFoodsMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "GetEatenFavoriteFoods", 1);
                if (getPetsMethod == IntPtr.Zero || initFoodsMethod == IntPtr.Zero || getFoodsMethod == IntPtr.Zero)
                {
                    status = "AuraMono PetSystem method(s) unavailable pets=0x" + getPetsMethod.ToInt64().ToString("X")
                        + " initFoods=0x" + initFoodsMethod.ToInt64().ToString("X")
                        + " getFoods=0x" + getFoodsMethod.ToInt64().ToString("X");
                    return false;
                }

                int maxFullness = this.GetPetFeedMaxFullnessAuraMono(dog);
                if (maxFullness <= 0)
                {
                    maxFullness = 100;
                    this.PetFeedLog("AuraMono fullness limit unavailable; fallback=100");
                }

                IntPtr* petArgs = stackalloc IntPtr[1];
                petArgs[0] = (IntPtr)(&entityTypeValue);
                IntPtr exc = IntPtr.Zero;
                IntPtr petListObj = auraMonoRuntimeInvoke(getPetsMethod, petSystemObj, (IntPtr)petArgs, ref exc);
                if (exc != IntPtr.Zero || petListObj == IntPtr.Zero)
                {
                    status = "AuraMono GetPetComponentDatas failed exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                int mineCount = 0;
                int otherCount = 0;
                int unknownOwnerCount = 0;
                string targetSource = "ownedList";
                List<PetFeedTarget> collectedPets = new List<PetFeedTarget>();
                string collectStatus;
                if (this.TryCollectPetFeedPetListAuraMono(dog, collectedPets, out visibleCount, out collectStatus))
                {
                    if (collectStatus.IndexOf("world=", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        targetSource = collectStatus.IndexOf("world=0", StringComparison.OrdinalIgnoreCase) >= 0 ? "ownedList" : "worldEntities";
                    }

                    foreach (PetFeedTarget target in collectedPets)
                    {
                        if (target == null || target.NetId == 0U)
                        {
                            continue;
                        }

                        this.CountPetFeedOwner(target, ref mineCount, ref otherCount, ref unknownOwnerCount);
                        if (this.CanAttemptPetFeedTarget(target))
                        {
                            targets.Add(target);
                        }
                    }

                    this.PetFeedLog("World pet scan " + (dog ? "dog" : "cat") + ": " + collectStatus);
                }
                else
                {
                    this.PetFeedLog("World pet scan " + (dog ? "dog" : "cat") + " unavailable: " + collectStatus + ". Falling back to owned list.");

                    HashSet<uint> seenPetNetIds = new HashSet<uint>();
                    List<IntPtr> petItems = new List<IntPtr>();
                    if (this.TryEnumerateAuraMonoCollectionItems(petListObj, petItems))
                    {
                        foreach (IntPtr petData in petItems)
                        {
                            if (!this.TryGetPetFeedTargetAuraMono(petData, maxFullness, out PetFeedTarget target))
                            {
                                continue;
                            }

                            target.Source = "ownedList";
                            target.IsDog = dog;
                            this.TryPopulatePetFeedKnownFavoriteFoodsAuraMono(petSystemObj, getEatenFavoriteFoodsMethod, target);
                            if (!seenPetNetIds.Add(target.NetId))
                            {
                                continue;
                            }

                            visibleCount++;
                            this.CountPetFeedOwner(target, ref mineCount, ref otherCount, ref unknownOwnerCount);
                            if (this.CanAttemptPetFeedTarget(target))
                            {
                                targets.Add(target);
                            }
                        }
                    }
                }

                exc = IntPtr.Zero;
                IntPtr* foodArgs = stackalloc IntPtr[1];
                foodArgs[0] = (IntPtr)(&entityTypeValue);
                auraMonoRuntimeInvoke(initFoodsMethod, petSystemObj, (IntPtr)foodArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "AuraMono InitFoods failed exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                exc = IntPtr.Zero;
                IntPtr foodListObj = auraMonoRuntimeInvoke(getFoodsMethod, petSystemObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || foodListObj == IntPtr.Zero)
                {
                    status = "AuraMono GetFoods failed exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                List<IntPtr> foodItems = new List<IntPtr>();
                if (this.TryEnumerateAuraMonoCollectionItems(foodListObj, foodItems))
                {
                    foreach (IntPtr foodObj in foodItems)
                    {
                        if (this.TryGetPetFeedFoodSupplyAuraMono(foodObj, out PetFeedFoodSupply food) && food.Count > 0 && food.Fullness > 0 && food.NetId != 0U && !food.IsLock)
                        {
                            foods.Add(food);
                        }
                    }
                }

                foods.Sort((a, b) =>
                {
                    int cmp = a.Fullness.CompareTo(b.Fullness);
                    if (cmp != 0) return cmp;
                    return a.StaticId.CompareTo(b.StaticId);
                });
                this.RegisterPetFeedFoodOptions(foods);

                if (targets.Count > 0 && foods.Count == 0)
                {
                    status = "AuraMono no usable pet food";
                    return false;
                }
                if (!this.ApplyPetFeedSelectedFoodFilter(foods, targets.Count > 0, out string filterStatus))
                {
                    status = "AuraMono " + filterStatus;
                    return false;
                }

                foreach (PetFeedTarget target in targets)
                {
                    this.TryPopulatePetFeedKnownFavoriteFoodsAuraMono(petSystemObj, getEatenFavoriteFoodsMethod, target);
                }

                status = "AuraMono source=" + targetSource + " visible=" + visibleCount + this.FormatPetFeedOwnerCounts(mineCount, otherCount, unknownOwnerCount) + " hungry=" + targets.Count + " foods=" + foods.Count + this.FormatPetFeedSelectedFoodStatus() + " max=" + maxFullness + " entityType=" + entityTypeValue;
                this.PetFeedLog("Plan " + (dog ? "dog" : "cat") + " " + status);
                return true;
            }
            catch (Exception ex)
            {
                status = "AuraMono plan exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryGetPetFeedAuraEntityTypeValue(bool dog, out int value, out string status)
        {
            value = dog ? this.petFeedAuraDogEntityTypeValue : this.petFeedAuraCatEntityTypeValue;
            status = string.Empty;
            if (value != int.MinValue)
            {
                return true;
            }

            this.ResolveAuraFarmRuntimeMethodsViaMono();
            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoStringNew == null
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectUnbox == null)
            {
                status = "AuraMono enum prerequisites unavailable";
                return false;
            }

            IntPtr entityTypeClass = this.FindAuraMonoClassByFullName("EcsClient.XDT.Scene.Shared.Data.SharedData.EntityType");
            if (entityTypeClass == IntPtr.Zero)
            {
                entityTypeClass = this.FindAuraMonoClassAcrossLoadedAssemblies("EcsClient.XDT.Scene.Shared.Data.SharedData", "EntityType");
            }

            string[] names = dog ? new[] { "dog", "Dog", "DOG" } : new[] { "cat", "Cat", "CAT" };
            if (entityTypeClass != IntPtr.Zero && this.TryReadAuraMonoStaticIntField(entityTypeClass, names, out value))
            {
                if (dog)
                {
                    this.petFeedAuraDogEntityTypeValue = value;
                }
                else
                {
                    this.petFeedAuraCatEntityTypeValue = value;
                }

                status = "AuraMono EntityType field=" + value;
                return true;
            }

            if (!this.TryCreateAuraMonoSystemTypeObject("EcsClient.XDT.Scene.Shared.Data.SharedData.EntityType", out IntPtr entityTypeObj) || entityTypeObj == IntPtr.Zero)
            {
                status = "AuraMono EntityType System.Type unavailable";
                return false;
            }

            IntPtr enumClass = this.FindAuraMonoClassByFullName("System.Enum");
            if (enumClass == IntPtr.Zero)
            {
                enumClass = this.FindAuraMonoClassAcrossLoadedAssemblies("System", "Enum");
            }

            IntPtr parseMethod = enumClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(enumClass, "Parse", 2) : IntPtr.Zero;
            if (parseMethod == IntPtr.Zero)
            {
                status = "AuraMono System.Enum.Parse(Type,string) unavailable";
                return false;
            }

            IntPtr* args = stackalloc IntPtr[2];
            args[0] = entityTypeObj;
            foreach (string name in names)
            {
                IntPtr nameObj = auraMonoStringNew(this.auraMonoRootDomain, name);
                if (nameObj == IntPtr.Zero)
                {
                    continue;
                }

                args[1] = nameObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr boxedEnum = auraMonoRuntimeInvoke(parseMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc == IntPtr.Zero && boxedEnum != IntPtr.Zero && this.TryUnboxMonoInt32(boxedEnum, out value))
                {
                    if (dog)
                    {
                        this.petFeedAuraDogEntityTypeValue = value;
                    }
                    else
                    {
                        this.petFeedAuraCatEntityTypeValue = value;
                    }

                    status = "AuraMono EntityType." + name + "=" + value;
                    return true;
                }
            }

            status = "AuraMono EntityType parse failed for " + (dog ? "dog" : "cat");
            return false;
        }

        private unsafe bool TryReadAuraMonoStaticIntField(IntPtr classPtr, string[] fieldNames, out int value)
        {
            value = 0;
            if (classPtr == IntPtr.Zero
                || fieldNames == null
                || fieldNames.Length == 0
                || auraMonoClassGetFieldFromName == null
                || auraMonoClassVtable == null
                || auraMonoFieldStaticGetValue == null
                || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            IntPtr vtable = auraMonoClassVtable(this.auraMonoRootDomain, classPtr);
            if (vtable == IntPtr.Zero)
            {
                return false;
            }

            foreach (string fieldName in fieldNames)
            {
                IntPtr fieldPtr = auraMonoClassGetFieldFromName(classPtr, fieldName);
                if (fieldPtr == IntPtr.Zero)
                {
                    continue;
                }

                int rawValue = 0;
                auraMonoFieldStaticGetValue(vtable, fieldPtr, (IntPtr)(&rawValue));
                value = rawValue;
                return true;
            }

            return false;
        }

        private bool TryCollectWorldPetFeedTargetsAuraMono(
            bool dog,
            List<PetFeedTarget> targets,
            HashSet<uint> seenPetNetIds,
            int maxFullness,
            int entityTypeValue,
            ref int visibleCount,
            ref int mineCount,
            ref int otherCount,
            ref int unknownOwnerCount,
            out int added,
            out string status)
        {
            added = 0;
            status = "world scan unavailable";
            if (targets == null || seenPetNetIds == null)
            {
                status = "world scan target buffers unavailable";
                return false;
            }

            try
            {
                if (!this.TryGetNetCookScanOrigin(out Vector3 origin, out string originStatus))
                {
                    status = "origin unavailable: " + originStatus;
                    return false;
                }

                if (!this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out _, out string managerStatus))
                {
                    status = managerStatus;
                    return false;
                }

                IntPtr dictionaryObj = IntPtr.Zero;
                if ((!this.TryGetMonoObjectMember(managerObj, "_dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(managerObj, "dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero))
                {
                    status = "level object dictionary unavailable";
                    return false;
                }

                List<IntPtr> entries = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(dictionaryObj, entries) || entries.Count <= 0)
                {
                    status = "level object dictionary empty";
                    return false;
                }

                int nearbyLevelObjects = 0;
                int ownerResolved = 0;
                int petComponentResolved = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    IntPtr entryObj = entries[i];
                    if (entryObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr levelObjectObj = IntPtr.Zero;
                    if ((!this.TryGetMonoObjectMember(entryObj, "Value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(entryObj, "value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(entryObj, "_value", out levelObjectObj) || levelObjectObj == IntPtr.Zero))
                    {
                        levelObjectObj = entryObj;
                    }

                    if (levelObjectObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.TryGetMonoBoolMember(levelObjectObj, "isActive", out bool isActive) && !isActive)
                    {
                        continue;
                    }

                    if (!this.TryExtractHomePositionMonoObject(levelObjectObj, out Vector3 levelObjectPosition))
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(origin, levelObjectPosition);
                    if (distance > PetFeedWorldScanRadius)
                    {
                        continue;
                    }

                    nearbyLevelObjects++;
                    ulong levelObjectNetId = 0UL;
                    if (!this.TryGetMonoUInt64Member(levelObjectObj, "netId", out levelObjectNetId) || levelObjectNetId == 0UL)
                    {
                        if (!this.TryGetMonoUInt64Member(entryObj, "Key", out levelObjectNetId)
                            && !this.TryGetMonoUInt64Member(entryObj, "key", out levelObjectNetId)
                            && !this.TryGetMonoUInt64Member(entryObj, "_key", out levelObjectNetId))
                        {
                            continue;
                        }
                    }

                    if (levelObjectNetId == 0UL || !this.TryResolveOwnerIdFromLevelObjectIdMono(levelObjectNetId, out uint ownerNetId) || ownerNetId == 0U)
                    {
                        continue;
                    }

                    ownerResolved++;
                    if (seenPetNetIds.Contains(ownerNetId))
                    {
                        continue;
                    }

                    if (!this.TryGetAuraMonoEntityObjectByNetId(ownerNetId, out IntPtr entityObj) || entityObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetPetFeedTargetFromEntityAuraMono(entityObj, dog, maxFullness, entityTypeValue, out PetFeedTarget target))
                    {
                        continue;
                    }

                    petComponentResolved++;
                    if (!seenPetNetIds.Add(target.NetId))
                    {
                        continue;
                    }

                    target.Source = "world";
                    target.IsDog = dog;
                    visibleCount++;
                    added++;
                    this.CountPetFeedOwner(target, ref mineCount, ref otherCount, ref unknownOwnerCount);
                    if (this.CanAttemptPetFeedTarget(target))
                    {
                        targets.Add(target);
                    }
                }

                status = "nearbyLevelObjects=" + nearbyLevelObjects
                    + " owners=" + ownerResolved
                    + " pets=" + petComponentResolved
                    + " added=" + added
                    + " radius=" + PetFeedWorldScanRadius.ToString("F0") + "m";
                return added > 0;
            }
            catch (Exception ex)
            {
                status = "world scan exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TryGetPetFeedTargetFromEntityAuraMono(IntPtr entityObj, bool dog, int maxFullness, int entityTypeValue, out PetFeedTarget target)
        {
            target = null;
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr entityClass = auraMonoObjectGetClass(entityObj);
            IntPtr getAllComponentsMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "GetAllComponents", 0);
            if (getAllComponentsMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr componentsObj = auraMonoRuntimeInvoke(getAllComponentsMethod, entityObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || componentsObj == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components) || components.Count <= 0)
            {
                return false;
            }

            List<int> favoriteFoods = null;
            List<int> dislikeFoods = null;
            for (int i = 0; i < components.Count && i < 128; i++)
            {
                IntPtr componentObj = components[i];
                if (componentObj == IntPtr.Zero)
                {
                    continue;
                }

                string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(componentObj));
                this.TryMergePetFeedPreferenceListsAuraMono(componentObj, ref favoriteFoods, ref dislikeFoods);
                bool classLooksLikePet = !string.IsNullOrEmpty(className)
                    && (className.IndexOf("PetComponent", StringComparison.OrdinalIgnoreCase) >= 0
                        || className.IndexOf("DogComponent", StringComparison.OrdinalIgnoreCase) >= 0
                        || className.IndexOf("MeowComponent", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!classLooksLikePet)
                {
                    continue;
                }

                IntPtr petDataObj = IntPtr.Zero;
                if ((!this.TryGetMonoObjectMember(componentObj, "petComponentData", out petDataObj) || petDataObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(componentObj, "_petComponentData", out petDataObj) || petDataObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(componentObj, "PetComponentData", out petDataObj) || petDataObj == IntPtr.Zero))
                {
                    continue;
                }

                if (!this.TryGetPetFeedTargetAuraMono(petDataObj, maxFullness, out PetFeedTarget candidate))
                {
                    continue;
                }

                bool entityTypeMatches = candidate.EntityType == 0 || candidate.EntityType == entityTypeValue;
                bool classMatches = dog
                    ? className.IndexOf("DogComponent", StringComparison.OrdinalIgnoreCase) >= 0
                    : className.IndexOf("MeowComponent", StringComparison.OrdinalIgnoreCase) >= 0 || className.IndexOf("CatComponent", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!entityTypeMatches && !classMatches)
                {
                    continue;
                }

                if (favoriteFoods != null && favoriteFoods.Count > 0)
                {
                    candidate.FavoriteFoods = favoriteFoods;
                    candidate.FavoriteSource = "properties";
                }
                if (dislikeFoods != null && dislikeFoods.Count > 0)
                {
                    candidate.DislikeFoods = dislikeFoods;
                }

                target = candidate;
                return true;
            }

            return false;
        }

        private bool TryGetPetFeedTargetAuraMono(IntPtr petData, int maxFullness, out PetFeedTarget target)
        {
            target = null;
            if (petData == IntPtr.Zero)
            {
                return false;
            }

            bool? isMine = null;
            if (this.TryGetMonoBoolMember(petData, "isMine", out bool isMineValue)
                || this.TryGetMonoBoolMember(petData, "IsMine", out isMineValue)
                || this.TryGetMonoBoolMember(petData, "_isMine", out isMineValue))
            {
                isMine = isMineValue;
            }

            if ((!this.TryGetMonoObjectMember(petData, "animalComponentData", out IntPtr animalData) || animalData == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(petData, "_animalComponentData", out animalData) || animalData == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(petData, "AnimalComponentData", out animalData) || animalData == IntPtr.Zero))
            {
                return false;
            }

            if (!this.TryGetMonoIntMember(animalData, "fullness", out int fullness)
                && !this.TryGetMonoIntMember(animalData, "_fullness", out fullness)
                && !this.TryGetMonoIntMember(animalData, "Fullness", out fullness))
            {
                return false;
            }

            if (!this.TryGetMonoUInt32Member(animalData, "netId", out uint netId)
                && !this.TryGetMonoUInt32Member(animalData, "_netId", out netId)
                && !this.TryGetMonoUInt32Member(animalData, "NetId", out netId))
            {
                return false;
            }

            if (netId == 0U)
            {
                return false;
            }

            List<int> favoriteFoods = null;
            List<int> dislikeFoods = null;
            this.TryMergePetFeedPreferenceListsAuraMono(petData, ref favoriteFoods, ref dislikeFoods);
            this.TryMergePetFeedPreferenceListsAuraMono(animalData, ref favoriteFoods, ref dislikeFoods);

            int entityType = 0;
            if (!this.TryGetMonoIntMember(animalData, "entityType", out entityType))
            {
                this.TryGetMonoIntMember(animalData, "_entityType", out entityType);
            }

            int breedId = 0;
            if (!this.TryGetMonoIntMember(animalData, "breedId", out breedId))
            {
                this.TryGetMonoIntMember(animalData, "_breedId", out breedId);
            }

            string name = string.Empty;
            if (!this.TryGetMonoStringMember(animalData, "name", out name))
            {
                this.TryGetMonoStringMember(animalData, "_name", out name);
            }

            string textureId = string.Empty;
            this.TryGetPetTextureIdAuraMono(petData, out textureId);

            target = new PetFeedTarget
            {
                NetId = netId,
                CurrentFullness = fullness,
                MaxFullness = maxFullness,
                IsMine = isMine,
                EntityType = entityType,
                FavoriteFoods = favoriteFoods,
                DislikeFoods = dislikeFoods,
                FavoriteSource = favoriteFoods != null && favoriteFoods.Count > 0 ? "properties" : null,
                Name = name,
                BreedId = breedId,
                PetTextureId = textureId
            };
            this.TryPopulatePetFeedTableFavoriteFoodsAuraMono(target);
            return true;
        }

        private bool TryGetPetFeedFoodSupplyAuraMono(IntPtr foodObj, out PetFeedFoodSupply food)
        {
            food = null;
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

            food = new PetFeedFoodSupply
            {
                NetId = netId,
                Count = count,
                Fullness = fullness,
                StaticId = staticId,
                Name = this.ResolvePetFeedFoodName(staticId, foodObj),
                IsLock = isLock
            };
            return true;
        }

        private int GetPetFeedMaxFullnessAuraMono(bool dog)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null)
                {
                    return 0;
                }

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
                if (tableDataClass == IntPtr.Zero)
                {
                    return 0;
                }

                string fieldName = dog ? "TableDogThemes" : "TableKittyThemes";
                if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, fieldName, out IntPtr tableObj) || tableObj == IntPtr.Zero)
                {
                    return 0;
                }

                List<IntPtr> items = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(tableObj, items) || items.Count == 0)
                {
                    return 0;
                }

                foreach (IntPtr entry in items)
                {
                    IntPtr themeObj = IntPtr.Zero;
                    if ((!this.TryGetMonoObjectMember(entry, "Value", out themeObj) || themeObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(entry, "value", out themeObj) || themeObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(entry, "_value", out themeObj) || themeObj == IntPtr.Zero))
                    {
                        themeObj = entry;
                    }

                    if (themeObj != IntPtr.Zero
                        && (this.TryGetMonoIntMember(themeObj, "fullnessThreshold", out int value)
                            || this.TryGetMonoIntMember(themeObj, "_fullnessThreshold", out value)
                            || this.TryGetMonoIntMember(themeObj, "FullnessThreshold", out value))
                        && value > 0)
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private bool EnsurePetFeedReflection(out string status)
        {
            status = string.Empty;
            if (this.petFeedPrepareMethod != null
                && this.petFeedBeginMethod != null
                && this.petFeedPetSystemInstanceProperty != null
                && this.petFeedGetPetComponentDatasMethod != null
                && this.petFeedInitFoodsMethod != null
                && this.petFeedGetFoodsMethod != null
                && this.petFeedEntityTypeType != null)
            {
                return true;
            }

            if (this.petFeedManagedReflectionUnavailable)
            {
                status = string.IsNullOrEmpty(this.petFeedManagedReflectionUnavailableStatus)
                    ? "managed pet feed resolver unavailable"
                    : this.petFeedManagedReflectionUnavailableStatus;
                return false;
            }

            Type protocolType = this.FindLoadedTypeByFullName("XDTDataAndProtocol.ProtocolService.Pet.PetProtocolManager");
            this.petFeedPetSystemType = this.FindLoadedTypeByFullName("XDTGameSystem.GameplaySystem.Pet.PetSystem");
            this.petFeedEntityTypeType = this.FindLoadedTypeByFullName("EcsClient.XDT.Scene.Shared.Data.SharedData.EntityType");
            this.petFeedPetTypeType = this.FindLoadedTypeByFullName("XDT.Scene.Shared.Modules.Pet.PetType");
            this.petFeedStorageTypeType = this.FindLoadedTypeByFullName("EcsClient.XDT.Scene.Shared.Data.StaticPartial.EStorageType");

            if (protocolType == null || this.petFeedPetSystemType == null || this.petFeedEntityTypeType == null || this.petFeedPetTypeType == null || this.petFeedStorageTypeType == null)
            {
                List<string> missingTypes = new List<string>();
                if (protocolType == null)
                {
                    missingTypes.Add("PetProtocolManager");
                }

                if (this.petFeedPetSystemType == null)
                {
                    missingTypes.Add("PetSystem");
                }

                if (this.petFeedEntityTypeType == null)
                {
                    missingTypes.Add("EntityType");
                }

                if (this.petFeedPetTypeType == null)
                {
                    missingTypes.Add("PetType");
                }

                if (this.petFeedStorageTypeType == null)
                {
                    missingTypes.Add("EStorageType");
                }

                status = "missing type(s): " + string.Join(", ", missingTypes.ToArray());
                IntPtr auraProtocol = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Pet.PetProtocolManager");
                IntPtr auraPetSystem = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.Pet.PetSystem");
                IntPtr auraEntityType = this.FindAuraMonoClassByFullName("EcsClient.XDT.Scene.Shared.Data.SharedData.EntityType");
                this.petFeedManagedReflectionUnavailable = true;
                this.petFeedManagedReflectionUnavailableStatus = status;
                this.PetFeedLog("Resolver failed: " + status
                    + " protocol=" + (protocolType != null ? protocolType.FullName : "null")
                    + " petSystem=" + (this.petFeedPetSystemType != null ? this.petFeedPetSystemType.FullName : "null")
                    + " entityType=" + (this.petFeedEntityTypeType != null ? this.petFeedEntityTypeType.FullName : "null")
                    + " aura(protocol=" + (auraProtocol != IntPtr.Zero)
                    + ",petSystem=" + (auraPetSystem != IntPtr.Zero)
                    + ",entityType=" + (auraEntityType != IntPtr.Zero) + ")");
                return false;
            }

            this.petFeedPrepareMethod = protocolType.GetMethod("PrepareFeed", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(uint) }, null);
            this.petFeedBeginMethod = protocolType.GetMethod("BeginFeed", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            this.petFeedPetSystemInstanceProperty = this.petFeedPetSystemType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                ?? this.GetDataModuleInstanceProperty(this.petFeedPetSystemType);
            this.petFeedGetPetComponentDatasMethod = this.petFeedPetSystemType.GetMethod("GetPetComponentDatas", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { this.petFeedEntityTypeType }, null);
            this.petFeedInitFoodsMethod = this.petFeedPetSystemType.GetMethod("InitFoods", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { this.petFeedEntityTypeType }, null);
            this.petFeedInitFoodsForPickerMethod = this.petFeedPetSystemType.GetMethod("InitFoods", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { this.petFeedPetTypeType, this.petFeedStorageTypeType }, null);
            this.petFeedGetFoodsMethod = this.petFeedPetSystemType.GetMethod("GetFoods", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            this.petFeedGetFoodBackpackItemsMethod = this.petFeedPetSystemType.GetMethod("GetFoodBackpackItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            this.petFeedGetEatenFavoriteFoodsMethod = this.petFeedPetSystemType.GetMethod("GetEatenFavoriteFoods", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(uint) }, null);

            if (this.petFeedPrepareMethod == null || this.petFeedBeginMethod == null || this.petFeedPetSystemInstanceProperty == null || this.petFeedGetPetComponentDatasMethod == null || this.petFeedInitFoodsMethod == null || this.petFeedInitFoodsForPickerMethod == null || this.petFeedGetFoodsMethod == null || this.petFeedGetFoodBackpackItemsMethod == null)
            {
                List<string> missingMethods = new List<string>();
                if (this.petFeedPrepareMethod == null)
                {
                    missingMethods.Add("PrepareFeed(uint)");
                }

                if (this.petFeedBeginMethod == null)
                {
                    missingMethods.Add("BeginFeed(...)");
                }

                if (this.petFeedPetSystemInstanceProperty == null)
                {
                    missingMethods.Add("PetSystem.Instance");
                }

                if (this.petFeedGetPetComponentDatasMethod == null)
                {
                    missingMethods.Add("PetSystem.GetPetComponentDatas(EntityType)");
                }

                if (this.petFeedInitFoodsMethod == null)
                {
                    missingMethods.Add("PetSystem.InitFoods(EntityType)");
                }

                if (this.petFeedInitFoodsForPickerMethod == null)
                {
                    missingMethods.Add("PetSystem.InitFoods(PetType,EStorageType)");
                }

                if (this.petFeedGetFoodsMethod == null)
                {
                    missingMethods.Add("PetSystem.GetFoods()");
                }

                if (this.petFeedGetFoodBackpackItemsMethod == null)
                {
                    missingMethods.Add("PetSystem.GetFoodBackpackItems()");
                }

                status = "missing method(s): " + string.Join(", ", missingMethods.ToArray());
                this.petFeedManagedReflectionUnavailable = true;
                this.petFeedManagedReflectionUnavailableStatus = status;
                this.PetFeedLog("Resolver failed: " + status
                    + " protocol=" + protocolType.FullName
                    + " petSystem=" + this.petFeedPetSystemType.FullName
                    + " entityType=" + this.petFeedEntityTypeType.FullName);
                return false;
            }

            return true;
        }

        private Type FindLoadedTypeByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            Type directType = Type.GetType(fullName, false);
            if (directType != null && string.Equals(directType.FullName, fullName, StringComparison.Ordinal))
            {
                return directType;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null && string.Equals(type.FullName, fullName, StringComparison.Ordinal))
                {
                    return type;
                }
            }

            foreach (Assembly assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                Type resolved = types.FirstOrDefault(type => type != null && string.Equals(type.FullName, fullName, StringComparison.Ordinal));
                if (resolved != null)
                {
                    return resolved;
                }
            }

            return null;
        }

        private bool TryGetPetFeedTarget(object petData, int maxFullness, out PetFeedTarget target)
        {
            target = null;
            if (petData == null)
            {
                return false;
            }

            bool? isMine = null;
            if (this.TryGetObjectMember(petData, "isMine", out object isMineObj) && isMineObj != null)
            {
                try
                {
                    isMine = Convert.ToBoolean(isMineObj);
                }
                catch
                {
                }
            }

            if (!this.TryGetObjectMember(petData, "animalComponentData", out object animalData) || animalData == null)
            {
                return false;
            }

            if (!this.TryReadIntFromMember(animalData, "fullness", out int fullness))
            {
                return false;
            }

            if (!this.TryReadUIntFromMember(animalData, "netId", out uint netId) || netId == 0U)
            {
                return false;
            }

            int entityType = 0;
            this.TryReadIntFromMember(animalData, "entityType", out entityType);
            int breedId = 0;
            this.TryReadIntFromMember(animalData, "breedId", out breedId);

            List<int> favoriteFoods = null;
            List<int> dislikeFoods = null;
            this.TryMergePetFeedPreferenceListsManaged(petData, ref favoriteFoods, ref dislikeFoods);
            this.TryMergePetFeedPreferenceListsManaged(animalData, ref favoriteFoods, ref dislikeFoods);
            string name = string.Empty;
            if (this.TryGetObjectMember(animalData, "name", out object nameObj) && nameObj is string petName)
            {
                name = petName;
            }

            string textureId = string.Empty;
            this.TryGetPetTextureIdManaged(petData, out textureId);

            target = new PetFeedTarget
            {
                NetId = netId,
                CurrentFullness = fullness,
                MaxFullness = maxFullness,
                IsMine = isMine,
                EntityType = entityType,
                FavoriteFoods = favoriteFoods,
                DislikeFoods = dislikeFoods,
                FavoriteSource = favoriteFoods != null && favoriteFoods.Count > 0 ? "properties" : null,
                Name = name,
                BreedId = breedId,
                PetTextureId = textureId
            };
            this.TryPopulatePetFeedTableFavoriteFoods(target);
            return true;
        }

        private void TryPopulatePetFeedKnownFavoriteFoodsManaged(object petSystem, PetFeedTarget target)
        {
            if (petSystem == null || target == null || target.NetId == 0U || this.petFeedGetEatenFavoriteFoodsMethod == null)
            {
                return;
            }

            try
            {
                object favoriteObj = this.petFeedGetEatenFavoriteFoodsMethod.Invoke(petSystem, new object[] { target.NetId });
                if (this.TryReadIntListObject(favoriteObj, out List<int> favoriteFoods) && favoriteFoods.Count > 0)
                {
                    this.MergePetFeedIntList(ref target.FavoriteFoods, favoriteFoods);
                    target.FavoriteSource = string.IsNullOrEmpty(target.FavoriteSource) ? "eatenFavorites" : target.FavoriteSource + "+eatenFavorites";
                }
            }
            catch
            {
            }
        }

        private unsafe void TryPopulatePetFeedKnownFavoriteFoodsAuraMono(IntPtr petSystemObj, IntPtr getEatenFavoriteFoodsMethod, PetFeedTarget target)
        {
            if (petSystemObj == IntPtr.Zero || getEatenFavoriteFoodsMethod == IntPtr.Zero || target == null || target.NetId == 0U || auraMonoRuntimeInvoke == null)
            {
                return;
            }

            try
            {
                uint netId = target.NetId;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&netId);
                IntPtr exc = IntPtr.Zero;
                IntPtr favoriteObj = auraMonoRuntimeInvoke(getEatenFavoriteFoodsMethod, petSystemObj, (IntPtr)args, ref exc);
                if (exc == IntPtr.Zero && this.TryReadMonoIntListObject(favoriteObj, out List<int> favoriteFoods) && favoriteFoods.Count > 0)
                {
                    this.MergePetFeedIntList(ref target.FavoriteFoods, favoriteFoods);
                    target.FavoriteSource = string.IsNullOrEmpty(target.FavoriteSource) ? "eatenFavorites" : target.FavoriteSource + "+eatenFavorites";
                }
            }
            catch
            {
            }
        }

        private void TryPopulatePetFeedTableFavoriteFoods(PetFeedTarget target)
        {
            if (target == null || target.BreedId <= 0)
            {
                return;
            }

            try
            {
                Type tableDataType = this.FindLoadedTypeByFullName("TableData");
                if (tableDataType == null)
                {
                    return;
                }

                MethodInfo getAnimalUnit = tableDataType.GetMethod("GetAnimalUnit", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(bool) }, null);
                MethodInfo getAnimalGroup = tableDataType.GetMethod("GetAnimalGroup", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(bool) }, null);
                if (getAnimalUnit == null || getAnimalGroup == null)
                {
                    return;
                }

                object unit = getAnimalUnit.Invoke(null, new object[] { target.BreedId, false });
                if ((unit == null || !this.TryReadIntFromMember(unit, "groupId", out int groupId) || groupId <= 0)
                    && !this.TryGetPetFeedFallbackAnimalGroupId(target.BreedId, out groupId))
                {
                    return;
                }

                target.FavoriteGroupId = groupId;
                object group = getAnimalGroup.Invoke(null, new object[] { groupId, false });
                if (group == null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(target.PetAvatarIconKey)
                    && this.TryGetObjectMember(group, "avatarIcon", out object avatarIconObj)
                    && avatarIconObj is string avatarIcon
                    && !string.IsNullOrWhiteSpace(avatarIcon))
                {
                    target.PetAvatarIconKey = avatarIcon;
                }

                if (!this.TryReadIntListFromMember(group, "favoriteFood", out List<int> favoriteFoods))
                {
                    return;
                }

                this.MergePetFeedIntList(ref target.FavoriteFoods, favoriteFoods);
                if (favoriteFoods.Count > 0)
                {
                    target.FavoriteSource = string.IsNullOrEmpty(target.FavoriteSource) ? "animalGroup" : target.FavoriteSource + "+animalGroup";
                }
            }
            catch
            {
            }
        }

        private unsafe void TryPopulatePetFeedTableFavoriteFoodsAuraMono(PetFeedTarget target)
        {
            if (target == null || target.BreedId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
            {
                return;
            }

            try
            {
                IntPtr tableDataClass = this.TryGetPetFeedAuraMonoTableDataClass();
                if (tableDataClass == IntPtr.Zero)
                {
                    return;
                }

                IntPtr getAnimalUnit = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetAnimalUnit", 2);
                IntPtr getAnimalGroup = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetAnimalGroup", 2);
                if (getAnimalUnit == IntPtr.Zero || getAnimalGroup == IntPtr.Zero)
                {
                    return;
                }

                int breedId = target.BreedId;
                bool needException = false;
                IntPtr* unitArgs = stackalloc IntPtr[2];
                unitArgs[0] = (IntPtr)(&breedId);
                unitArgs[1] = (IntPtr)(&needException);
                IntPtr exc = IntPtr.Zero;
                IntPtr unitObj = auraMonoRuntimeInvoke(getAnimalUnit, IntPtr.Zero, (IntPtr)unitArgs, ref exc);
                if ((exc != IntPtr.Zero || unitObj == IntPtr.Zero || !this.TryGetMonoIntMember(unitObj, "groupId", out int groupId) || groupId <= 0)
                    && !this.TryGetPetFeedFallbackAnimalGroupId(target.BreedId, out groupId))
                {
                    return;
                }

                target.FavoriteGroupId = groupId;
                IntPtr* groupArgs = stackalloc IntPtr[2];
                groupArgs[0] = (IntPtr)(&groupId);
                groupArgs[1] = (IntPtr)(&needException);
                exc = IntPtr.Zero;
                IntPtr groupObj = auraMonoRuntimeInvoke(getAnimalGroup, IntPtr.Zero, (IntPtr)groupArgs, ref exc);
                if (exc != IntPtr.Zero || groupObj == IntPtr.Zero)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(target.PetAvatarIconKey)
                    && this.TryGetMonoStringMember(groupObj, "avatarIcon", out string avatarIcon)
                    && !string.IsNullOrWhiteSpace(avatarIcon))
                {
                    target.PetAvatarIconKey = avatarIcon;
                }

                if (!this.TryReadMonoIntListMember(groupObj, "favoriteFood", out List<int> favoriteFoods))
                {
                    return;
                }

                this.MergePetFeedIntList(ref target.FavoriteFoods, favoriteFoods);
                if (favoriteFoods.Count > 0)
                {
                    target.FavoriteSource = string.IsNullOrEmpty(target.FavoriteSource) ? "animalGroup" : target.FavoriteSource + "+animalGroup";
                }
            }
            catch
            {
            }
        }

        private bool TryGetPetFeedFallbackAnimalGroupId(int breedId, out int groupId)
        {
            groupId = 0;
            if (breedId <= 0)
            {
                return false;
            }

            // Pet breed ids are formatted like 90304 / 90204. The animal group
            // is the leading family id when runtime field access cannot read it.
            int candidate = breedId / 100;
            if (candidate <= 0 || candidate == breedId)
            {
                return false;
            }

            groupId = candidate;
            return true;
        }

        private IntPtr TryGetPetFeedAuraMonoTableDataClass()
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

        private string FormatPetFeedFavoriteDebugSummary()
        {
            if (this.petFeedDetectedPets == null || this.petFeedDetectedPets.Count == 0)
            {
                return string.Empty;
            }

            int breedKnown = this.petFeedDetectedPets.Count(pet => pet != null && pet.BreedId > 0);
            int groupKnown = this.petFeedDetectedPets.Count(pet => pet != null && pet.FavoriteGroupId > 0);
            string samples = string.Join(",", this.petFeedDetectedPets
                .Where(pet => pet != null)
                .Take(3)
                .Select(pet => (pet.IsDog ? "dog" : "cat") + ":" + pet.NetId
                    + "/breed=" + pet.BreedId
                    + "/group=" + pet.FavoriteGroupId
                    + "/fav=" + (pet.FavoriteFoods != null ? pet.FavoriteFoods.Count : 0)
                    + "/avatar=" + (string.IsNullOrWhiteSpace(pet.PetAvatarIconKey) ? "none" : pet.PetAvatarIconKey)));
            return " breedKnown=" + breedKnown + " groupKnown=" + groupKnown + " samples=[" + samples + "]";
        }

        private void TryMergePetFeedPreferenceListsManaged(object obj, ref List<int> favoriteFoods, ref List<int> dislikeFoods)
        {
            if (obj == null)
            {
                return;
            }

            if (this.TryReadIntListFromMember(obj, "FavoriteFoods", out List<int> favorites)
                || this.TryReadIntListFromMember(obj, "favoriteFoods", out favorites)
                || this.TryReadIntListFromMember(obj, "_favoriteFoods", out favorites))
            {
                this.MergePetFeedIntList(ref favoriteFoods, favorites);
            }

            if (this.TryReadIntListFromMember(obj, "DislikeFoods", out List<int> dislikes)
                || this.TryReadIntListFromMember(obj, "dislikeFoods", out dislikes)
                || this.TryReadIntListFromMember(obj, "_dislikeFoods", out dislikes))
            {
                this.MergePetFeedIntList(ref dislikeFoods, dislikes);
            }
        }

        private void TryMergePetFeedPreferenceListsAuraMono(IntPtr obj, ref List<int> favoriteFoods, ref List<int> dislikeFoods)
        {
            if (obj == IntPtr.Zero)
            {
                return;
            }

            if (this.TryReadMonoIntListMember(obj, "FavoriteFoods", out List<int> favorites)
                || this.TryReadMonoIntListMember(obj, "favoriteFoods", out favorites)
                || this.TryReadMonoIntListMember(obj, "_favoriteFoods", out favorites))
            {
                this.MergePetFeedIntList(ref favoriteFoods, favorites);
            }

            if (this.TryReadMonoIntListMember(obj, "DislikeFoods", out List<int> dislikes)
                || this.TryReadMonoIntListMember(obj, "dislikeFoods", out dislikes)
                || this.TryReadMonoIntListMember(obj, "_dislikeFoods", out dislikes))
            {
                this.MergePetFeedIntList(ref dislikeFoods, dislikes);
            }
        }

        private bool TryReadIntListFromMember(object obj, string memberName, out List<int> values)
        {
            values = null;
            if (!this.TryGetObjectMember(obj, memberName, out object raw) || raw == null)
            {
                return false;
            }

            return this.TryReadIntListObject(raw, out values);
        }

        private bool TryReadIntListObject(object raw, out List<int> values)
        {
            values = new List<int>();
            if (raw == null || raw is string || !(raw is IEnumerable enumerable))
            {
                values = null;
                return false;
            }

            foreach (object item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }

                try
                {
                    int value = Convert.ToInt32(item);
                    if (value > 0 && !values.Contains(value))
                    {
                        values.Add(value);
                    }
                }
                catch
                {
                }
            }

            return true;
        }

        private bool TryReadMonoIntListMember(IntPtr obj, string memberName, out List<int> values)
        {
            values = null;
            return (this.TryGetPetFeedMonoReferenceFieldMember(obj, memberName, out IntPtr listObj)
                    || this.TryGetMonoObjectMember(obj, memberName, out listObj))
                && this.TryReadMonoIntListObject(listObj, out values);
        }

        private unsafe bool TryGetPetFeedMonoReferenceFieldMember(IntPtr obj, string memberName, out IntPtr valueObj)
        {
            valueObj = IntPtr.Zero;
            if (obj == IntPtr.Zero || string.IsNullOrEmpty(memberName) || auraMonoObjectGetClass == null || auraMonoFieldGetValue == null)
            {
                return false;
            }

            IntPtr klass = auraMonoObjectGetClass(obj);
            if (klass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr fieldPtr = this.FindAuraMonoFieldOnHierarchy(klass, memberName);
            if (fieldPtr == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                IntPtr rawValue = IntPtr.Zero;
                auraMonoFieldGetValue(obj, fieldPtr, (IntPtr)(&rawValue));
                valueObj = rawValue;
                return rawValue != IntPtr.Zero;
            }
            catch
            {
                valueObj = IntPtr.Zero;
                return false;
            }
        }

        private unsafe bool TryReadMonoIntListObject(IntPtr listObj, out List<int> values)
        {
            values = new List<int>();
            if (listObj == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                values = null;
                return false;
            }

            if (auraMonoArrayLength != null && auraMonoArrayAddrWithSize != null)
            {
                try
                {
                    int count = (int)Math.Min(auraMonoArrayLength(listObj).ToUInt64(), 4096UL);
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr raw = auraMonoArrayAddrWithSize(listObj, sizeof(int), (UIntPtr)i);
                        if (raw == IntPtr.Zero)
                        {
                            continue;
                        }

                        int value = *(int*)raw;
                        if (value > 0 && !values.Contains(value))
                        {
                            values.Add(value);
                        }
                    }

                    if (values.Count > 0)
                    {
                        return true;
                    }
                }
                catch
                {
                    values.Clear();
                }
            }

            List<IntPtr> items = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items))
            {
                return values.Count > 0;
            }

            foreach (IntPtr itemObj in items)
            {
                if (itemObj == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    IntPtr raw = auraMonoObjectUnbox(itemObj);
                    if (raw == IntPtr.Zero)
                    {
                        continue;
                    }

                    int value = *(int*)raw;
                    if (value > 0 && !values.Contains(value))
                    {
                        values.Add(value);
                    }
                }
                catch
                {
                }
            }

            return true;
        }

        private void MergePetFeedIntList(ref List<int> target, List<int> values)
        {
            if (values == null || values.Count == 0)
            {
                return;
            }

            if (target == null)
            {
                target = new List<int>();
            }

            foreach (int value in values)
            {
                if (value > 0 && !target.Contains(value))
                {
                    target.Add(value);
                }
            }
        }

        private void RefreshPetFeedPetList()
        {
            this.petFeedDetectedPets.Clear();
            this.petFeedPetListScrollIndex = 0;

            int catCount = 0;
            int dogCount = 0;
            string catStatus;
            string dogStatus;
            bool catOk = this.TryCollectPetFeedPetList(false, this.petFeedDetectedPets, out catCount, out catStatus);
            bool dogOk = this.TryCollectPetFeedPetList(true, this.petFeedDetectedPets, out dogCount, out dogStatus);

            foreach (PetFeedTarget pet in this.petFeedDetectedPets)
            {
                this.TryLoadPetFeedPetTexture(pet);
            }

            int avatarKnownCount = this.petFeedDetectedPets.Count(pet => pet != null && !string.IsNullOrWhiteSpace(pet.PetAvatarIconKey));
            int textureKnownCount = this.petFeedDetectedPets.Count(pet => pet != null && pet.PetTexture != null);
            this.petFeedPetListStatus = "cats=" + catCount + " dogs=" + dogCount;
            this.PetFeedLog("Pet list scan complete: " + this.petFeedPetListStatus + " avatarKnown=" + avatarKnownCount + " textures=" + textureKnownCount + " catStatus=" + catStatus + " dogStatus=" + dogStatus);
            if (!catOk && !dogOk)
            {
                this.petFeedPetListStatus = "Pet scan failed: cats=" + catStatus + " dogs=" + dogStatus;
                this.AddMenuNotification("Pet scan failed", new Color(1f, 0.58f, 0.42f));
            }
            else
            {
                this.AddMenuNotification("Pet scan: " + this.petFeedPetListStatus, new Color(0.45f, 0.88f, 1f));
            }
        }

        private bool TryCollectPetFeedPetList(bool dog, List<PetFeedTarget> pets, out int count, out string status)
        {
            count = 0;
            status = string.Empty;
            if (pets == null)
            {
                status = "target list unavailable";
                return false;
            }

            if (this.TryCollectPetFeedPetListManaged(dog, pets, out count, out status))
            {
                return true;
            }

            return this.TryCollectPetFeedPetListAuraMono(dog, pets, out count, out status);
        }

        private bool TryCollectPetFeedPetListManaged(bool dog, List<PetFeedTarget> pets, out int count, out string status)
        {
            count = 0;
            status = string.Empty;
            try
            {
                if (!this.EnsurePetFeedReflection(out status))
                {
                    return false;
                }

                object petSystem = this.petFeedPetSystemInstanceProperty.GetValue(null, null);
                if (petSystem == null)
                {
                    status = "PetSystem unavailable";
                    return false;
                }

                object entityTypeValue = Enum.Parse(this.petFeedEntityTypeType, dog ? "dog" : "cat");
                int maxFullness = this.GetPetFeedMaxFullness(dog);
                object petListObj = this.petFeedGetPetComponentDatasMethod.Invoke(petSystem, new object[] { entityTypeValue });
                if (petListObj is IEnumerable petList)
                {
                    foreach (object petData in petList)
                    {
                        if (!this.TryGetPetFeedTarget(petData, maxFullness > 0 ? maxFullness : 100, out PetFeedTarget target))
                        {
                            continue;
                        }

                        target.IsDog = dog;
                        target.Source = "ownedList";
                        this.TryPopulatePetFeedKnownFavoriteFoodsManaged(petSystem, target);
                        if (!pets.Any(existing => existing.NetId == target.NetId))
                        {
                            pets.Add(target);
                            count++;
                        }
                    }
                }

                status = "managed count=" + count;
                return true;
            }
            catch (Exception ex)
            {
                status = "managed exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryCollectPetFeedPetListAuraMono(bool dog, List<PetFeedTarget> pets, out int count, out string status)
        {
            count = 0;
            status = "AuraMono pet list unavailable";
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    status = "AuraMono API unavailable";
                    return false;
                }

                if (!this.TryGetPetFeedAuraEntityTypeValue(dog, out int entityTypeValue, out status))
                {
                    return false;
                }

                int maxFullness = this.GetPetFeedMaxFullnessAuraMono(dog);
                if (maxFullness <= 0)
                {
                    maxFullness = 100;
                }

                HashSet<uint> seenPetNetIds = new HashSet<uint>();
                foreach (PetFeedTarget existing in pets)
                {
                    if (existing != null && existing.NetId != 0U)
                    {
                        seenPetNetIds.Add(existing.NetId);
                    }
                }

                bool worldOk = this.TryCollectVisiblePetFeedPetsAuraMono(
                    dog,
                    pets,
                    seenPetNetIds,
                    maxFullness,
                    entityTypeValue,
                    out int worldCount,
                    out string worldStatus);

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Pet.PetSystem", out IntPtr petSystemObj) || petSystemObj == IntPtr.Zero)
                {
                    count = worldCount;
                    status = "AuraMono world=" + worldCount + " owned=0 total=" + count + " worldStatus=" + worldStatus + " ownedStatus=PetSystem instance unavailable";
                    return worldOk;
                }

                IntPtr petSystemClass = auraMonoObjectGetClass(petSystemObj);
                IntPtr getPetsMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "GetPetComponentDatas", 1);
                IntPtr getEatenFavoriteFoodsMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "GetEatenFavoriteFoods", 1);
                if (getPetsMethod == IntPtr.Zero)
                {
                    count = worldCount;
                    status = "AuraMono world=" + worldCount + " owned=0 total=" + count + " worldStatus=" + worldStatus + " ownedStatus=GetPetComponentDatas unavailable";
                    return worldOk;
                }

                IntPtr* petArgs = stackalloc IntPtr[1];
                petArgs[0] = (IntPtr)(&entityTypeValue);
                IntPtr exc = IntPtr.Zero;
                IntPtr petListObj = auraMonoRuntimeInvoke(getPetsMethod, petSystemObj, (IntPtr)petArgs, ref exc);
                if (exc != IntPtr.Zero || petListObj == IntPtr.Zero)
                {
                    count = worldCount;
                    status = "AuraMono world=" + worldCount + " owned=0 total=" + count + " worldStatus=" + worldStatus + " ownedStatus=GetPetComponentDatas failed exc=0x" + exc.ToInt64().ToString("X");
                    return worldOk;
                }

                List<IntPtr> petItems = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(petListObj, petItems))
                {
                    count = worldCount;
                    status = "AuraMono world=" + worldCount + " owned=0 total=" + count + " worldStatus=" + worldStatus + " ownedStatus=pet list empty";
                    return true;
                }

                int ownedCount = 0;
                foreach (IntPtr petData in petItems)
                {
                    if (!this.TryGetPetFeedTargetAuraMono(petData, maxFullness, out PetFeedTarget target))
                    {
                        continue;
                    }

                    target.IsDog = dog;
                    target.Source = "ownedList";
                    this.TryPopulatePetFeedKnownFavoriteFoodsAuraMono(petSystemObj, getEatenFavoriteFoodsMethod, target);
                    if (seenPetNetIds.Add(target.NetId))
                    {
                        pets.Add(target);
                        ownedCount++;
                    }
                    else
                    {
                        PetFeedTarget existing = pets.FirstOrDefault(candidate => candidate != null && candidate.NetId == target.NetId);
                        if (existing != null)
                        {
                            existing.Source = string.IsNullOrWhiteSpace(existing.Source) ? "ownedList" : existing.Source + "+ownedList";
                            if ((existing.FavoriteFoods == null || existing.FavoriteFoods.Count == 0) && target.FavoriteFoods != null && target.FavoriteFoods.Count > 0)
                            {
                                existing.FavoriteFoods = new List<int>(target.FavoriteFoods);
                                existing.FavoriteSource = target.FavoriteSource;
                            }
                            if ((existing.DislikeFoods == null || existing.DislikeFoods.Count == 0) && target.DislikeFoods != null && target.DislikeFoods.Count > 0)
                            {
                                existing.DislikeFoods = new List<int>(target.DislikeFoods);
                            }
                            if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(target.Name))
                            {
                                existing.Name = target.Name;
                            }
                            if (string.IsNullOrWhiteSpace(existing.PetTextureId) && !string.IsNullOrWhiteSpace(target.PetTextureId))
                            {
                                existing.PetTextureId = target.PetTextureId;
                            }
                            if (string.IsNullOrWhiteSpace(existing.PetAvatarIconKey) && !string.IsNullOrWhiteSpace(target.PetAvatarIconKey))
                            {
                                existing.PetAvatarIconKey = target.PetAvatarIconKey;
                            }
                            if (existing.BreedId == 0 && target.BreedId != 0)
                            {
                                existing.BreedId = target.BreedId;
                            }
                            if (existing.FavoriteGroupId == 0 && target.FavoriteGroupId != 0)
                            {
                                existing.FavoriteGroupId = target.FavoriteGroupId;
                            }
                        }
                    }
                }

                count = worldCount + ownedCount;
                status = "AuraMono world=" + worldCount + " owned=" + ownedCount + " total=" + count + " worldStatus=" + worldStatus;
                return true;
            }
            catch (Exception ex)
            {
                status = "AuraMono exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TryCollectVisiblePetFeedPetsAuraMono(
            bool dog,
            List<PetFeedTarget> pets,
            HashSet<uint> seenPetNetIds,
            int maxFullness,
            int entityTypeValue,
            out int count,
            out string status)
        {
            count = 0;
            status = "world entity scan unavailable";
            if (pets == null || seenPetNetIds == null)
            {
                status = "world entity scan target buffers unavailable";
                return false;
            }

            try
            {
                if (!this.TryEnumerateAuraMonoLoadedEntityObjects(out List<IntPtr> entityObjects, out string enumerateStatus))
                {
                    status = enumerateStatus;
                    return false;
                }

                int inspected = 0;
                int candidates = 0;
                int limit = Math.Min(entityObjects.Count, PetFeedEntityScanLimit);
                for (int i = 0; i < entityObjects.Count && inspected < limit; i++)
                {
                    IntPtr entityObj = entityObjects[i];
                    inspected++;
                    if (entityObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetPetFeedTargetFromEntityAuraMono(entityObj, dog, maxFullness, entityTypeValue, out PetFeedTarget target))
                    {
                        continue;
                    }

                    candidates++;
                    if (target.NetId == 0U || !seenPetNetIds.Add(target.NetId))
                    {
                        continue;
                    }

                    target.Source = "worldEntities";
                    target.IsDog = dog;
                    pets.Add(target);
                    count++;
                }

                status = "entities=" + entityObjects.Count + " inspected=" + inspected + " candidates=" + candidates + " added=" + count;
                return count > 0;
            }
            catch (Exception ex)
            {
                status = "world entity scan exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private void StartPetFeedSingle(PetFeedTarget pet)
        {
            if (pet == null || pet.NetId == 0U)
            {
                this.AddMenuNotification("Feed pet: invalid pet", new Color(1f, 0.58f, 0.42f));
                return;
            }

            if (!this.TryBuildPetFeedPlan(pet.IsDog, out List<PetFeedTarget> targets, out List<PetFeedFoodSupply> foods, out int visibleCount, out string status))
            {
                this.AddMenuNotification("Feed pet: " + status, new Color(1f, 0.58f, 0.42f));
                this.PetFeedLog("Single feed plan failed netId=" + pet.NetId + ": " + status);
                return;
            }

            PetFeedTarget target = targets.FirstOrDefault(candidate => candidate.NetId == pet.NetId);
            if (target == null)
            {
                this.AddMenuNotification("Feed pet: not hungry or not visible", new Color(0.45f, 0.88f, 1f));
                this.PetFeedLog("Single feed skipped netId=" + pet.NetId + ": not hungry/visible. planVisible=" + visibleCount);
                return;
            }

            target.Name = string.IsNullOrWhiteSpace(target.Name) ? pet.Name : target.Name;
            if ((target.FavoriteFoods == null || target.FavoriteFoods.Count == 0) && pet.FavoriteFoods != null)
            {
                target.FavoriteFoods = new List<int>(pet.FavoriteFoods);
                target.FavoriteSource = pet.FavoriteSource;
            }

            this.petFeedAllCoroutine = MelonCoroutines.Start(this.PetFeedAllRoutine(pet.IsDog, new List<PetFeedTarget> { target }, foods, visibleCount));
        }

        private bool TryGetPetTextureIdManaged(object petData, out string textureId)
        {
            textureId = string.Empty;
            if (petData == null || !this.TryGetObjectMember(petData, "adoptedTime", out object adoptedObj) || adoptedObj == null)
            {
                return false;
            }

            try
            {
                if (adoptedObj is DateTime adoptedTime)
                {
                    textureId = adoptedTime.ToFileTime().ToString();
                    return !string.IsNullOrWhiteSpace(textureId);
                }
            }
            catch
            {
            }

            return false;
        }

        private unsafe bool TryGetPetTextureIdAuraMono(IntPtr petData, out string textureId)
        {
            textureId = string.Empty;
            if (petData == IntPtr.Zero || auraMonoObjectUnbox == null || !this.TryGetMonoObjectMember(petData, "adoptedTime", out IntPtr adoptedObj) || adoptedObj == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                IntPtr raw = auraMonoObjectUnbox(adoptedObj);
                if (raw == IntPtr.Zero)
                {
                    return false;
                }

                ulong dateData = *(ulong*)raw;
                long ticks = (long)(dateData & 0x3FFFFFFFFFFFFFFFUL);
                if (ticks <= 0)
                {
                    return false;
                }

                textureId = new DateTime(ticks).ToFileTime().ToString();
                return !string.IsNullOrWhiteSpace(textureId);
            }
            catch
            {
                return false;
            }
        }

        private bool TryLoadPetFeedPetTexture(PetFeedTarget pet)
        {
            if (pet == null || pet.PetTexture != null)
            {
                return pet != null && pet.PetTexture != null;
            }

            if (pet.PetTextureLoadAttempted)
            {
                return false;
            }

            pet.PetTextureLoadAttempted = true;
            if (!string.IsNullOrWhiteSpace(pet.PetTextureId))
            {
                try
                {
                    Type utilityType = this.FindLoadedTypeByFullName("XDTBaseService.Services.Texture.LocalTextureCacheUtility");
                    Type imageEnumType = this.FindLoadedTypeByFullName("XDTBaseService.Services.Cache.ImageEnum");
                    if (utilityType != null && imageEnumType != null)
                    {
                        MethodInfo getLocalTexture = utilityType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(method => method.Name == "GetLocalTexture" && method.GetParameters().Length == 5);
                        if (getLocalTexture != null)
                        {
                            object imageEnumPhoto = Enum.Parse(imageEnumType, "Photo");
                            object[] args = new object[] { pet.PetTextureId, 400, 400, null, imageEnumPhoto };
                            object result = getLocalTexture.Invoke(null, args);
                            if (result is bool ok && ok && args[3] is Texture2D texture && texture != null)
                            {
                                pet.PetTexture = texture;
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(pet.PetAvatarIconKey))
            {
                List<string> keys = new List<string>
                {
                    pet.PetAvatarIconKey,
                    this.NormalizeRadarIconSpriteKey(pet.PetAvatarIconKey)
                };

                foreach (string rawKey in keys)
                {
                    string key = this.NormalizeRadarIconSpriteKey(rawKey);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (this.autoSellBagItemTextures.TryGetValue(key, out Texture2D texture) && texture != null)
                    {
                        pet.PetTexture = texture;
                        return true;
                    }
                }
            }

            return false;
        }

        private string FormatPetFeedFavoriteFoodNames(PetFeedTarget pet)
        {
            if (pet == null || pet.FavoriteFoods == null || pet.FavoriteFoods.Count == 0)
            {
                return "No known favorite";
            }

            List<string> names = new List<string>();
            foreach (int staticId in pet.FavoriteFoods.Take(3))
            {
                names.Add(this.GetPetFeedFoodDisplayName(staticId, string.Empty));
            }

            if (pet.FavoriteFoods.Count > names.Count)
            {
                names.Add("+" + (pet.FavoriteFoods.Count - names.Count));
            }

            return string.Join(", ", names.ToArray());
        }

        private void CountPetFeedOwner(PetFeedTarget target, ref int mineCount, ref int otherCount, ref int unknownOwnerCount)
        {
            if (target == null || !target.IsMine.HasValue)
            {
                unknownOwnerCount++;
                return;
            }

            if (target.IsMine.Value)
            {
                mineCount++;
            }
            else
            {
                otherCount++;
            }
        }

        private string FormatPetFeedOwnerCounts(int mineCount, int otherCount, int unknownOwnerCount)
        {
            return " mine=" + mineCount + " other=" + otherCount + " unknownOwner=" + unknownOwnerCount;
        }

        private void RegisterPetFeedFoodOptions(List<PetFeedFoodSupply> foods)
        {
            if (foods == null || foods.Count == 0)
            {
                return;
            }

            if (this.petFeedFoodOptions == null)
            {
                this.petFeedFoodOptions = new List<PetFeedFoodOption>();
            }

            Dictionary<int, PetFeedFoodOption> byStaticId = new Dictionary<int, PetFeedFoodOption>();
            foreach (PetFeedFoodOption existing in this.petFeedFoodOptions)
            {
                if (existing != null && existing.StaticId > 0 && !byStaticId.ContainsKey(existing.StaticId))
                {
                    byStaticId[existing.StaticId] = new PetFeedFoodOption
                    {
                        StaticId = existing.StaticId,
                        Name = existing.Name,
                        Count = existing.Count,
                        Fullness = existing.Fullness
                    };
                }
            }

            foreach (PetFeedFoodSupply food in foods)
            {
                if (food == null || food.StaticId <= 0)
                {
                    continue;
                }

                string name = this.GetPetFeedFoodDisplayName(food.StaticId, food.Name);
                if (!byStaticId.TryGetValue(food.StaticId, out PetFeedFoodOption option))
                {
                    option = new PetFeedFoodOption
                    {
                        StaticId = food.StaticId,
                        Name = name,
                        Count = 0,
                        Fullness = food.Fullness
                    };
                    byStaticId[food.StaticId] = option;
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    option.Name = name;
                }
                option.Count += Math.Max(1, food.Count);
                if (option.Fullness <= 0)
                {
                    option.Fullness = food.Fullness;
                }
            }

            this.petFeedFoodOptions.Clear();
            this.petFeedFoodOptions.AddRange(byStaticId.Values
                .OrderBy(option => option.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.StaticId));
            this.ClampPetFeedFoodDropdownScrollIndex();
        }

        private bool ApplyPetFeedSelectedFoodFilter(List<PetFeedFoodSupply> foods, bool requireFood, out string status)
        {
            status = string.Empty;
            if (this.petFeedSelectedFoodStaticId <= 0)
            {
                return true;
            }

            string selectedLabel = this.GetPetFeedSelectedFoodLabel();
            if (foods != null)
            {
                foods.RemoveAll(food => food == null || food.StaticId != this.petFeedSelectedFoodStaticId);
            }

            if ((foods == null || foods.Count == 0) && this.TryAppendCachedPetFeedFoodSupplies(this.petFeedSelectedFoodStaticId, foods))
            {
                return true;
            }

            if (requireFood && (foods == null || foods.Count == 0))
            {
                status = "selected pet food unavailable: " + selectedLabel;
                return false;
            }

            return true;
        }

        private bool TryAppendCachedPetFeedFoodSupplies(int staticId, List<PetFeedFoodSupply> foods)
        {
            if (staticId <= 0 || foods == null || this.autoSellBagItems == null || this.autoSellBagItems.Count == 0)
            {
                return false;
            }

            int fullness = this.TryGetPetFeedFoodFullnessCached(staticId, out int cachedFullness) ? cachedFullness : 0;
            if (fullness <= 0)
            {
                return false;
            }

            int added = 0;
            foreach (AutoSellBagItemEntry entry in this.autoSellBagItems)
            {
                if (entry == null || entry.StaticId != staticId || entry.NetId == 0U || entry.Count <= 0)
                {
                    continue;
                }

                foods.Add(new PetFeedFoodSupply
                {
                    NetId = entry.NetId,
                    Count = Math.Max(1, entry.Count),
                    Fullness = fullness,
                    StaticId = entry.StaticId,
                    Name = this.GetPetFeedFoodDisplayName(entry.StaticId, entry.DisplayName),
                    IsLock = false
                });
                added += Math.Max(1, entry.Count);
            }

            if (added > 0)
            {
                this.PetFeedLog("Selected food fallback from backpack staticId=" + staticId + " addedCount=" + added);
                foods.Sort((a, b) =>
                {
                    int cmp = a.Fullness.CompareTo(b.Fullness);
                    if (cmp != 0) return cmp;
                    return a.StaticId.CompareTo(b.StaticId);
                });
                return true;
            }

            return false;
        }

        private List<PetFeedFoodSupply> GetPetFeedFoodsForTarget(List<PetFeedFoodSupply> foods, PetFeedTarget target)
        {
            return foods;
        }

        private string FormatPetFeedTargetPreferenceStatus(PetFeedTarget target, List<PetFeedUsedFood> usedFoods)
        {
            return string.Empty;
        }

        private string FormatPetFeedSelectedFoodStatus()
        {
            return " selectedFood=\"" + this.GetPetFeedSelectedFoodLabel().Replace("\"", "'") + "\"";
        }

        private string GetPetFeedSelectedFoodLabel()
        {
            if (this.petFeedSelectedFoodStaticId <= 0)
            {
                return "Any Food";
            }

            return this.GetPetFeedFoodDisplayName(this.petFeedSelectedFoodStaticId, this.petFeedSelectedFoodName);
        }

        private string GetPetFeedFoodDisplayName(int staticId, string name)
        {
            name = this.NormalizePetFeedFoodName(staticId, name);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            if (staticId > 0 && this.petFeedFoodNameByStaticId.TryGetValue(staticId, out string cachedName))
            {
                cachedName = this.NormalizePetFeedFoodName(staticId, cachedName);
                if (!string.IsNullOrWhiteSpace(cachedName))
                {
                    return cachedName;
                }
            }

            if (this.TryGetSpecialPetFeedLabel(staticId, out string specialLabel))
            {
                return specialLabel;
            }

            if (staticId > 0 && this.TryGetRadarStaticIdIconKey(staticId, out string spriteKey) && !string.IsNullOrWhiteSpace(spriteKey))
            {
                string spriteLabel = this.NormalizePetFeedFoodName(staticId, this.GetAutoSellItemDisplayName(spriteKey));
                if (!string.IsNullOrWhiteSpace(spriteLabel))
                {
                    return spriteLabel;
                }
            }

            if (this.TryGetPetFeedEntityTypeId(staticId, out int entityTypeId))
            {
                if (entityTypeId == 431)
                {
                    return "Universal Animal Food #" + staticId;
                }

                if (entityTypeId == 402)
                {
                    return "Cat Food #" + staticId;
                }

                if (entityTypeId == 411)
                {
                    return "Dog Food #" + staticId;
                }
            }

            return staticId > 0 ? ("Food #" + staticId) : "Unknown Food";
        }

        private bool TryGetSpecialPetFeedLabel(int staticId, out string label)
        {
            label = string.Empty;
            switch (staticId)
            {
                case 4021:
                    label = "Cat Food";
                    return true;
                case 96000:
                    label = "Dog Food";
                    return true;
                case 823000:
                    label = "Universal Animal Food";
                    return true;
                default:
                    return false;
            }
        }

        private bool TryGetPetFeedFoodIconTexture(int staticId, out Texture2D texture)
        {
            texture = null;
            if (staticId <= 0)
            {
                return false;
            }

            if (this.petFeedFoodIconByStaticId.TryGetValue(staticId, out texture) && texture != null)
            {
                return true;
            }

            List<string> keys = new List<string>();
            if (this.TryGetRadarStaticIdIconKey(staticId, out string spriteKey) && !string.IsNullOrWhiteSpace(spriteKey))
            {
                keys.Add(spriteKey);
                keys.Add(this.GetAutoSellSpriteNameFromMatchKey(spriteKey));
                keys.Add(this.NormalizeAutoSellMatchKey(spriteKey));
            }
            keys.Add(staticId.ToString());

            foreach (string rawKey in keys)
            {
                string key = this.NormalizeRadarIconSpriteKey(rawKey);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (this.autoSellBagItemTextures.TryGetValue(key, out texture) && texture != null)
                {
                    this.petFeedFoodIconByStaticId[staticId] = texture;
                    return true;
                }

                if (this.TryLoadCachedItemIconForPetFeed(staticId, key, out texture) && texture != null)
                {
                    this.autoSellBagItemTextures[key] = texture;
                    this.petFeedFoodIconByStaticId[staticId] = texture;
                    return true;
                }
            }

            return false;
        }

        private bool TryLoadCachedItemIconForPetFeed(int staticId, string key, out Texture2D texture)
        {
            texture = null;
            if (staticId <= 0 || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            int attemptKey = (staticId * 397) ^ key.GetHashCode();
            if (!this.petFeedFoodIconLoadAttempted.Add(attemptKey))
            {
                return false;
            }

            return this.TryLoadCachedItemIcon(key, out texture) && texture != null;
        }

        private void CachePetFeedFoodIconTexture(int staticId, string spriteName, string matchKey)
        {
            if (staticId <= 0 || this.petFeedFoodIconByStaticId.ContainsKey(staticId))
            {
                return;
            }

            List<string> keys = new List<string>();
            keys.Add(spriteName);
            keys.Add(this.GetAutoSellSpriteNameFromMatchKey(matchKey));
            keys.Add(this.NormalizeAutoSellMatchKey(matchKey));
            if (this.TryGetRadarStaticIdIconKey(staticId, out string mappedSpriteKey))
            {
                keys.Add(mappedSpriteKey);
                keys.Add(this.GetAutoSellSpriteNameFromMatchKey(mappedSpriteKey));
                keys.Add(this.NormalizeAutoSellMatchKey(mappedSpriteKey));
            }
            keys.Add(staticId.ToString());

            foreach (string rawKey in keys)
            {
                string key = this.NormalizeRadarIconSpriteKey(rawKey);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (this.autoSellBagItemTextures.TryGetValue(key, out Texture2D texture) && texture != null)
                {
                    this.petFeedFoodIconByStaticId[staticId] = texture;
                    return;
                }

                // Do not load cached PNGs during pet-food scan. Disk texture loads
                // from the OnGUI button path can stall or close the IL2CPP player.
            }
        }

        private void SelectPetFeedFood(int staticId, string name)
        {
            this.petFeedSelectedFoodStaticId = Math.Max(0, staticId);
            this.petFeedSelectedFoodName = this.petFeedSelectedFoodStaticId <= 0
                ? "Any Food"
                : this.GetPetFeedFoodDisplayName(this.petFeedSelectedFoodStaticId, name);
            this.petFeedFoodDropdownOpen = false;
        }

        private void TrySelectDefaultPetFeedFood()
        {
            if (this.petFeedFoodOptions == null || this.petFeedFoodOptions.Count == 0)
            {
                return;
            }

            bool selectedExists = this.petFeedSelectedFoodStaticId > 0
                && this.petFeedFoodOptions.Any(option => option != null && option.StaticId == this.petFeedSelectedFoodStaticId);
            if (selectedExists && !string.Equals(this.petFeedSelectedFoodName, "Any Food", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            PetFeedFoodOption universal = this.petFeedFoodOptions.FirstOrDefault(option =>
                option != null
                && option.StaticId > 0
                && string.Equals(this.GetPetFeedFoodDisplayName(option.StaticId, option.Name), "Universal Animal Food", StringComparison.OrdinalIgnoreCase));
            if (universal == null)
            {
                universal = this.petFeedFoodOptions.FirstOrDefault(option =>
                    option != null
                    && option.StaticId > 0
                    && this.GetPetFeedFoodDisplayName(option.StaticId, option.Name).IndexOf("Universal Animal Food", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (universal != null)
            {
                this.petFeedSelectedFoodStaticId = universal.StaticId;
                this.petFeedSelectedFoodName = this.GetPetFeedFoodDisplayName(universal.StaticId, universal.Name);
            }
        }

        private void ScrollPetFeedFoodDropdown(int delta)
        {
            if (delta == 0)
            {
                return;
            }

            this.petFeedFoodDropdownScrollIndex += delta;
            this.ClampPetFeedFoodDropdownScrollIndex();
        }

        private int GetPetFeedFoodDropdownOptionCount()
        {
            if (this.petFeedFoodOptions == null)
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(this.petFeedFoodSearchText))
            {
                return this.petFeedFoodOptions.Count;
            }

            string search = this.petFeedFoodSearchText.Trim();
            int count = 0;
            foreach (PetFeedFoodOption option in this.petFeedFoodOptions)
            {
                if (this.PetFeedFoodOptionMatchesSearch(option, search))
                {
                    count++;
                }
            }

            return count;
        }

        private List<PetFeedFoodOption> GetPetFeedFoodDropdownOptions()
        {
            List<PetFeedFoodOption> result = new List<PetFeedFoodOption>();
            if (this.petFeedFoodOptions == null)
            {
                return result;
            }

            string search = string.IsNullOrWhiteSpace(this.petFeedFoodSearchText) ? string.Empty : this.petFeedFoodSearchText.Trim();
            foreach (PetFeedFoodOption option in this.petFeedFoodOptions)
            {
                if (string.IsNullOrEmpty(search) || this.PetFeedFoodOptionMatchesSearch(option, search))
                {
                    result.Add(option);
                }
            }

            return result;
        }

        private bool PetFeedFoodOptionMatchesSearch(PetFeedFoodOption option, string search)
        {
            if (option == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            string optionLabel = this.GetPetFeedFoodDisplayName(option.StaticId, option.Name) ?? string.Empty;
            return optionLabel.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || option.StaticId.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetPetFeedFoodDropdownScrollIndexFromTrack(float mouseY, Rect scrollTrackRect, float thumbHeight, int optionCount)
        {
            int maxScroll = Math.Max(0, optionCount - PetFeedFoodVisibleRows);
            if (maxScroll <= 0 || scrollTrackRect.height <= thumbHeight)
            {
                this.petFeedFoodDropdownScrollIndex = 0;
                return;
            }

            float usableHeight = Mathf.Max(1f, scrollTrackRect.height - thumbHeight);
            float normalized = Mathf.Clamp01((mouseY - scrollTrackRect.y - this.petFeedFoodScrollbarDragOffset) / usableHeight);
            this.petFeedFoodDropdownScrollIndex = Mathf.Clamp(Mathf.RoundToInt(normalized * maxScroll), 0, maxScroll);
        }

        private void ClampPetFeedFoodDropdownScrollIndex()
        {
            int optionCount = this.GetPetFeedFoodDropdownOptionCount();
            int maxScroll = Math.Max(0, optionCount - PetFeedFoodVisibleRows);
            if (this.petFeedFoodDropdownScrollIndex < 0)
            {
                this.petFeedFoodDropdownScrollIndex = 0;
            }
            else if (this.petFeedFoodDropdownScrollIndex > maxScroll)
            {
                this.petFeedFoodDropdownScrollIndex = maxScroll;
            }
        }

        private void RefreshPetFeedFoodOptions()
        {
            float now = Time.realtimeSinceStartup;
            if (this.petFeedFoodScanInProgress || now < this.petFeedNextFoodScanAllowedAt)
            {
                this.AddMenuNotification("Pet food scan is cooling down.", new Color(1f, 0.72f, 0.42f));
                return;
            }

            if (this.petFeedFoodOptions == null)
            {
                this.petFeedFoodOptions = new List<PetFeedFoodOption>();
            }

            this.petFeedFoodScanInProgress = true;
            this.petFeedNextFoodScanAllowedAt = now + 1.25f;
            try
            {
                this.petFeedFoodNameByStaticId.Clear();
                int before = this.petFeedFoodOptions.Count;
                List<PetFeedFoodOption> previousOptions = this.petFeedFoodOptions
                    .Where(option => option != null && option.StaticId > 0)
                    .Select(option => new PetFeedFoodOption
                    {
                        StaticId = option.StaticId,
                        Name = option.Name,
                        Count = option.Count,
                        Fullness = option.Fullness
                    })
                    .ToList();
                this.petFeedFoodDropdownOpen = false;
                int scannedItems = this.RefreshPetFeedFoodCacheFromBackpack();
                bool usedPetFoodList = this.TryRefreshPetFeedFoodOptionsFromPetSystem(out int petFoodCount, out string petFoodStatus);
                int cachedAdded = 0;
                if (!usedPetFoodList)
                {
                    int cachedBefore = this.petFeedFoodOptions.Count;
                    this.RegisterPetFeedFoodOptionsFromCachedItems();
                    this.RestoreMissingPetFeedFoodOptions(previousOptions);
                    cachedAdded = Math.Max(0, this.petFeedFoodOptions.Count - cachedBefore);
                }
                this.TrySelectDefaultPetFeedFood();

                int count = this.petFeedFoodOptions.Count;
                this.ClampPetFeedFoodDropdownScrollIndex();
                this.PetFeedLog("Food scan complete: source=" + (usedPetFoodList ? "petSystem" : "backpackFallback")
                    + " backpackItems=" + scannedItems
                    + " petFoods=" + petFoodCount
                    + " cachedAdded=" + cachedAdded
                    + " foodOptions=" + count
                    + " defaultFood=\"" + this.GetPetFeedSelectedFoodLabel().Replace("\"", "'") + "\""
                    + (string.IsNullOrWhiteSpace(petFoodStatus) ? string.Empty : " status=" + petFoodStatus));
                this.LogPetFeedFoodOptionSample();
                if (count > 0)
                {
                    this.AddMenuNotification("Pet food scan: " + count + " food type(s).", new Color(0.45f, 0.88f, 1f));
                }
                else
                {
                    this.AddMenuNotification(before > 0 ? "Pet food list unchanged" : "No pet food found in backpack", new Color(1f, 0.72f, 0.42f));
                }
            }
            finally
            {
                this.petFeedFoodScanInProgress = false;
            }
        }

        private void LogPetFeedFoodOptionSample()
        {
            if (this.petFeedFoodOptions == null || this.petFeedFoodOptions.Count == 0)
            {
                return;
            }

            List<string> sample = this.petFeedFoodOptions
                .Where(option => option != null)
                .Take(12)
                .Select(option => this.GetPetFeedFoodDisplayName(option.StaticId, option.Name) + "#" + option.StaticId + "x" + option.Count)
                .ToList();

            bool hasUniversal = this.petFeedFoodOptions.Any(option => option != null
                && this.GetPetFeedFoodDisplayName(option.StaticId, option.Name).IndexOf("Universal Animal Food", StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasDog = this.petFeedFoodOptions.Any(option => option != null
                && this.GetPetFeedFoodDisplayName(option.StaticId, option.Name).IndexOf("dog", StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasCat = this.petFeedFoodOptions.Any(option => option != null
                && this.GetPetFeedFoodDisplayName(option.StaticId, option.Name).IndexOf("cat", StringComparison.OrdinalIgnoreCase) >= 0);

            this.PetFeedLog("Food option sample: count=" + this.petFeedFoodOptions.Count
                + " hasUniversal=" + hasUniversal
                + " hasCatNamed=" + hasCat
                + " hasDogNamed=" + hasDog
                + " sample=[" + string.Join(", ", sample.ToArray()).Replace("\"", "'") + "]");
        }

        private void RestoreMissingPetFeedFoodOptions(List<PetFeedFoodOption> previousOptions)
        {
            if (previousOptions == null || previousOptions.Count == 0)
            {
                return;
            }

            if (this.petFeedFoodOptions == null)
            {
                this.petFeedFoodOptions = new List<PetFeedFoodOption>();
            }

            HashSet<int> existingIds = new HashSet<int>(this.petFeedFoodOptions
                .Where(option => option != null && option.StaticId > 0)
                .Select(option => option.StaticId));

            foreach (PetFeedFoodOption previous in previousOptions)
            {
                if (previous == null || previous.StaticId <= 0 || existingIds.Contains(previous.StaticId))
                {
                    continue;
                }

                this.petFeedFoodOptions.Add(new PetFeedFoodOption
                {
                    StaticId = previous.StaticId,
                    Name = previous.Name,
                    Count = 0,
                    Fullness = previous.Fullness
                });
                existingIds.Add(previous.StaticId);
            }

            this.petFeedFoodOptions = this.petFeedFoodOptions
                .Where(option => option != null)
                .OrderBy(option => option.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.StaticId)
                .ToList();
            this.ClampPetFeedFoodDropdownScrollIndex();
        }

        private int RefreshPetFeedFoodCacheFromBackpack()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (this.autoSellBagItems != null && now < this.petFeedNextFullBackpackFoodScanAt)
                {
                    return this.autoSellBagItems.Count;
                }

                List<AutoSellBagItemEntry> scannedItems = this.ScanBackpackForAutoSellItems();
                if (scannedItems != null)
                {
                    this.autoSellBagItems = scannedItems;
                    this.SeedPetFeedFoodCacheFromBackpackItems(scannedItems);
                    this.petFeedNextFullBackpackFoodScanAt = now + 6f;
                    return scannedItems.Count;
                }
            }
            catch (Exception ex)
            {
                this.PetFeedLog("Food scan backpack exception: " + ex.Message);
            }

            return this.autoSellBagItems != null ? this.autoSellBagItems.Count : 0;
        }

        private void SeedPetFeedFoodCacheFromBackpackItems(List<AutoSellBagItemEntry> scannedItems)
        {
            if (scannedItems == null || scannedItems.Count == 0)
            {
                return;
            }

            foreach (AutoSellBagItemEntry entry in scannedItems)
            {
                if (entry == null || entry.StaticId <= 0)
                {
                    continue;
                }

                if (entry.EntityType > 0)
                {
                    this.petFeedEntityTypeByStaticId[entry.StaticId] = entry.EntityType;
                }

                string displayName = this.CleanPetFeedFoodName(entry.DisplayName);
                if (!string.IsNullOrWhiteSpace(displayName) && !int.TryParse(displayName, out _))
                {
                    this.petFeedFoodNameByStaticId[entry.StaticId] = displayName;
                }

                if (!string.IsNullOrWhiteSpace(entry.SpriteName))
                {
                    this.RememberRadarStaticIdIconMapping(entry.StaticId, entry.SpriteName);
                    this.CachePetFeedFoodIconTexture(entry.StaticId, entry.SpriteName, displayName);
                }
            }
        }

        private bool TryRefreshPetFeedFoodOptionsFromPetSystem(out int foodCount, out string status)
        {
            foodCount = 0;
            status = string.Empty;

            Dictionary<int, PetFeedFoodSupply> byStaticId = new Dictionary<int, PetFeedFoodSupply>();
            bool catOk = this.TryCollectPetFeedFoods(false, byStaticId, out string catStatus);
            bool dogOk = this.TryCollectPetFeedFoods(true, byStaticId, out string dogStatus);
            foodCount = byStaticId.Count;
            status = "cat=" + catStatus + "; dog=" + dogStatus;

            if (foodCount <= 0)
            {
                return false;
            }

            this.petFeedFoodOptions.Clear();
            List<PetFeedFoodSupply> foods = byStaticId.Values.ToList();
            foods.Sort((a, b) =>
            {
                int cmp = string.Compare(this.GetPetFeedFoodDisplayName(a.StaticId, a.Name), this.GetPetFeedFoodDisplayName(b.StaticId, b.Name), StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
                return a.StaticId.CompareTo(b.StaticId);
            });

            foreach (PetFeedFoodSupply food in foods)
            {
                this.CachePetFeedFoodIconTexture(food.StaticId, string.Empty, food.Name);
            }
            this.RegisterPetFeedFoodOptions(foods);
            return catOk || dogOk;
        }

        private bool TryCollectPetFeedFoods(bool dog, Dictionary<int, PetFeedFoodSupply> byStaticId, out string status)
        {
            if (this.TryCollectPetFeedFoodsManaged(dog, byStaticId, out status))
            {
                return true;
            }

            string managedStatus = status;
            if (this.TryCollectPetFeedFoodsAuraMono(dog, byStaticId, out status))
            {
                return true;
            }

            status = managedStatus + " / " + status;
            return false;
        }

        private bool TryCollectPetFeedFoodsManaged(bool dog, Dictionary<int, PetFeedFoodSupply> byStaticId, out string status)
        {
            status = "managed unavailable";
            try
            {
                if (!this.EnsurePetFeedReflection(out status))
                {
                    return false;
                }

                object petSystem = this.petFeedPetSystemInstanceProperty.GetValue(null, null);
                if (petSystem == null)
                {
                    status = "managed PetSystem unavailable";
                    return false;
                }

                int itemCount = 0;
                int named = 0;
                foreach (string storageName in new[] { "Backpack", "Warehouse" })
                {
                    if (!this.TryGetPetFeedPickerItemsManaged(petSystem, dog, storageName, out IEnumerable items, out string storageStatus))
                    {
                        status = "managed " + storageStatus;
                        return itemCount > 0;
                    }

                    foreach (object item in items)
                    {
                        if (!this.TryReadIntFromMember(item, "staticId", out int staticId) || staticId <= 0)
                        {
                            continue;
                        }

                        int count = this.TryReadIntFromMember(item, "count", out int itemCountValue) ? Math.Max(1, itemCountValue) : 1;
                        uint netId = this.TryReadUIntFromMember(item, "netId", out uint itemNetId) ? itemNetId : 0U;
                        string name = this.ReadPetFeedBackpackItemNameManaged(item);
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            int step = this.TryReadIntFromMember(item, "step", out int itemStep) ? itemStep : 0;
                            this.TryResolvePetFeedFoodNameFromBackpackItemTypeManaged(staticId, step, netId, out name);
                        }
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            this.TryResolvePetFeedFoodNameFromEntityTableManaged(staticId, out name);
                        }
                        name = this.NormalizePetFeedFoodName(staticId, name);

                        if (!byStaticId.TryGetValue(staticId, out PetFeedFoodSupply supply))
                        {
                            supply = new PetFeedFoodSupply
                            {
                                StaticId = staticId,
                                Count = 0,
                                Fullness = this.TryGetPetFeedFoodFullnessCached(staticId, out int fullness) ? fullness : 1,
                                NetId = netId,
                                Name = name,
                                IsLock = false
                            };
                            byStaticId[staticId] = supply;
                        }

                        supply.Count += count;
                        if (supply.NetId == 0U)
                        {
                            supply.NetId = netId;
                        }
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            supply.Name = name;
                            this.petFeedFoodNameByStaticId[staticId] = name;
                            named++;
                        }

                        itemCount++;
                    }
                }

                status = "managed backpackItems=" + itemCount;
                if (named > 0)
                {
                    status += " named=" + named;
                }
                return itemCount > 0;
            }
            catch (Exception ex)
            {
                status = "managed exception: " + (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        private unsafe bool TryCollectPetFeedFoodsAuraMono(bool dog, Dictionary<int, PetFeedFoodSupply> byStaticId, out string status)
        {
            status = "AuraMono unavailable";
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    status = "AuraMono API unavailable";
                    return false;
                }

                if (!this.TryGetPetFeedAuraEntityTypeValue(dog, out int entityTypeValue, out status))
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Pet.PetSystem", out IntPtr petSystemObj) || petSystemObj == IntPtr.Zero)
                {
                    status = "AuraMono PetSystem unavailable";
                    return false;
                }

                int itemCount = 0;
                int named = 0;
                foreach (int storageValue in new[] { 1, 2 })
                {
                    if (!this.TryGetPetFeedPickerItemsAuraMono(petSystemObj, dog, storageValue, out List<IntPtr> foodItems, out string storageStatus))
                    {
                        status = "AuraMono " + storageStatus;
                        return itemCount > 0;
                    }

                    foreach (IntPtr item in foodItems)
                    {
                        if (item == IntPtr.Zero || !this.TryGetMonoIntMember(item, "staticId", out int staticId) || staticId <= 0)
                        {
                            continue;
                        }

                        int count = this.TryGetMonoIntMember(item, "count", out int itemCountValue) ? Math.Max(1, itemCountValue) : 1;
                        uint netId = this.TryGetMonoUIntMember(item, "netId", out uint itemNetId) ? itemNetId : 0U;
                        string name = this.ReadPetFeedBackpackItemNameAuraMono(item);
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            int step = this.TryGetMonoIntMember(item, "step", out int itemStep) ? itemStep : 0;
                            this.TryResolvePetFeedFoodNameFromBackpackItemTypeManaged(staticId, step, netId, out name);
                        }
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            this.TryResolvePetFeedFoodNameFromEntityTableManaged(staticId, out name);
                        }
                        name = this.NormalizePetFeedFoodName(staticId, name);

                        if (!byStaticId.TryGetValue(staticId, out PetFeedFoodSupply supply))
                        {
                            supply = new PetFeedFoodSupply
                            {
                                StaticId = staticId,
                                Count = 0,
                                Fullness = this.TryGetPetFeedFoodFullnessCached(staticId, out int fullness) ? fullness : 1,
                                NetId = netId,
                                Name = name,
                                IsLock = false
                            };
                            byStaticId[staticId] = supply;
                        }

                        supply.Count += count;
                        if (supply.NetId == 0U)
                        {
                            supply.NetId = netId;
                        }
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            supply.Name = name;
                            this.petFeedFoodNameByStaticId[staticId] = name;
                            named++;
                        }

                        itemCount++;
                    }
                }

                status = "AuraMono backpackItems=" + itemCount + " entityType=" + entityTypeValue;
                if (named > 0)
                {
                    status += " named=" + named;
                }
                return itemCount > 0;
            }
            catch (Exception ex)
            {
                status = "AuraMono exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TryGetPetFeedPickerItemsManaged(object petSystem, bool dog, string storageName, out IEnumerable items, out string status)
        {
            items = null;
            status = "picker items unavailable";
            if (petSystem == null || this.petFeedInitFoodsForPickerMethod == null || this.petFeedGetFoodBackpackItemsMethod == null)
            {
                status = "picker methods unavailable";
                return false;
            }

            try
            {
                object petTypeValue = Enum.Parse(this.petFeedPetTypeType, dog ? "Dog" : "Meow");
                object storageTypeValue = Enum.Parse(this.petFeedStorageTypeType, storageName);
                this.petFeedInitFoodsForPickerMethod.Invoke(petSystem, new[] { petTypeValue, storageTypeValue });
                object itemsObj = this.petFeedGetFoodBackpackItemsMethod.Invoke(petSystem, null);
                if (itemsObj is IEnumerable enumerable)
                {
                    items = enumerable;
                    status = storageName;
                    return true;
                }

                status = "GetFoodBackpackItems returned null for " + storageName;
                return false;
            }
            catch (Exception ex)
            {
                status = storageName + " exception: " + (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        private unsafe bool TryGetPetFeedPickerItemsAuraMono(IntPtr petSystemObj, bool dog, int storageTypeValue, out List<IntPtr> items, out string status)
        {
            items = new List<IntPtr>();
            status = "AuraMono picker items unavailable";
            if (petSystemObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono picker prerequisites unavailable";
                return false;
            }

            IntPtr petSystemClass = auraMonoObjectGetClass(petSystemObj);
            IntPtr initFoodsMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "InitFoods", 2);
            IntPtr getFoodBackpackItemsMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "GetFoodBackpackItems", 0);
            if (initFoodsMethod == IntPtr.Zero || getFoodBackpackItemsMethod == IntPtr.Zero)
            {
                status = "picker methods unavailable initFoods=0x" + initFoodsMethod.ToInt64().ToString("X")
                    + " getFoodBackpackItems=0x" + getFoodBackpackItemsMethod.ToInt64().ToString("X");
                return false;
            }

            int petTypeValue = dog ? 2 : 3;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&petTypeValue);
            args[1] = (IntPtr)(&storageTypeValue);
            auraMonoRuntimeInvoke(initFoodsMethod, petSystemObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "InitFoods(" + petTypeValue + "," + storageTypeValue + ") failed exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            exc = IntPtr.Zero;
            IntPtr itemsObj = auraMonoRuntimeInvoke(getFoodBackpackItemsMethod, petSystemObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || itemsObj == IntPtr.Zero)
            {
                status = "GetFoodBackpackItems(" + storageTypeValue + ") failed exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            if (!this.TryEnumerateAuraMonoCollectionItems(itemsObj, items))
            {
                status = "enumeration failed for storage=" + storageTypeValue;
                return false;
            }

            status = "storage=" + storageTypeValue;
            return true;
        }

        private int SupplementPetFeedFoodSuppliesFromManagedBackpackItems(object petSystem, Dictionary<int, PetFeedFoodSupply> byStaticId)
        {
            if (petSystem == null || byStaticId == null || byStaticId.Count == 0)
            {
                return 0;
            }

            try
            {
                MethodInfo getFoodBackpackItemsMethod = petSystem.GetType().GetMethod("GetFoodBackpackItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (getFoodBackpackItemsMethod == null)
                {
                    return 0;
                }

                object itemsObj = getFoodBackpackItemsMethod.Invoke(petSystem, null);
                if (!(itemsObj is IEnumerable items))
                {
                    return 0;
                }

                int named = 0;
                foreach (object item in items)
                {
                    if (!this.TryReadIntFromMember(item, "staticId", out int staticId) || staticId <= 0)
                    {
                        continue;
                    }

                    string name = this.ReadPetFeedBackpackItemNameManaged(item);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        int step = this.TryReadIntFromMember(item, "step", out int itemStep) ? itemStep : 0;
                        uint netId = this.TryReadUIntFromMember(item, "netId", out uint itemNetIdForName) ? itemNetIdForName : 0U;
                        this.TryResolvePetFeedFoodNameFromBackpackItemTypeManaged(staticId, step, netId, out name);
                    }
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        this.TryResolvePetFeedFoodNameFromEntityTableManaged(staticId, out name);
                    }
                    if (!byStaticId.TryGetValue(staticId, out PetFeedFoodSupply supply))
                    {
                        int count = this.TryReadIntFromMember(item, "count", out int itemCount) ? Math.Max(1, itemCount) : 1;
                        uint netId = this.TryReadUIntFromMember(item, "netId", out uint itemNetId) ? itemNetId : 0U;
                        supply = new PetFeedFoodSupply
                        {
                            StaticId = staticId,
                            Count = count,
                            Fullness = this.TryGetPetFeedFoodFullnessCached(staticId, out int fullness) ? fullness : 1,
                            NetId = netId,
                            Name = name,
                            IsLock = false
                        };
                        byStaticId[staticId] = supply;
                    }
                    else if (!string.IsNullOrWhiteSpace(name) && (string.IsNullOrWhiteSpace(supply.Name) || int.TryParse(supply.Name, out _)))
                    {
                        supply.Name = name;
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        this.petFeedFoodNameByStaticId[staticId] = name;
                        named++;
                    }
                }

                return named;
            }
            catch
            {
                return 0;
            }
        }

        private unsafe int SupplementPetFeedFoodSuppliesFromAuraMonoBackpackItems(IntPtr petSystemObj, Dictionary<int, PetFeedFoodSupply> byStaticId)
        {
            if (petSystemObj == IntPtr.Zero || byStaticId == null || byStaticId.Count == 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            try
            {
                IntPtr petSystemClass = auraMonoObjectGetClass(petSystemObj);
                IntPtr getFoodBackpackItemsMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "GetFoodBackpackItems", 0);
                if (getFoodBackpackItemsMethod == IntPtr.Zero)
                {
                    return 0;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr itemsObj = auraMonoRuntimeInvoke(getFoodBackpackItemsMethod, petSystemObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || itemsObj == IntPtr.Zero)
                {
                    return 0;
                }

                List<IntPtr> items = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(itemsObj, items))
                {
                    return 0;
                }

                int named = 0;
                foreach (IntPtr item in items)
                {
                    if (item == IntPtr.Zero || !this.TryGetMonoIntMember(item, "staticId", out int staticId) || staticId <= 0)
                    {
                        continue;
                    }

                    string name = this.ReadPetFeedBackpackItemNameAuraMono(item);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        int step = this.TryGetMonoIntMember(item, "step", out int itemStep) ? itemStep : 0;
                        uint netId = this.TryGetMonoUIntMember(item, "netId", out uint itemNetIdForName) ? itemNetIdForName : 0U;
                        this.TryResolvePetFeedFoodNameFromBackpackItemTypeManaged(staticId, step, netId, out name);
                    }
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        this.TryResolvePetFeedFoodNameFromEntityTableManaged(staticId, out name);
                    }
                    if (!byStaticId.TryGetValue(staticId, out PetFeedFoodSupply supply))
                    {
                        int count = this.TryGetMonoIntMember(item, "count", out int itemCount) ? Math.Max(1, itemCount) : 1;
                        uint netId = this.TryGetMonoUIntMember(item, "netId", out uint itemNetId) ? itemNetId : 0U;
                        supply = new PetFeedFoodSupply
                        {
                            StaticId = staticId,
                            Count = count,
                            Fullness = this.TryGetPetFeedFoodFullnessCached(staticId, out int fullness) ? fullness : 1,
                            NetId = netId,
                            Name = name,
                            IsLock = false
                        };
                        byStaticId[staticId] = supply;
                    }
                    else if (!string.IsNullOrWhiteSpace(name) && (string.IsNullOrWhiteSpace(supply.Name) || int.TryParse(supply.Name, out _)))
                    {
                        supply.Name = name;
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        this.petFeedFoodNameByStaticId[staticId] = name;
                        named++;
                    }
                }

                return named;
            }
            catch
            {
                return 0;
            }
        }

        private bool MergePetFeedFoodSupply(Dictionary<int, PetFeedFoodSupply> byStaticId, PetFeedFoodSupply food)
        {
            if (byStaticId == null || food == null || food.StaticId <= 0 || food.Count <= 0 || food.Fullness <= 0 || food.NetId == 0U || food.IsLock)
            {
                return false;
            }

            if (!byStaticId.TryGetValue(food.StaticId, out PetFeedFoodSupply existing))
            {
                byStaticId[food.StaticId] = food;
                return true;
            }

            existing.Count = Math.Max(existing.Count, food.Count);
            if (existing.Fullness <= 0 || food.Fullness < existing.Fullness)
            {
                existing.Fullness = food.Fullness;
            }
            if (existing.NetId == 0U)
            {
                existing.NetId = food.NetId;
            }
            if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(food.Name))
            {
                existing.Name = food.Name;
            }
            return false;
        }

        private void RegisterPetFeedFoodOptionsFromCachedItems()
        {
            Dictionary<int, PetFeedFoodSupply> byStaticId = new Dictionary<int, PetFeedFoodSupply>();
            if (this.autoSellBagItems != null)
            {
                foreach (AutoSellBagItemEntry entry in this.autoSellBagItems)
                {
                    if (entry == null || entry.StaticId <= 0 || !this.IsLikelyPetFeedFoodEntry(entry))
                    {
                        continue;
                    }

                    if (!byStaticId.TryGetValue(entry.StaticId, out PetFeedFoodSupply supply))
                    {
                        string displayName = this.CleanPetFeedFoodName(entry.DisplayName);
                        if (string.IsNullOrWhiteSpace(displayName) || int.TryParse(displayName, out _))
                        {
                            displayName = this.GetAutoSellItemDisplayName(entry.MatchKey);
                        }
                        displayName = this.NormalizePetFeedFoodName(entry.StaticId, displayName);

                        supply = new PetFeedFoodSupply
                        {
                            StaticId = entry.StaticId,
                            Count = 0,
                            Fullness = this.TryGetPetFeedFoodFullnessCached(entry.StaticId, out int tableFullness) ? tableFullness : 1,
                            Name = displayName
                        };
                        byStaticId[entry.StaticId] = supply;
                    }

                    supply.Count += Math.Max(1, entry.Count);
                    if (!string.IsNullOrWhiteSpace(entry.SpriteName))
                    {
                        this.RememberRadarStaticIdIconMapping(entry.StaticId, entry.SpriteName);
                    }
                    this.CachePetFeedFoodIconTexture(entry.StaticId, entry.SpriteName, entry.MatchKey);
                    if (!string.IsNullOrWhiteSpace(supply.Name))
                    {
                        this.petFeedFoodNameByStaticId[entry.StaticId] = supply.Name;
                    }
                }
            }

            if (byStaticId.Count > 0)
            {
                this.RegisterPetFeedFoodOptions(byStaticId.Values.ToList());
            }
        }

        private bool IsLikelyPetFeedFoodEntry(AutoSellBagItemEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            string text = ((entry.SpriteName ?? string.Empty) + " " + (entry.MatchKey ?? string.Empty) + " " + (entry.DisplayName ?? string.Empty)).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (this.IsPetFeedFoodStaticIdFromTables(entry.StaticId))
            {
                return true;
            }

            string[] foodKeywords = new[] { "food", "bread", "jam", "mushroom", "salad", "soup", "stew", "pie", "cake", "fish", "meat", "fruit", "vegetable", "berry", "apple", "cheese", "egg", "milk", "honey", "candy", "snack", "meal", "dish", "seafood", "octopus", "oyster", "animal" };
            foreach (string keyword in foodKeywords)
            {
                if (text.Contains(keyword))
                {
                    return true;
                }
            }

            return text.Contains("gather_") || text.Contains("fruit_");
        }

        private bool TryGetPetFeedEntityTypeId(int staticId, out int entityTypeId)
        {
            entityTypeId = 0;
            if (staticId <= 0)
            {
                return false;
            }

            if (this.petFeedEntityTypeByStaticId.TryGetValue(staticId, out entityTypeId) && entityTypeId > 0)
            {
                return true;
            }

            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                MethodInfo method = tableDataType?.GetMethod("GetEntityTypeID", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
                if (method == null)
                {
                    return false;
                }

                object value = method.Invoke(null, new object[] { staticId });
                if (value == null)
                {
                    return false;
                }

                entityTypeId = Convert.ToInt32(value);
                if (entityTypeId > 0)
                {
                    this.petFeedEntityTypeByStaticId[staticId] = entityTypeId;
                    return true;
                }
            }
            catch
            {
            }

            entityTypeId = 0;
            return false;
        }

        private bool IsPetFeedFoodStaticIdFromTables(int staticId)
        {
            return this.TryGetPetFeedFoodFullnessCached(staticId, out _);
        }

        private bool TryGetPetFeedFoodFullnessCached(int staticId, out int fullness)
        {
            fullness = 0;
            if (staticId <= 0)
            {
                return false;
            }

            if (this.petFeedFoodFullnessCache.TryGetValue(staticId, out int cached))
            {
                fullness = cached;
                return cached > 0;
            }

            if (this.TryGetPetFeedFoodFullnessFromTables(staticId, out fullness) && fullness > 0)
            {
                this.petFeedFoodFullnessCache[staticId] = fullness;
                return true;
            }

            this.petFeedFoodFullnessCache[staticId] = 0;
            fullness = 0;
            return false;
        }

        private bool TryGetPetFeedFoodFullnessFromTables(int staticId, out int fullness)
        {
            fullness = 0;
            if (staticId <= 0)
            {
                return false;
            }

            if (this.TryGetPetFeedFoodFullnessFromTablesManaged(staticId, out fullness))
            {
                return true;
            }

            return this.TryGetPetFeedFoodFullnessFromTablesAuraMono(staticId, out fullness);
        }

        private bool TryGetPetFeedFoodFullnessFromTablesManaged(int staticId, out int fullness)
        {
            fullness = 0;
            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    return false;
                }

                foreach (string methodName in new[] { "GetPetFood", "GetDogfood", "GetCatfood" })
                {
                    MethodInfo method = tableDataType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(bool) }, null);
                    if (method == null)
                    {
                        continue;
                    }

                    object tableObj = method.Invoke(null, new object[] { staticId, false });
                    if (tableObj == null)
                    {
                        continue;
                    }

                    if (this.TryReadPetFeedFoodFullnessFromTableObject(tableObj, out fullness))
                    {
                        return true;
                    }

                    fullness = 1;
                    return true;
                }
            }
            catch
            {
            }

            fullness = 0;
            return false;
        }

        private unsafe bool TryGetPetFeedFoodFullnessFromTablesAuraMono(int staticId, out int fullness)
        {
            fullness = 0;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr tableDataClass = this.TryGetPetFeedAuraMonoTableDataClass();
                if (tableDataClass == IntPtr.Zero)
                {
                    return false;
                }

                foreach (string methodName in new[] { "GetPetFood", "GetDogfood", "GetCatfood" })
                {
                    IntPtr method = this.FindAuraMonoMethodOnHierarchy(tableDataClass, methodName, 2);
                    if (method == IntPtr.Zero)
                    {
                        continue;
                    }

                    bool needException = false;
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)(&staticId);
                    args[1] = (IntPtr)(&needException);
                    IntPtr exc = IntPtr.Zero;
                    IntPtr tableObj = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || tableObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.TryReadPetFeedFoodFullnessFromTableObjectAuraMono(tableObj, out fullness))
                    {
                        return true;
                    }

                    fullness = 1;
                    return true;
                }
            }
            catch
            {
            }

            fullness = 0;
            return false;
        }

        private bool TryReadPetFeedFoodFullnessFromTableObject(object tableObj, out int fullness)
        {
            fullness = 0;
            foreach (string memberName in new[] { "catFoodFullness", "dogFoodFullness", "foodFullness", "petFoodFullness" })
            {
                if (this.TryReadIntListFromMember(tableObj, memberName, out List<int> values) && values != null && values.Count > 0)
                {
                    fullness = values.Where(value => value > 0).DefaultIfEmpty(1).Min();
                    return true;
                }
            }

            return false;
        }

        private bool TryReadPetFeedFoodFullnessFromTableObjectAuraMono(IntPtr tableObj, out int fullness)
        {
            fullness = 0;
            foreach (string memberName in new[] { "catFoodFullness", "dogFoodFullness", "foodFullness", "petFoodFullness" })
            {
                if (this.TryReadMonoIntListMember(tableObj, memberName, out List<int> values) && values != null && values.Count > 0)
                {
                    fullness = values.Where(value => value > 0).DefaultIfEmpty(1).Min();
                    return true;
                }
            }

            return false;
        }

        private bool TryGetPetFeedFoodSupply(object foodObj, out PetFeedFoodSupply food)
        {
            food = null;
            if (foodObj == null
                || !this.TryReadUIntFromMember(foodObj, "netId", out uint netId)
                || !this.TryReadIntFromMember(foodObj, "count", out int count)
                || !this.TryReadIntFromMember(foodObj, "foodFullness", out int fullness))
            {
                return false;
            }

            this.TryReadIntFromMember(foodObj, "staticId", out int staticId);
            bool isLock = false;
            if (this.TryGetObjectMember(foodObj, "isLock", out object lockObj) && lockObj != null)
            {
                try { isLock = Convert.ToBoolean(lockObj); } catch { }
            }

            food = new PetFeedFoodSupply
            {
                NetId = netId,
                Count = count,
                Fullness = fullness,
                StaticId = staticId,
                Name = this.ResolvePetFeedFoodName(staticId, foodObj),
                IsLock = isLock
            };
            return true;
        }

        private List<PetFeedUsedFood> TakePetFeedFood(List<PetFeedFoodSupply> foods, int neededFullness)
        {
            List<PetFeedUsedFood> result = new List<PetFeedUsedFood>();
            if (foods == null || neededFullness <= 0)
            {
                return result;
            }

            int addedFullness = 0;
            for (int i = 0; i < foods.Count && addedFullness < neededFullness && result.Count < 100; i++)
            {
                PetFeedFoodSupply food = foods[i];
                while (food.Count > 0 && addedFullness < neededFullness && result.Count < 100)
                {
                    result.Add(new PetFeedUsedFood
                    {
                        NetId = food.NetId,
                        StaticId = food.StaticId,
                        Fullness = food.Fullness,
                        Name = food.Name
                    });
                    food.Count--;
                    addedFullness += food.Fullness;
                }
            }

            return result;
        }

        private bool CanAttemptPetFeedTarget(PetFeedTarget target)
        {
            if (target == null)
            {
                return false;
            }

            if (target.CurrentFullness < target.MaxFullness)
            {
                return true;
            }

            if (target.IsMine != true)
            {
                return !this.WasPetFeedProbeAttemptedRecently(target.NetId);
            }

            return false;
        }

        private int GetPetFeedNeededFullness(PetFeedTarget target, List<PetFeedFoodSupply> foods)
        {
            if (target == null || foods == null || foods.Count == 0)
            {
                return 0;
            }

            int neededFullness = target.MaxFullness - target.CurrentFullness;
            if (neededFullness > 0)
            {
                return neededFullness;
            }

            if (target.IsMine != true)
            {
                if (this.WasPetFeedProbeAttemptedRecently(target.NetId))
                {
                    return 0;
                }

                PetFeedFoodSupply probeFood = foods.FirstOrDefault(food => food != null && food.Count > 0 && food.Fullness > 0);
                if (probeFood != null)
                {
                    return 1;
                }
            }

            return 0;
        }

        private bool WasPetFeedProbeAttemptedRecently(uint petNetId)
        {
            if (petNetId == 0U)
            {
                return false;
            }

            if (!this.petFeedProbeAttemptedAt.TryGetValue(petNetId, out float lastAt))
            {
                return false;
            }

            if (Time.realtimeSinceStartup - lastAt <= PetFeedProbeCooldownSeconds)
            {
                return true;
            }

            this.petFeedProbeAttemptedAt.Remove(petNetId);
            return false;
        }

        private string FormatPetFeedUsedFoods(List<PetFeedUsedFood> foods)
        {
            if (foods == null || foods.Count == 0)
            {
                return "[]";
            }

            List<string> parts = new List<string>();
            foreach (PetFeedUsedFood food in foods)
            {
                if (food == null)
                {
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(food.Name) ? "?" : food.Name;
                parts.Add("{name=\"" + name.Replace("\"", "'") + "\",staticId=" + food.StaticId + ",netId=" + food.NetId + ",fullness=" + food.Fullness + "}");
            }

            return "[" + string.Join(",", parts.ToArray()) + "]";
        }

        private string ResolvePetFeedFoodName(int staticId, object foodObj)
        {
            if (staticId > 0 && this.petFeedFoodNameByStaticId.TryGetValue(staticId, out string cachedName))
            {
                cachedName = this.NormalizePetFeedFoodName(staticId, cachedName);
                if (!string.IsNullOrWhiteSpace(cachedName))
                {
                    return cachedName;
                }
            }

            string name = string.Empty;
            if (foodObj != null && this.TryReadPetFeedFoodNameFromManagedObject(foodObj, out name))
            {
                return this.CachePetFeedFoodName(staticId, name);
            }

            if (this.TryResolvePetFeedFoodNameFromManagedTable(staticId, out name))
            {
                return this.CachePetFeedFoodName(staticId, name);
            }

            if (this.TryResolvePetFeedFoodNameFromEntityTableManaged(staticId, out name))
            {
                return this.CachePetFeedFoodName(staticId, name);
            }

            if (staticId > 0 && this.TryGetRadarStaticIdIconKey(staticId, out string spriteKey) && !string.IsNullOrWhiteSpace(spriteKey))
            {
                return this.CachePetFeedFoodName(staticId, this.GetAutoSellItemDisplayName(spriteKey));
            }

            return string.Empty;
        }

        private unsafe string ResolvePetFeedFoodName(int staticId, IntPtr foodObj)
        {
            if (staticId > 0 && this.petFeedFoodNameByStaticId.TryGetValue(staticId, out string cachedName))
            {
                cachedName = this.NormalizePetFeedFoodName(staticId, cachedName);
                if (!string.IsNullOrWhiteSpace(cachedName))
                {
                    return cachedName;
                }
            }

            string name = string.Empty;
            if (foodObj != IntPtr.Zero && this.TryReadPetFeedFoodNameFromAuraMonoObject(foodObj, out name))
            {
                return this.CachePetFeedFoodName(staticId, name);
            }

            if (this.TryResolvePetFeedFoodNameFromManagedTable(staticId, out name)
                || this.TryResolvePetFeedFoodNameFromAuraMonoTable(staticId, out name))
            {
                return this.CachePetFeedFoodName(staticId, name);
            }

            if (this.TryResolvePetFeedFoodNameFromEntityTableManaged(staticId, out name))
            {
                return this.CachePetFeedFoodName(staticId, name);
            }

            if (staticId > 0 && this.TryGetRadarStaticIdIconKey(staticId, out string spriteKey) && !string.IsNullOrWhiteSpace(spriteKey))
            {
                return this.CachePetFeedFoodName(staticId, this.GetAutoSellItemDisplayName(spriteKey));
            }

            return string.Empty;
        }

        private string CachePetFeedFoodName(int staticId, string name)
        {
            name = this.NormalizePetFeedFoodName(staticId, name);
            if (staticId > 0 && !string.IsNullOrWhiteSpace(name))
            {
                this.petFeedFoodNameByStaticId[staticId] = name;
            }

            return name;
        }

        private string NormalizePetFeedFoodName(int staticId, string name)
        {
            name = this.CleanPetFeedFoodName(name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            name = this.PrettifyPetFeedInternalName(name);

            string digitsOnly = new string(name.Where(char.IsDigit).ToArray());
            bool hasLetter = name.Any(char.IsLetter);
            bool hasCjk = name.Any(ch => ch >= 0x2E80);
            if (!hasLetter && !hasCjk && digitsOnly.Length > 0)
            {
                return string.Empty;
            }

            if (int.TryParse(name, out int numericName) && numericName > 0)
            {
                return string.Empty;
            }

            if (staticId > 0 && string.Equals(digitsOnly, staticId.ToString(), StringComparison.Ordinal))
            {
                return string.Empty;
            }

            if (!this.IsAcceptablePetFeedFoodLabel(staticId, name))
            {
                return string.Empty;
            }

            return name;
        }

        private bool IsAcceptablePetFeedFoodLabel(int staticId, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || staticId <= 0)
            {
                return !string.IsNullOrWhiteSpace(name);
            }

            if (this.TryGetSpecialPetFeedLabel(staticId, out _))
            {
                return true;
            }

            if (!this.IsPetFeedFoodStaticIdFromTables(staticId))
            {
                return true;
            }

            string lowered = name.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lowered))
            {
                return false;
            }

            if (lowered.Any(ch => ch >= 0x2E80))
            {
                return true;
            }

            string[] rejectedKeywords = new[]
            {
                "swordman", "swordsman", "bowman", "archer", "warrior", "guard",
                "villager", "merchant", "npc", "monster", "soldier", "farmer"
            };
            foreach (string keyword in rejectedKeywords)
            {
                if (lowered.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            string[] acceptedKeywords = new[]
            {
                "food", "feed", "treat", "kibble", "ration", "meal", "snack", "animal", "pet",
                "cat", "dog", "universal", "bread", "cake", "pie", "cookie", "biscuit", "candy",
                "jam", "soup", "stew", "salad", "rice", "noodle", "porridge", "sandwich", "burger",
                "pizza", "fish", "meat", "seafood", "shrimp", "crab", "oyster", "octopus", "egg",
                "milk", "cheese", "honey", "fruit", "berry", "apple", "mushroom", "vegetable",
                "veggie", "corn", "wheat", "flour", "bean", "nut", "tofu", "dessert", "tea",
                "coffee", "sushi", "sashimi", "mochi", "dumpling"
            };
            foreach (string keyword in acceptedKeywords)
            {
                if (lowered.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private string PrettifyPetFeedInternalName(string name)
        {
            string raw = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string key = raw.ToLowerInvariant()
                .Replace("ui_item_normal_", string.Empty)
                .Replace("ui_item_special_", string.Empty)
                .Replace("sprite_", string.Empty);

            if (key.StartsWith("p_", StringComparison.Ordinal))
            {
                key = key.Substring(2);
            }

            if (string.Equals(key, raw, StringComparison.Ordinal) && !raw.Contains("_"))
            {
                return raw;
            }

            string[] parts = key.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return raw;
            }

            if (parts.Length >= 2 && string.Equals(parts[0], parts[1], StringComparison.OrdinalIgnoreCase))
            {
                parts = parts.Skip(1).ToArray();
            }

            string pretty = string.Join(" ", parts);
            if (string.IsNullOrWhiteSpace(pretty))
            {
                return raw;
            }

            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(pretty);
        }

        private string CleanPetFeedFoodName(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            return name.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        }

        private string ReadPetFeedBackpackItemNameManaged(object item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            try
            {
                PropertyInfo nameProperty = item.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object rawName = nameProperty != null ? nameProperty.GetValue(item, null) : null;
                if (rawName == null && this.TryGetObjectMember(item, "Name", out object memberName))
                {
                    rawName = memberName;
                }
                if (rawName != null)
                {
                    string value = this.NormalizePetFeedFoodName(this.TryReadIntFromMember(item, "staticId", out int staticId) ? staticId : 0, rawName.ToString());
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            if (this.TryReadPetFeedFoodNameFromManagedObject(item, out string name))
            {
                return this.NormalizePetFeedFoodName(this.TryReadIntFromMember(item, "staticId", out int staticId) ? staticId : 0, name);
            }

            if (this.TryGetObjectMember(item, "icon", out object rawIcon) && rawIcon != null)
            {
                string icon = this.CleanPetFeedFoodName(rawIcon.ToString());
                if (!string.IsNullOrWhiteSpace(icon) && !int.TryParse(icon, out _))
                {
                    return this.GetAutoSellItemDisplayName(icon);
                }
            }

            return string.Empty;
        }

        private string ReadPetFeedBackpackItemNameAuraMono(IntPtr item)
        {
            if (item == IntPtr.Zero)
            {
                return string.Empty;
            }

            if (this.TryGetMonoStringMember(item, "Name", out string propertyName))
            {
                string normalizedName = this.NormalizePetFeedFoodName(this.TryGetMonoIntMember(item, "staticId", out int staticId) ? staticId : 0, propertyName);
                if (!string.IsNullOrWhiteSpace(normalizedName))
                {
                    return normalizedName;
                }
            }

            if (this.TryReadPetFeedFoodNameFromAuraMonoObject(item, out string name))
            {
                return this.NormalizePetFeedFoodName(this.TryGetMonoIntMember(item, "staticId", out int staticId) ? staticId : 0, name);
            }

            if (this.TryGetMonoStringMember(item, "icon", out string icon)
                && !string.IsNullOrWhiteSpace(icon)
                && !int.TryParse(icon, out _))
            {
                return this.GetAutoSellItemDisplayName(icon);
            }

            return string.Empty;
        }

        private bool TryResolvePetFeedFoodNameFromBackpackItemTypeManaged(int staticId, int step, uint netId, out string name)
        {
            name = string.Empty;
            if (staticId <= 0)
            {
                return false;
            }

            try
            {
                Type backpackItemType = this.FindLoadedType("BackpackItem", "XDTGameSystem.UISystem.BackPack.BackpackItem", "UISystem.BackPack.BackpackItem");
                MethodInfo method = backpackItemType?.GetMethod("GetBackPackName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int), typeof(uint) }, null);
                if (method == null)
                {
                    return false;
                }

                object rawName = method.Invoke(null, new object[] { staticId, step, netId });
                name = this.NormalizePetFeedFoodName(staticId, rawName?.ToString());
                return !string.IsNullOrWhiteSpace(name);
            }
            catch
            {
                name = string.Empty;
                return false;
            }
        }

        private bool TryResolvePetFeedFoodNameFromEntityTableManaged(int staticId, out string name)
        {
            name = string.Empty;
            if (staticId <= 0)
            {
                return false;
            }

            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                MethodInfo method = tableDataType?.GetMethod("GetEntity", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(bool) }, null);
                if (method == null)
                {
                    return false;
                }

                object entityObj = method.Invoke(null, new object[] { staticId, false });
                if (entityObj == null)
                {
                    return false;
                }

                PropertyInfo nameProperty = entityObj.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object rawName = nameProperty != null ? nameProperty.GetValue(entityObj, null) : null;
                if (rawName == null && this.TryGetObjectMember(entityObj, "name", out object memberName))
                {
                    rawName = memberName;
                }

                name = this.CleanPetFeedFoodName(rawName?.ToString());
                return !string.IsNullOrWhiteSpace(name) && !int.TryParse(name, out _);
            }
            catch
            {
                name = string.Empty;
                return false;
            }
        }

        private bool TryReadPetFeedFoodNameFromManagedObject(object obj, out string name)
        {
            name = string.Empty;
            if (obj == null)
            {
                return false;
            }

            foreach (string member in new[] { "name", "_name", "Name", "itemName", "_itemName", "ItemName", "displayName", "_displayName", "DisplayName", "icon", "_icon", "Icon", "iconName", "itemIcon" })
            {
                if (this.TryGetObjectMember(obj, member, out object raw) && raw != null)
                {
                    string value = raw.ToString();
                    if (!string.IsNullOrWhiteSpace(value) && !int.TryParse(value, out _))
                    {
                        name = value;
                        return true;
                    }
                }
            }

            foreach (string member in new[] { "item", "_item", "itemData", "_itemData", "baseData", "_baseData", "config", "_config", "tableData", "_tableData" })
            {
                if (this.TryGetObjectMember(obj, member, out object nested)
                    && nested != null
                    && !object.ReferenceEquals(nested, obj)
                    && this.TryReadPetFeedFoodNameFromManagedObject(nested, out name))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryResolvePetFeedFoodNameFromManagedTable(int staticId, out string name)
        {
            name = string.Empty;
            if (staticId <= 0)
            {
                return false;
            }

            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    return false;
                }

                foreach (string methodName in new[] { "GetPetFood", "GetDogfood", "GetCatfood" })
                {
                    MethodInfo method = tableDataType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(bool) }, null);
                    if (method == null)
                    {
                        continue;
                    }

                    object tableObj = method.Invoke(null, new object[] { staticId, false });
                    if (tableObj == null)
                    {
                        continue;
                    }

                    if (this.TryResolvePetFeedFoodNameFromManagedTableObject(tableObj, staticId, out name))
                    {
                        return true;
                    }
                }

                foreach (string methodName in new[] { "GetItem", "GetItems", "GetItemData", "GetItemBase", "GetProp", "GetGoods", "GetFood" })
                {
                    foreach (MethodInfo method in tableDataType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!string.Equals(method.Name, methodName, StringComparison.Ordinal)
                            || method.ReturnType == typeof(void))
                        {
                            continue;
                        }

                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length < 1 || parameters.Length > 2 || parameters[0].ParameterType != typeof(int))
                        {
                            continue;
                        }

                        object[] args = parameters.Length == 1 ? new object[] { staticId } : new object[] { staticId, true };
                        object item = method.Invoke(null, args);
                        if (item is string text && !string.IsNullOrWhiteSpace(text))
                        {
                            name = text;
                            return true;
                        }
                        if (item != null && this.TryReadPetFeedFoodNameFromManagedObject(item, out name))
                        {
                            return true;
                        }
                    }
                }

                foreach (string fieldName in new[] { "TableItems", "TableItem", "TableItemDatas", "TableItemBases", "TableProps", "TableGoods", "TableFoods" })
                {
                    FieldInfo field = tableDataType.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    object tableObj = field?.GetValue(null);
                    if (this.TryResolvePetFeedFoodNameFromManagedTableObject(tableObj, staticId, out name))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryResolvePetFeedFoodNameFromManagedTableObject(object tableObj, int staticId, out string name)
        {
            name = string.Empty;
            if (tableObj == null)
            {
                return false;
            }

            try
            {
                if (tableObj is IDictionary dictionary)
                {
                    object item = null;
                    if (dictionary.Contains(staticId))
                    {
                        item = dictionary[staticId];
                    }
                    else
                    {
                        foreach (DictionaryEntry entry in dictionary)
                        {
                            if (this.ObjectMatchesPetFeedStaticId(entry.Key, staticId))
                            {
                                item = entry.Value;
                                break;
                            }
                        }
                    }

                    return item != null
                        && (this.TryReadPetFeedFoodNameFromManagedObject(item, out name)
                            || this.TryResolvePetFeedFoodNameFromManagedLinkedItem(item, staticId, out name));
                }

                if (tableObj is IEnumerable enumerable)
                {
                    foreach (object entry in enumerable)
                    {
                        object item = entry;
                        if (entry != null && this.TryGetObjectMember(entry, "Value", out object value) && value != null)
                        {
                            item = value;
                        }

                        if (item != null
                            && this.ObjectMatchesPetFeedStaticId(item, staticId)
                            && (this.TryReadPetFeedFoodNameFromManagedObject(item, out name)
                                || this.TryResolvePetFeedFoodNameFromManagedLinkedItem(item, staticId, out name)))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryResolvePetFeedFoodNameFromManagedLinkedItem(object obj, int staticId, out string name)
        {
            name = string.Empty;
            if (obj == null)
            {
                return false;
            }

            foreach (string member in new[] { "itemId", "_itemId", "ItemId", "goodsId", "_goodsId", "GoodsId", "foodId", "_foodId", "FoodId", "propId", "_propId", "PropId", "templateId", "_templateId", "TemplateId" })
            {
                if (!this.TryReadIntFromMember(obj, member, out int linkedId) || linkedId <= 0 || linkedId == staticId)
                {
                    continue;
                }

                if (this.TryResolvePetFeedFoodNameFromManagedTable(linkedId, out name))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ObjectMatchesPetFeedStaticId(object obj, int staticId)
        {
            if (obj == null || staticId <= 0)
            {
                return false;
            }

            try
            {
                if (Convert.ToInt32(obj) == staticId)
                {
                    return true;
                }
            }
            catch
            {
            }

            foreach (string member in new[] { "id", "_id", "Id", "staticId", "_staticId", "StaticId", "itemId", "_itemId", "ItemId" })
            {
                if (this.TryReadIntFromMember(obj, member, out int value) && value == staticId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryReadPetFeedFoodNameFromAuraMonoObject(IntPtr obj, out string name)
        {
            name = string.Empty;
            if (obj == IntPtr.Zero)
            {
                return false;
            }

            foreach (string member in new[] { "name", "_name", "Name", "itemName", "_itemName", "ItemName", "displayName", "_displayName", "DisplayName", "icon", "_icon", "Icon", "iconName", "itemIcon" })
            {
                if (this.TryGetMonoStringMember(obj, member, out string value)
                    && !string.IsNullOrWhiteSpace(value)
                    && !int.TryParse(value, out _))
                {
                    name = value;
                    return true;
                }
            }

            foreach (string member in new[] { "item", "_item", "itemData", "_itemData", "baseData", "_baseData", "config", "_config", "tableData", "_tableData" })
            {
                if (this.TryGetMonoObjectMember(obj, member, out IntPtr nested)
                    && nested != IntPtr.Zero
                    && nested != obj
                    && this.TryReadPetFeedFoodNameFromAuraMonoObject(nested, out name))
                {
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryResolvePetFeedFoodNameFromAuraMonoTable(int staticId, out string name)
        {
            name = string.Empty;
            if (staticId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
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
                if (tableDataClass == IntPtr.Zero)
                {
                    return false;
                }

                foreach (string methodName in new[] { "GetPetFood", "GetDogfood", "GetCatfood" })
                {
                    IntPtr method = this.FindAuraMonoMethodOnHierarchy(tableDataClass, methodName, 2);
                    if (method == IntPtr.Zero)
                    {
                        continue;
                    }

                    bool needException = false;
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)(&staticId);
                    args[1] = (IntPtr)(&needException);
                    IntPtr exc = IntPtr.Zero;
                    IntPtr tableObj = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || tableObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.TryResolvePetFeedFoodNameFromAuraMonoTableObject(tableObj, staticId, out name))
                    {
                        return true;
                    }
                }

                foreach (string methodName in new[] { "GetItem", "GetItems", "GetItemData", "GetItemBase", "GetProp", "GetGoods", "GetFood" })
                {
                    int methodArgCount = 2;
                    IntPtr method = this.FindAuraMonoMethodOnHierarchy(tableDataClass, methodName, methodArgCount);
                    if (method == IntPtr.Zero)
                    {
                        methodArgCount = 1;
                        method = this.FindAuraMonoMethodOnHierarchy(tableDataClass, methodName, methodArgCount);
                    }
                    if (method == IntPtr.Zero)
                    {
                        continue;
                    }

                    bool strict = true;
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)(&staticId);
                    args[1] = (IntPtr)(&strict);
                    IntPtr exc = IntPtr.Zero;
                    IntPtr itemObj = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc == IntPtr.Zero && itemObj != IntPtr.Zero && this.TryReadPetFeedFoodNameFromAuraMonoObject(itemObj, out name))
                    {
                        return true;
                    }
                }

                foreach (string fieldName in new[] { "TableItems", "TableItem", "TableItemDatas", "TableItemBases", "TableProps", "TableGoods", "TableFoods" })
                {
                    if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, fieldName, out IntPtr tableObj) || tableObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.TryResolvePetFeedFoodNameFromAuraMonoTableObject(tableObj, staticId, out name))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryResolvePetFeedFoodNameFromAuraMonoTableObject(IntPtr tableObj, int staticId, out string name)
        {
            name = string.Empty;
            if (tableObj == IntPtr.Zero || staticId <= 0)
            {
                return false;
            }

            List<IntPtr> items = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(tableObj, items))
            {
                return false;
            }

            foreach (IntPtr entryObj in items)
            {
                if (entryObj == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr itemObj = entryObj;
                if (this.TryGetMonoObjectMember(entryObj, "Value", out IntPtr valueObj) && valueObj != IntPtr.Zero)
                {
                    itemObj = valueObj;
                }

                if (this.AuraMonoObjectMatchesPetFeedStaticId(itemObj, staticId)
                    && (this.TryReadPetFeedFoodNameFromAuraMonoObject(itemObj, out name)
                        || this.TryResolvePetFeedFoodNameFromAuraMonoLinkedItem(itemObj, staticId, out name)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryResolvePetFeedFoodNameFromAuraMonoLinkedItem(IntPtr obj, int staticId, out string name)
        {
            name = string.Empty;
            if (obj == IntPtr.Zero)
            {
                return false;
            }

            foreach (string member in new[] { "itemId", "_itemId", "ItemId", "goodsId", "_goodsId", "GoodsId", "foodId", "_foodId", "FoodId", "propId", "_propId", "PropId", "templateId", "_templateId", "TemplateId" })
            {
                if (!this.TryGetMonoIntMember(obj, member, out int linkedId) || linkedId <= 0 || linkedId == staticId)
                {
                    continue;
                }

                if (this.TryResolvePetFeedFoodNameFromManagedTable(linkedId, out name)
                    || this.TryResolvePetFeedFoodNameFromAuraMonoTable(linkedId, out name))
                {
                    return true;
                }
            }

            return false;
        }

        private bool AuraMonoObjectMatchesPetFeedStaticId(IntPtr obj, int staticId)
        {
            if (obj == IntPtr.Zero || staticId <= 0)
            {
                return false;
            }

            foreach (string member in new[] { "id", "_id", "Id", "staticId", "_staticId", "StaticId", "itemId", "_itemId", "ItemId" })
            {
                if (this.TryGetMonoIntMember(obj, member, out int value) && value == staticId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryInvokePetFeedPrepare(uint petNetId, out string status)
        {
            status = string.Empty;
            if (petNetId == 0U)
            {
                status = "empty pet request";
                return false;
            }

            try
            {
                if (!this.EnsurePetFeedReflection(out status))
                {
                    string managedStatus = status;
                    if (this.TryInvokePetFeedPrepareAuraMono(petNetId, out status))
                    {
                        return true;
                    }

                    status = managedStatus + ". " + status;
                    return false;
                }

                this.petFeedPrepareMethod.Invoke(null, new object[] { petNetId });
                return true;
            }
            catch (Exception ex)
            {
                status = (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        private bool TryInvokePetFeedBegin(uint petNetId, List<uint> foodNetIds, out string status)
        {
            status = string.Empty;
            if (petNetId == 0U || foodNetIds == null || foodNetIds.Count == 0)
            {
                status = "empty feed request";
                return false;
            }

            try
            {
                if (!this.EnsurePetFeedReflection(out status))
                {
                    string managedStatus = status;
                    if (this.TryInvokePetFeedBeginAuraMono(petNetId, foodNetIds, out status))
                    {
                        return true;
                    }

                    status = managedStatus + ". " + status;
                    return false;
                }

                this.petFeedBeginMethod.Invoke(null, new object[] { petNetId, foodNetIds, 0U });
                return true;
            }
            catch (Exception ex)
            {
                status = (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        private unsafe bool TryInvokePetFeedPrepareAuraMono(uint petNetId, out string status)
        {
            status = "AuraMono PrepareFeed unavailable";
            try
            {
                if (!this.TryResolvePetFeedAuraProtocol(out status))
                {
                    return false;
                }

                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&petNetId);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.petFeedAuraPrepareMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "AuraMono PrepareFeed failed exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                status = "AuraMono PrepareFeed ok";
                return true;
            }
            catch (Exception ex)
            {
                status = "AuraMono PrepareFeed exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryInvokePetFeedBeginAuraMono(uint petNetId, List<uint> foodNetIds, out string status)
        {
            status = "AuraMono BeginFeed unavailable";
            try
            {
                if (!this.TryResolvePetFeedAuraProtocol(out status))
                {
                    return false;
                }

                if (!this.TryCreatePetFeedAuraUIntList(foodNetIds, out IntPtr foodListObj, out status) || foodListObj == IntPtr.Zero)
                {
                    return false;
                }

                uint toolNetId = 0U;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&petNetId);
                args[1] = foodListObj;
                args[2] = (IntPtr)(&toolNetId);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.petFeedAuraBeginMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "AuraMono BeginFeed failed exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                status = "AuraMono BeginFeed ok";
                return true;
            }
            catch (Exception ex)
            {
                status = "AuraMono BeginFeed exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TryResolvePetFeedAuraProtocol(out string status)
        {
            status = string.Empty;
            if (this.petFeedAuraPrepareMethod != IntPtr.Zero && this.petFeedAuraBeginMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono protocol API unavailable";
                return false;
            }

            IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Pet.PetProtocolManager");
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.Pet", "PetProtocolManager");
            }
            if (protocolClass == IntPtr.Zero)
            {
                status = "AuraMono PetProtocolManager class unavailable";
                return false;
            }

            this.petFeedAuraPrepareMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "PrepareFeed", 1);
            this.petFeedAuraBeginMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "BeginFeed", 3);
            if (this.petFeedAuraPrepareMethod == IntPtr.Zero || this.petFeedAuraBeginMethod == IntPtr.Zero)
            {
                status = "AuraMono PetProtocolManager method(s) unavailable prepare=0x" + this.petFeedAuraPrepareMethod.ToInt64().ToString("X")
                    + " begin=0x" + this.petFeedAuraBeginMethod.ToInt64().ToString("X");
                return false;
            }

            return true;
        }

        private unsafe bool TryCreatePetFeedAuraUIntList(List<uint> values, out IntPtr listObj, out string status)
        {
            listObj = IntPtr.Zero;
            status = string.Empty;
            if (values == null || values.Count == 0)
            {
                status = "AuraMono food list empty";
                return false;
            }

            this.ResolveAuraFarmRuntimeMethodsViaMono();
            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoStringNew == null
                || auraMonoObjectGetClass == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                status = "AuraMono List<uint> prerequisites unavailable";
                return false;
            }

            string[] typeCandidates = new[]
            {
                "System.Collections.Generic.List`1[System.UInt32]",
                "System.Collections.Generic.List`1[[System.UInt32, mscorlib]]",
                "System.Collections.Generic.List`1[[System.UInt32, System.Private.CoreLib]]"
            };

            IntPtr* typeArgs = stackalloc IntPtr[1];
            IntPtr* createArgs = stackalloc IntPtr[1];
            for (int i = 0; i < typeCandidates.Length && listObj == IntPtr.Zero; i++)
            {
                IntPtr typeNameObj = auraMonoStringNew(this.auraMonoRootDomain, typeCandidates[i]);
                if (typeNameObj == IntPtr.Zero)
                {
                    continue;
                }

                typeArgs[0] = typeNameObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                {
                    continue;
                }

                createArgs[0] = typeObj;
                exc = IntPtr.Zero;
                listObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)createArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    listObj = IntPtr.Zero;
                }
            }

            if (listObj == IntPtr.Zero)
            {
                status = "AuraMono List<uint> create failed";
                return false;
            }

            IntPtr listClass = this.petFeedAuraUIntListClass;
            if (listClass == IntPtr.Zero)
            {
                listClass = auraMonoObjectGetClass(listObj);
                this.petFeedAuraUIntListClass = listClass;
            }

            IntPtr addMethod = this.petFeedAuraUIntListAddMethod;
            if (addMethod == IntPtr.Zero && listClass != IntPtr.Zero)
            {
                addMethod = this.FindAuraMonoMethodOnHierarchy(listClass, "Add", 1);
                this.petFeedAuraUIntListAddMethod = addMethod;
            }

            if (addMethod == IntPtr.Zero)
            {
                status = "AuraMono List<uint>.Add unavailable";
                return false;
            }

            uint value = 0U;
            IntPtr* addArgs = stackalloc IntPtr[1];
            addArgs[0] = (IntPtr)(&value);
            foreach (uint rawValue in values)
            {
                value = rawValue;
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(addMethod, listObj, (IntPtr)addArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "AuraMono List<uint>.Add failed exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }
            }

            return true;
        }

        private int GetPetFeedMaxFullness(bool dog)
        {
            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    return 0;
                }

                string fieldName = dog ? "TableDogThemes" : "TableKittyThemes";
                FieldInfo field = tableDataType.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object tableObj = field?.GetValue(null);
                if (!(tableObj is IEnumerable table))
                {
                    return 0;
                }

                foreach (object entry in table)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    if (this.TryGetObjectMember(entry, "Value", out object theme)
                        && theme != null
                        && this.TryReadIntFromMember(theme, "fullnessThreshold", out int value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private bool TryReadIntFromMember(object obj, string memberName, out int value)
        {
            value = 0;
            if (!this.TryGetObjectMember(obj, memberName, out object raw) || raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToInt32(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadUIntFromMember(object obj, string memberName, out uint value)
        {
            value = 0U;
            if (!this.TryGetObjectMember(obj, memberName, out object raw) || raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToUInt32(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetMonoUIntMember(IntPtr obj, string memberName, out uint value)
        {
            value = 0U;
            if (!this.TryGetMonoIntMember(obj, memberName, out int signedValue) || signedValue < 0)
            {
                return false;
            }

            value = (uint)signedValue;
            return true;
        }

        private void PetFeedLog(string message)
        {
            if (!PetFeedLogsEnabled || string.IsNullOrEmpty(message))
            {
                return;
            }

            try
            {
                MelonLogger.Msg("[PetFeed] " + message);
            }
            catch
            {
            }
        }
    }
}
