using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, Features round 8 of 8 FINAL (migration plan item 11): the
    // PET CARE sub-tab — DrawPetPlayTab (PetPlayFeature.cs:223-627) plus its chained tail
    // DrawPetFeedFavoriteFoodsTable (PetFeedFeature.cs:4503-4571), display sub-index 7 (the tabs
    // list {"Main","Food & Repair","Snow Sculpting","Auto Buy","Auto Sell","Mass Cook","Puzzle",
    // "Pet Care"} maps display indices to automationSubTab 0-7 exactly; dispatcher:
    // Gui.cs:1296-1299 `automationSubTab == 7 → DrawPetPlayTab`).
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawers and every backend method they call stay fully functional and untouched —
    //    ZERO changes to PetPlayFeature.cs / PetFeedFeature.cs; this file only READS the same
    //    fields and CALLS the same methods (all this.-accessible partial-class state). Two
    //    independent rendering paths over one backend.
    //  - Wiring is by STATIC display-position index (UguiShellFeaturesTabIndex = 2 +
    //    UguiShellFeaturesPetCareSubIndex = 7, declared with their siblings in
    //    UguiPhase3Content.cs), never label comparison. The processor gates on the SAME
    //    IsUguiShellFeaturesSubTabActive function the Main round established — no new gate.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //  - The IMGUI panel-height formula (Gui.cs:304-307: 898 + GetPetFeedFavoriteUiTableHeight())
    //    is an estimate — this port computes its scroll height from the relayout cursor like
    //    every prior conditional-content round.
    //  - NO PERSISTENCE ANYWHERE: SaveKeybinds appears ZERO times in PetPlayFeature.cs AND ZERO
    //    times in PetFeedFeature.cs (grep-verified). Every toggle/setting on this tab is a plain
    //    in-memory field write. This file deliberately contains ZERO SaveKeybinds calls and no
    //    bespoke persist method — none exists to call.
    //
    // THE ROUND'S BIG DESIGN DECISION — the hand-rolled dropdown scrollbar is NOT ported:
    //  Source (:451-586) hand-rolls an entire scrollbar subsystem for the food-option list — a
    //  manually-tracked petFeedFoodDropdownScrollIndex + PetFeedFoodVisibleRows-sized window, a
    //  hand-drawn track+thumb, raw Event.current MouseDown/MouseDrag/MouseUp drag math, a wheel
    //  handler, and up/down arrow buttons — all of it existing only because IMGUI has no native
    //  scrolling widget. This port REPLACES the whole mechanism with the migration's standard
    //  CreateUguiScrollView + pooled-row-list shape (Mass Cook's recipe list, Food & Repair's
    //  custom-food list, Extra's rows, Pictures' nested lists — all identical): ONE pooled row
    //  list inside a real nested ScrollRect, bound from the FULL (unclamped)
    //  GetPetFeedFoodDropdownOptions() result every gated frame while open. Drag-scroll and
    //  wheel-scroll come from the ScrollRect for free. Consequences, verified:
    //   - ScrollPetFeedFoodDropdown, SetPetFeedFoodDropdownScrollIndexFromTrack and
    //     ClampPetFeedFoodDropdownScrollIndex are NEVER called from this file, and
    //     petFeedFoodDropdownScrollIndex is NEVER read for rendering.
    //   - The source's search-change cascade (:482-486) writes petFeedFoodDropdownScrollIndex = 0
    //     and petFeedFoodScrollbarDragging = false; this port mirrors those two FIELD writes (so
    //     the IMGUI twin behaves exactly as if the search had been typed there — the Mass Cook
    //     netCookRecipeScrollPos-reset idiom) and snaps its OWN ScrollRect content to the top
    //     (the same "reset scroll on filter change" move Mass Cook's recipe search uses).
    //   - "Any Food" (:497-504) is just row 0 of the same pooled list (per plan) — same row
    //     shape, StaticId 0, no icon, no count, selected while petFeedSelectedFoodStaticId <= 0;
    //     it scrolls with the list instead of sitting fixed above the window (deliberate: the
    //     fixed row only existed because the manual window started below it).
    //   - The up/down arrows and track/thumb have NO port at all.
    //
    // Source nuances verified against DrawPetPlayTab, replayed exactly:
    //  - Style map (:229-234, :270-276, :311-318, :393-420): header = 15 bold white; card titles
    //    ("TRAINING"/"MY PETS"/"PET FOOD"/"FEEDING"/"FAVORITE FOODS") = 12 bold uiText at FULL
    //    alpha (labelStyle — NOT the 0.78-muted look other tabs use); status/stats/message/option
    //    text = 11-12pt uiText; pet names + dropdown value = 12 bold white; arrow + "x{N}" count
    //    = 12 bold accent; selected option text = white (mapped to the kit's text-on-accent rule
    //    on the accent row fill, Mass Cook precedent). The pets-card row separators map
    //    DrawCardOutline's 1px line to a 1px uiText@0.25 Image.
    //  - TRAINING card (:239-267, 160 tall): 3 toggles, each a flag write + a PetPlayLog line
    //    ("Cat play "/"Dog train "/"Pet wash " + enabled/disabled) — a log line, NOT a toast,
    //    NOT a save. DrawSwitchToggle localizes internally (UiKitPrimitives.cs:750), so the
    //    toggle labels get this.L here; buttons/labels on this tab are raw unlocalized EXCEPT
    //    the four feeding-card strings the source itself wraps in L (see below).
    //  - MY PETS card (:270-377) — DYNAMIC HEIGHT, the exact source formula reproduced:
    //    petCareRowCount = petCareListVisible ? petCareEntries.Count : 0;
    //    height = 116 + rowCount*72 + (listVisible && rowCount==0 ? 22 : 0). Live status label
    //    (petCareListStatus) beside the title; "Refresh"/"Show My Pets" button (label swaps on
    //    petCareListVisible) → petCareListVisible = true + RefreshPetCareList(); "Train until
    //    all learned (+energy food)" toggle → on DISABLE only, StopPetCareTrainLoop("Train loop
    //    switched off.") — no save either direction.
    //  - The visible-list block (:307-377): TickPetCareAutoStatsRefresh() once per gated frame
    //    while visible (the source's own per-repaint refresh tick, self-throttled); empty-state
    //    label "No owned pets found nearby." when the entry count is 0; else pooled 72px rows.
    //    Row = name+type label (entry.Name or NetId fallback + "  (dog)"/"  (cat)" — double
    //    space), the stats line with per-field -1-sentinel "?" fallbacks and the actions/learned
    //    segment ENTIRELY OMITTED when MotionsTotal < 0 (" · actions ?" only — no learned
    //    segment), the message line, a separator before every row but the first, and the
    //    2-real-state button tail: (petCareBusy && IsPetCareEntryActiveSession(netId)) → ONE
    //    "Stop" button → StopPetCareActiveSession(); else → "Play"+"Wash" pair BOTH gated on the
    //    single shared petCareBusy flag → OnPetCarePlayClicked/OnPetCareWashClicked(entry).
    //    petCareBusy comes from ONE TryGetPetCareBusyLabel call per gated frame (the source's
    //    single pre-loop call at :325), NEVER per row; IsPetCareEntryActiveSession stays per-row
    //    like the source's :355. Click closures capture the ROW HANDLE (Research idiom); the
    //    handle's BoundEntry is rebound every frame so a click always acts on the live entry.
    //  - PET FOOD card (:379-588): closed height = 86 EXACTLY (source formula's else branch).
    //    The open height is THIS PORT'S OWN 386 (76 header zone + 300 panel + 10 pad) — the
    //    source's 34+(6+1)*36+114 = 400 encoded the fixed-visible-rows window this port replaces,
    //    so per plan the open height is a reasonable fixed panel of the same visual footprint.
    //    Header (:424-441): control-fill box + whole-header Button toggling the SHARED
    //    petFeedFoodDropdownOpen flag; 20x20 icon shown only while a food with a cached texture
    //    is selected (label inset flips 12↔34 to match the source's +24 shift); caption =
    //    GetPetFeedSelectedFoodLabel() LIVE per gated frame; accent "^"/"v" arrow. "Scan Food"/
    //    "Wait..." button (:443-449): label AND enabled state both re-evaluated every gated
    //    frame (time-dependent: !petFeedFoodScanInProgress && realtime >=
    //    petFeedNextFoodScanAllowedAt) → RefreshPetFeedFoodOptions() (which toasts, closes the
    //    dropdown and re-selects a default itself — the layout signature catches the close).
    //  - The open panel: search InputField (64-char limit, live per keystroke via onValueChanged
    //    + the applied-text gated poll pair — the Mass Cook/Teleport-NPC wiring-insurance idiom,
    //    external IMGUI-twin edits included) with a PLACEHOLDER ("Search pet food...", italic,
    //    uiText@0.48 — :470-481) implemented as a sibling label over the field, SetActive'd by
    //    string.IsNullOrEmpty(field.text) — the migration's first placeholder, no kit support
    //    needed. Search-change cascade per the design-decision block above. Option rows (:497-
    //    538): 24x24 RawImage icon from TryGetPetFeedFoodIconTexture (hidden when no texture —
    //    Food & Repair precedent; missing icons re-tried at 2Hz instead of the IMGUI twin's
    //    every-repaint hammering — the lookup fires an async RequestGameItemIconByStaticId on
    //    every miss), GetPetFeedFoodDisplayName label, trailing accent "x{Count}", selection
    //    highlight on petFeedSelectedFoodStaticId. Row click → SelectPetFeedFood(StaticId, RAW
    //    Name) — "Any Food" row passes (0, "Any Food") exactly like :502; the backend closes the
    //    dropdown itself (:2471).
    //  - FEEDING card (:590-624, 160 tall): "Feed All Cats"/"Feed All Dogs" both gated on the
    //    SAME live busy flag (petFeedAllCoroutine != null || realtime < petFeedAllBusyUntil —
    //    time-dependent, re-evaluated every gated frame) → StartPetFeedAll(false/true); "Skip 5
    //    star food" toggle = flag-only (no log, no toast, no save); "Show pets favorite food" →
    //    LogNearbyPetFavoriteFoods(). These four labels are L'd in source and stay localized.
    //  - FAVORITE FOODS table (PetFeedFeature.cs:4503-4571 + :4492-4501): contributes ZERO
    //    height/content while petFeedFavoriteUiRows.Count == 0 — BOTH source functions return
    //    early/zero in that case (no card, no header, nothing drawn); reproduced as the card
    //    SetActive(false) + zero cursor contribution. Otherwise a card with the 3-column header
    //    row (name/like/dislike — 11 bold, uiSubTabText muted color like the source's
    //    mutedColor) and a REAL nested ScrollRect body (the source's genuine GUI.BeginScrollView
    //    at :4553 — the Pictures-round nested-scroll approach, NOT a fake clipped container)
    //    showing up to PetFeedFavoriteUiMaxVisibleRows (6) of PetFeedFavoriteUiRowHeight (52)
    //    rows, wrapping text cells with the source's exact "?"/"(none)" fallbacks. Rows are
    //    plain pooled text — no icons, no click handlers. Column split replayed: colName = 92,
    //    colLike = (innerW - 92) * 0.52, colDislike = rest. The card height (52 title+header
    //    zone + body + 12 pad) sizes to actually CONTAIN the table (the IMGUI panelHeight
    //    (:4538) under-measured by its title zone and let content overdraw; TMP clips, so this
    //    port sizes honestly — same call the Mass Cook status label made).
    //  - Cursor parity: header +42; cards step height+14 (:268 num += 174 for the 160 card;
    //    :379/:588 Ceil(cardH + 14)); the favorite table starts at feeding.yMax + 12 (:624) and
    //    the total adds the source's trailing +40 (:626).
    //
    // Cross-surface sync cadence: every gated frame — 5 toggle re-syncs (SetIsOnWithoutNotify),
    // pets status label + Refresh/Show caption, the visible-list block (tick, ONE busy probe,
    // pooled pet-row bind with per-row diffed texts/button states), food header caption/icon/
    // arrow, scan gate+label, open-panel work only while open (search poll pair, placeholder,
    // full-list row bind), feeding busy gate, favorite-rows bind, then the layout-signature
    // check (listVisible, open, pet-row count, favorite visible-row count). Everything diffs
    // before writing; per-frame sync disabled after 3 consecutive errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handles (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiPetCarePetRowHandle
        {
            public GameObject Root;
            public GameObject Separator;      // active for every row but index 0 (:336-338)
            public GameObject NameLabel;
            public string NameShown;
            public GameObject StatsLabel;
            public string StatsShown;
            public GameObject MessageLabel;
            public string MessageShown;
            public GameObject StopButton;     // active-session state (:355-362)
            public GameObject PlayButton;     // idle state pair (:363-375)
            public GameObject WashButton;
            public PetCareEntry BoundEntry;   // rebound every frame — clicks act on the live entry
            public int ActiveSessionShown = -1;   // -1 forces the first apply
            public int PlayWashEnabledShown = -1;
        }

        private sealed class UguiPetCareFoodRowHandle
        {
            public GameObject Root;
            public Image Fill;                // clear / accent when selected (:499/:522)
            public GameObject IconGo;         // RawImage carrier — hidden when no texture (:531-535)
            public RawImage Icon;
            public GameObject NameLabel;
            public GameObject CountLabel;     // "x{Count}" — hidden on the Any Food row
            public string CountShown;
            public int BoundStaticId = int.MinValue;
            public string BoundName;          // RAW option name — SelectPetFeedFood gets this (:525)
            public string BoundDisplay;       // shown label (GetPetFeedFoodDisplayName)
            public bool SelectedShown;
            public bool IconShown;
        }

        private sealed class UguiPetCareFavRowHandle
        {
            public GameObject Root;
            public GameObject NameLabel;
            public string NameShown;
            public GameObject LikeLabel;
            public string LikeShown;
            public GameObject DislikeLabel;
            public string DislikeShown;
        }

        private sealed class UguiShellFeaturesPetCareHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float ContentWidth;            // block w minus kit viewport insets

            public Toggle AutoCatToggle;
            public Toggle AutoDogToggle;
            public Toggle AutoWashToggle;

            public GameObject PetsCard;
            public GameObject PetsStatusLabel;    // petCareListStatus — live
            public string PetsStatusShown;
            public GameObject RefreshButton;      // "Refresh"/"Show My Pets" caption swap
            public string RefreshShown;
            public Toggle TrainLoopToggle;
            public GameObject PetsEmptyLabel;     // "No owned pets found nearby."
            public readonly List<UguiPetCarePetRowHandle> PetRows = new List<UguiPetCarePetRowHandle>();

            public GameObject FoodCard;
            public GameObject FoodHeader;         // whole-header dropdown button
            public GameObject FoodHeaderIconGo;
            public RawImage FoodHeaderIcon;
            public int FoodHeaderIconShownId;     // 0 = none shown
            public GameObject FoodHeaderValue;
            public string FoodHeaderValueShown;
            public RectTransform FoodHeaderValueRt; // left inset flips 12↔34 with the icon
            public GameObject FoodArrow;          // "^" open / "v" closed
            public string FoodArrowShown;
            public GameObject ScanButton;         // "Scan Food"/"Wait..." — label + gate live
            public string ScanShown;
            public GameObject FoodPanel;          // the open-state panel
            public InputField FoodSearchField;
            public string FoodSearchApplied;      // poll-pair cache (external-change detection)
            public GameObject FoodSearchPlaceholder; // the migration's first placeholder label
            public GameObject FoodListScroll;
            public Transform FoodListContent;
            public readonly List<UguiPetCareFoodRowHandle> FoodRows = new List<UguiPetCareFoodRowHandle>();
            public float NextFoodIconRetryAt;     // 2Hz retry for missing icons (file header)

            public GameObject FeedCard;
            public GameObject FeedCatsButton;
            public GameObject FeedDogsButton;
            public Toggle SkipFiveStarToggle;

            public GameObject FavCard;
            public GameObject FavListScroll;      // nested ScrollRect — viewport resized by relayout
            public Transform FavListContent;
            public readonly List<UguiPetCareFavRowHandle> FavRows = new List<UguiPetCareFavRowHandle>();

            public int LayoutSignature = -1;
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellFeaturesPetCareHandle uguiShellFeaturesPetCare;

        // Content-local fixed geometry — the source's num cursor (left 40, width 520) re-based to
        // 8: header 8 (+42) → TRAINING card 50 (160 tall, +174) → MY PETS card 224. Everything
        // from 224 down is owned by the relayout (three of the four remaining blocks resize).
        private const float UguiPetCareConditionalTopY = 224f;
        private const float UguiPetCarePetRowsTopY = 110f;    // :334 — rows at petsRect.y + 110
        private const float UguiPetCarePetRowStep = 72f;      // :334 — i * 72
        private const float UguiPetCareFoodRowStep = 36f;     // :383 petFoodOptionHeight
        private const float UguiPetCareFoodPanelHeight = 300f; // ≈ source :454 (34 + 7*36 + 14)
        private const float UguiPetCareFoodCardClosedHeight = 86f; // :387 — EXACT source formula
        private const float UguiPetCareFoodCardOpenHeight = 386f;  // this port's own (file header)

        // ----------------------------------------------------------------------------------------
        // Live layout signature — list visibility, dropdown-open, pet-row count and favorite
        // visible-row count all drive real layout. The food OPTION count is deliberately absent:
        // the open panel is a fixed 300 and the options scroll inside it (design decision).
        // ----------------------------------------------------------------------------------------

        private int ComputeUguiFeaturesPetCareLayoutSignature()
        {
            int petRowCount = this.petCareListVisible ? this.petCareEntries.Count : 0;
            int favVisible = Math.Min(PetFeedFavoriteUiMaxVisibleRows, this.petFeedFavoriteUiRows.Count);
            return (this.petCareListVisible ? 1 : 0)
                 | (this.petFeedFoodDropdownOpen ? 2 : 0)
                 | (Mathf.Clamp(petRowCount, 0, 1023) << 2)
                 | (favVisible << 12);
        }

        // :340-342 — NetId fallback + the double-spaced type suffix.
        private static string BuildUguiFeaturesPetCarePetNameLine(PetCareEntry entry)
        {
            return (string.IsNullOrEmpty(entry.Name) ? entry.NetId.ToString() : entry.Name)
                + (entry.IsDog ? "  (dog)" : "  (cat)");
        }

        // :346-351 — per-field -1-sentinel "?" fallbacks; the actions/learned segment is ENTIRELY
        // OMITTED (replaced by " · actions ?") when MotionsTotal < 0 — no "learned" text at all.
        private static string BuildUguiFeaturesPetCarePetStatsLine(PetCareEntry entry)
        {
            string petStatsLine = "energy " + (entry.Vitality >= 0 ? entry.Vitality.ToString() : "?")
                + " · food " + (entry.Fullness >= 0 ? entry.Fullness.ToString() : "?")
                + " · growth " + (entry.Chemistry >= 0 ? entry.Chemistry.ToString() : "?");
            petStatsLine += entry.MotionsTotal >= 0
                ? " · actions " + entry.MotionsUnlocked + "/" + entry.MotionsTotal + " · learned " + entry.MotionsLearned
                : " · actions ?";
            return petStatsLine;
        }

        // The kit has no italic setter (this round introduces the migration's first placeholder
        // text) — TMP first, legacy Text fallback, mirroring TrySetUguiLabelBold's shape.
        private void TrySetUguiFeaturesPetCareLabelItalic(GameObject label)
        {
            if (label == null)
            {
                return;
            }
            try
            {
                TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.fontStyle = FontStyles.Italic;
                    return;
                }
                Text txt = label.GetComponent<Text>();
                if (txt != null)
                {
                    txt.fontStyle = FontStyle.Italic;
                }
            }
            catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawPetPlayTab + DrawPetFeedFavoriteFoodsTable: header, TRAINING card,
        // MY PETS card (dynamic), PET FOOD card (dynamic), FEEDING card, FAVORITE FOODS card
        // (dynamic, zero-when-empty) — everything built ONCE; RelayoutUguiShellFeaturesPetCare
        // owns positions from MY PETS down and the total scroll height. Handle assigned LAST
        // (Research idiom).
        private GameObject BuildUguiShellFeaturesPetCareContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellFeaturesPetCare = null;

            UguiShellFeaturesPetCareHandle handle = new UguiShellFeaturesPetCareHandle();
            GameObject block = this.CreateUguiGo("FeaturesPetCareContent", parent);
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
            handle.ContentWidth = w - 22f; // viewport insets: 4 left + 18 right

            const float rowX = 8f;
            float rowW = handle.ContentWidth - 16f;
            // Style map (file header): titles = 12 bold uiText FULL alpha; body = uiText; muted
            // (favorite-table headers only) = uiSubTabText; names/values = white.
            Color textColor = this.UguiKitTextColor();
            Color favHeaderColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 1f);
            Color separatorColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.25f);
            Color accent = this.UguiKitAccent();

            // -------- Header (:236) --------
            GameObject header = this.CreateUguiLabel(scrollContent, "Header", "PET CARE", 15f, Color.white, false);
            this.TrySetUguiLabelBold(header);
            PlaceUguiTopLeft(header, rowX, 8f, rowW, 30f);

            // -------- TRAINING card (:239-267, static at 50) --------
            GameObject trainCard = this.CreateUguiGo("TrainingCard", scrollContent);
            PlaceUguiTopLeft(trainCard, rowX, 50f, rowW, 160f);
            this.AddUguiImage(trainCard, this.UguiKitPanelBg(), true, 1f);
            GameObject trainTitle = this.CreateUguiLabel(trainCard.transform, "Title", "TRAINING", 12f, textColor, false);
            this.TrySetUguiLabelBold(trainTitle);
            PlaceUguiTopLeft(trainTitle, 16f, 12f, 180f, 20f);

            // :244-266 — rowY = +40 stepping 42; flag + PetPlayLog only (file header).
            handle.AutoCatToggle = this.CreateUguiCheckbox(trainCard.transform, "AutoCatToggle",
                this.L("Auto Cat Play"), this.petPlayAutoCatEnabled,
                new System.Action<bool>(this.OnUguiFeaturesPetCareAutoCatToggled));
            PlaceUguiTopLeft(handle.AutoCatToggle.gameObject, 16f, 40f, 250f, 28f);
            handle.AutoDogToggle = this.CreateUguiCheckbox(trainCard.transform, "AutoDogToggle",
                this.L("Auto Dog Train"), this.petPlayAutoDogEnabled,
                new System.Action<bool>(this.OnUguiFeaturesPetCareAutoDogToggled));
            PlaceUguiTopLeft(handle.AutoDogToggle.gameObject, 16f, 82f, 250f, 28f);
            handle.AutoWashToggle = this.CreateUguiCheckbox(trainCard.transform, "AutoWashToggle",
                this.L("Auto Pet Wash"), this.petPlayAutoWashEnabled,
                new System.Action<bool>(this.OnUguiFeaturesPetCareAutoWashToggled));
            PlaceUguiTopLeft(handle.AutoWashToggle.gameObject, 16f, 124f, 250f, 28f);

            // -------- MY PETS card (:270-377 — dynamic height via relayout) --------
            handle.PetsCard = this.CreateUguiGo("PetsCard", scrollContent);
            this.AddUguiImage(handle.PetsCard, this.UguiKitPanelBg(), true, 1f);
            GameObject petsTitle = this.CreateUguiLabel(handle.PetsCard.transform, "Title", "MY PETS", 12f, textColor, false);
            this.TrySetUguiLabelBold(petsTitle);
            PlaceUguiTopLeft(petsTitle, 16f, 12f, 130f, 20f);
            handle.PetsStatusShown = this.petCareListStatus ?? string.Empty;
            handle.PetsStatusLabel = this.CreateUguiLabel(handle.PetsCard.transform, "Status",
                handle.PetsStatusShown, 11f, textColor, false);
            PlaceUguiTopLeft(handle.PetsStatusLabel, 150f, 14f, rowW - 166f, 16f); // :284

            handle.RefreshShown = this.petCareListVisible ? "Refresh" : "Show My Pets";
            handle.RefreshButton = this.CreateUguiPrimaryButton(handle.PetsCard.transform, "RefreshButton",
                handle.RefreshShown, new System.Action(this.OnUguiFeaturesPetCareRefreshClicked));
            PlaceUguiTopLeft(handle.RefreshButton, 16f, 38f, 160f, 30f); // :286

            handle.TrainLoopToggle = this.CreateUguiCheckbox(handle.PetsCard.transform, "TrainLoopToggle",
                this.L("Train until all learned (+energy food)"), this.petCareTrainLoopEnabled,
                new System.Action<bool>(this.OnUguiFeaturesPetCareTrainLoopToggled));
            PlaceUguiTopLeft(handle.TrainLoopToggle.gameObject, 16f, 74f, rowW - 32f, 28f); // :294-297

            handle.PetsEmptyLabel = this.CreateUguiLabel(handle.PetsCard.transform, "EmptyLabel",
                "No owned pets found nearby.", 11f, textColor, false);
            PlaceUguiTopLeft(handle.PetsEmptyLabel, 16f, 110f, rowW - 32f, 18f); // :322
            handle.PetsEmptyLabel.SetActive(false);

            // -------- PET FOOD card (:379-588 — dynamic height via relayout) --------
            handle.FoodCard = this.CreateUguiGo("FoodCard", scrollContent);
            this.AddUguiImage(handle.FoodCard, this.UguiKitPanelBg(), true, 1f);
            GameObject foodTitle = this.CreateUguiLabel(handle.FoodCard.transform, "Title", "PET FOOD", 12f, textColor, false);
            this.TrySetUguiLabelBold(foodTitle);
            PlaceUguiTopLeft(foodTitle, 16f, 12f, 180f, 20f);

            GameObject foodCaption = this.CreateUguiLabel(handle.FoodCard.transform, "Caption", "Pet Food", 12f, textColor, false);
            this.TrySetUguiLabelBold(foodCaption);
            PlaceUguiTopLeft(foodCaption, 16f, 45f, 72f, 20f); // :423 — foodY + 5

            // Dropdown header (:424-441): control-fill box, whole-header button, icon + value +
            // accent arrow. The SHARED petFeedFoodDropdownOpen flag is the panel's model on both
            // surfaces (Mass Cook precedent — scan/pick cascades close both surfaces at once).
            handle.FoodHeader = this.CreateUguiGo("FoodHeader", handle.FoodCard.transform);
            PlaceUguiTopLeft(handle.FoodHeader, 92f, 40f, rowW - 224f, 28f); // :424
            Image foodHeaderBg = this.AddUguiImage(handle.FoodHeader, this.UguiKitControlFill(), true, 1.5f);
            foodHeaderBg.raycastTarget = true;
            Button foodHeaderBtn = handle.FoodHeader.AddComponent<Button>();
            foodHeaderBtn.targetGraphic = foodHeaderBg;
            foodHeaderBtn.onClick.AddListener(new System.Action(this.OnUguiFeaturesPetCareFoodHeaderClicked));

            handle.FoodHeaderIconGo = this.CreateUguiGo("Icon", handle.FoodHeader.transform);
            PlaceUguiTopLeft(handle.FoodHeaderIconGo, 7f, 4f, 20f, 20f); // :435
            handle.FoodHeaderIcon = handle.FoodHeaderIconGo.AddComponent<RawImage>();
            handle.FoodHeaderIcon.raycastTarget = false;
            handle.FoodHeaderIconGo.SetActive(false);
            handle.FoodHeaderIconShownId = 0;

            handle.FoodHeaderValueShown = this.GetPetFeedSelectedFoodLabel();
            handle.FoodHeaderValue = this.CreateUguiLabel(handle.FoodHeader.transform, "Value",
                handle.FoodHeaderValueShown, 12f, Color.white, false);
            this.TrySetUguiLabelBold(handle.FoodHeaderValue);
            StretchUguiFill(handle.FoodHeaderValue, 12f, 1f, 34f, 1f); // :431-432; left flips to 34 with the icon
            handle.FoodHeaderValueRt = handle.FoodHeaderValue.GetComponent<RectTransform>();

            handle.FoodArrowShown = this.petFeedFoodDropdownOpen ? "^" : "v";
            handle.FoodArrow = this.CreateUguiLabel(handle.FoodHeader.transform, "Arrow",
                handle.FoodArrowShown, 12f, accent, true);
            this.TrySetUguiLabelBold(handle.FoodArrow);
            RectTransform arrowRt = handle.FoodArrow.GetComponent<RectTransform>();
            arrowRt.anchorMin = new Vector2(1f, 0.5f);   // :441 — 14 wide at xMax - 22
            arrowRt.anchorMax = new Vector2(1f, 0.5f);
            arrowRt.pivot = new Vector2(1f, 0.5f);
            arrowRt.anchoredPosition = new Vector2(-8f, 0f);
            arrowRt.sizeDelta = new Vector2(16f, 26f);

            handle.ScanShown = "Scan Food";
            handle.ScanButton = this.CreateUguiPrimaryButton(handle.FoodCard.transform, "ScanButton",
                handle.ScanShown, new System.Action(this.OnUguiFeaturesPetCareScanFoodClicked));
            PlaceUguiTopLeft(handle.ScanButton, rowW - 116f, 40f, 100f, 28f); // :445

            // The open panel (:451-586 mapped per the design-decision block): search field +
            // placeholder + ONE pooled row list in a real nested ScrollRect.
            handle.FoodPanel = this.CreateUguiGo("FoodPanel", handle.FoodCard.transform);
            PlaceUguiTopLeft(handle.FoodPanel, 16f, 76f, rowW - 32f, UguiPetCareFoodPanelHeight); // :454 — header yMax + 8
            this.AddUguiImage(handle.FoodPanel, this.UguiKitContentBg(), true, 1f); // :455 themeContentStyle
            float panelW = rowW - 32f;

            handle.FoodSearchApplied = this.petFeedFoodSearchText ?? string.Empty;
            handle.FoodSearchField = this.CreateUguiInputField(handle.FoodPanel.transform, "SearchField",
                handle.FoodSearchApplied, 64,
                new System.Action<string>(this.OnUguiFeaturesPetCareFoodSearchChanged));
            PlaceUguiTopLeft(handle.FoodSearchField.gameObject, 8f, 6f, panelW - 16f, 26f); // :465

            // Placeholder (:470-481): sibling label OVER the field (raycastTarget=false from the
            // kit), italic, uiText@0.48, active only while the field is empty (file header).
            handle.FoodSearchPlaceholder = this.CreateUguiLabel(handle.FoodPanel.transform, "SearchPlaceholder",
                "Search pet food...", 12f,
                new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.48f), false);
            this.TrySetUguiFeaturesPetCareLabelItalic(handle.FoodSearchPlaceholder);
            PlaceUguiTopLeft(handle.FoodSearchPlaceholder, 17f, 7f, panelW - 34f, 24f); // :480
            handle.FoodSearchPlaceholder.SetActive(string.IsNullOrEmpty(handle.FoodSearchApplied));

            Transform foodListContent;
            handle.FoodListScroll = this.CreateUguiScrollView(handle.FoodPanel.transform, "FoodList",
                10f, out foodListContent);
            PlaceUguiTopLeft(handle.FoodListScroll, 4f, 40f, panelW - 8f, UguiPetCareFoodPanelHeight - 46f); // :496
            handle.FoodListContent = foodListContent;
            try
            {
                Image listBg = handle.FoodListScroll.GetComponent<Image>();
                if (listBg != null)
                {
                    listBg.color = Color.clear; // the panel itself is the box (:455)
                }
                if (foodListContent != null && foodListContent.parent != null)
                {
                    Image listVpBg = foodListContent.parent.GetComponent<Image>();
                    if (listVpBg != null)
                    {
                        listVpBg.color = Color.clear;
                    }
                }
            }
            catch { }
            handle.FoodPanel.SetActive(false); // relayout applies the real open state below

            // -------- FEEDING card (:590-624, 160 tall; position via relayout) --------
            handle.FeedCard = this.CreateUguiGo("FeedCard", scrollContent);
            this.AddUguiImage(handle.FeedCard, this.UguiKitPanelBg(), true, 1f);
            GameObject feedTitle = this.CreateUguiLabel(handle.FeedCard.transform, "Title", "FEEDING", 12f, textColor, false);
            this.TrySetUguiLabelBold(feedTitle);
            PlaceUguiTopLeft(feedTitle, 16f, 12f, 180f, 20f);

            handle.FeedCatsButton = this.CreateUguiPrimaryButton(handle.FeedCard.transform, "FeedCatsButton",
                this.L("Feed All Cats"), new System.Action(this.OnUguiFeaturesPetCareFeedCatsClicked));
            PlaceUguiTopLeft(handle.FeedCatsButton, 16f, 38f, 150f, 32f); // :598
            handle.FeedDogsButton = this.CreateUguiPrimaryButton(handle.FeedCard.transform, "FeedDogsButton",
                this.L("Feed All Dogs"), new System.Action(this.OnUguiFeaturesPetCareFeedDogsClicked));
            PlaceUguiTopLeft(handle.FeedDogsButton, 180f, 38f, 150f, 32f); // :603

            handle.SkipFiveStarToggle = this.CreateUguiCheckbox(handle.FeedCard.transform, "SkipFiveStarToggle",
                this.L("Skip 5 star food"), this.petFeedSkipFiveStarFood,
                new System.Action<bool>(this.OnUguiFeaturesPetCareSkipFiveStarToggled));
            PlaceUguiTopLeft(handle.SkipFiveStarToggle.gameObject, 16f, 78f, rowW - 32f, 28f); // :610

            GameObject favFoodButton = this.CreateUguiPrimaryButton(handle.FeedCard.transform, "FavFoodButton",
                this.L("Show pets favorite food"), new System.Action(this.OnUguiFeaturesPetCareShowFavoritesClicked));
            PlaceUguiTopLeft(favFoodButton, 16f, 112f, rowW - 32f, 30f); // :619

            // -------- FAVORITE FOODS card (PetFeedFeature.cs:4503-4571 — zero-when-empty) --------
            handle.FavCard = this.CreateUguiGo("FavCard", scrollContent);
            this.AddUguiImage(handle.FavCard, this.UguiKitPanelBg(), true, 1f);
            GameObject favTitle = this.CreateUguiLabel(handle.FavCard.transform, "Title", "FAVORITE FOODS", 12f, textColor, false);
            this.TrySetUguiLabelBold(favTitle);
            PlaceUguiTopLeft(favTitle, 16f, 8f, 220f, 18f); // :4543

            // Column split (:4531-4534) — computed on the CELL inner width so headers and cells
            // line up (content sits at card x 12 + the kit viewport's 4px left inset).
            float favInnerW = handle.ContentWidth - 16f - 24f - 22f;
            float favColName = 92f;
            float favColLike = (favInnerW - favColName) * 0.52f;
            float favColDislike = favInnerW - favColName - favColLike;
            GameObject favHeadName = this.CreateUguiLabel(handle.FavCard.transform, "HeadName", "name", 11f, favHeaderColor, false);
            this.TrySetUguiLabelBold(favHeadName);
            PlaceUguiTopLeft(favHeadName, 16f, 30f, favColName, 22f); // :4547
            GameObject favHeadLike = this.CreateUguiLabel(handle.FavCard.transform, "HeadLike", "like", 11f, favHeaderColor, false);
            this.TrySetUguiLabelBold(favHeadLike);
            PlaceUguiTopLeft(favHeadLike, 16f + favColName, 30f, favColLike, 22f);
            GameObject favHeadDislike = this.CreateUguiLabel(handle.FavCard.transform, "HeadDislike", "dislike", 11f, favHeaderColor, false);
            this.TrySetUguiLabelBold(favHeadDislike);
            PlaceUguiTopLeft(favHeadDislike, 16f + favColName + favColLike, 30f, favColDislike, 22f);

            Transform favListContent;
            handle.FavListScroll = this.CreateUguiScrollView(handle.FavCard.transform, "FavList",
                10f, out favListContent);
            PlaceUguiTopLeft(handle.FavListScroll, 12f, 52f, handle.ContentWidth - 16f - 24f,
                PetFeedFavoriteUiRowHeight); // real height set by the relayout
            handle.FavListContent = favListContent;
            try
            {
                Image favBg = handle.FavListScroll.GetComponent<Image>();
                if (favBg != null)
                {
                    favBg.color = Color.clear;
                }
                if (favListContent != null && favListContent.parent != null)
                {
                    Image favVpBg = favListContent.parent.GetComponent<Image>();
                    if (favVpBg != null)
                    {
                        favVpBg.color = Color.clear;
                    }
                }
            }
            catch { }
            handle.FavCard.SetActive(false); // relayout applies the real presence below

            // Keep the separator color available to row creation without re-deriving it there.
            this.uguiPetCareSeparatorColor = separatorColor;

            // Prime the dynamic regions for the current state.
            handle.LayoutSignature = this.ComputeUguiFeaturesPetCareLayoutSignature();
            this.RelayoutUguiShellFeaturesPetCare(handle);

            handle.Root = block;
            this.uguiShellFeaturesPetCare = handle;
            return block;
        }

        // Snapshot of the pets-row separator color for pooled-row creation (rows are created
        // lazily, after the builder's locals are gone; rebuilt with the shell on theme changes).
        private Color uguiPetCareSeparatorColor = new Color(1f, 1f, 1f, 0.25f);

        // ----------------------------------------------------------------------------------------
        // Relayout — positions everything from MY PETS down and sets the total scroll height,
        // mirroring the source's num accumulation (:268-626). Reposition/SetActive only;
        // per-frame text/value syncs stay in the processor.
        // ----------------------------------------------------------------------------------------

        private void RelayoutUguiShellFeaturesPetCare(UguiShellFeaturesPetCareHandle handle)
        {
            const float rowX = 8f;
            float rowW = handle.ContentWidth - 16f;
            float yCur = UguiPetCareConditionalTopY;

            // :278-279 — the EXACT source height formula.
            int petRowCount = this.petCareListVisible ? this.petCareEntries.Count : 0;
            float petsCardHeight = 116f + petRowCount * UguiPetCarePetRowStep
                + (this.petCareListVisible && petRowCount == 0 ? 22f : 0f);
            PlaceUguiTopLeft(handle.PetsCard, rowX, yCur, rowW, petsCardHeight);
            SetUguiGoActive(handle.PetsEmptyLabel, this.petCareListVisible && petRowCount == 0); // :320-323
            yCur += Mathf.CeilToInt(petsCardHeight + 14f); // :379

            bool open = this.petFeedFoodDropdownOpen;
            float foodCardHeight = open ? UguiPetCareFoodCardOpenHeight : UguiPetCareFoodCardClosedHeight;
            PlaceUguiTopLeft(handle.FoodCard, rowX, yCur, rowW, foodCardHeight);
            SetUguiGoActive(handle.FoodPanel, open);
            yCur += Mathf.CeilToInt(foodCardHeight + 14f); // :588

            PlaceUguiTopLeft(handle.FeedCard, rowX, yCur, rowW, 160f); // :590
            yCur += 160f + 12f; // :624 — the table starts at feedActionRect.yMax + 12

            // Zero-when-empty (file header): BOTH source functions return early/zero at 0 rows.
            int favVisible = Math.Min(PetFeedFavoriteUiMaxVisibleRows, this.petFeedFavoriteUiRows.Count);
            SetUguiGoActive(handle.FavCard, favVisible > 0);
            if (favVisible > 0)
            {
                float favBodyHeight = favVisible * PetFeedFavoriteUiRowHeight; // :4537
                float favCardHeight = 52f + favBodyHeight + 12f;
                PlaceUguiTopLeft(handle.FavCard, rowX, yCur, rowW, favCardHeight);
                PlaceUguiTopLeft(handle.FavListScroll, 12f, 52f, rowW - 24f, favBodyHeight);
                yCur += favCardHeight + 14f; // :4570
            }

            this.SetUguiScrollContentHeight(handle.ScrollContent, yCur + 40f); // :626 — return num + 40
        }

        private void RefreshUguiFeaturesPetCareLayout(UguiShellFeaturesPetCareHandle handle)
        {
            int signature = this.ComputeUguiFeaturesPetCareLayoutSignature();
            if (signature != handle.LayoutSignature)
            {
                handle.LayoutSignature = signature;
                this.RelayoutUguiShellFeaturesPetCare(handle);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Pooled MY PETS rows (grow-on-demand, rebind-by-diff, deactivate-not-destroy) — 72px
        // pitch inside the growing card (the OUTER scroll is the scroller; the card just grows,
        // like the source). Bound every gated frame; when the list is hidden every row deactivates.
        // ----------------------------------------------------------------------------------------

        private UguiPetCarePetRowHandle CreateUguiFeaturesPetCarePetRow(
            UguiShellFeaturesPetCareHandle handle, int index)
        {
            UguiPetCarePetRowHandle row = new UguiPetCarePetRowHandle();
            float rowW = handle.ContentWidth - 16f;

            GameObject root = this.CreateUguiGo("Pet" + index, handle.PetsCard.transform);
            PlaceUguiTopLeft(root, 0f, UguiPetCarePetRowsTopY + index * UguiPetCarePetRowStep,
                rowW, UguiPetCarePetRowStep);

            // :336-338 — a separator ABOVE every row but the first (card-local rowTop - 5 → row-
            // local -5; the card has no mask, the outer viewport clips like IMGUI's window did).
            GameObject separator = this.CreateUguiGo("Separator", root.transform);
            PlaceUguiTopLeft(separator, 12f, -5f, rowW - 24f, 1f);
            this.AddUguiImage(separator, this.uguiPetCareSeparatorColor, false, 1f);
            separator.SetActive(index > 0);
            row.Separator = separator;

            row.NameLabel = this.CreateUguiLabel(root.transform, "Name", "", 12f, Color.white, false);
            this.TrySetUguiLabelBold(row.NameLabel);
            PlaceUguiTopLeft(row.NameLabel, 16f, 0f, rowW - 200f, 18f); // :340

            row.StatsLabel = this.CreateUguiLabel(root.transform, "Stats", "", 11f, this.UguiKitTextColor(), false);
            PlaceUguiTopLeft(row.StatsLabel, 16f, 28f, rowW - 32f, 18f); // :352

            row.MessageLabel = this.CreateUguiLabel(root.transform, "Message", "", 11f, this.UguiKitTextColor(), false);
            PlaceUguiTopLeft(row.MessageLabel, 16f, 46f, rowW - 32f, 18f); // :353

            // Button tail (file header): Stop alone in the active-session state; Play+Wash pair
            // otherwise (all themePrimaryButtonStyle in source). Closures capture the ROW HANDLE —
            // BoundEntry is rebound every frame, so clicks always act on the live entry.
            UguiPetCarePetRowHandle captured = row;
            row.StopButton = this.CreateUguiPrimaryButton(root.transform, "StopButton", "Stop",
                new System.Action(() => this.OnUguiFeaturesPetCarePetStopClicked(captured)));
            PlaceUguiTopLeft(row.StopButton, rowW - 96f, 0f, 80f, 26f); // :358
            row.StopButton.SetActive(false);
            row.PlayButton = this.CreateUguiPrimaryButton(root.transform, "PlayButton", "Play",
                new System.Action(() => this.OnUguiFeaturesPetCarePetPlayClicked(captured)));
            PlaceUguiTopLeft(row.PlayButton, rowW - 180f, 0f, 80f, 26f); // :366
            row.WashButton = this.CreateUguiPrimaryButton(root.transform, "WashButton", "Wash",
                new System.Action(() => this.OnUguiFeaturesPetCarePetWashClicked(captured)));
            PlaceUguiTopLeft(row.WashButton, rowW - 96f, 0f, 80f, 26f); // :370

            row.Root = root;
            return row;
        }

        private void SyncUguiFeaturesPetCarePetRows(UguiShellFeaturesPetCareHandle handle)
        {
            int count = this.petCareListVisible ? this.petCareEntries.Count : 0;

            // ONE busy probe per gated frame — the source's single pre-loop call (:325); the
            // per-NETID active-session check stays per-row like the source's :355.
            bool petCareBusy = false;
            if (count > 0)
            {
                string petCareBusyLabel;
                petCareBusy = this.TryGetPetCareBusyLabel(out petCareBusyLabel);
            }

            for (int i = 0; i < count; i++)
            {
                if (i >= handle.PetRows.Count)
                {
                    handle.PetRows.Add(this.CreateUguiFeaturesPetCarePetRow(handle, i));
                }
                UguiPetCarePetRowHandle row = handle.PetRows[i];
                PetCareEntry entry = (i < this.petCareEntries.Count) ? this.petCareEntries[i] : null;
                if (entry == null)
                {
                    // :329-332 — the source skips drawing but keeps the 72px slot (the height
                    // formula counts it); an inactive root keeps the slot here the same way.
                    if (row.Root != null && row.Root.activeSelf)
                    {
                        row.Root.SetActive(false);
                    }
                    continue;
                }
                if (row.Root != null && !row.Root.activeSelf)
                {
                    row.Root.SetActive(true);
                }
                row.BoundEntry = entry;

                this.SyncUguiSelfLabelText(row.NameLabel, ref row.NameShown,
                    BuildUguiFeaturesPetCarePetNameLine(entry));
                this.SyncUguiSelfLabelText(row.StatsLabel, ref row.StatsShown,
                    BuildUguiFeaturesPetCarePetStatsLine(entry));
                this.SyncUguiSelfLabelText(row.MessageLabel, ref row.MessageShown,
                    entry.Message ?? string.Empty); // :353

                // The 2-real-state tail (:355-375), diffed per row.
                bool rowIsActiveSession = petCareBusy && this.IsPetCareEntryActiveSession(entry.NetId);
                int sessionState = rowIsActiveSession ? 1 : 0;
                if (sessionState != row.ActiveSessionShown)
                {
                    row.ActiveSessionShown = sessionState;
                    SetUguiGoActive(row.StopButton, rowIsActiveSession);
                    SetUguiGoActive(row.PlayButton, !rowIsActiveSession);
                    SetUguiGoActive(row.WashButton, !rowIsActiveSession);
                }
                if (!rowIsActiveSession)
                {
                    // :365 — BOTH gated together on the one shared flag.
                    int enabledState = petCareBusy ? 0 : 1;
                    if (enabledState != row.PlayWashEnabledShown)
                    {
                        row.PlayWashEnabledShown = enabledState;
                        this.SetUguiButtonInteractable(row.PlayButton, !petCareBusy);
                        this.SetUguiButtonInteractable(row.WashButton, !petCareBusy);
                    }
                }
            }

            for (int i = count; i < handle.PetRows.Count; i++)
            {
                UguiPetCarePetRowHandle row = handle.PetRows[i];
                if (row.Root != null && row.Root.activeSelf)
                {
                    row.Root.SetActive(false);
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Pooled PET FOOD option rows — "Any Food" is row 0 of the SAME pool (design decision);
        // bound from the FULL GetPetFeedFoodDropdownOptions() result every gated frame while the
        // panel is open (parity: the source re-filters per repaint at :488).
        // ----------------------------------------------------------------------------------------

        private UguiPetCareFoodRowHandle CreateUguiFeaturesPetCareFoodRow(
            UguiShellFeaturesPetCareHandle handle, int index, float innerW)
        {
            UguiPetCareFoodRowHandle row = new UguiPetCareFoodRowHandle();

            GameObject root = this.CreateUguiGo("Food" + index, handle.FoodListContent);
            PlaceUguiTopLeft(root, 0f, index * UguiPetCareFoodRowStep, innerW, UguiPetCareFoodRowStep);
            // Unselected rows draw NO box in source (GUIStyle.none) — a clear fill keeps the
            // raycast surface (alpha-0 images still raycast) and flips to accent when selected.
            row.Fill = this.AddUguiImage(root, Color.clear, true, 1.5f);
            row.Fill.raycastTarget = true;
            Button btn = root.AddComponent<Button>();
            btn.targetGraphic = row.Fill;
            UguiPetCareFoodRowHandle captured = row;
            btn.onClick.AddListener(new System.Action(
                () => this.OnUguiFeaturesPetCareFoodRowClicked(captured)));

            GameObject iconGo = this.CreateUguiGo("Icon", root.transform);
            PlaceUguiTopLeft(iconGo, 10f, 6f, 24f, 24f); // :533
            row.Icon = iconGo.AddComponent<RawImage>();
            row.Icon.raycastTarget = false;
            iconGo.SetActive(false);
            row.IconGo = iconGo;

            row.NameLabel = this.CreateUguiLabel(root.transform, "Name", "", 12f, this.UguiKitTextColor(), false);
            PlaceUguiTopLeft(row.NameLabel, 42f, 2f, innerW - 88f, 32f); // :529-530, :536

            row.CountLabel = this.CreateUguiLabel(root.transform, "Count", "", 12f, this.UguiKitAccent(), true);
            this.TrySetUguiLabelBold(row.CountLabel);
            PlaceUguiTopLeft(row.CountLabel, innerW - 42f, 2f, 36f, 32f); // :537
            row.CountLabel.SetActive(false);

            row.Root = root;
            return row;
        }

        private void BindUguiFeaturesPetCareFoodRow(UguiPetCareFoodRowHandle row,
            int staticId, string rawName, string display, int count, bool showCount, bool allowIconRetry)
        {
            bool rebind = row.BoundStaticId != staticId
                || !string.Equals(row.BoundDisplay, display, StringComparison.Ordinal);
            if (rebind)
            {
                row.BoundStaticId = staticId;
                row.BoundName = rawName;
                row.BoundDisplay = display;
                this.SetUguiLabelText(row.NameLabel, display);
            }

            string countText = showCount ? ("x" + count) : string.Empty;
            if (!string.Equals(countText, row.CountShown, StringComparison.Ordinal))
            {
                row.CountShown = countText;
                if (showCount)
                {
                    this.SetUguiLabelText(row.CountLabel, countText);
                }
                SetUguiGoActive(row.CountLabel, showCount);
            }

            // Icon: resolve on rebind; while missing, re-try only on the shared 2Hz timer (the
            // lookup fires an async icon request on every miss — file header).
            if (rebind || (!row.IconShown && allowIconRetry))
            {
                Texture2D tex = null;
                bool has = staticId > 0 && this.TryGetPetFeedFoodIconTexture(staticId, out tex) && tex != null;
                if (has != row.IconShown || (has && rebind))
                {
                    row.IconShown = has;
                    if (has && row.Icon != null)
                    {
                        try { row.Icon.texture = tex; } catch { }
                    }
                    SetUguiGoActive(row.IconGo, has);
                }
            }

            // Selection diffs per bind — an IMGUI-twin pick or a scan's default-select moves it.
            bool selected = staticId <= 0
                ? this.petFeedSelectedFoodStaticId <= 0   // :498 — Any Food
                : staticId == this.petFeedSelectedFoodStaticId; // :521
            if (selected != row.SelectedShown)
            {
                row.SelectedShown = selected;
                try
                {
                    if (row.Fill != null)
                    {
                        row.Fill.color = selected ? this.UguiKitAccent() : Color.clear;
                    }
                }
                catch { }
                this.SetUguiLabelColor(row.NameLabel, selected
                    ? this.GetUiTextOnAccent(this.UguiKitAccent())
                    : this.UguiKitTextColor());
            }
        }

        private void SyncUguiFeaturesPetCareFoodRows(UguiShellFeaturesPetCareHandle handle)
        {
            // The FULL (unclamped) filtered list — never windowed, never reimplemented; the
            // nested ScrollRect owns all scrolling (design decision, file header).
            List<PetFeedFoodOption> visible = this.GetPetFeedFoodDropdownOptions();
            int optionCount = visible.Count;
            float innerW = handle.ContentWidth - 16f - 32f - 8f - 22f; // card→panel→scroll→viewport insets

            float now = Time.realtimeSinceStartup;
            bool allowIconRetry = now >= handle.NextFoodIconRetryAt;

            int rowTotal = optionCount + 1; // + the Any Food row
            for (int i = 0; i < rowTotal; i++)
            {
                if (i >= handle.FoodRows.Count)
                {
                    handle.FoodRows.Add(this.CreateUguiFeaturesPetCareFoodRow(handle, i, innerW));
                }
                UguiPetCareFoodRowHandle row = handle.FoodRows[i];
                if (row.Root != null && !row.Root.activeSelf)
                {
                    row.Root.SetActive(true);
                }

                if (i == 0)
                {
                    // :497-504 — Any Food: id 0, raw name "Any Food", no icon, no count.
                    this.BindUguiFeaturesPetCareFoodRow(row, 0, "Any Food", "Any Food", 0, false, false);
                    continue;
                }

                PetFeedFoodOption option = visible[i - 1];
                if (option == null)
                {
                    if (row.Root != null && row.Root.activeSelf)
                    {
                        row.Root.SetActive(false); // :515-518 — slot kept, nothing drawn
                    }
                    continue;
                }
                this.BindUguiFeaturesPetCareFoodRow(row, option.StaticId, option.Name,
                    this.GetPetFeedFoodDisplayName(option.StaticId, option.Name), // :528
                    option.Count, true, allowIconRetry);
            }

            for (int i = rowTotal; i < handle.FoodRows.Count; i++)
            {
                UguiPetCareFoodRowHandle row = handle.FoodRows[i];
                if (row.Root != null && row.Root.activeSelf)
                {
                    row.Root.SetActive(false);
                }
            }

            if (allowIconRetry)
            {
                handle.NextFoodIconRetryAt = now + 0.5f;
            }

            this.SetUguiScrollContentHeight(handle.FoodListContent,
                Mathf.Max(1f, rowTotal * UguiPetCareFoodRowStep));
        }

        private void ResetUguiFeaturesPetCareFoodListScroll(UguiShellFeaturesPetCareHandle handle)
        {
            try
            {
                if (handle.FoodListContent != null)
                {
                    RectTransform rt = handle.FoodListContent.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 0f);
                    }
                }
            }
            catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Pooled FAVORITE FOODS rows — plain read-only text cells inside the nested ScrollRect
        // (no icons, no click handlers — :4555-4567), wrapped like the source's cellStyle.
        // ----------------------------------------------------------------------------------------

        private UguiPetCareFavRowHandle CreateUguiFeaturesPetCareFavRow(
            UguiShellFeaturesPetCareHandle handle, int index)
        {
            UguiPetCareFavRowHandle row = new UguiPetCareFavRowHandle();
            float innerW = handle.ContentWidth - 16f - 24f - 22f;
            float colName = 92f;                              // :4531
            float colLike = (innerW - colName) * 0.52f;       // :4533
            float colDislike = innerW - colName - colLike;    // :4534
            float cellH = PetFeedFavoriteUiRowHeight - 4f;    // :4564-4566

            GameObject root = this.CreateUguiGo("Fav" + index, handle.FavListContent);
            PlaceUguiTopLeft(root, 0f, index * PetFeedFavoriteUiRowHeight, innerW, PetFeedFavoriteUiRowHeight);

            row.NameLabel = this.CreateUguiLabel(root.transform, "Name", "", 11f, this.UguiKitTextColor(), false);
            this.TrySetUguiLabelWrapped(row.NameLabel);
            PlaceUguiTopLeft(row.NameLabel, 0f, 0f, colName - 4f, cellH);
            row.LikeLabel = this.CreateUguiLabel(root.transform, "Like", "", 11f, this.UguiKitTextColor(), false);
            this.TrySetUguiLabelWrapped(row.LikeLabel);
            PlaceUguiTopLeft(row.LikeLabel, colName, 0f, colLike - 4f, cellH);
            row.DislikeLabel = this.CreateUguiLabel(root.transform, "Dislike", "", 11f, this.UguiKitTextColor(), false);
            this.TrySetUguiLabelWrapped(row.DislikeLabel);
            PlaceUguiTopLeft(row.DislikeLabel, colName + colLike, 0f, colDislike - 4f, cellH);

            row.Root = root;
            return row;
        }

        private void SyncUguiFeaturesPetCareFavRows(UguiShellFeaturesPetCareHandle handle)
        {
            int count = this.petFeedFavoriteUiRows.Count;
            for (int i = 0; i < count; i++)
            {
                if (i >= handle.FavRows.Count)
                {
                    handle.FavRows.Add(this.CreateUguiFeaturesPetCareFavRow(handle, i));
                }
                UguiPetCareFavRowHandle row = handle.FavRows[i];
                PetFeedFavoriteUiRow src = this.petFeedFavoriteUiRows[i];
                if (src == null)
                {
                    if (row.Root != null && row.Root.activeSelf)
                    {
                        row.Root.SetActive(false); // :4557-4561 — slot kept
                    }
                    continue;
                }
                if (row.Root != null && !row.Root.activeSelf)
                {
                    row.Root.SetActive(true);
                }
                this.SyncUguiSelfLabelText(row.NameLabel, ref row.NameShown, src.Name ?? "?");           // :4564
                this.SyncUguiSelfLabelText(row.LikeLabel, ref row.LikeShown, src.Like ?? "(none)");      // :4565
                this.SyncUguiSelfLabelText(row.DislikeLabel, ref row.DislikeShown, src.Dislike ?? "(none)"); // :4566
            }

            for (int i = count; i < handle.FavRows.Count; i++)
            {
                UguiPetCareFavRowHandle row = handle.FavRows[i];
                if (row.Root != null && row.Root.activeSelf)
                {
                    row.Root.SetActive(false);
                }
            }

            if (count > 0)
            {
                this.SetUguiScrollContentHeight(handle.FavListContent,
                    count * PetFeedFavoriteUiRowHeight); // :4552 — full list, window scrolls
            }
        }

        // ----------------------------------------------------------------------------------------
        // Food header sync (caption / icon / arrow) — LIVE per gated frame; the selection moves
        // from either surface and from scans' default-select.
        // ----------------------------------------------------------------------------------------

        private void SyncUguiFeaturesPetCareFoodHeader(UguiShellFeaturesPetCareHandle handle, bool allowIconRetry)
        {
            this.SyncUguiSelfLabelText(handle.FoodHeaderValue, ref handle.FoodHeaderValueShown,
                this.GetPetFeedSelectedFoodLabel()); // :440
            this.SyncUguiSelfLabelText(handle.FoodArrow, ref handle.FoodArrowShown,
                this.petFeedFoodDropdownOpen ? "^" : "v"); // :441

            // :433-439 — icon only while a real food with a cached texture is selected; the value
            // label's left inset flips 12↔34 (the source's +24 shift).
            int selectedId = this.petFeedSelectedFoodStaticId;
            bool wantLookup = selectedId > 0
                && (selectedId != handle.FoodHeaderIconShownId
                    || (!handle.FoodHeaderIconGo.activeSelf && allowIconRetry));
            if (selectedId <= 0)
            {
                if (handle.FoodHeaderIconShownId != 0 || handle.FoodHeaderIconGo.activeSelf)
                {
                    handle.FoodHeaderIconShownId = 0;
                    SetUguiGoActive(handle.FoodHeaderIconGo, false);
                    this.SetUguiFeaturesPetCareHeaderValueInset(handle, false);
                }
            }
            else if (wantLookup)
            {
                Texture2D tex;
                bool has = this.TryGetPetFeedFoodIconTexture(selectedId, out tex) && tex != null;
                handle.FoodHeaderIconShownId = selectedId;
                if (has && handle.FoodHeaderIcon != null)
                {
                    try { handle.FoodHeaderIcon.texture = tex; } catch { }
                }
                SetUguiGoActive(handle.FoodHeaderIconGo, has);
                this.SetUguiFeaturesPetCareHeaderValueInset(handle, has);
            }
        }

        private void SetUguiFeaturesPetCareHeaderValueInset(UguiShellFeaturesPetCareHandle handle, bool iconVisible)
        {
            try
            {
                RectTransform rt = handle.FoodHeaderValueRt;
                if (rt != null)
                {
                    float left = iconVisible ? 34f : 12f;
                    if (Mathf.Abs(rt.offsetMin.x - left) > 0.5f)
                    {
                        rt.offsetMin = new Vector2(left, rt.offsetMin.y);
                    }
                }
            }
            catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellFeaturesPetCareOnUpdate()
        {
            UguiShellFeaturesPetCareHandle handle = this.uguiShellFeaturesPetCare;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellFeaturesSubTabActive(UguiShellFeaturesPetCareSubIndex))
            {
                return;
            }

            try
            {
                // Toggle re-syncs (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.AutoCatToggle, this.petPlayAutoCatEnabled);
                this.SyncUguiToggleFromField(handle.AutoDogToggle, this.petPlayAutoDogEnabled);
                this.SyncUguiToggleFromField(handle.AutoWashToggle, this.petPlayAutoWashEnabled);
                this.SyncUguiToggleFromField(handle.TrainLoopToggle, this.petCareTrainLoopEnabled);
                this.SyncUguiToggleFromField(handle.SkipFiveStarToggle, this.petFeedSkipFiveStarFood);

                // MY PETS live pieces: status label (:284) + the caption swap (:286).
                this.SyncUguiSelfLabelText(handle.PetsStatusLabel, ref handle.PetsStatusShown,
                    this.petCareListStatus ?? string.Empty);
                string refreshText = this.petCareListVisible ? "Refresh" : "Show My Pets";
                if (!string.Equals(refreshText, handle.RefreshShown, StringComparison.Ordinal))
                {
                    handle.RefreshShown = refreshText;
                    this.SetUguiButtonLabel(handle.RefreshButton, refreshText);
                }

                // :309 — the source's own per-repaint refresh tick, ONLY while the list is
                // visible (self-throttled internally). Row bind runs unconditionally so hiding
                // the list (rowCount 0) deactivates every pooled row.
                if (this.petCareListVisible)
                {
                    this.TickPetCareAutoStatsRefresh();
                }
                this.SyncUguiFeaturesPetCarePetRows(handle);

                // PET FOOD header + scan gate (:433-449) — time-dependent, every gated frame.
                float now = Time.realtimeSinceStartup;
                bool allowIconRetry = now >= handle.NextFoodIconRetryAt; // shared 2Hz icon timer
                this.SyncUguiFeaturesPetCareFoodHeader(handle, allowIconRetry);
                bool canScanPetFood = !this.petFeedFoodScanInProgress
                    && now >= this.petFeedNextFoodScanAllowedAt;
                this.SetUguiButtonInteractable(handle.ScanButton, canScanPetFood);
                string scanText = canScanPetFood ? "Scan Food" : "Wait...";
                if (!string.Equals(scanText, handle.ScanShown, StringComparison.Ordinal))
                {
                    handle.ScanShown = scanText;
                    this.SetUguiButtonLabel(handle.ScanButton, scanText);
                }

                if (this.petFeedFoodDropdownOpen)
                {
                    // Search field poll pair (Mass Cook idiom): a missed onValueChanged lands via
                    // the first branch; an IMGUI-twin edit of the shared field via the second.
                    InputField searchField = handle.FoodSearchField;
                    if (searchField != null)
                    {
                        string uiText = searchField.text ?? string.Empty;
                        if (!string.Equals(uiText, handle.FoodSearchApplied, StringComparison.Ordinal))
                        {
                            this.ApplyUguiFeaturesPetCareFoodSearch(handle, uiText);
                        }
                        else
                        {
                            string fieldText = this.petFeedFoodSearchText ?? string.Empty;
                            if (!string.Equals(fieldText, handle.FoodSearchApplied, StringComparison.Ordinal))
                            {
                                handle.FoodSearchApplied = fieldText;
                                try { searchField.SetTextWithoutNotify(fieldText); } catch { }
                                // External edit — the IMGUI twin's own cascade already reset its
                                // scroll index; just snap our list to the top the same way.
                                this.ResetUguiFeaturesPetCareFoodListScroll(handle);
                            }
                        }
                        // Placeholder (:470-481): visible only while the field is empty.
                        SetUguiGoActive(handle.FoodSearchPlaceholder,
                            string.IsNullOrEmpty(searchField.text));
                    }

                    // Full-list row bind every gated frame while open — parity with :488's
                    // per-repaint re-filter; also catches scans and cross-surface selection moves.
                    this.SyncUguiFeaturesPetCareFoodRows(handle);
                }

                // FEEDING busy gate (:596-607) — time-dependent, both buttons together.
                bool petFeedBusy = this.petFeedAllCoroutine != null || now < this.petFeedAllBusyUntil;
                this.SetUguiButtonInteractable(handle.FeedCatsButton, !petFeedBusy);
                this.SetUguiButtonInteractable(handle.FeedDogsButton, !petFeedBusy);

                // FAVORITE FOODS rows — cheap text diffs; presence/height via the signature.
                this.SyncUguiFeaturesPetCareFavRows(handle);

                // Conditional-layout signature (list visibility, dropdown, row counts).
                this.RefreshUguiFeaturesPetCareLayout(handle);
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Features Pet Care content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order).
        // NO SaveKeybinds anywhere: the source files contain ZERO persistence (grep-verified,
        // file header) — every write below is the same plain in-memory field write the IMGUI
        // twin performs.
        // ----------------------------------------------------------------------------------------

        // :246-250 — flag + PetPlayLog (a log line, not a toast). The equal-guard is the UGUI
        // analog of IMGUI's prev-vs-new check.
        private void OnUguiFeaturesPetCareAutoCatToggled(bool value)
        {
            if (value == this.petPlayAutoCatEnabled)
            {
                return;
            }
            this.petPlayAutoCatEnabled = value;
            this.PetPlayLog("Cat play " + (value ? "enabled" : "disabled"));
        }

        // :253-258.
        private void OnUguiFeaturesPetCareAutoDogToggled(bool value)
        {
            if (value == this.petPlayAutoDogEnabled)
            {
                return;
            }
            this.petPlayAutoDogEnabled = value;
            this.PetPlayLog("Dog train " + (value ? "enabled" : "disabled"));
        }

        // :261-266.
        private void OnUguiFeaturesPetCareAutoWashToggled(bool value)
        {
            if (value == this.petPlayAutoWashEnabled)
            {
                return;
            }
            this.petPlayAutoWashEnabled = value;
            this.PetPlayLog("Pet wash " + (value ? "enabled" : "disabled"));
        }

        // :286-290 — sets visible TRUE (never toggles off) + refresh; the list's appearance
        // lands via the layout signature (+ an immediate refresh for click responsiveness).
        private void OnUguiFeaturesPetCareRefreshClicked()
        {
            try
            {
                this.petCareListVisible = true;
                this.RefreshPetCareList();
                UguiShellFeaturesPetCareHandle handle = this.uguiShellFeaturesPetCare;
                if (handle != null && handle.Root != null)
                {
                    this.SyncUguiFeaturesPetCarePetRows(handle);
                    this.RefreshUguiFeaturesPetCareLayout(handle);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Pet Care refresh error: " + ex.Message);
            }
        }

        // :298-305 — on DISABLE only, StopPetCareTrainLoop; NO save either direction.
        private void OnUguiFeaturesPetCareTrainLoopToggled(bool value)
        {
            if (value == this.petCareTrainLoopEnabled)
            {
                return;
            }
            this.petCareTrainLoopEnabled = value;
            if (!value)
            {
                try { this.StopPetCareTrainLoop("Train loop switched off."); }
                catch (Exception ex)
                {
                    ModLogger.Msg("[UguiShell] Pet Care train loop stop error: " + ex.Message);
                }
            }
        }

        // :358-361 — the active-session tail's single button.
        private void OnUguiFeaturesPetCarePetStopClicked(UguiPetCarePetRowHandle row)
        {
            try
            {
                this.StopPetCareActiveSession();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Pet Care stop error: " + ex.Message);
            }
        }

        // :366-368 — acts on the row's live-bound entry (Research idiom).
        private void OnUguiFeaturesPetCarePetPlayClicked(UguiPetCarePetRowHandle row)
        {
            if (row == null || row.BoundEntry == null)
            {
                return;
            }
            try
            {
                this.OnPetCarePlayClicked(row.BoundEntry);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Pet Care play error: " + ex.Message);
            }
        }

        // :370-372.
        private void OnUguiFeaturesPetCarePetWashClicked(UguiPetCarePetRowHandle row)
        {
            if (row == null || row.BoundEntry == null)
            {
                return;
            }
            try
            {
                this.OnPetCareWashClicked(row.BoundEntry);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Pet Care wash error: " + ex.Message);
            }
        }

        // :427-430 — the SHARED open flag; relayout on the click frame.
        private void OnUguiFeaturesPetCareFoodHeaderClicked()
        {
            try
            {
                this.petFeedFoodDropdownOpen = !this.petFeedFoodDropdownOpen;
                UguiShellFeaturesPetCareHandle handle = this.uguiShellFeaturesPetCare;
                if (handle == null || handle.Root == null)
                {
                    return;
                }
                this.SyncUguiSelfLabelText(handle.FoodArrow, ref handle.FoodArrowShown,
                    this.petFeedFoodDropdownOpen ? "^" : "v");
                if (this.petFeedFoodDropdownOpen)
                {
                    try { this.SyncUguiFeaturesPetCareFoodRows(handle); } catch { }
                }
                this.RefreshUguiFeaturesPetCareLayout(handle);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Pet Care food dropdown error: " + ex.Message);
            }
        }

        // :445-448 — the backend gates, toasts, closes the dropdown and default-selects itself;
        // the click-frame layout refresh catches the close.
        private void OnUguiFeaturesPetCareScanFoodClicked()
        {
            try
            {
                this.RefreshPetFeedFoodOptions();
                UguiShellFeaturesPetCareHandle handle = this.uguiShellFeaturesPetCare;
                if (handle != null && handle.Root != null)
                {
                    this.RefreshUguiFeaturesPetCareLayout(handle);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Pet Care scan food error: " + ex.Message);
            }
        }

        // :469-486 — the search cascade: shared text + the source's own scroll-state resets
        // (petFeedFoodDropdownScrollIndex/petFeedFoodScrollbarDragging FIELD writes keep the
        // IMGUI twin exactly as if the search was typed there — design-decision block), then
        // snap OUR ScrollRect to the top and rebind (per-keystroke live filter).
        private void ApplyUguiFeaturesPetCareFoodSearch(UguiShellFeaturesPetCareHandle handle, string text)
        {
            handle.FoodSearchApplied = text;
            this.petFeedFoodSearchText = text;
            this.petFeedFoodDropdownScrollIndex = 0;   // :484 — field write only, never read here
            // (:485's petFeedFoodScrollbarDragging reset is gone — the IMGUI scrollbar died with
            // the IMGUI Pet Care tab in Phase 5.)
            this.ResetUguiFeaturesPetCareFoodListScroll(handle);
            if (this.petFeedFoodDropdownOpen)
            {
                this.SyncUguiFeaturesPetCareFoodRows(handle);
            }
        }

        private void OnUguiFeaturesPetCareFoodSearchChanged(string value)
        {
            UguiShellFeaturesPetCareHandle handle = this.uguiShellFeaturesPetCare;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                string text = value ?? string.Empty;
                if (string.Equals(text, handle.FoodSearchApplied, StringComparison.Ordinal))
                {
                    return; // the gated poll already applied it (or a redundant event)
                }
                this.ApplyUguiFeaturesPetCareFoodSearch(handle, text);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Pet Care food search error: " + ex.Message);
            }
        }

        // :500-503 / :523-526 — SelectPetFeedFood(StaticId, RAW Name); the backend closes the
        // shared dropdown flag itself (:2471), the click-frame refresh applies it.
        private void OnUguiFeaturesPetCareFoodRowClicked(UguiPetCareFoodRowHandle row)
        {
            if (row == null || row.BoundStaticId == int.MinValue)
            {
                return;
            }
            try
            {
                this.SelectPetFeedFood(row.BoundStaticId, row.BoundName);
                UguiShellFeaturesPetCareHandle handle = this.uguiShellFeaturesPetCare;
                if (handle != null && handle.Root != null)
                {
                    this.RefreshUguiFeaturesPetCareLayout(handle);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Pet Care food pick error: " + ex.Message);
            }
        }

        // :598-601 / :603-606 — the backend re-checks its own busy state and toasts.
        private void OnUguiFeaturesPetCareFeedCatsClicked()
        {
            try
            {
                this.StartPetFeedAll(false);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Pet Care feed cats error: " + ex.Message);
            }
        }

        private void OnUguiFeaturesPetCareFeedDogsClicked()
        {
            try
            {
                this.StartPetFeedAll(true);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Pet Care feed dogs error: " + ex.Message);
            }
        }

        // :614-617 — flag-only: no log, no toast, no save (verified).
        private void OnUguiFeaturesPetCareSkipFiveStarToggled(bool value)
        {
            if (value == this.petFeedSkipFiveStarFood)
            {
                return;
            }
            this.petFeedSkipFiveStarFood = value;
        }

        // :619-622 — populates petFeedFavoriteUiRows (+ logs/toasts internally); the table's
        // appearance lands via the layout signature.
        private void OnUguiFeaturesPetCareShowFavoritesClicked()
        {
            try
            {
                this.LogNearbyPetFavoriteFoods();
                UguiShellFeaturesPetCareHandle handle = this.uguiShellFeaturesPetCare;
                if (handle != null && handle.Root != null)
                {
                    this.SyncUguiFeaturesPetCareFavRows(handle);
                    this.RefreshUguiFeaturesPetCareLayout(handle);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Pet Care favorites error: " + ex.Message);
            }
        }
    }
}
