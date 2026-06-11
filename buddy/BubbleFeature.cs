using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        // Bubble position is server-authoritative (the server spawns bubble ECS entities and syncs them
        // to the client via BubbleSyncSystem; daily/fishing-ship bubbles have no client-side request to
        // intercept, and the client senders CreateActivityBubble/CreateBubble are just requests). The
        // native-detour approaches to reposition the game's own bubbles either never fired (hook on the
        // unmanaged thunk) or hard-crashed (detour on the JITed body), so they were removed. The only
        // safe thing the mod can do is invoke CreateActivityBubble/CreateBubble ITSELF at the player
        // position — which is what Fast Bubble Gen and the spawn-bubble hotkey do.
        private const float BubbleSpawnHeightOffset = 0.35f;

        private bool bubbleMonoResolverLogged = false;
        private IntPtr bubbleMonoCreateActivityBubbleMethodPtr = IntPtr.Zero;
        private IntPtr bubbleMonoCreateBubbleMethodPtr = IntPtr.Zero;
        private IntPtr bubbleMonoActivityEventTimeCounterFieldPtr = IntPtr.Zero;
        private float bubbleSpawnRateAccumulator = 0f;
        private float nextBubbleFeaturePatchAttemptAt = -999f;
        private const float BubbleFeaturePatchRetryIntervalSeconds = 5f;

        private void InitializeBubbleFeature()
        {
            this.nextBubbleFeaturePatchAttemptAt = -999f;
        }

        // Resolve the Mono spawn methods sooner (called when Fast Bubble Gen is toggled on).
        private void RequestBubbleFeatureImmediateRetry()
        {
            this.nextBubbleFeaturePatchAttemptAt = -999f;
        }

        private void ProcessBubbleFeatureOnUpdate()
        {
            // Fast Bubble Gen: spawn bubbles at the player on a custom rate.
            this.ProcessBubbleFeatureMonoRuntimeEffects();

            // Resolve the Mono spawn methods once (needed by Fast Bubble Gen + the spawn-bubble hotkey).
            if (this.bubbleMonoCreateActivityBubbleMethodPtr != IntPtr.Zero
                || this.bubbleMonoCreateBubbleMethodPtr != IntPtr.Zero)
            {
                return;
            }

            if (Time.unscaledTime < this.nextBubbleFeaturePatchAttemptAt)
            {
                return;
            }

            this.nextBubbleFeaturePatchAttemptAt = Time.unscaledTime + BubbleFeaturePatchRetryIntervalSeconds;

            try
            {
                this.TryResolveBubbleFeatureMono();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[BubbleFeature] Mono resolve attempt failed: " + ex.Message);
            }
        }

        private void TryResolveBubbleFeatureMono()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return;
            }

            try
            {
                if (this.bubbleMonoCreateActivityBubbleMethodPtr == IntPtr.Zero)
                {
                    IntPtr activityProtocolClass = this.FindAuraMonoClassByFullName(
                        "XDTDataAndProtocol.ProtocolService.ActivityEvent.ActivityEventProtocolManager");
                    if (activityProtocolClass != IntPtr.Zero)
                    {
                        this.bubbleMonoCreateActivityBubbleMethodPtr = this.FindAuraMonoMethodOnHierarchy(
                            activityProtocolClass,
                            "CreateActivityBubble",
                            1);
                    }
                }

                if (this.bubbleMonoCreateBubbleMethodPtr == IntPtr.Zero)
                {
                    IntPtr bubbleProtocolClass = this.FindAuraMonoClassByFullName(
                        "XDTDataAndProtocol.ProtocolService.Bubble.BubbleProtocolManager");
                    if (bubbleProtocolClass != IntPtr.Zero)
                    {
                        this.bubbleMonoCreateBubbleMethodPtr = this.FindAuraMonoMethodOnHierarchy(
                            bubbleProtocolClass,
                            "CreateBubble",
                            1);
                    }
                }

                if (this.bubbleMonoActivityEventTimeCounterFieldPtr == IntPtr.Zero)
                {
                    IntPtr moduleClass = this.FindAuraMonoClassByFullName(
                        "XDTLevelAndEntity.Game.Module.ActivityEvent.ActivityEventModule");
                    if (moduleClass != IntPtr.Zero)
                    {
                        this.bubbleMonoActivityEventTimeCounterFieldPtr = this.FindAuraMonoFieldOnHierarchy(
                            moduleClass,
                            "_timeCounter");
                        if (this.bubbleMonoActivityEventTimeCounterFieldPtr == IntPtr.Zero)
                        {
                            this.bubbleMonoActivityEventTimeCounterFieldPtr = this.FindAuraMonoFieldOnHierarchy(
                                moduleClass,
                                "timeCounter");
                        }
                    }
                }

                if (!this.bubbleMonoResolverLogged
                    && (this.bubbleMonoCreateActivityBubbleMethodPtr != IntPtr.Zero
                        || this.bubbleMonoCreateBubbleMethodPtr != IntPtr.Zero))
                {
                    this.bubbleMonoResolverLogged = true;
                    ModLogger.Msg(string.Format(
                        "[BubbleFeature] Mono resolver ready: CreateActivityBubble={0}, CreateBubble={1}, ActivityEvent._timeCounter={2}",
                        this.bubbleMonoCreateActivityBubbleMethodPtr != IntPtr.Zero,
                        this.bubbleMonoCreateBubbleMethodPtr != IntPtr.Zero,
                        this.bubbleMonoActivityEventTimeCounterFieldPtr != IntPtr.Zero));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[BubbleFeature] Mono resolver failed: " + ex.Message);
            }
        }

        private void ProcessBubbleFeatureMonoRuntimeEffects()
        {
            if (!this.fastBubbleGenEnabled)
            {
                return;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return;
            }

            this.TryProcessBubbleSpawnRateViaMono();
        }

        private unsafe void TryProcessBubbleSpawnRateViaMono()
        {
            float ratePerMinute = Mathf.Clamp(this.bubbleBubblesPerMinute, 0f, 100f);
            if (ratePerMinute <= 0f || this.bubbleMonoCreateActivityBubbleMethodPtr == IntPtr.Zero)
            {
                return;
            }

            // Suppress the game's activity-event auto-spawn timer so it doesn't also emit bubbles at
            // random positions while we drive our own at the player. (Value-type write — GC-safe.)
            if (this.bubbleMonoActivityEventTimeCounterFieldPtr != IntPtr.Zero
                && this.TryResolveAuraMonoModule(
                    "XDTLevelAndEntity.Game.Module.ActivityEvent.ActivityEventModule",
                    out IntPtr moduleObj)
                && moduleObj != IntPtr.Zero
                && auraMonoFieldSetValue != null)
            {
                float zero = 0f;
                auraMonoFieldSetValue(moduleObj, this.bubbleMonoActivityEventTimeCounterFieldPtr, (IntPtr)(&zero));
            }

            float deltaSeconds = Mathf.Max(Time.deltaTime, 0f);
            this.bubbleSpawnRateAccumulator += ratePerMinute / 60f * deltaSeconds;

            while (this.bubbleSpawnRateAccumulator >= 1f)
            {
                if (!this.TryInvokeMonoCreateActivityBubbleAt(BubbleFeaturePatches.GetSpawnPositionAtPlayer()))
                {
                    break;
                }

                this.bubbleSpawnRateAccumulator -= 1f;
            }
        }

        private unsafe bool TryInvokeMonoCreateActivityBubbleAt(Vector3 spawn)
        {
            return this.TryInvokeMonoVector3BubbleMethod(this.bubbleMonoCreateActivityBubbleMethodPtr, spawn);
        }

        private unsafe bool TryInvokeMonoCreateBubbleAt(Vector3 spawn)
        {
            return this.TryInvokeMonoVector3BubbleMethod(this.bubbleMonoCreateBubbleMethodPtr, spawn);
        }

        private unsafe bool TryInvokeMonoVector3BubbleMethod(IntPtr methodPtr, Vector3 spawn)
        {
            if (methodPtr == IntPtr.Zero
                || auraMonoRuntimeInvoke == null
                || !this.AttachAuraMonoThread()
                || spawn == Vector3.zero)
            {
                return false;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&spawn);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                return exc == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Keybind: spawn one bubble at the player.</summary>
        public bool TrySpawnBubbleOnKeybind()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (this.bubbleMonoCreateActivityBubbleMethodPtr == IntPtr.Zero
                && this.bubbleMonoCreateBubbleMethodPtr == IntPtr.Zero)
            {
                this.TryResolveBubbleFeatureMono();
            }

            Vector3 spawn = BubbleFeaturePatches.GetSpawnPositionAtPlayer();
            if (spawn == Vector3.zero)
            {
                return false;
            }

            if (this.TryInvokeMonoCreateActivityBubbleAt(spawn))
            {
                return true;
            }

            return this.TryInvokeMonoCreateBubbleAt(spawn);
        }

        // Player spawn position (always — there is no longer a "spawn at player" toggle; Fast Bubble Gen
        // and the hotkey always spawn at the player).
        public static class BubbleFeaturePatches
        {
            public static Vector3 GetSpawnPositionAtPlayer()
            {
                if (Instance == null)
                {
                    return Vector3.zero;
                }

                GameObject player = Instance.GetPlayerObject();
                if (player == null)
                {
                    return Vector3.zero;
                }

                Vector3 pos = player.transform.position;
                return pos + Vector3.up * BubbleSpawnHeightOffset;
            }
        }
    }
}
