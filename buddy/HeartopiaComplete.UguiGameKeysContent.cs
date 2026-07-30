using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Settings→Game Keys. Rebinds the GAME's own keys, not the mod's.
    //
    // Sibling of Settings→Keybinds and deliberately a SEPARATE page rather than more sections in
    // it: the two have nothing in common under the hood. Mod hotkeys are `KeyCode` fields written
    // through ApplyActiveKeybind and saved in the mod config; game keys are string control paths
    // written through `InputActionMap.ApplyBindingOverride` into the live asset (GameKeyBindings.cs).
    // Mixing them in one list would put two different storage models, two different reset meanings
    // and two different "unbound" states behind identical-looking rows.
    //
    // Layout order is chosen so the page is usable without a filter: the 40 real gameplay verbs
    // come first, and AllKeyboardKeysMap — 110 rows of one self-named action per physical key — is
    // built LAST and COLLAPSED. Collapsing is cheap precisely because it is last: toggling it
    // changes only the total scroll height, so nothing above has to reflow.
    //
    // Capture differs from the mod page in one deliberate way: Escape CANCELS here. On the mod page
    // Escape means "bind to None", which has no analogue for a game binding — an empty override is
    // just "use the asset default", and that is what each row's reset button already does.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private sealed class UguiShellGameKeysHandle
        {
            public GameObject Root;
            public GameObject NormalRoot;
            public GameObject CaptureRoot;
            public GameObject CaptureBindingLabel;
            public string CaptureShownBinding;
            public RectTransform CaptureCancelRect;
            public bool CaptureShown;
            public int CaptureArmedAtFrame = -1;

            public readonly List<GameKeyRow> Rows = new List<GameKeyRow>();
            public readonly List<GameObject> RowBindLabels = new List<GameObject>();
            public readonly List<GameObject> RowResetButtons = new List<GameObject>();
            public readonly List<string> RowShownText = new List<string>();
            public readonly List<bool> RowShownOverridden = new List<bool>();

            public GameObject DirectKeysPanel;      // AllKeyboardKeysMap — collapsed by default
            public GameObject DirectKeysToggleLabel;
            public Transform ScrollContent;
            public float HeightCollapsed;
            public float HeightExpanded;
            public bool DirectKeysExpanded;

            public int ErrorCount;
        }

        private UguiShellGameKeysHandle uguiShellGameKeys;

        // Index into the handle's Rows list, -1 when not capturing. An INDEX rather than a row
        // reference so a rebuild of the page can't leave capture pointing at an orphaned row.
        private int gameKeyCaptureIndex = -1;

        private GameObject BuildUguiShellGameKeysContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellGameKeys = null;

            UguiShellGameKeysHandle handle = new UguiShellGameKeysHandle();
            GameObject block = this.CreateUguiGo("SettingsGameKeysContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            float contentW = w - 22f;
            const float pad = 12f;
            float panelW = contentW - pad * 2f;

            Transform scrollContent;
            GameObject scroll = this.CreateUguiScrollView(block.transform, "Sections", 10f, out scrollContent);
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
            handle.NormalRoot = scroll;
            handle.ScrollContent = scrollContent;

            float yCur = 10f;
            GameObject header = this.CreateUguiHeaderLabel(scrollContent, "Header", this.L("GAME KEY BINDINGS"), 15f);
            PlaceUguiTopLeft(header, pad + 4f, yCur, panelW - 8f, 22f);
            yCur += 26f;

            GameObject note = this.CreateUguiLabel(scrollContent, "Note",
                this.L("These are the GAME's own keys, not the mod's. Changes apply immediately and are not saved by the game."),
                11f, this.UguiKitMutedColor(), false);
            PlaceUguiTopLeft(note, pad + 4f, yCur, panelW - 8f, 30f);
            yCur += 30f;

            GameObject note2 = this.CreateUguiLabel(scrollContent, "Note2",
                this.L("Rebinding moves every action sharing that key — the game draws one hint per key."),
                11f, this.UguiKitMutedColor(), false);
            PlaceUguiTopLeft(note2, pad + 4f, yCur, panelW - 8f, 18f);
            yCur += 24f;

            // An EMPTY list counts as not-ready too. It used to fall through and build a page of
            // empty sections plus a "Show" button whose panel had never been created — which is
            // exactly what a parse regression looked like from the outside: no rows, and a button
            // that silently did nothing.
            List<GameKeyRow> rows = this.TryGetGameKeyRows();
            if (rows == null || rows.Count == 0)
            {
                GameObject wait = this.CreateUguiLabel(scrollContent, "NotReady",
                    this.L("Key bindings are not loaded yet — enter the world and reopen this page."),
                    12f, this.UguiKitTextColor(), false);
                PlaceUguiTopLeft(wait, pad + 4f, yCur, panelW - 8f, 24f);
                this.SetUguiScrollContentHeight(scrollContent, yCur + 40f);
                handle.Root = block;
                this.uguiShellGameKeys = handle;
                return block;
            }

            Color rowText = this.UguiKitTextColor();
            float rowW = panelW - 28f;

            // The camera-mode keys FIRST. They live in AllKeyboardKeysMap, so without this they sit
            // anonymously ("1", "q") among 110 siblings behind a collapsed toggle — which is exactly
            // where they could not be found. These are also the only rows whose hint sprites the
            // icon layer can relabel, since hints resolve through InputEvent.KeyN -> that map.
            List<GameKeyRow> featured = new List<GameKeyRow>();
            for (int f = 0; f < GameKeyFeaturedDirectActions.Length; f++)
            {
                string want = GameKeyFeaturedDirectActions[f][0];
                for (int i = 0; i < rows.Count; i++)
                {
                    if (string.Equals(rows[i].Map, GameKeyDirectMapName, StringComparison.Ordinal)
                        && string.Equals(rows[i].Action, want, StringComparison.Ordinal))
                    {
                        featured.Add(rows[i]);
                        break;
                    }
                }
            }

            if (featured.Count > 0)
            {
                yCur = this.BuildUguiGameKeysPanel(handle, scrollContent, featured,
                    this.L("Camera mode"), pad, yCur, panelW, rowW, rowText, out GameObject _);
            }

            // Everything except the direct-key map, in the documented display order.
            for (int m = 0; m < GameKeyMapOrder.Length; m++)
            {
                string mapName = GameKeyMapOrder[m];
                if (string.Equals(mapName, GameKeyDirectMapName, StringComparison.Ordinal))
                {
                    continue;
                }

                yCur = this.BuildUguiGameKeysSection(handle, scrollContent, rows, mapName,
                    pad, yCur, panelW, rowW, rowText, out GameObject _);
            }

            GameObject resetAll = this.CreateUguiDangerButton(scrollContent, "ResetAll",
                this.L("RESET ALL TO DEFAULTS"), new System.Action(this.OnUguiGameKeysResetAllClicked));
            PlaceUguiTopLeft(resetAll, pad, yCur, panelW, 34f);
            yCur += 34f + 14f;

            // Collapsed tail. It is last on purpose: expanding only grows the scroll height.
            // The button is built ONLY if the map actually produced rows — a toggle with nothing
            // behind it is worse than no toggle, because clicking it looks like a broken page.
            bool hasDirectKeys = false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].Map, GameKeyDirectMapName, StringComparison.Ordinal))
                {
                    hasDirectKeys = true;
                    break;
                }
            }

            if (hasDirectKeys)
            {
                GameObject toggle = this.CreateUguiSecondaryButton(scrollContent, "DirectKeysToggle",
                    this.L("Show all direct keys"), new System.Action(this.OnUguiGameKeysDirectToggleClicked));
                PlaceUguiTopLeft(toggle, pad, yCur, panelW, 28f);
                Transform toggleLabel = toggle.transform.Find("Label");
                handle.DirectKeysToggleLabel = toggleLabel != null ? toggleLabel.gameObject : null;
                yCur += 34f;

                handle.HeightCollapsed = yCur + 12f;

                GameObject directPanel;
                yCur = this.BuildUguiGameKeysSection(handle, scrollContent, rows, GameKeyDirectMapName,
                    pad, yCur, panelW, rowW, rowText, out directPanel);
                handle.DirectKeysPanel = directPanel;
                handle.HeightExpanded = yCur + 12f;

                SetUguiGoActive(handle.DirectKeysPanel, false);
                handle.DirectKeysExpanded = false;
                yCur = handle.HeightCollapsed;
            }

            this.SetUguiScrollContentHeight(scrollContent, yCur + 12f);

            // ---------------- Capture view ----------------
            GameObject capture = this.CreateUguiGo("CaptureView", block.transform);
            PlaceUguiTopLeft(capture, 0f, 0f, w, h);

            GameObject capHeader = this.CreateUguiHeaderLabel(capture.transform, "Header",
                this.L("GAME KEY BINDINGS"), 15f);
            PlaceUguiTopLeft(capHeader, pad + 4f, 10f, panelW - 8f, 22f);

            GameObject capPanel = this.CreateUguiGo("CapturePanel", capture.transform);
            PlaceUguiTopLeft(capPanel, pad, 40f, panelW, 116f);
            Color accent = this.UguiKitAccent();
            this.AddUguiImage(capPanel, new Color(this.uiContentR, this.uiContentG, this.uiContentB,
                Mathf.Clamp(this.uiPanelAlpha * 0.82f, 0.14f, 0.92f)), true, 1f);
            this.AddUguiRingOverlay(capPanel, new Color(accent.r, accent.g, accent.b, 0.24f), 1f);

            GameObject pressLabel = this.CreateUguiLabel(capPanel.transform, "Press",
                this.L("PRESS ANY KEY FOR:"), 13f, this.UguiKitTextColor(), true);
            this.TrySetUguiLabelBold(pressLabel);
            PlaceUguiTopLeft(pressLabel, 12f, 18f, panelW - 24f, 20f);

            GameObject bindingLabel = this.CreateUguiLabel(capPanel.transform, "Binding",
                "", 13f, new Color(1f, 0.86f, 0.36f), true);
            this.TrySetUguiLabelBold(bindingLabel);
            PlaceUguiTopLeft(bindingLabel, 12f, 42f, panelW - 24f, 24f);
            handle.CaptureBindingLabel = bindingLabel;

            GameObject cancelBtn = this.CreateUguiDangerButton(capPanel.transform, "Cancel",
                this.L("CANCEL"), new System.Action(this.OnUguiGameKeyCancelClicked));
            PlaceUguiTopLeft(cancelBtn, (panelW - 240f) * 0.5f, 76f, 240f, 30f);
            handle.CaptureCancelRect = cancelBtn.GetComponent<RectTransform>();

            capture.SetActive(false);
            handle.CaptureRoot = capture;
            handle.CaptureShown = false;

            handle.Root = block;
            this.uguiShellGameKeys = handle;
            return block;
        }

        private const string GameKeyDirectMapName = "AllKeyboardKeysMap";

        // Builds one map's panel and appends its rows to the handle. Returns the new yCur.
        // The featured camera-mode rows are hoisted into their own panel above, so they are skipped
        // here — a binding must not get two rows, or two writers would fight over one override.
        private float BuildUguiGameKeysSection(UguiShellGameKeysHandle handle, Transform scrollContent,
            List<GameKeyRow> allRows, string mapName, float pad, float yCur, float panelW, float rowW,
            Color rowText, out GameObject panelGo)
        {
            bool isDirect = string.Equals(mapName, GameKeyDirectMapName, StringComparison.Ordinal);

            List<GameKeyRow> mapRows = new List<GameKeyRow>();
            for (int i = 0; i < allRows.Count; i++)
            {
                if (!string.Equals(allRows[i].Map, mapName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (isDirect && IsFeaturedDirectAction(allRows[i].Action))
                {
                    continue;
                }

                mapRows.Add(allRows[i]);
            }

            return this.BuildUguiGameKeysPanel(handle, scrollContent, mapRows, this.L(mapName),
                pad, yCur, panelW, rowW, rowText, out panelGo);
        }

        private float BuildUguiGameKeysPanel(UguiShellGameKeysHandle handle, Transform scrollContent,
            List<GameKeyRow> mapRows, string title, float pad, float yCur, float panelW, float rowW,
            Color rowText, out GameObject panelGo)
        {
            panelGo = null;

            if (mapRows.Count == 0)
            {
                return yCur;
            }

            float panelH = 36f + mapRows.Count * 32f;
            GameObject panel = this.CreateUguiSettingsMainPanel(scrollContent, "Panel_" + title,
                title + "  (" + mapRows.Count + ")");
            PlaceUguiTopLeft(panel, pad, yCur, panelW, panelH);
            panelGo = panel;

            for (int r = 0; r < mapRows.Count; r++)
            {
                GameKeyRow keyRow = mapRows[r];

                GameObject row = this.CreateUguiGo("Row_" + keyRow.Map + "_" + keyRow.Index, panel.transform);
                PlaceUguiTopLeft(row, 14f, 36f + r * 32f, rowW, 28f);
                this.AddUguiImage(row, new Color(1f, 1f, 1f, 0.05f), true, 1f);

                GameObject label = this.CreateUguiLabel(row.transform, "Label",
                    FormatGameKeyRowLabel(keyRow), 12f, rowText, false);
                PlaceUguiTopLeft(label, 10f, 1f, rowW - 180f, 26f);

                int rowIndex = handle.Rows.Count; // capture a copy for the closures
                string bindText = FormatControlPath(this.GetGameKeyEffectivePath(keyRow));

                GameObject bindBtn = this.CreateUguiSecondaryButton(row.transform, "Bind",
                    bindText, new System.Action(() => this.OnUguiGameKeyRowClicked(rowIndex)));
                PlaceUguiTopLeft(bindBtn, rowW - 162f, 3f, 124f, 22f);
                this.TrySetUguiButtonLabelSize(bindBtn, 11.5f);

                GameObject resetBtn = this.CreateUguiSecondaryButton(row.transform, "Reset",
                    "x", new System.Action(() => this.OnUguiGameKeyRowResetClicked(rowIndex)));
                PlaceUguiTopLeft(resetBtn, rowW - 30f, 3f, 24f, 22f);
                this.TrySetUguiButtonLabelSize(resetBtn, 11.5f);
                SetUguiGoActive(resetBtn, this.IsGameKeyOverridden(keyRow));

                Transform btnLabel = bindBtn.transform.Find("Label");
                handle.Rows.Add(keyRow);
                handle.RowBindLabels.Add(btnLabel != null ? btnLabel.gameObject : null);
                handle.RowResetButtons.Add(resetBtn);
                handle.RowShownText.Add(bindText);
                handle.RowShownOverridden.Add(this.IsGameKeyOverridden(keyRow));
            }

            return yCur + panelH + 14f;
        }

        // ---- click handlers ---------------------------------------------------------------------

        private void OnUguiGameKeyRowClicked(int rowIndex)
        {
            UguiShellGameKeysHandle handle = this.uguiShellGameKeys;
            if (handle == null || rowIndex < 0 || rowIndex >= handle.Rows.Count)
            {
                return;
            }

            this.gameKeyCaptureIndex = rowIndex;
            handle.CaptureArmedAtFrame = Time.frameCount;
        }

        private void OnUguiGameKeyRowResetClicked(int rowIndex)
        {
            UguiShellGameKeysHandle handle = this.uguiShellGameKeys;
            if (handle == null || rowIndex < 0 || rowIndex >= handle.Rows.Count)
            {
                return;
            }

            // Whole key, same as binding one: a half-reset would leave the split state behind.
            if (this.TryMoveGameKey(handle.Rows[rowIndex], null) > 0)
            {
                this.SaveAllSettings();
            }
        }

        private void OnUguiGameKeyCancelClicked()
        {
            this.gameKeyCaptureIndex = -1;
        }

        private void OnUguiGameKeysResetAllClicked()
        {
            int cleared = this.ClearAllGameKeyOverrides();
            if (cleared > 0)
            {
                this.SaveAllSettings();
            }

            this.AddMenuNotification(
                cleared > 0 ? this.L("Game keys restored to defaults") : this.L("No game key overrides to reset"),
                cleared > 0 ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.82f, 0.45f));
        }

        private void OnUguiGameKeysDirectToggleClicked()
        {
            UguiShellGameKeysHandle handle = this.uguiShellGameKeys;
            if (handle == null || handle.DirectKeysPanel == null)
            {
                // Should be unreachable now the button is only built alongside its panel — but a
                // dead click has to leave a trace rather than look like a frozen page.
                ModLogger.Msg("[InputMap] direct-keys toggle has no panel to show.");
                return;
            }

            handle.DirectKeysExpanded = !handle.DirectKeysExpanded;
            SetUguiGoActive(handle.DirectKeysPanel, handle.DirectKeysExpanded);
            this.SetUguiScrollContentHeight(handle.ScrollContent,
                handle.DirectKeysExpanded ? handle.HeightExpanded : handle.HeightCollapsed);
            this.SetUguiLabelText(handle.DirectKeysToggleLabel,
                this.L(handle.DirectKeysExpanded ? "Hide direct keys" : "Show all direct keys"));
        }

        // ---- capture ----------------------------------------------------------------------------

        // Same guards as the mod-keybinds poller and for the same reasons: the arming click must not
        // read itself as a Mouse0 bind, and Mouse0 over CANCEL must reach the button (onClick fires
        // on pointer UP, so binding on the DOWN frame would exit capture first). Both are scoped to
        // Mouse0 only. Reuses UguiKeybindPollCandidates — the same curated list, deliberately not
        // Enum.GetValues(KeyCode).
        private bool TryCaptureUguiGameKeyFromPolling()
        {
            UguiShellGameKeysHandle handle = this.uguiShellGameKeys;
            if (handle == null || this.gameKeyCaptureIndex < 0 || this.gameKeyCaptureIndex >= handle.Rows.Count)
            {
                return false;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Cancel, NOT "bind to None" — see the file header.
                this.gameKeyCaptureIndex = -1;
                return true;
            }

            bool mouse0Eligible = Time.frameCount != handle.CaptureArmedAtFrame;
            if (mouse0Eligible && handle.CaptureCancelRect != null)
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

            KeyCode pressed = KeyCode.None;
            if (mouse0Eligible && Input.GetMouseButtonDown(0))
            {
                pressed = KeyCode.Mouse0;
            }
            else if (Input.GetMouseButtonDown(1))
            {
                pressed = KeyCode.Mouse1;
            }
            else if (Input.GetMouseButtonDown(2))
            {
                pressed = KeyCode.Mouse2;
            }
            else if (Input.anyKeyDown)
            {
                KeyCode[] candidates = UguiKeybindPollCandidates;
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (Input.GetKeyDown(candidates[i]))
                    {
                        pressed = candidates[i];
                        break;
                    }
                }
            }

            if (pressed == KeyCode.None)
            {
                return false;
            }

            string path = ControlPathForKeyCode(pressed);
            if (path == null)
            {
                // A key with no control in this asset's keyboard layout — stay in capture rather
                // than writing a path the Input System would silently fail to resolve.
                this.AddMenuNotification(this.L("That key cannot be bound"), new Color(1f, 0.82f, 0.45f));
                return false;
            }

            if (this.TryMoveGameKey(handle.Rows[this.gameKeyCaptureIndex], path) > 0)
            {
                this.SaveAllSettings();
            }

            this.gameKeyCaptureIndex = -1;
            return true;
        }

        // ---- per-frame processor ----------------------------------------------------------------

        private void ProcessUguiShellGameKeysOnUpdate()
        {
            UguiShellGameKeysHandle handle = this.uguiShellGameKeys;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSettingsSubTabActive(UguiShellSettingsGameKeysSubIndex))
            {
                return;
            }

            try
            {
                bool capturing = this.gameKeyCaptureIndex >= 0 && this.gameKeyCaptureIndex < handle.Rows.Count;
                if (capturing)
                {
                    string caption = FormatGameKeyRowLabel(handle.Rows[this.gameKeyCaptureIndex]);
                    if (!string.Equals(caption, handle.CaptureShownBinding, StringComparison.Ordinal))
                    {
                        handle.CaptureShownBinding = caption;
                        this.SetUguiLabelText(handle.CaptureBindingLabel, caption.ToUpperInvariant());
                    }

                    this.TryCaptureUguiGameKeyFromPolling();
                    capturing = this.gameKeyCaptureIndex >= 0;
                }

                if (capturing != handle.CaptureShown)
                {
                    handle.CaptureShown = capturing;
                    SetUguiGoActive(handle.NormalRoot, !capturing);
                    SetUguiGoActive(handle.CaptureRoot, capturing);
                }

                if (capturing)
                {
                    return;
                }

                for (int i = 0; i < handle.Rows.Count; i++)
                {
                    GameKeyRow row = handle.Rows[i];

                    string text = FormatControlPath(this.GetGameKeyEffectivePath(row));
                    if (!string.Equals(text, handle.RowShownText[i], StringComparison.Ordinal))
                    {
                        handle.RowShownText[i] = text;
                        this.SetUguiLabelText(handle.RowBindLabels[i], text);
                    }

                    bool overridden = this.IsGameKeyOverridden(row);
                    if (overridden != handle.RowShownOverridden[i])
                    {
                        handle.RowShownOverridden[i] = overridden;
                        SetUguiGoActive(handle.RowResetButtons[i], overridden);
                    }
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Game Keys content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }
    }
}
