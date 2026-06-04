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

            return 400f;
        }
    }
}
