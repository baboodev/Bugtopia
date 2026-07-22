using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, New Features round 4 of 8 (migration plan item 12): the
    // PICTURES sub-tab — DrawPicturesTab (PicturesDecryptFeature.cs:75-237), newFeaturesSubTab
    // == 3 (AnimalCareFeature.cs:44-46 dispatcher). The remaining five subs (Daily Quests,
    // Homeland Farm, Ice Skating, Extra, Sea Clean — display 1, 2, 4, 5, 7) stay on the shell
    // placeholder until their own rounds ship.
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawer and every backend method it calls stay fully functional and untouched —
    //    this file only READS the same fields and CALLS the same action methods (all directly on
    //    HeartopiaComplete via the PicturesDecryptFeature.cs / DrawUploadFeature.cs partials;
    //    ZERO backend interop additions this round: 2 coroutine refs, 2 slider fields, 1 status
    //    string, 1 list, 1 dirty flag, path/manifest/refresh helpers + the 5 action methods).
    //  - Wiring is by STATIC display-position index (UguiShellNewFeaturesTabIndex = 3 +
    //    UguiShellPicturesSubIndex = 3, declared with their siblings in UguiShellTabIndices.cs),
    //    never label comparison. The processor gates on the SAME
    //    IsUguiShellNewFeaturesSubTabActive function Animal Care's round established.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // Source nuances verified against the drawer, replayed exactly:
    //  - LOCALIZATION: this feature uses dot-namespaced translation KEYS (pictures.title,
    //    pictures.paths, pictures.draw_hint, pictures.decrypt_all, pictures.encrypt_changed,
    //    pictures.scan_changed, pictures.changed_count, pictures.manifest_missing) — passed
    //    through this.L/this.LF untouched, never substituted with literal English. FOUR strings
    //    are deliberately UNLOCALIZED source literals and stay that way: the "Extract open
    //    drawing" / "Upload drawing.png" buttons (:175/:182) and the two slider label prefixes
    //    "Upload chunk budget: {N} runs" / "Upload chunk delay: {N:0.00}s" (:193/:201).
    //  - TWO INDEPENDENT BUSY FLAGS with the source's exact GUI.enabled scoping (:145-187):
    //    busy = picturesTaskCoroutine != null gates decrypt_all, encrypt_changed, scan_changed
    //    AND "Extract open drawing" (all four sit inside the :147 GUI.enabled = !busy scope);
    //    "Upload drawing.png" alone gets !busy && !chunkSendBusy (chunkSendBusy =
    //    drawUploadChunkCoroutine != null, set at :181 AFTER Extract is drawn). The sliders are
    //    drawn after :187 GUI.enabled = true — NEVER gated. Both are live coroutine-reference
    //    checks recomputed EVERY gated frame (Animal Care's live-gate idiom, null-check-only
    //    flavor); SetUguiButtonInteractable self-diffs so the per-frame call is a cheap compare.
    //  - Buttons: decrypt/encrypt use themePrimaryButtonStyle (:148/:153) → kit Primary tier;
    //    scan/extract/upload use plain GUI.skin.button (:160/:175/:182) → kit Secondary tier
    //    (the established mapping, UguiShell.cs:443).
    //  - Sliders: budget is UI_DrawAccentIntSlider (UiKit.cs:562-566 — whole-number track +
    //    Clamp(RoundToInt)) → wholeNumbers=true and the same clamp in the handler, range
    //    [DrawUploadRunsPerChunkMin..Max] = [32..256]; delay is a plain accent slider whose
    //    committed value snaps to the nearest 0.05s — Clamp(Round(raw*20)/20, min, max)
    //    (:207-210, the migration's FOURTH distinct rounding granularity), range
    //    [DrawUploadChunkDelayMin..Max] = [0.05..1]. Both handlers write the field directly —
    //    no method call, no save, no notification (verified: plain assignments in the source).
    //    Per-frame epsilon re-syncs pull the handles onto the snapped fields (sprint idiom).
    //
    // NEW mechanic 1 — TEXT-DRIVEN heights (Phase 2e toast spike, reused not reimplemented):
    //    the source computes pathsH/hintH per frame via bodyStyle.CalcHeight(text, innerW)+4
    //    (:117/:122) and folds them into the card height. Mirrored with the spike's proven
    //    TMP measurement (HeartopiaComplete.UguiPoc.cs:566-646): set the text FIRST, then
    //    measure via the STRING overload — here the width-constrained form
    //    GetPreferredValues(text, innerW, 0f) (TMP_Text.cs:2948, real RVA; with auto-sizing off
    //    the height argument is unused, width is the wrap constraint), same Ceil(h)+4 formula,
    //    same sanity gate (reject non-finite/non-positive/absurd → keep previous height, never
    //    garbage). The spike's build-time caveat applies VERBATIM here: this content builds as a
    //    NON-ACTIVE sub-tab (display sub 0 is the default), so the TMP components have never
    //    Awoken at first measure — on a rejected measure the fallback height stands and a
    //    MeasureOk=false flag makes the slow tick re-measure once the tab is actually visible
    //    (component alive by then); nothing latches broken.
    // NEW mechanic 2 — NESTED scroll region (:219-231): the changed-files list is a real kit
    //    ScrollRect INSIDE the card, inside the tab's outer ScrollRect — chosen over a plain
    //    RectMask2D container because rows beyond the 6 visible must stay reachable (the IMGUI
    //    twin scrolls them; a clipped-only container would strand them). Nesting is safe here
    //    because UGUI event routing is region-exclusive: the pointer's raycast hits the INNER
    //    viewport first, and both IScrollHandler (wheel) and IBeginDragHandler (drag) resolve to
    //    the NEAREST handling ancestor — the inner ScrollRect — so wheel/drag over the list
    //    drives ONLY the list, and everywhere else drives ONLY the outer scroll; neither gesture
    //    ever double-scrolls. That matches IMGUI's own nested BeginScrollView semantics (the
    //    inner region consumes wheel events over its rect). Both kit backgrounds are cleared to
    //    Color.clear (Logging idiom — alpha-0 images still raycast, so the region keeps its
    //    events); the source draws no box behind the list either. Visible height =
    //    min(count,6)*18+4, inner content height = count*18 (:119-121/:222) — when count <= 6
    //    the inner region simply cannot scroll. Only shown (and only taking card height) when
    //    count > 0, exactly like the source. Row labels are POOLED: grown on demand, rebound by
    //    cached-string diff, deactivated (not destroyed) on shrink. The inner scroll position is
    //    surface-local state (not mirrored onto picturesChangedScrollPos), the same independence
    //    every round's outer scroll has.
    //
    // Refresh/sync model — the ONE deliberate divergence, documented: the IMGUI drawer reloads
    // the manifest JSON from disk EVERY frame (:104) just to derive hasManifest, and lazily runs
    // the expensive SHA-hashing changed-list refresh when picturesChangedListDirty is set
    // (:105-109; the decrypt routine sets the flag at :452, the encrypt routine rewrites the
    // list directly at :493/:540 WITHOUT the flag). Per-frame disk IO is not portable to a
    // persistent UGUI surface, so this mirror keeps a cached hasManifest snapshot instead,
    // refreshed at build (one manifest load, no hashing), on the dirty-flag path, and on Scan —
    // every mutation the feature itself performs lands in one of those. The 0.5s slow tick
    // (Sand Sculpture's box-lines cadence) owns: the :105-109 dirty lazy-refresh (fresh manifest
    // load + RefreshPicturesChangedList + clear flag — the SHA pass runs on the main thread
    // exactly like the IMGUI twin's, once per dirty event, first fired the moment the sub-tab
    // becomes visible since NextSlowSyncAt starts at 0), the paths re-resolve (reflection-backed
    // TryGetScreenCaptureRootPath at 2Hz; text diff → re-measure → relayout), the measure
    // retries, the changed-count header rebuild (live count + cached hasManifest), and the
    // per-row text diffs that pick up the encrypt routine's flagless background rewrites.
    // Per frame: the 5 busy gates, the 2 slider epsilon re-syncs + their value labels (drag
    // responsiveness, Sand Sculpture's DelayLabel precedent), and the status line
    // (IsNullOrWhiteSpace(picturesLastStatus) ? "Idle." : it — :233, alloc-free until changed).
    //
    // "Scan changed" reproduces the source's FULL :160-170 sequence, not just "call scan":
    // dirty=true → TryLoadPicturesManifest(destPath) → RefreshPicturesChangedList → dirty=false
    // → recompute hasManifest → picturesLastStatus = changed_count | manifest_missing (with
    // destPath resolved fresh via the same two helpers the drawer's frame uses, :102-103);
    // then the UGUI-side bookkeeping (snapshot, rows, header, relayout) — mirror state only.
    //
    // Layout replays the source's cursor math verbatim (content top margin 8 standing in for
    // startY; ONE themed card — GUI.Box(themePanelStyle)+DrawCardOutline (:130-131) → the kit
    // settings-panel chrome with an EMPTY header slot, Animal Care's action-card precedent,
    // because the source's title is its OWN label in the TEXT color: bold 12 uiText (:85-92) —
    // the Sand Sculpture not-the-header-color precedent — as are both body texts, plain 11
    // uiText (:94-100, full alpha). Card 520→panelW; the 248-wide button pair + its 504 rowPairW
    // span pad..right-edge in the source (16+504 = 520) — a full-width role, so
    // btnW = (panelW-24)/2 keeps that span at any shell width):
    //   card y=8, card-local: title (16,10,innerW,22); paths (16,32,innerW,pathsH);
    //   hint +rowGap below; then rows of 32-high buttons at +42 steps (decrypt|encrypt pair,
    //   scan full rowPairW, extract|upload pair); two slider rows (label 220x20 at innerX,
    //   slider at innerX+228, innerW-228 wide, kit-placed 18 high at +1 for the 18px handle;
    //   +30 steps); changed header (innerW x20); [list (12,·,panelW-24,scrollH); +8] when
    //   count>0; status (innerW x56). cardH = the source's :123-127 formula VERBATIM —
    //   including its quirk of charging scrollH+8 unconditionally (8px of extra card padding
    //   when the list is empty, status drawn at the un-padded cursor: :230 only advances inside
    //   the count>0 branch). Content height = 8 + cardH + 24 (:236 return, dispatcher adds 0).
    // RelayoutUguiShellNewFeaturesPictures owns every position/height; it re-runs when the
    // layout-driving values (pathsH, hintH, list count) change — the signature idiom, extended
    // per this round's brief to TEXT-DRIVEN heights, so it stores the exact values it laid out
    // with. Per-frame sync disabled after 3 consecutive errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellNewFeaturesPicturesHandle
        {
            public GameObject Root;
            public Transform OuterContent;        // outer scroll content (card height feeds it)
            public GameObject Card;               // the single themed section card

            // Measured word-wrapped labels (heights drive the whole layout below them)
            public GameObject PathsLabel;
            public string PathsShown;
            public float PathsH;                  // Ceil(measured)+4, source :117 formula
            public bool PathsMeasureOk;           // false → slow tick re-measures (spike caveat)
            public GameObject HintLabel;
            public float HintH;
            public bool HintMeasureOk;

            // Buttons — 2 primary + 3 secondary, busy-gated per the file header's exact scoping
            public GameObject DecryptButton;      // !busy
            public GameObject EncryptButton;      // !busy
            public GameObject ScanButton;         // !busy (inside the :147 scope!)
            public GameObject ExtractButton;      // !busy (drawn BEFORE :181 re-scopes)
            public GameObject UploadButton;       // !busy && !chunkSendBusy

            // Slider rows (unlocalized label prefixes — source literals)
            public GameObject BudgetLabel;
            public string BudgetShown;
            public Slider BudgetSlider;           // wholeNumbers, [32..256]
            public GameObject DelayLabel;
            public string DelayShown;
            public Slider DelaySlider;            // [0.05..1], commit snaps to 0.05s

            // Changed-files header + nested list + status
            public GameObject HeaderLabel;
            public string HeaderShown;
            public GameObject ListScroll;         // the INNER kit ScrollRect (file header)
            public Transform ListContent;
            public readonly List<GameObject> RowLabels = new List<GameObject>();
            public readonly List<string> RowShown = new List<string>();
            public GameObject StatusLabel;
            public string StatusShown;

            // Cached manifest snapshot (refresh policy in the file header — never per-frame IO)
            public bool HasManifest;

            // Geometry cached at build (positions derive from these in the relayout)
            public float PanelW;
            public float InnerW;
            public float BtnW;

            // Layout signature — the exact values the last relayout used (text-driven heights +
            // list count; a mismatch re-runs the relayout)
            public float LayoutPathsH = -1f;
            public float LayoutHintH = -1f;
            public int LayoutCount = -1;

            public float NextSlowSyncAt;          // 0.5s tick (starts 0 → fires on first frame)
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellNewFeaturesPicturesHandle uguiShellNewFeaturesPictures;

        // ----------------------------------------------------------------------------------------
        // Busy conditions — the EXACT source expressions (:145/:180), recomputed on every call
        // (live coroutine references change from background activity; never cache the result)
        // ----------------------------------------------------------------------------------------

        private bool IsUguiPicturesTaskBusy()
        {
            return this.picturesTaskCoroutine != null;
        }

        private bool IsUguiPicturesChunkSendBusy()
        {
            return this.drawUploadChunkCoroutine != null;
        }

        // ----------------------------------------------------------------------------------------
        // Live text builders (shared by builder + processor so both surfaces render one truth)
        // ----------------------------------------------------------------------------------------

        // :193 — UNLOCALIZED source literal, exact concatenation.
        private string BuildUguiPicturesBudgetText()
        {
            return "Upload chunk budget: " + this.drawUploadRunsPerChunk + " runs";
        }

        // :201 — UNLOCALIZED source literal, exact "0.00" format.
        private string BuildUguiPicturesDelayText()
        {
            return "Upload chunk delay: " + this.drawUploadChunkDelaySeconds.ToString("0.00") + "s";
        }

        // :213-215 — live list count + the cached manifest snapshot (file header refresh policy).
        private string BuildUguiPicturesHeaderText(bool hasManifest)
        {
            return hasManifest
                ? this.LF("pictures.changed_count", this.picturesChangedRelativePaths.Count)
                : this.L("pictures.manifest_missing");
        }

        // :233 — alloc-free until the underlying status actually changes.
        private string BuildUguiPicturesStatusText()
        {
            return string.IsNullOrWhiteSpace(this.picturesLastStatus) ? "Idle." : this.picturesLastStatus;
        }

        // ----------------------------------------------------------------------------------------
        // Wrapped-text measurement — the Phase 2e toast spike's mechanism (UguiPoc.cs:575-646),
        // width-constrained flavor: GetPreferredValues(text, width, 0f) wraps at width (TMP
        // ignores the height argument with auto-sizing off). Same Ceil+4 as the source's
        // CalcHeight formula (:117/:122), same sanity gate as the spike (non-finite/absurd →
        // keep the fallback, never garbage). ok=false lets the slow tick retry while the
        // component has never Awoken (built on a non-active sub-tab — spike build-time caveat).
        // ----------------------------------------------------------------------------------------

        private float MeasureUguiPicturesWrappedHeight(GameObject label, string text, float width,
            float fallback, out bool ok)
        {
            ok = false;
            try
            {
                if (label == null)
                {
                    return fallback;
                }
                float h = -1f;
                TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    h = tmp.GetPreferredValues(text, width, 0f).y;
                }
                else
                {
                    // Legacy fallback (only reachable if the kit's TMP creation fell back):
                    // preferredHeight wraps at the label's own rect width — set before measuring.
                    Text legacy = label.GetComponent<Text>();
                    if (legacy != null)
                    {
                        h = legacy.preferredHeight;
                    }
                }

                bool sane = h > 0f && h < 600f && !float.IsNaN(h) && !float.IsInfinity(h);
                if (sane)
                {
                    ok = true;
                    return Mathf.Ceil(h) + 4f;
                }
            }
            catch { }
            return fallback;
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawPicturesTab: one themed card carrying everything, in a transparent
        // outer scroll view. Controls are built ONCE at throwaway positions; the relayout (which
        // ALWAYS runs at the end of the builder) owns every position/height. Deliberately does
        // NOT run the dirty lazy-refresh here — the SHA pass would hitch the whole shell build
        // for a tab that may never be opened; the slow tick pays it on first activation instead,
        // which is exactly when the IMGUI twin would have paid it (its first frame of this tab).
        // Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellNewFeaturesPicturesContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellNewFeaturesPictures = null;

            UguiShellNewFeaturesPicturesHandle handle = new UguiShellNewFeaturesPicturesHandle();
            GameObject block = this.CreateUguiGo("NewFeaturesPicturesContent", parent);
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

            float contentWidth = w - 22f;      // viewport insets: 4 left + 18 right
            float panelW = contentWidth - 16f; // card at x=8, 8px right margin
            handle.OuterContent = scrollContent;
            handle.PanelW = panelW;
            handle.InnerW = panelW - 32f;                    // source innerW = width - pad*2 (:115)
            handle.BtnW = Mathf.Floor((panelW - 24f) / 2f);  // pair spans pad..right edge (file header)

            // Frame-start resolves the drawer performs at :102-104 — done ONCE here (one manifest
            // JSON load for the initial hasManifest snapshot; NO changed-list hashing, see above).
            string sourcePath = this.TryGetScreenCaptureRootPath();
            string destPath = this.GetScreenCaptureDecryptedRootPath(sourcePath);
            PicturesManifest manifest = this.TryLoadPicturesManifest(destPath);
            handle.HasManifest = manifest != null && manifest.Files != null && manifest.Files.Count > 0;

            // The source's two text roles (:85-100): title = bold 12 uiText (NOT the header
            // color — Sand Sculpture precedent), body = plain 11 uiText, both full alpha.
            Color textColor = this.UguiKitTextColor();

            // -------- The one themed card (:129-131 — themePanelStyle box + card outline).
            // Kit settings-panel chrome with an EMPTY header slot (Animal Care action-card
            // precedent); the title below is the source's own label. Height owned by relayout.
            GameObject card = this.CreateUguiSettingsMainPanel(scrollContent, "PicturesCard", string.Empty);
            handle.Card = card;

            // -------- Title (:136 — card-local 16,10, innerW x 22) --------
            GameObject title = this.CreateUguiLabel(card.transform, "Title",
                this.L("pictures.title"), 12f, textColor, false);
            this.TrySetUguiLabelBold(title);
            PlaceUguiTopLeft(title, 16f, 10f, handle.InnerW, 22f);

            // -------- Measured paths label (:116-117/:139 — LF with the two resolved paths) ----
            handle.PathsShown = this.LF("pictures.paths", sourcePath, destPath);
            handle.PathsLabel = this.CreateUguiLabel(card.transform, "PathsLabel",
                handle.PathsShown, 11f, textColor, false);
            this.TrySetUguiLabelWrapped(handle.PathsLabel);
            PlaceUguiTopLeft(handle.PathsLabel, 16f, 32f, handle.InnerW, 36f);

            // -------- Measured hint label (:122/:142) --------
            string hintText = this.L("pictures.draw_hint");
            handle.HintLabel = this.CreateUguiLabel(card.transform, "HintLabel",
                hintText, 11f, textColor, false);
            this.TrySetUguiLabelWrapped(handle.HintLabel);
            PlaceUguiTopLeft(handle.HintLabel, 16f, 72f, handle.InnerW, 36f);

            // -------- Button row 1: decrypt | encrypt (:148-156 — PRIMARY tier, !busy) --------
            handle.DecryptButton = this.CreateUguiPrimaryButton(card.transform, "DecryptAllButton",
                this.L("pictures.decrypt_all"), new System.Action(this.OnUguiPicturesDecryptAllClicked));
            handle.EncryptButton = this.CreateUguiPrimaryButton(card.transform, "EncryptChangedButton",
                this.L("pictures.encrypt_changed"), new System.Action(this.OnUguiPicturesEncryptChangedClicked));

            // -------- Button row 2: scan, full pair width (:160-170 — Secondary, !busy) --------
            handle.ScanButton = this.CreateUguiSecondaryButton(card.transform, "ScanChangedButton",
                this.L("pictures.scan_changed"), new System.Action(this.OnUguiPicturesScanChangedClicked));

            // -------- Button row 3: extract | upload (:175-185 — Secondary, UNLOCALIZED;
            // extract gated !busy, upload !busy && !chunkSendBusy — file header) --------
            handle.ExtractButton = this.CreateUguiSecondaryButton(card.transform, "ExtractButton",
                "Extract open drawing", new System.Action(this.OnUguiPicturesExtractClicked));
            handle.UploadButton = this.CreateUguiSecondaryButton(card.transform, "UploadButton",
                "Upload drawing.png", new System.Action(this.OnUguiPicturesUploadClicked));

            bool busy = this.IsUguiPicturesTaskBusy();
            bool chunkSendBusy = this.IsUguiPicturesChunkSendBusy();
            this.SetUguiButtonInteractable(handle.DecryptButton, !busy);
            this.SetUguiButtonInteractable(handle.EncryptButton, !busy);
            this.SetUguiButtonInteractable(handle.ScanButton, !busy);
            this.SetUguiButtonInteractable(handle.ExtractButton, !busy);
            this.SetUguiButtonInteractable(handle.UploadButton, !busy && !chunkSendBusy);

            // -------- Slider rows (:193-211 — labels 220x20, sliders at +228, innerW-228) -----
            handle.BudgetShown = this.BuildUguiPicturesBudgetText();
            handle.BudgetLabel = this.CreateUguiLabel(card.transform, "BudgetLabel",
                handle.BudgetShown, 11f, textColor, false);
            handle.BudgetSlider = this.CreateUguiSlider(card.transform, "BudgetSlider",
                DrawUploadRunsPerChunkMin, DrawUploadRunsPerChunkMax, this.drawUploadRunsPerChunk,
                true, new System.Action<float>(this.OnUguiPicturesBudgetChanged));

            handle.DelayShown = this.BuildUguiPicturesDelayText();
            handle.DelayLabel = this.CreateUguiLabel(card.transform, "DelayLabel",
                handle.DelayShown, 11f, textColor, false);
            handle.DelaySlider = this.CreateUguiSlider(card.transform, "DelaySlider",
                DrawUploadChunkDelayMin, DrawUploadChunkDelayMax, this.drawUploadChunkDelaySeconds,
                false, new System.Action<float>(this.OnUguiPicturesDelayChanged));

            // -------- Changed-count header (:213-216) --------
            handle.HeaderShown = this.BuildUguiPicturesHeaderText(handle.HasManifest);
            handle.HeaderLabel = this.CreateUguiLabel(card.transform, "ChangedHeader",
                handle.HeaderShown, 11f, textColor, false);

            // -------- Nested changed-files list (:219-231 — file header, mechanic 2) --------
            Transform listContent;
            handle.ListScroll = this.CreateUguiScrollView(card.transform, "ChangedList", 10f, out listContent);
            handle.ListContent = listContent;
            try
            {
                Image listBg = handle.ListScroll.GetComponent<Image>();
                if (listBg != null)
                {
                    listBg.color = Color.clear; // the source draws no box behind the list
                }
                if (listContent != null && listContent.parent != null)
                {
                    Image listVpBg = listContent.parent.GetComponent<Image>();
                    if (listVpBg != null)
                    {
                        listVpBg.color = Color.clear;
                    }
                }
            }
            catch { }
            this.SyncUguiPicturesListRows(handle);

            // -------- Status (:233-234 — wrapped, fixed 56 high) --------
            handle.StatusShown = this.BuildUguiPicturesStatusText();
            handle.StatusLabel = this.CreateUguiLabel(card.transform, "StatusLabel",
                handle.StatusShown, 11f, textColor, false);
            this.TrySetUguiLabelWrapped(handle.StatusLabel);

            // First measurements (may run while this sub-tab is inactive — spike caveat; a
            // rejected measure keeps the 36f seed and the slow tick retries once visible).
            bool ok;
            handle.PathsH = this.MeasureUguiPicturesWrappedHeight(handle.PathsLabel,
                handle.PathsShown, handle.InnerW, 36f, out ok);
            handle.PathsMeasureOk = ok;
            handle.HintH = this.MeasureUguiPicturesWrappedHeight(handle.HintLabel,
                hintText, handle.InnerW, 36f, out ok);
            handle.HintMeasureOk = ok;

            this.RelayoutUguiShellNewFeaturesPictures(handle);

            handle.Root = block;
            this.uguiShellNewFeaturesPictures = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Relayout — replays the source cursor chain (:111-236) with the current text-driven
        // heights and list count, then stores the values it laid out with (the signature).
        // Everything above the paths label is fixed; everything below flows.
        // ----------------------------------------------------------------------------------------

        private void RelayoutUguiShellNewFeaturesPictures(UguiShellNewFeaturesPicturesHandle handle)
        {
            const float rowGap = 10f;   // :112
            const float statusH = 56f;  // :113
            const float btnH = 32f;     // :81
            const float btnGap = 8f;    // :81
            const float sliderRowH = 20f; // :114

            float innerW = handle.InnerW;
            float btnW = handle.BtnW;
            float rowPairW = btnW * 2f + btnGap;
            int count = this.picturesChangedRelativePaths.Count;
            float scrollH = count > 0 ? Mathf.Min(count, 6) * 18f + 4f : 0f; // :119-121

            // The :123-127 card-height formula VERBATIM (scrollH+8 charged unconditionally —
            // the source's own empty-list padding quirk, see file header).
            float cardH = 10f + 22f + handle.PathsH + rowGap
                + handle.HintH + rowGap
                + btnH + rowGap + btnH + rowGap + btnH + rowGap
                + sliderRowH + rowGap + sliderRowH + rowGap
                + 20f + scrollH + 8f + statusH + 16f;

            PlaceUguiTopLeft(handle.Card, 8f, 8f, handle.PanelW, cardH);

            // Cursor replay (card-local; title fixed at 16,10 by the builder).
            float cy = 32f; // 10 + 22 (title)
            PlaceUguiTopLeft(handle.PathsLabel, 16f, cy, innerW, handle.PathsH);
            cy += handle.PathsH + rowGap;

            PlaceUguiTopLeft(handle.HintLabel, 16f, cy, innerW, handle.HintH);
            cy += handle.HintH + rowGap;

            PlaceUguiTopLeft(handle.DecryptButton, 16f, cy, btnW, btnH);
            PlaceUguiTopLeft(handle.EncryptButton, 16f + btnW + btnGap, cy, btnW, btnH);
            cy += btnH + rowGap;

            PlaceUguiTopLeft(handle.ScanButton, 16f, cy, rowPairW, btnH);
            cy += btnH + rowGap;

            PlaceUguiTopLeft(handle.ExtractButton, 16f, cy, btnW, btnH);
            PlaceUguiTopLeft(handle.UploadButton, 16f + btnW + btnGap, cy, btnW, btnH);
            cy += btnH + rowGap;

            // Slider rows (:193-211): label at cy, slider at cy+2 h=16 in the source; the kit
            // handle is 18px, so it sits at cy+1 h=18 — same visual center, handle fully inside.
            PlaceUguiTopLeft(handle.BudgetLabel, 16f, cy, 220f, sliderRowH);
            PlaceUguiTopLeft(handle.BudgetSlider.gameObject, 16f + 228f, cy + 1f, innerW - 228f, 18f);
            cy += sliderRowH + rowGap;

            PlaceUguiTopLeft(handle.DelayLabel, 16f, cy, 220f, sliderRowH);
            PlaceUguiTopLeft(handle.DelaySlider.gameObject, 16f + 228f, cy + 1f, innerW - 228f, 18f);
            cy += sliderRowH + rowGap;

            PlaceUguiTopLeft(handle.HeaderLabel, 16f, cy, innerW, 20f);
            cy += 20f;

            // Nested list (:221: innerX-4 → card-local 12, innerW+8 → panelW-24) — present and
            // advancing the cursor ONLY when count > 0 (:219-231).
            if (count > 0)
            {
                handle.ListScroll.SetActive(true);
                PlaceUguiTopLeft(handle.ListScroll, 12f, cy, handle.PanelW - 24f, scrollH);
                cy += scrollH + 8f;
            }
            else
            {
                handle.ListScroll.SetActive(false);
            }

            PlaceUguiTopLeft(handle.StatusLabel, 16f, cy, innerW, statusH);

            // :236 return = sectionRect.yMax + 24 (dispatcher adds nothing on sub 3).
            this.SetUguiScrollContentHeight(handle.OuterContent, 8f + cardH + 24f);

            handle.LayoutPathsH = handle.PathsH;
            handle.LayoutHintH = handle.HintH;
            handle.LayoutCount = count;
        }

        // ----------------------------------------------------------------------------------------
        // Nested-list row pool — grown on demand, rebound by cached-string diff, deactivated on
        // shrink (file header). Rows are index-stable at (0, i*18) — they never reposition; the
        // inner content height tracks the live count (:222).
        // ----------------------------------------------------------------------------------------

        private void SyncUguiPicturesListRows(UguiShellNewFeaturesPicturesHandle handle)
        {
            List<string> list = this.picturesChangedRelativePaths;
            int count = list.Count;
            float rowW = handle.PanelW - 24f - 22f; // list width minus the kit viewport insets
            Color textColor = this.UguiKitTextColor();

            for (int i = 0; i < count; i++)
            {
                if (i < handle.RowLabels.Count)
                {
                    GameObject row = handle.RowLabels[i];
                    if (row != null && !row.activeSelf)
                    {
                        row.SetActive(true);
                    }
                    if (!string.Equals(handle.RowShown[i], list[i], StringComparison.Ordinal))
                    {
                        handle.RowShown[i] = list[i];
                        this.SetUguiLabelText(row, list[i]);
                    }
                }
                else
                {
                    GameObject row = this.CreateUguiLabel(handle.ListContent, "Row" + i,
                        list[i], 11f, textColor, false);
                    PlaceUguiTopLeft(row, 0f, i * 18f, rowW, 18f); // :226 row chain
                    handle.RowLabels.Add(row);
                    handle.RowShown.Add(list[i]);
                }
            }

            for (int i = count; i < handle.RowLabels.Count; i++)
            {
                GameObject row = handle.RowLabels[i];
                if (row != null && row.activeSelf)
                {
                    row.SetActive(false);
                }
            }

            this.SetUguiScrollContentHeight(handle.ListContent, count * 18f);
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellNewFeaturesPicturesOnUpdate()
        {
            UguiShellNewFeaturesPicturesHandle handle = this.uguiShellNewFeaturesPictures;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellNewFeaturesSubTabActive(UguiShellPicturesSubIndex))
            {
                return;
            }

            try
            {
                // Busy gates — the FULL live conditions recomputed EVERY gated frame, with the
                // source's exact scoping (file header: scan+extract ride !busy, upload alone
                // rides both flags). SetUguiButtonInteractable self-diffs the actual write.
                bool busy = this.IsUguiPicturesTaskBusy();
                bool chunkSendBusy = this.IsUguiPicturesChunkSendBusy();
                this.SetUguiButtonInteractable(handle.DecryptButton, !busy);
                this.SetUguiButtonInteractable(handle.EncryptButton, !busy);
                this.SetUguiButtonInteractable(handle.ScanButton, !busy);
                this.SetUguiButtonInteractable(handle.ExtractButton, !busy);
                this.SetUguiButtonInteractable(handle.UploadButton, !busy && !chunkSendBusy);

                // Slider re-syncs: pull the handles onto the committed fields after a drag AND
                // mirror external IMGUI edits (sprint idiom — epsilon diff, WithoutNotify), plus
                // their live value labels (Sand Sculpture's DelayLabel per-frame precedent).
                if (handle.BudgetSlider != null
                    && Mathf.Abs(handle.BudgetSlider.value - this.drawUploadRunsPerChunk) > 0.0005f)
                {
                    handle.BudgetSlider.SetValueWithoutNotify(this.drawUploadRunsPerChunk);
                }
                this.SyncUguiSelfLabelText(handle.BudgetLabel, ref handle.BudgetShown,
                    this.BuildUguiPicturesBudgetText());

                if (handle.DelaySlider != null
                    && Mathf.Abs(handle.DelaySlider.value - this.drawUploadChunkDelaySeconds) > 0.0005f)
                {
                    handle.DelaySlider.SetValueWithoutNotify(this.drawUploadChunkDelaySeconds);
                }
                this.SyncUguiSelfLabelText(handle.DelayLabel, ref handle.DelayShown,
                    this.BuildUguiPicturesDelayText());

                // Status — background coroutines rewrite picturesLastStatus; cached-string diff.
                this.SyncUguiSelfLabelText(handle.StatusLabel, ref handle.StatusShown,
                    this.BuildUguiPicturesStatusText());

                // 0.5s slow tick — everything that touches disk/reflection or allocates
                // composite strings (file header refresh model).
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;

                    // Frame-start path resolve (:102-103) at 2Hz; a changed paths text (e.g.
                    // CACHE_PATH becoming available) re-measures and re-flows the card.
                    string sourcePath = this.TryGetScreenCaptureRootPath();
                    string destPath = this.GetScreenCaptureDecryptedRootPath(sourcePath);
                    string pathsText = this.LF("pictures.paths", sourcePath, destPath);
                    bool pathsChanged = !string.Equals(pathsText, handle.PathsShown, StringComparison.Ordinal);
                    if (pathsChanged)
                    {
                        handle.PathsShown = pathsText;
                        this.SetUguiLabelText(handle.PathsLabel, pathsText);
                    }
                    if (pathsChanged || !handle.PathsMeasureOk)
                    {
                        bool ok;
                        handle.PathsH = this.MeasureUguiPicturesWrappedHeight(handle.PathsLabel,
                            handle.PathsShown, handle.InnerW, handle.PathsH, out ok);
                        handle.PathsMeasureOk = ok;
                    }
                    if (!handle.HintMeasureOk)
                    {
                        // Hint text is fixed after build — only the spike-caveat retry remains.
                        bool ok;
                        handle.HintH = this.MeasureUguiPicturesWrappedHeight(handle.HintLabel,
                            this.L("pictures.draw_hint"), handle.InnerW, handle.HintH, out ok);
                        handle.HintMeasureOk = ok;
                    }

                    // The :105-109 dirty lazy-refresh, verbatim semantics (fresh manifest load
                    // immediately preceding the refresh, exactly what the drawer's frame holds).
                    // This is the SHA pass — main-thread, once per dirty event, IMGUI parity.
                    if (this.picturesChangedListDirty)
                    {
                        PicturesManifest manifest = this.TryLoadPicturesManifest(destPath);
                        this.RefreshPicturesChangedList(manifest, destPath);
                        this.picturesChangedListDirty = false;
                        handle.HasManifest = manifest != null && manifest.Files != null && manifest.Files.Count > 0;
                    }

                    // Row rebinds catch the encrypt routine's FLAGLESS background rewrites
                    // (:493/:540 — file header); header rides the live count + cached snapshot.
                    this.SyncUguiPicturesListRows(handle);
                    this.SyncUguiSelfLabelText(handle.HeaderLabel, ref handle.HeaderShown,
                        this.BuildUguiPicturesHeaderText(handle.HasManifest));

                    // Signature check — text-driven heights + list count (file header).
                    if (handle.LayoutPathsH != handle.PathsH
                        || handle.LayoutHintH != handle.HintH
                        || handle.LayoutCount != this.picturesChangedRelativePaths.Count)
                    {
                        this.RelayoutUguiShellNewFeaturesPictures(handle);
                    }
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] NewFeatures/Pictures content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order)
        // ----------------------------------------------------------------------------------------

        // :148-151 — routes straight to StartPicturesDecryptAll (internally guarded against
        // re-entry at :241, so a same-frame race click is harmless — same as the IMGUI twin).
        private void OnUguiPicturesDecryptAllClicked()
        {
            this.StartPicturesDecryptAll(false);
        }

        // :153-156 — same shape (internal guard at :257).
        private void OnUguiPicturesEncryptChangedClicked()
        {
            this.StartPicturesEncryptChanged(false);
        }

        // :160-170 — the FULL scan sequence, not just "call scan" (file header): dirty=true →
        // manifest reload → refresh → dirty=false → hasManifest recompute → the two-way status
        // write. destPath resolved fresh via the same helpers the drawer's frame uses (:102-103).
        // Then UGUI-side bookkeeping only (snapshot/rows/header/relayout — mirror state).
        private void OnUguiPicturesScanChangedClicked()
        {
            string sourcePath = this.TryGetScreenCaptureRootPath();
            string destPath = this.GetScreenCaptureDecryptedRootPath(sourcePath);

            this.picturesChangedListDirty = true;
            PicturesManifest manifest = this.TryLoadPicturesManifest(destPath);
            this.RefreshPicturesChangedList(manifest, destPath);
            this.picturesChangedListDirty = false;
            bool hasManifest = manifest != null && manifest.Files != null && manifest.Files.Count > 0;
            this.picturesLastStatus = hasManifest
                ? this.LF("pictures.changed_count", this.picturesChangedRelativePaths.Count)
                : this.L("pictures.manifest_missing");

            UguiShellNewFeaturesPicturesHandle handle = this.uguiShellNewFeaturesPictures;
            if (handle != null && handle.Root != null)
            {
                handle.HasManifest = hasManifest;
                this.SyncUguiPicturesListRows(handle);
                this.SyncUguiSelfLabelText(handle.HeaderLabel, ref handle.HeaderShown,
                    this.BuildUguiPicturesHeaderText(hasManifest));
                if (handle.LayoutCount != this.picturesChangedRelativePaths.Count)
                {
                    this.RelayoutUguiShellNewFeaturesPictures(handle);
                }
            }
        }

        // :175-178 — direct call; the !busy gate is the interactable state, like GUI.enabled.
        private void OnUguiPicturesExtractClicked()
        {
            this.DrawExtractOpenDrawing();
        }

        // :182-185 — direct call (internally guarded against a live chunk send at
        // DrawUploadFeature.cs:694, so a same-frame race click is harmless).
        private void OnUguiPicturesUploadClicked()
        {
            this.DrawUploadSendForOpenDrawing();
        }

        // :194-198 — UI_DrawAccentIntSlider semantics (UiKit.cs:562-566): whole-number track,
        // Clamp(RoundToInt) committed straight to the field. Plain assignment — no save, no
        // notification (verified against the source).
        private void OnUguiPicturesBudgetChanged(float value)
        {
            this.drawUploadRunsPerChunk = Mathf.Clamp(Mathf.RoundToInt(value),
                DrawUploadRunsPerChunkMin, DrawUploadRunsPerChunkMax);
        }

        // :202-210 — the EXACT source snap: Clamp(Round(raw*20)/20, min, max) — nearest 0.05s
        // (the migration's fourth distinct rounding granularity). Plain assignment.
        private void OnUguiPicturesDelayChanged(float value)
        {
            this.drawUploadChunkDelaySeconds = Mathf.Clamp(Mathf.Round(value * 20f) / 20f,
                DrawUploadChunkDelayMin, DrawUploadChunkDelayMax);
        }
    }
}
