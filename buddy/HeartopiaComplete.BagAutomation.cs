﻿using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using UnityObject = UnityEngine.Object;
using Il2CppType = Il2CppSystem.Type;
using Il2CppFieldInfo = Il2CppSystem.Reflection.FieldInfo;
using Il2CppMethodInfo = Il2CppSystem.Reflection.MethodInfo;
using Il2CppPropertyInfo = Il2CppSystem.Reflection.PropertyInfo;
using Il2CppBindingFlags = Il2CppSystem.Reflection.BindingFlags;
using Il2CppObject = Il2CppSystem.Object;
using Object = UnityEngine.Object;


namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private int FindClosestItemIndexLocal(Vector3 playerPos, Vector3[] positions)
        {
            int result = -1;
            float bestSqr = 25f; // 5 units
            for (int i = 0; i < positions.Length; i++)
            {
                float sq = (positions[i] - playerPos).sqrMagnitude;
                if (sq < bestSqr)
                {
                    bestSqr = sq;
                    result = i;
                }
            }
            return result;
        }

        private bool IsBagAutomationActiveOrQueued()
        {
            return this.IsAutoRepairActiveOrQueued() || this.IsAutoEatActiveOrQueued();
        }

        private void ProcessPendingBagAutomation()
        {
            if (this.isRepairing || this.isAutoEating)
            {
                return;
            }

            if (this.pendingAutoRepairRequest)
            {
                this.pendingAutoRepairRequest = false;
                this.lastStartWasAutoRepair = true;
                this.AutoEatRepairLog("[AutoRepair] Running queued durability repair request.");
                this.StartRepair(true);
                this.resourceRepairPauseUntil = Time.time + this.resourceAutoRepairPauseSeconds;
                return;
            }

            if (this.pendingAutoEatRequest)
            {
                if (!this.autoEatAutoTriggerEnabled || !this.IsEnergyAtOrBelowAutoEatTrigger() || Time.unscaledTime < this.nextAutoEatDirectRetryAt)
                {
                    this.pendingAutoEatRequest = false;
                    return;
                }

                this.pendingAutoEatRequest = false;
                this.AutoEatRepairLog($"[AutoEat] Running queued energy panel request ({this.GetCurrentEnergyDisplay()}, threshold={this.autoEatTriggerPercent}%).");
                this.StartAutoEat();
            }
        }

        private bool TryGetDirectBagExecutor(out object bagObj, out Type functionType, out Type storageType, out MethodInfo execute)
        {
            bagObj = this.cachedDirectBagModuleObj;
            functionType = this.cachedDirectBackpackFunctionType;
            storageType = this.cachedDirectBagStorageType;
            execute = this.cachedDirectExecuteBackpackItemFuncMethod;
            if (bagObj != null && functionType != null && storageType != null && execute != null)
            {
                return true;
            }

            Type bagType = this.FindLoadedType("XDTLevelAndEntity.Game.Module.Bag.BagModule", "BagModule");
            if (bagType == null)
            {
                this.AutoEatRepairLog("[DirectBackpackManaged] BagModule type unavailable.");
                return false;
            }

            if (!this.TryGetManagedModule(bagType, out bagObj) || bagObj == null)
            {
                this.AutoEatRepairLog("[DirectBackpackManaged] BagModule instance unavailable.");
                return false;
            }

            functionType = this.FindLoadedType("XDTGameSystem.GameplaySystem.BackPack.BackpackItemFunction", "BackpackItemFunction");
            storageType = this.FindLoadedType("EcsClient.XDT.Scene.Shared.Data.StaticPartial.EStorageType", "EStorageType");
            if (functionType == null || storageType == null)
            {
                this.AutoEatRepairLog("[DirectBackpackManaged] enum type unavailable. functionType=" + (functionType != null) + " storageType=" + (storageType != null));
                return false;
            }

            execute = bagObj.GetType().GetMethod("ExecuteBackpackItemFunc", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { functionType, typeof(uint), storageType }, null);
            if (execute == null)
            {
                this.AutoEatRepairLog("[DirectBackpackManaged] ExecuteBackpackItemFunc method unavailable.");
                return false;
            }

            this.cachedDirectBagModuleObj = bagObj;
            this.cachedDirectBackpackFunctionType = functionType;
            this.cachedDirectBagStorageType = storageType;
            this.cachedDirectExecuteBackpackItemFuncMethod = execute;
            return true;
        }

        private bool IsBagOpen()
        {
            GameObject bag = GameObject.Find(BAG_PANEL_PATH);
            return bag != null && bag.activeInHierarchy;
        }

        private bool IsLikelyBagItemSprite(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName))
            {
                return false;
            }

            return spriteName.StartsWith("ui_item_normal_p_", StringComparison.Ordinal)
                || spriteName.StartsWith("ui_item_special_p_", StringComparison.Ordinal)
                || spriteName.StartsWith("p_", StringComparison.Ordinal)
                || spriteName.Contains("_birdphoto")
                || spriteName.Contains("_gather_")
                || spriteName.Contains("_food_");
        }

        private string ResolveBagItemDisplayName(string matchKey, int staticId)
        {
            if (staticId > 0 && this.TryGetResolvedFoodNameFromStaticId(staticId, out string tableName) && !this.IsPoorBagItemDisplayName(tableName, staticId))
            {
                return tableName;
            }

            string spriteName = this.GetAutoSellItemDisplayName(matchKey);
            if (!this.IsPoorBagItemDisplayName(spriteName, staticId))
            {
                return spriteName;
            }

            return staticId > 0 ? ("Item " + staticId) : (string.IsNullOrWhiteSpace(spriteName) ? "Unknown Item" : spriteName);
        }

        private bool IsPoorBagItemDisplayName(string displayName, int staticId)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return true;
            }

            string trimmed = displayName.Trim();
            if (trimmed.Length > 0)
            {
                bool allQuestionMarks = true;
                for (int i = 0; i < trimmed.Length; i++)
                {
                    if (trimmed[i] != '?')
                    {
                        allQuestionMarks = false;
                        break;
                    }
                }

                if (allQuestionMarks)
                {
                    return true;
                }
            }

            if (staticId > 0 && trimmed.StartsWith(staticId.ToString(), StringComparison.Ordinal))
            {
                string suffix = trimmed.Substring(staticId.ToString().Length).Trim();
                if (suffix.Length == 0 || this.IsNumericTokenSequence(suffix))
                {
                    return true;
                }
            }

            if (this.IsNumericTokenSequence(trimmed))
            {
                return true;
            }

            for (int i = 0; i < trimmed.Length; i++)
            {
                if (!char.IsDigit(trimmed[i]))
                {
                    return false;
                }
            }

            return trimmed.Length > 0;
        }

        private bool DoesBagItemEntryMatchSprite(AutoSellBagItemEntry entry, string normalizedSprite)
        {
            if (entry == null || string.IsNullOrWhiteSpace(normalizedSprite))
            {
                return false;
            }

            string[] keys =
            {
                this.NormalizeAutoSellMatchKey(entry.SpriteName),
                this.NormalizeAutoSellMatchKey(entry.MatchKey),
                this.NormalizeAutoSellMatchKey(this.GetAutoSellSpriteNameFromMatchKey(entry.MatchKey))
            };

            for (int i = 0; i < keys.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(keys[i]) && string.Equals(keys[i], normalizedSprite, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryFindFallbackBagButton(out Button button)
        {
            button = null;

            try
            {
                GameObject statusPanel = GameObject.Find(STATUS_PANEL_PATH) ?? GameObject.Find("StatusPanel(Clone)");
                if (statusPanel != null)
                {
                    Button[] statusButtons = statusPanel.GetComponentsInChildren<Button>(true);
                    for (int i = 0; i < statusButtons.Length; i++)
                    {
                        Button candidate = statusButtons[i];
                        if (candidate == null || candidate.gameObject == null || !candidate.interactable)
                        {
                            continue;
                        }

                        string lower = (candidate.gameObject.name ?? string.Empty).ToLowerInvariant();
                        if (!lower.Contains("bag"))
                        {
                            continue;
                        }

                        button = candidate;
                        return true;
                    }
                }

                Button[] allButtons = Resources.FindObjectsOfTypeAll<Button>();
                for (int i = 0; i < allButtons.Length; i++)
                {
                    Button candidate = allButtons[i];
                    if (candidate == null || candidate.gameObject == null || !candidate.gameObject.activeInHierarchy || !candidate.interactable)
                    {
                        continue;
                    }

                    string path = this.GetTransformPath(candidate.transform).ToLowerInvariant();
                    if (path.Contains("statuspanel") && path.Contains("bag@"))
                    {
                        button = candidate;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

    }
}
