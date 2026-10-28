using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        // Empty set = no restriction. Non-empty = allow-list shared by the radar category and the
        // matching farm. Ids are insect item ids / bird entity ids, the values of the suffix maps.
        private readonly List<int> radarInsectSpeciesFilterList = new List<int>();
        private readonly List<int> radarBirdSpeciesFilterList = new List<int>();
        private readonly HashSet<int> radarInsectSpeciesFilter = new HashSet<int>();
        private readonly HashSet<int> radarBirdSpeciesFilter = new HashSet<int>();

        private List<RadarSpeciesFilterEntry> radarInsectCatalog;
        private List<RadarSpeciesFilterEntry> radarBirdCatalog;
        private string radarSpeciesCatalogLanguage = string.Empty;
        private readonly Dictionary<uint, InsectFilterItemIdCache> insectFilterItemIdCache = new Dictionary<uint, InsectFilterItemIdCache>();
        private int insectFilterItemIdCacheEpoch = -1;
        private const float InsectFilterItemIdCacheSeconds = 5f;
        private const float InsectFilterItemIdMissSeconds = 0.5f;

        private struct InsectFilterItemIdCache
        {
            public int ItemId;
            public float ExpiresAt;
        }
        private bool radarSpeciesNamesPending;
        private bool radarSpeciesCatalogRebuilt;
        private int radarSpeciesNameMisses;
        private float radarSpeciesNameRetryAt;

        private static Dictionary<int, string> radarInsectItemIconKeys;
        private static Dictionary<int, string> radarBirdItemIconKeys;

        private sealed class RadarSpeciesFilterEntry
        {
            public int Id;
            public string Name = string.Empty;
            public string NameFold = string.Empty;
            public string IconKey = string.Empty;
            public string Canonical = string.Empty;
            public bool NameUnresolved;
        }

        private void ReplaceRadarSpeciesFilter(List<int> source, List<int> dest, HashSet<int> set)
        {
            dest.Clear();
            set.Clear();
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                int id = source[i];
                if (id <= 0 || !set.Add(id))
                {
                    continue;
                }

                dest.Add(id);
            }
        }

        private void ToggleRadarSpeciesFilter(bool insects, int id)
        {
            if (id <= 0)
            {
                return;
            }

            HashSet<int> set = insects ? this.radarInsectSpeciesFilter : this.radarBirdSpeciesFilter;
            List<int> list = insects ? this.radarInsectSpeciesFilterList : this.radarBirdSpeciesFilterList;
            if (set.Remove(RadarSpeciesFilterNoneId))
            {
                list.Remove(RadarSpeciesFilterNoneId);
            }

            if (!set.Add(id))
            {
                set.Remove(id);
                list.Remove(id);
            }
            else
            {
                list.Add(id);
            }

            this.QueueRadarSettingsSave();
            this.DropRadarMarkersRejectedBySpeciesFilter(insects);
            UguiRadarFilterHandle handle = this.uguiRadarFilter;
            if (handle != null)
            {
                this.RebuildUguiRadarFilterVisible(handle, false);
                this.SyncUguiRadarFilterChrome(handle);
                this.SyncUguiRadarFilterGrid(handle);
            }
        }

        private void ClearRadarSpeciesFilter(bool insects)
        {
            HashSet<int> set = insects ? this.radarInsectSpeciesFilter : this.radarBirdSpeciesFilter;
            List<int> list = insects ? this.radarInsectSpeciesFilterList : this.radarBirdSpeciesFilterList;
            if (set.Count == 0)
            {
                return;
            }

            set.Clear();
            list.Clear();
            this.QueueRadarSettingsSave();
            UguiRadarFilterHandle handle = this.uguiRadarFilter;
            if (handle != null)
            {
                this.RebuildUguiRadarFilterVisible(handle, false);
                this.SyncUguiRadarFilterChrome(handle);
                this.SyncUguiRadarFilterGrid(handle);
            }
        }

        private bool IsInsectSpeciesAllowed(int staticId)
        {
            if (this.radarInsectSpeciesFilter.Count == 0)
            {
                return true;
            }

            return staticId > 0 && this.radarInsectSpeciesFilter.Contains(staticId);
        }

        private bool IsInsectNetIdAllowed(uint netId)
        {
            if (this.radarInsectSpeciesFilter.Count == 0)
            {
                return true;
            }

            if (netId == 0U)
            {
                return false;
            }

            int itemId;
            this.TryResolveInsectFilterItemId(netId, out itemId);
            return this.IsInsectSpeciesAllowed(itemId);
        }

        // The filter stores bag item ids (InsectSuffixToItemId). EntityUtil.GetEntityResId(uint)
        // is the overload that reads garbage and returns 0, so a non-empty filter rejected every
        // insect. The entity-object overload is the one MapSpots already uses.
        private bool TryResolveInsectFilterItemId(uint netId, out int itemId)
        {
            itemId = 0;
            float now = Time.unscaledTime;
            int epoch = AuraMonoWorldEpoch;
            if (epoch != this.insectFilterItemIdCacheEpoch)
            {
                this.insectFilterItemIdCache.Clear();
                this.insectFilterItemIdCacheEpoch = epoch;
            }

            InsectFilterItemIdCache cached;
            if (this.insectFilterItemIdCache.TryGetValue(netId, out cached) && now < cached.ExpiresAt)
            {
                itemId = cached.ItemId;
                return itemId > 0;
            }

            this.EnsureEntityResIdMethods();
            IntPtr entityObj;
            if (!this.TryGetAuraMonoEntityObjectByNetId(netId, out entityObj) || entityObj == IntPtr.Zero)
            {
                this.RememberInsectFilterItemId(netId, 0, now + InsectFilterItemIdMissSeconds);
                return false;
            }

            uint pin = AuraMonoPinNew(entityObj);
            if (pin == 0U)
            {
                this.RememberInsectFilterItemId(netId, 0, now + InsectFilterItemIdMissSeconds);
                return false;
            }

            try
            {
                this.TryGetCollectableStaticIdViaAura(entityObj, out itemId);
            }
            finally
            {
                AuraMonoPinFree(pin);
            }

            float ttl = itemId > 0 ? InsectFilterItemIdCacheSeconds : InsectFilterItemIdMissSeconds;
            this.RememberInsectFilterItemId(netId, itemId, now + ttl);
            return itemId > 0;
        }

        private void RememberInsectFilterItemId(uint netId, int itemId, float expiresAt)
        {
            if (this.insectFilterItemIdCache.Count > 96)
            {
                List<uint> expired = null;
                float now = Time.unscaledTime;
                foreach (KeyValuePair<uint, InsectFilterItemIdCache> pair in this.insectFilterItemIdCache)
                {
                    if (now < pair.Value.ExpiresAt)
                    {
                        continue;
                    }

                    if (expired == null)
                    {
                        expired = new List<uint>();
                    }

                    expired.Add(pair.Key);
                }

                if (expired != null)
                {
                    for (int i = 0; i < expired.Count; i++)
                    {
                        this.insectFilterItemIdCache.Remove(expired[i]);
                    }
                }
            }

            InsectFilterItemIdCache entry = new InsectFilterItemIdCache();
            entry.ItemId = itemId;
            entry.ExpiresAt = expiresAt;
            this.insectFilterItemIdCache[netId] = entry;
        }

        private void FilterLoadedInsectTargets(List<uint> ids, List<Vector3> positions)
        {
            if (ids == null || this.radarInsectSpeciesFilter.Count == 0)
            {
                return;
            }

            for (int i = ids.Count - 1; i >= 0; i--)
            {
                if (this.IsInsectNetIdAllowed(ids[i]))
                {
                    continue;
                }

                ids.RemoveAt(i);
                if (positions != null && i < positions.Count)
                {
                    positions.RemoveAt(i);
                }
            }
        }

        private bool IsBirdSpeciesAllowed(int staticId)
        {
            if (this.radarBirdSpeciesFilter.Count == 0)
            {
                return true;
            }

            return staticId > 0 && this.radarBirdSpeciesFilter.Contains(staticId);
        }

        private bool IsRadarInsectObjectAllowed(GameObject target)
        {
            if (this.radarInsectSpeciesFilter.Count == 0)
            {
                return true;
            }

            if (target == null)
            {
                return false;
            }

            int suffix = GetTrailingNumber(target.name);
            int itemId;
            return suffix > 0
                && InsectSuffixToItemId.TryGetValue(suffix, out itemId)
                && this.radarInsectSpeciesFilter.Contains(itemId);
        }

        private bool IsRadarBirdObjectAllowed(GameObject target)
        {
            if (this.radarBirdSpeciesFilter.Count == 0)
            {
                return true;
            }

            int itemId;
            return target != null
                && TryGetBirdItemIdFromPrefabName(target.name, out itemId)
                && this.radarBirdSpeciesFilter.Contains(itemId);
        }

        private void DropRadarMarkersRejectedBySpeciesFilter(bool insects)
        {
            if (this.trackedObjectMarkers.Count == 0)
            {
                return;
            }

            string label = insects ? "Insect" : "Bird";
            List<GameObject> doomed = null;
            foreach (KeyValuePair<int, GameObject> entry in this.trackedObjectMarkers)
            {
                GameObject marker = entry.Value;
                if (marker == null)
                {
                    continue;
                }

                if (!string.Equals(this.GetMarkerCanonicalLabel(marker), label, StringComparison.Ordinal))
                {
                    continue;
                }

                GameObject target;
                bool allowed = this.TryGetRadarMarkerTrackedTarget(marker, out target)
                    && (insects
                        ? this.IsRadarInsectObjectAllowed(target)
                        : this.IsRadarBirdObjectAllowed(target));
                if (allowed)
                {
                    continue;
                }

                if (doomed == null)
                {
                    doomed = new List<GameObject>();
                }

                doomed.Add(marker);
            }

            if (doomed == null)
            {
                return;
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                GameObject marker = doomed[i];
                this.RemoveTrackedMarkerMapping(marker);
                if (marker != null)
                {
                    UnityEngine.Object.Destroy(marker);
                }
            }
        }

        private static void EnsureRadarSpeciesIconKeys()
        {
            if (radarInsectItemIconKeys != null)
            {
                return;
            }

            radarInsectItemIconKeys = new Dictionary<int, string>(InsectSuffixToItemId.Count);
            foreach (KeyValuePair<int, int> pair in InsectSuffixToItemId)
            {
                if (!radarInsectItemIconKeys.ContainsKey(pair.Value))
                {
                    radarInsectItemIconKeys.Add(pair.Value, "p_insect_insect" + pair.Key.ToString());
                }
            }

            radarBirdItemIconKeys = new Dictionary<int, string>(BirdSuffixToItemId.Count + BirdPrefabTailToItemId.Count);
            foreach (KeyValuePair<int, int> pair in BirdSuffixToItemId)
            {
                if (!radarBirdItemIconKeys.ContainsKey(pair.Value))
                {
                    radarBirdItemIconKeys.Add(pair.Value, "p_bird_bird" + pair.Key.ToString());
                }
            }

            foreach (KeyValuePair<string, int> pair in BirdPrefabTailToItemId)
            {
                radarBirdItemIconKeys[pair.Value] = "p_bird_bird" + pair.Key;
            }
        }

        private void EnsureRadarSpeciesCatalog()
        {
            EnsureRadarSpeciesIconKeys();
            string language = this.selectedLanguage ?? string.Empty;
            if (this.radarInsectCatalog != null
                && string.Equals(language, this.radarSpeciesCatalogLanguage, StringComparison.Ordinal))
            {
                return;
            }

            this.radarSpeciesCatalogLanguage = language;
            this.radarInsectCatalog = this.BuildRadarSpeciesCatalog(radarInsectItemIconKeys, "Insect");
            this.radarBirdCatalog = this.BuildRadarSpeciesCatalog(radarBirdItemIconKeys, "Bird");
            this.radarSpeciesCatalogRebuilt = true;
            this.radarSpeciesNamesPending = true;
            this.radarSpeciesNameMisses = 0;
            this.radarSpeciesNameRetryAt = 0f;
        }

        private List<RadarSpeciesFilterEntry> BuildRadarSpeciesCatalog(Dictionary<int, string> icons, string canonical)
        {
            List<RadarSpeciesFilterEntry> list = new List<RadarSpeciesFilterEntry>(icons.Count);
            foreach (KeyValuePair<int, string> pair in icons)
            {
                RadarSpeciesFilterEntry entry = new RadarSpeciesFilterEntry();
                entry.Id = pair.Key;
                entry.IconKey = pair.Value;
                entry.Canonical = canonical;
                entry.Name = pair.Key.ToString();
                entry.NameFold = entry.Name;
                entry.NameUnresolved = true;
                list.Add(entry);
            }

            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        private static void SortRadarSpeciesCatalog(List<RadarSpeciesFilterEntry> list)
        {
            if (list == null)
            {
                return;
            }

            list.Sort((a, b) =>
            {
                int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : a.Id.CompareTo(b.Id);
            });
        }

        private bool RefreshRadarSpeciesNames()
        {
            this.EnsureProduceMethod();
            int budget = 24;
            bool changed = this.ResolveRadarSpeciesNames(this.radarInsectCatalog, ref budget)
                | this.ResolveRadarSpeciesNames(this.radarBirdCatalog, ref budget);
            if (changed)
            {
                this.radarSpeciesNameMisses = 0;
            }
            else if (budget < 24)
            {
                this.radarSpeciesNameMisses++;
            }

            bool unresolved = this.CatalogHasUnresolved(this.radarInsectCatalog)
                || this.CatalogHasUnresolved(this.radarBirdCatalog);
            if (!unresolved || this.radarSpeciesNameMisses >= 8)
            {
                this.radarSpeciesNamesPending = false;
            }

            return changed;
        }

        private bool ResolveRadarSpeciesNames(List<RadarSpeciesFilterEntry> list, ref int budget)
        {
            if (list == null || budget <= 0)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < list.Count && budget > 0; i++)
            {
                RadarSpeciesFilterEntry entry = list[i];
                if (entry == null || !entry.NameUnresolved)
                {
                    continue;
                }

                budget--;
                string name;
                if (!this.TryGetItemName(entry.Id, out name) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                entry.Name = name.Trim();
                entry.NameUnresolved = false;
                entry.NameFold = entry.Name.ToLowerInvariant() + " " + entry.Id.ToString();
                changed = true;
            }

            return changed;
        }

        private bool CatalogHasUnresolved(List<RadarSpeciesFilterEntry> list)
        {
            if (list == null)
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].NameUnresolved)
                {
                    return true;
                }
            }

            return false;
        }

        // ----------------------------------------------------------------------------------------
        // Filter sub-tab
        // ----------------------------------------------------------------------------------------

        private const int UguiRadarFilterColumns = 5;
        private const float UguiRadarFilterCellH = 86f;

        private sealed class UguiRadarFilterCell
        {
            public Image Bg;
            public Image Ring;
            public Image Icon;
            public GameObject Initials;
            public GameObject Name;
            public int SigId = int.MinValue;
            public bool SigSelected;
            public string SigName;
            public bool HasIcon;
        }

        private sealed class UguiRadarFilterHandle
        {
            public GameObject Root;
            public bool ShowingInsects = true;
            public bool WasActive;
            public string Search = string.Empty;
            public InputField SearchField;
            public UguiRadarSegmentHandle InsectSegment;
            public UguiRadarSegmentHandle BirdSegment;
            public GameObject SummaryLabel;
            public string SummaryShown;
            public GameObject ClearButton;
            public GameObject QuickUnopenedButton;
            public GameObject QuickBelow5Button;
            public GameObject QuickNotMasterButton;
            public bool QuickUnopenedShown;
            public bool QuickBelow5Shown;
            public bool QuickNotMasterShown;
            public bool QuickLabelsForInsects = true;
            public GameObject EmptyLabel;
            public UguiVirtualGridHandle Grid;
            public readonly List<UguiRadarFilterCell> Cells = new List<UguiRadarFilterCell>();
            public readonly List<RadarSpeciesFilterEntry> Visible = new List<RadarSpeciesFilterEntry>();
            public Color CellNormalFill;
            public Color CellActiveFill;
            public Color RingNormal;
            public Color RingSelected;
            public Color TextColor;
            public float NextIconRetryAt;
            public int ErrorCount;
        }

        private UguiRadarFilterHandle uguiRadarFilter;

        private GameObject BuildUguiShellRadarFilterContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiRadarFilter = null;

            UguiRadarFilterHandle handle = new UguiRadarFilterHandle();
            GameObject block = this.CreateUguiGo("RadarFilterContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            Color accent = this.UguiKitAccent();
            handle.CellNormalFill = this.UguiKitContentBg();
            handle.CellActiveFill = new Color(accent.r, accent.g, accent.b, 0.18f);
            handle.RingNormal = new Color(1f, 1f, 1f, Mathf.Clamp(0.05f + (this.uiPanelAlpha * 0.05f), 0.05f, 0.10f));
            handle.RingSelected = new Color(accent.r, accent.g, accent.b, 0.8f);
            handle.TextColor = this.UguiKitTextColor();

            float segW = (w - 24f) * 0.5f;
            handle.InsectSegment = this.CreateUguiRadarSegmentButton(block.transform, "Insects",
                () => this.OnUguiRadarFilterCategoryClicked(true));
            PlaceUguiTopLeft(handle.InsectSegment.Root, 8f, 8f, segW, 32f);
            handle.BirdSegment = this.CreateUguiRadarSegmentButton(block.transform, "Birds",
                () => this.OnUguiRadarFilterCategoryClicked(false));
            PlaceUguiTopLeft(handle.BirdSegment.Root, 16f + segW, 8f, segW, 32f);

            InputField search = this.CreateUguiInputField(block.transform, "Search", string.Empty, 48,
                this.OnUguiRadarFilterSearchChanged);
            PlaceUguiTopLeft(search.gameObject, 8f, 48f, w - 16f, 28f);
            handle.SearchField = search;
            GameObject placeholderGo = this.CreateUguiGo("Placeholder", search.transform);
            StretchUguiFill(placeholderGo, 8f, 3f, 8f, 3f);
            Text placeholder = this.CreateUguiLegacyText(placeholderGo, this.L("Search"), 13,
                this.UguiKitMutedColor(), TextAnchor.MiddleLeft);
            placeholder.raycastTarget = false;
            search.placeholder = placeholder;

            float quickGap = 6f;
            float quickBtnW = (w - 16f - (quickGap * 2f)) / 3f;
            handle.QuickUnopenedButton = this.CreateUguiSecondaryButton(block.transform, "QuickUnopened",
                this.L("Unopened"), () => this.OnUguiRadarFilterQuickClicked(0));
            PlaceUguiTopLeft(handle.QuickUnopenedButton, 8f, 84f, quickBtnW, 28f);
            handle.QuickBelow5Button = this.CreateUguiSecondaryButton(block.transform, "QuickBelow5",
                this.L("Under 5 Stars"), () => this.OnUguiRadarFilterQuickClicked(1));
            PlaceUguiTopLeft(handle.QuickBelow5Button, 8f + quickBtnW + quickGap, 84f, quickBtnW, 28f);
            handle.QuickNotMasterButton = this.CreateUguiSecondaryButton(block.transform, "QuickNotMaster",
                this.L("Not Catching Master"), () => this.OnUguiRadarFilterQuickClicked(2));
            PlaceUguiTopLeft(handle.QuickNotMasterButton, 8f + (2f * (quickBtnW + quickGap)), 84f, quickBtnW, 28f);

            handle.SummaryShown = this.L("All species");
            handle.SummaryLabel = this.CreateUguiLabel(block.transform, "Summary", handle.SummaryShown, 12f,
                this.UguiKitMutedColor(), false);
            PlaceUguiTopLeft(handle.SummaryLabel, 8f, 124f, w - 116f, 22f);
            handle.ClearButton = this.CreateUguiDangerButton(block.transform, "Clear", this.L("Clear"),
                this.OnUguiRadarFilterClearClicked);
            PlaceUguiTopLeft(handle.ClearButton, w - 96f, 116f, 88f, 28f);

            float gridY = 156f;
            float gridH = Mathf.Max(80f, h - gridY - 8f);
            handle.Grid = this.CreateUguiVirtualGrid(block.transform, "SpeciesGrid", w - 16f,
                UguiRadarFilterColumns, UguiRadarFilterCellH, gridH);
            PlaceUguiTopLeft(handle.Grid.Root, 8f, gridY, w - 16f, gridH);
            for (int k = 0; k < handle.Grid.Slots.Count; k++)
            {
                handle.Cells.Add(this.BuildUguiRadarFilterCell(handle, handle.Grid.Slots[k], k));
            }

            handle.EmptyLabel = this.CreateUguiLabel(block.transform, "Empty", this.L("No matching species"),
                13f, this.UguiKitMutedColor(), true);
            PlaceUguiTopLeft(handle.EmptyLabel, 8f, gridY + 12f, w - 16f, 24f);
            handle.EmptyLabel.SetActive(false);

            this.EnsureRadarSpeciesCatalog();
            this.radarSpeciesCatalogRebuilt = false;
            this.RebuildUguiRadarFilterVisible(handle, true);
            this.SyncUguiRadarFilterChrome(handle);
            this.SyncUguiRadarFilterGrid(handle);

            handle.Root = block;
            this.uguiRadarFilter = handle;
            this.EnsureRadarCollectionFilterHooks();
            return block;
        }

        private void OnUguiRadarFilterQuickClicked(int which)
        {
            UguiRadarFilterHandle handle = this.uguiRadarFilter;
            if (handle == null)
            {
                return;
            }

            if (this.radarCollectionFilterDirty || !this.radarCollectionFilterReady)
            {
                this.radarCollectionFilterDirty = false;
                if (!this.TryRebuildRadarCollectionQuickSets())
                {
                    return;
                }
            }

            bool insects = handle.ShowingInsects;
            HashSet<int> ids;
            if (which == 0)
            {
                ids = insects ? this.radarInsectQuickUnopened : this.radarBirdQuickUnopened;
            }
            else if (which == 1)
            {
                ids = insects ? this.radarInsectQuickBelow5 : this.radarBirdQuickBelow5;
            }
            else
            {
                ids = insects ? this.radarInsectQuickNotMaster : this.radarBirdQuickNotMaster;
            }

            this.ApplyRadarSpeciesFilter(insects, ids);
        }

        private void SetUguiRadarFilterQuickLabel(GameObject button, string text)
        {
            if (button == null)
            {
                return;
            }

            Transform label = button.transform.Find("Label");
            if (label == null)
            {
                return;
            }

            this.SetUguiLabelText(label.gameObject, text);
        }

        private void ApplyUguiRadarFilterQuickButton(GameObject button, ref bool shown, bool selected)
        {
            if (button == null || shown == selected)
            {
                return;
            }

            shown = selected;
            Color accent = this.UguiKitAccent();
            Color controlFill = this.UguiKitControlFill();
            Image bg = button.GetComponent<Image>();
            if (bg != null)
            {
                bg.color = selected
                    ? new Color(accent.r, accent.g, accent.b, 0.18f)
                    : new Color(controlFill.r, controlFill.g, controlFill.b, Mathf.Clamp(this.uiPanelAlpha, 0.25f, 1f));
            }

            Transform ringT = button.transform.Find("Ring");
            Image ring = ringT != null ? ringT.GetComponent<Image>() : null;
            if (ring != null)
            {
                ring.color = selected
                    ? new Color(accent.r, accent.g, accent.b, 0.35f)
                    : new Color(1f, 1f, 1f, 0.08f);
            }
        }

        private UguiRadarFilterCell BuildUguiRadarFilterCell(UguiRadarFilterHandle handle, UguiVirtualGridSlot slot, int slotIndex)
        {
            UguiRadarFilterCell cell = new UguiRadarFilterCell();
            GameObject root = slot.Root;
            float cellW = handle.Grid.CellW - 8f;

            cell.Bg = this.AddUguiImage(root, handle.CellNormalFill, true, 1.5f);
            cell.Bg.raycastTarget = true;
            Button button = root.AddComponent<Button>();
            button.targetGraphic = cell.Bg;
            button.transition = Selectable.Transition.None;
            int slotCopy = slotIndex;
            this.WireUguiClick(button.onClick, () => this.OnUguiRadarFilterCellClicked(slotCopy));

            this.AddUguiRingOverlay(root, handle.RingNormal, 1.5f);
            Transform ringT = root.transform.Find("Ring");
            cell.Ring = (ringT != null) ? ringT.GetComponent<Image>() : null;

            GameObject iconGo = this.CreateUguiGo("Icon", root.transform);
            PlaceUguiTopLeft(iconGo, (cellW - 40f) * 0.5f, 4f, 40f, 40f);
            cell.Icon = iconGo.AddComponent<Image>();
            cell.Icon.raycastTarget = false;
            try { cell.Icon.preserveAspect = true; } catch { }
            iconGo.SetActive(false);

            cell.Initials = this.CreateUguiLabel(root.transform, "Initials", "", 13f, handle.TextColor, true);
            this.TrySetUguiLabelBold(cell.Initials);
            PlaceUguiTopLeft(cell.Initials, (cellW - 40f) * 0.5f, 4f, 40f, 40f);
            cell.Initials.SetActive(false);

            cell.Name = this.CreateUguiLabel(root.transform, "Name", "", 10f, handle.TextColor, true);
            this.TrySetUguiTransferNameWrapped(cell.Name);
            PlaceUguiTopLeft(cell.Name, 4f, 46f, cellW - 8f, 32f);
            return cell;
        }

        private void OnUguiRadarFilterCategoryClicked(bool insects)
        {
            UguiRadarFilterHandle handle = this.uguiRadarFilter;
            if (handle == null || handle.ShowingInsects == insects)
            {
                return;
            }

            handle.ShowingInsects = insects;
            this.RebuildUguiRadarFilterVisible(handle, true);
            this.SyncUguiRadarFilterChrome(handle);
            this.SyncUguiRadarFilterGrid(handle);
        }

        private void OnUguiRadarFilterSearchChanged(string text)
        {
            UguiRadarFilterHandle handle = this.uguiRadarFilter;
            if (handle == null)
            {
                return;
            }

            string next = text ?? string.Empty;
            if (string.Equals(next, handle.Search, StringComparison.Ordinal))
            {
                return;
            }

            handle.Search = next;
            this.RebuildUguiRadarFilterVisible(handle, false);
            this.SyncUguiRadarFilterChrome(handle);
            this.SyncUguiRadarFilterGrid(handle);
        }

        private void OnUguiRadarFilterClearClicked()
        {
            UguiRadarFilterHandle handle = this.uguiRadarFilter;
            if (handle == null)
            {
                return;
            }

            this.ClearRadarSpeciesFilter(handle.ShowingInsects);
        }

        private void OnUguiRadarFilterCellClicked(int slotIndex)
        {
            UguiRadarFilterHandle handle = this.uguiRadarFilter;
            if (handle == null || handle.Grid == null || slotIndex < 0 || slotIndex >= handle.Grid.Slots.Count)
            {
                return;
            }

            int index = handle.Grid.Slots[slotIndex].BoundIndex;
            if (index < 0 || index >= handle.Visible.Count)
            {
                return;
            }

            RadarSpeciesFilterEntry entry = handle.Visible[index];
            if (entry == null)
            {
                return;
            }

            this.ToggleRadarSpeciesFilter(handle.ShowingInsects, entry.Id);
        }

        private void RebuildUguiRadarFilterVisible(UguiRadarFilterHandle handle, bool resetScroll)
        {
            if (handle == null)
            {
                return;
            }

            this.EnsureRadarSpeciesCatalog();
            List<RadarSpeciesFilterEntry> source = handle.ShowingInsects ? this.radarInsectCatalog : this.radarBirdCatalog;
            HashSet<int> selected = handle.ShowingInsects ? this.radarInsectSpeciesFilter : this.radarBirdSpeciesFilter;
            string query = (handle.Search ?? string.Empty).Trim().ToLowerInvariant();
            handle.Visible.Clear();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    RadarSpeciesFilterEntry entry = source[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    if (query.Length > 0 && entry.NameFold.IndexOf(query, StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    handle.Visible.Add(entry);
                }
            }

            handle.Visible.Sort((a, b) =>
            {
                int aSel = selected.Contains(a.Id) ? 0 : 1;
                int bSel = selected.Contains(b.Id) ? 0 : 1;
                if (aSel != bSel)
                {
                    return aSel.CompareTo(bSel);
                }

                int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : a.Id.CompareTo(b.Id);
            });

            if (resetScroll && handle.Grid != null && handle.Grid.ContentRt != null)
            {
                Vector2 pos = handle.Grid.ContentRt.anchoredPosition;
                pos.y = 0f;
                handle.Grid.ContentRt.anchoredPosition = pos;
            }
        }

        private void SyncUguiRadarFilterChrome(UguiRadarFilterHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            int insectCount = this.CountDisplayedRadarSpeciesFilter(this.radarInsectSpeciesFilter);
            int birdCount = this.CountDisplayedRadarSpeciesFilter(this.radarBirdSpeciesFilter);
            bool insectRestricted = this.radarInsectSpeciesFilter.Count > 0;
            bool birdRestricted = this.radarBirdSpeciesFilter.Count > 0;
            string insectLabel = insectRestricted
                ? this.L("Insects") + "  " + insectCount.ToString()
                : this.L("Insects");
            string birdLabel = birdRestricted
                ? this.L("Birds") + "  " + birdCount.ToString()
                : this.L("Birds");
            this.ApplyUguiRadarFilterSegment(handle.InsectSegment, insectLabel, handle.ShowingInsects);
            this.ApplyUguiRadarFilterSegment(handle.BirdSegment, birdLabel, !handle.ShowingInsects);

            bool currentRestricted = handle.ShowingInsects ? insectRestricted : birdRestricted;
            int current = handle.ShowingInsects ? insectCount : birdCount;
            string summary = currentRestricted ? this.LF("{0} selected", current) : this.L("All species");
            if (!string.Equals(summary, handle.SummaryShown, StringComparison.Ordinal))
            {
                handle.SummaryShown = summary;
                this.SetUguiLabelText(handle.SummaryLabel, summary);
            }

            this.SetUguiButtonInteractable(handle.ClearButton, currentRestricted);
            if (handle.QuickLabelsForInsects != handle.ShowingInsects)
            {
                handle.QuickLabelsForInsects = handle.ShowingInsects;
                string third = handle.ShowingInsects
                    ? this.L("Not Catching Master")
                    : this.L("Not Photo Master");
                this.SetUguiRadarFilterQuickLabel(handle.QuickNotMasterButton, third);
            }

            HashSet<int> currentSet = handle.ShowingInsects ? this.radarInsectSpeciesFilter : this.radarBirdSpeciesFilter;
            HashSet<int> unopened = handle.ShowingInsects ? this.radarInsectQuickUnopened : this.radarBirdQuickUnopened;
            HashSet<int> below5 = handle.ShowingInsects ? this.radarInsectQuickBelow5 : this.radarBirdQuickBelow5;
            HashSet<int> notMaster = handle.ShowingInsects ? this.radarInsectQuickNotMaster : this.radarBirdQuickNotMaster;
            this.ApplyUguiRadarFilterQuickButton(handle.QuickUnopenedButton,
                ref handle.QuickUnopenedShown, this.radarCollectionFilterReady && RadarSpeciesFilterEquals(currentSet, unopened));
            this.ApplyUguiRadarFilterQuickButton(handle.QuickBelow5Button,
                ref handle.QuickBelow5Shown, this.radarCollectionFilterReady && RadarSpeciesFilterEquals(currentSet, below5));
            this.ApplyUguiRadarFilterQuickButton(handle.QuickNotMasterButton,
                ref handle.QuickNotMasterShown, this.radarCollectionFilterReady && RadarSpeciesFilterEquals(currentSet, notMaster));
            bool empty = handle.Visible.Count == 0;
            SetUguiGoActive(handle.EmptyLabel, empty);
            SetUguiGoActive(handle.Grid != null ? handle.Grid.Root : null, !empty);
        }

        private void ApplyUguiRadarFilterSegment(UguiRadarSegmentHandle seg, string label, bool selected)
        {
            if (seg == null)
            {
                return;
            }

            int selectedBit = selected ? 1 : 0;
            if (seg.AppliedSelected == selectedBit && string.Equals(label, seg.AppliedLabel, StringComparison.Ordinal))
            {
                return;
            }

            seg.AppliedSelected = selectedBit;
            seg.AppliedLabel = label;

            Color accent = this.UguiKitAccent();
            Color controlFill = this.UguiKitControlFill();
            Color text = this.UguiKitTextColor();
            if (seg.Bg != null)
            {
                seg.Bg.color = selected
                    ? new Color(accent.r, accent.g, accent.b, 0.18f)
                    : new Color(controlFill.r, controlFill.g, controlFill.b, Mathf.Clamp(this.uiPanelAlpha, 0.25f, 1f));
            }

            if (seg.Ring != null)
            {
                seg.Ring.color = selected
                    ? new Color(accent.r, accent.g, accent.b, 0.35f)
                    : new Color(1f, 1f, 1f, 0.08f);
            }

            this.SetUguiLabelText(seg.Label, label);
            this.SetUguiLabelColor(seg.Label, selected
                ? new Color(0.98f, 0.99f, 1f, 1f)
                : new Color(text.r, text.g, text.b, 0.92f));
        }

        private void SyncUguiRadarFilterGrid(UguiRadarFilterHandle handle)
        {
            if (handle == null || handle.Grid == null || handle.Grid.Root == null || !handle.Grid.Root.activeSelf)
            {
                return;
            }

            this.UpdateUguiVirtualGridAssignments(handle.Grid, handle.Visible.Count);
            HashSet<int> selected = handle.ShowingInsects ? this.radarInsectSpeciesFilter : this.radarBirdSpeciesFilter;
            for (int k = 0; k < handle.Grid.Slots.Count && k < handle.Cells.Count; k++)
            {
                UguiVirtualGridSlot slot = handle.Grid.Slots[k];
                UguiRadarFilterCell cell = handle.Cells[k];
                int index = slot.BoundIndex;
                if (index < 0 || index >= handle.Visible.Count)
                {
                    cell.SigId = int.MinValue;
                    continue;
                }

                RadarSpeciesFilterEntry entry = handle.Visible[index];
                if (entry == null)
                {
                    cell.SigId = int.MinValue;
                    continue;
                }

                bool isSelected = selected.Contains(entry.Id);
                if (cell.SigId == entry.Id && cell.SigSelected == isSelected
                    && string.Equals(cell.SigName, entry.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                this.BindUguiRadarFilterCell(handle, cell, entry, isSelected);
            }
        }

        private void BindUguiRadarFilterCell(UguiRadarFilterHandle handle, UguiRadarFilterCell cell,
            RadarSpeciesFilterEntry entry, bool isSelected)
        {
            if (cell.Bg != null)
            {
                cell.Bg.color = isSelected ? handle.CellActiveFill : handle.CellNormalFill;
            }

            if (cell.Ring != null)
            {
                cell.Ring.color = isSelected ? handle.RingSelected : handle.RingNormal;
            }

            Texture2D tex = null;
            if (!string.IsNullOrEmpty(entry.IconKey))
            {
                this.TryGetRadarFilterIconTexture(entry.IconKey, out tex);
            }

            Sprite sprite = (tex != null) ? this.GetOrCreateUguiTransferSprite(tex) : null;
            bool hasIcon = sprite != null;
            cell.HasIcon = hasIcon;
            if (cell.Icon != null)
            {
                if (hasIcon)
                {
                    cell.Icon.sprite = sprite;
                }

                SetUguiGoActive(cell.Icon.gameObject, hasIcon);
            }

            SetUguiGoActive(cell.Initials, !hasIcon);
            if (!hasIcon)
            {
                this.SetUguiLabelText(cell.Initials, this.GetAutoSellItemInitials(entry.Name));
            }

            this.SetUguiLabelText(cell.Name, entry.Name);
            cell.SigId = entry.Id;
            cell.SigSelected = isSelected;
            cell.SigName = entry.Name;
        }

        // Portraits come from the async item loader and the dictionaries it already fills.
        // The radar fallback walks every loaded sprite; doing that once per cell is what made
        // the first open hitch, so this grid never calls it.
        private bool TryGetRadarFilterIconTexture(string iconKey, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrWhiteSpace(iconKey))
            {
                return false;
            }

            string normalizedKey = this.NormalizeRadarIconSpriteKey(iconKey);
            if (string.IsNullOrEmpty(normalizedKey))
            {
                return false;
            }

            if (this.radarIconEspTextures.TryGetValue(normalizedKey, out texture) && texture != null)
            {
                return true;
            }

            if (this.autoSellBagItemTextures.TryGetValue(normalizedKey, out texture) && texture != null)
            {
                this.radarIconEspTextures[normalizedKey] = texture;
                return true;
            }

            this.RequestGameItemIconByIconName(normalizedKey, normalizedKey);
            texture = null;
            return false;
        }

        private void RetryUguiRadarFilterIcons(UguiRadarFilterHandle handle)
        {
            if (handle.Grid == null || handle.Visible.Count == 0)
            {
                return;
            }

            for (int k = 0; k < handle.Grid.Slots.Count && k < handle.Cells.Count; k++)
            {
                UguiRadarFilterCell cell = handle.Cells[k];
                int index = handle.Grid.Slots[k].BoundIndex;
                if (cell.HasIcon || cell.SigId == int.MinValue || index < 0 || index >= handle.Visible.Count)
                {
                    continue;
                }

                RadarSpeciesFilterEntry entry = handle.Visible[index];
                Texture2D tex;
                if (entry != null
                    && this.TryGetRadarFilterIconTexture(entry.IconKey, out tex)
                    && tex != null)
                {
                    cell.SigId = int.MinValue;
                }
            }
        }

        private void ProcessUguiShellRadarFilterOnUpdate()
        {
            UguiRadarFilterHandle handle = this.uguiRadarFilter;
            if (handle == null || handle.ErrorCount >= 3)
            {
                return;
            }

            try
            {
                if (!this.IsUguiShellRadarSubTabActive(UguiShellRadarFilterSubIndex))
                {
                    handle.WasActive = false;
                    return;
                }

                if (!handle.WasActive)
                {
                    handle.WasActive = true;
                    this.radarSpeciesNamesPending = true;
                    this.radarSpeciesNameMisses = 0;
                    this.radarCollectionFilterDirty = true;
                }

                this.EnsureRadarSpeciesCatalog();
                if (this.radarSpeciesCatalogRebuilt)
                {
                    this.radarSpeciesCatalogRebuilt = false;
                    this.RebuildUguiRadarFilterVisible(handle, false);
                }

                float now = Time.unscaledTime;
                if (this.radarSpeciesNamesPending && now >= this.radarSpeciesNameRetryAt)
                {
                    this.radarSpeciesNameRetryAt = now + 0.5f;
                    bool hadUnresolved = this.CatalogHasUnresolved(this.radarInsectCatalog)
                        || this.CatalogHasUnresolved(this.radarBirdCatalog);
                    this.RefreshRadarSpeciesNames();
                    if (hadUnresolved && !this.radarSpeciesNamesPending)
                    {
                        SortRadarSpeciesCatalog(this.radarInsectCatalog);
                        SortRadarSpeciesCatalog(this.radarBirdCatalog);
                        this.RebuildUguiRadarFilterVisible(handle, false);
                    }
                }

                if (this.radarCollectionFilterDirty)
                {
                    this.radarCollectionFilterDirty = false;
                    this.TryRebuildRadarCollectionQuickSets();
                }

                if (handle.SearchField != null)
                {
                    string typed = handle.SearchField.text ?? string.Empty;
                    if (!string.Equals(typed, handle.Search, StringComparison.Ordinal))
                    {
                        handle.Search = typed;
                        this.RebuildUguiRadarFilterVisible(handle, false);
                    }
                }

                this.SyncUguiRadarFilterChrome(handle);
                this.SyncUguiRadarFilterGrid(handle);
                if (now >= handle.NextIconRetryAt)
                {
                    handle.NextIconRetryAt = now + 0.5f;
                    this.RetryUguiRadarFilterIcons(handle);
                }

                handle.ErrorCount = 0;
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                if (handle.ErrorCount == 1 || handle.ErrorCount == 3)
                {
                    ModLogger.Msg("[RadarFilter] ui sync failed: " + ex.Message);
                }
            }
        }
    }
}
