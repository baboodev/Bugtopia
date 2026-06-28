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
        private float DrawMassCookTab(int startY)
        {
            int num = startY;
            const float left = 40f;
            const float controlWidth = 470f;
            const float buttonHeight = 36f;
            const float rowGap = 10f;
            bool hasCapturedStoves = this.netCookTargets.Count > 0;
            bool hasRecipe = this.netCookRecipeId > 0;
            Color mutedTextColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.78f);

            GUIStyle netCookHeader = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            netCookHeader.normal.textColor = Color.white;

            GUIStyle smallLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
            smallLabelStyle.normal.textColor = mutedTextColor;

            GUIStyle valueLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight };
            valueLabelStyle.normal.textColor = Color.white;

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            statusStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            GUIStyle statLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            statLabelStyle.normal.textColor = mutedTextColor;

            GUIStyle statValueStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            statValueStyle.normal.textColor = Color.white;

            GUIStyle pillStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            pillStyle.normal.textColor = this.netCookEnabled ? new Color(0.45f, 1f, 0.55f) : mutedTextColor;

            GUIStyle dropdownValueStyle = new GUIStyle(GUI.skin.label);
            dropdownValueStyle.fontSize = 12;
            dropdownValueStyle.fontStyle = FontStyle.Bold;
            dropdownValueStyle.alignment = TextAnchor.MiddleLeft;
            dropdownValueStyle.normal.textColor = Color.white;

            GUIStyle dropdownArrowStyle = new GUIStyle(GUI.skin.label);
            dropdownArrowStyle.fontSize = 12;
            dropdownArrowStyle.fontStyle = FontStyle.Bold;
            dropdownArrowStyle.alignment = TextAnchor.MiddleCenter;
            dropdownArrowStyle.normal.textColor = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);

            GUIStyle dropdownOptionStyle = new GUIStyle(GUI.skin.label);
            dropdownOptionStyle.fontSize = 11;
            dropdownOptionStyle.fontStyle = FontStyle.Bold;
            dropdownOptionStyle.alignment = TextAnchor.MiddleLeft;
            dropdownOptionStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            GUIStyle dropdownOptionActiveStyle = new GUIStyle(dropdownOptionStyle);
            dropdownOptionActiveStyle.normal.textColor = Color.white;

            Rect headerRect = new Rect(left, (float)num, controlWidth, 30f);
            GUI.Label(headerRect, "MASS COOK", netCookHeader);
            GUI.Box(new Rect(headerRect.xMax - 86f, headerRect.y + 3f, 86f, 24f), "", this.netCookEnabled ? (this.themeTopTabActiveStyle ?? this.themePrimaryButtonStyle ?? GUI.skin.box) : (this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box));
            GUI.Label(new Rect(headerRect.xMax - 86f, headerRect.y + 3f, 86f, 24f), this.netCookEnabled ? "RUNNING" : "READY", pillStyle);
            num += 42;

            float halfButtonWidth = (controlWidth - rowGap) * 0.5f;
            bool netCookCaptureBusy = this.netCookCaptureInProgress
                || this.netCookCaptureCoroutine != null
                || Time.unscaledTime < this.nextNetCookCaptureAllowedAt;
            GUI.enabled = !netCookCaptureBusy;
            if (GUI.Button(new Rect(left, (float)num, halfButtonWidth, buttonHeight), "Capture Stoves", this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                if (this.TryCaptureNetCookFromCurrentTarget())
                {
                    bool expandingCapture = this.netCookCaptureCoroutine != null;
                    string captureNotice = expandingCapture
                        ? "Expanding stove capture..."
                        : this.netCookStatus;
                    if (string.IsNullOrWhiteSpace(captureNotice))
                    {
                        captureNotice = "Mass cook stoves captured";
                    }
                    this.AddMenuNotification(captureNotice, expandingCapture ? new Color(1f, 0.85f, 0.45f) : new Color(0.45f, 1f, 0.55f));
                }
                else
                {
                    this.AddMenuNotification(this.netCookStatus ?? "Capture failed.", new Color(1f, 0.55f, 0.55f));
                }
            }
            GUI.enabled = true;

            if (GUI.Button(new Rect(left + halfButtonWidth + rowGap, (float)num, halfButtonWidth, buttonHeight), "Reset Capture", this.netCookEnabled ? (this.themeDangerButtonStyle ?? GUI.skin.button) : GUI.skin.button))
            {
                this.ResetNetCookCaptureContext("Captured stoves reset. Capture stoves again.");
                this.AddMenuNotification("Mass cook captured stoves reset", new Color(1f, 0.75f, 0.45f));
            }
            num += 50;

            bool cleanupBusy = this.netCookCleanupCoroutine != null;
            GUI.enabled = !cleanupBusy;
            if (GUI.Button(new Rect(left, (float)num, controlWidth, buttonHeight), "Clean Up Finished Food", this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartNetCookCleanupSweep();
            }
            GUI.enabled = true;
            num += 50;

            bool previousNetCookMiniGameOnly = this.netCookMiniGameOnly;
            bool nextNetCookMiniGameOnly = this.DrawSwitchToggle(new Rect(left, (float)num, controlWidth, 28f), this.netCookMiniGameOnly, "Mini Game Only");
            if (nextNetCookMiniGameOnly != previousNetCookMiniGameOnly)
            {
                this.netCookMiniGameOnly = nextNetCookMiniGameOnly;
                this.netCookRecipeDropdownOpen = false;
                this.netCookStatus = this.netCookMiniGameOnly
                    ? "Mini game only mode enabled. Capture stoves to assist active cooking."
                    : "Mini game only mode disabled. Select a recipe to mass cook.";
                try { this.SaveKeybinds(false); } catch { }
            }
            num += 38;

            if (this.netCookMiniGameOnly)
            {
                const string assistModeDescription = "Handles cooking mini-game prompts and auto-collects finished food. It will not prepare or start cooking.";
                float assistTextWidth = controlWidth - 24f;
                float assistTextHeight = Mathf.Max(32f, statusStyle.CalcHeight(new GUIContent(assistModeDescription), assistTextWidth));
                float assistModeHeight = 36f + assistTextHeight + 12f;
                Rect assistModeRect = new Rect(left, (float)num, controlWidth, assistModeHeight);
                GUI.Box(assistModeRect, "", this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(assistModeRect, 1f);
                GUI.Label(new Rect(assistModeRect.x + 12f, assistModeRect.y + 8f, assistModeRect.width - 24f, 18f), "ASSIST MODE", smallLabelStyle);
                GUI.Label(
                    new Rect(assistModeRect.x + 12f, assistModeRect.y + 28f, assistTextWidth, assistTextHeight),
                    assistModeDescription,
                    statusStyle);
                num += Mathf.CeilToInt(assistModeHeight) + 12;
            }
            else
            {
                this.EnsureNetCookRecipeCache();
                GUI.Label(new Rect(left, (float)num, controlWidth, 18f), "RECIPE", smallLabelStyle);
                num += 20;

                Rect recipeDropdownRect = new Rect(left, (float)num, controlWidth, 36f);
                GUI.Box(recipeDropdownRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(recipeDropdownRect, 1f);
                if (GUI.Button(recipeDropdownRect, "", GUIStyle.none))
                {
                    this.netCookRecipeDropdownOpen = !this.netCookRecipeDropdownOpen;
                }
                GUI.Label(new Rect(recipeDropdownRect.x + 12f, recipeDropdownRect.y + 1f, recipeDropdownRect.width - 34f, recipeDropdownRect.height - 2f), this.GetNetCookSelectedRecipeLabel(), dropdownValueStyle);
                GUI.Label(new Rect(recipeDropdownRect.xMax - 24f, recipeDropdownRect.y + 1f, 16f, recipeDropdownRect.height - 2f), this.netCookRecipeDropdownOpen ? "^" : "v", dropdownArrowStyle);
                num += 46;

                if (this.netCookRecipeDropdownOpen)
                {
                    float panelHeight = 260f;
                    Rect panelRect = new Rect(recipeDropdownRect.x, recipeDropdownRect.yMax + 4f, recipeDropdownRect.width, panelHeight);
                    GUI.Box(panelRect, "", this.themeContentStyle ?? this.themePanelStyle ?? GUI.skin.box);
                    this.DrawCardOutline(panelRect, 1f);

                    Rect searchRect = new Rect(panelRect.x + 8f, panelRect.y + 8f, panelRect.width - 16f, 28f);
                    GUI.Box(searchRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
                    this.DrawCardOutline(searchRect, 1f);
                    GUI.Label(new Rect(searchRect.x + 10f, searchRect.y + 4f, 52f, 20f), "Search", smallLabelStyle);
                    string nextRecipeSearch = GUI.TextField(new Rect(searchRect.x + 66f, searchRect.y + 3f, searchRect.width - 74f, 22f), this.netCookRecipeSearchText ?? "", 64);
                    if (!string.Equals(nextRecipeSearch, this.netCookRecipeSearchText, StringComparison.Ordinal))
                    {
                        this.netCookRecipeSearchText = nextRecipeSearch;
                        this.netCookRecipeScrollPos = Vector2.zero;
                    }

                    List<KeyValuePair<int, string>> visibleNetCookRecipes = this.GetVisibleNetCookRecipeEntries();
                    Rect viewRect = new Rect(panelRect.x + 4f, searchRect.yMax + 8f, panelRect.width - 8f, panelRect.height - 48f);
                    float contentHeight = Mathf.Max(1f, visibleNetCookRecipes.Count * 28f);
                    Rect contentRect = new Rect(0f, 0f, viewRect.width - 18f, contentHeight);
                    this.netCookRecipeScrollPos = GUI.BeginScrollView(viewRect, this.netCookRecipeScrollPos, contentRect);

                    if (visibleNetCookRecipes.Count <= 0)
                    {
                        GUI.Label(new Rect(8f, 6f, contentRect.width - 16f, 22f), "No recipes match your search.", dropdownOptionStyle);
                    }

                    for (int i = 0; i < visibleNetCookRecipes.Count; i++)
                    {
                        KeyValuePair<int, string> recipeEntry = visibleNetCookRecipes[i];
                        Rect optionRect = new Rect(0f, i * 28f, contentRect.width, 24f);
                        bool isSelected = recipeEntry.Key == this.netCookRecipeId;
                        GUI.Box(optionRect, "", isSelected ? (this.themeTopTabActiveStyle ?? this.themePrimaryButtonStyle ?? GUI.skin.box) : (this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box));
                        if (GUI.Button(optionRect, "", GUIStyle.none))
                        {
                            this.netCookRecipeId = recipeEntry.Key;
                            this.netCookRecipeDropdownOpen = false;
                            this.netCookCookQuantity = 1;
                            this.netCookCookQuantityInput = "1";
                            this.nextNetCookMaxRefreshAt = 0f;
                            this.netCookStatus = "Selected recipe: " + recipeEntry.Value;
                        }
                        string recipeDisplayName = string.IsNullOrWhiteSpace(recipeEntry.Value) ? ("Recipe " + recipeEntry.Key) : recipeEntry.Value;
                        GUI.Label(new Rect(optionRect.x + 8f, optionRect.y + 1f, optionRect.width - 16f, optionRect.height - 2f), recipeDisplayName, isSelected ? dropdownOptionActiveStyle : dropdownOptionStyle);
                    }

                    GUI.EndScrollView();
                    num += (int)panelHeight + 8;
                }

                float ingredientHalfWidth = (controlWidth - rowGap) * 0.5f;
                bool previousNetCookMoveIngredients = this.netCookMoveIngredients;
                bool nextNetCookMoveIngredients = this.DrawSwitchToggle(new Rect(left, (float)num, ingredientHalfWidth, 28f), this.netCookMoveIngredients, "Move Ingredients");
                if (nextNetCookMoveIngredients != previousNetCookMoveIngredients)
                {
                    this.netCookMoveIngredients = nextNetCookMoveIngredients;
                    this.nextNetCookMaxRefreshAt = 0f;
                    try { this.SaveKeybinds(false); } catch { }
                }

                bool previousNetCookUseAllIngredients = this.netCookUseAllIngredients;
                bool nextNetCookUseAllIngredients = this.DrawSwitchToggle(new Rect(left + ingredientHalfWidth + rowGap, (float)num, ingredientHalfWidth, 28f), this.netCookUseAllIngredients, "Use All Ingredients");
                if (nextNetCookUseAllIngredients != previousNetCookUseAllIngredients)
                {
                    this.netCookUseAllIngredients = nextNetCookUseAllIngredients;
                    this.nextNetCookMaxRefreshAt = 0f;
                    try { this.SaveKeybinds(false); } catch { }
                }
                num += 38;

                this.RefreshNetCookMaxCookQuantity();
                string maxLabel = this.netCookMaxCookQuantity > 0
                    ? ("Ingredients max: " + this.netCookMaxCookQuantity)
                    : "Ingredients max: —";

                GUI.Label(new Rect(left, (float)num, controlWidth * 0.42f, 18f), "DISH LIMIT (0 = unlimited)", smallLabelStyle);
                GUI.Label(new Rect(left + controlWidth * 0.58f, (float)num, controlWidth * 0.42f, 18f), maxLabel, valueLabelStyle);
                num += 20;

                Rect qtyRect = new Rect(left, (float)num, controlWidth * 0.42f, 32f);
                GUI.Box(qtyRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(qtyRect, 1f);
                string nextQtyInput = GUI.TextField(new Rect(qtyRect.x + 10f, qtyRect.y + 5f, qtyRect.width - 20f, 22f), this.netCookCookQuantityInput ?? "1", 6);
                if (!string.Equals(nextQtyInput, this.netCookCookQuantityInput, StringComparison.Ordinal))
                {
                    this.netCookCookQuantityInput = nextQtyInput;
                    this.SyncNetCookCookQuantityFromInput();
                }
                num += 42;
            }

            string netCookButtonText = this.netCookEnabled
                ? (this.netCookMiniGameOnly ? "STOP MINI GAME ASSIST" : "STOP MASS COOK")
                : (this.netCookMiniGameOnly ? "START MINI GAME ASSIST" : "START MASS COOK");
            if (GUI.Button(new Rect(left, (float)num, controlWidth, buttonHeight + 2f), netCookButtonText, this.netCookEnabled ? (this.themeDangerButtonStyle ?? GUI.skin.button) : (this.themePrimaryButtonStyle ?? GUI.skin.button)))
            {
                if (this.netCookEnabled)
                {
                    this.StopNetCookInternal("Disabled");
                }
                else
                {
                    this.StartNetCookInternal();
                }
            }
            num += 52;

            Rect settingsRect = new Rect(left, (float)num, controlWidth, 112f);
            GUI.Box(settingsRect, "", this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(settingsRect, 1f);

            float settingsX = settingsRect.x + 12f;
            float settingsWidth = settingsRect.width - 24f;
            float settingsY = settingsRect.y + 10f;
            GUI.Label(new Rect(settingsX, settingsY, settingsWidth * 0.55f, 18f), "COOK DELAY", smallLabelStyle);
            GUI.Label(new Rect(settingsX + settingsWidth * 0.55f, settingsY, settingsWidth * 0.45f, 18f), $"{this.netCookInterval:F2}s", valueLabelStyle);
            settingsY += 20f;
            float prevNetCookInterval = this.netCookInterval;
            this.netCookInterval = Mathf.Round(this.DrawAccentSlider(new Rect(settingsX, settingsY, settingsWidth, 20f), this.netCookInterval, 0.25f, 10f) * 100f) / 100f;
            if (Math.Abs(this.netCookInterval - prevNetCookInterval) > 0.0001f) { try { this.SaveKeybinds(false); } catch { } }
            settingsY += 38f;

            GUI.Label(new Rect(settingsX, settingsY, settingsWidth * 0.55f, 18f), "SCAN RADIUS", smallLabelStyle);
            GUI.Label(new Rect(settingsX + settingsWidth * 0.55f, settingsY, settingsWidth * 0.45f, 18f), $"{this.netCookScanRadiusMeters:F0}m", valueLabelStyle);
            settingsY += 20f;
            float prevNetCookScanRadius = this.netCookScanRadiusMeters;
            this.netCookScanRadiusMeters = Mathf.Round(this.DrawAccentSlider(new Rect(settingsX, settingsY, settingsWidth, 20f), this.netCookScanRadiusMeters, NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters));
            if (Math.Abs(this.netCookScanRadiusMeters - prevNetCookScanRadius) > 0.0001f)
            {
                this.netCookStatus = $"Scan radius set to {this.netCookScanRadiusMeters:F0}m. Capture stoves again to refresh targets.";
                try { this.SaveKeybinds(false); } catch { }
            }
            num += 126;

            Rect statusPanelRect = new Rect(left, (float)num, controlWidth, 118f);
            GUI.Box(statusPanelRect, "", this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(statusPanelRect, 1f);

            GUI.Label(new Rect(statusPanelRect.x + 12f, statusPanelRect.y + 8f, statusPanelRect.width - 24f, 18f), "STATUS", smallLabelStyle);

            float statTop = statusPanelRect.y + 32f;
            float statWidth = (statusPanelRect.width - 36f) / 2f;
            Rect statRect = new Rect(statusPanelRect.x + 12f, statTop, statWidth, 42f);
            GUI.Box(statRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
            GUI.Label(new Rect(statRect.x, statRect.y + 4f, statRect.width, 16f), "STOVES", statLabelStyle);
            GUI.Label(new Rect(statRect.x, statRect.y + 20f, statRect.width, 18f), this.netCookTargets.Count.ToString(), statValueStyle);

            statRect.x += statWidth + 12f;
            GUI.Box(statRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
            GUI.Label(new Rect(statRect.x, statRect.y + 4f, statRect.width, 16f), "SENT", statLabelStyle);
            GUI.Label(new Rect(statRect.x, statRect.y + 20f, statRect.width, 18f), this.netCookSentCount.ToString(), statValueStyle);

            string readiness = this.netCookMiniGameOnly
                ? (hasCapturedStoves ? "Ready to assist active cooking mini-games." : "Capture stoves to begin assisting.")
                : (hasCapturedStoves ? (hasRecipe ? "Ready to cook." : "Select a recipe to continue.") : "Capture stoves to begin.");
            string currentStatus = string.IsNullOrWhiteSpace(this.netCookStatus) ? readiness : this.netCookStatus;
            GUI.Label(new Rect(statusPanelRect.x + 12f, statusPanelRect.y + 82f, statusPanelRect.width - 24f, 28f), currentStatus, statusStyle);
            num += 132;

            return (float)num + 20f;
        }

        private void StartNetCookInternal()
        {
            if (this.netCookStartCoroutine != null)
            {
                this.netCookStatus = "Preparing mass cook after ingredient move...";
                this.AddMenuNotification(this.netCookStatus, new Color(1f, 0.85f, 0.45f));
                return;
            }

            if (this.netCookCaptureCoroutine != null)
            {
                this.netCookStatus = "Still expanding stove capture. Please wait...";
                this.AddMenuNotification(this.netCookStatus, new Color(1f, 0.85f, 0.45f));
                return;
            }

            if (!this.HasNetCookContext() && !this.TryCaptureNetCookFromCurrentTarget())
            {
                if (string.IsNullOrWhiteSpace(this.netCookStatus))
                {
                    this.netCookStatus = "Capture a cooker target first.";
                }
                this.AddMenuNotification(this.netCookStatus, new Color(1f, 0.55f, 0.55f));
                return;
            }

            if (this.netCookMiniGameOnly)
            {
                if (!this.EnsureNetCookAssistTargets(out string assistStatus))
                {
                    this.netCookStatus = assistStatus;
                    this.AddMenuNotification(assistStatus, new Color(1f, 0.55f, 0.55f));
                    return;
                }

                if (this.autoCookEnabled)
                {
                    this.StopAutoCookInternal("Mini game assist started");
                }

                this.netCookEnabled = true;
                this.netCookDrainAfterIngredientsRunOut = false;
                this.netCookDrainReason = null;
                float assistNow = Time.unscaledTime;
                this.PrimeNetCookTargetsForMiniGame(assistNow);
                this.netCookStatus = "Mini game assist running on " + this.netCookTargets.Count + " stove(s).";
                this.NetCookLog("STARTED mini-game-only cookerStaticId=" + this.netCookCookerStaticId + " targets=" + this.netCookTargets.Count);
                return;
            }

            if (!this.EnsureNetCookTargetsForCurrentRecipe(out string targetStatus))
            {
                this.netCookStatus = targetStatus;
                this.AddMenuNotification(targetStatus, new Color(1f, 0.55f, 0.55f));
                return;
            }

            if (this.netCookRecipeId <= 0)
            {
                this.netCookStatus = "Select a recipe first.";
                this.AddMenuNotification("Select a net cook recipe first", new Color(1f, 0.55f, 0.55f));
                return;
            }

            if (!this.IsNetCookRecipeCompatibleWithCurrentCooker(out string compatibilityStatus))
            {
                this.netCookStatus = compatibilityStatus;
                this.AddMenuNotification(compatibilityStatus, new Color(1f, 0.55f, 0.55f));
                return;
            }

            bool deferredStartAfterWarehouseMove = false;
            if (this.netCookMoveIngredients)
            {
                this.SyncNetCookCookQuantityFromInput();
                this.RefreshNetCookMaxCookQuantity(true);
                int moveCookQuantity = this.GetNetCookWarehouseMoveBatchCount();
                if (!this.TryMoveNetCookIngredientsFromWarehouse(this.netCookUseAllIngredients, moveCookQuantity, out string moveStatus))
                {
                    this.netCookStatus = moveStatus;
                    this.AddMenuNotification(moveStatus, new Color(1f, 0.55f, 0.55f));
                    return;
                }

                if (!string.IsNullOrWhiteSpace(moveStatus)
                    && moveStatus.IndexOf("Moved ", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.AddMenuNotification(moveStatus, new Color(0.45f, 1f, 0.55f));
                    deferredStartAfterWarehouseMove = true;
                }

                this.TryInvokeNetCookRefreshSlots();
            }

            if (deferredStartAfterWarehouseMove)
            {
                this.netCookStatus = "Waiting for ingredients in bag...";
                this.netCookStartCoroutine = ModCoroutines.Start(this.NetCookStartAfterWarehouseMoveRoutine());
                return;
            }

            if (!this.TryCompleteNetCookStart(out string startStatus))
            {
                this.netCookStatus = startStatus;
                this.AddMenuNotification(startStatus, new Color(1f, 0.55f, 0.55f));
            }
        }

        private bool TryCompleteNetCookStart(out string status)
        {
            status = string.Empty;
            if (!this.TryBuildNetCookMaterials(this.netCookRecipeId, out List<uint> previewMaterials, out string previewStatus))
            {
                status = previewStatus;
                return false;
            }

            if (this.autoCookEnabled)
            {
                this.StopAutoCookInternal("Net cook started");
            }

            this.netCookEnabled = true;
            this.netCookDrainAfterIngredientsRunOut = false;
            this.netCookDrainReason = null;
            this.netCookCompletedDishCount = 0;
            this.netCookCommittedDishCount = 0;
            float now = Time.unscaledTime;
            this.PrimeNetCookTargetsForStart(now);
            this.SeedNetCookCommittedDishCountFromActiveTargets();
            this.netCookMaterialNetIds.Clear();
            this.netCookMaterialNetIds.AddRange(previewMaterials);
            this.netCookStatus = "Net mass cook running on " + this.netCookTargets.Count + " stove(s).";
            this.NetCookLog("STARTED recipe=" + this.netCookRecipeId + " cookerStaticId=" + this.netCookCookerStaticId + " targets=" + this.netCookTargets.Count + " materials=" + this.netCookMaterialNetIds.Count);
            return true;
        }

        private System.Collections.IEnumerator NetCookStartAfterWarehouseMoveRoutine()
        {
            string lastStatus = "Ingredients not ready after warehouse move.";
            float deadline = Time.unscaledTime + NetCookPostMoveMaterialRetrySeconds;
            try
            {
                while (Time.unscaledTime < deadline)
                {
                    yield return null;
                    this.TryInvokeNetCookRefreshSlots();
                    if (this.TryCompleteNetCookStart(out string startStatus))
                    {
                        this.netCookStartCoroutine = null;
                        yield break;
                    }

                    lastStatus = startStatus;
                    float waitUntil = Time.unscaledTime + NetCookPostMoveMaterialRetryIntervalSeconds;
                    while (Time.unscaledTime < waitUntil)
                    {
                        yield return null;
                    }
                }

                this.netCookStatus = lastStatus;
                this.AddMenuNotification(lastStatus, new Color(1f, 0.55f, 0.55f));
                this.NetCookLog("Deferred mass cook start failed: " + lastStatus);
            }
            finally
            {
                this.netCookStartCoroutine = null;
            }
        }

        private void PrimeNetCookTargetsForStart(float now)
        {
            float targetStagger = NetCookMinTargetStaggerSeconds;
            bool hasInProgressCooking = false;
            for (int i = 0; i < this.netCookTargets.Count; i++)
            {
                NetCookTargetContext target = this.netCookTargets[i];
                target.ContinuePulses = 0;
                target.IdleRetries = 0;
                target.LastCookCommandAt = -999f;
                target.NextActionAt = now + (i * targetStagger);

                if (this.TryGetNetCookTargetCookingStatus(target, out int cookingStatus, out int resultRecipeId, out int foodQuality, out _))
                {
                    target.LastStatus = cookingStatus;
                    target.LastStatusActionAt = -999f;
                    target.Phase = cookingStatus == 0 ? 0 : 2;
                    this.NetCookLog("Start priority stove " + target.CookerNetId + " status=" + this.GetNetCookCookingStatusName(cookingStatus) + " result=" + resultRecipeId + " quality=" + foodQuality);
                    if (this.IsNetCookInProgressStatus(cookingStatus))
                    {
                        hasInProgressCooking = true;
                    }
                }
                else
                {
                    target.Phase = 0;
                    target.LastStatus = -1;
                    target.LastStatusActionAt = -999f;
                }

                this.netCookTargets[i] = target;
            }

            if (!hasInProgressCooking)
            {
                for (int i = 0; i < this.netCookTargets.Count; i++)
                {
                    NetCookTargetContext target = this.netCookTargets[i];
                    target.Phase = 0;
                    target.ContinuePulses = 0;
                    target.LastStatus = -1;
                    target.LastStatusActionAt = -999f;
                    target.IdleRetries = 0;
                    target.LastCookCommandAt = -999f;
                    target.NextActionAt = now + (i * targetStagger);
                    this.netCookTargets[i] = target;
                }
                this.NetCookLog("Start priority found no active cooking stoves; starting fresh cook order.");
                return;
            }

            this.netCookTargets.Sort((a, b) =>
            {
                int priorityCompare = this.GetNetCookStartPriority(a).CompareTo(this.GetNetCookStartPriority(b));
                if (priorityCompare != 0)
                {
                    return priorityCompare;
                }

                return a.NextActionAt.CompareTo(b.NextActionAt);
            });

            for (int i = 0; i < this.netCookTargets.Count; i++)
            {
                NetCookTargetContext target = this.netCookTargets[i];
                target.NextActionAt = now + (i * targetStagger);
                this.netCookTargets[i] = target;
            }
        }

        private void PrimeNetCookTargetsForMiniGame(float now)
        {
            float targetStagger = NetCookMinTargetStaggerSeconds;
            for (int i = 0; i < this.netCookTargets.Count; i++)
            {
                NetCookTargetContext target = this.netCookTargets[i];
                target.Phase = 2;
                target.ContinuePulses = 0;
                target.IdleRetries = 0;
                target.LastStatusActionAt = -999f;
                target.LastCookCommandAt = -999f;
                target.NextActionAt = now + (i * targetStagger);

                if (this.TryGetNetCookTargetCookingStatus(target, out int cookingStatus, out int resultRecipeId, out int foodQuality, out _))
                {
                    target.LastStatus = cookingStatus;
                    this.NetCookLog("Mini game prime stove " + target.CookerNetId + " status=" + this.GetNetCookCookingStatusName(cookingStatus) + " result=" + resultRecipeId + " quality=" + foodQuality);
                }
                else
                {
                    target.LastStatus = -1;
                }

                this.netCookTargets[i] = target;
            }
        }

        private int GetNetCookStartPriority(NetCookTargetContext target)
        {
            if (target == null)
            {
                return 99;
            }

            if (target.Phase == 3 || target.LastStatus == 3 || target.LastStatus == 4)
            {
                return 0;
            }

            if (target.LastStatus == 1 || target.LastStatus == 2)
            {
                return 1;
            }

            if (target.LastStatus == 5 || target.LastStatus == 6)
            {
                return 2;
            }

            if (target.LastStatus == 0 || target.Phase == 0)
            {
                return 3;
            }

            return 3;
        }

        private bool IsNetCookInProgressStatus(int cookingStatus)
        {
            return cookingStatus >= 1 && cookingStatus <= 4;
        }

        private void StopNetCookInternal(string reason)
        {
            if (this.netCookStartCoroutine != null)
            {
                ModCoroutines.Stop(this.netCookStartCoroutine);
                this.netCookStartCoroutine = null;
            }

            bool wasEnabled = this.netCookEnabled;
            this.netCookEnabled = false;
            this.netCookDrainAfterIngredientsRunOut = false;
            this.netCookDrainReason = null;
            this.netCookStatus = reason ?? "Stopped";
            if (wasEnabled)
            {
                this.NetCookLog("STOPPED: " + this.netCookStatus);
            }
        }

        private void StartNetCookCleanupSweep()
        {
            if (this.netCookEnabled)
            {
                this.netCookStatus = "Stop mass cook before cleanup.";
                this.AddMenuNotification(this.netCookStatus, new Color(1f, 0.75f, 0.45f));
                return;
            }

            if (this.netCookCleanupCoroutine != null)
            {
                this.netCookStatus = "Cleanup already running.";
                this.AddMenuNotification(this.netCookStatus, new Color(1f, 0.75f, 0.45f));
                return;
            }

            if (!this.HasNetCookContext() && !this.TryCaptureNetCookFromCurrentTarget())
            {
                this.netCookStatus = string.IsNullOrWhiteSpace(this.netCookStatus) ? "Capture stoves first." : this.netCookStatus;
                this.AddMenuNotification(this.netCookStatus, new Color(1f, 0.55f, 0.55f));
                return;
            }

            this.netCookCleanupCoroutine = ModCoroutines.Start(this.NetCookCleanupRoutine());
        }

        private System.Collections.IEnumerator NetCookCleanupRoutine()
        {
            this.netCookStatus = "Cleaning up finished food...";
            this.NetCookLog("Cleanup started. targets=" + this.netCookTargets.Count);

            int collected = 0;
            int stillCooking = 0;
            int unavailable = 0;
            int failed = 0;

            try
            {
                for (int i = 0; i < this.netCookTargets.Count; i++)
                {
                    NetCookTargetContext target = this.netCookTargets[i];
                    this.ApplyNetCookTargetContext(target);

                    if (!this.TryGetNetCookTargetCookingStatus(target, out int cookingStatus, out int resultRecipeId, out int foodQuality, out string statusDetails))
                    {
                        unavailable++;
                        this.NetCookLog("Cleanup status unavailable for stove " + target.CookerNetId + ": " + statusDetails);
                        yield return new WaitForSeconds(0.05f);
                        continue;
                    }

                    this.NetCookLog("Cleanup stove " + target.CookerNetId + " status=" + this.GetNetCookCookingStatusName(cookingStatus) + " result=" + resultRecipeId + " quality=" + foodQuality);

                    if (cookingStatus == 5 || cookingStatus == 6)
                    {
                        if (this.TryInvokeNetCookInteract())
                        {
                            collected++;
                            target.Phase = 0;
                            target.ContinuePulses = 0;
                            target.LastStatus = -1;
                            target.LastStatusActionAt = Time.unscaledTime;
                            target.NextActionAt = Time.unscaledTime + NetCookCollectRestartDelaySeconds;
                            target.IdleRetries = 0;
                            target.SentCount++;
                            this.netCookSentCount++;
                            this.netCookTargets[i] = target;
                            yield return new WaitForSeconds(0.1f);
                            continue;
                        }

                        failed++;
                        this.NetCookLog("Cleanup interact failed for stove " + target.CookerNetId + ".");
                    }
                    else if (this.IsNetCookInProgressStatus(cookingStatus))
                    {
                        stillCooking++;
                    }
                    else
                    {
                        unavailable++;
                    }

                    yield return new WaitForSeconds(0.05f);
                }
            }
            finally
            {
                this.netCookCleanupCoroutine = null;
            }

            if (collected > 0)
            {
                this.netCookStatus = "Cleanup collected " + collected + " finished stove(s)." + (stillCooking > 0 ? " " + stillCooking + " still cooking." : string.Empty);
                this.AddMenuNotification(this.netCookStatus, new Color(0.45f, 1f, 0.55f));
            }
            else if (stillCooking > 0)
            {
                this.netCookStatus = "No finished food to collect. " + stillCooking + " stove(s) still cooking.";
                this.AddMenuNotification(this.netCookStatus, new Color(1f, 0.75f, 0.45f));
            }
            else if (failed > 0)
            {
                this.netCookStatus = "Cleanup failed on " + failed + " stove(s).";
                this.AddMenuNotification(this.netCookStatus, new Color(1f, 0.55f, 0.55f));
            }
            else
            {
                this.netCookStatus = "No finished food found on captured stoves.";
                this.AddMenuNotification(this.netCookStatus, new Color(1f, 0.75f, 0.45f));
            }

            this.NetCookLog("Cleanup finished. collected=" + collected + " stillCooking=" + stillCooking + " unavailable=" + unavailable + " failed=" + failed);
        }

        private void ResetNetCookCaptureContext(string status = null)
        {
            if (this.netCookEnabled)
            {
                this.StopNetCookInternal("Capture reset");
            }

            if (this.netCookCleanupCoroutine != null)
            {
                ModCoroutines.Stop(this.netCookCleanupCoroutine);
                this.netCookCleanupCoroutine = null;
            }

            if (this.netCookCaptureCoroutine != null)
            {
                ModCoroutines.Stop(this.netCookCaptureCoroutine);
                this.netCookCaptureCoroutine = null;
            }

            if (this.netCookStartCoroutine != null)
            {
                ModCoroutines.Stop(this.netCookStartCoroutine);
                this.netCookStartCoroutine = null;
            }

            this.netCookCaptureGeneration++;
            this.netCookCaptureInProgress = false;
            HeartopiaComplete.DebugEspClearGroup("mass-cook-capture");

            this.netCookCookerNetId = 0U;
            this.netCookCookerStaticId = 0;
            this.netCookCookerType = 0;
            this.netCookLevelObjectNetId = 0UL;
            this.netCookSentCount = 0;
            this.netCookTargets.Clear();
            this.netCookMaterialNetIds.Clear();
            this.netCookDrainAfterIngredientsRunOut = false;
            this.netCookDrainReason = null;
            this.netCookRecipeId = 0;
            this.netCookRecipeDropdownOpen = false;
            this.netCookRecipeScrollPos = Vector2.zero;
            this.netCookRecipeSearchText = "";
            this.InvalidateNetCookRecipeCache();
            this.netCookStatus = status ?? "Captured stoves reset. Capture stoves again.";
            this.NetCookLog(this.netCookStatus);
        }

        private void InvalidateNetCookRecipeCache()
        {
            this.netCookRecipeEntries.Clear();
            this.netCookVisibleRecipeEntries.Clear();
            this.netCookRecipeCookerTypes.Clear();
            this.netCookRecipeRequirementsCache.Clear();
            this.netCookRecipeCacheCookerStaticId = 0;
            this.netCookRecipeCacheFailureCookerStaticId = 0;
            this.nextNetCookRecipeCacheRetryAt = 0f;
            this.nextNetCookMaxRefreshAt = 0f;
        }

        private bool HasFreshNetCookRecipeCache()
        {
            return this.netCookRecipeEntries.Count > 0
                && this.netCookRecipeCacheCookerStaticId == this.netCookCookerStaticId
                && this.netCookRecipeCacheFailureCookerStaticId != this.netCookCookerStaticId;
        }

        private bool HasNetCookContext()
        {
            return this.netCookCookerNetId != 0U
                && this.netCookCookerStaticId > 0
                && this.netCookLevelObjectNetId != 0UL
                && (this.netCookTargets.Count > 0 || this.EnsureNetCookRecipeCache());
        }

        private void ProcessNetCookTargets(float now)
        {
            if (this.netCookTargets.Count <= 0)
            {
                return;
            }

            bool hasDueTarget = false;
            for (int i = 0; i < this.netCookTargets.Count; i++)
            {
                NetCookTargetContext target = this.netCookTargets[i];
                if (target != null && now >= target.NextActionAt)
                {
                    hasDueTarget = true;
                    break;
                }
            }

            if (!hasDueTarget)
            {
                return;
            }

            bool attempted = false;
            int readyTargets = 0;
            int processedTargets = 0;
            float interval = Mathf.Clamp(this.netCookInterval, 0.25f, 10f);
            this.SortNetCookTargetsForAction(now);

            for (int i = 0; i < this.netCookTargets.Count; i++)
            {
                NetCookTargetContext target = this.netCookTargets[i];
                if (target == null)
                {
                    this.netCookTargets.RemoveAt(i);
                    i--;
                    continue;
                }

                if (now < target.NextActionAt)
                {
                    continue;
                }

                attempted = true;
                processedTargets++;
                this.ApplyNetCookTargetContext(target);

                if (this.netCookDrainAfterIngredientsRunOut)
                {
                    if (this.ProcessNetCookDrainTarget(i, target, now, out bool targetRemoved))
                    {
                        readyTargets++;
                    }
                    if (targetRemoved)
                    {
                        i--;
                    }
                    if (processedTargets >= NetCookMaxActionsPerTick)
                    {
                        break;
                    }
                    continue;
                }

                if (target.Phase == 1 && this.HasPendingNetCookPrepareTarget(now))
                {
                    target.NextActionAt = now + NetCookBatchStartHoldSeconds;
                    this.netCookTargets[i] = target;
                    continue;
                }

                if (target.Phase == 0)
                {
                    if (this.IsNetCookCookQuantityCommitFull())
                    {
                        if (!this.netCookDrainAfterIngredientsRunOut)
                        {
                            this.BeginNetCookDrain(this.FormatNetCookQuantityDrainReason());
                        }

                        if (this.TryGetNetCookTargetCookingStatus(target, out int limitCookingStatus, out _, out _, out _)
                            && limitCookingStatus == 0)
                        {
                            this.netCookTargets.RemoveAt(i);
                            i--;
                            if (processedTargets >= NetCookMaxActionsPerTick)
                            {
                                break;
                            }
                            continue;
                        }

                        target.NextActionAt = now + this.GetNetCookStatusPollDelay(target);
                        this.netCookTargets[i] = target;
                        if (processedTargets >= NetCookMaxActionsPerTick)
                        {
                            break;
                        }
                        continue;
                    }

                    if (!this.TryBuildNetCookMaterials(this.netCookRecipeId, out List<uint> freshMaterials, out string materialStatus))
                    {
                        this.BeginNetCookDrain(this.FormatNetCookIngredientDrainReason(materialStatus));
                        this.netCookStatus = this.netCookDrainReason + " Finishing active stove(s)...";
                        target.NextActionAt = now + NetCookFastRetryDelaySeconds;
                        this.netCookTargets[i] = target;
                        if (processedTargets >= NetCookMaxActionsPerTick)
                        {
                            break;
                        }
                        continue;
                    }

                    this.netCookMaterialNetIds.Clear();
                    this.netCookMaterialNetIds.AddRange(freshMaterials);

                    if (this.TryInvokeNetCookPrepare(this.netCookRecipeId, this.netCookMaterialNetIds))
                    {
                        this.RecordNetCookPrepareCommitted();
                        target.Phase = 1;
                        target.ContinuePulses = 0;
                        target.IdleRetries = 0;
                        target.LastStatus = -1;
                        target.LastStatusActionAt = -999f;
                        target.LastCookCommandAt = now;
                        target.NextActionAt = now + NetCookPhaseAdvanceDelaySeconds;
                        readyTargets++;
                    }
                    else
                    {
                        this.netCookStatus = "PrepareCooking failed on stove " + target.CookerNetId + ". Retrying...";
                        target.NextActionAt = now + 2f;
                    }
                }
                else if (target.Phase == 1)
                {
                    if (this.TryInvokeNetCookStart())
                    {
                        target.Phase = 2;
                        target.IdleRetries = 0;
                        target.LastCookCommandAt = now;
                        target.NextActionAt = now + NetCookPhaseAdvanceDelaySeconds;
                        readyTargets++;
                    }
                    else
                    {
                        this.netCookStatus = "StartCooking failed on stove " + target.CookerNetId + ". Retrying...";
                        target.NextActionAt = now + 2f;
                    }
                }
                else if (target.Phase == 3)
                {
                    if (this.TryInvokeNetCookContinue())
                    {
                        target.Phase = 2;
                        target.ContinuePulses++;
                        target.LastCookCommandAt = now;
                        target.SentCount++;
                        this.netCookSentCount++;
                        target.NextActionAt = now + NetCookPhaseAdvanceDelaySeconds;
                        readyTargets++;
                    }
                    else
                    {
                        this.netCookStatus = "ContinueCooking adjust failed on stove " + target.CookerNetId + ". Retrying...";
                        target.NextActionAt = now + 1.25f;
                    }
                }
                else
                {
                    if (this.TryGetNetCookTargetCookingStatus(target, out int cookingStatus, out int resultRecipeId, out int foodQuality, out string statusDetails))
                    {
                        if (target.LastStatus != cookingStatus)
                        {
                            target.LastStatus = cookingStatus;
                            this.NetCookLog("Stove " + target.CookerNetId + " status=" + this.GetNetCookCookingStatusName(cookingStatus) + " result=" + resultRecipeId + " quality=" + foodQuality);
                        }

                        if (cookingStatus == 0)
                        {
                            target.IdleRetries++;
                            if (now - target.LastCookCommandAt < NetCookStartStatusGraceSeconds)
                            {
                                target.Phase = 2;
                                target.NextActionAt = now + this.GetNetCookStatusPollDelay(target);
                            }
                            else if (target.IdleRetries >= NetCookIdleResyncRetryThreshold)
                            {
                                this.NetCookLog("Stove " + target.CookerNetId + " stayed Idle after start grace; re-preparing instead of dropping it.");
                                target.Phase = 0;
                                target.IdleRetries = 0;
                                target.LastCookCommandAt = -999f;
                                target.NextActionAt = now + NetCookIdleReprepareDelaySeconds;
                            }
                            else
                            {
                                target.Phase = 2;
                                target.NextActionAt = now + this.GetNetCookStatusPollDelay(target);
                            }
                        }
                        else if (cookingStatus == 1 || cookingStatus == 2)
                        {
                            target.IdleRetries = 0;
                            target.LastCookCommandAt = now;
                            target.NextActionAt = now + this.GetNetCookStatusPollDelay(target);
                            readyTargets++;
                        }
                        else if (cookingStatus == 3 || cookingStatus == 4)
                        {
                            target.IdleRetries = 0;
                            if (now - target.LastStatusActionAt < 1.5f)
                            {
                                target.NextActionAt = now + this.GetNetCookStatusPollDelay(target);
                            }
                            else if (this.TryInvokeNetCookInteract())
                            {
                                target.Phase = 3;
                                target.LastStatusActionAt = now;
                                target.LastCookCommandAt = now;
                                target.SentCount++;
                                this.netCookSentCount++;
                                target.NextActionAt = now + NetCookPhaseAdvanceDelaySeconds;
                                readyTargets++;
                            }
                            else
                            {
                                this.netCookStatus = "InteractWithCooker adjust failed on stove " + target.CookerNetId + ". Retrying...";
                                target.NextActionAt = now + 1.25f;
                            }
                        }
                        else if (cookingStatus == 5 || cookingStatus == 6)
                        {
                            target.IdleRetries = 0;
                            if (this.TryInvokeNetCookInteract())
                            {
                                target.Phase = 0;
                                target.ContinuePulses = 0;
                                target.LastStatus = -1;
                                target.LastStatusActionAt = -999f;
                                target.IdleRetries = 0;
                                target.LastCookCommandAt = -999f;
                                target.SentCount++;
                                this.netCookSentCount++;
                                this.RecordNetCookCompletedDish();
                                target.NextActionAt = now + Mathf.Max(NetCookCollectRestartDelaySeconds, interval * 0.2f);
                                readyTargets++;
                            }
                            else
                            {
                                this.netCookStatus = "Collect cooked food failed on stove " + target.CookerNetId + ". Retrying...";
                                target.NextActionAt = now + 1.25f;
                            }
                        }
                        else
                        {
                            target.NextActionAt = now + this.GetNetCookStatusPollDelay(target);
                        }
                    }
                    else
                    {
                        if (now - target.LastStatusActionAt > 3f)
                        {
                            target.LastStatusActionAt = now;
                            this.NetCookLog("Status poll unavailable for stove " + target.CookerNetId + ": " + statusDetails);
                        }
                        this.netCookStatus = "Waiting for cooking status on stove " + target.CookerNetId + ".";
                        target.NextActionAt = now + this.GetNetCookStatusPollDelay(target);
                    }
                }

                this.netCookTargets[i] = target;
                if (processedTargets >= NetCookMaxActionsPerTick)
                {
                    break;
                }
            }

            if (attempted && readyTargets > 0)
            {
                if (this.netCookDrainAfterIngredientsRunOut)
                {
                    this.netCookStatus = (this.netCookDrainReason ?? "Ingredients ran out.") + " Finishing " + this.netCookTargets.Count + " active stove(s)...";
                }
                else
                {
                    this.netCookStatus = this.FormatNetCookActiveStatus();
                }
            }
        }

        private void ProcessNetCookMiniGameTargets(float now)
        {
            if (this.netCookTargets.Count <= 0)
            {
                return;
            }

            bool hasDueTarget = false;
            for (int i = 0; i < this.netCookTargets.Count; i++)
            {
                NetCookTargetContext target = this.netCookTargets[i];
                if (target != null && now >= target.NextActionAt)
                {
                    hasDueTarget = true;
                    break;
                }
            }

            if (!hasDueTarget)
            {
                return;
            }

            bool attempted = false;
            int actionsTaken = 0;
            int processedTargets = 0;
            this.SortNetCookTargetsForAction(now);

            for (int i = 0; i < this.netCookTargets.Count; i++)
            {
                NetCookTargetContext target = this.netCookTargets[i];
                if (target == null)
                {
                    this.netCookTargets.RemoveAt(i);
                    i--;
                    continue;
                }

                if (now < target.NextActionAt)
                {
                    continue;
                }

                attempted = true;
                processedTargets++;
                this.ApplyNetCookTargetContext(target);

                if (target.Phase == 3)
                {
                    if (this.TryInvokeNetCookContinue())
                    {
                        target.Phase = 2;
                        target.ContinuePulses++;
                        target.LastCookCommandAt = now;
                        target.SentCount++;
                        this.netCookSentCount++;
                        target.NextActionAt = now + NetCookPhaseAdvanceDelaySeconds;
                        actionsTaken++;
                    }
                    else
                    {
                        this.netCookStatus = "ContinueCooking mini-game assist failed on stove " + target.CookerNetId + ". Retrying...";
                        target.NextActionAt = now + 1.25f;
                    }

                    this.netCookTargets[i] = target;
                    if (processedTargets >= NetCookMaxActionsPerTick)
                    {
                        break;
                    }
                    continue;
                }

                if (this.TryGetNetCookTargetCookingStatus(target, out int cookingStatus, out int resultRecipeId, out int foodQuality, out string statusDetails))
                {
                    if (target.LastStatus != cookingStatus)
                    {
                        target.LastStatus = cookingStatus;
                        this.NetCookLog("Mini game stove " + target.CookerNetId + " status=" + this.GetNetCookCookingStatusName(cookingStatus) + " result=" + resultRecipeId + " quality=" + foodQuality);
                    }

                    target.Phase = 2;
                    target.IdleRetries = 0;

                    if (cookingStatus == 3 || cookingStatus == 4)
                    {
                        if (now - target.LastStatusActionAt < 1.5f)
                        {
                            target.NextActionAt = now + this.GetNetCookStatusPollDelay(target);
                        }
                        else if (this.TryInvokeNetCookInteract())
                        {
                            target.Phase = 3;
                            target.LastStatusActionAt = now;
                            target.LastCookCommandAt = now;
                            target.SentCount++;
                            this.netCookSentCount++;
                            target.NextActionAt = now + NetCookPhaseAdvanceDelaySeconds;
                            actionsTaken++;
                        }
                        else
                        {
                            this.netCookStatus = "InteractWithCooker mini-game assist failed on stove " + target.CookerNetId + ". Retrying...";
                            target.NextActionAt = now + 1.25f;
                        }
                    }
                    else if (cookingStatus == 5 || cookingStatus == 6)
                    {
                        if (now - target.LastStatusActionAt < 1.5f)
                        {
                            target.NextActionAt = now + this.GetNetCookStatusPollDelay(target);
                        }
                        else if (this.TryInvokeNetCookInteract())
                        {
                            target.Phase = 2;
                            target.LastStatus = -1;
                            target.LastStatusActionAt = now;
                            target.LastCookCommandAt = -999f;
                            target.SentCount++;
                            this.netCookSentCount++;
                            target.NextActionAt = now + Mathf.Max(NetCookCollectRestartDelaySeconds, this.GetNetCookStatusPollDelay(target) * 0.35f);
                            actionsTaken++;
                        }
                        else
                        {
                            this.netCookStatus = "Collect finished food failed on stove " + target.CookerNetId + ". Retrying...";
                            target.NextActionAt = now + 1.25f;
                        }
                    }
                    else
                    {
                        target.NextActionAt = now + this.GetNetCookStatusPollDelay(target);
                    }
                }
                else
                {
                    if (now - target.LastStatusActionAt > 3f)
                    {
                        target.LastStatusActionAt = now;
                        this.NetCookLog("Mini game status poll unavailable for stove " + target.CookerNetId + ": " + statusDetails);
                    }
                    this.netCookStatus = "Waiting for active cooking on stove " + target.CookerNetId + ".";
                    target.NextActionAt = now + this.GetNetCookStatusPollDelay(target);
                }

                this.netCookTargets[i] = target;
                if (processedTargets >= NetCookMaxActionsPerTick)
                {
                    break;
                }
            }

            if (attempted && actionsTaken > 0)
            {
                this.netCookStatus = "Mini game assist active: " + this.netCookTargets.Count + " stove(s), actions " + this.netCookSentCount + ".";
            }
        }

        private void SortNetCookTargetsForAction(float now)
        {
            if (this.netCookTargets.Count <= 1)
            {
                return;
            }

            this.netCookTargets.Sort((a, b) =>
            {
                bool readyA = a != null && now >= a.NextActionAt;
                bool readyB = b != null && now >= b.NextActionAt;
                if (readyA != readyB)
                {
                    return readyA ? -1 : 1;
                }

                int priorityCompare = this.GetNetCookActionPriority(a).CompareTo(this.GetNetCookActionPriority(b));
                if (priorityCompare != 0)
                {
                    return priorityCompare;
                }

                float nextA = a != null ? a.NextActionAt : float.MaxValue;
                float nextB = b != null ? b.NextActionAt : float.MaxValue;
                return nextA.CompareTo(nextB);
            });
        }

        private float GetNetCookStatusPollDelay(NetCookTargetContext target)
        {
            uint cookerNetId = target != null ? target.CookerNetId : 0U;
            return NetCookStatusPollDelaySeconds + (cookerNetId % 7U) * 0.03f;
        }

        private int GetNetCookActionPriority(NetCookTargetContext target)
        {
            if (target == null)
            {
                return 99;
            }

            if (target.Phase == 3 || target.LastStatus == 3 || target.LastStatus == 4)
            {
                return 0;
            }

            if (target.Phase == 1)
            {
                return 1;
            }

            if (target.LastStatus == 5 || target.LastStatus == 6)
            {
                return 2;
            }

            if (target.LastStatus == 1 || target.LastStatus == 2)
            {
                return 3;
            }

            if (target.LastStatus == 0 || target.Phase == 0)
            {
                return 4;
            }

            return 4;
        }

        private bool HasOtherNetCookInProgressTarget(int currentIndex)
        {
            for (int i = 0; i < this.netCookTargets.Count; i++)
            {
                if (i == currentIndex)
                {
                    continue;
                }

                NetCookTargetContext other = this.netCookTargets[i];
                if (other == null)
                {
                    continue;
                }

                if (other.Phase == 1 || other.Phase == 3 || this.IsNetCookInProgressStatus(other.LastStatus))
                {
                    return true;
                }
            }

            return false;
        }

        private void BeginNetCookDrain(string reason)
        {
            if (this.netCookDrainAfterIngredientsRunOut)
            {
                return;
            }

            this.netCookDrainAfterIngredientsRunOut = true;
            this.netCookDrainReason = string.IsNullOrWhiteSpace(reason) ? "Ingredients ran out." : reason;
            this.NetCookLog(this.netCookDrainReason + " Draining active stoves before stop.");
        }

        private bool HasNetCookCookQuantityLimit()
        {
            return this.netCookCookQuantity > 0;
        }

        private bool IsNetCookCookQuantityCommitFull()
        {
            return this.HasNetCookCookQuantityLimit()
                && this.netCookCommittedDishCount >= this.netCookCookQuantity;
        }

        private bool IsNetCookTargetOccupiedWithDish(NetCookTargetContext target)
        {
            if (target == null)
            {
                return false;
            }

            if (target.Phase == 1 || target.Phase == 3)
            {
                return true;
            }

            return target.LastStatus >= 1 && target.LastStatus <= 6;
        }

        private void SeedNetCookCommittedDishCountFromActiveTargets()
        {
            this.netCookCommittedDishCount = 0;
            if (!this.HasNetCookCookQuantityLimit())
            {
                return;
            }

            for (int i = 0; i < this.netCookTargets.Count; i++)
            {
                if (this.IsNetCookTargetOccupiedWithDish(this.netCookTargets[i]))
                {
                    this.netCookCommittedDishCount++;
                }
            }

            if (this.IsNetCookCookQuantityCommitFull())
            {
                this.BeginNetCookDrain(this.FormatNetCookQuantityDrainReason());
            }
        }

        private void RecordNetCookPrepareCommitted()
        {
            if (!this.HasNetCookCookQuantityLimit())
            {
                return;
            }

            this.netCookCommittedDishCount++;
            if (this.IsNetCookCookQuantityCommitFull())
            {
                this.BeginNetCookDrain(this.FormatNetCookQuantityDrainReason());
            }
        }

        private string FormatNetCookQuantityDrainReason()
        {
            return "Cook quantity limit reached (" + this.netCookCookQuantity + ").";
        }

        private void RecordNetCookCompletedDish()
        {
            this.netCookCompletedDishCount++;
        }

        private int GetNetCookWarehouseMoveBatchCount()
        {
            if (this.netCookUseAllIngredients)
            {
                return Math.Max(1, this.netCookMaxCookQuantity);
            }

            if (!this.HasNetCookCookQuantityLimit())
            {
                return Math.Max(1, this.netCookMaxCookQuantity);
            }

            return this.netCookCookQuantity;
        }

        private string FormatNetCookActiveStatus()
        {
            string cookedLabel = this.HasNetCookCookQuantityLimit()
                ? ("cooked " + this.netCookCompletedDishCount + "/" + this.netCookCookQuantity)
                : ("cooked " + this.netCookCompletedDishCount);
            return "Net mass cook active: " + this.netCookTargets.Count + " stove(s), " + cookedLabel + ".";
        }

        private string FormatNetCookIngredientDrainReason(string materialStatus)
        {
            string recipeLabel = this.GetNetCookSelectedRecipeLabel();
            if (string.IsNullOrWhiteSpace(recipeLabel))
            {
                recipeLabel = "selected recipe";
            }

            if (this.IsNetCookMissingIngredientStatus(materialStatus))
            {
                return "Ingredients ran out for " + recipeLabel + ".";
            }

            if (string.IsNullOrWhiteSpace(materialStatus) || this.IsNetCookInternalMaterialCheckStatus(materialStatus))
            {
                return "Ingredients ran out for " + recipeLabel + ".";
            }

            return "Ingredients unavailable for " + recipeLabel + ": " + materialStatus;
        }

        private bool IsNetCookInternalMaterialCheckStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("methods", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsNetCookMissingIngredientStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status.IndexOf("Missing ingredients", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("Recipe slot has no material net id", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("Recipe has no usable material slots", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ProcessNetCookDrainTarget(int targetIndex, NetCookTargetContext target, float now, out bool targetRemoved)
        {
            targetRemoved = false;

            if (target.Phase == 1)
            {
                if (this.TryInvokeNetCookStart())
                {
                    target.Phase = 2;
                    target.NextActionAt = now + 0.75f;
                    this.netCookTargets[targetIndex] = target;
                    return true;
                }

                this.netCookStatus = "StartCooking while stopping failed on stove " + target.CookerNetId + ". Retrying...";
                target.NextActionAt = now + 1.25f;
                this.netCookTargets[targetIndex] = target;
                return false;
            }

            if (target.Phase == 3)
            {
                if (this.TryInvokeNetCookContinue())
                {
                    target.Phase = 2;
                    target.ContinuePulses++;
                    target.SentCount++;
                    this.netCookSentCount++;
                    target.NextActionAt = now + 0.75f;
                    this.netCookTargets[targetIndex] = target;
                    return true;
                }

                this.netCookStatus = "ContinueCooking while stopping failed on stove " + target.CookerNetId + ". Retrying...";
                target.NextActionAt = now + 1.25f;
                this.netCookTargets[targetIndex] = target;
                return false;
            }

            if (!this.TryGetNetCookTargetCookingStatus(target, out int cookingStatus, out int resultRecipeId, out int foodQuality, out string statusDetails))
            {
                if (now - target.LastStatusActionAt > 3f)
                {
                    target.LastStatusActionAt = now;
                    this.NetCookLog("Drain status poll unavailable for stove " + target.CookerNetId + ": " + statusDetails);
                }
                this.netCookStatus = "Stopping after ingredients run out: waiting for stove " + target.CookerNetId + ".";
                target.NextActionAt = now + 0.75f;
                this.netCookTargets[targetIndex] = target;
                return false;
            }

            if (target.LastStatus != cookingStatus)
            {
                target.LastStatus = cookingStatus;
                this.NetCookLog("Drain stove " + target.CookerNetId + " status=" + this.GetNetCookCookingStatusName(cookingStatus) + " result=" + resultRecipeId + " quality=" + foodQuality);
            }

            if (cookingStatus == 0)
            {
                this.netCookTargets.RemoveAt(targetIndex);
                targetRemoved = true;
                return true;
            }

            if (cookingStatus == 1 || cookingStatus == 2)
            {
                target.NextActionAt = now + 0.5f;
                this.netCookTargets[targetIndex] = target;
                return true;
            }

            if (cookingStatus == 3 || cookingStatus == 4)
            {
                if (now - target.LastStatusActionAt < 1.5f)
                {
                    target.NextActionAt = now + 0.5f;
                    this.netCookTargets[targetIndex] = target;
                    return false;
                }

                if (this.TryInvokeNetCookInteract())
                {
                    target.Phase = 3;
                    target.LastStatusActionAt = now;
                    target.SentCount++;
                    this.netCookSentCount++;
                    target.NextActionAt = now + 0.75f;
                    this.netCookTargets[targetIndex] = target;
                    return true;
                }

                this.netCookStatus = "InteractWithCooker while stopping failed on stove " + target.CookerNetId + ". Retrying...";
                target.NextActionAt = now + 1.25f;
                this.netCookTargets[targetIndex] = target;
                return false;
            }

            if (cookingStatus == 5 || cookingStatus == 6)
            {
                if (now - target.LastStatusActionAt < 1.5f)
                {
                    target.NextActionAt = now + 0.5f;
                    this.netCookTargets[targetIndex] = target;
                    return false;
                }

                if (this.TryInvokeNetCookInteract())
                {
                    target.Phase = 4;
                    target.LastStatusActionAt = now;
                    target.SentCount++;
                    this.netCookSentCount++;
                    this.RecordNetCookCompletedDish();
                    target.NextActionAt = now + 0.75f;
                    this.netCookTargets[targetIndex] = target;
                    return true;
                }

                this.netCookStatus = "Collect cooked food while stopping failed on stove " + target.CookerNetId + ". Retrying...";
                target.NextActionAt = now + 1.25f;
                this.netCookTargets[targetIndex] = target;
                return false;
            }

            target.NextActionAt = now + 0.5f;
            this.netCookTargets[targetIndex] = target;
            return false;
        }

        private bool TryGetNetCookTargetCookingStatus(NetCookTargetContext target, out int cookingStatus, out int resultRecipeId, out int foodQuality, out string status)
        {
            cookingStatus = -1;
            resultRecipeId = 0;
            foodQuality = 0;
            status = "Cooking status unavailable.";

            try
            {
                if (!this.TryGetAuraMonoEntityObjectByNetId(target.CookerNetId, out IntPtr burnerEntityObj) || burnerEntityObj == IntPtr.Zero)
                {
                    status = "AuraMono burner entity missing.";
                    return false;
                }

                if (!this.TryResolveNetCookCookingComponentAuraMono(burnerEntityObj, out IntPtr cookingComponentObj, out string componentStatus))
                {
                    status = componentStatus;
                    return false;
                }

                IntPtr componentDataObj = IntPtr.Zero;
                if ((!this.TryGetMonoObjectMember(cookingComponentObj, "ComponentData", out componentDataObj) || componentDataObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(cookingComponentObj, "_componentData", out componentDataObj) || componentDataObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(cookingComponentObj, "componentData", out componentDataObj) || componentDataObj == IntPtr.Zero))
                {
                    status = "Cooking component data missing.";
                    return false;
                }

                if (!this.TryGetMonoInt32Member(componentDataObj, "Status", out cookingStatus)
                    && !this.TryGetMonoInt32Member(componentDataObj, "status", out cookingStatus))
                {
                    status = "Cooking status field missing.";
                    return false;
                }

                this.TryGetMonoInt32Member(componentDataObj, "ResultRecipeId", out resultRecipeId);
                if (resultRecipeId <= 0)
                {
                    this.TryGetMonoInt32Member(componentDataObj, "resultRecipeId", out resultRecipeId);
                }

                this.TryGetMonoInt32Member(componentDataObj, "FoodQuality", out foodQuality);
                if (foodQuality <= 0)
                {
                    this.TryGetMonoInt32Member(componentDataObj, "foodQuality", out foodQuality);
                }

                status = "Cooking status ready.";
                return true;
            }
            catch (Exception ex)
            {
                status = "Cooking status exception: " + ex.Message;
                return false;
            }
        }

        private string GetNetCookCookingStatusName(int status)
        {
            switch (status)
            {
                case 0:
                    return "Idle";
                case 1:
                    return "Preparing";
                case 2:
                    return "Cooking";
                case 3:
                    return "Danger";
                case 4:
                    return "Relief";
                case 5:
                    return "Succeed";
                case 6:
                    return "Failed";
                default:
                    return "Unknown(" + status + ")";
            }
        }

        private void ApplyNetCookTargetContext(NetCookTargetContext target)
        {
            this.netCookCookerNetId = target.CookerNetId;
            this.netCookCookerStaticId = target.CookerStaticId;
            this.netCookCookerType = target.CookerType;
            this.netCookLevelObjectNetId = target.LevelObjectNetId;
        }

        private void ProcessNetCookLoop()
        {
            if (!this.HasNetCookContext())
            {
                this.StopNetCookInternal("Missing cooker target.");
                return;
            }

            if (this.netCookMiniGameOnly)
            {
                float miniGameNow = Time.unscaledTime;
                if (this.netCookTargets.Count > 0)
                {
                    this.ProcessNetCookMiniGameTargets(miniGameNow);
                }
                return;
            }

            if (this.netCookRecipeId <= 0)
            {
                this.StopNetCookInternal("No recipe selected.");
                return;
            }

            float now = Time.unscaledTime;
            if (this.netCookTargets.Count > 0)
            {
                this.ProcessNetCookTargets(now);
            }

            if (this.netCookDrainAfterIngredientsRunOut && this.netCookTargets.Count <= 0)
            {
                this.StopNetCookInternal((this.netCookDrainReason ?? "Ingredients ran out.") + " All active stoves finished.");
            }
        }

        private bool TryCaptureNetCookFromCurrentTarget()
        {
            float now = Time.unscaledTime;
            if (this.netCookCaptureInProgress || this.netCookCaptureCoroutine != null)
            {
                this.netCookStatus = "Stove capture already running.";
                return false;
            }

            if (!this.IsNetCookRuntimeCaptureReady(out string runtimeStatus))
            {
                this.netCookStatus = runtimeStatus;
                this.NetCookLog("Capture delayed: " + runtimeStatus);
                return false;
            }

            if (now < this.nextNetCookCaptureAllowedAt)
            {
                if (this.netCookTargets.Count > 0)
                {
                    this.netCookStatus = "Using recent capture: " + this.netCookTargets.Count + " stove(s).";
                    return true;
                }

                this.netCookStatus = "Stove capture cooling down.";
                return false;
            }

            this.netCookCaptureInProgress = true;
            this.nextNetCookCaptureAllowedAt = now + NetCookCaptureCooldownSeconds;
            this.NetCookLog("Capture requested.");
            try
            {
                int previousRecipeId = this.netCookRecipeId;
                int previousCookerStaticId = this.netCookCookerStaticId;
                int previousCookerType = this.netCookCookerType;
                this.netCookTargets.Clear();
                bool resolvedTargets = this.TryResolveNetCookContextsFromCurrentTarget(this.netCookTargets, out string multiCaptureStatus, true);
                uint cookerNetId = 0U;
                int cookerStaticId = 0;
                int cookerType = 0;
                ulong levelObjectNetId = 0UL;
                string captureStatus = multiCaptureStatus;
                if ((resolvedTargets && this.netCookTargets.Count > 0)
                    || this.TryResolveNetCookContextFromCurrentTarget(out cookerNetId, out cookerStaticId, out cookerType, out levelObjectNetId, out captureStatus))
                {
                    if (!resolvedTargets || this.netCookTargets.Count <= 0)
                    {
                        this.netCookTargets.Add(new NetCookTargetContext
                        {
                            CookerNetId = cookerNetId,
                            CookerStaticId = cookerStaticId,
                            CookerType = cookerType,
                            LevelObjectNetId = levelObjectNetId
                        });
                        multiCaptureStatus = captureStatus;
                    }

                    NetCookTargetContext primaryTarget = this.netCookTargets[0];
                    this.netCookCookerNetId = primaryTarget.CookerNetId;
                    this.netCookCookerStaticId = primaryTarget.CookerStaticId;
                    this.netCookCookerType = primaryTarget.CookerType;
                    this.netCookLevelObjectNetId = primaryTarget.LevelObjectNetId;
                    if (primaryTarget.CookerStaticId > 0)
                    {
                        this.netCookLastCapturedCookerStaticId = primaryTarget.CookerStaticId;
                    }
                    if (primaryTarget.CookerType > 0)
                    {
                        this.netCookLastCapturedCookerType = primaryTarget.CookerType;
                    }
                    this.netCookSentCount = 0;
                    this.netCookRecipeDropdownOpen = false;
                    bool cookerChanged = !this.IsSameNetCookCookerFamily(previousCookerStaticId, previousCookerType, primaryTarget.CookerStaticId, primaryTarget.CookerType);
                    bool recipeCacheReady = this.HasFreshNetCookRecipeCache();
                    if (!recipeCacheReady || cookerChanged)
                    {
                        this.InvalidateNetCookRecipeCache();
                        this.NetCookLog("Refreshing recipe cache after capture for cookerStaticId=" + primaryTarget.CookerStaticId + " cookerType=" + primaryTarget.CookerType + " cookerChanged=" + cookerChanged + ".");
                        recipeCacheReady = this.EnsureNetCookRecipeCache();
                    }
                    else
                    {
                        this.NetCookLog("Reusing recipe cache after capture for cookerStaticId=" + primaryTarget.CookerStaticId + " cookerType=" + primaryTarget.CookerType + ".");
                    }
                    if (previousRecipeId > 0 && this.GetVisibleNetCookRecipeEntries().Any(kv => kv.Key == previousRecipeId))
                    {
                        this.netCookRecipeId = previousRecipeId;
                        this.NetCookLog("Preserved selected recipe " + previousRecipeId + " after capture.");
                    }
                    else
                    {
                        this.netCookRecipeId = 0;
                        this.TrySelectDefaultNetCookRecipeForCooker();
                    }

                    this.netCookStatus = string.IsNullOrWhiteSpace(multiCaptureStatus) ? "Captured cooker target(s)." : multiCaptureStatus;
                    this.SyncNetCookCaptureDebugEsp();
                    this.NetCookLog("Capture recipe cache ready=" + recipeCacheReady + " visibleRecipes=" + this.GetVisibleNetCookRecipeEntries().Count);
                    this.NetCookLog("Captured cookerStaticId=" + primaryTarget.CookerStaticId + " cookerType=" + primaryTarget.CookerType + " cooker=" + primaryTarget.CookerNetId + " levelObject=" + primaryTarget.LevelObjectNetId + " selectedRecipe=" + this.netCookRecipeId + " targets=" + this.netCookTargets.Count);
                    bool forceDeferredRefresh = this.ShouldForceNetCookDeferredBroadRefresh(out string deferredRefreshReason);
                    this.NetCookLog("Deferred refresh decision forceBroad=" + forceDeferredRefresh + " reason=" + deferredRefreshReason + " targets=" + this.netCookTargets.Count + ".");
                    this.StartNetCookDeferredOwnerWindowExpansion(primaryTarget.CookerStaticId, primaryTarget.CookerType, forceDeferredRefresh);
                    return true;
                }

                if (string.IsNullOrWhiteSpace(multiCaptureStatus))
                {
                    multiCaptureStatus = "No cooker target found.";
                }

                this.netCookStatus = multiCaptureStatus;
                this.NetCookLog("Capture failed: " + multiCaptureStatus);
                return false;
            }
            catch (Exception ex)
            {
                this.netCookStatus = "Capture failed: " + ex.Message;
                this.NetCookLog("TryCaptureNetCookFromCurrentTarget exception: " + ex);
                return false;
            }
            finally
            {
                this.netCookCaptureInProgress = false;
            }
        }

        private void UpdateNetCookRuntimeReadiness()
        {
            float now = Time.unscaledTime;
            bool playerReady = false;
            try
            {
                GameObject player = GameObject.Find("p_player_skeleton(Clone)");
                playerReady = player != null && player.activeInHierarchy;
            }
            catch
            {
                playerReady = false;
            }

            if (playerReady)
            {
                this.netCookRuntimeLastReadyAt = now;
                if (this.netCookRuntimeReadySince <= 0f)
                {
                    this.netCookRuntimeReadySince = now;
                }
            }
            else
            {
                this.netCookRuntimeReadySince = 0f;
            }
        }

        private bool IsNetCookRuntimeCaptureReady(out string status)
        {
            this.UpdateNetCookRuntimeReadiness();

            float now = Time.unscaledTime;
            if (now < NetCookMinimumStartupCaptureDelaySeconds)
            {
                status = "Game is still warming up. Try Capture Stoves again in " + Mathf.CeilToInt(NetCookMinimumStartupCaptureDelaySeconds - now) + "s.";
                return false;
            }

            if (this.netCookRuntimeReadySince <= 0f)
            {
                status = "Player runtime is not ready yet. Try Capture Stoves again after the town finishes loading.";
                return false;
            }

            float stableFor = now - this.netCookRuntimeReadySince;
            if (stableFor < NetCookRuntimeReadyGraceSeconds)
            {
                status = "Stove scanner is warming up. Try Capture Stoves again in " + Mathf.CeilToInt(NetCookRuntimeReadyGraceSeconds - stableFor) + "s.";
                return false;
            }

            status = "Runtime ready.";
            return true;
        }

        private void SyncNetCookCaptureDebugEsp()
        {
            HeartopiaComplete.DebugEspClearGroup("mass-cook-capture");

            for (int i = 0; i < this.netCookTargets.Count; i++)
            {
                NetCookTargetContext debugTarget = this.netCookTargets[i];
                if (!this.TryRefreshNetCookTargetWorldPosition(debugTarget, true))
                {
                    continue;
                }

                Vector3 debugPosition = debugTarget.WorldPosition;
                string debugKey = "mass-cook-capture-" + debugTarget.CookerNetId;
                string debugLabel =
                    "Stove " + (i + 1)
                    + "\nNetId " + debugTarget.CookerNetId
                    + "\nStatic " + debugTarget.CookerStaticId + " | Type " + debugTarget.CookerType;
                HeartopiaComplete.DebugEspUpsert(
                    debugKey,
                    debugPosition,
                    debugLabel,
                    new Color(1f, 0.65f, 0.2f),
                    "mass-cook-capture",
                    0f,
                    true);
            }
        }

        private bool ShouldForceNetCookDeferredBroadRefresh(out string reason)
        {
            if (this.netCookTargets.Count < NetCookDeferredBroadRefreshTargetThreshold)
            {
                reason = "target-count-below-threshold";
                return true;
            }

            if (this.netCookLastDeferredWorldScanCandidateCount >= 0
                && this.netCookLastBroadRefreshWorldScanCandidateCount >= 0
                && this.netCookLastDeferredWorldScanCandidateCount != this.netCookLastBroadRefreshWorldScanCandidateCount)
            {
                reason = "world-scan-candidate-count-changed " + this.netCookLastBroadRefreshWorldScanCandidateCount + "->" + this.netCookLastDeferredWorldScanCandidateCount;
                return true;
            }

            reason = "cache-fresh";
            return false;
        }

        private void StartNetCookDeferredOwnerWindowExpansion(int desiredCookerStaticId, int desiredCookerType, bool forceBroadRefresh = false)
        {
            if (!NetCookUnsafeBroadAuraMonoExpansionEnabled)
            {
                this.netCookStatus = "Captured " + this.netCookTargets.Count + " nearby stove(s).";
                return;
            }

            if (this.netCookCaptureCoroutine != null
                || this.netCookTargets.Count <= 0
                || this.netCookTargets.Count >= NetCookMaxCaptureTargets
                || desiredCookerStaticId <= 0)
            {
                return;
            }

            int captureGeneration = ++this.netCookCaptureGeneration;
            this.netCookCaptureCoroutine = ModCoroutines.Start(this.NetCookDeferredOwnerWindowExpansionRoutine(desiredCookerStaticId, desiredCookerType, forceBroadRefresh, captureGeneration));
        }

        private System.Collections.IEnumerator NetCookCoroutineWarmupRoutine()
        {
            yield return null;
        }

        private System.Collections.IEnumerator NetCookDeferredOwnerWindowExpansionRoutine(int desiredCookerStaticId, int desiredCookerType, bool forceBroadRefresh, int captureGeneration)
        {
            yield return null;

            if (captureGeneration != this.netCookCaptureGeneration)
            {
                yield break;
            }

            float safeStartAt = Time.unscaledTime + NetCookDeferredBroadRefreshStartDelaySeconds;
            while (Time.unscaledTime < safeStartAt)
            {
                yield return null;
                if (captureGeneration != this.netCookCaptureGeneration)
                {
                    yield break;
                }
            }

            int added = 0;
            int skippedDifferentCooker = 0;
            int skippedDuplicateCooker = 0;
            int ownerCandidatesWithEntity = 0;
            int ownerCandidatesWithCookBuild = 0;
            int broadInspected = 0;
            int broadCookBuilds = 0;
            int broadAdded = 0;
            HashSet<uint> inspectedOwnerNetIds = new HashSet<uint>();
            HashSet<string> seenTargets = new HashSet<string>();
            HashSet<uint> seenCookerNetIds = new HashSet<uint>();
            HashSet<uint> ownerSeedNetIds = new HashSet<uint>();
            List<string> debugSamples = NetCookScanDebugLogsEnabled ? new List<string>(NetCookScanDebugSampleLimit) : null;

            try
            {
                for (int i = 0; i < this.netCookTargets.Count; i++)
                {
                    if (captureGeneration != this.netCookCaptureGeneration)
                    {
                        yield break;
                    }

                    NetCookTargetContext target = this.netCookTargets[i];
                    if (target.CookerNetId != 0U)
                    {
                        seenCookerNetIds.Add(target.CookerNetId);
                    }

                    if (target.LevelObjectNetId != 0UL)
                    {
                        seenTargets.Add(target.CookerNetId + ":" + target.LevelObjectNetId);
                        uint ownerNetId = ExtractNetCookOwnerNetId(target.LevelObjectNetId);
                        if (ownerNetId != 0U)
                        {
                            ownerSeedNetIds.Add(ownerNetId);
                        }
                    }
                }

                if (ownerSeedNetIds.Count <= 0 || !this.TryGetNetCookScanOrigin(out Vector3 scanOrigin, out _))
                {
                    this.netCookStatus = "Captured " + this.netCookTargets.Count + " nearby stove(s).";
                    yield break;
                }

                List<uint> seeds = ownerSeedNetIds.ToList();
                int frameInspections = 0;
                this.netCookStatus = "Expanding stove capture... " + this.netCookTargets.Count + " stove(s).";

                bool useOwnerWindow = false;
                if (useOwnerWindow)
                {
                    for (int seedIndex = 0; seedIndex < seeds.Count && this.netCookTargets.Count < NetCookMaxCaptureTargets; seedIndex++)
                    {
                        uint seedOwnerNetId = seeds[seedIndex];
                        if (seedOwnerNetId == 0U)
                        {
                            continue;
                        }

                        long start = Math.Max(1L, (long)seedOwnerNetId - NetCookFastOwnerNetIdProbeWindow);
                        long end = (long)seedOwnerNetId + NetCookFastOwnerNetIdProbeWindow;
                        for (long ownerCandidate = start; ownerCandidate <= end && this.netCookTargets.Count < NetCookMaxCaptureTargets; ownerCandidate++)
                        {
                            uint ownerCookBuildNetId = (uint)ownerCandidate;
                            if (!inspectedOwnerNetIds.Add(ownerCookBuildNetId))
                            {
                                continue;
                            }

                            frameInspections++;
                            if (frameInspections >= NetCookOwnerWindowInspectionsPerFrame)
                            {
                                this.netCookStatus = "Expanding stove capture... " + this.netCookTargets.Count + " stove(s).";
                                frameInspections = 0;
                                yield return null;
                                if (captureGeneration != this.netCookCaptureGeneration)
                                {
                                    yield break;
                                }
                            }

                            if (!this.TryGetAuraMonoEntityObjectByNetId(ownerCookBuildNetId, out IntPtr ownerEntityObj) || ownerEntityObj == IntPtr.Zero)
                            {
                                continue;
                            }
                            ownerCandidatesWithEntity++;

                            if (!this.TryResolveNetCookBuildComponentAuraMono(ownerEntityObj, out IntPtr cookBuildComponentObj, out _))
                            {
                                continue;
                            }
                            ownerCandidatesWithCookBuild++;

                            Vector3 ownerPosition;
                            bool hasOwnerPosition = true;
                            if (!this.TryGetAuraMonoEntityPosition(ownerEntityObj, out ownerPosition)
                                && !this.TryExtractHomePositionMonoObject(ownerEntityObj, out ownerPosition)
                                && !this.TryExtractHomePositionMonoObject(cookBuildComponentObj, out ownerPosition))
                            {
                                hasOwnerPosition = false;
                                ownerPosition = scanOrigin;
                            }

                            int cookerStaticId = 0;
                            this.TryGetMonoInt32Member(cookBuildComponentObj, "_cookerStaticId", out cookerStaticId);
                            if (cookerStaticId <= 0)
                            {
                                this.TryGetMonoInt32Member(cookBuildComponentObj, "cookerStaticId", out cookerStaticId);
                            }

                            int cookerType = 0;
                            if (cookerStaticId > 0 && cookerStaticId == desiredCookerStaticId && desiredCookerType > 0)
                            {
                                cookerType = desiredCookerType;
                            }
                            else if (cookerStaticId > 0)
                            {
                                this.TryGetCookerTypeForStaticId(cookerStaticId, out cookerType);
                            }
                            if (cookerType <= 0)
                            {
                                cookerType = desiredCookerType;
                            }

                            if (!this.IsCompatibleNetCookCooker(cookerStaticId, cookerType, desiredCookerStaticId, desiredCookerType))
                            {
                                skippedDifferentCooker++;
                                AddNetCookScanDebugSample(debugSamples, "deferred-owner owner=" + ownerCookBuildNetId + " rejected incompatible static=" + cookerStaticId + " type=" + cookerType);
                                continue;
                            }

                            int addedForOwner = this.TryAddCookBuildBurnerMapTargetsAuraMono(
                                ownerCookBuildNetId,
                                ownerEntityObj,
                                cookBuildComponentObj,
                                this.netCookTargets,
                                seenTargets,
                                seenCookerNetIds,
                                ownerPosition,
                                desiredCookerStaticId,
                                desiredCookerType,
                                ref skippedDifferentCooker,
                                ref skippedDuplicateCooker);
                            if (addedForOwner <= 0)
                            {
                                addedForOwner = this.TryAddSynthesizedNetCookBurnerTargets(
                                    ownerCookBuildNetId,
                                    ownerPosition,
                                    cookerStaticId,
                                    cookerType,
                                    desiredCookerStaticId,
                                    desiredCookerType,
                                    this.netCookTargets,
                                    seenTargets,
                                    seenCookerNetIds,
                                    ref skippedDifferentCooker,
                                    ref skippedDuplicateCooker,
                                    debugSamples,
                                    "deferred-owner-fallback");
                            }
                            if (addedForOwner <= 0)
                            {
                                AddNetCookScanDebugSample(debugSamples, "deferred-owner owner=" + ownerCookBuildNetId + " static=" + cookerStaticId + " type=" + cookerType + " produced no burners");
                            }
                            if (!hasOwnerPosition && addedForOwner > 0)
                            {
                                this.NetCookLog("Deferred owner-window stove " + ownerCookBuildNetId + " accepted without reliable world position.");
                            }

                            added += addedForOwner;
                        }
                    }
                }

                if (this.netCookTargets.Count < NetCookMaxCaptureTargets
                    && (forceBroadRefresh || useOwnerWindow || added > 0 || this.netCookTargets.Count < NetCookDeferredBroadRefreshTargetThreshold))
                {
                    List<uint> cookBuildPins = new List<uint>();
                    if (this.TryEnumerateNetCookCookBuildComponentObjects(out List<IntPtr> cookBuildComponents, out string enumerateStatus, cookBuildPins))
                    {
                    // Direct-ECS cook-build components instead of the crash-prone entity-graph walk.
                    // Raw object pointers must not survive a yield: scalarize to owner entity netIds in
                    // this same frame, then re-resolve each by netId inside the throttled loop below.
                    try
                    {
                    List<uint> broadEntityNetIds = new List<uint>(cookBuildComponents.Count);
                    for (int compIndex = 0; compIndex < cookBuildComponents.Count; compIndex++)
                    {
                        if (this.TryGetNetCookCookBuildOwnerNetId(cookBuildComponents[compIndex], out uint candidateNetId)
                            && candidateNetId != 0U)
                        {
                            broadEntityNetIds.Add(candidateNetId);
                        }
                    }

                    float maxScanDistance = Mathf.Clamp(this.netCookScanRadiusMeters, NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters);
                    int broadFrameInspections = 0;
                    this.netCookStatus = "Refreshing nearby stove cache... " + this.netCookTargets.Count + " stove(s).";
                    for (int entityIndex = 0; entityIndex < broadEntityNetIds.Count && this.netCookTargets.Count < NetCookMaxCaptureTargets; entityIndex++)
                    {
                        broadInspected++;
                        broadFrameInspections++;
                        if (broadFrameInspections >= NetCookOwnerWindowInspectionsPerFrame)
                        {
                            broadFrameInspections = 0;
                            this.netCookStatus = "Refreshing nearby stove cache... " + this.netCookTargets.Count + " stove(s).";
                            yield return null;
                            if (captureGeneration != this.netCookCaptureGeneration)
                            {
                                yield break;
                            }
                        }

                        uint ownerCookBuildNetId = broadEntityNetIds[entityIndex];
                        if (inspectedOwnerNetIds.Contains(ownerCookBuildNetId))
                        {
                            continue;
                        }

                        if (!this.TryGetAuraMonoEntityObjectByNetId(ownerCookBuildNetId, out IntPtr ownerEntityObj) || ownerEntityObj == IntPtr.Zero
                            || !this.TryResolveNetCookBuildComponentAuraMono(ownerEntityObj, out IntPtr cookBuildComponentObj, out _))
                        {
                            continue;
                        }
                        inspectedOwnerNetIds.Add(ownerCookBuildNetId);
                        broadCookBuilds++;

                        Vector3 ownerPosition;
                        if (!this.TryGetAuraMonoEntityPosition(ownerEntityObj, out ownerPosition)
                            && !this.TryExtractHomePositionMonoObject(ownerEntityObj, out ownerPosition)
                            && !this.TryExtractHomePositionMonoObject(cookBuildComponentObj, out ownerPosition))
                        {
                            continue;
                        }
                        if (Vector3.Distance(scanOrigin, ownerPosition) > maxScanDistance)
                        {
                            continue;
                        }

                        int cookerStaticId = 0;
                        this.TryGetMonoInt32Member(cookBuildComponentObj, "_cookerStaticId", out cookerStaticId);
                        if (cookerStaticId <= 0)
                        {
                            this.TryGetMonoInt32Member(cookBuildComponentObj, "cookerStaticId", out cookerStaticId);
                        }

                        int cookerType = 0;
                        if (cookerStaticId > 0 && cookerStaticId == desiredCookerStaticId && desiredCookerType > 0)
                        {
                            cookerType = desiredCookerType;
                        }
                        else if (cookerStaticId > 0)
                        {
                            this.TryGetCookerTypeForStaticId(cookerStaticId, out cookerType);
                        }
                        if (cookerType <= 0)
                        {
                            cookerType = desiredCookerType;
                        }

                        if (!this.IsCompatibleNetCookCooker(cookerStaticId, cookerType, desiredCookerStaticId, desiredCookerType))
                        {
                            skippedDifferentCooker++;
                            continue;
                        }

                        this.RegisterNetCookWorldCooker(ownerCookBuildNetId, 0, cookerStaticId, cookerType);
                        int addedForOwner = this.TryAddCookBuildBurnerMapTargetsAuraMono(
                            ownerCookBuildNetId,
                            ownerEntityObj,
                            cookBuildComponentObj,
                            this.netCookTargets,
                            seenTargets,
                            seenCookerNetIds,
                            ownerPosition,
                            desiredCookerStaticId,
                            desiredCookerType,
                            ref skippedDifferentCooker,
                            ref skippedDuplicateCooker);
                        if (addedForOwner <= 0)
                        {
                            addedForOwner = this.TryAddSynthesizedNetCookBurnerTargets(
                                ownerCookBuildNetId,
                                ownerPosition,
                                cookerStaticId,
                                cookerType,
                                desiredCookerStaticId,
                                desiredCookerType,
                                this.netCookTargets,
                                seenTargets,
                                seenCookerNetIds,
                                ref skippedDifferentCooker,
                                ref skippedDuplicateCooker,
                                debugSamples,
                                "deferred-broad");
                        }

                        if (addedForOwner > 0)
                        {
                            broadAdded += addedForOwner;
                            added += addedForOwner;
                        }
                    }
                    }
                    finally
                    {
                        FreeAuraMonoPins(cookBuildPins);
                    }
                    }
                    else
                    {
                        FreeAuraMonoPins(cookBuildPins);
                    }
                }
                else if (NetCookScanDebugLogsEnabled)
                {
                    this.NetCookLog("Deferred broad cook-build scan unavailable.");
                }

                if (broadInspected > 0)
                {
                    this.netCookLastBroadRefreshWorldScanCandidateCount = this.netCookLastDeferredWorldScanCandidateCount;
                    this.nextNetCookBroadRefreshAllowedAt = Time.unscaledTime + NetCookBroadRefreshCooldownSeconds;
                }

                if (added > 0)
                {
                    int removedDifferentCooker = this.RemoveIncompatibleNetCookTargets(this.netCookTargets, seenTargets, seenCookerNetIds, desiredCookerStaticId, desiredCookerType);
                    if (removedDifferentCooker > 0)
                    {
                        this.NetCookLog("Filtered " + removedDifferentCooker + " incompatible deferred cooker target(s).");
                    }

                    int removedOutOfRange = this.RemoveOutOfRangeNetCookTargets(this.netCookTargets, seenTargets, seenCookerNetIds);
                    if (removedOutOfRange > 0)
                    {
                        this.NetCookLog("Filtered " + removedOutOfRange + " deferred cooker target(s) outside scan radius.");
                    }

                    this.RegisterNetCookTargets(this.netCookTargets);
                    this.SortNetCookTargetsByDistanceFromScanOrigin(this.netCookTargets);
                }

                this.netCookStatus = "Captured " + this.netCookTargets.Count + " nearby stove(s) within " + Mathf.Clamp(this.netCookScanRadiusMeters, NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters).ToString("F0") + "m.";
                this.SyncNetCookCaptureDebugEsp();
                this.AddMenuNotification(this.netCookStatus, new Color(0.45f, 1f, 0.55f));
                this.NetCookLog("Deferred stove refresh seeds=" + seeds.Count + " ownerWindow=" + useOwnerWindow + " window=+/-" + NetCookFastOwnerNetIdProbeWindow + " inspectedOwners=" + inspectedOwnerNetIds.Count + " ownerEntities=" + ownerCandidatesWithEntity + " ownerCookBuilds=" + ownerCandidatesWithCookBuild + " broadInspected=" + broadInspected + " broadCookBuilds=" + broadCookBuilds + " broadAdded=" + broadAdded + " added=" + added + ".");
                this.NetCookLog(this.netCookStatus);
                this.LogNetCookTargetSummary(this.netCookTargets);
            }
            finally
            {
                if (captureGeneration == this.netCookCaptureGeneration)
                {
                    this.netCookCaptureCoroutine = null;
                }
            }
        }

        private bool TryInvokeNetCookPrepare(int recipeId, List<uint> materials)
        {
            string materialPreview = materials == null || materials.Count == 0
                ? "<none>"
                : string.Join(", ", materials.Take(Math.Min(materials.Count, 8)));
            this.NetCookLog("Prepare send attempt recipe=" + recipeId
                + " cookerNetId=" + this.netCookCookerNetId
                + " levelObject=" + this.netCookLevelObjectNetId
                + " cookerType=" + this.netCookCookerType
                + " magicSpice=" + NetCookUseMagicSpice
                + " materials=[" + materialPreview + "]");
            try
            {
                if (this.TryInvokeNetCookInteractionCommand(out string interactionStatus))
                {
                    this.NetCookLog("PrepareCooking sent via StartCookCommand.");
                    return true;
                }

                this.NetCookLog("StartCookCommand prepare unavailable: " + interactionStatus);

                if (this.TryInvokeNetCookCookingSystemPrepareAuraMono(out string auraPrepareStatus))
                {
                    this.NetCookLog("PrepareCooking sent via AuraMono CookingSystem.");
                    return true;
                }

                this.NetCookLog("AuraMono CookingSystem prepare unavailable: " + auraPrepareStatus);

                if (!this.EnsureNetCookProtocolMethods())
                {
                    if (this.TrySendNetCookPrepareCommand(recipeId, materials, out string directStatus))
                    {
                        this.NetCookLog("PrepareCooking sent via direct command fallback.");
                        return true;
                    }

                    this.NetCookLog("Prepare direct fallback failed: " + directStatus);
                    return false;
                }

                List<uint> payloadMaterials = new List<uint>(materials);
                this.netCookPrepareMethod.Invoke(null, new object[]
                {
                    this.netCookCookerNetId,
                    this.netCookLevelObjectNetId,
                    recipeId,
                    payloadMaterials,
                    NetCookUseMagicSpice
                });
                return true;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                this.NetCookLog("PrepareCooking exception: " + inner.GetType().Name + ": " + inner.Message);
                if (this.TrySendNetCookPrepareCommand(recipeId, materials, out string fallbackStatus))
                {
                    this.NetCookLog("PrepareCooking reflection failed; direct command fallback succeeded.");
                    return true;
                }

                this.netCookStatus = "Prepare exception: " + inner.Message;
                if (!string.IsNullOrWhiteSpace(fallbackStatus))
                {
                    this.NetCookLog("Prepare fallback failed: " + fallbackStatus);
                }

                return false;
            }
        }

        private bool TryInvokeNetCookInteractionCommand(out string status)
        {
            status = "StartCook interaction unavailable.";
            try
            {
                if (!this.EnsureNetCookInteractionMethod())
                {
                    status = this.netCookStatus ?? status;
                    return false;
                }

                object evt = Activator.CreateInstance(this.netCookStartCookCommandEventType);
                this.TrySetFieldValue(this.netCookStartCookCommandEventType, ref evt, "cookerNetId", this.netCookCookerNetId);
                this.TrySetFieldValue(this.netCookStartCookCommandEventType, ref evt, "levelObjectNetId", this.netCookLevelObjectNetId);
                this.TrySetFieldValue(this.netCookStartCookCommandEventType, ref evt, "useMagicSpice", NetCookUseMagicSpice);

                object result = this.netCookExecuteClickCommandMethod.Invoke(null, new object[] { evt, true });
                int errorCode = result is int code ? code : -1;
                this.NetCookLog("StartCookCommand ExecuteClickCommand result=" + errorCode);
                if (errorCode != 0)
                {
                    status = "StartCookCommand rejected (" + errorCode + ").";
                    return false;
                }

                status = "StartCookCommand accepted.";
                return true;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                status = "StartCookCommand exception: " + inner.Message;
                this.NetCookLog("StartCookCommand exception: " + inner.GetType().Name + ": " + inner.Message);
                return false;
            }
        }

        private unsafe bool TryInvokeNetCookCookingSystemPrepareAuraMono(out string status)
        {
            status = "AuraMono CookingSystem prepare unavailable.";
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    status = "AuraMono API unavailable.";
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Cooking.CookingSystem", out IntPtr cookingSystemObj) || cookingSystemObj == IntPtr.Zero)
                {
                    status = "CookingSystem mono module unavailable.";
                    return false;
                }

                IntPtr cookingSystemClass = auraMonoObjectGetClass(cookingSystemObj);
                if (cookingSystemClass == IntPtr.Zero)
                {
                    status = "CookingSystem mono class unavailable.";
                    return false;
                }

                IntPtr initDetailMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "InitCookingRecipeDetail", 1);
                if (initDetailMethod != IntPtr.Zero && this.netCookRecipeId > 0)
                {
                    int recipeId = this.netCookRecipeId;
                    IntPtr initExc = IntPtr.Zero;
                    IntPtr* initArgs = stackalloc IntPtr[1];
                    initArgs[0] = (IntPtr)(&recipeId);
                    auraMonoRuntimeInvoke(initDetailMethod, cookingSystemObj, (IntPtr)initArgs, ref initExc);
                    if (initExc != IntPtr.Zero)
                    {
                        status = "InitCookingRecipeDetail raised exception.";
                        return false;
                    }
                }

                IntPtr prepareMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "PrepareCooking", 3);
                if (prepareMethod == IntPtr.Zero)
                {
                    status = "PrepareCooking mono method unavailable.";
                    return false;
                }

                uint cookerNetId = this.netCookCookerNetId;
                ulong levelObjectNetId = this.netCookLevelObjectNetId;
                bool useMagicSpice = NetCookUseMagicSpice;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&cookerNetId);
                args[1] = (IntPtr)(&levelObjectNetId);
                args[2] = (IntPtr)(&useMagicSpice);
                auraMonoRuntimeInvoke(prepareMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "PrepareCooking raised exception.";
                    return false;
                }

                status = "AuraMono CookingSystem prepare sent.";
                return true;
            }
            catch (Exception ex)
            {
                status = "AuraMono CookingSystem prepare exception: " + ex.Message;
                this.NetCookLog("AuraMono CookingSystem PrepareCooking exception: " + ex);
                return false;
            }
        }

        private bool TryInvokeNetCookStart()
        {
            if (this.TryInvokeNetCookProtocolAuraMono("StartCooking", out string auraStatus))
            {
                this.NetCookLog("CookingProtocolManager.StartCooking sent via AuraMono.");
                return true;
            }
            if (!string.IsNullOrWhiteSpace(auraStatus))
            {
                this.NetCookLog("AuraMono StartCooking unavailable: " + auraStatus);
            }

            try
            {
                if (!this.EnsureNetCookProtocolMethods())
                {
                    if (this.TrySendNetCookStartCommand(out string directStatus))
                    {
                        this.NetCookLog("StartCooking sent via direct command fallback.");
                        return true;
                    }

                    this.NetCookLog("Start direct fallback failed: " + directStatus);
                    return false;
                }

                this.netCookStartMethod.Invoke(null, new object[]
                {
                    this.netCookCookerNetId,
                    this.netCookLevelObjectNetId
                });
                return true;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                this.NetCookLog("StartCooking exception: " + inner.GetType().Name + ": " + inner.Message);
                if (this.TrySendNetCookStartCommand(out string fallbackStatus))
                {
                    this.NetCookLog("StartCooking reflection failed; direct command fallback succeeded.");
                    return true;
                }

                this.netCookStatus = "Start exception: " + inner.Message;
                if (!string.IsNullOrWhiteSpace(fallbackStatus))
                {
                    this.NetCookLog("Start fallback failed: " + fallbackStatus);
                }

                return false;
            }
        }

        private bool TryInvokeNetCookContinue()
        {
            if (this.TryInvokeNetCookProtocolAuraMono("ContinueCooking", out string auraStatus))
            {
                this.NetCookLog("CookingProtocolManager.ContinueCooking sent via AuraMono.");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(auraStatus))
            {
                this.NetCookLog("AuraMono ContinueCooking unavailable: " + auraStatus);
            }

            if (this.TrySendNetCookContinueCommand(out string directStatus))
            {
                this.NetCookLog("ContinueCooking sent via direct command fallback.");
                return true;
            }

            this.NetCookLog("Continue direct fallback failed: " + directStatus);
            return false;
        }

        private bool HasPendingNetCookPrepareTarget(float now)
        {
            if (this.netCookTargets.Count <= 1)
            {
                return false;
            }

            for (int i = 0; i < this.netCookTargets.Count; i++)
            {
                NetCookTargetContext other = this.netCookTargets[i];
                if (other == null)
                {
                    continue;
                }

                if (other.Phase == 0 && now >= other.NextActionAt)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryInvokeNetCookInteract()
        {
            if (this.TryInvokeNetCookProtocolAuraMono("InteractWithCooker", out string auraStatus))
            {
                this.NetCookLog("CookingProtocolManager.InteractWithCooker sent via AuraMono.");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(auraStatus))
            {
                this.NetCookLog("AuraMono InteractWithCooker unavailable: " + auraStatus);
            }

            if (this.TrySendNetCookInteractCommand(out string directStatus))
            {
                this.NetCookLog("InteractWithCooker sent via direct command fallback.");
                return true;
            }

            this.NetCookLog("Interact direct fallback failed: " + directStatus);
            return false;
        }

        private unsafe bool TryInvokeNetCookProtocolAuraMono(string methodName, out string status)
        {
            status = "AuraMono cooking protocol unavailable.";
            try
            {
                if (string.IsNullOrWhiteSpace(methodName)
                    || !this.EnsureAuraMonoApiReady()
                    || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null)
                {
                    status = "AuraMono API unavailable.";
                    return false;
                }

                IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Cooking.CookingProtocolManager");
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.Cooking", "CookingProtocolManager");
                }
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "CookingProtocolManager");
                }
                if (protocolClass == IntPtr.Zero)
                {
                    status = "CookingProtocolManager mono class unavailable.";
                    return false;
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(protocolClass, methodName, 2);
                if (method == IntPtr.Zero)
                {
                    status = methodName + " mono method unavailable.";
                    return false;
                }

                uint cookerNetId = this.netCookCookerNetId;
                ulong levelObjectNetId = this.netCookLevelObjectNetId;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&cookerNetId);
                args[1] = (IntPtr)(&levelObjectNetId);
                auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = methodName + " raised exception.";
                    return false;
                }

                status = methodName + " sent.";
                return true;
            }
            catch (Exception ex)
            {
                status = methodName + " exception: " + ex.Message;
                this.NetCookLog("AuraMono " + methodName + " exception: " + ex);
                return false;
            }
        }

        private bool TrySendNetCookPrepareCommand(int recipeId, List<uint> materials, out string status)
        {
            status = "Prepare command unavailable.";
            try
            {
                if (!this.EnsureNetCookDirectCommandMethods())
                {
                    status = this.netCookStatus ?? status;
                    return false;
                }

                object command = Activator.CreateInstance(this.netCookPrepareCommandType);
                this.TrySetFieldValue(this.netCookPrepareCommandType, ref command, "LevelObjectNetId", this.netCookLevelObjectNetId);
                this.TrySetFieldValue(this.netCookPrepareCommandType, ref command, "CookingRecipeId", recipeId);
                this.TrySetFieldValue(this.netCookPrepareCommandType, ref command, "UseMagicSpice", NetCookUseMagicSpice);

                FieldInfo cookingTypeField = this.netCookPrepareCommandType.GetField("CookingType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (cookingTypeField != null && cookingTypeField.FieldType != null)
                {
                    object cookerTypeValue = Enum.ToObject(cookingTypeField.FieldType, this.netCookCookerType);
                    cookingTypeField.SetValue(command, cookerTypeValue);
                }

                FieldInfo materialsField = this.netCookPrepareCommandType.GetField("Materials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (materialsField != null)
                {
                    object materialList = this.CreateCompatibleUIntList(materialsField.FieldType, materials);
                    materialsField.SetValue(command, materialList);
                }

                object result = this.netCookSendPrepareCommandMethod.Invoke(null, new object[] { command, true, this.netCookReliableChannelValue });
                int sendCode = result is int code ? code : -1;
                this.NetCookLog("Prepare direct send result=" + sendCode);
                if (sendCode < 0)
                {
                    status = "Prepare direct send failed (" + sendCode + ").";
                    return false;
                }

                status = "Prepare direct send ok.";
                return true;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                status = "Prepare direct send exception: " + inner.Message;
                this.NetCookLog("Prepare direct send exception: " + inner.GetType().Name + ": " + inner.Message);
                return false;
            }
        }

        private bool TrySendNetCookStartCommand(out string status)
        {
            status = "Start command unavailable.";
            try
            {
                if (!this.EnsureNetCookDirectCommandMethods())
                {
                    status = this.netCookStatus ?? status;
                    return false;
                }

                object command = Activator.CreateInstance(this.netCookStartCommandType);
                this.TrySetFieldValue(this.netCookStartCommandType, ref command, "LevelObjectNetId", this.netCookLevelObjectNetId);
                object result = this.netCookSendStartCommandMethod.Invoke(null, new object[] { command, true, this.netCookReliableChannelValue });
                int sendCode = result is int code ? code : -1;
                this.NetCookLog("Start direct send result=" + sendCode);
                if (sendCode < 0)
                {
                    status = "Start direct send failed (" + sendCode + ").";
                    return false;
                }

                status = "Start direct send ok.";
                return true;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                status = "Start direct send exception: " + inner.Message;
                this.NetCookLog("Start direct send exception: " + inner.GetType().Name + ": " + inner.Message);
                return false;
            }
        }

        private bool TrySendNetCookContinueCommand(out string status)
        {
            return this.TrySendNetCookLevelObjectCommand(this.netCookContinueCommandType, this.netCookSendContinueCommandMethod, "Continue", out status);
        }

        private bool TrySendNetCookInteractCommand(out string status)
        {
            return this.TrySendNetCookLevelObjectCommand(this.netCookInteractCommandType, this.netCookSendInteractCommandMethod, "Interact", out status);
        }

        private bool TrySendNetCookLevelObjectCommand(Type commandType, MethodInfo sendMethod, string label, out string status)
        {
            status = label + " command unavailable.";
            try
            {
                if (!this.EnsureNetCookDirectCommandMethods())
                {
                    status = this.netCookStatus ?? status;
                    return false;
                }

                commandType = label == "Continue" ? this.netCookContinueCommandType : commandType;
                commandType = label == "Interact" ? this.netCookInteractCommandType : commandType;
                sendMethod = label == "Continue" ? this.netCookSendContinueCommandMethod : sendMethod;
                sendMethod = label == "Interact" ? this.netCookSendInteractCommandMethod : sendMethod;
                if (commandType == null || sendMethod == null)
                {
                    status = label + " direct command method unavailable.";
                    return false;
                }

                object command = Activator.CreateInstance(commandType);
                this.TrySetFieldValue(commandType, ref command, "LevelObjectNetId", this.netCookLevelObjectNetId);
                object result = sendMethod.Invoke(null, new object[] { command, true, this.netCookReliableChannelValue });
                int sendCode = result is int code ? code : -1;
                this.NetCookLog(label + " direct send result=" + sendCode);
                if (sendCode < 0)
                {
                    status = label + " direct send failed (" + sendCode + ").";
                    return false;
                }

                status = label + " direct send ok.";
                return true;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                status = label + " direct send exception: " + inner.Message;
                this.NetCookLog(label + " direct send exception: " + inner.GetType().Name + ": " + inner.Message);
                return false;
            }
        }

        private bool EnsureNetCookProtocolMethods()
        {
            if (this.netCookPrepareMethod != null && this.netCookStartMethod != null)
            {
                return true;
            }

            Type cookingProtocolType = this.FindLoadedType(
                "XDTDataAndProtocol.ProtocolService.Cooking.CookingProtocolManager",
                "CookingProtocolManager");
            if (cookingProtocolType == null)
            {
                this.netCookStatus = "CookingProtocolManager unavailable.";
                return false;
            }

            foreach (MethodInfo method in cookingProtocolType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method == null)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "PrepareCooking" && parameters.Length == 5)
                {
                    this.netCookPrepareMethod = method;
                }
                else if (method.Name == "StartCooking" && parameters.Length == 2)
                {
                    this.netCookStartMethod = method;
                }
            }

            if (this.netCookPrepareMethod == null || this.netCookStartMethod == null)
            {
                this.netCookStatus = "Cooking protocol methods unavailable.";
                this.NetCookLog(this.netCookStatus);
                return false;
            }

            return true;
        }

        private bool EnsureNetCookDirectCommandMethods()
        {
            if (this.netCookSendPrepareCommandMethod != null
                && this.netCookSendStartCommandMethod != null
                && this.netCookSendContinueCommandMethod != null
                && this.netCookSendInteractCommandMethod != null
                && this.netCookPrepareCommandType != null
                && this.netCookStartCommandType != null
                && this.netCookContinueCommandType != null
                && this.netCookInteractCommandType != null
                && this.netCookReliableChannelValue != null)
            {
                return true;
            }

            Type webRequestType = this.FindLoadedType(
                "XDTDataAndProtocol.ProtocolService.WebRequestUtility",
                "WebRequestUtility");
            if (webRequestType == null)
            {
                this.netCookStatus = "WebRequestUtility unavailable.";
                return false;
            }

            this.netCookPrepareCommandType = this.FindLoadedType(
                "XDT.Scene.Shared.Modules.Cooking.PrepareCookingNetworkCommand",
                "PrepareCookingNetworkCommand");
            this.netCookStartCommandType = this.FindLoadedType(
                "XDT.Scene.Shared.Modules.Cooking.StartCookingNetworkCommand",
                "StartCookingNetworkCommand");
            this.netCookContinueCommandType = this.FindLoadedType(
                "XDT.Scene.Shared.Modules.Cooking.ContinueCookingNetworkCommand",
                "ContinueCookingNetworkCommand");
            this.netCookInteractCommandType = this.FindLoadedType(
                "XDT.Scene.Shared.Modules.Cooking.CookingInteractNetworkCommand",
                "CookingInteractNetworkCommand");
            if (this.netCookPrepareCommandType == null
                || this.netCookStartCommandType == null
                || this.netCookContinueCommandType == null
                || this.netCookInteractCommandType == null)
            {
                this.netCookStatus = "Cooking command types unavailable. Prepare=" + (this.netCookPrepareCommandType != null)
                    + " Start=" + (this.netCookStartCommandType != null)
                    + " Continue=" + (this.netCookContinueCommandType != null)
                    + " Interact=" + (this.netCookInteractCommandType != null);
                return false;
            }

            MethodInfo sendCommandOpen = null;
            foreach (MethodInfo method in webRequestType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method == null || method.Name != "SendCommand" || !method.IsGenericMethodDefinition)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 3)
                {
                    sendCommandOpen = method;
                    break;
                }
            }

            if (sendCommandOpen == null)
            {
                this.netCookStatus = "SendCommand unavailable.";
                return false;
            }

            this.netCookSendPrepareCommandMethod = sendCommandOpen.MakeGenericMethod(this.netCookPrepareCommandType);
            this.netCookSendStartCommandMethod = sendCommandOpen.MakeGenericMethod(this.netCookStartCommandType);
            this.netCookSendContinueCommandMethod = sendCommandOpen.MakeGenericMethod(this.netCookContinueCommandType);
            this.netCookSendInteractCommandMethod = sendCommandOpen.MakeGenericMethod(this.netCookInteractCommandType);

            Type channelType = this.FindLoadedType("XD.GameGerm.Network.ChannelType", "ChannelType");
            if (channelType == null)
            {
                this.netCookStatus = "ChannelType unavailable.";
                return false;
            }

            this.netCookReliableChannelValue = Enum.Parse(channelType, "Reliable");
            return true;
        }

        private bool EnsureNetCookInteractionMethod()
        {
            if (this.netCookExecuteClickCommandMethod != null && this.netCookStartCookCommandEventType != null)
            {
                return true;
            }

            Type playerInteractionType = this.FindLoadedType(
                "XDTLevelAndEntity.Gameplay.PlayerInteraction",
                "PlayerInteraction");
            if (playerInteractionType == null)
            {
                this.netCookStatus = "PlayerInteraction unavailable.";
                return false;
            }

            this.netCookStartCookCommandEventType = this.FindLoadedType(
                "ScriptsRefactory.DataAndProtocol.Events.StartCookCommandEvent",
                "StartCookCommandEvent");
            if (this.netCookStartCookCommandEventType == null)
            {
                this.netCookStatus = "StartCookCommandEvent unavailable.";
                return false;
            }

            foreach (MethodInfo method in playerInteractionType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method == null || method.Name != "ExecuteClickCommand" || !method.IsGenericMethodDefinition)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2)
                {
                    this.netCookExecuteClickCommandMethod = method.MakeGenericMethod(this.netCookStartCookCommandEventType);
                    return true;
                }
            }

            this.netCookStatus = "ExecuteClickCommand unavailable.";
            return false;
        }

        private bool EnsureNetCookMethods()
        {
            if (this.netCookPrepareMethod != null
                && this.netCookStartMethod != null
                && this.netCookCookingSystemInstanceProperty != null
                && this.netCookInitRecipeDetailMethod != null
                && this.netCookGetAllRecipesMethod != null)
            {
                return true;
            }

            if (!this.EnsureNetCookProtocolMethods())
            {
                return false;
            }

            if (!this.EnsureNetCookSystemMethods())
            {
                return false;
            }

            if (this.netCookGetAllRecipesMethod == null)
            {
                this.netCookStatus = "GetAllRecipes unavailable.";
                this.NetCookLog(this.netCookStatus);
                return false;
            }

            return true;
        }

        private bool EnsureNetCookSystemMethods()
        {
            if (this.netCookCookingSystemInstanceProperty != null
                && this.netCookInitRecipeDetailMethod != null
                && this.netCookGetAllRecipesMethod != null)
            {
                return true;
            }

            Type cookingSystemType = this.FindNetCookCookingSystemType();
            if (cookingSystemType == null)
            {
                this.netCookStatus = "CookingSystem unavailable.";
                return false;
            }

            this.netCookCookingSystemInstanceProperty = cookingSystemType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            this.netCookInitRecipeDetailMethod = cookingSystemType.GetMethod("InitCookingRecipeDetail", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null);
            this.netCookGetRecipeDetailMethod = cookingSystemType.GetMethod("GetRecipeDetail", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null);
            this.netCookGetAllRecipesMethod = cookingSystemType.GetMethod("GetAllRecipes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null);
            this.netCookRefreshSlotsMethod = cookingSystemType.GetMethod("RefreshSlots", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

            if (this.netCookGetAllRecipesMethod == null)
            {
                foreach (MethodInfo method in cookingSystemType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method == null || method.Name != "GetAllRecipes")
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1)
                    {
                        this.netCookGetAllRecipesMethod = method;
                        break;
                    }
                }
            }

            if (this.netCookCookingSystemInstanceProperty == null || this.netCookInitRecipeDetailMethod == null || this.netCookGetAllRecipesMethod == null)
            {
                this.netCookStatus = "CookingSystem methods unavailable.";
                return false;
            }

            return true;
        }

        private Type FindNetCookCookingSystemType()
        {
            Type directType = this.FindLoadedType(
                "XDTGameSystem.GameplaySystem.Cooking.CookingSystem",
                "CookingSystem");
            if (directType != null)
            {
                return directType;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    string fullName = type.FullName ?? type.Name ?? string.Empty;
                    bool nameLooksRelevant = fullName.IndexOf("CookingSystem", StringComparison.OrdinalIgnoreCase) >= 0
                        || fullName.IndexOf("GameplaySystem.Cooking", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool hasRecipeCacheField = this.FindFieldInHierarchy(type, "_cookingRecipesCache") != null;
                    if (!nameLooksRelevant && !hasRecipeCacheField)
                    {
                        continue;
                    }

                    if (!type.IsClass)
                    {
                        continue;
                    }

                    PropertyInfo instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    if (instanceProperty == null)
                    {
                        PropertyInfo dataModuleInstanceProperty = this.GetDataModuleInstanceProperty(type);
                        if (dataModuleInstanceProperty != null)
                        {
                            instanceProperty = dataModuleInstanceProperty;
                        }
                    }

                    MethodInfo initRecipeDetailMethod = type.GetMethod("InitCookingRecipeDetail", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null);
                    MethodInfo getAllRecipesMethod = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == "GetAllRecipes" && m.GetParameters().Length == 1);
                    if (instanceProperty != null && getAllRecipesMethod != null && (initRecipeDetailMethod != null || hasRecipeCacheField))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private bool EnsureNetCookRecipeCache()
        {
            if (this.netCookCookerStaticId <= 0)
            {
                this.netCookStatus = "No cooker captured.";
                return false;
            }

            if (this.netCookEnabled && this.netCookTargets.Count > 0 && this.netCookRecipeEntries.Count > 0)
            {
                return true;
            }

            if (this.netCookRecipeEntries.Count > 0 && this.netCookRecipeCacheCookerStaticId == this.netCookCookerStaticId)
            {
                return true;
            }

            if (this.netCookRecipeCacheFailureCookerStaticId == this.netCookCookerStaticId && Time.time < this.nextNetCookRecipeCacheRetryAt)
            {
                return false;
            }

            try
            {
                this.NetCookLog("Rebuilding recipe cache for cookerStaticId=" + this.netCookCookerStaticId + " cookerType=" + this.netCookCookerType + "...");
                this.netCookRecipeEntries.Clear();
                this.netCookRecipeCookerTypes.Clear();
                this.netCookRecipeCacheCookerStaticId = 0;

                if (this.TryBuildNetCookRecipeCacheFromCookingSystemAllRecipesAuraMono())
                {
                    this.ResetNetCookRecipeCacheRetry();
                    return this.netCookRecipeEntries.Count > 0;
                }

                if (this.TryBuildNetCookRecipeCacheFromUnlockedRecipes())
                {
                    this.ResetNetCookRecipeCacheRetry();
                    return this.netCookRecipeEntries.Count > 0;
                }

                if (!this.EnsureNetCookMethods())
                {
                    this.MarkNetCookRecipeCacheRetry();
                    return false;
                }

                if (this.netCookCookingSystemInstanceProperty == null || this.netCookGetAllRecipesMethod == null)
                {
                    this.netCookStatus = "Runtime recipe methods unavailable.";
                    this.NetCookLog(this.netCookStatus);
                    this.MarkNetCookRecipeCacheRetry();
                    return false;
                }

                object cookingSystem = this.netCookCookingSystemInstanceProperty.GetValue(null, null);
                if (cookingSystem == null)
                {
                    this.netCookStatus = "CookingSystem instance unavailable.";
                    this.NetCookLog(this.netCookStatus);
                    this.MarkNetCookRecipeCacheRetry();
                    return false;
                }

                object allRecipesObj = this.netCookGetAllRecipesMethod.Invoke(cookingSystem, new object[] { this.netCookCookerStaticId });
                if (allRecipesObj == null)
                {
                    this.netCookStatus = "Runtime recipe list is null.";
                    this.NetCookLog(this.netCookStatus);
                    this.MarkNetCookRecipeCacheRetry();
                    return false;
                }

                this.NetCookLog("Runtime recipe list type=" + (allRecipesObj.GetType().FullName ?? allRecipesObj.GetType().Name));

                List<(int recipeId, string recipeName, int sortOrder)> runtimeRecipes = new List<(int, string, int)>(128);
                List<object> recipeObjects = new List<object>(128);
                if (!this.TryEnumerateManagedCollectionItems(allRecipesObj, recipeObjects))
                {
                    this.netCookStatus = "Runtime recipe list enumeration failed.";
                    this.NetCookLog(this.netCookStatus);
                    return false;
                }

                this.NetCookLog("Runtime recipe objects=" + recipeObjects.Count);

                for (int recipeIndex = 0; recipeIndex < recipeObjects.Count; recipeIndex++)
                {
                    object recipeObj = recipeObjects[recipeIndex];
                    if (recipeObj == null || !this.TryGetObjectMember(recipeObj, "staticId", out object staticIdObj) || staticIdObj == null)
                    {
                        continue;
                    }

                    int recipeId;
                    try
                    {
                        recipeId = Convert.ToInt32(staticIdObj);
                    }
                    catch
                    {
                        continue;
                    }

                    string recipeName = "Recipe " + recipeId;
                    if (this.TryGetObjectMember(recipeObj, "name", out object nameObj) && nameObj is string entityName && !string.IsNullOrWhiteSpace(entityName))
                    {
                        recipeName = entityName.Trim();
                    }

                    int sortOrder = recipeId;
                    if (this.TryGetObjectMember(recipeObj, "sortOrder", out object sortOrderObj) && sortOrderObj != null)
                    {
                        try
                        {
                            sortOrder = Convert.ToInt32(sortOrderObj);
                        }
                        catch
                        {
                        }
                    }

                    if (this.netCookCookerType > 0)
                    {
                        this.netCookRecipeCookerTypes[recipeId] = this.netCookCookerType;
                    }

                    runtimeRecipes.Add((recipeId, recipeName, sortOrder));
                }

                runtimeRecipes.Sort((a, b) =>
                {
                    int bySort = a.sortOrder.CompareTo(b.sortOrder);
                    if (bySort != 0)
                    {
                        return bySort;
                    }
                    int byName = string.Compare(a.recipeName, b.recipeName, StringComparison.OrdinalIgnoreCase);
                    if (byName != 0)
                    {
                        return byName;
                    }
                    return a.recipeId.CompareTo(b.recipeId);
                });

                for (int i = 0; i < runtimeRecipes.Count; i++)
                {
                    var recipe = runtimeRecipes[i];
                    this.netCookRecipeEntries.Add(new KeyValuePair<int, string>(recipe.recipeId, recipe.recipeName));
                }

                this.netCookRecipeCacheCookerStaticId = this.netCookCookerStaticId;
                if (this.netCookRecipeEntries.Count == 0)
                {
                    this.netCookStatus = "No recipes available for this cooker.";
                    this.NetCookLog(this.netCookStatus + " staticId=" + this.netCookCookerStaticId + " cookerType=" + this.netCookCookerType);
                }
                else
                {
                    if (NetCookLogsEnabled)
                    {
                        this.NetCookLog("Recipe cache ready count=" + this.netCookRecipeEntries.Count + " first=[" + string.Join(", ", this.netCookRecipeEntries.Take(Math.Min(6, this.netCookRecipeEntries.Count)).Select(kv => kv.Key + ":" + kv.Value).ToArray()) + "]");
                    }
                }

                this.ResetNetCookRecipeCacheRetry();
                return this.netCookRecipeEntries.Count > 0;
            }
            catch (Exception ex)
            {
                this.netCookStatus = "Recipe cache failed: " + ex.Message;
                this.NetCookLog(this.netCookStatus);
                this.MarkNetCookRecipeCacheRetry();
                return false;
            }
        }

        private bool TryBuildNetCookRecipeCacheFromUnlockedRecipes()
        {
            try
            {
                if (!this.TryGetUnlockedCookingRecipeIds(out List<int> unlockedRecipeIds, out string status))
                {
                    this.NetCookLog("Unlocked recipe scan unavailable. " + status);
                    return false;
                }

                this.NetCookLog("Unlocked cooking recipes=" + unlockedRecipeIds.Count);

                if (this.TryBuildNetCookRecipeCacheFromUnlockedRecipesAuraMono(unlockedRecipeIds))
                {
                    return true;
                }

                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    this.NetCookLog("Unlocked recipe scan missing TableData.");
                    return false;
                }

                MethodInfo getCookingRecipeMethod = tableDataType.GetMethod("GetCookingRecipe", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(int), typeof(bool) }, null);
                MethodInfo getEntityMethod = tableDataType.GetMethod("GetEntity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(int), typeof(bool) }, null);
                if (getCookingRecipeMethod == null || getEntityMethod == null)
                {
                    this.NetCookLog("Unlocked recipe scan missing table lookup methods.");
                    return false;
                }

                List<(int recipeId, string recipeName, int sortOrder)> unlockedRecipes = new List<(int, string, int)>(unlockedRecipeIds.Count);
                for (int i = 0; i < unlockedRecipeIds.Count; i++)
                {
                    int recipeId = unlockedRecipeIds[i];
                    object recipeTable = getCookingRecipeMethod.Invoke(null, new object[] { recipeId, false });
                    if (recipeTable == null)
                    {
                        continue;
                    }

                    int recipeCookerType = 0;
                    if (this.TryGetObjectMember(recipeTable, "cookerType", out object cookerTypeObj) && cookerTypeObj != null)
                    {
                        recipeCookerType = Convert.ToInt32(cookerTypeObj);
                    }

                    this.netCookRecipeCookerTypes[recipeId] = recipeCookerType;
                    if (this.netCookCookerType > 0 && recipeCookerType > 0 && recipeCookerType != this.netCookCookerType)
                    {
                        continue;
                    }

                    int sortOrder = recipeId;
                    if (this.TryGetObjectMember(recipeTable, "sortOrder", out object sortOrderObj) && sortOrderObj != null)
                    {
                        sortOrder = Convert.ToInt32(sortOrderObj);
                    }

                    string recipeName = "Recipe " + recipeId;
                    object entityTable = getEntityMethod.Invoke(null, new object[] { recipeId, false });
                    if (entityTable != null && this.TryGetObjectMember(entityTable, "name", out object recipeNameObj) && recipeNameObj is string entityName && !string.IsNullOrWhiteSpace(entityName))
                    {
                        recipeName = entityName.Trim();
                    }

                    unlockedRecipes.Add((recipeId, recipeName, sortOrder));
                }

                unlockedRecipes.Sort((a, b) =>
                {
                    int bySort = a.sortOrder.CompareTo(b.sortOrder);
                    if (bySort != 0)
                    {
                        return bySort;
                    }
                    int byName = string.Compare(a.recipeName, b.recipeName, StringComparison.OrdinalIgnoreCase);
                    if (byName != 0)
                    {
                        return byName;
                    }
                    return a.recipeId.CompareTo(b.recipeId);
                });

                for (int i = 0; i < unlockedRecipes.Count; i++)
                {
                    var recipe = unlockedRecipes[i];
                    this.netCookRecipeEntries.Add(new KeyValuePair<int, string>(recipe.recipeId, recipe.recipeName));
                }

                this.netCookRecipeCacheCookerStaticId = this.netCookCookerStaticId;
                if (this.netCookRecipeEntries.Count == 0)
                {
                    this.netCookStatus = "No unlocked recipes available for this cooker.";
                    this.NetCookLog(this.netCookStatus + " staticId=" + this.netCookCookerStaticId + " cookerType=" + this.netCookCookerType);
                }
                else
                {
                    if (NetCookLogsEnabled)
                    {
                        this.NetCookLog("Unlocked recipe cache ready count=" + this.netCookRecipeEntries.Count + " first=[" + string.Join(", ", this.netCookRecipeEntries.Take(Math.Min(6, this.netCookRecipeEntries.Count)).Select(kv => kv.Key + ":" + kv.Value).ToArray()) + "]");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                this.NetCookLog("Unlocked recipe scan exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryBuildNetCookRecipeCacheFromCookingSystemAllRecipesAuraMono()
        {
            try
            {
                if (this.netCookCookerStaticId <= 0)
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Cooking.CookingSystem", out IntPtr cookingSystemObj)
                    || cookingSystemObj == IntPtr.Zero
                    || auraMonoObjectGetClass == null
                    || auraMonoRuntimeInvoke == null)
                {
                    this.NetCookLog("CookingSystem AuraMono recipe cache unavailable.");
                    return false;
                }

                IntPtr cookingSystemClass = auraMonoObjectGetClass(cookingSystemObj);
                if (cookingSystemClass == IntPtr.Zero)
                {
                    this.NetCookLog("CookingSystem AuraMono recipe cache missing class.");
                    return false;
                }

                IntPtr getAllRecipesMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "GetAllRecipes", 1);
                if (getAllRecipesMethod == IntPtr.Zero)
                {
                    this.NetCookLog("CookingSystem AuraMono recipe cache missing GetAllRecipes.");
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                int cookerStaticId = this.netCookCookerStaticId;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&cookerStaticId);
                IntPtr recipeListObj = auraMonoRuntimeInvoke(getAllRecipesMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || recipeListObj == IntPtr.Zero)
                {
                    this.NetCookLog("CookingSystem AuraMono GetAllRecipes returned no recipe list.");
                    return false;
                }

                List<IntPtr> recipeItems = new List<IntPtr>(256);
                List<uint> recipePins = new List<uint>();
                if (!this.TryEnumerateAuraMonoCollectionItems(recipeListObj, recipeItems, recipePins) || recipeItems.Count <= 0)
                {
                    FreeAuraMonoPins(recipePins);
                    this.NetCookLog("CookingSystem AuraMono GetAllRecipes enumeration returned 0 recipes.");
                    return false;
                }

                List<(int recipeId, string recipeName, int sortOrder)> runtimeRecipes = new List<(int, string, int)>(recipeItems.Count);
                try
                {
                for (int i = 0; i < recipeItems.Count; i++)
                {
                    IntPtr recipeObj = recipeItems[i];
                    if (recipeObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    int recipeId = 0;
                    if (!this.TryGetMonoInt32Member(recipeObj, "staticId", out recipeId)
                        && !this.TryGetMonoInt32Member(recipeObj, "StaticId", out recipeId)
                        && !this.TryGetMonoIntMember(recipeObj, "staticId", out recipeId)
                        && !this.TryGetMonoIntMember(recipeObj, "StaticId", out recipeId))
                    {
                        continue;
                    }

                    if (recipeId <= 0)
                    {
                        continue;
                    }

                    string recipeName = string.Empty;
                    if (!this.TryGetMonoStringMember(recipeObj, "name", out recipeName)
                        && !this.TryGetMonoStringMember(recipeObj, "Name", out recipeName))
                    {
                        recipeName = string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(recipeName)
                        && this.TryResolveNetCookRecipeDetailNameAuraMono(cookingSystemObj, cookingSystemClass, recipeId, out string detailName))
                    {
                        recipeName = detailName;
                    }

                    if (string.IsNullOrWhiteSpace(recipeName))
                    {
                        recipeName = "Recipe " + recipeId;
                    }
                    else
                    {
                        recipeName = recipeName.Trim();
                    }

                    int sortOrder = recipeId;
                    if (!this.TryGetMonoInt32Member(recipeObj, "sortOrder", out sortOrder)
                        && !this.TryGetMonoInt32Member(recipeObj, "SortOrder", out sortOrder)
                        && !this.TryGetMonoIntMember(recipeObj, "sortOrder", out sortOrder)
                        && !this.TryGetMonoIntMember(recipeObj, "SortOrder", out sortOrder))
                    {
                        sortOrder = recipeId;
                    }

                    if (this.netCookCookerType > 0)
                    {
                        this.netCookRecipeCookerTypes[recipeId] = this.netCookCookerType;
                    }

                    runtimeRecipes.Add((recipeId, recipeName, sortOrder));
                }

                runtimeRecipes.Sort((a, b) =>
                {
                    int bySort = a.sortOrder.CompareTo(b.sortOrder);
                    if (bySort != 0)
                    {
                        return bySort;
                    }

                    int byName = string.Compare(a.recipeName, b.recipeName, StringComparison.OrdinalIgnoreCase);
                    if (byName != 0)
                    {
                        return byName;
                    }

                    return a.recipeId.CompareTo(b.recipeId);
                });

                for (int i = 0; i < runtimeRecipes.Count; i++)
                {
                    var recipe = runtimeRecipes[i];
                    this.netCookRecipeEntries.Add(new KeyValuePair<int, string>(recipe.recipeId, recipe.recipeName));
                }
                }
                finally
                {
                    FreeAuraMonoPins(recipePins);
                }

                this.netCookRecipeCacheCookerStaticId = this.netCookCookerStaticId;
                if (this.netCookRecipeEntries.Count <= 0)
                {
                    this.NetCookLog("CookingSystem AuraMono GetAllRecipes produced no usable recipes.");
                    return false;
                }

                this.NetCookLog("CookingSystem AuraMono recipe cache ready count=" + this.netCookRecipeEntries.Count + " first=[" + string.Join(", ", this.netCookRecipeEntries.Take(Math.Min(6, this.netCookRecipeEntries.Count)).Select(kv => kv.Key + ":" + kv.Value).ToArray()) + "]");
                return true;
            }
            catch (Exception ex)
            {
                this.NetCookLog("CookingSystem AuraMono recipe cache exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryResolveNetCookRecipeDetailNameAuraMono(IntPtr cookingSystemObj, IntPtr cookingSystemClass, int recipeId, out string recipeName)
        {
            recipeName = string.Empty;
            if (cookingSystemObj == IntPtr.Zero || cookingSystemClass == IntPtr.Zero || recipeId <= 0 || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr getDetailMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "GetRecipeDetail", 1);
                IntPtr initDetailMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "InitCookingRecipeDetail", 1);
                IntPtr detailMethod = getDetailMethod != IntPtr.Zero ? getDetailMethod : initDetailMethod;
                if (detailMethod == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&recipeId);
                IntPtr detailObj = auraMonoRuntimeInvoke(detailMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if ((detailObj == IntPtr.Zero || exc != IntPtr.Zero) && detailMethod != initDetailMethod && initDetailMethod != IntPtr.Zero)
                {
                    exc = IntPtr.Zero;
                    detailObj = auraMonoRuntimeInvoke(initDetailMethod, cookingSystemObj, (IntPtr)args, ref exc);
                }

                if (exc != IntPtr.Zero || detailObj == IntPtr.Zero)
                {
                    return false;
                }

                return (this.TryGetMonoStringMember(detailObj, "name", out recipeName)
                        || this.TryGetMonoStringMember(detailObj, "Name", out recipeName))
                    && !string.IsNullOrWhiteSpace(recipeName);
            }
            catch
            {
                recipeName = string.Empty;
                return false;
            }
        }

        private bool TryBuildNetCookRecipeCacheFromUnlockedRecipesAuraMono(List<int> unlockedRecipeIds)
        {
            try
            {
                if (unlockedRecipeIds == null || unlockedRecipeIds.Count == 0)
                {
                    return false;
                }

                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    this.NetCookLog("Unlocked recipe mono scan unavailable: AuraMono API not ready.");
                    return false;
                }

                IntPtr ecsImage = this.FindAuraMonoImage(new[]
                {
                    "EcsClient",
                    "EcsClient.dll"
                });
                if (ecsImage == IntPtr.Zero || auraMonoClassFromName == null)
                {
                    this.NetCookLog("Unlocked recipe mono scan missing EcsClient image.");
                    return false;
                }

                IntPtr tableDataClass = auraMonoClassFromName(ecsImage, string.Empty, "TableData");
                if (tableDataClass == IntPtr.Zero)
                {
                    tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
                }

                if (tableDataClass == IntPtr.Zero)
                {
                    this.NetCookLog("Unlocked recipe mono scan missing TableData class.");
                    return false;
                }

                if (this.TryBuildNetCookRecipeCacheFromCookingSystemMono(unlockedRecipeIds, out bool builtFromCookingSystem))
                {
                    return true;
                }

                if (builtFromCookingSystem)
                {
                    return false;
                }

                IntPtr tableCookingRecipesObj = IntPtr.Zero;
                if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableCookingRecipes", out tableCookingRecipesObj) || tableCookingRecipesObj == IntPtr.Zero)
                {
                    this.NetCookLog("Unlocked recipe mono scan missing TableData.TableCookingRecipes.");
                    return false;
                }

                IntPtr tableEntitysObj = IntPtr.Zero;
                if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableEntitys", out tableEntitysObj) || tableEntitysObj == IntPtr.Zero)
                {
                    this.NetCookLog("Unlocked recipe mono scan missing TableData.TableEntitys.");
                    return false;
                }

                HashSet<int> unlockedSet = new HashSet<int>(unlockedRecipeIds);
                Dictionary<int, int> cookerTypesById = new Dictionary<int, int>(unlockedSet.Count);
                Dictionary<int, int> sortOrdersById = new Dictionary<int, int>(unlockedSet.Count);
                Dictionary<int, string> namesById = new Dictionary<int, string>(unlockedSet.Count);

                List<IntPtr> recipeItems = new List<IntPtr>(512);
                List<uint> recipePins = new List<uint>();
                if (!this.TryEnumerateAuraMonoCollectionItems(tableCookingRecipesObj, recipeItems, recipePins) || recipeItems.Count == 0)
                {
                    FreeAuraMonoPins(recipePins);
                    this.NetCookLog("Unlocked recipe mono scan failed: TableCookingRecipes enumeration unavailable.");
                    return false;
                }

                try
                {
                for (int i = 0; i < recipeItems.Count; i++)
                {
                    if (!this.TryReadNetCookRecipeTableEntryMono(recipeItems[i], unlockedSet, out int recipeId, out int cookerType, out int sortOrder))
                    {
                        continue;
                    }

                    cookerTypesById[recipeId] = cookerType;
                    sortOrdersById[recipeId] = sortOrder;
                }

                List<IntPtr> entityItems = new List<IntPtr>(2048);
                List<uint> entityPins = new List<uint>();
                if (this.TryEnumerateAuraMonoCollectionItems(tableEntitysObj, entityItems, entityPins))
                {
                    try
                    {
                    for (int i = 0; i < entityItems.Count; i++)
                    {
                        if (!this.TryReadNetCookEntityTableEntryMono(entityItems[i], unlockedSet, tableDataClass, out int entityId, out string entityName))
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(entityName))
                        {
                            namesById[entityId] = entityName.Trim();
                        }
                    }
                    }
                    finally
                    {
                        FreeAuraMonoPins(entityPins);
                    }
                }
                }
                finally
                {
                    FreeAuraMonoPins(recipePins);
                }

                List<(int recipeId, string recipeName, int sortOrder)> unlockedRecipes = new List<(int, string, int)>(unlockedRecipeIds.Count);
                for (int i = 0; i < unlockedRecipeIds.Count; i++)
                {
                    int recipeId = unlockedRecipeIds[i];
                    int recipeCookerType = 0;
                    cookerTypesById.TryGetValue(recipeId, out recipeCookerType);
                    this.netCookRecipeCookerTypes[recipeId] = recipeCookerType;

                    if (this.netCookCookerType > 0 && recipeCookerType > 0 && recipeCookerType != this.netCookCookerType)
                    {
                        continue;
                    }

                    int sortOrder = recipeId;
                    sortOrdersById.TryGetValue(recipeId, out sortOrder);
                    if (sortOrder <= 0)
                    {
                        sortOrder = recipeId;
                    }

                    string recipeName = namesById.TryGetValue(recipeId, out string resolvedName) && !string.IsNullOrWhiteSpace(resolvedName)
                        ? resolvedName
                        : ("Recipe " + recipeId);
                    unlockedRecipes.Add((recipeId, recipeName, sortOrder));
                }

                unlockedRecipes.Sort((a, b) =>
                {
                    int bySort = a.sortOrder.CompareTo(b.sortOrder);
                    if (bySort != 0)
                    {
                        return bySort;
                    }
                    int byName = string.Compare(a.recipeName, b.recipeName, StringComparison.OrdinalIgnoreCase);
                    if (byName != 0)
                    {
                        return byName;
                    }
                    return a.recipeId.CompareTo(b.recipeId);
                });

                for (int i = 0; i < unlockedRecipes.Count; i++)
                {
                    var recipe = unlockedRecipes[i];
                    this.netCookRecipeEntries.Add(new KeyValuePair<int, string>(recipe.recipeId, recipe.recipeName));
                }

                this.netCookRecipeCacheCookerStaticId = this.netCookCookerStaticId;
                if (this.netCookRecipeEntries.Count == 0)
                {
                    this.netCookStatus = "No unlocked recipes available for this cooker.";
                    this.NetCookLog(this.netCookStatus + " staticId=" + this.netCookCookerStaticId + " cookerType=" + this.netCookCookerType);
                }
                else
                {
                    if (NetCookLogsEnabled)
                    {
                        this.NetCookLog("Unlocked recipe mono cache ready count=" + this.netCookRecipeEntries.Count + " first=[" + string.Join(", ", this.netCookRecipeEntries.Take(Math.Min(6, this.netCookRecipeEntries.Count)).Select(kv => kv.Key + ":" + kv.Value).ToArray()) + "]");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                this.NetCookLog("Unlocked recipe mono scan exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryBuildNetCookRecipeCacheFromCookingSystemMono(List<int> unlockedRecipeIds, out bool attempted)
        {
            attempted = false;

            try
            {
                if (this.netCookCookerStaticId <= 0)
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Cooking.CookingSystem", out IntPtr cookingSystemObj) || cookingSystemObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                attempted = true;
                IntPtr cookingSystemClass = auraMonoObjectGetClass(cookingSystemObj);
                if (cookingSystemClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getAllRecipesMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "GetAllRecipes", 1);
                if (getAllRecipesMethod == IntPtr.Zero)
                {
                    this.NetCookLog("Unlocked recipe mono scan missing CookingSystem.GetAllRecipes.");
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                int cookerStaticId = this.netCookCookerStaticId;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&cookerStaticId);
                IntPtr recipeListObj = auraMonoRuntimeInvoke(getAllRecipesMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || recipeListObj == IntPtr.Zero)
                {
                    this.NetCookLog("Unlocked recipe mono scan failed: CookingSystem.GetAllRecipes raised exception.");
                    return false;
                }

                List<IntPtr> recipeItems = new List<IntPtr>(256);
                List<uint> recipePins = new List<uint>();
                if (!this.TryEnumerateAuraMonoCollectionItems(recipeListObj, recipeItems, recipePins) || recipeItems.Count == 0)
                {
                    FreeAuraMonoPins(recipePins);
                    this.NetCookLog("Unlocked recipe mono scan failed: CookingSystem.GetAllRecipes enumeration unavailable.");
                    return false;
                }

                IntPtr ecsImage = this.FindAuraMonoImage(new[]
                {
                    "EcsClient",
                    "EcsClient.dll"
                });
                IntPtr tableDataClass = ecsImage != IntPtr.Zero && auraMonoClassFromName != null
                    ? auraMonoClassFromName(ecsImage, string.Empty, "TableData")
                    : IntPtr.Zero;
                if (tableDataClass == IntPtr.Zero && ecsImage != IntPtr.Zero && auraMonoClassFromName != null)
                {
                    tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
                }

                HashSet<int> unlockedSet = new HashSet<int>(unlockedRecipeIds);
                List<(int recipeId, string recipeName, int sortOrder)> unlockedRecipes = new List<(int, string, int)>(recipeItems.Count);
                try
                {
                for (int i = 0; i < recipeItems.Count; i++)
                {
                    IntPtr recipeObj = recipeItems[i];
                    if (recipeObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    int recipeId = 0;
                    if (!this.TryGetMonoInt32Member(recipeObj, "staticId", out recipeId) && !this.TryGetMonoIntMember(recipeObj, "staticId", out recipeId))
                    {
                        continue;
                    }

                    if (recipeId <= 0 || !unlockedSet.Contains(recipeId))
                    {
                        continue;
                    }

                    string recipeName = string.Empty;
                    if (!this.TryGetMonoStringMember(recipeObj, "name", out recipeName) || string.IsNullOrWhiteSpace(recipeName))
                    {
                        if (!this.TryResolveNetCookRecipeNameFromTableDataMono(tableDataClass, recipeId, out recipeName) || string.IsNullOrWhiteSpace(recipeName))
                        {
                            recipeName = "Recipe " + recipeId;
                        }
                    }
                    else
                    {
                        recipeName = recipeName.Trim();
                    }

                    int sortOrder = recipeId;
                    if (!this.TryGetMonoInt32Member(recipeObj, "sortOrder", out sortOrder) && !this.TryGetMonoIntMember(recipeObj, "sortOrder", out sortOrder))
                    {
                        sortOrder = recipeId;
                    }

                    if (this.netCookCookerType > 0)
                    {
                        this.netCookRecipeCookerTypes[recipeId] = this.netCookCookerType;
                    }

                    unlockedRecipes.Add((recipeId, recipeName, sortOrder));
                }

                unlockedRecipes.Sort((a, b) =>
                {
                    int bySort = a.sortOrder.CompareTo(b.sortOrder);
                    if (bySort != 0)
                    {
                        return bySort;
                    }
                    int byName = string.Compare(a.recipeName, b.recipeName, StringComparison.OrdinalIgnoreCase);
                    if (byName != 0)
                    {
                        return byName;
                    }
                    return a.recipeId.CompareTo(b.recipeId);
                });

                for (int i = 0; i < unlockedRecipes.Count; i++)
                {
                    var recipe = unlockedRecipes[i];
                    this.netCookRecipeEntries.Add(new KeyValuePair<int, string>(recipe.recipeId, recipe.recipeName));
                }
                }
                finally
                {
                    FreeAuraMonoPins(recipePins);
                }

                this.netCookRecipeCacheCookerStaticId = this.netCookCookerStaticId;
                if (this.netCookRecipeEntries.Count == 0)
                {
                    this.NetCookLog("Unlocked recipe mono CookingSystem returned 0 visible recipes.");
                    return false;
                }

                if (NetCookLogsEnabled)
                {
                    this.NetCookLog("Unlocked recipe mono cache ready count=" + this.netCookRecipeEntries.Count + " first=[" + string.Join(", ", this.netCookRecipeEntries.Take(Math.Min(6, this.netCookRecipeEntries.Count)).Select(kv => kv.Key + ":" + kv.Value).ToArray()) + "]");
                }
                return true;
            }
            catch (Exception ex)
            {
                this.NetCookLog("Unlocked recipe mono CookingSystem scan exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryResolveNetCookRecipeNameFromTableDataMono(IntPtr tableDataClass, int recipeId, out string recipeName)
        {
            recipeName = string.Empty;
            if (tableDataClass == IntPtr.Zero || recipeId <= 0 || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr getEntityMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntity", 2);
                if (getEntityMethod == IntPtr.Zero)
                {
                    return false;
                }

                bool needException = false;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&recipeId);
                args[1] = (IntPtr)(&needException);
                IntPtr entityObj = auraMonoRuntimeInvoke(getEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || entityObj == IntPtr.Zero)
                {
                    return false;
                }

                return this.TryResolveNetCookEntityDisplayNameMono(entityObj, tableDataClass, out recipeName) && !string.IsNullOrWhiteSpace(recipeName);
            }
            catch
            {
                recipeName = string.Empty;
                return false;
            }
        }

        private bool TryReadNetCookRecipeTableEntryMono(IntPtr itemObj, HashSet<int> unlockedIds, out int recipeId, out int cookerType, out int sortOrder)
        {
            recipeId = 0;
            cookerType = 0;
            sortOrder = 0;
            if (itemObj == IntPtr.Zero || unlockedIds == null || unlockedIds.Count == 0)
            {
                return false;
            }

            IntPtr valueObj = IntPtr.Zero;
            IntPtr keyObj = IntPtr.Zero;
            bool hasValue = this.TryGetMonoObjectMember(itemObj, "Value", out valueObj)
                || this.TryGetMonoObjectMember(itemObj, "value", out valueObj)
                || this.TryGetMonoObjectMember(itemObj, "_value", out valueObj);
            bool hasKey = this.TryGetMonoObjectMember(itemObj, "Key", out keyObj)
                || this.TryGetMonoObjectMember(itemObj, "key", out keyObj)
                || this.TryGetMonoObjectMember(itemObj, "_key", out keyObj);

            if (!hasValue || valueObj == IntPtr.Zero)
            {
                valueObj = itemObj;
            }

            if (hasKey && keyObj != IntPtr.Zero)
            {
                this.TryUnboxMonoInt32(keyObj, out recipeId);
            }

            if (recipeId <= 0)
            {
                this.TryGetMonoInt32Member(valueObj, "id", out recipeId);
                if (recipeId <= 0)
                {
                    this.TryGetMonoIntMember(valueObj, "id", out recipeId);
                }
            }

            if (recipeId <= 0 || !unlockedIds.Contains(recipeId))
            {
                return false;
            }

            this.TryGetMonoInt32Member(valueObj, "cookerType", out cookerType);
            if (cookerType <= 0)
            {
                this.TryGetMonoIntMember(valueObj, "cookerType", out cookerType);
            }

            this.TryGetMonoInt32Member(valueObj, "sortOrder", out sortOrder);
            if (sortOrder <= 0)
            {
                this.TryGetMonoIntMember(valueObj, "sortOrder", out sortOrder);
            }

            return true;
        }

        private bool TryReadNetCookEntityTableEntryMono(IntPtr itemObj, HashSet<int> unlockedIds, IntPtr tableDataClass, out int entityId, out string name)
        {
            entityId = 0;
            name = string.Empty;
            if (itemObj == IntPtr.Zero || unlockedIds == null || unlockedIds.Count == 0)
            {
                return false;
            }

            IntPtr valueObj = IntPtr.Zero;
            IntPtr keyObj = IntPtr.Zero;
            bool hasValue = this.TryGetMonoObjectMember(itemObj, "Value", out valueObj)
                || this.TryGetMonoObjectMember(itemObj, "value", out valueObj)
                || this.TryGetMonoObjectMember(itemObj, "_value", out valueObj);
            bool hasKey = this.TryGetMonoObjectMember(itemObj, "Key", out keyObj)
                || this.TryGetMonoObjectMember(itemObj, "key", out keyObj)
                || this.TryGetMonoObjectMember(itemObj, "_key", out keyObj);

            if (!hasValue || valueObj == IntPtr.Zero)
            {
                valueObj = itemObj;
            }

            if (hasKey && keyObj != IntPtr.Zero)
            {
                this.TryUnboxMonoInt32(keyObj, out entityId);
            }

            if (entityId <= 0)
            {
                this.TryGetMonoInt32Member(valueObj, "id", out entityId);
                if (entityId <= 0)
                {
                    this.TryGetMonoIntMember(valueObj, "id", out entityId);
                }
            }

            if (entityId <= 0 || !unlockedIds.Contains(entityId))
            {
                return false;
            }

            return this.TryResolveNetCookEntityDisplayNameMono(valueObj, tableDataClass, out name);
        }

        private bool TryResolveNetCookEntityDisplayNameMono(IntPtr entityObj, IntPtr tableDataClass, out string name)
        {
            name = string.Empty;
            if (entityObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoStringMember(entityObj, "name", out string localizedName) && !string.IsNullOrWhiteSpace(localizedName))
            {
                name = localizedName.Trim();
                return true;
            }

            IntPtr rawNameObj = IntPtr.Zero;
            if (this.TryGetMonoObjectMember(entityObj, "_name", out rawNameObj) && rawNameObj != IntPtr.Zero)
            {
                if (this.TryLocalizeNetCookMonoString(tableDataClass, rawNameObj, out string localizedRawName) && !string.IsNullOrWhiteSpace(localizedRawName))
                {
                    name = localizedRawName.Trim();
                    return true;
                }

                if (this.TryReadMonoString(rawNameObj, out string rawName) && !string.IsNullOrWhiteSpace(rawName))
                {
                    name = rawName.Trim();
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryLocalizeNetCookMonoString(IntPtr tableDataClass, IntPtr stringObj, out string localized)
        {
            localized = string.Empty;
            if (tableDataClass == IntPtr.Zero || stringObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr localizeMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "Localize", 1);
            if (localizeMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = stringObj;
            IntPtr localizedObj = auraMonoRuntimeInvoke(localizeMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || localizedObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryReadMonoString(localizedObj, out localized);
        }

        private bool TryGetUnlockedCookingRecipeIds(out List<int> recipeIds, out string status)
        {
            recipeIds = new List<int>(256);
            status = "Cooking client service unavailable.";

            try
            {
                string cacheStatus = "CookingSystem cache not checked before live service.";

                Type ecsServiceType = this.FindLoadedType("XDTDataAndProtocol.ProtocolService.EcsService", "EcsService")
                    ?? this.FindLoadedEcsServiceType();
                Type cookingClientServiceType = this.FindNetCookCookingClientServiceType();
                if (ecsServiceType == null || cookingClientServiceType == null)
                {
                    this.LogNetCookTypeDiagnosticsOnce();
                    status = "Cooking recipe scan types unavailable."
                        + $" (ecs={(ecsServiceType != null ? ecsServiceType.FullName : "null")}, cooking={(cookingClientServiceType != null ? cookingClientServiceType.FullName : "null")}, cache={cacheStatus})";
                    return false;
                }

                MethodInfo tryGetMethod = ecsServiceType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "TryGet" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                if (tryGetMethod == null)
                {
                    status = "EcsService.TryGet unavailable.";
                    return false;
                }

                object[] serviceArgs = new object[] { null, false };
                object serviceResult = tryGetMethod.MakeGenericMethod(cookingClientServiceType).Invoke(null, serviceArgs);
                if (!(serviceResult is bool) || !(bool)serviceResult || serviceArgs[0] == null)
                {
                    status = "ICookingClientService unavailable.";
                    return false;
                }

                object cookingClientService = serviceArgs[0];
                MethodInfo getAllCookingRecipesMethod = cookingClientService.GetType().GetMethod("GetAllCookingRecipes", BindingFlags.Public | BindingFlags.Instance);
                if (getAllCookingRecipesMethod == null)
                {
                    status = "GetAllCookingRecipes unavailable.";
                    return false;
                }

                Type recipeComponentType = this.FindNetCookRecipeComponentType(cookingClientServiceType, getAllCookingRecipesMethod);
                if (recipeComponentType == null)
                {
                    status = "Cooking recipe component unavailable.";
                    return false;
                }

                Type recipeListType = typeof(List<>).MakeGenericType(recipeComponentType);
                object recipeList = Activator.CreateInstance(recipeListType);
                getAllCookingRecipesMethod.Invoke(cookingClientService, new object[] { recipeList });

                List<object> recipeComponents = new List<object>(256);
                if (!this.TryEnumerateManagedCollectionItems(recipeList, recipeComponents))
                {
                    status = "Unlocked recipe list enumeration failed.";
                    return false;
                }

                HashSet<int> uniqueIds = new HashSet<int>();
                for (int i = 0; i < recipeComponents.Count; i++)
                {
                    object recipeComponent = recipeComponents[i];
                    if (recipeComponent == null)
                    {
                        continue;
                    }

                    if (!this.TryGetObjectMember(recipeComponent, "StaticId", out object staticIdObj) || staticIdObj == null)
                    {
                        continue;
                    }

                    int recipeId = Convert.ToInt32(staticIdObj);
                    if (recipeId > 0 && uniqueIds.Add(recipeId))
                    {
                        recipeIds.Add(recipeId);
                    }
                }

                status = "Unlocked recipes ready.";
                return true;
            }
            catch (Exception ex)
            {
                status = "Unlocked recipe scan failed: " + ex.Message;
                recipeIds.Clear();
                return false;
            }
        }

        private bool TryGetUnlockedCookingRecipeIdsFromCookingSystemCache(List<int> recipeIds, out string status)
        {
            status = "CookingSystem cache unavailable.";

            try
            {
                if (!this.EnsureNetCookSystemMethods())
                {
                    if (this.TryGetUnlockedCookingRecipeIdsFromCookingSystemCacheAuraMono(recipeIds, out status))
                    {
                        return true;
                    }

                    if (this.TryGetUnlockedCookingRecipeIdsFromCookingSystemCacheIl2Cpp(recipeIds, out status))
                    {
                        return true;
                    }

                    this.LogNetCookTypeDiagnosticsOnce();
                    status = string.IsNullOrWhiteSpace(status)
                        ? (this.netCookStatus ?? "CookingSystem methods unavailable.")
                        : status;
                    return false;
                }

                if (this.netCookCookingSystemInstanceProperty == null)
                {
                    if (this.TryGetUnlockedCookingRecipeIdsFromCookingSystemCacheAuraMono(recipeIds, out status))
                    {
                        return true;
                    }

                    if (this.TryGetUnlockedCookingRecipeIdsFromCookingSystemCacheIl2Cpp(recipeIds, out status))
                    {
                        return true;
                    }

                    status = string.IsNullOrWhiteSpace(status) ? "CookingSystem instance property unavailable." : status;
                    return false;
                }

                object cookingSystem = this.netCookCookingSystemInstanceProperty.GetValue(null, null);
                if (cookingSystem == null)
                {
                    if (this.TryGetUnlockedCookingRecipeIdsFromCookingSystemCacheAuraMono(recipeIds, out status))
                    {
                        return true;
                    }

                    if (this.TryGetUnlockedCookingRecipeIdsFromCookingSystemCacheIl2Cpp(recipeIds, out status))
                    {
                        return true;
                    }

                    status = string.IsNullOrWhiteSpace(status) ? "CookingSystem instance unavailable." : status;
                    return false;
                }

                this.NetCookLog("CookingSystem cache source type=" + (cookingSystem.GetType().FullName ?? cookingSystem.GetType().Name));

                FieldInfo recipeCacheField = this.FindFieldInHierarchy(cookingSystem.GetType(), "_cookingRecipesCache");
                if (recipeCacheField == null)
                {
                    status = "CookingSystem recipe cache field unavailable.";
                    return false;
                }

                object recipeCacheObj = recipeCacheField.GetValue(cookingSystem);
                if (recipeCacheObj == null)
                {
                    status = "CookingSystem recipe cache is null.";
                    return false;
                }

                List<object> recipeComponents = new List<object>(256);
                if (!this.TryEnumerateManagedCollectionItems(recipeCacheObj, recipeComponents))
                {
                    status = "CookingSystem recipe cache enumeration failed.";
                    return false;
                }

                this.NetCookLog("CookingSystem cache recipes=" + recipeComponents.Count);

                HashSet<int> uniqueIds = new HashSet<int>();
                for (int i = 0; i < recipeComponents.Count; i++)
                {
                    object recipeComponent = recipeComponents[i];
                    if (recipeComponent == null)
                    {
                        continue;
                    }

                    if (!this.TryGetObjectMember(recipeComponent, "StaticId", out object staticIdObj) || staticIdObj == null)
                    {
                        continue;
                    }

                    int recipeId = Convert.ToInt32(staticIdObj);
                    if (recipeId > 0 && uniqueIds.Add(recipeId))
                    {
                        recipeIds.Add(recipeId);
                    }
                }

                status = "CookingSystem cache ready.";
                return recipeIds.Count > 0;
            }
            catch (Exception ex)
            {
                if (this.TryGetUnlockedCookingRecipeIdsFromCookingSystemCacheAuraMono(recipeIds, out status))
                {
                    return true;
                }

                status = "CookingSystem cache scan failed: " + ex.Message;
                recipeIds.Clear();
                return false;
            }
        }

        private bool TryGetUnlockedCookingRecipeIdsFromCookingSystemCacheAuraMono(List<int> recipeIds, out string status)
        {
            status = "CookingSystem AuraMono cache unavailable.";

            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Cooking.CookingSystem", out IntPtr cookingSystemObj) || cookingSystemObj == IntPtr.Zero)
                {
                    status = "AuraMono CookingSystem unavailable.";
                    return false;
                }

                string sourceTypeName = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass != null ? auraMonoObjectGetClass(cookingSystemObj) : IntPtr.Zero);
                this.NetCookLog("CookingSystem cache source type=" + (string.IsNullOrWhiteSpace(sourceTypeName) ? "AuraMono-unknown" : sourceTypeName));

                IntPtr recipeCacheObj = IntPtr.Zero;
                foreach (string memberName in new[] { "_cookingRecipesCache", "cookingRecipesCache", "CookingRecipesCache" })
                {
                    if (this.TryGetMonoObjectMember(cookingSystemObj, memberName, out recipeCacheObj) && recipeCacheObj != IntPtr.Zero)
                    {
                        break;
                    }
                }

                if (recipeCacheObj == IntPtr.Zero)
                {
                    status = "CookingSystem recipe cache field unavailable.";
                    return false;
                }

                List<IntPtr> recipeComponents = new List<IntPtr>(256);
                List<uint> recipePins = new List<uint>();
                if (!this.TryEnumerateAuraMonoCollectionItems(recipeCacheObj, recipeComponents, recipePins))
                {
                    FreeAuraMonoPins(recipePins);
                    status = "CookingSystem recipe cache enumeration failed.";
                    return false;
                }

                this.NetCookLog("CookingSystem cache recipes=" + recipeComponents.Count);

                HashSet<int> uniqueIds = new HashSet<int>();
                try
                {
                for (int i = 0; i < recipeComponents.Count; i++)
                {
                    IntPtr recipeComponentObj = recipeComponents[i];
                    if (recipeComponentObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    int recipeId = 0;
                    bool haveRecipeId = this.TryGetMonoInt32Member(recipeComponentObj, "StaticId", out recipeId)
                        || this.TryGetMonoInt32Member(recipeComponentObj, "staticId", out recipeId)
                        || this.TryGetMonoInt32Member(recipeComponentObj, "_staticId", out recipeId)
                        || this.TryGetMonoIntMember(recipeComponentObj, "StaticId", out recipeId)
                        || this.TryGetMonoIntMember(recipeComponentObj, "staticId", out recipeId)
                        || this.TryGetMonoIntMember(recipeComponentObj, "_staticId", out recipeId);
                    if (!haveRecipeId)
                    {
                        continue;
                    }

                    if (recipeId > 0 && uniqueIds.Add(recipeId))
                    {
                        recipeIds.Add(recipeId);
                    }
                }
                }
                finally
                {
                    FreeAuraMonoPins(recipePins);
                }

                status = "CookingSystem cache ready.";
                return recipeIds.Count > 0;
            }
            catch (Exception ex)
            {
                status = "CookingSystem AuraMono cache scan failed: " + ex.Message;
                recipeIds.Clear();
                return false;
            }
        }

        private bool TryGetUnlockedCookingRecipeIdsFromCookingSystemCacheIl2Cpp(List<int> recipeIds, out string status)
        {
            status = "CookingSystem IL2CPP cache unavailable.";

            try
            {
                Il2CppType cookingSystemType = this.TryGetNetCookIl2CppType(
                    "XDTGameSystem.GameplaySystem.Cooking.CookingSystem",
                    "CookingSystem");
                if (cookingSystemType == null)
                {
                    status = "CookingSystem unavailable.";
                    return false;
                }

                Il2CppPropertyInfo instanceProperty = cookingSystemType.GetProperty("Instance");
                if (instanceProperty == null)
                {
                    status = "CookingSystem instance property unavailable.";
                    return false;
                }

                Il2CppObject cookingSystem = instanceProperty.GetValue(null) as Il2CppObject;
                if (cookingSystem == null)
                {
                    status = "CookingSystem instance unavailable.";
                    return false;
                }

                Il2CppType cookingSystemObjectType = cookingSystem.GetIl2CppType();
                this.NetCookLog("CookingSystem cache source type=" + ((cookingSystemObjectType?.FullName?.ToString() ?? cookingSystemObjectType?.Name?.ToString()) ?? "unknown"));

                Il2CppFieldInfo recipeCacheField = cookingSystemObjectType.GetField("_cookingRecipesCache", (Il2CppBindingFlags)62);
                if (recipeCacheField == null)
                {
                    status = "CookingSystem recipe cache field unavailable.";
                    return false;
                }

                Il2CppObject recipeCacheObj = recipeCacheField.GetValue(cookingSystem);
                if (recipeCacheObj == null)
                {
                    status = "CookingSystem recipe cache is null.";
                    return false;
                }

                Il2CppType recipeCacheType = recipeCacheObj.GetIl2CppType();
                Il2CppPropertyInfo countProperty = recipeCacheType.GetProperty("Count");
                Il2CppMethodInfo getItemMethod = recipeCacheType.GetMethod("get_Item", new Il2CppReferenceArray<Il2CppType>(new Il2CppType[]
                {
                    Il2CppType.GetType("System.Int32")
                }));
                if (countProperty == null || getItemMethod == null)
                {
                    status = "CookingSystem recipe cache enumeration failed.";
                    return false;
                }

                int count = countProperty.GetValue(recipeCacheObj).Unbox<int>();
                this.NetCookLog("CookingSystem cache recipes=" + count);

                HashSet<int> uniqueIds = new HashSet<int>();
                for (int i = 0; i < count; i++)
                {
                    Il2CppObject recipeComponent = getItemMethod.Invoke(recipeCacheObj, new Il2CppReferenceArray<Il2CppObject>(new Il2CppObject[]
                    {
                        this.BoxInt(i)
                    }));
                    if (recipeComponent == null)
                    {
                        continue;
                    }

                    Il2CppType recipeComponentType = recipeComponent.GetIl2CppType();
                    if (recipeComponentType == null)
                    {
                        continue;
                    }

                    int recipeId = 0;
                    bool haveRecipeId = false;

                    Il2CppFieldInfo staticIdField = recipeComponentType.GetField("StaticId", (Il2CppBindingFlags)62);
                    if (staticIdField != null)
                    {
                        recipeId = staticIdField.GetValue(recipeComponent).Unbox<int>();
                        haveRecipeId = true;
                    }
                    else
                    {
                        Il2CppPropertyInfo staticIdProperty = recipeComponentType.GetProperty("StaticId");
                        if (staticIdProperty != null)
                        {
                            recipeId = staticIdProperty.GetValue(recipeComponent).Unbox<int>();
                            haveRecipeId = true;
                        }
                    }

                    if (haveRecipeId && recipeId > 0 && uniqueIds.Add(recipeId))
                    {
                        recipeIds.Add(recipeId);
                    }
                }

                status = "CookingSystem cache ready.";
                return recipeIds.Count > 0;
            }
            catch (Exception ex)
            {
                status = "CookingSystem cache scan failed: " + ex.Message;
                recipeIds.Clear();
                return false;
            }
        }

        private Il2CppType TryGetNetCookIl2CppType(params string[] typeNames)
        {
            if (typeNames == null)
            {
                return null;
            }

            string[] assemblies = new string[]
            {
                "XDTGameSystem",
                "XDTGameSystem.dll",
                "XDTDataAndProtocol",
                "XDTDataAndProtocol.dll",
                "EcsSystem",
                "EcsSystem.dll",
                "EcsClient",
                "EcsClient.dll",
                "XDTLevelAndEntity",
                "XDTLevelAndEntity.dll",
                "Client",
                "Client.dll",
                "Assembly-CSharp",
                "Assembly-CSharp.dll"
            };

            foreach (string typeName in typeNames)
            {
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    continue;
                }

                try
                {
                    Il2CppType direct = Il2CppType.GetType(typeName);
                    if (direct != null)
                    {
                        return direct;
                    }
                }
                catch
                {
                }

                foreach (string assemblyName in assemblies)
                {
                    try
                    {
                        Il2CppType qualified = Il2CppType.GetType(typeName + ", " + assemblyName);
                        if (qualified != null)
                        {
                            return qualified;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private void LogNetCookTypeDiagnosticsOnce()
        {
            if (this.netCookTypeDiagnosticsLogged)
            {
                return;
            }

            this.netCookTypeDiagnosticsLogged = true;

            try
            {
                (string label, string[] names)[] probes = new (string, string[])[]
                {
                    ("CookingSystem", new[] { "XDTGameSystem.GameplaySystem.Cooking.CookingSystem", "CookingSystem" }),
                    ("EcsService", new[] { "XDTDataAndProtocol.ProtocolService.EcsService", "EcsService" }),
                    ("ICookingClientService", new[] { "XDTDataAndProtocol.ProtocolService.Cooking.ICookingClientService", "ICookingClientService" }),
                    ("CookingClientService", new[] { "EcsSystem.ClientSystem.Cooking.CookingClientService", "CookingClientService" }),
                    ("CookingRecipeComponent", new[] { "XDT.Scene.Shared.Modules.Cooking.CookingRecipeComponent", "CookingRecipeComponent" }),
                    ("BackPackSystem", new[] { "XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", "BackPackSystem" }),
                    ("PlayerDataCenter", new[] { "XDTDataAndProtocol.PlayerDataCenter", "PlayerDataCenter" }),
                    ("TableData", new[] { "TableData", "EcsClient.TableData" })
                };

                List<string> probeResults = new List<string>(probes.Length);
                for (int i = 0; i < probes.Length; i++)
                {
                    Type managedType = this.FindLoadedType(probes[i].names);
                    Il2CppType il2CppType = this.TryGetNetCookIl2CppType(probes[i].names);
                    IntPtr auraMonoClass = IntPtr.Zero;
                    for (int nameIndex = 0; nameIndex < probes[i].names.Length; nameIndex++)
                    {
                        auraMonoClass = this.FindAuraMonoClassByFullName(probes[i].names[nameIndex]);
                        if (auraMonoClass != IntPtr.Zero)
                        {
                            break;
                        }
                    }

                    string managedName = managedType != null ? (managedType.FullName ?? managedType.Name) : "null";
                    string il2CppName = il2CppType != null ? ((il2CppType.FullName?.ToString() ?? il2CppType.Name?.ToString()) ?? "unknown") : "null";
                    string auraMonoName = auraMonoClass != IntPtr.Zero ? this.GetAuraMonoClassDisplayName(auraMonoClass) : "null";
                    probeResults.Add(probes[i].label + "(managed=" + managedName + ", il2cpp=" + il2CppName + ", auraMono=" + auraMonoName + ")");
                }

                if (NetCookLogsEnabled)
                {
                    this.NetCookLog("Type diagnostics: " + string.Join(" | ", probeResults.ToArray()));
                }
            }
            catch (Exception ex)
            {
                this.NetCookLog("Type diagnostics failed: " + ex.Message);
            }
        }

        private Type FindNetCookCookingClientServiceType()
        {
            Type directType = this.FindLoadedType(
                "XDTDataAndProtocol.ProtocolService.Cooking.ICookingClientService",
                "EcsSystem.ClientSystem.Cooking.ICookingClientService",
                "ICookingClientService",
                "CookingClientService");
            if (directType != null)
            {
                return directType;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    if (this.IsNetCookCookingClientServiceContract(type))
                    {
                        return type;
                    }

                    if (!type.IsClass)
                    {
                        continue;
                    }

                    Type interfaceType = type.GetInterfaces().FirstOrDefault(this.IsNetCookCookingClientServiceContract);
                    if (interfaceType != null)
                    {
                        return interfaceType;
                    }
                }
            }

            return null;
        }

        private bool IsNetCookCookingClientServiceContract(Type type)
        {
            if (type == null)
            {
                return false;
            }

            MethodInfo getAllMethod = type.GetMethod("GetAllCookingRecipes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo getRecentMethod = type.GetMethod("GetRecentRecipes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (getAllMethod == null || getRecentMethod == null)
            {
                return false;
            }

            ParameterInfo[] allParameters = getAllMethod.GetParameters();
            ParameterInfo[] recentParameters = getRecentMethod.GetParameters();
            if (allParameters.Length != 1 || recentParameters.Length != 1)
            {
                return false;
            }

            return this.TryGetNetCookListElementType(allParameters[0].ParameterType, out _)
                && this.TryGetNetCookListElementType(recentParameters[0].ParameterType, out Type recentElementType)
                && recentElementType == typeof(int);
        }

        private Type FindNetCookRecipeComponentType(Type cookingClientServiceType, MethodInfo getAllCookingRecipesMethod)
        {
            Type directType = this.FindLoadedType(
                "XDT.Scene.Shared.Modules.Cooking.CookingRecipeComponent",
                "CookingRecipeComponent");
            if (directType != null)
            {
                return directType;
            }

            if (getAllCookingRecipesMethod != null)
            {
                ParameterInfo[] parameters = getAllCookingRecipesMethod.GetParameters();
                if (parameters.Length == 1 && this.TryGetNetCookListElementType(parameters[0].ParameterType, out Type parameterElementType))
                {
                    return parameterElementType;
                }
            }

            if (cookingClientServiceType != null)
            {
                MethodInfo contractMethod = cookingClientServiceType.GetMethod("GetAllCookingRecipes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (contractMethod != null)
                {
                    ParameterInfo[] parameters = contractMethod.GetParameters();
                    if (parameters.Length == 1 && this.TryGetNetCookListElementType(parameters[0].ParameterType, out Type contractElementType))
                    {
                        return contractElementType;
                    }
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    string fullName = type.FullName ?? type.Name ?? string.Empty;
                    if (fullName.IndexOf("CookingRecipeComponent", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    FieldInfo staticIdField = type.GetField("StaticId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (staticIdField != null && staticIdField.FieldType == typeof(int))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private bool TryGetNetCookListElementType(Type listType, out Type elementType)
        {
            elementType = null;
            if (listType == null)
            {
                return false;
            }

            if (listType.IsGenericType && listType.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type[] arguments = listType.GetGenericArguments();
                if (arguments.Length == 1)
                {
                    elementType = arguments[0];
                    return elementType != null;
                }
            }

            return false;
        }

        private void ResetNetCookRecipeCacheRetry()
        {
            this.netCookRecipeCacheFailureCookerStaticId = 0;
            this.nextNetCookRecipeCacheRetryAt = 0f;
        }

        private void MarkNetCookRecipeCacheRetry()
        {
            this.netCookRecipeCacheFailureCookerStaticId = this.netCookCookerStaticId;
            this.nextNetCookRecipeCacheRetryAt = Time.time + 0.75f;
        }

        private List<KeyValuePair<int, string>> GetVisibleNetCookRecipeEntries()
        {
            this.netCookVisibleRecipeEntries.Clear();
            if (!this.EnsureNetCookRecipeCache())
            {
                return this.netCookVisibleRecipeEntries;
            }

            string search = (this.netCookRecipeSearchText ?? string.Empty).Trim();
            bool filterBySearch = !string.IsNullOrWhiteSpace(search);

            for (int i = 0; i < this.netCookRecipeEntries.Count; i++)
            {
                KeyValuePair<int, string> recipeEntry = this.netCookRecipeEntries[i];
                if (this.netCookCookerType > 0)
                {
                    if (!this.netCookRecipeCookerTypes.TryGetValue(recipeEntry.Key, out int recipeCookerType) || recipeCookerType != this.netCookCookerType)
                    {
                        continue;
                    }
                }

                string recipeName = recipeEntry.Value ?? string.Empty;
                if (filterBySearch && recipeName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                this.netCookVisibleRecipeEntries.Add(recipeEntry);
            }

            this.netCookVisibleRecipeEntries.Sort((a, b) =>
            {
                string nameA = a.Value ?? string.Empty;
                string nameB = b.Value ?? string.Empty;
                int byName = string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
                if (byName != 0)
                {
                    return byName;
                }

                return a.Key.CompareTo(b.Key);
            });

            return this.netCookVisibleRecipeEntries;
        }

        private void TrySelectDefaultNetCookRecipeForCooker()
        {
            List<KeyValuePair<int, string>> visibleRecipes = this.GetVisibleNetCookRecipeEntries();
            if (visibleRecipes.Count <= 0)
            {
                return;
            }

            for (int i = 0; i < visibleRecipes.Count; i++)
            {
                if (visibleRecipes[i].Key == this.netCookRecipeId)
                {
                    return;
                }
            }

            this.netCookRecipeId = visibleRecipes[0].Key;
        }

        private bool EnsureNetCookAssistTargets(out string status)
        {
            status = "Assist targets ready.";
            if (this.netCookTargets.Count <= 0)
            {
                if (!this.TryResolveNetCookContextsFromCurrentTarget(this.netCookTargets, out status) || this.netCookTargets.Count <= 0)
                {
                    if (this.netCookCookerNetId != 0U && this.netCookLevelObjectNetId != 0UL)
                    {
                        this.netCookTargets.Add(new NetCookTargetContext
                        {
                            CookerNetId = this.netCookCookerNetId,
                            CookerStaticId = this.netCookCookerStaticId,
                            CookerType = this.netCookCookerType,
                            LevelObjectNetId = this.netCookLevelObjectNetId
                        });
                    }
                }
            }

            for (int i = this.netCookTargets.Count - 1; i >= 0; i--)
            {
                NetCookTargetContext target = this.netCookTargets[i];
                if (target == null || target.CookerNetId == 0U || target.LevelObjectNetId == 0UL)
                {
                    this.netCookTargets.RemoveAt(i);
                }
            }

            if (this.netCookTargets.Count <= 0)
            {
                status = "No nearby cooker targets found.";
                return false;
            }

            this.ApplyNetCookTargetContext(this.netCookTargets[0]);
            status = "Using " + this.netCookTargets.Count + " nearby stove(s) for mini-game assist.";
            return true;
        }

        private bool EnsureNetCookTargetsForCurrentRecipe(out string status)
        {
            status = "Cooker targets ready.";
            if (this.netCookTargets.Count <= 0)
            {
                if (!this.TryResolveNetCookContextsFromCurrentTarget(this.netCookTargets, out status) || this.netCookTargets.Count <= 0)
                {
                    if (this.netCookCookerNetId != 0U && this.netCookLevelObjectNetId != 0UL)
                    {
                        this.netCookTargets.Add(new NetCookTargetContext
                        {
                            CookerNetId = this.netCookCookerNetId,
                            CookerStaticId = this.netCookCookerStaticId,
                            CookerType = this.netCookCookerType,
                            LevelObjectNetId = this.netCookLevelObjectNetId
                        });
                    }
                }
            }

            if (this.netCookTargets.Count <= 0)
            {
                status = "No nearby cooker targets found.";
                return false;
            }

            int recipeCookerType = 0;
            this.netCookRecipeCookerTypes.TryGetValue(this.netCookRecipeId, out recipeCookerType);

            for (int i = this.netCookTargets.Count - 1; i >= 0; i--)
            {
                NetCookTargetContext target = this.netCookTargets[i];
                if (target.CookerNetId == 0U || target.LevelObjectNetId == 0UL)
                {
                    this.netCookTargets.RemoveAt(i);
                    continue;
                }

                if (recipeCookerType > 0 && target.CookerType > 0 && recipeCookerType != target.CookerType)
                {
                    this.netCookTargets.RemoveAt(i);
                }
            }

            if (this.netCookTargets.Count <= 0)
            {
                status = "No nearby stoves support the selected recipe.";
                return false;
            }

            this.ApplyNetCookTargetContext(this.netCookTargets[0]);
            status = "Using " + this.netCookTargets.Count + " nearby stove(s).";
            return true;
        }

        private bool TryResolveNetCookContextsFromCurrentTarget(List<NetCookTargetContext> targets, out string status, bool deferOwnerWindowExpansion = false)
        {
            status = "No cooker target found.";
            if (targets == null)
            {
                status = "Target buffer unavailable.";
                return false;
            }

            targets.Clear();
            if (this.TryResolveNetCookContextsFromRegisteredCache(targets, out status))
            {
                return true;
            }

            if (this.TryResolveNetCookContextsFromCookBuildComponents(targets, out status))
            {
                return true;
            }
            this.NetCookLog("Cook-build registry lookup: " + status);

            List<ulong> candidateLevelObjects = new List<ulong>(32);
            HashSet<ulong> candidateLevelObjectSet = new HashSet<ulong>();

            if (this.TryGetCurrentFocusedLevelObjectNetId(out ulong focusedLevelObjectNetId, out string focusStatus) && focusedLevelObjectNetId != 0UL)
            {
                AddNetCookCandidateLevelObject(candidateLevelObjects, candidateLevelObjectSet, focusedLevelObjectNetId);
            }
            else
            {
                this.NetCookLog("Focused target unavailable: " + focusStatus);
            }

            if (this.TryGetCurrentInteractTargetLevelObjects(candidateLevelObjects, out string interactStatus, candidateLevelObjectSet))
            {
                status = interactStatus;
            }
            else
            {
                this.NetCookLog("Interact target lookup: " + interactStatus);
            }

            if (this.TryGetCurrentInteractTargetLevelObjectsViaAuraMono(candidateLevelObjects, out string auraMonoInteractStatus, candidateLevelObjectSet))
            {
                status = auraMonoInteractStatus;
            }
            else
            {
                this.NetCookLog("AuraMono interact lookup: " + auraMonoInteractStatus);
            }

            if (this.TryGetNearbyCookerLevelObjectsViaWorldScan(candidateLevelObjects, out string scanStatus, candidateLevelObjectSet))
            {
                status = scanStatus;
            }
            else
            {
                this.NetCookLog("Nearby cooker scan: " + scanStatus);
            }

            if (deferOwnerWindowExpansion)
            {
                int lowIdCandidateCount = 0;
                for (int i = 0; i < candidateLevelObjects.Count; i++)
                {
                    ulong candidateNetId = candidateLevelObjects[i];
                    if (candidateNetId > 0UL && candidateNetId <= uint.MaxValue)
                    {
                        lowIdCandidateCount++;
                    }
                }
                this.netCookLastDeferredWorldScanCandidateCount = lowIdCandidateCount;
            }

            List<ulong> resolveCandidates = this.GetNetCookResolveCandidates(candidateLevelObjects);
            HashSet<string> seenTargets = new HashSet<string>();
            HashSet<uint> seenCookerNetIds = new HashSet<uint>();
            for (int i = 0; i < resolveCandidates.Count; i++)
            {
                if (targets.Count >= NetCookMaxCaptureTargets)
                {
                    break;
                }

                ulong candidateLevelObjectNetId = resolveCandidates[i];
                if (!this.TryResolveNetCookContextFromLevelObject(candidateLevelObjectNetId, out uint cookerNetId, out int cookerStaticId, out int cookerType, out string resolveStatus))
                {
                    if (!this.TryResolveNetCookContextFromLevelObjectAuraMono(candidateLevelObjectNetId, out cookerNetId, out cookerStaticId, out cookerType, out resolveStatus))
                    {
                        continue;
                    }
                }

                if (cookerNetId == 0U || cookerStaticId <= 0)
                {
                    continue;
                }

                if (!seenCookerNetIds.Add(cookerNetId))
                {
                    this.NetCookLog("Skipped duplicate stove burner " + cookerNetId + " from level object " + candidateLevelObjectNetId + ".");
                    continue;
                }

                bool hasWorldPosition = this.TryGetNetCookTargetWorldPosition(candidateLevelObjectNetId, cookerNetId, out Vector3 worldPosition);
                string key = cookerNetId + ":" + candidateLevelObjectNetId;
                if (!seenTargets.Add(key))
                {
                    continue;
                }

                targets.Add(new NetCookTargetContext
                {
                    CookerNetId = cookerNetId,
                    CookerStaticId = cookerStaticId,
                    CookerType = cookerType,
                    LevelObjectNetId = candidateLevelObjectNetId,
                    HasWorldPosition = hasWorldPosition,
                    WorldPosition = worldPosition
                });
            }

            int desiredCookerStaticId = this.GetPreferredNetCookTargetStaticId(targets);
            int desiredCookerType = this.GetPreferredNetCookTargetCookerType(targets, desiredCookerStaticId);

            int candidateOwnerWindowAdded = 0;
            if (targets.Count <= 0 && candidateLevelObjects.Count > 0 && this.TryGetNetCookScanOrigin(out Vector3 candidateOwnerScanOrigin, out _))
            {
                HashSet<uint> candidateOwnerSeedNetIds = new HashSet<uint>();
                for (int i = 0; i < candidateLevelObjects.Count; i++)
                {
                    ulong candidateNetId = candidateLevelObjects[i];
                    if (candidateNetId <= uint.MaxValue)
                    {
                        continue;
                    }

                    uint ownerNetId = ExtractNetCookOwnerNetId(candidateNetId);
                    if (ownerNetId != 0U)
                    {
                        candidateOwnerSeedNetIds.Add(ownerNetId);
                    }
                }

                if (candidateOwnerSeedNetIds.Count > 0)
                {
                    int skippedDifferentCooker = 0;
                    int skippedDuplicateCooker = 0;
                    candidateOwnerWindowAdded = this.TryAddNearbyCookBuildTargetsByOwnerNetIdWindow(
                        targets,
                        seenTargets,
                        seenCookerNetIds,
                        candidateOwnerSeedNetIds,
                        candidateOwnerScanOrigin,
                        desiredCookerStaticId,
                        desiredCookerType,
                        ref skippedDifferentCooker,
                        ref skippedDuplicateCooker,
                        null,
                        NetCookCandidateOwnerNetIdProbeWindow);
                    if (candidateOwnerWindowAdded > 0)
                    {
                        this.NetCookLog("Added " + candidateOwnerWindowAdded + " cooker target(s) from candidate owner-window fallback seeds=" + candidateOwnerSeedNetIds.Count + ".");
                    }
                    else
                    {
                        this.NetCookLog("Candidate owner-window fallback found no cooker targets seeds=" + candidateOwnerSeedNetIds.Count + " skippedDifferentCooker=" + skippedDifferentCooker + " skippedDuplicateCooker=" + skippedDuplicateCooker + ".");
                    }
                }
                else
                {
                    this.NetCookLog("Candidate owner-window fallback had no packed owner seeds from candidates=" + candidateLevelObjects.Count + ".");
                }
            }

            int ownerWindowAdded = 0;
            if (!deferOwnerWindowExpansion && targets.Count > 0 && targets.Count < NetCookMaxCaptureTargets && this.TryGetNetCookScanOrigin(out Vector3 scanOrigin, out _))
            {
                HashSet<uint> ownerSeedNetIds = new HashSet<uint>();
                for (int i = 0; i < targets.Count; i++)
                {
                    uint ownerNetId = ExtractNetCookOwnerNetId(targets[i].LevelObjectNetId);
                    if (ownerNetId != 0U)
                    {
                        ownerSeedNetIds.Add(ownerNetId);
                    }
                }

                int skippedDifferentCooker = 0;
                int skippedDuplicateCooker = 0;
                if (targets.Count < NetCookMaxCaptureTargets)
                {
                    ownerWindowAdded += this.TryAddNearbyCookBuildTargetsByOwnerNetIdWindow(
                        targets,
                        seenTargets,
                        seenCookerNetIds,
                        ownerSeedNetIds,
                        scanOrigin,
                        desiredCookerStaticId,
                        desiredCookerType,
                        ref skippedDifferentCooker,
                        ref skippedDuplicateCooker,
                        null,
                        NetCookFastOwnerNetIdProbeWindow);
                }
                if (ownerWindowAdded > 0)
                {
                    this.NetCookLog("Added " + ownerWindowAdded + " nearby cooker target(s) from owner-window expansion.");
                }
            }
            else if (deferOwnerWindowExpansion && targets.Count > 0 && targets.Count < NetCookMaxCaptureTargets)
            {
                this.NetCookLog("Deferred owner-window expansion; using " + targets.Count + " seed cooker target(s) for initial capture.");
            }

            int registeredWorldAdded = this.TryAddRegisteredWorldCookerTargets(targets, seenTargets, seenCookerNetIds, desiredCookerStaticId, desiredCookerType);
            if (registeredWorldAdded > 0)
            {
                this.NetCookLog("Added " + registeredWorldAdded + " registered world cooker target(s).");
            }

            int registeredAdded = this.TryAddRegisteredNetCookTargets(targets, seenTargets, seenCookerNetIds, desiredCookerStaticId, desiredCookerType);
            if (registeredAdded > 0)
            {
                this.NetCookLog("Added " + registeredAdded + " registered cooker target(s) from session cache.");
            }

            string entityScanStatus = null;
            bool needsEntityScan = NetCookUnsafeBroadAuraMonoExpansionEnabled
                && (!deferOwnerWindowExpansion || targets.Count <= 0)
                && targets.Count <= 1
                && ownerWindowAdded == 0
                && candidateOwnerWindowAdded == 0
                && registeredWorldAdded == 0
                && registeredAdded == 0;
            if (needsEntityScan && this.TryAddNearbyCookBuildTargetsViaAuraMonoEntityScan(targets, seenTargets, seenCookerNetIds, candidateLevelObjects, desiredCookerStaticId, desiredCookerType, out entityScanStatus))
            {
                status = entityScanStatus;
            }
            else if (needsEntityScan)
            {
                this.NetCookLog("Cook build entity scan: " + entityScanStatus);
            }
            else
            {
                this.NetCookLog("Skipped broad cook-build entity scan; using " + targets.Count + " resolved/registered target(s).");
            }

            desiredCookerStaticId = this.GetPreferredNetCookTargetStaticId(targets);
            desiredCookerType = this.GetPreferredNetCookTargetCookerType(targets, desiredCookerStaticId);
            if (desiredCookerType <= 0 && desiredCookerStaticId > 0)
            {
                this.TryGetCookerTypeForStaticId(desiredCookerStaticId, out desiredCookerType);
            }

            int removedDifferentCooker = this.RemoveIncompatibleNetCookTargets(targets, seenTargets, seenCookerNetIds, desiredCookerStaticId, desiredCookerType);
            if (removedDifferentCooker > 0)
            {
                this.NetCookLog("Filtered " + removedDifferentCooker + " incompatible cooker target(s); using cookerStaticId=" + desiredCookerStaticId + " cookerType=" + desiredCookerType + ".");
            }

            int removedOutOfRange = this.RemoveOutOfRangeNetCookTargets(targets, seenTargets, seenCookerNetIds);
            if (removedOutOfRange > 0)
            {
                this.NetCookLog("Filtered " + removedOutOfRange + " cooker target(s) outside scan radius=" + Mathf.Clamp(this.netCookScanRadiusMeters, NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters).ToString("F0") + "m.");
            }

            this.RegisterNetCookTargets(targets);
            this.SortNetCookTargetsByDistanceFromScanOrigin(targets);

            if (targets.Count <= 0)
            {
                status = "Nearby scan found no valid cooker targets.";
                return false;
            }

            status = "Captured " + targets.Count + " nearby stove(s) within " + Mathf.Clamp(this.netCookScanRadiusMeters, NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters).ToString("F0") + "m.";
            this.NetCookLog(status);
            this.LogNetCookTargetSummary(targets);
            return true;
        }

        private void RefreshNetCookDeferredWorldScanCandidateCount()
        {
            try
            {
                List<ulong> candidateLevelObjects = new List<ulong>(128);
                HashSet<ulong> candidateLevelObjectSet = new HashSet<ulong>();
                if (!this.TryGetNearbyCookerLevelObjectsViaWorldScan(candidateLevelObjects, out string scanStatus, candidateLevelObjectSet))
                {
                    this.NetCookLog("Deferred refresh world-scan metadata unavailable: " + scanStatus);
                    return;
                }

                int lowIdCandidateCount = 0;
                for (int i = 0; i < candidateLevelObjects.Count; i++)
                {
                    ulong candidateNetId = candidateLevelObjects[i];
                    if (candidateNetId > 0UL && candidateNetId <= uint.MaxValue)
                    {
                        lowIdCandidateCount++;
                    }
                }

                this.netCookLastDeferredWorldScanCandidateCount = lowIdCandidateCount;
                this.NetCookLog("Deferred refresh world-scan metadata lowIdCandidates=" + lowIdCandidateCount + " previousBroadLowIdCandidates=" + this.netCookLastBroadRefreshWorldScanCandidateCount + ".");
            }
            catch (Exception ex)
            {
                this.NetCookLog("Deferred refresh world-scan metadata exception: " + ex.Message);
            }
        }

        private List<ulong> GetNetCookResolveCandidates(List<ulong> candidateLevelObjects)
        {
            if (candidateLevelObjects == null || candidateLevelObjects.Count <= 0)
            {
                return candidateLevelObjects ?? new List<ulong>(0);
            }

            List<ulong> resolveCandidates = new List<ulong>(Math.Min(candidateLevelObjects.Count, NetCookMaxCaptureTargets));
            int composedCount = 0;
            int ownerlessComposedCount = 0;
            int lowIdCount = 0;

            for (int i = 0; i < candidateLevelObjects.Count; i++)
            {
                ulong levelObjectNetId = candidateLevelObjects[i];
                if (levelObjectNetId > uint.MaxValue)
                {
                    if (ExtractNetCookOwnerNetId(levelObjectNetId) == 0U)
                    {
                        ownerlessComposedCount++;
                        continue;
                    }

                    resolveCandidates.Add(levelObjectNetId);
                    composedCount++;
                    if (resolveCandidates.Count >= NetCookMaxCaptureTargets)
                    {
                        break;
                    }
                }
            }

            for (int i = 0; i < candidateLevelObjects.Count && resolveCandidates.Count < NetCookMaxCaptureTargets; i++)
            {
                ulong levelObjectNetId = candidateLevelObjects[i];
                if (levelObjectNetId == 0UL || levelObjectNetId > uint.MaxValue)
                {
                    continue;
                }

                resolveCandidates.Add(levelObjectNetId);
                lowIdCount++;
            }

            if (composedCount <= 0 && lowIdCount > 0)
            {
                this.NetCookLog("Skipping " + lowIdCount + " low-id level object resolve candidate(s); using cook-build scan fallback.");
                return new List<ulong>(0);
            }

            if (resolveCandidates.Count > 0)
            {
                if (resolveCandidates.Count < candidateLevelObjects.Count)
                {
                    this.NetCookLog("Prioritizing " + composedCount + " composed cooker candidate(s) plus " + lowIdCount + " nearby world-scan candidate(s); skippedOwnerlessComposed=" + ownerlessComposedCount + " capped " + (candidateLevelObjects.Count - resolveCandidates.Count) + " candidate(s).");
                }
                else if (composedCount > 0)
                {
                    this.NetCookLog("Resolving " + composedCount + " composed cooker candidate(s) plus " + lowIdCount + " nearby world-scan candidate(s); skippedOwnerlessComposed=" + ownerlessComposedCount + ".");
                }

                return resolveCandidates;
            }

            List<ulong> cappedCandidates = new List<ulong>(NetCookMaxCaptureTargets);
            for (int i = 0; i < candidateLevelObjects.Count && cappedCandidates.Count < NetCookMaxCaptureTargets; i++)
            {
                cappedCandidates.Add(candidateLevelObjects[i]);
            }
            this.NetCookLog("Capped cooker candidate resolves at " + cappedCandidates.Count + " of " + candidateLevelObjects.Count + ".");
            return cappedCandidates;
        }

        private int TryAddRegisteredWorldCookerTargets(List<NetCookTargetContext> targets, HashSet<string> seenTargets, HashSet<uint> seenCookerNetIds, int desiredCookerStaticId, int desiredCookerType)
        {
            if (targets == null || seenTargets == null || seenCookerNetIds == null || this.netCookRegisteredWorldCookers.Count <= 0)
            {
                return 0;
            }

            int added = 0;
            int skippedDifferentCooker = 0;
            int skippedDuplicateCooker = 0;
            foreach (NetCookRegisteredWorldCooker registeredCooker in this.netCookRegisteredWorldCookers.Values)
            {
                if (targets.Count >= NetCookMaxCaptureTargets)
                {
                    break;
                }

                if (registeredCooker == null
                    || registeredCooker.OwnerNetId == 0U
                    || registeredCooker.StaticId <= 0
                    || !this.IsCompatibleNetCookCooker(registeredCooker.StaticId, registeredCooker.CookerType, desiredCookerStaticId, desiredCookerType))
                {
                    continue;
                }

                added += this.TryAddSynthesizedNetCookBurnerTargets(
                    registeredCooker.OwnerNetId,
                    Vector3.zero,
                    registeredCooker.StaticId,
                    registeredCooker.CookerType,
                    desiredCookerStaticId,
                    desiredCookerType,
                    targets,
                    seenTargets,
                    seenCookerNetIds,
                    ref skippedDifferentCooker,
                    ref skippedDuplicateCooker);
            }

            return added;
        }

        private static bool AddNetCookCandidateLevelObject(List<ulong> candidateLevelObjects, HashSet<ulong> candidateLevelObjectSet, ulong levelObjectNetId)
        {
            if (candidateLevelObjects == null || levelObjectNetId == 0UL)
            {
                return false;
            }

            if (candidateLevelObjectSet != null)
            {
                if (!candidateLevelObjectSet.Add(levelObjectNetId))
                {
                    return false;
                }
            }
            else if (candidateLevelObjects.Contains(levelObjectNetId))
            {
                return false;
            }

            candidateLevelObjects.Add(levelObjectNetId);
            return true;
        }

        private static void AddNetCookScanDebugSample(List<string> samples, string message)
        {
            if (samples == null || samples.Count >= NetCookScanDebugSampleLimit || string.IsNullOrEmpty(message))
            {
                return;
            }

            samples.Add(message);
        }

        private void RegisterNetCookTargets(List<NetCookTargetContext> targets)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                NetCookTargetContext target = targets[i];
                if (target == null || target.CookerNetId == 0U || target.LevelObjectNetId == 0UL || target.CookerStaticId <= 0)
                {
                    continue;
                }

                NetCookTargetContext copy = this.CloneNetCookTargetContext(target);
                this.TryRefreshNetCookTargetWorldPosition(copy, false);
                this.netCookRegisteredTargets[target.CookerNetId + ":" + target.LevelObjectNetId] = copy;
            }
        }

        private int TryAddRegisteredNetCookTargets(List<NetCookTargetContext> targets, HashSet<string> seenTargets, HashSet<uint> seenCookerNetIds, int desiredCookerStaticId, int desiredCookerType)
        {
            if (targets == null || seenTargets == null || seenCookerNetIds == null || this.netCookRegisteredTargets.Count <= 0)
            {
                return 0;
            }

            int added = 0;
            foreach (NetCookTargetContext registeredTarget in this.netCookRegisteredTargets.Values)
            {
                if (targets.Count >= NetCookMaxCaptureTargets)
                {
                    break;
                }

                if (registeredTarget == null
                    || registeredTarget.CookerNetId == 0U
                    || registeredTarget.LevelObjectNetId == 0UL
                    || registeredTarget.CookerStaticId <= 0
                    || !this.IsCompatibleNetCookCooker(registeredTarget.CookerStaticId, registeredTarget.CookerType, desiredCookerStaticId, desiredCookerType))
                {
                    continue;
                }

                string key = registeredTarget.CookerNetId + ":" + registeredTarget.LevelObjectNetId;
                if (seenCookerNetIds.Contains(registeredTarget.CookerNetId) || seenTargets.Contains(key))
                {
                    continue;
                }

                NetCookTargetContext copy = this.CloneNetCookTargetContext(registeredTarget);
                if (!this.TryRefreshNetCookTargetWorldPosition(copy, true))
                {
                    continue;
                }

                targets.Add(copy);
                seenCookerNetIds.Add(copy.CookerNetId);
                seenTargets.Add(key);
                added++;
            }

            return added;
        }

        private int TryAddNearbyCookBuildTargetsFromLevelObjectOwners(List<NetCookTargetContext> targets, HashSet<string> seenTargets, HashSet<uint> seenCookerNetIds, Vector3 scanOrigin, int desiredCookerStaticId, int desiredCookerType, ref int skippedDifferentCooker, ref int skippedDuplicateCooker)
        {
            if (targets == null || seenTargets == null || seenCookerNetIds == null || this.netCookAuraMonoLevelObjectPtrs.Count <= 0)
            {
                return 0;
            }

            int added = 0;
            int inspectedLevelObjects = 0;
            int inspectedOwners = 0;
            int cookBuildOwners = 0;
            HashSet<uint> inspectedOwnerNetIds = new HashSet<uint>();

            try
            {
                foreach (KeyValuePair<ulong, long> pair in this.netCookAuraMonoLevelObjectPtrs)
                {
                    if (targets.Count >= NetCookMaxCaptureTargets || inspectedLevelObjects >= 256)
                    {
                        break;
                    }

                    inspectedLevelObjects++;
                    if (!this.TryGetNetCookLevelObjectOwnerNetIdAuraMono(pair.Key, pair.Value, out uint ownerNetId) || ownerNetId == 0U)
                    {
                        continue;
                    }

                    if (!inspectedOwnerNetIds.Add(ownerNetId))
                    {
                        continue;
                    }

                    inspectedOwners++;
                    if (!this.TryGetAuraMonoEntityObjectByNetId(ownerNetId, out IntPtr ownerEntityObj) || ownerEntityObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryResolveNetCookBuildComponentAuraMono(ownerEntityObj, out IntPtr cookBuildComponentObj, out _))
                    {
                        continue;
                    }

                    cookBuildOwners++;
                    added += this.TryAddCookBuildBurnerMapTargetsAuraMono(
                        ownerNetId,
                        ownerEntityObj,
                        cookBuildComponentObj,
                        targets,
                        seenTargets,
                        seenCookerNetIds,
                        scanOrigin,
                        desiredCookerStaticId,
                        desiredCookerType,
                        ref skippedDifferentCooker,
                        ref skippedDuplicateCooker);
                }

                if (inspectedOwners > 0 || added > 0)
                {
                    this.NetCookLog("LevelObject owner resolver inspectedLevelObjects=" + inspectedLevelObjects + " owners=" + inspectedOwners + " cookBuilds=" + cookBuildOwners + " added=" + added + ".");
                }
            }
            catch (Exception ex)
            {
                this.NetCookLog("LevelObject owner resolver exception: " + ex.Message);
            }

            return added;
        }

        private bool TryGetNetCookLevelObjectOwnerNetIdAuraMono(ulong levelObjectNetId, long levelObjectPtr, out uint ownerNetId)
        {
            ownerNetId = 0U;

            try
            {
                if (this.TryResolveOwnerIdFromLevelObjectIdMono(levelObjectNetId, out ownerNetId) && ownerNetId != 0U)
                {
                    return true;
                }

                if (levelObjectNetId > uint.MaxValue)
                {
                    ownerNetId = ExtractNetCookOwnerNetId(levelObjectNetId);
                    return ownerNetId != 0U;
                }

                IntPtr levelObjectObj = new IntPtr(levelObjectPtr);
                if (levelObjectObj != IntPtr.Zero
                    && this.TryInvokeAuraMonoZeroArg(levelObjectObj, out IntPtr boxedOwnerNetId, "get_ownerNetId")
                    && this.TryUnboxMonoUInt32(boxedOwnerNetId, out ownerNetId))
                {
                    return ownerNetId != 0U;
                }
            }
            catch
            {
            }

            return false;
        }

        private int TryAddCookBuildBurnerMapTargetsAuraMono(uint ownerCookBuildNetId, IntPtr ownerEntityObj, IntPtr cookBuildComponentObj, List<NetCookTargetContext> targets, HashSet<string> seenTargets, HashSet<uint> seenCookerNetIds, Vector3 scanOrigin, int desiredCookerStaticId, int desiredCookerType, ref int skippedDifferentCooker, ref int skippedDuplicateCooker)
        {
            if (ownerCookBuildNetId == 0U || cookBuildComponentObj == IntPtr.Zero || targets == null || seenTargets == null || seenCookerNetIds == null)
            {
                return 0;
            }

            int cookerStaticId = 0;
            this.TryGetMonoInt32Member(cookBuildComponentObj, "_cookerStaticId", out cookerStaticId);
            if (cookerStaticId <= 0)
            {
                this.TryGetMonoInt32Member(cookBuildComponentObj, "cookerStaticId", out cookerStaticId);
            }
            if (cookerStaticId <= 0)
            {
                return 0;
            }

            int cookerType = 0;
            if (cookerStaticId == desiredCookerStaticId && desiredCookerType > 0)
            {
                cookerType = desiredCookerType;
            }
            else if (cookerStaticId == this.netCookCookerStaticId && this.netCookCookerType > 0)
            {
                cookerType = this.netCookCookerType;
            }
            else
            {
                this.TryGetCookerTypeForStaticId(cookerStaticId, out cookerType);
            }
            if (cookerType <= 0)
            {
                cookerType = desiredCookerType;
            }

            if (!this.IsCompatibleNetCookCooker(cookerStaticId, cookerType, desiredCookerStaticId, desiredCookerType))
            {
                skippedDifferentCooker++;
                return 0;
            }

            IntPtr burnerMapObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(cookBuildComponentObj, "_cookBurnerMap", out burnerMapObj) || burnerMapObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(cookBuildComponentObj, "cookBurnerMap", out burnerMapObj) || burnerMapObj == IntPtr.Zero))
            {
                return 0;
            }

            List<IntPtr> burnerEntries = new List<IntPtr>(8);
            List<uint> burnerPins = new List<uint>();
            if (!this.TryEnumerateAuraMonoCollectionItems(burnerMapObj, burnerEntries, burnerPins) || burnerEntries.Count <= 0)
            {
                FreeAuraMonoPins(burnerPins);
                return 0;
            }

            Vector3 ownerPosition;
            bool hasOwnerPosition = this.TryGetAuraMonoEntityPosition(ownerEntityObj, out ownerPosition)
                || this.TryExtractHomePositionMonoObject(ownerEntityObj, out ownerPosition)
                || this.TryExtractHomePositionMonoObject(cookBuildComponentObj, out ownerPosition);
            if (!hasOwnerPosition)
            {
                ownerPosition = scanOrigin;
            }

            int added = 0;
            float maxScanDistance = Mathf.Clamp(this.netCookScanRadiusMeters, NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters);
            try
            {
            for (int i = 0; i < burnerEntries.Count && targets.Count < NetCookMaxCaptureTargets; i++)
            {
                IntPtr entryObj = burnerEntries[i];
                if (entryObj == IntPtr.Zero)
                {
                    continue;
                }

                if (!this.TryGetMonoUInt64Member(entryObj, "Key", out ulong levelObjectNetId)
                    && !this.TryGetMonoUInt64Member(entryObj, "key", out levelObjectNetId)
                    && !this.TryGetMonoUInt64Member(entryObj, "_key", out levelObjectNetId))
                {
                    continue;
                }
                if (levelObjectNetId <= uint.MaxValue)
                {
                    continue;
                }

                IntPtr cookingComponentObj = IntPtr.Zero;
                if ((!this.TryGetMonoObjectMember(entryObj, "Value", out cookingComponentObj) || cookingComponentObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entryObj, "value", out cookingComponentObj) || cookingComponentObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entryObj, "_value", out cookingComponentObj) || cookingComponentObj == IntPtr.Zero))
                {
                    continue;
                }

                if (!this.TryGetNetCookCookingComponentEntityNetId(cookingComponentObj, out uint burnerCookerNetId) || burnerCookerNetId == 0U)
                {
                    continue;
                }

                string key = burnerCookerNetId + ":" + levelObjectNetId;
                if (seenCookerNetIds.Contains(burnerCookerNetId) || seenTargets.Contains(key))
                {
                    skippedDuplicateCooker++;
                    continue;
                }

                bool hasWorldPosition = this.TryGetNetCookTargetWorldPosition(levelObjectNetId, burnerCookerNetId, out Vector3 worldPosition);
                if (!hasWorldPosition)
                {
                    worldPosition = ownerPosition;
                    hasWorldPosition = true;
                }
                if (hasWorldPosition && Vector3.Distance(scanOrigin, worldPosition) > maxScanDistance)
                {
                    continue;
                }

                seenCookerNetIds.Add(burnerCookerNetId);
                seenTargets.Add(key);
                targets.Add(new NetCookTargetContext
                {
                    CookerNetId = burnerCookerNetId,
                    CookerStaticId = cookerStaticId,
                    CookerType = cookerType,
                    LevelObjectNetId = levelObjectNetId,
                    HasWorldPosition = hasWorldPosition,
                    WorldPosition = worldPosition
                });
                added++;
            }
            }
            finally
            {
                FreeAuraMonoPins(burnerPins);
            }

            return added;
        }

        private bool TryResolveNetCookContextsFromRegisteredCache(List<NetCookTargetContext> targets, out string status)
        {
            status = "Registered cooker cache unavailable.";
            int preferredCookerStaticId = this.netCookCookerStaticId > 0 ? this.netCookCookerStaticId : this.netCookLastCapturedCookerStaticId;
            int preferredCookerType = this.netCookCookerType > 0 ? this.netCookCookerType : this.netCookLastCapturedCookerType;
            if (targets == null
                || (this.netCookRegisteredTargets.Count <= 0 && this.netCookRegisteredWorldCookers.Count <= 0)
                || preferredCookerStaticId <= 0
                || preferredCookerType <= 0)
            {
                return false;
            }

            HashSet<string> seenTargets = new HashSet<string>();
            HashSet<uint> seenCookerNetIds = new HashSet<uint>();
            int added = this.TryAddRegisteredNetCookTargets(targets, seenTargets, seenCookerNetIds, preferredCookerStaticId, preferredCookerType);
            added += this.TryAddRegisteredWorldCookerTargets(targets, seenTargets, seenCookerNetIds, preferredCookerStaticId, preferredCookerType);
            if (added <= 0 || targets.Count <= 0)
            {
                targets.Clear();
                status = "No cached cooker targets matched the last cooker type.";
                return false;
            }

            int removedOutOfRange = this.RemoveOutOfRangeNetCookTargets(targets, seenTargets, seenCookerNetIds);
            if (targets.Count <= 0)
            {
                status = "Cached cooker targets are outside scan radius.";
                return false;
            }

            this.RegisterNetCookTargets(targets);
            this.SortNetCookTargetsByDistanceFromScanOrigin(targets);
            status = "Captured " + targets.Count + " cached stove(s) within " + Mathf.Clamp(this.netCookScanRadiusMeters, NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters).ToString("F0") + "m.";
            this.NetCookLog(status + (removedOutOfRange > 0 ? " Filtered " + removedOutOfRange + " stale cached target(s)." : string.Empty));
            this.LogNetCookTargetSummary(targets);
            return true;
        }

        // Direct ECS source for cook builds (stoves), replacing the crash-prone recursive entity-graph
        // walk (TryEnumerateAuraMonoLoadedEntityObjects). CookBuildComponent is a ViewComponent in the
        // Homeland namespace, so Entities.GetComponents<CookBuildComponent> enumerates every stove
        // without dereferencing arbitrary entity pointers. See AGENTS.md / TYPE_RESOLUTION.md.
        private IntPtr netCookCookBuildComponentAuraClass = IntPtr.Zero;

        private bool TryResolveNetCookCookBuildComponentClassAuraMono(out IntPtr componentClass)
        {
            if (this.netCookCookBuildComponentAuraClass == IntPtr.Zero)
            {
                this.netCookCookBuildComponentAuraClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.Gameplay.Component.Homeland.CookBuildComponent");
                if (this.netCookCookBuildComponentAuraClass == IntPtr.Zero)
                {
                    this.netCookCookBuildComponentAuraClass = this.FindAuraMonoClassByFullName(
                        "XDTLevelAndEntity.GamePlay.Component.Homeland.CookBuildComponent");
                }
            }

            componentClass = this.netCookCookBuildComponentAuraClass;
            return componentClass != IntPtr.Zero;
        }

        // Enumerate every CookBuildComponent object via the safe direct-ECS GetComponents path.
        // Returned IntPtrs are valid only synchronously — scalarize before any coroutine yield.
        private bool TryEnumerateNetCookCookBuildComponentObjects(out List<IntPtr> cookBuildComponents, out string status, List<uint> componentPins = null)
        {
            cookBuildComponents = null;
            status = string.Empty;
            if (!this.TryResolveNetCookCookBuildComponentClassAuraMono(out IntPtr cookBuildClass))
            {
                status = "CookBuildComponent class unavailable (AuraMono).";
                return false;
            }

            if (!this.TryAuraMonoGetComponentObjects(cookBuildClass, out cookBuildComponents, componentPins)
                || cookBuildComponents == null
                || cookBuildComponents.Count == 0)
            {
                status = "GetComponents<CookBuildComponent> returned no stoves.";
                return false;
            }

            return true;
        }

        // Resolve the owner entity netId for a cook-build component (its `entity` back-reference).
        private bool TryGetNetCookCookBuildOwnerNetId(IntPtr cookBuildComponentObj, out uint ownerNetId)
        {
            ownerNetId = 0U;
            if (cookBuildComponentObj == IntPtr.Zero)
            {
                return false;
            }

            if ((this.TryGetMonoObjectMember(cookBuildComponentObj, "entity", out IntPtr entityObj) && entityObj != IntPtr.Zero)
                || (this.TryGetMonoObjectMember(cookBuildComponentObj, "_entity", out entityObj) && entityObj != IntPtr.Zero))
            {
                return this.TryGetAuraMonoEntityNetId(entityObj, out ownerNetId) && ownerNetId != 0U;
            }

            return false;
        }

        // CookingComponent (the burner/pot, also a Homeland ViewComponent) — direct-ECS enumeration,
        // replacing the per-entity resolve over the crash-prone entity-graph walk.
        private IntPtr netCookCookingComponentAuraClass = IntPtr.Zero;

        private bool TryResolveNetCookCookingComponentClassAuraMono(out IntPtr componentClass)
        {
            if (this.netCookCookingComponentAuraClass == IntPtr.Zero)
            {
                this.netCookCookingComponentAuraClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.Gameplay.Component.Homeland.CookingComponent");
                if (this.netCookCookingComponentAuraClass == IntPtr.Zero)
                {
                    this.netCookCookingComponentAuraClass = this.FindAuraMonoClassByFullName(
                        "XDTLevelAndEntity.GamePlay.Component.Homeland.CookingComponent");
                }
            }

            componentClass = this.netCookCookingComponentAuraClass;
            return componentClass != IntPtr.Zero;
        }

        private bool TryEnumerateNetCookCookingComponentObjects(out List<IntPtr> cookingComponents, out string status, List<uint> componentPins = null)
        {
            cookingComponents = null;
            status = string.Empty;
            if (!this.TryResolveNetCookCookingComponentClassAuraMono(out IntPtr cookingClass))
            {
                status = "CookingComponent class unavailable (AuraMono).";
                return false;
            }

            if (!this.TryAuraMonoGetComponentObjects(cookingClass, out cookingComponents, componentPins)
                || cookingComponents == null
                || cookingComponents.Count == 0)
            {
                status = "GetComponents<CookingComponent> returned no burners.";
                return false;
            }

            return true;
        }

        private bool TryResolveNetCookContextsFromCookBuildComponents(List<NetCookTargetContext> targets, out string status)
        {
            status = "Cook-build component registry unavailable.";
            if (targets == null)
            {
                status = "Target buffer unavailable.";
                return false;
            }

            try
            {
                if (!this.TryGetNetCookScanOrigin(out Vector3 scanOrigin, out string originStatus))
                {
                    status = "Cook-build scan origin unavailable: " + originStatus;
                    return false;
                }

                List<uint> cookBuildPins = new List<uint>();
                if (!this.TryEnumerateNetCookCookBuildComponentObjects(out List<IntPtr> cookBuildComponents, out string enumerateStatus, cookBuildPins))
                {
                    FreeAuraMonoPins(cookBuildPins);
                    status = "Cook-build component list unavailable: " + enumerateStatus;
                    return false;
                }

                float maxScanDistance = Mathf.Clamp(this.netCookScanRadiusMeters, NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters);
                List<NetCookTargetContext> discoveredTargets = new List<NetCookTargetContext>(NetCookMaxCaptureTargets);
                HashSet<string> discoveredKeys = new HashSet<string>();
                HashSet<uint> discoveredCookerNetIds = new HashSet<uint>();
                int inspectedEntities = 0;
                int inspectedCookBuilds = 0;
                int inspectedBurners = 0;

                try
                {
                for (int i = 0; i < cookBuildComponents.Count; i++)
                {
                    if (discoveredTargets.Count >= NetCookMaxCaptureTargets)
                    {
                        break;
                    }

                    IntPtr cookBuildComponentObj = cookBuildComponents[i];
                    if (cookBuildComponentObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    inspectedEntities++;
                    inspectedCookBuilds++;

                    // Owner entity (world position / distance cull) is the component's back-reference.
                    IntPtr ownerEntityObj = IntPtr.Zero;
                    if (!this.TryGetMonoObjectMember(cookBuildComponentObj, "entity", out ownerEntityObj) || ownerEntityObj == IntPtr.Zero)
                    {
                        this.TryGetMonoObjectMember(cookBuildComponentObj, "_entity", out ownerEntityObj);
                    }

                    Vector3 ownerPosition = scanOrigin;
                    bool hasOwnerPosition = false;
                    if (ownerEntityObj != IntPtr.Zero)
                    {
                        hasOwnerPosition = this.TryGetAuraMonoEntityPosition(ownerEntityObj, out ownerPosition)
                            || this.TryExtractHomePositionMonoObject(ownerEntityObj, out ownerPosition);
                    }
                    if (!hasOwnerPosition)
                    {
                        hasOwnerPosition = this.TryExtractHomePositionMonoObject(cookBuildComponentObj, out ownerPosition);
                    }
                    if (hasOwnerPosition && Vector3.Distance(scanOrigin, ownerPosition) > maxScanDistance)
                    {
                        continue;
                    }
                    if (!hasOwnerPosition)
                    {
                        ownerPosition = scanOrigin;
                    }

                    int cookerStaticId = 0;
                    this.TryGetMonoInt32Member(cookBuildComponentObj, "_cookerStaticId", out cookerStaticId);
                    if (cookerStaticId <= 0)
                    {
                        this.TryGetMonoInt32Member(cookBuildComponentObj, "cookerStaticId", out cookerStaticId);
                    }
                    if (cookerStaticId <= 0)
                    {
                        continue;
                    }

                    int cookerType = 0;
                    if (cookerStaticId == this.netCookCookerStaticId && this.netCookCookerType > 0)
                    {
                        cookerType = this.netCookCookerType;
                    }

                    if (!this.TryGetMonoObjectMember(cookBuildComponentObj, "_cookBurnerMap", out IntPtr burnerMapObj) || burnerMapObj == IntPtr.Zero)
                    {
                        this.TryGetMonoObjectMember(cookBuildComponentObj, "cookBurnerMap", out burnerMapObj);
                    }
                    if (burnerMapObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    List<IntPtr> burnerEntries = new List<IntPtr>(8);
                    List<uint> burnerPins = new List<uint>();
                    if (!this.TryEnumerateAuraMonoCollectionItems(burnerMapObj, burnerEntries, burnerPins) || burnerEntries.Count <= 0)
                    {
                        FreeAuraMonoPins(burnerPins);
                        continue;
                    }

                    try
                    {
                    for (int entryIndex = 0; entryIndex < burnerEntries.Count; entryIndex++)
                    {
                        if (discoveredTargets.Count >= NetCookMaxCaptureTargets)
                        {
                            break;
                        }

                        IntPtr entryObj = burnerEntries[entryIndex];
                        if (entryObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        inspectedBurners++;
                        if (!this.TryGetMonoUInt64Member(entryObj, "Key", out ulong levelObjectNetId)
                            && !this.TryGetMonoUInt64Member(entryObj, "key", out levelObjectNetId)
                            && !this.TryGetMonoUInt64Member(entryObj, "_key", out levelObjectNetId))
                        {
                            continue;
                        }

                        if (levelObjectNetId <= uint.MaxValue)
                        {
                            continue;
                        }

                        IntPtr cookingComponentObj = IntPtr.Zero;
                        if ((!this.TryGetMonoObjectMember(entryObj, "Value", out cookingComponentObj) || cookingComponentObj == IntPtr.Zero)
                            && (!this.TryGetMonoObjectMember(entryObj, "value", out cookingComponentObj) || cookingComponentObj == IntPtr.Zero)
                            && (!this.TryGetMonoObjectMember(entryObj, "_value", out cookingComponentObj) || cookingComponentObj == IntPtr.Zero))
                        {
                            continue;
                        }

                        if (!this.TryGetNetCookCookingComponentEntityNetId(cookingComponentObj, out uint burnerCookerNetId) || burnerCookerNetId == 0U)
                        {
                            continue;
                        }

                        if (!discoveredCookerNetIds.Add(burnerCookerNetId))
                        {
                            continue;
                        }

                        string key = burnerCookerNetId + ":" + levelObjectNetId;
                        if (!discoveredKeys.Add(key))
                        {
                            discoveredCookerNetIds.Remove(burnerCookerNetId);
                            continue;
                        }

                        int targetCookerType = cookerType;

                        discoveredTargets.Add(new NetCookTargetContext
                        {
                            CookerNetId = burnerCookerNetId,
                            CookerStaticId = cookerStaticId,
                            CookerType = targetCookerType,
                            LevelObjectNetId = levelObjectNetId,
                            HasWorldPosition = true,
                            WorldPosition = ownerPosition
                        });
                    }
                    }
                    finally
                    {
                        FreeAuraMonoPins(burnerPins);
                    }
                }

                if (discoveredTargets.Count <= 0)
                {
                    status = "Cook-build component scan found no nearby burners.";
                    return false;
                }

                int desiredCookerStaticId = this.GetPreferredNetCookTargetStaticId(discoveredTargets);
                int desiredCookerType = this.GetPreferredNetCookTargetCookerType(discoveredTargets, desiredCookerStaticId);
                if (desiredCookerType <= 0 && desiredCookerStaticId > 0)
                {
                    this.TryGetCookerTypeForStaticId(desiredCookerStaticId, out desiredCookerType);
                }

                HashSet<string> seenTargets = new HashSet<string>();
                HashSet<uint> seenCookerNetIds = new HashSet<uint>();
                for (int i = 0; i < discoveredTargets.Count && targets.Count < NetCookMaxCaptureTargets; i++)
                {
                    NetCookTargetContext target = discoveredTargets[i];
                    if (target.CookerType <= 0)
                    {
                        target.CookerType = desiredCookerType;
                    }
                    if (!this.IsCompatibleNetCookCooker(target.CookerStaticId, target.CookerType, desiredCookerStaticId, desiredCookerType))
                    {
                        continue;
                    }

                    string key = target.CookerNetId + ":" + target.LevelObjectNetId;
                    if (!seenCookerNetIds.Add(target.CookerNetId) || !seenTargets.Add(key))
                    {
                        seenCookerNetIds.Remove(target.CookerNetId);
                        continue;
                    }

                    targets.Add(target);
                }

                if (targets.Count <= 0)
                {
                    status = "Cook-build component scan found no matching cooker type.";
                    return false;
                }

                this.RegisterNetCookTargets(targets);
                this.SortNetCookTargetsByDistanceFromScanOrigin(targets);
                status = "Captured " + targets.Count + " stove(s) from cook-build registry within " + maxScanDistance.ToString("F0") + "m.";
                this.NetCookLog("Cook-build registry scan entities=" + inspectedEntities + " cookBuilds=" + inspectedCookBuilds + " burners=" + inspectedBurners + " selectedStatic=" + desiredCookerStaticId + " selectedType=" + desiredCookerType + ".");
                this.NetCookLog(status);
                this.LogNetCookTargetSummary(targets);
                return true;
                }
                finally
                {
                    FreeAuraMonoPins(cookBuildPins);
                }
            }
            catch (Exception ex)
            {
                status = "Cook-build registry scan exception: " + ex.Message;
                this.NetCookLog(status);
                return false;
            }
        }

        private NetCookTargetContext CloneNetCookTargetContext(NetCookTargetContext source)
        {
            if (source == null)
            {
                return null;
            }

            return new NetCookTargetContext
            {
                CookerNetId = source.CookerNetId,
                CookerStaticId = source.CookerStaticId,
                CookerType = source.CookerType,
                LevelObjectNetId = source.LevelObjectNetId,
                Phase = source.Phase,
                ContinuePulses = source.ContinuePulses,
                SentCount = source.SentCount,
                LastStatus = source.LastStatus,
                IdleRetries = source.IdleRetries,
                LastStatusActionAt = source.LastStatusActionAt,
                LastCookCommandAt = source.LastCookCommandAt,
                NextActionAt = source.NextActionAt,
                HasWorldPosition = source.HasWorldPosition,
                WorldPosition = source.WorldPosition
            };
        }

        private bool TryRefreshNetCookTargetWorldPosition(NetCookTargetContext target, bool requireResolvedPosition)
        {
            if (target == null)
            {
                return false;
            }

            if (this.TryGetNetCookTargetWorldPosition(target.LevelObjectNetId, target.CookerNetId, out Vector3 resolvedPosition)
                && resolvedPosition != Vector3.zero)
            {
                target.HasWorldPosition = true;
                target.WorldPosition = resolvedPosition;
                return true;
            }

            if (!requireResolvedPosition && target.HasWorldPosition && target.WorldPosition != Vector3.zero)
            {
                return true;
            }

            target.HasWorldPosition = false;
            target.WorldPosition = Vector3.zero;
            return false;
        }

        private bool TryAddNetCookTargetFromKnownIds(uint cookerNetId, ulong levelObjectNetId, int desiredCookerStaticId, int desiredCookerType, List<NetCookTargetContext> targets, HashSet<string> seenTargets, HashSet<uint> seenCookerNetIds)
        {
            if (cookerNetId == 0U || levelObjectNetId == 0UL || targets == null || seenTargets == null || seenCookerNetIds == null)
            {
                return false;
            }

            string key = cookerNetId + ":" + levelObjectNetId;
            if (seenCookerNetIds.Contains(cookerNetId) || seenTargets.Contains(key))
            {
                return false;
            }

            int targetStaticId = desiredCookerStaticId;
            int targetCookerType = desiredCookerType;
            uint ownerNetId = ExtractNetCookOwnerNetId(levelObjectNetId);
            if (ownerNetId != 0U && this.TryResolveNetCookBurnerFromCookBuildAuraMono(ownerNetId, levelObjectNetId, out uint resolvedCookerNetId, out int resolvedStaticId, out int resolvedCookerType, out _))
            {
                if (resolvedCookerNetId != 0U)
                {
                    cookerNetId = resolvedCookerNetId;
                    key = cookerNetId + ":" + levelObjectNetId;
                    if (seenCookerNetIds.Contains(cookerNetId) || seenTargets.Contains(key))
                    {
                        return false;
                    }
                }

                if (resolvedStaticId > 0)
                {
                    targetStaticId = resolvedStaticId;
                }
                if (resolvedCookerType > 0)
                {
                    targetCookerType = resolvedCookerType;
                }
            }

            if (targetStaticId <= 0 && targetCookerType <= 0)
            {
                return false;
            }

            if (targetCookerType <= 0 && targetStaticId > 0)
            {
                this.TryGetCookerTypeForStaticId(targetStaticId, out targetCookerType);
            }

            if (!this.IsCompatibleNetCookCooker(targetStaticId, targetCookerType, desiredCookerStaticId, desiredCookerType))
            {
                return false;
            }

            bool hasWorldPosition = this.TryGetNetCookTargetWorldPosition(levelObjectNetId, cookerNetId, out Vector3 worldPosition);
            seenCookerNetIds.Add(cookerNetId);
            seenTargets.Add(key);
            targets.Add(new NetCookTargetContext
            {
                CookerNetId = cookerNetId,
                CookerStaticId = targetStaticId,
                CookerType = targetCookerType,
                LevelObjectNetId = levelObjectNetId,
                HasWorldPosition = hasWorldPosition,
                WorldPosition = worldPosition
            });
            return true;
        }

        private void RegisterNetCookWorldCooker(uint worldCookerNetId, int resourceId, int staticId, int knownCookerType = 0)
        {
            if (worldCookerNetId == 0U)
            {
                return;
            }

            int resolvedStaticId = staticId;
            int cookerType = knownCookerType;
            if (resolvedStaticId <= 0 || cookerType <= 0)
            {
                if (this.TryResolveNetCookWorldCookerDataManaged(worldCookerNetId, out int managedStaticId, out _))
                {
                    resolvedStaticId = managedStaticId;
                    if (cookerType <= 0)
                    {
                        this.TryGetCookerTypeForStaticId(resolvedStaticId, out cookerType);
                    }
                }
            }

            if (resolvedStaticId <= 0)
            {
                resolvedStaticId = resourceId;
                if (cookerType <= 0)
                {
                    this.TryGetCookerTypeForStaticId(resolvedStaticId, out cookerType);
                }
            }
            else if (cookerType <= 0)
            {
                this.TryGetCookerTypeForStaticId(resolvedStaticId, out cookerType);
            }

            if (this.netCookRegisteredWorldCookers.TryGetValue(worldCookerNetId, out NetCookRegisteredWorldCooker existingCooker)
                && existingCooker != null
                && existingCooker.ResourceId == resourceId
                && existingCooker.StaticId == resolvedStaticId
                && existingCooker.CookerType == cookerType)
            {
                return;
            }

            this.netCookRegisteredWorldCookers[worldCookerNetId] = new NetCookRegisteredWorldCooker
            {
                OwnerNetId = worldCookerNetId,
                ResourceId = resourceId,
                StaticId = resolvedStaticId,
                CookerType = cookerType
            };
            if (NetCookScanDebugLogsEnabled)
            {
                this.NetCookLog("Registered world cooker owner=" + worldCookerNetId + " resourceId=" + resourceId + " staticId=" + resolvedStaticId + " cookerType=" + cookerType + ".");
            }
        }

        private void EnsureNetCookWorldCookerRegistrationPatch()
        {
            if ((this.netCookWorldCookerRegistrationPatched && this.netCookCookBuildSpawnPatched && this.netCookCookBuildUpdatePatched) || HeartopiaComplete.harmonyInstance == null)
            {
                return;
            }

            if (Time.unscaledTime < this.nextNetCookWorldCookerPatchAttemptAt)
            {
                return;
            }
            this.nextNetCookWorldCookerPatchAttemptAt = Time.unscaledTime + 2f;

            try
            {
                if (!this.netCookWorldCookerRegistrationPatched)
                {
                    Type protocolType = this.FindLoadedType(
                        "XDTDataAndProtocol.ProtocolService.Cooking.CookingProtocolManager",
                        "CookingProtocolManager");
                    MethodInfo addWorldCookerMethod = protocolType?.GetMethod("AddWorldCooker", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(uint), typeof(int), typeof(int) }, null);
                    MethodInfo postfixMethod = typeof(HeartopiaComplete).GetMethod(nameof(NetCookAddWorldCookerPostfix), BindingFlags.Static | BindingFlags.NonPublic);
                    if (addWorldCookerMethod != null && postfixMethod != null)
                    {
                        HeartopiaComplete.harmonyInstance.Patch(addWorldCookerMethod, null, new HarmonyMethod(postfixMethod), null, null, null);
                        this.netCookWorldCookerRegistrationPatched = true;
                        this.NetCookLog("Patched CookingProtocolManager.AddWorldCooker registry hook.");
                    }
                }

                Type cookBuildType = null;
                if (!this.netCookCookBuildSpawnPatched || !this.netCookCookBuildUpdatePatched)
                {
                    cookBuildType = this.FindLoadedType(
                        "XDTLevelAndEntity.Gameplay.Component.Homeland.CookBuildComponent",
                        "CookBuildComponent");

                    // These Homeland ECS components are AuraMono-only on this build, so the MANAGED
                    // type is usually absent here -> the Harmony hooks below never install and the
                    // registry stays empty. That's fine now (capture uses GetComponents<CookBuildComponent>),
                    // but log it once so the hook's status is observable instead of silently dead.
                    if (cookBuildType == null && !this.netCookCookBuildHookWarned)
                    {
                        this.netCookCookBuildHookWarned = true;
                        this.NetCookHookLog("CookBuild registry hooks NOT installed: managed CookBuildComponent type unavailable via FindLoadedType (AuraMono-only build). Capture relies on GetComponents<CookBuildComponent> instead.");
                    }
                }

                if (!this.netCookCookBuildSpawnPatched && cookBuildType != null)
                {
                    MethodInfo onSpawnedMethod = cookBuildType.GetMethod("OnSpawned", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    MethodInfo postfixMethod = typeof(HeartopiaComplete).GetMethod(nameof(NetCookCookBuildOnSpawnedPostfix), BindingFlags.Static | BindingFlags.NonPublic);
                    if (onSpawnedMethod != null && postfixMethod != null)
                    {
                        HeartopiaComplete.harmonyInstance.Patch(onSpawnedMethod, null, new HarmonyMethod(postfixMethod), null, null, null);
                        this.netCookCookBuildSpawnPatched = true;
                        this.NetCookHookLog("Patched CookBuildComponent.OnSpawned registry hook.");
                    }
                    else if (!this.netCookCookBuildHookWarned)
                    {
                        this.netCookCookBuildHookWarned = true;
                        this.NetCookHookLog("CookBuildComponent.OnSpawned hook NOT installed: onSpawnedMethod=" + (onSpawnedMethod != null) + " postfix=" + (postfixMethod != null) + ".");
                    }
                }

                if (!this.netCookCookBuildUpdatePatched && cookBuildType != null)
                {
                    MethodInfo onUpdatedMethod = cookBuildType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == "OnComponentUpdated" && m.GetParameters().Length == 1);
                    MethodInfo postfixMethod = typeof(HeartopiaComplete).GetMethod(nameof(NetCookCookBuildOnUpdatedPostfix), BindingFlags.Static | BindingFlags.NonPublic);
                    if (onUpdatedMethod != null && postfixMethod != null)
                    {
                        HeartopiaComplete.harmonyInstance.Patch(onUpdatedMethod, null, new HarmonyMethod(postfixMethod), null, null, null);
                        this.netCookCookBuildUpdatePatched = true;
                        this.NetCookHookLog("Patched CookBuildComponent.OnComponentUpdated registry hook.");
                    }
                    else if (!this.netCookCookBuildHookWarned)
                    {
                        this.netCookCookBuildHookWarned = true;
                        this.NetCookHookLog("CookBuildComponent.OnComponentUpdated hook NOT installed: onUpdatedMethod=" + (onUpdatedMethod != null) + " postfix=" + (postfixMethod != null) + ".");
                    }
                }
            }
            catch (Exception ex)
            {
                this.NetCookLog("Cooker registry hook failed: " + ex.Message);
            }
        }

        private static void NetCookAddWorldCookerPostfix(uint worldCookerNetId, int resourceId, int staticId)
        {
            HeartopiaComplete.Instance?.RegisterNetCookWorldCooker(worldCookerNetId, resourceId, staticId);
        }

        private static void NetCookCookBuildOnSpawnedPostfix(object __instance)
        {
            HeartopiaComplete inst = HeartopiaComplete.Instance;
            if (inst == null)
            {
                return;
            }

            inst.netCookCookBuildSpawnHookCount++;
            if (inst.netCookCookBuildSpawnHookCount <= 3 || inst.netCookCookBuildSpawnHookCount % 50 == 0)
            {
                inst.NetCookHookLog("CookBuild OnSpawned hook fired (count=" + inst.netCookCookBuildSpawnHookCount + ").");
            }

            inst.RegisterNetCookCookBuildComponent(__instance);
        }

        private static void NetCookCookBuildOnUpdatedPostfix(object __instance)
        {
            HeartopiaComplete inst = HeartopiaComplete.Instance;
            if (inst == null)
            {
                return;
            }

            inst.netCookCookBuildUpdateHookCount++;
            if (inst.netCookCookBuildUpdateHookCount <= 3 || inst.netCookCookBuildUpdateHookCount % 50 == 0)
            {
                inst.NetCookHookLog("CookBuild OnComponentUpdated hook fired (count=" + inst.netCookCookBuildUpdateHookCount + ").");
            }

            inst.RegisterNetCookCookBuildComponent(__instance);
        }

        private void RegisterNetCookCookBuildComponent(object cookBuildComponent)
        {
            if (cookBuildComponent == null)
            {
                return;
            }

            bool registeredManaged = false;
            try
            {
                uint ownerNetId = 0U;
                object entity = this.TryGetManagedMemberValue(cookBuildComponent, "entity");
                if (entity != null)
                {
                    this.TryReadManagedNetIdMember(entity, "netId", out ownerNetId);
                    if (ownerNetId == 0U)
                    {
                        this.TryInvokeManagedNetIdMethod(entity, "GetNetId", out ownerNetId);
                    }
                }

                if (ownerNetId == 0U)
                {
                    return;
                }

                int staticId = 0;
                this.TryReadManagedInt32Member(cookBuildComponent, "_cookerStaticId", out staticId);
                if (staticId <= 0)
                {
                    object buildData = this.TryInvokeManagedObjectMethod(cookBuildComponent, "GetBuildData");
                    if (buildData != null)
                    {
                        this.TryReadManagedInt32Member(buildData, "staticId", out staticId);
                    }
                }

                if (staticId <= 0 && this.TryResolveNetCookWorldCookerDataManaged(ownerNetId, out int managedStaticId, out _))
                {
                    staticId = managedStaticId;
                }

                if (staticId <= 0)
                {
                    return;
                }

                this.RegisterNetCookWorldCooker(ownerNetId, 0, staticId);
                int cookerType = 0;
                this.TryGetCookerTypeForStaticId(staticId, out cookerType);
                this.RegisterNetCookBurnersFromCookBuild(cookBuildComponent, staticId, cookerType);
                registeredManaged = true;
            }
            catch (Exception ex)
            {
                this.NetCookLog("CookBuild registration failed: " + ex.Message);
            }

            if (!registeredManaged)
            {
                this.TryRegisterNetCookCookBuildComponentAuraMono(cookBuildComponent);
            }
        }

        private bool TryRegisterNetCookCookBuildComponentAuraMono(object cookBuildComponent)
        {
            if (cookBuildComponent == null || !this.TryGetIl2CppObjectPointer(cookBuildComponent, out IntPtr cookBuildComponentObj) || cookBuildComponentObj == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;
                }

                IntPtr ownerEntityObj = IntPtr.Zero;
                if ((!this.TryGetMonoObjectMember(cookBuildComponentObj, "entity", out ownerEntityObj) || ownerEntityObj == IntPtr.Zero)
                    && !this.TryInvokeAuraMonoZeroArg(cookBuildComponentObj, out ownerEntityObj, "get_entity", "GetEntity"))
                {
                    return false;
                }

                if (!this.TryGetAuraMonoEntityNetId(ownerEntityObj, out uint ownerNetId) || ownerNetId == 0U)
                {
                    return false;
                }

                int staticId = 0;
                this.TryGetMonoInt32Member(cookBuildComponentObj, "_cookerStaticId", out staticId);
                if (staticId <= 0)
                {
                    this.TryGetMonoInt32Member(cookBuildComponentObj, "cookerStaticId", out staticId);
                }
                if (staticId <= 0 && this.TryResolveNetCookWorldCookerDataManaged(ownerNetId, out int managedStaticId, out _))
                {
                    staticId = managedStaticId;
                }
                if (staticId <= 0)
                {
                    return false;
                }

                int cookerType = 0;
                this.TryGetCookerTypeForStaticId(staticId, out cookerType);
                this.RegisterNetCookWorldCooker(ownerNetId, 0, staticId);

                Vector3 ownerPosition = Vector3.zero;
                this.TryGetAuraMonoEntityPosition(ownerEntityObj, out ownerPosition);
                if (ownerPosition == Vector3.zero)
                {
                    this.TryExtractHomePositionMonoObject(ownerEntityObj, out ownerPosition);
                }
                if (ownerPosition == Vector3.zero)
                {
                    this.TryExtractHomePositionMonoObject(cookBuildComponentObj, out ownerPosition);
                }

                List<NetCookTargetContext> targets = new List<NetCookTargetContext>(8);
                HashSet<string> seenTargets = new HashSet<string>();
                HashSet<uint> seenCookerNetIds = new HashSet<uint>();
                int skippedDifferentCooker = 0;
                int skippedDuplicateCooker = 0;

                int added = this.TryAddCookBuildBurnerMapTargetsAuraMono(
                    ownerNetId,
                    ownerEntityObj,
                    cookBuildComponentObj,
                    targets,
                    seenTargets,
                    seenCookerNetIds,
                    ownerPosition,
                    staticId,
                    cookerType,
                    ref skippedDifferentCooker,
                    ref skippedDuplicateCooker);

                if (added <= 0)
                {
                    added = this.TryAddSynthesizedNetCookBurnerTargets(
                        ownerNetId,
                        ownerPosition,
                        staticId,
                        cookerType,
                        staticId,
                        cookerType,
                        targets,
                        seenTargets,
                        seenCookerNetIds,
                        ref skippedDifferentCooker,
                        ref skippedDuplicateCooker,
                        null,
                        "cook-build-register");
                }

                if (targets.Count <= 0)
                {
                    return false;
                }

                this.RegisterNetCookTargets(targets);
                this.NetCookLog("Registered " + targets.Count + " AuraMono cook-build burner target(s) owner=" + ownerNetId + " staticId=" + staticId + " cookerType=" + cookerType + ".");
                return true;
            }
            catch (Exception ex)
            {
                this.NetCookLog("AuraMono CookBuild registration failed: " + ex.Message);
                return false;
            }
        }

        private void RegisterNetCookBurnersFromCookBuild(object cookBuildComponent, int staticId, int cookerType)
        {
            if (cookBuildComponent == null || staticId <= 0)
            {
                return;
            }

            try
            {
                object burnerMap = this.TryGetManagedMemberValue(cookBuildComponent, "_cookBurnerMap");
                if (!(burnerMap is IEnumerable enumerable))
                {
                    return;
                }

                int added = 0;
                foreach (object entry in enumerable)
                {
                    object keyObj = null;
                    object valueObj = null;
                    if (entry is DictionaryEntry dictionaryEntry)
                    {
                        keyObj = dictionaryEntry.Key;
                        valueObj = dictionaryEntry.Value;
                    }
                    else
                    {
                        keyObj = this.TryGetManagedMemberValue(entry, "Key");
                        valueObj = this.TryGetManagedMemberValue(entry, "Value");
                    }

                    if (!this.TryConvertManagedNetIdToUInt64(keyObj, out ulong levelObjectNetId) || levelObjectNetId == 0UL || valueObj == null)
                    {
                        continue;
                    }

                    uint cookerNetId = 0U;
                    object burnerEntity = this.TryGetManagedMemberValue(valueObj, "entity");
                    if (burnerEntity != null)
                    {
                        this.TryReadManagedNetIdMember(burnerEntity, "netId", out cookerNetId);
                        if (cookerNetId == 0U)
                        {
                            this.TryInvokeManagedNetIdMethod(burnerEntity, "GetNetId", out cookerNetId);
                        }
                    }

                    if (cookerNetId == 0U)
                    {
                        continue;
                    }

                    bool hasWorldPosition = this.TryGetNetCookTargetWorldPosition(levelObjectNetId, cookerNetId, out Vector3 worldPosition);
                    this.netCookRegisteredTargets[cookerNetId + ":" + levelObjectNetId] = new NetCookTargetContext
                    {
                        CookerNetId = cookerNetId,
                        CookerStaticId = staticId,
                        CookerType = cookerType,
                        LevelObjectNetId = levelObjectNetId,
                        HasWorldPosition = hasWorldPosition,
                        WorldPosition = worldPosition
                    };
                    added++;
                }

                if (added > 0)
                {
                    this.NetCookLog("Registered " + added + " cook-build burner target(s) for staticId=" + staticId + ".");
                }
            }
            catch (Exception ex)
            {
                this.NetCookLog("CookBuild burner registration failed: " + ex.Message);
            }
        }

        private void LogNetCookTargetSummary(List<NetCookTargetContext> targets)
        {
            if (!NetCookLogsEnabled || targets == null || targets.Count <= 0)
            {
                return;
            }

            Vector3 scanOrigin = Vector3.zero;
            bool hasOrigin = this.TryGetNetCookScanOrigin(out scanOrigin, out _);
            List<string> summary = new List<string>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                NetCookTargetContext target = targets[i];
                string distanceText = "?";
                if (hasOrigin && target.HasWorldPosition)
                {
                    distanceText = Vector3.Distance(scanOrigin, target.WorldPosition).ToString("F1");
                }

                summary.Add(target.CookerNetId + "/lo=" + target.LevelObjectNetId + "/static=" + target.CookerStaticId + "/d=" + distanceText);
            }

            this.NetCookLog("Target order: " + string.Join(", ", summary));
        }

        private void SortNetCookTargetsByDistanceFromScanOrigin(List<NetCookTargetContext> targets)
        {
            if (targets == null || targets.Count <= 1)
            {
                return;
            }

            if (!this.TryGetNetCookScanOrigin(out Vector3 scanOrigin, out _))
            {
                return;
            }

            targets.Sort((a, b) =>
            {
                float distanceA = a.HasWorldPosition ? Vector3.Distance(scanOrigin, a.WorldPosition) : float.MaxValue;
                float distanceB = b.HasWorldPosition ? Vector3.Distance(scanOrigin, b.WorldPosition) : float.MaxValue;
                return distanceA.CompareTo(distanceB);
            });
        }

        private int RemoveIncompatibleNetCookTargets(List<NetCookTargetContext> targets, HashSet<string> seenTargets, HashSet<uint> seenCookerNetIds, int desiredCookerStaticId, int desiredCookerType)
        {
            if (targets == null)
            {
                return 0;
            }

            int removed = 0;
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                NetCookTargetContext target = targets[i];
                if (this.IsCompatibleNetCookCooker(target.CookerStaticId, target.CookerType, desiredCookerStaticId, desiredCookerType))
                {
                    continue;
                }

                if (seenTargets != null)
                {
                    seenTargets.Remove(target.CookerNetId + ":" + target.LevelObjectNetId);
                }
                if (seenCookerNetIds != null)
                {
                    seenCookerNetIds.Remove(target.CookerNetId);
                }
                targets.RemoveAt(i);
                removed++;
            }

            return removed;
        }

        private int RemoveOutOfRangeNetCookTargets(List<NetCookTargetContext> targets, HashSet<string> seenTargets, HashSet<uint> seenCookerNetIds)
        {
            if (targets == null || targets.Count <= 0)
            {
                return 0;
            }

            if (!this.TryGetNetCookScanOrigin(out Vector3 scanOrigin, out _))
            {
                return 0;
            }

            float maxScanDistance = Mathf.Clamp(this.netCookScanRadiusMeters, NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters);
            int removed = 0;
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                NetCookTargetContext target = targets[i];
                if (target == null)
                {
                    targets.RemoveAt(i);
                    removed++;
                    continue;
                }

                if (!target.HasWorldPosition
                    && this.TryGetNetCookTargetWorldPosition(target.LevelObjectNetId, target.CookerNetId, out Vector3 resolvedPosition)
                    && resolvedPosition != Vector3.zero)
                {
                    target.HasWorldPosition = true;
                    target.WorldPosition = resolvedPosition;
                    targets[i] = target;
                }

                if (!target.HasWorldPosition || Vector3.Distance(scanOrigin, target.WorldPosition) > maxScanDistance)
                {
                    if (seenTargets != null)
                    {
                        seenTargets.Remove(target.CookerNetId + ":" + target.LevelObjectNetId);
                    }
                    if (seenCookerNetIds != null)
                    {
                        seenCookerNetIds.Remove(target.CookerNetId);
                    }

                    targets.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        private bool IsCompatibleNetCookCooker(int cookerStaticId, int cookerType, int desiredCookerStaticId, int desiredCookerType)
        {
            if (desiredCookerType > 0 && cookerType > 0)
            {
                return cookerType == desiredCookerType;
            }

            if (desiredCookerStaticId > 0 && cookerStaticId > 0)
            {
                return cookerStaticId == desiredCookerStaticId;
            }

            return true;
        }

        private bool IsSameNetCookCookerFamily(int firstStaticId, int firstCookerType, int secondStaticId, int secondCookerType)
        {
            if (firstCookerType > 0 && secondCookerType > 0)
            {
                return firstCookerType == secondCookerType;
            }

            if (firstStaticId > 0 && secondStaticId > 0)
            {
                return firstStaticId == secondStaticId;
            }

            return false;
        }

        private int GetPreferredNetCookTargetStaticId(List<NetCookTargetContext> targets)
        {
            if (targets == null || targets.Count <= 0)
            {
                return this.netCookCookerStaticId;
            }

            Dictionary<int, int> counts = new Dictionary<int, int>();
            int bestStaticId = this.netCookCookerStaticId;
            int bestCount = bestStaticId > 0 ? 0 : -1;
            for (int i = 0; i < targets.Count; i++)
            {
                int staticId = targets[i].CookerStaticId;
                if (staticId <= 0)
                {
                    continue;
                }

                counts.TryGetValue(staticId, out int count);
                count++;
                counts[staticId] = count;
                if (count > bestCount || (count == bestCount && staticId == this.netCookCookerStaticId))
                {
                    bestStaticId = staticId;
                    bestCount = count;
                }
            }

            return bestStaticId;
        }

        private int GetPreferredNetCookTargetCookerType(List<NetCookTargetContext> targets, int preferredStaticId)
        {
            if (targets == null)
            {
                return this.netCookCookerType;
            }

            Dictionary<int, int> counts = new Dictionary<int, int>();
            int bestCookerType = this.netCookCookerType;
            int bestCount = bestCookerType > 0 ? 0 : -1;
            for (int i = 0; i < targets.Count; i++)
            {
                int cookerType = targets[i].CookerType;
                if (cookerType <= 0)
                {
                    continue;
                }

                counts.TryGetValue(cookerType, out int count);
                count++;
                counts[cookerType] = count;
                if (count > bestCount || (count == bestCount && cookerType == this.netCookCookerType))
                {
                    bestCookerType = cookerType;
                    bestCount = count;
                }
            }

            if (bestCookerType > 0)
            {
                return bestCookerType;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].CookerStaticId == preferredStaticId && targets[i].CookerType > 0)
                {
                    return targets[i].CookerType;
                }
            }

            return this.netCookCookerType;
        }

        private bool TryAddNearbyCookBuildTargetsViaAuraMonoEntityScan(List<NetCookTargetContext> targets, HashSet<string> seenTargets, HashSet<uint> seenCookerNetIds, List<ulong> candidateLevelObjects, int desiredCookerStaticId, int desiredCookerType, out string status)
        {
            status = "AuraMono cook build entity scan unavailable.";
            if (targets == null || seenTargets == null || seenCookerNetIds == null)
            {
                status = "AuraMono cook build target buffer unavailable.";
                return false;
            }

            try
            {
                if (!this.TryGetNetCookScanOrigin(out Vector3 scanOrigin, out string originStatus))
                {
                    status = "AuraMono cook build scan origin unavailable: " + originStatus;
                    return false;
                }

                List<uint> cookBuildPins = new List<uint>();
                if (!this.TryEnumerateNetCookCookBuildComponentObjects(out List<IntPtr> cookBuildComponents, out string enumerateStatus, cookBuildPins))
                {
                    FreeAuraMonoPins(cookBuildPins);
                    status = "AuraMono cook build component scan unavailable: " + enumerateStatus;
                    return false;
                }

                int inspectedCookBuilds = 0;
                int skippedDifferentCooker = 0;
                int skippedDuplicateCooker = 0;
                int skippedOwnerLevelObjectId = 0;
                int directInspected = 0;
                int directAdded = 0;
                int synthesizedAdded = 0;
                int ownerWindowAdded = 0;
                int added = 0;
                List<string> debugSamples = NetCookScanDebugLogsEnabled ? new List<string>(NetCookScanDebugSampleLimit) : null;
                HashSet<uint> ownerSeedNetIds = new HashSet<uint>();
                try
                {
                for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                {
                    uint ownerNetId = ExtractNetCookOwnerNetId(targets[targetIndex].LevelObjectNetId);
                    if (ownerNetId != 0U)
                    {
                        ownerSeedNetIds.Add(ownerNetId);
                    }
                }
                if (candidateLevelObjects != null)
                {
                    for (int candidateIndex = 0; candidateIndex < candidateLevelObjects.Count; candidateIndex++)
                    {
                        ulong candidateLevelObjectNetId = candidateLevelObjects[candidateIndex];
                        if (candidateLevelObjectNetId <= uint.MaxValue)
                        {
                            continue;
                        }

                        uint ownerNetId = ExtractNetCookOwnerNetId(candidateLevelObjectNetId);
                        if (ownerNetId != 0U)
                        {
                            ownerSeedNetIds.Add(ownerNetId);
                        }
                    }
                }

                for (int i = 0; i < cookBuildComponents.Count; i++)
                {
                    if (targets.Count >= NetCookMaxCaptureTargets)
                    {
                        break;
                    }

                    IntPtr cookBuildComponentObj = cookBuildComponents[i];
                    if (cookBuildComponentObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    inspectedCookBuilds++;

                    // Owner entity (position + owner netId) is the component's back-reference.
                    IntPtr ownerEntityObj = IntPtr.Zero;
                    if (!this.TryGetMonoObjectMember(cookBuildComponentObj, "entity", out ownerEntityObj) || ownerEntityObj == IntPtr.Zero)
                    {
                        this.TryGetMonoObjectMember(cookBuildComponentObj, "_entity", out ownerEntityObj);
                    }

                    Vector3 ownerPosition = scanOrigin;
                    bool gotOwnerPosition = false;
                    if (ownerEntityObj != IntPtr.Zero)
                    {
                        gotOwnerPosition = this.TryGetAuraMonoEntityPosition(ownerEntityObj, out ownerPosition)
                            || this.TryExtractHomePositionMonoObject(ownerEntityObj, out ownerPosition);
                    }
                    if (!gotOwnerPosition && !this.TryExtractHomePositionMonoObject(cookBuildComponentObj, out ownerPosition))
                    {
                        ownerPosition = scanOrigin;
                    }

                    int cookerStaticId = 0;
                    this.TryGetMonoInt32Member(cookBuildComponentObj, "_cookerStaticId", out cookerStaticId);
                    if (cookerStaticId <= 0)
                    {
                        this.TryGetMonoInt32Member(cookBuildComponentObj, "cookerStaticId", out cookerStaticId);
                    }

                    int cookerType = 0;
                    if (cookerStaticId > 0 && cookerStaticId == desiredCookerStaticId && desiredCookerType > 0)
                    {
                        cookerType = desiredCookerType;
                    }
                    else if (cookerStaticId > 0)
                    {
                        this.TryGetCookerTypeForStaticId(cookerStaticId, out cookerType);
                    }
                    if (cookerType <= 0)
                    {
                        cookerType = desiredCookerType;
                    }

                    uint ownerCookBuildNetId = 0U;
                    this.TryGetAuraMonoEntityNetId(ownerEntityObj, out ownerCookBuildNetId);
                    if (ownerCookBuildNetId != 0U)
                    {
                        ownerSeedNetIds.Add(ownerCookBuildNetId);
                    }

                    if (!this.IsCompatibleNetCookCooker(cookerStaticId, cookerType, desiredCookerStaticId, desiredCookerType))
                    {
                        skippedDifferentCooker++;
                        AddNetCookScanDebugSample(debugSamples, "cookBuild owner=" + ownerCookBuildNetId + " rejected incompatible static=" + cookerStaticId + " type=" + cookerType + " desiredStatic=" + desiredCookerStaticId + " desiredType=" + desiredCookerType);
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(cookBuildComponentObj, "_cookBurnerMap", out IntPtr burnerMapObj) || burnerMapObj == IntPtr.Zero)
                    {
                        this.TryGetMonoObjectMember(cookBuildComponentObj, "cookBurnerMap", out burnerMapObj);
                    }

                    List<IntPtr> burnerEntries = new List<IntPtr>(8);
                    List<uint> burnerPins = new List<uint>();
                    if (burnerMapObj != IntPtr.Zero && this.TryEnumerateAuraMonoCollectionItems(burnerMapObj, burnerEntries, burnerPins) && burnerEntries.Count > 0)
                    {
                        try
                        {
                        for (int entryIndex = 0; entryIndex < burnerEntries.Count; entryIndex++)
                        {
                            if (targets.Count >= NetCookMaxCaptureTargets)
                            {
                                break;
                            }

                            IntPtr entryObj = burnerEntries[entryIndex];
                            if (entryObj == IntPtr.Zero)
                            {
                                continue;
                            }

                            ulong levelObjectNetId = 0UL;
                            if (!this.TryGetMonoUInt64Member(entryObj, "Key", out levelObjectNetId)
                                && !this.TryGetMonoUInt64Member(entryObj, "key", out levelObjectNetId)
                                && !this.TryGetMonoUInt64Member(entryObj, "_key", out levelObjectNetId))
                            {
                                AddNetCookScanDebugSample(debugSamples, "burnerMap owner=" + ownerCookBuildNetId + " entry=" + entryIndex + " rejected missing levelObject key");
                                continue;
                            }

                            IntPtr cookingComponentObj = IntPtr.Zero;
                            if ((!this.TryGetMonoObjectMember(entryObj, "Value", out cookingComponentObj) || cookingComponentObj == IntPtr.Zero)
                                && (!this.TryGetMonoObjectMember(entryObj, "value", out cookingComponentObj) || cookingComponentObj == IntPtr.Zero)
                                && (!this.TryGetMonoObjectMember(entryObj, "_value", out cookingComponentObj) || cookingComponentObj == IntPtr.Zero))
                            {
                                AddNetCookScanDebugSample(debugSamples, "burnerMap owner=" + ownerCookBuildNetId + " levelObject=" + levelObjectNetId + " rejected missing cooking component");
                                continue;
                            }

                            uint burnerCookerNetId;
                            if (!this.TryGetNetCookCookingComponentEntityNetId(cookingComponentObj, out burnerCookerNetId) || burnerCookerNetId == 0U)
                            {
                                AddNetCookScanDebugSample(debugSamples, "burnerMap owner=" + ownerCookBuildNetId + " levelObject=" + levelObjectNetId + " rejected missing burner entity netId");
                                continue;
                            }

                            if (levelObjectNetId <= uint.MaxValue)
                            {
                                skippedOwnerLevelObjectId++;
                                AddNetCookScanDebugSample(debugSamples, "burnerMap owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " rejected owner-levelObject id=" + levelObjectNetId);
                                continue;
                            }

                            int targetCookerType = 0;
                            if (!this.TryGetMonoIntMember(cookingComponentObj, "cookerwareType", out targetCookerType) || targetCookerType <= 0)
                            {
                                this.TryGetMonoIntMember(cookingComponentObj, "_cookerwareType", out targetCookerType);
                            }
                            if (targetCookerType <= 0)
                            {
                                targetCookerType = cookerType;
                            }

                            int targetStaticId = cookerStaticId;
                            if (targetStaticId <= 0)
                            {
                                AddNetCookScanDebugSample(debugSamples, "burnerMap owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected missing staticId");
                                continue;
                            }

                            if (targetCookerType <= 0 && !this.TryGetCookerTypeForStaticId(targetStaticId, out targetCookerType))
                            {
                                AddNetCookScanDebugSample(debugSamples, "burnerMap owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected missing cookerType static=" + targetStaticId);
                                continue;
                            }

                            if (!this.IsCompatibleNetCookCooker(targetStaticId, targetCookerType, desiredCookerStaticId, desiredCookerType))
                            {
                                skippedDifferentCooker++;
                                AddNetCookScanDebugSample(debugSamples, "burnerMap owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected incompatible static=" + targetStaticId + " type=" + targetCookerType);
                                continue;
                            }

                            if (!seenCookerNetIds.Add(burnerCookerNetId))
                            {
                                skippedDuplicateCooker++;
                                AddNetCookScanDebugSample(debugSamples, "burnerMap owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected duplicate burner");
                                continue;
                            }

                            string key = burnerCookerNetId + ":" + levelObjectNetId;
                            if (!seenTargets.Add(key))
                            {
                                seenCookerNetIds.Remove(burnerCookerNetId);
                                AddNetCookScanDebugSample(debugSamples, "burnerMap owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected duplicate target key");
                                continue;
                            }

                            targets.Add(new NetCookTargetContext
                            {
                                CookerNetId = burnerCookerNetId,
                                CookerStaticId = targetStaticId,
                                CookerType = targetCookerType,
                                LevelObjectNetId = levelObjectNetId,
                                HasWorldPosition = true,
                                WorldPosition = ownerPosition
                            });
                            AddNetCookScanDebugSample(debugSamples, "burnerMap accepted owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " static=" + targetStaticId + " type=" + targetCookerType + " dist=" + Vector3.Distance(scanOrigin, ownerPosition).ToString("F1"));
                            added++;
                        }
                        }
                        finally
                        {
                            FreeAuraMonoPins(burnerPins);
                        }
                    }
                    else
                    {
                        FreeAuraMonoPins(burnerPins);
                    }

                    int synthesizedForCookBuild = this.TryAddSynthesizedNetCookBurnerTargets(
                        ownerCookBuildNetId,
                        ownerPosition,
                        cookerStaticId,
                        cookerType,
                        desiredCookerStaticId,
                        desiredCookerType,
                        targets,
                        seenTargets,
                        seenCookerNetIds,
                        ref skippedDifferentCooker,
                        ref skippedDuplicateCooker,
                        debugSamples,
                        "entity");
                    if (synthesizedForCookBuild > 0)
                    {
                        synthesizedAdded += synthesizedForCookBuild;
                        added += synthesizedForCookBuild;
                    }
                }

                ownerWindowAdded = this.TryAddNearbyCookBuildTargetsByOwnerNetIdWindow(
                    targets,
                    seenTargets,
                    seenCookerNetIds,
                    ownerSeedNetIds,
                    scanOrigin,
                    desiredCookerStaticId,
                    desiredCookerType,
                    ref skippedDifferentCooker,
                    ref skippedDuplicateCooker,
                    debugSamples);
                added += ownerWindowAdded;

                List<uint> cookingPins = new List<uint>();
                if (this.TryEnumerateNetCookCookingComponentObjects(out List<IntPtr> cookingComponents, out _, cookingPins))
                {
                try
                {
                for (int i = 0; i < cookingComponents.Count; i++)
                {
                    if (targets.Count >= NetCookMaxCaptureTargets)
                    {
                        break;
                    }

                    IntPtr cookingComponentObj = cookingComponents[i];
                    if (cookingComponentObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    directInspected++;

                    // Burner entity (position) is the cooking component's back-reference.
                    IntPtr burnerEntityObj = IntPtr.Zero;
                    if (!this.TryGetMonoObjectMember(cookingComponentObj, "entity", out burnerEntityObj) || burnerEntityObj == IntPtr.Zero)
                    {
                        this.TryGetMonoObjectMember(cookingComponentObj, "_entity", out burnerEntityObj);
                    }

                    Vector3 burnerPosition = scanOrigin;
                    bool gotBurnerPosition = false;
                    if (burnerEntityObj != IntPtr.Zero)
                    {
                        gotBurnerPosition = this.TryGetAuraMonoEntityPosition(burnerEntityObj, out burnerPosition)
                            || this.TryExtractHomePositionMonoObject(burnerEntityObj, out burnerPosition);
                    }
                    if (!gotBurnerPosition && !this.TryExtractHomePositionMonoObject(cookingComponentObj, out burnerPosition))
                    {
                        burnerPosition = scanOrigin;
                    }

                    if (!this.TryGetNetCookCookingComponentEntityNetId(cookingComponentObj, out uint burnerCookerNetId) || burnerCookerNetId == 0U)
                    {
                        AddNetCookScanDebugSample(debugSamples, "direct burner rejected missing burner entity netId");
                        continue;
                    }

                    if (!this.TryGetNetCookCookingComponentDataLevelObjectNetId(cookingComponentObj, out ulong levelObjectNetId) || levelObjectNetId == 0UL)
                    {
                        AddNetCookScanDebugSample(debugSamples, "direct burner=" + burnerCookerNetId + " rejected missing levelObject id");
                        continue;
                    }
                    if (levelObjectNetId <= uint.MaxValue)
                    {
                        skippedOwnerLevelObjectId++;
                        AddNetCookScanDebugSample(debugSamples, "direct burner=" + burnerCookerNetId + " rejected owner-levelObject id=" + levelObjectNetId);
                        continue;
                    }

                    int targetCookerType = 0;
                    if (!this.TryGetMonoIntMember(cookingComponentObj, "cookerwareType", out targetCookerType) || targetCookerType <= 0)
                    {
                        this.TryGetMonoIntMember(cookingComponentObj, "_cookerwareType", out targetCookerType);
                    }

                    int targetStaticId = 0;
                    if (this.TryResolveNetCookParentCookerNetIdAuraMono(burnerCookerNetId, out uint parentCookerNetId, out _)
                        && parentCookerNetId != 0U
                        && this.TryGetAuraMonoEntityObjectByNetId(parentCookerNetId, out IntPtr parentEntityObj)
                        && parentEntityObj != IntPtr.Zero
                        && this.TryResolveNetCookBuildComponentAuraMono(parentEntityObj, out IntPtr parentCookBuildComponentObj, out _))
                    {
                        this.TryGetMonoInt32Member(parentCookBuildComponentObj, "_cookerStaticId", out targetStaticId);
                        if (targetStaticId <= 0)
                        {
                            this.TryGetMonoInt32Member(parentCookBuildComponentObj, "cookerStaticId", out targetStaticId);
                        }
                    }

                    if (targetStaticId <= 0)
                    {
                        targetStaticId = desiredCookerStaticId;
                    }

                    if (targetStaticId <= 0)
                    {
                        AddNetCookScanDebugSample(debugSamples, "direct burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected missing staticId");
                        continue;
                    }

                    if (targetCookerType <= 0 && !this.TryGetCookerTypeForStaticId(targetStaticId, out targetCookerType))
                    {
                        targetCookerType = desiredCookerType;
                    }

                    if (targetCookerType <= 0)
                    {
                        AddNetCookScanDebugSample(debugSamples, "direct burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected missing cookerType static=" + targetStaticId);
                        continue;
                    }

                    if (!this.IsCompatibleNetCookCooker(targetStaticId, targetCookerType, desiredCookerStaticId, desiredCookerType))
                    {
                        skippedDifferentCooker++;
                        AddNetCookScanDebugSample(debugSamples, "direct burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected incompatible static=" + targetStaticId + " type=" + targetCookerType);
                        continue;
                    }

                    if (!seenCookerNetIds.Add(burnerCookerNetId))
                    {
                        skippedDuplicateCooker++;
                        AddNetCookScanDebugSample(debugSamples, "direct burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected duplicate burner");
                        continue;
                    }

                    string key = burnerCookerNetId + ":" + levelObjectNetId;
                    if (!seenTargets.Add(key))
                    {
                        seenCookerNetIds.Remove(burnerCookerNetId);
                        AddNetCookScanDebugSample(debugSamples, "direct burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected duplicate target key");
                        continue;
                    }

                    targets.Add(new NetCookTargetContext
                    {
                        CookerNetId = burnerCookerNetId,
                        CookerStaticId = targetStaticId,
                        CookerType = targetCookerType,
                        LevelObjectNetId = levelObjectNetId,
                        HasWorldPosition = true,
                        WorldPosition = burnerPosition
                    });
                    AddNetCookScanDebugSample(debugSamples, "direct accepted burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " static=" + targetStaticId + " type=" + targetCookerType + " dist=" + Vector3.Distance(scanOrigin, burnerPosition).ToString("F1"));
                    directAdded++;
                    added++;
                }
                }
                finally
                {
                    FreeAuraMonoPins(cookingPins);
                }
                }

                status = "AuraMono cook build entity scan inspected=" + inspectedCookBuilds + " added=" + added + " synthesized=" + synthesizedAdded + " ownerWindowAdded=" + ownerWindowAdded + " directInspected=" + directInspected + " directAdded=" + directAdded + " skippedOwnerLevelObjectId=" + skippedOwnerLevelObjectId + " skippedDuplicateCooker=" + skippedDuplicateCooker + " skippedDifferentCooker=" + skippedDifferentCooker + " targetTotal=" + targets.Count + ".";
                this.NetCookLog(status);
                if (debugSamples != null)
                {
                    string ownerSeedRange = ownerSeedNetIds.Count > 0 ? (ownerSeedNetIds.Min() + "-" + ownerSeedNetIds.Max()) : "none";
                    this.NetCookLog("Scan debug ownerSeeds=" + ownerSeedNetIds.Count + " ownerSeedRange=" + ownerSeedRange + " desiredStatic=" + desiredCookerStaticId + " desiredType=" + desiredCookerType + " samples=" + debugSamples.Count + "/" + NetCookScanDebugSampleLimit + " [" + string.Join(" | ", debugSamples.ToArray()) + "]");
                }
                return added > 0;
                }
                finally
                {
                    FreeAuraMonoPins(cookBuildPins);
                }
            }
            catch (Exception ex)
            {
                status = "AuraMono cook build entity scan exception: " + ex.Message;
                this.NetCookLog(status);
                return false;
            }
        }

        private int TryAddNearbyCookBuildTargetsByOwnerNetIdWindow(
            List<NetCookTargetContext> targets,
            HashSet<string> seenTargets,
            HashSet<uint> seenCookerNetIds,
            HashSet<uint> ownerSeedNetIds,
            Vector3 scanOrigin,
            int desiredCookerStaticId,
            int desiredCookerType,
            ref int skippedDifferentCooker,
            ref int skippedDuplicateCooker,
            List<string> debugSamples = null,
            int ownerNetIdProbeWindow = NetCookOwnerNetIdProbeWindow)
        {
            if (targets == null || seenTargets == null || seenCookerNetIds == null || ownerSeedNetIds == null || ownerSeedNetIds.Count <= 0)
            {
                return 0;
            }

            int added = 0;
            HashSet<uint> inspectedOwnerNetIds = new HashSet<uint>();
            List<uint> seeds = ownerSeedNetIds.ToList();
            int ownerCandidatesWithEntity = 0;
            int ownerCandidatesWithCookBuild = 0;

            for (int seedIndex = 0; seedIndex < seeds.Count; seedIndex++)
            {
                if (targets.Count >= NetCookMaxCaptureTargets)
                {
                    break;
                }

                uint seedOwnerNetId = seeds[seedIndex];
                if (seedOwnerNetId == 0U)
                {
                    continue;
                }

                long start = Math.Max(1L, (long)seedOwnerNetId - ownerNetIdProbeWindow);
                long end = (long)seedOwnerNetId + ownerNetIdProbeWindow;
                for (long ownerCandidate = start; ownerCandidate <= end; ownerCandidate++)
                {
                    if (targets.Count >= NetCookMaxCaptureTargets)
                    {
                        break;
                    }

                    uint ownerCookBuildNetId = (uint)ownerCandidate;
                    if (!inspectedOwnerNetIds.Add(ownerCookBuildNetId))
                    {
                        continue;
                    }

                    if (!this.TryGetAuraMonoEntityObjectByNetId(ownerCookBuildNetId, out IntPtr ownerEntityObj) || ownerEntityObj == IntPtr.Zero)
                    {
                        continue;
                    }
                    ownerCandidatesWithEntity++;

                    if (!this.TryResolveNetCookBuildComponentAuraMono(ownerEntityObj, out IntPtr cookBuildComponentObj, out _))
                    {
                        continue;
                    }
                    ownerCandidatesWithCookBuild++;

                    Vector3 ownerPosition;
                    bool hasOwnerPosition = true;
                    if (!this.TryGetAuraMonoEntityPosition(ownerEntityObj, out ownerPosition)
                        && !this.TryExtractHomePositionMonoObject(ownerEntityObj, out ownerPosition)
                        && !this.TryExtractHomePositionMonoObject(cookBuildComponentObj, out ownerPosition))
                    {
                        hasOwnerPosition = false;
                        ownerPosition = scanOrigin;
                    }

                    int cookerStaticId = 0;
                    this.TryGetMonoInt32Member(cookBuildComponentObj, "_cookerStaticId", out cookerStaticId);
                    if (cookerStaticId <= 0)
                    {
                        this.TryGetMonoInt32Member(cookBuildComponentObj, "cookerStaticId", out cookerStaticId);
                    }

                    int cookerType = 0;
                    if (cookerStaticId > 0 && cookerStaticId == desiredCookerStaticId && desiredCookerType > 0)
                    {
                        cookerType = desiredCookerType;
                    }
                    else if (cookerStaticId > 0)
                    {
                        this.TryGetCookerTypeForStaticId(cookerStaticId, out cookerType);
                    }
                    if (cookerType <= 0)
                    {
                        cookerType = desiredCookerType;
                    }

                    if (!this.IsCompatibleNetCookCooker(cookerStaticId, cookerType, desiredCookerStaticId, desiredCookerType))
                    {
                        skippedDifferentCooker++;
                        AddNetCookScanDebugSample(debugSamples, "owner-window owner=" + ownerCookBuildNetId + " rejected incompatible static=" + cookerStaticId + " type=" + cookerType);
                        continue;
                    }

                    int addedForOwner = this.TryAddCookBuildBurnerMapTargetsAuraMono(
                        ownerCookBuildNetId,
                        ownerEntityObj,
                        cookBuildComponentObj,
                        targets,
                        seenTargets,
                        seenCookerNetIds,
                        ownerPosition,
                        desiredCookerStaticId,
                        desiredCookerType,
                        ref skippedDifferentCooker,
                        ref skippedDuplicateCooker);
                    if (addedForOwner <= 0)
                    {
                        addedForOwner = this.TryAddSynthesizedNetCookBurnerTargets(
                            ownerCookBuildNetId,
                            ownerPosition,
                            cookerStaticId,
                            cookerType,
                            desiredCookerStaticId,
                            desiredCookerType,
                            targets,
                            seenTargets,
                            seenCookerNetIds,
                            ref skippedDifferentCooker,
                            ref skippedDuplicateCooker,
                            debugSamples,
                            "owner-window-fallback");
                    }
                    if (addedForOwner <= 0)
                    {
                        AddNetCookScanDebugSample(debugSamples, "owner-window owner=" + ownerCookBuildNetId + " static=" + cookerStaticId + " type=" + cookerType + " produced no burners");
                    }
                    if (!hasOwnerPosition && addedForOwner > 0)
                    {
                        this.NetCookLog("Owner-window stove " + ownerCookBuildNetId + " accepted without reliable world position.");
                    }
                    added += addedForOwner;
                }
            }

            if (added > 0)
            {
                this.NetCookLog("Owner-window scan seeds=" + seeds.Count + " window=+/-" + ownerNetIdProbeWindow + " inspected=" + inspectedOwnerNetIds.Count + " entities=" + ownerCandidatesWithEntity + " cookBuilds=" + ownerCandidatesWithCookBuild + " added=" + added + ".");
            }
            else if (NetCookScanDebugLogsEnabled)
            {
                this.NetCookLog("Owner-window scan seeds=" + seeds.Count + " window=+/-" + ownerNetIdProbeWindow + " inspected=" + inspectedOwnerNetIds.Count + " entities=" + ownerCandidatesWithEntity + " cookBuilds=" + ownerCandidatesWithCookBuild + " added=" + added + ".");
            }
            return added;
        }

        private int TryAddSynthesizedNetCookBurnerTargets(uint ownerCookBuildNetId, Vector3 ownerPosition, int cookerStaticId, int cookerType, int desiredCookerStaticId, int desiredCookerType, List<NetCookTargetContext> targets, HashSet<string> seenTargets, HashSet<uint> seenCookerNetIds, ref int skippedDifferentCooker, ref int skippedDuplicateCooker, List<string> debugSamples = null, string source = "synth")
        {
            if (ownerCookBuildNetId == 0U || targets == null || seenTargets == null || seenCookerNetIds == null)
            {
                return 0;
            }

            int added = 0;
            const int maxLikelyCookBurnerScriptId = 16;
            for (int scriptId = 1; scriptId <= maxLikelyCookBurnerScriptId; scriptId++)
            {
                if (targets.Count >= NetCookMaxCaptureTargets)
                {
                    break;
                }

                ulong levelObjectNetId = ComposeNetCookLevelObjectId(ownerCookBuildNetId, scriptId);
                if (!this.TryResolveNetCookBurnerFromCookBuildAuraMono(ownerCookBuildNetId, levelObjectNetId, out uint burnerCookerNetId, out int targetStaticId, out int targetCookerType, out _)
                    || burnerCookerNetId == 0U)
                {
                    continue;
                }

                if (targetStaticId <= 0)
                {
                    targetStaticId = cookerStaticId;
                }

                if (targetStaticId <= 0)
                {
                    AddNetCookScanDebugSample(debugSamples, source + " owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected missing staticId");
                    continue;
                }

                if (targetCookerType <= 0)
                {
                    targetCookerType = cookerType;
                }
                if (targetCookerType <= 0)
                {
                    targetCookerType = desiredCookerType;
                }
                if (targetCookerType <= 0 && !this.TryGetCookerTypeForStaticId(targetStaticId, out targetCookerType))
                {
                    AddNetCookScanDebugSample(debugSamples, source + " owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected missing cookerType static=" + targetStaticId);
                    continue;
                }

                if (!this.IsCompatibleNetCookCooker(targetStaticId, targetCookerType, desiredCookerStaticId, desiredCookerType))
                {
                    skippedDifferentCooker++;
                    AddNetCookScanDebugSample(debugSamples, source + " owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected incompatible static=" + targetStaticId + " type=" + targetCookerType);
                    continue;
                }

                if (!seenCookerNetIds.Add(burnerCookerNetId))
                {
                    skippedDuplicateCooker++;
                    AddNetCookScanDebugSample(debugSamples, source + " owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected duplicate burner");
                    continue;
                }

                string key = burnerCookerNetId + ":" + levelObjectNetId;
                if (!seenTargets.Add(key))
                {
                    seenCookerNetIds.Remove(burnerCookerNetId);
                    AddNetCookScanDebugSample(debugSamples, source + " owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " rejected duplicate target key");
                    continue;
                }

                Vector3 targetWorldPosition = ownerPosition;
                bool hasWorldPosition = targetWorldPosition != Vector3.zero;
                if (this.TryGetNetCookTargetWorldPosition(levelObjectNetId, burnerCookerNetId, out Vector3 resolvedPosition)
                    && resolvedPosition != Vector3.zero)
                {
                    targetWorldPosition = resolvedPosition;
                    hasWorldPosition = true;
                }

                targets.Add(new NetCookTargetContext
                {
                    CookerNetId = burnerCookerNetId,
                    CookerStaticId = targetStaticId,
                    CookerType = targetCookerType,
                    LevelObjectNetId = levelObjectNetId,
                    HasWorldPosition = hasWorldPosition,
                    WorldPosition = targetWorldPosition
                });
                AddNetCookScanDebugSample(debugSamples, source + " accepted owner=" + ownerCookBuildNetId + " burner=" + burnerCookerNetId + " levelObject=" + levelObjectNetId + " static=" + targetStaticId + " type=" + targetCookerType);
                added++;
            }

            return added;
        }

        private static ulong ComposeNetCookLevelObjectId(uint ownerCookBuildNetId, int levelObjectScriptId)
        {
            return ((ulong)(uint)levelObjectScriptId << 32) | ownerCookBuildNetId;
        }

        private static uint ExtractNetCookOwnerNetId(ulong levelObjectNetId)
        {
            return (uint)(levelObjectNetId & 0xFFFFFFFFUL);
        }

        private bool TryGetNetCookCookingComponentEntityNetId(IntPtr cookingComponentObj, out uint burnerCookerNetId)
        {
            burnerCookerNetId = 0U;
            if (cookingComponentObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr burnerEntityObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(cookingComponentObj, "entity", out burnerEntityObj) || burnerEntityObj == IntPtr.Zero)
                && !this.TryInvokeAuraMonoZeroArg(cookingComponentObj, out burnerEntityObj, "get_entity", "GetEntity"))
            {
                return false;
            }

            return this.TryGetAuraMonoEntityNetId(burnerEntityObj, out burnerCookerNetId) && burnerCookerNetId != 0U;
        }

        private bool TryGetNetCookTargetWorldPosition(ulong levelObjectNetId, uint cookerNetId, out Vector3 position)
        {
            position = Vector3.zero;

            if (levelObjectNetId != 0UL
                && this.netCookAuraMonoLevelObjectPtrs.TryGetValue(levelObjectNetId, out long levelObjectPtr)
                && levelObjectPtr != 0L
                && this.TryExtractHomePositionMonoObject(new IntPtr(levelObjectPtr), out position)
                && position != Vector3.zero)
            {
                return true;
            }

            if (cookerNetId != 0U
                && this.TryGetAuraMonoEntityObjectByNetId(cookerNetId, out IntPtr cookerEntityObj)
                && cookerEntityObj != IntPtr.Zero
                && this.TryGetAuraMonoEntityPosition(cookerEntityObj, out position)
                && position != Vector3.zero)
            {
                return true;
            }

            return false;
        }

        private bool TryGetNetCookCookingComponentDataLevelObjectNetId(IntPtr cookingComponentObj, out ulong levelObjectNetId)
        {
            levelObjectNetId = 0UL;
            if (cookingComponentObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr componentDataObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(cookingComponentObj, "ComponentData", out componentDataObj) || componentDataObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(cookingComponentObj, "_componentData", out componentDataObj) || componentDataObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(cookingComponentObj, "componentData", out componentDataObj) || componentDataObj == IntPtr.Zero))
            {
                return false;
            }

            return this.TryGetMonoUInt64Member(componentDataObj, "levelObjectNetId", out levelObjectNetId)
                || this.TryGetMonoUInt64Member(componentDataObj, "LevelObjectNetId", out levelObjectNetId)
                || this.TryGetMonoUInt64Member(componentDataObj, "_levelObjectNetId", out levelObjectNetId);
        }

        private bool TryResolveNetCookContextFromCurrentTarget(out uint cookerNetId, out int cookerStaticId, out int cookerType, out ulong levelObjectNetId, out string status)
        {
            cookerNetId = 0U;
            cookerStaticId = 0;
            cookerType = 0;
            levelObjectNetId = 0UL;
            status = "No cooker target found.";
            this.NetCookLog("Resolving cooker from current target...");

            List<ulong> candidateLevelObjects = new List<ulong>(8);
            if (this.TryGetCurrentFocusedLevelObjectNetId(out ulong focusedLevelObjectNetId, out string focusStatus) && focusedLevelObjectNetId != 0UL)
            {
                candidateLevelObjects.Add(focusedLevelObjectNetId);
                this.NetCookLog("Focused level object=" + focusedLevelObjectNetId);
            }
            else
            {
                this.NetCookLog("Focused target unavailable: " + focusStatus);
            }

            if (this.TryGetCurrentInteractTargetLevelObjects(candidateLevelObjects, out string interactStatus) && candidateLevelObjects.Count > 0)
            {
                status = interactStatus;
                this.NetCookLog("Interact targets added. " + interactStatus);
            }
            else
            {
                this.NetCookLog("Interact target lookup: " + interactStatus);
            }

            if (this.TryGetCurrentInteractTargetLevelObjectsViaAuraMono(candidateLevelObjects, out string auraMonoInteractStatus) && candidateLevelObjects.Count > 0)
            {
                status = auraMonoInteractStatus;
                this.NetCookLog("AuraMono interact targets added. " + auraMonoInteractStatus);
            }
            else
            {
                this.NetCookLog("AuraMono interact lookup: " + auraMonoInteractStatus);
            }

            if (candidateLevelObjects.Count <= 0)
            {
                status = "No focused cooker target found.";
                this.NetCookLog("No candidate level objects found.");
                return false;
            }

            if (NetCookLogsEnabled)
            {
                this.NetCookLog("Candidate level objects: " + string.Join(", ", candidateLevelObjects));
            }

            for (int i = 0; i < candidateLevelObjects.Count; i++)
            {
                ulong candidateLevelObjectNetId = candidateLevelObjects[i];
                this.NetCookLog("Checking candidate level object " + candidateLevelObjectNetId + "...");
                if (!this.TryResolveNetCookContextFromLevelObject(candidateLevelObjectNetId, out cookerNetId, out cookerStaticId, out cookerType, out status))
                {
                    string managedStatus = status;
                    this.NetCookLog("Managed resolve rejected level object " + candidateLevelObjectNetId + ": " + managedStatus);
                    if (!this.TryResolveNetCookContextFromLevelObjectAuraMono(candidateLevelObjectNetId, out cookerNetId, out cookerStaticId, out cookerType, out status))
                    {
                        this.NetCookLog("Rejected level object " + candidateLevelObjectNetId + ": " + status);
                        continue;
                    }

                    this.NetCookLog("AuraMono resolve accepted level object " + candidateLevelObjectNetId + " after managed failure: " + managedStatus);
                }

                levelObjectNetId = candidateLevelObjectNetId;
                status = "Captured cooker " + cookerStaticId + " from current target.";
                this.NetCookLog("Accepted candidate level object " + candidateLevelObjectNetId + ".");
                return true;
            }

            this.NetCookLog("All candidate level objects rejected.");
            return false;
        }

        private bool TryGetNearbyCookerLevelObjectsViaWorldScan(List<ulong> candidateLevelObjects, out string status, HashSet<ulong> candidateLevelObjectSet = null)
        {
            status = "World scan unavailable.";
            if (candidateLevelObjects == null)
            {
                return false;
            }

            try
            {
                if (!this.TryGetNetCookScanOrigin(out Vector3 scanOrigin, out string originStatus))
                {
                    status = "World scan origin unavailable: " + originStatus;
                    this.NetCookLog(status);
                    return false;
                }

                Type levelObjectManagerType = this.FindLevelObjectManagerRuntimeType();
                if (levelObjectManagerType == null)
                {
                    this.NetCookLog("World scan managed LevelObjectManager unavailable. Trying AuraMono fallback...");
                    return this.TryGetNearbyCookerLevelObjectsViaAuraMonoWorldScan(candidateLevelObjects, out status, candidateLevelObjectSet);
                }

                PropertyInfo instanceProperty = levelObjectManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object levelObjectManager = instanceProperty != null ? instanceProperty.GetValue(null, null) : null;
                if (levelObjectManager == null)
                {
                    this.NetCookLog("World scan managed LevelObjectManager instance unavailable. Trying AuraMono fallback...");
                    return this.TryGetNearbyCookerLevelObjectsViaAuraMonoWorldScan(candidateLevelObjects, out status, candidateLevelObjectSet);
                }

                object dictionaryObj = this.TryGetManagedMemberValue(levelObjectManager, "_dictionary")
                    ?? this.TryGetManagedMemberValue(levelObjectManager, "dictionary");
                if (!(dictionaryObj is IEnumerable enumerable))
                {
                    this.NetCookLog("World scan managed dictionary unavailable. Trying AuraMono fallback...");
                    return this.TryGetNearbyCookerLevelObjectsViaAuraMonoWorldScan(candidateLevelObjects, out status, candidateLevelObjectSet);
                }

                List<KeyValuePair<ulong, float>> nearbyCandidates = new List<KeyValuePair<ulong, float>>();
                float maxScanDistance = Mathf.Clamp(this.netCookScanRadiusMeters, NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters);

                // Snapshot the dictionary entries before iterating — entities can be added/removed
                // during capture (stove spawn/despawn), which would throw InvalidOperationException
                // or cause a native crash via the IL2CPP collection enumerator.
                List<object> levelObjectEntries;
                try { levelObjectEntries = enumerable.Cast<object>().ToList(); }
                catch { levelObjectEntries = new List<object>(); }

                for (int _loIdx = 0; _loIdx < levelObjectEntries.Count; _loIdx++)
                {
                    object entry = levelObjectEntries[_loIdx];
                    if (entry == null)
                    {
                        continue;
                    }

                    object levelObject;
                    try { levelObject = this.TryGetManagedMemberValue(entry, "Value") ?? entry; }
                    catch { continue; }
                    if (levelObject == null)
                    {
                        continue;
                    }

                    ulong levelObjectNetId = 0UL;
                    if (!this.TryReadManagedUInt64Member(levelObject, "netId", out levelObjectNetId) || levelObjectNetId == 0UL)
                    {
                        if (!this.TryReadManagedUInt64Member(entry, "Key", out levelObjectNetId) || levelObjectNetId == 0UL)
                        {
                            continue;
                        }
                    }

                    if (candidateLevelObjectSet != null ? candidateLevelObjectSet.Contains(levelObjectNetId) : candidateLevelObjects.Contains(levelObjectNetId))
                    {
                        continue;
                    }

                    if (this.TryReadManagedBoolMember(levelObject, "isActive", out bool isActive) && !isActive)
                    {
                        continue;
                    }

                    if (!this.TryGetNetCookLevelObjectPosition(levelObject, out Vector3 levelObjectPosition))
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(scanOrigin, levelObjectPosition);
                    if (distance > maxScanDistance)
                    {
                        continue;
                    }

                    nearbyCandidates.Add(new KeyValuePair<ulong, float>(levelObjectNetId, distance));
                }

                if (nearbyCandidates.Count <= 0)
                {
                    status = "World scan found no nearby level objects.";
                    this.NetCookLog(status);
                    return false;
                }

                nearbyCandidates.Sort((a, b) => a.Value.CompareTo(b.Value));

                int added = 0;
                for (int i = 0; i < nearbyCandidates.Count; i++)
                {
                    ulong levelObjectNetId = nearbyCandidates[i].Key;
                    if (!AddNetCookCandidateLevelObject(candidateLevelObjects, candidateLevelObjectSet, levelObjectNetId))
                    {
                        continue;
                    }

                    added++;
                }

                status = "World scan added " + added + " nearby level objects within " + maxScanDistance.ToString("F0") + "m.";
                if (NetCookLogsEnabled)
                {
                    this.NetCookLog("World scan origin=" + scanOrigin + " radius=" + maxScanDistance.ToString("F0") + "m candidates=" + nearbyCandidates.Count + " added=" + added + " nearest=[" + string.Join(", ", nearbyCandidates.Take(Math.Min(16, nearbyCandidates.Count)).Select(kv => kv.Key + "@" + kv.Value.ToString("F2")).ToArray()) + "]");
                }
                return added > 0;
            }
            catch (Exception ex)
            {
                this.NetCookLog("World scan managed exception: " + ex.Message + ". Trying AuraMono fallback...");
                return this.TryGetNearbyCookerLevelObjectsViaAuraMonoWorldScan(candidateLevelObjects, out status, candidateLevelObjectSet);
            }
        }

        private bool TryGetNearbyCookerLevelObjectsViaAuraMonoWorldScan(List<ulong> candidateLevelObjects, out string status, HashSet<ulong> candidateLevelObjectSet = null)
        {
            status = "AuraMono world scan unavailable.";
            if (candidateLevelObjects == null)
            {
                return false;
            }

            try
            {
                if (!this.TryGetNetCookScanOrigin(out Vector3 scanOrigin, out string originStatus))
                {
                    status = "AuraMono world scan origin unavailable: " + originStatus;
                    this.NetCookLog(status);
                    return false;
                }

                if (!this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out _, out string managerStatus))
                {
                    status = "AuraMono world scan failed: " + managerStatus;
                    this.NetCookLog(status);
                    return false;
                }

                IntPtr dictionaryObj = IntPtr.Zero;
                if ((!this.TryGetMonoObjectMember(managerObj, "_dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(managerObj, "dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero))
                {
                    status = "AuraMono world scan failed: level object dictionary unavailable.";
                    this.NetCookLog(status);
                    return false;
                }

                List<IntPtr> entries = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(dictionaryObj, entries) || entries.Count <= 0)
                {
                    status = "AuraMono world scan found no dictionary entries.";
                    this.NetCookLog(status);
                    return false;
                }

                this.netCookAuraMonoLevelObjectPtrs.Clear();
                List<KeyValuePair<ulong, float>> nearbyCandidates = new List<KeyValuePair<ulong, float>>();
                float maxScanDistance = Mathf.Clamp(this.netCookScanRadiusMeters, NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters);

                for (int i = 0; i < entries.Count; i++)
                {
                    IntPtr entryObj = entries[i];
                    if (entryObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr levelObjectObj = IntPtr.Zero;
                    if ((!this.TryGetMonoObjectMember(entryObj, "Value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(entryObj, "value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(entryObj, "_value", out levelObjectObj) || levelObjectObj == IntPtr.Zero))
                    {
                        levelObjectObj = entryObj;
                    }

                    if (levelObjectObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    ulong levelObjectNetId = 0UL;
                    if (!this.TryGetMonoUInt64Member(levelObjectObj, "netId", out levelObjectNetId) || levelObjectNetId == 0UL)
                    {
                        if (!this.TryGetMonoUInt64Member(entryObj, "Key", out levelObjectNetId)
                            && !this.TryGetMonoUInt64Member(entryObj, "key", out levelObjectNetId)
                            && !this.TryGetMonoUInt64Member(entryObj, "_key", out levelObjectNetId))
                        {
                            continue;
                        }
                    }

                    if (levelObjectNetId == 0UL || (candidateLevelObjectSet != null ? candidateLevelObjectSet.Contains(levelObjectNetId) : candidateLevelObjects.Contains(levelObjectNetId)))
                    {
                        continue;
                    }

                    if (this.TryGetMonoBoolMember(levelObjectObj, "isActive", out bool isActive) && !isActive)
                    {
                        continue;
                    }

                    if (!this.TryExtractHomePositionMonoObject(levelObjectObj, out Vector3 levelObjectPosition))
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(scanOrigin, levelObjectPosition);
                    if (distance > maxScanDistance)
                    {
                        continue;
                    }

                    this.netCookAuraMonoLevelObjectPtrs[levelObjectNetId] = levelObjectObj.ToInt64();
                    nearbyCandidates.Add(new KeyValuePair<ulong, float>(levelObjectNetId, distance));
                }

                if (nearbyCandidates.Count <= 0)
                {
                    status = "AuraMono world scan found no nearby level objects.";
                    this.NetCookLog(status);
                    return false;
                }

                nearbyCandidates.Sort((a, b) => a.Value.CompareTo(b.Value));

                int added = 0;
                for (int i = 0; i < nearbyCandidates.Count; i++)
                {
                    ulong levelObjectNetId = nearbyCandidates[i].Key;
                    if (!AddNetCookCandidateLevelObject(candidateLevelObjects, candidateLevelObjectSet, levelObjectNetId))
                    {
                        continue;
                    }

                    added++;
                }

                status = "AuraMono world scan added " + added + " nearby level objects within " + maxScanDistance.ToString("F0") + "m.";
                if (NetCookLogsEnabled)
                {
                    this.NetCookLog("AuraMono world scan origin=" + scanOrigin + " radius=" + maxScanDistance.ToString("F0") + "m candidates=" + nearbyCandidates.Count + " added=" + added + " nearest=[" + string.Join(", ", nearbyCandidates.Take(Math.Min(16, nearbyCandidates.Count)).Select(kv => kv.Key + "@" + kv.Value.ToString("F2")).ToArray()) + "]");
                }
                return added > 0;
            }
            catch (Exception ex)
            {
                status = "AuraMono world scan exception: " + ex.Message;
                this.NetCookLog(status);
                return false;
            }
        }

        private bool TryResolveNetCookContextFromLevelObjectAuraMono(ulong levelObjectNetId, out uint cookerNetId, out int cookerStaticId, out int cookerType, out string status)
        {
            cookerNetId = 0U;
            cookerStaticId = 0;
            cookerType = 0;
            status = "AuraMono level object is not a cooker.";
            this.NetCookLog("AuraMono resolving level object " + levelObjectNetId + "...");

            try
            {
                this.ResolveAuraFarmRuntimeMethods();
                if (!this.TryResolveOwnerIdFromLevelObjectIdMono(levelObjectNetId, out cookerNetId) || cookerNetId == 0U)
                {
                    status = "AuraMono cooker owner net id missing.";
                    this.NetCookLog(status);
                    return false;
                }

                uint ownerCookBuildNetId = cookerNetId;
                cookerNetId = 0U;
                this.NetCookLog("AuraMono level object " + levelObjectNetId + " ownerNetId=" + ownerCookBuildNetId);

                if (this.TryResolveNetCookBurnerFromCookBuildAuraMono(ownerCookBuildNetId, levelObjectNetId, out cookerNetId, out cookerStaticId, out int auraCookerType, out string cookBuildStatus))
                {
                    this.NetCookLog("AuraMono cook-build burner netId=" + cookerNetId);
                    if (cookerStaticId > 0)
                    {
                        this.NetCookLog("AuraMono cook-build staticId=" + cookerStaticId);
                    }
                    if (auraCookerType > 0)
                    {
                        cookerType = auraCookerType;
                        this.NetCookLog("AuraMono cook-build cookerType=" + cookerType);
                    }
                }
                else
                {
                    this.NetCookLog("AuraMono cook-build burner lookup failed. " + cookBuildStatus);
                }

                if (cookerNetId != 0U && this.TryResolveNetCookParentCookerNetIdAuraMono(cookerNetId, out uint parentCookerNetId, out string parentStatus))
                {
                    this.NetCookLog("AuraMono burner parentCookerNetId=" + parentCookerNetId);
                }
                else if (cookerNetId != 0U)
                {
                    this.NetCookLog("AuraMono burner parent lookup failed.");
                }

                if (cookerStaticId <= 0 && this.TryResolveNetCookWorldCookerDataManaged(ownerCookBuildNetId, out cookerStaticId, out string managedStatus))
                {
                    this.NetCookLog(managedStatus);
                }
                else if (cookerStaticId <= 0)
                {
                    this.NetCookLog("Managed cooker data lookup failed.");
                }

                IntPtr ownerEntityObj = IntPtr.Zero;
                if (cookerStaticId <= 0)
                {
                    if (!this.TryGetAuraMonoEntityObjectByNetId(ownerCookBuildNetId, out ownerEntityObj) || ownerEntityObj == IntPtr.Zero)
                    {
                        status = "AuraMono cooker owner entity missing.";
                        this.NetCookLog(status);
                        return false;
                    }
                }

                if (cookerStaticId <= 0)
                {
                    if (!this.TryResolveNetCookWorldCookerComponentAuraMono(ownerEntityObj, out IntPtr worldCookerComponentObj, out string componentStatus))
                    {
                        status = componentStatus;
                        this.NetCookLog(status);
                        return false;
                    }

                    string transformTypeName = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass != null ? auraMonoObjectGetClass(worldCookerComponentObj) : IntPtr.Zero);
                    this.NetCookLog("AuraMono transform component type=" + transformTypeName);

                    IntPtr componentDataObj = IntPtr.Zero;
                    if ((!this.TryGetMonoObjectMember(worldCookerComponentObj, "ComponentData", out componentDataObj) || componentDataObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(worldCookerComponentObj, "_componentData", out componentDataObj) || componentDataObj == IntPtr.Zero))
                    {
                        status = "AuraMono cooker component data missing.";
                        this.NetCookLog(status);
                        return false;
                    }

                    if (!this.TryGetMonoInt32Member(componentDataObj, "staticId", out cookerStaticId) || cookerStaticId <= 0)
                    {
                        status = "AuraMono cooker static id missing.";
                        this.NetCookLog(status);
                        return false;
                    }

                    this.NetCookLog("AuraMono cooker staticId=" + cookerStaticId);
                }

                if (cookerNetId == 0U)
                {
                    status = "AuraMono burner cooker net id missing.";
                    this.NetCookLog(status);
                    return false;
                }

                if (cookerType <= 0 && !this.TryGetCookerTypeForStaticId(cookerStaticId, out cookerType))
                {
                    status = "AuraMono cooker type lookup failed.";
                    this.NetCookLog(status);
                    return false;
                }

                status = "AuraMono cooker context ready.";
                this.NetCookLog("AuraMono cooker type=" + cookerType);
                return true;
            }
            catch (Exception ex)
            {
                status = "AuraMono cooker resolve exception: " + ex.Message;
                this.NetCookLog(status);
                return false;
            }
        }

        private bool TryResolveNetCookBurnerFromCookBuildAuraMono(uint ownerCookBuildNetId, ulong levelObjectNetId, out uint burnerCookerNetId, out int cookerStaticId, out int cookerType, out string status)
        {
            burnerCookerNetId = 0U;
            cookerStaticId = 0;
            cookerType = 0;
            status = "AuraMono cook build lookup unavailable.";

            try
            {
                if (ownerCookBuildNetId == 0U)
                {
                    status = "AuraMono cook build owner net id missing.";
                    return false;
                }

                if (!this.TryGetAuraMonoEntityObjectByNetId(ownerCookBuildNetId, out IntPtr ownerEntityObj) || ownerEntityObj == IntPtr.Zero)
                {
                    status = "AuraMono cook build entity missing.";
                    return false;
                }

                if (!this.TryResolveNetCookBuildComponentAuraMono(ownerEntityObj, out IntPtr cookBuildComponentObj, out string componentStatus))
                {
                    status = componentStatus;
                    return false;
                }

                this.TryGetMonoInt32Member(cookBuildComponentObj, "_cookerStaticId", out cookerStaticId);
                if (cookerStaticId <= 0)
                {
                    this.TryGetMonoInt32Member(cookBuildComponentObj, "cookerStaticId", out cookerStaticId);
                }

                if (!this.TryInvokeAuraMonoUInt64Arg(cookBuildComponentObj, levelObjectNetId, out IntPtr cookingComponentObj, "GetCooingComponent", "GetCookingComponent") || cookingComponentObj == IntPtr.Zero)
                {
                    status = "AuraMono GetCooingComponent unavailable for level object " + levelObjectNetId + ".";
                    return false;
                }

                this.TryGetMonoIntMember(cookingComponentObj, "cookerwareType", out cookerType);
                if (cookerType <= 0)
                {
                    this.TryGetMonoIntMember(cookingComponentObj, "_cookerwareType", out cookerType);
                }

                IntPtr burnerEntityObj = IntPtr.Zero;
                if ((!this.TryGetMonoObjectMember(cookingComponentObj, "entity", out burnerEntityObj) || burnerEntityObj == IntPtr.Zero)
                    && !this.TryInvokeAuraMonoZeroArg(cookingComponentObj, out burnerEntityObj, "get_entity", "GetEntity"))
                {
                    status = "AuraMono cooking component entity unavailable.";
                    return false;
                }

                if (!this.TryGetAuraMonoEntityNetId(burnerEntityObj, out burnerCookerNetId) || burnerCookerNetId == 0U)
                {
                    status = "AuraMono burner cooker net id unavailable.";
                    return false;
                }

                status = "AuraMono cook build burner ready.";
                return true;
            }
            catch (Exception ex)
            {
                status = "AuraMono cook build burner exception: " + ex.Message;
                return false;
            }
        }

        private bool TryResolveNetCookParentCookerNetIdAuraMono(uint burnerCookerNetId, out uint parentCookerNetId, out string status)
        {
            parentCookerNetId = 0U;
            status = "AuraMono parent cooker unavailable.";

            try
            {
                if (burnerCookerNetId == 0U)
                {
                    status = "AuraMono burner cooker net id missing.";
                    return false;
                }

                if (!this.TryGetAuraMonoEntityObjectByNetId(burnerCookerNetId, out IntPtr burnerEntityObj) || burnerEntityObj == IntPtr.Zero)
                {
                    status = "AuraMono burner entity missing.";
                    return false;
                }

                if (!this.TryResolveNetCookCookingComponentAuraMono(burnerEntityObj, out IntPtr cookingComponentObj, out string componentStatus))
                {
                    status = componentStatus;
                    return false;
                }

                if (!this.TryInvokeAuraMonoZeroArg(cookingComponentObj, out IntPtr parentBoxedObj, "GetParentNetId", "get_ParentNetId") || parentBoxedObj == IntPtr.Zero)
                {
                    status = "AuraMono GetParentNetId unavailable.";
                    return false;
                }

                if (!this.TryUnboxMonoUInt32(parentBoxedObj, out parentCookerNetId) || parentCookerNetId == 0U)
                {
                    ulong parentAsUlong = this.TryReadMonoUnsignedIntegral(parentBoxedObj);
                    parentCookerNetId = (uint)parentAsUlong;
                }

                if (parentCookerNetId == 0U)
                {
                    status = "AuraMono parent cooker net id invalid.";
                    return false;
                }

                status = "AuraMono parent cooker net id ready.";
                return true;
            }
            catch (Exception ex)
            {
                status = "AuraMono parent cooker exception: " + ex.Message;
                return false;
            }
        }

        private bool TryResolveNetCookCookingComponentAuraMono(IntPtr entityObj, out IntPtr componentObj, out string status)
        {
            componentObj = IntPtr.Zero;
            status = "AuraMono cooking component missing.";
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr componentsObj, "GetAllComponents") || componentsObj == IntPtr.Zero)
            {
                status = "AuraMono burner GetAllComponents unavailable.";
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components) || components.Count <= 0)
            {
                status = "AuraMono burner has no components.";
                return false;
            }

            for (int i = 0; i < components.Count && i < 128; i++)
            {
                IntPtr candidate = components[i];
                if (candidate == IntPtr.Zero)
                {
                    continue;
                }

                string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(candidate));
                if (string.IsNullOrEmpty(className))
                {
                    continue;
                }

                if (className.IndexOf("CookingComponent", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    componentObj = candidate;
                    status = "AuraMono cooking component ready.";
                    return true;
                }
            }

            status = "AuraMono current target is not a cooking component.";
            return false;
        }

        private bool TryResolveNetCookBuildComponentAuraMono(IntPtr entityObj, out IntPtr componentObj, out string status)
        {
            componentObj = IntPtr.Zero;
            status = "AuraMono cook build component missing.";
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr componentsObj, "GetAllComponents") || componentsObj == IntPtr.Zero)
            {
                status = "AuraMono cook build GetAllComponents unavailable.";
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components) || components.Count <= 0)
            {
                status = "AuraMono cook build has no components.";
                return false;
            }

            for (int i = 0; i < components.Count && i < 128; i++)
            {
                IntPtr candidate = components[i];
                if (candidate == IntPtr.Zero)
                {
                    continue;
                }

                string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(candidate));
                if (string.IsNullOrEmpty(className))
                {
                    continue;
                }

                if (className.IndexOf("CookBuildComponent", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    componentObj = candidate;
                    status = "AuraMono cook build component ready.";
                    return true;
                }
            }

            status = "AuraMono owner is not a cook build.";
            return false;
        }

        private bool TryResolveNetCookWorldCookerDataManaged(uint cookerNetId, out int cookerStaticId, out string status)
        {
            cookerStaticId = 0;
            status = "Managed world cooker data unavailable.";

            try
            {
                Type dataCenterType = this.FindLoadedType(
                    "XDTDataAndProtocol.ComponentsData.DataCenter",
                    "ScriptsRefactory.DataAndProtocol.ComponentsData.DataCenter",
                    "DataCenter");
                Type worldCookerDataType = this.FindLoadedType(
                    "XDTDataAndProtocol.ComponentsData.WorldCookerComponentData",
                    "ScriptsRefactory.DataAndProtocol.ComponentsData.WorldCookerComponentData",
                    "WorldCookerComponentData");
                Type netIdType = this.FindLoadedType(
                    "EcsClient.XDT.Scene.Shared.Data.SharedData.NetId",
                    "XDT.Scene.Shared.Data.SharedData.NetId",
                    "NetId");

                if (dataCenterType == null || worldCookerDataType == null || netIdType == null)
                {
                    status = "Managed cooker data types unavailable. DataCenter=" + (dataCenterType != null) + " WorldCookerComponentData=" + (worldCookerDataType != null) + " NetId=" + (netIdType != null);
                    return false;
                }

                MethodInfo tryGetComponentMethod = null;
                foreach (MethodInfo method in dataCenterType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method == null || method.Name != "TryGetComponentData" || !method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 2
                        && parameters[0].ParameterType == netIdType
                        && parameters[1].ParameterType.IsByRef)
                    {
                        tryGetComponentMethod = method.MakeGenericMethod(worldCookerDataType);
                        break;
                    }
                }

                if (tryGetComponentMethod == null)
                {
                    status = "Managed TryGetComponentData<WorldCookerComponentData> unavailable.";
                    return false;
                }

                object netIdArg = this.CreateNetCookNetIdArgument(netIdType, cookerNetId);
                if (netIdArg == null)
                {
                    status = "Managed cooker NetId argument creation failed.";
                    return false;
                }

                object componentDataBox = Activator.CreateInstance(worldCookerDataType);
                object[] args = new object[] { netIdArg, componentDataBox };
                object invokeResult = tryGetComponentMethod.Invoke(null, args);
                bool found = invokeResult is bool foundFlag && foundFlag;
                if (!found)
                {
                    status = "Managed WorldCookerComponentData missing for netId " + cookerNetId + ".";
                    return false;
                }

                object componentData = args[1] ?? componentDataBox;
                if (componentData == null)
                {
                    status = "Managed WorldCookerComponentData result missing.";
                    return false;
                }

                FieldInfo staticIdField = worldCookerDataType.GetField("staticId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (staticIdField == null)
                {
                    status = "Managed WorldCookerComponentData.staticId missing.";
                    return false;
                }

                object staticIdValue = staticIdField.GetValue(componentData);
                if (staticIdValue == null)
                {
                    status = "Managed cooker staticId value missing.";
                    return false;
                }

                cookerStaticId = Convert.ToInt32(staticIdValue);
                if (cookerStaticId <= 0)
                {
                    status = "Managed cooker staticId invalid.";
                    return false;
                }

                status = "Managed cooker staticId=" + cookerStaticId;
                return true;
            }
            catch (Exception ex)
            {
                status = "Managed cooker data exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private object CreateNetCookNetIdArgument(Type netIdType, uint netId)
        {
            if (netIdType == null)
            {
                return null;
            }

            try
            {
                foreach (MethodInfo method in netIdType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method == null || method.Name != "op_Implicit" || method.ReturnType != netIdType)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(uint))
                    {
                        return method.Invoke(null, new object[] { netId });
                    }
                }

                object boxed = Activator.CreateInstance(netIdType);
                if (boxed == null)
                {
                    return null;
                }

                FieldInfo valueField = netIdType.GetField("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (valueField != null)
                {
                    valueField.SetValue(boxed, netId);
                    return boxed;
                }

                PropertyInfo valueProperty = netIdType.GetProperty("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (valueProperty != null && valueProperty.CanWrite)
                {
                    valueProperty.SetValue(boxed, netId, null);
                    return boxed;
                }
            }
            catch
            {
            }

            return null;
        }

        private bool TryResolveNetCookWorldCookerComponentAuraMono(IntPtr entityObj, out IntPtr componentObj, out string status)
        {
            componentObj = IntPtr.Zero;
            status = "AuraMono world cooker component missing.";
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr entityClass = auraMonoObjectGetClass(entityObj);
            if (entityClass == IntPtr.Zero)
            {
                status = "AuraMono owner entity class unavailable.";
                return false;
            }

            IntPtr getAllComponentsMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "GetAllComponents", 0);
            if (getAllComponentsMethod == IntPtr.Zero)
            {
                status = "AuraMono owner entity GetAllComponents unavailable.";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr componentsObj = auraMonoRuntimeInvoke(getAllComponentsMethod, entityObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || componentsObj == IntPtr.Zero)
            {
                status = "AuraMono owner entity components unavailable.";
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components) || components.Count <= 0)
            {
                status = "AuraMono owner entity has no components.";
                return false;
            }

            for (int i = 0; i < components.Count && i < 128; i++)
            {
                IntPtr candidate = components[i];
                if (candidate == IntPtr.Zero)
                {
                    continue;
                }

                string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(candidate));
                if (string.IsNullOrEmpty(className))
                {
                    continue;
                }

                if (className.IndexOf("WorldCookerComponent", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    componentObj = candidate;
                    status = "AuraMono world cooker component ready.";
                    return true;
                }
            }

            status = "AuraMono current target is not a world cooker.";
            return false;
        }

        private bool TryGetNetCookScanOrigin(out Vector3 origin, out string status)
        {
            origin = Vector3.zero;
            status = "No scan origin.";

            try
            {
                GameObject player = GetPlayer();
                if (player != null && player.transform != null)
                {
                    origin = player.transform.position;
                    status = "Player origin";
                    return true;
                }

                Camera mainCamera = Camera.main;
                if (mainCamera != null && mainCamera.transform != null)
                {
                    origin = mainCamera.transform.position;
                    status = "Camera origin";
                    return true;
                }

                status = "Player and camera unavailable.";
                return false;
            }
            catch (Exception ex)
            {
                status = "Scan origin exception: " + ex.Message;
                return false;
            }
        }

        private bool TryGetNetCookLevelObjectPosition(object levelObject, out Vector3 position)
        {
            position = Vector3.zero;

            try
            {
                if (this.TryGetManagedMemberValue(levelObject, "position") is Vector3 directPosition)
                {
                    position = directPosition;
                    return true;
                }

                if (this.TryGetManagedMemberValue(levelObject, "worldPosition") is Vector3 worldPosition)
                {
                    position = worldPosition;
                    return true;
                }

                object ownerEntity = this.TryGetManagedMemberValue(levelObject, "ownerEntity");
                if (ownerEntity != null)
                {
                    if (this.TryGetManagedMemberValue(ownerEntity, "position") is Vector3 entityPosition)
                    {
                        position = entityPosition;
                        return true;
                    }

                    object transformComponent = this.TryGetManagedMemberValue(ownerEntity, "transformComponent");
                    if (transformComponent != null)
                    {
                        if (this.TryGetManagedMemberValue(transformComponent, "position") is Vector3 transformPosition)
                        {
                            position = transformPosition;
                            return true;
                        }

                        if (this.TryGetManagedMemberValue(transformComponent, "worldPosition") is Vector3 transformWorldPosition)
                        {
                            position = transformWorldPosition;
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryResolveNetCookContextFromLevelObject(ulong levelObjectNetId, out uint cookerNetId, out int cookerStaticId, out int cookerType, out string status)
        {
            cookerNetId = 0U;
            cookerStaticId = 0;
            cookerType = 0;
            status = "Level object is not a cooker.";
            this.NetCookLog("Resolving level object " + levelObjectNetId + "...");

            try
            {
                Type levelObjectManagerType = this.FindLevelObjectManagerRuntimeType();
                if (levelObjectManagerType == null)
                {
                    status = "LevelObjectManager unavailable.";
                    this.NetCookLog(status);
                    return false;
                }

                PropertyInfo instanceProperty = levelObjectManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object levelObjectManager = instanceProperty != null ? instanceProperty.GetValue(null, null) : null;
                if (levelObjectManager == null)
                {
                    status = "LevelObjectManager instance unavailable.";
                    this.NetCookLog(status);
                    return false;
                }

                MethodInfo getLevelObjectMethod = levelObjectManagerType.GetMethod("GetLevelObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ulong) }, null)
                    ?? levelObjectManagerType.GetMethod("GetLevelObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ulong), typeof(int) }, null);
                if (getLevelObjectMethod == null)
                {
                    status = "GetLevelObject unavailable.";
                    this.NetCookLog(status);
                    return false;
                }

                object levelObject = getLevelObjectMethod.GetParameters().Length == 1
                    ? getLevelObjectMethod.Invoke(levelObjectManager, new object[] { levelObjectNetId })
                    : getLevelObjectMethod.Invoke(levelObjectManager, new object[] { levelObjectNetId, 0 });
                if (levelObject == null)
                {
                    status = "Target level object missing.";
                    this.NetCookLog(status + " netId=" + levelObjectNetId);
                    return false;
                }

                if (!this.TryReadManagedUInt32Member(levelObject, "ownerNetId", out cookerNetId) || cookerNetId == 0U)
                {
                    status = "Cooker owner net id missing.";
                    this.NetCookLog(status);
                    return false;
                }

                this.NetCookLog("Level object " + levelObjectNetId + " ownerNetId=" + cookerNetId);

                object ownerEntity = this.TryGetManagedMemberValue(levelObject, "ownerEntity");
                if (ownerEntity == null)
                {
                    status = "Cooker owner entity missing.";
                    this.NetCookLog(status);
                    return false;
                }

                object transformComponent = this.TryGetManagedMemberValue(ownerEntity, "transformComponent");
                if (transformComponent == null)
                {
                    status = "Cooker transform component missing.";
                    this.NetCookLog(status);
                    return false;
                }

                string transformTypeName = transformComponent.GetType().FullName ?? transformComponent.GetType().Name;
                this.NetCookLog("Transform component type=" + transformTypeName);
                if (transformTypeName.IndexOf("WorldCookerComponent", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    status = "Current target is not a world cooker.";
                    this.NetCookLog(status);
                    return false;
                }

                object componentData = this.TryGetManagedMemberValue(transformComponent, "ComponentData");
                if (componentData == null || !this.TryReadManagedInt32Member(componentData, "staticId", out cookerStaticId) || cookerStaticId <= 0)
                {
                    status = "Cooker static id missing.";
                    this.NetCookLog(status);
                    return false;
                }

                this.NetCookLog("Cooker staticId=" + cookerStaticId);

                if (!this.TryGetCookerTypeForStaticId(cookerStaticId, out cookerType))
                {
                    status = "Cooker type lookup failed.";
                    this.NetCookLog(status);
                    return false;
                }

                status = "Cooker context ready.";
                this.NetCookLog("Cooker type=" + cookerType);
                return true;
            }
            catch (Exception ex)
            {
                status = "Cooker resolve exception: " + ex.Message;
                this.NetCookLog(status);
                return false;
            }
        }

        private bool TryGetCookerTypeForStaticId(int cookerStaticId, out int cookerType)
        {
            cookerType = 0;
            if (cookerStaticId <= 0)
            {
                return false;
            }

            if (this.netCookCookerTypeCache.TryGetValue(cookerStaticId, out cookerType))
            {
                return cookerType > 0;
            }

            if (this.netCookCookerTypeFailedStaticIds.Contains(cookerStaticId))
            {
                return false;
            }

            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    this.NetCookLog("GetCookerType failed: TableData unavailable.");
                    this.netCookCookerTypeFailedStaticIds.Add(cookerStaticId);
                    return false;
                }

                MethodInfo getCookerMethod = tableDataType.GetMethod("GetCooker", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(int), typeof(bool) }, null);
                if (getCookerMethod == null)
                {
                    this.NetCookLog("GetCookerType failed: GetCooker unavailable.");
                    this.netCookCookerTypeFailedStaticIds.Add(cookerStaticId);
                    return false;
                }

                object cookerData = getCookerMethod.Invoke(null, new object[] { cookerStaticId, false });
                if (cookerData == null)
                {
                    this.NetCookLog("GetCookerType failed: cooker data null for staticId=" + cookerStaticId);
                    this.netCookCookerTypeFailedStaticIds.Add(cookerStaticId);
                    return false;
                }

                if (!this.TryGetObjectMember(cookerData, "cookerType", out object cookerTypeObj) || cookerTypeObj == null)
                {
                    this.NetCookLog("GetCookerType failed: cookerType member missing for staticId=" + cookerStaticId);
                    this.netCookCookerTypeFailedStaticIds.Add(cookerStaticId);
                    return false;
                }

                cookerType = Convert.ToInt32(cookerTypeObj);
                this.netCookCookerTypeCache[cookerStaticId] = cookerType;
                this.NetCookLog("GetCookerType staticId=" + cookerStaticId + " => cookerType=" + cookerType);
                return cookerType > 0;
            }
            catch (Exception ex)
            {
                this.NetCookLog("GetCookerType exception for staticId=" + cookerStaticId + ": " + ex.Message);
                this.netCookCookerTypeFailedStaticIds.Add(cookerStaticId);
                cookerType = 0;
                return false;
            }
        }

        private void NetCookLog(string message)
        {
            if (!NetCookLogsEnabled)
            {
                return;
            }

            try
            {
                ModLogger.Msg("[NetCook] " + message);
            }
            catch
            {
            }
        }

        // Unconditional NetCook diagnostics (NOT gated by MasterLogNetCook): used for the
        // registry-hook install status and hook-fire counts so they are observable without
        // enabling the noisy full NetCook trace.
        private void NetCookHookLog(string message)
        {
            try
            {
                ModLogger.Msg("[NetCook] " + message);
            }
            catch
            {
            }
        }

        private string GetNetCookSelectedRecipeLabel()
        {
            if (this.netCookRecipeId <= 0)
            {
                return "None";
            }

            for (int i = 0; i < this.netCookRecipeEntries.Count; i++)
            {
                if (this.netCookRecipeEntries[i].Key == this.netCookRecipeId)
                {
                    return this.netCookRecipeEntries[i].Value;
                }
            }

            return "Recipe " + this.netCookRecipeId;
        }

        private bool IsNetCookRecipeCompatibleWithCurrentCooker(out string status)
        {
            status = "Recipe/cooker ready.";
            if (this.netCookRecipeId <= 0)
            {
                status = "No recipe selected.";
                return false;
            }

                if (this.netCookCookerStaticId <= 0)
                {
                    status = "No cooker captured.";
                    return false;
                }

            if (!this.EnsureNetCookRecipeCache())
            {
                status = this.netCookStatus ?? "Recipe cache unavailable.";
                return false;
            }

            try
            {
                int currentCookerType = this.netCookCookerType;
                if (!this.netCookRecipeCookerTypes.TryGetValue(this.netCookRecipeId, out int recipeCookerType))
                {
                    return this.netCookRecipeCacheCookerStaticId == this.netCookCookerStaticId;
                }

                if (currentCookerType > 0 && recipeCookerType > 0 && currentCookerType != recipeCookerType)
                {
                    status = "Selected recipe does not match this cooker.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                status = "Cooker validation failed: " + ex.Message;
                return false;
            }
        }

        private void SyncNetCookCookQuantityFromInput()
        {
            if (!int.TryParse(this.netCookCookQuantityInput, out int parsed) || parsed < 0)
            {
                parsed = 1;
            }

            this.netCookCookQuantity = parsed;
            this.netCookCookQuantityInput = this.netCookCookQuantity.ToString();
        }

        private void ResetNetCookDishLimitToDefault()
        {
            this.netCookCookQuantity = 1;
            this.netCookCookQuantityInput = "1";
        }

        private void RefreshNetCookMaxCookQuantity(bool force = false)
        {
            if (this.netCookMiniGameOnly || this.netCookRecipeId <= 0)
            {
                this.netCookMaxCookQuantity = 0;
                return;
            }

            float now = Time.unscaledTime;
            if (!force && now < this.nextNetCookMaxRefreshAt)
            {
                return;
            }

            this.nextNetCookMaxRefreshAt = now + NetCookMaxRefreshIntervalSeconds;
            if (!this.TryComputeNetCookMaxQuantity(this.netCookRecipeId, this.netCookMoveIngredients, out int maxQuantity))
            {
                this.netCookMaxCookQuantity = 0;
                return;
            }

            this.netCookMaxCookQuantity = maxQuantity;
        }

        private bool TryComputeNetCookMaxQuantity(int recipeId, bool includeWarehouse, out int maxQuantity)
        {
            maxQuantity = 0;
            if (recipeId <= 0)
            {
                return false;
            }

            if (!this.TryGetNetCookRecipeRequirements(recipeId, out List<NetCookIngredientRequirement> requirements, out _))
            {
                return false;
            }

            if (requirements == null || requirements.Count == 0)
            {
                return false;
            }

            Dictionary<int, int> totalsByStaticId = new Dictionary<int, int>();
            this.AggregateNetCookIngredientCounts(NetCookBackpackStorageType, totalsByStaticId);
            if (includeWarehouse)
            {
                this.AggregateNetCookIngredientCounts(NetCookWarehouseStorageType, totalsByStaticId);
            }

            Dictionary<int, int> requiredPerDish = new Dictionary<int, int>();
            for (int i = 0; i < requirements.Count; i++)
            {
                NetCookIngredientRequirement requirement = requirements[i];
                if (requirement.StaticId <= 0 || requirement.CountPerDish <= 0)
                {
                    continue;
                }

                if (requiredPerDish.TryGetValue(requirement.StaticId, out int existing))
                {
                    requiredPerDish[requirement.StaticId] = existing + requirement.CountPerDish;
                }
                else
                {
                    requiredPerDish[requirement.StaticId] = requirement.CountPerDish;
                }
            }

            if (requiredPerDish.Count == 0)
            {
                return false;
            }

            bool hasLimit = false;
            foreach (KeyValuePair<int, int> pair in requiredPerDish)
            {
                totalsByStaticId.TryGetValue(pair.Key, out int available);
                int possible = available / Math.Max(1, pair.Value);
                if (!hasLimit)
                {
                    maxQuantity = possible;
                    hasLimit = true;
                }
                else
                {
                    maxQuantity = Math.Min(maxQuantity, possible);
                }
            }

            return hasLimit;
        }

        private bool TryGetNetCookRecipeRequirements(int recipeId, out List<NetCookIngredientRequirement> requirements, out string status)
        {
            requirements = null;
            status = "Recipe requirements unavailable.";
            if (recipeId <= 0)
            {
                return false;
            }

            if (this.netCookRecipeRequirementsCache.TryGetValue(recipeId, out List<NetCookIngredientRequirement> cached) && cached != null)
            {
                requirements = cached;
                status = "Recipe requirements ready.";
                return cached.Count > 0;
            }

            List<NetCookIngredientRequirement> resolved = new List<NetCookIngredientRequirement>(8);
            if (this.TryGetNetCookRecipeRequirementsAuraMono(recipeId, resolved, out status)
                || this.TryGetNetCookRecipeRequirementsManaged(recipeId, resolved, out status)
                || this.TryGetNetCookRecipeRequirementsFromTable(recipeId, resolved, out status))
            {
                this.netCookRecipeRequirementsCache[recipeId] = resolved;
                requirements = resolved;
                return resolved.Count > 0;
            }

            this.netCookRecipeRequirementsCache[recipeId] = resolved;
            requirements = resolved;
            return false;
        }

        private unsafe bool TryGetNetCookRecipeRequirementsAuraMono(int recipeId, List<NetCookIngredientRequirement> requirements, out string status)
        {
            status = "AuraMono recipe requirements unavailable.";
            requirements?.Clear();
            if (requirements == null)
            {
                return false;
            }

            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Cooking.CookingSystem", out IntPtr cookingSystemObj)
                    || cookingSystemObj == IntPtr.Zero
                    || auraMonoObjectGetClass == null
                    || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                IntPtr cookingSystemClass = auraMonoObjectGetClass(cookingSystemObj);
                if (cookingSystemClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr initDetailMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "InitCookingRecipeDetail", 1);
                IntPtr getDetailMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "GetRecipeDetail", 1);
                IntPtr detailMethod = initDetailMethod != IntPtr.Zero ? initDetailMethod : getDetailMethod;
                if (detailMethod == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&recipeId);
                IntPtr detailObj = auraMonoRuntimeInvoke(detailMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if ((detailObj == IntPtr.Zero || exc != IntPtr.Zero) && detailMethod != getDetailMethod && getDetailMethod != IntPtr.Zero)
                {
                    exc = IntPtr.Zero;
                    detailObj = auraMonoRuntimeInvoke(getDetailMethod, cookingSystemObj, (IntPtr)args, ref exc);
                }

                if (exc != IntPtr.Zero || detailObj == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryAppendNetCookRequirementsFromMonoDetail(detailObj, requirements))
                {
                    return false;
                }

                status = "AuraMono recipe requirements ready.";
                return requirements.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetNetCookRecipeRequirementsManaged(int recipeId, List<NetCookIngredientRequirement> requirements, out string status)
        {
            status = "Managed recipe requirements unavailable.";
            requirements?.Clear();
            if (requirements == null)
            {
                return false;
            }

            try
            {
                if (!this.EnsureNetCookMethods())
                {
                    return false;
                }

                object cookingSystem = this.netCookCookingSystemInstanceProperty.GetValue(null, null);
                if (cookingSystem == null)
                {
                    return false;
                }

                object detail = this.netCookInitRecipeDetailMethod.Invoke(cookingSystem, new object[] { recipeId });
                if (detail == null && this.netCookGetRecipeDetailMethod != null)
                {
                    detail = this.netCookGetRecipeDetailMethod.Invoke(cookingSystem, new object[] { recipeId });
                }

                if (detail == null)
                {
                    return false;
                }

                if (!this.TryAppendNetCookRequirementsFromManagedDetail(detail, requirements))
                {
                    return false;
                }

                status = "Managed recipe requirements ready.";
                return requirements.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetNetCookRecipeRequirementsFromTable(int recipeId, List<NetCookIngredientRequirement> requirements, out string status)
        {
            status = "Table recipe requirements unavailable.";
            requirements?.Clear();
            if (requirements == null)
            {
                return false;
            }

            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    return false;
                }

                MethodInfo getCookingRecipeMethod = tableDataType.GetMethod("GetCookingRecipe", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(int), typeof(bool) }, null);
                if (getCookingRecipeMethod == null)
                {
                    return false;
                }

                object recipeTable = getCookingRecipeMethod.Invoke(null, new object[] { recipeId, false });
                if (recipeTable == null)
                {
                    return false;
                }

                if (!this.TryAppendNetCookRequirementsFromManagedObject(recipeTable, requirements))
                {
                    return false;
                }

                status = "Table recipe requirements ready.";
                return requirements.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryAppendNetCookRequirementsFromManagedDetail(object detail, List<NetCookIngredientRequirement> requirements)
        {
            if (detail == null || requirements == null)
            {
                return false;
            }

            object slotsObj = this.TryGetManagedMemberValue(detail, "materialSlots");
            IEnumerable slots = slotsObj as IEnumerable;
            if (slots != null)
            {
                foreach (object slot in slots)
                {
                    if (this.TryReadNetCookMaterialSlotRequirementManaged(slot, out NetCookIngredientRequirement requirement))
                    {
                        requirements.Add(requirement);
                    }
                }
            }

            if (requirements.Count > 0)
            {
                return true;
            }

            return this.TryAppendNetCookRequirementsFromManagedObject(detail, requirements);
        }

        private unsafe bool TryAppendNetCookRequirementsFromMonoDetail(IntPtr detailObj, List<NetCookIngredientRequirement> requirements)
        {
            if (detailObj == IntPtr.Zero || requirements == null)
            {
                return false;
            }

            IntPtr slotsObj = IntPtr.Zero;
            if (this.TryGetMonoObjectMember(detailObj, "materialSlots", out slotsObj) && slotsObj != IntPtr.Zero)
            {
                List<IntPtr> slotItems = new List<IntPtr>(16);
                if (this.TryEnumerateAuraMonoCollectionItems(slotsObj, slotItems))
                {
                    for (int i = 0; i < slotItems.Count; i++)
                    {
                        if (this.TryReadNetCookMaterialSlotRequirementMono(slotItems[i], out NetCookIngredientRequirement requirement))
                        {
                            requirements.Add(requirement);
                        }
                    }
                }
            }

            return requirements.Count > 0;
        }

        private bool TryAppendNetCookRequirementsFromManagedObject(object source, List<NetCookIngredientRequirement> requirements)
        {
            if (source == null || requirements == null)
            {
                return false;
            }

            string[] materialCollectionFields = { "materials", "cookingMaterials", "materialList", "recipeMaterials", "Materials", "MaterialList" };
            for (int fieldIndex = 0; fieldIndex < materialCollectionFields.Length; fieldIndex++)
            {
                object materialsObj = this.TryGetManagedMemberValue(source, materialCollectionFields[fieldIndex]);
                IEnumerable materials = materialsObj as IEnumerable;
                if (materials == null)
                {
                    continue;
                }

                foreach (object material in materials)
                {
                    if (this.TryReadNetCookMaterialSlotRequirementManaged(material, out NetCookIngredientRequirement requirement))
                    {
                        requirements.Add(requirement);
                    }
                }
            }

            return requirements.Count > 0;
        }

        private bool TryReadNetCookMaterialSlotRequirementManaged(object slot, out NetCookIngredientRequirement requirement)
        {
            requirement = default;
            if (slot == null)
            {
                return false;
            }

            int staticId = 0;
            string[] staticIdFields = { "staticId", "materialStaticId", "itemStaticId", "entityStaticId", "materialId", "StaticId", "MaterialStaticId" };
            for (int i = 0; i < staticIdFields.Length; i++)
            {
                if (this.TryReadManagedInt32Member(slot, staticIdFields[i], out staticId) && staticId > 0)
                {
                    break;
                }
            }

            if (staticId <= 0)
            {
                object nested = this.TryGetManagedMemberValue(slot, "material");
                if (nested != null && nested != slot)
                {
                    return this.TryReadNetCookMaterialSlotRequirementManaged(nested, out requirement);
                }

                return false;
            }

            int countPerDish = 1;
            string[] countFields = { "needNum", "needCount", "count", "materialCount", "num", "NeedNum", "Count" };
            for (int i = 0; i < countFields.Length; i++)
            {
                if (this.TryReadManagedInt32Member(slot, countFields[i], out int candidate) && candidate > 0)
                {
                    countPerDish = candidate;
                    break;
                }
            }

            requirement = new NetCookIngredientRequirement { StaticId = staticId, CountPerDish = countPerDish };
            return true;
        }

        private bool TryReadNetCookMaterialSlotRequirementMono(IntPtr slotObj, out NetCookIngredientRequirement requirement)
        {
            requirement = default;
            if (slotObj == IntPtr.Zero)
            {
                return false;
            }

            int staticId = 0;
            string[] staticIdFields = { "staticId", "materialStaticId", "itemStaticId", "entityStaticId", "materialId", "StaticId", "MaterialStaticId" };
            for (int i = 0; i < staticIdFields.Length; i++)
            {
                if (this.TryGetMonoIntMember(slotObj, staticIdFields[i], out staticId) && staticId > 0)
                {
                    break;
                }
            }

            if (staticId <= 0)
            {
                if (this.TryGetMonoObjectMember(slotObj, "material", out IntPtr nestedObj) && nestedObj != IntPtr.Zero)
                {
                    return this.TryReadNetCookMaterialSlotRequirementMono(nestedObj, out requirement);
                }

                return false;
            }

            int countPerDish = 1;
            string[] countFields = { "needNum", "needCount", "count", "materialCount", "num", "NeedNum", "Count" };
            for (int i = 0; i < countFields.Length; i++)
            {
                if (this.TryGetMonoIntMember(slotObj, countFields[i], out int candidate) && candidate > 0)
                {
                    countPerDish = candidate;
                    break;
                }
            }

            requirement = new NetCookIngredientRequirement { StaticId = staticId, CountPerDish = countPerDish };
            return true;
        }

        private void AggregateNetCookIngredientCounts(int storageType, Dictionary<int, int> totalsByStaticId)
        {
            if (totalsByStaticId == null)
            {
                return;
            }

            int countBefore = totalsByStaticId.Count;
            this.AggregateNetCookIngredientCountsAuraMono(storageType, totalsByStaticId);
            if (totalsByStaticId.Count == countBefore)
            {
                this.AggregateNetCookIngredientCountsManaged(storageType, totalsByStaticId);
            }
        }

        private unsafe void AggregateNetCookIngredientCountsAuraMono(int storageType, Dictionary<int, int> totalsByStaticId)
        {
            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj)
                    || backPackSystemObj == IntPtr.Zero
                    || auraMonoObjectGetClass == null
                    || auraMonoRuntimeInvoke == null)
                {
                    return;
                }

                IntPtr backPackClass = auraMonoObjectGetClass(backPackSystemObj);
                IntPtr getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
                bool needsStorageType = true;
                if (getAllItemMethod == IntPtr.Zero)
                {
                    getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                    needsStorageType = false;
                }

                if (getAllItemMethod == IntPtr.Zero)
                {
                    return;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr itemListObj;
                int storageTypeValue = storageType;
                if (needsStorageType)
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&storageTypeValue);
                    itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, (IntPtr)args, ref exc);
                }
                else
                {
                    itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, IntPtr.Zero, ref exc);
                }

                if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                {
                    return;
                }

                // Pin every enumerated item the moment it is obtained: the member reads below box
                // values (mono-side allocations) that can trigger a moving SGen collection and relocate
                // the not-yet-processed items in this list, turning their IntPtrs stale -> native AV in
                // auraMonoObjectGetClass (observed: ExecutionEngineException 0x80131506 on the OnGUI
                // thread during Mass Cook). mono_gc_disable is not exported on this build, so per-item
                // pinning is the only protection. Freed in finally.
                List<IntPtr> items = new List<IntPtr>(128);
                List<uint> itemPins = new List<uint>(128);
                bool enumerated = this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, itemPins);
                try
                {
                    if (!enumerated)
                    {
                        return;
                    }

                    for (int i = 0; i < items.Count; i++)
                    {
                        IntPtr itemObj = items[i];
                        if (itemObj == IntPtr.Zero
                            || (this.TryGetDirectBackpackItemIsLocked(itemObj, out bool isLocked) && isLocked)
                            || !this.TryGetDirectBackpackItemStaticId(itemObj, out int staticId)
                            || staticId <= 0)
                        {
                            continue;
                        }

                        if (!this.TryGetDirectBackpackItemCount(itemObj, out int count) || count <= 0)
                        {
                            count = 1;
                        }

                        if (totalsByStaticId.TryGetValue(staticId, out int existing))
                        {
                            totalsByStaticId[staticId] = existing + count;
                        }
                        else
                        {
                            totalsByStaticId[staticId] = count;
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
        }

        private void AggregateNetCookIngredientCountsManaged(int storageType, Dictionary<int, int> totalsByStaticId)
        {
            try
            {
                Type backPackType = this.FindLoadedType("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", "BackPackSystem");
                if (backPackType == null)
                {
                    return;
                }

                if (!this.TryGetManagedModule(backPackType, out object backPackObj) || backPackObj == null)
                {
                    backPackObj = this.TryGetStaticObjectAcrossHierarchy(backPackType, "Instance", "_instance");
                }

                if (backPackObj == null)
                {
                    return;
                }

                Type storageTypeEnum = this.FindLoadedType("EcsClient.XDT.Scene.Shared.Data.StaticPartial.EStorageType", "EStorageType");
                object storageArg = storageTypeEnum != null && storageTypeEnum.IsEnum ? Enum.ToObject(storageTypeEnum, storageType) : (object)storageType;
                MethodInfo getAllItem = backPackObj.GetType().GetMethod("GetAllItem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { storageArg.GetType() }, null);
                if (getAllItem == null)
                {
                    getAllItem = backPackObj.GetType().GetMethod("GetAllItem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                }

                if (getAllItem == null)
                {
                    return;
                }

                object itemListObj = getAllItem.GetParameters().Length == 1
                    ? getAllItem.Invoke(backPackObj, new[] { storageArg })
                    : getAllItem.Invoke(backPackObj, null);
                IEnumerable items = itemListObj as IEnumerable;
                if (items == null)
                {
                    return;
                }

                foreach (object item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (this.TryReadManagedBoolMember(item, "isLock", out bool isLocked) && isLocked)
                    {
                        continue;
                    }

                    if (!this.TryReadManagedInt32Member(item, "staticId", out int staticId) || staticId <= 0)
                    {
                        this.TryReadManagedInt32Member(item, "StaticId", out staticId);
                    }

                    if (staticId <= 0)
                    {
                        continue;
                    }

                    int count = 1;
                    this.TryGetManagedBackpackItemCount(item, out count);
                    if (totalsByStaticId.TryGetValue(staticId, out int existing))
                    {
                        totalsByStaticId[staticId] = existing + count;
                    }
                    else
                    {
                        totalsByStaticId[staticId] = count;
                    }
                }
            }
            catch
            {
            }
        }

        private bool TryMoveNetCookIngredientsFromWarehouse(bool useAll, int cookQuantity, out string status)
        {
            status = string.Empty;
            if (!this.TryGetNetCookRecipeRequirements(this.netCookRecipeId, out List<NetCookIngredientRequirement> requirements, out string requirementStatus))
            {
                status = requirementStatus;
                return false;
            }

            if (!this.TryBuildNetCookWarehouseMoveMap(requirements, cookQuantity, out Dictionary<uint, int> moveMap, out string buildStatus))
            {
                status = buildStatus;
                return false;
            }

            if (moveMap.Count == 0)
            {
                status = string.IsNullOrWhiteSpace(buildStatus)
                    ? "Bag already has required ingredients."
                    : buildStatus;
                return true;
            }

            List<uint> keys = new List<uint>(moveMap.Keys);
            int sentStacks = 0;
            int sentQty = 0;
            for (int offset = 0; offset < keys.Count; offset += TransferBatchMaxCount)
            {
                Dictionary<uint, int> chunk = new Dictionary<uint, int>();
                int end = Math.Min(keys.Count, offset + TransferBatchMaxCount);
                for (int i = offset; i < end; i++)
                {
                    uint netId = keys[i];
                    chunk[netId] = moveMap[netId];
                }

                if (!this.TrySendTransferBatch(chunk, NetCookWarehouseStorageType, out string error))
                {
                    status = string.IsNullOrEmpty(error)
                        ? "MoveBatchBackpackItems failed"
                        : error + (sentStacks > 0 ? " (after " + sentStacks + " stack(s))" : string.Empty);
                    return false;
                }

                sentStacks += chunk.Count;
                foreach (int qty in chunk.Values)
                {
                    sentQty += qty;
                }
            }

            this.nextNetCookMaxRefreshAt = 0f;
            status = "Moved " + sentStacks + " ingredient stack(s), qty " + sentQty + " -> Bag";
            return true;
        }

        private unsafe bool TryBuildNetCookWarehouseMoveMap(List<NetCookIngredientRequirement> requirements, int cookQuantity, out Dictionary<uint, int> moveMap, out string status)
        {
            moveMap = new Dictionary<uint, int>();
            status = string.Empty;
            if (requirements == null || requirements.Count == 0)
            {
                status = "Recipe requirements unavailable.";
                return false;
            }

            Dictionary<int, int> requiredPerDish = new Dictionary<int, int>();
            HashSet<int> requiredStaticIds = new HashSet<int>();
            for (int i = 0; i < requirements.Count; i++)
            {
                NetCookIngredientRequirement requirement = requirements[i];
                if (requirement.StaticId <= 0 || requirement.CountPerDish <= 0)
                {
                    continue;
                }

                requiredStaticIds.Add(requirement.StaticId);
                if (requiredPerDish.TryGetValue(requirement.StaticId, out int existing))
                {
                    requiredPerDish[requirement.StaticId] = existing + requirement.CountPerDish;
                }
                else
                {
                    requiredPerDish[requirement.StaticId] = requirement.CountPerDish;
                }
            }

            if (requiredStaticIds.Count == 0)
            {
                status = "Recipe requirements unavailable.";
                return false;
            }

            Dictionary<int, List<KeyValuePair<uint, int>>> stacksByStaticId = new Dictionary<int, List<KeyValuePair<uint, int>>>();
            if (!this.TryCollectNetCookWarehouseStacks(stacksByStaticId, requiredStaticIds, out status))
            {
                return false;
            }

            Dictionary<int, int> bagTotalsByStaticId = new Dictionary<int, int>();
            this.AggregateNetCookIngredientCounts(NetCookBackpackStorageType, bagTotalsByStaticId);

            int batches = Math.Max(1, cookQuantity);
            bool anyMoveDeficit = false;
            foreach (KeyValuePair<int, int> requirement in requiredPerDish)
            {
                int neededTotal = batches * requirement.Value;
                bagTotalsByStaticId.TryGetValue(requirement.Key, out int inBag);
                int remaining = Math.Max(0, neededTotal - inBag);
                if (remaining <= 0)
                {
                    continue;
                }

                anyMoveDeficit = true;
                if (!stacksByStaticId.TryGetValue(requirement.Key, out List<KeyValuePair<uint, int>> stacks) || stacks == null || stacks.Count == 0)
                {
                    continue;
                }

                stacks.Sort((a, b) => a.Value.CompareTo(b.Value));
                for (int i = 0; i < stacks.Count && remaining > 0; i++)
                {
                    uint netId = stacks[i].Key;
                    int stackCount = stacks[i].Value;
                    int take = Math.Min(remaining, stackCount);
                    if (take <= 0)
                    {
                        continue;
                    }

                    moveMap[netId] = take;
                    remaining -= take;
                }
            }

            if (moveMap.Count == 0)
            {
                status = anyMoveDeficit
                    ? "No matching ingredients in warehouse."
                    : "Bag already has required ingredients.";
            }

            return true;
        }

        private unsafe bool TryCollectNetCookWarehouseStacks(Dictionary<int, List<KeyValuePair<uint, int>>> stacksByStaticId, HashSet<int> requiredStaticIds, out string status)
        {
            status = string.Empty;
            if (stacksByStaticId == null || requiredStaticIds == null || requiredStaticIds.Count == 0)
            {
                status = "Warehouse scan unavailable.";
                return false;
            }

            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj)
                    || backPackSystemObj == IntPtr.Zero
                    || auraMonoObjectGetClass == null
                    || auraMonoRuntimeInvoke == null)
                {
                    status = "BackPackSystem unavailable.";
                    return false;
                }

                IntPtr backPackClass = auraMonoObjectGetClass(backPackSystemObj);
                IntPtr getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
                bool needsStorageType = true;
                if (getAllItemMethod == IntPtr.Zero)
                {
                    getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                    needsStorageType = false;
                }

                if (getAllItemMethod == IntPtr.Zero)
                {
                    status = "GetAllItem unavailable.";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr itemListObj;
                int storageTypeValue = NetCookWarehouseStorageType;
                if (needsStorageType)
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&storageTypeValue);
                    itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, (IntPtr)args, ref exc);
                }
                else
                {
                    itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj, IntPtr.Zero, ref exc);
                }

                if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
                {
                    status = "Warehouse read failed.";
                    return false;
                }

                List<IntPtr> warehouseItems = new List<IntPtr>(128);
                if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, warehouseItems))
                {
                    return true;
                }

                for (int i = 0; i < warehouseItems.Count; i++)
                {
                    IntPtr itemObj = warehouseItems[i];
                    if (itemObj == IntPtr.Zero
                        || !this.TryGetDirectBackpackItemNetId(itemObj, out uint netId)
                        || netId == 0U
                        || (this.TryGetDirectBackpackItemIsLocked(itemObj, out bool isLocked) && isLocked)
                        || !this.TryGetDirectBackpackItemStaticId(itemObj, out int staticId)
                        || !requiredStaticIds.Contains(staticId))
                    {
                        continue;
                    }

                    if (!this.TryGetDirectBackpackItemCount(itemObj, out int count) || count <= 0)
                    {
                        count = 1;
                    }

                    if (!stacksByStaticId.TryGetValue(staticId, out List<KeyValuePair<uint, int>> stacks))
                    {
                        stacks = new List<KeyValuePair<uint, int>>(4);
                        stacksByStaticId[staticId] = stacks;
                    }

                    stacks.Add(new KeyValuePair<uint, int>(netId, count));
                }

                return true;
            }
            catch (Exception ex)
            {
                status = "Warehouse scan failed: " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryInvokeNetCookRefreshSlots()
        {
            try
            {
                if (this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Cooking.CookingSystem", out IntPtr cookingSystemObj)
                    && cookingSystemObj != IntPtr.Zero
                    && auraMonoObjectGetClass != null
                    && auraMonoRuntimeInvoke != null)
                {
                    IntPtr cookingSystemClass = auraMonoObjectGetClass(cookingSystemObj);
                    IntPtr refreshMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "RefreshSlots", 0);
                    if (refreshMethod != IntPtr.Zero)
                    {
                        IntPtr exc = IntPtr.Zero;
                        auraMonoRuntimeInvoke(refreshMethod, cookingSystemObj, IntPtr.Zero, ref exc);
                        if (exc == IntPtr.Zero)
                        {
                            return true;
                        }
                    }
                }

                if (this.EnsureNetCookMethods() && this.netCookRefreshSlotsMethod != null)
                {
                    object cookingSystem = this.netCookCookingSystemInstanceProperty.GetValue(null, null);
                    if (cookingSystem != null)
                    {
                        this.netCookRefreshSlotsMethod.Invoke(cookingSystem, null);
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryBuildNetCookMaterials(int recipeId, out List<uint> materials, out string status)
        {
            materials = new List<uint>(16);
            status = "Materials ready.";

            if (!this.IsNetCookRecipeCompatibleWithCurrentCooker(out status))
            {
                return false;
            }

            try
            {
                if (this.TryBuildNetCookMaterialsAuraMono(recipeId, materials, out status))
                {
                    return true;
                }

                string auraStatus = status;
                if (this.IsNetCookMissingIngredientStatus(auraStatus))
                {
                    return false;
                }

                if (!this.EnsureNetCookMethods())
                {
                    status = !string.IsNullOrWhiteSpace(auraStatus)
                        ? auraStatus
                        : (this.netCookStatus ?? "Cooking methods unavailable.");
                    return false;
                }

                object cookingSystem = this.netCookCookingSystemInstanceProperty.GetValue(null, null);
                if (cookingSystem == null)
                {
                    status = "CookingSystem instance unavailable.";
                    return false;
                }

                object detail = this.netCookInitRecipeDetailMethod.Invoke(cookingSystem, new object[] { recipeId });
                if (detail == null && this.netCookGetRecipeDetailMethod != null)
                {
                    detail = this.netCookGetRecipeDetailMethod.Invoke(cookingSystem, new object[] { recipeId });
                }

                if (detail == null)
                {
                    status = "Recipe detail unavailable.";
                    return false;
                }

                if (this.netCookRefreshSlotsMethod != null)
                {
                    try
                    {
                        this.netCookRefreshSlotsMethod.Invoke(cookingSystem, null);
                    }
                    catch
                    {
                    }
                }

                object slotsObj = this.TryGetManagedMemberValue(detail, "materialSlots");
                IEnumerable slots = slotsObj as IEnumerable;
                if (slots == null)
                {
                    status = "Recipe slots unavailable.";
                    return false;
                }

                foreach (object slot in slots)
                {
                    if (slot == null)
                    {
                        continue;
                    }

                    if (!this.TryReadManagedBoolMember(slot, "filled", out bool filled) || !filled)
                    {
                        string slotName = this.GetNetCookSelectedRecipeLabel();
                        status = "Missing ingredients for " + slotName;
                        return false;
                    }

                    if (!this.TryReadManagedUInt32Member(slot, "filledMaterialNetId", out uint materialNetId) || materialNetId == 0U)
                    {
                        status = "Recipe slot has no material net id.";
                        return false;
                    }

                    materials.Add(materialNetId);
                }

                if (materials.Count == 0)
                {
                    status = "Recipe has no usable material slots.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                status = "Build materials failed: " + inner.Message;
                return false;
            }
        }

        private unsafe bool TryBuildNetCookMaterialsAuraMono(int recipeId, List<uint> materials, out string status)
        {
            status = "Materials ready.";
            if (materials == null)
            {
                status = "Material buffer unavailable.";
                return false;
            }

            try
            {
                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Cooking.CookingSystem", out IntPtr cookingSystemObj)
                    || cookingSystemObj == IntPtr.Zero
                    || auraMonoObjectGetClass == null
                    || auraMonoRuntimeInvoke == null)
                {
                    status = "AuraMono CookingSystem unavailable.";
                    return false;
                }

                IntPtr cookingSystemClass = auraMonoObjectGetClass(cookingSystemObj);
                if (cookingSystemClass == IntPtr.Zero)
                {
                    status = "AuraMono CookingSystem class unavailable.";
                    return false;
                }

                IntPtr initDetailMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "InitCookingRecipeDetail", 1);
                IntPtr getDetailMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "GetRecipeDetail", 1);
                IntPtr detailMethod = initDetailMethod != IntPtr.Zero ? initDetailMethod : getDetailMethod;
                if (detailMethod == IntPtr.Zero)
                {
                    status = "AuraMono recipe detail method unavailable.";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&recipeId);
                IntPtr detailObj = auraMonoRuntimeInvoke(detailMethod, cookingSystemObj, (IntPtr)args, ref exc);
                if ((detailObj == IntPtr.Zero || exc != IntPtr.Zero) && detailMethod != getDetailMethod && getDetailMethod != IntPtr.Zero)
                {
                    exc = IntPtr.Zero;
                    detailObj = auraMonoRuntimeInvoke(getDetailMethod, cookingSystemObj, (IntPtr)args, ref exc);
                }

                if (exc != IntPtr.Zero || detailObj == IntPtr.Zero)
                {
                    status = "AuraMono recipe detail unavailable.";
                    return false;
                }

                IntPtr refreshMethod = this.FindAuraMonoMethodOnHierarchy(cookingSystemClass, "RefreshSlots", 0);
                if (refreshMethod != IntPtr.Zero)
                {
                    exc = IntPtr.Zero;
                    auraMonoRuntimeInvoke(refreshMethod, cookingSystemObj, IntPtr.Zero, ref exc);
                }

                IntPtr slotsObj = IntPtr.Zero;
                if (!this.TryGetMonoObjectMember(detailObj, "materialSlots", out slotsObj) || slotsObj == IntPtr.Zero)
                {
                    status = "AuraMono recipe slots unavailable.";
                    return false;
                }

                List<IntPtr> slotItems = new List<IntPtr>(16);
                if (!this.TryEnumerateAuraMonoCollectionItems(slotsObj, slotItems) || slotItems.Count == 0)
                {
                    status = "AuraMono recipe slots unavailable.";
                    return false;
                }

                for (int i = 0; i < slotItems.Count; i++)
                {
                    IntPtr slotObj = slotItems[i];
                    if (slotObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoBoolMember(slotObj, "filled", out bool filled) || !filled)
                    {
                        string slotName = this.GetNetCookSelectedRecipeLabel();
                        status = "Missing ingredients for " + slotName;
                        return false;
                    }

                    if (!this.TryGetMonoUInt32Member(slotObj, "filledMaterialNetId", out uint materialNetId) || materialNetId == 0U)
                    {
                        status = "Recipe slot has no material net id.";
                        return false;
                    }

                    materials.Add(materialNetId);
                }

                if (materials.Count == 0)
                {
                    status = "Recipe has no usable material slots.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                status = "AuraMono material build failed: " + ex.Message;
                materials.Clear();
                return false;
            }
        }

        private void StartAutoCookInternal()
        {
            this.autoCookEnabled = true;
            this.cookingCleanupMode = false;
            this.SetGameSpeed(this.cookingAutoSpeed);
            ModLogger.Msg($"[Cooking] Bot STARTED (Auto Speed x{this.cookingAutoSpeed:F1})");
            this.AddMenuNotification("Auto Cook Enabled", new Color(0.45f, 1f, 0.55f));

            if (cookingPatrolEnabled && cookingPatrolPoints.Count > 0 && !isCookingPatrolActive)
            {
                isCookingPatrolActive = true;
                cookingPatrolCoroutine = ModCoroutines.Start(CookingPatrolRoutine());
                ModLogger.Msg("[Cooking Patrol] STARTED");
            }
            // Setup auto-stop timer if enabled
            int autoStopSeconds = this.GetAutoCookAutoStopSeconds();
            if (this.autoCookAutoStopEnabled && autoStopSeconds > 0)
            {
                this.autoCookAutoStopAt = Time.unscaledTime + autoStopSeconds;
            }
            else
            {
                this.autoCookAutoStopAt = -1f;
            }
        }

        private void StopAutoCookInternal(string reason)
        {
            bool wasEnabled = this.autoCookEnabled || this.isCookingPatrolActive;
            this.autoCookEnabled = false;
            this.cookingCleanupMode = false;
            this.SetGameSpeed(1f);

            isCookingPatrolActive = false;
            if (cookingPatrolCoroutine != null)
            {
                ModCoroutines.Stop(cookingPatrolCoroutine);
                cookingPatrolCoroutine = null;
            }

            if (wasEnabled)
            {
                ModLogger.Msg("[Cooking] Bot STOPPED: " + reason);
                this.AddMenuNotification("Auto Cook " + reason, new Color(1f, 0.7f, 0.45f));
            }
        }

        // Token: 0x06000011 RID: 17 RVA: 0x00003D94 File Offset: 0x00001F94
        // Token: 0x0600001E RID: 30 RVA: 0x00009D74 File Offset: 0x00007F74
        private void RunAutoCookLogic()
        {
            bool flag = !this.cookingCleanupMode && this.IsAddButtonVisible();
            if (flag)
            {
                this.cookingCleanupMode = true;
                this.cookingPanelClosed = false;
            }
            bool flag2 = this.cookingCleanupMode;
            if (flag2)
            {
                bool flag3 = !this.cookingPanelClosed;
                if (flag3)
                {
                    GameObject gameObject = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)");
                    bool flag4 = gameObject != null;
                    if (flag4)
                    {
                        string[] array = new string[]
                        {
                            "back@w",
                            "back@btn",
                            "close@btn",
                            "closeBtn@w",
                            "BackBtn",
                            "back_btn"
                        };
                        foreach (string text in array)
                        {
                            Transform transform = gameObject.transform.Find(text);
                            bool flag5 = transform == null;
                            if (flag5)
                            {
                                Button[] array3 = gameObject.GetComponentsInChildren<Button>(true);
                                foreach (Button button in array3)
                                {
                                    bool flag6 = button.name == text && button.gameObject.activeInHierarchy && button.interactable;
                                    if (flag6)
                                    {
                                        button.onClick.Invoke();
                                        this.cookingPanelClosed = true;
                                        this.cookingPanelClosedTime = Time.unscaledTime;
                                        return;
                                    }
                                }
                            }
                            else
                            {
                                Button component = transform.GetComponent<Button>();
                                bool flag7 = component != null && transform.gameObject.activeInHierarchy;
                                if (flag7)
                                {
                                    component.onClick.Invoke();
                                    this.cookingPanelClosed = true;
                                    this.cookingPanelClosedTime = Time.unscaledTime;
                                    break;
                                }
                            }
                        }
                        return;
                    }
                    this.cookingPanelClosed = true;
                    this.cookingPanelClosedTime = Time.unscaledTime;
                }
                bool flag8 = this.ClickCookingCleanup();
                bool flag9 = flag8;
                if (!flag9)
                {
                    float num = Time.unscaledTime - this.cookingPanelClosedTime;
                    bool flag10 = num < 15f;
                    if (!flag10)
                    {
                        this.cookingCleanupMode = false;
                        this.cookingPanelClosed = false;
                        this.autoCookEnabled = false;
                    }
                }
            }
            else
            {
                bool flag12 = false;
                string text2 = "GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)/AniRoot@queueanimation/detail@t/btnBar@go/confirm@swapbtn";
                GameObject gameObject2 = GameObject.Find(text2);
                bool flag13 = gameObject2 != null;
                if (flag13)
                {
                    Button component2 = gameObject2.GetComponent<Button>();
                    bool flag14 = component2 != null && gameObject2.activeInHierarchy && component2.interactable;
                    if (flag14)
                    {
                        component2.onClick.Invoke();
                        this.lastConfirmClickTime = Time.unscaledTime;
                        flag12 = true;
                    }
                }
                this.ClickButtonIfExists("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn");
                this.ClickButtonIfExists("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_cook_danger@list/CommonIconForCookDanger(Clone)/root_visible@go/icon@img@btn");
                bool flag15 = !flag12 && Time.unscaledTime - this.lastConfirmClickTime > 5f;
                if (flag15)
                {
                    GameObject gameObject3 = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)");
                    bool flag16 = gameObject3 != null;
                    if (flag16)
                    {
                        Button[] array5 = gameObject3.GetComponentsInChildren<Button>(true);
                        foreach (Button button2 in array5)
                        {
                            bool flag17 = !button2.gameObject.activeInHierarchy || !button2.interactable;
                            if (!flag17)
                            {
                                string text3 = button2.name.ToLower();
                                bool flag18 = text3.Contains("confirm") || text3.Contains("queue") || text3.Contains("cook") || text3.Contains("start");
                                if (flag18)
                                {
                                    button2.onClick.Invoke();
                                    this.lastConfirmClickTime = Time.unscaledTime;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        private bool ClickCookingCleanup()
        {
            bool result = false;
            bool flag = false;
            string[] array = new string[]
            {
                "ui_common_btn_close",
                "ui_common_close",
                "btn_close"
            };

            this.cookImageScanBuffer.Clear();
            this.CollectImagesFromPath("GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)", this.cookImageScanBuffer);
            this.CollectImagesFromPath("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list", this.cookImageScanBuffer);
            this.CollectImagesFromPath("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_cook_danger@list", this.cookImageScanBuffer);
            if (this.cookImageScanBuffer.Count == 0)
            {
                return false;
            }

            foreach (Image image in this.cookImageScanBuffer)
            {
                if (!(image == null) && !(image.sprite == null) && image.gameObject.activeInHierarchy && image.sprite.name.Contains("ui_cooking_icon_time01"))
                {
                    flag = true;
                }
            }
            foreach (Image image2 in this.cookImageScanBuffer)
            {
                if (!(image2 == null) && !(image2.sprite == null) && image2.gameObject.activeInHierarchy)
                {
                    string name = image2.sprite.name;
                    foreach (string text in array)
                    {
                        if (name.Contains(text))
                        {
                            Button button = image2.GetComponent<Button>();
                            if (button == null)
                            {
                                button = image2.GetComponentInParent<Button>();
                            }
                            if (button != null && button.interactable)
                            {
                                button.onClick.Invoke();
                                return true;
                            }
                        }
                    }
                }
            }
            if (!flag)
            {
                foreach (Image image3 in this.cookImageScanBuffer)
                {
                    if (!(image3 == null) && !(image3.sprite == null) && image3.gameObject.activeInHierarchy)
                    {
                        string name2 = image3.sprite.name;
                        if (name2.Contains("ui_dynamic_interaction_900"))
                        {
                            result = true;
                            Button button2 = image3.GetComponent<Button>();
                            if (button2 == null)
                            {
                                button2 = image3.GetComponentInParent<Button>();
                            }
                            if (button2 != null && button2.interactable)
                            {
                                button2.onClick.Invoke();
                            }
                        }
                        else if (name2.Contains("ui_dynamic_interaction_902"))
                        {
                            result = true;
                            Button button3 = image3.GetComponent<Button>();
                            if (button3 == null)
                            {
                                button3 = image3.GetComponentInParent<Button>();
                            }
                            if (button3 != null && button3.interactable)
                            {
                                button3.onClick.Invoke();
                            }
                        }
                        else if (name2.Contains("ui_cooking_icon_time01"))
                        {
                            result = true;
                        }
                    }
                }
            }
            return result;
        }

        private bool ClickCookingCleanupThrottled(float interval)
        {
            if (Time.unscaledTime < this.nextCookingCleanupScanAt)
            {
                return this.lastCookingCleanupResult;
            }

            this.lastCookingCleanupResult = this.ClickCookingCleanup();
            this.nextCookingCleanupScanAt = Time.unscaledTime + Mathf.Max(0.05f, interval);
            return this.lastCookingCleanupResult;
        }

        private bool IsCurrentCookTimerActive()
        {
            string spriteName = this.GetCurrentCookInteractSpriteName();
            if (string.IsNullOrEmpty(spriteName))
            {
                return false;
            }

            bool timerActive = spriteName.Contains("ui_cooking_icon_time") || spriteName.Contains("cooking_icon_time") || spriteName.Contains("icon_time") || spriteName.Contains("timer") || spriteName.Contains("clock");
            if (timerActive)
            {
                this.lastCookingTimerSeenAt = Time.unscaledTime;
            }
            return timerActive;
        }

        private bool IsCurrentCookTakeoutReady()
        {
            string spriteName = this.GetCurrentCookInteractSpriteName();
            if (string.IsNullOrEmpty(spriteName))
            {
                return false;
            }

            return spriteName.Contains("ui_dynamic_interaction_902") || spriteName.Contains("heart");
        }

        private string GetCurrentCookInteractSpriteName()
        {
            GameObject gameObject = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn");
            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                return string.Empty;
            }

            Image image = gameObject.GetComponent<Image>();
            if (image == null || image.sprite == null)
            {
                return string.Empty;
            }

            return image.sprite.name.ToLowerInvariant();
        }

        private void ClickCookInteractSafely()
        {
            GameObject gameObject = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn");
            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                return;
            }

            Image image = gameObject.GetComponent<Image>();
            if (image == null || image.sprite == null)
            {
                return;
            }

            string spriteName = image.sprite.name.ToLowerInvariant();
            bool timerActive = spriteName.Contains("ui_cooking_icon_time") || spriteName.Contains("cooking_icon_time") || spriteName.Contains("icon_time") || spriteName.Contains("timer") || spriteName.Contains("clock");
            if (timerActive)
            {
                this.lastCookingTimerSeenAt = Time.unscaledTime;
                return;
            }

            // While cook panel is open, avoid clicking generic interact icon.
            // Panel actions are handled by confirm/danger/cleanup buttons.
            if (this.IsCookPanelOpen())
            {
                return;
            }

            bool isTakeout = spriteName.Contains("ui_dynamic_interaction_902") || spriteName.Contains("heart");
            // Allow unknown non-timer/non-takeout interact icons for stove entry/start.
            if (isTakeout)
            {
                return;
            }

            bool isGlove = spriteName.Contains("ui_dynamic_interaction_902");
            bool timerRecentlySeen = (Time.unscaledTime - this.lastCookingTimerSeenAt) < this.GetCookTakeoutSafetyDelay();
            if (isGlove && timerRecentlySeen)
            {
                return;
            }

            Button button = gameObject.GetComponent<Button>();
            if (button != null && button.interactable)
            {
                button.onClick.Invoke();
            }
        }

        private void ClickCookPanelCloseIfOpen()
        {
            GameObject cookPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)");
            if (cookPanel == null || !cookPanel.activeInHierarchy)
            {
                return;
            }

            Button[] buttons = cookPanel.GetComponentsInChildren<Button>(true);
            if (buttons == null)
            {
                return;
            }

            foreach (Button btn in buttons)
            {
                if (btn == null || btn.gameObject == null || !btn.gameObject.activeInHierarchy || !btn.interactable)
                {
                    continue;
                }

                string n = btn.name.ToLowerInvariant();
                if (n.Contains("close") || n.Contains("back") || n.Contains("exit") || n.Contains("return"))
                {
                    btn.onClick.Invoke();
                    return;
                }
            }
        }

        private bool IsCookPanelOpen()
        {
            GameObject cookPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)");
            return cookPanel != null && cookPanel.activeInHierarchy;
        }

        private void ClickCookRefreshButtonIfAvailable()
        {
            if (Time.unscaledTime - this.lastCookRefreshClickAt < 0.2f)
            {
                return;
            }

            GameObject cookPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)");
            if (cookPanel == null || !cookPanel.activeInHierarchy)
            {
                return;
            }

            Button[] buttons = cookPanel.GetComponentsInChildren<Button>(true);
            if (buttons == null || buttons.Length == 0)
            {
                return;
            }

            foreach (Button btn in buttons)
            {
                if (btn == null || btn.gameObject == null || !btn.gameObject.activeInHierarchy)
                {
                    continue;
                }

                string name = btn.name.ToLowerInvariant();
                if (name.Contains("refresh@btn") && btn.interactable)
                {
                    btn.onClick.Invoke();
                    this.lastCookRefreshClickAt = Time.unscaledTime;
                    return;
                }
            }
        }

        private void ClickCookConfirmButtonIfAvailable()
        {
            if (Time.unscaledTime - this.lastCookConfirmClickAt < 0.3f)
            {
                return;
            }

            // Avoid hammering Start Cooking while current stove already has active timer.
            if (this.IsCurrentCookTimerActive())
            {
                return;
            }

            if (this.ClickButtonIfExistsReturn("GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)/AniRoot@queueanimation/detail@t/btnBar@go/confirm@swapbtn"))
            {
                this.lastCookConfirmClickAt = Time.unscaledTime;
            }
        }

        private void ClickCookDangerButtonIfAvailable()
        {
            if (Time.unscaledTime - this.lastCookRefreshClickAt < 0.3f)
            {
                return;
            }

            if (this.ClickButtonIfExistsReturn("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_cook_danger@list/CommonIconForCookDanger(Clone)/root_visible@go/icon@img@btn"))
            {
                this.lastCookRefreshClickAt = Time.unscaledTime;
            }
        }

        private float GetCookTakeoutSafetyDelay()
        {
            return Mathf.Max(this.cookingTakeoutSafetyDelay, this.gameSpeed * 0.06f);
        }

        private int GetAutoCookAutoStopSeconds()
        {
            return Math.Max(0, this.autoCookAutoStopHours) * 3600
                + Math.Max(0, this.autoCookAutoStopMinutes) * 60
                + Math.Max(0, this.autoCookAutoStopSeconds);
        }

        private System.Collections.IEnumerator CookingPatrolRoutine()
        {
            int index = 0;
            while (isCookingPatrolActive && cookingPatrolEnabled)
            {
                if (cookingPatrolPoints.Count == 0) break;

                CookingPatrolPoint point = cookingPatrolPoints[index];

                // 1. TELEPORT to location
                TeleportTo(point.Position.ToVector3());

                // 2. APPLY CHARACTER ROTATION
                Quaternion targetRotation = point.Rotation.ToQuaternion();
                this.EnsureRotationOverridePatched();
                HeartopiaComplete.OverridePlayerRotation = true;
                HeartopiaComplete.PlayerOverrideRot = targetRotation;
                this.playerRotationFramesRemaining = 100;

                // 3. WAIT at spot
                yield return new WaitForSecondsRealtime(cookingWaitAtSpot);

                // 4. Disable rotation override before moving to next point
                HeartopiaComplete.OverridePlayerRotation = false;
                this.playerRotationFramesRemaining = 0;

                // 5. NEXT POINT
                index++;
                if (index >= cookingPatrolPoints.Count) index = 0;
            }
            isCookingPatrolActive = false;
            HeartopiaComplete.OverridePlayerRotation = false;
        }

        private bool TryClickVisibleCookingStoreItemByMatch(Transform content, string match)
        {
            if (content == null) return false;
            string[] matchCandidates = LocalizationManager.GetTranslationCandidates(match);
            for (int i = 0; i < content.childCount; i++)
            {
                Transform gw = content.GetChild(i);
                if (gw == null) continue;
                // primary reliable name path
                Transform nameT = gw.Find("AniRoot@ani/info@group/titleName/name@txt") ?? gw.Find("AniRoot@ani/info@group/titleName/titleNameNormal_img");
                string txtVal = null;
                if (nameT != null)
                {
                    Text t = nameT.GetComponent<Text>();
                    if (t != null) txtVal = t.text;
                }
                // fallback: any child Text
                if (string.IsNullOrEmpty(txtVal))
                {
                    var any = gw.GetComponentInChildren<Text>(true);
                    if (any != null) txtVal = any.text;
                }
                bool isMatch = false;
                if (!string.IsNullOrEmpty(txtVal))
                {
                    for (int c = 0; c < matchCandidates.Length; c++)
                    {
                        string candidate = matchCandidates[c];
                        if (!string.IsNullOrWhiteSpace(candidate) && txtVal.IndexOf(candidate, StringComparison.InvariantCultureIgnoreCase) >= 0)
                        {
                            isMatch = true;
                            break;
                        }
                    }
                }

                if (isMatch)
                {
                    // Only click if this is a gold currency item (not star currency)
                    if (!IsGoldCurrencyItem(gw))
                    {
                        if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] Skipping {txtVal} - not gold currency (star/diamond item)"); }
                        continue;
                    }
                    Transform card = gw.Find("AniRoot@ani/card@btn") ?? gw.Find("card@btn");
                    if (card != null)
                    {
                        var b = card.GetComponent<Button>() ?? card.GetComponentInChildren<Button>(true);
                        if (b != null && b.interactable) { b.onClick.Invoke(); if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] Clicked item {txtVal}"); } return true; }
                        if (SimulateClick(card.gameObject)) { if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] SimClicked item {txtVal}"); } return true; }
                    }
                }
            }
            return false;
        }

        private static void SetCookingStoreScrollPosition(ScrollRect scrollRect, float normalized)
        {
            if (scrollRect == null) return;
            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(normalized);
        }

        private bool ClickCookingStoreItemByMatch(string match)
        {
            try
            {
                GameObject shop = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ShopPanel(Clone)");
                if (shop == null) return false;
                Transform scrollTransform = shop.transform.Find("goods@scroll");
                Transform content = shop.transform.Find("goods@scroll/Content");
                if (content == null) return false;
                // if content exists but not populated yet, log and return false so caller can retry
                if (content.childCount == 0)
                {
                    LogAutoBuy(" Shop content empty - will retry shortly");
                    return false;
                }

                ScrollRect scrollRect = null;
                if (scrollTransform != null)
                {
                    scrollRect = scrollTransform.GetComponent<ScrollRect>() ?? scrollTransform.GetComponentInChildren<ScrollRect>(true);
                }

                if (TryClickVisibleCookingStoreItemByMatch(content, match)) return true;

                if (scrollRect != null)
                {
                    const int scrollSteps = 12;
                    if (this.autoBuyShopScrollStep < 0)
                    {
                        this.autoBuyShopScrollStep = 0;
                        SetCookingStoreScrollPosition(scrollRect, 1f);
                        return false;
                    }
                    else if (this.autoBuyShopScrollStep < scrollSteps)
                    {
                        this.autoBuyShopScrollStep++;
                        float normalized = 1f - ((float)this.autoBuyShopScrollStep / (float)scrollSteps);
                        SetCookingStoreScrollPosition(scrollRect, normalized);
                        return false;
                    }
                }
            }
            catch (Exception ex) { LogAutoBuy(" ClickCookingStoreItemByMatch error: " + ex.Message); }
            return false;
        }

        private string GetCookingPatrolSaveDirectory()
        {
            return HelperPaths.GetDirectory("cooking_patrol_saves");
        }

        private string SanitizeCookingPatrolSaveName(string name)
        {
            string candidate = string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                candidate = candidate.Replace(c, '_');
            }
            return string.IsNullOrWhiteSpace(candidate) ? "" : candidate;
        }

        private string GetCookingPatrolPath(string saveName = null)
        {
            string safeName = this.SanitizeCookingPatrolSaveName(saveName ?? this.cookingPatrolSaveName);
            return Path.Combine(this.GetCookingPatrolSaveDirectory(), safeName + ".json");
        }

        private List<string> GetCookingPatrolSaveNames()
        {
            UnifiedConfigData config = this.LoadUnifiedConfig();
            if (config != null)
            {
                List<string> configSaves = new List<string>();
                foreach (NamedCookingPatrolSave save in config.CookingPatrolSaves)
                {
                    string name = this.SanitizeCookingPatrolSaveName(save?.Name);
                    if (!string.IsNullOrWhiteSpace(name) && !configSaves.Contains(name))
                    {
                        configSaves.Add(name);
                    }
                }
                configSaves.Sort(StringComparer.OrdinalIgnoreCase);
                return configSaves;
            }
            List<string> saves = new List<string>();
            try
            {
                string dir = this.GetCookingPatrolSaveDirectory();
                string[] files = Directory.GetFiles(dir, "*.json");
                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrWhiteSpace(name) && !saves.Contains(name))
                    {
                        saves.Add(name);
                    }
                    if (!this.customDisplayIdEnabled && !string.IsNullOrEmpty(this.customDisplayId))
                    {
                        this.customDisplayIdEnabled = true;
                    }
                }
            }
            catch
            {
            }
            saves.Sort(StringComparer.OrdinalIgnoreCase);
            return saves;
        }

        private bool DeleteCookingPatrolSave(string saveName)
        {
            string safeName = this.SanitizeCookingPatrolSaveName(saveName);
            if (string.IsNullOrEmpty(safeName))
            {
                this.AddMenuNotification("Enter a save name to delete", new Color(1f, 0.5f, 0.5f));
                return false;
            }

            try
            {
                UnifiedConfigData config = this.LoadUnifiedConfig();
                if (config != null)
                {
                    int removed = config.CookingPatrolSaves.RemoveAll(s => string.Equals(this.SanitizeCookingPatrolSaveName(s?.Name), safeName, StringComparison.OrdinalIgnoreCase));
                    if (removed <= 0)
                    {
                        this.AddMenuNotification($"Save not found: {safeName}", new Color(1f, 0.5f, 0.5f));
                        return false;
                    }
                    this.SaveUnifiedConfig(config);
                    ModLogger.Msg($"Deleted cooking patrol save '{safeName}'.");
                    this.AddMenuNotification($"Deleted: {safeName}", new Color(1f, 0.75f, 0.45f));
                    return true;
                }
                string path = this.GetCookingPatrolPath(safeName);
                if (!File.Exists(path))
                {
                    this.AddMenuNotification($"Save not found: {safeName}", new Color(1f, 0.5f, 0.5f));
                    return false;
                }
                File.Delete(path);
                ModLogger.Msg($"Deleted cooking patrol save '{safeName}'.");
                this.AddMenuNotification($"Deleted: {safeName}", new Color(1f, 0.75f, 0.45f));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error deleting cooking patrol save: " + ex.Message);
                this.AddMenuNotification("Failed to delete patrol save", new Color(1f, 0.4f, 0.4f));
                return false;
            }
        }

        private void SaveCookingPatrolPoints(string saveName = null)
        {
            try
            {
                string safeName = this.SanitizeCookingPatrolSaveName(saveName ?? this.cookingPatrolSaveName);
                if (string.IsNullOrEmpty(safeName))
                {
                    this.AddMenuNotification("Enter a save name before saving", new Color(1f, 0.5f, 0.5f));
                    return;
                }
                this.cookingPatrolSaveName = safeName;
                UnifiedConfigData config = this.LoadOrCreateUnifiedConfig();
                this.PopulateAllConfigSections(config);
                this.SaveUnifiedConfig(config);
                ModLogger.Msg($"Cooking patrol points saved to '{safeName}'! ({cookingPatrolPoints.Count} points with rotations)");
                this.AddMenuNotification($"Cooking patrol saved: {safeName}", new Color(0.55f, 0.88f, 1f));
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error saving cooking patrol points: " + ex.Message);
                this.AddMenuNotification("Failed to save cooking patrol", new Color(1f, 0.4f, 0.4f));
            }
        }

        private void LoadCookingPatrolPoints(string saveName = null)
        {
            try
            {
                string safeName = this.SanitizeCookingPatrolSaveName(saveName ?? this.cookingPatrolSaveName);
                if (string.IsNullOrEmpty(safeName))
                {
                    this.AddMenuNotification("Enter a save name before loading", new Color(1f, 0.5f, 0.5f));
                    return;
                }
                this.cookingPatrolSaveName = safeName;
                UnifiedConfigData config = this.LoadUnifiedConfig();
                if (config != null)
                {
                    NamedCookingPatrolSave save = config.CookingPatrolSaves.FirstOrDefault(s => string.Equals(this.SanitizeCookingPatrolSaveName(s?.Name), safeName, StringComparison.OrdinalIgnoreCase));
                    if (save == null)
                    {
                        ModLogger.Msg($"Cooking patrol slot '{safeName}' not found.");
                        this.AddMenuNotification($"Patrol save not found: {safeName}", new Color(1f, 0.5f, 0.5f));
                        return;
                    }
                    cookingPatrolPoints.Clear();
                    foreach (CookingPatrolPoint point in save.Points)
                    {
                        if (point != null) cookingPatrolPoints.Add(point);
                    }
                    ModLogger.Msg($"Loaded {cookingPatrolPoints.Count} cooking patrol points from '{safeName}' (with rotations: true).");
                    this.AddMenuNotification($"Cooking patrol loaded: {safeName}", new Color(0.55f, 0.88f, 1f));
                    return;
                }
                string path = this.GetCookingPatrolPath(safeName);
                if (!File.Exists(path))
                {
                    ModLogger.Msg($"Cooking patrol slot '{safeName}' not found.");
                    this.AddMenuNotification($"Patrol save not found: {safeName}", new Color(1f, 0.5f, 0.5f));
                    return;
                }
                cookingPatrolPoints.Clear();
                string json = File.ReadAllText(path);

                // Check if this is the new format (with Rotation) or old format (just Vector3)
                bool hasRotation = json.Contains("\"Rotation\"");

                // Parse JSON manually - find all coordinate blocks
                int pointsStart = json.IndexOf("[");
                int pointsEnd = json.LastIndexOf("]");
                if (pointsStart == -1 || pointsEnd == -1) return;

                string pointsSection = json.Substring(pointsStart + 1, pointsEnd - pointsStart - 1);

                if (hasRotation)
                {
                    // New format with Position and Rotation
                    string[] pointBlocks = pointsSection.Split(new string[] { "    }" }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string block in pointBlocks)
                    {
                        if (!block.Contains("Position")) continue;

                        // Extract Position block (between "Position": { and })
                        int posStart = block.IndexOf("\"Position\"");
                        if (posStart == -1) continue;
                        int posObjStart = block.IndexOf("{", posStart);
                        int posObjEnd = block.IndexOf("}", posObjStart);
                        string posBlock = block.Substring(posObjStart, posObjEnd - posObjStart + 1);

                        float px = ExtractCoordinate(posBlock, "x");
                        float py = ExtractCoordinate(posBlock, "y");
                        float pz = ExtractCoordinate(posBlock, "z");

                        // Extract Rotation block (between "Rotation": { and })
                        int rotStart = block.IndexOf("\"Rotation\"");
                        Quaternion rotation = Quaternion.identity;
                        if (rotStart != -1)
                        {
                            int rotObjStart = block.IndexOf("{", rotStart);
                            int rotObjEnd = block.IndexOf("}", rotObjStart);
                            string rotBlock = block.Substring(rotObjStart, rotObjEnd - rotObjStart + 1);

                            float rx = ExtractCoordinate(rotBlock, "x");
                            float ry = ExtractCoordinate(rotBlock, "y");
                            float rz = ExtractCoordinate(rotBlock, "z");
                            float rw = ExtractCoordinate(rotBlock, "w");
                            rotation = new Quaternion(rx, ry, rz, rw);
                        }

                        cookingPatrolPoints.Add(new CookingPatrolPoint(new Vector3(px, py, pz), rotation));
                    }
                }
                else
                {
                    // Old format - just Vector3 positions, use default rotation
                    string[] pointBlocks = pointsSection.Split(new string[] { "}," }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string block in pointBlocks)
                    {
                        string cleanBlock = block.Trim().Trim('{').Trim('}').Trim(',');
                        if (string.IsNullOrEmpty(cleanBlock)) continue;

                        // Extract x, y, z values
                        float x = ExtractCoordinate(cleanBlock, "\"x\"");
                        float y = ExtractCoordinate(cleanBlock, "\"y\"");
                        float z = ExtractCoordinate(cleanBlock, "\"z\"");

                        cookingPatrolPoints.Add(new CookingPatrolPoint(new Vector3(x, y, z), Quaternion.identity));
                    }
                }
                ModLogger.Msg($"Loaded {cookingPatrolPoints.Count} cooking patrol points from '{safeName}' (with rotations: {hasRotation}).");
                this.AddMenuNotification($"Cooking patrol loaded: {safeName}", new Color(0.55f, 0.88f, 1f));
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error loading cooking patrol points: " + ex.Message);
                this.AddMenuNotification("Failed to load cooking patrol", new Color(1f, 0.4f, 0.4f));
            }
        }

        private struct NetCookIngredientRequirement
        {
            public int StaticId;
            public int CountPerDish;
        }

        private sealed class NetCookTargetContext
        {
            public uint CookerNetId;
            public int CookerStaticId;
            public int CookerType;
            public ulong LevelObjectNetId;
            public int Phase;
            public int ContinuePulses;
            public int SentCount;
            public int LastStatus = -1;
            public int IdleRetries;
            public float LastStatusActionAt = -999f;
            public float LastCookCommandAt = -999f;
            public float NextActionAt;
            public bool HasWorldPosition;
            public Vector3 WorldPosition;
        }

        private sealed class NetCookRegisteredWorldCooker
        {
            public uint OwnerNetId;
            public int ResourceId;
            public int StaticId;
            public int CookerType;
        }

    }
}
