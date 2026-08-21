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
        // Event-driven bubble removal (events-first policy): collecting a bubble removes its ENTITY server-side
        // -> EntityRemoveEvent(netId). Aura-scan bubble markers are keyed by (int)netId, and those are exactly
        // the ones with no scene-object liveness check in the 0.75 s marker sync — without this they linger
        // until the next snapshot refresh (10 s) / aura rescan (40 s cache). Distance-gated to <25 m (same rule
        // as the scene-target path): near = authoritative pickup, far = could be entity streaming-out, which
        // must NOT drop the marker (anti-flicker) — the snapshot pipeline handles those.
        private bool bubbleRemoveEventHookRegistered;
        private Vector3 bubbleRadarLastSyncOrigin;
        private bool bubbleRadarHasLastSyncOrigin;

        private void EnsureBubbleRemoveEventHook()
        {
            if (this.bubbleRemoveEventHookRegistered)
            {
                return;
            }
            // Registration is append-only (a second call would add a duplicate handler) -> latch on success.
            this.bubbleRemoveEventHookRegistered = this.RegisterGameEventHook(
                "XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityRemoveEvent", 32, this.OnBubbleEntityRemoveEvent);
        }

        // Where the radar currently believes a bubble is, nearest to `near`. Tracked positions are
        // refreshed from the live scene object each marker sync, so this is the drifting bubble's
        // CURRENT place, not the one the farm planned against.
        internal bool TryGetLiveBubblePosition(Vector3 near, float maxDistance, out Vector3 position)
        {
            return this.TryGetLiveBubblePosition(near, maxDistance, out position, out _);
        }

        // bubbleId comes back too, because "is this bubble still here" cannot be answered by
        // position: a drifting bubble leaves any fixed point within a second or two, and reading
        // that as "it popped" is how the farm reported four collects it never made.
        internal bool TryGetLiveBubblePosition(Vector3 near, float maxDistance, out Vector3 position, out int bubbleId)
        {
            position = Vector3.zero;
            bubbleId = 0;
            float bestSqr = maxDistance * maxDistance;
            bool found = false;

            // The bound scene objects first: they are the bubble itself, read this frame. The two
            // dictionaries below are only as fresh as the last marker sync.
            //
            // ⚠️ Read the object inline rather than through TryGetBubbleLivePosition — that one
            // PRUNES dead entries, and pruning the dictionary being enumerated throws.
            foreach (KeyValuePair<int, GameObject> live in this.bubbleLiveObjects)
            {
                GameObject go = live.Value;
                if (go == null)
                {
                    continue;
                }

                Vector3 livePos;
                try
                {
                    if (!go.activeInHierarchy)
                    {
                        continue;
                    }

                    livePos = go.transform.position;
                }
                catch
                {
                    continue;
                }

                if (livePos == Vector3.zero)
                {
                    continue;
                }

                float liveSqr = (livePos - near).sqrMagnitude;
                if (liveSqr < bestSqr)
                {
                    bestSqr = liveSqr;
                    position = livePos;
                    bubbleId = live.Key;
                    found = true;
                }
            }

            if (found)
            {
                return true;
            }

            foreach (KeyValuePair<int, Vector3> tracked in this.bubbleRadarTrackedPositions)
            {
                float sqr = (tracked.Value - near).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    position = tracked.Value;
                    found = true;
                }
            }

            if (found)
            {
                return true;
            }

            foreach (KeyValuePair<int, Vector3> snap in this.bubbleRadarSnapshotPositions)
            {
                float sqr = (snap.Value - near).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    position = snap.Value;
                    found = true;
                }
            }

            return found;
        }

        // Make the next marker sync run now instead of on its interval. A bubble the walker is
        // closing on has to be re-read far more often than the radar's idle cadence.
        internal void ForceBubbleRadarResync()
        {
            this.bubbleRadarForceRefresh = true;
            this.nextBubbleMarkerSyncAt = -999f;
        }

        private void OnBubbleEntityRemoveEvent(GameEventSnapshot e)
        {
            if (!this.showBubbleRadar || !this.bubbleRadarHasLastSyncOrigin)
            {
                return;
            }
            uint netId = e.ReadUInt32(8);
            if (netId == 0U)
            {
                return;
            }
            int markerId = unchecked((int)netId);
            Vector3 bubblePos;
            if (!this.bubbleRadarTrackedPositions.TryGetValue(markerId, out bubblePos)
                && !this.bubbleRadarSnapshotPositions.TryGetValue(markerId, out bubblePos))
            {
                return; // not a bubble we track
            }
            float retainSqr = BubbleRadarSceneMissingRetainMinDistance * BubbleRadarSceneMissingRetainMinDistance;
            if ((this.bubbleRadarLastSyncOrigin - bubblePos).sqrMagnitude >= retainSqr)
            {
                return; // far away — likely streamed out, keep the marker
            }
            this.RemoveBubbleTrackedMarker(markerId);
            this.bubbleRadarSnapshotPositions.Remove(markerId);
            this.BubbleRadarLogThrottled("entity-remove",
                "Bubble netId=" + netId.ToString() + " entity removed nearby (collected) -> marker dropped.", 2f);
        }

        private Type FindLoadedBubbleServiceType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    string typeName = type.Name ?? string.Empty;
                    string fullName = type.FullName ?? string.Empty;
                    bool isBubbleInterface = string.Equals(typeName, "IBubbleService", StringComparison.Ordinal)
                        || fullName.EndsWith(".Bubble.IBubbleService", StringComparison.Ordinal);
                    bool nameMatch = isBubbleInterface
                        || string.Equals(typeName, "BubbleClientService", StringComparison.Ordinal)
                        || typeName.EndsWith("IBubbleService", StringComparison.Ordinal)
                        || typeName.EndsWith("BubbleClientService", StringComparison.Ordinal)
                        || (typeName.IndexOf("Bubble", StringComparison.OrdinalIgnoreCase) >= 0
                            && typeName.IndexOf("Service", StringComparison.OrdinalIgnoreCase) >= 0)
                        || fullName.IndexOf(".Bubble.", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!nameMatch)
                    {
                        continue;
                    }

                    MethodInfo getAllMethod = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "GmGetAllBubble" && m.GetParameters().Length >= 1);
                    if (getAllMethod != null)
                    {
                        if (isBubbleInterface)
                        {
                            return type;
                        }

                        return type;
                    }
                }
            }

            return null;
        }

        private bool ShouldTrackBubbleObject(string lowerName)
        {
            if (string.IsNullOrEmpty(lowerName))
            {
                return false;
            }

            // Safe world-space bubble collectible visuals seen in Heartopia.
            if (lowerName.Contains("p_bubble_bubble_") && lowerName.Contains("(clone)"))
            {
                return true;
            }

            return false;
        }

        private bool IsUsableBubbleSceneObject(GameObject candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            try
            {
                if (!candidate.scene.IsValid() || !candidate.scene.isLoaded)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            if ((candidate.hideFlags & HideFlags.HideAndDontSave) != 0)
            {
                return false;
            }

            if (!candidate.activeInHierarchy)
            {
                return false;
            }

            if (string.IsNullOrEmpty(candidate.name))
            {
                return false;
            }

            string lowerName = candidate.name.ToLowerInvariant();
            if (!this.ShouldTrackBubbleObject(lowerName))
            {
                return false;
            }

            // Keep the safe scan focused on world visuals instead of UI or unloaded assets.
            if (candidate.GetComponent<Canvas>() != null)
            {
                return false;
            }

            return this.HasLiveBubbleVisual(candidate);
        }

        private bool HasLiveBubbleVisual(GameObject candidate)
        {
            if (candidate == null || !candidate.activeInHierarchy)
            {
                return false;
            }

            try
            {
                Component[] components = candidate.GetComponentsInChildren<Component>(false);
                if (components != null)
                {
                    for (int i = 0; i < components.Length; i++)
                    {
                        Component component = components[i];
                        if (component == null || component.gameObject == null || !component.gameObject.activeInHierarchy)
                        {
                            continue;
                        }

                        Type componentType = component.GetType();
                        if (componentType == null || !string.Equals(componentType.Name, "ParticleSystem", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        bool isPlaying = false;
                        int particleCount = 0;
                        bool isAlive = false;
                        try
                        {
                            PropertyInfo isPlayingProperty = componentType.GetProperty("isPlaying", BindingFlags.Public | BindingFlags.Instance);
                            if (isPlayingProperty != null)
                            {
                                object raw = isPlayingProperty.GetValue(component, null);
                                if (raw is bool rawBool)
                                {
                                    isPlaying = rawBool;
                                }
                            }
                        }
                        catch
                        {
                        }

                        try
                        {
                            PropertyInfo countProperty = componentType.GetProperty("particleCount", BindingFlags.Public | BindingFlags.Instance);
                            if (countProperty != null)
                            {
                                object raw = countProperty.GetValue(component, null);
                                if (raw != null)
                                {
                                    particleCount = Convert.ToInt32(raw);
                                }
                            }
                        }
                        catch
                        {
                        }

                        try
                        {
                            MethodInfo isAliveMethod = componentType.GetMethod("IsAlive", new Type[] { typeof(bool) });
                            if (isAliveMethod != null)
                            {
                                object raw = isAliveMethod.Invoke(component, new object[] { true });
                                if (raw is bool rawBool)
                                {
                                    isAlive = rawBool;
                                }
                            }
                        }
                        catch
                        {
                        }

                        if (isPlaying || particleCount > 0 || isAlive)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>(false);
                if (renderers != null)
                {
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Renderer renderer = renderers[i];
                        if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
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

        // ==========================================================================================
        // FOLLOWING A BUBBLE, RATHER THAN REMEMBERING WHERE IT APPEARED.
        //
        // Measured 2026-08-20 with the BubbleProbe plugin, one live bubble in the scene:
        //     live=(76.0, -30.6, -83.5)
        //     radar tracked  11.14m off (77.0, -30.6, -72.4)
        //     spawn snapshot 11.14m off (77.0, -30.6, -72.4)      sceneTargets=0
        //
        // Tracked and snapshot were byte-identical and eleven metres wrong, because the marker sync
        // only re-reads a bubble's transform when bubbleRadarSceneTargets has an entry for it — and
        // that dictionary is keyed by Unity INSTANCE ID while the sync looks it up by BUBBLE ID (a
        // netId). The two key spaces never meet, so the branch was dead and every bubble stayed
        // frozen at the position it spawned at. The farm then walked to that ghost, arrived 0.21 m
        // from it, and the dwell capped with "marker still present".
        //
        // The fix is to hold the GameObject itself, keyed the way the sync asks for it. Binding
        // happens ONCE per bubble, by proximity, while the recorded position is still fresh — at
        // spawn that position IS the bubble, so a tight radius is safe; the two only diverge
        // afterwards. From then on the object is followed directly, at the cost of a transform read.
        private const float BubbleLiveResolveRadius = 5f;
        private const float BubbleLiveResolveInterval = 1f;
        private const int BubbleLiveBindMissLimit = 3;
        private float bubbleLiveResolveNextAt;

        // The live transform of a bubble already bound to its scene object.
        internal bool TryGetBubbleLivePosition(int bubbleId, out Vector3 position)
        {
            position = Vector3.zero;
            if (!this.bubbleLiveObjects.TryGetValue(bubbleId, out GameObject go))
            {
                return false;
            }

            if (go == null)
            {
                this.bubbleLiveObjects.Remove(bubbleId);
                return false;
            }

            try
            {
                if (!go.activeInHierarchy)
                {
                    this.bubbleLiveObjects.Remove(bubbleId);
                    return false;
                }

                position = go.transform.position;
                return position != Vector3.zero;
            }
            catch
            {
                this.bubbleLiveObjects.Remove(bubbleId);
                return false;
            }
        }

        // Is this exact bubble still in the world? The only honest completion test for a chase.
        internal bool IsBubbleStillLive(int bubbleId)
        {
            return bubbleId != 0 && this.TryGetBubbleLivePosition(bubbleId, out _);
        }

        // Binds every bubble that has no object yet to the nearest live one. ONE scene walk for all
        // of them, throttled, and skipped entirely when nothing is unbound — this is the expensive
        // half, and after the first pass there is normally nothing left to do.
        private void ResolveBubbleLiveObjects(Dictionary<int, Vector3> known)
        {
            if (known == null || known.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.bubbleLiveResolveNextAt)
            {
                return;
            }

            bool anyUnbound = false;
            foreach (KeyValuePair<int, Vector3> entry in known)
            {
                if (!this.bubbleLiveObjects.TryGetValue(entry.Key, out GameObject bound) || bound == null)
                {
                    anyUnbound = true;
                    break;
                }
            }

            if (!anyUnbound)
            {
                return;
            }

            this.bubbleLiveResolveNextAt = now + BubbleLiveResolveInterval;

            Vector3 scanOrigin = this.bubbleRadarLastSyncOrigin;
            bool haveOrigin = this.bubbleRadarHasLastSyncOrigin;

            GameObject[] all;
            try
            {
                all = UnityEngine.Object.FindObjectsOfType<GameObject>();
            }
            catch
            {
                return;
            }

            if (all == null)
            {
                return;
            }

            // Collect the live bubbles once, then match against that; the alternative is a full
            // scene walk per unbound bubble.
            this.bubbleLiveScanBuffer.Clear();
            for (int i = 0; i < all.Length; i++)
            {
                GameObject candidate = all[i];
                try
                {
                    if (!this.IsUsableBubbleSceneObject(candidate))
                    {
                        continue;
                    }

                    Vector3 pos = candidate.transform.position;
                    if (pos == Vector3.zero)
                    {
                        continue;
                    }

                    this.bubbleLiveScanBuffer.Add(new KeyValuePair<GameObject, Vector3>(candidate, pos));
                }
                catch
                {
                    // Objects can be destroyed mid-walk; skipping one is not a failure.
                }
            }

            // NO EARLY RETURN ON AN EMPTY SCAN. "The scene has no bubbles" is not a reason to skip
            // the pass — it is the strongest evidence there is that everything still tracked is a
            // ghost, and skipping here is what let two of them sit on the radar forever.
            int newlyBound = 0;
            this.bubbleLiveEvictBuffer.Clear();
            foreach (KeyValuePair<int, Vector3> entry in known)
            {
                if (this.bubbleLiveObjects.TryGetValue(entry.Key, out GameObject existing) && existing != null)
                {
                    continue;
                }

                float bestSqr = BubbleLiveResolveRadius * BubbleLiveResolveRadius;
                GameObject best = null;
                for (int i = 0; i < this.bubbleLiveScanBuffer.Count; i++)
                {
                    float sqr = (this.bubbleLiveScanBuffer[i].Value - entry.Value).sqrMagnitude;
                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = this.bubbleLiveScanBuffer[i].Key;
                    }
                }

                if (best != null)
                {
                    this.bubbleLiveObjects[entry.Key] = best;
                    this.bubbleLiveBindMisses.Remove(entry.Key);
                    newlyBound++;
                    continue;
                }

                // A BUBBLE WITH NO OBJECT, SCAN AFTER SCAN, IS NOT A BUBBLE.
                //
                // Measured 2026-08-20: the world held zero bubbles (scene scan and the bridge agreed)
                // while the radar still drew two, at 146 m and 135 m from the player — both beyond
                // the tour's 120 m range, so the farm had no candidates at all and sat in "Scanning
                // for nodes" with nothing to do. They survived because the retain rule keeps a
                // distant marker on the theory that it is merely streamed out.
                //
                // An unbindable entry also costs: it keeps ResolveBubbleLiveObjects finding
                // something unbound, so the full scene walk re-runs every second, forever.
                //
                // Several consecutive scans, not one: at a world load the entries can legitimately
                // exist a moment before their objects do.
                int misses;
                this.bubbleLiveBindMisses.TryGetValue(entry.Key, out misses);
                misses++;
                this.bubbleLiveBindMisses[entry.Key] = misses;

                // ⚠️ ABSENCE IS ONLY PROOF NEARBY. Far out, "no object" means streamed out, which is
                // exactly what the existing retain rule says — so the eviction borrows its distance
                // rather than inventing a second opinion. A distant entry keeps counting misses and
                // is dropped the moment the player is close enough for its absence to mean anything.
                bool closeEnoughToBeSure = !haveOrigin
                    || (entry.Value - scanOrigin).sqrMagnitude
                        < BubbleRadarSceneMissingRetainMinDistance * BubbleRadarSceneMissingRetainMinDistance;
                if (misses >= BubbleLiveBindMissLimit && closeEnoughToBeSure)
                {
                    this.bubbleLiveEvictBuffer.Add(entry.Key);
                }
            }

            for (int i = 0; i < this.bubbleLiveEvictBuffer.Count; i++)
            {
                int ghost = this.bubbleLiveEvictBuffer[i];
                this.RemoveBubbleTrackedMarker(ghost);
                this.bubbleRadarSnapshotPositions.Remove(ghost);
                this.bubbleLiveBindMisses.Remove(ghost);
                this.bubbleLiveObjects.Remove(ghost);
            }

            if (newlyBound > 0 || this.bubbleLiveEvictBuffer.Count > 0)
            {
                this.BubbleRadarLogThrottled("live-bind",
                    "bound " + newlyBound + ", dropped " + this.bubbleLiveEvictBuffer.Count
                    + " ghost(s) with no scene object, out of " + this.bubbleLiveScanBuffer.Count
                    + " live and " + known.Count + " known.", 5f);
            }
        }

        private bool TryGetAllSpawnedBubblePositionsSceneScan(Dictionary<int, Vector3> positions, out string status)
        {
            status = "Bubble scene scan unavailable";
            if (positions == null)
            {
                return false;
            }

            positions.Clear();
            this.bubbleRadarSceneTargets.Clear();

            try
            {
                GameObject[] sceneObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                if (sceneObjects == null || sceneObjects.Length == 0)
                {
                    status = "Bubble scene scan found no objects";
                    return false;
                }

                int resolvedCount = 0;
                for (int i = 0; i < sceneObjects.Length; i++)
                {
                    GameObject candidate = sceneObjects[i];
                    try
                    {
                        if (!this.IsUsableBubbleSceneObject(candidate))
                        {
                            continue;
                        }

                        Vector3 bubblePos = candidate.transform.position;
                        if (bubblePos == Vector3.zero)
                        {
                            Component ps = candidate.GetComponent("ParticleSystem");
                            if (ps == null)
                            {
                                ps = this.FindChildComponentByName(candidate.transform, "ParticleSystem");
                            }

                            if (ps != null)
                            {
                                bubblePos = ps.transform.position;
                            }
                        }

                        if (bubblePos == Vector3.zero)
                        {
                            continue;
                        }

                        int instanceId = candidate.GetInstanceID();
                        positions[instanceId] = bubblePos;
                        this.bubbleRadarSceneTargets[instanceId] = candidate;
                        resolvedCount++;
                    }
                    catch
                    {
                        // Scene objects may be destroyed or reloaded while scanning; skip safely.
                    }
                }

                status = "Bubble scene scan resolved " + resolvedCount.ToString() + " bubble(s).";
                this.BubbleRadarLog("Scene bubble scan complete. resolved=" + resolvedCount.ToString() + " marker(s)");
                return resolvedCount > 0;
            }
            catch (Exception ex)
            {
                status = "Bubble scene scan failed: " + ex.GetType().Name + ": " + ex.Message;
                this.BubbleRadarLog("Scene bubble scan failed: " + ex.GetType().Name + " - " + ex.Message);
                return false;
            }
        }

        private bool TryGetAllSpawnedBubblePositionsManagedEntities(Dictionary<int, Vector3> positions, out string status)
        {
            status = "Bubble entity scan unavailable";
            if (positions == null)
            {
                return false;
            }

            positions.Clear();

            try
            {
                float now = Time.unscaledTime;
                if (now < this.nextBubbleEntityTypeResolveAttemptAt)
                {
                    status = "Bubble entity type resolve throttled";
                    return false;
                }

                this.nextBubbleEntityTypeResolveAttemptAt = now + BubbleRadarEntityTypeResolveRetryInterval;

                Type entitiesType = this.FindEntitiesRuntimeType();
                Type bubbleComponentType = this.FindBubbleComponentRuntimeType();
                if (entitiesType == null || bubbleComponentType == null)
                {
                    status = "Bubble entity types unavailable. entities=" + (entitiesType != null ? entitiesType.FullName : "null") + " bubble=" + (bubbleComponentType != null ? bubbleComponentType.FullName : "null");
                    this.BubbleRadarLogThrottled("managed-types-missing", status, 8f);
                    return false;
                }

                this.nextBubbleEntityTypeResolveAttemptAt = -999f;

                MethodInfo getComponentsMethod = entitiesType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetComponents" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                if (getComponentsMethod == null)
                {
                    status = "Entities.GetComponents unavailable";
                    return false;
                }

                Type listType = typeof(List<>).MakeGenericType(bubbleComponentType);
                object componentList = Activator.CreateInstance(listType);
                object[] args = new object[] { componentList };
                getComponentsMethod.MakeGenericMethod(bubbleComponentType).Invoke(null, args);
                object results = args[0] ?? componentList;

                if (!(results is System.Collections.IEnumerable enumerable))
                {
                    status = "Bubble component list not enumerable";
                    return false;
                }

                int resolvedCount = 0;
                foreach (object component in enumerable)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (!this.TryResolveManagedBubbleMarker(component, out int markerId, out Vector3 bubblePos))
                        {
                            continue;
                        }

                        positions[markerId] = bubblePos;
                        resolvedCount++;
                    }
                    catch
                    {
                    }
                }

                status = "Bubble entity scan resolved " + resolvedCount.ToString() + " bubble(s).";
                this.BubbleRadarLog("Managed bubble entity scan complete. resolved=" + resolvedCount.ToString() + " marker(s)");
                return resolvedCount > 0;
            }
            catch (Exception ex)
            {
                status = "Bubble entity scan failed: " + ex.GetType().Name + ": " + ex.Message;
                this.BubbleRadarLog("Managed bubble entity scan failed: " + ex.GetType().Name + " - " + ex.Message);
                return false;
            }
        }

        private Type FindBubbleComponentRuntimeType()
        {
            Type resolved = this.FindLoadedType(
                "XDTLevelAndEntity.Gameplay.Component.Bubble.BubbleComponent",
                "XDTLevelAndEntity.GamePlay.Component.Bubble.BubbleComponent",
                "Il2CppXDTLevelAndEntity.Gameplay.Component.Bubble.BubbleComponent",
                "Il2CppXDTLevelAndEntity.GamePlay.Component.Bubble.BubbleComponent",
                "BubbleComponent")
                ?? this.FindLoadedTypeBySuffix(
                    "Gameplay.Component.Bubble.BubbleComponent",
                    "GamePlay.Component.Bubble.BubbleComponent",
                    ".BubbleComponent");
            if (resolved != null)
            {
                return resolved;
            }

            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (Type type in types)
                    {
                        if (type == null || !string.Equals(type.Name, "BubbleComponent", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string fullName = type.FullName ?? string.Empty;
                        if (fullName.IndexOf("Chat", StringComparison.OrdinalIgnoreCase) >= 0
                            || fullName.IndexOf("Widget", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        if (type.GetProperty("ComponentData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null
                            || type.GetField("_componentData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null)
                        {
                            return type;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private bool TryGetAllSpawnedBubblePositions(Dictionary<int, Vector3> positions, out string status)
        {
            status = "Bubble service unavailable";
            if (positions == null)
            {
                return false;
            }

            positions.Clear();

            try
            {
                if (!this.TryGetBubbleClientService(out object bubbleService, out status) || bubbleService == null)
                {
                    bool entityResolved = this.TryGetAllSpawnedBubblePositionsManagedEntities(positions, out string entityStatus);
                    if (entityResolved)
                    {
                        status = "Bubble entity scan ready. " + entityStatus;
                        return true;
                    }

                    bool auraResolved = this.TryGetAllSpawnedBubblePositionsAuraMonoSafe(positions, out string auraStatus);
                    if (auraResolved)
                    {
                        status = "Aura bubble scan ready. " + auraStatus;
                        return true;
                    }

                    bool auraCoolingDown = !string.IsNullOrEmpty(auraStatus)
                        && (auraStatus.IndexOf("throttled", StringComparison.OrdinalIgnoreCase) >= 0
                            || auraStatus.IndexOf("waiting for world settle", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (auraCoolingDown && this.lastAuraBubbleScanSuccessAt > 0f)
                    {
                        status = "Aura bubble snapshot retained. " + auraStatus;
                        return false;
                    }

                    bool sceneResolved = this.TryGetAllSpawnedBubblePositionsSceneScan(positions, out string sceneStatus);
                    status = sceneResolved
                        ? status + " | Bubble scene scan ready. " + sceneStatus
                        : status + " | Entity fallback: " + entityStatus + " | Aura fallback: " + auraStatus + " | Scene fallback: " + sceneStatus;
                    return sceneResolved;
                }

                Type runtimeType = bubbleService.GetType();
                if (this.cachedBubbleClientServiceGetAllMethod == null || this.cachedBubbleClientServiceGetAllMethod.DeclaringType != runtimeType)
                {
                    this.cachedBubbleClientServiceGetAllMethod = runtimeType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "GmGetAllBubble" && m.GetParameters().Length >= 1);
                }

                if (this.cachedBubbleClientServiceGetAllMethod == null)
                {
                    status = "BubbleClientService.GmGetAllBubble unavailable";
                    return false;
                }

                ParameterInfo[] parameters = this.cachedBubbleClientServiceGetAllMethod.GetParameters();
                Type ecsEntityType = this.ResolveBubbleEntityListElementType(parameters[0].ParameterType);
                if (ecsEntityType == null)
                {
                    status = "Bubble entity list type unavailable";
                    return false;
                }

                Type listType = typeof(List<>).MakeGenericType(ecsEntityType);
                object bubbleList = Activator.CreateInstance(listType);
                object[] args = parameters.Length >= 2
                    ? new object[] { bubbleList, -1 }
                    : new object[] { bubbleList };
                this.cachedBubbleClientServiceGetAllMethod.Invoke(bubbleService, args);

                if (!(bubbleList is System.Collections.IEnumerable enumerable))
                {
                    status = "Bubble list not enumerable";
                    return false;
                }

                int resolvedCount = 0;
                foreach (object bubbleEntity in enumerable)
                {
                    if (!this.TryResolveBubbleEntityMarker(bubbleEntity, out int markerId, out Vector3 bubblePos))
                    {
                        continue;
                    }

                    positions[markerId] = bubblePos;
                    resolvedCount++;
                }

                status = "Bubble ECS scan resolved " + resolvedCount.ToString() + " bubble(s).";
                this.BubbleRadarLog("ECS bubble fetch complete. resolved=" + resolvedCount.ToString() + " marker(s)");
                return resolvedCount > 0;
            }
            catch (Exception ex)
            {
                this.BubbleRadarLog("ECS bubble fetch failed: " + ex.GetType().Name + " - " + ex.Message);
                bool entityResolved = this.TryGetAllSpawnedBubblePositionsManagedEntities(positions, out string entityStatus);
                if (entityResolved)
                {
                    status = "Bubble ECS scan failed: " + ex.GetType().Name + " | Bubble entity scan ready. " + entityStatus;
                    return true;
                }

                bool auraResolved = this.TryGetAllSpawnedBubblePositionsAuraMonoSafe(positions, out string auraStatus);
                if (auraResolved)
                {
                    status = "Bubble ECS scan failed: " + ex.GetType().Name + " | Aura bubble scan ready. " + auraStatus;
                    return true;
                }

                bool auraCoolingDown = !string.IsNullOrEmpty(auraStatus)
                    && (auraStatus.IndexOf("throttled", StringComparison.OrdinalIgnoreCase) >= 0
                        || auraStatus.IndexOf("waiting for world settle", StringComparison.OrdinalIgnoreCase) >= 0);
                if (auraCoolingDown && this.lastAuraBubbleScanSuccessAt > 0f)
                {
                    status = "Bubble ECS scan failed: " + ex.GetType().Name + " | Aura bubble snapshot retained. " + auraStatus;
                    return false;
                }

                bool sceneResolved = this.TryGetAllSpawnedBubblePositionsSceneScan(positions, out string sceneStatus);
                status = sceneResolved
                    ? "Bubble ECS scan failed: " + ex.GetType().Name + " | Bubble scene scan ready. " + sceneStatus
                    : "Bubble ECS scan failed: " + ex.GetType().Name + " | Entity fallback: " + entityStatus + " | Aura fallback: " + auraStatus + " | Scene fallback: " + sceneStatus;
                return sceneResolved;
            }
        }

        private bool TryGetBubbleClientService(out object bubbleService, out string status)
        {
            bubbleService = null;
            status = "Bubble service unavailable";

            try
            {
                if (this.cachedBubbleClientServiceTryGetMethod == null || this.cachedBubbleClientServiceType == null)
                {
                    float now = Time.unscaledTime;
                    if (now < this.nextBubbleClientServiceResolveAttemptAt)
                    {
                        status = "Bubble service resolve throttled";
                        this.BubbleRadarLogThrottled("service-throttled", "Bubble service resolve throttled.", 4f);
                        return false;
                    }

                    this.nextBubbleClientServiceResolveAttemptAt = now + BubbleRadarServiceResolveRetryInterval;

                    Type ecsServiceType = this.FindLoadedType(
                        "XDTDataAndProtocol.ProtocolService.EcsService",
                        "Il2CppXDTDataAndProtocol.ProtocolService.EcsService",
                        "EcsService")
                        ?? this.FindLoadedTypeBySuffix("ProtocolService.EcsService", "EcsService")
                        ?? this.FindLoadedEcsServiceType();
                    Type bubbleServiceType = this.FindLoadedType(
                        "XDTDataAndProtocol.ProtocolService.Bubble.IBubbleService",
                        "Il2CppXDTDataAndProtocol.ProtocolService.Bubble.IBubbleService",
                        "IBubbleService",
                        "ClientSystem.Bubble.BubbleClientService",
                        "Il2CppClientSystem.Bubble.BubbleClientService",
                        "BubbleClientService")
                        ?? this.FindLoadedTypeBySuffix("Bubble.IBubbleService", "IBubbleService", "Bubble.BubbleClientService", "BubbleClientService")
                        ?? this.FindLoadedBubbleServiceType();
                    if (ecsServiceType == null || bubbleServiceType == null)
                    {
                        status = "Bubble service types unavailable";
                        this.BubbleRadarLogThrottled("service-types-missing", "Bubble service types unavailable. ecs=" + (ecsServiceType != null ? ecsServiceType.FullName : "null") + " bubble=" + (bubbleServiceType != null ? bubbleServiceType.FullName : "null"), 8f);
                        return false;
                    }

                    MethodInfo tryGetMethod = ecsServiceType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "TryGet" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                    if (tryGetMethod == null)
                    {
                        status = "EcsService.TryGet unavailable";
                        this.nextBubbleClientServiceResolveAttemptAt = now + BubbleRadarServiceResolveRetryInterval;
                        this.BubbleRadarLogThrottled("service-tryget-missing", "EcsService.TryGet unavailable for bubble radar.", 8f);
                        return false;
                    }

                    this.cachedBubbleClientServiceType = bubbleServiceType;
                    this.cachedBubbleClientServiceTryGetMethod = tryGetMethod.MakeGenericMethod(bubbleServiceType);
                    this.nextBubbleClientServiceResolveAttemptAt = -999f;
                    this.BubbleRadarLog("Resolved bubble service types. ecs=" + ecsServiceType.FullName + " bubble=" + bubbleServiceType.FullName);
                }

                object[] serviceArgs = new object[] { null, true };
                object serviceResult = this.cachedBubbleClientServiceTryGetMethod.Invoke(null, serviceArgs);
                if (!(serviceResult is bool) || !(bool)serviceResult || serviceArgs[0] == null)
                {
                    status = "IBubbleService unavailable";
                    this.BubbleRadarLogThrottled("service-unavailable", "IBubbleService unavailable.", 4f);
                    return false;
                }

                bubbleService = serviceArgs[0];
                status = "Bubble service ready";
                this.BubbleRadarLogThrottled("service-ready", "Bubble service ready via " + bubbleService.GetType().FullName, 12f);
                return true;
            }
            catch (Exception ex)
            {
                status = "Bubble service resolve failed: " + ex.GetType().Name;
                this.BubbleRadarLog("Bubble service resolve failed: " + ex.GetType().Name + " - " + ex.Message);
                return false;
            }
        }

        private bool TryGetAllSpawnedBubblePositionsAuraMono(Dictionary<int, Vector3> positions, out string status)
        {
            status = "Aura bubble scan unavailable";
            if (positions == null)
            {
                return false;
            }

            positions.Clear();

            // Direct-ECS bubble source: enumerate BubbleComponent objects via Entities.GetComponents<T>
            // (no entity-graph walk → no native AV) and read each bubble's radar marker + position from
            // its owner entity. Safe replacement for the former TryEnumerateAuraMonoLoadedEntityObjects
            // walk; covers AuraMono-only builds where the GM service and managed GetComponents both fail.
            if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                status = "Aura bubble scan unavailable: GetComponents not ready";
                return false;
            }

            IntPtr bubbleClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.Bubble.BubbleComponent");
            if (bubbleClass == IntPtr.Zero)
            {
                bubbleClass = this.FindAuraMonoClassByFullName("ScriptsRefactory.LevelAndEntity.Gameplay.Component.Bubble.BubbleComponent");
            }

            if (bubbleClass == IntPtr.Zero)
            {
                status = "Aura bubble scan unavailable: BubbleComponent class missing";
                return false;
            }

            try
            {
                // Pin components + each derived entity across the field reads (moving sgen GC stale-pointer
                // guard): an unpinned component/entity relocated mid-loop -> bad read -> heap corruption.
                List<uint> bubblePins = new List<uint>();
                if (!this.TryAuraMonoGetComponentObjects(bubbleClass, out List<IntPtr> bubbleComponents, bubblePins) || bubbleComponents == null)
                {
                    FreeAuraMonoPins(bubblePins);
                    status = "Aura bubble scan: GetComponents<BubbleComponent> returned no bubbles";
                    return false;
                }

                int inspected = 0;
                int bubbleMatches = 0;
                try
                {
                    for (int i = 0; i < bubbleComponents.Count; i++)
                    {
                        IntPtr componentObj = bubbleComponents[i];
                        if (componentObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        inspected++;

                        // Bubble entity is the component's back-reference.
                        IntPtr entityObj = IntPtr.Zero;
                        if ((!this.TryGetMonoObjectMember(componentObj, "entity", out entityObj) || entityObj == IntPtr.Zero)
                            && (!this.TryGetMonoObjectMember(componentObj, "_entity", out entityObj) || entityObj == IntPtr.Zero))
                        {
                            continue;
                        }

                        uint entityPin = AuraMonoPinNew(entityObj);
                        try
                        {
                            if (!this.TryResolveAuraMonoBubbleEntityMarker(entityObj, out int markerId, out Vector3 bubblePos))
                            {
                                continue;
                            }

                            positions[markerId] = bubblePos;
                            bubbleMatches++;
                        }
                        finally
                        {
                            AuraMonoPinFree(entityPin);
                        }
                    }
                }
                finally
                {
                    FreeAuraMonoPins(bubblePins);
                }

                status = bubbleMatches > 0
                    ? $"Aura bubble scan resolved {bubbleMatches}/{inspected} bubble(s) via GetComponents"
                    : (inspected > 0 ? $"Aura bubble scan inspected {inspected} bubble component(s) but resolved none" : "Aura bubble GetComponents empty");
                this.BubbleRadarLog("Aura bubble scan via GetComponents<BubbleComponent>: inspected=" + inspected + " resolved=" + bubbleMatches);
                return bubbleMatches > 0;
            }
            catch (Exception ex)
            {
                status = "Aura bubble scan exception: " + ex.GetType().Name + ": " + ex.Message;
                this.BubbleRadarLog("Aura bubble scan exception: " + ex.GetType().Name + " - " + ex.Message);
                return false;
            }
        }

        private bool TryGetAllSpawnedBubblePositionsAuraMonoSafe(Dictionary<int, Vector3> positions, out string status)
        {
            status = "Aura bubble scan throttled";
            if (positions == null)
            {
                return false;
            }

            float now = Time.unscaledTime;
            if (this.bubbleRadarActivatedAt > 0f && now - this.bubbleRadarActivatedAt < BubbleRadarAuraInitialSettleDelay)
            {
                status = "Aura bubble scan waiting for world settle";
                this.BubbleRadarLogThrottled("aura-scan-settle", "Aura bubble scan waiting for world settle.", 4f);
                return false;
            }

            if (now < this.nextAuraBubbleScanAttemptAt)
            {
                this.BubbleRadarLogThrottled("aura-scan-throttled", "Aura bubble scan throttled.", 6f);
                return false;
            }

            bool resolved = this.TryGetAllSpawnedBubblePositionsAuraMono(positions, out status);
            if (resolved)
            {
                this.lastAuraBubbleScanSuccessAt = now;
                this.lastAuraBubbleScanFailureAt = -999f;
                this.bubbleRadarAuraConsecutiveFailures = 0;
                this.nextAuraBubbleScanAttemptAt = now + BubbleRadarAuraSuccessRefreshInterval;
                return true;
            }

            this.lastAuraBubbleScanFailureAt = now;
            this.bubbleRadarAuraConsecutiveFailures = Mathf.Min(this.bubbleRadarAuraConsecutiveFailures + 1, 6);
            float retryDelay = Mathf.Min(
                BubbleRadarAuraRetryInterval * Mathf.Pow(1.5f, this.bubbleRadarAuraConsecutiveFailures - 1),
                BubbleRadarAuraMaxFailureBackoff);
            this.nextAuraBubbleScanAttemptAt = now + retryDelay;
            return false;
        }

        private bool TryResolveAuraMonoBubbleLocation(IntPtr componentObj, out Vector3 position)
        {
            position = Vector3.zero;
            if (componentObj == IntPtr.Zero)
            {
                return false;
            }

            foreach (string memberName in new[] { "value", "Value", "position", "Position", "worldPosition", "WorldPosition" })
            {
                if (this.TryGetMonoVector3Member(componentObj, memberName, out position) && position != Vector3.zero)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryResolveAuraMonoBubbleLocationFromNestedData(IntPtr componentObj, out Vector3 position)
        {
            position = Vector3.zero;
            if (componentObj == IntPtr.Zero)
            {
                return false;
            }

            foreach (string dataMemberName in new[] { "ComponentData", "_componentData", "componentData", "data", "_data" })
            {
                if (!this.TryGetMonoObjectMember(componentObj, dataMemberName, out IntPtr dataObj) || dataObj == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryResolveAuraMonoBubbleLocation(dataObj, out position))
                {
                    return true;
                }
            }

            return false;
        }

        private Type ResolveBubbleEntityListElementType(Type listType)
        {
            if (listType == null)
            {
                return null;
            }

            if (listType.IsGenericType)
            {
                Type[] args = listType.GetGenericArguments();
                if (args.Length == 1)
                {
                    return args[0];
                }
            }

            Type listInterface = listType.GetInterfaces()
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IList<>));
            if (listInterface != null)
            {
                Type[] args = listInterface.GetGenericArguments();
                if (args.Length == 1)
                {
                    return args[0];
                }
            }

            return null;
        }

        private bool TryGetBubbleOptComponentValue(object bubbleOptData, string propertyName, Type componentType, out object componentValue)
        {
            componentValue = null;
            if (bubbleOptData == null || componentType == null)
            {
                return false;
            }

            try
            {
                PropertyInfo componentProperty = null;
                if (propertyName == "BubbleLocationComponent")
                {
                    if (this.cachedBubbleOptDataLocationProperty == null)
                    {
                        this.cachedBubbleOptDataLocationProperty = this.cachedBubbleOptDataType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                    }
                    componentProperty = this.cachedBubbleOptDataLocationProperty;
                }
                else if (propertyName == "BubbleIdComponent")
                {
                    if (this.cachedBubbleOptDataIdProperty == null)
                    {
                        this.cachedBubbleOptDataIdProperty = this.cachedBubbleOptDataType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                    }
                    componentProperty = this.cachedBubbleOptDataIdProperty;
                }
                else
                {
                    componentProperty = this.cachedBubbleOptDataType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                }

                if (componentProperty == null)
                {
                    return false;
                }

                object entityDataOptObj = componentProperty.GetValue(bubbleOptData, null);
                if (entityDataOptObj == null)
                {
                    return false;
                }

                if (this.cachedEntityDataOptTryGetValueMethod == null)
                {
                    Type entityDataOptType = this.FindLoadedType("XDT.Scene.Shared.Entity.EntityOptData.EntityDataOpt", "EntityDataOpt");
                    if (entityDataOptType == null)
                    {
                        return false;
                    }

                    this.cachedEntityDataOptTryGetValueMethod = entityDataOptType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "TryGetValue" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                }

                if (this.cachedEntityDataOptTryGetValueMethod == null)
                {
                    return false;
                }

                object[] args = new object[] { entityDataOptObj, null };
                object result = this.cachedEntityDataOptTryGetValueMethod.MakeGenericMethod(componentType).Invoke(null, args);
                if (result is bool && (bool)result && args[1] != null)
                {
                    componentValue = args[1];
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool ShouldRetainEmptyBubbleSceneSnapshot(Vector3 scanOrigin)
        {
            return this.bubbleRadarSnapshotPositions.Count > 0;
        }

    }
}
