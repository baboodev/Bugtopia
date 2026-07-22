using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, item 7 (migration plan: cosmic-waddling-rainbow.md):
    // the RADAR tab, BOTH sub-tabs in one file (one round, one tab — unlike Resource Gathering's
    // four independent rounds): sub display index 0 = the main radar tab (DrawRadarTab's default
    // branch, HeartopiaComplete.Radar.cs:1169-1269 + the seven DrawRadar*Dropdown functions at
    // :1390-1755) and sub display index 1 = DrawRadarSettingsTab (:936-1121).
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawer and every backend method it calls stay fully functional and untouched —
    //    this file only READS the same fields and CALLS the same methods. Radar lives entirely
    //    inside HeartopiaComplete itself, so there are ZERO backend interop additions: every
    //    field (show*Radar, radar*DropdownOpen, radarMaxDistance, resourceVisualEsp*, …) and
    //    every method (ToggleRadar, CheckRadarAutoToggle, RunRadar, Cleanup,
    //    CloseAllRadarDropdowns, GetRadarSelectionSummary, AreAllMushroomRadarsEnabled,
    //    RemoveTrackedMarkersByNameContains, ClearHideAndSeekMorphMarkers,
    //    QueueRadarSettingsSave, OnRadarDisplayModeChanged, ResetRadarSettingsToDefaults) is
    //    already this.-accessible.
    //  - Wiring is by STATIC display-position index (UguiShellRadarTabIndex = 4 +
    //    UguiShellRadarMainSubIndex/UguiShellRadarSettingsSubIndex = 0/1, declared next to their
    //    siblings in UguiPhase3Content.cs), never label comparison.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs these builders).
    //
    // NEW primitives this round (the plan's flagged "first real consumer" builds — kept in this
    // file until a second consumer appears, the SetUguiButtonLabel/SetUguiGoActive precedent):
    //  - CreateUguiRadarOptionRow: the multi-select checkbox-dropdown ITEM row — one whole-row-
    //    clickable box whose background/ring/label switch between the theme's normal and active
    //    box styles by checked state, mirroring DrawRadarDropdownOption (Radar.cs:1757):
    //    themeTopTabStyle = controlFill @ clamp(uiPanelAlpha, .25, 1) + white .08 ring;
    //    themeTopTabActiveStyle = accent @ .18 + accent .35 ring; label bold 12 centered,
    //    white when on. Built on the kit's Toggle infrastructure but as its own highlighted-box
    //    visual, NOT a reuse of the switch-style CreateUguiCheckbox. Because closure-driven
    //    visuals ignore SetIsOnWithoutNotify (the round-2 CreateUguiSwitch lesson), the handle
    //    exposes ApplyVisual and SyncUguiRadarOptionRow re-applies it after every external
    //    re-sync.
    //  - CreateUguiRadarDropdownGroup: the collapsible group — "== Title ==" line + a clickable
    //    header box (live selection summary + accent chevron, DrawRadarDropdownHeader,
    //    Radar.cs:1271) + the option rows (optional master row first — Mushrooms only). Open
    //    state IS the shared radar*DropdownOpen field (both surfaces one accordion), and the
    //    open-click handler calls the REAL this.CloseAllRadarDropdowns() — whose 6-field list is
    //    MISSING radarUnderwaterDropdownOpen (Radar.cs:1380-1388, confirmed by reading it). That
    //    pre-existing quirk (opening any other group does NOT close an open Underwater group) is
    //    deliberately REPLICATED, not fixed, by inheriting the method instead of re-listing
    //    fields: "fixing" it silently is a design decision for the user, not a migration's.
    //  - CreateUguiRadarSegmentButton + ApplyUguiRadarSegmentState: the 2/3-way segmented picker
    //    (DrawRadarStyleSegmentButton, Radar.cs:1123) — same normal/active box pair, bold 12
    //    centered label that gains a "  [ON]" suffix when selected (text (.98,.99,1) selected /
    //    uiText @ .92 otherwise). Reused by every segmented control on the Settings sub-tab:
    //    the 2-way display mode, the 3-way visual style, and the two on/off rows (Big Map Spots
    //    / Player Avatars) that the source styles as one full-row segment with live On/Off text.
    //
    // The 7 groups are DATA-DRIVEN (one spec array, BuildUguiRadarGroupSpecs) rather than seven
    // hand-built blocks: 28 rows share identical build/position/sync code, and every real
    // difference fits a delegate slot — the Mushrooms master toggle (force-sets all 5, and the
    // group tail re-derives the master via AreAllMushroomRadarsEnabled after ANY change,
    // Radar.cs:1443-1451), the Misc rows' per-item marker-cleanup / RunRadar side effects
    // (each Set delegate carries its source block verbatim, including the enable-time RunRadar
    // calls for Insects/Other Players that are redundant with the group tail — present in
    // source, reproduced, not deduped), and the per-group summary builders (source label lists
    // verbatim — note Misc summarizes Other Players as "Players", Radar.cs:1648, while its row
    // label is "Other Players"). Summaries go through the REAL this.GetRadarSelectionSummary.
    //
    // Layout: both sub-tabs replay their IMGUI drawer's own y-cursor chain. The main tab keeps
    // the source's literal 260px-wide column (x=20 headers, x=28/252 option rows — the IMGUI tab
    // really is that narrow column). Seven independently-collapsible groups change the content
    // height, so RelayoutUguiShellRadarMain walks all 7 open states (positions rows, flips
    // visibility, repositions Force Refresh + credits, resizes the scroll content — the
    // signature idiom over the 7 shared open fields also picks up IMGUI-twin header clicks; the
    // CloseAllRadarDropdowns quirk means MULTIPLE groups can legitimately be open at once, which
    // the walk handles naturally). The Settings sub-tab adapts the source's 520px cards to the
    // cell width (Foraging precedent: cards at x=8, right-anchored sliders keep the source's
    // 16px right inset) and relayouts on radarDisplayMode (the 3 Game-Map-only rows +
    // everything below them, Radar.cs:1011-1039).
    //
    // Cross-surface sync cadence (established split):
    //  - Every gated frame (shell visible + Radar tab + own sub display index): the primary
    //    button label (ENABLE/DISABLE RADAR), all 28 option-row re-syncs (cheap bool compares;
    //    SetIsOnWithoutNotify + ApplyVisual), the main tab's open-state layout signature, and on
    //    Settings: 5 slider re-syncs + value labels (cached-string diffs), 4 switch-toggle
    //    re-syncs, 7 segment states (cached label+selected diffs), the radarDisplayMode layout
    //    signature. WithoutNotify everywhere — an external re-sync must never replay side
    //    effects.
    //  - 0.5s tick (NextSlowSyncAt idiom): the 7 group summaries (list-building allocations,
    //    the GetActivePriorityLocation-footer precedent); forced to run immediately after any
    //    of our own changes or a layout change so a click never shows a stale summary.
    //
    // Saves: Settings controls call this.QueueRadarSettingsSave() exactly where the source does;
    // the debounced queue is flushed by OnUpdate (HeartopiaComplete.cs:730) independent of any
    // menu, so no new flush mechanism. The main tab's toggles save NOTHING (source parity — the
    // show*Radar flags are session state there too).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Option row primitive (multi-select checkbox-dropdown item)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiRadarOptionRowHandle
        {
            public GameObject Root;
            public Toggle Toggle;
            public Action<bool> ApplyVisual;      // visuals ONLY — safe to call after SetIsOnWithoutNotify
        }

        // One whole-row-clickable highlighted-box toggle — DrawRadarDropdownOption's visual
        // (Radar.cs:1757-1777): normal = themeTopTabStyle box, active = themeTopTabActiveStyle
        // box (texture colors read from EnsureThemeStyles, UiKit.cs:1018-1023), label localized
        // + bold 12 + centered, white-on-active. Toggle-based so the shared
        // SetIsOnWithoutNotify re-sync shape applies; visuals re-applied explicitly because a
        // closure visual never hears WithoutNotify writes (round-2 CreateUguiSwitch lesson).
        // applyVisual(initial) at the end paints the seed state WITHOUT firing onChanged
        // (deliberately unlike CreateUguiSwitch — these rows' handlers carry radar side effects).
        private UguiRadarOptionRowHandle CreateUguiRadarOptionRow(Transform parent, string name,
            string label, bool initial, System.Action<bool> onChanged)
        {
            UguiRadarOptionRowHandle row = new UguiRadarOptionRowHandle();
            GameObject go = this.CreateUguiGo(name, parent);
            row.Root = go;

            Color controlFill = this.UguiKitControlFill();
            Color accent = this.UguiKitAccent();
            Color textColor = this.UguiKitTextColor();
            Color normalFill = new Color(controlFill.r, controlFill.g, controlFill.b,
                Mathf.Clamp(this.uiPanelAlpha, 0.25f, 1f));
            Color normalRing = new Color(1f, 1f, 1f, 0.08f);
            Color activeFill = new Color(accent.r, accent.g, accent.b, 0.18f);
            Color activeRing = new Color(accent.r, accent.g, accent.b, 0.35f);

            Image bg = this.AddUguiImage(go, initial ? activeFill : normalFill, true, 1.5f);
            bg.raycastTarget = true;
            this.AddUguiRingOverlay(go, initial ? activeRing : normalRing, 1.5f);
            Image ring = null;
            try
            {
                Transform ringT = go.transform.Find("Ring");
                ring = (ringT != null) ? ringT.GetComponent<Image>() : null;
            }
            catch { }

            Toggle tog = go.AddComponent<Toggle>();
            tog.targetGraphic = bg;
            row.Toggle = tog;

            GameObject lbl = this.CreateUguiLabel(go.transform, "Label", this.L(label), 12f,
                initial ? Color.white : textColor, true);
            this.TrySetUguiLabelBold(lbl);
            StretchUguiFill(lbl, 4f, 0f, 4f, 0f);

            System.Action<bool> applyVisual = delegate (bool v)
            {
                try
                {
                    if (bg != null)
                    {
                        bg.color = v ? activeFill : normalFill;
                    }
                    if (ring != null)
                    {
                        ring.color = v ? activeRing : normalRing;
                    }
                    this.SetUguiLabelColor(lbl, v ? Color.white : textColor);
                }
                catch { }
            };
            row.ApplyVisual = applyVisual;

            tog.isOn = initial;
            this.TryWireUguiEvent(tog.onValueChanged, new System.Action<bool>(delegate (bool v)
            {
                applyVisual(v);
                if (onChanged != null)
                {
                    onChanged(v);
                }
            }), name);
            return row;
        }

        // External re-sync (IMGUI twin / Select All / master cascade edits): WithoutNotify plus
        // an explicit visual re-apply — the closure visual never hears WithoutNotify writes.
        private void SyncUguiRadarOptionRow(UguiRadarOptionRowHandle row, bool live)
        {
            if (row == null || row.Toggle == null)
            {
                return;
            }
            if (row.Toggle.isOn != live)
            {
                row.Toggle.SetIsOnWithoutNotify(live);
                if (row.ApplyVisual != null)
                {
                    row.ApplyVisual(live);
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Segmented-button primitive (2/3-way exclusive picker + on/off single-segment rows)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiRadarSegmentHandle
        {
            public GameObject Root;
            public Image Bg;
            public Image Ring;                    // may be null (ring sprite unavailable)
            public GameObject Label;
            public string AppliedLabel;           // last base label applied (pre-suffix)
            public int AppliedSelected = -1;      // -1 = never applied
        }

        // One segment box — DrawRadarStyleSegmentButton (Radar.cs:1123-1139). Selection visuals
        // (including the "  [ON]" label suffix — two spaces, source-verbatim) come from
        // ApplyUguiRadarSegmentState, called at build seed time and from the per-frame sync, so
        // IMGUI-twin edits restyle it the same way our own clicks do.
        private UguiRadarSegmentHandle CreateUguiRadarSegmentButton(Transform parent, string name,
            System.Action onClick)
        {
            UguiRadarSegmentHandle seg = new UguiRadarSegmentHandle();
            GameObject go = this.CreateUguiGo(name, parent);
            seg.Root = go;

            Color controlFill = this.UguiKitControlFill();
            seg.Bg = this.AddUguiImage(go, new Color(controlFill.r, controlFill.g, controlFill.b,
                Mathf.Clamp(this.uiPanelAlpha, 0.25f, 1f)), true, 1.5f);
            seg.Bg.raycastTarget = true;
            this.AddUguiRingOverlay(go, new Color(1f, 1f, 1f, 0.08f), 1.5f);
            try
            {
                Transform ringT = go.transform.Find("Ring");
                seg.Ring = (ringT != null) ? ringT.GetComponent<Image>() : null;
            }
            catch { }

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = seg.Bg;
            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
            }

            GameObject lbl = this.CreateUguiLabel(go.transform, "Label", "", 12f,
                this.UguiKitTextColor(), true);
            this.TrySetUguiLabelBold(lbl);
            StretchUguiFill(lbl, 4f, 0f, 4f, 0f);
            seg.Label = lbl;
            return seg;
        }

        // No-ops until label or selection actually changed (allocation-free on the idle path —
        // this runs per gated frame for 7 segments). Colors read live at apply time (the LOD
        // highlight precedent); the fill/ring pair is the themeTopTab normal/active mapping, the
        // text colors are DrawRadarStyleSegmentButton's own literals.
        private void ApplyUguiRadarSegmentState(UguiRadarSegmentHandle seg, string baseLabel, bool selected)
        {
            if (seg == null)
            {
                return;
            }
            int selectedBit = selected ? 1 : 0;
            if (seg.AppliedSelected == selectedBit
                && string.Equals(baseLabel, seg.AppliedLabel, StringComparison.Ordinal))
            {
                return;
            }
            seg.AppliedSelected = selectedBit;
            seg.AppliedLabel = baseLabel;

            Color accent = this.UguiKitAccent();
            Color controlFill = this.UguiKitControlFill();
            Color text = this.UguiKitTextColor();
            if (seg.Bg != null)
            {
                seg.Bg.color = selected
                    ? new Color(accent.r, accent.g, accent.b, 0.18f)
                    : new Color(controlFill.r, controlFill.g, controlFill.b,
                        Mathf.Clamp(this.uiPanelAlpha, 0.25f, 1f));
            }
            if (seg.Ring != null)
            {
                seg.Ring.color = selected
                    ? new Color(accent.r, accent.g, accent.b, 0.35f)
                    : new Color(1f, 1f, 1f, 0.08f);
            }
            this.SetUguiLabelText(seg.Label, selected ? baseLabel + "  [ON]" : baseLabel);
            this.SetUguiLabelColor(seg.Label, selected
                ? new Color(0.98f, 0.99f, 1f, 1f)
                : new Color(text.r, text.g, text.b, 0.92f));
        }

        // ----------------------------------------------------------------------------------------
        // Group model (data-driven — see file header for why)
        // ----------------------------------------------------------------------------------------

        // One option row's binding: raw label key (localized at build), flag getter, and a Set
        // that carries the field write PLUS that row's own source side effects (Misc's marker
        // cleanups / enable-time RunRadar) — the shared group tail stays out of it.
        private struct UguiRadarOptionBinding
        {
            public string Label;
            public Func<bool> Get;
            public Action<bool> Set;
        }

        private sealed class UguiRadarGroupSpec
        {
            public string Title;                  // raw key — header shows "== L(Title) =="
            public Func<string> Summary;          // live summary (source list verbatim → GetRadarSelectionSummary)
            public Func<bool> GetOpen;            // the SHARED radar*DropdownOpen field
            public Action<bool> SetOpen;
            public bool HasMaster;                // Mushrooms only
            public UguiRadarOptionBinding Master;
            public UguiRadarOptionBinding[] Items;
            public Action AfterChanged;           // the group tail (runs only on an actual change)
        }

        private sealed class UguiRadarGroupHandle
        {
            public UguiRadarGroupSpec Spec;
            public GameObject TitleLabel;
            public GameObject HeaderBox;
            public GameObject SummaryLabel;
            public string SummaryShown;
            public GameObject ChevronLabel;
            public string ChevronShown;
            public UguiRadarOptionRowHandle MasterRow;   // null unless HasMaster
            public readonly List<UguiRadarOptionRowHandle> ItemRows = new List<UguiRadarOptionRowHandle>();
        }

        // Builds one group's pieces (title line, header box with summary + chevron, master row,
        // item rows) parented flat under the scroll content; RelayoutUguiShellRadarMain owns all
        // positions/visibility. Header visuals = DrawRadarDropdownHeader (Radar.cs:1271-1306):
        // themeTopTabStyle box + DrawCardOutline hairline stacked on the style's own baked ring,
        // summary bold 12 MiddleLeft in uiText, chevron bold 12 centered in accent at xMax-22.
        private UguiRadarGroupHandle CreateUguiRadarDropdownGroup(Transform parent, string name,
            UguiRadarGroupSpec spec)
        {
            UguiRadarGroupHandle group = new UguiRadarGroupHandle();
            group.Spec = spec;

            group.TitleLabel = this.CreateUguiBodyLabel(parent, name + "Title",
                "== " + this.L(spec.Title) + " ==", 13f);

            GameObject box = this.CreateUguiGo(name + "Header", parent);
            Color controlFill = this.UguiKitControlFill();
            Image boxBg = this.AddUguiImage(box, new Color(controlFill.r, controlFill.g, controlFill.b,
                Mathf.Clamp(this.uiPanelAlpha, 0.25f, 1f)), true, 1.5f);
            boxBg.raycastTarget = true;
            // The style's own baked ring + the DrawCardOutline hairline — IMGUI composites both.
            this.AddUguiRingOverlay(box, new Color(1f, 1f, 1f, 0.08f), 1.5f);
            this.AddUguiRingOverlay(box, new Color(1f, 1f, 1f,
                Mathf.Clamp(0.05f + (this.uiPanelAlpha * 0.05f), 0.05f, 0.10f)), 1.5f);
            Button btn = box.AddComponent<Button>();
            btn.targetGraphic = boxBg;
            btn.onClick.AddListener(new System.Action(() => this.OnUguiRadarGroupHeaderClicked(spec)));
            group.HeaderBox = box;

            group.SummaryShown = (spec.Summary != null) ? spec.Summary() : string.Empty;
            group.SummaryLabel = this.CreateUguiLabel(box.transform, "Summary", group.SummaryShown,
                12f, this.UguiKitTextColor(), false);
            this.TrySetUguiLabelBold(group.SummaryLabel);
            PlaceUguiTopLeft(group.SummaryLabel, 10f, 1f, 228f, 26f);

            group.ChevronLabel = this.CreateUguiLabel(box.transform, "Chevron", "v", 12f,
                this.UguiKitAccent(), true);
            this.TrySetUguiLabelBold(group.ChevronLabel);
            PlaceUguiTopLeft(group.ChevronLabel, 238f, 1f, 14f, 26f);
            group.ChevronShown = "v";

            if (spec.HasMaster)
            {
                UguiRadarOptionBinding master = spec.Master; // struct copy for the closure
                group.MasterRow = this.CreateUguiRadarOptionRow(parent, name + "Master",
                    master.Label, master.Get(),
                    new System.Action<bool>(v => this.OnUguiRadarGroupOptionToggled(master, spec, v)));
            }

            for (int i = 0; i < spec.Items.Length; i++)
            {
                UguiRadarOptionBinding item = spec.Items[i]; // struct copy per iteration (closure)
                group.ItemRows.Add(this.CreateUguiRadarOptionRow(parent, name + "Item" + i,
                    item.Label, item.Get(),
                    new System.Action<bool>(v => this.OnUguiRadarGroupOptionToggled(item, spec, v))));
            }

            return group;
        }

        // DrawRadarDropdownHeader's click block (Radar.cs:1279-1287) verbatim: compute the next
        // state first; when OPENING, close the others via the REAL CloseAllRadarDropdowns() —
        // whose list omits radarUnderwaterDropdownOpen (the replicated quirk; see file header) —
        // then write this group's own field. Immediate relayout for click feedback; the per-frame
        // signature check stays responsible for the IMGUI twin's own header clicks.
        private void OnUguiRadarGroupHeaderClicked(UguiRadarGroupSpec spec)
        {
            if (spec == null || spec.GetOpen == null || spec.SetOpen == null)
            {
                return;
            }
            bool nextOpen = !spec.GetOpen();
            if (nextOpen)
            {
                this.CloseAllRadarDropdowns();
            }
            spec.SetOpen(nextOpen);

            UguiShellRadarMainHandle handle = this.uguiShellRadarMain;
            if (handle != null)
            {
                handle.LayoutSignature = this.ComputeUguiRadarMainLayoutSignature();
                this.RelayoutUguiShellRadarMain(handle);
                handle.NextSlowSyncAt = 0f; // summaries must not lag the layout change
            }
        }

        // Shared row handler: the source's per-row changed guard, the binding's own write (field
        // + Misc side effects), then the group tail — exactly the `if (changed)` cascade every
        // DrawRadar*Dropdown ends with.
        private void OnUguiRadarGroupOptionToggled(UguiRadarOptionBinding binding,
            UguiRadarGroupSpec spec, bool value)
        {
            if (binding.Get == null || binding.Set == null)
            {
                return;
            }
            if (value == binding.Get())
            {
                return;
            }
            binding.Set(value);
            if (spec != null && spec.AfterChanged != null)
            {
                spec.AfterChanged();
            }

            UguiShellRadarMainHandle handle = this.uguiShellRadarMain;
            if (handle != null)
            {
                handle.NextSlowSyncAt = 0f; // summary + master-row re-derive show next frame
            }
        }

        // The shared end-of-group cascade (every group, Radar.cs e.g. 1476-1483).
        private void ApplyUguiRadarStandardGroupTail()
        {
            this.CheckRadarAutoToggle();
            if (this.isRadarActive)
            {
                this.RunRadar();
            }
        }

        // Mushrooms only (Radar.cs:1443-1451): the master flag re-derives from the 5 individuals
        // FIRST (an AND — AreAllMushroomRadarsEnabled), then the standard cascade.
        private void ApplyUguiRadarMushroomGroupTail()
        {
            this.showMushroomRadar = this.AreAllMushroomRadarsEnabled();
            this.ApplyUguiRadarStandardGroupTail();
        }

        // ----------------------------------------------------------------------------------------
        // Summaries (source list-building verbatim; localization happens INSIDE
        // GetRadarSelectionSummary — pass raw labels, Radar.cs:1786)
        // ----------------------------------------------------------------------------------------

        // Radar.cs:1392-1406 — "All Mushrooms" wins outright while the master flag is set.
        private string BuildUguiRadarMushroomSummary()
        {
            if (this.showMushroomRadar)
            {
                return this.L("All Mushrooms");
            }
            List<string> selected = new List<string>();
            if (this.showOysterMushroomRadar) selected.Add("Oyster Mushroom");
            if (this.showButtonMushroomRadar) selected.Add("Button Mushroom");
            if (this.showPennyBunRadar) selected.Add("Penny Bun");
            if (this.showShiitakeRadar) selected.Add("Shiitake");
            if (this.showTruffleRadar) selected.Add("Black Truffle");
            return this.GetRadarSelectionSummary(selected);
        }

        // Radar.cs:1458-1461.
        private string BuildUguiRadarBerriesSummary()
        {
            List<string> selected = new List<string>();
            if (this.showBlueberryRadar) selected.Add("Blueberries");
            if (this.showRaspberryRadar) selected.Add("Raspberries");
            return this.GetRadarSelectionSummary(selected);
        }

        // Radar.cs:1490-1495.
        private string BuildUguiRadarEventsSummary()
        {
            List<string> selected = new List<string>();
            if (this.showFiddleheadRadar) selected.Add("Fiddlehead");
            if (this.showTallMustardRadar) selected.Add("Tall Mustard");
            if (this.showBurdockRadar) selected.Add("Burdock");
            if (this.showMustardGreensRadar) selected.Add("Mustard Greens");
            return this.GetRadarSelectionSummary(selected);
        }

        // Radar.cs:1530-1535.
        private string BuildUguiRadarUnderwaterSummary()
        {
            List<string> selected = new List<string>();
            if (this.showGlasswortRadar) selected.Add("Glasswort");
            if (this.showSeaGrapeRadar) selected.Add("Sea Grape");
            if (this.showWakameRadar) selected.Add("Wakame");
            if (this.showContaminatedRadar) selected.Add("Contaminated");
            return this.GetRadarSelectionSummary(selected);
        }

        // Radar.cs:1570-1573.
        private string BuildUguiRadarResourcesSummary()
        {
            List<string> selected = new List<string>();
            if (this.showStoneRadar) selected.Add("Stones");
            if (this.showOreRadar) selected.Add("Ores");
            return this.GetRadarSelectionSummary(selected);
        }

        // Radar.cs:1602-1607.
        private string BuildUguiRadarTreesSummary()
        {
            List<string> selected = new List<string>();
            if (this.showTreeRadar) selected.Add("Trees");
            if (this.showRareTreeRadar) selected.Add("Rare Trees");
            if (this.showAppleTreeRadar) selected.Add("Apple Trees");
            if (this.showOrangeTreeRadar) selected.Add("Mandarin Trees");
            return this.GetRadarSelectionSummary(selected);
        }

        // Radar.cs:1642-1649 — Other Players summarizes as "Players" (row label differs; kept).
        private string BuildUguiRadarMiscSummary()
        {
            List<string> selected = new List<string>();
            if (this.showBubbleRadar) selected.Add("Bubbles");
            if (this.showBirdRadar) selected.Add("Birds");
            if (this.showInsectRadar) selected.Add("Insects");
            if (this.showFishShadowRadar) selected.Add("Fish Shadows");
            if (this.showMeteorRadar) selected.Add("Meteors");
            if (this.showOtherPlayersRadar) selected.Add("Players");
            return this.GetRadarSelectionSummary(selected);
        }

        // ----------------------------------------------------------------------------------------
        // The 7 group specs — display order = DrawRadarTab's call order (Radar.cs:1253-1259)
        // ----------------------------------------------------------------------------------------

        private UguiRadarGroupSpec[] BuildUguiRadarGroupSpecs()
        {
            return new UguiRadarGroupSpec[]
            {
                // Mushrooms (Radar.cs:1390-1454) — the ONLY group with a master row. Toggling it
                // force-sets all 5 individuals (source order kept: master field first).
                new UguiRadarGroupSpec
                {
                    Title = "Mushrooms",
                    Summary = this.BuildUguiRadarMushroomSummary,
                    GetOpen = () => this.radarMushroomsDropdownOpen,
                    SetOpen = v => this.radarMushroomsDropdownOpen = v,
                    HasMaster = true,
                    Master = new UguiRadarOptionBinding
                    {
                        Label = "All Mushrooms",
                        Get = () => this.showMushroomRadar,
                        Set = v =>
                        {
                            this.showMushroomRadar = v;
                            this.showOysterMushroomRadar = v;
                            this.showButtonMushroomRadar = v;
                            this.showPennyBunRadar = v;
                            this.showShiitakeRadar = v;
                            this.showTruffleRadar = v;
                        }
                    },
                    Items = new UguiRadarOptionBinding[]
                    {
                        new UguiRadarOptionBinding { Label = "Oyster Mushroom", Get = () => this.showOysterMushroomRadar, Set = v => this.showOysterMushroomRadar = v },
                        new UguiRadarOptionBinding { Label = "Button Mushroom", Get = () => this.showButtonMushroomRadar, Set = v => this.showButtonMushroomRadar = v },
                        new UguiRadarOptionBinding { Label = "Penny Bun", Get = () => this.showPennyBunRadar, Set = v => this.showPennyBunRadar = v },
                        new UguiRadarOptionBinding { Label = "Shiitake", Get = () => this.showShiitakeRadar, Set = v => this.showShiitakeRadar = v },
                        new UguiRadarOptionBinding { Label = "Black Truffle", Get = () => this.showTruffleRadar, Set = v => this.showTruffleRadar = v },
                    },
                    AfterChanged = this.ApplyUguiRadarMushroomGroupTail
                },
                // Berries (Radar.cs:1456-1486).
                new UguiRadarGroupSpec
                {
                    Title = "Berries",
                    Summary = this.BuildUguiRadarBerriesSummary,
                    GetOpen = () => this.radarBerriesDropdownOpen,
                    SetOpen = v => this.radarBerriesDropdownOpen = v,
                    Items = new UguiRadarOptionBinding[]
                    {
                        new UguiRadarOptionBinding { Label = "Blueberries", Get = () => this.showBlueberryRadar, Set = v => this.showBlueberryRadar = v },
                        new UguiRadarOptionBinding { Label = "Raspberries", Get = () => this.showRaspberryRadar, Set = v => this.showRaspberryRadar = v },
                    },
                    AfterChanged = this.ApplyUguiRadarStandardGroupTail
                },
                // Events (Radar.cs:1488-1526).
                new UguiRadarGroupSpec
                {
                    Title = "Events",
                    Summary = this.BuildUguiRadarEventsSummary,
                    GetOpen = () => this.radarEventsDropdownOpen,
                    SetOpen = v => this.radarEventsDropdownOpen = v,
                    Items = new UguiRadarOptionBinding[]
                    {
                        new UguiRadarOptionBinding { Label = "Fiddlehead", Get = () => this.showFiddleheadRadar, Set = v => this.showFiddleheadRadar = v },
                        new UguiRadarOptionBinding { Label = "Tall Mustard", Get = () => this.showTallMustardRadar, Set = v => this.showTallMustardRadar = v },
                        new UguiRadarOptionBinding { Label = "Burdock", Get = () => this.showBurdockRadar, Set = v => this.showBurdockRadar = v },
                        new UguiRadarOptionBinding { Label = "Mustard Greens", Get = () => this.showMustardGreensRadar, Set = v => this.showMustardGreensRadar = v },
                    },
                    AfterChanged = this.ApplyUguiRadarStandardGroupTail
                },
                // Underwater (Radar.cs:1528-1566) — the group CloseAllRadarDropdowns forgets.
                new UguiRadarGroupSpec
                {
                    Title = "Underwater",
                    Summary = this.BuildUguiRadarUnderwaterSummary,
                    GetOpen = () => this.radarUnderwaterDropdownOpen,
                    SetOpen = v => this.radarUnderwaterDropdownOpen = v,
                    Items = new UguiRadarOptionBinding[]
                    {
                        new UguiRadarOptionBinding { Label = "Glasswort", Get = () => this.showGlasswortRadar, Set = v => this.showGlasswortRadar = v },
                        new UguiRadarOptionBinding { Label = "Sea Grape", Get = () => this.showSeaGrapeRadar, Set = v => this.showSeaGrapeRadar = v },
                        new UguiRadarOptionBinding { Label = "Wakame", Get = () => this.showWakameRadar, Set = v => this.showWakameRadar = v },
                        new UguiRadarOptionBinding { Label = "Contaminated", Get = () => this.showContaminatedRadar, Set = v => this.showContaminatedRadar = v },
                    },
                    AfterChanged = this.ApplyUguiRadarStandardGroupTail
                },
                // Resources (Radar.cs:1568-1598).
                new UguiRadarGroupSpec
                {
                    Title = "Resources",
                    Summary = this.BuildUguiRadarResourcesSummary,
                    GetOpen = () => this.radarResourcesDropdownOpen,
                    SetOpen = v => this.radarResourcesDropdownOpen = v,
                    Items = new UguiRadarOptionBinding[]
                    {
                        new UguiRadarOptionBinding { Label = "Stones", Get = () => this.showStoneRadar, Set = v => this.showStoneRadar = v },
                        new UguiRadarOptionBinding { Label = "Ores", Get = () => this.showOreRadar, Set = v => this.showOreRadar = v },
                    },
                    AfterChanged = this.ApplyUguiRadarStandardGroupTail
                },
                // Trees (Radar.cs:1600-1638).
                new UguiRadarGroupSpec
                {
                    Title = "Trees",
                    Summary = this.BuildUguiRadarTreesSummary,
                    GetOpen = () => this.radarTreesDropdownOpen,
                    SetOpen = v => this.radarTreesDropdownOpen = v,
                    Items = new UguiRadarOptionBinding[]
                    {
                        new UguiRadarOptionBinding { Label = "Trees", Get = () => this.showTreeRadar, Set = v => this.showTreeRadar = v },
                        new UguiRadarOptionBinding { Label = "Rare Trees", Get = () => this.showRareTreeRadar, Set = v => this.showRareTreeRadar = v },
                        new UguiRadarOptionBinding { Label = "Apple Trees", Get = () => this.showAppleTreeRadar, Set = v => this.showAppleTreeRadar = v },
                        new UguiRadarOptionBinding { Label = "Mandarin Trees", Get = () => this.showOrangeTreeRadar, Set = v => this.showOrangeTreeRadar = v },
                    },
                    AfterChanged = this.ApplyUguiRadarStandardGroupTail
                },
                // Misc (Radar.cs:1640-1755) — the per-item side effects are REAL and non-uniform;
                // each Set carries its source block verbatim. The enable-time RunRadar calls
                // (Insects/Other Players) are redundant with the group tail but present in
                // source — reproduced, not deduped.
                new UguiRadarGroupSpec
                {
                    Title = "Misc",
                    Summary = this.BuildUguiRadarMiscSummary,
                    GetOpen = () => this.radarMiscDropdownOpen,
                    SetOpen = v => this.radarMiscDropdownOpen = v,
                    Items = new UguiRadarOptionBinding[]
                    {
                        // Radar.cs:1657-1659 — no side effects.
                        new UguiRadarOptionBinding { Label = "Bubbles", Get = () => this.showBubbleRadar, Set = v => this.showBubbleRadar = v },
                        // Radar.cs:1661-1674.
                        new UguiRadarOptionBinding
                        {
                            Label = "Birds",
                            Get = () => this.showBirdRadar,
                            Set = v =>
                            {
                                this.showBirdRadar = v;
                                if (!this.showBirdRadar)
                                {
                                    this.RemoveTrackedMarkersByNameContains("p_bird_bird");
                                    this.RemoveTrackedMarkersByNameContains("p_bird_");
                                    this.RemoveTrackedMarkersByNameContains("bird");
                                }
                            }
                        },
                        // Radar.cs:1676-1696.
                        new UguiRadarOptionBinding
                        {
                            Label = "Insects",
                            Get = () => this.showInsectRadar,
                            Set = v =>
                            {
                                this.showInsectRadar = v;
                                if (this.showInsectRadar)
                                {
                                    if (this.isRadarActive)
                                    {
                                        this.RunRadar();
                                    }
                                }
                                else
                                {
                                    this.RemoveTrackedMarkersByNameContains("p_insect_insect");
                                    this.RemoveTrackedMarkersByNameContains("p_insect_");
                                    this.RemoveTrackedMarkersByNameContains("insect");
                                }
                            }
                        },
                        // Radar.cs:1698-1711.
                        new UguiRadarOptionBinding
                        {
                            Label = "Fish Shadows",
                            Get = () => this.showFishShadowRadar,
                            Set = v =>
                            {
                                this.showFishShadowRadar = v;
                                if (!this.showFishShadowRadar)
                                {
                                    this.RemoveTrackedMarkersByNameContains("fishshadow");
                                    this.RemoveTrackedMarkersByNameContains("fish_shadow");
                                    this.RemoveTrackedMarkersByNameContains("p_fish");
                                }
                            }
                        },
                        // Radar.cs:1713-1725.
                        new UguiRadarOptionBinding
                        {
                            Label = "Meteors",
                            Get = () => this.showMeteorRadar,
                            Set = v =>
                            {
                                this.showMeteorRadar = v;
                                if (!this.showMeteorRadar)
                                {
                                    this.RemoveTrackedMarkersByNameContains("p_rock_meteorite");
                                    this.RemoveTrackedMarkersByNameContains("meteorite");
                                }
                            }
                        },
                        // Radar.cs:1727-1743 — row label "Other Players", summary label "Players".
                        new UguiRadarOptionBinding
                        {
                            Label = "Other Players",
                            Get = () => this.showOtherPlayersRadar,
                            Set = v =>
                            {
                                this.showOtherPlayersRadar = v;
                                if (!this.showOtherPlayersRadar)
                                {
                                    this.RemoveTrackedMarkersByNameContains("p_player_skeleton");
                                    this.ClearHideAndSeekMorphMarkers();
                                }
                                else if (this.isRadarActive)
                                {
                                    this.RunRadar();
                                }
                            }
                        },
                    },
                    AfterChanged = this.ApplyUguiRadarStandardGroupTail
                },
            };
        }

        // ----------------------------------------------------------------------------------------
        // Shared gate: is the shell showing a specific Radar sub-tab right now?
        // ----------------------------------------------------------------------------------------

        private bool IsUguiShellRadarSubTabActive(int subIndex)
        {
            try
            {
                UguiShellHandle shell = this.uguiShell;
                if (shell == null || shell.ActiveIndex != UguiShellRadarTabIndex
                    || !this.IsUguiWindowVisible(shell.Window))
                {
                    return false;
                }
                UguiTabBarHandle bar = (UguiShellRadarTabIndex < shell.SubTabBars.Count)
                    ? shell.SubTabBars[UguiShellRadarTabIndex]
                    : null;
                return bar != null && bar.ActiveIndex == subIndex;
            }
            catch
            {
                return false;
            }
        }

        // ----------------------------------------------------------------------------------------
        // MAIN sub-tab (display index 0) — DrawRadarTab's default branch
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellRadarMainHandle
        {
            public GameObject Root;
            public Transform ScrollContent;

            public GameObject ToggleButton;       // primary — ENABLE/DISABLE RADAR label swap
            public string ToggleShown;
            public UguiRadarGroupHandle[] Groups;
            public GameObject ForceRefreshButton; // repositioned by the relayout
            public GameObject CreditsLabel;       // repositioned by the relayout

            public int LayoutSignature = -1;      // packed 7 radar*DropdownOpen bits
            public float NextSlowSyncAt;          // 0.5s summary tick
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellRadarMainHandle uguiShellRadarMain;

        // Positions replay DrawRadarTab's own cursor (base y=8): 8 primary(40) +50 → 58
        // select/clear(30) +45 → 103 the seven groups (variable) → force refresh +40 → credits
        // (120, source returns num+130). The source's literal 260px column is kept (see file
        // header). Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellRadarMainContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellRadarMain = null;

            UguiShellRadarMainHandle handle = new UguiShellRadarMainHandle();
            GameObject block = this.CreateUguiGo("RadarMainContent", parent);
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

            // Radar.cs:1176-1181 — DrawPrimaryActionButton localizes internally, so L() here.
            handle.ToggleShown = this.L(this.isRadarActive ? "DISABLE RADAR" : "ENABLE RADAR");
            handle.ToggleButton = this.CreateUguiPrimaryButton(scrollContent, "ToggleRadar",
                handle.ToggleShown, new System.Action(this.OnUguiRadarToggleClicked));
            PlaceUguiTopLeft(handle.ToggleButton, 20f, 8f, 260f, 40f);

            // Radar.cs:1185-1250 — the two bulk buttons are NOT symmetric beyond the field list
            // (see their handlers). 12pt labels: 15-char captions in 125px kit buttons.
            GameObject selectAll = this.CreateUguiSecondaryButton(scrollContent, "SelectAllLoots",
                this.L("Select All Loots"), new System.Action(this.OnUguiRadarSelectAllLootsClicked));
            PlaceUguiTopLeft(selectAll, 20f, 58f, 125f, 30f);
            this.TrySetUguiButtonLabelSize(selectAll, 12f);
            GameObject clearAll = this.CreateUguiSecondaryButton(scrollContent, "ClearAllLoots",
                this.L("Clear All Loots"), new System.Action(this.OnUguiRadarClearAllLootsClicked));
            PlaceUguiTopLeft(clearAll, 155f, 58f, 125f, 30f);
            this.TrySetUguiButtonLabelSize(clearAll, 12f);

            // The seven groups (order = DrawRadarTab's call order, Radar.cs:1253-1259).
            UguiRadarGroupSpec[] specs = this.BuildUguiRadarGroupSpecs();
            handle.Groups = new UguiRadarGroupHandle[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                handle.Groups[i] = this.CreateUguiRadarDropdownGroup(scrollContent, "Group" + i, specs[i]);
            }

            // Radar.cs:1260-1264 — drawn unconditionally; the CLICK is what's gated on
            // isRadarActive (see the handler).
            handle.ForceRefreshButton = this.CreateUguiSecondaryButton(scrollContent, "ForceRefresh",
                this.L("Force Refresh Scan"), new System.Action(this.OnUguiRadarForceRefreshClicked));

            // Radar.cs:1267 — a raw GUI.Label, NOT localized in source; verbatim.
            handle.CreditsLabel = this.CreateUguiBodyLabel(scrollContent, "Credits",
                "  Credits: OG dll creator :)\n- breckdareck for ForagerRadar", 13f);
            this.TrySetUguiLabelWrapped(handle.CreditsLabel);

            handle.LayoutSignature = this.ComputeUguiRadarMainLayoutSignature();
            this.RelayoutUguiShellRadarMain(handle);

            handle.Root = block;
            this.uguiShellRadarMain = handle;
            return block;
        }

        private int ComputeUguiRadarMainLayoutSignature()
        {
            return (this.radarMushroomsDropdownOpen ? 1 : 0)
                 | (this.radarBerriesDropdownOpen ? 2 : 0)
                 | (this.radarEventsDropdownOpen ? 4 : 0)
                 | (this.radarUnderwaterDropdownOpen ? 8 : 0)
                 | (this.radarResourcesDropdownOpen ? 16 : 0)
                 | (this.radarTreesDropdownOpen ? 32 : 0)
                 | (this.radarMiscDropdownOpen ? 64 : 0);
        }

        // The UGUI analog of the IMGUI drawers' y-cursor accumulation across all seven groups
        // (header: title +24, box +34; open: rows at x=28 pitch 30; +8 group gap — Radar.cs:1271-
        // 1306 + each DrawRadar*Dropdown), then Force Refresh (+40) and the credits label
        // (source: return num + 130). Reposition/SetActive/resize only; nothing is rebuilt.
        // Multiple groups CAN be open at once (the CloseAllRadarDropdowns quirk) — the walk
        // handles any combination.
        private void RelayoutUguiShellRadarMain(UguiShellRadarMainHandle handle)
        {
            if (handle == null || handle.Groups == null)
            {
                return;
            }

            float yCur = 103f; // 8 + 50 (primary) + 45 (select/clear) — fixed rows above
            for (int i = 0; i < handle.Groups.Length; i++)
            {
                UguiRadarGroupHandle group = handle.Groups[i];
                if (group == null)
                {
                    continue;
                }

                PlaceUguiTopLeft(group.TitleLabel, 20f, yCur, 260f, 20f);
                yCur += 24f;
                PlaceUguiTopLeft(group.HeaderBox, 20f, yCur, 260f, 28f);
                yCur += 34f;

                bool open = false;
                try { open = group.Spec != null && group.Spec.GetOpen != null && group.Spec.GetOpen(); }
                catch { }

                string chevron = open ? "^" : "v";
                if (!string.Equals(chevron, group.ChevronShown, StringComparison.Ordinal))
                {
                    group.ChevronShown = chevron;
                    this.SetUguiLabelText(group.ChevronLabel, chevron);
                }

                if (group.MasterRow != null)
                {
                    SetUguiGoActive(group.MasterRow.Root, open);
                    if (open)
                    {
                        PlaceUguiTopLeft(group.MasterRow.Root, 28f, yCur, 252f, 26f);
                        yCur += 30f;
                    }
                }
                for (int r = 0; r < group.ItemRows.Count; r++)
                {
                    UguiRadarOptionRowHandle row = group.ItemRows[r];
                    if (row == null)
                    {
                        continue;
                    }
                    SetUguiGoActive(row.Root, open);
                    if (open)
                    {
                        PlaceUguiTopLeft(row.Root, 28f, yCur, 252f, 26f);
                        yCur += 30f;
                    }
                }

                yCur += 8f;
            }

            PlaceUguiTopLeft(handle.ForceRefreshButton, 20f, yCur, 260f, 30f);
            yCur += 40f;
            PlaceUguiTopLeft(handle.CreditsLabel, 20f, yCur, 260f, 120f);

            this.SetUguiScrollContentHeight(handle.ScrollContent, yCur + 130f);
        }

        // ----------------------------------------------------------------------------------------
        // MAIN per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellRadarMainOnUpdate()
        {
            UguiShellRadarMainHandle handle = this.uguiShellRadarMain;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellRadarSubTabActive(UguiShellRadarMainSubIndex))
            {
                return;
            }

            try
            {
                // Primary button label (isRadarActive also flips from hotkeys/auto-disable).
                string toggleText = this.L(this.isRadarActive ? "DISABLE RADAR" : "ENABLE RADAR");
                if (!string.Equals(toggleText, handle.ToggleShown, StringComparison.Ordinal))
                {
                    handle.ToggleShown = toggleText;
                    this.SetUguiButtonLabel(handle.ToggleButton, toggleText);
                }

                // All 28 option rows (cheap bool compares; WithoutNotify + explicit visual).
                // Covers IMGUI-twin edits, Select/Clear All, and the mushroom master cascade.
                if (handle.Groups != null)
                {
                    for (int i = 0; i < handle.Groups.Length; i++)
                    {
                        UguiRadarGroupHandle group = handle.Groups[i];
                        if (group == null || group.Spec == null)
                        {
                            continue;
                        }
                        if (group.MasterRow != null && group.Spec.HasMaster
                            && group.Spec.Master.Get != null)
                        {
                            this.SyncUguiRadarOptionRow(group.MasterRow, group.Spec.Master.Get());
                        }
                        for (int r = 0; r < group.ItemRows.Count && r < group.Spec.Items.Length; r++)
                        {
                            if (group.Spec.Items[r].Get != null)
                            {
                                this.SyncUguiRadarOptionRow(group.ItemRows[r], group.Spec.Items[r].Get());
                            }
                        }
                    }
                }

                // Open-state layout signature — picks up the IMGUI twin's own header clicks
                // (our clicks relayout immediately and pre-store the signature).
                int signature = this.ComputeUguiRadarMainLayoutSignature();
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellRadarMain(handle);
                    handle.NextSlowSyncAt = 0f; // summaries must not lag a layout change
                }

                // 0.5s tick: the 7 summaries (list-building allocations — the throttled tier).
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    if (handle.Groups != null)
                    {
                        for (int i = 0; i < handle.Groups.Length; i++)
                        {
                            UguiRadarGroupHandle group = handle.Groups[i];
                            if (group == null || group.Spec == null || group.Spec.Summary == null)
                            {
                                continue;
                            }
                            this.SyncUguiSelfLabelText(group.SummaryLabel, ref group.SummaryShown,
                                group.Spec.Summary());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Radar main content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // MAIN change handlers — each mirrors its IMGUI block EXACTLY (same side effects, order)
        // ----------------------------------------------------------------------------------------

        // Radar.cs:1176-1181.
        private void OnUguiRadarToggleClicked()
        {
            this.ToggleRadar();
        }

        // Radar.cs:1185-1217 — 28 flags true (source order), CheckRadarAutoToggle, then RunRadar
        // ONLY while the radar is active.
        private void OnUguiRadarSelectAllLootsClicked()
        {
            this.showMushroomRadar = true;
            this.showOysterMushroomRadar = true;
            this.showButtonMushroomRadar = true;
            this.showPennyBunRadar = true;
            this.showShiitakeRadar = true;
            this.showTruffleRadar = true;
            this.showFiddleheadRadar = true;
            this.showTallMustardRadar = true;
            this.showBurdockRadar = true;
            this.showMustardGreensRadar = true;
            this.showGlasswortRadar = true;
            this.showSeaGrapeRadar = true;
            this.showWakameRadar = true;
            this.showContaminatedRadar = true;
            this.showBlueberryRadar = true;
            this.showRaspberryRadar = true;
            this.showStoneRadar = true;
            this.showOreRadar = true;
            this.showTreeRadar = true;
            this.showRareTreeRadar = true;
            this.showAppleTreeRadar = true;
            this.showOrangeTreeRadar = true;
            this.showBubbleRadar = true;
            this.showBirdRadar = true;
            this.showInsectRadar = true;
            this.showFishShadowRadar = true;
            this.showMeteorRadar = true;
            this.showOtherPlayersRadar = true;
            this.CheckRadarAutoToggle();
            if (this.isRadarActive)
            {
                this.RunRadar();
            }

            UguiShellRadarMainHandle handle = this.uguiShellRadarMain;
            if (handle != null)
            {
                handle.NextSlowSyncAt = 0f; // summaries refresh next frame
            }
        }

        // Radar.cs:1218-1250 — same 28 flags false, CheckRadarAutoToggle, then Cleanup()
        // UNCONDITIONALLY (not the Select-All shape — deliberate asymmetry in source).
        private void OnUguiRadarClearAllLootsClicked()
        {
            this.showMushroomRadar = false;
            this.showOysterMushroomRadar = false;
            this.showButtonMushroomRadar = false;
            this.showPennyBunRadar = false;
            this.showShiitakeRadar = false;
            this.showTruffleRadar = false;
            this.showFiddleheadRadar = false;
            this.showTallMustardRadar = false;
            this.showBurdockRadar = false;
            this.showMustardGreensRadar = false;
            this.showGlasswortRadar = false;
            this.showSeaGrapeRadar = false;
            this.showWakameRadar = false;
            this.showContaminatedRadar = false;
            this.showBlueberryRadar = false;
            this.showRaspberryRadar = false;
            this.showStoneRadar = false;
            this.showOreRadar = false;
            this.showTreeRadar = false;
            this.showRareTreeRadar = false;
            this.showAppleTreeRadar = false;
            this.showOrangeTreeRadar = false;
            this.showBubbleRadar = false;
            this.showBirdRadar = false;
            this.showInsectRadar = false;
            this.showFishShadowRadar = false;
            this.showMeteorRadar = false;
            this.showOtherPlayersRadar = false;
            this.CheckRadarAutoToggle();
            this.Cleanup();

            UguiShellRadarMainHandle handle = this.uguiShellRadarMain;
            if (handle != null)
            {
                handle.NextSlowSyncAt = 0f;
            }
        }

        // Radar.cs:1260-1264 — the button draws always; the click only acts while the radar is
        // active (`&& this.isRadarActive` on the click result).
        private void OnUguiRadarForceRefreshClicked()
        {
            if (!this.isRadarActive)
            {
                return;
            }
            this.RunRadar();
        }

        // ----------------------------------------------------------------------------------------
        // SETTINGS sub-tab (display index 1) — DrawRadarSettingsTab
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellRadarSettingsHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float CardWidth;               // cards at x=8, 8px right margin

            // Range card
            public GameObject MaxDistanceLabel;
            public string MaxDistanceShown;
            public Slider MaxDistanceSlider;

            // Resource Display (naked rows between the cards, like the source)
            public UguiRadarSegmentHandle ModeEspSegment;
            public UguiRadarSegmentHandle ModeMapSegment;

            // Game-Map-only conditional rows (fixed positions; relayout flips visibility)
            public GameObject TrackLimitLabel;
            public string TrackLimitShown;
            public Slider TrackLimitSlider;
            public GameObject BigMapCaption;
            public UguiRadarSegmentHandle BigMapSegment;
            public GameObject AvatarsCaption;
            public UguiRadarSegmentHandle AvatarsSegment;

            // Visual ESP card (repositioned by the relayout)
            public GameObject VisualCard;
            public UguiRadarSegmentHandle StyleBeaconSegment;
            public UguiRadarSegmentHandle StyleCardSegment;
            public UguiRadarSegmentHandle StyleMinimalSegment;
            public Toggle ShowDistanceToggle;
            public Toggle ShowConnectorToggle;
            public Toggle ShowOffscreenToggle;
            public Toggle ShowGroundRingToggle;
            public GameObject ScaleLabel;
            public string ScaleShown;
            public Slider ScaleSlider;
            public GameObject OpacityLabel;
            public string OpacityShown;
            public Slider OpacitySlider;
            public GameObject MarkerLimitLabel;
            public string MarkerLimitShown;
            public Slider MarkerLimitSlider;

            public int LayoutSignature = -1;      // radarDisplayMode
            public int ErrorCount;
        }

        private UguiShellRadarSettingsHandle uguiShellRadarSettings;

        // Vertical cursor replays DrawRadarSettingsTab exactly (base y=8): 8 header +52 → 60
        // Range card(112) +128 → 188 Resource Display +64 → 252 [Game-Map rows +36 ×3 → 360]
        // Visual card(332) +348. The source's 520px panel adapts to the cell (cards at x=8,
        // width = content-16; the source's own proportions: 16px insets, sliders at x=190 with a
        // 16px right inset — Foraging's width-adaptation precedent). All controls including the
        // conditional rows are built ONCE; RelayoutUguiShellRadarSettings owns visibility + the
        // Visual card position + content height. Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellRadarSettingsContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellRadarSettings = null;

            UguiShellRadarSettingsHandle handle = new UguiShellRadarSettingsHandle();
            GameObject block = this.CreateUguiGo("RadarSettingsContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            Transform scrollContent;
            GameObject scroll = this.CreateUguiScrollView(block.transform, "Scroll", 10f, out scrollContent);
            PlaceUguiTopLeft(scroll, 0f, 0f, w, h);
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

            float contentW = w - 22f;      // viewport insets: 4 left + 18 right
            float cardW = contentW - 16f;  // cards at x=8, 8px right margin
            handle.CardWidth = cardW;
            Color textColor = this.UguiKitTextColor();
            // IMGUI subStyle: fontSize 11, uiText @ 0.72 (Radar.cs:946-948).
            Color subColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.72f);

            // ---- Header row (Radar.cs:950-956): bold-16 title, subStyle subtitle, DANGER reset.
            GameObject title = this.CreateUguiLabel(scrollContent, "Title", this.L("Radar Settings"),
                16f, textColor, false);
            this.TrySetUguiLabelBold(title);
            PlaceUguiTopLeft(title, 8f, 8f, 280f, 24f);
            GameObject subtitle = this.CreateUguiLabel(scrollContent, "Subtitle",
                this.L("Range, visual style, and overlay behavior."), 11f, subColor, false);
            PlaceUguiTopLeft(subtitle, 8f, 28f, 360f, 18f);
            GameObject resetBtn = this.CreateUguiDangerButton(scrollContent, "ResetDefaults",
                this.L("Reset Defaults"), new System.Action(this.OnUguiRadarResetDefaultsClicked));
            PlaceUguiTopLeft(resetBtn, 8f + cardW - 118f, 12f, 118f, 28f);
            this.TrySetUguiButtonLabelSize(resetBtn, 12f);

            // ---- Range card (Radar.cs:958-985): themePanelStyle box + card outline.
            GameObject rangeCard = this.CreateUguiGo("RangeCard", scrollContent);
            PlaceUguiTopLeft(rangeCard, 8f, 60f, cardW, 112f);
            this.AddUguiImage(rangeCard, this.UguiKitPanelBg(), true, 1f);
            this.AddUguiRingOverlay(rangeCard, new Color(1f, 1f, 1f,
                Mathf.Clamp(0.05f + (this.uiPanelAlpha * 0.05f), 0.05f, 0.10f)), 1f);

            GameObject rangeTitle = this.CreateUguiLabel(rangeCard.transform, "CardTitle",
                this.L("Range"), 13f, textColor, false);
            this.TrySetUguiLabelBold(rangeTitle);
            PlaceUguiTopLeft(rangeTitle, 16f, 12f, 240f, 20f);

            handle.MaxDistanceShown = this.LF("Radar Max Distance: {0}m", this.radarMaxDistance.ToString("F0"));
            handle.MaxDistanceLabel = this.CreateUguiBodyLabel(rangeCard.transform, "MaxDistanceLabel",
                handle.MaxDistanceShown, 13f);
            PlaceUguiTopLeft(handle.MaxDistanceLabel, 16f, 42f, cardW - 32f, 20f);
            // Source rounds to wholes; wholeNumbers snaps natively (Fishing Scan Range precedent).
            // The 995→1000 snap lives in the handler.
            handle.MaxDistanceSlider = this.CreateUguiSlider(rangeCard.transform, "MaxDistanceSlider",
                25f, 1000f, this.radarMaxDistance, true,
                new System.Action<float>(this.OnUguiRadarMaxDistanceChanged));
            PlaceUguiTopLeft(handle.MaxDistanceSlider.gameObject, 16f, 68f, cardW - 32f, 20f);

            // ---- Resource Display (Radar.cs:987-1008) — naked rows on the tab background.
            GameObject modeCaption = this.CreateUguiLabel(scrollContent, "ModeCaption",
                this.L("Resource Display"), 11f, subColor, false);
            PlaceUguiTopLeft(modeCaption, 24f, 194f, 200f, 18f);
            float modeSegWidth = (cardW - 32f - 10f) / 2f;
            handle.ModeEspSegment = this.CreateUguiRadarSegmentButton(scrollContent, "ModeEsp",
                new System.Action(() => this.OnUguiRadarDisplayModeSegmentClicked(0)));
            PlaceUguiTopLeft(handle.ModeEspSegment.Root, 24f, 214f, modeSegWidth, 30f);
            this.ApplyUguiRadarSegmentState(handle.ModeEspSegment, this.L("ESP Overlay"), this.radarDisplayMode == 0);
            handle.ModeMapSegment = this.CreateUguiRadarSegmentButton(scrollContent, "ModeMap",
                new System.Action(() => this.OnUguiRadarDisplayModeSegmentClicked(1)));
            PlaceUguiTopLeft(handle.ModeMapSegment.Root, 24f + modeSegWidth + 10f, 214f, modeSegWidth, 30f);
            this.ApplyUguiRadarSegmentState(handle.ModeMapSegment, this.L("Game Map"), this.radarDisplayMode == 1);

            // ---- Game-Map-only rows (Radar.cs:1011-1039) — fixed positions at 252/288/324;
            // relayout flips their visibility only (Foraging aura-row idiom).
            handle.TrackLimitShown = this.LF("Map Markers (nearest): {0}", this.radarGameTrackLimit.ToString());
            handle.TrackLimitLabel = this.CreateUguiLabel(scrollContent, "TrackLimitLabel",
                handle.TrackLimitShown, 11f, subColor, false);
            PlaceUguiTopLeft(handle.TrackLimitLabel, 24f, 252f, 240f, 20f);
            handle.TrackLimitSlider = this.CreateUguiSlider(scrollContent, "TrackLimitSlider",
                1f, 30f, this.radarGameTrackLimit, true,
                new System.Action<float>(this.OnUguiRadarTrackLimitChanged));
            PlaceUguiTopLeft(handle.TrackLimitSlider.gameObject, 198f, 253f, cardW - 206f, 20f);

            handle.BigMapCaption = this.CreateUguiLabel(scrollContent, "BigMapCaption",
                this.L("Show on big map"), 11f, subColor, false);
            PlaceUguiTopLeft(handle.BigMapCaption, 24f, 294f, 170f, 20f);
            handle.BigMapSegment = this.CreateUguiRadarSegmentButton(scrollContent, "BigMapSegment",
                new System.Action(this.OnUguiRadarBigMapSpotsSegmentClicked));
            PlaceUguiTopLeft(handle.BigMapSegment.Root, 198f, 288f, cardW - 206f, 30f);
            this.ApplyUguiRadarSegmentState(handle.BigMapSegment,
                this.radarBigMapSpots ? this.L("On") : this.L("Off"), this.radarBigMapSpots);

            handle.AvatarsCaption = this.CreateUguiLabel(scrollContent, "AvatarsCaption",
                this.L("Player Avatars (all)"), 11f, subColor, false);
            PlaceUguiTopLeft(handle.AvatarsCaption, 24f, 330f, 170f, 20f);
            handle.AvatarsSegment = this.CreateUguiRadarSegmentButton(scrollContent, "AvatarsSegment",
                new System.Action(this.OnUguiRadarPlayerAvatarsSegmentClicked));
            PlaceUguiTopLeft(handle.AvatarsSegment.Root, 198f, 324f, cardW - 206f, 30f);
            this.ApplyUguiRadarSegmentState(handle.AvatarsSegment,
                this.radarPlayerAvatarsAll ? this.L("On") : this.L("Off"), this.radarPlayerAvatarsAll);

            // ---- Visual ESP card (Radar.cs:1041-1117) — y owned by the relayout.
            GameObject visualCard = this.CreateUguiGo("VisualCard", scrollContent);
            this.AddUguiImage(visualCard, this.UguiKitPanelBg(), true, 1f);
            this.AddUguiRingOverlay(visualCard, new Color(1f, 1f, 1f,
                Mathf.Clamp(0.05f + (this.uiPanelAlpha * 0.05f), 0.05f, 0.10f)), 1f);
            handle.VisualCard = visualCard;

            GameObject visualTitle = this.CreateUguiLabel(visualCard.transform, "CardTitle",
                this.L("Visual ESP"), 13f, textColor, false);
            this.TrySetUguiLabelBold(visualTitle);
            PlaceUguiTopLeft(visualTitle, 16f, 12f, 220f, 20f);
            GameObject visualSub = this.CreateUguiLabel(visualCard.transform, "CardSub",
                this.L("Clean screen-space overlay for resources and radar targets."), 11f, subColor, false);
            PlaceUguiTopLeft(visualSub, 16f, 32f, cardW - 32f, 18f);

            GameObject styleCaption = this.CreateUguiLabel(visualCard.transform, "StyleCaption",
                this.L("Overlay Style"), 11f, subColor, false);
            PlaceUguiTopLeft(styleCaption, 16f, 62f, 180f, 18f);

            float styleSegWidth = (cardW - 32f - 10f * 2f) / 3f;
            handle.StyleBeaconSegment = this.CreateUguiRadarSegmentButton(visualCard.transform, "StyleBeacon",
                new System.Action(() => this.OnUguiRadarVisualEspStyleSegmentClicked(0)));
            PlaceUguiTopLeft(handle.StyleBeaconSegment.Root, 16f, 84f, styleSegWidth, 34f);
            this.ApplyUguiRadarSegmentState(handle.StyleBeaconSegment, this.L("Beacon"), this.resourceVisualEspStyle == 0);
            handle.StyleCardSegment = this.CreateUguiRadarSegmentButton(visualCard.transform, "StyleCard",
                new System.Action(() => this.OnUguiRadarVisualEspStyleSegmentClicked(1)));
            PlaceUguiTopLeft(handle.StyleCardSegment.Root, 16f + styleSegWidth + 10f, 84f, styleSegWidth, 34f);
            this.ApplyUguiRadarSegmentState(handle.StyleCardSegment, this.L("Card"), this.resourceVisualEspStyle == 1);
            handle.StyleMinimalSegment = this.CreateUguiRadarSegmentButton(visualCard.transform, "StyleMinimal",
                new System.Action(() => this.OnUguiRadarVisualEspStyleSegmentClicked(2)));
            PlaceUguiTopLeft(handle.StyleMinimalSegment.Root, 16f + (styleSegWidth + 10f) * 2f, 84f, styleSegWidth, 34f);
            this.ApplyUguiRadarSegmentState(handle.StyleMinimalSegment, this.L("Minimal"), this.resourceVisualEspStyle == 2);

            // 2×2 grid of NORMAL switch-style toggles (source: DrawSwitchToggle, Radar.cs:1083-
            // 1086 — NOT the segmented style; ported as kit checkboxes like every prior round's
            // DrawSwitchToggle). DrawSwitchToggle localizes internally, so L() here.
            float toggleWidth = (cardW - 48f) * 0.5f;
            handle.ShowDistanceToggle = this.CreateUguiCheckbox(visualCard.transform, "ShowDistance",
                this.L("Show Distance"), this.resourceVisualEspShowDistance,
                new System.Action<bool>(this.OnUguiRadarEspShowDistanceToggled));
            PlaceUguiTopLeft(handle.ShowDistanceToggle.gameObject, 16f, 136f, toggleWidth, 24f);
            handle.ShowConnectorToggle = this.CreateUguiCheckbox(visualCard.transform, "ShowConnector",
                this.L("Connector Lines"), this.resourceVisualEspShowConnector,
                new System.Action<bool>(this.OnUguiRadarEspShowConnectorToggled));
            PlaceUguiTopLeft(handle.ShowConnectorToggle.gameObject, 24f + toggleWidth, 136f, toggleWidth, 24f);
            handle.ShowOffscreenToggle = this.CreateUguiCheckbox(visualCard.transform, "ShowOffscreen",
                this.L("Offscreen Chips"), this.resourceVisualEspShowOffscreen,
                new System.Action<bool>(this.OnUguiRadarEspShowOffscreenToggled));
            PlaceUguiTopLeft(handle.ShowOffscreenToggle.gameObject, 16f, 166f, toggleWidth, 24f);
            handle.ShowGroundRingToggle = this.CreateUguiCheckbox(visualCard.transform, "ShowGroundRing",
                this.L("Ground Ring"), this.resourceVisualEspShowGroundRing,
                new System.Action<bool>(this.OnUguiRadarEspShowGroundRingToggled));
            PlaceUguiTopLeft(handle.ShowGroundRingToggle.gameObject, 24f + toggleWidth, 166f, toggleWidth, 24f);

            // Three sliders (Radar.cs:1092-1117) — rounding differs per row (see handlers).
            handle.ScaleShown = this.LF("Overlay Scale: {0}", this.resourceVisualEspScale.ToString("F2"));
            handle.ScaleLabel = this.CreateUguiBodyLabel(visualCard.transform, "ScaleLabel", handle.ScaleShown, 13f);
            PlaceUguiTopLeft(handle.ScaleLabel, 16f, 208f, 220f, 20f);
            handle.ScaleSlider = this.CreateUguiSlider(visualCard.transform, "ScaleSlider",
                0.8f, 1.5f, this.resourceVisualEspScale, false,
                new System.Action<float>(this.OnUguiRadarEspScaleChanged));
            PlaceUguiTopLeft(handle.ScaleSlider.gameObject, 190f, 209f, cardW - 206f, 20f);

            handle.OpacityShown = this.LF("Overlay Opacity: {0}", this.resourceVisualEspOpacity.ToString("F2"));
            handle.OpacityLabel = this.CreateUguiBodyLabel(visualCard.transform, "OpacityLabel", handle.OpacityShown, 13f);
            PlaceUguiTopLeft(handle.OpacityLabel, 16f, 250f, 220f, 20f);
            handle.OpacitySlider = this.CreateUguiSlider(visualCard.transform, "OpacitySlider",
                0.35f, 1f, this.resourceVisualEspOpacity, false,
                new System.Action<float>(this.OnUguiRadarEspOpacityChanged));
            PlaceUguiTopLeft(handle.OpacitySlider.gameObject, 190f, 251f, cardW - 206f, 20f);

            handle.MarkerLimitShown = this.LF("Overlay Marker Limit: {0}", this.resourceVisualEspMaxMarkers.ToString());
            handle.MarkerLimitLabel = this.CreateUguiBodyLabel(visualCard.transform, "MarkerLimitLabel", handle.MarkerLimitShown, 13f);
            PlaceUguiTopLeft(handle.MarkerLimitLabel, 16f, 292f, 240f, 20f);
            handle.MarkerLimitSlider = this.CreateUguiSlider(visualCard.transform, "MarkerLimitSlider",
                20f, 200f, this.resourceVisualEspMaxMarkers, true,
                new System.Action<float>(this.OnUguiRadarEspMarkerLimitChanged));
            PlaceUguiTopLeft(handle.MarkerLimitSlider.gameObject, 190f, 293f, cardW - 206f, 20f);

            handle.LayoutSignature = this.radarDisplayMode;
            this.RelayoutUguiShellRadarSettings(handle);

            handle.Root = block;
            this.uguiShellRadarSettings = handle;
            return block;
        }

        // The conditional block's IMGUI cursor (Radar.cs:1011-1041): mode == 1 inserts 3 rows of
        // 36 between the mode picker (ends 252) and the Visual card (252 or 360); the card is 332
        // high and the source advances 348 past its top for the tab height.
        private void RelayoutUguiShellRadarSettings(UguiShellRadarSettingsHandle handle)
        {
            if (handle == null)
            {
                return;
            }
            bool gameMap = this.radarDisplayMode == 1;

            SetUguiGoActive(handle.TrackLimitLabel, gameMap);
            SetUguiGoActive(handle.TrackLimitSlider != null ? handle.TrackLimitSlider.gameObject : null, gameMap);
            SetUguiGoActive(handle.BigMapCaption, gameMap);
            SetUguiGoActive(handle.BigMapSegment != null ? handle.BigMapSegment.Root : null, gameMap);
            SetUguiGoActive(handle.AvatarsCaption, gameMap);
            SetUguiGoActive(handle.AvatarsSegment != null ? handle.AvatarsSegment.Root : null, gameMap);

            float visualY = gameMap ? 360f : 252f;
            if (handle.VisualCard != null)
            {
                PlaceUguiTopLeft(handle.VisualCard, 8f, visualY, handle.CardWidth, 332f);
            }
            this.SetUguiScrollContentHeight(handle.ScrollContent, visualY + 348f + 8f);
        }

        // ----------------------------------------------------------------------------------------
        // SETTINGS per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellRadarSettingsOnUpdate()
        {
            UguiShellRadarSettingsHandle handle = this.uguiShellRadarSettings;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellRadarSubTabActive(UguiShellRadarSettingsSubIndex))
            {
                return;
            }

            try
            {
                // Slider re-syncs + value labels (external IMGUI edits and Reset Defaults) —
                // WithoutNotify only; the labels format the live field like the IMGUI twin.
                if (handle.MaxDistanceSlider != null
                    && Mathf.Abs(handle.MaxDistanceSlider.value - this.radarMaxDistance) > 0.0005f)
                {
                    handle.MaxDistanceSlider.SetValueWithoutNotify(this.radarMaxDistance);
                }
                this.SyncUguiSelfLabelText(handle.MaxDistanceLabel, ref handle.MaxDistanceShown,
                    this.LF("Radar Max Distance: {0}m", this.radarMaxDistance.ToString("F0")));

                if (handle.TrackLimitSlider != null
                    && Mathf.RoundToInt(handle.TrackLimitSlider.value) != this.radarGameTrackLimit)
                {
                    handle.TrackLimitSlider.SetValueWithoutNotify(this.radarGameTrackLimit);
                }
                this.SyncUguiSelfLabelText(handle.TrackLimitLabel, ref handle.TrackLimitShown,
                    this.LF("Map Markers (nearest): {0}", this.radarGameTrackLimit.ToString()));

                if (handle.ScaleSlider != null
                    && Mathf.Abs(handle.ScaleSlider.value - this.resourceVisualEspScale) > 0.0005f)
                {
                    handle.ScaleSlider.SetValueWithoutNotify(this.resourceVisualEspScale);
                }
                this.SyncUguiSelfLabelText(handle.ScaleLabel, ref handle.ScaleShown,
                    this.LF("Overlay Scale: {0}", this.resourceVisualEspScale.ToString("F2")));

                if (handle.OpacitySlider != null
                    && Mathf.Abs(handle.OpacitySlider.value - this.resourceVisualEspOpacity) > 0.0005f)
                {
                    handle.OpacitySlider.SetValueWithoutNotify(this.resourceVisualEspOpacity);
                }
                this.SyncUguiSelfLabelText(handle.OpacityLabel, ref handle.OpacityShown,
                    this.LF("Overlay Opacity: {0}", this.resourceVisualEspOpacity.ToString("F2")));

                if (handle.MarkerLimitSlider != null
                    && Mathf.RoundToInt(handle.MarkerLimitSlider.value) != this.resourceVisualEspMaxMarkers)
                {
                    handle.MarkerLimitSlider.SetValueWithoutNotify(this.resourceVisualEspMaxMarkers);
                }
                this.SyncUguiSelfLabelText(handle.MarkerLimitLabel, ref handle.MarkerLimitShown,
                    this.LF("Overlay Marker Limit: {0}", this.resourceVisualEspMaxMarkers.ToString()));

                // Switch-toggle re-syncs.
                this.SyncUguiToggleFromField(handle.ShowDistanceToggle, this.resourceVisualEspShowDistance);
                this.SyncUguiToggleFromField(handle.ShowConnectorToggle, this.resourceVisualEspShowConnector);
                this.SyncUguiToggleFromField(handle.ShowOffscreenToggle, this.resourceVisualEspShowOffscreen);
                this.SyncUguiToggleFromField(handle.ShowGroundRingToggle, this.resourceVisualEspShowGroundRing);

                // Segment states (cached label+selected compares inside Apply — cheap when idle).
                this.ApplyUguiRadarSegmentState(handle.ModeEspSegment, this.L("ESP Overlay"), this.radarDisplayMode == 0);
                this.ApplyUguiRadarSegmentState(handle.ModeMapSegment, this.L("Game Map"), this.radarDisplayMode == 1);
                this.ApplyUguiRadarSegmentState(handle.StyleBeaconSegment, this.L("Beacon"), this.resourceVisualEspStyle == 0);
                this.ApplyUguiRadarSegmentState(handle.StyleCardSegment, this.L("Card"), this.resourceVisualEspStyle == 1);
                this.ApplyUguiRadarSegmentState(handle.StyleMinimalSegment, this.L("Minimal"), this.resourceVisualEspStyle == 2);
                this.ApplyUguiRadarSegmentState(handle.BigMapSegment,
                    this.radarBigMapSpots ? this.L("On") : this.L("Off"), this.radarBigMapSpots);
                this.ApplyUguiRadarSegmentState(handle.AvatarsSegment,
                    this.radarPlayerAvatarsAll ? this.L("On") : this.L("Off"), this.radarPlayerAvatarsAll);

                // Conditional-layout signature (the 3 Game-Map rows + the Visual card position).
                if (this.radarDisplayMode != handle.LayoutSignature)
                {
                    handle.LayoutSignature = this.radarDisplayMode;
                    this.RelayoutUguiShellRadarSettings(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Radar settings content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // SETTINGS change handlers — each mirrors its IMGUI block EXACTLY
        // ----------------------------------------------------------------------------------------

        // Radar.cs:952-955 — one method call; ResetRadarSettingsToDefaults owns the 14 field
        // resets + queued save + conditional force-rescan + notification.
        private void OnUguiRadarResetDefaultsClicked()
        {
            this.ResetRadarSettingsToDefaults();
        }

        // Radar.cs:969-984 — Mathf.Round, then the 995→1000 snap-to-max (a snap, not a clamp:
        // dragging past the threshold jumps the value to the ceiling), THEN the change compare
        // (source order: snap before compare, so 996 with the field already at 1000 is a no-op).
        // On change: queued save + force-rescan while the radar is active.
        private void OnUguiRadarMaxDistanceChanged(float value)
        {
            float rounded = Mathf.Round(value);
            if (rounded >= 995f)
            {
                rounded = 1000f;
            }
            if (Mathf.Abs(rounded - this.radarMaxDistance) <= 0.0001f)
            {
                return;
            }
            this.radarMaxDistance = rounded;
            this.QueueRadarSettingsSave();
            if (this.isRadarActive)
            {
                this.lastScanTime = 0f;
                this.bubbleRadarForceRefresh = true;
            }
        }

        // Radar.cs:991-1008 — the click-time guard is inside the source's click block.
        private void OnUguiRadarDisplayModeSegmentClicked(int mode)
        {
            if (this.radarDisplayMode == mode)
            {
                return;
            }
            this.radarDisplayMode = mode;
            this.OnRadarDisplayModeChanged();
            this.QueueRadarSettingsSave();
            // Immediate relayout for click feedback (the per-frame signature covers IMGUI edits).
            UguiShellRadarSettingsHandle handle = this.uguiShellRadarSettings;
            if (handle != null)
            {
                handle.LayoutSignature = this.radarDisplayMode;
                this.RelayoutUguiShellRadarSettings(handle);
            }
        }

        // Radar.cs:1013-1019 — int clamp 1..30.
        private void OnUguiRadarTrackLimitChanged(float value)
        {
            int rounded = Mathf.Clamp(Mathf.RoundToInt(value), 1, 30);
            if (rounded == this.radarGameTrackLimit)
            {
                return;
            }
            this.radarGameTrackLimit = rounded;
            this.QueueRadarSettingsSave();
        }

        // Radar.cs:1023-1028 — unconditional invert on click.
        private void OnUguiRadarBigMapSpotsSegmentClicked()
        {
            this.radarBigMapSpots = !this.radarBigMapSpots;
            this.QueueRadarSettingsSave();
        }

        // Radar.cs:1032-1037.
        private void OnUguiRadarPlayerAvatarsSegmentClicked()
        {
            this.radarPlayerAvatarsAll = !this.radarPlayerAvatarsAll;
            this.QueueRadarSettingsSave();
        }

        // Radar.cs:1052-1075 — same guard shape as the display-mode segments.
        private void OnUguiRadarVisualEspStyleSegmentClicked(int style)
        {
            if (this.resourceVisualEspStyle == style)
            {
                return;
            }
            this.resourceVisualEspStyle = style;
            this.QueueRadarSettingsSave();
        }

        // Radar.cs:1077-1090 — the source queues ONE save when any of the four changed; the
        // per-toggle queue here is identical net behavior (the queue is a debounced flag).
        private void OnUguiRadarEspShowDistanceToggled(bool value)
        {
            if (value == this.resourceVisualEspShowDistance)
            {
                return;
            }
            this.resourceVisualEspShowDistance = value;
            this.QueueRadarSettingsSave();
        }

        private void OnUguiRadarEspShowConnectorToggled(bool value)
        {
            if (value == this.resourceVisualEspShowConnector)
            {
                return;
            }
            this.resourceVisualEspShowConnector = value;
            this.QueueRadarSettingsSave();
        }

        private void OnUguiRadarEspShowOffscreenToggled(bool value)
        {
            if (value == this.resourceVisualEspShowOffscreen)
            {
                return;
            }
            this.resourceVisualEspShowOffscreen = value;
            this.QueueRadarSettingsSave();
        }

        private void OnUguiRadarEspShowGroundRingToggled(bool value)
        {
            if (value == this.resourceVisualEspShowGroundRing)
            {
                return;
            }
            this.resourceVisualEspShowGroundRing = value;
            this.QueueRadarSettingsSave();
        }

        // Radar.cs:1093-1099 — round to 2 decimals (Mathf.Round(x*100)/100).
        private void OnUguiRadarEspScaleChanged(float value)
        {
            float rounded = Mathf.Round(value * 100f) / 100f;
            if (Mathf.Abs(rounded - this.resourceVisualEspScale) <= 0.0001f)
            {
                return;
            }
            this.resourceVisualEspScale = rounded;
            this.QueueRadarSettingsSave();
        }

        // Radar.cs:1102-1108 — round to 2 decimals.
        private void OnUguiRadarEspOpacityChanged(float value)
        {
            float rounded = Mathf.Round(value * 100f) / 100f;
            if (Mathf.Abs(rounded - this.resourceVisualEspOpacity) <= 0.0001f)
            {
                return;
            }
            this.resourceVisualEspOpacity = rounded;
            this.QueueRadarSettingsSave();
        }

        // Radar.cs:1111-1117 — plain int clamp 20..200 (NOT the 2-decimal rounding).
        private void OnUguiRadarEspMarkerLimitChanged(float value)
        {
            int rounded = Mathf.Clamp(Mathf.RoundToInt(value), 20, 200);
            if (rounded == this.resourceVisualEspMaxMarkers)
            {
                return;
            }
            this.resourceVisualEspMaxMarkers = rounded;
            this.QueueRadarSettingsSave();
        }
    }
}
