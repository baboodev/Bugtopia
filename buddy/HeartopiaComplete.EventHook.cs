using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    // Reusable game-event hook engine.
    //
    // Subscribe to any XDTGame.Core.EventCenter event (every event is a `struct : IEvent`) by
    // NativeDetour-ing the inflated generic dispatcher EventCenter.DispatchEvent<T>(in T) for the
    // concrete event type. This is "strategy A" from docs/GAME_EVENTS.md, implemented with the
    // Iced-relocating MonoMod.RuntimeDetour (same proven path as the fishing NotifyFloatInWater
    // hook), NOT the abandoned 14-byte BubbleMonoNativeHook steal.
    //
    // Flow recap (ilspy-dumps/XDTBaseService/XDTGame.Core/EventCenter.cs):
    //   DispatchEvent<T>(in T) -> LinkedListExecutor.Dispatch<T> -> SingleLinkedList.Invoke<T>
    //     -> (node.data as Action<T>)?.Invoke(@event)
    // We intercept at the DispatchEvent<T> entry, so we see every dispatch of that type regardless
    // of who (if anyone) is subscribed, then forward to the original via the trampoline.
    //
    // ABI: for a value-type (non-shared) generic instantiation mono emits dedicated code with no
    // hidden rgctx arg, so DispatchEvent<T>(in T)'s native signature is exactly void(IntPtr): the
    // `in T` is a raw pointer to the bare struct (no mono object header — by-ref, not boxed).
    // Confirmed in-world (instrument open/close, no crash).
    //
    // Usage:
    //   RegisterGameEventHook("XDTDataAndProtocol.Events.SomeEvent", payloadBytes, snap => { ... });
    // The handler runs on the Unity main thread (in OnUpdate's drain), so it may allocate, log, and
    // call AuraMono/Unity freely. The native detour body itself only Marshal.Copy's the payload into
    // a reused buffer and forwards — it never allocates, throws, or calls into Mono.
    public partial class HeartopiaComplete
    {
        // Debug: log every registered event dispatch (scalar dump). Off by default — turn on to
        // discover/verify event payloads. Individual features register their own handlers regardless.
        internal static bool MasterLogGameEvents = false;

        // EventCenter.DispatchEvent<T>(in T): static, one pointer arg, void return.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DispatchEventHookDelegate(IntPtr eventPtr);

        // Per-entity EventCenter.DispatchEvent<T>(uint netId, in T): static, (uint, pointer) args,
        // void return. Used for events dispatched per-netId (e.g. dog QTE: TeaseDogRoundBeginEvent,
        // PetTeaseQteResultEvent) — a DIFFERENT method from the global 1-arg dispatcher.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DispatchEventByNetIdHookDelegate(uint netId, IntPtr eventPtr);

        // mono_compile_method returns the native code pointer; the engine's shared
        // auraMonoCompileMethod delegate is declared void, so resolve our own IntPtr-returning one.
        private delegate IntPtr EventHookCompileMethodDelegate(IntPtr method);

        // Distinct event types we can hook concurrently. Raised 16 -> 32 -> 48 -> 96 -> 128, every
        // step after the pool ran out (or came within a handful of slots) in a real session. 48 died
        // on a full-feature run: the headless pet-play pack (TeaseCatStartResult/TeaseCatEnd/
        // CatPlayExit/CatPlayPromote/PetTeaseQteResult/PetTeaseEndResult) was refused wholesale, so
        // My Pets -> Play had no end signal and hung until its watchdog while the game's own result
        // panel popped unsuppressed.
        // 128 covers the 91 distinct event names the features register in total (recounted
        // 2026-09-04, before QuietPopups added its six) — the budget is "distinct names registered
        // over the WHOLE session", not "hooked right
        // now", because slots are never released. Slots are cheap: a few small static arrays; a
        // NativeDetour is only installed per event type actually registered, never per empty slot.
        // MUST equal the number of static EventSlotBody/EventNetIdSlotBody methods below (each detour
        // slot needs its own unmanaged-callable body with a compile-time slot index). If this exceeds
        // the body count, slots past the array length throw IndexOutOfRange at install time.
        // Hard ceiling 255: the dispatch ring stores the slot index in a byte (eventRingSlot).
        private const int MaxEventHookSlots = 128;
        private const int EventPayloadCap = 64;     // max struct bytes snapshotted per dispatch

        // Read-only view over a snapshotted event payload, handed to handlers on the main thread.
        // Valid only for the duration of the handler call (the underlying buffer is ring-reused).
        public readonly struct GameEventSnapshot
        {
            private readonly byte[] _data;
            private readonly int _len;
            private readonly byte[] _strBytes;
            private readonly int _strLen;

            public GameEventSnapshot(string eventName, uint netId, byte[] data, int len, byte[] strBytes = null, int strLen = 0)
            {
                this.EventName = eventName;
                this.NetId = netId;
                this._data = data;
                this._len = len;
                this._strBytes = strBytes;
                this._strLen = strLen;
            }

            // Content of the event's captured string field (opt in via RegisterGameEventHook's
            // stringFieldOffset). Empty unless the slot declared one. The bytes were memcpy'd out
            // of the mono string DURING dispatch (see PushEventToRing) — the mono pointer itself is
            // never kept, so this is safe to touch on the main thread.
            public string StringValue => (this._strBytes != null && this._strLen > 0)
                ? System.Text.Encoding.Unicode.GetString(this._strBytes, 0, this._strLen)
                : string.Empty;

            public string EventName { get; }

            // For per-netId events the dispatch netId (e.g. the dog's netId); 0 for global events.
            public uint NetId { get; }
            public int Length => this._len;

            public int ReadInt32(int offset) => (this._data != null && offset >= 0 && offset + 4 <= this._len) ? BitConverter.ToInt32(this._data, offset) : 0;
            public uint ReadUInt32(int offset) => (this._data != null && offset >= 0 && offset + 4 <= this._len) ? BitConverter.ToUInt32(this._data, offset) : 0u;
            public ulong ReadUInt64(int offset) => (this._data != null && offset >= 0 && offset + 8 <= this._len) ? BitConverter.ToUInt64(this._data, offset) : 0ul;
            public float ReadSingle(int offset) => (this._data != null && offset >= 0 && offset + 4 <= this._len) ? BitConverter.ToSingle(this._data, offset) : 0f;
            public byte ReadByte(int offset) => (this._data != null && offset >= 0 && offset < this._len) ? this._data[offset] : (byte)0;
            public bool ReadBool(int offset) => this.ReadByte(offset) != 0;
        }

        private sealed class GameEventHookEntry
        {
            public string EventFullName;
            public int PayloadBytes;
            public int StringFieldOffset = -1; // -1 = capture no string field
            public bool ByNetId; // true => hook the 2-arg DispatchEvent<T>(uint netId, in T) overload
            public bool SuppressForward; // when true, detour swallows dispatch (no trampoline call)
            public float NextResolveLogAt;
            public readonly List<Action<GameEventSnapshot>> Handlers = new List<Action<GameEventSnapshot>>();
            public int Slot;
            public bool InstallAttempted;
            public bool Installed;
            public MonoMod.RuntimeDetour.NativeDetour Detour;
            public Delegate HookKeepAlive;  // anti-GC (global or by-netId body)
            public Delegate Trampoline;
        }

        private readonly Dictionary<string, GameEventHookEntry> gameEventHooksByName = new Dictionary<string, GameEventHookEntry>(StringComparer.Ordinal);
        private readonly GameEventHookEntry[] gameEventHookSlots = new GameEventHookEntry[MaxEventHookSlots];
        private int gameEventHookSlotCount;
        private bool gameEventHooksHardFailed; // EventCenter / DispatchEvent / compile unavailable

        // Per-slot routing consumed by the static native bodies (no instance state, no closures).
        private static readonly int[] eventSlotPayloadLen = new int[MaxEventHookSlots];
        // Per-slot offset of a `string` field inside the event struct to capture BY VALUE, or -1.
        private static readonly int[] eventSlotStringOffset = CreateEventStringOffsets();
        private static readonly bool[] eventSlotSuppressForward = new bool[MaxEventHookSlots];
        private static readonly int[] eventSlotHandlerCount = new int[MaxEventHookSlots];
        private static readonly DispatchEventHookDelegate[] eventSlotTrampoline = new DispatchEventHookDelegate[MaxEventHookSlots];
        private static readonly DispatchEventByNetIdHookDelegate[] eventSlotTrampolineNetId = new DispatchEventByNetIdHookDelegate[MaxEventHookSlots];

        // Ring buffer. Producer (detour body) and consumer (OnUpdate drain) both run on the Unity
        // main thread — the game dispatches these events from gameplay/state code on the same thread
        // that drives OnUpdate — so this is single-threaded in practice. Buffers are preallocated and
        // reused so the native-boundary body never allocates.
        private const int EventRingSize = 64; // power of two
        private static readonly byte[] eventRingSlot = new byte[EventRingSize];
        private static readonly int[] eventRingLen = new int[EventRingSize];
        private static readonly uint[] eventRingNetId = new uint[EventRingSize];
        private static readonly byte[][] eventRingData = CreateEventRing();
        // Parallel ring for captured string CONTENT (UTF-16 bytes), preallocated like the payload
        // ring so the native-boundary body never allocates.
        private const int EventStringCap = 512; // 256 chars — chat/mail strings are short
        private static readonly byte[][] eventRingStringData = CreateEventStringRing();
        private static readonly int[] eventRingStringLen = new int[EventRingSize];
        private static int eventRingWrite;
        private static int eventRingRead;

        private static byte[][] CreateEventRing()
        {
            byte[][] ring = new byte[EventRingSize][];
            for (int i = 0; i < EventRingSize; i++)
            {
                ring[i] = new byte[EventPayloadCap];
            }
            return ring;
        }

        private static byte[][] CreateEventStringRing()
        {
            byte[][] ring = new byte[EventRingSize][];
            for (int i = 0; i < EventRingSize; i++)
            {
                ring[i] = new byte[EventStringCap];
            }
            return ring;
        }

        private static int[] CreateEventStringOffsets()
        {
            int[] offsets = new int[MaxEventHookSlots];
            for (int i = 0; i < MaxEventHookSlots; i++)
            {
                offsets[i] = -1;
            }
            return offsets;
        }

        // Register a handler for a GLOBAL game event (dispatched via DispatchEvent<T>(in T)).
        // Idempotent per (name): re-registering the same name adds another handler to the shared
        // detour. payloadBytes = the event struct size from the dump (bytes to snapshot; clamp to
        // EventPayloadCap). Use 0 for empty events. The handler runs on the Unity main thread.
        internal bool RegisterGameEventHook(string eventFullName, int payloadBytes, Action<GameEventSnapshot> handler)
        {
            return this.RegisterGameEventHookInternal(eventFullName, payloadBytes, false, handler, false);
        }

        // Register a handler for a PER-ENTITY game event (dispatched via DispatchEvent<T>(uint
        // netId, in T) — e.g. dog QTE events). The handler receives the dispatch netId in
        // GameEventSnapshot.NetId. Same name must NOT also be registered as global (one overload per
        // event type per slot).
        internal bool RegisterGameEventHookByNetId(string eventFullName, int payloadBytes, Action<GameEventSnapshot> handler)
        {
            return this.RegisterGameEventHookInternal(eventFullName, payloadBytes, true, handler, false);
        }

        internal void SetGameEventHookSuppressForward(string eventFullName, bool suppress)
        {
            if (string.IsNullOrEmpty(eventFullName)
                || !this.gameEventHooksByName.TryGetValue(eventFullName, out GameEventHookEntry entry))
            {
                return;
            }

            entry.SuppressForward = suppress;
            if (entry.Installed)
            {
                eventSlotSuppressForward[entry.Slot] = suppress;
            }
        }

        private bool RegisterGameEventHookInternal(string eventFullName, int payloadBytes, bool byNetId, Action<GameEventSnapshot> handler, bool suppressForward)
        {
            if (string.IsNullOrEmpty(eventFullName) || (handler == null && !suppressForward))
            {
                return false;
            }

            int clamped = Math.Max(0, Math.Min(payloadBytes, EventPayloadCap));

            if (this.gameEventHooksByName.TryGetValue(eventFullName, out GameEventHookEntry existing))
            {
                if (existing.ByNetId != byNetId)
                {
                    ModLogger.Msg("[EventHook] " + eventFullName + " already hooked with byNetId=" + existing.ByNetId + "; ignoring conflicting byNetId=" + byNetId);
                    return false;
                }
                if (handler != null)
                {
                    existing.Handlers.Add(handler);
                }

                if (suppressForward)
                {
                    existing.SuppressForward = true;
                    if (existing.Installed)
                    {
                        eventSlotSuppressForward[existing.Slot] = true;
                    }
                }

                if (clamped > existing.PayloadBytes)
                {
                    existing.PayloadBytes = clamped;
                    if (existing.Installed)
                    {
                        eventSlotPayloadLen[existing.Slot] = clamped;
                    }
                }

                this.SyncGameEventHookSlotHandlerCount(existing);
                return true;
            }

            if (this.gameEventHookSlotCount >= MaxEventHookSlots)
            {
                // A refused hook degrades its feature SILENTLY (the caller only sees `false`), so this
                // has to be loud: the 2026-08-23 "My Pets -> Play never ends" bug was six pet-play
                // hooks refused here at 48/48 while every other log line looked healthy.
                ModLogger.Warning("[EventHook] slot pool exhausted (" + this.gameEventHookSlotCount + "/"
                    + MaxEventHookSlots + "); cannot hook " + eventFullName
                    + " — the feature that asked for it will run without this event (raise MaxEventHookSlots"
                    + " and add matching EventSlotBody/EventNetIdSlotBody thunks). Slots are never released.");
                return false;
            }

            GameEventHookEntry entry = new GameEventHookEntry
            {
                EventFullName = eventFullName,
                PayloadBytes = clamped,
                ByNetId = byNetId,
                SuppressForward = suppressForward,
                Slot = this.gameEventHookSlotCount
            };
            if (handler != null)
            {
                entry.Handlers.Add(handler);
            }
            this.gameEventHookSlots[entry.Slot] = entry;
            this.gameEventHooksByName[eventFullName] = entry;
            this.gameEventHookSlotCount++;
            this.SyncGameEventHookSlotHandlerCount(entry);
            return true;
        }

        private void SyncGameEventHookSlotHandlerCount(GameEventHookEntry entry)
        {
            if (entry == null || entry.Slot < 0 || entry.Slot >= MaxEventHookSlots)
            {
                return;
            }

            eventSlotHandlerCount[entry.Slot] = entry.Handlers.Count;
        }

        // True once the detour for this event is live (used by features that keep an event-driven
        // flag but want a polling fallback until/unless the hook actually installs).
        internal bool IsGameEventHookInstalled(string eventFullName)
        {
            return !string.IsNullOrEmpty(eventFullName)
                && this.gameEventHooksByName.TryGetValue(eventFullName, out GameEventHookEntry e)
                && e.Installed;
        }

        // Called from OnUpdate: drains buffered dispatches to handlers, and — ONLY once a world is
        // up — runs a catch-up install pass for hooks registered after the gate callback already
        // finished (a feature switched on mid-session). It never installs before that, because
        // inflating DispatchEvent<T> on half-loaded Mono images aborts the process.
        //
        // There is no longer a "transport" exemption: the world-ready gate reads the game's level
        // FSM directly (GameWorld) instead of listening for LoadingOpened/LoadingClosed, so nothing
        // here has to run before a world exists. That exemption was the last pre-world inflate left
        // and it is what crashed startup — see project memory eventhook-preworld-inflate-abort.
        private void ProcessGameEventHooksOnUpdate()
        {
            if (this.gameEventHookSlotCount == 0)
            {
                return;
            }

            // Registration is metadata-only and must happen regardless, or the gate would never
            // know to call us; the INSTALL below is the part that needs a live world.
            this.EnsureGameEventHooksWorldReadyRegistered();

            if (this.IsWorldReady)
            {
                this.EnsureGameEventHooksInstalled();
            }

            this.DrainGameEventHooks();
        }

        // World-ready gating of the install pass.
        //
        // Installing a detour means resolving the event struct's MonoClass and inflating
        // EventCenter.DispatchEvent<T> through mono_metadata_get_generic_inst +
        // mono_class_inflate_generic_method. On the login/load menu the game's Mono images are only
        // partially up: a class can resolve by name while inflating a generic over it makes the
        // runtime g_assert and abort() the process — uncatchable, nothing useful in the log
        // (WER xdt.exe.7988 / 30332 on DispatchEvent<StartCookEvent>, xdt.exe.34488 on
        // DispatchEvent<LoadingOpenedEvent>).
        //
        // So EVERY install runs from the world-ready gate, with no exceptions left. The gate itself
        // no longer needs an event to know a world exists — it polls GameWorld's level FSM — which
        // is what allowed the last exemption to go away. Registration stays ungated (metadata only).
        private const float GameEventHookInstallRetrySeconds = 0.5f;
        private float gameEventHookNextInstallAttemptAt = -999f;

        private const string GameEventHooksWorldReadyCallbackName = "GameEventHooksInstall";
        private bool gameEventHooksWorldReadyRegistered;

        // World-ready callback: install everything still pending. Returns true only when nothing is
        // pending any more, so the gate keeps retrying (bounded) while an image is still loading and
        // re-runs on the next world load for hooks registered after this one.
        private bool InstallGameEventHooksOnWorldReady()
        {
            if (this.gameEventHooksHardFailed)
            {
                return true;
            }

            this.gameEventHookNextInstallAttemptAt = -999f; // gate-driven pass ignores the bootstrap throttle
            this.EnsureGameEventHooksInstalled();

            for (int i = 0; i < this.gameEventHookSlotCount; i++)
            {
                GameEventHookEntry e = this.gameEventHookSlots[i];
                if (e != null && !e.InstallAttempted)
                {
                    return false;
                }
            }

            return true;
        }

        // Metadata-only and safe at any time; kept separate from the install pass so OnUpdate can
        // do this before a world exists without dragging the inflate along with it.
        private void EnsureGameEventHooksWorldReadyRegistered()
        {
            if (this.gameEventHooksHardFailed || this.gameEventHooksWorldReadyRegistered)
            {
                return;
            }

            this.gameEventHooksWorldReadyRegistered = true;
            this.RegisterWorldReadyCallback(GameEventHooksWorldReadyCallbackName, this.InstallGameEventHooksOnWorldReady);
        }

        private void EnsureGameEventHooksInstalled()
        {
            if (this.gameEventHooksHardFailed)
            {
                return;
            }

            this.EnsureGameEventHooksWorldReadyRegistered();

            float installNow = UnityEngine.Time.unscaledTime;
            if (installNow < this.gameEventHookNextInstallAttemptAt)
            {
                return;
            }
            this.gameEventHookNextInstallAttemptAt = installNow + GameEventHookInstallRetrySeconds;

            bool anyPending = false;
            for (int i = 0; i < this.gameEventHookSlotCount; i++)
            {
                GameEventHookEntry e = this.gameEventHookSlots[i];
                if (e != null && !e.InstallAttempted)
                {
                    anyPending = true;
                    break;
                }
            }
            if (!anyPending)
            {
                return;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // AuraMono not up yet — retry on a later frame.
                }

                IntPtr eventCenterClass = this.FindAuraMonoClassByFullName("XDTGame.Core.EventCenter");
                if (eventCenterClass == IntPtr.Zero)
                {
                    return; // XDTBaseService image not loaded yet — retry later.
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                EventHookCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<EventHookCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.gameEventHooksHardFailed = true;
                    ModLogger.Msg("[EventHook] mono_compile_method export unavailable — event hooks disabled");
                    return;
                }

                for (int i = 0; i < this.gameEventHookSlotCount; i++)
                {
                    GameEventHookEntry entry = this.gameEventHookSlots[i];
                    if (entry == null || entry.InstallAttempted)
                    {
                        continue;
                    }

                    // No per-entry exemption any more: every caller of this method has already
                    // established that a world is up, so every pending hook may inflate now.
                    this.TryInstallGameEventDetour(entry, eventCenterClass, compile);
                }
            }
            catch (Exception ex)
            {
                this.gameEventHooksHardFailed = true; // never crash-loop
                ModLogger.Msg("[EventHook] install pass failed: " + ex.Message);
            }
        }

        private void TryInstallGameEventDetour(GameEventHookEntry entry, IntPtr eventCenterClass, EventHookCompileMethodDelegate compile)
        {
            // Global events go through DispatchEvent<T>(in T) (1 param); per-netId events go through
            // DispatchEvent<T>(uint netId, in T) (2 params). Select the overload by param count.
            int argc = entry.ByNetId ? 2 : 1;
            IntPtr openDispatch = this.FindAuraMonoMethodOnHierarchy(eventCenterClass, "DispatchEvent", argc);
            if (openDispatch == IntPtr.Zero)
            {
                entry.InstallAttempted = true;
                ModLogger.Msg("[EventHook] EventCenter.DispatchEvent (" + argc + "-arg) not found for " + entry.EventFullName);
                return;
            }

            // The event struct's image (e.g. XDTDataAndProtocol) may load after EventCenter. If the
            // class isn't resolvable yet, leave InstallAttempted=false so we retry on a later frame.
            IntPtr eventClass = this.ResolveGameEventClass(entry.EventFullName);
            if (eventClass == IntPtr.Zero)
            {
                if (UnityEngine.Time.unscaledTime >= entry.NextResolveLogAt)
                {
                    entry.NextResolveLogAt = UnityEngine.Time.unscaledTime + 5f;
                    ModLogger.Msg("[EventHook] awaiting event type: " + entry.EventFullName);
                }

                return;
            }

            entry.InstallAttempted = true;

            try
            {
                if (!this.TryInflateDispatchForEvent(openDispatch, eventClass, argc, out IntPtr inflated))
                {
                    ModLogger.Msg("[EventHook] inflate DispatchEvent<" + entry.EventFullName + "> (" + argc + "-arg) failed");
                    return;
                }

                IntPtr nativePtr = compile(inflated);
                if (nativePtr == IntPtr.Zero)
                {
                    ModLogger.Msg("[EventHook] mono_compile_method null for " + entry.EventFullName);
                    return;
                }

                // Guard against a MaxEventHookSlots vs body-array-length drift: a slot past the body
                // count would otherwise throw a cryptic IndexOutOfRange here. Fail this one hook with a
                // clear message instead of crashing the install.
                int bodyCount = entry.ByNetId ? EventNetIdSlotBodies.Length : EventSlotBodies.Length;
                if (entry.Slot >= bodyCount)
                {
                    ModLogger.Msg("[EventHook] slot " + entry.Slot + " >= body count " + bodyCount
                        + " for " + entry.EventFullName + " — extend EventSlotBodies/EventNetIdSlotBodies to MaxEventHookSlots (" + MaxEventHookSlots + ").");
                    return;
                }

                Delegate body = entry.ByNetId ? (Delegate)EventNetIdSlotBodies[entry.Slot] : EventSlotBodies[entry.Slot];
                entry.HookKeepAlive = body;
                entry.Detour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, body);

                if (entry.ByNetId)
                {
                    DispatchEventByNetIdHookDelegate tramp = entry.Detour.GenerateTrampoline<DispatchEventByNetIdHookDelegate>();
                    entry.Trampoline = tramp;
                    if (tramp == null)
                    {
                        this.RevertHalfInstalledDetour(entry);
                        return;
                    }
                    eventSlotTrampolineNetId[entry.Slot] = tramp;
                }
                else
                {
                    DispatchEventHookDelegate tramp = entry.Detour.GenerateTrampoline<DispatchEventHookDelegate>();
                    entry.Trampoline = tramp;
                    if (tramp == null)
                    {
                        this.RevertHalfInstalledDetour(entry);
                        return;
                    }
                    eventSlotTrampoline[entry.Slot] = tramp;
                }

                eventSlotPayloadLen[entry.Slot] = entry.PayloadBytes;
                eventSlotSuppressForward[entry.Slot] = entry.SuppressForward;
                eventSlotStringOffset[entry.Slot] = entry.StringFieldOffset;
                this.SyncGameEventHookSlotHandlerCount(entry);
                entry.Installed = true;
                ModLogger.Msg("[EventHook] hooked " + entry.EventFullName + " @0x" + nativePtr.ToInt64().ToString("X")
                    + " (slot " + entry.Slot + ", " + (entry.ByNetId ? "per-netId" : "global") + ")");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[EventHook] install failed for " + entry.EventFullName + ": " + ex.Message);
            }
        }

        // Without a working trampoline the game would stop dispatching this event — revert so normal
        // gameplay is untouched.
        private void RevertHalfInstalledDetour(GameEventHookEntry entry)
        {
            try { entry.Detour?.Undo(); } catch { }
            entry.Detour = null;
            entry.HookKeepAlive = null;
            entry.Trampoline = null;
            ModLogger.Msg("[EventHook] trampoline unavailable for " + entry.EventFullName + "; detour reverted");
        }

        // Inflate the open generic EventCenter.DispatchEvent<T> for a concrete event struct class.
        // Mirrors TryAutoIceSkatingInflateAuraGenericMethod; validates the expected param count
        // (1 = global `in T`, 2 = per-netId `(uint, in T)`).
        private unsafe bool TryInflateDispatchForEvent(IntPtr openMethod, IntPtr eventClass, int expectedParamCount, out IntPtr inflatedMethod)
        {
            inflatedMethod = IntPtr.Zero;
            if (openMethod == IntPtr.Zero
                || eventClass == IntPtr.Zero
                || auraMonoClassGetType == null
                || auraMonoMetadataGetGenericInst == null
                || auraMonoClassInflateGenericMethod == null)
            {
                return false;
            }

            IntPtr typeArg = auraMonoClassGetType(eventClass);
            if (typeArg == IntPtr.Zero)
            {
                return false;
            }

            IntPtr* typeArgs = stackalloc IntPtr[1];
            typeArgs[0] = typeArg;
            IntPtr genericInst = auraMonoMetadataGetGenericInst(1, (IntPtr)typeArgs);
            if (genericInst == IntPtr.Zero)
            {
                return false;
            }

            MonoGenericContext context = new MonoGenericContext
            {
                class_inst = IntPtr.Zero,
                method_inst = genericInst
            };

            inflatedMethod = auraMonoClassInflateGenericMethod(openMethod, ref context);
            if (inflatedMethod == IntPtr.Zero)
            {
                return false;
            }

            // Guard the native signature we splice our hook over: a wrong method_inst would
            // otherwise hand the body a garbage pointer and AV the process.
            return AuraMonoMethodParamCountIs(inflatedMethod, (uint)expectedParamCount);
        }

        // ---- Native detour bodies. MaxEventHookSlots fixed static thunks (no closures) routing to
        // RouteEventSlot.
        // MUST NOT throw across the boundary, allocate, or call into Mono/Il2Cpp/Unity. They only
        // Marshal.Copy the payload into a reused buffer and forward via the trampoline. ----

        private static readonly DispatchEventHookDelegate[] EventSlotBodies =
        {
            EventSlotBody0, EventSlotBody1, EventSlotBody2, EventSlotBody3,
            EventSlotBody4, EventSlotBody5, EventSlotBody6, EventSlotBody7,
            EventSlotBody8, EventSlotBody9, EventSlotBody10, EventSlotBody11,
            EventSlotBody12, EventSlotBody13, EventSlotBody14, EventSlotBody15,
            EventSlotBody16, EventSlotBody17, EventSlotBody18, EventSlotBody19,
            EventSlotBody20, EventSlotBody21, EventSlotBody22, EventSlotBody23,
            EventSlotBody24, EventSlotBody25, EventSlotBody26, EventSlotBody27,
            EventSlotBody28, EventSlotBody29, EventSlotBody30, EventSlotBody31,
            EventSlotBody32, EventSlotBody33, EventSlotBody34, EventSlotBody35,
            EventSlotBody36, EventSlotBody37, EventSlotBody38, EventSlotBody39,
            EventSlotBody40, EventSlotBody41, EventSlotBody42, EventSlotBody43,
            EventSlotBody44, EventSlotBody45, EventSlotBody46, EventSlotBody47,
            EventSlotBody48, EventSlotBody49, EventSlotBody50, EventSlotBody51,
            EventSlotBody52, EventSlotBody53, EventSlotBody54, EventSlotBody55,
            EventSlotBody56, EventSlotBody57, EventSlotBody58, EventSlotBody59,
            EventSlotBody60, EventSlotBody61, EventSlotBody62, EventSlotBody63,
            EventSlotBody64, EventSlotBody65, EventSlotBody66, EventSlotBody67,
            EventSlotBody68, EventSlotBody69, EventSlotBody70, EventSlotBody71,
            EventSlotBody72, EventSlotBody73, EventSlotBody74, EventSlotBody75,
            EventSlotBody76, EventSlotBody77, EventSlotBody78, EventSlotBody79,
            EventSlotBody80, EventSlotBody81, EventSlotBody82, EventSlotBody83,
            EventSlotBody84, EventSlotBody85, EventSlotBody86, EventSlotBody87,
            EventSlotBody88, EventSlotBody89, EventSlotBody90, EventSlotBody91,
            EventSlotBody92, EventSlotBody93, EventSlotBody94, EventSlotBody95,
            EventSlotBody96, EventSlotBody97, EventSlotBody98, EventSlotBody99,
            EventSlotBody100, EventSlotBody101, EventSlotBody102, EventSlotBody103,
            EventSlotBody104, EventSlotBody105, EventSlotBody106, EventSlotBody107,
            EventSlotBody108, EventSlotBody109, EventSlotBody110, EventSlotBody111,
            EventSlotBody112, EventSlotBody113, EventSlotBody114, EventSlotBody115,
            EventSlotBody116, EventSlotBody117, EventSlotBody118, EventSlotBody119,
            EventSlotBody120, EventSlotBody121, EventSlotBody122, EventSlotBody123,
            EventSlotBody124, EventSlotBody125, EventSlotBody126, EventSlotBody127
        };

        private static void EventSlotBody0(IntPtr p) => RouteEventSlot(0, p);
        private static void EventSlotBody1(IntPtr p) => RouteEventSlot(1, p);
        private static void EventSlotBody2(IntPtr p) => RouteEventSlot(2, p);
        private static void EventSlotBody3(IntPtr p) => RouteEventSlot(3, p);
        private static void EventSlotBody4(IntPtr p) => RouteEventSlot(4, p);
        private static void EventSlotBody5(IntPtr p) => RouteEventSlot(5, p);
        private static void EventSlotBody6(IntPtr p) => RouteEventSlot(6, p);
        private static void EventSlotBody7(IntPtr p) => RouteEventSlot(7, p);
        private static void EventSlotBody8(IntPtr p) => RouteEventSlot(8, p);
        private static void EventSlotBody9(IntPtr p) => RouteEventSlot(9, p);
        private static void EventSlotBody10(IntPtr p) => RouteEventSlot(10, p);
        private static void EventSlotBody11(IntPtr p) => RouteEventSlot(11, p);
        private static void EventSlotBody12(IntPtr p) => RouteEventSlot(12, p);
        private static void EventSlotBody13(IntPtr p) => RouteEventSlot(13, p);
        private static void EventSlotBody14(IntPtr p) => RouteEventSlot(14, p);
        private static void EventSlotBody15(IntPtr p) => RouteEventSlot(15, p);
        private static void EventSlotBody16(IntPtr p) => RouteEventSlot(16, p);
        private static void EventSlotBody17(IntPtr p) => RouteEventSlot(17, p);
        private static void EventSlotBody18(IntPtr p) => RouteEventSlot(18, p);
        private static void EventSlotBody19(IntPtr p) => RouteEventSlot(19, p);
        private static void EventSlotBody20(IntPtr p) => RouteEventSlot(20, p);
        private static void EventSlotBody21(IntPtr p) => RouteEventSlot(21, p);
        private static void EventSlotBody22(IntPtr p) => RouteEventSlot(22, p);
        private static void EventSlotBody23(IntPtr p) => RouteEventSlot(23, p);
        private static void EventSlotBody24(IntPtr p) => RouteEventSlot(24, p);
        private static void EventSlotBody25(IntPtr p) => RouteEventSlot(25, p);
        private static void EventSlotBody26(IntPtr p) => RouteEventSlot(26, p);
        private static void EventSlotBody27(IntPtr p) => RouteEventSlot(27, p);
        private static void EventSlotBody28(IntPtr p) => RouteEventSlot(28, p);
        private static void EventSlotBody29(IntPtr p) => RouteEventSlot(29, p);
        private static void EventSlotBody30(IntPtr p) => RouteEventSlot(30, p);
        private static void EventSlotBody31(IntPtr p) => RouteEventSlot(31, p);
        private static void EventSlotBody32(IntPtr p) => RouteEventSlot(32, p);
        private static void EventSlotBody33(IntPtr p) => RouteEventSlot(33, p);
        private static void EventSlotBody34(IntPtr p) => RouteEventSlot(34, p);
        private static void EventSlotBody35(IntPtr p) => RouteEventSlot(35, p);
        private static void EventSlotBody36(IntPtr p) => RouteEventSlot(36, p);
        private static void EventSlotBody37(IntPtr p) => RouteEventSlot(37, p);
        private static void EventSlotBody38(IntPtr p) => RouteEventSlot(38, p);
        private static void EventSlotBody39(IntPtr p) => RouteEventSlot(39, p);
        private static void EventSlotBody40(IntPtr p) => RouteEventSlot(40, p);
        private static void EventSlotBody41(IntPtr p) => RouteEventSlot(41, p);
        private static void EventSlotBody42(IntPtr p) => RouteEventSlot(42, p);
        private static void EventSlotBody43(IntPtr p) => RouteEventSlot(43, p);
        private static void EventSlotBody44(IntPtr p) => RouteEventSlot(44, p);
        private static void EventSlotBody45(IntPtr p) => RouteEventSlot(45, p);
        private static void EventSlotBody46(IntPtr p) => RouteEventSlot(46, p);
        private static void EventSlotBody47(IntPtr p) => RouteEventSlot(47, p);
        private static void EventSlotBody48(IntPtr p) => RouteEventSlot(48, p);
        private static void EventSlotBody49(IntPtr p) => RouteEventSlot(49, p);
        private static void EventSlotBody50(IntPtr p) => RouteEventSlot(50, p);
        private static void EventSlotBody51(IntPtr p) => RouteEventSlot(51, p);
        private static void EventSlotBody52(IntPtr p) => RouteEventSlot(52, p);
        private static void EventSlotBody53(IntPtr p) => RouteEventSlot(53, p);
        private static void EventSlotBody54(IntPtr p) => RouteEventSlot(54, p);
        private static void EventSlotBody55(IntPtr p) => RouteEventSlot(55, p);
        private static void EventSlotBody56(IntPtr p) => RouteEventSlot(56, p);
        private static void EventSlotBody57(IntPtr p) => RouteEventSlot(57, p);
        private static void EventSlotBody58(IntPtr p) => RouteEventSlot(58, p);
        private static void EventSlotBody59(IntPtr p) => RouteEventSlot(59, p);
        private static void EventSlotBody60(IntPtr p) => RouteEventSlot(60, p);
        private static void EventSlotBody61(IntPtr p) => RouteEventSlot(61, p);
        private static void EventSlotBody62(IntPtr p) => RouteEventSlot(62, p);
        private static void EventSlotBody63(IntPtr p) => RouteEventSlot(63, p);
        private static void EventSlotBody64(IntPtr p) => RouteEventSlot(64, p);
        private static void EventSlotBody65(IntPtr p) => RouteEventSlot(65, p);
        private static void EventSlotBody66(IntPtr p) => RouteEventSlot(66, p);
        private static void EventSlotBody67(IntPtr p) => RouteEventSlot(67, p);
        private static void EventSlotBody68(IntPtr p) => RouteEventSlot(68, p);
        private static void EventSlotBody69(IntPtr p) => RouteEventSlot(69, p);
        private static void EventSlotBody70(IntPtr p) => RouteEventSlot(70, p);
        private static void EventSlotBody71(IntPtr p) => RouteEventSlot(71, p);
        private static void EventSlotBody72(IntPtr p) => RouteEventSlot(72, p);
        private static void EventSlotBody73(IntPtr p) => RouteEventSlot(73, p);
        private static void EventSlotBody74(IntPtr p) => RouteEventSlot(74, p);
        private static void EventSlotBody75(IntPtr p) => RouteEventSlot(75, p);
        private static void EventSlotBody76(IntPtr p) => RouteEventSlot(76, p);
        private static void EventSlotBody77(IntPtr p) => RouteEventSlot(77, p);
        private static void EventSlotBody78(IntPtr p) => RouteEventSlot(78, p);
        private static void EventSlotBody79(IntPtr p) => RouteEventSlot(79, p);
        private static void EventSlotBody80(IntPtr p) => RouteEventSlot(80, p);
        private static void EventSlotBody81(IntPtr p) => RouteEventSlot(81, p);
        private static void EventSlotBody82(IntPtr p) => RouteEventSlot(82, p);
        private static void EventSlotBody83(IntPtr p) => RouteEventSlot(83, p);
        private static void EventSlotBody84(IntPtr p) => RouteEventSlot(84, p);
        private static void EventSlotBody85(IntPtr p) => RouteEventSlot(85, p);
        private static void EventSlotBody86(IntPtr p) => RouteEventSlot(86, p);
        private static void EventSlotBody87(IntPtr p) => RouteEventSlot(87, p);
        private static void EventSlotBody88(IntPtr p) => RouteEventSlot(88, p);
        private static void EventSlotBody89(IntPtr p) => RouteEventSlot(89, p);
        private static void EventSlotBody90(IntPtr p) => RouteEventSlot(90, p);
        private static void EventSlotBody91(IntPtr p) => RouteEventSlot(91, p);
        private static void EventSlotBody92(IntPtr p) => RouteEventSlot(92, p);
        private static void EventSlotBody93(IntPtr p) => RouteEventSlot(93, p);
        private static void EventSlotBody94(IntPtr p) => RouteEventSlot(94, p);
        private static void EventSlotBody95(IntPtr p) => RouteEventSlot(95, p);
        private static void EventSlotBody96(IntPtr p) => RouteEventSlot(96, p);
        private static void EventSlotBody97(IntPtr p) => RouteEventSlot(97, p);
        private static void EventSlotBody98(IntPtr p) => RouteEventSlot(98, p);
        private static void EventSlotBody99(IntPtr p) => RouteEventSlot(99, p);
        private static void EventSlotBody100(IntPtr p) => RouteEventSlot(100, p);
        private static void EventSlotBody101(IntPtr p) => RouteEventSlot(101, p);
        private static void EventSlotBody102(IntPtr p) => RouteEventSlot(102, p);
        private static void EventSlotBody103(IntPtr p) => RouteEventSlot(103, p);
        private static void EventSlotBody104(IntPtr p) => RouteEventSlot(104, p);
        private static void EventSlotBody105(IntPtr p) => RouteEventSlot(105, p);
        private static void EventSlotBody106(IntPtr p) => RouteEventSlot(106, p);
        private static void EventSlotBody107(IntPtr p) => RouteEventSlot(107, p);
        private static void EventSlotBody108(IntPtr p) => RouteEventSlot(108, p);
        private static void EventSlotBody109(IntPtr p) => RouteEventSlot(109, p);
        private static void EventSlotBody110(IntPtr p) => RouteEventSlot(110, p);
        private static void EventSlotBody111(IntPtr p) => RouteEventSlot(111, p);
        private static void EventSlotBody112(IntPtr p) => RouteEventSlot(112, p);
        private static void EventSlotBody113(IntPtr p) => RouteEventSlot(113, p);
        private static void EventSlotBody114(IntPtr p) => RouteEventSlot(114, p);
        private static void EventSlotBody115(IntPtr p) => RouteEventSlot(115, p);
        private static void EventSlotBody116(IntPtr p) => RouteEventSlot(116, p);
        private static void EventSlotBody117(IntPtr p) => RouteEventSlot(117, p);
        private static void EventSlotBody118(IntPtr p) => RouteEventSlot(118, p);
        private static void EventSlotBody119(IntPtr p) => RouteEventSlot(119, p);
        private static void EventSlotBody120(IntPtr p) => RouteEventSlot(120, p);
        private static void EventSlotBody121(IntPtr p) => RouteEventSlot(121, p);
        private static void EventSlotBody122(IntPtr p) => RouteEventSlot(122, p);
        private static void EventSlotBody123(IntPtr p) => RouteEventSlot(123, p);
        private static void EventSlotBody124(IntPtr p) => RouteEventSlot(124, p);
        private static void EventSlotBody125(IntPtr p) => RouteEventSlot(125, p);
        private static void EventSlotBody126(IntPtr p) => RouteEventSlot(126, p);
        private static void EventSlotBody127(IntPtr p) => RouteEventSlot(127, p);

        private static void RouteEventSlot(int slot, IntPtr eventPtr)
        {
            if (eventSlotSuppressForward[slot])
            {
                if (eventSlotHandlerCount[slot] > 0)
                {
                    PushEventToRing(slot, 0u, eventPtr);
                }

                return;
            }

            DispatchEventHookDelegate orig = eventSlotTrampoline[slot];
            PushEventToRing(slot, 0u, eventPtr);
            if (orig != null)
            {
                orig(eventPtr);
            }
        }

        // ---- Per-netId native bodies. MaxEventHookSlots fixed static thunks routing to RouteEventNetIdSlot.
        // Same boundary rules as the global bodies; they additionally carry the dispatch netId. ----

        private static readonly DispatchEventByNetIdHookDelegate[] EventNetIdSlotBodies =
        {
            EventNetIdSlotBody0, EventNetIdSlotBody1, EventNetIdSlotBody2, EventNetIdSlotBody3,
            EventNetIdSlotBody4, EventNetIdSlotBody5, EventNetIdSlotBody6, EventNetIdSlotBody7,
            EventNetIdSlotBody8, EventNetIdSlotBody9, EventNetIdSlotBody10, EventNetIdSlotBody11,
            EventNetIdSlotBody12, EventNetIdSlotBody13, EventNetIdSlotBody14, EventNetIdSlotBody15,
            EventNetIdSlotBody16, EventNetIdSlotBody17, EventNetIdSlotBody18, EventNetIdSlotBody19,
            EventNetIdSlotBody20, EventNetIdSlotBody21, EventNetIdSlotBody22, EventNetIdSlotBody23,
            EventNetIdSlotBody24, EventNetIdSlotBody25, EventNetIdSlotBody26, EventNetIdSlotBody27,
            EventNetIdSlotBody28, EventNetIdSlotBody29, EventNetIdSlotBody30, EventNetIdSlotBody31,
            EventNetIdSlotBody32, EventNetIdSlotBody33, EventNetIdSlotBody34, EventNetIdSlotBody35,
            EventNetIdSlotBody36, EventNetIdSlotBody37, EventNetIdSlotBody38, EventNetIdSlotBody39,
            EventNetIdSlotBody40, EventNetIdSlotBody41, EventNetIdSlotBody42, EventNetIdSlotBody43,
            EventNetIdSlotBody44, EventNetIdSlotBody45, EventNetIdSlotBody46, EventNetIdSlotBody47,
            EventNetIdSlotBody48, EventNetIdSlotBody49, EventNetIdSlotBody50, EventNetIdSlotBody51,
            EventNetIdSlotBody52, EventNetIdSlotBody53, EventNetIdSlotBody54, EventNetIdSlotBody55,
            EventNetIdSlotBody56, EventNetIdSlotBody57, EventNetIdSlotBody58, EventNetIdSlotBody59,
            EventNetIdSlotBody60, EventNetIdSlotBody61, EventNetIdSlotBody62, EventNetIdSlotBody63,
            EventNetIdSlotBody64, EventNetIdSlotBody65, EventNetIdSlotBody66, EventNetIdSlotBody67,
            EventNetIdSlotBody68, EventNetIdSlotBody69, EventNetIdSlotBody70, EventNetIdSlotBody71,
            EventNetIdSlotBody72, EventNetIdSlotBody73, EventNetIdSlotBody74, EventNetIdSlotBody75,
            EventNetIdSlotBody76, EventNetIdSlotBody77, EventNetIdSlotBody78, EventNetIdSlotBody79,
            EventNetIdSlotBody80, EventNetIdSlotBody81, EventNetIdSlotBody82, EventNetIdSlotBody83,
            EventNetIdSlotBody84, EventNetIdSlotBody85, EventNetIdSlotBody86, EventNetIdSlotBody87,
            EventNetIdSlotBody88, EventNetIdSlotBody89, EventNetIdSlotBody90, EventNetIdSlotBody91,
            EventNetIdSlotBody92, EventNetIdSlotBody93, EventNetIdSlotBody94, EventNetIdSlotBody95,
            EventNetIdSlotBody96, EventNetIdSlotBody97, EventNetIdSlotBody98, EventNetIdSlotBody99,
            EventNetIdSlotBody100, EventNetIdSlotBody101, EventNetIdSlotBody102, EventNetIdSlotBody103,
            EventNetIdSlotBody104, EventNetIdSlotBody105, EventNetIdSlotBody106, EventNetIdSlotBody107,
            EventNetIdSlotBody108, EventNetIdSlotBody109, EventNetIdSlotBody110, EventNetIdSlotBody111,
            EventNetIdSlotBody112, EventNetIdSlotBody113, EventNetIdSlotBody114, EventNetIdSlotBody115,
            EventNetIdSlotBody116, EventNetIdSlotBody117, EventNetIdSlotBody118, EventNetIdSlotBody119,
            EventNetIdSlotBody120, EventNetIdSlotBody121, EventNetIdSlotBody122, EventNetIdSlotBody123,
            EventNetIdSlotBody124, EventNetIdSlotBody125, EventNetIdSlotBody126, EventNetIdSlotBody127
        };

        private static void EventNetIdSlotBody0(uint n, IntPtr p) => RouteEventNetIdSlot(0, n, p);
        private static void EventNetIdSlotBody1(uint n, IntPtr p) => RouteEventNetIdSlot(1, n, p);
        private static void EventNetIdSlotBody2(uint n, IntPtr p) => RouteEventNetIdSlot(2, n, p);
        private static void EventNetIdSlotBody3(uint n, IntPtr p) => RouteEventNetIdSlot(3, n, p);
        private static void EventNetIdSlotBody4(uint n, IntPtr p) => RouteEventNetIdSlot(4, n, p);
        private static void EventNetIdSlotBody5(uint n, IntPtr p) => RouteEventNetIdSlot(5, n, p);
        private static void EventNetIdSlotBody6(uint n, IntPtr p) => RouteEventNetIdSlot(6, n, p);
        private static void EventNetIdSlotBody7(uint n, IntPtr p) => RouteEventNetIdSlot(7, n, p);
        private static void EventNetIdSlotBody8(uint n, IntPtr p) => RouteEventNetIdSlot(8, n, p);
        private static void EventNetIdSlotBody9(uint n, IntPtr p) => RouteEventNetIdSlot(9, n, p);
        private static void EventNetIdSlotBody10(uint n, IntPtr p) => RouteEventNetIdSlot(10, n, p);
        private static void EventNetIdSlotBody11(uint n, IntPtr p) => RouteEventNetIdSlot(11, n, p);
        private static void EventNetIdSlotBody12(uint n, IntPtr p) => RouteEventNetIdSlot(12, n, p);
        private static void EventNetIdSlotBody13(uint n, IntPtr p) => RouteEventNetIdSlot(13, n, p);
        private static void EventNetIdSlotBody14(uint n, IntPtr p) => RouteEventNetIdSlot(14, n, p);
        private static void EventNetIdSlotBody15(uint n, IntPtr p) => RouteEventNetIdSlot(15, n, p);
        private static void EventNetIdSlotBody16(uint n, IntPtr p) => RouteEventNetIdSlot(16, n, p);
        private static void EventNetIdSlotBody17(uint n, IntPtr p) => RouteEventNetIdSlot(17, n, p);
        private static void EventNetIdSlotBody18(uint n, IntPtr p) => RouteEventNetIdSlot(18, n, p);
        private static void EventNetIdSlotBody19(uint n, IntPtr p) => RouteEventNetIdSlot(19, n, p);
        private static void EventNetIdSlotBody20(uint n, IntPtr p) => RouteEventNetIdSlot(20, n, p);
        private static void EventNetIdSlotBody21(uint n, IntPtr p) => RouteEventNetIdSlot(21, n, p);
        private static void EventNetIdSlotBody22(uint n, IntPtr p) => RouteEventNetIdSlot(22, n, p);
        private static void EventNetIdSlotBody23(uint n, IntPtr p) => RouteEventNetIdSlot(23, n, p);
        private static void EventNetIdSlotBody24(uint n, IntPtr p) => RouteEventNetIdSlot(24, n, p);
        private static void EventNetIdSlotBody25(uint n, IntPtr p) => RouteEventNetIdSlot(25, n, p);
        private static void EventNetIdSlotBody26(uint n, IntPtr p) => RouteEventNetIdSlot(26, n, p);
        private static void EventNetIdSlotBody27(uint n, IntPtr p) => RouteEventNetIdSlot(27, n, p);
        private static void EventNetIdSlotBody28(uint n, IntPtr p) => RouteEventNetIdSlot(28, n, p);
        private static void EventNetIdSlotBody29(uint n, IntPtr p) => RouteEventNetIdSlot(29, n, p);
        private static void EventNetIdSlotBody30(uint n, IntPtr p) => RouteEventNetIdSlot(30, n, p);
        private static void EventNetIdSlotBody31(uint n, IntPtr p) => RouteEventNetIdSlot(31, n, p);
        private static void EventNetIdSlotBody32(uint n, IntPtr p) => RouteEventNetIdSlot(32, n, p);
        private static void EventNetIdSlotBody33(uint n, IntPtr p) => RouteEventNetIdSlot(33, n, p);
        private static void EventNetIdSlotBody34(uint n, IntPtr p) => RouteEventNetIdSlot(34, n, p);
        private static void EventNetIdSlotBody35(uint n, IntPtr p) => RouteEventNetIdSlot(35, n, p);
        private static void EventNetIdSlotBody36(uint n, IntPtr p) => RouteEventNetIdSlot(36, n, p);
        private static void EventNetIdSlotBody37(uint n, IntPtr p) => RouteEventNetIdSlot(37, n, p);
        private static void EventNetIdSlotBody38(uint n, IntPtr p) => RouteEventNetIdSlot(38, n, p);
        private static void EventNetIdSlotBody39(uint n, IntPtr p) => RouteEventNetIdSlot(39, n, p);
        private static void EventNetIdSlotBody40(uint n, IntPtr p) => RouteEventNetIdSlot(40, n, p);
        private static void EventNetIdSlotBody41(uint n, IntPtr p) => RouteEventNetIdSlot(41, n, p);
        private static void EventNetIdSlotBody42(uint n, IntPtr p) => RouteEventNetIdSlot(42, n, p);
        private static void EventNetIdSlotBody43(uint n, IntPtr p) => RouteEventNetIdSlot(43, n, p);
        private static void EventNetIdSlotBody44(uint n, IntPtr p) => RouteEventNetIdSlot(44, n, p);
        private static void EventNetIdSlotBody45(uint n, IntPtr p) => RouteEventNetIdSlot(45, n, p);
        private static void EventNetIdSlotBody46(uint n, IntPtr p) => RouteEventNetIdSlot(46, n, p);
        private static void EventNetIdSlotBody47(uint n, IntPtr p) => RouteEventNetIdSlot(47, n, p);
        private static void EventNetIdSlotBody48(uint n, IntPtr p) => RouteEventNetIdSlot(48, n, p);
        private static void EventNetIdSlotBody49(uint n, IntPtr p) => RouteEventNetIdSlot(49, n, p);
        private static void EventNetIdSlotBody50(uint n, IntPtr p) => RouteEventNetIdSlot(50, n, p);
        private static void EventNetIdSlotBody51(uint n, IntPtr p) => RouteEventNetIdSlot(51, n, p);
        private static void EventNetIdSlotBody52(uint n, IntPtr p) => RouteEventNetIdSlot(52, n, p);
        private static void EventNetIdSlotBody53(uint n, IntPtr p) => RouteEventNetIdSlot(53, n, p);
        private static void EventNetIdSlotBody54(uint n, IntPtr p) => RouteEventNetIdSlot(54, n, p);
        private static void EventNetIdSlotBody55(uint n, IntPtr p) => RouteEventNetIdSlot(55, n, p);
        private static void EventNetIdSlotBody56(uint n, IntPtr p) => RouteEventNetIdSlot(56, n, p);
        private static void EventNetIdSlotBody57(uint n, IntPtr p) => RouteEventNetIdSlot(57, n, p);
        private static void EventNetIdSlotBody58(uint n, IntPtr p) => RouteEventNetIdSlot(58, n, p);
        private static void EventNetIdSlotBody59(uint n, IntPtr p) => RouteEventNetIdSlot(59, n, p);
        private static void EventNetIdSlotBody60(uint n, IntPtr p) => RouteEventNetIdSlot(60, n, p);
        private static void EventNetIdSlotBody61(uint n, IntPtr p) => RouteEventNetIdSlot(61, n, p);
        private static void EventNetIdSlotBody62(uint n, IntPtr p) => RouteEventNetIdSlot(62, n, p);
        private static void EventNetIdSlotBody63(uint n, IntPtr p) => RouteEventNetIdSlot(63, n, p);
        private static void EventNetIdSlotBody64(uint n, IntPtr p) => RouteEventNetIdSlot(64, n, p);
        private static void EventNetIdSlotBody65(uint n, IntPtr p) => RouteEventNetIdSlot(65, n, p);
        private static void EventNetIdSlotBody66(uint n, IntPtr p) => RouteEventNetIdSlot(66, n, p);
        private static void EventNetIdSlotBody67(uint n, IntPtr p) => RouteEventNetIdSlot(67, n, p);
        private static void EventNetIdSlotBody68(uint n, IntPtr p) => RouteEventNetIdSlot(68, n, p);
        private static void EventNetIdSlotBody69(uint n, IntPtr p) => RouteEventNetIdSlot(69, n, p);
        private static void EventNetIdSlotBody70(uint n, IntPtr p) => RouteEventNetIdSlot(70, n, p);
        private static void EventNetIdSlotBody71(uint n, IntPtr p) => RouteEventNetIdSlot(71, n, p);
        private static void EventNetIdSlotBody72(uint n, IntPtr p) => RouteEventNetIdSlot(72, n, p);
        private static void EventNetIdSlotBody73(uint n, IntPtr p) => RouteEventNetIdSlot(73, n, p);
        private static void EventNetIdSlotBody74(uint n, IntPtr p) => RouteEventNetIdSlot(74, n, p);
        private static void EventNetIdSlotBody75(uint n, IntPtr p) => RouteEventNetIdSlot(75, n, p);
        private static void EventNetIdSlotBody76(uint n, IntPtr p) => RouteEventNetIdSlot(76, n, p);
        private static void EventNetIdSlotBody77(uint n, IntPtr p) => RouteEventNetIdSlot(77, n, p);
        private static void EventNetIdSlotBody78(uint n, IntPtr p) => RouteEventNetIdSlot(78, n, p);
        private static void EventNetIdSlotBody79(uint n, IntPtr p) => RouteEventNetIdSlot(79, n, p);
        private static void EventNetIdSlotBody80(uint n, IntPtr p) => RouteEventNetIdSlot(80, n, p);
        private static void EventNetIdSlotBody81(uint n, IntPtr p) => RouteEventNetIdSlot(81, n, p);
        private static void EventNetIdSlotBody82(uint n, IntPtr p) => RouteEventNetIdSlot(82, n, p);
        private static void EventNetIdSlotBody83(uint n, IntPtr p) => RouteEventNetIdSlot(83, n, p);
        private static void EventNetIdSlotBody84(uint n, IntPtr p) => RouteEventNetIdSlot(84, n, p);
        private static void EventNetIdSlotBody85(uint n, IntPtr p) => RouteEventNetIdSlot(85, n, p);
        private static void EventNetIdSlotBody86(uint n, IntPtr p) => RouteEventNetIdSlot(86, n, p);
        private static void EventNetIdSlotBody87(uint n, IntPtr p) => RouteEventNetIdSlot(87, n, p);
        private static void EventNetIdSlotBody88(uint n, IntPtr p) => RouteEventNetIdSlot(88, n, p);
        private static void EventNetIdSlotBody89(uint n, IntPtr p) => RouteEventNetIdSlot(89, n, p);
        private static void EventNetIdSlotBody90(uint n, IntPtr p) => RouteEventNetIdSlot(90, n, p);
        private static void EventNetIdSlotBody91(uint n, IntPtr p) => RouteEventNetIdSlot(91, n, p);
        private static void EventNetIdSlotBody92(uint n, IntPtr p) => RouteEventNetIdSlot(92, n, p);
        private static void EventNetIdSlotBody93(uint n, IntPtr p) => RouteEventNetIdSlot(93, n, p);
        private static void EventNetIdSlotBody94(uint n, IntPtr p) => RouteEventNetIdSlot(94, n, p);
        private static void EventNetIdSlotBody95(uint n, IntPtr p) => RouteEventNetIdSlot(95, n, p);
        private static void EventNetIdSlotBody96(uint n, IntPtr p) => RouteEventNetIdSlot(96, n, p);
        private static void EventNetIdSlotBody97(uint n, IntPtr p) => RouteEventNetIdSlot(97, n, p);
        private static void EventNetIdSlotBody98(uint n, IntPtr p) => RouteEventNetIdSlot(98, n, p);
        private static void EventNetIdSlotBody99(uint n, IntPtr p) => RouteEventNetIdSlot(99, n, p);
        private static void EventNetIdSlotBody100(uint n, IntPtr p) => RouteEventNetIdSlot(100, n, p);
        private static void EventNetIdSlotBody101(uint n, IntPtr p) => RouteEventNetIdSlot(101, n, p);
        private static void EventNetIdSlotBody102(uint n, IntPtr p) => RouteEventNetIdSlot(102, n, p);
        private static void EventNetIdSlotBody103(uint n, IntPtr p) => RouteEventNetIdSlot(103, n, p);
        private static void EventNetIdSlotBody104(uint n, IntPtr p) => RouteEventNetIdSlot(104, n, p);
        private static void EventNetIdSlotBody105(uint n, IntPtr p) => RouteEventNetIdSlot(105, n, p);
        private static void EventNetIdSlotBody106(uint n, IntPtr p) => RouteEventNetIdSlot(106, n, p);
        private static void EventNetIdSlotBody107(uint n, IntPtr p) => RouteEventNetIdSlot(107, n, p);
        private static void EventNetIdSlotBody108(uint n, IntPtr p) => RouteEventNetIdSlot(108, n, p);
        private static void EventNetIdSlotBody109(uint n, IntPtr p) => RouteEventNetIdSlot(109, n, p);
        private static void EventNetIdSlotBody110(uint n, IntPtr p) => RouteEventNetIdSlot(110, n, p);
        private static void EventNetIdSlotBody111(uint n, IntPtr p) => RouteEventNetIdSlot(111, n, p);
        private static void EventNetIdSlotBody112(uint n, IntPtr p) => RouteEventNetIdSlot(112, n, p);
        private static void EventNetIdSlotBody113(uint n, IntPtr p) => RouteEventNetIdSlot(113, n, p);
        private static void EventNetIdSlotBody114(uint n, IntPtr p) => RouteEventNetIdSlot(114, n, p);
        private static void EventNetIdSlotBody115(uint n, IntPtr p) => RouteEventNetIdSlot(115, n, p);
        private static void EventNetIdSlotBody116(uint n, IntPtr p) => RouteEventNetIdSlot(116, n, p);
        private static void EventNetIdSlotBody117(uint n, IntPtr p) => RouteEventNetIdSlot(117, n, p);
        private static void EventNetIdSlotBody118(uint n, IntPtr p) => RouteEventNetIdSlot(118, n, p);
        private static void EventNetIdSlotBody119(uint n, IntPtr p) => RouteEventNetIdSlot(119, n, p);
        private static void EventNetIdSlotBody120(uint n, IntPtr p) => RouteEventNetIdSlot(120, n, p);
        private static void EventNetIdSlotBody121(uint n, IntPtr p) => RouteEventNetIdSlot(121, n, p);
        private static void EventNetIdSlotBody122(uint n, IntPtr p) => RouteEventNetIdSlot(122, n, p);
        private static void EventNetIdSlotBody123(uint n, IntPtr p) => RouteEventNetIdSlot(123, n, p);
        private static void EventNetIdSlotBody124(uint n, IntPtr p) => RouteEventNetIdSlot(124, n, p);
        private static void EventNetIdSlotBody125(uint n, IntPtr p) => RouteEventNetIdSlot(125, n, p);
        private static void EventNetIdSlotBody126(uint n, IntPtr p) => RouteEventNetIdSlot(126, n, p);
        private static void EventNetIdSlotBody127(uint n, IntPtr p) => RouteEventNetIdSlot(127, n, p);

        private static void RouteEventNetIdSlot(int slot, uint netId, IntPtr eventPtr)
        {
            if (eventSlotSuppressForward[slot])
            {
                if (eventSlotHandlerCount[slot] > 0)
                {
                    PushEventToRing(slot, netId, eventPtr);
                }

                return;
            }

            DispatchEventByNetIdHookDelegate orig = eventSlotTrampolineNetId[slot];
            PushEventToRing(slot, netId, eventPtr);
            if (orig != null)
            {
                orig(netId, eventPtr);
            }
        }

        // Snapshot the payload into the reused ring buffer. Native-boundary safe: only a bounded
        // Marshal.Copy + index writes, never allocates or calls into Mono. Single-threaded in practice.
        private static void PushEventToRing(int slot, uint netId, IntPtr eventPtr)
        {
            try
            {
                int len = eventSlotPayloadLen[slot];
                int w = eventRingWrite;
                int idx = w & (EventRingSize - 1);
                if (len > 0 && eventPtr != IntPtr.Zero)
                {
                    byte[] buf = eventRingData[idx];
                    if (len > buf.Length)
                    {
                        len = buf.Length;
                    }
                    Marshal.Copy(eventPtr, buf, 0, len);
                    eventRingLen[idx] = len;
                }
                else
                {
                    eventRingLen[idx] = 0; // empty-payload event (e.g. *CloseEvent) — record dispatch
                }

                // Optional string-field capture. PURE MEMORY READS ONLY — no mono calls, no
                // allocation: a detour body that calls into Mono (mono_string_to_utf8) while Mono
                // holds its runtime locks deadlocks the main thread — that mistake froze the game
                // once and is why the string is copied out by value here instead.
                // Mono string layout on x64: [MonoObject vtable+sync = 16][int32 length][UTF-16 chars].
                int strOffset = eventSlotStringOffset[slot];
                int strBytes = 0;
                if (strOffset >= 0 && eventPtr != IntPtr.Zero)
                {
                    IntPtr strObj = Marshal.ReadIntPtr(eventPtr, strOffset);
                    if (strObj != IntPtr.Zero)
                    {
                        int chars = Marshal.ReadInt32(strObj, 2 * IntPtr.Size);
                        if (chars > 0)
                        {
                            strBytes = chars * 2;
                            if (strBytes > EventStringCap)
                            {
                                strBytes = EventStringCap;
                            }
                            Marshal.Copy(strObj + (2 * IntPtr.Size) + 4, eventRingStringData[idx], 0, strBytes);
                        }
                    }
                }
                eventRingStringLen[idx] = strBytes;

                eventRingSlot[idx] = (byte)slot;
                eventRingNetId[idx] = netId;
                eventRingWrite = w + 1;
            }
            catch
            {
            }
        }

        private void DrainGameEventHooks()
        {
            int guard = 0;
            while (eventRingRead != eventRingWrite && guard++ < EventRingSize)
            {
                int idx = eventRingRead & (EventRingSize - 1);
                int slot = eventRingSlot[idx];
                int len = eventRingLen[idx];
                uint netId = eventRingNetId[idx];
                byte[] buf = eventRingData[idx];
                byte[] strBuf = eventRingStringData[idx];
                int strLen = eventRingStringLen[idx];
                eventRingRead++;

                if (slot < 0 || slot >= this.gameEventHookSlotCount)
                {
                    continue;
                }

                GameEventHookEntry entry = this.gameEventHookSlots[slot];
                if (entry == null)
                {
                    continue;
                }

                GameEventSnapshot snap = new GameEventSnapshot(entry.EventFullName, netId, buf, len, strBuf, strLen);

#if FEATURE_MCP
                // Agent observation log (McpOps.Data.cs). Deliberately HERE and not in
                // PushEventToRing: this is the main thread, whereas the ring is filled from the
                // detour body, where allocating or touching Mono deadlocks the game. One bool test
                // when no agent is connected.
                McpNoteGameEvent(entry.EventFullName, netId, len);
#endif

                if (MasterLogGameEvents)
                {
                    ModLogger.Msg("[EventHook] " + entry.EventFullName + (netId != 0u ? " netId=" + netId : string.Empty) + " len=" + len + GameEventScalarDump(buf, len));
                }

                List<Action<GameEventSnapshot>> handlers = entry.Handlers;
                for (int h = 0; h < handlers.Count; h++)
                {
                    try
                    {
                        handlers[h](snap);
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Msg("[EventHook] handler error for " + entry.EventFullName + ": " + ex.Message);
                    }
                }
            }
        }

        // ---- EntityCreate/Remove verification PoC (the generic "object appeared on the map"
        // channel). High-frequency, so we don't log each one — we count them and emit a throttled
        // summary + a sample netId/archetype so the EntityData netId@8 offset can be confirmed live
        // and the frequency measured before wiring a feature (e.g. NetCook stove detection) to it. ----
        internal static bool MasterLogEntityEvents = false; // debug toggle for the summary log (measured; off)

        private const string EntityCreateEventName = "XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityCreateEvent";
        private const string EntityRemoveEventName = "XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityRemoveEvent";
        // EntityData: level(sbyte)@0, entityId(uint)@4, netId(uint)@8, field(uint)@12, tag(ulong)@16,
        // archetypeId(short bucket@24/index@26), _priority(int)@28 → 32 bytes.
        private const int EntityEventBytes = 32;
        private const float EntityEventLogInterval = 3f;

        private bool entityEventDebugRegistered;
        private int entityCreateCount;
        private int entityCreateNonZeroCount; // creates with netId != 0 (the networked ones we'd qualify)
        private int entityRemoveCount;
        private uint entityLastCreateNetId;
        private uint entityLastRemoveNetId;
        private int entityLastCreateArch;
        private float entityEventNextLogAt;

        private void ProcessEntityEventDebugOnUpdate()
        {
            if (!MasterLogEntityEvents)
            {
                return;
            }

            if (!this.entityEventDebugRegistered)
            {
                this.entityEventDebugRegistered = true;
                this.RegisterGameEventHook(EntityCreateEventName, EntityEventBytes, this.OnEntityCreateDebug);
                this.RegisterGameEventHook(EntityRemoveEventName, EntityEventBytes, this.OnEntityRemoveDebug);
            }

            if (UnityEngine.Time.unscaledTime < this.entityEventNextLogAt)
            {
                return;
            }
            this.entityEventNextLogAt = UnityEngine.Time.unscaledTime + EntityEventLogInterval;

            if (this.entityCreateCount > 0 || this.entityRemoveCount > 0)
            {
                int bucket = (short)(this.entityLastCreateArch & 0xFFFF);
                int index = (short)((this.entityLastCreateArch >> 16) & 0xFFFF);
                ModLogger.Msg("[EntityEvents] +" + this.entityCreateCount + " (nz=" + this.entityCreateNonZeroCount + ") / -" + this.entityRemoveCount
                    + " in " + EntityEventLogInterval + "s; lastNzCreate netId=" + this.entityLastCreateNetId
                    + " arch=" + bucket + ":" + index + "; lastRemove netId=" + this.entityLastRemoveNetId);
                this.entityCreateCount = 0;
                this.entityCreateNonZeroCount = 0;
                this.entityRemoveCount = 0;
            }
        }

        private void OnEntityCreateDebug(GameEventSnapshot e)
        {
            this.entityCreateCount++;
            uint netId = e.ReadUInt32(8);
            if (netId != 0u)
            {
                // Only the networked entities matter for feature detection (cookers etc.); sample
                // those so the archetype/netId shown isn't drowned out by the netId=0 local spam.
                this.entityCreateNonZeroCount++;
                this.entityLastCreateNetId = netId;
                this.entityLastCreateArch = e.ReadInt32(24);
            }
        }

        private void OnEntityRemoveDebug(GameEventSnapshot e)
        {
            this.entityRemoveCount++;
            this.entityLastRemoveNetId = e.ReadUInt32(8);
        }

        private IntPtr ResolveGameEventClass(string eventFullName)
        {
            if (string.IsNullOrWhiteSpace(eventFullName))
            {
                return IntPtr.Zero;
            }

            IntPtr cls = this.FindAuraMonoClassByFullName(eventFullName);
            if (cls != IntPtr.Zero)
            {
                return cls;
            }

            int lastDot = eventFullName.LastIndexOf('.');
            if (lastDot <= 0)
            {
                return IntPtr.Zero;
            }

            string ns = eventFullName.Substring(0, lastDot);
            string name = eventFullName.Substring(lastDot + 1);
            if (!ns.StartsWith("ScriptsRefactory.DataAndProtocol", StringComparison.Ordinal))
            {
                return IntPtr.Zero;
            }

            cls = this.FindAuraMonoClassInImages(ns, name, new string[]
            {
                "XDTDataAndProtocol",
                "XDTDataAndProtocol.dll",
                "XDTLevelAndEntity",
                "XDTLevelAndEntity.dll",
                "Client",
                "Client.dll"
            });
            if (cls != IntPtr.Zero && MasterLogShowOffBypass)
            {
                ModLogger.Msg("[EventHook] resolved " + eventFullName + " via XDTDataAndProtocol image");
            }

            return cls;
        }

        // Cheap scalar dump for MasterLogGameEvents discovery: first few int/uint words.
        private static string GameEventScalarDump(byte[] buf, int len)
        {
            if (buf == null || len < 4)
            {
                return string.Empty;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(" i32[");
            int words = Math.Min(len / 4, 6);
            for (int i = 0; i < words; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(BitConverter.ToInt32(buf, i * 4));
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
