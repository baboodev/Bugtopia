using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI SHELL — Self → Game LOD (new page, no IMGUI twin: the IMGUI menu is retired).
    //
    // UI for GameLodFeature.cs (world detail / draw distance overrides). Follows the round-5 Self
    // content ground rules (HeartopiaComplete.UguiSelfContent.cs header): built once inside the
    // shell, wiring by static display index (UguiShellSelfGameLodSubIndex), kit checkboxes (not
    // switches), per-frame processor gated on "shell visible AND Self tab AND this sub-tab", value
    // sync via SetValueWithoutNotify/SetIsOnWithoutNotify only, live status lines on the 0.5s slow
    // tick (they change from the feature's background apply loop, not from user edits).
    //
    // Content is ~2x the cell height → scrolls (Settings→Main precedent). No conditional relayout:
    // sliders stay visible while their toggle is off (the toggle gates APPLY, not visibility) —
    // matches the Fun sub-tab's sprint sliders, avoids the layout-signature machinery.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private sealed class UguiShellSelfGameLodHandle
        {
            public GameObject Root;
            public Transform ScrollContent;

            public Toggle FurnitureToggle;
            public GameObject FurnitureMaxLabel;
            public string FurnitureMaxShown;
            public Slider FurnitureMaxSlider;
            public GameObject FurnitureDistLabel;
            public string FurnitureDistShown;
            public Slider FurnitureDistSlider;
            public GameObject FurnitureMeshLabel;
            public string FurnitureMeshShown;
            public Slider FurnitureMeshSlider;
            public GameObject FurnitureStatusLabel;
            public string FurnitureStatusShown;

            // UGC Texture Cache — moved here from the Pictures sub-tab (2026-07-26): blank UGC
            // photo textures turned out to be a side effect of this section's own draw-distance
            // extension (see GameLodFeature.cs TryGameLodFurnitureApply header + project memory
            // ugc-texture-cache-blank-fix / world-lod-streaming-map r13), so the mitigation controls
            // now live next to the setting that triggers the problem.
            public GameObject CachePurgeButton;
            public GameObject CachePurgeStatusLabel;
            public string CachePurgeStatusShown;
            public Toggle CacheRaiseLimitToggle;
            public GameObject CacheCapacityLabel;
            public string CacheCapacityShown;
            public Slider CacheCapacitySlider;
            public GameObject CacheApplyStatusLabel;
            public string CacheApplyStatusShown;

            public Toggle ForceLod0Toggle;
            public Toggle BrgBiasToggle;
            public GameObject BrgBiasLabel;
            public string BrgBiasShown;
            public Slider BrgBiasSlider;
            public GameObject BrgStatusLabel;
            public string BrgStatusShown;

            public Toggle VegetationToggle;
            public Toggle VegetationDuringLoadToggle;
            public GameObject VegetationLabel;
            public string VegetationShown;
            public Slider VegetationSlider;
            public GameObject VegetationStatusLabel;
            public string VegetationStatusShown;

            public Toggle HlodToggle;
            public GameObject HlodLabel;
            public string HlodShown;
            public Slider HlodSlider;
            public GameObject HlodStatusLabel;
            public string HlodStatusShown;

            public Toggle XdLodToggle;
            public GameObject XdLodStatusLabel;
            public string XdLodStatusShown;

            public Toggle SignificanceToggle;
            public GameObject SignificanceStatusLabel;
            public string SignificanceStatusShown;

            public Toggle NineCellToggle;
            public GameObject NineCellLabel;
            public string NineCellShown;
            public Slider NineCellSlider;
            public GameObject NineCellStatusLabel;
            public string NineCellStatusShown;

            public Toggle ShadowToggle;
            public GameObject ShadowLabel;
            public string ShadowShown;
            public Slider ShadowSlider;
            public GameObject ShadowStatusLabel;
            public string ShadowStatusShown;

            public float NextSlowSyncAt;
            public int ErrorCount;
        }

        private UguiShellSelfGameLodHandle uguiShellSelfGameLod;

        private GameObject BuildUguiShellSelfGameLodContent(Transform parent, float x, float y, float w, float h)
        {
            this.uguiShellSelfGameLod = null;

            UguiShellSelfGameLodHandle handle = new UguiShellSelfGameLodHandle();
            GameObject block = this.CreateUguiGo("SelfGameLodContent", parent);
            PlaceUguiTopLeft(block, x, y, w, h);
            this.AddUguiImage(block, this.UguiKitContentBg(), true, 1f);

            Transform scrollContent;
            GameObject scroll = this.CreateUguiScrollView(block.transform, "Scroll", 10f, out scrollContent);
            PlaceUguiTopLeft(scroll, 0f, 0f, w, h);
            try
            {
                Image scrollBg = scroll.GetComponent<Image>();
                if (scrollBg != null)
                {
                    scrollBg.color = new Color(0f, 0f, 0f, 0f);
                }
            }
            catch { }

            const float pad = 16f;
            const float labelW = 210f;
            float rowW = w - pad * 2f - 24f; // scrollbar clearance
            float sliderX = pad + labelW + 10f;
            float sliderW = rowW - labelW - 10f;
            Color muted = this.UguiKitMutedColor();
            Color hintColor = new Color(muted.r, muted.g, muted.b, 0.85f);
            float yCur = 12f;

            // ---------------- FURNITURE & BUILDINGS ----------------
            GameObject furnHeader = this.CreateUguiHeaderLabel(scrollContent, "FurnitureHeader",
                this.L("FURNITURE & BUILDINGS"), 12f);
            PlaceUguiTopLeft(furnHeader, pad, yCur, rowW, 18f);
            yCur += 24f;

            handle.FurnitureToggle = this.CreateUguiCheckbox(scrollContent, "FurnitureToggle",
                this.L("Extend furniture draw distance"), this.gameLodFurnitureEnabled,
                new System.Action<bool>(this.OnUguiGameLodFurnitureToggled));
            PlaceUguiTopLeft(handle.FurnitureToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 30f;

            handle.FurnitureMaxShown = this.LF("Max objects: {0}", this.gameLodFurnitureMaxObjects);
            handle.FurnitureMaxLabel = this.CreateUguiBodyLabel(scrollContent, "FurnitureMaxLabel",
                handle.FurnitureMaxShown, 13f);
            PlaceUguiTopLeft(handle.FurnitureMaxLabel, pad, yCur + 2f, labelW, 20f);
            handle.FurnitureMaxSlider = this.CreateUguiSlider(scrollContent, "FurnitureMaxSlider",
                60f, 5000f, this.gameLodFurnitureMaxObjects, true,
                new System.Action<float>(this.OnUguiGameLodFurnitureMaxChanged));
            PlaceUguiTopLeft(handle.FurnitureMaxSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 28f;

            handle.FurnitureDistShown = this.LF("Draw distance: {0} m", this.gameLodFurnitureDistance);
            handle.FurnitureDistLabel = this.CreateUguiBodyLabel(scrollContent, "FurnitureDistLabel",
                handle.FurnitureDistShown, 13f);
            PlaceUguiTopLeft(handle.FurnitureDistLabel, pad, yCur + 2f, labelW, 20f);
            handle.FurnitureDistSlider = this.CreateUguiSlider(scrollContent, "FurnitureDistSlider",
                100f, 9999f, this.gameLodFurnitureDistance, true,
                new System.Action<float>(this.OnUguiGameLodFurnitureDistChanged));
            PlaceUguiTopLeft(handle.FurnitureDistSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 28f;

            handle.FurnitureMeshShown = this.LF("Mesh detail distance: {0} m", this.gameLodFurnitureMeshDistance);
            handle.FurnitureMeshLabel = this.CreateUguiBodyLabel(scrollContent, "FurnitureMeshLabel",
                handle.FurnitureMeshShown, 13f);
            PlaceUguiTopLeft(handle.FurnitureMeshLabel, pad, yCur + 2f, labelW, 20f);
            handle.FurnitureMeshSlider = this.CreateUguiSlider(scrollContent, "FurnitureMeshSlider",
                100f, 2000f, this.gameLodFurnitureMeshDistance, true,
                new System.Action<float>(this.OnUguiGameLodFurnitureMeshChanged));
            PlaceUguiTopLeft(handle.FurnitureMeshSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 26f;

            GameObject furnHint = this.CreateUguiLabel(scrollContent, "FurnitureHint",
                this.L("Heavy settings are applied a few seconds AFTER the world loads, so loading stays fast. Max objects and draw distance are the main memory cost — lower them if loading or streaming feels heavy."),
                11f, hintColor, false);
            this.TrySetUguiLabelWrapped(furnHint);
            PlaceUguiTopLeft(furnHint, pad, yCur, rowW, 30f);
            yCur += 34f;

            handle.FurnitureStatusShown = this.BuildUguiGameLodStatusText(this.gameLodFurnitureStatus);
            handle.FurnitureStatusLabel = this.CreateUguiLabel(scrollContent, "FurnitureStatus",
                handle.FurnitureStatusShown, 11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.FurnitureStatusLabel);
            PlaceUguiTopLeft(handle.FurnitureStatusLabel, pad, yCur, rowW, 18f);
            yCur += 28f;

            // ---------------- UGC TEXTURE CACHE ----------------
            GameObject ugcHeader = this.CreateUguiHeaderLabel(scrollContent, "UgcCacheHeader",
                this.L("UGC TEXTURE CACHE"), 12f);
            PlaceUguiTopLeft(ugcHeader, pad, yCur, rowW, 18f);
            yCur += 24f;

            GameObject ugcHint = this.CreateUguiLabel(scrollContent, "UgcCacheHint",
                this.L("Photo frames, screens and custom-photo furniture can render blank/white — either the game's own 100-item texture cache evicting mid-session (Purge + raise capacity below help), or the furniture setting above streaming in too many at once and overwhelming downloads. If Purge doesn't help, try lowering furniture draw distance/count first."),
                11f, hintColor, false);
            this.TrySetUguiLabelWrapped(ugcHint);
            PlaceUguiTopLeft(ugcHint, pad, yCur, rowW, 44f);
            yCur += 50f;

            handle.CachePurgeButton = this.CreateUguiSecondaryButton(scrollContent, "CachePurgeButton",
                this.L("Purge Texture Cache"), new System.Action(this.OnUguiUgcCachePurgeClicked));
            PlaceUguiTopLeft(handle.CachePurgeButton, pad, yCur, 180f, 24f);
            yCur += 30f;

            handle.CachePurgeStatusShown = this.BuildUguiUgcCachePurgeStatusText();
            handle.CachePurgeStatusLabel = this.CreateUguiLabel(scrollContent, "CachePurgeStatus",
                handle.CachePurgeStatusShown, 11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.CachePurgeStatusLabel);
            PlaceUguiTopLeft(handle.CachePurgeStatusLabel, pad, yCur, rowW, 18f);
            yCur += 26f;

            handle.CacheRaiseLimitToggle = this.CreateUguiCheckbox(scrollContent, "CacheRaiseLimitToggle",
                this.L("Raise cache capacity"), this.ugcCacheRaiseLimitEnabled,
                new System.Action<bool>(this.OnUguiUgcCacheRaiseLimitToggled));
            PlaceUguiTopLeft(handle.CacheRaiseLimitToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 30f;

            handle.CacheCapacityShown = this.BuildUguiUgcCacheCapacityText();
            handle.CacheCapacityLabel = this.CreateUguiBodyLabel(scrollContent, "CacheCapacityLabel",
                handle.CacheCapacityShown, 13f);
            PlaceUguiTopLeft(handle.CacheCapacityLabel, pad, yCur + 2f, labelW, 20f);
            handle.CacheCapacitySlider = this.CreateUguiSlider(scrollContent, "CacheCapacitySlider",
                UgcCacheMinCapacity, UgcCacheMaxCapacity, this.ugcCacheTargetCapacity, true,
                new System.Action<float>(this.OnUguiUgcCacheCapacityChanged));
            PlaceUguiTopLeft(handle.CacheCapacitySlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 28f;

            handle.CacheApplyStatusShown = this.BuildUguiUgcCacheApplyStatusText();
            handle.CacheApplyStatusLabel = this.CreateUguiLabel(scrollContent, "CacheApplyStatus",
                handle.CacheApplyStatusShown, 11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.CacheApplyStatusLabel);
            PlaceUguiTopLeft(handle.CacheApplyStatusLabel, pad, yCur, rowW, 18f);
            yCur += 28f;

            // ---------------- MESH QUALITY ----------------
            GameObject brgHeader = this.CreateUguiHeaderLabel(scrollContent, "BrgHeader",
                this.L("MESH QUALITY"), 12f);
            PlaceUguiTopLeft(brgHeader, pad, yCur, rowW, 18f);
            yCur += 24f;

            handle.ForceLod0Toggle = this.CreateUguiCheckbox(scrollContent, "ForceLod0Toggle",
                this.L("Force max mesh detail (BRG LOD0)"), this.gameLodForceLod0Enabled,
                new System.Action<bool>(this.OnUguiGameLodForceLod0Toggled));
            PlaceUguiTopLeft(handle.ForceLod0Toggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 30f;

            handle.BrgBiasToggle = this.CreateUguiCheckbox(scrollContent, "BrgBiasToggle",
                this.L("Boost furniture LOD bias"), this.gameLodBrgBiasEnabled,
                new System.Action<bool>(this.OnUguiGameLodBrgBiasToggled));
            PlaceUguiTopLeft(handle.BrgBiasToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 30f;

            handle.BrgBiasShown = this.LF("LOD bias: x{0:0.0}", this.gameLodBrgBias);
            handle.BrgBiasLabel = this.CreateUguiBodyLabel(scrollContent, "BrgBiasLabel",
                handle.BrgBiasShown, 13f);
            PlaceUguiTopLeft(handle.BrgBiasLabel, pad, yCur + 2f, labelW, 20f);
            handle.BrgBiasSlider = this.CreateUguiSlider(scrollContent, "BrgBiasSlider",
                1f, 4f, this.gameLodBrgBias, false,
                new System.Action<float>(this.OnUguiGameLodBrgBiasChanged));
            PlaceUguiTopLeft(handle.BrgBiasSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 26f;

            handle.BrgStatusShown = this.BuildUguiGameLodStatusText(this.gameLodBrgStatus);
            handle.BrgStatusLabel = this.CreateUguiLabel(scrollContent, "BrgStatus",
                handle.BrgStatusShown, 11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.BrgStatusLabel);
            PlaceUguiTopLeft(handle.BrgStatusLabel, pad, yCur, rowW, 18f);
            yCur += 28f;

            // ---------------- VEGETATION & NEIGHBOR HOUSES ----------------
            GameObject vegHeader = this.CreateUguiHeaderLabel(scrollContent, "VegHeader",
                this.L("VEGETATION & NEIGHBOR HOUSES"), 12f);
            PlaceUguiTopLeft(vegHeader, pad, yCur, rowW, 18f);
            yCur += 24f;

            handle.VegetationToggle = this.CreateUguiCheckbox(scrollContent, "VegetationToggle",
                this.L("Extend tree/house LOD distance"), this.gameLodVegetationEnabled,
                new System.Action<bool>(this.OnUguiGameLodVegetationToggled));
            PlaceUguiTopLeft(handle.VegetationToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 30f;

            handle.VegetationShown = this.BuildUguiGameLodVegetationText();
            handle.VegetationLabel = this.CreateUguiBodyLabel(scrollContent, "VegetationLabel",
                handle.VegetationShown, 13f);
            PlaceUguiTopLeft(handle.VegetationLabel, pad, yCur + 2f, labelW, 20f);
            handle.VegetationSlider = this.CreateUguiSlider(scrollContent, "VegetationSlider",
                10f, 1000f, this.GameLodVegetationSliderValue(), true,
                new System.Action<float>(this.OnUguiGameLodVegetationChanged));
            PlaceUguiTopLeft(handle.VegetationSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 30f;

            handle.VegetationDuringLoadToggle = this.CreateUguiCheckbox(scrollContent, "VegDuringLoadToggle",
                this.L("Also apply while the world loads (slower loading, full terrain detail)"),
                this.gameLodVegetationApplyDuringLoad,
                new System.Action<bool>(this.OnUguiGameLodVegetationDuringLoadToggled));
            PlaceUguiTopLeft(handle.VegetationDuringLoadToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 30f;

            GameObject rebakeBtn = this.CreateUguiSecondaryButton(scrollContent, "VegRebakeButton",
                this.L("Rebake now"), new System.Action(this.OnUguiGameLodVegetationRebakeClicked));
            PlaceUguiTopLeft(rebakeBtn, pad, yCur, 140f, 24f);
            yCur += 30f;

            GameObject vegHint = this.CreateUguiLabel(scrollContent, "VegHint",
                this.L("Sets the game's PC_LODBIAS quality value (trees, rocks, grass, terrain chunks) — higher holds high-poly meshes further out; 10 is the engine default. Terrain chunks bake this at load time, so islands and cliffs near your spawn only get the full value with the checkbox above."),
                11f, hintColor, false);
            this.TrySetUguiLabelWrapped(vegHint);
            PlaceUguiTopLeft(vegHint, pad, yCur, rowW, 30f);
            yCur += 34f;

            handle.VegetationStatusShown = this.BuildUguiGameLodStatusText(this.gameLodVegetationStatus);
            handle.VegetationStatusLabel = this.CreateUguiLabel(scrollContent, "VegStatus",
                handle.VegetationStatusShown, 11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.VegetationStatusLabel);
            PlaceUguiTopLeft(handle.VegetationStatusLabel, pad, yCur, rowW, 18f);
            yCur += 28f;

            // ---------------- LANDSCAPE (HLOD) ----------------
            GameObject hlodHeader = this.CreateUguiHeaderLabel(scrollContent, "HlodHeader",
                this.L("LANDSCAPE (HLOD)"), 12f);
            PlaceUguiTopLeft(hlodHeader, pad, yCur, rowW, 18f);
            yCur += 24f;

            handle.HlodToggle = this.CreateUguiCheckbox(scrollContent, "HlodToggle",
                this.L("Extend landscape detail distance"), this.gameLodHlodEnabled,
                new System.Action<bool>(this.OnUguiGameLodHlodToggled));
            PlaceUguiTopLeft(handle.HlodToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 30f;

            handle.HlodShown = this.LF("Distance multiplier: x{0:0.0}", this.gameLodHlodMult);
            handle.HlodLabel = this.CreateUguiBodyLabel(scrollContent, "HlodLabel",
                handle.HlodShown, 13f);
            PlaceUguiTopLeft(handle.HlodLabel, pad, yCur + 2f, labelW, 20f);
            handle.HlodSlider = this.CreateUguiSlider(scrollContent, "HlodSlider",
                1f, 4f, this.gameLodHlodMult, false,
                new System.Action<float>(this.OnUguiGameLodHlodChanged));
            PlaceUguiTopLeft(handle.HlodSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 28f;

            GameObject hlodHint = this.CreateUguiLabel(scrollContent, "HlodHint",
                this.L("Distant low-poly scenery (rocks, islands, scene chunks) swaps to full-detail geometry much further away. Covers both HLOD proxies and the base-scene chunk streamer; applies live, no reload."),
                11f, hintColor, false);
            this.TrySetUguiLabelWrapped(hlodHint);
            PlaceUguiTopLeft(hlodHint, pad, yCur, rowW, 30f);
            yCur += 34f;

            handle.HlodStatusShown = this.BuildUguiGameLodStatusText(this.gameLodHlodStatus);
            handle.HlodStatusLabel = this.CreateUguiLabel(scrollContent, "HlodStatus",
                handle.HlodStatusShown, 11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.HlodStatusLabel);
            PlaceUguiTopLeft(handle.HlodStatusLabel, pad, yCur, rowW, 18f);
            yCur += 28f;

            // ---------------- PROPS (XDLOD) ----------------
            GameObject xdlodHeader = this.CreateUguiHeaderLabel(scrollContent, "XdLodHeader",
                this.L("PROPS (XDLOD)"), 12f);
            PlaceUguiTopLeft(xdlodHeader, pad, yCur, rowW, 18f);
            yCur += 24f;

            handle.XdLodToggle = this.CreateUguiCheckbox(scrollContent, "XdLodToggle",
                this.L("Force max prop mesh detail"), this.gameLodXdLodEnabled,
                new System.Action<bool>(this.OnUguiGameLodXdLodToggled));
            PlaceUguiTopLeft(handle.XdLodToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 28f;

            GameObject xdlodHint = this.CreateUguiLabel(scrollContent, "XdLodHint",
                this.L("Props with the game's custom mesh-swap LOD keep their high-poly mesh at any distance. May raise VRAM/CPU load."),
                11f, hintColor, false);
            this.TrySetUguiLabelWrapped(xdlodHint);
            PlaceUguiTopLeft(xdlodHint, pad, yCur, rowW, 30f);
            yCur += 34f;

            handle.XdLodStatusShown = this.BuildUguiGameLodStatusText(this.gameLodXdLodStatus);
            handle.XdLodStatusLabel = this.CreateUguiLabel(scrollContent, "XdLodStatus",
                handle.XdLodStatusShown, 11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.XdLodStatusLabel);
            PlaceUguiTopLeft(handle.XdLodStatusLabel, pad, yCur, rowW, 18f);
            yCur += 28f;

            // ---------------- CHARACTERS & NPCS ----------------
            GameObject sigHeader = this.CreateUguiHeaderLabel(scrollContent, "SigHeader",
                this.L("CHARACTERS & NPCS"), 12f);
            PlaceUguiTopLeft(sigHeader, pad, yCur, rowW, 18f);
            yCur += 24f;

            handle.SignificanceToggle = this.CreateUguiCheckbox(scrollContent, "SignificanceToggle",
                this.L("Full character detail at distance"), this.gameLodSignificanceOffEnabled,
                new System.Action<bool>(this.OnUguiGameLodSignificanceToggled));
            PlaceUguiTopLeft(handle.SignificanceToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 28f;

            GameObject sigHint = this.CreateUguiLabel(scrollContent, "SigHint",
                this.L("Disables the game's distance-based LOD/animation reduction for players, NPCs and pets (what photo mode does)."),
                11f, hintColor, false);
            this.TrySetUguiLabelWrapped(sigHint);
            PlaceUguiTopLeft(sigHint, pad, yCur, rowW, 30f);
            yCur += 34f;

            handle.SignificanceStatusShown = this.BuildUguiGameLodStatusText(this.gameLodSignificanceStatus);
            handle.SignificanceStatusLabel = this.CreateUguiLabel(scrollContent, "SigStatus",
                handle.SignificanceStatusShown, 11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.SignificanceStatusLabel);
            PlaceUguiTopLeft(handle.SignificanceStatusLabel, pad, yCur, rowW, 18f);
            yCur += 28f;

            // ---------------- LIVE ENTITIES ----------------
            GameObject nineHeader = this.CreateUguiHeaderLabel(scrollContent, "NineHeader",
                this.L("LIVE ENTITIES"), 12f);
            PlaceUguiTopLeft(nineHeader, pad, yCur, rowW, 18f);
            yCur += 24f;

            handle.NineCellToggle = this.CreateUguiCheckbox(scrollContent, "NineCellToggle",
                this.L("Extend entity visibility range"), this.gameLodNineCellEnabled,
                new System.Action<bool>(this.OnUguiGameLodNineCellToggled));
            PlaceUguiTopLeft(handle.NineCellToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 30f;

            handle.NineCellShown = this.LF("Range multiplier: x{0:0.0}", this.gameLodNineCellMult);
            handle.NineCellLabel = this.CreateUguiBodyLabel(scrollContent, "NineCellLabel",
                handle.NineCellShown, 13f);
            PlaceUguiTopLeft(handle.NineCellLabel, pad, yCur + 2f, labelW, 20f);
            handle.NineCellSlider = this.CreateUguiSlider(scrollContent, "NineCellSlider",
                1f, 5f, this.gameLodNineCellMult, false,
                new System.Action<float>(this.OnUguiGameLodNineCellChanged));
            PlaceUguiTopLeft(handle.NineCellSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 28f;

            GameObject nineHint = this.CreateUguiLabel(scrollContent, "NineHint",
                this.L("Players, NPCs, animals, collectables stay visible further away. Capped by the server's sync range — some entities cannot appear beyond it."),
                11f, hintColor, false);
            this.TrySetUguiLabelWrapped(nineHint);
            PlaceUguiTopLeft(nineHint, pad, yCur, rowW, 30f);
            yCur += 34f;

            handle.NineCellStatusShown = this.BuildUguiGameLodStatusText(this.gameLodNineCellStatus);
            handle.NineCellStatusLabel = this.CreateUguiLabel(scrollContent, "NineStatus",
                handle.NineCellStatusShown, 11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.NineCellStatusLabel);
            PlaceUguiTopLeft(handle.NineCellStatusLabel, pad, yCur, rowW, 18f);
            yCur += 28f;

            // ---------------- SHADOWS ----------------
            GameObject shadowHeader = this.CreateUguiHeaderLabel(scrollContent, "ShadowHeader",
                this.L("SHADOWS"), 12f);
            PlaceUguiTopLeft(shadowHeader, pad, yCur, rowW, 18f);
            yCur += 24f;

            handle.ShadowToggle = this.CreateUguiCheckbox(scrollContent, "ShadowToggle",
                this.L("Custom shadow distance"), this.gameLodShadowEnabled,
                new System.Action<bool>(this.OnUguiGameLodShadowToggled));
            PlaceUguiTopLeft(handle.ShadowToggle.gameObject, pad, yCur, rowW, 24f);
            yCur += 30f;

            handle.ShadowShown = this.LF("Shadow distance: {0:F0} m", this.gameLodShadowDistance);
            handle.ShadowLabel = this.CreateUguiBodyLabel(scrollContent, "ShadowLabel",
                handle.ShadowShown, 13f);
            PlaceUguiTopLeft(handle.ShadowLabel, pad, yCur + 2f, labelW, 20f);
            handle.ShadowSlider = this.CreateUguiSlider(scrollContent, "ShadowSlider",
                50f, 800f, this.gameLodShadowDistance, true,
                new System.Action<float>(this.OnUguiGameLodShadowChanged));
            PlaceUguiTopLeft(handle.ShadowSlider.gameObject, sliderX, yCur + 3f, sliderW, 20f);
            yCur += 26f;

            handle.ShadowStatusShown = this.BuildUguiGameLodStatusText(this.gameLodShadowStatus);
            handle.ShadowStatusLabel = this.CreateUguiLabel(scrollContent, "ShadowStatus",
                handle.ShadowStatusShown, 11f, hintColor, false);
            this.TrySetUguiLabelWrapped(handle.ShadowStatusLabel);
            PlaceUguiTopLeft(handle.ShadowStatusLabel, pad, yCur, rowW, 18f);
            yCur += 26f;

            // ---------------- DIAGNOSTICS ----------------
            GameObject diagHeader = this.CreateUguiHeaderLabel(scrollContent, "DiagHeader",
                this.L("DIAGNOSTICS"), 12f);
            PlaceUguiTopLeft(diagHeader, pad, yCur, rowW, 18f);
            yCur += 24f;

            GameObject dumpBtn = this.CreateUguiSecondaryButton(scrollContent, "DumpLodButton",
                this.L("Dump nearby LOD objects to log"), new System.Action(this.OnUguiGameLodDumpClicked));
            PlaceUguiTopLeft(dumpBtn, pad, yCur, 240f, 24f);
            yCur += 30f;

            GameObject diagHint = this.CreateUguiLabel(scrollContent, "DiagHint",
                this.L("Stand next to an object that still swaps detail, press the button, and check the log: it lists every Unity LODGroup within 250 m plus HLOD/XDLod/NineCell counts, so the owning system can be identified."),
                11f, hintColor, false);
            this.TrySetUguiLabelWrapped(diagHint);
            PlaceUguiTopLeft(diagHint, pad, yCur, rowW, 44f);
            yCur += 50f;

            GameObject footerHint = this.CreateUguiLabel(scrollContent, "FooterHint",
                this.L("Unity LODGroup bias lives in Settings → Main → Performance (LOD Override). All overrides here revert to game defaults when switched off."),
                11f, hintColor, false);
            this.TrySetUguiLabelWrapped(footerHint);
            PlaceUguiTopLeft(footerHint, pad, yCur, rowW, 30f);
            yCur += 38f;

            this.SetUguiScrollContentHeight(scrollContent, yCur);

            handle.ScrollContent = scrollContent;
            handle.Root = block;
            this.uguiShellSelfGameLod = handle;
            return block;
        }

        private string BuildUguiGameLodStatusText(string status)
        {
            return string.IsNullOrEmpty(status) ? this.L("Status: idle") : this.LF("Status: {0}", status);
        }

        private string BuildUguiUgcCachePurgeStatusText()
        {
            return string.IsNullOrWhiteSpace(this.ugcCachePurgeStatus) ? "Idle." : this.ugcCachePurgeStatus;
        }

        private string BuildUguiUgcCacheApplyStatusText()
        {
            return string.IsNullOrWhiteSpace(this.ugcCacheApplyStatus) ? "Idle." : this.ugcCacheApplyStatus;
        }

        private string BuildUguiUgcCacheCapacityText()
        {
            return this.LF("Cache size: {0} items", Mathf.Clamp(this.ugcCacheTargetCapacity, UgcCacheMinCapacity, UgcCacheMaxCapacity));
        }

        private float GameLodVegetationSliderValue()
        {
            return Mathf.Clamp(this.gameLodVegetationTargetPref, 10, 1000);
        }

        // Absolute target + the game's own value for reference (multiplier framing was removed —
        // it kept adopting the mod's own output as the baseline and silently collapsing to ×1).
        private string BuildUguiGameLodVegetationText()
        {
            return this.LF("Detail value: {0} (game: {1})",
                this.GameLodEffectiveVegetationPrefForUi(),
                this.GameLodVegetationOriginalPref());
        }

        private void ProcessUguiShellSelfGameLodOnUpdate()
        {
            UguiShellSelfGameLodHandle handle = this.uguiShellSelfGameLod;
            if (handle == null || handle.Root == null || handle.ErrorCount >= 3
                || !this.IsUguiShellSelfSubTabActive(UguiShellSelfGameLodSubIndex))
            {
                return;
            }

            try
            {
                this.SyncUguiToggleFromField(handle.FurnitureToggle, this.gameLodFurnitureEnabled);
                this.SyncUguiToggleFromField(handle.ForceLod0Toggle, this.gameLodForceLod0Enabled);
                this.SyncUguiToggleFromField(handle.BrgBiasToggle, this.gameLodBrgBiasEnabled);
                this.SyncUguiToggleFromField(handle.VegetationToggle, this.gameLodVegetationEnabled);
                this.SyncUguiToggleFromField(handle.VegetationDuringLoadToggle, this.gameLodVegetationApplyDuringLoad);
                this.SyncUguiToggleFromField(handle.HlodToggle, this.gameLodHlodEnabled);
                this.SyncUguiToggleFromField(handle.XdLodToggle, this.gameLodXdLodEnabled);
                this.SyncUguiToggleFromField(handle.SignificanceToggle, this.gameLodSignificanceOffEnabled);
                this.SyncUguiToggleFromField(handle.NineCellToggle, this.gameLodNineCellEnabled);
                this.SyncUguiToggleFromField(handle.ShadowToggle, this.gameLodShadowEnabled);

                if (handle.FurnitureMaxSlider != null
                    && Mathf.Abs(handle.FurnitureMaxSlider.value - this.gameLodFurnitureMaxObjects) > 0.5f)
                {
                    handle.FurnitureMaxSlider.SetValueWithoutNotify(this.gameLodFurnitureMaxObjects);
                }
                this.SyncUguiSelfLabelText(handle.FurnitureMaxLabel, ref handle.FurnitureMaxShown,
                    this.LF("Max objects: {0}", this.gameLodFurnitureMaxObjects));

                if (handle.FurnitureDistSlider != null
                    && Mathf.Abs(handle.FurnitureDistSlider.value - this.gameLodFurnitureDistance) > 0.5f)
                {
                    handle.FurnitureDistSlider.SetValueWithoutNotify(this.gameLodFurnitureDistance);
                }
                this.SyncUguiSelfLabelText(handle.FurnitureDistLabel, ref handle.FurnitureDistShown,
                    this.LF("Draw distance: {0} m", this.gameLodFurnitureDistance));

                if (handle.FurnitureMeshSlider != null
                    && Mathf.Abs(handle.FurnitureMeshSlider.value - this.gameLodFurnitureMeshDistance) > 0.5f)
                {
                    handle.FurnitureMeshSlider.SetValueWithoutNotify(this.gameLodFurnitureMeshDistance);
                }
                this.SyncUguiSelfLabelText(handle.FurnitureMeshLabel, ref handle.FurnitureMeshShown,
                    this.LF("Mesh detail distance: {0} m", this.gameLodFurnitureMeshDistance));

                // UGC Texture Cache — independent of the busy/toggle gating above, its own
                // coroutine + own toggle (Pictures sub-tab precedent, moved here 2026-07-26).
                this.SetUguiButtonInteractable(handle.CachePurgeButton, !this.IsUgcCachePurgeBusy());
                this.SyncUguiToggleFromField(handle.CacheRaiseLimitToggle, this.ugcCacheRaiseLimitEnabled);
                if (handle.CacheCapacitySlider != null
                    && Mathf.Abs(handle.CacheCapacitySlider.value - this.ugcCacheTargetCapacity) > 0.5f)
                {
                    handle.CacheCapacitySlider.SetValueWithoutNotify(this.ugcCacheTargetCapacity);
                }
                this.SyncUguiSelfLabelText(handle.CacheCapacityLabel, ref handle.CacheCapacityShown,
                    this.BuildUguiUgcCacheCapacityText());
                // Purge status updates progressively mid-coroutine — every-frame diff (cheap:
                // alloc-free until the text actually changes).
                this.SyncUguiSelfLabelText(handle.CachePurgeStatusLabel, ref handle.CachePurgeStatusShown,
                    this.BuildUguiUgcCachePurgeStatusText());

                if (handle.BrgBiasSlider != null
                    && Mathf.Abs(handle.BrgBiasSlider.value - this.gameLodBrgBias) > 0.005f)
                {
                    handle.BrgBiasSlider.SetValueWithoutNotify(this.gameLodBrgBias);
                }
                this.SyncUguiSelfLabelText(handle.BrgBiasLabel, ref handle.BrgBiasShown,
                    this.LF("LOD bias: x{0:0.0}", this.gameLodBrgBias));

                if (handle.VegetationSlider != null
                    && Mathf.Abs(handle.VegetationSlider.value - this.GameLodVegetationSliderValue()) > 0.5f)
                {
                    handle.VegetationSlider.SetValueWithoutNotify(this.GameLodVegetationSliderValue());
                }
                this.SyncUguiSelfLabelText(handle.VegetationLabel, ref handle.VegetationShown,
                    this.BuildUguiGameLodVegetationText());

                if (handle.HlodSlider != null
                    && Mathf.Abs(handle.HlodSlider.value - this.gameLodHlodMult) > 0.005f)
                {
                    handle.HlodSlider.SetValueWithoutNotify(this.gameLodHlodMult);
                }
                this.SyncUguiSelfLabelText(handle.HlodLabel, ref handle.HlodShown,
                    this.LF("Distance multiplier: x{0:0.0}", this.gameLodHlodMult));

                if (handle.NineCellSlider != null
                    && Mathf.Abs(handle.NineCellSlider.value - this.gameLodNineCellMult) > 0.005f)
                {
                    handle.NineCellSlider.SetValueWithoutNotify(this.gameLodNineCellMult);
                }
                this.SyncUguiSelfLabelText(handle.NineCellLabel, ref handle.NineCellShown,
                    this.LF("Range multiplier: x{0:0.0}", this.gameLodNineCellMult));

                if (handle.ShadowSlider != null
                    && Mathf.Abs(handle.ShadowSlider.value - this.gameLodShadowDistance) > 0.5f)
                {
                    handle.ShadowSlider.SetValueWithoutNotify(this.gameLodShadowDistance);
                }
                this.SyncUguiSelfLabelText(handle.ShadowLabel, ref handle.ShadowShown,
                    this.LF("Shadow distance: {0:F0} m", this.gameLodShadowDistance));

                // Live status lines change from the feature's background apply loop — 0.5s tick.
                if (Time.unscaledTime >= handle.NextSlowSyncAt)
                {
                    handle.NextSlowSyncAt = Time.unscaledTime + 0.5f;
                    this.SyncUguiSelfLabelText(handle.FurnitureStatusLabel, ref handle.FurnitureStatusShown,
                        this.BuildUguiGameLodStatusText(this.gameLodFurnitureStatus));
                    this.SyncUguiSelfLabelText(handle.CacheApplyStatusLabel, ref handle.CacheApplyStatusShown,
                        this.BuildUguiUgcCacheApplyStatusText());
                    this.SyncUguiSelfLabelText(handle.BrgStatusLabel, ref handle.BrgStatusShown,
                        this.BuildUguiGameLodStatusText(this.gameLodBrgStatus));
                    this.SyncUguiSelfLabelText(handle.VegetationStatusLabel, ref handle.VegetationStatusShown,
                        this.BuildUguiGameLodStatusText(this.gameLodVegetationStatus));
                    this.SyncUguiSelfLabelText(handle.HlodStatusLabel, ref handle.HlodStatusShown,
                        this.BuildUguiGameLodStatusText(this.gameLodHlodStatus));
                    this.SyncUguiSelfLabelText(handle.XdLodStatusLabel, ref handle.XdLodStatusShown,
                        this.BuildUguiGameLodStatusText(this.gameLodXdLodStatus));
                    this.SyncUguiSelfLabelText(handle.SignificanceStatusLabel, ref handle.SignificanceStatusShown,
                        this.BuildUguiGameLodStatusText(this.gameLodSignificanceStatus));
                    this.SyncUguiSelfLabelText(handle.NineCellStatusLabel, ref handle.NineCellStatusShown,
                        this.BuildUguiGameLodStatusText(this.gameLodNineCellStatus));
                    this.SyncUguiSelfLabelText(handle.ShadowStatusLabel, ref handle.ShadowStatusShown,
                        this.BuildUguiGameLodStatusText(this.gameLodShadowStatus));
                }
            }
            catch (Exception ex)
            {
                handle.ErrorCount++;
                ModLogger.Msg("[UguiShell] Self Game LOD content sync error (" + handle.ErrorCount
                    + "/3, disabled at 3): " + ex.Message);
            }
        }

        // --- change handlers (notify + save; applies/reverts run in GameLodFeature's tick) -------

        private void OnUguiGameLodFurnitureToggled(bool value)
        {
            if (value == this.gameLodFurnitureEnabled)
            {
                return;
            }
            this.SetGameLodFurnitureEnabled(value);
            this.AddMenuNotification(value ? this.L("Furniture draw distance extended") : this.L("Furniture draw distance reverting"),
                new Color(0.55f, 1f, 0.65f));
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodFurnitureMaxChanged(float value)
        {
            int rounded = Mathf.Clamp(Mathf.RoundToInt(value / 10f) * 10, 60, 5000);
            if (rounded == this.gameLodFurnitureMaxObjects)
            {
                return;
            }
            this.gameLodFurnitureMaxObjects = rounded;
            this.nextGameLodApplyAt = 0f;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodFurnitureDistChanged(float value)
        {
            int rounded = Mathf.Clamp(Mathf.RoundToInt(value / 50f) * 50, 100, 9999);
            if (Mathf.RoundToInt(value) >= 9950)
            {
                rounded = 9999;
            }
            if (rounded == this.gameLodFurnitureDistance)
            {
                return;
            }
            this.gameLodFurnitureDistance = rounded;
            this.nextGameLodApplyAt = 0f;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodFurnitureMeshChanged(float value)
        {
            int rounded = Mathf.Clamp(Mathf.RoundToInt(value / 50f) * 50, 100, 2000);
            if (rounded == this.gameLodFurnitureMeshDistance)
            {
                return;
            }
            this.gameLodFurnitureMeshDistance = rounded;
            this.nextGameLodApplyAt = 0f;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiUgcCachePurgeClicked()
        {
            this.StartUgcCachePurge();
        }

        private void OnUguiUgcCacheRaiseLimitToggled(bool value)
        {
            if (value == this.ugcCacheRaiseLimitEnabled)
            {
                return;
            }
            this.ugcCacheRaiseLimitEnabled = value;
            this.nextUgcCacheApplyAt = 0f;
            this.AddMenuNotification(value ? this.L("UGC texture cache limit raised") : this.L("UGC texture cache limit reverting"),
                new Color(0.55f, 1f, 0.65f));
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiUgcCacheCapacityChanged(float value)
        {
            int rounded = Mathf.Clamp(Mathf.RoundToInt(value / 50f) * 50, UgcCacheMinCapacity, UgcCacheMaxCapacity);
            if (rounded == this.ugcCacheTargetCapacity)
            {
                return;
            }
            this.ugcCacheTargetCapacity = rounded;
            if (this.ugcCacheRaiseLimitEnabled)
            {
                this.nextUgcCacheApplyAt = 0f;
            }
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodForceLod0Toggled(bool value)
        {
            if (value == this.gameLodForceLod0Enabled)
            {
                return;
            }
            this.SetGameLodForceLod0Enabled(value);
            this.AddMenuNotification(value ? this.L("Max mesh detail on") : this.L("Max mesh detail off"),
                new Color(0.55f, 1f, 0.65f));
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodBrgBiasToggled(bool value)
        {
            if (value == this.gameLodBrgBiasEnabled)
            {
                return;
            }
            this.SetGameLodBrgBiasEnabled(value);
            this.AddMenuNotification(value ? this.L("Furniture LOD bias boost on") : this.L("Furniture LOD bias boost off"),
                new Color(0.55f, 1f, 0.65f));
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodBrgBiasChanged(float value)
        {
            float rounded = Mathf.Clamp(Mathf.Round(value * 10f) / 10f, 1f, 4f);
            if (Mathf.Abs(rounded - this.gameLodBrgBias) <= 0.0001f)
            {
                return;
            }
            this.gameLodBrgBias = rounded;
            this.nextGameLodApplyAt = 0f;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodVegetationToggled(bool value)
        {
            if (value == this.gameLodVegetationEnabled)
            {
                return;
            }
            this.SetGameLodVegetationEnabled(value);
            this.AddMenuNotification(value ? this.L("Tree/house LOD distance extended") : this.L("Tree/house LOD distance reverting"),
                new Color(0.55f, 1f, 0.65f));
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodVegetationChanged(float value)
        {
            int rounded = Mathf.Clamp(Mathf.RoundToInt(value / 10f) * 10, 10, 1000);
            if (rounded == this.gameLodVegetationTargetPref)
            {
                return;
            }
            this.gameLodVegetationTargetPref = rounded;
            if (this.gameLodVegetationEnabled)
            {
                // Cheap registry write now; the heavy rebake stays behind the button / world load.
                this.GameLodWriteVegetationPref();
                this.gameLodVegetationStatus = this.L("Value saved — press 'Rebake now' or reload the world.");
            }
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodVegetationRebakeClicked()
        {
            this.RequestGameLodVegetationRebake();
            this.AddMenuNotification(this.L("Rebaking vegetation instancing…"), new Color(0.45f, 0.85f, 1f));
        }

        private void OnUguiGameLodVegetationDuringLoadToggled(bool value)
        {
            if (value == this.gameLodVegetationApplyDuringLoad)
            {
                return;
            }
            this.gameLodVegetationApplyDuringLoad = value;
            this.AddMenuNotification(
                value ? this.L("Terrain detail also applied during load (slower loading)")
                      : this.L("Loading stays fast (terrain near spawn stays coarse)"),
                new Color(0.45f, 0.85f, 1f));
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodDumpClicked()
        {
            this.DumpGameLodNearbyLodObjects();
            this.AddMenuNotification(this.L("Nearby LOD objects dumped to log."), new Color(0.45f, 0.85f, 1f));
        }

        private void OnUguiGameLodHlodToggled(bool value)
        {
            if (value == this.gameLodHlodEnabled)
            {
                return;
            }
            this.SetGameLodHlodEnabled(value);
            this.AddMenuNotification(value ? this.L("Landscape detail distance extended") : this.L("Landscape detail distance reverting"),
                new Color(0.55f, 1f, 0.65f));
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodHlodChanged(float value)
        {
            float rounded = Mathf.Clamp(Mathf.Round(value * 10f) / 10f, 1f, 4f);
            if (Mathf.Abs(rounded - this.gameLodHlodMult) <= 0.0001f)
            {
                return;
            }
            this.gameLodHlodMult = rounded;
            this.nextGameLodApplyAt = 0f;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodXdLodToggled(bool value)
        {
            if (value == this.gameLodXdLodEnabled)
            {
                return;
            }
            this.SetGameLodXdLodEnabled(value);
            this.AddMenuNotification(value ? this.L("Max prop mesh detail on") : this.L("Max prop mesh detail off"),
                new Color(0.55f, 1f, 0.65f));
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodSignificanceToggled(bool value)
        {
            if (value == this.gameLodSignificanceOffEnabled)
            {
                return;
            }
            this.SetGameLodSignificanceOffEnabled(value);
            this.AddMenuNotification(value ? this.L("Full character detail on") : this.L("Full character detail off"),
                new Color(0.55f, 1f, 0.65f));
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodNineCellToggled(bool value)
        {
            if (value == this.gameLodNineCellEnabled)
            {
                return;
            }
            this.SetGameLodNineCellEnabled(value);
            this.AddMenuNotification(value ? this.L("Entity visibility range extended") : this.L("Entity visibility range reverting"),
                new Color(0.55f, 1f, 0.65f));
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodNineCellChanged(float value)
        {
            float rounded = Mathf.Clamp(Mathf.Round(value * 10f) / 10f, 1f, 5f);
            if (Mathf.Abs(rounded - this.gameLodNineCellMult) <= 0.0001f)
            {
                return;
            }
            this.gameLodNineCellMult = rounded;
            this.nextGameLodNineCellWalkAt = 0f;
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodShadowToggled(bool value)
        {
            if (value == this.gameLodShadowEnabled)
            {
                return;
            }
            this.SetGameLodShadowEnabled(value);
            this.AddMenuNotification(value ? this.L("Custom shadow distance on") : this.L("Custom shadow distance off"),
                new Color(0.55f, 1f, 0.65f));
            try { this.SaveKeybinds(false); } catch { }
        }

        private void OnUguiGameLodShadowChanged(float value)
        {
            float rounded = Mathf.Clamp(Mathf.Round(value / 10f) * 10f, 50f, 800f);
            if (Mathf.Abs(rounded - this.gameLodShadowDistance) <= 0.0001f)
            {
                return;
            }
            this.gameLodShadowDistance = rounded;
            this.nextGameLodApplyAt = 0f;
            try { this.SaveKeybinds(false); } catch { }
        }
    }
}
