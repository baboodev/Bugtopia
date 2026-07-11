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

        // Fashionwave / battle-pass TOKEN sell UI (XDTGame.UI.Panel.BattlePassSellPanel) — the panel
        // that shows "Available in Fashionwave" items and sells them for the current period token via
        // RecycleProtocolManager.CmdBattlePassSell (same currency as Auto Sell "Festival For Tokens").
        // Opened via its OWN static Open() which builds the intent (current period currency +
        // per-period override prefab) and UIManager.OpenView<BattlePassSellPanel>. Separate from
        // ItemSellPanel above, which is the COIN sell (CmdQuickSell).
        private const string TokenSellPanelTypeName = "XDTGame.UI.Panel.BattlePassSellPanel";
        private string tokenSellPanelStatus = "Idle.";

        // XDT* UI type -> AuraMono only (no managed fallback, per project convention). Invoke the
        // 0-arg static BattlePassSellPanel.Open(); it self-populates from the active battle-pass period.
        private void StartTokenSellOpenPanel()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    this.tokenSellPanelStatus = "Aura mono runtime not ready.";
                    this.AddMenuNotification(this.tokenSellPanelStatus, new Color(1f, 0.55f, 0.45f));
                    return;
                }

                IntPtr panelClass = this.FindAuraMonoClassByFullName(TokenSellPanelTypeName);
                if (panelClass == IntPtr.Zero)
                {
                    panelClass = this.FindAuraMonoClassInImages(
                        "XDTGame.UI.Panel",
                        "BattlePassSellPanel",
                        new string[] { "XDTGameUI", "XDTGameUI.dll", "Client", "Client.dll" });
                }
                if (panelClass == IntPtr.Zero)
                {
                    this.tokenSellPanelStatus = "BattlePassSellPanel class not found.";
                    this.AddMenuNotification(this.tokenSellPanelStatus, new Color(1f, 0.55f, 0.45f));
                    return;
                }

                IntPtr openMethod = this.FindAuraMonoMethodOnHierarchy(panelClass, "Open", 0);
                if (openMethod == IntPtr.Zero)
                {
                    this.tokenSellPanelStatus = "BattlePassSellPanel.Open() not found.";
                    this.AddMenuNotification(this.tokenSellPanelStatus, new Color(1f, 0.55f, 0.45f));
                    return;
                }

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(openMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.tokenSellPanelStatus = "BattlePassSellPanel.Open() raised an exception (no active period?).";
                    this.AddMenuNotification(this.tokenSellPanelStatus, new Color(1f, 0.55f, 0.45f));
                    return;
                }

                this.tokenSellPanelStatus = "Opened token sell panel.";
                this.AddMenuNotification(this.tokenSellPanelStatus, new Color(0.45f, 0.88f, 1f));
            }
            catch (Exception ex)
            {
                this.tokenSellPanelStatus = "Token sell open exception: " + ex.GetType().Name;
                this.AddMenuNotification(this.tokenSellPanelStatus, new Color(1f, 0.55f, 0.45f));
            }
        }

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
