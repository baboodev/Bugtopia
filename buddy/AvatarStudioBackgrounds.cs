using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // AVATAR STUDIO — BACKGROUNDS for the Generate Avatar panel.
    //
    // The panel's backdrop is a 3D plane, not UI: `background_plane@frame` holds ten MeshRenderers
    // (backgroundimage01..10, Unlit/Texture, materials m_avatar_scenebg01..10) and the panel's own
    // button just switches which one is active. The capture is a screen grab, so whatever material
    // sits on the visible plane ends up in the avatar.
    //
    // Three sources, all verified live on 2026-09-30:
    //   panel   the ten planes' own materials, pickable directly instead of cycling;
    //   extra   15 more scene backgrounds the game ships for other screens (mini battle pass,
    //           research, gacha, pay shop) — all 512x256 Unlit/Texture or Unlit/Color, loaded by
    //           the IL2CPP-side ResManager.LoadObjectSync("ui/material/<name>") through the interop
    //           (the same class HeartopiaComplete.GameIcons.cs loads icons with — no AuraMono).
    //           The two private-island materials are Skybox/Cubemap and do not work on a plane;
    //   file    any PNG/JPG in %LocalLow%/Bugtopia/AvatarBackgrounds, on a copy of the panel's own
    //           material, cropped to the 2:1 the game's backgrounds use.
    //
    // A choice wins over the panel's button: if the game switches the active plane, the override
    // moves with it. "Game" hands control back. Every plane gets its original material back when
    // the panel closes — the panel object is POOLED (see AvatarStudioFeature.cs), so anything left
    // on it would survive into the next open.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const string AvatarStudioPlaneParentName = "background_plane@frame";
        private const string AvatarStudioBackgroundFolder = "AvatarBackgrounds";
        private const string AvatarStudioMaterialPrefix = "ui/material/";
        private const float AvatarStudioBackgroundScanIntervalSec = 2f;
        private const float AvatarStudioBackgroundAspect = 2f;          // 512x256, like the game's own
        private const long AvatarStudioBackgroundMaxFileBytes = 32L * 1024L * 1024L;

        // Material name -> label. Verified to load and render on the panel's plane.
        private static readonly string[,] AvatarStudioExtraBackgrounds = new string[,]
        {
            { "m_avatar_scenebg_minibp", "Mini pass" },
            { "m_avatar_scenebg_minibp_dream", "Mini pass: Dream" },
            { "m_avatar_scenebg_minibp_foison", "Mini pass: Foison" },
            { "m_avatar_scenebg_minibp_kindergarten", "Mini pass: Kindergarten" },
            { "m_avatar_scenebg_minibp_sauna", "Mini pass: Sauna" },
            { "m_avatar_scenebg_minibp_starriver", "Mini pass: Star River" },
            { "m_avatar_scenebg_minibp_street", "Mini pass: Street" },
            { "m_avatar_scenebg_minibp_tribe", "Mini pass: Tribe" },
            { "m_avatar_scenebg_research", "Research" },
            { "m_avatar_scenebg_research_white", "Research: White" },
            { "m_scenebg_gacha_1001", "Gacha 1001" },
            { "m_scenebg_gacha_1002", "Gacha 1002" },
            { "m_scenebg_gacha_1003", "Gacha 1003" },
            { "m_scenebg_payshop_1", "Pay shop 1" },
            { "m_scenebg_payshop_2", "Pay shop 2" },
        };

        internal enum AvatarStudioBackgroundKind
        {
            Game,
            Panel,
            Extra,
            File
        }

        internal sealed class AvatarStudioBackground
        {
            public AvatarStudioBackgroundKind Kind;
            public int PlaneIndex;
            public string Name;      // material name (Extra) or full path (File)
            public string Label;
        }

        private readonly List<AvatarStudioBackground> avatarStudioBackgrounds = new List<AvatarStudioBackground>();
        private readonly List<MeshRenderer> avatarStudioPlanes = new List<MeshRenderer>();
        private readonly List<Material> avatarStudioPlaneOriginals = new List<Material>();
        private readonly Dictionary<string, Material> avatarStudioExtraMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);

        private int avatarStudioBackgroundSelected;
        private int avatarStudioBackgroundListVersion;
        private string avatarStudioBackgroundFilesSignature = string.Empty;
        private float avatarStudioBackgroundNextScanAt;

        private Material avatarStudioBackgroundOverride;
        private MeshRenderer avatarStudioOverridePlane;
        private Material avatarStudioCustomMaterial;
        private Texture2D avatarStudioCustomTexture;

        private MethodInfo avatarStudioResLoadObjectSync;
        private MethodInfo avatarStudioResUnLoadSync;
        private float avatarStudioResRetryAt;

        internal List<AvatarStudioBackground> AvatarStudioBackgrounds
        {
            get { return this.avatarStudioBackgrounds; }
        }

        internal int AvatarStudioBackgroundSelected
        {
            get { return this.avatarStudioBackgroundSelected; }
        }

        internal int AvatarStudioBackgroundListVersion
        {
            get { return this.avatarStudioBackgroundListVersion; }
        }

        // ── lifecycle (called from AvatarStudioFeature open/close) ──────────────────────────────

        private void CaptureAvatarStudioPlanes(GameObject root)
        {
            this.avatarStudioPlanes.Clear();
            this.avatarStudioPlaneOriginals.Clear();
            this.avatarStudioBackgroundOverride = null;
            this.avatarStudioOverridePlane = null;
            this.avatarStudioBackgroundSelected = 0;

            try
            {
                List<MeshRenderer> found = new List<MeshRenderer>();
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.parent != null && t.parent.name == AvatarStudioPlaneParentName)
                    {
                        MeshRenderer r = t.GetComponent<MeshRenderer>();
                        if (r != null)
                        {
                            found.Add(r);
                        }
                    }
                }
                found.Sort((a, b) => string.CompareOrdinal(a.gameObject.name, b.gameObject.name));
                foreach (MeshRenderer r in found)
                {
                    this.avatarStudioPlanes.Add(r);
                    this.avatarStudioPlaneOriginals.Add(r.sharedMaterial);
                }
            }
            catch (Exception ex)
            {
                this.avatarStudioPlanes.Clear();
                this.avatarStudioPlaneOriginals.Clear();
                FeatureLog.Fail(AvatarStudioTag, "background planes lookup threw: " + ex.Message);
            }

            this.avatarStudioBackgroundFilesSignature = null; // force a folder scan
            this.avatarStudioBackgroundNextScanAt = 0f;
            this.RebuildAvatarStudioBackgroundList();
            if (this.avatarStudioPlanes.Count == 0)
            {
                FeatureLog.Fail(AvatarStudioTag, AvatarStudioPlaneParentName + " has no planes — background picker disabled");
            }
        }

        private void ReleaseAvatarStudioBackgrounds()
        {
            try
            {
                for (int i = 0; i < this.avatarStudioPlanes.Count; i++)
                {
                    MeshRenderer r = this.avatarStudioPlanes[i];
                    Material original = i < this.avatarStudioPlaneOriginals.Count ? this.avatarStudioPlaneOriginals[i] : null;
                    if (r != null && original != null && r.sharedMaterial != original)
                    {
                        r.sharedMaterial = original;
                    }
                }
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(AvatarStudioTag, "background restore threw: " + ex.Message);
            }

            this.DestroyAvatarStudioCustomBackground();

            // Hand the extra materials back to the game's refcounting.
            foreach (KeyValuePair<string, Material> kv in this.avatarStudioExtraMaterials)
            {
                try
                {
                    if (kv.Value != null && this.avatarStudioResUnLoadSync != null)
                    {
                        this.avatarStudioResUnLoadSync.Invoke(null, new object[] { kv.Value });
                    }
                }
                catch (Exception ex)
                {
                    FeatureLog.Fail(AvatarStudioTag, "background unload threw for " + kv.Key + ": " + ex.Message);
                }
            }
            this.avatarStudioExtraMaterials.Clear();

            this.avatarStudioPlanes.Clear();
            this.avatarStudioPlaneOriginals.Clear();
            this.avatarStudioBackgroundOverride = null;
            this.avatarStudioOverridePlane = null;
            this.avatarStudioBackgroundSelected = 0;
        }

        private void DestroyAvatarStudioCustomBackground()
        {
            try
            {
                if (this.avatarStudioCustomMaterial != null)
                {
                    Object.Destroy(this.avatarStudioCustomMaterial);
                }
                if (this.avatarStudioCustomTexture != null)
                {
                    Object.Destroy(this.avatarStudioCustomTexture);
                }
            }
            catch { }
            this.avatarStudioCustomMaterial = null;
            this.avatarStudioCustomTexture = null;
        }

        // ── the list ────────────────────────────────────────────────────────────────────────────

        internal static string AvatarStudioBackgroundDirectory
        {
            get { return HelperPaths.GetDirectory(AvatarStudioBackgroundFolder); }
        }

        private void RebuildAvatarStudioBackgroundList()
        {
            AvatarStudioBackground keep = this.avatarStudioBackgroundSelected > 0
                && this.avatarStudioBackgroundSelected < this.avatarStudioBackgrounds.Count
                ? this.avatarStudioBackgrounds[this.avatarStudioBackgroundSelected]
                : null;

            this.avatarStudioBackgrounds.Clear();
            this.avatarStudioBackgrounds.Add(new AvatarStudioBackground
            {
                Kind = AvatarStudioBackgroundKind.Game,
                Label = this.L("Game (panel button)"),
            });
            for (int i = 0; i < this.avatarStudioPlanes.Count; i++)
            {
                this.avatarStudioBackgrounds.Add(new AvatarStudioBackground
                {
                    Kind = AvatarStudioBackgroundKind.Panel,
                    PlaneIndex = i,
                    Label = this.L("Panel") + " " + (i + 1),
                });
            }
            for (int i = 0; i < AvatarStudioExtraBackgrounds.GetLength(0); i++)
            {
                this.avatarStudioBackgrounds.Add(new AvatarStudioBackground
                {
                    Kind = AvatarStudioBackgroundKind.Extra,
                    Name = AvatarStudioExtraBackgrounds[i, 0],
                    Label = AvatarStudioExtraBackgrounds[i, 1],
                });
            }
            foreach (string file in this.ListAvatarStudioBackgroundFiles(out string _))
            {
                this.avatarStudioBackgrounds.Add(new AvatarStudioBackground
                {
                    Kind = AvatarStudioBackgroundKind.File,
                    Name = file,
                    Label = this.L("File:") + " " + Path.GetFileName(file),
                });
            }

            // Keep the current pick selected if it still exists (a rescan must not jump the list).
            this.avatarStudioBackgroundSelected = 0;
            if (keep != null)
            {
                for (int i = 1; i < this.avatarStudioBackgrounds.Count; i++)
                {
                    AvatarStudioBackground b = this.avatarStudioBackgrounds[i];
                    if (b.Kind == keep.Kind && b.PlaneIndex == keep.PlaneIndex
                        && string.Equals(b.Name, keep.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        this.avatarStudioBackgroundSelected = i;
                        break;
                    }
                }

                // The picked file was removed from the folder: its background goes with it.
                if (this.avatarStudioBackgroundSelected == 0)
                {
                    if (this.avatarStudioOverridePlane != null)
                    {
                        this.RestoreAvatarStudioPlane(this.avatarStudioOverridePlane);
                    }
                    this.avatarStudioOverridePlane = null;
                    this.avatarStudioBackgroundOverride = null;
                    this.DestroyAvatarStudioCustomBackground();
                }
            }
            this.avatarStudioBackgroundListVersion++;
        }

        private List<string> ListAvatarStudioBackgroundFiles(out string signature)
        {
            List<string> files = new List<string>();
            signature = string.Empty;
            try
            {
                string dir = AvatarStudioBackgroundDirectory;
                foreach (string f in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    {
                        files.Add(f);
                    }
                }
                files.Sort(StringComparer.OrdinalIgnoreCase);
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (string f in files)
                {
                    FileInfo fi = new FileInfo(f);
                    sb.Append(fi.Name).Append('|').Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append(';');
                }
                signature = sb.ToString();
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(AvatarStudioTag, "background folder scan threw: " + ex.Message);
            }
            return files;
        }

        // ── per-frame (from the window driver, only while the window shows) ─────────────────────

        internal void TickAvatarStudioBackground()
        {
            float now = Time.unscaledTime;
            if (now >= this.avatarStudioBackgroundNextScanAt)
            {
                this.avatarStudioBackgroundNextScanAt = now + AvatarStudioBackgroundScanIntervalSec;
                this.ListAvatarStudioBackgroundFiles(out string signature);
                if (!string.Equals(signature, this.avatarStudioBackgroundFilesSignature, StringComparison.Ordinal))
                {
                    bool first = this.avatarStudioBackgroundFilesSignature == null;
                    this.avatarStudioBackgroundFilesSignature = signature;
                    if (!first)
                    {
                        this.RebuildAvatarStudioBackgroundList();
                    }
                }
            }

            if (this.avatarStudioBackgroundOverride != null)
            {
                this.KeepAvatarStudioBackgroundOnActivePlane();
            }
        }

        private MeshRenderer ActiveAvatarStudioPlane()
        {
            for (int i = 0; i < this.avatarStudioPlanes.Count; i++)
            {
                MeshRenderer r = this.avatarStudioPlanes[i];
                if (r != null && r.gameObject.activeSelf)
                {
                    return r;
                }
            }
            return null;
        }

        // The panel's button activates a different plane; the override follows it.
        private void KeepAvatarStudioBackgroundOnActivePlane()
        {
            try
            {
                MeshRenderer active = this.ActiveAvatarStudioPlane();
                if (active == null)
                {
                    return;
                }
                if (this.avatarStudioOverridePlane != null && this.avatarStudioOverridePlane != active)
                {
                    this.RestoreAvatarStudioPlane(this.avatarStudioOverridePlane);
                }
                this.avatarStudioOverridePlane = active;
                if (active.sharedMaterial != this.avatarStudioBackgroundOverride)
                {
                    active.sharedMaterial = this.avatarStudioBackgroundOverride;
                }
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(AvatarStudioTag, "background keep threw: " + ex.Message);
            }
        }

        private void RestoreAvatarStudioPlane(MeshRenderer plane)
        {
            if (plane == null)
            {
                return;
            }
            // Unity's == (native identity), not IndexOf: two interop wrappers of one renderer need
            // not be the same managed object.
            for (int i = 0; i < this.avatarStudioPlanes.Count && i < this.avatarStudioPlaneOriginals.Count; i++)
            {
                if (this.avatarStudioPlanes[i] == plane && this.avatarStudioPlaneOriginals[i] != null)
                {
                    plane.sharedMaterial = this.avatarStudioPlaneOriginals[i];
                    return;
                }
            }
        }

        // ── picking ─────────────────────────────────────────────────────────────────────────────

        internal void ApplyAvatarStudioBackground(int index)
        {
            if (index < 0 || index >= this.avatarStudioBackgrounds.Count)
            {
                return;
            }

            AvatarStudioBackground pick = this.avatarStudioBackgrounds[index];
            Material material = null;
            string error = null;
            try
            {
                switch (pick.Kind)
                {
                    case AvatarStudioBackgroundKind.Panel:
                        material = pick.PlaneIndex < this.avatarStudioPlaneOriginals.Count
                            ? this.avatarStudioPlaneOriginals[pick.PlaneIndex]
                            : null;
                        break;
                    case AvatarStudioBackgroundKind.Extra:
                        material = this.LoadAvatarStudioExtraBackground(pick.Name, out error);
                        break;
                    case AvatarStudioBackgroundKind.File:
                        material = this.LoadAvatarStudioFileBackground(pick.Name, out error);
                        break;
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
            }

            if (pick.Kind != AvatarStudioBackgroundKind.Game && material == null)
            {
                string why = error ?? "material unavailable";
                FeatureLog.Fail(AvatarStudioTag, "background " + pick.Label + " failed: " + why);
                this.SetAvatarStudioStatus("Background failed: " + why);
                return;
            }

            // Drop the previous pick: restore its plane, free a file texture that is no longer used.
            if (this.avatarStudioOverridePlane != null)
            {
                this.RestoreAvatarStudioPlane(this.avatarStudioOverridePlane);
            }
            this.avatarStudioOverridePlane = null;
            if (pick.Kind != AvatarStudioBackgroundKind.File)
            {
                this.DestroyAvatarStudioCustomBackground();
            }

            this.avatarStudioBackgroundSelected = index;
            this.avatarStudioBackgroundOverride = pick.Kind == AvatarStudioBackgroundKind.Game ? null : material;
            if (this.avatarStudioBackgroundOverride != null)
            {
                this.KeepAvatarStudioBackgroundOnActivePlane();
            }
            this.SetAvatarStudioStatus("Background: " + pick.Label);
            FeatureLog.Once(AvatarStudioTag, "first-background", "first background applied (" + pick.Kind + ")");
        }

        private bool EnsureAvatarStudioResManager()
        {
            if (this.avatarStudioResLoadObjectSync != null)
            {
                return true;
            }
            if (Time.unscaledTime < this.avatarStudioResRetryAt)
            {
                return false;
            }
            this.avatarStudioResRetryAt = Time.unscaledTime + 15f;

            // ResManager is IL2CPP engine code with an interop stub (not an XDT/EcsClient Mono type),
            // so the managed lookup is the right channel here — HeartopiaComplete.GameIcons.cs does
            // the same for icons.
            Type type = this.FindLoadedType(
                "ScriptsRefactory.ResSystem.ResManager",
                "Il2CppScriptsRefactory.ResSystem.ResManager");
            if (type == null)
            {
                FeatureLog.Fail(AvatarStudioTag, "ResManager interop type not found — extra backgrounds unavailable");
                return false;
            }

            foreach (MethodInfo m in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                ParameterInfo[] ps = m.GetParameters();
                if (m.Name == "LoadObjectSync" && ps.Length == 3 && ps[0].ParameterType == typeof(string))
                {
                    this.avatarStudioResLoadObjectSync = m;
                }
                else if (m.Name == "UnLoadSync" && ps.Length == 1)
                {
                    this.avatarStudioResUnLoadSync = m;
                }
            }

            if (this.avatarStudioResLoadObjectSync == null)
            {
                FeatureLog.Fail(AvatarStudioTag, "ResManager.LoadObjectSync(string, string, Type) not found");
                return false;
            }
            return true;
        }

        private Material LoadAvatarStudioExtraBackground(string name, out string error)
        {
            error = null;
            if (this.avatarStudioExtraMaterials.TryGetValue(name, out Material cached) && cached != null)
            {
                return cached;
            }
            if (!this.EnsureAvatarStudioResManager())
            {
                error = "the game's resource loader is unavailable";
                return null;
            }

            object result = this.avatarStudioResLoadObjectSync.Invoke(null, new object[]
            {
                AvatarStudioMaterialPrefix + name,
                null,
                Il2CppInterop.Runtime.Il2CppType.Of<Material>(),
            });
            Material material = (result as Il2CppObjectBase)?.TryCast<Material>();
            if (material == null)
            {
                error = "the game could not load " + name;
                return null;
            }

            this.avatarStudioExtraMaterials[name] = material;
            return material;
        }

        private Material LoadAvatarStudioFileBackground(string path, out string error)
        {
            error = null;
            FileInfo fi = new FileInfo(path);
            if (!fi.Exists)
            {
                error = "file not found";
                return null;
            }
            if (fi.Length > AvatarStudioBackgroundMaxFileBytes)
            {
                error = "file is larger than 32 MB";
                return null;
            }

            // The template carries the plane's shader (Unlit/Texture); only the texture changes.
            Material template = this.avatarStudioPlaneOriginals.Count > 0 ? this.avatarStudioPlaneOriginals[0] : null;
            if (template == null)
            {
                error = "the panel's background material is unavailable";
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                Object.Destroy(texture);
                error = "not a readable PNG/JPG";
                return null;
            }
            texture.wrapMode = TextureWrapMode.Clamp;

            Material material = new Material(template);
            material.mainTexture = texture;

            // Cover-crop to the plane's 2:1 so the picture is never stretched.
            float aspect = texture.height > 0 ? texture.width / (float)texture.height : AvatarStudioBackgroundAspect;
            Vector2 scale = Vector2.one;
            if (aspect > AvatarStudioBackgroundAspect)
            {
                scale.x = AvatarStudioBackgroundAspect / aspect;
            }
            else
            {
                scale.y = aspect / AvatarStudioBackgroundAspect;
            }
            material.mainTextureScale = scale;
            material.mainTextureOffset = new Vector2((1f - scale.x) * 0.5f, (1f - scale.y) * 0.5f);

            this.DestroyAvatarStudioCustomBackground();
            this.avatarStudioCustomTexture = texture;
            this.avatarStudioCustomMaterial = material;
            return material;
        }

        internal void OpenAvatarStudioBackgroundFolder()
        {
            string dir = AvatarStudioBackgroundDirectory;
            this.avatarStudioBackgroundNextScanAt = 0f; // rescan on the next frame
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", dir);
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(AvatarStudioTag, "opening " + dir + " threw: " + ex.Message);
            }
            this.SetAvatarStudioStatus("Put PNG/JPG files in " + dir);
        }
    }
}
