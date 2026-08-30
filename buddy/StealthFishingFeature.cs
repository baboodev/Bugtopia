using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    // Hide Fishing From Others — you fish for real, nobody around you sees it.
    //
    // WHY THIS WORKS AT ALL: fishing rides two INDEPENDENT channels.
    //
    //   gameplay  : FishingProtocolManager -> WebRequestUtility.SendCommand
    //               (CastRodNetworkCommand, ActivateRodBuoyNetworkCommand, PullRodNetworkCommand).
    //               Server-authoritative: this is what actually catches the fish and pays out.
    //   cosmetics : PlayerSyncStatus -> NetSender.SendMsg
    //               -> CharacterProtocolManager.SendPlayerShowActionData
    //               -> ClientShowActionNetworkCommand.
    //               The server NEVER interprets these bytes: it caches the latest blob per
    //               actionKey (PlayerShowActionComponent + PlayerShowActionIdProperty.SlotId) and
    //               rebroadcasts them as ClientShowActionNetworkEvent. Animation is therefore
    //               100% client-authoritative -- see the "player-animation-replication-pipeline"
    //               and "fishing-animation-replication" notes.
    //
    // So we cut the second channel only. The catch still resolves, the reward still lands, and
    // the observers' PlayerFishingMotion never starts because the bytes that would start it
    // never leave this process.
    //
    // THE CHOKEPOINT: CharacterProtocolManager.SendPlayerShowActionData(uint, List<CharacterSyncAction>)
    // (ilspy-dumps/XDTDataAndProtocol/XDTDataAndProtocol.ProtocolService.GamePlay.Character/
    //  CharacterProtocolManager.cs:240). Two properties make it the right place:
    //   1. Its ONLY caller is NetSender.SendMsg (NetSender.cs:57).
    //   2. That caller does _actions.Clear() then _actions.Add(one) -- so every invocation carries
    //      EXACTLY ONE actionKey. The filter is a single int read at a fixed offset; no payload
    //      parsing, no packed-int decoding.
    // Dropping the call outright is the cleanest cut: the whole ClientShowActionNetworkCommand for
    // that one key simply never goes out, and no game state is left half-updated (the dirty bit was
    // already cleared by SendSyncStatus before OnSend ran -- see "AFTER-EFFECT" below).
    //
    // WHAT WE DROP (actionKey = SyncFieldId = propertyId | (fieldId << 16)):
    //   FishingStatus  [NetId(5, 10)]  fields 0..8  -- InFishingState / FishRodNetId / FishState /
    //       Pressed / TarPos / FloatPos / PullStrength / BaitingFishStaticId / BaitingFishNetId.
    //       This is the whole state machine the observer's PlayerFishingMotion polls every frame in
    //       CheckState()/TickState(); with it gone the clip has nothing to react to.
    //   FsmStatus      [NetId(1, 201)] field 6 (MotionData) -- the body motion clip, i.e.
    //       ActionId.PlayerFishingThrowMotion(418) while aiming and ActionId.PlayerFishingMotion(402)
    //       while fishing. Field 7 (UpperMotionData) is deliberately LEFT ALONE: fishing is
    //       body-only, so blocking it would only hide unrelated upper-body motions.
    //   CastActionEvent[NetId(13, 203)] field 0 -- one-shot casts, i.e. ActionId.FishThrowSuccess(297).
    //   QuickActionEvent[NetId(18, 202)] field 0 -- the quickCast variant of the same channel.
    //   EquipStatus    [NetId(2, 1)]   fields 0..10 -- handHoldId / handHoldNetId / visible: hides
    //       the rod appearing in your hands for everyone who already has you spawned.
    //   ShowOffStatus  [NetId(12, 13)] fields 0..2  -- the "show off the fish" display pose.
    //
    // Everything else -- MovementStatus above all -- still flows, so your position and rotation stay
    // correct for everyone. You simply stand there.
    //
    // ROTATION IS NOT ON THIS CHANNEL. Position and rotation go out with the transform sync
    // (LocalPlayerComponent._TickSendSelfTransform), which this filter deliberately leaves alone --
    // blocking it would desync where you are standing. That makes the mod's own pre-cast turn toward
    // the fish the loudest remaining tell, so TryEnterFishingAtTarget (HeartopiaComplete.Fishing.cs)
    // skips TryFacePlayerTowardCastTarget entirely while this toggle is on.
    //
    // WHAT CANNOT BE HIDDEN (client-side limits, stated honestly):
    //   * The rod for someone who spawns you LATER. The initial handhold comes from the server ECS
    //     snapshot (PlayerSyncClientService.GenPlayerEntity -> HolderService.GetCurrentHolder ->
    //     HandholdComponentData), not from EquipStatus. Blocking EquipStatus only helps observers
    //     who already had you in view.
    //   * Fish shadows circling your buoy. ServerFishComponent entities are world objects and the
    //     server pushes their AI state to everyone (FishShadowSyncSystem -> CmdUpdateFishShadowAiState
    //     keyed by your BuoyNetId). Not a client-side decision.
    //   * The buoy VISUAL is fine: it is a locally created entity per rod
    //     (HandHoldFishingRod.OnAttached -> Entities.CreateEntity<FloatComponentData>), and only
    //     PlayerFishingMotion moves it out of BuoyState.FollowRod using the replicated TarPos/FloatPos.
    //     With those dropped it stays glued to the rod on every other client.
    //
    // AFTER-EFFECT (documented, not a bug): SendSyncStatus clears a field's dirty bit BEFORE calling
    // its OnSend, so a value we drop here is never re-sent on its own. Concretely: after you switch
    // this off, observers keep showing the last body motion they saw until something sets a NEW one.
    // Leaving the fishing FSM does exactly that (TransitionFish2Free -> PlayerStateFree.TransitEnter
    // -> SetMotion), and so does any movement -- so flip this OFF once you are out of a fishing
    // session and it heals itself on the next state change. Flipping it off mid-cast just means the
    // rest of that cast is visible.
    //
    // FAIL-OPEN EVERYWHERE: any pointer that does not pass the shape check -> forward to the
    // trampoline unchanged. A broken hook must degrade to vanilla, never to "your sync is dead".
    //
    // THE DETOUR BODY DOES NO MONO CALLS AND NO LOGGING. The first version of this feature resolved
    // the List<CharacterSyncAction> field offsets from inside the body (mono_object_get_class +
    // mono_class_get_field_from_name + mono_field_get_offset) and logged the result -- i.e. Mono
    // metadata lookups and a BepInEx logger call from inside the prologue of a Mono method on the
    // network-send path. That is an abort with no crash dump, and pressing a movement key was enough
    // to trigger it, because MovementStatus makes this the busiest call in the game. The body is now
    // arithmetic only; the layout is validated per packet by the _size == 1 / max_length signature
    // instead (see StealthFishTryReadFirstActionKey).
    //
    // The detour is a Mono NativeDetour (AuraMono resolve + mono_compile_method + MonoMod
    // NativeDetour + GenerateTrampoline), the same shape as the NotifyFloatInWater detour in
    // HeartopiaComplete.Fishing.cs and CraftDirectSendFeature. No IL2CPP .text patch, so the Themis
    // module-integrity rule in AGENTS.md holds. Install runs on the world-ready gate, and -- like
    // CraftDirectSend -- the detour is NEVER Undo()n once live (tearing a native detour down
    // mid-session is its own corruption source, memory: native-detours-world-change-corruption);
    // with the flag off the body is a pure pass-through, and it is not INSTALLED until the toggle is
    // first switched on, so the opt-in property is preserved.
    public partial class HeartopiaComplete
    {
        // ---- opcodes (PlayerSyncStatus [NetId(propertyId, x)] / OpCode.cs) ----------------------
        private const int StealthFishOpFsmStatus = 1;
        private const int StealthFishOpEquipStatus = 2;
        private const int StealthFishOpFishingStatus = 5;
        private const int StealthFishOpShowOffStatus = 12;
        private const int StealthFishOpCastActionEvent = 13;
        private const int StealthFishOpQuickActionEvent = 18;

        // FieldCount values read off the status structs; a game update that changes them only makes
        // the set slightly wider/narrower than needed, never wrong for the fields that still exist.
        private const int StealthFishFishingFieldCount = 9;
        private const int StealthFishEquipFieldCount = 11;
        private const int StealthFishShowOffFieldCount = 3;
        private const int StealthFishFsmMotionDataField = 6;

        // Sanity ceiling for the MonoArray max_length read: NetSender's list never grows past a
        // handful of entries, so anything larger means we are not looking at that array.
        private const int StealthFishMaxPlausibleArrayLength = 4096;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StealthFishShowActionHookDelegate(uint netId, IntPtr changeActions);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr StealthFishCompileMethodDelegate(IntPtr method);

        // The toggle, read by the detour body. Static because the body must be.
        private static bool stealthFishHideActive;

        private static MonoMod.RuntimeDetour.NativeDetour stealthFishDetour;
        private static StealthFishShowActionHookDelegate stealthFishHookKeepAlive; // anti-GC
        private static StealthFishShowActionHookDelegate stealthFishTrampoline;

        private static HashSet<int> stealthFishBlockedKeys;
        private static long stealthFishDroppedCount;

        private bool stealthFishEnabled;
        private bool stealthFishCallbackRegistered;
        private bool stealthFishHookTried;

        // ---- public surface (UI + config) ------------------------------------------------------

        public bool GetStealthFishingEnabled()
        {
            return this.stealthFishEnabled;
        }

        public void SetStealthFishingEnabled(bool value)
        {
            if (this.stealthFishEnabled == value)
            {
                return;
            }

            this.stealthFishEnabled = value;
            stealthFishHideActive = value;

            if (!value)
            {
                FeatureLog.Toggle("StealthFish", false);
                FeatureLog.Life("StealthFish", "dropped " + stealthFishDroppedCount
                    + " show-action packet(s) this session; your last body motion stays as the"
                    + " observers saw it until the next state change (leave fishing or move)");
                return;
            }

            FeatureLog.Toggle("StealthFish", true);
            if (stealthFishTrampoline == null)
            {
                // Install is deferred to the world-ready gate; say so rather than looking silent.
                FeatureLog.Life("StealthFish", "arming — the show-action hook installs on the next world-ready gate");
            }
        }

        public static long GetStealthFishingDroppedCount()
        {
            return stealthFishDroppedCount;
        }

        // Status line for the fishing panel.
        public string GetStealthFishingStatus()
        {
            if (!this.stealthFishEnabled)
            {
                return "off";
            }

            if (stealthFishTrampoline == null)
            {
                return this.stealthFishHookTried ? "hook unavailable" : "arming";
            }

            return "hiding (" + stealthFishDroppedCount + " dropped)";
        }

        // ---- per-frame glue --------------------------------------------------------------------

        private void ProcessStealthFishingOnUpdate()
        {
            if (!this.stealthFishEnabled)
            {
                // Deliberately NOT Undo()n once installed — see the header. An inert detour is a
                // pass-through; the flag is the whole switch.
                stealthFishHideActive = false;
                return;
            }

            stealthFishHideActive = true;

            // Hook installs run on the world-ready gate, never on a retry timer here
            // (AGENTS.md §1 hard rule). Registration is idempotent and cheap.
            if (!this.stealthFishCallbackRegistered)
            {
                this.stealthFishCallbackRegistered = true;
                this.RegisterWorldReadyCallback("StealthFishing", this.TryInstallStealthFishingHookOnWorldReady);
            }
        }

        // ---- install ---------------------------------------------------------------------------

        private bool TryInstallStealthFishingHookOnWorldReady()
        {
            if (stealthFishTrampoline != null || this.stealthFishHookTried)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false; // AuraMono not up yet — retry
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                StealthFishCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<StealthFishCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.stealthFishHookTried = true;
                    FeatureLog.Fail("StealthFish", "mono_compile_method unavailable — feature off.");
                    return true;
                }

                IntPtr cls = this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.GamePlay.Character.CharacterProtocolManager");
                if (cls == IntPtr.Zero)
                {
                    return false; // image not loaded yet — retry
                }

                // public static void SendPlayerShowActionData(uint, List<CharacterSyncAction>)
                // -> void(uint, object*). Static, non-generic, no struct-by-value: the Win64 ABI
                // passes both args in registers exactly as the delegate declares them.
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "SendPlayerShowActionData", 2);
                if (method == IntPtr.Zero)
                {
                    this.stealthFishHookTried = true;
                    FeatureLog.Fail("StealthFish",
                        "CharacterProtocolManager.SendPlayerShowActionData(2) not found — feature off (game update?).");
                    return true;
                }

                IntPtr nativePtr = compile(method);
                if (nativePtr == IntPtr.Zero)
                {
                    this.stealthFishHookTried = true;
                    FeatureLog.Fail("StealthFish", "mono_compile_method returned null — feature off.");
                    return true;
                }

                EnsureStealthFishingBlockedKeys();

                stealthFishHookKeepAlive = StealthFishSendShowActionDetourBody;
                stealthFishDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, stealthFishHookKeepAlive);
                stealthFishTrampoline = stealthFishDetour.GenerateTrampoline<StealthFishShowActionHookDelegate>();
                if (stealthFishTrampoline == null)
                {
                    // Only place we ever Undo: the hook was never usable, so this is install
                    // rollback, not a live-detour teardown. Without a trampoline every show-action
                    // packet would be swallowed — that would break the game for everyone watching.
                    try { stealthFishDetour?.Undo(); } catch { }
                    stealthFishDetour = null;
                    this.stealthFishHookTried = true;
                    FeatureLog.Fail("StealthFish", "trampoline unavailable; detour reverted — feature off.");
                    return true;
                }

                this.stealthFishHookTried = true;
                FeatureLog.Life("StealthFish", "Hooked CharacterProtocolManager.SendPlayerShowActionData @0x"
                    + nativePtr.ToInt64().ToString("X") + " — " + stealthFishBlockedKeys.Count + " action keys filtered");
                return true;
            }
            catch (Exception ex)
            {
                try { stealthFishDetour?.Undo(); } catch { }
                stealthFishDetour = null;
                stealthFishTrampoline = null;
                this.stealthFishHookTried = true;
                FeatureLog.Fail("StealthFish", "hook install failed: " + ex.Message + " — feature off.");
                return true;
            }
        }

        private static void EnsureStealthFishingBlockedKeys()
        {
            if (stealthFishBlockedKeys != null)
            {
                return;
            }

            HashSet<int> keys = new HashSet<int>();
            for (int i = 0; i < StealthFishFishingFieldCount; i++)
            {
                keys.Add(StealthFishSyncFieldId(StealthFishOpFishingStatus, i));
            }
            for (int i = 0; i < StealthFishEquipFieldCount; i++)
            {
                keys.Add(StealthFishSyncFieldId(StealthFishOpEquipStatus, i));
            }
            for (int i = 0; i < StealthFishShowOffFieldCount; i++)
            {
                keys.Add(StealthFishSyncFieldId(StealthFishOpShowOffStatus, i));
            }
            keys.Add(StealthFishSyncFieldId(StealthFishOpFsmStatus, StealthFishFsmMotionDataField));
            keys.Add(StealthFishSyncFieldId(StealthFishOpCastActionEvent, 0));
            keys.Add(StealthFishSyncFieldId(StealthFishOpQuickActionEvent, 0));

            stealthFishBlockedKeys = keys;
        }

        // SyncFieldId is [StructLayout(Explicit)] { short propertyId @0; short fieldId @2; int id @0 },
        // i.e. little-endian packing of the two shorts into one int.
        private static int StealthFishSyncFieldId(int propertyId, int fieldId)
        {
            return (propertyId & 0xFFFF) | (fieldId << 16);
        }

        // ---- detour body -----------------------------------------------------------------------

        // HARD RULE for this body (same one written on NotifyFloatInWaterDetourBody in
        // HeartopiaComplete.Fishing.cs): it may do static-field reads, raw pointer arithmetic and the
        // forward call to the trampoline -- NOTHING else. No Mono API, no Il2Cpp, no Unity, no
        // FeatureLog/ModLogger, no exceptions across the boundary. We run inside the prologue of a
        // Mono method on the network-send path; a mono_class_* lookup or a logger call from here is
        // an abort with no dump, and the first movement key is enough to hit it.
        //
        // That is why the List layout below is ARITHMETIC + a runtime signature check rather than a
        // mono_field_get_offset probe: the offsets are the fixed Mono object model, and the check
        // proves on every single packet that the object really has that shape before we act on it.
        private static unsafe void StealthFishSendShowActionDetourBody(uint netId, IntPtr changeActions)
        {
            StealthFishShowActionHookDelegate orig = stealthFishTrampoline;

            if (stealthFishHideActive
                && stealthFishBlockedKeys != null
                && StealthFishTryReadFirstActionKey(changeActions, out int actionKey)
                && stealthFishBlockedKeys.Contains(actionKey))
            {
                stealthFishDroppedCount++;
                return; // the packet never leaves this client
            }

            if (orig != null)
            {
                orig(netId, changeActions);
            }
        }

        // Mono object model, all derived from IntPtr.Size (no metadata call):
        //   MonoObject      : vtable | synchronisation                            -> header = 2*S
        //   List<T>         : header | T[] _items | int _size | int _version      -> _items @2*S, _size @3*S
        //   T[] (MonoArray) : header | MonoArrayBounds* bounds | uintptr max_length | vector
        //                                                                         -> max_length @3*S, vector @4*S
        //   CharacterSyncAction { int actionKey; BytesSegment data; }             -> actionKey @0
        //
        // SIGNATURE CHECK instead of metadata: NetSender.SendMsg is the only caller and it does
        // _actions.Clear() + _actions.Add(one), so _size is ALWAYS exactly 1 and max_length is at
        // least 1. If either fails, the object is not the shape we assumed and we pass the packet
        // through untouched rather than dereferencing further. Element 0 is the only element that
        // exists, so we never need the element stride.
        private static unsafe bool StealthFishTryReadFirstActionKey(IntPtr list, out int actionKey)
        {
            actionKey = 0;

            if (!StealthFishLooksLikeObjectPointer(list))
            {
                return false;
            }

            int pointerSize = IntPtr.Size;
            byte* listPtr = (byte*)list;

            int size = *(int*)(listPtr + (pointerSize * 3));
            if (size != 1)
            {
                return false; // not the one-action shape SendMsg always produces
            }

            IntPtr items = *(IntPtr*)(listPtr + (pointerSize * 2));
            if (!StealthFishLooksLikeObjectPointer(items))
            {
                return false;
            }

            byte* itemsPtr = (byte*)items;
            long maxLength = pointerSize == 8
                ? *(long*)(itemsPtr + (pointerSize * 3))
                : *(int*)(itemsPtr + (pointerSize * 3));
            if (maxLength < 1L || maxLength > StealthFishMaxPlausibleArrayLength)
            {
                return false; // not a MonoArray at that address
            }

            actionKey = *(int*)(itemsPtr + (pointerSize * 4));

            // ShowActionLengthConfig.IsValidActionData refuses negatives, so the game itself never
            // sends one; a negative here means we read garbage -> pass the packet through.
            return actionKey >= 0;
        }

        // Every dereference above goes through this first: a null, low or unaligned address is
        // certainly not a managed object and reading it would AV the process uncatchably.
        private static bool StealthFishLooksLikeObjectPointer(IntPtr obj)
        {
            if (obj == IntPtr.Zero)
            {
                return false;
            }

            ulong address = (ulong)obj.ToInt64();
            return address >= 0x10000UL && (address & (ulong)(IntPtr.Size - 1)) == 0UL;
        }
    }
}
