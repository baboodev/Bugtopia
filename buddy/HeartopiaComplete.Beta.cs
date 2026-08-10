using System;
using System.IO;

namespace HeartopiaMod
{
    // ============================================================================================
    // BETA GATE — a mod-wide "show the experimental stuff" switch, unlocked by dropping a marker
    // file into the mod's own folder:
    //
    //     %LocalLow%/Bugtopia/beta
    //
    // Read ONCE at startup (OnInitializeMelon, right after the config load) and never again — the
    // flag is a static that anything may read at any time without a null check or a world gate.
    // Creating or deleting the marker therefore takes effect on the NEXT game start, which is the
    // whole point: a session cannot have a feature appear or vanish under it half-way through, and
    // no per-frame or timer code exists to keep it fresh.
    //
    // Consumers so far:
    //   * the Music sidebar tab (HeartopiaComplete.UguiShell.cs, IsUguiShellTabUnlocked) — its nav
    //     row is built but left inactive when the flag is off.
    //
    // How to gate a new experimental surface: read HeartopiaComplete.BetaEnabled. For a UGUI shell
    // tab, add its display index to IsUguiShellTabUnlocked instead of hiding it locally — that one
    // function also owns the sidebar row re-flow, so a locked tab leaves no gap.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // Marker file base name. The EXTENSION IS IGNORED on purpose: Windows Explorer hides known
        // extensions, so a user told to "create a file called beta" typically ends up with
        // beta.txt and would otherwise be left staring at an unchanged menu. "beta" (no
        // extension), "beta.txt" and "beta.whatever" all count; "betamax.bin" does not.
        private const string BetaMarkerBaseName = "beta";

        // Set once by RefreshBetaFlag from OnInitializeMelon. Static so feature code can read it
        // from anywhere — including static helpers with no instance in hand.
        internal static bool BetaEnabled;

        // One-shot startup read. Fail-closed: any IO problem leaves the flag false, i.e. the
        // experimental surfaces stay hidden, which is the safe direction.
        private static void RefreshBetaFlag()
        {
            bool found = false;
            string root = null;
            try
            {
                root = HelperPaths.Root;
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    // "beta*" narrows the enumeration; the name-without-extension compare is what
                    // actually decides, so siblings like "betamax.bin" are not mistaken for it.
                    foreach (string path in Directory.EnumerateFiles(root, BetaMarkerBaseName + "*"))
                    {
                        if (string.Equals(Path.GetFileNameWithoutExtension(path), BetaMarkerBaseName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Beta] marker check failed (staying disabled): " + ex.Message);
                found = false;
            }

            BetaEnabled = found;
            if (found)
            {
                ModLogger.Msg("[Beta] marker found in " + root + " — experimental features ENABLED.");
            }
        }
    }
}
