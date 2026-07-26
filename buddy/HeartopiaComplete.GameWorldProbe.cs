using System;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // GameWorld level-FSM probe — PHASE 1: READ-ONLY OBSERVATION, no behaviour change.
    //
    // The world-ready gate currently rides on two EventCenter events (LoadingOpenedEvent /
    // LoadingClosedEvent). Those are the one hook path that must install BEFORE a world exists,
    // and installing them means inflating EventCenter.DispatchEvent<T> over an event struct whose
    // metadata may not be loaded yet — which makes Mono g_assert and abort() the process
    // (0xC0000409 with RCX=7 = FAST_FAIL_FATAL_APP_EXIT; WER xdt.exe.34488, and the same class
    // twice before). See project memory: eventhook-preworld-inflate-abort.
    //
    // The game exposes the exact same information as PLAIN PUBLIC STATIC PROPERTIES on
    // XDTGame.Core.GameWorld (image XDTBaseService, ilspy-dumps/XDTBaseService/XDTGame.Core/GameWorld.cs):
    //
    //     public static GameLevelState state       => _currentLevel?.state ?? GameLevelState.None;
    //     public static bool           isSwitching => _transition != null;
    //     public static IGameLevel     gameLevel   => _currentLevel?.Instance;   // + GetSceneId/GetLevelType
    //
    // Reading those needs NO detour, NO generic inflation and NO raw static-field read — they are
    // ordinary property getters invoked through mono_runtime_invoke, so the runtime handles class
    // init itself. They are also null-safe by construction (`?.` + `?? None`), so before a world
    // exists they simply answer None/false instead of faulting.
    //
    // THIS FILE ONLY WATCHES AND LOGS. Nothing here feeds the gate yet. The point of phase 1 is to
    // run a session with `Settings -> Logging -> World Stage` on and confirm in the log that the
    // probe's transitions line up with the event-driven gate's own transitions, BEFORE anything
    // starts depending on it. Phase 2 makes the ladder authoritative; phase 3 deletes the event
    // transport and with it the crash.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        internal static bool MasterLogWorldStage = false;

        // GameLevelState : byte  (ilspy-dumps/XDTBaseService/XDTGame.Core/GameLevelState.cs)
        private static readonly string[] GameWorldLevelStateNames =
        {
            "None", "Initilizing", "Initilized", "Loading", "Loaded", "UnLoading", "Unloaded", "Destroying", "Destroyed"
        };

        // Level ids seen at GameWorld.EnterLevel call sites: MonoApp/LoginSystem/SettingPanel use 0,
        // LoginPanel/LanguageSwitchPanel use 1, BuildModule uses 3.
        private static readonly string[] GameWorldImageNames = { "XDTBaseService", "XDTBaseService.dll" };

        private const float GameWorldProbeIntervalSeconds = 0.25f;
        private const int GameWorldProbeMaxConsecutiveFailures = 5;

        private IntPtr gameWorldProbeClass = IntPtr.Zero;
        private IntPtr gameWorldProbeStateMethod = IntPtr.Zero;
        private IntPtr gameWorldProbeIsSwitchingMethod = IntPtr.Zero;
        private IntPtr gameWorldProbeGameLevelMethod = IntPtr.Zero;
        private bool gameWorldProbeResolved;
        private bool gameWorldProbeResolveLogged;
        // Time.unscaledTime at which the resolve actually succeeded — recorded even while logging is
        // off, so the eventual log line reports when it REALLY happened rather than when the user
        // happened to tick the toggle.
        private float gameWorldProbeResolvedAt = -1f;
        private bool gameWorldProbeDisabled;
        private int gameWorldProbeFailures;
        private float nextGameWorldProbeAt;

        // Last observed tuple — we only log on CHANGE, so a quiet session stays quiet.
        private int gameWorldProbeLastState = -1;
        private int gameWorldProbeLastSwitching = -1;
        private int gameWorldProbeLastSceneId = int.MinValue;
        private int gameWorldProbeLastLevelType = int.MinValue;
        private bool gameWorldProbeLastGateReady;
        private int gameWorldProbeLastGateEpoch = -1;

        // Called from ProcessWorldReadyOnUpdate (already early in OnUpdate, main thread).
        private void ProcessGameWorldProbeOnUpdate(float now)
        {
            // The probe itself runs unconditionally; MasterLogWorldStage gates only the OUTPUT.
            // It used to be gated as a whole, which made the first measurement useless: the flag is
            // not persisted, so a fresh process starts with it off and the probe only woke up when
            // the user opened the menu and ticked it — long after the first world had loaded. The
            // open question (does GameWorld resolve before the FIRST world load, from a cold start?)
            // can only be answered by probing from frame one. Cost is 2-3 invokes per 0.25 s.
            if (this.gameWorldProbeDisabled || now < this.nextGameWorldProbeAt)
            {
                return;
            }

            this.nextGameWorldProbeAt = now + GameWorldProbeIntervalSeconds;

            // Same precondition the existing gate's fallback uses: the game's Mono side is
            // confirmed live. Deliberately NOT gated on IsWorldReady — observing the pre-world
            // stages is the whole point — and deliberately NOT on AuraMonoStaticFieldReadsAllowed,
            // because this invokes getters rather than reading statics raw.
            if (!AuraMonoGameDataLive)
            {
                return;
            }

            try
            {
                if (!this.EnsureGameWorldProbeResolved())
                {
                    return;
                }

                // Emitted from HERE, not from the resolve path: that method early-returns once
                // resolved, so a toggle switched on AFTER the resolve would otherwise never get the
                // line at all. gameWorldProbeResolvedAt holds the real resolve moment regardless.
                if (!this.gameWorldProbeResolveLogged && MasterLogWorldStage)
                {
                    this.gameWorldProbeResolveLogged = true;
                    ModLogger.Msg("[WorldStage] GameWorld resolved at T+"
                        + this.gameWorldProbeResolvedAt.ToString("0.0") + "s since mod start (state=ok isSwitching=ok gameLevel="
                        + (this.gameWorldProbeGameLevelMethod != IntPtr.Zero ? "ok" : "MISSING") + ") — probing every "
                        + GameWorldProbeIntervalSeconds.ToString("0.##") + "s.");
                }

                if (!this.TryReadGameWorldStage(out int state, out bool switching, out int sceneId, out int levelType))
                {
                    this.NoteGameWorldProbeFailure("read returned no value");
                    return;
                }

                this.gameWorldProbeFailures = 0;
                this.ApplyGameWorldProbeSample(state, switching, sceneId, levelType);
                this.ReportGameWorldStageIfChanged(state, switching, sceneId, levelType);
                this.ProbeLevelLoadedTypeResolution(levelType, sceneId);
            }
            catch (Exception ex)
            {
                this.NoteGameWorldProbeFailure(ex.GetType().Name + " - " + ex.Message);
            }
        }

        private void NoteGameWorldProbeFailure(string reason)
        {
            this.gameWorldProbeFailures++;
            if (this.gameWorldProbeFailures < GameWorldProbeMaxConsecutiveFailures)
            {
                return;
            }

            // Fail closed and stay quiet: never crash-loop or spam. The gate is told explicitly so
            // it reverts to the loading events + player-presence fallback instead of freezing on
            // the last picture this probe handed it.
            this.gameWorldProbeDisabled = true;
            this.OnGameWorldProbeUnavailable();
            ModLogger.Msg("[WorldStage] probe disabled after " + GameWorldProbeMaxConsecutiveFailures
                + " consecutive failures (" + reason + ").");
        }

        private bool EnsureGameWorldProbeResolved()
        {
            if (this.gameWorldProbeResolved)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoClassFromName == null)
            {
                return false;
            }

            if (this.gameWorldProbeClass == IntPtr.Zero)
            {
                IntPtr image = this.FindAuraMonoImage(GameWorldImageNames);
                if (image == IntPtr.Zero)
                {
                    return false; // XDTBaseService not loaded yet — retry on a later tick.
                }

                this.gameWorldProbeClass = auraMonoClassFromName(image, "XDTGame.Core", "GameWorld");
                if (this.gameWorldProbeClass == IntPtr.Zero)
                {
                    return false;
                }
            }

            this.gameWorldProbeStateMethod = this.FindAuraMonoMethodOnHierarchy(this.gameWorldProbeClass, "get_state", 0);
            this.gameWorldProbeIsSwitchingMethod = this.FindAuraMonoMethodOnHierarchy(this.gameWorldProbeClass, "get_isSwitching", 0);
            this.gameWorldProbeGameLevelMethod = this.FindAuraMonoMethodOnHierarchy(this.gameWorldProbeClass, "get_gameLevel", 0);

            if (this.gameWorldProbeStateMethod == IntPtr.Zero || this.gameWorldProbeIsSwitchingMethod == IntPtr.Zero)
            {
                return false;
            }

            this.gameWorldProbeResolved = true;
            if (this.gameWorldProbeResolvedAt < 0f)
            {
                this.gameWorldProbeResolvedAt = Time.unscaledTime;
            }

            return true;
        }

        // state: GameLevelState (byte enum). switching: bool. sceneId/levelType: -1 when there is
        // no current level (login screen) or the getter is unavailable on this build.
        private unsafe bool TryReadGameWorldStage(out int state, out bool switching, out int sceneId, out int levelType)
        {
            state = -1;
            switching = false;
            sceneId = -1;
            levelType = -1;

            IntPtr exc = IntPtr.Zero;
            IntPtr boxedState = auraMonoRuntimeInvoke(this.gameWorldProbeStateMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxedState == IntPtr.Zero || !this.TryUnboxMonoByte(boxedState, out byte stateByte))
            {
                return false;
            }
            state = stateByte;

            exc = IntPtr.Zero;
            IntPtr boxedSwitching = auraMonoRuntimeInvoke(this.gameWorldProbeIsSwitchingMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxedSwitching == IntPtr.Zero || !this.TryUnboxMonoByte(boxedSwitching, out byte switchingByte))
            {
                return false;
            }
            switching = switchingByte != 0;

            // gameLevel is a reference (IGameLevel), returned directly rather than boxed. It is a
            // raw mono pointer handed to us mid-flight, and the GetSceneId/GetLevelType invokes
            // below can allocate — so pin it for the walk (AGENTS.md stale-pointer rule).
            if (this.gameWorldProbeGameLevelMethod == IntPtr.Zero)
            {
                return true; // state+switching are enough; level identity is a bonus.
            }

            exc = IntPtr.Zero;
            IntPtr levelObj = auraMonoRuntimeInvoke(this.gameWorldProbeGameLevelMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || levelObj == IntPtr.Zero)
            {
                return true; // no current level (login screen) — not a failure.
            }

            if (!AuraMonoPinningAvailable)
            {
                return true;
            }

            uint pin = AuraMonoPinNew(levelObj);
            try
            {
                if (this.TryGameWorldInvokeInt(levelObj, "GetSceneId", out int scene))
                {
                    sceneId = scene;
                }

                if (this.TryGameWorldInvokeInt(levelObj, "GetLevelType", out int type))
                {
                    levelType = type;
                }
            }
            finally
            {
                AuraMonoPinFree(pin);
            }

            return true;
        }

        // ----------------------------------------------------------------------------------------
        // DIAGNOSTIC: what can actually be resolved at WorldStage.LevelLoaded?
        //
        // The open question is whether `LevelLoaded` is a usable rung or a trap. By construction it
        // is reached BOTH on the login screen (level loaded, levelType 0, so IsWorldReady is false)
        // and in a real world during the 1.5 s grace — and those two are worlds apart in what the
        // game can actually answer. This measures the difference instead of arguing about it.
        //
        // STRICTLY metadata only: image lookup + mono_class_from_name. No static-field reads, no
        // method invokes, no generic inflation — i.e. none of the operations that actually fault
        // early (see project memory: auramono-static-field-login-crash,
        // eventhook-preworld-inflate-abort). If resolution turns out to succeed on the login screen
        // too, that is itself the answer: the danger is not resolving types, it is USING them, and
        // the rung has to be judged on that.
        //
        // Logs once per distinct (levelType, sceneId) so a session yields one line for login and one
        // per world, not a stream.
        private static readonly string[][] LevelLoadedProbeTypes =
        {
            //            image                    namespace                                        class
            new[] { "XDTBaseService",     "XDTGame.Core",                                  "GameWorld" },
            new[] { "XDTBaseService",     "XDTBaseService.Services.Cache",                 "LocalTextureCacheService" },
            new[] { "XDTLevelAndEntity",  "XDTLevelAndEntity.Gameplay.Component.Homeland", "PhotoFrameComponent" },
            new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.Config",                     "RenderLoadConfig" },
            new[] { "XDTViewBase",        "XDTViewBase.Loader",                            "DownLoadTexture2dAdvancedLoader" }
        };

        private readonly System.Collections.Generic.HashSet<long> levelLoadedProbeDone =
            new System.Collections.Generic.HashSet<long>();

        private void ProbeLevelLoadedTypeResolution(int levelType, int sceneId)
        {
            // Trigger on the RAW FSM condition (a level is loaded and settled into place), not on
            // the derived stage — the login level deliberately no longer reaches LevelLoaded, and
            // gating on the stage would silence exactly the case worth watching. The computed stage
            // is printed instead, so the log shows login sitting at MonoLive and a real world at
            // LevelLoaded: that IS the verification of the rung fix.
            if (!MasterLogWorldStage || !this.worldProbeLevelLoaded || this.worldProbeInTransition)
            {
                return;
            }

            long key = ((long)levelType << 32) ^ (uint)sceneId;
            if (!this.levelLoadedProbeDone.Add(key))
            {
                return;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[WorldStage] resolve probe: stage=").Append(this.CurrentWorldStage)
              .Append(" levelType=").Append(levelType)
              .Append(" scene=").Append(sceneId)
              .Append(levelType == WorldLevelTypeLogin ? " (LOGIN SCREEN)" : " (playable)")
              .Append(" ->");

            for (int i = 0; i < LevelLoadedProbeTypes.Length; i++)
            {
                string[] t = LevelLoadedProbeTypes[i];
                string result;
                try
                {
                    IntPtr image = this.FindAuraMonoImage(new[] { t[0], t[0] + ".dll" });
                    if (image == IntPtr.Zero)
                    {
                        result = "no-image";
                    }
                    else if (auraMonoClassFromName == null)
                    {
                        result = "no-api";
                    }
                    else
                    {
                        IntPtr klass = auraMonoClassFromName(image, t[1], t[2]);
                        result = klass != IntPtr.Zero ? "OK" : "class-MISS";
                    }
                }
                catch (Exception ex)
                {
                    result = "EX:" + ex.GetType().Name;
                }

                sb.Append("  ").Append(t[2]).Append('=').Append(result);
            }

            ModLogger.Msg(sb.ToString());
        }

        private unsafe bool TryUnboxMonoByte(IntPtr boxed, out byte value)
        {
            value = 0;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null || !this.TryAuraMonoBoxedIsValueType(boxed))
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            // Read exactly one byte: GameLevelState is `: byte` and bool is 1 byte, so a 4-byte
            // read would pull in whatever the boxed slot's padding happens to hold.
            value = *(byte*)raw;
            return true;
        }

        private bool TryGameWorldInvokeInt(IntPtr instanceObj, string methodName, out int value)
        {
            value = 0;
            if (instanceObj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr klass = auraMonoObjectGetClass(instanceObj);
            IntPtr method = klass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(klass, methodName, 0) : IntPtr.Zero;
            if (method == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, instanceObj, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero && boxed != IntPtr.Zero && this.TryUnboxMonoInt32(boxed, out value);
        }

        // Logs the probe's view side by side with the live gate, so the two can be compared in the
        // log without any guesswork about ordering. Emits only when something actually changed.
        private void ReportGameWorldStageIfChanged(int state, bool switching, int sceneId, int levelType)
        {
            // Logging off: return WITHOUT updating the snapshot, so the first tick after the user
            // ticks the toggle always emits the current state instead of staying silent until the
            // next transition happens to come along.
            if (!MasterLogWorldStage)
            {
                return;
            }

            int switchingFlag = switching ? 1 : 0;
            bool gateReady = this.IsWorldReady;
            int gateEpoch = this.worldReadyEpoch;

            bool changed = state != this.gameWorldProbeLastState
                || switchingFlag != this.gameWorldProbeLastSwitching
                || sceneId != this.gameWorldProbeLastSceneId
                || levelType != this.gameWorldProbeLastLevelType
                || gateReady != this.gameWorldProbeLastGateReady
                || gateEpoch != this.gameWorldProbeLastGateEpoch;
            if (!changed)
            {
                return;
            }

            this.gameWorldProbeLastState = state;
            this.gameWorldProbeLastSwitching = switchingFlag;
            this.gameWorldProbeLastSceneId = sceneId;
            this.gameWorldProbeLastLevelType = levelType;
            this.gameWorldProbeLastGateReady = gateReady;
            this.gameWorldProbeLastGateEpoch = gateEpoch;

            string stateName = state >= 0 && state < GameWorldLevelStateNames.Length
                ? GameWorldLevelStateNames[state]
                : ("#" + state);

            ModLogger.Msg("[WorldStage] " + this.CurrentWorldStage
                + " | GameWorld state=" + stateName
                + " switching=" + (switching ? "Y" : "N")
                + " scene=" + sceneId
                + " levelType=" + levelType
                + "  ||  gate IsWorldReady=" + (gateReady ? "Y" : "N")
                + " epoch=" + gateEpoch);
        }
    }
}
