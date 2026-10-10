using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // Hobby category, display position right after New Features. Subs: Pumpkin Carving,
    // Sand Sculpture, Snow Sculpting. Ice Skating stays on New Features.
    public partial class HeartopiaComplete
    {
        private sealed class UguiShellHobbyPumpkinHandle
        {
            public GameObject Root;
            public Transform ScrollContent;
            public Toggle AutoToggle;
            public Toggle PlaceToggle;
            public Toggle CollectToggle;
            public GameObject StatusLabel;
            public string StatusShown;
            public int ErrorCount;
        }

        private UguiShellHobbyPumpkinHandle uguiShellHobbyPumpkin;

        private GameObject BuildUguiShellHobbyPumpkinContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellHobbyPumpkin = null;
            UguiShellHobbyPumpkinHandle handle = new UguiShellHobbyPumpkinHandle();
            GameObject block = this.CreateUguiGo("HobbyPumpkinContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            Transform scrollContent;
            GameObject scroll = this.CreateUguiScrollView(block.transform, "Scroll", 10f, out scrollContent);
            PlaceUguiTopLeft(scroll, 0f, 0f, w, h);
            handle.ScrollContent = scrollContent;

            Color muted = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);
            GameObject header = this.CreateUguiLabel(scrollContent, "Header", this.L("Pumpkin Carving"), 14f, this.UguiKitTextColor(), false);
            this.TrySetUguiLabelBold(header);
            PlaceUguiTopLeft(header, 8f, 8f, 460f, 24f);

            handle.AutoToggle = this.CreateUguiCheckbox(scrollContent, "AutoPumpkin",
                this.L("Auto Pumpkin Carving"), this.autoPumpkinEnabled,
                new System.Action<bool>(this.OnUguiHobbyPumpkinAutoToggled));
            PlaceUguiTopLeft(handle.AutoToggle.gameObject, 8f, 42f, 420f, 30f);

            GameObject hint = this.CreateUguiLabel(scrollContent, "Hint",
                this.L("Reports a perfect score immediately. The carving panel stays closed."),
                11f, muted, false);
            this.TrySetUguiLabelWrapped(hint);
            PlaceUguiTopLeft(hint, 8f, 78f, w - 36f, 36f);

            handle.PlaceToggle = this.CreateUguiCheckbox(scrollContent, "AutoPlacePumpkin",
                this.L("Auto-place pumpkin from backpack"), this.autoPumpkinPlace,
                new System.Action<bool>(this.OnUguiHobbyPumpkinPlaceToggled));
            PlaceUguiTopLeft(handle.PlaceToggle.gameObject, 8f, 122f, 460f, 30f);

            handle.CollectToggle = this.CreateUguiCheckbox(scrollContent, "AutoCollectPumpkin",
                this.L("Auto-collect carved pumpkin"), this.autoPumpkinCollect,
                new System.Action<bool>(this.OnUguiHobbyPumpkinCollectToggled));
            PlaceUguiTopLeft(handle.CollectToggle.gameObject, 8f, 158f, 420f, 30f);

            handle.StatusShown = this.BuildUguiHobbyPumpkinStatusText();
            handle.StatusLabel = this.CreateUguiLabel(scrollContent, "Status", handle.StatusShown, 12f, this.UguiKitTextColor(), false);
            PlaceUguiTopLeft(handle.StatusLabel, 8f, 196f, w - 36f, 22f);
            this.SetUguiScrollContentHeight(scrollContent, 236f);

            handle.Root = block;
            this.uguiShellHobbyPumpkin = handle;
            return block;
        }

        private string BuildUguiHobbyPumpkinStatusText()
        {
            return this.L("Status") + ": " + this.pumpkinStatus + "    " + this.L("Done") + " " + this.pumpkinDoneCount;
        }

        private void OnUguiHobbyPumpkinAutoToggled(bool value)
        {
            if (value == this.autoPumpkinEnabled)
            {
                return;
            }

            this.autoPumpkinEnabled = value;
            this.AddMenuNotification(
                this.L("Auto Pumpkin Carving") + ": " + (value ? this.L("On") : this.L("Off")),
                value ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
        }

        private void OnUguiHobbyPumpkinPlaceToggled(bool value)
        {
            this.autoPumpkinPlace = value;
        }

        private void OnUguiHobbyPumpkinCollectToggled(bool value)
        {
            this.autoPumpkinCollect = value;
        }

        private bool IsUguiShellHobbySubTabActive(int subIndex)
        {
            try
            {
                UguiShellHandle shell = this.uguiShell;
                if (shell == null || shell.ActiveIndex != UguiShellHobbyTabIndex || !this.IsUguiWindowVisible(shell.Window))
                {
                    return false;
                }

                UguiTabBarHandle bar = (UguiShellHobbyTabIndex < shell.SubTabBars.Count)
                    ? shell.SubTabBars[UguiShellHobbyTabIndex]
                    : null;
                return bar != null && bar.ActiveIndex == subIndex;
            }
            catch
            {
                return false;
            }
        }

        private void ProcessUguiShellHobbyPumpkinOnUpdate()
        {
            UguiShellHobbyPumpkinHandle handle = this.uguiShellHobbyPumpkin;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellHobbySubTabActive(UguiShellHobbyPumpkinSubIndex))
            {
                return;
            }

            try
            {
                this.SyncUguiToggleFromField(handle.AutoToggle, this.autoPumpkinEnabled);
                this.SyncUguiToggleFromField(handle.PlaceToggle, this.autoPumpkinPlace);
                this.SyncUguiToggleFromField(handle.CollectToggle, this.autoPumpkinCollect);
                this.SyncUguiSelfLabelText(handle.StatusLabel, ref handle.StatusShown, this.BuildUguiHobbyPumpkinStatusText());
            }
            catch
            {
                handle.ErrorCount++;
            }
        }
    }
}
