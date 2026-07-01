#if MELONLOADER
using MelonLoader;

[assembly: MelonInfo(typeof(HeartopiaMod.HeartopiaMelonPlugin), "Bugtopia", HeartopiaMod.ModBuildVersion.Numeric, "HeartopiaMod")]
[assembly: MelonGame(null, null)]

namespace HeartopiaMod
{
    public class HeartopiaMelonPlugin : MelonMod
    {
        private HeartopiaComplete _mod;

        public override void OnInitializeMelon()
        {
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
