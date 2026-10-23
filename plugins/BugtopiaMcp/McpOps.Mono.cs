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
    internal sealed partial class McpRuntime
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

            if (HeartopiaComplete.auraMonoImageGetTableInfo == null || HeartopiaComplete.auraMonoTableInfoGetRows == null
                || HeartopiaComplete.auraMonoMetadataDecodeRowCol == null || HeartopiaComplete.auraMonoMetadataStringHeap == null
                || HeartopiaComplete.auraMonoAssemblyForeach == null || HeartopiaComplete.auraMonoAssemblyGetImage == null
                || HeartopiaComplete.auraMonoImageGetName == null)
            {
                throw new McpOpException("internal",
                    "metadata table exports unavailable on this build — search is not possible");
            }

            string nsFilter = McpArgs.GetString(args, "namespace");
            string imageFilter = McpArgs.GetString(args, "image");
            int limit = McpArgs.GetInt(args, "limit", 50);
            if (limit < 1) { limit = 1; } else if (limit > 500) { limit = 500; }

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            // Same image sweep FindAuraMonoClassAnySpelling uses (HeartopiaComplete.AuraClassResolve.cs);
            // both are synchronous and main-thread, so sharing the buffer and the kept-alive callback
            // is safe.
            HeartopiaComplete.AuraMonoSweepImages.Clear();
            if (HeartopiaComplete.auraMonoSweepCallbackKeepAlive == null)
            {
                HeartopiaComplete.auraMonoSweepCallbackKeepAlive = HeartopiaComplete.AuraMonoSweepCollect;
            }

            HeartopiaComplete.auraMonoAssemblyForeach(HeartopiaComplete.auraMonoSweepCallbackKeepAlive, IntPtr.Zero);

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.BeginArray("types");

            int scannedImages = 0;
            int scannedTypes = 0;
            int matched = 0;
            int emitted = 0;

            for (int i = 0; i < HeartopiaComplete.AuraMonoSweepImages.Count; i++)
            {
                IntPtr image = HeartopiaComplete.AuraMonoSweepImages[i];
                if (image == IntPtr.Zero)
                {
                    continue;
                }

                string imageName;
                try
                {
                    imageName = Marshal.PtrToStringAnsi(HeartopiaComplete.auraMonoImageGetName(image)) ?? string.Empty;
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
                    table = HeartopiaComplete.auraMonoImageGetTableInfo(image, MonoTableTypeDef);
                    if (table == IntPtr.Zero)
                    {
                        continue;
                    }

                    rows = HeartopiaComplete.auraMonoTableInfoGetRows(table);
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
                        uint nameIdx = HeartopiaComplete.auraMonoMetadataDecodeRowCol(table, row, MonoTypeDefColName);
                        uint nsIdx = HeartopiaComplete.auraMonoMetadataDecodeRowCol(table, row, MonoTypeDefColNamespace);
                        typeName = Marshal.PtrToStringAnsi(HeartopiaComplete.auraMonoMetadataStringHeap(image, nameIdx));
                        typeNamespace = Marshal.PtrToStringAnsi(HeartopiaComplete.auraMonoMetadataStringHeap(image, nsIdx));
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

        // Name candidates + the exhaustive image sweep moved to HeartopiaComplete.AuraClassResolve.cs
        // (HeartopiaComplete.BuildAuraMonoNameCandidates / TryResolveAuraMonoClassAnySpelling / FindAuraMonoClassAnySpelling)
        // when TryAuraSendCommand started resolving command types through them: that helper ships in
        // normal builds, so its resolver cannot live behind this gate.

        private string HandleMcpMonoFind(Dictionary<string, object> args)
        {
            string name = McpArgs.GetString(args, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new McpOpException("bad_args", "'name' is required");
            }

            bool found = this.Mod.TryResolveAuraMonoClassAnySpelling(name, out IntPtr klass, out string resolved);

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
                List<string> candidates = HeartopiaComplete.BuildAuraMonoNameCandidates(name);
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
            string ns = HeartopiaComplete.auraMonoClassGetNamespace != null
                ? Marshal.PtrToStringAnsi(HeartopiaComplete.auraMonoClassGetNamespace(klass)) : null;
            string cn = HeartopiaComplete.auraMonoClassGetName != null
                ? Marshal.PtrToStringAnsi(HeartopiaComplete.auraMonoClassGetName(klass)) : null;
            w.Str("namespace", ns);
            w.Str("class", cn);
            w.Bool("valueType", HeartopiaComplete.auraMonoClassIsValueType != null && HeartopiaComplete.auraMonoClassIsValueType(klass) != 0);

            w.BeginArray("baseChain");
            IntPtr parent = HeartopiaComplete.auraMonoClassGetParent != null ? HeartopiaComplete.auraMonoClassGetParent(klass) : IntPtr.Zero;
            int guard = 0;
            while (parent != IntPtr.Zero && guard++ < 16)
            {
                string pns = HeartopiaComplete.auraMonoClassGetNamespace != null
                    ? Marshal.PtrToStringAnsi(HeartopiaComplete.auraMonoClassGetNamespace(parent)) : null;
                string pcn = HeartopiaComplete.auraMonoClassGetName != null
                    ? Marshal.PtrToStringAnsi(HeartopiaComplete.auraMonoClassGetName(parent)) : null;
                w.ArrayStr(string.IsNullOrEmpty(pns) ? pcn : pns + "." + pcn);
                parent = HeartopiaComplete.auraMonoClassGetParent(parent);
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

            if (!this.Mod.TryResolveAuraMonoClassAnySpelling(name, out IntPtr klass, out string resolved))
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

                    cursor = HeartopiaComplete.auraMonoClassGetParent != null ? HeartopiaComplete.auraMonoClassGetParent(cursor) : IntPtr.Zero;
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

                    cursor = HeartopiaComplete.auraMonoClassGetParent != null ? HeartopiaComplete.auraMonoClassGetParent(cursor) : IntPtr.Zero;
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
            if (HeartopiaComplete.auraMonoClassGetFields == null || HeartopiaComplete.auraMonoFieldGetName == null)
            {
                return true;
            }

            IntPtr iter = IntPtr.Zero;
            while (true)
            {
                IntPtr field = HeartopiaComplete.auraMonoClassGetFields(klass, ref iter);
                if (field == IntPtr.Zero)
                {
                    return true;
                }

                string fieldName = Marshal.PtrToStringAnsi(HeartopiaComplete.auraMonoFieldGetName(field));
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

                uint flags = HeartopiaComplete.auraMonoFieldGetFlags != null ? HeartopiaComplete.auraMonoFieldGetFlags(field) : 0u;
                // FIELD_ATTRIBUTE_STATIC = 0x10, FIELD_ATTRIBUTE_FIELD_ACCESS_MASK = 0x07.
                w.Bool("static", (flags & 0x10u) != 0u);
                w.Num("access", flags & 0x07u);
                if (HeartopiaComplete.auraMonoFieldGetOffset != null)
                {
                    // The raw mono offset. Reading an instance field through a MonoObject* means
                    // subtracting 2*IntPtr.Size for the object header — see the struct-offset note in
                    // project memory before using this for pointer arithmetic.
                    w.Num("offset", HeartopiaComplete.auraMonoFieldGetOffset(field));
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
            if (HeartopiaComplete.auraMonoClassGetMethods == null || HeartopiaComplete.auraMonoMethodGetName == null)
            {
                return true;
            }

            IntPtr iter = IntPtr.Zero;
            while (true)
            {
                IntPtr method = HeartopiaComplete.auraMonoClassGetMethods(klass, ref iter);
                if (method == IntPtr.Zero)
                {
                    return true;
                }

                string methodName = Marshal.PtrToStringAnsi(HeartopiaComplete.auraMonoMethodGetName(method));
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
                if (HeartopiaComplete.auraMonoMethodSignature != null && HeartopiaComplete.auraMonoSignatureGetParamCount != null)
                {
                    IntPtr sig = HeartopiaComplete.auraMonoMethodSignature(method);
                    if (sig != IntPtr.Zero)
                    {
                        w.Num("params", HeartopiaComplete.auraMonoSignatureGetParamCount(sig));
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
            if (HeartopiaComplete.auraMonoFieldGetType == null || HeartopiaComplete.auraMonoTypeGetName == null)
            {
                return null;
            }

            IntPtr type = HeartopiaComplete.auraMonoFieldGetType(field);
            if (type == IntPtr.Zero)
            {
                return null;
            }

            IntPtr namePtr = HeartopiaComplete.auraMonoTypeGetName(type);
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
                HeartopiaComplete.auraMonoFree?.Invoke(namePtr);
            }
        }

    }
}
