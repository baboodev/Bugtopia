using System;
using System.Collections;
using System.Globalization;
using HeartopiaMod.Plugins;
using UnityEngine;

namespace SamplePlugin
{
    // Reference sandbox plugin. Exercises every part of the contract that matters for hot reload:
    // a Tick, a host-managed coroutine (the classic thing that pins a load context when it is not
    // revoked), and a Call entry point for `plugin.call`.
    //
    // Edit, rebuild, `plugin.load` again with the same id — the host unloads the previous version
    // first. No game restart.
    public sealed class SampleProbe : IBugtopiaPlugin
    {
        private IHostApi host;
        private long ticks;
        private long coroutineBeats;
        private DateTime loadedUtc;

        public void Load(IHostApi api)
        {
            this.host = api;
            this.loadedUtc = DateTime.UtcNow;
            this.host.Log("loaded — sdk v" + PluginSdk.Version);

            // Started through the host, never as a Thread/Timer: the host stops it on unload, and a
            // surviving iterator would keep this whole assembly's load context alive forever (the
            // iterator is a type from THIS assembly).
            this.host.StartCoroutine(this.Heartbeat());
        }

        public void Tick()
        {
            this.ticks++;
        }

        public void Unload()
        {
            this.host?.Log("unloading after " + this.ticks + " ticks, " + this.coroutineBeats + " beats");
            this.host = null;
        }

        public string Call(string method, string argsJson)
        {
            switch (method)
            {
                case "stats":
                    return this.Stats();

                case "throw":
                    // Proves a plugin fault is contained: the host answers with plugin_error and
                    // keeps running.
                    throw new InvalidOperationException("deliberate failure from the sample plugin");

                default:
                    return null;
            }
        }

        private string Stats()
        {
            bool haveWorld = this.host != null && this.host.IsWorldReady;
            bool havePos = this.host != null && this.host.TryGetPlayerPosition(out Vector3 pos);
            Vector3 position = Vector3.zero;
            if (havePos)
            {
                this.host.TryGetPlayerPosition(out position);
            }

            return "{\"ticks\":" + this.ticks.ToString(CultureInfo.InvariantCulture)
                + ",\"beats\":" + this.coroutineBeats.ToString(CultureInfo.InvariantCulture)
                + ",\"worldReady\":" + (haveWorld ? "true" : "false")
                + ",\"epoch\":" + (this.host == null ? 0 : this.host.WorldEpoch).ToString(CultureInfo.InvariantCulture)
                + ",\"position\":{\"x\":" + position.x.ToString("F2", CultureInfo.InvariantCulture)
                + ",\"y\":" + position.y.ToString("F2", CultureInfo.InvariantCulture)
                + ",\"z\":" + position.z.ToString("F2", CultureInfo.InvariantCulture) + "}"
                + ",\"loadedUtc\":\"" + this.loadedUtc.ToString("O", CultureInfo.InvariantCulture) + "\"}";
        }

        private IEnumerator Heartbeat()
        {
            while (true)
            {
                yield return ModWait.Realtime(1f);
                this.coroutineBeats++;
            }
        }
    }
}
