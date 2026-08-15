using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BugtopiaMcp
{
    // ============================================================================================
    // Bugtopia MCP bridge — phase 1.
    //
    // stdin/stdout carry newline-delimited JSON-RPC 2.0 (the MCP stdio transport). NOTHING else may
    // write to stdout: a stray Console.WriteLine corrupts the stream and the client drops the
    // server. All diagnostics go to stderr, which the client shows as server logs.
    //
    // Tools are a STATIC core plus a `game_rpc` passthrough. The game already self-describes its op
    // registry over `rpc.describe` (phase 2 turns that into a dynamic tool list); until then the
    // passthrough is what reaches any op this bridge does not know by name, so the game can grow
    // ops without a bridge rebuild.
    // ============================================================================================
    internal static class Program
    {
        private const string ServerName = "bugtopia";
        private const string ServerVersion = "0.1.0";
        private const string DefaultProtocolVersion = "2025-06-18";

        private static TextWriter outWriter;

        private static int Main(string[] args)
        {
            Stream stdin = Console.OpenStandardInput();
            Stream stdout = Console.OpenStandardOutput();
            UTF8Encoding utf8 = new UTF8Encoding(false);

            using (StreamReader reader = new StreamReader(stdin, utf8))
            using (StreamWriter writer = new StreamWriter(stdout, utf8) { AutoFlush = true, NewLine = "\n" })
            {
                outWriter = writer;
                Log("bugtopia MCP bridge " + ServerVersion + " ready; endpoint = " + GameLink.EndpointPath());

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        HandleMessage(line);
                    }
                    catch (Exception ex)
                    {
                        // A malformed frame must never kill the server: the client would see the
                        // whole MCP server disappear instead of one failed call.
                        Log("frame failed: " + ex);
                    }
                }
            }

            GameLink.Close();
            return 0;
        }

        private static void HandleMessage(string line)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            string method = GetString(root, "method");
            bool hasId = root.TryGetProperty("id", out JsonElement id);
            // A notification carries no id and MUST NOT be answered.
            string idJson = hasId ? id.GetRawText() : null;

            if (string.IsNullOrEmpty(method))
            {
                return;
            }

            root.TryGetProperty("params", out JsonElement parameters);

            switch (method)
            {
                case "initialize":
                    Respond(idJson, InitializeResult(parameters));
                    return;

                case "notifications/initialized":
                case "notifications/cancelled":
                    return;

                case "ping":
                    Respond(idJson, "{}");
                    return;

                case "tools/list":
                    Respond(idJson, ToolsListResult());
                    return;

                case "tools/call":
                    Respond(idJson, ToolsCallResult(parameters));
                    return;

                default:
                    if (idJson != null)
                    {
                        RespondError(idJson, -32601, "method not found: " + method);
                    }

                    return;
            }
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // MCP surface
        // ────────────────────────────────────────────────────────────────────────────────────────

        private static string InitializeResult(JsonElement parameters)
        {
            // Echo the client's protocol version when it sent one — the most compatible behaviour
            // for a minimal server, and it keeps working as clients move forward.
            string protocol = DefaultProtocolVersion;
            if (parameters.ValueKind == JsonValueKind.Object)
            {
                string requested = GetString(parameters, "protocolVersion");
                if (!string.IsNullOrEmpty(requested))
                {
                    protocol = requested;
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"protocolVersion\":").Append(Quote(protocol));
            sb.Append(",\"capabilities\":{\"tools\":{}}");
            sb.Append(",\"serverInfo\":{\"name\":").Append(Quote(ServerName))
              .Append(",\"version\":").Append(Quote(ServerVersion)).Append('}');
            sb.Append(",\"instructions\":").Append(Quote(
                "Live view of a running Heartopia game through the Bugtopia mod. Tools answer only "
                + "while the game is running with the %LocalLow%/Bugtopia/mcp marker file present. "
                + "Use game_rpc to reach ops this bridge does not expose by name; game_status lists "
                + "every registered op under its `describe` payload."));
            sb.Append('}');
            return sb.ToString();
        }

        // tool name -> op name, rebuilt from the game's own `rpc.describe` on every tools/list.
        private static readonly Dictionary<string, string> ToolToOp = new Dictionary<string, string>(StringComparer.Ordinal);

        // The tool list is generated from the RUNNING BUILD's op registry, so adding an op to the
        // mod needs no change here. When the game is unreachable we still publish a minimal static
        // list: an empty tool list would make the server look broken, and the tools' own error text
        // is what tells the agent the game is down.
        private static string ToolsListResult()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"tools\":[");
            bool first = true;

            GameLink.Result describe = GameLink.Call("rpc.describe", "{}");
            int dynamicCount = 0;

            lock (ToolToOp)
            {
                ToolToOp.Clear();

                if (describe.Ok)
                {
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(describe.Payload);
                        if (doc.RootElement.TryGetProperty("ops", out JsonElement ops)
                            && ops.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement op in ops.EnumerateArray())
                            {
                                string opName = GetString(op, "name");
                                if (string.IsNullOrEmpty(opName))
                                {
                                    continue;
                                }

                                string toolName = "game_" + opName.Replace('.', '_');
                                ToolToOp[toolName] = opName;

                                string description = GetString(op, "summary") ?? opName;
                                if (op.TryGetProperty("needsWorld", out JsonElement nw)
                                    && nw.ValueKind == JsonValueKind.True)
                                {
                                    description += " [needs a loaded world — fails on the login screen]";
                                }

                                if (string.Equals(GetString(op, "cost"), "heavy", StringComparison.Ordinal))
                                {
                                    description += " [heavy: the game runs at most one of these per frame]";
                                }

                                string schema = op.TryGetProperty("argsSchema", out JsonElement s)
                                    ? s.GetRawText()
                                    : "{\"type\":\"object\"}";

                                if (!first) { sb.Append(','); }
                                first = false;
                                AppendTool(sb, toolName, description, schema);
                                dynamicCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("could not parse rpc.describe: " + ex.Message);
                    }
                }
            }

            if (dynamicCount == 0)
            {
                Log("serving the offline fallback tool list (" + (describe.Error ?? "no ops") + ")");

                if (!first) { sb.Append(','); }
                first = false;
                AppendTool(sb, "game_status",
                    "Live state of the running game: mod build, loader, per-frame pump, world-ready "
                    + "gate, frame/FPS, and every active mod feature. Answers on the login screen too. "
                    + "(Offline fallback entry — the real tool list comes from the running game.)",
                    "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
            }

            // Always present, never from the registry: `ping` is answered on the socket thread so it
            // works even while the game's main thread is stalled, and `game_rpc` is the escape hatch
            // for anything the catalogue did not carry.
            if (!first) { sb.Append(','); }
            AppendTool(sb, "game_ping",
                "Check whether the game is running and the bridge socket is reachable. Answered "
                + "without touching the game's main thread, so it works even during a freeze.",
                "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");

            sb.Append(',');
            AppendTool(sb, "game_eval",
                "Compile and run a C# snippet inside the running game, then unload it. The snippet is "
                + "the body of a method with `host` (IHostApi), `mod` (HeartopiaComplete) and `args` "
                + "in scope. PREFER `host` — it is the curated surface (IsWorldReady, WorldEpoch, "
                + "TryGetPlayerPosition, Log, StartCoroutine); `mod` exposes only the mod's PUBLIC "
                + "members, so much of its internals is not reachable from here. "
                + "`return <anything>;` and its ToString() comes back. Compiled against the "
                + "exact assemblies this session loaded, so compile errors are returned here with "
                + "snippet line numbers instead of costing a game restart. Requires unsafe ops to be "
                + "enabled in the mod. Runs on the main thread — keep it short and non-blocking.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"code\":{\"type\":\"string\",\"description\":\"C# statements, e.g. return mod.IsWorldReady;\"},"
                + "\"usings\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"extra namespaces\"},"
                + "\"args\":{\"type\":\"string\",\"description\":\"raw string handed to the snippet as `args`\"},"
                + "\"compileOnly\":{\"type\":\"boolean\",\"description\":\"check that it compiles without running it\"}},"
                + "\"required\":[\"code\"],\"additionalProperties\":false}");

            sb.Append(',');
            AppendTool(sb, "game_rpc",
                "Escape hatch: call any op by name, including ops this tool list did not carry "
                + "(e.g. when the game was unreachable at listing time). `rpc.describe` returns the "
                + "full catalogue.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"op\":{\"type\":\"string\",\"description\":\"op name, e.g. 'status' or 'entities.list'\"},"
                + "\"args\":{\"type\":\"object\",\"description\":\"op arguments\"}},"
                + "\"required\":[\"op\"],\"additionalProperties\":false}");

            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendTool(StringBuilder sb, string name, string description, string schema)
        {
            sb.Append("{\"name\":").Append(Quote(name));
            sb.Append(",\"description\":").Append(Quote(description));
            sb.Append(",\"inputSchema\":").Append(schema);
            sb.Append('}');
        }

        private static string ToolsCallResult(JsonElement parameters)
        {
            string name = GetString(parameters, "name");
            JsonElement arguments = default;
            bool hasArgs = parameters.ValueKind == JsonValueKind.Object
                           && parameters.TryGetProperty("arguments", out arguments);

            string op;
            string argsJson = "{}";

            // Registry-backed tools resolve through the map built by the last tools/list. A client
            // that calls a tool it was told about therefore reaches the right op even if this bridge
            // has never heard of it.
            string mapped;
            lock (ToolToOp)
            {
                ToolToOp.TryGetValue(name ?? string.Empty, out mapped);
            }

            if (mapped != null)
            {
                if (hasArgs && arguments.ValueKind == JsonValueKind.Object)
                {
                    argsJson = arguments.GetRawText();
                }

                GameLink.Result mappedResult = GameLink.Call(mapped, argsJson);
                return mappedResult.Ok ? RenderPayload(mappedResult.Payload) : ToolError(mappedResult.Error);
            }

            if (string.Equals(name, "game_eval", StringComparison.Ordinal))
            {
                if (!hasArgs || arguments.ValueKind != JsonValueKind.Object)
                {
                    return ToolError("game_eval needs a 'code' argument.");
                }

                return EvalResult(arguments);
            }

            switch (name)
            {
                case "game_status":
                    op = "status";
                    break;

                case "game_ping":
                    op = "ping";
                    break;

                case "game_log_tail":
                    op = "log.tail";
                    if (hasArgs && arguments.ValueKind == JsonValueKind.Object)
                    {
                        argsJson = arguments.GetRawText();
                    }

                    break;

                case "game_rpc":
                    if (!hasArgs || arguments.ValueKind != JsonValueKind.Object)
                    {
                        return ToolError("game_rpc needs an 'op' argument.");
                    }

                    op = GetString(arguments, "op");
                    if (string.IsNullOrEmpty(op))
                    {
                        return ToolError("game_rpc needs an 'op' argument.");
                    }

                    if (arguments.TryGetProperty("args", out JsonElement inner)
                        && inner.ValueKind == JsonValueKind.Object)
                    {
                        argsJson = inner.GetRawText();
                    }

                    break;

                default:
                    return ToolError("unknown tool: " + name);
            }

            GameLink.Result result = GameLink.Call(op, argsJson);
            if (!result.Ok)
            {
                return ToolError(result.Error);
            }

            return RenderPayload(result.Payload);
        }

        // An op that returns an encoded image becomes an MCP `image` content block, not a wall of
        // base64 text — the whole point of the screenshot op is that the model SEES the frame. The
        // metadata still rides along as text, so timings and the capture site stay visible.
        private static string RenderPayload(string payload)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(payload);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("base64", out JsonElement b64)
                    && b64.ValueKind == JsonValueKind.String
                    && root.TryGetProperty("format", out JsonElement fmt)
                    && fmt.ValueKind == JsonValueKind.String)
                {
                    string mime = "image/" + fmt.GetString();
                    StringBuilder meta = new StringBuilder();
                    foreach (JsonProperty p in root.EnumerateObject())
                    {
                        if (string.Equals(p.Name, "base64", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (meta.Length > 0)
                        {
                            meta.Append(", ");
                        }

                        meta.Append(p.Name).Append('=').Append(p.Value.ToString());
                    }

                    StringBuilder sb = new StringBuilder();
                    sb.Append("{\"content\":[{\"type\":\"image\",\"data\":").Append(Quote(b64.GetString()));
                    sb.Append(",\"mimeType\":").Append(Quote(mime)).Append('}');
                    sb.Append(",{\"type\":\"text\",\"text\":").Append(Quote(meta.ToString())).Append("}]}");
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                Log("payload render fell back to text: " + ex.Message);
            }

            return ToolText(Pretty(payload));
        }

        // ── game_eval ────────────────────────────────────────────────────────────────────────────
        // Compile here, run there. The game side needs nothing new: an eval IS a sandbox plugin with
        // a lifetime of one call, so this composes plugin.load / plugin.call / plugin.unload.
        private const string EvalPluginId = "__eval";
        private static EnvPaths cachedEnv;

        private static string EvalResult(JsonElement arguments)
        {
            string code = GetString(arguments, "code");
            if (string.IsNullOrWhiteSpace(code))
            {
                return ToolError("game_eval needs a non-empty 'code'.");
            }

            string[] usings = null;
            if (arguments.TryGetProperty("usings", out JsonElement u) && u.ValueKind == JsonValueKind.Array)
            {
                List<string> list = new List<string>();
                foreach (JsonElement item in u.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        list.Add(item.GetString());
                    }
                }

                usings = list.ToArray();
            }

            bool compileOnly = arguments.TryGetProperty("compileOnly", out JsonElement co)
                               && co.ValueKind == JsonValueKind.True;
            string snippetArgs = GetString(arguments, "args") ?? string.Empty;

            if (!TryGetEnv(out EnvPaths paths, out string envError))
            {
                return ToolError(envError);
            }

            EvalCompiler.Result compiled;
            try
            {
                compiled = EvalCompiler.Compile(code, usings, paths);
            }
            catch (Exception ex)
            {
                return ToolError("compiler failed: " + ex);
            }

            if (!compiled.Success)
            {
                // Compile errors are a RESULT, not a tool failure: the caller fixes them and retries
                // without ever touching the game. That round trip is the whole point.
                StringBuilder sb = new StringBuilder();
                sb.Append("Compilation failed (").Append(compiled.Diagnostics.Count)
                  .Append(" diagnostic(s), ").Append(Math.Round(compiled.CompileMs)).Append(" ms, ")
                  .Append(compiled.ReferenceCount).AppendLine(" references):");
                foreach (string d in compiled.Diagnostics)
                {
                    sb.Append("  ").AppendLine(d);
                }

                return ToolError(sb.ToString());
            }

            StringBuilder report = new StringBuilder();
            report.Append("compiled in ").Append(Math.Round(compiled.CompileMs)).Append(" ms against ")
                  .Append(compiled.ReferenceCount).Append(" references; ")
                  .Append(compiled.Dll.Length).Append(" bytes");
            foreach (string d in compiled.Diagnostics)
            {
                report.AppendLine().Append("  ").Append(d);
            }

            if (compileOnly)
            {
                return ToolText(report.ToString());
            }

            string dllB64 = Convert.ToBase64String(compiled.Dll);
            string pdbB64 = Convert.ToBase64String(compiled.Pdb);

            GameLink.Result load = GameLink.Call("plugin.load",
                "{\"id\":" + Quote(EvalPluginId) + ",\"dll\":" + Quote(dllB64)
                + ",\"pdb\":" + Quote(pdbB64) + ",\"replace\":true}");
            if (!load.Ok)
            {
                return ToolError(report + "\n\nload failed: " + load.Error);
            }

            if (!IsTrue(load.Payload, "loaded"))
            {
                return ToolError(report + "\n\nload rejected: " + Pretty(load.Payload));
            }

            GameLink.Result call = GameLink.Call("plugin.call",
                "{\"id\":" + Quote(EvalPluginId) + ",\"method\":\"run\",\"args\":" + Quote(snippetArgs) + "}");

            // Unload unconditionally: a snippet that threw must not be left resident, ticking, and
            // pinning its context until someone notices.
            GameLink.Result unload = GameLink.Call("plugin.unload", "{\"id\":" + Quote(EvalPluginId) + "}");
            if (!unload.Ok)
            {
                Log("eval cleanup failed: " + unload.Error);
            }

            if (!call.Ok)
            {
                return ToolError(report + "\n\nran, but threw: " + call.Error);
            }

            string value = ExtractResult(call.Payload);
            return ToolText(report + "\n\nresult: " + value);
        }

        private static bool TryGetEnv(out EnvPaths paths, out string error)
        {
            error = null;
            if (cachedEnv != null)
            {
                paths = cachedEnv;
                return true;
            }

            paths = null;
            GameLink.Result env = GameLink.Call("env", "{}");
            if (!env.Ok)
            {
                error = "cannot resolve assembly paths — " + env.Error;
                return false;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(env.Payload);
                JsonElement root = doc.RootElement;
                paths = new EnvPaths
                {
                    ModAssembly = GetString(root, "modAssembly"),
                    InteropDir = GetString(root, "interopDir"),
                    CoreDir = GetString(root, "coreDir"),
                    RuntimeDir = GetString(root, "runtimeDir"),
                };
            }
            catch (Exception ex)
            {
                error = "env payload unreadable: " + ex.Message;
                return false;
            }

            if (string.IsNullOrEmpty(paths.ModAssembly))
            {
                error = "the game did not report its mod assembly path";
                return false;
            }

            cachedEnv = paths;
            Log("eval references resolved from " + paths.ModAssembly);
            return true;
        }

        private static bool IsTrue(string json, string property)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty(property, out JsonElement v)
                       && v.ValueKind == JsonValueKind.True;
            }
            catch
            {
                return false;
            }
        }

        private static string ExtractResult(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out JsonElement v))
                {
                    return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
                }
            }
            catch
            {
            }

            return Pretty(json);
        }

        private static string ToolText(string text)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"content\":[{\"type\":\"text\",\"text\":").Append(Quote(CrashBanner() + text)).Append("}]}");
            return sb.ToString();
        }

        // Prepended ONCE per connection. An uncatchable native crash leaves no exception and no log
        // line, so without this an agent sees only "the call failed", retries the exact thing that
        // killed the game, and does it again.
        private static string CrashBanner()
        {
            if (!GameLink.JustConnected)
            {
                return string.Empty;
            }

            GameLink.JustConnected = false;

            GameLink.Result report = GameLink.Call("session.report", "{}");
            if (!report.Ok)
            {
                return string.Empty;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(report.Payload);
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("previousSessionCrashed", out JsonElement crashed)
                    || crashed.ValueKind != JsonValueKind.True)
                {
                    return string.Empty;
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("⚠ THE PREVIOUS GAME SESSION DIED MID-OPERATION.");
                if (root.TryGetProperty("previousOp", out JsonElement prev))
                {
                    sb.Append("It was running: ").AppendLine(prev.GetRawText());
                }

                if (root.TryGetProperty("quarantined", out JsonElement q)
                    && q.ValueKind == JsonValueKind.Array && q.GetArrayLength() > 0)
                {
                    sb.AppendLine("Those bytes are now quarantined; plugin.load refuses them unless force:true.");
                }

                if (root.TryGetProperty("recentDumps", out JsonElement dumps)
                    && dumps.ValueKind == JsonValueKind.Array && dumps.GetArrayLength() > 0)
                {
                    sb.Append("Newest crash dump: ").AppendLine(dumps[0].GetString());
                }

                sb.AppendLine("---");
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ToolError(string message)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"isError\":true,\"content\":[{\"type\":\"text\",\"text\":")
              .Append(Quote(message)).Append("}]}");
            return sb.ToString();
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // JSON-RPC plumbing
        // ────────────────────────────────────────────────────────────────────────────────────────

        private static void Respond(string idJson, string resultJson)
        {
            if (idJson == null)
            {
                return;
            }

            outWriter.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":" + idJson + ",\"result\":" + resultJson + "}");
        }

        private static void RespondError(string idJson, int code, string message)
        {
            if (idJson == null)
            {
                return;
            }

            outWriter.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":" + idJson
                                + ",\"error\":{\"code\":" + code.ToString(CultureInfo.InvariantCulture)
                                + ",\"message\":" + Quote(message) + "}}");
        }

        internal static void Log(string message)
        {
            try
            {
                Console.Error.WriteLine("[bugtopia-mcp] " + message);
            }
            catch
            {
            }
        }

        private static string GetString(JsonElement element, string name)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return null;
        }

        private static string Pretty(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
                {
                    WriteIndented = true,
                });
            }
            catch
            {
                return json;
            }
        }

        internal static string Quote(string value)
        {
            if (value == null)
            {
                return "null";
            }

            StringBuilder sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }

    // ============================================================================================
    // The link to the running game. Lazy, self-healing: every call connects if needed, and any IO
    // failure drops the connection so the next call retries from scratch. A game restart therefore
    // needs no bridge restart.
    // ============================================================================================
    internal static class GameLink
    {
        private const int RpcProtocolVersion = 1;
        private const int ConnectTimeoutMs = 2000;
        private const int ReadTimeoutMs = 15000;

        private static readonly object Gate = new object();
        private static TcpClient client;
        private static StreamReader reader;
        private static StreamWriter writer;
        private static long nextId = 1;
        private static string modVersion = "?";

        // Set on every fresh connection. A reconnect is the ONLY hint an agent gets that the game
        // restarted — and if it restarted because something killed it, that fact must reach the
        // agent before it repeats whatever it was doing.
        internal static bool JustConnected;

        internal struct Result
        {
            internal bool Ok;
            internal string Payload;
            internal string Error;
        }

        internal static string EndpointPath()
        {
            string overridePath = Environment.GetEnvironmentVariable("BUGTOPIA_MCP_ENDPOINT");
            if (!string.IsNullOrEmpty(overridePath))
            {
                return overridePath;
            }

            // %LocalLow% has no SpecialFolder; derive it the same way the mod's HelperPaths does.
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Directory.GetParent(localAppData)?.FullName ?? localAppData;
            return Path.Combine(appData, "LocalLow", "Bugtopia", "McpBridge", "endpoint.json");
        }

        internal static Result Call(string op, string argsJson)
        {
            lock (Gate)
            {
                if (!EnsureConnected(out string connectError))
                {
                    return new Result { Ok = false, Error = connectError };
                }

                try
                {
                    long id = nextId++;
                    writer.WriteLine("{\"i\":" + id.ToString(CultureInfo.InvariantCulture)
                                     + ",\"op\":" + Program.Quote(op)
                                     + ",\"a\":" + (string.IsNullOrEmpty(argsJson) ? "{}" : argsJson) + "}");

                    string line = reader.ReadLine();
                    if (line == null)
                    {
                        Drop();
                        return new Result { Ok = false, Error = "the game closed the connection mid-call (crash?)." };
                    }

                    return Interpret(line, op);
                }
                catch (Exception ex)
                {
                    Drop();
                    return new Result
                    {
                        Ok = false,
                        Error = "link failed during '" + op + "': " + ex.Message,
                    };
                }
            }
        }

        private static Result Interpret(string line, string op)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True)
            {
                string payload = root.TryGetProperty("r", out JsonElement r) ? r.GetRawText() : "{}";
                return new Result { Ok = true, Payload = payload };
            }

            string code = "internal";
            string message = "unknown error";
            if (root.TryGetProperty("e", out JsonElement e) && e.ValueKind == JsonValueKind.Object)
            {
                if (e.TryGetProperty("code", out JsonElement c) && c.ValueKind == JsonValueKind.String)
                {
                    code = c.GetString();
                }

                if (e.TryGetProperty("msg", out JsonElement m) && m.ValueKind == JsonValueKind.String)
                {
                    message = m.GetString();
                }
            }

            return new Result { Ok = false, Error = Explain(code, message, op) };
        }

        // Turn a wire code into something the agent can act on rather than guess at.
        private static string Explain(string code, string message, string op)
        {
            switch (code)
            {
                case "world_not_ready":
                    return "'" + op + "' needs a loaded world; the game is on the login screen or a "
                           + "loading screen right now. (" + message + ")";
                case "writes_disabled":
                    return "'" + op + "' is a write op and writes are OFF for this session. "
                           + "Enable them in the mod before retrying. (" + message + ")";
                case "unsafe_disabled":
                    return "'" + op + "' is an unsafe op and unsafe ops are OFF for this session. ("
                           + message + ")";
                case "unknown_op":
                    return "this build has no op named '" + op + "'. Run game_status and read the "
                           + "`describe` payload for the op catalogue. (" + message + ")";
                case "timeout":
                    return "the game did not answer within the main-thread deadline — it is loading, "
                           + "stalled, or paused. (" + message + ")";
                case "busy":
                    return "the game's main-thread queue is full; retry shortly. (" + message + ")";
                default:
                    return code + ": " + message;
            }
        }

        private static bool EnsureConnected(out string error)
        {
            error = null;
            if (client != null && client.Connected)
            {
                return true;
            }

            Drop();

            string path = EndpointPath();
            if (!File.Exists(path))
            {
                // Three distinct causes produce this one symptom, and two of them are silent on the
                // game side by design (no marker ⇒ nothing logs; no FEATURE_MCP ⇒ the code is not
                // even in the binary). Name all three, or the next person debugs it blind.
                error = "no endpoint file at " + path + " — the bridge is not up in the game. Causes,"
                        + " in order of likelihood:\n"
                        + "  1. bugtopia.dll was built WITHOUT -p:Mcp=true. Note that build-all.bat"
                        + " produces the Universal flavour, which cannot carry the bridge and"
                        + " auto-deploys over BepInEx\\plugins — run buddy\\build-mcp.bat instead.\n"
                        + "  2. the marker file %LocalLow%\\Bugtopia\\mcp is missing (it is read once"
                        + " at startup and the mod never creates it).\n"
                        + "  3. the game is not running.";
                return false;
            }

            int port;
            string token;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                port = doc.RootElement.GetProperty("port").GetInt32();
                token = doc.RootElement.GetProperty("token").GetString();
            }
            catch (Exception ex)
            {
                error = "endpoint file is unreadable (" + ex.Message + "): " + path;
                return false;
            }

            try
            {
                TcpClient attempt = new TcpClient();
                if (!attempt.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs))
                {
                    attempt.Dispose();
                    error = "no answer on 127.0.0.1:" + port + " — the endpoint file is stale, so the "
                            + "game is probably not running any more.";
                    return false;
                }

                attempt.NoDelay = true;
                NetworkStream stream = attempt.GetStream();
                stream.ReadTimeout = ReadTimeoutMs;
                stream.WriteTimeout = ReadTimeoutMs;

                UTF8Encoding utf8 = new UTF8Encoding(false);
                StreamReader r = new StreamReader(stream, utf8);
                StreamWriter w = new StreamWriter(stream, utf8) { AutoFlush = true, NewLine = "\n" };

                w.WriteLine("{\"i\":0,\"op\":\"hello\",\"a\":{\"proto\":"
                            + RpcProtocolVersion.ToString(CultureInfo.InvariantCulture)
                            + ",\"token\":" + Program.Quote(token) + "}}");

                string response = r.ReadLine();
                if (response == null)
                {
                    attempt.Dispose();
                    error = "the game rejected the handshake (stale token — restart the bridge after "
                            + "restarting the game, so it re-reads endpoint.json).";
                    return false;
                }

                using JsonDocument doc = JsonDocument.Parse(response);
                if (!doc.RootElement.TryGetProperty("ok", out JsonElement ok)
                    || ok.ValueKind != JsonValueKind.True)
                {
                    attempt.Dispose();
                    error = "handshake refused: " + response;
                    return false;
                }

                if (doc.RootElement.TryGetProperty("r", out JsonElement result)
                    && result.TryGetProperty("version", out JsonElement v)
                    && v.ValueKind == JsonValueKind.String)
                {
                    modVersion = v.GetString();
                }

                client = attempt;
                reader = r;
                writer = w;
                JustConnected = true;
                Program.Log("connected to game on 127.0.0.1:" + port + " (bugtopia " + modVersion + ")");
                return true;
            }
            catch (Exception ex)
            {
                Drop();
                error = "could not connect on 127.0.0.1:" + port + ": " + ex.Message;
                return false;
            }
        }

        private static void Drop()
        {
            try { reader?.Dispose(); } catch { }
            try { writer?.Dispose(); } catch { }
            try { client?.Dispose(); } catch { }
            reader = null;
            writer = null;
            client = null;
        }

        internal static void Close()
        {
            lock (Gate)
            {
                Drop();
            }
        }
    }
}
