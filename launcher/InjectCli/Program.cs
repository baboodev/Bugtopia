using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Bugtopia.Launch;

namespace Bugtopia.Inject.Cli
{
    /// <summary>
    /// Injects the bootstrap into a game that was started by hand, then shows what the bootstrap made
    /// of it. The injection itself lives in <see cref="Injector"/>, shared with the GUI launcher; what
    /// is here is target selection and reading the log back, which is the whole value of this tool
    /// during bring-up.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string processName = "xdt";
            string dllPath = null;
            int pid = 0;
            int logTail = 40;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--process":
                        processName = ValueOf(args, ref i);
                        break;
                    case "--pid":
                        if (!int.TryParse(ValueOf(args, ref i), out pid))
                        {
                            Console.Error.WriteLine("--pid needs a number.");
                            return 2;
                        }
                        break;
                    case "--dll":
                        dllPath = ValueOf(args, ref i);
                        break;
                    case "--log-tail":
                        int.TryParse(ValueOf(args, ref i), out logTail);
                        break;
                    case "-h":
                    case "--help":
                        Usage();
                        return 0;
                    default:
                        Console.Error.WriteLine("Unknown argument: " + args[i]);
                        Usage();
                        return 2;
                }
            }

            if (string.IsNullOrWhiteSpace(dllPath))
            {
                Usage();
                return 2;
            }

            dllPath = Path.GetFullPath(dllPath);
            if (!File.Exists(dllPath))
            {
                Console.Error.WriteLine("No such DLL: " + dllPath);
                return 2;
            }

            Process target;
            try
            {
                target = FindTarget(pid, processName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }

            Console.WriteLine($"Target: {target.ProcessName} (pid {target.Id})");
            Console.WriteLine("DLL:    " + dllPath);

            if (Injector.IsModuleLoaded(target, dllPath))
            {
                Console.Error.WriteLine(
                    "That module is already loaded in the target. Injecting twice would run the " +
                    "bootstrap again against a process that already hosts a CLR; restart the game first.");
                return 2;
            }

            // The log is written next to the DLL and is the only account of what happened after
            // DllMain returns, so note where it was and how big, to tail only the new part.
            string logPath = Path.Combine(Path.GetDirectoryName(dllPath), "bugtopia_inject.log");
            long logOffset = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;

            try
            {
                Injector.Inject(target, dllPath);
            }
            catch (InjectionException ex)
            {
                Console.Error.WriteLine("Injection failed: " + ex.Message);
                DumpLog(logPath, logOffset, logTail);
                return 1;
            }

            Console.WriteLine("Injected. Waiting for the bootstrap to report...");
            WaitForLogActivity(logPath, logOffset, TimeSpan.FromSeconds(45));
            DumpLog(logPath, logOffset, logTail);
            return 0;
        }

        private static Process FindTarget(int pid, string processName)
        {
            if (pid != 0)
            {
                try
                {
                    return Process.GetProcessById(pid);
                }
                catch (ArgumentException)
                {
                    throw new InvalidOperationException("No process with pid " + pid + ".");
                }
            }

            Process[] matches = Process.GetProcessesByName(processName);
            if (matches.Length == 0)
                throw new InvalidOperationException("No running process named '" + processName + "'.");
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    "Several processes named '" + processName + "' (" +
                    string.Join(", ", matches.Select(p => p.Id)) + "). Pass one with --pid.");
            }
            return matches[0];
        }

        private static void WaitForLogActivity(string logPath, long offset, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (File.Exists(logPath) && new FileInfo(logPath).Length > offset)
                    {
                        // Give the bootstrap a moment to finish writing rather than tailing a
                        // half-written account of it.
                        Thread.Sleep(1500);
                        return;
                    }
                }
                catch (IOException)
                {
                }
                Thread.Sleep(250);
            }
            Console.Error.WriteLine("The bootstrap wrote nothing within " + timeout.TotalSeconds + "s.");
        }

        private static void DumpLog(string logPath, long offset, int tail)
        {
            if (!File.Exists(logPath))
            {
                Console.Error.WriteLine("No log at " + logPath + " — DllMain did not get far enough to open it.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("---- " + logPath + " ----");
            try
            {
                using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                string[] lines = reader.ReadToEnd()
                                       .Split('\n')
                                       .Select(l => l.TrimEnd('\r'))
                                       .Where(l => l.Length > 0)
                                       .ToArray();
                foreach (string line in lines.Skip(Math.Max(0, lines.Length - tail)))
                    Console.WriteLine(line);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine("Could not read the log: " + ex.Message);
            }
        }

        private static string ValueOf(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
                throw new ArgumentException(args[i] + " needs a value.");
            return args[++i];
        }

        private static void Usage()
        {
            Console.Error.WriteLine(
@"InjectCli — load bugtopia_inject.dll into a running game.

  InjectCli --dll <path> [--process <name>] [--pid <n>] [--log-tail <n>]

  --dll <path>      The native bootstrap to inject.
  --process <name>  Process name without .exe (default: xdt).
  --pid <n>         Exact process, when the name is ambiguous.
  --log-tail <n>    Lines of bugtopia_inject.log to print afterwards (default 40).

The DLL takes its configuration from BUGTOPIA_STORAGE, or from bugtopia_inject.cfg
beside it — a process's environment cannot be set from outside, so a game started by
hand needs the file:

  storage=C:\path\to\folder\holding\BepInEx\and\dotnet

Exit codes: 0 = injected, 1 = injection failed, 2 = bad usage or no target.");
        }

    }
}
