using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, round 5 (migration plan: cosmic-waddling-rainbow.md):
    // Self's four remaining sub-tabs — Main, Fun, Privacy, Game UI — deferred out of round 4
    // (which handled Self→Building + the floating Building Move Panel; that round's shared
    // builder/bind architecture is DONE and untouched here).
    //
    // Ground rules (same as rounds 1-4):
    //  - The IMGUI drawers (DrawSelfTab + its seven composed controls, DrawSelfFunTab,
    //    DrawPrivacyBlockExtraTab, DrawSelfGameUiTab) stay fully functional and untouched — this
    //    file only READS the same fields and CALLS the same action methods. Two independent
    //    rendering paths over one backend.
    //  - Wiring is by STATIC display-position index (UguiShellSelfTabIndex = 0 +
    //    UguiShellSelfMainSubIndex/FunSubIndex/PrivacySubIndex/GameUiSubIndex — declared next to
    //    their round-1..4 siblings in UguiShellTabIndices.cs), never by localized label comparison.
    //  - All four sub-tabs live inside the already-registered modal shell: no input-ownership
    //    entries, no theme registration of their own (the shell's "UguiShell" rebuilder re-runs
    //    these builders with fresh theme colors).
    //  - Toggles are kit CHECKBOXES (round-2 deviation note applies: CreateUguiSwitch's visuals
    //    are driven from its own onValueChanged closure, so silent WithoutNotify re-syncs would
    //    strand them — and it fires onChanged once at build, replaying side effects on every
    //    theme rebuild. The checkbox follows WithoutNotify updates for free). Both IMGUI switch
    //    variants localize their label internally (DrawSwitchToggle/DrawWrappedSwitchToggle both
    //    GUI.Label this.L(label)), so every checkbox label here goes through this.L too. The
    //    three IMGUI DrawWrappedSwitchToggle uses on Main exist purely for multi-line label
    //    layout in a 260px column — the shell rows are ~2x wider, so a standard full-width
    //    checkbox row fits those labels; no separate control.
    //
    // MAIN's 12 toggles have genuinely DISTINCT side-effect chains (reset cascades, apply/restore
    // pairs, AuraMono restore calls) — each gets its OWN named handler mirroring its IMGUI block
    // exactly (HeartopiaComplete.Gui.cs:1584-1921). Deliberately NOT a binding-array loop: the
    // Logging round's loop worked because all 39 flags were side-effect-free; these are not.
    // GAME UI's 7 sliders are the opposite case — genuinely uniform (same range, same rounding,
    // same save), so they DO use a data-driven loop over GameUiTimingSliderLabels, the Logging
    // round's array precedent.
    //
    // Cross-surface sync (all four sub-tabs): every backing field here is ALSO editable from the
    // still-live IMGUI twin, so per-frame processors — gated on "shell visible AND Self tab
    // active AND this exact sub-tab active" (IsUguiShellSelfSubTabActive, the round-4 gate) —
    // re-sync control state from the live fields. Toggles via Toggle.SetIsOnWithoutNotify,
    // sliders via Slider.SetValueWithoutNotify (NEVER the plain setters — those fire
    // onValueChanged and replay side effects). Cadence split:
    //  - Every gated frame: toggle bool compares, slider value compares, slider VALUE-labels
    //    (format + cached-string compare — the Building jog-row idiom; only SetText on change),
    //    and Main's conditional-section relayout signature. This is also what makes Game UI's
    //    "Reset to game defaults" reflect in its sliders on the next frame.
    //  - 0.5s throttle (NextSlowSyncAt, the Settings→Main slow-tick idiom): the genuinely LIVE
    //    text — Privacy's four counters + hooks-status line, Fun's two status lines, Game UI's
    //    status line. These change from background hooks, not user edits; 0.5s matches the
    //    Spawn-Vehicle status-refresh precedent.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handles (per-instance state — assigned LAST in each builder, Research idiom)
        // ----------------------------------------------------------------------------------------

        private sealed class UguiShellSelfMainHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public float ContentWidth;          // scroll content width (block w minus viewport insets)

            public Toggle CameraToggle;
            public Toggle CrosshairToggle;      // only visible while Camera Toggle is on
            public Toggle NoclipToggle;
            public Toggle VehicleBypassToggle;
            public Toggle VehicleBypassServerToggle;
            public GameObject NoclipSpeedLabel; // only visible while Noclip is on
            public string NoclipSpeedShown;
            public Slider NoclipSpeedSlider;
            public GameObject NoclipBoostLabel;
            public string NoclipBoostShown;
            public Slider NoclipBoostSlider;
            public Toggle AntiAfkToggle;
            public GameObject AfkIntervalLabel; // only visible while Anti AFK is on
            public string AfkIntervalShown;
            public Slider AfkIntervalSlider;
            public Toggle WarehouseToggle;
            public Toggle StrangerChatToggle;
            public Toggle ChatTranslateToggle;
            public Toggle ChatTranslateDebugToggle;
            public Toggle ChatTranslateForceAllToggle;
            public Toggle ChatTranslatePostcardToggle;
            public GameObject GameSpeedLabel;   // unconditional
            public string GameSpeedShown;
            public Slider GameSpeedSlider;
            public Toggle CustomFovToggle;
            public GameObject FovLabel;         // unconditional (value only APPLIES while toggle on)
            public string FovShown;
            public Slider FovSlider;
            public Toggle AnalogMoveToggle;
            public GameObject AnalogMoveHint;
            public Toggle SkipShowOffToggle;
            public GameObject NoclipHelpLabel;  // trailing, only visible while Noclip is on

            public int LayoutSignature = -1;    // packed conditional-visibility state
            public int ErrorCount;              // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private sealed class UguiShellSelfFunHandle
        {
            public GameObject Root;
            public Toggle ForceSkateToggle;
            public Toggle ForceSwimToggle;
            public GameObject LocomotionStatusLabel;   // live (forceLocomotionLastStatus)
            public string LocomotionStatusShown;
            public Toggle SwimSprintToggle;
            public GameObject SprintDurationLabel;     // "∞" display rule at the max
            public string SprintDurationShown;
            public Slider SprintDurationSlider;
            public GameObject SprintCooldownLabel;
            public string SprintCooldownShown;
            public Slider SprintCooldownSlider;
            public GameObject SprintStatusLabel;       // live (swimSprintTweakStatus suffix)
            public string SprintStatusShown;
            public Toggle VerticalGuardToggle;
            public float NextSlowSyncAt;               // 0.5s tick for the two live status lines
            public int ErrorCount;
        }

        private sealed class UguiShellSelfPrivacyHandle
        {
            public GameObject Root;
            public Toggle LogsToggle;
            public Toggle MergesToggle;
            public Toggle SpamsToggle;
            public Toggle UploadCheatToggle;
            public GameObject LogsCountLabel;
            public string LogsCountShown;
            public GameObject MergesCountLabel;
            public string MergesCountShown;
            public GameObject SpamsCountLabel;
            public string SpamsCountShown;
            public GameObject UploadCheatCountLabel;
            public string UploadCheatCountShown;
            public GameObject HooksStatusLabel;
            public string HooksStatusShown;
            public float NextSlowSyncAt;               // 0.5s tick for counters + hooks status
            public int ErrorCount;
        }

        private sealed class UguiShellSelfGameUiHandle
        {
            public GameObject Root;
            public Toggle EnabledToggle;
            public readonly List<GameObject> TimingLabels = new List<GameObject>();
            public readonly List<string> TimingShown = new List<string>();
            public readonly List<Slider> TimingSliders = new List<Slider>();
            public GameObject StatusLabel;             // live (gameUiTimingsStatus suffix)
            public string StatusShown;
            public float NextSlowSyncAt;               // 0.5s tick for the status line
            public int ErrorCount;
        }

        private UguiShellSelfMainHandle uguiShellSelfMain;
        private UguiShellSelfFunHandle uguiShellSelfFun;
        private UguiShellSelfPrivacyHandle uguiShellSelfPrivacy;
        private UguiShellSelfGameUiHandle uguiShellSelfGameUi;

        // Cached-string label refresh (Building jog-row ValueShown idiom): format is the caller's
        // job; SetText only fires when the text actually changed (TMP re-layout hygiene).
        private void SyncUguiSelfLabelText(GameObject label, ref string shown, string text)
        {
            if (!string.Equals(text, shown, StringComparison.Ordinal))
            {
                shown = text;
                this.SetUguiLabelText(label, text);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Self → Main (12 side-effectful toggles + 5 sliders — DrawSelfTab:1584-1706 + the seven
        // composed controls at :1732-1921). Content is ~2x the cell height, so it scrolls
        // (Settings→Main precedent); conditional sections reposition via relayout-on-signature.
        // ----------------------------------------------------------------------------------------

        private GameObject BuildUguiShellSelfMainContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellSelfMain = null;

            UguiShellSelfMainHandle handle = new UguiShellSelfMainHandle();
            GameObject block = this.CreateUguiGo("SelfMainContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            Transform scrollContent;
            GameObject scroll = this.CreateUguiScrollView(block.transform, "Scroll", 10f, out scrollContent);
            PlaceUguiTopLeft(scroll, 0f, 0f, w, h);
            // Flat look over the block's ContentBg (Logging idiom) — alpha-0 images still raycast,
            // so wheel/drag scrolling keeps working.
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

            Color muted = this.UguiKitMutedColor();

            // Controls are created in IMGUI source order; positions/visibility belong to
            // RelayoutUguiShellSelfMain (the y-cursor accumulation analog), called once below.
            handle.CameraToggle = this.CreateUguiCheckbox(scrollContent, "CameraToggle",
                this.L("Camera Toggle"), this.mouseLookEnabled,
                new System.Action<bool>(this.OnUguiSelfMouseLookToggled));
            handle.CrosshairToggle = this.CreateUguiCheckbox(scrollContent, "CrosshairToggle",
                this.L("Show Crosshair"), this.showMouseLookCrosshair,
                new System.Action<bool>(this.OnUguiSelfCrosshairToggled));
            handle.NoclipToggle = this.CreateUguiCheckbox(scrollContent, "NoclipToggle",
                this.L("Noclip"), this.noclipEnabled,
                new System.Action<bool>(this.OnUguiSelfNoclipToggled));
            handle.VehicleBypassToggle = this.CreateUguiCheckbox(scrollContent, "VehicleBypassToggle",
                this.L("Vehicle Bypass"), this.vehicleBypassEnabled,
                new System.Action<bool>(this.OnUguiSelfVehicleBypassToggled));
            handle.VehicleBypassServerToggle = this.CreateUguiCheckbox(scrollContent, "VehicleBypassServerToggle",
                this.L("Vehicle Bypass Server Events"), this.vehicleBypassServerEventsEnabled,
                new System.Action<bool>(this.OnUguiSelfVehicleBypassServerToggled));

            handle.NoclipSpeedShown = this.LF("Noclip Speed: {0:F1}", this.noclipSpeed);
            handle.NoclipSpeedLabel = this.CreateUguiBodyLabel(scrollContent, "NoclipSpeedLabel", handle.NoclipSpeedShown, 13f);
            handle.NoclipSpeedSlider = this.CreateUguiSlider(scrollContent, "NoclipSpeedSlider",
                5f, 50f, this.noclipSpeed, false,
                new System.Action<float>(this.OnUguiSelfNoclipSpeedChanged));
            handle.NoclipBoostShown = this.LF("Noclip Boost: {0:F1}x", this.noclipBoostMultiplier);
            handle.NoclipBoostLabel = this.CreateUguiBodyLabel(scrollContent, "NoclipBoostLabel", handle.NoclipBoostShown, 13f);
            handle.NoclipBoostSlider = this.CreateUguiSlider(scrollContent, "NoclipBoostSlider",
                1f, 5f, this.noclipBoostMultiplier, false,
                new System.Action<float>(this.OnUguiSelfNoclipBoostChanged));

            handle.AntiAfkToggle = this.CreateUguiCheckbox(scrollContent, "AntiAfkToggle",
                this.L("Anti AFK (Auto Click)"), this.antiAfkEnabled,
                new System.Action<bool>(this.OnUguiSelfAntiAfkToggled));
            handle.AfkIntervalShown = this.LF("AFK Click Interval: {0:F0}s", this.antiAfkInterval);
            handle.AfkIntervalLabel = this.CreateUguiBodyLabel(scrollContent, "AfkIntervalLabel", handle.AfkIntervalShown, 13f);
            handle.AfkIntervalSlider = this.CreateUguiSlider(scrollContent, "AfkIntervalSlider",
                5f, 9f, this.antiAfkInterval, false,
                new System.Action<float>(this.OnUguiSelfAfkIntervalChanged));

            handle.WarehouseToggle = this.CreateUguiCheckbox(scrollContent, "WarehouseToggle",
                this.L("Warehouse Anywhere"), this.warehouseBypassEnabled,
                new System.Action<bool>(this.OnUguiSelfWarehouseBypassToggled));
            handle.StrangerChatToggle = this.CreateUguiCheckbox(scrollContent, "StrangerChatToggle",
                this.L("Stranger Chat Bypass"), this.strangerChatBypassEnabled,
                new System.Action<bool>(this.OnUguiSelfStrangerChatBypassToggled));
            handle.ChatTranslateToggle = this.CreateUguiCheckbox(scrollContent, "ChatTranslateToggle",
                this.L("Chat Translate Unlock"), this.chatForceTranslateEnabled,
                new System.Action<bool>(this.OnUguiSelfChatTranslateToggled));
            handle.ChatTranslateDebugToggle = this.CreateUguiCheckbox(scrollContent, "ChatTranslateDebugToggle",
                this.L("Chat Translate: Debug Log"), this.chatTranslateVerboseLog,
                new System.Action<bool>(this.OnUguiSelfChatTranslateDebugToggled));
            handle.ChatTranslateForceAllToggle = this.CreateUguiCheckbox(scrollContent, "ChatTranslateForceAllToggle",
                this.L("Chat Translate: Force ALL Languages"), this.chatTranslateForceAllLangs,
                new System.Action<bool>(this.OnUguiSelfChatTranslateForceAllToggled));
            handle.ChatTranslatePostcardToggle = this.CreateUguiCheckbox(scrollContent, "ChatTranslatePostcardToggle",
                this.L("Chat Translate: Postcard Bypass (test)"), this.chatTranslatePostcardBypass,
                new System.Action<bool>(this.OnUguiSelfChatTranslatePostcardToggled));

            handle.GameSpeedShown = this.LF("Game Speed: {0:F1}x", this.gameSpeed);
            handle.GameSpeedLabel = this.CreateUguiBodyLabel(scrollContent, "GameSpeedLabel", handle.GameSpeedShown, 13f);
            handle.GameSpeedSlider = this.CreateUguiSlider(scrollContent, "GameSpeedSlider",
                1f, 10f, this.gameSpeed, false,
                new System.Action<float>(this.OnUguiSelfGameSpeedChanged));

            handle.CustomFovToggle = this.CreateUguiCheckbox(scrollContent, "CustomFovToggle",
                this.L("Custom Camera FOV"), this.customCameraFOVEnabled,
                new System.Action<bool>(this.OnUguiSelfCustomFovToggled));
            handle.FovShown = this.LF("Camera FOV: {0:F0}", this.cameraFOV);
            handle.FovLabel = this.CreateUguiBodyLabel(scrollContent, "FovLabel", handle.FovShown, 13f);
            handle.FovSlider = this.CreateUguiSlider(scrollContent, "FovSlider",
                30f, 120f, this.cameraFOV, false,
                new System.Action<float>(this.OnUguiSelfCameraFovChanged));

            handle.AnalogMoveToggle = this.CreateUguiCheckbox(scrollContent, "AnalogMoveToggle",
                this.L("Analog Move (gamepad stick)"), this.analogMoveBridgeEnabled,
                new System.Action<bool>(this.OnUguiSelfAnalogMoveToggled));
            // Static help line — unlocalized in the IMGUI drawer (raw GUI.Label), kept verbatim.
            handle.AnalogMoveHint = this.CreateUguiLabel(scrollContent, "AnalogMoveHint",
                "Drives the character from the gamepad left stick (and WASD).",
                11f, new Color(muted.r, muted.g, muted.b, 0.85f), false);
            this.TrySetUguiLabelWrapped(handle.AnalogMoveHint);

            handle.SkipShowOffToggle = this.CreateUguiCheckbox(scrollContent, "SkipShowOffToggle",
                this.L("Skip Show Off animations"), this.skipShowOffAnimations,
                new System.Action<bool>(this.OnUguiSelfSkipShowOffToggled));

            // Trailing conditional help label — localized in the IMGUI drawer, kept verbatim.
            handle.NoclipHelpLabel = this.CreateUguiLabel(scrollContent, "NoclipHelp",
                this.L("Noclip: WASD + Space/Ctrl\nShift = Speed Boost"),
                11f, new Color(muted.r, muted.g, muted.b, 0.85f), false);
            this.TrySetUguiLabelWrapped(handle.NoclipHelpLabel);

            handle.LayoutSignature = this.ComputeUguiSelfMainLayoutSignature();
            this.RelayoutUguiShellSelfMain(handle);

            handle.Root = block;
            this.uguiShellSelfMain = handle;
            return block;
        }

        private int ComputeUguiSelfMainLayoutSignature()
        {
            return (this.mouseLookEnabled ? 1 : 0)
                 | (this.noclipEnabled ? 2 : 0)
                 | (this.antiAfkEnabled ? 4 : 0);
        }

        // Positions every Main control from the CURRENT conditional state — the UGUI analog of
        // DrawSelfTab's y-cursor accumulation. Reposition/SetActive only; nothing is rebuilt.
        // Conditional sections mirror IMGUI exactly: Crosshair under Camera Toggle; the two
        // noclip sliders under the vehicle-bypass toggles; the AFK interval under Anti AFK; the
        // noclip help footer at the very end.
        private void RelayoutUguiShellSelfMain(UguiShellSelfMainHandle handle)
        {
            bool mouseLook = this.mouseLookEnabled;
            bool noclip = this.noclipEnabled;
            bool antiAfk = this.antiAfkEnabled;

            const float rowX = 8f;
            const float labelW = 200f;
            float rowW = handle.ContentWidth - 16f;
            float sliderX = rowX + labelW + 10f;
            float sliderW = handle.ContentWidth - sliderX - 8f;
            float yCur = 8f;

            if (handle.CameraToggle != null)
            {
                PlaceUguiTopLeft(handle.CameraToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;

            SetUguiGoActive(handle.CrosshairToggle != null ? handle.CrosshairToggle.gameObject : null, mouseLook);
            if (mouseLook)
            {
                if (handle.CrosshairToggle != null)
                {
                    PlaceUguiTopLeft(handle.CrosshairToggle.gameObject, rowX, yCur, rowW, 24f);
                }
                yCur += 30f;
            }

            if (handle.NoclipToggle != null)
            {
                PlaceUguiTopLeft(handle.NoclipToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;
            if (handle.VehicleBypassToggle != null)
            {
                PlaceUguiTopLeft(handle.VehicleBypassToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;
            if (handle.VehicleBypassServerToggle != null)
            {
                PlaceUguiTopLeft(handle.VehicleBypassServerToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;

            SetUguiGoActive(handle.NoclipSpeedLabel, noclip);
            SetUguiGoActive(handle.NoclipSpeedSlider != null ? handle.NoclipSpeedSlider.gameObject : null, noclip);
            SetUguiGoActive(handle.NoclipBoostLabel, noclip);
            SetUguiGoActive(handle.NoclipBoostSlider != null ? handle.NoclipBoostSlider.gameObject : null, noclip);
            if (noclip)
            {
                if (handle.NoclipSpeedLabel != null)
                {
                    PlaceUguiTopLeft(handle.NoclipSpeedLabel, rowX, yCur + 2f, labelW, 20f);
                }
                if (handle.NoclipSpeedSlider != null)
                {
                    PlaceUguiTopLeft(handle.NoclipSpeedSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
                }
                yCur += 28f;
                if (handle.NoclipBoostLabel != null)
                {
                    PlaceUguiTopLeft(handle.NoclipBoostLabel, rowX, yCur + 2f, labelW, 20f);
                }
                if (handle.NoclipBoostSlider != null)
                {
                    PlaceUguiTopLeft(handle.NoclipBoostSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
                }
                yCur += 28f;
            }

            if (handle.AntiAfkToggle != null)
            {
                PlaceUguiTopLeft(handle.AntiAfkToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;

            SetUguiGoActive(handle.AfkIntervalLabel, antiAfk);
            SetUguiGoActive(handle.AfkIntervalSlider != null ? handle.AfkIntervalSlider.gameObject : null, antiAfk);
            if (antiAfk)
            {
                if (handle.AfkIntervalLabel != null)
                {
                    PlaceUguiTopLeft(handle.AfkIntervalLabel, rowX, yCur + 2f, labelW, 20f);
                }
                if (handle.AfkIntervalSlider != null)
                {
                    PlaceUguiTopLeft(handle.AfkIntervalSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
                }
                yCur += 28f;
            }

            if (handle.WarehouseToggle != null)
            {
                PlaceUguiTopLeft(handle.WarehouseToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;
            if (handle.StrangerChatToggle != null)
            {
                PlaceUguiTopLeft(handle.StrangerChatToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;
            if (handle.ChatTranslateToggle != null)
            {
                PlaceUguiTopLeft(handle.ChatTranslateToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;

            if (handle.GameSpeedLabel != null)
            {
                PlaceUguiTopLeft(handle.GameSpeedLabel, rowX, yCur + 2f, labelW, 20f);
            }
            if (handle.GameSpeedSlider != null)
            {
                PlaceUguiTopLeft(handle.GameSpeedSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            }
            yCur += 28f;

            if (handle.CustomFovToggle != null)
            {
                PlaceUguiTopLeft(handle.CustomFovToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;
            if (handle.FovLabel != null)
            {
                PlaceUguiTopLeft(handle.FovLabel, rowX, yCur + 2f, labelW, 20f);
            }
            if (handle.FovSlider != null)
            {
                PlaceUguiTopLeft(handle.FovSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            }
            yCur += 28f;

            if (handle.AnalogMoveToggle != null)
            {
                PlaceUguiTopLeft(handle.AnalogMoveToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 28f;
            if (handle.AnalogMoveHint != null)
            {
                PlaceUguiTopLeft(handle.AnalogMoveHint, rowX, yCur, rowW, 18f);
            }
            yCur += 24f;

            if (handle.SkipShowOffToggle != null)
            {
                PlaceUguiTopLeft(handle.SkipShowOffToggle.gameObject, rowX, yCur, rowW, 24f);
            }
            yCur += 30f;

            SetUguiGoActive(handle.NoclipHelpLabel, noclip);
            if (noclip)
            {
                if (handle.NoclipHelpLabel != null)
                {
                    PlaceUguiTopLeft(handle.NoclipHelpLabel, rowX, yCur, rowW, 36f);
                }
                yCur += 42f;
            }

            this.SetUguiScrollContentHeight(handle.ScrollContent, yCur + 8f);
        }

        // Called every frame from ProcessUguiShellOnUpdate; skips in a few comparisons unless the
        // shell is visible ON Self→Main.
        private void ProcessUguiShellSelfMainOnUpdate()
        {
            UguiShellSelfMainHandle handle = this.uguiShellSelfMain;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSelfSubTabActive(UguiShellSelfMainSubIndex))
            {
                return;
            }

            try
            {
                this.SyncUguiToggleFromField(handle.CameraToggle, this.mouseLookEnabled);
                this.SyncUguiToggleFromField(handle.CrosshairToggle, this.showMouseLookCrosshair);
                this.SyncUguiToggleFromField(handle.NoclipToggle, this.noclipEnabled);
                this.SyncUguiToggleFromField(handle.VehicleBypassToggle, this.vehicleBypassEnabled);
                this.SyncUguiToggleFromField(handle.VehicleBypassServerToggle, this.vehicleBypassServerEventsEnabled);
                this.SyncUguiToggleFromField(handle.AntiAfkToggle, this.antiAfkEnabled);
                this.SyncUguiToggleFromField(handle.WarehouseToggle, this.warehouseBypassEnabled);
                this.SyncUguiToggleFromField(handle.StrangerChatToggle, this.strangerChatBypassEnabled);
                this.SyncUguiToggleFromField(handle.ChatTranslateToggle, this.chatForceTranslateEnabled);
                this.SyncUguiToggleFromField(handle.ChatTranslateDebugToggle, this.chatTranslateVerboseLog);
                this.SyncUguiToggleFromField(handle.ChatTranslateForceAllToggle, this.chatTranslateForceAllLangs);
                this.SyncUguiToggleFromField(handle.ChatTranslatePostcardToggle, this.chatTranslatePostcardBypass);
                this.SyncUguiToggleFromField(handle.CustomFovToggle, this.customCameraFOVEnabled);
                this.SyncUguiToggleFromField(handle.AnalogMoveToggle, this.analogMoveBridgeEnabled);
                this.SyncUguiToggleFromField(handle.SkipShowOffToggle, this.skipShowOffAnimations);

                if (handle.NoclipSpeedSlider != null && Mathf.Abs(handle.NoclipSpeedSlider.value - this.noclipSpeed) > 0.0005f)
                {
                    handle.NoclipSpeedSlider.SetValueWithoutNotify(this.noclipSpeed);
                }
                this.SyncUguiSelfLabelText(handle.NoclipSpeedLabel, ref handle.NoclipSpeedShown,
                    this.LF("Noclip Speed: {0:F1}", this.noclipSpeed));
                if (handle.NoclipBoostSlider != null && Mathf.Abs(handle.NoclipBoostSlider.value - this.noclipBoostMultiplier) > 0.0005f)
                {
                    handle.NoclipBoostSlider.SetValueWithoutNotify(this.noclipBoostMultiplier);
                }
                this.SyncUguiSelfLabelText(handle.NoclipBoostLabel, ref handle.NoclipBoostShown,
                    this.LF("Noclip Boost: {0:F1}x", this.noclipBoostMultiplier));
                if (handle.AfkIntervalSlider != null && Mathf.Abs(handle.AfkIntervalSlider.value - this.antiAfkInterval) > 0.0005f)
                {
                    handle.AfkIntervalSlider.SetValueWithoutNotify(this.antiAfkInterval);
                }
                this.SyncUguiSelfLabelText(handle.AfkIntervalLabel, ref handle.AfkIntervalShown,
                    this.LF("AFK Click Interval: {0:F0}s", this.antiAfkInterval));
                if (handle.GameSpeedSlider != null && Mathf.Abs(handle.GameSpeedSlider.value - this.gameSpeed) > 0.0005f)
                {
                    handle.GameSpeedSlider.SetValueWithoutNotify(this.gameSpeed);
                }
                this.SyncUguiSelfLabelText(handle.GameSpeedLabel, ref handle.GameSpeedShown,
                    this.LF("Game Speed: {0:F1}x", this.gameSpeed));
                if (handle.FovSlider != null && Mathf.Abs(handle.FovSlider.value - this.cameraFOV) > 0.0005f)
                {
                    handle.FovSlider.SetValueWithoutNotify(this.cameraFOV);
                }
                this.SyncUguiSelfLabelText(handle.FovLabel, ref handle.FovShown,
                    this.LF("Camera FOV: {0:F0}", this.cameraFOV));

                int signature = this.ComputeUguiSelfMainLayoutSignature();
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RelayoutUguiShellSelfMain(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Self Main content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // --- Main-tab change handlers — each mirrors its IMGUI block EXACTLY (same side-effect
        // chain, same save/notify order, same one-direction-only calls where IMGUI has them).
        // Every handler guards on "value actually changed" so a redundant event (or the
        // WithoutNotify re-syncs, which never fire these) cannot replay side effects. ------------

        // Gui.cs:1586-1595 — save, THEN mouse-look state refresh, THEN notification.
        private void OnUguiSelfMouseLookToggled(bool value)
        {
            if (value == this.mouseLookEnabled)
            {
                return;
            }
            this.mouseLookEnabled = value;
            this.SaveKeybinds(false);
            this.UpdateMouseLookState();
            this.AddMenuNotification(
                $"Camera Toggle {(this.mouseLookEnabled ? "Enabled" : "Disabled")}",
                this.mouseLookEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
        }

        // Gui.cs:1600-1608.
        private void OnUguiSelfCrosshairToggled(bool value)
        {
            if (value == this.showMouseLookCrosshair)
            {
                return;
            }
            this.showMouseLookCrosshair = value;
            this.SaveKeybinds(false);
            this.AddMenuNotification(
                $"Crosshair {(this.showMouseLookCrosshair ? "Enabled" : "Disabled")}",
                this.showMouseLookCrosshair ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
        }

        // Gui.cs:1612-1621 — the exact if/else; NO notification and NO save in the source
        // (steady-state noclip is owned by ProcessNoclipMovementOnUpdate; this is the edge init/
        // teardown only).
        private void OnUguiSelfNoclipToggled(bool value)
        {
            if (value == this.noclipEnabled)
            {
                return;
            }
            this.noclipEnabled = value;
            if (this.noclipEnabled)
            {
                this.InitializeNoclipDriveState();
            }
            else
            {
                this.ClearNoclipVehicleOverride();
            }
        }

        // Gui.cs:1623-1630 — notification only, both directions; no save in the source.
        private void OnUguiSelfVehicleBypassToggled(bool value)
        {
            if (value == this.vehicleBypassEnabled)
            {
                return;
            }
            this.vehicleBypassEnabled = value;
            this.AddMenuNotification(
                "Vehicle Bypass " + (this.vehicleBypassEnabled ? "Enabled" : "Disabled"),
                this.vehicleBypassEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
        }

        // Gui.cs:1633-1643.
        private void OnUguiSelfVehicleBypassServerToggled(bool value)
        {
            if (value == this.vehicleBypassServerEventsEnabled)
            {
                return;
            }
            this.vehicleBypassServerEventsEnabled = value;
            this.AddMenuNotification(
                "Vehicle Bypass Server Events " + (this.vehicleBypassServerEventsEnabled ? "Enabled" : "Disabled"),
                this.vehicleBypassServerEventsEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
        }

        // Gui.cs:1650-1655 — try/catch-wrapped save, no notification.
        private void OnUguiSelfNoclipSpeedChanged(float value)
        {
            if (Mathf.Abs(value - this.noclipSpeed) <= 0.0001f)
            {
                return;
            }
            this.noclipSpeed = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:1660-1665.
        private void OnUguiSelfNoclipBoostChanged(float value)
        {
            if (Mathf.Abs(value - this.noclipBoostMultiplier) <= 0.0001f)
            {
                return;
            }
            this.noclipBoostMultiplier = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:1669-1676 — anti-AFK pulse timestamp reset + save + notification.
        private void OnUguiSelfAntiAfkToggled(bool value)
        {
            if (value == this.antiAfkEnabled)
            {
                return;
            }
            this.antiAfkEnabled = value;
            this.lastAntiAfkPulseAt = Time.unscaledTime;
            this.SaveKeybinds(false);
            this.AddMenuNotification(
                $"Anti AFK {(this.antiAfkEnabled ? "Enabled" : "Disabled")}",
                this.antiAfkEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
        }

        // Gui.cs:1683-1688 — plain save (NOT try/catch-wrapped in the source), no notification.
        private void OnUguiSelfAfkIntervalChanged(float value)
        {
            if (Mathf.Abs(value - this.antiAfkInterval) <= 0.01f)
            {
                return;
            }
            this.antiAfkInterval = value;
            this.SaveKeybinds(false);
        }

        // Gui.cs:1732-1762 — the FULL reset cascade, verbatim: feature static reset + seven
        // retry/log-latch field resets + the disable-only AuraMono type-object drop, then save,
        // then the localized notification.
        private void OnUguiSelfWarehouseBypassToggled(bool value)
        {
            if (value == this.warehouseBypassEnabled)
            {
                return;
            }
            this.warehouseBypassEnabled = value;
            WarehouseBypassFeature.ResetState();
            this.warehouseMonoTabGiveUp = false;
            this.warehouseMonoTabNextAttemptAt = -999f;
            this.warehouseMonoTabUnlockCommitted = false;
            this.warehouseMonoTabUnlockedLogged = false;
            this.warehouseMonoMoveButtonLogged = false;
            this.warehouseMonoTabIconLogged = false;
            this.warehouseBagOpenBypassCacheFrame = -1;
            if (!this.warehouseBypassEnabled)
            {
                this.warehouseAuraBagPanelTypeObj = IntPtr.Zero;
            }
            this.SaveKeybinds(false);
            if (this.warehouseBypassEnabled)
            {
                this.AddMenuNotification(this.L("Warehouse Anywhere Enabled"), new Color(0.55f, 0.88f, 1f));
            }
            else
            {
                this.AddMenuNotification(this.L("Warehouse Anywhere Disabled"), new Color(0.88f, 0.6f, 0.6f));
            }
        }

        // Gui.cs:1764-1799 — verbatim, BOTH directions have real, different logic. Enable: reset
        // the patch/latch fields (incl. the original-gate snapshot pair) and notify. Disable:
        // AuraMono display-gate restore first (logged either way via StrangerChatLog), then reset
        // three fields (the snapshot pair deliberately NOT cleared here — IMGUI parity), notify.
        private void OnUguiSelfStrangerChatBypassToggled(bool value)
        {
            if (value == this.strangerChatBypassEnabled)
            {
                return;
            }
            this.strangerChatBypassEnabled = value;
            this.SaveKeybinds(false);
            if (this.strangerChatBypassEnabled)
            {
                this.strangerChatBypassPatchApplied = false;
                this.strangerChatBypassPatchUnavailableLogged = false;
                this.strangerChatOriginalInSelfRoom = false;
                this.strangerChatOriginalInSelfRoomValid = false;
                this.nextStrangerChatBypassPatchAttemptAt = -999f;
                this.AddMenuNotification(this.L("Stranger Chat Bypass Enabled"), new Color(0.55f, 0.88f, 1f));
            }
            else
            {
                if (this.TryRestoreAuraMonoStrangerChatDisplayGate(out string restoreStatus))
                {
                    this.StrangerChatLog("Stranger Chat Bypass restored. " + restoreStatus);
                }
                else if (!string.IsNullOrWhiteSpace(restoreStatus))
                {
                    this.StrangerChatLog("Stranger Chat Bypass restore failed. " + restoreStatus);
                }

                this.strangerChatBypassPatchApplied = false;
                this.strangerChatBypassPatchUnavailableLogged = false;
                this.nextStrangerChatBypassPatchAttemptAt = -999f;
                this.AddMenuNotification(this.L("Stranger Chat Bypass Disabled"), new Color(0.88f, 0.6f, 0.6f));
            }
        }

        // Gui.cs:1801-1822 — three retry/latch field resets + save + notification both directions.
        private void OnUguiSelfChatTranslateToggled(bool value)
        {
            if (value == this.chatForceTranslateEnabled)
            {
                return;
            }
            this.chatForceTranslateEnabled = value;
            this.chatForceTranslateUnavailableLogged = false;
            this.chatForceTranslateNextHookAttemptAt = -999f;
            this.chatForceTranslateNextResolveAt = -999f;
            this.SaveKeybinds(false);
            if (this.chatForceTranslateEnabled)
            {
                this.AddMenuNotification(this.L("Chat Translate Unlock Enabled"), new Color(0.55f, 0.88f, 1f));
            }
            else
            {
                this.AddMenuNotification(this.L("Chat Translate Unlock Disabled"), new Color(0.88f, 0.6f, 0.6f));
            }
        }

        private void OnUguiSelfChatTranslateDebugToggled(bool value)
        {
            if (value == this.chatTranslateVerboseLog)
            {
                return;
            }
            this.chatTranslateVerboseLog = value;
            this.chatTranslateGameStateLogged = false;
            this.SaveKeybinds(false);
        }

        private void OnUguiSelfChatTranslateForceAllToggled(bool value)
        {
            if (value == this.chatTranslateForceAllLangs)
            {
                return;
            }
            this.chatTranslateForceAllLangs = value;
            this.SaveKeybinds(false);
        }

        private void OnUguiSelfChatTranslatePostcardToggled(bool value)
        {
            if (value == this.chatTranslatePostcardBypass)
            {
                return;
            }
            this.chatTranslatePostcardBypass = value;
            this.postcardUnavailableLogged = false;
            this.postcardNextMailIdResolveAt = -999f;
            this.SaveKeybinds(false);
            this.AddMenuNotification(
                value ? this.L("Postcard Translate Bypass On") : this.L("Postcard Translate Bypass Off"),
                value ? new Color(0.55f, 0.88f, 1f) : new Color(0.88f, 0.6f, 0.6f));
        }

        // Gui.cs:1828-1834 — NOT a direct field write: SetGameSpeed clamps + applies the timescale,
        // and the config save is the QUEUED game-speed one, not SaveKeybinds.
        private void OnUguiSelfGameSpeedChanged(float value)
        {
            if (Mathf.Abs(value - this.gameSpeed) <= 0.0001f)
            {
                return;
            }
            this.SetGameSpeed(value);
            this.QueueGameSpeedConfigSave();
        }

        // Gui.cs:1842-1861 — apply on enable / restore on disable, then try/catch-wrapped save.
        // No notification in the source.
        private void OnUguiSelfCustomFovToggled(bool value)
        {
            if (value == this.customCameraFOVEnabled)
            {
                return;
            }
            this.customCameraFOVEnabled = value;
            if (this.customCameraFOVEnabled)
            {
                this.ApplyCameraFOV();
            }
            else
            {
                this.RestoreCameraFOV();
            }

            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:1866-1876 — the value is ALWAYS adjustable; ApplyCameraFOV only fires while the
        // Custom Camera FOV toggle is currently on (it takes visual effect on the next enable
        // otherwise). Exact-inequality guard mirrors the IMGUI `newFov != this.cameraFOV`.
        private void OnUguiSelfCameraFovChanged(float value)
        {
            if (value == this.cameraFOV)
            {
                return;
            }
            this.cameraFOV = value;
            if (this.customCameraFOVEnabled)
            {
                this.ApplyCameraFOV();
            }

            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:1884-1899 — bridge release on the turning-OFF edge only; save either direction.
        // No notification in the source.
        private void OnUguiSelfAnalogMoveToggled(bool value)
        {
            if (value == this.analogMoveBridgeEnabled)
            {
                return;
            }
            this.analogMoveBridgeEnabled = value;
            if (!this.analogMoveBridgeEnabled)
            {
                this.ReleaseMovementBridgeIfInjecting();
            }

            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:1909-1919 — save only, no notification.
        private void OnUguiSelfSkipShowOffToggled(bool value)
        {
            if (value == this.skipShowOffAnimations)
            {
                return;
            }
            this.skipShowOffAnimations = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Self → Fun (DrawSelfFunTab:1924-2028 — force locomotion + custom swim sprint). Fits the
        // cell without scrolling; no conditional layout (every control is always visible).
        // ----------------------------------------------------------------------------------------

        private string BuildUguiSelfFunLocomotionStatusText()
        {
            return "Swim/Skate on land (others see it). Status: " + this.forceLocomotionLastStatus;
        }

        private string BuildUguiSelfFunSprintDurationText()
        {
            // The "∞" display rule, verbatim from DrawSelfFunTab:1978-1982: the slider max IS the
            // infinite setting.
            bool swimSprintInfinite = this.swimSprintDurationSeconds >= SwimSprintDurationMax - 0.001f;
            return swimSprintInfinite
                ? this.L("Sprint Duration: ") + "∞"
                : this.LF("Sprint Duration: {0:F1}s", this.swimSprintDurationSeconds);
        }

        private string BuildUguiSelfFunSprintStatusText()
        {
            return "Underwater dash (Shift). Max duration = never ends (a sharp turn still cancels)."
                + (this.swimSprintTweakEnabled ? " Status: " + this.swimSprintTweakStatus : string.Empty);
        }

        private GameObject BuildUguiShellSelfFunContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellSelfFun = null;

            UguiShellSelfFunHandle handle = new UguiShellSelfFunHandle();
            GameObject block = this.CreateUguiGo("SelfFunContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            const float labelW = 200f;
            float rowW = w - pad * 2f;
            float sliderX = pad + labelW + 10f;
            float sliderW = w - sliderX - pad;
            Color muted = this.UguiKitMutedColor();
            Color hintColor = new Color(muted.r, muted.g, muted.b, 0.85f);
            float yCur = 12f;

            handle.ForceSkateToggle = this.CreateUguiCheckbox(block.transform, "ForceSkateToggle",
                this.L("Force Skate (skate on land)"), this.forceSkateEnabled,
                new System.Action<bool>(this.OnUguiSelfForceSkateToggled));
            PlaceUguiTopLeft(handle.ForceSkateToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 30f;

            handle.ForceSwimToggle = this.CreateUguiCheckbox(block.transform, "ForceSwimToggle",
                this.L("Force Swim (swim on land)"), this.forceSwimEnabled,
                new System.Action<bool>(this.OnUguiSelfForceSwimToggled));
            PlaceUguiTopLeft(handle.ForceSwimToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 30f;

            handle.LocomotionStatusShown = this.BuildUguiSelfFunLocomotionStatusText();
            handle.LocomotionStatusLabel = this.CreateUguiLabel(block.transform, "LocomotionStatus",
                handle.LocomotionStatusShown, 11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.LocomotionStatusLabel);
            PlaceUguiTopLeft(handle.LocomotionStatusLabel, pad, yCur, rowW, 32f);
            yCur += 40f;

            handle.SwimSprintToggle = this.CreateUguiCheckbox(block.transform, "SwimSprintToggle",
                this.L("Custom Swim Sprint"), this.swimSprintTweakEnabled,
                new System.Action<bool>(this.OnUguiSelfSwimSprintToggled));
            PlaceUguiTopLeft(handle.SwimSprintToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 30f;

            handle.SprintDurationShown = this.BuildUguiSelfFunSprintDurationText();
            handle.SprintDurationLabel = this.CreateUguiBodyLabel(block.transform, "SprintDurationLabel",
                handle.SprintDurationShown, 13f);
            PlaceUguiTopLeft(handle.SprintDurationLabel, pad, yCur + 2f, labelW, 20f);
            handle.SprintDurationSlider = this.CreateUguiSlider(block.transform, "SprintDurationSlider",
                SwimSprintDurationMin, SwimSprintDurationMax, this.swimSprintDurationSeconds, false,
                new System.Action<float>(this.OnUguiSelfSprintDurationChanged));
            PlaceUguiTopLeft(handle.SprintDurationSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 28f;

            handle.SprintCooldownShown = this.LF("Sprint Cooldown: {0:F1}s", this.swimSprintCooldownSeconds);
            handle.SprintCooldownLabel = this.CreateUguiBodyLabel(block.transform, "SprintCooldownLabel",
                handle.SprintCooldownShown, 13f);
            PlaceUguiTopLeft(handle.SprintCooldownLabel, pad, yCur + 2f, labelW, 20f);
            handle.SprintCooldownSlider = this.CreateUguiSlider(block.transform, "SprintCooldownSlider",
                SwimSprintCooldownMin, SwimSprintCooldownMax, this.swimSprintCooldownSeconds, false,
                new System.Action<float>(this.OnUguiSelfSprintCooldownChanged));
            PlaceUguiTopLeft(handle.SprintCooldownSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 28f;

            handle.SprintStatusShown = this.BuildUguiSelfFunSprintStatusText();
            handle.SprintStatusLabel = this.CreateUguiLabel(block.transform, "SprintStatus",
                handle.SprintStatusShown, 11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.SprintStatusLabel);
            PlaceUguiTopLeft(handle.SprintStatusLabel, pad, yCur, rowW, 32f);
            yCur += 40f;

            handle.VerticalGuardToggle = this.CreateUguiCheckbox(block.transform, "VerticalGuardToggle",
                this.L("Sprint Ignores Space/Ctrl"), this.swimSprintVerticalGuardEnabled,
                new System.Action<bool>(this.OnUguiSelfVerticalGuardToggled));
            PlaceUguiTopLeft(handle.VerticalGuardToggle.gameObject, pad, yCur, rowW, 24f);

            handle.Root = block;
            this.uguiShellSelfFun = handle;
            return block;
        }

        private void ProcessUguiShellSelfFunOnUpdate()
        {
            UguiShellSelfFunHandle handle = this.uguiShellSelfFun;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSelfSubTabActive(UguiShellSelfFunSubIndex))
            {
                return;
            }

            try
            {
                this.SyncUguiToggleFromField(handle.ForceSkateToggle, this.forceSkateEnabled);
                this.SyncUguiToggleFromField(handle.ForceSwimToggle, this.forceSwimEnabled);
                this.SyncUguiToggleFromField(handle.SwimSprintToggle, this.swimSprintTweakEnabled);
                this.SyncUguiToggleFromField(handle.VerticalGuardToggle, this.swimSprintVerticalGuardEnabled);

                if (handle.SprintDurationSlider != null
                    && Mathf.Abs(handle.SprintDurationSlider.value - this.swimSprintDurationSeconds) > 0.0005f)
                {
                    handle.SprintDurationSlider.SetValueWithoutNotify(this.swimSprintDurationSeconds);
                }
                this.SyncUguiSelfLabelText(handle.SprintDurationLabel, ref handle.SprintDurationShown,
                    this.BuildUguiSelfFunSprintDurationText());
                if (handle.SprintCooldownSlider != null
                    && Mathf.Abs(handle.SprintCooldownSlider.value - this.swimSprintCooldownSeconds) > 0.0005f)
                {
                    handle.SprintCooldownSlider.SetValueWithoutNotify(this.swimSprintCooldownSeconds);
                }
                this.SyncUguiSelfLabelText(handle.SprintCooldownLabel, ref handle.SprintCooldownShown,
                    this.LF("Sprint Cooldown: {0:F1}s", this.swimSprintCooldownSeconds));

                // The two live status lines change from the feature's background apply loop, not
                // from user edits — 0.5s tick (Settings→Main slow-sync idiom).
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    this.SyncUguiSelfLabelText(handle.LocomotionStatusLabel, ref handle.LocomotionStatusShown,
                        this.BuildUguiSelfFunLocomotionStatusText());
                    this.SyncUguiSelfLabelText(handle.SprintStatusLabel, ref handle.SprintStatusShown,
                        this.BuildUguiSelfFunSprintStatusText());
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Self Fun content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // --- Fun change handlers (DrawSelfFunTab verbatim: notification colors differ per
        // toggle; Force Skate/Swim have NO save; the sprint pair notifies THEN saves). ----------

        // Gui.cs:1937-1940 — notification only (single green shade both directions), no save.
        private void OnUguiSelfForceSkateToggled(bool value)
        {
            if (value == this.forceSkateEnabled)
            {
                return;
            }
            this.forceSkateEnabled = value;
            this.AddMenuNotification(this.forceSkateEnabled ? "Force Skate on" : "Force Skate off",
                new Color(0.55f, 1f, 0.65f));
        }

        // Gui.cs:1951-1954 — its own (cyan) color, no save.
        private void OnUguiSelfForceSwimToggled(bool value)
        {
            if (value == this.forceSwimEnabled)
            {
                return;
            }
            this.forceSwimEnabled = value;
            this.AddMenuNotification(this.forceSwimEnabled ? "Force Swim on" : "Force Swim off",
                new Color(0.45f, 0.85f, 1f));
        }

        // Gui.cs:1969-1975 — notify FIRST, then try/catch-wrapped save (source order).
        private void OnUguiSelfSwimSprintToggled(bool value)
        {
            if (value == this.swimSprintTweakEnabled)
            {
                return;
            }
            this.swimSprintTweakEnabled = value;
            this.AddMenuNotification(
                this.swimSprintTweakEnabled ? "Custom Swim Sprint on" : "Custom Swim Sprint off",
                new Color(0.45f, 0.85f, 1f));
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:1984-1990 — rounds to the nearest 0.5s (the max lands exactly on
        // SwimSprintDurationMax, which the label shows as "∞").
        private void OnUguiSelfSprintDurationChanged(float value)
        {
            float rounded = Mathf.Round(value * 2f) / 2f;
            if (Mathf.Abs(rounded - this.swimSprintDurationSeconds) <= 0.0001f)
            {
                return;
            }
            this.swimSprintDurationSeconds = rounded;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:1995-2001 — rounds to the nearest 0.1s.
        private void OnUguiSelfSprintCooldownChanged(float value)
        {
            float rounded = Mathf.Round(value * 10f) / 10f;
            if (Mathf.Abs(rounded - this.swimSprintCooldownSeconds) <= 0.0001f)
            {
                return;
            }
            this.swimSprintCooldownSeconds = rounded;
            try { this.SaveKeybinds(false); } catch { }
        }

        // Gui.cs:2019-2025 — notify then save.
        private void OnUguiSelfVerticalGuardToggled(bool value)
        {
            if (value == this.swimSprintVerticalGuardEnabled)
            {
                return;
            }
            this.swimSprintVerticalGuardEnabled = value;
            this.AddMenuNotification(
                this.swimSprintVerticalGuardEnabled ? "Sprint ignores Space/Ctrl: on" : "Sprint ignores Space/Ctrl: off",
                new Color(0.45f, 0.85f, 1f));
            try { this.SaveKeybinds(false); } catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Self → Privacy (DrawPrivacyBlockExtraTab, PrivacyBlockFeature.cs:460-540): four toggles,
        // each followed by a LIVE counter reading an internal static int the detour bodies
        // increment in the background (NOT instance fields — same static split as the Building
        // round's ignore flags), plus the hooks-status method line. Counters + status tick at
        // 0.5s; toggle saves have no notifications (IMGUI parity).
        // ----------------------------------------------------------------------------------------

        private GameObject BuildUguiShellSelfPrivacyContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellSelfPrivacy = null;

            UguiShellSelfPrivacyHandle handle = new UguiShellSelfPrivacyHandle();
            GameObject block = this.CreateUguiGo("SelfPrivacyContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            float rowW = w - pad * 2f;
            Color muted = this.UguiKitMutedColor();
            Color counterColor = new Color(muted.r, muted.g, muted.b, 0.9f); // IMGUI labelStyle 0.9 alpha
            float yCur = 12f;

            // IMGUI header: "Privacy", bold 13, header color, unlocalized.
            GameObject header = this.CreateUguiHeaderLabel(block.transform, "Header", "Privacy", 13f);
            PlaceUguiTopLeft(header, pad, yCur, rowW, 22f);
            yCur += 28f;

            handle.LogsToggle = this.CreateUguiCheckbox(block.transform, "LogsToggle",
                this.L("Block Server Log Uploads"), this.privacyBlockLogUploads,
                new System.Action<bool>(this.OnUguiSelfPrivacyLogsToggled));
            PlaceUguiTopLeft(handle.LogsToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 28f;
            handle.LogsCountShown = this.LF("Logs blocked: {0}", privacyBlockedLogCount);
            handle.LogsCountLabel = this.CreateUguiLabel(block.transform, "LogsCount",
                handle.LogsCountShown, 11f, counterColor, false);
            PlaceUguiTopLeft(handle.LogsCountLabel, pad, yCur, rowW, 18f);
            yCur += 24f;

            handle.MergesToggle = this.CreateUguiCheckbox(block.transform, "MergesToggle",
                this.L("Block Room Merge (Enter)"), this.privacyBlockRoomMerges,
                new System.Action<bool>(this.OnUguiSelfPrivacyMergesToggled));
            PlaceUguiTopLeft(handle.MergesToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 28f;
            handle.MergesCountShown = this.LF("Merges blocked: {0}", privacyBlockedMergeCount);
            handle.MergesCountLabel = this.CreateUguiLabel(block.transform, "MergesCount",
                handle.MergesCountShown, 11f, counterColor, false);
            PlaceUguiTopLeft(handle.MergesCountLabel, pad, yCur, rowW, 18f);
            yCur += 24f;

            handle.SpamsToggle = this.CreateUguiCheckbox(block.transform, "SpamsToggle",
                this.L("Block Spam Reports"), this.privacyBlockSpamReports,
                new System.Action<bool>(this.OnUguiSelfPrivacySpamsToggled));
            PlaceUguiTopLeft(handle.SpamsToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 28f;
            handle.SpamsCountShown = this.LF("Spams blocked: {0}", privacyBlockedSpamCount);
            handle.SpamsCountLabel = this.CreateUguiLabel(block.transform, "SpamsCount",
                handle.SpamsCountShown, 11f, counterColor, false);
            PlaceUguiTopLeft(handle.SpamsCountLabel, pad, yCur, rowW, 18f);
            yCur += 24f;

            handle.UploadCheatToggle = this.CreateUguiCheckbox(block.transform, "UploadCheatToggle",
                this.L("Block Cheat Upload"), this.privacyBlockUploadCheat,
                new System.Action<bool>(this.OnUguiSelfPrivacyUploadCheatToggled));
            PlaceUguiTopLeft(handle.UploadCheatToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 28f;
            handle.UploadCheatCountShown = this.LF("UploadCheat seen: {0} | blocked: {1}",
                privacyUploadCheatSeenCount, privacyBlockedUploadCheatCount);
            handle.UploadCheatCountLabel = this.CreateUguiLabel(block.transform, "UploadCheatCount",
                handle.UploadCheatCountShown, 11f, counterColor, false);
            PlaceUguiTopLeft(handle.UploadCheatCountLabel, pad, yCur, rowW, 18f);
            yCur += 22f;

            handle.HooksStatusShown = this.GetPrivacyBlockHooksStatus();
            handle.HooksStatusLabel = this.CreateUguiLabel(block.transform, "HooksStatus",
                handle.HooksStatusShown, 11f, counterColor, false);
            PlaceUguiTopLeft(handle.HooksStatusLabel, pad, yCur, rowW, 18f);

            handle.Root = block;
            this.uguiShellSelfPrivacy = handle;
            return block;
        }

        private void ProcessUguiShellSelfPrivacyOnUpdate()
        {
            UguiShellSelfPrivacyHandle handle = this.uguiShellSelfPrivacy;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSelfSubTabActive(UguiShellSelfPrivacySubIndex))
            {
                return;
            }

            try
            {
                this.SyncUguiToggleFromField(handle.LogsToggle, this.privacyBlockLogUploads);
                this.SyncUguiToggleFromField(handle.MergesToggle, this.privacyBlockRoomMerges);
                this.SyncUguiToggleFromField(handle.SpamsToggle, this.privacyBlockSpamReports);
                this.SyncUguiToggleFromField(handle.UploadCheatToggle, this.privacyBlockUploadCheat);

                // Counters increment from background detour bodies (Interlocked, any time) and the
                // hooks status flips as install attempts land — 0.5s tick keeps them live without
                // per-frame string.Format churn.
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    this.SyncUguiSelfLabelText(handle.LogsCountLabel, ref handle.LogsCountShown,
                        this.LF("Logs blocked: {0}", privacyBlockedLogCount));
                    this.SyncUguiSelfLabelText(handle.MergesCountLabel, ref handle.MergesCountShown,
                        this.LF("Merges blocked: {0}", privacyBlockedMergeCount));
                    this.SyncUguiSelfLabelText(handle.SpamsCountLabel, ref handle.SpamsCountShown,
                        this.LF("Spams blocked: {0}", privacyBlockedSpamCount));
                    this.SyncUguiSelfLabelText(handle.UploadCheatCountLabel, ref handle.UploadCheatCountShown,
                        this.LF("UploadCheat seen: {0} | blocked: {1}",
                            privacyUploadCheatSeenCount, privacyBlockedUploadCheatCount));
                    this.SyncUguiSelfLabelText(handle.HooksStatusLabel, ref handle.HooksStatusShown,
                        this.GetPrivacyBlockHooksStatus());
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Self Privacy content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // --- Privacy change handlers (PrivacyBlockFeature.cs:473-531: try/catch-wrapped save
        // only, NO notifications — all four identical in shape, kept as named handlers for the
        // same reason as Main: the fields are watched by native detour bodies). ------------------

        private void OnUguiSelfPrivacyLogsToggled(bool value)
        {
            if (value == this.privacyBlockLogUploads)
            {
                return;
            }
            this.privacyBlockLogUploads = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiSelfPrivacyMergesToggled(bool value)
        {
            if (value == this.privacyBlockRoomMerges)
            {
                return;
            }
            this.privacyBlockRoomMerges = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiSelfPrivacySpamsToggled(bool value)
        {
            if (value == this.privacyBlockSpamReports)
            {
                return;
            }
            this.privacyBlockSpamReports = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiSelfPrivacyUploadCheatToggled(bool value)
        {
            if (value == this.privacyBlockUploadCheat)
            {
                return;
            }
            this.privacyBlockUploadCheat = value;
            try { this.SaveKeybinds(false); } catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Self → Game UI (DrawSelfGameUiTab, GameUiTimingsFeature.cs:300-359): master toggle +
        // SEVEN genuinely uniform sliders (same range, same 0.1 rounding, same save) driven from
        // GameUiTimingSliderLabels in a data-driven loop (the Logging round's array precedent) +
        // reset-to-defaults button + live status line. The reset button's new values reach the
        // sliders through the same per-frame SetValueWithoutNotify re-sync as every other round —
        // no special-case refresh.
        // ----------------------------------------------------------------------------------------

        private string BuildUguiSelfGameUiTimingLabelText(int index)
        {
            return this.LF(GameUiTimingSliderLabels[index] + ": {0:F1}s", this.gameUiTimingSeconds[index]);
        }

        private string BuildUguiSelfGameUiStatusText()
        {
            return this.L("How long the game's toasts/tips stay on screen (item-obtained bubbles, text toasts, banners). Applies live; disable to restore game defaults.")
                + (this.gameUiTimingsEnabled ? " Status: " + this.gameUiTimingsStatus : string.Empty);
        }

        private GameObject BuildUguiShellSelfGameUiContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellSelfGameUi = null;

            UguiShellSelfGameUiHandle handle = new UguiShellSelfGameUiHandle();
            GameObject block = this.CreateUguiGo("SelfGameUiContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            const float labelW = 240f; // longest label: "Item Toast (obtained items): 2.5s"
            float rowW = w - pad * 2f;
            float sliderX = pad + labelW + 10f;
            float sliderW = w - sliderX - pad;
            Color muted = this.UguiKitMutedColor();
            float yCur = 12f;

            handle.EnabledToggle = this.CreateUguiCheckbox(block.transform, "EnabledToggle",
                this.L("Custom UI Timings"), this.gameUiTimingsEnabled,
                new System.Action<bool>(this.OnUguiSelfGameUiTimingsToggled));
            PlaceUguiTopLeft(handle.EnabledToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 32f;

            for (int i = 0; i < GameUiTimingSliderLabels.Length; i++)
            {
                int indexCopy = i; // capture a copy for the change closure
                string text = this.BuildUguiSelfGameUiTimingLabelText(i);
                GameObject label = this.CreateUguiBodyLabel(block.transform, "TimingLabel" + i, text, 13f);
                PlaceUguiTopLeft(label, pad, yCur + 2f, labelW, 20f);
                handle.TimingLabels.Add(label);
                handle.TimingShown.Add(text);

                Slider slider = this.CreateUguiSlider(block.transform, "TimingSlider" + i,
                    GameUiTimingMin, GameUiTimingMax, this.gameUiTimingSeconds[i], false,
                    new System.Action<float>(v => this.OnUguiSelfGameUiTimingChanged(indexCopy, v)));
                PlaceUguiTopLeft(slider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
                handle.TimingSliders.Add(slider);
                yCur += 28f;
            }
            yCur += 6f;

            GameObject resetBtn = this.CreateUguiSecondaryButton(block.transform, "ResetButton",
                this.L("Reset to game defaults"),
                new System.Action(this.OnUguiSelfGameUiResetClicked));
            PlaceUguiTopLeft(resetBtn, pad, yCur, 260f, 28f);
            yCur += 36f;

            handle.StatusShown = this.BuildUguiSelfGameUiStatusText();
            handle.StatusLabel = this.CreateUguiLabel(block.transform, "Status",
                handle.StatusShown, 11f, new Color(muted.r, muted.g, muted.b, 0.85f), false);
            this.TrySetUguiLabelWrapped(handle.StatusLabel);
            PlaceUguiTopLeft(handle.StatusLabel, pad, yCur, rowW, 60f);

            handle.Root = block;
            this.uguiShellSelfGameUi = handle;
            return block;
        }

        private void ProcessUguiShellSelfGameUiOnUpdate()
        {
            UguiShellSelfGameUiHandle handle = this.uguiShellSelfGameUi;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSelfSubTabActive(UguiShellSelfGameUiSubIndex))
            {
                return;
            }

            try
            {
                this.SyncUguiToggleFromField(handle.EnabledToggle, this.gameUiTimingsEnabled);

                // Per-frame value re-sync is exactly what makes "Reset to game defaults" (and any
                // IMGUI-twin edit) show up in these sliders on the next frame.
                for (int i = 0; i < handle.TimingSliders.Count && i < this.gameUiTimingSeconds.Length; i++)
                {
                    Slider slider = handle.TimingSliders[i];
                    if (slider != null && Mathf.Abs(slider.value - this.gameUiTimingSeconds[i]) > 0.0005f)
                    {
                        slider.SetValueWithoutNotify(this.gameUiTimingSeconds[i]);
                    }
                    string text = this.BuildUguiSelfGameUiTimingLabelText(i);
                    if (i < handle.TimingLabels.Count && !string.Equals(text, handle.TimingShown[i], StringComparison.Ordinal))
                    {
                        handle.TimingShown[i] = text;
                        this.SetUguiLabelText(handle.TimingLabels[i], text);
                    }
                }

                // The status line's suffix comes from the feature's 0.5s background apply loop.
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    this.SyncUguiSelfLabelText(handle.StatusLabel, ref handle.StatusShown,
                        this.BuildUguiSelfGameUiStatusText());
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Self Game UI content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // GameUiTimingsFeature.cs:313-319 — notify FIRST, then try/catch-wrapped save (source
        // order). The actual apply/restore is owned by ProcessGameUiTimingsOnUpdate's edge detect.
        private void OnUguiSelfGameUiTimingsToggled(bool value)
        {
            if (value == this.gameUiTimingsEnabled)
            {
                return;
            }
            this.gameUiTimingsEnabled = value;
            this.AddMenuNotification(
                this.gameUiTimingsEnabled ? "Custom UI timings on" : "Custom UI timings off (restoring defaults)",
                new Color(0.45f, 0.85f, 1f));
            try { this.SaveKeybinds(false); } catch { }
        }

        // GameUiTimingsFeature.cs:323-342 — 0.1s rounding per slider; the IMGUI drawer batches
        // SaveKeybinds to once per frame when ANY slider changed, this fires it per actual change
        // (functionally equivalent for this field — an accepted deviation per the round spec).
        private void OnUguiSelfGameUiTimingChanged(int index, float value)
        {
            if (index < 0 || index >= this.gameUiTimingSeconds.Length)
            {
                return;
            }
            float rounded = Mathf.Round(value * 10f) / 10f;
            if (Mathf.Abs(rounded - this.gameUiTimingSeconds[index]) <= 0.0001f)
            {
                return;
            }
            this.gameUiTimingSeconds[index] = rounded;
            try { this.SaveKeybinds(false); } catch { }
        }

        // GameUiTimingsFeature.cs:344-349 — defaults copy + save + notification; the sliders pick
        // the new values up via the per-frame WithoutNotify re-sync above.
        private void OnUguiSelfGameUiResetClicked()
        {
            Array.Copy(GameUiTimingGameDefaults, this.gameUiTimingSeconds, this.gameUiTimingSeconds.Length);
            try { this.SaveKeybinds(false); } catch { }
            this.AddMenuNotification("UI timings reset to game defaults", new Color(0.45f, 0.85f, 1f));
        }
    }
}
