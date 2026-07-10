#if LOADER_MELON
using MelonLoader;

[assembly: MelonInfo(typeof(HeartopiaMod.HeartopiaMelonPlugin), "Bugtopia", HeartopiaMod.ModBuildVersion.Numeric, "HeartopiaMod")]
[assembly: MelonGame(null, null)]
// The single unified DLL references BOTH loaders' assemblies; under MelonLoader the BepInEx ones
// are absent. Mark them optional so ML doesn't warn now — and won't hard-fail to load us once a
// future ML version turns the missing-dependency warning into an error.
[assembly: MelonOptionalDependencies("BepInEx.Core", "BepInEx.Unity.IL2CPP")]

namespace HeartopiaMod
{
    public class HeartopiaMelonPlugin : MelonMod
    {
        private HeartopiaComplete _mod;

        public override void OnInitializeMelon()
        {
            ModLoaderInfo.IsMelonLoader = true;
            MelonLogAdapter.Install();
            ModCoroutines.InitMelonLoader();
            _mod = new HeartopiaComplete();
            _mod.OnInitializeMelon();
        }

        public override void OnLateUpdate()
        {
            try { _mod?.OnLateUpdate(); }
            catch (System.Exception ex) { ModEntryGuard.Report("OnLateUpdate", ex); }
        }

        public override void OnUpdate()
        {
            try { _mod?.OnUpdate(); }
            catch (System.Exception ex) { ModEntryGuard.Report("OnUpdate", ex); }
        }

        public override void OnGUI()
        {
            try { _mod?.OnGUI(); }
            catch (System.Exception ex) { ModEntryGuard.Report("OnGUI", ex); }
        }

        public override void OnDeinitializeMelon() => _mod?.OnDeinitializeMelon();
    }
}
#endif
