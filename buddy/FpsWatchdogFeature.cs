using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // FPS Watchdog — frame-time monitor that writes a log line every time the frame rate degrades.
    //
    // WHY IT EXISTS: "the game stutters" arrives as a sentence, never as numbers, and by the time
    // anyone looks the frame is long gone. Half the FPS regressions this mod has shipped
    // (per-trigger _moduleDic rescans, the radius scan freeze, synchronous breadcrumb IO in a
    // per-entity loop) were only found by bisecting features by hand. This turns that into a log
    // line that already names the magnitude, the world state AND how much of the frame the mod
    // itself spent — so the first question ("is it us?") is answered before anyone opens a profiler.
    //
    // TWO INDEPENDENT DETECTORS, because the two failures look nothing alike:
    //   * HITCH  — one frame longer than FpsWatchHitchMs. A stall: an asset load, a GC pause, a
    //              blocking IO call, a scan that walked too many entities in one tick.
    //   * DROP   — the smoothed rate sitting under a floor for >= EnterSustainSeconds. A regime
    //              change: a crowded map, a LOD override that backfired, an automation loop that
    //              is now costing real time every frame.
    // A drop logs on ENTRY as well as on recovery, on purpose: if the process dies inside the drop
    // the recovery line never comes, and the entry line is the only evidence left.
    //
    // ATTRIBUTION comes free from the phase markers OnUpdate already carries. Breadcrumbs.Phase()
    // is called ~24x per OnUpdate (plus the dc.* markers nested inside Daily Claims); hooking its
    // observer turns that existing marker set into a per-section timer at the cost of one
    // Stopwatch.GetTimestamp() per marker (~25 ns — about 0.0006 ms of a 16 ms frame). Caveat worth
    // knowing when reading the output: a section's time is measured from ITS marker to the NEXT
    // one, so it includes that marker's own breadcrumb file write.
    //
    // COST WHEN ENABLED: a handful of float ops, one timestamp pair, and one array clear per frame.
    // COST WHEN DISABLED: two bool tests per frame; the phase observer is unhooked, so the markers
    // go back to a single null check.
    //
    // OUTPUT: every event goes to the mod log unconditionally (never toast-only, never behind a
    // MasterLog flag) AND is appended to %LocalLow%/Bugtopia/Logs/fps-watchdog.log, which survives
    // the loader log's rotation and is the file to hand over when reporting a stutter.
    // MasterLogFpsWatchdog adds the per-frame detail (full section table on every hitch) on top.
    //
    // RATE LIMITING IS LOAD-BEARING: hitches arrive in bursts (streaming, a shader warm-up, a
    // GC storm), and a watchdog that writes a line per hitch turns a 200 ms stall into a 2 s one.
    // At most one hitch line every HitchLogIntervalSeconds; everything suppressed in between is
    // counted and reported on the next line that does get through.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // Verbose tracing (Settings -> Logging). OFF by default: the watchdog's own event lines are
        // NOT gated on it — this only adds the per-hitch section table and the per-window detail.
        internal static bool MasterLogFpsWatchdog = false;

        private const float FpsWatchEnterSustainSeconds = 0.5f;   // below the floor this long -> drop
        private const float FpsWatchExitSustainSeconds = 1.0f;    // back above it this long -> recovered
        private const float FpsWatchExitHysteresis = 1.12f;       // recover at 112% of the floor
        private const float FpsWatchRelativeFloor = 0.6f;         // ...or at 60% of the session baseline
        private const float FpsWatchHitchLogIntervalSeconds = 2f;
        private const float FpsWatchOngoingLogIntervalSeconds = 10f;
        private const float FpsWatchSummaryIntervalSeconds = 60f;
        private const float FpsWatchQuietSummaryIntervalSeconds = 300f;
        private const float FpsWatchResumeGraceSeconds = 3f;      // after a loading screen / world entry
        private const float FpsWatchMaxSampleSeconds = 5f;        // longer than this is a load, not a hitch
        private const float FpsWatchBaselineAlpha = 0.01f;        // ~100-frame window, drop frames excluded
        private const float FpsWatchShortAlpha = 0.12f;           // ~8-frame window, the drop detector's input
        private const long FpsWatchFileMaxBytes = 4L * 1024L * 1024L;

        // Settings -> Main -> PERFORMANCE. Persisted; see the fpsWatchdogDisabled note in
        // HeartopiaComplete.ConfigTypes.cs for why the SAVED field is the inverse of this one.
        private bool fpsWatchdogEnabled = true;
        private int fpsWatchdogHitchMs = 120;   // 120 ms ~= 8 fps for one frame: visible as a jolt
        private int fpsWatchdogLowFps = 30;

        // ---- per-frame sampling state ----------------------------------------------------------
        private bool fpsWatchdogPrimed;              // false until one clean sample has been taken
        private float fpsWatchdogBaselineFps;        // slow EMA, frozen while a drop is in progress
        private float fpsWatchdogShortFps;           // fast EMA, the drop detector reads this
        private float fpsWatchdogResumeGraceUntil;   // no drop/hitch verdicts until this passes
        private bool fpsWatchdogWasSuppressed = true;

        // ---- drop state machine ----------------------------------------------------------------
        private bool fpsWatchdogInDrop;
        private float fpsWatchdogBelowSince = -1f;
        private float fpsWatchdogAboveSince = -1f;
        private float fpsWatchdogDropStartedAt;
        private float fpsWatchdogDropNextOngoingAt;
        private float fpsWatchdogDropBaselineAtEntry;
        private float fpsWatchdogDropSeconds;
        private int fpsWatchdogDropFrames;
        private float fpsWatchdogDropMinFps = float.MaxValue;
        private float fpsWatchdogDropWorstFrameMs;
        private float fpsWatchdogDropModMsTotal;
        private float fpsWatchdogDropModMsPeak;

        // ---- hitch rate limiter -----------------------------------------------------------------
        private float fpsWatchdogNextHitchLogAt;
        private int fpsWatchdogSuppressedHitches;

        // ---- summary window ---------------------------------------------------------------------
        private float fpsWatchdogWindowStartedAt;
        private float fpsWatchdogWindowSeconds;
        private int fpsWatchdogWindowFrames;
        private int fpsWatchdogWindowHitches;
        private int fpsWatchdogWindowDrops;
        private float fpsWatchdogWindowDropSeconds;
        private float fpsWatchdogWindowWorstFrameMs;
        private float fpsWatchdogWindowModMsTotal;
        private float fpsWatchdogWindowModMsPeak;
        private float fpsWatchdogLastQuietSummaryAt;

        // Frame-time histogram, for the 1% low without keeping a sample buffer. Bounds are the
        // INCLUSIVE upper edge of each bucket in ms, deliberately dense across 10..36 ms (that is
        // 28..100 fps, where every reading anyone argues about lives).
        private static readonly float[] FpsWatchdogBucketMs =
        {
            2f, 4f, 6f, 8f, 10f, 11f, 12f, 13f, 14f, 15f, 16f, 17f, 18f, 20f, 22f,
            25f, 28f, 32f, 36f, 42f, 50f, 60f, 75f, 100f, 150f, 250f, 500f, 1000f, 2000f, float.MaxValue
        };
        private readonly int[] fpsWatchdogHistogram = new int[FpsWatchdogBucketMs.Length];

        // ---- mod self-cost ----------------------------------------------------------------------
        private long fpsWatchdogFrameStartTicks;
        private bool fpsWatchdogFrameOpen;
        private float fpsWatchdogModMs;              // last COMPLETED OnUpdate, in ms

        // ---- section attribution (Breadcrumbs.PhaseObserver) -------------------------------------
        private const int FpsWatchdogMaxSections = 64;
        private readonly string[] fpsWatchdogSectionNames = new string[FpsWatchdogMaxSections];
        private readonly float[] fpsWatchdogSectionFrameMs = new float[FpsWatchdogMaxSections];
        private readonly float[] fpsWatchdogSectionWindowMs = new float[FpsWatchdogMaxSections];
        private readonly float[] fpsWatchdogSectionDropMs = new float[FpsWatchdogMaxSections];
        private int fpsWatchdogSectionCount;
        private int fpsWatchdogSectionActive = -1;
        private long fpsWatchdogSectionStartTicks;
        private int fpsWatchdogSectionMainThreadId = -1;
        private Action<string> fpsWatchdogPhaseObserver;
        private bool fpsWatchdogPhaseHooked;

        // ---- log file ----------------------------------------------------------------------------
        private StreamWriter fpsWatchdogWriter;
        private bool fpsWatchdogWriterFailed;

        private static readonly double FpsWatchdogTicksToMs =
            1000.0 / System.Diagnostics.Stopwatch.Frequency;

        // =========================================================================================
        // Frame hooks. Begin runs at the very top of OnUpdate, End at the very bottom
        // (HeartopiaComplete.cs). End is NOT in a finally: if OnUpdate throws, the frame is simply
        // left unmeasured and the next Begin reopens it — an exception escaping OnUpdate is its own
        // (already logged) problem and must not be masked by watchdog bookkeeping.
        // =========================================================================================
        private void BeginFpsWatchdogFrame()
        {
            if (!this.fpsWatchdogEnabled)
            {
                if (this.fpsWatchdogPhaseHooked)
                {
                    this.UnhookFpsWatchdogPhaseObserver();
                }
                return;
            }

            try
            {
                this.SampleFpsWatchdogFrame();
            }
            catch (Exception ex)
            {
                // A watchdog must never be the thing that breaks the frame it is watching. Returning
                // here matters as much as the unhook: falling through would re-install the observer
                // that was just removed, on the very next line.
                this.fpsWatchdogEnabled = false;
                this.UnhookFpsWatchdogPhaseObserver();
                ModLogger.Warning("[FpsWatch] sampler failed, watchdog disabled for this session: " + ex);
                return;
            }

            if (!this.fpsWatchdogPhaseHooked)
            {
                this.HookFpsWatchdogPhaseObserver();
            }

            Array.Clear(this.fpsWatchdogSectionFrameMs, 0, this.fpsWatchdogSectionCount);
            this.fpsWatchdogSectionActive = -1;
            this.fpsWatchdogFrameStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            this.fpsWatchdogSectionStartTicks = this.fpsWatchdogFrameStartTicks;
            this.fpsWatchdogFrameOpen = true;
        }

        private void EndFpsWatchdogFrame()
        {
            if (!this.fpsWatchdogFrameOpen)
            {
                return;
            }
            this.fpsWatchdogFrameOpen = false;

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            this.fpsWatchdogModMs = (float)((now - this.fpsWatchdogFrameStartTicks) * FpsWatchdogTicksToMs);

            int active = this.fpsWatchdogSectionActive;
            if (active >= 0)
            {
                this.fpsWatchdogSectionFrameMs[active] +=
                    (float)((now - this.fpsWatchdogSectionStartTicks) * FpsWatchdogTicksToMs);
                this.fpsWatchdogSectionActive = -1;
            }

            // Roll this frame's section table into the window (and the drop, while one is open).
            // Only for a frame the sampler actually graded: OnUpdate keeps ticking behind a loading
            // screen, and folding those frames in would hand the summary a "worst section" that
            // describes the load rather than the play session.
            if (this.fpsWatchdogWasSuppressed)
            {
                return;
            }
            bool inDrop = this.fpsWatchdogInDrop;
            for (int i = 0; i < this.fpsWatchdogSectionCount; i++)
            {
                float ms = this.fpsWatchdogSectionFrameMs[i];
                if (ms <= 0f)
                {
                    continue;
                }
                this.fpsWatchdogSectionWindowMs[i] += ms;
                if (inDrop)
                {
                    this.fpsWatchdogSectionDropMs[i] += ms;
                }
            }
        }

        // =========================================================================================
        // The sampler. Runs once per frame, on the PREVIOUS frame's delta.
        // =========================================================================================
        private void SampleFpsWatchdogFrame()
        {
            float now = Time.unscaledTime;

            // Suppression: a loading screen, the login level, or any world transition legitimately
            // freezes the frame for whole seconds. Sampling those would poison the baseline and
            // fire a "drop" on every zone change, so the detector stands down entirely and comes
            // back with a grace period while streaming settles.
            bool suppressed = !this.IsWorldReady || this.IsWorldLoadingScreenVisible;
            if (suppressed)
            {
                this.fpsWatchdogWasSuppressed = true;
                if (this.fpsWatchdogInDrop)
                {
                    // A world change ends the drop rather than "recovering" from it — the numbers
                    // after the transition describe a different world.
                    this.EndFpsWatchdogDrop(now, "world transition");
                }
                return;
            }

            if (this.fpsWatchdogWasSuppressed)
            {
                this.fpsWatchdogWasSuppressed = false;
                this.fpsWatchdogResumeGraceUntil = now + FpsWatchResumeGraceSeconds;
                this.fpsWatchdogBelowSince = -1f;
                this.fpsWatchdogAboveSince = -1f;
                // Re-seed the fast EMA from the BASELINE, not from zero: zero would climb back
                // through the floor over ~30 frames and spend that whole time looking like a drop,
                // leaning on the grace window to not report one. The baseline is kept — it is a
                // property of the machine, and a world change is no reason to relearn it.
                this.fpsWatchdogShortFps = this.fpsWatchdogBaselineFps;
                // First frame back carries the whole loading screen in its delta — never a sample.
                return;
            }

            float dtSeconds = Time.unscaledDeltaTime;
            if (dtSeconds <= 0.0001f || dtSeconds > FpsWatchMaxSampleSeconds)
            {
                // Alt-tab, a breakpoint, a driver reset: not a frame anyone rendered.
                return;
            }

            float frameMs = dtSeconds * 1000f;
            float instantFps = 1000f / frameMs;
            float modMs = this.fpsWatchdogModMs;

            // ---- accumulators ----
            this.fpsWatchdogWindowSeconds += dtSeconds;
            this.fpsWatchdogWindowFrames++;
            this.fpsWatchdogWindowModMsTotal += modMs;
            if (modMs > this.fpsWatchdogWindowModMsPeak)
            {
                this.fpsWatchdogWindowModMsPeak = modMs;
            }
            if (frameMs > this.fpsWatchdogWindowWorstFrameMs)
            {
                this.fpsWatchdogWindowWorstFrameMs = frameMs;
            }
            this.fpsWatchdogHistogram[FpsWatchdogBucketIndex(frameMs)]++;
            if (this.fpsWatchdogWindowStartedAt <= 0f)
            {
                this.fpsWatchdogWindowStartedAt = now;
                this.fpsWatchdogLastQuietSummaryAt = now;
            }

            // ---- smoothing ----
            bool graced = now < this.fpsWatchdogResumeGraceUntil;
            bool hitchFrame = frameMs >= this.FpsWatchdogHitchMsClamped();
            if (!this.fpsWatchdogPrimed)
            {
                this.fpsWatchdogPrimed = true;
                this.fpsWatchdogBaselineFps = instantFps;
                this.fpsWatchdogShortFps = instantFps;
            }
            else
            {
                this.fpsWatchdogShortFps = Mathf.Lerp(this.fpsWatchdogShortFps, instantFps, FpsWatchShortAlpha);
                // The baseline answers "what does this machine normally do here", so the frames
                // that make up an anomaly are exactly the ones it must not absorb.
                if (!this.fpsWatchdogInDrop && !hitchFrame)
                {
                    this.fpsWatchdogBaselineFps =
                        Mathf.Lerp(this.fpsWatchdogBaselineFps, instantFps, FpsWatchBaselineAlpha);
                }
            }

            // ---- hitch detector ----
            if (hitchFrame && !graced)
            {
                this.fpsWatchdogWindowHitches++;
                if (now >= this.fpsWatchdogNextHitchLogAt)
                {
                    this.fpsWatchdogNextHitchLogAt = now + FpsWatchHitchLogIntervalSeconds;
                    this.LogFpsWatchdogHitch(frameMs, instantFps, modMs);
                    this.fpsWatchdogSuppressedHitches = 0;
                }
                else
                {
                    this.fpsWatchdogSuppressedHitches++;
                }
            }

            // ---- sustained-drop detector ----
            float floorFps = this.FpsWatchdogFloorFps();
            if (!this.fpsWatchdogInDrop)
            {
                if (!graced && this.fpsWatchdogShortFps < floorFps)
                {
                    if (this.fpsWatchdogBelowSince < 0f)
                    {
                        this.fpsWatchdogBelowSince = now;
                    }
                    else if (now - this.fpsWatchdogBelowSince >= FpsWatchEnterSustainSeconds)
                    {
                        this.BeginFpsWatchdogDrop(now, floorFps, modMs);
                    }
                }
                else
                {
                    this.fpsWatchdogBelowSince = -1f;
                }
            }
            else
            {
                this.fpsWatchdogDropSeconds += dtSeconds;
                this.fpsWatchdogDropFrames++;
                this.fpsWatchdogDropModMsTotal += modMs;
                if (modMs > this.fpsWatchdogDropModMsPeak)
                {
                    this.fpsWatchdogDropModMsPeak = modMs;
                }
                if (instantFps < this.fpsWatchdogDropMinFps)
                {
                    this.fpsWatchdogDropMinFps = instantFps;
                }
                if (frameMs > this.fpsWatchdogDropWorstFrameMs)
                {
                    this.fpsWatchdogDropWorstFrameMs = frameMs;
                }

                if (this.fpsWatchdogShortFps >= floorFps * FpsWatchExitHysteresis)
                {
                    if (this.fpsWatchdogAboveSince < 0f)
                    {
                        this.fpsWatchdogAboveSince = now;
                    }
                    else if (now - this.fpsWatchdogAboveSince >= FpsWatchExitSustainSeconds)
                    {
                        this.EndFpsWatchdogDrop(now, "recovered");
                    }
                }
                else
                {
                    this.fpsWatchdogAboveSince = -1f;
                    if (now >= this.fpsWatchdogDropNextOngoingAt)
                    {
                        this.fpsWatchdogDropNextOngoingAt = now + FpsWatchOngoingLogIntervalSeconds;
                        this.LogFpsWatchdogDropOngoing();
                    }
                }
            }

            // ---- periodic summary ----
            if (now - this.fpsWatchdogWindowStartedAt >= FpsWatchSummaryIntervalSeconds)
            {
                this.EmitFpsWatchdogWindowSummary(now);
            }
        }

        private int FpsWatchdogHitchMsClamped()
        {
            return Mathf.Clamp(this.fpsWatchdogHitchMs, 40, 1000);
        }

        // The bar a drop has to fall under: the absolute floor the user set, OR a relative slide off
        // this machine's own baseline — whichever is HIGHER. On a 144 fps rig a slide to 45 fps is a
        // real regression that an absolute floor of 30 would never see; on a 35 fps rig the relative
        // test alone would fire constantly, and the absolute floor is what keeps it honest.
        private float FpsWatchdogFloorFps()
        {
            float relative = this.fpsWatchdogBaselineFps * FpsWatchRelativeFloor;
            float absolute = Mathf.Clamp(this.fpsWatchdogLowFps, 10, 120);
            return Mathf.Max(absolute, relative);
        }

        private static int FpsWatchdogBucketIndex(float frameMs)
        {
            float[] bounds = FpsWatchdogBucketMs;
            for (int i = 0; i < bounds.Length; i++)
            {
                if (frameMs <= bounds[i])
                {
                    return i;
                }
            }
            return bounds.Length - 1;
        }

        // =========================================================================================
        // Drop lifecycle
        // =========================================================================================
        private void BeginFpsWatchdogDrop(float now, float floorFps, float modMs)
        {
            this.fpsWatchdogInDrop = true;
            this.fpsWatchdogBelowSince = -1f;
            this.fpsWatchdogAboveSince = -1f;
            this.fpsWatchdogDropStartedAt = now;
            this.fpsWatchdogDropNextOngoingAt = now + FpsWatchOngoingLogIntervalSeconds;
            this.fpsWatchdogDropBaselineAtEntry = this.fpsWatchdogBaselineFps;
            this.fpsWatchdogDropSeconds = 0f;
            this.fpsWatchdogDropFrames = 0;
            this.fpsWatchdogDropMinFps = float.MaxValue;
            this.fpsWatchdogDropWorstFrameMs = 0f;
            this.fpsWatchdogDropModMsTotal = 0f;
            this.fpsWatchdogDropModMsPeak = 0f;
            Array.Clear(this.fpsWatchdogSectionDropMs, 0, this.fpsWatchdogSectionCount);
            this.fpsWatchdogWindowDrops++;

            // Logged on ENTRY, not just on recovery: a drop that ends in a freeze or a crash never
            // reaches EndFpsWatchdogDrop, and this line is then the only record it happened at all.
            this.EmitFpsWatchdogLine("DROP START fps " + FpsNum1(this.fpsWatchdogShortFps)
                + " (floor " + FpsNum1(floorFps) + ", baseline " + FpsNum1(this.fpsWatchdogBaselineFps) + ")"
                + " | mod " + FpsMs2(modMs) + " ms"
                + this.FpsWatchdogContextSuffix());
        }

        private void LogFpsWatchdogDropOngoing()
        {
            float avgFps = this.fpsWatchdogDropSeconds > 0.001f
                ? this.fpsWatchdogDropFrames / this.fpsWatchdogDropSeconds
                : 0f;
            this.EmitFpsWatchdogLine("drop ongoing " + FpsNum1(this.fpsWatchdogDropSeconds) + " s"
                + " | avg " + FpsNum1(avgFps) + " fps, min " + FpsNum1(this.SafeDropMinFps())
                + ", worst frame " + FpsMs0(this.fpsWatchdogDropWorstFrameMs) + " ms"
                + " | mod avg " + FpsMs2(this.AvgDropModMs()) + " ms peak " + FpsMs2(this.fpsWatchdogDropModMsPeak) + " ms"
                + this.FpsWatchdogWorstSectionSuffix(this.fpsWatchdogSectionDropMs));
        }

        private void EndFpsWatchdogDrop(float now, string reason)
        {
            if (!this.fpsWatchdogInDrop)
            {
                return;
            }
            this.fpsWatchdogInDrop = false;
            this.fpsWatchdogAboveSince = -1f;
            this.fpsWatchdogBelowSince = -1f;

            float elapsed = Mathf.Max(this.fpsWatchdogDropSeconds, now - this.fpsWatchdogDropStartedAt);
            this.fpsWatchdogWindowDropSeconds += elapsed;
            float avgFps = this.fpsWatchdogDropSeconds > 0.001f
                ? this.fpsWatchdogDropFrames / this.fpsWatchdogDropSeconds
                : 0f;

            this.EmitFpsWatchdogLine("DROP END (" + reason + ") after " + FpsNum1(elapsed) + " s"
                + " | avg " + FpsNum1(avgFps) + " fps, min " + FpsNum1(this.SafeDropMinFps())
                + ", worst frame " + FpsMs0(this.fpsWatchdogDropWorstFrameMs) + " ms"
                + ", " + this.fpsWatchdogDropFrames + " frames"
                + " | mod avg " + FpsMs2(this.AvgDropModMs()) + " ms peak " + FpsMs2(this.fpsWatchdogDropModMsPeak) + " ms"
                + this.FpsWatchdogWorstSectionSuffix(this.fpsWatchdogSectionDropMs)
                + " | baseline " + FpsNum1(this.fpsWatchdogDropBaselineAtEntry)
                + this.FpsWatchdogContextSuffix());
        }

        private float SafeDropMinFps()
        {
            return this.fpsWatchdogDropMinFps == float.MaxValue ? 0f : this.fpsWatchdogDropMinFps;
        }

        private float AvgDropModMs()
        {
            return this.fpsWatchdogDropFrames > 0
                ? this.fpsWatchdogDropModMsTotal / this.fpsWatchdogDropFrames
                : 0f;
        }

        // =========================================================================================
        // Hitch + window summary
        // =========================================================================================
        private void LogFpsWatchdogHitch(float frameMs, float instantFps, float modMs)
        {
            StringBuilder sb = new StringBuilder(220);
            sb.Append("hitch ").Append(FpsMs0(frameMs)).Append(" ms (").Append(FpsNum1(instantFps)).Append(" fps)");
            if (this.fpsWatchdogSuppressedHitches > 0)
            {
                sb.Append(" [+").Append(this.fpsWatchdogSuppressedHitches).Append(" more since the last line]");
            }
            sb.Append(" | mod ").Append(FpsMs2(modMs)).Append(" ms");
            sb.Append(this.FpsWatchdogWorstSectionSuffix(this.fpsWatchdogSectionFrameMs));
            sb.Append(" | now ").Append(FpsNum1(this.fpsWatchdogShortFps))
              .Append(" fps, baseline ").Append(FpsNum1(this.fpsWatchdogBaselineFps));
            sb.Append(this.FpsWatchdogContextSuffix());
            this.EmitFpsWatchdogLine(sb.ToString());

            // The full table only when the verbose flag is on — it is one line per phase marker.
            if (MasterLogFpsWatchdog)
            {
                this.DumpFpsWatchdogSectionTable("hitch", this.fpsWatchdogSectionFrameMs, 0.05f);
            }
        }

        private void EmitFpsWatchdogWindowSummary(float now)
        {
            float elapsed = now - this.fpsWatchdogWindowStartedAt;
            float avgFps = this.fpsWatchdogWindowSeconds > 0.001f
                ? this.fpsWatchdogWindowFrames / this.fpsWatchdogWindowSeconds
                : 0f;
            float onePercentLowFps = this.FpsWatchdogPercentileLowFps(0.99f);
            float avgModMs = this.fpsWatchdogWindowFrames > 0
                ? this.fpsWatchdogWindowModMsTotal / this.fpsWatchdogWindowFrames
                : 0f;

            // A clean window still gets a line, but only every QuietSummaryInterval — enough to
            // anchor "the machine was fine here" without a line a minute in a healthy session.
            bool notable = this.fpsWatchdogWindowHitches > 0
                || this.fpsWatchdogWindowDrops > 0
                || (avgFps > 1f && onePercentLowFps > 0f && onePercentLowFps < avgFps * 0.7f);
            bool quietDue = now - this.fpsWatchdogLastQuietSummaryAt >= FpsWatchQuietSummaryIntervalSeconds;

            if (notable || quietDue)
            {
                this.fpsWatchdogLastQuietSummaryAt = now;
                this.EmitFpsWatchdogLine(FpsMs0(elapsed) + "s window: avg " + FpsNum1(avgFps) + " fps"
                    + ", 1% low " + FpsNum1(onePercentLowFps) + " fps"
                    + ", worst frame " + FpsMs0(this.fpsWatchdogWindowWorstFrameMs) + " ms"
                    + " | " + this.fpsWatchdogWindowHitches + " hitches, "
                    + this.fpsWatchdogWindowDrops + " drops (" + FpsNum1(this.fpsWatchdogWindowDropSeconds) + " s)"
                    + " | mod avg " + FpsMs2(avgModMs) + " ms peak " + FpsMs2(this.fpsWatchdogWindowModMsPeak) + " ms"
                    + this.FpsWatchdogWorstSectionSuffix(this.fpsWatchdogSectionWindowMs));

                if (MasterLogFpsWatchdog)
                {
                    this.DumpFpsWatchdogSectionTable("window", this.fpsWatchdogSectionWindowMs, 1f);
                }
            }

            this.fpsWatchdogWindowStartedAt = now;
            this.fpsWatchdogWindowSeconds = 0f;
            this.fpsWatchdogWindowFrames = 0;
            this.fpsWatchdogWindowHitches = 0;
            this.fpsWatchdogWindowDrops = 0;
            this.fpsWatchdogWindowDropSeconds = 0f;
            this.fpsWatchdogWindowWorstFrameMs = 0f;
            this.fpsWatchdogWindowModMsTotal = 0f;
            this.fpsWatchdogWindowModMsPeak = 0f;
            Array.Clear(this.fpsWatchdogHistogram, 0, this.fpsWatchdogHistogram.Length);
            Array.Clear(this.fpsWatchdogSectionWindowMs, 0, this.fpsWatchdogSectionCount);
        }

        // "1% low" the way benchmarks mean it: the frame time at the 99th percentile, expressed back
        // as a rate. Read off the histogram, so no per-frame sample buffer and no sort.
        private float FpsWatchdogPercentileLowFps(float percentile)
        {
            int total = 0;
            for (int i = 0; i < this.fpsWatchdogHistogram.Length; i++)
            {
                total += this.fpsWatchdogHistogram[i];
            }
            if (total <= 0)
            {
                return 0f;
            }

            int target = Mathf.CeilToInt(total * percentile);
            int running = 0;
            for (int i = 0; i < this.fpsWatchdogHistogram.Length; i++)
            {
                running += this.fpsWatchdogHistogram[i];
                if (running >= target)
                {
                    float boundMs = FpsWatchdogBucketMs[i];
                    // Top bucket is unbounded — fall back to the measured worst frame.
                    if (boundMs >= float.MaxValue)
                    {
                        boundMs = Mathf.Max(this.fpsWatchdogWindowWorstFrameMs, 1f);
                    }
                    return boundMs > 0.0001f ? 1000f / boundMs : 0f;
                }
            }
            return 0f;
        }

        // =========================================================================================
        // Section attribution — the Breadcrumbs.Phase observer
        // =========================================================================================
        private void HookFpsWatchdogPhaseObserver()
        {
            if (this.fpsWatchdogPhaseObserver == null)
            {
                this.fpsWatchdogPhaseObserver = this.OnFpsWatchdogPhaseMarker;
            }
            this.fpsWatchdogSectionMainThreadId = Environment.CurrentManagedThreadId;
            Breadcrumbs.PhaseObserver = this.fpsWatchdogPhaseObserver;
            this.fpsWatchdogPhaseHooked = true;
        }

        private void UnhookFpsWatchdogPhaseObserver()
        {
            if (Breadcrumbs.PhaseObserver == this.fpsWatchdogPhaseObserver)
            {
                Breadcrumbs.PhaseObserver = null;
            }
            this.fpsWatchdogPhaseHooked = false;
            this.fpsWatchdogSectionActive = -1;
        }

        // Called from Breadcrumbs.Phase(), ~24+ times per OnUpdate. Everything here is one
        // timestamp, a short reference-equality scan and two float adds — no allocation, no lock.
        private void OnFpsWatchdogPhaseMarker(string area)
        {
            // Breadcrumbs also runs a background hang watchdog; only the main thread's markers
            // describe the frame being timed.
            if (Environment.CurrentManagedThreadId != this.fpsWatchdogSectionMainThreadId)
            {
                return;
            }

            long ts = System.Diagnostics.Stopwatch.GetTimestamp();
            int prev = this.fpsWatchdogSectionActive;
            if (prev >= 0)
            {
                this.fpsWatchdogSectionFrameMs[prev] +=
                    (float)((ts - this.fpsWatchdogSectionStartTicks) * FpsWatchdogTicksToMs);
            }
            this.fpsWatchdogSectionActive = this.ResolveFpsWatchdogSection(area);
            this.fpsWatchdogSectionStartTicks = ts;
        }

        // Every call site passes a string LITERAL, so the interned reference matches on the first
        // compare and the Equals fallback is only there for a non-literal that slips in later.
        private int ResolveFpsWatchdogSection(string area)
        {
            if (string.IsNullOrEmpty(area))
            {
                area = "?";
            }
            string[] names = this.fpsWatchdogSectionNames;
            int count = this.fpsWatchdogSectionCount;
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(names[i], area) || string.Equals(names[i], area, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            if (count >= FpsWatchdogMaxSections)
            {
                return count - 1; // table full: fold into the last slot rather than grow unbounded
            }
            names[count] = area;
            this.fpsWatchdogSectionCount = count + 1;
            return count;
        }

        private string FpsWatchdogWorstSectionSuffix(float[] table)
        {
            int worst = -1;
            float worstMs = 0f;
            for (int i = 0; i < this.fpsWatchdogSectionCount; i++)
            {
                if (table[i] > worstMs)
                {
                    worstMs = table[i];
                    worst = i;
                }
            }
            return worst < 0 || worstMs <= 0.01f
                ? string.Empty
                : " (worst " + this.fpsWatchdogSectionNames[worst] + " " + FpsMs2(worstMs) + " ms)";
        }

        private void DumpFpsWatchdogSectionTable(string label, float[] table, float minMs)
        {
            for (int i = 0; i < this.fpsWatchdogSectionCount; i++)
            {
                if (table[i] >= minMs)
                {
                    ModLogger.Msg("[FpsWatch]   " + label + " section " + this.fpsWatchdogSectionNames[i]
                        + " = " + FpsMs2(table[i]) + " ms");
                }
            }
        }

        // =========================================================================================
        // Context + output
        // =========================================================================================
        // What was on screen when it happened. Only built for a line that is actually being
        // written, so the player lookup (cached, 1 s throttle) never runs on the hot path.
        private string FpsWatchdogContextSuffix()
        {
            try
            {
                StringBuilder sb = new StringBuilder(96);
                int levelType = this.CurrentLevelType;
                if (levelType >= 0)
                {
                    sb.Append(" | ").Append(FpsWatchdogLevelName(levelType))
                      .Append(" scene ").Append(this.CurrentSceneId);
                }
                if (this.TryGetLocalPlayerPosition(out Vector3 pos) && pos != Vector3.zero)
                {
                    sb.Append(" | pos ").Append(FpsNum1(pos.x)).Append(',').Append(FpsNum1(pos.y)).Append(',').Append(FpsNum1(pos.z));
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        // Level ids as HeartopiaComplete.WorldReady.cs names them; anything else prints its number,
        // because an unnamed level in a stutter report is still a fact worth having.
        private static string FpsWatchdogLevelName(int levelType)
        {
            switch (levelType)
            {
                case WorldLevelTypeLogin: return "Login";
                case WorldLevelTypeTown: return "Town";
                case WorldLevelTypeMicroHome: return "Homeland";
                default: return "level " + levelType.ToString(CultureInfo.InvariantCulture);
            }
        }

        // Both sinks, always. The mod log is what an agent tails live; the file is what survives the
        // session and is worth attaching to a bug report.
        private void EmitFpsWatchdogLine(string body)
        {
            ModLogger.Msg("[FpsWatch] " + body);
            this.WriteFpsWatchdogFileLine(body);
        }

        private void WriteFpsWatchdogFileLine(string body)
        {
            if (this.fpsWatchdogWriterFailed)
            {
                return;
            }
            try
            {
                if (this.fpsWatchdogWriter == null)
                {
                    string path = HelperPaths.GetFile("fps-watchdog.log", "Logs");
                    // Append across sessions (a stutter is often "it started yesterday"), but never
                    // let the file grow without a bound.
                    try
                    {
                        FileInfo info = new FileInfo(path);
                        if (info.Exists && info.Length > FpsWatchFileMaxBytes)
                        {
                            info.Delete();
                        }
                    }
                    catch
                    {
                    }

                    this.fpsWatchdogWriter = new StreamWriter(
                        new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
                    this.fpsWatchdogWriter.WriteLine();
                    this.fpsWatchdogWriter.WriteLine("# session " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        + "  build " + ModBuildVersion.Display
                        + "  hitch>=" + this.FpsWatchdogHitchMsClamped() + "ms  lowFps<" + this.fpsWatchdogLowFps);
                }

                this.fpsWatchdogWriter.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + body);
                this.fpsWatchdogWriter.Flush();
            }
            catch (Exception ex)
            {
                this.fpsWatchdogWriterFailed = true;
                this.fpsWatchdogWriter = null;
                ModLogger.Warning("[FpsWatch] log file unavailable, mod log only: " + ex.Message);
            }
        }

        // Invariant formatting: these lines are numbers people diff and paste, and a comma decimal
        // separator on a Russian/German client makes them unparseable. Named for the feature, not
        // for the format — HeartopiaComplete is one partial class across 200 files, and a static
        // `F1` here would be a landmine for the next person who wants that name.
        private static string FpsMs0(float v) { return v.ToString("0", CultureInfo.InvariantCulture); }
        private static string FpsNum1(float v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }
        private static string FpsMs2(float v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }

        // =========================================================================================
        // Settings plumbing
        // =========================================================================================
        internal void SetFpsWatchdogEnabled(bool enabled)
        {
            if (enabled == this.fpsWatchdogEnabled)
            {
                return;
            }
            this.fpsWatchdogEnabled = enabled;
            if (!enabled)
            {
                if (this.fpsWatchdogInDrop)
                {
                    this.EndFpsWatchdogDrop(Time.unscaledTime, "watchdog off");
                }
                this.UnhookFpsWatchdogPhaseObserver();
                ModLogger.Msg("[FpsWatch] disabled.");
            }
            else
            {
                this.ResetFpsWatchdogState();
                ModLogger.Msg("[FpsWatch] enabled — hitch >= " + this.FpsWatchdogHitchMsClamped()
                    + " ms, sustained drop below " + this.FpsWatchdogFloorFps().ToString("0.0", CultureInfo.InvariantCulture)
                    + " fps. Log: " + HelperPaths.GetFile("fps-watchdog.log", "Logs"));
            }
        }

        // Threshold edits invalidate the smoothing, not just the comparison — re-arm from scratch
        // so the next verdict is taken against the new setting with a clean baseline.
        internal void ResetFpsWatchdogState()
        {
            this.fpsWatchdogPrimed = false;
            this.fpsWatchdogBaselineFps = 0f;
            this.fpsWatchdogShortFps = 0f;
            this.fpsWatchdogInDrop = false;
            this.fpsWatchdogBelowSince = -1f;
            this.fpsWatchdogAboveSince = -1f;
            this.fpsWatchdogSuppressedHitches = 0;
            this.fpsWatchdogNextHitchLogAt = 0f;
            this.fpsWatchdogWasSuppressed = true;
            this.fpsWatchdogWindowStartedAt = 0f;
            this.fpsWatchdogWindowSeconds = 0f;
            this.fpsWatchdogWindowFrames = 0;
            this.fpsWatchdogWindowHitches = 0;
            this.fpsWatchdogWindowDrops = 0;
            this.fpsWatchdogWindowDropSeconds = 0f;
            this.fpsWatchdogWindowWorstFrameMs = 0f;
            this.fpsWatchdogWindowModMsTotal = 0f;
            this.fpsWatchdogWindowModMsPeak = 0f;
            Array.Clear(this.fpsWatchdogHistogram, 0, this.fpsWatchdogHistogram.Length);
            Array.Clear(this.fpsWatchdogSectionWindowMs, 0, this.fpsWatchdogSectionCount);
            Array.Clear(this.fpsWatchdogSectionDropMs, 0, this.fpsWatchdogSectionCount);
        }

        // Called from OnDeinitializeMelon. The observer is a delegate onto THIS instance held by a
        // static field in Breadcrumbs — leaving it installed would keep the mod object alive and
        // keep charging every phase marker for a watchdog that is no longer running.
        internal void ShutdownFpsWatchdog()
        {
            try
            {
                if (this.fpsWatchdogInDrop)
                {
                    this.EndFpsWatchdogDrop(Time.unscaledTime, "shutdown");
                }
            }
            catch
            {
            }

            this.UnhookFpsWatchdogPhaseObserver();

            try
            {
                if (this.fpsWatchdogWriter != null)
                {
                    this.fpsWatchdogWriter.Flush();
                    this.fpsWatchdogWriter.Dispose();
                }
            }
            catch
            {
            }
            this.fpsWatchdogWriter = null;
        }

        // Live one-liner for the status surfaces / MCP `env`.
        internal string BuildFpsWatchdogSummaryText()
        {
            if (!this.fpsWatchdogEnabled)
            {
                return "off";
            }
            if (!this.fpsWatchdogPrimed)
            {
                return "arming";
            }
            return (this.fpsWatchdogInDrop ? "DROP " : "ok ")
                + FpsNum1(this.fpsWatchdogShortFps) + " fps (baseline " + FpsNum1(this.fpsWatchdogBaselineFps)
                + ", floor " + FpsNum1(this.FpsWatchdogFloorFps()) + ")";
        }
    }
}
