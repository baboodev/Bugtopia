#if FEATURE_MCP
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod.Plugins
{
    // ============================================================================================
    // The sandbox plugin contract.
    //
    // A sandbox plugin is experimental code loaded into a COLLECTIBLE AssemblyLoadContext so it can
    // be swapped without restarting the game — the whole point of the exercise, since a restart plus
    // relogin plus walking back to a town costs minutes and a reload costs seconds.
    //
    // ── WHY THIS LIVES IN bugtopia.dll AND NOT A SEPARATE SDK ────────────────────────────────────
    // A separate contract assembly would need its own deployment next to the loader and its own
    // resolution story. Putting it here costs nothing and buys something: a plugin already references
    // bugtopia.dll, so `IHostApi.Mod` can hand it the live HeartopiaComplete instance and every
    // public member of the mod becomes reachable from an experiment. Split it out later only if
    // plugins ever need to outlive a mod rebuild.
    //
    // ── WHAT MAKES UNLOAD ACTUALLY WORK ──────────────────────────────────────────────────────────
    // A collectible ALC is collected only when NOTHING outside it references anything inside it.
    // Several perfectly ordinary things make that impossible forever, so they are contract
    // violations, checked STATICALLY before the bytes are ever loaded (PluginHost.Validate):
    //
    //   * NativeDetour / Harmony — a detour points at a managed trampoline inside the plugin. Unload
    //     it and the next call from game code jumps into freed memory. This mod also never tears
    //     detours down by policy (see the native-detour post-mortems), so there is no safe exit.
    //   * ClassInjector.RegisterTypeInIl2Cpp — injected types get negative type indices that only the
    //     injector's own detours translate; the registration is permanent by construction.
    //   * [DllImport] — the resolved native module handle is cached in the ALC and pins it.
    //   * Thread / Timer — a live thread keeps its ALC alive indefinitely. Use host.StartCoroutine,
    //     which the host revokes on unload.
    //   * DelegateSupport.ConvertDelegate — the wrapper is cached in an Il2CppInterop static.
    //
    // The direction of a reference matters and is easy to get backwards: a plugin holding the host
    // is FINE (inside → outside). The host holding the plugin is what pins, which is why the host
    // drops every reference it took before calling Unload on the ALC.
    // ============================================================================================

    /// Implemented by exactly one public, parameterless-constructible type per plugin assembly.
    /// Every method runs on the Unity main thread.
    public interface IBugtopiaPlugin
    {
        /// Called once, immediately after load. Throwing here aborts the load and unloads the ALC.
        void Load(IHostApi host);

        /// Called every frame from the mod's OnUpdate, inside a circuit breaker: a tick that throws
        /// repeatedly cools down and is eventually disabled rather than spamming every frame.
        void Tick();

        /// Called once before unload. Best-effort cleanup; the host revokes coroutines itself.
        void Unload();

        /// Ad-hoc entry point for `plugin.call` over MCP. Return a JSON string, or null if the
        /// plugin does not handle that method name.
        string Call(string method, string argsJson);
    }

    /// What the host lends a plugin. Everything here is safe to call from Load/Tick/Unload.
    public interface IHostApi
    {
        /// The plugin's id, as passed to plugin.load.
        string Id { get; }

        /// The live mod instance — the whole public surface of the mod, deliberately.
        HeartopiaComplete Mod { get; }

        /// Goes to the mod log, prefixed with the plugin id, and into the MCP log ring.
        void Log(string message);

        /// On-screen toast.
        void Toast(string message);

        /// The world-ready gate. False on the login screen and during loading.
        bool IsWorldReady { get; }

        /// Increments whenever the world changes; cached per-world state must be dropped when it does.
        int WorldEpoch { get; }

        /// Local player position, or false when no anchor can be resolved.
        bool TryGetPlayerPosition(out Vector3 position);

        /// Starts a coroutine on the mod's managed scheduler. AUTOMATICALLY STOPPED on unload — this
        /// is why plugins must not start their own threads or timers.
        void StartCoroutine(IEnumerator routine);

        /// Scratch key/value store, persisted per plugin id under %LocalLow%/Bugtopia/Plugins/.
        IDictionary<string, string> State { get; }

        /// Live reflection over the game's embedded Mono runtime. Without this a snippet can only
        /// reach the mod's PUBLIC surface, which excludes every AuraMono helper — i.e. it could not
        /// touch a single game type.
        IMonoApi Mono { get; }

        /// EventCenter subscriptions. AGENTS.md §7 puts events ahead of polling for anything that
        /// reacts to a state change, so this is what makes that rule testable from a snippet.
        IEventsApi Events { get; }
    }

    /// Subscribing to the game's own event bus.
    ///
    /// Handlers run on the Unity main thread, from the hook engine's drain, and are AUTOMATICALLY
    /// REVOKED when the plugin unloads — which is the only reason this can exist at all. The engine
    /// has no unsubscribe path (its registration installs a native detour, and this mod never tears
    /// detours down), so a handler registered directly by a plugin would keep that plugin's load
    /// context alive forever and silently break hot reload. The host therefore owns the registration
    /// and only the callback behind it belongs to the plugin.
    public interface IEventsApi
    {
        /// Subscribe to an event type by full name, e.g.
        /// "XDTDataAndProtocol.Events.RefreshBackPackEvent".
        ///
        /// `payloadBytes` is how much of the event struct to copy for the handler to read; too small
        /// only truncates what is readable, it cannot corrupt anything. Use mono.describe to see the
        /// struct's fields.
        ///
        /// ⚠️ Each DISTINCT event type costs one of the engine's 48 hook slots and is never
        /// reclaimed (~38 are already used by shipped features). Subscribing to a type some feature
        /// already hooks costs nothing extra.
        bool Subscribe(string eventFullName, int payloadBytes,
                       Action<HeartopiaComplete.GameEventSnapshot> handler, out string error);

        /// How many distinct types are subscribed and how many dispatches have been delivered.
        int SubscribedTypeCount { get; }

        long DispatchedCount { get; }
    }

    /// The narrow AuraMono surface lent to sandbox code.
    ///
    /// ⚠️ Everything here returns raw native handles. Class and method handles live as long as the
    /// image and are safe to keep; an object handle returned by <see cref="Invoke"/> is a
    /// <c>MonoObject*</c> that the moving GC relocates as soon as anything allocates — read what you
    /// need from it immediately, and never store one across a frame or a yield.
    public interface IMonoApi
    {
        /// Resolves a type by full name, trying the build's spelling variants (Gameplay/GamePlay,
        /// Il2Cpp prefix, ScriptsRefactory). IntPtr.Zero when nothing matched.
        IntPtr FindClass(string fullName);

        /// Finds a method by name AND parameter count, walking the base chain. The arity matters:
        /// invoking the wrong overload is a documented way to fault the process.
        IntPtr FindMethod(IntPtr klass, string methodName, int paramCount);

        /// Invokes through the guarded path (null check, exception check, garbage-result
        /// suppression). `instance` is IntPtr.Zero for a static method.
        bool Invoke(IntPtr method, IntPtr instance, IntPtr[] args, out IntPtr result, out string error);

        /// False when the runtime did not expose the GC-handle exports. Everything that returns
        /// pinned objects FAILS CLOSED in that case rather than handing back pointers the moving GC
        /// can relocate under you.
        bool PinningAvailable { get; }

        /// Every live instance of a component type — `Entities.GetComponents&lt;T&gt;` under the hood,
        /// which AGENTS.md §8 names the preferred way to find "all of type T". THE RESULT OWNS PINS:
        /// use it in a `using`, and do not keep the pointers after disposing.
        ///
        /// Do NOT reach for the entity-graph walk instead — dereferencing arbitrary entity pointers
        /// is a documented way to fault the process.
        IMonoObjects GetComponents(IntPtr componentClass);

        /// Items of a mono collection (List&lt;T&gt;, dictionary values, …), pinned at collection time.
        /// Same ownership rules as GetComponents: enumerating into bare pointers and then reading
        /// members off them is the classic mid-loop crash, because each member read can allocate and
        /// move what you are still holding.
        IMonoObjects EnumerateCollection(IntPtr collectionObj);

        /// Reads a member off an object as a boxed value / reference. IntPtr.Zero on miss.
        bool TryGetField(IntPtr obj, string fieldName, out IntPtr valueObj);

        /// Scalarizing readers — the safe shape. Prefer these over holding the boxed object: they
        /// take the value out immediately, so nothing survives to be moved.
        uint GetUInt(IntPtr obj, params string[] fieldNames);

        int GetInt(IntPtr boxedStruct, string fieldName);

        string GetString(IntPtr monoStringObj);

        /// Manual pinning, for a pointer that must outlive the call that produced it. Always paired
        /// with Unpin in a finally.
        uint Pin(IntPtr obj);

        void Unpin(uint handle);

        /// ⚠️ SENDS A REAL COMMAND TO THE LIVE SERVER. A wrong field value is not a wrong answer,
        /// it is a changed account — this is the channel that sells items, submits quests and spends
        /// currency. There is no dry run and no undo.
        ///
        /// `fields` maps field name to value; int, uint, long, float, bool and string are supported
        /// and anything else is REFUSED rather than coerced. `channel` is ChannelType — 1 is
        /// Reliable, which is what every existing caller uses.
        ///
        /// Find the command type and its fields with mono.describe first: the field names must match
        /// the running build exactly, and a name that does not exist stops the send rather than
        /// sending a partly-filled command.
        bool SendCommand(string commandFullName, IDictionary<string, object> fields,
                         int channel, bool needAuthed, out string status);

        /// Everything SendCommand does EXCEPT the send: resolves the type, inflates the generic,
        /// allocates, sets every field and unboxes — then stops. Use it to check a command type and
        /// its field names without mutating anything; on this path "just try it" costs a real
        /// account change.
        ///
        /// A separate method rather than a flag on purpose: nobody sends by mistake because they
        /// forgot a boolean.
        bool ValidateCommand(string commandFullName, IDictionary<string, object> fields, out string status);
    }

    /// A pinned snapshot of mono objects. Disposing releases every pin it took — which is why the
    /// correct usage is also the shortest one:
    ///
    ///     using (var set = host.Mono.GetComponents(cls))
    ///         for (int i = 0; i &lt; set.Count; i++) { … read scalars out of set[i] … }
    ///
    /// The pointers are valid only until Dispose. Copy out netIds, positions and strings inside the
    /// loop; never store a pointer past it, and never carry one across a coroutine yield.
    public interface IMonoObjects : IDisposable
    {
        int Count { get; }

        IntPtr this[int index] { get; }

        /// Null when the enumeration succeeded; a reason when it did not (an empty set and a failed
        /// one are not the same thing, and mistaking one for the other hides real breakage).
        string Error { get; }
    }

    /// Bumped when the contract changes shape. The host refuses a plugin built against a different
    /// major so a stale plugin fails with a clear message instead of a MissingMethodException.
    public static class PluginSdk
    {
        public const int Version = 1;
    }
}
#endif
