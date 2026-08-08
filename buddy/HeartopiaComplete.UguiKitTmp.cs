using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

using UnityObject = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // TMP ISOLATION LAYER — the ONLY file in the project allowed to name TextMeshPro types.
    //
    // Why: the two loader interops disagree on the TMP namespace. BepInEx's generator keeps plain
    // TMPro; MelonLoader's Il2CppAssemblies generator emits Il2CppTMPro. GlobalUsings.cs picks the
    // compile-time namespace per flavor, but the UNIVERSAL DLL (compiled against plain TMPro) must
    // also RUN under MelonLoader, whose runtime Unity.TextMeshPro.dll has no TMPro.* types — the
    // JIT then throws TypeLoadException while compiling any method that references them, BEFORE any
    // try/catch inside that method can run. The kit's legacy-Text fallback never got a chance.
    //
    // The contract that makes the fallback reachable again:
    //  - Every method here carries [MethodImpl(MethodImplOptions.NoInlining)] and a TMP-free
    //    signature. Callers JIT cleanly (they only see this method's def); TMP types load only if a
    //    method HERE is actually invoked. NoInlining is load-bearing: tiered recompilation would
    //    otherwise inline these small bodies into callers and re-plant the type refs there.
    //  - Callers gate every invocation on UguiTmpTypesLoadable(), a string-only runtime probe of
    //    the loaded Unity.TextMeshPro assembly for THIS flavor's compile-time namespace.
    //  - No other file may declare a TMP-typed local/field/param. Kit state that holds TMP objects
    //    (uguiKitTmpFont) is typed UnityEngine.Object and TryCast'd here.
    //
    // This also degrades gracefully on installs whose interop generator uses the OTHER convention
    // (e.g. newer BepInEx builds that prefix): probe fails -> legacy Text everywhere, no crash.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // Compile-time TMP namespace this flavor binds to — MUST stay in sync with GlobalUsings.cs.
#if LOADER_MELON && !LOADER_BEPINEX
        private const string UguiTmpProbeTypeName = "Il2CppTMPro.TextMeshProUGUI";
#else
        private const string UguiTmpProbeTypeName = "TMPro.TextMeshProUGUI";
#endif

        private static int uguiTmpTypesPresent; // 0 = not probed yet, 1 = present, 2 = absent

        // String-only probe — deliberately NO TMP type refs, safe to JIT anywhere. Cached for the
        // session: the interop assembly is fixed at load, the answer can never change mid-run.
        private static bool UguiTmpTypesLoadable()
        {
            int state = uguiTmpTypesPresent;
            if (state == 0)
            {
                bool ok = false;
                try
                {
                    Assembly tmpAsm = null;
                    Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < loaded.Length; i++)
                    {
                        string simpleName = null;
                        try { simpleName = loaded[i].GetName().Name; } catch { }
                        if (string.Equals(simpleName, "Unity.TextMeshPro", StringComparison.OrdinalIgnoreCase))
                        {
                            tmpAsm = loaded[i];
                            break;
                        }
                    }
                    if (tmpAsm == null)
                    {
                        // Not loaded yet (probe can run before any TMP label exists) — ask the
                        // loader's resolver for it; both loaders serve their interop folder.
                        try { tmpAsm = Assembly.Load("Unity.TextMeshPro"); } catch { }
                    }
                    ok = tmpAsm != null && tmpAsm.GetType(UguiTmpProbeTypeName, false) != null;
                    if (!ok)
                    {
                        ModLogger.Msg("[UguiKit] TMP probe: '" + UguiTmpProbeTypeName + "' not found in "
                            + (tmpAsm == null ? "<no Unity.TextMeshPro assembly>" : "the loaded Unity.TextMeshPro")
                            + " — this loader's interop uses the other TMP namespace; kit labels fall back to legacy Text.");
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[UguiKit] TMP probe failed (assuming absent): " + ex.Message);
                    ok = false;
                }
                state = ok ? 1 : 2;
                uguiTmpTypesPresent = state;
            }
            return state == 1;
        }

        // ----------------------------------------------------------------------------------------
        // Font resolve (TMP half of EnsureUguiFonts)
        // ----------------------------------------------------------------------------------------

        // BUG FIX (2026-07-22): this used to take the FIRST TMP_FontAsset the scan returned and
        // latch it for the process. Which one that is depends on what happens to be loaded the
        // first time the menu opens, so the whole UI silently changed typeface between runs —
        // logs show "TMP=LiberationSans SDF" when opened at the login screen vs
        // "TMP=FZY4JW_SDF" (a GAME font) when opened in-world. That caused two reported bugs at
        // once: the game font's material carries an OUTLINE (game UI text is outlined so it
        // reads over the world), and its Latin glyphs are far wider (~9px/char vs ~6px), so
        // labels this layout was measured against started overrunning their rects.
        // The font is now HARD-PINNED to LiberationSans SDF — no user choice (the picker was
        // built, then removed once OS fonts proved impossible and the only real alternatives
        // were the game's own outlined, much wider assets). LiberationSans is TMP's built-in:
        // always loaded, clean material, and the exact metrics every rect in this kit was sized
        // against. First-found stays as a last resort ONLY so the UI still renders text if the
        // built-in ever goes missing; it is not a preference.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void UguiKitTmpResolveFonts()
        {
            TMP_FontAsset pinnedCandidate = null;
            TMP_FontAsset anyCandidate = null;
            try
            {
                var found = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<TMP_FontAsset>());
                if (found != null)
                {
                    for (int i = 0; i < found.Length; i++)
                    {
                        TMP_FontAsset fa = (found[i] != null) ? found[i].TryCast<TMP_FontAsset>() : null;
                        if (fa == null)
                        {
                            continue;
                        }
                        if (anyCandidate == null)
                        {
                            anyCandidate = fa;
                        }
                        string faName = fa.name ?? string.Empty;
                        if (faName.IndexOf("LiberationSans", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            pinnedCandidate = fa;
                            break; // pinned target found — stop looking
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] TMP font scan failed: " + ex.Message);
            }

            // TMP's own default setting is the canonical home of LiberationSans SDF and can hand it
            // over even on a frame when FindObjectsOfTypeAll has not seen it yet — worth asking
            // before settling for a game font.
            if (pinnedCandidate == null)
            {
                try
                {
                    TMP_FontAsset def = TMP_Settings.defaultFontAsset;
                    if (def != null)
                    {
                        if ((def.name ?? string.Empty).IndexOf("LiberationSans", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            pinnedCandidate = def;
                        }
                        else if (anyCandidate == null)
                        {
                            anyCandidate = def;
                        }
                    }
                }
                catch (Exception ex) { ModLogger.Msg("[UguiKit] TMP_Settings.defaultFontAsset failed: " + ex.Message); }
            }

            TMP_FontAsset resolved = pinnedCandidate != null ? pinnedCandidate : anyCandidate;
            this.uguiKitTmpFont = resolved;
            this.uguiKitFontPinned = pinnedCandidate != null;

            // Our OWN material preset, cloned from whichever asset won above. TMP draws outline /
            // glow / drop-shadow from MATERIAL properties, and a game font asset's shared material
            // has them dialled in for readability over the world — which is where the reported
            // outline came from. Zeroing them on the SHARED material would restyle the game's own
            // text, so clone once and hand every kit label the clone instead (one instance for the
            // whole UI, not one per label). SetFloat on a property this shader lacks is a no-op, so
            // the list is safe to over-specify. If the clone fails we simply keep the stock look.
            if (resolved != null)
            {
                try
                {
                    Material srcMat = resolved.material;
                    if (srcMat != null)
                    {
                        Material flat = new Material(srcMat);
                        flat.name = srcMat.name + " (Bugtopia flat)";
                        flat.SetFloat("_OutlineWidth", 0f);
                        flat.SetFloat("_OutlineSoftness", 0f);
                        flat.SetFloat("_GlowPower", 0f);
                        flat.SetFloat("_GlowOuter", 0f);
                        flat.SetFloat("_UnderlayDilate", 0f);
                        flat.SetFloat("_UnderlaySoftness", 0f);
                        flat.SetFloat("_UnderlayOffsetX", 0f);
                        flat.SetFloat("_UnderlayOffsetY", 0f);
                        this.uguiKitTmpMaterial = flat;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[UguiKit] TMP flat material clone failed (keeping stock look): " + ex.Message);
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Label construction (TMP half of CreateUguiLabel)
        // ----------------------------------------------------------------------------------------

        private const int UguiTmpLabelBuilt = 0;      // TMP label fully set up
        private const int UguiTmpLabelBuiltBroken = 1; // half-initialized TMP claimed the GO — keep it
        private const int UguiTmpLabelNotBuilt = 2;   // GO untouched — caller falls back to legacy

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int UguiKitTmpBuildLabel(GameObject go, string name, float size, Color color, bool centered, string text)
        {
            try
            {
                TMP_FontAsset font = (this.uguiKitTmpFont != null) ? this.uguiKitTmpFont.TryCast<TMP_FontAsset>() : null;
                if (font == null)
                {
                    return UguiTmpLabelNotBuilt;
                }
                TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.font = font;
                if (this.uguiKitTmpMaterial != null)
                {
                    // Outline/glow/shadow-free preset (EnsureUguiFonts) — assigning the SHARED
                    // material keeps all kit labels on one material, so this stays a single
                    // draw-call batch instead of instancing a material per label.
                    try { tmp.fontSharedMaterial = this.uguiKitTmpMaterial; } catch { }
                }
                tmp.fontSize = size;
                tmp.color = color;
                // Wrap by default (2026-07-22): a label that outgrows its rect should flow onto
                // a second line wherever the rect has the height for one, and only fall back to
                // the "…" below when it genuinely has nowhere to go. Single-line labels are
                // unaffected — wrapping only does anything once the text exceeds the width.
                // (TMP 2.0 renamed this to textWrappingMode/TextWrappingModes; enableWordWrapping
                // is the [FormerlySerializedAs] alias and still the one that exists on this
                // build's stripped TMP, so it stays.)
                tmp.enableWordWrapping = true;
                // BUG FIX (2026-07-22): TMP's DEFAULT overflowMode is Overflow — text longer
                // than its rect keeps rendering straight past the edge and draws OVER whatever
                // sits next to it. Reported on Auto Sell (the selected-item key line rendering
                // under the "Auto" checkbox, the star-info hint spilling out of the 205px left
                // column and across "Keep Per Item"), but it was never an Auto Sell bug: those
                // rects are correctly sized and stop short of their neighbours, and EVERY label
                // in the app had the same behaviour, so any string long enough would collide.
                // Ellipsis keeps the glyphs inside the rect and trims with "…" instead, which
                // makes a too-long label a readability question rather than a layout collision.
                // Applies to wrapped labels too (TrySetUguiLabelWrapped): they wrap on width as
                // before and only ellipsize past the rect HEIGHT.
                tmp.overflowMode = TextOverflowModes.Ellipsis;
                tmp.alignment = centered ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
                tmp.raycastTarget = false;
                tmp.text = text;
                return UguiTmpLabelBuilt;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] TMP label '" + name + "' failed, using legacy Text: " + ex.Message);
                if (go.GetComponent<TextMeshProUGUI>() != null)
                {
                    // A half-initialized TMP graphic already claimed the CanvasRenderer; a
                    // second Graphic on the same GO is invalid — keep the broken TMP.
                    return UguiTmpLabelBuiltBroken;
                }
                return UguiTmpLabelNotBuilt;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Style setters (TMP halves of the kit + content-file helpers). Each returns false when the
        // label has no TMP component, sending the caller down its legacy branch.
        // ----------------------------------------------------------------------------------------

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpTrySetBold(GameObject label)
        {
            TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                return false;
            }
            tmp.fontStyle = FontStyles.Bold; // enum verified present in this build's TMP
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpTrySetItalic(GameObject label)
        {
            TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                return false;
            }
            tmp.fontStyle = FontStyles.Italic;
            return true;
        }

        // Kit default wrap variant: multi-line top-LEFT (About bodies, Research status/footer).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpTrySetWrapped(GameObject label)
        {
            TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                return false;
            }
            tmp.enableWordWrapping = true;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            return true;
        }

        // Tab-bar variant: wrapped and centred on BOTH axes, so a two-line label sits centred in
        // the tab instead of hugging its top edge.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpTrySetWrappedCentered(GameObject label)
        {
            TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                return false;
            }
            tmp.enableWordWrapping = true;
            tmp.alignment = TextAlignmentOptions.Center;
            return true;
        }

        // Transfer-cell variant: wrapped but horizontally CENTERED (IMGUI itemStyle parity).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpTrySetWrappedTop(GameObject label)
        {
            TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                return false;
            }
            tmp.enableWordWrapping = true;
            tmp.alignment = TextAlignmentOptions.Top;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpTrySetRightAligned(GameObject label)
        {
            TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                return false;
            }
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpTrySetText(GameObject label, string text)
        {
            TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                return false;
            }
            tmp.text = text;
            return true;
        }

        // Read-back for the CJK checks that run AFTER construction (the bold gate).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpTryGetText(GameObject label, out string text)
        {
            text = null;
            TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                return false;
            }
            text = tmp.text;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpTrySetColor(GameObject label, Color color)
        {
            TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                return false;
            }
            tmp.color = color;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpTrySetFontSize(GameObject label, float size)
        {
            TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                return false;
            }
            tmp.fontSize = size;
            return true;
        }

        // ----------------------------------------------------------------------------------------
        // Component lookup + measurement (toast layout, Pictures wrapped-height)
        // ----------------------------------------------------------------------------------------

        // Toast cards cache the TMP component (as UnityEngine.Object — see UguiToastCard.LabelTmp).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private UnityObject UguiKitTmpGetLabelComponent(GameObject label)
        {
            return label.GetComponent<TextMeshProUGUI>();
        }

        // singleLine=true  -> GetPreferredValues(text).x   (unconstrained single-line WIDTH)
        // singleLine=false -> GetPreferredValues(text, width, 0).y (wrapped HEIGHT at that width)
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpMeasureFromComponent(UnityObject tmpObj, string text, float width, bool singleLine, out float value)
        {
            value = 0f;
            TextMeshProUGUI tmp = (tmpObj != null) ? tmpObj.TryCast<TextMeshProUGUI>() : null;
            if (tmp == null)
            {
                return false;
            }
            if (singleLine)
            {
                value = tmp.GetPreferredValues(text).x;
            }
            else
            {
                value = tmp.GetPreferredValues(text, width, 0f).y;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpMeasureWrappedHeightFromLabel(GameObject label, string text, float width, out float value)
        {
            value = 0f;
            TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                return false;
            }
            value = tmp.GetPreferredValues(text, width, 0f).y;
            return true;
        }

        // CJK fallback wiring (see EnsureUguiKitCjkFallback in HeartopiaComplete.UguiKit.cs for the
        // why). Returns the fallback's display name once wired, or null to retry later. TMP-free
        // signature: `probeChar` is an int codepoint and the result is a plain string, so callers
        // JIT without pulling in TMP types.
        // Full inventory of every route a CJK-capable TMP font could arrive by. Logged once so the
        // question "does a usable Chinese font asset exist in this process at all?" is answered by
        // evidence instead of by a single FindObjectsOfTypeAll call that came back with two Latin
        // fonts. Returns the asset if found, else null.
        // Route 4 — load the game's own font bundles off disk.
        //
        // Established by decrypting StreamingAssets/AssetBundle/fonts_*.ab (UnityCN, AB key
        // "27v8HxLIptguw3Jn" capped at 0x6f — see the tabledata-decryption research):
        //   fonts_1f32e5f8.ab  4.80 MB  Font:FZY4JW              <- SOURCE font (the actual TTF)
        //   fonts_511c4845.ab  0.01 MB  MonoBehaviour:FZY4JW_SDF <- the TMP font asset + atlas
        //   fonts_76ab0d41.ab  0.01 MB  FZY4K_SDF
        //   fonts_7ca52680.ab  0.01 MB  Kanit-Medium SDF   (Thai)
        //   fonts_1b0e2909.ab  0.01 MB  Jua-Regular SDF    (Korean)
        //   fonts_67944a8c.ab  0.01 MB  MPLUSRounded1c-Bold SDF (Japanese)
        // A ~10 KB bundle covering all of CJK proves those SDF assets are DYNAMIC: the atlas ships
        // nearly empty and glyphs are rasterized on demand from sourceFontFile. Two consequences:
        //  (1) HasCharacter must be asked with tryAddCharacter:true — the plain form only reports
        //      what is already in the atlas, which is why the first attempt found nothing;
        //  (2) the SOURCE bundle has to be loaded too, or the dynamic asset has no TTF to
        //      rasterize from. Unity does not auto-resolve a bundle dependency that is not loaded.
        // Sizes are used instead of hardcoded hash names so a content patch (which renames these
        // files) does not silently break it: small bundles hold the SDF assets, the big ones the
        // source fonts. Already-loaded bundles throw and are skipped.
        //
        // The sweep runs ONCE per session and harvests EVERY font asset it meets into
        // uguiKitBundleFonts, whether or not it matches the caller's probe. Two reasons, both
        // learned the hard way (2026-08-09): a later hunt for a different script cannot re-open
        // these bundles (LoadFromFile throws for anything already loaded — including by us), and
        // the Korean face is precisely the one that fails a Han probe, so probe-filtered harvesting
        // threw away the asset the next hunt needed.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void UguiKitTmpSweepFontBundles(System.Text.StringBuilder report)
        {
            this.uguiKitBundleSweepDone = true;

            string dir;
            try { dir = Application.streamingAssetsPath + "/AssetBundle"; }
            catch (Exception ex) { report.Append("bundleDir=<err ").Append(ex.Message).Append("> "); return; }

            string[] files;
            try
            {
                if (!System.IO.Directory.Exists(dir))
                {
                    report.Append("bundleDir=missing ");
                    return;
                }
                files = System.IO.Directory.GetFiles(dir, "fonts_*.ab");
            }
            catch (Exception ex) { report.Append("bundleList=<err ").Append(ex.Message).Append("> "); return; }

            // Source fonts first (largest), then the SDF assets (smallest). Same reason as above:
            // the dynamic asset needs its TTF present before it can add a glyph.
            Array.Sort(files, (a, b) =>
            {
                long la = 0, lb = 0;
                try { la = new System.IO.FileInfo(a).Length; } catch { }
                try { lb = new System.IO.FileInfo(b).Length; } catch { }
                return lb.CompareTo(la);
            });

            int loaded = 0;
            report.Append("bundles=").Append(files.Length).Append(' ');
            // Both passes always run to completion — the sweep is once per session, so stopping at
            // the first usable font would strand every other face where no later pass can reach it.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    long len = 0;
                    try { len = new System.IO.FileInfo(files[i]).Length; } catch { }
                    bool big = len > 262144;
                    if (pass == 0 ? !big : big)
                    {
                        continue; // pass 0 = source fonts, pass 1 = the small SDF bundles
                    }
                    AssetBundle ab = null;
                    try { ab = AssetBundle.LoadFromFile(files[i]); }
                    catch { continue; }        // already loaded by the game, or undecryptable
                    if (ab == null)
                    {
                        continue;
                    }
                    loaded++;
                    if (pass == 0)
                    {
                        continue;              // source fonts: loading them is the whole point
                    }
                    try
                    {
                        var assets = ab.LoadAllAssets(Il2CppInterop.Runtime.Il2CppType.Of<TMP_FontAsset>());
                        if (assets == null)
                        {
                            continue;
                        }
                        for (int k = 0; k < assets.Length; k++)
                        {
                            TMP_FontAsset fa = (assets[k] != null) ? assets[k].TryCast<TMP_FontAsset>() : null;
                            if (fa == null)
                            {
                                continue;
                            }
                            // Harvested unconditionally — see this method's header. Which of these
                            // covers which script is a question each hunt asks for itself.
                            if (!this.UguiKitTmpBundleFontsContains(fa))
                            {
                                this.uguiKitBundleFonts.Add(fa);
                                report.Append("bundleFont='").Append(fa.name ?? "?").Append("' ");
                            }
                        }
                    }
                    catch { }
                }
            }
            report.Append("bundlesLoaded=").Append(loaded)
                .Append(" bundleFonts=").Append(this.uguiKitBundleFonts.Count).Append(' ');
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpBundleFontsContains(TMP_FontAsset fa)
        {
            for (int i = 0; i < this.uguiKitBundleFonts.Count; i++)
            {
                UnityObject held = this.uguiKitBundleFonts[i];
                if (held != null && held.Pointer == fa.Pointer)
                {
                    return true;
                }
            }
            return false;
        }

        // Wire EVERY non-Latin face we can reach into LiberationSans' fallback table, in one pass.
        //
        // Simplified 2026-08-09 (round 3), once the per-script hunts had proved the point they were
        // built to prove. Probing at WIRING time was the wrong place for the question: it made the
        // set of fallbacks depend on which script asked first, which is exactly how a Han probe
        // came to reject the Korean face. Coverage adds up in a chain, so there is nothing to
        // decide here — wire them all, and ask about coverage afterwards, once per script that is
        // actually needed (UguiKitTmpPrimaryHasCharacter).
        //
        // `probeChars` is the adoption filter, and callers pass EVERY script the kit models — never
        // just the ones this session happens to need. That distinction is the whole round-2 lesson:
        // filtering by the asking script is order-dependent and drops the face the next script will
        // need; filtering by the fixed full set wires the same chain no matter who asks first. It
        // earns its keep because the game's font bundles also hold Latin display faces (Quicksand,
        // Fredoka, XdtownRounded) that LiberationSans makes redundant — without it they take chain
        // slots and a flattened material clone each, for glyphs they will never be asked to draw.
        // Returns the head font's name, or null when nothing at all could be wired.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private string UguiKitTmpWireFallbackFonts(int[] probeChars)
        {
            TMP_FontAsset primary = (this.uguiKitTmpFont != null) ? this.uguiKitTmpFont.TryCast<TMP_FontAsset>() : null;
            if (primary == null)
            {
                return null;
            }
            var table = primary.fallbackFontAssetTable;
            if (table == null)
            {
                return null;
            }

            System.Text.StringBuilder report = new System.Text.StringBuilder();
            System.Collections.Generic.List<TMP_FontAsset> candidates =
                new System.Collections.Generic.List<TMP_FontAsset>();

            // Source 1 — the game's font bundles off disk, swept once per session.
            if (!this.uguiKitBundleSweepDone)
            {
                try { this.UguiKitTmpSweepFontBundles(report); }
                catch (Exception ex) { report.Append("bundleSweep=<err ").Append(ex.Message).Append("> "); }
            }
            for (int i = 0; i < this.uguiKitBundleFonts.Count; i++)
            {
                TMP_FontAsset fa = (this.uguiKitBundleFonts[i] != null)
                    ? this.uguiKitBundleFonts[i].TryCast<TMP_FontAsset>() : null;
                if (UguiKitTmpFontCoversAny(fa, probeChars))
                {
                    UguiKitTmpAddFontCandidate(candidates, fa);
                }
            }

            // Source 2 — font assets already in the process (the game's own, once it has loaded the
            // face for its language) plus TMP's project-wide defaults, which are a different list
            // from any single asset's own fallbackFontAssetTable.
            try
            {
                var all = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<TMP_FontAsset>());
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        TMP_FontAsset fa = (all[i] != null) ? all[i].TryCast<TMP_FontAsset>() : null;
                        if (UguiKitTmpFontCoversAny(fa, probeChars))
                        {
                            UguiKitTmpAddFontCandidate(candidates, fa);
                        }
                    }
                }
            }
            catch (Exception ex) { report.Append("assetScan=<err ").Append(ex.Message).Append("> "); }

            try
            {
                TMP_FontAsset def = TMP_Settings.defaultFontAsset;
                if (UguiKitTmpFontCoversAny(def, probeChars))
                {
                    UguiKitTmpAddFontCandidate(candidates, def);
                }
                var globals = TMP_Settings.fallbackFontAssets;
                if (globals != null)
                {
                    for (int i = 0; i < globals.Count; i++)
                    {
                        if (UguiKitTmpFontCoversAny(globals[i], probeChars))
                        {
                            UguiKitTmpAddFontCandidate(candidates, globals[i]);
                        }
                    }
                }
            }
            catch (Exception ex) { report.Append("tmpSettings=<err ").Append(ex.Message).Append("> "); }

            // Source 3 — the game's live text components. If the script is on screen anywhere, the
            // font drawing it is reachable here even when the asset scan missed it (different load
            // path / not registered as a standalone asset object). Capped: a big scene holds
            // thousands of these, though the DISTINCT fonts among them are few.
            try
            {
                var texts = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<TextMeshProUGUI>());
                int scanned = 0;
                if (texts != null)
                {
                    System.Collections.Generic.List<TMP_FontAsset> seen =
                        new System.Collections.Generic.List<TMP_FontAsset>();
                    for (int i = 0; i < texts.Length && scanned < 4000; i++)
                    {
                        TextMeshProUGUI t = (texts[i] != null) ? texts[i].TryCast<TextMeshProUGUI>() : null;
                        if (t == null)
                        {
                            continue;
                        }
                        scanned++;
                        TMP_FontAsset fa = t.font;
                        if (fa == null || UguiKitTmpListContains(seen, fa))
                        {
                            continue; // HasCharacter(tryAddCharacter) is not free — ask each face once
                        }
                        seen.Add(fa);
                        if (UguiKitTmpFontCoversAny(fa, probeChars))
                        {
                            UguiKitTmpAddFontCandidate(candidates, fa);
                        }
                    }
                }
                report.Append("liveTexts=").Append(scanned).Append(' ');
            }
            catch (Exception ex) { report.Append("liveTextScan=<err ").Append(ex.Message).Append("> "); }

            if (candidates.Count == 0)
            {
                ModLogger.Msg("[UguiKit] fallback wiring: " + report.ToString() + "=> no non-Latin face found");
                return null;
            }

            // FZY4JW leads where it is present: it is the game's primary CJK face, so Han renders in
            // the shapes the game itself uses rather than the Japanese face's variants. Order only
            // decides anything for glyphs several candidates share.
            for (int i = 1; i < candidates.Count; i++)
            {
                if ((candidates[i].name ?? string.Empty).IndexOf("FZY4JW", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TMP_FontAsset head = candidates[i];
                    candidates.RemoveAt(i);
                    candidates.Insert(0, head);
                    break;
                }
            }

            System.Text.StringBuilder wired = new System.Text.StringBuilder();
            for (int n = 0; n < candidates.Count; n++)
            {
                TMP_FontAsset fa = candidates[n];
                if (fa.Pointer == primary.Pointer || UguiKitTmpTableContains(table, fa))
                {
                    continue;
                }
                // Flatten the fallback's OWN material before wiring it — our flat-material clone is
                // assigned per-label and so only governs the PRIMARY font. ONLY for assets WE loaded
                // off disk (AssetBundle.LoadFromFile throws for a bundle the game already holds, so
                // anything in uguiKitBundleFonts is our own instance): against a CJK game client the
                // sources above hand back the very asset the GAME's UI draws with, and rewriting its
                // material would reach straight into the game's rendering — for a tweak that was
                // never even the fix for the "too bold" report (synthetic bold was, see
                // TrySetUguiLabelBold). A foreign asset is wired in untouched.
                if (this.UguiKitTmpBundleFontsContains(fa))
                {
                    try
                    {
                        Material fbSrc = fa.material;
                        if (fbSrc != null)
                        {
                            Material fbFlat = new Material(fbSrc);
                            fbFlat.name = (fbSrc.name ?? "?") + " (Bugtopia flat)";
                            fbFlat.SetFloat("_OutlineWidth", 0f);
                            fbFlat.SetFloat("_OutlineSoftness", 0f);
                            fbFlat.SetFloat("_FaceDilate", 0f);
                            fbFlat.SetFloat("_GlowPower", 0f);
                            fbFlat.SetFloat("_GlowOuter", 0f);
                            fbFlat.SetFloat("_UnderlayDilate", 0f);
                            fbFlat.SetFloat("_UnderlaySoftness", 0f);
                            fbFlat.SetFloat("_UnderlayOffsetX", 0f);
                            fbFlat.SetFloat("_UnderlayOffsetY", 0f);
                            fa.material = fbFlat;
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Msg("[UguiKit] fallback '" + (fa.name ?? "?") + "' flatten failed: " + ex.Message);
                    }
                }
                else
                {
                    report.Append("(game asset, material untouched) ");
                }

                table.Add(fa);
                if (wired.Length > 0) { wired.Append(", "); }
                wired.Append(fa.name ?? "?");
            }

            ModLogger.Msg("[UguiKit] fallback wiring behind " + (primary.name ?? "?") + ": " + report.ToString()
                + "newly wired [" + (wired.Length > 0 ? wired.ToString() : "none, already present")
                + "] tableCount=" + table.Count);
            return candidates[0].name;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void UguiKitTmpAddFontCandidate(System.Collections.Generic.List<TMP_FontAsset> candidates,
            TMP_FontAsset fa)
        {
            if (fa != null && !UguiKitTmpListContains(candidates, fa))
            {
                candidates.Add(fa);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool UguiKitTmpListContains(System.Collections.Generic.List<TMP_FontAsset> list,
            TMP_FontAsset fa)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].Pointer == fa.Pointer)
                {
                    return true;
                }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool UguiKitTmpTableContains(Il2CppSystem.Collections.Generic.List<TMP_FontAsset> table,
            TMP_FontAsset fa)
        {
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i] != null && table[i].Pointer == fa.Pointer)
                {
                    return true;
                }
            }
            return false;
        }

        // tryAddCharacter:true is load-bearing — a DYNAMIC font asset reports false for a glyph that
        // is not in its atlas yet even when its source font could rasterize it. Asking without it is
        // what made the very first CJK attempt (2026-07-25) find nothing at all.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool UguiKitTmpFontCoversAny(TMP_FontAsset fa, int[] probeChars)
        {
            if (fa == null || probeChars == null)
            {
                return false;
            }
            for (int i = 0; i < probeChars.Length; i++)
            {
                try
                {
                    if (fa.HasCharacter((char)probeChars[i], false, true))
                    {
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        // Coverage question, asked of the PRIMARY font WITH its fallback chain: "can the kit draw
        // this script now?". The one place a probe glyph belongs.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool UguiKitTmpPrimaryHasCharacter(int probeChar)
        {
            TMP_FontAsset primary = (this.uguiKitTmpFont != null) ? this.uguiKitTmpFont.TryCast<TMP_FontAsset>() : null;
            if (primary == null)
            {
                return false;
            }
            try { return primary.HasCharacter((char)probeChar, true, true); }
            catch { return false; }
        }
    }
}
