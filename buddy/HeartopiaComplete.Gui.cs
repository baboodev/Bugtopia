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
        // `OnGUI` is therefore gone. Note what that did and did NOT buy: the injected MonoBehaviour is
        // still required, because it is the only source of the per-frame tick that Il2CppInterop calls
        // are legal from (a Mono-side pump was tried and faults on the first il2cpp call). So the 5
        // ClassInjector detours its injection costs remain — they are the floor under BepInEx, not
        // leftovers. Retiring IMGUI still stands on its own: one less Unity message to serve, and the
        // ESP/crosshair now draw from a pooled retained-mode canvas instead of re-emitting every quad
        // each frame.
        //
        // The entry points in HeartopiaComplete/BepInExPlugin/MelonLoaderPlugin still forward OnGUI so
        // the loader contract stays intact; they now hand it to a method that does nothing.
        // ========================================================================================
        public void OnGUI()
        {
        }
    }
}
