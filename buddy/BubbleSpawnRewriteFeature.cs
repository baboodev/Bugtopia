using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Bubbles Spawn At Player (detour A of the bubble pair; detour B = AutoBubbleCollectFeature.cs).
    //
    // Rewrites the coordinates of every client spawn REQUEST to the player's position by
    // NativeDetour-ing the four 1-arg Vector3 sender wrappers (each builds its network command
    // and sends it via WebRequestUtility.SendCommand internally):
    //   slot 0  BubbleProtocolManager.CreateBubble(Vector3)            — daily town bubbles
    //   slot 1  BubbleProtocolManager.CreateVisitBubble(Vector3)       — visit bubbles
    //   slot 2  ActivityEventProtocolManager.CreateActivityBubble(V3)  — activity events
    //   slot 3  HideAndSeekProtocolManager.CreateHideAndSeekBubble(V3) — hide & seek
    // The server is authoritative — it spawns the bubble entity at (approximately) the requested
    // position and syncs it back, so the bubble REALLY is at the player for everyone.
    //
    // Same proven technique as the EventHook engine / building cell-patch:
    // mono_compile_method + MonoMod NativeDetour (Iced reloc), NOT the abandoned 14-byte steal.
    //
    // Win64 ABI: Vector3 is 12 bytes (not 1/2/4/8) → passed BY REFERENCE to a caller-made copy
    // (RCX). Confirmed by the PrecisionToCellSize precedent (12-byte Vector3 RETURN goes through
    // an sret pointer — same aggregate rule). Writing through the pointer mutates only the
    // caller's temporary copy, which is exactly what the callee reads. Native signature:
    // void(Vector3*), delegate void(IntPtr).
    //
    // Player position: the native bodies must not call into Mono/Il2Cpp/Unity (EventHook boundary
    // rules), so OnUpdate caches the position into static floats every frame ("same-frame writer
    // site" pattern) and the bodies only read statics. Senders fire from main-thread Update loops
    // (LevelBubbleSystem.Update, ActivityEventModule.Update), so writer and readers share the
    // Unity main thread — no tearing.
    //
    // The 1-arg CreateBubble resolve cannot collide with detour B's 4-arg CreateBubble: method
    // lookup is exact-arity (FindAuraMonoMethodOnHierarchy param count). Fast Bubble Gen's own
    // self-invoke of CreateActivityBubble passes through slot 2 and gets rewritten to the same
    // cached player position — a harmless no-op.
    public partial class HeartopiaComplete
    {
        // -- config (persisted; GUI in the Automation tab bubble block) --
        private bool bubbleSpawnAtPlayerEnabled = false;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void BubbleSpawnVector3HookDelegate(IntPtr posPtr);

        private const int BubbleSpawnSlotCount = 4;

        private static readonly string[] BubbleSpawnSlotClass =
        {
            "XDTDataAndProtocol.ProtocolService.Bubble.BubbleProtocolManager",
            "XDTDataAndProtocol.ProtocolService.Bubble.BubbleProtocolManager",
            "XDTDataAndProtocol.ProtocolService.ActivityEvent.ActivityEventProtocolManager",
            "XDTDataAndProtocol.ProtocolService.HideAndSeek.HideAndSeekProtocolManager",
        };

        private static readonly string[] BubbleSpawnSlotMethod =
        {
            "CreateBubble",
            "CreateVisitBubble",
            "CreateActivityBubble",
            "CreateHideAndSeekBubble",
        };

        private static readonly MonoMod.RuntimeDetour.NativeDetour[] bubbleSpawnDetour =
            new MonoMod.RuntimeDetour.NativeDetour[BubbleSpawnSlotCount];
        private static readonly BubbleSpawnVector3HookDelegate[] bubbleSpawnKeepAlive =
            new BubbleSpawnVector3HookDelegate[BubbleSpawnSlotCount];      // anti-GC
        private static readonly BubbleSpawnVector3HookDelegate[] bubbleSpawnTrampoline =
            new BubbleSpawnVector3HookDelegate[BubbleSpawnSlotCount];
        private readonly bool[] bubbleSpawnSlotFailed = new bool[BubbleSpawnSlotCount];
        private float bubbleSpawnNextInstallAttemptAt = -999f;
        private const float BubbleSpawnInstallRetrySeconds = 5f;

        // Player-position cache: written every frame in OnUpdate (main thread), read by the
        // native bodies (same thread). armed=false → bodies pass the original coords through.
        private static bool bubbleSpawnRewriteArmed;
        private static float bubbleSpawnPlayerX;
        private static float bubbleSpawnPlayerY;
        private static float bubbleSpawnPlayerZ;

        // GUI: retry the install immediately when the toggle is flipped on.
        private void RequestBubbleSpawnRewriteImmediateInstall()
        {
            this.bubbleSpawnNextInstallAttemptAt = -999f;
        }

        // Fixed per-slot bodies (no closures), EventSlotBody* style.
        private static void BubbleSpawnBody0(IntPtr p) => BubbleSpawnRoute(0, p);
        private static void BubbleSpawnBody1(IntPtr p) => BubbleSpawnRoute(1, p);
        private static void BubbleSpawnBody2(IntPtr p) => BubbleSpawnRoute(2, p);
        private static void BubbleSpawnBody3(IntPtr p) => BubbleSpawnRoute(3, p);

        private static readonly BubbleSpawnVector3HookDelegate[] BubbleSpawnBodies =
        {
            BubbleSpawnBody0, BubbleSpawnBody1, BubbleSpawnBody2, BubbleSpawnBody3,
        };

        // Native detour body. Boundary rules: no allocation, no throw, no Mono/Unity calls —
        // only static reads and a 12-byte write into the caller's stack copy of the Vector3.
        private static unsafe void BubbleSpawnRoute(int slot, IntPtr posPtr)
        {
            try
            {
                if (bubbleSpawnRewriteArmed && posPtr != IntPtr.Zero)
                {
                    float* v = (float*)posPtr;
                    v[0] = bubbleSpawnPlayerX;
                    v[1] = bubbleSpawnPlayerY;
                    v[2] = bubbleSpawnPlayerZ;
                }
            }
            catch
            {
            }

            BubbleSpawnVector3HookDelegate orig = bubbleSpawnTrampoline[slot];
            if (orig != null)
            {
                orig(posPtr); // disabled / no player → original coordinates untouched
            }
        }

        private void ProcessBubbleSpawnAtPlayerOnUpdate()
        {
            if (!this.bubbleSpawnAtPlayerEnabled)
            {
                bubbleSpawnRewriteArmed = false; // detours stay installed but become pass-through
                return;
            }

            // Skeleton-first player position (transform.root is the SHIP while sea-fishing).
            if (this.TryGetLocalPlayerPosition(out Vector3 pos))
            {
                bubbleSpawnPlayerX = pos.x;
                bubbleSpawnPlayerY = pos.y + BubbleSpawnHeightOffset; // shared 0.35f lift
                bubbleSpawnPlayerZ = pos.z;
                bubbleSpawnRewriteArmed = true;
            }
            else
            {
                bubbleSpawnRewriteArmed = false; // loading/lobby — don't rewrite
            }

            this.EnsureBubbleSpawnDetoursInstalled();
        }

        private void EnsureBubbleSpawnDetoursInstalled()
        {
            if (Time.unscaledTime < this.bubbleSpawnNextInstallAttemptAt)
            {
                return;
            }
            this.bubbleSpawnNextInstallAttemptAt = Time.unscaledTime + BubbleSpawnInstallRetrySeconds;

            bool anyPending = false;
            for (int i = 0; i < BubbleSpawnSlotCount; i++)
            {
                if (bubbleSpawnDetour[i] == null && !this.bubbleSpawnSlotFailed[i])
                {
                    anyPending = true;
                    break;
                }
            }
            if (!anyPending)
            {
                return;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return; // AuraMono not up yet — retry on a later frame
            }

            for (int slot = 0; slot < BubbleSpawnSlotCount; slot++)
            {
                if (bubbleSpawnDetour[slot] != null || this.bubbleSpawnSlotFailed[slot])
                {
                    continue;
                }

                try
                {
                    IntPtr cls = this.FindAuraMonoClassByFullName(BubbleSpawnSlotClass[slot]);
                    if (cls == IntPtr.Zero)
                    {
                        continue; // image not loaded yet — leave the slot pending, retry later
                    }

                    // Exact 1-arg arity: on BubbleProtocolManager this selects the Vector3 SENDER
                    // and never the 4-arg render overload (that one belongs to detour B).
                    IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, BubbleSpawnSlotMethod[slot], 1);
                    if (method == IntPtr.Zero)
                    {
                        this.bubbleSpawnSlotFailed[slot] = true;
                        ModLogger.Msg("[BubbleSpawn] " + BubbleSpawnSlotMethod[slot] + "(1-arg) not found");
                        continue;
                    }

                    IntPtr nativePtr = this.CompileBubbleDetourTarget(method);
                    if (nativePtr == IntPtr.Zero)
                    {
                        this.bubbleSpawnSlotFailed[slot] = true;
                        ModLogger.Msg("[BubbleSpawn] compile failed for " + BubbleSpawnSlotMethod[slot]);
                        continue;
                    }

                    bubbleSpawnKeepAlive[slot] = BubbleSpawnBodies[slot];
                    bubbleSpawnDetour[slot] = new MonoMod.RuntimeDetour.NativeDetour(
                        nativePtr, bubbleSpawnKeepAlive[slot]);
                    bubbleSpawnTrampoline[slot] =
                        bubbleSpawnDetour[slot].GenerateTrampoline<BubbleSpawnVector3HookDelegate>();
                    if (bubbleSpawnTrampoline[slot] == null)
                    {
                        // Without the trampoline the game would stop sending this request type — revert.
                        try { bubbleSpawnDetour[slot].Undo(); } catch { }
                        bubbleSpawnDetour[slot] = null;
                        bubbleSpawnKeepAlive[slot] = null;
                        this.bubbleSpawnSlotFailed[slot] = true;
                        ModLogger.Msg("[BubbleSpawn] trampoline unavailable for "
                            + BubbleSpawnSlotMethod[slot] + "; detour reverted");
                        continue;
                    }

                    ModLogger.Msg("[BubbleSpawn] detour installed: " + BubbleSpawnSlotMethod[slot]
                        + " @0x" + nativePtr.ToInt64().ToString("X"));
                }
                catch (Exception ex)
                {
                    this.bubbleSpawnSlotFailed[slot] = true; // never crash-loop
                    ModLogger.Msg("[BubbleSpawn] install " + BubbleSpawnSlotMethod[slot]
                        + " failed: " + ex.Message);
                }
            }
        }
    }
}
