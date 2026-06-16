using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private int newFeaturesSubTab;

        private void SetNewFeaturesSubTab(int subTab)
        {
            if (this.newFeaturesSubTab != subTab)
            {
                this.newFeaturesSubTab = subTab;
                this.tabScrollPos = Vector2.zero;
            }
        }

        private float DrawNewFeaturesTab(int startY)
        {
            if (this.newFeaturesSubTab == 0)
            {
                return this.DrawAnimalCareTab(startY);
            }

            if (this.newFeaturesSubTab == 1)
            {
                float y = this.DrawDailyQuestSubmitControls(startY);
                y = this.DrawDailyClaimsControls(y);
                return this.DrawBirdPhotoSubmitControls(y) + 40f;
            }

            if (this.newFeaturesSubTab == 2)
            {
                return this.DrawHomelandFarmTab(startY);
            }

            if (this.newFeaturesSubTab == 3)
            {
                return this.DrawPicturesTab(startY);
            }

            if (this.newFeaturesSubTab == 4)
            {
                return this.DrawIceSkatingExtrasTab(startY);
            }

            if (this.newFeaturesSubTab == 5)
            {
                return this.DrawBuildingTab(startY);
            }

            return startY + 40f;
        }

        private float DrawAnimalCareTab(int startY)
        {
            float num = this.DrawWildAnimalFeedSection(startY);
            num = this.DrawWildAnimalGiftSection(num);
            return num + 40f;
        }

        private float CalculateNewFeaturesTabHeight()
        {
            if (this.newFeaturesSubTab == 0)
            {
                return 560f;
            }

            if (this.newFeaturesSubTab == 1)
            {
                return 560f;
            }

            if (this.newFeaturesSubTab == 2)
            {
                return 1230f;
            }

            if (this.newFeaturesSubTab == 3)
            {
                return 640f;
            }

            if (this.newFeaturesSubTab == 4)
            {
                return 480f;
            }

            if (this.newFeaturesSubTab == 5)
            {
                return 200f;
            }

            return 400f;
        }
    }
}
