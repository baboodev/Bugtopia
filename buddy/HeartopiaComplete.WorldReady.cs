using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Monotonic ladder describing how far the game has got. Compare with >= — new rungs may be
    // inserted later, so never switch on exact equality for "at least this far" checks.
    internal enum WorldStage
    {
        // Nothing known: the game's Mono side has not been confirmed live yet.
        None = 0,
        // Mono is live (some image resolved) but the level FSM has not been read yet — either
        // GameWorld is still unresolved, or we are on the legacy event-only path.
        MonoLive,
        // A level transition is in flight (GameWorld.isSwitching). Covers login->town, town->home,
        // home->town and every scene transfer, INCLUDING the ones that show no loading splash.
        Loading,
        // The level's own LoadTask has FINISHED (state == Loaded) but the transition wrapper is
        // still running its fade-out/close — so the world's modules are constructed while the
        // loading splash is still covering the screen. Measured window: ~1-2 s, plus the 1.5 s grace
        // before WorldReady. This is the rung for work that is safe once the world exists and is
        // better done where the player cannot see it hitch. Playable levels only: the login level
        // never gets here (see CurrentWorldStage).
        LevelBuilt,
        // A PLAYABLE level finished loading and no transition is running, but it has not had its
        // settle grace yet. The login level never reaches this rung — it stays at MonoLive — so
        // `>= LevelLoaded` already means "a real world is loaded".
        LevelLoaded,
        // A playable world (not login) has been loaded and has had WorldReadyGraceSeconds to settle.
        WorldReady,
        // ...and it has since had time to actually quieten down: a local player has existed for
        // WorldSettleSeconds (or the hard fallback elapsed). This is the rung for genuinely HEAVY
        // work — anything that triggers mass asset loading — which is why Game LOD waits for it.
        WorldSettled
    }

    // Central "the world is up" gate.
    //
    // PRIMARY source is the game's own level state machine, polled via GameWorld (see
    // HeartopiaComplete.GameWorldProbe.cs). The loading-screen EVENTS below are kept only as a
    // fallback for the window before GameWorld resolves (~T+55..60s on a cold start) and for builds
    // where it cannot be resolved at all. Phase 3 removes them entirely — they are the one hook
    // that must install pre-world, and installing them is what aborts the process
    // (see project memory: eventhook-preworld-inflate-abort).
    //
    // Why the FSM beats the events, both measured live:
    //   * The events cannot tell the LOGIN level from a real world — both emit LoadingOpened/Closed
    //     — so the gate used to report IsWorldReady=Y while sitting on the login screen.
    //     GameWorld reports levelType 0 there (1 = town, 3 = micro-home).
    //   * Homeland enter/exit emits NO loading events at all. The gate stayed "ready" through the
    //     whole tear-down + 53s rebuild and never bumped the epoch, so per-world caches keyed on it
    //     were never invalidated. The FSM shows the full Loaded->UnLoading->Loading->Loaded arc.
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
        // Grace after the splash disappears before callbacks run. The loading screen closes a beat
        // before the world settles; a short pause keeps resolves off the busiest frames without
        // being long enough for the user to notice a feature "not working yet".
        private const float WorldReadyGraceSeconds = 1.5f;

        // Fallback path: how long a local player must have existed before we call it a world.
        private const float WorldReadyFallbackSettleSeconds = 5f;

        // WorldSettled: how long a local player must have been around after the world came up, and
        // the hard cap that declares it settled anyway if the player never resolves. These are the
        // values Game LOD used privately before this rung existed — kept identical so its behaviour
        // does not change with the move.
        private const float WorldSettleSeconds = 8f;
        private const float WorldSettleFallbackSeconds = 45f;

        // Per-epoch retry for callbacks that report "not satisfied yet" (an image can still be
        // streaming in right after the splash). 1 s x 30 = ~30 s of retries per world load, then
        // the callback sleeps until the next one.
        private const float WorldReadyCallbackRetrySeconds = 1f;
        private const int WorldReadyCallbackMaxAttemptsPerEpoch = 30;


        // GameLevelState.Loaded — see ilspy-dumps/XDTBaseService/XDTGame.Core/GameLevelState.cs
        // { None, Initilizing, Initilized, Loading, Loaded, UnLoading, Unloaded, Destroying, Destroyed }.
        private const int GameLevelStateLoaded = 4;

        // Level types observed live (GameWorld.EnterLevel call sites corroborate: MonoApp/LoginSystem
        // pass 0, LoginPanel passes 1, BuildModule passes 3). Anything > 0 is a playable world.
        internal const int WorldLevelTypeLogin = 0;
        internal const int WorldLevelTypeTown = 1;      // StarTown, scene id 1
        internal const int WorldLevelTypeMicroHome = 3; // homeland, scene id 4

        private sealed class WorldReadyCallbackEntry
        {
            public string Name;
            // Returns true when the work is done for this world; false = retry shortly.
            public Func<bool> Attempt;
            public WorldStage MinStage;
            public int LastEpochDone;
            public int AttemptsThisEpoch;
            public int AttemptsEpoch;
            public float NextAttemptAt;
            public bool GaveUpLogged;
        }

        private readonly List<WorldReadyCallbackEntry> worldReadyCallbacks = new List<WorldReadyCallbackEntry>();
        private readonly List<Action> worldLoadingStartedCallbacks = new List<Action>();

        // Time the current world's "ready" signal arrived (FSM said loaded, or the fallback settle).
        // 0 while there is no live world.
        private float worldReadySignalAt = 0f;
        private int worldReadyEpoch = 0;
        private float worldFallbackPlayerSeenAt = 0f;
        // First moment a local player was seen in THIS world; reset whenever the gate closes.
        private float worldSettlePlayerSeenAt = 0f;

        // ---- Level-FSM view, fed by ProcessGameWorldProbeOnUpdate. worldProbeValid latches true on
        // the first good read and back to false if the probe gives up, so the player-presence
        // fallback can take back over rather than the gate freezing on a stale picture. ----
        private bool worldProbeValid;
        private bool worldProbeLevelLoaded;
        private bool worldProbeInTransition;
        private int worldProbeLevelType = -1;
        private int worldProbeSceneId = -1;

        // Level identity of the world currently loaded. -1 until the probe has read it.
        internal int CurrentLevelType => this.worldProbeLevelType;
        internal int CurrentSceneId => this.worldProbeSceneId;

        // A playable world, as opposed to the login level (or nothing loaded at all).
        internal bool IsInPlayableWorld => this.worldProbeValid && this.worldProbeLevelType > WorldLevelTypeLogin;

        // Named level checks. All false while the probe has not read a level yet, so they are safe
        // to use as "definitely in X" tests and never as "definitely NOT in X".
        internal bool IsInLoginLevel => this.worldProbeValid && this.worldProbeLevelType == WorldLevelTypeLogin;
        internal bool IsInTownLevel => this.worldProbeValid && this.worldProbeLevelType == WorldLevelTypeTown;
        internal bool IsInMicroHomeLevel => this.worldProbeValid && this.worldProbeLevelType == WorldLevelTypeMicroHome;

        // True once the world has had time to quieten down — the rung heavy work waits for.
        private bool IsWorldSettledNow
        {
            get
            {
                if (!this.IsWorldReady)
                {
                    return false;
                }

                float now = Time.unscaledTime;
                return (this.worldSettlePlayerSeenAt > 0f && now - this.worldSettlePlayerSeenAt >= WorldSettleSeconds)
                    || (now - this.worldReadySignalAt >= WorldSettleFallbackSeconds);
            }
        }

        // How far along the game is right now. See the WorldStage doc comment.
        internal WorldStage CurrentWorldStage
        {
            get
            {
                if (!AuraMonoGameDataLive)
                {
                    return WorldStage.None;
                }

                if (!this.worldProbeValid)
                {
                    // Fallback path (GameWorld unresolvable): the player-presence heuristic cannot
                    // describe the level, so claim no more than the gate itself has established.
                    if (!this.IsWorldReady)
                    {
                        return WorldStage.MonoLive;
                    }

                    return this.IsWorldSettledNow ? WorldStage.WorldSettled : WorldStage.WorldReady;
                }

                // A transition is running. If the LEVEL itself has already finished loading, we are
                // in the fade-out window: modules constructed, splash still covering the screen.
                // That is LevelBuilt — the place to do work that would otherwise hitch in view.
                // Login excluded here for the same reason it is excluded below.
                if (this.worldProbeInTransition)
                {
                    return this.worldProbeLevelLoaded && this.worldProbeLevelType > WorldLevelTypeLogin
                        ? WorldStage.LevelBuilt
                        : WorldStage.Loading;
                }

                // The login level sits at MonoLive, NOT LevelLoaded, even though it is genuinely
                // "loaded and not transitioning". Measured 2026-07-26: on the login screen every
                // probed game type resolves perfectly (GameWorld, LocalTextureCacheService,
                // PhotoFrameComponent, RenderLoadConfig, DownLoadTexture2dAdvancedLoader — all OK
                // across five images). So a caller cannot tell login from a real world by asking
                // whether types resolve; it only finds out later, when a static-field read or an
                // invoke against an unconstructed service AVs. That is a silent failure, so the
                // ladder encodes the distinction itself and `>= LevelLoaded` is safe by
                // construction. Lobby/menu features that legitimately run there (join friend, join
                // town) use `>= MonoLive` plus IsInLoginLevel, which says what it means.
                if (!this.worldProbeLevelLoaded || this.worldProbeLevelType <= WorldLevelTypeLogin)
                {
                    return WorldStage.MonoLive;
                }

                if (!this.IsWorldReady)
                {
                    return WorldStage.LevelLoaded;
                }

                return this.IsWorldSettledNow ? WorldStage.WorldSettled : WorldStage.WorldReady;
            }
        }

        // Increments once per world load. Caches keyed on this are dropped when the world changes.
        internal int WorldReadyEpoch => this.worldReadyEpoch;

        // True while a level transition is in flight. Note this is BROADER than the old
        // splash-visible meaning it replaced: homeland swaps run a full transition with no splash
        // at all, and this now covers them.
        internal bool IsWorldLoadingScreenVisible => this.worldProbeValid && this.worldProbeInTransition;

        // The gate itself: a live PLAYABLE world that has had WorldReadyGraceSeconds to settle.
        internal bool IsWorldReady
        {
            get
            {
                if (this.worldReadySignalAt <= 0f
                    || Time.unscaledTime - this.worldReadySignalAt < WorldReadyGraceSeconds)
                {
                    return false;
                }

                if (this.worldProbeValid)
                {
                    // Authoritative once the FSM is readable: a loaded, non-login level with no
                    // transition running. This is what closes the gate during a homeland swap,
                    // which produces no loading events whatsoever.
                    return this.worldProbeLevelLoaded
                        && !this.worldProbeInTransition
                        && this.worldProbeLevelType > WorldLevelTypeLogin;
                }

                // Probe not up (yet): the standing signal can only have come from the
                // player-presence fallback, which already required a live world.
                return true;
            }
        }

        // Stronger sibling of AuraMonoStaticFieldReadsAllowed() for game-data reads driven by the
        // UI instead of by gameplay.
        //
        // That latch only proves the game's Mono side is live (some image resolved) — it says
        // NOTHING about a world existing. The UGUI shell can be opened on the load menu, and its
        // build/refresh paths then walk modules that are not constructed yet: that is exactly how
        // the snow tab's bag counter reached BackPackSystem and took the process down with an
        // uncatchable AV (WER coreclr_25388; the raw-read guard TryGetAuraMonoStaticFieldVtable is
        // the safety net, this is the "don't even ask" gate).
        //
        // Use it for anything a panel build or a per-frame UI refresh triggers. Do NOT use it for
        // lobby/menu features that legitimately work before a world exists (join friend, join town)
        // or for the loading pipeline itself.
        internal bool IsGameDataQueryable => AuraMonoStaticFieldReadsAllowed() && this.IsWorldReady;

        // Register work that must run once per world load — warmups, Mono class/method resolution,
        // NativeDetour installs. `attempt` returns true when it is done for this world and false to
        // be retried a second later (bounded, see WorldReadyCallbackMaxAttemptsPerEpoch).
        // Registration is cheap and safe from anywhere; the callback only ever runs on the main
        // thread from OnUpdate, and only with a live world.
        internal void RegisterWorldReadyCallback(string name, Func<bool> attempt)
        {
            this.RegisterWorldStageCallback(name, WorldStage.WorldReady, attempt);
        }

        // Same contract, but the caller picks how far the game must be. Use a lower rung only when
        // the work genuinely does not need a playable world — most things want WorldReady.
        internal void RegisterWorldStageCallback(string name, WorldStage minStage, Func<bool> attempt)
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
                MinStage = minStage,
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
            this.EnsureBuiltInWorldReadyWarmups();
            this.UpdateWorldReadyFallback(now);
            this.UpdateWorldSettleTracking(now);
            this.DrainWorldReadyCallbacks(now);

            // Phase-1 read-only observation of the game's own level FSM, for comparison against the
            // gate above (HeartopiaComplete.GameWorldProbe.cs). Self-gated on its Logging toggle and
            // feeds nothing — remove this call, and the gate behaves exactly as before.
            this.ProcessGameWorldProbeOnUpdate(now);
        }

        private bool worldReadyBuiltInWarmupsRegistered;

        // Registered ONCE, from the first tick. Every feature whose resolve/install used to poll on
        // its own blind timer now (a) refuses to run before IsWorldReady and (b) gets its timer
        // re-armed here the moment a world comes up — so nothing waits out a 3-30 s cooldown after
        // the splash clears, and nothing polls while there is no world to resolve against.
        //
        // Registering centrally (instead of per feature, per frame) keeps the hot paths
        // allocation-free: a lambda handed to RegisterWorldReadyCallback every frame would be a new
        // delegate object every frame, dedup by name notwithstanding.
        private void EnsureBuiltInWorldReadyWarmups()
        {
            if (this.worldReadyBuiltInWarmupsRegistered)
            {
                return;
            }

            this.worldReadyBuiltInWarmupsRegistered = true;
            this.RegisterWorldReadyCallback("BuiltInWarmups", this.OnWorldReadyRearmWarmups);
        }

        // Re-arm every deferred warmup for the new world — STAGGERED, not all on one frame.
        //
        // This used to set all 18 timers to -999f ("attempt on the next tick"), so every feature did
        // its Mono type resolution / method compilation / detour install in the SAME frame the gate
        // opened. Individually each is smallish; eighteen at once is a visible micro-freeze, landing
        // exactly when the loading splash clears and the player regains control — the worst possible
        // moment for it. Nothing here is urgent to a tenth of a second, so they are spread instead.
        //
        // Order matters a little: the things a player can notice immediately (input bridge, UI
        // timings, hotkey guards) go first; background resolvers that nobody is waiting on go last.
        private const float WorldReadyWarmupStaggerSeconds = 0.12f;

        private float WorldReadyWarmupSlot(int index)
        {
            // Slot 0 keeps the old "next tick" behaviour; later slots trail behind it.
            return index <= 0 ? -999f : Time.unscaledTime + (index * WorldReadyWarmupStaggerSeconds);
        }

        private bool OnWorldReadyRearmWarmups()
        {
            int slot = 0;

            // Event-hook detours first: a fresh world can bring images whose event types were
            // unresolvable before, and everything else is happier once events are flowing.
            this.gameEventHookNextInstallAttemptAt = this.WorldReadyWarmupSlot(slot++);

            // Player-facing: a delay here would actually be felt.
            this.movementInputRetryAt = this.WorldReadyWarmupSlot(slot++);
            this.instrumentHotkeyGuardNextResolveAt = this.WorldReadyWarmupSlot(slot++);
            this.gameUiTimingsNextApplyAt = this.WorldReadyWarmupSlot(slot++);
            this.instantTeleportNextAttemptAt = this.WorldReadyWarmupSlot(slot++);

            // Unconditional warmups.
            this.homelandFarmNextSceneLoadFinishedProbeAt = this.WorldReadyWarmupSlot(slot++);
            this.homelandFarmNextRuntimeResolveAt = this.WorldReadyWarmupSlot(slot++);
            this.spawnVehicleNextHookAttemptAt = this.WorldReadyWarmupSlot(slot++);

            // Behind a persisted toggle — nobody is watching these land.
            this.privacyBlockNextHookAttemptAt = this.WorldReadyWarmupSlot(slot++);
            this.seaCleanBannerNextAttemptAt = this.WorldReadyWarmupSlot(slot++);
            this.swimVerticalNextAttemptAt = this.WorldReadyWarmupSlot(slot++);
            this.chatForceTranslateNextResolveAt = this.WorldReadyWarmupSlot(slot++);
            this.postcardNextMailIdResolveAt = this.WorldReadyWarmupSlot(slot++);
            this.nextBubbleFeaturePatchAttemptAt = this.WorldReadyWarmupSlot(slot++);
            this.bubbleSpawnNextInstallAttemptAt = this.WorldReadyWarmupSlot(slot++);
            this.bubbleCreateNextInstallAttemptAt = this.WorldReadyWarmupSlot(slot++);
            this.avatarPatchNextTryAt = this.WorldReadyWarmupSlot(slot++);
            this.sanrioNextConfigWalkAt = this.WorldReadyWarmupSlot(slot++);
            return true;
        }

        // NOTE (phase 3, 2026-07-26): the LoadingOpenedEvent / LoadingClosedEvent hooks that used to
        // live here are GONE, along with EnsureWorldLoadingHooks and their two handlers.
        //
        // They were the only hooks in the mod that had to install before a world existed — the gate
        // could not wait for the signal that tells it a world arrived — and installing them means
        // inflating EventCenter.DispatchEvent<T>, which on half-loaded Mono images makes the runtime
        // g_assert and abort() the process. That was an intermittent, uncatchable startup crash
        // (WER xdt.exe.34488, plus 7988/30332 in the same family). Removing them removes the entire
        // pre-world generic-inflate surface rather than trying to make it safe.
        //
        // Nothing regressed by dropping them: the level FSM is strictly more informative. It also
        // reports the LOGIN level (which the events could not distinguish from a real world) and
        // homeland swaps (which emit no loading events at all).

        // Fed by the GameWorld probe on every successful read (~4 Hz). This is what makes the gate
        // track homeland swaps and tell the login level apart from a real world.
        private void ApplyGameWorldProbeSample(int state, bool switching, int sceneId, int levelType)
        {
            bool levelChanged = sceneId != this.worldProbeSceneId || levelType != this.worldProbeLevelType;

            this.worldProbeValid = true;
            this.worldProbeLevelLoaded = state == GameLevelStateLoaded;
            this.worldProbeInTransition = switching;
            this.worldProbeSceneId = sceneId;
            this.worldProbeLevelType = levelType;

            bool worldUp = this.worldProbeLevelLoaded && !switching && levelType > WorldLevelTypeLogin;
            if (worldUp)
            {
                // MarkWorldReadySignal is a no-op while a signal is already standing, so a steady
                // world does not re-arm the grace or churn the epoch every 250 ms.
                this.MarkWorldReadySignal("GameWorld FSM (levelType " + levelType + ", scene " + sceneId + ")");
                return;
            }

            // Not a live playable world: drop the standing signal so the NEXT arrival counts as a
            // fresh world (new epoch -> per-world caches invalidated, callbacks re-run). The
            // `> 0f` test alone is the right guard — at startup nothing has marked ready yet, and
            // if the EVENT path had marked ready (including the login-level false positive) then
            // clearing it here is exactly the correction we want.
            if (this.worldReadySignalAt > 0f)
            {
                this.CloseWorldGate(switching
                    ? ("level transition started" + (levelChanged ? " -> levelType " + levelType : string.Empty))
                    : "level no longer loaded");
            }
        }

        // Notes when a local player first showed up in the current world — the WorldSettled input.
        // Cheap: one position read per frame only while the gate is open and not settled yet.
        private void UpdateWorldSettleTracking(float now)
        {
            if (this.worldReadySignalAt <= 0f || this.worldSettlePlayerSeenAt > 0f)
            {
                return;
            }

            try
            {
                if (this.TryGetLocalPlayerPosition(out Vector3 playerPos) && playerPos != Vector3.zero)
                {
                    this.worldSettlePlayerSeenAt = now;
                }
            }
            catch { }
        }

        // Single place that tears the gate down, whichever source noticed. Fires the
        // loading-started callbacks (hand settings back to the game, drop per-world caches).
        private void CloseWorldGate(string reason)
        {
            this.worldReadySignalAt = 0f;
            this.worldFallbackPlayerSeenAt = 0f;
            this.worldSettlePlayerSeenAt = 0f;
            ModLogger.Msg("[WorldReady] gate closed — " + reason + ".");

            for (int i = 0; i < this.worldLoadingStartedCallbacks.Count; i++)
            {
                try { this.worldLoadingStartedCallbacks[i](); }
                catch (Exception ex)
                {
                    ModLogger.Msg("[WorldReady] loading-started callback failed: " + ex.Message);
                }
            }
        }

        // Called by the probe when it gives up, so the gate falls back to the event/player path
        // instead of freezing on the last picture the FSM gave it.
        private void OnGameWorldProbeUnavailable()
        {
            if (!this.worldProbeValid)
            {
                return;
            }

            this.worldProbeValid = false;
            this.worldProbeLevelLoaded = false;
            this.worldProbeInTransition = false;
            ModLogger.Msg("[WorldReady] GameWorld probe unavailable — falling back to loading events + player presence.");
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

        // Last-resort safety net for builds where GameWorld cannot be resolved at all (a future
        // update renames or moves it). Requires the game's Mono side to be confirmed live (an image
        // resolved) AND a local player to have existed for a few seconds — the same pair GameLod
        // used before this gate existed. This is now the ONLY fallback: the loading events are gone.
        private void UpdateWorldReadyFallback(float now)
        {
            // The FSM knows better and knows it sooner; two sources marking "ready" would race, and
            // the player-presence heuristic cannot tell a half-loaded level from a settled one.
            if (this.worldProbeValid)
            {
                return;
            }

            if (this.worldReadySignalAt > 0f)
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
                this.MarkWorldReadySignal("player-present fallback (GameWorld FSM unavailable)");
            }
        }

        private void DrainWorldReadyCallbacks(float now)
        {
            if (this.worldReadyCallbacks.Count == 0)
            {
                return;
            }

            WorldStage stage = this.CurrentWorldStage;
            int epoch = this.worldReadyEpoch;
            for (int i = 0; i < this.worldReadyCallbacks.Count; i++)
            {
                WorldReadyCallbackEntry entry = this.worldReadyCallbacks[i];
                if (stage < entry.MinStage || entry.LastEpochDone == epoch || now < entry.NextAttemptAt)
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
