using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // CAMERA-MODE HOTKEY GUARD — when a mod hotkey collides with an interaction key that is LIVE
    // right now, the game wins and the mod stays quiet.
    //
    // The condition is not recomputed here, because the game already computes it and publishes the
    // answer in the scene graph. `IconsBarWidget.RefreshCellShortcutVisible` does:
    //
    //     bool flag = ShowKeyboardTip && MouseControlMode == MoveRotate;
    //     _shortCutCell[i]?.SetViewActive(flag && i < nodes.cells_list.ChildrenCount);
    //
    // and `UIWidget.SetViewActive(v)` is `ui?.gameObject.SetActive(v)`. So "the hint widget for
    // slot i is active in the hierarchy" IS, exactly, "camera mode is on AND slot i currently has
    // something to do" — `ChildrenCount` being the number of interactions the targeted object
    // offers. Reading `activeInHierarchy` therefore needs no hook, no AuraMono and no polling of
    // game state; it is one bool off an object we already know how to find.
    //
    // Scope is deliberately the camera-mode bar only (cells 1-4 plus the Q bar-switch). The HUD
    // chips (bag / map / camera …) collide the same way, but they are visible whenever keyboard
    // tips are on, so "is an action available" would not filter anything there — the block would be
    // permanent rather than contextual.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // Scene-graph anchors, from IconsBarWidget_Auto's binder:
        //   cells_list        = "root_visible@go/cells@t/cells@list"
        //   switchBar_shortcut = "root_visible@go/switchBar@shortcut"
        // A cell is a DIRECT child of cells@list, so its sibling index is the slot number — exact,
        // and independent of whatever the hint sprite happens to be showing.
        private const string CameraCellsListNode = "cells@list";
        private const string CameraSwitchBarNode = "switchBar@shortcut";

        // The direct-map actions the camera bar drives (TrackingPanel.RegisterPcControl:
        // Key1..Key4 -> TrackingBar_ClickInteractChild(0..3), KeyQ -> TrackingBar_SwitchInteractBar).
        private const int CameraSwitchBarSlot = -1;

        // Effective control path -> slot. Rebuilt whenever the layout changes, since a rebind moves
        // which physical key owns a slot.
        private readonly Dictionary<string, int> cameraSlotByPath =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private bool cameraSlotMapBuilt;

        private readonly List<Image> cameraHintProbes = new List<Image>();
        private readonly List<int> cameraHintSlots = new List<int>();
        private float cameraHintNextScanAt;
        private float cameraHintScanBackoff = 1f;
        private float cameraRowsNextTryAt;

        // MouseControlMode is a PlayerPrefs-backed property on GameSettingSystem
        // (`PlayerPrefs.GetInt("MouseControlMode", 1)`, 0 = MoveRotate), so it can be read straight
        // from the shared store. Cached per frame all the same: this sits in the hotkey path, which
        // runs ~50 times a frame, and PlayerPrefs.GetInt is a native call.
        private int cameraGuardModeFrame = -1;
        private bool cameraGuardModeOn;

        internal void InvalidateCameraSlotMap()
        {
            this.cameraSlotMapBuilt = false;
        }

        // The guard itself. Ordered cheapest-first: most calls die on the mode check, and a key that
        // is not an interaction key never reaches the probe list at all.
        private bool IsCameraInteractionHotkeyConflict(KeyCode key)
        {
            if (key == KeyCode.None || key == this.keyToggleMenu)
            {
                return false;
            }

            try
            {
                if (!this.IsCameraLookModeOnCached())
                {
                    return false;
                }

                string path = ControlPathForKeyCode(key);
                if (path == null)
                {
                    return false;
                }

                int slot;
                if (!this.TryGetCameraSlotForPath(path, out slot))
                {
                    return false;
                }

                this.EnsureCameraHintProbes();

                for (int i = 0; i < this.cameraHintProbes.Count; i++)
                {
                    if (this.cameraHintSlots[i] != slot)
                    {
                        continue;
                    }

                    Image probe = this.cameraHintProbes[i];
                    if (probe != null && probe.gameObject.activeInHierarchy)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                // Never let a guard failure swallow input: fail OPEN, the mod hotkey still works.
                return false;
            }
        }

        private bool IsCameraLookModeOnCached()
        {
            if (this.cameraGuardModeFrame == Time.frameCount)
            {
                return this.cameraGuardModeOn;
            }

            this.cameraGuardModeFrame = Time.frameCount;
            this.cameraGuardModeOn =
                PlayerPrefs.GetInt("MouseControlMode", GameMouseControlModeDragRotate)
                == GameMouseControlModeMoveRotate;
            return this.cameraGuardModeOn;
        }

        private bool TryGetCameraSlotForPath(string path, out int slot)
        {
            slot = 0;

            if (!this.cameraSlotMapBuilt)
            {
                if (!this.TryBuildCameraSlotMap())
                {
                    return false;
                }
            }

            return this.cameraSlotByPath.TryGetValue(path, out slot);
        }

        private bool TryBuildCameraSlotMap()
        {
            // Rows may not exist yet if the player has no saved overrides and has never opened the
            // Game Keys page. Building them is one ToJson + parse, so retry slowly rather than on
            // every keystroke.
            List<GameKeyRow> rows = this.gameKeyRows;
            if (rows == null)
            {
                if (Time.unscaledTime < this.cameraRowsNextTryAt)
                {
                    return false;
                }

                this.cameraRowsNextTryAt = Time.unscaledTime + 5f;
                rows = this.TryGetGameKeyRows();
                if (rows == null)
                {
                    return false;
                }
            }

            this.cameraSlotByPath.Clear();

            for (int i = 0; i < rows.Count; i++)
            {
                GameKeyRow row = rows[i];
                if (!string.Equals(row.Map, GameKeyDirectMapName, StringComparison.Ordinal))
                {
                    continue;
                }

                int slot;
                if (string.Equals(row.Action, "q", StringComparison.Ordinal))
                {
                    slot = CameraSwitchBarSlot;
                }
                else if (row.Action.Length == 1 && row.Action[0] >= '1' && row.Action[0] <= '4')
                {
                    slot = row.Action[0] - '1';
                }
                else
                {
                    continue;
                }

                this.cameraSlotByPath[this.GetGameKeyEffectivePath(row)] = slot;
            }

            this.cameraSlotMapBuilt = true;
            return true;
        }

        // Probes are cached: the game REUSES its `_shortCutCell` widgets, so one successful scan
        // lasts. The scan only ever runs for a key that already matched a camera slot, which means
        // the player actually has a colliding hotkey — everyone else pays nothing.
        private void EnsureCameraHintProbes()
        {
            for (int i = this.cameraHintProbes.Count - 1; i >= 0; i--)
            {
                if (this.cameraHintProbes[i] == null)
                {
                    this.cameraHintProbes.RemoveAt(i);
                    this.cameraHintSlots.RemoveAt(i);
                }
            }

            if (this.cameraHintProbes.Count > 0 || Time.unscaledTime < this.cameraHintNextScanAt)
            {
                return;
            }

            this.cameraHintNextScanAt = Time.unscaledTime + this.cameraHintScanBackoff;
            this.cameraHintScanBackoff = Mathf.Min(this.cameraHintScanBackoff * 2f, 15f);

            Il2CppArrayBase<Image> images = Resources.FindObjectsOfTypeAll<Image>();
            if (images == null)
            {
                return;
            }

            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null)
                {
                    continue;
                }

                int slot = CameraHintSlotForImage(image);
                if (slot == int.MinValue)
                {
                    continue;
                }

                this.cameraHintProbes.Add(image);
                this.cameraHintSlots.Add(slot);
            }

            if (this.cameraHintProbes.Count > 0)
            {
                this.cameraHintScanBackoff = 1f;
                ModLogger.Msg("[InputMap] camera-bar hint probes: " + this.cameraHintProbes.Count + ".");
            }
        }

        // int.MinValue = "not a camera-bar hint". Walks up remembering the previous node, because
        // the SLOT is the sibling index of the cell — the child of cells@list, not the hint itself.
        private static int CameraHintSlotForImage(Image image)
        {
            Transform previous = image.transform;
            Transform current = previous.parent;

            for (int depth = 0; depth < 6 && current != null; depth++)
            {
                string name = current.name;

                if (string.Equals(name, CameraCellsListNode, StringComparison.OrdinalIgnoreCase))
                {
                    return previous.GetSiblingIndex();
                }

                if (string.Equals(name, CameraSwitchBarNode, StringComparison.OrdinalIgnoreCase))
                {
                    return CameraSwitchBarSlot;
                }

                previous = current;
                current = current.parent;
            }

            return int.MinValue;
        }
    }
}
