using System;
using System.Collections.Generic;

namespace HeartopiaMod
{
    // ============================================================================================
    // Spelling-tolerant, exhaustive AuraMono class resolution.
    //
    // `FindAuraMonoClassByFullName` (HeartopiaComplete.AuraMono.cs) is HINT-DRIVEN: it maps a
    // namespace prefix to a short list of likely images. That assumes a namespace predicts its
    // image, which is false for real types in this build — everything under
    // `XDTLevelAndEntity.Core.World.*` compiles into the `XDTDataAndProtocol` image — so it has
    // measured blind spots. That is exactly why so many features hand-roll their own
    // across-assemblies fallback after calling it.
    //
    // This is that fallback written once: try the spelling variants AGENTS.md §7 tells a human to
    // enumerate by hand, then ask EVERY loaded image directly via `mono_assembly_foreach`.
    //
    // It lived inside `#if FEATURE_MCP` (McpOps.Mono.cs) while `mono.find` was its only caller.
    // It is out here now because `HeartopiaComplete.TryAuraSendCommand` resolves command types
    // through it, and that helper has to exist in normal builds.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // The spelling variants AGENTS.md §7 tells a human to write out by hand. Doing it here is the
        // entire point: the caller states the type once and learns which form the build actually uses.
        private static List<string> BuildAuraMonoNameCandidates(string name)
        {
            List<string> candidates = new List<string>(6) { name };

            void Add(string candidate)
            {
                if (!string.IsNullOrEmpty(candidate) && !candidates.Contains(candidate))
                {
                    candidates.Add(candidate);
                }
            }

            if (name.Contains(".Gameplay."))
            {
                Add(name.Replace(".Gameplay.", ".GamePlay."));
            }
            else if (name.Contains(".GamePlay."))
            {
                Add(name.Replace(".GamePlay.", ".Gameplay."));
            }

            if (name.StartsWith("Il2Cpp", StringComparison.Ordinal))
            {
                Add(name.Substring(6));
            }
            else
            {
                Add("Il2Cpp" + name);
            }

            if (name.StartsWith("ScriptsRefactory.", StringComparison.Ordinal))
            {
                Add(name.Substring("ScriptsRefactory.".Length));
            }
            else
            {
                Add("ScriptsRefactory." + name);
            }

            return candidates;
        }

        private bool TryResolveAuraMonoClassAnySpelling(string name, out IntPtr klass, out string resolvedName)
        {
            klass = IntPtr.Zero;
            resolvedName = null;

            List<string> candidates = BuildAuraMonoNameCandidates(name);
            for (int i = 0; i < candidates.Count; i++)
            {
                IntPtr found = this.FindAuraMonoClassByFullName(candidates[i]);
                if (found != IntPtr.Zero)
                {
                    klass = found;
                    resolvedName = candidates[i];
                    return true;
                }
            }

            // Exhaustive sweep, because the shared resolver is HINT-DRIVEN: it maps a namespace
            // prefix to a short list of likely images and its across-assemblies fallback goes through
            // managed reflection on Il2CppMonoGame.MonoHost. Measured blind spot: every
            // XDTLevelAndEntity.Core.World.* type — including ViewComponent, the live base class of
            // components this very session enumerated 16 instances of. A research tool that answers
            // "no such type" when the type is demonstrably loaded is worse than no tool, so this asks
            // every loaded image directly.
            for (int i = 0; i < candidates.Count; i++)
            {
                IntPtr found = this.FindAuraMonoClassInAnyImage(candidates[i]);
                if (found != IntPtr.Zero)
                {
                    klass = found;
                    resolvedName = candidates[i];
                    return true;
                }
            }

            return false;
        }

        // Collected by the native callback below; only ever touched inside the synchronous sweep.
        private static readonly List<IntPtr> AuraMonoSweepImages = new List<IntPtr>();
        private static MonoAssemblyForeachCallbackDelegate auraMonoSweepCallbackKeepAlive;

        private IntPtr FindAuraMonoClassInAnyImage(string fullTypeName)
        {
            if (auraMonoAssemblyForeach == null || auraMonoAssemblyGetImage == null
                || auraMonoClassFromName == null || string.IsNullOrWhiteSpace(fullTypeName))
            {
                return IntPtr.Zero;
            }

            int lastDot = fullTypeName.LastIndexOf('.');
            if (lastDot <= 0)
            {
                return IntPtr.Zero;
            }

            string ns = fullTypeName.Substring(0, lastDot);
            string cn = fullTypeName.Substring(lastDot + 1);

            try
            {
                AuraMonoSweepImages.Clear();
                if (auraMonoSweepCallbackKeepAlive == null)
                {
                    // Held in a static: a delegate handed to native code and then collected is a
                    // callback into freed memory.
                    auraMonoSweepCallbackKeepAlive = AuraMonoSweepCollect;
                }

                auraMonoAssemblyForeach(auraMonoSweepCallbackKeepAlive, IntPtr.Zero);

                for (int i = 0; i < AuraMonoSweepImages.Count; i++)
                {
                    IntPtr image = AuraMonoSweepImages[i];
                    if (image == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr found = auraMonoClassFromName(image, ns, cn);
                    if (found != IntPtr.Zero)
                    {
                        return found;
                    }
                }
            }
            catch
            {
            }

            return IntPtr.Zero;
        }

        private static void AuraMonoSweepCollect(IntPtr assembly, IntPtr userData)
        {
            // Returns into native code — nothing may escape.
            try
            {
                if (assembly == IntPtr.Zero || auraMonoAssemblyGetImage == null)
                {
                    return;
                }

                IntPtr image = auraMonoAssemblyGetImage(assembly);
                if (image != IntPtr.Zero)
                {
                    AuraMonoSweepImages.Add(image);
                }
            }
            catch
            {
            }
        }

        internal IntPtr FindAuraMonoClassAnySpelling(string fullName)
        {
            return this.TryResolveAuraMonoClassAnySpelling(fullName, out IntPtr klass, out _)
                ? klass
                : IntPtr.Zero;
        }
    }
}
