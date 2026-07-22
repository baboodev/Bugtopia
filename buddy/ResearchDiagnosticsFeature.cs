using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    // Research Institute (研究室) diagnostics tab — read-only state/store dumps, panel-open probes
    // and a verbose EventCenter trace for every research event. No server mutations are wired: the
    // only commands this tab can trigger are the game's own CLIENT-side panel opens / area flag.
    //
    // System map (researched 2026-07-14, see .research-record/RESEARCH_STORE_REPORT.md):
    // - ResearchSystem (DataModule, XDTLevelAndEntity.Game.Module.Research) owns the CLIENT-created
    //   fake entities (platform staticId 1911, shop machine 1912, monitors 1913-16, instruments
    //   2001-2004; netIds are NetId.Next() locals) and exposes scalar getters.
    // - Research shop = plain store id 142; goods unlock via StoreGroup condition
    //   "ItemIsResearch[staticId] = 1"; weekly buy limit = TableResearchLevelUpgrade.shopBuyTime.
    // - Interact commands 8000/8001/8002 dispatch ResearchPlatformOpenEvent /
    //   ResearchInstrumentOpenEvent{netId} / ResearchShopOpenEvent; UIEventBridge opens the panels
    //   UNCONDITIONALLY (the unlock gates live in the interact commands) — so dispatching those
    //   events is both a diagnostic probe of the bridge and a gate-free panel opener.
    public partial class HeartopiaComplete
    {
        private const string ResearchSystemTypeName = "XDTLevelAndEntity.Game.Module.Research.ResearchSystem";
        private const string ResearchSyncSystemTypeName = "ClientSystem.Research.ResearchSyncSystem";
        private const string ResearchGameTimeUtilityTypeName = "XDTDataAndProtocol.ProtocolService.GameTimeUtility";
        private const string ResearchShopPanelTypeName = "XDTGame.UI.Panel.ResearchShopPanel";
        private const string ResearchControlPanelTypeName = "XDTGame.UI.Panel.ResearchControlPanel";

        // EventCenter events dispatched by this tab (namespaces verified in ilspy-dumps).
        private const string ResearchEvInstrumentOpen = "XDTGameSystem.UI.ResearchInstrumentOpenEvent";     // {uint instrumentNetId@0}
        private const string ResearchEvShowHide = "XDTDataAndProtocol.Events.ShowHideResearchStuffEvent";   // {bool isShow@0}

        private string researchDiagStatus = "Idle.";

        // Dispatch cache: open generic EventCenter.DispatchEvent(1 arg) + per-event inflated
        // instantiation (same recipe as the map-track StartTrack dispatch).
        private IntPtr researchDispatchOpenMethod = IntPtr.Zero;
        private readonly Dictionary<string, IntPtr> researchDispatchMethodByEvent = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
        private int researchInstrumentOpenNetIdOffset = -2; // -2 = not resolved yet, -1 = resolve failed

        // GameTimeUtility.GetCurrentGameTime returns a near-epoch DateTime while the server clock
        // is not synced in the current context (live report: 0001-01-01 00:05:30 outside the main
        // town). Only trust it (for remaining-time math) past this floor. Ticks of 2000-01-01.
        private const long ResearchSaneClockTicksFloor = 630822816000000000L;

        // --- "panels anywhere" spoof -------------------------------------------------------------
        // The research panels chain DataModule<ResearchSystem>.GetResearchLevel()/GetResearchExp(),
        // which count the CLIENT fake entities — those exist only in the own main town, so the
        // panels NRE everywhere else (GetResearchLevelUpgrade(0) == null). This is a WORLD problem,
        // not a distance one: outside the main town there is nothing in the world to scan. The
        // spoof detours both getters (Mono NativeDetour on the compiled methods — never on IL2CPP
        // .text) and substitutes the SERVER-SYNC level/exp whenever the real read returns 0, which
        // makes ResearchShopPanel/ResearchControlPanel fully functional from any location (store
        // goods, buy counts and purchases are world-independent; upgrades stay safely dead because
        // GetResearchPlatformNetId still reads 0). Armed automatically on tab open / first panel
        // open; detours install lazily and are never torn down (project rule).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ResearchIntGetterHookDelegate(IntPtr self);

        private static bool researchSpoofActive;                // read by the detour bodies
        private static int researchSpoofLevel;                  // server-sync platform level
        private static int researchSpoofExp;                    // server-sync platform exp
        private bool researchSpoofDetourAttempted;
        private MonoMod.RuntimeDetour.NativeDetour researchSpoofLevelDetour;
        private MonoMod.RuntimeDetour.NativeDetour researchSpoofExpDetour;
        private ResearchIntGetterHookDelegate researchSpoofLevelKeepAlive;
        private ResearchIntGetterHookDelegate researchSpoofExpKeepAlive;
        private static ResearchIntGetterHookDelegate researchSpoofLevelTrampoline;
        private static ResearchIntGetterHookDelegate researchSpoofExpTrampoline;

        // --- instrument monitor (list + status + completion alerts) ------------------------------
        // A background poll of the SERVER-SYNC instrument cache (readable from any location) that
        // both feeds the in-tab instrument list and fires a menu notification the instant a running
        // research crosses its completeTime. Runs from OnUpdate, throttled; refreshes immediately
        // when the Research tab opens.
        private struct ResearchInstrumentSnapshot
        {
            public int StaticId;
            public int Level;
            public int ResearchingItemId;   // 0 = idle
            public long CompleteTicks;       // 0 = none
        }

        private bool researchMonitorEnabled = true;             // poll + completion alerts (always on)
        private const float ResearchMonitorPollInterval = 5f;
        private float researchMonitorNextPollAt;
        private bool researchTabWasActive;                       // edge-detect the tab opening
        private bool researchTabPreparePending;                  // arm spoof + force-spawn once, on open
        private readonly List<ResearchInstrumentSnapshot> researchMonitorSnapshot = new List<ResearchInstrumentSnapshot>();
        private long researchMonitorClockTicks;                  // game clock sampled at last poll (0 = unsynced)
        private float researchMonitorClockSampledAt;             // unscaledTime of that sample (for UI interpolation)
        private bool researchMonitorHasSnapshot;
        // Completions already announced — key = staticId:itemId:completeTicks (ticks make a re-run of
        // the same item a fresh key, so it alerts again). Grows ~1 per completion; never needs pruning.
        private readonly HashSet<string> researchMonitorNotified = new HashSet<string>(StringComparer.Ordinal);

        private void ResearchLog(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            try
            {
                ModLogger.Msg("[Research] " + message);
            }
            catch
            {
            }
        }

        // ---- event dispatch (open panels the way the game's interact commands do) -----------------

        // Resolve the open generic EventCenter.DispatchEvent<T>(in T) once, then inflate + compile
        // per event type (cached). Mirrors EnsureMapTrackReady/InflateGenericDispatch.
        private bool EnsureResearchDispatchCore(out string error)
        {
            error = null;
            if (this.researchDispatchOpenMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoClassGetType == null || auraMonoMetadataGetGenericInst == null
                || auraMonoClassInflateGenericMethod == null || auraMonoRuntimeInvoke == null)
            {
                error = "AuraMono runtime not ready.";
                return false;
            }

            IntPtr eventCenter = this.FindAuraMonoClassByFullName("XDTGame.Core.EventCenter");
            if (eventCenter == IntPtr.Zero)
            {
                eventCenter = this.FindAuraMonoClassInImages("XDTGame.Core", "EventCenter",
                    new[] { "XDTBaseService", "XDTBaseService.dll" });
            }

            if (eventCenter == IntPtr.Zero)
            {
                error = "EventCenter class unresolved (enter a town first).";
                return false;
            }

            IntPtr openDispatch = this.FindAuraMonoMethodOnHierarchy(eventCenter, "DispatchEvent", 1);
            if (openDispatch == IntPtr.Zero)
            {
                error = "EventCenter.DispatchEvent(1-arg) unresolved.";
                return false;
            }

            this.researchDispatchOpenMethod = openDispatch;
            return true;
        }

        private IntPtr GetResearchDispatchMethod(string eventFullName, out string error)
        {
            error = null;
            if (this.researchDispatchMethodByEvent.TryGetValue(eventFullName, out IntPtr cached) && cached != IntPtr.Zero)
            {
                return cached;
            }

            if (!this.EnsureResearchDispatchCore(out error))
            {
                return IntPtr.Zero;
            }

            IntPtr eventClass = this.FindAuraMonoClassByFullName(eventFullName);
            if (eventClass == IntPtr.Zero)
            {
                int lastDot = eventFullName.LastIndexOf('.');
                string ns = lastDot > 0 ? eventFullName.Substring(0, lastDot) : string.Empty;
                string shortName = lastDot > 0 ? eventFullName.Substring(lastDot + 1) : eventFullName;
                eventClass = this.FindAuraMonoClassAcrossLoadedAssemblies(ns, shortName);
            }

            if (eventClass == IntPtr.Zero)
            {
                error = "event class unresolved: " + eventFullName;
                return IntPtr.Zero;
            }

            IntPtr inflated = this.InflateGenericDispatch(this.researchDispatchOpenMethod, eventClass);
            if (inflated == IntPtr.Zero || !AuraMonoMethodParamCountIs(inflated, 1))
            {
                error = "DispatchEvent inflate failed for " + eventFullName;
                return IntPtr.Zero;
            }

            this.researchDispatchMethodByEvent[eventFullName] = inflated;
            return inflated;
        }

        // Dispatch a single-bool event (e.g. ShowHideResearchStuffEvent{isShow}). The struct's only
        // field sits at offset 0, so writing byte 0 of the zeroed buffer is the bool.
        private unsafe bool DispatchResearchBoolEvent(string eventFullName, bool value, out string error)
        {
            IntPtr method = this.GetResearchDispatchMethod(eventFullName, out error);
            if (method == IntPtr.Zero)
            {
                return false;
            }

            byte* buf = stackalloc byte[16];
            for (int i = 0; i < 16; i++)
            {
                buf[i] = 0;
            }

            buf[0] = (byte)(value ? 1 : 0);

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)buf;
            auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                error = "dispatch raised an exception: " + eventFullName;
                return false;
            }

            return true;
        }

        private unsafe bool DispatchResearchInstrumentOpen(uint instrumentNetId, out string error)
        {
            IntPtr method = this.GetResearchDispatchMethod(ResearchEvInstrumentOpen, out error);
            if (method == IntPtr.Zero)
            {
                return false;
            }

            if (this.researchInstrumentOpenNetIdOffset == -2)
            {
                IntPtr eventClass = this.FindAuraMonoClassByFullName(ResearchEvInstrumentOpen);
                if (eventClass == IntPtr.Zero)
                {
                    eventClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGameSystem.UI", "ResearchInstrumentOpenEvent");
                }

                this.researchInstrumentOpenNetIdOffset =
                    eventClass != IntPtr.Zero && this.TryGetTrackFieldRawOffset(eventClass, "instrumentNetId", out int off)
                        ? off
                        : -1;
                this.ResearchLog("ResearchInstrumentOpenEvent.instrumentNetId raw offset = " + this.researchInstrumentOpenNetIdOffset);
            }

            if (this.researchInstrumentOpenNetIdOffset < 0)
            {
                error = "instrumentNetId field offset unresolved.";
                return false;
            }

            byte* buf = stackalloc byte[16];
            for (int i = 0; i < 16; i++)
            {
                buf[i] = 0;
            }

            *(uint*)(buf + this.researchInstrumentOpenNetIdOffset) = instrumentNetId;

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)buf;
            auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                error = "dispatch raised an exception.";
                return false;
            }

            return true;
        }

        // ---- "panels anywhere" spoof bodies + install ----------------------------------------------

        // Alloc-free, no Mono calls: forward to the original; substitute the server-sync value only
        // when the real read is 0 (client entities absent) and the toggle is on. Real data always wins.
        private static int ResearchSpoofLevelBody(IntPtr self)
        {
            ResearchIntGetterHookDelegate tramp = researchSpoofLevelTrampoline;
            int real = tramp != null ? tramp(self) : 0;
            if (real > 0 || !researchSpoofActive)
            {
                return real;
            }

            return researchSpoofLevel;
        }

        private static int ResearchSpoofExpBody(IntPtr self)
        {
            ResearchIntGetterHookDelegate tramp = researchSpoofExpTrampoline;
            int real = tramp != null ? tramp(self) : 0;
            if (real > 0 || !researchSpoofActive)
            {
                return real;
            }

            return researchSpoofExp;
        }

        // Lazy single-shot install of both getter detours (mirrors the HomelandFarm event-diag
        // install: resolve → mono_compile_method → NativeDetour → trampoline-or-revert). Instance
        // int getters have the plain Win64 ABI int(this) — no sret involved.
        private bool EnsureResearchSpoofDetours()
        {
            if (this.researchSpoofLevelDetour != null && this.researchSpoofExpDetour != null)
            {
                return true;
            }

            if (this.researchSpoofDetourAttempted)
            {
                return false;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false; // runtime not up — retry on the next toggle attempt
                }

                IntPtr systemClass = this.ResolveResearchClass(ResearchSystemTypeName);
                if (systemClass == IntPtr.Zero)
                {
                    return false; // image not loaded yet — retry later, don't burn the attempt
                }

                this.researchSpoofDetourAttempted = true; // single shot from here

                bool levelOk = this.TryInstallResearchSpoofDetour(systemClass, "GetResearchLevel",
                    ResearchSpoofLevelBody, ref this.researchSpoofLevelDetour, ref this.researchSpoofLevelKeepAlive,
                    ref researchSpoofLevelTrampoline);
                bool expOk = this.TryInstallResearchSpoofDetour(systemClass, "GetResearchExp",
                    ResearchSpoofExpBody, ref this.researchSpoofExpDetour, ref this.researchSpoofExpKeepAlive,
                    ref researchSpoofExpTrampoline);
                this.ResearchLog("spoof detour install: GetResearchLevel=" + levelOk + " GetResearchExp=" + expOk + ".");
                return levelOk && expOk;
            }
            catch (Exception ex)
            {
                this.researchSpoofDetourAttempted = true;
                this.ResearchLog("spoof detour install failed: " + ex.Message);
                return false;
            }
        }

        private bool TryInstallResearchSpoofDetour(
            IntPtr systemClass,
            string methodName,
            ResearchIntGetterHookDelegate body,
            ref MonoMod.RuntimeDetour.NativeDetour detourField,
            ref ResearchIntGetterHookDelegate keepAliveField,
            ref ResearchIntGetterHookDelegate trampolineField)
        {
            try
            {
                IntPtr native = this.ResolveBuildingMonoNative(systemClass, methodName, 0);
                if (native == IntPtr.Zero)
                {
                    this.ResearchLog("spoof: " + methodName + " native resolve failed.");
                    return false;
                }

                keepAliveField = body;
                detourField = new MonoMod.RuntimeDetour.NativeDetour(native, body);
                ResearchIntGetterHookDelegate tramp = detourField.GenerateTrampoline<ResearchIntGetterHookDelegate>();
                if (tramp == null)
                {
                    // Without the trampoline the game would lose the real getter — revert.
                    try { detourField.Undo(); } catch { }
                    detourField = null;
                    keepAliveField = null;
                    this.ResearchLog("spoof: " + methodName + " trampoline null, reverted.");
                    return false;
                }

                trampolineField = tramp;
                this.ResearchLog("spoof: hooked ResearchSystem." + methodName + " @0x" + native.ToInt64().ToString("X") + ".");
                return true;
            }
            catch (Exception ex)
            {
                this.ResearchLog("spoof: " + methodName + " install failed: " + ex.Message);
                return false;
            }
        }

        // Server-sync platform read (level/exp/netId) — shared by the state dump and the spoof.
        private bool TryGetResearchServerPlatform(out int level, out int exp, out uint netId, out string status)
        {
            level = 0;
            exp = 0;
            netId = 0U;
            status = "ok";
            IntPtr syncClass = this.ResolveResearchClass(ResearchSyncSystemTypeName);
            IntPtr method = syncClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(syncClass, "GetServerPlatformData", 0) : IntPtr.Zero;
            if (method == IntPtr.Zero)
            {
                status = "ResearchSyncSystem.GetServerPlatformData unresolved";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "GetServerPlatformData raised an exception";
                return false;
            }

            if (boxed == IntPtr.Zero)
            {
                status = "server platform data not cached yet (no component sync seen this session)";
                return false;
            }

            uint pin = AuraMonoPinNew(boxed);
            try
            {
                this.TryGetMonoIntMember(boxed, "level", out level);
                this.TryGetMonoIntMember(boxed, "exp", out exp);
                this.TryGetMonoUInt32Member(boxed, "netId", out netId);
                return level > 0;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        // Silent read of the SERVER-SYNC instrument cache into snapshots (staticId/level/researching/
        // completeTicks) — the world-independent source for both the monitor tick and the in-tab
        // list. Returns true when the dictionary resolved (even if empty). Shares the kvp-walk shape
        // with DumpResearchServerSyncState but never logs.
        private bool TryGetResearchServerInstruments(List<ResearchInstrumentSnapshot> into, out string status)
        {
            status = "ok";
            into.Clear();
            if (auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono runtime not ready";
                return false;
            }

            IntPtr syncClass = this.ResolveResearchClass(ResearchSyncSystemTypeName);
            IntPtr method = syncClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(syncClass, "GetServerInstrumentData", 0) : IntPtr.Zero;
            if (method == IntPtr.Zero)
            {
                status = "ResearchSyncSystem.GetServerInstrumentData unresolved";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr dictObj = auraMonoRuntimeInvoke(method, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || dictObj == IntPtr.Zero)
            {
                status = "GetServerInstrumentData invoke failed";
                return false;
            }

            List<IntPtr> kvps = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            uint dictPin = AuraMonoPinNew(dictObj);
            try
            {
                this.TryEnumerateAuraMonoCollectionItems(dictObj, kvps, pins, 64);
                for (int i = 0; i < kvps.Count; i++)
                {
                    if (kvps[i] == IntPtr.Zero)
                    {
                        continue;
                    }

                    this.TryGetMonoIntMember(kvps[i], "Key", out int staticId);
                    if (!this.TryGetMonoObjectMember(kvps[i], "Value", out IntPtr valueBoxed) || valueBoxed == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint valuePin = AuraMonoPinNew(valueBoxed);
                    try
                    {
                        this.TryGetMonoIntMember(valueBoxed, "level", out int level);
                        this.TryGetMonoIntMember(valueBoxed, "researchingItemId", out int researchingId);
                        bool haveCd = this.TryResearchReadDateTimeTicks(valueBoxed, "completeTime", out long cdTicks);
                        into.Add(new ResearchInstrumentSnapshot
                        {
                            StaticId = staticId,
                            Level = level,
                            ResearchingItemId = researchingId,
                            CompleteTicks = haveCd ? cdTicks : 0L
                        });
                    }
                    finally
                    {
                        AuraMonoPinFree(valuePin);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
                AuraMonoPinFree(dictPin);
            }

            into.Sort((a, b) => a.StaticId.CompareTo(b.StaticId));
            return true;
        }

        // Resolve the CLIENT (fake-entity) netId for an instrument staticId — the value
        // ResearchInstrumentPanel.Open(netId) needs to render the instrument's own data & enable the
        // Begin-Research button. Present only in the own main town (client entities); 0 elsewhere.
        private bool TryResolveClientInstrumentNetId(int staticId, out uint netId)
        {
            netId = 0U;
            IntPtr systemClass = this.ResolveResearchClass(ResearchSystemTypeName);
            IntPtr systemObj = systemClass != IntPtr.Zero ? this.TryGetAuraMonoDataModuleInstance(systemClass) : IntPtr.Zero;
            IntPtr getInstruments = systemObj != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(systemClass, "GetResearchInstruments", 0) : IntPtr.Zero;
            if (getInstruments == IntPtr.Zero)
            {
                return false;
            }

            uint systemPin = AuraMonoPinNew(systemObj);
            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr seqObj = auraMonoRuntimeInvoke(getInstruments, systemObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || seqObj == IntPtr.Zero)
                {
                    return false;
                }

                List<IntPtr> items = new List<IntPtr>();
                List<uint> pins = new List<uint>();
                uint seqPin = AuraMonoPinNew(seqObj);
                try
                {
                    this.TryEnumerateAuraMonoCollectionItems(seqObj, items, pins, 64);
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i] == IntPtr.Zero)
                        {
                            continue;
                        }

                        this.TryGetMonoIntMember(items[i], "staticId", out int sid);
                        if (sid == staticId && this.TryGetMonoUInt32Member(items[i], "netId", out uint nid) && nid != 0U)
                        {
                            netId = nid;
                            return true;
                        }
                    }
                }
                finally
                {
                    FreeAuraMonoPins(pins);
                    AuraMonoPinFree(seqPin);
                }
            }
            finally
            {
                AuraMonoPinFree(systemPin);
            }

            return false;
        }

        // Called every frame from OnUpdate; self-throttled. Polls the server-sync instruments (for
        // the list) and fires a one-shot notification the moment a running research is complete.
        // Also edge-detects the Research tab opening so it refreshes the list and prepares the
        // instruments (spoof + force-spawn) immediately — the tab is click-ready on open.
        private void ProcessResearchMonitorOnUpdate()
        {
            // Cheap per-frame edge detect (no AuraMono): opening the tab forces an immediate poll
            // and queues a one-shot prepare so SELECT ITEM / the panel buttons work right away.
            bool tabActive = (this.selectedTab == 9 && this.showMenu) || this.IsUguiShellResearchTabActive();
            if (tabActive && !this.researchTabWasActive)
            {
                this.researchMonitorNextPollAt = 0f;
                this.researchTabPreparePending = true;
            }
            this.researchTabWasActive = tabActive;

            if (!this.researchMonitorEnabled)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.researchMonitorNextPollAt)
            {
                return;
            }
            this.researchMonitorNextPollAt = now + ResearchMonitorPollInterval;

            // World gate: never touch the module registry from the main menu.
            if (this.GetPlayer() == null || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return;
            }

            // One-shot prepare on tab open: arm the level spoof and materialise the client
            // instruments (both no-op if already done / not in the main town) so the buttons are
            // ready before the user clicks. Silent — never toasts or flips visible state.
            if (this.researchTabPreparePending)
            {
                this.researchTabPreparePending = false;
                this.TryAutoArmResearchSpoofForPanelOpen();
                this.TryForceSpawnResearchInstitute(out _);
            }

            List<ResearchInstrumentSnapshot> fresh = new List<ResearchInstrumentSnapshot>();
            if (!this.TryGetResearchServerInstruments(fresh, out _))
            {
                return; // not cached yet — keep the last snapshot, retry next poll
            }

            long nowTicks = this.GetResearchGameTimeTicks();
            bool clockSane = nowTicks >= ResearchSaneClockTicksFloor;

            this.researchMonitorSnapshot.Clear();
            this.researchMonitorSnapshot.AddRange(fresh);
            this.researchMonitorClockTicks = nowTicks;
            this.researchMonitorClockSampledAt = now;
            this.researchMonitorHasSnapshot = true;

            if (!clockSane)
            {
                return; // can't judge completion without a synced clock
            }

            for (int i = 0; i < fresh.Count; i++)
            {
                ResearchInstrumentSnapshot inst = fresh[i];
                if (inst.ResearchingItemId <= 0 || inst.CompleteTicks <= 0L || inst.CompleteTicks > nowTicks)
                {
                    continue; // idle or still running
                }

                string key = inst.StaticId + ":" + inst.ResearchingItemId + ":" + inst.CompleteTicks;
                if (!this.researchMonitorNotified.Add(key))
                {
                    continue; // already announced this exact completion
                }

                string analyzer = "Analyzer " + (inst.StaticId - 2000);
                string itemName = this.ResearchResolveItemName(inst.ResearchingItemId);
                string msg = analyzer + " finished researching " + itemName + " — ready to collect.";
                this.AddMenuNotification(this.L(msg), new Color(0.45f, 1f, 0.55f));
                this.ResearchLog("completion alert: " + msg + " (staticId=" + inst.StaticId + " itemId=" + inst.ResearchingItemId + ")");
            }
        }

        // Item display name via TableData.GetEntity(id).name, falling back to the raw id. Best-effort.
        private string ResearchResolveItemName(int itemId)
        {
            try
            {
                IntPtr tableData = this.FindAuraMonoClassInImages(string.Empty, "TableData", new[] { "EcsClient", "EcsClient.dll" });
                if (tableData == IntPtr.Zero)
                {
                    tableData = this.FindAuraMonoClassByFullName("TableData");
                }

                IntPtr method = tableData != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(tableData, "GetEntity", 1) : IntPtr.Zero;
                if (method == IntPtr.Zero)
                {
                    return "item " + itemId;
                }

                unsafe
                {
                    int localId = itemId;
                    IntPtr exc = IntPtr.Zero;
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&localId);
                    IntPtr entity = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || entity == IntPtr.Zero)
                    {
                        return "item " + itemId;
                    }

                    uint pin = AuraMonoPinNew(entity);
                    try
                    {
                        return this.TryGetMonoStringMember(entity, "name", out string name) && !string.IsNullOrEmpty(name)
                            ? name
                            : "item " + itemId;
                    }
                    finally
                    {
                        AuraMonoPinFree(pin);
                    }
                }
            }
            catch
            {
                return "item " + itemId;
            }
        }

        // Arm the panels-anywhere spoof so the panels/store just work without the user doing
        // anything. Silent + best-effort: on failure (world not loaded / server platform data not
        // cached yet) it does NOTHING and lets the caller open the panel as-is (which may NRE), and
        // it never toasts. Idempotent — no-op once armed.
        private void TryAutoArmResearchSpoofForPanelOpen()
        {
            if (researchSpoofActive)
            {
                return;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return;
            }

            if (!this.TryGetResearchServerPlatform(out int level, out int exp, out _, out _))
            {
                return; // no server-sync platform yet — open as-is, retry next time
            }

            if (!this.EnsureResearchSpoofDetours())
            {
                return;
            }

            researchSpoofLevel = level;
            researchSpoofExp = exp;
            researchSpoofActive = true;
            this.ResearchLog("panels-anywhere spoof armed: level=" + level + " exp=" + exp + ".");
        }

        // ---- shared aura invoke helpers ------------------------------------------------------------

        // Quick client-entity platform level read (0 outside the own main town). The research
        // panels' OnStart chains GetResearchLevelUpgrade(level) — level 0 has no table row, so the
        // game NREs inside OnStart (UIViewLayer catches it, the panel just never shows). The panel
        // buttons use this to warn honestly instead of reporting a false "opened".
        private bool ResearchPanelsWillNreHere(out string warning)
        {
            warning = null;

            // The spoof makes GetResearchLevel() return the server-sync level, so the panel's
            // OnStart chain resolves a real TableResearchLevelUpgrade row and no longer NREs.
            if (researchSpoofActive && researchSpoofLevel > 0)
            {
                return false;
            }

            try
            {
                IntPtr systemClass = this.ResolveResearchClass(ResearchSystemTypeName);
                IntPtr systemObj = systemClass != IntPtr.Zero ? this.TryGetAuraMonoDataModuleInstance(systemClass) : IntPtr.Zero;
                if (systemObj == IntPtr.Zero)
                {
                    warning = "ResearchSystem.Instance unavailable — panels will fail here.";
                    return true;
                }

                uint pin = AuraMonoPinNew(systemObj);
                try
                {
                    if (this.TryResearchInvokeInt(this.FindAuraMonoMethodOnHierarchy(systemClass, "GetResearchLevel", 0), systemObj, out int level)
                        && level <= 0)
                    {
                        warning = "client platform level reads 0 here (research entities stream in only near the Research Institute building) — "
                            + "the panel's OnStart will NRE on GetResearchLevelUpgrade(0); the game swallows it and the panel won't show.";
                        return true;
                    }
                }
                finally
                {
                    AuraMonoPinFree(pin);
                }
            }
            catch
            {
            }

            return false;
        }

        private bool ResearchWorldReady(out string error)
        {
            error = null;
            if (this.GetPlayer() == null)
            {
                error = "No local player — enter a world first.";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                error = "AuraMono runtime not ready.";
                return false;
            }

            return true;
        }

        private IntPtr ResolveResearchClass(string fullName)
        {
            IntPtr cls = this.FindAuraMonoClassByFullName(fullName);
            if (cls == IntPtr.Zero)
            {
                int lastDot = fullName.LastIndexOf('.');
                if (lastDot > 0)
                {
                    cls = this.FindAuraMonoClassAcrossLoadedAssemblies(fullName.Substring(0, lastDot), fullName.Substring(lastDot + 1));
                }
            }

            return cls;
        }

        // 0-arg instance/static invoke returning a boxed scalar int/uint (returns false on miss/exc).
        private bool TryResearchInvokeInt(IntPtr method, IntPtr instance, out int value)
        {
            value = 0;
            if (method == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, instance, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoInt32(boxed, out value);
        }

        // Full 8-byte read of a boxed Int64. The shared TryReadMonoUnsignedIntegral helper tries a
        // 4-byte unbox FIRST and early-returns it, which silently truncates boxed longs to the low
        // dword — live report 2026-07-15: every DateTime printed near-epoch because Ticks lost its
        // high 32 bits (all values landed under 2^32). Never route Int64 through that helper.
        private unsafe bool TryResearchUnboxInt64(IntPtr boxed, out long value)
        {
            value = 0L;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(long*)raw;
            return true;
        }

        // Reads DateTime.Ticks off a boxed DateTime member of a (pinned) boxed struct/object.
        private bool TryResearchReadDateTimeTicks(IntPtr obj, string memberName, out long ticks)
        {
            ticks = 0L;
            if (!this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxedDt) || boxedDt == IntPtr.Zero)
            {
                return false;
            }

            uint pin = AuraMonoPinNew(boxedDt);
            try
            {
                // get_Ticks via the unbox-this-safe member reader returns a boxed Int64.
                if (!this.TryGetMonoObjectMember(boxedDt, "Ticks", out IntPtr boxedTicks) || boxedTicks == IntPtr.Zero)
                {
                    return false;
                }

                uint ticksPin = AuraMonoPinNew(boxedTicks);
                try
                {
                    return this.TryResearchUnboxInt64(boxedTicks, out ticks);
                }
                finally
                {
                    AuraMonoPinFree(ticksPin);
                }
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        // Current server/game time ticks via GameTimeUtility.GetCurrentGameTime() (the exact clock
        // the game compares research completeTime against). 0 = unavailable.
        private long GetResearchGameTimeTicks()
        {
            try
            {
                IntPtr cls = this.ResolveResearchClass(ResearchGameTimeUtilityTypeName);
                IntPtr method = cls != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(cls, "GetCurrentGameTime", 0) : IntPtr.Zero;
                if (method == IntPtr.Zero)
                {
                    return 0L;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(method, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    return 0L;
                }

                uint pin = AuraMonoPinNew(boxed);
                try
                {
                    if (!this.TryGetMonoObjectMember(boxed, "Ticks", out IntPtr boxedTicks) || boxedTicks == IntPtr.Zero)
                    {
                        return 0L;
                    }

                    uint ticksPin = AuraMonoPinNew(boxedTicks);
                    try
                    {
                        return this.TryResearchUnboxInt64(boxedTicks, out long ticks) ? ticks : 0L;
                    }
                    finally
                    {
                        AuraMonoPinFree(ticksPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(pin);
                }
            }
            catch
            {
                return 0L;
            }
        }

        // "2h 14m" / "47m" / "<1m" from a remaining tick span.
        private static string ResearchFormatRemaining(long remainingTicks)
        {
            if (remainingTicks <= 0L)
            {
                return "<1m";
            }

            long totalMinutes = remainingTicks / TimeSpan.TicksPerMinute;
            long hours = totalMinutes / 60L;
            long minutes = totalMinutes % 60L;
            if (hours > 0L)
            {
                return hours + "h " + minutes + "m";
            }

            return minutes > 0L ? minutes + "m" : "<1m";
        }

        // ---- panel probes ---------------------------------------------------------------------------

        // Force the game to create the institute's client entities (platform + instruments + shop)
        // without walking up to the building. Mirrors what DynamicMapItemService does on proximity:
        // set its `creatResearchStuff` flag true, then dispatch ShowHideResearchStuffEvent{isShow=
        // true} → ResearchSystem.CreateResearchStuffs. Unlocked instruments are rebuilt from the
        // server cache, so their client netId == the server netId → StartResearch is accepted (the
        // whole point). Only works inside the main-town level (ResearchSystem module scope); no-ops
        // harmlessly if the entities already exist (CreateResearchStuffs guards on _created). Silent
        // core (out status) — the tab-open auto-prepare calls this without toasting.
        private unsafe bool TryForceSpawnResearchInstitute(out string status)
        {
            status = "ok";
            try
            {
                if (this.GetPlayer() == null || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    status = "world/AuraMono not ready";
                    return false;
                }

                // Gate: the ResearchSystem module only exists in the main town (GameLevel_Main). If
                // its instance is null we're not in a town, and CreateResearchStuffs would NRE on the
                // level config.
                IntPtr systemClass = this.ResolveResearchClass(ResearchSystemTypeName);
                IntPtr systemObj = systemClass != IntPtr.Zero ? this.TryGetAuraMonoDataModuleInstance(systemClass) : IntPtr.Zero;
                if (systemObj == IntPtr.Zero)
                {
                    status = "not in the main town (ResearchSystem module inactive)";
                    return false;
                }

                if (auraMonoClassGetFieldFromName == null || auraMonoFieldSetValue == null)
                {
                    status = "AuraMono field-set exports missing";
                    return false;
                }

                // Resolve DynamicMapItemService (namespace ClientSystem.MapResource — the ilspy image
                // folder "Mapresource" differs from the real namespace) and its live instance.
                IntPtr svcClass = this.FindAuraMonoClassByFullName("ClientSystem.MapResource.DynamicMapItemService");
                if (svcClass == IntPtr.Zero)
                {
                    svcClass = this.FindAuraMonoClassAcrossLoadedAssemblies("ClientSystem.MapResource", "DynamicMapItemService");
                }
                if (svcClass == IntPtr.Zero)
                {
                    svcClass = this.FindAuraMonoClassAcrossLoadedAssemblies("ClientSystem.Mapresource", "DynamicMapItemService");
                }
                if (svcClass == IntPtr.Zero)
                {
                    status = "DynamicMapItemService class unresolved";
                    return false;
                }

                if (!this.TryDailyClaimsAuraMonoEcsTryGet(svcClass, false, out IntPtr svcObj, out string svcStatus) || svcObj == IntPtr.Zero)
                {
                    status = "DynamicMapItemService instance unavailable (" + svcStatus + ")";
                    return false;
                }

                bool fieldSet = false;
                uint svcPin = AuraMonoPinNew(svcObj);
                try
                {
                    IntPtr field = auraMonoClassGetFieldFromName(svcClass, "creatResearchStuff");
                    if (field == IntPtr.Zero)
                    {
                        status = "creatResearchStuff field not found";
                        return false;
                    }

                    byte one = 1;
                    auraMonoFieldSetValue(svcObj, field, (IntPtr)(&one));
                    fieldSet = true;
                }
                finally
                {
                    AuraMonoPinFree(svcPin);
                }

                if (!fieldSet)
                {
                    return false;
                }

                // Fire the same event the streamer fires — ResearchSystem builds the entities.
                if (!this.DispatchResearchBoolEvent(ResearchEvShowHide, true, out string dispErr))
                {
                    status = "flag set but ShowHideResearchStuffEvent dispatch failed (" + dispErr + ")";
                    return false;
                }

                status = "ok";
                return true;
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + " " + ex.Message;
                return false;
            }
        }

        // Open ResearchInstrumentPanel (the "pick an item to research" panel) for one instrument.
        // Prefers the client-entity netId (full function with the Begin-Research button live); the
        // institute's client entities stream in by PROXIMITY (DynamicMapItemService flips
        // creatResearchStuff + ShowHideResearchStuffEvent as you enter/leave the building's map-
        // resource radius), so away from the building there is no client entity → it opens with
        // netId 0 (still lists researchable items via the spoofed GetAllResearchItemId, but Begin-
        // Research is disabled by the game). Auto-arms the level spoof so the item list isn't gated.
        private void StartResearchOpenInstrumentPanelForStaticId(int staticId)
        {
            if (!this.ResearchWorldReady(out string gateError))
            {
                this.researchDiagStatus = gateError;
                this.AddMenuNotification(this.researchDiagStatus, new Color(1f, 0.7f, 0.45f));
                return;
            }

            this.TryAutoArmResearchSpoofForPanelOpen();

            bool haveClient = this.TryResolveClientInstrumentNetId(staticId, out uint netId);
            string analyzer = "Analyzer " + (staticId - 2000);
            if (this.DispatchResearchInstrumentOpen(netId, out string error))
            {
                if (haveClient)
                {
                    this.researchDiagStatus = "Opened " + analyzer + " research panel (clientNetId=" + netId + ").";
                    this.AddMenuNotification(this.researchDiagStatus, new Color(0.45f, 0.88f, 1f));
                }
                else
                {
                    this.researchDiagStatus = "Opened " + analyzer + " research panel in VIEW mode — approach the Research Institute building to start research (its instruments stream in by proximity).";
                    this.AddMenuNotification("Opened " + analyzer + " (view only — stand by the Research Institute to start research).", new Color(1f, 0.7f, 0.45f));
                }

                this.ResearchLog(this.researchDiagStatus);
            }
            else
            {
                this.researchDiagStatus = analyzer + " panel open failed: " + error;
                this.ResearchLog(this.researchDiagStatus);
                this.AddMenuNotification(this.researchDiagStatus, new Color(1f, 0.55f, 0.45f));
            }
        }

        private void StartResearchOpenPanelDirect(string panelTypeName, string label)
        {
            if (!this.ResearchWorldReady(out string gateError))
            {
                this.researchDiagStatus = gateError;
                this.AddMenuNotification(this.researchDiagStatus, new Color(1f, 0.7f, 0.45f));
                return;
            }

            this.TryAutoArmResearchSpoofForPanelOpen();
            bool willNre = this.ResearchPanelsWillNreHere(out string nreWarning);
            if (this.TryOpenAuraPanelByTypeName(panelTypeName, "Opened " + label + "."))
            {
                this.researchDiagStatus = "OpenView(" + label + ") invoked." + (willNre
                    ? " WARNING: " + nreWarning
                    : string.Empty);
                this.ResearchLog(this.researchDiagStatus);
                this.AddMenuNotification(willNre ? "OpenView sent, but the panel will NRE here — stand by the Research Institute." : this.researchDiagStatus,
                    willNre ? new Color(1f, 0.7f, 0.45f) : new Color(0.45f, 0.88f, 1f));
            }
            else
            {
                this.researchDiagStatus = label + " OpenView failed: " + (this.forceOpenShopStatus ?? "unknown");
                this.ResearchLog(this.researchDiagStatus);
                this.AddMenuNotification(this.researchDiagStatus, new Color(1f, 0.55f, 0.45f));
            }
        }

        // ---- UI -------------------------------------------------------------------------------------

        private float CalculateResearchTabHeight()
        {
            // Header + status + Instruments header + one row per instrument (or the empty line) +
            // the two panel buttons + the footer hint.
            return 250f + Math.Max(1, this.researchMonitorSnapshot.Count) * 32f;
        }

        private float DrawResearchTab(int startY)
        {
            const float left = 20f;
            float y = startY;

            Color textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = textColor;
            GUIStyle bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            bodyStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);
            GUIStyle monoStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            monoStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.85f);

            GUI.Label(new Rect(left, y, 520f, 24f), this.L("Research Institute"), headerStyle);
            y += 30f;

            GUI.Label(new Rect(left, y, 560f, 20f), this.LF("Status: {0}", this.researchDiagStatus), monoStyle);
            y += 26f;

            // -- instruments (live list + status; auto-loaded on open) --
            GUI.Label(new Rect(left, y, 400f, 20f), this.L("Instruments"), headerStyle);
            y += 26f;

            if (!this.researchMonitorHasSnapshot || this.researchMonitorSnapshot.Count == 0)
            {
                GUI.Label(new Rect(left, y, 560f, 34f),
                    this.L("Loading instrument data… (enter the main town — the list fills in a moment)."),
                    monoStyle);
                y += 38f;
            }
            else
            {
                // Interpolate the game clock forward from the last poll so the countdowns tick smoothly.
                long estNowTicks = this.researchMonitorClockTicks;
                bool clockSane = estNowTicks >= ResearchSaneClockTicksFloor;
                if (clockSane)
                {
                    estNowTicks += (long)((Time.unscaledTime - this.researchMonitorClockSampledAt) * TimeSpan.TicksPerSecond);
                }

                for (int i = 0; i < this.researchMonitorSnapshot.Count; i++)
                {
                    ResearchInstrumentSnapshot inst = this.researchMonitorSnapshot[i];
                    string analyzer = this.LF("Analyzer {0}  ·  Lv {1}", inst.StaticId - 2000, inst.Level);

                    bool busy;
                    string status;
                    Color statusColor;
                    if (inst.ResearchingItemId <= 0)
                    {
                        busy = false;
                        status = this.L("idle");
                        statusColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.7f);
                    }
                    else if (inst.CompleteTicks > 0L && clockSane && inst.CompleteTicks <= estNowTicks)
                    {
                        // Research finished — the slot is free to pick a new item.
                        busy = false;
                        status = this.LF("DONE · {0}", this.ResearchResolveItemName(inst.ResearchingItemId));
                        statusColor = new Color(0.45f, 1f, 0.55f);
                    }
                    else
                    {
                        busy = true;
                        string remain = (inst.CompleteTicks > 0L && clockSane)
                            ? ResearchFormatRemaining(inst.CompleteTicks - estNowTicks)
                            : "?";
                        status = this.LF("researching {0} · {1}", this.ResearchResolveItemName(inst.ResearchingItemId), remain);
                        statusColor = new Color(1f, 0.85f, 0.45f);
                    }

                    GUI.Label(new Rect(left, y, 200f, 22f), analyzer, bodyStyle);
                    GUIStyle statusStyle = new GUIStyle(monoStyle);
                    statusStyle.normal.textColor = statusColor;
                    GUI.Label(new Rect(left + 205f, y, 210f, 22f), status, statusStyle);

                    // A busy analyzer can't take a new item — grey out its button.
                    GUI.enabled = !busy;
                    if (this.DrawSecondaryActionButton(new Rect(left + 420f, y - 2f, 125f, 26f), this.L("SELECT ITEM")) && !busy)
                    {
                        this.StartResearchOpenInstrumentPanelForStaticId(inst.StaticId);
                    }
                    GUI.enabled = true;

                    y += 32f;
                }

                y += 8f;
            }

            // -- panel shortcuts --
            if (this.DrawSecondaryActionButton(new Rect(left, y, 200f, 30f), this.L("RESEARCH STORE")))
            {
                this.StartResearchOpenPanelDirect(ResearchShopPanelTypeName, "Research Store");
            }

            if (this.DrawSecondaryActionButton(new Rect(left + 210f, y, 200f, 30f), this.L("CONTROL CONSOLE")))
            {
                this.StartResearchOpenPanelDirect(ResearchControlPanelTypeName, "Control Console");
            }

            y += 38f;

            GUI.Label(new Rect(left, y, 560f, 34f),
                this.L("Live from the server-sync cache. SELECT ITEM opens that analyzer's research picker (busy analyzers are locked until they finish). Everything is prepared automatically when you open this tab."),
                monoStyle);
            y += 40f;
            return y;
        }
    }
}
