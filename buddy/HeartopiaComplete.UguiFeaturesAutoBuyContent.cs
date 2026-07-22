using System;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, Features round 4 of 8 (migration plan item 11): the
    // AUTO BUY sub-tab — the inline automationSubTab == 3 branch (HeartopiaComplete.Gui.cs:
    // 1080-1279), display sub-index 3 (the tabs list {"Main","Food & Repair","Snow Sculpting",
    // "Auto Buy","Auto Sell","Mass Cook","Puzzle","Pet Care"} maps display indices to
    // automationSubTab 0-7 exactly). The remaining four subs (Food & Repair 1, Auto Sell 4,
    // Mass Cook 5, Pet Care 7) are separate future rounds and keep the shell placeholder.
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawer and every backend method it calls stay fully functional and untouched —
    //    this file only READS the same fields and CALLS the same methods (all this.-accessible
    //    partial-class state; ZERO backend interop additions). Two independent rendering paths
    //    over one backend.
    //  - Wiring is by STATIC display-position index (UguiShellFeaturesTabIndex = 2 +
    //    UguiShellFeaturesAutoBuySubIndex = 3, declared with their siblings in
    //    UguiShellTabIndices.cs), never label comparison. The processor gates on the SAME
    //    IsUguiShellFeaturesSubTabActive function the Main round established — no new gate.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // Source nuances verified against the branch, replayed exactly:
    //  - ONE DrawExentriSectionPanel card holds everything (:1108-1110), 580x762 at the source's
    //    uniform column left — full-width role → panelW (Puzzle precedent), via the shared
    //    CreateUguiSettingsMainPanel chrome (identical fill/line formula: uiContent @
    //    clamp(panelAlpha*0.82, .14, .92) + accent @ 0.24 ring; its 11pt header vs the source's
    //    12pt sectionStyle is the accepted sibling-tab look — Theme-round precedent). The
    //    source's height is 762 + a dropdown-OPEN growth term (:1108); the growth is an
    //    inline-reflow artifact of drawing the option list in the flow — the kit's stock
    //    UnityEngine.UI.Dropdown shows its list as its own popup overlay (Birds precedent), so
    //    the card is a FIXED 762 here. Content ends at 576 (conditional shown) — the remaining
    //    ~186px of card is genuine source slack (IMGUI renders the same empty card bottom).
    //  - Shop dropdown: IMGUI's hand-rolled box+buttons (:1119-1170) → kit CreateUguiDropdown,
    //    wired like Birds' Capture Mode (out-bool listenerWired + per-frame poll fallback).
    //    Option strings are drawn RAW in the source (:1127/:1155 — plain labels, no L), so they
    //    are passed unlocalized. On pick, the :1159-1163 block is replayed verbatim (index +
    //    the "No shop selected."/"Selected: X" status write). The :1160
    //    forceOpenShopDropdownOpen=false write is deliberately NOT reproduced: that field is
    //    IMGUI-only visual state (its twin's open option list), and cross-surface writes of
    //    another surface's visuals are what these rounds avoid (puzzleUiOpenLogged precedent).
    //    Stock-Dropdown nuance: re-picking the ALREADY-selected option fires no event (IMGUI
    //    re-ran the status write on any option click) — accepted stock semantics (Birds).
    //  - Localization roles: "Auto Buy", "Select Shop Panel", "Max per item", "Manual Store ID",
    //    "Manual Store Name" go through L (source L()s each). Every button label gets ONE L:
    //    DrawPrimary/SecondaryActionButton localize internally (UiKitPrimitives.cs:731/:744),
    //    and the source's explicit L("BUY ALL (COIN)") at :1205 only double-localizes
    //    (idempotent). "QuickBuyItem (store / slot / item)" is a RAW literal (:1219) — kept
    //    unlocalized on purpose.
    //  - Text styles: bodyStyle = 12pt uiText @ 0.95 (:1090-1091); mutedStyle = 11pt subTabText
    //    @ 0.92 wordWrap (:1092-1093, final status only). The three multi-line-rect labels
    //    (block reason h=32, quick-buy status h=36, final status h=40) get
    //    TrySetUguiLabelWrapped (long backend statuses genuinely wrap there).
    //  - Block-reason overlap quirk: the conditional label's rect is 32 tall but the cursor
    //    advances only 20 (:1213-1214), so the next label overlaps its bottom 12px in IMGUI —
    //    replayed as-is (labels don't raycast; the overlap is the source look).
    //  - "Max per item" is the ONE live parse-clamp-writeback field (Foraging HH:MM:SS idiom,
    //    single-field): the IMGUI block re-binds shopBuyAllMaxPerItem.ToString() every frame,
    //    TryParse → Clamp(1, 999999) → the int field, SaveKeybinds(false) only on an actual
    //    value change (:1187-1200). The cross-surface contract is the INT itself (no shared
    //    input string exists), so a failed parse keeps the raw text in the box and the 0.5s
    //    external re-sync snaps it back to the int's text — gentler than the IMGUI twin's
    //    instant per-frame rebind, same steady state.
    //  - The other five fields are free-text STORES, not parse fields: the source writes the
    //    TextField return straight into the shared string every frame, unparsed and unclamped
    //    (:1221-1223, :1235-1238, :1256-1259) — validation happens inside the backend calls.
    //    Each field's onValueChanged writes the raw text into its shared string (so the IMGUI
    //    twin and any backend read always see the current text, matching the source's
    //    every-frame store), and the consuming click handlers ALSO re-read .text into the
    //    shared strings first (Teleport-XYZ click-time-read insurance — covers a silently
    //    failed UnityEvent<string> wiring) before calling the backend, which reads the shared
    //    strings itself (ShopQuickBuyFeature.cs:53-65, Shop.cs:349/:365).
    //  - BUY ALL (COIN) gate is LIVE, not cached: GUI.enabled = !shopBuyAllRunning &&
    //    IsForceShopBuyAllSupported(selection) is computed every drawn IMGUI frame (:1203-1204)
    //    — selection changes move it, and the busy flag clears from the background buy
    //    coroutine — so the processor re-evaluates it every gated frame (the supported check is
    //    a zero-alloc literal switch, Shop.cs:34-48) and re-syncs button interactability + the
    //    conditional reason label. The click handler re-checks the same gate at click time: the
    //    interactable sync is per-frame, and without the re-check a same-frame race could reach
    //    StartShopBuyAllGold's own guard, whose unsupported path fires a red notification the
    //    disabled IMGUI button could never produce.
    //  - 2-way notifications: Open Selected Shop (:1172-1184), Open ID (:1239-1251) and Open
    //    Name (:1260-1272) each store the SAME out-string into forceOpenShopStatus AND show it
    //    as the notification on BOTH outcomes — green (0.45,1,0.55) on true, red (1,0.55,0.55)
    //    on false. The full two-branch shape is kept verbatim (Snow precedent, not collapsed).
    //    Open Buy Panel (:1225-1228) shows NO notification — StartShopQuickBuyOpenPanel
    //    notifies internally (untouched).
    //  - Status labels: shopBuyAllStatus is bound RAW — no null-coalesce, like :1216 (the field
    //    inits to "Idle." and every backend assignment is non-null); the other two replay the
    //    source's fallbacks (shopQuickBuyStatus ?? "Idle." :1230, forceOpenShopStatus ??
    //    "No shop selected." :1275).
    //
    // Cross-surface sync cadence: every gated frame — dropdown poll fallback FIRST (a user pick
    // lands before the external re-sync could clobber it — Birds order), dropdown external
    // re-sync (SetValueWithoutNotify + LastValue), the live BUY ALL gate (interactable + reason
    // text; reason text syncs even while hidden — Snow's stays-ready idiom), and the three
    // status labels (raw shared-field references, zero-alloc, cached-string compare). The six
    // InputField external re-syncs ride the 0.5s tick (SyncUguiInputFieldFromBackingField —
    // diffed against the *Seen caches so in-progress typing is never clobbered; Max per item's
    // ToString allocates, hence the tick). Conditional layout (the reason row + everything
    // below it) repositions via relayout-on-signature (Foraging's idiom). Per-frame sync
    // disabled after 3 consecutive errors (LIVE rail idiom).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellFeaturesAutoBuyHandle
        {
            public GameObject Root;
            public float ForceWidth;              // panel inner width (panelW - 28, source forceWidth)

            public Dropdown ShopDropdown;
            public bool ShopDropdownListenerWired;
            public int ShopDropdownLastValue;     // poll-fallback change detection

            public InputField MaxPerItemField;    // live parse-clamp-writeback (file header)
            public string MaxPerItemSeen;         // external-change sync cache (int-derived text)

            public GameObject BuyAllButton;       // interactable re-synced every gated frame
            public GameObject BlockReasonLabel;   // conditional row (!supported && reason)
            public string BlockReasonShown;
            public GameObject BuyAllStatusLabel;  // raw shopBuyAllStatus (no coalesce — header)
            public string BuyAllStatusShown;

            public GameObject QuickBuyCaption;    // repositions with the reason row
            public InputField QuickBuyStoreField; // free-text stores (click-time consumed)
            public string QuickBuyStoreSeen;
            public InputField QuickBuySlotField;
            public string QuickBuySlotSeen;
            public InputField QuickBuyItemField;
            public string QuickBuyItemSeen;
            public GameObject OpenBuyPanelButton;
            public GameObject QuickBuyStatusLabel;
            public string QuickBuyStatusShown;

            public GameObject ManualIdCaption;
            public InputField ManualIdField;
            public string ManualIdSeen;
            public GameObject OpenIdButton;
            public GameObject ManualNameCaption;
            public InputField ManualNameField;
            public string ManualNameSeen;
            public GameObject OpenNameButton;
            public GameObject ForceOpenStatusLabel;
            public string ForceOpenStatusShown;

            public int LayoutSignature = -1;      // reason-row-visible bit
            public float NextSlowSyncAt;          // 0.5s tick for InputField re-syncs (header)
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellFeaturesAutoBuyHandle uguiShellFeaturesAutoBuy;

        // ----------------------------------------------------------------------------------------
        // Live layout signature — the reason row is the branch's only conditional (Gui.cs:
        // 1211-1215). Same expression the relayout and the per-frame gate use.
        // ----------------------------------------------------------------------------------------

        private int ComputeUguiFeaturesAutoBuyLayoutSignature()
        {
            bool supported = this.IsForceShopBuyAllSupported(this.forceOpenShopSelectedIndex,
                out string blockReason);
            return (!supported && !string.IsNullOrEmpty(blockReason)) ? 1 : 0;
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of the automationSubTab == 3 branch: one fixed 762px section card holding a
        // dropdown, 6 input fields, 5 buttons and 4 status labels. All controls — including the
        // conditionally-visible reason label — are built ONCE here in IMGUI source order;
        // RelayoutUguiShellFeaturesAutoBuy owns the positions from the BUY ALL row down (the
        // y-cursor accumulation analog). Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellFeaturesAutoBuyContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellFeaturesAutoBuy = null;

            UguiShellFeaturesAutoBuyHandle handle = new UguiShellFeaturesAutoBuyHandle();
            GameObject block = this.CreateUguiGo("FeaturesAutoBuyContent", parent);
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

            float contentWidth = w - 22f;       // viewport insets: 4 left + 18 right
            float panelW = contentWidth - 16f;  // full-width card at x=8, 8px right margin
            handle.ForceWidth = panelW - 28f;   // source forceWidth = panel width - 28 (:1113)

            // Source text roles (file header): bodyStyle = 12 uiText @ 0.95; mutedStyle = 11
            // subTabText @ 0.92 wrapped.
            Color bodyColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.95f);
            Color mutedColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);

            // -------- Section card (:1108-1111 — DrawExentriSectionPanel + L("Auto Buy") header;
            // FIXED 762, dropdown-open growth dropped — file header) --------
            GameObject card = this.CreateUguiSettingsMainPanel(scrollContent, "AutoBuyPanel", this.L("Auto Buy"));
            PlaceUguiTopLeft(card, 8f, 8f, panelW, 762f);

            // -------- Select Shop Panel label + dropdown (:1116-1129, panel-local 48/74) --------
            GameObject selectLabel = this.CreateUguiLabel(card.transform, "SelectShopLabel",
                this.L("Select Shop Panel"), 12f, bodyColor, false);
            PlaceUguiTopLeft(selectLabel, 14f, 48f, handle.ForceWidth, 20f);

            int shopInitial = Mathf.Clamp(this.forceOpenShopSelectedIndex, 0,
                this.forceOpenShopOptions.Length - 1);
            handle.ShopDropdownLastValue = shopInitial;
            bool shopWired;
            handle.ShopDropdown = this.CreateUguiDropdown(card.transform, "ShopDropdown",
                this.forceOpenShopOptions, shopInitial,
                new System.Action<int>(this.OnUguiFeaturesAutoBuyShopPicked), out shopWired);
            handle.ShopDropdownListenerWired = shopWired;
            PlaceUguiTopLeft(handle.ShopDropdown.gameObject, 14f, 74f, 280f, 28f);

            // -------- Open Selected Shop (:1172 — secondary 220x32 at 116) --------
            GameObject openSelected = this.CreateUguiSecondaryButton(card.transform, "OpenSelectedShop",
                this.L("Open Selected Shop"),
                new System.Action(this.OnUguiFeaturesAutoBuyOpenSelectedShopClicked));
            PlaceUguiTopLeft(openSelected, 14f, 116f, 220f, 32f);

            // -------- Max per item (:1188-1192 — label 120x22 + field 80x22 at +124, y 156) ----
            GameObject maxLabel = this.CreateUguiLabel(card.transform, "MaxPerItemLabel",
                this.L("Max per item"), 12f, bodyColor, false);
            PlaceUguiTopLeft(maxLabel, 14f, 156f, 120f, 22f);
            handle.MaxPerItemSeen = this.shopBuyAllMaxPerItem.ToString();
            handle.MaxPerItemField = this.CreateUguiInputField(card.transform, "MaxPerItemField",
                handle.MaxPerItemSeen, 8, new System.Action<string>(this.OnUguiFeaturesAutoBuyMaxPerItemEdited));
            PlaceUguiTopLeft(handle.MaxPerItemField.gameObject, 138f, 156f, 80f, 22f);

            // -------- BUY ALL (COIN) (:1203-1208 — gated primary 220x32 at 186) --------
            handle.BuyAllButton = this.CreateUguiPrimaryButton(card.transform, "BuyAllButton",
                this.L("BUY ALL (COIN)"),
                new System.Action(this.OnUguiFeaturesAutoBuyBuyAllClicked));
            PlaceUguiTopLeft(handle.BuyAllButton, 14f, 186f, 220f, 32f);
            bool initialSupported = this.IsForceShopBuyAllSupported(this.forceOpenShopSelectedIndex,
                out string initialBlockReason);
            this.SetUguiButtonInteractable(handle.BuyAllButton,
                !this.shopBuyAllRunning && initialSupported);

            // -------- Conditional block reason (:1211-1215 — bodyStyle, rect h=32; position and
            // visibility owned by the relayout) --------
            handle.BlockReasonShown = initialBlockReason ?? string.Empty;
            handle.BlockReasonLabel = this.CreateUguiLabel(card.transform, "BlockReasonLabel",
                handle.BlockReasonShown, 12f, bodyColor, false);
            this.TrySetUguiLabelWrapped(handle.BlockReasonLabel);

            // -------- Buy-all status (:1216 — RAW bind, no coalesce; see file header) --------
            handle.BuyAllStatusShown = this.shopBuyAllStatus;
            handle.BuyAllStatusLabel = this.CreateUguiLabel(card.transform, "BuyAllStatusLabel",
                handle.BuyAllStatusShown, 12f, bodyColor, false);

            // -------- QuickBuyItem caption + 3 free-text fields (:1219-1223 — RAW literal
            // caption; fields 90/90/100 x28 at 0/+98/+196, limits 8/8/10) --------
            handle.QuickBuyCaption = this.CreateUguiLabel(card.transform, "QuickBuyCaption",
                "QuickBuyItem (store / slot / item)", 12f, bodyColor, false);
            handle.QuickBuyStoreSeen = this.shopQuickBuyStoreIdInput ?? string.Empty;
            handle.QuickBuyStoreField = this.CreateUguiInputField(card.transform, "QuickBuyStoreField",
                handle.QuickBuyStoreSeen, 8, new System.Action<string>(this.OnUguiFeaturesAutoBuyQuickBuyStoreEdited));
            handle.QuickBuySlotSeen = this.shopQuickBuySlotIdInput ?? string.Empty;
            handle.QuickBuySlotField = this.CreateUguiInputField(card.transform, "QuickBuySlotField",
                handle.QuickBuySlotSeen, 8, new System.Action<string>(this.OnUguiFeaturesAutoBuyQuickBuySlotEdited));
            handle.QuickBuyItemSeen = this.shopQuickBuyItemIdInput ?? string.Empty;
            handle.QuickBuyItemField = this.CreateUguiInputField(card.transform, "QuickBuyItemField",
                handle.QuickBuyItemSeen, 10, new System.Action<string>(this.OnUguiFeaturesAutoBuyQuickBuyItemEdited));

            // -------- Open Buy Panel (:1225-1228 — secondary 220x32, NO notification) --------
            handle.OpenBuyPanelButton = this.CreateUguiSecondaryButton(card.transform, "OpenBuyPanel",
                this.L("Open Buy Panel"),
                new System.Action(this.OnUguiFeaturesAutoBuyOpenBuyPanelClicked));

            // -------- Quick-buy status (:1230 — bodyStyle h=36, ?? "Idle.") --------
            handle.QuickBuyStatusShown = this.shopQuickBuyStatus ?? "Idle.";
            handle.QuickBuyStatusLabel = this.CreateUguiLabel(card.transform, "QuickBuyStatusLabel",
                handle.QuickBuyStatusShown, 12f, bodyColor, false);
            this.TrySetUguiLabelWrapped(handle.QuickBuyStatusLabel);

            // -------- Manual Store ID row (:1233-1251 — field 120x28 limit 8 + Open ID at +130,
            // 120x28) --------
            handle.ManualIdCaption = this.CreateUguiLabel(card.transform, "ManualIdCaption",
                this.L("Manual Store ID"), 12f, bodyColor, false);
            handle.ManualIdSeen = this.forceOpenShopManualStoreIdInput ?? string.Empty;
            handle.ManualIdField = this.CreateUguiInputField(card.transform, "ManualIdField",
                handle.ManualIdSeen, 8, new System.Action<string>(this.OnUguiFeaturesAutoBuyManualIdEdited));
            handle.OpenIdButton = this.CreateUguiSecondaryButton(card.transform, "OpenIdButton",
                this.L("Open ID"),
                new System.Action(this.OnUguiFeaturesAutoBuyOpenIdClicked));

            // -------- Manual Store Name row (:1254-1272 — field 240x28 limit 64 + Open Name at
            // +250, 130x28) --------
            handle.ManualNameCaption = this.CreateUguiLabel(card.transform, "ManualNameCaption",
                this.L("Manual Store Name"), 12f, bodyColor, false);
            handle.ManualNameSeen = this.forceOpenShopManualStoreNameInput ?? string.Empty;
            handle.ManualNameField = this.CreateUguiInputField(card.transform, "ManualNameField",
                handle.ManualNameSeen, 64, new System.Action<string>(this.OnUguiFeaturesAutoBuyManualNameEdited));
            handle.OpenNameButton = this.CreateUguiSecondaryButton(card.transform, "OpenNameButton",
                this.L("Open Name"),
                new System.Action(this.OnUguiFeaturesAutoBuyOpenNameClicked));

            // -------- Final status (:1275 — mutedStyle h=40, ?? "No shop selected.") --------
            handle.ForceOpenStatusShown = this.forceOpenShopStatus ?? "No shop selected.";
            handle.ForceOpenStatusLabel = this.CreateUguiLabel(card.transform, "ForceOpenStatusLabel",
                handle.ForceOpenStatusShown, 11f, mutedColor, false);
            this.TrySetUguiLabelWrapped(handle.ForceOpenStatusLabel);

            handle.LayoutSignature = this.ComputeUguiFeaturesAutoBuyLayoutSignature();
            this.RelayoutUguiShellFeaturesAutoBuy(handle);

            // Card top 8 + fixed 762 + 16 comfort margin (round-1 convention). The card is the
            // honest extent: the source RETURNS its bare inner cursor (:1278, 556/576), which
            // stops ~186px above the card bottom it drew — sizing to the card keeps the whole
            // ring scrollable. Never conditional (the reason row moves content INSIDE the card).
            this.SetUguiScrollContentHeight(scrollContent, 8f + 762f + 16f);

            handle.Root = block;
            this.uguiShellFeaturesAutoBuy = handle;
            return block;
        }

        // Positions everything from the BUY ALL row down for the CURRENT gate state — the UGUI
        // analog of the IMGUI branch's num accumulation (Gui.cs:1210-1276): buy-all cursor 222 →
        // [reason at 222 rect h=32, +20 shown / +0 hidden — the source's overlap quirk, file
        // header] → status +22 → caption +22 → fields +34 → button +36 → status +40 → id caption
        // +24 → id row +38 → name caption +24 → name row +42 → final status. Everything above
        // BUY ALL is fixed at build time. Reposition/SetActive only; nothing is rebuilt.
        private void RelayoutUguiShellFeaturesAutoBuy(UguiShellFeaturesAutoBuyHandle handle)
        {
            bool supported = this.IsForceShopBuyAllSupported(this.forceOpenShopSelectedIndex,
                out string blockReason);
            bool reasonVisible = !supported && !string.IsNullOrEmpty(blockReason);
            float forceWidth = handle.ForceWidth;

            float yCur = 222f;
            SetUguiGoActive(handle.BlockReasonLabel, reasonVisible);
            if (reasonVisible)
            {
                PlaceUguiTopLeft(handle.BlockReasonLabel, 14f, yCur, forceWidth, 32f);
                yCur += 20f;
            }

            if (handle.BuyAllStatusLabel != null)
            {
                PlaceUguiTopLeft(handle.BuyAllStatusLabel, 14f, yCur, forceWidth, 18f);
            }
            yCur += 22f;

            if (handle.QuickBuyCaption != null)
            {
                PlaceUguiTopLeft(handle.QuickBuyCaption, 14f, yCur, forceWidth, 20f);
            }
            yCur += 22f;

            if (handle.QuickBuyStoreField != null)
            {
                PlaceUguiTopLeft(handle.QuickBuyStoreField.gameObject, 14f, yCur, 90f, 28f);
            }
            if (handle.QuickBuySlotField != null)
            {
                PlaceUguiTopLeft(handle.QuickBuySlotField.gameObject, 112f, yCur, 90f, 28f);
            }
            if (handle.QuickBuyItemField != null)
            {
                PlaceUguiTopLeft(handle.QuickBuyItemField.gameObject, 210f, yCur, 100f, 28f);
            }
            yCur += 34f;

            if (handle.OpenBuyPanelButton != null)
            {
                PlaceUguiTopLeft(handle.OpenBuyPanelButton, 14f, yCur, 220f, 32f);
            }
            yCur += 36f;

            if (handle.QuickBuyStatusLabel != null)
            {
                PlaceUguiTopLeft(handle.QuickBuyStatusLabel, 14f, yCur, forceWidth, 36f);
            }
            yCur += 40f;

            if (handle.ManualIdCaption != null)
            {
                PlaceUguiTopLeft(handle.ManualIdCaption, 14f, yCur, forceWidth, 20f);
            }
            yCur += 24f;

            if (handle.ManualIdField != null)
            {
                PlaceUguiTopLeft(handle.ManualIdField.gameObject, 14f, yCur, 120f, 28f);
            }
            if (handle.OpenIdButton != null)
            {
                PlaceUguiTopLeft(handle.OpenIdButton, 144f, yCur, 120f, 28f);
            }
            yCur += 38f;

            if (handle.ManualNameCaption != null)
            {
                PlaceUguiTopLeft(handle.ManualNameCaption, 14f, yCur, forceWidth, 20f);
            }
            yCur += 24f;

            if (handle.ManualNameField != null)
            {
                PlaceUguiTopLeft(handle.ManualNameField.gameObject, 14f, yCur, 240f, 28f);
            }
            if (handle.OpenNameButton != null)
            {
                PlaceUguiTopLeft(handle.OpenNameButton, 264f, yCur, 130f, 28f);
            }
            yCur += 42f;

            if (handle.ForceOpenStatusLabel != null)
            {
                PlaceUguiTopLeft(handle.ForceOpenStatusLabel, 14f, yCur, forceWidth, 40f);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellFeaturesAutoBuyOnUpdate()
        {
            UguiShellFeaturesAutoBuyHandle handle = this.uguiShellFeaturesAutoBuy;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellFeaturesSubTabActive(UguiShellFeaturesAutoBuySubIndex))
            {
                return;
            }

            try
            {
                // Dropdown poll fallback — only when UnityEvent<int> wiring reported failure
                // (Birds/Settings→Main precedent). Runs BEFORE the external re-sync below so a
                // user pick lands before it could be clobbered.
                if (!handle.ShopDropdownListenerWired && handle.ShopDropdown != null)
                {
                    int v = handle.ShopDropdown.value;
                    if (v != handle.ShopDropdownLastValue)
                    {
                        this.OnUguiFeaturesAutoBuyShopPicked(v); // updates ShopDropdownLastValue itself
                    }
                }

                // Dropdown external re-sync (the IMGUI twin moved forceOpenShopSelectedIndex) —
                // WithoutNotify + LastValue update (Birds shape).
                if (handle.ShopDropdown != null)
                {
                    int want = Mathf.Clamp(this.forceOpenShopSelectedIndex, 0,
                        this.forceOpenShopOptions.Length - 1);
                    if (handle.ShopDropdown.value != want)
                    {
                        handle.ShopDropdown.SetValueWithoutNotify(want);
                        handle.ShopDropdownLastValue = want;
                    }
                }

                // LIVE buy-all gate — re-evaluated every gated frame like the IMGUI block's
                // GUI.enabled (:1203-1204): selection-dependent + the background busy flag.
                // Zero-alloc (literal switch). Reason text syncs even while hidden (Snow idiom).
                bool supported = this.IsForceShopBuyAllSupported(this.forceOpenShopSelectedIndex,
                    out string blockReason);
                this.SetUguiButtonInteractable(handle.BuyAllButton,
                    !this.shopBuyAllRunning && supported);
                this.SyncUguiSelfLabelText(handle.BlockReasonLabel, ref handle.BlockReasonShown,
                    blockReason ?? string.Empty);

                // Status labels — raw shared-field references (zero-alloc), per frame; the
                // coalesce set mirrors the source bindings exactly (file header).
                this.SyncUguiSelfLabelText(handle.BuyAllStatusLabel, ref handle.BuyAllStatusShown,
                    this.shopBuyAllStatus);
                this.SyncUguiSelfLabelText(handle.QuickBuyStatusLabel, ref handle.QuickBuyStatusShown,
                    this.shopQuickBuyStatus ?? "Idle.");
                this.SyncUguiSelfLabelText(handle.ForceOpenStatusLabel, ref handle.ForceOpenStatusShown,
                    this.forceOpenShopStatus ?? "No shop selected.");

                // InputField external re-syncs (IMGUI-twin edits) — 0.5s tick; diffed against
                // the *Seen caches so in-progress UGUI typing is never clobbered.
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    SyncUguiInputFieldFromBackingField(handle.MaxPerItemField,
                        ref handle.MaxPerItemSeen, this.shopBuyAllMaxPerItem.ToString());
                    SyncUguiInputFieldFromBackingField(handle.QuickBuyStoreField,
                        ref handle.QuickBuyStoreSeen, this.shopQuickBuyStoreIdInput);
                    SyncUguiInputFieldFromBackingField(handle.QuickBuySlotField,
                        ref handle.QuickBuySlotSeen, this.shopQuickBuySlotIdInput);
                    SyncUguiInputFieldFromBackingField(handle.QuickBuyItemField,
                        ref handle.QuickBuyItemSeen, this.shopQuickBuyItemIdInput);
                    SyncUguiInputFieldFromBackingField(handle.ManualIdField,
                        ref handle.ManualIdSeen, this.forceOpenShopManualStoreIdInput);
                    SyncUguiInputFieldFromBackingField(handle.ManualNameField,
                        ref handle.ManualNameSeen, this.forceOpenShopManualStoreNameInput);
                }

                // Conditional-layout signature (the reason row shifts everything below it).
                int signature = (!supported && !string.IsNullOrEmpty(blockReason)) ? 1 : 0;
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellFeaturesAutoBuy(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Features Auto Buy content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order).
        // ----------------------------------------------------------------------------------------

        // Gui.cs:1157-1164 — index + the two-way status write, verbatim. The :1160
        // forceOpenShopDropdownOpen=false write is IMGUI-only visual state, deliberately NOT
        // reproduced (file header). No equal-guard: the source ran this on ANY option click and
        // both writes are idempotent (the kit dropdown sets value before wiring, so there is no
        // build-time fire to absorb).
        private void OnUguiFeaturesAutoBuyShopPicked(int index)
        {
            UguiShellFeaturesAutoBuyHandle handle = this.uguiShellFeaturesAutoBuy;
            if (handle != null)
            {
                handle.ShopDropdownLastValue = index;
            }
            this.forceOpenShopSelectedIndex = index;
            this.forceOpenShopStatus = index == 0
                ? "No shop selected."
                : ("Selected: " + this.forceOpenShopOptions[index]);
        }

        // Gui.cs:1172-1184 — the SAME out-string is stored into forceOpenShopStatus AND shown as
        // the notification on BOTH outcomes; green on true, red on false. The full two-branch
        // shape is kept verbatim (not collapsed) to mirror the source exactly.
        private void OnUguiFeaturesAutoBuyOpenSelectedShopClicked()
        {
            if (this.TryOpenSelectedForceShop(out string openStatus))
            {
                this.forceOpenShopStatus = openStatus;
                this.AddMenuNotification(openStatus, new Color(0.45f, 1f, 0.55f));
            }
            else
            {
                this.forceOpenShopStatus = openStatus;
                this.AddMenuNotification(openStatus, new Color(1f, 0.55f, 0.55f));
            }
        }

        // Gui.cs:1187-1200 as an on-edit event (Foraging's parse-clamp-writeback core, single
        // field over the INT contract — file header): TryParse → Clamp(1, 999999) → the int
        // field, SaveKeybinds(false) only on an actual value change (the source's prev-vs-new
        // compare), then the field text re-normalized via SetTextWithoutNotify when it differs
        // (never .text — that would re-fire the event). A failed parse keeps the int and the raw
        // text (the 0.5s external re-sync restores the int's text — gentler than the IMGUI
        // twin's instant per-frame rebind).
        private void OnUguiFeaturesAutoBuyMaxPerItemEdited(string text)
        {
            UguiShellFeaturesAutoBuyHandle handle = this.uguiShellFeaturesAutoBuy;
            if (handle == null)
            {
                return;
            }
            string raw = text ?? string.Empty;
            if (int.TryParse(raw, out int parsed))
            {
                int prev = this.shopBuyAllMaxPerItem;
                this.shopBuyAllMaxPerItem = Mathf.Clamp(parsed, 1, 999999);
                if (this.shopBuyAllMaxPerItem != prev)
                {
                    try { this.SaveKeybinds(false); } catch { }
                }
                string normalized = this.shopBuyAllMaxPerItem.ToString();
                handle.MaxPerItemSeen = normalized;
                if (handle.MaxPerItemField != null
                    && !string.Equals(normalized, raw, StringComparison.Ordinal))
                {
                    try { handle.MaxPerItemField.SetTextWithoutNotify(normalized); } catch { }
                }
            }
            else
            {
                handle.MaxPerItemSeen = raw;
            }
        }

        // Gui.cs:1203-1208 — the click handler re-checks the IMGUI gate at click time (the
        // interactable sync is per-frame; without this a same-frame race could reach the
        // backend's own unsupported path, which fires a red notification the disabled IMGUI
        // button could never produce — file header).
        private void OnUguiFeaturesAutoBuyBuyAllClicked()
        {
            if (this.shopBuyAllRunning
                || !this.IsForceShopBuyAllSupported(this.forceOpenShopSelectedIndex, out string _))
            {
                return;
            }
            this.StartShopBuyAllGold();
        }

        // Gui.cs:1221-1223 — free-text STORES (no parse, no clamp — file header): the raw text
        // goes straight into the shared string so the IMGUI twin and the backend's own reads
        // always see the current value, exactly like the source's every-frame TextField store.
        private void OnUguiFeaturesAutoBuyQuickBuyStoreEdited(string text)
        {
            string raw = text ?? string.Empty;
            this.shopQuickBuyStoreIdInput = raw;
            UguiShellFeaturesAutoBuyHandle handle = this.uguiShellFeaturesAutoBuy;
            if (handle != null)
            {
                handle.QuickBuyStoreSeen = raw;
            }
        }

        private void OnUguiFeaturesAutoBuyQuickBuySlotEdited(string text)
        {
            string raw = text ?? string.Empty;
            this.shopQuickBuySlotIdInput = raw;
            UguiShellFeaturesAutoBuyHandle handle = this.uguiShellFeaturesAutoBuy;
            if (handle != null)
            {
                handle.QuickBuySlotSeen = raw;
            }
        }

        private void OnUguiFeaturesAutoBuyQuickBuyItemEdited(string text)
        {
            string raw = text ?? string.Empty;
            this.shopQuickBuyItemIdInput = raw;
            UguiShellFeaturesAutoBuyHandle handle = this.uguiShellFeaturesAutoBuy;
            if (handle != null)
            {
                handle.QuickBuyItemSeen = raw;
            }
        }

        // Gui.cs:1225-1228 — NO notification here (the backend notifies internally). The three
        // shared strings are re-read from the fields first (Teleport-XYZ click-time-read
        // insurance — file header); StartShopQuickBuyOpenPanel parses them itself.
        private void OnUguiFeaturesAutoBuyOpenBuyPanelClicked()
        {
            UguiShellFeaturesAutoBuyHandle handle = this.uguiShellFeaturesAutoBuy;
            if (handle != null)
            {
                try
                {
                    if (handle.QuickBuyStoreField != null)
                    {
                        this.shopQuickBuyStoreIdInput = handle.QuickBuyStoreField.text ?? string.Empty;
                        handle.QuickBuyStoreSeen = this.shopQuickBuyStoreIdInput;
                    }
                    if (handle.QuickBuySlotField != null)
                    {
                        this.shopQuickBuySlotIdInput = handle.QuickBuySlotField.text ?? string.Empty;
                        handle.QuickBuySlotSeen = this.shopQuickBuySlotIdInput;
                    }
                    if (handle.QuickBuyItemField != null)
                    {
                        this.shopQuickBuyItemIdInput = handle.QuickBuyItemField.text ?? string.Empty;
                        handle.QuickBuyItemSeen = this.shopQuickBuyItemIdInput;
                    }
                }
                catch { }
            }
            this.StartShopQuickBuyOpenPanel();
        }

        // Gui.cs:1235-1259 — the two manual free-text stores, same shape as quick-buy.
        private void OnUguiFeaturesAutoBuyManualIdEdited(string text)
        {
            string raw = text ?? string.Empty;
            this.forceOpenShopManualStoreIdInput = raw;
            UguiShellFeaturesAutoBuyHandle handle = this.uguiShellFeaturesAutoBuy;
            if (handle != null)
            {
                handle.ManualIdSeen = raw;
            }
        }

        private void OnUguiFeaturesAutoBuyManualNameEdited(string text)
        {
            string raw = text ?? string.Empty;
            this.forceOpenShopManualStoreNameInput = raw;
            UguiShellFeaturesAutoBuyHandle handle = this.uguiShellFeaturesAutoBuy;
            if (handle != null)
            {
                handle.ManualNameSeen = raw;
            }
        }

        // Gui.cs:1239-1251 — click-time re-read (insurance), then the verbatim two-branch:
        // the SAME out-string into forceOpenShopStatus AND the notification, green/red.
        private void OnUguiFeaturesAutoBuyOpenIdClicked()
        {
            UguiShellFeaturesAutoBuyHandle handle = this.uguiShellFeaturesAutoBuy;
            if (handle != null && handle.ManualIdField != null)
            {
                try
                {
                    this.forceOpenShopManualStoreIdInput = handle.ManualIdField.text ?? string.Empty;
                    handle.ManualIdSeen = this.forceOpenShopManualStoreIdInput;
                }
                catch { }
            }
            if (this.TryOpenForceShopByManualStoreId(out string manualIdStatus))
            {
                this.forceOpenShopStatus = manualIdStatus;
                this.AddMenuNotification(manualIdStatus, new Color(0.45f, 1f, 0.55f));
            }
            else
            {
                this.forceOpenShopStatus = manualIdStatus;
                this.AddMenuNotification(manualIdStatus, new Color(1f, 0.55f, 0.55f));
            }
        }

        // Gui.cs:1260-1272 — same verbatim two-branch for the name path.
        private void OnUguiFeaturesAutoBuyOpenNameClicked()
        {
            UguiShellFeaturesAutoBuyHandle handle = this.uguiShellFeaturesAutoBuy;
            if (handle != null && handle.ManualNameField != null)
            {
                try
                {
                    this.forceOpenShopManualStoreNameInput = handle.ManualNameField.text ?? string.Empty;
                    handle.ManualNameSeen = this.forceOpenShopManualStoreNameInput;
                }
                catch { }
            }
            if (this.TryOpenForceShopByManualStoreName(out string manualNameStatus))
            {
                this.forceOpenShopStatus = manualNameStatus;
                this.AddMenuNotification(manualNameStatus, new Color(0.45f, 1f, 0.55f));
            }
            else
            {
                this.forceOpenShopStatus = manualNameStatus;
                this.AddMenuNotification(manualNameStatus, new Color(1f, 0.55f, 0.55f));
            }
        }
    }
}
