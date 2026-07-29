#if LOADER_MELON
using MelonLoader;

[assembly: MelonInfo(typeof(HeartopiaMod.HeartopiaMelonPlugin), "Bugtopia", HeartopiaMod.ModBuildVersion.Numeric, "baboodev")]
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

        // Runs BEFORE MelonLoader loads its support module — and the support module is what first
        // triggers Il2CppInterop's ClassInjector `Setup()` (`Main.Initialize:78` →
        // `MonoEnumeratorWrapper.Register()` → `RegisterTypeInIl2Cpp`, inside `SupportModule.Setup()`
        // at `Core.cs:110`, whereas mods load one line earlier at `Core.cs:108`). That ordering is
        // the entire point: hooks can only be suppressed before `Setup()` installs them, and it runs
        // once, lazily. Measured margin on this build is ~2.2 s.
        //
        // ⚠️ NOTHING here may touch Unity, il2cpp or any game type — the il2cpp runtime is not up.
        // `ClassInjectorHookTrim` is CoreCLR reflection only, and forcing `InjectorHelpers`' static
        // init this early is safe (module enumeration, dictionaries, `Hook<T>` default ctors — no VM
        // call). `MelonLogAdapter.Install` is just a delegate assignment, so calling it here as well
        // as in OnInitializeMelon is harmless and gets the trim's own lines into the log.
        public override void OnEarlyInitializeMelon()
        {
            ModLoaderInfo.IsMelonLoader = true;
            MelonLogAdapter.Install();
            ClassInjectorHookTrim.Apply();
        }

        public override void OnInitializeMelon()
        {
            // Repeated from OnEarlyInitializeMelon on purpose, as insurance: both are idempotent
            // (a bool store and a delegate assignment), and if a MelonLoader build ever stops
            // calling the early hook, losing the log sink here would be a SILENT failure.
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
