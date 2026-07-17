using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

using Il2CppObject = Il2CppSystem.Object;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private static bool PetPlayLogsEnabled => MasterLogPetPlay;

        private bool petPlayAutoCatEnabled = false;
        private bool petPlayAutoDogEnabled = false;
        private bool petPlayAutoWashEnabled = false;
        private bool petPlayRuntimeReadyLogged = false;
        private float petPlayNextResolverProbeAt = 0f;
        private float petPlayNextAutoTickAt = 0f;
        private int petPlayCatAnswerCount = 0;
        private int petPlayDogAnswerCount = 0;
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
        private float petPlayNextActiveQuestionFailureLogAt = 0f;
        private float petPlayNextCatQuestionScanAt = 0f;
        private float petPlayNextWashTickAt = 0f;
        private float petPlayLastWashClickAt = -999f;
        private uint petPlayLastWashPetNetId = 0U;
        private int petPlayWashClickCount = 0;
        private bool petPlayWashClickLocked = false;
        private bool petPlayWashSawButtonHidden = false;
        private IntPtr petPlayAuraPetBathingRoundStartMethod = IntPtr.Zero;

        // ---- Headless cat play (background session, no game-mode/UI entry) ----
        private enum PetPlayHeadlessCatState
        {
            Idle,
            Starting,
            Active
        }

        private PetPlayHeadlessCatState petPlayHeadlessCatState = PetPlayHeadlessCatState.Idle;
        private uint petPlayHeadlessCatNetId = 0U;
        private float petPlayHeadlessCatStartSentAt = 0f;
        private float petPlayHeadlessCatLastActivityAt = 0f;
        private int petPlayHeadlessCatAnswerCount = 0;
        private int petPlayHeadlessCatLastExitReason = -1;
        private bool petPlayHeadlessCatHooksRegistered = false;
        private IntPtr petPlayAuraMeowBeginTeaseMethod = IntPtr.Zero;
        private IntPtr petPlayAuraMeowCancelTeaseMethod = IntPtr.Zero;

        // ---- Pet care list (My Pets rows with per-pet Play / Wash) ----
        private sealed class PetCareEntry
        {
            public uint NetId;
            public string Name = string.Empty;
            public bool IsDog;
            public int Fullness = -1;
            public int Vitality = -1;
            public int Chemistry = -1;
            public int MotionsTotal = -1;
            public int MotionsUnlocked = -1;
            public int MotionsLearned = -1;
            public string Message = string.Empty;
        }

        private readonly List<PetCareEntry> petCareEntries = new List<PetCareEntry>();
        private bool petCareListVisible = false;
        private string petCareListStatus = string.Empty;
        private float petCareNextScanAllowedAt = 0f;
        private int petCareDogTeaseVitalityCost = -1;
        // Auto stats refresh: there are NO value-change EventCenter events for vitality/fullness
        // (UpdateVitality/UpdateFullness just write component data), so while the list is visible a
        // round-robin poll reloads one pet per interval; feed/favor events trigger immediate reloads.
        private int petCareStatsCursor = 0;
        private float petCareNextAutoStatsAt = 0f;
        private int petCareStatsBurstRemaining = 0;
        private bool petCareAutoHooksRegistered = false;
        private const string PetFeedEndResultEventName = "XDTDataAndProtocol.Events.PetFeedEndResultEvent";
        private const string PetFavorChangedEventName = "XDTDataAndProtocol.Events.PetFavorChangedEvent";

        // ---- Headless dog training (protocol-only, no game mode / panels) ----
        private enum PetPlayHeadlessDogState
        {
            Idle,
            Preparing,
            Beginning,
            Active
        }

        private PetPlayHeadlessDogState petPlayHeadlessDogState = PetPlayHeadlessDogState.Idle;
        private uint petPlayHeadlessDogNetId = 0U;
        private string petPlayHeadlessDogName = string.Empty;
        private int petPlayHeadlessDogLearningId = 0;
        private string petPlayHeadlessDogLearningName = string.Empty;
        private float petPlayHeadlessDogPhaseSentAt = 0f;
        private float petPlayHeadlessDogLastActivityAt = 0f;
        private float petPlayHeadlessDogAwaitingSince = 0f;
        private float petPlayHeadlessDogNextCachePollAt = 0f;
        private bool petPlayHeadlessDogAwaitingAnswer = false;
        private bool petPlayHeadlessDogPerformanceSeen = false;
        private int petPlayHeadlessDogPreActionMotionId = 0;
        private float petPlayHeadlessDogAnswerNotBefore = -1f;
        private int petPlayHeadlessDogNotReadyRetries = 0;

        // ---- Train-until-learned loop: repeats Play sessions on one pet until every unlocked
        // action is learned; when the pet runs out of energy it feeds the ENERGY item (the pet-feed
        // "hobby tool": TableDogfooditem.isHobbyTool / TableCatfooditem.catHobbyTool — Energy Dog
        // Food / Energy Fish Jerky) via BeginFeed's HobbyToolNetId parameter. ----
        private enum PetCareTrainLoopPhase
        {
            Idle,
            WaitSession,
            Delay,
            FeedPrepare,
            Feeding
        }

        private bool petCareTrainLoopEnabled = false;
        private PetCareTrainLoopPhase petCareTrainLoopPhase = PetCareTrainLoopPhase.Idle;
        private uint petCareTrainLoopNetId = 0U;
        private bool petCareTrainLoopIsDog = false;
        private string petCareTrainLoopName = string.Empty;
        private float petCareTrainLoopNextActionAt = 0f;
        private float petCareTrainLoopFeedSentAt = 0f;
        private uint petCareTrainLoopPendingToolNetId = 0U;
        private int petCareTrainLoopFeedAttempts = 0;
        private int petCareTrainLoopSessions = 0;
        private int petCareTrainLoopZeroSessions = 0;
        private int petCareTrainLoopLastVitality = -1;
        private bool petCareTrainLoopForceFeed = false;

        // ---- Dog session trace (Auto vs Headless comparison): logs every server event, dog motion
        // change, panel state change and answer send with timestamps while a dog context is active.
        private int petPlayDogTraceLastMotionId = -1;
        private uint petPlayDogTraceLastPanelNetId = 0U;
        private int petPlayDogTraceLastPanelRound = -1;
        private int petPlayDogTraceLastPanelState = -1;

        // Detailed dog-session trace — gated by the Logging tab's "Pet Play" switch like all other
        // PetPlay logs, and only while a dog context (auto or headless) is active.
        private void DogTraceLog(string message)
        {
            if (!PetPlayLogsEnabled
                || (!this.petPlayAutoDogEnabled && this.petPlayHeadlessDogState == PetPlayHeadlessDogState.Idle))
            {
                return;
            }

            try
            {
                ModLogger.Msg("[PetPlay][dog] t=" + Time.unscaledTime.ToString("F2") + " " + message);
            }
            catch
            {
            }
        }

        private static string FormatPetTeaseQteResult(int result)
        {
            switch (result)
            {
                case 0: return "Invalid";
                case 1: return "Success";
                case 2: return "Failure";
                case 3: return "NotReady";
                case 4: return "Timeout";
                default: return "result" + result;
            }
        }
        private int petPlayHeadlessDogAnswerCount = 0;
        private int petPlayHeadlessDogSuccessCount = 0;
        private int petPlayHeadlessDogFailCount = 0;
        private bool petPlayHeadlessDogHooksRegistered = false;
        private IntPtr petPlayAuraPetPrepareTeaseMethod = IntPtr.Zero;
        private IntPtr petPlayAuraPetEndTeaseMethod = IntPtr.Zero;
        private IntPtr petPlayAuraPetBeginTeaseMethod = IntPtr.Zero;
        private IntPtr petPlayAuraPetBathingBeginMethod = IntPtr.Zero;
        private IntPtr petPlayAuraPetBathingCancelMethod = IntPtr.Zero;

        // ---- Headless pet wash (protocol-only round loop) ----
        private enum PetPlayHeadlessWashState
        {
            Idle,
            Beginning,
            Rounds
        }

        private PetPlayHeadlessWashState petPlayHeadlessWashState = PetPlayHeadlessWashState.Idle;
        private uint petPlayHeadlessWashNetId = 0U;
        private string petPlayHeadlessWashName = string.Empty;
        private int petPlayHeadlessWashRoundTotal = 0;
        private int petPlayHeadlessWashRoundIndex = 0;
        private float petPlayHeadlessWashPhaseSentAt = 0f;
        private float petPlayHeadlessWashLastActivityAt = 0f;
        private float petPlayHeadlessWashNextRoundAt = -1f;
        private bool petPlayHeadlessWashHooksRegistered = false;

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

            Rect trainRect = new Rect(left, num, width, 160f);
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

            rowY += 42f;
            bool nextWash = this.DrawSwitchToggle(new Rect(trainRect.x + 16f, rowY, 250f, 28f), this.petPlayAutoWashEnabled, "Auto Pet Wash");
            if (nextWash != this.petPlayAutoWashEnabled)
            {
                this.petPlayAutoWashEnabled = nextWash;
                this.PetPlayLog("Pet wash " + (nextWash ? "enabled" : "disabled"));
            }

            num += 174;

            GUIStyle petCareStatusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            petCareStatusStyle.normal.textColor = textColor;

            int petCareRowCount = this.petCareListVisible ? this.petCareEntries.Count : 0;
            float petsCardHeight = 116f + petCareRowCount * 72f + (this.petCareListVisible && petCareRowCount == 0 ? 22f : 0f);
            Rect petsRect = new Rect(left, num, width, petsCardHeight);
            GUI.Box(petsRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(petsRect, 1f);
            GUI.Label(new Rect(petsRect.x + 16f, petsRect.y + 12f, 200f, 20f), "MY PETS", labelStyle);
            GUI.Label(new Rect(petsRect.x + 150f, petsRect.y + 14f, width - 166f, 16f), this.petCareListStatus ?? string.Empty, petCareStatusStyle);

            if (GUI.Button(new Rect(petsRect.x + 16f, petsRect.y + 38f, 160f, 30f), this.petCareListVisible ? "Refresh" : "Show My Pets", this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.petCareListVisible = true;
                this.RefreshPetCareList();
            }

            // Train-until-learned loop: Play repeats sessions until every unlocked action is
            // learned, feeding the energy item (Energy Dog Food / Energy Fish Jerky) when tired.
            bool nextTrainLoop = this.DrawSwitchToggle(
                new Rect(petsRect.x + 16f, petsRect.y + 74f, width - 32f, 28f),
                this.petCareTrainLoopEnabled,
                "Train until all learned (+energy food)");
            if (nextTrainLoop != this.petCareTrainLoopEnabled)
            {
                this.petCareTrainLoopEnabled = nextTrainLoop;
                if (!nextTrainLoop)
                {
                    this.StopPetCareTrainLoop("Train loop switched off.");
                }
            }

            if (this.petCareListVisible)
            {
                this.TickPetCareAutoStatsRefresh();

                GUIStyle petNameStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip
                };
                petNameStyle.normal.textColor = Color.white;

                if (petCareRowCount == 0)
                {
                    GUI.Label(new Rect(petsRect.x + 16f, petsRect.y + 110f, width - 32f, 18f), "No owned pets found nearby.", petCareStatusStyle);
                }

                bool petCareBusy = this.TryGetPetCareBusyLabel(out string petCareBusyLabel);
                for (int i = 0; i < petCareRowCount; i++)
                {
                    PetCareEntry entry = this.petCareEntries[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    float rowTop = petsRect.y + 110f + i * 72f;
                    if (i > 0)
                    {
                        this.DrawCardOutline(new Rect(petsRect.x + 12f, rowTop - 5f, width - 24f, 1f), 1f);
                    }

                    GUI.Label(new Rect(petsRect.x + 16f, rowTop, width - 200f, 18f),
                        (string.IsNullOrEmpty(entry.Name) ? entry.NetId.ToString() : entry.Name) + (entry.IsDog ? "  (dog)" : "  (cat)"),
                        petNameStyle);

                    // The buttons occupy only the top ~26px of the row — the stats and message lines
                    // sit below them and can use the full card width.
                    string petStatsLine = "energy " + (entry.Vitality >= 0 ? entry.Vitality.ToString() : "?")
                        + " · food " + (entry.Fullness >= 0 ? entry.Fullness.ToString() : "?")
                        + " · growth " + (entry.Chemistry >= 0 ? entry.Chemistry.ToString() : "?");
                    if (entry.MotionsTotal >= 0)
                    {
                        petStatsLine += entry.IsDog
                            ? " · actions " + entry.MotionsUnlocked + "/" + entry.MotionsTotal + " · learned " + entry.MotionsLearned
                            : " · actions " + entry.MotionsTotal + " · learned " + entry.MotionsLearned;
                    }
                    else
                    {
                        petStatsLine += " · actions ?";
                    }
                    GUI.Label(new Rect(petsRect.x + 16f, rowTop + 28f, width - 32f, 18f), petStatsLine, petCareStatusStyle);
                    GUI.Label(new Rect(petsRect.x + 16f, rowTop + 46f, width - 32f, 18f), entry.Message ?? string.Empty, petCareStatusStyle);

                    bool rowIsActiveSession = petCareBusy && this.IsPetCareEntryActiveSession(entry.NetId);
                    if (rowIsActiveSession)
                    {
                        if (GUI.Button(new Rect(petsRect.xMax - 96f, rowTop, 80f, 26f), "Stop", this.themePrimaryButtonStyle ?? GUI.skin.button))
                        {
                            this.StopPetCareActiveSession();
                        }
                    }
                    else
                    {
                        GUI.enabled = !petCareBusy;
                        if (GUI.Button(new Rect(petsRect.xMax - 180f, rowTop, 80f, 26f), "Play", this.themePrimaryButtonStyle ?? GUI.skin.button))
                        {
                            this.OnPetCarePlayClicked(entry);
                        }
                        if (GUI.Button(new Rect(petsRect.xMax - 96f, rowTop, 80f, 26f), "Wash", this.themePrimaryButtonStyle ?? GUI.skin.button))
                        {
                            this.OnPetCareWashClicked(entry);
                        }
                        GUI.enabled = true;
                    }
                }
            }

            num += Mathf.CeilToInt(petsCardHeight + 14f);
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

            Rect feedActionRect = new Rect(left, num, width, 160f);
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

            float toggleY = buttonY + 40f;
            bool nextSkipFiveStar = this.DrawSwitchToggle(
                new Rect(feedActionRect.x + 16f, toggleY, width - 32f, 28f),
                this.petFeedSkipFiveStarFood,
                this.L("Skip 5 star food"));
            if (nextSkipFiveStar != this.petFeedSkipFiveStarFood)
            {
                this.petFeedSkipFiveStarFood = nextSkipFiveStar;
            }

            if (GUI.Button(new Rect(feedActionRect.x + 16f, toggleY + 34f, width - 32f, 30f), this.L("Show pets favorite food"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.LogNearbyPetFavoriteFoods();
            }

            num = this.DrawPetFeedFavoriteFoodsTable(left, width, feedActionRect.yMax + 12f, labelStyle);

            return num + 40f;
        }

        private void EnsurePetPlayRuntimePatches()
        {
            bool catQteVisible = this.petPlayAutoCatEnabled && Time.unscaledTime < this.petPlayLastCatAnswerAt + 1.2f;
            bool dogQteVisible = this.petPlayAutoDogEnabled && Time.unscaledTime < this.petPlayLastDogAnswerAt + 1.2f;
            bool washActive = this.petPlayAutoWashEnabled && Time.unscaledTime < this.petPlayLastWashClickAt + 2f;
            bool active = catQteVisible || dogQteVisible || washActive;
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
            bool headlessCatActive = this.petPlayHeadlessCatState != PetPlayHeadlessCatState.Idle;
            bool headlessDogActive = this.petPlayHeadlessDogState != PetPlayHeadlessDogState.Idle;
            bool headlessWashActive = this.petPlayHeadlessWashState != PetPlayHeadlessWashState.Idle;
            bool trainLoopActive = this.petCareTrainLoopPhase != PetCareTrainLoopPhase.Idle;
            if (!this.petPlayAutoCatEnabled && !this.petPlayAutoDogEnabled && !this.petPlayAutoWashEnabled
                && !headlessCatActive && !headlessDogActive && !headlessWashActive && !trainLoopActive)
            {
                return;
            }

            // Register the QTE event hooks once a pet-play automation is active. Cat answers straight
            // from the CatPlayQuestionForUiEvent payload; dog round-begin (per-netId) kicks the resolver.
            this.EnsurePetPlayEventHooks();

            if (Time.unscaledTime < this.petPlayNextAutoTickAt)
            {
                return;
            }

            this.petPlayNextAutoTickAt = Time.unscaledTime + 0.12f;

            if (this.petPlayAutoCatEnabled || headlessCatActive)
            {
                // When the CatPlayQuestionForUiEvent hook is live, answers come from the event
                // handler (no scan). Fall back to the UI scan only until the detour installs.
                if (!this.IsGameEventHookInstalled(CatPlayQuestionForUiEventName))
                {
                    this.TryAutoAnswerCatPlayFromQuestionState();
                }
            }

            if (headlessCatActive)
            {
                this.UpdateHeadlessCatPlay();
            }

            if (headlessDogActive)
            {
                this.UpdateHeadlessDogPlay();
            }

            if (headlessWashActive)
            {
                this.UpdateHeadlessWash();
            }

            if (trainLoopActive)
            {
                this.UpdatePetCareTrainLoop();
            }

            if (this.petPlayAutoDogEnabled)
            {
                // Register the full dog event pack in auto mode too — the handlers no-op for the
                // headless FSM but their [dog] trace lines make Auto vs Headless runs comparable.
                this.EnsureHeadlessDogEventHooks();

                // Dog choice needs the live learning/motion state, so the resolver still runs here —
                // the per-netId TeaseDogRoundBeginEvent just clears the throttle so it answers
                // promptly. Scans on its own timer when the hook isn't installed.
                this.TryAutoAnswerDogPlayFromUi();
            }

            if (this.petPlayAutoWashEnabled)
            {
                this.TryAutoPetWash();
            }
        }

        // AuraMono ONLY (XDT* protocol statics — managed reflection is dead on this game).
        private bool TryInvokeCatTeaseQte(uint catNetId, int qteValue)
        {
            return this.TryInvokeAuraMonoCatTeaseQte(catNetId, qteValue);
        }

        private bool TryInvokeDogTeaseQte(uint dogNetId, bool encourage)
        {
            return this.TryInvokeAuraMonoDogTeaseQte(dogNetId, encourage);
        }

        // ---- Event-driven QTE (see docs/GAME_EVENTS.md): the cat question is a GLOBAL event we
        // answer straight from its payload; dog round-begin is a PER-netId event that kicks the
        // existing resolver (the encourage/ignore choice needs the dog's live learning/motion). ----
        private const string CatPlayQuestionForUiEventName = "XDTDataAndProtocol.Events.CatPlayQuestionForUiEvent";
        // CatPlayQuestionForUiEvent: catHandle(uint)@0, questionId(MeowQteType=byte)@4, duration(float)@8 → 12 bytes.
        private const int CatPlayQuestionForUiEventBytes = 12;
        private const string TeaseDogRoundBeginEventName = "XDTDataAndProtocol.Events.TeaseDogRoundBeginEvent";

        // ---- Headless cat play events (all GLOBAL dispatch from MeowProtocolManager) ----
        // TeaseCatStartResultEvent: catHandle(uint)@0, success(bool)@4 → 8 bytes padded.
        private const string TeaseCatStartResultEventName = "XDTDataAndProtocol.Events.TeaseCatStartResultEvent";
        private const int TeaseCatStartResultEventBytes = 8;
        // TeaseCatEndEvent: catHandle@0, motionId@4, motionExpResult@8, motionExpAddition@12,
        // chemistryAddition@16, extraChemistryAddition@20 → 24 bytes. Dispatched only when the
        // session finish reason != Cancel (CancelTease ends silently).
        private const string TeaseCatEndEventName = "XDTDataAndProtocol.Events.TeaseCatEndEvent";
        private const int TeaseCatEndEventBytes = 24;
        // CatPlayExitForUiEvent: catHandle@0, 6×int, reason(MeowTeaseFinishReason int)@28 → 32 bytes.
        // TrackingCatPlay opens CatPlayResultPanel on it — suppressed while headless is active.
        private const string CatPlayExitForUiEventName = "XDTDataAndProtocol.Events.CatPlayExitForUiEvent";
        private const int CatPlayExitForUiEventBytes = 32;
        // CatPlayPromoteForUiEvent: skillId@0, learned(bool)@4, currentScore@8, deltaScore@12 → 16 bytes.
        // TrackingCatPlay opens CatPlayTipPanel on it — suppressed while headless is active.
        private const string CatPlayPromoteForUiEventName = "XDTDataAndProtocol.Events.CatPlayPromoteForUiEvent";
        private const int CatPlayPromoteForUiEventBytes = 16;

        // ---- Headless dog training events ----
        // PetTeasePrepareResultEvent / PetTeaseBeginResultEvent (GLOBAL): netId(uint)@0, isSucceed(bool)@4 → 8 bytes.
        // PetTeaseBeginResultEvent is also what always-on TrackingDogPlay latches the dog netId from
        // (to open the result panel at session end) — suppressed while headless dog play is active.
        private const string PetTeasePrepareResultEventName = "XDTDataAndProtocol.Events.PetTeasePrepareResultEvent";
        private const string PetTeaseBeginResultEventName = "XDTDataAndProtocol.Events.PetTeaseBeginResultEvent";
        private const int PetTeaseResultEventBytes = 8;
        // PetTeaseQteResultEvent (per-netId): result(PetTeaseQteResult int)@0, isSelfPet(bool)@4 → 8 bytes.
        private const string PetTeaseQteResultEventName = "XDTDataAndProtocol.Events.PetTeaseQteResultEvent";
        private const int PetTeaseQteResultEventBytes = 8;
        // TeaseDogPlayEvent (per-netId, empty payload): the dog PERFORMED its round motion — this is
        // the same signal DogPlayStatusPanel opens the answer window on. Judge the motion NOW.
        private const string TeaseDogPlayEventName = "XDTDataAndProtocol.Events.TeaseDogPlayEvent";
        // PetTeaseEndResultEvent (per-netId): petHandle@0 + inline serverData PetTeaseEndNetworkEvent
        // {Result@4, Reason@8, Pet@12, LearningId@16, LearningExp@20, LearningGrowth@24, LearnedGrowth@28} → 32 bytes.
        private const string PetTeaseEndResultEventName = "XDTDataAndProtocol.Events.PetTeaseEndResultEvent";
        private const int PetTeaseEndResultEventBytes = 32;

        // ---- Headless wash events ----
        // PetBathBeginResultClientEvent (GLOBAL): Result(PetBathingBeginResult int)@0, petHandle@4, roundTotal@8 → 12 bytes.
        private const string PetBathBeginResultClientEventName = "XDTDataAndProtocol.Events.PetBathBeginResultClientEvent";
        private const int PetBathBeginResultClientEventBytes = 12;
        // PetBathClickResultClientEvent (GLOBAL): isSucceed(bool)@0, petHandle@4, roundIndex@8, roundDuration(float)@12 → 16 bytes.
        private const string PetBathClickResultClientEventName = "XDTDataAndProtocol.Events.PetBathClickResultClientEvent";
        private const int PetBathClickResultClientEventBytes = 16;
        // PetBathRoundEndClientEvent (per-netId): IsLastRound(bool)@0 → 1 byte.
        private const string PetBathRoundEndClientEventName = "XDTDataAndProtocol.Events.PetBathRoundEndClientEvent";
        private const int PetBathRoundEndClientEventBytes = 1;
        // PetBathEndResultClientEvent (per-netId): empty payload, dispatched on bathing Success.
        private const string PetBathEndResultClientEventName = "XDTDataAndProtocol.Events.PetBathEndResultClientEvent";

        private bool petPlayEventHooksRegistered;

        private void EnsurePetPlayEventHooks()
        {
            if (this.petPlayEventHooksRegistered)
            {
                return;
            }

            this.petPlayEventHooksRegistered = true;
            this.RegisterGameEventHook(CatPlayQuestionForUiEventName, CatPlayQuestionForUiEventBytes, this.OnCatPlayQuestionEvent);
            this.RegisterGameEventHookByNetId(TeaseDogRoundBeginEventName, 0, this.OnTeaseDogRoundBeginEvent);
        }

        // Cat QTE question appeared — answer directly. MeowQteType {Up=0,Down=1,Shake=2} maps 1:1 to
        // the answer enum MeowTeaseQteType, so qteValue == (int)questionId (no sprite scan needed).
        private void OnCatPlayQuestionEvent(GameEventSnapshot e)
        {
            bool headlessActive = this.petPlayHeadlessCatState != PetPlayHeadlessCatState.Idle;
            if (!this.petPlayAutoCatEnabled && !headlessActive)
            {
                return;
            }

            uint catNetId = e.ReadUInt32(0);
            int qteValue = e.ReadByte(4);
            if (catNetId == 0U)
            {
                return;
            }

            bool headlessCat = headlessActive && catNetId == this.petPlayHeadlessCatNetId;
            if (headlessCat)
            {
                this.petPlayHeadlessCatLastActivityAt = Time.unscaledTime;
            }

            // One answer per (cat, qte) within a short window — the event normally fires once per
            // question, but guard against any duplicate dispatch.
            if (catNetId == this.petPlayLastCatNetId
                && qteValue == this.petPlayLastCatQte
                && Time.unscaledTime - this.petPlayLastCatAnswerAt < 0.85f)
            {
                return;
            }

            this.petPlayLastCatNetId = catNetId;
            this.petPlayLastCatQte = qteValue;
            this.petPlayLastCatAnswerAt = Time.unscaledTime;

            if (this.TryInvokeCatTeaseQte(catNetId, qteValue))
            {
                this.petPlayCatAnswerCount++;
                if (headlessCat)
                {
                    this.petPlayHeadlessCatAnswerCount++;
                    this.SetPetCareMessage(catNetId, "Playing: " + this.petPlayHeadlessCatAnswerCount + " QTE answered...");
                }
                this.PetPlayLog("Cat QTE answered via event netId=" + catNetId + " type=" + qteValue + (headlessCat ? " (headless)" : string.Empty) + ".");
            }
        }

        // Dog round began (per-netId) — clear the resolver throttle so the next automation tick
        // answers immediately instead of waiting up to the scan interval. The choice itself is
        // resolved by TryAutoAnswerDogPlayFromUi from the dog's live state.
        private void OnTeaseDogRoundBeginEvent(GameEventSnapshot e)
        {
            this.DogTraceLog("EVENT RoundBegin netId=" + e.NetId
                + " playHook=" + this.IsGameEventHookInstalled(TeaseDogPlayEventName));

            // Headless dog training: a round began for our dog — arm the answer poll (no UI panel
            // exists to read the round from). Snapshot the dog's current motion as the baseline so
            // the motion-watch fallback can detect the NEW performance of this round.
            if (this.petPlayHeadlessDogState != PetPlayHeadlessDogState.Idle
                && e.NetId == this.petPlayHeadlessDogNetId)
            {
                if (this.petPlayHeadlessDogState == PetPlayHeadlessDogState.Beginning)
                {
                    this.petPlayHeadlessDogState = PetPlayHeadlessDogState.Active;
                }

                this.petPlayHeadlessDogAwaitingAnswer = true;
                this.petPlayHeadlessDogPerformanceSeen = false;
                this.petPlayHeadlessDogAnswerNotBefore = -1f;
                this.petPlayHeadlessDogNotReadyRetries = 0;
                this.petPlayHeadlessDogAwaitingSince = Time.unscaledTime;
                this.petPlayHeadlessDogNextCachePollAt = Time.unscaledTime + 0.1f;
                this.petPlayHeadlessDogLastActivityAt = Time.unscaledTime;
            }

            if (!this.petPlayAutoDogEnabled)
            {
                return;
            }

            this.petPlayNextDogRoundScanAt = 0f;
            this.petPlayLastDogRound = -1; // a new round began — allow re-answering this dog
            this.PetPlayLog("Dog round-begin event netId=" + e.NetId + " -> kicking resolver.");
        }

        // ---- Headless cat play: protocol-only session (BeginTease → server QTE pushes → TeaseQte
        // answers → natural end). No client game mode is entered, so the status panels never open
        // (they are mode-gated via UIEventBridge). The two reactive popups that WOULD open from the
        // always-on TrackingPanel — CatPlayResultPanel (exit) and CatPlayTipPanel (promote) — are
        // suppressed at the dispatch detour while the session runs. Question bubbles stay visible
        // as live feedback. See .research-record/PET_QTE_HEADLESS_REPORT.md. ----

        private void StartHeadlessCatPlayForTarget(uint catNetId, string catName)
        {
            if (this.petPlayHeadlessCatState != PetPlayHeadlessCatState.Idle || catNetId == 0U)
            {
                return;
            }

            this.EnsurePetPlayEventHooks();
            this.EnsureHeadlessCatEventHooks();

            this.petPlayHeadlessCatNetId = catNetId;
            this.petPlayHeadlessCatAnswerCount = 0;
            this.petPlayHeadlessCatLastExitReason = -1;
            this.SetHeadlessCatUiSuppression(true);

            if (!this.TryInvokeCatBeginTease(catNetId))
            {
                this.SetHeadlessCatUiSuppression(false);
                this.petPlayHeadlessCatNetId = 0U;
                this.SetPetCareMessage(catNetId, "Play start failed (protocol unavailable).");
                return;
            }

            this.petPlayHeadlessCatState = PetPlayHeadlessCatState.Starting;
            this.petPlayHeadlessCatStartSentAt = Time.unscaledTime;
            this.petPlayHeadlessCatLastActivityAt = Time.unscaledTime;
            this.SetPetCareMessage(catNetId, "Play: asking server...");
            this.PetPlayLog("Headless cat play: BeginTease sent netId=" + catNetId + " name=" + catName + ".");
        }

        private void StopHeadlessCatPlay(string reason)
        {
            if (this.petPlayHeadlessCatState == PetPlayHeadlessCatState.Idle)
            {
                return;
            }

            // CancelTease ends silently: EndTeaseResult skips the exit/end UI events for
            // reason == Cancel, so no result panel and no TeaseCatEndEvent will follow.
            this.TryInvokeCatCancelTease(this.petPlayHeadlessCatNetId);
            this.FinishHeadlessCatPlay("Stopped (" + reason + ") after " + this.petPlayHeadlessCatAnswerCount + " answers.");
        }

        private void FinishHeadlessCatPlay(string status)
        {
            uint endedNetId = this.petPlayHeadlessCatNetId;
            this.petPlayHeadlessCatState = PetPlayHeadlessCatState.Idle;
            this.petPlayHeadlessCatNetId = 0U;
            this.SetHeadlessCatUiSuppression(false);
            if (endedNetId != 0U)
            {
                this.SetPetCareMessage(endedNetId, status);
                this.RefreshPetCareEntryStats(endedNetId);
                this.OnPetCareTrainLoopSessionFinished(endedNetId, this.petPlayHeadlessCatAnswerCount);
            }
            this.PetPlayLog("Headless cat play: " + status);
        }

        private void EnsureHeadlessCatEventHooks()
        {
            if (this.petPlayHeadlessCatHooksRegistered)
            {
                return;
            }

            this.petPlayHeadlessCatHooksRegistered = true;
            this.RegisterGameEventHook(TeaseCatStartResultEventName, TeaseCatStartResultEventBytes, this.OnTeaseCatStartResultEvent);
            this.RegisterGameEventHook(TeaseCatEndEventName, TeaseCatEndEventBytes, this.OnTeaseCatEndEvent);
            this.RegisterGameEventHook(CatPlayExitForUiEventName, CatPlayExitForUiEventBytes, this.OnCatPlayExitForUiEvent);
            this.RegisterGameEventHook(CatPlayPromoteForUiEventName, CatPlayPromoteForUiEventBytes, this.OnCatPlayPromoteForUiEvent);
        }

        // Suppress-forward is toggled ONLY for the headless session window so normal UI-mode play
        // keeps its result/tip panels. Our own handlers still see suppressed dispatches (the detour
        // reads the payload before deciding whether to forward).
        private void SetHeadlessCatUiSuppression(bool on)
        {
            this.SetGameEventHookSuppressForward(CatPlayExitForUiEventName, on);
            this.SetGameEventHookSuppressForward(CatPlayPromoteForUiEventName, on);
        }

        private void OnTeaseCatStartResultEvent(GameEventSnapshot e)
        {
            if (this.petPlayHeadlessCatState != PetPlayHeadlessCatState.Starting
                || e.ReadUInt32(0) != this.petPlayHeadlessCatNetId)
            {
                return;
            }

            if (e.ReadBool(4))
            {
                this.petPlayHeadlessCatState = PetPlayHeadlessCatState.Active;
                this.petPlayHeadlessCatLastActivityAt = Time.unscaledTime;
                this.SetPetCareMessage(this.petPlayHeadlessCatNetId, "Playing: session accepted - cat is coming, waiting for first QTE...");
                this.PetPlayLog("Headless cat play: server accepted BeginTease netId=" + this.petPlayHeadlessCatNetId + ".");
            }
            else
            {
                // Server rejected (hungry/tired/stamina/occupied/…) — the game shows its own toast.
                this.FinishHeadlessCatPlay("Start rejected by server (see game toast).");
            }
        }

        private void OnTeaseCatEndEvent(GameEventSnapshot e)
        {
            if (this.petPlayHeadlessCatState == PetPlayHeadlessCatState.Idle
                || e.ReadUInt32(0) != this.petPlayHeadlessCatNetId)
            {
                return;
            }

            int motionId = e.ReadInt32(4);
            int motionExpAddition = e.ReadInt32(12);
            int chemistryAddition = e.ReadInt32(16) + e.ReadInt32(20);
            string reasonText = this.petPlayHeadlessCatLastExitReason >= 0
                ? FormatMeowTeaseFinishReason(this.petPlayHeadlessCatLastExitReason)
                : "?";
            this.FinishHeadlessCatPlay("Ended (" + reasonText + "): answers=" + this.petPlayHeadlessCatAnswerCount
                + " motion=" + motionId + " exp+=" + motionExpAddition + " chem+=" + chemistryAddition + ".");
        }

        // Suppressed from the UI while headless — we still read the finish reason for the summary.
        private void OnCatPlayExitForUiEvent(GameEventSnapshot e)
        {
            if (this.petPlayHeadlessCatState == PetPlayHeadlessCatState.Idle
                || e.ReadUInt32(0) != this.petPlayHeadlessCatNetId)
            {
                return;
            }

            this.petPlayHeadlessCatLastExitReason = e.ReadInt32(28);
            this.petPlayHeadlessCatLastActivityAt = Time.unscaledTime;
        }

        private void OnCatPlayPromoteForUiEvent(GameEventSnapshot e)
        {
            if (this.petPlayHeadlessCatState == PetPlayHeadlessCatState.Idle)
            {
                return;
            }

            // Skill/exp promote tick (tip panel suppressed) — counts as session activity and gives
            // the row a progress line: skillId@0, learned(bool)@4, currentScore@8, deltaScore@12.
            this.petPlayHeadlessCatLastActivityAt = Time.unscaledTime;
            int skillId = e.ReadInt32(0);
            bool learned = e.ReadBool(4);
            int currentScore = e.ReadInt32(8);
            int deltaScore = e.ReadInt32(12);
            this.SetPetCareMessage(this.petPlayHeadlessCatNetId,
                "Playing: " + this.petPlayHeadlessCatAnswerCount + " answered · skill " + skillId
                + " exp +" + deltaScore + " (" + currentScore + ")" + (learned ? " · LEARNED!" : string.Empty));
        }

        private void UpdateHeadlessCatPlay()
        {
            float now = Time.unscaledTime;
            if (this.petPlayHeadlessCatState == PetPlayHeadlessCatState.Starting
                && now - this.petPlayHeadlessCatStartSentAt > 6f)
            {
                this.FinishHeadlessCatPlay("Start timeout: no TeaseCatStartResultEvent within 6s.");
            }
            else if (this.petPlayHeadlessCatState == PetPlayHeadlessCatState.Active
                && now - this.petPlayHeadlessCatLastActivityAt > 45f)
            {
                this.TryInvokeCatCancelTease(this.petPlayHeadlessCatNetId);
                this.FinishHeadlessCatPlay("Watchdog: no QTE activity for 45s — cancelled.");
            }
        }

        private static string FormatMeowTeaseFinishReason(int reason)
        {
            switch (reason)
            {
                case 0: return "Quit";
                case 1: return "Offline";
                case 2: return "MaxTargetMotionCount";
                case 3: return "MaxFailedCount";
                case 4: return "MaxMotionLearnedCount";
                case 5: return "Cancel";
                default: return "reason" + reason;
            }
        }

        // AuraMono ONLY (XDT* protocol statics — managed reflection is dead on this game).
        private bool TryInvokeCatBeginTease(uint catNetId)
        {
            return this.TryInvokeAuraMonoMeowUIntMethod("BeginTease", ref this.petPlayAuraMeowBeginTeaseMethod, catNetId);
        }

        private bool TryInvokeCatCancelTease(uint catNetId)
        {
            return this.TryInvokeAuraMonoMeowUIntMethod("CancelTease", ref this.petPlayAuraMeowCancelTeaseMethod, catNetId);
        }

        private unsafe bool TryInvokeAuraMonoMeowUIntMethod(string methodName, ref IntPtr methodCache, uint netId)
        {
            try
            {
                if (methodCache == IntPtr.Zero)
                {
                    if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                    {
                        this.PetPlayLog("Aura Meow." + methodName + " unavailable: AuraMono API not ready.");
                        return false;
                    }

                    IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Meow.MeowProtocolManager");
                    if (protocolClass == IntPtr.Zero)
                    {
                        this.PetPlayLog("Aura Meow." + methodName + " unavailable: MeowProtocolManager class not found.");
                        return false;
                    }

                    methodCache = this.FindAuraMonoMethodOnHierarchy(protocolClass, methodName, 1);
                }

                if (methodCache == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    this.PetPlayLog("Aura Meow." + methodName + " unavailable: method not resolved.");
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&netId);
                auraMonoRuntimeInvoke(methodCache, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.PetPlayLog("Aura Meow." + methodName + " netId=" + netId + " exc=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                this.PetPlayLog("Aura Meow." + methodName + " exception: " + ex.Message);
                return false;
            }
        }

        // ================= My Pets list (per-pet Play / Wash rows) =================

        private void RefreshPetCareList()
        {
            if (Time.realtimeSinceStartup < this.petCareNextScanAllowedAt)
            {
                return;
            }

            this.petCareNextScanAllowedAt = Time.realtimeSinceStartup + 1.5f;
            this.EnsurePetCareAutoRefreshHooks();

            Dictionary<uint, string> oldMessages = new Dictionary<uint, string>();
            foreach (PetCareEntry old in this.petCareEntries)
            {
                if (old != null && old.NetId != 0U && !string.IsNullOrEmpty(old.Message))
                {
                    oldMessages[old.NetId] = old.Message;
                }
            }

            this.petCareEntries.Clear();

            List<PetFeedTarget> pets = new List<PetFeedTarget>();
            this.TryCollectPetFeedPetList(false, pets, out int catCount, out string catStatus);
            this.TryCollectPetFeedPetList(true, pets, out int dogCount, out string dogStatus);

            foreach (PetFeedTarget pet in pets)
            {
                if (pet == null || pet.NetId == 0U || pet.IsMine != true)
                {
                    continue;
                }

                PetCareEntry entry = new PetCareEntry
                {
                    NetId = pet.NetId,
                    Name = pet.Name ?? string.Empty,
                    IsDog = pet.IsDog,
                    Fullness = pet.CurrentFullness
                };
                this.TryLoadPetCareStats(entry);
                this.TryLoadPetCareMotionCounts(entry);
                if (oldMessages.TryGetValue(entry.NetId, out string oldMessage))
                {
                    entry.Message = oldMessage;
                }

                this.petCareEntries.Add(entry);
            }

            this.petCareListStatus = "mine=" + this.petCareEntries.Count + " scanned=" + (catCount + dogCount);
            this.PetPlayLog("Pet care list: " + this.petCareListStatus + " catStatus=" + catStatus + " dogStatus=" + dogStatus);
        }

        // Fills vitality / fullness / chemistry (growth) / name from PetSystem.GetPetComponentData
        // via the Mono bridge. AuraMono ONLY: managed reflection over EcsClient/XDT* types is dead
        // code on this game (user rule — see prefer-auramono memory / TYPE_RESOLUTION.md).
        private bool TryLoadPetCareStats(PetCareEntry entry)
        {
            if (entry == null || entry.NetId == 0U)
            {
                return false;
            }

            return this.TryLoadPetCareStatsAuraMono(entry);
        }

        private unsafe bool TryLoadPetCareStatsAuraMono(PetCareEntry entry)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
                {
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Pet.PetSystem", out IntPtr petSystemObj)
                    || petSystemObj == IntPtr.Zero)
                {
                    this.PetPlayLog("Pet care stats: AuraMono PetSystem module unavailable.");
                    return false;
                }

                IntPtr petSystemClass = auraMonoObjectGetClass(petSystemObj);
                IntPtr getDataMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "GetPetComponentData", 2);
                if (getDataMethod == IntPtr.Zero)
                {
                    this.PetPlayLog("Pet care stats: AuraMono GetPetComponentData(2) unavailable.");
                    return false;
                }

                if (!this.TryGetPetFeedAuraEntityTypeValue(entry.IsDog, out int entityTypeValue, out string enumStatus))
                {
                    this.PetPlayLog("Pet care stats: entity type value unavailable: " + enumStatus);
                    return false;
                }

                uint netId = entry.NetId;
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&entityTypeValue);
                args[1] = (IntPtr)(&netId);
                IntPtr dataObj = auraMonoRuntimeInvoke(getDataMethod, petSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || dataObj == IntPtr.Zero)
                {
                    return false;
                }

                // Boxed PetComponentData return — pin while member reads allocate (sgen moves).
                uint dataPin = AuraMonoPinNew(dataObj);
                try
                {
                    bool any = false;
                    if (this.TryGetNestedMonoIntMember(dataObj, out int vitality, "animalComponentData", "vitality"))
                    {
                        entry.Vitality = vitality;
                        any = true;
                    }

                    if (this.TryGetNestedMonoIntMember(dataObj, out int fullness, "animalComponentData", "fullness"))
                    {
                        entry.Fullness = fullness;
                        any = true;
                    }

                    if (this.TryGetMonoIntMember(dataObj, "chemistry", out int chemistry))
                    {
                        entry.Chemistry = chemistry;
                        any = true;
                    }

                    if (string.IsNullOrEmpty(entry.Name)
                        && this.TryGetMonoObjectMember(dataObj, "animalComponentData", out IntPtr animalObj) && animalObj != IntPtr.Zero
                        && this.TryGetMonoStringMember(animalObj, "name", out string petName) && !string.IsNullOrEmpty(petName))
                    {
                        entry.Name = petName;
                    }

                    return any;
                }
                finally
                {
                    AuraMonoPinFree(dataPin);
                }
            }
            catch (Exception ex)
            {
                this.PetPlayLog("Pet care stats (aura) netId=" + entry.NetId + " exception: " + ex.Message);
                return false;
            }
        }

        // One pet per interval keeps the OnGUI-path cost negligible; a "burst" (after favor-changed
        // or a finished session) walks the whole list quickly.
        private void TickPetCareAutoStatsRefresh()
        {
            if (this.petCareEntries.Count == 0 || Time.unscaledTime < this.petCareNextAutoStatsAt)
            {
                return;
            }

            float interval = this.petCareStatsBurstRemaining > 0 ? 0.15f : 0.7f;
            this.petCareNextAutoStatsAt = Time.unscaledTime + interval;
            if (this.petCareStatsBurstRemaining > 0)
            {
                this.petCareStatsBurstRemaining--;
            }

            if (this.petCareStatsCursor >= this.petCareEntries.Count)
            {
                this.petCareStatsCursor = 0;
            }

            PetCareEntry entry = this.petCareEntries[this.petCareStatsCursor];
            this.petCareStatsCursor++;
            if (entry != null)
            {
                this.TryLoadPetCareStats(entry);
            }
        }

        private void EnsurePetCareAutoRefreshHooks()
        {
            if (this.petCareAutoHooksRegistered)
            {
                return;
            }

            this.petCareAutoHooksRegistered = true;
            // Feeding finished for a specific pet (per-netId) — its fullness/vitality just changed.
            this.RegisterGameEventHookByNetId(PetFeedEndResultEventName, 4, this.OnPetCareFeedEndEvent);
            // Own-pet chemistry grew (global, payload = delta only, no netId) — burst-refresh all rows.
            this.RegisterGameEventHook(PetFavorChangedEventName, 4, this.OnPetCareFavorChangedEvent);
        }

        private void OnPetCareFeedEndEvent(GameEventSnapshot e)
        {
            this.RefreshPetCareEntryStats(e.NetId);

            // Train loop: the energy feed completed — verify it actually restored energy and resume.
            if (this.petCareTrainLoopPhase == PetCareTrainLoopPhase.Feeding
                && e.NetId == this.petCareTrainLoopNetId)
            {
                for (int i = 0; i < this.petCareEntries.Count; i++)
                {
                    PetCareEntry entry = this.petCareEntries[i];
                    if (entry != null && entry.NetId == e.NetId)
                    {
                        if (entry.Vitality > this.petCareTrainLoopLastVitality)
                        {
                            this.petCareTrainLoopFeedAttempts = 0;
                        }
                        break;
                    }
                }

                this.petCareTrainLoopPhase = PetCareTrainLoopPhase.Delay;
                this.petCareTrainLoopNextActionAt = Time.unscaledTime + 1.2f;
            }
        }

        private void OnPetCareFavorChangedEvent(GameEventSnapshot e)
        {
            this.petCareStatsBurstRemaining = this.petCareEntries.Count;
            this.petCareNextAutoStatsAt = 0f;
        }

        private void RefreshPetCareEntryStats(uint netId)
        {
            if (netId == 0U)
            {
                return;
            }

            for (int i = 0; i < this.petCareEntries.Count; i++)
            {
                PetCareEntry entry = this.petCareEntries[i];
                if (entry != null && entry.NetId == netId)
                {
                    this.TryLoadPetCareStats(entry);
                    this.TryLoadPetCareMotionCounts(entry);
                    break;
                }
            }
        }

        // Counts the pet's learning motions: unlocked / total and how many of the unlocked ones are
        // already learned. Dogs gate unlocks on growth (TableDogLearningMotion.unlockGrowth); cat
        // motions have no unlock gate (TableKittyLearningMotion has no such field) — all count as
        // unlocked. Loaded on list refresh and after sessions — not in the per-second stats poll
        // (it enumerates the whole motion list via AuraMono).
        private void TryLoadPetCareMotionCounts(PetCareEntry entry)
        {
            if (entry == null || entry.NetId == 0U)
            {
                return;
            }

            try
            {
                List<PetCareMotionInfo> motions = new List<PetCareMotionInfo>(24);
                if (!this.TryCollectPetMotionsAuraMono(entry.NetId, entry.IsDog, motions, out int growth, out _) || motions.Count == 0)
                {
                    return;
                }

                int unlocked = 0;
                int learned = 0;
                for (int i = 0; i < motions.Count; i++)
                {
                    PetCareMotionInfo motion = motions[i];
                    if (entry.IsDog
                        && this.TryGetDogLearningMotionUnlockGrowth(motion.Id, out int unlockGrowth)
                        && growth < unlockGrowth)
                    {
                        continue; // locked
                    }

                    unlocked++;
                    if (motion.Exp > 0 && motion.Exp >= motion.Exp2Learn)
                    {
                        learned++;
                    }
                }

                entry.MotionsTotal = motions.Count;
                entry.MotionsUnlocked = unlocked;
                entry.MotionsLearned = learned;
            }
            catch (Exception ex)
            {
                this.PetPlayLog("Pet care motion counts netId=" + entry.NetId + " exception: " + ex.Message);
            }
        }

        private void SetPetCareMessage(uint netId, string message)
        {
            if (netId == 0U)
            {
                return;
            }

            for (int i = 0; i < this.petCareEntries.Count; i++)
            {
                PetCareEntry entry = this.petCareEntries[i];
                if (entry != null && entry.NetId == netId)
                {
                    entry.Message = message ?? string.Empty;
                    break;
                }
            }
        }

        private bool TryGetPetCareBusyLabel(out string busy)
        {
            if (this.petPlayHeadlessCatState != PetPlayHeadlessCatState.Idle)
            {
                busy = "cat play";
                return true;
            }

            if (this.petPlayHeadlessDogState != PetPlayHeadlessDogState.Idle)
            {
                busy = "dog training";
                return true;
            }

            if (this.petPlayHeadlessWashState != PetPlayHeadlessWashState.Idle)
            {
                busy = "washing";
                return true;
            }

            if (this.petCareTrainLoopPhase != PetCareTrainLoopPhase.Idle)
            {
                busy = "train loop";
                return true;
            }

            busy = string.Empty;
            return false;
        }

        private bool IsPetCareEntryActiveSession(uint netId)
        {
            return netId != 0U
                && ((this.petPlayHeadlessCatState != PetPlayHeadlessCatState.Idle && this.petPlayHeadlessCatNetId == netId)
                    || (this.petPlayHeadlessDogState != PetPlayHeadlessDogState.Idle && this.petPlayHeadlessDogNetId == netId)
                    || (this.petPlayHeadlessWashState != PetPlayHeadlessWashState.Idle && this.petPlayHeadlessWashNetId == netId)
                    || (this.petCareTrainLoopPhase != PetCareTrainLoopPhase.Idle && this.petCareTrainLoopNetId == netId));
        }

        private void StopPetCareActiveSession()
        {
            this.StopPetCareTrainLoop("Train loop stopped by user after " + this.petCareTrainLoopSessions + " sessions.");

            if (this.petPlayHeadlessCatState != PetPlayHeadlessCatState.Idle)
            {
                this.StopHeadlessCatPlay("user");
            }

            if (this.petPlayHeadlessDogState != PetPlayHeadlessDogState.Idle)
            {
                this.TryInvokePetEndTease(this.petPlayHeadlessDogNetId);
                this.FinishHeadlessDogPlay("Training stopped by user after " + this.petPlayHeadlessDogAnswerCount + " rounds.");
            }

            if (this.petPlayHeadlessWashState != PetPlayHeadlessWashState.Idle)
            {
                this.TryInvokePetBathingCancel(this.petPlayHeadlessWashNetId);
                this.FinishHeadlessWash("Wash cancelled by user.");
            }
        }

        private void OnPetCarePlayClicked(PetCareEntry entry)
        {
            if (entry == null || entry.NetId == 0U)
            {
                return;
            }

            if (this.TryGetPetCareBusyLabel(out string busy))
            {
                this.SetPetCareMessage(entry.NetId, "Busy: " + busy + " already in progress.");
                return;
            }

            this.TryLoadPetCareStats(entry);

            // Train-until-learned mode: the loop handles hungry/low-energy itself (feeds the energy
            // item) and repeats sessions until every unlocked action is learned.
            if (this.petCareTrainLoopEnabled)
            {
                this.ArmPetCareTrainLoop(entry);
                this.SetPetCareMessage(entry.NetId, "Train loop: starting...");
                return;
            }

            // Same client-side pre-checks the game's interact command does (TeasePetCommand):
            // hungry = fullness <= 0 for owned pets; dog energy = vitality < teaseVitalityPointDecrease.
            if (entry.Fullness == 0)
            {
                this.SetPetCareMessage(entry.NetId, "Pet is hungry - feed it before playing.");
                return;
            }

            if (entry.IsDog)
            {
                int teaseCost = this.GetDogTeaseVitalityCost();
                if (teaseCost > 0 && entry.Vitality >= 0 && entry.Vitality < teaseCost)
                {
                    this.SetPetCareMessage(entry.NetId, "Not enough energy for play (" + entry.Vitality + "/" + teaseCost + " needed).");
                    return;
                }

                this.StartHeadlessDogPlay(entry.NetId, entry.Name);
            }
            else
            {
                if (entry.Vitality == 0)
                {
                    this.SetPetCareMessage(entry.NetId, "Not enough energy for play.");
                    return;
                }

                this.StartHeadlessCatPlayForTarget(entry.NetId, entry.Name);
            }
        }

        private void OnPetCareWashClicked(PetCareEntry entry)
        {
            if (entry == null || entry.NetId == 0U)
            {
                return;
            }

            if (this.TryGetPetCareBusyLabel(out string busy))
            {
                this.SetPetCareMessage(entry.NetId, "Busy: " + busy + " already in progress.");
                return;
            }

            this.StartHeadlessWash(entry.NetId, entry.Name);
        }

        // ================= Train-until-learned loop =================

        private void ArmPetCareTrainLoop(PetCareEntry entry)
        {
            this.petCareTrainLoopNetId = entry.NetId;
            this.petCareTrainLoopIsDog = entry.IsDog;
            this.petCareTrainLoopName = entry.Name ?? string.Empty;
            this.petCareTrainLoopSessions = 0;
            this.petCareTrainLoopFeedAttempts = 0;
            this.petCareTrainLoopZeroSessions = 0;
            this.petCareTrainLoopForceFeed = false;
            this.petCareTrainLoopLastVitality = entry.Vitality;
            this.petCareTrainLoopPhase = PetCareTrainLoopPhase.Delay;
            this.petCareTrainLoopNextActionAt = Time.unscaledTime;
            this.PetPlayLog("Train loop armed for " + (entry.IsDog ? "dog" : "cat") + " netId=" + entry.NetId + ".");
        }

        private void StopPetCareTrainLoop(string message)
        {
            if (this.petCareTrainLoopPhase == PetCareTrainLoopPhase.Idle)
            {
                return;
            }

            uint netId = this.petCareTrainLoopNetId;
            this.petCareTrainLoopPhase = PetCareTrainLoopPhase.Idle;
            this.petCareTrainLoopNetId = 0U;
            if (netId != 0U && !string.IsNullOrEmpty(message))
            {
                this.SetPetCareMessage(netId, message);
            }
            this.PetPlayLog("Train loop: " + message);
        }

        // Called from the cat/dog session Finish paths so the loop can schedule the next step.
        private void OnPetCareTrainLoopSessionFinished(uint netId, int answersInSession)
        {
            if (this.petCareTrainLoopPhase != PetCareTrainLoopPhase.WaitSession
                || netId != this.petCareTrainLoopNetId)
            {
                return;
            }

            if (answersInSession <= 0)
            {
                this.petCareTrainLoopZeroSessions++;
                // A rejected/empty session is usually low energy the pre-check could not see
                // (e.g. the cat's server-side vitality gate) — try one feed before giving up.
                this.petCareTrainLoopForceFeed = true;
            }
            else
            {
                this.petCareTrainLoopZeroSessions = 0;
            }

            if (this.petCareTrainLoopZeroSessions >= 3)
            {
                this.StopPetCareTrainLoop("Train loop stopped: sessions keep failing (see game toasts).");
                return;
            }

            this.petCareTrainLoopPhase = PetCareTrainLoopPhase.Delay;
            this.petCareTrainLoopNextActionAt = Time.unscaledTime + 2.5f;
        }

        private void UpdatePetCareTrainLoop()
        {
            float now = Time.unscaledTime;
            switch (this.petCareTrainLoopPhase)
            {
                case PetCareTrainLoopPhase.Delay:
                    if (now < this.petCareTrainLoopNextActionAt)
                    {
                        return;
                    }

                    this.PetCareTrainLoopDecideNextStep(now);
                    break;

                case PetCareTrainLoopPhase.FeedPrepare:
                    if (now < this.petCareTrainLoopNextActionAt)
                    {
                        return;
                    }

                    // Prepare was sent; now begin the feed with ONLY the energy item (hobby tool slot).
                    if (!this.TryInvokePetFeedBeginAuraMono(this.petCareTrainLoopNetId, new List<uint>(), this.petCareTrainLoopPendingToolNetId, out string beginStatus))
                    {
                        this.StopPetCareTrainLoop("Train loop stopped: energy feed failed (" + beginStatus + ").");
                        return;
                    }

                    this.petCareTrainLoopPhase = PetCareTrainLoopPhase.Feeding;
                    this.petCareTrainLoopFeedSentAt = now;
                    break;

                case PetCareTrainLoopPhase.Feeding:
                    if (now - this.petCareTrainLoopFeedSentAt > 8f)
                    {
                        // No feed-end event — re-check stats and let the decision step judge.
                        this.RefreshPetCareEntryStats(this.petCareTrainLoopNetId);
                        this.petCareTrainLoopPhase = PetCareTrainLoopPhase.Delay;
                        this.petCareTrainLoopNextActionAt = now + 0.5f;
                    }
                    break;
            }
        }

        private void PetCareTrainLoopDecideNextStep(float now)
        {
            uint netId = this.petCareTrainLoopNetId;
            this.RefreshPetCareEntryStats(netId);

            PetCareEntry entry = null;
            for (int i = 0; i < this.petCareEntries.Count; i++)
            {
                if (this.petCareEntries[i] != null && this.petCareEntries[i].NetId == netId)
                {
                    entry = this.petCareEntries[i];
                    break;
                }
            }

            if (entry == null)
            {
                this.StopPetCareTrainLoop("Train loop stopped: pet not in list anymore.");
                return;
            }

            if (entry.MotionsTotal >= 0 && entry.MotionsUnlocked >= 0 && entry.MotionsLearned >= entry.MotionsUnlocked)
            {
                this.StopPetCareTrainLoop("Train loop DONE: all " + entry.MotionsUnlocked + " unlocked actions learned (sessions=" + this.petCareTrainLoopSessions + ").");
                return;
            }

            int dogCost = entry.IsDog ? this.GetDogTeaseVitalityCost() : 0;
            bool lowEnergy = entry.IsDog
                ? (dogCost > 0 && entry.Vitality >= 0 && entry.Vitality < dogCost)
                : entry.Vitality == 0;
            bool hungry = entry.Fullness == 0;

            if (lowEnergy || hungry || this.petCareTrainLoopForceFeed)
            {
                this.petCareTrainLoopForceFeed = false;
                if (this.petCareTrainLoopFeedAttempts >= 3)
                {
                    this.StopPetCareTrainLoop("Train loop stopped: feeding did not restore energy (attempts=" + this.petCareTrainLoopFeedAttempts + ").");
                    return;
                }

                if (!this.TryFindPetEnergyFood(entry.IsDog, out uint toolNetId, out string foodName, out int foodVitality, out string findStatus))
                {
                    this.StopPetCareTrainLoop("Train loop stopped: no energy food in bag (" + findStatus + ").");
                    return;
                }

                this.petCareTrainLoopFeedAttempts++;
                this.petCareTrainLoopLastVitality = entry.Vitality;
                this.petCareTrainLoopPendingToolNetId = toolNetId;
                this.SetPetCareMessage(netId, "Feeding '" + foodName + "' (+"+ foodVitality + " energy)...");
                this.PetPlayLog("Train loop: feeding energy item '" + foodName + "' netId=" + toolNetId + " to pet " + netId + ".");

                if (!this.TryInvokePetFeedPrepareAuraMono(netId, out string prepStatus))
                {
                    this.StopPetCareTrainLoop("Train loop stopped: feed prepare failed (" + prepStatus + ").");
                    return;
                }

                this.petCareTrainLoopPhase = PetCareTrainLoopPhase.FeedPrepare;
                this.petCareTrainLoopNextActionAt = now + 0.25f;
                return;
            }

            // Energy and food are fine — run the next training session (postpone while any other
            // headless session is still winding down).
            if (this.petPlayHeadlessCatState != PetPlayHeadlessCatState.Idle
                || this.petPlayHeadlessDogState != PetPlayHeadlessDogState.Idle
                || this.petPlayHeadlessWashState != PetPlayHeadlessWashState.Idle)
            {
                this.petCareTrainLoopNextActionAt = now + 2f;
                return;
            }

            this.petCareTrainLoopSessions++;
            this.petCareTrainLoopPhase = PetCareTrainLoopPhase.WaitSession;
            if (entry.IsDog)
            {
                this.StartHeadlessDogPlay(netId, entry.Name);
                if (this.petPlayHeadlessDogState == PetPlayHeadlessDogState.Idle)
                {
                    this.StopPetCareTrainLoop("Train loop stopped: dog session failed to start.");
                }
            }
            else
            {
                this.StartHeadlessCatPlayForTarget(netId, entry.Name);
                if (this.petPlayHeadlessCatState == PetPlayHeadlessCatState.Idle)
                {
                    this.StopPetCareTrainLoop("Train loop stopped: cat session failed to start.");
                }
            }
        }

        // Finds an ENERGY pet item. These are NOT foods: PetSystem sorts hobby-tool items
        // (TableDogfooditem.isHobbyTool / TableCatfooditem.catHobbyTool — Energy Dog Food / Energy
        // Fish Jerky) into a separate _toolItems list exposed via GetTools(); the food collectors
        // never see them. InitFoods(EntityType) scans the BACKPACK AND THE WAREHOUSE in one pass.
        // Prefers the lowest-vitality item to conserve stronger snacks.
        private unsafe bool TryFindPetEnergyFood(bool isDog, out uint itemNetId, out string itemName, out int vitality, out string status)
        {
            itemNetId = 0U;
            itemName = string.Empty;
            vitality = 0;
            status = "not scanned";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
                {
                    status = "AuraMono API unavailable";
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Pet.PetSystem", out IntPtr petSystemObj)
                    || petSystemObj == IntPtr.Zero)
                {
                    status = "PetSystem unavailable";
                    return false;
                }

                IntPtr petSystemClass = auraMonoObjectGetClass(petSystemObj);
                IntPtr initFoodsMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "InitFoods", 1);
                IntPtr getToolsMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "GetTools", 0);
                if (initFoodsMethod == IntPtr.Zero || getToolsMethod == IntPtr.Zero)
                {
                    status = "InitFoods(1)/GetTools unavailable initFoods=0x" + initFoodsMethod.ToInt64().ToString("X")
                        + " getTools=0x" + getToolsMethod.ToInt64().ToString("X");
                    return false;
                }

                if (!this.TryGetPetFeedAuraEntityTypeValue(isDog, out int entityTypeValue, out string enumStatus))
                {
                    status = "entity type unavailable: " + enumStatus;
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* initArgs = stackalloc IntPtr[1];
                initArgs[0] = (IntPtr)(&entityTypeValue);
                auraMonoRuntimeInvoke(initFoodsMethod, petSystemObj, (IntPtr)initArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "InitFoods failed exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                exc = IntPtr.Zero;
                IntPtr toolsObj = auraMonoRuntimeInvoke(getToolsMethod, petSystemObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || toolsObj == IntPtr.Zero)
                {
                    status = "GetTools failed exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                uint toolsPin = AuraMonoPinNew(toolsObj);
                List<IntPtr> items = new List<IntPtr>(8);
                List<uint> itemPins = new List<uint>(8);
                try
                {
                    if (!this.TryEnumerateAuraMonoCollectionItems(toolsObj, items, itemPins) || items.Count == 0)
                    {
                        status = "no energy items in backpack or warehouse";
                        return false;
                    }

                    foreach (IntPtr toolObj in items)
                    {
                        if (toolObj == IntPtr.Zero
                            || !this.TryGetMonoUInt32Member(toolObj, "NetId", out uint toolNetId) || toolNetId == 0U
                            || !this.TryGetMonoIntMember(toolObj, "Count", out int toolCount) || toolCount <= 0)
                        {
                            continue;
                        }

                        this.TryGetMonoIntMember(toolObj, "Vitality", out int toolVitality);
                        if (itemNetId == 0U || toolVitality < vitality)
                        {
                            itemNetId = toolNetId;
                            vitality = toolVitality;
                            if (!this.TryGetMonoStringMember(toolObj, "Name", out itemName) || string.IsNullOrEmpty(itemName))
                            {
                                itemName = this.TryGetMonoIntMember(toolObj, "StaticId", out int toolStaticId)
                                    ? ("#" + toolStaticId)
                                    : "energy item";
                            }
                        }
                    }

                    status = itemNetId != 0U ? ("found among " + items.Count + " tool items") : "tool items unreadable";
                    return itemNetId != 0U;
                }
                finally
                {
                    AuraMonoPinFree(toolsPin);
                    FreeAuraMonoPins(itemPins);
                }
            }
            catch (Exception ex)
            {
                status = "energy item scan exception: " + ex.Message;
                return false;
            }
        }

        // ================= Headless dog training (protocol-only) =================
        // PrepareTease -> BeginTease(learningId) -> per-round TeaseDogRoundBeginEvent arms a
        // DogTeaseCache poll -> TeaseQte(encourage = ActionConfig == ActionFormal) -> server ends
        // with PetTeaseEndResultEvent (CompleteActions / TooManyMistakes) or we EndTease.
        // PetTeaseBeginResultEvent is suppressed while active so the always-on TrackingDogPlay never
        // latches our session (its end handler would open CatPlayResultPanel).

        private void StartHeadlessDogPlay(uint dogNetId, string dogName)
        {
            if (this.petPlayHeadlessDogState != PetPlayHeadlessDogState.Idle || dogNetId == 0U)
            {
                return;
            }

            this.EnsurePetPlayEventHooks();
            this.EnsureHeadlessDogEventHooks();

            if (!this.TryPickDogLearningMotion(dogNetId, out int learningId, out string learningName, out string pickStatus))
            {
                this.SetPetCareMessage(dogNetId, pickStatus);
                this.PetPlayLog("Headless dog play aborted: " + pickStatus);
                return;
            }

            this.petPlayHeadlessDogNetId = dogNetId;
            this.petPlayHeadlessDogName = dogName ?? string.Empty;
            this.petPlayHeadlessDogLearningId = learningId;
            this.petPlayHeadlessDogLearningName = learningName ?? string.Empty;
            this.petPlayHeadlessDogAnswerCount = 0;
            this.petPlayHeadlessDogSuccessCount = 0;
            this.petPlayHeadlessDogFailCount = 0;
            this.petPlayHeadlessDogAwaitingAnswer = false;
            this.petPlayHeadlessDogPerformanceSeen = false;
            this.petPlayHeadlessDogAnswerNotBefore = -1f;
            this.petPlayHeadlessDogNotReadyRetries = 0;

            // The dog performs its table preAction (e.g. sniffing) BEFORE the judged motion each
            // round — knowing it lets the motion-watch skip the prepare phase without the play event.
            this.petPlayHeadlessDogPreActionMotionId = 0;
            if (this.TryGetDogLearningMotionData(learningId, out _, out int preActionMotionId, out _))
            {
                this.petPlayHeadlessDogPreActionMotionId = preActionMotionId;
            }

            this.PetPlayLog("Headless dog hooks installed: play=" + this.IsGameEventHookInstalled(TeaseDogPlayEventName)
                + " roundBegin=" + this.IsGameEventHookInstalled(TeaseDogRoundBeginEventName)
                + " qteResult=" + this.IsGameEventHookInstalled(PetTeaseQteResultEventName)
                + " end=" + this.IsGameEventHookInstalled(PetTeaseEndResultEventName)
                + " preAction=" + this.petPlayHeadlessDogPreActionMotionId + ".");
            this.SetGameEventHookSuppressForward(PetTeaseBeginResultEventName, true);

            if (!this.TryInvokePetPrepareTease(dogNetId))
            {
                this.SetGameEventHookSuppressForward(PetTeaseBeginResultEventName, false);
                this.petPlayHeadlessDogNetId = 0U;
                this.SetPetCareMessage(dogNetId, "Training start failed (protocol unavailable).");
                return;
            }

            this.petPlayHeadlessDogState = PetPlayHeadlessDogState.Preparing;
            this.petPlayHeadlessDogPhaseSentAt = Time.unscaledTime;
            this.petPlayHeadlessDogLastActivityAt = Time.unscaledTime;
            this.SetPetCareMessage(dogNetId, "Training: preparing '" + this.petPlayHeadlessDogLearningName + "' (" + pickStatus + ")...");
            this.PetPlayLog("Headless dog play: PrepareTease sent netId=" + dogNetId + " learningId=" + learningId + " (" + pickStatus + ").");
        }

        private void FinishHeadlessDogPlay(string status)
        {
            uint endedNetId = this.petPlayHeadlessDogNetId;
            this.petPlayHeadlessDogState = PetPlayHeadlessDogState.Idle;
            this.petPlayHeadlessDogNetId = 0U;
            this.petPlayHeadlessDogAwaitingAnswer = false;
            this.SetGameEventHookSuppressForward(PetTeaseBeginResultEventName, false);
            if (endedNetId != 0U)
            {
                this.SetPetCareMessage(endedNetId, status);
                this.RefreshPetCareEntryStats(endedNetId);
                this.OnPetCareTrainLoopSessionFinished(endedNetId, this.petPlayHeadlessDogAnswerCount);
            }
            this.PetPlayLog("Headless dog play: " + status);
        }

        private void EnsureHeadlessDogEventHooks()
        {
            if (this.petPlayHeadlessDogHooksRegistered)
            {
                return;
            }

            this.petPlayHeadlessDogHooksRegistered = true;
            this.RegisterGameEventHook(PetTeasePrepareResultEventName, PetTeaseResultEventBytes, this.OnPetTeasePrepareResultEvent);
            this.RegisterGameEventHook(PetTeaseBeginResultEventName, PetTeaseResultEventBytes, this.OnPetTeaseBeginResultEvent);
            this.RegisterGameEventHookByNetId(TeaseDogPlayEventName, 0, this.OnTeaseDogPlayEventHeadless);
            this.RegisterGameEventHookByNetId(PetTeaseQteResultEventName, PetTeaseQteResultEventBytes, this.OnPetTeaseQteResultEvent);
            this.RegisterGameEventHookByNetId(PetTeaseEndResultEventName, PetTeaseEndResultEventBytes, this.OnPetTeaseEndResultEvent);
        }

        private void OnPetTeasePrepareResultEvent(GameEventSnapshot e)
        {
            this.DogTraceLog("EVENT PrepareResult netId=" + e.ReadUInt32(0) + " ok=" + e.ReadBool(4));

            if (this.petPlayHeadlessDogState != PetPlayHeadlessDogState.Preparing
                || e.ReadUInt32(0) != this.petPlayHeadlessDogNetId)
            {
                return;
            }

            if (!e.ReadBool(4))
            {
                this.FinishHeadlessDogPlay("Training rejected by server (hungry/energy/occupied - see toast).");
                return;
            }

            if (!this.TryInvokePetBeginTease(this.petPlayHeadlessDogNetId, this.petPlayHeadlessDogLearningId))
            {
                this.TryInvokePetEndTease(this.petPlayHeadlessDogNetId);
                this.FinishHeadlessDogPlay("BeginTease invoke failed (see log).");
                return;
            }

            this.petPlayHeadlessDogState = PetPlayHeadlessDogState.Beginning;
            this.petPlayHeadlessDogPhaseSentAt = Time.unscaledTime;
            this.petPlayHeadlessDogLastActivityAt = Time.unscaledTime;
        }

        private void OnPetTeaseBeginResultEvent(GameEventSnapshot e)
        {
            this.DogTraceLog("EVENT BeginResult netId=" + e.ReadUInt32(0) + " ok=" + e.ReadBool(4));

            if (this.petPlayHeadlessDogState != PetPlayHeadlessDogState.Beginning
                || e.ReadUInt32(0) != this.petPlayHeadlessDogNetId)
            {
                return;
            }

            if (!e.ReadBool(4))
            {
                this.FinishHeadlessDogPlay("Training begin rejected (motion locked/unavailable - see toast).");
                return;
            }

            this.petPlayHeadlessDogState = PetPlayHeadlessDogState.Active;
            this.petPlayHeadlessDogLastActivityAt = Time.unscaledTime;
            this.SetPetCareMessage(this.petPlayHeadlessDogNetId, "Training '" + this.petPlayHeadlessDogLearningName + "' running...");
            this.PetPlayLog("Headless dog play: session active netId=" + this.petPlayHeadlessDogNetId + ".");
        }

        private void OnPetTeaseQteResultEvent(GameEventSnapshot e)
        {
            this.DogTraceLog("EVENT QteResult netId=" + e.NetId + " result=" + FormatPetTeaseQteResult(e.ReadInt32(0)));

            if (this.petPlayHeadlessDogState == PetPlayHeadlessDogState.Idle
                || e.NetId != this.petPlayHeadlessDogNetId)
            {
                return;
            }

            // PetTeaseQteResult: 0 Invalid, 1 Success, 2 Failure, 3 NotReady, 4 Timeout.
            int result = e.ReadInt32(0);
            if (result == 1)
            {
                this.petPlayHeadlessDogSuccessCount++;
            }
            else if (result == 2 || result == 4)
            {
                this.petPlayHeadlessDogFailCount++;
            }
            else if (result == 3)
            {
                // NotReady: the server did NOT count the answer — we fired before the answer window
                // opened. Re-arm and retry after a beat (bounded, in case the window truly is shut).
                if (this.petPlayHeadlessDogNotReadyRetries < 4)
                {
                    this.petPlayHeadlessDogNotReadyRetries++;
                    this.petPlayHeadlessDogAwaitingAnswer = true;
                    this.petPlayHeadlessDogAwaitingSince = Time.unscaledTime;
                    this.petPlayHeadlessDogAnswerNotBefore = Time.unscaledTime + 0.7f;
                    this.PetPlayLog("Headless dog answer was NotReady - retry " + this.petPlayHeadlessDogNotReadyRetries + " in 0.7s.");
                }
                else
                {
                    this.PetPlayLog("Headless dog answer NotReady retry limit reached - waiting for next round.");
                }
            }

            if (result == 1 || result == 2 || result == 4)
            {
                // Success = the answer was ACCEPTED by the server (comes for encourage and ignore
                // alike); Failure/Timeout = the round was missed.
                this.SetPetCareMessage(this.petPlayHeadlessDogNetId,
                    "Training '" + this.petPlayHeadlessDogLearningName + "': " + (result == 1 ? "answered" : "missed")
                    + " (answered=" + this.petPlayHeadlessDogSuccessCount + " missed=" + this.petPlayHeadlessDogFailCount + ")");
            }

            this.petPlayHeadlessDogLastActivityAt = Time.unscaledTime;
        }

        private void OnPetTeaseEndResultEvent(GameEventSnapshot e)
        {
            this.DogTraceLog("EVENT End netId=" + e.NetId + " result=" + e.ReadInt32(4)
                + " reason=" + FormatPetTeaseEndReason(e.ReadInt32(8))
                + " learningId=" + e.ReadInt32(16) + " exp=" + e.ReadInt32(20)
                + " growth=" + e.ReadInt32(24) + "/" + e.ReadInt32(28));

            if (this.petPlayHeadlessDogState == PetPlayHeadlessDogState.Idle
                || e.NetId != this.petPlayHeadlessDogNetId)
            {
                return;
            }

            int result = e.ReadInt32(4);
            int reason = e.ReadInt32(8);
            int learningExp = e.ReadInt32(20);
            this.FinishHeadlessDogPlay("Training ended (" + FormatPetTeaseEndReason(reason) + ", result=" + result + "): '" + this.petPlayHeadlessDogLearningName
                + "' rounds=" + this.petPlayHeadlessDogAnswerCount
                + " answered=" + this.petPlayHeadlessDogSuccessCount
                + " missed=" + this.petPlayHeadlessDogFailCount
                + " exp=" + learningExp + ".");
        }

        private void UpdateHeadlessDogPlay()
        {
            float now = Time.unscaledTime;
            switch (this.petPlayHeadlessDogState)
            {
                case PetPlayHeadlessDogState.Preparing:
                case PetPlayHeadlessDogState.Beginning:
                    if (now - this.petPlayHeadlessDogPhaseSentAt > 6f)
                    {
                        this.TryInvokePetEndTease(this.petPlayHeadlessDogNetId);
                        this.FinishHeadlessDogPlay("Training start timeout (no server response).");
                    }
                    break;

                case PetPlayHeadlessDogState.Active:
                    if (this.petPlayHeadlessDogAwaitingAnswer && now >= this.petPlayHeadlessDogNextCachePollAt)
                    {
                        this.petPlayHeadlessDogNextCachePollAt = now + 0.15f;
                        this.TryAnswerHeadlessDogRound(now);
                        if (this.petPlayHeadlessDogAwaitingAnswer && now - this.petPlayHeadlessDogAwaitingSince > 15f)
                        {
                            // Final safety: something kept the round pending — drop it, next round re-arms.
                            this.petPlayHeadlessDogAwaitingAnswer = false;
                            this.PetPlayLog("Headless dog round dropped (pending >15s).");
                        }
                    }

                    if (now - this.petPlayHeadlessDogLastActivityAt > 45f)
                    {
                        this.TryInvokePetEndTease(this.petPlayHeadlessDogNetId);
                        this.FinishHeadlessDogPlay("Watchdog: no training activity for 45s - ended.");
                    }
                    break;
            }
        }

        private struct PetCareMotionInfo
        {
            public int Id;
            public int Exp;
            public int Exp2Learn;
            public string Name;
        }

        // The dog performed its round motion — judge NOW, exactly when the game's own answer window
        // opens (this is what makes the working Auto Dog Train correct: the panel it reads updates
        // on this event, so the motion data is the round's performance, not idle/stale state).
        private void OnTeaseDogPlayEventHeadless(GameEventSnapshot e)
        {
            this.DogTraceLog("EVENT Play netId=" + e.NetId + " (answer window opens)");

            if (this.petPlayHeadlessDogState != PetPlayHeadlessDogState.Active
                || e.NetId != this.petPlayHeadlessDogNetId)
            {
                return;
            }

            this.petPlayHeadlessDogPerformanceSeen = true;
            this.petPlayHeadlessDogLastActivityAt = Time.unscaledTime;
            // Human-ish beat before answering: the server opens the answer window with this same
            // message — replying in the exact frame races the round-state commit (NotReady).
            this.petPlayHeadlessDogAnswerNotBefore = Time.unscaledTime + 0.5f;
        }

        // Answers the armed round with the SAME choice resolver the working Auto Dog Train uses
        // (learning-table + live dog motion; all AuraMono) — just with our known learningId instead
        // of reading it from the DogPlayStatusPanel (which never opens headless). Judged only after
        // the performance signal (TeaseDogPlayEvent) or, as a backstop, 4s after round begin.
        private void TryAnswerHeadlessDogRound(float now)
        {
            if (!this.petPlayHeadlessDogAwaitingAnswer)
            {
                return;
            }

            uint dogNetId = this.petPlayHeadlessDogNetId;
            float sinceArm = now - this.petPlayHeadlessDogAwaitingSince;

            // Trace the dog's motion while the round runs — DIAGNOSTIC ONLY. The performance motion
            // syncs ~5s BEFORE the server opens the answer window (proven by the Auto-vs-Headless
            // trace), so judging by motion fires into a closed window and the server discards the
            // answer. The ONLY valid answer gate is TeaseDogPlayEvent (what DogPlayStatusPanel uses).
            if (this.TryGetDogComponentMotionId(dogNetId, out int liveMotionId, out _)
                && liveMotionId != this.petPlayDogTraceLastMotionId)
            {
                this.DogTraceLog("dog motion " + this.petPlayDogTraceLastMotionId + " -> " + liveMotionId
                    + " (preAction=" + this.petPlayHeadlessDogPreActionMotionId + ")");
                this.petPlayDogTraceLastMotionId = liveMotionId;
            }

            if (!this.petPlayHeadlessDogPerformanceSeen)
            {
                if (sinceArm > 12f)
                {
                    // No Play event — the answer window never opened; nothing valid to send.
                    this.petPlayHeadlessDogAwaitingAnswer = false;
                    this.DogTraceLog("no Play event within 12s - skipping round");
                }

                return;
            }

            if (now < this.petPlayHeadlessDogAnswerNotBefore)
            {
                return;
            }

            bool encourage;
            string choiceStatus;
            bool resolved = this.TryResolveDogQteChoiceForLearning(dogNetId, this.petPlayHeadlessDogLearningId, out encourage, out choiceStatus)
                || this.TryResolveDogQteChoiceFromMotion(dogNetId, out encourage, out choiceStatus);

            if (!resolved)
            {
                if (now - this.petPlayHeadlessDogAnswerNotBefore < 2f)
                {
                    return; // retry on the next poll tick — motion may not be readable yet
                }

                // Window is open but the choice is unresolvable — a deliberate "ignore" beats a timeout.
                encourage = false;
                choiceStatus = "unresolved -> ignore (" + choiceStatus + ")";
            }

            this.DogTraceLog("SEND TeaseQte(" + (encourage ? "encourage" : "ignore") + ") headless, sinceRound=" + sinceArm.ToString("F1")
                + "s perfSeen=" + this.petPlayHeadlessDogPerformanceSeen + " retries=" + this.petPlayHeadlessDogNotReadyRetries);
            if (this.TryInvokeDogTeaseQte(dogNetId, encourage))
            {
                this.petPlayHeadlessDogAwaitingAnswer = false;
                this.petPlayHeadlessDogAnswerCount++;
                this.petPlayHeadlessDogLastActivityAt = now;
                this.SetPetCareMessage(dogNetId,
                    "Training '" + this.petPlayHeadlessDogLearningName + "': round " + this.petPlayHeadlessDogAnswerCount
                    + " -> " + (encourage ? "encourage" : "ignore") + "...");
                this.PetPlayLog("Headless dog QTE answered action=" + (encourage ? "encourage" : "ignore")
                    + " perfSeen=" + this.petPlayHeadlessDogPerformanceSeen + " t=" + sinceArm.ToString("F1") + "s " + choiceStatus + ".");
            }
        }

        // Picks the user's requested learning motion: first an unfinished (started but not learned)
        // one; otherwise the first available (unlocked) one.
        private bool TryPickDogLearningMotion(uint dogNetId, out int learningId, out string learningName, out string status)
        {
            learningId = 0;
            learningName = string.Empty;

            List<PetCareMotionInfo> motions = new List<PetCareMotionInfo>(24);
            if (!this.TryCollectDogMotions(dogNetId, motions, out int growth, out status))
            {
                return false;
            }

            if (motions.Count == 0)
            {
                status = "No trainable motions found.";
                return false;
            }

            int unfinishedIndex = -1;
            int notBegunIndex = -1;
            int firstUnlockedIndex = -1;
            int lockedCount = 0;

            for (int i = 0; i < motions.Count; i++)
            {
                PetCareMotionInfo motion = motions[i];
                if (this.TryGetDogLearningMotionUnlockGrowth(motion.Id, out int unlockGrowth) && growth < unlockGrowth)
                {
                    lockedCount++;
                    continue;
                }

                if (firstUnlockedIndex < 0)
                {
                    firstUnlockedIndex = i;
                }

                if (motion.Exp > 0 && motion.Exp < motion.Exp2Learn && unfinishedIndex < 0)
                {
                    unfinishedIndex = i;
                }
                else if (motion.Exp == 0 && notBegunIndex < 0)
                {
                    notBegunIndex = i;
                }
            }

            int pickedIndex;
            if (unfinishedIndex >= 0)
            {
                pickedIndex = unfinishedIndex;
                status = "continuing unfinished";
            }
            else if (notBegunIndex >= 0)
            {
                pickedIndex = notBegunIndex;
                status = "starting new";
            }
            else if (firstUnlockedIndex >= 0)
            {
                pickedIndex = firstUnlockedIndex;
                status = "all learned - repeating";
            }
            else
            {
                status = "No unlocked motions (locked=" + lockedCount + ", growth=" + growth + ").";
                return false;
            }

            learningId = motions[pickedIndex].Id;
            learningName = string.IsNullOrEmpty(motions[pickedIndex].Name) ? ("#" + learningId) : motions[pickedIndex].Name;
            return true;
        }

        // AuraMono ONLY (managed reflection over XDT* types is dead on this game).
        private bool TryCollectDogMotions(uint dogNetId, List<PetCareMotionInfo> motions, out int growth, out string status)
        {
            return this.TryCollectPetMotionsAuraMono(dogNetId, true, motions, out growth, out status);
        }

        private unsafe bool TryCollectPetMotionsAuraMono(uint dogNetId, bool isDog, List<PetCareMotionInfo> motions, out int growth, out string status)
        {
            growth = 0;
            status = "AuraMono motion list unavailable.";

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
                {
                    status = "AuraMono API unavailable.";
                    return false;
                }

                if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Pet.PetSystem", out IntPtr petSystemObj)
                    || petSystemObj == IntPtr.Zero)
                {
                    status = "AuraMono PetSystem module unavailable.";
                    return false;
                }

                IntPtr petSystemClass = auraMonoObjectGetClass(petSystemObj);
                IntPtr getMotionsMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "GetMotionDatas", 2);
                if (getMotionsMethod == IntPtr.Zero)
                {
                    status = "AuraMono GetMotionDatas unavailable.";
                    return false;
                }

                if (!this.TryGetPetFeedAuraEntityTypeValue(isDog, out int entityTypeValue, out string enumStatus))
                {
                    status = "AuraMono entity type unavailable: " + enumStatus;
                    return false;
                }

                uint netId = dogNetId;
                IntPtr exc = IntPtr.Zero;

                // growth (chemistry) for the unlock check.
                IntPtr getDataMethod = this.FindAuraMonoMethodOnHierarchy(petSystemClass, "GetPetComponentData", 2);
                if (getDataMethod != IntPtr.Zero)
                {
                    IntPtr* dataArgs = stackalloc IntPtr[2];
                    dataArgs[0] = (IntPtr)(&entityTypeValue);
                    dataArgs[1] = (IntPtr)(&netId);
                    IntPtr dataObj = auraMonoRuntimeInvoke(getDataMethod, petSystemObj, (IntPtr)dataArgs, ref exc);
                    if (exc == IntPtr.Zero && dataObj != IntPtr.Zero)
                    {
                        uint dataPin = AuraMonoPinNew(dataObj);
                        try
                        {
                            this.TryGetMonoIntMember(dataObj, "chemistry", out growth);
                        }
                        finally
                        {
                            AuraMonoPinFree(dataPin);
                        }
                    }

                    exc = IntPtr.Zero;
                }

                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&entityTypeValue);
                args[1] = (IntPtr)(&netId);
                IntPtr motionsObj = auraMonoRuntimeInvoke(getMotionsMethod, petSystemObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || motionsObj == IntPtr.Zero)
                {
                    status = "AuraMono GetMotionDatas failed exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                uint motionsPin = AuraMonoPinNew(motionsObj);
                List<IntPtr> items = new List<IntPtr>(24);
                List<uint> itemPins = new List<uint>(24);
                try
                {
                    if (!this.TryEnumerateAuraMonoCollectionItems(motionsObj, items, itemPins) || items.Count == 0)
                    {
                        status = "AuraMono motion list empty.";
                        return false;
                    }

                    for (int i = 0; i < items.Count; i++)
                    {
                        IntPtr motionObj = items[i];
                        if (motionObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        if (!this.TryGetMonoIntMember(motionObj, "id", out int id) || id == 0)
                        {
                            continue;
                        }

                        this.TryGetMonoIntMember(motionObj, "exp", out int exp);
                        this.TryGetMonoIntMember(motionObj, "exp2Learn", out int exp2Learn);
                        this.TryGetMonoStringMember(motionObj, "name", out string name);
                        motions.Add(new PetCareMotionInfo
                        {
                            Id = id,
                            Exp = exp,
                            Exp2Learn = exp2Learn,
                            Name = name
                        });
                    }

                    status = "AuraMono motions=" + motions.Count + " growth=" + growth;
                    return motions.Count > 0;
                }
                finally
                {
                    AuraMonoPinFree(motionsPin);
                    FreeAuraMonoPins(itemPins);
                }
            }
            catch (Exception ex)
            {
                status = "AuraMono motion collect exception: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        // Static table data — cache resolved values so repeated count/pick passes cost nothing.
        private readonly Dictionary<int, int> petCareUnlockGrowthCache = new Dictionary<int, int>();

        // AuraMono ONLY: table row via the Mono bridge ("unlockGrowth" resolves to the int property
        // getter — the backing field is a ushort).
        private bool TryGetDogLearningMotionUnlockGrowth(int learningId, out int unlockGrowth)
        {
            if (this.petCareUnlockGrowthCache.TryGetValue(learningId, out unlockGrowth))
            {
                return unlockGrowth >= 0;
            }

            try
            {
                if (this.TryGetAuraMonoDogLearningMotion(learningId, out IntPtr tableObj, out _)
                    && tableObj != IntPtr.Zero
                    && this.TryGetMonoIntMember(tableObj, "unlockGrowth", out unlockGrowth))
                {
                    this.petCareUnlockGrowthCache[learningId] = unlockGrowth;
                    return true;
                }
            }
            catch
            {
            }

            unlockGrowth = 0;
            return false;
        }

        // The exact client-side energy gate the game's TeasePetCommand uses:
        // vitality < TableDogThemes.First().teaseVitalityPointDecrease. AuraMono ONLY.
        private int GetDogTeaseVitalityCost()
        {
            if (this.petCareDogTeaseVitalityCost >= 0)
            {
                return this.petCareDogTeaseVitalityCost;
            }

            int auraCost = this.GetDogTeaseVitalityCostAuraMono();
            if (auraCost >= 0)
            {
                this.petCareDogTeaseVitalityCost = auraCost;
            }

            return auraCost;
        }

        private int GetDogTeaseVitalityCostAuraMono()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null)
                {
                    return -1;
                }

                // TableData lives in the EcsClient image at global namespace (same resolution
                // chain as TryGetAuraMonoDogLearningMotion).
                IntPtr ecsImage = this.FindAuraMonoImage(new string[] { "EcsClient", "EcsClient.dll" });
                IntPtr tableDataClass = ecsImage != IntPtr.Zero ? auraMonoClassFromName(ecsImage, string.Empty, "TableData") : IntPtr.Zero;
                if (tableDataClass == IntPtr.Zero)
                {
                    tableDataClass = this.FindAuraMonoClassAcrossLoadedAssemblies(string.Empty, "TableData");
                }
                if (tableDataClass == IntPtr.Zero)
                {
                    return -1;
                }

                if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableDogThemes", out IntPtr dictObj) || dictObj == IntPtr.Zero)
                {
                    return -1;
                }

                List<IntPtr> entries = new List<IntPtr>(4);
                List<uint> entryPins = new List<uint>(4);
                try
                {
                    if (!this.TryEnumerateAuraMonoCollectionItems(dictObj, entries, entryPins) || entries.Count == 0)
                    {
                        return -1;
                    }

                    for (int i = 0; i < entries.Count; i++)
                    {
                        IntPtr kvObj = entries[i];
                        if (kvObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        if ((this.TryGetMonoObjectMember(kvObj, "Value", out IntPtr rowObj)
                                || this.TryGetMonoObjectMember(kvObj, "value", out rowObj))
                            && rowObj != IntPtr.Zero
                            && this.TryGetMonoIntMember(rowObj, "teaseVitalityPointDecrease", out int cost))
                        {
                            return cost;
                        }

                        break;
                    }

                    return -1;
                }
                finally
                {
                    FreeAuraMonoPins(entryPins);
                }
            }
            catch
            {
                return -1;
            }
        }

        private static string FormatPetTeaseEndReason(int reason)
        {
            switch (reason)
            {
                case 0: return "Quit";
                case 1: return "CompleteActions";
                case 2: return "TooManyMistakes";
                default: return "reason" + reason;
            }
        }

        // ================= Headless pet wash (protocol-only) =================
        // PetBathinghBegin [sic] -> begin result (Success carries roundTotal; Failed = already
        // clean / on cooldown) -> per round: PetBathingRoundStart -> click result (roundIndex,
        // duration) -> per-netId round end (IsLastRound) -> next round -> per-netId end event.
        // The PetBathPanel never opens (it is GamePetBathMode-gated), so no wash UI appears.

        private void StartHeadlessWash(uint petNetId, string petName)
        {
            if (this.petPlayHeadlessWashState != PetPlayHeadlessWashState.Idle || petNetId == 0U)
            {
                return;
            }

            this.EnsureHeadlessWashEventHooks();

            this.petPlayHeadlessWashNetId = petNetId;
            this.petPlayHeadlessWashName = petName ?? string.Empty;
            this.petPlayHeadlessWashRoundTotal = 0;
            this.petPlayHeadlessWashRoundIndex = -1;
            this.petPlayHeadlessWashNextRoundAt = -1f;

            if (!this.TryInvokePetBathingBegin(petNetId))
            {
                this.petPlayHeadlessWashNetId = 0U;
                this.SetPetCareMessage(petNetId, "Wash start failed (protocol unavailable).");
                return;
            }

            this.petPlayHeadlessWashState = PetPlayHeadlessWashState.Beginning;
            this.petPlayHeadlessWashPhaseSentAt = Time.unscaledTime;
            this.petPlayHeadlessWashLastActivityAt = Time.unscaledTime;
            this.SetPetCareMessage(petNetId, "Wash: asking server...");
            this.PetPlayLog("Headless wash: PetBathinghBegin sent netId=" + petNetId + ".");
        }

        private void FinishHeadlessWash(string status)
        {
            uint endedNetId = this.petPlayHeadlessWashNetId;
            this.petPlayHeadlessWashState = PetPlayHeadlessWashState.Idle;
            this.petPlayHeadlessWashNetId = 0U;
            this.petPlayHeadlessWashNextRoundAt = -1f;
            if (endedNetId != 0U)
            {
                this.SetPetCareMessage(endedNetId, status);
                this.RefreshPetCareEntryStats(endedNetId);
            }
            this.PetPlayLog("Headless wash: " + status);
        }

        private void EnsureHeadlessWashEventHooks()
        {
            if (this.petPlayHeadlessWashHooksRegistered)
            {
                return;
            }

            this.petPlayHeadlessWashHooksRegistered = true;
            this.RegisterGameEventHook(PetBathBeginResultClientEventName, PetBathBeginResultClientEventBytes, this.OnPetBathBeginResultEvent);
            this.RegisterGameEventHook(PetBathClickResultClientEventName, PetBathClickResultClientEventBytes, this.OnPetBathClickResultEvent);
            this.RegisterGameEventHookByNetId(PetBathRoundEndClientEventName, PetBathRoundEndClientEventBytes, this.OnPetBathRoundEndEvent);
            this.RegisterGameEventHookByNetId(PetBathEndResultClientEventName, 0, this.OnPetBathEndResultEvent);
        }

        private void OnPetBathBeginResultEvent(GameEventSnapshot e)
        {
            if (this.petPlayHeadlessWashState != PetPlayHeadlessWashState.Beginning
                || e.ReadUInt32(4) != this.petPlayHeadlessWashNetId)
            {
                return;
            }

            // PetBathingBeginResult: 0 Failed, 1 Success, 2 FeatureNotOpen, 3 PetOccupying,
            // 4 PlayerOccupying, 5 NoStamina, 6 Hungry, 7 NotAtHome.
            int result = e.ReadInt32(0);
            switch (result)
            {
                case 1:
                    this.petPlayHeadlessWashRoundTotal = e.ReadInt32(8);
                    this.petPlayHeadlessWashState = PetPlayHeadlessWashState.Rounds;
                    this.petPlayHeadlessWashLastActivityAt = Time.unscaledTime;
                    this.petPlayHeadlessWashNextRoundAt = Time.unscaledTime + 0.35f;
                    this.SetPetCareMessage(this.petPlayHeadlessWashNetId, "Washing: 0/" + this.petPlayHeadlessWashRoundTotal + " rounds...");
                    break;
                case 0:
                    this.FinishHeadlessWash("Pet is already clean - wash unavailable.");
                    break;
                case 3:
                case 4:
                    this.FinishHeadlessWash("Pet or player is busy - wash unavailable.");
                    break;
                case 5:
                    this.FinishHeadlessWash("No player stamina for washing.");
                    break;
                case 6:
                    this.FinishHeadlessWash("Pet is hungry - feed it before washing.");
                    break;
                case 7:
                    this.FinishHeadlessWash("Washing works only at home.");
                    break;
                default:
                    this.FinishHeadlessWash("Wash unavailable (result " + result + ").");
                    break;
            }
        }

        private void OnPetBathClickResultEvent(GameEventSnapshot e)
        {
            if (this.petPlayHeadlessWashState != PetPlayHeadlessWashState.Rounds
                || e.ReadUInt32(4) != this.petPlayHeadlessWashNetId
                || !e.ReadBool(0))
            {
                return;
            }

            this.petPlayHeadlessWashRoundIndex = e.ReadInt32(8);
            this.petPlayHeadlessWashLastActivityAt = Time.unscaledTime;
            this.petPlayHeadlessWashNextRoundAt = -1f;
            this.SetPetCareMessage(this.petPlayHeadlessWashNetId,
                "Washing: round " + (this.petPlayHeadlessWashRoundIndex + 1) + "/" + this.petPlayHeadlessWashRoundTotal + "...");
        }

        private void OnPetBathRoundEndEvent(GameEventSnapshot e)
        {
            if (this.petPlayHeadlessWashState != PetPlayHeadlessWashState.Rounds
                || e.NetId != this.petPlayHeadlessWashNetId)
            {
                return;
            }

            this.petPlayHeadlessWashLastActivityAt = Time.unscaledTime;
            if (!e.ReadBool(0))
            {
                // Next scrub — the game waits for the player's click here; keep a human-ish beat.
                this.petPlayHeadlessWashNextRoundAt = Time.unscaledTime + 0.4f;
            }
        }

        private void OnPetBathEndResultEvent(GameEventSnapshot e)
        {
            if (this.petPlayHeadlessWashState == PetPlayHeadlessWashState.Idle
                || e.NetId != this.petPlayHeadlessWashNetId)
            {
                return;
            }

            this.FinishHeadlessWash("Washed! (" + this.petPlayHeadlessWashRoundTotal + " rounds done)");
        }

        private void UpdateHeadlessWash()
        {
            float now = Time.unscaledTime;
            if (this.petPlayHeadlessWashState == PetPlayHeadlessWashState.Beginning)
            {
                if (now - this.petPlayHeadlessWashPhaseSentAt > 6f)
                {
                    this.FinishHeadlessWash("Wash start timeout (no server response).");
                }
            }
            else if (this.petPlayHeadlessWashState == PetPlayHeadlessWashState.Rounds)
            {
                if (this.petPlayHeadlessWashNextRoundAt >= 0f && now >= this.petPlayHeadlessWashNextRoundAt)
                {
                    this.petPlayHeadlessWashNextRoundAt = -1f;
                    if (!this.TryInvokePetBathingRoundStart(this.petPlayHeadlessWashNetId))
                    {
                        this.TryInvokePetBathingCancel(this.petPlayHeadlessWashNetId);
                        this.FinishHeadlessWash("Wash round invoke failed (see log).");
                        return;
                    }

                    this.petPlayHeadlessWashLastActivityAt = now;
                }

                if (now - this.petPlayHeadlessWashLastActivityAt > 30f)
                {
                    this.TryInvokePetBathingCancel(this.petPlayHeadlessWashNetId);
                    this.FinishHeadlessWash("Wash watchdog: no progress for 30s - cancelled.");
                }
            }
        }

        // ================= PetProtocolManager invokers (managed -> AuraMono fallback) =================

        // AuraMono ONLY (XDT* protocol statics — managed reflection is dead on this game).
        private bool TryInvokePetPrepareTease(uint netId)
        {
            return this.TryInvokeAuraMonoPetUIntMethod("PrepareTease", ref this.petPlayAuraPetPrepareTeaseMethod, netId);
        }

        private bool TryInvokePetEndTease(uint netId)
        {
            return this.TryInvokeAuraMonoPetUIntMethod("EndTease", ref this.petPlayAuraPetEndTeaseMethod, netId);
        }

        // NB: the game method name really is "PetBathinghBegin" (typo in game code).
        private bool TryInvokePetBathingBegin(uint netId)
        {
            return this.TryInvokeAuraMonoPetUIntMethod("PetBathinghBegin", ref this.petPlayAuraPetBathingBeginMethod, netId);
        }

        private bool TryInvokePetBathingCancel(uint netId)
        {
            return this.TryInvokeAuraMonoPetUIntMethod("PetBathingCancel", ref this.petPlayAuraPetBathingCancelMethod, netId);
        }

        private unsafe bool TryInvokeAuraMonoPetUIntMethod(string methodName, ref IntPtr methodCache, uint netId)
        {
            try
            {
                if (methodCache == IntPtr.Zero)
                {
                    if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                    {
                        this.PetPlayLog("Aura Pet." + methodName + " unavailable: AuraMono API not ready.");
                        return false;
                    }

                    IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Pet.PetProtocolManager");
                    if (protocolClass == IntPtr.Zero)
                    {
                        this.PetPlayLog("Aura Pet." + methodName + " unavailable: PetProtocolManager class not found.");
                        return false;
                    }

                    methodCache = this.FindAuraMonoMethodOnHierarchy(protocolClass, methodName, 1);
                }

                if (methodCache == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    this.PetPlayLog("Aura Pet." + methodName + " unavailable: method not resolved.");
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&netId);
                auraMonoRuntimeInvoke(methodCache, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.PetPlayLog("Aura Pet." + methodName + " netId=" + netId + " exc=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                this.PetPlayLog("Aura Pet." + methodName + " exception: " + ex.Message);
                return false;
            }
        }

        // AuraMono ONLY.
        private bool TryInvokePetBeginTease(uint netId, int learningId)
        {
            return this.TryInvokeAuraMonoPetBeginTease(netId, learningId);
        }

        private unsafe bool TryInvokeAuraMonoPetBeginTease(uint netId, int learningId)
        {
            try
            {
                if (this.petPlayAuraPetBeginTeaseMethod == IntPtr.Zero)
                {
                    if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                    {
                        return false;
                    }

                    IntPtr protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Pet.PetProtocolManager");
                    if (protocolClass == IntPtr.Zero)
                    {
                        return false;
                    }

                    this.petPlayAuraPetBeginTeaseMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "BeginTease", 2);
                }

                if (this.petPlayAuraPetBeginTeaseMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
                {
                    this.PetPlayLog("Aura Pet.BeginTease unavailable: method not resolved.");
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&netId);
                args[1] = (IntPtr)(&learningId);
                auraMonoRuntimeInvoke(this.petPlayAuraPetBeginTeaseMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.PetPlayLog("Aura Pet.BeginTease netId=" + netId + " learningId=" + learningId + " exc=0x" + exc.ToInt64().ToString("X"));
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                this.PetPlayLog("Aura Pet.BeginTease exception: " + ex.Message);
                return false;
            }
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

        private unsafe bool TryGetAuraMonoUiView(string viewTypeName, string viewLabel, out IntPtr viewObj, out string status, bool allowServiceDicFallback = true)
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

                if (!this.TryGetAuraMonoUiManagerObject(out IntPtr uiManagerObj, out status, allowServiceDicFallback) || uiManagerObj == IntPtr.Zero)
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

                // viewTypeObj is a freshly-created System.Type mono object, not cached/pinned. There is
                // no mono_gc_disable on this sgen (moving) build, so the GC can relocate/collect it
                // between creation and the GetView invoke that consumes it -> native AV. This periodic
                // GetView poll (e.g. the instrument hotkey guard, every TTL) is exactly the real,
                // no-ProcDump "occasional crash while AFK" the breadcrumbs localized here. Pin it.
                uint viewTypePin = AuraMonoPinNew(viewTypeObj);
                try
                {
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
                finally
                {
                    AuraMonoPinFree(viewTypePin);
                }
            }
            catch (Exception ex)
            {
                status = viewLabel + " exception: " + ex.Message;
                viewObj = IntPtr.Zero;
                return false;
            }
        }

        // The resolved UIManager pinned across frames. It is a per-world singleton, and resolving
        // it (especially the Managers._serviceDic enumeration fallback) is heavy and was a crash
        // source — so cache it once, return the pinned pointer thereafter, and let
        // AuraMonoObjectCache re-resolve only after a world-epoch change or GC collection.
        private AuraMonoObjectCache cachedAuraMonoUiManagerObj;

        private bool TryGetAuraMonoUiManagerObject(out IntPtr uiManagerObj, out string status, bool allowServiceDicFallback = true)
        {
            // Fast path: pinned, world-epoch-validated cache — no per-frame invoke/enumeration.
            if (this.cachedAuraMonoUiManagerObj.TryGet(out uiManagerObj))
            {
                status = "UIManager (cached).";
                return true;
            }

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
                            this.cachedAuraMonoUiManagerObj.Set(uiManagerObj);
                            status = "UIManager.Instance ready.";
                            return true;
                        }
                    }
                }

                if (!allowServiceDicFallback)
                {
                    status = "UIManager.Instance unavailable (serviceDic fallback disabled).";
                    return false;
                }

                if (this.TryGetAuraMonoUiManagerFromManagersServiceDic(out uiManagerObj, out status))
                {
                    this.cachedAuraMonoUiManagerObj.Set(uiManagerObj);
                    return true;
                }

                return false;
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
            List<uint> entryPins = new List<uint>(16);
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(serviceDicObj, entries, entryPins) || entries.Count == 0)
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

                    // The class-name read and GetView lookup below allocate (boxing/strings), so a
                    // GC could move managerObj before we return it. Pin it for the rest of the loop
                    // body (freed with the rest in finally; the caller pins it again via the cache).
                    entryPins.Add(AuraMonoPinNew(managerObj));

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
            finally
            {
                FreeAuraMonoPins(entryPins);
            }
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

            this.DogTraceLog("SEND TeaseQte(" + (encourage ? "encourage" : "ignore") + ") auto, panelRound=" + round);
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

        // DogTeaseCacheComponent is not read anymore: its only access path was managed
        // EntityDataOpt reflection, which is dead on this game (AuraMono-only rule). The choice is
        // resolved from the learning table + the dog's live motion instead (both AuraMono).
        private bool TryResolveDogQteChoice(uint dogNetId, int round, out bool encourage, out string status)
        {
            encourage = true;

            if (this.TryResolveDogQteChoiceFromLearningTable(dogNetId, out encourage, out string learningTableStatus))
            {
                status = learningTableStatus;
                return true;
            }

            if (this.TryResolveDogQteChoiceFromMotion(dogNetId, out encourage, out string motionStatus))
            {
                status = motionStatus;
                return true;
            }

            status = learningTableStatus + " " + motionStatus;
            return false;
        }

        private bool TryResolveDogQteChoiceFromLearningTable(uint dogNetId, out bool encourage, out string status)
        {
            encourage = true;

            if (!this.TryGetDogPlayPanelLearningId(out int learningId, out string learningStatus) || learningId <= 0)
            {
                status = learningStatus;
                return false;
            }

            return this.TryResolveDogQteChoiceForLearning(dogNetId, learningId, out encourage, out status);
        }

        // Shared choice core (Auto Dog Train + headless): encourage when the dog's live motion IS
        // the trained motion (table motionID match, or the motion's requireLearningId matches).
        private bool TryResolveDogQteChoiceForLearning(uint dogNetId, int learningId, out bool encourage, out string status)
        {
            encourage = true;
            status = "DogLearningMotion PreAction unavailable.";

            if (learningId <= 0)
            {
                status = "learningId unavailable.";
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

        // AuraMono ONLY (EcsClient TableData — managed reflection is dead on this game).
        private bool TryGetDogLearningMotionData(int learningId, out int motionId, out int preAction, out string status)
        {
            return this.TryGetDogLearningMotionDataAuraMono(learningId, out motionId, out preAction, out status);
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

        // AuraMono ONLY (EcsClient TableData — managed reflection is dead on this game).
        private bool TryGetDogLearningPreAction(int learningId, out int preAction, out string status)
        {
            return this.TryGetDogLearningPreActionAuraMono(learningId, out preAction, out status);
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

        // AuraMono ONLY (EcsClient TableData — managed reflection is dead on this game).
        private bool TryGetDogMotionLearningInfo(int dogMotionId, out int requireLearningId, out bool teaseNotLearningMotion, out string status)
        {
            return this.TryGetDogMotionLearningInfoAuraMono(dogMotionId, out requireLearningId, out teaseNotLearningMotion, out status);
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

        // AuraMono ONLY (DataCenter/DogComponentData — managed reflection is dead on this game).
        private bool TryGetDogComponentMotionId(uint dogNetId, out int motionId, out string status)
        {
            return this.TryGetDogComponentMotionIdAuraMono(dogNetId, out motionId, out status);
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

                if (dogNetId != this.petPlayDogTraceLastPanelNetId
                    || round != this.petPlayDogTraceLastPanelRound
                    || roundState != this.petPlayDogTraceLastPanelState)
                {
                    this.DogTraceLog("panel netId=" + dogNetId + " round=" + round + " state=" + roundState
                        + " (0=None 1=Ready 2=Play 3=End)");
                    this.petPlayDogTraceLastPanelNetId = dogNetId;
                    this.petPlayDogTraceLastPanelRound = round;
                    this.petPlayDogTraceLastPanelState = roundState;
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

        private void TryAutoPetWash()
        {
            float scanDelay = this.petPlayWashClickLocked ? 0.1f : 0.28f;
            if (Time.unscaledTime < this.petPlayNextWashTickAt)
            {
                return;
            }

            this.petPlayNextWashTickAt = Time.unscaledTime + scanDelay;

            if (!this.TryGetActivePetBathState(out uint petNetId, out bool skillButtonActive, out bool roundActive, out string status) || petNetId == 0U)
            {
                this.petPlayWashClickLocked = false;
                this.petPlayWashSawButtonHidden = false;
                this.petPlayLastWashPetNetId = 0U;
                return;
            }

            if (this.petPlayWashClickLocked)
            {
                if (Time.unscaledTime - this.petPlayLastWashClickAt > 20f)
                {
                    this.petPlayWashClickLocked = false;
                    this.petPlayWashSawButtonHidden = false;
                    this.PetPlayLog("Pet bath lock timeout reset netId=" + petNetId + ".");
                }
                else
                {
                    if (!skillButtonActive)
                    {
                        this.petPlayWashSawButtonHidden = true;
                    }

                    if (this.petPlayWashSawButtonHidden && skillButtonActive && !roundActive)
                    {
                        this.petPlayWashClickLocked = false;
                        this.petPlayWashSawButtonHidden = false;
                    }

                    return;
                }
            }

            if (!skillButtonActive || roundActive)
            {
                return;
            }

            if (!this.TryInvokePetBathingRoundStart(petNetId))
            {
                return;
            }

            this.petPlayLastWashPetNetId = petNetId;
            this.petPlayLastWashClickAt = Time.unscaledTime;
            this.petPlayWashClickCount++;
            this.petPlayWashClickLocked = true;
            this.petPlayWashSawButtonHidden = false;
            this.petPlayNextWashTickAt = Time.unscaledTime + 0.35f;
            this.PetPlayLog("Pet bath click netId=" + petNetId + " total=" + this.petPlayWashClickCount + " " + status + ".");
        }

        private bool TryGetActivePetBathState(out uint petNetId, out bool skillButtonActive, out bool roundActive, out string status)
        {
            petNetId = 0U;
            skillButtonActive = false;
            roundActive = false;
            status = "PetBathPanel not found.";

            try
            {
                if (!this.TryGetAuraMonoUiView("XDTGame.UI.Panel.PetBathPanel", "PetBathPanel", out IntPtr panelObj, out status)
                    || panelObj == IntPtr.Zero)
                {
                    return false;
                }

                if (!this.TryGetMonoUInt32Member(panelObj, "_petNetId", out petNetId) || petNetId == 0U)
                {
                    status = "PetBathPanel._petNetId unavailable.";
                    return false;
                }

                this.TryGetMonoBoolMember(panelObj, "_roundStart", out roundActive);
                if (!this.TryGetPetBathPanelSkillButtonActive(panelObj, out skillButtonActive, out string buttonStatus))
                {
                    status = buttonStatus;
                    return false;
                }

                status = "PetBathPanel netId=" + petNetId
                    + " roundActive=" + roundActive
                    + " skillButton=" + skillButtonActive
                    + " " + buttonStatus;
                return true;
            }
            catch (Exception ex)
            {
                status = "PetBathPanel exception: " + ex.Message;
                return false;
            }
        }

        private bool TryGetPetBathPanelSkillButtonActive(IntPtr panelObj, out bool active, out string status)
        {
            active = false;
            status = "skill_main_hold_widget unavailable.";

            if (panelObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryReadAuraMonoObjectField(panelObj, out IntPtr uiObj, "ui")
                || uiObj == IntPtr.Zero)
            {
                status = "PetBathPanel.ui unavailable.";
                return false;
            }

            if (!this.TryReadAuraMonoObjectField(uiObj, out IntPtr skillWidgetObj, "skill_main_hold_widget")
                || skillWidgetObj == IntPtr.Zero)
            {
                status = "PetBathPanel.skill_main_hold_widget unavailable.";
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(skillWidgetObj, out IntPtr gameObjectObj, "get_gameObject")
                || gameObjectObj == IntPtr.Zero)
            {
                status = "skill_main_hold_widget.gameObject unavailable.";
                return false;
            }

            if (!this.ModTryAuraMonoReadBoolProperty(gameObjectObj, "get_activeInHierarchy", out active))
            {
                status = "skill_main_hold_widget.activeInHierarchy unreadable.";
                return false;
            }

            status = "skill_main_hold_widget.active=" + active;
            return true;
        }

        // AuraMono ONLY (XDT* protocol static — managed reflection is dead on this game).
        private bool TryInvokePetBathingRoundStart(uint petNetId)
        {
            return this.TryInvokeAuraMonoPetBathingRoundStart(petNetId);
        }

        private bool EnsureAuraMonoPetBathingRoundStartMethod(out string status)
        {
            status = "AuraMono pet bath protocol unavailable.";
            if (this.petPlayAuraPetBathingRoundStartMethod != IntPtr.Zero)
            {
                status = "AuraMono pet bath protocol ready.";
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

                this.petPlayAuraPetBathingRoundStartMethod = this.FindAuraMonoMethodOnHierarchy(protocolClass, "PetBathingRoundStart", 1);
                status = "AuraMono pet bath class=0x" + protocolClass.ToInt64().ToString("X")
                    + " roundStart=0x" + this.petPlayAuraPetBathingRoundStartMethod.ToInt64().ToString("X");
                return this.petPlayAuraPetBathingRoundStartMethod != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                status = "AuraMono pet bath exception: " + ex.Message;
                return false;
            }
        }

        private unsafe bool TryInvokeAuraMonoPetBathingRoundStart(uint petNetId)
        {
            if (!this.EnsureAuraMonoPetBathingRoundStartMethod(out string status) || this.petPlayAuraPetBathingRoundStartMethod == IntPtr.Zero)
            {
                this.PetPlayLog("Aura pet bath unavailable: " + status);
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&petNetId);
            auraMonoRuntimeInvoke(this.petPlayAuraPetBathingRoundStartMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            bool ok = exc == IntPtr.Zero;
            if (!ok)
            {
                this.PetPlayLog("Aura pet bath netId=" + petNetId + " exc=0x" + exc.ToInt64().ToString("X"));
            }

            return ok;
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
                IntPtr auraMeow = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Meow.MeowProtocolManager");
                IntPtr auraPet = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Pet.PetProtocolManager");
                this.EnsureAuraMonoPetBathingRoundStartMethod(out string washStatus);
                this.PetPlayLog("Resolver probe: aura meow/pet=0x" + auraMeow.ToInt64().ToString("X") + "/0x" + auraPet.ToInt64().ToString("X")
                    + " cat=" + catStatus + " dog=" + dogStatus + " wash=" + washStatus);
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
