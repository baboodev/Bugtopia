using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

using Il2CppObject = Il2CppSystem.Object;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const bool PetPlayLogsEnabled = MasterLogPetPlay;

        private bool petPlayAutoCatEnabled = false;
        private bool petPlayAutoDogEnabled = false;
        private bool petPlayRuntimeReadyLogged = false;
        private float petPlayNextResolverProbeAt = 0f;
        private float petPlayNextAutoTickAt = 0f;
        private float petPlayNextHeartbeatAt = 0f;
        private int petPlayCatAnswerCount = 0;
        private int petPlayDogAnswerCount = 0;
        private MethodInfo petPlayMeowTeaseQteMethod = null;
        private MethodInfo petPlayDogTeaseQteMethod = null;
        private IntPtr petPlayAuraMeowTeaseQteMethod = IntPtr.Zero;
        private IntPtr petPlayAuraDogTeaseQteMethod = IntPtr.Zero;
        private uint petPlayLastCatNetId = 0U;
        private int petPlayLastCatQte = -1;
        private string petPlayLastCatSprite = string.Empty;
        private IntPtr petPlayLastCatQuestionCell = IntPtr.Zero;
        private float petPlayLastCatAnswerAt = -999f;
        private uint petPlayLastDogNetId = 0U;
        private int petPlayLastDogRound = -1;
        private float petPlayLastDogAnswerAt = -999f;
        private float petPlayNextDogRoundScanAt = 0f;
        private float petPlayNextPanelInvokeFailureLogAt = 0f;
        private float petPlayNextActiveQuestionFailureLogAt = 0f;
        private float petPlayNextCatQuestionScanAt = 0f;

        private float DrawPetPlayTab(int startY)
        {
            float num = startY;
            const float left = 40f;
            const float width = 520f;

            Color textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            headerStyle.normal.textColor = Color.white;

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            labelStyle.normal.textColor = textColor;

            GUI.Label(new Rect(left, num, width, 30f), "PET CARE", headerStyle);
            num += 42;

            Rect trainRect = new Rect(left, num, width, 118f);
            GUI.Box(trainRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(trainRect, 1f);
            GUI.Label(new Rect(trainRect.x + 16f, trainRect.y + 12f, 180f, 20f), "TRAINING", labelStyle);

            float rowY = trainRect.y + 40f;
            bool nextCat = this.DrawSwitchToggle(new Rect(trainRect.x + 16f, rowY, 250f, 28f), this.petPlayAutoCatEnabled, "Auto Cat Play");
            if (nextCat != this.petPlayAutoCatEnabled)
            {
                this.petPlayAutoCatEnabled = nextCat;
                this.PetPlayLog("Cat play " + (nextCat ? "enabled" : "disabled"));
            }

            rowY += 42f;
            bool nextDog = this.DrawSwitchToggle(new Rect(trainRect.x + 16f, rowY, 250f, 28f), this.petPlayAutoDogEnabled, "Auto Dog Train");
            if (nextDog != this.petPlayAutoDogEnabled)
            {
                this.petPlayAutoDogEnabled = nextDog;
                this.PetPlayLog("Dog train " + (nextDog ? "enabled" : "disabled"));
            }

            num += 132;
            int petFoodOptionCount = this.GetPetFeedFoodDropdownOptionCount();
            this.ClampPetFeedFoodDropdownScrollIndex();
            int visibleFoodRows = this.petFeedFoodDropdownOpen ? Math.Min(PetFeedFoodVisibleRows, petFoodOptionCount) : 0;
            const float petFoodOptionHeight = 36f;
            const float petFoodSearchHeight = 34f;
            float foodSelectorHeight = this.petFeedFoodDropdownOpen
                ? petFoodSearchHeight + (PetFeedFoodVisibleRows + 1) * petFoodOptionHeight + 114f
                : 86f;
            Rect feedRect = new Rect(left, num, width, foodSelectorHeight);
            GUI.Box(feedRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(feedRect, 1f);
            GUI.Label(new Rect(feedRect.x + 16f, feedRect.y + 12f, 180f, 20f), "PET FOOD", labelStyle);

            GUIStyle dropdownValueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            dropdownValueStyle.normal.textColor = Color.white;

            GUIStyle dropdownArrowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            dropdownArrowStyle.normal.textColor = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);

            GUIStyle optionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                wordWrap = false
            };
            optionStyle.normal.textColor = textColor;
            GUIStyle optionActiveStyle = new GUIStyle(optionStyle);
            optionActiveStyle.normal.textColor = Color.white;

            float foodY = feedRect.y + 40f;
            GUI.Label(new Rect(feedRect.x + 16f, foodY + 5f, 72f, 20f), "Pet Food", labelStyle);
            Rect foodDropdownRect = new Rect(feedRect.x + 92f, foodY, feedRect.width - 224f, 28f);
            GUI.Box(foodDropdownRect, string.Empty, this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(foodDropdownRect, 1f);
            if (GUI.Button(foodDropdownRect, string.Empty, GUIStyle.none))
            {
                this.petFeedFoodDropdownOpen = !this.petFeedFoodDropdownOpen;
            }
            float selectedLabelX = foodDropdownRect.x + 10f;
            float selectedLabelWidth = foodDropdownRect.width - 34f;
            if (this.petFeedSelectedFoodStaticId > 0 && this.TryGetPetFeedFoodIconTexture(this.petFeedSelectedFoodStaticId, out Texture2D selectedFoodIcon) && selectedFoodIcon != null)
            {
                Rect selectedIconRect = new Rect(foodDropdownRect.x + 7f, foodDropdownRect.y + 4f, 20f, 20f);
                GUI.DrawTexture(selectedIconRect, selectedFoodIcon, ScaleMode.ScaleToFit, true);
                selectedLabelX += 24f;
                selectedLabelWidth -= 24f;
            }
            GUI.Label(new Rect(selectedLabelX, foodDropdownRect.y + 1f, selectedLabelWidth, foodDropdownRect.height - 2f), this.GetPetFeedSelectedFoodLabel(), dropdownValueStyle);
            GUI.Label(new Rect(foodDropdownRect.xMax - 22f, foodDropdownRect.y + 1f, 14f, foodDropdownRect.height - 2f), this.petFeedFoodDropdownOpen ? "^" : "v", dropdownArrowStyle);

            bool canScanPetFood = !this.petFeedFoodScanInProgress && Time.realtimeSinceStartup >= this.petFeedNextFoodScanAllowedAt;
            GUI.enabled = canScanPetFood;
            if (GUI.Button(new Rect(feedRect.xMax - 116f, foodY, 100f, 28f), canScanPetFood ? "Scan Food" : "Wait...", this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.RefreshPetFeedFoodOptions();
            }
            GUI.enabled = true;

            if (this.petFeedFoodDropdownOpen)
            {
                float optionHeight = petFoodOptionHeight;
                Rect panelRect = new Rect(feedRect.x + 16f, foodDropdownRect.yMax + 8f, feedRect.width - 32f, petFoodSearchHeight + (PetFeedFoodVisibleRows + 1) * optionHeight + 14f);
                GUI.Box(panelRect, string.Empty, this.themeContentStyle ?? this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(panelRect, 1f);

                Event currentEvent = Event.current;
                if (currentEvent != null && currentEvent.type == EventType.ScrollWheel && panelRect.Contains(currentEvent.mousePosition))
                {
                    this.ScrollPetFeedFoodDropdown(currentEvent.delta.y > 0f ? 1 : -1);
                    currentEvent.Use();
                }

                Rect searchRect = new Rect(panelRect.x + 8f, panelRect.y + 6f, panelRect.width - 34f, 26f);
                GUI.Box(searchRect, string.Empty, this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(searchRect, 1f);
                string previousSearch = this.petFeedFoodSearchText ?? string.Empty;
                this.petFeedFoodSearchText = GUI.TextField(searchRect, previousSearch, 64);
                if (string.IsNullOrEmpty(this.petFeedFoodSearchText) && string.IsNullOrEmpty(previousSearch))
                {
                    GUIStyle placeholderStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 12,
                        fontStyle = FontStyle.Italic,
                        alignment = TextAnchor.MiddleLeft,
                        clipping = TextClipping.Clip
                    };
                    placeholderStyle.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, 0.48f);
                    GUI.Label(new Rect(searchRect.x + 9f, searchRect.y + 1f, searchRect.width - 18f, searchRect.height - 2f), "Search pet food...", placeholderStyle);
                }
                if (!string.Equals(previousSearch, this.petFeedFoodSearchText ?? string.Empty, StringComparison.Ordinal))
                {
                    this.petFeedFoodDropdownScrollIndex = 0;
                    this.petFeedFoodScrollbarDragging = false;
                }

                List<PetFeedFoodOption> visibleFoodOptions = this.GetPetFeedFoodDropdownOptions();
                petFoodOptionCount = visibleFoodOptions.Count;
                visibleFoodRows = Math.Min(PetFeedFoodVisibleRows, petFoodOptionCount);
                this.ClampPetFeedFoodDropdownScrollIndex();

                bool canScrollUp = this.petFeedFoodDropdownScrollIndex > 0;
                bool canScrollDown = this.petFeedFoodDropdownScrollIndex + visibleFoodRows < petFoodOptionCount;

                Rect listRect = new Rect(panelRect.x + 4f, panelRect.y + petFoodSearchHeight + 6f, panelRect.width - 26f, panelRect.height - petFoodSearchHeight - 10f);
                Rect anyRect = new Rect(listRect.x, listRect.y, listRect.width, optionHeight);
                bool anySelected = this.petFeedSelectedFoodStaticId <= 0;
                GUI.Box(anyRect, string.Empty, anySelected ? (this.themeTopTabActiveStyle ?? this.themePrimaryButtonStyle ?? GUI.skin.box) : GUIStyle.none);
                if (GUI.Button(anyRect, string.Empty, GUIStyle.none))
                {
                    this.SelectPetFeedFood(0, "Any Food");
                }
                GUI.Label(new Rect(anyRect.x + 42f, anyRect.y, anyRect.width - 50f, anyRect.height), "Any Food", anySelected ? optionActiveStyle : optionStyle);

                for (int row = 0; row < visibleFoodRows; row++)
                {
                    int optionIndex = this.petFeedFoodDropdownScrollIndex + row;
                    if (optionIndex < 0 || optionIndex >= visibleFoodOptions.Count)
                    {
                        continue;
                    }

                    PetFeedFoodOption option = visibleFoodOptions[optionIndex];
                    if (option == null)
                    {
                        continue;
                    }

                    Rect optionRect = new Rect(listRect.x, listRect.y + (row + 1) * optionHeight, listRect.width, optionHeight);
                    bool isSelected = option.StaticId == this.petFeedSelectedFoodStaticId;
                    GUI.Box(optionRect, string.Empty, isSelected ? (this.themeTopTabActiveStyle ?? this.themePrimaryButtonStyle ?? GUI.skin.box) : GUIStyle.none);
                    if (GUI.Button(optionRect, string.Empty, GUIStyle.none))
                    {
                        this.SelectPetFeedFood(option.StaticId, option.Name);
                    }

                    string optionLabel = this.GetPetFeedFoodDisplayName(option.StaticId, option.Name);
                    float labelX = optionRect.x + 42f;
                    float labelWidth = optionRect.width - 88f;
                    if (this.TryGetPetFeedFoodIconTexture(option.StaticId, out Texture2D foodIcon) && foodIcon != null)
                    {
                        Rect iconRect = new Rect(optionRect.x + 10f, optionRect.y + 6f, 24f, 24f);
                        GUI.DrawTexture(iconRect, foodIcon, ScaleMode.ScaleToFit, true);
                    }
                    GUI.Label(new Rect(labelX, optionRect.y + 2f, labelWidth, optionRect.height - 4f), optionLabel, isSelected ? optionActiveStyle : optionStyle);
                    GUI.Label(new Rect(optionRect.xMax - 42f, optionRect.y + 2f, 36f, optionRect.height - 4f), "x" + option.Count, dropdownArrowStyle);
                }

                if (petFoodOptionCount > PetFeedFoodVisibleRows)
                {
                    Rect scrollTrackRect = new Rect(panelRect.xMax - 18f, listRect.y + 2f, 8f, listRect.height - 4f);
                    GUI.Box(scrollTrackRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
                    int maxScroll = Math.Max(1, petFoodOptionCount - PetFeedFoodVisibleRows);
                    float thumbHeight = Mathf.Max(32f, scrollTrackRect.height * (PetFeedFoodVisibleRows / (float)Math.Max(PetFeedFoodVisibleRows, petFoodOptionCount)));
                    float thumbY = scrollTrackRect.y + (scrollTrackRect.height - thumbHeight) * (this.petFeedFoodDropdownScrollIndex / (float)maxScroll);
                    Rect scrollThumbRect = new Rect(scrollTrackRect.x, thumbY, scrollTrackRect.width, thumbHeight);
                    GUI.Box(scrollThumbRect, string.Empty, this.themePrimaryButtonStyle ?? GUI.skin.box);

                    if (currentEvent != null)
                    {
                        if (currentEvent.type == EventType.MouseDown && scrollTrackRect.Contains(currentEvent.mousePosition))
                        {
                            this.petFeedFoodScrollbarDragging = true;
                            this.petFeedFoodScrollbarDragOffset = scrollThumbRect.Contains(currentEvent.mousePosition)
                                ? currentEvent.mousePosition.y - scrollThumbRect.y
                                : thumbHeight * 0.5f;
                            this.SetPetFeedFoodDropdownScrollIndexFromTrack(currentEvent.mousePosition.y, scrollTrackRect, thumbHeight, petFoodOptionCount);
                            currentEvent.Use();
                        }
                        else if (currentEvent.type == EventType.MouseDrag && this.petFeedFoodScrollbarDragging)
                        {
                            this.SetPetFeedFoodDropdownScrollIndexFromTrack(currentEvent.mousePosition.y, scrollTrackRect, thumbHeight, petFoodOptionCount);
                            currentEvent.Use();
                        }
                        else if (currentEvent.rawType == EventType.MouseUp)
                        {
                            this.petFeedFoodScrollbarDragging = false;
                        }
                    }

                    Rect upRect = new Rect(panelRect.xMax - 44f, panelRect.y + 6f, 22f, 22f);
                    Rect downRect = new Rect(panelRect.xMax - 44f, panelRect.yMax - 28f, 22f, 22f);
                    GUI.enabled = canScrollUp;
                    if (GUI.Button(upRect, "^", this.themeTopTabStyle ?? GUI.skin.button))
                    {
                        this.ScrollPetFeedFoodDropdown(-1);
                    }
                    GUI.enabled = canScrollDown;
                    if (GUI.Button(downRect, "v", this.themeTopTabStyle ?? GUI.skin.button))
                    {
                        this.ScrollPetFeedFoodDropdown(1);
                    }
                    GUI.enabled = true;
                }
            }

            num += Mathf.CeilToInt(foodSelectorHeight + 14f);

            Rect feedActionRect = new Rect(left, num, width, 82f);
            GUI.Box(feedActionRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(feedActionRect, 1f);
            GUI.Label(new Rect(feedActionRect.x + 16f, feedActionRect.y + 12f, 180f, 20f), "FEEDING", labelStyle);

            float buttonY = feedActionRect.y + 38f;
            bool petFeedBusy = this.petFeedAllCoroutine != null || Time.realtimeSinceStartup < this.petFeedAllBusyUntil;
            GUI.enabled = !petFeedBusy;
            if (GUI.Button(new Rect(feedActionRect.x + 16f, buttonY, 150f, 32f), this.L("Feed All Cats"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartPetFeedAll(false);
            }

            if (GUI.Button(new Rect(feedActionRect.x + 180f, buttonY, 150f, 32f), this.L("Feed All Dogs"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartPetFeedAll(true);
            }
            GUI.enabled = true;

            num += Mathf.CeilToInt(feedActionRect.height + 18f);
            return num + 40f;
        }

        private void EnsurePetPlayRuntimePatches()
        {
            bool catQteVisible = this.petPlayAutoCatEnabled && Time.unscaledTime < this.petPlayLastCatAnswerAt + 1.2f;
            bool dogQteVisible = this.petPlayAutoDogEnabled && Time.unscaledTime < this.petPlayLastDogAnswerAt + 1.2f;
            bool active = catQteVisible || dogQteVisible;
            if (!active && (this.petPlayRuntimeReadyLogged || Time.unscaledTime < 18f))
            {
                return;
            }

            if (Time.unscaledTime < this.petPlayNextResolverProbeAt)
            {
                return;
            }

            this.petPlayNextResolverProbeAt = Time.unscaledTime + (active ? 2f : 8f);

            try
            {
                bool catReady = !this.petPlayAutoCatEnabled;
                string catStatus = "Cat auto play idle.";
                if (catQteVisible)
                {
                    catReady = true;
                    catStatus = "Cat question-state active.";
                }

                bool dogReady = !this.petPlayAutoDogEnabled;
                string dogStatus = "Dog auto play idle.";
                if (dogQteVisible)
                {
                    dogReady = this.EnsureAuraMonoDogTeaseQteMethod(out dogStatus);
                }

                bool ready = catReady && dogReady;
                if (!ready || ready != this.petPlayRuntimeReadyLogged)
                {
                    this.LogPetPlayResolverProbe(catStatus, dogStatus);
                }

                this.petPlayRuntimeReadyLogged = ready;
            }
            catch (Exception ex)
            {
                this.PetPlayLog("Runtime probe error: " + (ex.InnerException ?? ex).Message);
            }
        }

        private void UpdatePetPlayAutomation()
        {
            if (!this.petPlayAutoCatEnabled && !this.petPlayAutoDogEnabled)
            {
                return;
            }

            if (Time.unscaledTime < this.petPlayNextAutoTickAt)
            {
                return;
            }

            this.petPlayNextAutoTickAt = Time.unscaledTime + 0.12f;

            if (Time.unscaledTime >= this.petPlayNextHeartbeatAt)
            {
                this.petPlayNextHeartbeatAt = Time.unscaledTime + 3f;
                this.PetPlayLog("Auto tick. cat=" + this.petPlayAutoCatEnabled
                    + " dogTrain=" + this.petPlayAutoDogEnabled);
            }

            if (this.petPlayAutoCatEnabled)
            {
                this.TryAutoAnswerCatPlayFromQuestionState();
            }

            if (this.petPlayAutoDogEnabled)
            {
                this.TryAutoAnswerDogPlayFromUi();
            }
        }

        private bool TryInvokeCatTeaseQte(uint catNetId, int qteValue)
        {
            if (!this.EnsureCatTeaseQteMethod())
            {
                return this.TryInvokeAuraMonoCatTeaseQte(catNetId, qteValue);
            }

            ParameterInfo[] parameters = this.petPlayMeowTeaseQteMethod.GetParameters();
            object qteArg = Enum.ToObject(parameters[1].ParameterType, qteValue);
            this.petPlayMeowTeaseQteMethod.Invoke(null, new object[] { catNetId, qteArg });
            return true;
        }

        private bool TryInvokeDogTeaseQte(uint dogNetId, bool encourage)
        {
            if (this.TryInvokeAuraMonoDogTeaseQte(dogNetId, encourage))
            {
                return true;
            }

            if (!this.EnsureDogTeaseQteMethod())
            {
                this.PetPlayLog("PetProtocolManager.TeaseQte unavailable.");
                return false;
            }

            this.petPlayDogTeaseQteMethod.Invoke(null, new object[] { dogNetId, encourage });
            return true;
        }

        private bool EnsureCatTeaseQteMethod()
        {
            if (this.petPlayMeowTeaseQteMethod != null)
            {
                return true;
            }

            Type protocolType = this.FindLoadedType(
                "XDTDataAndProtocol.ProtocolService.Meow.MeowProtocolManager",
                "MeowProtocolManager");
            if (protocolType == null)
            {
                return false;
            }

            foreach (MethodInfo method in protocolType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method == null || method.Name != "TeaseQte")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType == typeof(uint) && parameters[1].ParameterType.IsEnum)
                {
                    this.petPlayMeowTeaseQteMethod = method;
                    return true;
                }
            }

            return false;
        }

        private bool EnsureDogTeaseQteMethod()
        {
            if (this.petPlayDogTeaseQteMethod != null)
            {
                return true;
            }

            Type protocolType = this.FindLoadedType(
                "XDTDataAndProtocol.ProtocolService.Pet.PetProtocolManager",
                "PetProtocolManager");
            if (protocolType == null)
            {
                return false;
            }

            this.petPlayDogTeaseQteMethod = protocolType.GetMethod(
                "TeaseQte",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(uint), typeof(bool) },
                null);
            return this.petPlayDogTeaseQteMethod != null;
        }

        private void TryAutoAnswerCatPlayFromQuestionState()
        {
            float nextScanDelay = Time.unscaledTime < this.petPlayLastCatAnswerAt + 1.2f ? 0.12f : 0.45f;
            if (Time.unscaledTime < this.petPlayNextCatQuestionScanAt)
            {
                return;
            }

            this.petPlayNextCatQuestionScanAt = Time.unscaledTime + nextScanDelay;

            if (!this.TryGetActiveCatPlayQuestion(out uint activeCatNetId, out int qteValue, out string spriteName, out IntPtr activeQuestionCell, out string questionStatus)
                || activeCatNetId == 0U)
            {
                this.petPlayLastCatNetId = 0U;
                this.petPlayLastCatQte = -1;
                this.petPlayLastCatSprite = string.Empty;
                this.petPlayLastCatQuestionCell = IntPtr.Zero;
                return;
            }

            if (activeQuestionCell != IntPtr.Zero && activeQuestionCell == this.petPlayLastCatQuestionCell)
            {
                return;
            }

            if (activeCatNetId == this.petPlayLastCatNetId
                && qteValue == this.petPlayLastCatQte
                && Time.unscaledTime - this.petPlayLastCatAnswerAt < 0.85f)
            {
                return;
            }

            this.petPlayLastCatNetId = activeCatNetId;
            this.petPlayLastCatQte = qteValue;
            this.petPlayLastCatSprite = spriteName;
            this.petPlayLastCatQuestionCell = activeQuestionCell;
            this.petPlayLastCatAnswerAt = Time.unscaledTime;

            bool protocolOk = this.TryInvokeCatTeaseQte(activeCatNetId, qteValue);
            if (protocolOk)
            {
                this.petPlayCatAnswerCount++;
                this.petPlayNextCatQuestionScanAt = Time.unscaledTime + 0.18f;
                this.PetPlayLog("Cat QTE answered via question state netId=" + activeCatNetId
                    + " type=" + qteValue
                    + " sprite=" + spriteName
                    + " net=True.");
            }
        }

        private bool TryGetActiveCatPlayPanel(out object panelTarget, out uint catNetId, out bool inputDisabled, out string status)
        {
            panelTarget = null;
            catNetId = 0U;
            inputDisabled = false;
            status = "CatPlayStatusPanel not found.";

            try
            {
                Component[] components = Resources.FindObjectsOfTypeAll<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null || component.gameObject == null || !component.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    if (!this.TryGetPetPlayPanelTarget(component, "CatPlayStatusPanel", out object target) || target == null)
                    {
                        continue;
                    }

                    if (!this.TryReadManagedBoolMember(target, "_inputDisabled", out inputDisabled)
                        && !this.TryReadManagedBoolMember(target, "inputDisabled", out inputDisabled))
                    {
                        inputDisabled = false;
                    }

                    if (!this.TryReadManagedUInt32Member(target, "_catNetId", out catNetId)
                        && !this.TryReadManagedUInt32Member(target, "catNetId", out catNetId))
                    {
                        status = "CatPlayStatusPanel missing cat net id.";
                        continue;
                    }

                    panelTarget = target;
                    status = "CatPlayStatusPanel active: netId=" + catNetId + " inputDisabled=" + inputDisabled + ".";
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                status = "CatPlayStatusPanel scan exception: " + ex.Message;
                return false;
            }
        }

        private bool TryGetPetPlayPanelTarget(Component component, string panelTypeName, out object target)
        {
            target = null;
            if (component == null || string.IsNullOrEmpty(panelTypeName))
            {
                return false;
            }

            try
            {
                string managedName = component.GetType().FullName ?? component.GetType().Name ?? string.Empty;
                if (managedName.IndexOf(panelTypeName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    target = component;
                    return true;
                }

                Il2CppObject il2CppObject = component.TryCast<Il2CppObject>();
                string il2CppName = il2CppObject?.GetIl2CppType()?.FullName?.ToString() ?? string.Empty;
                if (il2CppName.IndexOf(panelTypeName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    target = component;
                    return true;
                }

                Type wrapperType = component.GetType();
                PropertyInfo implProperty = wrapperType.GetProperty("Impl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object impl = implProperty != null ? implProperty.GetValue(component, null) : null;
                string implName = impl?.GetType().FullName ?? impl?.GetType().Name ?? string.Empty;
                if (impl != null && implName.IndexOf(panelTypeName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    target = impl;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetActiveCatPlayQuestion(out uint catNetId, out int qteValue, out string spriteName, out IntPtr questionCellObj, out string status)
        {
            catNetId = 0U;
            qteValue = -1;
            spriteName = string.Empty;
            questionCellObj = IntPtr.Zero;
            status = "not checked";

            try
            {
                if (!this.TryGetAuraMonoTrackingCatPlay(out IntPtr catPlayObj, out status) || catPlayObj == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetMonoObjectMember(catPlayObj, "_questionCells", out IntPtr questionCellsObj) || questionCellsObj == IntPtr.Zero)
                {
                    status = "TrackingCatPlay._questionCells unavailable.";
                    return false;
                }

                List<IntPtr> entries = new List<IntPtr>(4);
                if (!this.TryEnumerateAuraMonoCollectionItems(questionCellsObj, entries) || entries.Count == 0)
                {
                    status = "TrackingCatPlay has no active question cells.";
                    return false;
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    IntPtr entryObj = entries[i];
                    if (entryObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryReadCatQuestionEntryNetId(entryObj, out uint entryNetId) || entryNetId == 0U)
                    {
                        continue;
                    }

                    if (!this.TryGetCatQuestionEntryValue(entryObj, out IntPtr entryCellObj) || entryCellObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.TryReadCatQuestionEntrySprite(entryObj, out string entrySprite)
                        && this.TryMapCatQteSprite(entrySprite, out int entryQte))
                    {
                        catNetId = entryNetId;
                        qteValue = entryQte;
                        spriteName = entrySprite;
                        questionCellObj = entryCellObj;
                        status = "active question sprite=" + entrySprite + ".";
                        return true;
                    }
                }

                status = "active question entries had no readable sprite/qte.";
                return false;
            }
            catch (Exception ex)
            {
                status = "active question exception: " + ex.Message;
                return false;
            }
        }

        private bool TryGetCatQuestionEntryValue(IntPtr entryObj, out IntPtr cellObj)
        {
            cellObj = IntPtr.Zero;
            return entryObj != IntPtr.Zero
                && ((this.TryGetMonoObjectMember(entryObj, "Value", out cellObj) && cellObj != IntPtr.Zero)
                    || (this.TryGetMonoObjectMember(entryObj, "value", out cellObj) && cellObj != IntPtr.Zero)
                    || (this.TryGetMonoObjectMember(entryObj, "_value", out cellObj) && cellObj != IntPtr.Zero));
        }

        private bool TryReadCatQuestionEntryNetId(IntPtr entryObj, out uint catNetId)
        {
            catNetId = 0U;
            if (entryObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoUInt32Member(entryObj, "Key", out catNetId)
                || this.TryGetMonoUInt32Member(entryObj, "key", out catNetId)
                || this.TryGetMonoUInt32Member(entryObj, "_key", out catNetId))
            {
                return true;
            }

            if ((this.TryGetMonoObjectMember(entryObj, "Key", out IntPtr keyObj)
                    || this.TryGetMonoObjectMember(entryObj, "key", out keyObj)
                    || this.TryGetMonoObjectMember(entryObj, "_key", out keyObj))
                && this.TryUnboxMonoUInt32(keyObj, out catNetId))
            {
                return true;
            }

            return false;
        }

        private bool TryReadCatQuestionEntrySprite(IntPtr entryObj, out string spriteName)
        {
            spriteName = string.Empty;
            if (entryObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetCatQuestionEntryValue(entryObj, out IntPtr cellObj) || cellObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoObjectMember(cellObj, "_icon", out IntPtr iconObj) && iconObj != IntPtr.Zero)
            {
                if (this.TryGetMonoStringMember(iconObj, "SpriteName", out spriteName)
                    || this.TryGetMonoStringMember(iconObj, "spriteName", out spriteName)
                    || this.TryGetMonoStringMember(iconObj, "_spriteName", out spriteName))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryMapCatQteSprite(string spriteName, out int qteValue)
        {
            qteValue = -1;
            if (string.IsNullOrEmpty(spriteName))
            {
                return false;
            }

            string lower = spriteName.ToLowerInvariant();
            if (lower.Contains("ui_cat_play_up"))
            {
                qteValue = 0;
                return true;
            }

            if (lower.Contains("ui_cat_play_down"))
            {
                qteValue = 1;
                return true;
            }

            if (lower.Contains("ui_cat_play_shake"))
            {
                qteValue = 2;
                return true;
            }

            return false;
        }

        private bool TryGetAuraMonoTrackingCatPlay(out IntPtr catPlayObj, out string status)
        {
            catPlayObj = IntPtr.Zero;
            if (!this.TryGetAuraMonoTrackingPanel(out IntPtr trackingPanelObj, out status) || trackingPanelObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(trackingPanelObj, "_catPlay", out catPlayObj) || catPlayObj == IntPtr.Zero)
            {
                status = "TrackingPanel._catPlay unavailable.";
                return false;
            }

            status = "TrackingCatPlay ready.";
            return true;
        }

        private unsafe bool TryGetAuraMonoTrackingPanel(out IntPtr trackingPanelObj, out string status)
        {
            return this.TryGetAuraMonoUiView("XDTGame.UI.Panel.TrackingPanel", "TrackingPanel", out trackingPanelObj, out status);
        }

        private unsafe bool TryGetAuraMonoUiView(string viewTypeName, string viewLabel, out IntPtr viewObj, out string status)
        {
            viewObj = IntPtr.Zero;
            status = viewLabel + " not resolved.";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    status = "AuraMono API not ready.";
                    return false;
                }

                if (!this.TryGetAuraMonoUiManagerObject(out IntPtr uiManagerObj, out status) || uiManagerObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr uiManagerClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(uiManagerObj) : IntPtr.Zero;
                IntPtr getViewMethod = this.FindAuraMonoMethodOnHierarchy(uiManagerClass, "GetView", 1);
                if (getViewMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    status = "UI manager GetView(Type) unavailable.";
                    return false;
                }

                if (!this.TryCreateAuraMonoSystemTypeObject(viewTypeName, out IntPtr viewTypeObj) || viewTypeObj == IntPtr.Zero)
                {
                    status = viewLabel + " System.Type unavailable.";
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = viewTypeObj;
                viewObj = auraMonoRuntimeInvoke(getViewMethod, uiManagerObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || viewObj == IntPtr.Zero)
                {
                    status = "UIManager.GetView(" + viewLabel + ") returned null.";
                    viewObj = IntPtr.Zero;
                    return false;
                }

                status = viewLabel + " ready.";
                return true;
            }
            catch (Exception ex)
            {
                status = viewLabel + " exception: " + ex.Message;
                viewObj = IntPtr.Zero;
                return false;
            }
        }

        private bool TryGetAuraMonoUiManagerObject(out IntPtr uiManagerObj, out string status)
        {
            uiManagerObj = IntPtr.Zero;
            status = "UI manager not resolved.";

            try
            {
                IntPtr uiManagerClass = this.FindAuraMonoClassByFullName("XDTGame.Core.UIManager");
                if (uiManagerClass != IntPtr.Zero)
                {
                    IntPtr getInstanceMethod = this.FindAuraMonoMethodOnHierarchy(uiManagerClass, "get_Instance", 0);
                    if (getInstanceMethod != IntPtr.Zero && auraMonoRuntimeInvoke != null)
                    {
                        IntPtr exc = IntPtr.Zero;
                        uiManagerObj = auraMonoRuntimeInvoke(getInstanceMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                        if (exc == IntPtr.Zero && uiManagerObj != IntPtr.Zero)
                        {
                            status = "UIManager.Instance ready.";
                            return true;
                        }
                    }
                }

                return this.TryGetAuraMonoUiManagerFromManagersServiceDic(out uiManagerObj, out status);
            }
            catch (Exception ex)
            {
                status = "UI manager exception: " + ex.Message;
                uiManagerObj = IntPtr.Zero;
                return false;
            }
        }

        private bool TryGetAuraMonoUiManagerFromManagersServiceDic(out IntPtr uiManagerObj, out string status)
        {
            uiManagerObj = IntPtr.Zero;
            status = "Managers._serviceDic not resolved.";

            IntPtr managersClass = this.FindAuraMonoClassByFullName("XDTGame.Framework.Managers");
            if (managersClass == IntPtr.Zero)
            {
                status = "Managers class unavailable.";
                return false;
            }

            if ((!this.TryGetAuraMonoStaticObjectField(managersClass, "_serviceDic", out IntPtr serviceDicObj) || serviceDicObj == IntPtr.Zero)
                && (!this.TryGetAuraMonoStaticObjectField(managersClass, "serviceDic", out serviceDicObj) || serviceDicObj == IntPtr.Zero))
            {
                status = "Managers._serviceDic unavailable.";
                return false;
            }

            List<IntPtr> entries = new List<IntPtr>(16);
            if (!this.TryEnumerateAuraMonoCollectionItems(serviceDicObj, entries) || entries.Count == 0)
            {
                status = "Managers._serviceDic empty.";
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                IntPtr entryObj = entries[i];
                if (entryObj == IntPtr.Zero)
                {
                    continue;
                }

                if ((!this.TryGetMonoObjectMember(entryObj, "Value", out IntPtr serviceObj) || serviceObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entryObj, "value", out serviceObj) || serviceObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entryObj, "_value", out serviceObj) || serviceObj == IntPtr.Zero))
                {
                    continue;
                }

                if ((!this.TryGetMonoObjectMember(serviceObj, "manager", out IntPtr managerObj) || managerObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(serviceObj, "_manager", out managerObj) || managerObj == IntPtr.Zero))
                {
                    continue;
                }

                IntPtr managerClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(managerObj) : IntPtr.Zero;
                string managerName = managerClass != IntPtr.Zero ? this.GetAuraMonoClassDisplayName(managerClass) : string.Empty;
                bool looksLikeUiManager = managerName.EndsWith("UIManager", StringComparison.Ordinal)
                    || managerName.EndsWith(".UIManager", StringComparison.Ordinal)
                    || this.FindAuraMonoMethodOnHierarchy(managerClass, "GetView", 1) != IntPtr.Zero;
                if (!looksLikeUiManager)
                {
                    continue;
                }

                uiManagerObj = managerObj;
                status = "UI manager resolved via Managers._serviceDic: " + managerName + ".";
                return true;
            }

            status = "Managers._serviceDic had no UI manager.";
            return false;
        }

        private unsafe bool TryRemoveActiveCatQuestionCell(uint catNetId)
        {
            string status = "invalid catNetId.";
            if (catNetId == 0U || !this.TryGetAuraMonoTrackingCatPlay(out IntPtr catPlayObj, out status) || catPlayObj == IntPtr.Zero)
            {
                this.PetPlayLog("Cat question clear unavailable: " + status);
                return false;
            }

            IntPtr catPlayClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(catPlayObj) : IntPtr.Zero;
            IntPtr removeMethod = this.FindAuraMonoMethodOnHierarchy(catPlayClass, "RemoveQuestionCell", 1);
            if (removeMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                this.PetPlayLog("Cat question clear method unavailable.");
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&catNetId);
            auraMonoRuntimeInvoke(removeMethod, catPlayObj, (IntPtr)args, ref exc);
            bool ok = exc == IntPtr.Zero;
            if (!ok)
            {
                this.PetPlayLog("Cat question clear failed: exc=0x" + exc.ToInt64().ToString("X"));
            }

            return ok;
        }

        private void TryAutoAnswerDogPlayFromUi()
        {
            float nextScanDelay = Time.unscaledTime < this.petPlayLastDogAnswerAt + 1.2f ? 0.12f : 1.25f;
            if (Time.unscaledTime < this.petPlayNextDogRoundScanAt)
            {
                return;
            }

            this.petPlayNextDogRoundScanAt = Time.unscaledTime + nextScanDelay;

            // Skip AuraMono panel scan if no dog session has been active recently (avoids
            // scanning a non-existent/destroyed DogPlayStatusPanel on every tick).
            bool dogSessionRecentlyActive = this.petPlayLastDogAnswerAt > 0f
                && Time.unscaledTime - this.petPlayLastDogAnswerAt < 60f;
            if (!dogSessionRecentlyActive && this.petPlayLastDogNetId == 0U && this.petPlayLastDogRound < 0)
            {
                // First-ever scan is allowed; let it through. Subsequent cold scans get throttled.
            }

            if (!this.TryGetActiveDogPlayRound(out uint dogNetId, out int round, out string dogStatus) || dogNetId == 0U)
            {
                this.petPlayLastDogNetId = 0U;
                this.petPlayLastDogRound = -1;
                return;
            }

            if (dogNetId == this.petPlayLastDogNetId && round == this.petPlayLastDogRound)
            {
                return;
            }

            if (!this.TryResolveDogQteChoice(dogNetId, round, out bool encourage, out string choiceStatus))
            {
                if (Time.unscaledTime >= this.petPlayNextActiveQuestionFailureLogAt)
                {
                    this.petPlayNextActiveQuestionFailureLogAt = Time.unscaledTime + 3f;
                    this.PetPlayLog("Dog QTE choice unavailable: " + choiceStatus + ".");
                }
                return;
            }

            bool protocolOk = this.TryInvokeDogTeaseQte(dogNetId, encourage);
            if (protocolOk)
            {
                this.petPlayLastDogNetId = dogNetId;
                this.petPlayLastDogRound = round;
                this.petPlayLastDogAnswerAt = Time.unscaledTime;
                this.petPlayNextDogRoundScanAt = Time.unscaledTime + 0.18f;
                this.petPlayDogAnswerCount++;
                this.PetPlayLog("Dog QTE answered netId=" + dogNetId + " round=" + round + " action=" + (encourage ? "encourage" : "ignore") + " directNet=" + protocolOk + " " + choiceStatus + ".");
            }
        }

        private string FormatCatQteName(int qteValue)
        {
            switch (qteValue)
            {
                case 0:
                    return "second";
                case 1:
                    return "third";
                case 2:
                    return "main";
                default:
                    return "type " + qteValue;
            }
        }

        private bool TryResolveDogQteChoice(uint dogNetId, int round, out bool encourage, out string status)
        {
            encourage = true;
            status = "not checked";

            if (!this.TryGetDogTeaseCache(dogNetId, out int actionRound, out int actionConfig, out int actionFormal, out status))
            {
                string cacheStatus = status;
                if (this.TryResolveDogQteChoiceFromLearningTable(dogNetId, out encourage, out string learningTableStatus))
                {
                    status = learningTableStatus + " cacheFallback=" + cacheStatus;
                    return true;
                }

                if (this.TryResolveDogQteChoiceFromMotion(dogNetId, out encourage, out string motionStatus))
                {
                    status = motionStatus + " cacheFallback=" + cacheStatus;
                    return true;
                }

                status = cacheStatus + " " + learningTableStatus + " " + motionStatus;
                return false;
            }

            if (actionRound > 0 && round > 0 && actionRound != round)
            {
                status = "stale dog cache round=" + actionRound + " uiRound=" + round + " config=" + actionConfig + " formal=" + actionFormal;
                return false;
            }

            encourage = actionConfig == actionFormal;
            status = "choiceSource=DogTeaseCache actionRound=" + actionRound + " config=" + actionConfig + " formal=" + actionFormal;
            return true;
        }

        private bool TryResolveDogQteChoiceFromLearningTable(uint dogNetId, out bool encourage, out string status)
        {
            encourage = true;
            status = "DogLearningMotion PreAction unavailable.";

            if (!this.TryGetDogPlayPanelLearningId(out int learningId, out string learningStatus) || learningId <= 0)
            {
                status = learningStatus;
                return false;
            }

            if (!this.TryGetDogLearningMotionData(learningId, out int targetMotionId, out int preAction, out string learningDataStatus))
            {
                status = learningDataStatus + " learningId=" + learningId;
                return false;
            }

            if (!this.TryGetDogComponentMotionId(dogNetId, out int dogMotionId, out string motionStatus) || dogMotionId <= 0)
            {
                status = motionStatus + " learningId=" + learningId + " targetMotionId=" + targetMotionId + " preAction=" + preAction;
                return false;
            }

            bool hasDogMotionInfo = this.TryGetDogMotionLearningInfo(dogMotionId, out int requireLearningId, out bool teaseNotLearningMotion, out string dogMotionStatus);
            encourage = dogMotionId == targetMotionId || (hasDogMotionInfo && requireLearningId == learningId);
            status = "choiceSource=DogLearningMotion learningId=" + learningId + " targetMotionId=" + targetMotionId + " preAction=" + preAction + " dogMotionId=" + dogMotionId + " requireLearningId=" + (hasDogMotionInfo ? requireLearningId.ToString() : "?") + " teaseNotLearning=" + (hasDogMotionInfo ? teaseNotLearningMotion.ToString() : "?") + " action=" + (encourage ? "encourage" : "ignore") + " dogMotionInfo=" + dogMotionStatus;
            return true;
        }

        private bool TryGetDogLearningMotionData(int learningId, out int motionId, out int preAction, out string status)
        {
            motionId = 0;
            preAction = 0;
            status = "DogLearningMotion unavailable.";

            if (this.TryGetDogLearningMotionDataManaged(learningId, out motionId, out preAction, out status))
            {
                return true;
            }

            string managedStatus = status;
            if (this.TryGetDogLearningMotionDataAuraMono(learningId, out motionId, out preAction, out status))
            {
                return true;
            }

            status = managedStatus + " " + status;
            return false;
        }

        private bool TryGetDogLearningMotionDataManaged(int learningId, out int motionId, out int preAction, out string status)
        {
            motionId = 0;
            preAction = 0;
            status = "managed TableDogLearningMotion unavailable.";

            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    return false;
                }

                MethodInfo getMethod = tableDataType.GetMethod("GetDogLearningMotion", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(bool) }, null);
                if (getMethod == null)
                {
                    status = "managed TableData.GetDogLearningMotion unavailable.";
                    return false;
                }

                object tableObj = getMethod.Invoke(null, new object[] { learningId, false });
                if (tableObj == null)
                {
                    status = "managed TableDogLearningMotion missing learningId=" + learningId + ".";
                    return false;
                }

                bool hasMotion = this.TryGetNestedIntMember(tableObj, out motionId, "motionID");
                bool hasPreAction = this.TryGetNestedIntMember(tableObj, out preAction, "PreAction");
                if (!hasMotion || !hasPreAction)
                {
                    status = "managed TableDogLearningMotion fields unreadable motion=" + hasMotion + " preAction=" + hasPreAction + ".";
                    return false;
                }

                status = "managed TableDogLearningMotion motionID=" + motionId + " PreAction=" + preAction;
                return motionId > 0;
            }
            catch (Exception ex)
            {
                status = "managed TableDogLearningMotion exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryGetDogLearningMotionDataAuraMono(int learningId, out int motionId, out int preAction, out string status)
        {
            motionId = 0;
            preAction = 0;
            status = "AuraMono TableDogLearningMotion unavailable.";

            try
            {
                if (!this.TryGetAuraMonoDogLearningMotion(learningId, out IntPtr tableObj, out status))
                {
                    return false;
                }

                bool hasMotion = this.TryGetMonoIntMember(tableObj, "motionID", out motionId);
                bool hasPreAction = this.TryGetMonoIntMember(tableObj, "PreAction", out preAction)
                    || this.TryGetMonoIntMember(tableObj, "_PreAction", out preAction);
                if (!hasMotion || !hasPreAction)
                {
                    status = "AuraMono TableDogLearningMotion fields unreadable motion=" + hasMotion + " preAction=" + hasPreAction + ".";
                    return false;
                }

                status = "AuraMono TableDogLearningMotion motionID=" + motionId + " PreAction=" + preAction;
                return motionId > 0;
            }
            catch (Exception ex)
            {
                status = "AuraMono TableDogLearningMotion exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TryGetDogLearningPreAction(int learningId, out int preAction, out string status)
        {
            preAction = 0;
            status = "DogLearningMotion unavailable.";

            if (this.TryGetDogLearningPreActionManaged(learningId, out preAction, out status))
            {
                return true;
            }

            string managedStatus = status;
            if (this.TryGetDogLearningPreActionAuraMono(learningId, out preAction, out status))
            {
                return true;
            }

            status = managedStatus + " " + status;
            return false;
        }

        private bool TryGetDogLearningPreActionManaged(int learningId, out int preAction, out string status)
        {
            preAction = 0;
            status = "managed TableDogLearningMotion unavailable.";

            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    return false;
                }

                MethodInfo getMethod = tableDataType.GetMethod("GetDogLearningMotion", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(bool) }, null);
                if (getMethod == null)
                {
                    status = "managed TableData.GetDogLearningMotion unavailable.";
                    return false;
                }

                object tableObj = getMethod.Invoke(null, new object[] { learningId, false });
                if (tableObj == null)
                {
                    status = "managed TableDogLearningMotion missing learningId=" + learningId + ".";
                    return false;
                }

                if (!this.TryGetNestedIntMember(tableObj, out preAction, "PreAction"))
                {
                    status = "managed TableDogLearningMotion.PreAction unreadable.";
                    return false;
                }

                status = "managed TableDogLearningMotion PreAction=" + preAction;
                return preAction > 0;
            }
            catch (Exception ex)
            {
                status = "managed TableDogLearningMotion exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryGetDogLearningPreActionAuraMono(int learningId, out int preAction, out string status)
        {
            preAction = 0;
            status = "AuraMono TableDogLearningMotion unavailable.";

            try
            {
                if (!this.TryGetAuraMonoDogLearningMotion(learningId, out IntPtr tableObj, out status))
                {
                    return false;
                }

                if (!this.TryGetMonoIntMember(tableObj, "PreAction", out preAction)
                    && !this.TryGetMonoIntMember(tableObj, "_PreAction", out preAction))
                {
                    status = "AuraMono TableDogLearningMotion.PreAction unreadable.";
                    return false;
                }

                status = "AuraMono TableDogLearningMotion PreAction=" + preAction;
                return preAction > 0;
            }
            catch (Exception ex)
            {
                status = "AuraMono TableDogLearningMotion exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryGetAuraMonoDogLearningMotion(int learningId, out IntPtr tableObj, out string status)
        {
            tableObj = IntPtr.Zero;
            status = "AuraMono TableDogLearningMotion unavailable.";

            if (learningId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr ecsImage = this.FindAuraMonoImage(new string[] { "EcsClient", "EcsClient.dll" });
            IntPtr tableDataClass = ecsImage != IntPtr.Zero ? auraMonoClassFromName(ecsImage, string.Empty, "TableData") : IntPtr.Zero;
            if (tableDataClass == IntPtr.Zero && ecsImage != IntPtr.Zero)
            {
                tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
            }
            if (tableDataClass == IntPtr.Zero)
            {
                tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
            }
            if (tableDataClass == IntPtr.Zero)
            {
                tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies("EcsClient", "TableData");
            }
            if (tableDataClass == IntPtr.Zero)
            {
                status = "AuraMono TableData class unavailable.";
                return false;
            }

            IntPtr getMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetDogLearningMotion", 2);
            if (getMethod == IntPtr.Zero)
            {
                status = "AuraMono TableData.GetDogLearningMotion unavailable.";
                return false;
            }

            bool needException = false;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&learningId);
            args[1] = (IntPtr)(&needException);
            IntPtr exc = IntPtr.Zero;
            tableObj = auraMonoRuntimeInvoke(getMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || tableObj == IntPtr.Zero)
            {
                status = "AuraMono TableDogLearningMotion missing learningId=" + learningId + " exc=0x" + exc.ToInt64().ToString("X");
                return false;
            }

            status = "AuraMono TableDogLearningMotion ready.";
            return true;
        }

        private bool TryGetDogMotionLearningInfo(int dogMotionId, out int requireLearningId, out bool teaseNotLearningMotion, out string status)
        {
            requireLearningId = 0;
            teaseNotLearningMotion = false;
            status = "Dogmotion unavailable.";

            if (this.TryGetDogMotionLearningInfoManaged(dogMotionId, out requireLearningId, out teaseNotLearningMotion, out status))
            {
                return true;
            }

            string managedStatus = status;
            if (this.TryGetDogMotionLearningInfoAuraMono(dogMotionId, out requireLearningId, out teaseNotLearningMotion, out status))
            {
                return true;
            }

            status = managedStatus + " " + status;
            return false;
        }

        private bool TryGetDogMotionLearningInfoManaged(int dogMotionId, out int requireLearningId, out bool teaseNotLearningMotion, out string status)
        {
            requireLearningId = 0;
            teaseNotLearningMotion = false;
            status = "managed TableDogmotion unavailable.";

            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (tableDataType == null)
                {
                    return false;
                }

                MethodInfo getMethod = tableDataType.GetMethod("GetDogmotion", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(bool) }, null);
                if (getMethod == null)
                {
                    status = "managed TableData.GetDogmotion unavailable.";
                    return false;
                }

                object tableObj = getMethod.Invoke(null, new object[] { dogMotionId, false });
                if (tableObj == null)
                {
                    status = "managed TableDogmotion missing id=" + dogMotionId + ".";
                    return false;
                }

                if (!this.TryGetNestedIntMember(tableObj, out requireLearningId, "requireLearningId"))
                {
                    status = "managed TableDogmotion.requireLearningId unreadable.";
                    return false;
                }

                if (this.TryGetObjectMember(tableObj, "teaseNotLearningMotion", out object raw) && raw is bool flag)
                {
                    teaseNotLearningMotion = flag;
                }

                status = "managed TableDogmotion requireLearningId=" + requireLearningId + " teaseNotLearning=" + teaseNotLearningMotion;
                return true;
            }
            catch (Exception ex)
            {
                status = "managed TableDogmotion exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryGetDogMotionLearningInfoAuraMono(int dogMotionId, out int requireLearningId, out bool teaseNotLearningMotion, out string status)
        {
            requireLearningId = 0;
            teaseNotLearningMotion = false;
            status = "AuraMono TableDogmotion unavailable.";

            try
            {
                if (dogMotionId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                IntPtr ecsImage = this.FindAuraMonoImage(new string[] { "EcsClient", "EcsClient.dll" });
                IntPtr tableDataClass = ecsImage != IntPtr.Zero ? auraMonoClassFromName(ecsImage, string.Empty, "TableData") : IntPtr.Zero;
                if (tableDataClass == IntPtr.Zero && ecsImage != IntPtr.Zero)
                {
                    tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
                }
                if (tableDataClass == IntPtr.Zero)
                {
                    tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
                }
                if (tableDataClass == IntPtr.Zero)
                {
                    tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies("EcsClient", "TableData");
                }
                if (tableDataClass == IntPtr.Zero)
                {
                    status = "AuraMono TableData class unavailable.";
                    return false;
                }

                IntPtr getMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetDogmotion", 2);
                if (getMethod == IntPtr.Zero)
                {
                    status = "AuraMono TableData.GetDogmotion unavailable.";
                    return false;
                }

                bool needException = false;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&dogMotionId);
                args[1] = (IntPtr)(&needException);
                IntPtr exc = IntPtr.Zero;
                IntPtr tableObj = auraMonoRuntimeInvoke(getMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || tableObj == IntPtr.Zero)
                {
                    status = "AuraMono TableDogmotion missing id=" + dogMotionId + " exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                if (!this.TryGetMonoIntMember(tableObj, "requireLearningId", out requireLearningId))
                {
                    status = "AuraMono TableDogmotion.requireLearningId unreadable.";
                    return false;
                }

                this.TryGetMonoBoolMember(tableObj, "teaseNotLearningMotion", out teaseNotLearningMotion);
                status = "AuraMono TableDogmotion requireLearningId=" + requireLearningId + " teaseNotLearning=" + teaseNotLearningMotion;
                return true;
            }
            catch (Exception ex)
            {
                status = "AuraMono TableDogmotion exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TryResolveDogQteChoiceFromMotion(uint dogNetId, out bool encourage, out string status)
        {
            encourage = true;
            status = "dog motion fallback unavailable.";

            if (!this.TryGetDogPlayPanelLearningId(out int learningId, out string learningStatus) || learningId <= 0)
            {
                status = learningStatus;
                return false;
            }

            if (!this.TryGetDogComponentMotionId(dogNetId, out int dogMotionId, out string motionStatus) || dogMotionId <= 1)
            {
                status = motionStatus + " learningId=" + learningId;
                return false;
            }

            encourage = dogMotionId == learningId;
            status = "choiceSource=DogMotion learningId=" + learningId + " dogMotionId=" + dogMotionId;
            return true;
        }

        private bool TryGetDogPlayPanelLearningId(out int learningId, out string status)
        {
            learningId = 0;
            status = "DogPlayStatusPanel._learningId unavailable.";

            try
            {
                if (!this.TryGetAuraMonoUiView("XDTGame.UI.Panel.DogPlayStatusPanel", "DogPlayStatusPanel", out IntPtr panelObj, out status)
                    || panelObj == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetMonoIntMember(panelObj, "_learningId", out learningId))
                {
                    status = "DogPlayStatusPanel._learningId unavailable.";
                    return false;
                }

                status = "DogPlayStatusPanel learningId=" + learningId;
                return learningId > 0;
            }
            catch (Exception ex)
            {
                status = "DogPlayStatusPanel learningId exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TryGetDogComponentMotionId(uint dogNetId, out int motionId, out string status)
        {
            motionId = 0;
            status = "DogComponentData unavailable.";

            if (this.TryGetDogComponentMotionIdAuraMono(dogNetId, out motionId, out status))
            {
                return true;
            }

            string auraStatus = status;
            try
            {
                Type dataCenterType = this.FindLoadedType(
                    "XDTDataAndProtocol.ComponentsData.DataCenter",
                    "ScriptsRefactory.DataAndProtocol.ComponentsData.DataCenter",
                    "DataCenter");
                Type dogComponentDataType = this.FindLoadedType(
                    "XDTDataAndProtocol.ComponentsData.DogComponentData",
                    "ScriptsRefactory.DataAndProtocol.ComponentsData.DogComponentData",
                    "DogComponentData");
                Type netIdType = this.FindLoadedType(
                    "EcsClient.XDT.Scene.Shared.Data.SharedData.NetId",
                    "XDT.Scene.Shared.Data.SharedData.NetId",
                    "NetId");

                if (dataCenterType == null || dogComponentDataType == null || netIdType == null)
                {
                    status = auraStatus + ". DogComponentData types unavailable. DataCenter=" + (dataCenterType != null) + " DogComponentData=" + (dogComponentDataType != null) + " NetId=" + (netIdType != null);
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
                        tryGetComponentMethod = method.MakeGenericMethod(dogComponentDataType);
                        break;
                    }
                }

                if (tryGetComponentMethod == null)
                {
                    status = "DataCenter.TryGetComponentData<DogComponentData> unavailable.";
                    return false;
                }

                object netIdArg = this.CreateNetCookNetIdArgument(netIdType, dogNetId);
                if (netIdArg == null)
                {
                    status = "DogComponentData NetId argument creation failed.";
                    return false;
                }

                object componentDataBox = Activator.CreateInstance(dogComponentDataType);
                object[] args = new object[] { netIdArg, componentDataBox };
                object invokeResult = tryGetComponentMethod.Invoke(null, args);
                bool found = invokeResult is bool foundFlag && foundFlag;
                if (!found)
                {
                    status = "DogComponentData missing for netId " + dogNetId + ".";
                    return false;
                }

                object componentData = args[1] ?? componentDataBox;
                if (!this.TryGetNestedIntMember(componentData, out motionId, "petComponentData", "animalComponentData", "motionId"))
                {
                    status = "DogComponentData motionId unavailable.";
                    return false;
                }

                status = "DogComponentData motionId=" + motionId;
                return motionId > 0;
            }
            catch (Exception ex)
            {
                status = "DogComponentData exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TryGetDogComponentMotionIdAuraMono(uint dogNetId, out int motionId, out string status)
        {
            motionId = 0;
            status = "AuraMono DogComponent unavailable.";

            try
            {
                if (dogNetId == 0U)
                {
                    status = "AuraMono DogComponent netId unavailable.";
                    return false;
                }

                if (!this.TryGetAuraMonoEntityObjectByNetId(dogNetId, out IntPtr entityObj) || entityObj == IntPtr.Zero)
                {
                    status = "AuraMono dog entity unavailable for netId " + dogNetId + ".";
                    return false;
                }

                if (!this.TryResolveDogComponentAuraMono(entityObj, out IntPtr dogComponentObj, out string componentStatus))
                {
                    status = componentStatus;
                    return false;
                }

                if (this.TryReadDogMotionIdFromDogComponentAuraMono(dogComponentObj, out motionId, out string motionStatus))
                {
                    status = motionStatus;
                    return motionId > 0;
                }

                status = motionStatus;
                return false;
            }
            catch (Exception ex)
            {
                status = "AuraMono DogComponent motion exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TryResolveDogComponentAuraMono(IntPtr entityObj, out IntPtr componentObj, out string status)
        {
            componentObj = IntPtr.Zero;
            status = "AuraMono DogComponent missing.";
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr componentsObj, "GetAllComponents") || componentsObj == IntPtr.Zero)
            {
                status = "AuraMono dog entity GetAllComponents unavailable.";
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components) || components.Count <= 0)
            {
                status = "AuraMono dog entity has no components.";
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

                if (className.EndsWith(".DogComponent", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(className, "DogComponent", StringComparison.OrdinalIgnoreCase)
                    || className.IndexOf("DogComponent", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    componentObj = candidate;
                    status = "AuraMono DogComponent ready.";
                    return true;
                }
            }

            status = "AuraMono dog entity is missing DogComponent.";
            return false;
        }

        private bool TryReadDogMotionIdFromDogComponentAuraMono(IntPtr dogComponentObj, out int motionId, out string status)
        {
            motionId = 0;
            status = "AuraMono DogComponent motionId unavailable.";
            if (dogComponentObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoIntMember(dogComponentObj, "currentMotionId", out motionId) && motionId > 0)
            {
                status = "AuraMono DogComponent currentMotionId=" + motionId;
                return true;
            }

            if (this.TryGetMonoObjectMember(dogComponentObj, "_animalMotionComponent", out IntPtr animalMotionComponentObj)
                && animalMotionComponentObj != IntPtr.Zero
                && this.TryGetMonoIntMember(animalMotionComponentObj, "motionId", out motionId)
                && motionId > 0)
            {
                status = "AuraMono DogComponent animalMotion.motionId=" + motionId;
                return true;
            }

            if (this.TryGetNestedMonoIntMember(dogComponentObj, out motionId, "_petComponentData", "animalComponentData", "motionId") && motionId > 0)
            {
                status = "AuraMono DogComponent _petComponentData.motionId=" + motionId;
                return true;
            }

            if (this.TryGetNestedMonoIntMember(dogComponentObj, out motionId, "_data", "petComponentData", "animalComponentData", "motionId") && motionId > 0)
            {
                status = "AuraMono DogComponent _data.motionId=" + motionId;
                return true;
            }

            if (this.TryGetNestedMonoIntMember(dogComponentObj, out motionId, "_animalComponentData", "motionId") && motionId > 0)
            {
                status = "AuraMono DogComponent _animalComponentData.motionId=" + motionId;
                return true;
            }

            return false;
        }

        private bool TryGetNestedMonoIntMember(IntPtr rootObj, out int value, params string[] memberPath)
        {
            value = 0;
            if (rootObj == IntPtr.Zero || memberPath == null || memberPath.Length == 0)
            {
                return false;
            }

            IntPtr current = rootObj;
            for (int i = 0; i < memberPath.Length - 1; i++)
            {
                if (!this.TryGetMonoObjectMember(current, memberPath[i], out IntPtr next) || next == IntPtr.Zero)
                {
                    return false;
                }

                current = next;
            }

            return this.TryGetMonoIntMember(current, memberPath[memberPath.Length - 1], out value);
        }

        private bool TryGetNestedIntMember(object root, out int value, params string[] memberPath)
        {
            value = 0;
            object current = root;
            if (current == null || memberPath == null || memberPath.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < memberPath.Length; i++)
            {
                if (current == null || !this.TryGetObjectMember(current, memberPath[i], out object next) || next == null)
                {
                    return false;
                }

                current = next;
            }

            try
            {
                value = Convert.ToInt32(current);
                return true;
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private bool TryFindDogQteFromVisibleUi(out bool encourage, out string status)
        {
            encourage = true;
            status = "no visible dog action icon";

            int encourageCount = 0;
            int ignoreCount = 0;
            string encourageName = string.Empty;
            string ignoreName = string.Empty;

            try
            {
                Image[] images = Resources.FindObjectsOfTypeAll<Image>();
                for (int i = 0; i < images.Length; i++)
                {
                    Image image = images[i];
                    if (image == null || image.sprite == null || image.gameObject == null || !image.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    string name = image.sprite.name ?? string.Empty;
                    string lower = name.ToLowerInvariant();
                    if (lower.Contains("dogplay_encourage") || lower.Contains("dog_play_encourage") || lower.Contains("dog_encourage"))
                    {
                        encourageCount++;
                        encourageName = name;
                        continue;
                    }

                    if (lower.Contains("dogplay_ignore") || lower.Contains("dog_play_ignore") || lower.Contains("dog_ignore"))
                    {
                        ignoreCount++;
                        ignoreName = name;
                    }
                }

                if (encourageCount > 0 && ignoreCount == 0)
                {
                    encourage = true;
                    status = "sprite=" + encourageName + " encourageCount=" + encourageCount;
                    return true;
                }

                if (ignoreCount > 0 && encourageCount == 0)
                {
                    encourage = false;
                    status = "sprite=" + ignoreName + " ignoreCount=" + ignoreCount;
                    return true;
                }

                status = "visible dog action icons ambiguous encourage=" + encourageCount + " ignore=" + ignoreCount;
                return false;
            }
            catch (Exception ex)
            {
                status = "visible dog action scan exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private bool TryGetDogTeaseCache(uint dogNetId, out int actionRound, out int actionConfig, out int actionFormal, out string status)
        {
            actionRound = 0;
            actionConfig = 0;
            actionFormal = 0;
            status = "DogTeaseCache unavailable.";

            try
            {
                object worldManager = this.TryResolvePetPlayWorldManager();
                if (worldManager == null)
                {
                    status = "world manager unavailable.";
                    return false;
                }

                MethodInfo getNetworkEntityMethod = worldManager.GetType().GetMethod("GetNetworkEntity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getNetworkEntityMethod == null)
                {
                    status = "GetNetworkEntity unavailable.";
                    return false;
                }

                object ecsEntity = getNetworkEntityMethod.Invoke(worldManager, new object[] { dogNetId });
                if (ecsEntity == null)
                {
                    status = "dog EcsEntity unavailable.";
                    return false;
                }

                Type componentType = this.FindLoadedType(
                    "XDT.Scene.Shared.Modules.Dog.DogTeaseCacheComponent",
                    "DogTeaseCacheComponent");
                Type entityDataOptOpenType = this.FindLoadedType(
                    "XDT.Scene.Shared.Entity.EntityOptData.EntityDataOpt`1",
                    "EntityDataOpt`1");
                Type entityDataOptExtensionsType = this.FindLoadedType(
                    "XDT.Scene.Shared.Entity.EntityOptData.EntityDataOpt",
                    "EntityDataOpt");
                if (componentType == null || entityDataOptOpenType == null || entityDataOptExtensionsType == null)
                {
                    status = "DogTeaseCache types unavailable. component=" + (componentType != null) + " opt=" + (entityDataOptOpenType != null) + " ext=" + (entityDataOptExtensionsType != null);
                    return false;
                }

                Type optType = entityDataOptOpenType.MakeGenericType(componentType);
                object opt = Activator.CreateInstance(optType);
                FieldInfo entityField = optType.GetField("_entity", BindingFlags.Instance | BindingFlags.NonPublic);
                if (entityField == null)
                {
                    status = "EntityDataOpt._entity unavailable.";
                    return false;
                }
                entityField.SetValue(opt, ecsEntity);

                MethodInfo tryGetValueMethod = null;
                foreach (MethodInfo method in entityDataOptExtensionsType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method == null || method.Name != "TryGetValue" || !method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 2 && parameters[1].ParameterType.IsByRef)
                    {
                        tryGetValueMethod = method.MakeGenericMethod(componentType);
                        break;
                    }
                }

                if (tryGetValueMethod == null)
                {
                    status = "EntityDataOpt.TryGetValue<DogTeaseCacheComponent> unavailable.";
                    return false;
                }

                object componentBox = Activator.CreateInstance(componentType);
                object[] args = new object[] { opt, componentBox };
                object result = tryGetValueMethod.Invoke(null, args);
                bool found = result is bool foundFlag && foundFlag;
                if (!found)
                {
                    status = "DogTeaseCacheComponent missing for netId " + dogNetId + ".";
                    return false;
                }

                object component = args[1] ?? componentBox;
                FieldInfo actionRoundField = componentType.GetField("ActionRound", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo actionConfigField = componentType.GetField("ActionConfig", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo actionFormalField = componentType.GetField("ActionFormal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (actionRoundField == null || actionConfigField == null || actionFormalField == null)
                {
                    status = "DogTeaseCache fields unavailable.";
                    return false;
                }

                actionRound = Convert.ToInt32(actionRoundField.GetValue(component));
                actionConfig = Convert.ToInt32(actionConfigField.GetValue(component));
                actionFormal = Convert.ToInt32(actionFormalField.GetValue(component));
                status = "DogTeaseCache ready.";
                return true;
            }
            catch (Exception ex)
            {
                status = "DogTeaseCache exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private object TryResolvePetPlayWorldManager()
        {
            Type[] managerTypes = new Type[]
            {
                this.FindLoadedType("EcsSystem.XD.GameGerm.Ecs.Boost.Client.ClientNetworkManager", "ClientNetworkManager"),
                this.FindLoadedType("EcsSystem.World.XDTownClientNetworkManager", "XDTownClientNetworkManager")
            };

            foreach (Type managerType in managerTypes)
            {
                if (managerType == null)
                {
                    continue;
                }

                object manager = this.TryGetStaticObjectAcrossHierarchy(managerType, "Instance", "_instance", "instance", "Current", "Singleton");
                if (manager == null && typeof(UnityEngine.Object).IsAssignableFrom(managerType))
                {
                    try
                    {
                        UnityEngine.Object[] sceneObjects = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
                        if (sceneObjects != null)
                        {
                            for (int _si = 0; _si < sceneObjects.Length; _si++)
                            {
                                try
                                {
                                    UnityEngine.Object sceneObject = sceneObjects[_si];
                                    // IL2CPP-wrapped objects may be partially freed — null-check before GetType()
                                    if (sceneObject == null)
                                    {
                                        continue;
                                    }
                                    Type sceneObjectType = sceneObject.GetType();
                                    if (sceneObjectType != null && managerType.IsAssignableFrom(sceneObjectType))
                                    {
                                        manager = sceneObject;
                                        break;
                                    }
                                }
                                catch
                                {
                                    // Skip destroyed/invalid IL2CPP object
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                if (manager == null)
                {
                    continue;
                }

                if (this.TryGetObjectMember(manager, "WorldManager", out object worldManager) && worldManager != null)
                {
                    return worldManager;
                }

                if (this.TryGetObjectMember(manager, "EcsClientNetwork", out object ecsClientNetwork) && ecsClientNetwork != null
                    && this.TryGetObjectMember(ecsClientNetwork, "WorldManager", out worldManager) && worldManager != null)
                {
                    return worldManager;
                }
            }

            return null;
        }

        private bool TryGetActiveDogPlayRound(out uint dogNetId, out int round, out string status)
        {
            dogNetId = 0U;
            round = -1;
            status = "not checked";

            try
            {
                if (!this.TryGetAuraMonoUiView("XDTGame.UI.Panel.DogPlayStatusPanel", "DogPlayStatusPanel", out IntPtr panelObj, out status)
                    || panelObj == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetMonoUInt32Member(panelObj, "_netId", out dogNetId) || dogNetId == 0U)
                {
                    status = "DogPlayStatusPanel._netId unavailable.";
                    return false;
                }

                if (!this.TryGetMonoIntMember(panelObj, "_roundState", out int roundState))
                {
                    status = "DogPlayStatusPanel._roundState unavailable.";
                    return false;
                }

                if (!this.TryGetMonoIntMember(panelObj, "_round", out round))
                {
                    round = 0;
                }

                if (roundState != 2)
                {
                    status = "DogPlayStatusPanel not in Play state: state=" + roundState + " round=" + round + ".";
                    return false;
                }

                status = "DogPlayStatusPanel active: netId=" + dogNetId + " round=" + round + " state=" + roundState + ".";
                return true;
            }
            catch (Exception ex)
            {
                status = "active dog round exception: " + ex.Message;
                return false;
            }
        }

        private bool TryFindCatQteFromVisibleUi(out int qteValue, out string spriteName)
        {
            qteValue = -1;
            spriteName = string.Empty;

            Image[] images = Resources.FindObjectsOfTypeAll<Image>();
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || image.sprite == null || image.gameObject == null || !image.gameObject.activeInHierarchy)
                {
                    continue;
                }

                string name = image.sprite.name ?? string.Empty;
                string lower = name.ToLowerInvariant();
                if (lower.Contains("ui_cat_play_up"))
                {
                    qteValue = 0;
                    spriteName = name;
                    return true;
                }

                if (lower.Contains("ui_cat_play_down"))
                {
                    qteValue = 1;
                    spriteName = name;
                    return true;
                }

                if (lower.Contains("ui_cat_play_shake"))
                {
                    qteValue = 2;
                    spriteName = name;
                    return true;
                }
            }

            return false;
        }

        private bool IsDogPlayQteVisible()
        {
            Image[] images = Resources.FindObjectsOfTypeAll<Image>();
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || image.sprite == null || image.gameObject == null || !image.gameObject.activeInHierarchy)
                {
                    continue;
                }

                string lower = (image.sprite.name ?? string.Empty).ToLowerInvariant();
                if (lower.Contains("dogplay_encourage") || lower.Contains("dogplay_ignore"))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetCurrentTeasePetNetId(out uint netId)
        {
            netId = 0U;
            try
            {
                if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero)
                {
                    return false;
                }

                if ((!this.TryGetMonoObjectMember(playerObj, "Status", out IntPtr statusObj) && !this.TryGetMonoObjectMember(playerObj, "status", out statusObj)) || statusObj == IntPtr.Zero)
                {
                    return false;
                }

                if ((!this.TryGetMonoObjectMember(statusObj, "FsmStatus", out IntPtr fsmStatusObj) && !this.TryGetMonoObjectMember(statusObj, "fsmStatus", out fsmStatusObj)) || fsmStatusObj == IntPtr.Zero)
                {
                    return false;
                }

                return this.TryGetMonoUInt32Member(fsmStatusObj, "TeasePetNetId", out netId)
                    || this.TryGetMonoUInt32Member(fsmStatusObj, "teasePetNetId", out netId);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetAuraMonoCharacterObject(out IntPtr characterObj)
        {
            characterObj = IntPtr.Zero;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr characterClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Game.GameMode.Character");
            if (characterClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getCharacterMethod = this.FindAuraMonoMethodOnHierarchy(characterClass, "get_character", 0);
            if (getCharacterMethod != IntPtr.Zero && auraMonoRuntimeInvoke != null)
            {
                IntPtr exc = IntPtr.Zero;
                characterObj = auraMonoRuntimeInvoke(getCharacterMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            }

            if (characterObj == IntPtr.Zero)
            {
                this.TryGetAuraMonoStaticObjectField(characterClass, "_character", out characterObj);
            }

            return characterObj != IntPtr.Zero;
        }

        private bool TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj)
        {
            playerObj = IntPtr.Zero;
            if (!this.TryGetAuraMonoCharacterObject(out IntPtr characterObj) || characterObj == IntPtr.Zero)
            {
                return false;
            }

            return (this.TryGetMonoObjectMember(characterObj, "Player", out playerObj) || this.TryGetMonoObjectMember(characterObj, "player", out playerObj))
                && playerObj != IntPtr.Zero;
        }

        private bool TryFindAuraMonoPlayerState(string classNameEndsWith, out IntPtr stateObj)
        {
            stateObj = IntPtr.Zero;
            if (string.IsNullOrEmpty(classNameEndsWith) || !this.TryGetAuraMonoCharacterObject(out IntPtr characterObj) || characterObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(characterObj, "bodyFsMachine", out IntPtr fsMachineObj) || fsMachineObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(fsMachineObj, "PlayerStates", out IntPtr statesObj) || statesObj == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> states = new List<IntPtr>(96);
            if (!this.TryEnumerateAuraMonoCollectionItems(statesObj, states))
            {
                return false;
            }

            for (int i = 0; i < states.Count; i++)
            {
                IntPtr candidate = states[i];
                if (candidate == IntPtr.Zero || auraMonoObjectGetClass == null)
                {
                    continue;
                }

                string displayName = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(candidate));
                if (displayName.EndsWith(classNameEndsWith, StringComparison.Ordinal)
                    || displayName.EndsWith("." + classNameEndsWith, StringComparison.Ordinal))
                {
                    stateObj = candidate;
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryInvokeCatLocalQte(int qteValue)
        {
            try
            {
                if (!this.TryFindAuraMonoPlayerState("PlayerStateTeaseCat", out IntPtr stateObj) || stateObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr stateClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(stateObj) : IntPtr.Zero;
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(stateClass, "OnQteEvent", 1);
                if (method == IntPtr.Zero)
                {
                    return false;
                }

                byte qteByte = (byte)Mathf.Clamp(qteValue, 0, 2);
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&qteByte);
                auraMonoRuntimeInvoke(method, stateObj, (IntPtr)args, ref exc);
                bool ok = exc == IntPtr.Zero;
                if (!ok)
                {
                    this.PetPlayLog("Cat local QTE failed: exc=0x" + exc.ToInt64().ToString("X"));
                }
                return ok;
            }
            catch (Exception ex)
            {
                this.PetPlayLog("Cat local QTE exception: " + ex.Message);
                return false;
            }
        }

        private unsafe bool TryInvokeDogLocalQte(bool encourage)
        {
            try
            {
                if (!this.TryFindAuraMonoPlayerState("PlayerStateTeaseDog", out IntPtr stateObj) || stateObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr stateClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(stateObj) : IntPtr.Zero;
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(stateClass, encourage ? "OnMainInteraction" : "OnSecondInteraction", 1);
                if (method == IntPtr.Zero)
                {
                    return false;
                }

                bool down = false;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&down);
                auraMonoRuntimeInvoke(method, stateObj, (IntPtr)args, ref exc);
                bool ok = exc == IntPtr.Zero;
                if (!ok)
                {
                    this.PetPlayLog("Dog local QTE failed: exc=0x" + exc.ToInt64().ToString("X"));
                }
                return ok;
            }
            catch (Exception ex)
            {
                this.PetPlayLog("Dog local QTE exception: " + ex.Message);
                return false;
            }
        }

        private bool EnsureAuraMonoCatTeaseQteMethod(out string status)
        {
            status = "AuraMono cat protocol unavailable.";
            if (this.petPlayAuraMeowTeaseQteMethod != IntPtr.Zero)
            {
                status = "AuraMono cat protocol ready.";
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    status = "AuraMono API not ready.";
                    return false;
                }

                IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Meow.MeowProtocolManager");
                if (protocolClass == IntPtr.Zero)
                {
                    status = "MeowProtocolManager class unavailable.";
                    return false;
                }

                this.petPlayAuraMeowTeaseQteMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "TeaseQte", 2);
                status = "AuraMono cat protocol class=0x" + protocolClass.ToInt64().ToString("X")
                    + " teaseQte=0x" + this.petPlayAuraMeowTeaseQteMethod.ToInt64().ToString("X");
                return this.petPlayAuraMeowTeaseQteMethod != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                status = "AuraMono cat protocol exception: " + ex.Message;
                return false;
            }
        }

        private bool EnsureAuraMonoDogTeaseQteMethod(out string status)
        {
            status = "AuraMono dog protocol unavailable.";
            if (this.petPlayAuraDogTeaseQteMethod != IntPtr.Zero)
            {
                status = "AuraMono dog protocol ready.";
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    status = "AuraMono API not ready.";
                    return false;
                }

                IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Pet.PetProtocolManager");
                if (protocolClass == IntPtr.Zero)
                {
                    status = "PetProtocolManager class unavailable.";
                    return false;
                }

                this.petPlayAuraDogTeaseQteMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "TeaseQte", 2);
                status = "AuraMono dog protocol class=0x" + protocolClass.ToInt64().ToString("X")
                    + " teaseQte=0x" + this.petPlayAuraDogTeaseQteMethod.ToInt64().ToString("X");
                return this.petPlayAuraDogTeaseQteMethod != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                status = "AuraMono dog protocol exception: " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryInvokeAuraMonoCatTeaseQte(uint catNetId, int qteValue)
        {
            if (!this.EnsureAuraMonoCatTeaseQteMethod(out string status) || this.petPlayAuraMeowTeaseQteMethod == IntPtr.Zero)
            {
                this.PetPlayLog("Aura cat QTE unavailable: " + status);
                return false;
            }

            // Must pass int32-sized value: MeowQteType is an enum (int32), not byte.
            // Passing a byte* here causes mono_runtime_invoke to read 3 extra stack bytes → crash.
            int qteInt = Mathf.Clamp(qteValue, 0, 2);
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&catNetId);
            args[1] = (IntPtr)(&qteInt);
            auraMonoRuntimeInvoke(this.petPlayAuraMeowTeaseQteMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            bool ok = exc == IntPtr.Zero;
            if (!ok)
            {
                this.PetPlayLog("Aura cat QTE netId=" + catNetId + " type=" + qteValue + " ok=False exc=0x" + exc.ToInt64().ToString("X"));
            }
            return ok;
        }

        private unsafe bool TryInvokeAuraMonoDogTeaseQte(uint dogNetId, bool encourage)
        {
            if (!this.EnsureAuraMonoDogTeaseQteMethod(out string status) || this.petPlayAuraDogTeaseQteMethod == IntPtr.Zero)
            {
                this.PetPlayLog("Aura dog QTE unavailable: " + status);
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&dogNetId);
            args[1] = (IntPtr)(&encourage);
            auraMonoRuntimeInvoke(this.petPlayAuraDogTeaseQteMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            bool ok = exc == IntPtr.Zero;
            this.PetPlayLog("Aura dog QTE netId=" + dogNetId + " encourage=" + encourage + " ok=" + ok + (ok ? string.Empty : " exc=0x" + exc.ToInt64().ToString("X")));
            return ok;
        }

        private void LogPetPlayResolverProbe(string catStatus, string dogStatus)
        {
            try
            {
                Type trackingCat = this.FindLoadedType("XDTGame.UI.Panel.TrackingCatPlay", "TrackingCatPlay");
                Type catPanel = this.FindLoadedType("XDTGame.UI.Panel.CatPlayStatusPanel", "CatPlayStatusPanel");
                Type dogPanel = this.FindLoadedType("XDTGame.UI.Panel.DogPlayStatusPanel", "DogPlayStatusPanel");
                Type meowProtocol = this.FindLoadedType("XDTDataAndProtocol.ProtocolService.Meow.MeowProtocolManager", "MeowProtocolManager");
                Type petProtocol = this.FindLoadedType("XDTDataAndProtocol.ProtocolService.Pet.PetProtocolManager", "PetProtocolManager");
                IntPtr auraMeow = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Meow.MeowProtocolManager");
                IntPtr auraPet = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Pet.PetProtocolManager");
                this.PetPlayLog("Resolver probe: managed trackingCat/catPanel/dogPanel/meow/pet="
                    + (trackingCat != null) + "/" + (catPanel != null) + "/" + (dogPanel != null) + "/" + (meowProtocol != null) + "/" + (petProtocol != null)
                    + " aura meow/pet=0x" + auraMeow.ToInt64().ToString("X") + "/0x" + auraPet.ToInt64().ToString("X")
                    + " cat=" + catStatus + " dog=" + dogStatus);
            }
            catch (Exception ex)
            {
                this.PetPlayLog("Resolver probe exception: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void PetPlayLog(string message)
        {
            if (!PetPlayLogsEnabled || string.IsNullOrEmpty(message))
            {
                return;
            }

            try
            {
                ModLogger.Msg("[PetPlay] " + message);
            }
            catch
            {
            }
        }
    }
}
