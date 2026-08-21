using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    // ============================================================================================
    // Emote Unlock — every solo emote and face emote usable, including the ones the account never
    // bought.
    //
    // ── WHY THIS IS TWO HALVES ───────────────────────────────────────────────────────────────────
    // An emote is a request/response handshake, not a local animation:
    //
    //   EmojiPanel -> EmotionCommand -> SocialReqTask.OnStart
    //        sends SendSingleActionNetworkCommand{Id, IsLoopSingle}
    //   server echoes PlayerSocialEvent -> SocialReqTask.OnResp plays it LOCALLY:
    //        loop     -> PlayerStateSingleShow.SetData(true, id, loop)
    //        one-shot -> player.Cast(PlayerSocialActionArg{socialType})
    //
    // The local half is what animates, and it is CLIENT-SIDE — measured, and confirmed by a second
    // client: a locked emote played this way is visible to other players too, because
    // PlayerSingleShowStatus / CastActionEvent replicate on the client's word with no ownership
    // check. But the game will not run that half for a locked id: the server never echoes, so
    // SocialReqTask spins for 2 s and gives up. Hence:
    //
    //   HALF 1 (panel)  EmojiPanel builds its rows from ExpressionActionProtocolManager
    //                   .GetExpressionActions() -> ExpressionActionClientService
    //                   .ExpressionActionComponent(), an EcsFilter over the entities the SERVER
    //                   synced. It never consults IsExpressionActionObtained, so flipping that flag
    //                   adds no rows. We hand the panel a prebuilt list containing every id instead.
    //   HALF 2 (click)  SendSingleAction is intercepted; the mod re-sends it and runs the local half
    //                   itself on the next frame, without waiting for an echo that will not come.
    //
    // ── WHY THE HOOKS LOOK LIKE THIS ─────────────────────────────────────────────────────────────
    // Both native hooks are CALLBACK-FREE: one returns a pointer that was built earlier on the main
    // thread, the other only records two scalars into static fields. Neither calls back into game
    // Mono. That restriction is not stylistic — a hook that re-entered Mono from the reverse-pinvoke
    // callback froze and then killed the process once already (see the free-build post-mortem), so
    // every hook here is the "constant return / record and return" shape, installed and removed with
    // NativeDetour.Apply()/Undo() rather than branching to a trampoline inside the callback.
    //
    // These are Mono-JIT detours, NOT GameAssembly .text patches — the anti-cheat surface rule holds.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        internal static bool MasterLogEmoteUnlock = false;

        // ExpressionActionType: the panel buckets by this. 1 = Expression (face), 2 = SingleAction.
        private const int EmoteUnlockTypeExpression = 1;
        private const int EmoteUnlockTypeSingleAction = 2;

        // The tables are contiguous from 1; anything the game's table does not know is skipped, so a
        // shorter table in a future patch just yields fewer rows instead of broken cells.
        private const int EmoteUnlockMaxSingleActionId = 400;
        private const int EmoteUnlockMaxExpressionId = 400;

        private bool emoteUnlockEnabled;
        private bool emoteUnlockActive;          // detours currently applied
        private bool emoteUnlockBuildTried;      // list build attempted for this world
        private int emoteUnlockWorldEpoch = -1;
        private string emoteUnlockStatus = "Idle.";
        private int emoteUnlockListedSingle;
        private int emoteUnlockListedExpression;

        // The fabricated owned-list, kept alive by a mono GC handle so the hook can hand the same
        // pointer back every call without the collector moving it.
        private static IntPtr emoteUnlockListObj = IntPtr.Zero;
        private static uint emoteUnlockListHandle;

        private static MonoMod.RuntimeDetour.NativeDetour emoteUnlockListDetour;
        private static MonoMod.RuntimeDetour.NativeDetour emoteUnlockSendDetour;
        private static MonoMod.RuntimeDetour.NativeDetour emoteUnlockFaceDetour;
        private static EmoteUnlockListHookDelegate emoteUnlockListHookDelegate;   // anti-GC
        private static EmoteUnlockSendHookDelegate emoteUnlockSendHookDelegate;   // anti-GC
        private static EmoteUnlockFaceHookDelegate emoteUnlockFaceHookDelegate;   // anti-GC
        private static EmoteUnlockCompileMethodDelegate emoteUnlockCompileMethod;

        // ExpressionActionComponent() is an instance method returning a reference — so the native
        // signature is just (this) -> object*.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr EmoteUnlockListHookDelegate(IntPtr self);

        // CharacterProtocolManager.SendSingleAction(int, bool) is static and void.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EmoteUnlockSendHookDelegate(int socialType, bool isLoopSingle);

        // CharacterProtocolManager.SendEmotion(int) — the face-emote command, a different path that
        // the body-emote hook never saw.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EmoteUnlockFaceHookDelegate(int emojiId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr EmoteUnlockCompileMethodDelegate(IntPtr method);

        // Set by the send hook, drained on the main thread. Volatile: written from whatever thread
        // the game happens to send on, read from OnUpdate.
        private static volatile int emoteUnlockPendingId;
        private static volatile bool emoteUnlockPendingLoop;
        private static volatile bool emoteUnlockPendingSet;
        private static volatile int emoteUnlockPendingFaceId;
        private static volatile bool emoteUnlockPendingFaceSet;

        public bool EmoteUnlockEnabled
        {
            get { return this.emoteUnlockEnabled; }
            set { this.emoteUnlockEnabled = value; }
        }

        public string EmoteUnlockStatus
        {
            get { return this.emoteUnlockStatus; }
        }

        private void ProcessEmoteUnlockOnUpdate()
        {
            // A world change invalidates the fabricated list (its elements are mono objects from the
            // previous world) and the JIT entries the detours sit on.
            if (this.emoteUnlockWorldEpoch != this.WorldReadyEpoch)
            {
                this.emoteUnlockWorldEpoch = this.WorldReadyEpoch;
                this.EmoteUnlockTeardown("world changed");
                this.emoteUnlockBuildTried = false;
            }

            if (!this.emoteUnlockEnabled)
            {
                if (this.emoteUnlockActive)
                {
                    this.EmoteUnlockTeardown("toggle off");
                }

                return;
            }

            if (!this.IsWorldReady)
            {
                return;
            }

            if (!this.emoteUnlockActive && !this.emoteUnlockBuildTried)
            {
                this.EnsureEmoteUnlockInstalled();
            }

            this.DrainEmoteUnlockPending();
        }

        // ── half 2: the click ───────────────────────────────────────────────────────────────────
        //
        // The hook swallowed the game's send, so the mod owns the whole emote from here: send the
        // command for real (the server still decides whether to grant the state for an owned id) and
        // run the local half immediately instead of waiting for an echo a locked id never gets.
        private void DrainEmoteUnlockPending()
        {
            if (emoteUnlockPendingFaceSet)
            {
                int faceId = emoteUnlockPendingFaceId;
                emoteUnlockPendingFaceSet = false;
                try
                {
                    this.TryAuraSendCommand(
                        "XDT.Scene.Shared.Modules.ExpressionAction.SendExpressionNetworkCommand",
                        new Dictionary<string, object> { { "Id", faceId } },
                        AuraChannelReliable, true, out string faceSend);
                    bool shown = this.TryEmoteUnlockShowFace(faceId, out string faceStatus);
                    this.emoteUnlockStatus = "Face " + faceId + (shown ? " shown" : " — " + faceStatus);
                    if (MasterLogEmoteUnlock)
                    {
                        ModLogger.Msg("[EmoteUnlock] face=" + faceId + " send=" + faceSend + " local=" + faceStatus);
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[EmoteUnlock] face " + faceId + " threw: " + ex.Message);
                }
            }

            if (!emoteUnlockPendingSet)
            {
                return;
            }

            int id = emoteUnlockPendingId;
            bool loop = emoteUnlockPendingLoop;
            emoteUnlockPendingSet = false;

            try
            {
                Dictionary<string, object> fields = new Dictionary<string, object>
                {
                    { "Id", id },
                    { "IsLoopSingle", loop },
                };
                this.TryAuraSendCommand(
                    "XDT.Scene.Shared.Modules.ExpressionAction.SendSingleActionNetworkCommand",
                    fields, AuraChannelReliable, true, out string sendStatus);

                // SocialReqTask decides with `singleaction.isLoop != 0 || resp.isLoopSingle`, i.e. the
                // TABLE has the last word — the panel passes isLoopSingle=false for a looping emote
                // clicked from the normal tab. Using only the caller's flag sent every loop emote
                // down the one-shot Cast path, which is accepted and animates NOTHING.
                bool tableLoop = this.EmoteUnlockTableIsLoop(id);
                bool useLoop = loop || tableLoop;
                bool played = useLoop
                    ? this.TryEmoteUnlockSetSingleShow(true, id, true, out string playStatus)
                    : this.TryEmoteUnlockCastSocial(id, out playStatus);

                this.emoteUnlockStatus = "Played " + id + (useLoop ? " (loop)" : string.Empty)
                                         + (played ? string.Empty : " — local half failed");
                if (MasterLogEmoteUnlock || !played)
                {
                    ModLogger.Msg("[EmoteUnlock] id=" + id + " loop=" + useLoop + " (table=" + tableLoop
                                  + ", arg=" + loop + ") send=" + sendStatus + " local=" + playStatus);
                }
            }
            catch (Exception ex)
            {
                this.emoteUnlockStatus = "Play failed: " + ex.Message;
                ModLogger.Msg("[EmoteUnlock] play id=" + id + " threw: " + ex);
            }
        }

        // player.Cast(new PlayerSocialActionArg{ socialType = id }) — the one-shot half.
        private unsafe bool TryEmoteUnlockCastSocial(int socialType, out string status)
        {
            status = "unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoObjectNew == null || auraMonoFieldSetValue == null
                || auraMonoClassGetFieldFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr argClass = this.FindAuraMonoClassInAllLoadedImages("PlayerSocialActionArg", "XDTLevelAndEntity.Gameplay.Action");
            IntPtr playerClass = this.FindAuraMonoClassInAllLoadedImages("LocalPlayerComponent", "XDTLevelAndEntity.Gameplay.Component.Player");
            if (argClass == IntPtr.Zero || playerClass == IntPtr.Zero)
            {
                status = "class resolve failed";
                return false;
            }

            IntPtr castMethod = this.FindAuraMonoMethodOnHierarchy(playerClass, "Cast", 1);
            IntPtr field = auraMonoClassGetFieldFromName(argClass, "socialType");
            if (castMethod == IntPtr.Zero || field == IntPtr.Zero)
            {
                status = "Cast/socialType missing";
                return false;
            }

            List<uint> pins = new List<uint>();
            if (!this.TryAuraMonoGetComponentObjects(playerClass, out List<IntPtr> players, pins)
                || players == null || players.Count == 0)
            {
                FreeAuraMonoPins(pins);
                status = "no local player";
                return false;
            }

            try
            {
                IntPtr argObj = auraMonoObjectNew(this.auraMonoRootDomain, argClass);
                if (argObj == IntPtr.Zero)
                {
                    status = "alloc failed";
                    return false;
                }

                auraMonoFieldSetValue(argObj, field, (IntPtr)(&socialType));

                IntPtr* args = stackalloc IntPtr[1];
                args[0] = argObj;
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(castMethod, players[0], (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "Cast threw 0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                status = "cast ok";
                return true;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        // PlayerStateSingleShow.SetData(isShow, socialType, isLoop) — the held-pose half, reached the
        // way LocalPlayerComponent reaches it: character -> bodyFsMachine -> GetState(PlayerState).
        // The NON-generic GetState(PlayerState) overload, because GetState<T>() would need generic
        // inflation.
        private unsafe bool TryEmoteUnlockSetSingleShow(bool isShow, int socialType, bool isLoop, out string status)
        {
            status = "unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr playerClass = this.FindAuraMonoClassInAllLoadedImages("LocalPlayerComponent", "XDTLevelAndEntity.Gameplay.Component.Player");
            IntPtr fsmClass = this.FindAuraMonoClassInAllLoadedImages("PlayerFsMachine", "XDTLevelAndEntity.Game.GameMode");
            IntPtr showClass = this.FindAuraMonoClassInAllLoadedImages("PlayerStateSingleShow", "XDTLevelAndEntity.Gameplay.Component.Player");
            if (playerClass == IntPtr.Zero || fsmClass == IntPtr.Zero || showClass == IntPtr.Zero)
            {
                status = "class resolve failed";
                return false;
            }

            IntPtr getCharacter = this.FindAuraMonoMethodOnHierarchy(playerClass, "get_character", 0);
            IntPtr getState = this.FindAuraMonoMethodOnHierarchy(fsmClass, "GetState", 1);
            IntPtr setData = this.FindAuraMonoMethodOnHierarchy(showClass, "SetData", 3);
            IntPtr fsmField = auraMonoClassGetFieldFromName == null
                ? IntPtr.Zero
                : auraMonoClassGetFieldFromName(this.FindAuraMonoClassInAllLoadedImages("Character", "XDTLevelAndEntity.Game.GameMode"), "bodyFsMachine");
            if (getCharacter == IntPtr.Zero || getState == IntPtr.Zero || setData == IntPtr.Zero || fsmField == IntPtr.Zero)
            {
                status = "method/field resolve failed";
                return false;
            }

            List<uint> pins = new List<uint>();
            if (!this.TryAuraMonoGetComponentObjects(playerClass, out List<IntPtr> players, pins)
                || players == null || players.Count == 0)
            {
                FreeAuraMonoPins(pins);
                status = "no local player";
                return false;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr character = auraMonoRuntimeInvoke(getCharacter, players[0], IntPtr.Zero, ref exc);
                if (character == IntPtr.Zero || exc != IntPtr.Zero)
                {
                    status = "character unavailable";
                    return false;
                }

                IntPtr fsm = IntPtr.Zero;
                if (auraMonoFieldGetValue != null)
                {
                    auraMonoFieldGetValue(character, fsmField, (IntPtr)(&fsm));
                }

                if (fsm == IntPtr.Zero)
                {
                    status = "bodyFsMachine unreadable";
                    return false;
                }

                int stateSingleShow = 41; // PlayerState.SingleShow
                IntPtr* stateArgs = stackalloc IntPtr[1];
                stateArgs[0] = (IntPtr)(&stateSingleShow);
                exc = IntPtr.Zero;
                IntPtr state = auraMonoRuntimeInvoke(getState, fsm, (IntPtr)stateArgs, ref exc);
                if (state == IntPtr.Zero || exc != IntPtr.Zero)
                {
                    status = "SingleShow state unavailable";
                    return false;
                }

                byte show = isShow ? (byte)1 : (byte)0;
                byte loop = isLoop ? (byte)1 : (byte)0;
                int type = socialType;
                IntPtr* setArgs = stackalloc IntPtr[3];
                setArgs[0] = (IntPtr)(&show);
                setArgs[1] = (IntPtr)(&type);
                setArgs[2] = (IntPtr)(&loop);
                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(setData, state, (IntPtr)setArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "SetData threw 0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                status = "pose ok";
                return true;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        // TableSingleaction.isLoop for an id — the same field SocialReqTask consults.
        private unsafe bool EmoteUnlockTableIsLoop(int id)
        {
            IntPtr tableData = this.FindAuraMonoClassInAllLoadedImages("TableData", string.Empty);
            IntPtr getter = tableData == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(tableData, "GetSingleaction", 2);
            if (getter == IntPtr.Zero || auraMonoRuntimeInvoke == null || auraMonoFieldGetValue == null
                || auraMonoObjectGetClass == null || auraMonoClassGetFieldFromName == null)
            {
                return false;
            }

            int probe = id;
            byte needException = 0;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&probe);
            args[1] = (IntPtr)(&needException);
            IntPtr exc = IntPtr.Zero;
            IntPtr row = auraMonoRuntimeInvoke(getter, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || row == IntPtr.Zero)
            {
                return false;
            }

            IntPtr rowClass = auraMonoObjectGetClass(row);
            if (rowClass == IntPtr.Zero)
            {
                return false;
            }

            // `isLoop` is a PROPERTY (`public int isLoop => _isLoop;`) over a private BYTE backing
            // field — asking for a field named "isLoop" returns nothing, and reading the backing
            // field as an int would pull 4 bytes for a 1-byte value. Field first, getter as fallback.
            IntPtr field = auraMonoClassGetFieldFromName(rowClass, "_isLoop");
            if (field != IntPtr.Zero)
            {
                byte isLoopByte = 0;
                auraMonoFieldGetValue(row, field, (IntPtr)(&isLoopByte));
                return isLoopByte != 0;
            }

            IntPtr getter2 = this.FindAuraMonoMethodOnHierarchy(rowClass, "get_isLoop", 0);
            if (getter2 == IntPtr.Zero)
            {
                ModLogger.Msg("[EmoteUnlock] TableSingleaction._isLoop / get_isLoop unresolved — "
                              + "looping emotes will fall back to the one-shot path");
                return false;
            }

            IntPtr exc2 = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getter2, row, IntPtr.Zero, ref exc2);
            if (exc2 != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            int unboxed = 0;
            byte* raw = (byte*)boxed + (2 * IntPtr.Size);
            unboxed = *(int*)raw;
            return unboxed != 0;
        }

        // A face emote has NO local half in the game: the client draws the bubble off the server's
        // echo (ExpressionOneFrameComponent -> SocialFaceEvent -> EmojiSystem). A locked id never
        // echoes, so the mod dispatches that same event itself.
        //
        // ⚠️ Unlike a body emote, this is LOCAL ONLY. Body emotes replicate because
        // PlayerSingleShowStatus / CastActionEvent are client-written networked statuses; the face
        // bubble has no such channel, so other players will not see a locked expression.
        private unsafe bool TryEmoteUnlockShowFace(int emojiId, out string status)
        {
            status = "unavailable";
            if (auraMonoRuntimeInvoke == null || auraMonoClassGetType == null
                || auraMonoMetadataGetGenericInst == null || auraMonoClassInflateGenericMethod == null)
            {
                return false;
            }

            IntPtr eventCenter = this.FindAuraMonoClassInAllLoadedImages("EventCenter", "XDTGame.Core");
            IntPtr eventClass = this.FindAuraMonoClassInAllLoadedImages("SocialFaceEvent", "XDTDataAndProtocol.Events.Player");
            if (eventCenter == IntPtr.Zero || eventClass == IntPtr.Zero)
            {
                status = "EventCenter/SocialFaceEvent missing";
                return false;
            }

            IntPtr openMethod = this.FindAuraMonoMethodOnHierarchy(eventCenter, "DispatchEvent", 1);
            if (openMethod == IntPtr.Zero
                || !this.TryInflateDispatchForEvent(openMethod, eventClass, 1, out IntPtr dispatch))
            {
                status = "DispatchEvent<SocialFaceEvent> inflate failed";
                return false;
            }

            if (!this.TryResolveSelfPlayerNetId(out uint selfNetId) || selfNetId == 0U)
            {
                status = "self netId unavailable";
                return false;
            }

            // struct SocialFaceEvent { uint netId; int emojiId; } — 8 bytes, natural layout. The
            // parameter is `in T`, so the slot holds a pointer to the value.
            byte* payload = stackalloc byte[8];
            *(uint*)payload = selfNetId;
            *(int*)(payload + 4) = emojiId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)payload;
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(dispatch, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "dispatch threw 0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "face shown (local only)";
            return true;
        }

        // ── install / teardown ──────────────────────────────────────────────────────────────────

        private void EnsureEmoteUnlockInstalled()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // AuraMono not up yet — retry next frame, do not burn the tried flag.
                }

                if (!this.TryBuildEmoteUnlockList(out string buildStatus))
                {
                    this.emoteUnlockStatus = "List build failed: " + buildStatus;
                    this.emoteUnlockBuildTried = true;
                    ModLogger.Msg("[EmoteUnlock] " + this.emoteUnlockStatus);
                    return;
                }

                if (!this.TryInstallEmoteUnlockDetours(out string detourStatus))
                {
                    this.emoteUnlockStatus = "Detour failed: " + detourStatus;
                    this.emoteUnlockBuildTried = true;
                    ModLogger.Msg("[EmoteUnlock] " + this.emoteUnlockStatus);
                    return;
                }

                this.emoteUnlockActive = true;
                this.emoteUnlockBuildTried = true;
                this.emoteUnlockStatus = "Active — " + this.emoteUnlockListedSingle + " actions, "
                                         + this.emoteUnlockListedExpression + " expressions listed.";
                ModLogger.Msg("[EmoteUnlock] " + this.emoteUnlockStatus);
            }
            catch (Exception ex)
            {
                this.emoteUnlockBuildTried = true;
                this.emoteUnlockStatus = "Install threw: " + ex.Message;
                ModLogger.Msg("[EmoteUnlock] install threw: " + ex);
            }
        }

        private void EmoteUnlockTeardown(string reason)
        {
            try
            {
                if (emoteUnlockListDetour != null)
                {
                    emoteUnlockListDetour.Undo();
                    emoteUnlockListDetour.Dispose();
                    emoteUnlockListDetour = null;
                }

                if (emoteUnlockSendDetour != null)
                {
                    emoteUnlockSendDetour.Undo();
                    emoteUnlockSendDetour.Dispose();
                    emoteUnlockSendDetour = null;
                }

                if (emoteUnlockFaceDetour != null)
                {
                    emoteUnlockFaceDetour.Undo();
                    emoteUnlockFaceDetour.Dispose();
                    emoteUnlockFaceDetour = null;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[EmoteUnlock] teardown threw: " + ex.Message);
            }

            if (emoteUnlockListHandle != 0 && auraMonoGcHandleFree != null)
            {
                try
                {
                    auraMonoGcHandleFree(emoteUnlockListHandle);
                }
                catch (Exception)
                {
                }
            }

            emoteUnlockListHandle = 0;
            emoteUnlockListObj = IntPtr.Zero;
            emoteUnlockPendingSet = false;
            emoteUnlockPendingFaceSet = false;

            if (this.emoteUnlockActive)
            {
                this.emoteUnlockStatus = "Inactive (" + reason + ").";
                if (MasterLogEmoteUnlock)
                {
                    ModLogger.Msg("[EmoteUnlock] torn down: " + reason);
                }
            }

            this.emoteUnlockActive = false;
        }

        // Builds List<ExpressionActionComponent> holding every id the tables know, on the MAIN
        // THREAD (allocation is fine here; it is inside the native callback that it would not be).
        private unsafe bool TryBuildEmoteUnlockList(out string status)
        {
            status = "unavailable";
            if (auraMonoObjectNew == null || auraMonoRuntimeInvoke == null || auraMonoFieldSetValue == null
                || auraMonoClassGetFieldFromName == null || auraMonoGcHandleNew == null)
            {
                return false;
            }

            IntPtr componentClass = this.FindAuraMonoClassInAllLoadedImages("ExpressionActionComponent", "XDT.Scene.Shared.Modules.ExpressionAction");
            if (componentClass == IntPtr.Zero)
            {
                status = "ExpressionActionComponent class missing";
                return false;
            }

            IntPtr listObj = this.TryCreateEmoteUnlockList();
            if (listObj == IntPtr.Zero)
            {
                status = "List<ExpressionActionComponent> create failed";
                return false;
            }

            IntPtr listClass = auraMonoObjectGetClass == null ? IntPtr.Zero : auraMonoObjectGetClass(listObj);
            if (listClass == IntPtr.Zero)
            {
                status = "list class unresolved";
                return false;
            }

            IntPtr addMethod = this.FindAuraMonoMethodOnHierarchy(listClass, "Add", 1);
            IntPtr typeField = auraMonoClassGetFieldFromName(componentClass, "ExpressionActionType");
            IntPtr idField = auraMonoClassGetFieldFromName(componentClass, "Id");
            if (addMethod == IntPtr.Zero || typeField == IntPtr.Zero || idField == IntPtr.Zero)
            {
                status = "Add/fields missing";
                return false;
            }

            uint listHandle = auraMonoGcHandleNew(listObj, true);
            this.emoteUnlockListedSingle = this.AppendEmoteUnlockRows(
                listObj, addMethod, componentClass, typeField, idField,
                EmoteUnlockTypeSingleAction, EmoteUnlockMaxSingleActionId, "GetSingleaction");
            this.emoteUnlockListedExpression = this.AppendEmoteUnlockRows(
                listObj, addMethod, componentClass, typeField, idField,
                EmoteUnlockTypeExpression, EmoteUnlockMaxExpressionId, "GetExpression");

            if (this.emoteUnlockListedSingle + this.emoteUnlockListedExpression == 0)
            {
                if (auraMonoGcHandleFree != null)
                {
                    auraMonoGcHandleFree(listHandle);
                }

                status = "no rows built";
                return false;
            }

            emoteUnlockListObj = listObj;
            emoteUnlockListHandle = listHandle;
            status = "ok";
            return true;
        }

        // One row per id the game's own table accepts — TableData.GetXxx(id) returning null means the
        // id does not exist on this build, and a row the panel cannot look up would break its cell.
        private unsafe int AppendEmoteUnlockRows(IntPtr listObj, IntPtr addMethod, IntPtr componentClass,
                                                 IntPtr typeField, IntPtr idField,
                                                 int actionType, int maxId, string tableGetter)
        {
            // TableData sits in the GLOBAL namespace, and its getters take TWO parameters —
            // GetSingleaction(int id, bool needException = false). Asking for arity 1 silently found
            // nothing, which skipped validation entirely and listed ids 161-400 / 184-400 that no
            // table row backs; the panel then had 800 rows it could not look up and drew none.
            IntPtr tableData = this.FindAuraMonoClassInAllLoadedImages("TableData", string.Empty);
            IntPtr getter = tableData == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(tableData, tableGetter, 2);
            if (getter == IntPtr.Zero)
            {
                // Fail CLOSED: an unvalidated list is worse than no list, because it breaks the panel
                // instead of merely leaving it as it was.
                ModLogger.Msg("[EmoteUnlock] " + tableGetter + "(int,bool) unresolved (TableData="
                              + (tableData != IntPtr.Zero) + ") — refusing to list unvalidated ids");
                return 0;
            }

            int added = 0;

            // Argument slots are allocated ONCE: a stackalloc inside a 400-iteration loop is a real
            // stack-overflow risk (CA2014), not a style nit.
            IntPtr* probeArgs = stackalloc IntPtr[2];
            IntPtr* addArgs = stackalloc IntPtr[1];
            int probe = 0;
            int rowId = 0;
            int type = actionType;
            byte needException = 0;

            for (int id = 1; id <= maxId; id++)
            {
                probe = id;
                probeArgs[0] = (IntPtr)(&probe);
                probeArgs[1] = (IntPtr)(&needException);
                IntPtr probeExc = IntPtr.Zero;
                IntPtr row = auraMonoRuntimeInvoke(getter, IntPtr.Zero, (IntPtr)probeArgs, ref probeExc);
                if (probeExc != IntPtr.Zero || row == IntPtr.Zero)
                {
                    continue; // no such row on this build
                }

                // UNFINISHED ROWS — the ones the artists have not drawn yet.
                //
                // The panel's cell icon is an atlas sprite named after the id
                // (AtlasSpriteUtility.GetSingleActionItemIcon -> "singleaction_{id}", which
                // DynamicAtlasProxy loads as "ui_item_normal_singleaction_{id}"). A row whose sprite
                // was never shipped comes up as a blank cell, and in practice its animator state is
                // missing too, so it plays whatever the controller falls back to — the last row that
                // does have a clip. That is the "five blank buttons that all play the same thing" at
                // the end of the list.
                //
                // ResManager.HasAsset is a synchronous index lookup, so this costs one dictionary hit
                // per id and needs no atlas load.
                //
                // FAILS OPEN, unlike the table probe above: if the resolver is unavailable the answer
                // is "unknown", and dropping every row on an unknown would empty the panel — far
                // worse than listing a few blanks.
                if (!this.EmoteUnlockRowHasIcon(actionType, id))
                {
                    continue;
                }

                IntPtr boxed = auraMonoObjectNew(this.auraMonoRootDomain, componentClass);
                if (boxed == IntPtr.Zero)
                {
                    break;
                }

                rowId = id;
                auraMonoFieldSetValue(boxed, typeField, (IntPtr)(&type));
                auraMonoFieldSetValue(boxed, idField, (IntPtr)(&rowId));

                // Value-type element: Add wants a pointer to the raw struct, which for a boxed value
                // sits one object header (2 pointers) past the handle.
                addArgs[0] = boxed + (2 * IntPtr.Size);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(addMethod, listObj, (IntPtr)addArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    break;
                }

                added++;
            }

            return added;
        }

        // Is the panel's cell icon for this row actually shipped?
        //
        // Sprite names come from AtlasSpriteUtility — "singleaction_{id}" and "expression_{id}"
        // (999 is "expression_like"). BuildGameIconLoadKey adds the atlas prefix the dynamic
        // NormalItem atlas loads them under, so that literal stays in the icon pipeline where the
        // rest of the mod already keeps it.
        //
        // True when the check cannot be made at all — see the caller: unknown must not mean "drop".
        private bool EmoteUnlockRowHasIcon(int actionType, int id)
        {
            if (!this.EnsureGameIconResManager())
            {
                return true;
            }

            string sprite;
            if (actionType == EmoteUnlockTypeSingleAction)
            {
                sprite = "singleaction_" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (actionType == EmoteUnlockTypeExpression)
            {
                sprite = "expression_"
                    + (id == 999 ? "like" : id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                return true;
            }

            return this.GameIconHasAsset(BuildGameIconLoadKey(sprite));
        }

        // Inflating List<T> for a game struct has one working route in this mod: build it through the
        // BCL — Type.GetType(assembly-qualified name) then Activator.CreateInstance — and read the
        // class off the instance. Same shape the daily-quest submit path uses.
        private unsafe IntPtr TryCreateEmoteUnlockList()
        {
            string[] candidates =
            {
                "System.Collections.Generic.List`1[[XDT.Scene.Shared.Modules.ExpressionAction.ExpressionActionComponent, EcsClient]]",
                "System.Collections.Generic.List`1[[XDT.Scene.Shared.Modules.ExpressionAction.ExpressionActionComponent, XDTDataAndProtocol]]",
                "System.Collections.Generic.List`1[[XDT.Scene.Shared.Modules.ExpressionAction.ExpressionActionComponent, Assembly-CSharp]]",
            };

            if (auraMonoStringNew == null || auraMonoRuntimeInvoke == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr* typeArgs = stackalloc IntPtr[1];
            IntPtr* createArgs = stackalloc IntPtr[1];
            for (int i = 0; i < candidates.Length; i++)
            {
                IntPtr nameObj = auraMonoStringNew(this.auraMonoRootDomain, candidates[i]);
                if (nameObj == IntPtr.Zero)
                {
                    continue;
                }

                typeArgs[0] = nameObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                {
                    continue;
                }

                createArgs[0] = typeObj;
                exc = IntPtr.Zero;
                IntPtr obj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)createArgs, ref exc);
                if (exc == IntPtr.Zero && obj != IntPtr.Zero)
                {
                    return obj;
                }
            }

            return IntPtr.Zero;
        }

        private bool TryInstallEmoteUnlockDetours(out string status)
        {
            status = "unavailable";

            IntPtr serviceClass = this.FindAuraMonoClassInAllLoadedImages("ExpressionActionClientService", "ClientSystem.ExpressionAction");
            IntPtr protocolClass = this.FindAuraMonoClassInAllLoadedImages("CharacterProtocolManager", "XDTDataAndProtocol.ProtocolService.GamePlay.Character");
            if (serviceClass == IntPtr.Zero || protocolClass == IntPtr.Zero)
            {
                status = "service/protocol class missing";
                return false;
            }

            IntPtr listMethod = this.FindAuraMonoMethodOnHierarchy(serviceClass, "ExpressionActionComponent", 0);
            IntPtr sendMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "SendSingleAction", 2);
            IntPtr faceMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "SendEmotion", 1);
            if (listMethod == IntPtr.Zero || sendMethod == IntPtr.Zero || faceMethod == IntPtr.Zero)
            {
                status = "target methods missing (list=" + (listMethod != IntPtr.Zero)
                         + " send=" + (sendMethod != IntPtr.Zero) + " face=" + (faceMethod != IntPtr.Zero) + ")";
                return false;
            }

            if (emoteUnlockCompileMethod == null)
            {
                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                if (monoModule != IntPtr.Zero)
                {
                    emoteUnlockCompileMethod = this.GetAuraMonoExport<EmoteUnlockCompileMethodDelegate>(monoModule, "mono_compile_method");
                }
            }

            if (emoteUnlockCompileMethod == null)
            {
                status = "mono_compile_method unavailable";
                return false;
            }

            IntPtr listPtr = emoteUnlockCompileMethod(listMethod);
            IntPtr sendPtr = emoteUnlockCompileMethod(sendMethod);
            IntPtr facePtr = emoteUnlockCompileMethod(faceMethod);
            if (listPtr == IntPtr.Zero || sendPtr == IntPtr.Zero || facePtr == IntPtr.Zero)
            {
                status = "compile returned null";
                return false;
            }

            emoteUnlockListHookDelegate = EmoteUnlockListNative;
            emoteUnlockSendHookDelegate = EmoteUnlockSendNative;
            emoteUnlockListDetour = new MonoMod.RuntimeDetour.NativeDetour(listPtr, emoteUnlockListHookDelegate);
            emoteUnlockSendDetour = new MonoMod.RuntimeDetour.NativeDetour(sendPtr, emoteUnlockSendHookDelegate);
            emoteUnlockFaceHookDelegate = EmoteUnlockFaceNative;
            emoteUnlockFaceDetour = new MonoMod.RuntimeDetour.NativeDetour(facePtr, emoteUnlockFaceHookDelegate);
            status = "installed";
            return true;
        }

        // ── the hooks: callback-free by construction ────────────────────────────────────────────

        // Returns the list built earlier on the main thread. No allocation, no Mono call — just a
        // pointer the GC handle is holding still.
        private static IntPtr EmoteUnlockListNative(IntPtr self)
        {
            return emoteUnlockListObj;
        }

        // Swallows the game's send and records it. Everything real happens on the main thread in
        // DrainEmoteUnlockPending, so nothing here re-enters Mono.
        private static void EmoteUnlockSendNative(int socialType, bool isLoopSingle)
        {
            emoteUnlockPendingId = socialType;
            emoteUnlockPendingLoop = isLoopSingle;
            emoteUnlockPendingSet = true;
        }

        private static void EmoteUnlockFaceNative(int emojiId)
        {
            emoteUnlockPendingFaceId = emojiId;
            emoteUnlockPendingFaceSet = true;
        }
    }
}
