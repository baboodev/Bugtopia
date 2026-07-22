using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, Features round 7 of 8 (migration plan item 11): the
    // AUTO SELL sub-tab — DrawAutoSellTab (HeartopiaComplete.AutoSell.cs:51-404), dispatched from
    // the automationSubTab == 4 branch (HeartopiaComplete.Gui.cs:1281-1284), display sub-index 4
    // (the tabs list {"Main","Food & Repair","Snow Sculpting","Auto Buy","Auto Sell","Mass Cook",
    // "Puzzle","Pet Care"} maps display indices to automationSubTab 0-7 exactly). The remaining
    // two subs (Mass Cook 5, Pet Care 7) are separate future rounds and keep the shell
    // placeholder.
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawer and every backend method it calls stay fully functional and untouched —
    //    this file only READS the same autoSell* fields and CALLS the same methods (all
    //    this.-accessible partial-class state; ZERO backend interop additions). Two independent
    //    rendering paths over one backend.
    //  - Wiring is by STATIC display-position index (UguiShellFeaturesTabIndex = 2 +
    //    UguiShellFeaturesAutoSellSubIndex = 4, declared with their siblings in
    //    UguiShellTabIndices.cs), never label comparison. The processor gates on the SAME
    //    IsUguiShellFeaturesSubTabActive function the Main round established — no new gate.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // The grid (this round's actual point): the source's hand-rolled virtualized icon grid
    // (:263-336 — 6 columns, 92x88 cells, firstVisibleIndex/lastVisibleIndexExclusive from the
    // scroll position) migrates onto the kit's virtualized grid primitive (CreateUguiVirtualGrid /
    // UpdateUguiVirtualGridAssignments, HeartopiaComplete.UguiKit.cs) exactly the way its first
    // consumer bound it (HeartopiaComplete.UguiBagWarehouseContent.cs): a FIXED slot pool sized to
    // the largest viewport (the source's own 276px cap → ceil(276/88)+1 buffer = 5 rows x 6 = 30
    // slots) built ONCE; the core owns content height, scroll polling, slot positioning and
    // BoundIndex assignment; this consumer owns per-slot visuals and rebinds ONLY slots whose
    // per-slot signature (BoundIndex + entry fields + isSelected) changed. Never destroys or
    // creates cells after construction. Unlike Bag/Warehouse (grid = last element, full-height
    // LIVE-rail deviation) this grid KEEPS the source's Min(rows*88, 276) height: real content
    // follows below it (Clear / Open Sell Panel / Open Token Sell / status line), so the outer
    // scroll page needs the bounded height, and the relayout shifts that trailing block exactly
    // like the source's y-cursor.
    //
    // Source nuances verified against DrawAutoSellTab, replayed exactly:
    //  - SELECTION IDENTITY (:292-298 + click :321-331) — the already-reworked model (project
    //    memory "AutoSell identity-selection rework"): the highlight mirrors the sell-time match
    //    EXACTLY — identity (staticId + star, star compared through Math.Max(0, StarRate) against
    //    autoSellSelectedStar including star 0) in exact mode with a selected staticId; key + star
    //    (family/typed mode: OrdinalIgnoreCase key equality AND (selectedStar <= 0 || StarRate ==
    //    selectedStar)) otherwise. The two-branch shape is kept verbatim, NOT simplified. A cell
    //    click REPLACES the whole selection: key (family key in family mode), staticId (0 in
    //    family mode), star = Math.Max(0, StarRate) — the clicked cell's own identity always
    //    wins — plus the status/summary writes and save (:322-330).
    //  - MATCH ITEM text field (:142-150): free text, limit 80, NOT clamped; writing a NEW value
    //    also resets autoSellSelectedStaticId = 0 AND autoSellSelectedStar = 0 (hand-typed key =
    //    plain text matching, no identity/star constraint) + save. The on-edit handler keeps the
    //    source's newKey != autoSellItemKey guard; external IMGUI-twin edits ride the 0.5s
    //    SyncUguiInputFieldFromBackingField tick (Auto-Buy idiom), and the cell-click/Clear
    //    handlers mirror their key writes into the field immediately via SetTextWithoutNotify
    //    (IMGUI rebinds the TextField from the shared string every frame).
    //  - AUTO toggle (:122-134): on change nextAutoSellAt = 0f; on ENABLE also
    //    autoSellBackpackDirty = true (scan on the first tick); status "Enabled"/"Disabled";
    //    save; green (0.45,1,0.55) / red (1,0.55,0.55) toast — order verbatim.
    //  - Festival For Tokens (:157-163): change also zeroes autoSellFestivalCurrencyNextProbeAt
    //    (immediate re-probe) + save. Interval slider (:153-156): float, clamp [1,120], label
    //    LF("Interval: {0:F0}s"), save only when |new-prev| > 0.001 — kit slider stays
    //    fractional (wholeNumbers = false; the source never rounds the value, only the display).
    //  - CAP (:192-198): the label swaps to "Cap: ignored" and the int-slider [0,200] HIDES
    //    entirely while autoSellFullStack — a fixed-position conditional (no reflow in source
    //    either), driven through the layout signature. Keep Per Item (:199-202): int [0,200],
    //    always visible. Both int sliders are wholeNumbers = TRUE (int-contract precedent).
    //  - SOURCE DROPDOWN (:210-243, options :377-401): hand-rolled box+options → kit
    //    CreateUguiDropdown (Settings→Main precedent; out-bool listenerWired + per-frame poll
    //    fallback, Birds order). Options are the raw autoSellScanSourceLabels (source draws them
    //    raw). On pick the :392-398 block is replayed verbatim: source index, autoSellBagItems =
    //    null (grid back to "press scan"), autoSellBagItemScrollPos = Vector2.zero, status +
    //    summary writes, save. The scrollPos write is a shared DATA reset the source performs
    //    (the list is nulled in the same breath, so no in-flight IMGUI interaction exists to
    //    disturb — unlike the never-written dropdownOpen visual flag, forceOpenShopDropdownOpen
    //    precedent); the UGUI-local analog snaps the grid content back to the top. The :393
    //    autoSellScanSourceDropdownOpen=false write is IMGUI-only visual state — NOT reproduced.
    //    Stock-Dropdown nuance: re-picking the already-selected option fires no event (accepted,
    //    Birds/Auto-Buy) — the destructive pick body also keeps the Transfer-round index guard.
    //  - ROW ACTION BLOCKING (:224-237): IMGUI disables Sell Selected + Scan Items while the
    //    dropdown is open (GUI.enabled = !blockRowActions). Reproduced two ways: the expanded
    //    probe (transform.Find("Dropdown List"), the Food & Repair stripping-proof idiom —
    //    IsUguiFoodRepairDropdownExpanded is reused directly) drives SetUguiButtonInteractable on
    //    every expanded-state change, AND both click handlers re-check the probe at click time
    //    (Auto-Buy's same-frame-race insurance). The stock popup's full-screen blocker already
    //    swallows the first outside click on top of that.
    //  - SCAN (:230-236): autoSellBagItems = ScanBackpackForAutoSellItems() (never null), then
    //    status = "<label> list refreshed" when Count > 0, else "No <lower> items found" —
    //    verbatim, count-based. SELL SELECTED (:226-229): ExecuteDirectAutoSell(false).
    //  - GRID REGION states (:253-347), mutually exclusive: hide-flag → hint only ("item list is
    //    hidden. Scan Items still updates selected item data"); non-null non-empty → header
    //    LF("{0} Items ({1})") + the grid; non-null EMPTY → "No ... items found yet. Try again
    //    after the world finishes loading."; null → "Press Scan Items to read ... data. Icons
    //    load automatically from the game." The three hint strings share one label (Bag/Warehouse
    //    EmptyLabel idiom); all contain the LIVE scan-source label and re-sync on the text tick.
    //  - CELL visuals (:299-333): fill/outline themeTopTabActiveStyle + 2px when selected vs
    //    themeContentStyle + 1px (accent@0.18 fill + accent@0.8 ring vs ContentBg + the
    //    DrawCardOutline edge — the Bag/Warehouse mapping); icon 42x42 (TryGetAutoSellItemTexture,
    //    initials fallback via GetAutoSellItemInitials); count badge "x{N}" top-RIGHT only when
    //    Count > 0 and star badge "{N}*" top-LEFT only when StarRate > 0 — BOTH tinyStyle-derived
    //    in THIS source (:282-283), so both are muted subTabText here, not Bag/Warehouse's
    //    white/gold; name bottom 10pt UpperCenter wrapped. The whole cell is one always-active
    //    Button (the source's GUI.Button covers selected cells too — re-selecting is idempotent);
    //    no locked state exists on AutoSellBagItemEntry.
    //  - CLEAR (:349-357): danger-tier; key/staticId/star/status/summary reset + save, verbatim.
    //  - OPEN SELL PANEL / OPEN TOKEN SELL (:363-370): StartShopQuickSellOpenPanel() /
    //    StartTokenSellOpenPanel() — no toasts here (both notify internally). Below them ONE
    //    label combines both statuses: "Coins: " + (shopQuickSellStatus ?? "Idle.") + "     " +
    //    "Tokens: " + (tokenSellPanelStatus ?? "Idle.") (:372-374).
    //  - Localization roles: L("AUTO SELL") title; DrawSwitchToggle/action-button labels localize
    //    internally → ONE this.L each ("Auto", "Festival For Tokens", "Sell Whole Stack",
    //    "Sell Every Match", "Match Similar Items", "Hide Item List", "Sell Selected",
    //    "Scan Items", "Open Sell Panel", "Open Token Sell"); explicit L("Clear");
    //    LF("Interval: {0:F0}s"/"Cap: {0}"/"Keep Per Item: {0}"/"{0} Items ({1})"). Everything
    //    else ("Match Item", the examples line, star info, sell-mode help, "Cap: ignored",
    //    "Status", every status/hint/summary string, the dropdown options) is RAW in source and
    //    stays raw.
    //  - Narrow-panel adaptation (contentColW 484 vs the source's 580px panel — the Bag/Warehouse
    //    precedent): row ORDER, heights and y-advances are the source's; column x/widths scale to
    //    panelW (left/right settings columns 205 each, cells (panelW-22)/6 wide instead of 92).
    //    The selected card's title/key lines stop short of the Auto toggle instead of running
    //    under it (the source's 18px overlap would be 140px here); the meta line at y51 clears
    //    the toggle and keeps its full width. The dropdown-open inline growth (:239-243,
    //    :377-401) is an IMGUI reflow artifact — the stock popup overlays (Birds precedent).
    //
    // Cross-surface sync cadence: every gated frame — dropdown poll fallback FIRST, dropdown
    // external re-sync, the row-blocking expanded edge, 6 toggle re-syncs, 3 slider re-syncs +
    // the five fast labels (interval/cap/keep values, star info, sell-mode help — IMGUI drew all
    // of them per frame), the layout signature (grid state + fullStack), then the grid sync
    // (assignment + per-slot signature rebinds; the backend auto-tick rescans autoSellBagItems on
    // its own, so list identity/count flips arrive from OFF-surface too). Card/status/hint texts
    // ride the 0.25s tick (+ an immediate pass after actions and on signature flips); icon
    // retries (async loads land later) the 0.5s tick; the Match Item external re-sync the 0.5s
    // tick. Per-frame sync disabled after 3 consecutive errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // Fixed layout rows above the dynamic region (block-local Y, mirrors the IMGUI y-cursor:
        // title 12+24 → selected card 36+90 → settings card 126+216 → source row 342+40 → status
        // card 382+64 → dynamic 446).
        private const float UguiAutoSellDynamicTopY = 446f;
        private const int UguiAutoSellColumns = 6;        // IMGUI columns (:265)
        private const float UguiAutoSellCellH = 88f;      // IMGUI cellH (:264)
        private const float UguiAutoSellListMaxH = 276f;  // IMGUI listHeight cap (:267)

        private sealed class UguiAutoSellCellWidgets
        {
            public Image Bg;
            public Image Ring;               // AddUguiRingOverlay's "Ring" child (may be null)
            public Button CellButton;        // whole cell, always active (source :321)
            public Image Icon;
            public GameObject InitialsLabel;
            public GameObject CountBadge;    // top-right "x{N}" — muted (tinyStyle-derived, :282)
            public GameObject StarBadge;     // top-left "{N}*" — muted (tinyStyle-derived, :283)
            public GameObject NameLabel;
            public bool HasIcon;

            // Last-bound signature — compared field-by-field each gated frame; a mismatch (data
            // change, recycling via BoundIndex, selection change) rebinds JUST this slot.
            // SigIndex == int.MinValue forces a rebind on next sync.
            public int SigIndex = int.MinValue;
            public uint SigNetId;
            public int SigCount;
            public int SigStar;
            public bool SigSelected;
            public string SigName;
        }

        private sealed class UguiShellFeaturesAutoSellHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float PanelW;

            // Selected-item summary card
            public Image SelectedIcon;
            public GameObject SelectedIconFallback;   // the "?" label
            public GameObject SelectedTitle;
            public GameObject SelectedKeyLine;        // "Similar:/Exact: <key>  N*"
            public GameObject SelectedMeta;
            public Toggle AutoToggle;
            public string ShownSelTitle;
            public string ShownSelKey;
            public string ShownSelMeta;
            public bool ShownSelHasIcon;
            public int ShownSelIconTexId;

            // Settings card
            public InputField MatchKeyField;
            public string MatchKeySeen;               // external-change sync cache
            public GameObject IntervalLabel;
            public string IntervalShown;
            public Slider IntervalSlider;
            public Toggle FestivalToggle;
            public GameObject StarInfoLabel;
            public string StarInfoShown;
            public Toggle FullStackToggle;
            public Toggle EveryMatchToggle;
            public Toggle FamilyToggle;
            public Toggle HideListToggle;
            public GameObject SellModeHelpLabel;
            public string SellModeHelpShown;
            public GameObject CapLabel;
            public string CapShown;
            public Slider CapSlider;                  // hidden while autoSellFullStack (:193-198)
            public GameObject KeepLabel;
            public string KeepShown;
            public Slider KeepSlider;

            // Source row
            public Dropdown SourceDropdown;
            public bool SourceListenerWired;
            public int SourceLastValue;               // poll-fallback change detection
            public GameObject SellSelectedButton;
            public GameObject ScanItemsButton;
            public bool RowActionsBlocked;            // last-applied dropdown-open block state

            // Status card
            public GameObject StatusValue;
            public string StatusShown;
            public GameObject MatchSummaryLabel;
            public string MatchSummaryShown;

            // Grid region (positions owned by the relayout)
            public GameObject GridHeaderLabel;
            public string GridHeaderShown;
            public UguiVirtualGridHandle Grid;
            public readonly List<UguiAutoSellCellWidgets> Cells = new List<UguiAutoSellCellWidgets>();
            public GameObject ListHintLabel;          // hidden/empty/null states share this label
            public string ListHintShown;

            // Trailing block (positions owned by the relayout)
            public GameObject ClearButton;
            public GameObject OpenSellPanelButton;
            public GameObject OpenTokenSellButton;
            public GameObject SellPanelsStatusLabel;  // "Coins: ...     Tokens: ..."
            public string SellPanelsStatusShown;

            // Theme snapshot (the shell rebuild re-runs the builder on theme change).
            public Color CellNormalFill;
            public Color CellActiveFill;              // themeTopTabActiveStyle fill: accent @ 0.18
            public Color RingNormal;                  // DrawCardOutline edge
            public Color RingSelected;                // stands in for IMGUI's thickness-2 outline
            public Color TextColor;
            public Color MutedColor;                  // tinyStyle (both cell badges use it, :282-283)

            public int LayoutSignature = -1;
            public float NextTextSyncAt;              // 0.25s tick (statuses are live feedback)
            public float NextIconRetryAt;             // 0.5s tick — async icon loads land later
            public float NextSlowSyncAt;              // 0.5s tick — Match Item external re-sync
            public int ErrorCount;                    // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellFeaturesAutoSellHandle uguiShellFeaturesAutoSell;

        // ----------------------------------------------------------------------------------------
        // Live layout signature — grid-region state (hide flag + list null/empty/rows bucket) +
        // the Cap slider's visibility flag. rowsBucket saturates at 4: listHeight =
        // Min(rows*88, 276) is 88/176/264/276, identical for every rows >= 4 (:267).
        // ----------------------------------------------------------------------------------------

        private int ComputeUguiFeaturesAutoSellLayoutSignature()
        {
            List<AutoSellBagItemEntry> items = this.autoSellBagItems;
            bool scanned = items != null;
            int count = scanned ? items.Count : 0;
            int rowsBucket = (!this.autoSellHideBagItems && count > 0)
                ? Mathf.Min(4, Mathf.CeilToInt(count / (float)UguiAutoSellColumns))
                : 0;
            return (this.autoSellHideBagItems ? 1 : 0)
                | (scanned ? 2 : 0)
                | (count > 0 ? 4 : 0)
                | (this.autoSellFullStack ? 8 : 0)
                | (rowsBucket << 4);
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawAutoSellTab: title, selected-item card (icon + key/meta + Auto
        // toggle), the 2-column settings card, source dropdown + 2 action buttons, status card,
        // then the relayout-owned dynamic region (grid states → Clear → open-panel buttons →
        // combined status line) inside ONE outer scroll view. Everything — including the whole
        // grid pool — is built ONCE here in IMGUI source order; the relayout only repositions /
        // SetActives. Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellFeaturesAutoSellContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellFeaturesAutoSell = null;

            UguiShellFeaturesAutoSellHandle handle = new UguiShellFeaturesAutoSellHandle();
            GameObject block = this.CreateUguiGo("FeaturesAutoSellContent", parent);
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
            float panelW = contentWidth - 16f; // cards at x=8, 8px right margin
            handle.PanelW = panelW;

            // Theme snapshot — IMGUI style sources named per field (Bag/Warehouse mapping).
            Color accent = this.UguiKitAccent();
            handle.CellNormalFill = this.UguiKitContentBg();                        // themeContentStyle
            handle.CellActiveFill = new Color(accent.r, accent.g, accent.b, 0.18f); // themeTopTabActiveStyle fill
            handle.RingNormal = new Color(1f, 1f, 1f,
                Mathf.Clamp(0.05f + (this.uiPanelAlpha * 0.05f), 0.05f, 0.10f));    // DrawCardOutline edge
            handle.RingSelected = new Color(accent.r, accent.g, accent.b, 0.8f);    // thickness-2 outline stand-in
            handle.TextColor = this.UguiKitTextColor();
            handle.MutedColor = this.UguiKitMutedColor();                           // tinyStyle

            Color textColor = handle.TextColor;
            Color mutedColor = handle.MutedColor;

            // -------- Title (:95 — titleStyle 15 bold header color, L("AUTO SELL")) --------
            GameObject title = this.CreateUguiHeaderLabel(scrollContent, "Title", this.L("AUTO SELL"), 15f);
            PlaceUguiTopLeft(title, 8f, 12f, panelW, 22f);

            // -------- Selected-item summary card (:98-134 — themePanelStyle + outline, 78 tall).
            // Auto toggle right-aligned; title/key stop short of it (file header). --------
            GameObject selCard = this.CreateUguiGo("SelectedCard", scrollContent);
            PlaceUguiTopLeft(selCard, 8f, 36f, panelW, 78f);
            this.AddUguiImage(selCard, this.UguiKitPanelBg(), true, 1f);
            this.AddUguiRingOverlay(selCard, handle.RingNormal, 1f);

            GameObject iconBox = this.CreateUguiGo("IconBox", selCard.transform);
            PlaceUguiTopLeft(iconBox, 12f, 12f, 54f, 54f);
            this.AddUguiImage(iconBox, this.UguiKitContentBg(), true, 1.5f);      // themeContentStyle box
            this.AddUguiRingOverlay(iconBox, handle.RingNormal, 1.5f);

            GameObject selIconGo = this.CreateUguiGo("Icon", iconBox.transform);
            PlaceUguiTopLeft(selIconGo, 5f, 5f, 44f, 44f);
            handle.SelectedIcon = selIconGo.AddComponent<Image>();
            handle.SelectedIcon.raycastTarget = false;
            try { handle.SelectedIcon.preserveAspect = true; } catch { }
            selIconGo.SetActive(false);

            handle.SelectedIconFallback = this.CreateUguiLabel(iconBox.transform, "Fallback", "?", 20f, textColor, true);
            this.TrySetUguiLabelBold(handle.SelectedIconFallback);
            StretchUguiFill(handle.SelectedIconFallback, 0f, 0f, 0f, 0f);

            // The Auto checkbox reserved 152px for a ~60px "Auto" row, and the key line beside it
            // (the longest string on this card) paid for the difference by truncating. 92px still
            // clears the checkbox + label and hands the text back 60px.
            float autoToggleX = panelW - 92f;
            handle.SelectedTitle = this.CreateUguiLabel(selCard.transform, "SelTitle", "", 12f, textColor, false);
            this.TrySetUguiLabelBold(handle.SelectedTitle);                       // fieldLabelStyle
            PlaceUguiTopLeft(handle.SelectedTitle, 78f, 10f, autoToggleX - 78f - 8f, 22f);
            handle.SelectedKeyLine = this.CreateUguiLabel(selCard.transform, "SelKey", "", 11f, mutedColor, false);
            PlaceUguiTopLeft(handle.SelectedKeyLine, 78f, 32f, autoToggleX - 78f - 8f, 18f);
            handle.SelectedMeta = this.CreateUguiLabel(selCard.transform, "SelMeta", "", 11f, mutedColor, false);
            PlaceUguiTopLeft(handle.SelectedMeta, 78f, 51f, panelW - 90f, 18f);   // y51 clears the toggle

            handle.AutoToggle = this.CreateUguiCheckbox(selCard.transform, "AutoToggle",
                this.L("Auto"), this.autoSellEnabled,
                new System.Action<bool>(this.OnUguiFeaturesAutoSellAutoToggled));
            PlaceUguiTopLeft(handle.AutoToggle.gameObject, autoToggleX, 22f, 80f, 24f);

            // -------- Settings card (:137-203 — 204 tall, two columns; 205px columns here) ------
            GameObject setCard = this.CreateUguiGo("SettingsCard", scrollContent);
            PlaceUguiTopLeft(setCard, 8f, 126f, panelW, 204f);
            this.AddUguiImage(setCard, this.UguiKitPanelBg(), true, 1f);
            this.AddUguiRingOverlay(setCard, handle.RingNormal, 1f);

            float colW = 205f;
            float colRightX = 229f;
            float colRightW = panelW - colRightX - 12f;

            GameObject matchLabel = this.CreateUguiLabel(setCard.transform, "MatchItemLabel",
                "Match Item", 12f, textColor, false);                             // raw in source (:141)
            this.TrySetUguiLabelBold(matchLabel);
            PlaceUguiTopLeft(matchLabel, 12f, 9f, colW, 20f);
            handle.MatchKeySeen = this.autoSellItemKey ?? string.Empty;
            handle.MatchKeyField = this.CreateUguiInputField(setCard.transform, "MatchKeyField",
                handle.MatchKeySeen, 80, new System.Action<string>(this.OnUguiFeaturesAutoSellMatchKeyEdited));
            PlaceUguiTopLeft(handle.MatchKeyField.gameObject, 12f, 31f, colW, 24f);

            // Hint lines are the long ones on this card and they do not fit one line at colW, so
            // they get TrySetUguiLabelWrapped (wrap + TopLeft, so a 2-line hint hangs off the top
            // of its box rather than centring) and a 2-line box. Everything below each hint moved
            // down by the extra line; the card total is unchanged.
            GameObject examples = this.CreateUguiLabel(setCard.transform, "ExamplesLabel",
                "Examples: birdphoto, p_birdphoto, food_badfood", 11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(examples);
            PlaceUguiTopLeft(examples, 12f, 59f, colW, 28f);

            handle.IntervalShown = this.LF("Interval: {0:F0}s", this.autoSellInterval);
            handle.IntervalLabel = this.CreateUguiLabel(setCard.transform, "IntervalLabel",
                handle.IntervalShown, 12f, textColor, false);
            this.TrySetUguiLabelBold(handle.IntervalLabel);
            PlaceUguiTopLeft(handle.IntervalLabel, 12f, 91f, colW, 18f);
            handle.IntervalSlider = this.CreateUguiSlider(setCard.transform, "IntervalSlider",
                1f, 120f, this.autoSellInterval, false,
                new System.Action<float>(this.OnUguiFeaturesAutoSellIntervalChanged));
            PlaceUguiTopLeft(handle.IntervalSlider.gameObject, 12f, 109f, colW, 20f);

            handle.FestivalToggle = this.CreateUguiCheckbox(setCard.transform, "FestivalToggle",
                this.L("Festival For Tokens"), this.autoSellFestivalTokensEnabled,
                new System.Action<bool>(this.OnUguiFeaturesAutoSellFestivalToggled));
            PlaceUguiTopLeft(handle.FestivalToggle.gameObject, 12f, 134f, colW, 24f);

            handle.StarInfoShown = this.BuildUguiFeaturesAutoSellStarInfoText();
            handle.StarInfoLabel = this.CreateUguiLabel(setCard.transform, "StarInfoLabel",
                handle.StarInfoShown, 11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.StarInfoLabel);
            PlaceUguiTopLeft(handle.StarInfoLabel, 12f, 162f, colW, 32f);

            handle.FullStackToggle = this.CreateUguiCheckbox(setCard.transform, "FullStackToggle",
                this.L("Sell Whole Stack"), this.autoSellFullStack,
                new System.Action<bool>(this.OnUguiFeaturesAutoSellFullStackToggled));
            PlaceUguiTopLeft(handle.FullStackToggle.gameObject, colRightX, 12f, colRightW, 24f);
            handle.EveryMatchToggle = this.CreateUguiCheckbox(setCard.transform, "EveryMatchToggle",
                this.L("Sell Every Match"), this.autoSellAllMatchingStacks,
                new System.Action<bool>(this.OnUguiFeaturesAutoSellEveryMatchToggled));
            PlaceUguiTopLeft(handle.EveryMatchToggle.gameObject, colRightX, 42f, colRightW, 24f);
            handle.FamilyToggle = this.CreateUguiCheckbox(setCard.transform, "FamilyToggle",
                this.L("Match Similar Items"), this.autoSellMatchFamily,
                new System.Action<bool>(this.OnUguiFeaturesAutoSellFamilyToggled));
            PlaceUguiTopLeft(handle.FamilyToggle.gameObject, colRightX, 72f, colRightW, 24f);
            handle.HideListToggle = this.CreateUguiCheckbox(setCard.transform, "HideListToggle",
                this.L("Hide Item List"), this.autoSellHideBagItems,
                new System.Action<bool>(this.OnUguiFeaturesAutoSellHideListToggled));
            PlaceUguiTopLeft(handle.HideListToggle.gameObject, colRightX, 102f, colRightW, 24f);

            handle.SellModeHelpShown = this.BuildUguiFeaturesAutoSellModeHelpText();
            handle.SellModeHelpLabel = this.CreateUguiLabel(setCard.transform, "SellModeHelp",
                handle.SellModeHelpShown, 11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.SellModeHelpLabel);
            PlaceUguiTopLeft(handle.SellModeHelpLabel, colRightX, 126f, colRightW, 28f);

            handle.CapShown = this.autoSellFullStack ? "Cap: ignored" : this.LF("Cap: {0}", this.autoSellMaxPerStack);
            handle.CapLabel = this.CreateUguiLabel(setCard.transform, "CapLabel",
                handle.CapShown, 11f, mutedColor, false);
            PlaceUguiTopLeft(handle.CapLabel, colRightX, 158f, 100f, 18f);
            handle.CapSlider = this.CreateUguiSlider(setCard.transform, "CapSlider",
                0f, 200f, this.autoSellMaxPerStack, true,
                new System.Action<float>(this.OnUguiFeaturesAutoSellCapChanged));
            PlaceUguiTopLeft(handle.CapSlider.gameObject, colRightX + 104f, 158f, colRightW - 104f, 20f);

            handle.KeepShown = this.LF("Keep Per Item: {0}", this.autoSellReserveCount);
            handle.KeepLabel = this.CreateUguiLabel(setCard.transform, "KeepLabel",
                handle.KeepShown, 11f, mutedColor, false);
            PlaceUguiTopLeft(handle.KeepLabel, colRightX, 182f, 100f, 18f);
            handle.KeepSlider = this.CreateUguiSlider(setCard.transform, "KeepSlider",
                0f, 200f, this.autoSellReserveCount, true,
                new System.Action<float>(this.OnUguiFeaturesAutoSellKeepChanged));
            PlaceUguiTopLeft(handle.KeepSlider.gameObject, colRightX + 104f, 182f, colRightW - 104f, 20f);

            // -------- Source row (:205-237 — dropdown 120x28, buttons from +12, 3px above) ------
            handle.SourceLastValue = Mathf.Clamp(this.autoSellScanSource, 0,
                this.autoSellScanSourceLabels.Length - 1);
            bool sourceWired;
            handle.SourceDropdown = this.CreateUguiDropdown(scrollContent, "SourceDropdown",
                this.autoSellScanSourceLabels, handle.SourceLastValue,
                new System.Action<int>(this.OnUguiFeaturesAutoSellSourcePicked), out sourceWired);
            handle.SourceListenerWired = sourceWired;
            PlaceUguiTopLeft(handle.SourceDropdown.gameObject, 8f, 342f, 120f, 28f);

            float buttonsX = 8f + 120f + 12f;
            float buttonAvail = panelW - (buttonsX - 8f);
            float buttonW = Mathf.Max(110f, (buttonAvail - 10f) * 0.5f);          // :222-223
            handle.SellSelectedButton = this.CreateUguiPrimaryButton(scrollContent, "SellSelectedButton",
                this.L("Sell Selected"), new System.Action(this.OnUguiFeaturesAutoSellSellSelectedClicked));
            PlaceUguiTopLeft(handle.SellSelectedButton, buttonsX, 339f, buttonW, 34f);
            handle.ScanItemsButton = this.CreateUguiSecondaryButton(scrollContent, "ScanItemsButton",
                this.L("Scan Items"), new System.Action(this.OnUguiFeaturesAutoSellScanClicked));
            PlaceUguiTopLeft(handle.ScanItemsButton, buttonsX + buttonW + 10f, 339f, buttonW, 34f);

            // -------- Status card (:245-250 — 52 tall, two live lines) --------
            GameObject statusCard = this.CreateUguiGo("StatusCard", scrollContent);
            PlaceUguiTopLeft(statusCard, 8f, 382f, panelW, 52f);
            this.AddUguiImage(statusCard, this.UguiKitPanelBg(), true, 1f);
            this.AddUguiRingOverlay(statusCard, handle.RingNormal, 1f);
            GameObject statusCaption = this.CreateUguiLabel(statusCard.transform, "Caption",
                "Status", 12f, textColor, false);                                 // raw in source (:248)
            this.TrySetUguiLabelBold(statusCaption);
            PlaceUguiTopLeft(statusCaption, 12f, 7f, 62f, 18f);
            handle.StatusShown = this.autoSellStatus ?? "Idle";
            handle.StatusValue = this.CreateUguiLabel(statusCard.transform, "Value",
                handle.StatusShown, 11f, mutedColor, false);
            PlaceUguiTopLeft(handle.StatusValue, 78f, 7f, panelW - 96f, 18f);
            handle.MatchSummaryShown = this.autoSellLastMatchSummary ?? "No scan yet";
            handle.MatchSummaryLabel = this.CreateUguiLabel(statusCard.transform, "Summary",
                handle.MatchSummaryShown, 11f, mutedColor, false);
            PlaceUguiTopLeft(handle.MatchSummaryLabel, 12f, 27f, panelW - 24f, 18f);

            // -------- Grid region (:253-347 — positions owned by the relayout) --------
            handle.GridHeaderLabel = this.CreateUguiLabel(scrollContent, "GridHeader", "", 12f, textColor, false);
            this.TrySetUguiLabelBold(handle.GridHeaderLabel);                     // fieldLabelStyle (:260)
            handle.GridHeaderLabel.SetActive(false);

            // Pool sized to the source's own 276px viewport cap — the largest this layout allows.
            handle.Grid = this.CreateUguiVirtualGrid(scrollContent, "ItemGrid", panelW,
                UguiAutoSellColumns, UguiAutoSellCellH, UguiAutoSellListMaxH);
            handle.Grid.Root.SetActive(false);
            for (int k = 0; k < handle.Grid.Slots.Count; k++)
            {
                handle.Cells.Add(this.BuildUguiFeaturesAutoSellCell(handle, handle.Grid.Slots[k], k));
            }

            handle.ListHintLabel = this.CreateUguiLabel(scrollContent, "ListHint", "", 13f, textColor, false);

            // -------- Trailing block (:349-374 — positions owned by the relayout) --------
            handle.ClearButton = this.CreateUguiDangerButton(scrollContent, "ClearButton",
                this.L("Clear"), new System.Action(this.OnUguiFeaturesAutoSellClearClicked));
            handle.OpenSellPanelButton = this.CreateUguiSecondaryButton(scrollContent, "OpenSellPanel",
                this.L("Open Sell Panel"), new System.Action(this.OnUguiFeaturesAutoSellOpenSellPanelClicked));
            handle.OpenTokenSellButton = this.CreateUguiSecondaryButton(scrollContent, "OpenTokenSell",
                this.L("Open Token Sell"), new System.Action(this.OnUguiFeaturesAutoSellOpenTokenSellClicked));
            handle.SellPanelsStatusLabel = this.CreateUguiLabel(scrollContent, "SellPanelsStatus",
                "", 11f, mutedColor, false);

            // Populate immediately (no empty first frame).
            handle.LayoutSignature = this.ComputeUguiFeaturesAutoSellLayoutSignature();
            this.RelayoutUguiShellFeaturesAutoSell(handle);
            this.SyncUguiFeaturesAutoSellTexts(handle);
            this.SyncUguiFeaturesAutoSellGrid(handle);

            handle.Root = block;
            this.uguiShellFeaturesAutoSell = handle;
            return block;
        }

        // One pooled cell's visuals — built ONCE per slot, then only rebound. Positions are
        // cell-local mirrors of the IMGUI cell rects (:302-333): icon 42x42 centered at y6,
        // count badge top-right, star badge top-left, name strip at y51.
        private UguiAutoSellCellWidgets BuildUguiFeaturesAutoSellCell(UguiShellFeaturesAutoSellHandle handle,
            UguiVirtualGridSlot slot, int slotIndex)
        {
            UguiAutoSellCellWidgets w = new UguiAutoSellCellWidgets();
            GameObject root = slot.Root;
            float cellW = handle.Grid.CellW - 8f;   // inner cell (IMGUI: cellW-8 x cellH-8)

            w.Bg = this.AddUguiImage(root, handle.CellNormalFill, true, 1.5f);
            w.Bg.raycastTarget = true;
            w.CellButton = root.AddComponent<Button>();
            w.CellButton.targetGraphic = w.Bg;
            // No ColorTint transition: the bind pass owns cell colors exactly (IMGUI's
            // GUIStyle.none button has no hover/press visuals either).
            w.CellButton.transition = Selectable.Transition.None;
            int slotCopy = slotIndex; // capture a copy for the click closure
            w.CellButton.onClick.AddListener(new System.Action(() => this.OnUguiFeaturesAutoSellCellClicked(slotCopy)));

            this.AddUguiRingOverlay(root, handle.RingNormal, 1.5f);
            Transform ringT = root.transform.Find("Ring");
            w.Ring = (ringT != null) ? ringT.GetComponent<Image>() : null;

            GameObject iconGo = this.CreateUguiGo("Icon", root.transform);
            PlaceUguiTopLeft(iconGo, (cellW - 42f) * 0.5f, 6f, 42f, 42f);
            w.Icon = iconGo.AddComponent<Image>();
            w.Icon.raycastTarget = false;
            try { w.Icon.preserveAspect = true; } catch { }
            iconGo.SetActive(false);

            w.InitialsLabel = this.CreateUguiLabel(root.transform, "Initials", "", 14f, handle.TextColor, true);
            this.TrySetUguiLabelBold(w.InitialsLabel);                             // initialsStyle (:284)
            PlaceUguiTopLeft(w.InitialsLabel, (cellW - 42f) * 0.5f, 6f, 42f, 42f);
            w.InitialsLabel.SetActive(false);

            w.CountBadge = this.CreateUguiLabel(root.transform, "Count", "", 10f, handle.MutedColor, false);
            this.TrySetUguiLabelRightAligned(w.CountBadge);                        // countBadgeStyle UpperRight
            PlaceUguiTopLeft(w.CountBadge, cellW - 34f, 4f, 30f, 16f);
            w.CountBadge.SetActive(false);

            w.StarBadge = this.CreateUguiLabel(root.transform, "Star", "", 10f, handle.MutedColor, false);
            PlaceUguiTopLeft(w.StarBadge, 4f, 4f, 34f, 16f);                       // starBadgeStyle UpperLeft
            w.StarBadge.SetActive(false);

            w.NameLabel = this.CreateUguiLabel(root.transform, "Name", "", 10f, handle.TextColor, true);
            this.TrySetUguiTransferNameWrapped(w.NameLabel);                       // itemStyle UpperCenter+wrap
            PlaceUguiTopLeft(w.NameLabel, 4f, 51f, cellW - 8f, 30f);

            return w;
        }

        // ----------------------------------------------------------------------------------------
        // Layout (the dynamic region below the status card — grid states + trailing block)
        // ----------------------------------------------------------------------------------------

        // Reposition/SetActive only — nothing is rebuilt (the Settings→Main relayout idiom, the
        // UGUI analog of the source's y-cursor: grid region → Clear +44 → open buttons +36 →
        // status line +30). Also owns the Cap slider's fixed-position visibility (:193-198).
        private void RelayoutUguiShellFeaturesAutoSell(UguiShellFeaturesAutoSellHandle handle)
        {
            float panelW = handle.PanelW;
            List<AutoSellBagItemEntry> items = this.autoSellBagItems;
            bool scanned = items != null;
            int count = scanned ? items.Count : 0;
            bool showGrid = !this.autoSellHideBagItems && count > 0;
            float yCur = UguiAutoSellDynamicTopY;

            SetUguiGoActive(handle.GridHeaderLabel, showGrid);
            SetUguiGoActive(handle.Grid != null ? handle.Grid.Root : null, showGrid);
            SetUguiGoActive(handle.ListHintLabel, !showGrid);
            if (showGrid)
            {
                PlaceUguiTopLeft(handle.GridHeaderLabel, 8f, yCur, panelW, 22f);
                yCur += 24f;                                                       // :261
                int rows = Mathf.CeilToInt(count / (float)UguiAutoSellColumns);
                float listHeight = Mathf.Min(rows * UguiAutoSellCellH, UguiAutoSellListMaxH); // :267
                PlaceUguiTopLeft(handle.Grid.Root, 8f, yCur, panelW, listHeight);
                yCur += listHeight + 12f;                                          // :336
            }
            else
            {
                // Hidden hint (:255, +30) and both empty states (:340/:345, +30) share the line.
                PlaceUguiTopLeft(handle.ListHintLabel, 8f, yCur, panelW, 24f);
                yCur += 30f;
            }

            // Cap slider — fixed position inside the settings card, visibility only (:193-198).
            SetUguiGoActive(handle.CapSlider != null ? handle.CapSlider.gameObject : null,
                !this.autoSellFullStack);

            PlaceUguiTopLeft(handle.ClearButton, 8f, yCur, 120f, 32f);
            yCur += 44f;                                                           // :358
            PlaceUguiTopLeft(handle.OpenSellPanelButton, 8f, yCur, 195f, 32f);
            PlaceUguiTopLeft(handle.OpenTokenSellButton, 8f + 205f, yCur, 195f, 32f);
            yCur += 36f;                                                           // :371
            PlaceUguiTopLeft(handle.SellPanelsStatusLabel, 8f, yCur, panelW - 20f, 22f);
            yCur += 30f;                                                           // :375

            this.SetUguiScrollContentHeight(handle.ScrollContent, yCur + 8f);
        }

        // ----------------------------------------------------------------------------------------
        // Display builders (raw source strings)
        // ----------------------------------------------------------------------------------------

        private string BuildUguiFeaturesAutoSellStarInfoText()
        {
            // :166-168 — no global star slider; the constraint travels with the clicked cell.
            return this.autoSellSelectedStar > 0
                ? "Stars: " + this.autoSellSelectedStar + "* only (from selected item)"
                : "Stars: any (star comes from the clicked item)";
        }

        private string BuildUguiFeaturesAutoSellModeHelpText()
        {
            // :188-190.
            return this.autoSellMatchFamily
                ? "Similar: sells same item family, like all birdphotos."
                : "Exact: sells only this selected item.";
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellFeaturesAutoSellOnUpdate()
        {
            UguiShellFeaturesAutoSellHandle handle = this.uguiShellFeaturesAutoSell;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellFeaturesSubTabActive(UguiShellFeaturesAutoSellSubIndex))
            {
                return;
            }

            try
            {
                // Dropdown poll fallback — only when UnityEvent<int> wiring reported failure
                // (Birds precedent). BEFORE the external re-sync so a user pick lands first.
                if (!handle.SourceListenerWired && handle.SourceDropdown != null)
                {
                    int v = handle.SourceDropdown.value;
                    if (v != handle.SourceLastValue)
                    {
                        this.OnUguiFeaturesAutoSellSourcePicked(v); // updates SourceLastValue itself
                    }
                }

                // Dropdown external re-sync (the IMGUI twin moved autoSellScanSource) —
                // WithoutNotify + LastValue update (Birds shape).
                if (handle.SourceDropdown != null)
                {
                    int want = Mathf.Clamp(this.autoSellScanSource, 0,
                        this.autoSellScanSourceLabels.Length - 1);
                    if (handle.SourceDropdown.value != want)
                    {
                        handle.SourceDropdown.SetValueWithoutNotify(want);
                        handle.SourceLastValue = want;
                    }
                }

                // Row-action blocking — the IMGUI GUI.enabled = !dropdownOpen mirror (:224-237),
                // re-applied on every expanded-state change (probe: the stock popup's
                // "Dropdown List" child — the Food & Repair stripping-proof idiom).
                bool sourceOpen = IsUguiFoodRepairDropdownExpanded(handle.SourceDropdown);
                if (sourceOpen != handle.RowActionsBlocked)
                {
                    handle.RowActionsBlocked = sourceOpen;
                    this.SetUguiButtonInteractable(handle.SellSelectedButton, !sourceOpen);
                    this.SetUguiButtonInteractable(handle.ScanItemsButton, !sourceOpen);
                }

                // Toggle re-syncs (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.AutoToggle, this.autoSellEnabled);
                this.SyncUguiToggleFromField(handle.FestivalToggle, this.autoSellFestivalTokensEnabled);
                this.SyncUguiToggleFromField(handle.FullStackToggle, this.autoSellFullStack);
                this.SyncUguiToggleFromField(handle.EveryMatchToggle, this.autoSellAllMatchingStacks);
                this.SyncUguiToggleFromField(handle.FamilyToggle, this.autoSellMatchFamily);
                this.SyncUguiToggleFromField(handle.HideListToggle, this.autoSellHideBagItems);

                // Slider re-syncs — epsilon compare for the float interval (:155 keeps fractional
                // values), RoundToInt for the int contracts (Settings→Main FpsSlider shape).
                if (handle.IntervalSlider != null
                    && Mathf.Abs(handle.IntervalSlider.value - this.autoSellInterval) > 0.001f)
                {
                    handle.IntervalSlider.SetValueWithoutNotify(this.autoSellInterval);
                }
                if (handle.CapSlider != null
                    && Mathf.RoundToInt(handle.CapSlider.value) != this.autoSellMaxPerStack)
                {
                    handle.CapSlider.SetValueWithoutNotify(this.autoSellMaxPerStack);
                }
                if (handle.KeepSlider != null
                    && Mathf.RoundToInt(handle.KeepSlider.value) != this.autoSellReserveCount)
                {
                    handle.KeepSlider.SetValueWithoutNotify(this.autoSellReserveCount);
                }

                // Fast labels — IMGUI drew all five per frame; drag/toggle feedback must not lag
                // the 0.25s tick (Food & Repair slider-label precedent).
                this.SyncUguiSelfLabelText(handle.IntervalLabel, ref handle.IntervalShown,
                    this.LF("Interval: {0:F0}s", this.autoSellInterval));
                this.SyncUguiSelfLabelText(handle.CapLabel, ref handle.CapShown,
                    this.autoSellFullStack ? "Cap: ignored" : this.LF("Cap: {0}", this.autoSellMaxPerStack));
                this.SyncUguiSelfLabelText(handle.KeepLabel, ref handle.KeepShown,
                    this.LF("Keep Per Item: {0}", this.autoSellReserveCount));
                this.SyncUguiSelfLabelText(handle.StarInfoLabel, ref handle.StarInfoShown,
                    this.BuildUguiFeaturesAutoSellStarInfoText());
                this.SyncUguiSelfLabelText(handle.SellModeHelpLabel, ref handle.SellModeHelpShown,
                    this.BuildUguiFeaturesAutoSellModeHelpText());

                // Conditional layout (grid states + Cap visibility). The backend auto-tick and
                // PetFeed both rewrite autoSellBagItems off-surface, so flips arrive any frame.
                int signature = this.ComputeUguiFeaturesAutoSellLayoutSignature();
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellFeaturesAutoSell(handle);
                    this.SyncUguiFeaturesAutoSellTexts(handle); // a state flip must not show stale text
                }

                // The virtualized grid: scroll poll + slot assignment + per-slot signature rebinds.
                this.SyncUguiFeaturesAutoSellGrid(handle);

                if (Time.unscaledTime >= handle.NextTextSyncAt)
                {
                    handle.NextTextSyncAt = Time.unscaledTime + 0.25f;
                    this.SyncUguiFeaturesAutoSellTexts(handle);
                }
                if (Time.unscaledTime >= handle.NextIconRetryAt)
                {
                    handle.NextIconRetryAt = Time.unscaledTime + 0.5f;
                    this.RetryUguiFeaturesAutoSellIcons(handle);
                }
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    SyncUguiInputFieldFromBackingField(handle.MatchKeyField,
                        ref handle.MatchKeySeen, this.autoSellItemKey ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Features Auto Sell content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Text refresh (0.25s tick + after actions/relayout — every write change-guarded)
        // ----------------------------------------------------------------------------------------

        private void SyncUguiFeaturesAutoSellTexts(UguiShellFeaturesAutoSellHandle handle)
        {
            // Selected-item card (:112-120) — title, key line, meta, all verbatim conditionals.
            AutoSellBagItemEntry selectedEntry = this.GetSelectedAutoSellBagItemEntry();
            string selectedTitle = selectedEntry != null ? selectedEntry.DisplayName : "No item selected";
            string selectedKey = this.GetActiveAutoSellMatchKey();
            if (string.IsNullOrWhiteSpace(selectedKey))
            {
                selectedKey = "Choose from scanned items or type a key below";
            }
            string keyLine = (this.autoSellMatchFamily ? "Similar: " : "Exact: ") + selectedKey
                + (this.autoSellSelectedStar > 0 ? ("  " + this.autoSellSelectedStar + "*") : "");
            string selectedMeta = selectedEntry != null && selectedEntry.Count > 0
                ? ("Source: " + this.GetAutoSellStorageLabel(selectedEntry.FromBackpack, selectedEntry.FromWarehouse)
                    + "  Count: " + selectedEntry.Count
                    + (selectedEntry.StackCount > 1 ? (" in " + selectedEntry.StackCount + " stacks") : "")
                    + this.GetAutoSellStarSummary(selectedEntry)
                    + (selectedEntry.StaticId > 0 ? ("  staticId: " + selectedEntry.StaticId) : ""))
                : (this.autoSellLastMatchSummary ?? "No scan yet");
            this.SyncUguiSelfLabelText(handle.SelectedTitle, ref handle.ShownSelTitle, selectedTitle);
            this.SyncUguiSelfLabelText(handle.SelectedKeyLine, ref handle.ShownSelKey, keyLine);
            this.SyncUguiSelfLabelText(handle.SelectedMeta, ref handle.ShownSelMeta, selectedMeta);

            // Selected icon (:103-110) — texture or the "?" fallback (Bag/Warehouse shape; the
            // sprite wrapper cache is shared with that tab, both feed off the same icon caches).
            Texture2D selTex = null;
            if (selectedEntry != null)
            {
                this.TryGetAutoSellItemTexture(selectedEntry, out selTex);
            }
            Sprite selSprite = (selTex != null) ? this.GetOrCreateUguiTransferSprite(selTex) : null;
            bool showSelIcon = selSprite != null;
            int selTexId = showSelIcon ? selTex.GetInstanceID() : 0;
            if (showSelIcon != handle.ShownSelHasIcon || selTexId != handle.ShownSelIconTexId)
            {
                handle.ShownSelHasIcon = showSelIcon;
                handle.ShownSelIconTexId = selTexId;
                if (handle.SelectedIcon != null)
                {
                    if (showSelIcon)
                    {
                        handle.SelectedIcon.sprite = selSprite;
                    }
                    SetUguiGoActive(handle.SelectedIcon.gameObject, showSelIcon);
                }
                SetUguiGoActive(handle.SelectedIconFallback, !showSelIcon);
            }

            // Status card (:249-250) — live (the backend tick rewrites both off-surface).
            this.SyncUguiSelfLabelText(handle.StatusValue, ref handle.StatusShown,
                this.autoSellStatus ?? "Idle");
            this.SyncUguiSelfLabelText(handle.MatchSummaryLabel, ref handle.MatchSummaryShown,
                this.autoSellLastMatchSummary ?? "No scan yet");

            // Grid header vs the three hint states — strings verbatim, all carry the LIVE
            // scan-source label (:255/:260/:340/:345).
            List<AutoSellBagItemEntry> items = this.autoSellBagItems;
            bool scanned = items != null;
            int count = scanned ? items.Count : 0;
            if (!this.autoSellHideBagItems && count > 0)
            {
                this.SyncUguiSelfLabelText(handle.GridHeaderLabel, ref handle.GridHeaderShown,
                    this.LF("{0} Items ({1})", this.GetAutoSellScanSourceLabel(), count));
            }
            else
            {
                string hint = this.autoSellHideBagItems
                    ? (this.GetAutoSellScanSourceLabel()
                        + " item list is hidden. Scan Items still updates selected item data.")
                    : (scanned
                        ? ("No " + this.GetAutoSellScanSourceLabel().ToLowerInvariant()
                            + " items found yet. Try again after the world finishes loading.")
                        : ("Press Scan Items to read " + this.GetAutoSellScanSourceLabel().ToLowerInvariant()
                            + " data. Icons load automatically from the game."));
                this.SyncUguiSelfLabelText(handle.ListHintLabel, ref handle.ListHintShown, hint);
            }

            // Combined open-panel status line (:372-374).
            this.SyncUguiSelfLabelText(handle.SellPanelsStatusLabel, ref handle.SellPanelsStatusShown,
                "Coins: " + (this.shopQuickSellStatus ?? "Idle.")
                + "     Tokens: " + (this.tokenSellPanelStatus ?? "Idle."));
        }

        // ----------------------------------------------------------------------------------------
        // Grid sync (assignment + per-slot signature rebinds — the Bag/Warehouse shape)
        // ----------------------------------------------------------------------------------------

        private void SyncUguiFeaturesAutoSellGrid(UguiShellFeaturesAutoSellHandle handle)
        {
            UguiVirtualGridHandle grid = handle.Grid;
            if (grid == null || grid.Root == null || !grid.Root.activeSelf)
            {
                return;
            }

            int count = (this.autoSellBagItems != null) ? this.autoSellBagItems.Count : 0;
            this.UpdateUguiVirtualGridAssignments(grid, count);

            // Computed ONCE per sync like the IMGUI pass (:276); the per-cell family key below
            // matches the source's per-visible-cell work (:292).
            string activeAutoSellKey = this.GetActiveAutoSellMatchKey();

            List<UguiAutoSellCellWidgets> cells = handle.Cells;
            for (int k = 0; k < grid.Slots.Count && k < cells.Count; k++)
            {
                UguiVirtualGridSlot slot = grid.Slots[k];
                UguiAutoSellCellWidgets w = cells[k];
                int index = slot.BoundIndex;
                if (index < 0)
                {
                    w.SigIndex = int.MinValue;
                    continue;
                }
                AutoSellBagItemEntry entry = (this.autoSellBagItems != null && index < this.autoSellBagItems.Count)
                    ? this.autoSellBagItems[index]
                    : null;
                if (entry == null)
                {
                    // Hide the slot rather than show stale content (Bag/Warehouse null shape).
                    if (slot.Root != null && slot.Root.activeSelf)
                    {
                        slot.Root.SetActive(false);
                    }
                    w.SigIndex = int.MinValue;
                    continue;
                }

                // Highlight must mirror the sell-time match exactly (:292-298): identity
                // (staticId + star) in exact mode, key + star in family/typed mode. Two-branch
                // shape verbatim — do NOT simplify.
                string entrySelectKey = this.autoSellMatchFamily
                    ? this.GetAutoSellFamilyKey(entry.MatchKey)
                    : entry.MatchKey;
                bool isSelected = !this.autoSellMatchFamily && this.autoSellSelectedStaticId > 0
                    ? (entry.StaticId == this.autoSellSelectedStaticId
                        && Math.Max(0, entry.StarRate) == this.autoSellSelectedStar)
                    : (string.Equals(activeAutoSellKey, entrySelectKey, StringComparison.OrdinalIgnoreCase)
                        && (this.autoSellSelectedStar <= 0 || entry.StarRate == this.autoSellSelectedStar));

                if (w.SigIndex == index && w.SigNetId == entry.NetId && w.SigCount == entry.Count
                    && w.SigStar == entry.StarRate && w.SigSelected == isSelected
                    && string.Equals(w.SigName, entry.DisplayName, StringComparison.Ordinal))
                {
                    continue; // unchanged — no rebind, which is the whole point of the pool
                }

                this.BindUguiFeaturesAutoSellCell(handle, w, entry, index, isSelected);
            }
        }

        // Writes one pooled cell's full visual state from an entry — the retained-mode equivalent
        // of the IMGUI per-cell block (:299-333). Reposition/activation is the grid core's job;
        // this only touches content.
        private void BindUguiFeaturesAutoSellCell(UguiShellFeaturesAutoSellHandle handle,
            UguiAutoSellCellWidgets w, AutoSellBagItemEntry entry, int index, bool isSelected)
        {
            if (w.Bg != null)
            {
                w.Bg.color = isSelected ? handle.CellActiveFill : handle.CellNormalFill;
            }
            if (w.Ring != null)
            {
                w.Ring.color = isSelected ? handle.RingSelected : handle.RingNormal;
            }

            Texture2D tex;
            this.TryGetAutoSellItemTexture(entry, out tex);
            Sprite sprite = (tex != null) ? this.GetOrCreateUguiTransferSprite(tex) : null;
            bool hasIcon = sprite != null;
            w.HasIcon = hasIcon;
            if (w.Icon != null)
            {
                if (hasIcon)
                {
                    w.Icon.sprite = sprite;
                }
                SetUguiGoActive(w.Icon.gameObject, hasIcon);
            }
            SetUguiGoActive(w.InitialsLabel, !hasIcon);
            if (!hasIcon)
            {
                this.SetUguiLabelText(w.InitialsLabel, this.GetAutoSellItemInitials(entry.DisplayName));
            }

            bool showCount = entry.Count > 0;                                      // :312
            SetUguiGoActive(w.CountBadge, showCount);
            if (showCount)
            {
                this.SetUguiLabelText(w.CountBadge, "x" + entry.Count);
            }

            bool showStar = entry.StarRate > 0;                                    // :316
            SetUguiGoActive(w.StarBadge, showStar);
            if (showStar)
            {
                this.SetUguiLabelText(w.StarBadge, entry.StarRate + "*");
            }

            this.SetUguiLabelText(w.NameLabel, entry.DisplayName);

            w.SigIndex = index;
            w.SigNetId = entry.NetId;
            w.SigCount = entry.Count;
            w.SigStar = entry.StarRate;
            w.SigSelected = isSelected;
            w.SigName = entry.DisplayName;
        }

        // 0.5s tick: cells showing initials re-try the texture lookup (the game loads icons
        // async; IMGUI gets the upgrade for free by redrawing every frame). A hit just
        // invalidates that slot's signature — the next grid sync rebinds it with the icon.
        // TryGetAutoSellItemTexture also re-fires the request-once async load on a miss.
        private void RetryUguiFeaturesAutoSellIcons(UguiShellFeaturesAutoSellHandle handle)
        {
            UguiVirtualGridHandle grid = handle.Grid;
            if (grid == null || grid.Root == null || !grid.Root.activeSelf || this.autoSellBagItems == null)
            {
                return;
            }
            List<UguiAutoSellCellWidgets> cells = handle.Cells;
            for (int k = 0; k < grid.Slots.Count && k < cells.Count; k++)
            {
                UguiAutoSellCellWidgets w = cells[k];
                int index = grid.Slots[k].BoundIndex;
                if (index < 0 || w.HasIcon || w.SigIndex == int.MinValue || index >= this.autoSellBagItems.Count)
                {
                    continue;
                }
                AutoSellBagItemEntry entry = this.autoSellBagItems[index];
                if (entry == null)
                {
                    continue;
                }
                Texture2D tex;
                if (this.TryGetAutoSellItemTexture(entry, out tex) && tex != null)
                {
                    w.SigIndex = int.MinValue;
                }
            }
        }

        // Same-frame refresh after a user action (the per-frame processor would catch up a frame
        // later anyway — this just keeps clicks snappy). Everything inside is change-guarded.
        private void RefreshUguiFeaturesAutoSellAfterAction(UguiShellFeaturesAutoSellHandle handle)
        {
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                int signature = this.ComputeUguiFeaturesAutoSellLayoutSignature();
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellFeaturesAutoSell(handle);
                }
                this.SyncUguiFeaturesAutoSellTexts(handle);
                this.SyncUguiFeaturesAutoSellGrid(handle);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Features Auto Sell refresh error: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order).
        // ----------------------------------------------------------------------------------------

        // AutoSell.cs:122-134 — nextAutoSellAt reset; ENABLE also forces a scan on the next tick
        // (autoSellBackpackDirty); status; save; green/red toast — order verbatim.
        private void OnUguiFeaturesAutoSellAutoToggled(bool value)
        {
            if (value == this.autoSellEnabled)
            {
                return;
            }
            this.autoSellEnabled = value;
            this.nextAutoSellAt = 0f;
            if (value)
            {
                this.autoSellBackpackDirty = true; // force a scan on the first tick after enabling
            }
            this.autoSellStatus = value ? "Enabled" : "Disabled";
            try { this.SaveKeybinds(false); } catch { }
            this.AddMenuNotification("Auto Sell " + (value ? "Enabled" : "Disabled"),
                value ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
            this.RefreshUguiFeaturesAutoSellAfterAction(this.uguiShellFeaturesAutoSell);
        }

        // AutoSell.cs:142-150 — a hand-typed key means PLAIN TEXT matching: the write also resets
        // the selection identity (staticId 0) AND the star constraint (star 0), then saves. The
        // newKey != autoSellItemKey guard is the source's own.
        private void OnUguiFeaturesAutoSellMatchKeyEdited(string text)
        {
            string raw = text ?? string.Empty;
            UguiShellFeaturesAutoSellHandle handle = this.uguiShellFeaturesAutoSell;
            if (handle != null)
            {
                handle.MatchKeySeen = raw;
            }
            if (string.Equals(raw, this.autoSellItemKey, StringComparison.Ordinal))
            {
                return;
            }
            this.autoSellItemKey = raw;
            // Hand-typed key = plain text matching: no item identity, no star constraint.
            this.autoSellSelectedStaticId = 0;
            this.autoSellSelectedStar = 0;
            try { this.SaveKeybinds(false); } catch { }
        }

        // AutoSell.cs:153-156 — assignment always (double clamp), save only on a real change.
        private void OnUguiFeaturesAutoSellIntervalChanged(float value)
        {
            float prev = this.autoSellInterval;
            this.autoSellInterval = Mathf.Clamp(value, 1f, 120f);
            if (Math.Abs(this.autoSellInterval - prev) > 0.001f)
            {
                try { this.SaveKeybinds(false); } catch { }
            }
        }

        // AutoSell.cs:157-163 — a change also zeroes the festival-currency probe timer so the
        // currency is re-probed right away.
        private void OnUguiFeaturesAutoSellFestivalToggled(bool value)
        {
            if (value == this.autoSellFestivalTokensEnabled)
            {
                return;
            }
            this.autoSellFestivalTokensEnabled = value;
            this.autoSellFestivalCurrencyNextProbeAt = 0f; // re-probe the currency right away
            try { this.SaveKeybinds(false); } catch { }
        }

        // AutoSell.cs:172-186 — the four right-column toggles are flag + save only. Full Stack
        // additionally drives the Cap slider's visibility through the layout signature.
        private void OnUguiFeaturesAutoSellFullStackToggled(bool value)
        {
            if (value == this.autoSellFullStack)
            {
                return;
            }
            this.autoSellFullStack = value;
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiFeaturesAutoSellAfterAction(this.uguiShellFeaturesAutoSell);
        }

        private void OnUguiFeaturesAutoSellEveryMatchToggled(bool value)
        {
            if (value == this.autoSellAllMatchingStacks)
            {
                return;
            }
            this.autoSellAllMatchingStacks = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiFeaturesAutoSellFamilyToggled(bool value)
        {
            if (value == this.autoSellMatchFamily)
            {
                return;
            }
            this.autoSellMatchFamily = value;
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiFeaturesAutoSellAfterAction(this.uguiShellFeaturesAutoSell);
        }

        private void OnUguiFeaturesAutoSellHideListToggled(bool value)
        {
            if (value == this.autoSellHideBagItems)
            {
                return;
            }
            this.autoSellHideBagItems = value;
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiFeaturesAutoSellAfterAction(this.uguiShellFeaturesAutoSell);
        }

        // AutoSell.cs:192-198 / :199-202 — int sliders, clamp + save-on-change.
        private void OnUguiFeaturesAutoSellCapChanged(float value)
        {
            int prev = this.autoSellMaxPerStack;
            this.autoSellMaxPerStack = Mathf.Clamp(Mathf.RoundToInt(value), 0, 200);
            if (this.autoSellMaxPerStack != prev)
            {
                try { this.SaveKeybinds(false); } catch { }
            }
        }

        private void OnUguiFeaturesAutoSellKeepChanged(float value)
        {
            int prev = this.autoSellReserveCount;
            this.autoSellReserveCount = Mathf.Clamp(Mathf.RoundToInt(value), 0, 200);
            if (this.autoSellReserveCount != prev)
            {
                try { this.SaveKeybinds(false); } catch { }
            }
        }

        // AutoSell.cs:390-399 — the option-pick block verbatim (minus the IMGUI-only
        // dropdownOpen close): source index, list nulled back to "press scan", the shared
        // scroll-pos reset (+ the UGUI grid's own top-snap analog), status + summary, save.
        // Index guard = the Transfer-round shape (the destructive body must never re-fire for
        // the already-active source).
        private void OnUguiFeaturesAutoSellSourcePicked(int index)
        {
            UguiShellFeaturesAutoSellHandle handle = this.uguiShellFeaturesAutoSell;
            if (handle != null)
            {
                handle.SourceLastValue = index;
            }
            if (index < 0 || index >= this.autoSellScanSourceLabels.Length
                || index == this.autoSellScanSource)
            {
                return;
            }
            this.autoSellScanSource = index;
            this.autoSellBagItems = null;
            this.autoSellBagItemScrollPos = Vector2.zero;
            this.autoSellStatus = "Scan source: " + this.GetAutoSellScanSourceLabel();
            this.autoSellLastMatchSummary = "Press Scan Items to load "
                + this.GetAutoSellScanSourceLabel().ToLowerInvariant() + " items.";
            try { this.SaveKeybinds(false); } catch { }
            if (handle != null && handle.Grid != null && handle.Grid.ContentRt != null)
            {
                try { handle.Grid.ContentRt.anchoredPosition = Vector2.zero; } catch { }
            }
            this.RefreshUguiFeaturesAutoSellAfterAction(handle);
        }

        // AutoSell.cs:226-229 — the click-time expanded re-check mirrors the IMGUI
        // GUI.enabled = !blockRowActions gate (Auto-Buy same-frame-race insurance; the
        // interactable sync is per-frame).
        private void OnUguiFeaturesAutoSellSellSelectedClicked()
        {
            UguiShellFeaturesAutoSellHandle handle = this.uguiShellFeaturesAutoSell;
            if (handle != null && IsUguiFoodRepairDropdownExpanded(handle.SourceDropdown))
            {
                return;
            }
            this.ExecuteDirectAutoSell(false);
            this.RefreshUguiFeaturesAutoSellAfterAction(handle);
        }

        // AutoSell.cs:230-236 — scan + the count-based status write, verbatim
        // (ScanBackpackForAutoSellItems never returns null; the source dereferences .Count).
        private void OnUguiFeaturesAutoSellScanClicked()
        {
            UguiShellFeaturesAutoSellHandle handle = this.uguiShellFeaturesAutoSell;
            if (handle != null && IsUguiFoodRepairDropdownExpanded(handle.SourceDropdown))
            {
                return;
            }
            this.autoSellBagItems = this.ScanBackpackForAutoSellItems();
            this.autoSellStatus = this.autoSellBagItems.Count > 0
                ? (this.GetAutoSellScanSourceLabel() + " list refreshed")
                : ("No " + this.GetAutoSellScanSourceLabel().ToLowerInvariant() + " items found");
            this.RefreshUguiFeaturesAutoSellAfterAction(handle);
        }

        // Cell click — AutoSell.cs:321-331 verbatim: the clicked cell REPLACES the whole
        // selection (key/staticId/star — its own identity always wins, incl. star 0), status +
        // summary + save; then the new key is mirrored into the Match Item field immediately
        // (IMGUI rebinds its TextField from the shared string every frame).
        private void OnUguiFeaturesAutoSellCellClicked(int slotIndex)
        {
            try
            {
                UguiShellFeaturesAutoSellHandle handle = this.uguiShellFeaturesAutoSell;
                if (handle == null || handle.Grid == null || slotIndex < 0
                    || slotIndex >= handle.Grid.Slots.Count)
                {
                    return;
                }
                int index = handle.Grid.Slots[slotIndex].BoundIndex;
                if (this.autoSellBagItems == null || index < 0 || index >= this.autoSellBagItems.Count)
                {
                    return;
                }
                AutoSellBagItemEntry entry = this.autoSellBagItems[index];
                if (entry == null)
                {
                    return;
                }

                this.autoSellItemKey = this.autoSellMatchFamily
                    ? this.GetAutoSellFamilyKey(entry.MatchKey)
                    : entry.MatchKey;
                // The clicked cell IS the selection: its staticId (exact mode) and its star
                // (including "no star") replace any previous constraint entirely.
                this.autoSellSelectedStaticId = this.autoSellMatchFamily ? 0 : Math.Max(0, entry.StaticId);
                this.autoSellSelectedStar = Math.Max(0, entry.StarRate);
                this.autoSellStatus = "Selected: " + entry.DisplayName
                    + (this.autoSellMatchFamily ? " family" : "");
                this.autoSellLastMatchSummary = "Selection changed. Use Sell Selected or wait for Auto Sell.";
                try { this.SaveKeybinds(false); } catch { }

                handle.MatchKeySeen = this.autoSellItemKey;
                if (handle.MatchKeyField != null)
                {
                    try { handle.MatchKeyField.SetTextWithoutNotify(this.autoSellItemKey); } catch { }
                }
                this.RefreshUguiFeaturesAutoSellAfterAction(handle);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Features Auto Sell cell click error: " + ex.Message);
            }
        }

        // AutoSell.cs:349-357 — danger-tier full selection reset, verbatim; the emptied key is
        // mirrored into the Match Item field immediately.
        private void OnUguiFeaturesAutoSellClearClicked()
        {
            this.autoSellItemKey = "";
            this.autoSellSelectedStaticId = 0;
            this.autoSellSelectedStar = 0;
            this.autoSellStatus = "Selection cleared";
            this.autoSellLastMatchSummary = "No scan yet";
            try { this.SaveKeybinds(false); } catch { }
            UguiShellFeaturesAutoSellHandle handle = this.uguiShellFeaturesAutoSell;
            if (handle != null)
            {
                handle.MatchKeySeen = string.Empty;
                if (handle.MatchKeyField != null)
                {
                    try { handle.MatchKeyField.SetTextWithoutNotify(string.Empty); } catch { }
                }
            }
            this.RefreshUguiFeaturesAutoSellAfterAction(handle);
        }

        // AutoSell.cs:363-366 / :367-370 — both backends notify internally, no toasts here.
        private void OnUguiFeaturesAutoSellOpenSellPanelClicked()
        {
            this.StartShopQuickSellOpenPanel();
            this.RefreshUguiFeaturesAutoSellAfterAction(this.uguiShellFeaturesAutoSell);
        }

        private void OnUguiFeaturesAutoSellOpenTokenSellClicked()
        {
            this.StartTokenSellOpenPanel();
            this.RefreshUguiFeaturesAutoSellAfterAction(this.uguiShellFeaturesAutoSell);
        }
    }
}
