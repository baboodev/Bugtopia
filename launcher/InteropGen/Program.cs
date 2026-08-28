using System;
using System.IO;
using Bugtopia.Interop;

namespace Bugtopia.Interop.Cli
{
    internal static class Program
    {
        private const int ExitOk = 0;
        private const int ExitStale = 1;
        private const int ExitError = 2;

        private static int Main(string[] args)
        {
            string game = null;
            string bepinex = null;
            string runtime = null;
            string unhollowed = null;
            bool check = false;
            bool force = false;
            bool quiet = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--game":
                        game = Next(args, ref i, "--game");
                        break;
                    case "--bepinex":
                        bepinex = Next(args, ref i, "--bepinex");
                        break;
                    case "--runtime":
                        runtime = Next(args, ref i, "--runtime");
                        break;
                    // Consumed here only so it is not rejected as unknown: BepInEx reads it straight
                    // off Environment.GetCommandLineArgs(), which is this process's own command line.
                    // Redirects the whole interop base path (interop\ and unity-libs\ both).
                    case "--unhollowed-path":
                        unhollowed = Next(args, ref i, "--unhollowed-path");
                        break;
                    case "--check":
                        check = true;
                        break;
                    case "--force":
                        force = true;
                        break;
                    case "-q":
                    case "--quiet":
                        quiet = true;
                        break;
                    case "-h":
                    case "--help":
                        Usage();
                        return ExitOk;
                    default:
                        Console.Error.WriteLine("Unknown argument: " + arg);
                        Usage();
                        return ExitError;
                }

                if (game == "\0" || bepinex == "\0")
                    return ExitError;
            }

            if (game == null || bepinex == null)
            {
                Usage();
                return ExitError;
            }

            Action<string> log = quiet ? (Action<string>)(delegate { }) : Console.Error.WriteLine;

            try
            {
                InteropPaths paths = InteropPaths.Resolve(game, bepinex, runtime);
                log("Game:    " + paths.GameExe);
                log("BepInEx: " + paths.BepInExRoot);
                if (unhollowed != null)
                    log("Interop base path overridden: " + unhollowed);

                using var host = new InteropHost(paths, log);

                if (check)
                {
                    // --check is the pipeline entry point: stdout carries the verdict and nothing else.
                    string stored = host.ReadStoredHash();
                    string current = host.ComputeHash();
                    bool upToDate = stored != null &&
                                    string.Equals(stored, current, StringComparison.OrdinalIgnoreCase);

                    log("Stored:  " + (stored ?? "<none>"));
                    log("Current: " + current);
                    Console.WriteLine(upToDate ? "up-to-date" : (stored == null ? "missing" : "stale"));
                    return upToDate ? ExitOk : ExitStale;
                }

                if (!force && host.IsUpToDate())
                {
                    log("Interop is already up to date; nothing to do (use --force to rebuild anyway).");
                    Console.WriteLine("up-to-date");
                    return ExitOk;
                }

                host.Generate(force);
                Console.WriteLine("generated");
                return ExitOk;
            }
            catch (InteropSetupException ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return ExitError;
            }
            catch (Exception ex)
            {
                // Reflection failures arrive wrapped; the inner one is the informative half.
                Exception reported = ex is System.Reflection.TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException
                    : ex;
                Console.Error.WriteLine("error: " + reported);
                return ExitError;
            }
        }

        private static string Next(string[] args, ref int i, string name)
        {
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine(name + " needs a value.");
                return "\0";
            }
            return args[++i];
        }

        private static void Usage()
        {
            Console.Error.WriteLine(
@"InteropGen — regenerate BepInEx Il2Cpp interop assemblies without launching the game.

  InteropGen --game <folder> --bepinex <folder> [--check] [--force] [--quiet]

  --game <folder>     Game install folder (the one holding <name>.exe and <name>_Data).
  --bepinex <folder>  BepInEx root, or a folder containing one.
  --runtime <folder>  BepInEx's 'dotnet' folder. Defaults to the one next to the BepInEx root.
  --unhollowed-path <folder>
                      Override the interop base path (holds interop\ and unity-libs\).
                      Handed to BepInEx through this process's command line.
  --check             Report only; do not generate.
  --force             Rebuild even when the interop set is already current.
  --quiet             Suppress progress on stderr.

stdout is one word: up-to-date | stale | missing | generated. Progress goes to stderr.
Exit codes: 0 = ok / up to date, 1 = regeneration needed (--check only), 2 = error.

Note: initialising BepInEx paths makes BepInEx rewrite <bepinex>\config\BepInEx.cfg
(values survive, entry comments do not). The original is snapshotted and put back on exit.");
        }
    }
}
