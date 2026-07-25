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
    }
}
