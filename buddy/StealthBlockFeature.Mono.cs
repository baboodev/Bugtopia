using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    // AuraMono bindings for StealthBlockFeature. Everything the feature touches is embedded-Mono
    // only, so each call goes through mono_runtime_invoke; no managed reflection path exists.
    //
    // Pointer hygiene (the recurring native-AV class in this codebase):
    //   * class/method IntPtrs are cached (image lifetime), OBJECT pointers never are;
    //   * the MapSpotsSystem DataModule instance is re-resolved per call and pinned across the
    //     invoke — a level-scoped module can be torn down between frames;
    //   * the roster walk enumerates with pins and frees them in a finally, and every element is
    //     scalarised (category/usageId ints) before anything else can allocate on the mono heap.
    public partial class HeartopiaComplete
    {
        private bool TryResolveStealthBlockBindings()
        {
            if (this.stealthBlockGetMapSpotsMethod != IntPtr.Zero
                && this.stealthBlockTryGetShortIdMethod != IntPtr.Zero
                && this.stealthBlockIsBlockedMethod != IntPtr.Zero
                && this.stealthBlockSendValidated
                && this.stealthBlockSpotUsageIdOffset >= 0)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectNew == null
                || auraMonoObjectUnbox == null || auraMonoFieldSetValue == null
                || auraMonoFieldGetOffset == null)
            {
                return false;
            }

            try
            {
                if (this.stealthBlockMapSpotsClass == IntPtr.Zero)
                {
                    this.stealthBlockMapSpotsClass = this.FindAuraMonoClassInImages(
                        "XDTGameSystem.GameplaySystem.MapSpots", "MapSpotsSystem", StealthBlockGameSystemImages);
                    if (this.stealthBlockMapSpotsClass == IntPtr.Zero)
                    {
                        this.stealthBlockMapSpotsClass = this.FindAuraMonoClassByFullName(
                            "XDTGameSystem.GameplaySystem.MapSpots.MapSpotsSystem");
                    }
                }

                if (this.stealthBlockMapSpotsClass == IntPtr.Zero)
                {
                    return false;
                }

                if (this.stealthBlockGetPlayerCountMethod == IntPtr.Zero)
                {
                    this.stealthBlockGetPlayerCountMethod = this.FindAuraMonoMethodOnHierarchy(this.stealthBlockMapSpotsClass, "GetPlayerCount", 0);
                }
                if (this.stealthBlockGetFriendsCountMethod == IntPtr.Zero)
                {
                    this.stealthBlockGetFriendsCountMethod = this.FindAuraMonoMethodOnHierarchy(this.stealthBlockMapSpotsClass, "GetFriendsCount", 0);
                }
                if (this.stealthBlockGetMapSpotsMethod == IntPtr.Zero)
                {
                    // GetMapSpots() and GetMapSpots(Predicate<MapSpotData>) are both present —
                    // paramCount 0 selects the no-arg overload that returns the live list.
                    this.stealthBlockGetMapSpotsMethod = this.FindAuraMonoMethodOnHierarchy(this.stealthBlockMapSpotsClass, "GetMapSpots", 0);
                }

                if (this.stealthBlockTryGetShortIdMethod == IntPtr.Zero)
                {
                    IntPtr playerProtocol = this.FindAuraMonoClassInImages(
                        "XDTDataAndProtocol.ProtocolService.Player", "PlayerProtocolManager", StealthBlockProtocolImages);
                    if (playerProtocol == IntPtr.Zero)
                    {
                        playerProtocol = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Player.PlayerProtocolManager");
                    }
                    if (playerProtocol != IntPtr.Zero)
                    {
                        this.stealthBlockTryGetShortIdMethod = this.FindAuraMonoMethodOnHierarchy(playerProtocol, "TryGetPlayerShortId", 2);
                    }
                }

                if (this.stealthBlockFriendLevelMethod == IntPtr.Zero)
                {
                    IntPtr friendProtocol = this.FindAuraMonoClassInImages(
                        "XDTDataAndProtocol.ProtocolService.Social", "FriendProtocolManager", StealthBlockProtocolImages);
                    if (friendProtocol == IntPtr.Zero)
                    {
                        friendProtocol = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Social.FriendProtocolManager");
                    }
                    if (friendProtocol != IntPtr.Zero)
                    {
                        // bool TryGetFriendLevelByCache(uint playerNetId, out int level) — the only
                        // friend test with an all-scalar signature. TryGetFriend*/TryGetFriendByNetId
                        // hand back a FriendComponent by out, a struct far wider than a pointer:
                        // passing an out slot for that corrupts the stack (memory:
                        // auramono-invoke-out-params).
                        this.stealthBlockFriendLevelMethod = this.FindAuraMonoMethodOnHierarchy(friendProtocol, "TryGetFriendLevelByCache", 2);
                    }
                }

                if (this.stealthBlockIsBlockedMethod == IntPtr.Zero)
                {
                    IntPtr blockProtocol = this.FindAuraMonoClassInImages(
                        "XDTDataAndProtocol.ProtocolService.Social", "BlockListProtocolManager", StealthBlockProtocolImages);
                    if (blockProtocol == IntPtr.Zero)
                    {
                        blockProtocol = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Social.BlockListProtocolManager");
                    }
                    if (blockProtocol != IntPtr.Zero)
                    {
                        this.stealthBlockIsBlockedMethod = this.FindAuraMonoMethodOnHierarchy(blockProtocol, "IsPlayerInBlockList", 1);
                    }
                }

                if (!this.stealthBlockSendValidated)
                {
                    // Both commands proved end-to-end without sending: resolved, inflated,
                    // arity-checked, allocated, fields set, unboxed. Blocking is the one send in this
                    // mod that cannot be walked back — it deletes the friendship on the way — so the
                    // readiness gate has to be something stronger than "the pointers are non-null",
                    // and it has to cost nothing to be wrong.
                    this.stealthBlockSendValidated =
                        this.TryValidateAuraCommand(StealthBlockBlockCommand,
                            new Dictionary<string, object>
                            {
                                ["ShortId"] = 0L,
                                ["Reason"] = StealthBlockReasonDefault,
                            },
                            out _)
                        && this.TryValidateAuraCommand(StealthBlockUnblockCommand,
                            new Dictionary<string, object> { ["ShortId"] = 0L },
                            out _);
                }

                if (this.stealthBlockSpotUsageIdOffset < 0)
                {
                    this.ResolveStealthBlockSpotOffsets();
                }

                bool ready = this.stealthBlockGetMapSpotsMethod != IntPtr.Zero
                    && this.stealthBlockGetPlayerCountMethod != IntPtr.Zero
                    && this.stealthBlockGetFriendsCountMethod != IntPtr.Zero
                    && this.stealthBlockTryGetShortIdMethod != IntPtr.Zero
                    && this.stealthBlockIsBlockedMethod != IntPtr.Zero
                    && this.stealthBlockSendValidated
                    && this.stealthBlockSpotUsageIdOffset >= 0;

                if (!ready && !this.stealthBlockResolveFailedLogged)
                {
                    this.stealthBlockResolveFailedLogged = true;
                    ModLogger.Msg("[StealthBlock] Resolve incomplete: spots=" + (this.stealthBlockGetMapSpotsMethod != IntPtr.Zero)
                        + " counts=" + (this.stealthBlockGetPlayerCountMethod != IntPtr.Zero && this.stealthBlockGetFriendsCountMethod != IntPtr.Zero)
                        + " shortId=" + (this.stealthBlockTryGetShortIdMethod != IntPtr.Zero)
                        + " friend=" + (this.stealthBlockFriendLevelMethod != IntPtr.Zero)
                        + " isBlocked=" + (this.stealthBlockIsBlockedMethod != IntPtr.Zero)
                        + " sends=" + this.stealthBlockSendValidated
                        + " spotOffsets=" + this.stealthBlockSpotUsageIdOffset);
                }

                return ready;
            }
            catch (Exception ex)
            {
                if (!this.stealthBlockResolveFailedLogged)
                {
                    this.stealthBlockResolveFailedLogged = true;
                    ModLogger.Msg("[StealthBlock] Resolve threw: " + ex.Message);
                }
                return false;
            }
        }

        // MapSpotData is a plain struct; mono_field_get_offset reports offsets that include the
        // MonoObject header, so the unboxed payload needs the two-pointer header subtracted
        // (memory: auramono-struct-field-offsets).
        private void ResolveStealthBlockSpotOffsets()
        {
            IntPtr spotClass = this.FindAuraMonoClassInImages(
                "XDTGameSystem.GameplaySystem.MapSpots", "MapSpotData", StealthBlockGameSystemImages);
            if (spotClass == IntPtr.Zero)
            {
                spotClass = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.MapSpots.MapSpotData");
            }
            if (spotClass == IntPtr.Zero)
            {
                return;
            }

            IntPtr categoryField = this.FindAuraMonoFieldOnHierarchy(spotClass, "category");
            IntPtr usageField = this.FindAuraMonoFieldOnHierarchy(spotClass, "usageId");
            if (categoryField == IntPtr.Zero || usageField == IntPtr.Zero)
            {
                return;
            }

            int header = 2 * IntPtr.Size;
            int categoryOffset = (int)auraMonoFieldGetOffset(categoryField) - header;
            int usageOffset = (int)auraMonoFieldGetOffset(usageField) - header;
            if (categoryOffset < 0 || usageOffset < 0)
            {
                return;
            }

            this.stealthBlockSpotCategoryOffset = categoryOffset;
            this.stealthBlockSpotUsageIdOffset = usageOffset;
            ModLogger.Msg("[StealthBlock] MapSpotData offsets: category@" + categoryOffset + " usageId@" + usageOffset);
        }

        // Instance-method-on-DataModule scalar call. The module instance is resolved fresh and
        // pinned for the duration — never cached across frames.
        private unsafe bool TryStealthBlockInvokeScalar(IntPtr moduleClass, IntPtr method, out int value)
        {
            value = 0;
            if (moduleClass == IntPtr.Zero || method == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr instance = this.TryGetAuraMonoDataModuleInstance(moduleClass);
            if (instance == IntPtr.Zero)
            {
                return false;
            }

            uint pin = AuraMonoPinNew(instance);
            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(method, instance, IntPtr.Zero, ref exc);
                return exc == IntPtr.Zero && boxed != IntPtr.Zero && this.TryUnboxMonoInt32(boxed, out value);
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        // Fills stealthBlockScanNetIds with the netIds of every OTHER player spot in the room and
        // reports whether our own spot showed up. selfSeen == false means the read is not
        // trustworthy (world change / service down) and the caller must not diff on it.
        private unsafe bool TryScanStealthBlockRoster(out bool selfSeen)
        {
            selfSeen = false;
            this.stealthBlockScanNetIds.Clear();

            if (!this.TryResolveSelfPlayerNetId(out uint selfNetId) || selfNetId == 0U)
            {
                return false;
            }

            IntPtr instance = this.TryGetAuraMonoDataModuleInstance(this.stealthBlockMapSpotsClass);
            if (instance == IntPtr.Zero)
            {
                return false;
            }

            uint instancePin = AuraMonoPinNew(instance);
            List<IntPtr> items = new List<IntPtr>(64);
            List<uint> pins = new List<uint>(64);
            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr listObj = auraMonoRuntimeInvoke(this.stealthBlockGetMapSpotsMethod, instance, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
                {
                    return false;
                }

                uint listPin = AuraMonoPinNew(listObj);
                try
                {
                    if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items, pins))
                    {
                        return false;
                    }

                    for (int i = 0; i < items.Count; i++)
                    {
                        IntPtr data = auraMonoObjectUnbox(items[i]);
                        if (data == IntPtr.Zero)
                        {
                            continue;
                        }

                        int category = Marshal.ReadInt32(data, this.stealthBlockSpotCategoryOffset);
                        if (category != StealthBlockSpotEnumPlayer)
                        {
                            continue;
                        }

                        uint netId = unchecked((uint)Marshal.ReadInt32(data, this.stealthBlockSpotUsageIdOffset));
                        if (netId == 0U)
                        {
                            continue;
                        }

                        if (netId == selfNetId)
                        {
                            selfSeen = true;
                            continue;
                        }

                        if (!this.stealthBlockScanNetIds.Contains(netId))
                        {
                            this.stealthBlockScanNetIds.Add(netId);
                        }
                    }

                    return true;
                }
                finally
                {
                    AuraMonoPinFree(listPin);
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
                AuraMonoPinFree(instancePin);
            }
        }

        // static bool TryGetPlayerShortId(uint netId, out long shortId). mono_runtime_invoke takes
        // the ADDRESS of real storage for the out slot — a long is pointer-sized here, but the
        // address form is what keeps this correct if the signature ever widens.
        private unsafe bool TryStealthBlockResolveShortId(uint netId, out long shortId)
        {
            shortId = 0L;
            if (this.stealthBlockTryGetShortIdMethod == IntPtr.Zero)
            {
                return false;
            }

            uint netIdArg = netId;
            long resolved = 0L;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&netIdArg);
            args[1] = (IntPtr)(&resolved);

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.stealthBlockTryGetShortIdMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero || !this.TryUnboxMonoBoolean(boxed, out bool ok) || !ok)
            {
                return false;
            }

            shortId = resolved;
            return shortId != 0L;
        }

        // static bool TryGetFriendLevelByCache(uint playerNetId, out int level). Returning false is
        // "not a known friend"; a resolve failure also returns false, which the caller compensates
        // for with the GetFriendsCount() cross-check (fail-closed on friends).
        private unsafe bool TryStealthBlockIsFriend(uint netId)
        {
            if (this.stealthBlockFriendLevelMethod == IntPtr.Zero)
            {
                return false;
            }

            uint netIdArg = netId;
            int level = 0;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&netIdArg);
            args[1] = (IntPtr)(&level);

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.stealthBlockFriendLevelMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero && boxed != IntPtr.Zero && this.TryUnboxMonoBoolean(boxed, out bool isFriend) && isFriend;
        }

        // static bool IsPlayerInBlockList(long shortId) — the SERVER-synced answer (the ECS block
        // filter), which is why it is the arming confirmation rather than "we sent the command".
        private unsafe bool TryStealthBlockIsBlocked(long shortId, out bool blocked)
        {
            blocked = false;
            if (this.stealthBlockIsBlockedMethod == IntPtr.Zero)
            {
                return false;
            }

            long shortIdArg = shortId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&shortIdArg);

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.stealthBlockIsBlockedMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero && boxed != IntPtr.Zero && this.TryUnboxMonoBoolean(boxed, out blocked);
        }

        private bool TryStealthBlockSendBlock(long shortId)
        {
            // IsFollow / IsFollowing are left false on purpose. The game fills them from a profile
            // fetch purely so the server can also drop the follow relation; false means "do not
            // touch follows", which is both cheaper (no per-player profile round trip) and less
            // destructive than what the vanilla path does.
            // ShortId is Int64 and Reason is an int-backed enum — the shared sender takes the write
            // width from each value's runtime type, so the L suffix is load-bearing.
            return this.TryStealthBlockSendCommand(StealthBlockBlockCommand,
                new Dictionary<string, object>
                {
                    ["ShortId"] = shortId,
                    ["Reason"] = StealthBlockReasonDefault,
                },
                shortId, "block");
        }

        private bool TryStealthBlockSendUnblock(long shortId)
        {
            return this.TryStealthBlockSendCommand(StealthBlockUnblockCommand,
                new Dictionary<string, object> { ["ShortId"] = shortId },
                shortId, "unblock");
        }

        private bool TryStealthBlockSendCommand(string commandFullName, IDictionary<string, object> fields,
            long shortId, string label)
        {
            if (!this.TryAuraSendCommand(commandFullName, fields, AuraChannelReliable, true, out string status))
            {
                // Logged for every failure, not just a thrown one: a block that quietly did not go
                // out leaves the roster believing it did.
                ModLogger.Msg("[StealthBlock] " + label + " send failed for shortId=" + shortId + ": " + status);
                return false;
            }

            return true;
        }
    }
}
