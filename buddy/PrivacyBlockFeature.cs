using System;
using System.Runtime.InteropServices;
using System.Threading;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HeartopiaMod
{
    // AuraMono NativeDetour hooks for Logan log upload, town merge enter, antispam report.
    // UploadCheat is log-only (always forwards via trampoline).
    public partial class HeartopiaComplete
    {
        public bool privacyBlockLogUploads;
        public bool privacyBlockRoomMerges;
        public bool privacyBlockSpamReports;

        internal static int privacyBlockedLogCount;
        internal static int privacyBlockedMergeCount;
        internal static int privacyBlockedSpamCount;
        internal static int privacyUploadCheatSeenCount;

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

        private bool privacyLogHookTried;
        private bool privacyMergeHookTried;
        private bool privacySpamHookTried;
        private bool privacyUploadCheatHookTried;
        private bool privacyBlockHooksHardFailed;

        private static NativeDetour privacyLogDetour;
        private static NativeDetour privacyMergeDetour;
        private static NativeDetour privacySpamDetour;
        private static NativeDetour privacyUploadCheatDetour;

        private static TryUploadLogHookDelegate privacyLogHookKeepAlive;
        private static EnterRoomMergeHookDelegate privacyMergeHookKeepAlive;
        private static ReportHookDelegate privacySpamHookKeepAlive;
        private static UploadCheatHookDelegate privacyUploadCheatHookKeepAlive;

        private static TryUploadLogHookDelegate privacyLogTrampoline;
        private static EnterRoomMergeHookDelegate privacyMergeTrampoline;
        private static ReportHookDelegate privacySpamTrampoline;
        private static UploadCheatHookDelegate privacyUploadCheatTrampoline;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void TryUploadLogHookDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EnterRoomMergeHookDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ReportHookDelegate(IntPtr encodedShortId, int type);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void UploadCheatHookDelegate(IntPtr self, IntPtr objectId, IntPtr stream, IntPtr success, IntPtr failure);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr PrivacyBlockCompileMethodDelegate(IntPtr method);

        private void ProcessPrivacyBlockOnUpdate()
        {
            if (this.privacyBlockHooksHardFailed)
            {
                return;
            }

            if (this.PrivacyBlockAllHooksSettled())
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

        private bool PrivacyBlockHookSettled(bool tried, Delegate trampoline) => trampoline != null || tried;

        private bool PrivacyBlockAllHooksSettled()
        {
            return this.PrivacyBlockHookSettled(this.privacyLogHookTried, privacyLogTrampoline)
                && this.PrivacyBlockHookSettled(this.privacyMergeHookTried, privacyMergeTrampoline)
                && this.PrivacyBlockHookSettled(this.privacySpamHookTried, privacySpamTrampoline)
                && this.PrivacyBlockHookSettled(this.privacyUploadCheatHookTried, privacyUploadCheatTrampoline);
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

                if (!this.privacyLogHookTried)
                {
                    this.TryInstallPrivacyLogHook(compile);
                }

                if (!this.privacyMergeHookTried)
                {
                    this.TryInstallPrivacyMergeHook(compile);
                }

                if (!this.privacySpamHookTried)
                {
                    this.TryInstallPrivacySpamHook(compile);
                }

                if (!this.privacyUploadCheatHookTried)
                {
                    this.TryInstallPrivacyUploadCheatHook(compile);
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

            ModLogger.Msg("[PrivacyBlock] hooked UploadSystem.UploadCheat (log-only) @0x" + nativePtr.ToInt64().ToString("X"));
        }

        private void MarkPrivacyHookMissing(ref bool triedFlag, string label)
        {
            triedFlag = true;
            ModLogger.Msg("[PrivacyBlock] " + label + " not found — hook skipped");
        }

        private static void PrivacyLogDetourBody()
        {
            bool block = HeartopiaComplete.Instance != null && HeartopiaComplete.Instance.privacyBlockLogUploads;
            ModLogger.Msg("[PrivacyBlock] LoganUploadService.TryUploadLog block=" + block);
            if (block)
            {
                Interlocked.Increment(ref privacyBlockedLogCount);
                return;
            }

            privacyLogTrampoline?.Invoke();
        }

        private static void PrivacyMergeDetourBody()
        {
            bool block = HeartopiaComplete.Instance != null && HeartopiaComplete.Instance.privacyBlockRoomMerges;
            ModLogger.Msg("[PrivacyBlock] TownMergeProtocolManager.EnterRoomMerge block=" + block);
            if (block)
            {
                Interlocked.Increment(ref privacyBlockedMergeCount);
                return;
            }

            privacyMergeTrampoline?.Invoke();
        }

        private static void PrivacySpamDetourBody(IntPtr encodedShortId, int type)
        {
            string shortId = PrivacyReadMonoString(encodedShortId);
            bool block = HeartopiaComplete.Instance != null && HeartopiaComplete.Instance.privacyBlockSpamReports;
            ModLogger.Msg("[PrivacyBlock] ReportProtocolManager.Report shortId=" + shortId + " type=" + type + " block=" + block);
            if (block)
            {
                Interlocked.Increment(ref privacyBlockedSpamCount);
                return;
            }

            privacySpamTrampoline?.Invoke(encodedShortId, type);
        }

        private static void PrivacyUploadCheatDetourBody(IntPtr self, IntPtr objectId, IntPtr stream, IntPtr success, IntPtr failure)
        {
            string obsId = PrivacyReadMonoString(objectId);
            int bytes = PrivacyReadMonoArrayLength(stream);
            Interlocked.Increment(ref privacyUploadCheatSeenCount);
            ModLogger.Msg("[PrivacyBlock] UploadSystem.UploadCheat objectId=" + obsId + " bytes=" + bytes);
            privacyUploadCheatTrampoline?.Invoke(self, objectId, stream, success, failure);
        }

        private static string PrivacyReadMonoString(IntPtr strObj)
        {
            HeartopiaComplete host = HeartopiaComplete.Instance;
            if (host == null || strObj == IntPtr.Zero)
            {
                return string.Empty;
            }

            return host.TryReadMonoString(strObj, out string value) ? value : string.Empty;
        }

        private static int PrivacyReadMonoArrayLength(IntPtr arrayObj)
        {
            HeartopiaComplete host = HeartopiaComplete.Instance;
            if (host == null || arrayObj == IntPtr.Zero)
            {
                return -1;
            }

            return host.PrivacyTryGetMonoArrayLength(arrayObj);
        }

        private int PrivacyTryGetMonoArrayLength(IntPtr arrayObj)
        {
            if (arrayObj == IntPtr.Zero || auraMonoArrayLength == null || !this.IsAuraMonoArrayObject(arrayObj))
            {
                return -1;
            }

            try
            {
                return (int)Math.Min(auraMonoArrayLength(arrayObj).ToUInt64(), int.MaxValue);
            }
            catch
            {
                return -1;
            }
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
            return "Log=" + log + " Merge=" + merge + " Report=" + spam + " UploadCheat=" + cheat;
        }

        private float DrawPrivacyBlockExtraTab(float startY, float left)
        {
            const float width = 460f;
            float y = startY + 10f;

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 1f);
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            labelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.9f);

            GUI.Label(new Rect(left, y, width, 22f), "Privacy", headerStyle);
            y += 28f;

            bool prevBlockLogs = this.privacyBlockLogUploads;
            float logsToggleHeight = this.GetSwitchToggleHeight(360f, "Block Server Log Uploads", 25f);
            this.privacyBlockLogUploads = this.DrawWrappedSwitchToggle(
                new Rect(left, y, 360f, logsToggleHeight),
                this.privacyBlockLogUploads,
                "Block Server Log Uploads",
                25f);
            if (this.privacyBlockLogUploads != prevBlockLogs)
            {
                try { this.SaveKeybinds(false); } catch { }
            }

            y += logsToggleHeight + 4f;
            GUI.Label(new Rect(left, y, width, 18f), this.LF("Logs blocked: {0}", privacyBlockedLogCount), labelStyle);
            y += 22f;

            bool prevBlockMerges = this.privacyBlockRoomMerges;
            float mergesToggleHeight = this.GetSwitchToggleHeight(360f, "Block Room Merge (Enter)", 25f);
            this.privacyBlockRoomMerges = this.DrawWrappedSwitchToggle(
                new Rect(left, y, 360f, mergesToggleHeight),
                this.privacyBlockRoomMerges,
                "Block Room Merge (Enter)",
                25f);
            if (this.privacyBlockRoomMerges != prevBlockMerges)
            {
                try { this.SaveKeybinds(false); } catch { }
            }

            y += mergesToggleHeight + 4f;
            GUI.Label(new Rect(left, y, width, 18f), this.LF("Merges blocked: {0}", privacyBlockedMergeCount), labelStyle);
            y += 22f;

            bool prevBlockSpams = this.privacyBlockSpamReports;
            float spamsToggleHeight = this.GetSwitchToggleHeight(360f, "Block Spam Reports", 25f);
            this.privacyBlockSpamReports = this.DrawWrappedSwitchToggle(
                new Rect(left, y, 360f, spamsToggleHeight),
                this.privacyBlockSpamReports,
                "Block Spam Reports",
                25f);
            if (this.privacyBlockSpamReports != prevBlockSpams)
            {
                try { this.SaveKeybinds(false); } catch { }
            }

            y += spamsToggleHeight + 4f;
            GUI.Label(new Rect(left, y, width, 18f), this.LF("Spams blocked: {0} | UploadCheat seen: {1}", privacyBlockedSpamCount, privacyUploadCheatSeenCount), labelStyle);
            y += 20f;
            GUI.Label(new Rect(left, y, width, 18f), this.GetPrivacyBlockHooksStatus(), labelStyle);
            y += 22f;

            return y;
        }
    }
}
