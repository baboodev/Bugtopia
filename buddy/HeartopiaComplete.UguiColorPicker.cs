using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // Furniture Dye picker — a floating window with a graphics-app colour picker (SV square + hue
    // strip + hex field), shown while a DYEABLE object is focused in build mode.
    //
    // The model half (what is focused, which parts it has, how a colour reaches the object) lives
    // in FurnitureDyeFeature.cs. This file is only the widget tree, the pointer polling and the
    // throttling.
    //
    // ── WHY THE SV SQUARE IS THREE IMAGES AND NOT A TEXTURE ─────────────────────────────────────
    // The obvious build is one Texture2D regenerated per hue. At 128x128 that is 16k SetPixel
    // interop calls on every drag frame — unusable. Instead the square is the standard three-layer
    // composite, which is exact rather than an approximation:
    //
    //   layer 1  solid, colour = the pure hue           -> hue
    //   layer 2  white, alpha ramp 1..0 left to right   -> lerp(white, hue, s)
    //   layer 3  black, alpha ramp 0..1 top to bottom   -> that * v
    //
    // Only layer 1 changes with the hue, and it is a plain `img.color =`. The two ramp textures are
    // 256x1 and 1x256, generated ONCE for the session. The hue strip is a third one-off 1x256.
    // Nothing is regenerated while dragging.
    //
    // ── INPUT ───────────────────────────────────────────────────────────────────────────────────
    // POLLED, like the kit's window drag (ProcessUguiWindowDrag) — no IDragHandler components, so
    // nothing has to be injected into the IL2CPP type system. RectangleContainsScreenPoint picks
    // the surface on mouse-down, ScreenPointToLocalPointInRectangle turns the cursor into
    // normalized coordinates, and the drag stays captured by whichever surface claimed it until
    // the button is released.
    //
    // ── APPLYING ────────────────────────────────────────────────────────────────────────────────
    // The object recolours live while dragging, throttled to UguiDyeApplyIntervalSec so a drag
    // costs a handful of Mono invokes rather than one per frame, plus one guaranteed apply on
    // release so the final colour is never the throttled-away one. Nothing is SENT here: the
    // change rides the player's own build confirm, and the server prices it normally.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const float UguiDyeW = 300f;
        private const float UguiDyeH = 442f;
        private const float UguiDyeTitleH = 24f;
        private const int UguiDyeSortingOrder = 29360;  // beside the Move Panel (29350), below Shell

        private const float UguiDyePad = 16f;
        private const float UguiDyeSquareW = UguiDyeW - UguiDyePad * 2f;   // 268
        private const float UguiDyeSquareH = 150f;
        private const float UguiDyeHueH = 18f;
        private const int UguiDyeRampSize = 256;
        private const int UguiDyePalettePerRow = 9;
        private const float UguiDyeSwatch = 26f;

        // A drag repaints the object at most this often; the release always repaints once more.
        private const float UguiDyeApplyIntervalSec = 0.08f;
        // The focus walk is five AuraMono invokes plus a list enumeration. At 10 Hz it is
        // invisible in the frame budget and still feels instant when you click a new object;
        // per-frame it would be one of the most expensive things the mod does.
        private const float UguiDyeFocusIntervalSec = 0.1f;

        private const int UguiDyeSurfaceNone = 0;
        private const int UguiDyeSurfaceSquare = 1;
        private const int UguiDyeSurfaceHue = 2;

        private sealed class UguiDyePickerHandle
        {
            public UguiWindowHandle Window;
            public GameObject HeaderLabel;
            public string HeaderShown;
            public GameObject PartsRow;
            public readonly List<Image> PartButtons = new List<Image>();
            public readonly List<GameObject> PartRoots = new List<GameObject>();
            public RectTransform SquareRt;
            public Image SquareHue;          // layer 1 — the only one that changes with the hue
            public RectTransform SquareCursor;
            public RectTransform HueRt;
            public RectTransform HueCursor;
            public Image Preview;
            public InputField HexField;
            public string HexShown;
            public GameObject PaletteRow;
            public readonly List<Image> PaletteSwatches = new List<Image>();
            public readonly List<GameObject> PaletteRoots = new List<GameObject>();
            public GameObject StatusLabel;
            public string StatusShown;
            public float LastSyncedUiScale = -1f;
            public int ErrorCount;
        }

        private UguiDyePickerHandle uguiDyePicker;
        private bool uguiDyePickerBuildFailed;

        // Shared one-off textures (session lifetime, DontUnloadUnusedAsset like the kit's own).
        private Sprite uguiDyeSatRampSprite;   // white, alpha 1 -> 0 left to right
        private Sprite uguiDyeValRampSprite;   // black, alpha 0 -> 1 top to bottom
        private Sprite uguiDyeHueSprite;       // full hue ramp, bottom to top
        private Texture2D uguiDyeSatRampTex, uguiDyeValRampTex, uguiDyeHueTex;

        // Live picker state — HSV is the source of truth so a drag to V=0 does not lose the hue.
        private float uguiDyeH = 0f, uguiDyeS = 1f, uguiDyeV = 1f;
        private int uguiDyeSelectedPart;
        private int uguiDyeDragSurface = UguiDyeSurfaceNone;
        private float uguiDyeNextApplyAt;
        private bool uguiDyeApplyPending;
        private int uguiDyeSyncedStaticId = -1;
        private bool uguiDyeSyncedPanel;
        private string uguiDyeStatus = string.Empty;
        private float uguiDyeNextFocusAt;
        // Set while the picker writes the hex field itself. InputField.text's setter fires
        // onValueChanged synchronously, so without this the display refresh re-enters the input
        // path and stages a dye edit nobody asked for (see OnUguiDyeHexChanged).
        private bool uguiDyeHexWriting;

        // The user-facing switch (Self -> Building). Persisted as furnitureDyePickerEnabled.
        private bool furnitureDyePickerEnabled;

        // ----------------------------------------------------------------------------------------
        // Per-frame driver — called from OnUpdate next to the other UGUI processors.
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiDyePickerOnUpdate()
        {
            try
            {
                if (!this.furnitureDyePickerEnabled)
                {
                    if (this.uguiDyePicker != null && this.IsUguiWindowVisible(this.uguiDyePicker.Window))
                    {
                        this.SetUguiWindowVisible(this.uguiDyePicker.Window, false);
                    }
                    return;
                }

                // The focus walk is the expensive part, so it runs only while the feature is on,
                // and then only on its own 10 Hz clock. A drag in progress skips it entirely: the
                // target cannot change while the button is held, and re-reading mid-drag would
                // fight the colours we are writing.
                if (this.uguiDyeDragSurface == UguiDyeSurfaceNone
                    && Time.unscaledTime >= this.uguiDyeNextFocusAt)
                {
                    this.uguiDyeNextFocusAt = Time.unscaledTime + UguiDyeFocusIntervalSec;
                    this.RefreshFurnitureDyeTarget();
                }
                FurnitureDyeTarget target = this.FurnitureDyeFocusedTarget;
                bool show = target != null;

                UguiDyePickerHandle handle = this.uguiDyePicker;
                if (handle == null)
                {
                    if (!show || this.uguiDyePickerBuildFailed)
                    {
                        return;
                    }
                    this.BuildUguiDyePicker();
                    handle = this.uguiDyePicker;
                    if (handle == null)
                    {
                        return;
                    }
                }

                if (handle.ErrorCount >= 3)
                {
                    return;
                }

                if (this.IsUguiWindowVisible(handle.Window) != show)
                {
                    this.SetUguiWindowVisible(handle.Window, show);
                    if (!show)
                    {
                        this.uguiDyeDragSurface = UguiDyeSurfaceNone;
                    }
                }
                if (!show)
                {
                    return;
                }

                this.ProcessUguiWindowFrame(handle.Window);

                float targetScale = this.GetUiScale();
                if (!Mathf.Approximately(targetScale, handle.LastSyncedUiScale))
                {
                    handle.LastSyncedUiScale = targetScale;
                    this.SetUguiWindowScale(handle.Window, targetScale);
                }

                this.SyncUguiDyePickerToTarget(handle, target);
                this.ProcessUguiDyePickerInput(handle);
                this.FlushUguiDyeApply(target);
                this.RefreshUguiDyePickerVisuals(handle, target);
            }
            catch (Exception ex)
            {
                UguiDyePickerHandle h = this.uguiDyePicker;
                if (h != null)
                {
                    h.ErrorCount++;
                }
                this.uguiDyeDragSurface = UguiDyeSurfaceNone;
                ModLogger.Msg("[UguiDye] tick error ("
                    + (h != null ? h.ErrorCount : 0) + "/3, disabled at 3): " + ex.Message);
            }
        }

        // A new item under the cursor resets the picker to that item's CURRENT colour, so the
        // panel always opens showing what is actually on the object rather than a stale hue.
        private void SyncUguiDyePickerToTarget(UguiDyePickerHandle handle, FurnitureDyeTarget target)
        {
            bool panel = target.Source == FurnitureDyeSource.Panel;

            // In panel mode the GAME owns part selection - it has its own tab strip right there,
            // and two competing selectors would be a bug generator. Follow it, and adopt that
            // part's colour whenever it changes under us.
            if (panel && this.uguiDyeSelectedPart != target.PanelSelectedPart
                && this.uguiDyeSyncedStaticId == target.StaticId)
            {
                this.uguiDyeSelectedPart = target.PanelSelectedPart;
                this.AdoptUguiDyeColorFromTarget(target);
                this.RebuildUguiDyePaletteRow(handle, target);
            }

            if (this.uguiDyeSyncedStaticId == target.StaticId && this.uguiDyeSyncedPanel == panel)
            {
                return;
            }

            this.uguiDyeSyncedStaticId = target.StaticId;
            this.uguiDyeSyncedPanel = panel;
            this.uguiDyeSelectedPart = panel ? target.PanelSelectedPart : 0;
            this.uguiDyeStatus = string.Empty;
            this.AdoptUguiDyeColorFromTarget(target);
            this.RebuildUguiDyePartRow(handle, target);
            this.RebuildUguiDyePaletteRow(handle, target);
        }

        private void AdoptUguiDyeColorFromTarget(FurnitureDyeTarget target)
        {
            if (target == null || target.Parts == null || target.Parts.Count == 0)
            {
                return;
            }
            int idx = Mathf.Clamp(this.uguiDyeSelectedPart, 0, target.Parts.Count - 1);
            FurnitureDyeSubPart first = target.Parts[idx].Sub[0];
            int packed = target.Current.TryGetValue(first.Body, out int live) ? live : first.DefaultColor;
            Color.RGBToHSV(FurnitureDyeUnpack(packed), out this.uguiDyeH, out this.uguiDyeS, out this.uguiDyeV);
        }

        // ----------------------------------------------------------------------------------------
        // Pointer polling
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiDyePickerInput(UguiDyePickerHandle handle)
        {
            if (handle.SquareRt == null || handle.HueRt == null)
            {
                return;
            }

            Vector3 m3 = Input.mousePosition;
            Vector2 mouse = new Vector2(m3.x, m3.y);

            if (this.uguiDyeDragSurface == UguiDyeSurfaceNone)
            {
                if (!Input.GetMouseButtonDown(0))
                {
                    return;
                }
                if (RectTransformUtility.RectangleContainsScreenPoint(handle.SquareRt, mouse, null))
                {
                    this.uguiDyeDragSurface = UguiDyeSurfaceSquare;
                }
                else if (RectTransformUtility.RectangleContainsScreenPoint(handle.HueRt, mouse, null))
                {
                    this.uguiDyeDragSurface = UguiDyeSurfaceHue;
                }
                else
                {
                    return;
                }
            }
            else if (!Input.GetMouseButton(0))
            {
                // Release: whatever the throttle swallowed, apply it now.
                this.uguiDyeDragSurface = UguiDyeSurfaceNone;
                this.uguiDyeApplyPending = true;
                this.uguiDyeNextApplyAt = 0f;
                return;
            }

            // The captured surface keeps the drag even when the cursor leaves its rect — that is
            // what every graphics tool does, and it is the difference between a picker that feels
            // sloppy and one that does not.
            RectTransform rt = this.uguiDyeDragSurface == UguiDyeSurfaceSquare ? handle.SquareRt : handle.HueRt;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, mouse, null, out Vector2 local))
            {
                return;
            }

            Rect r = rt.rect;
            float nx = Mathf.Clamp01((local.x - r.xMin) / Mathf.Max(1f, r.width));
            float ny = Mathf.Clamp01((local.y - r.yMin) / Mathf.Max(1f, r.height));

            if (this.uguiDyeDragSurface == UguiDyeSurfaceSquare)
            {
                this.uguiDyeS = nx;
                this.uguiDyeV = ny;
            }
            else
            {
                this.uguiDyeH = nx;
            }

            this.uguiDyeApplyPending = true;
        }

        private void FlushUguiDyeApply(FurnitureDyeTarget target)
        {
            if (!this.uguiDyeApplyPending || target == null || target.Parts == null
                || target.Parts.Count == 0)
            {
                return;
            }
            if (Time.unscaledTime < this.uguiDyeNextApplyAt)
            {
                return;
            }

            this.uguiDyeApplyPending = false;
            this.uguiDyeNextApplyAt = Time.unscaledTime + UguiDyeApplyIntervalSec;

            int packed = this.CurrentUguiDyePacked();
            if (this.UguiDyeSelectedPartAlreadyIs(target, packed))
            {
                return;
            }
            this.ApplyUguiDyeSelection(target, packed);
        }

        // True when every body of the selected part already carries `packed` - on the object, or
        // as its default when the object is undyed. Staging that would be a paid no-op.
        private bool UguiDyeSelectedPartAlreadyIs(FurnitureDyeTarget target, int packed)
        {
            int idx = Mathf.Clamp(this.uguiDyeSelectedPart, 0, target.Parts.Count - 1);
            List<FurnitureDyeSubPart> sub = target.Parts[idx].Sub;
            for (int i = 0; i < sub.Count; i++)
            {
                int have = target.Current.TryGetValue(sub[i].Body, out int live) ? live : sub[i].DefaultColor;
                if (!UguiDyeSameColour(have, packed))
                {
                    return false;
                }
            }
            return true;
        }

        // The picker holds HSV and packs back to RGB, so a colour adopted from the object comes
        // back up to 1/255 off per channel. That is not a choice the player made - treat it as
        // the same colour, or the no-op guard above would wave the round-trip through as "new".
        private static bool UguiDyeSameColour(int a, int b)
        {
            for (int shift = 8; shift <= 24; shift += 8)
            {
                int d = ((a >> shift) & 0xFF) - ((b >> shift) & 0xFF);
                if (d > 1 || d < -1)
                {
                    return false;
                }
            }
            return true;
        }

        private int CurrentUguiDyePacked()
        {
            return FurnitureDyePack(Color.HSVToRGB(this.uguiDyeH, this.uguiDyeS, this.uguiDyeV));
        }

        // Every part keeps its own colour: the rows we send are the object's CURRENT colours with
        // only the selected part replaced. Sending just the one row would clear the others, because
        // ModifyDyeColor replaces the whole list rather than merging into it.
        //
        // A part may cover SEVERAL bodies (ColorParts merged on partNameTextId). The palette path
        // gives each of them its own coordinated colors[i]; a FREE colour has no per-body variant,
        // so every body in the selected part takes the picked colour. That is the only thing "one
        // arbitrary colour for this part" can mean - the item palette row is where the game's
        // coordinated pairing still lives.
        private void ApplyUguiDyeSelection(FurnitureDyeTarget target, int packed)
        {
            int idx = Mathf.Clamp(this.uguiDyeSelectedPart, 0, target.Parts.Count - 1);
            List<KeyValuePair<byte, int>> rows = this.BuildUguiDyeRows(target);
            for (int i = 0; i < rows.Count; i++)
            {
                if (this.FindUguiDyeSubPart(target, idx, rows[i].Key) != null)
                {
                    rows[i] = new KeyValuePair<byte, int>(rows[i].Key, packed);
                }
            }

            if (target.Source == FurnitureDyeSource.Panel)
            {
                // The panel path stages into the game's own pending-edit dictionary and keeps
                // target.Current in step itself; the player's Confirm sends it.
                this.TryApplyFurnitureDyePanel(target, packed, out string panelStatus);
                this.uguiDyeStatus = panelStatus;
                return;
            }

            if (this.TryApplyFurnitureDye(rows, out string status))
            {
                // Keep the local mirror in step so the next part switch reads what we just wrote
                // instead of waiting for the next focus refresh.
                for (int i = 0; i < rows.Count; i++)
                {
                    target.Current[rows[i].Key] = rows[i].Value;
                }
                this.uguiDyeStatus = FurnitureDyeHex(packed) + " — confirm the placement to save";
            }
            else
            {
                this.uguiDyeStatus = status;
            }
        }

        // The object's CURRENT colours for every body of every part, in a stable order - the base
        // the callers then overwrite. Built from Current so untouched parts keep what they have,
        // and capped at what one build operation can carry.
        private List<KeyValuePair<byte, int>> BuildUguiDyeRows(FurnitureDyeTarget target)
        {
            List<KeyValuePair<byte, int>> rows = new List<KeyValuePair<byte, int>>();
            for (int i = 0; i < target.Parts.Count; i++)
            {
                List<FurnitureDyeSubPart> sub = target.Parts[i].Sub;
                for (int j = 0; j < sub.Count; j++)
                {
                    int colour = target.Current.TryGetValue(sub[j].Body, out int live)
                        ? live : sub[j].DefaultColor;
                    rows.Add(new KeyValuePair<byte, int>(sub[j].Body, colour));
                }
            }
            return rows;
        }

        private FurnitureDyeSubPart FindUguiDyeSubPart(FurnitureDyeTarget target, int partIndex, byte body)
        {
            if (partIndex < 0 || partIndex >= target.Parts.Count)
            {
                return null;
            }
            List<FurnitureDyeSubPart> sub = target.Parts[partIndex].Sub;
            for (int j = 0; j < sub.Count; j++)
            {
                if (sub[j].Body == body)
                {
                    return sub[j];
                }
            }
            return null;
        }

        private void OnUguiDyePartClicked(int index)
        {
            FurnitureDyeTarget target = this.FurnitureDyeFocusedTarget;
            if (target == null || target.Parts == null || index < 0 || index >= target.Parts.Count)
            {
                return;
            }
            this.uguiDyeSelectedPart = index;
            this.AdoptUguiDyeColorFromTarget(target);   // show that part's colour, do not impose ours
        }

        private void OnUguiDyeSwatchClicked(int index)
        {
            FurnitureDyeTarget target = this.FurnitureDyeFocusedTarget;
            if (target == null || target.Parts == null || target.Parts.Count == 0)
            {
                return;
            }
            int p = Mathf.Clamp(this.uguiDyeSelectedPart, 0, target.Parts.Count - 1);
            FurnitureDyePart part = target.Parts[p];
            if (index < 0 || index >= part.Palette.Length)
            {
                return;
            }

            // Faithful to DyeColorPanel.UpdatePartDyeColor: swatch i writes EACH sub-part's own
            // colors[i], not one colour across the whole group.
            List<KeyValuePair<byte, int>> rows = this.BuildUguiDyeRows(target);
            for (int i = 0; i < rows.Count; i++)
            {
                FurnitureDyeSubPart sub = this.FindUguiDyeSubPart(target, p, rows[i].Key);
                if (sub != null && index < sub.Palette.Length)
                {
                    rows[i] = new KeyValuePair<byte, int>(rows[i].Key, sub.Palette[index]);
                }
            }

            if (this.TryApplyFurnitureDye(rows, out string swatchStatus))
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    target.Current[rows[i].Key] = rows[i].Value;
                }
                this.AdoptUguiDyeColorFromTarget(target);
                this.uguiDyeStatus = "palette colour " + (index + 1) + " applied";
            }
            else
            {
                this.uguiDyeStatus = swatchStatus;
            }
        }

        private void OnUguiDyeHexChanged(string text)
        {
            // BUG FIX: the picker's own display refresh assigns HexField.text, and that fires this
            // handler. Treating it as input staged the item's CURRENT colour the moment the window
            // appeared - in panel mode a Confirm then charged a dye for a no-op, and in build mode
            // merely focusing an object re-wrote its colours into the build save. Only the player's
            // typing is input.
            if (this.uguiDyeHexWriting)
            {
                return;
            }
            if (!TryParseFurnitureDyeHex(text, out int packed))
            {
                return; // half-typed input is not an error — just not a colour yet
            }
            Color.RGBToHSV(FurnitureDyeUnpack(packed), out this.uguiDyeH, out this.uguiDyeS, out this.uguiDyeV);
            this.uguiDyeApplyPending = true;
            this.uguiDyeNextApplyAt = 0f;
        }

        private void OnUguiDyeResetClicked()
        {
            FurnitureDyeTarget target = this.FurnitureDyeFocusedTarget;
            if (target == null || target.Parts == null || target.Parts.Count == 0)
            {
                return;
            }
            List<KeyValuePair<byte, int>> rows = new List<KeyValuePair<byte, int>>();
            for (int i = 0; i < target.Parts.Count; i++)
            {
                List<FurnitureDyeSubPart> sub = target.Parts[i].Sub;
                for (int j = 0; j < sub.Count; j++)
                {
                    rows.Add(new KeyValuePair<byte, int>(sub[j].Body, sub[j].DefaultColor));
                }
            }
            if (target.Source == FurnitureDyeSource.Panel)
            {
                // One part at a time: the panel stages the SELECTED part, so resetting everything
                // would claim more than we did.
                int p = Mathf.Clamp(this.uguiDyeSelectedPart, 0, target.Parts.Count - 1);
                this.TryApplyFurnitureDyePanel(target, target.Parts[p].Sub[0].DefaultColor,
                                               out string panelStatus);
                this.AdoptUguiDyeColorFromTarget(target);
                this.uguiDyeStatus = panelStatus;
                return;
            }

            if (this.TryApplyFurnitureDye(rows, out string status))
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    target.Current[rows[i].Key] = rows[i].Value;
                }
                this.AdoptUguiDyeColorFromTarget(target);
                this.uguiDyeStatus = "reset to the item's default colours";
            }
            else
            {
                this.uguiDyeStatus = status;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Visual refresh — every setter is change-gated (TMP re-layout hygiene, and an Image whose
        // colour is re-assigned every frame dirties its canvas batch for nothing).
        // ----------------------------------------------------------------------------------------

        private void RefreshUguiDyePickerVisuals(UguiDyePickerHandle handle, FurnitureDyeTarget target)
        {
            Color pure = Color.HSVToRGB(this.uguiDyeH, 1f, 1f);
            if (handle.SquareHue != null && handle.SquareHue.color != pure)
            {
                handle.SquareHue.color = pure;
            }

            Color live = Color.HSVToRGB(this.uguiDyeH, this.uguiDyeS, this.uguiDyeV);
            if (handle.Preview != null && handle.Preview.color != live)
            {
                handle.Preview.color = live;
            }

            if (handle.SquareCursor != null)
            {
                handle.SquareCursor.anchoredPosition = new Vector2(
                    this.uguiDyeS * UguiDyeSquareW, -(1f - this.uguiDyeV) * UguiDyeSquareH);
            }
            if (handle.HueCursor != null)
            {
                handle.HueCursor.anchoredPosition = new Vector2(this.uguiDyeH * UguiDyeSquareW, 0f);
            }

            string hex = FurnitureDyeHex(this.CurrentUguiDyePacked());
            if (handle.HexField != null && handle.HexShown != hex)
            {
                handle.HexShown = hex;
                // Assigning .text fires onValueChanged; the parse is idempotent so the round trip
                // is harmless, but skip it while the field has focus so typing is never stomped.
                if (!handle.HexField.isFocused)
                {
                    this.uguiDyeHexWriting = true;
                    try
                    {
                        handle.HexField.text = hex;
                    }
                    finally
                    {
                        this.uguiDyeHexWriting = false;
                    }
                }
            }

            string header = "#" + target.StaticId
                + (target.Parts.Count > 1 ? "  ·  part " + (this.uguiDyeSelectedPart + 1)
                                            + "/" + target.Parts.Count : string.Empty)
                + (target.Source == FurnitureDyeSource.Panel ? "  ·  panel" : string.Empty);
            if (handle.HeaderShown != header)
            {
                handle.HeaderShown = header;
                this.SetUguiLabelText(handle.HeaderLabel, header);
            }

            if (handle.StatusShown != this.uguiDyeStatus)
            {
                handle.StatusShown = this.uguiDyeStatus;
                this.SetUguiLabelText(handle.StatusLabel, this.uguiDyeStatus);
            }

            for (int i = 0; i < handle.PartButtons.Count; i++)
            {
                Color want = i == this.uguiDyeSelectedPart ? this.UguiKitAccent() : this.UguiKitControlFill();
                if (handle.PartButtons[i] != null && handle.PartButtons[i].color != want)
                {
                    handle.PartButtons[i].color = want;
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        private void BuildUguiDyePicker()
        {
            this.uguiDyePicker = null;
            UguiDyePickerHandle handle = null;
            try
            {
                if (!this.EnsureUguiDyeSprites())
                {
                    this.uguiDyePickerBuildFailed = true;
                    ModLogger.Msg("[UguiDye] gradient sprites unavailable — picker disabled");
                    return;
                }

                handle = new UguiDyePickerHandle();
                handle.Window = this.CreateUguiWindow(
                    "BugtopiaUguiDyePicker", null, null,
                    new Vector2(UguiDyeW, UguiDyeH), UguiDyeSortingOrder, UguiDyeTitleH);
                Transform t = handle.Window.PanelRt;

                Color text = this.UguiKitTextColor();
                Color dim = new Color(text.r, text.g, text.b, 0.7f);

                GameObject title = this.CreateUguiLabel(t, "Title", this.L("Dye colour"), 12f, text, false);
                this.TrySetUguiLabelBold(title);
                PlaceUguiTopLeft(title, UguiDyePad, 4f, 160f, 18f);

                handle.HeaderShown = string.Empty;
                handle.HeaderLabel = this.CreateUguiLabel(t, "Header", string.Empty, 11f, dim, false);
                PlaceUguiTopLeft(handle.HeaderLabel, UguiDyeW - 130f, 5f, 114f, 18f);

                float y = 30f;
                handle.PartsRow = this.CreateUguiGo("PartsRow", t);
                PlaceUguiTopLeft(handle.PartsRow, UguiDyePad, y, UguiDyeSquareW, 24f);

                y += 30f;
                handle.SquareRt = this.BuildUguiDyeSquare(handle, t, UguiDyePad, y);

                y += UguiDyeSquareH + 12f;
                handle.HueRt = this.BuildUguiDyeHueStrip(handle, t, UguiDyePad, y);

                y += UguiDyeHueH + 14f;
                GameObject prevGo = this.CreateUguiGo("Preview", t);
                PlaceUguiTopLeft(prevGo, UguiDyePad, y, 46f, 26f);
                handle.Preview = this.AddUguiImage(prevGo, Color.white, true, 2f);

                handle.HexField = this.CreateUguiInputField(t, "Hex", "#FFFFFF", 7,
                    new System.Action<string>(this.OnUguiDyeHexChanged));
                PlaceUguiTopLeft(handle.HexField.gameObject, UguiDyePad + 54f, y, 96f, 26f);

                GameObject reset = this.CreateUguiSecondaryButton(t, "Reset", this.L("Reset"),
                    new System.Action(this.OnUguiDyeResetClicked));
                PlaceUguiTopLeft(reset, UguiDyePad + 158f, y, UguiDyeSquareW - 158f, 26f);

                y += 34f;
                GameObject palLabel = this.CreateUguiLabel(t, "PaletteLabel",
                    this.L("Item palette"), 10f, dim, false);
                PlaceUguiTopLeft(palLabel, UguiDyePad, y, UguiDyeSquareW, 16f);

                y += 18f;
                handle.PaletteRow = this.CreateUguiGo("PaletteRow", t);
                PlaceUguiTopLeft(handle.PaletteRow, UguiDyePad, y, UguiDyeSquareW, UguiDyeSwatch * 3f + 8f);

                y += UguiDyeSwatch * 3f + 12f;
                handle.StatusShown = string.Empty;
                handle.StatusLabel = this.CreateUguiLabel(t, "Status", string.Empty, 10f, dim, false);
                PlaceUguiTopLeft(handle.StatusLabel, UguiDyePad, y, UguiDyeSquareW, 30f);

                // Opening position: top-right, clear of the Move Panel's top-left corner.
                float s = (handle.Window.Scale >= 0.1f) ? handle.Window.Scale : 1f;
                handle.Window.PanelRt.anchoredPosition = new Vector2(
                    Screen.width / s * 0.5f - UguiDyeW * 0.5f - 14f,
                    Screen.height / s * 0.5f - 150f - UguiDyeH * 0.5f);
                this.ClampUguiWindowPosition(handle.Window);

                this.uguiDyePicker = handle;

                this.RegisterUguiThemeRebuilder("UguiDyePicker",
                    new System.Action(this.RebuildUguiDyePickerForTheme));

                // Floating (non-modal) input surface — closures read the LIVE field, never the
                // captured handle, because a theme rebuild replaces it.
                this.RegisterInputOwnershipSurface("UguiDyePicker", false,
                    () => this.uguiDyePicker != null && this.IsUguiWindowVisible(this.uguiDyePicker.Window),
                    () => this.uguiDyePicker != null && this.IsUguiWindowPointerOver(this.uguiDyePicker.Window));

                FeatureLog.Life(FurnitureDyeTag, "colour picker built (sortingOrder "
                    + UguiDyeSortingOrder + ")");
            }
            catch (Exception ex)
            {
                this.uguiDyePickerBuildFailed = true;
                try
                {
                    if (handle != null && handle.Window != null && handle.Window.Root != null)
                    {
                        Object.Destroy(handle.Window.Root);
                    }
                }
                catch { }
                this.uguiDyePicker = null;
                FeatureLog.Fail(FurnitureDyeTag, "picker build failed: " + ex.Message);
            }
        }

        private RectTransform BuildUguiDyeSquare(UguiDyePickerHandle handle, Transform parent, float x, float y)
        {
            GameObject root = this.CreateUguiGo("SVSquare", parent);
            PlaceUguiTopLeft(root, x, y, UguiDyeSquareW, UguiDyeSquareH);

            // Layer 1 — the pure hue. raycastTarget stays on so the window hit test knows the
            // pointer is over us (the picker polls its own rects, but input OWNERSHIP is decided
            // by the kit's standard test).
            handle.SquareHue = this.AddUguiImage(root, Color.red, false, 1f);
            handle.SquareHue.raycastTarget = true;

            GameObject sat = this.CreateUguiGo("Sat", root.transform);
            PlaceUguiTopLeft(sat, 0f, 0f, UguiDyeSquareW, UguiDyeSquareH);
            Image satImg = this.AddUguiImage(sat, Color.white, false, 1f);
            satImg.sprite = this.uguiDyeSatRampSprite;
            satImg.type = Image.Type.Simple;

            GameObject val = this.CreateUguiGo("Val", root.transform);
            PlaceUguiTopLeft(val, 0f, 0f, UguiDyeSquareW, UguiDyeSquareH);
            Image valImg = this.AddUguiImage(val, Color.white, false, 1f);
            valImg.sprite = this.uguiDyeValRampSprite;
            valImg.type = Image.Type.Simple;

            GameObject cursor = this.CreateUguiGo("Cursor", root.transform);
            PlaceUguiTopLeft(cursor, -6f, -6f, 12f, 12f);
            Image ring = this.AddUguiImage(cursor, Color.white, false, 1f);
            if (this.EnsureUguiRingSprite())
            {
                ring.sprite = this.uguiKitRingSprite;
                ring.type = Image.Type.Simple;
            }
            handle.SquareCursor = cursor.GetComponent<RectTransform>();

            return root.GetComponent<RectTransform>();
        }

        private RectTransform BuildUguiDyeHueStrip(UguiDyePickerHandle handle, Transform parent, float x, float y)
        {
            GameObject root = this.CreateUguiGo("HueStrip", parent);
            PlaceUguiTopLeft(root, x, y, UguiDyeSquareW, UguiDyeHueH);
            Image img = this.AddUguiImage(root, Color.white, false, 1f);
            img.sprite = this.uguiDyeHueSprite;
            img.type = Image.Type.Simple;
            img.raycastTarget = true;

            GameObject cursor = this.CreateUguiGo("HueCursor", root.transform);
            PlaceUguiTopLeft(cursor, -2f, -2f, 4f, UguiDyeHueH + 4f);
            this.AddUguiImage(cursor, Color.white, false, 1f);
            handle.HueCursor = cursor.GetComponent<RectTransform>();

            return root.GetComponent<RectTransform>();
        }

        private void RebuildUguiDyePartRow(UguiDyePickerHandle handle, FurnitureDyeTarget target)
        {
            for (int i = 0; i < handle.PartRoots.Count; i++)
            {
                try { Object.Destroy(handle.PartRoots[i]); } catch { }
            }
            handle.PartRoots.Clear();
            handle.PartButtons.Clear();

            // Panel mode: the game's own tab strip is on screen, ours would be a second one.
            bool many = target.Parts.Count > 1 && target.Source != FurnitureDyeSource.Panel;
            SetUguiGoActive(handle.PartsRow, many);
            if (!many)
            {
                return;
            }

            float w = Mathf.Min(60f, (UguiDyeSquareW - (target.Parts.Count - 1) * 6f) / target.Parts.Count);
            for (int i = 0; i < target.Parts.Count; i++)
            {
                int copy = i;
                GameObject btn = this.CreateUguiSecondaryButton(handle.PartsRow.transform, "Part" + i,
                    this.L("Part") + " " + (i + 1), () => this.OnUguiDyePartClicked(copy));
                PlaceUguiTopLeft(btn, i * (w + 6f), 0f, w, 24f);
                handle.PartRoots.Add(btn);
                handle.PartButtons.Add(btn.GetComponent<Image>());
            }
        }

        private void RebuildUguiDyePaletteRow(UguiDyePickerHandle handle, FurnitureDyeTarget target)
        {
            for (int i = 0; i < handle.PaletteRoots.Count; i++)
            {
                try { Object.Destroy(handle.PaletteRoots[i]); } catch { }
            }
            handle.PaletteRoots.Clear();
            handle.PaletteSwatches.Clear();

            // Same reasoning as the part row: in panel mode the game already shows this item's
            // swatches, and a second copy of them is noise.
            SetUguiGoActive(handle.PaletteRow, target.Source != FurnitureDyeSource.Panel);
            if (target.Source == FurnitureDyeSource.Panel)
            {
                return;
            }

            int p = Mathf.Clamp(this.uguiDyeSelectedPart, 0, target.Parts.Count - 1);
            int[] palette = target.Parts[p].Palette;
            if (palette == null)
            {
                return;
            }

            int max = Mathf.Min(palette.Length, UguiDyePalettePerRow * 3);
            for (int i = 0; i < max; i++)
            {
                int copy = i;
                GameObject cell = this.CreateUguiGo("Swatch" + i, handle.PaletteRow.transform);
                PlaceUguiTopLeft(cell,
                    (i % UguiDyePalettePerRow) * (UguiDyeSwatch + 3f),
                    (i / UguiDyePalettePerRow) * (UguiDyeSwatch + 2f),
                    UguiDyeSwatch, UguiDyeSwatch);
                Image img = this.AddUguiImage(cell, FurnitureDyeUnpack(palette[i]), true, 2f);
                img.raycastTarget = true;
                Button b = cell.AddComponent<Button>();
                b.targetGraphic = img;
                this.WireUguiClick(b.onClick, () => this.OnUguiDyeSwatchClicked(copy));
                handle.PaletteRoots.Add(cell);
                handle.PaletteSwatches.Add(img);
            }
        }

        private void RebuildUguiDyePickerForTheme()
        {
            UguiDyePickerHandle old = this.uguiDyePicker;
            try
            {
                if (old != null && old.Window != null && old.Window.Root != null)
                {
                    Object.Destroy(old.Window.Root);
                }
            }
            catch { }
            this.uguiDyePicker = null;
            this.uguiDyeSyncedStaticId = -1;   // force a re-sync so the rows come back
            this.uguiDyePickerBuildFailed = false;
        }

        // ----------------------------------------------------------------------------------------
        // The three one-off gradient sprites
        // ----------------------------------------------------------------------------------------

        private bool EnsureUguiDyeSprites()
        {
            if (this.uguiDyeSatRampSprite != null && this.uguiDyeValRampSprite != null
                && this.uguiDyeHueSprite != null)
            {
                return true;
            }

            try
            {
                if (this.uguiDyeSatRampSprite == null)
                {
                    // White, opaque at x=0 fading to clear at x=1 — over the hue this yields
                    // lerp(white, hue, s) exactly.
                    this.uguiDyeSatRampTex = NewUguiDyeTexture(UguiDyeRampSize, 1);
                    for (int x = 0; x < UguiDyeRampSize; x++)
                    {
                        float s = x / (float)(UguiDyeRampSize - 1);
                        this.uguiDyeSatRampTex.SetPixel(x, 0, new Color(1f, 1f, 1f, 1f - s));
                    }
                    this.uguiDyeSatRampTex.Apply();
                    this.uguiDyeSatRampSprite = NewUguiDyeSprite(this.uguiDyeSatRampTex, UguiDyeRampSize, 1);
                }

                if (this.uguiDyeValRampSprite == null)
                {
                    // Black, clear at the TOP (v=1) to opaque at the bottom (v=0). Texture v=0 is
                    // the bottom row in Unity, so index 0 is the opaque end.
                    this.uguiDyeValRampTex = NewUguiDyeTexture(1, UguiDyeRampSize);
                    for (int y = 0; y < UguiDyeRampSize; y++)
                    {
                        float v = y / (float)(UguiDyeRampSize - 1);
                        this.uguiDyeValRampTex.SetPixel(0, y, new Color(0f, 0f, 0f, 1f - v));
                    }
                    this.uguiDyeValRampTex.Apply();
                    this.uguiDyeValRampSprite = NewUguiDyeSprite(this.uguiDyeValRampTex, 1, UguiDyeRampSize);
                }

                if (this.uguiDyeHueSprite == null)
                {
                    // Horizontal hue ramp — the strip is wide, so the ramp runs along x.
                    this.uguiDyeHueTex = NewUguiDyeTexture(UguiDyeRampSize, 1);
                    for (int x = 0; x < UguiDyeRampSize; x++)
                    {
                        Color c = Color.HSVToRGB(x / (float)(UguiDyeRampSize - 1), 1f, 1f);
                        this.uguiDyeHueTex.SetPixel(x, 0, c);
                    }
                    this.uguiDyeHueTex.Apply();
                    this.uguiDyeHueSprite = NewUguiDyeSprite(this.uguiDyeHueTex, UguiDyeRampSize, 1);
                }

                return this.uguiDyeSatRampSprite != null && this.uguiDyeValRampSprite != null
                    && this.uguiDyeHueSprite != null;
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(FurnitureDyeTag, "gradient sprite build failed: " + ex.Message);
                return false;
            }
        }

        private static Texture2D NewUguiDyeTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;   // no wrap bleed at the ramp ends
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private static Sprite NewUguiDyeSprite(Texture2D tex, int w, int h)
        {
            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f,
                                          0, SpriteMeshType.FullRect);
            if (sprite != null)
            {
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            return sprite;
        }
    }
}
