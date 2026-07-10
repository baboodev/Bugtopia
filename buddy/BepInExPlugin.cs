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
                ModLogger.Msg("HeartopiaBehaviour Awake — Update/OnGUI active on BepInEx manager.");
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
