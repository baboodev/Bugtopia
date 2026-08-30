using UnityEngine;

namespace HeartopiaMod
{
    // Settings → Logging: runtime ON/OFF switches for every extended-logging master flag
    // (the MasterLog* static bools scattered across the HeartopiaComplete partials).
    // State IS PERSISTED (KeybindConfigData / PopulateKeybindConfig / ApplyKeybindConfig). All but
    // one default to false — MasterLogGatherScan is the exception and defaults TRUE, on both the
    // flag and its config field. This was session-only until the set was made (almost entirely)
    // default-OFF; the save is committed by the checkbox wrapper in
    // BuildUguiShellSettingsLoggingContent, because the bindings themselves are bare field setters
    // with nowhere to hook a save.
    public partial class HeartopiaComplete
    {
        private const float LoggingTabRowHeight = 30f;
        // Guards against a binding being added without the row area growing to fit it — the builder
        // logs a mismatch. Keep in sync with BuildUguiLoggingToggleBindings.
        private const int LoggingTabRowCount = 60;


    }
}
