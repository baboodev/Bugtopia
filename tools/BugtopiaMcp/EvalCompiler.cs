using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
// GetMetadataReader() is an extension on PEReader that lives in this namespace, not on the type.
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace BugtopiaMcp
{
    // ============================================================================================
    // `game_eval` — compile a C# snippet here, run it in the game as a one-shot sandbox plugin.
    //
    // The game side needed NO new code for this: phase 4 already provides load-into-a-collectible-
    // context, call, and unload. An eval is just a plugin with a lifetime of one call, which is why
    // this whole feature is a bridge-side composite over ops that already exist.
    //
    // Roslyn lives here rather than in the game on purpose: ~15 MB of compiler has no business
    // inside a process an anti-cheat watches, and iterating on the code generator costs a 3 s
    // rebuild here versus a game restart plus relogin there.
    //
    // References come from the RUNNING session (`env` op), never from guessed paths — the snippet is
    // compiled against the exact assemblies that session loaded, so a type that resolves at compile
    // time resolves at run time too.
    // ============================================================================================
    internal static class EvalCompiler
    {
        // Metadata reading is the expensive part (~400 assemblies), so the set is built once and
        // reused for every subsequent eval in this bridge process.
        private static List<MetadataReference> cachedReferences;
        private static string cachedFrom;

        internal sealed class Result
        {
            internal bool Success;
            internal byte[] Dll;
            internal byte[] Pdb;
            internal List<string> Diagnostics = new List<string>();
            internal string GeneratedSource;
            internal double CompileMs;
            internal int ReferenceCount;
        }

        // The snippet becomes the body of __Run, which returns object — so `return 42;`,
        // `return someList;` and `return null;` all work, and the wrapper stringifies whatever comes
        // back. `host` and `mod` are in scope because that is what an experiment always needs first.
        internal static string BuildSource(string snippet, string[] extraUsings)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Globalization;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine("using HeartopiaMod;");
            sb.AppendLine("using HeartopiaMod.Plugins;");
            sb.AppendLine("using UnityEngine;");
            if (extraUsings != null)
            {
                foreach (string u in extraUsings)
                {
                    if (!string.IsNullOrWhiteSpace(u))
                    {
                        sb.Append("using ").Append(u.Trim().TrimEnd(';')).AppendLine(";");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("public sealed class __Eval : IBugtopiaPlugin");
            sb.AppendLine("{");
            sb.AppendLine("    private IHostApi __host;");
            sb.AppendLine("    public void Load(IHostApi h) { this.__host = h; }");
            sb.AppendLine("    public void Tick() { }");
            sb.AppendLine("    public void Unload() { }");
            sb.AppendLine("    public string Call(string method, string argsJson)");
            sb.AppendLine("    {");
            sb.AppendLine("        object __r = __Run(this.__host, this.__host == null ? null : this.__host.Mod, argsJson);");
            sb.AppendLine("        return __r == null ? \"null\" : __r.ToString();");
            sb.AppendLine("    }");
            sb.AppendLine("    private object __Run(IHostApi host, HeartopiaComplete mod, string args)");
            sb.AppendLine("    {");
            sb.AppendLine("#line 1 \"snippet\"");
            sb.AppendLine(snippet);
            sb.AppendLine("#line default");
            // A snippet that ends in `return` makes this unreachable, which is the normal case — so
            // the warning would fire on essentially every eval and train the reader to ignore
            // diagnostics. Suppressed here rather than filtered later, so a CS0162 coming from the
            // author's OWN code still gets through.
            sb.AppendLine("#pragma warning disable 0162");
            sb.AppendLine("        return null;");
            sb.AppendLine("#pragma warning restore 0162");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        internal static Result Compile(string snippet, string[] extraUsings, EnvPaths paths)
        {
            Result result = new Result();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            result.GeneratedSource = BuildSource(snippet, extraUsings);

            List<MetadataReference> references = GetReferences(paths, result);
            result.ReferenceCount = references.Count;

            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                SourceText.From(result.GeneratedSource, Encoding.UTF8),
                new CSharpParseOptions(LanguageVersion.CSharp10));

            CSharpCompilation compilation = CSharpCompilation.Create(
                "__eval_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                new[] { tree },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    // The game runs x64 and the plugin host loads raw bytes; allowUnsafe keeps
                    // pointer-shaped experiments possible, which is most of what this mod does.
                    allowUnsafe: true,
                    platform: Platform.X64));

            using MemoryStream dll = new MemoryStream();
            using MemoryStream pdb = new MemoryStream();
            EmitResult emit = compilation.Emit(dll, pdb,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

            foreach (Diagnostic d in emit.Diagnostics)
            {
                if (d.Severity == DiagnosticSeverity.Error || d.Severity == DiagnosticSeverity.Warning)
                {
                    result.Diagnostics.Add(Describe(d));
                }
            }

            sw.Stop();
            result.CompileMs = sw.Elapsed.TotalMilliseconds;
            result.Success = emit.Success;
            if (emit.Success)
            {
                result.Dll = dll.ToArray();
                result.Pdb = pdb.ToArray();
            }

            return result;
        }

        // Line numbers are remapped to the SNIPPET by the #line directive, so an error points at the
        // line the author wrote rather than somewhere inside the generated wrapper.
        private static string Describe(Diagnostic d)
        {
            // MAPPED, not raw: #line directives are exactly what remaps a diagnostic onto the
            // snippet the author wrote, and GetLineSpan() ignores them — which reported every error
            // as "generated wrapper" and made the line numbers useless.
            FileLinePositionSpan span = d.Location.GetMappedLineSpan();
            string where = span.Path == "snippet"
                ? "line " + (span.StartLinePosition.Line + 1) + ", col " + (span.StartLinePosition.Character + 1)
                : "generated wrapper";
            return d.Severity.ToString().ToLowerInvariant() + " " + d.Id + " (" + where + "): " + d.GetMessage();
        }

        private static List<MetadataReference> GetReferences(EnvPaths paths, Result result)
        {
            string key = paths.Fingerprint;
            if (cachedReferences != null && string.Equals(cachedFrom, key, StringComparison.Ordinal))
            {
                return cachedReferences;
            }

            List<MetadataReference> refs = new List<MetadataReference>(512);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ── Reference order is load-bearing ──────────────────────────────────────────────────
            // The interop folder holds ~180 game assemblies and some of them redeclare types that
            // also exist in Unity's own modules — `UnityHelper.dll` declares its own
            // `UnityEngine.Object`, which makes the single most obvious snippet
            // (`UnityEngine.Object.FindObjectsOfType<T>()`) fail with CS0433. The mod's own csproj
            // sidesteps this by referencing ~20 hand-picked interop DLLs, but a snippet needs wide
            // reach, so instead: establish a PREFERRED set first, remember every type it exports,
            // and then drop any later assembly that collides with it.
            //
            // Dropping the whole assembly on a single collision is blunt, but it is predictable, and
            // the alternative (extern aliases) is not something a snippet author should have to
            // think about.
            HashSet<string> preferredTypes = new HashSet<string>(StringComparer.Ordinal);

            AddAssembly(refs, seen, paths.ModAssembly, preferredTypes, null);
            AddDirectory(refs, seen, paths.RuntimeDir, preferredTypes, null);
            foreach (string unityModule in PreferredUnityModules)
            {
                if (!string.IsNullOrEmpty(paths.InteropDir))
                {
                    AddAssembly(refs, seen, Path.Combine(paths.InteropDir, unityModule + ".dll"),
                        preferredTypes, null);
                }
            }

            // Everything else may join only if it does not shadow the preferred set.
            List<string> skipped = new List<string>();
            AddDirectory(refs, seen, paths.InteropDir, null, preferredTypes, skipped);
            AddDirectory(refs, seen, paths.CoreDir, null, preferredTypes, skipped);

            if (skipped.Count > 0)
            {
                Program.Log("eval: skipped " + skipped.Count + " assemblies that redeclare core types ("
                    + string.Join(", ", skipped.GetRange(0, Math.Min(6, skipped.Count))) + ")");
            }

            cachedReferences = refs;
            cachedFrom = key;
            return refs;
        }

        // The Unity surface a snippet actually reaches for, and the same modules buddy.csproj pins.
        private static readonly string[] PreferredUnityModules =
        {
            "UnityEngine", "UnityEngine.CoreModule", "UnityEngine.PhysicsModule",
            "UnityEngine.UI", "UnityEngine.UIModule", "UnityEngine.IMGUIModule",
            "UnityEngine.InputLegacyModule", "UnityEngine.AnimationModule",
            "UnityEngine.TextRenderingModule", "UnityEngine.ImageConversionModule",
            "UnityEngine.AIModule", "UnityEngine.AssetBundleModule",
            "UnityEngine.SharedInternalsModule", "Unity.TextMeshPro", "Unity.InputSystem",
            "Il2Cppmscorlib",
        };

        private static void AddDirectory(List<MetadataReference> refs, HashSet<string> seen, string dir,
                                         HashSet<string> collectTypesInto, HashSet<string> mustNotCollideWith,
                                         List<string> skipped = null)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(dir, "*.dll"))
            {
                AddAssembly(refs, seen, file, collectTypesInto, mustNotCollideWith, skipped);
            }
        }

        private static void AddAssembly(List<MetadataReference> refs, HashSet<string> seen, string path,
                                        HashSet<string> collectTypesInto, HashSet<string> mustNotCollideWith,
                                        List<string> skipped = null)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            string name = Path.GetFileName(path);
            if (!seen.Add(name))
            {
                // First one wins. The mod assembly is added before the directories precisely so a
                // stale copy sitting in a loader folder cannot shadow the one actually loaded.
                return;
            }

            // The metadata check must happen NOW, not at Emit. CreateFromFile is lazy: handing it a
            // native DLL succeeds here and then fails the whole compilation later with CS0009 — and
            // these folders are full of native DLLs (coreclr, clrjit, dobby, msquic…). A try/catch
            // around CreateFromFile catches nothing for exactly that reason.
            if (!TryReadTypeNames(path, out List<string> typeNames))
            {
                return;
            }

            if (mustNotCollideWith != null)
            {
                for (int i = 0; i < typeNames.Count; i++)
                {
                    if (mustNotCollideWith.Contains(typeNames[i]))
                    {
                        skipped?.Add(Path.GetFileNameWithoutExtension(path));
                        return;
                    }
                }
            }

            try
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
            catch
            {
                return;
            }

            if (collectTypesInto != null)
            {
                for (int i = 0; i < typeNames.Count; i++)
                {
                    collectTypesInto.Add(typeNames[i]);
                }
            }
        }

        // Doubles as the managed-metadata check. It must be EAGER: MetadataReference.CreateFromFile
        // is lazy, so handing it a native DLL succeeds here and then fails the entire compilation
        // later with CS0009 — and these folders are full of native DLLs (coreclr, clrjit, dobby,
        // msquic…). A try/catch around CreateFromFile catches nothing, for exactly that reason.
        private static bool TryReadTypeNames(string path, out List<string> typeNames)
        {
            typeNames = null;
            try
            {
                using FileStream fs = File.OpenRead(path);
                using PEReader pe = new PEReader(fs);
                if (!pe.HasMetadata)
                {
                    return false;
                }

                MetadataReader md = pe.GetMetadataReader();
                typeNames = new List<string>(md.TypeDefinitions.Count);
                foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
                {
                    TypeDefinition td = md.GetTypeDefinition(handle);
                    if ((td.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
                    {
                        continue; // only public types can collide for a snippet
                    }

                    string ns = md.GetString(td.Namespace);
                    string name = md.GetString(td.Name);
                    typeNames.Add(string.IsNullOrEmpty(ns) ? name : ns + "." + name);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    // Disk layout of the RUNNING session, from the game's `env` op.
    internal sealed class EnvPaths
    {
        internal string ModAssembly;
        internal string InteropDir;
        internal string CoreDir;
        internal string RuntimeDir;

        internal string Fingerprint =>
            (this.ModAssembly ?? "-") + "|" + (this.InteropDir ?? "-") + "|"
            + (this.CoreDir ?? "-") + "|" + (this.RuntimeDir ?? "-");
    }
}
