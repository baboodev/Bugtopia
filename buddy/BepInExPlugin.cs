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

        // Two pumps, in preference order. The mod needs a per-frame tick from the il2cpp/Unity side
        // (the embedded-Mono route is a proven dead end — re-entering il2cpp from a thread executing
        // Mono code faults on the first Il2CppInterop call, and this mod is Il2CppInterop end to end).
        //
        //   1. PLAYER-LOOP NODE (preferred) — a raw function pointer in a Unity player-loop slot.
        //      Injects no managed type, so it causes no ClassInjector detours at all.
        //   2. INJECTED MonoBehaviour (fallback) — AddComponent -> RegisterTypeInIl2Cpp ->
        //      InjectorHelpers.Setup() -> inline detours on IL2CPP VM internals. Once that happens the
        //      hooks cannot be taken back: they ARE the injection mechanism (injected classes get
        //      NEGATIVE type indices that only those hooks translate), and detaching them crashes
        //      within seconds. See the post-mortems in `mono-side-per-frame-tick` /
        //      `melonloader-vs-bepinex-detection-delta`.
        //
        // The component was only ever needed for the tick; everything else was rehomed already:
        //   * OnGUI      -> retired; ESP/crosshair are retained-mode UGUI (HeartopiaComplete.UguiOverlay.cs)
        //   * coroutines -> fully managed scheduler (ModCoroutines.cs), no il2cpp enumerator bridge
        public override void Load()
        {
            ModLoaderInfo.IsMelonLoader = false;
            BepInExLogAdapter.Install(Log);
            ModCoroutines.InitBepInEx();

            // Trim the ClassInjector hooks that injection would otherwise install but that provably
            // cannot fire for this mod (ClassInjectorHookTrimFeature.cs). MUST precede AddComponent:
            // InjectorHelpers.Setup() installs them lazily on the first RegisterTypeInIl2Cpp, and
            // once installed they cannot be removed (proven the hard way — see that file's header).
            // Preferred pump: two Unity player-loop nodes driven by raw function pointers
            // (PlayerLoopProbeFeature.cs). Needs no injected managed type, so it triggers no
            // ClassInjector detours. Both prerequisites are live-proven on this build — the reverse
            // P/Invoke ticks once per frame, and Il2CppInterop calls are legal from that site.
            //
            // NOTE: on its own this does NOT reduce the hook count, because BepInEx installs all 5
            // itself before plugins load (it converts a managed delegate to redirect Unity's log
            // output). The reduction only lands with `BepInEx.cfg [Logging] UnityLogListening = false`
            // — exposed as a toggle on Settings→Logging (UnityLogMirrorSetting.cs).
            //
            // The trim runs BEFORE the pump branch on purpose. With the listener disabled, nothing
            // has triggered `InjectorHelpers.Setup()` yet at this point, so this is the only moment
            // the suppression can land — and the PlayerLoop pump returns early, so anything after
            // that branch would never execute. With the listener enabled (the default) the hooks are
            // already applied and this is a logged no-op, exactly as before.
            ClassInjectorHookTrim.Apply();

            bool pumpDeadLastRun = PlayerLoopPumpMarker.Exists();
            if (!pumpDeadLastRun && PlayerLoopProbe.Install())
            {
                return;   // HeartopiaComplete is constructed on the first tick
            }

            ModLogger.Msg(pumpDeadLastRun
                ? "[PlayerLoopProbe] skipped — a previous run recorded that the node never ticked; "
                  + "using the injected MonoBehaviour pump."
                : "[PlayerLoopProbe] unavailable (" + PlayerLoopProbe.Status
                  + ") — using the injected MonoBehaviour pump.");

            // The trim already ran above; Setup() is lazy and one-shot, so this AddComponent is what
            // triggers it when the PlayerLoop pump was unavailable.
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
