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
        // Token: 0x06000003 RID: 3 RVA: 0x0000206C File Offset: 0x0000026C
        private void ScanMeteorites()
        {
            // Finds all objects starting with p_rock_meteorite (handles 6, 7, 8 etc.)
            meteorList = GameObject.FindObjectsOfType<GameObject>()
            .Where(obj => obj != null && obj.activeInHierarchy && obj.name.StartsWith("p_rock_meteorite"))
            .ToList();
        }

        private bool TryOpenMeteorWeatherExchangeShop()
        {
            const string label = "Meteor / Starfall Exchange";
            return this.TryOpenWeatherExchangeShopPanelByStoreId(MeteorStarfallExchangeStoreId, 0, label);
        }

        private bool ShouldTrackMeteorObject(string lowerName)
        {
            return !string.IsNullOrEmpty(lowerName)
                && lowerName.StartsWith("p_rock_meteorite", StringComparison.Ordinal);
        }

        private bool ShouldRunMeteorAutoInteract()
        {
            if (this.auraFarmEnabled)
            {
                return false;
            }

            if (!this.autoFarmActive || !this.isRadarActive || !this.showMeteorRadar)
            {
                return false;
            }

            if (this.farmState != HeartopiaComplete.AutoFarmState.Collecting)
            {
                return false;
            }

            return this.IsMeteorNodePosition(this.lastNodePosition);
        }

        private bool IsMeteorNodePosition(Vector3 nodePosition)
        {
            if (nodePosition.sqrMagnitude < 0.01f)
            {
                return false;
            }

            if (this.radarContainer != null)
            {
                for (int i = 0; i < this.radarContainer.transform.childCount; i++)
                {
                    Transform child = this.radarContainer.transform.GetChild(i);
                    if (child == null)
                    {
                        continue;
                    }

                    string markerLabel = this.GetMarkerCanonicalLabel(child.gameObject);
                    if (string.IsNullOrEmpty(markerLabel) || !markerLabel.Contains("Meteor"))
                    {
                        continue;
                    }

                    if (Vector3.Distance(child.position, nodePosition) <= 3f)
                    {
                        return true;
                    }
                }
            }

            if (this.meteorList != null)
            {
                for (int i = this.meteorList.Count - 1; i >= 0; i--)
                {
                    GameObject meteor = this.meteorList[i];
                    if (meteor == null || !meteor.activeInHierarchy)
                    {
                        continue;
                    }

                    if (Vector3.Distance(meteor.transform.position, nodePosition) <= 3f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void StartMeteorAutoInteractSequence()
        {
            this.meteorAutoInteractActive = true;
            this.meteorAutoInteractClicksRemaining = 3;
            this.meteorAutoInteractTimer = meteorAutoInteractInterval;
        }

        private void UpdateMeteorAutoInteractSequence()
        {
            if (this.meteorAutoInteractClicksRemaining <= 0)
            {
                this.StopMeteorAutoInteractSequence();
                return;
            }

            this.meteorAutoInteractTimer += Time.unscaledDeltaTime;
            if (this.meteorAutoInteractTimer < meteorAutoInteractInterval)
            {
                return;
            }

            this.meteorAutoInteractTimer = 0f;
            this.autoCollectClickedSinceArrival = true;
            this.SimulateFKeyPulse(0.12f);
            this.DirectClickInteractButton();
            this.meteorAutoInteractClicksRemaining--;
        }

        private void StopMeteorAutoInteractSequence()
        {
            this.meteorAutoInteractActive = false;
            this.meteorAutoInteractClicksRemaining = 0;
            this.meteorAutoInteractTimer = 0f;
            HeartopiaComplete.SimulateFKeyHeld = false;
            HeartopiaComplete.SimulateFKeyDown = false;
            HeartopiaComplete.SimulateFKeyUp = false;
        }

    }
}
