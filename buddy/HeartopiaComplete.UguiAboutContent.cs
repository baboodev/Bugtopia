using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Settings -> About tab content.
    //
    // Fully static: built once, no per-frame logic at all. Text is verbatim and deliberately
    // UNLOCALIZED (the original drawer did not localize About either). Role mapping: title and
    // headings use header labels, intro/body/version use muted labels.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Settings → About (static content — built once, no per-frame logic at all)
        // ----------------------------------------------------------------------------------------

        // UGUI mirror of DrawAboutTab (HeartopiaComplete.Config.cs): title, intro line, four
        // heading+body pairs, version line. Text copied verbatim (the IMGUI drawer does not
        // localize About, so neither does this). Role mapping: title/headings = header labels
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
                "Automation and utility mod for Heartopia.", 12f);
            this.TrySetUguiLabelWrapped(intro);
            PlaceUguiTopLeft(intro, pad, yCur, innerW, 40f);
            yCur += 44f;

            GameObject h1 = this.CreateUguiHeaderLabel(block.transform, "WhatHeading", "What it does", 13f);
            PlaceUguiTopLeft(h1, pad, yCur, innerW, 20f);
            yCur += 22f;
            GameObject b1 = this.CreateUguiMutedLabel(block.transform, "WhatBody",
                "Farming, gathering, teleport, radar, bag tools, and other QoL helpers — from one in-game menu. Press Insert to open it.",
                12f);
            this.TrySetUguiLabelWrapped(b1);
            PlaceUguiTopLeft(b1, pad, yCur, innerW, 56f);
            yCur += 62f;

            GameObject h2 = this.CreateUguiHeaderLabel(block.transform, "OpenHeading", "Open & free", 13f);
            PlaceUguiTopLeft(h2, pad, yCur, innerW, 20f);
            yCur += 22f;
            GameObject b2 = this.CreateUguiMutedLabel(block.transform, "OpenBody",
                "Bugtopia will always stay open-source and free for everyone.", 12f);
            this.TrySetUguiLabelWrapped(b2);
            PlaceUguiTopLeft(b2, pad, yCur, innerW, 40f);
            yCur += 46f;

            GameObject h3 = this.CreateUguiHeaderLabel(block.transform, "CreditsHeading", "Credits", 13f);
            PlaceUguiTopLeft(h3, pad, yCur, innerW, 20f);
            yCur += 22f;
            GameObject b3 = this.CreateUguiMutedLabel(block.transform, "CreditsBody",
                "Based on Heartopia Helper by Rayyy2.\nThank you to everyone who shares ideas for new features.",
                12f);
            this.TrySetUguiLabelWrapped(b3);
            PlaceUguiTopLeft(b3, pad, yCur, innerW, 56f);
            yCur += 62f;

            GameObject h4 = this.CreateUguiHeaderLabel(block.transform, "DisclaimerHeading", "Disclaimer", 13f);
            PlaceUguiTopLeft(h4, pad, yCur, innerW, 20f);
            yCur += 22f;
            GameObject b4 = this.CreateUguiMutedLabel(block.transform, "DisclaimerBody",
                "For educational and research use only. Use at your own risk; you are responsible for any account actions taken by the game operator.",
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
