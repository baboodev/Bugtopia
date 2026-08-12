using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Disable Tutorials — silences BOTH tutorial mechanisms the game runs. They are entirely
    // separate systems with separate data, separate triggers and separate owners, so this feature
    // carries two independent kill switches under one toggle.
    //
    // 1) HIGHLIGHT GUIDES (the ring/arrow that points at a button or menu).
    //    TableHighlightGuide (714 rows today, ids dense 1..714) -> GuideModule, a ViewModule that
    //    ticks CheckGuide() every 0.1 s and promotes the first HighlightGuideItem whose Check()
    //    passes into _curItem. 100% client-side; completion lives in PlayerPrefs
    //    (<ShortId>guideCompele<id>), which this feature deliberately never touches.
    //
    //    LEVER: HighlightGuideItem._CheckConditions() opens with
    //        if (stepDone || DataModule<GuideSystem>.Instance.reproduceDoneIds.Contains(id))
    //            return false;
    //    reproduceDoneIds is a PUBLIC HashSet<int> on GuideSystem and — despite the name — it is
    //    consulted for EVERY highlight, not just the repeatable ones. Seeding it with every id
    //    makes Check() fail on its first line, so nothing is ever promoted to _curItem and the
    //    module's FSM simply idles. See ilspy-dumps/XDTGameUI/XDTLevelAndEntity.Game.Module.Guide/
    //    HighlightGuideItem.cs and .../XDTGameSystem.GameplaySystem.Guide/GuideSystem.cs.
    //
    //    WHY THAT LEVER AND NOT THE OBVIOUS ONES:
    //      * GuideModule.SetGuideComplete(id) is public but writes PlayerPrefs — permanent damage
    //        to the player's tutorial progress, and it does not remove the item for this session.
    //      * Clearing GuideModule._items would be one invoke instead of ~1200, but GuideModule is a
    //        ViewModule with no static Instance, so resolving it goes through Managers.GetModule
    //        (Type) — a documented hard-crash path in this project (memory:
    //        auramono-viewmodule-resolve-typecrash). GuideSystem is a DataModule<T> with a clean
    //        get_Instance, which is why the more expensive but far safer route wins here.
    //
    //    REVERSIBILITY IS FREE: GuideSystem is [ModuleScope(GameLevel_Main)] and its OnCreate does
    //    `reproduceDoneIds = new HashSet<int>()`. The seeding therefore lives exactly one world
    //    load. Turn the toggle off, cross a loading screen, and the game is bit-for-bit vanilla —
    //    no PlayerPrefs keys written, no progress reset.
    //
    // 2) TUTORIAL POPUPS (the window with text / screenshots / video).
    //    TableGuide (303 rows) carries its own trigger (guideEventString + guideEventTypeParam,
    //    mostly TaskSubmit/TaskAccept). The SERVER evaluates it and pushes ShowGuideNetworkEvent;
    //    the client only renders. So there is nothing to "decide" client-side — the only lever is
    //    to stop the render.
    //
    //    LEVER: UIEventBridge.OnNewGuideTipOpen / OnGuideBottomTipOpen are the ONLY listeners for
    //    NewGuideTipOpenEvent / GuideBottomTipOpenEvent, and each does nothing but
    //    Panel.Open(guideId). Suppressing the dispatch means the panel never opens at all — no
    //    frame of flicker, unlike closing it after the fact. Same mechanism and same shape as
    //    ShowOffBypassFeature, which has been suppressing FlauntActionEvent in production.
    //
    //    Server state is untouched (GuideComponent.IsDone is server-side), and the in-game guide
    //    handbook (GuideReviewPanel, fed by IGuideService.GetAllGuideIds) keeps listing every
    //    tutorial, so the player can still read any of them on demand.
    //
    // Both halves degrade silently: any resolve failure leaves the game alone and reports through
    // the status string rather than throwing.
    public partial class HeartopiaComplete
    {
        private const string TutorialGuideSystemTypeName = "XDTGameSystem.GameplaySystem.Guide.GuideSystem";
        private const string TutorialGuideSystemNamespace = "XDTGameSystem.GameplaySystem.Guide";
        private const string TutorialGuideSystemClassName = "GuideSystem";

        // Popup open events — int guideId, 4 bytes.
        private const string TutorialTipOpenEventName = "XDTGameSystem.UI.NewGuideTipOpenEvent";
        private const string TutorialBottomTipOpenEventName = "XDTGameSystem.UI.GuideBottomTipOpenEvent";
        private const int TutorialTipOpenEventPayloadBytes = 4;

        // Highlight overlay open event — int guideId + bool forceShow. Suppressed only as the
        // fallback for when the reproduceDoneIds seeding could not be applied (see below).
        private const string TutorialHighlightOpenEventName = "XDTGameSystem.UI.NewGuideOpenEvent";
        private const int TutorialHighlightOpenEventPayloadBytes = 8;

        // Empty struct (Size = 1). GuideModule listens and sets its private `interrupted` flag; its
        // next Update then runs the game's own teardown — GuideViewCloseRequestEvent,
        // RemoveCommonClickEvent(), _curItem = null, NeedCheckGuide(). Dispatching this is how we
        // dismiss a highlight that was already on screen when the toggle flipped, without writing
        // a single game field.
        private const string TutorialInterruptEventName = "XDTDataAndProtocol.Events.InterruptGuideEvent";

        // Ids are dense 1..Count today, so the live Dictionary count is the exact ceiling. The
        // margin absorbs a future update that leaves gaps in the id space; overshooting is free
        // because ids that do not exist simply never get looked up.
        private const int TutorialHighlightIdMargin = 512;

        // Used only when the live table cannot be read at all — comfortably above the current 714.
        private const int TutorialHighlightIdFallbackCeiling = 1536;

        // Hard stop so a corrupt count can never spin the invoke loop.
        private const int TutorialHighlightIdMaxCeiling = 8192;

        internal static bool MasterLogTutorialBlock = true;

        private bool blockTutorials;
        private bool tutorialBlockRegistered;
        private bool tutorialBlockApplyPending;
        private bool tutorialHighlightBlockApplied;
        private bool tutorialBlockLastToggleState;
        private IntPtr tutorialGuideSystemClass = IntPtr.Zero;
        private IntPtr tutorialInterruptDispatchMethod = IntPtr.Zero;
        private float tutorialInterruptResolveRetryAt;
        private int tutorialHighlightBlockedCount;
        private string tutorialBlockStatus = "Idle.";
        private string tutorialBlockLastLoggedStatus;

        // Diagnostic: a highlight that opens after a successful seed proves the set we seeded is
        // not the set the game reads. The probe re-resolves everything FROM SCRATCH at that moment
        // and reports the pointers, so the two views can be compared directly. Deliberately run
        // from OnUpdate rather than inside the dispatch detour — re-entering mono from inside a
        // detour body is not a risk worth taking for a log line.
        private bool tutorialDiagProbePending;
        private int tutorialDiagProbeGuideId;

        private void ProcessTutorialBlockOnUpdate()
        {
            this.EnsureTutorialBlockRegistrations();

            bool on = this.blockTutorials;
            if (on != this.tutorialBlockLastToggleState)
            {
                this.tutorialBlockLastToggleState = on;
                if (on)
                {
                    // Flipped on mid-session: apply as soon as the world allows it.
                    this.tutorialBlockApplyPending = true;
                }
                else
                {
                    // Flipped off: stop forcing anything. The seeding already applied to this
                    // world stays until the next load recreates GuideSystem — documented in the
                    // toggle's tooltip and in the plan.
                    this.tutorialBlockApplyPending = false;
                    this.TutorialBlockSetStatus("Off — highlights return after the next world load.");
                }
            }

            this.SetGameEventHookSuppressForward(TutorialTipOpenEventName, on);
            this.SetGameEventHookSuppressForward(TutorialBottomTipOpenEventName, on);

            // Belt to the seeding's braces: if the HashSet route did not take (renamed field after
            // a game update, module not up yet), stop the overlay from rendering anyway. Kept OFF
            // once the seeding is in, because a suppressed open event leaves the item sitting in
            // _curItem forever, re-dispatching its position sync every frame.
            this.SetGameEventHookSuppressForward(
                TutorialHighlightOpenEventName,
                on && !this.tutorialHighlightBlockApplied);

            // Runs outside the dispatch detour on purpose (see the field comment).
            if (this.tutorialDiagProbePending)
            {
                this.tutorialDiagProbePending = false;
                this.RunTutorialDoneSetProbe(this.tutorialDiagProbeGuideId);
            }

            if (!on || !this.tutorialBlockApplyPending)
            {
                return;
            }

            // The world-ready callback already seeded this world — nothing left for the toggle
            // path to do. Without this, a config that starts with the toggle ON would seed twice
            // (once here, once from the gate) for no gain.
            if (this.tutorialHighlightBlockApplied)
            {
                this.tutorialBlockApplyPending = false;
                return;
            }

            // Resolving game types before a world exists fails at best and AVs at worst
            // (AGENTS.md world-ready rule) — the world-ready callback owns the normal path, this
            // branch only serves a mid-session toggle flip.
            if (!this.IsWorldReady)
            {
                return;
            }

            // Single shot: a failure here waits for the next world-ready rather than spinning a
            // per-frame retry timer, which the world-ready rule explicitly forbids.
            this.tutorialBlockApplyPending = false;
            this.TryApplyTutorialHighlightBlock();
        }

        private void EnsureTutorialBlockRegistrations()
        {
            if (this.tutorialBlockRegistered)
            {
                return;
            }

            this.tutorialBlockRegistered = true;

            bool tipOk = this.RegisterGameEventHook(
                TutorialTipOpenEventName, TutorialTipOpenEventPayloadBytes, this.OnTutorialTipOpenEventHook);
            bool bottomOk = this.RegisterGameEventHook(
                TutorialBottomTipOpenEventName, TutorialTipOpenEventPayloadBytes, this.OnTutorialBottomTipOpenEventHook);
            bool overlayOk = this.RegisterGameEventHook(
                TutorialHighlightOpenEventName, TutorialHighlightOpenEventPayloadBytes, this.OnTutorialHighlightOpenEventHook);

            if (MasterLogTutorialBlock)
            {
                ModLogger.Msg("[TutorialBlock] hook register tip=" + tipOk
                    + " bottomTip=" + bottomOk
                    + " overlay=" + overlayOk);
            }

            // The seeding runs once per world load: GuideSystem is level-scoped, so every new world
            // hands us a fresh empty HashSet that has to be re-seeded.
            this.RegisterWorldReadyCallback("TutorialBlock", this.OnTutorialBlockWorldReady);
        }

        private bool OnTutorialBlockWorldReady()
        {
            // New world => new GuideSystem => new (empty) reproduceDoneIds.
            this.tutorialHighlightBlockApplied = false;
            this.tutorialGuideSystemClass = IntPtr.Zero;
            this.tutorialHighlightBlockedCount = 0;
            this.tutorialBlockApplyPending = false;

            if (!this.blockTutorials)
            {
                return true; // nothing to do for this world; a later toggle flip handles itself
            }

            // Returning false asks the gate to retry shortly (an image can still be streaming in).
            return this.TryApplyTutorialHighlightBlock();
        }

        // Seed GuideSystem.reproduceDoneIds so every highlight fails Check() on its first line.
        // Returns false on any resolve miss so the world-ready gate retries.
        private unsafe bool TryApplyTutorialHighlightBlock()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
                {
                    this.TutorialBlockSetStatus("Mono API not ready.");
                    return false;
                }

                // Raw reads against game statics AV before login — this flag is the project's
                // guard for "the game's Mono side has actually handed us a live object".
                if (!AuraMonoGameDataLive)
                {
                    this.TutorialBlockSetStatus("Game Mono side not live yet.");
                    return false;
                }

                // Pin-less walks race the moving sgen GC; the collection/field helpers below fail
                // closed without pinning, so refuse rather than risk a native AV.
                if (!AuraMonoPinningAvailable)
                {
                    this.TutorialBlockSetStatus("AuraMono pinning unavailable — highlight block disabled.");
                    return false;
                }

                if (this.tutorialGuideSystemClass == IntPtr.Zero)
                {
                    this.tutorialGuideSystemClass = this.FindAuraMonoClassByFullName(TutorialGuideSystemTypeName);
                    if (this.tutorialGuideSystemClass == IntPtr.Zero)
                    {
                        this.tutorialGuideSystemClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                            TutorialGuideSystemNamespace, TutorialGuideSystemClassName);
                    }
                }

                if (this.tutorialGuideSystemClass == IntPtr.Zero)
                {
                    this.TutorialBlockSetStatus("GuideSystem class not found.");
                    return false;
                }

                // Level-scoped singleton — resolved fresh, never cached across frames.
                IntPtr instance = this.TryGetAuraMonoDataModuleInstance(this.tutorialGuideSystemClass);
                if (instance == IntPtr.Zero)
                {
                    this.TutorialBlockSetStatus("DataModule<GuideSystem>.Instance null.");
                    return false;
                }

                uint instancePin = AuraMonoPinNew(instance);
                try
                {
                    if (!this.TryReadAuraMonoObjectField(instance, out IntPtr doneSet, "reproduceDoneIds")
                        || doneSet == IntPtr.Zero)
                    {
                        // OnCreate allocates it, so a null here means the module exists but has not
                        // run OnCreate yet — worth a retry.
                        this.TutorialBlockSetStatus("reproduceDoneIds not readable yet.");
                        return false;
                    }

                    uint setPin = AuraMonoPinNew(doneSet);
                    try
                    {
                        IntPtr setClass = auraMonoObjectGetClass(doneSet);
                        IntPtr addMethod = setClass != IntPtr.Zero
                            ? this.FindAuraMonoMethodOnHierarchy(setClass, "Add", 1)
                            : IntPtr.Zero;
                        if (addMethod == IntPtr.Zero)
                        {
                            this.TutorialBlockSetStatus("HashSet<int>.Add not found.");
                            return false;
                        }

                        int ceiling = this.ResolveTutorialHighlightIdCeiling();
                        int countBefore = this.ReadTutorialSetCount(doneSet);

                        // stackalloc is hoisted out of the loop on purpose: inside it the frame
                        // would grow by one slot per iteration and never unwind until return.
                        int idArg = 0;
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = (IntPtr)(&idArg);

                        for (int id = 1; id <= ceiling; id++)
                        {
                            idArg = id;
                            IntPtr exc = IntPtr.Zero;
                            auraMonoRuntimeInvoke(addMethod, doneSet, (IntPtr)args, ref exc);
                            if (exc != IntPtr.Zero)
                            {
                                this.TutorialBlockSetStatus("HashSet<int>.Add threw at id " + id + ".");
                                return false;
                            }
                        }

                        this.tutorialHighlightBlockedCount = ceiling;
                        this.tutorialHighlightBlockApplied = true;

                        // Log the EFFECT, not just the intent: a count that did not move by
                        // `ceiling` means the Add calls landed somewhere the game does not read.
                        int countAfter = this.ReadTutorialSetCount(doneSet);
                        this.TutorialBlockSetStatus("Highlights blocked (applied " + ceiling
                            + " ids, count " + countBefore + " -> " + countAfter
                            + ", instance=0x" + instance.ToString("X")
                            + " set=0x" + doneSet.ToString("X") + ").");
                    }
                    finally
                    {
                        AuraMonoPinFree(setPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(instancePin);
                }

                // Anything already on screen predates the seeding — dismiss it through the game's
                // own interrupt path.
                this.TryDispatchTutorialInterruptEvent();
                return true;
            }
            catch (Exception ex)
            {
                this.TutorialBlockSetStatus("Highlight block error: " + ex.Message);
                return false;
            }
        }

        // Ids are dense 1..Count in the live table, so the Dictionary's own count is the exact
        // ceiling and it tracks future content automatically. get_Count is a plain method on the
        // constructed generic — no enumerator, so none of the boxed-struct-enumerator hazards.
        private int ResolveTutorialHighlightIdCeiling()
        {
            try
            {
                IntPtr tableDataClass = this.FindAuraMonoClassInImages(
                    string.Empty, "TableData", new[] { "EcsClient", "EcsClient.dll" });
                if (tableDataClass == IntPtr.Zero)
                {
                    tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
                }

                if (tableDataClass == IntPtr.Zero
                    || !this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableHighlightGuides", out IntPtr dict)
                    || dict == IntPtr.Zero)
                {
                    return TutorialHighlightIdFallbackCeiling;
                }

                uint dictPin = AuraMonoPinNew(dict);
                try
                {
                    IntPtr dictClass = auraMonoObjectGetClass(dict);
                    IntPtr getCount = dictClass != IntPtr.Zero
                        ? this.FindAuraMonoMethodOnHierarchy(dictClass, "get_Count", 0)
                        : IntPtr.Zero;
                    if (getCount == IntPtr.Zero)
                    {
                        return TutorialHighlightIdFallbackCeiling;
                    }

                    IntPtr exc = IntPtr.Zero;
                    IntPtr boxed = auraMonoRuntimeInvoke(getCount, dict, IntPtr.Zero, ref exc);
                    if (exc != IntPtr.Zero || boxed == IntPtr.Zero
                        || !this.TryUnboxMonoInt32(boxed, out int count) || count <= 0)
                    {
                        return TutorialHighlightIdFallbackCeiling;
                    }

                    int ceiling = count + TutorialHighlightIdMargin;
                    return ceiling > TutorialHighlightIdMaxCeiling ? TutorialHighlightIdMaxCeiling : ceiling;
                }
                finally
                {
                    AuraMonoPinFree(dictPin);
                }
            }
            catch
            {
                return TutorialHighlightIdFallbackCeiling;
            }
        }

        // EventCenter.DispatchEvent<InterruptGuideEvent>(in default) — same inflate-and-invoke
        // shape ShowOffBypassFeature uses for NextFlauntActionEvent. InterruptGuideEvent is an
        // empty struct, so the argument is just a pointer to a zeroed slot.
        private unsafe bool TryDispatchTutorialInterruptEvent()
        {
            try
            {
                if (this.tutorialInterruptDispatchMethod == IntPtr.Zero)
                {
                    if (Time.unscaledTime < this.tutorialInterruptResolveRetryAt)
                    {
                        return false;
                    }

                    this.tutorialInterruptResolveRetryAt = Time.unscaledTime + 2f;
                    if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                        || auraMonoClassGetType == null || auraMonoMetadataGetGenericInst == null
                        || auraMonoClassInflateGenericMethod == null || auraMonoRuntimeInvoke == null)
                    {
                        return false;
                    }

                    IntPtr eventCenterClass = this.FindAuraMonoClassByFullName("XDTGame.Core.EventCenter");
                    if (eventCenterClass == IntPtr.Zero)
                    {
                        eventCenterClass = this.FindAuraMonoClassInImages("XDTGame.Core", "EventCenter",
                            new[] { "XDTBaseService", "XDTBaseService.dll" });
                    }

                    IntPtr openDispatch = eventCenterClass != IntPtr.Zero
                        ? this.FindAuraMonoMethodOnHierarchy(eventCenterClass, "DispatchEvent", 1)
                        : IntPtr.Zero;
                    IntPtr interruptClass = this.ResolveGameEventClass(TutorialInterruptEventName);
                    if (openDispatch == IntPtr.Zero || interruptClass == IntPtr.Zero)
                    {
                        if (MasterLogTutorialBlock)
                        {
                            ModLogger.Msg("[TutorialBlock] interrupt resolve failed dispatch="
                                + (openDispatch != IntPtr.Zero) + " eventClass=" + (interruptClass != IntPtr.Zero));
                        }

                        return false;
                    }

                    if (!this.TryInflateDispatchForEvent(openDispatch, interruptClass, 1, out IntPtr inflated))
                    {
                        if (MasterLogTutorialBlock)
                        {
                            ModLogger.Msg("[TutorialBlock] interrupt inflate failed");
                        }

                        return false;
                    }

                    this.tutorialInterruptDispatchMethod = inflated;
                }

                IntPtr exc = IntPtr.Zero;
                int payload = 0; // empty struct — the callee reads nothing
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&payload);
                auraMonoRuntimeInvoke(this.tutorialInterruptDispatchMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                return exc == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private void OnTutorialTipOpenEventHook(GameEventSnapshot e)
        {
            if (!MasterLogTutorialBlock)
            {
                return;
            }

            ModLogger.Msg("[TutorialBlock] NewGuideTipOpenEvent guideId=" + e.ReadInt32(0)
                + " suppress=" + this.blockTutorials);
        }

        private void OnTutorialBottomTipOpenEventHook(GameEventSnapshot e)
        {
            if (!MasterLogTutorialBlock)
            {
                return;
            }

            ModLogger.Msg("[TutorialBlock] GuideBottomTipOpenEvent guideId=" + e.ReadInt32(0)
                + " suppress=" + this.blockTutorials);
        }

        private void OnTutorialHighlightOpenEventHook(GameEventSnapshot e)
        {
            if (!MasterLogTutorialBlock)
            {
                return;
            }

            int guideId = e.ReadInt32(0);
            ModLogger.Msg("[TutorialBlock] NewGuideOpenEvent guideId=" + guideId
                + " seeded=" + this.tutorialHighlightBlockApplied
                + " suppress=" + (this.blockTutorials && !this.tutorialHighlightBlockApplied));

            // A highlight got through a seed that reported success — capture the evidence.
            if (this.tutorialHighlightBlockApplied && !this.tutorialDiagProbePending)
            {
                this.tutorialDiagProbeGuideId = guideId;
                this.tutorialDiagProbePending = true;
            }
        }

        // Re-resolve GuideSystem -> reproduceDoneIds from scratch and report what the game would
        // see right now. Compare the pointers against the ones logged at seed time: identical
        // pointers with Count=1226 mean the seeding is fine and the block failed elsewhere;
        // different pointers (or Count=0) mean we seeded the wrong object.
        private unsafe void RunTutorialDoneSetProbe(int probeGuideId)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null
                    || !AuraMonoGameDataLive || !AuraMonoPinningAvailable)
                {
                    ModLogger.Msg("[TutorialBlock] probe skipped — AuraMono not ready.");
                    return;
                }

                IntPtr cls = this.FindAuraMonoClassByFullName(TutorialGuideSystemTypeName);
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        TutorialGuideSystemNamespace, TutorialGuideSystemClassName);
                }

                IntPtr instance = this.TryGetAuraMonoDataModuleInstance(cls);
                if (instance == IntPtr.Zero)
                {
                    ModLogger.Msg("[TutorialBlock] probe: class=0x" + cls.ToString("X") + " instance=NULL");
                    return;
                }

                uint instancePin = AuraMonoPinNew(instance);
                try
                {
                    if (!this.TryReadAuraMonoObjectField(instance, out IntPtr doneSet, "reproduceDoneIds")
                        || doneSet == IntPtr.Zero)
                    {
                        ModLogger.Msg("[TutorialBlock] probe: instance=0x" + instance.ToString("X")
                            + " reproduceDoneIds=NULL");
                        return;
                    }

                    uint setPin = AuraMonoPinNew(doneSet);
                    try
                    {
                        int count = this.ReadTutorialSetCount(doneSet);
                        string contains = "n/a";
                        IntPtr setClass = auraMonoObjectGetClass(doneSet);
                        IntPtr containsMethod = setClass != IntPtr.Zero
                            ? this.FindAuraMonoMethodOnHierarchy(setClass, "Contains", 1)
                            : IntPtr.Zero;
                        if (containsMethod != IntPtr.Zero && probeGuideId > 0)
                        {
                            int idArg = probeGuideId;
                            IntPtr* args = stackalloc IntPtr[1];
                            args[0] = (IntPtr)(&idArg);
                            IntPtr exc = IntPtr.Zero;
                            IntPtr boxed = auraMonoRuntimeInvoke(containsMethod, doneSet, (IntPtr)args, ref exc);
                            if (exc == IntPtr.Zero && boxed != IntPtr.Zero
                                && this.TryUnboxMonoBoolean(boxed, out bool has))
                            {
                                contains = has.ToString();
                            }
                        }

                        ModLogger.Msg("[TutorialBlock] probe guideId=" + probeGuideId
                            + " instance=0x" + instance.ToString("X")
                            + " set=0x" + doneSet.ToString("X")
                            + " count=" + count
                            + " contains=" + contains);
                    }
                    finally
                    {
                        AuraMonoPinFree(setPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(instancePin);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[TutorialBlock] probe error: " + ex.Message);
            }
        }

        private int ReadTutorialSetCount(IntPtr setObj)
        {
            try
            {
                IntPtr setClass = auraMonoObjectGetClass(setObj);
                IntPtr getCount = setClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(setClass, "get_Count", 0)
                    : IntPtr.Zero;
                if (getCount == IntPtr.Zero)
                {
                    return -1;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(getCount, setObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero || !this.TryUnboxMonoInt32(boxed, out int count))
                {
                    return -1;
                }

                return count;
            }
            catch
            {
                return -1;
            }
        }

        private void TutorialBlockSetStatus(string status)
        {
            this.tutorialBlockStatus = status;
            if (!MasterLogTutorialBlock || string.Equals(status, this.tutorialBlockLastLoggedStatus, StringComparison.Ordinal))
            {
                return;
            }

            this.tutorialBlockLastLoggedStatus = status;
            ModLogger.Msg("[TutorialBlock] " + status);
        }
    }
}
