using UnityEngine;

namespace HeartopiaMod
{
    // Settings → Logging: runtime ON/OFF switches for every extended-logging master flag
    // (the MasterLog* static bools scattered across the HeartopiaComplete partials).
    // State is SESSION-ONLY by design — nothing here is saved to or loaded from the config,
    // so a restart always returns to the compiled-in defaults.
    public partial class HeartopiaComplete
    {
        private const float LoggingTabRowHeight = 30f;
        private const int LoggingTabRowCount = 39; // keep in sync with DrawLoggingTab rows

        private float CalculateLoggingTabHeight()
        {
            // header (title 30 + subtitle 44) + toggle rows + bottom padding.
            return 10f + 8f + 30f + 44f + (LoggingTabRowCount * LoggingTabRowHeight) + 24f;
        }

        private void DrawLoggingToggleRow(float left, ref float y, ref bool flag, string label)
        {
            flag = this.DrawSwitchToggle(new Rect(left, y, 320f, 26f), flag, label);
            y += LoggingTabRowHeight;
        }

        private float DrawLoggingTab(int startY)
        {
            const float left = 24f;
            const float width = 540f;
            float y = startY + 8f;

            Color textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            Color mutedColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            titleStyle.normal.textColor = textColor;

            GUIStyle bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
            bodyStyle.normal.textColor = mutedColor;

            GUI.Label(new Rect(left, y, width, 28f), this.L("Logging"), titleStyle);
            y += 30f;
            GUI.Label(new Rect(left, y, width, 40f),
                "Extended Logging — session only, not saved. Toggles the verbose debug logs each feature writes to the mod log.",
                bodyStyle);
            y += 44f;

            this.DrawLoggingToggleRow(left, ref y, ref MasterLogAuraFarm, "Aura Farm");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogBirdFarm, "Bird Farm");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogBirdFarmCrashTrace, "Bird Farm Crash Trace");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogInsectFarm, "Insect Farm");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogAutoFish, "Auto Fish");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogInstantCatch, "Instant Catch");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogAutoFarm, "Auto Farm");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogQuestAssistant, "Quest Assistant");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogAutoEatRepair, "Auto Eat/Repair");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogNpcTeleport, "NPC Teleport");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogNetCook, "NetCook");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogNetCookScan, "NetCook Scan");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogPuzzle, "Puzzle");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogAutoSell, "Auto Sell");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogRadarIconEsp, "Radar Icon ESP");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogBubbleRadar, "Bubble Radar");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogAutoBuy, "Auto Buy");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogForceOpenShop, "Force Open Shop");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogPetPlay, "Pet Play");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogPetFeed, "Pet Feed");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogWildAnimalFeed, "Wild Animal Feed");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogHomelandFarm, "Homeland Farm");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogPadBuild, "Pad Build");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogWildAnimalGift, "Wild Animal Gift");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogAutoIceSkating, "Auto Ice Skating");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogDailyQuestSubmit, "Daily Quest Submit");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogDailyClaims, "Daily Claims");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogBirdPhotoSubmit, "Bird Photo Submit");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogStrangerChat, "Stranger Chat");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogGameEvents, "Game Events");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogEntityEvents, "Entity Events");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogGameIcons, "Game Icons");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogPersistentHud, "Persistent HUD");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogSandSculpture, "Sand Sculpture");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogShowOffBypass, "Show-Off Bypass");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogSnowSculpture, "Snow Sculpture");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogSeaCleanQte, "Sea Clean QTE");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogCorruptionCleanse, "Corruption Cleanse");
            this.DrawLoggingToggleRow(left, ref y, ref MasterLogUnderwaterRadar, "Underwater Radar");

            return y + 12f;
        }
    }
}
