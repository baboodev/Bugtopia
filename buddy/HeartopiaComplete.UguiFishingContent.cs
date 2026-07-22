using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, Resource Gathering round 2 of 4 (migration plan:
    // cosmic-waddling-rainbow.md, item 6): the FISHING sub-tab only — AutoFishingFarm.DrawSection
    // (AutoFishingFarm.cs:641-790) plus FishingRouteFeature.DrawSection (FishingRouteFeature.cs),
    // which IMGUI embeds right below it in the same scrolling flow. Insects/Birds are separate
    // follow-up rounds and are untouched (their cells stay shell placeholders).
    //
    // Ground rules (same as Foraging and every prior round):
    //  - Both IMGUI drawers and every backend method they call stay fully functional and
    //    untouched — this file only READS the same state and CALLS the same action methods.
    //    Two independent rendering paths over one backend. The only backend additions this round
    //    are purely-additive interop: AutoFishingFarm.SetDetectRangeFromUi (the IMGUI slider
    //    block's round/status/log behavior as a callable) and FishingRouteFeature's read-only
    //    accessors + SaveCurrentLocationFromUi + RemoveCustomSpotAt (the removal-plus-index-fixup
    //    extracted so BOTH surfaces share one implementation).
    //  - Wiring is by STATIC display-position index (UguiShellResourceGatheringTabIndex = 1 +
    //    UguiShellFishingSubIndex = 1, declared next to their siblings in UguiPhase3Content.cs;
    //    Fishing = autoFarmSubTab 1 per DrawAutoFarmTab's dispatcher), never label comparison.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own.
    //
    // Presentation: unlike Foraging's three bordered cards, the IMGUI source draws a FLAT list of
    // controls straight onto the tab background (no DrawExentriSectionPanel anywhere) — so this
    // is one flat stack inside a transparent scroll view (Logging idiom: alpha-0 images still
    // raycast, so wheel/drag scrolling works over the block's own ContentBg). The layout replays
    // the IMGUI functions' own `num +=` cursor chain, including the `num + 10f` hand-off between
    // the two DrawSections. Everything above the Auto Bait toggle never moves (positioned once in
    // the builder); everything at/below the Auto Bait conditional block is owned by
    // RelayoutUguiShellFishing (relayout-on-signature-change, Foraging shape — one longer chain
    // instead of three panels).
    //
    // Cross-surface sync cadence (established split):
    //  - Every gated frame: toggle/slider re-syncs (SetIsOnWithoutNotify/SetValueWithoutNotify —
    //    NEVER the plain setters, which would replay side effects like SetEnabled's session-state
    //    reset or SetAutoBaitEnabled's counter reset), the Status/Tool/Target readouts (pure
    //    string-remapping formatters, no side effects — IMGUI formats them per drawn frame too),
    //    slider value labels and button labels (cached-string diffs), and the layout signature
    //    (autoBait / route active / paused / custom-only / spot count).
    //  - 0.5s tick (NextSlowSyncAt idiom): the custom-spot count probe — the list is private to
    //    FishingRouteFeature, so the only read is ExportCustomSpots(), which COPIES entries; that
    //    allocation belongs on the throttle, not per frame. Save/Delete clicks from THIS surface
    //    refresh immediately instead of waiting for the probe.
    //
    // The custom-spot rows reuse the Teleport-Custom CRUD idiom (destroy-all + rebuild-from-0 on
    // count change — a small user-curated list, deliberately NOT the Bag/Warehouse virtualized
    // pool) but with row shape (b): a non-clickable name+coords LABEL plus one trailing danger
    // "X" button, matching the source's GUI.Label rows — NOT Teleport-Custom's labelless
    // two-button rows. Deletion routes through FishingRouteFeature.RemoveCustomSpotAt so the
    // route-pointer fixup (the one genuinely delicate piece here) has exactly one implementation.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellFishingHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float ContentWidth;            // scroll content width (block w minus viewport insets)

            // Part A — Auto Fishing Farm (fixed stack; only the Auto Bait block below moves)
            public Toggle ShadowNetToggle;
            public Toggle InstantCatchToggle;
            public Toggle SkipCatchToggle;
            public Toggle SkipCastToggle;
            public Toggle SkipBaitToggle;
            public GameObject StatusLabel;
            public string StatusShown;
            public GameObject ToolLabel;
            public string ToolShown;
            public GameObject TargetLabel;        // wraps to 2 lines (36px in source vs 20px)
            public string TargetShown;
            public GameObject ScanRangeLabel;
            public string ScanRangeShown;
            public Slider ScanRangeSlider;
            public Toggle AutoBaitToggle;

            // Auto Bait conditional block (visible only while Auto Bait is on)
            public GameObject BaitChoiceButton;   // label cycles "Item: Bait" / "Item: Attractor"
            public string BaitChoiceShown;
            public GameObject BaitMaxLabel;
            public string BaitMaxShown;
            public Slider BaitMaxSlider;
            public GameObject NoFishLabel;
            public string NoFishShown;
            public Slider NoFishSlider;
            public GameObject RemainingLabel;
            public string RemainingShown;
            public GameObject BaitResetButton;

            // Part B — Fishing Locations (everything moves with the Auto Bait block above it)
            public GameObject RouteHeader;
            public GameObject RouteActionButton;  // label swaps Start/Stop Fishing Locations
            public string RouteActionShown;
            public Toggle CustomOnlyToggle;
            public GameObject NoCustomHintLabel;  // custom-only on + zero spots
            public GameObject LocationLabel;      // active-only
            public string LocationShown;
            public GameObject RouteStatusLabel;   // active-only
            public string RouteStatusShown;
            public GameObject PausedLabel;        // active + pausedForRepair
            public GameObject SaveLocationButton;
            public GameObject CustomCountLabel;   // list-only
            public string CustomCountShown;
            public readonly List<GameObject> SpotRows = new List<GameObject>();
            public int CustomSpotsCount;          // 0.5s-probe cache (+ forced after Save/Delete)

            public int LayoutSignature = -1;      // packed autoBait/active/paused/customOnly/count
            public float NextSlowSyncAt;          // 0.5s tick
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellFishingHandle uguiShellFishing;

        // Fixed Part A cursor bottom: 8 header +28, equip +42, five toggles +30 each, status +24,
        // tool +24, target +40, range label +22, range slider +30, auto-bait toggle +30 → 398.
        private const float UguiFishingBaitBlockTop = 398f;

        // The IMGUI No-fish slider bounds — mirror AutoFishingFarm.AutoBaitNoFishSecondsMin/Max
        // (private consts 3f/60f; a numeric range, deliberately not exposed for this).
        private const float UguiFishingNoFishMin = 3f;
        private const float UguiFishingNoFishMax = 60f;

        // ----------------------------------------------------------------------------------------
        // Small display builders — IMGUI strings verbatim (only the L()-localized ones go through
        // L, matching each IMGUI call site exactly; AddMenuNotification localizes internally, so
        // notification handlers pass the same strings the IMGUI twin passes).
        // ----------------------------------------------------------------------------------------

        private string BuildUguiFishingBaitChoiceText()
        {
            // AutoFishingFarm.cs:747-749 (AutoBaitChoice: Bait=0, Attractor=1).
            return AutoFishingFarm.GetAutoBaitChoice() == 0
                ? this.L("Item: Bait")
                : this.L("Item: Attractor");
        }

        private string BuildUguiFishingRouteActionText()
        {
            // FishingRouteFeature.cs draw — DrawPrimaryActionButton localizes internally.
            return FishingRouteFeature.Active
                ? this.L("Stop Fishing Locations")
                : this.L("Start Fishing Locations");
        }

        private string BuildUguiFishingLocationText()
        {
            return this.LF("Location: {0}/{1} ({2})",
                FishingRouteFeature.CurrentIndex + 1,
                FishingRouteFeature.TotalSpotCount,
                FishingRouteFeature.GetSpotName(FishingRouteFeature.CurrentIndex));
        }

        private string BuildUguiFishingRouteStatusText()
        {
            // Source: UI_LocalizeFormat("Route: {0}", UI_Localize(lastStatus)) — the INNER value
            // is localized; the outer format has no other translatable text.
            return "Route: " + this.L(FishingRouteFeature.LastStatus);
        }

        private int ComputeUguiFishingLayoutSignature(UguiShellFishingHandle handle)
        {
            return (AutoFishingFarm.GetAutoBaitEnabled() ? 1 : 0)
                 | (FishingRouteFeature.Active ? 2 : 0)
                 | (FishingRouteFeature.PausedForRepair ? 4 : 0)
                 | (FishingRouteFeature.GetCustomSpotsOnly() ? 8 : 0)
                 | (handle.CustomSpotsCount << 4);
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // One flat scrolling stack (the stack runs ~700-1000px against a ~520px cell; IMGUI
        // scrolls the whole tab via tabScrollPos). All controls — including conditional ones —
        // are built ONCE here; RelayoutUguiShellFishing owns every position at/below the Auto
        // Bait conditional plus the scroll content height. Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellFishingContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellFishing = null;

            UguiShellFishingHandle handle = new UguiShellFishingHandle();
            GameObject block = this.CreateUguiGo("FishingContent", parent);
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

            // ------------- Part A: fixed stack (AutoFishingFarm.DrawSection 641-744) -----------
            // IMGUI draws at x=20 with fixed control widths (260/280/320/360); x=16 here sits at
            // the same visual 20px after the 4px viewport inset. Positions replay the source's
            // own cursor chain — see UguiFishingBaitBlockTop for the running total.
            GameObject farmHeader = this.CreateUguiHeaderLabel(scrollContent, "FarmHeader",
                this.L("Auto Fishing Farm"), 14f);
            PlaceUguiTopLeft(farmHeader, 16f, 8f, 320f, 22f);

            // Source also calls AutoFishingFarm's private master-log Log() — debug output, not
            // user-visible behavior; deliberately not reproduced (same for every handler below).
            GameObject equipBtn = this.CreateUguiPrimaryButton(scrollContent, "EquipRod",
                this.L("Auto Equip Rod"), new System.Action(this.OnUguiFishingEquipRodClicked));
            PlaceUguiTopLeft(equipBtn, 16f, 36f, 260f, 35f);

            handle.ShadowNetToggle = this.CreateUguiCheckbox(scrollContent, "ShadowNetToggle",
                this.L("Auto Fish Shadow Net"), AutoFishingFarm.IsEnabled,
                new System.Action<bool>(this.OnUguiFishingShadowNetToggled));
            PlaceUguiTopLeft(handle.ShadowNetToggle.gameObject, 16f, 78f, 280f, 25f);

            handle.InstantCatchToggle = this.CreateUguiCheckbox(scrollContent, "InstantCatchToggle",
                this.L("Instant Catch"), AutoFishingFarm.GetInstantCatchEnabled(),
                new System.Action<bool>(this.OnUguiFishingInstantCatchToggled));
            PlaceUguiTopLeft(handle.InstantCatchToggle.gameObject, 16f, 108f, 280f, 25f);

            handle.SkipCatchToggle = this.CreateUguiCheckbox(scrollContent, "SkipCatchToggle",
                this.L("Skip Catch Animation"), AutoFishingFarm.GetSkipCatchAnimEnabled(),
                new System.Action<bool>(this.OnUguiFishingSkipCatchToggled));
            PlaceUguiTopLeft(handle.SkipCatchToggle.gameObject, 16f, 138f, 280f, 25f);

            handle.SkipCastToggle = this.CreateUguiCheckbox(scrollContent, "SkipCastToggle",
                this.L("Skip Cast Animation"), AutoFishingFarm.GetSkipCastAnimEnabled(),
                new System.Action<bool>(this.OnUguiFishingSkipCastToggled));
            PlaceUguiTopLeft(handle.SkipCastToggle.gameObject, 16f, 168f, 280f, 25f);

            handle.SkipBaitToggle = this.CreateUguiCheckbox(scrollContent, "SkipBaitToggle",
                this.L("Skip Bait Animation"), AutoFishingFarm.GetSkipBaitAnimEnabled(),
                new System.Action<bool>(this.OnUguiFishingSkipBaitToggled));
            PlaceUguiTopLeft(handle.SkipBaitToggle.gameObject, 16f, 198f, 280f, 25f);

            // Status readouts — safe to seed at build time (pure formatters, unlike Foraging's
            // stop-cascade conditional). IMGUI `small` style = fontSize 12.
            handle.StatusShown = this.LF("Status: {0}", AutoFishingFarm.GetLastStatus());
            handle.StatusLabel = this.CreateUguiBodyLabel(scrollContent, "StatusLabel", handle.StatusShown, 12f);
            PlaceUguiTopLeft(handle.StatusLabel, 16f, 228f, 360f, 20f);

            handle.ToolShown = this.LF("Tool: {0}", AutoFishingFarm.GetLastToolStatus());
            handle.ToolLabel = this.CreateUguiBodyLabel(scrollContent, "ToolLabel", handle.ToolShown, 12f);
            PlaceUguiTopLeft(handle.ToolLabel, 16f, 252f, 360f, 20f);

            handle.TargetShown = this.LF("Target: {0}", AutoFishingFarm.GetLastTargetStatus());
            handle.TargetLabel = this.CreateUguiBodyLabel(scrollContent, "TargetLabel", handle.TargetShown, 12f);
            this.TrySetUguiLabelWrapped(handle.TargetLabel);
            PlaceUguiTopLeft(handle.TargetLabel, 16f, 276f, 360f, 36f);

            handle.ScanRangeShown = this.LF("Scan Range: {0:F0}m", AutoFishingFarm.GetDetectRange());
            handle.ScanRangeLabel = this.CreateUguiBodyLabel(scrollContent, "ScanRangeLabel", handle.ScanRangeShown, 12f);
            PlaceUguiTopLeft(handle.ScanRangeLabel, 16f, 316f, 320f, 20f);
            handle.ScanRangeSlider = this.CreateUguiSlider(scrollContent, "ScanRangeSlider",
                1f, 200f, AutoFishingFarm.GetDetectRange(), true,
                new System.Action<float>(this.OnUguiFishingScanRangeChanged));
            PlaceUguiTopLeft(handle.ScanRangeSlider.gameObject, 16f, 338f, 260f, 20f);

            handle.AutoBaitToggle = this.CreateUguiCheckbox(scrollContent, "AutoBaitToggle",
                this.L("Auto Bait"), AutoFishingFarm.GetAutoBaitEnabled(),
                new System.Action<bool>(this.OnUguiFishingAutoBaitToggled));
            PlaceUguiTopLeft(handle.AutoBaitToggle.gameObject, 16f, UguiFishingBaitBlockTop - 30f, 280f, 25f);

            // ------------- Auto Bait conditional block (positions owned by the relayout) --------
            handle.BaitChoiceShown = this.BuildUguiFishingBaitChoiceText();
            handle.BaitChoiceButton = this.CreateUguiSecondaryButton(scrollContent, "BaitChoiceButton",
                handle.BaitChoiceShown, new System.Action(this.OnUguiFishingBaitChoiceClicked));

            handle.BaitMaxShown = this.LF("Max: {0}", AutoFishingFarm.GetAutoBaitMaxCount());
            handle.BaitMaxLabel = this.CreateUguiBodyLabel(scrollContent, "BaitMaxLabel", handle.BaitMaxShown, 12f);
            // Slider range [0,50] = the SOURCE slider's own bounds (the setter clamps 0-999;
            // deliberately NOT widened to match it).
            handle.BaitMaxSlider = this.CreateUguiSlider(scrollContent, "BaitMaxSlider",
                0f, 50f, AutoFishingFarm.GetAutoBaitMaxCount(), true,
                new System.Action<float>(this.OnUguiFishingBaitMaxChanged));

            handle.NoFishShown = this.LF("No-fish: {0:F0}s", AutoFishingFarm.GetAutoBaitNoFishSeconds());
            handle.NoFishLabel = this.CreateUguiBodyLabel(scrollContent, "NoFishLabel", handle.NoFishShown, 12f);
            handle.NoFishSlider = this.CreateUguiSlider(scrollContent, "NoFishSlider",
                UguiFishingNoFishMin, UguiFishingNoFishMax, AutoFishingFarm.GetAutoBaitNoFishSeconds(), true,
                new System.Action<float>(this.OnUguiFishingNoFishChanged));

            handle.RemainingShown = this.LF("Remaining: {0}/{1}",
                AutoFishingFarm.GetAutoBaitRemaining(), AutoFishingFarm.GetAutoBaitMaxCount());
            handle.RemainingLabel = this.CreateUguiBodyLabel(scrollContent, "RemainingLabel", handle.RemainingShown, 12f);
            handle.BaitResetButton = this.CreateUguiSecondaryButton(scrollContent, "BaitResetButton",
                this.L("Reset"), new System.Action(this.OnUguiFishingBaitResetClicked));

            // ------------- Part B: Fishing Locations (FishingRouteFeature.DrawSection) ----------
            handle.RouteHeader = this.CreateUguiHeaderLabel(scrollContent, "RouteHeader",
                this.L("Fishing Locations"), 14f);

            handle.RouteActionShown = this.BuildUguiFishingRouteActionText();
            handle.RouteActionButton = this.CreateUguiPrimaryButton(scrollContent, "RouteActionButton",
                handle.RouteActionShown, new System.Action(this.OnUguiFishingRouteActionClicked));

            handle.CustomOnlyToggle = this.CreateUguiCheckbox(scrollContent, "CustomOnlyToggle",
                this.L("Custom Spots Only"), FishingRouteFeature.GetCustomSpotsOnly(),
                new System.Action<bool>(this.OnUguiFishingCustomOnlyToggled));

            handle.NoCustomHintLabel = this.CreateUguiLabel(scrollContent, "NoCustomHint",
                this.L("No custom spots saved - using all spots"), 12f, new Color(1f, 0.7f, 0.45f), false);

            handle.LocationShown = this.BuildUguiFishingLocationText();
            handle.LocationLabel = this.CreateUguiBodyLabel(scrollContent, "LocationLabel", handle.LocationShown, 12f);

            handle.RouteStatusShown = this.BuildUguiFishingRouteStatusText();
            handle.RouteStatusLabel = this.CreateUguiBodyLabel(scrollContent, "RouteStatusLabel", handle.RouteStatusShown, 12f);

            handle.PausedLabel = this.CreateUguiLabel(scrollContent, "PausedWarn",
                this.L("Teleport paused until repair finishes"), 12f, new Color(1f, 0.7f, 0.45f), false);

            handle.SaveLocationButton = this.CreateUguiSecondaryButton(scrollContent, "SaveLocationButton",
                this.L("Save Current Location"), new System.Action(this.OnUguiFishingSaveLocationClicked));

            handle.CustomCountLabel = this.CreateUguiBodyLabel(scrollContent, "CustomCountLabel", "", 12f);

            this.RebuildUguiShellFishingSpotRows(handle);
            handle.LayoutSignature = this.ComputeUguiFishingLayoutSignature(handle);
            this.RelayoutUguiShellFishing(handle);

            handle.Root = block;
            this.uguiShellFishing = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Custom-spot rows — Teleport-Custom CRUD idiom (destroy all + rebuild from 0). Rows are
        // created at y=0; RelayoutUguiShellFishing must ALWAYS run right after (every call site
        // does) — it owns the y positions, so create/position stay two halves of one refresh.
        // ----------------------------------------------------------------------------------------

        private void RebuildUguiShellFishingSpotRows(UguiShellFishingHandle handle)
        {
            for (int i = 0; i < handle.SpotRows.Count; i++)
            {
                GameObject row = handle.SpotRows[i];
                if (row != null)
                {
                    try { UnityEngine.Object.Destroy(row); } catch { }
                }
            }
            handle.SpotRows.Clear();

            // ExportCustomSpots copies (the live list is private to FishingRouteFeature); it also
            // filters nulls, so rows and count always agree.
            List<CustomTeleportEntry> spots = FishingRouteFeature.ExportCustomSpots();
            handle.CustomSpotsCount = spots.Count;
            this.SyncUguiSelfLabelText(handle.CustomCountLabel, ref handle.CustomCountShown,
                this.LF("Custom spots: {0}", spots.Count));

            Transform content = handle.ScrollContent;
            if (content == null)
            {
                return;
            }

            for (int i = 0; i < spots.Count; i++)
            {
                CustomTeleportEntry entry = spots[i];
                if (entry == null)
                {
                    continue;
                }
                Vector3 p = entry.position;
                int indexCopy = i; // capture copy for the click closure
                // Row shape (b): name+coords LABEL (the source uses GUI.Label, not a button) +
                // one trailing danger "X" — string unlocalized like the source's own row label.
                UguiListRowHandle row = this.CreateUguiListRow(content, "Spot" + i, 16f, 0f, 312f, 26f,
                    $"{entry.name} ({p.x:F0}, {p.y:F0}, {p.z:F0})",
                    null, null,
                    false, true, null,
                    new UguiListRowButtonSpec[]
                    {
                        new UguiListRowButtonSpec
                        {
                            Label = "X", Tier = UguiListRowTierDanger, Width = 26f, Enabled = true,
                            OnClick = new System.Action(() => this.OnUguiFishingSpotDeleteClicked(indexCopy))
                        }
                    });
                handle.SpotRows.Add(row.Root);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Relayout — the UGUI analog of both IMGUI cursor chains from the Auto Bait conditional
        // down (AutoFishingFarm.cs:745-790 + all of FishingRouteFeature.DrawSection), including
        // the `num + 10f` hand-off between them. Reposition/SetActive/resize only.
        // ----------------------------------------------------------------------------------------

        private void RelayoutUguiShellFishing(UguiShellFishingHandle handle)
        {
            bool autoBait = AutoFishingFarm.GetAutoBaitEnabled();
            bool routeActive = FishingRouteFeature.Active;
            bool paused = FishingRouteFeature.PausedForRepair;
            bool hintVisible = FishingRouteFeature.GetCustomSpotsOnly() && handle.CustomSpotsCount == 0;
            bool listVisible = handle.CustomSpotsCount > 0;

            SetUguiGoActive(handle.BaitChoiceButton, autoBait);
            SetUguiGoActive(handle.BaitMaxLabel, autoBait);
            SetUguiGoActive(handle.BaitMaxSlider != null ? handle.BaitMaxSlider.gameObject : null, autoBait);
            SetUguiGoActive(handle.NoFishLabel, autoBait);
            SetUguiGoActive(handle.NoFishSlider != null ? handle.NoFishSlider.gameObject : null, autoBait);
            SetUguiGoActive(handle.RemainingLabel, autoBait);
            SetUguiGoActive(handle.BaitResetButton, autoBait);

            float yCur = UguiFishingBaitBlockTop;
            if (autoBait)
            {
                PlaceUguiTopLeft(handle.BaitChoiceButton, 16f, yCur, 260f, 30f);
                yCur += 36f;
                PlaceUguiTopLeft(handle.BaitMaxLabel, 16f, yCur, 320f, 20f);
                yCur += 22f;
                if (handle.BaitMaxSlider != null)
                {
                    PlaceUguiTopLeft(handle.BaitMaxSlider.gameObject, 16f, yCur, 260f, 20f);
                }
                yCur += 30f;
                PlaceUguiTopLeft(handle.NoFishLabel, 16f, yCur, 320f, 20f);
                yCur += 22f;
                if (handle.NoFishSlider != null)
                {
                    PlaceUguiTopLeft(handle.NoFishSlider.gameObject, 16f, yCur, 260f, 20f);
                }
                yCur += 30f;
                // Source: label at (20,num,180,24), Reset at (190,num-3,90,28).
                PlaceUguiTopLeft(handle.RemainingLabel, 16f, yCur, 180f, 24f);
                PlaceUguiTopLeft(handle.BaitResetButton, 186f, yCur - 3f, 90f, 28f);
                yCur += 32f;
            }

            yCur += 10f; // AutoFishingFarm.cs:787 hands FishingRouteFeature.DrawSection num+10f

            PlaceUguiTopLeft(handle.RouteHeader, 16f, yCur, 320f, 22f);
            yCur += 28f;
            PlaceUguiTopLeft(handle.RouteActionButton, 16f, yCur, 260f, 35f);
            yCur += 42f;
            if (handle.CustomOnlyToggle != null)
            {
                PlaceUguiTopLeft(handle.CustomOnlyToggle.gameObject, 16f, yCur, 280f, 25f);
            }
            yCur += 30f;

            SetUguiGoActive(handle.NoCustomHintLabel, hintVisible);
            if (hintVisible)
            {
                PlaceUguiTopLeft(handle.NoCustomHintLabel, 16f, yCur, 360f, 20f);
                yCur += 24f;
            }

            SetUguiGoActive(handle.LocationLabel, routeActive);
            SetUguiGoActive(handle.RouteStatusLabel, routeActive);
            SetUguiGoActive(handle.PausedLabel, routeActive && paused);
            if (routeActive)
            {
                PlaceUguiTopLeft(handle.LocationLabel, 16f, yCur, 360f, 20f);
                yCur += 24f;
                PlaceUguiTopLeft(handle.RouteStatusLabel, 16f, yCur, 360f, 20f);
                yCur += 24f;
                if (paused)
                {
                    PlaceUguiTopLeft(handle.PausedLabel, 16f, yCur, 360f, 20f);
                    yCur += 24f;
                }
            }

            PlaceUguiTopLeft(handle.SaveLocationButton, 16f, yCur, 260f, 30f);
            yCur += 38f;

            SetUguiGoActive(handle.CustomCountLabel, listVisible);
            if (listVisible)
            {
                PlaceUguiTopLeft(handle.CustomCountLabel, 16f, yCur, 320f, 20f);
                yCur += 24f;
                for (int i = 0; i < handle.SpotRows.Count; i++)
                {
                    GameObject row = handle.SpotRows[i];
                    if (row == null)
                    {
                        continue;
                    }
                    // Source pitch is 26 (rows touching); 32 here gives the row buttons the same
                    // 6px breathing room as Teleport-Custom's list.
                    PlaceUguiTopLeft(row, 16f, yCur, 312f, 26f);
                    yCur += 32f;
                }
            }

            yCur += 10f; // FishingRouteFeature.DrawSection returns num + 10f
            // AutoFishingFarm.DrawSection returns num + 20f — that's the stack's bottom margin.
            this.SetUguiScrollContentHeight(handle.ScrollContent, yCur + 20f);
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellFishingOnUpdate()
        {
            UguiShellFishingHandle handle = this.uguiShellFishing;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellResourceGatheringSubTabActive(UguiShellFishingSubIndex))
            {
                return;
            }

            try
            {
                // Toggle re-syncs (external IMGUI/hotkey edits) — WithoutNotify only; the plain
                // setters would replay side effects (SetEnabled's session reset, SetAutoBait's
                // counter reset, SetCustomSpotsOnly's rotation restart).
                this.SyncUguiToggleFromField(handle.ShadowNetToggle, AutoFishingFarm.IsEnabled);
                this.SyncUguiToggleFromField(handle.InstantCatchToggle, AutoFishingFarm.GetInstantCatchEnabled());
                this.SyncUguiToggleFromField(handle.SkipCatchToggle, AutoFishingFarm.GetSkipCatchAnimEnabled());
                this.SyncUguiToggleFromField(handle.SkipCastToggle, AutoFishingFarm.GetSkipCastAnimEnabled());
                this.SyncUguiToggleFromField(handle.SkipBaitToggle, AutoFishingFarm.GetSkipBaitAnimEnabled());
                this.SyncUguiToggleFromField(handle.AutoBaitToggle, AutoFishingFarm.GetAutoBaitEnabled());
                this.SyncUguiToggleFromField(handle.CustomOnlyToggle, FishingRouteFeature.GetCustomSpotsOnly());

                // Status readouts — every gated frame like the IMGUI drawer (pure string
                // remapping, no side effects); cached diffs limit SetText churn.
                this.SyncUguiSelfLabelText(handle.StatusLabel, ref handle.StatusShown,
                    this.LF("Status: {0}", AutoFishingFarm.GetLastStatus()));
                this.SyncUguiSelfLabelText(handle.ToolLabel, ref handle.ToolShown,
                    this.LF("Tool: {0}", AutoFishingFarm.GetLastToolStatus()));
                this.SyncUguiSelfLabelText(handle.TargetLabel, ref handle.TargetShown,
                    this.LF("Target: {0}", AutoFishingFarm.GetLastTargetStatus()));

                float range = AutoFishingFarm.GetDetectRange();
                if (handle.ScanRangeSlider != null && Mathf.Abs(handle.ScanRangeSlider.value - range) > 0.0005f)
                {
                    handle.ScanRangeSlider.SetValueWithoutNotify(range);
                }
                this.SyncUguiSelfLabelText(handle.ScanRangeLabel, ref handle.ScanRangeShown,
                    this.LF("Scan Range: {0:F0}m", range));

                // Auto Bait block — only while visible.
                if (AutoFishingFarm.GetAutoBaitEnabled())
                {
                    string choiceText = this.BuildUguiFishingBaitChoiceText();
                    if (!string.Equals(choiceText, handle.BaitChoiceShown, StringComparison.Ordinal))
                    {
                        handle.BaitChoiceShown = choiceText;
                        this.SetUguiButtonLabel(handle.BaitChoiceButton, choiceText);
                    }

                    int maxCount = AutoFishingFarm.GetAutoBaitMaxCount();
                    if (handle.BaitMaxSlider != null && Mathf.Abs(handle.BaitMaxSlider.value - maxCount) > 0.0005f)
                    {
                        handle.BaitMaxSlider.SetValueWithoutNotify(maxCount);
                    }
                    this.SyncUguiSelfLabelText(handle.BaitMaxLabel, ref handle.BaitMaxShown,
                        this.LF("Max: {0}", maxCount));

                    float noFish = AutoFishingFarm.GetAutoBaitNoFishSeconds();
                    if (handle.NoFishSlider != null && Mathf.Abs(handle.NoFishSlider.value - noFish) > 0.0005f)
                    {
                        handle.NoFishSlider.SetValueWithoutNotify(noFish);
                    }
                    this.SyncUguiSelfLabelText(handle.NoFishLabel, ref handle.NoFishShown,
                        this.LF("No-fish: {0:F0}s", noFish));

                    this.SyncUguiSelfLabelText(handle.RemainingLabel, ref handle.RemainingShown,
                        this.LF("Remaining: {0}/{1}", AutoFishingFarm.GetAutoBaitRemaining(), maxCount));
                }

                // Route action label + active-only status lines.
                string actionText = this.BuildUguiFishingRouteActionText();
                if (!string.Equals(actionText, handle.RouteActionShown, StringComparison.Ordinal))
                {
                    handle.RouteActionShown = actionText;
                    this.SetUguiButtonLabel(handle.RouteActionButton, actionText);
                }
                if (FishingRouteFeature.Active)
                {
                    this.SyncUguiSelfLabelText(handle.LocationLabel, ref handle.LocationShown,
                        this.BuildUguiFishingLocationText());
                    this.SyncUguiSelfLabelText(handle.RouteStatusLabel, ref handle.RouteStatusShown,
                        this.BuildUguiFishingRouteStatusText());
                }

                // 0.5s tick: the custom-spot count probe (ExportCustomSpots allocates a copy —
                // cross-surface adds/deletes land within half a second; this surface's own
                // Save/Delete refresh immediately in their handlers).
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    handle.CustomSpotsCount = FishingRouteFeature.ExportCustomSpots().Count;
                }

                // Conditional-layout signature (bait block / route lines / hint / row count).
                int signature = this.ComputeUguiFishingLayoutSignature(handle);
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    if (handle.SpotRows.Count != handle.CustomSpotsCount)
                    {
                        this.RebuildUguiShellFishingSpotRows(handle);
                    }
                    this.RelayoutUguiShellFishing(handle);
                    handle.NextSlowSyncAt = 0f; // labels/probe must not lag a layout change
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Fishing content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order).
        // Notification strings are passed exactly as the IMGUI call sites pass them
        // (AddMenuNotification localizes internally).
        // ----------------------------------------------------------------------------------------

        // The shared "<Label> Enabled/Disabled" toast every AutoFishingFarm toggle uses
        // (AutoFishingFarm.cs:661-664, 671-674, 685-688, 696-699, 707-710, 738-741).
        private void NotifyUguiFishingToggle(string label, bool value)
        {
            this.AddMenuNotification(label + " " + (value ? "Enabled" : "Disabled"),
                value ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
        }

        // AutoFishingFarm.cs:650-654.
        private void OnUguiFishingEquipRodClicked()
        {
            this.EquipHandTool(3);
        }

        // AutoFishingFarm.cs:657-665 — SetEnabled MUST get the host: enabling captures the
        // previously-equipped tool, disabling restores it and releases the fishing press;
        // a null host would silently break the tool restore. No SaveKeybinds (source parity).
        private void OnUguiFishingShadowNetToggled(bool value)
        {
            if (value == AutoFishingFarm.IsEnabled)
            {
                return;
            }
            AutoFishingFarm.SetEnabled(value, this);
            this.NotifyUguiFishingToggle("Auto Fish Shadow Net", value);
        }

        // AutoFishingFarm.cs:667-676 — installs/removes the instant-catch detour internally.
        private void OnUguiFishingInstantCatchToggled(bool value)
        {
            if (value == AutoFishingFarm.GetInstantCatchEnabled())
            {
                return;
            }
            AutoFishingFarm.SetInstantCatchEnabled(value);
            this.NotifyUguiFishingToggle("Instant Catch", value);
            try { this.SaveKeybinds(false); } catch { }
        }

        // AutoFishingFarm.cs:681-690.
        private void OnUguiFishingSkipCatchToggled(bool value)
        {
            if (value == AutoFishingFarm.GetSkipCatchAnimEnabled())
            {
                return;
            }
            AutoFishingFarm.SetSkipCatchAnimEnabled(value);
            this.NotifyUguiFishingToggle("Skip Catch Animation", value);
            try { this.SaveKeybinds(false); } catch { }
        }

        // AutoFishingFarm.cs:692-701 — the setter also resets its poll window when disabled.
        private void OnUguiFishingSkipCastToggled(bool value)
        {
            if (value == AutoFishingFarm.GetSkipCastAnimEnabled())
            {
                return;
            }
            AutoFishingFarm.SetSkipCastAnimEnabled(value);
            this.NotifyUguiFishingToggle("Skip Cast Animation", value);
            try { this.SaveKeybinds(false); } catch { }
        }

        // AutoFishingFarm.cs:703-712.
        private void OnUguiFishingSkipBaitToggled(bool value)
        {
            if (value == AutoFishingFarm.GetSkipBaitAnimEnabled())
            {
                return;
            }
            AutoFishingFarm.SetSkipBaitAnimEnabled(value);
            this.NotifyUguiFishingToggle("Skip Bait Animation", value);
            try { this.SaveKeybinds(false); } catch { }
        }

        // AutoFishingFarm.cs:721-731 via SetDetectRangeFromUi (round / change-detect / "Range
        // updated" status / log live inside AutoFishingFarm; it no-ops when unchanged, so the
        // call is unconditional like the source's own always-round-and-write). The source's
        // save-on-actual-change is reproduced with a before/after compare — the wrapper has no
        // host to save through.
        private void OnUguiFishingScanRangeChanged(float value)
        {
            float before = AutoFishingFarm.GetDetectRange();
            AutoFishingFarm.SetDetectRangeFromUi(Mathf.Round(value));
            if (Math.Abs(AutoFishingFarm.GetDetectRange() - before) > 0.0001f)
            {
                try { this.SaveKeybinds(false); } catch { }
            }
        }

        // AutoFishingFarm.cs:734-742 — the setter resets the bait counter + timers on change.
        private void OnUguiFishingAutoBaitToggled(bool value)
        {
            if (value == AutoFishingFarm.GetAutoBaitEnabled())
            {
                return;
            }
            AutoFishingFarm.SetAutoBaitEnabled(value);
            this.NotifyUguiFishingToggle("Auto Bait", value);
            try { this.SaveKeybinds(false); } catch { }
            // The conditional block re-flows via the layout signature on the next gated frame.
        }

        // AutoFishingFarm.cs:747-755 (AutoBaitChoice: Bait=0, Attractor=1).
        private void OnUguiFishingBaitChoiceClicked()
        {
            int current = AutoFishingFarm.GetAutoBaitChoice();
            AutoFishingFarm.SetAutoBaitChoice(current == 0 ? 1 : 0);
            try { this.SaveKeybinds(false); } catch { }
        }

        // AutoFishingFarm.cs:758-766 — the setter refills the live counter on an actual change.
        private void OnUguiFishingBaitMaxChanged(float value)
        {
            int newMax = Mathf.RoundToInt(value);
            if (newMax == AutoFishingFarm.GetAutoBaitMaxCount())
            {
                return;
            }
            AutoFishingFarm.SetAutoBaitMaxCount(newMax);
            try { this.SaveKeybinds(false); } catch { }
        }

        // AutoFishingFarm.cs:769-776 — the public setter's clamp (3-60) equals the slider's own
        // bounds, so it behaves identically to the IMGUI block's direct field write.
        private void OnUguiFishingNoFishChanged(float value)
        {
            float rounded = Mathf.Round(value);
            if (Math.Abs(rounded - AutoFishingFarm.GetAutoBaitNoFishSeconds()) <= 0.0001f)
            {
                return;
            }
            AutoFishingFarm.SetAutoBaitNoFishSeconds(rounded);
            try { this.SaveKeybinds(false); } catch { }
        }

        // AutoFishingFarm.cs:780-783 — no save, no notification (source parity).
        private void OnUguiFishingBaitResetClicked()
        {
            AutoFishingFarm.ResetAutoBaitCounter();
        }

        // FishingRouteFeature.DrawSection:436-449 — Start/Stop no-op on a null host, so `this`
        // is mandatory; they own every snapshot/restore side effect.
        private void OnUguiFishingRouteActionClicked()
        {
            if (FishingRouteFeature.Active)
            {
                FishingRouteFeature.Stop(this);
                this.AddMenuNotification(this.L("Fishing Locations stopped"), new Color(1f, 0.55f, 0.55f));
            }
            else
            {
                FishingRouteFeature.Start(this);
                this.AddMenuNotification(this.L("Fishing Locations started"), new Color(0.45f, 1f, 0.55f));
            }
        }

        // FishingRouteFeature.DrawSection:452-460 — two direction-dependent messages/colors.
        private void OnUguiFishingCustomOnlyToggled(bool value)
        {
            if (value == FishingRouteFeature.GetCustomSpotsOnly())
            {
                return;
            }
            FishingRouteFeature.SetCustomSpotsOnly(value);
            this.AddMenuNotification(
                this.L(value ? "Route: custom spots only" : "Route: all spots"),
                value ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.75f, 0.5f));
            try { this.SaveKeybinds(false); } catch { }
        }

        // FishingRouteFeature.DrawSection:487-490 via the pass-through wrapper (the save itself
        // notifies + persists internally); the row list refreshes immediately, not on the probe.
        private void OnUguiFishingSaveLocationClicked()
        {
            FishingRouteFeature.SaveCurrentLocationFromUi(this);
            this.RefreshUguiFishingSpotListNow();
        }

        // FishingRouteFeature.RemoveCustomSpotAt = DrawSection's old removal block (route-pointer
        // fixup + save) extracted so both surfaces share one implementation. Destroying the
        // clicked row's own button inside its click is safe (Unity defers Destroy to frame end).
        private void OnUguiFishingSpotDeleteClicked(int index)
        {
            FishingRouteFeature.RemoveCustomSpotAt(index, this);
            this.RefreshUguiFishingSpotListNow();
        }

        private void RefreshUguiFishingSpotListNow()
        {
            UguiShellFishingHandle handle = this.uguiShellFishing;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                this.RebuildUguiShellFishingSpotRows(handle);
                handle.LayoutSignature = this.ComputeUguiFishingLayoutSignature(handle);
                this.RelayoutUguiShellFishing(handle);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Fishing spot list refresh error: " + ex.Message);
            }
        }
    }
}
