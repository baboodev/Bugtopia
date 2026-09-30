using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Order card on the Research / Order page (sits above the Research block).
    //
    // Shows the current order (the game's own item label), the time left until it arrives with a
    // local-clock ETA, and one button that opens whichever order panel the machine would open right
    // now (new order / in transit / arrived — OrderShopFeature.cs). Data comes from OrderShopFeature's
    // snapshot; this file only renders it.
    //
    // Refresh: its own 1 Hz tick, gated exactly like the Research block (shell visible AND this tab
    // active), so nothing runs while the menu is closed. The countdown is interpolated between
    // snapshot reads, and labels are only re-set when their text actually changes.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const float UguiOrderShopCardHeight = 112f;

        private sealed class UguiShellOrderShopContentHandle
        {
            public GameObject Root;
            public GameObject ItemLabel;
            public GameObject StatusLabel;
            public GameObject ResultLabel;
            public string LastItemText;
            public string LastStatusText;
            public int LastStatusKind = -1;   // 0 muted, 1 in transit, 2 arrived
            public float NextRefreshAt;
            public bool WasActive;
            public int ErrorCount;            // refresh disabled at 3 (Research block idiom)
        }

        private UguiShellOrderShopContentHandle uguiShellOrderShopContent;

        private GameObject BuildUguiShellOrderShopContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellOrderShopContent = null;

            UguiShellOrderShopContentHandle handle = new UguiShellOrderShopContentHandle();
            GameObject block = this.CreateUguiGo("OrderShopContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            const float buttonW = 190f;
            Color muted = this.UguiKitMutedColor();

            // Button in the header row, so the item line gets the full card width — the game's
            // labels run long ("Building Permit: Vibrant Adjustable Window").
            GameObject header = this.CreateUguiHeaderLabel(block.transform, "Header", this.L("Order Machine"), 14f);
            PlaceUguiTopLeft(header, pad, 12f, w - pad * 3f - buttonW, 22f);

            GameObject open = this.CreateUguiPrimaryButton(block.transform, "OpenOrderPanel", this.L("OPEN ORDER PANEL"),
                new System.Action(this.OnUguiOrderShopOpenClicked));
            PlaceUguiTopLeft(open, w - pad - buttonW, 8f, buttonW, 30f);

            handle.ItemLabel = this.CreateUguiBodyLabel(block.transform, "Item", this.L("Loading order…"), 13f);
            PlaceUguiTopLeft(handle.ItemLabel, pad, 42f, w - pad * 2f, 22f);

            handle.StatusLabel = this.CreateUguiLabel(block.transform, "Status", string.Empty, 12f, muted, false);
            PlaceUguiTopLeft(handle.StatusLabel, pad, 66f, w - pad * 2f, 20f);

            handle.ResultLabel = this.CreateUguiLabel(block.transform, "Result", string.Empty, 11f,
                new Color(muted.r, muted.g, muted.b, 0.85f), false);
            PlaceUguiTopLeft(handle.ResultLabel, pad, 88f, w - pad * 2f, 18f);

            handle.Root = block;
            this.uguiShellOrderShopContent = handle;
            return block;
        }

        private void OnUguiOrderShopOpenClicked()
        {
            try
            {
                bool opened = this.TryOpenOrderShopPanel(out string userStatus);
                UguiShellOrderShopContentHandle handle = this.uguiShellOrderShopContent;
                if (handle != null)
                {
                    this.SetUguiLabelText(handle.ResultLabel, userStatus);
                }

                // The game panel is full-screen and sits UNDER the mod overlay — hide the menu so the
                // panel is actually usable (the menu hotkey brings it back, like the close button).
                if (opened && this.uguiShell != null)
                {
                    this.SetUguiWindowVisible(this.uguiShell.Window, false);
                }
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(OrderShopLogTag, "Open order panel click failed: " + ex);
            }
        }

        // Called every frame from ProcessUguiShellOnUpdate; does nothing unless the shell is visible
        // on the Research / Order tab, then works at most once per second.
        private void ProcessUguiShellOrderShopContentOnUpdate()
        {
            UguiShellOrderShopContentHandle handle = this.uguiShellOrderShopContent;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3)
            {
                return;
            }

            bool active = this.IsUguiShellResearchTabActive();
            bool justOpened = active && !handle.WasActive;
            handle.WasActive = active;
            if (!active || (!justOpened && Time.unscaledTime < handle.NextRefreshAt))
            {
                return;
            }
            handle.NextRefreshAt = Time.unscaledTime + 1f;

            try
            {
                this.RefreshOrderShopSnapshotIfDue(justOpened);
                this.RenderUguiOrderShopContent(handle);
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                FeatureLog.Fail(OrderShopLogTag, "Order card refresh error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        private void RenderUguiOrderShopContent(UguiShellOrderShopContentHandle handle)
        {
            OrderShopSnapshot snap = this.orderShopSnapshot;
            string item;
            string status;
            int kind;
            if (!snap.Valid)
            {
                item = this.IsWorldReady ? this.L("Loading order…") : this.L("Order data unavailable here.");
                status = string.Empty;
                kind = 0;
            }
            else if (!snap.HasOrder)
            {
                item = this.L("No active order.");
                status = this.L("The button opens the order catalog.");
                kind = 0;
            }
            else
            {
                item = snap.Name;
                long remaining = this.GetOrderShopRemainingMsNow();
                if (remaining > 0L)
                {
                    // Local ETA = PC clock + remaining; the game clock itself is offset from the PC
                    // clock, so RefreshTime is never converted directly.
                    string eta = DateTime.Now.AddMilliseconds(remaining).ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
                    status = this.LF("Arrives in {0} (around {1})", FormatOrderShopRemaining(remaining), eta);
                    kind = 1;
                }
                else
                {
                    status = this.L("Arrived — ready to collect.");
                    kind = 2;
                }
            }

            if (!string.Equals(item, handle.LastItemText, StringComparison.Ordinal))
            {
                handle.LastItemText = item;
                this.SetUguiLabelText(handle.ItemLabel, item);
            }
            if (!string.Equals(status, handle.LastStatusText, StringComparison.Ordinal))
            {
                handle.LastStatusText = status;
                this.SetUguiLabelText(handle.StatusLabel, status);
            }
            if (kind != handle.LastStatusKind)
            {
                handle.LastStatusKind = kind;
                Color muted = this.UguiKitMutedColor();
                this.SetUguiLabelColor(handle.StatusLabel, kind == 2
                    ? new Color(0.45f, 1f, 0.55f)
                    : kind == 1 ? new Color(1f, 0.85f, 0.45f) : new Color(muted.r, muted.g, muted.b, 0.85f));
            }
        }
    }
}
