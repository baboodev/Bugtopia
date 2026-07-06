using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HeartopiaMod
{
    // "Spawn Vehicle" test page (New Features → Spawn Vehicle sub-tab). Lists every vehicle in the game's
    // TableCar master table and offers a per-row Spawn button so we can probe whether the server lets us
    // summon a vehicle the player does not own. The summon reuses VehicleBypassFeature's proven
    // TryVehicleBypassForceSummon (PlayerCallVehicle → CallVehicleCommand); the server answers async with a
    // CallVehicleEvent and, on rejection (VehicleNotHave/CarUnAvailable), shows the in-game "召唤载具失败" tip.
    public partial class HeartopiaComplete
    {
        private readonly List<SpawnVehicleRow> spawnVehicleRows = new List<SpawnVehicleRow>();
        private bool spawnVehicleListLoaded;
        private bool spawnVehicleGetOn;
        private string spawnVehicleStatus = "Idle. Load the vehicle list, then press Spawn on a row to test.";

        // EStorageType.Garage (EcsClient.XDT.Scene.Shared.Data.StaticPartial.EStorageType) — the player's
        // owned-vehicle inventory, same storage the real Vehicle Panel reads via
        // DataModule<BackPackSystem>.Instance.GetAllItem(EStorageType.Garage) (VehiclePanel.cs:80).
        private const int SpawnVehicleGarageStorageType = 3;
        private readonly HashSet<int> spawnVehicleOwnedStaticIds = new HashSet<int>();
        private bool spawnVehicleShowOwnedOnly = true;

        // Definitive Spawn result: VehicleClientSystem reacts to the wire CallVehicleEvent by calling
        // VehicleProtocolManager.ServerCallVehicleResult, which — depending on VehicleErrorCode — either
        // dispatches UpdateCurrentVehicle (Success) or a UITipEvent{tipId=10139} ("召唤载具失败", any other
        // non-success code) via the GLOBAL EventCenter.DispatchEvent<T> (hookable), UNLIKE the raw
        // CallVehicleEvent itself (fired through a separate _eventCenter.On<NetworkEvent<T>> bus this mod's
        // detour engine can't reach). VehicleErrorCode.AreaForbid is the one case that dispatches NEITHER —
        // a silent server-side reject, so a timeout after sending is read as "likely AreaForbid".
        private const float SpawnVehicleResultTimeoutSeconds = 3f;
        private int spawnVehicleAwaitingStaticId;
        private float spawnVehicleAwaitingSince = -1f;

        // "Live Vehicles" section: unlike the Garage/TableCar summon above (owner-gated CallVehicleCommand),
        // this targets vehicles that ALREADY EXIST as world entities (anyone's — another player's parked
        // car, an NPC's, or an event/GameplayVehicle). The normal in-game interact for this
        // (VehicleMainInteract/EnterVehicleTask, ilspy-dumps XDTLevelAndEntity.Gameplay.Interaction.Command)
        // has NO ownership check at all — only "is seat 0 already occupied" — so we call the same
        // VehicleProtocolManager.GetOnVehicle(vehicleNetId, seatIndex, levelObjectId) directly.
        private readonly List<LiveVehicleRow> liveVehicleRows = new List<LiveVehicleRow>();
        private bool liveVehicleListLoaded;
        private string liveVehicleStatus = "Idle. Scan to list vehicles currently loaded in the world (any owner).";

        private IntPtr liveVehicleComponentClass;
        private IntPtr liveVehicleGetCarConfigMethod;
        private IntPtr liveVehicleHavePassengerMethod;
        private bool liveVehicleClassesResolved;
        private bool liveVehicleClassesResolveFailed;

        private IntPtr liveVehicleGetOnMethod;
        private bool liveVehicleGetOnMethodResolved;
        private bool liveVehicleGetOnMethodResolveFailed;

        private bool liveVehicleTurnOnHookInstalled;

        // VehicleProtocolManager.ReCallVehicle(staticId, pos, yAxis, VehicleReCallCommandType) — the same
        // call the normal Vehicle Panel's "Recall" button uses (VehicleManager.ReCallVehicle(itemId) wraps
        // this with Vector3.zero/0f). Exposed here as a stuck-vehicle escape hatch: a vehicle you already
        // have out physically blocks the LOCAL, position-based space check every future summon runs
        // (VehicleUtility.CreateVehiclePosition — an OverlapBox/Raycast right in front of the player against
        // the Vehicle layer, ilspy-dumps XDTLevelAndEntity.ResHandle.Vehicle/VehicleUtility.cs), so "spawn
        // stopped working" after riding one is almost always this, not corrupted state — Recall (or just
        // walking away from the parked vehicle) clears it.
        private IntPtr spawnVehicleRecallMethod;
        private bool spawnVehicleRecallMethodResolved;
        private bool spawnVehicleRecallMethodResolveFailed;

        private struct SpawnVehicleRow
        {
            public int StaticId;
            public string Name;
        }

        private struct LiveVehicleRow
        {
            public uint NetId;
            public int StaticId;
            public string Name;
            public bool Seat0Free;
            public string OwnerName;
        }

        // Reads LevelEntityComponentData.ownerId (the summoning player's netId — baked in server-side by
        // ServerSpawnVehicle, see VehicleProtocolManager.cs) via managed reflection on DataCenter.
        // TryGetComponentData<T>(NetId, out T) — the SAME generic method/pattern HomelandFarmFeature.cs
        // already resolves for its own component types (proven working on this build for
        // XDTDataAndProtocol.ComponentsData types), just narrowly re-resolved here for
        // LevelEntityComponentData so this feature doesn't depend on HomelandFarm's broader readiness gate.
        // VehicleComponent/VehicleComponentData carry no owner field of their own (checked both), so this
        // reflection path is the only way to answer "whose Garage vehicle is this" for a live entity.
        private Type spawnVehicleDataCenterType;
        private Type spawnVehicleNetIdType;
        private Type spawnVehicleLevelEntityDataType;
        private MethodInfo spawnVehicleTryGetComponentDataMethodDef;
        private FieldInfo spawnVehicleOwnerIdField;
        private bool spawnVehicleOwnerReflectionResolved;
        private bool spawnVehicleOwnerReflectionResolveFailed;

        private float DrawSpawnVehicleTab(int startY)
        {
            const float left = 40f;
            const float contentWidth = 450f;
            float y = startY;

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 1f);
            GUI.Label(new Rect(left, y, contentWidth, 24f), "Spawn Vehicle", headerStyle);
            y += 30f;

            GUIStyle hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            hintStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.8f);
            GUI.Label(
                new Rect(left, y, contentWidth, 44f),
                "All vehicles in the game table (TableCar). Press Spawn to send a summon command to the "
                + "server. Vehicles you don't own are rejected server-side with a \"summon failed\" tip.",
                hintStyle);
            y += 48f;

            if (GUI.Button(
                    new Rect(left, y, 200f, 32f),
                    this.spawnVehicleListLoaded ? "Refresh List" : "Load Vehicle List",
                    this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                bool ok = this.TryLoadSpawnVehicleList(out string status);
                this.spawnVehicleStatus = status;
                this.AddMenuNotification(status, ok ? new Color(0.45f, 0.88f, 1f) : new Color(1f, 0.55f, 0.45f));
            }

            bool newGetOn = this.DrawSwitchToggle(
                new Rect(left + 220f, y + 4f, 250f, 25f),
                this.spawnVehicleGetOn,
                "Get on after spawn");
            if (newGetOn != this.spawnVehicleGetOn)
            {
                this.spawnVehicleGetOn = newGetOn;
            }
            y += 34f;

            bool newShowOwnedOnly = this.DrawSwitchToggle(
                new Rect(left, y, 260f, 25f),
                this.spawnVehicleShowOwnedOnly,
                "Show only vehicles I own");
            if (newShowOwnedOnly != this.spawnVehicleShowOwnedOnly)
            {
                this.spawnVehicleShowOwnedOnly = newShowOwnedOnly;
            }
            y += 34f;

            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true, fontStyle = FontStyle.Italic };
            statusStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.9f);
            GUI.Label(new Rect(left, y, contentWidth, 34f), this.spawnVehicleStatus, statusStyle);
            y += 38f;

            if (this.spawnVehicleListLoaded)
            {
                int visibleCount = this.CountVisibleSpawnVehicleRows();
                GUIStyle countStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
                countStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.95f);
                GUI.Label(
                    new Rect(left, y, contentWidth, 20f),
                    "Vehicles: " + visibleCount
                        + (this.spawnVehicleShowOwnedOnly ? (" owned (of " + this.spawnVehicleRows.Count + " total)") : string.Empty),
                    countStyle);
                y += 26f;

                GUIStyle rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
                rowStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.92f);
                for (int i = 0; i < this.spawnVehicleRows.Count; i++)
                {
                    SpawnVehicleRow row = this.spawnVehicleRows[i];
                    if (this.spawnVehicleShowOwnedOnly && !this.spawnVehicleOwnedStaticIds.Contains(row.StaticId))
                    {
                        continue;
                    }

                    string label = row.StaticId + "   " + (string.IsNullOrEmpty(row.Name) ? "(vehicle)" : row.Name);
                    GUI.Label(new Rect(left, y + 4f, contentWidth - 180f, 22f), label, rowStyle);
                    if (GUI.Button(
                            new Rect(left + contentWidth - 170f, y, 78f, 26f),
                            "Recall",
                            GUI.skin.button))
                    {
                        this.RecallVehicleById(row.StaticId, row.Name);
                    }
                    if (GUI.Button(
                            new Rect(left + contentWidth - 86f, y, 86f, 26f),
                            "Spawn",
                            this.themePrimaryButtonStyle ?? GUI.skin.button))
                    {
                        this.SpawnVehicleById(row.StaticId, row.Name);
                    }
                    y += 30f;
                }
            }

            y += 20f;
            GUI.DrawTexture(new Rect(left, y, contentWidth, 1f), Texture2D.whiteTexture);
            y += 16f;

            GUI.Label(new Rect(left, y, contentWidth, 24f), "Live Vehicles In World (any owner)", headerStyle);
            y += 30f;

            GUI.Label(
                new Rect(left, y, contentWidth, 58f),
                "Vehicles already spawned nearby/loaded — someone else's parked car, an NPC's, or an "
                + "event vehicle. The normal \"get in\" interact has no ownership check, only seat "
                + "availability, so this calls it directly. Getting on someone's vehicle is visible to them.",
                hintStyle);
            y += 62f;

            if (GUI.Button(
                    new Rect(left, y, 200f, 32f),
                    this.liveVehicleListLoaded ? "Rescan" : "Scan Live Vehicles",
                    this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                bool ok = this.TryScanLiveVehicles(out string status);
                this.liveVehicleStatus = status;
                this.AddMenuNotification(status, ok ? new Color(0.45f, 0.88f, 1f) : new Color(1f, 0.55f, 0.45f));
            }
            y += 42f;

            GUI.Label(new Rect(left, y, contentWidth, 34f), this.liveVehicleStatus, statusStyle);
            y += 38f;

            if (this.liveVehicleListLoaded)
            {
                GUIStyle countStyle2 = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
                countStyle2.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.95f);
                GUI.Label(new Rect(left, y, contentWidth, 20f), "Found: " + this.liveVehicleRows.Count, countStyle2);
                y += 26f;

                GUIStyle rowStyle2 = new GUIStyle(GUI.skin.label) { fontSize = 12 };
                rowStyle2.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.92f);
                GUIStyle seatStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Italic };
                for (int i = 0; i < this.liveVehicleRows.Count; i++)
                {
                    LiveVehicleRow row = this.liveVehicleRows[i];
                    string name = string.IsNullOrEmpty(row.Name) ? "(vehicle)" : row.Name;
                    string ownerSuffix = string.IsNullOrEmpty(row.OwnerName) ? string.Empty : ("   Owner: " + row.OwnerName);
                    string label = row.NetId + "   " + row.StaticId + "   " + name + ownerSuffix;
                    GUI.Label(new Rect(left, y + 1f, contentWidth - 106f, 18f), label, rowStyle2);
                    seatStyle.normal.textColor = row.Seat0Free
                        ? new Color(0.45f, 1f, 0.55f, 0.9f)
                        : new Color(1f, 0.6f, 0.5f, 0.85f);
                    GUI.Label(
                        new Rect(left, y + 17f, contentWidth - 106f, 16f),
                        row.Seat0Free ? "driver seat open" : "driver seat occupied",
                        seatStyle);
                    using (new GuiEnabledScope(row.Seat0Free))
                    {
                        if (GUI.Button(
                                new Rect(left + contentWidth - 96f, y + 2f, 96f, 26f),
                                "Get On",
                                this.themePrimaryButtonStyle ?? GUI.skin.button)
                            && row.Seat0Free)
                        {
                            this.TryGetOnLiveVehicle(row.NetId, row.StaticId, name);
                        }
                    }

                    y += 36f;
                }
            }

            return y + 30f;
        }

        // Row count actually drawn in the Garage/TableCar list, given the current owned-only filter —
        // used both for the "Vehicles: N" label and the Teleport-tab height estimate, so a filtered list
        // doesn't leave a wall of empty scroll space sized for the full ~350-row catalog.
        private int CountVisibleSpawnVehicleRows()
        {
            if (!this.spawnVehicleShowOwnedOnly)
            {
                return this.spawnVehicleRows.Count;
            }

            int count = 0;
            for (int i = 0; i < this.spawnVehicleRows.Count; i++)
            {
                if (this.spawnVehicleOwnedStaticIds.Contains(this.spawnVehicleRows[i].StaticId))
                {
                    count++;
                }
            }

            return count;
        }

        // Thin RAII-style helper so the "Get On" button can be visually/functionally disabled for
        // occupied seats without duplicating GUI.enabled save/restore at every call site.
        private readonly struct GuiEnabledScope : IDisposable
        {
            private readonly bool previous;

            public GuiEnabledScope(bool enabled)
            {
                this.previous = GUI.enabled;
                GUI.enabled = enabled;
            }

            public void Dispose()
            {
                GUI.enabled = this.previous;
            }
        }

        // Reads TableData.TableCars (Dictionary<int, TableCar>) via AuraMono only — TableData lives in the
        // EcsClient Mono image (per prefer-auramono-no-managed-fallback, no managed reflection path). Names
        // are resolved AFTER the collection pins are freed, using scalar staticIds only, so no moving-GC
        // stale-pointer risk (see AGENTS.md §11).
        private bool TryLoadSpawnVehicleList(out string status)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "AuraMono not ready (enter a town and retry).";
                return false;
            }

            IntPtr tableDataClass = this.FindAuraMonoTableDataClass();
            if (tableDataClass == IntPtr.Zero)
            {
                status = "TableData class unavailable (enter a town).";
                return false;
            }

            if (!this.TryGetAuraMonoStaticObjectField(tableDataClass, "TableCars", out IntPtr carsObj) || carsObj == IntPtr.Zero)
            {
                status = "TableCars unavailable (enter a town, wait for tables to load).";
                return false;
            }

            List<int> ids = new List<int>();
            List<IntPtr> items = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(carsObj, items, pins) || items.Count == 0)
                {
                    status = "No vehicles found in TableCars.";
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr entry = items[i];
                    if (entry == IntPtr.Zero)
                    {
                        continue;
                    }

                    int id = 0;
                    if (!this.TryGetMonoInt32Member(entry, "Key", out id) || id <= 0)
                    {
                        IntPtr value = this.TryGetMonoDictionaryEntryValue(entry);
                        if (value != IntPtr.Zero)
                        {
                            id = this.TryReadMonoIntMember(value, "id");
                        }
                    }

                    if (id > 0)
                    {
                        ids.Add(id);
                    }
                }
            }
            finally
            {
                for (int i = 0; i < pins.Count; i++)
                {
                    AuraMonoPinFree(pins[i]);
                }
            }

            if (ids.Count == 0)
            {
                status = "No vehicle ids read from TableCars.";
                return false;
            }

            ids.Sort();

            ItemDumpEntityNameResolver nameResolver = this.CreateItemDumpEntityNameResolver();
            this.spawnVehicleRows.Clear();
            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                string name = string.Empty;
                try
                {
                    name = nameResolver.Resolve(id);
                }
                catch
                {
                }

                this.spawnVehicleRows.Add(new SpawnVehicleRow { StaticId = id, Name = name });
            }

            this.spawnVehicleListLoaded = true;
            this.TryLoadSpawnVehicleOwnedIds(out string ownedStatus);
            status = "Loaded " + this.spawnVehicleRows.Count + " vehicles from TableCar. " + ownedStatus;
            return true;
        }

        // Reads the player's owned vehicles from EStorageType.Garage (BackPackSystem.GetAllItem), the same
        // storage the real Vehicle Panel reads (VehiclePanel.cs:80) — powers "Show only vehicles I own".
        // Failure here is soft: the Garage-read is a nice-to-have filter, not the catalog load itself, so a
        // failure just leaves spawnVehicleOwnedStaticIds empty (owned-only would show nothing — the toggle
        // can be switched off to fall back to the full catalog).
        private unsafe bool TryLoadSpawnVehicleOwnedIds(out string status)
        {
            this.spawnVehicleOwnedStaticIds.Clear();
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "(owned-vehicle check: AuraMono not ready)";
                return false;
            }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackObj)
                || backPackObj == IntPtr.Zero
                || auraMonoObjectGetClass == null
                || auraMonoRuntimeInvoke == null)
            {
                status = "(owned-vehicle check: BackPackSystem unavailable)";
                return false;
            }

            IntPtr backPackClass = auraMonoObjectGetClass(backPackObj);
            IntPtr getAllItemMethod = backPackClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1)
                : IntPtr.Zero;
            if (getAllItemMethod == IntPtr.Zero)
            {
                status = "(owned-vehicle check: GetAllItem unavailable)";
                return false;
            }

            int storageType = SpawnVehicleGarageStorageType;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&storageType);
            IntPtr exc = IntPtr.Zero;
            IntPtr itemListObj = auraMonoRuntimeInvoke(getAllItemMethod, backPackObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || itemListObj == IntPtr.Zero)
            {
                status = "(owned-vehicle check: Garage read failed)";
                return false;
            }

            List<IntPtr> items = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(itemListObj, items, pins))
                {
                    status = "(Garage is empty)";
                    return true;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] != IntPtr.Zero
                        && this.TryGetDirectBackpackItemStaticId(items[i], out int staticId)
                        && staticId > 0)
                    {
                        this.spawnVehicleOwnedStaticIds.Add(staticId);
                    }
                }
            }
            finally
            {
                for (int i = 0; i < pins.Count; i++)
                {
                    AuraMonoPinFree(pins[i]);
                }
            }

            status = "Garage: " + this.spawnVehicleOwnedStaticIds.Count + " owned.";
            return true;
        }

        private void SpawnVehicleById(int staticId, string name)
        {
            if (staticId <= 0)
            {
                return;
            }

            string label = string.IsNullOrEmpty(name) ? ("id=" + staticId) : (name + " (id=" + staticId + ")");
            bool sent;
            string error = string.Empty;
            try
            {
                sent = this.TryVehicleBypassForceSummon(staticId, this.spawnVehicleGetOn, out error);
            }
            catch (Exception ex)
            {
                sent = false;
                error = ex.Message;
            }

            if (sent)
            {
                this.spawnVehicleStatus = "Summon sent for " + label + " — waiting for server result...";
                this.AddMenuNotification("Summon sent: " + label, new Color(0.45f, 0.88f, 1f));
                this.spawnVehicleAwaitingStaticId = staticId;
                this.spawnVehicleAwaitingSince = Time.realtimeSinceStartup;
            }
            else
            {
                this.spawnVehicleStatus = "Summon failed for " + label + ": " + error;
                this.AddMenuNotification("Summon failed: " + label, new Color(1f, 0.55f, 0.45f));
                this.spawnVehicleAwaitingStaticId = 0;
                this.spawnVehicleAwaitingSince = -1f;
            }

            ModLogger.Msg("[SpawnVehicle] " + this.spawnVehicleStatus);
        }

        // UpdateCurrentVehicle/UITipEvent are both GLOBAL EventCenter.DispatchEvent<T> events (per
        // VehicleProtocolManager.ServerCallVehicleResult), hookable via the mod's existing NativeDetour
        // event engine (HeartopiaComplete.EventHook.cs) — unlike the raw wire CallVehicleEvent, which
        // VehicleClientSystem reacts to through a separate _eventCenter.On<NetworkEvent<T>> bus this
        // engine can't reach.
        private bool spawnVehicleUpdateHookInstalled;
        private bool spawnVehicleTipHookInstalled;

        // Called UNCONDITIONALLY from OnUpdate (not gated on the Spawn button/tab) — RegisterGameEventHook
        // only ADDS the entry; the actual NativeDetour attach happens lazily over later OnUpdate frames
        // (EnsureGameEventHooksInstalled: resolve class -> inflate generic -> mono_compile_method ->
        // NativeDetour). Registering it at Spawn-press time raced the server's reply: a fast Success could
        // dispatch UpdateCurrentVehicle before the detour finished attaching, so it was silently missed on
        // BOTH the underwater AND the (successful, vehicle-visibly-spawned) on-land test — both showed the
        // 3s timeout regardless of the real result. Registering early (mirrors EnsureNetCookEventHooks'
        // same unconditional-in-OnUpdate pattern) gives the installer many frames of head start before a
        // human can possibly click Spawn.
        private void EnsureSpawnVehicleResultHooks()
        {
            // Each hook is guarded independently — RegisterGameEventHook adds ANOTHER handler on every
            // call for a name already installed, so a combined all-or-nothing flag would re-register (and
            // double-fire) whichever one succeeded first if the other failed and this ran again later.
            if (!this.spawnVehicleUpdateHookInstalled)
            {
                this.spawnVehicleUpdateHookInstalled = this.RegisterGameEventHook(
                    "XDTDataAndProtocol.Events.UpdateCurrentVehicle",
                    12,
                    this.OnSpawnVehicleUpdateCurrentVehicleEvent);
            }

            // Only tipId (offset 0, 4 bytes) is ever read — UITipEvent's second field is a managed string
            // reference; the event-hook engine's handler runs deferred from a drained ring buffer, so by
            // the time it fires the original event (and that string) may already be collected. Never touch
            // it — 4 bytes keeps the snapshot from tempting a later reader into doing so.
            if (!this.spawnVehicleTipHookInstalled)
            {
                this.spawnVehicleTipHookInstalled = this.RegisterGameEventHook(
                    "ScriptsRefactory.DataAndProtocol.Events.UITipEvent",
                    4,
                    this.OnSpawnVehicleUiTipEvent);
            }
        }

        private void OnSpawnVehicleUpdateCurrentVehicleEvent(GameEventSnapshot snapshot)
        {
            // CONFIRMED live (2026-07-16, 3 clean tests, hook install verified True each time): this event
            // never actually dispatches for a real success on this server — TickSpawnVehicleResultTimeout's
            // world-scan is the real success signal now. Kept registered (harmless, near-zero cost) as a
            // fast-path shortcut in case that ever changes; logs every receipt unconditionally so a future
            // firing is visible immediately rather than silently swallowed by the match filter below.
            uint loggedNetId = snapshot.ReadUInt32(0);
            int loggedStaticId = snapshot.ReadInt32(4);
            uint loggedOwnerNetId = snapshot.ReadUInt32(8);
            ModLogger.Msg("[SpawnVehicle] UpdateCurrentVehicle received: vehicleNetId=" + loggedNetId
                + " vehicleStaticId=" + loggedStaticId + " ownerNetId=" + loggedOwnerNetId
                + " (awaiting=" + this.spawnVehicleAwaitingStaticId + ")");

            if (this.spawnVehicleAwaitingStaticId == 0 || loggedStaticId != this.spawnVehicleAwaitingStaticId)
            {
                return;
            }

            this.spawnVehicleAwaitingStaticId = 0;
            this.spawnVehicleAwaitingSince = -1f;
            this.spawnVehicleStatus = "Summon SUCCEEDED — vehicle netId=" + loggedNetId + ".";
            this.AddMenuNotification(this.spawnVehicleStatus, new Color(0.45f, 1f, 0.55f));
            ModLogger.Msg("[SpawnVehicle] " + this.spawnVehicleStatus);
        }

        // Vehicle-summon-failed tip (10139, "召唤载具失败"). UITipEvent fires for many unrelated tips
        // across the whole game — filtering to this exact tipId is the only thing that scopes it to us.
        private const int SpawnVehicleFailedTipId = 10139;

        private void OnSpawnVehicleUiTipEvent(GameEventSnapshot snapshot)
        {
            if (this.spawnVehicleAwaitingStaticId == 0)
            {
                return;
            }

            int tipId = snapshot.ReadInt32(0);
            if (tipId != SpawnVehicleFailedTipId)
            {
                return;
            }

            this.spawnVehicleAwaitingStaticId = 0;
            this.spawnVehicleAwaitingSince = -1f;
            this.spawnVehicleStatus = "Summon REJECTED by server (\"summon failed\" tip).";
            this.AddMenuNotification(this.spawnVehicleStatus, new Color(1f, 0.55f, 0.45f));
            ModLogger.Msg("[SpawnVehicle] " + this.spawnVehicleStatus);
        }

        // CONFIRMED live (2026-07-16, 3 clean tests): UpdateCurrentVehicle never dispatches for a real
        // success — hook confirmed installed, vehicle visibly spawned, zero receipts logged, every time.
        // The rejection tip fires instantly and reliably instead. Read as: this server only replies
        // explicitly on rejection and stays silent on success (the new entity is the only signal) — not a
        // hook bug. So on timeout, actively VERIFY via the already-proven Live Vehicles scan (matching
        // both staticId and self-ownership) instead of just guessing from silence alone.
        private void TickSpawnVehicleResultTimeout()
        {
            if (this.spawnVehicleAwaitingStaticId == 0)
            {
                return;
            }

            if (Time.realtimeSinceStartup - this.spawnVehicleAwaitingSince < SpawnVehicleResultTimeoutSeconds)
            {
                return;
            }

            int staticId = this.spawnVehicleAwaitingStaticId;
            this.spawnVehicleAwaitingStaticId = 0;
            this.spawnVehicleAwaitingSince = -1f;

            if (this.TryFindSelfOwnedLiveVehicleByStaticId(staticId, out uint vehicleNetId))
            {
                this.spawnVehicleStatus = "Summon SUCCEEDED (confirmed via world scan) — vehicle netId=" + vehicleNetId + ".";
                this.AddMenuNotification(this.spawnVehicleStatus, new Color(0.45f, 1f, 0.55f));
            }
            else
            {
                this.spawnVehicleStatus = "No server response after " + SpawnVehicleResultTimeoutSeconds
                    + "s, and no matching owned vehicle found in the world — likely silently rejected "
                    + "(VehicleErrorCode.AreaForbid dispatches no event).";
            }

            ModLogger.Msg("[SpawnVehicle] " + this.spawnVehicleStatus);
        }

        // Scans live VehicleComponents (reuses the Live Vehicles infra) for one matching staticId AND
        // owned by the local player — the definitive "did my summon actually work" check, since the
        // server apparently never confirms success over the wire (see comment above).
        private bool TryFindSelfOwnedLiveVehicleByStaticId(int staticId, out uint vehicleNetId)
        {
            vehicleNetId = 0;
            if (staticId <= 0 || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryEnsureLiveVehicleAuraMonoMethods()
                || !this.TryResolveSelfPlayerNetId(out uint selfNetId)
                || selfNetId == 0)
            {
                return false;
            }

            List<IntPtr> components = null;
            List<uint> pins = new List<uint>();
            bool found = false;
            uint foundNetId = 0;
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.liveVehicleComponentClass, out components, pins)
                    || components == null)
                {
                    return false;
                }

                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr componentObj = components[i];
                    if (componentObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.TryReadLiveVehicleStaticId(componentObj) != staticId)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(componentObj, "entity", out IntPtr entityObj)
                        || entityObj == IntPtr.Zero
                        || !this.TryGetMonoUInt32Member(entityObj, "netId", out uint netId)
                        || netId == 0)
                    {
                        continue;
                    }

                    // Reflection-based owner lookup (DataCenter.TryGetComponentData<LevelEntityComponentData>)
                    // allocates, but that only affects unpinned objects — componentObj/entityObj stay valid
                    // via `pins` regardless, and we're done with both before moving to the next candidate.
                    if (this.TryReadVehicleOwnerNetId(netId) == selfNetId)
                    {
                        found = true;
                        foundNetId = netId;
                        break;
                    }
                }
            }
            finally
            {
                for (int i = 0; i < pins.Count; i++)
                {
                    AuraMonoPinFree(pins[i]);
                }
            }

            vehicleNetId = foundNetId;
            return found;
        }

        private bool TryEnsureSpawnVehicleRecallMethod()
        {
            if (this.spawnVehicleRecallMethodResolved)
            {
                return this.spawnVehicleRecallMethod != IntPtr.Zero;
            }

            if (this.spawnVehicleRecallMethodResolveFailed)
            {
                return false;
            }

            IntPtr protocolClass = this.ResolveVehicleBypassClass(
                "XDTDataAndProtocol.ProtocolService.Vehicle",
                "VehicleProtocolManager",
                VehicleBypassProtocolImages,
                "XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager");
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager");
            }

            if (protocolClass != IntPtr.Zero)
            {
                this.spawnVehicleRecallMethod = this.FindVehicleBypassMethod(protocolClass, "ReCallVehicle", 4);
            }

            this.spawnVehicleRecallMethodResolved = true;
            if (this.spawnVehicleRecallMethod == IntPtr.Zero)
            {
                this.spawnVehicleRecallMethodResolveFailed = true;
                return false;
            }

            return true;
        }

        // VehicleProtocolManager.ReCallVehicle(staticId, Vector3.zero, 0f, VehicleReCallCommandType.Destroy)
        // — same effect as the normal Vehicle Panel's Recall button (VehicleManager.ReCallVehicle(itemId)
        // wraps this identically). Destroy = 0, the enum's first/default member.
        private unsafe void RecallVehicleById(int staticId, string name)
        {
            string label = string.IsNullOrEmpty(name) ? ("id=" + staticId) : (name + " (id=" + staticId + ")");
            if (staticId <= 0)
            {
                return;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    this.spawnVehicleStatus = "Recall failed for " + label + ": AuraMono not ready.";
                    return;
                }

                if (!this.TryEnsureSpawnVehicleRecallMethod())
                {
                    this.spawnVehicleStatus = "Recall failed for " + label + ": ReCallVehicle method unavailable.";
                    return;
                }

                int idLocal = staticId;
                Vector3 posLocal = Vector3.zero;
                float yAxisLocal = 0f;
                int reCallTypeLocal = 0; // VehicleReCallCommandType.Destroy
                IntPtr* args = stackalloc IntPtr[4];
                args[0] = (IntPtr)(&idLocal);
                args[1] = (IntPtr)(&posLocal);
                args[2] = (IntPtr)(&yAxisLocal);
                args[3] = (IntPtr)(&reCallTypeLocal);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.spawnVehicleRecallMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.spawnVehicleStatus = "Recall threw for " + label + ".";
                    this.AddMenuNotification("Recall threw: " + label, new Color(1f, 0.55f, 0.45f));
                    return;
                }

                this.spawnVehicleStatus = "Recall sent for " + label + " — if it was blocking a summon, try Spawn again.";
                this.AddMenuNotification("Recall sent: " + label, new Color(0.45f, 0.88f, 1f));
            }
            catch (Exception ex)
            {
                this.spawnVehicleStatus = "Recall failed for " + label + ": " + ex.Message;
            }

            ModLogger.Msg("[SpawnVehicle] " + this.spawnVehicleStatus);
        }

        // Resolves VehicleComponent (XDTLevelAndEntity image, same class VehicleBypassFeature/
        // VehicleTeleportFeature already use) plus the two zero/one-arg instance methods needed to read
        // each live component: GetCarConfig() -> TableCar (staticId) and HavePassengerInSeatIndex(int).
        private bool TryEnsureLiveVehicleAuraMonoMethods()
        {
            if (this.liveVehicleClassesResolved)
            {
                return this.liveVehicleComponentClass != IntPtr.Zero
                    && this.liveVehicleGetCarConfigMethod != IntPtr.Zero
                    && this.liveVehicleHavePassengerMethod != IntPtr.Zero;
            }

            if (this.liveVehicleClassesResolveFailed)
            {
                return false;
            }

            IntPtr componentClass = this.FindAuraMonoClassByFullName(
                "XDTLevelAndEntity.Gameplay.Component.Vehicle.VehicleComponent");
            if (componentClass == IntPtr.Zero)
            {
                componentClass = this.ResolveVehicleBypassClass(
                    "XDTLevelAndEntity.Gameplay.Component.Vehicle",
                    "VehicleComponent",
                    VehicleBypassLevelEntityImages,
                    "XDTLevelAndEntity.Gameplay.Component.Vehicle.VehicleComponent");
            }

            if (componentClass != IntPtr.Zero)
            {
                this.liveVehicleComponentClass = componentClass;
                this.liveVehicleGetCarConfigMethod = this.FindAuraMonoMethodOnHierarchy(componentClass, "GetCarConfig", 0);
                this.liveVehicleHavePassengerMethod = this.FindAuraMonoMethodOnHierarchy(componentClass, "HavePassengerInSeatIndex", 1);
            }

            this.liveVehicleClassesResolved = true;
            if (this.liveVehicleComponentClass == IntPtr.Zero
                || this.liveVehicleGetCarConfigMethod == IntPtr.Zero
                || this.liveVehicleHavePassengerMethod == IntPtr.Zero)
            {
                this.liveVehicleClassesResolveFailed = true;
                return false;
            }

            return true;
        }

        private bool TryEnsureLiveVehicleGetOnMethod()
        {
            if (this.liveVehicleGetOnMethodResolved)
            {
                return this.liveVehicleGetOnMethod != IntPtr.Zero;
            }

            if (this.liveVehicleGetOnMethodResolveFailed)
            {
                return false;
            }

            IntPtr protocolClass = this.ResolveVehicleBypassClass(
                "XDTDataAndProtocol.ProtocolService.Vehicle",
                "VehicleProtocolManager",
                VehicleBypassProtocolImages,
                "XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager");
            if (protocolClass == IntPtr.Zero)
            {
                protocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Vehicle.VehicleProtocolManager");
            }

            if (protocolClass != IntPtr.Zero)
            {
                this.liveVehicleGetOnMethod = this.FindVehicleBypassMethod(protocolClass, "GetOnVehicle", 3);
            }

            this.liveVehicleGetOnMethodResolved = true;
            if (this.liveVehicleGetOnMethod == IntPtr.Zero)
            {
                this.liveVehicleGetOnMethodResolveFailed = true;
                return false;
            }

            return true;
        }

        // Scans EVERY currently-loaded VehicleComponent (Entities.GetComponents<T> pattern, AGENTS.md's
        // preferred all-of-type scan — reused TryAuraMonoGetComponentObjects) regardless of who owns it:
        // another player's parked/driving vehicle, an NPC's, or an event/GameplayVehicle. Component object
        // pointers never leave this synchronous method — netId/staticId/seat state are scalarized into
        // liveVehicleRows before pins are freed (no cross-yield pointer risk, AGENTS.md §11).
        private bool TryScanLiveVehicles(out string status)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "AuraMono not ready (enter a town and retry).";
                return false;
            }

            if (!this.TryEnsureLiveVehicleAuraMonoMethods())
            {
                status = "VehicleComponent methods unavailable (enter a town).";
                return false;
            }

            this.EnsureVehicleTurnOnEventHook();

            List<IntPtr> components = null;
            List<uint> pins = new List<uint>();
            List<LiveVehicleRow> found = new List<LiveVehicleRow>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.liveVehicleComponentClass, out components, pins)
                    || components == null
                    || components.Count == 0)
                {
                    status = "No live vehicles currently loaded nearby.";
                    return false;
                }

                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr componentObj = components[i];
                    if (componentObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(componentObj, "entity", out IntPtr entityObj)
                        || entityObj == IntPtr.Zero
                        || !this.TryGetMonoUInt32Member(entityObj, "netId", out uint netId)
                        || netId == 0)
                    {
                        continue;
                    }

                    int staticId = this.TryReadLiveVehicleStaticId(componentObj);
                    if (staticId <= 0)
                    {
                        continue;
                    }

                    bool seat0Free = !this.TryReadLiveVehicleSeat0Occupied(componentObj);
                    found.Add(new LiveVehicleRow { NetId = netId, StaticId = staticId, Seat0Free = seat0Free });
                }
            }
            finally
            {
                for (int i = 0; i < pins.Count; i++)
                {
                    AuraMonoPinFree(pins[i]);
                }
            }

            if (found.Count == 0)
            {
                status = "No live vehicles resolved (found components, but none readable).";
                return false;
            }

            found.Sort((a, b) => a.NetId.CompareTo(b.NetId));

            ItemDumpEntityNameResolver nameResolver = this.CreateItemDumpEntityNameResolver();
            this.TryResolveSelfPlayerNetId(out uint selfNetId);
            IntPtr friendInstance = this.EnsureNameReadReady();
            this.liveVehicleRows.Clear();
            for (int i = 0; i < found.Count; i++)
            {
                LiveVehicleRow row = found[i];
                try
                {
                    row.Name = nameResolver.Resolve(row.StaticId);
                }
                catch
                {
                    row.Name = string.Empty;
                }

                row.OwnerName = this.TryResolveVehicleOwnerName(row.NetId, selfNetId, friendInstance);
                this.liveVehicleRows.Add(row);
            }

            this.liveVehicleListLoaded = true;
            status = "Found " + this.liveVehicleRows.Count + " live vehicle(s) in the loaded world.";
            return true;
        }

        // Orchestrates one vehicle row's owner label: "You" for the local player's own vehicle (skips the
        // network/profile lookup entirely), the resolved player name via the existing FriendSystem-backed
        // pipeline (EnsureNameReadReady/TryReadPlayerName, HeartopiaComplete.MapSpots.cs — same lever used
        // for stranger names on the map/nameplate), or the raw netId if a name isn't cached yet. Empty
        // string when there is no owner at all (ownerId == 0 — an ownerless/event GameplayVehicle).
        private unsafe string TryResolveVehicleOwnerName(uint vehicleNetId, uint selfNetId, IntPtr friendInstance)
        {
            uint ownerNetId = this.TryReadVehicleOwnerNetId(vehicleNetId);
            if (ownerNetId == 0)
            {
                return string.Empty;
            }

            if (selfNetId != 0 && ownerNetId == selfNetId)
            {
                return "You";
            }

            if (friendInstance == IntPtr.Zero)
            {
                return "netId=" + ownerNetId;
            }

            uint namePin = 0u;
            try
            {
                if (this.TryReadPlayerName(friendInstance, ownerNetId, out _, out IntPtr nameStrPtr, out namePin, out _)
                    && this.TryReadMonoString(nameStrPtr, out string ownerName)
                    && !string.IsNullOrEmpty(ownerName))
                {
                    return ownerName;
                }
            }
            catch
            {
            }
            finally
            {
                if (namePin != 0u)
                {
                    AuraMonoPinFree(namePin);
                }
            }

            return "netId=" + ownerNetId;
        }

        private bool TryEnsureSpawnVehicleOwnerReflection()
        {
            if (this.spawnVehicleOwnerReflectionResolved)
            {
                return this.spawnVehicleTryGetComponentDataMethodDef != null
                    && this.spawnVehicleLevelEntityDataType != null
                    && this.spawnVehicleOwnerIdField != null
                    && this.spawnVehicleNetIdType != null;
            }

            if (this.spawnVehicleOwnerReflectionResolveFailed)
            {
                return false;
            }

            this.spawnVehicleDataCenterType = this.FindLoadedType(
                "XDTDataAndProtocol.ComponentsData.DataCenter",
                "ScriptsRefactory.DataAndProtocol.ComponentsData.DataCenter",
                "DataCenter");
            this.spawnVehicleNetIdType = this.FindLoadedType(
                "XDTDataAndProtocol.ComponentsData.NetId",
                "NetId");
            this.spawnVehicleLevelEntityDataType = this.FindLoadedType(
                "XDTDataAndProtocol.ComponentsData.LevelEntityComponentData",
                "LevelEntityComponentData");

            if (this.spawnVehicleDataCenterType != null && this.spawnVehicleNetIdType != null)
            {
                foreach (MethodInfo method in this.spawnVehicleDataCenterType.GetMethods(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method == null || method.Name != "TryGetComponentData" || !method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 2
                        && string.Equals(parameters[0].ParameterType.Name, "NetId", StringComparison.Ordinal))
                    {
                        this.spawnVehicleTryGetComponentDataMethodDef = method;
                        break;
                    }
                }
            }

            if (this.spawnVehicleLevelEntityDataType != null)
            {
                this.spawnVehicleOwnerIdField = this.spawnVehicleLevelEntityDataType.GetField(
                    "ownerId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            this.spawnVehicleOwnerReflectionResolved = true;
            if (this.spawnVehicleTryGetComponentDataMethodDef == null
                || this.spawnVehicleLevelEntityDataType == null
                || this.spawnVehicleOwnerIdField == null
                || this.spawnVehicleNetIdType == null)
            {
                this.spawnVehicleOwnerReflectionResolveFailed = true;
                return false;
            }

            return true;
        }

        private uint TryReadVehicleOwnerNetId(uint vehicleNetId)
        {
            if (vehicleNetId == 0 || !this.TryEnsureSpawnVehicleOwnerReflection())
            {
                return 0u;
            }

            try
            {
                MethodInfo tryGetMethod = this.spawnVehicleTryGetComponentDataMethodDef.MakeGenericMethod(this.spawnVehicleLevelEntityDataType);
                object netIdArg = this.CreateNetCookNetIdArgument(this.spawnVehicleNetIdType, vehicleNetId);
                if (netIdArg == null)
                {
                    return 0u;
                }

                object dataBox = Activator.CreateInstance(this.spawnVehicleLevelEntityDataType);
                object[] args = new object[] { netIdArg, dataBox };
                bool found = tryGetMethod.Invoke(null, args) is bool ok && ok;
                if (!found)
                {
                    return 0u;
                }

                object data = args[1] ?? dataBox;
                object ownerIdObj = this.spawnVehicleOwnerIdField.GetValue(data);
                return ownerIdObj is uint u ? u : 0u;
            }
            catch
            {
                return 0u;
            }
        }

        private unsafe int TryReadLiveVehicleStaticId(IntPtr componentObj)
        {
            if (componentObj == IntPtr.Zero || this.liveVehicleGetCarConfigMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr carConfigObj = auraMonoRuntimeInvoke(this.liveVehicleGetCarConfigMethod, componentObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || carConfigObj == IntPtr.Zero)
            {
                return 0;
            }

            return this.TryReadMonoIntMember(carConfigObj, "id");
        }

        private unsafe bool TryReadLiveVehicleSeat0Occupied(IntPtr componentObj)
        {
            if (componentObj == IntPtr.Zero || this.liveVehicleHavePassengerMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return true;
            }

            int seatIndex = 0;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&seatIndex);
            IntPtr exc = IntPtr.Zero;
            IntPtr resultObj = auraMonoRuntimeInvoke(this.liveVehicleHavePassengerMethod, componentObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || resultObj == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return true;
            }

            IntPtr raw = auraMonoObjectUnbox(resultObj);
            return raw != IntPtr.Zero && *(byte*)raw != 0;
        }

        // Direct call to VehicleProtocolManager.GetOnVehicle(vehicleNetId, seatIndex, levelObjectId) — the
        // SAME command EnterVehicleTask sends when you walk up to a vehicle and interact normally. No
        // CallVehicleCommand/Garage-ownership involved; the only client-side gate in the real game is
        // "is this seat already occupied" (VehicleMainInteract.IsDisplayable). Whether the SERVER
        // additionally rejects a non-owned vehicle here is exactly what this button tests — watch
        // liveVehicleStatus for the VehicleErrorCode the PlayerVehicleTurnOnEvent hook reports back.
        private unsafe void TryGetOnLiveVehicle(uint vehicleNetId, int staticId, string name)
        {
            string label = (string.IsNullOrEmpty(name) ? "(vehicle)" : name) + " (netId=" + vehicleNetId + ")";
            if (vehicleNetId == 0 || staticId <= 0)
            {
                this.liveVehicleStatus = "Get On failed for " + label + ": missing netId/staticId.";
                return;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    this.liveVehicleStatus = "Get On failed for " + label + ": AuraMono not ready.";
                    return;
                }

                if (!this.TryEnsureLiveVehicleGetOnMethod())
                {
                    this.liveVehicleStatus = "Get On failed for " + label + ": GetOnVehicle method unavailable.";
                    return;
                }

                this.EnsureNoclipVehicleAuraMono();
                if (!this.TryGetNoclipVehicleManagerMonoObject(out IntPtr managerObj) || managerObj == IntPtr.Zero)
                {
                    this.liveVehicleStatus = "Get On failed for " + label + ": VehicleManager unavailable.";
                    return;
                }

                if (!this.TryVehicleBypassEnsureSummonMethods()
                    || !this.TryVehicleBypassGetLevelObjectId(managerObj, staticId, out int levelObjectId))
                {
                    this.liveVehicleStatus = "Get On failed for " + label + ": seat levelObjectId unavailable.";
                    return;
                }

                uint netId = vehicleNetId;
                int seatIndex = 0;
                int levelObjectIdLocal = levelObjectId;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&netId);
                args[1] = (IntPtr)(&seatIndex);
                args[2] = (IntPtr)(&levelObjectIdLocal);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.liveVehicleGetOnMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.liveVehicleStatus = "Get On threw for " + label + ".";
                    this.AddMenuNotification("Get On threw: " + label, new Color(1f, 0.55f, 0.45f));
                    return;
                }

                this.liveVehicleStatus = "Get On sent for " + label + " — waiting for server result...";
                this.AddMenuNotification("Get On sent: " + label, new Color(0.45f, 0.88f, 1f));
            }
            catch (Exception ex)
            {
                this.liveVehicleStatus = "Get On failed for " + label + ": " + ex.Message;
            }

            ModLogger.Msg("[SpawnVehicle] " + this.liveVehicleStatus);
        }

        // PlayerVehicleTurnOnEvent (ScriptsRefactory.DataAndProtocol.Events, GLOBAL) — dispatched by the
        // server's reply to VehicleGetOnCommand for EVERY player, so this filters to our own netId.
        // Field order/offsets follow C# struct declaration order (sequential layout, no [StructLayout]
        // override) — the same convention already confirmed for PlayerVehicleQTEEvent in this codebase:
        // playerNetId(uint)@0, vehicleNetId(uint)@4, seatIndex(int)@8, errorCode(VehicleErrorCode/int)@12.
        private void EnsureVehicleTurnOnEventHook()
        {
            if (this.liveVehicleTurnOnHookInstalled)
            {
                return;
            }

            bool ok = this.RegisterGameEventHook(
                "ScriptsRefactory.DataAndProtocol.Events.PlayerVehicleTurnOnEvent",
                16,
                this.OnLiveVehicleTurnOnEvent);
            if (ok)
            {
                this.liveVehicleTurnOnHookInstalled = true;
            }
        }

        private void OnLiveVehicleTurnOnEvent(GameEventSnapshot snapshot)
        {
            if (!this.TryResolveSelfPlayerNetId(out uint selfNetId) || selfNetId == 0)
            {
                return;
            }

            uint playerNetId = snapshot.ReadUInt32(0);
            if (playerNetId != selfNetId)
            {
                return;
            }

            uint vehicleNetId = snapshot.ReadUInt32(4);
            int seatIndex = snapshot.ReadInt32(8);
            int errorCode = snapshot.ReadInt32(12);
            bool success = errorCode == 0; // VehicleErrorCode.Success == 0
            string result = success
                ? ("Get On SUCCEEDED on vehicle netId=" + vehicleNetId + " seat=" + seatIndex + ".")
                : ("Get On REJECTED for vehicle netId=" + vehicleNetId + " — VehicleErrorCode=" + errorCode + ".");
            this.liveVehicleStatus = result;
            this.AddMenuNotification(result, success ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.6f, 0.45f));
            ModLogger.Msg("[SpawnVehicle] " + result);
        }
    }
}
