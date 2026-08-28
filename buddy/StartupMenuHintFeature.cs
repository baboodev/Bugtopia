using System;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // STARTUP MENU HINT — one toast at mod start, naming the key that opens the mod menu.
    //
    // The launcher prints the same line under its Play button (launcher/BugtopiaLauncher/ui.html),
    // but a player who was handed a ready-made install, or who came back after a few weeks, never
    // sees that screen: nothing in the game itself says which key exists. This is the in-game half
    // of that hint.
    //
    // Timing: straight from OnInitializeMelon, NOT the world-ready gate. The gate never opens for a
    // session that ends at the login screen, which is exactly the session where someone is hunting
    // for the menu. Nothing here touches game code — the toast stack is pure Unity UI and already
    // renders this early (LoadUiTheme's own "UI theme loaded" toast rides the same moment, and
    // EnsureUguiFonts explicitly handles a first notification that fires before TMP's font is in
    // memory), so the gate's rule about resolving/inflating game code does not apply.
    //
    // Registered after LoadKeybinds so the toast names the player's OWN key, not the Insert default.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // Longer than the 5 s default: this one is read, not glanced at, and it is the only line a
        // first-time player has to take away. It also lands while the game is still booting, so it
        // must outlive a few seconds of the player looking elsewhere.
        private const float StartupMenuHintDurationSeconds = 15f;

        private bool startupMenuHintShown;

        private void ShowStartupMenuHint()
        {
            if (this.startupMenuHintShown)
            {
                return;
            }

            this.startupMenuHintShown = true;

            try
            {
                if (this.keyToggleMenu == KeyCode.None)
                {
                    // Unbound on purpose: "Press None" is worse than silence.
                    return;
                }

                this.AddMenuNotification(
                    this.LF("Press {0} to open the Bugtopia menu", FormatKeybindLabel(this.keyToggleMenu)),
                    new Color(0.55f, 0.88f, 1f),
                    StartupMenuHintDurationSeconds);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[StartupMenuHint] failed to show: " + ex.Message);
            }
        }
    }
}
