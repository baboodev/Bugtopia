#if FEATURE_MCP
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // Screenshot capture for the MCP bridge — lets the agent SEE the frame instead of inferring it
    // from entity lists and canvas names.
    //
    // ── WHY A PLAYER-LOOP NODE AND NOT A CAMERA RENDER ───────────────────────────────────────────
    // The obvious alternative — point a camera at a RenderTexture and call Render() on demand — is
    // self-contained and needs no timing hook, but it loses the entire UI: ScreenSpaceOverlay
    // canvases draw straight to the backbuffer, not through any camera. For an agent, "what panel is
    // open" is half the value of a screenshot, so the capture has to read the real backbuffer, which
    // means running after the frame has been rendered.
    //
    // ── WHY IT INSTALLS ITSELF, LAZILY, AND SEPARATELY ───────────────────────────────────────────
    // PlayerLoopProbe.Install() treats a failed node insert as fatal and falls back to an injected
    // MonoBehaviour — which drags in ClassInjector and its five GameAssembly .text detours, the exact
    // surface this mod spent so much effort removing. A screenshot must never be able to cause that,
    // so this node goes in through TryInsertExtraNode (failure is local), and only on the first
    // request: a session that never asks for a screenshot never touches the player loop at all.
    //
    // ── FALLBACK ─────────────────────────────────────────────────────────────────────────────────
    // If the anchor does not exist on this Unity version the capture still works from LateUpdate,
    // reading whatever the backbuffer currently holds. That can be the previous frame or a partially
    // composited one, so those results are flagged `mayBeTorn`.
    // ============================================================================================
    internal static unsafe class McpScreenshot
    {
        internal const int MinIntervalMs = 400;

        internal static string Status = "not installed";
        internal static bool NodeInstalled;
        internal static bool UsingFallback;

        // Request/response handshake between the pump (Update) and the capture site (after render).
        private static volatile bool requested;
        private static volatile bool ready;
        private static byte[] payload;
        private static int payloadWidth;
        private static int payloadHeight;
        private static double payloadEncodeMs;
        private static double payloadReadMs;
        private static bool payloadTorn;
        private static string failure;

        private static int requestedMaxWidth = 1600;
        private static int requestedQuality = 70;
        private static int lastCaptureTick;

        private static IntPtr captureSlot;
        private static Texture2D fullTex;
        private static Texture2D scaledTex;
        private static int fullTexW;
        private static int fullTexH;

        // ── Install ──────────────────────────────────────────────────────────────────────────────

        internal static bool EnsureInstalled()
        {
            if (NodeInstalled || UsingFallback)
            {
                return true;
            }

            try
            {
                PreJit();
                captureSlot = PlayerLoopProbe.AllocSlot((IntPtr)(delegate* unmanaged[Cdecl]<void>)&CaptureThunk);

                // PostLateUpdate/FinishFrameRendering is where the frame is done but not yet handed
                // on, i.e. the player-loop equivalent of WaitForEndOfFrame.
                if (PlayerLoopProbe.TryInsertExtraNode("UnityEngine.PlayerLoop.PostLateUpdate",
                        "FinishFrameRendering", captureSlot,
                        Il2CppInterop.Runtime.Il2CppType.Of<Il2CppSystem.Attribute>(), out string error))
                {
                    NodeInstalled = true;
                    Status = "player-loop node installed (PostLateUpdate/FinishFrameRendering)";
                    ModLogger.Msg("[Mcp] screenshot: " + Status);
                    return true;
                }

                UsingFallback = true;
                Status = "no render node (" + error + ") — capturing from LateUpdate, frames may tear";
                ModLogger.Msg("[Mcp] screenshot: " + Status);
                return true;
            }
            catch (Exception ex)
            {
                UsingFallback = true;
                Status = "install threw: " + ex.GetType().Name + ": " + ex.Message + " — using LateUpdate";
                ModLogger.Warning("[Mcp] screenshot: " + Status);
                return true;
            }
        }

        private static void PreJit()
        {
            try
            {
                System.Reflection.MethodInfo m = typeof(McpScreenshot).GetMethod(
                    "CaptureThunk",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (m != null)
                {
                    RuntimeHelpers.PrepareMethod(m.MethodHandle);
                }
            }
            catch
            {
            }
        }

        // ── Capture sites ────────────────────────────────────────────────────────────────────────

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void CaptureThunk()
        {
            if (!requested)
            {
                return;
            }

            // This returns into native engine code: an escaping exception would unwind through the
            // player loop, so nothing may leave this frame.
            try
            {
                CaptureNow(false);
            }
            catch (Exception ex)
            {
                try
                {
                    failure = ex.GetType().Name + ": " + ex.Message;
                    requested = false;
                    ready = true;
                }
                catch
                {
                }
            }
        }

        // Fallback site, driven from OnLateUpdate when no render node could be installed.
        internal static void OnLateUpdateFallback()
        {
            if (!UsingFallback || !requested)
            {
                return;
            }

            try
            {
                CaptureNow(true);
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ": " + ex.Message;
                requested = false;
                ready = true;
            }
        }

        private static void CaptureNow(bool mayBeTorn)
        {
            requested = false;
            failure = null;

            int screenW = Screen.width;
            int screenH = Screen.height;
            if (screenW <= 0 || screenH <= 0)
            {
                failure = "screen is " + screenW + "x" + screenH;
                ready = true;
                return;
            }

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            // Textures are cached and rebuilt only when the resolution changes: allocating a
            // full-screen Texture2D per capture would churn megabytes through the GC. HideAndDontSave
            // keeps them off the scene graph so a level load cannot destroy them under us.
            if (fullTex == null || fullTexW != screenW || fullTexH != screenH)
            {
                if (fullTex != null)
                {
                    UnityEngine.Object.Destroy(fullTex);
                }

                fullTex = new Texture2D(screenW, screenH, TextureFormat.RGB24, false);
                fullTex.hideFlags = HideFlags.HideAndDontSave;
                fullTexW = screenW;
                fullTexH = screenH;
            }

            // ReadPixels reads the ACTIVE RenderTexture, or the backbuffer when there is none.
            fullTex.ReadPixels(new Rect(0f, 0f, screenW, screenH), 0, 0, false);
            fullTex.Apply(false);
            double readMs = sw.Elapsed.TotalMilliseconds;

            Texture2D source = fullTex;
            int outW = screenW;
            int outH = screenH;

            int maxW = requestedMaxWidth;
            if (maxW > 0 && screenW > maxW)
            {
                outW = maxW;
                outH = Mathf.Max(1, Mathf.RoundToInt(screenH * (maxW / (float)screenW)));
                if (TryDownscale(fullTex, outW, outH, out Texture2D scaled))
                {
                    source = scaled;
                }
                else
                {
                    // Downscaling is a convenience, not a requirement — a full-resolution JPEG is a
                    // worse payload, not a failed capture.
                    outW = screenW;
                    outH = screenH;
                }
            }

            byte[] jpg = ImageConversion.EncodeToJPG(source, requestedQuality);
            sw.Stop();

            payload = jpg;
            payloadWidth = outW;
            payloadHeight = outH;
            payloadReadMs = readMs;
            payloadEncodeMs = sw.Elapsed.TotalMilliseconds - readMs;
            payloadTorn = mayBeTorn;
            lastCaptureTick = Environment.TickCount;
            ready = true;
        }

        private static bool TryDownscale(Texture2D src, int w, int h, out Texture2D dst)
        {
            dst = null;
            RenderTexture rt = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                if (scaledTex == null || scaledTex.width != w || scaledTex.height != h)
                {
                    if (scaledTex != null)
                    {
                        UnityEngine.Object.Destroy(scaledTex);
                    }

                    scaledTex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    scaledTex.hideFlags = HideFlags.HideAndDontSave;
                }

                rt = RenderTexture.GetTemporary(w, h, 0);
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                scaledTex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, false);
                scaledTex.Apply(false);
                dst = scaledTex;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null)
                {
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
        }

        // ── Pump-side API ────────────────────────────────────────────────────────────────────────

        internal static bool TooSoon(out int waitMs)
        {
            int since = unchecked(Environment.TickCount - lastCaptureTick);
            if (lastCaptureTick != 0 && since >= 0 && since < MinIntervalMs)
            {
                waitMs = MinIntervalMs - since;
                return true;
            }

            waitMs = 0;
            return false;
        }

        internal static void Request(int maxWidth, int quality)
        {
            requestedMaxWidth = maxWidth;
            requestedQuality = quality;
            ready = false;
            payload = null;
            failure = null;
            requested = true;
        }

        internal static bool IsReady => ready;

        internal static bool TryTake(out byte[] bytes, out int width, out int height,
                                     out double readMs, out double encodeMs, out bool torn, out string error)
        {
            bytes = payload;
            width = payloadWidth;
            height = payloadHeight;
            readMs = payloadReadMs;
            encodeMs = payloadEncodeMs;
            torn = payloadTorn;
            error = failure;
            ready = false;
            payload = null;
            return error == null && bytes != null && bytes.Length > 0;
        }
    }
}
#endif
