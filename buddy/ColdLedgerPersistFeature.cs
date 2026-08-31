using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace HeartopiaMod
{
    // Persists long resource cooldowns across game sessions.
    //
    // WHY. Rare trees and daily rocks carry cooldowns of hours (MapResourceProduce: 86 400 s), but
    // the client only learns a resource is cold when the server pushes its verdict — and that
    // happens on close-range streaming. After a restart the verdict ledger starts empty, so the
    // farm re-targets yesterday's harvest and pays a long walk per tree before the verdict arrives:
    // measured 2026-08-26, three stale rare trees in a row, ~116 m + ~30 m + ~100 m of walking to
    // rediscover cooldowns it had known 13 minutes earlier.
    //
    // WHAT IS PERSISTED — the verdict ledger (collectColdByNetId), NOT the visited stamps.
    //
    //   * The stamps have a deliberate 600 s cap with a history: an 8 h cap was tried once on a
    //     wrong reading and rolled back. They are a backstop corrected mainly by time, so a bad
    //     entry must not be able to park a node for hours. Seeding them with 9 h entries would
    //     re-fight that settled decision.
    //   * The ledger is the authority the mod already trusts over components ("> now => NOT
    //     collectable, whatever the component says"), its own header documents the persistable
    //     property ("verdicts do not go stale: endUnixTimeMs is an ABSOLUTE instant"), and it is
    //     corrected by LIVE data: a fresh event with endMs=0 overwrites a stale entry across
    //     sweeps. A wrong persisted row heals the moment the entity streams in.
    //
    // Seeding the ledger feeds every consumer at once — marker hiding, tour pruning, the walker's
    // mid-walk abandon — with no new code paths on the read side.
    //
    // ⚠️ NETIDS. The ledger used to be cleared with the comment "netIds are per-session". Measured
    // otherwise for static world entities: netId 21118 held across a full game restart, its
    // remaining cooldown down by exactly the elapsed wall clock (32 399 s at 22:29 → 31 644 s at
    // 22:42, Δ = 755 s ≈ 12.6 min). Static resources keep their ids; dynamic bushes are reborn
    // under NEW netIds, so their persisted rows match nothing and expire harmlessly. Should an id
    // ever be reused for a different entity, the wrong "cold" hides one marker until the next
    // sweep corrects it — bounded, and the floor below keeps short-cooldown noise out entirely.
    public partial class HeartopiaComplete
    {
        private const string ColdLedgerFileName = "cold-ledger.tsv";

        // Only cooldowns with at least this much left are worth a disk row: regular trees (120 s)
        // and stones (300 s) expire before a restart matters, while rare trees and daily rocks
        // (86 400 s) are exactly what gets re-walked.
        //
        // ⚠️ This floor does NOT decide what belongs here — the component-agreement test below
        // does. An earlier version of this comment said growing bushes "cost nothing" because
        // their rows die with their netIds; once the store began hiding markers by POSITION that
        // stopped being true, and it cost two separate outages.
        private const long ColdLedgerPersistFloorMs = 30L * 60L * 1000L;

        // A rewrite per new verdict would thrash during sweeps (153 events per pass); batching to a
        // cadence loses at most this many seconds of ledger on a crash.
        private const float ColdLedgerFlushInterval = 30f;

        // Hard cap on rows so the file cannot grow without bound; furthest expiries win because
        // they are the ones a restart would otherwise re-walk.
        private const int ColdLedgerMaxRows = 256;

        private struct PersistedColdEntry
        {
            public long EndUnixMs;
            public Vector3 Position;   // informative: last place the scan saw this netId, or zero
        }

        private readonly System.Collections.Generic.Dictionary<uint, PersistedColdEntry> coldLedgerPersisted =
            new System.Collections.Generic.Dictionary<uint, PersistedColdEntry>(64);

        private bool coldLedgerLoaded;
        private bool coldLedgerDirty;
        private float coldLedgerNextFlushAt;

        // ---- write side ------------------------------------------------------------------------

        // Called from the CollectColdEvent handler after a record lands in the ledger. Cheap by
        // design: the common case (short or zero cooldown) is one subtraction and one compare.
        private void NotePersistableColdVerdict(uint netId, long endUnixMs)
        {
            if (netId == 0u)
            {
                return;
            }

            // ⚠️ ONLY A COOLDOWN THE COMPONENT ITSELF CONFIRMS MAY BE WRITTEN DOWN.
            //
            // A verdict's future end is not always a cooldown. On anything that REGROWS it is a
            // MATURITY, and this store is keyed by position as well as netId — so a row written
            // while a plant was growing keeps hiding that spot long after it has ripened, or
            // after it was collected and a new one grew there under a new netId.
            //
            // Twice measured, twice the same shape:
            //   * three ripe truffles, component inCold=false, invisible with ~5.8 h left on rows
            //     from an earlier session;
            //   * every underwater plant in the bay reading component.inCold=false, end=0 while
            //     the ledger claimed 6-7 h — so the farm skipped a glasswort 21 m away and swam
            //     to contamination at 60 m, because "nearest AVAILABLE" had nothing else left.
            //
            // The first cut excluded a staticId RANGE (the mushroom ids). That is the wrong shape
            // of rule: the underwater plants carry staticIds 1, 4, 9, 14-18 and sailed straight
            // through it. What actually separates the two cases is not an id but AGREEMENT — for
            // the things this file exists for (rare trees, daily rocks) the component's inCold is
            // the trustworthy flag (FARM_WALK_TO_NODE.md §7b), and it says so when they cool.
            // A regrowing plant never sets it.
            //
            // Requiring the live entry costs nothing either: these verdicts arrive when the
            // entity streams in close, which is exactly when the scan holds it.
            bool componentConfirmsCold = false;
            bool seenInScan = false;
            Vector3 pos = Vector3.zero;
            for (int i = 0; i < this.liveCollectableColds.Count; i++)
            {
                if (this.liveCollectableColds[i].NetId == netId)
                {
                    seenInScan = true;
                    pos = this.liveCollectableColds[i].Position;
                    componentConfirmsCold = this.liveCollectableColds[i].OnCooldown;
                    break;
                }
            }

            if (seenInScan && !componentConfirmsCold)
            {
                // Growing, not cooling. Drop anything written for it before this rule existed —
                // that is what clears the rows already on disk.
                if (this.coldLedgerPersisted.Remove(netId))
                {
                    this.coldLedgerDirty = true;
                }

                return;
            }

            long nowMs = NowUnixMs();
            if (endUnixMs > nowMs + ColdLedgerPersistFloorMs)
            {

                // Re-broadcast sweeps deliver the same end time every pass; a real change (new
                // cooldown, first sighting) dirties the file — and so does FINALLY LEARNING THE
                // POSITION. A verdict often lands a beat before the 2 s scan refresh knows the
                // entity, so rows were written as (0,0,0) and never healed: the end never
                // changed, the early return always fired, and a position-less row cannot hide
                // any marker (measured: 3 of 4 rare-tree rows sat at 0,0,0).
                if (this.coldLedgerPersisted.TryGetValue(netId, out PersistedColdEntry existing)
                    && Math.Abs(existing.EndUnixMs - endUnixMs) < 1000L
                    && (existing.Position.sqrMagnitude > 0.01f || pos.sqrMagnitude < 0.01f))
                {
                    return;
                }

                this.coldLedgerPersisted[netId] = new PersistedColdEntry
                {
                    EndUnixMs = endUnixMs,
                    Position = pos,
                };
                this.coldLedgerDirty = true;
                return;
            }

            // Went warm (or the cooldown is short now): a stale row must not out-persist the
            // truth, or a server-side reset would stay hidden until the row's expiry.
            if (this.coldLedgerPersisted.Remove(netId))
            {
                this.coldLedgerDirty = true;
            }
        }

        // ---- seed side -------------------------------------------------------------------------

        // Re-seeds the (just cleared) ledger from disk. Called on every world-ready, which is the
        // one place the ledger is emptied.
        private void SeedPersistedColdLedger()
        {
            this.EnsureColdLedgerLoaded();

            long nowMs = NowUnixMs();
            int seeded = 0;
            long longestLeftMs = 0L;
            System.Collections.Generic.List<uint> expired = null;

            foreach (var pair in this.coldLedgerPersisted)
            {
                long leftMs = pair.Value.EndUnixMs - nowMs;
                if (leftMs <= 0L)
                {
                    (expired = expired ?? new System.Collections.Generic.List<uint>()).Add(pair.Key);
                    continue;
                }

                // Never overwrite a live record: anything already in the ledger was heard from the
                // game THIS session and is fresher than the file by definition.
                if (this.collectColdByNetId.ContainsKey(pair.Key))
                {
                    continue;
                }

                this.collectColdByNetId[pair.Key] = new CollectColdRecord
                {
                    EndUnixMs = pair.Value.EndUnixMs,
                    AvailableNum = 0,
                    SeenAt = Time.unscaledTime,   // "heard Ns ago" then counts from this seed
                };
                seeded++;
                if (leftMs > longestLeftMs)
                {
                    longestLeftMs = leftMs;
                }
            }

            if (expired != null)
            {
                for (int i = 0; i < expired.Count; i++)
                {
                    this.coldLedgerPersisted.Remove(expired[i]);
                }

                this.coldLedgerDirty = true;
            }

            if (seeded > 0)
            {
                ModLogger.Msg("[ColdLedger] restored " + seeded + " long cooldown(s) from the previous "
                    + "session (longest " + (longestLeftMs / 3600000.0).ToString("F1", CultureInfo.InvariantCulture)
                    + "h) — their markers stay hidden instead of being re-walked.");
            }
        }

        // ---- file ------------------------------------------------------------------------------

        private void EnsureColdLedgerLoaded()
        {
            if (this.coldLedgerLoaded)
            {
                return;
            }

            this.coldLedgerLoaded = true;
            try
            {
                string path = HelperPaths.GetFile(ColdLedgerFileName);
                if (!File.Exists(path))
                {
                    return;
                }

                long nowMs = NowUnixMs();
                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrEmpty(line) || line[0] == '#')
                    {
                        continue;
                    }

                    string[] parts = line.Split('\t');
                    if (parts.Length < 2
                        || !uint.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint netId)
                        || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long endMs))
                    {
                        continue;
                    }

                    if (endMs <= nowMs)
                    {
                        this.coldLedgerDirty = true;   // expired rows drop on the next flush
                        continue;
                    }

                    Vector3 pos = Vector3.zero;
                    if (parts.Length >= 5
                        && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                        && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                        && float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        pos = new Vector3(x, y, z);
                    }

                    this.coldLedgerPersisted[netId] = new PersistedColdEntry { EndUnixMs = endMs, Position = pos };
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[ColdLedger] load failed: " + ex.Message);
            }
        }

        // Full rewrite, like the coordinate harvest: the set lives in memory, and a rewrite
        // survives a crash better than a stream held open.
        private void FlushPersistedColdLedger(bool force)
        {
            if (!this.coldLedgerDirty)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (!force && now < this.coldLedgerNextFlushAt)
            {
                return;
            }

            this.coldLedgerNextFlushAt = now + ColdLedgerFlushInterval;
            this.coldLedgerDirty = false;
            try
            {
                long nowMs = NowUnixMs();
                var rows = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<uint, PersistedColdEntry>>(this.coldLedgerPersisted.Count);
                foreach (var pair in this.coldLedgerPersisted)
                {
                    if (pair.Value.EndUnixMs > nowMs)
                    {
                        rows.Add(pair);
                    }
                }

                // Furthest expiry first; the cap trims what a restart would least miss.
                rows.Sort((a, b) => b.Value.EndUnixMs.CompareTo(a.Value.EndUnixMs));
                if (rows.Count > ColdLedgerMaxRows)
                {
                    rows.RemoveRange(ColdLedgerMaxRows, rows.Count - ColdLedgerMaxRows);
                }

                var sb = new System.Text.StringBuilder(rows.Count * 48 + 64);
                sb.Append("# netId\tendUnixMs\tx\ty\tz — long resource cooldowns, reseeded on world-ready\n");
                foreach (var pair in rows)
                {
                    sb.Append(pair.Key.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(pair.Value.EndUnixMs.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(pair.Value.Position.x.ToString("F1", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(pair.Value.Position.y.ToString("F1", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(pair.Value.Position.z.ToString("F1", CultureInfo.InvariantCulture)).Append('\n');
                }

                File.WriteAllText(HelperPaths.GetFile(ColdLedgerFileName), sb.ToString());
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[ColdLedger] flush failed: " + ex.Message);
            }
        }

        private void ProcessColdLedgerOnUpdate()
        {
            this.FlushPersistedColdLedger(false);
        }

        // Is there a persisted long cooldown at this spot? The far-range fallback for the marker
        // hide: beyond streaming range TryReadEntityNetId answers 0, so the netId-keyed ledger
        // cannot speak for the entity — but its POSITION still can. Without this the farm
        // toured eight dead rare trees in one run (23:15 log), each discovered cold only at
        // ~40 m when the entity streamed in and the verdict finally had a netId to land on.
        internal bool TryGetPersistedColdAtPosition(Vector3 position, out long endUnixMs)
        {
            endUnixMs = 0L;
            this.EnsureColdLedgerLoaded();
            long nowMs = NowUnixMs();
            foreach (var pair in this.coldLedgerPersisted)
            {
                Vector3 p = pair.Value.Position;
                if (p.sqrMagnitude < 0.01f || pair.Value.EndUnixMs <= nowMs)
                {
                    continue;   // a zero position identifies nothing, an expired row hides nothing
                }

                float dx = p.x - position.x;
                float dz = p.z - position.z;
                if ((dx * dx) + (dz * dz) <= 2.25f)
                {
                    endUnixMs = pair.Value.EndUnixMs;
                    return true;
                }
            }

            return false;
        }
    }
}
