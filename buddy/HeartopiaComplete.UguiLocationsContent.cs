using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Resource Gathering > Locations sub-tab: the Gather Locations route
    // (GatherLocationsFeature). No IMGUI ancestor.
    //
    // One flat scroll stack:
    //   Start/Stop button, three live status lines, empty-wait slider, category list (checkbox +
    //   "name (spots)" + Select / ^ / v), New / Export / Import row, and — for the selected
    //   category — rename/delete, Save Current Location and the spot list (^ / v / X).
    //
    // Persistent controls are built once; the category rows and spot rows are destroyed and
    // rebuilt whenever the layout signature (category/spot structure, names, flags, selection)
    // changes, then RelayoutUguiShellLocations owns every y position and the scroll height.
    // Every mutation goes through GatherLocationsFeature (which rebuilds its flattened route) and
    // is followed by SaveKeybinds(false), which persists the category list.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const float UguiLocationsRowW = 420f;
        private const float UguiLocationsRowH = 26f;
        private const float UguiLocationsRowPitch = 32f;

        private sealed class UguiShellLocationsHandle
        {
            public GameObject Root;
            public Transform ScrollContent;

            public string SelectedCategoryId;

            public GameObject StartButton;
            public string StartShown;
            public GameObject LocationLabel;
            public string LocationShown;
            public GameObject StatusLabel;
            public string StatusShown;
            public GameObject EmptyLabel;
            public string EmptyShown;

            public GameObject EmptyWaitLabel;
            public string EmptyWaitShown;
            public Slider EmptyWaitSlider;
            public Toggle StartNearestToggle;

            public GameObject CategoryHeader;
            public readonly List<GameObject> CategoryRows = new List<GameObject>();
            // Parallel to CategoryRows — SyncUguiToggleFromField every gated frame (same Fade/
            // OnEnable race fix as Foraging loot toggles: menu close+reopen left every checkmark opaque).
            public readonly List<Toggle> CategoryEnabledToggles = new List<Toggle>();
            public readonly List<string> CategoryEnabledIds = new List<string>();

            public InputField NewNameInput;
            public GameObject NewCategoryButton;
            public GameObject ExportButton;
            public GameObject ImportButton;

            // Selected-category panel.
            public GameObject SelectedHeader;
            public string SelectedHeaderShown;
            public InputField RenameInput;
            public GameObject ApplyRenameButton;
            public GameObject DeleteButton;
            public GameObject SaveLocationButton;
            public readonly List<GameObject> SpotRows = new List<GameObject>();
            public string ShownRenameForId;

            public int LayoutSignature = int.MinValue;
            public int ErrorCount;
        }

        private UguiShellLocationsHandle uguiShellLocations;

        // ----------------------------------------------------------------------------------------
        // Text builders
        // ----------------------------------------------------------------------------------------

        private string BuildUguiLocationsStartText()
        {
            return this.L(GatherLocationsFeature.Active ? "Stop Locations" : "Start Locations");
        }

        private string BuildUguiLocationsLocationText()
        {
            if (!GatherLocationsFeature.Active || GatherLocationsFeature.RouteCount <= 0)
            {
                return this.LF("Location: - ({0} spots on route)", GatherLocationsFeature.RouteCount);
            }
            return this.LF("Location: {0} / {1} ({2}/{3})",
                GatherLocationsFeature.CurrentCategoryName,
                GatherLocationsFeature.CurrentSpotName,
                GatherLocationsFeature.RouteIndex + 1,
                GatherLocationsFeature.RouteCount);
        }

        private string BuildUguiLocationsStatusText()
        {
            return "Status: " + this.L(GatherLocationsFeature.LastStatus);
        }

        private string BuildUguiLocationsEmptyText()
        {
            float elapsed = GatherLocationsFeature.EmptyElapsedSeconds;
            float wait = GatherLocationsFeature.EmptyWaitSeconds;
            if (elapsed < 0f)
            {
                return this.LF("Empty timer: - / {0:F0}s", wait);
            }
            return this.LF("Empty timer: {0:F0}s / {1:F0}s", elapsed, wait);
        }

        private string BuildUguiLocationsEmptyWaitText()
        {
            return this.LF("Empty wait: {0:F0}s", GatherLocationsFeature.EmptyWaitSeconds);
        }

        private static HeartopiaComplete.LocationCategoryEntry FindUguiLocationsCategory(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            IReadOnlyList<HeartopiaComplete.LocationCategoryEntry> list = GatherLocationsFeature.Categories;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].id == id)
                {
                    return list[i];
                }
            }
            return null;
        }

        private int ComputeUguiLocationsLayoutSignature(UguiShellLocationsHandle handle)
        {
            unchecked
            {
                int h = 17;
                // Active hides reorder buttons — rebuild rows when Start/Stop flips.
                h = h * 31 + (GatherLocationsFeature.Active ? 1 : 0);
                IReadOnlyList<HeartopiaComplete.LocationCategoryEntry> list = GatherLocationsFeature.Categories;
                for (int i = 0; i < list.Count; i++)
                {
                    HeartopiaComplete.LocationCategoryEntry c = list[i];
                    if (c == null)
                    {
                        continue;
                    }
                    h = h * 31 + (c.id ?? "").GetHashCode();
                    h = h * 31 + (c.name ?? "").GetHashCode();
                    h = h * 31 + (c.enabledOnRoute ? 1 : 0);
                    h = h * 31 + (c.spots != null ? c.spots.Count : 0);
                }

                HeartopiaComplete.LocationCategoryEntry sel = FindUguiLocationsCategory(handle.SelectedCategoryId);
                if (sel == null)
                {
                    handle.SelectedCategoryId = null;
                    return h * 31 - 1;
                }
                h = h * 31 + sel.id.GetHashCode();
                if (sel.spots != null)
                {
                    for (int i = 0; i < sel.spots.Count; i++)
                    {
                        HeartopiaComplete.LocationSpotEntry s = sel.spots[i];
                        if (s == null)
                        {
                            continue;
                        }
                        h = h * 31 + (s.name ?? "").GetHashCode();
                        h = h * 31 + s.x.GetHashCode();
                        h = h * 31 + s.y.GetHashCode();
                        h = h * 31 + s.z.GetHashCode();
                    }
                }
                return h;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Builder
        // ----------------------------------------------------------------------------------------

        private GameObject BuildUguiShellLocationsContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellLocations = null;

            UguiShellLocationsHandle handle = new UguiShellLocationsHandle();
            GameObject block = this.CreateUguiGo("LocationsContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            Transform scrollContent;
            GameObject scroll = this.CreateUguiScrollView(block.transform, "Scroll", 10f, out scrollContent);
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
            handle.ScrollContent = scrollContent;

            GameObject header = this.CreateUguiHeaderLabel(scrollContent, "LocationsHeader",
                this.L("Gather Locations"), 14f);
            PlaceUguiTopLeft(header, 16f, 8f, 320f, 22f);

            handle.StartShown = this.BuildUguiLocationsStartText();
            handle.StartButton = this.CreateUguiPrimaryButton(scrollContent, "StartButton",
                handle.StartShown, new System.Action(this.OnUguiLocationsStartClicked));
            PlaceUguiTopLeft(handle.StartButton, 16f, 36f, 260f, 35f);

            handle.LocationShown = this.BuildUguiLocationsLocationText();
            handle.LocationLabel = this.CreateUguiBodyLabel(scrollContent, "LocationLabel", handle.LocationShown, 12f);
            PlaceUguiTopLeft(handle.LocationLabel, 16f, 78f, 460f, 20f);

            handle.StatusShown = this.BuildUguiLocationsStatusText();
            handle.StatusLabel = this.CreateUguiBodyLabel(scrollContent, "StatusLabel", handle.StatusShown, 12f);
            PlaceUguiTopLeft(handle.StatusLabel, 16f, 102f, 460f, 20f);

            handle.EmptyShown = this.BuildUguiLocationsEmptyText();
            handle.EmptyLabel = this.CreateUguiBodyLabel(scrollContent, "EmptyLabel", handle.EmptyShown, 12f);
            PlaceUguiTopLeft(handle.EmptyLabel, 16f, 126f, 460f, 20f);

            handle.EmptyWaitShown = this.BuildUguiLocationsEmptyWaitText();
            handle.EmptyWaitLabel = this.CreateUguiBodyLabel(scrollContent, "EmptyWaitLabel", handle.EmptyWaitShown, 12f);
            PlaceUguiTopLeft(handle.EmptyWaitLabel, 16f, 156f, 320f, 20f);

            handle.EmptyWaitSlider = this.CreateUguiSlider(scrollContent, "EmptyWaitSlider",
                1f, 120f, Mathf.Clamp(GatherLocationsFeature.EmptyWaitSeconds, 1f, 120f), true,
                new System.Action<float>(this.OnUguiLocationsEmptyWaitChanged));
            PlaceUguiTopLeft(handle.EmptyWaitSlider.gameObject, 16f, 178f, 260f, 20f);

            handle.StartNearestToggle = this.CreateUguiCheckbox(scrollContent, "StartNearestToggle",
                this.L("Start at nearest spot"), GatherLocationsFeature.StartNearest,
                new System.Action<bool>(this.OnUguiLocationsStartNearestToggled));
            PlaceUguiTopLeft(handle.StartNearestToggle.gameObject, 16f, 204f, 320f, 26f);

            handle.CategoryHeader = this.CreateUguiHeaderLabel(scrollContent, "CategoryHeader",
                this.L("Categories"), 14f);

            handle.NewNameInput = this.CreateUguiInputField(scrollContent, "NewNameInput", "", 40, null);
            handle.NewCategoryButton = this.CreateUguiSecondaryButton(scrollContent, "NewCategoryButton",
                this.L("+ New Category"), new System.Action(this.OnUguiLocationsNewCategoryClicked));
            handle.ExportButton = this.CreateUguiSecondaryButton(scrollContent, "ExportButton",
                this.L("Export Selected"), new System.Action(this.OnUguiLocationsExportClicked));
            handle.ImportButton = this.CreateUguiSecondaryButton(scrollContent, "ImportButton",
                this.L("Import"), new System.Action(this.OnUguiLocationsImportClicked));

            handle.SelectedHeader = this.CreateUguiHeaderLabel(scrollContent, "SelectedHeader", "", 14f);
            handle.RenameInput = this.CreateUguiInputField(scrollContent, "RenameInput", "", 40, null);
            handle.ApplyRenameButton = this.CreateUguiSecondaryButton(scrollContent, "ApplyRenameButton",
                this.L("Rename"), new System.Action(this.OnUguiLocationsRenameClicked));
            handle.DeleteButton = this.CreateUguiDangerButton(scrollContent, "DeleteButton",
                this.L("Delete"), new System.Action(this.OnUguiLocationsDeleteClicked));
            handle.SaveLocationButton = this.CreateUguiPrimaryButton(scrollContent, "SaveLocationButton",
                this.L("Save Current Location"), new System.Action(this.OnUguiLocationsSaveLocationClicked));

            this.RebuildUguiShellLocationsRows(handle);
            handle.LayoutSignature = this.ComputeUguiLocationsLayoutSignature(handle);
            this.RelayoutUguiShellLocations(handle);

            handle.Root = block;
            this.uguiShellLocations = handle;
            return block;
        }

        // ----------------------------------------------------------------------------------------
        // Rows — destroy all + rebuild at y=0; RelayoutUguiShellLocations must always run next.
        // ----------------------------------------------------------------------------------------

        private void RebuildUguiShellLocationsRows(UguiShellLocationsHandle handle)
        {
            for (int i = 0; i < handle.CategoryRows.Count; i++)
            {
                if (handle.CategoryRows[i] != null)
                {
                    try { UnityEngine.Object.Destroy(handle.CategoryRows[i]); } catch { }
                }
            }
            handle.CategoryRows.Clear();
            handle.CategoryEnabledToggles.Clear();
            handle.CategoryEnabledIds.Clear();
            for (int i = 0; i < handle.SpotRows.Count; i++)
            {
                if (handle.SpotRows[i] != null)
                {
                    try { UnityEngine.Object.Destroy(handle.SpotRows[i]); } catch { }
                }
            }
            handle.SpotRows.Clear();

            Transform content = handle.ScrollContent;
            if (content == null)
            {
                return;
            }

            HeartopiaComplete.LocationCategoryEntry selected = FindUguiLocationsCategory(handle.SelectedCategoryId);
            if (selected == null)
            {
                handle.SelectedCategoryId = null;
            }

            IReadOnlyList<HeartopiaComplete.LocationCategoryEntry> list = GatherLocationsFeature.Categories;
            for (int i = 0; i < list.Count; i++)
            {
                HeartopiaComplete.LocationCategoryEntry c = list[i];
                if (c == null)
                {
                    continue;
                }
                string id = c.id;
                bool isSelected = id == handle.SelectedCategoryId;

                GameObject row = this.CreateUguiGo("CatRow" + i, content);
                PlaceUguiTopLeft(row, 16f, 0f, UguiLocationsRowW, UguiLocationsRowH);

                int spotCount = c.spots != null ? c.spots.Count : 0;
                Toggle enabled = this.CreateUguiCheckbox(row.transform, "Enabled",
                    (c.name ?? "") + " (" + spotCount + ")", c.enabledOnRoute,
                    new System.Action<bool>(v => this.OnUguiLocationsCategoryEnabledToggled(id, v)));
                PlaceUguiTopLeft(enabled.gameObject, 0f, 0f, 236f, UguiLocationsRowH);
                handle.CategoryEnabledToggles.Add(enabled);
                handle.CategoryEnabledIds.Add(id);

                GameObject selectBtn = isSelected
                    ? this.CreateUguiPrimaryButton(row.transform, "Select", this.L("Selected"),
                        new System.Action(() => this.OnUguiLocationsSelectClicked(id)))
                    : this.CreateUguiSecondaryButton(row.transform, "Select", this.L("Select"),
                        new System.Action(() => this.OnUguiLocationsSelectClicked(id)));
                PlaceUguiTopLeft(selectBtn, 242f, 0f, 76f, UguiLocationsRowH);

                // Reorder is disabled while the route runs — mid-travel list edits desync travelIndex.
                if (!GatherLocationsFeature.Active)
                {
                    GameObject up = this.CreateUguiSecondaryButton(row.transform, "Up", "^",
                        new System.Action(() => this.OnUguiLocationsMoveCategoryClicked(id, -1)));
                    PlaceUguiTopLeft(up, 324f, 0f, 44f, UguiLocationsRowH);

                    GameObject down = this.CreateUguiSecondaryButton(row.transform, "Down", "v",
                        new System.Action(() => this.OnUguiLocationsMoveCategoryClicked(id, 1)));
                    PlaceUguiTopLeft(down, 374f, 0f, 44f, UguiLocationsRowH);
                }

                handle.CategoryRows.Add(row);
            }

            if (selected != null && selected.spots != null)
            {
                bool showReorder = !GatherLocationsFeature.Active;
                for (int i = 0; i < selected.spots.Count; i++)
                {
                    HeartopiaComplete.LocationSpotEntry s = selected.spots[i];
                    if (s == null)
                    {
                        continue;
                    }
                    string catId = selected.id;
                    int index = i;
                    UguiListRowButtonSpec goBtn = new UguiListRowButtonSpec
                    {
                        Label = this.L("Go"), Tier = UguiListRowTierSecondary, Width = 44f, Enabled = true,
                        OnClick = new System.Action(() => this.OnUguiLocationsGoSpotClicked(catId, index))
                    };
                    UguiListRowButtonSpec[] spotButtons = showReorder
                        ? new UguiListRowButtonSpec[]
                        {
                            goBtn,
                            new UguiListRowButtonSpec
                            {
                                Label = "^", Tier = UguiListRowTierSecondary, Width = 34f, Enabled = true,
                                OnClick = new System.Action(() => this.OnUguiLocationsMoveSpotClicked(catId, index, -1))
                            },
                            new UguiListRowButtonSpec
                            {
                                Label = "v", Tier = UguiListRowTierSecondary, Width = 34f, Enabled = true,
                                OnClick = new System.Action(() => this.OnUguiLocationsMoveSpotClicked(catId, index, 1))
                            },
                            new UguiListRowButtonSpec
                            {
                                Label = "X", Tier = UguiListRowTierDanger, Width = 34f, Enabled = true,
                                OnClick = new System.Action(() => this.OnUguiLocationsRemoveSpotClicked(catId, index))
                            },
                        }
                        : new UguiListRowButtonSpec[]
                        {
                            goBtn,
                            new UguiListRowButtonSpec
                            {
                                Label = "X", Tier = UguiListRowTierDanger, Width = 34f, Enabled = true,
                                OnClick = new System.Action(() => this.OnUguiLocationsRemoveSpotClicked(catId, index))
                            },
                        };
                    UguiListRowHandle row = this.CreateUguiListRow(content, "Spot" + i,
                        16f, 0f, UguiLocationsRowW, UguiLocationsRowH,
                        $"{s.name} ({s.x:F0}, {s.y:F0}, {s.z:F0})", null, null,
                        false, true, null, spotButtons);
                    handle.SpotRows.Add(row.Root);
                }
            }
        }

        private void RelayoutUguiShellLocations(UguiShellLocationsHandle handle)
        {
            HeartopiaComplete.LocationCategoryEntry selected = FindUguiLocationsCategory(handle.SelectedCategoryId);

            float yCur = 236f;
            PlaceUguiTopLeft(handle.CategoryHeader, 16f, yCur, 320f, 22f);
            yCur += 28f;

            for (int i = 0; i < handle.CategoryRows.Count; i++)
            {
                if (handle.CategoryRows[i] != null)
                {
                    PlaceUguiTopLeft(handle.CategoryRows[i], 16f, yCur, UguiLocationsRowW, UguiLocationsRowH);
                }
                yCur += UguiLocationsRowPitch;
            }
            yCur += 4f;

            PlaceUguiTopLeft(handle.NewNameInput.gameObject, 16f, yCur, 200f, UguiLocationsRowH);
            PlaceUguiTopLeft(handle.NewCategoryButton, 222f, yCur, 150f, UguiLocationsRowH);
            yCur += 34f;
            PlaceUguiTopLeft(handle.ExportButton, 16f, yCur, 150f, UguiLocationsRowH);
            PlaceUguiTopLeft(handle.ImportButton, 172f, yCur, 100f, UguiLocationsRowH);
            yCur += 40f;

            bool hasSelected = selected != null;
            SetUguiGoActive(handle.SelectedHeader, hasSelected);
            SetUguiGoActive(handle.RenameInput.gameObject, hasSelected);
            SetUguiGoActive(handle.ApplyRenameButton, hasSelected);
            SetUguiGoActive(handle.DeleteButton, hasSelected);
            SetUguiGoActive(handle.SaveLocationButton, hasSelected);

            if (hasSelected)
            {
                string headerText = this.LF("Selected: {0} ({1})", selected.name,
                    selected.spots != null ? selected.spots.Count : 0);
                this.SyncUguiSelfLabelText(handle.SelectedHeader, ref handle.SelectedHeaderShown, headerText);
                PlaceUguiTopLeft(handle.SelectedHeader, 16f, yCur, 420f, 22f);
                yCur += 28f;

                if (handle.ShownRenameForId != selected.id)
                {
                    handle.ShownRenameForId = selected.id;
                    handle.RenameInput.text = selected.name ?? "";
                }
                PlaceUguiTopLeft(handle.RenameInput.gameObject, 16f, yCur, 200f, UguiLocationsRowH);
                PlaceUguiTopLeft(handle.ApplyRenameButton, 222f, yCur, 90f, UguiLocationsRowH);
                PlaceUguiTopLeft(handle.DeleteButton, 318f, yCur, 90f, UguiLocationsRowH);
                yCur += 36f;

                PlaceUguiTopLeft(handle.SaveLocationButton, 16f, yCur, 260f, 30f);
                yCur += 38f;

                for (int i = 0; i < handle.SpotRows.Count; i++)
                {
                    if (handle.SpotRows[i] != null)
                    {
                        PlaceUguiTopLeft(handle.SpotRows[i], 16f, yCur, UguiLocationsRowW, UguiLocationsRowH);
                    }
                    yCur += UguiLocationsRowPitch;
                }
            }
            else
            {
                handle.ShownRenameForId = null;
            }

            this.SetUguiScrollContentHeight(handle.ScrollContent, yCur + 20f);
        }

        private void RefreshUguiLocationsNow()
        {
            UguiShellLocationsHandle handle = this.uguiShellLocations;
            if (handle == null || handle.Root == null)
            {
                return;
            }
            try
            {
                this.RebuildUguiShellLocationsRows(handle);
                handle.LayoutSignature = this.ComputeUguiLocationsLayoutSignature(handle);
                this.RelayoutUguiShellLocations(handle);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Locations refresh error: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame driver
        // ----------------------------------------------------------------------------------------

        private void ProcessUguiShellLocationsOnUpdate()
        {
            UguiShellLocationsHandle handle = this.uguiShellLocations;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellResourceGatheringSubTabActive(UguiShellLocationsSubIndex))
            {
                return;
            }

            try
            {
                string startText = this.BuildUguiLocationsStartText();
                if (!string.Equals(startText, handle.StartShown, StringComparison.Ordinal))
                {
                    handle.StartShown = startText;
                    this.SetUguiButtonLabel(handle.StartButton, startText);
                }

                this.SyncUguiSelfLabelText(handle.LocationLabel, ref handle.LocationShown,
                    this.BuildUguiLocationsLocationText());
                this.SyncUguiSelfLabelText(handle.StatusLabel, ref handle.StatusShown,
                    this.BuildUguiLocationsStatusText());
                this.SyncUguiSelfLabelText(handle.EmptyLabel, ref handle.EmptyShown,
                    this.BuildUguiLocationsEmptyText());

                this.SyncUguiToggleFromField(handle.StartNearestToggle, GatherLocationsFeature.StartNearest);

                // Category enabled checkboxes: re-apply isOn + graphic SetActive every frame while
                // visible — CreateUguiCheckbox's Fade checkmark goes opaque on menu/tab re-enable
                // (see SyncUguiToggleFromField). Same pattern as Foraging loot toggles.
                for (int i = 0; i < handle.CategoryEnabledToggles.Count && i < handle.CategoryEnabledIds.Count; i++)
                {
                    HeartopiaComplete.LocationCategoryEntry c = FindUguiLocationsCategory(handle.CategoryEnabledIds[i]);
                    this.SyncUguiToggleFromField(handle.CategoryEnabledToggles[i], c != null && c.enabledOnRoute);
                }

                float wait = GatherLocationsFeature.EmptyWaitSeconds;
                float sliderValue = Mathf.Clamp(wait, 1f, 120f);
                if (handle.EmptyWaitSlider != null && Mathf.Abs(handle.EmptyWaitSlider.value - sliderValue) > 0.0005f)
                {
                    handle.EmptyWaitSlider.SetValueWithoutNotify(sliderValue);
                }
                this.SyncUguiSelfLabelText(handle.EmptyWaitLabel, ref handle.EmptyWaitShown,
                    this.BuildUguiLocationsEmptyWaitText());

                int signature = this.ComputeUguiLocationsLayoutSignature(handle);
                if (signature != handle.LayoutSignature)
                {
                    handle.LayoutSignature = signature;
                    this.RebuildUguiShellLocationsRows(handle);
                    this.RelayoutUguiShellLocations(handle);
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Locations content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Handlers
        // ----------------------------------------------------------------------------------------

        private void OnUguiLocationsStartClicked()
        {
            if (GatherLocationsFeature.Active)
            {
                GatherLocationsFeature.Stop(this);
                this.AddMenuNotification(this.L("Gather Locations stopped"), new Color(1f, 0.55f, 0.55f));
            }
            else
            {
                GatherLocationsFeature.Start(this);
                if (GatherLocationsFeature.Active)
                {
                    this.AddMenuNotification(this.L("Gather Locations started"), new Color(0.45f, 1f, 0.55f));
                }
            }
        }

        private void OnUguiLocationsEmptyWaitChanged(float value)
        {
            float rounded = Mathf.Round(value);
            if (Mathf.Abs(rounded - GatherLocationsFeature.EmptyWaitSeconds) <= 0.0001f)
            {
                return;
            }
            GatherLocationsFeature.SetEmptyWaitSeconds(rounded);
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiLocationsStartNearestToggled(bool value)
        {
            if (GatherLocationsFeature.StartNearest == value)
            {
                return;
            }
            GatherLocationsFeature.SetStartNearest(value);
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiLocationsCategoryEnabledToggled(string id, bool value)
        {
            HeartopiaComplete.LocationCategoryEntry category = FindUguiLocationsCategory(id);
            if (category == null || category.enabledOnRoute == value)
            {
                return;
            }
            GatherLocationsFeature.SetCategoryEnabled(id, value);
            try { this.SaveKeybinds(false); } catch { }
            UguiShellLocationsHandle handle = this.uguiShellLocations;
            if (handle != null)
            {
                handle.LayoutSignature = this.ComputeUguiLocationsLayoutSignature(handle);
            }
        }

        private void OnUguiLocationsSelectClicked(string id)
        {
            UguiShellLocationsHandle handle = this.uguiShellLocations;
            if (handle == null)
            {
                return;
            }
            handle.SelectedCategoryId = id;
            this.RefreshUguiLocationsNow();
        }

        private void OnUguiLocationsMoveCategoryClicked(string id, int delta)
        {
            GatherLocationsFeature.MoveCategory(id, delta);
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiLocationsNow();
        }

        private void OnUguiLocationsNewCategoryClicked()
        {
            UguiShellLocationsHandle handle = this.uguiShellLocations;
            if (handle == null)
            {
                return;
            }
            string name = handle.NewNameInput != null ? handle.NewNameInput.text : "";
            string id = GatherLocationsFeature.AddCategory(name);
            handle.SelectedCategoryId = id;
            if (handle.NewNameInput != null)
            {
                handle.NewNameInput.text = "";
            }
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiLocationsNow();
        }

        private void OnUguiLocationsExportClicked()
        {
            if (!GatherLocationsFeature.TryExportSelected(this, out string error))
            {
                this.AddMenuNotification(this.L(error ?? "Export failed"), new Color(1f, 0.55f, 0.55f));
            }
        }

        private void OnUguiLocationsImportClicked()
        {
            bool ok = GatherLocationsFeature.TryImport(this, out string error);
            if (!ok)
            {
                this.AddMenuNotification(this.L(error ?? "Import failed"), new Color(1f, 0.55f, 0.55f));
                return;
            }
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiLocationsNow();
        }

        private void OnUguiLocationsRenameClicked()
        {
            UguiShellLocationsHandle handle = this.uguiShellLocations;
            if (handle == null || string.IsNullOrEmpty(handle.SelectedCategoryId) || handle.RenameInput == null)
            {
                return;
            }
            GatherLocationsFeature.RenameCategory(handle.SelectedCategoryId, handle.RenameInput.text);
            handle.ShownRenameForId = null; // re-seed the input with the sanitized name
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiLocationsNow();
        }

        private void OnUguiLocationsDeleteClicked()
        {
            UguiShellLocationsHandle handle = this.uguiShellLocations;
            if (handle == null || string.IsNullOrEmpty(handle.SelectedCategoryId))
            {
                return;
            }
            GatherLocationsFeature.DeleteCategory(handle.SelectedCategoryId);
            handle.SelectedCategoryId = null;
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiLocationsNow();
        }

        private void OnUguiLocationsSaveLocationClicked()
        {
            UguiShellLocationsHandle handle = this.uguiShellLocations;
            if (handle == null || string.IsNullOrEmpty(handle.SelectedCategoryId))
            {
                return;
            }
            if (GatherLocationsFeature.AddSpotAtPlayer(this, handle.SelectedCategoryId))
            {
                try { this.SaveKeybinds(false); } catch { }
                this.RefreshUguiLocationsNow();
            }
        }

        private void OnUguiLocationsGoSpotClicked(string categoryId, int index)
        {
            if (GatherLocationsFeature.TryGoToSpot(this, categoryId, index))
            {
                this.AddMenuNotification(this.L("Going to spot"), new Color(0.45f, 1f, 0.55f));
            }
        }

        private void OnUguiLocationsMoveSpotClicked(string categoryId, int index, int delta)
        {
            GatherLocationsFeature.MoveSpot(categoryId, index, delta);
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiLocationsNow();
        }

        private void OnUguiLocationsRemoveSpotClicked(string categoryId, int index)
        {
            GatherLocationsFeature.RemoveSpot(categoryId, index);
            try { this.SaveKeybinds(false); } catch { }
            this.RefreshUguiLocationsNow();
        }
    }
}
