using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Central "the world is up" gate, driven by the game's own loading-screen events.
    //
    // XDTGameSystem.UI.LoadingOpenedEvent / LoadingClosedEvent are empty structs (Size = 1, so 0
    // payload bytes) dispatched globally around every world load — login, town/homeland change,
    // scene transfer. LoadingClosedEvent is the earliest RELIABLE "there is a live world" signal:
    // the game's Mono images are loaded, its services are constructed and the local player exists.
    //
    // Before this gate every feature polled on its own timer (VehicleBypass 3 s, WarehouseBypass /
    // StrangerChat 5 s, GameLod 30 s, ...) starting from the very first OnUpdate — i.e. on the
    // login screen, where nothing resolves and raw static-field reads AV uncatchably (see
    // memory: AuraMono static-field login crash). Features register here instead and are called
    // back ONCE per world load, on the Unity main thread, with a bounded retry for the resolves
    // that still need a moment after the splash disappears.
    //
    // Fallback: if the loading hooks never install (slot pool exhausted, event type renamed by a
    // game update), the gate falls back to "game Mono side live + a local player has existed for
    // a few seconds". Nothing ever hangs waiting for an event that will not arrive.
    public partial class HeartopiaComplete
    {
        internal const string WorldLoadingOpenedEventName = "XDTGameSystem.UI.LoadingOpenedEvent";
        internal const string WorldLoadingClosedEventName = "XDTGameSystem.UI.LoadingClosedEvent";

        // Grace after the splash disappears before callbacks run. The loading screen closes a beat
        // before the world settles; a short pause keeps resolves off the busiest frames without
        // being long enough for the user to notice a feature "not working yet".
        private const float WorldReadyGraceSeconds = 1.5f;

        // Fallback path: how long a local player must have existed before we call it a world.
        private const float WorldReadyFallbackSettleSeconds = 5f;

        // Per-epoch retry for callbacks that report "not satisfied yet" (an image can still be
        // streaming in right after the splash). 1 s x 30 = ~30 s of retries per world load, then
        // the callback sleeps until the next one.
        private const float WorldReadyCallbackRetrySeconds = 1f;
        private const int WorldReadyCallbackMaxAttemptsPerEpoch = 30;

        private const float WorldReadyHookRetrySeconds = 30f;

        private sealed class WorldReadyCallbackEntry
        {
            public string Name;
            // Returns true when the work is done for this world; false = retry shortly.
            public Func<bool> Attempt;
            public int LastEpochDone;
            public int AttemptsThisEpoch;
            public int AttemptsEpoch;
            public float NextAttemptAt;
            public bool GaveUpLogged;
        }

        private readonly List<WorldReadyCallbackEntry> worldReadyCallbacks = new List<WorldReadyCallbackEntry>();
        private readonly List<Action> worldLoadingStartedCallbacks = new List<Action>();

        private bool worldLoadingHooksRegistered;
        private float worldNextLoadingHookAttemptAt = 0f;
        private bool worldLoadingEventsSeen;
        private bool worldLoadingScreenVisible;

        // Time the current world's "ready" signal arrived (loading closed, or the fallback settle).
        // 0 while there is no live world.
        private float worldReadySignalAt = 0f;
        private int worldReadyEpoch = 0;
        private float worldFallbackPlayerSeenAt = 0f;

        // Increments once per world load. Caches keyed on this are dropped when the world changes.
        internal int WorldReadyEpoch => this.worldReadyEpoch;

        // True while the game's loading splash is up (only meaningful once the hooks have fired).
        internal bool IsWorldLoadingScreenVisible => this.worldLoadingScreenVisible;

        // The gate itself: a live world that has had WorldReadyGraceSeconds to settle.
        internal bool IsWorldReady
        {
            get
            {
                return !this.worldLoadingScreenVisible
                    && this.worldReadySignalAt > 0f
                    && Time.unscaledTime - this.worldReadySignalAt >= WorldReadyGraceSeconds;
            }
        }

        // Register work that must run once per world load — warmups, Mono class/method resolution,
        // NativeDetour installs. `attempt` returns true when it is done for this world and false to
        // be retried a second later (bounded, see WorldReadyCallbackMaxAttemptsPerEpoch).
        // Registration is cheap and safe from anywhere; the callback only ever runs on the main
        // thread from OnUpdate, and only with a live world.
        internal void RegisterWorldReadyCallback(string name, Func<bool> attempt)
        {
            if (attempt == null)
            {
                return;
            }

            for (int i = 0; i < this.worldReadyCallbacks.Count; i++)
            {
                if (string.Equals(this.worldReadyCallbacks[i].Name, name, StringComparison.Ordinal))
                {
                    return; // idempotent — same feature re-registering after a toggle flip
                }
            }

            this.worldReadyCallbacks.Add(new WorldReadyCallbackEntry
            {
                Name = name ?? "<unnamed>",
                Attempt = attempt,
                LastEpochDone = -1,
                AttemptsEpoch = -1
            });
        }

        // Register work that must run when a world load STARTS (splash appears): hand settings back
        // to the game, drop per-world caches. Never fires if the loading hooks never install.
        internal void RegisterWorldLoadingStartedCallback(Action callback)
        {
            if (callback != null)
            {
                this.worldLoadingStartedCallbacks.Add(callback);
            }
        }

        // Force the pending callbacks of the CURRENT world to run again (used when a feature toggle
        // flips on: its install has to happen now, not at the next world load).
        internal void ResetWorldReadyCallback(string name)
        {
            for (int i = 0; i < this.worldReadyCallbacks.Count; i++)
            {
                WorldReadyCallbackEntry entry = this.worldReadyCallbacks[i];
                if (!string.Equals(entry.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                entry.LastEpochDone = -1;
                entry.AttemptsThisEpoch = 0;
                entry.AttemptsEpoch = this.worldReadyEpoch;
                entry.NextAttemptAt = 0f;
                entry.GaveUpLogged = false;
                return;
            }
        }

        // Called early in OnUpdate, before any feature tick.
        private void ProcessWorldReadyOnUpdate()
        {
            float now = Time.unscaledTime;
            this.EnsureWorldLoadingHooks(now);
            this.UpdateWorldReadyFallback(now);
            this.DrainWorldReadyCallbacks(now);
        }

        // The loading hooks themselves cannot wait for the world — they are what tells us the world
        // arrived. RegisterGameEventHook is metadata-only (the EventHook engine installs the native
        // detour lazily once Mono is up), so one call is normally enough; the retry only covers the
        // "registration refused" cases (slot pool exhausted).
        private void EnsureWorldLoadingHooks(float now)
        {
            if (this.worldLoadingHooksRegistered || now < this.worldNextLoadingHookAttemptAt)
            {
                return;
            }

            this.worldNextLoadingHookAttemptAt = now + WorldReadyHookRetrySeconds;
            try
            {
                bool opened = this.RegisterGameEventHook(WorldLoadingOpenedEventName, 0,
                    new Action<GameEventSnapshot>(this.OnWorldLoadingOpenedEvent));
                bool closed = this.RegisterGameEventHook(WorldLoadingClosedEventName, 0,
                    new Action<GameEventSnapshot>(this.OnWorldLoadingClosedEvent));
                this.worldLoadingHooksRegistered = opened && closed;
                if (this.worldLoadingHooksRegistered)
                {
                    ModLogger.Msg("[WorldReady] loading-screen hooks registered (LoadingOpened/LoadingClosed)"
                        + " — warmups and hook installs now wait for the world.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[WorldReady] loading hook registration failed: " + ex.Message);
            }
        }

        private void OnWorldLoadingOpenedEvent(GameEventSnapshot snapshot)
        {
            this.worldLoadingEventsSeen = true;
            this.worldLoadingScreenVisible = true;
            this.worldReadySignalAt = 0f;
            this.worldFallbackPlayerSeenAt = 0f;
            ModLogger.Msg("[WorldReady] loading screen opened — world gate closed.");

            for (int i = 0; i < this.worldLoadingStartedCallbacks.Count; i++)
            {
                try { this.worldLoadingStartedCallbacks[i](); }
                catch (Exception ex)
                {
                    ModLogger.Msg("[WorldReady] loading-started callback failed: " + ex.Message);
                }
            }
        }

        private void OnWorldLoadingClosedEvent(GameEventSnapshot snapshot)
        {
            this.worldLoadingEventsSeen = true;
            this.worldLoadingScreenVisible = false;
            this.MarkWorldReadySignal("LoadingClosedEvent");
        }

        private void MarkWorldReadySignal(string source)
        {
            if (this.worldReadySignalAt > 0f)
            {
                return; // already have a live world; don't restart the grace or bump the epoch
            }

            this.worldReadySignalAt = Time.unscaledTime;
            this.worldReadyEpoch++;
            ModLogger.Msg("[WorldReady] world " + this.worldReadyEpoch + " ready via " + source
                + " — deferred warmups/hooks run in " + WorldReadyGraceSeconds.ToString("0.#") + "s.");
        }

        // Safety net for builds where the loading events never reach us. Requires the game's Mono
        // side to be confirmed live (an image resolved) AND a local player to have existed for a
        // few seconds — the same pair GameLod used before this gate existed.
        private void UpdateWorldReadyFallback(float now)
        {
            if (this.worldReadySignalAt > 0f || this.worldLoadingScreenVisible)
            {
                return;
            }

            if (!AuraMonoGameDataLive)
            {
                this.worldFallbackPlayerSeenAt = 0f;
                return;
            }

            bool playerAlive = false;
            try
            {
                playerAlive = this.TryGetLocalPlayerPosition(out Vector3 playerPos) && playerPos != Vector3.zero;
            }
            catch { }

            if (!playerAlive)
            {
                this.worldFallbackPlayerSeenAt = 0f;
                return;
            }

            if (this.worldFallbackPlayerSeenAt <= 0f)
            {
                this.worldFallbackPlayerSeenAt = now;
                return;
            }

            if (now - this.worldFallbackPlayerSeenAt >= WorldReadyFallbackSettleSeconds)
            {
                this.MarkWorldReadySignal(this.worldLoadingEventsSeen
                    ? "player-present fallback (loading events seen but no close)"
                    : "player-present fallback (no loading events)");
            }
        }

        private void DrainWorldReadyCallbacks(float now)
        {
            if (this.worldReadyCallbacks.Count == 0 || !this.IsWorldReady)
            {
                return;
            }

            int epoch = this.worldReadyEpoch;
            for (int i = 0; i < this.worldReadyCallbacks.Count; i++)
            {
                WorldReadyCallbackEntry entry = this.worldReadyCallbacks[i];
                if (entry.LastEpochDone == epoch || now < entry.NextAttemptAt)
                {
                    continue;
                }

                if (entry.AttemptsEpoch != epoch)
                {
                    entry.AttemptsEpoch = epoch;
                    entry.AttemptsThisEpoch = 0;
                    entry.GaveUpLogged = false;
                }

                if (entry.AttemptsThisEpoch >= WorldReadyCallbackMaxAttemptsPerEpoch)
                {
                    if (!entry.GaveUpLogged)
                    {
                        entry.GaveUpLogged = true;
                        ModLogger.Msg("[WorldReady] " + entry.Name + " still unresolved after "
                            + WorldReadyCallbackMaxAttemptsPerEpoch + " attempts — sleeping until the next world load.");
                    }
                    continue;
                }

                entry.AttemptsThisEpoch++;
                entry.NextAttemptAt = now + WorldReadyCallbackRetrySeconds;

                bool done;
                try
                {
                    done = entry.Attempt();
                }
                catch (Exception ex)
                {
                    done = false;
                    ModLogger.Msg("[WorldReady] " + entry.Name + " threw: " + ex.GetType().Name + " - " + ex.Message);
                }

                if (done)
                {
                    entry.LastEpochDone = epoch;
                }
            }
        }
    }
}
