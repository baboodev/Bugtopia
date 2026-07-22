using UnityEngine;

namespace HeartopiaMod
{
    // Settings → Logging: runtime ON/OFF switches for every extended-logging master flag
    // (the MasterLog* static bools scattered across the HeartopiaComplete partials).
    // State is SESSION-ONLY by design — nothing here is saved to or loaded from the config,
    // so a restart always returns to the compiled-in defaults.
    public partial class HeartopiaComplete
    {
        private const float LoggingTabRowHeight = 30f;
        private const int LoggingTabRowCount = 39; // keep in sync with DrawLoggingTab rows


    }
}
