using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, New Features round 1 of 8 (migration plan item 12): the
    // ANIMAL CARE sub-tab — DrawAnimalCareTab (AnimalCareFeature.cs:387-392), a thin delegator
    // over DrawWildAnimalFeedSection (WildAnimalFeedFeature.cs:3804-3859) and
    // DrawWildAnimalGiftSection (WildAnimalGiftFeature.cs:774-804). This round also establishes
    // the New Features tab's shell wiring (UguiShellNewFeaturesTabIndex + the
    // IsUguiShellNewFeaturesSubTabActive gate) that the seven future rounds (Daily Quests,
    // Homeland Farm, Pictures, Ice Skating, Extra, Sand Sculpture, Sea Clean — display subs 1-7,
    // still shell placeholders) will reuse, the same way Foraging's round 1 did for Resource
    // Gathering.
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawers and every backend method they call stay fully functional and
    //    untouched — this file only READS the same fields and CALLS the same action methods
    //    (all directly on HeartopiaComplete; ZERO backend interop additions this round). Two
    //    independent rendering paths over one backend.
    //  - Wiring is by STATIC display-position index (UguiShellNewFeaturesTabIndex = 3 +
    //    UguiShellAnimalCareSubIndex = 0, declared next to their siblings in
    //    UguiShellTabIndices.cs — see the derivation there), never label comparison.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // The 3 trough toggles are flag-only in the source (WildAnimalFeedFeature.cs:3820-3838 —
    // plain assignments, no SaveKeybinds, no AddMenuNotification): the change handlers here
    // write ONLY the bool. Verified against the IMGUI drawer — do not add a save or a toast.
    //
    // NEW pattern this round — LIVE-TIMER busy gates. Both primary buttons disable while
    //   feed: wildAnimalFeedCoroutine != null || Time.realtimeSinceStartup < wildAnimalFeedBusyUntil
    //   gift: wildAnimalGiftCoroutine != null || Time.realtimeSinceStartup < wildAnimalGiftBusyUntil
    // (WildAnimalFeedFeature.cs:3845 / WildAnimalGiftFeature.cs:789 — two INDEPENDENT
    // coroutine/timer pairs; never conflate them). Every prior round's busy gate was a plain
    // bool, but these conditions embed a live Time comparison AND a coroutine reference that
    // both change from background activity — so the processor recomputes the FULL condition
    // every gated frame (exactly like IMGUI's per-frame GUI.enabled) and a button disabled on
    // tab entry re-enables ON ITS OWN when the timer lapses / the coroutine ends, with zero
    // user input. SetUguiButtonInteractable self-diffs the property write, so the per-frame
    // call is a cheap compare except on actual transitions. Both Start* methods are also
    // internally guarded (coroutine-null + cooldown checks), so a same-frame race click is
    // harmless — same as the IMGUI twin.
    //
    // Presentation: the source's own three-box split replayed exactly — a headed TROUGHS card,
    // an UNLABELED action card (header passed as "" — the source draws no label on it), and a
    // headed GIFTS card, all via the shared CreateUguiSettingsMainPanel chrome (Foraging-round
    // precedent for card sections). The two status labels are NOT children of the cards: the
    // IMGUI drawers paint them on the tab background at the card's left edge BELOW each action
    // card (feed :3856, gift :801) — mirrored as free labels on the scroll content. Card
    // headers "WILD ANIMAL TROUGHS" / "WILD ANIMAL GIFTS" and the toggle/button labels all
    // go through L().
    //
    // Positions replay the source's cursor math (content top margin 8 standing in for startY,
    // the Foraging convention), EXCEPT the troughs card height — the source's 198 left ~46px of
    // empty card under the last checkbox, trimmed 2026-07-29 to the real extent (last checkbox
    // ends 124+28 = 152, +16 bottom pad = 168), so everything below sits 30 higher than the
    // source cursor:
    //   troughs card   y=8    h=168  (toggles at +40/+82/+124, rows 300/300/280 x 28)
    //   +180 (168+12) → action card y=188 h=74 (button at +16,+22, 200x32)
    //   += 84  →  feed status y=272 (520x36 in source → panelW x 36)
    //   += 44  →  gifts card  y=316 h=74 (header +12, button at +16,+34, 220x32)
    //   += 84  →  gift status y=400
    //   += 44  →  444; + the source's trailing 40 → content height 484.
    // 484 < the ~520px visible cell, so the scroll view (kept for consistency with every other
    // tab's content file) effectively never scrolls at the default shell size.
    //
    // Cross-surface sync cadence (Insects/Birds shape — no 0.5s tier, nothing here allocates):
    // every gated frame (shell visible + New Features tab + Animal Care sub-tab) re-sync the 3
    // toggles (SetIsOnWithoutNotify via SyncUguiToggleFromField — external IMGUI edits), the 2
    // busy gates (recomputed live, see above), and the 2 status labels (cached-string diffs —
    // they change from background coroutine activity). Per-frame sync disabled after 3
    // consecutive errors (LIVE rail idiom).
    //
    // ROSTER card (added below the gifts status; no IMGUI twin — this surface is UGUI-only).
    // Lists every unlocked animal with its live trough fullness and its favourite foods, plus a
    // per-row button that opens the GAME's own feeding panel for that animal
    // (WildAnimalRosterFeature.cs — AnimalFeedPanelLogic.StartLogic on the trough's netId). Rows
    // are DYNAMIC, so they follow the Research tab's rail rather than this file's fixed-position
    // one: the backend snapshot is refreshed on its own 2s throttle, a signature over the fully
    // computed row text is diffed, and only a real change destroys + rebuilds the rows. The card
    // height and the scroll content height are recomputed from the row count on every rebuild —
    // everything above the card keeps its fixed positions untouched.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellNewFeaturesAnimalCareHandle
        {
            public GameObject Root;

            // TROUGHS card — 3 flag-only switch toggles
            public Toggle PreferFavoritesToggle;
            public Toggle SkipFiveStarToggle;
            public Toggle SkipEggToggle;

            // Feed action card + its free-floating status line
            public GameObject FeedButton;         // busy-gated (live-timer condition, file header)
            public GameObject FeedStatusLabel;
            public string FeedStatusShown;

            // GIFTS card + its free-floating status line
            public GameObject GiftButton;         // busy-gated (independent coroutine/timer pair)
            public GameObject GiftStatusLabel;
            public string GiftStatusShown;

            // ROSTER card — dynamic rows (Research rail: computed-signature diff, see file header)
            public GameObject RosterPanel;        // resized on every row rebuild
            public Transform RosterRoot;          // rows live under the card itself
            public readonly List<GameObject> RosterRows = new List<GameObject>();
            public Transform ScrollContent;       // needed to re-set the content height per rebuild
            public float PanelWidth;
            public string RosterSignature;        // null = never populated

            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellNewFeaturesAnimalCareHandle uguiShellNewFeaturesAnimalCare;

        // ----------------------------------------------------------------------------------------
        // Shared gate: is the shell showing a specific New Features sub-tab right now?
        // (Foraging's IsUguiShellResourceGatheringSubTabActive shape, pointed at this tab —
        // future New Features rounds gate their processors on this same function.)
        // ----------------------------------------------------------------------------------------

        private bool IsUguiShellNewFeaturesSubTabActive(int subIndex)
        {
            try
            {
                UguiShellHandle shell = this.uguiShell;
                if (shell == null || shell.ActiveIndex != UguiShellNewFeaturesTabIndex
                    || !this.IsUguiWindowVisible(shell.Window))
                {
                    return false;
                }
                UguiTabBarHandle bar = (UguiShellNewFeaturesTabIndex < shell.SubTabBars.Count)
                    ? shell.SubTabBars[UguiShellNewFeaturesTabIndex]
                    : null;
                return bar != null && bar.ActiveIndex == subIndex;
            }
            catch
            {
                return false;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Busy conditions — the EXACT source expressions (feed :3845, gift :789). Recomputed on
        // every call; never cache the result (live Time comparison + background coroutine ref).
        // ----------------------------------------------------------------------------------------

        private bool IsUguiAnimalCareFeedBusy()
        {
            return this.wildAnimalFeedCoroutine != null
                || Time.realtimeSinceStartup < this.wildAnimalFeedBusyUntil;
        }

        private bool IsUguiAnimalCareGiftBusy()
        {
            return this.wildAnimalGiftCoroutine != null
                || Time.realtimeSinceStartup < this.wildAnimalGiftBusyUntil;
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawAnimalCareTab: three cards + two free status labels in a transparent
        // scroll view, every position replaying the IMGUI cursor chain ONCE at build time (all
        // heights fixed — no relayout machinery). Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellNewFeaturesAnimalCareContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellNewFeaturesAnimalCare = null;

            UguiShellNewFeaturesAnimalCareHandle handle = new UguiShellNewFeaturesAnimalCareHandle();
            GameObject block = this.CreateUguiGo("NewFeaturesAnimalCareContent", parent);
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

            float contentWidth = w - 22f; // viewport insets: 4 left + 18 right
            float panelW = contentWidth - 16f; // panels at x=8, 8px right margin

            // IMGUI statusStyle for both status lines: fontSize 11, wordWrap, uiText @ 0.82
            // (feed :3853-3854, gift :798-799 — identical construction in both drawers).
            Color statusColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.82f);

            // -------- WILD ANIMAL TROUGHS card (feed :3814-3838) --------
            // The kit panel chrome carries the header. 2026-07-29: was the source's fixed 198 —
            // ~46px of empty card under the last checkbox. Now the real extent: last checkbox
            // ends at 124+28 = 152, +16 bottom pad = 168. Everything below rose by the same 30.
            GameObject troughs = this.CreateUguiSettingsMainPanel(scrollContent, "TroughsPanel", this.L("WILD ANIMAL TROUGHS"));
            PlaceUguiTopLeft(troughs, 8f, 8f, panelW, 168f);

            // Rows replay the source rowY chain: +40, then += 42 twice; widths 300/300/280.
            handle.PreferFavoritesToggle = this.CreateUguiCheckbox(troughs.transform, "PreferFavorites",
                this.L("Prefer Favorite Food"), this.wildAnimalFeedPreferFavorites,
                new System.Action<bool>(this.OnUguiAnimalCarePreferFavoritesToggled));
            PlaceUguiTopLeft(handle.PreferFavoritesToggle.gameObject, 16f, 40f, 300f, 28f);

            handle.SkipFiveStarToggle = this.CreateUguiCheckbox(troughs.transform, "SkipFiveStar",
                this.L("Skip 5 Star Food"), this.wildAnimalFeedSkipFiveStarFood,
                new System.Action<bool>(this.OnUguiAnimalCareSkipFiveStarToggled));
            PlaceUguiTopLeft(handle.SkipFiveStarToggle.gameObject, 16f, 82f, 300f, 28f);

            handle.SkipEggToggle = this.CreateUguiCheckbox(troughs.transform, "SkipEgg",
                this.L("Skip Egg"), this.wildAnimalFeedSkipEgg,
                new System.Action<bool>(this.OnUguiAnimalCareSkipEggToggled));
            PlaceUguiTopLeft(handle.SkipEggToggle.gameObject, 16f, 124f, 280f, 28f);

            // -------- Unlabeled feed action card (h=74, :3841-3851; source cursor y=218, now
            // 188 = troughs bottom 176 + the chain's 12px gap after the troughs trim) --------
            GameObject feedAction = this.CreateUguiSettingsMainPanel(scrollContent, "FeedActionPanel", string.Empty);
            PlaceUguiTopLeft(feedAction, 8f, 188f, panelW, 74f);

            handle.FeedButton = this.CreateUguiPrimaryButton(feedAction.transform, "FeedAllButton",
                this.L("Feed All Troughs"), new System.Action(this.OnUguiAnimalCareFeedAllClicked));
            PlaceUguiTopLeft(handle.FeedButton, 16f, 22f, 200f, 32f);
            this.SetUguiButtonInteractable(handle.FeedButton, !this.IsUguiAnimalCareFeedBusy());

            // Feed status — OUTSIDE the card in the source (:3856, card-left aligned); the
            // per-frame sync owns its text from here on.
            handle.FeedStatusShown = this.wildAnimalFeedLastStatus ?? string.Empty;
            handle.FeedStatusLabel = this.CreateUguiLabel(scrollContent, "FeedStatus",
                handle.FeedStatusShown, 11f, statusColor, false);
            this.TrySetUguiLabelWrapped(handle.FeedStatusLabel);
            PlaceUguiTopLeft(handle.FeedStatusLabel, 8f, 272f, panelW, 36f);

            // -------- WILD ANIMAL GIFTS card (feed's source cursor returned 346, now 316 after
            // the troughs trim; h=74, gift :784-796) --------
            GameObject gifts = this.CreateUguiSettingsMainPanel(scrollContent, "GiftsPanel", this.L("WILD ANIMAL GIFTS"));
            PlaceUguiTopLeft(gifts, 8f, 316f, panelW, 74f);

            handle.GiftButton = this.CreateUguiPrimaryButton(gifts.transform, "ClaimGiftsButton",
                this.L("Claim All Wild Gifts"), new System.Action(this.OnUguiAnimalCareClaimGiftsClicked));
            PlaceUguiTopLeft(handle.GiftButton, 16f, 34f, 220f, 32f);
            this.SetUguiButtonInteractable(handle.GiftButton, !this.IsUguiAnimalCareGiftBusy());

            // Gift status — same free-label placement (:801).
            handle.GiftStatusShown = this.wildAnimalGiftLastStatus ?? string.Empty;
            handle.GiftStatusLabel = this.CreateUguiLabel(scrollContent, "GiftStatus",
                handle.GiftStatusShown, 11f, statusColor, false);
            this.TrySetUguiLabelWrapped(handle.GiftStatusLabel);
            PlaceUguiTopLeft(handle.GiftStatusLabel, 8f, 400f, panelW, 36f);

            // -------- ROSTER card (UGUI-only; rows are dynamic, see the file header) --------
            // Placed at 444 = gift status end (436) + the 8px gap this chain uses. Its height and
            // the scroll content height are (re)computed by RebuildUguiShellAnimalCareRosterRows;
            // the initial call below also covers the "backend not ready yet" hint row.
            handle.RosterPanel = this.CreateUguiSettingsMainPanel(scrollContent, "RosterPanel", this.L("WILD ANIMAL ROSTER"));
            handle.RosterRoot = handle.RosterPanel.transform;
            handle.ScrollContent = scrollContent;
            handle.PanelWidth = panelW;

            this.RefreshWildAnimalRosterIfDue();
            handle.RosterSignature = this.BuildUguiShellAnimalCareRosterSignature();
            this.RebuildUguiShellAnimalCareRosterRows(handle);

            handle.Root = block;
            this.uguiShellNewFeaturesAnimalCare = handle;
            return block;
        }

        // Content-space Y where the roster card starts (gift status ends at 436 + the chain's 8px
        // gap). Everything above it keeps the fixed positions documented in the file header.
        private const float UguiAnimalCareRosterTopY = 444f;

        private const float UguiAnimalCareRosterRowHeight = 42f;
        private const float UguiAnimalCareRosterRowsTopY = 34f;   // card-local, under the header
        private const float UguiAnimalCareRosterButtonWidth = 110f;

        // Signature over the FULLY COMPUTED row text (Research rail): any change in the group set,
        // a fullness tick, or a late-resolving favourite list rebuilds; an idle roster rebuilds
        // nothing. The status string joins the signature ONLY in the empty state, where it IS the
        // painted row — with rows present it is toast-only, and folding it in would rebuild every
        // row on each button click for no visible change.
        private string BuildUguiShellAnimalCareRosterSignature()
        {
            if (this.wildAnimalRosterEntries.Count == 0)
            {
                return "empty:" + (this.wildAnimalRosterStatus ?? string.Empty);
            }

            StringBuilder sb = new StringBuilder(this.wildAnimalRosterEntries.Count * 48 + 32);
            for (int i = 0; i < this.wildAnimalRosterEntries.Count; i++)
            {
                WildAnimalRosterEntry entry = this.wildAnimalRosterEntries[i];
                sb.Append(entry.GroupId).Append('|')
                  .Append(entry.GroupName).Append('|')
                  .Append(entry.Fullness).Append('/').Append(entry.Capacity).Append('|')
                  .Append(entry.Favorites).Append('\n');
            }
            return sb.ToString();
        }

        // Destroys + rebuilds the roster rows, then resizes the card AND the scroll content from
        // the resulting row count. Only called from the initial build or on a signature change.
        private void RebuildUguiShellAnimalCareRosterRows(UguiShellNewFeaturesAnimalCareHandle handle)
        {
            for (int i = 0; i < handle.RosterRows.Count; i++)
            {
                GameObject row = handle.RosterRows[i];
                if (row != null)
                {
                    try { Object.Destroy(row); } catch { }
                }
            }
            handle.RosterRows.Clear();

            Transform root = handle.RosterRoot;
            if (root == null)
            {
                return;
            }

            float panelW = handle.PanelWidth;
            float cardHeight;

            if (this.wildAnimalRosterEntries.Count == 0)
            {
                // Empty state: the backend's own status line (not ready / no unlocked animals /
                // resolve error) rather than a generic placeholder.
                string hint = string.IsNullOrEmpty(this.wildAnimalRosterStatus)
                    ? this.L("Loading animal data…")
                    : this.wildAnimalRosterStatus;
                GameObject empty = this.CreateUguiLabel(root, "RosterEmpty", hint, 11f,
                    this.UguiKitMutedColor(), false);
                this.TrySetUguiLabelWrapped(empty);
                PlaceUguiTopLeft(empty, 16f, UguiAnimalCareRosterRowsTopY, panelW - 32f, 34f);
                handle.RosterRows.Add(empty);
                cardHeight = UguiAnimalCareRosterRowsTopY + 34f + 12f;
            }
            else
            {
                float buttonX = panelW - 16f - UguiAnimalCareRosterButtonWidth;
                float favoritesWidth = Mathf.Max(80f, buttonX - 16f - 8f);
                Color muted = this.UguiKitMutedColor();
                float yCur = UguiAnimalCareRosterRowsTopY;

                for (int i = 0; i < this.wildAnimalRosterEntries.Count; i++)
                {
                    WildAnimalRosterEntry entry = this.wildAnimalRosterEntries[i];

                    // Animals outside their visiting window never reach this list — the backend
                    // filters them out (RefreshWildAnimalRoster pass 2), so every row here is
                    // present and feedable.
                    GameObject name = this.CreateUguiBodyLabel(root, "RosterName" + i, entry.GroupName, 12f);
                    PlaceUguiTopLeft(name, 16f, yCur, 150f, 20f);
                    handle.RosterRows.Add(name);

                    GameObject fullness = this.CreateUguiLabel(root, "RosterFullness" + i,
                        entry.Capacity > 0 ? entry.Fullness + " / " + entry.Capacity : "—",
                        12f, UguiAnimalCareFullnessColor(entry.Fullness, entry.Capacity), false);
                    PlaceUguiTopLeft(fullness, 172f, yCur, 86f, 20f);
                    handle.RosterRows.Add(fullness);

                    // Capture a copy for the click closure — the entry object is replaced wholesale
                    // on every backend refresh.
                    int groupIdCopy = entry.GroupId;
                    GameObject open = this.CreateUguiSecondaryButton(root, "RosterOpen" + i, this.L("FEED PANEL"),
                        new System.Action(() => this.StartWildAnimalOpenFeedPanel(groupIdCopy)));
                    PlaceUguiTopLeft(open, buttonX, yCur - 2f, UguiAnimalCareRosterButtonWidth, 26f);
                    handle.RosterRows.Add(open);

                    GameObject favorites = this.CreateUguiLabel(root, "RosterFavorites" + i,
                        string.IsNullOrEmpty(entry.Favorites)
                            ? this.L("Favorites: —")
                            : this.LF("Favorites: {0}", entry.Favorites),
                        10f, new Color(muted.r, muted.g, muted.b, 0.9f), false);
                    PlaceUguiTopLeft(favorites, 16f, yCur + 20f, favoritesWidth, 18f);
                    handle.RosterRows.Add(favorites);

                    yCur += UguiAnimalCareRosterRowHeight;
                }

                cardHeight = yCur + 12f;
            }

            PlaceUguiTopLeft(handle.RosterPanel, 8f, UguiAnimalCareRosterTopY, panelW, cardHeight);

            // Extent = roster card end + the source DrawAnimalCareTab's own 40px trailing pad.
            this.SetUguiScrollContentHeight(handle.ScrollContent, UguiAnimalCareRosterTopY + cardHeight + 40f);
        }

        // Green when full, amber while it still has room, red when the trough is (near) empty —
        // the same three-band read the game's own trough mesh uses (FeedTroughComponent stages).
        private static Color UguiAnimalCareFullnessColor(int fullness, int capacity)
        {
            if (capacity <= 0)
            {
                return new Color(0.7f, 0.7f, 0.7f, 0.9f);
            }

            float ratio = Mathf.Clamp01(fullness / (float)capacity);
            if (ratio >= 1f)
            {
                return new Color(0.45f, 1f, 0.55f);
            }

            return ratio < 0.2f ? new Color(1f, 0.55f, 0.45f) : new Color(1f, 0.85f, 0.45f);
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellNewFeaturesAnimalCareOnUpdate()
        {
            UguiShellNewFeaturesAnimalCareHandle handle = this.uguiShellNewFeaturesAnimalCare;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellNewFeaturesSubTabActive(UguiShellAnimalCareSubIndex))
            {
                return;
            }

            try
            {
                // Toggle re-syncs (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.PreferFavoritesToggle, this.wildAnimalFeedPreferFavorites);
                this.SyncUguiToggleFromField(handle.SkipFiveStarToggle, this.wildAnimalFeedSkipFiveStarFood);
                this.SyncUguiToggleFromField(handle.SkipEggToggle, this.wildAnimalFeedSkipEgg);

                // Busy gates — the FULL live conditions recomputed EVERY gated frame (file
                // header: coroutine refs and timers change from background activity; a disabled
                // button must re-enable on its own). SetUguiButtonInteractable self-diffs the
                // actual Button write.
                this.SetUguiButtonInteractable(handle.FeedButton, !this.IsUguiAnimalCareFeedBusy());
                this.SetUguiButtonInteractable(handle.GiftButton, !this.IsUguiAnimalCareGiftBusy());

                // Status lines — background coroutines rewrite these; cached-string diffs.
                this.SyncUguiSelfLabelText(handle.FeedStatusLabel, ref handle.FeedStatusShown,
                    this.wildAnimalFeedLastStatus ?? string.Empty);
                this.SyncUguiSelfLabelText(handle.GiftStatusLabel, ref handle.GiftStatusShown,
                    this.wildAnimalGiftLastStatus ?? string.Empty);

                // Roster rows — the backend snapshot has its own 2s throttle (and only ticks while
                // this tab is on screen, since this is its only caller); the rebuild below fires
                // only when the computed row text actually changed.
                this.RefreshWildAnimalRosterIfDue();
                string rosterSignature = this.BuildUguiShellAnimalCareRosterSignature();
                if (!string.Equals(rosterSignature, handle.RosterSignature, StringComparison.Ordinal))
                {
                    handle.RosterSignature = rosterSignature;
                    this.RebuildUguiShellAnimalCareRosterRows(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] NewFeatures/AnimalCare content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order)
        // ----------------------------------------------------------------------------------------

        // WildAnimalFeedFeature.cs:3820-3824 — flag only: no save, no notification (file header).
        private void OnUguiAnimalCarePreferFavoritesToggled(bool value)
        {
            this.wildAnimalFeedPreferFavorites = value;
        }

        // WildAnimalFeedFeature.cs:3827-3831 — flag only.
        private void OnUguiAnimalCareSkipFiveStarToggled(bool value)
        {
            this.wildAnimalFeedSkipFiveStarFood = value;
        }

        // WildAnimalFeedFeature.cs:3834-3838 — flag only.
        private void OnUguiAnimalCareSkipEggToggled(bool value)
        {
            this.wildAnimalFeedSkipEgg = value;
        }

        // WildAnimalFeedFeature.cs:3847-3850 — the button routes straight to StartWildAnimalFeedAll
        // (internally guarded against re-entry/cooldown, so a same-frame race click is safe).
        private void OnUguiAnimalCareFeedAllClicked()
        {
            this.StartWildAnimalFeedAll(silent: false);
        }

        // WildAnimalGiftFeature.cs:791-794 — same shape over the gift section's own backend.
        private void OnUguiAnimalCareClaimGiftsClicked()
        {
            this.StartWildAnimalClaimAllGifts(silent: false);
        }
    }
}
