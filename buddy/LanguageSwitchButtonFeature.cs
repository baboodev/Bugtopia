using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // LANGUAGE SWITCH BUTTON — bring back Settings -> "Switch language" on the China build
    //
    // SettingPanel.OnStart calls
    //     nodes.language_btn.gameObject.SetActive(Managers.GetModule<LoginSystem>().IsOverSea);
    // and the China build (TapTap CN) is compiled without the `IsOverSea = true` line, so
    // the button is hidden there. Nothing else is cut: the button, its click handler
    // (OpenView<LanguageSwitchPanel>), the panel, all 12 rows of the Languages table and every
    // translation in designTable.db are the same as on the global build. Verified on Ideapad
    // 2026-10-01: revealing the button opens the stock panel, and picking English re-enters the world
    // in English.
    //
    // UIView.Open dispatches UIPanelOpenEvent right AFTER OnStart, so re-activating the button on
    // that event wins over the hide. The event carries only a System.Type the event engine cannot
    // read (docs/GAME_EVENTS.md), so it is a "some panel opened" trigger and the settings panel is
    // looked up by its hierarchy path — one GameObject.Find per panel open, nothing per frame.
    // On the global build the button is already active and this is a no-op.
    //
    // THE LOGIN SCREEN opens the same SettingPanel at the same path, but event hooks install only
    // once a world is up (inflating DispatchEvent<T> on the login screen aborts the process), so
    // the hook above never fires there. Before a world exists a Unity-only check covers it
    // instead: GameObject.Find twice a second until the panel shows, then an activeSelf read per
    // frame on the cached button, so a reopen that hides it again is undone on the next frame.
    // No Mono is touched. The game's own panel already handles a pick on the login screen: it
    // changes the language, closes every panel and reopens LoginPanel (LanguageSwitchPanel
    // .OnConfirmClick, GameWorld.IsLevel<GameLevel_Login> branch).
    //
    // Pair with LocalizationFallbackFeature (untranslated strings) and the UguiKitTmp font sweep
    // releasing its bundles — without that release the game could not load the English font.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string LanguageButtonPanelOpenEventName = "XDTGame.Framework.UI.UIPanelOpenEvent";
        private const string LanguageButtonSettingPanelPath = "GameApp/startup_root(Clone)/XDUIRoot/Full/SettingPanel(Clone)";
        private const string LanguageButtonRelativePath = "AniRoot@queueanimation/GameObjectLayout/language@btn";

        private const float LanguageButtonLoginFindInterval = 0.5f;

        private bool languageButtonRevealLogged;
        private Transform languageButtonLoginCached;
        private float languageButtonLoginNextFindAt;

        private void RegisterLanguageSwitchButtonReveal()
        {
            if (!this.RegisterGameEventHook(LanguageButtonPanelOpenEventName, 0, this.OnPanelOpenedRevealLanguageButton))
            {
                ModLogger.Msg("[LanguageButton] UIPanelOpenEvent hook refused — the settings language button stays as the game sets it.");
            }
        }

        private void OnPanelOpenedRevealLanguageButton(GameEventSnapshot e)
        {
            this.RevealSettingLanguageButton(FindSettingLanguageButton());
        }

        // Login screen (and loading screens): the event hook is not installed yet, see the header.
        private void ProcessLanguageButtonLoginRevealOnUpdate()
        {
            if (this.IsWorldReady)
            {
                this.languageButtonLoginCached = null;
                return;
            }

            Transform button = this.languageButtonLoginCached;
            if (button == null)   // Unity null: never found, or the panel was destroyed
            {
                float now = Time.unscaledTime;
                if (now < this.languageButtonLoginNextFindAt)
                {
                    return;
                }
                this.languageButtonLoginNextFindAt = now + LanguageButtonLoginFindInterval;

                button = FindSettingLanguageButton();
                if (button == null)
                {
                    return;
                }
                this.languageButtonLoginCached = button;
            }

            this.RevealSettingLanguageButton(button);
        }

        private static Transform FindSettingLanguageButton()
        {
            GameObject panel = GameObject.Find(LanguageButtonSettingPanelPath);
            return panel == null ? null : panel.transform.Find(LanguageButtonRelativePath);
        }

        private void RevealSettingLanguageButton(Transform button)
        {
            if (button == null || button.gameObject.activeSelf)
            {
                return;
            }

            button.gameObject.SetActive(true);
            if (!this.languageButtonRevealLogged)
            {
                this.languageButtonRevealLogged = true;
                ModLogger.Msg("[LanguageButton] settings language button was hidden by the game (China build) — shown.");
            }
        }
    }
}
