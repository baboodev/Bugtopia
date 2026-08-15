using System;
using System.Collections;
using System.Globalization;
using HeartopiaMod.Plugins;
using UnityEngine;

namespace SamplePlugin
{
    // Reference sandbox plugin. Exercises every part of the contract that matters for hot reload:
    // a Tick, a host-managed coroutine (the classic thing that pins a load context when it is not
    // revoked), a page of its own controls in the menu's Agent tab, and a Call entry point for
    // `plugin.call`.
    //
    // Edit, rebuild, `plugin.load` again with the same id — the host unloads the previous version
    // first. No game restart.
    public sealed class SampleProbe : IBugtopiaPlugin
    {
        private IHostApi host;
        private long ticks;
        private long coroutineBeats;
        private DateTime loadedUtc;
        private IPluginLabel liveLine;
        private bool countTicks = true;

        public void Load(IHostApi api)
        {
            this.host = api;
            this.loadedUtc = DateTime.UtcNow;
            this.host.Log("loaded — sdk v" + PluginSdk.Version);

            // Started through the host, never as a Thread/Timer: the host stops it on unload, and a
            // surviving iterator would keep this whole assembly's load context alive forever (the
            // iterator is a type from THIS assembly).
            this.host.StartCoroutine(this.Heartbeat());

            this.BuildPage();
        }

        // The page is REMOVED FOR US on unload, callbacks and all — which is the only reason a
        // button may safely close over `this`. Nothing here needs undoing in Unload().
        private void BuildPage()
        {
            IPluginPage page = this.host.Ui.AddPage("Sample Probe");
            if (page == null)
            {
                return; // mod shutting down
            }

            page.AddNote("Everything on this page belongs to the sample plugin. Unload it and the "
                         + "page, its sub-tab and these handlers go with it.");
            this.liveLine = page.AddLabel("ticks 0 · beats 0");
            page.AddToggle("Count ticks", this.countTicks, v =>
            {
                this.countTicks = v;
                this.host.Log("countTicks -> " + v);
            });
            page.AddButton("Log stats", () => this.host.Log(this.Stats()));
            page.AddButton("Toast position", () =>
            {
                this.host.Toast(this.host.TryGetPlayerPosition(out Vector3 p)
                    ? "at " + p.ToString("F1")
                    : "no player anchor");
            });
        }

        public void Tick()
        {
            if (this.countTicks)
            {
                this.ticks++;
            }

            // SetText in place, never a new row: the host caps elements per page precisely because
            // "add a line every frame" is the mistake this makes obvious.
            if (this.liveLine != null && (this.ticks % 30) == 0)
            {
                this.liveLine.SetText("ticks " + this.ticks + " · beats " + this.coroutineBeats);
            }
        }

        public void Unload()
        {
            this.host?.Log("unloading after " + this.ticks + " ticks, " + this.coroutineBeats + " beats");
            this.liveLine = null;
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
