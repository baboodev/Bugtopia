using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HeartopiaMod
{
    // Disk autoload, persisted plugin state, and the shared quarantine/resident files.
    //
    // User dlls live in %LocalLow%/Bugtopia/Plugins, never next to bugtopia.dll: BepInEx and
    // MelonLoader would load those as their own plugins. Bytes are still read into a stream so the
    // file is not locked and can be rebuilt while the previous version is loaded.
    internal static partial class PluginHost
    {
        private const string ReadmeName = "README.txt";
        private const string ResidentName = "resident.json";
        private const string QuarantineName = "quarantine.json";

        private static readonly Dictionary<string, string> DiskErrors =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly HashSet<string> QuarantinedShas =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<string> QuarantineLines = new List<string>();

        private static bool quarantineLoaded;
        private static bool diskLoaded;

        // Stashed by ReadPreviousResident before the file is rewritten, so session.report can
        // describe a killed session even though the on-disk record is already empty.
        internal static string PreviousResidentJson;
        internal static bool PreviousSessionHadResident;
        internal static bool PreviousSessionKilled;
        private static bool stateRefusalLogged;

        internal struct PluginRow
        {
            internal string Id;
            internal string Detail;
            internal bool HasDll;
            internal bool Enabled;
            internal bool Loaded;
            internal bool Quarantined;
            internal long Ticks;
        }

        internal static event Action CleanQuit;

        internal static void Install()
        {
            try
            {
                // Before any other install log line, so startup is in the ring. The bridge plugin
                // loads later, on world-ready, and would otherwise miss everything until then.
                ModLogger.EnableRing();
                EnsureFolder();
                McpQuitSignal.Install();
                ReadPreviousResident();
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Plugin] host install failed: " + ex.Message);
            }
        }

        // Application.quitting fired, so this exit is orderly. Flush state and drop the resident
        // list. A file that still names plugins on the next launch, with quitSignal true, means
        // the process was killed while they were ticking.
        internal static void OnCleanQuit()
        {
            foreach (KeyValuePair<string, LoadedPlugin> pair in Loaded)
            {
                try
                {
                    pair.Value.Api?.FlushState(true);
                }
                catch
                {
                }
            }

            WriteResidentRaw("{\"quitSignal\":true,\"plugins\":[]}");
            Action quit = CleanQuit;
            if (quit != null)
            {
                try
                {
                    quit();
                }
                catch
                {
                }
            }
        }

        // Once per process, on the first world-ready. Loads only dlls the user has turned on.
        // A new dll stays unloaded until Enabled is checked. Later worlds do not reload; Reload does.
        internal static bool OnWorldReady()
        {
            if (diskLoaded)
            {
                return true;
            }

            diskLoaded = true;
            try
            {
                ScanAndLoad();
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Plugin] disk scan failed: " + ex.Message);
            }

            return true;
        }

        internal static void ReloadFromDisk()
        {
            List<string> ids = new List<string>();
            foreach (KeyValuePair<string, LoadedPlugin> pair in Loaded)
            {
                if (pair.Value.Source == PluginSource.Disk)
                {
                    ids.Add(pair.Key);
                }
            }

            for (int i = 0; i < ids.Count; i++)
            {
                TryUnload(ids[i], out _);
            }

            DiskErrors.Clear();
            ScanAndLoad();
        }

        internal static void SetDiskEnabled(string id, bool enabled)
        {
            if (!IsPluginId(id))
            {
                return;
            }

            string dir = Folder();
            string enabledMarker = Path.Combine(dir, id + ".enabled");
            string disabledMarker = Path.Combine(dir, id + ".disabled");
            try
            {
                if (enabled)
                {
                    if (File.Exists(disabledMarker))
                    {
                        File.Delete(disabledMarker);
                    }

                    File.WriteAllBytes(enabledMarker, Array.Empty<byte>());
                    string dll = Path.Combine(dir, id + ".dll");
                    if (File.Exists(dll))
                    {
                        TryLoadFile(dll, false, out _);
                    }
                }
                else
                {
                    if (File.Exists(enabledMarker))
                    {
                        File.Delete(enabledMarker);
                    }

                    if (Loaded.TryGetValue(id, out LoadedPlugin entry) && entry.Source == PluginSource.Disk)
                    {
                        TryUnload(id, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Plugin] could not change '" + id + "': " + ex.Message);
            }

            Generation++;
        }

        internal static bool TryForceLoad(string id, out string error)
        {
            error = null;
            if (!IsPluginId(id))
            {
                error = "id is not a safe file name";
                return false;
            }

            string dll = Path.Combine(Folder(), id + ".dll");
            if (!File.Exists(dll))
            {
                error = "dll is missing";
                return false;
            }

            if (Loaded.ContainsKey(id))
            {
                TryUnload(id, out _);
            }

            return TryLoadFile(dll, true, out error);
        }

        internal static bool TryGetTicks(string id, out long ticks, out string lastError)
        {
            if (id != null && Loaded.TryGetValue(id, out LoadedPlugin entry))
            {
                ticks = entry.Ticks;
                lastError = entry.LastError;
                return true;
            }

            ticks = 0;
            lastError = null;
            return false;
        }

        internal static void CopyRows(List<PluginRow> into)
        {
            into.Clear();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, LoadedPlugin> pair in Loaded)
            {
                seen.Add(pair.Key);
                into.Add(DescribeLoaded(pair.Value));
            }

            string dir = Folder();
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.dll");
            }
            catch
            {
                return;
            }

            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                string id = Path.GetFileNameWithoutExtension(files[i]);
                if (!ConsiderDiskName(Path.GetFileName(files[i]), id) || seen.Contains(id))
                {
                    continue;
                }

                bool enabled = IsDiskOptedIn(dir, id);
                DiskErrors.TryGetValue(id, out string error);
                bool quarantined = string.Equals(error, "quarantined", StringComparison.Ordinal);
                string detail = enabled ? (error ?? "not loaded") : "not enabled";
                into.Add(new PluginRow
                {
                    Id = id,
                    Detail = detail,
                    HasDll = true,
                    Enabled = enabled,
                    Loaded = false,
                    Quarantined = quarantined,
                    Ticks = 0,
                });
            }
        }

        internal static bool IsQuarantined(string sha)
        {
            EnsureQuarantineLoaded();
            return !string.IsNullOrEmpty(sha) && QuarantinedShas.Contains(sha);
        }

        internal static void Quarantine(string sha, string reason)
        {
            EnsureQuarantineLoaded();
            if (string.IsNullOrEmpty(sha) || !QuarantinedShas.Add(sha))
            {
                return;
            }

            QuarantineLines.Add(sha + "\t" + (reason ?? string.Empty));
            SaveQuarantine();
        }

        internal static bool Unquarantine(string sha)
        {
            EnsureQuarantineLoaded();
            if (string.IsNullOrEmpty(sha) || !QuarantinedShas.Remove(sha))
            {
                return false;
            }

            for (int i = QuarantineLines.Count - 1; i >= 0; i--)
            {
                if (QuarantineLines[i].StartsWith(sha, StringComparison.OrdinalIgnoreCase))
                {
                    QuarantineLines.RemoveAt(i);
                }
            }

            SaveQuarantine();
            return true;
        }

        internal static void CopyQuarantineLines(List<string> into)
        {
            EnsureQuarantineLoaded();
            if (into == null)
            {
                return;
            }

            for (int i = 0; i < QuarantineLines.Count; i++)
            {
                into.Add(QuarantineLines[i]);
            }
        }

        private static PluginRow DescribeLoaded(LoadedPlugin entry)
        {
            string dir = Folder();
            bool hasDll = File.Exists(Path.Combine(dir, entry.Id + ".dll"));
            bool enabled = entry.Source != PluginSource.Disk || IsDiskOptedIn(dir, entry.Id);
            string detail = entry.Source == PluginSource.Disk ? "disk" : "mcp";

            return new PluginRow
            {
                Id = entry.Id,
                Detail = detail,
                HasDll = hasDll,
                Enabled = enabled || !hasDll,
                Loaded = true,
                Quarantined = false,
                Ticks = entry.Ticks,
            };
        }

        private static void ScanAndLoad()
        {
            EnsureFolder();
            string dir = Folder();
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.dll");
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Plugin] could not list " + dir + ": " + ex.Message);
                return;
            }

            Array.Sort(files, StringComparer.Ordinal);
            int loaded = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                string id = Path.GetFileNameWithoutExtension(fileName);
                if (!ConsiderDiskName(fileName, id))
                {
                    continue;
                }

                if (!IsPluginId(id))
                {
                    ModLogger.Warning("[Plugin] skipped '" + fileName + "' — id must be letters, digits, '.', '_' or '-'.");
                    continue;
                }

                if (!IsDiskOptedIn(dir, id))
                {
                    continue;
                }

                if (TryLoadFile(files[i], false, out _))
                {
                    loaded++;
                }
            }

            ModLogger.Msg("[Plugin] disk scan: " + loaded + " loaded from " + dir);
            HeartopiaComplete.Instance?.SyncPluginsTabWithFolder();
        }

        // True when the folder holds at least one plugin dll. A .disabled sibling still counts:
        // the file is there, and the Plugins tab is how it gets switched back on.
        internal static bool HasDiskPlugins()
        {
            string dir;
            try
            {
                dir = Folder();
            }
            catch
            {
                return false;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.dll");
            }
            catch
            {
                return false;
            }

            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                string id = Path.GetFileNameWithoutExtension(fileName);
                if (ConsiderDiskName(fileName, id) && IsPluginId(id))
                {
                    return true;
                }
            }

            return false;
        }

        // Opt-in is a sibling <id>.enabled written by the Plugins tab. <id>.disabled still wins,
        // so an older skip marker keeps the dll off even if both files are present.
        private static bool IsDiskOptedIn(string dir, string id)
        {
            if (File.Exists(Path.Combine(dir, id + ".disabled")))
            {
                return false;
            }

            return File.Exists(Path.Combine(dir, id + ".enabled"));
        }

        private static bool ConsiderDiskName(string fileName, string id)
        {
            if (fileName.Equals("bugtopia.dll", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (id.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static bool TryLoadFile(string dllPath, bool force, out string error)
        {
            string id = Path.GetFileNameWithoutExtension(dllPath);
            error = null;
            if (!IsPluginId(id))
            {
                error = "id is not a safe file name";
                DiskErrors[id] = error;
                return false;
            }

            if (Loaded.ContainsKey(id))
            {
                error = "already loaded";
                return false;
            }

            byte[] dll;
            try
            {
                dll = File.ReadAllBytes(dllPath);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                DiskErrors[id] = error;
                ModLogger.Warning("[Plugin] '" + id + "' could not be read: " + error);
                return false;
            }

            string sha = Sha256Hex(dll);
            if (!force && IsQuarantined(sha))
            {
                error = "quarantined";
                DiskErrors[id] = error;
                ModLogger.Warning("[Plugin] '" + id + "' is quarantined (sha " + sha.Substring(0, Math.Min(12, sha.Length))
                    + "). Enable it again from the Plugins tab.");
                return false;
            }

            if (force)
            {
                Unquarantine(sha);
            }

            byte[] pdb = null;
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            if (File.Exists(pdbPath))
            {
                try
                {
                    pdb = File.ReadAllBytes(pdbPath);
                }
                catch
                {
                }
            }

            bool ok = TryLoad(id, dll, pdb, PluginSource.Disk, out error, out List<string> violations);
            if (!ok)
            {
                string detail = error ?? "load failed";
                if (violations != null && violations.Count > 0)
                {
                    detail = detail + ": " + string.Join("; ", violations);
                }

                DiskErrors[id] = detail;
                ModLogger.Warning("[Plugin] '" + id + "' did not load: " + detail);
                return false;
            }

            DiskErrors.Remove(id);
            return true;
        }

        internal static bool IsPluginId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64)
            {
                return false;
            }

            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ReadPreviousResident()
        {
            string path = Path.Combine(Folder(), ResidentName);
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text) || text.IndexOf("\"sha\"", StringComparison.Ordinal) < 0)
                {
                    return;
                }

                bool killed = text.IndexOf("\"quitSignal\":true", StringComparison.Ordinal) >= 0;
                List<string> shas = ExtractJsonStrings(text, "sha");
                PreviousResidentJson = text;
                PreviousSessionHadResident = true;
                PreviousSessionKilled = killed;
                if (killed)
                {
                    ModLogger.Warning("[Plugin] the previous session was killed with " + shas.Count
                        + " plugin(s) resident. Those bytes are quarantined.");
                    for (int i = 0; i < shas.Count; i++)
                    {
                        Quarantine(shas[i], "was resident and ticking when the previous session was killed");
                    }
                }
                else
                {
                    ModLogger.Msg("[Plugin] previous resident record had no quit signal — not quarantining.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Plugin] could not read the resident record: " + ex.Message);
            }

            WriteResidentRaw("{\"quitSignal\":" + (McpQuitSignal.Armed ? "true" : "false") + ",\"plugins\":[]}");
        }

        private static List<string> ExtractJsonStrings(string text, string key)
        {
            List<string> found = new List<string>();
            string needle = "\"" + key + "\":\"";
            int from = 0;
            while (from < text.Length)
            {
                int at = text.IndexOf(needle, from, StringComparison.Ordinal);
                if (at < 0)
                {
                    break;
                }

                int start = at + needle.Length;
                int end = text.IndexOf('"', start);
                if (end < 0)
                {
                    break;
                }

                found.Add(text.Substring(start, end - start));
                from = end + 1;
            }

            return found;
        }

        private static void EnsureQuarantineLoaded()
        {
            if (quarantineLoaded)
            {
                return;
            }

            quarantineLoaded = true;
            string path = Path.Combine(Folder(), QuarantineName);
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                    {
                        continue;
                    }

                    QuarantineLines.Add(lines[i]);
                    int tab = lines[i].IndexOf('\t');
                    QuarantinedShas.Add(tab > 0 ? lines[i].Substring(0, tab) : lines[i].Trim());
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Plugin] quarantine list unreadable: " + ex.Message);
            }
        }

        private static void SaveQuarantine()
        {
            try
            {
                File.WriteAllLines(Path.Combine(Folder(), QuarantineName), QuarantineLines, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static void EnsureFolder()
        {
            string dir = Folder();
            string readme = Path.Combine(dir, ReadmeName);
            if (File.Exists(readme))
            {
                return;
            }

            try
            {
                File.WriteAllText(readme,
                    "Bugtopia plugins\r\n"
                    + "\r\n"
                    + "Drop a net6.0 dll here (and its pdb, same name).\r\n"
                    + "The file name without .dll is the plugin id.\r\n"
                    + "Nothing loads until you turn it on in the Plugins tab, which writes <id>.enabled.\r\n"
                    + "A sibling file <id>.disabled skips that dll.\r\n"
                    + "State is stored in state/<id>.json.\r\n"
                    + "quarantine.json lists hashes that crashed a previous session; clear a line\r\n"
                    + "or use Load anyway on the Plugins tab.\r\n"
                    + "See docs/PLUGINS.md in the mod repository.\r\n",
                    new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        internal static string FolderPath()
        {
            return Folder();
        }

        private static string Folder()
        {
            return HelperPaths.GetDirectory("Plugins");
        }

        private static void WriteResidentRaw(string json)
        {
            try
            {
                File.WriteAllText(Path.Combine(Folder(), ResidentName), json ?? "{}", new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static bool WriteStateFile(string id, IDictionary<string, string> state)
        {
            try
            {
                string dir = HelperPaths.GetDirectory("Plugins", "state");
                StringBuilder sb = new StringBuilder(128);
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, string> pair in state)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    sb.Append('"').Append(JsonEscape(pair.Key)).Append("\":\"");
                    sb.Append(JsonEscape(pair.Value)).Append('"');
                }

                sb.Append('}');
                File.WriteAllText(Path.Combine(dir, id + ".json"), sb.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Warning("[Plugin] could not save state for '" + id + "': " + ex.Message);
                return false;
            }
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    default:
                        sb.Append(c < 32 ? ' ' : c);
                        break;
                }
            }

            return sb.ToString();
        }

        private static bool IsStateToken(string value)
        {
            if (value == null)
            {
                return true;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\n' || value[i] == '\r')
                {
                    return false;
                }
            }

            return true;
        }

        private static void RefuseStateOnce(string id)
        {
            if (stateRefusalLogged)
            {
                return;
            }

            stateRefusalLogged = true;
            ModLogger.Warning("[Plugin] '" + id + "' state refused a newline. The previous value was kept.");
        }

        // Flat string map. A newline in a key or value is refused so the file stays one JSON object.
        private sealed class StateMap : IDictionary<string, string>
        {
            private readonly Dictionary<string, string> inner =
                new Dictionary<string, string>(StringComparer.Ordinal);

            private readonly string id;

            internal bool Dirty;

            private StateMap(string id)
            {
                this.id = id;
            }

            internal static StateMap Load(string id)
            {
                StateMap map = new StateMap(id);
                try
                {
                    string path = Path.Combine(HelperPaths.GetDirectory("Plugins", "state"), id + ".json");
                    if (File.Exists(path))
                    {
                        Parse(File.ReadAllText(path), map.inner);
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Warning("[Plugin] state for '" + id + "' unreadable: " + ex.Message);
                }

                return map;
            }

            internal void Forget()
            {
                this.inner.Clear();
                this.Dirty = false;
            }

            public string this[string key]
            {
                get => this.inner[key];
                set
                {
                    if (!IsStateToken(key) || !IsStateToken(value))
                    {
                        RefuseStateOnce(this.id);
                        return;
                    }

                    this.inner[key] = value ?? string.Empty;
                    this.Dirty = true;
                }
            }

            public ICollection<string> Keys => this.inner.Keys;

            public ICollection<string> Values => this.inner.Values;

            public int Count => this.inner.Count;

            public bool IsReadOnly => false;

            public void Add(string key, string value) => this[key] = value;

            public void Add(KeyValuePair<string, string> item) => this[item.Key] = item.Value;

            public void Clear()
            {
                if (this.inner.Count == 0)
                {
                    return;
                }

                this.inner.Clear();
                this.Dirty = true;
            }

            public bool Contains(KeyValuePair<string, string> item) =>
                this.inner.TryGetValue(item.Key, out string value) && string.Equals(value, item.Value, StringComparison.Ordinal);

            public bool ContainsKey(string key) => this.inner.ContainsKey(key);

            public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
            {
                ((ICollection<KeyValuePair<string, string>>)this.inner).CopyTo(array, arrayIndex);
            }

            public bool Remove(string key)
            {
                if (!this.inner.Remove(key))
                {
                    return false;
                }

                this.Dirty = true;
                return true;
            }

            public bool Remove(KeyValuePair<string, string> item)
            {
                if (!this.Contains(item))
                {
                    return false;
                }

                return this.Remove(item.Key);
            }

            public bool TryGetValue(string key, out string value) => this.inner.TryGetValue(key, out value);

            public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => this.inner.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

            private static void Parse(string text, Dictionary<string, string> into)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                int i = 0;
                Skip(text, ref i);
                if (i >= text.Length || text[i] != '{')
                {
                    return;
                }

                i++;
                while (i < text.Length)
                {
                    Skip(text, ref i);
                    if (i >= text.Length || text[i] == '}')
                    {
                        return;
                    }

                    if (text[i] == ',')
                    {
                        i++;
                        continue;
                    }

                    string key = ReadString(text, ref i);
                    Skip(text, ref i);
                    if (i >= text.Length || text[i] != ':')
                    {
                        return;
                    }

                    i++;
                    Skip(text, ref i);
                    string value = ReadString(text, ref i);
                    if (key != null && IsStateToken(key) && IsStateToken(value))
                    {
                        into[key] = value ?? string.Empty;
                    }
                }
            }

            private static void Skip(string text, ref int i)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
            }

            private static string ReadString(string text, ref int i)
            {
                if (i >= text.Length || text[i] != '"')
                {
                    return null;
                }

                i++;
                StringBuilder sb = new StringBuilder();
                while (i < text.Length)
                {
                    char c = text[i++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }

                    if (c == '\\' && i < text.Length)
                    {
                        char n = text[i++];
                        switch (n)
                        {
                            case '"':
                            case '\\':
                                sb.Append(n);
                                break;
                            case 'n':
                                sb.Append('\n');
                                break;
                            case 'r':
                                sb.Append('\r');
                                break;
                            default:
                                sb.Append(n);
                                break;
                        }

                        continue;
                    }

                    sb.Append(c);
                }

                return null;
            }
        }
    }
}
