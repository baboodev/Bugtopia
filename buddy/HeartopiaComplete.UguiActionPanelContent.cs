using System;
using UnityEngine;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // ACTION PANEL WINDOW — the floating grid the hotkey opens.
    //
    // Built on the kit's window factory, the same one behind the Free Placement panel and the Quest
    // Assistant, so it inherits the drag strip, the theme, the click-blocker and — the part asked
    // for explicitly — the shared persisted UI scale: SetUguiWindowScale drives Canvas.scaleFactor,
    // and the Phase 2e re-sync below pushes a new value whenever the slider moves.
    //
    // NOTHING is added to the mod menu for this. The panel has action buttons and a title strip and
    // that is all; the only way in or out is the hotkey (Keybinds ▸ CORE ▸ Action Panel), which is
    // where a hotkey has to live to be rebindable at all.
    //
    // Buttons ARE the content, so there is no scroll view: the grid is sized to the catalogue at
    // build time and the window is exactly as tall as its rows need.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const int UguiActionPanelColumns = 3;
        private const float UguiActionPanelButtonW = 104f;
        private const float UguiActionPanelButtonH = 26f;
        private const float UguiActionPanelGap = 5f;
        private const float UguiActionPanelPadX = 10f;
        private const float UguiActionPanelPadBottom = 10f;
        private const float UguiActionPanelTitleH = 22f;

        // Above the Quest Assistant (29360) and below the shell (29400): the panel is meant to sit
        // over the game and under the menu, the same rung as the other floating windows.
        private const int UguiActionPanelSortingOrder = 29370;

        private sealed class UguiActionPanelHandle
        {
            public UguiWindowHandle Window;
            public float LastSyncedUiScale = -1f;  // Phase 2e shell scale-sync idiom
        }

        private UguiActionPanelHandle uguiActionPanel;
        private bool uguiActionPanelBuildFailed;

        private static float UguiActionPanelWidth
        {
            get
            {
                return (UguiActionPanelPadX * 2f)
                    + (UguiActionPanelColumns * UguiActionPanelButtonW)
                    + ((UguiActionPanelColumns - 1) * UguiActionPanelGap);
            }
        }

        private static float UguiActionPanelHeight
        {
            get
            {
                int rows = (ActionPanelRows.Length + UguiActionPanelColumns - 1) / UguiActionPanelColumns;
                return UguiActionPanelTitleH + UguiActionPanelPadBottom
                    + (rows * UguiActionPanelButtonH)
                    + ((rows - 1) * UguiActionPanelGap);
            }
        }

        private void BuildUguiActionPanel()
        {
            this.uguiActionPanel = null;
            UguiActionPanelHandle handle = null;
            try
            {
                handle = new UguiActionPanelHandle();

                // Title passed as NULL and drawn below by hand, the Free Placement panel's idiom.
                // The kit's own title is 18pt placed at y 14..40, which needs a ~44px strip; this
                // panel's strip is 22, so the kit title ended up UNDER the first row of buttons —
                // they are added to the same panel afterwards and paint over it. A compact 12pt
                // label inside the strip fits the window instead of the window growing to fit it.
                handle.Window = this.CreateUguiWindow(
                    "BugtopiaUguiActionPanel",
                    null,
                    null,
                    new Vector2(UguiActionPanelWidth, UguiActionPanelHeight),
                    UguiActionPanelSortingOrder,
                    UguiActionPanelTitleH);
                Transform panelT = handle.Window.PanelRt;

                GameObject title = this.CreateUguiLabel(panelT, "Title", this.L("Actions"),
                    12f, this.UguiKitHeaderColor(), false);
                this.TrySetUguiLabelBold(title);
                PlaceUguiTopLeft(title, UguiActionPanelPadX, 3f,
                                 UguiActionPanelWidth - (UguiActionPanelPadX * 2f), 16f);

                for (int i = 0; i < ActionPanelRows.Length; i++)
                {
                    ActionPanelRow row = ActionPanelRows[i];   // copy: the closure outlives the loop
                    int column = i % UguiActionPanelColumns;
                    int line = i / UguiActionPanelColumns;

                    GameObject button = this.CreateUguiSecondaryButton(panelT, "Action" + row.Id,
                        row.Label, () => this.CastActionPanelRow(row));
                    PlaceUguiTopLeft(button,
                        UguiActionPanelPadX + (column * (UguiActionPanelButtonW + UguiActionPanelGap)),
                        UguiActionPanelTitleH + (line * (UguiActionPanelButtonH + UguiActionPanelGap)),
                        UguiActionPanelButtonW,
                        UguiActionPanelButtonH);
                }

                handle.LastSyncedUiScale = this.GetUiScale();
                this.SetUguiWindowScale(handle.Window, handle.LastSyncedUiScale);

                // Right-hand side, vertically centred — clear of the game's own left-hand HUD and of
                // the quest tracker. Canvas space is centre-pivoted, so the extents come from the
                // screen divided by the CURRENT scale.
                float s = Mathf.Max(handle.Window.Scale, 0.1f);
                float halfW = Screen.width / s * 0.5f;
                handle.Window.PanelRt.anchoredPosition = new Vector2(
                    halfW - (UguiActionPanelWidth * 0.5f) - 24f,
                    0f);
                this.ClampUguiWindowPosition(handle.Window);

                this.uguiActionPanel = handle;

                // Lives OUTSIDE the shell, so it needs its own theme rebuilder (idempotent by name).
                this.RegisterUguiThemeRebuilder("UguiActionPanel",
                    new System.Action(this.RebuildUguiActionPanelForTheme));

                // FLOATING input-ownership surface, not modal: the panel must not swallow the game's
                // input while it is only sitting there, but a click ON it belongs to it.
                this.RegisterInputOwnershipSurface("UguiActionPanel", false,
                    () => this.uguiActionPanel != null
                        && this.IsUguiWindowVisible(this.uguiActionPanel.Window),
                    () => this.uguiActionPanel != null
                        && this.IsUguiWindowPointerOver(this.uguiActionPanel.Window));

                ModLogger.Msg("[UguiShell] Action panel built — " + ActionPanelRows.Length
                    + " actions, sortingOrder " + UguiActionPanelSortingOrder);
            }
            catch (Exception ex)
            {
                this.uguiActionPanelBuildFailed = true;
                try
                {
                    if (handle != null && handle.Window != null && handle.Window.Root != null)
                    {
                        Object.Destroy(handle.Window.Root);
                    }
                }
                catch { }
                this.uguiActionPanel = null;
                ModLogger.Msg("[UguiShell] Action panel build failed: " + ex.Message);
            }
        }

        // A theme change destroys and rebuilds the whole UI, so the window is dropped and rebuilt on
        // the next frame it should show — the same shape the other out-of-shell windows use.
        private void RebuildUguiActionPanelForTheme()
        {
            try
            {
                if (this.uguiActionPanel != null && this.uguiActionPanel.Window != null
                    && this.uguiActionPanel.Window.Root != null)
                {
                    Object.Destroy(this.uguiActionPanel.Window.Root);
                }
            }
            catch { }

            this.uguiActionPanel = null;
            this.uguiActionPanelBuildFailed = false;
        }

        private void ProcessUguiActionPanelOnUpdate()
        {
            try
            {
                bool show = this.actionPanelVisible;

                UguiActionPanelHandle handle = this.uguiActionPanel;
                if (handle == null)
                {
                    // Built on the first frame it is actually wanted — a panel nobody opens costs
                    // nothing, and building it before the world is up would race the shell.
                    if (!show || this.uguiActionPanelBuildFailed || !this.IsWorldReady)
                    {
                        return;
                    }

                    this.BuildUguiActionPanel();
                    handle = this.uguiActionPanel;
                    if (handle == null)
                    {
                        return;
                    }
                }

                if (this.IsUguiWindowVisible(handle.Window) != show)
                {
                    this.SetUguiWindowVisible(handle.Window, show);
                }

                if (!show)
                {
                    return;
                }

                this.ProcessUguiWindowFrame(handle.Window);   // title-strip drag (kit driver)

                // Phase 2e scale re-sync — compare the RAW GetUiScale() against the last pushed
                // value; SetUguiWindowScale logs unconditionally, so only call it on a real change.
                float targetScale = this.GetUiScale();
                if (!Mathf.Approximately(targetScale, handle.LastSyncedUiScale))
                {
                    handle.LastSyncedUiScale = targetScale;
                    this.SetUguiWindowScale(handle.Window, targetScale);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiShell] Action panel update failed: " + ex.Message);
                this.uguiActionPanelBuildFailed = true;
            }
        }
    }
}
