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
        private float CalculateTeleportTabHeight()
        {
            // Teleport tab with various sub-tabs
            if (this.teleportSubTab == 0) return 620f;
            if (this.teleportSubTab == 1) return 80f + (this.animalCareLocations.Length * 45f);
            if (this.teleportSubTab == 2) return 110f + (Math.Max(1, this.cachedNpcTeleportEntries != null ? this.cachedNpcTeleportEntries.Count : 0) * 45f);
            if (this.teleportSubTab == 3) return 80f + (this.fastTravelLocations.Count * 45f);
            if (this.teleportSubTab == 4) return 80f + (this.eventLocations.Count * 45f);
            if (this.teleportSubTab == 5) return 80f + (this.houseLocations.Length * 45f);
            if (this.teleportSubTab == 6) return 180f + (this.customTeleportList.Count * 38f);
            if (this.teleportSubTab == 7) return 180f;
            return 420f; // XYZ fallback
        }

        public void TeleportToLocationWithOffset(Vector3 targetPos, float offset)
        {
            GameObject p = GetPlayer();
            Vector3 final = targetPos;
            if (p != null)
            {
                Vector3 forward = p.transform.forward;
                final = targetPos - forward * offset;
            }
            this.TeleportToLocation(final);
        }

        public void TeleportDirectToLocation(Vector3 targetPos)
        {
            this.TeleportToLocation(targetPos);
            this.teleportFramesRemaining = Math.Min(this.teleportFramesRemaining, 10);
        }

        private void SetTeleportSubTab(int subTab)
        {
            if (this.teleportSubTab != subTab)
            {
                this.teleportSubTab = subTab;
                this.tabScrollPos = Vector2.zero;
                this.fastTravelScrollPosition = Vector2.zero;
            }
        }

        public void OnTeleportArrivedResource()
        {
            this.isResourceFarmTeleport = false;
            this.resourceJustArrived = true;
            this.resourceArrivalTime = Time.unscaledTime;
            ModLogger.Msg($"[ResourceFarm] Arrived! Will press F for {this.resourceClickDuration}s (after {this.resourceArrivalDelay}s delay)");
        }

        private void TeleportToNextResource()
        {
            if (this.resourceMarkerPositions.Count == 0) return;
            if (OverridePlayerPosition) return;
            GameObject p = this.FindPlayerRoot();
            if (p == null)
            {
                ModLogger.Warning("[ResourceFarm] Cannot find player!");
                return;
            }
            Vector3 pos = p.transform.position;
            if (!this.hasResourceStartPosition)
            {
                this.resourceStartPosition = pos;
                this.hasResourceStartPosition = true;
            }
            Vector3 targetPos;
            if (this.isResourceReturningToStart)
            {
                targetPos = this.resourceStartPosition;
                this.isResourceReturningToStart = false;
                this.visitedResourceMarkerIndices.Clear();
                this.resourceMarkersNeedShuffle = true;
                this.currentResourceMarkerIndex = -1;
                ModLogger.Msg("[ResourceFarm] Returning to start position...");
            }
            else
            {
                List<int> notVisited = new List<int>();
                for (int i=0;i<this.resourceMarkerPositions.Count;i++) if (!this.visitedResourceMarkerIndices.Contains(i)) notVisited.Add(i);
                if (notVisited.Count == 0)
                {
                    this.isResourceReturningToStart = true;
                    this.visitedResourceMarkerIndices.Clear();
                    this.resourceMarkersNeedShuffle = true;
                    this.currentResourceMarkerIndex = -1;
                    ModLogger.Msg($"[ResourceFarm] All {this.resourceMarkerPositions.Count} resources visited! Returning to start...");
                    return;
                }
                int idx = this.instanceRng.Next(0, notVisited.Count);
                int chosen = notVisited[idx];
                this.visitedResourceMarkerIndices.Add(chosen);
                this.currentResourceMarkerIndex = chosen;
                targetPos = this.resourceMarkerPositions[chosen];
                ModLogger.Msg($"[ResourceFarm] Teleporting to resource {this.visitedResourceMarkerIndices.Count}/{this.resourceMarkerPositions.Count} (index:{chosen})");
            }

            this.isResourceFarmTeleport = true;
            this.TeleportToLocation(targetPos);
            this.teleportFramesRemaining = 10;
        }

        // Token: 0x06000021 RID: 33 RVA: 0x000062EC File Offset: 0x000044EC
        private float DrawTeleportTab(int startY)
        {
            int num = startY;

            // Home tab: keep all home controls isolated here.
            if (this.teleportSubTab == 0)
            {
                this.RefreshAutoHomePosition();
                GameObject playerObj = GameObject.Find("p_player_skeleton(Clone)");
                GUI.Label(new Rect(20f, (float)num, 260f, 20f), "Home Position");
                num += 25;
                GUI.enabled = (this.homePositionSet || this.autoHomePositionValid);
                if (this.DrawPrimaryActionButton(new Rect(20f, (float)num, 125f, 35f), "TP Home"))
                {
                    this.TeleportToHome();
                }
                GUI.enabled = true;
                num += 45;

                if (this.homePositionSet && !this.autoHomePositionValid)
                {
                    GUI.color = Color.green;
                    GUI.Label(new Rect(20f, (float)num, 340f, 20f), $"Home Set: ({this.homePosition.x:F1}, {this.homePosition.y:F1}, {this.homePosition.z:F1})");
                }
                GUI.color = Color.white;
                if (this.homePositionSet && !this.autoHomePositionValid)
                {
                    num += 25;
                }

                if (playerObj == null && !this.autoHomePositionValid)
                {
                    GUI.color = Color.yellow;
                    GUI.Label(new Rect(20f, (float)num, 340f, 20f), "Not Ready");
                }
                else if (!string.IsNullOrEmpty(this.autoHomeStatus))
                {
                    GUI.color = this.autoHomePositionValid ? new Color(0.45f, 1f, 1f) : Color.yellow;
                    GUI.Label(new Rect(20f, (float)num, 340f, 20f), this.autoHomeStatus);
                }
                else if (HeartopiaComplete.OverridePlayerPosition)
                {
                    GUI.color = Color.yellow;
                    GUI.Label(new Rect(20f, (float)num, 260f, 20f), "Teleporting...");
                }
                else
                {
                    GUI.color = Color.green;
                    GUI.Label(new Rect(20f, (float)num, 260f, 20f), "Ready to Travel");
                }
                GUI.color = Color.white;
                num += 30;

                if (playerObj != null)
                {
                    Vector3 p = playerObj.transform.position;

                    // Copy position button: copies to clipboard and populates XYZ inputs
                    if (GUI.Button(new Rect(20f, (float)num, 140f, 28f), "Copy Position"))
                    {
                        GUIUtility.systemCopyBuffer = string.Format("{0:F3},{1:F3},{2:F3}", p.x, p.y, p.z);
                        this.customTPX = p.x.ToString("F3");
                        this.customTPY = p.y.ToString("F3");
                        this.customTPZ = p.z.ToString("F3");
                        this.AddMenuNotification("Current position copied to clipboard and XYZ fields", new Color(0.55f, 0.88f, 1f));
                    }

                    num += 34;
                    GUI.Label(new Rect(20f, (float)num, 340f, 40f), $"Current Position:\n({p.x:F1}, {p.y:F1}, {p.z:F1})");
                }
                else
                {
                    GUI.Label(new Rect(20f, (float)num, 340f, 40f), "Current Position:\nPlayer not found");
                }
                return (float)num + 60f;
            }

            float panelX = 20f;
            float panelY = (float)num;
            float listButtonWidth = 440f;

            if (this.teleportSubTab == 1)
            {
                int y = num;
                for (int animalIdx = 0; animalIdx < this.animalCareLocations.Length; animalIdx++)
                {
                    Vector3 animalPos = this.animalCareLocations[animalIdx];
                    string animalLabel = animalIdx < this.animalCareLocationNames.Length && !string.IsNullOrWhiteSpace(this.animalCareLocationNames[animalIdx])
                        ? this.animalCareLocationNames[animalIdx]
                        : string.Format("Animal Care #{0}", animalIdx + 1);
                    if (GUI.Button(new Rect(panelX, (float)y, listButtonWidth, 40f), string.Format("{0}\n({1:F0}, {2:F0}, {3:F0})", animalLabel, animalPos.x, animalPos.y, animalPos.z)))
                    {
                        this.TeleportToLocation(animalPos);
                    }
                    y += 45;
                }
                return y + 20f;
            }

            if (this.teleportSubTab == 3)
            {
                int y = num;
                foreach (KeyValuePair<string, Vector3> keyValuePair in this.fastTravelLocations)
                {
                    if (GUI.Button(new Rect(panelX, (float)y, listButtonWidth, 40f), $"{keyValuePair.Key}\n({keyValuePair.Value.x:F0}, {keyValuePair.Value.y:F0}, {keyValuePair.Value.z:F0})"))
                    {
                        this.TeleportToLocation(keyValuePair.Value);
                    }
                    y += 45;
                }
                return y + 20f;
            }

            if (this.teleportSubTab == 2)
            {
                int y = num;
                List<KeyValuePair<string, Vector3>> npcEntries = this.GetTeleportNpcEntries(false);
                GUI.color = npcEntries.Count > 0 ? new Color(0.45f, 1f, 1f) : Color.yellow;
                GUI.Label(new Rect(panelX, (float)y, listButtonWidth, 22f), this.npcTeleportStatus);
                GUI.color = Color.white;
                y += 28;

                if (GUI.Button(new Rect(panelX, (float)y, 120f, 26f), "Refresh NPCs"))
                {
                    npcEntries = this.GetTeleportNpcEntries(true);
                }
                GUI.Label(new Rect(panelX + 132f, (float)y + 4f, 48f, 20f), "Search");
                this.npcTeleportSearchText = GUI.TextField(new Rect(panelX + 184f, (float)y, listButtonWidth - 184f, 26f), this.npcTeleportSearchText ?? "", 40);
                y += 34;

                string npcSearch = this.NormalizeNpcTeleportName(this.npcTeleportSearchText);
                if (!string.IsNullOrWhiteSpace(npcSearch))
                {
                    npcEntries = npcEntries
                        .Where(entry => this.NormalizeNpcTeleportName(entry.Key).Contains(npcSearch))
                        .ToList();
                }

                if (npcEntries.Count == 0)
                {
                    IEnumerable<string> preferredNames = this.GetNpcTeleportPreferredNames();
                    if (!string.IsNullOrWhiteSpace(npcSearch))
                    {
                        preferredNames = preferredNames.Where(name => this.NormalizeNpcTeleportName(name).Contains(npcSearch));
                    }
                    foreach (string npcName in preferredNames)
                    {
                        GUI.enabled = false;
                        string reason = string.IsNullOrWhiteSpace(this.npcTeleportSearchText) && (this.cachedNpcTeleportEntries == null || this.cachedNpcTeleportEntries.Count == 0)
                            ? "Press Refresh NPCs"
                            : "Live location unavailable";
                        GUI.Button(new Rect(panelX, (float)y, listButtonWidth, 40f), npcName + "\n" + reason);
                        GUI.enabled = true;
                        y += 45;
                    }
                    return y + 20f;
                }

                foreach (KeyValuePair<string, Vector3> keyValuePair2 in npcEntries)
                {
                    if (GUI.Button(new Rect(panelX, (float)y, listButtonWidth, 40f), keyValuePair2.Key))
                    {
                        this.TeleportToLocation(keyValuePair2.Value);
                    }
                    y += 45;
                }
                return y + 20f;
            }

            if (this.teleportSubTab == 4)
            {
                int y = num;
                foreach (KeyValuePair<string, Vector3> keyValuePair3 in this.eventLocations)
                {
                    if (GUI.Button(new Rect(panelX, (float)y, listButtonWidth, 40f), $"{keyValuePair3.Key}\n({keyValuePair3.Value.x:F0}, {keyValuePair3.Value.y:F0}, {keyValuePair3.Value.z:F0})"))
                    {
                        this.TeleportToLocation(keyValuePair3.Value);
                    }
                    y += 45;
                }
                return y + 20f;
            }

            if (this.teleportSubTab == 5)
            {
                int y = num;
                for (int houseIdx = 0; houseIdx < this.houseLocations.Length; houseIdx++)
                {
                    Vector3 housePos = this.houseLocations[houseIdx];
                    if (GUI.Button(new Rect(panelX, (float)y, listButtonWidth, 40f), string.Format("House Slot #{0}\n({1:F0}, {2:F0}, {3:F0})", houseIdx + 1, housePos.x, housePos.y, housePos.z)))
                    {
                        this.TeleportToLocation(housePos);
                    }
                    y += 45;
                }
                return y + 20f;
            }

            if (this.teleportSubTab == 6)
            {
                GUI.Label(new Rect(panelX, panelY + 8f, 40f, 25f), "Name:");
                this.customTeleportName = GUI.TextField(new Rect(panelX + 45f, panelY + 8f, 160f, 25f), this.customTeleportName);
                if (GUI.Button(new Rect(panelX + 212f, panelY + 8f, 70f, 25f), "Save"))
                {
                    GameObject player = GameObject.Find("p_player_skeleton(Clone)");
                    if (player != null)
                    {
                        this.customTeleportList.Add(new CustomTeleportEntry { name = this.customTeleportName, position = player.transform.position });
                        this.SaveCustomTeleports();
                    }
                }

                int y = (int)(panelY + 46f);
                if (this.customTeleportList.Count > 0)
                {
                    GUI.Label(new Rect(panelX, (float)y, 260f, 22f), "Saved Teleports");
                    y += 24;
                    for (int i = 0; i < this.customTeleportList.Count; ++i)
                    {
                        var entry = this.customTeleportList[i];
                        if (GUI.Button(new Rect(panelX, (float)y, 320f, 32f), entry.name))
                        {
                            this.TeleportToLocation(entry.position);
                        }
                        if (this.DrawDangerActionButton(new Rect(panelX + 328f, (float)y, 70f, 32f), "DEL"))
                        {
                            this.customTeleportList.RemoveAt(i);
                            this.SaveCustomTeleports();
                            break;
                        }
                        y += 38;
                    }
                }
                return y + 20f;
            }

            if (this.teleportSubTab == 7)
            {
                GUI.Label(new Rect(panelX, panelY + 8f, 260f, 24f), "Direct XYZ Teleport");
                GUI.Label(new Rect(panelX, panelY + 40f, 18f, 25f), "X:");
                this.customTPX = GUI.TextField(new Rect(panelX + 18f, panelY + 40f, 90f, 25f), this.customTPX);
                GUI.Label(new Rect(panelX + 118f, panelY + 40f, 18f, 25f), "Y:");
                this.customTPY = GUI.TextField(new Rect(panelX + 136f, panelY + 40f, 90f, 25f), this.customTPY);
                GUI.Label(new Rect(panelX + 236f, panelY + 40f, 18f, 25f), "Z:");
                this.customTPZ = GUI.TextField(new Rect(panelX + 254f, panelY + 40f, 90f, 25f), this.customTPZ);

                if (GUI.Button(new Rect(panelX, panelY + 78f, 344f, 32f), "Teleport to XYZ"))
                {
                    if (float.TryParse(this.customTPX, out float x) &&
                        float.TryParse(this.customTPY, out float y) &&
                        float.TryParse(this.customTPZ, out float z))
                    {
                        this.TeleportToLocation(new Vector3(x, y, z));
                    }
                }
                return panelY + 130f;
            }

            return panelY + 20f;
        }

        // Token: 0x06000023 RID: 35 RVA: 0x00006C80 File Offset: 0x00004E80
        private void TeleportToHome()
        {
            // Always resolve current home on demand so town switches do not reuse
            // a stale cached home position from the last room.
            this.RefreshAutoHomePosition(true);

            if (this.autoHomePositionValid)
            {
                this.TeleportToLocation(this.autoHomePosition);
                this.autoHomeStatus = "Home Ready";
                ModLogger.Msg($"[HOME] Teleported to auto home [{this.autoHomeNetId}]: {this.autoHomePosition}");
            }
            else if (this.homePositionSet)
            {
                this.TeleportToLocation(this.homePosition);
                this.autoHomeStatus = "Teleported to manual home";
                ModLogger.Msg($"[HOME] Teleported to home: {this.homePosition}");
            }
            else
            {
                ModLogger.Msg("[HOME] Home position not set!");
                this.autoHomeStatus = "Auto home unavailable";
            }
        }

        private string NormalizeNpcTeleportName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }
            char[] array = name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
            return new string(array);
        }

        private IEnumerable<string> GetNpcTeleportLookupKeys(string name)
        {
            HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(name))
            {
                return hashSet;
            }

            Action<string> action = delegate (string candidate)
            {
                string text = this.NormalizeNpcTeleportName(candidate);
                if (!string.IsNullOrEmpty(text))
                {
                    hashSet.Add(text);
                }
            };

            action(name);
            int num = name.IndexOf('(');
            if (num > 0)
            {
                action(name.Substring(0, num).Trim());
            }

            action(name.Replace("Mrs.", string.Empty).Replace("Mr.", string.Empty).Trim());
            action(name.Replace("Baily", "Bailey"));
            action(name.Replace("Mrs.Joan", "Joan"));
            action(name.Replace("Massimo (Town)", "Massimo"));
            action(name.Replace("Ka Ching", "Kaching"));

            return hashSet;
        }

        private bool TryResolveNpcTeleportId(string label, Dictionary<string, int> idMap, out int npcId)
        {
            npcId = 0;
            if (idMap == null || idMap.Count == 0)
            {
                return false;
            }

            foreach (string key in this.GetNpcTeleportLookupKeys(label))
            {
                if (idMap.TryGetValue(key, out npcId))
                {
                    return true;
                }
            }

            return false;
        }

        private List<KeyValuePair<string, int>> GetNpcTeleportCandidates(Dictionary<string, int> idMap)
        {
            List<KeyValuePair<string, int>> candidates = new List<KeyValuePair<string, int>>();
            HashSet<int> seenIds = new HashSet<int>();
            if (idMap == null || idMap.Count == 0)
            {
                return candidates;
            }

            foreach (string preferredName in this.GetNpcTeleportPreferredNames())
            {
                int npcId;
                if (this.TryResolveNpcTeleportId(preferredName, idMap, out npcId) && seenIds.Add(npcId))
                {
                    candidates.Add(new KeyValuePair<string, int>(preferredName, npcId));
                }
            }

            if (this.cachedNpcTeleportIdNames != null && this.cachedNpcTeleportIdNames.Count > 0)
            {
                List<KeyValuePair<int, string>> discovered = this.cachedNpcTeleportIdNames.ToList();
                discovered.Sort((left, right) => string.Compare(left.Value, right.Value, StringComparison.OrdinalIgnoreCase));
                foreach (KeyValuePair<int, string> entry in discovered)
                {
                    if (entry.Key > 0 && seenIds.Add(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                    {
                        candidates.Add(new KeyValuePair<string, int>(entry.Value.Trim(), entry.Key));
                    }
                }
            }
            else
            {
                foreach (KeyValuePair<string, int> entry in idMap.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (entry.Value > 0 && seenIds.Add(entry.Value))
                    {
                        candidates.Add(new KeyValuePair<string, int>(entry.Key, entry.Value));
                    }
                }
            }

            return candidates;
        }

        private IEnumerable<string> GetNpcTeleportPreferredNames()
        {
            return this.npcTeleportPreferredNames ?? Array.Empty<string>();
        }

        private List<KeyValuePair<string, int>> GetNpcTeleportTableEntries()
        {
            List<KeyValuePair<string, int>> entries = new List<KeyValuePair<string, int>>();
            HashSet<int> seenIds = new HashSet<int>();
            try
            {
                Type type = this.FindLoadedType("TableData", "EcsClient.TableData");
                if (type == null)
                {
                    this.npcTeleportIdResolveStatus = "Managed TableData not found";
                    return entries;
                }

                object value = type.GetField("TableNpcs", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (value == null)
                {
                    this.npcTeleportIdResolveStatus = "TableNpcs unavailable";
                    return entries;
                }

                IDictionary dictionary = value as IDictionary;
                if (dictionary != null)
                {
                    foreach (DictionaryEntry dictionaryEntry in dictionary)
                    {
                        this.TryAddNpcTeleportTableEntry(entries, seenIds, dictionaryEntry.Key, dictionaryEntry.Value);
                    }
                }
                else if (value is IEnumerable enumerable)
                {
                    foreach (object item in enumerable)
                    {
                        if (item == null)
                        {
                            continue;
                        }

                        object keyObj;
                        object valueObj;
                        if (this.TryGetObjectMember(item, "Key", out keyObj) && this.TryGetObjectMember(item, "Value", out valueObj))
                        {
                            this.TryAddNpcTeleportTableEntry(entries, seenIds, keyObj, valueObj);
                        }
                    }
                }

                entries.Sort((left, right) => string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase));
                this.npcTeleportIdResolveStatus = entries.Count > 0 ? "NPC table loaded" : "NPC table empty/unreadable";
            }
            catch (Exception ex)
            {
                this.npcTeleportIdResolveStatus = "NPC table error: " + ex.Message;
            }

            return entries;
        }

        private bool TryAddNpcTeleportTableEntry(List<KeyValuePair<string, int>> entries, HashSet<int> seenIds, object keyObj, object tableNpcObj)
        {
            if (entries == null || seenIds == null || tableNpcObj == null)
            {
                return false;
            }

            int npcId;
            try
            {
                npcId = Convert.ToInt32(keyObj);
            }
            catch
            {
                object idObj;
                if (!this.TryGetObjectMember(tableNpcObj, "id", out idObj))
                {
                    return false;
                }

                try
                {
                    npcId = Convert.ToInt32(idObj);
                }
                catch
                {
                    return false;
                }
            }

            if (npcId <= 0 || !seenIds.Add(npcId))
            {
                return false;
            }

            object showInMapObj;
            if (this.TryGetObjectMember(tableNpcObj, "showInMap", out showInMapObj) && showInMapObj is bool && !(bool)showInMapObj)
            {
                return false;
            }

            object nameObj;
            string name = null;
            if (this.TryGetObjectMember(tableNpcObj, "name", out nameObj) && nameObj is string)
            {
                name = ((string)nameObj).Trim();
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = "NPC " + npcId;
            }

            entries.Add(new KeyValuePair<string, int>(name, npcId));
            return true;
        }

        private bool TryGetNpcTeleportPosition(object npcComponent, out Vector3 position)
        {
            position = Vector3.zero;
            object obj;
            if (this.TryGetObjectMember(npcComponent, "position", out obj) && obj is Vector3)
            {
                position = (Vector3)obj;
                return true;
            }
            object obj2;
            if (this.TryGetObjectMember(npcComponent, "entity", out obj2))
            {
                object obj3;
                if (this.TryGetObjectMember(obj2, "position", out obj3) && obj3 is Vector3)
                {
                    position = (Vector3)obj3;
                    return true;
                }
                object obj4;
                if (this.TryGetObjectMember(obj2, "transform", out obj4) && this.TryExtractHomePosition(obj4, out position))
                {
                    return true;
                }
            }
            object obj5;
            if (this.TryGetObjectMember(npcComponent, "transform", out obj5) && this.TryExtractHomePosition(obj5, out position))
            {
                return true;
            }
            return false;
        }

        private void AddOrUpdateNpcTeleportEntry(List<KeyValuePair<string, Vector3>> entries, Dictionary<string, int> indexByName, string rawName, Vector3 position)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return;
            }

            string text = rawName.Trim();
            string key = this.NormalizeNpcTeleportName(text);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            int num;
            if (indexByName.TryGetValue(key, out num))
            {
                entries[num] = new KeyValuePair<string, Vector3>(entries[num].Key, position);
                return;
            }

            indexByName[key] = entries.Count;
            entries.Add(new KeyValuePair<string, Vector3>(text, position));
        }

        private Dictionary<string, int> GetNpcTeleportIdMap()
        {
            if (this.npcTeleportIdCacheReady && this.cachedNpcTeleportIds != null && this.cachedNpcTeleportIds.Count > 0)
            {
                this.LogNpcTeleportDebug("GetNpcTeleportIdMap: using cached ids count=" + this.cachedNpcTeleportIds.Count);
                return this.cachedNpcTeleportIds;
            }
            if (this.npcTeleportIdCacheReady && this.cachedNpcTeleportIds != null && this.cachedNpcTeleportIds.Count == 0 && Time.unscaledTime < this.nextNpcTeleportIdRetryTime)
            {
                this.LogNpcTeleportDebug("GetNpcTeleportIdMap: using cached empty ids until retry, status=" + this.npcTeleportIdResolveStatus);
                return this.cachedNpcTeleportIds;
            }

            Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Type type = this.FindLoadedType("TableData", "EcsClient.TableData");
            if (type == null)
            {
                Dictionary<string, int> monoDictionary = this.BuildNpcTeleportIdMapMono();
                this.cachedNpcTeleportIds = monoDictionary;
                this.npcTeleportIdCacheReady = true;
                this.nextNpcTeleportIdRetryTime = Time.unscaledTime + (monoDictionary.Count > 0 ? 30f : 5f);
                return this.cachedNpcTeleportIds;
            }

            try
            {
                Dictionary<int, string> idNames = new Dictionary<int, string>();
                object value = type.GetField("TableNpcs", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                IDictionary dictionary2 = value as IDictionary;
                if (dictionary2 != null)
                {
                    foreach (DictionaryEntry dictionaryEntry in dictionary2)
                    {
                        int num;
                        try
                        {
                            num = Convert.ToInt32(dictionaryEntry.Key);
                        }
                        catch
                        {
                            continue;
                        }

                        object value2 = dictionaryEntry.Value;
                        if (value2 == null)
                        {
                            continue;
                        }

                        object obj;
                        if (!this.TryGetObjectMember(value2, "name", out obj) || !(obj is string) || string.IsNullOrWhiteSpace((string)obj))
                        {
                            continue;
                        }

                        string key = this.NormalizeNpcTeleportName((string)obj);
                        if (!string.IsNullOrEmpty(key) && !dictionary.ContainsKey(key))
                        {
                            dictionary[key] = num;
                            idNames[num] = ((string)obj).Trim();
                        }
                    }
                }
                this.cachedNpcTeleportIdNames = idNames;
            }
            catch
            {
            }

            this.LogNpcTeleportDebug("Managed NPC id map count=" + dictionary.Count);
            if (dictionary.Count == 0)
            {
                Dictionary<string, int> monoDictionary = this.BuildNpcTeleportIdMapMono();
                if (monoDictionary.Count > 0)
                {
                    dictionary = monoDictionary;
                }
            }

            this.cachedNpcTeleportIds = dictionary;
            this.npcTeleportIdCacheReady = true;
            this.nextNpcTeleportIdRetryTime = (dictionary.Count > 0) ? (Time.unscaledTime + 30f) : (Time.unscaledTime + 5f);
            this.LogNpcTeleportDebug("Final NPC id map count=" + this.cachedNpcTeleportIds.Count + " status=" + this.npcTeleportIdResolveStatus);
            return this.cachedNpcTeleportIds;
        }

        private unsafe Dictionary<string, int> BuildNpcTeleportIdMapMono()
        {
            Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<int, string> idNames = new Dictionary<int, string>();
            this.LogNpcTeleportDebug("BuildNpcTeleportIdMapMono: reading TableData.TableNpcs", true);
            HashSet<string> targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string text in this.GetNpcTeleportPreferredNames())
            {
                foreach (string key in this.GetNpcTeleportLookupKeys(text))
                {
                    targets.Add(key);
                }
            }
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                this.npcTeleportIdResolveStatus = "Mono API not ready";
                this.LogNpcTeleportDebug(this.npcTeleportIdResolveStatus, true);
                return dictionary;
            }

            IntPtr ecsImage = this.FindAuraMonoImage(new string[]
            {
                "EcsClient",
                "EcsClient.dll"
            });
            if (ecsImage == IntPtr.Zero)
            {
                this.npcTeleportIdResolveStatus = "EcsClient image not found";
                this.LogNpcTeleportDebug(this.npcTeleportIdResolveStatus, true);
                return dictionary;
            }
            IntPtr tableDataClass = ecsImage != IntPtr.Zero ? auraMonoClassFromName(ecsImage, string.Empty, "TableData") : IntPtr.Zero;
            if (tableDataClass == IntPtr.Zero && ecsImage != IntPtr.Zero)
            {
                tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
            }
            if (tableDataClass == IntPtr.Zero)
            {
                this.npcTeleportIdResolveStatus = "TableData mono class not found";
                this.LogNpcTeleportDebug(this.npcTeleportIdResolveStatus, true);
                return dictionary;
            }

            IntPtr tableNpcsObj;
            if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableNpcs", out tableNpcsObj) || tableNpcsObj == IntPtr.Zero)
            {
                this.npcTeleportIdResolveStatus = "TableData.TableNpcs mono field unavailable";
                this.LogNpcTeleportDebug(this.npcTeleportIdResolveStatus, true);
                return dictionary;
            }

            int hits = 0;
            int inspected = 0;
            List<string> samples = new List<string>();
            List<IntPtr> items = new List<IntPtr>();
            if (this.TryEnumerateAuraMonoCollectionItems(tableNpcsObj, items))
            {
                foreach (IntPtr itemObj in items)
                {
                    inspected++;
                    int npcId;
                    string name;
                    if (!this.TryReadNpcTableEntryMono(itemObj, out npcId, out name))
                    {
                        continue;
                    }

                    string key = this.NormalizeNpcTeleportName(name);
                    if (!string.IsNullOrEmpty(key) && !dictionary.ContainsKey(key))
                    {
                        dictionary[key] = npcId;
                        idNames[npcId] = name.Trim();
                        hits++;
                        if (samples.Count < 8)
                        {
                            samples.Add(npcId + ":" + name);
                        }
                        targets.Remove(key);
                        if (targets.Count == 0)
                        {
                            break;
                        }
                    }
                }
            }
            else
            {
                this.npcTeleportIdResolveStatus = "TableData.TableNpcs mono enumeration failed";
            }

            foreach (string preferredName in this.GetNpcTeleportPreferredNames())
            {
                foreach (string preferredKey in this.GetNpcTeleportLookupKeys(preferredName))
                {
                    int npcId;
                    if (!string.IsNullOrEmpty(preferredKey) && dictionary.TryGetValue(preferredKey, out npcId) && !dictionary.ContainsKey(this.NormalizeNpcTeleportName(preferredName)))
                    {
                        dictionary[this.NormalizeNpcTeleportName(preferredName)] = npcId;
                    }
                }
            }

            this.npcTeleportIdResolveStatus = dictionary.Count > 0
                ? string.Format("Mono NPC ids: {0} match(es)", hits)
                : (string.IsNullOrEmpty(this.npcTeleportIdResolveStatus) ? "Mono TableNpcs returned 0 matches" : this.npcTeleportIdResolveStatus);
            this.cachedNpcTeleportIdNames = idNames;
            this.LogNpcTeleportDebug("Mono TableNpcs scan: inspected=" + inspected + " matches=" + hits + " missing=" + targets.Count + (samples.Count > 0 ? " samples=" + string.Join(", ", samples) : string.Empty) + " status=" + this.npcTeleportIdResolveStatus, true);
            return dictionary;
        }

        private bool TryReadNpcTableEntryMono(IntPtr itemObj, out int npcId, out string name)
        {
            npcId = 0;
            name = string.Empty;
            if (itemObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr valueObj = IntPtr.Zero;
            IntPtr keyObj = IntPtr.Zero;
            bool hasValue = this.TryGetMonoObjectMember(itemObj, "Value", out valueObj)
                || this.TryGetMonoObjectMember(itemObj, "value", out valueObj)
                || this.TryGetMonoObjectMember(itemObj, "_value", out valueObj);
            bool hasKey = this.TryGetMonoObjectMember(itemObj, "Key", out keyObj)
                || this.TryGetMonoObjectMember(itemObj, "key", out keyObj)
                || this.TryGetMonoObjectMember(itemObj, "_key", out keyObj);

            if (!hasValue || valueObj == IntPtr.Zero)
            {
                valueObj = itemObj;
            }

            if (hasKey && keyObj != IntPtr.Zero)
            {
                this.TryUnboxMonoInt32(keyObj, out npcId);
            }

            if (npcId <= 0)
            {
                this.TryGetMonoInt32Member(valueObj, "id", out npcId);
                if (npcId <= 0)
                {
                    this.TryGetMonoInt32Member(valueObj, "Id", out npcId);
                }
            }

            return npcId > 0 && this.TryGetMonoStringMember(valueObj, "name", out name) && !string.IsNullOrWhiteSpace(name);
        }

        private void LogNpcTeleportDebug(string message, bool force = false)
        {
            if (!NpcTeleportDebugLogsEnabled)
            {
                return;
            }

            if (!force && Time.unscaledTime < this.nextNpcTeleportDebugLogTime)
            {
                return;
            }
            this.nextNpcTeleportDebugLogTime = Time.unscaledTime + 3f;
            ModLogger.Msg("[NpcTeleport] " + message);
        }

        private bool TryGetNpcNetIdViaClientService(int npcId, out uint netId)
        {
            netId = 0U;
            try
            {
                Type ecsServiceType = this.FindLoadedType("XDTDataAndProtocol.ProtocolService.EcsService", "EcsService");
                Type npcClientServiceType = this.FindLoadedType(
                    "ClientSystem.Npc.NpcClientService",
                    "XDTDataAndProtocol.ProtocolService.Npc.INpcClientService",
                    "NpcClientService",
                    "INpcClientService");
                if (ecsServiceType == null || npcClientServiceType == null)
                {
                    return false;
                }

                MethodInfo tryGetMethod = ecsServiceType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "TryGet" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                if (tryGetMethod == null)
                {
                    return false;
                }

                object[] serviceArgs = new object[] { null, false };
                object serviceResult = tryGetMethod.MakeGenericMethod(npcClientServiceType).Invoke(null, serviceArgs);
                if (!(serviceResult is bool) || !(bool)serviceResult || serviceArgs[0] == null)
                {
                    return false;
                }

                object npcService = serviceArgs[0];
                MethodInfo tryGetNetIdMethod = npcService.GetType().GetMethod("TryGetNpcNetId", BindingFlags.Public | BindingFlags.Instance);
                if (tryGetNetIdMethod != null)
                {
                    object[] netIdArgs = new object[] { npcId, 0U };
                    object netIdResult = tryGetNetIdMethod.Invoke(npcService, netIdArgs);
                    if (netIdResult is bool && (bool)netIdResult && this.TryConvertToUInt(netIdArgs[1], out netId) && netId != 0U)
                    {
                        return true;
                    }
                }

                MethodInfo tryGetEntityMethod = npcService.GetType().GetMethod("TryGetNpcEntity", BindingFlags.Public | BindingFlags.Instance);
                if (tryGetEntityMethod == null)
                {
                    return false;
                }

                ParameterInfo[] parameters = tryGetEntityMethod.GetParameters();
                if (parameters.Length != 2 || !parameters[1].ParameterType.IsByRef)
                {
                    return false;
                }

                Type ecsEntityType = parameters[1].ParameterType.GetElementType();
                object[] entityArgs = new object[] { npcId, Activator.CreateInstance(ecsEntityType) };
                object entityResult = tryGetEntityMethod.Invoke(npcService, entityArgs);
                if (!(entityResult is bool) || !(bool)entityResult || entityArgs[1] == null)
                {
                    return false;
                }

                Type ecsEntityExtensionType = this.FindLoadedType("XD.GameGerm.Ecs.Boost.Extensions.EcsEntityExtensions", "EcsEntityExtensions");
                MethodInfo getNetIdMethod = ecsEntityExtensionType != null
                    ? ecsEntityExtensionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "GetNetId" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == ecsEntityType)
                    : null;
                if (getNetIdMethod == null)
                {
                    return false;
                }

                object value = getNetIdMethod.Invoke(null, new object[] { entityArgs[1] });
                return this.TryConvertToUInt(value, out netId) && netId != 0U;
            }
            catch
            {
                netId = 0U;
                return false;
            }
        }

        private bool TryGetLiveNpcPositionById(int npcId, out Vector3 position)
        {
            position = Vector3.zero;
            uint netId;
            if (this.TryGetNpcNetIdViaClientService(npcId, out netId) && this.TryGetEntityPositionByNetId(netId, out position))
            {
                return true;
            }
            return false;
        }

        private unsafe bool TryGetLiveNpcPositionByIdMono(int npcId, out Vector3 position)
        {
            position = Vector3.zero;
            if (npcId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr dataImage = this.FindAuraMonoImage(new string[]
            {
                "XDTDataAndProtocol",
                "XDTDataAndProtocol.dll"
            });
            IntPtr protocolClass = dataImage != IntPtr.Zero ? auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.MapSpot", "MapSpotProtocolManager") : IntPtr.Zero;
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.MapSpot", "MapSpotProtocolManager");
            }
            if (protocolClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr methodPtr = auraMonoClassGetMethodFromName(protocolClass, "TryGetMapSpotPosition", 3);
            if (methodPtr == IntPtr.Zero)
            {
                return false;
            }

            int npcSpotEnum = 2;
            Vector3 resolvedPosition = Vector3.zero;
            IntPtr exc = IntPtr.Zero;
            IntPtr boxedResult;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&npcSpotEnum);
            args[1] = (IntPtr)(&npcId);
            args[2] = (IntPtr)(&resolvedPosition);
            boxedResult = auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxedResult == IntPtr.Zero || !this.TryUnboxMonoBoolean(boxedResult, out bool ok) || !ok)
            {
                return false;
            }

            position = resolvedPosition;
            return position != Vector3.zero;
        }

        private bool IsNpcEntityComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            string simpleName = typeName;
            int lastDot = simpleName.LastIndexOf('.');
            if (lastDot >= 0 && lastDot + 1 < simpleName.Length)
            {
                simpleName = simpleName.Substring(lastDot + 1);
            }

            return string.Equals(simpleName, "NpcComponent", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryReadNpcTeleportIntMember(object obj, out int value, params string[] memberNames)
        {
            value = 0;
            if (obj == null || memberNames == null)
            {
                return false;
            }

            foreach (string memberName in memberNames)
            {
                try
                {
                    object raw;
                    if (this.TryGetObjectMember(obj, memberName, out raw) && raw != null)
                    {
                        value = Convert.ToInt32(raw);
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private bool TryReadNpcTeleportStringMember(object obj, out string value, params string[] memberNames)
        {
            value = string.Empty;
            if (obj == null || memberNames == null)
            {
                return false;
            }

            foreach (string memberName in memberNames)
            {
                try
                {
                    object raw;
                    if (this.TryGetObjectMember(obj, memberName, out raw) && raw != null)
                    {
                        string text = raw.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            value = text.Trim();
                            return true;
                        }
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private bool TryResolveNpcStaticIdFromComponent(object npcComponent, out int staticId)
        {
            staticId = 0;
            if (npcComponent == null)
            {
                return false;
            }

            if (this.TryReadNpcTeleportIntMember(npcComponent, out staticId, "staticId", "StaticId", "_staticId", "npcId", "NpcId", "id", "Id") && staticId > 0)
            {
                return true;
            }

            try
            {
                object componentData;
                if ((this.TryGetObjectMember(npcComponent, "ComponentData", out componentData) || this.TryInvokeZeroArgMember(npcComponent, out componentData, "get_ComponentData")) && componentData != null)
                {
                    return this.TryReadNpcTeleportIntMember(componentData, out staticId, "staticId", "StaticId", "npcId", "NpcId", "id", "Id") && staticId > 0;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryResolveNpcNameFromComponent(object npcComponent, out string npcName)
        {
            npcName = string.Empty;
            if (npcComponent == null)
            {
                return false;
            }

            if (this.TryReadNpcTeleportStringMember(npcComponent, out npcName, "npcName", "NpcName", "displayName", "DisplayName", "name", "Name") && !string.IsNullOrWhiteSpace(npcName))
            {
                return true;
            }

            try
            {
                object value;
                if (this.TryInvokeZeroArgMember(npcComponent, out value, "get_npcName", "GetNpcName", "get_Name", "GetName") && value != null)
                {
                    string text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        npcName = text.Trim();
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private string ResolvePreferredNpcTeleportLabel(string liveNpcName)
        {
            foreach (string preferredName in this.GetNpcTeleportPreferredNames())
            {
                foreach (string key in this.GetNpcTeleportLookupKeys(preferredName))
                {
                    foreach (string liveKey in this.GetNpcTeleportLookupKeys(liveNpcName))
                    {
                        if (string.Equals(key, liveKey, StringComparison.OrdinalIgnoreCase))
                        {
                            return preferredName;
                        }
                    }
                }
            }

            return liveNpcName;
        }

        private int PopulateLiveNpcEntriesFromUnityObjects(List<KeyValuePair<string, Vector3>> entries, Dictionary<string, int> indexByName)
        {
            int liveMatches = 0;
            List<string> samples = new List<string>();

            try
            {
                Il2CppType il2CppType = this.TryGetNpcTeleportIl2CppType(
                    "XDTLevelAndEntity.Gameplay.Component.Npc.NpcComponent",
                    "XDTLevelAndEntity.GamePlay.Component.Npc.NpcComponent",
                    "XDTLevelAndEntity.Gameplay.Component.InternalNpc.InternalNpcComponent",
                    "XDTLevelAndEntity.GamePlay.Component.InternalNpc.InternalNpcComponent",
                    "NpcComponent");
                if (il2CppType == null)
                {
                    this.LogNpcTeleportDebug("Unity NPC scan: NpcComponent Il2CppType unavailable");
                    return 0;
                }

                UnityObject[] objects = Object.FindObjectsOfType(il2CppType);
                if (objects == null || objects.Length == 0)
                {
                    this.LogNpcTeleportDebug("Unity NPC scan: found 0 NpcComponent objects");
                    return 0;
                }

                foreach (UnityObject unityObject in objects)
                {
                    if (unityObject == null)
                    {
                        continue;
                    }

                    object npcComponent = unityObject;
                    int staticId = 0;
                    this.TryResolveNpcStaticIdFromComponent(npcComponent, out staticId);

                    Vector3 position;
                    if (!this.TryGetNpcTeleportPosition(npcComponent, out position) || position == Vector3.zero)
                    {
                        if (staticId > 0 && samples.Count < 5)
                        {
                            samples.Add("staticId=" + staticId + ":no-pos");
                        }
                        continue;
                    }

                    string liveName;
                    if (!this.TryResolveNpcNameFromComponent(npcComponent, out liveName))
                    {
                        if (staticId > 0 && samples.Count < 5)
                        {
                            samples.Add("staticId=" + staticId + ":no-name");
                        }
                        continue;
                    }

                    string label = this.ResolvePreferredNpcTeleportLabel(liveName);
                    this.AddOrUpdateNpcTeleportEntry(entries, indexByName, label, position);
                    liveMatches++;

                    if (samples.Count < 5)
                    {
                        samples.Add(label + (staticId > 0 ? ("#" + staticId) : string.Empty));
                    }
                }

                this.LogNpcTeleportDebug("Unity NPC scan: objects=" + objects.Length + " liveMatches=" + liveMatches + (samples.Count > 0 ? " samples=" + string.Join(", ", samples) : string.Empty));
            }
            catch (Exception ex)
            {
                this.LogNpcTeleportDebug("Unity NPC scan exception: " + ex.Message);
            }

            return liveMatches;
        }

        private bool TryResolveNpcStaticIdFromIl2CppObject(Il2CppObject npcObject, out int staticId)
        {
            staticId = 0;
            if (npcObject == null)
            {
                return false;
            }

            try
            {
                Il2CppType ilType = npcObject.GetIl2CppType();
                foreach (string member in new string[] { "staticId", "StaticId", "_staticId", "npcId", "NpcId", "id", "Id" })
                {
                    if (this.TryReadUIntMember(ilType, npcObject, member, out uint value) && value > 0U)
                    {
                        staticId = unchecked((int)value);
                        return true;
                    }
                }

                foreach (string member in new string[] { "ComponentData", "_componentData", "componentData", "Data", "data" })
                {
                    if (this.TryReadObjectMember(ilType, npcObject, member, out Il2CppObject dataObj) && dataObj != null)
                    {
                        Il2CppType dataType = dataObj.GetIl2CppType();
                        foreach (string dataMember in new string[] { "staticId", "StaticId", "npcId", "NpcId", "id", "Id" })
                        {
                            if (this.TryReadUIntMember(dataType, dataObj, dataMember, out uint dataValue) && dataValue > 0U)
                            {
                                staticId = unchecked((int)dataValue);
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryResolveNpcNameFromIl2CppObject(Il2CppObject npcObject, out string npcName)
        {
            npcName = string.Empty;
            if (npcObject == null)
            {
                return false;
            }

            try
            {
                Il2CppType ilType = npcObject.GetIl2CppType();
                foreach (string member in new string[] { "npcName", "NpcName", "displayName", "DisplayName", "name", "Name" })
                {
                    if (this.TryReadStringMember(ilType, npcObject, member, out npcName))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryExtractNpcPositionFromIl2CppObject(Il2CppObject npcObject, out Vector3 position)
        {
            position = Vector3.zero;
            if (npcObject == null)
            {
                return false;
            }

            try
            {
                if (this.TryReadVector3Member(npcObject, "position", out position) && position != Vector3.zero)
                {
                    return true;
                }

                Il2CppType ilType = npcObject.GetIl2CppType();
                foreach (string member in new string[] { "entity", "Entity", "_entity" })
                {
                    if (this.TryReadObjectMember(ilType, npcObject, member, out Il2CppObject entityObj) && entityObj != null)
                    {
                        if (this.TryReadVector3Member(entityObj, "position", out position) && position != Vector3.zero)
                        {
                            return true;
                        }
                        if (this.TryReadVector3Member(entityObj, "worldPosition", out position) && position != Vector3.zero)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private Il2CppType TryGetNpcTeleportIl2CppType(params string[] typeNames)
        {
            if (typeNames == null)
            {
                return null;
            }

            string[] assemblies = new string[]
            {
                "XDTLevelAndEntity",
                "XDTLevelAndEntity.dll",
                "Assembly-CSharp",
                "Assembly-CSharp.dll"
            };

            foreach (string typeName in typeNames)
            {
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    continue;
                }

                try
                {
                    Il2CppType direct = Il2CppType.GetType(typeName);
                    if (direct != null)
                    {
                        return direct;
                    }
                }
                catch
                {
                }

                foreach (string assemblyName in assemblies)
                {
                    try
                    {
                        Il2CppType qualified = Il2CppType.GetType(typeName + ", " + assemblyName);
                        if (qualified != null)
                        {
                            return qualified;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private int PopulateLiveNpcEntriesFromMapSpots(List<KeyValuePair<string, Vector3>> entries, Dictionary<string, int> indexByName, Dictionary<string, int> npcIdMap)
        {
            int num = 0;
            try
            {
                Type type = this.FindLoadedType("XDTGameSystem.GameplaySystem.MapSpots.MapSpotsSystem", "MapSpotsSystem");
                if (type == null)
                {
                    return 0;
                }

                object obj = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                if (obj == null)
                {
                    FieldInfo fieldInfo = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (fieldInfo != null)
                    {
                        obj = fieldInfo.GetValue(null);
                    }
                }
                if (obj == null)
                {
                    return 0;
                }

                MethodInfo methodInfo = obj.GetType().GetMethod("GetMapSpots", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (methodInfo == null)
                {
                    return 0;
                }

                IEnumerable enumerable = methodInfo.Invoke(obj, null) as IEnumerable;
                if (enumerable == null)
                {
                    return 0;
                }

                Dictionary<int, string> dictionary = new Dictionary<int, string>();
                foreach (string npcName in this.GetNpcTeleportPreferredNames())
                {
                    int num2;
                    if (this.TryResolveNpcTeleportId(npcName, npcIdMap, out num2) && !dictionary.ContainsKey(num2))
                    {
                        dictionary[num2] = npcName;
                    }
                }

                foreach (object obj2 in enumerable)
                {
                    if (obj2 == null)
                    {
                        continue;
                    }

                    object obj3;
                    if (!this.TryGetObjectMember(obj2, "category", out obj3) || obj3 == null || !string.Equals(obj3.ToString(), "Npc", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    object obj4;
                    object obj5;
                    if (!this.TryGetObjectMember(obj2, "usageId", out obj4) || !this.TryGetObjectMember(obj2, "position", out obj5) || !(obj5 is Vector3))
                    {
                        continue;
                    }

                    int num3;
                    try
                    {
                        num3 = Convert.ToInt32(obj4);
                    }
                    catch
                    {
                        continue;
                    }

                    string value;
                    if (!dictionary.TryGetValue(num3, out value))
                    {
                        continue;
                    }

                    this.AddOrUpdateNpcTeleportEntry(entries, indexByName, value, (Vector3)obj5);
                    num++;
                }
            }
            catch
            {
            }

            return num;
        }

        private List<KeyValuePair<string, Vector3>> GetTeleportNpcEntries(bool forceRefresh = false)
        {
            if (!forceRefresh)
            {
                return this.cachedNpcTeleportEntries ?? new List<KeyValuePair<string, Vector3>>();
            }

            if (!NpcTeleportLiveLocationEnabled)
            {
                this.npcTeleportStatus = "NPC teleport disabled";
                this.cachedNpcTeleportEntries = new List<KeyValuePair<string, Vector3>>();
                return this.cachedNpcTeleportEntries;
            }

            if (GetPlayer() == null)
            {
                this.npcTeleportStatus = "Not Ready";
                this.cachedNpcTeleportEntries = new List<KeyValuePair<string, Vector3>>();
                this.LogNpcTeleportDebug("GetTeleportNpcEntries: player not ready");
                return this.cachedNpcTeleportEntries;
            }

            List<KeyValuePair<string, Vector3>> list = new List<KeyValuePair<string, Vector3>>();
            Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int liveMatches = this.PopulateLiveNpcEntriesFromUnityObjects(list, dictionary);
            Dictionary<string, int> npcTeleportIdMap = this.GetNpcTeleportIdMap();
            this.LogNpcTeleportDebug("GetTeleportNpcEntries: idMapCount=" + npcTeleportIdMap.Count + " cacheReady=" + this.npcTeleportIdCacheReady + " resolveStatus=" + this.npcTeleportIdResolveStatus);
            List<KeyValuePair<string, int>> npcCandidates = this.GetNpcTeleportCandidates(npcTeleportIdMap);
            int resolvedIds = 0;
            int totalTargets = npcCandidates.Count;
            foreach (KeyValuePair<string, int> npcCandidate in npcCandidates)
            {
                if (npcCandidate.Value <= 0)
                {
                    continue;
                }
                resolvedIds++;
                Vector3 vector;
                if (this.TryGetLiveNpcPositionById(npcCandidate.Value, out vector) || this.TryGetLiveNpcPositionByIdMono(npcCandidate.Value, out vector))
                {
                    this.AddOrUpdateNpcTeleportEntry(list, dictionary, npcCandidate.Key, vector);
                    liveMatches = Math.Max(liveMatches, list.Count);
                }
            }

            this.cachedNpcTeleportEntries = list;
            this.npcTeleportStatus = (liveMatches > 0)
                ? string.Format("Live NPCs: {0}/{1}", liveMatches, totalTargets)
                : (resolvedIds > 0
                    ? string.Format("No live NPC positions found ({0}/{1} ids resolved)", resolvedIds, totalTargets)
                    : ("No NPC ids resolved" + (string.IsNullOrEmpty(this.npcTeleportIdResolveStatus) ? string.Empty : (" [" + this.npcTeleportIdResolveStatus + "]"))));
            this.LogNpcTeleportDebug("GetTeleportNpcEntries: built entries count=" + list.Count + " resolvedIds=" + resolvedIds + " liveMatches=" + liveMatches + " status=" + this.npcTeleportStatus);
            return this.cachedNpcTeleportEntries;
        }

        // Token: 0x06000025 RID: 37 RVA: 0x00006E00 File Offset: 0x00005000
        private void TeleportToLocation(Vector3 targetPos)
        {
            Breadcrumbs.Drop("Teleport", targetPos.ToString("F1"));
            GameObject gameObject = GameObject.Find("p_player_skeleton(Clone)");
            bool flag = gameObject == null;
            if (flag)
            {
                ModLogger.Msg("Player not found!");
            }
            else
            {
                Vector3 position = gameObject.transform.position;
                this.EnsurePositionOverridePatched();
                HeartopiaComplete.OverridePosition = targetPos;
                HeartopiaComplete.OverridePlayerPosition = true;
                CharacterController component = gameObject.GetComponent<CharacterController>();
                bool flag2 = component != null;
                if (flag2)
                {
                    component.enabled = false;
                }
                gameObject.transform.position = targetPos;
                bool flag3 = component != null;
                if (flag3)
                {
                    component.enabled = true;
                }
                this.teleportFramesRemaining = 30;
            }
        }

        private void TeleportToLocation(Vector3 targetPos, Quaternion targetRot)
        {
            GameObject gameObject = GameObject.Find("p_player_skeleton(Clone)");
            bool flag = gameObject == null;
            if (flag)
            {
                ModLogger.Msg("Player not found!");
            }
            else
            {
                Vector3 position = gameObject.transform.position;
                this.EnsurePositionOverridePatched();
                this.EnsureRotationOverridePatched();
                HeartopiaComplete.OverridePosition = targetPos;
                HeartopiaComplete.OverridePlayerPosition = true;
                HeartopiaComplete.PlayerOverrideRot = targetRot;
                HeartopiaComplete.OverridePlayerRotation = true;
                CharacterController component = gameObject.GetComponent<CharacterController>();
                bool flag2 = component != null;
                if (flag2)
                {
                    component.enabled = false;
                }
                gameObject.transform.position = targetPos;
                gameObject.transform.rotation = targetRot;
                bool flag3 = component != null;
                if (flag3)
                {
                    component.enabled = true;
                }
                this.teleportFramesRemaining = 30;
                this.playerRotationFramesRemaining = 30;
            }
        }

        private void TeleportTo(Vector3 targetPos)
        {
            this.EnsurePositionOverridePatched();
            OverridePosition = targetPos;
            OverridePlayerPosition = true;
            teleportFramesRemaining = 10;
            GameObject p = GetPlayer();
            if (p != null)
            {
                p.transform.position = targetPos;
                if (p.transform.root != null) p.transform.root.position = targetPos;
            }
        }

        private void SyncTeleportPosition()
        {
            if (OverridePlayerPosition && teleportFramesRemaining > 0)
            {
                teleportFramesRemaining--;
                GameObject p = GetPlayer();
                if (p != null)
                {
                    p.transform.position = OverridePosition;
                    if (p.transform.root != null) p.transform.root.position = OverridePosition;
                }
                if (teleportFramesRemaining <= 0)
                {
                    OverridePlayerPosition = false;
                    try
                    {
                        if (this.isResourceFarmTeleport)
                        {
                            this.OnTeleportArrivedResource();
                        }
                    }
                    catch { }
                }
            }
        }

        private bool TryClickNpcChatIcon()
        {
            try
            {
                // Preferred: use IconsBarWidget candidates and score by known interaction sprite IDs.
                // From user logs this UI exposes two candidates: ui_dynamic_interaction_301 and ui_dynamic_interaction_3010.
                try
                {
                    var listRoot = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list");
                    if (listRoot != null)
                    {
                        Transform bestCell = null;
                        string bestSprite = string.Empty;
                        int bestScore = int.MinValue;

                        int childCount = listRoot.transform.childCount;
                        for (int ci = 0; ci < childCount; ci++)
                        {
                            var cell = listRoot.transform.GetChild(ci);
                            if (cell == null || !cell.gameObject.activeInHierarchy) continue;

                            var cellImgs = cell.GetComponentsInChildren<Image>(true);
                            if (cellImgs == null || cellImgs.Length == 0) continue;

                            string iconSprite = string.Empty;
                            for (int ii = 0; ii < cellImgs.Length; ii++)
                            {
                                var im = cellImgs[ii];
                                if (im == null || im.sprite == null) continue;

                                string imgName = (im.name ?? string.Empty).ToLowerInvariant();
                                string sp = (im.sprite.name ?? string.Empty).ToLowerInvariant();
                                if (imgName.Contains("icon@img@btn") || sp.Contains("ui_dynamic_interaction_"))
                                {
                                    iconSprite = sp;
                                    break;
                                }

                                if (string.IsNullOrEmpty(iconSprite))
                                {
                                    iconSprite = sp;
                                }
                            }

                            if (string.IsNullOrEmpty(iconSprite)) continue;

                            int score = 0;
                            // Prefer the exact observed NPC interaction icon first.
                            if (iconSprite.Contains("ui_dynamic_interaction_301(clone)") || iconSprite.EndsWith("_301")) score += 120;
                            else if (iconSprite.Contains("ui_dynamic_interaction_3010(clone)") || iconSprite.EndsWith("_3010")) score += 90;

                            if (iconSprite.Contains("chat") || iconSprite.Contains("talk") || iconSprite.Contains("bubble") || iconSprite.Contains("dialog")) score += 40;

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestCell = cell;
                                bestSprite = iconSprite;
                            }
                        }

                        if (bestCell != null && bestScore > 0)
                        {
                            var iconBtnNode = bestCell.Find("root_visible@go/icon@img@btn");
                            if (iconBtnNode != null)
                            {
                                var iconBtn = iconBtnNode.GetComponent<Button>();
                                if (iconBtn != null && iconBtn.interactable)
                                {
                                    iconBtn.onClick.Invoke();
                                    LogAutoBuy(" Clicked interaction icon: " + bestSprite);
                                    return true;
                                }

                                if (SimulateClick(iconBtnNode.gameObject))
                                {
                                    LogAutoBuy(" SimClicked interaction icon: " + bestSprite);
                                    return true;
                                }
                            }

                            var btn = bestCell.GetComponentInChildren<Button>(true);
                            if (btn != null && btn.interactable)
                            {
                                btn.onClick.Invoke();
                                LogAutoBuy(" Clicked best IconsBarWidget cell: " + bestSprite);
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex) { LogAutoBuy(" IconsBarWidget search error: " + ex.Message); }
                var imgs = Object.FindObjectsOfType<Image>();
                foreach (var img in imgs)
                {
                    if (img == null || img.sprite == null || !img.gameObject.activeInHierarchy) continue;
                    string s = img.sprite.name.ToLowerInvariant();
                    if (s.Contains("chat") || s.Contains("talk") || s.Contains("bubble") || s.Contains("dialog") || s.Contains("petplay"))
                    {
                        var btn = img.GetComponentInParent<Button>();
                        if (btn != null && btn.interactable) { btn.onClick.Invoke(); LogAutoBuy(" Clicked chat Button"); return true; }
                        Transform t = img.transform;
                        for (int i = 0; i < 6 && t != null; i++)
                        {
                            if (SimulateClick(t.gameObject)) { LogAutoBuy(" Simulated click on chat UI"); return true; }
                            t = t.parent;
                        }
                    }
                }
            }
            catch (Exception ex) { LogAutoBuy(" TryClickNpcChatIcon error: " + ex.Message); }
            // Prefer dialog-specific icon GameObjects by name to avoid global player-chat icon
            try
            {
                // Single pass through active-only transforms (replaces 5x Resources.FindObjectsOfTypeAll which includes inactive objects)
                Transform[] activeTransforms = Object.FindObjectsOfType<Transform>();
                for (int ti = 0; ti < activeTransforms.Length; ti++)
                {
                    Transform tr = activeTransforms[ti];
                    try
                    {
                        if (tr == null) continue;
                        string trName = tr.name;
                        if (string.IsNullOrEmpty(trName)) continue;
                        if (!trName.Contains("CommonIconForDialog") && !trName.Contains("CommonIconForCookNormalDialog") &&
                            !trName.Contains("CommonIconForCookDangerDialog") && !trName.Contains("CommonIconForRecycleDialog")) continue;
                        // find a Button or Image child to click
                        var btn = tr.GetComponentInChildren<Button>(true);
                        if (btn != null && btn.interactable) { btn.onClick.Invoke(); if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] Clicked preferred icon '{trName}'"); }
                            // verify dialogue opened
                            var dlg = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                            if (dlg != null && dlg.activeInHierarchy) return true;
                        }
                        var img = tr.GetComponentInChildren<Image>(true);
                        if (img != null)
                        {
                            if (img.GetComponentInParent<Button>() is Button b && b.interactable) { b.onClick.Invoke(); if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] Clicked preferred image button '{trName}'"); } var dlg2 = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)"); if (dlg2 != null && dlg2.activeInHierarchy) return true; }
                            if (SimulateClick(img.gameObject)) { if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] SimClicked preferred image '{trName}'"); } var dlg3 = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)"); if (dlg3 != null && dlg3.activeInHierarchy) return true; }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { LogAutoBuy(" Preferred icon search error: " + ex.Message); }

            // Fallback: find NPC name Texts and click nearby Image UI (chat bubble above NPC)
            try
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    var texts = Object.FindObjectsOfType<Text>();
                    // Hoist Image scan outside the Text loop: was O(NxM), now O(N+M)
                    var allImgs = Object.FindObjectsOfType<Image>();
                    foreach (var t in texts)
                    {
                        try
                        {
                            if (t == null || !t.gameObject.activeInHierarchy) continue;
                            string txt = t.text ?? "";
                            if (string.IsNullOrWhiteSpace(txt)) continue;
                            // avoid UI menu texts by ignoring DialoguePanel and other known panels
                            if (t.transform.IsChildOf(GameObject.Find("GameApp/startup_root(Clone)")?.transform) == false) continue;
                            Vector3 worldPos = t.transform.position;
                            Vector3 screen = cam.WorldToScreenPoint(worldPos);
                            if (screen.z < 0) continue;
                            // search images near this screen point
                            foreach (var im in allImgs)
                            {
                                if (im == null || !im.gameObject.activeInHierarchy) continue;
                                Vector3 imScreen = cam.WorldToScreenPoint(im.transform.position);
                                if (imScreen.z < 0) continue;
                                float dist = Vector2.Distance(new Vector2(screen.x, screen.y), new Vector2(imScreen.x, imScreen.y));
                                if (dist < 140f)
                                {
                                    // likely the bubble icon above this NPC
                                    var btn = im.GetComponentInParent<Button>();
                                    if (btn != null && btn.interactable) { btn.onClick.Invoke(); if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] Clicked nearby UI '{im.name}' near name '{txt}'"); } return true; }
                                    if (SimulateClick(im.gameObject)) { if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] SimClicked nearby UI '{im.name}' near name '{txt}'"); } return true; }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { LogAutoBuy(" Fallback detection error: " + ex.Message); }

            return false;
        }

        private string GetCustomTeleportPath()
        {
            return HelperPaths.GetFile("custom_teleports.json");
        }

        private void SaveCustomTeleports()
        {
            try
            {
                UnifiedConfigData config = this.LoadOrCreateUnifiedConfig();
                this.PopulateAllConfigSections(config);
                this.SaveUnifiedConfig(config);
                ModLogger.Msg("Custom Teleports Saved!");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error Saving Teleports: " + ex.Message);
            }
        }

        private void LoadCustomTeleports()
        {
            try
            {
                UnifiedConfigData config = this.LoadUnifiedConfig();
                if (config != null)
                {
                    this.customTeleportList.Clear();
                    foreach (CustomTeleportEntry entry in config.CustomTeleports)
                    {
                        if (entry != null) this.customTeleportList.Add(entry);
                    }
                    ModLogger.Msg($"Loaded {this.customTeleportList.Count} custom teleports.");
                    return;
                }
                string path = this.GetCustomTeleportPath();
                if (File.Exists(path))
                {
                    this.customTeleportList.Clear();
                    string[] lines = File.ReadAllLines(path);
                    foreach (string line in lines)
                    {
                        if (line.Contains("\"name\":"))
                        {
                            try 
                            {
                                // Simple efficient parsing for flat structure
                                string name = GetJsonString(line, "\"name\":");
                                float x = GetJsonFloat(line, "\"x\":");
                                float y = GetJsonFloat(line, "\"y\":");
                                float z = GetJsonFloat(line, "\"z\":");
                                
                                this.customTeleportList.Add(new CustomTeleportEntry { name = name, position = new Vector3(x, y, z) });
                            } 
                            catch {}
                        }
                    }
                    ModLogger.Msg($"Loaded {this.customTeleportList.Count} custom teleports.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error Loading Teleports: " + ex.Message);
            }
        }

        [Serializable]
        public class CustomTeleportEntry
        {
            public string name;
            public Vector3 position;
        }

    }
}
