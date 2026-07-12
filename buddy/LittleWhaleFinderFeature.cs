using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Little Whale Finder — locate the daily "找到小鲸鱼" (Find the Little Whale) photo figurine.
    //
    // Mechanic (cn tables, 2026-07-12): 11 daily tasks 1106401-1106411, one per figurine style
    // 302565-302575 (喷水小鲸鱼 in 11 colors). While the day's task is active (TaskState >= 3) the
    // matching MapDynamicResource row 300023-300033 spawns THAT figurine at its own mapPosId
    // (10187-10197) — 11 fixed hiding spots in the underwater level; the daily "movement" is the
    // style<->spot rotation. Completion = TakePhoto of the figurine's staticId. The game gives NO
    // map hint (trackMarks empty — deliberate hide-and-seek).
    //
    // Finder: the client registers every spawned dynamic map item in DynamicObjectManager
    // (ScriptsRefactory.LevelAndEntity.BaseSystem.LevelScene, a Managers module): _dynamicObj maps
    // dynamicConfigId -> netId, and GetDynamicObject(int configId) resolves the live Entity. We
    // resolve the manager via the Managers._moduleDic[Type].module walk (the safe _serviceDic
    // pattern from CorruptionCleanseFeature — mono_type_get_object key, reference-type TryGetValue
    // out, NO Type.GetType, NO generic invoke), then probe GetDynamicObject(300023..300033); the
    // first non-null entity is today's figurine — read its `position` member. Marker/notification/
    // teleport mirror the Rainbow Whale tracker.
    public partial class HeartopiaComplete
    {
        private const int LittleWhaleConfigIdFirst = 300023; // MapDynamicResource ids, styles 1-11
        private const int LittleWhaleConfigIdLast = 300033;
        private const int LittleWhaleTaskIdFirst = 1106401;  // daily "找到小鲸鱼" task per style
        private const int LittleWhaleTaskIdLast = 1106411;
        private const int LittleWhaleEntityIdFirst = 302565; // style-1 figurine staticId
        private const float LittleWhalePollInterval = 3f;
        private const int LittleWhaleMissesToLose = 3;

        private const string DynamicObjectManagerTypeName =
            "ScriptsRefactory.LevelAndEntity.BaseSystem.LevelScene.DynamicObjectManager";

        // Toggle (persisted; default OFF). Drawn in the Auto Sea Clean tab.
        private bool littleWhaleFinderEnabled;

        private bool littleWhalePresent;
        private Vector3 littleWhaleLastPos;
        private int littleWhaleActiveConfigId;
        private int littleWhaleMissStreak;
        private float littleWhaleNextPollAt;
        private string littleWhaleLastStatus = string.Empty;

        // Game-map pin (button): a Furniture-type map TRACK at the figurine position showing the
        // figurine's own item icon — dispatched directly via DispatchStartTrack with a reserved
        // token, so it works regardless of radar state/display mode. Session-only (not persisted);
        // the sync loop never touches it (it only removes tokens it put into mapTrackInjected).
        private const ulong LittleWhaleMapPinToken = 0x4C57484C50494E01UL; // reserved, no collision with marker tokens
        private bool littleWhaleMapPinActive;
        private Vector3 littleWhaleMapPinPos;
        private int littleWhaleMapPinWorldEpoch;

        // Called every frame from OnUpdate; self-throttled to one probe pass per 3s.
        private void ProcessLittleWhaleFinderOnUpdate()
        {
            if (!this.littleWhaleFinderEnabled)
            {
                if (this.littleWhalePresent)
                {
                    this.littleWhalePresent = false;
                    this.littleWhaleMissStreak = 0;
                }
                this.RemoveLittleWhaleMapPin(false);
                return;
            }

            // World change wipes the game's track list — just forget the pin, never dispatch into
            // a torn-down world.
            if (this.littleWhaleMapPinActive && this.littleWhaleMapPinWorldEpoch != HeartopiaComplete.AuraMonoWorldEpoch)
            {
                this.littleWhaleMapPinActive = false;
            }

            float now = Time.unscaledTime;
            if (now < this.littleWhaleNextPollAt)
            {
                return;
            }
            this.littleWhaleNextPollAt = now + LittleWhalePollInterval;

            // World gate (same startup-crash class as the Rainbow Whale tracker): never touch the
            // task system / module registry from the main menu — no local player = no world.
            if (this.GetPlayer() == null)
            {
                this.littleWhaleMissStreak = 0;
                return;
            }

            bool found;
            Vector3 pos = Vector3.zero;
            int configId = 0;
            try
            {
                found = this.TryLocateLittleWhale(out pos, out configId, out this.littleWhaleLastStatus);
            }
            catch (Exception ex)
            {
                found = false;
                this.littleWhaleLastStatus = "probe exception: " + ex.GetType().Name;
            }

            if (found)
            {
                this.littleWhaleMissStreak = 0;
                this.littleWhaleLastPos = pos;
                this.littleWhaleActiveConfigId = configId;
                if (!this.littleWhalePresent)
                {
                    this.littleWhalePresent = true;
                    this.AddMenuNotification(this.L("Little Whale figurine located!"), new Color(1f, 0.65f, 0.85f));
                    ModLogger.Msg("[LittleWhale] figurine located: config=" + configId
                        + " style=" + (configId - LittleWhaleConfigIdFirst + 1) + " pos=" + pos.ToString("F1"));
                }

                // Keep an active pin on the refined position (StartTrack re-dispatch with the same
                // token overwrites the entry).
                if (this.littleWhaleMapPinActive && (pos - this.littleWhaleMapPinPos).sqrMagnitude > 1f
                    && this.DispatchStartTrack(LittleWhaleMapPinToken, pos, MapTrackTypeFurniture,
                        LittleWhaleEntityIdFirst + (configId - LittleWhaleConfigIdFirst), 0u))
                {
                    this.littleWhaleMapPinPos = pos;
                }
                return;
            }

            if (this.littleWhalePresent && ++this.littleWhaleMissStreak >= LittleWhaleMissesToLose)
            {
                this.littleWhalePresent = false;
                this.littleWhaleMissStreak = 0;
                this.RemoveLittleWhaleMapPin(false);
                ModLogger.Msg("[LittleWhale] figurine no longer resolves (" + this.littleWhaleLastStatus + ").");
            }
        }

        // Button: pin/unpin the figurine on the game map. The pin is a Furniture map track carrying
        // the figurine's own item staticId, so the big map shows the actual whale-figurine icon
        // (same mechanism as the Contaminated seashell pin).
        private void ToggleLittleWhaleMapPin()
        {
            if (this.littleWhaleMapPinActive)
            {
                this.RemoveLittleWhaleMapPin(true);
                return;
            }

            if (!this.littleWhalePresent)
            {
                this.AddMenuNotification(this.L("Little Whale figurine is not located."), new Color(1f, 0.7f, 0.45f));
                return;
            }

            if (!this.EnsureMapTrackReady() || !this.AttachAuraMonoThread())
            {
                this.AddMenuNotification(this.L("Map track system is not ready."), new Color(1f, 0.7f, 0.45f));
                return;
            }

            int figurineStaticId = LittleWhaleEntityIdFirst + (this.littleWhaleActiveConfigId - LittleWhaleConfigIdFirst);
            if (this.DispatchStartTrack(LittleWhaleMapPinToken, this.littleWhaleLastPos, MapTrackTypeFurniture, figurineStaticId, 0u))
            {
                this.littleWhaleMapPinActive = true;
                this.littleWhaleMapPinPos = this.littleWhaleLastPos;
                this.littleWhaleMapPinWorldEpoch = HeartopiaComplete.AuraMonoWorldEpoch;
                this.AddMenuNotification(this.L("Figurine pinned on the game map."), new Color(1f, 0.65f, 0.85f));
            }
            else
            {
                this.AddMenuNotification(this.L("Map track system is not ready."), new Color(1f, 0.7f, 0.45f));
            }
        }

        private void RemoveLittleWhaleMapPin(bool notify)
        {
            if (!this.littleWhaleMapPinActive)
            {
                return;
            }

            this.littleWhaleMapPinActive = false;
            try
            {
                if (this.AttachAuraMonoThread())
                {
                    this.DispatchStopTrack(LittleWhaleMapPinToken);
                }
            }
            catch
            {
            }

            if (notify)
            {
                this.AddMenuNotification(this.L("Figurine unpinned from the map."), new Color(0.8f, 0.8f, 0.8f));
            }
        }

        // Locate TODAY'S figurine. Completed prior-day tasks keep their figurine spawned (the
        // appearCondition is TaskState >= 3, which stays true after completion — live bug
        // 2026-07-12: the finder locked onto yesterday's already-photographed figurine), so the
        // spawn probe alone is ambiguous. Primary path: read the ACTIVE daily task
        // (1106401-1106411, state Accepted/CanSubmit) and probe ONLY its config id. Fallback when
        // the task list is unreadable: probe all 11 — trust a SINGLE hit, refuse ambiguity.
        private unsafe bool TryLocateLittleWhale(out Vector3 pos, out int configId, out string status)
        {
            pos = Vector3.zero;
            configId = 0;

            bool taskKnown = this.TryGetTodaysLittleWhaleConfigId(out int todaysConfigId, out bool taskListReadable, out string taskStatus);

            // The task list is AUTHORITATIVE: when it reads fine and holds no active whale daily
            // (not taken yet, or already completed today), there is nothing to find — do NOT fall
            // through to the spawn probe. Entity resolution is AOI-bound, so near a lingering
            // prior-day figurine the probe sees exactly one "spawned" figurine and false-locates it
            // (live report 2026-07-12: returning to yesterday's spot re-notified "located").
            if (!taskKnown && taskListReadable)
            {
                status = "no active Find-the-Little-Whale daily (not taken, or already done today)";
                return false;
            }

            if (!this.TryResolveDynamicObjectManagerAura(out IntPtr managerObj, out uint managerPin, out status))
            {
                return false;
            }

            try
            {
                IntPtr managerClass = auraMonoObjectGetClass(managerObj);
                IntPtr getMethod = managerClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(managerClass, "GetDynamicObject", 1)
                    : IntPtr.Zero;
                if (getMethod == IntPtr.Zero)
                {
                    status = "GetDynamicObject method unresolved";
                    return false;
                }

                if (taskKnown)
                {
                    if (this.TryProbeLittleWhaleConfig(managerObj, getMethod, todaysConfigId, out pos))
                    {
                        configId = todaysConfigId;
                        status = "ok (task-matched)";
                        return true;
                    }

                    status = "today's task active but its figurine is not spawned/streamed yet";
                    return false;
                }

                // Task list unreadable — probe everything, but only trust an UNAMBIGUOUS result.
                int hits = 0;
                for (int id = LittleWhaleConfigIdFirst; id <= LittleWhaleConfigIdLast; id++)
                {
                    if (this.TryProbeLittleWhaleConfig(managerObj, getMethod, id, out Vector3 hitPos))
                    {
                        hits++;
                        pos = hitPos;
                        configId = id;
                    }
                }

                if (hits == 1)
                {
                    status = "ok (single spawn, task state unavailable: " + taskStatus + ")";
                    return true;
                }

                configId = 0;
                pos = Vector3.zero;
                status = hits == 0
                    ? "no figurine spawned (daily task not taken / not in the underwater level)"
                    : hits + " figurines spawned (stale prior-day ones linger) and the task list is unreadable: " + taskStatus;
                return false;
            }
            finally
            {
                AuraMonoPinFree(managerPin);
            }
        }

        // One GetDynamicObject(configId) probe -> entity position. Entity pinned across the read.
        private unsafe bool TryProbeLittleWhaleConfig(IntPtr managerObj, IntPtr getMethod, int configIdToProbe, out Vector3 pos)
        {
            pos = Vector3.zero;
            int localId = configIdToProbe;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&localId); // value-type arg = pointer to the value
            IntPtr exc = IntPtr.Zero;
            IntPtr entityObj = auraMonoRuntimeInvoke(getMethod, managerObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || entityObj == IntPtr.Zero)
            {
                return false;
            }

            uint entityPin = AuraMonoPinNew(entityObj);
            try
            {
                return this.TryGetMonoVector3Member(entityObj, "position", out pos);
            }
            finally
            {
                AuraMonoPinFree(entityPin);
            }
        }

        // Today's style = the ACTIVE "Find the Little Whale" daily in the task list: TaskId in
        // [1106401..1106411] with TaskState Accepted(3) or CanSubmit(4). Reuses the quest
        // assistant's TaskSystem resolve + GetAllTasks (same enumeration + pin discipline as
        // QuestAssistantResolveSnapshot). taskListReadable separates "the list read fine but holds
        // no active whale daily" (authoritative NO — daily not taken / already done) from "the list
        // could not be read at all" (only then may the caller fall back to spawn probing).
        private bool TryGetTodaysLittleWhaleConfigId(out int configId, out bool taskListReadable, out string status)
        {
            configId = 0;
            taskListReadable = false;
            status = "AuraMono unavailable";
            // Same readiness gate QuestAssistantResolveSnapshot applies before touching the task
            // system — invoking mono methods without the api/thread attach is a hard crash.
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.QuestAssistantEnsureTaskSystem(out IntPtr taskSystemObj, out status) || taskSystemObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr listObj = auraMonoRuntimeInvoke(this.questAssistantGetAllTasksMethod, taskSystemObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
            {
                status = "GetAllTasks failed";
                return false;
            }

            List<IntPtr> taskItems = new List<IntPtr>();
            List<uint> taskPins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, taskItems, taskPins))
                {
                    status = "task list empty";
                    return false;
                }

                taskListReadable = true;

                for (int i = 0; i < taskItems.Count; i++)
                {
                    IntPtr item = taskItems[i];
                    if (item == IntPtr.Zero
                        || !this.TryGetMonoObjectMember(item, "taskItemComponent", out IntPtr boxedComponent)
                        || boxedComponent == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint componentPin = AuraMonoPinNew(boxedComponent);
                    try
                    {
                        if (!this.TryGetMonoIntMember(boxedComponent, "TaskId", out int taskId)
                            || taskId < LittleWhaleTaskIdFirst || taskId > LittleWhaleTaskIdLast)
                        {
                            continue;
                        }

                        int state = this.TryGetMonoIntMember(boxedComponent, "TaskState", out int s) ? s : -1;
                        if (state == QuestAssistantStateAccepted || state == QuestAssistantStateCanSubmit)
                        {
                            configId = LittleWhaleConfigIdFirst + (taskId - LittleWhaleTaskIdFirst);
                            status = "ok (taskId=" + taskId + " state=" + state + ")";
                            return true;
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(componentPin);
                    }
                }

                status = "no active Find-the-Little-Whale daily in the task list";
                return false;
            }
            finally
            {
                FreeAuraMonoPins(taskPins);
            }
        }

        // Managers._moduleDic[typeof(DynamicObjectManager)].module — pin-for-pin mirror of the
        // proven _serviceDic walk (TryResolveCorruptionConfigManager): mono_type_get_object dict
        // key, Dictionary.TryGetValue with a REFERENCE-type out slot, plain member reads. Never
        // touches Type.GetType or generic invokes (the ViewModule crash traps). On success the
        // manager object is PINNED — the caller must AuraMonoPinFree(managerPin) in a finally.
        private unsafe bool TryResolveDynamicObjectManagerAura(out IntPtr managerObj, out uint managerPin, out string status)
        {
            managerObj = IntPtr.Zero;
            managerPin = 0U;
            status = "AuraMono unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null
                || auraMonoObjectGetClass == null || auraMonoFieldGetValueObject == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryCreateAuraMonoSystemTypeObjectFromClass(DynamicObjectManagerTypeName, out IntPtr typeObj) || typeObj == IntPtr.Zero)
            {
                status = "DynamicObjectManager Type object unresolved";
                return false;
            }

            IntPtr managersClass = this.FindAuraMonoClassByFullName("XDTGame.Framework.Managers");
            if (managersClass == IntPtr.Zero)
            {
                managersClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.Framework", "Managers");
            }

            IntPtr moduleDicField = managersClass != IntPtr.Zero ? this.FindAuraMonoFieldOnHierarchy(managersClass, "_moduleDic") : IntPtr.Zero;
            if (moduleDicField == IntPtr.Zero)
            {
                status = "Managers._moduleDic field unresolved";
                return false;
            }

            IntPtr moduleDicObj = auraMonoFieldGetValueObject(this.auraMonoRootDomain, moduleDicField, IntPtr.Zero);
            if (moduleDicObj == IntPtr.Zero)
            {
                status = "Managers._moduleDic is null (Managers not started?)";
                return false;
            }

            uint dicPin = AuraMonoPinNew(moduleDicObj);
            IntPtr moduleWrapperObj;
            try
            {
                IntPtr dicClass = auraMonoObjectGetClass(moduleDicObj);
                IntPtr tryGetValueMethod = dicClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(dicClass, "TryGetValue", 2) : IntPtr.Zero;
                if (tryGetValueMethod == IntPtr.Zero)
                {
                    status = "Dictionary.TryGetValue method missing";
                    return false;
                }

                IntPtr localModuleObj = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = typeObj;                    // Type key (reference) — object ptr directly.
                args[1] = (IntPtr)(&localModuleObj);  // out ModuleObject (reference out) — ptr to local.
                IntPtr exc = IntPtr.Zero;
                IntPtr result = auraMonoRuntimeInvoke(tryGetValueMethod, moduleDicObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "TryGetValue invoke exception";
                    return false;
                }

                bool got = result != IntPtr.Zero && this.TryUnboxMonoBoolean(result, out bool b) && b;
                if (!got || localModuleObj == IntPtr.Zero)
                {
                    status = "DynamicObjectManager not registered in _moduleDic (world not loaded?)";
                    return false;
                }

                moduleWrapperObj = localModuleObj;
            }
            finally
            {
                AuraMonoPinFree(dicPin);
            }

            uint wrapperPin = AuraMonoPinNew(moduleWrapperObj);
            try
            {
                if (!this.TryGetMonoObjectMember(moduleWrapperObj, "module", out managerObj) || managerObj == IntPtr.Zero)
                {
                    status = "ModuleObject.module (DynamicObjectManager) null";
                    managerObj = IntPtr.Zero;
                    return false;
                }

                managerPin = AuraMonoPinNew(managerObj);
                status = "ok";
                return true;
            }
            finally
            {
                AuraMonoPinFree(wrapperPin);
            }
        }

        // Teleport to the figurine (button in the Auto Sea Clean tab).
        private void StartLittleWhaleTeleport()
        {
            if (!this.littleWhalePresent)
            {
                this.AddMenuNotification(this.L("Little Whale figurine is not located."), new Color(1f, 0.7f, 0.45f));
                return;
            }

            this.TeleportToLocation(this.littleWhaleLastPos);
            this.AddMenuNotification(this.L("Teleported to the Little Whale figurine."), new Color(1f, 0.65f, 0.85f));
        }
    }
}
