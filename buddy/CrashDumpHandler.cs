using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace HeartopiaMod
{
    // Writes a full process minidump when the game dies from an UNHANDLED native fault, so a native
    // access violation (the recurring AuraMono crash class) can be analysed with the
    // tools/Read-Minidump* scripts even though nothing usable lands in LogOutput.log.
    //
    // Mechanism: a single chained SetUnhandledExceptionFilter. The OS calls it only at last chance,
    // when nothing else handled the exception and the process is already terminating, so writing a
    // dump and calling the engine's previous filter is safe. We then hand off to that previous filter
    // so the game's own save-on-crash (XD_OPT_SAVE_DATA_ON_CRASH) still runs.
    //
    // WHAT THIS DELIBERATELY DOES NOT DO: register an AddVectoredExceptionHandler. A VEH runs on
    // EVERY first-chance exception, and CoreCLR raises SEH exceptions (0xE0434352) as its normal
    // managed-exception dispatch. A *managed* VEH callback re-enters the CLR while it is mid-dispatch
    // and corrupts it -> ExecutionEngineException (0x80131506) during startup/assembly load. A
    // managed callback is only safe at the last-chance filter, never first-chance.
    internal static class CrashDumpHandler
    {
        private const int EXCEPTION_CONTINUE_SEARCH = 0;

        // MINIDUMP_TYPE: full memory + the metadata dotnet-dump/clrstack needs.
        private const int DumpTypeFull =
            0x00000002  // WithFullMemory
            | 0x00000004 // WithHandleData
            | 0x00000800 // WithFullMemoryInfo
            | 0x00001000 // WithThreadInfo
            | 0x00000100 // WithProcessThreadData
            | 0x00000020; // WithUnloadedModules

        private const int MaxRetainedDumps = 3;

        private static int _installed;
        private static int _dumpWritten;
        private static string _dumpDir;

        // Keep the marshalled delegate alive for the process lifetime: if the GC collects it the
        // native callback pointer dangles and the handler itself becomes the crash.
        private static TopLevelExceptionFilter _uefDelegate;
        private static IntPtr _previousFilter;

        public static void Install()
        {
            if (Interlocked.Exchange(ref _installed, 1) != 0)
            {
                return;
            }

            try
            {
                _dumpDir = HelperPaths.GetDirectory("CrashDumps");
                PruneOldDumps();

                _uefDelegate = OnUnhandledException;
                _previousFilter = SetUnhandledExceptionFilter(_uefDelegate);

                ModLogger.Msg("[CrashDump] Native crash handler installed; dumps -> " + _dumpDir);
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[CrashDump] Install failed: " + ex.Message);
            }
        }

        // Last-chance filter: a fault reaches here only if nothing handled it. Dump, then hand off to
        // the engine's previous filter (so save-on-crash still runs); if there was none, fall through
        // to default OS handling (WER).
        private static int OnUnhandledException(IntPtr exceptionInfo)
        {
            try
            {
                uint code = 0u;
                if (exceptionInfo != IntPtr.Zero)
                {
                    IntPtr recordPtr = Marshal.ReadIntPtr(exceptionInfo); // EXCEPTION_POINTERS.ExceptionRecord
                    if (recordPtr != IntPtr.Zero)
                    {
                        code = unchecked((uint)Marshal.ReadInt32(recordPtr)); // ExceptionRecord.ExceptionCode (first field)
                    }
                }

                TryWriteDump(exceptionInfo, code);
            }
            catch
            {
            }

            if (_previousFilter != IntPtr.Zero)
            {
                try
                {
                    TopLevelExceptionFilter previous =
                        Marshal.GetDelegateForFunctionPointer<TopLevelExceptionFilter>(_previousFilter);
                    return previous(exceptionInfo);
                }
                catch
                {
                }
            }

            return EXCEPTION_CONTINUE_SEARCH;
        }

        private static void TryWriteDump(IntPtr exceptionInfo, uint code)
        {
            // One dump per process. Set the flag BEFORE writing so a fault inside MiniDumpWriteDump
            // (possible under heap corruption) cannot recurse into a second attempt.
            if (Interlocked.Exchange(ref _dumpWritten, 1) != 0)
            {
                return;
            }

            string path = Path.Combine(
                _dumpDir ?? AppDomain.CurrentDomain.BaseDirectory,
                "xdt_crash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + code.ToString("X8") + ".dmp");

            IntPtr hFile = CreateFile(path, 0x40000000u /* GENERIC_WRITE */, 0u, IntPtr.Zero,
                2u /* CREATE_ALWAYS */, 0x80u /* FILE_ATTRIBUTE_NORMAL */, IntPtr.Zero);
            if (hFile == IntPtr.Zero || hFile == new IntPtr(-1))
            {
                ModLogger.Warning("[CrashDump] CreateFile failed for " + path + " (err " + Marshal.GetLastWin32Error() + ")");
                return;
            }

            try
            {
                MINIDUMP_EXCEPTION_INFORMATION info = new MINIDUMP_EXCEPTION_INFORMATION
                {
                    ThreadId = GetCurrentThreadId(),
                    ExceptionPointers = exceptionInfo,
                    ClientPointers = 0,
                };

                bool ok = MiniDumpWriteDump(GetCurrentProcess(), GetCurrentProcessId(), hFile, DumpTypeFull,
                    ref info, IntPtr.Zero, IntPtr.Zero);
                if (ok)
                {
                    ModLogger.Warning("[CrashDump] caught fatal 0x" + code.ToString("X8") + " -> dump written: " + path);
                }
                else
                {
                    ModLogger.Warning("[CrashDump] MiniDumpWriteDump failed (err " + Marshal.GetLastWin32Error() + ") for " + path);
                }
            }
            finally
            {
                CloseHandle(hFile);
            }
        }

        // Keep only the newest few dumps; full-memory dumps are large. Best-effort, install-time only.
        private static void PruneOldDumps()
        {
            try
            {
                string[] files = Directory.GetFiles(_dumpDir, "xdt_crash_*.dmp");
                if (files.Length < MaxRetainedDumps)
                {
                    return;
                }

                Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                for (int i = MaxRetainedDumps - 1; i < files.Length; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }
            }
            catch
            {
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int TopLevelExceptionFilter(IntPtr exceptionInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct MINIDUMP_EXCEPTION_INFORMATION
        {
            public uint ThreadId;
            public IntPtr ExceptionPointers;
            public int ClientPointers;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr SetUnhandledExceptionFilter(TopLevelExceptionFilter filter);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, IntPtr hFile, int dumpType,
            ref MINIDUMP_EXCEPTION_INFORMATION exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
    }
}
