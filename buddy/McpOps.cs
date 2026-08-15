#if FEATURE_MCP
using System;
using System.Collections.Generic;
using System.Text;

namespace HeartopiaMod
{
    // ============================================================================================
    // Op registry for the MCP bridge.
    //
    // An "op" is one request the agent can make of the running game. Registration is EXPLICIT — no
    // attribute scanning, no reflection sweep — so `grep McpOps.Register` lists the entire surface,
    // which is the same reason the rest of this mod resolves things by hand.
    //
    // Handlers run ONLY on the Unity main thread, from McpBridge.Drain(). They return a JSON
    // fragment (an object, `{...}`) that gets spliced into the response envelope, and raise
    // McpOpException to answer with a structured error code instead of a stack trace.
    //
    // The dictionary is built once during OnInitializeMelon and never mutated afterwards; the
    // listener starts after that, so the socket threads only ever read a frozen map.
    // ============================================================================================

    internal delegate string McpOpHandler(Dictionary<string, object> args);

    // A handler-raised failure that maps to a wire error code rather than `internal`.
    internal sealed class McpOpException : Exception
    {
        internal readonly string Code;

        internal McpOpException(string code, string message)
            : base(message)
        {
            this.Code = code;
        }
    }

    [Flags]
    internal enum McpOpFlags
    {
        None = 0,
        Read = 1 << 0,
        // Mutates game or mod state — gated on McpBridge.AllowWrites.
        Write = 1 << 1,
        // Can kill the process (arbitrary invoke, plugin/eval) — gated on McpBridge.AllowUnsafe.
        Unsafe = 1 << 2,
        // Refused with `world_not_ready` unless the world-ready gate is open. AGENTS.md §1: never
        // resolve/inflate game types before a world exists.
        NeedsWorld = 1 << 3,
    }

    internal enum McpOpCost
    {
        // Reads cached state; many may run in one frame within the time budget.
        Cheap,
        // Scans, allocates, or otherwise costs real frame time — at most one per frame.
        Heavy,
    }

    internal static class McpOps
    {
        internal sealed class OpEntry
        {
            internal string Name;
            internal McpOpFlags Flags;
            internal McpOpCost Cost;
            internal string Summary;
            // JSON object describing the accepted arguments, spliced verbatim into rpc.describe.
            internal string ArgsSchema;
            internal McpOpHandler Handler;
        }

        private static readonly Dictionary<string, OpEntry> Registry =
            new Dictionary<string, OpEntry>(StringComparer.Ordinal);

        private static string describeCache;

        internal static int Count => Registry.Count;

        internal static void Register(string name, McpOpFlags flags, McpOpCost cost, string summary,
                                      string argsSchema, McpOpHandler handler)
        {
            if (string.IsNullOrEmpty(name) || handler == null)
            {
                return;
            }

            Registry[name] = new OpEntry
            {
                Name = name,
                Flags = flags,
                Cost = cost,
                Summary = summary ?? string.Empty,
                ArgsSchema = string.IsNullOrEmpty(argsSchema) ? "{}" : argsSchema,
                Handler = handler,
            };

            describeCache = null;
        }

        internal static bool TryGet(string name, out OpEntry op)
        {
            if (name == null)
            {
                op = null;
                return false;
            }

            return Registry.TryGetValue(name, out op);
        }

        // The self-description the bridge turns into an MCP tool list. Built once and cached: it is
        // handed out on every `hello`, and it never changes after registration.
        internal static string DescribeJson()
        {
            string cached = describeCache;
            if (cached != null)
            {
                return cached;
            }

            List<string> names = new List<string>(Registry.Keys);
            names.Sort(StringComparer.Ordinal);

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Num("protocol", McpBridge.RpcProtocolVersion);
            w.BeginArray("ops");
            for (int i = 0; i < names.Count; i++)
            {
                OpEntry op = Registry[names[i]];
                w.BeginArrayObject();
                w.Str("name", op.Name);
                w.Str("summary", op.Summary);
                w.Str("cost", op.Cost == McpOpCost.Heavy ? "heavy" : "cheap");
                w.Bool("needsWorld", (op.Flags & McpOpFlags.NeedsWorld) != 0);
                w.Bool("write", (op.Flags & McpOpFlags.Write) != 0);
                w.Bool("unsafe", (op.Flags & McpOpFlags.Unsafe) != 0);
                // A complete JSON Schema object — the bridge uses it as the MCP tool's inputSchema
                // without translating anything.
                w.Raw("argsSchema", op.ArgsSchema);
                w.EndObject();
            }

            w.EndArray();
            w.EndObject();

            cached = w.ToString();
            describeCache = cached;
            return cached;
        }

        // Sentinel a handler returns to say "not finished — run me again next frame".
        //
        // Needed because some answers cannot be produced inside the frame that asked for them: a
        // screenshot is captured after rendering, i.e. strictly later than the Update-phase pump. The
        // handler requests the work, returns this, and the pump re-queues the call; the socket thread
        // is still blocked on its 5 s wait, so the agent sees one normal synchronous response.
        //
        // Compared BY REFERENCE, so an ordinary "{}" result can never be mistaken for it.
        internal static readonly string Defer = new string("__mcp_defer__".ToCharArray());

        // Small helper for handlers that answer with a flat object.
        internal static string EmptyObject => "{}";

        internal static string StringList(string name, IReadOnlyList<string> values)
        {
            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.BeginArray(name);
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    w.ArrayStr(values[i]);
                }
            }

            w.EndArray();
            w.EndObject();
            return w.ToString();
        }

        internal static string Escape(string value)
        {
            StringBuilder sb = new StringBuilder((value?.Length ?? 0) + 2);
            McpJsonWriter.AppendString(sb, value);
            return sb.ToString();
        }
    }
}
#endif
