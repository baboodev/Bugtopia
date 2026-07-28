#if LOADER_BEPINEX
using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace HeartopiaMod
{
    [BepInPlugin(HeartopiaBepInPlugin.PluginGuid, HeartopiaBepInPlugin.PluginName, HeartopiaBepInPlugin.PluginVersion)]
    public class HeartopiaBepInPlugin : BasePlugin
    {
        public const string PluginGuid = "com.bugtopia.mod";
        public const string PluginName = "Bugtopia";
        public const string PluginVersion = ModBuildVersion.Numeric;

        // Injecting HeartopiaBehaviour is what makes this mod cause IL2CPP `.text` modification:
        // AddComponent -> ClassInjector.RegisterTypeInIl2Cpp -> InjectorHelpers.Setup() -> five inline
        // detours on IL2CPP VM internals. That cost is UNAVOIDABLE and the five are UNREMOVABLE — see
        // the two post-mortems in `mono-side-per-frame-tick` / `melonloader-vs-bepinex-detection-delta`:
        //   * the hooks are the injection mechanism itself (injected classes get NEGATIVE type indices
        //     that only those hooks translate), so detaching them crashes within seconds; and
        //   * the pump cannot come from the embedded-Mono side either — re-entering il2cpp from a
        //     thread executing Mono code faults on the first Il2CppInterop call, and this mod is
        //     Il2CppInterop end to end.
        // An Il2CppInterop mod must be pumped from the il2cpp/Unity side. This is that pump.
        //
        // What the component is still needed for is ONLY the per-frame tick; the rest was rehomed:
        //   * OnGUI      -> retired; ESP/crosshair are retained-mode UGUI (HeartopiaComplete.UguiOverlay.cs)
        //   * coroutines -> fully managed scheduler (ModCoroutines.cs), no il2cpp enumerator bridge
        public override void Load()
        {
            ModLoaderInfo.IsMelonLoader = false;
            BepInExLogAdapter.Install(Log);
            ModCoroutines.InitBepInEx();
            AddComponent<HeartopiaBehaviour>();
        }
    }

    public class HeartopiaBehaviour : MonoBehaviour
    {
        private HeartopiaComplete _mod;

        public HeartopiaBehaviour(IntPtr ptr)
            : base(ptr)
        {
        }

        private void Awake()
        {
            try
            {
                ModCoroutines.SetHost(this);
                _mod = new HeartopiaComplete();
                _mod.OnInitializeMelon();
                ModLogger.Msg("HeartopiaBehaviour Awake — Update/LateUpdate active on the BepInEx manager.");
            }
            catch (Exception ex)
            {
                ModEntryGuard.Report("Awake", ex);
            }
        }

        private void Update()
        {
            try { _mod?.OnUpdate(); }
            catch (Exception ex) { ModEntryGuard.Report("Update", ex); }
        }

        private void LateUpdate()
        {
            try { _mod?.OnLateUpdate(); }
            catch (Exception ex) { ModEntryGuard.Report("LateUpdate", ex); }
        }

        private void OnGUI()
        {
            try { _mod?.OnGUI(); }
            catch (Exception ex) { ModEntryGuard.Report("OnGUI", ex); }
        }

        private void OnDestroy()
        {
            ModLogger.Msg("HeartopiaBehaviour OnDestroy — shutting down mod.");
            _mod?.OnDeinitializeMelon();
        }
    }
}
#endif
