#if FEATURE_MCP
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // MCP data ops (phase 2b) — inventory, quests, and the live event stream.
    //
    // ── WHY THESE ARE SAFE DESPITE BEING AURAMONO-BACKED ─────────────────────────────────────────
    // None of this file talks to AuraMono. It reuses scans the mod already owns and that already
    // handle pinning correctly:
    //
    //   backpack -> ScanBackpackForAutoSellItems()  (HeartopiaComplete.AutoSell.cs) — the pinned
    //               enumeration AGENTS.md §11 cites as the reference implementation.
    //   quests   -> questAssistantSnapshot          (HeartopiaComplete.QuestAssistant.cs)
    //   events   -> the hook engine's own drain     (HeartopiaComplete.EventHook.cs)
    //
    // Both scan results are FULLY SCALAR (strings, ints, bools) by the time they reach us, so
    // caching them across frames is legal — the "never cache a MonoObject* in a raw IntPtr" rule
    // (CI lint E3) is about native pointers, and there are none here.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        private const float McpBackpackMinIntervalSeconds = 2f;

        private List<AutoSellBagItemEntry> mcpBackpackSnapshot;
        private float mcpBackpackSnapshotAt = float.NegativeInfinity;
        private double mcpBackpackScanMs;

        // Event-driven invalidation instead of a poll. The scan measures ~49 ms on a full inventory
        // — three frames — so re-running it on a TTL would hand an agent a stutter every few seconds
        // for data that usually has not changed. RefreshBackPackEvent is dispatched by
        // StorageBase.AddItem on every bag mutation, so the cache can be served indefinitely until
        // one actually arrives (docs/GAME_EVENTS.md; "events first", AGENTS.md §7).
        //
        // This costs NO hook slot: AutoSell already registers this event name, and the engine appends
        // a handler to the existing entry rather than allocating a second slot out of the 48.
        private const string McpBackpackEventName = "XDTDataAndProtocol.Events.RefreshBackPackEvent";
        private const int McpBackpackEventBytes = 4;
        private bool mcpBackpackDirty = true; // first call always scans
        private bool mcpDataEventHooksRegistered;
        private long mcpBackpackInvalidations;

        // Registered on the world-ready gate, never from OnUpdate (AGENTS.md §1 hard rule).
        private bool EnsureMcpDataEventHooks()
        {
            if (this.mcpDataEventHooksRegistered)
            {
                return true;
            }

            this.mcpDataEventHooksRegistered = true;
            this.RegisterGameEventHook(McpBackpackEventName, McpBackpackEventBytes, this.OnMcpRefreshBackPackEvent);
            return true;
        }

        // Main thread (event drain). Deliberately does NOT read the storage type: backpack.list
        // reports warehouse rows too, so any storage mutation is a reason to re-scan — unlike Auto
        // Sell, which only cares about the player backpack.
        private void OnMcpRefreshBackPackEvent(GameEventSnapshot e)
        {
            this.mcpBackpackDirty = true;
            this.mcpBackpackInvalidations++;
        }

        // ── Event observation log ────────────────────────────────────────────────────────────────
        // The hook engine already has a ring, but it is a CONSUMED QUEUE: its read cursor is advanced
        // by the drain that delivers events to handlers, so reading it here would steal events from
        // the features that asked for them. This is a separate, write-only observation log, appended
        // from the drain (main thread) rather than from the detour body — the detour must not
        // allocate or call into Mono, and appending a string reference there would be a landmine
        // waiting for someone to "improve" it into a formatted message.
        private const int McpEventLogSize = 256;
        private static readonly string[] McpEventLogName = new string[McpEventLogSize];
        private static readonly uint[] McpEventLogNetId = new uint[McpEventLogSize];
        private static readonly float[] McpEventLogTime = new float[McpEventLogSize];
        private static readonly int[] McpEventLogLen = new int[McpEventLogSize];
        private static int mcpEventLogWrite;
        private static int mcpEventLogCount;
        private static long mcpEventLogTotal;

        // Off unless the bridge is up: with no agent listening this is one bool test per dispatched
        // event, on a path that can fire hundreds of times a second.
        internal static bool McpEventLogEnabled;

        // Called from the event-hook drain (main thread) for every dispatch that reaches a handler.
        internal static void McpNoteGameEvent(string eventFullName, uint netId, int payloadLen)
        {
            if (!McpEventLogEnabled)
            {
                return;
            }

            try
            {
                int idx = mcpEventLogWrite & (McpEventLogSize - 1);
                McpEventLogName[idx] = eventFullName;
                McpEventLogNetId[idx] = netId;
                McpEventLogLen[idx] = payloadLen;
                McpEventLogTime[idx] = Time.unscaledTime;
                mcpEventLogWrite++;
                mcpEventLogTotal++;
                if (mcpEventLogCount < McpEventLogSize)
                {
                    mcpEventLogCount++;
                }
            }
            catch
            {
                // Never throw into the drain: one bad append must not cost a feature its event.
            }
        }

        private void RegisterMcpDataOps()
        {
            McpOps.Register(
                "backpack.list",
                McpOpFlags.Read | McpOpFlags.NeedsWorld,
                McpOpCost.Heavy,
                "Items in the backpack (and warehouse when it is open), merged by identity with star "
                + "breakdown. This is the same census Auto Sell acts on, so what you see here is what "
                + "it would sell. Cached and invalidated by the game's own bag-change event, not by a "
                + "clock — a large ageMs with dirty:false means nothing has changed, not that the data "
                + "is stale. The scan costs ~50 ms, so avoid refresh:true in a loop.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"name\":{\"type\":\"string\",\"description\":\"case-insensitive substring of the display name\"},"
                + "\"staticId\":{\"type\":\"integer\",\"description\":\"exact item static id\"},"
                + "\"limit\":{\"type\":\"integer\",\"description\":\"max rows, default 200, max 1000\"},"
                + "\"refresh\":{\"type\":\"boolean\",\"description\":\"force a rescan even if unchanged (rate-limited to one per 2 s)\"}},"
                + "\"additionalProperties\":false}",
                this.HandleMcpBackpackList);

            McpOps.Register(
                "quests.list",
                McpOpFlags.Read | McpOpFlags.NeedsWorld,
                McpOpCost.Cheap,
                "Quest snapshot with per-condition progress. Serves what Quest Assistant last "
                + "resolved and does NOT scan on its own — pass refresh:true to run the same scan its "
                + "button runs (self-throttled). An empty list with status 'Idle' means it has never "
                + "been asked to scan this session.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"refresh\":{\"type\":\"boolean\",\"description\":\"trigger a rescan before answering (async: the result lands in a later call)\"},"
                + "\"conditions\":{\"type\":\"boolean\",\"description\":\"include per-condition rows, default true\"}},"
                + "\"additionalProperties\":false}",
                this.HandleMcpQuestsList);

            McpOps.Register(
                "events.tail",
                McpOpFlags.Read,
                McpOpCost.Cheap,
                "Recent EventCenter dispatches, newest last. IMPORTANT: only event types the mod has "
                + "actually hooked appear here — the response lists them under `watching`. An empty "
                + "tail with a non-empty watch list means nothing fired, not that nothing happened.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"n\":{\"type\":\"integer\",\"description\":\"max rows (1-256, default 50)\"},"
                + "\"filter\":{\"type\":\"string\",\"description\":\"case-insensitive substring of the event type name\"},"
                + "\"watching\":{\"type\":\"boolean\",\"description\":\"include the hooked-event list, default true\"}},"
                + "\"additionalProperties\":false}",
                this.HandleMcpEventsTail);
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // backpack.list
        // ────────────────────────────────────────────────────────────────────────────────────────

        private string HandleMcpBackpackList(Dictionary<string, object> args)
        {
            int limit = McpArgs.GetInt(args, "limit", 200);
            if (limit < 1) { limit = 1; } else if (limit > 1000) { limit = 1000; }

            string nameFilter = McpArgs.GetString(args, "name");
            int staticIdFilter = McpArgs.GetInt(args, "staticId", 0);
            bool refresh = McpArgs.GetBool(args, "refresh", false);

            float now = Time.unscaledTime;
            float age = now - this.mcpBackpackSnapshotAt;

            // No TTL: a cached inventory is stale only when the game says so. The min interval still
            // applies, so a burst of adds (looting, a big craft) coalesces into one rescan instead of
            // one per item.
            bool wasDirty = this.mcpBackpackDirty;
            bool stale = this.mcpBackpackSnapshot == null || wasDirty;

            if ((stale || refresh) && (this.mcpBackpackSnapshot == null || age >= McpBackpackMinIntervalSeconds))
            {
                this.mcpBackpackDirty = false;
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                this.mcpBackpackSnapshot = this.ScanBackpackForAutoSellItems();
                sw.Stop();
                this.mcpBackpackScanMs = sw.Elapsed.TotalMilliseconds;
                this.mcpBackpackSnapshotAt = now;
                age = 0f;
            }

            List<AutoSellBagItemEntry> items = this.mcpBackpackSnapshot;
            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Num("ageMs", age * 1000f);
            w.Num("scanMs", this.mcpBackpackScanMs);
            // A stale-looking ageMs is fine and expected: the cache is invalidated by the game's own
            // RefreshBackPackEvent, not by a clock. `dirty` says a change arrived but the min
            // interval has not elapsed yet; `invalidations` is how many bag mutations were seen.
            w.Bool("dirty", this.mcpBackpackDirty);
            w.Num("invalidations", this.mcpBackpackInvalidations);
            w.Num("total", items == null ? 0 : items.Count);

            int matched = 0;
            int emitted = 0;
            w.BeginArray("items");
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    AutoSellBagItemEntry item = items[i];
                    if (item == null)
                    {
                        continue;
                    }

                    if (staticIdFilter != 0 && item.StaticId != staticIdFilter)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(nameFilter)
                        && (item.DisplayName == null
                            || item.DisplayName.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        continue;
                    }

                    matched++;
                    if (emitted >= limit)
                    {
                        continue;
                    }

                    emitted++;
                    w.BeginArrayObject();
                    w.Str("name", item.DisplayName);
                    w.Num("staticId", item.StaticId);
                    w.Num("count", item.Count);
                    w.Num("netId", item.NetId);
                    w.Num("entityType", item.EntityType);
                    w.Num("starRate", item.StarRate);
                    w.Bool("backpack", item.FromBackpack);
                    w.Bool("warehouse", item.FromWarehouse);

                    // Star breakdown only when it carries information — most items are unstarred and
                    // an array of six zeroes on every row is pure payload.
                    if (item.StarCounts != null)
                    {
                        bool anyStars = false;
                        for (int s = 0; s < item.StarCounts.Length; s++)
                        {
                            if (item.StarCounts[s] != 0)
                            {
                                anyStars = true;
                                break;
                            }
                        }

                        if (anyStars)
                        {
                            w.BeginArray("stars");
                            for (int s = 0; s < item.StarCounts.Length; s++)
                            {
                                w.ArrayStr(item.StarCounts[s].ToString());
                            }

                            w.EndArray();
                        }
                    }

                    w.EndObject();
                }
            }

            w.EndArray();
            w.Num("matched", matched);
            w.Num("returned", emitted);
            w.EndObject();
            return w.ToString();
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // quests.list
        // ────────────────────────────────────────────────────────────────────────────────────────

        private string HandleMcpQuestsList(Dictionary<string, object> args)
        {
            bool refresh = McpArgs.GetBool(args, "refresh", false);
            bool withConditions = McpArgs.GetBool(args, "conditions", true);

            if (refresh)
            {
                // The same entry point the UI button uses — it is self-throttled and sets its own
                // busy flag, so an agent looping refresh:true cannot make it re-enter. Deliberately
                // NOT awaited: the scan finishes on later frames, so this call answers with the
                // PREVIOUS snapshot and the next one sees the new data.
                try
                {
                    this.QuestAssistantOnDumpButtonClicked();
                }
                catch (Exception ex)
                {
                    ModLogger.Warning("[Mcp] quests refresh failed: " + ex.Message);
                }
            }

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Str("status", this.questAssistantLastStatus);
            w.Bool("busy", this.questAssistantBusy);
            w.Bool("refreshRequested", refresh);
            w.Num("available", this.questAssistantAvailable == null ? 0 : this.questAssistantAvailable.Count);

            List<QuestSnapshot> quests = this.questAssistantSnapshot;
            w.Num("total", quests == null ? 0 : quests.Count);
            w.BeginArray("quests");
            if (quests != null)
            {
                for (int i = 0; i < quests.Count; i++)
                {
                    QuestSnapshot q = quests[i];
                    if (q == null)
                    {
                        continue;
                    }

                    w.BeginArrayObject();
                    w.Str("name", q.Name);
                    w.Num("taskId", q.TaskId);
                    w.Num("taskNetId", q.TaskNetId);
                    w.Num("state", q.State);
                    w.Num("category", q.Category);
                    w.Bool("failed", q.IsFailed);
                    w.Str("objective", q.ObjectiveKind.ToString());
                    w.Num("objectiveTargetId", q.ObjectiveTargetId);
                    if (q.SubmitNpcId != 0)
                    {
                        w.Num("submitNpcId", q.SubmitNpcId);
                    }

                    if (q.ObjectiveAreaId != 0)
                    {
                        w.Num("objectiveAreaId", q.ObjectiveAreaId);
                    }

                    if (q.ObjectiveTargetIds != null && q.ObjectiveTargetIds.Count > 1)
                    {
                        // Plural only when it IS plural: a "collect any of these N" condition carries
                        // every qualifying id, and acting on just the first one is a known bug shape.
                        w.BeginArray("objectiveTargetIds");
                        for (int t = 0; t < q.ObjectiveTargetIds.Count; t++)
                        {
                            w.ArrayStr(q.ObjectiveTargetIds[t].ToString());
                        }

                        w.EndArray();
                    }

                    if (withConditions && q.Conditions != null && q.Conditions.Count > 0)
                    {
                        w.BeginArray("conditions");
                        for (int c = 0; c < q.Conditions.Count; c++)
                        {
                            ConditionSnapshot cond = q.Conditions[c];
                            if (cond == null)
                            {
                                continue;
                            }

                            w.BeginArrayObject();
                            w.Str("desc", cond.Description);
                            w.Num("current", cond.Current);
                            w.Num("needed", cond.Needed);
                            w.Bool("complete", cond.Complete);
                            w.Num("checkType", cond.CheckType);
                            if (!string.IsNullOrEmpty(cond.TypeParam))
                            {
                                w.Str("typeParam", cond.TypeParam);
                            }

                            w.EndObject();
                        }

                        w.EndArray();
                    }

                    w.EndObject();
                }
            }

            w.EndArray();
            w.EndObject();
            return w.ToString();
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // events.tail
        // ────────────────────────────────────────────────────────────────────────────────────────

        private string HandleMcpEventsTail(Dictionary<string, object> args)
        {
            int n = McpArgs.GetInt(args, "n", 50);
            if (n < 1) { n = 1; } else if (n > McpEventLogSize) { n = McpEventLogSize; }

            string filter = McpArgs.GetString(args, "filter");
            bool withWatching = McpArgs.GetBool(args, "watching", true);
            float now = Time.unscaledTime;

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Bool("enabled", McpEventLogEnabled);
            w.Num("totalObserved", mcpEventLogTotal);
            w.Num("ringSize", McpEventLogSize);

            // Oldest-to-newest walk over the live window, filtered, then trimmed to the last n.
            List<int> hits = new List<int>(Math.Min(n, McpEventLogSize));
            int start = mcpEventLogWrite - mcpEventLogCount;
            for (int i = 0; i < mcpEventLogCount; i++)
            {
                int idx = (start + i) & (McpEventLogSize - 1);
                string name = McpEventLogName[idx];
                if (name == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(filter)
                    && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                hits.Add(idx);
            }

            int first = hits.Count > n ? hits.Count - n : 0;
            w.Num("matched", hits.Count);
            w.BeginArray("events");
            for (int i = first; i < hits.Count; i++)
            {
                int idx = hits[i];
                w.BeginArrayObject();
                w.Str("event", McpEventLogName[idx]);
                w.Num("ageMs", (now - McpEventLogTime[idx]) * 1000f);
                if (McpEventLogNetId[idx] != 0u)
                {
                    w.Num("netId", McpEventLogNetId[idx]);
                }

                w.Num("payloadBytes", McpEventLogLen[idx]);
                w.EndObject();
            }

            w.EndArray();

            // Without this an empty tail is ambiguous between "nothing fired" and "nothing is even
            // being listened to" — and the second is the common case, because only event types some
            // feature asked for are hooked at all.
            if (withWatching)
            {
                w.BeginArray("watching");
                try
                {
                    foreach (KeyValuePair<string, GameEventHookEntry> pair in this.gameEventHooksByName)
                    {
                        GameEventHookEntry entry = pair.Value;
                        if (entry == null)
                        {
                            continue;
                        }

                        w.BeginArrayObject();
                        w.Str("event", entry.EventFullName);
                        w.Bool("installed", entry.Installed);
                        w.Num("handlers", entry.Handlers == null ? 0 : entry.Handlers.Count);
                        w.Bool("byNetId", entry.ByNetId);
                        w.EndObject();
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Warning("[Mcp] events.tail watch list failed: " + ex.Message);
                }

                w.EndArray();
            }

            // Sandbox subscriptions are a different channel from the shipped features' hooks, and
            // reporting them together would hide which is which.
            w.BeginObject("sandbox");
            McpEventBroker.WriteStatusJson(w);
            w.EndObject();

            w.EndObject();
            return w.ToString();
        }
    }
}
#endif
