using System;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;

namespace HeartopiaMod
{
    // Stranger Chat Bypass — show strangers' nearby chat (full text: chat log + head bubbles)
    // instead of the masked bubbles the game gives non-friends.
    //
    // HOW THE GAME GATES IT: every incoming nearby message is resolved live by
    // XDTLevelAndEntity.Game.Module.Chat.ChatVisibilitySystem.ResolveMessageCore (ChatModule:133;
    // no per-player caching). After the special-context checks (self, hide&seek, self-room,
    // multi-build, party, break-the-ice, card table) the last gate is the private
    // IsFriendChatVisible(long shortId) -> bool; false falls through to Default/Masked.
    //
    // We detour that leaf predicate and, while the toggle is on, answer true — every player
    // resolves as a chat-visible friend (ChatVisibilityReason.Friend: record + bubble + emoji).
    // While the toggle is off the body forwards to the original via the trampoline, so vanilla
    // filtering is back for the very next message — nothing to restore.
    //
    // WHY A DETOUR (replaced the SelfRoomSystem.IsInSelfRoom field-force, 2026-08-27): forcing
    // IsInSelfRoom=true poisoned every other consumer of that property (room timer/panels, and the
    // protocol guards — the old fallback path could even send SelfRoomSetChatVisibility_InRoom,
    // flipping a REAL self-room's server-side setting with nothing ever sending the inverse). The
    // restore also only rewrote the local field, could silently miss its snapshot, and the whole
    // thing needed a 3 s sustained re-apply. The detour has none of that surface: chat-scoped,
    // zero server commands, no game state mutated, no restore machinery.
    //
    // ABI: instance method, one long arg -> native (IntPtr self in RCX, long shortId in RDX),
    // managed bool returned in AL -> byte return. The body is allocation-free (one volatile bool
    // read, then constant or trampoline forward — the stock call), safe during teardown.
    // The detour is image-lifetime: installed once via the world-ready gate, never undone
    // (memory: native-detours-world-change-corruption; toggling is the flag, not Apply/Undo).
    public partial class HeartopiaComplete
    {
        private const string StrangerChatWorldReadyCallbackName = "StrangerChatBypass";

        private static readonly string[] StrangerChatImageNames =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
            "Client", "Client.dll"
        };

        private bool strangerChatCallbackRegistered;
        private bool strangerChatHookTried;

        // Written on the main thread (toggle / per-frame mirror); read by the native hook body.
        private static volatile bool strangerChatBypassActive;

        private static NativeDetour strangerChatFriendVisibleDetour;
        private static FriendChatVisibleHookDelegate strangerChatFriendVisibleKeepAlive; // anti-GC
        private static FriendChatVisibleHookDelegate strangerChatFriendVisibleTrampoline;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte FriendChatVisibleHookDelegate(IntPtr self, long targetShortId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr StrangerChatCompileMethodDelegate(IntPtr method);

        private void StrangerChatLog(string message)
        {
            if (!MasterLogStrangerChat || string.IsNullOrEmpty(message))
            {
                return;
            }

            ModLogger.Msg("[StrangerChat] " + message);
        }

        // Cheap per-frame tick: with the toggle off (default) this is a bool test + a static write.
        // Mirrors the persisted toggle into the static flag the hook body reads (covers config
        // restore at startup as well as UI flips), and hands the install to the world-ready gate.
        private void ProcessStrangerChatBypassOnUpdate()
        {
            if (!this.strangerChatBypassEnabled)
            {
                strangerChatBypassActive = false; // installed hook (if any) forwards -> vanilla
                return;
            }

            strangerChatBypassActive = true;
            if (this.strangerChatHookTried || strangerChatFriendVisibleTrampoline != null)
            {
                return;
            }

            // Hook installs run on the world-ready gate, never from a retry timer here
            // (AGENTS.md §1 hard rule). Registration is idempotent.
            if (!this.strangerChatCallbackRegistered)
            {
                this.strangerChatCallbackRegistered = true;
                this.RegisterWorldReadyCallback(StrangerChatWorldReadyCallbackName, this.TryInstallStrangerChatHookOnWorldReady);
            }
        }

        // Returns true when settled for this world (installed, or permanently unavailable), false
        // to be retried by the gate.
        private bool TryInstallStrangerChatHookOnWorldReady()
        {
            if (this.strangerChatHookTried || strangerChatFriendVisibleTrampoline != null)
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
                StrangerChatCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<StrangerChatCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.strangerChatHookTried = true;
                    ModLogger.Msg("[StrangerChat] mono_compile_method unavailable — bypass off.");
                    return true;
                }

                const string nameSpace = "XDTLevelAndEntity.Game.Module.Chat";
                const string shortName = "ChatVisibilitySystem";
                IntPtr cls = this.FindAuraMonoClassInImages(nameSpace, shortName, StrangerChatImageNames);
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassByFullName(nameSpace + "." + shortName);
                }
                if (cls == IntPtr.Zero)
                {
                    this.StrangerChatLog("ChatVisibilitySystem not loaded yet — retrying.");
                    return false; // image not loaded yet — retry
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "IsFriendChatVisible", 1);
                if (method == IntPtr.Zero)
                {
                    this.strangerChatHookTried = true;
                    ModLogger.Msg("[StrangerChat] ChatVisibilitySystem.IsFriendChatVisible(1) not found — bypass off (game update?).");
                    return true;
                }

                IntPtr nativePtr = compile(method);
                if (nativePtr == IntPtr.Zero)
                {
                    this.strangerChatHookTried = true;
                    ModLogger.Msg("[StrangerChat] mono_compile_method returned null for IsFriendChatVisible — bypass off.");
                    return true;
                }

                strangerChatFriendVisibleKeepAlive = StrangerChatFriendVisibleDetourBody;
                strangerChatFriendVisibleDetour = new NativeDetour(nativePtr, strangerChatFriendVisibleKeepAlive);
                strangerChatFriendVisibleTrampoline = strangerChatFriendVisibleDetour.GenerateTrampoline<FriendChatVisibleHookDelegate>();
                if (strangerChatFriendVisibleTrampoline == null)
                {
                    // Install rollback, not a live-detour teardown (the only case where Undo is
                    // safe — memory: native-detours-world-change-corruption).
                    try { strangerChatFriendVisibleDetour?.Undo(); } catch { }
                    strangerChatFriendVisibleDetour = null;
                    strangerChatFriendVisibleKeepAlive = null;
                    this.strangerChatHookTried = true;
                    ModLogger.Msg("[StrangerChat] trampoline unavailable for IsFriendChatVisible; detour reverted — bypass off.");
                    return true;
                }

                ModLogger.Msg("[StrangerChat] hooked ChatVisibilitySystem.IsFriendChatVisible @0x" + nativePtr.ToInt64().ToString("X"));
                return true;
            }
            catch (Exception ex)
            {
                this.strangerChatHookTried = true;
                try { strangerChatFriendVisibleDetour?.Undo(); } catch { }
                strangerChatFriendVisibleDetour = null;
                strangerChatFriendVisibleKeepAlive = null;
                strangerChatFriendVisibleTrampoline = null;
                ModLogger.Msg("[StrangerChat] IsFriendChatVisible hook install failed: " + ex.Message + " — bypass off.");
                return true;
            }
        }

        // Native->coreclr reverse-pinvoke body. Allocation-free, no Mono calls, no logging: hit
        // once per incoming nearby message, and can run from game code mid-teardown.
        private static byte StrangerChatFriendVisibleDetourBody(IntPtr self, long targetShortId)
        {
            if (strangerChatBypassActive)
            {
                return 1; // every player resolves as a chat-visible friend
            }

            FriendChatVisibleHookDelegate trampoline = strangerChatFriendVisibleTrampoline;
            return trampoline != null ? trampoline(self, targetShortId) : (byte)0; // 0 = vanilla deny
        }
    }
}
