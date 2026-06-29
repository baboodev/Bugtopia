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
        internal const bool MasterLogGameEvents = false;

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

        private const int MaxEventHookSlots = 16;   // distinct event types we can hook concurrently
        private const int EventPayloadCap = 64;     // max struct bytes snapshotted per dispatch

        // Read-only view over a snapshotted event payload, handed to handlers on the main thread.
        // Valid only for the duration of the handler call (the underlying buffer is ring-reused).
        public readonly struct GameEventSnapshot
        {
            private readonly byte[] _data;
            private readonly int _len;

            public GameEventSnapshot(string eventName, uint netId, byte[] data, int len)
            {
                this.EventName = eventName;
                this.NetId = netId;
                this._data = data;
                this._len = len;
            }

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
            public bool ByNetId; // true => hook the 2-arg DispatchEvent<T>(uint netId, in T) overload
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

        // Register a handler for a GLOBAL game event (dispatched via DispatchEvent<T>(in T)).
        // Idempotent per (name): re-registering the same name adds another handler to the shared
        // detour. payloadBytes = the event struct size from the dump (bytes to snapshot; clamp to
        // EventPayloadCap). Use 0 for empty events. The handler runs on the Unity main thread.
        internal bool RegisterGameEventHook(string eventFullName, int payloadBytes, Action<GameEventSnapshot> handler)
        {
            return this.RegisterGameEventHookInternal(eventFullName, payloadBytes, false, handler);
        }

        // Register a handler for a PER-ENTITY game event (dispatched via DispatchEvent<T>(uint
        // netId, in T) — e.g. dog QTE events). The handler receives the dispatch netId in
        // GameEventSnapshot.NetId. Same name must NOT also be registered as global (one overload per
        // event type per slot).
        internal bool RegisterGameEventHookByNetId(string eventFullName, int payloadBytes, Action<GameEventSnapshot> handler)
        {
            return this.RegisterGameEventHookInternal(eventFullName, payloadBytes, true, handler);
        }

        private bool RegisterGameEventHookInternal(string eventFullName, int payloadBytes, bool byNetId, Action<GameEventSnapshot> handler)
        {
            if (string.IsNullOrEmpty(eventFullName) || handler == null)
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
                existing.Handlers.Add(handler);
                if (clamped > existing.PayloadBytes)
                {
                    existing.PayloadBytes = clamped;
                    if (existing.Installed)
                    {
                        eventSlotPayloadLen[existing.Slot] = clamped;
                    }
                }
                return true;
            }

            if (this.gameEventHookSlotCount >= MaxEventHookSlots)
            {
                ModLogger.Msg("[EventHook] slot pool exhausted (" + MaxEventHookSlots + "); cannot hook " + eventFullName);
                return false;
            }

            GameEventHookEntry entry = new GameEventHookEntry
            {
                EventFullName = eventFullName,
                PayloadBytes = clamped,
                ByNetId = byNetId,
                Slot = this.gameEventHookSlotCount
            };
            entry.Handlers.Add(handler);
            this.gameEventHookSlots[entry.Slot] = entry;
            this.gameEventHooksByName[eventFullName] = entry;
            this.gameEventHookSlotCount++;
            return true;
        }

        // True once the detour for this event is live (used by features that keep an event-driven
        // flag but want a polling fallback until/unless the hook actually installs).
        internal bool IsGameEventHookInstalled(string eventFullName)
        {
            return !string.IsNullOrEmpty(eventFullName)
                && this.gameEventHooksByName.TryGetValue(eventFullName, out GameEventHookEntry e)
                && e.Installed;
        }

        // Called from OnUpdate: installs any not-yet-installed detours (once images are up) and
        // drains buffered dispatches to handlers.
        private void ProcessGameEventHooksOnUpdate()
        {
            if (this.gameEventHookSlotCount == 0)
            {
                return;
            }

            this.EnsureGameEventHooksInstalled();
            this.DrainGameEventHooks();
        }

        private void EnsureGameEventHooksInstalled()
        {
            if (this.gameEventHooksHardFailed)
            {
                return;
            }

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
            IntPtr eventClass = this.FindAuraMonoClassByFullName(entry.EventFullName);
            if (eventClass == IntPtr.Zero)
            {
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

        // ---- Native detour bodies. 16 fixed static thunks (no closures) routing to RouteEventSlot.
        // MUST NOT throw across the boundary, allocate, or call into Mono/Il2Cpp/Unity. They only
        // Marshal.Copy the payload into a reused buffer and forward via the trampoline. ----

        private static readonly DispatchEventHookDelegate[] EventSlotBodies =
        {
            EventSlotBody0, EventSlotBody1, EventSlotBody2, EventSlotBody3,
            EventSlotBody4, EventSlotBody5, EventSlotBody6, EventSlotBody7,
            EventSlotBody8, EventSlotBody9, EventSlotBody10, EventSlotBody11,
            EventSlotBody12, EventSlotBody13, EventSlotBody14, EventSlotBody15
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

        private static void RouteEventSlot(int slot, IntPtr eventPtr)
        {
            DispatchEventHookDelegate orig = eventSlotTrampoline[slot];
            PushEventToRing(slot, 0u, eventPtr);
            if (orig != null)
            {
                orig(eventPtr);
            }
        }

        // ---- Per-netId native bodies. 16 fixed static thunks routing to RouteEventNetIdSlot.
        // Same boundary rules as the global bodies; they additionally carry the dispatch netId. ----

        private static readonly DispatchEventByNetIdHookDelegate[] EventNetIdSlotBodies =
        {
            EventNetIdSlotBody0, EventNetIdSlotBody1, EventNetIdSlotBody2, EventNetIdSlotBody3,
            EventNetIdSlotBody4, EventNetIdSlotBody5, EventNetIdSlotBody6, EventNetIdSlotBody7,
            EventNetIdSlotBody8, EventNetIdSlotBody9, EventNetIdSlotBody10, EventNetIdSlotBody11,
            EventNetIdSlotBody12, EventNetIdSlotBody13, EventNetIdSlotBody14, EventNetIdSlotBody15
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

        private static void RouteEventNetIdSlot(int slot, uint netId, IntPtr eventPtr)
        {
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

                GameEventSnapshot snap = new GameEventSnapshot(entry.EventFullName, netId, buf, len);

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
