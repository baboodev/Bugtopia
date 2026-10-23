using System;
using HeartopiaMod;
using HeartopiaMod.Plugins;

namespace BugtopiaMcp
{
    public sealed class Plugin : IBugtopiaPlugin
    {
        private McpRuntime runtime;
        private IPluginPage page;
        private IPluginLabel status;

        public void Load(IHostApi host)
        {
            this.runtime = new McpRuntime(host.Mod);
            HeartopiaComplete.GameEventObserved = McpRuntime.McpNoteGameEvent;
            HeartopiaComplete.LateTick = McpScreenshot.OnLateUpdateFallback;
            PluginHost.CleanQuit += McpForensics.ClearResident;
            this.runtime.Start();

            if (host.Ui != null && host.Ui.IsAvailable)
            {
                this.page = host.Ui.AddPage("MCP Bridge");
                if (this.page != null)
                {
                    this.status = this.page.AddLabel("bridge starting");
                    this.page.AddNote("Writes let the agent change game state. Unsafe additionally allows raw invokes and hot-loaded plugins, which can crash the game — and it implies writes, so the two move together. Session only: never saved, always off again after a restart.");
                    this.page.AddToggle("Allow write ops", McpBridge.AllowWrites, this.OnWrites);
                    this.page.AddToggle("Allow unsafe ops (plugins, raw invokes)", McpBridge.AllowUnsafe, this.OnUnsafe);
                }
            }
        }

        public void Tick()
        {
            if (this.runtime != null)
            {
                this.runtime.Tick();
            }

            if (this.status != null)
            {
                this.status.SetText(McpBridge.Status
                    + "  port " + McpBridge.Port
                    + "  ops " + McpBridge.OpsServed
                    + "  writes " + (McpBridge.AllowWrites ? "on" : "off")
                    + "  unsafe " + (McpBridge.AllowUnsafe ? "on" : "off"));
            }
        }

        public void Unload()
        {
            HeartopiaComplete.GameEventObserved = null;
            HeartopiaComplete.LateTick = null;
            PluginHost.CleanQuit -= McpForensics.ClearResident;
            if (this.page != null)
            {
                this.page.Remove();
                this.page = null;
            }

            if (this.runtime != null)
            {
                this.runtime.Stop();
                this.runtime = null;
            }
        }

        public string Call(string method, string argsJson)
        {
            return null;
        }

        private void OnWrites(bool value)
        {
            if (!value)
            {
                McpBridge.SetPrivileges(false, false);
            }
            else
            {
                McpBridge.SetPrivileges(true, McpBridge.AllowUnsafe);
            }
        }

        private void OnUnsafe(bool value)
        {
            McpBridge.SetPrivileges(value || McpBridge.AllowWrites, value);
        }
    }
}
