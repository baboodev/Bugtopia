using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Settings -> About tab content.
    //
    // Fully static: built once, no per-frame logic at all. Localized like every other tab
    // (only the product name "Bugtopia" stays verbatim). Role mapping: title and
    // headings use header labels, intro/body/version use muted labels.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Settings → About (static content — built once, no per-frame logic at all)
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawAboutTab (HeartopiaComplete.Config.cs): title, intro line, four
        // heading+body pairs, version line. Role mapping: title/headings = header labels
        // (IMGUI headings use the uiHeader color), intro/bodies/version = muted labels (IMGUI
        // bodyStyle uses the subTabText color). Y advances mirror the IMGUI drawer's cursor.
        private GameObject BuildUguiShellAboutContent(Transform parent, float x, float y, float w, float h)
        {
            GameObject block = this.CreateUguiGo("AboutContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            const float pad = 16f;
            float innerW = w - pad * 2f;
            float yCur = 16f;

            GameObject title = this.CreateUguiHeaderLabel(block.transform, "Title", "Bugtopia", 18f);
            PlaceUguiTopLeft(title, pad, yCur, innerW, 28f);
            yCur += 30f;

            GameObject intro = this.CreateUguiMutedLabel(block.transform, "Intro",
                this.L("Automation and utility mod for Heartopia."), 12f);
            this.TrySetUguiLabelWrapped(intro);
            PlaceUguiTopLeft(intro, pad, yCur, innerW, 40f);
            yCur += 44f;

            // Bugtopia News link — a real button (not just text), directly under the intro so it
            // is visible without scrolling. Icon + label, same pairing as the sidebar footer, and
            // the same BugtopiaNewsUrl constant so the two can never drift apart.
            GameObject newsBtn = this.CreateUguiSecondaryButton(block.transform, "NewsButton",
                string.Empty, new System.Action(this.OnUguiShellNewsClicked));
            PlaceUguiTopLeft(newsBtn, pad, yCur, 170f, 30f);
            Image newsIcon = this.CreateUguiIcon(newsBtn.transform, UguiTelegramIconIndex, 15f, this.UguiKitAccent());
            if (newsIcon != null)
            {
                RectTransform newsIconRt = newsIcon.rectTransform;
                newsIconRt.anchorMin = new Vector2(0f, 0.5f);
                newsIconRt.anchorMax = new Vector2(0f, 0.5f);
                newsIconRt.pivot = new Vector2(0f, 0.5f);
                newsIconRt.anchoredPosition = new Vector2(12f, 0f);
                newsIcon.raycastTarget = false; // clicks belong to the button itself
            }
            GameObject newsLabel = this.CreateUguiLabel(newsBtn.transform, "NewsLabel",
                "Bugtopia News", 12f, this.UguiKitTextColor(), false);
            StretchUguiFill(newsLabel, 34f, 0f, 8f, 0f);
            yCur += 38f;

            GameObject h1 = this.CreateUguiHeaderLabel(block.transform, "WhatHeading", this.L("What it does"), 13f);
            PlaceUguiTopLeft(h1, pad, yCur, innerW, 20f);
            yCur += 22f;
            GameObject b1 = this.CreateUguiMutedLabel(block.transform, "WhatBody",
                this.L("Farming, gathering, teleport, radar, bag tools, and other QoL helpers — from one in-game menu. Press Insert to open it."),
                12f);
            this.TrySetUguiLabelWrapped(b1);
            PlaceUguiTopLeft(b1, pad, yCur, innerW, 56f);
            yCur += 62f;

            GameObject h2 = this.CreateUguiHeaderLabel(block.transform, "OpenHeading", this.L("Open & free"), 13f);
            PlaceUguiTopLeft(h2, pad, yCur, innerW, 20f);
            yCur += 22f;
            GameObject b2 = this.CreateUguiMutedLabel(block.transform, "OpenBody",
                this.L("Bugtopia will always stay open-source and free for everyone."), 12f);
            this.TrySetUguiLabelWrapped(b2);
            PlaceUguiTopLeft(b2, pad, yCur, innerW, 40f);
            yCur += 46f;

            GameObject h3 = this.CreateUguiHeaderLabel(block.transform, "CreditsHeading", this.L("Credits"), 13f);
            PlaceUguiTopLeft(h3, pad, yCur, innerW, 20f);
            yCur += 22f;
            GameObject b3 = this.CreateUguiMutedLabel(block.transform, "CreditsBody",
                this.L("Based on Heartopia Helper by Rayyy2.\nThank you to everyone who shares ideas for new features."),
                12f);
            this.TrySetUguiLabelWrapped(b3);
            PlaceUguiTopLeft(b3, pad, yCur, innerW, 56f);
            yCur += 62f;

            GameObject h4 = this.CreateUguiHeaderLabel(block.transform, "DisclaimerHeading", this.L("Disclaimer"), 13f);
            PlaceUguiTopLeft(h4, pad, yCur, innerW, 20f);
            yCur += 22f;
            GameObject b4 = this.CreateUguiMutedLabel(block.transform, "DisclaimerBody",
                this.L("For educational and research use only. Use at your own risk; you are responsible for any account actions taken by the game operator."),
                12f);
            this.TrySetUguiLabelWrapped(b4);
            PlaceUguiTopLeft(b4, pad, yCur, innerW, 56f);
            yCur += 62f;

            GameObject version = this.CreateUguiMutedLabel(block.transform, "Version",
                $"Version {ModBuildVersion.Display} · bugtopia.dll", 12f);
            PlaceUguiTopLeft(version, pad, yCur, innerW, 20f);

            return block;
        }
    }
}
