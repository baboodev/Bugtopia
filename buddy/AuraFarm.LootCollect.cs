using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private bool auraFarmLootCollectEnabled = true;
        private float auraFarmLootCollectDistance = 100f;
        private const float AuraFarmLootCollectDistanceMin = 1f;
        private const float AuraFarmLootCollectDistanceMax = 500f;
        private const float AuraFarmLootResendCooldown = 5f;
        private const int AuraFarmLootBatchPerTick = 1;
        private const int AuraLootCollectedState = 4;

        private IntPtr auraLootComponentClass = IntPtr.Zero;
        private IntPtr auraMonoSendPickLootMethodPtr = IntPtr.Zero;
        private MethodInfo auraSendPickLootMethod;
        private MethodInfo auraLootGetWaitingMethod;
        private PropertyInfo auraLootCollectedProperty;
        private PropertyInfo auraLootEntityProperty;
        private Type auraLootComponentManagedType;
        private MethodInfo auraEntitiesGetComponentsForLootMethod;
        private bool auraLootResolverReady;
        private bool auraLootResolverFailed;
        private readonly Dictionary<uint, float> auraLootLastSendAtByNetId = new Dictionary<uint, float>(32);
        private readonly List<AuraLootCandidate> auraLootCandidateBuffer = new List<AuraLootCandidate>(64);
        private float auraLootNextCooldownCleanupAt;

        private struct AuraLootCandidate
        {
            public uint NetId;
            public float DistanceSqr;
        }

        private void UpdateAuraFarmLootCollect()
        {
            if (!this.auraFarmEnabled || !this.auraFarmLootCollectEnabled)
            {
                return;
            }

            if (!this.EnsureAuraLootCollectResolver())
            {
                return;
            }

            if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos) || playerPos == Vector3.zero)
            {
                return;
            }

            float now = Time.unscaledTime;
            float maxDist = Mathf.Clamp(this.auraFarmLootCollectDistance, AuraFarmLootCollectDistanceMin, AuraFarmLootCollectDistanceMax);
            float maxDistSqr = maxDist * maxDist;
            this.auraLootCandidateBuffer.Clear();

            if (!this.TryCollectAuraLootCandidates(playerPos, maxDistSqr, this.auraLootCandidateBuffer))
            {
                return;
            }

            if (this.auraLootCandidateBuffer.Count == 0)
            {
                return;
            }

            this.auraLootCandidateBuffer.Sort((a, b) => a.DistanceSqr.CompareTo(b.DistanceSqr));

            int sent = 0;
            for (int i = 0; i < this.auraLootCandidateBuffer.Count; i++)
            {
                if (sent >= AuraFarmLootBatchPerTick)
                {
                    break;
                }

                uint netId = this.auraLootCandidateBuffer[i].NetId;
                if (netId == 0U)
                {
                    continue;
                }

                if (this.auraLootLastSendAtByNetId.TryGetValue(netId, out float lastSendAt) && now - lastSendAt < AuraFarmLootResendCooldown)
                {
                    continue;
                }

                if (!this.TrySendAuraPickLoot(netId))
                {
                    continue;
                }

                this.auraLootLastSendAtByNetId[netId] = now;
                sent++;
            }

            if (now >= this.auraLootNextCooldownCleanupAt)
            {
                this.auraLootNextCooldownCleanupAt = now + 30f;
                this.CleanupAuraLootSendCooldownMap(now);
            }
        }

        private void CleanupAuraLootSendCooldownMap(float now)
        {
            if (this.auraLootLastSendAtByNetId.Count == 0)
            {
                return;
            }

            List<uint> expired = null;
            foreach (KeyValuePair<uint, float> entry in this.auraLootLastSendAtByNetId)
            {
                if (now - entry.Value < AuraFarmLootResendCooldown * 2f)
                {
                    continue;
                }

                if (expired == null)
                {
                    expired = new List<uint>(8);
                }

                expired.Add(entry.Key);
            }

            if (expired == null)
            {
                return;
            }

            for (int i = 0; i < expired.Count; i++)
            {
                this.auraLootLastSendAtByNetId.Remove(expired[i]);
            }
        }

        private bool TryCollectAuraLootCandidates(Vector3 playerPos, float maxDistSqr, List<AuraLootCandidate> output)
        {
            output.Clear();
            if (this.TryCollectAuraLootCandidatesManaged(playerPos, maxDistSqr, output))
            {
                return true;
            }

            return this.TryCollectAuraLootCandidatesAuraMono(playerPos, maxDistSqr, output);
        }

        private bool TryCollectAuraLootCandidatesManaged(Vector3 playerPos, float maxDistSqr, List<AuraLootCandidate> output)
        {
            if (this.auraLootComponentManagedType == null
                || this.auraEntitiesGetComponentsForLootMethod == null
                || this.auraLootCollectedProperty == null
                || this.auraLootGetWaitingMethod == null
                || this.auraLootEntityProperty == null)
            {
                return false;
            }

            try
            {
                Type listType = typeof(List<>).MakeGenericType(this.auraLootComponentManagedType);
                IList components = Activator.CreateInstance(listType) as IList;
                if (components == null)
                {
                    return false;
                }

                MethodInfo getComponents = this.auraEntitiesGetComponentsForLootMethod.MakeGenericMethod(this.auraLootComponentManagedType);
                getComponents.Invoke(null, new object[] { components });

                PropertyInfo entityNetIdProperty = null;
                PropertyInfo entityPositionProperty = null;

                for (int i = 0; i < components.Count; i++)
                {
                    object loot = components[i];
                    if (loot == null)
                    {
                        continue;
                    }

                    if (this.auraLootCollectedProperty.GetValue(loot) is bool collected && collected)
                    {
                        continue;
                    }

                    object waitingObj = this.auraLootGetWaitingMethod.Invoke(loot, null);
                    if (!(waitingObj is float waiting) || waiting <= 0f)
                    {
                        continue;
                    }

                    object entity = this.auraLootEntityProperty.GetValue(loot);
                    if (entity == null)
                    {
                        continue;
                    }

                    Type entityType = entity.GetType();
                    if (entityNetIdProperty == null)
                    {
                        entityNetIdProperty = entityType.GetProperty("netId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        entityPositionProperty = entityType.GetProperty("position", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    }

                    if (entityNetIdProperty == null || entityPositionProperty == null)
                    {
                        return output.Count > 0;
                    }

                    object netIdObj = entityNetIdProperty.GetValue(entity);
                    object posObj = entityPositionProperty.GetValue(entity);
                    if (!(netIdObj is uint netId) || netId == 0U || !(posObj is Vector3 lootPos))
                    {
                        continue;
                    }

                    float distSqr = (lootPos - playerPos).sqrMagnitude;
                    if (distSqr > maxDistSqr)
                    {
                        continue;
                    }

                    output.Add(new AuraLootCandidate { NetId = netId, DistanceSqr = distSqr });
                }

                return true;
            }
            catch
            {
                return output.Count > 0;
            }
        }

        private bool TryCollectAuraLootCandidatesAuraMono(Vector3 playerPos, float maxDistSqr, List<AuraLootCandidate> output)
        {
            if (this.auraLootComponentClass == IntPtr.Zero)
            {
                return false;
            }

            List<uint> compPins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.auraLootComponentClass, out List<IntPtr> components, compPins) || components == null)
                {
                    return false;
                }

                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr comp = components[i];
                    if (comp == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.TryGetMonoInt32Member(comp, "_state", out int state) && state == AuraLootCollectedState)
                    {
                        continue;
                    }

                    if (this.TryGetMonoBoolMember(comp, "collected", out bool collected) && collected)
                    {
                        continue;
                    }

                    float waiting = 0f;
                    if (!this.TryGetAuraLootWaitingForCollectingTime(comp, out waiting) || waiting <= 0f)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(comp, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint entityPin = AuraMonoPinNew(entityObj);
                    try
                    {
                        if (!this.TryGetMonoUInt32Member(entityObj, "netId", out uint netId) || netId == 0U)
                        {
                            continue;
                        }

                        if (!this.TryGetMonoVector3Member(entityObj, "position", out Vector3 lootPos))
                        {
                            continue;
                        }

                        float distSqr = (lootPos - playerPos).sqrMagnitude;
                        if (distSqr > maxDistSqr)
                        {
                            continue;
                        }

                        output.Add(new AuraLootCandidate { NetId = netId, DistanceSqr = distSqr });
                    }
                    finally
                    {
                        AuraMonoPinFree(entityPin);
                    }
                }

                return true;
            }
            finally
            {
                FreeAuraMonoPins(compPins);
            }
        }

        private bool TryGetAuraLootWaitingForCollectingTime(IntPtr lootComponent, out float waiting)
        {
            waiting = 0f;
            if (lootComponent == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoInt32Member(lootComponent, "_state", out int state)
                || (state != 1 && state != 2))
            {
                return false;
            }

            return this.TryGetMonoSingleMember(lootComponent, "_waitingForCollectingTime", out waiting) && waiting > 0f;
        }

        private bool EnsureAuraLootCollectResolver()
        {
            if (this.auraLootResolverReady)
            {
                return true;
            }

            if (this.auraLootResolverFailed)
            {
                return false;
            }

            this.auraLootComponentManagedType = this.FindLoadedType(
                "XDTLevelAndEntity.Gameplay.Component.Gather.LootComponent",
                "XDTLevelAndEntity.GamePlay.Component.Gather.LootComponent",
                "Il2CppXDTLevelAndEntity.Gameplay.Component.Gather.LootComponent",
                "LootComponent");

            if (this.auraLootComponentManagedType != null)
            {
                this.auraLootCollectedProperty = this.auraLootComponentManagedType.GetProperty("collected", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                this.auraLootEntityProperty = this.auraLootComponentManagedType.GetProperty("entity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                this.auraLootGetWaitingMethod = this.auraLootComponentManagedType.GetMethod("GetWaitingForCollectingTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            }

            Type entitiesType = this.auraEntitiesType ?? this.FindLoadedType(
                "XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities",
                "Il2CppXDTLevelAndEntity.BaseSystem.EntitiesManager.Entities",
                "Entities");
            if (entitiesType != null)
            {
                this.auraEntitiesGetComponentsForLootMethod = entitiesType
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetComponents" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            }

            Type resourceProtocolType = this.auraResourceProtocolManagerType ?? this.FindLoadedType(
                "XDTDataAndProtocol.ProtocolService.Resource.ResourceProtocolManager",
                "ResourceProtocolManager");
            if (resourceProtocolType != null)
            {
                this.auraSendPickLootMethod = resourceProtocolType.GetMethod(
                    "SendPickLootCommand",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new Type[] { typeof(uint) },
                    null);
            }

            if (this.auraLootComponentClass == IntPtr.Zero)
            {
                this.auraLootComponentClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.Gather.LootComponent");
                if (this.auraLootComponentClass == IntPtr.Zero)
                {
                    this.auraLootComponentClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GamePlay.Component.Gather.LootComponent");
                }
            }

            if (this.auraMonoSendPickLootMethodPtr == IntPtr.Zero
                && auraMonoClassFromName != null
                && auraMonoClassGetMethodFromName != null)
            {
                IntPtr dataImage = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
                if (dataImage != IntPtr.Zero)
                {
                    IntPtr resourceClass = auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.Resource", "ResourceProtocolManager");
                    if (resourceClass != IntPtr.Zero)
                    {
                        this.auraMonoSendPickLootMethodPtr = auraMonoClassGetMethodFromName(resourceClass, "SendPickLootCommand", 1);
                    }
                }
            }

            bool hasSendPath = this.auraSendPickLootMethod != null || this.auraMonoSendPickLootMethodPtr != IntPtr.Zero;
            bool hasScanPath = (this.auraLootComponentManagedType != null && this.auraEntitiesGetComponentsForLootMethod != null)
                || this.auraLootComponentClass != IntPtr.Zero;

            if (hasSendPath && hasScanPath)
            {
                this.auraLootResolverReady = true;
                return true;
            }

            this.auraLootResolverFailed = true;
            if (HeartopiaComplete.MasterLogAuraFarm)
            {
                ModLogger.Msg("[AuraFarm] Loot collect resolver failed. send=" + hasSendPath + " scan=" + hasScanPath);
            }

            return false;
        }

        private bool TrySendAuraPickLoot(uint lootNetId)
        {
            try
            {
                if (this.auraMonoSendPickLootMethodPtr != IntPtr.Zero && auraMonoRuntimeInvoke != null)
                {
                    return this.InvokeAuraMonoPickLoot(lootNetId);
                }

                if (this.auraSendPickLootMethod != null)
                {
                    this.auraSendPickLootMethod.Invoke(null, new object[] { lootNetId });
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (HeartopiaComplete.MasterLogAuraFarm)
                {
                    ModLogger.Msg("[AuraFarm] SendPickLootCommand failed: " + ex.Message);
                }
            }

            return false;
        }

        private unsafe bool InvokeAuraMonoPickLoot(uint lootNetId)
        {
            if (this.auraMonoSendPickLootMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            uint id = lootNetId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);
            auraMonoRuntimeInvoke(this.auraMonoSendPickLootMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        private void ResetAuraLootCollectRuntimeState()
        {
            this.auraLootLastSendAtByNetId.Clear();
            this.auraLootCandidateBuffer.Clear();
            this.auraLootNextCooldownCleanupAt = 0f;
        }
    }
}
