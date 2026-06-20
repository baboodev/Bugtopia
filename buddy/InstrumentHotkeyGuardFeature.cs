using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const int InstrumentTypeBaYinTong = 4;
        private const int InstrumentTypePiano = 1;
        private const int MusicKeyOptionMode8 = 0;
        private const int MusicKeyOptionMode15a = 1;
        private const int MusicKeyOptionMode15b = 2;
        private const int MusicKeyOptionMode22 = 3;

        private static readonly string[] InstrumentInputKeys8Default =
        {
            "KeyY", "KeyU", "KeyI", "KeyO", "KeyH", "KeyJ", "KeyK", "KeyL"
        };

        private static readonly string[] InstrumentInputKeys8BaYinTong =
        {
            "KeyA", "KeyS", "KeyD", "KeyF", "KeyG", "KeyH", "KeyJ", "KeyK"
        };

        private static readonly string[] InstrumentInputKeys15a =
        {
            "KeyQ", "KeyW", "KeyE", "KeyR", "KeyT", "KeyY", "KeyU", "KeyI",
            "KeyA", "KeyS", "KeyD", "KeyF", "KeyG", "KeyH", "KeyJ"
        };

        private static readonly string[] InstrumentInputKeys15b =
        {
            "KeyY", "KeyU", "KeyI", "KeyO", "KeyP", "KeyH", "KeyJ", "KeyK", "KeyL",
            "KeySemicolon", "KeyN", "KeyM", "KeyComma", "KeyPeriod", "KeySlash"
        };

        private static readonly string[] InstrumentInputKeys22 =
        {
            "KeyQ", "KeyW", "KeyE", "KeyR", "KeyT", "KeyY", "KeyU", "KeyI",
            "KeyA", "KeyS", "KeyD", "KeyF", "KeyG", "KeyH", "KeyJ",
            "KeyZ", "KeyX", "KeyC", "KeyV", "KeyB", "KeyN", "KeyM"
        };

        private static readonly string[] InstrumentInputKeys22PianoSemitone =
        {
            "KeyQ", "KeyW", "KeyE", "KeyR", "KeyT", "KeyY", "KeyU", "KeyI",
            "KeyZ", "KeyX", "KeyC", "KeyV", "KeyB", "KeyN", "KeyM",
            "KeyComma", "KeyPeriod", "KeySlash", "KeyO", "KeyP", "KeyLeftBracket", "KeyRightBracket"
        };

        private static readonly string[] InstrumentInputKeysPianoBlack =
        {
            "Key2", "Key3", "Key5", "Key6", "Key7", "KeyS", "KeyD", "KeyG", "KeyH", "KeyJ",
            "KeyL", "KeySemicolon", "Key0", "KeyMinus", "KeyEqual"
        };

        // How often the (potentially expensive) instrument-panel resolve is allowed to run.
        // Between refreshes the last computed blocked-key set is reused, so the ~50 keybind
        // checks per frame only ever dedupe to one cheap lookup.
        private const float InstrumentHotkeyGuardRefreshInterval = 0.2f;

        // When the AuraMono native lookup finds no open panel (the common case on this build,
        // where the managed UI types are absent), back off before hammering the native
        // GetView/field-read path again. Running it every frame is both a per-frame full
        // AuraMono scan AND a per-frame native-AV exposure with no managed log if it faults.
        private const float InstrumentHotkeyGuardAuraMissCooldown = 1f;

        private readonly HashSet<KeyCode> instrumentHotkeyBlockedKeys = new HashSet<KeyCode>();
        private int instrumentHotkeyGuardFrame = -1;
        private float instrumentHotkeyGuardNextResolveAt;
        private float instrumentHotkeyGuardAuraRetryAt;
        private Type instrumentPanelTypeCache;
        private Type uiManagerTypeCache;
        private FieldInfo instrumentPanelInstrumentTypeField;
        private FieldInfo instrumentPanelKeyOptionField;
        private MethodInfo uiManagerGetViewMethod;
        private PropertyInfo uiManagerInstanceProperty;

        public static bool IsModHotkeyBlockedByInstrument(KeyCode key)
        {
            HeartopiaComplete instance = HeartopiaComplete.Instance;
            return instance != null && instance.IsInstrumentHotkeyConflict(key);
        }

        private static bool IsMouseKeyCode(KeyCode key)
        {
            return key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6;
        }

        private static int MouseKeyCodeToButtonIndex(KeyCode key)
        {
            return (int)key - (int)KeyCode.Mouse0;
        }

        private bool TryGetModHotkeyDown(KeyCode key)
        {
            if (key == KeyCode.None)
            {
                return false;
            }

            this.EnsureInstrumentHotkeyGuardUpdated();
            if (this.instrumentHotkeyBlockedKeys.Contains(key))
            {
                return false;
            }

            if (IsMouseKeyCode(key))
            {
                int button = MouseKeyCodeToButtonIndex(key);
                return Input.GetMouseButtonDown(button) || Input.GetKeyDown(key);
            }

            return Input.GetKeyDown(key);
        }

        // Held (not just the down edge) — for key-repeat behaviour. Same guard/mouse handling.
        private bool TryGetModHotkeyHeld(KeyCode key)
        {
            if (key == KeyCode.None)
            {
                return false;
            }

            this.EnsureInstrumentHotkeyGuardUpdated();
            if (this.instrumentHotkeyBlockedKeys.Contains(key))
            {
                return false;
            }

            if (IsMouseKeyCode(key))
            {
                int button = MouseKeyCodeToButtonIndex(key);
                return Input.GetMouseButton(button) || Input.GetKey(key);
            }

            return Input.GetKey(key);
        }

        private bool IsInstrumentHotkeyConflict(KeyCode key)
        {
            if (key == KeyCode.None)
            {
                return false;
            }

            this.EnsureInstrumentHotkeyGuardUpdated();
            return this.instrumentHotkeyBlockedKeys.Contains(key);
        }

        private void EnsureInstrumentHotkeyGuardUpdated()
        {
            // Dedupe the ~50 per-frame keybind checks down to one lookup per frame.
            if (this.instrumentHotkeyGuardFrame == Time.frameCount)
            {
                return;
            }

            this.instrumentHotkeyGuardFrame = Time.frameCount;

            // Only re-resolve the open instrument panel on a TTL, not every frame: the resolve
            // can fall into the heavy AuraMono native path on this build. Between refreshes the
            // previously computed blocked-key set is kept as-is (max ~0.2s lag when a panel
            // opens/closes, which is imperceptible for hotkey blocking).
            float now = Time.unscaledTime;
            if (now < this.instrumentHotkeyGuardNextResolveAt)
            {
                return;
            }

            this.instrumentHotkeyGuardNextResolveAt = now + InstrumentHotkeyGuardRefreshInterval;
            this.instrumentHotkeyBlockedKeys.Clear();

            if (!this.TryResolveInstrumentPanelKeyBindings(out HashSet<KeyCode> keys) || keys == null || keys.Count == 0)
            {
                return;
            }

            foreach (KeyCode key in keys)
            {
                if (key != KeyCode.None)
                {
                    this.instrumentHotkeyBlockedKeys.Add(key);
                }
            }
        }

        private bool TryResolveInstrumentPanelKeyBindings(out HashSet<KeyCode> keys)
        {
            keys = null;
            if (!this.TryGetOpenInstrumentPanel(out object panel, out int instrumentType, out int keyOption))
            {
                return false;
            }

            bool pianoSemitone = instrumentType == InstrumentTypePiano
                && keyOption == MusicKeyOptionMode22
                && this.TryGetGameSettingPianoSemitone(out bool semitone)
                && semitone;

            keys = new HashSet<KeyCode>();
            this.AddInstrumentLayoutKeys(keys, instrumentType, keyOption, pianoSemitone);
            return keys.Count > 0;
        }

        private void AddInstrumentLayoutKeys(HashSet<KeyCode> keys, int instrumentType, int keyOption, bool pianoSemitone)
        {
            switch (keyOption)
            {
                case MusicKeyOptionMode8:
                    this.AddInputEventKeys(
                        keys,
                        instrumentType == InstrumentTypeBaYinTong ? InstrumentInputKeys8BaYinTong : InstrumentInputKeys8Default);
                    break;
                case MusicKeyOptionMode15a:
                    this.AddInputEventKeys(keys, InstrumentInputKeys15a);
                    break;
                case MusicKeyOptionMode15b:
                    this.AddInputEventKeys(keys, InstrumentInputKeys15b);
                    break;
                case MusicKeyOptionMode22:
                    if (instrumentType == InstrumentTypePiano && pianoSemitone)
                    {
                        this.AddInputEventKeys(keys, InstrumentInputKeys22PianoSemitone);
                        this.AddInputEventKeys(keys, InstrumentInputKeysPianoBlack);
                    }
                    else
                    {
                        this.AddInputEventKeys(keys, InstrumentInputKeys22);
                    }
                    break;
            }
        }

        private void AddInputEventKeys(HashSet<KeyCode> keys, string[] inputEventNames)
        {
            if (inputEventNames == null)
            {
                return;
            }

            for (int i = 0; i < inputEventNames.Length; i++)
            {
                KeyCode keyCode = this.InputEventNameToKeyCode(inputEventNames[i]);
                if (keyCode != KeyCode.None)
                {
                    keys.Add(keyCode);
                }
            }
        }

        private KeyCode InputEventNameToKeyCode(string inputEventName)
        {
            if (string.IsNullOrEmpty(inputEventName) || !inputEventName.StartsWith("Key", StringComparison.Ordinal))
            {
                return KeyCode.None;
            }

            string suffix = inputEventName.Substring(3);
            if (suffix.Length == 1 && suffix[0] >= '0' && suffix[0] <= '9')
            {
                return KeyCode.Alpha0 + (suffix[0] - '0');
            }

            if (Enum.TryParse(suffix, out KeyCode keyCode))
            {
                return keyCode;
            }

            return KeyCode.None;
        }

        private bool TryGetOpenInstrumentPanel(out object panel, out int instrumentType, out int keyOption)
        {
            panel = null;
            instrumentType = 0;
            keyOption = MusicKeyOptionMode15a;

            try
            {
                panel = this.TryGetOpenInstrumentPanelManaged();
                if (panel != null)
                {
                    this.EnsureInstrumentPanelReflection(panel.GetType());
                    if (this.instrumentPanelInstrumentTypeField != null)
                    {
                        object value = this.instrumentPanelInstrumentTypeField.GetValue(panel);
                        if (value != null)
                        {
                            instrumentType = Convert.ToInt32(value);
                        }
                    }

                    if (this.instrumentPanelKeyOptionField != null)
                    {
                        object value = this.instrumentPanelKeyOptionField.GetValue(panel);
                        if (value != null)
                        {
                            keyOption = Convert.ToInt32(value);
                        }
                    }

                    return true;
                }

                // Managed path failed (UI types are absent on this build). Fall back to the
                // native AuraMono lookup, but only after the miss cooldown so a closed instrument
                // doesn't trigger a full native scan / native-AV exposure on every resolve tick.
                if (Time.unscaledTime < this.instrumentHotkeyGuardAuraRetryAt)
                {
                    return false;
                }

                if (!this.TryGetOpenInstrumentPanelAuraMono(out instrumentType, out keyOption))
                {
                    this.instrumentHotkeyGuardAuraRetryAt = Time.unscaledTime + InstrumentHotkeyGuardAuraMissCooldown;
                    return false;
                }

                // Panel found: allow the next resolve to read it again immediately so blocking
                // stays responsive while the instrument is open.
                this.instrumentHotkeyGuardAuraRetryAt = 0f;
                return true;
            }
            catch
            {
                // A native fault here is uncatchable, but a managed exception still means the
                // native path is unstable right now — back off before retrying it.
                this.instrumentHotkeyGuardAuraRetryAt = Time.unscaledTime + InstrumentHotkeyGuardAuraMissCooldown;
                panel = null;
                return false;
            }
        }

        private bool TryGetOpenInstrumentPanelAuraMono(out int instrumentType, out int keyOption)
        {
            instrumentType = 0;
            keyOption = MusicKeyOptionMode15a;

            // Must allow the Managers._serviceDic fallback: on this build UIManager.get_Instance
            // returns null via AuraMono, so the serviceDic enumeration is the ONLY path that
            // actually resolves the UI manager (it's what made detection "confirmed working"
            // originally). Disabling it here — as the "Crash fix" commit did — silently broke the
            // guard whenever no other feature had already warmed cachedAuraMonoUiManagerObj. The
            // heavy enumeration that the flag was meant to avoid now runs at most once per world
            // (then the pinned cache serves every later resolve), and the guard's own TTL +
            // miss-cooldown already bound how often a cold lookup can fire.
            if (!this.TryGetAuraMonoUiView(
                    "XDTGame.UI.Panel.InstrumentPanel",
                    "InstrumentPanel",
                    out IntPtr panelObj,
                    out _)
                || panelObj == IntPtr.Zero)
            {
                return false;
            }

            // mono_gc_disable does not exist on this sgen (moving) build — a GC-suspend guard is
            // impossible — so pin panelObj across the field reads. The moving GC can otherwise
            // relocate/collect it between resolve and read -> native AV (the no-crashlog crash this
            // guard was meant to prevent; surfaced reliably under a debugger).
            uint panelPin = AuraMonoPinNew(panelObj);
            try
            {
                instrumentType = (int)this.TryReadAuraMonoUIntField(panelObj, "_instrumentType", "instrumentType");
                keyOption = (int)this.TryReadAuraMonoUIntField(panelObj, "_nowKeyOption", "nowKeyOption");
                return true;
            }
            finally
            {
                AuraMonoPinFree(panelPin);
            }
        }

        private object TryGetOpenInstrumentPanelManaged()
        {
            this.EnsureInstrumentUiReflection();
            if (this.uiManagerTypeCache == null
                || this.instrumentPanelTypeCache == null
                || this.uiManagerInstanceProperty == null
                || this.uiManagerGetViewMethod == null)
            {
                return null;
            }

            object uiManager = this.uiManagerInstanceProperty.GetValue(null);
            if (uiManager == null)
            {
                return null;
            }

            return this.uiManagerGetViewMethod.Invoke(uiManager, new object[] { this.instrumentPanelTypeCache });
        }

        private void EnsureInstrumentUiReflection()
        {
            if (this.uiManagerTypeCache != null && this.instrumentPanelTypeCache != null)
            {
                return;
            }

            this.uiManagerTypeCache = this.FindLoadedType("XDTGame.Core.UIManager", "UIManager");
            this.instrumentPanelTypeCache = this.FindLoadedType("XDTGame.UI.Panel.InstrumentPanel", "InstrumentPanel");
            if (this.uiManagerTypeCache == null || this.instrumentPanelTypeCache == null)
            {
                return;
            }

            this.uiManagerInstanceProperty = this.uiManagerTypeCache.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static);
            this.uiManagerGetViewMethod = this.uiManagerTypeCache.GetMethod(
                "GetView",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Type) },
                null);
        }

        private void EnsureInstrumentPanelReflection(Type panelType)
        {
            if (panelType == null)
            {
                return;
            }

            if (this.instrumentPanelInstrumentTypeField != null
                && this.instrumentPanelInstrumentTypeField.DeclaringType == panelType)
            {
                return;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            this.instrumentPanelInstrumentTypeField = panelType.GetField("_instrumentType", flags)
                ?? panelType.GetField("instrumentType", flags);
            this.instrumentPanelKeyOptionField = panelType.GetField("_nowKeyOption", flags)
                ?? panelType.GetField("nowKeyOption", flags);
        }

        private bool TryGetGameSettingPianoSemitone(out bool pianoSemitone)
        {
            // GameSettingSystem.pianoSemitone is NOT a field — it's a computed property that
            // reads PlayerPrefs directly: `PlayerPrefs.GetInt("PianoSemitone", 0) >= 1`
            // (see ilspy-dumps/.../GameSettingSystem.cs). So neither managed-field reflection nor
            // an AuraMono field read can ever see it. UnityEngine.PlayerPrefs is shared with the
            // mod's runtime, so we read the same pref key directly — reliable and cheap.
            pianoSemitone = false;
            try
            {
                pianoSemitone = PlayerPrefs.GetInt("PianoSemitone", 0) >= 1;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
