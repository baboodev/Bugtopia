using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HeartopiaMod
{
    // Music Player (Music tab): plays RECM .bin note timelines — tools/bgm_to_bin.py output or the
    // game's own gramophone recordings (<persistentDataPath>/record/...). Two output modes:
    //   * Local  — post each note's Wwise event on the local player GameObject (only we hear it).
    //   * Network — stream PlayInstrumentNetworkCommand batches via MusicProtocolManager, so other
    //     clients play the notes at our player entity (docs: memory/instrument-play-protocol.md).
    //     Server-side relay validation is UNKNOWN — the "Test network echo" probe answers it: the
    //     server echo (PlayInstrumentNetworkEvent) is re-dispatched locally as the EventCenter event
    //     InstrumentInfoFromServer even for our own notes, so seeing our playerNetId there proves
    //     the server accepted and relayed the command.
    // All scheduling runs on the Unity main thread inside OnUpdate: the game's own gramophone
    // playback (AudioPlaybackComponent) is frame-quantized the same way, and NetworkClient's send
    // buffers are not thread-safe (memory/recm-bin-playback-threading.md) — no background thread.
    public partial class HeartopiaComplete
    {
        private struct MusicPlayerEvent
        {
            public float Time;
            public int NoteId;
            public byte InstrumentType;
            public bool IsStart;
        }

        private sealed class MusicPlayerTrack
        {
            public string Path;
            public string Name;
            public float Duration;
            public int EventCount;
            public string InstrumentsLabel;
            public byte PrimaryInstrumentType;
        }

        private const int MusicPlayerRecmMagic = 1380270916; // "RECM"
        private const short MusicPlayerRecmVersion = 1;
        private const int MusicPlayerRecmHeaderBytes = 10;   // magic(4) + version(2) + count(4)
        private const int MusicPlayerRecmEventBytes = 26;    // seq(4) time(4) playerId(8) type(1) isStart(1) noteId(4) distance(4)
        private const int MusicPlayerMaxEvents = 500000;
        // Note-ons that came due more than this long ago (frame hitch catch-up) are dropped instead
        // of burst-played; matching note-offs are then skipped via the held-notes set. The game's own
        // gramophone player bursts them — dropping sounds better for a live "performance".
        private const float MusicPlayerStaleNoteOnSeconds = 0.15f;
        // PlayInstrumentNetworkCommand caps PressingKeys/ReleasingKeys at 64 entries.
        private const int MusicPlayerMaxKeysPerCommand = 64;
        private const float MusicPlayerLoopGapSeconds = 1.0f;
        private const string MusicPlayerEchoEventName = "XDTDataAndProtocol.Events.InstrumentInfoFromServer";
        // We only read PlayInstrumentData.playerNetId at offset 0; 24 bytes stays clear of the
        // List<int> reference fields further in the struct.
        private const int MusicPlayerEchoEventBytes = 24;

        private static readonly Dictionary<byte, string> MusicPlayerInstrumentNames = new Dictionary<byte, string>
        {
            { 1, "Piano" }, { 2, "Conga" }, { 3, "Cajon" }, { 4, "BaYinTong" }, { 5, "EtherealDrum" },
            { 11, "Lute" }, { 12, "WoodenBass" }, { 13, "Recorder" }, { 14, "Concertina" },
            { 15, "BambooXiao" }, { 16, "Kalimba" }, { 17, "Lyre" }, { 18, "Bagpipe" },
            { 19, "Cello" }, { 20, "Violin" }, { 21, "Saxophone" }
        };

        // ---- config-backed (KeybindConfigData) ----
        private bool musicPlayerLoop;
        private bool musicPlayerNetworkMode;
        private bool musicPlayerSourceGameRecords;
        private string musicPlayerSelectedTrackName = string.Empty;

        // ---- catalog ----
        private readonly List<MusicPlayerTrack> musicPlayerTracks = new List<MusicPlayerTrack>();
        private bool musicPlayerCatalogScanned;
        private string musicPlayerCatalogStatus = string.Empty;
        private int musicPlayerSelectedIndex = -1;

        // ---- playback ----
        private bool musicPlayerPlaying;
        private readonly System.Diagnostics.Stopwatch musicPlayerClock = new System.Diagnostics.Stopwatch();
        private List<MusicPlayerEvent> musicPlayerEvents;
        private int musicPlayerNextIndex;
        private float musicPlayerClipDuration;
        private bool musicPlayerWaitingLoopRestart;
        private float musicPlayerLoopResumeAt;
        private int musicPlayerLoopsDone;
        // noteId -> instrumentType of the press that is currently held (release/stop bookkeeping).
        private readonly Dictionary<int, byte> musicPlayerHeldNotes = new Dictionary<int, byte>();
        private int musicPlayerNotesPlayed;
        private int musicPlayerNotesDropped;
        private string musicPlayerStatus = string.Empty;
        private float musicPlayerErrorLogThrottleAt;

        // ---- local audio (AkSoundEngine.PostEvent + Musicaudio name cache) ----
        private static MethodInfo musicPlayerAkPostEventMethod;
        private static MethodInfo musicPlayerAkRegisterGameObjMethod;
        private static MethodInfo musicPlayerAkSetObjectPositionMethod;
        private static bool musicPlayerAkPostEventResolveTried;
        private readonly Dictionary<int, ValueTuple<string, string>> musicPlayerNoteNames = new Dictionary<int, ValueTuple<string, string>>();
        private readonly HashSet<int> musicPlayerNoteNameFailed = new HashSet<int>();
        private int musicPlayerAkZeroStreak;
        private bool musicPlayerAkBankWarned;
        private GameObject musicPlayerAkRegisteredGo;

        // ---- Wwise instrument banks (mono AudioManager.LoadStaticBank/UnLoadStaticBank) ----
        // instrumentType -> bank short name (GeneratedSoundBanks/Windows, tools/instrument_banks.json).
        private static readonly Dictionary<byte, string> MusicPlayerInstrumentBanks = new Dictionary<byte, string>
        {
            { 1, "Musictheme_piano" }, { 2, "Musictheme_congaBongos" }, { 3, "Musictheme_cajon" },
            { 4, "Music_BoomWhackers" }, { 5, "Musictheme_hang" }, { 11, "Musictheme_lunghe" },
            { 12, "Musictheme_acousticBass" }, { 13, "Musictheme_sopranoRecorder" }, { 14, "Musictheme_concertina" },
            { 15, "Musictheme_xiao" }, { 16, "Musictheme_Kalimba" }, { 17, "Musictheme_lyre" },
            { 18, "Musictheme_bagpipes" }, { 19, "Musictheme_cello" }, { 20, "Musictheme_violin" },
            { 21, "Musictheme_sax" }
        };

        private IntPtr musicPlayerAudioManagerClass;
        private IntPtr musicPlayerLoadStaticBankMethod;
        private IntPtr musicPlayerUnloadStaticBankMethod;
        private readonly HashSet<string> musicPlayerLoadedStaticBanks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static void MusicPlayerLog(string message)
        {
            ModLogger.Msg("[MusicPlayer] " + message);
        }

        private static void MusicPlayerLogVerbose(string message)
        {
            if (MasterLogMusicPlayer)
            {
                ModLogger.Msg("[MusicPlayer] " + message);
            }
        }

        // ---- network (cached mono pointers; class/method/field ptrs are image-lifetime safe) ----
        private bool musicPlayerNetResolveTried;
        private string musicPlayerNetResolveError = string.Empty;
        private IntPtr musicPlayerProtoClass;
        private IntPtr musicPlayerPlayInstrumentMethod;
        private IntPtr musicPlayerStartPlayMethod;
        private IntPtr musicPlayerEndPlayMethod;
        private IntPtr musicPlayerPlayDataClass;
        private IntPtr musicPlayerFieldPlayerNetId;
        private IntPtr musicPlayerFieldInstrumentNetId;
        private IntPtr musicPlayerFieldLevelObjectNetId;
        private IntPtr musicPlayerFieldType;
        private IntPtr musicPlayerFieldPressingKeys;
        private IntPtr musicPlayerFieldReleasingKeys;
        private IntPtr musicPlayerIntListClass;
        private IntPtr musicPlayerIntListAddMethod;
        private uint musicPlayerSelfNetId;
        private bool musicPlayerNetSessionOpen;
        private byte musicPlayerNetSessionType;

        // ---- network echo probe ----
        private bool musicPlayerProbeHookRegistered;
        private int musicPlayerProbeStage; // 0 idle, 1 pressed (release pending), 2 released (awaiting echo)
        private float musicPlayerProbeNextActionAt;
        private float musicPlayerProbeDeadline;
        private int musicPlayerProbeNoteId;
        private byte musicPlayerProbeInstrumentType;
        private string musicPlayerProbeResult = string.Empty;
        private bool musicPlayerProbeEchoSeen;
        private bool musicPlayerProbeForeignEventSeen;

        // ==================== catalog ====================

        private string MusicPlayerGetModMusicDir()
        {
            try
            {
                return HelperPaths.GetDirectory("Music");
            }
            catch
            {
                return null;
            }
        }

        private string MusicPlayerGetGameRecordsDir()
        {
            try
            {
                string root = Application.persistentDataPath;
                if (string.IsNullOrEmpty(root))
                {
                    return null;
                }

                return Path.Combine(root, "record");
            }
            catch
            {
                return null;
            }
        }

        private void MusicPlayerRescanCatalog()
        {
            this.musicPlayerCatalogScanned = true;
            this.musicPlayerTracks.Clear();
            this.musicPlayerSelectedIndex = -1;

            string dir = this.musicPlayerSourceGameRecords
                ? this.MusicPlayerGetGameRecordsDir()
                : this.MusicPlayerGetModMusicDir();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                this.musicPlayerCatalogStatus = "Folder unavailable: " + (dir ?? "<null>");
                return;
            }

            string[] files;
            try
            {
                // Game records live in per-player subfolders (incl. /remote) — scan recursively.
                files = Directory.GetFiles(dir, "*.bin", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                this.musicPlayerCatalogStatus = "Scan failed: " + ex.Message;
                return;
            }

            foreach (string file in files)
            {
                MusicPlayerTrack track = MusicPlayerTryReadTrackInfo(file);
                if (track != null)
                {
                    this.musicPlayerTracks.Add(track);
                }
            }

            this.musicPlayerTracks.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            this.musicPlayerCatalogStatus = this.musicPlayerTracks.Count + " track(s) in " + dir;
            MusicPlayerLogVerbose("Catalog: " + files.Length + " .bin file(s) scanned, " + this.musicPlayerTracks.Count + " valid RECM track(s) in " + dir);

            if (!string.IsNullOrEmpty(this.musicPlayerSelectedTrackName))
            {
                for (int i = 0; i < this.musicPlayerTracks.Count; i++)
                {
                    if (string.Equals(this.musicPlayerTracks[i].Name, this.musicPlayerSelectedTrackName, StringComparison.OrdinalIgnoreCase))
                    {
                        this.musicPlayerSelectedIndex = i;
                        break;
                    }
                }
            }
        }

        // Header + trailing-duration + sampled instrument set; no full event materialization.
        private static MusicPlayerTrack MusicPlayerTryReadTrackInfo(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (fs.Length < MusicPlayerRecmHeaderBytes + 4)
                    {
                        return null;
                    }

                    if (br.ReadInt32() != MusicPlayerRecmMagic || br.ReadInt16() != MusicPlayerRecmVersion)
                    {
                        return null;
                    }

                    int count = br.ReadInt32();
                    long expected = MusicPlayerRecmHeaderBytes + (long)count * MusicPlayerRecmEventBytes + 4;
                    if (count < 0 || count > MusicPlayerMaxEvents || fs.Length < expected)
                    {
                        return null;
                    }

                    var types = new HashSet<byte>();
                    int sample = Math.Min(count, 400);
                    for (int i = 0; i < sample; i++)
                    {
                        fs.Position = MusicPlayerRecmHeaderBytes + (long)i * MusicPlayerRecmEventBytes + 16;
                        types.Add(br.ReadByte());
                    }

                    fs.Position = expected - 4;
                    float duration = br.ReadSingle();
                    if (float.IsNaN(duration) || duration <= 0f || duration > 4f * 3600f)
                    {
                        duration = 0f;
                    }

                    byte primary = 0;
                    var names = new List<string>();
                    foreach (byte t in types.OrderBy(v => v))
                    {
                        if (primary == 0)
                        {
                            primary = t;
                        }

                        names.Add(MusicPlayerInstrumentNames.TryGetValue(t, out string n) ? n : ("Type" + t));
                    }

                    return new MusicPlayerTrack
                    {
                        Path = path,
                        Name = Path.GetFileNameWithoutExtension(path),
                        Duration = duration,
                        EventCount = count,
                        InstrumentsLabel = names.Count > 0 ? string.Join("+", names) : "?",
                        PrimaryInstrumentType = primary
                    };
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool MusicPlayerTryLoadTrackEvents(string path, out List<MusicPlayerEvent> events, out float duration, out string error)
        {
            events = null;
            duration = 0f;
            error = string.Empty;
            try
            {
                using (FileStream fs = File.OpenRead(path))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (br.ReadInt32() != MusicPlayerRecmMagic || br.ReadInt16() != MusicPlayerRecmVersion)
                    {
                        error = "not a RECM v1 file";
                        return false;
                    }

                    int count = br.ReadInt32();
                    if (count < 0 || count > MusicPlayerMaxEvents)
                    {
                        error = "bad event count " + count;
                        return false;
                    }

                    var list = new List<MusicPlayerEvent>(count);
                    float lastTime = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        br.ReadUInt32();            // seq
                        float time = br.ReadSingle();
                        br.ReadInt64();             // playerId (gramophone attribute, unused here)
                        byte instrumentType = br.ReadByte();
                        bool isStart = br.ReadBoolean();
                        int noteId = br.ReadInt32();
                        br.ReadSingle();            // distance (gramophone attribute, unused here)
                        list.Add(new MusicPlayerEvent { Time = time, NoteId = noteId, InstrumentType = instrumentType, IsStart = isStart });
                        if (time > lastTime)
                        {
                            lastTime = time;
                        }
                    }

                    duration = fs.Position + 4 <= fs.Length ? br.ReadSingle() : 0f;
                    if (float.IsNaN(duration) || duration <= 0f)
                    {
                        duration = lastTime + 1f;
                    }

                    // OrderBy is stable: same-time press/release pairs keep their file order.
                    events = list.OrderBy(e => e.Time).ToList();
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ==================== playback control ====================

        private void MusicPlayerStart()
        {
            if (this.musicPlayerSelectedIndex < 0 || this.musicPlayerSelectedIndex >= this.musicPlayerTracks.Count)
            {
                this.musicPlayerStatus = "No track selected";
                return;
            }

            MusicPlayerTrack track = this.musicPlayerTracks[this.musicPlayerSelectedIndex];
            if (!MusicPlayerTryLoadTrackEvents(track.Path, out List<MusicPlayerEvent> events, out float duration, out string error))
            {
                this.musicPlayerStatus = "Load failed: " + error;
                return;
            }

            if (events.Count == 0)
            {
                this.musicPlayerStatus = "Track has no events";
                return;
            }

            this.musicPlayerNoteNameFailed.Clear(); // retry unresolved notes on every fresh play
            this.musicPlayerEvents = events;
            this.musicPlayerClipDuration = duration;
            this.musicPlayerNextIndex = 0;
            this.musicPlayerNotesPlayed = 0;
            this.musicPlayerNotesDropped = 0;
            this.musicPlayerLoopsDone = 0;
            this.musicPlayerWaitingLoopRestart = false;
            this.musicPlayerHeldNotes.Clear();
            this.musicPlayerAkZeroStreak = 0;
            this.musicPlayerAkBankWarned = false;

            FeatureLog.Life("MusicPlayer", "playing '" + track.Name + "' (" + events.Count + " note event(s))");
            MusicPlayerLogVerbose("Start '" + track.Name + "': events=" + events.Count + " duration=" + this.musicPlayerClipDuration.ToString("F1")
                + "s instruments=" + track.InstrumentsLabel + " mode=" + (this.musicPlayerNetworkMode ? "network" : "local"));

            if (this.musicPlayerNetworkMode)
            {
                if (!this.MusicPlayerEnsureNetworkResolved(out string netError))
                {
                    this.musicPlayerStatus = "Network unavailable: " + netError;
                    MusicPlayerLog("Start aborted — " + this.musicPlayerStatus);
                    return;
                }

                if (!this.TryResolveSelfPlayerNetId(out this.musicPlayerSelfNetId) || this.musicPlayerSelfNetId == 0)
                {
                    this.musicPlayerStatus = "Self netId unavailable (enter a world first)";
                    MusicPlayerLog("Start aborted — TryResolveSelfPlayerNetId failed (AuraMono PlayerDataCenter.GetSelfNetPlayerId + managed fallback both missed)");
                    return;
                }

                MusicPlayerLogVerbose("Self playerNetId=" + this.musicPlayerSelfNetId);
            }

            // Both modes play the local echo (in network mode the server does NOT echo our own
            // notes back — LevelInstrumentSystem filters self — so without this we would not hear
            // our own melody, unlike the vanilla instrument panel).
            this.MusicPlayerEnsureBanksLoaded(events);
            this.MusicPlayerPrefetchNoteNames(events);
            MusicPlayerEnsureAkPostEvent(this);
            this.MusicPlayerEnsurePlayerAkRegistered(GetLocalPlayer());

            if (this.musicPlayerNetworkMode)
            {
                this.MusicPlayerSendStartPlaying(track.PrimaryInstrumentType);
            }

            this.musicPlayerClock.Restart();
            this.musicPlayerPlaying = true;
            this.musicPlayerStatus = "Playing: " + track.Name;
            this.AddMenuNotification("Music: " + track.Name, new Color(0.45f, 1f, 0.55f));
        }

        private void MusicPlayerStop(string reason)
        {
            if (!this.musicPlayerPlaying)
            {
                return;
            }

            this.musicPlayerPlaying = false;
            this.musicPlayerClock.Stop();

            // Release everything still held so no note hangs (mirrors VoicePlayer.Dispose /
            // InstrumentPanel._ReleaseAllPressingKeys).
            this.MusicPlayerReleaseAllHeld();

            if (this.musicPlayerNetworkMode && this.musicPlayerNetSessionOpen)
            {
                this.MusicPlayerSendEndPlaying();
            }

            this.MusicPlayerUnloadBanks();
            this.musicPlayerEvents = null;
            this.musicPlayerWaitingLoopRestart = false;
            this.musicPlayerStatus = reason;
            FeatureLog.Life("MusicPlayer", "stopped (" + reason + "), notes played=" + this.musicPlayerNotesPlayed);
            MusicPlayerLogVerbose("Stop (" + reason + "): notes played=" + this.musicPlayerNotesPlayed
                + " dropped=" + this.musicPlayerNotesDropped + " loops=" + this.musicPlayerLoopsDone);
        }

        // ==================== per-frame tick (main thread, called from OnUpdate) ====================

        private void ProcessMusicPlayerOnUpdate()
        {
            try
            {
                this.MusicPlayerProbeTick();

                if (!this.musicPlayerPlaying || this.musicPlayerEvents == null)
                {
                    return;
                }

                if (this.musicPlayerWaitingLoopRestart)
                {
                    if (Time.unscaledTime >= this.musicPlayerLoopResumeAt)
                    {
                        this.musicPlayerWaitingLoopRestart = false;
                        this.musicPlayerNextIndex = 0;
                        this.musicPlayerLoopsDone++;
                        this.musicPlayerClock.Restart();
                    }

                    return;
                }

                float elapsed = (float)this.musicPlayerClock.Elapsed.TotalSeconds;

                if (this.musicPlayerNextIndex < this.musicPlayerEvents.Count
                    && this.musicPlayerEvents[this.musicPlayerNextIndex].Time <= elapsed)
                {
                    this.MusicPlayerDrainDueEvents(elapsed);
                }

                if (elapsed >= this.musicPlayerClipDuration)
                {
                    if (this.musicPlayerLoop)
                    {
                        // Release held notes across the gap, then restart from the top.
                        this.MusicPlayerReleaseAllHeld();
                        this.musicPlayerWaitingLoopRestart = true;
                        this.musicPlayerLoopResumeAt = Time.unscaledTime + MusicPlayerLoopGapSeconds;
                    }
                    else
                    {
                        this.MusicPlayerStop("Finished");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime >= this.musicPlayerErrorLogThrottleAt)
                {
                    this.musicPlayerErrorLogThrottleAt = Time.unscaledTime + 10f;
                    ModLogger.Msg("[MusicPlayer] tick error: " + ex.Message);
                }
            }
        }

        private void MusicPlayerDrainDueEvents(float elapsed)
        {
            // Tracks are usually single-instrument; group per type only when actually mixed.
            List<int> press = null;
            List<int> release = null;
            byte batchType = 0;

            while (this.musicPlayerNextIndex < this.musicPlayerEvents.Count)
            {
                MusicPlayerEvent ev = this.musicPlayerEvents[this.musicPlayerNextIndex];
                if (ev.Time > elapsed)
                {
                    break;
                }

                this.musicPlayerNextIndex++;

                // Flush the batch when the instrument type changes mid-drain (rare, multi-instrument
                // .bin) or a list hits the wire cap.
                bool typeChanges = batchType != 0 && ev.InstrumentType != batchType;
                bool capHit = (press != null && press.Count >= MusicPlayerMaxKeysPerCommand)
                    || (release != null && release.Count >= MusicPlayerMaxKeysPerCommand);
                if (typeChanges || capHit)
                {
                    this.MusicPlayerDispatchBatch(batchType, press, release);
                    press = null;
                    release = null;
                }

                batchType = ev.InstrumentType;

                if (ev.IsStart)
                {
                    if (elapsed - ev.Time > MusicPlayerStaleNoteOnSeconds)
                    {
                        // Frame-hitch catch-up: drop stale note-ons instead of burst-playing them.
                        this.musicPlayerNotesDropped++;
                        continue;
                    }

                    if (this.musicPlayerHeldNotes.ContainsKey(ev.NoteId))
                    {
                        continue;
                    }

                    this.musicPlayerHeldNotes[ev.NoteId] = ev.InstrumentType;
                    this.musicPlayerNotesPlayed++;
                    (press ??= new List<int>()).Add(ev.NoteId);
                }
                else
                {
                    // Only release notes we actually pressed (skips releases of dropped note-ons).
                    if (!this.musicPlayerHeldNotes.Remove(ev.NoteId))
                    {
                        continue;
                    }

                    (release ??= new List<int>()).Add(ev.NoteId);
                }
            }

            this.MusicPlayerDispatchBatch(batchType, press, release);
        }

        private void MusicPlayerDispatchBatch(byte instrumentType, List<int> press, List<int> release)
        {
            if ((press == null || press.Count == 0) && (release == null || release.Count == 0))
            {
                return;
            }

            MusicPlayerLogVerbose("batch type=" + instrumentType
                + " press=" + (press != null ? press.Count : 0)
                + " release=" + (release != null ? release.Count : 0));

            if (this.musicPlayerNetworkMode)
            {
                this.MusicPlayerSendPlayCommand(instrumentType, press, release);
            }

            // Local echo in BOTH modes: the server never plays our own notes back to us.
            this.MusicPlayerEnsurePlayerAkRegistered(GetLocalPlayer());
            if (press != null)
            {
                for (int i = 0; i < press.Count; i++)
                {
                    this.MusicPlayerPostLocalNote(press[i], true);
                }
            }

            if (release != null)
            {
                for (int i = 0; i < release.Count; i++)
                {
                    this.MusicPlayerPostLocalNote(release[i], false);
                }
            }
        }

        private void MusicPlayerReleaseAllHeld()
        {
            if (this.musicPlayerHeldNotes.Count == 0)
            {
                return;
            }

            // Local stop for our own ears always; wire releases additionally in network mode.
            foreach (KeyValuePair<int, byte> held in this.musicPlayerHeldNotes)
            {
                this.MusicPlayerPostLocalNote(held.Key, false);
            }

            if (this.musicPlayerNetworkMode)
            {
                foreach (var group in this.musicPlayerHeldNotes.GroupBy(kv => kv.Value))
                {
                    this.MusicPlayerSendPlayCommand(group.Key, null, group.Select(kv => kv.Key).ToList());
                }
            }

            this.musicPlayerHeldNotes.Clear();
        }

        // ==================== local audio ====================

        private static void MusicPlayerEnsureAkPostEvent(HeartopiaComplete host)
        {
            if (musicPlayerAkPostEventResolveTried)
            {
                return;
            }

            musicPlayerAkPostEventResolveTried = true;
            try
            {
                Type akType = host.FindLoadedType("AkSoundEngine", "Il2CppAkSoundEngine");
                if (akType == null)
                {
                    MusicPlayerLog("AkSoundEngine type NOT found in loaded assemblies — local playback unavailable");
                    return;
                }

                musicPlayerAkPostEventMethod = akType.GetMethod(
                    "PostEvent",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(GameObject) },
                    null);
                musicPlayerAkRegisterGameObjMethod = akType.GetMethod(
                    "RegisterGameObj",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(GameObject) },
                    null);
                musicPlayerAkSetObjectPositionMethod = akType.GetMethod(
                    "SetObjectPosition",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(GameObject), typeof(Transform) },
                    null);
                MusicPlayerLogVerbose("AkSoundEngine resolved: PostEvent=" + (musicPlayerAkPostEventMethod != null)
                    + " RegisterGameObj=" + (musicPlayerAkRegisterGameObjMethod != null)
                    + " SetObjectPosition=" + (musicPlayerAkSetObjectPositionMethod != null));
            }
            catch (Exception ex)
            {
                musicPlayerAkPostEventMethod = null;
                MusicPlayerLog("AkSoundEngine resolve error: " + ex.Message);
            }
        }

        // Register the player GameObject with the Wwise engine and pin its 3D position so notes
        // posted on it are audible (an unregistered GO defaults to the world origin — attenuated
        // to silence). The game normally does this via XDTAudioManager.PostEvent's native side.
        private void MusicPlayerEnsurePlayerAkRegistered(GameObject player)
        {
            if (player == null)
            {
                return;
            }

            try
            {
                if (!ReferenceEquals(this.musicPlayerAkRegisteredGo, player) && musicPlayerAkRegisterGameObjMethod != null)
                {
                    object result = musicPlayerAkRegisterGameObjMethod.Invoke(null, new object[] { player });
                    this.musicPlayerAkRegisteredGo = player;
                    MusicPlayerLogVerbose("RegisterGameObj(player) => " + (result?.ToString() ?? "null"));
                }

                if (musicPlayerAkSetObjectPositionMethod != null)
                {
                    musicPlayerAkSetObjectPositionMethod.Invoke(null, new object[] { player, player.transform });
                }
            }
            catch (Exception ex)
            {
                MusicPlayerLogVerbose("AK register/position error: " + ex.Message);
            }
        }

        // ==================== Wwise instrument banks ====================

        private bool MusicPlayerEnsureAudioManagerResolved(out string error)
        {
            error = string.Empty;
            if (this.musicPlayerLoadStaticBankMethod != IntPtr.Zero && this.musicPlayerUnloadStaticBankMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoStringNew == null || auraMonoClassFromName == null)
            {
                error = "AuraMono API not ready";
                return false;
            }

            if (this.musicPlayerAudioManagerClass == IntPtr.Zero)
            {
                IntPtr image = this.FindAuraMonoImage(new[] { "XDTViewBase", "XDTViewBase.dll" });
                this.musicPlayerAudioManagerClass = image != IntPtr.Zero
                    ? auraMonoClassFromName(image, "ScriptsRefactory.ViewBase.Audio", "AudioManager")
                    : IntPtr.Zero;
                if (this.musicPlayerAudioManagerClass == IntPtr.Zero)
                {
                    this.musicPlayerAudioManagerClass = this.FindAuraMonoClassByFullName("ScriptsRefactory.ViewBase.Audio.AudioManager");
                }
            }

            if (this.musicPlayerAudioManagerClass == IntPtr.Zero)
            {
                error = "AudioManager class not found (XDTViewBase image loads in-world)";
                return false;
            }

            if (this.musicPlayerLoadStaticBankMethod == IntPtr.Zero)
            {
                this.musicPlayerLoadStaticBankMethod = this.FindAuraMonoMethodOnHierarchy(this.musicPlayerAudioManagerClass, "LoadStaticBank", 1);
            }

            if (this.musicPlayerUnloadStaticBankMethod == IntPtr.Zero)
            {
                this.musicPlayerUnloadStaticBankMethod = this.FindAuraMonoMethodOnHierarchy(this.musicPlayerAudioManagerClass, "UnLoadStaticBank", 1);
            }

            if (this.musicPlayerLoadStaticBankMethod == IntPtr.Zero)
            {
                error = "AudioManager.LoadStaticBank not resolved";
                return false;
            }

            return true;
        }

        // Loads the Wwise sound banks for every instrument type present in the track via the game's
        // own AudioManager.LoadStaticBank (mono static, string arg). Without the bank loaded,
        // AkSoundEngine.PostEvent returns playingId 0 and the note is silent — the game normally
        // loads these banks lazily through AudioManager.PlaySound, which our raw PostEvent bypasses.
        private unsafe void MusicPlayerEnsureBanksLoaded(List<MusicPlayerEvent> events)
        {
            var types = new HashSet<byte>();
            for (int i = 0; i < events.Count; i++)
            {
                types.Add(events[i].InstrumentType);
            }

            if (!this.MusicPlayerEnsureAudioManagerResolved(out string error))
            {
                MusicPlayerLog("Bank load unavailable: " + error);
                this.musicPlayerStatus = "Bank load unavailable: " + error;
                return;
            }

            IntPtr* args = stackalloc IntPtr[1];
            foreach (byte type in types)
            {
                if (!MusicPlayerInstrumentBanks.TryGetValue(type, out string bankName))
                {
                    MusicPlayerLog("No bank mapping for instrumentType " + type + " — notes of this type may be silent");
                    continue;
                }

                if (this.musicPlayerLoadedStaticBanks.Contains(bankName))
                {
                    continue;
                }

                IntPtr bankNameObj = auraMonoStringNew(this.auraMonoRootDomain, bankName);
                if (bankNameObj == IntPtr.Zero)
                {
                    MusicPlayerLog("Bank '" + bankName + "': mono_string_new failed");
                    continue;
                }

                args[0] = bankNameObj;
                if (!TryAuraInvoke(this.musicPlayerLoadStaticBankMethod, IntPtr.Zero, (IntPtr)args, out IntPtr boxedResult, out string invokeError))
                {
                    MusicPlayerLog("Bank '" + bankName + "': LoadStaticBank invoke failed: " + invokeError);
                    continue;
                }

                bool loaded = boxedResult != IntPtr.Zero && this.TryUnboxMonoBoolean(boxedResult, out bool ok) && ok;
                if (loaded)
                {
                    MusicPlayerLogVerbose("Bank '" + bankName + "' (type " + type + ") loaded");
                    this.musicPlayerLoadedStaticBanks.Add(bankName);
                }
                else
                {
                    // A refused load means silent notes — always worth a log line.
                    MusicPlayerLog("Bank '" + bankName + "' (type " + type + ") load returned false");
                }
            }
        }

        private unsafe void MusicPlayerUnloadBanks()
        {
            if (this.musicPlayerLoadedStaticBanks.Count == 0 || this.musicPlayerUnloadStaticBankMethod == IntPtr.Zero)
            {
                this.musicPlayerLoadedStaticBanks.Clear();
                return;
            }

            IntPtr* args = stackalloc IntPtr[1];
            foreach (string bankName in this.musicPlayerLoadedStaticBanks)
            {
                IntPtr bankNameObj = auraMonoStringNew != null ? auraMonoStringNew(this.auraMonoRootDomain, bankName) : IntPtr.Zero;
                if (bankNameObj == IntPtr.Zero)
                {
                    continue;
                }

                args[0] = bankNameObj;
                TryAuraInvoke(this.musicPlayerUnloadStaticBankMethod, IntPtr.Zero, (IntPtr)args, out _, out _);
                MusicPlayerLogVerbose("Bank '" + bankName + "' unloaded");
            }

            this.musicPlayerLoadedStaticBanks.Clear();
        }

        private void MusicPlayerPostLocalNote(int noteId, bool isPress)
        {
            if (!this.musicPlayerNoteNames.TryGetValue(noteId, out ValueTuple<string, string> names))
            {
                if (!this.MusicPlayerResolveNoteNames(noteId, out names))
                {
                    MusicPlayerLogVerbose("note " + noteId + ": event name unresolved — skipped");
                    return;
                }
            }

            string eventName = isPress ? names.Item1 : names.Item2;
            if (string.IsNullOrEmpty(eventName))
            {
                return;
            }

            MusicPlayerEnsureAkPostEvent(this);
            if (musicPlayerAkPostEventMethod == null)
            {
                return;
            }

            GameObject player = GetLocalPlayer();
            if (player == null)
            {
                MusicPlayerLogVerbose("note " + noteId + ": local player GameObject not found — skipped");
                return;
            }

            try
            {
                object result = musicPlayerAkPostEventMethod.Invoke(null, new object[] { eventName, player });
                uint playingId = result is uint u ? u : 0u;
                MusicPlayerLogVerbose("PostEvent '" + eventName + "' => playingId " + playingId);
                if (isPress)
                {
                    if (playingId == 0)
                    {
                        this.musicPlayerAkZeroStreak++;
                        if (this.musicPlayerAkZeroStreak >= 8 && !this.musicPlayerAkBankWarned)
                        {
                            this.musicPlayerAkBankWarned = true;
                            this.musicPlayerStatus = "Wwise events not firing — see log (bank load results above)";
                            MusicPlayerLog("PostEvent returned playingId 0 for 8+ presses in a row — the instrument bank is not loaded or the event name is unknown to the engine");
                        }
                    }
                    else
                    {
                        this.musicPlayerAkZeroStreak = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime >= this.musicPlayerErrorLogThrottleAt)
                {
                    this.musicPlayerErrorLogThrottleAt = Time.unscaledTime + 10f;
                    MusicPlayerLog("PostEvent error: " + ex.Message);
                }
            }
        }

        private void MusicPlayerPrefetchNoteNames(List<MusicPlayerEvent> events)
        {
            var unique = new HashSet<int>();
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].IsStart)
                {
                    unique.Add(events[i].NoteId);
                }
            }

            int failed = 0;
            foreach (int noteId in unique)
            {
                if (!this.musicPlayerNoteNames.ContainsKey(noteId)
                    && !this.MusicPlayerResolveNoteNames(noteId, out _))
                {
                    failed++;
                }
            }

            if (failed > 0)
            {
                MusicPlayerLog("Note-name prefetch: " + (unique.Count - failed) + "/" + unique.Count + " resolved");
                this.musicPlayerStatus = failed + "/" + unique.Count + " note sounds unresolved (enter a world first?)";
            }
            else
            {
                MusicPlayerLogVerbose("Note-name prefetch: " + unique.Count + "/" + unique.Count + " resolved");
            }
        }

        // noteId -> (playEeventName, stopEventName) via AuraMono TableData.GetMusicaudio. Cached for
        // the session; failures are cached too (retryable only via game restart — table is static).
        private unsafe bool MusicPlayerResolveNoteNames(int noteId, out ValueTuple<string, string> names)
        {
            names = default;
            if (this.musicPlayerNoteNames.TryGetValue(noteId, out names))
            {
                return true;
            }

            if (this.musicPlayerNoteNameFailed.Contains(noteId))
            {
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                MusicPlayerLog("note " + noteId + ": AuraMono API not ready");
                return false;
            }

            IntPtr ecsImage = this.FindAuraMonoImage(new[] { "EcsClient", "EcsClient.dll" });
            IntPtr tableDataClass = ecsImage != IntPtr.Zero ? auraMonoClassFromName(ecsImage, string.Empty, "TableData") : IntPtr.Zero;
            if (tableDataClass == IntPtr.Zero)
            {
                MusicPlayerLog("note " + noteId + ": TableData class not found (EcsClient image loads in-world)");
                return false;
            }

            // TableData getters on this build are (int id, bool needException) — resolve 2-param
            // first, 1-param fallback (memory/faceshop-buyall-table-resolve-bugs.md).
            IntPtr getMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetMusicaudio", 2);
            int paramCount = 2;
            if (getMethod == IntPtr.Zero)
            {
                getMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetMusicaudio", 1);
                paramCount = 1;
            }

            if (getMethod == IntPtr.Zero)
            {
                MusicPlayerLog("note " + noteId + ": TableData.GetMusicaudio method not resolved");
                this.musicPlayerNoteNameFailed.Add(noteId);
                return false;
            }

            int id = noteId;
            bool needException = false;
            IntPtr exc = IntPtr.Zero;
            IntPtr row;
            if (paramCount == 2)
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                row = auraMonoRuntimeInvoke(getMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                row = auraMonoRuntimeInvoke(getMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            if (exc != IntPtr.Zero || row == IntPtr.Zero)
            {
                MusicPlayerLog("note " + noteId + ": GetMusicaudio(" + noteId + ") returned "
                    + (exc != IntPtr.Zero ? "exception" : "null row") + " — id not in Musicaudio table?");
                this.musicPlayerNoteNameFailed.Add(noteId);
                return false;
            }

            // Pin the row across the two string reads — each read allocates on the mono side and the
            // sgen GC moves unpinned objects (memory/auramono-sgen-gc-stale-pointer-crashes.md).
            uint pin = AuraMonoPinNew(row);
            try
            {
                this.TryGetMonoStringMember(row, "playEeventName", out string play);
                this.TryGetMonoStringMember(row, "stopEventName", out string stop);
                if (string.IsNullOrEmpty(play))
                {
                    MusicPlayerLog("note " + noteId + ": Musicaudio row has empty playEeventName");
                    this.musicPlayerNoteNameFailed.Add(noteId);
                    return false;
                }

                MusicPlayerLogVerbose("note " + noteId + " => '" + play + "' / '" + (stop ?? string.Empty) + "'");
                names = new ValueTuple<string, string>(play, stop ?? string.Empty);
                this.musicPlayerNoteNames[noteId] = names;
                return true;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        // ==================== network send ====================

        private bool MusicPlayerEnsureNetworkResolved(out string error)
        {
            if (this.musicPlayerPlayInstrumentMethod != IntPtr.Zero && this.musicPlayerPlayDataClass != IntPtr.Zero)
            {
                error = string.Empty;
                return true;
            }

            if (this.musicPlayerNetResolveTried && !string.IsNullOrEmpty(this.musicPlayerNetResolveError))
            {
                // Retry resolves are cheap failures only before the world/images are loaded; allow
                // a fresh attempt each call rather than caching the miss forever.
                this.musicPlayerNetResolveError = string.Empty;
            }

            this.musicPlayerNetResolveTried = true;
            error = string.Empty;

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectNew == null
                || auraMonoFieldSetValue == null || auraMonoObjectUnbox == null
                || auraMonoClassGetFieldFromName == null)
            {
                error = "AuraMono API not ready";
                this.musicPlayerNetResolveError = error;
                MusicPlayerLog("Net resolve failed: " + error);
                return false;
            }

            if (this.musicPlayerProtoClass == IntPtr.Zero)
            {
                this.musicPlayerProtoClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Music.MusicProtocolManager");
            }

            if (this.musicPlayerProtoClass == IntPtr.Zero)
            {
                error = "MusicProtocolManager class not found (enter a world first)";
                this.musicPlayerNetResolveError = error;
                MusicPlayerLog("Net resolve failed: " + error);
                return false;
            }

            if (this.musicPlayerPlayInstrumentMethod == IntPtr.Zero)
            {
                this.musicPlayerPlayInstrumentMethod = this.FindAuraMonoMethodOnHierarchy(this.musicPlayerProtoClass, "PlayInstrument", 1);
            }

            if (this.musicPlayerStartPlayMethod == IntPtr.Zero)
            {
                this.musicPlayerStartPlayMethod = this.FindAuraMonoMethodOnHierarchy(this.musicPlayerProtoClass, "StartPlayInstrument", 3);
            }

            if (this.musicPlayerEndPlayMethod == IntPtr.Zero)
            {
                this.musicPlayerEndPlayMethod = this.FindAuraMonoMethodOnHierarchy(this.musicPlayerProtoClass, "EndPlayInstrument", 2);
            }

            if (this.musicPlayerPlayDataClass == IntPtr.Zero)
            {
                this.musicPlayerPlayDataClass = this.FindAuraMonoClassByFullName("XDT.Scene.Shared.Modules.Music.PlayInstrumentData");
            }

            if (this.musicPlayerPlayInstrumentMethod == IntPtr.Zero || this.musicPlayerPlayDataClass == IntPtr.Zero)
            {
                error = "PlayInstrument/PlayInstrumentData not resolved (method=" + (this.musicPlayerPlayInstrumentMethod != IntPtr.Zero)
                    + " dataClass=" + (this.musicPlayerPlayDataClass != IntPtr.Zero) + ")";
                this.musicPlayerNetResolveError = error;
                MusicPlayerLog("Net resolve failed: " + error);
                return false;
            }

            if (this.musicPlayerFieldPlayerNetId == IntPtr.Zero)
            {
                this.musicPlayerFieldPlayerNetId = auraMonoClassGetFieldFromName(this.musicPlayerPlayDataClass, "playerNetId");
                this.musicPlayerFieldInstrumentNetId = auraMonoClassGetFieldFromName(this.musicPlayerPlayDataClass, "instrumentNetId");
                this.musicPlayerFieldLevelObjectNetId = auraMonoClassGetFieldFromName(this.musicPlayerPlayDataClass, "instrumentLevelObjectNetId");
                this.musicPlayerFieldType = auraMonoClassGetFieldFromName(this.musicPlayerPlayDataClass, "type");
                this.musicPlayerFieldPressingKeys = auraMonoClassGetFieldFromName(this.musicPlayerPlayDataClass, "pressingKeys");
                this.musicPlayerFieldReleasingKeys = auraMonoClassGetFieldFromName(this.musicPlayerPlayDataClass, "releasingKeys");
            }

            if (this.musicPlayerFieldPlayerNetId == IntPtr.Zero || this.musicPlayerFieldType == IntPtr.Zero
                || this.musicPlayerFieldPressingKeys == IntPtr.Zero || this.musicPlayerFieldReleasingKeys == IntPtr.Zero)
            {
                error = "PlayInstrumentData fields not resolved (playerNetId=" + (this.musicPlayerFieldPlayerNetId != IntPtr.Zero)
                    + " type=" + (this.musicPlayerFieldType != IntPtr.Zero)
                    + " pressingKeys=" + (this.musicPlayerFieldPressingKeys != IntPtr.Zero)
                    + " releasingKeys=" + (this.musicPlayerFieldReleasingKeys != IntPtr.Zero) + ")";
                this.musicPlayerNetResolveError = error;
                MusicPlayerLog("Net resolve failed: " + error);
                return false;
            }

            MusicPlayerLogVerbose("Net resolve OK: MusicProtocolManager.PlayInstrument/Start/End + PlayInstrumentData fields cached"
                + " (Start=" + (this.musicPlayerStartPlayMethod != IntPtr.Zero)
                + " End=" + (this.musicPlayerEndPlayMethod != IntPtr.Zero) + ")");
            error = string.Empty;
            return true;
        }

        // Creates a mono-side List<int>. BCL generic instantiation goes through mono-side
        // Type.GetType + Activator.CreateInstance (the proven pattern from GetAuraMonoUInt64ListObject
        // / DailyQuest ItemNetPair list); the resulting class + Add method are cached.
        private unsafe IntPtr MusicPlayerCreateIntList(out string error)
        {
            error = string.Empty;

            if (this.musicPlayerIntListClass != IntPtr.Zero && auraMonoObjectNew != null)
            {
                IntPtr fast = auraMonoObjectNew(this.auraMonoRootDomain, this.musicPlayerIntListClass);
                if (fast != IntPtr.Zero)
                {
                    if (auraMonoRuntimeObjectInit != null)
                    {
                        auraMonoRuntimeObjectInit(fast);
                    }

                    return fast;
                }
            }

            if (auraMonoStringNew == null || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                error = "List<int> factory prerequisites unavailable";
                return IntPtr.Zero;
            }

            IntPtr typeNameStr = auraMonoStringNew(this.auraMonoRootDomain, "System.Collections.Generic.List`1[System.Int32]");
            if (typeNameStr == IntPtr.Zero)
            {
                error = "mono_string_new failed";
                return IntPtr.Zero;
            }

            IntPtr* args = stackalloc IntPtr[1];
            IntPtr exc = IntPtr.Zero;
            args[0] = typeNameStr;
            IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
            {
                error = "Type.GetType(List<int>) failed";
                return IntPtr.Zero;
            }

            exc = IntPtr.Zero;
            args[0] = typeObj;
            IntPtr listObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
            {
                error = "Activator.CreateInstance(List<int>) failed";
                return IntPtr.Zero;
            }

            if (this.musicPlayerIntListClass == IntPtr.Zero && auraMonoObjectGetClass != null)
            {
                this.musicPlayerIntListClass = auraMonoObjectGetClass(listObj);
            }

            return listObj;
        }

        private unsafe bool MusicPlayerListAddInts(IntPtr listObj, List<int> values, out string error)
        {
            error = string.Empty;
            if (values == null || values.Count == 0)
            {
                return true;
            }

            if (this.musicPlayerIntListAddMethod == IntPtr.Zero)
            {
                IntPtr listClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(listObj) : IntPtr.Zero;
                this.musicPlayerIntListAddMethod = listClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(listClass, "Add", 1)
                    : IntPtr.Zero;
            }

            if (this.musicPlayerIntListAddMethod == IntPtr.Zero)
            {
                error = "List<int>.Add not resolved";
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            for (int i = 0; i < values.Count; i++)
            {
                int value = values[i];
                IntPtr exc = IntPtr.Zero;
                args[0] = (IntPtr)(&value);
                auraMonoRuntimeInvoke(this.musicPlayerIntListAddMethod, listObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    error = "List<int>.Add threw";
                    return false;
                }
            }

            return true;
        }

        // MusicProtocolManager.PlayInstrument(PlayInstrumentData) — the game's own non-generic wire
        // wrapper (WebRequestUtility.SendCommand<PlayInstrumentNetworkCommand> inside). Instrument
        // identity is sent as zeros in v1 — receiving clients play notes at the player entity without
        // checking it; whether the SERVER relays zero-id commands is what the probe answers.
        private unsafe bool MusicPlayerSendPlayCommand(byte instrumentType, List<int> press, List<int> release)
        {
            if ((press == null || press.Count == 0) && (release == null || release.Count == 0))
            {
                return true;
            }

            if (!this.MusicPlayerEnsureNetworkResolved(out string error))
            {
                this.musicPlayerStatus = "Net: " + error;
                return false;
            }

            var pins = new List<uint>(3);
            try
            {
                IntPtr pressList = this.MusicPlayerCreateIntList(out error);
                if (pressList == IntPtr.Zero)
                {
                    this.musicPlayerStatus = "Net: " + error;
                    return false;
                }

                pins.Add(AuraMonoPinNew(pressList));

                IntPtr releaseList = this.MusicPlayerCreateIntList(out error);
                if (releaseList == IntPtr.Zero)
                {
                    this.musicPlayerStatus = "Net: " + error;
                    return false;
                }

                pins.Add(AuraMonoPinNew(releaseList));

                if (!this.MusicPlayerListAddInts(pressList, press, out error)
                    || !this.MusicPlayerListAddInts(releaseList, release, out error))
                {
                    this.musicPlayerStatus = "Net: " + error;
                    return false;
                }

                IntPtr boxed = auraMonoObjectNew(this.auraMonoRootDomain, this.musicPlayerPlayDataClass);
                if (boxed == IntPtr.Zero)
                {
                    this.musicPlayerStatus = "Net: PlayInstrumentData alloc failed";
                    return false;
                }

                pins.Add(AuraMonoPinNew(boxed));

                uint playerNetId = this.musicPlayerSelfNetId;
                uint instrumentNetId = 0u;
                ulong levelObjectNetId = 0ul;
                int typeValue = instrumentType;
                auraMonoFieldSetValue(boxed, this.musicPlayerFieldPlayerNetId, (IntPtr)(&playerNetId));
                if (this.musicPlayerFieldInstrumentNetId != IntPtr.Zero)
                {
                    auraMonoFieldSetValue(boxed, this.musicPlayerFieldInstrumentNetId, (IntPtr)(&instrumentNetId));
                }

                if (this.musicPlayerFieldLevelObjectNetId != IntPtr.Zero)
                {
                    auraMonoFieldSetValue(boxed, this.musicPlayerFieldLevelObjectNetId, (IntPtr)(&levelObjectNetId));
                }

                auraMonoFieldSetValue(boxed, this.musicPlayerFieldType, (IntPtr)(&typeValue));
                // Reference-type fields take the object pointer DIRECTLY
                // (memory/auramono-field-set-value-ref-semantics.md).
                auraMonoFieldSetValue(boxed, this.musicPlayerFieldPressingKeys, pressList);
                auraMonoFieldSetValue(boxed, this.musicPlayerFieldReleasingKeys, releaseList);

                IntPtr unboxed = auraMonoObjectUnbox(boxed);
                if (unboxed == IntPtr.Zero)
                {
                    this.musicPlayerStatus = "Net: unbox failed";
                    return false;
                }

                IntPtr* args = stackalloc IntPtr[1];
                args[0] = unboxed;
                if (!TryAuraInvoke(this.musicPlayerPlayInstrumentMethod, IntPtr.Zero, (IntPtr)args, out _, out string invokeError))
                {
                    this.musicPlayerStatus = "Net: PlayInstrument failed: " + invokeError;
                    if (Time.unscaledTime >= this.musicPlayerErrorLogThrottleAt)
                    {
                        this.musicPlayerErrorLogThrottleAt = Time.unscaledTime + 10f;
                        MusicPlayerLog("PlayInstrument invoke failed: " + invokeError);
                    }

                    return false;
                }

                MusicPlayerLogVerbose("PlayInstrument sent: type=" + instrumentType
                    + " press=" + (press != null ? press.Count : 0)
                    + " release=" + (release != null ? release.Count : 0)
                    + " playerNetId=" + this.musicPlayerSelfNetId);
                return true;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private unsafe void MusicPlayerSendStartPlaying(byte instrumentType)
        {
            this.musicPlayerNetSessionOpen = false;
            this.musicPlayerNetSessionType = instrumentType;
            if (this.musicPlayerStartPlayMethod == IntPtr.Zero)
            {
                return;
            }

            int type = instrumentType;
            int staticId = 0;
            int keyOption = 1; // MusicKeyOption.KeyMode15a
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&type);
            args[1] = (IntPtr)(&staticId);
            args[2] = (IntPtr)(&keyOption);
            if (TryAuraInvoke(this.musicPlayerStartPlayMethod, IntPtr.Zero, (IntPtr)args, out _, out string startError))
            {
                this.musicPlayerNetSessionOpen = true;
                MusicPlayerLogVerbose("StartPlayingMusic sent (type=" + instrumentType + ")");
            }
            else
            {
                MusicPlayerLog("StartPlayingMusic failed: " + startError);
            }
        }

        private unsafe void MusicPlayerSendEndPlaying()
        {
            this.musicPlayerNetSessionOpen = false;
            if (this.musicPlayerEndPlayMethod == IntPtr.Zero)
            {
                return;
            }

            int type = this.musicPlayerNetSessionType;
            int keyOption = 1;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&type);
            args[1] = (IntPtr)(&keyOption);
            TryAuraInvoke(this.musicPlayerEndPlayMethod, IntPtr.Zero, (IntPtr)args, out _, out _);
            MusicPlayerLogVerbose("EndPlayingMusic sent (type=" + type + ")");
        }

        // ==================== network echo probe ====================

        private void MusicPlayerRunNetworkProbe()
        {
            if (this.musicPlayerPlaying || this.musicPlayerProbeStage != 0)
            {
                return;
            }

            if (!this.MusicPlayerEnsureNetworkResolved(out string error))
            {
                this.musicPlayerProbeResult = "Probe: " + error;
                MusicPlayerLog("Probe aborted: " + error);
                return;
            }

            if (!this.TryResolveSelfPlayerNetId(out this.musicPlayerSelfNetId) || this.musicPlayerSelfNetId == 0)
            {
                this.musicPlayerProbeResult = "Probe: self netId unavailable";
                MusicPlayerLog("Probe aborted: TryResolveSelfPlayerNetId failed (AuraMono PlayerDataCenter.GetSelfNetPlayerId + managed fallback both missed)");
                return;
            }

            MusicPlayerLog("Probe: self playerNetId=" + this.musicPlayerSelfNetId);

            if (!this.musicPlayerProbeHookRegistered)
            {
                this.musicPlayerProbeHookRegistered = true;
                bool registered = this.RegisterGameEventHook(MusicPlayerEchoEventName, MusicPlayerEchoEventBytes, this.OnMusicPlayerInstrumentEcho);
                MusicPlayerLog("Echo hook register requested (accepted=" + registered + ", installs on next event-engine tick)");
            }

            // Note choice: first note of the selected track, else a piano note that exists on every
            // build (Musicaudio row 10005 — tools/piano2row_map.json).
            this.musicPlayerProbeNoteId = 10005;
            this.musicPlayerProbeInstrumentType = 1;
            if (this.musicPlayerSelectedIndex >= 0 && this.musicPlayerSelectedIndex < this.musicPlayerTracks.Count)
            {
                MusicPlayerTrack track = this.musicPlayerTracks[this.musicPlayerSelectedIndex];
                if (track.PrimaryInstrumentType != 0
                    && MusicPlayerTryLoadTrackEvents(track.Path, out List<MusicPlayerEvent> events, out _, out _))
                {
                    for (int i = 0; i < events.Count; i++)
                    {
                        if (events[i].IsStart)
                        {
                            this.musicPlayerProbeNoteId = events[i].NoteId;
                            this.musicPlayerProbeInstrumentType = events[i].InstrumentType;
                            break;
                        }
                    }
                }
            }

            this.musicPlayerProbeEchoSeen = false;
            this.musicPlayerProbeForeignEventSeen = false;
            this.MusicPlayerSendStartPlaying(this.musicPlayerProbeInstrumentType);
            if (!this.MusicPlayerSendPlayCommand(this.musicPlayerProbeInstrumentType, new List<int> { this.musicPlayerProbeNoteId }, null))
            {
                this.musicPlayerProbeResult = "Probe: send failed — " + this.musicPlayerStatus;
                this.MusicPlayerSendEndPlaying();
                return;
            }

            this.musicPlayerProbeStage = 1;
            this.musicPlayerProbeNextActionAt = Time.unscaledTime + 0.25f;
            this.musicPlayerProbeDeadline = Time.unscaledTime + 4f;
            this.musicPlayerProbeResult = "Probe: note sent, waiting for server echo…";
            MusicPlayerLog("Probe: sent press noteId=" + this.musicPlayerProbeNoteId
                + " type=" + this.musicPlayerProbeInstrumentType + ", awaiting InstrumentInfoFromServer echo");
        }

        private void MusicPlayerProbeTick()
        {
            if (this.musicPlayerProbeStage == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (this.musicPlayerProbeStage == 1 && now >= this.musicPlayerProbeNextActionAt)
            {
                this.MusicPlayerSendPlayCommand(this.musicPlayerProbeInstrumentType, null, new List<int> { this.musicPlayerProbeNoteId });
                this.MusicPlayerSendEndPlaying();
                this.musicPlayerProbeStage = 2;
            }

            if (this.musicPlayerProbeEchoSeen)
            {
                this.musicPlayerProbeStage = 0;
                this.musicPlayerProbeResult = "Probe: ECHO RECEIVED — server relays standless notes; network mode is viable";
                MusicPlayerLog("Probe verdict: ECHO RECEIVED (server relayed our PlayInstrument)");
                this.AddMenuNotification("Music probe: echo OK", new Color(0.45f, 1f, 0.55f));
                return;
            }

            if (now >= this.musicPlayerProbeDeadline)
            {
                this.musicPlayerProbeStage = 0;
                this.musicPlayerProbeResult = this.musicPlayerProbeForeignEventSeen
                    ? "Probe: echo events arrived but none matched our netId — payload offsets may differ on this build"
                    : "Probe: no echo in 4s — server likely dropped the command (or the event hook missed)";
                MusicPlayerLog("Probe verdict: " + this.musicPlayerProbeResult
                    + " (hookInstalled=" + this.IsGameEventHookInstalled(MusicPlayerEchoEventName) + ")");
                this.AddMenuNotification("Music probe: no echo", new Color(1f, 0.55f, 0.55f));
            }
        }

        // Runs on the main thread (event hooks drain in OnUpdate). InstrumentInfoFromServer payload
        // starts with PlayInstrumentData whose first field is playerNetId — but only trust the event
        // arrival + id match, nothing deeper (reference fields live further in the struct).
        private void OnMusicPlayerInstrumentEcho(GameEventSnapshot e)
        {
            if (this.musicPlayerProbeStage == 0)
            {
                return;
            }

            uint playerNetId = e.ReadUInt32(0);
            if (playerNetId == this.musicPlayerSelfNetId)
            {
                this.musicPlayerProbeEchoSeen = true;
            }
            else
            {
                // Someone else playing nearby, or PlayInstrumentData field offsets differ on this
                // build — either way worth distinguishing from total silence in the verdict.
                this.musicPlayerProbeForeignEventSeen = true;
            }
        }

        // ==================== UI helpers (Music tab) ====================
        // The tab itself is UGUI — HeartopiaComplete.UguiMusicContent.cs. Only the shared
        // m:ss formatter lives here, next to the clock/duration state it reads from.

        private static string MusicPlayerFormatTime(float seconds)
        {
            if (seconds < 0f || float.IsNaN(seconds))
            {
                seconds = 0f;
            }

            int total = Mathf.FloorToInt(seconds);
            return (total / 60).ToString(CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("D2", CultureInfo.InvariantCulture);
        }
    }
}
