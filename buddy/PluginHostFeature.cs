#if FEATURE_MCP
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using HeartopiaMod.Plugins;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // Sandbox plugin host — collectible AssemblyLoadContexts, so experimental code can be swapped
    // without restarting the game.
    //
    // The contract and the reasoning behind its prohibitions live in PluginContract.cs. This file is
    // the machinery: validate → load → tick → revoke → unload → prove it collected.
    // ============================================================================================
    internal static class PluginHost
    {
        // Assemblies arrive as BYTES, never as a path. LoadFromStream takes no file lock, so the
        // plugin's source project can be rebuilt while the previous version is still live — which is
        // the entire iteration loop this exists for — and the pdb gives real line numbers in stack
        // traces.
        private sealed class PluginAlc : AssemblyLoadContext
        {
            internal PluginAlc(string id)
                : base("bugtopia-plugin:" + id, isCollectible: true)
            {
            }

            // null => resolve from the DEFAULT context. This is what keeps type identity: the
            // plugin's IBugtopiaPlugin, HeartopiaComplete, UnityEngine.Vector3 and every interop type
            // are the HOST's copies, not private duplicates that would fail every cast.
            protected override Assembly Load(AssemblyName assemblyName) => null;

            // A native library loaded through this context would pin it forever. Failing loudly beats
            // an ALC that silently never collects — and the validator rejects [DllImport] anyway, so
            // reaching here means something slipped through.
            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) => IntPtr.Zero;
        }

        private sealed class LoadedPlugin
        {
            internal string Id;
            internal string Sha256;
            internal IBugtopiaPlugin Instance;
            internal PluginAlc Alc;
            internal HostApi Api;
            internal DateTime LoadedUtc;
            internal long Ticks;
            internal FeatureBreakerState Breaker;
            internal string LastError;
        }

        // A plugin whose ALC has been asked to unload, still being watched to see whether it actually
        // collects. Kept OUT of the loaded list so nothing ticks it.
        private sealed class UnloadWatch
        {
            internal string Id;
            internal WeakReference Reference;
            internal int Attempts;
            internal int NextAttemptFrame;
            internal DateTime StartedUtc;
        }

        private static readonly Dictionary<string, LoadedPlugin> Loaded =
            new Dictionary<string, LoadedPlugin>(StringComparer.Ordinal);

        private static readonly List<UnloadWatch> Watches = new List<UnloadWatch>();
        private static readonly List<string> LeakedIds = new List<string>();

        internal static int LoadedCount => Loaded.Count;

        // ── Load ─────────────────────────────────────────────────────────────────────────────────

        internal static bool TryLoad(string id, byte[] dll, byte[] pdb, out string error, out List<string> violations)
        {
            error = null;
            violations = null;

            if (string.IsNullOrEmpty(id))
            {
                error = "a plugin id is required";
                return false;
            }

            if (dll == null || dll.Length == 0)
            {
                error = "no assembly bytes";
                return false;
            }

            if (Loaded.ContainsKey(id))
            {
                error = "'" + id + "' is already loaded — unload it first (plugin.reload does both)";
                return false;
            }

            if (!PluginValidator.Validate(dll, out string entryTypeName, out violations))
            {
                error = "contract violations (" + violations.Count + ")";
                return false;
            }

            string sha = Sha256Hex(dll);
            PluginAlc alc = new PluginAlc(id);
            LoadedPlugin entry = null;

            try
            {
                Assembly asm;
                using (MemoryStream dllStream = new MemoryStream(dll))
                {
                    if (pdb != null && pdb.Length > 0)
                    {
                        using MemoryStream pdbStream = new MemoryStream(pdb);
                        asm = alc.LoadFromStream(dllStream, pdbStream);
                    }
                    else
                    {
                        asm = alc.LoadFromStream(dllStream);
                    }
                }

                Type entryType = asm.GetType(entryTypeName, throwOnError: false);
                if (entryType == null)
                {
                    error = "entry type '" + entryTypeName + "' not found after load";
                    UnloadAlcQuietly(id, alc);
                    return false;
                }

                IBugtopiaPlugin instance = Activator.CreateInstance(entryType) as IBugtopiaPlugin;
                if (instance == null)
                {
                    error = "entry type does not implement IBugtopiaPlugin at runtime";
                    UnloadAlcQuietly(id, alc);
                    return false;
                }

                HostApi api = new HostApi(id);
                entry = new LoadedPlugin
                {
                    Id = id,
                    Sha256 = sha,
                    Instance = instance,
                    Alc = alc,
                    Api = api,
                    LoadedUtc = DateTime.UtcNow,
                };

                instance.Load(api);
                Loaded[id] = entry;
                WriteResidentRecord();
                ModLogger.Msg("[Plugin] loaded '" + id + "' (" + entryTypeName + ", " + dll.Length
                              + " bytes, sha " + sha.Substring(0, 12) + ")");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                // A plugin that threw in Load() must not stay half-alive: drop everything we took and
                // unload, or the failed attempt pins its own ALC for the rest of the session.
                if (entry != null)
                {
                    Loaded.Remove(id);
                    try { entry.Api.RevokeAll(); } catch { }
                }

                UnloadAlcQuietly(id, alc);
                return false;
            }
        }

        // ── Unload ───────────────────────────────────────────────────────────────────────────────

        internal static bool TryUnload(string id, out string error)
        {
            error = null;
            if (!Loaded.TryGetValue(id, out LoadedPlugin entry))
            {
                error = "'" + id + "' is not loaded";
                return false;
            }

            // Order matters and is the whole trick. Stop ticking it, let it clean up, then revoke
            // every host-held reference INTO it, and only then unload — a single surviving reference
            // (a coroutine iterator, the instance itself) keeps the ALC alive forever.
            Loaded.Remove(id);
            WriteResidentRecord();

            try
            {
                entry.Instance.Unload();
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Plugin] '" + id + "' threw in Unload(): " + ex.Message);
            }

            try
            {
                entry.Api.RevokeAll();
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Plugin] '" + id + "' revoke failed: " + ex.Message);
            }

            PluginAlc alc = entry.Alc;
            entry.Instance = null;
            entry.Alc = null;
            entry.Api = null;

            WeakReference reference = new WeakReference(alc, trackResurrection: false);
            try
            {
                alc.Unload();
            }
            catch (Exception ex)
            {
                error = "Unload() threw: " + ex.Message;
                return false;
            }

            Watches.Add(new UnloadWatch
            {
                Id = id,
                Reference = reference,
                NextAttemptFrame = Time.frameCount + 1,
                StartedUtc = DateTime.UtcNow,
            });

            ModLogger.Msg("[Plugin] unloading '" + id + "' — verifying collection…");
            return true;
        }

        private static void UnloadAlcQuietly(string id, PluginAlc alc)
        {
            try
            {
                alc.Unload();
            }
            catch
            {
            }
        }

        // ── Tick ─────────────────────────────────────────────────────────────────────────────────

        internal static void Tick()
        {
            if (Loaded.Count > 0)
            {
                float now = Time.unscaledTime;
                // ToArray: a plugin is allowed to get itself unloaded from inside its own Tick.
                LoadedPlugin[] snapshot = new LoadedPlugin[Loaded.Count];
                Loaded.Values.CopyTo(snapshot, 0);
                for (int i = 0; i < snapshot.Length; i++)
                {
                    LoadedPlugin entry = snapshot[i];
                    if (entry.Instance == null || !entry.Breaker.ShouldRun(now))
                    {
                        continue;
                    }

                    try
                    {
                        entry.Instance.Tick();
                        entry.Ticks++;
                        entry.Breaker.Success();
                    }
                    catch (Exception ex)
                    {
                        entry.LastError = ex.GetType().Name + ": " + ex.Message;
                        entry.Breaker.Failure("Plugin:" + entry.Id, ex, now);
                    }
                }
            }

            ProcessUnloadWatches();
        }

        // Collection is not instantaneous and forcing it in one blocking burst is a visible hitch, so
        // the attempts are spread over frames. Three tries is empirically enough for a clean plugin;
        // beyond that something outside still holds a reference and no amount of collecting helps.
        private static void ProcessUnloadWatches()
        {
            if (Watches.Count == 0)
            {
                return;
            }

            int frame = Time.frameCount;
            for (int i = Watches.Count - 1; i >= 0; i--)
            {
                UnloadWatch watch = Watches[i];
                if (frame < watch.NextAttemptFrame)
                {
                    continue;
                }

                if (!watch.Reference.IsAlive)
                {
                    Watches.RemoveAt(i);
                    ModLogger.Msg("[Plugin] '" + watch.Id + "' unloaded and collected after "
                                  + watch.Attempts + " GC pass(es).");
                    continue;
                }

                if (watch.Attempts >= 3)
                {
                    Watches.RemoveAt(i);
                    if (!LeakedIds.Contains(watch.Id))
                    {
                        LeakedIds.Add(watch.Id);
                    }

                    // Functionally it IS unloaded — nothing ticks it and its code no longer runs.
                    // Only the memory is retained, and a reload still works because the new version
                    // gets a fresh context.
                    ModLogger.Warning("[Plugin] '" + watch.Id + "' did not collect after 3 GC passes — "
                                      + "something still references it. It is functionally unloaded; "
                                      + "the memory returns on restart.");
                    continue;
                }

                watch.Attempts++;
                watch.NextAttemptFrame = frame + 10;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        // ── Query / call ─────────────────────────────────────────────────────────────────────────

        internal static string CallPlugin(string id, string method, string argsJson, out string error)
        {
            error = null;
            if (!Loaded.TryGetValue(id, out LoadedPlugin entry) || entry.Instance == null)
            {
                error = "'" + id + "' is not loaded";
                return null;
            }

            try
            {
                return entry.Instance.Call(method, argsJson);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        internal static void WriteListJson(McpJsonWriter w)
        {
            w.Num("loaded", Loaded.Count);
            w.Num("unloading", Watches.Count);
            w.BeginArray("plugins");
            foreach (KeyValuePair<string, LoadedPlugin> pair in Loaded)
            {
                LoadedPlugin entry = pair.Value;
                w.BeginArrayObject();
                w.Str("id", entry.Id);
                w.Str("sha256", entry.Sha256);
                w.Num("ticks", entry.Ticks);
                w.Str("loadedUtc", entry.LoadedUtc.ToString("O"));
                w.Num("coroutines", entry.Api == null ? 0 : entry.Api.CoroutineCount);
                if (!string.IsNullOrEmpty(entry.LastError))
                {
                    w.Str("lastError", entry.LastError);
                }

                w.EndObject();
            }

            w.EndArray();

            w.BeginArray("leaked");
            for (int i = 0; i < LeakedIds.Count; i++)
            {
                w.ArrayStr(LeakedIds[i]);
            }

            w.EndArray();
        }

        internal static bool TryGetSha(string id, out string sha)
        {
            if (id != null && Loaded.TryGetValue(id, out LoadedPlugin entry))
            {
                sha = entry.Sha256;
                return true;
            }

            sha = null;
            return false;
        }

        // Rewritten on every load and unload — twice per plugin lifetime, not per frame. A crash
        // while this file is non-empty means the process died with those plugins ticking
        // (McpForensics.ReadPreviousResident).
        private static void WriteResidentRecord()
        {
            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            // Stamped INTO the record: whether this session can clear it on a clean exit. Next
            // startup needs that to know whether the file surviving means anything at all.
            w.Bool("quitSignal", McpQuitSignal.Armed);
            w.BeginArray("plugins");
            foreach (KeyValuePair<string, LoadedPlugin> pair in Loaded)
            {
                w.BeginArrayObject();
                w.Str("id", pair.Value.Id);
                w.Str("sha", pair.Value.Sha256);
                w.Str("loadedUtc", pair.Value.LoadedUtc.ToString("O"));
                w.EndObject();
            }

            w.EndArray();
            w.EndObject();

            McpForensics.WriteResident(Loaded.Count == 0 ? "[]" : w.ToString());
        }

        internal static string Sha256Hex(byte[] data)
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(data);
            StringBuilder sb = new StringBuilder(64);
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }

            return sb.ToString();
        }

        // ── Host API implementation ──────────────────────────────────────────────────────────────

        private sealed class HostApi : IHostApi
        {
            private readonly string id;
            private readonly List<object> coroutines = new List<object>();
            private readonly Dictionary<string, string> state = new Dictionary<string, string>(StringComparer.Ordinal);

            internal HostApi(string id)
            {
                this.id = id;
            }

            internal int CoroutineCount => this.coroutines.Count;

            public string Id => this.id;

            public HeartopiaComplete Mod => HeartopiaComplete.Instance;

            public void Log(string message)
            {
                ModLogger.Msg("[Plugin:" + this.id + "] " + message);
            }

            public void Toast(string message)
            {
                try
                {
                    HeartopiaComplete.Instance?.McpPluginToast(message);
                }
                catch
                {
                    this.Log(message);
                }
            }

            public bool IsWorldReady
            {
                get
                {
                    HeartopiaComplete mod = HeartopiaComplete.Instance;
                    return mod != null && mod.IsWorldReady;
                }
            }

            public int WorldEpoch => HeartopiaComplete.AuraMonoWorldEpoch;

            public bool TryGetPlayerPosition(out Vector3 position)
            {
                position = Vector3.zero;
                HeartopiaComplete mod = HeartopiaComplete.Instance;
                return mod != null && mod.TryGetLocalPlayerPosition(out position);
            }

            public void StartCoroutine(IEnumerator routine)
            {
                if (routine == null)
                {
                    return;
                }

                object handle = ModCoroutines.Start(routine);
                if (handle != null)
                {
                    this.coroutines.Add(handle);
                }
            }

            public IDictionary<string, string> State => this.state;

            public IMonoApi Mono { get; } = new MonoApi();

            public IEventsApi Events => this.events ??= new EventsApi(this.id);

            private EventsApi events;

            public IUiApi Ui => this.ui ??= new UiApi(this.id);

            private UiApi ui;

            // Stateless apart from the id: the page objects themselves live in the mod (they are
            // Unity-facing and must survive a theme rebuild), so this cannot become a second place
            // that holds plugin delegates.
            private sealed class UiApi : IUiApi
            {
                private readonly string pluginId;

                internal UiApi(string pluginId)
                {
                    this.pluginId = pluginId;
                }

                public bool IsAvailable => HeartopiaComplete.Instance != null;

                public IPluginPage AddPage(string title)
                {
                    HeartopiaComplete mod = HeartopiaComplete.Instance;
                    return mod == null ? null : mod.AddMcpPluginPage(this.pluginId, title);
                }
            }

            // Carries the plugin id so RevokeAll can find exactly this plugin's handlers, and
            // nothing else: one plugin unloading must not silence another's subscriptions.
            private sealed class EventsApi : IEventsApi
            {
                private readonly string pluginId;

                internal EventsApi(string pluginId)
                {
                    this.pluginId = pluginId;
                }

                public bool Subscribe(string eventFullName, int payloadBytes,
                                      Action<HeartopiaComplete.GameEventSnapshot> handler, out string error)
                {
                    return McpEventBroker.Subscribe(this.pluginId, eventFullName, payloadBytes, handler, out error);
                }

                public int SubscribedTypeCount => McpEventBroker.SubscribedEventCount;

                public long DispatchedCount => McpEventBroker.Dispatched;
            }

            // Everything the host started on the plugin's behalf, torn down. The coroutine list is
            // the important one: ModCoroutines holds each iterator, and an iterator is a PLUGIN type,
            // so a single surviving routine pins the whole context.
            // Stateless: every call re-resolves through the live mod instance, so nothing here can
            // outlive an unload or pin the plugin's load context.
            private sealed class MonoApi : IMonoApi
            {
                public IntPtr FindClass(string fullName)
                {
                    HeartopiaComplete mod = HeartopiaComplete.Instance;
                    return mod == null || string.IsNullOrEmpty(fullName)
                        ? IntPtr.Zero
                        : mod.FindAuraMonoClassAnySpelling(fullName);
                }

                public IntPtr FindMethod(IntPtr klass, string methodName, int paramCount)
                {
                    HeartopiaComplete mod = HeartopiaComplete.Instance;
                    return mod == null ? IntPtr.Zero : mod.McpMonoFindMethod(klass, methodName, paramCount);
                }

                public bool Invoke(IntPtr method, IntPtr instance, IntPtr[] args,
                                   out IntPtr result, out string error)
                {
                    HeartopiaComplete mod = HeartopiaComplete.Instance;
                    if (mod == null)
                    {
                        result = IntPtr.Zero;
                        error = "mod instance unavailable";
                        return false;
                    }

                    return mod.McpMonoInvoke(method, instance, args, out result, out error);
                }

                public bool PinningAvailable => HeartopiaComplete.McpMonoPinningAvailable;

                public IMonoObjects GetComponents(IntPtr componentClass)
                {
                    return MonoObjects.Collect(
                        (HeartopiaComplete mod, List<IntPtr> objs, List<uint> pins, out string err) =>
                            mod.McpMonoGetComponents(componentClass, objs, pins, out err));
                }

                public IMonoObjects EnumerateCollection(IntPtr collectionObj)
                {
                    return MonoObjects.Collect(
                        (HeartopiaComplete mod, List<IntPtr> objs, List<uint> pins, out string err) =>
                            mod.McpMonoEnumerateCollection(collectionObj, objs, pins, out err));
                }

                public bool TryGetField(IntPtr obj, string fieldName, out IntPtr valueObj)
                {
                    HeartopiaComplete mod = HeartopiaComplete.Instance;
                    if (mod == null)
                    {
                        valueObj = IntPtr.Zero;
                        return false;
                    }

                    return mod.McpMonoTryGetField(obj, fieldName, out valueObj);
                }

                public uint GetUInt(IntPtr obj, params string[] fieldNames)
                {
                    HeartopiaComplete mod = HeartopiaComplete.Instance;
                    return mod == null ? 0u : mod.McpMonoGetUInt(obj, fieldNames);
                }

                public int GetInt(IntPtr boxedStruct, string fieldName)
                {
                    HeartopiaComplete mod = HeartopiaComplete.Instance;
                    return mod == null ? 0 : mod.McpMonoGetInt(boxedStruct, fieldName);
                }

                public string GetString(IntPtr monoStringObj)
                {
                    return HeartopiaComplete.McpMonoGetString(monoStringObj);
                }

                public uint Pin(IntPtr obj) => HeartopiaComplete.McpMonoPin(obj);

                public void Unpin(uint handle) => HeartopiaComplete.McpMonoUnpin(handle);

                public bool SendCommand(string commandFullName, IDictionary<string, object> fields,
                                        int channel, bool needAuthed, out string status)
                {
                    HeartopiaComplete mod = HeartopiaComplete.Instance;
                    if (mod == null)
                    {
                        status = "mod instance unavailable";
                        return false;
                    }

                    return mod.TryAuraSendCommand(commandFullName, fields, channel, needAuthed, out status);
                }

                public bool ValidateCommand(string commandFullName, IDictionary<string, object> fields,
                                            out string status)
                {
                    HeartopiaComplete mod = HeartopiaComplete.Instance;
                    if (mod == null)
                    {
                        status = "mod instance unavailable";
                        return false;
                    }

                    return mod.TryValidateAuraCommand(commandFullName, fields, out status);
                }
            }

            private delegate bool CollectDelegate(HeartopiaComplete mod, List<IntPtr> objects,
                                                  List<uint> pins, out string error);

            // Owns the pins it took and frees them on Dispose — so the correct usage (`using`) is
            // also the shortest one. A caller who forgets still gets a finalizer sweep rather than a
            // permanent leak, but the pointers are only valid inside the using block either way.
            private sealed class MonoObjects : IMonoObjects
            {
                private readonly List<IntPtr> objects = new List<IntPtr>();
                private readonly List<uint> pins = new List<uint>();
                private bool disposed;

                public string Error { get; private set; }

                public int Count => this.disposed ? 0 : this.objects.Count;

                public IntPtr this[int index]
                {
                    get
                    {
                        if (this.disposed || index < 0 || index >= this.objects.Count)
                        {
                            return IntPtr.Zero;
                        }

                        return this.objects[index];
                    }
                }

                internal static MonoObjects Collect(CollectDelegate collect)
                {
                    MonoObjects set = new MonoObjects();
                    HeartopiaComplete mod = HeartopiaComplete.Instance;
                    if (mod == null)
                    {
                        set.Error = "mod instance unavailable";
                        return set;
                    }

                    try
                    {
                        if (!collect(mod, set.objects, set.pins, out string error))
                        {
                            set.Error = error ?? "collection failed";
                            // Whatever was pinned before the failure still has to go back.
                            set.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        set.Error = ex.GetType().Name + ": " + ex.Message;
                        set.Dispose();
                    }

                    return set;
                }

                ~MonoObjects()
                {
                    this.ReleasePins();
                }

                public void Dispose()
                {
                    this.ReleasePins();
                    GC.SuppressFinalize(this);
                }

                private void ReleasePins()
                {
                    if (this.disposed)
                    {
                        return;
                    }

                    this.disposed = true;
                    try
                    {
                        HeartopiaComplete.FreeAuraMonoPins(this.pins);
                    }
                    catch
                    {
                    }

                    this.pins.Clear();
                    this.objects.Clear();
                }
            }

            internal void RevokeAll()
            {
                for (int i = 0; i < this.coroutines.Count; i++)
                {
                    try
                    {
                        ModCoroutines.Stop(this.coroutines[i]);
                    }
                    catch
                    {
                    }
                }

                this.coroutines.Clear();
                this.state.Clear();

                // Menu pages, and with them every click/toggle/slider callback the plugin handed
                // over. A live Button holding a plugin delegate pins the load context just as surely
                // as a coroutine iterator does — and unlike the coroutine it stays on screen looking
                // functional, so this must run whether or not the plugin cleaned up after itself.
                try
                {
                    int pages = HeartopiaComplete.Instance?.RemoveMcpPluginPages(this.id) ?? 0;
                    if (pages > 0)
                    {
                        ModLogger.Msg("[Plugin:" + this.id + "] removed " + pages + " menu page(s).");
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Warning("[Plugin:" + this.id + "] page removal failed: " + ex.Message);
                }

                // The engine keeps its registration forever — only this plugin's callbacks go. A
                // handler left behind would be a plugin-typed delegate held by the host, i.e. the
                // exact reference direction that makes a load context uncollectable.
                int events = McpEventBroker.RevokeAll(this.id);
                if (events > 0)
                {
                    ModLogger.Msg("[Plugin:" + this.id + "] revoked " + events + " event subscription(s).");
                }
            }
        }
    }

    // ============================================================================================
    // Static contract validation — reads the incoming bytes as metadata BEFORE the runtime ever
    // sees them.
    //
    // This is what makes "unload" a fact rather than a hope. Every rejection here is something that
    // would otherwise make the ALC uncollectible for the rest of the session, and the failure mode
    // without this check is miserable: the plugin loads, works, unloads "successfully", and the
    // process just quietly grows.
    //
    // All violations are reported together so the author fixes them in one pass.
    // ============================================================================================
    internal static class PluginValidator
    {
        private const string EntryInterface = "HeartopiaMod.Plugins.IBugtopiaPlugin";

        private static readonly string[] BannedAssemblies =
        {
            "0Harmony",
            "MonoMod.RuntimeDetour",
        };

        private static readonly string[] BannedTypes =
        {
            "Il2CppInterop.Runtime.Injection.ClassInjector",
            "Il2CppInterop.Runtime.DelegateSupport",
            "System.Threading.Thread",
            "System.Threading.Timer",
            "System.Timers.Timer",
        };

        internal static bool Validate(byte[] dll, out string entryTypeName, out List<string> violations)
        {
            entryTypeName = null;
            violations = new List<string>();

            try
            {
                using MemoryStream ms = new MemoryStream(dll, writable: false);
                using PEReader pe = new PEReader(ms);
                if (!pe.HasMetadata)
                {
                    violations.Add("not a managed assembly");
                    return false;
                }

                MetadataReader md = pe.GetMetadataReader();

                foreach (AssemblyReferenceHandle handle in md.AssemblyReferences)
                {
                    string name = md.GetString(md.GetAssemblyReference(handle).Name);
                    for (int i = 0; i < BannedAssemblies.Length; i++)
                    {
                        if (string.Equals(name, BannedAssemblies[i], StringComparison.OrdinalIgnoreCase))
                        {
                            violations.Add("references " + name
                                + " — detours cannot be unloaded; a detour into freed code is an instant crash");
                        }
                    }
                }

                foreach (TypeReferenceHandle handle in md.TypeReferences)
                {
                    TypeReference tr = md.GetTypeReference(handle);
                    string full = Combine(md.GetString(tr.Namespace), md.GetString(tr.Name));
                    for (int i = 0; i < BannedTypes.Length; i++)
                    {
                        if (string.Equals(full, BannedTypes[i], StringComparison.Ordinal))
                        {
                            violations.Add("uses " + full + " — it would keep the load context alive forever"
                                + (full.EndsWith("Thread", StringComparison.Ordinal)
                                    || full.EndsWith("Timer", StringComparison.Ordinal)
                                    ? "; use host.StartCoroutine instead"
                                    : string.Empty));
                        }
                    }
                }

                foreach (MethodDefinitionHandle handle in md.MethodDefinitions)
                {
                    MethodDefinition method = md.GetMethodDefinition(handle);
                    if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                    {
                        violations.Add("declares a [DllImport] method '" + md.GetString(method.Name)
                            + "' — the resolved native module pins the load context");
                    }
                }

                List<string> candidates = new List<string>();
                foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
                {
                    TypeDefinition td = md.GetTypeDefinition(handle);
                    if (!ImplementsEntryInterface(md, td))
                    {
                        continue;
                    }

                    string full = Combine(md.GetString(td.Namespace), md.GetString(td.Name));
                    if ((td.Attributes & TypeAttributes.Public) == 0)
                    {
                        violations.Add("entry type '" + full + "' must be public");
                        continue;
                    }

                    if ((td.Attributes & TypeAttributes.Abstract) != 0)
                    {
                        violations.Add("entry type '" + full + "' must not be abstract");
                        continue;
                    }

                    if (!HasParameterlessConstructor(md, td))
                    {
                        violations.Add("entry type '" + full + "' needs a public parameterless constructor");
                        continue;
                    }

                    candidates.Add(full);
                }

                if (candidates.Count == 0)
                {
                    violations.Add("no public type implements " + EntryInterface);
                }
                else if (candidates.Count > 1)
                {
                    violations.Add("more than one type implements " + EntryInterface + ": "
                        + string.Join(", ", candidates));
                }
                else
                {
                    entryTypeName = candidates[0];
                }
            }
            catch (Exception ex)
            {
                violations.Add("metadata read failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            return violations.Count == 0 && entryTypeName != null;
        }

        private static bool ImplementsEntryInterface(MetadataReader md, TypeDefinition td)
        {
            foreach (InterfaceImplementationHandle handle in td.GetInterfaceImplementations())
            {
                EntityHandle iface = md.GetInterfaceImplementation(handle).Interface;
                if (string.Equals(ResolveTypeName(md, iface), EntryInterface, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasParameterlessConstructor(MetadataReader md, TypeDefinition td)
        {
            foreach (MethodDefinitionHandle handle in td.GetMethods())
            {
                MethodDefinition method = md.GetMethodDefinition(handle);
                if (!string.Equals(md.GetString(method.Name), ".ctor", StringComparison.Ordinal))
                {
                    continue;
                }

                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                {
                    continue;
                }

                // Signature blob: [callconv][paramCount]… — a zero param count is what we want, and
                // reading the two header bytes avoids decoding the whole signature.
                BlobReader sig = md.GetBlobReader(method.Signature);
                sig.ReadSignatureHeader();
                if (sig.ReadCompressedInteger() == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveTypeName(MetadataReader md, EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeReference:
                {
                    TypeReference tr = md.GetTypeReference((TypeReferenceHandle)handle);
                    return Combine(md.GetString(tr.Namespace), md.GetString(tr.Name));
                }

                case HandleKind.TypeDefinition:
                {
                    TypeDefinition td = md.GetTypeDefinition((TypeDefinitionHandle)handle);
                    return Combine(md.GetString(td.Namespace), md.GetString(td.Name));
                }

                default:
                    return null;
            }
        }

        private static string Combine(string ns, string name)
        {
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }
    }
}
#endif
