using System;
using Bugtopia.Launch;

namespace Bugtopia.Launcher
{
    internal static class Program
    {
        internal const string VerbInterop = "interop";

        /// <summary>
        /// No arguments: the launcher window. With the verb below: host BepInEx's CoreCLR, run the
        /// interop generator, exit.
        ///
        /// That split is forced twice over. <c>coreclr_initialize</c> can only be called once per
        /// process, and the generator caches its paths in statics — so a window that must be able to
        /// do this again, possibly against a different storage folder, cannot do it in its own
        /// process. Re-executing ourselves keeps it to one file.
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == VerbInterop)
                return RunInterop(args);

            return PhotinoHost.Run();
        }

        private static int RunInterop(string[] args)
        {
            string game = null, storage = null;
            int mode = CoreClrHost.ModeCheck;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--game" when i + 1 < args.Length:
                        game = args[++i];
                        break;
                    case "--storage" when i + 1 < args.Length:
                        storage = args[++i];
                        break;
                    case "--generate":
                        mode = CoreClrHost.ModeGenerate;
                        break;
                    case "--force":
                        mode = CoreClrHost.ModeGenerateForce;
                        break;
                }
            }

            if (game == null || storage == null)
            {
                Console.Error.WriteLine("usage: Bugtopia interop --game <dir> --storage <dir> [--generate|--force]");
                return CoreClrHost.ResultError;
            }

            try
            {
                return CoreClrHost.Run(game, new StorageLayout(storage), mode);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return CoreClrHost.ResultError;
            }
        }
    }
}
