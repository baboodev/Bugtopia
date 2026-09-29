using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Rebuild stale BRG batches after a world load.
    //
    // Game bug (verified live 2026-09-29): BRG entities created in the world-load frame (the first
    // bulk of home plants/furniture, all sharing one _addBrgFrame) keep drawing the low-poly
    // placeholder mesh even after their streamed full mesh has loaded — the managed side reports
    // _meshStreamingListConfig set, and re-sending it (BatchRenderer.SetMeshStreamingList, even
    // with a different config first) changes nothing on the native batch. Identical objects
    // created a few frames later are fine. Recreating the native batch (BatchRenderer.DisRender +
    // Render — the same path the game itself takes when a batch's block goes dirty) builds it from
    // the now-loaded meshes and fixes it immediately.
    //
    // One pass per world load, a few seconds after the world settles, over every entity whose
    // streamed mesh is loaded; budgeted per frame. The queue holds uids only — never a MonoObject*
    // across frames.
    //
    // The 2026-09-30 game update fixed this in the game itself (BatchEntity.OnMeshLoadSuccess now
    // always recreates the batch via _DoRender), and models load correctly without this pass — so it
    // is OFF by default and kept only as a manual fallback.
    public partial class HeartopiaComplete
    {
        internal bool gameLodBrgRebuildEnabled = false;

        private const int GameLodBrgRebuildPerFrame = 25;
        private const float GameLodBrgRebuildDelaySeconds = 5f;

        private bool gameLodBrgRebuildWasSettled = false;
        private bool gameLodBrgRebuildArmed = false;
        private float gameLodBrgRebuildStartAt = 0f;
        private readonly List<uint> gameLodBrgRebuildQueue = new List<uint>();
        private int gameLodBrgRebuildCursor = 0;
        private int gameLodBrgRebuildEntities = 0;
        private int gameLodBrgRebuildBatches = 0;
        private FeatureBreakerState gameLodBrgRebuildBreaker;
        internal string gameLodBrgRebuildStatus = "";

        private IntPtr gameLodBrgGetRenderingMethod = IntPtr.Zero;

        // Manual trigger (Game LOD page button): start a pass on the next frame.
        internal void RequestGameLodBrgRebuild()
        {
            this.gameLodBrgRebuildQueue.Clear();
            this.gameLodBrgRebuildCursor = 0;
            this.gameLodBrgRebuildArmed = true;
            this.gameLodBrgRebuildStartAt = 0f;
            this.gameLodBrgRebuildStatus = this.L("Queued…");
        }

        // Called every frame from ProcessGameLodFeatureOnUpdate, before its all-off early-out.
        private void GameLodTickBrgRebuild()
        {
            float now = Time.unscaledTime;
            bool settled = this.CurrentWorldStage >= WorldStage.WorldSettled;
            if (!settled)
            {
                // A new load: drop any unfinished pass (its uids belong to the old world).
                if (this.gameLodBrgRebuildWasSettled || this.gameLodBrgRebuildQueue.Count > 0)
                {
                    this.gameLodBrgRebuildQueue.Clear();
                    this.gameLodBrgRebuildCursor = 0;
                }
                this.gameLodBrgRebuildWasSettled = false;
                return;
            }

            if (!this.gameLodBrgRebuildWasSettled)
            {
                this.gameLodBrgRebuildWasSettled = true;
                if (this.gameLodBrgRebuildEnabled)
                {
                    this.gameLodBrgRebuildArmed = true;
                    this.gameLodBrgRebuildStartAt = now + GameLodBrgRebuildDelaySeconds;
                    this.gameLodBrgRebuildStatus = this.L("Waiting for the world to finish loading…");
                }
            }

            if (!this.gameLodBrgRebuildArmed && this.gameLodBrgRebuildQueue.Count == 0)
            {
                return;
            }
            if (!this.gameLodBrgRebuildBreaker.ShouldRun(now) || !this.IsGameLodAuraReady())
            {
                return;
            }

            try
            {
                if (this.gameLodBrgRebuildArmed)
                {
                    if (now < this.gameLodBrgRebuildStartAt)
                    {
                        return;
                    }
                    this.gameLodBrgRebuildArmed = false;
                    if (!this.TryGameLodBrgRebuildSnapshot(out string snapStatus))
                    {
                        this.gameLodBrgRebuildStatus = snapStatus;
                        ModLogger.Msg("[GameLod] BRG rebuild: " + snapStatus);
                        this.gameLodBrgRebuildBreaker.Success();
                        return;
                    }
                }

                this.GameLodBrgRebuildStep();
                this.gameLodBrgRebuildBreaker.Success();
            }
            catch (Exception ex)
            {
                this.gameLodBrgRebuildQueue.Clear();
                this.gameLodBrgRebuildCursor = 0;
                this.gameLodBrgRebuildStatus = "error: " + ex.Message;
                this.gameLodBrgRebuildBreaker.Failure("GameLod BrgRebuild", ex, now);
            }
        }

        // Collect the uids of every rendering whose streamed full mesh is loaded.
        private bool TryGameLodBrgRebuildSnapshot(out string status)
        {
            this.gameLodBrgRebuildQueue.Clear();
            this.gameLodBrgRebuildCursor = 0;
            this.gameLodBrgRebuildEntities = 0;
            this.gameLodBrgRebuildBatches = 0;
            if (!AuraMonoPinningAvailable)
            {
                status = "pinning unavailable";
                return false;
            }

            if (!this.TryResolveGameLodService("XDTLevelAndEntity.BaseSystem.RenderingManager.IRenderingSystem",
                out IntPtr renderSystemObj, out uint renderPin, out status))
            {
                return false;
            }

            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryReadAuraMonoObjectField(renderSystemObj, out IntPtr mapObj, "_renderingMap") || mapObj == IntPtr.Zero)
                {
                    status = "_renderingMap unavailable";
                    return false;
                }
                uint mapPin = AuraMonoPinNew(mapObj);
                try
                {
                    if (!this.TryGameLodInvokeObject(mapObj, "get_Values", out IntPtr valuesObj) || valuesObj == IntPtr.Zero)
                    {
                        status = "_renderingMap.Values unavailable";
                        return false;
                    }
                    List<IntPtr> renderings = new List<IntPtr>();
                    if (!this.TryEnumerateAuraMonoCollectionItems(valuesObj, renderings, pins, 50000))
                    {
                        status = "rendering enumeration failed";
                        return false;
                    }

                    for (int i = 0; i < renderings.Count; i++)
                    {
                        IntPtr rendering = renderings[i];
                        if (rendering == IntPtr.Zero
                            || !this.TryReadAuraMonoObjectField(rendering, out IntPtr cfg, "_meshStreamingListConfig")
                            || cfg == IntPtr.Zero)
                        {
                            continue;
                        }
                        if (this.TryGameLodInvokeObject(rendering, "get_Uid", out IntPtr uidBoxed)
                            && this.TryUnboxMonoUInt32(uidBoxed, out uint uid))
                        {
                            this.gameLodBrgRebuildQueue.Add(uid);
                        }
                    }
                }
                finally
                {
                    AuraMonoPinFree(mapPin);
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
                AuraMonoPinFree(renderPin);
            }

            status = this.LF("Rebuilding {0} objects…", this.gameLodBrgRebuildQueue.Count);
            this.gameLodBrgRebuildStatus = status;
            this.GameLodLogOnce("BRG rebuild: queued " + this.gameLodBrgRebuildQueue.Count + " streamed renderings");
            return true;
        }

        private unsafe void GameLodBrgRebuildStep()
        {
            if (this.gameLodBrgRebuildCursor >= this.gameLodBrgRebuildQueue.Count)
            {
                return;
            }

            if (!this.TryResolveGameLodService("XDTLevelAndEntity.BaseSystem.RenderingManager.IRenderingSystem",
                out IntPtr renderSystemObj, out uint renderPin, out string status))
            {
                this.gameLodBrgRebuildStatus = status;
                this.gameLodBrgRebuildQueue.Clear();
                this.gameLodBrgRebuildCursor = 0;
                return;
            }

            try
            {
                if (this.gameLodBrgGetRenderingMethod == IntPtr.Zero)
                {
                    IntPtr rsClass = auraMonoObjectGetClass(renderSystemObj);
                    this.gameLodBrgGetRenderingMethod = rsClass != IntPtr.Zero
                        ? this.FindAuraMonoMethodOnHierarchy(rsClass, "GetRendering", 1) : IntPtr.Zero;
                    if (this.gameLodBrgGetRenderingMethod == IntPtr.Zero)
                    {
                        this.gameLodBrgRebuildStatus = "GetRendering missing";
                        this.gameLodBrgRebuildQueue.Clear();
                        this.gameLodBrgRebuildCursor = 0;
                        return;
                    }
                }

                IntPtr* args = stackalloc IntPtr[1];
                uint uid = 0U;
                args[0] = (IntPtr)(&uid);
                int end = Math.Min(this.gameLodBrgRebuildQueue.Count, this.gameLodBrgRebuildCursor + GameLodBrgRebuildPerFrame);
                for (; this.gameLodBrgRebuildCursor < end; this.gameLodBrgRebuildCursor++)
                {
                    uid = this.gameLodBrgRebuildQueue[this.gameLodBrgRebuildCursor];
                    if (!TryAuraInvoke(this.gameLodBrgGetRenderingMethod, renderSystemObj, (IntPtr)args,
                            out IntPtr rendering, out _) || rendering == IntPtr.Zero)
                    {
                        continue; // gone since the snapshot
                    }

                    int rebuilt = this.GameLodBrgRebuildOne(rendering);
                    if (rebuilt > 0)
                    {
                        this.gameLodBrgRebuildEntities++;
                        this.gameLodBrgRebuildBatches += rebuilt;
                    }
                }
            }
            finally
            {
                AuraMonoPinFree(renderPin);
            }

            if (this.gameLodBrgRebuildCursor >= this.gameLodBrgRebuildQueue.Count)
            {
                this.gameLodBrgRebuildStatus = this.LF("Rebuilt {0} objects ({1} batches).",
                    this.gameLodBrgRebuildEntities, this.gameLodBrgRebuildBatches);
                ModLogger.Msg("[GameLod] BRG rebuild done: " + this.gameLodBrgRebuildEntities + " objects, "
                    + this.gameLodBrgRebuildBatches + " batches");
                this.gameLodBrgRebuildQueue.Clear();
                this.gameLodBrgRebuildCursor = 0;
            }
            else
            {
                this.gameLodBrgRebuildStatus = this.LF("Rebuilding {0} objects…",
                    this.gameLodBrgRebuildQueue.Count - this.gameLodBrgRebuildCursor);
            }
        }

        // Recreate every live native batch of one rendering, then hand it the streamed mesh list.
        // Returns the number of batches rebuilt.
        private unsafe int GameLodBrgRebuildOne(IntPtr rendering)
        {
            uint entityPin = AuraMonoPinNew(rendering);
            try
            {
                if (!this.TryReadAuraMonoObjectField(rendering, out IntPtr cfg, "_meshStreamingListConfig") || cfg == IntPtr.Zero)
                {
                    return 0;
                }
                uint cfgPin = AuraMonoPinNew(cfg);
                try
                {
                    if (!this.TryGameLodInvokeObject(rendering, "get_renderer", out IntPtr renderer) || renderer == IntPtr.Zero)
                    {
                        return 0;
                    }
                    uint rendererPin = AuraMonoPinNew(renderer);
                    List<uint> batchPins = new List<uint>();
                    try
                    {
                        if (!this.TryReadAuraMonoObjectField(renderer, out IntPtr batchesObj, "_batches") || batchesObj == IntPtr.Zero
                            || !this.TryGameLodInvokeObject(batchesObj, "get_Values", out IntPtr batchValues) || batchValues == IntPtr.Zero)
                        {
                            return 0;
                        }
                        List<IntPtr> batches = new List<IntPtr>();
                        if (!this.TryEnumerateAuraMonoCollectionItems(batchValues, batches, batchPins, 16))
                        {
                            return 0;
                        }

                        int rebuilt = 0;
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = cfg;
                        foreach (IntPtr batch in batches)
                        {
                            // Only batches that are drawing now: a zero token means "not rendered",
                            // and Render() would create one the game never asked for.
                            if (batch == IntPtr.Zero
                                || !this.TryReadAuraMonoObjectField(batch, out IntPtr handleBoxed, "_handle")
                                || !this.TryUnboxMonoInt32(handleBoxed, out int token) || token == 0)
                            {
                                continue;
                            }

                            IntPtr batchClass = auraMonoObjectGetClass(batch);
                            IntPtr disRender = this.FindAuraMonoMethodOnHierarchy(batchClass, "DisRender", 0);
                            IntPtr render = this.FindAuraMonoMethodOnHierarchy(batchClass, "Render", 0);
                            IntPtr setMeshList = this.FindAuraMonoMethodOnHierarchy(batchClass, "SetMeshStreamingList", 1);
                            if (disRender == IntPtr.Zero || render == IntPtr.Zero || setMeshList == IntPtr.Zero)
                            {
                                continue;
                            }

                            if (!TryAuraInvoke(disRender, batch, IntPtr.Zero, out _, out _)
                                || !TryAuraInvoke(render, batch, IntPtr.Zero, out _, out _))
                            {
                                continue;
                            }

                            TryAuraInvoke(setMeshList, batch, (IntPtr)args, out _, out _);
                            rebuilt++;
                        }
                        return rebuilt;
                    }
                    finally
                    {
                        FreeAuraMonoPins(batchPins);
                        AuraMonoPinFree(rendererPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(cfgPin);
                }
            }
            finally
            {
                AuraMonoPinFree(entityPin);
            }
        }

        internal void SetGameLodBrgRebuildEnabled(bool value)
        {
            if (this.gameLodBrgRebuildEnabled == value)
            {
                return;
            }
            this.gameLodBrgRebuildEnabled = value;
            FeatureLog.Toggle("GameLod", value, "BrgRebuild");
            if (!value)
            {
                this.gameLodBrgRebuildArmed = false;
                this.gameLodBrgRebuildQueue.Clear();
                this.gameLodBrgRebuildCursor = 0;
                this.gameLodBrgRebuildStatus = "";
            }
        }
    }
}
