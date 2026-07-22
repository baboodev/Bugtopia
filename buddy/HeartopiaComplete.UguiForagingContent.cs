using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, Resource Gathering round 1 of 4 (migration plan:
    // cosmic-waddling-rainbow.md, item 6): the FORAGING sub-tab only — the FORAGING action/status
    // panel, the SETTINGS panel and the 13-toggle LOOT PRIORITIES grid from DrawAutoFarmTab's
    // default branch (HeartopiaComplete.Farm.cs:137-391). Fishing/Insects/Birds are separate
    // follow-up rounds and are untouched (their cells stay shell placeholders).
    //
    // Ground rules (same as rounds 1-5 + Bag/Warehouse):
    //  - The IMGUI drawer (DrawAutoFarmTab) and every backend method it calls stay fully
    //    functional and untouched — this file only READS the same fields and CALLS the same
    //    action methods. Two independent rendering paths over one backend.
    //  - Wiring is by STATIC display-position index (UguiShellResourceGatheringTabIndex = 1 +
    //    UguiShellForagingSubIndex = 0 — declared next to their siblings in
    //    UguiPhase3Content.cs), never by localized label comparison.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // The one genuinely BEHAVIORAL piece here (not just display): the IMGUI status conditional
    // (Farm.cs:161-211) STOPS the farm from two of its branches (no radar loot selected / Aura
    // Farm turned off while running) — autoFarmActive=false + SetGameSpeed(1) + farmState=Idle +
    // autoFarmAutoStopAt=-1. Those cascades are real farm behavior, so
    // EvaluateUguiForagingStatus re-runs the WHOLE conditional every gated frame (never a cached
    // format-only path), exactly like the IMGUI drawer re-runs it every drawn frame. It runs
    // ONLY while the shell is visible on this sub-tab — the same visibility semantics the IMGUI
    // twin has (its cascade also only fires while its menu is drawn on this tab) — and NOT at
    // build time (all tabs build on first F10, which would fire the cascade sight-unseen).
    //
    // The HH:MM:SS auto-stop entry is deliberately a DIFFERENT InputField pattern than Teleport
    // XYZ's click-time-read fields: IMGUI live-parses and re-normalizes these three strings
    // every drawn frame (int.TryParse → Mathf.Clamp 0-23/0-59/0-59 → the int field → the input
    // string OVERWRITTEN with the clamped value, Farm.cs:323-338). Here each field's
    // onValueChanged performs that same parse-clamp-writeback immediately on every edit
    // (SetTextWithoutNotify for the normalization echo — never .text, which would re-fire the
    // event). The shared autoFarmAutoStop*Input strings stay the cross-surface contract: our
    // edits write them (so the IMGUI twin shows the same text), and a 0.5s tick pulls external
    // edits back in via SyncUguiInputFieldFromBackingField (typing is never clobbered — the
    // handlers keep the *Seen caches in step with every write they make).
    //
    // Cross-surface sync cadence (established split):
    //  - Every gated frame: the status conditional (behavioral — see above), action-button label,
    //    mode readout, camera-stuck line visibility, toggle/slider re-syncs
    //    (SetIsOnWithoutNotify/SetValueWithoutNotify — NEVER the plain setters), slider value
    //    labels (cached-string compare), the auto-stop countdown text (a per-second countdown —
    //    throttling it would visibly stutter; IMGUI formats it per frame too), and the
    //    conditional-layout signature (auraFarmEnabled/autoFarmAutoStopEnabled — the IMGUI
    //    drawer's literal settingsHeight formula becomes relayout-on-signature-change).
    //  - 0.5s tick (NextSlowSyncAt idiom): the Resolver readout, the Priority Location footer
    //    (GetActivePriorityLocation walks the toggle list each call), and the three input-field
    //    external re-syncs. A layout-signature change forces the tick immediately so e.g.
    //    toggling Aura Farm never shows a 0.5s-stale Resolver line next to an already-moved row.
    //
    // The 13 loot-priority toggles are flag-only in the source (Farm.cs:366-382 — plain
    // assignments, no save, no notification): the change handlers here write ONLY the bool.
    // Verified against the IMGUI drawer — do not add a SaveKeybinds or AddMenuNotification.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handle (per-instance state — assigned LAST in the builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellForagingHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float ContentWidth;            // scroll content width (block w minus viewport insets)

            // FORAGING panel
            public GameObject ActionButton;       // label swaps Start/Stop Foraging
            public string ActionShown;
            public GameObject ModeLabel;          // "Aura Farm" / "No mode"
            public string ModeShown;
            public GameObject StatusTextLabel;    // the 6-way conditional's text+color
            public string StatusShown;
            public Color StatusColorShown;
            public GameObject CameraStuckLabel;   // visible while cameraStuckDisplayTimer > 0

            // SETTINGS panel (height changes with the conditional sections)
            public GameObject SettingsPanel;
            public GameObject AreaLoadLabel;
            public string AreaLoadShown;
            public Slider AreaLoadSlider;
            public Toggle AuraFarmToggle;
            public GameObject ResolverLabel;      // STANDBY / READY / RESOLVING (0.5s tick)
            public string ResolverShown;
            public Color ResolverColorShown;
            public GameObject CollectWaitLabel;   // aura-only row
            public string CollectWaitShown;
            public Slider CollectWaitSlider;
            public GameObject TeleportDelayLabel; // aura-only row
            public string TeleportDelayShown;
            public Slider TeleportDelaySlider;
            public Toggle AutoStopToggle;
            public GameObject TimerCaption;       // timer row (auto-stop-only)
            public GameObject TimerColon1;
            public GameObject TimerColon2;
            public InputField HoursField;         // live parse-clamp-writeback (see file header)
            public InputField MinutesField;
            public InputField SecondsField;
            public string HoursSeen;              // external-change sync caches (backing strings)
            public string MinutesSeen;
            public string SecondsSeen;
            public GameObject TimerStatusLabel;   // "Stops after:" / "Set at least 1 second" / "Remaining:"
            public string TimerStatusShown;

            // LOOT PRIORITIES panel
            public GameObject LootPanel;
            public UguiForagingLootBinding[] LootBindings;
            public readonly List<Toggle> LootToggles = new List<Toggle>();
            public GameObject PriorityLocationLabel;
            public string PriorityLocationShown;

            public int LayoutSignature = -1;      // packed auraFarmEnabled/autoFarmAutoStopEnabled
            public float NextSlowSyncAt;          // 0.5s tick
            public int ErrorCount;                // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellForagingHandle uguiShellForaging;

        // One loot-priority checkbox binding — label + get/set over the flag field, NOTHING else
        // (the source toggles are side-effect-free; see file header). Column/order mirror the
        // IMGUI 3-column layout (Farm.cs:366-382).
        private struct UguiForagingLootBinding
        {
            public string Label;
            public int Column;                    // 0 = Mushrooms, 1 = Events, 2 = Other
            public Func<bool> Get;
            public Action<bool> Set;
        }

        private UguiForagingLootBinding[] BuildUguiForagingLootBindings()
        {
            return new UguiForagingLootBinding[]
            {
                new UguiForagingLootBinding { Label = "Oyster", Column = 0, Get = () => this.priorityOysterMushroom, Set = v => this.priorityOysterMushroom = v },
                new UguiForagingLootBinding { Label = "Button", Column = 0, Get = () => this.priorityButtonMushroom, Set = v => this.priorityButtonMushroom = v },
                new UguiForagingLootBinding { Label = "Penny Bun", Column = 0, Get = () => this.priorityPennyBun, Set = v => this.priorityPennyBun = v },
                new UguiForagingLootBinding { Label = "Shiitake", Column = 0, Get = () => this.priorityShiitake, Set = v => this.priorityShiitake = v },
                new UguiForagingLootBinding { Label = "Truffle", Column = 0, Get = () => this.priorityTruffle, Set = v => this.priorityTruffle = v },
                new UguiForagingLootBinding { Label = "Fiddlehead", Column = 1, Get = () => this.priorityFiddlehead, Set = v => this.priorityFiddlehead = v },
                new UguiForagingLootBinding { Label = "Tall Mustard", Column = 1, Get = () => this.priorityTallMustard, Set = v => this.priorityTallMustard = v },
                new UguiForagingLootBinding { Label = "Burdock", Column = 1, Get = () => this.priorityBurdock, Set = v => this.priorityBurdock = v },
                new UguiForagingLootBinding { Label = "Mustard Greens", Column = 1, Get = () => this.priorityMustardGreens, Set = v => this.priorityMustardGreens = v },
                new UguiForagingLootBinding { Label = "Blueberries", Column = 2, Get = () => this.priorityBlueberry, Set = v => this.priorityBlueberry = v },
                new UguiForagingLootBinding { Label = "Raspberries", Column = 2, Get = () => this.priorityRaspberry, Set = v => this.priorityRaspberry = v },
                new UguiForagingLootBinding { Label = "Bubbles", Column = 2, Get = () => this.priorityBubble, Set = v => this.priorityBubble = v },
                new UguiForagingLootBinding { Label = "Insects", Column = 2, Get = () => this.priorityInsect, Set = v => this.priorityInsect = v },
            };
        }

        // ----------------------------------------------------------------------------------------
        // Shared gate: is the shell showing a specific Resource Gathering sub-tab right now?
        // ----------------------------------------------------------------------------------------

        private bool IsUguiShellResourceGatheringSubTabActive(int subIndex)
        {
            try
            {
                UguiShellHandle shell = this.uguiShell;
                if (shell == null || shell.ActiveIndex != UguiShellResourceGatheringTabIndex
                    || !this.IsUguiWindowVisible(shell.Window))
                {
                    return false;
                }
                UguiTabBarHandle bar = (UguiShellResourceGatheringTabIndex < shell.SubTabBars.Count)
                    ? shell.SubTabBars[UguiShellResourceGatheringTabIndex]
                    : null;
                return bar != null && bar.ActiveIndex == subIndex;
            }
            catch
            {
                return false;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Status conditional — the EXACT DrawAutoFarmTab chain (Farm.cs:161-211), INCLUDING the
        // two stop-cascades. Behavioral, not display-only: call every gated frame, never cache.
        // ----------------------------------------------------------------------------------------

        private void EvaluateUguiForagingStatus(out string statusText, out Color statusColor)
        {
            if (!this.AnyRadarLootToggleEnabled())
            {
                statusText = "Select radar loot first";
                statusColor = new Color(1f, 0.32f, 0.32f);
                if (this.autoFarmActive)
                {
                    this.autoFarmActive = false;
                    this.SetGameSpeed(1f);
                    this.farmState = HeartopiaComplete.AutoFarmState.Idle;
                    this.autoFarmAutoStopAt = -1f;
                }
            }
            else if (!this.auraFarmEnabled)
            {
                statusText = "Enable Aura Farm";
                statusColor = new Color(1f, 0.7f, 0.45f);
                if (this.autoFarmActive)
                {
                    this.autoFarmActive = false;
                    this.SetGameSpeed(1f);
                    this.farmState = HeartopiaComplete.AutoFarmState.Idle;
                    this.autoFarmAutoStopAt = -1f;
                }
            }
            else if (this.autoFarmStatus == "NO_TOGGLES_ERROR")
            {
                statusText = "Select radar loot first";
                statusColor = new Color(1f, 0.32f, 0.32f);
            }
            else if (this.autoFarmStatus == "RADAR_OFF_ERROR")
            {
                statusText = "Radar is off";
                statusColor = new Color(1f, 0.32f, 0.32f);
            }
            else if (this.autoFarmStatus == "MODE_REQUIRED_ERROR")
            {
                statusText = "Enable Aura Farm";
                statusColor = new Color(1f, 0.7f, 0.45f);
            }
            else if (!this.autoFarmActive && (this.autoFarmStatus == "READY" || this.autoFarmStatus == "Idle" || this.autoFarmStatus == "NO_TOGGLES"))
            {
                statusText = "Ready";
                statusColor = new Color(0.45f, 1f, 0.55f);
            }
            else
            {
                statusText = this.autoFarmStatus ?? "Idle";
                statusColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            }
        }

        // Small display builders — IMGUI strings verbatim (only the L()-localized ones go
        // through L, matching each IMGUI call site exactly).

        private string BuildUguiForagingActionText()
        {
            // DrawPrimaryActionButton localizes its label internally (UiKitPrimitives.cs:731).
            return this.autoFarmActive ? this.L("Stop Foraging") : this.L("Start Foraging");
        }

        private void BuildUguiForagingResolverDisplay(out string text, out Color color)
        {
            // Farm.cs:255-271 — 3-way readout of auraFarmEnabled + auraFarmMethodsReady.
            if (!this.auraFarmEnabled)
            {
                text = this.L("Resolver: STANDBY");
                color = new Color(0.8f, 0.8f, 0.8f);
            }
            else if (this.auraFarmMethodsReady)
            {
                text = this.L("Resolver: READY");
                color = new Color(0.45f, 1f, 0.55f);
            }
            else
            {
                text = this.L("Resolver: RESOLVING / NOT READY");
                color = new Color(1f, 0.7f, 0.45f);
            }
        }

        private string BuildUguiForagingTimerText()
        {
            // Farm.cs:340-346 — countdown wins over the configured-duration readout while the
            // farm is running with a target set. Unlocalized in the source.
            int autoStopSeconds = this.GetAutoFarmAutoStopSeconds();
            string timerText = autoStopSeconds <= 0
                ? "Set at least 1 second"
                : "Stops after: " + this.FormatDurationHms(autoStopSeconds);
            if (this.autoFarmActive && this.autoFarmAutoStopAt > 0f)
            {
                int remaining = Mathf.Max(0, Mathf.CeilToInt(this.autoFarmAutoStopAt - Time.unscaledTime));
                timerText = "Remaining: " + this.FormatDurationHms(remaining);
            }
            return timerText;
        }

        private string BuildUguiForagingPriorityLocationText()
        {
            // Farm.cs:384-387 — interpolated line unlocalized when set, L() only for "None".
            Vector3? activePriorityLoc = this.GetActivePriorityLocation();
            return activePriorityLoc != null
                ? $"Priority Location: {activePriorityLoc.Value.x:F1}, {activePriorityLoc.Value.y:F1}, {activePriorityLoc.Value.z:F1}"
                : this.L("Priority Location: None");
        }

        private int ComputeUguiForagingLayoutSignature()
        {
            return (this.auraFarmEnabled ? 1 : 0)
                 | (this.autoFarmAutoStopEnabled ? 2 : 0);
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawAutoFarmTab's Foraging branch: three DrawExentriSectionPanel cards
        // (via the shared CreateUguiSettingsMainPanel chrome — same IMGUI drawer being mirrored)
        // in a scroll view (the stack runs ~630-750px against a ~520px cell; IMGUI scrolls the
        // whole tab via tabScrollPos). All controls — including conditionally-visible ones — are
        // built ONCE here; RelayoutUguiShellForaging owns the conditional positions/visibility/
        // heights. Handle assigned LAST (Research idiom).
        private GameObject BuildUguiShellForagingContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellForaging = null;

            UguiShellForagingHandle handle = new UguiShellForagingHandle();
            GameObject block = this.CreateUguiGo("ForagingContent", parent);
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

            float panelW = handle.ContentWidth - 16f; // panels at x=8, 8px right margin

            // ---------------- FORAGING panel (fixed 112px — IMGUI statusPanel) ----------------
            GameObject foragingPanel = this.CreateUguiSettingsMainPanel(scrollContent, "ForagingPanel", this.L("FORAGING"));
            PlaceUguiTopLeft(foragingPanel, 8f, 8f, panelW, 112f);

            handle.ActionShown = this.BuildUguiForagingActionText();
            handle.ActionButton = this.CreateUguiPrimaryButton(foragingPanel.transform, "ActionButton",
                handle.ActionShown, new System.Action(this.OnUguiForagingActionClicked));
            PlaceUguiTopLeft(handle.ActionButton, 14f, 48f, 190f, 32f);

            // Status box — IMGUI: DrawTintedRoundedBox (uiPanel @ clamp(contentAlpha*0.55)) +
            // DrawCardOutline hairline.
            // The source's third piece, a 1.5px accent strip inset along the box's top edge, was
            // ported faithfully and then REMOVED (2026-07-22, user-reported): it reads as a stray
            // red bar above the STATUS block. IMGUI got away with it because its rounded-box bake
            // and the strip were composited into the same immediate-mode draw; here the strip is a
            // FLAT child rect sitting on a SLICED rounded parent, so its square ends overhang the
            // parent's corner radius instead of being absorbed by it — the same flat-over-rounded
            // mismatch this project has hit repeatedly. Don't re-add it for source parity.
            float boxW = panelW - 238f;
            GameObject statusBox = this.CreateUguiGo("StatusBox", foragingPanel.transform);
            PlaceUguiTopLeft(statusBox, 224f, 36f, boxW, 60f);
            this.AddUguiImage(statusBox,
                new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB,
                    Mathf.Clamp(this.uiContentAlpha * 0.55f, 0.12f, 0.74f)), true, 1f);
            this.AddUguiRingOverlay(statusBox,
                new Color(1f, 1f, 1f, Mathf.Clamp(0.05f + (this.uiPanelAlpha * 0.05f), 0.05f, 0.10f)), 1f);

            // "STATUS" mini-header (IMGUI sectionStyle: bold 12 header color — header-label role).
            GameObject statusHeader = this.CreateUguiHeaderLabel(statusBox.transform, "StatusHeader", this.L("STATUS"), 11f);
            PlaceUguiTopLeft(statusHeader, 12f, 7f, 92f, 18f);

            // Mode readout, top-right (IMGUI modeStyle: MiddleRight 11 in subTabText @ 0.9).
            Color modeMuted = this.UguiKitMutedColor();
            handle.ModeShown = this.auraFarmEnabled ? "Aura Farm" : "No mode";
            handle.ModeLabel = this.CreateUguiLabel(statusBox.transform, "Mode", handle.ModeShown, 11f,
                new Color(modeMuted.r, modeMuted.g, modeMuted.b, 0.9f), false);
            this.TrySetUguiLabelRightAligned(handle.ModeLabel);
            PlaceUguiTopLeft(handle.ModeLabel, 108f, 7f, boxW - 120f, 18f);

            // 6-way status text — seeded EMPTY on purpose: filling it runs the stop-cascade
            // conditional, which must only run while the tab is actually shown (file header).
            // The first gated frame paints it (StatusShown = null forces the first apply).
            handle.StatusTextLabel = this.CreateUguiLabel(statusBox.transform, "StatusText", "", 12f,
                this.UguiKitTextColor(), false);
            this.TrySetUguiLabelBold(handle.StatusTextLabel);
            this.TrySetUguiLabelWrapped(handle.StatusTextLabel);
            PlaceUguiTopLeft(handle.StatusTextLabel, 12f, 29f, boxW - 24f, 24f);

            // Camera-stuck warning — IMGUI paints it OVER the status text's lower half
            // (y+42 vs y+29..53); same overlapping placement kept for parity.
            handle.CameraStuckLabel = this.CreateUguiLabel(statusBox.transform, "CameraStuck",
                "Camera stuck fix running", 11f, new Color(1f, 0.45f, 0.45f), false);
            this.TrySetUguiLabelBold(handle.CameraStuckLabel);
            PlaceUguiTopLeft(handle.CameraStuckLabel, 12f, 42f, boxW - 24f, 16f);
            SetUguiGoActive(handle.CameraStuckLabel, this.cameraStuckDisplayTimer > 0f);

            // ---------------- SETTINGS panel (height owned by the relayout) ----------------
            GameObject settings = this.CreateUguiSettingsMainPanel(scrollContent, "SettingsPanel", this.L("SETTINGS"));
            handle.SettingsPanel = settings;

            // Fixed rows (panel-local, top-anchored — a panel-height change never moves them).
            handle.AreaLoadShown = this.LF("Area Load Delay: {0}s", (int)this.areaLoadDelay);
            handle.AreaLoadLabel = this.CreateUguiBodyLabel(settings.transform, "AreaLoadLabel", handle.AreaLoadShown, 13f);
            PlaceUguiTopLeft(handle.AreaLoadLabel, 14f, 42f, 150f, 20f);
            handle.AreaLoadSlider = this.CreateUguiSlider(settings.transform, "AreaLoadSlider",
                1f, 10f, this.areaLoadDelay, true,
                new System.Action<float>(this.OnUguiForagingAreaLoadDelayChanged));
            PlaceUguiTopLeft(handle.AreaLoadSlider.gameObject, 172f, 43f, panelW - 200f, 20f);

            handle.AuraFarmToggle = this.CreateUguiCheckbox(settings.transform, "AuraFarmToggle",
                this.L("Aura Farm"), this.auraFarmEnabled,
                new System.Action<bool>(this.OnUguiForagingAuraFarmToggled));
            PlaceUguiTopLeft(handle.AuraFarmToggle.gameObject, 14f, 76f, 250f, 24f);

            this.BuildUguiForagingResolverDisplay(out string resolverText, out Color resolverColor);
            handle.ResolverShown = resolverText;
            handle.ResolverColorShown = resolverColor;
            handle.ResolverLabel = this.CreateUguiLabel(settings.transform, "ResolverLabel",
                resolverText, 11f, resolverColor, false);
            this.TrySetUguiLabelWrapped(handle.ResolverLabel);
            PlaceUguiTopLeft(handle.ResolverLabel, 270f, 76f, panelW - 282f, 28f);

            // Aura-only rows — positions are constant when visible (110/144); relayout only flips
            // their visibility.
            handle.CollectWaitShown = this.LF("Collect Wait Max: {0}s", (int)this.auraCollectWaitTimeout);
            handle.CollectWaitLabel = this.CreateUguiBodyLabel(settings.transform, "CollectWaitLabel", handle.CollectWaitShown, 13f);
            PlaceUguiTopLeft(handle.CollectWaitLabel, 14f, 110f, 170f, 20f);
            handle.CollectWaitSlider = this.CreateUguiSlider(settings.transform, "CollectWaitSlider",
                4f, 30f, this.auraCollectWaitTimeout, true,
                new System.Action<float>(this.OnUguiForagingCollectWaitChanged));
            PlaceUguiTopLeft(handle.CollectWaitSlider.gameObject, 192f, 111f, panelW - 220f, 20f);

            handle.TeleportDelayShown = this.LF("Teleport Delay: {0}s", (int)this.foragingTeleportDelaySeconds);
            handle.TeleportDelayLabel = this.CreateUguiBodyLabel(settings.transform, "TeleportDelayLabel", handle.TeleportDelayShown, 13f);
            PlaceUguiTopLeft(handle.TeleportDelayLabel, 14f, 144f, 180f, 20f);
            handle.TeleportDelaySlider = this.CreateUguiSlider(settings.transform, "TeleportDelaySlider",
                0f, 10f, this.foragingTeleportDelaySeconds, true,
                new System.Action<float>(this.OnUguiForagingTeleportDelayChanged));
            PlaceUguiTopLeft(handle.TeleportDelaySlider.gameObject, 192f, 145f, panelW - 220f, 20f);

            // Auto Stop Timer toggle + timer row (positions owned by the relayout — the toggle
            // rides at 110 or 178 depending on the aura block).
            handle.AutoStopToggle = this.CreateUguiCheckbox(settings.transform, "AutoStopToggle",
                this.L("Auto Stop Timer"), this.autoFarmAutoStopEnabled,
                new System.Action<bool>(this.OnUguiForagingAutoStopToggled));

            handle.TimerCaption = this.CreateUguiBodyLabel(settings.transform, "TimerCaption", "Timer", 13f);
            handle.HoursSeen = this.autoFarmAutoStopHoursInput ?? string.Empty;
            handle.HoursField = this.CreateUguiInputField(settings.transform, "HoursField",
                handle.HoursSeen, 2, new System.Action<string>(this.OnUguiForagingAutoStopHoursEdited));
            handle.TimerColon1 = this.CreateUguiBodyLabel(settings.transform, "TimerColon1", ":", 13f);
            handle.MinutesSeen = this.autoFarmAutoStopMinutesInput ?? string.Empty;
            handle.MinutesField = this.CreateUguiInputField(settings.transform, "MinutesField",
                handle.MinutesSeen, 2, new System.Action<string>(this.OnUguiForagingAutoStopMinutesEdited));
            handle.TimerColon2 = this.CreateUguiBodyLabel(settings.transform, "TimerColon2", ":", 13f);
            handle.SecondsSeen = this.autoFarmAutoStopSecondsInput ?? string.Empty;
            handle.SecondsField = this.CreateUguiInputField(settings.transform, "SecondsField",
                handle.SecondsSeen, 2, new System.Action<string>(this.OnUguiForagingAutoStopSecondsEdited));

            // IMGUI timerSmall = bodyStyle (uiText @ 0.95); the countdown refreshes per gated
            // frame, so no throttled tick touches it.
            handle.TimerStatusShown = this.BuildUguiForagingTimerText();
            handle.TimerStatusLabel = this.CreateUguiLabel(settings.transform, "TimerStatus",
                handle.TimerStatusShown, 11f,
                new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.95f), false);

            // ---------------- LOOT PRIORITIES panel (fixed 318px — IMGUI priorityPanel) --------
            GameObject loot = this.CreateUguiSettingsMainPanel(scrollContent, "LootPanel", this.L("LOOT PRIORITIES"));
            handle.LootPanel = loot;

            float[] colX = new float[] { 18f, 164f, 310f };
            const float colW = 128f;
            GameObject colMushrooms = this.CreateUguiBodyLabel(loot.transform, "ColMushrooms", this.L("Mushrooms"), 13f);
            PlaceUguiTopLeft(colMushrooms, colX[0], 42f, colW, 20f);
            GameObject colEvents = this.CreateUguiBodyLabel(loot.transform, "ColEvents", this.L("Events"), 13f);
            PlaceUguiTopLeft(colEvents, colX[1], 42f, colW, 20f);
            GameObject colOther = this.CreateUguiBodyLabel(loot.transform, "ColOther", this.L("Other"), 13f);
            PlaceUguiTopLeft(colOther, colX[2], 42f, colW, 20f);

            handle.LootBindings = this.BuildUguiForagingLootBindings();
            int[] colRow = new int[3];
            for (int i = 0; i < handle.LootBindings.Length; i++)
            {
                UguiForagingLootBinding b = handle.LootBindings[i];
                // Flag-only write — deliberately NO SaveKeybinds/AddMenuNotification (source
                // parity); the binding's Set delegate is passed directly (Logging round shape).
                Toggle tog = this.CreateUguiCheckbox(loot.transform, "Loot" + i,
                    this.L(b.Label), b.Get(), b.Set);
                PlaceUguiTopLeft(tog.gameObject, colX[b.Column], 68f + colRow[b.Column] * 26f, colW, 22f);
                // 14pt checkbox labels overflow a 128px column ("Mustard Greens") — the IMGUI
                // twin wraps these via DrawWrappedSwitchToggle; here a smaller size fits one line.
                this.TrySetUguiButtonLabelSize(tog.gameObject, 12.5f);
                handle.LootToggles.Add(tog);
                colRow[b.Column]++;
            }

            handle.PriorityLocationShown = this.BuildUguiForagingPriorityLocationText();
            handle.PriorityLocationLabel = this.CreateUguiBodyLabel(loot.transform, "PriorityLocation",
                handle.PriorityLocationShown, 13f);
            PlaceUguiTopLeft(handle.PriorityLocationLabel, 18f, 284f, panelW - 36f, 20f);

            handle.LayoutSignature = this.ComputeUguiForagingLayoutSignature();
            this.RelayoutUguiShellForaging(handle);

            handle.Root = block;
            this.uguiShellForaging = handle;
            return block;
        }

        // Positions the conditional pieces from the CURRENT auraFarmEnabled/autoFarmAutoStopEnabled
        // state — the UGUI analog of the IMGUI drawer's settingsHeight formula + rowY accumulation
        // (Farm.cs:235-349). Reposition/SetActive/resize only; nothing is rebuilt.
        private void RelayoutUguiShellForaging(UguiShellForagingHandle handle)
        {
            bool aura = this.auraFarmEnabled;
            bool autoStop = this.autoFarmAutoStopEnabled;

            float panelW = handle.ContentWidth - 16f;
            const float settingsY = 8f + 112f + 14f; // below the fixed FORAGING panel

            SetUguiGoActive(handle.CollectWaitLabel, aura);
            SetUguiGoActive(handle.CollectWaitSlider != null ? handle.CollectWaitSlider.gameObject : null, aura);
            SetUguiGoActive(handle.TeleportDelayLabel, aura);
            SetUguiGoActive(handle.TeleportDelaySlider != null ? handle.TeleportDelaySlider.gameObject : null, aura);

            float rowY = aura ? 178f : 110f; // after 42 (area load) + 76 (aura toggle) [+ 110/144]
            if (handle.AutoStopToggle != null)
            {
                PlaceUguiTopLeft(handle.AutoStopToggle.gameObject, 14f, rowY, 250f, 24f);
            }
            float settingsBottom = rowY + 25f;

            SetUguiGoActive(handle.TimerCaption, autoStop);
            SetUguiGoActive(handle.HoursField != null ? handle.HoursField.gameObject : null, autoStop);
            SetUguiGoActive(handle.TimerColon1, autoStop);
            SetUguiGoActive(handle.MinutesField != null ? handle.MinutesField.gameObject : null, autoStop);
            SetUguiGoActive(handle.TimerColon2, autoStop);
            SetUguiGoActive(handle.SecondsField != null ? handle.SecondsField.gameObject : null, autoStop);
            SetUguiGoActive(handle.TimerStatusLabel, autoStop);
            if (autoStop)
            {
                rowY += 34f;
                PlaceUguiTopLeft(handle.TimerCaption, 14f, rowY, 110f, 20f);
                if (handle.HoursField != null)
                {
                    PlaceUguiTopLeft(handle.HoursField.gameObject, 126f, rowY, 46f, 22f);
                }
                PlaceUguiTopLeft(handle.TimerColon1, 176f, rowY, 10f, 20f);
                if (handle.MinutesField != null)
                {
                    PlaceUguiTopLeft(handle.MinutesField.gameObject, 190f, rowY, 46f, 22f);
                }
                PlaceUguiTopLeft(handle.TimerColon2, 240f, rowY, 10f, 20f);
                if (handle.SecondsField != null)
                {
                    PlaceUguiTopLeft(handle.SecondsField.gameObject, 254f, rowY, 46f, 22f);
                }
                PlaceUguiTopLeft(handle.TimerStatusLabel, 306f, rowY + 1f, panelW - 314f, 20f);
                settingsBottom = rowY + 22f;
            }

            float settingsH = settingsBottom + 16f;
            if (handle.SettingsPanel != null)
            {
                PlaceUguiTopLeft(handle.SettingsPanel, 8f, settingsY, panelW, settingsH);
            }

            float lootY = settingsY + settingsH + 14f;
            if (handle.LootPanel != null)
            {
                PlaceUguiTopLeft(handle.LootPanel, 8f, lootY, panelW, 318f);
            }

            this.SetUguiScrollContentHeight(handle.ScrollContent, lootY + 318f + 16f);
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (called from ProcessUguiShellOnUpdate)
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellForagingOnUpdate()
        {
            UguiShellForagingHandle handle = this.uguiShellForaging;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellResourceGatheringSubTabActive(UguiShellForagingSubIndex))
            {
                return;
            }

            try
            {
                // The behavioral status conditional — EVERY gated frame, unconditionally (its two
                // stop-cascades are real farm behavior; see file header). Only the label apply is
                // cached; the evaluation itself never is.
                this.EvaluateUguiForagingStatus(out string statusText, out Color statusColor);
                if (!string.Equals(statusText, handle.StatusShown, StringComparison.Ordinal)
                    || statusColor != handle.StatusColorShown)
                {
                    handle.StatusShown = statusText;
                    handle.StatusColorShown = statusColor;
                    this.SetUguiLabelText(handle.StatusTextLabel, statusText);
                    this.SetUguiLabelColor(handle.StatusTextLabel, statusColor);
                }

                string actionText = this.BuildUguiForagingActionText();
                if (!string.Equals(actionText, handle.ActionShown, StringComparison.Ordinal))
                {
                    handle.ActionShown = actionText;
                    this.SetUguiButtonLabel(handle.ActionButton, actionText);
                }

                this.SyncUguiSelfLabelText(handle.ModeLabel, ref handle.ModeShown,
                    this.auraFarmEnabled ? "Aura Farm" : "No mode");
                SetUguiGoActive(handle.CameraStuckLabel, this.cameraStuckDisplayTimer > 0f);

                // Settings re-syncs (external IMGUI edits) — WithoutNotify only.
                this.SyncUguiToggleFromField(handle.AuraFarmToggle, this.auraFarmEnabled);
                this.SyncUguiToggleFromField(handle.AutoStopToggle, this.autoFarmAutoStopEnabled);
                if (handle.AreaLoadSlider != null && Mathf.Abs(handle.AreaLoadSlider.value - this.areaLoadDelay) > 0.0005f)
                {
                    handle.AreaLoadSlider.SetValueWithoutNotify(this.areaLoadDelay);
                }
                this.SyncUguiSelfLabelText(handle.AreaLoadLabel, ref handle.AreaLoadShown,
                    this.LF("Area Load Delay: {0}s", (int)this.areaLoadDelay));
                if (handle.CollectWaitSlider != null && Mathf.Abs(handle.CollectWaitSlider.value - this.auraCollectWaitTimeout) > 0.0005f)
                {
                    handle.CollectWaitSlider.SetValueWithoutNotify(this.auraCollectWaitTimeout);
                }
                this.SyncUguiSelfLabelText(handle.CollectWaitLabel, ref handle.CollectWaitShown,
                    this.LF("Collect Wait Max: {0}s", (int)this.auraCollectWaitTimeout));
                if (handle.TeleportDelaySlider != null && Mathf.Abs(handle.TeleportDelaySlider.value - this.foragingTeleportDelaySeconds) > 0.0005f)
                {
                    handle.TeleportDelaySlider.SetValueWithoutNotify(this.foragingTeleportDelaySeconds);
                }
                this.SyncUguiSelfLabelText(handle.TeleportDelayLabel, ref handle.TeleportDelayShown,
                    this.LF("Teleport Delay: {0}s", (int)this.foragingTeleportDelaySeconds));

                // Loot-priority toggles (13 cheap bool compares).
                if (handle.LootBindings != null)
                {
                    for (int i = 0; i < handle.LootBindings.Length && i < handle.LootToggles.Count; i++)
                    {
                        this.SyncUguiToggleFromField(handle.LootToggles[i], handle.LootBindings[i].Get());
                    }
                }

                // Conditional-layout signature (aura block + timer row + panel heights).
                int signature = this.ComputeUguiForagingLayoutSignature();
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellForaging(handle);
                    handle.NextSlowSyncAt = 0f; // resolver/footer must not lag a layout change
                }

                // Countdown text — per gated frame while visible (it is a live per-second
                // countdown; the cached compare limits SetText churn to ~1/sec).
                if (this.autoFarmAutoStopEnabled)
                {
                    this.SyncUguiSelfLabelText(handle.TimerStatusLabel, ref handle.TimerStatusShown,
                        this.BuildUguiForagingTimerText());
                }

                // 0.5s tick: resolver readout, priority-location footer, input-field external
                // re-syncs (IMGUI edits of the shared autoFarmAutoStop*Input strings).
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;

                    this.BuildUguiForagingResolverDisplay(out string resolverText, out Color resolverColor);
                    if (!string.Equals(resolverText, handle.ResolverShown, StringComparison.Ordinal)
                        || resolverColor != handle.ResolverColorShown)
                    {
                        handle.ResolverShown = resolverText;
                        handle.ResolverColorShown = resolverColor;
                        this.SetUguiLabelText(handle.ResolverLabel, resolverText);
                        this.SetUguiLabelColor(handle.ResolverLabel, resolverColor);
                    }

                    this.SyncUguiSelfLabelText(handle.PriorityLocationLabel, ref handle.PriorityLocationShown,
                        this.BuildUguiForagingPriorityLocationText());

                    SyncUguiInputFieldFromBackingField(handle.HoursField, ref handle.HoursSeen, this.autoFarmAutoStopHoursInput);
                    SyncUguiInputFieldFromBackingField(handle.MinutesField, ref handle.MinutesSeen, this.autoFarmAutoStopMinutesInput);
                    SyncUguiInputFieldFromBackingField(handle.SecondsField, ref handle.SecondsSeen, this.autoFarmAutoStopSecondsInput);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Foraging content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Change handlers — each mirrors its IMGUI block EXACTLY (same side effects, same order)
        // ----------------------------------------------------------------------------------------

        // Farm.cs:156-159 — the primary button routes straight to ToggleAutoFarm (which owns all
        // start/stop side effects, including the error-status writes when preconditions fail).
        private void OnUguiForagingActionClicked()
        {
            this.ToggleAutoFarm();
        }

        // Farm.cs:243-246 — rounded to whole seconds, try/catch-wrapped save on change only.
        private void OnUguiForagingAreaLoadDelayChanged(float value)
        {
            float rounded = Mathf.Round(value);
            if (rounded == this.areaLoadDelay)
            {
                return;
            }
            this.areaLoadDelay = rounded;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Farm.cs:249-253 — a METHOD call, not a field write: SetAuraFarmEnabled owns the aura
        // scan/log/cache reset cascade (AuraFarm.cs:429).
        private void OnUguiForagingAuraFarmToggled(bool value)
        {
            if (value == this.auraFarmEnabled)
            {
                return;
            }
            this.SetAuraFarmEnabled(value);
        }

        // Farm.cs:282-292.
        private void OnUguiForagingCollectWaitChanged(float value)
        {
            float rounded = Mathf.Round(value);
            if (rounded == this.auraCollectWaitTimeout)
            {
                return;
            }
            this.auraCollectWaitTimeout = rounded;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Farm.cs:296-306.
        private void OnUguiForagingTeleportDelayChanged(float value)
        {
            float rounded = Mathf.Round(value);
            if (rounded == this.foragingTeleportDelaySeconds)
            {
                return;
            }
            this.foragingTeleportDelaySeconds = rounded;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Farm.cs:311 — flag only: the IMGUI drawer writes the switch return straight back with
        // no save and no notification (the flag is consumed by ToggleAutoFarm at start time).
        private void OnUguiForagingAutoStopToggled(bool value)
        {
            this.autoFarmAutoStopEnabled = value;
        }

        // Live parse-clamp-writeback core for one HH/MM/SS field — the IMGUI per-frame block
        // (Farm.cs:323-338) as an on-edit event: TryParse → Clamp(0..max) → the int field → the
        // SHARED input string (IMGUI binds the same string, so both surfaces show the same text)
        // → the UGUI field text re-normalized via SetTextWithoutNotify when it differs (typing
        // "99" in hours shows "23" immediately; "05" normalizes to "5"). A failed parse mirrors
        // IMGUI exactly: the raw text is kept in the shared string, the int field keeps its last
        // value, and nothing is echoed back. The Seen cache tracks every write so the 0.5s
        // external re-sync can never clobber in-progress typing.
        private void ApplyUguiForagingAutoStopEdit(InputField field, ref string seen, string text,
            int max, ref int backingValue, ref string backingInput)
        {
            string raw = text ?? string.Empty;
            int parsed;
            if (int.TryParse(raw, out parsed))
            {
                backingValue = Mathf.Clamp(parsed, 0, max);
                string normalized = backingValue.ToString();
                backingInput = normalized;
                seen = normalized;
                if (field != null && !string.Equals(normalized, raw, StringComparison.Ordinal))
                {
                    try { field.SetTextWithoutNotify(normalized); } catch { }
                }
            }
            else
            {
                backingInput = raw;
                seen = raw;
            }
        }

        private void OnUguiForagingAutoStopHoursEdited(string text)
        {
            UguiShellForagingHandle handle = this.uguiShellForaging;
            if (handle == null)
            {
                return;
            }
            this.ApplyUguiForagingAutoStopEdit(handle.HoursField, ref handle.HoursSeen, text, 23,
                ref this.autoFarmAutoStopHours, ref this.autoFarmAutoStopHoursInput);
        }

        private void OnUguiForagingAutoStopMinutesEdited(string text)
        {
            UguiShellForagingHandle handle = this.uguiShellForaging;
            if (handle == null)
            {
                return;
            }
            this.ApplyUguiForagingAutoStopEdit(handle.MinutesField, ref handle.MinutesSeen, text, 59,
                ref this.autoFarmAutoStopMinutes, ref this.autoFarmAutoStopMinutesInput);
        }

        private void OnUguiForagingAutoStopSecondsEdited(string text)
        {
            UguiShellForagingHandle handle = this.uguiShellForaging;
            if (handle == null)
            {
                return;
            }
            this.ApplyUguiForagingAutoStopEdit(handle.SecondsField, ref handle.SecondsSeen, text, 59,
                ref this.autoFarmAutoStopSeconds, ref this.autoFarmAutoStopSecondsInput);
        }
    }
}
