using System;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // Auto-focus the first text field when specific whitelist UI panels open (search/filter,
    // alert input, chat record).
    //
    // UIView.Open dispatches UIPanelOpenEvent after OnStart. The payload is a System.Type the
    // event engine cannot read (docs/GAME_EVENTS.md), so the hook is a "some panel changed"
    // trigger and open state comes from GameObject.Find on the known panel roots.
    public partial class HeartopiaComplete
    {
        private bool uiInputAutoFocus = true;

        private const string UiInputAutoFocusOpenEventName = "XDTGame.Framework.UI.UIPanelOpenEvent";
        private const string UiInputAutoFocusClosingEventName = "XDTGame.Framework.UI.UIPanelClosingEvent";
        private const string UiInputAutoFocusCloseEventName = "XDTGame.Framework.UI.UIPanelCloseEvent";
        private const string UiInputAutoFocusChatSendResultEventName =
            "XDTDataAndProtocol.Events.ChatMessageSendResult";
        private const float UiInputAutoFocusPollIntervalSec = 1f;
        private const int UiInputAutoFocusSearchRootIndex = 0;
        private const int UiInputAutoFocusAlertRootIndex = 1;
        private const int UiInputAutoFocusChatRootIndex = 2;
        private static readonly string[] UiInputAutoFocusPanelRoots =
        {
            "SearchAndFilterPanel(Clone)",
            "AlertPanel(Clone)",
            "ChatRecordPanel(Clone)",
        };
        private const string SearchFieldRel = "commonpopup_m/popup/content/options@group/search/description@input@go";
        private const string SearchButtonRel = "commonpopup_m/popup/content/options@group/search/search@btn";
        private const string AlertInputWidgetRel = "AniRoot/popup/content/contenttext/inputField@w@go";
        private const string AlertFieldRel = "AniRoot/popup/content/contenttext/inputField@w@go/field@input";
        private const string ChatFieldRel = "AniRoot/content@t/chat@go/InputArea/inputfield@input";

        private bool uiInputAutoFocusHooksRegistered;
        private bool uiInputAutoFocusProbePending;
        private float uiInputAutoFocusNextPollAt;
        private readonly bool[] uiInputAutoFocusWasOpen = new bool[UiInputAutoFocusPanelRoots.Length];
        private int uiInputAutoFocusPendingRootIndex = -1;
        private int uiInputAutoFocusActivateOnFrame = -1;
        // Chat OnSubmit / scroll refresh can drop selection for several frames after the first
        // send of a session — keep re-arming until isFocused sticks or retries run out.
        // Unity InputField.ActivateInputField only sets m_ShouldActivateNextUpdate; isFocused
        // becomes true in LateUpdate, so same-frame checks always fail — verify next frame.
        private int uiInputAutoFocusChatRefocusOnFrame = -1;
        private int uiInputAutoFocusChatRefocusRetries;
        private const int UiInputAutoFocusChatRefocusMaxRetries = 10;
        private bool uiInputAutoFocusChatVerifyPending;
        // After a successful focus, reclaim for a short window — open-time world hooks and
        // chat-list scroll often steal EventSystem selection a few frames later.
        private int uiInputAutoFocusChatHoldUntilFrame = -1;
        private const int UiInputAutoFocusChatHoldFrames = 120;
        // SingleLine Enter deactivates the field before our OnUpdate; use last-frame focus.
        private bool uiInputAutoFocusSearchWasFocused;
        private bool uiInputAutoFocusChatWasFocused;
        private string uiInputAutoFocusChatLastText = string.Empty;
        private readonly FeatureBreakerState uiInputAutoFocusBreaker = new FeatureBreakerState();
        private bool uiInputAutoFocusFailLogged;
        private bool uiInputAutoFocusResolveFailLogged;

        private void ProcessUiInputAutoFocusOnUpdate()
        {
            this.TickUiInputAutoFocusDetection();

            if (!this.uiInputAutoFocus)
            {
                this.uiInputAutoFocusPendingRootIndex = -1;
                this.uiInputAutoFocusActivateOnFrame = -1;
                this.ClearUiInputAutoFocusChatFocusState();
                this.uiInputAutoFocusChatHoldUntilFrame = -1;
                this.uiInputAutoFocusSearchWasFocused = false;
                this.uiInputAutoFocusChatWasFocused = false;
                this.uiInputAutoFocusChatLastText = string.Empty;
                return;
            }

            this.ClearStaleUiInputAutoFocusFields();
            this.EnsureUiInputAutoFocusSearchSingleLine();
            this.TickUiInputAutoFocusEnterKeys();
            this.TickUiInputAutoFocusChatHold();
            this.TickUiInputAutoFocusActivate();
        }

        private void ArmUiInputAutoFocusChatRefocus()
        {
            this.uiInputAutoFocusChatRefocusOnFrame = Time.frameCount + 2;
            this.uiInputAutoFocusChatRefocusRetries = UiInputAutoFocusChatRefocusMaxRetries;
            this.uiInputAutoFocusChatVerifyPending = false;
            this.uiInputAutoFocusChatHoldUntilFrame = Time.frameCount + UiInputAutoFocusChatHoldFrames;
        }

        // Do not Deactivate first — that undoes a LateUpdate focus from the previous arm.
        private static void ForceActivateUiInputAutoFocusChatField(InputField field)
        {
            field.readOnly = false;
            UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null)
            {
                es.SetSelectedGameObject(field.gameObject);
            }

            field.Select();
            field.ActivateInputField();
            field.MoveTextEnd(shift: false);
        }

        private void ClearUiInputAutoFocusChatFocusState()
        {
            this.uiInputAutoFocusChatRefocusOnFrame = -1;
            this.uiInputAutoFocusChatRefocusRetries = 0;
            this.uiInputAutoFocusChatVerifyPending = false;
        }

        private void OnUiInputAutoFocusChatSendResult(GameEventSnapshot e)
        {
            if (!this.uiInputAutoFocus
                || !this.uiInputAutoFocusWasOpen[UiInputAutoFocusChatRootIndex])
            {
                return;
            }

            this.ArmUiInputAutoFocusChatRefocus();
        }

        // Reclaim focus if something steals selection shortly after a successful activate.
        private void TickUiInputAutoFocusChatHold()
        {
            if (this.uiInputAutoFocusChatHoldUntilFrame < 0)
            {
                return;
            }

            if (Time.frameCount > this.uiInputAutoFocusChatHoldUntilFrame)
            {
                this.uiInputAutoFocusChatHoldUntilFrame = -1;
                return;
            }

            if (!this.uiInputAutoFocusWasOpen[UiInputAutoFocusChatRootIndex])
            {
                this.uiInputAutoFocusChatHoldUntilFrame = -1;
                return;
            }

            if (this.uiInputAutoFocusPendingRootIndex >= 0
                || this.uiInputAutoFocusChatVerifyPending
                || this.uiInputAutoFocusChatRefocusOnFrame >= 0)
            {
                return;
            }

            if (!TryResolveUiInputAutoFocusField(UiInputAutoFocusChatRootIndex, out InputField field)
                || field.isFocused)
            {
                return;
            }

            this.uiInputAutoFocusChatRefocusRetries = UiInputAutoFocusChatRefocusMaxRetries;
            this.uiInputAutoFocusPendingRootIndex = UiInputAutoFocusChatRootIndex;
            this.uiInputAutoFocusActivateOnFrame = Time.frameCount;
        }

        // Prefab uses MultiLineNewline; force SingleLine while the search panel is up so Enter
        // cannot insert a line break before our click handler runs.
        private void EnsureUiInputAutoFocusSearchSingleLine()
        {
            if (!TryResolveUiInputAutoFocusField(UiInputAutoFocusSearchRootIndex, out InputField search))
            {
                return;
            }

            if (search.lineType != InputField.LineType.SingleLine)
            {
                search.lineType = InputField.LineType.SingleLine;
            }
        }

        private void TickUiInputAutoFocusActivate()
        {
            // Verify LateUpdate focus from a previous ActivateInputField before issuing another.
            if (this.uiInputAutoFocusChatVerifyPending
                && this.uiInputAutoFocusChatRefocusOnFrame >= 0
                && Time.frameCount >= this.uiInputAutoFocusChatRefocusOnFrame
                && this.uiInputAutoFocusPendingRootIndex < 0)
            {
                this.uiInputAutoFocusChatVerifyPending = false;
                this.uiInputAutoFocusChatRefocusOnFrame = -1;
                if (TryResolveUiInputAutoFocusField(UiInputAutoFocusChatRootIndex, out InputField verifyField)
                    && verifyField.isFocused)
                {
                    this.ClearUiInputAutoFocusChatFocusState();
                    this.uiInputAutoFocusChatHoldUntilFrame =
                        Time.frameCount + UiInputAutoFocusChatHoldFrames;
                    this.uiInputAutoFocusBreaker.Success();
                }
                else if (this.uiInputAutoFocusChatRefocusRetries > 0)
                {
                    this.uiInputAutoFocusChatRefocusRetries--;
                    this.uiInputAutoFocusPendingRootIndex = UiInputAutoFocusChatRootIndex;
                    this.uiInputAutoFocusActivateOnFrame = Time.frameCount;
                }
                else
                {
                    this.ClearUiInputAutoFocusChatFocusState();
                }
            }
            else if (this.uiInputAutoFocusChatRefocusOnFrame >= 0
                && Time.frameCount >= this.uiInputAutoFocusChatRefocusOnFrame
                && this.uiInputAutoFocusPendingRootIndex < 0)
            {
                GameObject chatRoot = GameObject.Find(UiInputAutoFocusPanelRoots[UiInputAutoFocusChatRootIndex]);
                bool chatVisible = this.uiInputAutoFocusWasOpen[UiInputAutoFocusChatRootIndex]
                    || IsUiInputAutoFocusRootVisible(chatRoot);
                this.uiInputAutoFocusChatRefocusOnFrame = -1;
                if (chatVisible)
                {
                    if (TryResolveUiInputAutoFocusField(UiInputAutoFocusChatRootIndex, out InputField already)
                        && already.isFocused)
                    {
                        this.ClearUiInputAutoFocusChatFocusState();
                    }
                    else
                    {
                        this.uiInputAutoFocusPendingRootIndex = UiInputAutoFocusChatRootIndex;
                        this.uiInputAutoFocusActivateOnFrame = Time.frameCount;
                    }
                }
            }

            if (this.uiInputAutoFocusPendingRootIndex < 0
                || Time.frameCount < this.uiInputAutoFocusActivateOnFrame)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (!this.uiInputAutoFocusBreaker.ShouldRun(now))
            {
                this.uiInputAutoFocusPendingRootIndex = -1;
                this.uiInputAutoFocusActivateOnFrame = -1;
                return;
            }

            int rootIndex = this.uiInputAutoFocusPendingRootIndex;
            this.uiInputAutoFocusPendingRootIndex = -1;
            this.uiInputAutoFocusActivateOnFrame = -1;

            try
            {
                if (!TryResolveUiInputAutoFocusField(rootIndex, out InputField field))
                {
                    if (!this.uiInputAutoFocusResolveFailLogged)
                    {
                        this.uiInputAutoFocusResolveFailLogged = true;
                        ModLogger.Msg("[UiInputAutoFocus] activate skipped — input field not ready for "
                            + UiInputAutoFocusPanelRoots[rootIndex]);
                    }

                    return;
                }

                // Prefab ships MultiLineNewline; Enter would insert a line break instead of search.
                if (rootIndex == UiInputAutoFocusSearchRootIndex
                    && field.lineType != InputField.LineType.SingleLine)
                {
                    field.lineType = InputField.LineType.SingleLine;
                }

                if (rootIndex == UiInputAutoFocusChatRootIndex)
                {
                    if (field.isFocused)
                    {
                        this.ClearUiInputAutoFocusChatFocusState();
                        this.uiInputAutoFocusBreaker.Success();
                        return;
                    }

                    ForceActivateUiInputAutoFocusChatField(field);
                    // Same-frame isFocused is always false (LateUpdate). Verify next frame.
                    this.uiInputAutoFocusChatVerifyPending = true;
                    this.uiInputAutoFocusChatRefocusOnFrame = Time.frameCount + 1;
                    this.uiInputAutoFocusBreaker.Success();
                    return;
                }

                if (field.isFocused)
                {
                    this.uiInputAutoFocusBreaker.Success();
                    return;
                }

                field.Select();
                field.ActivateInputField();
                this.uiInputAutoFocusBreaker.Success();
            }
            catch (Exception ex)
            {
                this.uiInputAutoFocusBreaker.Failure("UiInputAutoFocus", ex, now);
            }
        }

        // Search: Enter runs the Search button (after forcing SingleLine so it is not a newline).
        // Chat: Enter is already OnSubmit in the game; we only re-focus after it clears focus.
        //
        // Important: with SingleLine, Unity deactivates the field on Enter before our OnUpdate, so
        // isFocused is already false. Use last-frame focus (wasFocused) together with GetKeyDown.
        private void TickUiInputAutoFocusEnterKeys()
        {
            bool searchNow = TryResolveUiInputAutoFocusField(UiInputAutoFocusSearchRootIndex, out InputField search)
                && search.isFocused;
            bool chatResolved = TryResolveUiInputAutoFocusField(UiInputAutoFocusChatRootIndex, out InputField chat);
            bool chatNow = chatResolved && chat.isFocused;
            string chatText = chatResolved ? (chat.text ?? string.Empty) : string.Empty;
            bool chatOpen = this.uiInputAutoFocusWasOpen[UiInputAutoFocusChatRootIndex];

            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            // Backup for first-open: OnSubmit clears text and may drop focus without our Update
            // seeing GetKeyDown (or before wasFocused is armed). Detect the clear edge.
            bool chatSubmitEdge = this.uiInputAutoFocusChatWasFocused
                && this.uiInputAutoFocusChatLastText.Length > 0
                && chatText.Length == 0
                && !chatNow;

            if (enter || chatSubmitEdge)
            {
                try
                {
                    if (enter && (searchNow || this.uiInputAutoFocusSearchWasFocused))
                    {
                        this.TryClickUiInputAutoFocusSearchButton();
                    }

                    // Arm on any Enter while the chat panel is open — not only when we still
                    // think the field was focused (first-open steals can clear wasFocused).
                    if (chatSubmitEdge
                        || (enter && (chatNow || this.uiInputAutoFocusChatWasFocused || chatOpen)))
                    {
                        this.ArmUiInputAutoFocusChatRefocus();
                    }
                }
                catch (Exception ex)
                {
                    this.uiInputAutoFocusBreaker.Failure("UiInputAutoFocus", ex, Time.unscaledTime);
                }
            }

            this.uiInputAutoFocusSearchWasFocused = searchNow;
            this.uiInputAutoFocusChatWasFocused = chatNow;
            this.uiInputAutoFocusChatLastText = chatText;
        }

        // CustomButton wires a Unity Button.onClick → OnClickedHandler. SimulateClick looks for
        // OnClick/Click and misses TriggerClick; invoke the Button directly.
        private bool TryClickUiInputAutoFocusSearchButton()
        {
            GameObject root = GameObject.Find(UiInputAutoFocusPanelRoots[UiInputAutoFocusSearchRootIndex]);
            if (root == null || !IsUiInputAutoFocusRootVisible(root))
            {
                return false;
            }

            Transform btnTransform = root.transform.Find(SearchButtonRel);
            if (btnTransform == null)
            {
                return false;
            }

            Button button = btnTransform.GetComponent<Button>()
                ?? btnTransform.GetComponentInChildren<Button>(true);
            if (button == null || !button.interactable || !button.enabled)
            {
                return false;
            }

            button.onClick.Invoke();
            return true;
        }

        // UIViewBase.SetViewActive hides panels with ui.canvas.enabled, not GameObject.SetActive.
        // Pooled clones stay activeInHierarchy while closed; canvas.enabled is the real open signal.
        private static bool IsUiInputAutoFocusRootVisible(GameObject root)
        {
            if (root == null || !root.activeInHierarchy)
            {
                return false;
            }

            Canvas canvas = root.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = root.GetComponentInChildren<Canvas>(true);
            }

            return canvas == null || canvas.enabled;
        }

        private static bool TryResolveUiInputAutoFocusField(int rootIndex, out InputField field)
        {
            field = null;
            if (rootIndex < 0 || rootIndex >= UiInputAutoFocusPanelRoots.Length)
            {
                return false;
            }

            GameObject root = GameObject.Find(UiInputAutoFocusPanelRoots[rootIndex]);
            // Pooled panels stay activeInHierarchy while canvas.enabled is false. Treating those
            // as resolvable left SearchAndFilter's InputField.isFocused sticky after close, so
            // Enter in chat was stolen by the search branch until a later open cleared it.
            if (!IsUiInputAutoFocusRootVisible(root))
            {
                return false;
            }

            string rel;
            switch (rootIndex)
            {
                case UiInputAutoFocusSearchRootIndex: rel = SearchFieldRel; break;
                case UiInputAutoFocusAlertRootIndex: rel = AlertFieldRel; break;
                case UiInputAutoFocusChatRootIndex: rel = ChatFieldRel; break;
                default: return false;
            }

            Transform t = root.transform.Find(rel);
            if (t == null || !t.gameObject.activeInHierarchy)
            {
                return false;
            }

            field = t.GetComponent<InputField>();
            return field != null && field.interactable;
        }

        // Canvas-hide does not run InputField.OnDisable, so isFocused can stick on a pooled panel.
        // Clear it when we see a hidden whitelist root still claiming focus.
        private void ClearStaleUiInputAutoFocusFields()
        {
            for (int i = 0; i < UiInputAutoFocusPanelRoots.Length; i++)
            {
                GameObject root = GameObject.Find(UiInputAutoFocusPanelRoots[i]);
                if (root == null || IsUiInputAutoFocusRootVisible(root))
                {
                    continue;
                }

                InputField[] fields = root.GetComponentsInChildren<InputField>(true);
                for (int f = 0; f < fields.Length; f++)
                {
                    InputField field = fields[f];
                    if (field != null && field.isFocused)
                    {
                        field.DeactivateInputField();
                    }
                }
            }
        }

        private void EnsureUiInputAutoFocusHooks()
        {
            if (this.uiInputAutoFocusHooksRegistered)
            {
                return;
            }

            this.uiInputAutoFocusHooksRegistered = true;
            bool ok = this.RegisterGameEventHook(UiInputAutoFocusOpenEventName, 0, this.OnUiInputAutoFocusPanelEvent)
                    & this.RegisterGameEventHook(UiInputAutoFocusClosingEventName, 0, this.OnUiInputAutoFocusPanelEvent)
                    & this.RegisterGameEventHook(UiInputAutoFocusCloseEventName, 0, this.OnUiInputAutoFocusPanelEvent);
            this.RegisterGameEventHook(
                UiInputAutoFocusChatSendResultEventName, 0, this.OnUiInputAutoFocusChatSendResult);
            if (!ok && !this.uiInputAutoFocusFailLogged)
            {
                this.uiInputAutoFocusFailLogged = true;
                ModLogger.Msg("[UiInputAutoFocus] UI panel event hooks refused — falling back to 1 s GameObject.Find poll.");
            }
        }

        private void OnUiInputAutoFocusPanelEvent(GameEventSnapshot e)
        {
            this.uiInputAutoFocusProbePending = true;
        }

        private bool UiInputAutoFocusHooksLive()
        {
            return this.IsGameEventHookInstalled(UiInputAutoFocusOpenEventName)
                && this.IsGameEventHookInstalled(UiInputAutoFocusClosingEventName)
                && this.IsGameEventHookInstalled(UiInputAutoFocusCloseEventName);
        }

        private void TickUiInputAutoFocusDetection()
        {
            this.EnsureUiInputAutoFocusHooks();
            if (!this.IsWorldReady)
            {
                for (int i = 0; i < this.uiInputAutoFocusWasOpen.Length; i++)
                {
                    this.uiInputAutoFocusWasOpen[i] = false;
                }

                this.uiInputAutoFocusPendingRootIndex = -1;
                this.uiInputAutoFocusActivateOnFrame = -1;
                this.ClearUiInputAutoFocusChatFocusState();
                this.uiInputAutoFocusChatHoldUntilFrame = -1;
                this.uiInputAutoFocusSearchWasFocused = false;
                this.uiInputAutoFocusChatWasFocused = false;
                this.uiInputAutoFocusChatLastText = string.Empty;
                this.uiInputAutoFocusProbePending = true;
                return;
            }

            float now = Time.unscaledTime;
            if (!this.UiInputAutoFocusHooksLive() && now >= this.uiInputAutoFocusNextPollAt)
            {
                this.uiInputAutoFocusNextPollAt = now + UiInputAutoFocusPollIntervalSec;
                this.uiInputAutoFocusProbePending = true;
            }

            if (!this.uiInputAutoFocusProbePending && this.uiInputAutoFocusPendingRootIndex < 0)
            {
                return;
            }

            this.uiInputAutoFocusProbePending = false;

            for (int i = 0; i < UiInputAutoFocusPanelRoots.Length; i++)
            {
                GameObject root = GameObject.Find(UiInputAutoFocusPanelRoots[i]);
                bool open = IsUiInputAutoFocusRootVisible(root);
                if (open && i == UiInputAutoFocusAlertRootIndex)
                {
                    Transform widget = root.transform.Find(AlertInputWidgetRel);
                    open = widget != null
                        && widget.gameObject.activeInHierarchy
                        && widget.gameObject.activeSelf;
                }

                bool was = this.uiInputAutoFocusWasOpen[i];
                this.uiInputAutoFocusWasOpen[i] = open;
                if (i == UiInputAutoFocusChatRootIndex && was && !open)
                {
                    this.uiInputAutoFocusChatHoldUntilFrame = -1;
                    this.ClearUiInputAutoFocusChatFocusState();
                }

                if (open && !was && this.uiInputAutoFocus)
                {
                    this.uiInputAutoFocusPendingRootIndex = i;
                    this.uiInputAutoFocusActivateOnFrame = Time.frameCount + 1;
                    // Seed retries on open: same-frame isFocused after Activate is always false
                    // until LateUpdate, so a zero-retry path would give up immediately.
                    if (i == UiInputAutoFocusChatRootIndex)
                    {
                        this.uiInputAutoFocusChatRefocusRetries = UiInputAutoFocusChatRefocusMaxRetries;
                        this.uiInputAutoFocusChatVerifyPending = false;
                    }
                }
            }
        }
    }
}
