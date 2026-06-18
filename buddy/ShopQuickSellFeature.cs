using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Opens the game's player-inventory SELL UI (XDTGame.UI.Panel.ItemSellPanel) — the quick-sell /
    // recycle panel that backs RecycleProtocolManager.CmdQuickSell. This is NOT the shop BUY flow:
    // SalePanel (see ShopQuickBuyFeature.cs) is buy-only (ShopItemData -> SalePanel.Open). ItemSellPanel
    // has no item parameter; it self-populates from BackPackSystem.GetItems, so opening it needs no
    // netId/staticId/count input — just UIManager.OpenView<ItemSellPanel>(new Intent()), exactly how the
    // game opens it (UIEventBridge.OnCoinBusinessFeatureRequested / DialogueNodeBranch case 5).
    public partial class HeartopiaComplete
    {
        private const string ShopQuickSellPanelTypeName = "XDTGame.UI.Panel.ItemSellPanel";

        private string shopQuickSellStatus = "Idle.";

        private void StartShopQuickSellOpenPanel()
        {
            const string successStatus = "Opened ItemSellPanel.";

            if (this.TryOpenSellPanelManaged(successStatus, out string managedError))
            {
                this.shopQuickSellStatus = "Opened ItemSellPanel (managed).";
                this.AddMenuNotification(this.shopQuickSellStatus, new Color(0.45f, 0.88f, 1f));
                return;
            }

            if (this.TryOpenSellPanelAura(successStatus, out string auraError))
            {
                this.shopQuickSellStatus = "Opened ItemSellPanel (aura).";
                this.AddMenuNotification(this.shopQuickSellStatus, new Color(0.45f, 0.88f, 1f));
                return;
            }

            this.shopQuickSellStatus = auraError ?? managedError ?? "Open failed.";
            this.AddMenuNotification(this.shopQuickSellStatus, new Color(1f, 0.55f, 0.45f));
        }

        // Managed path: UIManager.OpenView(Type, Intent) when the interop stub for ItemSellPanel exists.
        private bool TryOpenSellPanelManaged(string successStatus, out string error)
        {
            error = null;
            if (this.TryOpenPanelByResolvedTypeName(ShopQuickSellPanelTypeName, null, successStatus))
            {
                return true;
            }

            error = "managed ItemSellPanel open failed (type or UIManager.OpenView missing).";
            return false;
        }

        // AuraMono path: XDTGame.UI.Panel.* are usually AuraMono-class-only (no interop stub), so resolve
        // the class and invoke UIManager.OpenView(Type, null-Intent) through mono_runtime_invoke.
        private bool TryOpenSellPanelAura(string successStatus, out string error)
        {
            error = null;
            if (this.TryOpenAuraPanelByTypeNameViaMono(ShopQuickSellPanelTypeName, successStatus))
            {
                return true;
            }

            error = string.IsNullOrEmpty(this.forceOpenShopStatus)
                ? "aura ItemSellPanel open failed."
                : this.forceOpenShopStatus;
            return false;
        }
    }
}
