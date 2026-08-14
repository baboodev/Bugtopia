using UnityEngine;

namespace HeartopiaMod
{
    // Settings → Logging: runtime ON/OFF switches for every extended-logging master flag
    // (the MasterLog* static bools scattered across the HeartopiaComplete partials).
    // State IS PERSISTED (KeybindConfigData / PopulateKeybindConfig / ApplyKeybindConfig) and every
    // flag defaults to false. This was session-only until the whole set was made default-OFF; the
    // save is committed by the checkbox wrapper in BuildUguiShellSettingsLoggingContent, because the
    // bindings themselves are bare field setters with nowhere to hook a save.
    public partial class HeartopiaComplete
    {
        private const float LoggingTabRowHeight = 30f;
        // Guards against a binding being added without the row area growing to fit it — the builder
        // logs a mismatch. Keep in sync with BuildUguiLoggingToggleBindings.
        private const int LoggingTabRowCount = 47;


    }
}
