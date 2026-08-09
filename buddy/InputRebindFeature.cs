using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // Reaching the game's InputActionAsset, and keeping its on-screen key HINTS honest.
    //
    // The game has no key configuration of its own: `ShortCutKeyMap` exposes only
    // `DefaultKeyBoardMap`/`DefaultGamePadMap` and reads them directly, `GameSettingSystem` carries
    // no keyboard settings, and there is no controls panel. The physical key -> action mapping lives
    // in a Unity `InputActionAsset`; `XDStandardControlsMono.Register` only ever binds by action
    // NAME ("1", "mainInteraction", "touchLook", ...). Rebinding itself lives in GameKeyBindings.cs
    // and the Settings→Game Keys page; this file owns the asset handle, the one-off dump, and the
    // hint sprites.
    //
    // ⚠️ Interop-shape notes, learned by reading the generated Unity.InputSystem interop rather than
    // assuming: only the MAP-level `ApplyBindingOverride(InputActionMap, int, InputBinding)` was
    // generated (no action-level overloads), and `LoadBindingOverridesFromJson` /
    // `RemoveAllBindingOverrides` were not generated at all. `InputActionAsset.ToJson()` WAS, which
    // is what the dump uses — one call, no generic instantiation over a value type
    // (`ReadOnlyArray<InputBinding>` may have no AOT instance in this build, so enumerating bindings
    // that way is a crash risk; parsing the JSON is not).
    public partial class HeartopiaComplete
    {
        // Every [InputMap] trace — the one-off asset dump, the per-binding echo, rebind writes and
        // hint relabelling spread across GameKeyBindings / CameraModeHotkeyGuard / this file — routes
        // through LogInputMap so a SINGLE Settings → Logging switch governs the lot. OFF by default:
        // these lines are gold when diagnosing key mapping and pure noise otherwise. Session-only,
        // like every MasterLog* flag ([[game-key-rebinding-and-hints]]).
        internal static bool MasterLogInputMap = false;

        private static void LogInputMap(string message)
        {
            if (MasterLogInputMap)
            {
                ModLogger.Msg("[InputMap] " + message);
            }
        }

        private bool inputRebindDumpWritten;

        // ---- key-hint icons -----------------------------------------------------------------
        //
        // The game draws key hints TWO different ways, and only one of them is generated at all.
        //
        // GENERATED (the camera-mode interaction bar's 1..4 and Q): `ShortCutKeyUtil.GetKeyboardIcon`
        // maps an InputEvent through a HARD-CODED switch to "keymapping_KB_<KEY>" and hands it to
        // `ShortCutKeyWidget.SetIcon`. There is no table behind that switch, so a rebind cannot move
        // it — but the sprite it produced can be replaced.
        //
        // BAKED (the HUD's own bag / camera / map / chat / vehicle chips): the letter is part of the
        // prefab and the widget only calls SetActive on it. Nothing recomputes these, ever — not
        // even the game. They are handled separately below, keyed on the widget node they live under.
        //
        // Reaching it by DETOURING GetKeyboardIcon was the obvious idea and is the wrong one here:
        // new native detours on this build are 3-for-3 crashers, and this one would also return a
        // struct (win64 sret). Writing Image.sprite needs no hook at all — and it sticks, because
        // CustomImage.SetIcon early-outs when the AtlasSpriteID is unchanged, so the game only
        // re-asserts a hint when the widget is rebuilt. Hence a slow re-check rather than a
        // per-frame fight.
        //
        // WHICH binding drives a GENERATED hint is the part that is easy to get wrong. It comes from
        // `InputEvent.KeyN`, and every `InputEvent.KeyN` is registered against the self-named action
        // in AllKeyboardKeysMap — `XDStandardControlsMono.Register` literally does
        // `RegisterButtonAction(InputEvent.KeyV, "v")`. GameActionMap ALSO binds <Keyboard>/v (to
        // `openCamera` and `recentre`), but rebinding those does not move InputEvent.KeyV. Hence the
        // swap table below is built from the direct map ONLY. The baked hints are the mirror image:
        // they follow a SEMANTIC event (`InputEvent.OpenCamera` -> "openCamera") and ignore the
        // direct map entirely.
        // TWO sprite families, confirmed from the live log rather than assumed:
        //   keymapping_KB_<KEY>       — what ShortCutKeyUtil generates (camera-mode bar).
        //   keymapping_icon_KB_<KEY>  — the round chips the HUD prefabs ship (bag / camera / map…).
        // They are different art for the same letter, so a replacement must stay in the family of
        // the sprite it replaces or the chip renders in the wrong style next to its neighbours —
        // which is exactly what the first working version did.
        private const string KeyIconPrefix = "keymapping_KB_";
        private const string KeyChipIconPrefix = "keymapping_icon_KB_";

        // The HUD's own action hints are a SECOND, unrelated mechanism and they are why a rebind
        // appeared to do nothing. `CameraEntryWidget` listens on `InputEvent.OpenCamera` (registered
        // to the GameActionMap action "openCamera", NOT to InputEvent.KeyV) and its hint is just
        // `nodes.hudCamerashortcut_go?.SetActive(GameSettingSystem.Instance.ShowKeyboardTip)` — a
        // bare GameObject toggled on and off. There is no SetIcon anywhere on that path: the letter
        // is BAKED INTO THE PREFAB, so the game itself never updates it either.
        //
        // Node path -> the action whose key it advertises. Node names come from the widgets' _Auto
        // binders; the action names are the ones XDStandardControlsMono registers each semantic
        // InputEvent against, and all nine exist in the live asset.
        private static readonly string[][] GameKeyHintNodes =
        {
            new[] { "bagshortcut@go", "openBag" },
            new[] { "hudCamerashortcut@go", "openCamera" },
            new[] { "chatshortcut@go", "chat" },
            new[] { "hudEmojishortcut@go", "openEmoji" },
            new[] { "handleshortcut@go", "openHandle" },
            new[] { "hudVehicleshortcut@go", "TakeVehicle" },
            new[] { "mapshortcut@go", "openMap" },
            new[] { "spaceshortcut@go", "jump" },
            new[] { "watchshortcut@go", "openWatch" },
        };

        private readonly HashSet<string> keyHintNodesReported = new HashSet<string>(StringComparer.Ordinal);

        // default icon key -> new icon key, e.g. "1" -> "E". Rebuilt whenever an override changes.
        private readonly Dictionary<string, string> keyIconSwaps = new Dictionary<string, string>(StringComparer.Ordinal);
        // icon key -> sprite, indexed once from the loaded atlas, one dictionary per family.
        private readonly Dictionary<string, Sprite> keyIconSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly Dictionary<string, Sprite> keyChipIconSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        private readonly List<Image> keyIconTargets = new List<Image>();
        private readonly List<Sprite> keyIconOriginals = new List<Sprite>();
        private readonly List<Sprite> keyIconApplied = new List<Sprite>();
        private readonly List<int> keyIconTargetIds = new List<int>();

        // Containers the hints live under (the interaction bar's cell list, each HUD chip node).
        // Discovered by the full scan, then swept cheaply every tick — see SweepKeyIconRoots.
        private readonly List<Transform> keyIconRoots = new List<Transform>();

        private bool keyIconSwapsDirty = true;
        private bool keyIconSpritesIndexed;
        private float keyIconIndexNextTryAt;
        private float keyIconIndexBackoff = 1f;
        private bool keyIconIndexMissReported;
        private float keyIconNextCheckAt;
        private float keyIconNextScanAt;

        // Full dump of the live keymap. Written once per session: the action list is long, so the
        // whole JSON goes to a file and only the keyboard bindings are echoed to the log.
        internal void LogInputActionMapDumpOnce()
        {
            if (this.inputRebindDumpWritten)
            {
                return;
            }

            this.inputRebindDumpWritten = true;

            try
            {
                InputActionAsset asset = this.FindGameInputActionAsset();
                if (asset == null)
                {
                    LogInputMap("no InputActionAsset found — key rebinding unavailable.");
                    return;
                }

                string json = asset.ToJson();
                if (string.IsNullOrEmpty(json))
                {
                    LogInputMap("InputActionAsset.ToJson() returned nothing.");
                    return;
                }

                // Unchanged destination — HelperPaths resolves the same LocalLow root, minus the
                // hand-built path (see PlayerLoopPumpMarker for why that copy was a liability).
                string path = HelperPaths.GetFile("inputmap.json");
                System.IO.File.WriteAllText(path, json);
                LogInputMap("full asset written to " + path + " (" + json.Length + " chars).");

                foreach (string line in DescribeKeyboardBindings(json))
                {
                    LogInputMap(line);
                }
            }
            catch (Exception ex)
            {
                LogInputMap("dump failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal void MarkKeyIconSwapsDirty()
        {
            this.keyIconSwapsDirty = true;

            // A rebind moves which physical key owns a camera slot, so the guard's map is stale too
            // (CameraModeHotkeyGuard.cs). This is the single "the layout changed" signal.
            this.InvalidateCameraSlotMap();
            this.keyIconNextCheckAt = 0f;
            this.keyIconNextScanAt = 0f;

            // Re-arm the sprite index: a change means the player is in a menu, long past loading,
            // so this is exactly the moment a previously-empty atlas is most likely to be there.
            if (!this.keyIconSpritesIndexed)
            {
                this.keyIconIndexNextTryAt = 0f;
                this.keyIconIndexBackoff = 1f;
            }
        }

        // Called every frame from OnUpdate; does real work at most twice a second, and nothing at
        // all while no override is set — which is the common case.
        internal void ProcessGameKeyIconsOnUpdate()
        {
            if (this.keyIconSwapsDirty)
            {
                this.RebuildKeyIconSwaps();
            }

            // Overrides matter even with an EMPTY swap table: the swap table is built from the
            // direct map only, but a baked HUD hint follows a gameplay action (openCamera), which
            // has no direct-map entry at all.
            if (this.keyIconSwaps.Count == 0 && this.gameKeyOverrides.Count == 0
                && this.keyIconTargets.Count == 0)
            {
                return;
            }

            if (Time.unscaledTime < this.keyIconNextCheckAt)
            {
                return;
            }

            // 0.25 s rather than 0.5: the window that matters is between a hint widget appearing
            // and the player reading it, and once the containers are known a tick is only a walk
            // over a handful of small subtrees.
            this.keyIconNextCheckAt = Time.unscaledTime + 0.25f;

            try
            {
                if (!this.EnsureKeyIconSpriteIndex())
                {
                    return;
                }

                for (int i = this.keyIconTargets.Count - 1; i >= 0; i--)
                {
                    if (this.keyIconTargets[i] == null)
                    {
                        this.keyIconTargets.RemoveAt(i);
                        this.keyIconOriginals.RemoveAt(i);
                        this.keyIconApplied.RemoveAt(i);
                        this.keyIconTargetIds.RemoveAt(i);
                    }
                }

                // Hint widgets are created on demand (one per interaction cell of whatever is being
                // looked at), so new stale hints keep appearing — but far too rarely to justify a
                // scan every tick.
                // Cheap first, every tick: catches hint widgets the game just created.
                this.SweepKeyIconRoots();

                if (Time.unscaledTime >= this.keyIconNextScanAt)
                {
                    this.keyIconNextScanAt = Time.unscaledTime + (this.keyIconRoots.Count > 0 ? 10f : 3f);
                    this.ScanForKeyIconTargets();
                }

                for (int i = 0; i < this.keyIconTargets.Count; i++)
                {
                    Image image = this.keyIconTargets[i];
                    Sprite want = this.keyIconApplied[i];
                    Sprite current = image.sprite;
                    if (want != null && (current == null || current.GetInstanceID() != want.GetInstanceID()))
                    {
                        image.sprite = want;
                    }
                }
            }
            catch (Exception ex)
            {
                LogInputMap("icon sync failed: " + ex.GetType().Name + ": " + ex.Message);
                this.keyIconNextCheckAt = Time.unscaledTime + 10f;
            }
        }

        // Derives the swap table from the live overrides. Any change restores every swapped hint
        // first: a target captured under the old table would otherwise keep an obsolete "original".
        private void RebuildKeyIconSwaps()
        {
            this.keyIconSwapsDirty = false;
            this.RestoreKeyIcons();
            this.keyIconSwaps.Clear();

            List<GameKeyRow> rows = this.gameKeyRows;
            if (rows == null || this.gameKeyOverrides.Count == 0)
            {
                return;
            }

            // ONLY AllKeyboardKeysMap participates, and that is the whole correctness argument.
            // Every hint resolves as InputEvent.KeyN -> GetKeyboardIcon(KeyN), and every
            // InputEvent.KeyN is registered against that map's self-named action
            // (XDStandardControlsMono.Register: `RegisterButtonAction(InputEvent.KeyV, "v")`).
            // GameActionMap's `openCamera` also sits on <Keyboard>/v, but rebinding IT does not
            // move InputEvent.KeyV — so the V chip genuinely still means V and relabelling it would
            // be a lie. As a bonus this removes every false conflict: the direct map holds exactly
            // one action per physical key, so a group can never disagree with itself.
            int iconless = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                GameKeyRow row = rows[i];
                if (!string.Equals(row.Map, GameKeyDirectMapName, StringComparison.Ordinal))
                {
                    continue;
                }

                string from = IconKeyForControlPath(row.DefaultPath);
                if (from == null)
                {
                    continue; // this key has no hint sprite at all
                }

                string to = IconKeyForControlPath(this.GetGameKeyEffectivePath(row));
                if (to == null)
                {
                    // Rebound onto a key the game has no icon for — the widget hides itself, so
                    // there is nothing to relabel.
                    iconless++;
                    continue;
                }

                if (!string.Equals(from, to, StringComparison.Ordinal))
                {
                    this.keyIconSwaps[from] = to;
                }
            }

            if (this.keyIconSwaps.Count > 0)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (KeyValuePair<string, string> pair in this.keyIconSwaps)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(pair.Key).Append("->").Append(pair.Value);
                }

                LogInputMap("key hints relabelled: " + sb.ToString()
                              + (iconless > 0 ? " (" + iconless + " rebound onto a key with no hint sprite)" : ""));
            }
            else if (iconless > 0)
            {
                LogInputMap("no key hints relabelled — " + iconless
                              + " direct key(s) were rebound onto keys the game has no icon for.");
            }
        }

        // One pass over the loaded sprites, building icon key -> sprite. All keymapping_KB_* live in
        // the same atlas, so if any hint is on screen they are all loaded.
        //
        // ⚠️ Retries must NOT be a small fixed budget. The first version allowed 10 attempts at the
        // 0.5 s tick — five seconds measured from world-ready, which is BEFORE the UI atlas loads.
        // It burned through them during the loading screen, gave up permanently, and every later
        // rebind that session silently did nothing (the log said exactly that, once, and then went
        // quiet). Now it keeps trying with an exponential back-off, and any override change re-arms
        // it: the scan is not cheap, but it stops for good the moment it succeeds.
        private bool EnsureKeyIconSpriteIndex()
        {
            if (this.keyIconSpritesIndexed)
            {
                return true;
            }

            if (Time.unscaledTime < this.keyIconIndexNextTryAt)
            {
                return false;
            }

            this.keyIconIndexNextTryAt = Time.unscaledTime + this.keyIconIndexBackoff;
            this.keyIconIndexBackoff = Mathf.Min(this.keyIconIndexBackoff * 2f, 30f);

            Il2CppArrayBase<Sprite> sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            if (sprites == null)
            {
                return false;
            }

            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite == null)
                {
                    continue;
                }

                bool chip;
                string key = KeymappingIconKey(sprite.name, out chip);
                if (key == null)
                {
                    continue;
                }

                Dictionary<string, Sprite> family = chip ? this.keyChipIconSprites : this.keyIconSprites;
                if (!family.ContainsKey(key))
                {
                    family[key] = sprite;
                }
            }

            if (this.keyIconSprites.Count == 0 && this.keyChipIconSprites.Count == 0)
            {
                if (!this.keyIconIndexMissReported)
                {
                    this.keyIconIndexMissReported = true;
                    LogInputMap("no " + KeyIconPrefix + "* sprites loaded yet — retrying with back-off.");
                }

                return false;
            }

            this.keyIconSpritesIndexed = true;
            LogInputMap("key-hint sprites indexed: " + this.keyIconSprites.Count + " plain, "
                          + this.keyChipIconSprites.Count + " chip.");
            return true;
        }

        // Full sweep of every loaded Image. Expensive, so it runs rarely and its real job is to
        // discover the few CONTAINERS the hints live under; the per-tick sweep below works off
        // those. Kept as a periodic safety net for containers that appear later.
        private void ScanForKeyIconTargets()
        {
            if (this.keyIconSwaps.Count == 0 && this.gameKeyOverrides.Count == 0)
            {
                return;
            }

            Il2CppArrayBase<Image> images = Resources.FindObjectsOfTypeAll<Image>();
            if (images == null)
            {
                return;
            }

            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image != null)
                {
                    this.TryAdoptKeyIconTarget(image);
                }
            }
        }

        // Cheap sweep of the known containers, every tick. The full scan alone was too slow to be
        // correct: the game GROWS `_shortCutCell` when a target offers more interactions than the
        // last one did, so walking up to a flower with three actions creates brand-new hint widgets
        // that kept the default label until the next full scan up to three seconds later — which is
        // exactly the window in which the player reads the hint and presses the key.
        private void SweepKeyIconRoots()
        {
            for (int i = this.keyIconRoots.Count - 1; i >= 0; i--)
            {
                Transform root = this.keyIconRoots[i];
                if (root == null)
                {
                    this.keyIconRoots.RemoveAt(i);
                    continue;
                }

                Il2CppArrayBase<Image> images = root.GetComponentsInChildren<Image>(true);
                if (images == null)
                {
                    continue;
                }

                for (int j = 0; j < images.Length; j++)
                {
                    Image image = images[j];
                    if (image != null)
                    {
                        this.TryAdoptKeyIconTarget(image);
                    }
                }
            }
        }

        private void TryAdoptKeyIconTarget(Image image)
        {
            int id = image.GetInstanceID();
            if (this.keyIconTargetIds.Contains(id))
            {
                return;
            }

            Sprite sprite = image.sprite;
            if (sprite == null)
            {
                return;
            }

            // Harvest the instance the GAME itself put on a live widget. Several distinct Sprite
            // objects share one name, and only some of them render — picking the wrong one is how
            // a chip went blank. An instance seen in use is proof it works, so it wins over
            // whatever the bulk index happened to grab first. Safe here because a target we have
            // already written to was filtered out above, so this is never our own replacement.
            bool harvestChip;
            string harvestKey = KeymappingIconKey(sprite.name, out harvestChip);
            if (harvestKey != null)
            {
                if (harvestChip)
                {
                    this.keyChipIconSprites[harvestKey] = sprite;
                }
                else
                {
                    this.keyIconSprites[harvestKey] = sprite;
                }
            }

            // Two kinds of target. The generated hints name their sprite after the key, so the key
            // IS the lookup. The baked HUD hints do not — their sprite is prefab art with no
            // relationship to the binding, so they are identified by the widget node they hang
            // under and resolved through that node's action instead.
            Sprite replacement = this.ResolveKeyIconReplacement(image, sprite);
            if (replacement == null)
            {
                return;
            }

            this.keyIconTargets.Add(image);
            this.keyIconOriginals.Add(sprite);
            this.keyIconApplied.Add(replacement);
            this.keyIconTargetIds.Add(id);
            this.RememberKeyIconRoot(image.transform);
        }

        // The container a hint hangs under — the cell list for the interaction bar, the widget node
        // for a HUD chip. Remembering the LIST (not the cell) is the point: new cells are added to
        // it, and that is what the per-tick sweep has to see.
        private void RememberKeyIconRoot(Transform hint)
        {
            Transform current = hint;
            for (int depth = 0; depth < 6 && current != null; depth++)
            {
                string name = current.name;
                bool isRoot = name.IndexOf("cells@list", StringComparison.OrdinalIgnoreCase) >= 0
                              || name.IndexOf("shortcut@go", StringComparison.OrdinalIgnoreCase) >= 0
                              || name.IndexOf("switchBar@shortcut", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isRoot)
                {
                    for (int i = 0; i < this.keyIconRoots.Count; i++)
                    {
                        if (this.keyIconRoots[i] != null
                            && this.keyIconRoots[i].GetInstanceID() == current.GetInstanceID())
                        {
                            return;
                        }
                    }

                    this.keyIconRoots.Add(current);
                    return;
                }

                current = current.parent;
            }
        }

        private Sprite ResolveKeyIconReplacement(Image image, Sprite sprite)
        {
            // A hint node holds SEVERAL images — the letter plus decoration. Only the one already
            // showing a key letter may be touched. Without this gate the node lookup below happily
            // matched the chip's `common_bg_shadow` layer and stamped the letter over it, which
            // rendered as a second glyph behind the real one (the "extra outline").
            bool chip;
            string from = KeymappingIconKey(sprite.name, out chip);
            if (from == null)
            {
                return null;
            }

            // 1) The sprite names its own key, so the swap table answers directly. Covers the
            // generated bar AND any chip whose key moved as part of a group rebind.
            string to;
            if (this.keyIconSwaps.TryGetValue(from, out to))
            {
                return this.FindHintSprite(to, chip);
            }

            // 2) Baked HUD hint whose key did NOT move on its own — identified by the widget node
            // it sits under and resolved through that node's action. Only a few levels up; these
            // nodes are the hint's own root or its immediate parent.
            string action = this.FindKeyHintActionForImage(image);
            if (action == null)
            {
                return null;
            }

            List<GameKeyRow> rows = this.gameKeyRows;
            if (rows == null)
            {
                return null;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (!string.Equals(rows[i].Action, action, StringComparison.Ordinal))
                {
                    continue;
                }

                string iconKey = IconKeyForControlPath(this.GetGameKeyEffectivePath(rows[i]));
                if (iconKey == null)
                {
                    return null;
                }

                // Compare KEYS, not sprite instances. The same letter exists as several distinct
                // Sprite objects sharing one name, so an instance check called them different and
                // "replaced" an untouched hint with a foreign instance of its own letter — which
                // renders blank. A hint already showing the right key must be left strictly alone.
                if (string.Equals(from, iconKey, StringComparison.Ordinal))
                {
                    return null;
                }

                Sprite want = this.FindHintSprite(iconKey, chip);
                if (want == null)
                {
                    return null;
                }

                // Log only a real relabel — the no-op case used to log all nine nodes every session.
                if (this.keyHintNodesReported.Add(action))
                {
                    LogInputMap("HUD hint '" + action + "': '" + sprite.name
                                  + "' -> '" + want.name + "'.");
                }

                return want;
            }

            return null;
        }

        private string FindKeyHintActionForImage(Image image)
        {
            Transform t = image.transform;
            for (int depth = 0; depth < 4 && t != null; depth++)
            {
                string name = t.name;
                for (int i = 0; i < GameKeyHintNodes.Length; i++)
                {
                    if (string.Equals(name, GameKeyHintNodes[i][0], StringComparison.OrdinalIgnoreCase))
                    {
                        return GameKeyHintNodes[i][1];
                    }
                }

                t = t.parent;
            }

            return null;
        }

        internal void RestoreKeyIcons()
        {
            for (int i = 0; i < this.keyIconTargets.Count; i++)
            {
                Image image = this.keyIconTargets[i];
                Sprite original = this.keyIconOriginals[i];
                Sprite applied = this.keyIconApplied[i];
                if (image == null || original == null || applied == null)
                {
                    continue;
                }

                // Only undo OUR write: if something else has since changed the icon, leave it be.
                Sprite current = image.sprite;
                if (current != null && current.GetInstanceID() == applied.GetInstanceID())
                {
                    image.sprite = original;
                }
            }

            this.keyIconTargets.Clear();
            this.keyIconOriginals.Clear();
            this.keyIconApplied.Clear();
            this.keyIconTargetIds.Clear();
            // Roots are NOT cleared: they are scene containers, not our writes, and keeping them
            // means a changed layout re-applies on the next tick instead of after a full scan.
        }

        // Sprites pulled out of an atlas come back named "<name>(Clone)", so compare on the stem.
        // `chip` reports WHICH family the sprite belongs to, so a replacement can stay in it.
        private static string KeymappingIconKey(string spriteName, out bool chip)
        {
            chip = false;
            if (string.IsNullOrEmpty(spriteName))
            {
                return null;
            }

            int clone = spriteName.IndexOf("(Clone)", StringComparison.Ordinal);
            string stem = (clone >= 0 ? spriteName.Substring(0, clone) : spriteName).Trim();

            // Chip first: both start with "keymapping_", only one starts with "keymapping_KB_".
            if (stem.StartsWith(KeyChipIconPrefix, StringComparison.OrdinalIgnoreCase))
            {
                chip = true;
                return stem.Substring(KeyChipIconPrefix.Length);
            }

            return stem.StartsWith(KeyIconPrefix, StringComparison.OrdinalIgnoreCase)
                ? stem.Substring(KeyIconPrefix.Length)
                : null;
        }

        // Same family when it has the key; otherwise the other one. A hint in the wrong STYLE is a
        // cosmetic mismatch, a hint with the wrong LETTER is misinformation — so falling back beats
        // leaving it stale.
        private Sprite FindHintSprite(string key, bool chip)
        {
            Sprite sprite;
            if (chip)
            {
                if (this.keyChipIconSprites.TryGetValue(key, out sprite))
                {
                    return sprite;
                }

                return this.keyIconSprites.TryGetValue(key, out sprite) ? sprite : null;
            }

            if (this.keyIconSprites.TryGetValue(key, out sprite))
            {
                return sprite;
            }

            return this.keyChipIconSprites.TryGetValue(key, out sprite) ? sprite : null;
        }

        // The inverse of ShortCutKeyUtil.GetKeyNameFromInputEvent, which is a fixed switch — so this
        // list is EXHAUSTIVE, not a subset. Anything not here genuinely has no hint sprite: the game
        // returns an empty AtlasSpriteID and ShortCutComponent hides the widget outright.
        private static string IconKeyForControlPath(string path)
        {
            if (string.IsNullOrEmpty(path)
                || !path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string control = path.Substring("<Keyboard>/".Length);
            if (control.Length == 1)
            {
                char c = control[0];
                if (c >= 'a' && c <= 'z')
                {
                    return char.ToUpperInvariant(c).ToString();
                }

                if (c >= '0' && c <= '9')
                {
                    return control;
                }

                return null;
            }

            switch (control)
            {
                case "enter": return "Enter";
                case "escape": return "Esc";
                case "tab": return "Tab";
                case "space": return "Space";
                case "delete": return "Delete";
                case "leftShift":
                case "rightShift": return "Shift";
                case "leftCtrl":
                case "rightCtrl": return "Ctrl";
                case "leftAlt":
                case "rightAlt": return "Alt";
                default: return null;
            }
        }

        private InputActionAsset FindGameInputActionAsset()
        {
            // The asset hangs off the IL2CPP-side input host, but scanning loaded objects finds it
            // without needing that host's instance — the same approach the icon loader uses for
            // sprites, and it does not care which assembly owns the reference.
            Il2CppArrayBase<InputActionAsset> all = Resources.FindObjectsOfTypeAll<InputActionAsset>();
            if (all == null)
            {
                return null;
            }

            for (int i = 0; i < all.Length; i++)
            {
                InputActionAsset candidate = all[i];

                // ONLY the one that carries the game's own actions. There used to be a "otherwise
                // take the first asset found" fallback, and it was a trap: called before the game's
                // asset loads it happily returned a FOREIGN InputActionAsset (maps named Player/UI,
                // 22 bindings), whose rows were then cached for the whole session — an empty Game
                // Keys page and every saved override reported as no-longer-matching. Fail closed
                // and let the caller retry.
                if (candidate != null && candidate.FindAction("touchLook", false) != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        // ---- tiny JSON scanner ------------------------------------------------------------------
        //
        // Deliberately not a real parser: the asset JSON is Unity's own fixed shape, and pulling a
        // dependency (or hand-rolling one) to read two field names would be far more code than the
        // job needs. Anything unexpected is treated as "no match".

        private static IEnumerable<string> DescribeKeyboardBindings(string json)
        {
            List<string> lines = new List<string>();
            string path = null;

            foreach (string raw in json.Split('\n'))
            {
                string trimmed = raw.Trim();

                string p = ReadJsonString(trimmed, "\"path\": \"");
                if (p != null)
                {
                    path = p;
                    continue;
                }

                string action = ReadJsonString(trimmed, "\"action\": \"");
                if (action != null && path != null)
                {
                    if (path.IndexOf("eyboard", StringComparison.Ordinal) >= 0
                        || path.IndexOf("ouse", StringComparison.Ordinal) >= 0)
                    {
                        lines.Add(path + "  ->  " + action);
                    }

                    path = null;
                }
            }

            lines.Sort(StringComparer.OrdinalIgnoreCase);
            return lines;
        }

        private static string ReadJsonString(string line, string key)
        {
            int at = line.IndexOf(key, StringComparison.Ordinal);
            if (at < 0)
            {
                return null;
            }

            int start = at + key.Length;
            int end = line.IndexOf('"', start);
            return end > start ? line.Substring(start, end - start) : null;
        }
    }
}
