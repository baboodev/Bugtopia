using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Bugtopia.Interop
{
    /// <summary>
    /// The entry point a native host calls after starting BepInEx's own CoreCLR.
    ///
    /// Why it exists: a NativeAOT launcher has no CLR of its own and cannot load this assembly. It
    /// can, however, host the CoreCLR that BepInEx already ships and create a delegate into here —
    /// the same trick <c>bugtopia_inject.dll</c> uses inside the game, pointed at the launcher
    /// instead. That is what lets a 2 MB launcher drive generation with no .NET on the machine.
    ///
    /// The signature is deliberately blittable: <c>coreclr_create_delegate</c> hands back a raw
    /// function pointer with no marshalling behind it, so strings cross as UTF-8 pointers and the
    /// result is an int.
    ///
    /// Everything it does is written to <c>&lt;logDirectory&gt;\interopgen.log</c>, because the caller
    /// is on the other side of a runtime boundary and cannot catch an exception from here.
    /// </summary>
    public static class InteropEntry
    {
        public const int ModeCheck = 0;
        public const int ModeGenerate = 1;
        public const int ModeGenerateForce = 2;

        // Deliberately distinct from 0/1 so a caller can tell "stale" from "something broke".
        public const int ResultUpToDate = 0;
        public const int ResultStale = 1;
        public const int ResultGenerated = 2;
        public const int ResultError = 3;

        /// <param name="gameUtf8">UTF-8 path to the game folder.</param>
        /// <param name="bepInExUtf8">UTF-8 path to the BepInEx root, or a folder containing one.</param>
        /// <param name="logDirUtf8">UTF-8 path to a writable folder for the run's log.</param>
        /// <param name="mode">One of the Mode constants.</param>
        public static int Run(IntPtr gameUtf8, IntPtr bepInExUtf8, IntPtr logDirUtf8, int mode)
        {
            string logPath = null;
            try
            {
                string game = Marshal.PtrToStringUTF8(gameUtf8);
                string bepInEx = Marshal.PtrToStringUTF8(bepInExUtf8);
                string logDir = Marshal.PtrToStringUTF8(logDirUtf8);

                if (!string.IsNullOrEmpty(logDir))
                {
                    Directory.CreateDirectory(logDir);
                    logPath = Path.Combine(logDir, "interopgen.log");
                    File.WriteAllText(logPath, "");
                }

                void Log(string message)
                {
                    if (logPath == null)
                        return;
                    try
                    {
                        File.AppendAllText(logPath, message + Environment.NewLine);
                    }
                    catch (IOException)
                    {
                    }
                }

                InteropPaths paths = InteropPaths.Resolve(game, bepInEx);
                using var host = new InteropHost(paths, Log);

                if (mode == ModeCheck)
                {
                    string stored = host.ReadStoredHash();
                    string current = host.ComputeHash();
                    Log("stored:  " + (stored ?? "<none>"));
                    Log("current: " + current);
                    bool upToDate = stored != null &&
                                    string.Equals(stored, current, StringComparison.OrdinalIgnoreCase);
                    Log(upToDate ? "up-to-date" : (stored == null ? "missing" : "stale"));
                    return upToDate ? ResultUpToDate : ResultStale;
                }

                host.Generate(mode == ModeGenerateForce);
                Log("generated");
                return ResultGenerated;
            }
            catch (Exception ex)
            {
                // The host is across a runtime boundary; an escaping exception would take the whole
                // process with it and tell nobody anything.
                Exception reported = ex is System.Reflection.TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException
                    : ex;
                try
                {
                    if (logPath != null)
                        File.AppendAllText(logPath, "ERROR: " + reported + Environment.NewLine);
                }
                catch (IOException)
                {
                }
                return ResultError;
            }
        }
    }
}
