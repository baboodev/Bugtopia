using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // WILD ANIMAL ROSTER — the read-only companion to "Feed All Troughs" (WildAnimalFeedFeature.cs)
    // plus a per-animal "open the game's own feeding panel" action.
    //
    // Two independent halves:
    //
    // 1. SNAPSHOT (rows for the Animal Care tab). Reuses the feed feature's already-resolved
    //    channels verbatim — WildAnimalSystem.GetUnlockedAnimals / GetFullness /
    //    GetFeedTroughCapacity for the live numbers, TryGetWildAnimalGroupMeta (TableAnimalGroup)
    //    for groupName + favoriteFood ids, TableData.GetEntity(id).name for the food names.
    //    AuraMono only — the same single channel the feed planner uses, so the roster can never
    //    disagree with what Feed All would see. (Both used to try a managed-reflection twin first;
    //    WildAnimalSystem is an XDT* type absent from the BepInEx interop, so it never resolved.)
    //
    //    Two passes ON PURPOSE: pass 1 reads the scalars (groupId/fullness/capacity) with every
    //    Mono group row PINNED, pass 2 resolves names/favorites AFTER the pins are freed. The
    //    favorite/table lookups allocate on the Mono heap, and sgen moves un-pinned objects on
    //    allocation — reading a group row after a table call would be the classic stale-pointer AV
    //    (AGENTS.md §11). Pass 2 touches only ints, so it needs no pins at all.
    //
    // 2. OPEN FEED PANEL (lever A of the panel research). The game's own chain is
    //      interact 30 -> FeedTroughCommand -> PlayerStateFeedWildAnimal.Enter(troughNetId)
    //      -> G2UOpenInteractPanelEvent{panelName="FeedTroughPanel"} -> DefaultModule
    //      -> AnimalFeedRequestedEvent -> UIEventBridge -> AnimalFeedPanelLogic.StartLogic(netId)
    //    We call the LAST hop directly: AnimalFeedPanelLogic.StartLogic(uint) is a plain static
    //    Mono method with a single uint arg — no generic to inflate, no event struct offsets, no
    //    player state entered, no camera taken. The panel's own back button dispatches
    //    WildAnimalCancelFeedCommandEvent -> PlayerStateFeedWildAnimal.Cancel(), which is a no-op
    //    on a state we never entered (it only clears isInState).
    //
    //    The panel is addressed by the TROUGH's netId, not the animal's: AnimalFeedPanelLogic
    //    .InitFeedData reads FeedTroughComponentData(netId) to learn the group, and FALLS BACK TO
    //    PANDA when that read misses. So we only ever open with a netId we resolved ourselves, via
    //    Entities.GetComponents<FeedTroughComponent> -> component.entity -> netId ->
    //    AnimalProtocolManager.GetNetworkEntity -> AnimalUtil.GetGroup (the exact scan shape
    //    WildAnimalGiftFeature uses for gift boxes). Troughs stream with their map section, so a
    //    group whose trough is not loaded reports "not loaded here" instead of opening a Panda
    //    panel. Scan result is cached for WildAnimalRosterTroughScanTtlSeconds — a click on a
    //    stale/missing group forces one rescan before giving up.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Tunables
        // ----------------------------------------------------------------------------------------

        // Shares the "Wild Animal Feed" logging checkbox (Settings → Logging) — this feature is the
        // roster half of the same surface, and a separate switch just hides its diagnostics.
        private static bool WildAnimalRosterLogsEnabled => MasterLogWildAnimalFeed;

        // Snapshot cadence. Only ticks while the Animal Care sub-tab is actually on screen.
        private const float WildAnimalRosterRefreshIntervalSeconds = 2f;

        // Trough netId map lifetime. Troughs are static furniture; only map streaming changes them.
        private const float WildAnimalRosterTroughScanTtlSeconds = 20f;

        // Favorite-food names shown per row before the "+N" tail (a row is one line of UI).
        private const int WildAnimalRosterMaxFavoritesShown = 4;

        // ----------------------------------------------------------------------------------------
        // State
        // ----------------------------------------------------------------------------------------

        private sealed class WildAnimalRosterEntry
        {
            public int GroupId;
            public string GroupName;
            public int Fullness;
            public int Capacity;
            public string Favorites;   // already-joined display text ("" when unresolved)
        }

        private readonly List<WildAnimalRosterEntry> wildAnimalRosterEntries = new List<WildAnimalRosterEntry>();
        private string wildAnimalRosterStatus = string.Empty;
        private float wildAnimalRosterNextRefreshAt;

        // groupId -> joined favorite-food names. Only successful (non-empty) resolves are cached;
        // an empty result means the table was not ready yet and is retried on the next refresh.
        private readonly Dictionary<int, string> wildAnimalRosterFavoritesByGroup = new Dictionary<int, string>();
        private readonly Dictionary<int, string> wildAnimalRosterItemNameCache = new Dictionary<int, string>();

        // groupId -> trough netId, from the last component scan.
        private readonly Dictionary<int, uint> wildAnimalRosterTroughNetIdByGroup = new Dictionary<int, uint>();
        private float wildAnimalRosterTroughScanAt = float.NegativeInfinity;

        private IntPtr wildAnimalRosterFeedPanelStartLogicMethod = IntPtr.Zero;
        private IntPtr wildAnimalRosterTableDataGetEntityMethod = IntPtr.Zero;
        private int wildAnimalRosterTableDataGetEntityArgc;

        // ECS trough lookup (primary path — see TryResolveWildAnimalTroughNetIdViaProtocol).
        private IntPtr wildAnimalRosterGetFeedTroughMethod = IntPtr.Zero;
        private IntPtr wildAnimalRosterEntityGetNetIdMethod = IntPtr.Zero;
        private IntPtr wildAnimalRosterEntityIsAliveMethod = IntPtr.Zero;

        // Inflated DataCenter.TryGetComponentData<FeedTroughComponentData> — the group-spoof
        // detour target (see the "Group spoof" section).
        private IntPtr wildAnimalRosterTryGetComponentDataMethod = IntPtr.Zero;
        private bool wildAnimalRosterComponentDataResolveTried;

        // `XD.GameGerm.Ecs*` compiles into EcsClient; the image list must be explicit because the
        // by-full-name resolver has no prefix rule for that namespace (see the ⚠ note in
        // EnsureWildAnimalRosterTroughLookupMethods).
        private static readonly string[] WildAnimalRosterEcsImageNames =
        {
            "EcsClient", "EcsClient.dll",
            "XDTDataAndProtocol", "XDTDataAndProtocol.dll",
            "EcsSystem", "EcsSystem.dll",
            "Client", "Client.dll"
        };

        // Deduped one-shot failure messages (see WildAnimalRosterWarnOnce).
        private readonly HashSet<string> wildAnimalRosterWarnedOnce = new HashSet<string>(StringComparer.Ordinal);

        // ----------------------------------------------------------------------------------------
        // Snapshot
        // ----------------------------------------------------------------------------------------

        // Throttled entry point for the UI tick. Returns true when the snapshot was rebuilt.
        private bool RefreshWildAnimalRosterIfDue()
        {
            float now = Time.unscaledTime;
            if (now < this.wildAnimalRosterNextRefreshAt)
            {
                return false;
            }

            this.wildAnimalRosterNextRefreshAt = now + WildAnimalRosterRefreshIntervalSeconds;
            this.RefreshWildAnimalRoster();
            return true;
        }

        private void RefreshWildAnimalRoster()
        {
            try
            {
                this.wildAnimalRosterEntries.Clear();

                if (this.GetPlayer() == null)
                {
                    this.wildAnimalRosterStatus = "Enter a world to see your animals.";
                    return;
                }

                string status;
                if (!this.TryCollectWildAnimalRosterAuraMono(out status))
                {
                    // Resolver detail to the log; the empty-state row only points at it.
                    this.wildAnimalRosterEntries.Clear();
                    this.wildAnimalRosterStatus = "Animal list unavailable — see bugtopia.log.";
                    this.WildAnimalRosterWarnOnce("roster collect failed: " + status);
                    return;
                }

                // Pass 2 — drop animals outside their visiting window, then resolve names and
                // favourites for the survivors. Runs AFTER every Mono group row is released, so the
                // table lookups it performs can allocate (and move the heap) freely.
                //
                // GetUnlockedAnimals returns every group EVER unlocked, so without this filter a
                // long-expired visitor (Maltese) would sit in the list looking feedable. The test is
                // the planner's own, verbatim, so the roster can never disagree with what Feed All
                // does; an unresolvable appearTime leaves the group visible (fail-open, as there).
                int hidden = 0;
                for (int i = this.wildAnimalRosterEntries.Count - 1; i >= 0; i--)
                {
                    WildAnimalRosterEntry entry = this.wildAnimalRosterEntries[i];
                    if (this.ShouldSkipWildAnimalFeedGroupOffIsland(entry.GroupId))
                    {
                        this.wildAnimalRosterEntries.RemoveAt(i);
                        hidden++;
                        continue;
                    }

                    entry.GroupName = this.GetWildAnimalGroupDisplayName(entry.GroupId);
                    entry.Favorites = this.GetWildAnimalRosterFavoritesText(entry.GroupId);
                }

                this.wildAnimalRosterEntries.Sort(CompareWildAnimalRosterEntries);

                // Recomputed AFTER pass 2 — the collectors run before the filter, so their own
                // status line would count groups that are no longer listed.
                this.wildAnimalRosterStatus = this.FormatWildAnimalRosterStatus();
                if (hidden > 0)
                {
                    // Detail belongs in the log, not on screen.
                    this.WildAnimalRosterLog("hid " + hidden + " animal(s) outside their visiting window");
                }
            }
            catch (Exception ex)
            {
                // Exception text is diagnostic — log it, never paint it into the tab.
                this.wildAnimalRosterEntries.Clear();
                this.wildAnimalRosterStatus = "Animal list unavailable — see bugtopia.log.";
                this.WildAnimalRosterWarnOnce("refresh exception: " + ex);
            }
        }

        // Hungriest first (a care list is read top-down), ties broken by id so the row order is
        // deterministic and rows never swap on an idle refresh.
        private static int CompareWildAnimalRosterEntries(WildAnimalRosterEntry a, WildAnimalRosterEntry b)
        {
            float ratioA = a.Capacity > 0 ? a.Fullness / (float)a.Capacity : 1f;
            float ratioB = b.Capacity > 0 ? b.Fullness / (float)b.Capacity : 1f;
            int byRatio = ratioA.CompareTo(ratioB);
            return byRatio != 0 ? byRatio : a.GroupId.CompareTo(b.GroupId);
        }

        // Managed path — the same reflection block the feed planner resolves (EnsureWildAnimalFeed-
        // Reflection), so if Feed All works managed-side, so does this.

        // AuraMono path — reuses the feed planner's plan context (WildAnimalSystem module object +
        // the resolved GetFullness/GetFeedTroughCapacity methods + the enumerated group rows).
        private unsafe bool TryCollectWildAnimalRosterAuraMono(out string status)
        {
            this.wildAnimalRosterEntries.Clear();

            WildAnimalFeedAuraPlanContext context;
            if (!this.TryCreateWildAnimalFeedAuraPlanContext(out context, out status) || context == null)
            {
                return false;
            }

            // The system object and every group row are already pinned — the context takes those
            // gchandles DURING the GetUnlockedAnimals walk, the only moment the pointers are known
            // good. That matters here because each GetFullness/GetFeedTroughCapacity invoke boxes
            // its int return, and an sgen collection triggered by that allocation would otherwise
            // move the still-held rows (AGENTS.md §11). Pinning them here instead — after the walk —
            // was the crash: the rows had already moved, so the handles rooted recycled nursery
            // memory and TryUnboxMonoInt32 AV'd on it (WER xdt.exe.21532, 2026-08-28).

            // Arg slot hoisted out of the loop on purpose (CA2014): one 8-byte cell reused across
            // every group, rewritten per iteration.
            int localGroupId = 0;
            IntPtr* groupArgs = stackalloc IntPtr[1];
            groupArgs[0] = (IntPtr)(&localGroupId);

            try
            {
                for (int i = 0; i < context.GroupItems.Count; i++)
                {
                    IntPtr groupObj = context.GroupItems[i];
                    if (groupObj == IntPtr.Zero
                        || !this.TryGetWildAnimalFeedGroupIdAuraMono(groupObj, out int groupId)
                        || groupId <= 0)
                    {
                        continue;
                    }

                    localGroupId = groupId;

                    int fullness = 0;
                    IntPtr exc = IntPtr.Zero;
                    IntPtr fullnessObj = auraMonoRuntimeInvoke(context.GetFullnessMethod, context.WildAnimalSystemObj, (IntPtr)groupArgs, ref exc);
                    if (exc == IntPtr.Zero && fullnessObj != IntPtr.Zero)
                    {
                        this.TryUnboxMonoInt32(fullnessObj, out fullness);
                    }

                    int capacity = 0;
                    exc = IntPtr.Zero;
                    IntPtr capacityObj = auraMonoRuntimeInvoke(context.GetCapacityMethod, context.WildAnimalSystemObj, (IntPtr)groupArgs, ref exc);
                    if (exc == IntPtr.Zero && capacityObj != IntPtr.Zero)
                    {
                        this.TryUnboxMonoInt32(capacityObj, out capacity);
                    }

                    this.AddWildAnimalRosterEntry(groupId, fullness, capacity);
                }
            }
            finally
            {
                this.ReleaseWildAnimalFeedAuraPlanContext(context);
            }

            status = this.FormatWildAnimalRosterStatus();
            return true;
        }

        // Scalars only — names/favorites are filled by pass 2 (see the file header).
        private void AddWildAnimalRosterEntry(int groupId, int fullness, int capacity)
        {
            this.wildAnimalRosterEntries.Add(new WildAnimalRosterEntry
            {
                GroupId = groupId,
                GroupName = string.Empty,
                Fullness = Mathf.Max(0, fullness),
                Capacity = Mathf.Max(0, capacity),
                Favorites = string.Empty
            });
        }

        private string FormatWildAnimalRosterStatus()
        {
            int count = this.wildAnimalRosterEntries.Count;
            if (count == 0)
            {
                return "No unlocked animals yet.";
            }

            int hungry = 0;
            for (int i = 0; i < count; i++)
            {
                WildAnimalRosterEntry entry = this.wildAnimalRosterEntries[i];
                if (entry.Capacity > 0 && entry.Fullness < entry.Capacity)
                {
                    hungry++;
                }
            }

            return count + " animal(s), " + hungry + " trough(s) not full.";
        }

        // ----------------------------------------------------------------------------------------
        // Favorite food names
        // ----------------------------------------------------------------------------------------

        // TableAnimalGroup.favoriteFood (ids) -> joined item names. Cached per group once resolved;
        // an unresolved group returns "" and is retried on the next refresh (the design tables can
        // still be loading right after entering a world).
        private string GetWildAnimalRosterFavoritesText(int groupId)
        {
            if (this.wildAnimalRosterFavoritesByGroup.TryGetValue(groupId, out string cached))
            {
                return cached;
            }

            if (!this.TryGetWildAnimalGroupMeta(groupId, out List<int> favoriteFoods, out _, out _)
                || favoriteFoods == null || favoriteFoods.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(64);
            int shown = 0;
            for (int i = 0; i < favoriteFoods.Count && shown < WildAnimalRosterMaxFavoritesShown; i++)
            {
                int foodId = favoriteFoods[i];
                if (foodId <= 0)
                {
                    continue;
                }

                if (shown > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(this.ResolveWildAnimalRosterItemName(foodId));
                shown++;
            }

            if (shown == 0)
            {
                return string.Empty;
            }

            if (favoriteFoods.Count > shown)
            {
                sb.Append(" +").Append(favoriteFoods.Count - shown);
            }

            string text = sb.ToString();
            this.wildAnimalRosterFavoritesByGroup[groupId] = text;
            return text;
        }

        // TableData.GetEntity(id).name, preferring the (int, bool) overload this build ships and
        // falling back to the 1-arg one. Only successful resolves are cached.
        private unsafe string ResolveWildAnimalRosterItemName(int itemId)
        {
            if (this.wildAnimalRosterItemNameCache.TryGetValue(itemId, out string cached))
            {
                return cached;
            }

            string fallback = "#" + itemId;
            try
            {
                // The row pointer below is only safe because it is pinned across the string read;
                // with the gchandle exports missing that pin is a silent no-op, so fail closed and
                // show the raw id instead of walking movable sgen memory (AGENTS.md §11).
                if (!AuraMonoPinningAvailable || !this.EnsureWildAnimalRosterTableDataGetEntity())
                {
                    return fallback;
                }

                int localId = itemId;
                byte needException = 0;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&localId);
                if (this.wildAnimalRosterTableDataGetEntityArgc >= 2)
                {
                    args[1] = (IntPtr)(&needException);
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr rowObj = auraMonoRuntimeInvoke(
                    this.wildAnimalRosterTableDataGetEntityMethod,
                    IntPtr.Zero,
                    (IntPtr)args,
                    ref exc);
                if (exc != IntPtr.Zero || rowObj == IntPtr.Zero)
                {
                    return fallback;
                }

                // The row is a live Mono object and TryGetMonoStringMember allocates the managed
                // string copy — pin across the read.
                uint pin = AuraMonoPinNew(rowObj);
                try
                {
                    if (this.TryGetMonoStringMember(rowObj, "name", out string name) && !string.IsNullOrWhiteSpace(name))
                    {
                        this.wildAnimalRosterItemNameCache[itemId] = name;
                        return name;
                    }
                }
                finally
                {
                    AuraMonoPinFree(pin);
                }

                return fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private bool EnsureWildAnimalRosterTableDataGetEntity()
        {
            if (this.wildAnimalRosterTableDataGetEntityMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr tableData = this.FindAuraMonoClassInImages(string.Empty, "TableData", new[] { "EcsClient", "EcsClient.dll" });
            if (tableData == IntPtr.Zero)
            {
                tableData = this.FindAuraMonoClassByFullName("TableData");
            }

            if (tableData == IntPtr.Zero)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(tableData, "GetEntity", 2);
            int argc = 2;
            if (method == IntPtr.Zero)
            {
                method = this.FindAuraMonoMethodOnHierarchy(tableData, "GetEntity", 1);
                argc = 1;
            }

            if (method == IntPtr.Zero)
            {
                return false;
            }

            this.wildAnimalRosterTableDataGetEntityMethod = method;
            this.wildAnimalRosterTableDataGetEntityArgc = argc;
            return true;
        }

        // ----------------------------------------------------------------------------------------
        // Group spoof — makes the panel openable for animals whose trough is not streamed
        // ----------------------------------------------------------------------------------------

        // Why a detour and not data: the panel resolves its animal through
        //   DataCenter.TryGetComponentData<FeedTroughComponentData>(m_netId, out data) -> data.group
        // and there is NO netId we can legitimately hand it for a non-streamed group. Writing the
        // component onto the species entity was tried and REJECTED in testing:
        // WildAnimalProtocolManager.SpawnSpecies never calls DataCenter.AddEntity (only Spawn, for
        // ANIMALS, does), so a species netId is absent from LevelData._netEntitiesMap and
        // UpdateComponent silently no-ops — verified live, read-back returned group 0. DataCenter
        // mirrors streamed world entities only; no netId route exists for a far group.
        //
        // So intercept the read itself. The detour targets the INFLATED
        // TryGetComponentData<FeedTroughComponentData> — the code mono emits for that ONE type
        // argument — so reads of every other component type go nowhere near it. Game-wide only two
        // call sites read this type at all (InitFeedData and UpdateFeedTrough(uint, AnimalGroup)).
        //
        // Armed for exactly one synchronous StartLogic call and disarmed in a finally, so the
        // window is microseconds on the main thread. While armed the spoof answers ANY netId —
        // deliberate: we hand the panel netId 0 and let the detour supply the group.
        //
        // ABI: FeedTroughComponentData is a value type, so mono emits a dedicated (non-shared)
        // instantiation with NO hidden rgctx arg — the same finding the EventHook engine relies on
        // for DispatchEvent<T>. Native shape is byte(NetId, FeedTroughComponentData*), bool back in
        // AL. The netId arg is declared IntPtr rather than uint purely so a pass-through forward
        // re-passes the full register unchanged; we never read it.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte WildAnimalRosterTryGetComponentDataDelegate(IntPtr netId, IntPtr dataPtr);

        // mono_compile_method returns the native code pointer; the engine's shared
        // auraMonoCompileMethod delegate is declared void, so resolve an IntPtr-returning one.
        private delegate IntPtr WildAnimalRosterCompileMethodDelegate(IntPtr method);

        private static volatile int wildAnimalRosterSpoofGroupId;   // 0 = disarmed
        private static WildAnimalRosterTryGetComponentDataDelegate wildAnimalRosterTryGetTrampoline;
        private static readonly WildAnimalRosterTryGetComponentDataDelegate WildAnimalRosterTryGetBody =
            WildAnimalRosterTryGetComponentDataBody;

        private MonoMod.RuntimeDetour.NativeDetour wildAnimalRosterTryGetDetour;
        private bool wildAnimalRosterDetourAttempted;

        // Allocation-free, no Mono calls: either answer from the armed group or forward.
        private static unsafe byte WildAnimalRosterTryGetComponentDataBody(IntPtr netId, IntPtr dataPtr)
        {
            int group = wildAnimalRosterSpoofGroupId;
            if (group > 0 && dataPtr != IntPtr.Zero)
            {
                int* fields = (int*)dataPtr;
                fields[0] = group;   // AnimalGroup group
                fields[1] = 0;       // int value — the panel never reads it (its fullness comes
                                     // from WildAnimalSystem.GetFullness -> the species entity)
                return 1;
            }

            WildAnimalRosterTryGetComponentDataDelegate tramp = wildAnimalRosterTryGetTrampoline;
            return tramp != null ? tramp(netId, dataPtr) : (byte)0;
        }

        // Opens the panel for `groupId` with the spoof armed. netId 0 is handed to StartLogic on
        // purpose: with the group coming from the detour, the netId is only used by the panel's
        // EntityPositionViewModel, which is null-safe (Entities.GetEntity(id)?.InstanceID ??
        // Invalid, then an early-out on !IsValid) — the floating feed bar simply never positions.
        private bool TryOpenWildAnimalFeedPanelWithGroupSpoof(int groupId, out string status)
        {
            if (groupId <= 0)
            {
                status = "bad group id";
                return false;
            }

            if (!this.EnsureWildAnimalRosterGroupSpoofDetour(out status))
            {
                return false;
            }

            wildAnimalRosterSpoofGroupId = groupId;
            try
            {
                if (!this.TryInvokeAnimalFeedPanelStartLogic(0U, out status))
                {
                    return false;
                }

                status = "group spoof";
                return true;
            }
            finally
            {
                // Disarm even if StartLogic threw — leaving it armed would make every later read of
                // this component type answer with a stale group.
                wildAnimalRosterSpoofGroupId = 0;
            }
        }

        private bool EnsureWildAnimalRosterGroupSpoofDetour(out string status)
        {
            status = string.Empty;
            if (this.wildAnimalRosterTryGetDetour != null)
            {
                return true;
            }

            if (this.wildAnimalRosterDetourAttempted)
            {
                status = "group spoof detour unavailable";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                // Runtime not up yet — retry on the next click, don't burn the single-shot attempt.
                status = "AuraMono runtime not ready";
                return false;
            }

            if (!this.EnsureWildAnimalRosterTryGetComponentDataMethod(out status))
            {
                return false;
            }

            this.wildAnimalRosterDetourAttempted = true;   // single shot from here

            try
            {
                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                WildAnimalRosterCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<WildAnimalRosterCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    status = "mono_compile_method export unavailable";
                    this.WildAnimalRosterWarnOnce(status);
                    return false;
                }

                IntPtr nativePtr = compile(this.wildAnimalRosterTryGetComponentDataMethod);
                if (nativePtr == IntPtr.Zero)
                {
                    status = "mono_compile_method returned null for TryGetComponentData<FeedTroughComponentData>";
                    this.WildAnimalRosterWarnOnce(status);
                    return false;
                }

                this.wildAnimalRosterTryGetDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, WildAnimalRosterTryGetBody);
                WildAnimalRosterTryGetComponentDataDelegate tramp =
                    this.wildAnimalRosterTryGetDetour.GenerateTrampoline<WildAnimalRosterTryGetComponentDataDelegate>();
                if (tramp == null)
                {
                    // Without the trampoline the game would lose the real read — revert.
                    try { this.wildAnimalRosterTryGetDetour.Undo(); } catch { }
                    this.wildAnimalRosterTryGetDetour = null;
                    status = "group spoof trampoline null, reverted";
                    this.WildAnimalRosterWarnOnce(status);
                    return false;
                }

                wildAnimalRosterTryGetTrampoline = tramp;
                this.WildAnimalRosterLog("group spoof detour installed @0x" + nativePtr.ToInt64().ToString("X"));
                return true;
            }
            catch (Exception ex)
            {
                status = "group spoof detour install failed: " + ex.Message;
                this.WildAnimalRosterWarnOnce(status);
                return false;
            }
        }

        private bool EnsureWildAnimalRosterTryGetComponentDataMethod(out string status)
        {
            status = string.Empty;
            if (this.wildAnimalRosterTryGetComponentDataMethod != IntPtr.Zero)
            {
                return true;
            }

            if (this.wildAnimalRosterComponentDataResolveTried)
            {
                status = "DataCenter.TryGetComponentData unavailable";
                return false;
            }

            this.wildAnimalRosterComponentDataResolveTried = true;

            IntPtr dataCenterClass = this.FindAuraMonoClassInImages(
                "XDTDataAndProtocol.ComponentsData", "DataCenter", WildAnimalRosterEcsImageNames);
            IntPtr troughDataClass = this.FindAuraMonoClassInImages(
                "XDTDataAndProtocol.ComponentsData", "FeedTroughComponentData", WildAnimalRosterEcsImageNames);
            if (dataCenterClass == IntPtr.Zero || troughDataClass == IntPtr.Zero)
            {
                status = "DataCenter/FeedTroughComponentData class unavailable";
                this.WildAnimalRosterWarnOnce(status);
                return false;
            }

            // 2-param overload (NetId, out T) — the one InitFeedData uses; the 3-param sibling takes
            // a leading EGameLevel.
            IntPtr openTryGet = this.FindAuraMonoMethodOnHierarchy(dataCenterClass, "TryGetComponentData", 2);
            if (openTryGet == IntPtr.Zero)
            {
                status = "DataCenter.TryGetComponentData(2-arg) unavailable";
                this.WildAnimalRosterWarnOnce(status);
                return false;
            }

            this.wildAnimalRosterTryGetComponentDataMethod =
                this.InflateWildAnimalRosterComponentDataMethod(openTryGet, troughDataClass);
            if (this.wildAnimalRosterTryGetComponentDataMethod == IntPtr.Zero)
            {
                status = "TryGetComponentData<FeedTroughComponentData> inflate failed";
                this.WildAnimalRosterWarnOnce(status);
                return false;
            }

            this.WildAnimalRosterLog("TryGetComponentData<FeedTroughComponentData> inflated");
            return true;
        }

        // Same inflate recipe as DailyClaims' EcsService.TryGet<T> (AuraMonoEngine primitives),
        // including the param-count validation — a mismatched method_inst AVs the process on invoke
        // instead of throwing.
        private unsafe IntPtr InflateWildAnimalRosterComponentDataMethod(IntPtr openMethod, IntPtr argClass)
        {
            if (openMethod == IntPtr.Zero || argClass == IntPtr.Zero
                || auraMonoClassGetType == null || auraMonoMetadataGetGenericInst == null
                || auraMonoClassInflateGenericMethod == null)
            {
                return IntPtr.Zero;
            }

            IntPtr argType = auraMonoClassGetType(argClass);
            if (argType == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr* typeArgs = stackalloc IntPtr[1];
            typeArgs[0] = argType;
            IntPtr genericInst = auraMonoMetadataGetGenericInst(1, (IntPtr)typeArgs);
            if (genericInst == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            MonoGenericContext context = new MonoGenericContext
            {
                class_inst = IntPtr.Zero,
                method_inst = genericInst
            };

            IntPtr inflated = auraMonoClassInflateGenericMethod(openMethod, ref context);
            if (inflated == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return AuraMonoMethodParamCountIs(inflated, 2) ? inflated : IntPtr.Zero;
        }

        // ----------------------------------------------------------------------------------------
        // Open the game's feeding panel for one group (lever A — see the file header)
        // ----------------------------------------------------------------------------------------

        private void StartWildAnimalOpenFeedPanel(int groupId)
        {
            string groupName = this.GetWildAnimalGroupDisplayName(groupId);
            if (this.GetPlayer() == null)
            {
                this.wildAnimalRosterStatus = "No local player — enter a world first.";
                this.AddMenuNotification(this.wildAnimalRosterStatus, new Color(1f, 0.7f, 0.45f));
                return;
            }

            // Real trough first: when it IS streamed the panel gets the genuine entity, no detour is
            // armed at all, and the floating feed bar tracks the world object. The group spoof is
            // only for groups whose trough the client has never seen.
            string openStatus;
            bool opened;
            if (this.TryResolveWildAnimalTroughNetId(groupId, out uint troughNetId, out string troughStatus)
                && troughNetId != 0U)
            {
                this.WildAnimalRosterLog("open panel: group=" + groupId + " troughNetId=" + troughNetId
                    + " via " + troughStatus);
                opened = this.TryInvokeAnimalFeedPanelStartLogic(troughNetId, out openStatus);
            }
            else
            {
                this.WildAnimalRosterLog("open panel: group=" + groupId + " has no trough (" + troughStatus
                    + ") — opening with the group spoof");
                opened = this.TryOpenWildAnimalFeedPanelWithGroupSpoof(groupId, out openStatus);
            }

            if (opened)
            {
                this.wildAnimalRosterStatus = "Opened the feeding panel for " + groupName + ".";
                this.AddMenuNotification(this.wildAnimalRosterStatus, new Color(0.45f, 0.88f, 1f));
                this.WildAnimalRosterLog("opened feed panel group=" + groupId + " via " + openStatus);
            }
            else
            {
                // Reason to the LOG ONLY — a toast is transient and cannot be copied.
                this.wildAnimalRosterStatus = groupName + ": could not open the feeding panel.";
                this.AddMenuNotification(this.wildAnimalRosterStatus, new Color(1f, 0.55f, 0.45f));
                this.WildAnimalRosterWarnOnce("open panel failed group=" + groupId + " (" + groupName
                    + "): trough=[" + troughStatus + "] open=[" + openStatus + "]");
            }
        }

        // AnimalFeedPanelLogic.StartLogic(uint netId) — static, one uint arg, nothing generic. The
        // method itself builds the Intent and calls UIManager.StartLogic<AnimalFeedPanelLogic>,
        // which the game has already inflated/compiled for its own interact path.
        private unsafe bool TryInvokeAnimalFeedPanelStartLogic(uint troughNetId, out string status)
        {
            status = string.Empty;
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "AuraMono runtime not ready.";
                    return false;
                }

                if (this.wildAnimalRosterFeedPanelStartLogicMethod == IntPtr.Zero)
                {
                    IntPtr logicClass = this.FindAuraMonoClassByFullName("XDTGame.UI.Panel.AnimalFeedPanelLogic");
                    if (logicClass == IntPtr.Zero)
                    {
                        logicClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.UI.Panel", "AnimalFeedPanelLogic");
                    }

                    if (logicClass == IntPtr.Zero)
                    {
                        status = "AnimalFeedPanelLogic unavailable.";
                        return false;
                    }

                    // Neither PanelLogic<T> nor PanelLogicBase declares a StartLogic, so the
                    // hierarchy walk can only land on AnimalFeedPanelLogic's own static overload.
                    this.wildAnimalRosterFeedPanelStartLogicMethod = this.FindAuraMonoMethodOnHierarchy(logicClass, "StartLogic", 1);
                }

                if (this.wildAnimalRosterFeedPanelStartLogicMethod == IntPtr.Zero)
                {
                    status = "AnimalFeedPanelLogic.StartLogic unavailable.";
                    return false;
                }

                uint localNetId = troughNetId;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&localNetId);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.wildAnimalRosterFeedPanelStartLogicMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "StartLogic raised an exception.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Trough netId resolution (group -> the feed trough entity that carries it)
        // ----------------------------------------------------------------------------------------

        // PRIMARY path is the ECS one (WildAnimalProtocolManager.GetFeedTrough -> EcsEntity ->
        // GetNetId): the trough lives in IWildAnimalService's FeederGroupProperty filter as soon as
        // the SERVER has synced it, with no dependency on the view component being spawned. The
        // GetComponents<FeedTroughComponent> scan is only a fallback — `where T : ViewComponent`
        // means it sees troughs the client has actually SPAWNED, i.e. only the ones you are
        // standing near, which is why it reported "not loaded here" almost everywhere.
        private bool TryResolveWildAnimalTroughNetId(int groupId, out uint netId, out string status)
        {
            netId = 0U;
            status = string.Empty;
            if (groupId <= 0)
            {
                status = "bad group id";
                return false;
            }

            if (this.TryResolveWildAnimalTroughNetIdViaProtocol(groupId, out netId, out status) && netId != 0U)
            {
                this.wildAnimalRosterTroughNetIdByGroup[groupId] = netId;
                return true;
            }

            string protocolStatus = status;
            this.WildAnimalRosterLog("group=" + groupId + " protocol trough lookup failed: " + protocolStatus);

            bool stale = Time.unscaledTime - this.wildAnimalRosterTroughScanAt >= WildAnimalRosterTroughScanTtlSeconds;
            if (stale)
            {
                this.RefreshWildAnimalTroughNetIds();
            }

            if (this.wildAnimalRosterTroughNetIdByGroup.TryGetValue(groupId, out netId) && netId != 0U)
            {
                status = "view-component scan";
                return true;
            }

            // A miss on a fresh map is worth exactly one forced rescan — the player may have walked
            // into the trough's section since the cached scan.
            if (!stale)
            {
                this.RefreshWildAnimalTroughNetIds();
                if (this.wildAnimalRosterTroughNetIdByGroup.TryGetValue(groupId, out netId) && netId != 0U)
                {
                    status = "view-component scan";
                    return true;
                }
            }

            netId = 0U;
            status = protocolStatus + "; view scan found " + this.wildAnimalRosterTroughNetIdByGroup.Count + " trough(s)";
            return false;
        }

        // WildAnimalProtocolManager.GetFeedTrough(AnimalGroup) -> EcsEntity, then
        // XD.GameGerm.Ecs.EcsEntityExtensions.IsAlive + XD.GameGerm.Ecs.Boost.Extensions
        // .EcsEntityExtensions.GetNetId. GetFeedTrough is `private static` — invisible to C#, but
        // mono_runtime_invoke does not enforce visibility and FindAuraMonoMethodOnHierarchy walks
        // every method, so it is callable exactly like a public one.
        //
        // NOTE the two DIFFERENT EcsEntityExtensions classes: IsAlive lives in `XD.GameGerm.Ecs`,
        // GetNetId in `XD.GameGerm.Ecs.Boost.Extensions`. Both take the entity by value/`in`, so
        // both want the UNBOXED struct pointer (the gift feature's TryUnboxEntityArgForAuraMonoInvoke).
        private bool TryResolveWildAnimalTroughNetIdViaProtocol(int groupId, out uint netId, out string status)
        {
            netId = 0U;
            status = "AuraMono runtime not ready";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.EnsureWildAnimalRosterTroughLookupMethods(out status))
            {
                return false;
            }

            if (!this.TryResolveWildAnimalEntityNetIdForGroup(
                    this.wildAnimalRosterGetFeedTroughMethod, "trough", groupId, out netId, out status))
            {
                return false;
            }

            status = "protocol GetFeedTrough";
            return true;
        }

        // Driver for a `private static EcsEntity Xxx(AnimalGroup)` lookup on
        // WildAnimalProtocolManager (currently GetFeedTrough): invoke it, guard with IsAlive, read
        // the netId. Parameterised because the class has several lookups of this exact shape.
        private unsafe bool TryResolveWildAnimalEntityNetIdForGroup(
            IntPtr lookupMethod,
            string label,
            int groupId,
            out uint netId,
            out string status)
        {
            netId = 0U;
            status = label + " lookup unavailable";
            if (lookupMethod == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                int localGroupId = groupId;
                IntPtr* groupArgs = stackalloc IntPtr[1];
                groupArgs[0] = (IntPtr)(&localGroupId);
                IntPtr exc = IntPtr.Zero;
                IntPtr entityObj = auraMonoRuntimeInvoke(lookupMethod, IntPtr.Zero, (IntPtr)groupArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = label + " lookup raised an exception";
                    return false;
                }

                if (entityObj == IntPtr.Zero)
                {
                    status = label + " lookup returned null";
                    return false;
                }

                // The boxed EcsEntity must survive the two invokes below (each allocates).
                uint pin = AuraMonoPinNew(entityObj);
                try
                {
                    IntPtr* entityArgs = stackalloc IntPtr[1];
                    entityArgs[0] = this.TryUnboxEntityArgForAuraMonoInvoke(entityObj);

                    if (this.wildAnimalRosterEntityIsAliveMethod != IntPtr.Zero)
                    {
                        exc = IntPtr.Zero;
                        IntPtr aliveObj = auraMonoRuntimeInvoke(
                            this.wildAnimalRosterEntityIsAliveMethod,
                            IntPtr.Zero,
                            (IntPtr)entityArgs,
                            ref exc);
                        if (exc == IntPtr.Zero && aliveObj != IntPtr.Zero
                            && this.TryUnboxMonoBoolean(aliveObj, out bool alive) && !alive)
                        {
                            status = "no " + label + " entity for this group (server has not synced one)";
                            return false;
                        }
                    }

                    exc = IntPtr.Zero;
                    IntPtr netIdObj = auraMonoRuntimeInvoke(
                        this.wildAnimalRosterEntityGetNetIdMethod,
                        IntPtr.Zero,
                        (IntPtr)entityArgs,
                        ref exc);
                    if (exc != IntPtr.Zero || netIdObj == IntPtr.Zero)
                    {
                        status = "GetNetId failed on the " + label + " entity";
                        return false;
                    }

                    if (!this.TryUnboxMonoUInt32(netIdObj, out netId) || netId == 0U)
                    {
                        status = label + " entity has no netId";
                        return false;
                    }

                    status = string.Empty;
                    return true;
                }
                finally
                {
                    AuraMonoPinFree(pin);
                }
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool EnsureWildAnimalRosterTroughLookupMethods(out string status)
        {
            status = string.Empty;
            if (this.wildAnimalRosterGetFeedTroughMethod != IntPtr.Zero
                && this.wildAnimalRosterEntityGetNetIdMethod != IntPtr.Zero)
            {
                return true;
            }

            if (this.wildAnimalRosterGetFeedTroughMethod == IntPtr.Zero)
            {
                IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.WildAnimal.WildAnimalProtocolManager");
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTDataAndProtocol.ProtocolService.WildAnimal",
                        "WildAnimalProtocolManager");
                }

                if (protocolClass == IntPtr.Zero)
                {
                    status = "WildAnimalProtocolManager class unavailable";
                    this.WildAnimalRosterWarnOnce(status);
                    return false;
                }

                this.wildAnimalRosterGetFeedTroughMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "GetFeedTrough", 1);
            }

            // ⚠ Both EcsEntityExtensions classes MUST be resolved with FindAuraMonoClassInImages,
            // NOT FindAuraMonoClassByFullName. For a namespace it has no prefix rule for (and
            // `XD.GameGerm.Ecs*` is one), FindAuraMonoClassByFullName picks the FIRST LOADED image
            // out of a fixed candidate list — XDTGameUI — and searches only THAT one, so a class
            // living in EcsClient can never be found. That miss is exactly what made every
            // non-adjacent trough report "missing method(s): EcsEntityExtensions.GetNetId" and fall
            // through to the view-component scan, which only sees the trough you are standing next
            // to. FindAuraMonoClassInImages tries each image in turn.
            if (this.wildAnimalRosterEntityGetNetIdMethod == IntPtr.Zero)
            {
                IntPtr boostExtensions = this.FindAuraMonoClassInImages(
                    "XD.GameGerm.Ecs.Boost.Extensions",
                    "EcsEntityExtensions",
                    WildAnimalRosterEcsImageNames);
                if (boostExtensions == IntPtr.Zero)
                {
                    boostExtensions = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XD.GameGerm.Ecs.Boost.Extensions",
                        "EcsEntityExtensions");
                }

                if (boostExtensions != IntPtr.Zero)
                {
                    this.wildAnimalRosterEntityGetNetIdMethod = this.FindAuraMonoMethodOnHierarchy(boostExtensions, "GetNetId", 1);
                }

                this.WildAnimalRosterLog("EcsEntityExtensions(Boost) class=" + (boostExtensions != IntPtr.Zero)
                    + " GetNetId=" + (this.wildAnimalRosterEntityGetNetIdMethod != IntPtr.Zero));
            }

            // Optional — only used to skip a null EcsEntity cleanly; a miss just costs one managed
            // exception inside GetNetId, which the caller already handles. NOTE this is a DIFFERENT
            // class from the Boost one above, same short name.
            if (this.wildAnimalRosterEntityIsAliveMethod == IntPtr.Zero)
            {
                IntPtr coreExtensions = this.FindAuraMonoClassInImages(
                    "XD.GameGerm.Ecs",
                    "EcsEntityExtensions",
                    WildAnimalRosterEcsImageNames);
                if (coreExtensions != IntPtr.Zero)
                {
                    this.wildAnimalRosterEntityIsAliveMethod = this.FindAuraMonoMethodOnHierarchy(coreExtensions, "IsAlive", 1);
                }
            }

            if (this.wildAnimalRosterGetFeedTroughMethod == IntPtr.Zero || this.wildAnimalRosterEntityGetNetIdMethod == IntPtr.Zero)
            {
                status = "missing method(s): "
                    + (this.wildAnimalRosterGetFeedTroughMethod == IntPtr.Zero ? "WildAnimalProtocolManager.GetFeedTrough " : string.Empty)
                    + (this.wildAnimalRosterEntityGetNetIdMethod == IntPtr.Zero ? "EcsEntityExtensions.GetNetId" : string.Empty);
                this.WildAnimalRosterWarnOnce("trough lookup unavailable — " + status);
                return false;
            }

            this.WildAnimalRosterLog("trough lookup resolved: GetFeedTrough + GetNetId (isAlive="
                + (this.wildAnimalRosterEntityIsAliveMethod != IntPtr.Zero) + ")");
            return true;
        }

        // Entities.GetComponents<FeedTroughComponent> -> component.entity -> netId, then
        // AnimalProtocolManager.GetNetworkEntity(netId) + AnimalUtil.GetGroup to label it. Same
        // scan shape as WildAnimalGiftFeature's gift-box pass, including the component pins.
        private void RefreshWildAnimalTroughNetIds()
        {
            this.wildAnimalRosterTroughScanAt = Time.unscaledTime;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return;
                }

                if (!this.TryAuraMonoEntitiesGetComponentsInfraReady(out string infraStatus))
                {
                    this.WildAnimalRosterLog("trough scan skipped: " + infraStatus);
                    return;
                }

                IntPtr troughClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.WildAnimal.FeedTroughComponent");
                if (troughClass == IntPtr.Zero)
                {
                    troughClass = this.FindAuraMonoClassByFullName("ScriptsRefactory.LevelAndEntity.Gameplay.Component.WildAnimal.FeedTroughComponent");
                }

                if (troughClass == IntPtr.Zero)
                {
                    this.WildAnimalRosterLog("trough scan skipped: FeedTroughComponent class unavailable");
                    return;
                }

                List<uint> pins = new List<uint>();
                if (!this.TryAuraMonoGetComponentObjects(troughClass, out List<IntPtr> components, pins) || components == null)
                {
                    FreeAuraMonoPins(pins);
                    this.WildAnimalRosterLog("trough scan: GetComponents<FeedTroughComponent> unavailable");
                    return;
                }

                Dictionary<int, uint> resolved = new Dictionary<int, uint>();
                try
                {
                    for (int i = 0; i < components.Count; i++)
                    {
                        IntPtr componentObj = components[i];
                        if (componentObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        IntPtr entityObj;
                        if ((!this.TryGetMonoObjectMember(componentObj, "entity", out entityObj) || entityObj == IntPtr.Zero)
                            && (!this.TryGetMonoObjectMember(componentObj, "_entity", out entityObj) || entityObj == IntPtr.Zero))
                        {
                            continue;
                        }

                        if (!this.TryGetAuraMonoEntityNetId(entityObj, out uint troughNetId) || troughNetId == 0U)
                        {
                            continue;
                        }

                        // The view-layer entity is not the ECS EcsEntity AnimalUtil wants; go
                        // through the protocol's netId -> EcsEntity resolver first.
                        if (!this.TryGetNetworkEntityAuraMono(troughNetId, out IntPtr networkEntityObj)
                            || networkEntityObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        if (!this.TryAuraMonoAnimalUtilGetGroup(networkEntityObj, out int groupId) || groupId <= 0)
                        {
                            continue;
                        }

                        resolved[groupId] = troughNetId;
                    }
                }
                finally
                {
                    FreeAuraMonoPins(pins);
                }

                this.wildAnimalRosterTroughNetIdByGroup.Clear();
                foreach (KeyValuePair<int, uint> pair in resolved)
                {
                    this.wildAnimalRosterTroughNetIdByGroup[pair.Key] = pair.Value;
                }

                this.WildAnimalRosterLog("trough scan: components=" + components.Count
                    + " groups=" + this.wildAnimalRosterTroughNetIdByGroup.Count);
            }
            catch (Exception ex)
            {
                this.WildAnimalRosterLog("trough scan exception: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Logging
        // ----------------------------------------------------------------------------------------

        private void WildAnimalRosterLog(string message)
        {
            if (!WildAnimalRosterLogsEnabled)
            {
                return;
            }

            ModLogger.Msg("[WildAnimalRoster] " + message);
        }

        // Failures go to the log UNCONDITIONALLY (AGENTS.md §9 "log failures once"): the toast is
        // transient and unreadable at speed, and a resolve failure is precisely the thing a user
        // needs to be able to copy out of bugtopia.log. Deduped by text so a repeated click cannot
        // spam the file.
        private void WildAnimalRosterWarnOnce(string message)
        {
            if (string.IsNullOrEmpty(message) || !this.wildAnimalRosterWarnedOnce.Add(message))
            {
                return;
            }

            ModLogger.Msg("[WildAnimalRoster] " + message);
        }
    }
}
