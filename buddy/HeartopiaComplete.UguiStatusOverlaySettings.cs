using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // Settings → Status Overlay. The show toggle and the overlay's own scale live here, plus one
    // checkbox per row CollectLiveFeatureStatusEntries can emit, in that same catalog order.
    // Checked = drawn. The in-menu LIVE rail is not filtered by these.
    public partial class HeartopiaComplete
    {
        private sealed class UguiStatusOverlaySettingsHandle
        {
            public GameObject Root;
            public Toggle ShowToggle;
            public GameObject ScaleLabel;
            public Slider ScaleSlider;
            public readonly List<Toggle> RowToggles = new List<Toggle>();
            public int ErrorCount;
        }

        private UguiStatusOverlaySettingsHandle uguiStatusOverlaySettings;

        private GameObject BuildUguiShellStatusOverlaySettingsContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiStatusOverlaySettings = null;
            UguiStatusOverlaySettingsHandle handle = new UguiStatusOverlaySettingsHandle();
            try
            {
                GameObject block = this.CreateUguiGo("SettingsStatusOverlayContent", parent);
                PlaceUguiTopLeft(block, x, y, w, h);
                this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

                const float pad = 16f;
                GameObject title = this.CreateUguiHeaderLabel(block.transform, "Title", this.L("Status Overlay"), 18f);
                PlaceUguiTopLeft(title, pad, 12f, w - pad * 2f, 26f);

                handle.ShowToggle = this.CreateUguiCheckbox(block.transform, "ShowToggle",
                    this.L("Show Status Overlay"), this.showStatusOverlay,
                    new Action<bool>(this.OnUguiSettingsMainShowOverlayChanged));
                PlaceUguiTopLeft(handle.ShowToggle.gameObject, pad, 46f, w - pad * 2f, 24f);

                handle.ScaleLabel = this.CreateUguiBodyLabel(block.transform, "ScaleLabel",
                    this.LF("Overlay Scale: {0}%", Mathf.RoundToInt(this.GetStatusOverlayScale() * 100f)), 13f);
                PlaceUguiTopLeft(handle.ScaleLabel, pad, 80f, 180f, 20f);
                handle.ScaleSlider = this.CreateUguiSlider(block.transform, "ScaleSlider",
                    UiScaleMin, UiScaleMax, this.GetStatusOverlayScale(), false,
                    new Action<float>(this.OnUguiSettingsMainOverlayScaleChanged));
                PlaceUguiTopLeft(handle.ScaleSlider.gameObject, 200f, 78f, w - 200f - pad, 22f);

                const float rowStep = 28f;
                int rowCount = StatusOverlayEntryLabels.Length;
                Transform rowsContent;
                GameObject scroll = this.CreateUguiScrollView(block.transform, "Rows",
                    rowCount * rowStep + 16f, out rowsContent);
                PlaceUguiTopLeft(scroll, 8f, 112f, w - 16f, h - 120f);
                try
                {
                    Image scrollBg = scroll.GetComponent<Image>();
                    if (scrollBg != null)
                    {
                        scrollBg.color = Color.clear;
                    }
                    if (rowsContent != null && rowsContent.parent != null)
                    {
                        Image viewportBg = rowsContent.parent.GetComponent<Image>();
                        if (viewportBg != null)
                        {
                            viewportBg.color = Color.clear;
                        }
                    }
                }
                catch { }

                float rowW = w - 16f - 22f;
                for (int i = 0; i < rowCount; i++)
                {
                    string label = StatusOverlayEntryLabels[i];
                    Toggle tog = this.CreateUguiCheckbox(rowsContent, "Row" + i,
                        this.L(label), !this.statusOverlayHiddenLabels.Contains(label),
                        delegate(bool shown) { this.OnUguiStatusOverlayRowChanged(label, shown); });
                    PlaceUguiTopLeft(tog.gameObject, 8f, 8f + i * rowStep, rowW - 16f, 24f);
                    handle.RowToggles.Add(tog);
                }

                handle.Root = block;
                this.uguiStatusOverlaySettings = handle;
                return block;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiStatusOverlay] settings build failed: " + ex.Message);
                return null;
            }
        }

        private void OnUguiStatusOverlayRowChanged(string label, bool shown)
        {
            if (string.IsNullOrEmpty(label))
            {
                return;
            }
            bool hidden = this.statusOverlayHiddenLabels.Contains(label);
            if (shown == !hidden)
            {
                return;
            }
            if (shown)
            {
                this.statusOverlayHiddenLabels.Remove(label);
            }
            else
            {
                this.statusOverlayHiddenLabels.Add(label);
            }
            this.SaveKeybinds(false);
        }

        private void ProcessUguiShellStatusOverlaySettingsOnUpdate()
        {
            UguiStatusOverlaySettingsHandle handle = this.uguiStatusOverlaySettings;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSettingsSubTabActive(UguiShellSettingsStatusOverlaySubIndex))
            {
                return;
            }

            try
            {
                this.SyncUguiToggleFromField(handle.ShowToggle, this.showStatusOverlay);
                string scaleText = this.LF("Overlay Scale: {0}%", Mathf.RoundToInt(this.GetStatusOverlayScale() * 100f));
                this.SetUguiLabelText(handle.ScaleLabel, scaleText);
                if (handle.ScaleSlider != null
                    && Mathf.Abs(handle.ScaleSlider.value - this.GetStatusOverlayScale()) > 0.001f)
                {
                    handle.ScaleSlider.SetValueWithoutNotify(this.GetStatusOverlayScale());
                }

                int n = Mathf.Min(handle.RowToggles.Count, StatusOverlayEntryLabels.Length);
                for (int i = 0; i < n; i++)
                {
                    bool shown = !this.statusOverlayHiddenLabels.Contains(StatusOverlayEntryLabels[i]);
                    this.SyncUguiToggleFromField(handle.RowToggles[i], shown);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiStatusOverlay] settings sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }
    }
}
