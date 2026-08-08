using System;
using System.Runtime.InteropServices;
using System.Threading;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HeartopiaMod
{
    // AuraMono NativeDetour hooks for Logan log upload, town merge enter, antispam report, UploadCheat.
    // Each detour is installed ONLY while its matching privacyBlock* toggle is on (lazy, on demand);
    // with all toggles off we install zero detours, so world-change teardown never runs these
    // mono->coreclr callbacks. Bodies are allocation/IO-free: bump a counter and block or forward.
    public partial class HeartopiaComplete
    {
        public bool privacyBlockLogUploads;
        public bool privacyBlockRoomMerges;
        public bool privacyBlockSpamReports;
        public bool privacyBlockUploadCheat;
        public bool privacyBlockFriendVisitNotify;

        internal static int privacyBlockedLogCount;
        internal static int privacyBlockedMergeCount;
        internal static int privacyBlockedSpamCount;
        internal static int privacyUploadCheatSeenCount;
        internal static int privacyBlockedUploadCheatCount;
        internal static int privacyBlockedFriendVisitCount;

        private const float PrivacyBlockHookRetrySeconds = 5f;
        private const float PrivacyBlockDiagIntervalSeconds = 30f;
        private float privacyBlockNextHookAttemptAt = -999f;
        private float privacyBlockNextDiagAt = -999f;

        private static readonly string[] PrivacyLoganImageNames =
        {
            "XDTBaseService", "XDTBaseService.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll"
        };

        private static readonly string[] PrivacyUploadSystemImageNames =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll"
        };

        private static readonly string[] PrivacyProtocolImageNames =
        {
            "XDTDataAndProtocol", "XDTDataAndProtocol.dll",
            "Client", "Client.dll"
        };

        // EnterRoomNotifyFriendCommand lives in the shared-module image (EcsClient), not the
        // protocol one — the Social.Friend network structs are all defined there.
        private static readonly string[] PrivacyCommandImageNames =
        {
            "EcsClient", "EcsClient.dll",
            "Client", "Client.dll"
        };

        private bool privacyLogHookTried;
        private bool privacyMergeHookTried;
        private bool privacySpamHookTried;
        private bool privacyUploadCheatHookTried;
        private bool privacyFriendVisitHookTried;
        private bool privacyBlockHooksHardFailed;

        private static NativeDetour privacyLogDetour;
        private static NativeDetour privacyMergeDetour;
        private static NativeDetour privacySpamDetour;
        private static NativeDetour privacyUploadCheatDetour;
        private static NativeDetour privacyFriendVisitDetour;

        private static TryUploadLogHookDelegate privacyLogHookKeepAlive;
        private static EnterRoomMergeHookDelegate privacyMergeHookKeepAlive;
        private static ReportHookDelegate privacySpamHookKeepAlive;
        private static UploadCheatHookDelegate privacyUploadCheatHookKeepAlive;
        private static SendCommandHookDelegate privacyFriendVisitHookKeepAlive;

        private static TryUploadLogHookDelegate privacyLogTrampoline;
        private static EnterRoomMergeHookDelegate privacyMergeTrampoline;
        private static ReportHookDelegate privacySpamTrampoline;
        private static UploadCheatHookDelegate privacyUploadCheatTrampoline;
        private static SendCommandHookDelegate privacyFriendVisitTrampoline;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void TryUploadLogHookDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EnterRoomMergeHookDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ReportHookDelegate(IntPtr encodedShortId, int type);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void UploadCheatHookDelegate(IntPtr self, IntPtr objectId, IntPtr stream, IntPtr success, IntPtr failure);

        // WebRequestUtility.SendCommand<T>(T command, bool needAuthed, ChannelType channelType) -> int,
        // inflated over EnterRoomNotifyFriendCommand. That command is a STRUCT of a single long, so
        // Win64 passes it by value in RCX (not by pointer) — register-identical to an IntPtr slot,
        // which is all the trampoline needs since we forward the argument unmodified. needAuthed
        // (bool) and channelType (enum:int) ride RDX/R8 as int32. Arity is validated (3) before the
        // detour goes in, and the install refuses to splice if mono turns out to share this code
        // with another instantiation (see TryInstallPrivacyFriendVisitHook).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SendCommandHookDelegate(IntPtr command, int needAuthed, int channelType);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr PrivacyBlockCompileMethodDelegate(IntPtr method);

        private void ProcessPrivacyBlockOnUpdate()
        {
            if (this.privacyBlockHooksHardFailed)
            {
                return;
            }

            // Option B: install a NativeDetour only for a privacy feature the user has actually turned
            // on. With everything off (the default) we install ZERO detours, so the world-change
            // teardown never runs our mono->coreclr hook callbacks -> no ExecutionEngineException
            // regression for users who don't use this feature.
            if (!this.PrivacyBlockAnyEnabled())
            {
                return;
            }

            if (this.PrivacyBlockAllHooksSettled())
            {
                return;
            }

            // World-ready gate (LoadingClosedEvent): the privacy flags are persisted, so the 5 s
            // install retry used to start on the login screen where the target classes don't exist.
            // The timer is re-armed on world-ready, so the hooks land right after the splash.
            if (!this.IsWorldReady)
            {
                return;
            }

            if (Time.unscaledTime < this.privacyBlockNextHookAttemptAt)
            {
                return;
            }

            this.privacyBlockNextHookAttemptAt = Time.unscaledTime + PrivacyBlockHookRetrySeconds;
            this.EnsurePrivacyBlockHooks();
        }

        private bool PrivacyBlockAnyEnabled()
            => this.privacyBlockLogUploads || this.privacyBlockRoomMerges || this.privacyBlockSpamReports
                || this.privacyBlockUploadCheat || this.privacyBlockFriendVisitNotify;

        // A hook is "settled" when its feature is disabled (nothing to install) or it is already
        // installed/tried. Enabling a toggle later un-settles it, so the install loop resumes.
        private bool PrivacyBlockHookSettled(bool enabled, bool tried, Delegate trampoline) => !enabled || trampoline != null || tried;

        private bool PrivacyBlockAllHooksSettled()
        {
            return this.PrivacyBlockHookSettled(this.privacyBlockLogUploads, this.privacyLogHookTried, privacyLogTrampoline)
                && this.PrivacyBlockHookSettled(this.privacyBlockRoomMerges, this.privacyMergeHookTried, privacyMergeTrampoline)
                && this.PrivacyBlockHookSettled(this.privacyBlockSpamReports, this.privacySpamHookTried, privacySpamTrampoline)
                && this.PrivacyBlockHookSettled(this.privacyBlockUploadCheat, this.privacyUploadCheatHookTried, privacyUploadCheatTrampoline)
                && this.PrivacyBlockHookSettled(this.privacyBlockFriendVisitNotify, this.privacyFriendVisitHookTried, privacyFriendVisitTrampoline);
        }

        private void PrivacyBlockDiag(string message)
        {
            if (Time.unscaledTime < this.privacyBlockNextDiagAt)
            {
                return;
            }

            this.privacyBlockNextDiagAt = Time.unscaledTime + PrivacyBlockDiagIntervalSeconds;
            ModLogger.Msg("[PrivacyBlock] " + message);
        }

        private IntPtr ResolvePrivacyBlockClass(string nameSpace, string shortName, string[] imageNames, string fullTypeNameFallback)
        {
            IntPtr cls = this.FindAuraMonoClassInImages(nameSpace, shortName, imageNames);
            if (cls != IntPtr.Zero)
            {
                return cls;
            }

            if (!string.IsNullOrWhiteSpace(fullTypeNameFallback))
            {
                cls = this.FindAuraMonoClassByFullName(fullTypeNameFallback);
            }

            return cls;
        }

        private IntPtr FindPrivacyBlockMethod(IntPtr classPtr, string methodName, params int[] paramCounts)
        {
            if (classPtr == IntPtr.Zero || paramCounts == null || paramCounts.Length == 0)
            {
                return IntPtr.Zero;
            }

            for (int i = 0; i < paramCounts.Length; i++)
            {
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, paramCounts[i]);
                if (method != IntPtr.Zero)
                {
                    return method;
                }
            }

            return IntPtr.Zero;
        }

        private void EnsurePrivacyBlockHooks()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return;
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                PrivacyBlockCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<PrivacyBlockCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.privacyBlockHooksHardFailed = true;
                    ModLogger.Msg("[PrivacyBlock] mono_compile_method unavailable — hooks disabled");
                    return;
                }

                if (this.privacyBlockLogUploads && !this.privacyLogHookTried)
                {
                    this.TryInstallPrivacyLogHook(compile);
                }

                if (this.privacyBlockRoomMerges && !this.privacyMergeHookTried)
                {
                    this.TryInstallPrivacyMergeHook(compile);
                }

                if (this.privacyBlockSpamReports && !this.privacySpamHookTried)
                {
                    this.TryInstallPrivacySpamHook(compile);
                }

                if (this.privacyBlockUploadCheat && !this.privacyUploadCheatHookTried)
                {
                    this.TryInstallPrivacyUploadCheatHook(compile);
                }

                if (this.privacyBlockFriendVisitNotify && !this.privacyFriendVisitHookTried)
                {
                    this.TryInstallPrivacyFriendVisitHook(compile);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[PrivacyBlock] install pass failed: " + ex.Message);
            }
        }

        private void TryInstallPrivacyLogHook(PrivacyBlockCompileMethodDelegate compile)
        {
            const string nameSpace = "XDTBaseService.Services.Logan";
            const string shortName = "LoganUploadService";
            IntPtr cls = this.ResolvePrivacyBlockClass(nameSpace, shortName, PrivacyLoganImageNames, nameSpace + "." + shortName);
            if (cls == IntPtr.Zero)
            {
                this.PrivacyBlockDiag("LoganUploadService class not loaded yet");
                return;
            }

            IntPtr method = this.FindPrivacyBlockMethod(cls, "TryUploadLog", 0, 1);
            if (method == IntPtr.Zero)
            {
                this.MarkPrivacyHookMissing(ref this.privacyLogHookTried, nameSpace + "." + shortName + ".TryUploadLog");
                return;
            }

            IntPtr nativePtr = compile(method);
            if (nativePtr == IntPtr.Zero)
            {
                this.PrivacyBlockDiag("mono_compile_method null for LoganUploadService log upload");
                return;
            }

            this.privacyLogHookTried = true;
            privacyLogHookKeepAlive = PrivacyLogDetourBody;
            privacyLogDetour = new NativeDetour(nativePtr, privacyLogHookKeepAlive);
            privacyLogTrampoline = privacyLogDetour.GenerateTrampoline<TryUploadLogHookDelegate>();
            if (privacyLogTrampoline == null)
            {
                try { privacyLogDetour?.Undo(); } catch { }
                privacyLogDetour = null;
                privacyLogHookKeepAlive = null;
                this.privacyLogHookTried = false;
                ModLogger.Msg("[PrivacyBlock] trampoline unavailable for Logan log upload; detour reverted");
                return;
            }

            ModLogger.Msg("[PrivacyBlock] hooked LoganUploadService log upload @0x" + nativePtr.ToInt64().ToString("X"));
        }

        private void TryInstallPrivacyMergeHook(PrivacyBlockCompileMethodDelegate compile)
        {
            const string nameSpace = "XDTDataAndProtocol.ProtocolService.TownMerge";
            const string shortName = "TownMergeProtocolManager";
            IntPtr cls = this.ResolvePrivacyBlockClass(nameSpace, shortName, PrivacyProtocolImageNames, nameSpace + "." + shortName);
            if (cls == IntPtr.Zero)
            {
                this.PrivacyBlockDiag("TownMergeProtocolManager class not loaded yet");
                return;
            }

            IntPtr method = this.FindPrivacyBlockMethod(cls, "EnterRoomMerge", 0);
            if (method == IntPtr.Zero)
            {
                this.MarkPrivacyHookMissing(ref this.privacyMergeHookTried, nameSpace + "." + shortName + ".EnterRoomMerge");
                return;
            }

            IntPtr nativePtr = compile(method);
            if (nativePtr == IntPtr.Zero)
            {
                this.PrivacyBlockDiag("mono_compile_method null for TownMergeProtocolManager.EnterRoomMerge");
                return;
            }

            this.privacyMergeHookTried = true;
            privacyMergeHookKeepAlive = PrivacyMergeDetourBody;
            privacyMergeDetour = new NativeDetour(nativePtr, privacyMergeHookKeepAlive);
            privacyMergeTrampoline = privacyMergeDetour.GenerateTrampoline<EnterRoomMergeHookDelegate>();
            if (privacyMergeTrampoline == null)
            {
                try { privacyMergeDetour?.Undo(); } catch { }
                privacyMergeDetour = null;
                privacyMergeHookKeepAlive = null;
                ModLogger.Msg("[PrivacyBlock] trampoline unavailable for EnterRoomMerge; detour reverted");
                return;
            }

            ModLogger.Msg("[PrivacyBlock] hooked TownMergeProtocolManager.EnterRoomMerge @0x" + nativePtr.ToInt64().ToString("X"));
        }

        private void TryInstallPrivacySpamHook(PrivacyBlockCompileMethodDelegate compile)
        {
            const string nameSpace = "XDTDataAndProtocol.ProtocolService.Report";
            const string shortName = "ReportProtocolManager";
            IntPtr cls = this.ResolvePrivacyBlockClass(nameSpace, shortName, PrivacyProtocolImageNames, nameSpace + "." + shortName);
            if (cls == IntPtr.Zero)
            {
                this.PrivacyBlockDiag("ReportProtocolManager class not loaded yet");
                return;
            }

            IntPtr method = this.FindPrivacyBlockMethod(cls, "Report", 2, 1);
            if (method == IntPtr.Zero)
            {
                this.MarkPrivacyHookMissing(ref this.privacySpamHookTried, nameSpace + "." + shortName + ".Report");
                return;
            }

            IntPtr nativePtr = compile(method);
            if (nativePtr == IntPtr.Zero)
            {
                this.PrivacyBlockDiag("mono_compile_method null for ReportProtocolManager.Report");
                return;
            }

            this.privacySpamHookTried = true;
            privacySpamHookKeepAlive = PrivacySpamDetourBody;
            privacySpamDetour = new NativeDetour(nativePtr, privacySpamHookKeepAlive);
            privacySpamTrampoline = privacySpamDetour.GenerateTrampoline<ReportHookDelegate>();
            if (privacySpamTrampoline == null)
            {
                try { privacySpamDetour?.Undo(); } catch { }
                privacySpamDetour = null;
                privacySpamHookKeepAlive = null;
                ModLogger.Msg("[PrivacyBlock] trampoline unavailable for Report; detour reverted");
                return;
            }

            ModLogger.Msg("[PrivacyBlock] hooked ReportProtocolManager.Report @0x" + nativePtr.ToInt64().ToString("X"));
        }

        private void TryInstallPrivacyUploadCheatHook(PrivacyBlockCompileMethodDelegate compile)
        {
            const string nameSpace = "XDTGUI.Module.Upload";
            const string shortName = "UploadSystem";
            IntPtr cls = this.ResolvePrivacyBlockClass(nameSpace, shortName, PrivacyUploadSystemImageNames, nameSpace + "." + shortName);
            if (cls == IntPtr.Zero)
            {
                this.PrivacyBlockDiag("UploadSystem class not loaded yet");
                return;
            }

            IntPtr method = this.FindPrivacyBlockMethod(cls, "UploadCheat", 4, 3, 5);
            if (method == IntPtr.Zero)
            {
                this.MarkPrivacyHookMissing(ref this.privacyUploadCheatHookTried, nameSpace + "." + shortName + ".UploadCheat");
                return;
            }

            IntPtr nativePtr = compile(method);
            if (nativePtr == IntPtr.Zero)
            {
                this.PrivacyBlockDiag("mono_compile_method null for UploadSystem.UploadCheat");
                return;
            }

            this.privacyUploadCheatHookTried = true;
            privacyUploadCheatHookKeepAlive = PrivacyUploadCheatDetourBody;
            privacyUploadCheatDetour = new NativeDetour(nativePtr, privacyUploadCheatHookKeepAlive);
            privacyUploadCheatTrampoline = privacyUploadCheatDetour.GenerateTrampoline<UploadCheatHookDelegate>();
            if (privacyUploadCheatTrampoline == null)
            {
                try { privacyUploadCheatDetour?.Undo(); } catch { }
                privacyUploadCheatDetour = null;
                privacyUploadCheatHookKeepAlive = null;
                this.privacyUploadCheatHookTried = false;
                ModLogger.Msg("[PrivacyBlock] trampoline unavailable for UploadCheat; detour reverted");
                return;
            }

            ModLogger.Msg("[PrivacyBlock] hooked UploadSystem.UploadCheat @0x" + nativePtr.ToInt64().ToString("X"));
        }

        // Blocks the outgoing "I arrived at your room" notify. The notification is CLIENT-authored:
        // FriendNoticeSystem.HandleTransferNotice() sends EnterRoomNotifyFriendCommand after a
        // "go to friend" transfer completes, the server only relays it to the friend, whose client
        // turns it into the FriendArrivedTipPanel toast.
        //
        // The hook sits on the inflated SendCommand<EnterRoomNotifyFriendCommand> — the LAST gate
        // before the wire — rather than on HandleTransferNotice, because that method clears
        // _transferFriendId/_friendName at its very end: a no-op detour there would leave the
        // notice armed and fire it on the NEXT level load instead of cancelling it.
        private void TryInstallPrivacyFriendVisitHook(PrivacyBlockCompileMethodDelegate compile)
        {
            const string sendNameSpace = "XDTDataAndProtocol.ProtocolService";
            const string sendShortName = "WebRequestUtility";
            IntPtr sendCls = this.ResolvePrivacyBlockClass(sendNameSpace, sendShortName, PrivacyProtocolImageNames,
                sendNameSpace + "." + sendShortName);
            if (sendCls == IntPtr.Zero)
            {
                this.PrivacyBlockDiag("WebRequestUtility class not loaded yet");
                return;
            }

            IntPtr openSend = this.FindPrivacyBlockMethod(sendCls, "SendCommand", 3);
            if (openSend == IntPtr.Zero)
            {
                this.MarkPrivacyHookMissing(ref this.privacyFriendVisitHookTried, sendNameSpace + "." + sendShortName + ".SendCommand");
                return;
            }

            const string cmdNameSpace = "XDT.Scene.Shared.Modules.Social.Friend";
            IntPtr cmdCls = this.ResolvePrivacyBlockClass(cmdNameSpace, "EnterRoomNotifyFriendCommand", PrivacyCommandImageNames,
                cmdNameSpace + ".EnterRoomNotifyFriendCommand");
            if (cmdCls == IntPtr.Zero)
            {
                this.PrivacyBlockDiag("EnterRoomNotifyFriendCommand class not loaded yet");
                return;
            }

            if (!this.TryInstantCatchInflateAuraSendCommand(openSend, cmdCls, out IntPtr inflated) || inflated == IntPtr.Zero)
            {
                this.MarkPrivacyHookMissing(ref this.privacyFriendVisitHookTried, "SendCommand<EnterRoomNotifyFriendCommand> inflate");
                return;
            }

            IntPtr nativePtr = compile(inflated);
            if (nativePtr == IntPtr.Zero)
            {
                this.PrivacyBlockDiag("mono_compile_method null for SendCommand<EnterRoomNotifyFriendCommand>");
                return;
            }

            // Shared-code guard. Detouring a gsharedvt body would silently kill EVERY command that
            // shares it, so prove the instantiation got dedicated code first: inflate the same open
            // method over DeleteFriendNetworkCommand — a struct with the IDENTICAL {long} layout, the
            // worst case for sharing — and require a different entry point. Probe failure is treated
            // as "cannot prove it is safe" and aborts the install rather than guessing.
            IntPtr probeCls = this.ResolvePrivacyBlockClass(cmdNameSpace, "DeleteFriendNetworkCommand", PrivacyCommandImageNames,
                cmdNameSpace + ".DeleteFriendNetworkCommand");
            IntPtr probeNative = IntPtr.Zero;
            if (probeCls != IntPtr.Zero
                && this.TryInstantCatchInflateAuraSendCommand(openSend, probeCls, out IntPtr probeInflated)
                && probeInflated != IntPtr.Zero)
            {
                probeNative = compile(probeInflated);
            }

            if (probeNative == IntPtr.Zero || probeNative == nativePtr)
            {
                this.privacyFriendVisitHookTried = true;
                ModLogger.Msg("[PrivacyBlock] SendCommand<EnterRoomNotifyFriendCommand> shares code with another"
                    + " instantiation (probe=0x" + probeNative.ToInt64().ToString("X")
                    + " target=0x" + nativePtr.ToInt64().ToString("X") + ") — hook refused");
                return;
            }

            this.privacyFriendVisitHookTried = true;
            privacyFriendVisitHookKeepAlive = PrivacyFriendVisitDetourBody;
            privacyFriendVisitDetour = new NativeDetour(nativePtr, privacyFriendVisitHookKeepAlive);
            privacyFriendVisitTrampoline = privacyFriendVisitDetour.GenerateTrampoline<SendCommandHookDelegate>();
            if (privacyFriendVisitTrampoline == null)
            {
                try { privacyFriendVisitDetour?.Undo(); } catch { }
                privacyFriendVisitDetour = null;
                privacyFriendVisitHookKeepAlive = null;
                this.privacyFriendVisitHookTried = false;
                ModLogger.Msg("[PrivacyBlock] trampoline unavailable for friend-visit notify; detour reverted");
                return;
            }

            ModLogger.Msg("[PrivacyBlock] hooked SendCommand<EnterRoomNotifyFriendCommand> @0x" + nativePtr.ToInt64().ToString("X"));
        }

        private void MarkPrivacyHookMissing(ref bool triedFlag, string label)
        {
            triedFlag = true;
            ModLogger.Msg("[PrivacyBlock] " + label + " not found — hook skipped");
        }

        // These bodies run as native->coreclr reverse-pinvoke callbacks from mono-compiled game code,
        // including during world-change teardown. Keep them allocation- and IO-free on the hot path:
        // only bump a counter and either block (return) or forward via the trampoline. No per-call
        // ModLogger.Msg / mono-string reads — that managed allocation inside the native callback at a
        // fragile moment was part of the world-change ExecutionEngineException exposure.
        private static void PrivacyLogDetourBody()
        {
            if (HeartopiaComplete.Instance != null && HeartopiaComplete.Instance.privacyBlockLogUploads)
            {
                Interlocked.Increment(ref privacyBlockedLogCount);
                return;
            }

            privacyLogTrampoline?.Invoke();
        }

        private static void PrivacyMergeDetourBody()
        {
            if (HeartopiaComplete.Instance != null && HeartopiaComplete.Instance.privacyBlockRoomMerges)
            {
                Interlocked.Increment(ref privacyBlockedMergeCount);
                return;
            }

            privacyMergeTrampoline?.Invoke();
        }

        private static void PrivacySpamDetourBody(IntPtr encodedShortId, int type)
        {
            if (HeartopiaComplete.Instance != null && HeartopiaComplete.Instance.privacyBlockSpamReports)
            {
                Interlocked.Increment(ref privacyBlockedSpamCount);
                return;
            }

            privacySpamTrampoline?.Invoke(encodedShortId, type);
        }

        private static void PrivacyUploadCheatDetourBody(IntPtr self, IntPtr objectId, IntPtr stream, IntPtr success, IntPtr failure)
        {
            Interlocked.Increment(ref privacyUploadCheatSeenCount);
            if (HeartopiaComplete.Instance != null && HeartopiaComplete.Instance.privacyBlockUploadCheat)
            {
                Interlocked.Increment(ref privacyBlockedUploadCheatCount);
                return;
            }

            privacyUploadCheatTrampoline?.Invoke(self, objectId, stream, success, failure);
        }

        // Returns the same int SendCommand hands back; the only caller
        // (FriendNoticeSystem.HandleTransferNotice) discards it, so 0 is safe for the blocked path.
        private static int PrivacyFriendVisitDetourBody(IntPtr command, int needAuthed, int channelType)
        {
            if (HeartopiaComplete.Instance != null && HeartopiaComplete.Instance.privacyBlockFriendVisitNotify)
            {
                Interlocked.Increment(ref privacyBlockedFriendVisitCount);
                return 0;
            }

            return privacyFriendVisitTrampoline != null
                ? privacyFriendVisitTrampoline.Invoke(command, needAuthed, channelType)
                : 0;
        }

        internal string GetPrivacyBlockHooksStatus()
        {
            if (this.privacyBlockHooksHardFailed)
            {
                return "failed";
            }

            string log = privacyLogTrampoline != null ? "ok" : (this.privacyLogHookTried ? "missing" : "pending");
            string merge = privacyMergeTrampoline != null ? "ok" : (this.privacyMergeHookTried ? "missing" : "pending");
            string spam = privacySpamTrampoline != null ? "ok" : (this.privacySpamHookTried ? "missing" : "pending");
            string cheat = privacyUploadCheatTrampoline != null ? "ok" : (this.privacyUploadCheatHookTried ? "missing" : "pending");
            string visit = privacyFriendVisitTrampoline != null ? "ok" : (this.privacyFriendVisitHookTried ? "missing" : "pending");
            return "Log=" + log + " Merge=" + merge + " Report=" + spam + " UploadCheat=" + cheat + " Visit=" + visit;
        }

    }
}
