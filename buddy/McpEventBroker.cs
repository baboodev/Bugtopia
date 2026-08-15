#if FEATURE_MCP
using System;
using System.Collections.Generic;

namespace HeartopiaMod
{
    // ============================================================================================
    // Event subscriptions for sandbox code.
    //
    // ── WHY A BROKER AND NOT A DIRECT SUBSCRIPTION ───────────────────────────────────────────────
    // `RegisterGameEventHook` appends a handler and there is NO unsubscribe path — none exists in
    // the engine, by design, because the underlying registration installs a native detour and this
    // mod never tears detours down. A plugin that subscribed directly would leave its delegate in
    // the engine's handler list forever, and that delegate is a PLUGIN type, so its load context
    // could never be collected. Hot reload would silently stop working after the first subscription.
    //
    // So the registration with the engine is host-owned, made once per event type, and permanent —
    // exactly the treatment detours get. What the plugin supplies is an entry in a mutable list that
    // the host clears on unload. The engine keeps calling a delegate that belongs to bugtopia.dll;
    // whether anyone is listening behind it is the broker's business.
    //
    // ── THE SLOT BUDGET IS REAL ──────────────────────────────────────────────────────────────────
    // Each distinct event type costs one hook slot and the engine has 48 of them, ~38 already spoken
    // for by shipped features. Subscribing to an event some feature already hooks costs NOTHING
    // extra — the engine appends to the existing entry. Subscribing to a new type spends a slot that
    // is never reclaimed, so the count is reported and a failure says which limit was hit.
    // ============================================================================================
    internal static class McpEventBroker
    {
        private sealed class Subscription
        {
            internal string PluginId;
            internal Action<HeartopiaComplete.GameEventSnapshot> Handler;
        }

        // event name -> live subscribers. The engine-side registration for that name is made once
        // and never removed; this list is what actually changes.
        private static readonly Dictionary<string, List<Subscription>> Subscribers =
            new Dictionary<string, List<Subscription>>(StringComparer.Ordinal);

        // Event names this broker has already registered with the engine.
        private static readonly HashSet<string> EngineRegistered = new HashSet<string>(StringComparer.Ordinal);

        private static long dispatched;

        internal static long Dispatched => dispatched;

        internal static int SubscribedEventCount => Subscribers.Count;

        internal static bool Subscribe(string pluginId, string eventFullName, int payloadBytes,
                                       Action<HeartopiaComplete.GameEventSnapshot> handler, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(eventFullName))
            {
                error = "event type name is required";
                return false;
            }

            if (handler == null)
            {
                error = "handler is required";
                return false;
            }

            HeartopiaComplete mod = HeartopiaComplete.Instance;
            if (mod == null)
            {
                error = "mod instance unavailable";
                return false;
            }

            if (!Subscribers.TryGetValue(eventFullName, out List<Subscription> list))
            {
                list = new List<Subscription>();
                Subscribers[eventFullName] = list;
            }

            if (!EngineRegistered.Contains(eventFullName))
            {
                // One permanent registration per event type. The delegate handed over is a host
                // method, so nothing here can ever pin a plugin's load context.
                if (!mod.RegisterGameEventHook(eventFullName, payloadBytes, OnGameEvent))
                {
                    error = "the engine refused the hook for '" + eventFullName
                        + "' — the type may not exist, or the 48-slot budget is exhausted "
                        + "(subscribing to an event a shipped feature already hooks costs no slot)";
                    return false;
                }

                EngineRegistered.Add(eventFullName);
            }

            list.Add(new Subscription { PluginId = pluginId, Handler = handler });
            return true;
        }

        // Called by the engine on the main thread, for every dispatch of every subscribed type.
        private static void OnGameEvent(HeartopiaComplete.GameEventSnapshot snapshot)
        {
            if (!Subscribers.TryGetValue(snapshot.EventName, out List<Subscription> list) || list.Count == 0)
            {
                return;
            }

            dispatched++;

            // Snapshot the list: a handler is allowed to get its own plugin unloaded, which mutates
            // the collection being walked.
            Subscription[] current = list.ToArray();
            for (int i = 0; i < current.Length; i++)
            {
                try
                {
                    current[i].Handler(snapshot);
                }
                catch (Exception ex)
                {
                    // One plugin's bad handler must not cost the other subscribers their event, nor
                    // throw into the engine's drain.
                    ModLogger.Warning("[Mcp] plugin '" + current[i].PluginId + "' threw handling "
                        + snapshot.EventName + ": " + ex.Message);
                }
            }
        }

        // Called from the plugin host on unload. This is what makes the whole arrangement safe: the
        // engine registration stays, the plugin's delegate goes.
        internal static int RevokeAll(string pluginId)
        {
            int removed = 0;
            foreach (KeyValuePair<string, List<Subscription>> pair in Subscribers)
            {
                List<Subscription> list = pair.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(list[i].PluginId, pluginId, StringComparison.Ordinal))
                    {
                        list.RemoveAt(i);
                        removed++;
                    }
                }
            }

            return removed;
        }

        internal static void WriteStatusJson(McpJsonWriter w)
        {
            w.Num("subscribedTypes", Subscribers.Count);
            w.Num("dispatched", dispatched);
            w.BeginArray("subscriptions");
            foreach (KeyValuePair<string, List<Subscription>> pair in Subscribers)
            {
                w.BeginArrayObject();
                w.Str("event", pair.Key);
                w.Num("subscribers", pair.Value.Count);
                w.Bool("engineRegistered", EngineRegistered.Contains(pair.Key));
                w.EndObject();
            }

            w.EndArray();
        }
    }
}
#endif
