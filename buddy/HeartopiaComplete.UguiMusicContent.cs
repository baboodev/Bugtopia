using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — the MUSIC tab (display position UguiShellMusicTabIndex = 8, internal id 10).
    //
    // Backend: MusicPlayerFeature.cs (RECM .bin note-timeline playback). Unlike every other
    // content file in this folder this one has NO IMGUI ancestor to replay line-for-line — the
    // feature's own drawer (DrawMusicPlayerTab / GetMusicPlayerTabHeight) was written against the
    // IMGUI menu that Phase 5 retired, so it was dropped rather than migrated. The layout below
    // therefore reproduces that drawer's INFORMATION and ORDER, not its pixel arithmetic:
    //
    //   header (460x24 bold 14)                                          (+30)
    //   now-playing line — only while playing                            (+24)
    //   status line — only while musicPlayerStatus is non-empty          (+24)
    //   PLAY / STOP primary button (200x32)                              (+40)
    //   Loop checkbox (260x28)                                           (+32)
    //   Network mode checkbox (360x28)                                   (+32)
    //   [TEST NETWORK ECHO (240x28) (+34); [probe result (panelW x40) (+44)]]
    //   source (200x26) | Rescan (90x26) | Open folder (120x26)          (+32)
    //   catalog status — only when non-empty                             (+26)
    //   [empty-catalog hint (panelW x40)] OR N track rows (panelW x24)
    //   content height = final cursor + 20
    //
    // Ground rules (identical to every migrated round):
    //  - The backend stays untouched: this file only READS its fields and CALLS its existing
    //    methods (MusicPlayerStart/Stop/RescanCatalog/RunNetworkProbe, MusicPlayerGetModMusicDir,
    //    MusicPlayerGetGameRecordsDir, MusicPlayerFormatTime, SaveKeybinds). Zero interop here.
    //  - Wiring is by STATIC display-position index (UguiShellMusicTabIndex, declared with its
    //    siblings in UguiShellTabIndices.cs), never by label comparison.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // Behaviours carried over from the drawer verbatim — the traps:
    //  - LAZY FIRST SCAN: the drawer called MusicPlayerRescanCatalog() at the top of every paint
    //    when !musicPlayerCatalogScanned. Retained-mode has no paint, so the builder scans once
    //    and the per-frame gate re-scans if the flag is somehow still clear (e.g. a scan that
    //    threw). It is NOT re-scanned per frame otherwise — that would hit the disk every frame.
    //  - SAVE ON EVERY CONTROL: loop, network mode, source and track selection each
    //    SaveKeybinds(false); the transport buttons (PLAY/STOP/Rescan/Open folder/probe) do not.
    //  - NETWORK-MODE FLIP ORDER: stop under the OLD mode FIRST so held notes release on the
    //    channel they were pressed on, and only then flip the flag. Reversing this strands notes.
    //  - ROW PLAY BUTTON on a DIFFERENT track while playing: stop, then re-select, then start —
    //    the drawer's exact sequence.
    //  - PROBE RESULT COLOUR is content-driven: "ECHO RECEIVED" → green, "no echo" → orange,
    //    anything else → the muted body colour.
    //
    // Track rows are pooled on demand (the Extra round's carpet-row idiom): CreateUguiListRow
    // shape (c) with primaryText = null so the two buttons own the width — a fill-width Secondary
    // button carrying the track label (click = select) plus a fixed transport button reading the
    // short stop verb while THAT row is the playing track and the short play verb otherwise.
    // Click closures capture the SLOT index and re-read the live catalog at click time, so a
    // rescan between build and click can never act on a stale track. Rows past the catalog
    // length are SetActive(false), not destroyed.
    //
    // NO GEOMETRIC-SHAPES GLYPHS. The drawer used ▶/■ and the obvious port would keep them, but
    // the shell lets the user pick the font asset in Settings → UI Theme and U+25A0/U+25B6 are
    // not guaranteed to exist in an arbitrary one — the kit already refuses to draw a "▼"
    // dropdown caret for exactly this reason and bakes a sprite instead (UguiKit.cs,
    // EnsureUguiCaretSprite). A missing glyph here would be a tofu box on the only control that
    // starts playback. Hence localized short verbs on the button and an ASCII "> " selection
    // marker on the label.
    //
    // Cross-surface sync cadence: every gated frame (shell visible + Music tab) — the transport
    // button label, the two toggles, the now-playing/status/catalog lines (raw-reference and
    // scalar diffs so unchanged frames allocate nothing), the probe result, then the layout
    // signature check. The now-playing line is the one genuinely per-frame composite (the clock
    // advances continuously); it is gated on whole-second granularity so it recomposes ~1/sec
    // instead of ~60/sec. Per-frame sync disabled after 3 consecutive errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // Hard cap on pooled track rows — a music folder can hold hundreds of .bin files and the
        // drawer listed every one. Overflow is reported in the catalog line rather than silently
        // dropped.
        private const int UguiMusicMaxRowsShown = 60;

        // Per-row transport button width. Sized for the SHORT verbs behind music.row_play /
        // music.row_stop (all five languages are <= 5 glyphs) rather than the full PLAY/STOP the
        // main transport uses — "REPRODUCIR" would not fit here.
        private const float UguiMusicRowTransportW = 60f;

        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellMusicHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float PanelW;

            // Now-playing line — visible only while musicPlayerPlaying with a selected track.
            public GameObject NowPlayingLabel;
            public int NowPlayingSecond = -1;      // whole-second gate on the recompose
            public int NowPlayingSig = -1;         // (selectedIndex, notes, dropped, loops) packed

            // Free-text status line (musicPlayerStatus) — hidden while empty.
            public GameObject StatusLabel;
            public string StatusRawSeen;

            // Transport
            public GameObject PlayStopButton;
            public bool PlayStopShowsStop;         // false = label reads PLAY

            public Toggle LoopToggle;
            public Toggle NetworkToggle;

            // Network-only region: probe button (level 1) + its result line (level 2)
            public GameObject ProbeButton;
            public GameObject ProbeResultLabel;
            public string ProbeRawSeen;

            // Source row
            public GameObject SourceButton;
            public bool SourceShowsGameRecords;
            public GameObject RescanButton;
            public GameObject OpenFolderButton;

            // Catalog status + the empty-catalog hint
            public GameObject CatalogStatusLabel;
            public string CatalogRawSeen;
            public GameObject EmptyHintLabel;

            // Pooled track rows (grown on demand, never destroyed)
            public readonly List<UguiListRowHandle> TrackRows = new List<UguiListRowHandle>();
            public readonly List<string> TrackRowLabelSeen = new List<string>();
            public readonly List<bool> TrackRowPlayingSeen = new List<bool>();
            public readonly List<bool> TrackRowSelectedSeen = new List<bool>();

            // Layout signature — the exact values the last relayout used
            public int LayoutPacked = -1;
            public int LayoutRowCount = -1;

            public int ErrorCount;                 // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellMusicHandle uguiShellMusic;

        // Probe-result palette (content-driven, see file header).
        private static readonly Color UguiMusicProbeOkColor = new Color(0.45f, 1f, 0.55f);
        private static readonly Color UguiMusicProbeWarnColor = new Color(1f, 0.65f, 0.45f);

        // ----------------------------------------------------------------------------------------
        // Small helpers
        // ----------------------------------------------------------------------------------------

        // The drawer's row text: "<name>   <m:ss>   <instruments>".
        private string BuildUguiMusicRowLabel(MusicPlayerTrack track)
        {
            return track.Name + "   " + MusicPlayerFormatTime(track.Duration) + "   " + track.InstrumentsLabel;
        }

        private string BuildUguiMusicSourceLabel()
        {
            return this.musicPlayerSourceGameRecords
                ? this.L("Source: Game records")
                : this.L("Source: Bugtopia/Music");
        }

        // The live "<name>  0:12 / 2:34  notes N (dropped M) loop K" composite. Only called while
        // the line is visible.
        private string BuildUguiMusicNowPlayingText(MusicPlayerTrack selected)
        {
            float elapsed = this.musicPlayerWaitingLoopRestart
                ? 0f
                : (float)this.musicPlayerClock.Elapsed.TotalSeconds;
            string loopInfo = this.musicPlayerLoopsDone > 0
                ? " " + this.L("loop") + " " + this.musicPlayerLoopsDone
                : string.Empty;
            return selected.Name
                + "   " + MusicPlayerFormatTime(elapsed) + " / " + MusicPlayerFormatTime(this.musicPlayerClipDuration)
                + "   " + this.L("notes") + " " + this.musicPlayerNotesPlayed
                + (this.musicPlayerNotesDropped > 0
                    ? " (" + this.L("dropped") + " " + this.musicPlayerNotesDropped + ")"
                    : string.Empty)
                + loopInfo;
        }

        private MusicPlayerTrack GetUguiMusicSelectedTrack()
        {
            int i = this.musicPlayerSelectedIndex;
            return (i >= 0 && i < this.musicPlayerTracks.Count) ? this.musicPlayerTracks[i] : null;
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        private GameObject BuildUguiShellMusicContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellMusic = null;

            UguiShellMusicHandle handle = new UguiShellMusicHandle();
            GameObject block = this.CreateUguiGo("MusicContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            Transform scrollContent;
            GameObject scroll = this.CreateUguiScrollView(block.transform, "Scroll", 10f, out scrollContent);
            PlaceUguiTopLeft(scroll, 0f, 0f, w, h);
            // Flat look over the block's ContentBg (Logging idiom) — alpha-0 images still
            // raycast, so wheel/drag scrolling keeps working.
            try
            {
                Image scrollBg = scroll.GetComponent<Image>();
                if (scrollBg != null)
                {
                    scrollBg.color = Color.clear;
                }
                if (scrollContent != null && scrollContent.parent != null)
                {
                    Image viewportBg = scrollContent.parent.GetComponent<Image>();
                    if (viewportBg != null)
                    {
                        viewportBg.color = Color.clear;
                    }
                }
            }
            catch { }
            handle.ScrollContent = scrollContent;

            float contentWidth = w - 22f;      // viewport insets: 4 left + 18 right
            float panelW = contentWidth - 16f; // full-width elements at x=8, 8px right margin
            handle.PanelW = panelW;

            Color headerColor = this.UguiKitTextColor();
            Color mutedColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);

            // -------- Header (STATIC) --------
            GameObject header = this.CreateUguiLabel(scrollContent, "Header",
                this.L("Music Player (.bin)"), 14f, headerColor, false);
            this.TrySetUguiLabelBold(header);
            PlaceUguiTopLeft(header, 8f, 8f, 460f, 24f);

            // -------- Now-playing + status lines (both conditional) --------
            handle.NowPlayingLabel = this.CreateUguiLabel(scrollContent, "NowPlaying",
                string.Empty, 12f, mutedColor, false);
            handle.NowPlayingLabel.SetActive(false);
            handle.StatusRawSeen = this.musicPlayerStatus;
            handle.StatusLabel = this.CreateUguiLabel(scrollContent, "Status",
                this.musicPlayerStatus ?? string.Empty, 12f, mutedColor, false);
            handle.StatusLabel.SetActive(false);

            // -------- Transport --------
            handle.PlayStopShowsStop = this.musicPlayerPlaying;
            handle.PlayStopButton = this.CreateUguiPrimaryButton(scrollContent, "PlayStop",
                this.L(handle.PlayStopShowsStop ? "STOP" : "PLAY"),
                new System.Action(this.OnUguiMusicPlayStopClicked));

            handle.LoopToggle = this.CreateUguiCheckbox(scrollContent, "Loop",
                this.L("Loop"), this.musicPlayerLoop,
                new System.Action<bool>(this.OnUguiMusicLoopToggled));
            handle.NetworkToggle = this.CreateUguiCheckbox(scrollContent, "NetworkMode",
                this.L("Network mode (others hear) — experimental"), this.musicPlayerNetworkMode,
                new System.Action<bool>(this.OnUguiMusicNetworkModeToggled));

            // -------- Network-only probe region --------
            handle.ProbeButton = this.CreateUguiPrimaryButton(scrollContent, "ProbeEcho",
                this.L("TEST NETWORK ECHO"),
                new System.Action(this.OnUguiMusicProbeClicked));
            handle.ProbeButton.SetActive(false);
            handle.ProbeRawSeen = this.musicPlayerProbeResult;
            handle.ProbeResultLabel = this.CreateUguiLabel(scrollContent, "ProbeResult",
                this.musicPlayerProbeResult ?? string.Empty, 12f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.ProbeResultLabel);
            handle.ProbeResultLabel.SetActive(false);

            // -------- Source row --------
            handle.SourceShowsGameRecords = this.musicPlayerSourceGameRecords;
            handle.SourceButton = this.CreateUguiSecondaryButton(scrollContent, "Source",
                this.BuildUguiMusicSourceLabel(),
                new System.Action(this.OnUguiMusicSourceClicked));
            handle.RescanButton = this.CreateUguiSecondaryButton(scrollContent, "Rescan",
                this.L("Rescan"), new System.Action(this.OnUguiMusicRescanClicked));
            handle.OpenFolderButton = this.CreateUguiSecondaryButton(scrollContent, "OpenFolder",
                this.L("Open folder"), new System.Action(this.OnUguiMusicOpenFolderClicked));

            // -------- Catalog status + empty hint --------
            handle.CatalogRawSeen = this.musicPlayerCatalogStatus;
            handle.CatalogStatusLabel = this.CreateUguiLabel(scrollContent, "CatalogStatus",
                this.musicPlayerCatalogStatus ?? string.Empty, 12f, mutedColor, false);
            handle.CatalogStatusLabel.SetActive(false);
            handle.EmptyHintLabel = this.CreateUguiLabel(scrollContent, "EmptyHint",
                this.L("No .bin files found. Convert MIDI with tools/bgm_to_bin.py and drop the output here."),
                12f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.EmptyHintLabel);
            handle.EmptyHintLabel.SetActive(false);

            // The drawer's lazy first scan (file header) — once, here, not per frame.
            if (!this.musicPlayerCatalogScanned)
            {
                try { this.MusicPlayerRescanCatalog(); } catch { }
            }

            this.SyncUguiMusicTrackRows(handle);
            this.RelayoutUguiShellMusic(handle);

            handle.Root = block;
            this.uguiShellMusic = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Relayout — replays the cursor chain, SetActives every conditional region, and stores the
        // signature it laid out with.
        // ----------------------------------------------------------------------------------------

        private void RelayoutUguiShellMusic(UguiShellMusicHandle handle)
        {
            float panelW = handle.PanelW;
            float yCur = 38f;                          // header y=8 (+30)

            MusicPlayerTrack selected = this.GetUguiMusicSelectedTrack();
            bool showNowPlaying = this.musicPlayerPlaying && selected != null;
            SetUguiGoActive(handle.NowPlayingLabel, showNowPlaying);
            if (showNowPlaying)
            {
                PlaceUguiTopLeft(handle.NowPlayingLabel, 8f, yCur, panelW, 20f);
                yCur += 24f;
            }

            bool showStatus = !string.IsNullOrEmpty(this.musicPlayerStatus);
            SetUguiGoActive(handle.StatusLabel, showStatus);
            if (showStatus)
            {
                PlaceUguiTopLeft(handle.StatusLabel, 8f, yCur, panelW, 20f);
                yCur += 24f;
            }

            PlaceUguiTopLeft(handle.PlayStopButton, 8f, yCur, 200f, 32f);
            yCur += 40f;

            PlaceUguiTopLeft(handle.LoopToggle.gameObject, 8f, yCur, 260f, 28f);
            yCur += 32f;
            PlaceUguiTopLeft(handle.NetworkToggle.gameObject, 8f, yCur, 360f, 28f);
            yCur += 32f;

            // Network-only region: the probe button needs level 1, its result line level 2.
            bool net = this.musicPlayerNetworkMode;
            bool haveProbe = net && !string.IsNullOrEmpty(this.musicPlayerProbeResult);
            SetUguiGoActive(handle.ProbeButton, net);
            SetUguiGoActive(handle.ProbeResultLabel, haveProbe);
            if (net)
            {
                PlaceUguiTopLeft(handle.ProbeButton, 8f, yCur, 240f, 28f);
                yCur += 34f;
                if (haveProbe)
                {
                    PlaceUguiTopLeft(handle.ProbeResultLabel, 8f, yCur, panelW, 40f);
                    yCur += 44f;
                }
            }

            // Source row — three buttons on one line (the drawer's 200 / 90 / 110 widths).
            PlaceUguiTopLeft(handle.SourceButton, 8f, yCur, 200f, 26f);
            PlaceUguiTopLeft(handle.RescanButton, 216f, yCur, 90f, 26f);
            PlaceUguiTopLeft(handle.OpenFolderButton, 314f, yCur, 120f, 26f);
            yCur += 32f;

            bool showCatalog = !string.IsNullOrEmpty(this.musicPlayerCatalogStatus);
            SetUguiGoActive(handle.CatalogStatusLabel, showCatalog);
            if (showCatalog)
            {
                PlaceUguiTopLeft(handle.CatalogStatusLabel, 8f, yCur, panelW, 20f);
                yCur += 26f;
            }

            int total = this.musicPlayerTracks.Count;
            bool empty = total == 0;
            SetUguiGoActive(handle.EmptyHintLabel, empty);
            if (empty)
            {
                PlaceUguiTopLeft(handle.EmptyHintLabel, 8f, yCur, panelW, 40f);
                yCur += 44f;
            }

            int shown = Math.Min(total, UguiMusicMaxRowsShown);
            for (int i = 0; i < shown && i < handle.TrackRows.Count; i++)
            {
                PlaceUguiTopLeft(handle.TrackRows[i].Root, 8f, yCur, panelW, 22f);
                yCur += 24f;
            }

            this.SetUguiScrollContentHeight(handle.ScrollContent, yCur + 20f);

            handle.LayoutPacked = this.ComputeUguiMusicLayoutPacked();
            handle.LayoutRowCount = shown;
        }

        // Packed layout drivers: the conditional-region visibility bits, each inner level masked
        // to 0 while its outer level hides it (the Extra convention).
        private int ComputeUguiMusicLayoutPacked()
        {
            int packed = (this.musicPlayerPlaying && this.GetUguiMusicSelectedTrack() != null ? 1 : 0)
                | (!string.IsNullOrEmpty(this.musicPlayerStatus) ? 2 : 0)
                | (this.musicPlayerNetworkMode ? 4 : 0)
                | (!string.IsNullOrEmpty(this.musicPlayerCatalogStatus) ? 16 : 0)
                | (this.musicPlayerTracks.Count == 0 ? 32 : 0);
            if (this.musicPlayerNetworkMode && !string.IsNullOrEmpty(this.musicPlayerProbeResult))
            {
                packed |= 8;
            }
            return packed;
        }

        // ----------------------------------------------------------------------------------------
        // Track row pool — CreateUguiListRow shape (c) with primaryText = null: a fill-width
        // Secondary button carrying the label (click = select) plus a fixed transport button.
        // Rows are index-stable; click closures read the live catalog by slot index.
        // ----------------------------------------------------------------------------------------

        private void SyncUguiMusicTrackRows(UguiShellMusicHandle handle)
        {
            List<MusicPlayerTrack> list = this.musicPlayerTracks;
            int total = list.Count;
            int shown = Math.Min(total, UguiMusicMaxRowsShown);

            for (int i = 0; i < shown; i++)
            {
                if (i >= handle.TrackRows.Count)
                {
                    int slot = i; // capture a copy for the click closures
                    UguiListRowHandle row = this.CreateUguiListRow(handle.ScrollContent, "Track" + i,
                        8f, 0f, handle.PanelW, 22f,
                        null, null, null, false, true, null,
                        new UguiListRowButtonSpec[]
                        {
                            new UguiListRowButtonSpec
                            {
                                Label = string.Empty, Tier = UguiListRowTierSecondary, Width = 0f, Enabled = true,
                                OnClick = new System.Action(() => this.OnUguiMusicTrackSelectClicked(slot))
                            },
                            new UguiListRowButtonSpec
                            {
                                Label = this.L("music.row_play"), Tier = UguiListRowTierSecondary,
                                Width = UguiMusicRowTransportW, Enabled = true,
                                OnClick = new System.Action(() => this.OnUguiMusicTrackTransportClicked(slot))
                            }
                        });
                    handle.TrackRows.Add(row);
                    handle.TrackRowLabelSeen.Add(null);
                    handle.TrackRowPlayingSeen.Add(false);
                    handle.TrackRowSelectedSeen.Add(false);
                }

                UguiListRowHandle pooled = handle.TrackRows[i];
                if (pooled.Root != null && !pooled.Root.activeSelf)
                {
                    pooled.Root.SetActive(true);
                }

                MusicPlayerTrack track = list[i];
                bool isSelected = i == this.musicPlayerSelectedIndex;
                bool rowPlaying = this.musicPlayerPlaying && isSelected;
                string label = this.BuildUguiMusicRowLabel(track);

                if (!string.Equals(handle.TrackRowLabelSeen[i], label, StringComparison.Ordinal)
                    || handle.TrackRowSelectedSeen[i] != isSelected)
                {
                    handle.TrackRowLabelSeen[i] = label;
                    handle.TrackRowSelectedSeen[i] = isSelected;
                    // The drawer marked the selected row by recolouring its text to the accent;
                    // here the label lives inside a Secondary button, so a leading ASCII marker
                    // carries the same signal without fighting the button's own text colour.
                    this.SetUguiButtonLabel(pooled.Buttons.Count > 0 ? pooled.Buttons[0] : null,
                        (isSelected ? "> " : "   ") + label);
                }

                if (handle.TrackRowPlayingSeen[i] != rowPlaying)
                {
                    handle.TrackRowPlayingSeen[i] = rowPlaying;
                    this.SetUguiButtonLabel(pooled.Buttons.Count > 1 ? pooled.Buttons[1] : null,
                        this.L(rowPlaying ? "music.row_stop" : "music.row_play"));
                }
            }

            for (int i = shown; i < handle.TrackRows.Count; i++)
            {
                GameObject root = handle.TrackRows[i].Root;
                if (root != null && root.activeSelf)
                {
                    root.SetActive(false);
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellMusicOnUpdate()
        {
            UguiShellHandle shell = this.uguiShell;
            UguiShellMusicHandle handle = this.uguiShellMusic;
            if (shell == null || handle == null || handle.Root == null || handle.ErrorCount >= 3
                || shell.ActiveIndex != UguiShellMusicTabIndex || !this.IsUguiWindowVisible(shell.Window))
            {
                return;
            }

            try
            {
                // A scan that threw during the build leaves the flag clear — retry here rather
                // than leaving the tab permanently empty. Never a per-frame disk hit otherwise.
                if (!this.musicPlayerCatalogScanned)
                {
                    try { this.MusicPlayerRescanCatalog(); } catch { }
                }

                // Transport button label (playback also stops on its own at the end of a clip).
                if (handle.PlayStopShowsStop != this.musicPlayerPlaying)
                {
                    handle.PlayStopShowsStop = this.musicPlayerPlaying;
                    this.SetUguiButtonLabel(handle.PlayStopButton,
                        this.L(handle.PlayStopShowsStop ? "STOP" : "PLAY"));
                }

                // Toggle re-syncs (external edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.LoopToggle, this.musicPlayerLoop);
                this.SyncUguiToggleFromField(handle.NetworkToggle, this.musicPlayerNetworkMode);

                if (handle.SourceShowsGameRecords != this.musicPlayerSourceGameRecords)
                {
                    handle.SourceShowsGameRecords = this.musicPlayerSourceGameRecords;
                    this.SetUguiButtonLabel(handle.SourceButton, this.BuildUguiMusicSourceLabel());
                }

                // Now-playing composite — whole-second granularity plus a packed diff on the
                // counters, so a held frame allocates nothing (file header).
                MusicPlayerTrack selected = this.GetUguiMusicSelectedTrack();
                if (this.musicPlayerPlaying && selected != null)
                {
                    float elapsed = this.musicPlayerWaitingLoopRestart
                        ? 0f
                        : (float)this.musicPlayerClock.Elapsed.TotalSeconds;
                    int second = Mathf.FloorToInt(elapsed);
                    int sig = this.musicPlayerSelectedIndex
                        ^ (this.musicPlayerNotesPlayed << 4)
                        ^ (this.musicPlayerNotesDropped << 12)
                        ^ (this.musicPlayerLoopsDone << 22);
                    if (second != handle.NowPlayingSecond || sig != handle.NowPlayingSig)
                    {
                        handle.NowPlayingSecond = second;
                        handle.NowPlayingSig = sig;
                        this.SetUguiLabelText(handle.NowPlayingLabel,
                            this.BuildUguiMusicNowPlayingText(selected));
                    }
                }

                string statusRaw = this.musicPlayerStatus;
                if (!ReferenceEquals(statusRaw, handle.StatusRawSeen))
                {
                    handle.StatusRawSeen = statusRaw;
                    this.SetUguiLabelText(handle.StatusLabel, statusRaw ?? string.Empty);
                }

                string catalogRaw = this.musicPlayerCatalogStatus;
                if (!ReferenceEquals(catalogRaw, handle.CatalogRawSeen))
                {
                    handle.CatalogRawSeen = catalogRaw;
                    this.SetUguiLabelText(handle.CatalogStatusLabel, catalogRaw ?? string.Empty);
                }

                // Probe result — text plus the content-driven colour (file header).
                string probeRaw = this.musicPlayerProbeResult;
                if (!ReferenceEquals(probeRaw, handle.ProbeRawSeen))
                {
                    handle.ProbeRawSeen = probeRaw;
                    this.SetUguiLabelText(handle.ProbeResultLabel, probeRaw ?? string.Empty);
                    Color probeColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);
                    if (!string.IsNullOrEmpty(probeRaw))
                    {
                        if (probeRaw.Contains("ECHO RECEIVED"))
                        {
                            probeColor = UguiMusicProbeOkColor;
                        }
                        else if (probeRaw.Contains("no echo"))
                        {
                            probeColor = UguiMusicProbeWarnColor;
                        }
                    }
                    this.SetUguiLabelColor(handle.ProbeResultLabel, probeColor);
                }

                this.SyncUguiMusicTrackRows(handle);

                int shown = Math.Min(this.musicPlayerTracks.Count, UguiMusicMaxRowsShown);
                if (handle.LayoutPacked != this.ComputeUguiMusicLayoutPacked()
                    || handle.LayoutRowCount != shown)
                {
                    this.RelayoutUguiShellMusic(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Music content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors the retired drawer's block exactly (same side effects,
        // same order). Toggle handlers guard on "value actually changed": the kit checkbox fires
        // onChanged once at build (by design) and WithoutNotify re-syncs never fire events.
        // ----------------------------------------------------------------------------------------

        private void OnUguiMusicPlayStopClicked()
        {
            if (this.musicPlayerPlaying)
            {
                this.MusicPlayerStop("Stopped");
            }
            else
            {
                this.MusicPlayerStart();
            }
            this.RefreshUguiMusicAfterAction();
        }

        private void OnUguiMusicLoopToggled(bool value)
        {
            if (value == this.musicPlayerLoop)
            {
                return;
            }
            this.musicPlayerLoop = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // The drawer's exact order: stop under the OLD mode FIRST so held notes release on the
        // channel they were pressed on, and only then flip the flag (file header).
        private void OnUguiMusicNetworkModeToggled(bool value)
        {
            if (value == this.musicPlayerNetworkMode)
            {
                return;
            }
            if (this.musicPlayerPlaying)
            {
                this.MusicPlayerStop("Stopped (mode change)");
            }
            this.musicPlayerNetworkMode = value;
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiMusicAfterAction();
        }

        private void OnUguiMusicProbeClicked()
        {
            this.MusicPlayerRunNetworkProbe();
            this.RefreshUguiMusicAfterAction();
        }

        private void OnUguiMusicSourceClicked()
        {
            this.musicPlayerSourceGameRecords = !this.musicPlayerSourceGameRecords;
            this.MusicPlayerRescanCatalog();
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiMusicAfterAction();
        }

        private void OnUguiMusicRescanClicked()
        {
            this.MusicPlayerRescanCatalog();
            this.RefreshUguiMusicAfterAction();
        }

        private void OnUguiMusicOpenFolderClicked()
        {
            string dir = this.musicPlayerSourceGameRecords
                ? this.MusicPlayerGetGameRecordsDir()
                : this.MusicPlayerGetModMusicDir();
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }
            try { System.Diagnostics.Process.Start("explorer.exe", dir); } catch { }
        }

        // Row label click = select. Reads the live catalog by slot so a rescan between build and
        // click can never act on a stale track (file header).
        private void OnUguiMusicTrackSelectClicked(int slot)
        {
            if (slot < 0 || slot >= this.musicPlayerTracks.Count)
            {
                return;
            }
            this.musicPlayerSelectedIndex = slot;
            this.musicPlayerSelectedTrackName = this.musicPlayerTracks[slot].Name;
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiMusicAfterAction();
        }

        // Row transport click: the playing row's button stops it; any other row's stops whatever
        // is playing, re-selects, then starts — the drawer's exact sequence (file header).
        private void OnUguiMusicTrackTransportClicked(int slot)
        {
            if (slot < 0 || slot >= this.musicPlayerTracks.Count)
            {
                return;
            }

            bool rowPlaying = this.musicPlayerPlaying && slot == this.musicPlayerSelectedIndex;
            if (rowPlaying)
            {
                this.MusicPlayerStop("Stopped");
            }
            else
            {
                if (this.musicPlayerPlaying)
                {
                    this.MusicPlayerStop("Stopped");
                }
                this.musicPlayerSelectedIndex = slot;
                this.musicPlayerSelectedTrackName = this.musicPlayerTracks[slot].Name;
                this.MusicPlayerStart();
                try { this.SaveKeybinds(false); } catch { }
            }
            this.RefreshUguiMusicAfterAction();
        }

        // Immediate row + layout refresh after a click, so the tab reacts this same frame instead
        // of waiting for the next gated tick (the Extra round's Sanrio-toggle idiom).
        private void RefreshUguiMusicAfterAction()
        {
            UguiShellMusicHandle handle = this.uguiShellMusic;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                this.SyncUguiMusicTrackRows(handle);
                this.RelayoutUguiShellMusic(handle);
            }
            catch { }
        }
    }
}
