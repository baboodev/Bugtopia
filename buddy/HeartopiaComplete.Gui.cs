using System;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        // ========================================================================================
        // Phase 5 (IMGUI retirement): the IMGUI mod menu, Building Move Panel, Quest Assistant
        // window, Status Overlay and toast renderer are all UGUI now (HeartopiaComplete.Ugui*.cs).
        // OnGUI survives ONLY for the three overlays that were never UGUI candidates:
        //  - DrawResourceVisualEspOverlay / DrawVisualDebugEspOverlay (world-space ESP tags;
        //    per-frame camera projection of hundreds of short-lived rects — retained-mode UGUI
        //    would be pure churn here), both inside the Repaint guard exactly as before, and
        //  - DrawMouseLookCrosshair (HeartopiaComplete.CameraInput.cs).
        // The theme dirty/save tick that used to piggyback on EnsureThemeStyles at the top of
        // OnGUI now runs from OnUpdate (ProcessUiThemePersistenceOnUpdate,
        // HeartopiaComplete.UiKit.cs) — the UGUI theme tab must not depend on the IMGUI paint
        // loop.
        // ========================================================================================
        public void OnGUI()
        {
            Breadcrumbs.Tick("OnGUI");
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.contentColor = Color.white;

            // Deliberate remnant of EnsureThemeStyles: the ESP overlays build their label styles
            // from GUI.skin.label, whose FONT was the one skin property the old style bake set
            // that they actually inherited (Segoe UI via EnsureUiThemeFont). Keep applying it so
            // their text renders exactly as it did before the IMGUI menu retired.
            Font themeFont = this.EnsureUiThemeFont();
            if (themeFont != null && GUI.skin != null && GUI.skin.font != themeFont)
            {
                GUI.skin.font = themeFont;
            }

            if (Event.current == null || Event.current.type == EventType.Repaint)
            {
                this.DrawResourceVisualEspOverlay();
                this.DrawVisualDebugEspOverlay();
            }

            this.DrawMouseLookCrosshair();
        }
    }
}
