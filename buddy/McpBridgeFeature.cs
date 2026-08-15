#if FEATURE_MCP
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace HeartopiaMod
{
    // ============================================================================================
    // MCP BRIDGE — game side (phase 1: transport + pump + rpc.describe/status/log.tail).
    //
    // WHAT THIS IS: a loopback NDJSON socket that lets an external process (tools/BugtopiaMcp, the
    // stdio MCP server Claude Code spawns) read the running game. It is deliberately NOT an MCP
    // server: MCP protocol churn lives in that external process, where a change costs 3 s instead
    // of a game restart + relogin.
    //
    // ── THE ONE INVARIANT ────────────────────────────────────────────────────────────────────────
    // NO GAME ACCESS ON THE SOCKET THREAD. EVER.
    // Socket threads parse frames and enqueue; every handler that touches Unity, il2cpp or AuraMono
    // runs on the Unity main thread from Drain(). Calling into il2cpp/embedded Mono off-thread needs
    // a thread attach and puts SGen in reach of a thread CoreCLR does not coordinate with — the
    // exact crash family documented in AGENTS.md §11. Even ModLogger is off-limits here: the
    // BepInEx sink writes to a shared StreamWriter, so worker messages go through LogFromWorker()
    // and are drained on the main thread.
    //
    // ── THE GATE ─────────────────────────────────────────────────────────────────────────────────
    // The bridge exists this session if and only if `%LocalLow%/Bugtopia/mcp` is present at
    // startup. Read ONCE (next to RefreshBetaFlag), never re-read, and THE MOD NEVER CREATES IT on
    // any code path. Marker absent ⇒ no listener, no bound port, no op registry, no per-frame cost
    // beyond one bool test. Deleting it mid-session does not stop the session — that is the same
    // deliberate semantics as the beta marker (HeartopiaComplete.Beta.cs).
    // ============================================================================================
    internal static class McpBridge
    {
        // Bumped when the wire contract changes shape. The bridge refuses a version it does not know.
        internal const int RpcProtocolVersion = 1;

        // Extension ignored, exactly like the beta marker: Explorer hides known extensions, so a
        // file created as "mcp" often lands as "mcp.txt". "mcp", "mcp.txt", "mcp.on" all count;
        // "mcpanything" does not.
        private const string MarkerBaseName = "mcp";

        private const int FirstPort = 8770;
        private const int PortAttempts = 5;
        // Raised for screenshots (phase 3): a 1600-wide JPEG at quality 70 is ~200-500 KB, and base64
        // inflates it by a third. 8 MiB leaves headroom for a 4K capture without letting a malformed
        // client stream unbounded data at the parser.
        private const int MaxFrameBytes = 8 << 20;
        private const int CallTimeoutMs = 5000;
        // Depth guard: if the main thread is not draining (loading screen, freeze), reject instead
        // of building a backlog the agent will never see answered.
        private const int MaxQueueDepth = 64;
        private const int MaxClients = 4;
        private const double DrainBudgetMs = 3.0;

        internal static bool MarkerPresent;
        internal static bool Listening;
        internal static int Port;
        internal static string Status = "not started";
        internal static long OpsServed;
        internal static DateTime StartedUtc;

        // Privilege tiers. The marker authorises the CHANNEL; these authorise what it may do, and
        // both stay off until SetPrivileges turns them on for the session (no config key, no
        // persistence — a privilege that survives a restart is one nobody remembers granting).
        internal static bool AllowWrites = false;
        internal static bool AllowUnsafe = false;

        internal static void SetPrivileges(bool allowWrites, bool allowUnsafe)
        {
            AllowWrites = allowWrites;
            AllowUnsafe = allowUnsafe;
            ModLogger.Msg("[Mcp] privileges: writes=" + (allowWrites ? "ON" : "off")
                          + " unsafe=" + (allowUnsafe ? "ON" : "off"));
        }

        // Updated by Drain() on the main thread, read by socket threads for the response envelope.
        // A torn/stale read is harmless — it is diagnostic metadata, not control flow.
        internal static int LastFrame;

        private static string token;
        private static TcpListener listener;
        private static Thread acceptThread;
        private static volatile bool stopRequested;
        private static int clientCount;
        private static int queueDepth;

        private static readonly ConcurrentQueue<McpCall> Pending = new ConcurrentQueue<McpCall>();
        // Calls parked by a handler that needs a later frame (McpOps.Defer). Folded back into
        // Pending at the START of the next Drain, which is what bounds them to one attempt per frame.
        private static readonly ConcurrentQueue<McpCall> Deferred = new ConcurrentQueue<McpCall>();
        private static readonly ConcurrentQueue<string> WorkerLog = new ConcurrentQueue<string>();
        private static readonly Stopwatch DrainClock = new Stopwatch();

        internal sealed class McpCall
        {
            internal long Id;
            internal string Op;
            internal McpOps.OpEntry Entry;
            internal Dictionary<string, object> Args;
            internal readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);

            internal string ResultJson;
            internal string ErrorCode;
            internal string ErrorMessage;
            internal double Ms;

            // Set by the socket thread when it gives up waiting. The pump checks it before running
            // the handler, so an abandoned call costs nothing and (once write ops land) cannot
            // mutate the game after the requester is gone.
            internal volatile bool Abandoned;
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // Gate
        // ────────────────────────────────────────────────────────────────────────────────────────

        // One-shot startup read. Fail-closed: any IO problem leaves the bridge disabled.
        // Creates NOTHING — note the deliberate absence of HelperPaths.GetFile/GetDirectory here,
        // both of which call EnsureDirectory (same discipline as MonoAssemblyDump's opt-in folder).
        internal static void CheckMarker()
        {
            bool found = false;
            string root = null;
            try
            {
                root = HelperPaths.Root;
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    foreach (string path in Directory.EnumerateFiles(root, MarkerBaseName + "*"))
                    {
                        if (string.Equals(Path.GetFileNameWithoutExtension(path), MarkerBaseName,
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
                ModLogger.Msg("[Mcp] marker check failed (staying disabled): " + ex.Message);
                found = false;
            }

            MarkerPresent = found;
            Status = found ? "marker found, not started" : "disabled (no marker)";
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ────────────────────────────────────────────────────────────────────────────────────────

        internal static bool Start()
        {
            if (!MarkerPresent)
            {
                return false;
            }

            if (Listening)
            {
                return true;
            }

            try
            {
                token = NewToken();
                stopRequested = false;

                if (!TryBind())
                {
                    Status = "no free port in " + FirstPort + ".." + (FirstPort + PortAttempts - 1);
                    ModLogger.Warning("[Mcp] " + Status);
                    return false;
                }

                WriteEndpointFile();

                acceptThread = new Thread(AcceptLoop)
                {
                    IsBackground = true,
                    Name = "bugtopia-mcp-accept",
                };
                acceptThread.Start();

                Listening = true;
                StartedUtc = DateTime.UtcNow;
                Status = "listening on 127.0.0.1:" + Port;
                ModLogger.Msg("[Mcp] " + Status + " — " + McpOps.Count + " ops registered; endpoint written to "
                              + Path.Combine("McpBridge", "endpoint.json") + ".");
                return true;
            }
            catch (Exception ex)
            {
                Status = "start failed: " + ex.Message;
                ModLogger.Warning("[Mcp] " + Status);
                SafeStopListener();
                return false;
            }
        }

        internal static void Stop()
        {
            if (!Listening && listener == null)
            {
                return;
            }

            stopRequested = true;
            SafeStopListener();
            Listening = false;
            Status = "stopped";
            DeleteEndpointFile();

            // Anything still waiting gets a definitive answer instead of a 5 s hang.
            while (Pending.TryDequeue(out McpCall call))
            {
                Interlocked.Decrement(ref queueDepth);
                Fail(call, "internal", "bridge stopped");
            }

            ModLogger.Msg("[Mcp] stopped.");
        }

        private static bool TryBind()
        {
            for (int i = 0; i < PortAttempts; i++)
            {
                int candidate = FirstPort + i;
                TcpListener attempt = null;
                try
                {
                    // Loopback only, IPv4 only. The bridge connects to 127.0.0.1 by construction,
                    // and a narrower bind is a narrower attack surface for what is, by design, a
                    // remote-code-execution channel into the game process.
                    attempt = new TcpListener(IPAddress.Loopback, candidate);
                    attempt.ExclusiveAddressUse = true;
                    attempt.Start();
                    listener = attempt;
                    Port = candidate;
                    return true;
                }
                catch (SocketException)
                {
                    try { attempt?.Stop(); } catch { }
                }
            }

            return false;
        }

        private static void SafeStopListener()
        {
            try { listener?.Stop(); } catch { }
            listener = null;
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // Socket threads
        // ────────────────────────────────────────────────────────────────────────────────────────

        private static void AcceptLoop()
        {
            while (!stopRequested)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    // listener.Stop() from Stop() unblocks the accept by throwing — that is the exit.
                    break;
                }

                if (Interlocked.Increment(ref clientCount) > MaxClients)
                {
                    Interlocked.Decrement(ref clientCount);
                    try { client.Close(); } catch { }
                    continue;
                }

                Thread worker = new Thread(() => ClientLoop(client))
                {
                    IsBackground = true,
                    Name = "bugtopia-mcp-client",
                };
                worker.Start();
            }

            LogFromWorker("[Mcp] accept loop exited.");
        }

        private static void ClientLoop(TcpClient client)
        {
            try
            {
                client.NoDelay = true;
                using (NetworkStream stream = client.GetStream())
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))
                {
                    AutoFlush = true,
                    NewLine = "\n",
                })
                {
                    stream.WriteTimeout = 10000;
                    bool authenticated = false;
                    LineReader reader = new LineReader(stream, MaxFrameBytes);

                    while (!stopRequested)
                    {
                        string line = reader.ReadLine(out bool overflow, out bool closed);
                        if (closed)
                        {
                            break;
                        }

                        if (overflow)
                        {
                            // No reliable resync point mid-frame; answer and drop the connection.
                            writer.WriteLine("{\"ok\":false,\"e\":{\"code\":\"bad_args\",\"msg\":\"frame exceeds "
                                             + MaxFrameBytes + " bytes\"}}");
                            break;
                        }

                        if (line == null || line.Length == 0)
                        {
                            continue;
                        }

                        string response = HandleFrame(line, ref authenticated);
                        if (response == null)
                        {
                            break; // handshake rejected
                        }

                        writer.WriteLine(response);
                    }
                }
            }
            catch (Exception)
            {
                // A dropped connection is routine (the agent restarts, the bridge exits). Nothing
                // to log — and logging is not allowed from here anyway.
            }
            finally
            {
                Interlocked.Decrement(ref clientCount);
                try { client.Close(); } catch { }
            }
        }

        // Returns the response line, or null to close the connection.
        private static string HandleFrame(string line, ref bool authenticated)
        {
            long id = -1;
            try
            {
                Dictionary<string, object> frame = McpJsonParser.ParseObject(line);
                id = (long)McpArgs.GetInt(frame, "i", -1);
                string op = McpArgs.GetString(frame, "op");
                Dictionary<string, object> args = null;
                if (frame.TryGetValue("a", out object rawArgs) && rawArgs is Dictionary<string, object> map)
                {
                    args = map;
                }

                if (!authenticated)
                {
                    // The first frame must be the handshake; anything else is a stray client.
                    if (!string.Equals(op, "hello", StringComparison.Ordinal))
                    {
                        Thread.Sleep(1000);
                        return null;
                    }

                    int proto = McpArgs.GetInt(args, "proto", 0);
                    if (proto != RpcProtocolVersion)
                    {
                        return ErrorLine(id, "bad_args",
                            "protocol " + proto + " not supported (this build speaks " + RpcProtocolVersion + ")");
                    }

                    if (!TokenMatches(McpArgs.GetString(args, "token")))
                    {
                        // Delay before closing so a wrong token is not a fast oracle.
                        Thread.Sleep(1000);
                        return null;
                    }

                    authenticated = true;
                    return OkLine(id, HelloJson(), 0d);
                }

                if (string.IsNullOrEmpty(op))
                {
                    return ErrorLine(id, "bad_args", "missing 'op'");
                }

                // Ops answerable without touching the game are served right here, so a frozen or
                // loading game still responds to discovery and liveness.
                if (string.Equals(op, "rpc.describe", StringComparison.Ordinal))
                {
                    return OkLine(id, McpOps.DescribeJson(), 0d);
                }

                if (string.Equals(op, "ping", StringComparison.Ordinal))
                {
                    return OkLine(id, "{\"pong\":true}", 0d);
                }

                if (!McpOps.TryGet(op, out McpOps.OpEntry entry))
                {
                    return ErrorLine(id, "unknown_op", "no op named '" + op + "'");
                }

                // UNSAFE IMPLIES WRITE. The tiers are a ladder, not independent switches: an unsafe
                // op runs arbitrary code, and arbitrary code writes whatever it likes — so demanding
                // both for `plugin.load` was false granularity that only produced a confusing
                // refusal for anyone who granted the scarier privilege alone.
                if ((entry.Flags & McpOpFlags.Write) != 0 && !AllowWrites && !AllowUnsafe)
                {
                    return ErrorLine(id, "writes_disabled",
                        "write ops are off for this session (enabling unsafe would also cover this)");
                }

                if ((entry.Flags & McpOpFlags.Unsafe) != 0 && !AllowUnsafe)
                {
                    return ErrorLine(id, "unsafe_disabled", "unsafe ops are off for this session");
                }

                if (Interlocked.Increment(ref queueDepth) > MaxQueueDepth)
                {
                    Interlocked.Decrement(ref queueDepth);
                    return ErrorLine(id, "busy", "main-thread queue is full (game loading or stalled?)");
                }

                McpCall call = new McpCall
                {
                    Id = id,
                    Op = op,
                    Entry = entry,
                    Args = args,
                };

                Pending.Enqueue(call);

                if (!call.Done.Wait(CallTimeoutMs))
                {
                    call.Abandoned = true;
                    return ErrorLine(id, "timeout",
                        "no main-thread tick answered within " + CallTimeoutMs + " ms");
                }

                if (call.ErrorCode != null)
                {
                    return ErrorLine(id, call.ErrorCode, call.ErrorMessage);
                }

                return OkLine(id, call.ResultJson, call.Ms);
            }
            catch (McpJsonException ex)
            {
                return ErrorLine(id, "bad_args", ex.Message);
            }
            catch (Exception ex)
            {
                return ErrorLine(id, "internal", ex.Message);
            }
        }

        private static string HelloJson()
        {
            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Str("mod", "Bugtopia");
            w.Str("version", ModBuildVersion.Display);
            w.Str("loader", ModLoaderInfo.IsMelonLoader ? "MelonLoader" : "BepInEx");
            w.Num("protocol", RpcProtocolVersion);
            w.Num("pid", GetCurrentProcessIdSafe());
            w.Bool("allowWrites", AllowWrites);
            w.Bool("allowUnsafe", AllowUnsafe);
            w.Raw("describe", McpOps.DescribeJson());
            w.EndObject();
            return w.ToString();
        }

        private static string OkLine(long id, string resultJson, double ms)
        {
            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Num("i", id);
            w.Bool("ok", true);
            w.Raw("r", resultJson);
            w.Num("ms", ms);
            w.Num("frame", LastFrame);
            w.EndObject();
            return w.ToString();
        }

        private static string ErrorLine(long id, string code, string message)
        {
            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Num("i", id);
            w.Bool("ok", false);
            w.BeginObject("e");
            w.Str("code", code);
            w.Str("msg", message ?? string.Empty);
            w.EndObject();
            w.EndObject();
            return w.ToString();
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // Main thread
        // ────────────────────────────────────────────────────────────────────────────────────────

        // Called once per frame from HeartopiaComplete.OnUpdate, inside a FeatureBreakerState.
        internal static void Drain(int frameCount)
        {
            LastFrame = frameCount;

            while (WorkerLog.TryDequeue(out string line))
            {
                ModLogger.Msg(line);
            }

            // Yesterday's parked calls become today's work. Done before the budget clock starts so a
            // deferred call is never starved by fresh arrivals.
            while (Deferred.TryDequeue(out McpCall parked))
            {
                Pending.Enqueue(parked);
            }

            if (Pending.IsEmpty)
            {
                return;
            }

            bool heavyRan = false;
            DrainClock.Restart();

            while (DrainClock.Elapsed.TotalMilliseconds < DrainBudgetMs && Pending.TryDequeue(out McpCall call))
            {
                Interlocked.Decrement(ref queueDepth);

                if (call.Abandoned)
                {
                    continue;
                }

                if (call.Entry.Cost == McpOpCost.Heavy)
                {
                    if (heavyRan)
                    {
                        // Put it back rather than blowing the frame budget; it runs next frame,
                        // well inside the 5 s call timeout.
                        Interlocked.Increment(ref queueDepth);
                        Pending.Enqueue(call);
                        break;
                    }

                    heavyRan = true;
                }

                Execute(call);
            }

            DrainClock.Stop();
        }

        private static void Execute(McpCall call)
        {
            // One 64-byte marker per op. If an op kills the process, the crash dump names it.
            Breadcrumbs.Phase("mcp." + call.Op);

            // Ops that can actually take the process down also leave a record ON DISK, because an
            // uncatchable native AV produces no log line, no stack and no exception — the process is
            // just gone. A non-empty lastop.json at next startup is then proof of what killed it.
            // Cheap read ops are excluded: they cannot crash anything and an agent polls them, so a
            // file write per call would be pure tax (Breadcrumbs already covers them).
            bool dangerous = (call.Entry.Flags & (McpOpFlags.Write | McpOpFlags.Unsafe)) != 0;
            if (dangerous)
            {
                McpForensics.Arm(call.Op, McpArgs.GetString(call.Args, "id"), null, LastFrame,
                    IsWorldReadyForOps());
            }

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                if ((call.Entry.Flags & McpOpFlags.NeedsWorld) != 0 && !IsWorldReadyForOps())
                {
                    Fail(call, "world_not_ready", "the world-ready gate is closed");
                    return;
                }

                string result = call.Entry.Handler(call.Args);
                sw.Stop();

                if (ReferenceEquals(result, McpOps.Defer))
                {
                    // Parked, not answered. It goes on the DEFERRED queue rather than straight back
                    // onto Pending: re-queueing in place would let this same Drain pass dequeue it
                    // again while budget remains, spinning the frame away on a call that by
                    // definition cannot progress until the next one.
                    call.Ms += sw.Elapsed.TotalMilliseconds;
                    // Re-arm the depth accounting: the dequeue already decremented it, and the call
                    // is going back into circulation. Without this, parked calls would quietly raise
                    // the effective queue ceiling above MaxQueueDepth.
                    Interlocked.Increment(ref queueDepth);
                    Deferred.Enqueue(call);
                    return;
                }

                call.Ms += sw.Elapsed.TotalMilliseconds;
                call.ResultJson = string.IsNullOrEmpty(result) ? "{}" : result;
                OpsServed++;
                call.Done.Set();
            }
            catch (McpOpException ex)
            {
                sw.Stop();
                Fail(call, ex.Code, ex.Message);
            }
            catch (Exception ex)
            {
                sw.Stop();
                // Errors go to the log as well as the wire — never wire-only (project rule).
                ModLogger.Warning("[Mcp] op '" + call.Op + "' threw: " + ex);
                Fail(call, "internal", ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                // Reached only if the process survived — which is the entire signal.
                if (dangerous)
                {
                    McpForensics.Disarm();
                }
            }
        }

        private static void Fail(McpCall call, string code, string message)
        {
            call.ErrorCode = code;
            call.ErrorMessage = message;
            call.Done.Set();
        }

        private static bool IsWorldReadyForOps()
        {
            HeartopiaComplete host = HeartopiaComplete.Instance;
            return host != null && host.IsWorldReady;
        }

        // The only sanctioned way for a socket thread to log: queued here, emitted by Drain().
        internal static void LogFromWorker(string line)
        {
            if (!string.IsNullOrEmpty(line))
            {
                WorkerLog.Enqueue(line);
            }
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // Endpoint file + token
        // ────────────────────────────────────────────────────────────────────────────────────────

        // Note the directory name: the MARKER is a file called `mcp` in the same folder, so runtime
        // state cannot live in a folder of that name.
        private static string EndpointDirectory() => HelperPaths.GetDirectory("McpBridge");

        private static void WriteEndpointFile()
        {
            try
            {
                McpJsonWriter w = new McpJsonWriter();
                w.BeginObject();
                w.Num("port", Port);
                w.Str("token", token);
                w.Num("protocol", RpcProtocolVersion);
                w.Num("pid", GetCurrentProcessIdSafe());
                w.Str("version", ModBuildVersion.Display);
                w.Str("loader", ModLoaderInfo.IsMelonLoader ? "MelonLoader" : "BepInEx");
                w.Str("startedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                w.EndObject();

                File.WriteAllText(Path.Combine(EndpointDirectory(), "endpoint.json"), w.ToString(),
                    new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Mcp] could not write endpoint.json: " + ex.Message);
            }
        }

        private static void DeleteEndpointFile()
        {
            try
            {
                string path = Path.Combine(EndpointDirectory(), "endpoint.json");
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static string NewToken()
        {
            byte[] bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            StringBuilder sb = new StringBuilder(64);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        // Length-independent comparison. Overkill on loopback, but a token check that leaks timing
        // is the kind of detail that gets copied into somewhere it matters.
        private static bool TokenMatches(string candidate)
        {
            string expected = token;
            if (expected == null || candidate == null || candidate.Length != expected.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                diff |= expected[i] ^ candidate[i];
            }

            return diff == 0;
        }

        private static int GetCurrentProcessIdSafe()
        {
            try
            {
                return Environment.ProcessId;
            }
            catch
            {
                return 0;
            }
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // Framing
        // ────────────────────────────────────────────────────────────────────────────────────────

        // Reads '\n'-terminated UTF-8 frames off a NetworkStream with a hard size cap. Written by
        // hand rather than with StreamReader so the cap is enforced on BYTES as they arrive, and so
        // a partial frame at the cap can be detected instead of silently buffering forever.
        private sealed class LineReader
        {
            private readonly Stream stream;
            private readonly int maxBytes;
            private readonly byte[] buffer = new byte[8192];
            private readonly List<byte> frame = new List<byte>(256);
            private int available;
            private int offset;

            internal LineReader(Stream stream, int maxBytes)
            {
                this.stream = stream;
                this.maxBytes = maxBytes;
            }

            internal string ReadLine(out bool overflow, out bool closed)
            {
                overflow = false;
                closed = false;
                this.frame.Clear();

                while (true)
                {
                    if (this.offset >= this.available)
                    {
                        int read;
                        try
                        {
                            read = this.stream.Read(this.buffer, 0, this.buffer.Length);
                        }
                        catch (Exception)
                        {
                            closed = true;
                            return null;
                        }

                        if (read <= 0)
                        {
                            closed = true;
                            return null;
                        }

                        this.available = read;
                        this.offset = 0;
                    }

                    while (this.offset < this.available)
                    {
                        byte b = this.buffer[this.offset++];
                        if (b == (byte)'\n')
                        {
                            // Tolerate CRLF from a hand-typed test client.
                            int count = this.frame.Count;
                            if (count > 0 && this.frame[count - 1] == (byte)'\r')
                            {
                                count--;
                            }

                            return Encoding.UTF8.GetString(this.frame.ToArray(), 0, count);
                        }

                        if (this.frame.Count >= this.maxBytes)
                        {
                            overflow = true;
                            return null;
                        }

                        this.frame.Add(b);
                    }
                }
            }
        }
    }
}
#endif
