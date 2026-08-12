using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Auto-learn recipes — learns every blueprint / cookbook / music sheet that lands in the
    // backpack, without the 4.3 s "Learn" animation and without the batch-confirm panel.
    //
    // WHY THERE IS NO ANIMATION TO SKIP HERE. The vanilla click path is
    // BagModule.LearnRecipes() -> LearnEvent -> BackpackLearnRecipe (InteractId 10103), and that
    // interact already ships TWO branches
    // (ilspy-dumps/XDTLevelAndEntity/XDTLevelAndEntity.Gameplay.Interaction/BackpackLearnRecipe.cs):
    //
    //     if (!IsFullScreenOccupied() && player.IsStateOrEquivalent(Free))
    //         player.Cast(PlayerParameterLearn);                       // PlayerLearnAction, 4.3 s
    //     else
    //         QuickLearnSystem.LearnRecipes(netIds);                   // instant, no cast
    //
    // The cast is decorative: PlayerLearnAction.OnBehaveStart sends the network command on its
    // first frame and OnBehaveFinish only clears two animator bools and despawns the blue point.
    // This feature calls the protocol directly, i.e. it takes the game's own second branch, so
    // there is no clip to cut, no BatchLearningPanel and no confirmation.
    //
    // TRIGGER: QuickLearnEvent. QuickLearnSystem
    // (ilspy-dumps/XDTGameSystem/XDTGameSystem.GameplaySystem.QuickLearn/QuickLearnSystem.cs) is a
    // level-scoped DataModule that listens to DataCreated<BackpackItem> and, 0.5 s after a
    // learnable item lands, dispatches QuickLearnEvent — the same signal that raises the vanilla
    // quick-learn prompt. Events-first per AGENTS.md §7, with a slow poll until the detour is live.
    // The payload is a single List<uint> reference: it is NEVER read (a byte snapshot of a managed
    // reference is meaningless and dereferencing it would race SGen) — the dispatch alone is the
    // signal, and the netIds are re-derived from the backpack.
    //
    // ACTION: CharacterProtocolManager.LearnRecipes(List<uint>) ->
    // WebRequestUtility.SendCommand(LearnMoreRecipeNetworkCommand). Invoking the game's own
    // protocol manager rather than building the command struct ourselves keeps the whole
    // serialization detail on the game's side. BlueprintNetIds carries [MaxLength(120)], so batches
    // are chunked. The server answers UnlockMoreRecipeNetworkEvent{Result, StaticIds} with
    // Result 0 = Success, 1 = AlreadyLearn.
    //
    // ITEM SELECTION is a strict allowlist of the three learnable entity types — exactly the
    // predicate of BackPackSystem.GetAllRecipe(). This matters: QuickLearnSystem's own cache also
    // queues choosable treasure chests, which are NOT recipes and must never reach this command.
    // homelandblueprint (228) is a different type and is likewise excluded, so house blueprints are
    // safe.
    //
    // ALL game access here is AuraMono. BackPackSystem and CharacterProtocolManager are XDT* types
    // and the BepInEx interop contains no XDT*/EcsClient assemblies at all, so a managed-reflection
    // path would never resolve — see project rule prefer-auramono-no-managed-fallback. The backpack
    // is read directly rather than through AutoSell's pinned snapshot: reusing that snapshot would
    // couple this feature to AutoSell's refresh cadence and pin lifetime for no gain, since all we
    // need out of it is netId + entityType.
    public partial class HeartopiaComplete
    {
        private const string AutoLearnQuickLearnEventName = "XDTDataAndProtocol.Events.QuickLearnEvent";

        // Payload is one List<uint> reference — deliberately snapshot nothing.
        private const int AutoLearnQuickLearnEventPayloadBytes = 0;

        private const string AutoLearnBackPackSystemTypeName = "XDTGameSystem.GameplaySystem.BackPack.BackPackSystem";
        private const string AutoLearnProtocolTypeName = "XDTDataAndProtocol.ProtocolService.GamePlay.Character.CharacterProtocolManager";
        private const string AutoLearnProtocolNamespace = "XDTDataAndProtocol.ProtocolService.GamePlay.Character";
        private const string AutoLearnProtocolClassName = "CharacterProtocolManager";
        private const string AutoLearnProtocolMethodName = "LearnRecipes";

        // EcsClient.XDT.Scene.Shared.Data.SharedData.EntityType — the three kinds BackPackSystem
        // .GetAllRecipe() accepts. Allowlist, never a blocklist.
        private const int AutoLearnEntityTypeMusicSheet = 42;
        private const int AutoLearnEntityTypeCookbook = 47;
        private const int AutoLearnEntityTypeBlueprint = 302;

        // LearnMoreRecipeNetworkCommand.BlueprintNetIds is [MaxLength(120)]; overrunning it makes
        // the server reject the whole message.
        private const int AutoLearnMaxBatch = 120;

        // QuickLearnSystem dispatches 0.5 s after the item lands, so the item is already in
        // BackPackSystem by then; this is just slack for the bag data to settle.
        private const float AutoLearnSettleSeconds = 0.5f;

        // Covers the window before the detour installs, and any dispatch it could not splice.
        private const float AutoLearnFallbackPollSeconds = 10f;

        // The server removes the consumed items asynchronously; don't re-sweep into the same bag.
        private const float AutoLearnPostSendCooldownSeconds = 2f;

        // A netId that survives this many sends is not going to be learned (already known, locked,
        // server refusal). Stop rather than loop forever.
        private const int AutoLearnMaxAttemptsPerItem = 2;

        internal static bool MasterLogAutoLearn = false;

        private bool autoLearnRecipes;
        private bool autoLearnRegistered;
        private bool autoLearnHookInstallLogged;
        private bool autoLearnLastToggleState;
        private bool autoLearnSweepPending;
        private float autoLearnSweepAt;
        private float autoLearnNextFallbackSweepAt;
        private int autoLearnSentTotal;
        private IntPtr autoLearnProtocolMethod = IntPtr.Zero;
        private IntPtr autoLearnUIntListClass = IntPtr.Zero;
        private IntPtr autoLearnUIntListAddMethod = IntPtr.Zero;
        private readonly Dictionary<uint, int> autoLearnAttemptsByNetId = new Dictionary<uint, int>();
        private readonly List<uint> autoLearnPendingNetIds = new List<uint>();
        private readonly List<uint> autoLearnBatch = new List<uint>();
        private string autoLearnStatus = "Idle.";
        private string autoLearnLastLoggedStatus;
        private FeatureBreakerState autoLearnBreaker;

        private void ProcessAutoLearnRecipesOnUpdate()
        {
            this.EnsureAutoLearnRegistrations();

            bool on = this.autoLearnRecipes;
            if (on != this.autoLearnLastToggleState)
            {
                this.autoLearnLastToggleState = on;
                if (on)
                {
                    // Sweep whatever is already sitting in the bag; QuickLearnEvent only fires for
                    // items that arrive from now on.
                    this.autoLearnSweepPending = true;
                    this.autoLearnSweepAt = 0f;
                }
                else
                {
                    this.autoLearnSweepPending = false;
                    this.AutoLearnSetStatus("Off.");
                }
            }

            if (!on)
            {
                return;
            }

            float now = Time.unscaledTime;
            bool hooked = this.IsGameEventHookInstalled(AutoLearnQuickLearnEventName);
            if (hooked && !this.autoLearnHookInstallLogged)
            {
                this.autoLearnHookInstallLogged = true;
                ModLogger.Msg("[AutoLearn] hook installed: " + AutoLearnQuickLearnEventName);
            }

            // Event-primary, poll-fallback (AGENTS.md §7). The poll also covers the case where the
            // detour is live but a dispatch arrived while the toggle was off.
            if (!hooked && now >= this.autoLearnNextFallbackSweepAt)
            {
                this.autoLearnNextFallbackSweepAt = now + AutoLearnFallbackPollSeconds;
                this.autoLearnSweepPending = true;
                this.autoLearnSweepAt = 0f;
            }

            if (!this.autoLearnSweepPending || now < this.autoLearnSweepAt)
            {
                return;
            }

            // Resolving game types before a world exists fails at best and AVs at worst
            // (AGENTS.md world-ready rule). Hold the request until the gate is open.
            if (!this.IsWorldReady)
            {
                return;
            }

            if (!this.autoLearnBreaker.ShouldRun(now))
            {
                return;
            }

            this.autoLearnSweepPending = false;
            try
            {
                this.TryAutoLearnSweep(now);
                this.autoLearnBreaker.Success();
            }
            catch (Exception ex)
            {
                this.autoLearnBreaker.Failure("AutoLearn", ex, now);
                this.AutoLearnSetStatus("Sweep error: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void EnsureAutoLearnRegistrations()
        {
            if (this.autoLearnRegistered)
            {
                return;
            }

            this.autoLearnRegistered = true;
            bool ok = this.RegisterGameEventHook(
                AutoLearnQuickLearnEventName, AutoLearnQuickLearnEventPayloadBytes, this.OnAutoLearnQuickLearnEventHook);
            if (!ok)
            {
                ModLogger.Msg("[AutoLearn] QuickLearnEvent hook registration REFUSED — falling back to polling only.");
            }
            else if (MasterLogAutoLearn)
            {
                ModLogger.Msg("[AutoLearn] registered hook " + AutoLearnQuickLearnEventName);
            }

            this.RegisterWorldReadyCallback("AutoLearnRecipes", this.OnAutoLearnWorldReady);
        }

        // New world => new item instances, and the level-scoped QuickLearnSystem starts over. Drop
        // the per-netId attempt ledger (netIds are per-instance) and re-sweep the fresh bag.
        private bool OnAutoLearnWorldReady()
        {
            this.autoLearnAttemptsByNetId.Clear();
            this.autoLearnProtocolMethod = IntPtr.Zero;
            this.autoLearnUIntListClass = IntPtr.Zero;
            this.autoLearnUIntListAddMethod = IntPtr.Zero;
            if (this.autoLearnRecipes)
            {
                this.autoLearnSweepPending = true;
                this.autoLearnSweepAt = Time.unscaledTime + AutoLearnSettleSeconds;
            }

            return true;
        }

        private void OnAutoLearnQuickLearnEventHook(GameEventSnapshot e)
        {
            if (!this.autoLearnRecipes)
            {
                return;
            }

            this.autoLearnSweepPending = true;

            // Never pull the sweep EARLIER than an already-scheduled one: a batch of items raises
            // several QuickLearnEvents in a row, and the post-send cooldown is what stops the next
            // one from re-sweeping a bag the server has not finished emptying (which would burn the
            // per-item attempt budget on recipes that were in fact already learned).
            float at = Time.unscaledTime + AutoLearnSettleSeconds;
            if (at > this.autoLearnSweepAt)
            {
                this.autoLearnSweepAt = at;
            }

            if (MasterLogAutoLearn)
            {
                ModLogger.Msg("[AutoLearn] QuickLearnEvent — sweep armed");
            }
        }

        private void TryAutoLearnSweep(float now)
        {
            this.autoLearnPendingNetIds.Clear();
            if (!this.TryCollectAutoLearnNetIds(this.autoLearnPendingNetIds, out string collectStatus))
            {
                this.AutoLearnSetStatus(collectStatus);
                return;
            }

            if (this.autoLearnPendingNetIds.Count == 0)
            {
                this.AutoLearnSetStatus("Nothing to learn (" + this.autoLearnSentTotal + " sent this session).");
                return;
            }

            int sent = 0;
            for (int i = 0; i < this.autoLearnPendingNetIds.Count; i += AutoLearnMaxBatch)
            {
                this.autoLearnBatch.Clear();
                int end = Math.Min(i + AutoLearnMaxBatch, this.autoLearnPendingNetIds.Count);
                for (int j = i; j < end; j++)
                {
                    this.autoLearnBatch.Add(this.autoLearnPendingNetIds[j]);
                }

                if (!this.TryInvokeAutoLearnRecipes(this.autoLearnBatch, out string sendStatus))
                {
                    this.autoLearnSentTotal += sent;
                    this.AutoLearnSetStatus(sendStatus);
                    return;
                }

                // Charge the attempt per BATCH, right after it reaches the wire: a failure on a
                // later chunk must not re-send the chunks that already went out, and must not
                // charge items that never left.
                for (int j = 0; j < this.autoLearnBatch.Count; j++)
                {
                    uint netId = this.autoLearnBatch[j];
                    this.autoLearnAttemptsByNetId.TryGetValue(netId, out int attempts);
                    this.autoLearnAttemptsByNetId[netId] = attempts + 1;
                }

                sent += this.autoLearnBatch.Count;
            }

            this.autoLearnSentTotal += sent;
            this.autoLearnSweepAt = now + AutoLearnPostSendCooldownSeconds;
            this.AutoLearnSetStatus("Sent " + sent + " recipe(s); " + this.autoLearnSentTotal + " this session.");
        }

        // AuraMono ONLY. BackPackSystem is an XDT* type and the BepInEx interop ships none of those,
        // so a managed FindLoadedType/reflection path can never resolve here — it is dead code that
        // only produces a misleading "unavailable" (project rule: prefer-auramono-no-managed-fallback).
        //
        // Every item pointer is pinned for the duration of the walk: the member reads box their
        // values, and a mono-side allocation can trigger a moving SGen pass that relocates the items
        // still queued in this list. Only scalars (netId, entityType) leave the loop, so no pointer
        // outlives the pins.
        private unsafe bool TryCollectAutoLearnNetIds(List<uint> netIds, out string status)
        {
            status = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                status = "Mono API not ready.";
                return false;
            }

            if (!this.TryResolveAuraMonoModule(AutoLearnBackPackSystemTypeName, out IntPtr backPackObj)
                || backPackObj == IntPtr.Zero)
            {
                status = "BackPackSystem module unavailable.";
                return false;
            }

            uint backPackPin = AuraMonoPinNew(backPackObj);
            IntPtr itemListObj;
            try
            {
                IntPtr backPackClass = auraMonoObjectGetClass(backPackObj);
                IntPtr getAllItem = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
                bool needsStorage = true;
                if (getAllItem == IntPtr.Zero)
                {
                    getAllItem = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                    needsStorage = false;
                }

                if (getAllItem == IntPtr.Zero)
                {
                    status = "BackPackSystem.GetAllItem not found.";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                int storageTypeBackpack = 1; // EStorageType.Backpack — same literal AutoSell passes.
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&storageTypeBackpack);
                itemListObj = auraMonoRuntimeInvoke(
                    getAllItem, backPackObj, needsStorage ? (IntPtr)args : IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                {
                    status = "BackPackSystem.GetAllItem returned nothing.";
                    return false;
                }
            }
            finally
            {
                AuraMonoPinFree(backPackPin);
            }

            uint listPin = AuraMonoPinNew(itemListObj);
            try
            {
                // TryEnumerateAuraMonoCollectionItems returns false for an EMPTY collection too, so
                // probe get_Count first — otherwise an empty bag reads as a technical failure.
                IntPtr listClass = auraMonoObjectGetClass(itemListObj);
                IntPtr getCount = listClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(listClass, "get_Count", 0)
                    : IntPtr.Zero;
                int itemCount = this.GetAuraMonoIntCount(itemListObj, getCount);

                List<IntPtr> items = new List<IntPtr>();
                List<uint> pins = new List<uint>();
                bool enumerated = this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, pins);
                try
                {
                    if (!enumerated)
                    {
                        if (itemCount == 0)
                        {
                            return true; // genuinely empty bag, not a failure
                        }

                        status = "Backpack enumeration failed (get_Count=" + itemCount + ").";
                        return false;
                    }

                    for (int i = 0; i < items.Count; i++)
                    {
                        IntPtr itemObj = items[i];
                        if (itemObj == IntPtr.Zero
                            || !this.TryGetDirectBackpackItemEntityType(itemObj, out int entityType)
                            || !IsAutoLearnableEntityType(entityType)
                            || !this.TryGetDirectBackpackItemNetId(itemObj, out uint netId) || netId == 0U)
                        {
                            continue;
                        }

                        if (this.autoLearnAttemptsByNetId.TryGetValue(netId, out int attempts)
                            && attempts >= AutoLearnMaxAttemptsPerItem)
                        {
                            continue;
                        }

                        netIds.Add(netId);
                    }

                    return true;
                }
                finally
                {
                    FreeAuraMonoPins(pins);
                }
            }
            finally
            {
                AuraMonoPinFree(listPin);
            }
        }

        private static bool IsAutoLearnableEntityType(int entityType)
        {
            return entityType == AutoLearnEntityTypeBlueprint
                || entityType == AutoLearnEntityTypeCookbook
                || entityType == AutoLearnEntityTypeMusicSheet;
        }

        // CharacterProtocolManager.LearnRecipes(List<uint>) — static, one reference argument.
        private unsafe bool TryInvokeAutoLearnRecipes(List<uint> netIds, out string status)
        {
            status = string.Empty;
            if (netIds == null || netIds.Count == 0)
            {
                status = "Empty batch.";
                return false;
            }

            if (!this.TryResolveAutoLearnProtocol(out status))
            {
                return false;
            }

            if (!this.TryCreateAutoLearnUIntList(netIds, out IntPtr listObj, out status) || listObj == IntPtr.Zero)
            {
                return false;
            }

            // The list was built by a chain of mono allocations, so keep it rooted across the call:
            // SGen can move an unrooted MonoObject* and the protocol manager would read garbage.
            // AuraMonoPinNew degrades to 0 when the gchandle export is absent, and PinFree(0) is a
            // no-op — on such a build this is exactly the (proven, unpinned) PetFeed shape.
            uint listPin = AuraMonoPinNew(listObj);
            try
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = listObj;
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.autoLearnProtocolMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "LearnRecipes threw exc=0x" + exc.ToInt64().ToString("X") + ".";
                    return false;
                }
            }
            finally
            {
                AuraMonoPinFree(listPin);
            }

            if (MasterLogAutoLearn)
            {
                ModLogger.Msg("[AutoLearn] LearnRecipes sent for " + netIds.Count + " netId(s)");
            }

            return true;
        }

        private bool TryResolveAutoLearnProtocol(out string status)
        {
            status = string.Empty;
            if (this.autoLearnProtocolMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "Mono API not ready.";
                return false;
            }

            IntPtr protocolClass = this.FindAuraMonoClassByFullName(AutoLearnProtocolTypeName);
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(AutoLearnProtocolNamespace, AutoLearnProtocolClassName);
            }

            if (protocolClass == IntPtr.Zero)
            {
                status = "CharacterProtocolManager not found.";
                return false;
            }

            this.autoLearnProtocolMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, AutoLearnProtocolMethodName, 1);
            if (this.autoLearnProtocolMethod == IntPtr.Zero)
            {
                status = "CharacterProtocolManager.LearnRecipes(1 arg) not found.";
                return false;
            }

            return true;
        }

        // Type.GetType(name) + Activator.CreateInstance(type) + List<uint>.Add — the same shape
        // PetFeedFeature uses to hand a List<uint> to a protocol manager.
        private unsafe bool TryCreateAutoLearnUIntList(List<uint> values, out IntPtr listObj, out string status)
        {
            listObj = IntPtr.Zero;
            status = string.Empty;

            this.ResolveAuraFarmRuntimeMethodsViaMono();
            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoStringNew == null
                || auraMonoObjectGetClass == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                status = "List<uint> prerequisites unavailable.";
                return false;
            }

            string[] typeCandidates = new[]
            {
                "System.Collections.Generic.List`1[System.UInt32]",
                "System.Collections.Generic.List`1[[System.UInt32, mscorlib]]",
                "System.Collections.Generic.List`1[[System.UInt32, System.Private.CoreLib]]"
            };

            IntPtr* typeArgs = stackalloc IntPtr[1];
            IntPtr* createArgs = stackalloc IntPtr[1];
            for (int i = 0; i < typeCandidates.Length && listObj == IntPtr.Zero; i++)
            {
                IntPtr typeNameObj = auraMonoStringNew(this.auraMonoRootDomain, typeCandidates[i]);
                if (typeNameObj == IntPtr.Zero)
                {
                    continue;
                }

                typeArgs[0] = typeNameObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                {
                    continue;
                }

                createArgs[0] = typeObj;
                exc = IntPtr.Zero;
                listObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)createArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    listObj = IntPtr.Zero;
                }
            }

            if (listObj == IntPtr.Zero)
            {
                status = "List<uint> create failed.";
                return false;
            }

            if (this.autoLearnUIntListClass == IntPtr.Zero)
            {
                this.autoLearnUIntListClass = auraMonoObjectGetClass(listObj);
            }

            if (this.autoLearnUIntListAddMethod == IntPtr.Zero && this.autoLearnUIntListClass != IntPtr.Zero)
            {
                this.autoLearnUIntListAddMethod = this.FindAuraMonoMethodOnHierarchy(this.autoLearnUIntListClass, "Add", 1);
            }

            if (this.autoLearnUIntListAddMethod == IntPtr.Zero)
            {
                status = "List<uint>.Add unavailable.";
                return false;
            }

            // Add allocates (the backing array grows), so the list itself has to stay rooted while
            // it is being filled — the caller's pin only starts after this returns.
            uint fillPin = AuraMonoPinNew(listObj);
            try
            {
                // Hoisted: inside the loop the frame would grow by a slot per iteration.
                uint value = 0U;
                IntPtr* addArgs = stackalloc IntPtr[1];
                addArgs[0] = (IntPtr)(&value);
                for (int i = 0; i < values.Count; i++)
                {
                    value = values[i];
                    IntPtr exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(this.autoLearnUIntListAddMethod, listObj, (IntPtr)addArgs, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        status = "List<uint>.Add threw exc=0x" + exc.ToInt64().ToString("X") + ".";
                        listObj = IntPtr.Zero;
                        return false;
                    }
                }
            }
            finally
            {
                AuraMonoPinFree(fillPin);
            }

            return true;
        }

        // Failures always reach the log, not just the status string (they are the only diagnostic
        // when the feature silently does nothing); deduped so a stuck state cannot spam.
        private void AutoLearnSetStatus(string status)
        {
            this.autoLearnStatus = status;
            if (string.Equals(status, this.autoLearnLastLoggedStatus, StringComparison.Ordinal))
            {
                return;
            }

            this.autoLearnLastLoggedStatus = status;
            ModLogger.Msg("[AutoLearn] " + status);
        }
    }
}
