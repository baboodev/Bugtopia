#if FEATURE_MCP
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    // ============================================================================================
    // Live type reflection over the embedded Mono runtime — the ops that make AGENTS.md §7's alias
    // ritual unnecessary for new work.
    //
    // Today, finding a game type means guessing: `Gameplay` or `GamePlay`? an `Il2Cpp` prefix?
    // `ScriptsRefactory` duplicate? — then cross-checking a decompilation that may predate the
    // installed build. These ops answer from the RUNNING process instead, so the answer is true by
    // construction and survives a game patch.
    //
    // ── WHY THIS IS Read TIER AND NOT unsafe ─────────────────────────────────────────────────────
    // The crash family this project fights is object pointers: a raw `MonoObject*` that SGen moves
    // or collects. Nothing here touches one. Class, method and field handles are metadata that lives
    // as long as the image (AGENTS.md §9 explicitly allows caching them raw), and no invocation
    // happens — only names, offsets, flags and arities are read. Describing a type cannot crash the
    // game the way calling into it can, so it needs no privilege beyond reading.
    //
    // `mono_field_get_name` and `mono_method_get_name` point into metadata and must NOT be freed;
    // `mono_type_get_name` returns a g_malloc'd string that MUST be freed with mono_free. Getting
    // that backwards is a leak in one direction and a heap corruption in the other.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const int McpMonoMemberCap = 400;

        private void RegisterMcpMonoOps()
        {
            McpOps.Register(
                "mono.find",
                McpOpFlags.Read | McpOpFlags.NeedsWorld,
                McpOpCost.Cheap,
                "Resolve a game type in the RUNNING build by full name, trying the usual spelling "
                + "variants for you (Gameplay/GamePlay, Il2Cpp prefix, ScriptsRefactory). Returns "
                + "which spelling actually resolved — that is the name to use everywhere else.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"name\":{\"type\":\"string\",\"description\":\"full type name, e.g. XDTLevelAndEntity.Gameplay.Component.Bubble.BubbleComponent\"}},"
                + "\"required\":[\"name\"],\"additionalProperties\":false}",
                this.HandleMcpMonoFind);

            McpOps.Register(
                "mono.describe",
                McpOpFlags.Read | McpOpFlags.NeedsWorld,
                McpOpCost.Heavy,
                "Fields and methods of a game type as the RUNNING build defines them — names, types, "
                + "offsets, static/instance, arities — plus its base chain. This is the answer the "
                + "decompilation dumps only approximate: it cannot be stale, because it is read from "
                + "the loaded image.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"name\":{\"type\":\"string\",\"description\":\"full type name (spelling variants are tried)\"},"
                + "\"members\":{\"type\":\"string\",\"description\":\"fields | methods | both (default)\"},"
                + "\"filter\":{\"type\":\"string\",\"description\":\"case-insensitive substring of the member name\"},"
                + "\"inherited\":{\"type\":\"boolean\",\"description\":\"walk base classes too, default false\"}},"
                + "\"required\":[\"name\"],\"additionalProperties\":false}",
                this.HandleMcpMonoDescribe);

            McpOps.Register(
                "mono.search",
                McpOpFlags.Read | McpOpFlags.NeedsWorld,
                McpOpCost.Heavy,
                "Find game types whose name contains a substring, across every loaded image. This is "
                + "the discovery step mono.find cannot do — mono.find resolves a name you already "
                + "guessed. Answers from the running build, so it cannot go stale the way a "
                + "decompilation dump can.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"query\":{\"type\":\"string\",\"description\":\"case-insensitive substring of the type name\"},"
                + "\"namespace\":{\"type\":\"string\",\"description\":\"optional: also require this substring in the namespace\"},"
                + "\"image\":{\"type\":\"string\",\"description\":\"optional: restrict to images whose name contains this\"},"
                + "\"limit\":{\"type\":\"integer\",\"description\":\"max rows, default 50, max 500\"}},"
                + "\"required\":[\"query\"],\"additionalProperties\":false}",
                this.HandleMcpMonoSearch);
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // mono.search
        // ────────────────────────────────────────────────────────────────────────────────────────

        private const int MonoTableTypeDef = 0x02;
        private const uint MonoTypeDefColName = 1;
        private const uint MonoTypeDefColNamespace = 2;

        private string HandleMcpMonoSearch(Dictionary<string, object> args)
        {
            string query = McpArgs.GetString(args, "query");
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new McpOpException("bad_args", "'query' is required");
            }

            if (auraMonoImageGetTableInfo == null || auraMonoTableInfoGetRows == null
                || auraMonoMetadataDecodeRowCol == null || auraMonoMetadataStringHeap == null
                || auraMonoAssemblyForeach == null || auraMonoAssemblyGetImage == null
                || auraMonoImageGetName == null)
            {
                throw new McpOpException("internal",
                    "metadata table exports unavailable on this build — search is not possible");
            }

            string nsFilter = McpArgs.GetString(args, "namespace");
            string imageFilter = McpArgs.GetString(args, "image");
            int limit = McpArgs.GetInt(args, "limit", 50);
            if (limit < 1) { limit = 1; } else if (limit > 500) { limit = 500; }

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            McpMonoSweepImages.Clear();
            if (mcpMonoSweepCallbackKeepAlive == null)
            {
                mcpMonoSweepCallbackKeepAlive = McpMonoSweepCollect;
            }

            auraMonoAssemblyForeach(mcpMonoSweepCallbackKeepAlive, IntPtr.Zero);

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.BeginArray("types");

            int scannedImages = 0;
            int scannedTypes = 0;
            int matched = 0;
            int emitted = 0;

            for (int i = 0; i < McpMonoSweepImages.Count; i++)
            {
                IntPtr image = McpMonoSweepImages[i];
                if (image == IntPtr.Zero)
                {
                    continue;
                }

                string imageName;
                try
                {
                    imageName = Marshal.PtrToStringAnsi(auraMonoImageGetName(image)) ?? string.Empty;
                }
                catch
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(imageFilter)
                    && imageName.IndexOf(imageFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                IntPtr table;
                int rows;
                try
                {
                    table = auraMonoImageGetTableInfo(image, MonoTableTypeDef);
                    if (table == IntPtr.Zero)
                    {
                        continue;
                    }

                    rows = auraMonoTableInfoGetRows(table);
                }
                catch
                {
                    continue;
                }

                scannedImages++;

                for (int row = 0; row < rows; row++)
                {
                    string typeName;
                    string typeNamespace;
                    try
                    {
                        // Names only — no MonoClass is created, so nothing is loaded or initialised
                        // by searching.
                        uint nameIdx = auraMonoMetadataDecodeRowCol(table, row, MonoTypeDefColName);
                        uint nsIdx = auraMonoMetadataDecodeRowCol(table, row, MonoTypeDefColNamespace);
                        typeName = Marshal.PtrToStringAnsi(auraMonoMetadataStringHeap(image, nameIdx));
                        typeNamespace = Marshal.PtrToStringAnsi(auraMonoMetadataStringHeap(image, nsIdx));
                    }
                    catch
                    {
                        continue;
                    }

                    scannedTypes++;
                    if (string.IsNullOrEmpty(typeName)
                        || typeName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(nsFilter)
                        && (typeNamespace == null
                            || typeNamespace.IndexOf(nsFilter, StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        continue;
                    }

                    matched++;
                    if (emitted >= limit)
                    {
                        continue;
                    }

                    emitted++;
                    w.BeginArrayObject();
                    w.Str("name", string.IsNullOrEmpty(typeNamespace)
                        ? typeName
                        : typeNamespace + "." + typeName);
                    w.Str("image", imageName);
                    w.EndObject();
                }
            }

            w.EndArray();
            sw.Stop();
            w.Num("scannedImages", scannedImages);
            w.Num("scannedTypes", scannedTypes);
            w.Num("matched", matched);
            w.Num("returned", emitted);
            w.Num("scanMs", sw.Elapsed.TotalMilliseconds);
            // Say so rather than let a capped list read as the complete answer — concluding "there is
            // no such type" from a truncated search is the exact failure this op exists to prevent.
            w.Bool("truncated", matched > emitted);
            w.EndObject();
            return w.ToString();
        }

        // The spelling variants AGENTS.md §7 tells a human to write out by hand. Doing it here is the
        // entire point: the caller states the type once and learns which form the build actually uses.
        private static List<string> BuildMcpMonoNameCandidates(string name)
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

        private bool TryResolveMcpMonoClass(string name, out IntPtr klass, out string resolvedName)
        {
            klass = IntPtr.Zero;
            resolvedName = null;

            List<string> candidates = BuildMcpMonoNameCandidates(name);
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
                IntPtr found = this.TryFindMcpMonoClassInAnyImage(candidates[i]);
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
        private static readonly List<IntPtr> McpMonoSweepImages = new List<IntPtr>();
        private static MonoAssemblyForeachCallbackDelegate mcpMonoSweepCallbackKeepAlive;

        private IntPtr TryFindMcpMonoClassInAnyImage(string fullTypeName)
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
                McpMonoSweepImages.Clear();
                if (mcpMonoSweepCallbackKeepAlive == null)
                {
                    // Held in a static: a delegate handed to native code and then collected is a
                    // callback into freed memory.
                    mcpMonoSweepCallbackKeepAlive = McpMonoSweepCollect;
                }

                auraMonoAssemblyForeach(mcpMonoSweepCallbackKeepAlive, IntPtr.Zero);

                for (int i = 0; i < McpMonoSweepImages.Count; i++)
                {
                    IntPtr image = McpMonoSweepImages[i];
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

        private static void McpMonoSweepCollect(IntPtr assembly, IntPtr userData)
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
                    McpMonoSweepImages.Add(image);
                }
            }
            catch
            {
            }
        }

        private string HandleMcpMonoFind(Dictionary<string, object> args)
        {
            string name = McpArgs.GetString(args, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new McpOpException("bad_args", "'name' is required");
            }

            bool found = this.TryResolveMcpMonoClass(name, out IntPtr klass, out string resolved);

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Bool("found", found);
            w.Str("asked", name);
            if (found)
            {
                w.Str("resolved", resolved);
                w.Bool("exactAsAsked", string.Equals(resolved, name, StringComparison.Ordinal));
                this.WriteMcpMonoClassIdentity(w, klass);
            }
            else
            {
                w.BeginArray("tried");
                List<string> candidates = BuildMcpMonoNameCandidates(name);
                for (int i = 0; i < candidates.Count; i++)
                {
                    w.ArrayStr(candidates[i]);
                }

                w.EndArray();
                w.Str("hint", "not loaded in this session — many game assemblies only load after "
                    + "entering a town (AGENTS.md §5). Check status.world.ready first.");
            }

            w.EndObject();
            return w.ToString();
        }

        private void WriteMcpMonoClassIdentity(McpJsonWriter w, IntPtr klass)
        {
            string ns = auraMonoClassGetNamespace != null
                ? Marshal.PtrToStringAnsi(auraMonoClassGetNamespace(klass)) : null;
            string cn = auraMonoClassGetName != null
                ? Marshal.PtrToStringAnsi(auraMonoClassGetName(klass)) : null;
            w.Str("namespace", ns);
            w.Str("class", cn);
            w.Bool("valueType", auraMonoClassIsValueType != null && auraMonoClassIsValueType(klass) != 0);

            w.BeginArray("baseChain");
            IntPtr parent = auraMonoClassGetParent != null ? auraMonoClassGetParent(klass) : IntPtr.Zero;
            int guard = 0;
            while (parent != IntPtr.Zero && guard++ < 16)
            {
                string pns = auraMonoClassGetNamespace != null
                    ? Marshal.PtrToStringAnsi(auraMonoClassGetNamespace(parent)) : null;
                string pcn = auraMonoClassGetName != null
                    ? Marshal.PtrToStringAnsi(auraMonoClassGetName(parent)) : null;
                w.ArrayStr(string.IsNullOrEmpty(pns) ? pcn : pns + "." + pcn);
                parent = auraMonoClassGetParent(parent);
            }

            w.EndArray();
        }

        private string HandleMcpMonoDescribe(Dictionary<string, object> args)
        {
            string name = McpArgs.GetString(args, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new McpOpException("bad_args", "'name' is required");
            }

            if (!this.TryResolveMcpMonoClass(name, out IntPtr klass, out string resolved))
            {
                throw new McpOpException("bad_args",
                    "no type resolved for '" + name + "' — run mono.find to see the spellings tried");
            }

            string which = McpArgs.GetString(args, "members", "both");
            string filter = McpArgs.GetString(args, "filter");
            bool inherited = McpArgs.GetBool(args, "inherited", false);
            bool wantFields = !string.Equals(which, "methods", StringComparison.OrdinalIgnoreCase);
            bool wantMethods = !string.Equals(which, "fields", StringComparison.OrdinalIgnoreCase);

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Str("resolved", resolved);
            this.WriteMcpMonoClassIdentity(w, klass);

            int fieldCount = 0;
            int methodCount = 0;
            bool truncated = false;

            if (wantFields)
            {
                w.BeginArray("fields");
                IntPtr cursor = klass;
                int depth = 0;
                while (cursor != IntPtr.Zero && depth++ < 16)
                {
                    if (!this.WriteMcpMonoFields(w, cursor, filter, depth > 1, ref fieldCount))
                    {
                        truncated = true;
                        break;
                    }

                    if (!inherited)
                    {
                        break;
                    }

                    cursor = auraMonoClassGetParent != null ? auraMonoClassGetParent(cursor) : IntPtr.Zero;
                }

                w.EndArray();
            }

            if (wantMethods)
            {
                w.BeginArray("methods");
                IntPtr cursor = klass;
                int depth = 0;
                while (cursor != IntPtr.Zero && depth++ < 16)
                {
                    if (!this.WriteMcpMonoMethods(w, cursor, filter, depth > 1, ref methodCount))
                    {
                        truncated = true;
                        break;
                    }

                    if (!inherited)
                    {
                        break;
                    }

                    cursor = auraMonoClassGetParent != null ? auraMonoClassGetParent(cursor) : IntPtr.Zero;
                }

                w.EndArray();
            }

            w.Num("fieldCount", fieldCount);
            w.Num("methodCount", methodCount);
            // Say so rather than silently returning a partial type — a truncated member list that
            // looks complete is how someone concludes a method does not exist.
            w.Bool("truncated", truncated);
            if (truncated)
            {
                w.Str("truncatedNote", "member cap of " + McpMonoMemberCap
                    + " reached — narrow it with `filter`, or ask for fields and methods separately");
            }

            w.EndObject();
            return w.ToString();
        }

        // Returns false when the cap is hit, so the caller can report truncation instead of quietly
        // stopping.
        private bool WriteMcpMonoFields(McpJsonWriter w, IntPtr klass, string filter, bool fromBase, ref int count)
        {
            if (auraMonoClassGetFields == null || auraMonoFieldGetName == null)
            {
                return true;
            }

            IntPtr iter = IntPtr.Zero;
            while (true)
            {
                IntPtr field = auraMonoClassGetFields(klass, ref iter);
                if (field == IntPtr.Zero)
                {
                    return true;
                }

                string fieldName = Marshal.PtrToStringAnsi(auraMonoFieldGetName(field));
                if (string.IsNullOrEmpty(fieldName))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(filter)
                    && fieldName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (count >= McpMonoMemberCap)
                {
                    return false;
                }

                count++;
                w.BeginArrayObject();
                w.Str("name", fieldName);
                w.Str("type", McpMonoFieldTypeName(field));

                uint flags = auraMonoFieldGetFlags != null ? auraMonoFieldGetFlags(field) : 0u;
                // FIELD_ATTRIBUTE_STATIC = 0x10, FIELD_ATTRIBUTE_FIELD_ACCESS_MASK = 0x07.
                w.Bool("static", (flags & 0x10u) != 0u);
                w.Num("access", flags & 0x07u);
                if (auraMonoFieldGetOffset != null)
                {
                    // The raw mono offset. Reading an instance field through a MonoObject* means
                    // subtracting 2*IntPtr.Size for the object header — see the struct-offset note in
                    // project memory before using this for pointer arithmetic.
                    w.Num("offset", auraMonoFieldGetOffset(field));
                }

                if (fromBase)
                {
                    w.Bool("inherited", true);
                }

                w.EndObject();
            }
        }

        private bool WriteMcpMonoMethods(McpJsonWriter w, IntPtr klass, string filter, bool fromBase, ref int count)
        {
            if (auraMonoClassGetMethods == null || auraMonoMethodGetName == null)
            {
                return true;
            }

            IntPtr iter = IntPtr.Zero;
            while (true)
            {
                IntPtr method = auraMonoClassGetMethods(klass, ref iter);
                if (method == IntPtr.Zero)
                {
                    return true;
                }

                string methodName = Marshal.PtrToStringAnsi(auraMonoMethodGetName(method));
                if (string.IsNullOrEmpty(methodName))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(filter)
                    && methodName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (count >= McpMonoMemberCap)
                {
                    return false;
                }

                count++;
                w.BeginArrayObject();
                w.Str("name", methodName);

                // Arity is what callers actually need: every AuraMono invoke resolves a method by
                // name AND parameter count, and picking the wrong overload is a documented way to AV
                // the process (AGENTS.md §12).
                if (auraMonoMethodSignature != null && auraMonoSignatureGetParamCount != null)
                {
                    IntPtr sig = auraMonoMethodSignature(method);
                    if (sig != IntPtr.Zero)
                    {
                        w.Num("params", auraMonoSignatureGetParamCount(sig));
                    }
                }

                if (fromBase)
                {
                    w.Bool("inherited", true);
                }

                w.EndObject();
            }
        }

        private static string McpMonoFieldTypeName(IntPtr field)
        {
            if (auraMonoFieldGetType == null || auraMonoTypeGetName == null)
            {
                return null;
            }

            IntPtr type = auraMonoFieldGetType(field);
            if (type == IntPtr.Zero)
            {
                return null;
            }

            IntPtr namePtr = auraMonoTypeGetName(type);
            if (namePtr == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringAnsi(namePtr);
            }
            finally
            {
                // mono_type_get_name g_malloc's its result — unlike the metadata-owned name pointers
                // above, this one leaks unless it is handed back.
                auraMonoFree?.Invoke(namePtr);
            }
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // The same reach, for `eval` snippets and sandbox plugins (IHostApi.Mono)
        // ────────────────────────────────────────────────────────────────────────────────────────
        //
        // Without this, a snippet can only touch the mod's PUBLIC surface — which excludes every
        // AuraMono helper — so `eval` could not reach a single game type. That gap is what made an
        // earlier claim ("eval already covers arbitrary invocation") false.

        internal IntPtr McpMonoFindClass(string fullName)
        {
            return this.TryResolveMcpMonoClass(fullName, out IntPtr klass, out _) ? klass : IntPtr.Zero;
        }

        internal IntPtr McpMonoFindMethod(IntPtr klass, string methodName, int paramCount)
        {
            if (klass == IntPtr.Zero || string.IsNullOrEmpty(methodName))
            {
                return IntPtr.Zero;
            }

            return this.FindAuraMonoMethodOnHierarchy(klass, methodName, paramCount);
        }

        // ── Object graph ─────────────────────────────────────────────────────────────────────────
        // The three capabilities that make AuraMono usable from sandbox code, and they only work as
        // a set: enumerate instances, read fields off them, and hold them safely in between. Ship
        // any one alone and it is either useless or a trap — enumeration without pins is the
        // documented mid-loop crash (AGENTS.md §11), because every member read allocates and SGen
        // moves what you are still holding.

        internal bool McpMonoGetComponents(IntPtr componentClass, List<IntPtr> objects, List<uint> pins,
                                           out string error)
        {
            error = null;
            if (componentClass == IntPtr.Zero)
            {
                error = "component class is null";
                return false;
            }

            if (!AuraMonoPinningAvailable)
            {
                // Fail closed. Handing back unpinned pointers would "work" until the first GC.
                error = "pinning unavailable on this build — refusing to hand out unpinned objects";
                return false;
            }

            if (!this.TryAuraMonoGetComponentObjects(componentClass, out List<IntPtr> found,
                    out bool infrastructureOk, pins) || found == null)
            {
                // "Ran and found nothing" is a SUCCESS with zero rows. Reporting it as a failure is
                // how a caller concludes their type is wrong when the world simply has none of them
                // right now.
                if (infrastructureOk)
                {
                    return true;
                }

                error = "GetComponents could not run for that class — it is not an ECS component "
                    + "type, the world is not up, or the generic method could not be inflated";
                return false;
            }

            objects.AddRange(found);
            return true;
        }

        internal bool McpMonoEnumerateCollection(IntPtr collectionObj, List<IntPtr> objects, List<uint> pins,
                                                 out string error)
        {
            error = null;
            if (collectionObj == IntPtr.Zero)
            {
                error = "collection is null";
                return false;
            }

            if (!AuraMonoPinningAvailable)
            {
                error = "pinning unavailable on this build — refusing to hand out unpinned objects";
                return false;
            }

            if (!this.TryEnumerateAuraMonoCollectionItems(collectionObj, objects, pins))
            {
                error = "not an enumerable mono collection, or enumeration failed";
                return false;
            }

            return true;
        }

        internal bool McpMonoTryGetField(IntPtr obj, string fieldName, out IntPtr valueObj)
        {
            valueObj = IntPtr.Zero;
            if (obj == IntPtr.Zero || string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            return this.TryGetMonoObjectMember(obj, fieldName, out valueObj);
        }

        internal uint McpMonoGetUInt(IntPtr obj, string[] fieldNames)
        {
            if (obj == IntPtr.Zero || fieldNames == null || fieldNames.Length == 0)
            {
                return 0u;
            }

            return this.TryReadAuraMonoUIntField(obj, fieldNames);
        }

        internal int McpMonoGetInt(IntPtr boxedStruct, string fieldName)
        {
            if (boxedStruct == IntPtr.Zero || string.IsNullOrEmpty(fieldName))
            {
                return 0;
            }

            return this.TryReadAuraMonoStructIntField(boxedStruct, fieldName);
        }

        // mono_string_to_utf8 allocates; the result must go back through mono_free or it leaks.
        internal static string McpMonoGetString(IntPtr monoStringObj)
        {
            if (monoStringObj == IntPtr.Zero || auraMonoStringToUtf8 == null)
            {
                return null;
            }

            IntPtr utf8 = IntPtr.Zero;
            try
            {
                utf8 = auraMonoStringToUtf8(monoStringObj);
                return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (utf8 != IntPtr.Zero)
                {
                    auraMonoFree?.Invoke(utf8);
                }
            }
        }

        internal static bool McpMonoPinningAvailable => AuraMonoPinningAvailable;

        internal static uint McpMonoPin(IntPtr obj) => AuraMonoPinningAvailable ? AuraMonoPinNew(obj) : 0u;

        internal static void McpMonoUnpin(uint handle)
        {
            if (handle != 0u)
            {
                FreeAuraMonoPins(new List<uint> { handle });
            }
        }

        // Goes through the guarded invoke, never the raw export (CI lint E1/E2): null-method guard,
        // exception check and garbage-result suppression all live there.
        //
        // ⚠️ The returned IntPtr is a MonoObject* and is NOT safe to hold: SGen moves it as soon as
        // anything else allocates. Read what you need from it immediately, or pin it.
        internal bool McpMonoInvoke(IntPtr method, IntPtr instance, IntPtr[] args,
                                    out IntPtr result, out string error)
        {
            result = IntPtr.Zero;
            error = null;

            if (method == IntPtr.Zero)
            {
                error = "method is null";
                return false;
            }

            int count = args == null ? 0 : args.Length;
            IntPtr block = IntPtr.Zero;
            try
            {
                if (count > 0)
                {
                    block = Marshal.AllocHGlobal(IntPtr.Size * count);
                    for (int i = 0; i < count; i++)
                    {
                        Marshal.WriteIntPtr(block, i * IntPtr.Size, args[i]);
                    }
                }

                return TryAuraInvoke(method, instance, block, out result, out error);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                if (block != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(block);
                }
            }
        }
    }
}
#endif
