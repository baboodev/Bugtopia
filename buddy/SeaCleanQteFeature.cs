using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Auto Sea Clean QTE — auto-pass the ocean-cleanup pollutant QTE (2026-07-09 update).
    //
    // The Sea-Clean QTE is CLIENT-SIMULATED and self-reported to the server as a single
    // `bool QtePassed` (same trust model as fishing instant-catch / dragon-boat fake-checkpoint;
    // see .research-record/SEA_CLEAN_QTE_REPORT.md + memory seaclean-qte-client-authoritative).
    // There are two safe AuraMono levers — no IL2CPP .text patch:
    //   * EXCLUSIVE (private/solo) pollutant, fully client-side:
    //       SeaCleanMonsterComponent.ExecuteKill()  → MarkCleaned(qtePassed:true) → dying-Tick
    //       auto-sends ReqExclusiveSyncState(...,true,true). Instant kill, no hold / no roll.
    //   * PUBLIC "boss" pollutant (server owns HP, but trusts the pass bool):
    //       SeaCleanProtocolManager.ReqStartCleanPublicPollutant(netId, qtePassed:true)
    //       → "never-miss" (server still applies per-pass damage; not a one-shot).
    //
    // Both are driven off the GLOBAL EventCenter event SeaCleanExecutionStateEvent (dispatched on
    // every QTE state change). When a QTE becomes active (state == Ready) and it is NOT a shield
    // QTE, we resolve the pollutant currently in QTE by scanning SeaCleanMonsterComponent via the
    // reusable Entities.GetComponents<T> path (TryAuraMonoGetComponentObjects), pick the one whose
    // IsInQTE == true, and fire the matching lever. CleanupEventPublicPollutantSpawnedEvent captures
    // the boss netId as a fallback.
    //
    // Structurally identical to AutoFishingFarm / PetPlayFeature (QTE event → AuraMono invoke).
    // Fail-closed everywhere: if AuraMono / pinning / type resolution is not ready, no-op — never
    // crash. Gated entirely behind seaCleanQteEnabled; no runtime work (and no hook) when OFF.
    public partial class HeartopiaComplete
    {
        // Verbose diagnosis. Off by default — the user turns this on to verify offsets/enum/target
        // resolution live (we cannot test in-game). Every key step logs under [SeaCleanQte].
        private bool MasterLogSeaCleanQte = false;

        private bool seaCleanQteEnabled = false;
        private KeyCode seaCleanQteHotkey = KeyCode.None;

        // Event hooks are registered once (idempotent per name in the engine) the first time the
        // feature is enabled; the handlers themselves gate on seaCleanQteEnabled so a disabled
        // feature does zero work. This mirrors PetPlay's EnsurePetPlayEventHooks.
        private bool seaCleanQteEventHooksRegistered = false;

        // Boss netId captured from CleanupEventPublicPollutantSpawnedEvent (fallback if the scan
        // can't identify a public pollutant currently in QTE).
        private uint seaCleanQteBossNetId = 0U;

        // Throttles: collapse duplicate same-dispatch Ready events, and avoid re-killing the same
        // exclusive target within a short window (harmless but keeps the log clean).
        private float seaCleanQteNextActionAt = 0f;
        private uint seaCleanQteLastActedNetId = 0U;
        private float seaCleanQteLastActedAt = -999f;
        private float seaCleanQteNextResolveAt = 0f;

        // Cached AuraMono class/method pointers (class/method IntPtrs may stay raw — image lifetime).
        private IntPtr seaCleanQteMonsterClass = IntPtr.Zero;
        private IntPtr seaCleanQteExecuteKillMethod = IntPtr.Zero;
        private IntPtr seaCleanQteGetIsInQteMethod = IntPtr.Zero;
        private IntPtr seaCleanQteGetIsSharedMethod = IntPtr.Zero;
        private IntPtr seaCleanQteGetIsPublicMethod = IntPtr.Zero;
        private IntPtr seaCleanQteGetIsCleanedMethod = IntPtr.Zero;
        private IntPtr seaCleanQteGetIsQteShieldMethod = IntPtr.Zero;
        private IntPtr seaCleanQteProtocolClass = IntPtr.Zero;
        private IntPtr seaCleanQteReqStartPublicMethod = IntPtr.Zero;

        // ---- Event wiring ----------------------------------------------------------------------

        private const string SeaCleanExecutionStateEventName = "XDTDataAndProtocol.Events.SeaCleanExecutionStateEvent";
        // SeaCleanExecutionStateEvent (namespace XDTDataAndProtocol.Events) — Mono sequential layout.
        //   state (SeaCleanExecutionState=int)        @0
        //   targetEntityInstanceId (InstanceID, 8B, contains a long => 8-aligned) @8 (4..7 = padding)
        //     .id (EntityId=uint) @8, .world (int) @12
        //   isShieldQTE (bool/byte)                   @16
        //   holdProgress (float)                      @20
        //   isLastRound (bool/byte)                   @24
        //   completedRounds (int)                     @28
        //   totalRounds (int)                         @32
        // => 40 bytes (8-aligned tail). Offsets GUESSED from CLR/Mono natural alignment; verify live
        //    with MasterLogGameEvents (the InstanceID 8-alignment is the main uncertainty).
        private const int SeaCleanExecutionStateEventBytes = 40;
        private const int SeaCleanExecStateOffset = 0;
        private const int SeaCleanExecTargetEntityIdOffset = 8;   // InstanceID.id (low uint)
        private const int SeaCleanExecTargetWorldOffset = 12;     // InstanceID.world
        private const int SeaCleanExecIsShieldOffset = 16;

        // SeaCleanExecutionState enum: Idle=0, Ready=1, Holding=2, AwaitingRelease=3, Completed=4, Failed=5.
        private const int SeaCleanExecStateReady = 1;

        private const string CleanupEventPublicPollutantSpawnedEventName = "XDTDataAndProtocol.Events.CleanupEventPublicPollutantSpawnedEvent";
        // CleanupEventPublicPollutantSpawnedEvent: netId(uint)@0, maxHp(float)@4, currentHp(float)@8,
        // phaseIndex(byte)@12 => 16 bytes (4-aligned tail).
        private const int CleanupEventPublicPollutantSpawnedEventBytes = 16;

        private void SeaCleanQteLog(string message)
        {
            if (!this.MasterLogSeaCleanQte)
            {
                return;
            }

            ModLogger.Msg("[SeaCleanQte] " + message);
        }

        // Called every frame from OnUpdate. Registers the event hooks the first time the feature is
        // enabled (and never when it stays OFF), then relies on the shared event engine for install +
        // drain. All action happens inside the event handlers, gated on seaCleanQteEnabled.
        private void ProcessSeaCleanQteOnUpdate()
        {
            if (!this.seaCleanQteEnabled)
            {
                return;
            }

            this.EnsureSeaCleanQteEventHooks();
        }

        private void EnsureSeaCleanQteEventHooks()
        {
            if (this.seaCleanQteEventHooksRegistered)
            {
                return;
            }

            this.seaCleanQteEventHooksRegistered = true;
            this.RegisterGameEventHook(SeaCleanExecutionStateEventName, SeaCleanExecutionStateEventBytes, this.OnSeaCleanExecutionStateEvent);
            this.RegisterGameEventHook(CleanupEventPublicPollutantSpawnedEventName, CleanupEventPublicPollutantSpawnedEventBytes, this.OnCleanupEventPublicPollutantSpawnedEvent);
            this.SeaCleanQteLog("Registered SeaCleanExecutionStateEvent + CleanupEventPublicPollutantSpawnedEvent hooks.");
        }

        // Public boss appeared / phase change — remember its netId for the public-pollutant fallback.
        private void OnCleanupEventPublicPollutantSpawnedEvent(GameEventSnapshot e)
        {
            if (!this.seaCleanQteEnabled)
            {
                return;
            }

            uint netId = e.ReadUInt32(0);
            if (netId != 0U)
            {
                this.seaCleanQteBossNetId = netId;
                this.SeaCleanQteLog("Public pollutant spawned netId=" + netId + " (boss fallback captured).");
            }
        }

        // A QTE changed state. Auto-pass when it just became active (Ready) and it is NOT a shield QTE.
        private void OnSeaCleanExecutionStateEvent(GameEventSnapshot e)
        {
            if (!this.seaCleanQteEnabled)
            {
                return;
            }

            int state = e.ReadInt32(SeaCleanExecStateOffset);
            bool isShield = e.ReadBool(SeaCleanExecIsShieldOffset);
            uint targetEntityId = e.ReadUInt32(SeaCleanExecTargetEntityIdOffset);
            int targetWorld = e.ReadInt32(SeaCleanExecTargetWorldOffset);

            this.SeaCleanQteLog("QTE event state=" + state + " shield=" + isShield
                + " targetEntityId=" + targetEntityId + " world=" + targetWorld);

            // Only act when the QTE opens (Ready). Acting here short-circuits the hold entirely:
            // ExecuteKill (exclusive) instantly kills; ReqStartCleanPublicPollutant(true) reports a
            // pass (public boss). Shield QTEs are left alone (per design).
            if (state != SeaCleanExecStateReady || isShield)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.seaCleanQteNextActionAt)
            {
                return;
            }

            this.seaCleanQteNextActionAt = now + 0.2f;

            this.TryAutoPassSeaCleanQte("event");
        }

        // Resolve the pollutant currently in QTE and fire the matching client-authoritative lever.
        // Everything is synchronous within this scope (no yields / no cross-frame IntPtr holds — the
        // scan pins components for the loop and frees them in finally, per the moving-SGen rule).
        private unsafe void TryAutoPassSeaCleanQte(string source)
        {
            if (!this.EnsureSeaCleanQteAuraResolved(out string resolveStatus))
            {
                this.SeaCleanQteLog("resolve unavailable (" + source + "): " + resolveStatus);
                return;
            }

            if (this.seaCleanQteMonsterClass == IntPtr.Zero || this.seaCleanQteGetIsInQteMethod == IntPtr.Zero)
            {
                this.SeaCleanQteLog("monster class / IsInQTE getter unavailable — cannot resolve target.");
                return;
            }

            List<uint> compPins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.seaCleanQteMonsterClass, out List<IntPtr> components, compPins)
                    || components == null || components.Count == 0)
                {
                    this.SeaCleanQteLog("no SeaCleanMonsterComponent instances (GetComponents empty/unavailable).");
                    return;
                }

                int inQte = 0;
                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr compObj = components[i];
                    if (compObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    // The definitive "this is the QTE target" signal: the component's own IsInQTE.
                    if (!this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsInQteMethod, out bool isInQte) || !isInQte)
                    {
                        continue;
                    }

                    inQte++;

                    // Robust shield-skip independent of the event's isShield byte offset: never act on
                    // a component whose own QTE is a shield QTE.
                    if (this.seaCleanQteGetIsQteShieldMethod != IntPtr.Zero
                        && this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsQteShieldMethod, out bool isQteShield)
                        && isQteShield)
                    {
                        this.SeaCleanQteLog("in-QTE target is a shield QTE — leaving it alone.");
                        continue;
                    }

                    // Skip already-dying/cleaned targets.
                    if (this.seaCleanQteGetIsCleanedMethod != IntPtr.Zero
                        && this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsCleanedMethod, out bool isCleaned)
                        && isCleaned)
                    {
                        continue;
                    }

                    uint netId = 0U;
                    this.TryHomelandFarmTryReadAuraMonoComponentNetId(compObj, out netId);

                    bool isPublic = this.seaCleanQteGetIsPublicMethod != IntPtr.Zero
                        && this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsPublicMethod, out bool pub)
                        && pub;

                    if (isPublic)
                    {
                        if (netId == 0U)
                        {
                            netId = this.seaCleanQteBossNetId; // fallback to the captured boss netId
                        }

                        if (this.TrySeaCleanReqStartPublicPass(netId, source + "/public"))
                        {
                            return;
                        }

                        continue;
                    }

                    // Exclusive (private) pollutant: only when NOT shared. Shared env-hosted
                    // pollutants have no client kill lever — leave them alone.
                    bool isShared = this.seaCleanQteGetIsSharedMethod != IntPtr.Zero
                        && this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsSharedMethod, out bool shared)
                        && shared;
                    if (isShared)
                    {
                        this.SeaCleanQteLog("in-QTE target netId=" + netId + " is shared/non-public — no client lever, skipping.");
                        continue;
                    }

                    if (this.TrySeaCleanExecuteKill(compObj, netId, source + "/exclusive"))
                    {
                        return;
                    }
                }

                // No component reported IsInQTE, but a public boss is tracked and the (non-shield)
                // QTE fired: fall back to the boss pass so the public branch still works even if the
                // scan couldn't flag the component in time.
                if (inQte == 0 && this.seaCleanQteBossNetId != 0U)
                {
                    this.SeaCleanQteLog("no in-QTE component found; using tracked boss netId=" + this.seaCleanQteBossNetId + " fallback.");
                    this.TrySeaCleanReqStartPublicPass(this.seaCleanQteBossNetId, source + "/boss-fallback");
                    return;
                }

                this.SeaCleanQteLog("scanned " + components.Count + " pollutant(s), inQTE=" + inQte + " — nothing actionable.");
            }
            finally
            {
                FreeAuraMonoPins(compPins);
            }
        }

        // AuraMono instance invoke: SeaCleanMonsterComponent.ExecuteKill() — the game's own success
        // path for exclusive pollutants (MarkCleaned(qtePassed:true) → dying Tick sends
        // ReqExclusiveSyncState(...,true,true)).
        private unsafe bool TrySeaCleanExecuteKill(IntPtr compObj, uint netId, string source)
        {
            if (this.seaCleanQteExecuteKillMethod == IntPtr.Zero || compObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                this.SeaCleanQteLog("ExecuteKill unavailable (method/obj/invoke missing).");
                return false;
            }

            // Avoid re-killing the same target within a short window (event may re-dispatch).
            float now = Time.unscaledTime;
            if (netId != 0U && netId == this.seaCleanQteLastActedNetId && now - this.seaCleanQteLastActedAt < 2f)
            {
                return true;
            }

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.seaCleanQteExecuteKillMethod, compObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero)
            {
                this.SeaCleanQteLog("ExecuteKill threw exc=0x" + exc.ToInt64().ToString("X") + " (" + source + ").");
                return false;
            }

            this.seaCleanQteLastActedNetId = netId;
            this.seaCleanQteLastActedAt = now;
            this.SeaCleanQteLog("lever fired: ExecuteKill() netId=" + netId + " (" + source + ").");
            return true;
        }

        // AuraMono static invoke: SeaCleanProtocolManager.ReqStartCleanPublicPollutant(netId, qtePassed:true).
        private unsafe bool TrySeaCleanReqStartPublicPass(uint netId, string source)
        {
            if (this.seaCleanQteReqStartPublicMethod == IntPtr.Zero || netId == 0U || auraMonoRuntimeInvoke == null)
            {
                this.SeaCleanQteLog("ReqStartCleanPublicPollutant unavailable (method/netId/invoke missing, netId=" + netId + ").");
                return false;
            }

            float now = Time.unscaledTime;
            if (netId == this.seaCleanQteLastActedNetId && now - this.seaCleanQteLastActedAt < 0.5f)
            {
                return true;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            uint targetNetId = netId;
            byte qtePassed = 1; // true
            args[0] = (IntPtr)(&targetNetId);
            args[1] = (IntPtr)(&qtePassed);
            auraMonoRuntimeInvoke(this.seaCleanQteReqStartPublicMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                this.SeaCleanQteLog("ReqStartCleanPublicPollutant threw exc=0x" + exc.ToInt64().ToString("X") + " (" + source + ").");
                return false;
            }

            this.seaCleanQteLastActedNetId = netId;
            this.seaCleanQteLastActedAt = now;
            this.SeaCleanQteLog("lever fired: ReqStartCleanPublicPollutant(" + netId + ", qtePassed:true) (" + source + ").");
            return true;
        }

        private unsafe bool SeaCleanQteInvokeBoolGetter(IntPtr obj, IntPtr getter, out bool value)
        {
            value = false;
            if (obj == IntPtr.Zero || getter == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getter, obj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(boxed, out value);
        }

        // Resolve (once, with a retry throttle) the SeaCleanMonsterComponent class + its property
        // getters / ExecuteKill, and the SeaCleanProtocolManager + ReqStartCleanPublicPollutant.
        // Fail-closed: returns false (and no-ops) until AuraMono is up and the types resolve.
        private bool EnsureSeaCleanQteAuraResolved(out string status)
        {
            status = "cached";
            bool haveMonster = this.seaCleanQteMonsterClass != IntPtr.Zero
                && this.seaCleanQteExecuteKillMethod != IntPtr.Zero
                && this.seaCleanQteGetIsInQteMethod != IntPtr.Zero;
            bool haveProtocol = this.seaCleanQteReqStartPublicMethod != IntPtr.Zero;
            if (haveMonster && haveProtocol)
            {
                return true;
            }

            float now = Time.unscaledTime;
            if (now < this.seaCleanQteNextResolveAt)
            {
                status = "resolve throttled";
                return haveMonster; // exclusive path can still run once the monster side resolves
            }

            this.seaCleanQteNextResolveAt = now + 2f;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    status = "AuraMono API not ready";
                    return false;
                }

                if (this.seaCleanQteMonsterClass == IntPtr.Zero)
                {
                    this.seaCleanQteMonsterClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.Pollution.SeaCleanMonsterComponent");
                    if (this.seaCleanQteMonsterClass == IntPtr.Zero)
                    {
                        this.seaCleanQteMonsterClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GamePlay.Component.Pollution.SeaCleanMonsterComponent");
                    }
                    if (this.seaCleanQteMonsterClass == IntPtr.Zero)
                    {
                        this.seaCleanQteMonsterClass = this.FindAuraMonoClassByFullName("ScriptsRefactory.LevelAndEntity.Gameplay.Component.Pollution.SeaCleanMonsterComponent");
                    }
                    if (this.seaCleanQteMonsterClass == IntPtr.Zero)
                    {
                        this.seaCleanQteMonsterClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                            "XDTLevelAndEntity.Gameplay.Component.Pollution",
                            "SeaCleanMonsterComponent");
                    }
                }

                if (this.seaCleanQteMonsterClass != IntPtr.Zero)
                {
                    if (this.seaCleanQteExecuteKillMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteExecuteKillMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "ExecuteKill", 0);
                    }
                    if (this.seaCleanQteGetIsInQteMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteGetIsInQteMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_IsInQTE", 0);
                    }
                    if (this.seaCleanQteGetIsSharedMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteGetIsSharedMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_IsShared", 0);
                    }
                    if (this.seaCleanQteGetIsPublicMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteGetIsPublicMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_IsPublicPollutant", 0);
                    }
                    if (this.seaCleanQteGetIsCleanedMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteGetIsCleanedMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_IsCleaned", 0);
                    }
                    if (this.seaCleanQteGetIsQteShieldMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteGetIsQteShieldMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_IsQTEShield", 0);
                    }
                }

                if (this.seaCleanQteProtocolClass == IntPtr.Zero)
                {
                    this.seaCleanQteProtocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.SeaClean.SeaCleanProtocolManager");
                    if (this.seaCleanQteProtocolClass == IntPtr.Zero)
                    {
                        this.seaCleanQteProtocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                            "XDTDataAndProtocol.ProtocolService.SeaClean",
                            "SeaCleanProtocolManager");
                    }
                }

                if (this.seaCleanQteProtocolClass != IntPtr.Zero && this.seaCleanQteReqStartPublicMethod == IntPtr.Zero)
                {
                    this.seaCleanQteReqStartPublicMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteProtocolClass, "ReqStartCleanPublicPollutant", 2);
                }

                haveMonster = this.seaCleanQteMonsterClass != IntPtr.Zero
                    && this.seaCleanQteExecuteKillMethod != IntPtr.Zero
                    && this.seaCleanQteGetIsInQteMethod != IntPtr.Zero;
                haveProtocol = this.seaCleanQteReqStartPublicMethod != IntPtr.Zero;

                status = "monsterClass=0x" + this.seaCleanQteMonsterClass.ToInt64().ToString("X")
                    + " executeKill=0x" + this.seaCleanQteExecuteKillMethod.ToInt64().ToString("X")
                    + " isInQte=0x" + this.seaCleanQteGetIsInQteMethod.ToInt64().ToString("X")
                    + " isShared=0x" + this.seaCleanQteGetIsSharedMethod.ToInt64().ToString("X")
                    + " isPublic=0x" + this.seaCleanQteGetIsPublicMethod.ToInt64().ToString("X")
                    + " reqPublic=0x" + this.seaCleanQteReqStartPublicMethod.ToInt64().ToString("X");
                this.SeaCleanQteLog("resolve: " + status);
                return haveMonster || haveProtocol;
            }
            catch (Exception ex)
            {
                status = "resolve error: " + ex.Message;
                this.SeaCleanQteLog(status);
                return false;
            }
        }

        // ---- UI --------------------------------------------------------------------------------

        private float DrawSeaCleanQteTab(int startY)
        {
            const float left = 40f;
            float y = startY;

            Color textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = textColor;

            GUIStyle bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                wordWrap = true
            };
            bodyStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);

            GUI.Label(new Rect(left, y, 460f, 24f), this.L("Auto Sea Clean QTE"), headerStyle);
            y += 34f;

            bool prev = this.seaCleanQteEnabled;
            this.seaCleanQteEnabled = this.DrawSwitchToggle(new Rect(left, y, 360f, 30f), this.seaCleanQteEnabled, "Auto Sea Clean QTE");
            if (this.seaCleanQteEnabled != prev)
            {
                this.AddMenuNotification(
                    $"Auto Sea Clean QTE {(this.seaCleanQteEnabled ? "Enabled" : "Disabled")}",
                    this.seaCleanQteEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
            }
            y += 40f;

            GUI.Label(new Rect(left, y, 500f, 60f),
                this.L("Auto-passes the ocean cleanup pollutant QTE. Equip the sea cleaner and aim at a pollutant; the QTE is passed automatically (exclusive pollutants are cleaned instantly, the public boss never misses). Shield QTEs are left alone."),
                bodyStyle);
            y += 66f;

            KeyCode hotkey = this.seaCleanQteHotkey;
            GUI.Label(new Rect(left, y, 500f, 22f),
                this.LF("Hotkey: {0} (rebind in Settings > Keybinds)", FormatKeybindLabel(hotkey)),
                bodyStyle);
            y += 30f;

            return y + 20f;
        }

        private float CalculateSeaCleanQteTabHeight()
        {
            return 220f;
        }
    }
}
