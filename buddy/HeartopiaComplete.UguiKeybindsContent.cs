using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Phase 3 tab CONTENT, item 9 (migration plan: cosmic-waddling-rainbow.md):
    // Settings→Keybinds. UGUI mirror of the DrawSettingsTab fallthrough branch
    // (HeartopiaComplete.Config.cs:930, settingsSubTab == 1): "KEYBIND SETTINGS" header, four
    // section panels (CORE 6 / AUTOMATION 21 / PLAYER 6 / SPEED & TOOLS 16 = 49 click-to-rebind
    // rows), a DANGER reset button, and the capture-mode view that REPLACES the section list
    // while this.keyBindingActive is non-empty.
    //
    // Ground rules (same as every prior round):
    //  - The IMGUI drawer and its helpers (BeginKeybindSection / DrawKeybindRowInPanel /
    //    TryCaptureKeybindFromEvent / ApplyActiveKeybind / FormatKeybindLabel) stay untouched and
    //    fully functional — this file only READS the same fields and CALLS the same methods. The
    //    ONE sanctioned backend change this round is the ResetKeybindSettingsToDefaults()
    //    extraction in HeartopiaComplete.Config.cs (both surfaces call it; the IMGUI button's
    //    behavior is bit-identical).
    //  - Wiring is by STATIC display-position index (UguiShellSettingsTabIndex +
    //    UguiShellSettingsKeybindsSubIndex, declared with their siblings in
    //    UguiShellTabIndices.cs), never by localized label comparison.
    //  - Lives inside the already-registered modal shell: no input-ownership entries, no theme
    //    registration of its own (the shell's "UguiShell" rebuilder re-runs this builder).
    //
    // The 49 rows are ONE data-driven array grouped by section (the Logging-round 39-binding
    // precedent, KeyCode getters instead of bools). Each binding carries the EXACT label string
    // its IMGUI row passes to DrawKeybindRowInPanel — that string is ALSO the ApplyActiveKeybind
    // switch key, so arming capture with it routes the eventual write through the same backend
    // switch both surfaces share. Deliberately NO per-row Action<KeyCode> setter: every write
    // must flow through ApplyActiveKeybind (save + notification + keyBindAssignedAt stamp) or
    // ResetKeybindSettingsToDefaults — a second per-row write path that skips those side effects
    // would be a footgun, so it does not exist.
    //
    // Capture mode needs a NEW input mechanism here: the IMGUI twin's TryCaptureKeybindFromEvent
    // reads Event.current, which only exists inside an OnGUI callback — meaningless in this
    // Update-driven processor. Side mouse buttons (3-6) are ALREADY covered for both surfaces by
    // the polling TryCaptureSideMouseKeybindOnUpdate (runs every frame while capture is armed,
    // HeartopiaComplete.cs:755, BEFORE this file's processor at :711? — order is irrelevant:
    // whichever applies first empties keyBindingActive and the other no-ops). What was missing is
    // the keyboard + primary-mouse (0-2) poller, TryCaptureUguiKeybindFromPolling below, gated on
    // THIS sub-tab being the visible one — when the IMGUI capture panel is what's on screen, its
    // own Event-based capture already handles it, and two active surfaces still can't double-
    // apply (ApplyActiveKeybind clears keyBindingActive; the loser early-outs on the empty check).
    //
    // Cross-surface sync: keyBindingActive is also armed/cleared by the still-live IMGUI twin, so
    // the gated per-frame processor re-derives the view (sections vs capture panel) from the live
    // field every frame — never only from this surface's own clicks. Bind-button captions re-sync
    // from the live KeyCode fields the same way (covers IMGUI-side rebinds and the reset), with a
    // per-row shown-text cache so unchanged rows skip the TMP text setter.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Data-driven bindings — labels/pairs/ORDER copied exactly from the IMGUI drawer
        // (HeartopiaComplete.Config.cs:964-1022). 6 + 21 + 6 + 16 = 49 rows.
        // ----------------------------------------------------------------------------------------

        private struct UguiKeybindRowBinding
        {
            public string Label;        // EXACT DrawKeybindRowInPanel/ApplyActiveKeybind label
            public Func<KeyCode> Get;

            public UguiKeybindRowBinding(string label, Func<KeyCode> get)
            {
                this.Label = label;
                this.Get = get;
            }
        }

        private struct UguiKeybindSection
        {
            public string Title;        // EXACT BeginKeybindSection title (localized at build)
            public UguiKeybindRowBinding[] Rows;

            public UguiKeybindSection(string title, UguiKeybindRowBinding[] rows)
            {
                this.Title = title;
                this.Rows = rows;
            }
        }

        // Instance method (unlike the Logging round's static builder): the key* fields are
        // instance fields, so the getter lambdas capture `this`.
        private UguiKeybindSection[] BuildUguiKeybindSections()
        {
            return new UguiKeybindSection[]
            {
                new UguiKeybindSection("CORE", new UguiKeybindRowBinding[]
                {
                    new UguiKeybindRowBinding("Toggle Menu", () => this.keyToggleMenu),
                    new UguiKeybindRowBinding("Toggle Radar", () => this.keyToggleRadar),
                    new UguiKeybindRowBinding("Bypass UI", () => this.keyBypassUI),
                    new UguiKeybindRowBinding("Disable All", () => this.keyDisableAll),
                    new UguiKeybindRowBinding("Inspect Player", () => this.keyInspectPlayer),
                    new UguiKeybindRowBinding("Inspect Move", () => this.keyInspectMove)
                }),
                new UguiKeybindSection("AUTOMATION", new UguiKeybindRowBinding[]
                {
                    new UguiKeybindRowBinding("Aura Farm", () => this.keyAuraFarm),
                    new UguiKeybindRowBinding("Water + Weed Radius", () => this.keyWaterWeedRadius),
                    new UguiKeybindRowBinding("Auto Insect Farm", () => this.keyAutoInsectFarm),
                    new UguiKeybindRowBinding("Auto Bird Farm", () => this.keyAutoBirdFarm),
                    new UguiKeybindRowBinding("Fish Shadow Net", () => this.keyAutoFishShadowNet),
                    new UguiKeybindRowBinding("Mass Cook", () => this.keyMassCook),
                    new UguiKeybindRowBinding("Auto Puzzle", () => this.keyAutoPuzzle),
                    new UguiKeybindRowBinding("Auto Cat Play", () => this.keyAutoCatPlay),
                    new UguiKeybindRowBinding("Auto Dog Train", () => this.keyAutoDogTrain),
                    new UguiKeybindRowBinding("Auto Pet Wash", () => this.keyAutoPetWash),
                    new UguiKeybindRowBinding("Feed All Cats", () => this.keyFeedAllCats),
                    new UguiKeybindRowBinding("Feed All Dogs", () => this.keyFeedAllDogs),
                    new UguiKeybindRowBinding("Auto Snow Sculpture", () => this.autoSnowHotkey),
                    new UguiKeybindRowBinding("Auto Sand Sculpture", () => this.autoSandHotkey),
                    new UguiKeybindRowBinding("Auto Sea Clean QTE", () => this.seaCleanQteHotkey),
                    new UguiKeybindRowBinding("Bird Vacuum", () => this.keyBirdVacuum),
                    new UguiKeybindRowBinding("Spawn Bubble", () => this.keySpawnBubble),
                    new UguiKeybindRowBinding("Auto Repair", () => this.keyAutoRepair),
                    new UguiKeybindRowBinding("Auto Eat", () => this.keyAutoEat),
                    new UguiKeybindRowBinding("Use Bait", () => this.keyUseBait),
                    new UguiKeybindRowBinding("Use Attractor", () => this.keyUseAttractor)
                }),
                new UguiKeybindSection("PLAYER", new UguiKeybindRowBinding[]
                {
                    new UguiKeybindRowBinding("Noclip", () => this.keyNoclip),
                    new UguiKeybindRowBinding("Camera Toggle", () => this.keyCameraToggle),
                    new UguiKeybindRowBinding("Auto Ice Skating", () => this.keyAutoIceSkating),
                    new UguiKeybindRowBinding("Join My Town", () => this.keyJoinMyTown),
                    new UguiKeybindRowBinding("Anti AFK", () => this.keyAntiAfk),
                    new UguiKeybindRowBinding("Bypass Overlap", () => this.keyBypassOverlap)
                }),
                new UguiKeybindSection("SPEED & TOOLS", new UguiKeybindRowBinding[]
                {
                    new UguiKeybindRowBinding("Game Speed 1x", () => this.keyGameSpeed1x),
                    new UguiKeybindRowBinding("Game Speed 2x", () => this.keyGameSpeed2x),
                    new UguiKeybindRowBinding("Game Speed 5x", () => this.keyGameSpeed5x),
                    new UguiKeybindRowBinding("Game Speed 10x", () => this.keyGameSpeed10x),
                    new UguiKeybindRowBinding("Equip Axe", () => this.keyEquipAxe),
                    new UguiKeybindRowBinding("Equip Net", () => this.keyEquipNet),
                    new UguiKeybindRowBinding("Equip Rod", () => this.keyEquipRod),
                    new UguiKeybindRowBinding("Equip Sprinkler", () => this.keyEquipSprinkler),
                    new UguiKeybindRowBinding("Equip Bird Scanner", () => this.keyEquipBirdScanner),
                    new UguiKeybindRowBinding("Equip Pad", () => this.keyEquipPad),
                    new UguiKeybindRowBinding("Equip Sea Cleaner", () => this.keyEquipSeaCleaner),
                    new UguiKeybindRowBinding("Pad Confirm", () => this.keyPadConfirm),
                    new UguiKeybindRowBinding("Pad Cancel", () => this.keyPadCancel),
                    new UguiKeybindRowBinding("Pad Rotate", () => this.keyPadRotate),
                    new UguiKeybindRowBinding("Pad Move", () => this.keyPadMove),
                    new UguiKeybindRowBinding("Pad Delete", () => this.keyPadDelete)
                })
            };
        }

        private sealed class UguiShellKeybindsHandle
        {
            public GameObject Root;
            public GameObject NormalRoot;          // scroll view: header + 4 sections + reset
            public GameObject CaptureRoot;         // capture view (built inactive)
            public GameObject CaptureBindingLabel; // the amber UPPERCASE binding line
            public string CaptureShownBinding;     // last keyBindingActive rendered into it
            public RectTransform CaptureCancelRect; // pointer-over test target (see poller)
            public bool CaptureShown;              // which view is currently active
            public int CaptureArmedAtFrame = -1;   // frame a row click armed capture (see poller)
            public readonly List<UguiKeybindRowBinding> RowBindings = new List<UguiKeybindRowBinding>();
            public readonly List<GameObject> RowBindLabels = new List<GameObject>(); // bind-button "Label" children
            public readonly List<string> RowShownText = new List<string>();          // skip-unchanged cache
            public int ErrorCount;                 // per-frame sync disabled at 3 (LIVE rail idiom)
        }

        private UguiShellKeybindsHandle uguiShellKeybinds;

        // ----------------------------------------------------------------------------------------
        // Construction — everything is static layout (all 49 rows always exist; the only dynamic
        // pieces are label texts and the normal/capture view swap), so unlike Settings→Main there
        // is no relayout pass. Handle assigned LAST (Research idiom).
        // ----------------------------------------------------------------------------------------

        private GameObject BuildUguiShellKeybindsContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellKeybinds = null;

            UguiShellKeybindsHandle handle = new UguiShellKeybindsHandle();
            GameObject block = this.CreateUguiGo("SettingsKeybindsContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            float contentW = w - 22f;          // scroll viewport insets: 4 left + 18 right
            const float pad = 12f;
            float panelW = contentW - pad * 2f;

            // ---------------- Normal view: scroll with header + sections + reset ----------------
            Transform scrollContent;
            GameObject scroll = this.CreateUguiScrollView(block.transform, "Sections", 10f, out scrollContent);
            PlaceUguiTopLeft(scroll, 0f, 0f, w, h);
            // Flat look over the block's ContentBg (LIVE rail idiom) — alpha-0 images still
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
            handle.NormalRoot = scroll;

            float yCur = 10f;
            // IMGUI headerStyle is bold 15 white — header-label role carries it (SETTINGS precedent).
            GameObject header = this.CreateUguiHeaderLabel(scrollContent, "Header", this.L("KEYBIND SETTINGS"), 15f);
            PlaceUguiTopLeft(header, pad + 4f, yCur, panelW - 8f, 22f);
            yCur += 30f;

            UguiKeybindSection[] sections = this.BuildUguiKeybindSections();
            int totalRows = 0;
            for (int s = 0; s < sections.Length; s++)
            {
                totalRows += (sections[s].Rows != null) ? sections[s].Rows.Length : 0;
            }
            if (totalRows != 49)
            {
                // The IMGUI drawer has no shared row-count constant (BeginKeybindSection takes
                // literals 6/21/6/16), so this only guards THIS array against local edits.
                ModLogger.Msg("[UguiShell] Keybinds bindings (" + totalRows + ") != expected 49 — check BuildUguiKeybindSections");
            }

            Color rowText = this.UguiKitTextColor();
            for (int s = 0; s < sections.Length; s++)
            {
                UguiKeybindRowBinding[] rows = sections[s].Rows ?? new UguiKeybindRowBinding[0];
                // IMGUI BeginKeybindSection: panel height = 36 + rowCount*32, bold-11 header-color
                // title — CreateUguiSettingsMainPanel bakes the same title treatment.
                float panelH = 36f + rows.Length * 32f;
                GameObject panel = this.CreateUguiSettingsMainPanel(scrollContent, "Section" + s, this.L(sections[s].Title));
                PlaceUguiTopLeft(panel, pad, yCur, panelW, panelH);

                float rowW = panelW - 28f; // IMGUI: rowX = panelX + 14, rowWidth = panelWidth - 28
                for (int r = 0; r < rows.Length; r++)
                {
                    UguiKeybindRowBinding binding = rows[r];

                    // DrawKeybindRowInPanel mirror: tinted row bg, 12pt uiText label, bind button
                    // (124x22, right edge) showing FormatKeybindLabel — the kit Secondary tier
                    // stands in for IMGUI's themeTopTabStyle chip look (same ControlFill face).
                    GameObject row = this.CreateUguiGo("Row" + s + "_" + r, panel.transform);
                    PlaceUguiTopLeft(row, 14f, 36f + r * 32f, rowW, 28f);
                    this.AddUguiImage(row, new Color(1f, 1f, 1f, 0.05f), true, 1f);

                    GameObject label = this.CreateUguiLabel(row.transform, "Label",
                        this.L(binding.Label), 12f, rowText, false);
                    PlaceUguiTopLeft(label, 10f, 1f, rowW - 156f, 26f);

                    string bindText = FormatKeybindLabel(binding.Get());
                    string labelCopy = binding.Label; // capture a copy for the click closure
                    GameObject bindBtn = this.CreateUguiSecondaryButton(row.transform, "Bind",
                        bindText, new System.Action(() => this.OnUguiKeybindRowClicked(labelCopy)));
                    PlaceUguiTopLeft(bindBtn, rowW - 134f, 3f, 124f, 22f);
                    this.TrySetUguiButtonLabelSize(bindBtn, 11.5f); // 13pt kit default overflows 22px

                    Transform btnLabel = bindBtn.transform.Find("Label");
                    handle.RowBindings.Add(binding);
                    handle.RowBindLabels.Add(btnLabel != null ? btnLabel.gameObject : null);
                    handle.RowShownText.Add(bindText);
                }

                // IMGUI: num += 14 between sections; the last section is followed by 18 before
                // the reset button — the extra 4 is added after the loop.
                yCur += panelH + 14f;
            }
            yCur += 4f;

            // DrawDangerActionButton localizes internally, so the UGUI call site localizes too.
            GameObject resetBtn = this.CreateUguiDangerButton(scrollContent, "ResetDefaults",
                this.L("RESET TO DEFAULTS"), new System.Action(this.OnUguiKeybindsResetClicked));
            PlaceUguiTopLeft(resetBtn, pad, yCur, panelW, 34f);
            yCur += 34f + 20f;

            this.SetUguiScrollContentHeight(scrollContent, yCur);

            // ---------------- Capture view (replaces the sections while armed) ----------------
            // IMGUI parity: the "KEYBIND SETTINGS" header is drawn in BOTH states (Config.cs:942
            // runs before the capture branch), so the capture view carries its own copy; the
            // 116px panel replaces everything below it. Built inactive; the processor swaps.
            GameObject capture = this.CreateUguiGo("CaptureView", block.transform);
            PlaceUguiTopLeft(capture, 0f, 0f, w, h);

            GameObject capHeader = this.CreateUguiHeaderLabel(capture.transform, "Header", this.L("KEYBIND SETTINGS"), 15f);
            PlaceUguiTopLeft(capHeader, pad + 4f, 10f, panelW - 8f, 22f);

            GameObject capPanel = this.CreateUguiGo("CapturePanel", capture.transform);
            PlaceUguiTopLeft(capPanel, pad, 40f, panelW, 116f);
            // DrawExentriSectionPanel colors verbatim (accent line @ 0.24 over content fill).
            Color accent = this.UguiKitAccent();
            this.AddUguiImage(capPanel, new Color(this.uiContentR, this.uiContentG, this.uiContentB,
                Mathf.Clamp(this.uiPanelAlpha * 0.82f, 0.14f, 0.92f)), true, 1f);
            this.AddUguiRingOverlay(capPanel, new Color(accent.r, accent.g, accent.b, 0.24f), 1f);

            GameObject pressLabel = this.CreateUguiLabel(capPanel.transform, "Press",
                this.L("PRESS ANY KEY FOR:"), 13f, this.UguiKitTextColor(), true);
            this.TrySetUguiLabelBold(pressLabel);
            PlaceUguiTopLeft(pressLabel, 12f, 18f, panelW - 24f, 20f);

            // IMGUI hard-codes this amber (1, 0.86, 0.36) — NOT the theme accent; kept verbatim.
            GameObject bindingLabel = this.CreateUguiLabel(capPanel.transform, "Binding",
                "", 13f, new Color(1f, 0.86f, 0.36f), true);
            this.TrySetUguiLabelBold(bindingLabel);
            PlaceUguiTopLeft(bindingLabel, 12f, 42f, panelW - 24f, 24f);
            handle.CaptureBindingLabel = bindingLabel;

            // IMGUI: 240x30 danger button, centered ((540-240)/2 = 150 = its x offset).
            GameObject cancelBtn = this.CreateUguiDangerButton(capPanel.transform, "Cancel",
                this.L("CANCEL"), new System.Action(this.OnUguiKeybindCancelClicked));
            PlaceUguiTopLeft(cancelBtn, (panelW - 240f) * 0.5f, 76f, 240f, 30f);
            handle.CaptureCancelRect = cancelBtn.GetComponent<RectTransform>();

            capture.SetActive(false);
            handle.CaptureRoot = capture;
            handle.CaptureShown = false;

            handle.Root = block;
            this.uguiShellKeybinds = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Click handlers — each mirrors its IMGUI counterpart exactly.
        // ----------------------------------------------------------------------------------------

        // DrawKeybindRowInPanel click verbatim: arm capture, nothing else. The armed frame is
        // recorded so the poller can't read THIS click's own GetMouseButtonDown(0) as a Mouse0
        // bind on a sub-frame click — IMGUI's row button consumed that event before the capture
        // branch ever drew, so it structurally could not self-capture.
        private void OnUguiKeybindRowClicked(string bindingLabel)
        {
            this.keyBindingActive = bindingLabel;
            UguiShellKeybindsHandle handle = this.uguiShellKeybinds;
            if (handle != null)
            {
                handle.CaptureArmedAtFrame = Time.frameCount;
            }
        }

        // TRUE cancel — leaves the KeyCode field completely untouched (IMGUI CANCEL verbatim).
        // Contrast with Escape in the poller, which actively REBINDS to None.
        private void OnUguiKeybindCancelClicked()
        {
            this.keyBindingActive = "";
        }

        private void OnUguiKeybindsResetClicked()
        {
            // The extracted shared implementation — identical to the IMGUI button's behavior,
            // including the Settings→Main field resets (that tab's own gated processor re-syncs
            // its controls from the live fields automatically).
            this.ResetKeybindSettingsToDefaults();
        }

        // ----------------------------------------------------------------------------------------
        // Polling key capture — the Update-loop equivalent of TryCaptureKeybindFromEvent.
        // ----------------------------------------------------------------------------------------

        // Curated poll list. Deliberately NOT Enum.GetValues(typeof(KeyCode)): that includes
        // ~160 JoystickNButtonM values plus other never-bindable noise — hundreds of GetKeyDown
        // calls per captured frame for keys no keyboard can produce. This list covers everything
        // a keyboard-focused rebind flow can want. Escape is EXCLUDED on purpose (handled
        // explicitly as clear-to-None); OS-owned keys (Windows/Command, PrintScreen) are excluded
        // because the OS swallows or acts on them before the game sees a clean press.
        private static readonly KeyCode[] UguiKeybindPollCandidates = new KeyCode[]
        {
            // Letters
            KeyCode.A, KeyCode.B, KeyCode.C, KeyCode.D, KeyCode.E, KeyCode.F, KeyCode.G,
            KeyCode.H, KeyCode.I, KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.M, KeyCode.N,
            KeyCode.O, KeyCode.P, KeyCode.Q, KeyCode.R, KeyCode.S, KeyCode.T, KeyCode.U,
            KeyCode.V, KeyCode.W, KeyCode.X, KeyCode.Y, KeyCode.Z,
            // Top-row digits
            KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
            KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
            // Function keys
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6, KeyCode.F7,
            KeyCode.F8, KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F12, KeyCode.F13,
            KeyCode.F14, KeyCode.F15,
            // Arrows + nav cluster
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.Insert, KeyCode.Delete, KeyCode.Home, KeyCode.End, KeyCode.PageUp, KeyCode.PageDown,
            // Whitespace / editing
            KeyCode.Space, KeyCode.Return, KeyCode.Tab, KeyCode.Backspace,
            // Modifiers + locks
            KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.CapsLock, KeyCode.Numlock, KeyCode.ScrollLock,
            // Keypad
            KeyCode.Keypad0, KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4,
            KeyCode.Keypad5, KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9,
            KeyCode.KeypadPeriod, KeyCode.KeypadDivide, KeyCode.KeypadMultiply, KeyCode.KeypadMinus,
            KeyCode.KeypadPlus, KeyCode.KeypadEnter, KeyCode.KeypadEquals,
            // Punctuation
            KeyCode.BackQuote, KeyCode.Minus, KeyCode.Equals, KeyCode.LeftBracket, KeyCode.RightBracket,
            KeyCode.Backslash, KeyCode.Semicolon, KeyCode.Quote, KeyCode.Comma, KeyCode.Period,
            KeyCode.Slash,
            // Misc
            KeyCode.Pause, KeyCode.Menu
        };

        // Produces the identical ApplyActiveKeybind call TryCaptureKeybindFromEvent would for
        // every input source it handles, from Input polling instead of Event.current:
        //  - mouse 0-2 -> Mouse0+button (side buttons 3-6 belong to the ALREADY-polling
        //    TryCaptureSideMouseKeybindOnUpdate — never duplicated here);
        //  - Escape -> ApplyActiveKeybind(label, KeyCode.None): CLEARS TO NONE, it does NOT
        //    cancel (the event twin maps Escape's keyCode to None and still applies — an
        //    easy-to-miss source quirk; the CANCEL button is the only true no-op path);
        //  - any candidate key -> ApplyActiveKeybind(label, key). The event twin's
        //    "keyCode == None -> ignore" branch has no poll analog: GetKeyDown only ever
        //    answers for the concrete KeyCode asked.
        // Two guards replace the event consumption (e.Use) the poll model doesn't have, BOTH
        // scoped to Mouse0 only — the event twin consumes only real clicks (GUI.Button reacts
        // solely to button 0), so Mouse1/Mouse2 bind everywhere even over CANCEL, same as IMGUI:
        //  - armed-frame guard: a sub-frame click on a bind row is the arming click itself;
        //  - pointer-over-CANCEL guard: Button.onClick fires on pointer UP, so binding Mouse0 on
        //    the DOWN frame would exit capture before CANCEL could ever fire (IMGUI's CANCEL
        //    consumes its own MouseDown, making it clickable — mirrored as a rect test; overlay
        //    canvas, so the camera argument is null, the codebase-proven pattern).
        private bool TryCaptureUguiKeybindFromPolling()
        {
            if (string.IsNullOrEmpty(this.keyBindingActive))
            {
                return false;
            }

            UguiShellKeybindsHandle handle = this.uguiShellKeybinds;
            bool mouse0Eligible = handle == null || Time.frameCount != handle.CaptureArmedAtFrame;
            if (mouse0Eligible && handle != null && handle.CaptureCancelRect != null)
            {
                try
                {
                    Vector3 m = Input.mousePosition;
                    if (RectTransformUtility.RectangleContainsScreenPoint(
                        handle.CaptureCancelRect, new Vector2(m.x, m.y), null))
                    {
                        mouse0Eligible = false;
                    }
                }
                catch { }
            }

            if (mouse0Eligible && Input.GetMouseButtonDown(0))
            {
                this.ApplyActiveKeybind(this.keyBindingActive, KeyCode.Mouse0);
                return true;
            }
            if (Input.GetMouseButtonDown(1))
            {
                this.ApplyActiveKeybind(this.keyBindingActive, KeyCode.Mouse1);
                return true;
            }
            if (Input.GetMouseButtonDown(2))
            {
                this.ApplyActiveKeybind(this.keyBindingActive, KeyCode.Mouse2);
                return true;
            }

            if (!Input.anyKeyDown)
            {
                return false; // nothing pressed — skip the candidate sweep entirely
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                this.ApplyActiveKeybind(this.keyBindingActive, KeyCode.None);
                return true;
            }

            KeyCode[] candidates = UguiKeybindPollCandidates;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (Input.GetKeyDown(candidates[i]))
                {
                    this.ApplyActiveKeybind(this.keyBindingActive, candidates[i]);
                    return true;
                }
            }
            return false;
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame processor — called from ProcessUguiShellOnUpdate; skips in a few comparisons
        // unless the Keybinds sub-tab is the visible one. Order matters: capture label + poll
        // first (a capture this frame must swap the view back this same frame), then the view
        // swap from the LIVE keyBindingActive (covers captures armed/cleared by the IMGUI twin),
        // then the 49 bind-caption re-syncs (cheap delegate + string compare; SetText only on
        // change).
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellSettingsKeybindsOnUpdate()
        {
            UguiShellKeybindsHandle handle = this.uguiShellKeybinds;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSettingsSubTabActive(UguiShellSettingsKeybindsSubIndex))
            {
                return;
            }

            try
            {
                bool capturing = !string.IsNullOrEmpty(this.keyBindingActive);
                if (capturing)
                {
                    // Keep the amber line current even when the IMGUI twin armed the capture (or
                    // re-armed it with a different row) — IMGUI parity: L(label).ToUpperInvariant.
                    if (!string.Equals(this.keyBindingActive, handle.CaptureShownBinding, StringComparison.Ordinal))
                    {
                        handle.CaptureShownBinding = this.keyBindingActive;
                        this.SetUguiLabelText(handle.CaptureBindingLabel,
                            this.L(this.keyBindingActive).ToUpperInvariant());
                    }

                    this.TryCaptureUguiKeybindFromPolling();
                    capturing = !string.IsNullOrEmpty(this.keyBindingActive); // may have just applied
                }

                if (capturing != handle.CaptureShown)
                {
                    handle.CaptureShown = capturing;
                    SetUguiGoActive(handle.NormalRoot, !capturing);
                    SetUguiGoActive(handle.CaptureRoot, capturing);
                }

                if (!capturing)
                {
                    for (int i = 0; i < handle.RowBindings.Count && i < handle.RowBindLabels.Count; i++)
                    {
                        string text = FormatKeybindLabel(handle.RowBindings[i].Get());
                        if (!string.Equals(text, handle.RowShownText[i], StringComparison.Ordinal))
                        {
                            handle.RowShownText[i] = text;
                            this.SetUguiLabelText(handle.RowBindLabels[i], text);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Keybinds content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }
    }
}
