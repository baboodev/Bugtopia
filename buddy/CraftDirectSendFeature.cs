using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Direct Craft Send — the Craft button just sends the network command and the panel closes.
    //
    // This is the "don't play the sequence at all" alternative to Skip Craft / Dye Animations. The
    // craft button already closes both panels BEFORE it submits:
    //
    //     CraftCompositeDetailPanel._Make()
    //         M<IUIManager>.Inst.CloseView(this, playCloseAni: false);
    //         M<IUIManager>.Inst.CloseView<CraftCompositeMainPanel>();
    //         PlayerInteraction.ExecuteClickCommand<MakeItemEvent>(new MakeItemEvent { … });
    //
    // …so refusing the command leaves exactly the requested behaviour: panel closed, command sent
    // by us, nothing else. No occupancy, no Generic_MoveStride walk to the bench, no
    // PlayerCraftsmanAction — none of it is ever created, because PlayerCraftsmanArg is only ever
    // cast from MakeItemCommand's RPC callback.
    //
    // LEVER — the Instant Teleport pattern, already shipped in this mod. ButtonCommand<T>:
    //
    //     public InputResult ExecuteAsync(in T arg) {
    //         int num = IsExecutable(in arg);
    //         if (num == 0) return new InputResult(num, OnExecuteAsync(in arg));
    //         return new InputResult(num, null);
    //     }
    //
    // A Mono NativeDetour on MakeItemCommand.IsExecutable returning a non-zero code makes the
    // interact system skip OnExecuteAsync through the game's own path, and
    // PlayerInteraction.ToastInteractError explicitly ignores InteractErrorCode.Invalid (-1), so
    // the refusal is silent — no error tip.
    //
    // The payload is read from the COMMAND ARGUMENT — the `in MakeItemEvent` pointer the detour
    // receives — which is the one place all three producers (CraftCompositeDetailPanel,
    // DyeColorPanel, DrawSewingPanel) funnel through. Offsets are RESOLVED AT RUNTIME from the
    // struct's own metadata (mono_field_get_offset minus the 2-pointer boxed header, the codebase
    // idiom) and sanity-checked, so a field added by a game update disarms the feature instead of
    // producing a wild read. This matters more than usual here: MakeItemEvent mixes scalars with a
    // List<> reference field, so its layout is NOT safely guessable.
    //
    // SCOPE — plain crafts only. Dye (dyeColor) and artwork overlay (isDrawOverlay) forward to the
    // original: their payload lives in the List<(int, Color32)> colors reference field, which
    // cannot be reconstructed from a native callback without holding a mono object pointer across
    // frames (guaranteed stale-pointer AV). Those keep the vanilla sequence and are handled by the
    // Skip Craft / Dye Animations feature instead.
    //
    // Detour-body rules honoured (memory: mono-only-method-hook-nativedetour): the body only reads
    // scalars off the arg pointer, writes static fields, and either returns a constant or forwards
    // to the trampoline. It never allocates, throws, or calls back into game Mono. The actual
    // WebRequestUtility.SendCommand<MakeItemNetworkCommand> happens on the next main-thread tick.
    //
    // Vanilla checks NOT replicated: MakeItemCommand.IsExecutable also refuses when the focus level
    // object is missing, when the bench owner is in build mode, and when the player is not in free
    // locomotion (e.g. sitting). Reading those needs game-Mono calls, which are forbidden inside the
    // callback, so the direct path submits regardless — the server still validates recipe and
    // materials. Consequence: crafting works while seated, where vanilla would refuse with tip
    // 92001.
    //
    // No reward toast: that is dispatched from PlayerCraftsmanAction.OnBehaveFinish, which never
    // runs here. The item still arrives (server-granted) and the bag refresh fires normally.
    //
    // ⚠ This is a NEW native detour — the project's highest-risk category (memory:
    // auramono-native-hook-and-settings-gotchas — treat every new target as guilty until proven
    // innocent by a live test). It is therefore opt-in, default off, and the detour is Undo()n the
    // moment the toggle goes off, so a user who leaves it alone carries no added surface.
    public partial class HeartopiaComplete
    {
        // InteractErrorCode.Invalid — the one non-zero code ToastInteractError does not toast.
        private const int CraftDirectSendRefuseCode = -1;
        private const int CraftDirectSendMaxStructSpan = 256;
        private const int CraftDirectSendChannelReliable = 1; // ChannelType.Reliable

        // Occupy release. The vanilla craft ALWAYS ends with CharacterProtocolManager
        // .CancelOccupyCommand() — from PlayerCraftsmanAction.OnBehaveFinish on success and from
        // the RPCRespTask timeout callback on failure. Refusing the command skips both, and the
        // bench stays flagged occupied: LocalPlayerComponent.EnabledInteraction bails on
        // `levelObject.isOccupied`, so its interact icon never comes back (user report 2026-08-03).
        // We therefore owe the server that release. Mirrors vanilla's ordering: fire it when the
        // craft result lands, or after this fallback if no result ever arrives (the RPCRespTask
        // timeout is 1 s, so this sits just past it).
        private const float CraftDirectSendReleaseFallbackSeconds = 1.5f;
        private const int CraftDirectSendPartMaskBody = 1; // CharacterPartMask.Body

        private static readonly string[] CraftDirectSendImageNames =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
        };

        internal static bool MasterLogCraftDirectSend = false;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int CraftDirectSendIsExecutableHookDelegate(IntPtr self, IntPtr arg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr CraftDirectSendCompileMethodDelegate(IntPtr method);

        private static MonoMod.RuntimeDetour.NativeDetour craftDirectSendDetour;
        private static CraftDirectSendIsExecutableHookDelegate craftDirectSendHookKeepAlive; // anti-GC
        private static CraftDirectSendIsExecutableHookDelegate craftDirectSendTrampoline;

        // Written on the main thread, read by the native body. Only true once the detour is live.
        private static volatile bool craftDirectSendActive;

        // MakeItemEvent field offsets, resolved from metadata (never hardcoded — the struct mixes
        // scalars with a List<> reference, so mono is free to reorder it).
        private static int craftDirectSendRecipeIdOffset = -1;
        private static int craftDirectSendCountOffset = -1;
        private static int craftDirectSendThemeIdOffset = -1;
        private static int craftDirectSendDyeColorOffset = -1;
        private static int craftDirectSendDrawOverlayOffset = -1;

        // Hand-off from the native body to the main-thread tick (scalars only).
        private static volatile bool craftDirectSendPending;
        private static int craftDirectSendPendingRecipeId;
        private static int craftDirectSendPendingCount;
        private static int craftDirectSendPendingThemeId;

        private bool craftDirectSendEnabled;
        private bool craftDirectSendHookTried;
        private bool craftDirectSendOffsetsResolved;
        private bool craftDirectSendResolveLogged;
        private bool craftDirectSendCallbackRegistered;
        private int craftDirectSendCount;
        private string craftDirectSendStatus = "Idle.";
        private string craftDirectSendLastLoggedStatus;
        private FeatureBreakerState craftDirectSendBreaker;

        // WebRequestUtility.SendCommand<MakeItemNetworkCommand> resolution (cached metadata ptrs).
        private IntPtr craftDirectSendWebRequestClass = IntPtr.Zero;
        private IntPtr craftDirectSendCommandClass = IntPtr.Zero;
        private IntPtr craftDirectSendOpenSendMethod = IntPtr.Zero;
        private IntPtr craftDirectSendInflatedSendMethod = IntPtr.Zero;
        private IntPtr craftDirectSendFieldRecipeId = IntPtr.Zero;
        private IntPtr craftDirectSendFieldCount = IntPtr.Zero;
        private IntPtr craftDirectSendFieldColorThemeId = IntPtr.Zero;

        // Occupy-release state + its own SendCommand<ItemReleaseNetworkCommand> resolution.
        private bool craftDirectSendReleaseHookRegistered;
        private bool craftDirectSendReleasePending;
        private float craftDirectSendReleaseDueAt;
        private IntPtr craftDirectSendReleaseClass = IntPtr.Zero;
        private IntPtr craftDirectSendReleaseInflated = IntPtr.Zero;
        private IntPtr craftDirectSendReleaseFieldNetId = IntPtr.Zero;
        private IntPtr craftDirectSendReleaseFieldMask = IntPtr.Zero;

        private void ProcessCraftDirectSendOnUpdate()
        {
            float now = Time.unscaledTime;

            // Before the enable check: a release we already owe the server must still go out even
            // if the toggle was flipped off in the meantime, or the bench stays stuck.
            if (this.craftDirectSendReleasePending && now >= this.craftDirectSendReleaseDueAt)
            {
                this.craftDirectSendReleasePending = false;
                this.TryCraftDirectSendReleaseOccupy();
            }

            if (!this.craftDirectSendEnabled)
            {
                // Deliberately NOT Undo()n: tearing a native detour down mid-session is itself a
                // documented heap-corruption source (memory: native-detours-world-change-corruption),
                // and an inert hook simply forwards to the trampoline = vanilla behaviour. The
                // opt-in property is preserved by never INSTALLING it until the toggle is used.
                craftDirectSendActive = false;
                craftDirectSendPending = false;
                return;
            }

            this.EnsureCraftDirectSendReleaseHook();

            // Hook installs run on the world-ready gate, never from a retry timer here
            // (AGENTS.md §1 hard rule). Registration is idempotent and cheap.
            if (!this.craftDirectSendCallbackRegistered)
            {
                this.craftDirectSendCallbackRegistered = true;
                this.RegisterWorldReadyCallback("CraftDirectSend", this.TryInstallCraftDirectSendHookOnWorldReady);
            }

            craftDirectSendActive = craftDirectSendTrampoline != null;

            if (!craftDirectSendPending)
            {
                return;
            }

            craftDirectSendPending = false;
            int recipeId = craftDirectSendPendingRecipeId;
            int count = craftDirectSendPendingCount;
            int themeId = craftDirectSendPendingThemeId;

            if (!this.craftDirectSendBreaker.ShouldRun(now))
            {
                return;
            }

            try
            {
                if (this.TryCraftDirectSendMakeItem(recipeId, count, themeId, out string status))
                {
                    this.craftDirectSendCount++;
                    // We now owe the server the occupy release the vanilla flow would have sent.
                    this.craftDirectSendReleasePending = true;
                    this.craftDirectSendReleaseDueAt = now + CraftDirectSendReleaseFallbackSeconds;
                    this.CraftDirectSendSetStatus("Sent " + this.craftDirectSendCount + " craft(s) directly.");
                }
                else
                {
                    this.CraftDirectSendSetStatus("Send failed: " + status);
                }

                this.craftDirectSendBreaker.Success();
            }
            catch (Exception ex)
            {
                this.craftDirectSendBreaker.Failure("CraftDirectSend", ex, now);
                this.CraftDirectSendSetStatus("Error: " + ex.Message);
            }
        }

        // The craft result is our cue to release, exactly where vanilla's RPCRespTask callback sits.
        // Reuses the Skip Craft / Dye Animations event name — same event, and RegisterGameEventHook
        // simply adds another handler to the shared detour when both features are on.
        private void EnsureCraftDirectSendReleaseHook()
        {
            if (this.craftDirectSendReleaseHookRegistered)
            {
                return;
            }

            this.craftDirectSendReleaseHookRegistered = true;
            this.RegisterGameEventHook(CraftAnimSkipResultEventName,
                CraftAnimSkipResultEventPayloadBytes,
                this.OnCraftDirectSendMakeItemResultEvent);
        }

        private void OnCraftDirectSendMakeItemResultEvent(GameEventSnapshot e)
        {
            if (!this.craftDirectSendReleasePending)
            {
                return;
            }

            // Server confirmed the craft — release now instead of waiting out the fallback.
            this.craftDirectSendReleasePending = false;
            this.TryCraftDirectSendReleaseOccupy();
        }

        // WebRequestUtility.SendCommand<ItemReleaseNetworkCommand>{ ReleaseItemNetId = 0,
        // CharacterPartMask = Body } — byte-for-byte what CharacterProtocolManager
        // .CancelOccupyCommand() sends with its default arguments.
        private unsafe void TryCraftDirectSendReleaseOccupy()
        {
            try
            {
                if (!this.TryEnsureCraftDirectSendReleaseResolved(out string resolveStatus))
                {
                    this.CraftDirectSendSetStatus("Release unresolved: " + resolveStatus);
                    return;
                }

                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    this.CraftDirectSendSetStatus("Release skipped: AuraMono unavailable.");
                    return;
                }

                IntPtr cmdObj = auraMonoObjectNew(this.auraMonoRootDomain, this.craftDirectSendReleaseClass);
                if (cmdObj == IntPtr.Zero)
                {
                    this.CraftDirectSendSetStatus("Release alloc failed.");
                    return;
                }

                uint pin = AuraMonoPinNew(cmdObj);
                if (pin == 0U)
                {
                    this.CraftDirectSendSetStatus("Release pin failed.");
                    return;
                }

                try
                {
                    uint releaseItemNetId = 0u;
                    int partMask = CraftDirectSendPartMaskBody;
                    auraMonoFieldSetValue(cmdObj, this.craftDirectSendReleaseFieldNetId, (IntPtr)(&releaseItemNetId));
                    auraMonoFieldSetValue(cmdObj, this.craftDirectSendReleaseFieldMask, (IntPtr)(&partMask));

                    IntPtr cmdPtr = auraMonoObjectUnbox(cmdObj);
                    if (cmdPtr == IntPtr.Zero)
                    {
                        this.CraftDirectSendSetStatus("Release unbox failed.");
                        return;
                    }

                    int needAuthed = 1;
                    int channel = CraftDirectSendChannelReliable;
                    IntPtr* args = stackalloc IntPtr[3];
                    args[0] = cmdPtr;
                    args[1] = (IntPtr)(&needAuthed);
                    args[2] = (IntPtr)(&channel);

                    IntPtr exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(this.craftDirectSendReleaseInflated, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        this.CraftDirectSendSetStatus("Release SendCommand threw.");
                        return;
                    }

                    if (MasterLogCraftDirectSend)
                    {
                        ModLogger.Msg("[CraftDirectSend] ItemReleaseNetworkCommand sent (occupy released).");
                    }
                }
                finally
                {
                    AuraMonoPinFree(pin);
                }
            }
            catch (Exception ex)
            {
                this.CraftDirectSendSetStatus("Release error: " + ex.Message);
            }
        }

        private bool TryEnsureCraftDirectSendReleaseResolved(out string status)
        {
            if (this.craftDirectSendReleaseInflated != IntPtr.Zero
                && this.craftDirectSendReleaseFieldNetId != IntPtr.Zero
                && this.craftDirectSendReleaseFieldMask != IntPtr.Zero)
            {
                status = "ok";
                return true;
            }

            // Shares WebRequestUtility + SendCommand(3) with the craft sender.
            if (!this.TryEnsureCraftDirectSendResolved(out status))
            {
                return false;
            }

            if (this.craftDirectSendReleaseClass == IntPtr.Zero)
            {
                this.craftDirectSendReleaseClass =
                    this.FindAuraMonoClassByFullName("XDT.Scene.Shared.Modules.Occupy.ItemReleaseNetworkCommand");
                if (this.craftDirectSendReleaseClass == IntPtr.Zero)
                {
                    this.craftDirectSendReleaseClass =
                        this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.Occupy", "ItemReleaseNetworkCommand");
                }
            }

            if (this.craftDirectSendReleaseClass == IntPtr.Zero)
            {
                status = "ItemReleaseNetworkCommand class missing.";
                return false;
            }

            if (this.craftDirectSendReleaseInflated == IntPtr.Zero
                && !this.TryInstantCatchInflateAuraSendCommand(this.craftDirectSendOpenSendMethod,
                    this.craftDirectSendReleaseClass, out this.craftDirectSendReleaseInflated))
            {
                status = "SendCommand<ItemReleaseNetworkCommand> inflate failed.";
                return false;
            }

            if (this.craftDirectSendReleaseFieldNetId == IntPtr.Zero)
            {
                this.craftDirectSendReleaseFieldNetId =
                    this.FindAuraMonoFieldOnHierarchy(this.craftDirectSendReleaseClass, "ReleaseItemNetId");
            }

            if (this.craftDirectSendReleaseFieldMask == IntPtr.Zero)
            {
                this.craftDirectSendReleaseFieldMask =
                    this.FindAuraMonoFieldOnHierarchy(this.craftDirectSendReleaseClass, "CharacterPartMask");
            }

            if (this.craftDirectSendReleaseFieldNetId == IntPtr.Zero || this.craftDirectSendReleaseFieldMask == IntPtr.Zero)
            {
                status = "ItemReleaseNetworkCommand fields missing: ReleaseItemNetId="
                    + (this.craftDirectSendReleaseFieldNetId != IntPtr.Zero)
                    + " CharacterPartMask=" + (this.craftDirectSendReleaseFieldMask != IntPtr.Zero);
                return false;
            }

            status = "ok";
            return true;
        }

        // World-ready callback: returns true when there is nothing left to do for this world
        // (installed, or permanently disarmed), false to be retried.
        private bool TryInstallCraftDirectSendHookOnWorldReady()
        {
            if (craftDirectSendTrampoline != null || this.craftDirectSendHookTried)
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
                CraftDirectSendCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<CraftDirectSendCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.craftDirectSendHookTried = true;
                    ModLogger.Msg("[CraftDirectSend] mono_compile_method unavailable — feature off.");
                    return true;
                }

                // Layout first: refusing the command without being able to read the recipe would
                // make crafting silently vanish.
                if (!this.TryResolveCraftDirectSendArgOffsets())
                {
                    // A hard miss burns the tried-flag itself; otherwise retry.
                    return this.craftDirectSendHookTried;
                }

                // And the sender, for the same reason.
                if (!this.TryEnsureCraftDirectSendResolved(out string sendStatus))
                {
                    this.CraftDirectSendSetStatus("Sender unresolved: " + sendStatus);
                    return false;
                }

                IntPtr cls = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Gameplay.Interaction", "MakeItemCommand", CraftDirectSendImageNames);
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Interaction.MakeItemCommand");
                }
                if (cls == IntPtr.Zero)
                {
                    return false; // image not loaded yet — retry
                }

                // protected override int IsExecutable(in MakeItemEvent arg) -> int(self, ptr).
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "IsExecutable", 1);
                if (method == IntPtr.Zero)
                {
                    this.craftDirectSendHookTried = true;
                    ModLogger.Msg("[CraftDirectSend] MakeItemCommand.IsExecutable(1) not found — feature off (game update?).");
                    return true;
                }

                IntPtr nativePtr = compile(method);
                if (nativePtr == IntPtr.Zero)
                {
                    this.craftDirectSendHookTried = true;
                    ModLogger.Msg("[CraftDirectSend] mono_compile_method returned null — feature off.");
                    return true;
                }

                craftDirectSendHookKeepAlive = CraftDirectSendIsExecutableDetourBody;
                craftDirectSendDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, craftDirectSendHookKeepAlive);
                craftDirectSendTrampoline = craftDirectSendDetour.GenerateTrampoline<CraftDirectSendIsExecutableHookDelegate>();
                if (craftDirectSendTrampoline == null)
                {
                    // Only place we ever Undo: the hook was never usable, so this is install
                    // rollback rather than a live-detour teardown.
                    try { craftDirectSendDetour?.Undo(); } catch { }
                    craftDirectSendDetour = null;
                    this.craftDirectSendHookTried = true;
                    ModLogger.Msg("[CraftDirectSend] trampoline unavailable for IsExecutable; detour reverted — feature off.");
                    return true;
                }

                ModLogger.Msg("[CraftDirectSend] Hooked MakeItemCommand.IsExecutable @0x" + nativePtr.ToInt64().ToString("X")
                    + " (recipeId@" + craftDirectSendRecipeIdOffset
                    + " count@" + craftDirectSendCountOffset
                    + " themeId@" + craftDirectSendThemeIdOffset
                    + " dyeColor@" + craftDirectSendDyeColorOffset
                    + " isDrawOverlay@" + craftDirectSendDrawOverlayOffset + ").");
                return true;
            }
            catch (Exception ex)
            {
                try { craftDirectSendDetour?.Undo(); } catch { }
                craftDirectSendDetour = null;
                craftDirectSendTrampoline = null;
                this.craftDirectSendHookTried = true;
                ModLogger.Msg("[CraftDirectSend] IsExecutable hook install failed: " + ex.Message + " — feature off.");
                return true;
            }
        }

        // Reads the offsets the body needs out of MakeItemEvent's own metadata. Every offset is
        // sanity-checked so a metadata surprise disarms the feature instead of producing a wild read.
        private bool TryResolveCraftDirectSendArgOffsets()
        {
            if (this.craftDirectSendOffsetsResolved)
            {
                return true;
            }

            if (auraMonoFieldGetOffset == null)
            {
                return false;
            }

            IntPtr argClass = this.FindAuraMonoClassByFullName("ScriptsRefactory.DataAndProtocol.Events.MakeItemEvent");
            if (argClass == IntPtr.Zero)
            {
                argClass = this.FindAuraMonoClassAcrossLoadedAssemblies("ScriptsRefactory.DataAndProtocol.Events", "MakeItemEvent");
            }
            if (argClass == IntPtr.Zero)
            {
                return false; // image not up yet
            }

            IntPtr recipeField = this.FindAuraMonoFieldOnHierarchy(argClass, "recipeId");
            IntPtr countField = this.FindAuraMonoFieldOnHierarchy(argClass, "count");
            IntPtr themeField = this.FindAuraMonoFieldOnHierarchy(argClass, "themeId");
            IntPtr dyeField = this.FindAuraMonoFieldOnHierarchy(argClass, "dyeColor");
            IntPtr overlayField = this.FindAuraMonoFieldOnHierarchy(argClass, "isDrawOverlay");
            if (recipeField == IntPtr.Zero || countField == IntPtr.Zero || themeField == IntPtr.Zero
                || dyeField == IntPtr.Zero || overlayField == IntPtr.Zero)
            {
                this.craftDirectSendHookTried = true;
                ModLogger.Msg("[CraftDirectSend] MakeItemEvent fields (recipeId/count/themeId/dyeColor/isDrawOverlay)"
                    + " not found — feature off (game update?).");
                return false;
            }

            // mono_field_get_offset counts from the boxed object start; the `in` pointer we get is
            // the bare struct, so drop the 2-pointer header (codebase idiom).
            int header = 2 * IntPtr.Size;
            int recipeOffset = (int)auraMonoFieldGetOffset(recipeField) - header;
            int countOffset = (int)auraMonoFieldGetOffset(countField) - header;
            int themeOffset = (int)auraMonoFieldGetOffset(themeField) - header;
            int dyeOffset = (int)auraMonoFieldGetOffset(dyeField) - header;
            int overlayOffset = (int)auraMonoFieldGetOffset(overlayField) - header;

            bool sane = recipeOffset >= 0 && countOffset >= 0 && themeOffset >= 0
                && dyeOffset >= 0 && overlayOffset >= 0
                && recipeOffset + 4 <= CraftDirectSendMaxStructSpan
                && countOffset + 4 <= CraftDirectSendMaxStructSpan
                && themeOffset + 4 <= CraftDirectSendMaxStructSpan
                && dyeOffset + 1 <= CraftDirectSendMaxStructSpan
                && overlayOffset + 1 <= CraftDirectSendMaxStructSpan
                && (recipeOffset % 4) == 0 && (countOffset % 4) == 0 && (themeOffset % 4) == 0;
            if (!sane)
            {
                this.craftDirectSendHookTried = true;
                ModLogger.Msg("[CraftDirectSend] MakeItemEvent layout looks wrong (recipe=" + recipeOffset
                    + " count=" + countOffset + " theme=" + themeOffset
                    + " dye=" + dyeOffset + " overlay=" + overlayOffset + ") — feature off.");
                return false;
            }

            craftDirectSendRecipeIdOffset = recipeOffset;
            craftDirectSendCountOffset = countOffset;
            craftDirectSendThemeIdOffset = themeOffset;
            craftDirectSendDyeColorOffset = dyeOffset;
            craftDirectSendDrawOverlayOffset = overlayOffset;
            this.craftDirectSendOffsetsResolved = true;
            return true;
        }

        // Callback-free: reads scalars off the argument, sets statics, returns a constant or
        // forwards. A layout or payload surprise degrades to "vanilla craft", never to a lost click.
        private static unsafe int CraftDirectSendIsExecutableDetourBody(IntPtr self, IntPtr arg)
        {
            if (craftDirectSendActive && arg != IntPtr.Zero && !craftDirectSendPending
                && craftDirectSendRecipeIdOffset >= 0
                && craftDirectSendCountOffset >= 0
                && craftDirectSendThemeIdOffset >= 0
                && craftDirectSendDyeColorOffset >= 0
                && craftDirectSendDrawOverlayOffset >= 0)
            {
                byte* p = (byte*)arg;
                bool dyeColor = *(p + craftDirectSendDyeColorOffset) != 0;
                bool drawOverlay = *(p + craftDirectSendDrawOverlayOffset) != 0;
                if (!dyeColor && !drawOverlay)
                {
                    int recipeId = *(int*)(p + craftDirectSendRecipeIdOffset);
                    int count = *(int*)(p + craftDirectSendCountOffset);
                    int themeId = *(int*)(p + craftDirectSendThemeIdOffset);
                    if (recipeId > 0 && count > 0 && count <= 9999)
                    {
                        craftDirectSendPendingRecipeId = recipeId;
                        craftDirectSendPendingCount = count;
                        craftDirectSendPendingThemeId = themeId;
                        craftDirectSendPending = true; // raised last — the tick reads it first
                        return CraftDirectSendRefuseCode; // InteractErrorCode.Invalid — silent
                    }
                }
            }

            CraftDirectSendIsExecutableHookDelegate trampoline = craftDirectSendTrampoline;
            if (trampoline != null)
            {
                return trampoline(self, arg);
            }

            return 0;
        }

        // WebRequestUtility.SendCommand<MakeItemNetworkCommand>(cmd, needAuthed, channel) — the very
        // call CraftProtocolManager.MakeItem makes. Built directly rather than through MakeItem
        // itself because that takes an `int? themeId`, and a Nullable<> argument over
        // mono_runtime_invoke is exactly the generic-arg trap the project has been bitten by.
        private bool TryEnsureCraftDirectSendResolved(out string status)
        {
            if (this.craftDirectSendInflatedSendMethod != IntPtr.Zero
                && this.craftDirectSendFieldRecipeId != IntPtr.Zero
                && this.craftDirectSendFieldCount != IntPtr.Zero
                && this.craftDirectSendFieldColorThemeId != IntPtr.Zero)
            {
                status = "ok";
                return true;
            }

            if (auraMonoObjectNew == null || auraMonoFieldSetValue == null || auraMonoObjectUnbox == null)
            {
                status = "AuraMono object/field exports unavailable.";
                return false;
            }

            if (this.craftDirectSendWebRequestClass == IntPtr.Zero)
            {
                this.craftDirectSendWebRequestClass =
                    this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.WebRequestUtility");
                if (this.craftDirectSendWebRequestClass == IntPtr.Zero)
                {
                    this.craftDirectSendWebRequestClass =
                        this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService", "WebRequestUtility");
                }
            }

            if (this.craftDirectSendCommandClass == IntPtr.Zero)
            {
                this.craftDirectSendCommandClass =
                    this.FindAuraMonoClassByFullName("XDT.Scene.Shared.Modules.CraftingManual.MakeItemNetworkCommand");
                if (this.craftDirectSendCommandClass == IntPtr.Zero)
                {
                    this.craftDirectSendCommandClass =
                        this.FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.Modules.CraftingManual", "MakeItemNetworkCommand");
                }
            }

            if (this.craftDirectSendWebRequestClass == IntPtr.Zero || this.craftDirectSendCommandClass == IntPtr.Zero)
            {
                status = "class missing: WebRequestUtility=" + (this.craftDirectSendWebRequestClass != IntPtr.Zero)
                    + " MakeItemNetworkCommand=" + (this.craftDirectSendCommandClass != IntPtr.Zero);
                return false;
            }

            if (this.craftDirectSendOpenSendMethod == IntPtr.Zero)
            {
                this.craftDirectSendOpenSendMethod =
                    this.FindAuraMonoMethodOnHierarchy(this.craftDirectSendWebRequestClass, "SendCommand", 3);
            }

            if (this.craftDirectSendOpenSendMethod == IntPtr.Zero)
            {
                status = "SendCommand(3) missing on WebRequestUtility.";
                return false;
            }

            if (this.craftDirectSendInflatedSendMethod == IntPtr.Zero
                && !this.TryInstantCatchInflateAuraSendCommand(this.craftDirectSendOpenSendMethod,
                    this.craftDirectSendCommandClass, out this.craftDirectSendInflatedSendMethod))
            {
                status = "SendCommand<MakeItemNetworkCommand> inflate failed.";
                return false;
            }

            if (this.craftDirectSendFieldRecipeId == IntPtr.Zero)
            {
                this.craftDirectSendFieldRecipeId = this.FindAuraMonoFieldOnHierarchy(this.craftDirectSendCommandClass, "RecipeId");
            }

            if (this.craftDirectSendFieldCount == IntPtr.Zero)
            {
                this.craftDirectSendFieldCount = this.FindAuraMonoFieldOnHierarchy(this.craftDirectSendCommandClass, "Count");
            }

            if (this.craftDirectSendFieldColorThemeId == IntPtr.Zero)
            {
                this.craftDirectSendFieldColorThemeId = this.FindAuraMonoFieldOnHierarchy(this.craftDirectSendCommandClass, "colorThemeId");
            }

            if (this.craftDirectSendFieldRecipeId == IntPtr.Zero || this.craftDirectSendFieldCount == IntPtr.Zero
                || this.craftDirectSendFieldColorThemeId == IntPtr.Zero)
            {
                status = "MakeItemNetworkCommand fields missing: RecipeId=" + (this.craftDirectSendFieldRecipeId != IntPtr.Zero)
                    + " Count=" + (this.craftDirectSendFieldCount != IntPtr.Zero)
                    + " colorThemeId=" + (this.craftDirectSendFieldColorThemeId != IntPtr.Zero);
                return false;
            }

            if (!this.craftDirectSendResolveLogged)
            {
                this.craftDirectSendResolveLogged = true;
                ModLogger.Msg("[CraftDirectSend] sender resolved: SendCommand<MakeItemNetworkCommand>=0x"
                    + this.craftDirectSendInflatedSendMethod.ToInt64().ToString("X"));
            }

            status = "ok";
            return true;
        }

        private unsafe bool TryCraftDirectSendMakeItem(int recipeId, int count, int themeId, out string status)
        {
            if (!this.TryEnsureCraftDirectSendResolved(out status))
            {
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono runtime unavailable.";
                return false;
            }

            IntPtr cmdObj = auraMonoObjectNew(this.auraMonoRootDomain, this.craftDirectSendCommandClass);
            if (cmdObj == IntPtr.Zero)
            {
                status = "command alloc failed.";
                return false;
            }

            uint pin = AuraMonoPinNew(cmdObj);
            if (pin == 0U)
            {
                status = "command pin failed.";
                return false;
            }

            try
            {
                int recipeValue = recipeId;
                int countValue = count;
                int themeValue = themeId;
                auraMonoFieldSetValue(cmdObj, this.craftDirectSendFieldRecipeId, (IntPtr)(&recipeValue));
                auraMonoFieldSetValue(cmdObj, this.craftDirectSendFieldCount, (IntPtr)(&countValue));
                auraMonoFieldSetValue(cmdObj, this.craftDirectSendFieldColorThemeId, (IntPtr)(&themeValue));

                IntPtr cmdPtr = auraMonoObjectUnbox(cmdObj);
                if (cmdPtr == IntPtr.Zero)
                {
                    status = "command unbox failed.";
                    return false;
                }

                int needAuthed = 1;
                int channel = CraftDirectSendChannelReliable;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = cmdPtr;
                args[1] = (IntPtr)(&needAuthed);
                args[2] = (IntPtr)(&channel);

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.craftDirectSendInflatedSendMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "SendCommand threw.";
                    return false;
                }

                if (MasterLogCraftDirectSend)
                {
                    ModLogger.Msg("[CraftDirectSend] MakeItemNetworkCommand sent recipeId=" + recipeId
                        + " count=" + count + " colorThemeId=" + themeId);
                }

                status = "ok";
                return true;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private void CraftDirectSendSetStatus(string status)
        {
            this.craftDirectSendStatus = status;
            if (string.Equals(this.craftDirectSendLastLoggedStatus, status, StringComparison.Ordinal))
            {
                return;
            }

            this.craftDirectSendLastLoggedStatus = status;
            if (MasterLogCraftDirectSend || status.StartsWith("Send failed", StringComparison.Ordinal)
                || status.StartsWith("Error", StringComparison.Ordinal))
            {
                ModLogger.Msg("[CraftDirectSend] " + status);
            }
        }
    }
}
