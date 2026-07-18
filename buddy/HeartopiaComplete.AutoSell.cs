using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using UnityObject = UnityEngine.Object;
using Il2CppType = Il2CppSystem.Type;
using Il2CppFieldInfo = Il2CppSystem.Reflection.FieldInfo;
using Il2CppMethodInfo = Il2CppSystem.Reflection.MethodInfo;
using Il2CppPropertyInfo = Il2CppSystem.Reflection.PropertyInfo;
using Il2CppBindingFlags = Il2CppSystem.Reflection.BindingFlags;
using Il2CppObject = Il2CppSystem.Object;
using Object = UnityEngine.Object;


namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private void ClearDirectBackpackRuntimeItems()
        {
            for (int i = 0; i < this.directBackpackRuntimeItems.Count; i++)
            {
                AuraMonoPinFree(this.directBackpackRuntimeItems[i].MonoItemPin);
            }
            this.directBackpackRuntimeItems.Clear();
        }

        private void DirectBackpackVerboseLog(string message)
        {
            if (DirectBackpackVerboseLogsEnabled)
            {
                this.AutoEatRepairLog(message);
            }
        }

        private float DrawAutoSellTab(int startY)
        {
            int num = startY;
            float left = 20f;
            float panelWidth = 580f;
            float colWidth = 272f;

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 15;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = new Color(this.uiHeaderR, this.uiHeaderG, this.uiHeaderB);

            GUIStyle tinyStyle = new GUIStyle(GUI.skin.label);
            tinyStyle.fontSize = 11;
            tinyStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB);

            GUIStyle fieldLabelStyle = new GUIStyle(GUI.skin.label);
            fieldLabelStyle.fontSize = 12;
            fieldLabelStyle.fontStyle = FontStyle.Bold;
            fieldLabelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            GUIStyle dropdownValueStyle = new GUIStyle(GUI.skin.label);
            dropdownValueStyle.fontSize = 13;
            dropdownValueStyle.fontStyle = FontStyle.Bold;
            dropdownValueStyle.alignment = TextAnchor.MiddleLeft;
            dropdownValueStyle.normal.textColor = Color.white;

            GUIStyle dropdownArrowStyle = new GUIStyle(GUI.skin.label);
            dropdownArrowStyle.fontSize = 12;
            dropdownArrowStyle.fontStyle = FontStyle.Bold;
            dropdownArrowStyle.alignment = TextAnchor.MiddleCenter;
            dropdownArrowStyle.normal.textColor = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);

            GUIStyle dropdownOptionStyle = new GUIStyle(GUI.skin.label);
            dropdownOptionStyle.fontSize = 13;
            dropdownOptionStyle.fontStyle = FontStyle.Bold;
            dropdownOptionStyle.alignment = TextAnchor.MiddleCenter;
            dropdownOptionStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            GUIStyle dropdownOptionActiveStyle = new GUIStyle(dropdownOptionStyle);
            dropdownOptionActiveStyle.normal.textColor = Color.white;

            AutoSellBagItemEntry selectedEntry = this.GetSelectedAutoSellBagItemEntry();

            GUI.Label(new Rect(left, (float)num, panelWidth, 24f), this.L("AUTO SELL"), titleStyle);
            num += 24;

            Rect selectedCard = new Rect(left, (float)num, panelWidth, 78f);
            GUI.Box(selectedCard, "", this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(selectedCard, 1f);
            Rect selectedIconRect = new Rect(selectedCard.x + 12f, selectedCard.y + 12f, 54f, 54f);
            GUI.Box(selectedIconRect, "", this.themeContentStyle ?? GUI.skin.box);
            if (selectedEntry != null && this.TryGetAutoSellItemTexture(selectedEntry, out Texture2D selectedTex) && selectedTex != null)
            {
                GUI.DrawTexture(new Rect(selectedIconRect.x + 5f, selectedIconRect.y + 5f, 44f, 44f), selectedTex, ScaleMode.ScaleToFit, true);
            }
            else
            {
                GUI.Label(selectedIconRect, "?", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold });
            }

            string selectedTitle = selectedEntry != null ? selectedEntry.DisplayName : "No item selected";
            string selectedKey = this.GetActiveAutoSellMatchKey();
            if (string.IsNullOrWhiteSpace(selectedKey)) selectedKey = "Choose from scanned items or type a key below";
            GUI.Label(new Rect(selectedCard.x + 78f, selectedCard.y + 10f, 300f, 22f), selectedTitle, fieldLabelStyle);
            GUI.Label(new Rect(selectedCard.x + 78f, selectedCard.y + 32f, 360f, 18f), (this.autoSellMatchFamily ? "Similar: " : "Exact: ") + selectedKey + (this.autoSellSelectedStar > 0 ? ("  " + this.autoSellSelectedStar + "*") : ""), tinyStyle);
            string selectedMeta = selectedEntry != null && selectedEntry.Count > 0
                ? ("Source: " + this.GetAutoSellStorageLabel(selectedEntry.FromBackpack, selectedEntry.FromWarehouse) + "  Count: " + selectedEntry.Count + (selectedEntry.StackCount > 1 ? (" in " + selectedEntry.StackCount + " stacks") : "") + this.GetAutoSellStarSummary(selectedEntry) + (selectedEntry.StaticId > 0 ? ("  staticId: " + selectedEntry.StaticId) : ""))
                : (this.autoSellLastMatchSummary ?? "No scan yet");
            GUI.Label(new Rect(selectedCard.x + 78f, selectedCard.y + 51f, 360f, 18f), selectedMeta, tinyStyle);

            bool prevEnabled = this.autoSellEnabled;
            this.autoSellEnabled = this.DrawSwitchToggle(new Rect(selectedCard.x + 420f, selectedCard.y + 22f, 140f, 26f), this.autoSellEnabled, "Auto");
            if (this.autoSellEnabled != prevEnabled)
            {
                this.nextAutoSellAt = 0f;
                if (this.autoSellEnabled)
                {
                    this.autoSellBackpackDirty = true; // force a scan on the first tick after enabling
                }
                this.autoSellStatus = this.autoSellEnabled ? "Enabled" : "Disabled";
                try { this.SaveKeybinds(false); } catch { }
                this.AddMenuNotification("Auto Sell " + (this.autoSellEnabled ? "Enabled" : "Disabled"), this.autoSellEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
            }
            num += 90;

            Rect settingsCard = new Rect(left, (float)num, panelWidth, 204f);
            GUI.Box(settingsCard, "", this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(settingsCard, 1f);

            GUI.Label(new Rect(settingsCard.x + 12f, settingsCard.y + 9f, colWidth, 20f), "Match Item", fieldLabelStyle);
            string newKey = GUI.TextField(new Rect(settingsCard.x + 12f, settingsCard.y + 31f, colWidth, 24f), this.autoSellItemKey ?? "", 80);
            if (newKey != this.autoSellItemKey)
            {
                this.autoSellItemKey = newKey ?? "";
                // Hand-typed key = plain text matching: no item identity, no star constraint.
                this.autoSellSelectedStaticId = 0;
                this.autoSellSelectedStar = 0;
                try { this.SaveKeybinds(false); } catch { }
            }

            GUI.Label(new Rect(settingsCard.x + 12f, settingsCard.y + 59f, colWidth, 18f), "Examples: birdphoto, p_birdphoto, food_badfood", tinyStyle);
            GUI.Label(new Rect(settingsCard.x + 12f, settingsCard.y + 83f, colWidth, 18f), this.LF("Interval: {0:F0}s", this.autoSellInterval), fieldLabelStyle);
            float prevInterval = this.autoSellInterval;
            this.autoSellInterval = Mathf.Clamp(this.UI_DrawAccentSlider(new Rect(settingsCard.x + 12f, settingsCard.y + 103f, colWidth, 16f), this.autoSellInterval, 1f, 120f), 1f, 120f);
            if (Math.Abs(this.autoSellInterval - prevInterval) > 0.001f) { try { this.SaveKeybinds(false); } catch { } }
            bool prevFestivalTokens = this.autoSellFestivalTokensEnabled;
            this.autoSellFestivalTokensEnabled = this.DrawSwitchToggle(new Rect(settingsCard.x + 12f, settingsCard.y + 130f, colWidth, 26f), this.autoSellFestivalTokensEnabled, "Festival For Tokens");
            if (this.autoSellFestivalTokensEnabled != prevFestivalTokens)
            {
                this.autoSellFestivalCurrencyNextProbeAt = 0f; // re-probe the currency right away
                try { this.SaveKeybinds(false); } catch { }
            }

            // No global star slider anymore: the star constraint travels with the clicked cell.
            string starInfo = this.autoSellSelectedStar > 0
                ? "Stars: " + this.autoSellSelectedStar + "* only (from selected item)"
                : "Stars: any (star comes from the clicked item)";
            GUI.Label(new Rect(settingsCard.x + 12f, settingsCard.y + 164f, colWidth, 18f), starInfo, tinyStyle);

            float toggleX = settingsCard.x + 304f;
            bool prevFullStack = this.autoSellFullStack;
            this.autoSellFullStack = this.DrawSwitchToggle(new Rect(toggleX, settingsCard.y + 12f, 250f, 24f), this.autoSellFullStack, "Sell Whole Stack");
            if (this.autoSellFullStack != prevFullStack) { try { this.SaveKeybinds(false); } catch { } }

            bool prevAll = this.autoSellAllMatchingStacks;
            this.autoSellAllMatchingStacks = this.DrawSwitchToggle(new Rect(toggleX, settingsCard.y + 42f, 250f, 24f), this.autoSellAllMatchingStacks, "Sell Every Match");
            if (this.autoSellAllMatchingStacks != prevAll) { try { this.SaveKeybinds(false); } catch { } }

            bool prevFamily = this.autoSellMatchFamily;
            this.autoSellMatchFamily = this.DrawSwitchToggle(new Rect(toggleX, settingsCard.y + 72f, 250f, 24f), this.autoSellMatchFamily, "Match Similar Items");
            if (this.autoSellMatchFamily != prevFamily) { try { this.SaveKeybinds(false); } catch { } }

            bool prevHideList = this.autoSellHideBagItems;
            this.autoSellHideBagItems = this.DrawSwitchToggle(new Rect(toggleX, settingsCard.y + 102f, 250f, 24f), this.autoSellHideBagItems, "Hide Item List");
            if (this.autoSellHideBagItems != prevHideList) { try { this.SaveKeybinds(false); } catch { } }

            string sellModeHelp = this.autoSellMatchFamily
                ? "Similar: sells same item family, like all birdphotos."
                : "Exact: sells only this selected item.";
            GUI.Label(new Rect(toggleX, settingsCard.y + 126f, 250f, 16f), sellModeHelp, tinyStyle);
            GUI.Label(new Rect(toggleX, settingsCard.y + 146f, 108f, 18f), this.autoSellFullStack ? "Cap: ignored" : this.LF("Cap: {0}", this.autoSellMaxPerStack), tinyStyle);
            if (!this.autoSellFullStack)
            {
                int prevMax = this.autoSellMaxPerStack;
                this.autoSellMaxPerStack = this.UI_DrawAccentIntSlider(new Rect(toggleX + 110f, settingsCard.y + 148f, 130f, 16f), this.autoSellMaxPerStack, 0, 200);
                if (this.autoSellMaxPerStack != prevMax) { try { this.SaveKeybinds(false); } catch { } }
            }
            GUI.Label(new Rect(toggleX, settingsCard.y + 170f, 108f, 18f), this.LF("Keep Per Item: {0}", this.autoSellReserveCount), tinyStyle);
            int prevReserveCount = this.autoSellReserveCount;
            this.autoSellReserveCount = this.UI_DrawAccentIntSlider(new Rect(toggleX + 110f, settingsCard.y + 172f, 130f, 16f), this.autoSellReserveCount, 0, 200);
            if (this.autoSellReserveCount != prevReserveCount) { try { this.SaveKeybinds(false); } catch { } }
            num += 216;

            float sourceRowY = (float)num;
            float sourceDropdownX = left;
            float sourceDropdownWidth = 120f;
            float actionGap = 10f;
            float actionRowHeight = 34f;
            Rect sourceDropdownRect = new Rect(sourceDropdownX, sourceRowY, sourceDropdownWidth, 28f);

            GUI.Box(sourceDropdownRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(sourceDropdownRect, 1f);
            if (GUI.Button(sourceDropdownRect, "", GUIStyle.none))
            {
                this.autoSellScanSourceDropdownOpen = !this.autoSellScanSourceDropdownOpen;
            }
            GUI.Label(new Rect(sourceDropdownRect.x + 12f, sourceDropdownRect.y + 1f, sourceDropdownRect.width - 34f, sourceDropdownRect.height - 2f), this.GetAutoSellScanSourceLabel(), dropdownValueStyle);
            GUI.Label(new Rect(sourceDropdownRect.xMax - 24f, sourceDropdownRect.y + 1f, 16f, sourceDropdownRect.height - 2f), this.autoSellScanSourceDropdownOpen ? "^" : "v", dropdownArrowStyle);

            float primaryStartX = sourceDropdownRect.xMax + 12f;
            float primaryAvailableWidth = panelWidth - (primaryStartX - left);
            float primaryButtonWidth = Mathf.Max(110f, (primaryAvailableWidth - actionGap) * 0.5f);
            bool blockRowActions = this.autoSellScanSourceDropdownOpen;
            GUI.enabled = !blockRowActions;
            if (GUI.Button(new Rect(primaryStartX, sourceRowY - 3f, primaryButtonWidth, actionRowHeight), this.L("SELL SELECTED"), this.themePrimaryButtonStyle))
            {
                this.ExecuteDirectAutoSell(false);
            }
            if (this.DrawPrimaryActionButton(new Rect(primaryStartX + primaryButtonWidth + actionGap, sourceRowY - 3f, primaryButtonWidth, actionRowHeight), "SCAN ITEMS"))
            {
                this.autoSellBagItems = this.ScanBackpackForAutoSellItems();
                this.autoSellStatus = this.autoSellBagItems.Count > 0
                    ? (this.GetAutoSellScanSourceLabel() + " list refreshed")
                    : ("No " + this.GetAutoSellScanSourceLabel().ToLowerInvariant() + " items found");
            }
            GUI.enabled = true;
            num += Mathf.CeilToInt(actionRowHeight + 6f);
            if (this.autoSellScanSourceDropdownOpen)
            {
                float panelHeight = this.autoSellScanSourceLabels.Length * 30f + 8f;
                num = Mathf.Max(num, Mathf.CeilToInt(sourceDropdownRect.yMax + 4f + panelHeight + 8f));
            }

            Rect statusCard = new Rect(left, (float)num, panelWidth, 52f);
            GUI.Box(statusCard, "", this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(statusCard, 1f);
            GUI.Label(new Rect(statusCard.x + 12f, statusCard.y + 7f, 110f, 18f), "Status", fieldLabelStyle);
            GUI.Label(new Rect(statusCard.x + 78f, statusCard.y + 7f, statusCard.width - 96f, 18f), this.autoSellStatus ?? "Idle", tinyStyle);
            GUI.Label(new Rect(statusCard.x + 12f, statusCard.y + 27f, statusCard.width - 24f, 18f), this.autoSellLastMatchSummary ?? "No scan yet", tinyStyle);
            num += 64;

            if (this.autoSellHideBagItems)
            {
                GUI.Label(new Rect(left, (float)num, 500f, 24f), this.GetAutoSellScanSourceLabel() + " item list is hidden. Scan Items still updates selected item data.");
                num += 30;
            }
            else if (this.autoSellBagItems != null && this.autoSellBagItems.Count > 0)
            {
                GUI.Label(new Rect(left, (float)num, 500f, 22f), this.LF("{0} Items ({1})", this.GetAutoSellScanSourceLabel(), this.autoSellBagItems.Count), fieldLabelStyle);
                num += 24;

                float cellW = 92f;
                float cellH = 88f;
                int columns = 6;
                int rows = Mathf.CeilToInt(this.autoSellBagItems.Count / (float)columns);
                float listHeight = Mathf.Min(rows * cellH, 276f);
                Rect scrollViewRect = new Rect(left, (float)num, panelWidth, listHeight);
                Rect scrollContentRect = new Rect(0f, 0f, panelWidth - 20f, rows * cellH);
                this.autoSellBagItemScrollPos = GUI.BeginScrollView(scrollViewRect, this.autoSellBagItemScrollPos, scrollContentRect);
                int firstVisibleRow = Mathf.Max(0, Mathf.FloorToInt(this.autoSellBagItemScrollPos.y / cellH));
                int visibleRowCount = Mathf.CeilToInt(listHeight / cellH) + 1;
                int lastVisibleRow = Mathf.Min(rows - 1, firstVisibleRow + visibleRowCount);
                int firstVisibleIndex = Mathf.Clamp(firstVisibleRow * columns, 0, this.autoSellBagItems.Count);
                int lastVisibleIndexExclusive = Mathf.Clamp((lastVisibleRow + 1) * columns, 0, this.autoSellBagItems.Count);
                string activeAutoSellKey = this.GetActiveAutoSellMatchKey();
                GUIStyle itemStyle = new GUIStyle(GUI.skin.label);
                itemStyle.alignment = TextAnchor.UpperCenter;
                itemStyle.fontSize = 10;
                itemStyle.wordWrap = true;
                itemStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
                GUIStyle countBadgeStyle = new GUIStyle(tinyStyle) { alignment = TextAnchor.UpperRight, fontSize = 10 };
                GUIStyle starBadgeStyle = new GUIStyle(tinyStyle) { alignment = TextAnchor.UpperLeft, fontSize = 10 };
                GUIStyle initialsStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };

                for (int i = firstVisibleIndex; i < lastVisibleIndexExclusive; i++)
                {
                    AutoSellBagItemEntry entry = this.autoSellBagItems[i];
                    int col = i % columns;
                    int row = i / columns;
                    Rect cellRect = new Rect(col * cellW + 2f, row * cellH + 2f, cellW - 8f, cellH - 8f);
                    string entrySelectKey = this.autoSellMatchFamily ? this.GetAutoSellFamilyKey(entry.MatchKey) : entry.MatchKey;
                    // Highlight must mirror the sell-time match exactly: identity (staticId + star)
                    // in exact mode, key + star in family/typed mode.
                    bool isSelected = !this.autoSellMatchFamily && this.autoSellSelectedStaticId > 0
                        ? (entry.StaticId == this.autoSellSelectedStaticId && Math.Max(0, entry.StarRate) == this.autoSellSelectedStar)
                        : (string.Equals(activeAutoSellKey, entrySelectKey, StringComparison.OrdinalIgnoreCase)
                            && (this.autoSellSelectedStar <= 0 || entry.StarRate == this.autoSellSelectedStar));
                    GUI.Box(cellRect, "", isSelected ? (this.themeTopTabActiveStyle ?? this.themePanelStyle ?? GUI.skin.box) : (this.themeContentStyle ?? this.themePanelStyle ?? GUI.skin.box));
                    this.DrawCardOutline(cellRect, isSelected ? 2f : 1f);

                    Rect iconRect = new Rect(cellRect.x + 21f, cellRect.y + 6f, 42f, 42f);
                    if (this.TryGetAutoSellItemTexture(entry, out Texture2D tex) && tex != null)
                    {
                        GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, true);
                    }
                    else
                    {
                        GUI.Label(iconRect, this.GetAutoSellItemInitials(entry.DisplayName), initialsStyle);
                    }

                    if (entry.Count > 0)
                    {
                        GUI.Label(new Rect(cellRect.x + cellRect.width - 34f, cellRect.y + 4f, 30f, 16f), "x" + entry.Count, countBadgeStyle);
                    }
                    if (entry.StarRate > 0)
                    {
                        GUI.Label(new Rect(cellRect.x + 4f, cellRect.y + 4f, 34f, 16f), entry.StarRate + "*", starBadgeStyle);
                    }

                    if (GUI.Button(cellRect, GUIContent.none, GUIStyle.none))
                    {
                        this.autoSellItemKey = this.autoSellMatchFamily ? this.GetAutoSellFamilyKey(entry.MatchKey) : entry.MatchKey;
                        // The clicked cell IS the selection: its staticId (exact mode) and its star
                        // (including "no star") replace any previous constraint entirely.
                        this.autoSellSelectedStaticId = this.autoSellMatchFamily ? 0 : Math.Max(0, entry.StaticId);
                        this.autoSellSelectedStar = Math.Max(0, entry.StarRate);
                        this.autoSellStatus = "Selected: " + entry.DisplayName + (this.autoSellMatchFamily ? " family" : "");
                        this.autoSellLastMatchSummary = "Selection changed. Use Sell Selected or wait for Auto Sell.";
                        try { this.SaveKeybinds(false); } catch { }
                    }

                    GUI.Label(new Rect(cellRect.x + 4f, cellRect.y + 51f, cellRect.width - 8f, 30f), entry.DisplayName, itemStyle);
                }
                GUI.EndScrollView();
                num += Mathf.CeilToInt(listHeight + 12f);
            }
            else if (this.autoSellBagItems != null && this.autoSellBagItems.Count == 0)
            {
                GUI.Label(new Rect(left, (float)num, 500f, 24f), "No " + this.GetAutoSellScanSourceLabel().ToLowerInvariant() + " items found yet. Try again after the world finishes loading.");
                num += 30;
            }
            else
            {
                GUI.Label(new Rect(left, (float)num, 560f, 24f), "Press Scan Items to read " + this.GetAutoSellScanSourceLabel().ToLowerInvariant() + " data. Icons load automatically from the game.");
                num += 30;
            }

            if (GUI.Button(new Rect(left, (float)num, 120f, 32f), this.L("CLEAR"), this.themeDangerButtonStyle ?? GUI.skin.button))
            {
                this.autoSellItemKey = "";
                this.autoSellSelectedStaticId = 0;
                this.autoSellSelectedStar = 0;
                this.autoSellStatus = "Selection cleared";
                this.autoSellLastMatchSummary = "No scan yet";
                try { this.SaveKeybinds(false); } catch { }
            }
            num += 44;

            // OPEN SELL PANEL = coin sell (ItemSellPanel / CmdQuickSell); OPEN TOKEN SELL = Fashionwave /
            // battle-pass token sell (BattlePassSellPanel / CmdBattlePassSell) — the "Available in
            // Fashionwave" panel for the current period token.
            if (this.DrawPrimaryActionButton(new Rect(left, (float)num, 195f, 32f), "OPEN SELL PANEL"))
            {
                this.StartShopQuickSellOpenPanel();
            }
            if (this.DrawPrimaryActionButton(new Rect(left + 205f, (float)num, 195f, 32f), "OPEN TOKEN SELL"))
            {
                this.StartTokenSellOpenPanel();
            }
            num += 36;
            GUI.Label(new Rect(left, (float)num, panelWidth - 20f, 22f),
                "Coins: " + (this.shopQuickSellStatus ?? "Idle.") + "     Tokens: " + (this.tokenSellPanelStatus ?? "Idle."),
                tinyStyle);
            num += 30;

            if (this.autoSellScanSourceDropdownOpen)
            {
                float panelHeight = this.autoSellScanSourceLabels.Length * 30f + 8f;
                Rect panelRect = new Rect(sourceDropdownRect.x, sourceDropdownRect.yMax + 4f, sourceDropdownRect.width, panelHeight);
                GUI.Box(panelRect, "", this.themeContentStyle ?? this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(panelRect, 1f);
                for (int i = 0; i < this.autoSellScanSourceLabels.Length; i++)
                {
                    bool selected = this.autoSellScanSource == i;
                    Rect optionRect = new Rect(panelRect.x + 4f, panelRect.y + 4f + i * 30f, panelRect.width - 8f, 26f);
                    GUIStyle optionStyle = selected
                        ? (this.themeTopTabActiveStyle ?? this.themePrimaryButtonStyle ?? GUI.skin.button)
                        : (this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.button);
                    if (GUI.Button(optionRect, this.autoSellScanSourceLabels[i], optionStyle))
                    {
                        this.autoSellScanSource = i;
                        this.autoSellScanSourceDropdownOpen = false;
                        this.autoSellBagItems = null;
                        this.autoSellBagItemScrollPos = Vector2.zero;
                        this.autoSellStatus = "Scan source: " + this.GetAutoSellScanSourceLabel();
                        this.autoSellLastMatchSummary = "Press Scan Items to load " + this.GetAutoSellScanSourceLabel().ToLowerInvariant() + " items.";
                        try { this.SaveKeybinds(false); } catch { }
                    }
                }
            }

            return (float)num;
        }

        private void InvalidateDirectBackpackRuntimeSnapshot()
        {
            this.directBackpackRuntimeSnapshotAt = -999f;
            this.directBackpackRuntimeSnapshotSource = "";
            this.ClearDirectBackpackRuntimeItems();
            this.ClearCachedRepairKit();
            this.ClearCachedFood();
        }

        private bool TryGetDirectBackpackItemCountByNetId(uint targetNetId, out int count, bool forceRefresh = false)
        {
            count = 0;
            if (targetNetId == 0U)
            {
                return false;
            }

            if (this.TryRefreshDirectBackpackRuntimeSnapshot(forceRefresh))
            {
                for (int i = 0; i < this.directBackpackRuntimeItems.Count; i++)
                {
                    DirectBackpackRuntimeItem item = this.directBackpackRuntimeItems[i];
                    if (item != null && item.NetId == targetNetId)
                    {
                        count = item.Count;
                        return true;
                    }
                }
            }

            if (this.TryGetDirectBackpackItemCountByNetIdManaged(targetNetId, out count))
            {
                return true;
            }

            return DirectBackpackUnsafeAuraMonoFallbackEnabled
                && this.TryGetDirectBackpackItemCountByNetIdAuraMono(targetNetId, out count);
        }

        private bool TryGetDirectBackpackItemCountByNetIdManaged(uint targetNetId, out int count)
        {
            count = 0;
            try
            {
                if (!this.TryGetDirectBackpackSystem(out object backPackObj, out MethodInfo getAllItem, out Type storageType, out bool getAllItemNeedsStorage))
                {
                    return false;
                }

                object backpackStorage = storageType != null && storageType.IsEnum ? Enum.ToObject(storageType, 1) : (object)1;
                object itemListObj = getAllItemNeedsStorage
                    ? getAllItem.Invoke(backPackObj, new[] { backpackStorage })
                    : getAllItem.Invoke(backPackObj, null);

                IEnumerable items = itemListObj as IEnumerable;
                if (items == null)
                {
                    return false;
                }

                foreach (object item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (this.TryGetManagedUInt32Member(item, "netId", out uint candidateNetId)
                        && candidateNetId == targetNetId)
                    {
                        this.TryGetManagedBackpackItemCount(item, out count);
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetDirectBackpackItemCountByNetIdAuraMono(uint targetNetId, out int count)
        {
            count = 0;
            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj) || backPackSystemObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
                IntPtr getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
                bool getAllItemNeedsStorageType = true;
                if (getAllItemMethod == IntPtr.Zero)
                {
                    getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                    getAllItemNeedsStorageType = false;
                }
                if (getAllItemMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                int storageTypeBackpack = 1;
                IntPtr itemListObj;
                unsafe
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&storageTypeBackpack);
                    itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, getAllItemNeedsStorageType ? (IntPtr)args : IntPtr.Zero, ref exc);
                }
                if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                {
                    return false;
                }

                List<IntPtr> items = new List<IntPtr>();
                List<uint> itemPins = new List<uint>();
                if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, itemPins) || items.Count == 0)
                {
                    FreeAuraMonoPins(itemPins);
                    return false;
                }

                try
                {
                    foreach (IntPtr itemObj in items)
                    {
                        if (itemObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        if (this.TryGetDirectBackpackItemNetId(itemObj, out uint candidateNetId)
                            && candidateNetId == targetNetId)
                        {
                            this.TryGetDirectBackpackItemCount(itemObj, out count);
                            return true;
                        }
                    }
                }
                finally
                {
                    FreeAuraMonoPins(itemPins);
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryFindDirectBackpackItemByStaticId(int staticId, out uint netId)
        {
            netId = 0U;
            if (staticId <= 0)
            {
                return false;
            }

            this.lastDirectBackpackMatchedNetId = 0U;
            this.lastDirectBackpackMatchedStaticId = 0;
            this.lastDirectBackpackMatchedEntityType = 0;
            this.lastDirectBackpackMatchedCount = 0;

            if (this.TryRefreshDirectBackpackRuntimeSnapshot(false))
            {
                for (int i = 0; i < this.directBackpackRuntimeItems.Count; i++)
                {
                    DirectBackpackRuntimeItem item = this.directBackpackRuntimeItems[i];
                    if (item == null || item.NetId == 0U || item.StaticId != staticId)
                    {
                        continue;
                    }

                    netId = item.NetId;
                    this.lastDirectBackpackMatchedNetId = item.NetId;
                    this.lastDirectBackpackMatchedStaticId = item.StaticId;
                    this.lastDirectBackpackMatchedEntityType = item.EntityType;
                    this.lastDirectBackpackMatchedCount = item.Count;
                    this.AutoEatRepairLog("[UseBait] Runtime snapshot match netId=" + item.NetId + " staticId=" + item.StaticId + " count=" + item.Count);
                    return true;
                }
            }

            if (this.TryFindDirectBackpackItemByStaticIdManaged(staticId, out netId))
            {
                return true;
            }

            if (!DirectBackpackUnsafeAuraMonoFallbackEnabled)
            {
                return false;
            }

            return this.TryFindDirectBackpackItemByStaticIdAuraMono(staticId, out netId);
        }

        private bool TryFindDirectBackpackItemByStaticIdManaged(int staticId, out uint netId)
        {
            netId = 0U;
            try
            {
                if (!this.TryGetDirectBackpackSystem(out object backPackObj, out MethodInfo getAllItem, out Type storageType, out bool getAllItemNeedsStorage))
                {
                    return false;
                }

                object backpackStorage = storageType != null && storageType.IsEnum ? Enum.ToObject(storageType, 1) : (object)1;
                object itemListObj = getAllItemNeedsStorage
                    ? getAllItem.Invoke(backPackObj, new[] { backpackStorage })
                    : getAllItem.Invoke(backPackObj, null);

                IEnumerable items = itemListObj as IEnumerable;
                if (items == null)
                {
                    return false;
                }

                foreach (object item in items)
                {
                    if (item == null
                        || !this.TryGetManagedInt32Member(item, "staticId", out int candidateStaticId)
                        || candidateStaticId != staticId
                        || !this.TryGetManagedUInt32Member(item, "netId", out uint candidateNetId)
                        || candidateNetId == 0U)
                    {
                        continue;
                    }

                    netId = candidateNetId;
                    this.lastDirectBackpackMatchedNetId = candidateNetId;
                    this.lastDirectBackpackMatchedStaticId = candidateStaticId;
                    this.TryGetManagedInt32Member(item, "entityType", out this.lastDirectBackpackMatchedEntityType);
                    this.TryGetManagedBackpackItemCount(item, out this.lastDirectBackpackMatchedCount);
                    this.AutoEatRepairLog("[UseBait] Managed match netId=" + candidateNetId + " staticId=" + candidateStaticId + " count=" + this.lastDirectBackpackMatchedCount);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryFindDirectBackpackItemByStaticIdAuraMono(int staticId, out uint netId)
        {
            netId = 0U;
            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj) || backPackSystemObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
                IntPtr getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
                bool getAllItemNeedsStorageType = true;
                if (getAllItemMethod == IntPtr.Zero)
                {
                    getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                    getAllItemNeedsStorageType = false;
                }
                if (getAllItemMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                int storageTypeBackpack = 1;
                IntPtr itemListObj;
                unsafe
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&storageTypeBackpack);
                    itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, getAllItemNeedsStorageType ? (IntPtr)args : IntPtr.Zero, ref exc);
                }
                if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                {
                    return false;
                }

                List<IntPtr> items = new List<IntPtr>();
                List<uint> itemPins = new List<uint>();
                if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, itemPins) || items.Count == 0)
                {
                    FreeAuraMonoPins(itemPins);
                    return false;
                }

                try
                {
                    foreach (IntPtr itemObj in items)
                    {
                        if (itemObj == IntPtr.Zero
                            || !this.TryGetDirectBackpackItemStaticId(itemObj, out int candidateStaticId)
                            || candidateStaticId != staticId
                            || !this.TryGetDirectBackpackItemNetId(itemObj, out uint candidateNetId)
                            || candidateNetId == 0U)
                        {
                            continue;
                        }

                        netId = candidateNetId;
                        this.lastDirectBackpackMatchedNetId = candidateNetId;
                        this.lastDirectBackpackMatchedStaticId = candidateStaticId;
                        this.TryGetDirectBackpackItemEntityType(itemObj, out this.lastDirectBackpackMatchedEntityType);
                        this.TryGetDirectBackpackItemCount(itemObj, out this.lastDirectBackpackMatchedCount);
                        this.AutoEatRepairLog("[UseBait] AuraMono match netId=" + candidateNetId + " staticId=" + candidateStaticId + " count=" + this.lastDirectBackpackMatchedCount);
                        return true;
                    }
                }
                finally
                {
                    FreeAuraMonoPins(itemPins);
                }
            }
            catch
            {
            }

            return false;
        }

        private void AutoSellLog(string message)
        {
            if (AutoSellLogsEnabled)
            {
                ModLogger.Msg("[AutoSell] " + message);
            }
        }

        private void AutoSellLogSellResult(string message)
        {
            ModLogger.Msg("[AutoSell] " + message);
        }

        private void ProcessAutoSell()
        {
            if (!this.autoSellEnabled)
            {
                return;
            }

            // The backpack-change hook is installed only while Auto Sell is enabled (first enabled
            // tick). Its handler marks the bag dirty on RefreshBackPackEvent.
            this.EnsureAutoSellEventHooks();

            if (Time.unscaledTime < this.nextAutoSellAt)
            {
                return;
            }

            this.nextAutoSellAt = Time.unscaledTime + Mathf.Clamp(this.autoSellInterval, 1f, 120f);

            // If the hook is live and nothing was added/changed in the backpack since the last scan,
            // there is nothing new to sell — skip the scan/sell entirely. Fall back to always-scan
            // until the detour installs (engine lazy install / hard failure) so behaviour can't regress.
            if (this.IsGameEventHookInstalled(AutoSellBackpackEventName) && !this.autoSellBackpackDirty)
            {
                return;
            }

            Breadcrumbs.Drop("AutoSell.scan");
            if (!this.HasAutoSellSelection())
            {
                this.autoSellStatus = "Waiting for item selection";
                return; // don't consume the dirty flag — retry once ready
            }
            if (this.isRepairing || this.isAutoEating)
            {
                this.autoSellStatus = "Waiting for bag automation";
                return;
            }
            if (!this.IsAutoSellWorldReady())
            {
                this.autoSellStatus = "Waiting for world";
                return;
            }

            // About to scan the current bag state — consume the dirty flag. Selling re-marks it via
            // the resulting removal events, which costs at most one extra no-op scan next tick.
            this.autoSellBackpackDirty = false;
            this.ExecuteDirectAutoSell(true);
        }

        // RefreshBackPackEvent (XDTDataAndProtocol.Events) is a GLOBAL event dispatched by
        // StorageBase.AddItem on every item add (and other bag mutations); payload is just
        // EStorageType storageType @0. We use it as a "bag dirty" signal for the periodic Auto Sell
        // scan. See docs/GAME_EVENTS.md.
        private const string AutoSellBackpackEventName = "XDTDataAndProtocol.Events.RefreshBackPackEvent";
        private const int AutoSellBackpackEventBytes = 4;
        private const int AutoSellBackpackStorageType = 1; // EStorageType.Backpack

        private bool autoSellEventHooksRegistered;
        private bool autoSellBackpackDirty = true; // scan on the first run
        private bool autoSellListDirty; // UI item list wants a refresh (set by the bag-change event)

        private void EnsureAutoSellEventHooks()
        {
            if (this.autoSellEventHooksRegistered)
            {
                return;
            }

            this.autoSellEventHooksRegistered = true;
            this.RegisterGameEventHook(AutoSellBackpackEventName, AutoSellBackpackEventBytes, this.OnAutoSellRefreshBackPackEvent);
        }

        // Runs on the Unity main thread (event drain). Any storage mutation marks the UI item list
        // dirty (it can show warehouse items too); only player-backpack mutations mark the Auto Sell
        // scan dirty — other storages (garage, …) never feed the periodic sell.
        private void OnAutoSellRefreshBackPackEvent(GameEventSnapshot e)
        {
            this.autoSellListDirty = true;
            if (e.ReadInt32(0) == AutoSellBackpackStorageType)
            {
                this.autoSellBackpackDirty = true;
            }
        }

        private bool ExecuteDirectAutoSell(bool fromAuto)
        {
            Breadcrumbs.Drop("AutoSell.execute", fromAuto ? "auto" : "manual");
            if (!this.IsAutoSellWorldReady())
            {
                this.autoSellStatus = "Waiting for world";
                if (!fromAuto)
                {
                    this.AddMenuNotification("Auto Sell waits until you are in-world", new Color(1f, 0.85f, 0.35f));
                }
                return false;
            }

            Dictionary<uint, int> sellItems = this.BuildDirectSellItemMap(out int matchedBagTotal, out int reservedKept);
            if (sellItems.Count == 0)
            {
                bool reservedAll = matchedBagTotal > 0 && reservedKept > 0 && this.autoSellReserveCount > 0 && matchedBagTotal <= reservedKept;
                this.autoSellStatus = reservedAll
                    ? "Keep Per Item reserved all matches"
                    : !this.HasAutoSellSelection()
                    ? "Select or type an item first"
                    : "No matching sellable items";
                if (reservedAll && !fromAuto)
                {
                    this.AddMenuNotification("Nothing sold - Keep Per Item reserved all matches", new Color(1f, 0.85f, 0.35f));
                }
                return false;
            }

            int totalCount = 0;
            foreach (int count in sellItems.Values)
            {
                totalCount += Math.Max(0, count);
            }

            this.AutoSellLogSellResult("QuickSell request. stacks=" + sellItems.Count + " totalCount=" + totalCount + " key=" + this.GetActiveAutoSellMatchKey()
                + (!this.autoSellMatchFamily && this.autoSellSelectedStaticId > 0 ? " staticId=" + this.autoSellSelectedStaticId : "")
                + (this.autoSellSelectedStar > 0 ? " star=" + this.autoSellSelectedStar : ""));
            if (!string.IsNullOrWhiteSpace(this.autoSellSelectedDetails))
            {
                this.AutoSellLog("QuickSell items: " + this.autoSellSelectedDetails);
            }

            bool sent;
            Dictionary<uint, int> optimisticSoldMap = sellItems;
            if (this.autoSellFestivalTokensEnabled && this.TryGetFestivalSellCurrencyId(out int festivalCurrencyId))
            {
                Dictionary<uint, int> festivalItems = new Dictionary<uint, int>();
                Dictionary<uint, int> coinItems = new Dictionary<uint, int>();
                int festivalCapOverflow = 0;
                string festivalCurrencyStatus = string.Empty;
                try
                {
                    this.SplitSellItemsByPeriodCurrency(festivalCurrencyId, sellItems, out festivalItems, out coinItems, out festivalCapOverflow, out festivalCurrencyStatus);
                }
                catch (Exception ex)
                {
                    // The split writes its out params progressively — discard any partial token
                    // batch so a mid-split throw can't double-sell stacks (token + coin).
                    festivalItems = new Dictionary<uint, int>();
                    festivalCapOverflow = 0;
                    coinItems = new Dictionary<uint, int>(sellItems);
                    festivalCurrencyStatus = "split exception: " + ex.GetType().Name + ": " + ex.Message + " -> all coin";
                    this.AutoSellLogSellResult(festivalCurrencyStatus);
                }

                int festivalCount = this.SumAutoSellItemCounts(festivalItems);
                int coinCount = this.SumAutoSellItemCounts(coinItems);
                this.autoSellLastMatchSummary += "\nSell split: " + festivalItems.Count + " token stack(s), " + coinItems.Count + " coin stack(s)"
                    + (festivalCapOverflow > 0 ? ", " + festivalCapOverflow + " over cap -> coins" : "");
                ModLogger.Msg("[AutoSell] " + festivalCurrencyStatus);
                this.AutoSellLogSellResult("Festival sell batch count=" + festivalItems.Count + " coin batch count=" + coinItems.Count
                    + (festivalCapOverflow > 0 ? " capOverflow->coins=" + festivalCapOverflow : ""));
                // The optimistic UI removal must reflect the CLAMPED quantities. A cap-split stack
                // sells its token part AND its over-cap remainder for coins, so its two entries
                // (festivalItems[key]=tokenQty + coinItems[key]=overflow) must SUM to the full stack
                // — add, don't overwrite, or the overflow would linger in the on-screen list.
                Dictionary<uint, int> clampedSoldMap = new Dictionary<uint, int>(coinItems);
                foreach (KeyValuePair<uint, int> festivalEntry in festivalItems)
                {
                    clampedSoldMap.TryGetValue(festivalEntry.Key, out int existingCoin);
                    clampedSoldMap[festivalEntry.Key] = existingCoin + festivalEntry.Value;
                }
                optimisticSoldMap = clampedSoldMap;
                totalCount = festivalCount + coinCount;
                bool sentFestival = festivalItems.Count == 0 || this.TryQuickSellItemsBatched(festivalItems, festivalCurrencyId);
                bool sentCoins = coinItems.Count == 0 || this.TryQuickSellItemsBatched(coinItems, 0);
                this.AutoSellLogSellResult("Festival sell result tokenOk=" + sentFestival + " coinOk=" + sentCoins);
                sent = (festivalItems.Count > 0 || coinItems.Count > 0)
                    && (festivalItems.Count == 0 || sentFestival)
                    && (coinItems.Count == 0 || sentCoins);
                if (sent)
                {
                    this.autoSellStatus = festivalCount > 0 && coinCount > 0
                        ? ("Sell sent: " + festivalCount + " token(s), " + coinCount + " coin(s)")
                        : festivalCount > 0
                        ? ("Sell sent: " + festivalCount + " festival token item(s)")
                        : ("Sell sent: " + coinCount + " for coins");
                    if (festivalCapOverflow > 0)
                    {
                        this.autoSellStatus += " (incl. " + festivalCapOverflow + " over token cap -> coins)";
                    }
                }
                else if (sentCoins && festivalItems.Count > 0)
                {
                    this.autoSellStatus = "Sold " + coinCount + " for coins; token sell failed";
                }
                else if (sentFestival && coinItems.Count > 0)
                {
                    this.autoSellStatus = "Sold " + festivalCount + " for tokens; coin sell failed";
                }
                else
                {
                    this.autoSellStatus = "Sell failed";
                }
            }
            else if (this.autoSellFestivalTokensEnabled)
            {
                sent = this.TryQuickSellItemsBatched(sellItems, 0);
                this.autoSellStatus = sent ? ("Sell sent for coins (festival currency unavailable)") : "Sell failed";
            }
            else
            {
                sent = this.TryQuickSellItemsBatched(sellItems, 0);
                this.autoSellStatus = sent ? ("Sell sent: " + totalCount + " item(s)") : "Sell failed";
            }

            if (!sent && fromAuto)
            {
                this.autoSellEnabled = false;
                this.autoSellStatus = "Sell failed - auto disabled";
                this.AutoSellLog("Auto sell disabled after failed send to prevent repeated unsafe retries.");
            }
            if (sent && !fromAuto)
            {
                this.AddMenuNotification("Auto Sell: " + totalCount + " item(s)", new Color(0.45f, 1f, 0.55f));
                this.FinalizeManualAutoSellSend(optimisticSoldMap);
            }
            else if (!sent && !fromAuto)
            {
                this.AddMenuNotification("Auto Sell failed - check logs", new Color(1f, 0.55f, 0.55f));
            }
            return sent;
        }

        private void FinalizeManualAutoSellSend(Dictionary<uint, int> soldMap)
        {
            if (soldMap == null || soldMap.Count == 0)
            {
                return;
            }

            string statusMessage = this.autoSellStatus;
            this.ApplyOptimisticAutoSellToItemList(soldMap);
            this.autoSellStatus = statusMessage;
            this.ScheduleAutoSellListRescan();
        }

        private void ApplyOptimisticAutoSellToItemList(Dictionary<uint, int> soldMap)
        {
            if (this.autoSellBagItems == null || soldMap == null || soldMap.Count == 0)
            {
                return;
            }

            for (int i = this.autoSellBagItems.Count - 1; i >= 0; i--)
            {
                AutoSellBagItemEntry entry = this.autoSellBagItems[i];
                if (entry == null || !soldMap.TryGetValue(entry.NetId, out int soldQty) || soldQty <= 0)
                {
                    continue;
                }

                entry.Count -= soldQty;
                if (entry.Count <= 0)
                {
                    this.autoSellBagItems.RemoveAt(i);
                }
                else if (entry.StackCount > 1)
                {
                    entry.StackCount = Math.Max(1, entry.StackCount - 1);
                }
            }
        }

        private void ScheduleAutoSellListRescan()
        {
            // One event-gated rescan instead of blind retry polling: wait for the server's
            // RefreshBackPackEvent (the bag actually changed) with a deadline fallback for the
            // time before the event hook installs.
            this.EnsureAutoSellEventHooks();
            this.autoSellListDirty = false;
            this.autoSellPendingRescanAt = Time.unscaledTime + 0.35f;
            this.autoSellPendingRescanRetries = 1;
        }

        private void ProcessPendingAutoSellListRescan()
        {
            if (this.autoSellPendingRescanRetries <= 0)
            {
                return;
            }

            if (!this.showMenu || this.selectedTab != 3 || this.automationSubTab != 4)
            {
                this.autoSellPendingRescanRetries = 0;
                return;
            }

            if (Time.unscaledTime < this.autoSellPendingRescanAt)
            {
                return;
            }

            bool hookLive = this.IsGameEventHookInstalled(AutoSellBackpackEventName);
            bool deadlinePassed = Time.unscaledTime >= this.autoSellPendingRescanAt + 2f;
            if (hookLive && !this.autoSellListDirty && !deadlinePassed)
            {
                return; // bag unchanged so far — wait for the event (or the deadline)
            }

            string statusMessage = this.autoSellStatus;
            this.autoSellBagItems = this.ScanBackpackForAutoSellItems();
            this.autoSellStatus = statusMessage;
            this.autoSellPendingRescanRetries = 0;
            this.autoSellListDirty = false;
        }

        private bool IsAutoSellWorldReady()
        {
            try
            {
                GameObject loginPanel = GameObject.Find(LOGIN_PANEL_PATH);
                if (loginPanel != null && loginPanel.activeInHierarchy)
                {
                    return false;
                }

                GameObject loginRoomPanel = GameObject.Find(LOGIN_ROOM_PANEL_PATH);
                if (loginRoomPanel != null && loginRoomPanel.activeInHierarchy)
                {
                    return false;
                }

                GameObject player = GetPlayer();
                if (player == null || !player.activeInHierarchy)
                {
                    return false;
                }

                GameObject bagButton = GameObject.Find(BAG_BUTTON_PATH);
                if (bagButton == null)
                {
                    GameObject statusPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)");
                    if (statusPanel == null || !statusPanel.activeInHierarchy)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private Dictionary<uint, int> BuildDirectSellItemMap(out int matchedBagTotal, out int reservedKept)
        {
            matchedBagTotal = 0;
            reservedKept = 0;
            Dictionary<uint, int> sellItems = new Dictionary<uint, int>();
            Dictionary<uint, int> bagCountsByNetId = new Dictionary<uint, int>();
            Dictionary<uint, string> reserveGroupsByNetId = new Dictionary<uint, string>();
            Dictionary<uint, string> sellDetailsByNetId = new Dictionary<uint, string>();
            this.autoSellCollectedStaticIdsByNetId.Clear();

            // Exact mode sells by the clicked entry's identity (staticId + its star); family mode
            // and hand-typed keys match descriptors by text with no identity.
            int selectedStaticId = this.autoSellMatchFamily ? 0 : Math.Max(0, this.autoSellSelectedStaticId);
            int selectedStar = Mathf.Clamp(this.autoSellSelectedStar, 0, 5);
            string key = this.GetActiveAutoSellMatchKey();
            if (selectedStaticId <= 0 && (string.IsNullOrWhiteSpace(key) || key.Length < 2))
            {
                this.autoSellLastMatchSummary = "Safety: no item selected (match key empty/too short).";
                this.autoSellSelectedDetails = "";
                this.AutoSellLog("Scan blocked because no item is selected.");
                return sellItems;
            }

            List<string> skippedSamples = new List<string>();
            int inspected = 0;
            int skippedStar = 0;
            this.CollectDirectSellItemsMono(selectedStaticId, selectedStar, key, bagCountsByNetId, reserveGroupsByNetId, sellDetailsByNetId, skippedSamples, ref inspected, ref skippedStar);

            int matchedStacks = bagCountsByNetId.Count;
            foreach (int count in bagCountsByNetId.Values)
            {
                matchedBagTotal += Math.Max(0, count);
            }

            reservedKept = this.ApplyAutoSellReserve(sellItems, reserveGroupsByNetId, bagCountsByNetId);
            if (!this.autoSellAllMatchingStacks && !this.autoSellFestivalTokensEnabled)
            {
                this.LimitAutoSellToSingleStack(sellItems);
            }
            int totalCount = 0;
            foreach (int count in sellItems.Values)
            {
                totalCount += Math.Max(0, count);
            }

            List<string> finalSellSamples = new List<string>();
            foreach (KeyValuePair<uint, int> kv in sellItems.Take(12))
            {
                sellDetailsByNetId.TryGetValue(kv.Key, out string detail);
                finalSellSamples.Add("netId=" + kv.Key + " x" + Math.Max(0, kv.Value) + (string.IsNullOrWhiteSpace(detail) ? "" : " " + detail));
            }

            this.autoSellLastMatchSummary = "Selected stacks=" + sellItems.Count + ", matchedStacks=" + matchedStacks + ", bag=" + matchedBagTotal + ", sell=" + totalCount + ", inspected=" + inspected + (selectedStaticId > 0 ? ", staticId=" + selectedStaticId : "") + (reservedKept > 0 ? ", kept=" + reservedKept : "") + (skippedStar > 0 ? ", skipped star=" + skippedStar : "");
            this.autoSellSelectedDetails = finalSellSamples.Count > 0 ? string.Join("; ", finalSellSamples.ToArray()) : "";
            if (finalSellSamples.Count > 0)
            {
                this.autoSellLastMatchSummary += "\nSelected: " + string.Join("; ", finalSellSamples.Take(3).ToArray());
            }
            else if (matchedBagTotal > 0 && totalCount == 0)
            {
                int keep = Mathf.Clamp(this.autoSellReserveCount, 0, 200);
                this.autoSellLastMatchSummary += matchedBagTotal <= keep
                    ? "\nKeep Per Item reserved all matching items. Set Keep Per Item to 0 to sell them."
                    : "\nNo sellable quantity after Keep Per Item (single-count stacks are skipped). Lower Keep or raise Cap.";
            }
            else if (matchedStacks == 0 && skippedStar > 0 && selectedStar > 0)
            {
                this.autoSellLastMatchSummary += "\nAll matches have a different star than the selected " + selectedStar + "*. Click the list cell with the star you want to sell.";
            }
            if (skippedSamples.Count > 0)
            {
                this.autoSellLastMatchSummary += "\nSkipped: " + string.Join("; ", skippedSamples.Take(3).ToArray());
            }
            this.autoSellLastSellDetailsByNetId = new Dictionary<uint, string>(sellDetailsByNetId);
            this.AutoSellLogSellResult(this.autoSellLastMatchSummary.Replace("\n", " "));
            return sellItems;
        }

        private void RememberAutoSellCollectedStaticId(uint netId, int staticId)
        {
            if (netId == 0U || staticId <= 0)
            {
                return;
            }

            this.autoSellCollectedStaticIdsByNetId[netId] = staticId;
        }

        private void MergeAutoSellBagCount(Dictionary<uint, int> bagCountsByNetId, uint netId, int stackCount)
        {
            if (bagCountsByNetId == null || netId == 0U)
            {
                return;
            }

            int count = Math.Max(1, stackCount);
            if (bagCountsByNetId.TryGetValue(netId, out int existing))
            {
                bagCountsByNetId[netId] = Math.Max(existing, count);
            }
            else
            {
                bagCountsByNetId[netId] = count;
            }
        }

        private int ApplyAutoSellReserve(Dictionary<uint, int> sellItems, Dictionary<uint, string> reserveGroupsByNetId, Dictionary<uint, int> bagCountsByNetId)
        {
            sellItems?.Clear();
            if (sellItems == null || bagCountsByNetId == null || bagCountsByNetId.Count == 0)
            {
                return 0;
            }

            int reserve = Mathf.Clamp(this.autoSellReserveCount, 0, 200);
            Dictionary<string, List<uint>> groupedNetIds = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<uint, int> kv in bagCountsByNetId)
            {
                if (kv.Key == 0U || kv.Value <= 0)
                {
                    continue;
                }

                string groupKey = null;
                if (reserveGroupsByNetId != null)
                {
                    reserveGroupsByNetId.TryGetValue(kv.Key, out groupKey);
                }

                if (string.IsNullOrWhiteSpace(groupKey))
                {
                    groupKey = "net:" + kv.Key;
                }

                if (!groupedNetIds.TryGetValue(groupKey, out List<uint> netIds))
                {
                    netIds = new List<uint>();
                    groupedNetIds[groupKey] = netIds;
                }

                netIds.Add(kv.Key);
            }

            int reservedTotal = 0;
            foreach (List<uint> netIds in groupedNetIds.Values)
            {
                int totalBag = 0;
                for (int i = 0; i < netIds.Count; i++)
                {
                    if (bagCountsByNetId.TryGetValue(netIds[i], out int bagCount))
                    {
                        totalBag += Math.Max(0, bagCount);
                    }
                }

                if (totalBag <= 0)
                {
                    continue;
                }

                int reservedForGroup = Math.Min(reserve, totalBag);
                int sellablePool = Math.Max(0, totalBag - reservedForGroup);
                reservedTotal += reservedForGroup;
                netIds.Sort((uint a, uint b) =>
                {
                    int countA = bagCountsByNetId.TryGetValue(a, out int bagA) ? bagA : 0;
                    int countB = bagCountsByNetId.TryGetValue(b, out int bagB) ? bagB : 0;
                    return countA.CompareTo(countB);
                });

                int remainingKeep = reservedForGroup;
                for (int i = 0; i < netIds.Count && sellablePool > 0; i++)
                {
                    uint netId = netIds[i];
                    if (!bagCountsByNetId.TryGetValue(netId, out int bagCount) || bagCount <= 0)
                    {
                        continue;
                    }

                    int keptFromStack = Math.Min(bagCount, remainingKeep);
                    remainingKeep -= keptFromStack;
                    int sellableFromStack = bagCount - keptFromStack;
                    if (sellableFromStack <= 0)
                    {
                        continue;
                    }

                    int allocateFromStack = Math.Min(sellableFromStack, sellablePool);
                    int sellCount = this.GetAutoSellStackSellCount(allocateFromStack);
                    if (sellCount <= 0)
                    {
                        continue;
                    }

                    sellItems[netId] = sellCount;
                    sellablePool -= allocateFromStack;
                }
            }

            return reservedTotal;
        }

        private void LimitAutoSellToSingleStack(Dictionary<uint, int> sellItems)
        {
            if (sellItems == null || sellItems.Count <= 1)
            {
                return;
            }

            uint bestNetId = 0U;
            int bestSellCount = 0;
            foreach (KeyValuePair<uint, int> kv in sellItems)
            {
                if (bestNetId == 0U || kv.Value > bestSellCount)
                {
                    bestNetId = kv.Key;
                    bestSellCount = kv.Value;
                }
            }

            if (bestNetId == 0U)
            {
                sellItems.Clear();
                return;
            }

            sellItems.Clear();
            sellItems[bestNetId] = bestSellCount;
        }

        private string GetAutoSellReserveGroupKey(string descriptor, int starRate)
        {
            string matchKey = this.ExtractAutoSellMatchKeyFromDescriptor(descriptor);
            if (string.IsNullOrWhiteSpace(matchKey))
            {
                matchKey = this.NormalizeAutoSellMatchKey(descriptor);
            }
            if (string.IsNullOrWhiteSpace(matchKey))
            {
                matchKey = "unknown";
            }

            int normalizedStar = this.NormalizeAutoSellStarRate(starRate);
            return this.NormalizeAutoSellMatchKey(matchKey) + "|star:" + Mathf.Clamp(normalizedStar, 0, 5);
        }

        private int GetAutoSellStackSellCount(int stackCount)
        {
            int count = Math.Max(1, stackCount);
            if (this.autoSellFullStack)
            {
                return count;
            }

            int cap = Mathf.Clamp(this.autoSellMaxPerStack, 0, 200);
            return Math.Min(count, cap);
        }

        // Single sell-scan collector over the live mono backpack/warehouse lists. Matching is by
        // the selection identity (staticId) when set, otherwise by descriptor text (typed key or
        // family mode). Star data is resolved only for already-matched items and only when the
        // selection carries a star — the rest of the bag never pays the star-lookup invoke cost.
        private void CollectDirectSellItemsMono(int selectedStaticId, int selectedStar, string normalizedKey, Dictionary<uint, int> bagCountsByNetId, Dictionary<uint, string> reserveGroupsByNetId, Dictionary<uint, string> sellDetailsByNetId, List<string> skippedSamples, ref int inspected, ref int skippedStar)
        {
            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj) || backPackSystemObj == IntPtr.Zero)
                {
                    this.AutoSellLog("Mono backpack module unavailable.");
                    return;
                }

                IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
                IntPtr getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
                bool getAllItemNeedsStorageType = true;
                if (getAllItemMethod == IntPtr.Zero)
                {
                    getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                    getAllItemNeedsStorageType = false;
                }
                if (getAllItemMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    this.AutoSellLog("Mono GetAllItem unavailable.");
                    return;
                }

                bool sawItems = false;
                foreach (int storageTypeValue in this.GetAutoSellStorageTypeValues())
                {
                    IntPtr exc = IntPtr.Zero;
                    IntPtr itemListObj;
                    unsafe
                    {
                        int storageValue = storageTypeValue;
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = (IntPtr)(&storageValue);
                        itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, getAllItemNeedsStorageType ? (IntPtr)args : IntPtr.Zero, ref exc);
                    }
                    if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    List<IntPtr> items = new List<IntPtr>();
                    List<uint> itemPins = new List<uint>();
                    if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, itemPins) || items.Count == 0)
                    {
                        FreeAuraMonoPins(itemPins);
                        continue;
                    }
                    sawItems = true;

                    try
                    {
                        foreach (IntPtr itemObj in items)
                        {
                            if (itemObj == IntPtr.Zero)
                            {
                                continue;
                            }
                            inspected++;
                            if (!this.TryGetDirectBackpackItemNetId(itemObj, out uint netId) || netId == 0U)
                            {
                                continue;
                            }

                            this.TryGetDirectBackpackItemStaticId(itemObj, out int staticId);
                            string descriptor = null;
                            bool matched;
                            if (selectedStaticId > 0)
                            {
                                matched = staticId == selectedStaticId;
                            }
                            else
                            {
                                descriptor = this.GetDirectBackpackItemDescriptor(itemObj);
                                matched = this.AutoSellDescriptorMatches(descriptor, normalizedKey);
                            }
                            if (!matched)
                            {
                                continue;
                            }

                            this.TryGetDirectBackpackItemCount(itemObj, out int count);
                            count = Math.Max(1, count);
                            if (descriptor == null)
                            {
                                descriptor = this.GetDirectBackpackItemDescriptor(itemObj);
                            }

                            int starRate = 0;
                            if (selectedStar > 0)
                            {
                                this.TryResolveAutoSellMonoItemStar(netId, itemObj, descriptor, count, out starRate);
                                if (starRate != selectedStar)
                                {
                                    skippedStar++;
                                    if (skippedSamples.Count < 6)
                                    {
                                        skippedSamples.Add("skip star=" + starRate + " want=" + selectedStar + " " + descriptor);
                                    }
                                    continue;
                                }
                            }

                            this.MergeAutoSellBagCount(bagCountsByNetId, netId, count);
                            this.RememberAutoSellCollectedStaticId(netId, staticId);
                            reserveGroupsByNetId[netId] = selectedStaticId > 0
                                ? ("sid:" + staticId + "|star:" + starRate)
                                : this.GetAutoSellReserveGroupKey(descriptor, starRate);
                            sellDetailsByNetId[netId] = (starRate > 0 ? starRate + "* " : "") + descriptor;
                        }
                    }
                    finally
                    {
                        FreeAuraMonoPins(itemPins);
                    }
                }

                if (!sawItems)
                {
                    this.AutoSellLog("Mono " + this.GetAutoSellScanSourceLabel().ToLowerInvariant() + " list empty/unreadable.");
                }
            }
            catch (Exception ex)
            {
                this.AutoSellLogSellResult("Mono scan exception: " + ex.Message);
            }
        }

        private string NormalizeAutoSellMatchKey(string value)
        {
            string text = (value ?? string.Empty).Trim().ToLowerInvariant();
            text = text.Replace("ui_item_normal_", string.Empty);
            text = text.Replace("ui_item_special_", string.Empty);
            text = text.Replace("sprite_", string.Empty);
            return text;
        }

        private string GetActiveAutoSellMatchKey()
        {
            string key = this.NormalizeAutoSellMatchKey(this.autoSellItemKey);
            return this.autoSellMatchFamily ? this.GetAutoSellFamilyKey(key) : key;
        }

        private string GetAutoSellFamilyKey(string matchKey)
        {
            string key = this.NormalizeAutoSellMatchKey(matchKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (key.Contains("birdphoto"))
            {
                return key.StartsWith("p_", StringComparison.Ordinal) ? "p_birdphoto" : "birdphoto";
            }

            string trimmed = this.TrimTrailingDigitsAndUnderscores(key);
            if (!string.IsNullOrWhiteSpace(trimmed) && trimmed.Length >= 2)
            {
                return trimmed;
            }

            return key;
        }

        // A selection exists when either the clicked identity (exact mode) or a usable text key
        // (typed key / family mode) is present.
        private bool HasAutoSellSelection()
        {
            if (!this.autoSellMatchFamily && this.autoSellSelectedStaticId > 0)
            {
                return true;
            }

            string key = this.GetActiveAutoSellMatchKey();
            return !string.IsNullOrWhiteSpace(key) && key.Length >= 2;
        }

        // Single star resolver for mono backpack items: QualityComponent by netId first, then the
        // item's own star fields, then the bird-photo fallbacks (UI star cache, step field).
        // starRate is 0 when the item carries no star data.
        private bool TryResolveAutoSellMonoItemStar(uint netId, IntPtr itemObj, string descriptor, int stackCount, out int starRate)
        {
            if (!this.TryGetAutoSellQualityComponentStar(netId, out starRate))
            {
                this.TryGetDirectBackpackItemStarRate(itemObj, out starRate);
            }

            starRate = this.NormalizeAutoSellStarRate(starRate);
            if (starRate <= 0 && this.IsAutoSellBirdPhotoDescriptor(descriptor))
            {
                if (this.TryGetAutoSellCachedUiStar(descriptor, stackCount, out int uiStar))
                {
                    starRate = this.NormalizeAutoSellStarRate(uiStar);
                }
                else
                {
                    int step = this.GetDirectBackpackItemStep(itemObj);
                    if (step >= 1 && step <= 5)
                    {
                        starRate = step;
                    }
                }
            }

            return starRate > 0;
        }

        private bool IsAutoSellBirdPhotoDescriptor(string descriptor)
        {
            return this.NormalizeAutoSellMatchKey(descriptor).Contains("birdphoto");
        }

        private bool TryGetAutoSellQualityComponentStar(uint netId, out int starRate)
        {
            starRate = 0;
            if (netId == 0U)
            {
                return false;
            }

            try
            {
                if (!this.EnsureAutoSellQualityLookupReady())
                {
                    return this.TryGetAutoSellBackpackItemDataStar(netId, out starRate);
                }

                object netIdArg = this.CreateAutoSellNetIdArgument(netId);
                if (netIdArg == null)
                {
                    return false;
                }

                Type qualityType = this.cachedAutoSellTryGetQualityComponentMethod.GetGenericArguments().Length == 0
                    ? null
                    : this.cachedAutoSellTryGetQualityComponentMethod.GetParameters()[1].ParameterType.GetElementType();
                object qualityValue = qualityType != null && qualityType.IsValueType ? Activator.CreateInstance(qualityType) : null;
                object[] args = { netIdArg, qualityValue };
                object result = this.cachedAutoSellTryGetQualityComponentMethod.Invoke(null, args);
                if (!(result is bool ok) || !ok || args.Length < 2 || args[1] == null)
                {
                    return false;
                }

                if (this.TryGetManagedInt32Member(args[1], "Quality", out int quality))
                {
                    starRate = this.NormalizeAutoSellStarRate(quality);
                    return starRate > 0;
                }
            }
            catch (Exception ex)
            {
                this.AutoSellLog("Quality lookup exception: " + ex.GetType().Name + ": " + ex.Message);
            }

            return false;
        }

        private unsafe bool TryGetAutoSellBackpackItemDataStar(uint netId, out int starRate)
        {
            starRate = 0;
            if (netId == 0U
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoObjectGetClass == null
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj) || backPackSystemObj == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetMonoObjectMember(backPackSystemObj, "_itemData", out IntPtr itemDataDictObj) || itemDataDictObj == IntPtr.Zero)
                {
                    return false;
                }

                // Pin the dictionary across the two invokes below: each mono_runtime_invoke
                // allocates (boxed return values), which can trigger a moving-GC pass that would
                // relocate an unpinned dictionary between ContainsKey and get_Item.
                uint itemDataPin = AuraMonoPinNew(itemDataDictObj);
                try
                {
                    IntPtr dictClass = auraMonoObjectGetClass(itemDataDictObj);
                    // Gate with ContainsKey so the indexer below never throws KeyNotFoundException
                    // for absent netIds (this lookup runs per frame and used to spam the invoke
                    // guard). NOTE: deliberately NOT TryGetValue — its out-parameter writes the raw
                    // value into caller storage, which smashes the stack when the dictionary value
                    // is a struct wider than a pointer. ContainsKey + get_Item only ever returns
                    // boxed objects, which is safe for any value type.
                    if (auraMonoObjectUnbox != null)
                    {
                        IntPtr containsKeyMethod = this.FindAuraMonoMethodOnHierarchy(dictClass, "ContainsKey", 1);
                        if (containsKeyMethod != IntPtr.Zero)
                        {
                            IntPtr excContains = IntPtr.Zero;
                            IntPtr* containsArgs = stackalloc IntPtr[1];
                            containsArgs[0] = (IntPtr)(&netId);
                            IntPtr boxedHas = auraMonoRuntimeInvoke(containsKeyMethod, itemDataDictObj, (IntPtr)containsArgs, ref excContains);
                            if (excContains != IntPtr.Zero || boxedHas == IntPtr.Zero)
                            {
                                return false;
                            }

                            IntPtr rawHas = auraMonoObjectUnbox(boxedHas);
                            if (rawHas == IntPtr.Zero || Marshal.ReadByte(rawHas) == 0)
                            {
                                return false;
                            }
                        }
                    }

                    IntPtr getItemMethod = this.FindAuraMonoMethodOnHierarchy(dictClass, "get_Item", 1);
                    if (getItemMethod == IntPtr.Zero)
                    {
                        return false;
                    }

                    IntPtr itemDataObj;
                    IntPtr exc = IntPtr.Zero;
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&netId);
                    itemDataObj = auraMonoRuntimeInvoke(getItemMethod, itemDataDictObj, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || itemDataObj == IntPtr.Zero)
                    {
                        return false;
                    }

                    if (this.TryGetMonoInt32Member(itemDataObj, "starRate", out int rawStar))
                    {
                        starRate = this.NormalizeAutoSellStarRate(rawStar);
                        return starRate > 0;
                    }
                }
                finally
                {
                    AuraMonoPinFree(itemDataPin);
                }
            }
            catch
            {
            }

            starRate = 0;
            return false;
        }

        private bool EnsureAutoSellQualityLookupReady()
        {
            if (this.autoSellQualityLookupResolved)
            {
                return this.cachedAutoSellTryGetQualityComponentMethod != null && this.cachedAutoSellNetIdType != null;
            }

            this.autoSellQualityLookupResolved = true;
            try
            {
                Type dataCenterType = this.FindLoadedType(
                    "XDTDataAndProtocol.ComponentsData.DataCenter",
                    "DataCenter");
                Type qualityType = this.FindLoadedType(
                    "XDT.Scene.Shared.Entity.QualityComponent",
                    "QualityComponent");
                this.cachedAutoSellNetIdType = this.FindLoadedType(
                    "EcsClient.XDT.Scene.Shared.Data.SharedData.NetId",
                    "NetId");

                if (dataCenterType == null || qualityType == null || this.cachedAutoSellNetIdType == null)
                {
                    this.AutoSellLog("Quality lookup unavailable. DataCenter=" + (dataCenterType != null) + " QualityComponent=" + (qualityType != null) + " NetId=" + (this.cachedAutoSellNetIdType != null));
                    return false;
                }

                foreach (MethodInfo method in dataCenterType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method == null || method.Name != "TryGetComponentData" || !method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 2
                        && parameters[0].ParameterType == this.cachedAutoSellNetIdType
                        && parameters[1].ParameterType.IsByRef)
                    {
                        this.cachedAutoSellTryGetQualityComponentMethod = method.MakeGenericMethod(qualityType);
                        break;
                    }
                }

                if (this.cachedAutoSellTryGetQualityComponentMethod == null)
                {
                    this.AutoSellLog("Quality lookup unavailable. TryGetComponentData<T>(NetId, out T) not found.");
                    return false;
                }

                foreach (MethodInfo method in this.cachedAutoSellNetIdType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method != null
                        && method.Name == "op_Implicit"
                        && method.ReturnType == this.cachedAutoSellNetIdType)
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(uint))
                        {
                            this.cachedAutoSellNetIdFromUIntMethod = method;
                            break;
                        }
                    }
                }

                this.AutoSellLog("Quality lookup ready via DataCenter.TryGetComponentData<QualityComponent>.");
                return true;
            }
            catch (Exception ex)
            {
                this.AutoSellLog("Quality lookup setup exception: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private object CreateAutoSellNetIdArgument(uint netId)
        {
            if (this.cachedAutoSellNetIdType == null)
            {
                return null;
            }

            try
            {
                if (this.cachedAutoSellNetIdFromUIntMethod != null)
                {
                    return this.cachedAutoSellNetIdFromUIntMethod.Invoke(null, new object[] { netId });
                }

                object boxed = Activator.CreateInstance(this.cachedAutoSellNetIdType);
                FieldInfo valueField = this.cachedAutoSellNetIdType.GetField("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (valueField != null)
                {
                    valueField.SetValue(boxed, netId);
                    return boxed;
                }
            }
            catch
            {
            }

            return null;
        }

        private int NormalizeAutoSellStarRate(int starRate)
        {
            if (starRate <= 0)
            {
                return 0;
            }
            return Mathf.Clamp(starRate, 1, 5);
        }

        private bool AutoSellDescriptorMatches(string descriptor, string normalizedKey)
        {
            if (string.IsNullOrWhiteSpace(descriptor) || string.IsNullOrWhiteSpace(normalizedKey))
            {
                return false;
            }

            string text = this.NormalizeAutoSellMatchKey(descriptor);
            if (text.Contains(normalizedKey))
            {
                return true;
            }
            if (!normalizedKey.StartsWith("p_", StringComparison.Ordinal) && text.Contains("p_" + normalizedKey))
            {
                return true;
            }
            return false;
        }

        // Logged once when the live sold-record read fails and the cap clamp falls back to sold=0.
        private bool autoSellPeriodSoldFallbackLogged;

        // capOverflowToCoinsCount = token-eligible items BEYOND the per-period token sell cap
        // (TablePeriodCurrencySale.maxSellCount). Up to the cap sells for tokens; the remainder is
        // routed to the COIN batch (coinItems) rather than kept in the bag — the server would
        // otherwise cap/reject the over-limit part of PeriodSellNetworkCommand, so the excess must
        // go through CmdQuickSell instead (user choice 2026-07-12; was "keep in bag"). A split stack
        // ends up in BOTH festivalItems (tokenQty) and coinItems (overflow) under the same netId;
        // the token batch is sent first, leaving exactly `overflow` in the stack for the coin batch.
        private void SplitSellItemsByPeriodCurrency(int currencyTypeId, Dictionary<uint, int> sellItems, out Dictionary<uint, int> festivalItems, out Dictionary<uint, int> coinItems, out int capOverflowToCoinsCount, out string diagnostics)
        {
            festivalItems = new Dictionary<uint, int>();
            coinItems = new Dictionary<uint, int>();
            capOverflowToCoinsCount = 0;
            diagnostics = "festival split unavailable";
            if (sellItems == null || sellItems.Count == 0)
            {
                diagnostics = "no sell stacks";
                return;
            }

            if (currencyTypeId <= 0)
            {
                foreach (KeyValuePair<uint, int> kv in sellItems)
                {
                    if (kv.Key != 0U && kv.Value > 0)
                    {
                        coinItems[kv.Key] = kv.Value;
                    }
                }

                diagnostics = "currency id unresolved -> all coin";
                return;
            }

            HashSet<int> allowedStaticIds = null;
            string tablePath = "allowlist:unavailable";
            if (this.TryGetAllowedPeriodCurrencyStaticIds(currencyTypeId, out allowedStaticIds) && allowedStaticIds.Count > 0)
            {
                tablePath = "allowlist:" + allowedStaticIds.Count;
            }
            else
            {
                allowedStaticIds = null;
            }
            bool useAllowlist = allowedStaticIds != null;

            // Per-period sell caps captured by the same walk that built the allowlist. Sold-so-far
            // is read live (once per sell, lazily on the first capped stack) off the game's own
            // PeriodStallRecordComponent; on read failure clamp from sold=0 (valid for the first
            // sell of a period, still strictly tighter than the old unclamped send).
            this.TryGetPeriodCurrencyMaxSellCounts(currencyTypeId, out Dictionary<int, int> maxSellByEntityId);
            Dictionary<int, int> soldByEntityId = null;
            bool soldResolved = false;
            bool soldReadOk = false;
            Dictionary<int, int> remainingByEntityId = null;

            foreach (KeyValuePair<uint, int> kv in sellItems)
            {
                if (kv.Key == 0U || kv.Value <= 0)
                {
                    continue;
                }
                string descriptor = string.Empty;
                this.autoSellLastSellDetailsByNetId?.TryGetValue(kv.Key, out descriptor);
                this.ClassifyPeriodCurrencyStack(currencyTypeId, kv.Key, descriptor, allowedStaticIds, useAllowlist, out bool isFestival, out int matchedEntityId);
                if (!isFestival)
                {
                    coinItems[kv.Key] = kv.Value;
                    continue;
                }

                int maxSellCount = 0;
                if (matchedEntityId > 0 && maxSellByEntityId != null)
                {
                    maxSellByEntityId.TryGetValue(matchedEntityId, out maxSellCount);
                }

                if (maxSellCount <= 0)
                {
                    // maxSellCount == 0 (or unknown) = unlimited — sell the whole stack for tokens.
                    festivalItems[kv.Key] = kv.Value;
                    continue;
                }

                if (!soldResolved)
                {
                    soldResolved = true;
                    soldByEntityId = new Dictionary<int, int>();
                    soldReadOk = this.TryGetPeriodStallSoldCounts(currencyTypeId, soldByEntityId, out string soldStatus);
                    if (!soldReadOk && !this.autoSellPeriodSoldFallbackLogged)
                    {
                        this.autoSellPeriodSoldFallbackLogged = true;
                        this.AutoSellLogSellResult("token cap: sold-record read unavailable (" + soldStatus + ") - clamping caps from sold=0");
                    }
                }

                int sold = 0;
                if (soldReadOk && soldByEntityId != null)
                {
                    soldByEntityId.TryGetValue(matchedEntityId, out sold);
                }

                // Running remaining per entityId so several stacks that resolve to the SAME
                // table entityId within this one sell can't jointly exceed the cap.
                if (remainingByEntityId == null)
                {
                    remainingByEntityId = new Dictionary<int, int>();
                }
                if (!remainingByEntityId.TryGetValue(matchedEntityId, out int remaining))
                {
                    remaining = Math.Max(0, maxSellCount - Math.Max(0, sold));
                }

                int tokenQty = Math.Min(kv.Value, remaining);
                remainingByEntityId[matchedEntityId] = remaining - tokenQty;
                int overflow = kv.Value - tokenQty;
                if (tokenQty > 0)
                {
                    // PeriodSellNetworkCommand counts are per-stack quantities — a partial count
                    // is exactly how the game itself clamps capped sells.
                    festivalItems[kv.Key] = tokenQty;
                }
                if (overflow > 0)
                {
                    // Over-cap remainder sells for COINS instead of staying in the bag. Same netId
                    // as the token part when the stack is split; the token batch (sent first) drops
                    // the stack to exactly `overflow`, which the coin batch then takes. kv.Key is
                    // unique per stack, so this never collides with a non-festival coin entry.
                    coinItems[kv.Key] = overflow;
                    capOverflowToCoinsCount += overflow;
                }

                this.AutoSellLogSellResult("token cap: entityId=" + matchedEntityId + " max=" + maxSellCount + " sold=" + sold
                    + " remaining=" + remaining + " stack=" + kv.Value + " -> tokenQty=" + tokenQty + " overflow->coins=" + overflow);
            }
            diagnostics = "currency=" + currencyTypeId + " " + tablePath
                + " tokenStacks=" + festivalItems.Count + " coinStacks=" + coinItems.Count
                + (capOverflowToCoinsCount > 0 ? " capOverflow->coins=" + capOverflowToCoinsCount : "");
        }

        private int SumAutoSellItemCounts(Dictionary<uint, int> items)
        {
            if (items == null || items.Count == 0)
            {
                return 0;
            }
            int total = 0;
            foreach (int count in items.Values)
            {
                total += Math.Max(0, count);
            }
            return total;
        }

        private int autoSellFestivalCurrencyCachedId;
        private float autoSellFestivalCurrencyNextProbeAt;

        private bool TryGetFestivalSellCurrencyId(out int currencyId)
        {
            currencyId = 0;
            if (!this.autoSellFestivalTokensEnabled)
            {
                return false;
            }

            // The battle-pass currency resolve walks game systems/tables via AuraMono — far too
            // heavy (and historically crashy with no active festival) to re-run on every sell
            // tick. Probe on a timer: a live festival's currency is stable, and "no festival"
            // doesn't flip often either.
            float now = Time.unscaledTime;
            if (now < this.autoSellFestivalCurrencyNextProbeAt)
            {
                currencyId = this.autoSellFestivalCurrencyCachedId;
                return currencyId > 0;
            }

            bool resolved = this.TryResolveBattlePassPeriodCurrencyId(out currencyId, out string status) && currencyId > 0;
            this.autoSellFestivalCurrencyCachedId = resolved ? currencyId : 0;
            this.autoSellFestivalCurrencyNextProbeAt = now + (resolved ? 300f : 120f);
            this.AutoSellLog("Festival currency probe: " + (resolved ? ("currency=" + currencyId) : "unavailable") + " (" + status + ")");
            return resolved;
        }

        private string FormatAutoSellIdSample(HashSet<int> ids, int maxCount)
        {
            if (ids == null || ids.Count == 0)
            {
                return string.Empty;
            }

            List<int> sample = ids.Take(Mathf.Max(1, maxCount)).ToList();
            return string.Join(",", sample.ConvertAll(x => x.ToString()).ToArray());
        }

        private bool TryExtractStaticIdFromAutoSellText(string text, out int staticId)
        {
            staticId = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string normalized = this.NormalizeAutoSellMatchKey(text);
            int end = normalized.Length;
            int start = end;
            while (start > 0 && char.IsDigit(normalized[start - 1]))
            {
                start--;
            }

            if (start >= end)
            {
                return false;
            }

            return int.TryParse(normalized.Substring(start, end - start), out staticId) && staticId > 0;
        }

        private bool TryResolveStaticIdForSell(uint netId, string descriptor, out int staticId, out string source)
        {
            staticId = 0;
            source = "none";
            if (netId == 0U)
            {
                return false;
            }

            if (this.autoSellCollectedStaticIdsByNetId.TryGetValue(netId, out staticId) && staticId > 0)
            {
                source = "collect";
                return true;
            }

            if (this.autoSellBagItems != null)
            {
                for (int i = 0; i < this.autoSellBagItems.Count; i++)
                {
                    AutoSellBagItemEntry entry = this.autoSellBagItems[i];
                    if (entry != null && entry.NetId == netId && entry.StaticId > 0)
                    {
                        staticId = entry.StaticId;
                        source = "bagCache";
                        this.RememberAutoSellCollectedStaticId(netId, staticId);
                        return true;
                    }
                }
            }

            if (this.directBackpackRuntimeItems != null)
            {
                for (int i = 0; i < this.directBackpackRuntimeItems.Count; i++)
                {
                    DirectBackpackRuntimeItem item = this.directBackpackRuntimeItems[i];
                    if (item == null || item.NetId != netId)
                    {
                        continue;
                    }

                    if (item.StaticId > 0)
                    {
                        staticId = item.StaticId;
                        source = "runtime";
                        this.RememberAutoSellCollectedStaticId(netId, staticId);
                        return true;
                    }

                    if (item.MonoItem != IntPtr.Zero && this.TryGetDirectBackpackItemStaticId(item.MonoItem, out staticId) && staticId > 0)
                    {
                        item.StaticId = staticId;
                        source = "runtimeMono";
                        this.RememberAutoSellCollectedStaticId(netId, staticId);
                        return true;
                    }

                    if (item.ManagedItem != null && this.TryGetManagedInt32Member(item.ManagedItem, "staticId", out staticId) && staticId > 0)
                    {
                        item.StaticId = staticId;
                        source = "runtimeManaged";
                        this.RememberAutoSellCollectedStaticId(netId, staticId);
                        return true;
                    }
                }
            }

            if (this.TryExtractStaticIdFromAutoSellText(descriptor, out staticId))
            {
                source = "descriptor";
                this.RememberAutoSellCollectedStaticId(netId, staticId);
                return true;
            }

            if (this.TryExtractStaticIdFromAutoSellText(this.autoSellItemKey, out staticId))
            {
                source = "matchKey";
                this.RememberAutoSellCollectedStaticId(netId, staticId);
                return true;
            }

            if (this.TryResolveStaticIdFromLiveMonoBackpack(netId, out staticId))
            {
                source = "liveMono";
                this.RememberAutoSellCollectedStaticId(netId, staticId);
                return true;
            }

            return false;
        }

        private bool TryResolveStaticIdFromLiveMonoBackpack(uint targetNetId, out int staticId)
        {
            staticId = 0;
            if (targetNetId == 0U || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj) || backPackSystemObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
                IntPtr getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
                bool getAllItemNeedsStorageType = true;
                if (getAllItemMethod == IntPtr.Zero)
                {
                    getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                    getAllItemNeedsStorageType = false;
                }
                if (getAllItemMethod == IntPtr.Zero)
                {
                    return false;
                }

                foreach (int storageTypeValue in this.GetAutoSellStorageTypeValues())
                {
                    IntPtr exc = IntPtr.Zero;
                    IntPtr itemListObj;
                    unsafe
                    {
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = (IntPtr)(&storageTypeValue);
                        itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, getAllItemNeedsStorageType ? (IntPtr)args : IntPtr.Zero, ref exc);
                    }
                    if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    List<IntPtr> items = new List<IntPtr>();
                    List<uint> itemPins = new List<uint>();
                    if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, itemPins))
                    {
                        FreeAuraMonoPins(itemPins);
                        continue;
                    }

                    try
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            IntPtr itemObj = items[i];
                            if (itemObj == IntPtr.Zero || !this.TryGetDirectBackpackItemNetId(itemObj, out uint netId) || netId != targetNetId)
                            {
                                continue;
                            }

                            if (this.TryGetDirectBackpackItemStaticId(itemObj, out staticId) && staticId > 0)
                            {
                                return true;
                            }
                        }
                    }
                    finally
                    {
                        FreeAuraMonoPins(itemPins);
                    }
                }
            }
            catch (Exception ex)
            {
                this.AutoSellLog("Live mono staticId lookup exception: " + ex.Message);
            }

            return false;
        }

        private bool TryQuickSellItemsBatched(Dictionary<uint, int> itemsToSell, int currencyTypeId = 0)
        {
            if (itemsToSell == null || itemsToSell.Count == 0)
            {
                return false;
            }

            List<KeyValuePair<uint, int>> entries = itemsToSell.Where(x => x.Key != 0U && x.Value > 0).ToList();
            if (entries.Count == 0)
            {
                return false;
            }

            bool anySent = false;
            for (int offset = 0; offset < entries.Count; offset += 100)
            {
                Dictionary<uint, int> batch = new Dictionary<uint, int>();
                int limit = Math.Min(100, entries.Count - offset);
                for (int i = 0; i < limit; i++)
                {
                    KeyValuePair<uint, int> entry = entries[offset + i];
                    batch[entry.Key] = entry.Value;
                }

                bool sentBatch = false;
                if (currencyTypeId > 0)
                {
                    sentBatch = this.TryInvokeQuickSellManaged(batch, currencyTypeId);
                    if (!sentBatch)
                    {
                        sentBatch = this.TryInvokeQuickSellAuraMono(batch, currencyTypeId);
                    }
                }
                else
                {
                    sentBatch = this.TryInvokeQuickSellManaged(batch, 0);
                    if (!sentBatch)
                    {
                        sentBatch = this.TryInvokeQuickSellAuraMono(batch, 0);
                    }
                }

                if (sentBatch)
                {
                    anySent = true;
                    this.AutoSellLogSellResult("QuickSell batch ok offset=" + offset + " size=" + batch.Count + " currency=" + currencyTypeId);
                    continue;
                }
                this.AutoSellLogSellResult("QuickSell batch failed offset=" + offset + " size=" + batch.Count + " currency=" + currencyTypeId);
                return anySent;
            }

            return anySent;
        }

        private static void AutoSellAuraMonoAssemblySearchCallback(IntPtr assembly, IntPtr userData)
        {
            if (autoSellAuraMonoSearchResult != IntPtr.Zero || autoSellAuraMonoSearchHost == null || string.IsNullOrWhiteSpace(autoSellAuraMonoSearchClass))
            {
                return;
            }

            if (auraMonoAssemblyGetImage == null || auraMonoClassFromName == null)
            {
                return;
            }

            IntPtr image = auraMonoAssemblyGetImage(assembly);
            if (image == IntPtr.Zero)
            {
                return;
            }

            try
            {
                IntPtr classPtr = auraMonoClassFromName(image, autoSellAuraMonoSearchNamespace ?? string.Empty, autoSellAuraMonoSearchClass);
                if (classPtr != IntPtr.Zero)
                {
                    autoSellAuraMonoSearchResult = classPtr;
                }
            }
            catch
            {
            }
        }

        private bool TryGetAutoSellAuraMonoTableDataClass(out IntPtr tableDataClass, out string source)
        {
            tableDataClass = IntPtr.Zero;
            source = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || auraMonoClassFromName == null)
            {
                return false;
            }

            string[] imageNames = new[]
            {
                "EcsClient", "EcsClient.dll",
                "XDTDataAndProtocol", "XDTDataAndProtocol.dll",
                "XDTGameSystem", "XDTGameSystem.dll",
                "Client", "Client.dll",
                "Assembly-CSharp", "Assembly-CSharp.dll"
            };

            IntPtr found = this.FindAuraMonoClassByFullName("EcsClient.TableData");
            if (found != IntPtr.Zero)
            {
                tableDataClass = found;
                source = "fullName EcsClient.TableData";
                return true;
            }

            found = this.FindAuraMonoClassByFullName("TableData");
            if (found != IntPtr.Zero)
            {
                tableDataClass = found;
                source = "fullName TableData";
                return true;
            }

            found = this.FindAuraMonoClassInImages("EcsClient", "TableData", imageNames);
            if (found != IntPtr.Zero)
            {
                tableDataClass = found;
                source = "image scan ns=EcsClient TableData";
                return true;
            }

            found = this.FindAuraMonoClassInImages(string.Empty, "TableData", imageNames);
            if (found != IntPtr.Zero)
            {
                tableDataClass = found;
                source = "image scan ns=empty TableData";
                return true;
            }

            found = this.FindAuraMonoClassInAllLoadedImages("TableData", string.Empty);
            if (found != IntPtr.Zero)
            {
                tableDataClass = found;
                source = "assemblyForeach ns=empty TableData";
                return true;
            }

            found = this.FindAuraMonoClassInAllLoadedImages("TableData", "EcsClient");
            if (found != IntPtr.Zero)
            {
                tableDataClass = found;
                source = "assemblyForeach ns=EcsClient TableData";
                return true;
            }

            return false;
        }

        private bool TryReadBattlePassCurrencyFromPeriodTableMono(IntPtr tableDataClass, int periodId, out int currencyId, out string status)
        {
            currencyId = 0;
            status = string.Empty;
            if (tableDataClass == IntPtr.Zero || periodId <= 0)
            {
                status = "invalid table class or periodId";
                return false;
            }

            string[] staticFieldNames = new[]
            {
                "TableBattlePassPeriods", "tableBattlePassPeriods", "_tableBattlePassPeriods",
                "s_tableBattlePassPeriods", "battlePassPeriods", "BattlePassPeriodTable", "BattlePassPeriods"
            };
            for (int i = 0; i < staticFieldNames.Length; i++)
            {
                if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, staticFieldNames[i], out IntPtr tableObj) || tableObj == IntPtr.Zero)
                {
                    continue;
                }

                List<IntPtr> items = new List<IntPtr>(256);
                List<uint> itemPins = new List<uint>();
                if (!this.TryEnumerateAuraMonoCollectionItems(tableObj, items, itemPins) || items.Count == 0)
                {
                    FreeAuraMonoPins(itemPins);
                    continue;
                }

                try
                {
                    for (int j = 0; j < items.Count; j++)
                    {
                        IntPtr itemObj = items[j];
                        if (itemObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        IntPtr valueObj = itemObj;
                        IntPtr keyObj = IntPtr.Zero;
                        if (this.TryGetMonoObjectMember(itemObj, "Value", out IntPtr boxedValue) && boxedValue != IntPtr.Zero)
                        {
                            valueObj = boxedValue;
                        }
                        else if (this.TryGetMonoObjectMember(itemObj, "value", out boxedValue) && boxedValue != IntPtr.Zero)
                        {
                            valueObj = boxedValue;
                        }

                        int rowPeriodId = 0;
                        if (this.TryGetMonoObjectMember(itemObj, "Key", out keyObj) && keyObj != IntPtr.Zero)
                        {
                            this.TryGetMonoInt32Member(keyObj, "m_value", out rowPeriodId);
                            if (rowPeriodId <= 0)
                            {
                                this.TryGetMonoIntMember(keyObj, "m_value", out rowPeriodId);
                            }
                        }

                        if (rowPeriodId <= 0)
                        {
                            this.TryGetMonoInt32Member(valueObj, "id", out rowPeriodId);
                            if (rowPeriodId <= 0)
                            {
                                this.TryGetMonoIntMember(valueObj, "id", out rowPeriodId);
                            }
                            if (rowPeriodId <= 0)
                            {
                                this.TryGetMonoInt32Member(valueObj, "periodId", out rowPeriodId);
                            }
                            if (rowPeriodId <= 0)
                            {
                                this.TryGetMonoIntMember(valueObj, "periodId", out rowPeriodId);
                            }
                        }

                        if (rowPeriodId != periodId)
                        {
                            continue;
                        }

                        this.TryGetMonoInt32Member(valueObj, "currency", out currencyId);
                        if (currencyId <= 0)
                        {
                            this.TryGetMonoIntMember(valueObj, "currency", out currencyId);
                        }

                        if (currencyId > 0)
                        {
                            status = "staticTable field=" + staticFieldNames[i] + " periodId=" + periodId;
                            return true;
                        }
                    }
                }
                finally
                {
                    FreeAuraMonoPins(itemPins);
                }
            }

            status = "period row not found in static table (periodId=" + periodId + ")";
            return false;
        }

        private bool TryResolveBattlePassPeriodCurrencyId(out int currencyId, out string status)
        {
            currencyId = 0;
            status = string.Empty;

            try
            {
                Type battlePassSystemType = this.FindLoadedType(
                    "XDTGameSystem.GameplaySystem.BattlePass.BattlePassSystem",
                    "BattlePassSystem");
                if (battlePassSystemType == null)
                {
                    status = "BattlePassSystem type missing";
                    return this.TryResolveBattlePassPeriodCurrencyIdAura(out currencyId, out status);
                }

                PropertyInfo instanceProperty = this.GetDataModuleInstanceProperty(battlePassSystemType);
                if (instanceProperty == null)
                {
                    status = "DataModule<BattlePassSystem>.Instance missing";
                    return this.TryResolveBattlePassPeriodCurrencyIdAura(out currencyId, out status);
                }

                object battlePassSystem = instanceProperty.GetValue(null, null);
                if (battlePassSystem == null)
                {
                    status = "BattlePassSystem instance null";
                    return this.TryResolveBattlePassPeriodCurrencyIdAura(out currencyId, out status);
                }

                MethodInfo getCurrentPeriodId = battlePassSystemType.GetMethod("GetCurrentPeriodId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (getCurrentPeriodId == null)
                {
                    status = "GetCurrentPeriodId missing";
                    return this.TryResolveBattlePassPeriodCurrencyIdAura(out currencyId, out status);
                }

                int periodId = Convert.ToInt32(getCurrentPeriodId.Invoke(battlePassSystem, null));
                if (periodId <= 0)
                {
                    status = "no active battle pass period";
                    return false;
                }
                if (this.TryResolveBattlePassCurrencyIl2Cpp(periodId, out currencyId, out string il2CppStatus))
                {
                    status = il2CppStatus;
                    return true;
                }

                Type tableDataType = this.FindManagedTableDataType();
                if (tableDataType == null)
                {
                    status = "TableData missing (periodId=" + periodId + ")";
                    return this.TryResolveBattlePassPeriodCurrencyIdAura(out currencyId, out status);
                }

                object periodRow = null;
                MethodInfo[] tableMethods = tableDataType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < tableMethods.Length; i++)
                {
                    MethodInfo method = tableMethods[i];
                    if (method == null || method.Name != "GetBattlePassPeriod")
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 2 && parameters[0].ParameterType == typeof(int) && parameters[1].ParameterType == typeof(bool))
                    {
                        periodRow = method.Invoke(null, new object[] { periodId, true });
                        break;
                    }

                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                    {
                        periodRow = method.Invoke(null, new object[] { periodId });
                        break;
                    }
                }

                if (periodRow == null)
                {
                    status = "GetBattlePassPeriod returned null (periodId=" + periodId + ")";
                    return false;
                }

                if (!this.TryReadObjectInt(periodRow, "currency", out currencyId) || currencyId <= 0)
                {
                    status = "period row has no currency field (periodId=" + periodId + ")";
                    return false;
                }

                status = "periodId=" + periodId + " currency=" + currencyId;
                return true;
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                return this.TryResolveBattlePassPeriodCurrencyIdAura(out currencyId, out status);
            }
        }

        private unsafe bool TryResolveBattlePassCurrencyIl2Cpp(int periodId, out int currencyId, out string status)
        {
            currencyId = 0;
            status = string.Empty;
            if (periodId <= 0)
            {
                status = "invalid periodId";
                return false;
            }

            IntPtr tableClass = this.TryFindIl2CppClass(
                "TableData",
                "EcsClient",
                "Il2CppEcsClient",
                string.Empty);
            if (tableClass == IntPtr.Zero)
            {
                status = "IL2CPP TableData missing (periodId=" + periodId + ")";
                return false;
            }

            IntPtr periodRow = IntPtr.Zero;
            IntPtr exc = IntPtr.Zero;
            IntPtr method1 = IL2CPP.il2cpp_class_get_method_from_name(tableClass, "GetBattlePassPeriod", 1);
            if (method1 != IntPtr.Zero)
            {
                int pid = periodId;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&pid);
                periodRow = IL2CPP.il2cpp_runtime_invoke(method1, IntPtr.Zero, (void**)args, ref exc);
            }

            if (periodRow == IntPtr.Zero)
            {
                IntPtr method2 = IL2CPP.il2cpp_class_get_method_from_name(tableClass, "GetBattlePassPeriod", 2);
                if (method2 != IntPtr.Zero)
                {
                    int pid = periodId;
                    byte needException = 1;
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)(&pid);
                    args[1] = (IntPtr)(&needException);
                    exc = IntPtr.Zero;
                    periodRow = IL2CPP.il2cpp_runtime_invoke(method2, IntPtr.Zero, (void**)args, ref exc);
                }
            }

            if (periodRow == IntPtr.Zero)
            {
                status = "IL2CPP GetBattlePassPeriod null (periodId=" + periodId + ")";
                return false;
            }

            currencyId = TryReadIl2CppInstanceIntFieldStatic(periodRow, "currency");
            if (currencyId <= 0)
            {
                status = "IL2CPP period row currency missing (periodId=" + periodId + ")";
                return false;
            }

            status = "IL2CPP periodId=" + periodId + " currency=" + currencyId;
            return true;
        }

        private bool TryResolveBattlePassPeriodCurrencyIdAura(out int currencyId, out string status)
        {
            currencyId = 0;
            status = "AuraMono BP currency unresolved";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono unavailable";
                return false;
            }

            try
            {
                IntPtr systemImage = this.FindAuraMonoImage(new[] { "XDTGameSystem", "XDTGameSystem.dll" });
                IntPtr battlePassClass = systemImage != IntPtr.Zero
                    ? auraMonoClassFromName(systemImage, "XDTGameSystem.GameplaySystem.BattlePass", "BattlePassSystem")
                    : IntPtr.Zero;
                if (battlePassClass == IntPtr.Zero)
                {
                    battlePassClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGameSystem.GameplaySystem.BattlePass", "BattlePassSystem");
                }

                if (battlePassClass == IntPtr.Zero)
                {
                    status = "AuraMono BattlePassSystem class missing";
                    return false;
                }

                IntPtr getPeriodMethod = this.FindAuraMonoMethodOnHierarchy(battlePassClass, "GetCurrentPeriodId", 0);
                if (getPeriodMethod == IntPtr.Zero)
                {
                    status = "AuraMono GetCurrentPeriodId missing";
                    return false;
                }

                IntPtr battlePassInstance = this.TryGetAuraMonoDataModuleInstance(battlePassClass);
                if (battlePassInstance == IntPtr.Zero)
                {
                    status = "AuraMono BattlePassSystem instance missing";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr periodObj = auraMonoRuntimeInvoke(getPeriodMethod, battlePassInstance, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || periodObj == IntPtr.Zero || auraMonoObjectUnbox == null)
                {
                    status = "AuraMono GetCurrentPeriodId failed";
                    return false;
                }

                int periodId = Marshal.ReadInt32(auraMonoObjectUnbox(periodObj));
                if (periodId <= 0)
                {
                    // No active battle pass/festival period — nothing to resolve. Bail before the
                    // IL2CPP/table fallbacks: the static-table walk can only match a positive id.
                    status = "no active battle pass period";
                    return false;
                }
                if (this.TryResolveBattlePassCurrencyIl2Cpp(periodId, out currencyId, out string il2CppStatus))
                {
                    status = il2CppStatus;
                    return true;
                }

                if (!this.TryGetAutoSellAuraMonoTableDataClass(out IntPtr tableDataClass, out string tableSource))
                {
                    status = "AuraMono TableData missing (periodId=" + periodId + ")";
                    return false;
                }

                IntPtr getBattlePassPeriodMethod2 = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetBattlePassPeriod", 2);
                IntPtr getBattlePassPeriodMethod1 = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetBattlePassPeriod", 1);
                IntPtr periodRowObj = IntPtr.Zero;

                if (getBattlePassPeriodMethod1 != IntPtr.Zero)
                {
                    unsafe
                    {
                        int pid = periodId;
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = (IntPtr)(&pid);
                        exc = IntPtr.Zero;
                        periodRowObj = auraMonoRuntimeInvoke(getBattlePassPeriodMethod1, IntPtr.Zero, (IntPtr)args, ref exc);
                    }
                }

                if (periodRowObj == IntPtr.Zero && getBattlePassPeriodMethod2 != IntPtr.Zero)
                {
                    unsafe
                    {
                        int pid = periodId;
                        byte needException = 1;
                        IntPtr* args = stackalloc IntPtr[2];
                        args[0] = (IntPtr)(&pid);
                        args[1] = (IntPtr)(&needException);
                        exc = IntPtr.Zero;
                        periodRowObj = auraMonoRuntimeInvoke(getBattlePassPeriodMethod2, IntPtr.Zero, (IntPtr)args, ref exc);
                    }
                }

                if (periodRowObj != IntPtr.Zero)
                {
                    this.TryGetMonoInt32Member(periodRowObj, "currency", out currencyId);
                    if (currencyId <= 0)
                    {
                        this.TryGetMonoIntMember(periodRowObj, "currency", out currencyId);
                    }
                }

                if (currencyId <= 0 && this.TryReadBattlePassCurrencyFromPeriodTableMono(tableDataClass, periodId, out currencyId, out string staticStatus))
                {
                    status = "AuraMono " + staticStatus + " via " + tableSource;
                    return true;
                }

                if (currencyId <= 0)
                {
                    status = "AuraMono GetBattlePassPeriod/static table failed (periodId=" + periodId + ", table=" + tableSource + ")";
                    return false;
                }

                status = "AuraMono periodId=" + periodId + " currency=" + currencyId + " via " + tableSource;
                return true;
            }
            catch (Exception ex)
            {
                status = "AuraMono " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TryInvokeQuickSellManaged(Dictionary<uint, int> itemsToSell, int currencyTypeId = 0)
        {
            Breadcrumbs.Drop("AutoSell.quicksell.managed", "n=" + (itemsToSell?.Count ?? 0) + " cur=" + currencyTypeId);
            try
            {
                Type recycleType = this.FindLoadedType(
                    "XDTDataAndProtocol.ProtocolService.Recycle.RecycleProtocolManager",
                    "RecycleProtocolManager");
                if (recycleType == null)
                {
                    this.AutoSellLog("RecycleProtocolManager type unavailable.");
                    return false;
                }

                bool useAltCurrency = currencyTypeId > 0;
                if (useAltCurrency)
                {
                    MethodInfo alt = null;
                    foreach (MethodInfo candidate in recycleType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (candidate == null || candidate.Name != "CmdBattlePassSell")
                        {
                            continue;
                        }

                        ParameterInfo[] ps = candidate.GetParameters();
                        if (ps == null || ps.Length != 2)
                        {
                            continue;
                        }

                        Type firstType = ps[0].ParameterType;
                        Type secondType = ps[1].ParameterType;
                        if (firstType != null && firstType.IsEnum && secondType != null && typeof(IDictionary).IsAssignableFrom(secondType))
                        {
                            alt = candidate;
                            break;
                        }
                    }

                    if (alt == null)
                    {
                        this.AutoSellLog("CmdBattlePassSell method unavailable.");
                        return false;
                    }

                    Type currencyEnumType = alt.GetParameters()[0].ParameterType;
                    object currencyValue = Enum.ToObject(currencyEnumType, currencyTypeId);
                    alt.Invoke(null, new object[] { currencyValue, itemsToSell });
                    this.AutoSellLog("CmdBattlePassSell sent. currency=" + currencyTypeId + " stacks=" + itemsToSell.Count);
                    return true;
                }

                MethodInfo method = recycleType.GetMethod("CmdQuickSell", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Dictionary<uint, int>) }, null);
                if (method == null)
                {
                    foreach (MethodInfo candidate in recycleType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (candidate != null && candidate.Name == "CmdQuickSell" && candidate.GetParameters().Length == 1)
                        {
                            method = candidate;
                            break;
                        }
                    }
                }
                if (method == null)
                {
                    this.AutoSellLog("CmdQuickSell method unavailable.");
                    return false;
                }

                method.Invoke(null, new object[] { itemsToSell });
                this.AutoSellLog("CmdQuickSell sent. stacks=" + itemsToSell.Count);
                return true;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                this.AutoSellLog("Managed sell command exception: " + inner.GetType().Name + ": " + inner.Message);
                return false;
            }
        }

        private bool TryInvokeQuickSellAuraMono(Dictionary<uint, int> itemsToSell, int currencyTypeId = 0)
        {
            try
            {
                if (itemsToSell == null || itemsToSell.Count == 0)
                {
                    return false;
                }

                Breadcrumbs.Drop("AutoSell.quicksell.aura", "n=" + itemsToSell.Count + " cur=" + currencyTypeId);

                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    this.AutoSellLog("AuraMono QuickSell unavailable.");
                    return false;
                }

                this.ResolveAuraFarmRuntimeMethodsViaMono();

                if (!this.TryCreateAuraMonoUIntIntDictionary(itemsToSell, out IntPtr dictObj, out uint dictPin) || dictObj == IntPtr.Zero)
                {
                    this.AutoSellLog("AuraMono sell dictionary creation failed.");
                    return false;
                }

                try
                {
                    bool useAltCurrency = currencyTypeId > 0;
                    if (useAltCurrency)
                    {
                        if (!this.TryResolveAuraMonoBattlePassSellMethod(out IntPtr altMethod) || altMethod == IntPtr.Zero)
                        {
                            this.AutoSellLog("AuraMono CmdBattlePassSell method unavailable.");
                            return false;
                        }

                        unsafe
                        {
                            int currency = currencyTypeId;
                            IntPtr exc = IntPtr.Zero;
                            IntPtr* args = stackalloc IntPtr[2];
                            args[0] = (IntPtr)(&currency);
                            args[1] = dictObj;
                            auraMonoRuntimeInvoke(altMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                            if (exc != IntPtr.Zero)
                            {
                                this.AutoSellLog("AuraMono CmdBattlePassSell raised exception ptr=0x" + exc.ToInt64().ToString("X"));
                                return false;
                            }
                        }

                        this.AutoSellLog("AuraMono CmdBattlePassSell sent. currency=" + currencyTypeId + " stacks=" + itemsToSell.Count);
                        return true;
                    }

                    if (!this.TryResolveAuraMonoQuickSellMethod(out IntPtr methodPtr) || methodPtr == IntPtr.Zero)
                    {
                        this.AutoSellLog("AuraMono CmdQuickSell method unavailable.");
                        return false;
                    }

                    unsafe
                    {
                        IntPtr exc = IntPtr.Zero;
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = dictObj;
                        auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                        if (exc != IntPtr.Zero)
                        {
                            this.AutoSellLog("AuraMono CmdQuickSell raised exception ptr=0x" + exc.ToInt64().ToString("X"));
                            return false;
                        }
                    }

                    this.AutoSellLog("AuraMono CmdQuickSell sent. stacks=" + itemsToSell.Count);
                    return true;
                }
                finally
                {
                    AuraMonoPinFree(dictPin);
                }
            }
            catch (Exception ex)
            {
                this.AutoSellLog("AuraMono CmdQuickSell exception: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private bool TryResolveAuraMonoQuickSellMethod(out IntPtr methodPtr)
        {
            methodPtr = this.autoSellMonoQuickSellMethod;
            if (methodPtr != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null)
            {
                return false;
            }

            IntPtr dataImage = this.FindAuraMonoImage(new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll", "Client", "Client.dll" });
            IntPtr protocolClass = dataImage != IntPtr.Zero
                ? auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.Recycle", "RecycleProtocolManager")
                : IntPtr.Zero;
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.Recycle", "RecycleProtocolManager");
            }
            if (protocolClass == IntPtr.Zero)
            {
                return false;
            }

            methodPtr = this.FindAuraMonoMethodOnHierarchy(protocolClass, "CmdQuickSell", 1);
            if (methodPtr == IntPtr.Zero)
            {
                methodPtr = this.FindAuraMonoMethodOnHierarchy(protocolClass, "QuickSell", 1);
            }
            if (methodPtr == IntPtr.Zero)
            {
                return false;
            }

            this.autoSellMonoQuickSellMethod = methodPtr;
            return true;
        }

        private bool TryResolveAuraMonoMoveBatchBackpackItemsMethod(out IntPtr methodPtr)
        {
            methodPtr = this.transferMonoMoveBatchMethod;
            if (methodPtr != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null)
            {
                return false;
            }

            IntPtr dataImage = this.FindAuraMonoImage(new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll", "Client", "Client.dll" });
            IntPtr protocolClass = dataImage != IntPtr.Zero
                ? auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.BackPack", "BackpackProtocolManager")
                : IntPtr.Zero;
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.BackPack", "BackpackProtocolManager");
            }
            if (protocolClass == IntPtr.Zero)
            {
                return false;
            }

            methodPtr = this.FindAuraMonoMethodOnHierarchy(protocolClass, "MoveBatchBackpackItems", 2);
            if (methodPtr == IntPtr.Zero)
            {
                return false;
            }

            this.transferMonoMoveBatchMethod = methodPtr;
            return true;
        }

        private bool TryInvokeMoveBatchBackpackItemsManaged(Dictionary<uint, int> netIdToCounts, int targetStorageType)
        {
            try
            {
                if (netIdToCounts == null || netIdToCounts.Count == 0)
                {
                    return false;
                }

                if (this.cachedMoveBatchBackpackItemsMethod == null)
                {
                    Type protocolType = this.FindTypeByName("XDTDataAndProtocol.ProtocolService.BackPack.BackpackProtocolManager", "XDTDataAndProtocol.ProtocolService.BackPack", "BackpackProtocolManager")
                        ?? this.FindTypeBySignature("BackpackProtocolManager", "XDTDataAndProtocol", true, false);
                    if (protocolType == null)
                    {
                        return false;
                    }

                    this.cachedMoveBatchBackpackItemsMethod = protocolType.GetMethod("MoveBatchBackpackItems", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Dictionary<uint, int>), typeof(int) }, null);
                    if (this.cachedMoveBatchBackpackItemsMethod == null)
                    {
                        foreach (MethodInfo candidate in protocolType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (candidate != null && candidate.Name == "MoveBatchBackpackItems" && candidate.GetParameters().Length == 2)
                            {
                                this.cachedMoveBatchBackpackItemsMethod = candidate;
                                break;
                            }
                        }
                    }
                }

                if (this.cachedMoveBatchBackpackItemsMethod == null)
                {
                    return false;
                }

                this.cachedMoveBatchBackpackItemsMethod.Invoke(null, new object[] { netIdToCounts, targetStorageType });
                return true;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                ModLogger.Msg("[TRANSFER] Managed MoveBatchBackpackItems exception: " + inner.GetType().Name + ": " + inner.Message);
                return false;
            }
        }

        private bool TryInvokeMoveBatchBackpackItemsAuraMono(Dictionary<uint, int> netIdToCounts, int targetStorageType)
        {
            try
            {
                if (netIdToCounts == null || netIdToCounts.Count == 0)
                {
                    return false;
                }

                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                if (!this.TryCreateAuraMonoUIntIntDictionary(netIdToCounts, out IntPtr dictObj, out uint dictPin) || dictObj == IntPtr.Zero)
                {
                    ModLogger.Msg("[TRANSFER] AuraMono dictionary creation failed.");
                    return false;
                }

                try
                {
                    if (!this.TryResolveAuraMonoMoveBatchBackpackItemsMethod(out IntPtr methodPtr) || methodPtr == IntPtr.Zero)
                    {
                        ModLogger.Msg("[TRANSFER] AuraMono MoveBatchBackpackItems unavailable.");
                        return false;
                    }

                    unsafe
                    {
                        IntPtr exc = IntPtr.Zero;
                        IntPtr* args = stackalloc IntPtr[2];
                        args[0] = dictObj;
                        args[1] = (IntPtr)(&targetStorageType);
                        auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                        if (exc != IntPtr.Zero)
                        {
                            ModLogger.Msg("[TRANSFER] AuraMono MoveBatchBackpackItems exception ptr=0x" + exc.ToInt64().ToString("X"));
                            return false;
                        }
                    }

                    return true;
                }
                finally
                {
                    AuraMonoPinFree(dictPin);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[TRANSFER] AuraMono MoveBatchBackpackItems exception: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private bool TryResolveAuraMonoBattlePassSellMethod(out IntPtr methodPtr)
        {
            methodPtr = this.autoSellMonoBattlePassSellMethod;
            if (methodPtr != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null)
            {
                return false;
            }

            IntPtr dataImage = this.FindAuraMonoImage(new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll", "Client", "Client.dll" });
            IntPtr protocolClass = dataImage != IntPtr.Zero
                ? auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.Recycle", "RecycleProtocolManager")
                : IntPtr.Zero;
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.Recycle", "RecycleProtocolManager");
            }
            if (protocolClass == IntPtr.Zero)
            {
                return false;
            }

            methodPtr = this.FindAuraMonoMethodOnHierarchy(protocolClass, "CmdBattlePassSell", 2);
            if (methodPtr == IntPtr.Zero)
            {
                return false;
            }

            this.autoSellMonoBattlePassSellMethod = methodPtr;
            return true;
        }

        private bool TryFindDirectBackpackItem(string itemKey, bool anyFood, out uint netId)
        {
            netId = 0U;
            float now = Time.unscaledTime;
            string normalizedLookupKey = (itemKey ?? string.Empty).ToLowerInvariant();
            if (now < this.nextDirectBackpackLookupRetryAt
                && anyFood == this.lastDirectBackpackLookupAnyFood
                && string.Equals(normalizedLookupKey, this.lastDirectBackpackLookupKey, StringComparison.Ordinal))
            {
                return false;
            }

            this.lastDirectBackpackMatchedNetId = 0U;
            this.lastDirectBackpackMatchedStaticId = 0;
            this.lastDirectBackpackMatchedEntityType = 0;
            this.lastDirectBackpackMatchedCount = 0;
            bool runtimeSnapshotAvailable;
            if (this.TryFindDirectBackpackItemFromRuntimeSnapshot(itemKey, anyFood, out netId, out runtimeSnapshotAvailable))
            {
                this.lastDirectBackpackLookupKey = string.Empty;
                this.nextDirectBackpackLookupRetryAt = -999f;
                return true;
            }
            if (runtimeSnapshotAvailable)
            {
                this.lastDirectBackpackLookupKey = normalizedLookupKey;
                this.lastDirectBackpackLookupAnyFood = anyFood;
                this.nextDirectBackpackLookupRetryAt = now + DirectBackpackLookupMissBackoff;
                return false;
            }

            if (this.TryFindDirectBackpackItemManaged(itemKey, anyFood, out netId))
            {
                this.lastDirectBackpackLookupKey = string.Empty;
                this.nextDirectBackpackLookupRetryAt = -999f;
                return true;
            }

            if (!DirectBackpackUnsafeAuraMonoFallbackEnabled)
            {
                this.lastDirectBackpackLookupKey = normalizedLookupKey;
                this.lastDirectBackpackLookupAnyFood = anyFood;
                this.nextDirectBackpackLookupRetryAt = now + DirectBackpackLookupMissBackoff;
                return false;
            }

            try
            {
                this.AutoEatRepairLog("[DirectBackpackMono] Item scan start. key=" + (itemKey ?? "") + " anyFood=" + anyFood);
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj) || backPackSystemObj == IntPtr.Zero)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] BackPackSystem module unavailable.");
                    return false;
                }

                IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
                this.AutoEatRepairLog("[DirectBackpackMono] BackPackSystem resolved. class=" + this.GetAuraMonoClassDisplayName(backPackClass) + " obj=0x" + backPackSystemObj.ToString("X"));
                IntPtr getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
                bool getAllItemNeedsStorageType = true;
                if (getAllItemMethod == IntPtr.Zero)
                {
                    getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                    getAllItemNeedsStorageType = false;
                }
                this.AutoEatRepairLog("[DirectBackpackMono] GetAllItem lookup. method=0x" + getAllItemMethod.ToString("X") + " needsStorage=" + getAllItemNeedsStorageType);
                if (getAllItemMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] BackPackSystem.GetAllItem not found.");
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                int storageTypeBackpack = 1;
                IntPtr itemListObj;
                unsafe
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&storageTypeBackpack);
                    itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, getAllItemNeedsStorageType ? (IntPtr)args : IntPtr.Zero, ref exc);
                }
                if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] GetAllItem invocation failed.");
                    return false;
                }

                List<IntPtr> items = new List<IntPtr>();
                List<uint> itemPins = new List<uint>();
                if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, itemPins) || items.Count == 0)
                {
                    FreeAuraMonoPins(itemPins);
                    this.AutoEatRepairLog("[DirectBackpackMono] Backpack item list empty/unreadable.");
                    return false;
                }
                this.AutoEatRepairLog("[DirectBackpackMono] Backpack item list read. count=" + items.Count);

                string normalizedKey = normalizedLookupKey;
                int inspected = 0;
                int sampleCount = 0;
                try
                {
                    foreach (IntPtr itemObj in items)
                    {
                        if (itemObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        inspected++;
                        if (!this.TryGetDirectBackpackItemNetId(itemObj, out uint candidateNetId) || candidateNetId == 0U)
                        {
                            continue;
                        }

                        string descriptor = this.GetDirectBackpackItemDescriptor(itemObj).ToLowerInvariant();
                        if (string.IsNullOrEmpty(descriptor))
                        {
                            continue;
                        }
                        if (sampleCount < 6)
                        {
                            sampleCount++;
                            this.AutoEatRepairLog("[DirectBackpackMono] Item sample " + sampleCount + ": netId=" + candidateNetId + " descriptor=" + descriptor);
                        }

                        bool matches = anyFood
                            ? (descriptor.Contains("food_") || descriptor.Contains("p_food") || descriptor.Contains("ui_item_normal_p_food"))
                            : (!string.IsNullOrEmpty(normalizedKey) && descriptor.Contains(normalizedKey));

                        if (matches)
                        {
                            netId = candidateNetId;
                            this.lastDirectBackpackMatchedNetId = netId;
                            this.TryGetDirectBackpackItemStaticId(itemObj, out this.lastDirectBackpackMatchedStaticId);
                            this.TryGetDirectBackpackItemEntityType(itemObj, out this.lastDirectBackpackMatchedEntityType);
                            this.TryGetDirectBackpackItemCount(itemObj, out this.lastDirectBackpackMatchedCount);
                            this.AutoEatRepairLog("[DirectBackpackMono] Matched item netId=" + netId + " staticId=" + this.lastDirectBackpackMatchedStaticId + " entityType=" + this.lastDirectBackpackMatchedEntityType + " count=" + this.lastDirectBackpackMatchedCount + " descriptor=" + descriptor);
                            this.lastDirectBackpackLookupKey = string.Empty;
                            this.nextDirectBackpackLookupRetryAt = -999f;
                            return true;
                        }
                    }

                    this.AutoEatRepairLog("[DirectBackpackMono] No match. inspected=" + inspected + " key=" + normalizedKey);
                }
                finally
                {
                    FreeAuraMonoPins(itemPins);
                }
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] Item scan exception: " + ex.Message);
            }

            this.lastDirectBackpackLookupKey = normalizedLookupKey;
            this.lastDirectBackpackLookupAnyFood = anyFood;
            this.nextDirectBackpackLookupRetryAt = now + DirectBackpackLookupMissBackoff;
            return false;
        }

        private bool TryFindDirectBackpackItemFromRuntimeSnapshot(string itemKey, bool anyFood, out uint netId, out bool snapshotAvailable)
        {
            netId = 0U;
            snapshotAvailable = false;
            if (!this.TryRefreshDirectBackpackRuntimeSnapshot(false))
            {
                return false;
            }
            snapshotAvailable = true;

            string normalizedKey = (itemKey ?? string.Empty).ToLowerInvariant();
            for (int i = 0; i < this.directBackpackRuntimeItems.Count; i++)
            {
                DirectBackpackRuntimeItem item = this.directBackpackRuntimeItems[i];
                if (item == null || item.NetId == 0U)
                {
                    continue;
                }

                string descriptor = item.Descriptor ?? string.Empty;
                bool matches = anyFood
                    ? (descriptor.Contains("food_") || descriptor.Contains("p_food") || descriptor.Contains("ui_item_normal_p_food"))
                    : (!string.IsNullOrEmpty(normalizedKey) && descriptor.Contains(normalizedKey));

                if (!matches)
                {
                    continue;
                }

                netId = item.NetId;
                this.lastDirectBackpackMatchedNetId = item.NetId;
                this.lastDirectBackpackMatchedStaticId = item.StaticId;
                this.lastDirectBackpackMatchedEntityType = item.EntityType;
                this.lastDirectBackpackMatchedCount = item.Count;
                this.AutoEatRepairLog("[DirectBackpackRuntime] Matched item netId=" + item.NetId + " staticId=" + item.StaticId + " entityType=" + item.EntityType + " count=" + item.Count + " source=" + this.directBackpackRuntimeSnapshotSource + " descriptor=" + descriptor);
                return true;
            }

            return false;
        }

        private bool TryRefreshDirectBackpackRuntimeSnapshot(bool forceRefresh)
        {
            float now = Time.unscaledTime;
            if (!forceRefresh && now < this.nextDirectBackpackSnapshotRetryAt)
            {
                return false;
            }
            float snapshotTtl = this.IsBagAutomationActiveOrQueued()
                ? BusyDirectBackpackRuntimeSnapshotTtl
                : DirectBackpackRuntimeSnapshotTtl;
            if (!forceRefresh
                && this.directBackpackRuntimeItems.Count > 0
                && now - this.directBackpackRuntimeSnapshotAt <= snapshotTtl)
            {
                return true;
            }

            this.ClearDirectBackpackRuntimeItems();
            this.directBackpackRuntimeSnapshotSource = "";
            if (this.TryBuildDirectBackpackRuntimeSnapshotManaged())
            {
                this.directBackpackRuntimeSnapshotAt = now;
                this.directBackpackRuntimeSnapshotSource = "managed";
                this.nextDirectBackpackSnapshotRetryAt = -999f;
                return true;
            }

            this.ClearDirectBackpackRuntimeItems();
            if (DirectBackpackUnsafeAuraMonoFallbackEnabled && this.TryBuildDirectBackpackRuntimeSnapshotAuraMono())
            {
                this.directBackpackRuntimeSnapshotAt = now;
                this.directBackpackRuntimeSnapshotSource = "auraMono";
                this.nextDirectBackpackSnapshotRetryAt = -999f;
                return true;
            }

            this.directBackpackRuntimeSnapshotAt = -999f;
            this.nextDirectBackpackSnapshotRetryAt = now + DirectBackpackSnapshotFailureBackoff;
            return false;
        }

        private bool TryBuildDirectBackpackRuntimeSnapshotManaged()
        {
            try
            {
                if (!this.TryGetDirectBackpackSystem(out object backPackObj, out MethodInfo getAllItem, out Type storageType, out bool getAllItemNeedsStorage))
                {
                    return false;
                }

                object backpackStorage = storageType != null && storageType.IsEnum ? Enum.ToObject(storageType, 1) : (object)1;
                object itemListObj = getAllItemNeedsStorage
                    ? getAllItem.Invoke(backPackObj, new[] { backpackStorage })
                    : getAllItem.Invoke(backPackObj, null);

                IEnumerable items = itemListObj as IEnumerable;
                if (items == null)
                {
                    return false;
                }

                foreach (object item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (!this.TryGetManagedUInt32Member(item, "netId", out uint itemNetId) || itemNetId == 0U)
                    {
                        continue;
                    }

                    DirectBackpackRuntimeItem entry = new DirectBackpackRuntimeItem
                    {
                        NetId = itemNetId,
                        Descriptor = this.GetManagedBackpackItemDescriptor(item).ToLowerInvariant(),
                        ManagedItem = item
                    };
                    this.TryGetManagedInt32Member(item, "staticId", out entry.StaticId);
                    this.TryGetManagedInt32Member(item, "entityType", out entry.EntityType);
                    this.TryGetManagedBackpackItemCount(item, out entry.Count);
                    this.directBackpackRuntimeItems.Add(entry);
                }

                return this.directBackpackRuntimeItems.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryBuildDirectBackpackRuntimeSnapshotAuraMono()
        {
            try
            {
                this.AutoEatRepairLog("[DirectBackpackRuntime] AuraMono snapshot start.");
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj) || backPackSystemObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
                IntPtr getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
                bool getAllItemNeedsStorageType = true;
                if (getAllItemMethod == IntPtr.Zero)
                {
                    getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                    getAllItemNeedsStorageType = false;
                }
                if (getAllItemMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                int storageTypeBackpack = 1;
                IntPtr itemListObj;
                unsafe
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&storageTypeBackpack);
                    itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, getAllItemNeedsStorageType ? (IntPtr)args : IntPtr.Zero, ref exc);
                }
                if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                {
                    return false;
                }

                // Pin every enumerated item the moment it is obtained: the member reads below box
                // values (mono-side allocations), which can trigger a moving SGen collection that
                // relocates the not-yet-processed items in this list. Pins for accepted entries
                // are transferred into the snapshot (freed by ClearDirectBackpackRuntimeItems);
                // pins for skipped items are freed here.
                List<IntPtr> items = new List<IntPtr>();
                List<uint> itemPins = new List<uint>();
                bool enumerated = this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, itemPins);
                try
                {
                    if (!enumerated || items.Count == 0)
                    {
                        return false;
                    }

                    for (int i = 0; i < items.Count; i++)
                    {
                        IntPtr itemObj = items[i];
                        uint itemPin = i < itemPins.Count ? itemPins[i] : 0U;
                        if (itemObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        if (!this.TryGetDirectBackpackItemNetId(itemObj, out uint itemNetId) || itemNetId == 0U)
                        {
                            continue;
                        }

                        DirectBackpackRuntimeItem entry = new DirectBackpackRuntimeItem
                        {
                            NetId = itemNetId,
                            Descriptor = this.GetDirectBackpackItemDescriptor(itemObj).ToLowerInvariant(),
                            MonoItem = itemObj,
                            MonoItemPin = itemPin
                        };
                        if (i < itemPins.Count)
                        {
                            itemPins[i] = 0U; // ownership moved into the snapshot entry
                        }
                        this.TryGetDirectBackpackItemStaticId(itemObj, out entry.StaticId);
                        this.TryGetDirectBackpackItemEntityType(itemObj, out entry.EntityType);
                        this.TryGetDirectBackpackItemCount(itemObj, out entry.Count);
                        this.directBackpackRuntimeItems.Add(entry);
                    }

                    return this.directBackpackRuntimeItems.Count > 0;
                }
                finally
                {
                    FreeAuraMonoPins(itemPins); // releases only the pins not transferred above
                }
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetDirectBackpackItemNetId(IntPtr itemObj, out uint netId)
        {
            return this.TryGetMonoUInt32Member(itemObj, "netId", out netId)
                || this.TryGetMonoUInt32Member(itemObj, "_netId", out netId)
                || this.TryGetMonoUInt32Member(itemObj, "NetId", out netId);
        }

        private bool TryGetDirectBackpackItemStaticId(IntPtr itemObj, out int staticId)
        {
            return this.TryGetMonoInt32Member(itemObj, "staticId", out staticId)
                || this.TryGetMonoInt32Member(itemObj, "_staticId", out staticId)
                || this.TryGetMonoInt32Member(itemObj, "StaticId", out staticId);
        }

        private bool TryGetDirectBackpackItemEntityType(IntPtr itemObj, out int entityType)
        {
            return this.TryGetMonoInt32Member(itemObj, "entityType", out entityType)
                || this.TryGetMonoInt32Member(itemObj, "_entityType", out entityType)
                || this.TryGetMonoInt32Member(itemObj, "EntityType", out entityType);
        }

        private bool TryGetDirectBackpackItemCount(IntPtr itemObj, out int count)
        {
            return this.TryGetMonoInt32Member(itemObj, "count", out count)
                || this.TryGetMonoInt32Member(itemObj, "_count", out count)
                || this.TryGetMonoInt32Member(itemObj, "Count", out count)
                || this.TryGetMonoInt32Member(itemObj, "counterNum", out count)
                || this.TryGetMonoInt32Member(itemObj, "CounterNum", out count);
        }

        private bool TryGetDirectBackpackItemIsLocked(IntPtr itemObj, out bool isLocked)
        {
            isLocked = false;
            if (itemObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryGetMonoBoolMember(itemObj, "isLock", out isLocked)
                || this.TryGetMonoBoolMember(itemObj, "_isLock", out isLocked)
                || this.TryGetMonoBoolMember(itemObj, "IsLock", out isLocked)
                || this.TryGetMonoBoolMember(itemObj, "isLocked", out isLocked)
                || this.TryGetMonoBoolMember(itemObj, "IsLocked", out isLocked);
        }

        private int GetDirectBackpackItemStep(IntPtr itemObj)
        {
            if (itemObj == IntPtr.Zero)
            {
                return 0;
            }

            return this.TryGetMonoInt32Member(itemObj, "step", out int step)
                || this.TryGetMonoInt32Member(itemObj, "_step", out step)
                || this.TryGetMonoInt32Member(itemObj, "Step", out step)
                ? step
                : 0;
        }

        private bool TryGetDirectBackpackItemStarRate(IntPtr itemObj, out int starRate)
        {
            return this.TryGetDirectBackpackItemStarRate(itemObj, out starRate, 0);
        }

        private bool TryGetDirectBackpackItemStarRate(IntPtr itemObj, out int starRate, int depth)
        {
            starRate = 0;
            if (itemObj == IntPtr.Zero || depth > 3)
            {
                return false;
            }

            if (this.TryInvokeAuraMonoZeroArgInt(itemObj, out int runtimeStar, "get_starRate", "GetStarRate"))
            {
                starRate = this.NormalizeAutoSellStarRate(runtimeStar);
                return true;
            }

            string[] intMembers = { "starRate", "_starRate", "StarRate", "star", "_star", "Star", "quality", "_quality", "Quality" };
            foreach (string member in intMembers)
            {
                if (this.TryGetMonoInt32Member(itemObj, member, out int rawStar))
                {
                    starRate = this.NormalizeAutoSellStarRate(rawStar);
                    return true;
                }
            }

            string[] nestedMembers = { "packItem", "_packItem", "item", "_item", "itemData", "_itemData", "baseData", "_baseData", "bagData", "_bagData", "PetFoodItem", "petFoodItem" };
            foreach (string member in nestedMembers)
            {
                if (this.TryGetMonoObjectMember(itemObj, member, out IntPtr nestedObj) && nestedObj != IntPtr.Zero && nestedObj != itemObj)
                {
                    if (this.TryGetDirectBackpackItemStarRate(nestedObj, out starRate, depth + 1))
                    {
                        return true;
                    }
                }
            }

            if (this.TryInvokeAuraMonoZeroArg(itemObj, out IntPtr singleItemObj, "GetItem", "get_Item") && singleItemObj != IntPtr.Zero && singleItemObj != itemObj)
            {
                if (this.TryGetDirectBackpackItemStarRate(singleItemObj, out starRate, depth + 1))
                {
                    return true;
                }
            }

            if (this.TryInvokeAuraMonoZeroArg(itemObj, out IntPtr itemsObj, "GetItems", "get_Items") && itemsObj != IntPtr.Zero && itemsObj != itemObj)
            {
                List<IntPtr> nestedItems = new List<IntPtr>();
                List<uint> nestedPins = new List<uint>();
                if (this.TryEnumerateAuraMonoCollectionItems(itemsObj, nestedItems, nestedPins))
                {
                    try
                    {
                        foreach (IntPtr nestedItem in nestedItems)
                        {
                            if (this.TryGetDirectBackpackItemStarRate(nestedItem, out starRate, depth + 1))
                            {
                                return true;
                            }
                        }
                    }
                    finally
                    {
                        FreeAuraMonoPins(nestedPins);
                    }
                }
            }

            return false;
        }

        private string GetDirectBackpackItemDescriptor(IntPtr itemObj)
        {
            string[] stringMembers = { "icon", "_icon", "Icon", "iconName", "itemIcon", "name", "_name", "itemName", "templateId", "id" };
            List<string> parts = new List<string>();

            foreach (string member in stringMembers)
            {
                if (this.TryGetMonoStringMember(itemObj, member, out string value) && !string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(value);
                }
            }

            string[] nestedMembers = { "data", "_data", "itemData", "_itemData", "staticData", "_staticData", "tableData", "_tableData" };
            foreach (string member in nestedMembers)
            {
                if (this.TryGetMonoObjectMember(itemObj, member, out IntPtr nestedObj) && nestedObj != IntPtr.Zero)
                {
                    foreach (string nestedStringMember in stringMembers)
                    {
                        if (this.TryGetMonoStringMember(nestedObj, nestedStringMember, out string value) && !string.IsNullOrWhiteSpace(value))
                        {
                            parts.Add(value);
                        }
                    }
                }
            }

            return string.Join(" ", parts);
        }

        private unsafe bool TryExecuteDirectBackpackItemFunc(int functionValue, uint netId)
        {
            if (this.TryExecuteDirectBackpackItemFuncManaged(functionValue, netId))
            {
                return true;
            }

            try
            {
                this.AutoEatRepairLog("[DirectBackpackMono] Execute request. function=" + functionValue + " netId=" + netId);

                // Use cached BagModule + method pointers — avoid re-scanning Managers._moduleDic
                // via native Mono API on every single repair/eat trigger (was the FPS drop cause).
                this.cachedAuraMonoBagModuleObj.TryGet(out IntPtr bagModuleObj);
                IntPtr executeMethod = this.cachedAuraMonoBagExecuteMethod;

                if (bagModuleObj == IntPtr.Zero || executeMethod == IntPtr.Zero)
                {
                    if (!this.TryResolveAuraMonoModule("XDTLevelAndEntity.Game.Module.Bag.BagModule", out bagModuleObj) || bagModuleObj == IntPtr.Zero)
                    {
                        this.AutoEatRepairLog("[DirectBackpackMono] BagModule unavailable; trying protocol fallback.");
                        return this.TryExecuteDirectBackpackProtocolFallback(functionValue, netId);
                    }

                    IntPtr bagClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(bagModuleObj) : IntPtr.Zero;
                    this.AutoEatRepairLog("[DirectBackpackMono] BagModule resolved. class=" + this.GetAuraMonoClassDisplayName(bagClass) + " obj=0x" + bagModuleObj.ToString("X"));
                    executeMethod = this.FindAuraMonoMethodOnHierarchy(bagClass, "ExecuteBackpackItemFunc", 3);
                    this.AutoEatRepairLog("[DirectBackpackMono] ExecuteBackpackItemFunc lookup. method=0x" + executeMethod.ToString("X"));
                    if (executeMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                    {
                        this.AutoEatRepairLog("[DirectBackpackMono] BagModule.ExecuteBackpackItemFunc not found; trying protocol fallback.");
                        return this.TryExecuteDirectBackpackProtocolFallback(functionValue, netId);
                    }

                    // Store for subsequent calls.
                    this.cachedAuraMonoBagModuleObj.Set(bagModuleObj);
                    this.cachedAuraMonoBagExecuteMethod = executeMethod;
                }

                if (auraMonoRuntimeInvoke == null)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] auraMonoRuntimeInvoke unavailable.");
                    return this.TryExecuteDirectBackpackProtocolFallback(functionValue, netId);
                }

                int storageTypeBackpack = 1;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&functionValue);
                args[1] = (IntPtr)(&netId);
                args[2] = (IntPtr)(&storageTypeBackpack);

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(executeMethod, bagModuleObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    // Stale module pointer (e.g. after scene reload) — clear cache so next call re-resolves.
                    this.cachedAuraMonoBagModuleObj.Clear();
                    this.cachedAuraMonoBagExecuteMethod = IntPtr.Zero;
                    this.AutoEatRepairLog("[DirectBackpackMono] ExecuteBackpackItemFunc raised exception; clearing cache and trying protocol fallback.");
                    return this.TryExecuteDirectBackpackProtocolFallback(functionValue, netId);
                }

                this.AutoEatRepairLog("[DirectBackpackMono] ExecuteBackpackItemFunc completed.");
                return true;
            }
            catch (Exception ex)
            {
                // Clear cache on exception to prevent reusing a bad pointer.
                this.cachedAuraMonoBagModuleObj.Clear();
                this.cachedAuraMonoBagExecuteMethod = IntPtr.Zero;
                this.AutoEatRepairLog("[DirectBackpackMono] Execute exception: " + ex.Message);
                return this.TryExecuteDirectBackpackProtocolFallback(functionValue, netId);
            }
        }

        private bool TryExecuteDirectBackpackProtocolFallback(int functionValue, uint netId)
        {
            if (functionValue == 112)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] UseBackpackItem protocol fallback disabled for safety after crash. netId=" + netId + " entityType=" + this.lastDirectBackpackMatchedEntityType);
                return false;
            }

            if (functionValue == 113)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol fallback disabled for safety after crash. netId=" + netId + " staticId=" + this.lastDirectBackpackMatchedStaticId);
                return false;
            }

            this.AutoEatRepairLog("[DirectBackpackMono] No protocol fallback for function=" + functionValue);
            return false;
        }

        private unsafe bool TryInvokeUseBackpackItemProtocol(uint netId)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] UseBackpackItem protocol unavailable: Mono API not ready.");
                    return false;
                }

                int entityType = this.lastDirectBackpackMatchedEntityType;
                if (entityType == 0)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] UseBackpackItem protocol unavailable: entityType missing for netId=" + netId);
                    return false;
                }

                IntPtr dataImage = this.FindAuraMonoImage(new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll", "Client", "Client.dll" });
                IntPtr protocolClass = dataImage != IntPtr.Zero ? auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.BackPack", "BackpackProtocolManager") : IntPtr.Zero;
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.BackPack", "BackpackProtocolManager");
                }

                IntPtr method = protocolClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(protocolClass, "UseBackpackItem", 2) : IntPtr.Zero;
                this.AutoEatRepairLog("[DirectBackpackMono] UseBackpackItem protocol lookup. class=0x" + protocolClass.ToString("X") + " method=0x" + method.ToString("X") + " entityType=" + entityType + " netId=" + netId);
                if (method == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&entityType);
                args[1] = (IntPtr)(&netId);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] UseBackpackItem protocol raised exception.");
                    return false;
                }

                this.AutoEatRepairLog("[DirectBackpackMono] UseBackpackItem protocol sent. entityType=" + entityType + " netId=" + netId);
                return true;
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] UseBackpackItem protocol exception: " + ex.Message);
                return false;
            }
        }

        private bool TryFindDirectBackpackItemManaged(string itemKey, bool anyFood, out uint netId)
        {
            netId = 0U;
            try
            {
                if (!this.TryGetDirectBackpackSystem(out object backPackObj, out MethodInfo getAllItem, out Type storageType, out bool getAllItemNeedsStorage))
                {
                    return false;
                }

                object backpackStorage = storageType != null && storageType.IsEnum ? Enum.ToObject(storageType, 1) : (object)1;
                object itemListObj = null;
                if (getAllItemNeedsStorage)
                {
                    itemListObj = getAllItem.Invoke(backPackObj, new[] { backpackStorage });
                }
                else
                {
                    itemListObj = getAllItem.Invoke(backPackObj, null);
                }

                IEnumerable items = itemListObj as IEnumerable;
                if (items == null)
                {
                    this.AutoEatRepairLog("[DirectBackpackManaged] GetAllItem returned unreadable list.");
                    return false;
                }

                string normalizedKey = (itemKey ?? string.Empty).ToLowerInvariant();
                int inspected = 0;
                int samples = 0;
                foreach (object item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    inspected++;
                    if (!this.TryGetManagedUInt32Member(item, "netId", out uint candidateNetId) || candidateNetId == 0U)
                    {
                        continue;
                    }

                    string descriptor = this.GetManagedBackpackItemDescriptor(item).ToLowerInvariant();
                    if (samples < 6)
                    {
                        samples++;
                        this.AutoEatRepairLog("[DirectBackpackManaged] Item sample " + samples + ": netId=" + candidateNetId + " descriptor=" + descriptor);
                    }

                    bool matches = anyFood
                        ? (descriptor.Contains("food_") || descriptor.Contains("p_food") || descriptor.Contains("ui_item_normal_p_food"))
                        : (!string.IsNullOrEmpty(normalizedKey) && descriptor.Contains(normalizedKey));
                    if (matches)
                    {
                        netId = candidateNetId;
                        this.lastDirectBackpackMatchedNetId = netId;
                        this.TryGetManagedInt32Member(item, "staticId", out this.lastDirectBackpackMatchedStaticId);
                        this.TryGetManagedInt32Member(item, "entityType", out this.lastDirectBackpackMatchedEntityType);
                        this.TryGetManagedBackpackItemCount(item, out this.lastDirectBackpackMatchedCount);
                        this.AutoEatRepairLog("[DirectBackpackManaged] Matched item netId=" + netId + " staticId=" + this.lastDirectBackpackMatchedStaticId + " entityType=" + this.lastDirectBackpackMatchedEntityType + " count=" + this.lastDirectBackpackMatchedCount + " descriptor=" + descriptor);
                        return true;
                    }
                }

                this.AutoEatRepairLog("[DirectBackpackManaged] No match. inspected=" + inspected + " key=" + normalizedKey);
                return false;
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[DirectBackpackManaged] Item scan exception: " + ex.GetType().Name + ": " + ex.Message);
                netId = 0U;
                return false;
            }
        }

        private bool TryExecuteDirectBackpackItemFuncManaged(int functionValue, uint netId)
        {
            try
            {
                if (!this.TryGetDirectBagExecutor(out object bagObj, out Type functionType, out Type storageType, out MethodInfo execute))
                {
                    return false;
                }
                object function = Enum.ToObject(functionType, functionValue);
                object backpackStorage = Enum.ToObject(storageType, 1);
                this.AutoEatRepairLog("[DirectBackpackManaged] Execute request. function=" + functionValue + " netId=" + netId);
                execute.Invoke(bagObj, new object[] { function, netId, backpackStorage });
                this.AutoEatRepairLog("[DirectBackpackManaged] ExecuteBackpackItemFunc completed.");
                return true;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                this.AutoEatRepairLog("[DirectBackpackManaged] Execute exception: " + inner.GetType().Name + ": " + inner.Message);
                return false;
            }
        }

        private bool TryGetDirectBackpackSystem(out object backPackObj, out MethodInfo getAllItem, out Type storageType, out bool getAllItemNeedsStorage)
        {
            backPackObj = this.cachedDirectBackpackSystemObj;
            getAllItem = this.cachedDirectBackpackGetAllItemMethod;
            storageType = this.cachedDirectBackpackStorageType;
            getAllItemNeedsStorage = this.cachedDirectBackpackGetAllItemNeedsStorage;
            if (backPackObj != null && getAllItem != null)
            {
                return true;
            }

            Type backPackType = this.FindLoadedType("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", "BackPackSystem");
            if (backPackType == null)
            {
                this.AutoEatRepairLog("[DirectBackpackManaged] BackPackSystem type unavailable.");
                return false;
            }

            if (!this.TryGetManagedModule(backPackType, out backPackObj) || backPackObj == null)
            {
                backPackObj = this.TryGetStaticObjectAcrossHierarchy(backPackType, "Instance", "_instance");
            }
            if (backPackObj == null)
            {
                this.AutoEatRepairLog("[DirectBackpackManaged] BackPackSystem instance unavailable.");
                return false;
            }

            storageType = this.FindLoadedType("EcsClient.XDT.Scene.Shared.Data.StaticPartial.EStorageType", "EStorageType");
            object backpackStorage = storageType != null && storageType.IsEnum ? Enum.ToObject(storageType, 1) : (object)1;
            getAllItem = backPackObj.GetType().GetMethod("GetAllItem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { backpackStorage.GetType() }, null);
            getAllItemNeedsStorage = getAllItem != null;
            if (getAllItem == null)
            {
                getAllItem = backPackObj.GetType().GetMethod("GetAllItem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                getAllItemNeedsStorage = false;
            }
            if (getAllItem == null)
            {
                this.AutoEatRepairLog("[DirectBackpackManaged] GetAllItem method unavailable.");
                return false;
            }

            this.cachedDirectBackpackSystemObj = backPackObj;
            this.cachedDirectBackpackStorageType = storageType;
            this.cachedDirectBackpackGetAllItemMethod = getAllItem;
            this.cachedDirectBackpackGetAllItemNeedsStorage = getAllItemNeedsStorage;
            return true;
        }

        private bool TryGetManagedBackpackItemCount(object item, out int count)
        {
            return this.TryGetManagedInt32Member(item, "count", out count)
                || this.TryGetManagedInt32Member(item, "_count", out count)
                || this.TryGetManagedInt32Member(item, "Count", out count)
                || this.TryGetManagedInt32Member(item, "counterNum", out count)
                || this.TryGetManagedInt32Member(item, "CounterNum", out count);
        }

        private string GetManagedBackpackItemDescriptor(object item)
        {
            string[] members = { "icon", "_icon", "Icon", "iconName", "itemIcon", "name", "_name", "itemName", "templateId", "id", "staticId", "description" };
            List<string> parts = new List<string>();
            foreach (string member in members)
            {
                if (this.TryGetObjectMember(item, member, out object value) && value != null)
                {
                    string text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        parts.Add(text);
                    }
                }
            }

            return string.Join(" ", parts);
        }

        private bool TryGetAutoSellCachedUiStar(string descriptorOrKey, out int starRate)
        {
            return this.TryGetAutoSellCachedUiStar(descriptorOrKey, 0, out starRate);
        }

        private bool TryGetAutoSellCachedUiStar(string descriptorOrKey, int stackCount, out int starRate)
        {
            starRate = 0;
            string key = this.ExtractAutoSellMatchKeyFromDescriptor(descriptorOrKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                key = this.NormalizeAutoSellMatchKey(descriptorOrKey);
            }
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (stackCount > 0)
            {
                string countKey = this.GetAutoSellStarCountCacheKey(key, stackCount);
                if (this.autoSellUiStarByMatchKeyAndCount.TryGetValue(countKey, out int cachedCountStar) && cachedCountStar > 0)
                {
                    starRate = cachedCountStar;
                    return true;
                }

                string countSpriteKey = this.GetAutoSellStarCountCacheKey(this.NormalizeAutoSellMatchKey(this.GetAutoSellSpriteNameFromMatchKey(key)), stackCount);
                if (!string.Equals(countSpriteKey, countKey, StringComparison.OrdinalIgnoreCase)
                    && this.autoSellUiStarByMatchKeyAndCount.TryGetValue(countSpriteKey, out cachedCountStar)
                    && cachedCountStar > 0)
                {
                    starRate = cachedCountStar;
                    return true;
                }
            }

            if (this.autoSellUiStarByMatchKey.TryGetValue(key, out int cachedStar) && cachedStar > 0)
            {
                starRate = cachedStar;
                return true;
            }

            string spriteKey = this.NormalizeAutoSellMatchKey(this.GetAutoSellSpriteNameFromMatchKey(key));
            if (!string.Equals(spriteKey, key, StringComparison.OrdinalIgnoreCase)
                && this.autoSellUiStarByMatchKey.TryGetValue(spriteKey, out cachedStar)
                && cachedStar > 0)
            {
                starRate = cachedStar;
                return true;
            }

            return false;
        }

        private string GetAutoSellStarCountCacheKey(string key, int stackCount)
        {
            return this.NormalizeAutoSellMatchKey(key) + "|x" + Math.Max(1, stackCount);
        }

        private IEnumerable<int> GetAutoSellStorageTypeValues()
        {
            switch (Mathf.Clamp(this.autoSellScanSource, 0, 2))
            {
                case 1:
                    yield return 2;
                    yield break;
                case 2:
                    yield return 1;
                    yield return 2;
                    yield break;
                default:
                    yield return 1;
                    yield break;
            }
        }

        private string GetAutoSellScanSourceLabel()
        {
            return this.autoSellScanSourceLabels[Mathf.Clamp(this.autoSellScanSource, 0, this.autoSellScanSourceLabels.Length - 1)];
        }

        private string GetAutoSellStorageLabel(bool fromBackpack, bool fromWarehouse)
        {
            if (fromBackpack && fromWarehouse)
            {
                return "Both";
            }
            if (fromWarehouse)
            {
                return "Warehouse";
            }
            return "Bag";
        }

        private List<AutoSellBagItemEntry> ScanBackpackForAutoSellItems()
        {
            try
            {
                List<AutoSellBagItemEntry> items = new List<AutoSellBagItemEntry>();
                Dictionary<string, AutoSellBagItemEntry> byKey = new Dictionary<string, AutoSellBagItemEntry>(StringComparer.OrdinalIgnoreCase);
                int inspected = 0;

                // The mono scan is the single path: managed game assemblies are embedded-mono only
                // on this build, and the runtime snapshot hard-crashed in some game states.
                this.CollectAutoSellBackpackEntriesMono(items, byKey, ref inspected);

                items.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
                this.AutoSellLog(this.GetAutoSellScanSourceLabel() + " item list scan found " + items.Count + " unique item(s), inspected=" + inspected + ".");
                return items;
            }
            catch (Exception ex)
            {
                this.AutoSellLog("Scan items exception: " + ex.GetType().Name + ": " + ex.Message);
                return new List<AutoSellBagItemEntry>();
            }
        }

        private void CollectAutoSellBackpackEntriesMono(List<AutoSellBagItemEntry> items, Dictionary<string, AutoSellBagItemEntry> byKey, ref int inspected)
        {
            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj) || backPackSystemObj == IntPtr.Zero)
                {
                    return;
                }

                IntPtr backPackClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(backPackSystemObj) : IntPtr.Zero;
                IntPtr getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
                bool getAllItemNeedsStorageType = true;
                if (getAllItemMethod == IntPtr.Zero)
                {
                    getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                    getAllItemNeedsStorageType = false;
                }
                if (getAllItemMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    return;
                }

                foreach (int storageTypeValue in this.GetAutoSellStorageTypeValues())
                {
                    bool fromBackpack = storageTypeValue == 1;
                    IntPtr exc = IntPtr.Zero;
                    IntPtr itemListObj;
                    unsafe
                    {
                        int storageValue = storageTypeValue;
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = (IntPtr)(&storageValue);
                        itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, getAllItemNeedsStorageType ? (IntPtr)args : IntPtr.Zero, ref exc);
                    }
                    if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    List<IntPtr> backpackItems = new List<IntPtr>();
                    List<uint> itemPins = new List<uint>();
                    if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, backpackItems, itemPins))
                    {
                        FreeAuraMonoPins(itemPins);
                        continue;
                    }

                    try
                    {
                        foreach (IntPtr itemObj in backpackItems)
                        {
                            inspected++;
                            if (itemObj == IntPtr.Zero || !this.TryGetDirectBackpackItemNetId(itemObj, out uint netId) || netId == 0U)
                            {
                                continue;
                            }

                            int count = 1;
                            int staticId = 0;
                            int entityType = 0;
                            this.TryGetDirectBackpackItemCount(itemObj, out count);
                            this.TryGetDirectBackpackItemStaticId(itemObj, out staticId);
                            this.TryGetDirectBackpackItemEntityType(itemObj, out entityType);
                            string descriptor = this.GetDirectBackpackItemDescriptor(itemObj);
                            this.TryResolveAutoSellMonoItemStar(netId, itemObj, descriptor, Math.Max(1, count), out int starRate);
                            this.AddOrMergeAutoSellBackpackEntry(items, byKey, descriptor, netId, count, staticId, entityType, starRate, fromBackpack);
                        }
                    }
                    finally
                    {
                        FreeAuraMonoPins(itemPins);
                    }
                }
            }
            catch (Exception ex)
            {
                this.AutoSellLog("Backpack list mono scan exception: " + ex.Message);
            }
        }

        private void AddOrMergeAutoSellBackpackEntry(List<AutoSellBagItemEntry> items, Dictionary<string, AutoSellBagItemEntry> byKey, string descriptor, uint netId, int count, int staticId, int entityType, int starRate, bool fromBackpack)
        {
            string matchKey = this.ExtractAutoSellMatchKeyFromDescriptor(descriptor);
            if (string.IsNullOrWhiteSpace(matchKey))
            {
                matchKey = staticId > 0 ? staticId.ToString() : netId.ToString();
            }

            int normalizedStar = this.NormalizeAutoSellStarRate(starRate);
            if (normalizedStar <= 0 && this.TryGetAutoSellCachedUiStar(matchKey, count, out int uiStar))
            {
                normalizedStar = uiStar;
            }

            string listKey = matchKey + "|star:" + Mathf.Clamp(normalizedStar, 0, 5);
            if (!byKey.TryGetValue(listKey, out AutoSellBagItemEntry entry))
            {
                entry = new AutoSellBagItemEntry
                {
                    SpriteName = this.GetAutoSellSpriteNameFromMatchKey(matchKey),
                    DisplayName = this.ResolveBagItemDisplayName(matchKey, staticId) + (normalizedStar > 0 ? (" " + normalizedStar + "*") : ""),
                    MatchKey = matchKey,
                    NetId = netId,
                    Count = Math.Max(1, count),
                    StackCount = 1,
                    StaticId = staticId,
                    EntityType = entityType,
                    StarRate = normalizedStar,
                    FromBackpack = fromBackpack,
                    FromWarehouse = !fromBackpack
                };
                this.RememberRadarStaticIdIconMapping(staticId, entry.SpriteName);
                if (entry.StarRate > 0 && entry.StarRate < entry.StarCounts.Length)
                {
                    entry.StarCounts[entry.StarRate] = Math.Max(1, count);
                }
                byKey[listKey] = entry;
                items.Add(entry);
                return;
            }

            entry.Count += Math.Max(1, count);
            entry.StackCount++;
            if (normalizedStar > 0 && normalizedStar < entry.StarCounts.Length)
            {
                entry.StarCounts[normalizedStar] += Math.Max(1, count);
                if (entry.StarRate <= 0)
                {
                    entry.StarRate = normalizedStar;
                }
            }
            if (entry.NetId == 0U) entry.NetId = netId;
            if (entry.StaticId <= 0) entry.StaticId = staticId;
            if (entry.EntityType <= 0) entry.EntityType = entityType;
            if (fromBackpack) entry.FromBackpack = true;
            else entry.FromWarehouse = true;
            this.RememberRadarStaticIdIconMapping(staticId, entry.SpriteName);
        }

        private string ExtractAutoSellMatchKeyFromDescriptor(string descriptor)
        {
            string text = this.NormalizeAutoSellMatchKey(descriptor);
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string[] tokens = text.Split(new[] { ' ', ',', ';', '|', '/', '\\', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string best = tokens.FirstOrDefault(x => x.StartsWith("p_", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(best)) return best;
            best = tokens.FirstOrDefault(x => x.Contains("_birdphoto") || x.Contains("_gather_") || x.Contains("_food_") || x.StartsWith("food_", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(best)) return best;
            best = tokens.FirstOrDefault(x => x.Any(char.IsLetter));
            return best ?? text;
        }

        private string GetAutoSellSpriteNameFromMatchKey(string matchKey)
        {
            string key = this.NormalizeAutoSellMatchKey(matchKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return key.StartsWith("ui_item_", StringComparison.Ordinal) ? key : (key.StartsWith("p_", StringComparison.Ordinal) ? "ui_item_normal_" + key : key);
        }

        private List<string> GetAutoSellItemTextureKeys(AutoSellBagItemEntry entry)
        {
            List<string> keys = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entry == null)
            {
                return keys;
            }

            this.AddAutoSellItemTextureKey(keys, seen, entry.SpriteName);
            this.AddAutoSellItemTextureKey(keys, seen, this.GetAutoSellSpriteNameFromMatchKey(entry.MatchKey));
            this.AddAutoSellItemTextureKey(keys, seen, entry.MatchKey);
            if (entry.StaticId > 0 && this.TryGetRadarStaticIdIconKey(entry.StaticId, out string staticSpriteKey))
            {
                this.AddAutoSellItemTextureKey(keys, seen, staticSpriteKey);
            }

            return keys;
        }

        private void AddAutoSellItemTextureKey(List<string> keys, HashSet<string> seen, string key)
        {
            string normalized = this.NormalizeAutoSellMatchKey(key);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
            {
                return;
            }

            keys.Add(normalized);
        }

        private bool TryGetAutoSellItemTexture(AutoSellBagItemEntry entry, out Texture2D texture)
        {
            texture = null;
            if (entry == null)
            {
                return false;
            }

            foreach (string key in this.GetAutoSellItemTextureKeys(entry))
            {
                if (this.autoSellBagItemTextures.TryGetValue(key, out texture) && texture != null)
                {
                    return true;
                }
            }

            // Not in memory yet: ask the game's asset pipeline directly (request-once, throttled
            // inside). The grid shows initials until the async load lands.
            this.RequestGameItemIconForEntry(entry);
            return false;
        }

        private string GetAutoSellItemInitials(string displayName)
        {
            string text = (displayName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "?";
            }

            string[] parts = text.Split(new[] { ' ', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
            }

            return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }

        private string GetAutoSellStarSummary(AutoSellBagItemEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            if (entry.StarCounts != null)
            {
                for (int i = 1; i < entry.StarCounts.Length; i++)
                {
                    if (entry.StarCounts[i] > 0)
                    {
                        parts.Add(i + "*:" + entry.StarCounts[i]);
                    }
                }
            }
            if (parts.Count > 0)
            {
                return "  stars " + string.Join(" ", parts.ToArray());
            }
            return entry.StarRate > 0 ? ("  " + entry.StarRate + "*") : string.Empty;
        }

        private AutoSellBagItemEntry GetSelectedAutoSellBagItemEntry()
        {
            if (this.autoSellBagItems == null || this.autoSellBagItems.Count == 0)
            {
                return null;
            }

            AutoSellBagItemEntry fallback = null;
            if (!this.autoSellMatchFamily && this.autoSellSelectedStaticId > 0)
            {
                for (int i = 0; i < this.autoSellBagItems.Count; i++)
                {
                    AutoSellBagItemEntry entry = this.autoSellBagItems[i];
                    if (entry == null || entry.StaticId != this.autoSellSelectedStaticId)
                    {
                        continue;
                    }
                    if (Math.Max(0, entry.StarRate) == this.autoSellSelectedStar)
                    {
                        return entry;
                    }
                    if (fallback == null)
                    {
                        fallback = entry;
                    }
                }
                if (fallback != null)
                {
                    return fallback;
                }
            }

            string key = this.GetActiveAutoSellMatchKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            for (int i = 0; i < this.autoSellBagItems.Count; i++)
            {
                AutoSellBagItemEntry entry = this.autoSellBagItems[i];
                string entryKey = entry != null && this.autoSellMatchFamily ? this.GetAutoSellFamilyKey(entry.MatchKey) : (entry != null ? entry.MatchKey : "");
                if (entry != null && string.Equals(entryKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (this.autoSellSelectedStar > 0 && entry.StarRate == this.autoSellSelectedStar)
                    {
                        return entry;
                    }
                    if (fallback == null)
                    {
                        fallback = entry;
                    }
                }
            }

            return fallback;
        }

        private string GetAutoSellItemDisplayName(string spriteName)
        {
            string itemName = this.NormalizeAutoSellMatchKey(spriteName)
                .Replace("p_", "")
                .Replace("gather_", "")
                .Replace("food_", "")
                .Replace("fruit_", "")
                .Replace("_", " ");
            if (string.IsNullOrWhiteSpace(itemName))
            {
                return "Unknown Item";
            }

            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(itemName);
        }

        private bool DoesRuntimeBackpackItemMatchSprite(DirectBackpackRuntimeItem item, string normalizedSprite)
        {
            if (item == null || string.IsNullOrWhiteSpace(normalizedSprite))
            {
                return false;
            }

            string matchKey = this.ExtractAutoSellMatchKeyFromDescriptor(item.Descriptor);
            if (string.IsNullOrWhiteSpace(matchKey))
            {
                matchKey = item.StaticId > 0 ? item.StaticId.ToString() : string.Empty;
            }

            string[] keys =
            {
                this.NormalizeAutoSellMatchKey(matchKey),
                this.NormalizeAutoSellMatchKey(this.GetAutoSellSpriteNameFromMatchKey(matchKey))
            };

            for (int i = 0; i < keys.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(keys[i]) && string.Equals(keys[i], normalizedSprite, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class AutoSellBagItemEntry
        {
            public string SpriteName = string.Empty;
            public string DisplayName = string.Empty;
            public string MatchKey = string.Empty;
            public uint NetId;
            public int Count;
            public int StackCount;
            public int StaticId;
            public int EntityType;
            public int StarRate;
            public int[] StarCounts = new int[6];
            public bool FromBackpack;
            public bool FromWarehouse;
        }

        private sealed class DirectBackpackRuntimeItem
        {
            public uint NetId;
            public int StaticId;
            public int EntityType;
            public int Count;
            public string Descriptor = "";
            public object ManagedItem;
            // MonoItem is only valid while MonoItemPin (pinned gchandle) roots it: the snapshot
            // outlives the building tick, and SGen moves/collects unrooted objects — reading a
            // stale MonoItem hit mono's "GC filler class" fatal assert (the recurring crash).
            public IntPtr MonoItem;
            public uint MonoItemPin;
        }

    }
}
