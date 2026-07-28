namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        // ========================================================================================
        // IMGUI IS FULLY RETIRED.
        //
        // Phase 5 moved the mod menu, Building Move Panel, Quest Assistant window, Status Overlay and
        // toast renderer to UGUI. This final step moved the last three surfaces — the resource ESP,
        // the debug ESP and the mouse-look crosshair — into the retained-mode overlay in
        // HeartopiaComplete.UguiOverlay.cs, which draws from OnLateUpdate off a pooled canvas.
        //
        // `OnGUI` is therefore gone. That matters beyond tidiness: OnGUI is a Unity message that only
        // reaches a real injected MonoBehaviour, and injecting that MonoBehaviour is the SOLE reason
        // the mod caused Il2CppInterop's ClassInjector to install 5 inline detours inside
        // GameAssembly.dll's `.text` (InjectorHelpers.Setup() is lazy, and BepInEx injects nothing on
        // its own). With the last OnGUI consumer gone, that surface can now be dropped — see
        // MonoTickFeature.cs for the per-frame tick that replaces the pump half of the same component.
        //
        // The entry points in HeartopiaComplete/BepInExPlugin/MelonLoaderPlugin still forward OnGUI so
        // the loader contract stays intact; they now hand it to a method that does nothing.
        // ========================================================================================
        public void OnGUI()
        {
        }
    }
}
