using System;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    internal static class WarehouseBypassFeature
    {
        private const string BanButtonRelativePath = "tabs/ban@btn";
        private const string TabBarRelativePath = "tabs/tabBar@w";
        private const float UiBypassInterval = 0.25f;
        private const int BagCloseSettleFrames = 10;

        private static bool loggedArchitectureNote;
        private static bool loggedMonoTabUnlock;
        private static bool bagWasOpen;
        private static int bagCloseSettleFrames;
        private static float nextUiBypassAt = -999f;
        private static int warehouseTabMonoWarmupFrames;

        internal static void ResetState()
        {
            loggedArchitectureNote = false;
            bagWasOpen = false;
            bagCloseSettleFrames = 0;
            loggedMonoTabUnlock = false;
            nextUiBypassAt = -999f;
            warehouseTabMonoWarmupFrames = 0;
        }

        internal static void ResetWarehouseTabMonoWarmup()
        {
            warehouseTabMonoWarmupFrames = 0;
        }

        internal static void LogMonoTabUnlockOnce(string message)
        {
            if (loggedMonoTabUnlock)
            {
                return;
            }

            loggedMonoTabUnlock = true;
            ModLogger.Msg(message);
        }

        internal static void ResetWarehouseBagSession()
        {
            warehouseTabMonoWarmupFrames = 0;
        }

        internal static void Update(HeartopiaComplete host)
        {
            if (host == null || !host.WarehouseBypassEnabled)
            {
                return;
            }

            host.ModWarehouseBypassTickBagOpenState(out bool monoBagOpen);
            if (!monoBagOpen)
            {
                if (bagWasOpen)
                {
                    bagCloseSettleFrames++;
                    if (bagCloseSettleFrames >= BagCloseSettleFrames)
                    {
                        host.ModWarehouseOnBagClosed();
                        bagWasOpen = false;
                        bagCloseSettleFrames = 0;
                    }
                }
                else
                {
                    bagCloseSettleFrames = 0;
                }

                return;
            }

            bagCloseSettleFrames = 0;
            bool firstOpenThisSession = !bagWasOpen;
            bagWasOpen = true;

            if (firstOpenThisSession)
            {
                warehouseTabMonoWarmupFrames = 0;
            }

            if (!HeartopiaComplete.ModTryUnityFindActiveBagPanel(out GameObject bag) || bag == null || !bag.activeInHierarchy)
            {
                return;
            }

            bool unityStale = HeartopiaComplete.ModTryUnityBagPanelLooksStaleClosed(bag);

            if (warehouseTabMonoWarmupFrames < 3)
            {
                warehouseTabMonoWarmupFrames++;
                return;
            }

            float now = Time.unscaledTime;
            if (!unityStale && now >= nextUiBypassAt)
            {
                nextUiBypassAt = now + UiBypassInterval;
                TryApplyBagPanelUiBypass(bag);
            }

            bool itemSelected = IsTooltipActive(bag.transform, "tip@w")
                || IsTooltipActive(bag.transform, "microHomeland@w");

            if (!unityStale)
            {
                host.ModTryEnableWarehouseTabViaMono(itemSelected);
            }
        }

        private static void TryApplyBagPanelUiBypass(GameObject bag)
        {
            DisableBanOverlay(bag.transform);
            EnableWarehouseTabUnity(bag.transform);

            if (!loggedArchitectureNote)
            {
                loggedArchitectureNote = true;
                ModLogger.Msg("[WarehouseBypass] UI bypass active while bag is open (no inHomeland spoof).");
            }
        }

        private static bool IsTooltipActive(Transform bagRoot, string nameFragment)
        {
            Transform tip = FindChildTransformByName(bagRoot, nameFragment);
            return tip != null && tip.gameObject.activeInHierarchy;
        }

        private static void DisableBanOverlay(Transform bagRoot)
        {
            Transform banTransform = bagRoot.Find(BanButtonRelativePath) ?? FindChildTransformByName(bagRoot, "ban@btn");
            if (banTransform == null)
            {
                return;
            }

            banTransform.gameObject.SetActive(false);
            Button banButton = banTransform.GetComponent<Button>();
            if (banButton != null)
            {
                banButton.interactable = false;
            }

            Graphic banGraphic = banTransform.GetComponent<Graphic>();
            if (banGraphic != null)
            {
                banGraphic.raycastTarget = false;
            }
        }

        private static void EnableWarehouseTabUnity(Transform bagRoot)
        {
            Transform tabBarTransform = bagRoot.Find(TabBarRelativePath) ?? FindChildTransformByName(bagRoot, "tabBar@w");
            if (tabBarTransform == null || tabBarTransform.childCount < 2)
            {
                return;
            }

            Transform warehouseTabTransform = tabBarTransform.GetChild(1);
            foreach (Graphic graphic in warehouseTabTransform.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = true;
            }

            foreach (Button button in warehouseTabTransform.GetComponentsInChildren<Button>(true))
            {
                button.interactable = true;
            }
        }

        internal static Transform FindChildTransformByName(Transform root, string nameFragment)
        {
            if (root == null || string.IsNullOrEmpty(nameFragment))
            {
                return null;
            }

            if (root.name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildTransformByName(root.GetChild(i), nameFragment);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
