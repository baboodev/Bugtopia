using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HeartopiaMod
{
    // ============================================================================================
    // GAME KEY BINDINGS — the model behind Settings→Game Keys.
    //
    // Reads the live `InputActionAsset` (InputRebindFeature.cs owns finding it) and turns it into a
    // flat list of rebindable rows, then writes user choices back through
    // `InputActionMap.ApplyBindingOverride`. No hook, no patch — see [[game-key-rebinding-and-hints]].
    //
    // Everything here is derived from `InputActionAsset.ToJson()`, NOT from enumerating
    // `map.bindings`: `ReadOnlyArray<InputBinding>` is a value-type generic instantiation that may
    // have no AOT instance in this IL2CPP build, so touching it is a crash risk. The JSON is
    // Unity's own fixed serializer shape, which makes a line scanner sufficient and safe.
    //
    // The binding INDEX is what ApplyBindingOverride addresses, and it counts every entry in a
    // map's "bindings" array — including composite HEADS, which carry a pseudo-path ("2DVector")
    // and no physical control. So heads are counted but never shown; a row is only offered when its
    // path names a real device ("<Keyboard>/...", "<Mouse>/..."). Index math verified against the
    // live asset: AllKeyboardKeysMap #40 == <Keyboard>/1, matching what the interact remap logged.
    //
    // The asset on this build has four maps, and they are deliberately shown in this order:
    //   GameActionMap    (40 kb/mouse) — the real gameplay verbs: jump, sprint, mainInteraction...
    //   VehicleActionMap  (5)          — Move composite + TakeVehicle
    //   UIActionMap       (5)          — mostly VirtualMouse; rebinding these is a footgun
    //   AllKeyboardKeysMap (110)       — one self-named action per physical key ("1" -> "1"),
    //                                    which is where InputEvent.Key1 (the interact hint) lives.
    // AllKeyboardKeysMap goes LAST because it is 110 rows of noise for everyone who is not hunting
    // that one entry.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        internal sealed class GameKeyRow
        {
            public string Map;          // owning action map name
            public string Action;       // action name ("move", "1")
            public string Part;         // composite part name ("up"), "" for plain bindings
            public int Index;           // binding index WITHIN the map — ApplyBindingOverride's arg
            public string DefaultPath;  // the asset's own path, e.g. "<Keyboard>/w"
            public string Caption;      // friendly row label, "" to fall back to Action
            public string Shares;       // gameplay actions on the same physical key, "" if none
        }

        // Direct keys with a PROVEN gameplay meaning, hoisted out of the 110-row pile so they are
        // findable. Sources, all read from the decompiled game rather than guessed:
        //   TrackingPanel.RegisterPcControl  — Key1..Key4 -> TrackingBar_ClickInteractChild(0..3)
        //   TrackingPanel.OnInputTypeChange  — KeyQ       -> TrackingBar_SwitchInteractBar
        //   IconsBarWidget.RefreshShortcut   — the Q chip only appears in MouseControlMode.MoveRotate
        //                                      (and, since 2026-08-06, only on the bar that is the
        //                                      current interact target — see CameraModeHotkeyGuard)
        // These are exactly the keys the camera-mode interaction bar uses, and the ones whose hint
        // sprites sit above the tracked object.
        private static readonly string[][] GameKeyFeaturedDirectActions =
        {
            new[] { "1", "Interact slot 1" },
            new[] { "2", "Interact slot 2" },
            new[] { "3", "Interact slot 3" },
            new[] { "4", "Interact slot 4" },
            new[] { "q", "Switch interact bar" },
        };

        // Display order + section titles. A map missing from the asset is simply skipped.
        private static readonly string[] GameKeyMapOrder =
        {
            "GameActionMap", "VehicleActionMap", "UIActionMap", "AllKeyboardKeysMap"
        };

        private List<GameKeyRow> gameKeyRows;
        private bool gameKeyAssetMismatchLogged;

        // Key = "<map>/<index>", value = override path. This is both the live state and, later, the
        // persisted form — a binding is addressed by map+index, which survives across sessions
        // because the asset ships with the game and only changes when the game updates.
        private readonly Dictionary<string, string> gameKeyOverrides = new Dictionary<string, string>();

        internal static string GameKeyRowKey(GameKeyRow row)
        {
            return row.Map + "/" + row.Index.ToString();
        }

        // Parses the asset once and caches. Returns null if the asset is not up yet — callers must
        // treat that as "try again later", not as "there are no keys".
        internal List<GameKeyRow> TryGetGameKeyRows()
        {
            if (this.gameKeyRows != null)
            {
                return this.gameKeyRows;
            }

            try
            {
                InputActionAsset asset = this.FindGameInputActionAsset();
                if (asset == null)
                {
                    return null;
                }

                string json = asset.ToJson();
                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }

                List<GameKeyRow> parsed = ParseGameKeyRows(json);
                if (parsed.Count == 0)
                {
                    return null;
                }

                // Second, independent identity check — the first one (FindAction("touchLook")) is a
                // single interop call, and caching rows from the wrong asset poisons the session.
                // The direct map is the game asset's fingerprint: 110 self-named actions, one per
                // physical key. Nothing else in this process looks like that.
                int directRows = 0;
                for (int i = 0; i < parsed.Count; i++)
                {
                    if (string.Equals(parsed[i].Map, GameKeyDirectMapName, StringComparison.Ordinal))
                    {
                        directRows++;
                    }
                }

                if (directRows < 50)
                {
                    if (!this.gameKeyAssetMismatchLogged)
                    {
                        this.gameKeyAssetMismatchLogged = true;
                        ModLogger.Msg("[InputMap] ignoring an InputActionAsset without " + GameKeyDirectMapName
                                      + " (" + parsed.Count + " bindings) — not the game's; will retry.");
                    }

                    return null;
                }

                AnnotateGameKeyRows(parsed);
                this.gameKeyRows = parsed;

                // Per-map counts, so a future regression is visible in one log line instead of
                // needing another round trip. Expected on this build: GameActionMap 40,
                // VehicleActionMap 5, UIActionMap 2, AllKeyboardKeysMap 110 = 157.
                Dictionary<string, int> perMap = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < parsed.Count; i++)
                {
                    int n;
                    perMap.TryGetValue(parsed[i].Map, out n);
                    perMap[parsed[i].Map] = n + 1;
                }

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (KeyValuePair<string, int> pair in perMap)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(pair.Key).Append('=').Append(pair.Value);
                }

                ModLogger.Msg("[InputMap] " + parsed.Count + " rebindable key binding(s): " + sb.ToString());

                this.MarkKeyIconSwapsDirty();
                return this.gameKeyRows;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[InputMap] row build failed: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        // Line scanner over Unity's pretty-printed asset JSON. Per map: name, id, actions,
        // bindings; per binding: name, id, path, interactions, processors, groups, action,
        // isComposite, isPartOfComposite.
        //
        // The MAP NAME is latched off that field order: when the `"actions": [` line arrives, the
        // last `"name"` seen is necessarily that map's, because a map's own name precedes its
        // actions and bindings arrays and nothing else can appear in between. It replaces resolving
        // the map through `asset.FindAction(name).actionMap` — not because that was broken (the
        // "22 bindings" that motivated the change turned out to be the WRONG ASSET, not a failed
        // lookup) but because 157 interop calls to learn something the JSON already states is waste.
        // Verified against a real JSON parse of the live asset: identical per-map counts, zero
        // index mismatches.
        private static List<GameKeyRow> ParseGameKeyRows(string json)
        {
            List<GameKeyRow> rows = new List<GameKeyRow>();
            int index = -1;
            string currentMap = null;
            string lastName = null;
            string pendingName = null;
            string pendingPath = null;

            foreach (string raw in json.Split('\n'))
            {
                string line = raw.Trim();

                if (line.StartsWith("\"actions\"", StringComparison.Ordinal))
                {
                    currentMap = lastName;
                    continue;
                }

                if (line.StartsWith("\"bindings\"", StringComparison.Ordinal))
                {
                    index = -1;
                    pendingName = null;
                    pendingPath = null;
                    continue;
                }

                string name = ReadJsonString(line, "\"name\": \"");
                if (name != null)
                {
                    lastName = name;
                    pendingName = name;
                    continue;
                }

                string path = ReadJsonString(line, "\"path\": \"");
                if (path != null)
                {
                    index++;
                    pendingPath = path;
                    continue;
                }

                string action = ReadJsonString(line, "\"action\": \"");
                if (action == null)
                {
                    continue;
                }

                // A composite HEAD's path is a layout name, not a control — it still occupies an
                // index (so the counter above must see it) but there is nothing to rebind.
                if (currentMap != null && pendingPath != null && action.Length > 0
                    && IsPhysicalControlPath(pendingPath))
                {
                    rows.Add(new GameKeyRow
                    {
                        Map = currentMap,
                        Action = action,
                        Part = pendingName ?? string.Empty,
                        Index = index,
                        DefaultPath = pendingPath
                    });
                }

                pendingName = null;
                pendingPath = null;
            }

            return rows;
        }

        // Fills in the two display-only fields. Both are DERIVED from the asset, never guessed: the
        // caption comes from the proven table above, and `Shares` lists the gameplay actions that
        // sit on the same physical key — which is what makes an anonymous direct row like "v"
        // findable as the camera button, since the chips' hints all resolve through
        // InputEvent.KeyV -> AllKeyboardKeysMap "v" and not through GameActionMap's openCamera.
        private static void AnnotateGameKeyRows(List<GameKeyRow> rows)
        {
            Dictionary<string, string> gameplayByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Count; i++)
            {
                GameKeyRow row = rows[i];
                if (string.Equals(row.Map, GameKeyDirectMapName, StringComparison.Ordinal))
                {
                    continue;
                }

                string existing;
                gameplayByPath.TryGetValue(row.DefaultPath, out existing);
                gameplayByPath[row.DefaultPath] = string.IsNullOrEmpty(existing)
                    ? row.Action
                    : (existing.IndexOf(row.Action, StringComparison.Ordinal) >= 0 ? existing : existing + ", " + row.Action);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                GameKeyRow row = rows[i];
                row.Caption = string.Empty;
                row.Shares = string.Empty;

                if (!string.Equals(row.Map, GameKeyDirectMapName, StringComparison.Ordinal))
                {
                    continue;
                }

                for (int f = 0; f < GameKeyFeaturedDirectActions.Length; f++)
                {
                    if (string.Equals(row.Action, GameKeyFeaturedDirectActions[f][0], StringComparison.Ordinal))
                    {
                        row.Caption = GameKeyFeaturedDirectActions[f][1];
                        break;
                    }
                }

                string shared;
                if (gameplayByPath.TryGetValue(row.DefaultPath, out shared))
                {
                    row.Shares = shared;
                }
            }
        }

        internal static bool IsFeaturedDirectAction(string action)
        {
            for (int f = 0; f < GameKeyFeaturedDirectActions.Length; f++)
            {
                if (string.Equals(action, GameKeyFeaturedDirectActions[f][0], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // Only keyboard and mouse are offered: gamepad/joystick/touch paths exist in the asset but
        // cannot be captured by the keyboard-and-mouse rebind flow, and showing rows that can never
        // be set is worse than not showing them.
        private static bool IsPhysicalControlPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '<')
            {
                return false;
            }

            return path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase);
        }

        // ---- writing --------------------------------------------------------------------------

        // path == null or empty CLEARS the override (that is the API's own convention).
        // PRIVATE on purpose: callers must go through TryMoveGameKey, which moves the whole physical
        // key. Writing a single binding is only correct for the internal group/restore loops below.
        private bool TrySetGameKeyOverride(GameKeyRow row, string path)
        {
            if (row == null)
            {
                return false;
            }

            try
            {
                InputActionAsset asset = this.FindGameInputActionAsset();
                if (asset == null)
                {
                    return false;
                }

                // By MAP name first: map names are unique and this is one lookup instead of one per
                // action. The action route stays as a fallback only because it is the path the
                // interact remap already proved works on this build — the read side no longer
                // depends on either (see ParseGameKeyRows).
                InputActionMap map = asset.FindActionMap(row.Map, false);
                if (map == null)
                {
                    InputAction action = asset.FindAction(row.Action, false);
                    map = action == null ? null : action.actionMap;
                }

                if (map == null)
                {
                    ModLogger.Msg("[InputMap] cannot reach map '" + row.Map + "' for " + row.Action
                                  + " #" + row.Index + " — override not applied.");
                    return false;
                }

                InputBinding over = new InputBinding();
                over.overridePath = string.IsNullOrEmpty(path) ? string.Empty : path;
                map.ApplyBindingOverride(row.Index, over);

                string key = GameKeyRowKey(row);
                if (string.IsNullOrEmpty(path))
                {
                    this.gameKeyOverrides.Remove(key);
                }
                else
                {
                    this.gameKeyOverrides[key] = path;
                }

                // The on-screen hint sprite does NOT follow a rebind on its own — the game builds it
                // from a hard-coded switch — so the icon layer has to be told (InputRebindFeature.cs).
                this.MarkKeyIconSwapsDirty();

                ModLogger.Msg("[InputMap] " + row.Map + "/" + row.Action + " #" + row.Index + " -> "
                              + (string.IsNullOrEmpty(path) ? "default (" + row.DefaultPath + ")" : path));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[InputMap] override failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        // Moves a whole PHYSICAL KEY, not a single binding — this is what the UI calls.
        //
        // The asset routinely puts several bindings on one key: <Keyboard>/v carries `openCamera`,
        // `recentre` AND the direct-map action "v", and <Keyboard>/t carries `pickup`, `openHandle`
        // and "t". The game draws exactly ONE hint per key, and a player thinks in keys, so moving
        // one binding and leaving its siblings behind produces a state nobody wants: the camera
        // opens on the new key AND the old one, while the chip still shows the old one (which is
        // precisely what a first attempt at this shipped, and what the log then showed).
        //
        // Moving the group also happens to be what makes the hint follow, since hints resolve
        // through InputEvent.KeyN -> the DIRECT map's action, which is now carried along.
        internal int TryMoveGameKey(GameKeyRow row, string path)
        {
            List<GameKeyRow> rows = this.TryGetGameKeyRows();
            if (row == null || rows == null)
            {
                return 0;
            }

            int moved = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].DefaultPath, row.DefaultPath, StringComparison.OrdinalIgnoreCase)
                    && this.TrySetGameKeyOverride(rows[i], path))
                {
                    moved++;
                }
            }

            if (moved > 1)
            {
                ModLogger.Msg("[InputMap] moved " + moved + " binding(s) sharing " + row.DefaultPath
                              + " — the game draws one hint per key, so they travel together.");
            }

            return moved;
        }

        // What the row currently resolves to — the override if one is set, else the asset default.
        internal string GetGameKeyEffectivePath(GameKeyRow row)
        {
            string over;
            if (row != null && this.gameKeyOverrides.TryGetValue(GameKeyRowKey(row), out over))
            {
                return over;
            }

            return row == null ? string.Empty : row.DefaultPath;
        }

        internal bool IsGameKeyOverridden(GameKeyRow row)
        {
            return row != null && this.gameKeyOverrides.ContainsKey(GameKeyRowKey(row));
        }

        internal int ClearAllGameKeyOverrides()
        {
            List<GameKeyRow> rows = this.TryGetGameKeyRows();
            if (rows == null || this.gameKeyOverrides.Count == 0)
            {
                return 0;
            }

            int cleared = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (this.IsGameKeyOverridden(rows[i]) && this.TrySetGameKeyOverride(rows[i], null))
                {
                    cleared++;
                }
            }

            return cleared;
        }

        // ---- persistence ------------------------------------------------------------------------
        //
        // Binding overrides live ONLY in the loaded asset — Unity does not persist them and the
        // game has no settings slot for them — so they must be re-applied every launch. That has to
        // happen on the world-ready gate, not from a timer in OnUpdate ([[world-ready-gate]]): the
        // asset does not exist before a world does, and polling for it would be exactly the pattern
        // that rule exists to forbid.

        private List<GameKeyOverrideEntry> pendingGameKeyOverrides;
        private const string GameKeysWorldReadyCallbackName = "GameKeyOverrides";

        internal List<GameKeyOverrideEntry> ExportGameKeyOverrides()
        {
            List<GameKeyOverrideEntry> list = new List<GameKeyOverrideEntry>();

            // Anything loaded but not yet applied must survive a save, or opening the config before
            // entering a world would silently wipe the user's layout.
            if (this.gameKeyOverrides.Count == 0 && this.pendingGameKeyOverrides != null)
            {
                return this.pendingGameKeyOverrides;
            }

            foreach (KeyValuePair<string, string> pair in this.gameKeyOverrides)
            {
                list.Add(new GameKeyOverrideEntry { Binding = pair.Key, Path = pair.Value });
            }

            return list;
        }

        internal void ImportGameKeyOverrides(List<GameKeyOverrideEntry> saved)
        {
            this.pendingGameKeyOverrides = saved;
            if (saved == null || saved.Count == 0)
            {
                return;
            }

            this.RegisterWorldReadyCallback(GameKeysWorldReadyCallbackName, this.TryApplyPendingGameKeyOverrides);
        }

        private bool TryApplyPendingGameKeyOverrides()
        {
            List<GameKeyOverrideEntry> saved = this.pendingGameKeyOverrides;
            if (saved == null || saved.Count == 0)
            {
                return true;
            }

            List<GameKeyRow> rows = this.TryGetGameKeyRows();
            if (rows == null)
            {
                return false; // asset not up yet — the gate retries
            }

            int applied = 0;
            int skipped = 0;
            for (int i = 0; i < saved.Count; i++)
            {
                GameKeyOverrideEntry entry = saved[i];
                if (entry == null || string.IsNullOrEmpty(entry.Binding) || string.IsNullOrEmpty(entry.Path))
                {
                    continue;
                }

                GameKeyRow row = null;
                for (int r = 0; r < rows.Count; r++)
                {
                    if (string.Equals(GameKeyRowKey(rows[r]), entry.Binding, StringComparison.Ordinal))
                    {
                        row = rows[r];
                        break;
                    }
                }

                // A saved entry whose binding no longer exists (game update reordered the asset) is
                // DROPPED quietly rather than treated as an error — but it is counted and logged,
                // because silently losing a user's layout should at least leave a trace.
                if (row == null)
                {
                    skipped++;
                    continue;
                }

                if (this.TrySetGameKeyOverride(row, entry.Path))
                {
                    applied++;
                }
            }

            // ⚠️ Applying NOTHING is not "done" — it is the signature of having looked at the wrong
            // asset. Clearing `pending` here is how a whole saved layout got thrown away: the next
            // config write exports the (empty) live overrides over the user's file. Keep it and let
            // the gate try again; only a run that actually landed something may consume it.
            if (applied == 0 && skipped > 0)
            {
                ModLogger.Msg("[InputMap] none of the " + skipped
                              + " saved override(s) match the loaded asset — keeping them and retrying.");
                return false;
            }

            this.pendingGameKeyOverrides = null;
            ModLogger.Msg("[InputMap] restored " + applied + " saved key override(s)"
                          + (skipped > 0 ? ", " + skipped + " no longer match this asset" : "") + ".");
            return true;
        }

        // ---- key <-> control path ---------------------------------------------------------------
        //
        // The right-hand names are NOT guesses: they are the exact control names this build's asset
        // uses, read back out of AllKeyboardKeysMap (110 entries, one per physical key). Anything
        // absent from that list has no control to bind to, so the capture flow rejects it rather
        // than writing a path the Input System will silently fail to resolve.

        internal static string ControlPathForKeyCode(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z)
            {
                return "<Keyboard>/" + ((char)('a' + (key - KeyCode.A))).ToString();
            }

            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                return "<Keyboard>/" + ((char)('0' + (key - KeyCode.Alpha0))).ToString();
            }

            if (key >= KeyCode.F1 && key <= KeyCode.F12)
            {
                return "<Keyboard>/f" + (1 + (key - KeyCode.F1)).ToString();
            }

            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                return "<Keyboard>/numpad" + (key - KeyCode.Keypad0).ToString();
            }

            switch (key)
            {
                case KeyCode.Space: return "<Keyboard>/space";
                case KeyCode.Return: return "<Keyboard>/enter";
                case KeyCode.Tab: return "<Keyboard>/tab";
                case KeyCode.Backspace: return "<Keyboard>/backspace";
                case KeyCode.Escape: return "<Keyboard>/escape";
                case KeyCode.LeftShift: return "<Keyboard>/leftShift";
                case KeyCode.RightShift: return "<Keyboard>/rightShift";
                case KeyCode.LeftControl: return "<Keyboard>/leftCtrl";
                case KeyCode.RightControl: return "<Keyboard>/rightCtrl";
                case KeyCode.LeftAlt: return "<Keyboard>/leftAlt";
                case KeyCode.RightAlt: return "<Keyboard>/rightAlt";
                case KeyCode.LeftCommand: return "<Keyboard>/leftMeta";
                case KeyCode.RightCommand: return "<Keyboard>/rightMeta";
                case KeyCode.UpArrow: return "<Keyboard>/upArrow";
                case KeyCode.DownArrow: return "<Keyboard>/downArrow";
                case KeyCode.LeftArrow: return "<Keyboard>/leftArrow";
                case KeyCode.RightArrow: return "<Keyboard>/rightArrow";
                case KeyCode.Insert: return "<Keyboard>/insert";
                case KeyCode.Delete: return "<Keyboard>/delete";
                case KeyCode.Home: return "<Keyboard>/home";
                case KeyCode.End: return "<Keyboard>/end";
                case KeyCode.PageUp: return "<Keyboard>/pageUp";
                case KeyCode.PageDown: return "<Keyboard>/pageDown";
                case KeyCode.CapsLock: return "<Keyboard>/capsLock";
                case KeyCode.Numlock: return "<Keyboard>/numLock";
                case KeyCode.ScrollLock: return "<Keyboard>/scrollLock";
                case KeyCode.Pause: return "<Keyboard>/pause";
                case KeyCode.Print: return "<Keyboard>/printScreen";
                case KeyCode.Menu: return "<Keyboard>/contextMenu";
                case KeyCode.BackQuote: return "<Keyboard>/backquote";
                case KeyCode.Minus: return "<Keyboard>/minus";
                case KeyCode.Equals: return "<Keyboard>/equals";
                case KeyCode.LeftBracket: return "<Keyboard>/leftBracket";
                case KeyCode.RightBracket: return "<Keyboard>/rightBracket";
                case KeyCode.Backslash: return "<Keyboard>/backslash";
                case KeyCode.Semicolon: return "<Keyboard>/semicolon";
                case KeyCode.Quote: return "<Keyboard>/quote";
                case KeyCode.Comma: return "<Keyboard>/comma";
                case KeyCode.Period: return "<Keyboard>/period";
                case KeyCode.Slash: return "<Keyboard>/slash";
                case KeyCode.KeypadEnter: return "<Keyboard>/numpadEnter";
                case KeyCode.KeypadDivide: return "<Keyboard>/numpadDivide";
                case KeyCode.KeypadMultiply: return "<Keyboard>/numpadMultiply";
                case KeyCode.KeypadMinus: return "<Keyboard>/numpadMinus";
                case KeyCode.KeypadPlus: return "<Keyboard>/numpadPlus";
                case KeyCode.KeypadPeriod: return "<Keyboard>/numpadPeriod";
                case KeyCode.KeypadEquals: return "<Keyboard>/numpadEquals";
                case KeyCode.Mouse0: return "<Mouse>/leftButton";
                case KeyCode.Mouse1: return "<Mouse>/rightButton";
                case KeyCode.Mouse2: return "<Mouse>/middleButton";
                case KeyCode.Mouse3: return "<Mouse>/forwardButton";
                case KeyCode.Mouse4: return "<Mouse>/backButton";
                default: return null;
            }
        }

        // "<Keyboard>/leftShift" -> "Left Shift". Purely cosmetic; the path stays authoritative.
        internal static string FormatControlPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "-";
            }

            int slash = path.LastIndexOf('/');
            string control = slash >= 0 ? path.Substring(slash + 1) : path;
            if (control.Length == 0)
            {
                return path;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(control.Length + 4);
            sb.Append(char.ToUpperInvariant(control[0]));
            for (int i = 1; i < control.Length; i++)
            {
                char c = control[i];
                if (char.IsUpper(c) && !char.IsUpper(control[i - 1]))
                {
                    sb.Append(' ');
                }

                sb.Append(c);
            }

            if (path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase))
            {
                sb.Insert(0, "Mouse ");
            }

            return sb.ToString();
        }

        // Row caption: "move (up)" for composite parts, plain action name otherwise.
        internal static string FormatGameKeyRowLabel(GameKeyRow row)
        {
            if (row == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(row.Caption))
            {
                return row.Caption;
            }

            // A single-character direct action IS a key ("g"), so show it as one — lowercase reads
            // as a variable name and is the harder thing to spot when hunting for the G the game
            // just displayed.
            string label = row.Action.Length == 1 && string.Equals(row.Map, GameKeyDirectMapName, StringComparison.Ordinal)
                ? row.Action.ToUpperInvariant()
                : (string.IsNullOrEmpty(row.Part) ? row.Action : row.Action + "  (" + row.Part + ")");

            return string.IsNullOrEmpty(row.Shares) ? label : label + "  —  " + row.Shares;
        }
    }
}
