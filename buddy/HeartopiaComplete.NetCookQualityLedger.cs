using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // Mass Cook dish quality — Source A: the game's own Collection record, and the reader that
    // combines it with Source B (the mod's ledger).
    // Plan: docs/plans/2026-10-31-mass-cook-dish-stars.md §2.1 (Source A), §2.3 (reader).
    //
    // Source A semantics, proven live 2026-10-31 (plan §1.8 row 6): `PictorialBase` food records are
    // a MAX — "best star ever achieved account-wide". A fresh 3★ on an already-5★ dish changes
    // nothing, so Source A can label and filter, but it can never say what THIS cook produced;
    // that is Source B's job — the §2.2 census-diff writer lives in this file below and feeds
    // netCookRecipeBestQuality.
    //
    // The read shape is copied from TryClassifyRadarCatalogEntry (HeartopiaComplete.RadarCollectionFilter.cs)
    // — same PictorialSystem singleton, same TryGetPictorialData invoke, same pin discipline — but
    // nothing of the radar's certification tables travels here. The radar file itself is NOT touched;
    // this feature piggybacks on its three pictorial dirty flags instead of paying 3 more hook slots
    // (event-hook-slot-budget), per plan §2.1.
    //
    // Call-site rule: TryRefreshNetCookCollectionStars() does AuraMono work, so it is called only
    // from the Mass Cook panel paths and the confirmed-collect path (phases 1b/2 wire those), each
    // behind its own throttle. Never from OnUpdate, never per row (plan §2.1 refresh points).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // EPictorialNewState.Got — GetStarRate is only meaningful in this state; an entry the player
        // has never produced reports 0, which must render as "no label", not as a 0★ dish.
        private const int NetCookPictorialStateGot = 2;
        private const int NetCookStarMin = 1;
        private const int NetCookStarMax = 5;

        // A pictorial-detail burst fills the whole event ring in one dispatch (plan §2.1), so the
        // dirty flag is coalesced: at most one scan per this interval, longer backoff after failures.
        private const float NetCookCollectionStarScanMinIntervalSec = 1.0f;
        private const float NetCookCollectionStarScanBackoffSec = 5.0f;

        // Dish staticId (== recipeId for cooked dishes — proven by the P0-7 probe: 45101 has a
        // Collection entry, hidden 45129 does not) -> star 1..5. Absent key = unknown, and unknown
        // is a real state, not "1★" (plan §2.3): every consumer must show nothing for it.
        private readonly Dictionary<int, int> netCookCollectionStarById = new Dictionary<int, int>(512);
        private int netCookCollectionStarEpoch = -1;
        private bool netCookCollectionStarDirty = true;
        // Rising-edge tracker for the radar-flag piggyback (finding 7): true while the radar's flag
        // has already armed us and has not been consumed by the radar yet.
        private bool netCookCollectionStarRadarEdgeConsumed;
        private bool netCookCollectionStarScanFailedLogged;
        private bool netCookCollectionStarHooksRegistered;
        private IntPtr netCookPictorialTryGetMethod = IntPtr.Zero;
        private float netCookCollectionStarNextAttemptAtSec = -999f;

        // Source B storage (plan §2.2): "best star this install has witnessed". Written by the
        // census-diff path and the inventory seed in phase 1b; declared here so the reader below is
        // already the final §2.3 shape and nothing re-signatures when the writers land.
        private readonly Dictionary<int, int> netCookRecipeBestQuality = new Dictionary<int, int>(256);

        private void EnsureNetCookQualityHooks()
        {
            if (this.netCookCollectionStarHooksRegistered)
            {
                return;
            }

            this.netCookCollectionStarHooksRegistered = true;
            this.RegisterWorldReadyCallback("NetCookQualityCollection", this.OnNetCookQualityWorldReady);
        }

        private bool OnNetCookQualityWorldReady()
        {
            this.netCookCollectionStarDirty = true;
            return true;
        }

        // Trigger for the later phases: first Mass Cook panel open of a session and every confirmed
        // collect of our own dish re-arm the dirty flag; the scan itself stays throttled.
        private void MarkNetCookCollectionStarDirty()
        {
            this.netCookCollectionStarDirty = true;
        }

        // Consumes the dirty flag with a minimum interval (plan §2.1 coalescing rule). Returns true
        // only when a full scan replaced the cache. A failed scan logs once and keeps the previous
        // cache — the grid never blanks out mid-session.
        private bool TryRefreshNetCookCollectionStars()
        {
            this.EnsureNetCookQualityHooks();

            // Piggyback on the radar's pictorial hooks rather than registering a fourth, fifth and
            // sixth handler of the same three events — but on the radar flag's RISING EDGE, through
            // our own consumed-marker (review 2026-11-01, finding 7). Level-matching the radar flag
            // turned every picker frame into an arm: the flag initializes true and is cleared only
            // by radar rebuilds, so a world where the radar UI was never opened rescanned every
            // minimum-interval while the picker was open. Edge-matching arms once per radar dirty
            // burst; the epoch branch below still arms the first scan of a world without the radar
            // ever having been opened.
            if (this.radarCollectionFilterDirty)
            {
                if (!this.netCookCollectionStarRadarEdgeConsumed)
                {
                    this.netCookCollectionStarRadarEdgeConsumed = true;
                    this.netCookCollectionStarDirty = true;
                }
            }
            else
            {
                this.netCookCollectionStarRadarEdgeConsumed = false; // radar consumed its burst — re-arm on the next one
            }

            if (this.netCookCollectionStarEpoch != AuraMonoWorldEpoch)
            {
                this.netCookCollectionStarEpoch = AuraMonoWorldEpoch;
                this.netCookPictorialTryGetMethod = IntPtr.Zero;
                this.netCookCollectionStarDirty = true;
                this.netCookCollectionStarRadarEdgeConsumed = false; // new world: a still-set radar flag arms once here, too
                // The cache is deliberately NOT cleared on a world change: it is account-wide state,
                // a successful scan replaces it, and a failed scan keeps the last known stars.
            }

            if (!this.netCookCollectionStarDirty || this.netCookRecipeEntries.Count <= 0)
            {
                // Nothing to scan yet (recipe cache not ready) or nothing marked — stay dirty.
                return false;
            }

            float now = Time.unscaledTime;
            if (now < this.netCookCollectionStarNextAttemptAtSec)
            {
                return false;
            }

            this.netCookCollectionStarNextAttemptAtSec = now + NetCookCollectionStarScanMinIntervalSec;
            this.netCookCollectionStarDirty = false;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
                {
                    this.FailNetCookCollectionStarScan("aura mono unavailable");
                    return false;
                }

                IntPtr systemObj;
                if (!this.TryResolveAuraMonoModule(
                        "XDTGameSystem.GameplaySystem.Pictorial.PictorialSystem", out systemObj)
                    || systemObj == IntPtr.Zero)
                {
                    this.FailNetCookCollectionStarScan("PictorialSystem unresolved");
                    return false;
                }

                uint sysPin = AuraMonoPinNew(systemObj);
                if (sysPin == 0U)
                {
                    // Pinning unavailable → no safe scan (auramono-pinning-fail-closed).
                    this.FailNetCookCollectionStarScan("system pin failed");
                    return false;
                }

                try
                {
                    Dictionary<int, int> next = new Dictionary<int, int>(this.netCookRecipeEntries.Count);
                    for (int i = 0; i < this.netCookRecipeEntries.Count; i++)
                    {
                        int recipeId = this.netCookRecipeEntries[i].Key;
                        bool known;
                        int star;
                        if (!this.TryReadNetCookCollectionStar(systemObj, recipeId, out known, out star))
                        {
                            this.FailNetCookCollectionStarScan("entry read failed at " + recipeId);
                            return false;
                        }

                        if (known)
                        {
                            next[recipeId] = star;
                        }
                    }

                    this.netCookCollectionStarById.Clear();
                    foreach (KeyValuePair<int, int> pair in next)
                    {
                        this.netCookCollectionStarById[pair.Key] = pair.Value;
                    }

                    this.netCookCollectionStarScanFailedLogged = false;
                    return true;
                }
                finally
                {
                    AuraMonoPinFree(sysPin);
                }
            }
            catch (Exception ex)
            {
                this.FailNetCookCollectionStarScan(ex.Message);
                return false;
            }
        }

        // Hard failure (method unresolved, pin refused) returns false; a soft miss (no entry for
        // this id, _state unread) returns true with known=false — the Collection simply does not
        // cover that dish, which is the 217-of-449 half of the grid, not an error.
        private unsafe bool TryReadNetCookCollectionStar(IntPtr systemObj, int staticId, out bool known, out int star)
        {
            known = false;
            star = 0;

            if (this.netCookPictorialTryGetMethod == IntPtr.Zero)
            {
                IntPtr sysClass = auraMonoObjectGetClass(systemObj);
                this.netCookPictorialTryGetMethod = this.FindAuraMonoMethodOnHierarchy(sysClass, "TryGetPictorialData", 2);
            }

            if (this.netCookPictorialTryGetMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            int id = staticId;
            IntPtr outSlot = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&id);
            args[1] = (IntPtr)(&outSlot);
            IntPtr boxedOk = auraMonoRuntimeInvoke(this.netCookPictorialTryGetMethod, systemObj, (IntPtr)args, ref exc);
            bool hasEntry = exc == IntPtr.Zero && outSlot != IntPtr.Zero
                && (boxedOk == IntPtr.Zero || !this.TryUnboxMonoBoolean(boxedOk, out bool ok) || ok);
            if (!hasEntry)
            {
                return true;
            }

            uint pin = AuraMonoPinNew(outSlot);
            if (pin == 0U)
            {
                return false;
            }

            try
            {
                if (!this.TryGetMonoInt32Member(outSlot, "_state", out int state))
                {
                    return true;
                }

                if (state != NetCookPictorialStateGot)
                {
                    return true;
                }

                // Lookup starts at the instance class so PictorialFood.GetStarRate runs, which carries
                // the FeatureOpenEnum.CookingQuality gate: with the feature closed every food star
                // reads 0 and the labels simply never appear (plan §2.1 — correct degradation, no
                // invented fallback).
                if (!this.TryInvokeAuraMonoZeroArgInt(outSlot, out star, "GetStarRate"))
                {
                    return true;
                }

                known = star >= NetCookStarMin && star <= NetCookStarMax;
                if (!known)
                {
                    star = 0;
                }

                return true;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private void FailNetCookCollectionStarScan(string reason)
        {
            // Fail closed for a while, log once: a systematic failure cools down instead of spamming
            // every panel open (FeatureBreaker-style, per repo convention).
            this.netCookCollectionStarNextAttemptAtSec = Time.unscaledTime + NetCookCollectionStarScanBackoffSec;
            if (!this.netCookCollectionStarScanFailedLogged)
            {
                this.netCookCollectionStarScanFailedLogged = true;
                ModLogger.Msg("[NetCookQuality] Collection star scan failed: " + reason);
            }
        }

        // ---- Reader (plan §2.3): caches only, never triggers a measurement or an invoke. ----

        // 0 = unknown. Unknown must render as no label / keep the row / show the toggle.
        private int GetNetCookRecipeBestQuality(int recipeId)
        {
            int best = 0;
            int star;
            if (this.netCookCollectionStarById.TryGetValue(recipeId, out star) && star > best)
            {
                best = star;
            }

            if (this.netCookRecipeBestQuality.TryGetValue(recipeId, out star) && star > best)
            {
                best = star;
            }

            return best;
        }

        private bool NetCookRecipeFiveStarKnown(int recipeId)
        {
            return this.GetNetCookRecipeBestQuality(recipeId) >= NetCookStarMax;
        }

        // ========================================================================================
        // Source B writers (plan §2.2-a census diff, §2.2-b inventory seed).
        //
        // Attribution is a COUNT delta on (recipeId -> star -> total stack count), the plan's
        // original §2.2-a shape. The first implementation used a netId watermark instead; a live
        // pass on 2026-11-01 killed it (plan §1.8 mass-cook row, recipe 45230): the server MERGES a
        // collected dish into an existing same-staticId+same-star stack — a 2★ dish landed in the
        // old 2★ stack (netId 26077960, count 4->6) and two 3★ dishes became ONE stack (netId
        // 27457468, count 2), so five confirmed collect jobs credited zero stars. A watermark can
        // never see a merged arrival, and the merged identity is exactly the one R2/R1 must react
        // to (a 5★ cooking into an existing 5★ stack). The count diff is immune because its key IS
        // the merged identity: the increment on (recipe, star) is the outcome, however the bag got
        // there. Skew rule: anything sold/eaten inside the window makes the delta smaller, so the
        // worst case is a delayed credit, never an over-credit — safe under max semantics. That is
        // also why the two census passes are deliberately asymmetric: the BASELINE pass is strict
        // (a star/Count read gap at baseline would invent an increment on the old stock's count →
        // credit a star no dish produced), while the DIFF pass is tolerant (a gap there only hides
        // an increment → delay only). Each job still carries its epoch: the counts themselves are
        // fine across servers, but a cross-epoch window would mix two different bags.
        //
        // Writer gate (plan §8-1): every job starts at a collect interact this mod itself sent for
        // a target whose own prepare was server-confirmed (PrepareConfirmed). A neighbour's TakeFood
        // on a shared public stove can arm no job and confirms no job, so neighbour dishes never
        // enter the ledger. CookResultEvent only opens the settle window early; the send is the
        // commitment. The job always tracks the run's recipe, so a foreign dish collected on an
        // attended stove finds no same-recipe new stack and credits nothing.
        // ========================================================================================

        // Confirm -> re-read delay. P0-1 (live pass) measures the real settle latency and sizes this;
        // a diff that finds no new stack says so in the log rather than guessing a star.
        private const float NetCookQualityCollectSettleSec = 2.0f;
        private const float NetCookQualityCollectUnconfirmedMaxSec = 8.0f; // a send that never saw its TakeFood event
        private const float NetCookQualityCollectJobExpireSec = 20.0f;     // hard ceiling: drop, never credit stale
        private const float NetCookQualityCollectRetrySec = 1.0f;

        // The seed warm-up runs on the world-ready gate (AGENTS §1): the callback framework owns the
        // retry cadence (~1 s, bounded per epoch), so this file carries no seed retry timer at all
        // (the 5 s netCookLedgerSeedNextAttemptAtSec poll was removed 2026-11-01 in review).
        private const string NetCookLedgerSeedWorldReadyCallbackName = "NetCookQualityLedgerSeed";

        private sealed class NetCookQualityCollectJob
        {
            public ulong LevelObjectNetId;
            public int RecipeId;             // the run's recipe, captured at send
            public int Epoch;                // AuraMonoWorldEpoch when armed (see header)
            public int RunGeneration;        // netCookRunGeneration at send — feeds run counters/drain only for the live run
            // star -> total stack Count of same-identity (recipe, star) stacks visible at send.
            // Managed scalars only — nothing here holds a MonoObject*, so the job is frame-safe.
            public readonly Dictionary<int, int> BaselineStarCounts = new Dictionary<int, int>(6);
            public float SentAt;
            public bool Confirmed;           // CookResultEvent(TakeFood) armed the settle window
            public float DueAt;              // when the diff may run
        }

        private readonly List<NetCookQualityCollectJob> netCookQualityCollectJobs = new List<NetCookQualityCollectJob>(8);
        private int netCookRunBestCompletedQuality; // reset by StartNetCookInternal; drives the §4.3 R2 trigger
        // Bumped by StartNetCookInternal AND StopNetCookInternal (NetCook.cs). Collect jobs capture it
        // at send; only a job from the currently-live run may feed run counters or the §4.3 drain
        // trigger. Jobs deliberately outlive a stop (the collect diff completes from ProcessNetCookLoop
        // after netCookEnabled is already false), so without the stamp a stopped run A's settling
        // collect would credit — and could drain — a freshly started run B on the same recipe
        // (review 2026-11-01). The ledger max credit stays unconditional: a collected dish is forever
        // history no matter which run collected it.
        private int netCookRunGeneration;
        // Seed state is just the per-epoch done-marker + the world-ready callback; the old
        // netCookLedgerSeedPending flag became write-only when the pump-guard term was dropped
        // (review 2026-11-01, finding D2) and was removed with it.
        private int netCookLedgerSeedEpoch = -1;
        private bool netCookQualityLedgerSeedFailedLogged;
        private bool netCookQualityCensusUnreadableLogged;
        private bool netCookQualityBaselineUnreliableLogged;

        // Called from every collect-interact send in HeartopiaComplete.NetCook.cs, right after the
        // send returns. The interact is an async web request, so the dish being collected cannot be
        // in the bag yet — the census sees exactly the state its own arrival would contaminate.
        // A previous plate's dish that already settled IS in the baseline: the delta then nets to
        // zero for it, so contamination cancels instead of mis-crediting.
        private void BeginNetCookQualityCollectJob(NetCookTargetContext target)
        {
            if (target == null || !target.PrepareConfirmed || target.LevelObjectNetId == 0UL)
            {
                return;
            }

            int recipeId = this.netCookRecipeId;
            if (recipeId <= 0)
            {
                return;
            }

            if (!this.TryRefreshDirectBackpackRuntimeSnapshot(true))
            {
                // The diff needs a baseline; no census, no job. Log once so a systematically
                // unreadable bag is visible in the file log, not just missing outcomes.
                if (!this.netCookQualityCensusUnreadableLogged)
                {
                    this.netCookQualityCensusUnreadableLogged = true;
                    ModLogger.Msg("[NetCookQuality] collect diff skipped: backpack census unreadable");
                }

                return;
            }

            NetCookQualityCollectJob baselineJob = new NetCookQualityCollectJob();
            if (!this.TryBuildNetCookRecipeStarCensus(recipeId, baselineJob.BaselineStarCounts, true,
                    out int baselineUnreadable))
            {
                // Fail closed: an old stock stack whose star/Count could not be read would show up
                // at diff time as an increment invented from its Count — crediting a star no dish
                // produced. No baseline, no credit.
                if (!this.netCookQualityBaselineUnreliableLogged)
                {
                    this.netCookQualityBaselineUnreliableLogged = true;
                    ModLogger.Msg("[NetCookQuality] collect diff skipped: baseline census unreliable recipe="
                        + recipeId + " unreadableStacks=" + baselineUnreadable);
                }

                return;
            }

            // A re-prepare on a stove whose previous dish never settled supersedes the old job: the
            // old dish left the stove (or it did not, and its diff would find nothing attributable).
            for (int i = this.netCookQualityCollectJobs.Count - 1; i >= 0; i--)
            {
                if (this.netCookQualityCollectJobs[i].LevelObjectNetId == target.LevelObjectNetId)
                {
                    this.netCookQualityCollectJobs.RemoveAt(i);
                }
            }

            baselineJob.LevelObjectNetId = target.LevelObjectNetId;
            baselineJob.RecipeId = recipeId;
            baselineJob.Epoch = AuraMonoWorldEpoch;
            baselineJob.RunGeneration = this.netCookRunGeneration;
            baselineJob.SentAt = Time.unscaledTime;
            baselineJob.Confirmed = false;
            baselineJob.DueAt = Time.unscaledTime + NetCookQualityCollectUnconfirmedMaxSec;
            this.netCookQualityCollectJobs.Add(baselineJob);
        }

        // §2.2-b trigger: called when the CookingSystem recipe cache becomes ready. Registers the seed
        // on the world-ready gate (AGENTS §1 — the framework owns the retry cadence, bounded per
        // epoch), and attempts inline so a ready game seeds at once instead of a second later.
        // The seed is idempotent per world epoch, so a cache rebuild in the same epoch costs nothing.
        private void MarkNetCookLedgerSeedPending()
        {
            this.RegisterWorldReadyCallback(NetCookLedgerSeedWorldReadyCallbackName, this.OnWorldReadySeedNetCookLedger);
            this.SeedNetCookLedgerFromInventory(); // idempotent per epoch via netCookLedgerSeedEpoch
        }

        // World-ready callback body: true = done for this world epoch (seeded or permanently failed),
        // false = not yet (recipe list not built / AuraMono not ready), retry on the framework cadence.
        private bool OnWorldReadySeedNetCookLedger()
        {
            return this.SeedNetCookLedgerFromInventory();
        }

        // A TakeFood result for a stove we sent a collect to opens the settle window from the
        // confirmation, which is tighter than the blind unconfirmed wait. TakeFood events without a
        // matching job are somebody else's collect on the same stove — the job gate is the send, so
        // they confirm nothing (plan §8-1: neighbours stay out).
        private void ConfirmNetCookQualityCollectJob(ulong levelObjectNetId)
        {
            if (this.netCookQualityCollectJobs.Count <= 0)
            {
                return;
            }

            for (int i = 0; i < this.netCookQualityCollectJobs.Count; i++)
            {
                NetCookQualityCollectJob job = this.netCookQualityCollectJobs[i];
                if (job.LevelObjectNetId == levelObjectNetId && !job.Confirmed)
                {
                    job.Confirmed = true;
                    job.DueAt = Time.unscaledTime + NetCookQualityCollectSettleSec;
                    return;
                }
            }
        }

        // Runs at the top of ProcessNetCookLoop: before the two loop guards, because a collect diff
        // must complete even when the run stopped right after the collect (netCookEnabled is already
        // false by then and neither loop would run). Not a warm-up poll: every job self-confirms or
        // self-expires within NetCookQualityCollectJobExpireSec. The seed is NOT handled here — it
        // runs on the world-ready gate callback + the cache-ready inline attempt (AGENTS §1).
        private void ConsumeNetCookQualityLedgerJobs()
        {
            float now = Time.unscaledTime;

            for (int i = this.netCookQualityCollectJobs.Count - 1; i >= 0; i--)
            {
                NetCookQualityCollectJob job = this.netCookQualityCollectJobs[i];

                if (job.Epoch != AuraMonoWorldEpoch)
                {
                    // World switch: netIds belong to the previous server session; comparing them
                    // here could credit an arbitrary stack. Retire the job, log nothing — it is the
                    // expected outcome of a mid-cook world change.
                    this.netCookQualityCollectJobs.RemoveAt(i);
                    continue;
                }

                if (now - job.SentAt > NetCookQualityCollectJobExpireSec)
                {
                    // Tier-1 (never toast-only): an expired job is a lost attribution — the live pass
                    // reads these lines to size NetCookQualityCollectSettleSec/JobExpireSec.
                    ModLogger.Msg("[NetCookQuality] collect-diff job expired stove=" + job.LevelObjectNetId
                        + " recipe=" + job.RecipeId + " confirmed=" + job.Confirmed + " (no credit)");
                    this.netCookQualityCollectJobs.RemoveAt(i);
                    continue;
                }

                if (now < job.DueAt)
                {
                    continue;
                }

                if (this.CompleteNetCookQualityCollectJob(job, now))
                {
                    this.netCookQualityCollectJobs.RemoveAt(i);
                }
                else
                {
                    // Census unread right now — stay in the queue and retry, the expire ceiling above
                    // keeps a permanently unreadable bag from holding the job forever.
                    job.DueAt = now + NetCookQualityCollectRetrySec;
                }
            }
        }

        // True = the job is finished (credited or explicitly empty); false = census read failed and
        // the caller keeps the job for a retry.
        private bool CompleteNetCookQualityCollectJob(NetCookQualityCollectJob job, float now)
        {
            if (!this.TryRefreshDirectBackpackRuntimeSnapshot(true))
            {
                return false;
            }

            // Tolerant diff pass: a star/Count gap here only HIDES an increment (delay under max
            // semantics), it cannot invent one — the strict side is the baseline.
            Dictionary<int, int> current = new Dictionary<int, int>(job.BaselineStarCounts.Count + 2);
            if (!this.TryBuildNetCookRecipeStarCensus(job.RecipeId, current, false, out int diffUnreadable))
            {
                return false; // safety belt: tolerant mode should not fail, keep the job if it ever does
            }

            int[] before = new int[NetCookStarMax + 1];
            int[] after = new int[NetCookStarMax + 1];
            foreach (KeyValuePair<int, int> entry in job.BaselineStarCounts)
            {
                if (entry.Key >= 0 && entry.Key <= NetCookStarMax)
                {
                    before[entry.Key] = entry.Value;
                }
            }

            foreach (KeyValuePair<int, int> entry in current)
            {
                if (entry.Key >= 0 && entry.Key <= NetCookStarMax)
                {
                    after[entry.Key] = entry.Value;
                }
            }

            // Per-identity delta walk. A star bucket present only in the baseline means its stock
            // left the window (sold/eaten — the §8-2 race showing as a negative delta); a bucket
            // present only in `current` is a first sighting, i.e. baseline 0.
            StringBuilder deltas = new StringBuilder();
            for (int star = 0; star <= NetCookStarMax; star++)
            {
                if (before[star] == after[star])
                {
                    continue;
                }

                if (deltas.Length > 0)
                {
                    deltas.Append(' ');
                }

                deltas.Append(star).Append('*').Append(before[star]).Append("->").Append(after[star]);
            }

            // Each incremented identity is a dish that arrived during the window — with 2+ dishes
            // of the same recipe completing together the per-PLATE attribution is coarse by design
            // (P0-2), but every credited star was genuinely observed, and both consumers (the ledger
            // max, netCookRunBestCompletedQuality) take maxes, so coarse attribution can only delay
            // R2, never credit a star no dish achieved.
            int outcomeStar = 0;
            for (int star = NetCookStarMin; star <= NetCookStarMax; star++)
            {
                if (after[star] <= before[star])
                {
                    continue;
                }

                // Run membership = same recipe AND the job was armed by the run that is live right
                // now (the ledger credit itself is unconditional above — see CreditNetCookQualityObserved).
                this.CreditNetCookQualityObserved(job.RecipeId, star,
                    job.RecipeId == this.netCookRecipeId && job.RunGeneration == this.netCookRunGeneration);
                if (star > outcomeStar)
                {
                    outcomeStar = star;
                }
            }

            // Tier-1 lines, unconditional (MasterLogNetCook gates NetCookLog — the outcome evidence
            // P0-1/P0-2 are measured from must reach the file log on a default install). ageSec is
            // the send->diff latency P0-1 sizes the settle window from.
            string age = (now - job.SentAt).ToString("0.0", CultureInfo.InvariantCulture);
            if (outcomeStar > 0)
            {
                ModLogger.Msg("[NetCookQuality] collect outcome: recipe=" + job.RecipeId + " " + deltas
                    + " outcome=" + outcomeStar + " stove=" + job.LevelObjectNetId
                    + " confirmed=" + job.Confirmed + " ageSec=" + age);
                this.SaveKeybinds();
            }
            else
            {
                // The dish left the stove but no same-identity Count rose: sold/consumed inside the
                // window (negative delta cancels the arrival), the server still moving the stack, or
                // a bizarre/blocked outcome. Log it rather than guess a star.
                ModLogger.Msg("[NetCookQuality] collect outcome: recipe=" + job.RecipeId
                    + " no count increment " + (deltas.Length > 0 ? deltas.ToString() : "(no same-identity stacks seen)")
                    + " stove=" + job.LevelObjectNetId + " confirmed=" + job.Confirmed
                    + " ageSec=" + age + (diffUnreadable > 0 ? " unreadable=" + diffUnreadable : ""));
            }

            // A collect is also a Collection write (§4.3/P0-8: the game updates the pictorial record at
            // the collect moment), so re-arm the Source A scan — and since this just-confirmed dish is
            // exactly what the record may have moved, run that re-arm right here (§4.3-2): both numbers
            // land inside the same settle window and a diff-vs-record mismatch becomes a log line
            // instead of a mystery. The scan is dirty-flag+throttle, so this is not a new per-frame cost.
            this.MarkNetCookCollectionStarDirty();
            this.TryRefreshNetCookCollectionStars();

            if (outcomeStar > 0 && job.RecipeId == this.netCookRecipeId && job.RunGeneration == this.netCookRunGeneration)
            {
                int collectionStar = 0;
                this.netCookCollectionStarById.TryGetValue(job.RecipeId, out collectionStar);
                if (collectionStar >= NetCookStarMin && collectionStar != outcomeStar)
                {
                    ModLogger.Msg("[NetCookQuality] cross-check recipe=" + job.RecipeId
                        + " diff=" + outcomeStar + "* collection=" + collectionStar + "*");
                }

                // R2's only trigger (§4.3-3): Source B, THIS run, 5★ or better. Source A can never
                // drive it — a max cannot say what this cook produced. Drain = no new prepares,
                // everything already on a stove attended and collected as usual.
                if (this.netCookFiveStarModeEngaged && outcomeStar >= NetCookStarMax)
                {
                    this.BeginNetCookDrain("5-star dish achieved.");
                }
            }

            return true;
        }

        // One-recipe slice of the direct-backpack census: star bucket -> summed stack Count. The
        // snapshot must already be refreshed by the caller (both call sites pass forceRefresh:true
        // and read entry.MonoItem in the same synchronous scope — the pins are snapshot-owned).
        // strict=true refuses the whole census if any same-recipe entry has an unreadable star or
        // Count (baseline side: a gap there surfaces at diff time as a false increment built from
        // the old stock's Count). strict=false skips such entries and reports the count (diff side:
        // a gap there only hides an increment). A Count of 0 is treated as suspect: GetAllItem does
        // not report empty stacks, so a 0 means the member read failed — indistinguishable from a
        // real zero and unsafe for deltas either way.
        private bool TryBuildNetCookRecipeStarCensus(int recipeId, Dictionary<int, int> starCounts,
            bool strict, out int unreadable)
        {
            unreadable = 0;
            starCounts.Clear();

            for (int i = 0; i < this.directBackpackRuntimeItems.Count; i++)
            {
                DirectBackpackRuntimeItem item = this.directBackpackRuntimeItems[i];
                if (item == null || item.StaticId != recipeId)
                {
                    continue;
                }

                // Star resolution exactly like AutoSell's identity census (TryResolveAutoSellMonoItemStar):
                // QualityComponent by netId FIRST — the real star of a cooked dish lives in the ECS
                // component, while the stack item's own get_starRate reports 0 (measured live
                // 2026-11-01: every dish bucket landed in 0* and 1..5 stayed empty). The item's own
                // star fields are only the fallback, and a 0 is never a dish star — it means "no star
                // data", so it marks the entry unreadable instead of joining the census.
                int star = 0;
                bool starOk = item.NetId != 0U
                    && this.TryGetAutoSellQualityComponentStar(item.NetId, out star)
                    && star >= NetCookStarMin && star <= NetCookStarMax;
                if (!starOk && item.MonoItem != IntPtr.Zero
                    && this.TryGetDirectBackpackItemStarRate(item.MonoItem, out star))
                {
                    starOk = star >= NetCookStarMin && star <= NetCookStarMax;
                }

                if (!starOk || item.Count <= 0)
                {
                    unreadable++;
                    if (strict)
                    {
                        return false;
                    }

                    continue;
                }

                starCounts[star] = starCounts.TryGetValue(star, out int existing) ? existing + item.Count : item.Count;
            }

            return true;
        }

        private void CreditNetCookQualityObserved(int recipeId, int star, bool countsIntoRun)
        {
            int best;
            if (!this.netCookRecipeBestQuality.TryGetValue(recipeId, out best) || star > best)
            {
                this.netCookRecipeBestQuality[recipeId] = star;
            }

            if (countsIntoRun && star > this.netCookRunBestCompletedQuality)
            {
                this.netCookRunBestCompletedQuality = star;
            }
        }

        // §2.2-b seed: fold the stars of dish-kind stacks already in bag+warehouse into the ledger —
        // recovers achievements from before this feature existed. It is a seed, not a second Source A:
        // for the 232 Collection-covered dishes the record knows more than the bag does (an eaten 5★
        // is invisible to any census), which is exactly why Source A exists. Runs at most once per
        // AuraMono world epoch — and that epoch moves on a SCENE-HANDLE change, not on every world
        // ready: a town-to-town hop that reuses the same scene asset does not bump it and does not
        // re-scan (measured live 2026-11-01: login→town bumped, town→town did not). That is safe by
        // construction — the ledger persists across sessions and worlds (netCookQualityLedger in the
        // config), merging only ever raises a recipe's best star, and every collect job takes its
        // own fresh census baseline, so a missing re-seed can only under-report, never stale-credit.
        // The recipe-id set scopes the scan to dishes the mod cooks.
        // true = done for this epoch (seeded or permanently failed); false = not yet, retry on the
        // caller's sanctioned cadence (world-ready callback) or at the next cache-ready mark.
        private bool SeedNetCookLedgerFromInventory()
        {
            if (this.netCookLedgerSeedEpoch == AuraMonoWorldEpoch)
            {
                return true;
            }

            if (this.netCookRecipeEntries.Count <= 0)
            {
                return false; // Recipe list not built yet — retry on the framework cadence / next mark.
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return false;
            }

            HashSet<int> recipeIds = new HashSet<int>(this.netCookRecipeEntries.Count);
            for (int i = 0; i < this.netCookRecipeEntries.Count; i++)
            {
                recipeIds.Add(this.netCookRecipeEntries[i].Key);
            }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj)
                || backPackSystemObj == IntPtr.Zero)
            {
                return false; // DataModule not constructed yet for this world — retry is legitimate.
            }

            IntPtr backPackClass = auraMonoObjectGetClass(backPackSystemObj);
            IntPtr getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
            bool needsStorageArg = true;
            if (getAllItemMethod == IntPtr.Zero)
            {
                getAllItemMethod = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
                needsStorageArg = false;
            }

            if (getAllItemMethod == IntPtr.Zero)
            {
                if (!this.netCookQualityLedgerSeedFailedLogged)
                {
                    this.netCookQualityLedgerSeedFailedLogged = true;
                    ModLogger.Msg("[NetCookQuality] ledger seed skipped: GetAllItem unresolved");
                }

                this.netCookLedgerSeedEpoch = AuraMonoWorldEpoch;
                return true; // shape failure, not a readiness gap: permanent for this epoch
            }

            // Unconditional Tier-1 summary (errors-and-detail-to-log): the first live pass credited
            // zero stacks and said nothing, leaving no way to tell "bag had no dishes" from "star
            // reads failed". These counters are what disambiguate that next time.
            NetCookLedgerSeedStats stats = new NetCookLedgerSeedStats();
            this.ScanNetCookLedgerSeedStorage(backPackSystemObj, getAllItemMethod, needsStorageArg, 1, recipeIds, stats);
            this.ScanNetCookLedgerSeedStorage(backPackSystemObj, getAllItemMethod, needsStorageArg, 2, recipeIds, stats);

            this.netCookLedgerSeedEpoch = AuraMonoWorldEpoch;
            this.netCookQualityLedgerSeedFailedLogged = false;
            ModLogger.Msg("[NetCookQuality] ledger seed: enumerated=" + stats.Enumerated
                + " recipeMatched=" + stats.RecipeMatched + " starUnreadable=" + stats.StarUnreadable
                + " credited=" + stats.Credited);
            if (stats.Credited > 0)
            {
                this.SaveKeybinds();
            }

            return true;
        }

        private sealed class NetCookLedgerSeedStats
        {
            public int Enumerated;      // non-null stack objects seen across both storages
            public int RecipeMatched;   // stacks whose staticId is a mod-cookable recipe
            public int StarUnreadable;  // matched stacks whose star could not be read
            public int Credited;        // matched stacks folded into the ledger
        }

        // One storage type per call (EStorageType: 1 = Backpack, 2 = Warehouse). No stackalloc in a
        // loop — the two storages are two explicit calls (CA2014 discipline). Pins follow the §11
        // rule: every enumerated item is pinned at enumeration and freed in the finally.
        private unsafe void ScanNetCookLedgerSeedStorage(IntPtr backPackSystemObj, IntPtr getAllItemMethod,
            bool needsStorageArg, int storageValue, HashSet<int> recipeIds, NetCookLedgerSeedStats stats)
        {
            IntPtr exc = IntPtr.Zero;
            IntPtr itemListObj;
            {
                int storage = storageValue;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&storage);
                itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackSystemObj,
                    needsStorageArg ? (IntPtr)args : IntPtr.Zero, ref exc);
            }

            if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
            {
                return;
            }

            List<IntPtr> items = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, pins))
                {
                    return;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr itemObj = items[i];
                    if (itemObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    stats.Enumerated++;

                    if (!this.TryGetDirectBackpackItemStaticId(itemObj, out int staticId)
                        || !recipeIds.Contains(staticId))
                    {
                        continue;
                    }

                    stats.RecipeMatched++;

                    // Same resolution order as the collect census and AutoSell: QualityComponent by
                    // netId first (the dish's real star), item fields as fallback, 0 = no star data.
                    int star = 0;
                    bool starOk = this.TryGetDirectBackpackItemNetId(itemObj, out uint itemNetId)
                        && this.TryGetAutoSellQualityComponentStar(itemNetId, out star)
                        && star >= NetCookStarMin && star <= NetCookStarMax;
                    if (!starOk && this.TryGetDirectBackpackItemStarRate(itemObj, out star))
                    {
                        starOk = star >= NetCookStarMin && star <= NetCookStarMax;
                    }

                    if (!starOk)
                    {
                        stats.StarUnreadable++;
                        continue;
                    }

                    int best;
                    if (!this.netCookRecipeBestQuality.TryGetValue(staticId, out best) || star > best)
                    {
                        this.netCookRecipeBestQuality[staticId] = star;
                    }

                    stats.Credited++;
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        // ---- Persistence ("recipeId=quality;…", the SerializeNetCookSlotPrefs round-trip shape).
        // Only the best-ever ledger is persisted: "what you have achieved" survives relogs and must
        // not vanish when the dishes are eaten or sold (plan §2.2). ----

        internal string SerializeNetCookQualityLedger()
        {
            if (this.netCookRecipeBestQuality.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> entry in this.netCookRecipeBestQuality)
            {
                if (entry.Value < NetCookStarMin || entry.Value > NetCookStarMax)
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(';');
                }

                sb.Append(entry.Key.ToString(CultureInfo.InvariantCulture)).Append('=')
                  .Append(entry.Value.ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        internal void DeserializeNetCookQualityLedger(string raw)
        {
            this.netCookRecipeBestQuality.Clear();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            foreach (string entry in raw.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                int eq = entry.IndexOf('=');
                if (eq <= 0 || eq == entry.Length - 1)
                {
                    continue;
                }

                if (int.TryParse(entry.Substring(0, eq), NumberStyles.Integer, CultureInfo.InvariantCulture, out int recipeId)
                    && int.TryParse(entry.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int star)
                    && recipeId > 0 && star >= NetCookStarMin && star <= NetCookStarMax)
                {
                    this.netCookRecipeBestQuality[recipeId] = star;
                }
            }
        }
    }
}
