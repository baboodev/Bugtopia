using System.Globalization;
using HeartopiaMod.Plugins;

namespace HelloBugtopia
{
    // Smallest plugin that exercises the public surface: a log line, a persisted counter, a page,
    // and one Call method. Copy the project and replace this type.
    public sealed class HelloPlugin : IBugtopiaPlugin
    {
        private IHostApi host;
        private IPluginLabel line;
        private long ticks;
        private int runs;

        public void Load(IHostApi api)
        {
            this.host = api;
            int parsed;
            this.runs = this.host.State.TryGetValue("runs", out string raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0;
            this.host.Log("loaded — sdk v" + PluginSdk.Version + ", run " + (this.runs + 1));
            this.host.Toast("HelloBugtopia loaded");

            IPluginPage page = this.host.Ui.AddPage("Hello");
            if (page == null)
            {
                return;
            }

            page.AddNote("This page belongs to HelloBugtopia. Unload the plugin and the page goes with it.");
            this.line = page.AddLabel("ticks 0 · run " + (this.runs + 1));
            page.AddButton("Log folder", () => this.host.Log(this.host.PluginDirectory));
        }

        public void Tick()
        {
            this.ticks++;
            if (this.line != null && (this.ticks % 30) == 0)
            {
                this.line.SetText("ticks " + this.ticks + " · run " + (this.runs + 1));
            }
        }

        public void Unload()
        {
            this.runs++;
            if (this.host != null)
            {
                this.host.State["runs"] = this.runs.ToString(CultureInfo.InvariantCulture);
                this.host.Log("unloading after " + this.ticks + " ticks, run " + this.runs);
            }

            this.line = null;
            this.host = null;
        }

        public string Call(string method, string argsJson)
        {
            if (method == "ping")
            {
                string id = this.host == null ? string.Empty : this.host.Id;
                return "{\"ok\":true,\"id\":\"" + id + "\"}";
            }

            return null;
        }
    }
}
