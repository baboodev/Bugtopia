using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.U2D;

namespace HeartopiaMod
{
    // "Game" display mode for the radar: instead of the screen-space ESP, push the radar's selected
    // resources onto the in-game map as native local tracking items (the same mechanism the game uses
    // for quest targets). This stays entirely inside the embedded-Mono runtime: TrackData/StartTrack
    // are pure value structs, dispatched via XDTGame.Core.EventCenter.DispatchEvent<T>, and the icon is
    // loaded by the game itself — so no cross IL2CPP<->Mono Unity objects.
    //
    // v1: NavigationPoint tracking -> shows on the HUD minimap (CommonMapBar reads every tracking item)
    // with the generic quest-flag icon (gametask_icon_002). Limited to the N nearest to avoid the
    // per-item on-screen tracking pointer cluttering the screen. See
    // docs/plans/2026-06-22-radar-game-mapspots.md.
    public partial class HeartopiaComplete
    {
        // TrackType / TrackReason for this build (both byte enums; verified in ilspy-dumps).
        // The icon a tracking item shows is fixed by its TrackType (TrackingItem.GetAtlasSpriteId):
        //   Bird->theme_107, Fish->theme_104, Insect->theme_108 (real category icons, no id needed);
        //   NavigationPoint with an unknown StaticId -> generic "gametask_icon_002" flag.
        // Per-resource (forageable/stone) icons are NOT available via tracking without resolving the
        // resource's drop-item id from loot tables, so those stay on the flag.
        private const byte MapTrackTypeNavigationPoint = 11;
        private const byte MapTrackTypePlayer = 1;
        private const byte MapTrackTypeBird = 5;
        private const byte MapTrackTypeFish = 6;
        private const byte MapTrackTypeInsect = 7;
        // Furniture -> AtlasEnum.NormalItem, SpriteName = RewardUtility.GetIconName(StaticId): real per-item
        // icon (ui_item_normal_{prefab}). StaticId must be the resource ENTITY's static id (EntityUtil
        // .GetEntityResId), NOT the produce itemTypeID. This is how we get true per-resource icons.
        private const byte MapTrackTypeFurniture = 14;
        // MapResource -> AtlasEnum.Collectable, SpriteName = ui_dynamic_collectable_{StaticId}. For our
        // produce drop-item ids (timber/stone/fruit) that sprite EXISTS, and unlike Furniture this track
        // type matches a Collectable map-spot (IsSameType) so it also drives the BIG map per-position.
        private const byte MapTrackTypeMapResource = 8;
        private const byte MapTrackReasonLocal = 1;
        // Match a tracked marker to a live collectable entity within this XZ distance (m).
        private const float MapResMatchRadiusSqr = 9f;
        private const float MapResScanInterval = 2f;
        // Synthetic StaticId with no TableMapElement -> TrackingItem falls back to "gametask_icon_002".
        private const int MapTrackSyntheticStaticId = 900000000;
        // "Contaminated" (sea-clean pollutant) map icon: pin it to the decadopecten seashell entity (Shell
        // table 22000, normalStep0PrefabId = p_gather_decadopecten_step00). As a Furniture track this
        // resolves through RewardUtility.GetIconName(22000) -> AtlasEnum.NormalItem/
        // ui_item_normal_p_gather_decadopecten_step00 — the SAME seashell the ESP overlay draws — instead
        // of position-matching to a neighboring collectable (which stole a nearby wakame's icon).
        private const int ContaminatedMapIconStaticId = 22000;
        // High tag in the token so our synthetic tokens never collide with real server tokens (netIds).
        private const ulong MapTrackTokenTag = 0x5000000000000000UL;
        private const float MapTrackSyncInterval = 0.4f;
        private const float MapTrackMoveThresholdSqr = 9f;

        private int radarGameTrackLimit = 5;

        private bool mapTrackResolved;
        private float mapTrackNextResolveAt;
        private IntPtr mapTrackDispatchStartMethod = IntPtr.Zero; // inflated DispatchEvent<StartTrack>
        private IntPtr mapTrackDispatchStopMethod = IntPtr.Zero;  // inflated DispatchEvent<StopTrack>
        private int offTdPosition, offTdToken, offTdTargetNetId, offTdStaticId, offTdTrackType, offTdTrackReason;
        private int offStToken;

        private float mapTrackNextSyncAt;
        private float mapTrackBreakerUntil;
        private bool mapTrackBreakerLogged;
        private bool mapTrackResolveLogged;
        private int mapTrackDiagSyncs;

        private readonly Dictionary<ulong, Vector3> mapTrackInjected = new Dictionary<ulong, Vector3>(16);
        private readonly Dictionary<ulong, byte> mapTrackInjectedType = new Dictionary<ulong, byte>(16);
        private readonly Dictionary<ulong, int> mapTrackInjectedStaticId = new Dictionary<ulong, int>(16);
        private readonly Dictionary<ulong, Vector3> mapTrackDesired = new Dictionary<ulong, Vector3>(16);
        private readonly Dictionary<ulong, byte> mapTrackDesiredType = new Dictionary<ulong, byte>(16);
        private readonly Dictionary<ulong, int> mapTrackDesiredStaticId = new Dictionary<ulong, int>(16);
        private readonly List<ulong> mapTrackRemoveBuffer = new List<ulong>(16);
        private readonly List<MapTrackCandidate> mapTrackCandidates = new List<MapTrackCandidate>(128);
        // Resource icon cached by radar label. The game only instantiates a CollectableObjectComponent for
        // resources near the player (entity streaming), so distant markers can't resolve a produce id. But a
        // radar label ("Rare Tree", "Stone"...) maps to one resource TYPE with one icon, so once ANY marker
        // of that label resolves, every marker of that label reuses the icon — no live entity needed.
        private readonly Dictionary<string, int> mapTrackLabelIcon = new Dictionary<string, int>(32);
        // Labels whose icon came from the produce/dropGroup path (drop-item id) -> has a
        // ui_dynamic_collectable_{id} sprite -> eligible for MapResource track + big-map spot. Entity-
        // fallback labels (mushrooms) are absent here and stay on Furniture (minimap only).
        private readonly HashSet<string> mapTrackLabelProduce = new HashSet<string>();
        private readonly HashSet<string> mapTrackResolveDiag = new HashSet<string>(); // log icon resolution once per label

        // Big map (MapPanel) renders MapSpotData spots, not tracks. Inject per-position Collectable spots via
        // MapSpotProtocolManager.AddSpot, keyed by a UNIQUE usageId (low bits of the marker token). The spot
        // borrows its icon from the matching MapResource track (TargetNetId == usageId, StaticId == item id),
        // so each location shows the real ui_dynamic_collectable_{itemId} icon.
        private const int SpotEnumCollectable = 5;
        private const int SpotReasonAuto = 0;
        private const int GameSceneIdStarTown = 1;
        private IntPtr mapSpotAddMethod = IntPtr.Zero;
        private IntPtr mapSpotRemoveMethod = IntPtr.Zero;
        private bool mapSpotMethodsTried;
        private readonly Dictionary<int, Vector3> mapBigSpotInjected = new Dictionary<int, Vector3>(16);
        private readonly Dictionary<int, Vector3> mapBigSpotDesired = new Dictionary<int, Vector3>(16);
        private readonly List<int> mapBigSpotRemoveBuffer = new List<int>(16);
        // One-shot diagnostic: dump every sprite name in the packed collectable SpriteAtlas, so we can see
        // whether any mushroom sprite exists there (the .ab file list can't enumerate packed atlas contents).
        private bool mapAtlasDumped;
        private int mapAtlasDumpTries;
        private float mapAtlasNextTryAt;
        // Collectable atlas item-name -> collectable id (built from the live atlas). Lets us resolve a
        // resource whose produce path fails (mushrooms: produceId=0, entity id 130005 has no sprite) to its
        // real collectable id by matching the radar label (= the item name, e.g. "Shiitake" -> 48002).
        private readonly Dictionary<string, int> mapAtlasNameToId = new Dictionary<string, int>(64);
        // Every numeric id actually present in the collectable atlas. The produce path can resolve a REAL
        // drop-item id that has NO collectable sprite (meteor -> Starfall Shard 40034-40036): a MapResource
        // track would then render blank, so such ids must fall back to Furniture (NormalItem item icon).
        private readonly HashSet<int> mapAtlasIdSet = new HashSet<int>();

        // Other players: scan RemotePlayerComponent (view component of every remote player) for entity
        // netId + position, and position-match radar "Player" markers so their Player tracks carry the
        // REAL TargetNetId. With a real netId the game natively shows the friend's avatar photo
        // (MiniMapSpotWidget.SetData IsFriend branch -> HeadIconWidget with GetUserProfile().AvatarImageUrl)
        // and dedups our track against the vanilla server player spot (same usageId).
        private IntPtr mapPlayerClass = IntPtr.Zero;
        private bool mapPlayerClassResolveTried;
        private float mapPlayerNextScanAt;
        private bool mapPlayerDiagLogged;
        private readonly List<MapPlayerEntity> mapPlayerEntities = new List<MapPlayerEntity>(16);
        private struct MapPlayerEntity
        {
            public Vector3 Position;
            public uint NetId;
        }
        private const float MapPlayerScanInterval = 1.0f;  // players move — rescan faster than collectables
        private const float MapPlayerMatchRadiusSqr = 25f; // 5 m XZ: marker/entity drift between scans

        // Real player names for non-friends (radarPlayerAvatarsAll): the game shows strangers a Title instead
        // of their name. Read PlayerProfile.Name via AuraMono (FriendSystem.GetUserProfile(netId), boxed
        // struct return) in the throttled remote-player scan, cache netId -> pinned name MonoString, and
        // return it from a MapSpot.get_name detour for Player spots. No force-friend -> no dialog regression.
        private IntPtr mapNameFriendSystemClass = IntPtr.Zero;
        private IntPtr mapNamePlayerServiceClass = IntPtr.Zero;
        private readonly List<IntPtr> mapNameGetProfileMethods = new List<IntPtr>(2); // both 1-arg overloads
        private int mapNameProfileNameOffset = -1;   // PlayerProfile.Name raw offset (value-type buffer)
        private int mapNameProfileIdOffset = -1;     // PlayerProfile.Id (encoded shortId string) raw offset
        private int mapNameProfileAvatarUrlOffset = -1; // PlayerProfile.AvatarImageUrl raw offset (ESP avatar)
        private IntPtr mapNameDecodeShortIdMethod = IntPtr.Zero; // ShortIdUtil.DecodeShortId(string)->long
        private IntPtr mapNameGetPlayerNameMethod = IntPtr.Zero; // PlayerServiceSystem.GetPlayerName(long,long)
        private bool mapNameMethodsTried;
        private bool mapNameDiagLogged;
        // shortId -> pinned real-name MonoString. GetPlayerName(shortId) is the central name function (map
        // spot, profile card, over-head nameplate, chat...) so keying by shortId covers them all.
        private static Dictionary<long, IntPtr> mapNameByShortId = new Dictionary<long, IntPtr>(); // read by hook
        private readonly Dictionary<long, IntPtr> mapNameByShortIdNext = new Dictionary<long, IntPtr>();
        private readonly List<uint> mapNamePins = new List<uint>();      // pins backing the live map
        private readonly List<uint> mapNamePinsNext = new List<uint>();
        // netId -> avatar image url (managed string copies, no pinning). Built in the same player scan
        // that reads names; consumed by the ESP-beacon player-avatar resolver.
        private Dictionary<uint, string> mapAvatarUrlByNetId = new Dictionary<uint, string>();
        private readonly Dictionary<uint, string> mapAvatarUrlByNetIdNext = new Dictionary<uint, string>();
        private delegate IntPtr GetPlayerNameDelegate(IntPtr self, long shortId, long title);
        private static GetPlayerNameDelegate getPlayerNameHook;          // anti-GC
        private static GetPlayerNameDelegate getPlayerNameTrampoline;    // original
        private static MonoMod.RuntimeDetour.NativeDetour getPlayerNameDetour;
        private static volatile bool mapNameActive;
        private bool getNamePatchTried;

        // The over-head nameplate (EntityTrackBarModel.TryGetName) and the profile card read
        // userProfile.Title.TitleString directly, NOT GetPlayerName — so they never showed the real name.
        // GetUserProfile(shortId/netId) returns a by-value PlayerProfile copy that already carries the real
        // Name (@NameOffset); we detour it and mirror Name -> Title._titleString in the returned copy, so the
        // TitleString getter yields the real name for both surfaces. Pure copy-mutation of the caller's local
        // struct — the FriendSystem cache is untouched. Both 1-arg overloads (long shortId / uint netId) are
        // hooked with an identical unconditional body (no shortId gate needed -> no overload disambiguation).
        private int mapNameTitleStringOffset = -1;  // Title._titleString raw offset within PlayerProfile buffer
        // sret struct-return ABI (instance): RCX=self/this, RDX=sret buffer, R8=arg (confirmed via crash dump).
        private delegate IntPtr GetProfileDelegate(IntPtr self, IntPtr sret, long arg);
        private static volatile bool mapNameReadingSelf; // true while our scan is inside GetUserProfile
        private static readonly GetProfileDelegate[] getProfileHooks = new GetProfileDelegate[2];       // anti-GC
        private static readonly GetProfileDelegate[] getProfileTrampolines = new GetProfileDelegate[2];  // originals
        private static readonly MonoMod.RuntimeDetour.NativeDetour[] getProfileDetours = new MonoMod.RuntimeDetour.NativeDetour[2];
        private static int gpNameOffset = -1;         // PlayerProfile.Name raw offset (hook copy)
        private static int gpTitleStringOffset = -1;  // Title._titleString raw offset (hook copy)
        private bool getProfilePatchTried;

        // The over-head nameplate (EntityTrackBarModel.TryGetName) only returns a name for friends /
        // acquaintances / hide-seek — a plain stranger hits `return false` (empty) until you interact (open
        // their card), which registers them as an acquaintance. FriendSystem.IsAcquaintance is used ONLY by
        // that nameplate (no social/action gating elsewhere), so we detour it to report scanned players as
        // acquaintances — the nameplate then reaches the Title branch and shows our mirrored real name. Gated
        // on the name cache so it only affects players we actually have a name for.
        private delegate byte IsAcquaintanceDelegate(IntPtr self, long shortId);
        private static IsAcquaintanceDelegate isAcquaintanceHook;        // anti-GC
        private static IsAcquaintanceDelegate isAcquaintanceTrampoline;  // original
        private static MonoMod.RuntimeDetour.NativeDetour isAcquaintanceDetour;
        private bool acqPatchTried;

        // Per-token TargetNetId for the injected tracks (Player -> real player netId, MapResource -> the
        // big-map spot usageId, else 0). Changing it re-dispatches the track.
        private readonly Dictionary<ulong, uint> mapTrackDesiredTargetNet = new Dictionary<ulong, uint>(16);
        private readonly Dictionary<ulong, uint> mapTrackInjectedTargetNet = new Dictionary<ulong, uint>(16);

        // Live collectable entities: world position + resource ENTITY static id (for the real item icon).
        private IntPtr mapResCollectableClass = IntPtr.Zero;
        private bool mapResClassResolveTried;
        private float mapResNextScanAt;
        private bool mapResDiagLogged;
        private readonly List<MapResEntity> mapResEntities = new List<MapResEntity>(128);
        // EntityUtil.GetEntityResId(Entity) via AuraMono (managed EntityUtil is absent on this build).
        // Both 1-param overloads (uint / Entity) are stored; invoking with the entity object is safe for
        // both (Entity overload returns the real static id; uint overload harmlessly returns 0).
        private bool mapResEntityUtilTried;
        private readonly List<IntPtr> mapResGetResIdMethods = new List<IntPtr>(2);

        private struct MapResEntity
        {
            public Vector3 Position;
            public int StaticId;     // entity static id (works as icon for mushroom-type gathers)
            public int ProduceId;    // CollectableObjectComponent.itemTypeID -> TableMapResourceProduce
            public bool OnCooldown;  // CollectableObjectComponent.inCold (authoritative; depleted resource)
        }

        // Parallel lightweight snapshot of EVERY positioned collectable (no id filter): the
        // authoritative cooldown layer consumed by the radar cold-sync and the foraging wait.
        internal struct LiveCollectableCold
        {
            public Vector3 Position;
            public bool OnCooldown;  // inCold
            public long ColdEndMs;   // coldEndTime (unix ms; 0 when warm/unreadable)
        }

        private readonly List<LiveCollectableCold> liveCollectableColds = new List<LiveCollectableCold>(128);
        private float liveCollectableScanCompletedAt = -1f;

        // TableData.GetMapResourceProduce(produceId).hitProduce[0][0] = the drop item id, whose
        // GetIconName gives the real material/item icon (wood/stone/bamboo/fruit/mushroom).
        private IntPtr mapResGetProduceMethod = IntPtr.Zero;
        private bool mapResProduceTried;
        // Log each distinct produce id once (capped) so per-resource-type resolution is visible without spam.
        private readonly HashSet<int> mapResProduceDiagIds = new HashSet<int>();
        private readonly Dictionary<int, int> mapResProduceItemIdCache = new Dictionary<int, int>(64);

        // hitProduce[0][0] is usually a dropGroup KEY string (e.g. "BUSH101"), not a numeric item id.
        // RewardUtility.GetDropGroup(groupId) -> List<(RewardData,float)>; element[0].rewardId = the drop
        // item id (wood/stone/bamboo/fruit). This is the universal path for all resource types; the plain
        // integer parse only worked for the few produces that list a literal id, leaving trees/stones white.
        private IntPtr mapResDropGroupMethod = IntPtr.Zero;
        // Safe dropGroup resolution (the game's RewardUtility.GetDropGroup throws IndexOutOfRange on drop
        // rows with empty content). We read TableData.TableRandomDropsAndLowerUpperLimitsByDropGroup
        // (Dictionary<string,(int,int,List<TableRandomDrop>,List<int>)>) directly and guard content length.
        private IntPtr mapResDropDictGetter = IntPtr.Zero;   // static get_TableRandomDropsAndLowerUpperLimitsByDropGroup
        private IntPtr mapResDropDictGetItem = IntPtr.Zero;  // Dictionary.get_Item(string)
        private IntPtr mapResGetQualityMethod = IntPtr.Zero; // RewardUtility.GetQuality(RewardType,int,int)
        private IntPtr mapResGetEntityMethod = IntPtr.Zero;  // TableData.GetEntity(int,bool) -> TableEntity (.name)
        private int mapResGroupVerboseCount;
        private int mapTrackMatchDiagCount;

        private struct MapTrackCandidate
        {
            public ulong Token;
            public Vector3 Position;
            public float DistanceSqr;
            public byte TrackType;
            public string Label;
        }

        private static byte GetMapTrackTypeForLabel(string label)
        {
            switch (label)
            {
                case "Bird": return MapTrackTypeBird;
                case "Fish Shadow": return MapTrackTypeFish;
                case "Insect": return MapTrackTypeInsect;
                // Players/morphs: TrackType.Player -> the game's native player pin
                // (ui_dynamic_hud_map_mark_stranger), not the generic flag/placeholder.
                case "Player":
                case "Morph": return MapTrackTypePlayer;
                default: return MapTrackTypeNavigationPoint;
            }
        }

        // Only small "gather" collectables have a valid NormalItem icon (p_gather_*/p_fruit_*). Tree/Stone/
        // Ore entities expose an object prefab that has no item icon -> white circle, so they keep the flag.
        private static bool IsForageableLabel(string label)
        {
            switch (label)
            {
                case "Mushroom":
                case "Oyster":
                case "Button":
                case "Penny Bun":
                case "Shiitake":
                case "Truffle":
                case "Fiddlehead":
                case "Tall Mustard":
                case "Burdock":
                case "Mustard Greens":
                case "Blueberry":
                case "Raspberry":
                    return true;
                default:
                    return false;
            }
        }

        // Called when the ESP/Game segmented control changes.
        private void OnRadarDisplayModeChanged()
        {
            if (this.radarDisplayMode == 1)
            {
                this.mapTrackNextSyncAt = 0f; // sync promptly
            }
            else
            {
                this.ClearInjectedGameMapSpots();
            }
        }

        // Driven from OnUpdate every frame; self-gates and throttles.
        private void ProcessGameMapSpotsOnUpdate()
        {
            // Avatar patches follow their own toggle, independent of the radar/display mode (they also
            // upgrade the VANILLA player spots on the mini/big map).
            this.ManagePlayerAvatarPatches();

            if (this.radarDisplayMode != 1 || !this.isRadarActive || this.radarContainer == null)
            {
                if (this.mapTrackInjected.Count > 0)
                {
                    this.ClearInjectedGameMapSpots();
                }
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.mapTrackNextSyncAt || now < this.mapTrackBreakerUntil)
            {
                return;
            }
            this.mapTrackNextSyncAt = now + MapTrackSyncInterval;

            try
            {
                this.SyncGameTrackMarkers();
            }
            catch (Exception ex)
            {
                this.mapTrackBreakerUntil = now + 10f;
                if (!this.mapTrackBreakerLogged)
                {
                    this.mapTrackBreakerLogged = true;
                    ModLogger.Msg("[MapSpots] tracking sync error (cooling down): " + ex.Message);
                }
            }
        }

        private bool EnsureMapTrackReady()
        {
            if (this.mapTrackResolved)
            {
                return true;
            }

            float now = Time.unscaledTime;
            if (now < this.mapTrackNextResolveAt)
            {
                return false;
            }
            this.mapTrackNextResolveAt = now + 5f;

            if (!this.EnsureAuraMonoApiReady()
                || auraMonoClassGetType == null
                || auraMonoMetadataGetGenericInst == null
                || auraMonoClassInflateGenericMethod == null
                || auraMonoClassGetFieldFromName == null
                || auraMonoFieldGetOffset == null
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr eventCenter = this.FindAuraMonoClassByFullName("XDTGame.Core.EventCenter");
            if (eventCenter == IntPtr.Zero)
            {
                eventCenter = this.FindAuraMonoClassInImages("XDTGame.Core", "EventCenter",
                    new[] { "XDTBaseService", "XDTBaseService.dll" });
            }
            IntPtr openDispatch = eventCenter == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(eventCenter, "DispatchEvent", 1);

            IntPtr startTrackClass = this.ResolveTrackClass("StartTrack");
            IntPtr stopTrackClass = this.ResolveTrackClass("StopTrack");
            IntPtr trackDataClass = this.ResolveTrackClass("TrackData");

            if (openDispatch == IntPtr.Zero || startTrackClass == IntPtr.Zero || stopTrackClass == IntPtr.Zero || trackDataClass == IntPtr.Zero)
            {
                ModLogger.Msg("[MapSpots] track resolve failed: dispatch=" + (openDispatch != IntPtr.Zero)
                    + " StartTrack=" + (startTrackClass != IntPtr.Zero) + " StopTrack=" + (stopTrackClass != IntPtr.Zero)
                    + " TrackData=" + (trackDataClass != IntPtr.Zero));
                return false;
            }

            this.mapTrackDispatchStartMethod = this.InflateGenericDispatch(openDispatch, startTrackClass);
            this.mapTrackDispatchStopMethod = this.InflateGenericDispatch(openDispatch, stopTrackClass);
            if (this.mapTrackDispatchStartMethod == IntPtr.Zero || this.mapTrackDispatchStopMethod == IntPtr.Zero)
            {
                ModLogger.Msg("[MapSpots] track inflate failed: start=" + (this.mapTrackDispatchStartMethod != IntPtr.Zero)
                    + " stop=" + (this.mapTrackDispatchStopMethod != IntPtr.Zero));
                return false;
            }

            bool offsetsOk =
                this.TryGetTrackFieldRawOffset(trackDataClass, "Position", out this.offTdPosition)
                & this.TryGetTrackFieldRawOffset(trackDataClass, "Token", out this.offTdToken)
                & this.TryGetTrackFieldRawOffset(trackDataClass, "TargetNetId", out this.offTdTargetNetId)
                & this.TryGetTrackFieldRawOffset(trackDataClass, "StaticId", out this.offTdStaticId)
                & this.TryGetTrackFieldRawOffset(trackDataClass, "TrackType", out this.offTdTrackType)
                & this.TryGetTrackFieldRawOffset(trackDataClass, "TrackReason", out this.offTdTrackReason)
                & this.TryGetTrackFieldRawOffset(stopTrackClass, "Token", out this.offStToken);
            if (!offsetsOk)
            {
                ModLogger.Msg("[MapSpots] track field offset resolve failed");
                return false;
            }

            this.mapTrackResolved = true;
            if (!this.mapTrackResolveLogged)
            {
                this.mapTrackResolveLogged = true;
                ModLogger.Msg("[MapSpots] tracking resolved: DispatchEvent<StartTrack/StopTrack> + TrackData offsets OK");
            }
            return true;
        }

        private IntPtr ResolveTrackClass(string shortName)
        {
            IntPtr cls = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Track." + shortName);
            if (cls == IntPtr.Zero)
            {
                cls = this.FindAuraMonoClassInImages("XDTDataAndProtocol.ProtocolService.Track", shortName,
                    new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
            }
            return cls;
        }

        private bool TryGetTrackFieldRawOffset(IntPtr klass, string fieldName, out int rawOffset)
        {
            rawOffset = -1;
            if (klass == IntPtr.Zero || auraMonoClassGetFieldFromName == null || auraMonoFieldGetOffset == null)
            {
                return false;
            }
            IntPtr field = auraMonoClassGetFieldFromName(klass, fieldName);
            if (field == IntPtr.Zero)
            {
                return false;
            }
            // mono_field_get_offset includes the MonoObject header (2*IntPtr); a raw value buffer has none.
            rawOffset = (int)auraMonoFieldGetOffset(field) - 2 * IntPtr.Size;
            return rawOffset >= 0;
        }

        private unsafe IntPtr InflateGenericDispatch(IntPtr openMethod, IntPtr argClass)
        {
            IntPtr argType = auraMonoClassGetType(argClass);
            if (argType == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            IntPtr* typeArgs = stackalloc IntPtr[1];
            typeArgs[0] = argType;
            IntPtr genericInst = auraMonoMetadataGetGenericInst(1, (IntPtr)typeArgs);
            if (genericInst == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            MonoGenericContext context = new MonoGenericContext
            {
                class_inst = IntPtr.Zero,
                method_inst = genericInst
            };
            IntPtr inflated = auraMonoClassInflateGenericMethod(openMethod, ref context);
            if (inflated != IntPtr.Zero && auraMonoCompileMethod != null)
            {
                try { auraMonoCompileMethod(inflated); }
                catch { }
            }
            return inflated;
        }

        private void SyncGameTrackMarkers()
        {
            if (!this.EnsureMapTrackReady() || !this.AttachAuraMonoThread())
            {
                return;
            }

            this.mapTrackCandidates.Clear();
            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            float maxDistance = Mathf.Max(25f, this.radarMaxDistance);

            Transform containerTransform = this.radarContainer.transform;
            int childCount = containerTransform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = containerTransform.GetChild(i);
                if (child == null || child.gameObject == null)
                {
                    continue;
                }

                RadarMarkerMetadata metadata = this.GetMarkerMetadata(child.gameObject);
                if (metadata == null || string.IsNullOrWhiteSpace(metadata.CanonicalLabel))
                {
                    continue;
                }

                // Skip objects currently on cooldown (depleted). Their collectable entity has no live
                // produce, so a map marker would just show the generic placeholder flag — and there's
                // nothing to collect there right now anyway.
                if (metadata.IsCooldown)
                {
                    continue;
                }

                string label = metadata.CanonicalLabel.Trim();
                if (!this.IsResourceVisualEspLabel(label))
                {
                    continue;
                }

                // "Player" markers get a world track ONLY when the avatar feature is on: the pointer's avatar is
                // rendered by MapTrackWidget.SetData's friend branch, which we enable via the SCOPED force-friend
                // (mapTrackWidgetRendering) — no dialog leak. With the feature off we skip them (vanilla shows
                // players on the maps via server Player spots; a bare world pin would just be clutter). Morphs
                // stay tracked regardless (hide-and-seek reveal).
                if (string.Equals(label, "Player", StringComparison.Ordinal) && !this.radarPlayerAvatarsAll)
                {
                    continue;
                }

                Vector3 pos = child.position;
                float distSqr = (pos - camPos).sqrMagnitude;
                if (cam != null)
                {
                    float itemMaxDistance = string.Equals(label, "Bubble", StringComparison.Ordinal)
                        ? Mathf.Max(BubbleRadarMaxDistance, maxDistance)
                        : maxDistance;
                    if (distSqr > itemMaxDistance * itemMaxDistance)
                    {
                        continue;
                    }
                }

                ulong token = MapTrackTokenTag | (uint)this.GetGameMapSpotUsageId(child.gameObject, label, pos);
                byte trackType = GetMapTrackTypeForLabel(label);
                bool replaced = false;
                for (int c = 0; c < this.mapTrackCandidates.Count; c++)
                {
                    if (this.mapTrackCandidates[c].Token == token)
                    {
                        if (distSqr < this.mapTrackCandidates[c].DistanceSqr)
                        {
                            this.mapTrackCandidates[c] = new MapTrackCandidate { Token = token, Position = pos, DistanceSqr = distSqr, TrackType = trackType, Label = label };
                        }
                        replaced = true;
                        break;
                    }
                }
                if (!replaced)
                {
                    this.mapTrackCandidates.Add(new MapTrackCandidate { Token = token, Position = pos, DistanceSqr = distSqr, TrackType = trackType, Label = label });
                }
            }

            int limit = Mathf.Clamp(this.radarGameTrackLimit, 1, 30);
            this.mapTrackCandidates.Sort((a, b) => a.DistanceSqr.CompareTo(b.DistanceSqr));

            // Refresh live collectable entities (pos + resource static id) for real per-resource icons.
            this.RefreshCollectableScan();

            int matched = 0;
            this.mapTrackDesired.Clear();
            this.mapTrackDesiredType.Clear();
            this.mapTrackDesiredStaticId.Clear();
            this.mapTrackDesiredTargetNet.Clear();
            this.mapBigSpotDesired.Clear();
            this.mapAvatarWorldNetIdsNext.Clear();

            // Refresh remote-player entities only when player markers are actually on the radar.
            bool anyPlayerCandidates = false;
            for (int i = 0; i < this.mapTrackCandidates.Count; i++)
            {
                if (this.mapTrackCandidates[i].TrackType == MapTrackTypePlayer)
                {
                    anyPlayerCandidates = true;
                    break;
                }
            }
            if (anyPlayerCandidates)
            {
                this.RefreshRemotePlayerScan();
            }
            for (int i = 0; i < this.mapTrackCandidates.Count && this.mapTrackDesired.Count < limit; i++)
            {
                MapTrackCandidate cand = this.mapTrackCandidates[i];
                byte type = cand.TrackType;
                int staticId = MapTrackSyntheticStaticId;
                // Only generic resource markers (NavigationPoint) get the per-resource item icon. Bird/Fish/
                // Insect already carry real category icons and Player uses the native player pin, so leave
                // those types alone (don't override them into a Furniture/item icon).
                // Match to the nearest live collectable entity. Prefer its produced item id (drop material
                // icon, works for all: wood/stone/bamboo/fruit/mushroom); fall back to the entity static id
                // (correct for mushroom-type gathers). Furniture track -> NormalItem icon via GetIconName.
                // "Bubble" is NavigationPoint but NOT a collectable: a bubble drifting within the 3 m XZ match
                // radius of a random bush/tree would steal its item icon and poison mapTrackLabelIcon["Bubble"]
                // for every other bubble (random icon on the Bubbles radar) — keep bubbles on the plain flag.
                // "Contaminated" (sea-clean pollutant) is likewise NavigationPoint but NOT a collectable —
                // position-matching it stole a neighboring wakame's icon. Pin it to the decadopecten seashell
                // item (Furniture track -> NormalItem, same seashell as the ESP overlay) BEFORE the match
                // block so it never falls into the collectable position-match.
                if (string.Equals(cand.Label, "Contaminated", StringComparison.Ordinal))
                {
                    type = MapTrackTypeFurniture;
                    staticId = ContaminatedMapIconStaticId;
                }
                else if (type == MapTrackTypeNavigationPoint
                    && !string.Equals(cand.Label, "Bubble", StringComparison.Ordinal))
                {
                    bool didMatch = this.TryMatchCollectable(cand.Position, out int resStaticId, out int resProduceId, out bool resOnCooldown);
                    if (didMatch && resOnCooldown)
                    {
                        // Authoritative: this resource is depleted (on cooldown) -> no marker, even if the
                        // radar's own (local) cooldown tracking thinks it's still active.
                        continue;
                    }
                    if (didMatch)
                    {
                        // Resolve the collectable-atlas icon id. Priority:
                        //  1) produce drop-item id WITH a collectable sprite (materials: timber/stone/fruit).
                        //  2) atlas item-name == radar label (mushrooms: produceId=0, entity id 130005 has no
                        //     sprite, but "Shiitake" -> 48002 which IS in the atlas).
                        //  3) produce drop-item id WITHOUT a collectable sprite (meteor -> Starfall Shard
                        //     40034-40036): Furniture track -> the item's NormalItem icon (MapResource would
                        //     render blank). Until the atlas is enumerated the set is empty -> optimistic (1);
                        //     self-corrects once the atlas loads (type change re-dispatches the track).
                        //  4) entity static id fallback (NormalItem only, minimap via Furniture).
                        bool fromProduce = this.TryGetProduceItemId(resProduceId, out int produceItemId);
                        bool produceInAtlas = fromProduce && produceItemId > 0
                            && (this.mapAtlasIdSet.Count == 0 || this.mapAtlasIdSet.Contains(produceItemId));
                        bool useMapResource;
                        int iconItemId;
                        string how;
                        if (produceInAtlas)
                        {
                            iconItemId = produceItemId; useMapResource = true; how = "produce";
                        }
                        else if (this.TryResolveCollectableIdByLabel(cand.Label, out int collId))
                        {
                            iconItemId = collId; useMapResource = true; how = "atlasName";
                        }
                        else if (fromProduce && produceItemId > 0)
                        {
                            iconItemId = produceItemId; useMapResource = false; how = "produceItem";
                        }
                        else
                        {
                            iconItemId = resStaticId; useMapResource = false; how = "entity";
                        }
                        if (iconItemId > 0)
                        {
                            type = useMapResource ? MapTrackTypeMapResource : MapTrackTypeFurniture;
                            staticId = iconItemId;
                            matched++;
                            if (!string.IsNullOrEmpty(cand.Label))
                            {
                                this.mapTrackLabelIcon[cand.Label] = iconItemId; // remember icon for this resource type
                                if (useMapResource) this.mapTrackLabelProduce.Add(cand.Label);
                                else this.mapTrackLabelProduce.Remove(cand.Label);
                            }
                            if (!string.IsNullOrEmpty(cand.Label) && this.mapTrackResolveDiag.Add(cand.Label + "|" + how))
                            {
                                ModLogger.Msg("[MapSpots] resolve '" + cand.Label + "' produceId=" + resProduceId
                                    + " via=" + how + " itemId=" + iconItemId
                                    + " entityStaticId=" + resStaticId + " type=" + (type == MapTrackTypeMapResource ? "MapResource" : "Furniture"));
                            }
                        }
                        else if (!string.IsNullOrEmpty(cand.Label) && this.mapTrackResolveDiag.Add(cand.Label))
                        {
                            ModLogger.Msg("[MapSpots] resolve '" + cand.Label + "' produceId=" + resProduceId
                                + " fromProduce=" + fromProduce + " itemId=0 entityStaticId=" + resStaticId + " -> NO ICON (flag)");
                        }
                    }
                    else if (!string.IsNullOrEmpty(cand.Label)
                        && this.mapTrackLabelIcon.TryGetValue(cand.Label, out int cachedIcon) && cachedIcon > 0)
                    {
                        // No live entity here (distant / streamed out), but we've resolved this resource type
                        // before -> reuse its icon so far markers aren't stuck on the placeholder flag.
                        type = this.mapTrackLabelProduce.Contains(cand.Label) ? MapTrackTypeMapResource : MapTrackTypeFurniture;
                        staticId = cachedIcon;
                        matched++;
                    }
                    else if (this.mapTrackMatchDiagCount < 12)
                    {
                        // Diagnose unmatched resource markers with no cached icon yet.
                        this.mapTrackMatchDiagCount++;
                        float nearSqr = this.GetNearestCollectableInfo(cand.Position, out int nearProduceId, out int nearStaticId);
                        ModLogger.Msg("[MapSpots] unmatched '" + cand.Label + "' nearestDist="
                            + (nearSqr >= float.MaxValue ? -1f : Mathf.Sqrt(nearSqr)).ToString("F2")
                            + " nearProduceId=" + nearProduceId + " nearStaticId=" + nearStaticId
                            + " (collectables=" + this.mapResEntities.Count + ")");
                    }
                }
                // TargetNetId per track: Player -> the real player netId (position-matched against the
                // RemotePlayerComponent scan) so the game's native friend-avatar path and vanilla-spot dedup
                // work; MapResource -> the big-map spot usageId (low token bits); else 0.
                uint desiredTargetNet = 0u;
                if (type == MapTrackTypePlayer && this.TryMatchRemotePlayer(cand.Position, out uint playerNetId))
                {
                    desiredTargetNet = playerNetId;
                    this.mapAvatarWorldNetIdsNext.Add(playerNetId); // force-friend this one for the world pointer
                }
                else if (type == MapTrackTypeMapResource)
                {
                    desiredTargetNet = unchecked((uint)(cand.Token & 0xFFFFFFFFUL));
                }

                this.mapTrackDesired[cand.Token] = cand.Position;
                this.mapTrackDesiredType[cand.Token] = type;
                this.mapTrackDesiredStaticId[cand.Token] = staticId;
                this.mapTrackDesiredTargetNet[cand.Token] = desiredTargetNet;

                // Big map: a MapResource marker (drop-item icon) gets a per-position Collectable map-spot,
                // keyed by a UNIQUE usageId (low bits of the token). The spot borrows its icon from the
                // matching MapResource track (StaticId=itemId, TargetNetId=usageId), so it shows the real
                // item icon per location instead of one-per-type.
                if (this.radarBigMapSpots && type == MapTrackTypeMapResource && staticId > 0)
                {
                    int spotUsageId = unchecked((int)(uint)(cand.Token & 0xFFFFFFFFUL));
                    this.mapBigSpotDesired[spotUsageId] = cand.Position;
                }
            }

            // Remove tracks no longer desired.
            this.mapTrackRemoveBuffer.Clear();
            foreach (KeyValuePair<ulong, Vector3> entry in this.mapTrackInjected)
            {
                if (!this.mapTrackDesired.ContainsKey(entry.Key))
                {
                    this.mapTrackRemoveBuffer.Add(entry.Key);
                }
            }
            int removed = 0, removeFail = 0;
            for (int i = 0; i < this.mapTrackRemoveBuffer.Count; i++)
            {
                ulong token = this.mapTrackRemoveBuffer[i];
                if (this.DispatchStopTrack(token))
                {
                    removed++;
                }
                else
                {
                    removeFail++;
                }
                this.mapTrackInjected.Remove(token);
                this.mapTrackInjectedType.Remove(token);
                this.mapTrackInjectedStaticId.Remove(token);
                this.mapTrackInjectedTargetNet.Remove(token);
            }

            // Add new / refresh changed tracks (re-dispatching StartTrack overwrites the entry by token).
            int addOk = 0, addFail = 0;
            foreach (KeyValuePair<ulong, Vector3> entry in this.mapTrackDesired)
            {
                byte trackType = this.mapTrackDesiredType.TryGetValue(entry.Key, out byte t) ? t : MapTrackTypeNavigationPoint;
                int staticId = this.mapTrackDesiredStaticId.TryGetValue(entry.Key, out int s) ? s : MapTrackSyntheticStaticId;
                uint targetNet = this.mapTrackDesiredTargetNet.TryGetValue(entry.Key, out uint tn) ? tn : 0u;

                bool isNew = !this.mapTrackInjected.TryGetValue(entry.Key, out Vector3 prev);
                if (!isNew)
                {
                    // Re-dispatch if it moved OR its icon (type/staticId) or TargetNetId changed -- e.g. a
                    // placeholder that resolved to a real icon, or a player marker that just matched its
                    // netId (enables the native friend-avatar path).
                    byte prevType = this.mapTrackInjectedType.TryGetValue(entry.Key, out byte pt) ? pt : MapTrackTypeNavigationPoint;
                    int prevStaticId = this.mapTrackInjectedStaticId.TryGetValue(entry.Key, out int ps) ? ps : MapTrackSyntheticStaticId;
                    uint prevTargetNet = this.mapTrackInjectedTargetNet.TryGetValue(entry.Key, out uint ptn) ? ptn : 0u;
                    if ((entry.Value - prev).sqrMagnitude <= MapTrackMoveThresholdSqr
                        && prevType == trackType && prevStaticId == staticId && prevTargetNet == targetNet)
                    {
                        continue; // unchanged
                    }
                }

                if (this.DispatchStartTrack(entry.Key, entry.Value, trackType, staticId, targetNet))
                {
                    this.mapTrackInjected[entry.Key] = entry.Value;
                    this.mapTrackInjectedType[entry.Key] = trackType;
                    this.mapTrackInjectedStaticId[entry.Key] = staticId;
                    this.mapTrackInjectedTargetNet[entry.Key] = targetNet;
                    if (isNew) addOk++;
                }
                else if (isNew)
                {
                    addFail++;
                }
            }

            // Count Player-type candidates for the avatar-path diagnostic (how many player pointers the
            // radar saw, vs how many got force-friended for the non-friend avatar).
            int playerCandidates = 0;
            for (int pc = 0; pc < this.mapTrackCandidates.Count; pc++)
            {
                if (this.mapTrackCandidates[pc].TrackType == MapTrackTypePlayer)
                {
                    playerCandidates++;
                }
            }

            if (this.mapTrackDiagSyncs < 5 || addOk > 0 || removed > 0 || removeFail > 0 || addFail > 0 || playerCandidates > 0)
            {
                this.mapTrackDiagSyncs++;
                ModLogger.Msg("[MapSpots] track sync: candidates=" + this.mapTrackCandidates.Count
                    + " desired=" + this.mapTrackDesired.Count + " injected=" + this.mapTrackInjected.Count
                    + " collectables=" + this.mapResEntities.Count + " matched=" + matched
                    + " addOk=" + addOk + " addFail=" + addFail + " removed=" + removed + " removeFail=" + removeFail
                    // Avatar-path diagnostic: avatarsAll=toggle, playerCand=Player spots seen,
                    // forceFriend=players matched to a real netId (the non-friend avatar set).
                    + " | avatarsAll=" + this.radarPlayerAvatarsAll + " playerCand=" + playerCandidates
                    + " forceFriend=" + this.mapAvatarWorldNetIdsNext.Count);
            }

            // Publish the world-pointer force-friend set for the FriendClientService detour (swap in one
            // assignment so the static hook always sees a consistent set).
            mapAvatarWorldNetIds = new HashSet<uint>(this.mapAvatarWorldNetIdsNext);

            // Big-map markers (Collectable spots) from the same resolved item ids.
            this.SyncBigMapSpots();
        }

        // Scan live collectable entities; store world position + resource ENTITY static id
        // (EntityUtil.GetEntityResId) so a marker can use a real per-resource icon via Furniture track.
        private void RefreshCollectableScan()
        {
            float now = Time.unscaledTime;
            if (now < this.mapResNextScanAt)
            {
                return;
            }
            this.mapResNextScanAt = now + MapResScanInterval;

            if (this.mapResCollectableClass == IntPtr.Zero)
            {
                // Retry each scan until the image is loaded (do not lock on first miss).
                this.mapResCollectableClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.Gameplay.Component.Gather.CollectableObjectComponent");
                if (this.mapResCollectableClass == IntPtr.Zero)
                {
                    this.mapResCollectableClass = this.FindAuraMonoClassByFullName(
                        "XDTLevelAndEntity.GamePlay.Component.Gather.CollectableObjectComponent");
                }
                if (this.mapResCollectableClass == IntPtr.Zero)
                {
                    if (!this.mapResClassResolveTried)
                    {
                        this.mapResClassResolveTried = true;
                        ModLogger.Msg("[MapSpots] collectable scan: CollectableObjectComponent class NOT resolved (retrying)");
                    }
                    return;
                }
            }

            this.EnsureEntityResIdMethods();
            this.EnsureProduceMethod();

            this.mapResEntities.Clear();
            this.liveCollectableColds.Clear();
            // Pin the enumerated components, and each derived entity below, across their field reads:
            // GetEntityResId boxes its int return -> allocation -> the moving sgen GC may relocate an
            // unpinned component/entity mid-loop, and reading (or invoking on) a moved object crashes
            // hard (often with no WER dump). compPins is released once the loop is done.
            List<uint> compPins = new List<uint>();
            if (!this.TryAuraMonoGetComponentObjects(this.mapResCollectableClass, out List<IntPtr> components, compPins) || components == null)
            {
                FreeAuraMonoPins(compPins);
                if (!this.mapResDiagLogged)
                {
                    this.mapResDiagLogged = true;
                    ModLogger.Msg("[MapSpots] collectable scan: GetComponents returned null/false (class ok)");
                }
                return;
            }
            if (components.Count == 0 && !this.mapResDiagLogged)
            {
                this.mapResDiagLogged = true;
                ModLogger.Msg("[MapSpots] collectable scan: GetComponents returned 0 entities (class ok)");
            }

            // Scalarize each component (position + resource static id) immediately; never hold the IntPtrs.
            int rawCount = components.Count;
            int withEntity = 0, withPos = 0;
            uint sampleNetId = 0; int sampleItemTypeId = 0, sampleResId = 0, sampleStaticId = 0; bool sampled = false;
            try
            {
                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr comp = components[i];
                    if (comp == IntPtr.Zero)
                    {
                        continue;
                    }
                    if (!this.TryGetMonoObjectMember(comp, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero)
                    {
                        continue;
                    }
                    withEntity++;

                    // entity is a distinct object from comp (comp is pinned via compPins, but the entity
                    // is not) and we read it several times + invoke GetEntityResId on it -> pin it for
                    // the duration of these reads against the moving sgen GC.
                    uint entityPin = AuraMonoPinNew(entityObj);
                    try
                    {
                        Vector3 pos;
                        if (!this.TryGetMonoVector3Member(entityObj, "position", out pos))
                        {
                            continue;
                        }
                        withPos++;

                        // EntityUtil.GetEntityResId(entity) -> StaticEntityData.resourceID = the entity static id
                        // that GetIconName decodes into the real prefab/item icon.
                        int staticId = 0;
                        this.TryGetCollectableStaticIdViaAura(entityObj, out staticId);
                        this.TryGetMonoInt32Member(comp, "itemTypeID", out int produceId);
                        // Authoritative depletion state straight from the live entity (the radar's own cooldown
                        // tracking is local-only and misses resources already depleted by others / before login).
                        // Some resource families never set inCold when drained — they only zero availableNum
                        // (their shape also leaves the axe-checker) — so both count as depleted. availableNum
                        // is trusted only on a successful read: the out param is 0 on failure too.
                        bool onCooldown = this.TryGetMonoBoolMember(comp, "inCold", out bool inCold) && inCold;
                        if (!onCooldown
                            && this.TryGetMonoInt32Member(comp, "availableNum", out int liveAvailableNum)
                            && liveAvailableNum == 0)
                        {
                            onCooldown = true;
                        }

                        long liveColdEndMs = 0L;
                        if (onCooldown && this.TryGetMonoUInt64Member(comp, "coldEndTime", out ulong coldEndRaw))
                        {
                            liveColdEndMs = (long)coldEndRaw;
                        }
                        this.liveCollectableColds.Add(new LiveCollectableCold { Position = pos, OnCooldown = onCooldown, ColdEndMs = liveColdEndMs });

                        if (!sampled)
                        {
                            sampled = true;
                            this.TryGetMonoUInt32Member(entityObj, "netId", out sampleNetId);
                            sampleStaticId = staticId;
                            sampleItemTypeId = produceId;
                            sampleResId = this.mapResGetResIdMethods.Count;
                        }

                        if (staticId <= 0 && produceId <= 0)
                        {
                            continue;
                        }
                        this.mapResEntities.Add(new MapResEntity { Position = pos, StaticId = staticId, ProduceId = produceId, OnCooldown = onCooldown });
                    }
                    finally
                    {
                        AuraMonoPinFree(entityPin);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(compPins);
            }

            this.liveCollectableScanCompletedAt = Time.unscaledTime;

            if (!this.mapResDiagLogged && rawCount > 0)
            {
                this.mapResDiagLogged = true;
                ModLogger.Msg("[MapSpots] collectable scan raw=" + rawCount + " withEntity=" + withEntity
                    + " withPos=" + withPos + " usable=" + this.mapResEntities.Count
                    + " | sample netId=" + sampleNetId + " resIdMethods=" + sampleResId
                    + " itemTypeId=" + sampleItemTypeId + " staticId=" + sampleStaticId);
            }
        }

        // Scan live remote players (RemotePlayerComponent view components): world position + entity netId.
        // Same pinning discipline as the collectable scan: components pinned by the enumerator, each derived
        // entity pinned across its field reads (moving sgen GC).
        private void RefreshRemotePlayerScan()
        {
            float now = Time.unscaledTime;
            if (now < this.mapPlayerNextScanAt)
            {
                return;
            }
            this.mapPlayerNextScanAt = now + MapPlayerScanInterval;

            if (this.mapPlayerClass == IntPtr.Zero)
            {
                // Retry each scan until the image is loaded (do not lock on first miss).
                this.mapPlayerClass = this.FindAuraMonoClassByFullName(
                    "XDTLevelAndEntity.Gameplay.Component.Player.RemotePlayerComponent");
                if (this.mapPlayerClass == IntPtr.Zero)
                {
                    if (!this.mapPlayerClassResolveTried)
                    {
                        this.mapPlayerClassResolveTried = true;
                        ModLogger.Msg("[MapSpots] player scan: RemotePlayerComponent class NOT resolved (retrying)");
                    }
                    return;
                }
            }

            this.mapPlayerEntities.Clear();
            // Build the next real-name map (shortId -> pinned name MonoString) from this scan.
            this.mapNameByShortIdNext.Clear();
            this.mapNamePinsNext.Clear();
            IntPtr friendInstance = this.EnsureNameReadReady();

            List<uint> compPins = new List<uint>();
            if (!this.TryAuraMonoGetComponentObjects(this.mapPlayerClass, out List<IntPtr> components, compPins) || components == null)
            {
                FreeAuraMonoPins(compPins);
                return;
            }
            try
            {
                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr comp = components[i];
                    if (comp == IntPtr.Zero)
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
                        if (!this.TryGetMonoVector3Member(entityObj, "position", out Vector3 pos))
                        {
                            continue;
                        }
                        if (!this.TryGetMonoUInt32Member(entityObj, "netId", out uint netId) || netId == 0u)
                        {
                            continue;
                        }
                        this.mapPlayerEntities.Add(new MapPlayerEntity { Position = pos, NetId = netId });

                        // Real name: PlayerProfile.Name from FriendSystem.GetUserProfile(netId), keyed by the
                        // decoded shortId. Pin the string so the GetPlayerName detour can return it safely
                        // until the next scan rebuilds the map.
                        if (friendInstance != IntPtr.Zero
                            && this.TryReadPlayerName(friendInstance, netId, out long shortId, out IntPtr nameStr, out uint namePin, out string avatarUrl)
                            && nameStr != IntPtr.Zero)
                        {
                            if (this.mapNameByShortIdNext.ContainsKey(shortId))
                            {
                                AuraMonoPinFree(namePin); // already cached this player this pass
                            }
                            else
                            {
                                this.mapNamePinsNext.Add(namePin);
                                this.mapNameByShortIdNext[shortId] = nameStr;
                            }

                            if (!string.IsNullOrEmpty(avatarUrl))
                            {
                                this.mapAvatarUrlByNetIdNext[netId] = avatarUrl;
                            }
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(entityPin);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(compPins);
            }

            // Publish the new name map (backed by the Next pins), then release the previous pins.
            mapNameByShortId = new Dictionary<long, IntPtr>(this.mapNameByShortIdNext);
            FreeAuraMonoPins(this.mapNamePins);
            this.mapNamePins.Clear();
            this.mapNamePins.AddRange(this.mapNamePinsNext);
            this.mapNamePinsNext.Clear();

            // Publish the avatar-url map (plain managed strings — no pins).
            this.mapAvatarUrlByNetId = new Dictionary<uint, string>(this.mapAvatarUrlByNetIdNext);
            this.mapAvatarUrlByNetIdNext.Clear();

            if (!this.mapPlayerDiagLogged && this.mapPlayerEntities.Count > 0)
            {
                this.mapPlayerDiagLogged = true;
                ModLogger.Msg("[MapSpots] player scan: remotePlayers=" + this.mapPlayerEntities.Count
                    + " named=" + mapNameByShortId.Count + " sample netId=" + this.mapPlayerEntities[0].NetId);
            }

        }

        // Resolve FriendSystem class + GetUserProfile(1-arg) overloads + PlayerProfile.Name offset (once),
        // then return the live FriendSystem DataModule instance (or Zero if not ready).
        private IntPtr EnsureNameReadReady()
        {
            if (!this.mapNameMethodsTried)
            {
                if (auraMonoClassGetMethods == null || auraMonoMethodGetName == null
                    || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
                {
                    return IntPtr.Zero;
                }
                if (this.mapNameFriendSystemClass == IntPtr.Zero)
                {
                    this.mapNameFriendSystemClass = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.Social.FriendSystem");
                    if (this.mapNameFriendSystemClass == IntPtr.Zero)
                    {
                        return IntPtr.Zero; // image not loaded yet — retry later
                    }
                }
                if (this.mapNameGetProfileMethods.Count == 0)
                {
                    IntPtr iter = IntPtr.Zero;
                    while (true)
                    {
                        IntPtr m = auraMonoClassGetMethods(this.mapNameFriendSystemClass, ref iter);
                        if (m == IntPtr.Zero) break;
                        string nm = Marshal.PtrToStringAnsi(auraMonoMethodGetName(m)) ?? string.Empty;
                        if (nm == "GetUserProfile" && AuraMonoMethodParamCountIs(m, 1u))
                        {
                            this.mapNameGetProfileMethods.Add(m);
                        }
                    }
                }
                if (this.mapNameProfileNameOffset < 0 || this.mapNameProfileIdOffset < 0
                    || this.mapNameTitleStringOffset < 0)
                {
                    IntPtr profCls = this.FindAuraMonoClassByFullName("XDTGameSystem.PlayerService.PlayerProfile");
                    if (profCls != IntPtr.Zero)
                    {
                        this.TryGetTrackFieldRawOffset(profCls, "Name", out this.mapNameProfileNameOffset);
                        this.TryGetTrackFieldRawOffset(profCls, "Id", out this.mapNameProfileIdOffset);
                        this.TryGetTrackFieldRawOffset(profCls, "AvatarImageUrl", out this.mapNameProfileAvatarUrlOffset);
                        // Title._titleString within the PlayerProfile buffer = Title field offset (embedded
                        // value type) + _titleString offset within PlayerTitle.
                        IntPtr titleCls = this.FindAuraMonoClassByFullName("XDTGameSystem.PlayerService.PlayerTitle");
                        if (titleCls != IntPtr.Zero
                            && this.TryGetTrackFieldRawOffset(profCls, "Title", out int titleFieldOff)
                            && this.TryGetTrackFieldRawOffset(titleCls, "_titleString", out int titleStrOff))
                        {
                            this.mapNameTitleStringOffset = titleFieldOff + titleStrOff;
                        }
                    }
                }
                if (this.mapNameDecodeShortIdMethod == IntPtr.Zero)
                {
                    IntPtr shortIdCls = this.FindAuraMonoClassByFullName("Sazabi.Login.Shared.ShortIdUtil");
                    if (shortIdCls == IntPtr.Zero)
                    {
                        shortIdCls = this.FindAuraMonoClassInAllLoadedImages("ShortIdUtil", "Sazabi.Login.Shared");
                    }
                    if (shortIdCls != IntPtr.Zero)
                    {
                        this.mapNameDecodeShortIdMethod = this.FindAuraMonoMethodOnHierarchy(shortIdCls, "DecodeShortId", 1);
                    }
                }
                if (this.mapNamePlayerServiceClass == IntPtr.Zero)
                {
                    this.mapNamePlayerServiceClass = this.FindAuraMonoClassByFullName("XDTGameSystem.PlayerService.PlayerServiceSystem");
                }
                if (this.mapNameGetProfileMethods.Count == 0 || this.mapNameProfileNameOffset < 0
                    || this.mapNameProfileIdOffset < 0 || this.mapNameTitleStringOffset < 0
                    || this.mapNameDecodeShortIdMethod == IntPtr.Zero
                    || this.mapNamePlayerServiceClass == IntPtr.Zero)
                {
                    return IntPtr.Zero; // not fully resolved yet — retry (don't lock)
                }
                this.mapNameMethodsTried = true;
                ModLogger.Msg("[MapSpots] name read: GetUserProfile overloads=" + this.mapNameGetProfileMethods.Count
                    + " Name@" + this.mapNameProfileNameOffset + " Id@" + this.mapNameProfileIdOffset
                    + " Title.str@" + this.mapNameTitleStringOffset
                    + " decode=" + (this.mapNameDecodeShortIdMethod != IntPtr.Zero));
            }
            return this.TryGetAuraMonoDataModuleInstance(this.mapNameFriendSystemClass);
        }

        // Invoke FriendSystem.GetUserProfile(netId) -> boxed PlayerProfile -> read Name MonoString + Id
        // (encoded shortId), decode Id to the raw shortId. Tries both 1-arg overloads (uint netId vs long
        // shortId); the netId overload returns a populated profile -> keep whichever yields a name.
        private unsafe bool TryReadPlayerName(IntPtr friendInstance, uint netId, out long shortId, out IntPtr nameStr, out uint namePin, out string avatarUrl)
        {
            shortId = 0L;
            nameStr = IntPtr.Zero;
            namePin = 0u;
            avatarUrl = null;
            uint arg = netId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&arg);
            for (int i = 0; i < this.mapNameGetProfileMethods.Count; i++)
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr boxed;
                mapNameReadingSelf = true; // suppress the Title-mirror detour for our own cache-read invoke
                try
                {
                    boxed = auraMonoRuntimeInvoke(this.mapNameGetProfileMethods[i], friendInstance, (IntPtr)args, ref exc);
                }
                finally
                {
                    mapNameReadingSelf = false;
                }
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    continue;
                }
                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero)
                {
                    continue;
                }
                IntPtr candidate = Marshal.ReadIntPtr(raw, this.mapNameProfileNameOffset);
                IntPtr idStr = Marshal.ReadIntPtr(raw, this.mapNameProfileIdOffset);
                if (candidate == IntPtr.Zero)
                {
                    continue;
                }
                // Pin the name string BEFORE reading it: TryReadMonoString allocates, which can trigger a GC
                // that would otherwise move `candidate` -> stale pointer -> crash when the hook returns it.
                uint pin = AuraMonoPinNew(candidate);
                if (this.TryReadMonoString(candidate, out string s) && !string.IsNullOrEmpty(s)
                    && idStr != IntPtr.Zero && this.TryDecodeShortId(idStr, out shortId) && shortId != 0L)
                {
                    nameStr = candidate;
                    namePin = pin;
                    // Also copy the avatar image url out of the SAME profile buffer (managed string copy →
                    // no pin needed past this scope). Powers the ESP-beacon player-avatar icon.
                    if (this.mapNameProfileAvatarUrlOffset >= 0)
                    {
                        IntPtr avatarStr = Marshal.ReadIntPtr(raw, this.mapNameProfileAvatarUrlOffset);
                        if (avatarStr != IntPtr.Zero)
                        {
                            uint apin = AuraMonoPinNew(avatarStr);
                            if (this.TryReadMonoString(avatarStr, out string au) && !string.IsNullOrEmpty(au))
                            {
                                avatarUrl = au;
                            }
                            AuraMonoPinFree(apin);
                        }
                    }
                    if (i != 0) // move the working overload to the front so we stop probing the wrong one
                    {
                        IntPtr good = this.mapNameGetProfileMethods[i];
                        this.mapNameGetProfileMethods.RemoveAt(i);
                        this.mapNameGetProfileMethods.Insert(0, good);
                    }
                    if (!this.mapNameDiagLogged)
                    {
                        this.mapNameDiagLogged = true;
                        ModLogger.Msg("[MapSpots] name read: netId=" + netId + " shortId=" + shortId + " name='" + s + "'");
                    }
                    return true;
                }
                AuraMonoPinFree(pin); // empty/invalid — release
            }
            return false;
        }

        // ShortIdUtil.DecodeShortId(encodedId) -> raw shortId (long). Static; string arg passed directly.
        private unsafe bool TryDecodeShortId(IntPtr encodedIdStr, out long shortId)
        {
            shortId = 0L;
            if (this.mapNameDecodeShortIdMethod == IntPtr.Zero || encodedIdStr == IntPtr.Zero
                || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return false;
            }
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = encodedIdStr; // reference-type (string) arg = object pointer directly
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.mapNameDecodeShortIdMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }
            IntPtr rawv = auraMonoObjectUnbox(boxed);
            if (rawv == IntPtr.Zero)
            {
                return false;
            }
            shortId = *(long*)rawv;
            return true;
        }

        private bool TryMatchRemotePlayer(Vector3 pos, out uint netId)
        {
            netId = 0u;
            float bestSqr = MapPlayerMatchRadiusSqr;
            bool found = false;
            for (int i = 0; i < this.mapPlayerEntities.Count; i++)
            {
                float dx = this.mapPlayerEntities[i].Position.x - pos.x;
                float dz = this.mapPlayerEntities[i].Position.z - pos.z;
                float sqr = dx * dx + dz * dz;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    netId = this.mapPlayerEntities[i].NetId;
                    found = true;
                }
            }
            return found;
        }

        private void EnsureEntityResIdMethods()
        {
            if (this.mapResEntityUtilTried)
            {
                return;
            }
            if (auraMonoClassGetMethods == null || auraMonoMethodGetName == null
                || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return;
            }
            IntPtr cls = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil");
            if (cls == IntPtr.Zero)
            {
                cls = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtilExtensions");
            }
            if (cls == IntPtr.Zero)
            {
                return; // retry next scan (image may not be loaded yet)
            }

            this.mapResEntityUtilTried = true;
            IntPtr iter = IntPtr.Zero;
            while (true)
            {
                IntPtr m = auraMonoClassGetMethods(cls, ref iter);
                if (m == IntPtr.Zero)
                {
                    break;
                }
                string nm = Marshal.PtrToStringAnsi(auraMonoMethodGetName(m)) ?? string.Empty;
                if (nm == "GetEntityResId" && AuraMonoMethodParamCountIs(m, 1u))
                {
                    this.mapResGetResIdMethods.Add(m);
                }
            }
            ModLogger.Msg("[MapSpots] EntityUtil.GetEntityResId 1-arg methods=" + this.mapResGetResIdMethods.Count);
        }

        // Invoke EntityUtil.GetEntityResId with the entity object. Safe for both 1-param overloads:
        // the Entity overload returns the real static id; the uint overload reads garbage -> dict miss -> 0.
        private unsafe bool TryGetCollectableStaticIdViaAura(IntPtr entityObj, out int staticId)
        {
            staticId = 0;
            if (entityObj == IntPtr.Zero || this.mapResGetResIdMethods.Count == 0
                || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return false;
            }
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = entityObj; // reference-type arg = the object pointer
            for (int i = 0; i < this.mapResGetResIdMethods.Count; i++)
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(this.mapResGetResIdMethods[i], IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    continue;
                }
                int v = *(int*)auraMonoObjectUnbox(boxed);
                if (v > 0)
                {
                    staticId = v;
                    return true;
                }
            }
            return false;
        }

        private bool TryMatchCollectable(Vector3 pos, out int staticId, out int produceId, out bool onCooldown)
        {
            staticId = 0;
            produceId = 0;
            onCooldown = false;
            float bestSqr = MapResMatchRadiusSqr;
            bool found = false;
            for (int i = 0; i < this.mapResEntities.Count; i++)
            {
                float dx = this.mapResEntities[i].Position.x - pos.x;
                float dz = this.mapResEntities[i].Position.z - pos.z;
                float sqr = dx * dx + dz * dz;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    staticId = this.mapResEntities[i].StaticId;
                    produceId = this.mapResEntities[i].ProduceId;
                    onCooldown = this.mapResEntities[i].OnCooldown;
                    found = true;
                }
            }
            return found;
        }

        // Nearest collectable to pos (XZ) regardless of match radius — diagnostics only. Returns dist^2
        // (float.MaxValue if none) and the nearest collectable's produce/static ids.
        private float GetNearestCollectableInfo(Vector3 pos, out int produceId, out int staticId)
        {
            produceId = 0;
            staticId = 0;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < this.mapResEntities.Count; i++)
            {
                float dx = this.mapResEntities[i].Position.x - pos.x;
                float dz = this.mapResEntities[i].Position.z - pos.z;
                float sqr = dx * dx + dz * dz;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    produceId = this.mapResEntities[i].ProduceId;
                    staticId = this.mapResEntities[i].StaticId;
                }
            }
            return bestSqr;
        }

        // produceId (CollectableObjectComponent.itemTypeID) -> TableMapResourceProduce.hitProduce[0][0]
        // = the drop item id (e.g. 40021 = stone). GetIconName(itemId) then yields the real material icon.
        private void EnsureProduceMethod()
        {
            if (this.mapResProduceTried)
            {
                return;
            }
            if (auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null
                || auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null)
            {
                return;
            }
            IntPtr cls = this.FindAuraMonoClassByFullName("TableData");
            if (cls == IntPtr.Zero)
            {
                cls = this.FindAuraMonoClassInImages(string.Empty, "TableData", new[] { "EcsClient", "EcsClient.dll" });
            }
            if (cls == IntPtr.Zero)
            {
                return; // retry next scan
            }
            this.mapResProduceTried = true;
            this.mapResGetEntityMethod = this.FindAuraMonoMethodOnHierarchy(cls, "GetEntity", 2); // TableData.GetEntity(int,bool)
            // Signature is GetMapResourceProduce(int id, bool needException = false) = 2 params.
            this.mapResGetProduceMethod = this.FindAuraMonoMethodOnHierarchy(cls, "GetMapResourceProduce", 2);
            if (this.mapResGetProduceMethod == IntPtr.Zero)
            {
                this.mapResGetProduceMethod = this.FindAuraMonoMethodOnHierarchy(cls, "GetMapResourceProduce", 1);
            }
            ModLogger.Msg("[MapSpots] TableData.GetMapResourceProduce resolved=" + (this.mapResGetProduceMethod != IntPtr.Zero));

            // RewardUtility.GetDropGroup(string) resolves a dropGroup key -> the drop item id.
            IntPtr rewardCls = this.FindAuraMonoClassByFullName("XDTGameSystem.Utilities.RewardUtility");
            if (rewardCls == IntPtr.Zero)
            {
                rewardCls = this.FindAuraMonoClassInImages("XDTGameSystem.Utilities", "RewardUtility",
                    new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
            }
            if (rewardCls != IntPtr.Zero)
            {
                this.mapResDropGroupMethod = this.FindAuraMonoMethodOnHierarchy(rewardCls, "GetDropGroup", 1);
                this.mapResGetQualityMethod = this.FindAuraMonoMethodOnHierarchy(rewardCls, "GetQuality", 3);
            }
            // Safe dropGroup table: static property getter on TableData (returns the by-dropGroup dict).
            this.mapResDropDictGetter = this.FindAuraMonoMethodOnHierarchy(cls,
                "get_TableRandomDropsAndLowerUpperLimitsByDropGroup", 0);
            ModLogger.Msg("[MapSpots] RewardUtility.GetDropGroup resolved=" + (this.mapResDropGroupMethod != IntPtr.Zero)
                + " GetQuality=" + (this.mapResGetQualityMethod != IntPtr.Zero)
                + " dropDictGetter=" + (this.mapResDropDictGetter != IntPtr.Zero));
        }

        private unsafe bool TryGetProduceItemId(int produceId, out int itemId)
        {
            itemId = 0;
            if (produceId <= 0)
            {
                return false;
            }
            if (this.mapResProduceItemIdCache.TryGetValue(produceId, out itemId))
            {
                return itemId > 0;
            }
            this.mapResProduceItemIdCache[produceId] = 0; // cache miss until resolved (avoid re-invoking)

            if (this.mapResGetProduceMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }
            int pid = produceId;
            byte needException = 0;
            // Pass 2 args (int id, bool needException=false). Harmless if the resolved overload is 1-arg
            // (mono_runtime_invoke reads only as many args as the signature declares).
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&pid);
            args[1] = (IntPtr)(&needException);
            IntPtr exc = IntPtr.Zero;
            IntPtr produceObj = auraMonoRuntimeInvoke(this.mapResGetProduceMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || produceObj == IntPtr.Zero)
            {
                return false;
            }
            if (!this.TryGetMonoObjectMember(produceObj, "hitProduce", out IntPtr arr2d) || arr2d == IntPtr.Zero)
            {
                return false;
            }
            int outerLen = (int)auraMonoArrayLength(arr2d);
            if (outerLen <= 0)
            {
                return false;
            }

            // hitProduce is string[][] of dropGroup KEYS across all hit stages (a rare tree lists Timber,
            // Quality Timber AND Rare Timber in different slots, plus unrelated bonus boxes). Resolve EVERY
            // key. Rarity (GetQuality) can't rank material tiers (all return 1), but the tiers are
            // consecutive item ids in the SAME id-family (Timber 40002 / Quality 40003 / Rare 40004), while
            // bonus boxes live in a different family (70001/70002). So: take hitProduce[0][0]'s item as the
            // primary, then prefer the HIGHEST id within the primary's id-family (10000-block) = the headline
            // (Rare) tier, ignoring out-of-family bonus drops.
            const int IdFamilyBlock = 10000;
            bool diag = this.mapResProduceDiagIds.Count < 16 && this.mapResProduceDiagIds.Add(produceId);
            int primaryId = 0, bestId = 0, groupCount = 0;
            string firstRaw = null;
            for (int oi = 0; oi < outerLen; oi++)
            {
                IntPtr rowSlot = auraMonoArrayAddrWithSize(arr2d, IntPtr.Size, (UIntPtr)oi);
                IntPtr rowArr = rowSlot == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(rowSlot);
                if (rowArr == IntPtr.Zero) continue;
                int innerLen = (int)auraMonoArrayLength(rowArr);
                for (int ji = 0; ji < innerLen; ji++)
                {
                    IntPtr sSlot = auraMonoArrayAddrWithSize(rowArr, IntPtr.Size, (UIntPtr)ji);
                    IntPtr sObj = sSlot == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(sSlot);
                    if (sObj == IntPtr.Zero) continue;
                    if (firstRaw == null) this.TryReadMonoString(sObj, out firstRaw);
                    groupCount++;
                    bool ok = this.TryResolveDropGroupBest(sObj, out int gid, out int gq);
                    if (ok && gid > 0)
                    {
                        if (primaryId == 0) primaryId = gid; // hitProduce[0][0] = the resource's main drop
                        if (gid / IdFamilyBlock == primaryId / IdFamilyBlock && gid > bestId)
                        {
                            bestId = gid; // highest tier within the primary's material family
                        }
                    }
                    if (diag && this.mapResGroupVerboseCount < 40)
                    {
                        this.mapResGroupVerboseCount++;
                        this.TryReadMonoString(sObj, out string key);
                        ModLogger.Msg("[MapSpots]   group[" + oi + "][" + ji + "]='" + key + "' -> ok=" + ok
                            + " id=" + gid + " q=" + gq);
                    }
                }
            }

            if (diag)
            {
                ModLogger.Msg("[MapSpots] produce " + produceId + " hitProduce[0][0]='" + (firstRaw ?? "")
                    + "' groups=" + groupCount + " primaryId=" + primaryId + " bestItemId=" + bestId);
            }

            if (bestId > 0)
            {
                itemId = bestId;
                this.mapResProduceItemIdCache[produceId] = bestId;
                return true;
            }

            // Fallback: a few produces list a literal item id ("40021" or "40021,5" / "40021:5"); take
            // the leading integer of the first key.
            string raw = firstRaw ?? string.Empty;
            int end = 0;
            while (end < raw.Length && (char.IsDigit(raw[end]) || (end == 0 && raw[end] == '-'))) end++;
            if (end > 0 && int.TryParse(raw.Substring(0, end), out int parsed) && parsed > 0)
            {
                itemId = parsed;
                this.mapResProduceItemIdCache[produceId] = parsed;
                return true;
            }
            return false;
        }

        // dropGroup key (MonoString) -> best (highest-rarity) drop item id, read directly from
        // TableData.TableRandomDropsAndLowerUpperLimitsByDropGroup (Dictionary<string,(int,int,
        // List<TableRandomDrop>,List<int>)>). We do NOT use RewardUtility.GetDropGroup: it does an
        // unconditional content[0] and throws IndexOutOfRange on drop rows with empty content (e.g.
        // "TREE2032"), so it silently drops the rare-timber group. Here we guard content length and read
        // each row's content[0] = TableRewardItem (rewardType/rewardParam), ranking Item drops by rarity
        // (RewardUtility.GetQuality). All reads are array/field reads of live tables (no GC-moved pointers
        // held across yields; this runs synchronously inside the sync pass).
        private unsafe bool TryResolveDropGroupBest(IntPtr dropGroupStr, out int itemId, out int itemQuality)
        {
            itemId = 0;
            itemQuality = int.MinValue;
            if (dropGroupStr == IntPtr.Zero || this.mapResDropDictGetter == IntPtr.Zero
                || auraMonoRuntimeInvoke == null || auraMonoArrayAddrWithSize == null || auraMonoObjectUnbox == null)
            {
                return false;
            }

            // Fetch the by-dropGroup dictionary (level-cached; fetch fresh each time, cheap).
            IntPtr exc = IntPtr.Zero;
            IntPtr dictObj = auraMonoRuntimeInvoke(this.mapResDropDictGetter, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || dictObj == IntPtr.Zero)
            {
                return false;
            }
            // Resolve Dictionary.get_Item(string) lazily from the live dict's class.
            if (this.mapResDropDictGetItem == IntPtr.Zero && auraMonoObjectGetClass != null)
            {
                IntPtr dictCls = auraMonoObjectGetClass(dictObj);
                if (dictCls != IntPtr.Zero)
                {
                    this.mapResDropDictGetItem = this.FindAuraMonoMethodOnHierarchy(dictCls, "get_Item", 1);
                }
            }
            if (this.mapResDropDictGetItem == IntPtr.Zero)
            {
                return false;
            }

            // get_Item(key) -> boxed ValueTuple<int,int,List<TableRandomDrop>,List<int>> (throws KeyNotFound
            // if absent -> exc set -> treated as miss).
            exc = IntPtr.Zero;
            IntPtr* gargs = stackalloc IntPtr[1];
            gargs[0] = dropGroupStr;
            IntPtr boxedTuple = auraMonoRuntimeInvoke(this.mapResDropDictGetItem, dictObj, (IntPtr)gargs, ref exc);
            if (exc != IntPtr.Zero || boxedTuple == IntPtr.Zero)
            {
                return false;
            }
            IntPtr tupleData = auraMonoObjectUnbox(boxedTuple);
            if (tupleData == IntPtr.Zero)
            {
                return false;
            }
            // ValueTuple layout: Item1 int@0, Item2 int@4, Item3 (List<TableRandomDrop>) ref@8.
            IntPtr listObj = Marshal.ReadIntPtr(tupleData, 8);
            if (listObj == IntPtr.Zero)
            {
                return false;
            }
            if (!this.TryGetMonoIntMember(listObj, "_size", out int size) || size <= 0)
            {
                return false;
            }
            if (!this.TryGetMonoObjectMember(listObj, "_items", out IntPtr dropsArr) || dropsArr == IntPtr.Zero)
            {
                return false;
            }

            const int RewardTypeItem = 2;
            int bestId = 0, bestQuality = int.MinValue;
            for (int k = 0; k < size; k++)
            {
                IntPtr trSlot = auraMonoArrayAddrWithSize(dropsArr, IntPtr.Size, (UIntPtr)k);
                IntPtr tr = trSlot == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(trSlot);
                if (tr == IntPtr.Zero) continue;
                // TableRandomDrop.content = TableRewardItem[]; guard the length the game's helper assumes.
                if (!this.TryGetMonoObjectMember(tr, "content", out IntPtr contentArr) || contentArr == IntPtr.Zero) continue;
                if ((int)auraMonoArrayLength(contentArr) <= 0) continue;
                IntPtr ri0Slot = auraMonoArrayAddrWithSize(contentArr, IntPtr.Size, UIntPtr.Zero);
                IntPtr ri0 = ri0Slot == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(ri0Slot);
                if (ri0 == IntPtr.Zero) continue;
                if (!this.TryGetMonoInt32Member(ri0, "rewardType", out int rType) || rType != RewardTypeItem) continue;
                if (!this.TryGetMonoInt32Member(ri0, "rewardParam", out int rId) || rId <= 0) continue;
                int q = this.TryGetItemQuality(rId, out int qq) ? qq : 0;
                if (q > bestQuality)
                {
                    bestQuality = q;
                    bestId = rId;
                }
            }

            if (bestId > 0)
            {
                itemId = bestId;
                itemQuality = bestQuality == int.MinValue ? 0 : bestQuality;
                return true;
            }
            return false;
        }

        // RewardUtility.GetQuality(RewardType.Item, itemId, 0) -> item rarity (safe; reads TableEntity.rarity).
        private unsafe bool TryGetItemQuality(int itemId, out int quality)
        {
            quality = 0;
            if (this.mapResGetQualityMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return false;
            }
            int type = 2; // RewardType.Item
            int param = itemId;
            int value = 0;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&type);
            args[1] = (IntPtr)(&param);
            args[2] = (IntPtr)(&value);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.mapResGetQualityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }
            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }
            quality = *(int*)raw;
            return true;
        }

        // Match a radar label (= item name, e.g. "Shiitake") to a collectable-atlas id, using the live atlas
        // name->id map. Exact (case-insensitive) first, then a conservative prefix match ("Oyster" ->
        // "Oyster Mushroom"); the lowest-id non-"Bizarre" variant is preferred at build time.
        private bool TryResolveCollectableIdByLabel(string label, out int collId)
        {
            collId = 0;
            if (string.IsNullOrEmpty(label) || this.mapAtlasNameToId.Count == 0)
            {
                return false;
            }
            string key = label.Trim().ToLowerInvariant();
            if (this.mapAtlasNameToId.TryGetValue(key, out collId))
            {
                return true;
            }
            int bestLen = int.MaxValue;
            foreach (KeyValuePair<string, int> kv in this.mapAtlasNameToId)
            {
                bool related = kv.Key.StartsWith(key + " ", StringComparison.Ordinal)
                    || key.StartsWith(kv.Key + " ", StringComparison.Ordinal);
                if (related && kv.Key.Length < bestLen)
                {
                    bestLen = kv.Key.Length;
                    collId = kv.Value;
                }
            }
            return collId > 0;
        }

        // TableData.GetEntity(id).name -> item display name (diagnostic, to identify collectable ids).
        private unsafe bool TryGetItemName(int id, out string name)
        {
            name = null;
            if (this.mapResGetEntityMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }
            int pid = id;
            byte needException = 0;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&pid);
            args[1] = (IntPtr)(&needException);
            IntPtr exc = IntPtr.Zero;
            IntPtr entityObj = auraMonoRuntimeInvoke(this.mapResGetEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || entityObj == IntPtr.Zero)
            {
                return false;
            }
            if (!this.TryGetMonoObjectMember(entityObj, "name", out IntPtr strObj) || strObj == IntPtr.Zero)
            {
                return false;
            }
            return this.TryReadMonoString(strObj, out name);
        }

        // Resolve MapSpotProtocolManager.AddSpot / RemoveSpot (static, value-type args) for big-map spots.
        private void EnsureMapSpotMethods()
        {
            if (this.mapSpotMethodsTried)
            {
                return;
            }
            if (auraMonoRuntimeInvoke == null)
            {
                return;
            }
            IntPtr cls = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.MapSpot.MapSpotProtocolManager");
            if (cls == IntPtr.Zero)
            {
                cls = this.FindAuraMonoClassInImages("XDTDataAndProtocol.ProtocolService.MapSpot",
                    "MapSpotProtocolManager", new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
            }
            if (cls == IntPtr.Zero)
            {
                return; // retry next scan (image may not be loaded yet)
            }
            this.mapSpotMethodsTried = true;
            this.mapSpotAddMethod = this.FindAuraMonoMethodOnHierarchy(cls, "AddSpot", 5);
            this.mapSpotRemoveMethod = this.FindAuraMonoMethodOnHierarchy(cls, "RemoveSpot", 4);
            ModLogger.Msg("[MapSpots] MapSpotProtocolManager AddSpot=" + (this.mapSpotAddMethod != IntPtr.Zero)
                + " RemoveSpot=" + (this.mapSpotRemoveMethod != IntPtr.Zero));
        }

        // AddSpot(SpotEnum category, int useId, Vector3 position, SpotReason reason, GameSceneId gameSceneId).
        private unsafe bool AddBigMapSpot(int useId, Vector3 pos)
        {
            if (this.mapSpotAddMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }
            int category = SpotEnumCollectable, reason = SpotReasonAuto, scene = GameSceneIdStarTown, id = useId;
            Vector3 p = pos;
            IntPtr* args = stackalloc IntPtr[5];
            args[0] = (IntPtr)(&category);
            args[1] = (IntPtr)(&id);
            args[2] = (IntPtr)(&p);       // Vector3 by value -> pointer to the struct
            args[3] = (IntPtr)(&reason);
            args[4] = (IntPtr)(&scene);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.mapSpotAddMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        // RemoveSpot(SpotEnum category, int useId, SpotReason reason, GameSceneId gameSceneId).
        private unsafe bool RemoveBigMapSpot(int useId)
        {
            if (this.mapSpotRemoveMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }
            int category = SpotEnumCollectable, reason = SpotReasonAuto, scene = GameSceneIdStarTown, id = useId;
            IntPtr* args = stackalloc IntPtr[4];
            args[0] = (IntPtr)(&category);
            args[1] = (IntPtr)(&id);
            args[2] = (IntPtr)(&reason);
            args[3] = (IntPtr)(&scene);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.mapSpotRemoveMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        // Reconcile injected big-map Collectable spots against the desired set (keyed by drop-item id).
        private void SyncBigMapSpots()
        {
            this.EnsureMapSpotMethods();
            if (this.mapSpotAddMethod == IntPtr.Zero)
            {
                return;
            }

            this.mapBigSpotRemoveBuffer.Clear();
            foreach (KeyValuePair<int, Vector3> kv in this.mapBigSpotInjected)
            {
                if (!this.mapBigSpotDesired.ContainsKey(kv.Key))
                {
                    this.mapBigSpotRemoveBuffer.Add(kv.Key);
                }
            }
            int added = 0, removed = 0;
            for (int i = 0; i < this.mapBigSpotRemoveBuffer.Count; i++)
            {
                int id = this.mapBigSpotRemoveBuffer[i];
                this.RemoveBigMapSpot(id);
                this.mapBigSpotInjected.Remove(id);
                removed++;
            }
            foreach (KeyValuePair<int, Vector3> kv in this.mapBigSpotDesired)
            {
                bool isNew = !this.mapBigSpotInjected.TryGetValue(kv.Key, out Vector3 prev);
                if (!isNew && (kv.Value - prev).sqrMagnitude <= MapTrackMoveThresholdSqr)
                {
                    continue;
                }
                if (this.AddBigMapSpot(kv.Key, kv.Value))
                {
                    this.mapBigSpotInjected[kv.Key] = kv.Value;
                    if (isNew) added++;
                }
            }
            if (this.mapTrackDiagSyncs < 6 && (added > 0 || removed > 0))
            {
                ModLogger.Msg("[MapSpots] big-map spots: desired=" + this.mapBigSpotDesired.Count
                    + " injected=" + this.mapBigSpotInjected.Count + " added=" + added + " removed=" + removed);
            }

            this.TryDumpCollectableAtlasOnce();
        }

        // Enumerate the packed collectable SpriteAtlas to build the item-name -> collectable-id map (and log
        // it once). The atlas only loads when a collectable icon is actually rendered (big map open / a
        // MapResource marker shown), which can be well after startup, so retry (throttled) until it appears.
        private void TryDumpCollectableAtlasOnce()
        {
            if (this.mapAtlasDumped)
            {
                return;
            }
            float now = Time.unscaledTime;
            if (now < this.mapAtlasNextTryAt)
            {
                return;
            }
            this.mapAtlasNextTryAt = now + 2f;
            bool firstTry = this.mapAtlasDumpTries == 0;
            this.mapAtlasDumpTries++;
            try
            {
                Il2CppArrayBase<SpriteAtlas> atlases = Resources.FindObjectsOfTypeAll<SpriteAtlas>();
                if (atlases == null)
                {
                    return;
                }
                SpriteAtlas atlas = null;
                for (int i = 0; i < atlases.Length; i++)
                {
                    SpriteAtlas a = atlases[i];
                    if (a == null)
                    {
                        continue;
                    }
                    string an = a.name ?? string.Empty;
                    if (firstTry)
                    {
                        ModLogger.Msg("[AtlasDump] SpriteAtlas '" + an + "' spriteCount=" + a.spriteCount);
                    }
                    if (an.IndexOf("collectable", StringComparison.OrdinalIgnoreCase) >= 0 && a.spriteCount > 0)
                    {
                        atlas = a;
                        break;
                    }
                }
                if (atlas == null)
                {
                    if (firstTry)
                    {
                        ModLogger.Msg("[AtlasDump] collectable atlas not loaded yet; retrying (open the big map / show resource markers)...");
                    }
                    return;
                }
                {
                    string aname = atlas.name ?? string.Empty;
                    int count = atlas.spriteCount;
                    Il2CppReferenceArray<Sprite> sprites = new Il2CppReferenceArray<Sprite>(count);
                    int got = atlas.GetSprites(sprites);
                    var sb = new StringBuilder();
                    int logged = 0;
                    for (int s = 0; s < got; s++)
                    {
                        Sprite sp = sprites[s];
                        if (sp == null) continue;
                        string sn = sp.name ?? string.Empty;
                        if (sn.EndsWith("(Clone)", StringComparison.Ordinal))
                        {
                            sn = sn.Substring(0, sn.Length - "(Clone)".Length);
                        }
                        // Resolve the numeric id -> item name so we can identify which id is the mushroom.
                        const string pfx = "ui_dynamic_collectable_";
                        string label = sn;
                        if (sn.StartsWith(pfx, StringComparison.Ordinal)
                            && int.TryParse(sn.Substring(pfx.Length), out int cid))
                        {
                            this.mapAtlasIdSet.Add(cid); // ids with a real sprite (validates the produce path)
                            if (this.TryGetItemName(cid, out string nm) && !string.IsNullOrEmpty(nm))
                            {
                                label = cid + "=" + nm;
                                // Build name -> collectable id (prefer the lowest id so non-"Bizarre" variants win).
                                string key = nm.Trim().ToLowerInvariant();
                                if (!this.mapAtlasNameToId.TryGetValue(key, out int existing) || cid < existing)
                                {
                                    this.mapAtlasNameToId[key] = cid;
                                }
                            }
                        }
                        sb.Append(label).Append(" | ");
                        logged++;
                        if (sb.Length > 900)
                        {
                            ModLogger.Msg("[AtlasDump]   " + sb.ToString());
                            sb.Clear();
                        }
                        UnityEngine.Object.Destroy(sp); // GetSprites returns clones
                    }
                    if (sb.Length > 0)
                    {
                        ModLogger.Msg("[AtlasDump]   " + sb.ToString());
                    }
                    ModLogger.Msg("[AtlasDump] '" + aname + "' dumped " + logged + " sprite names; nameMap="
                        + this.mapAtlasNameToId.Count);
                    this.mapAtlasDumped = true;
                }
            }
            catch (Exception ex)
            {
                this.mapAtlasDumped = true; // don't spam on failure
                ModLogger.Msg("[AtlasDump] failed: " + ex.Message);
            }
        }

        private void ClearInjectedBigMapSpots()
        {
            if (this.mapBigSpotInjected.Count == 0 || this.mapSpotRemoveMethod == IntPtr.Zero)
            {
                this.mapBigSpotInjected.Clear();
                return;
            }
            foreach (KeyValuePair<int, Vector3> kv in this.mapBigSpotInjected)
            {
                this.RemoveBigMapSpot(kv.Key);
            }
            this.mapBigSpotInjected.Clear();
        }

        // ---- "Player avatars for everyone" (radarPlayerAvatarsAll) ----
        // The game shows the real avatar photo on a map player marker only when the target is a FRIEND:
        // MiniMapSpotWidget.SetData -> spot.IsFriend -> HeadIconWidget(GetUserProfile(usageId).AvatarImageUrl)
        // (same gate on the big map: MapSpotWidget -> MapSpot.IsFriend). The profile cache itself is warmed
        // for EVERY player with a server Player spot (MapSpotsSystem.CreateMapSpotData ->
        // FriendSystem.UpdateUserCacheInMap), so only the friendship CHECK blocks strangers. These two
        // callback-free NativeDetours replace the getters with "any (non-self) player counts": Apply while
        // the toggle is on, Undo when off (same pattern as the Building hooks — no trampoline, no managed
        // callbacks, allocation-free bodies reading raw fields).
        private delegate byte IsFriendGetterDelegate(IntPtr thisPtr);
        private static IsFriendGetterDelegate miniMapIsFriendHook;   // anti-GC
        private static IsFriendGetterDelegate mapSpotIsFriendHook;   // anti-GC
        private static MonoMod.RuntimeDetour.NativeDetour miniMapIsFriendDetour;
        private static MonoMod.RuntimeDetour.NativeDetour mapSpotIsFriendDetour;
        // Suppress the big-map "tracked" square frame (MapSpotWidget.tracked_go = MapSpot.IsTracked) on the
        // player spots our injected Player track incidentally marks as tracked. Trampoline: keep the real
        // value except for our player netIds (returns them to normal, non-tracked player styling/LOD).
        private static IsFriendGetterDelegate mapSpotIsTrackedHook;        // anti-GC
        private static IsFriendGetterDelegate mapSpotIsTrackedTrampoline;  // original
        private static MonoMod.RuntimeDetour.NativeDetour mapSpotIsTrackedDetour;
        private bool avatarPatchTried;
        private float avatarPatchNextTryAt;
        // MiniMapSpot is a STRUCT: `this` = pointer to the raw struct data -> header-subtracted offsets.
        private static int miniMapOffTrackType = -1;
        private static int miniMapOffUsageId = -1;
        // MapSpot is a CLASS: `this` = object pointer -> header-inclusive offsets (mono_field_get_offset as-is).
        private static int mapSpotOffCategory = -1;
        private static int mapSpotOffUsageId = -1;
        private static uint mapAvatarSelfNetId; // exclude self (vanilla getters do too)
        private const int SpotEnumPlayer = 3;


        // "Player Avatars (all)" for the IN-WORLD pointer: the native MapTrackWidget shows the avatar only
        // when TryGetFriendByNetId(cell.NetID) is true (twice, inline, unhookable). A trampoline detour on
        // FriendClientService.TryGetFriendByNetId forces true for the specific player netIds we inject a
        // world track for (scoped set, refreshed each sync) while the toggle is on — out FriendComponent is
        // left as the original's default (all readers are null-safe: GetDisplayName / IsNullOrEmpty), so the
        // blast radius is only those nearby players and only cosmetic (a blank name in a social panel).
        private delegate byte TryGetFriendByNetIdDelegate(IntPtr self, uint netId, IntPtr outFriend);
        private static TryGetFriendByNetIdDelegate friendGateHook;        // anti-GC
        private static TryGetFriendByNetIdDelegate friendGateTrampoline;  // original
        private static MonoMod.RuntimeDetour.NativeDetour friendGateDetour;
        private static volatile bool avatarForceFriendActive;
        // The in-world player pointer (MapTrackWidget) only draws the avatar head icon when
        // FriendProtocolManager.TryGetFriendByNetId is true — but that check is used by ~30 call sites
        // (dialogs, panels), so force-friending it globally broke those. Instead we bracket MapTrackWidget.SetData
        // with this flag and let FriendGateNative force-friend ONLY while it is set, so the override is confined
        // to the pointer's own render call and never leaks to a dialog's friend check.
        // The pointer's avatar is force-friended only while its icon is being computed (mapTrackWidgetRendering),
        // scoping the FriendGateNative override to that window. The old MapTrackCellModel/MapTrackWidget.SetData
        // brackets were removed in the 2026-07-09 update (MapTrackCellModel deleted, which silently disabled the
        // whole patch); TrackingItem.GetAtlasSpriteId is their superset and is the sole anchor now.
        private static volatile bool mapTrackWidgetRendering;
        private bool mapTrackSetDataPatchTried;
        private bool mapTrackClassMissLogged;
        // The track icon (incl. the player avatar URL) is computed by TrackingItem.GetAtlasSpriteId, which is
        // also invoked from refresh/streaming paths that DON'T go through our SetData brackets — so at close
        // range the avatar URL is recomputed without force-friend and reverts to the non-friend placeholder.
        // Bracket GetAtlasSpriteId itself (sret struct return: RCX=this, RDX=sret, R8=useReason) so force-friend
        // covers EVERY icon computation for our player netIds. Scoped like the SetData brackets -> no dialog leak.
        private delegate IntPtr GetAtlasSpriteIdDelegate(IntPtr self, IntPtr sret, int useReason);
        private static GetAtlasSpriteIdDelegate getAtlasSpriteIdHook;        // anti-GC
        private static GetAtlasSpriteIdDelegate getAtlasSpriteIdTrampoline;  // original
        private static MonoMod.RuntimeDetour.NativeDetour getAtlasSpriteIdDetour;
        // SECOND friend-gate anchor (2026-07-11): MapTrackWidget.SetData(MapTrackHudItem) re-checks
        // FriendProtocolManager.TryGetFriendByNetId(item.targetNetId) ITSELF to pick the avatar branch
        // (headIcon_widget.SetIcon(iconId.SpriteName)) vs the placeholder icon. GetAtlasSpriteId already
        // put the avatar URL into iconId, but this widget's own friend check runs OUTSIDE that bracket ->
        // real "not a friend" -> placeholder (the white-circle bug). Commit 06dd9c0 dropped this bracket
        // assuming GetAtlasSpriteId was a superset; the live "flag OFF" diagnostic proved it wasn't.
        // Re-bracket SetData so force-friend covers its check too. void instance, 1 ref-type arg.
        private delegate void MapTrackSetDataDelegate(IntPtr self, IntPtr arg);
        private static MapTrackSetDataDelegate mapTrackSetDataHook;        // anti-GC
        private static MapTrackSetDataDelegate mapTrackSetDataTrampoline;  // original
        private static MonoMod.RuntimeDetour.NativeDetour mapTrackSetDataDetour;
        // netIds we currently inject a world Player track for. Read by the static hook -> a plain field the
        // sync writes wholesale each pass (single-threaded UI thread; the detour also fires on it).
        private static HashSet<uint> mapAvatarWorldNetIds = new HashSet<uint>();
        private readonly HashSet<uint> mapAvatarWorldNetIdsNext = new HashSet<uint>();

        private void ManagePlayerAvatarPatches()
        {
            float now = Time.unscaledTime;
            if (now < this.avatarPatchNextTryAt)
            {
                return;
            }
            this.avatarPatchNextTryAt = now + 2f;
            avatarForceFriendActive = this.radarPlayerAvatarsAll;
            if (this.radarPlayerAvatarsAll)
            {
                // Prefer the AuraMono PlayerDataCenter path (the managed TryGetSelfPlayerNetId is dead on this
                // build -> returned 0); fall back to it only if the Mono path isn't up yet.
                if (this.TryResolveSelfPlayerNetIdMono(out uint selfId) && selfId != 0u)
                {
                    mapAvatarSelfNetId = selfId;
                }
                else if (this.TryGetSelfPlayerNetId(out uint selfIdManaged) && selfIdManaged != 0u)
                {
                    mapAvatarSelfNetId = selfIdManaged;
                }
                this.EnsurePlayerAvatarPatches();
                // In-world player pointer avatars: the force-friend detour on TryGetFriendByNetId is now SCOPED
                // to MapTrackWidget.SetData via the mapTrackWidgetRendering flag (EnsureTrackWidgetPatch), so it
                // no longer leaks into dialogs/panels (the earlier global force-friend regression). Map avatars
                // still come from the scoped get_IsFriend detours; names from GetPlayerName/GetUserProfile.
                this.EnsureTrackWidgetPatch();
                this.EnsureFriendGatePatch();
            }
            else
            {
                this.UndoPlayerAvatarPatches();
                this.UndoTrackWidgetPatch();
                this.UndoFriendGatePatch();
            }

            // Real names for non-friends (map spot / dialog title): scan + get_name detour. Managed by the
            // same avatar toggle. No force-friend -> no dialog regression.
            mapNameActive = this.radarPlayerAvatarsAll;
            if (this.radarPlayerAvatarsAll)
            {
                this.RefreshRemotePlayerScan(); // also (re)builds the netId -> real-name cache
                this.EnsureGetNamePatch();          // map spot / chat surfaces (GetPlayerName)
                this.EnsureGetProfilePatch();       // over-head nameplate + profile card (GetUserProfile.Title)
                this.EnsureIsAcquaintancePatch();   // let the nameplate show strangers without opening the card
            }
            else
            {
                this.UndoGetNamePatch();
                this.UndoGetProfilePatch();
                this.UndoIsAcquaintancePatch();
            }

            // The tracked-square suppression (MapSpot.get_IsTracked) is needed by BOTH big-map resource spots
            // and world-player spots, so it's managed independently of the avatar toggle.
            if (this.radarBigMapSpots || this.radarPlayerAvatarsAll)
            {
                this.EnsureIsTrackedPatch();
            }
            else
            {
                this.UndoIsTrackedPatch();
            }
        }

        // Detour PlayerServiceSystem.GetPlayerName(shortId, title): return the cached real name for players we
        // scanned (map spot, profile card, over-head nameplate, chat all route through this), else original.
        private void EnsureGetNamePatch()
        {
            try
            {
                if (getPlayerNameDetour != null)
                {
                    if (!getPlayerNameDetour.IsApplied)
                    {
                        getPlayerNameDetour.Apply();
                    }
                    return;
                }
                if (this.getNamePatchTried)
                {
                    return;
                }
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return;
                }
                if (this.mapNamePlayerServiceClass == IntPtr.Zero)
                {
                    this.mapNamePlayerServiceClass = this.FindAuraMonoClassByFullName("XDTGameSystem.PlayerService.PlayerServiceSystem");
                }
                if (this.mapNamePlayerServiceClass == IntPtr.Zero)
                {
                    return; // class not loaded yet — retry later
                }
                IntPtr nameNative = this.ResolveBuildingMonoNative(this.mapNamePlayerServiceClass, "GetPlayerName", 2);
                if (nameNative == IntPtr.Zero)
                {
                    this.getNamePatchTried = true;
                    ModLogger.Msg("[MapSpots] name patch: PlayerServiceSystem.GetPlayerName(2) not resolved");
                    return;
                }
                getPlayerNameHook = GetPlayerNameNative;
                getPlayerNameDetour = new MonoMod.RuntimeDetour.NativeDetour(nameNative, getPlayerNameHook);
                getPlayerNameTrampoline = getPlayerNameDetour.GenerateTrampoline<GetPlayerNameDelegate>();
                this.getNamePatchTried = true;
                ModLogger.Msg("[MapSpots] name patch: real-name detour installed on PlayerServiceSystem.GetPlayerName");
            }
            catch (Exception ex)
            {
                this.getNamePatchTried = true;
                ModLogger.Msg("[MapSpots] name patch: GetPlayerName detour failed: " + ex.Message);
            }
        }

        private void UndoGetNamePatch()
        {
            try
            {
                if (getPlayerNameDetour != null && getPlayerNameDetour.IsApplied)
                {
                    getPlayerNameDetour.Undo();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[MapSpots] name patch: GetPlayerName undo failed: " + ex.Message);
            }
        }

        private void FreePlayerNamePins()
        {
            FreeAuraMonoPins(this.mapNamePins);
            this.mapNamePins.Clear();
            FreeAuraMonoPins(this.mapNamePinsNext);
            this.mapNamePinsNext.Clear();
            this.mapNameByShortIdNext.Clear();
            mapNameByShortId = new Dictionary<long, IntPtr>();
        }

        // Detour BOTH 1-arg FriendSystem.GetUserProfile overloads (long shortId / uint netId): the over-head
        // nameplate and profile card read the returned copy's Title.TitleString, so mirror the profile's real
        // Name into Title._titleString within that copy. sret struct-return ABI: RCX=sret buffer, RDX=this,
        // R8=arg; the original returns the sret pointer in RAX, so the hook returns the trampoline's result to
        // keep RAX intact. Requires Name + Title._titleString offsets (resolved by EnsureNameReadReady).
        private void EnsureGetProfilePatch()
        {
            try
            {
                bool anyInstalled = false;
                for (int i = 0; i < getProfileDetours.Length; i++)
                {
                    if (getProfileDetours[i] != null)
                    {
                        anyInstalled = true;
                        if (!getProfileDetours[i].IsApplied)
                        {
                            getProfileDetours[i].Apply();
                        }
                    }
                }
                if (anyInstalled || this.getProfilePatchTried)
                {
                    return;
                }
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return;
                }
                if (this.mapNameGetProfileMethods.Count == 0 || this.mapNameProfileNameOffset < 0
                    || this.mapNameTitleStringOffset < 0)
                {
                    return; // methods/offsets not resolved yet — retry on a later scan
                }
                if (buildingMonoCompileMethod == null)
                {
                    IntPtr mod = this.GetAuraMonoModuleHandle();
                    if (mod != IntPtr.Zero)
                    {
                        buildingMonoCompileMethod = this.GetAuraMonoExport<BuildingMonoCompileMethodDelegate>(mod, "mono_compile_method");
                    }
                }
                if (buildingMonoCompileMethod == null)
                {
                    return; // retry
                }
                gpNameOffset = this.mapNameProfileNameOffset;
                gpTitleStringOffset = this.mapNameTitleStringOffset;
                getProfileHooks[0] = GetProfileNative0;
                getProfileHooks[1] = GetProfileNative1;
                int installed = 0;
                int count = this.mapNameGetProfileMethods.Count < 2 ? this.mapNameGetProfileMethods.Count : 2;
                for (int i = 0; i < count; i++)
                {
                    IntPtr native = buildingMonoCompileMethod(this.mapNameGetProfileMethods[i]);
                    if (native == IntPtr.Zero)
                    {
                        continue;
                    }
                    // Construct (auto-applies) then generate the trampoline. If trampoline generation throws,
                    // the detour is live with a null trampoline -> every GetUserProfile would return an
                    // unfilled struct. Undo immediately on failure so the hook is never left half-installed.
                    MonoMod.RuntimeDetour.NativeDetour d = new MonoMod.RuntimeDetour.NativeDetour(native, getProfileHooks[i]);
                    try
                    {
                        getProfileTrampolines[i] = d.GenerateTrampoline<GetProfileDelegate>();
                    }
                    catch
                    {
                        try { d.Undo(); } catch { }
                        getProfileTrampolines[i] = null;
                        throw;
                    }
                    getProfileDetours[i] = d;
                    installed++;
                }
                this.getProfilePatchTried = true;
                ModLogger.Msg("[MapSpots] name patch: GetUserProfile Title-mirror detour installed on "
                    + installed + " overload(s), Name@" + gpNameOffset + " Title.str@" + gpTitleStringOffset);
            }
            catch (Exception ex)
            {
                this.getProfilePatchTried = true;
                ModLogger.Msg("[MapSpots] name patch: GetUserProfile detour failed: " + ex.Message);
            }
        }

        private void UndoGetProfilePatch()
        {
            try
            {
                for (int i = 0; i < getProfileDetours.Length; i++)
                {
                    if (getProfileDetours[i] != null && getProfileDetours[i].IsApplied)
                    {
                        getProfileDetours[i].Undo();
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[MapSpots] name patch: GetUserProfile undo failed: " + ex.Message);
            }
        }

        // Detour FriendSystem.IsAcquaintance(long shortId) -> true for players in the name cache, so the
        // over-head nameplate shows their (mirrored) name without needing the profile card opened first.
        private void EnsureIsAcquaintancePatch()
        {
            try
            {
                if (isAcquaintanceDetour != null)
                {
                    if (!isAcquaintanceDetour.IsApplied)
                    {
                        isAcquaintanceDetour.Apply();
                    }
                    return;
                }
                if (this.acqPatchTried)
                {
                    return;
                }
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return;
                }
                if (this.mapNameFriendSystemClass == IntPtr.Zero)
                {
                    this.mapNameFriendSystemClass = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.Social.FriendSystem");
                }
                if (this.mapNameFriendSystemClass == IntPtr.Zero)
                {
                    return; // class not loaded yet — retry later
                }
                IntPtr acqNative = this.ResolveBuildingMonoNative(this.mapNameFriendSystemClass, "IsAcquaintance", 1);
                if (acqNative == IntPtr.Zero)
                {
                    this.acqPatchTried = true;
                    ModLogger.Msg("[MapSpots] name patch: FriendSystem.IsAcquaintance(1) not resolved");
                    return;
                }
                isAcquaintanceHook = IsAcquaintanceNative;
                MonoMod.RuntimeDetour.NativeDetour d = new MonoMod.RuntimeDetour.NativeDetour(acqNative, isAcquaintanceHook);
                try
                {
                    isAcquaintanceTrampoline = d.GenerateTrampoline<IsAcquaintanceDelegate>();
                }
                catch
                {
                    try { d.Undo(); } catch { }
                    isAcquaintanceTrampoline = null;
                    throw;
                }
                isAcquaintanceDetour = d;
                this.acqPatchTried = true;
                ModLogger.Msg("[MapSpots] name patch: IsAcquaintance detour installed (nameplate shows scanned strangers)");
            }
            catch (Exception ex)
            {
                this.acqPatchTried = true;
                ModLogger.Msg("[MapSpots] name patch: IsAcquaintance detour failed: " + ex.Message);
            }
        }

        private void UndoIsAcquaintancePatch()
        {
            try
            {
                if (isAcquaintanceDetour != null && isAcquaintanceDetour.IsApplied)
                {
                    isAcquaintanceDetour.Undo();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[MapSpots] name patch: IsAcquaintance undo failed: " + ex.Message);
            }
        }

        // Return true (=1) for players we have a cached name for so the nameplate treats them as acquaintances
        // and shows the name; everything else falls through to the original. Allocation-free (hot path).
        private static byte IsAcquaintanceNative(IntPtr self, long shortId)
        {
            if (mapNameActive && shortId != 0L && mapNameByShortId.ContainsKey(shortId))
            {
                return 1;
            }
            return isAcquaintanceTrampoline != null ? isAcquaintanceTrampoline(self, shortId) : (byte)0;
        }

        // sret struct-return hook bodies (one per overload so each calls its own trampoline). Allocation-free:
        // only a pass-through call + two Marshal memory ops. Returns the trampoline's result (the sret pointer)
        // to preserve RAX for the caller.
        // ABI CONFIRMED from a crash dump: mono passes an instance method's vtype return buffer AFTER `this`,
        // i.e. RCX=this, RDX=sret, R8=arg (a *static* sret method like CraftMath has sret first because there
        // is no `this`). So the SECOND pointer param is the return buffer to mirror into. The hook returns the
        // trampoline's result (RAX = sret pointer) to preserve the caller's return value.
        private static IntPtr GetProfileNative0(IntPtr self, IntPtr sret, long arg)
        {
            IntPtr ret = getProfileTrampolines[0] != null ? getProfileTrampolines[0](self, sret, arg) : sret;
            MirrorProfileNameIntoTitle(sret);
            return ret;
        }

        private static IntPtr GetProfileNative1(IntPtr self, IntPtr sret, long arg)
        {
            IntPtr ret = getProfileTrampolines[1] != null ? getProfileTrampolines[1](self, sret, arg) : sret;
            MirrorProfileNameIntoTitle(sret);
            return ret;
        }

        private static void MirrorProfileNameIntoTitle(IntPtr sret)
        {
            // Skip while our own throttled scan is inside GetUserProfile (it invokes the now-detoured method to
            // build the name cache) — the mirror is only for the game's over-head/card reads, not our reads.
            if (mapNameReadingSelf || !mapNameActive || sret == IntPtr.Zero || gpNameOffset < 0 || gpTitleStringOffset < 0)
            {
                return;
            }
            IntPtr namePtr = Marshal.ReadIntPtr(sret, gpNameOffset);
            if (namePtr == IntPtr.Zero)
            {
                return;
            }
            // MonoString.length @ 16 (2*IntPtr header). Never replace the title with a blank/partial name —
            // that would reproduce the empty-nameplate bug. Only mirror a non-empty Name.
            if (Marshal.ReadInt32(namePtr, 16) <= 0)
            {
                return;
            }
            Marshal.WriteIntPtr(sret, gpTitleStringOffset, namePtr);
        }

        // GetPlayerName(self, shortId, title) replacement: return the cached real name for a scanned player,
        // else the original (friend nickname / stranger title / unknown shortId unchanged). Allocation-free
        // (fires on the hot UI path — never log or allocate here).
        private static IntPtr GetPlayerNameNative(IntPtr self, long shortId, long title)
        {
            if (mapNameActive && shortId != 0L && mapNameByShortId.TryGetValue(shortId, out IntPtr name) && name != IntPtr.Zero)
            {
                return name;
            }
            return getPlayerNameTrampoline != null ? getPlayerNameTrampoline(self, shortId, title) : IntPtr.Zero;
        }

        // Resolve MapSpot.category/usageId field offsets (header-inclusive; MapSpot is a class). Idempotent.
        private bool TryEnsureMapSpotOffsets(out IntPtr spotCls)
        {
            spotCls = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.MapSpots.MapSpot");
            if (spotCls == IntPtr.Zero)
            {
                return false;
            }
            if (mapSpotOffCategory >= 0 && mapSpotOffUsageId >= 0)
            {
                return true;
            }
            if (auraMonoClassGetFieldFromName == null || auraMonoFieldGetOffset == null)
            {
                return false;
            }
            IntPtr catField = auraMonoClassGetFieldFromName(spotCls, "category");
            IntPtr useField = auraMonoClassGetFieldFromName(spotCls, "usageId");
            if (catField == IntPtr.Zero || useField == IntPtr.Zero)
            {
                return false;
            }
            mapSpotOffCategory = (int)auraMonoFieldGetOffset(catField);
            mapSpotOffUsageId = (int)auraMonoFieldGetOffset(useField);
            return true;
        }

        private bool isTrackedPatchTried;
        private void EnsureIsTrackedPatch()
        {
            try
            {
                if (mapSpotIsTrackedDetour != null)
                {
                    if (!mapSpotIsTrackedDetour.IsApplied)
                    {
                        mapSpotIsTrackedDetour.Apply();
                    }
                    return;
                }
                if (this.isTrackedPatchTried)
                {
                    return;
                }
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return;
                }
                if (!this.TryEnsureMapSpotOffsets(out IntPtr spotCls))
                {
                    return; // class/fields not ready — retry later
                }
                IntPtr trackedNative = this.ResolveBuildingMonoNative(spotCls, "get_IsTracked", 0);
                if (trackedNative == IntPtr.Zero)
                {
                    this.isTrackedPatchTried = true;
                    ModLogger.Msg("[MapSpots] avatar patch: MapSpot.get_IsTracked not resolved");
                    return;
                }
                mapSpotIsTrackedHook = MapSpotIsTrackedNative;
                mapSpotIsTrackedDetour = new MonoMod.RuntimeDetour.NativeDetour(trackedNative, mapSpotIsTrackedHook);
                mapSpotIsTrackedTrampoline = mapSpotIsTrackedDetour.GenerateTrampoline<IsFriendGetterDelegate>();
                this.isTrackedPatchTried = true;
                ModLogger.Msg("[MapSpots] avatar patch: tracked-frame suppression installed on MapSpot.get_IsTracked");
            }
            catch (Exception ex)
            {
                this.isTrackedPatchTried = true;
                ModLogger.Msg("[MapSpots] avatar patch: get_IsTracked detour failed: " + ex.Message);
            }
        }

        private void UndoIsTrackedPatch()
        {
            try
            {
                if (mapSpotIsTrackedDetour != null && mapSpotIsTrackedDetour.IsApplied)
                {
                    mapSpotIsTrackedDetour.Undo();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[MapSpots] avatar patch: get_IsTracked undo failed: " + ex.Message);
            }
        }

        private void EnsurePlayerAvatarPatches()
        {
            try
            {
                if (miniMapIsFriendDetour != null)
                {
                    if (!miniMapIsFriendDetour.IsApplied)
                    {
                        miniMapIsFriendDetour.Apply();
                    }
                    if (mapSpotIsFriendDetour != null && !mapSpotIsFriendDetour.IsApplied)
                    {
                        mapSpotIsFriendDetour.Apply();
                    }
                    return;
                }
                if (this.avatarPatchTried)
                {
                    return; // creation already failed permanently
                }
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                    || auraMonoClassGetFieldFromName == null || auraMonoFieldGetOffset == null)
                {
                    return; // AuraMono not up yet — retry on a later tick
                }

                IntPtr miniCls = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.MapSpots.MiniMapSpot");
                IntPtr spotCls = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.MapSpots.MapSpot");
                if (miniCls == IntPtr.Zero || spotCls == IntPtr.Zero)
                {
                    return; // image not loaded yet — retry later
                }

                if (!this.TryGetTrackFieldRawOffset(miniCls, "trackType", out int offTt)
                    || !this.TryGetTrackFieldRawOffset(miniCls, "usageId", out int offUse))
                {
                    this.avatarPatchTried = true;
                    ModLogger.Msg("[MapSpots] avatar patch: MiniMapSpot field offsets not resolved");
                    return;
                }
                IntPtr catField = auraMonoClassGetFieldFromName(spotCls, "category");
                IntPtr useField = auraMonoClassGetFieldFromName(spotCls, "usageId");
                if (catField == IntPtr.Zero || useField == IntPtr.Zero)
                {
                    this.avatarPatchTried = true;
                    ModLogger.Msg("[MapSpots] avatar patch: MapSpot fields not resolved");
                    return;
                }

                // Resolve + JIT-compile via the shared Building helper (mono_compile_method with IntPtr return).
                IntPtr miniNative = this.ResolveBuildingMonoNative(miniCls, "get_IsFriend", 0);
                IntPtr spotNative = this.ResolveBuildingMonoNative(spotCls, "get_IsFriend", 0);
                if (miniNative == IntPtr.Zero || spotNative == IntPtr.Zero)
                {
                    this.avatarPatchTried = true;
                    ModLogger.Msg("[MapSpots] avatar patch: get_IsFriend compile failed (mini="
                        + (miniNative != IntPtr.Zero) + " spot=" + (spotNative != IntPtr.Zero) + ")");
                    return;
                }

                miniMapOffTrackType = offTt;
                miniMapOffUsageId = offUse;
                mapSpotOffCategory = (int)auraMonoFieldGetOffset(catField);
                mapSpotOffUsageId = (int)auraMonoFieldGetOffset(useField);

                miniMapIsFriendHook = MiniMapIsFriendNative;
                mapSpotIsFriendHook = MapSpotIsFriendNative;
                miniMapIsFriendDetour = new MonoMod.RuntimeDetour.NativeDetour(miniNative, miniMapIsFriendHook);
                mapSpotIsFriendDetour = new MonoMod.RuntimeDetour.NativeDetour(spotNative, mapSpotIsFriendHook);

                this.avatarPatchTried = true;
                ModLogger.Msg("[MapSpots] avatar patch: detours installed on MiniMapSpot/MapSpot.get_IsFriend"
                    + " (self=" + mapAvatarSelfNetId + ")");
            }
            catch (Exception ex)
            {
                this.avatarPatchTried = true;
                ModLogger.Msg("[MapSpots] avatar patch failed: " + ex.Message);
            }
        }

        private void UndoPlayerAvatarPatches()
        {
            avatarForceFriendActive = false;
            try
            {
                if (miniMapIsFriendDetour != null && miniMapIsFriendDetour.IsApplied)
                {
                    miniMapIsFriendDetour.Undo();
                }
                if (mapSpotIsFriendDetour != null && mapSpotIsFriendDetour.IsApplied)
                {
                    mapSpotIsFriendDetour.Undo();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[MapSpots] avatar patch undo failed: " + ex.Message);
            }
            this.UndoFriendGatePatch();
        }

        private void UndoFriendGatePatch()
        {
            avatarForceFriendActive = false;
            try
            {
                if (friendGateDetour != null && friendGateDetour.IsApplied)
                {
                    friendGateDetour.Undo();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[MapSpots] friend-gate undo failed: " + ex.Message);
            }
        }

        // Bracket TrackingItem.GetAtlasSpriteId (the pointer icon source): set mapTrackWidgetRendering while the
        // icon is computed so FriendGateNative force-friends exactly for that window (sret struct return:
        // RCX=this, RDX=sret, R8=useReason). Allocation-free. The old MapTrackCellModel/MapTrackWidget.SetData
        // brackets were dropped with MapTrackCellModel in the 2026-07-09 update; GetAtlasSpriteId is their superset.
        private void EnsureTrackWidgetPatch()
        {
            try
            {
                if (getAtlasSpriteIdDetour != null)
                {
                    if (!getAtlasSpriteIdDetour.IsApplied)
                    {
                        getAtlasSpriteIdDetour.Apply();
                    }
                    if (mapTrackSetDataDetour != null && !mapTrackSetDataDetour.IsApplied)
                    {
                        mapTrackSetDataDetour.Apply();
                    }
                    return;
                }
                if (this.mapTrackSetDataPatchTried)
                {
                    return;
                }
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return;
                }
                IntPtr trackingItemCls = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.Navigation.TrackingItem");
                if (trackingItemCls == IntPtr.Zero)
                {
                    trackingItemCls = this.FindAuraMonoClassInAllLoadedImages("TrackingItem", "XDTGameSystem.GameplaySystem.Navigation");
                }
                if (trackingItemCls == IntPtr.Zero)
                {
                    if (!this.mapTrackClassMissLogged)
                    {
                        this.mapTrackClassMissLogged = true;
                        ModLogger.Msg("[MapSpots] avatar patch: TrackingItem class NOT resolved yet — retrying");
                    }
                    return; // image not loaded yet — retry later
                }
                IntPtr atlasNative = this.ResolveBuildingMonoNative(trackingItemCls, "GetAtlasSpriteId", 1);
                if (atlasNative == IntPtr.Zero)
                {
                    this.mapTrackSetDataPatchTried = true;
                    ModLogger.Msg("[MapSpots] avatar patch: GetAtlasSpriteId not resolved");
                    return;
                }
                getAtlasSpriteIdHook = GetAtlasSpriteIdNative;
                MonoMod.RuntimeDetour.NativeDetour ad = new MonoMod.RuntimeDetour.NativeDetour(atlasNative, getAtlasSpriteIdHook);
                try
                {
                    getAtlasSpriteIdTrampoline = ad.GenerateTrampoline<GetAtlasSpriteIdDelegate>();
                }
                catch
                {
                    try { ad.Undo(); } catch { }
                    getAtlasSpriteIdTrampoline = null;
                    throw;
                }
                getAtlasSpriteIdDetour = ad;

                // Second anchor: bracket MapTrackWidget.SetData(MapTrackHudItem) so its OWN in-line
                // FriendProtocolManager.TryGetFriendByNetId check (which chooses avatar vs placeholder) is
                // force-friended for our netIds too. Non-fatal if it can't resolve — GetAtlasSpriteId still
                // sets the URL, and the diagnostic will show it stayed on the placeholder path.
                IntPtr widgetCls = this.FindAuraMonoClassByFullName("XDTGame.UI.Widget.MapTrackWidget");
                if (widgetCls == IntPtr.Zero)
                {
                    widgetCls = this.FindAuraMonoClassInAllLoadedImages("MapTrackWidget", "XDTGame.UI.Widget");
                }
                IntPtr widgetSetData = widgetCls != IntPtr.Zero
                    ? this.ResolveBuildingMonoNative(widgetCls, "SetData", 1) : IntPtr.Zero;
                if (widgetSetData != IntPtr.Zero)
                {
                    mapTrackSetDataHook = MapTrackSetDataNative;
                    MonoMod.RuntimeDetour.NativeDetour wd = new MonoMod.RuntimeDetour.NativeDetour(widgetSetData, mapTrackSetDataHook);
                    try
                    {
                        mapTrackSetDataTrampoline = wd.GenerateTrampoline<MapTrackSetDataDelegate>();
                        mapTrackSetDataDetour = wd;
                        ModLogger.Msg("[MapSpots] avatar patch: MapTrackWidget.SetData bracket installed (second friend-gate anchor)");
                    }
                    catch (Exception exWidget)
                    {
                        try { wd.Undo(); } catch { }
                        mapTrackSetDataTrampoline = null;
                        mapTrackSetDataDetour = null;
                        ModLogger.Msg("[MapSpots] avatar patch: MapTrackWidget.SetData bracket failed: " + exWidget.Message);
                    }
                }
                else
                {
                    ModLogger.Msg("[MapSpots] avatar patch: MapTrackWidget.SetData NOT resolved (widgetCls="
                        + (widgetCls != IntPtr.Zero) + ") — GetAtlasSpriteId-only");
                }

                this.mapTrackSetDataPatchTried = true;
                ModLogger.Msg("[MapSpots] avatar patch: GetAtlasSpriteId detour installed (scoped world-pointer avatars)");
            }
            catch (Exception ex)
            {
                this.mapTrackSetDataPatchTried = true;
                ModLogger.Msg("[MapSpots] avatar patch: GetAtlasSpriteId detour failed: " + ex.Message);
            }
        }

        // Bracket body for MapTrackWidget.SetData: force-friend for the duration so the widget's own
        // Player friend check takes the avatar branch for our netIds. void instance, 1 ref-type arg.
        // Allocation-free; save/restore the flag for nesting safety (GetAtlasSpriteId may nest under it).
        private static void MapTrackSetDataNative(IntPtr self, IntPtr arg)
        {
            bool prev = mapTrackWidgetRendering;
            mapTrackWidgetRendering = true;
            try
            {
                if (mapTrackSetDataTrampoline != null)
                {
                    mapTrackSetDataTrampoline(self, arg);
                }
            }
            finally
            {
                mapTrackWidgetRendering = prev;
            }
        }

        // sret bracket for TrackingItem.GetAtlasSpriteId: force-friend for the duration so the Player branch
        // (avatar URL) is taken for our netIds regardless of the calling path. Returns the trampoline's result
        // (the sret pointer -> RAX). Allocation-free.
        private static IntPtr GetAtlasSpriteIdNative(IntPtr self, IntPtr sret, int useReason)
        {
            bool prev = mapTrackWidgetRendering;
            mapTrackWidgetRendering = true;
            try
            {
                return getAtlasSpriteIdTrampoline != null ? getAtlasSpriteIdTrampoline(self, sret, useReason) : sret;
            }
            finally
            {
                mapTrackWidgetRendering = prev;
            }
        }


        private void UndoTrackWidgetPatch()
        {
            try
            {
                if (getAtlasSpriteIdDetour != null && getAtlasSpriteIdDetour.IsApplied)
                {
                    getAtlasSpriteIdDetour.Undo();
                }
                if (mapTrackSetDataDetour != null && mapTrackSetDataDetour.IsApplied)
                {
                    mapTrackSetDataDetour.Undo();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[MapSpots] avatar patch: GetAtlasSpriteId undo failed: " + ex.Message);
            }
            mapTrackWidgetRendering = false;
        }

        // MapSpot.get_IsTracked replacement: keep the real value, but suppress the tracked square
        // (MapSpotWidget.tracked_go) on OUR radar spots:
        //  - Collectable category: vanilla never creates Collectable spots, so ALL of them are ours (the
        //    big-map resource markers) -> always suppress; the icon still resolves via GetAtlasSpriteID.
        //  - Player category: only our world-pointer players (usageId in the set) -> suppress; they then
        //    render like normal untracked player pins with just the friend avatar.
        private static unsafe byte MapSpotIsTrackedNative(IntPtr thisPtr)
        {
            byte real = mapSpotIsTrackedTrampoline != null ? mapSpotIsTrackedTrampoline(thisPtr) : (byte)0;
            if (real == 0 || thisPtr == IntPtr.Zero || mapSpotOffCategory < 0 || mapSpotOffUsageId < 0)
            {
                return real;
            }
            int category = *(int*)((byte*)thisPtr + mapSpotOffCategory);
            if (category == SpotEnumCollectable)
            {
                return 0; // resource big-map spot (always ours)
            }
            if (avatarForceFriendActive && category == SpotEnumPlayer)
            {
                int usage = *(int*)((byte*)thisPtr + mapSpotOffUsageId);
                if (usage != 0 && mapAvatarWorldNetIds.Contains((uint)usage))
                {
                    return 0; // our radar player marker
                }
            }
            return 1;
        }

        // Install (once) the FriendClientService.TryGetFriendByNetId trampoline detour that force-friends the
        // in-world-pointer player netIds. Needs a trampoline (call original) unlike the constant IsFriend
        // getters, so it uses GenerateTrampoline like the other conditional detours (PrivacyBlock/Fishing).
        private bool friendGatePatchTried;
        private bool friendGateClassMissLogged;
        private void EnsureFriendGatePatch()
        {
            try
            {
                if (friendGateDetour != null)
                {
                    if (!friendGateDetour.IsApplied)
                    {
                        friendGateDetour.Apply();
                    }
                    return;
                }
                if (this.friendGatePatchTried)
                {
                    return;
                }
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return;
                }
                // Concrete implementer of IFriendService.TryGetFriendByNetId (interface method is Mono-only).
                // NB the ilspy folder "EcsSystem/ClientSystem.Social.Friend" is the image name, but the C#
                // NAMESPACE is just "ClientSystem.Social.Friend".
                IntPtr cls = this.FindAuraMonoClassByFullName("ClientSystem.Social.Friend.FriendClientService");
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassInAllLoadedImages("FriendClientService", "ClientSystem.Social.Friend");
                }
                if (cls == IntPtr.Zero)
                {
                    if (!this.friendGateClassMissLogged)
                    {
                        this.friendGateClassMissLogged = true;
                        ModLogger.Msg("[MapSpots] avatar patch: FriendClientService class NOT resolved yet (retrying)");
                    }
                    return; // image not loaded yet — retry later
                }
                IntPtr native = this.ResolveBuildingMonoNative(cls, "TryGetFriendByNetId", 2);
                if (native == IntPtr.Zero)
                {
                    this.friendGatePatchTried = true;
                    ModLogger.Msg("[MapSpots] avatar patch: FriendClientService.TryGetFriendByNetId(2) not resolved");
                    return;
                }
                friendGateHook = FriendGateNative;
                friendGateDetour = new MonoMod.RuntimeDetour.NativeDetour(native, friendGateHook);
                friendGateTrampoline = friendGateDetour.GenerateTrampoline<TryGetFriendByNetIdDelegate>();
                this.friendGatePatchTried = true;
                ModLogger.Msg("[MapSpots] avatar patch: friend-gate detour installed on FriendClientService.TryGetFriendByNetId");
            }
            catch (Exception ex)
            {
                this.friendGatePatchTried = true;
                ModLogger.Msg("[MapSpots] avatar patch: friend-gate detour failed: " + ex.Message);
            }
        }

        // Trampoline body: run the real check first (this also fills `outFriend` with the real friend or the
        // default on a miss — so the out is always valid/null-safe). If it's a real friend, keep it. Else,
        // while the toggle is on, force TRUE for the specific player netIds we render an in-world pointer for
        // (out stays the original's default -> all readers null-safe) so MapTrackWidget shows the avatar.
        private static byte FriendGateNative(IntPtr self, uint netId, IntPtr outFriend)
        {
            byte real = friendGateTrampoline != null ? friendGateTrampoline(self, netId, outFriend) : (byte)0;
            if (real != 0)
            {
                return 1;
            }
            // Only force TRUE while a bracketed pointer render path is on the stack (mapTrackWidgetRendering) —
            // that confines the override to the in-world pointer's avatar rendering. Every other caller
            // (dialogs, panels, party, gifts) runs with the flag clear and gets the real (correct) result.
            if (avatarForceFriendActive && netId != 0u && netId != mapAvatarSelfNetId
                && mapAvatarWorldNetIds.Contains(netId))
            {
                // Force TRUE only while a bracketed pointer render path (GetAtlasSpriteId or
                // MapTrackWidget.SetData) is on the stack — confines the override to the world pointer.
                if (mapTrackWidgetRendering)
                {
                    return 1;
                }
            }
            return 0;
        }

        // MiniMapSpot.get_IsFriend replacement (applied only while the toggle is on): any player with a
        // real netId except self counts as a friend -> the minimap widget renders the avatar HeadIconWidget.
        // Non-player spots return false exactly like vanilla. `this` = raw struct pointer.
        private static unsafe byte MiniMapIsFriendNative(IntPtr thisPtr)
        {
            if (thisPtr == IntPtr.Zero || miniMapOffTrackType < 0 || miniMapOffUsageId < 0)
            {
                return 0;
            }
            if (*((byte*)thisPtr + miniMapOffTrackType) != MapTrackTypePlayer)
            {
                return 0;
            }
            uint usage = *(uint*)((byte*)thisPtr + miniMapOffUsageId);
            return (byte)((usage != 0u && usage != mapAvatarSelfNetId) ? 1 : 0);
        }

        // MapSpot.get_IsFriend replacement (big map + simple map widget). `this` = MapSpot OBJECT pointer.
        private static unsafe byte MapSpotIsFriendNative(IntPtr thisPtr)
        {
            if (thisPtr == IntPtr.Zero || mapSpotOffCategory < 0 || mapSpotOffUsageId < 0)
            {
                return 0;
            }
            if (*(int*)((byte*)thisPtr + mapSpotOffCategory) != SpotEnumPlayer)
            {
                return 0;
            }
            int usage = *(int*)((byte*)thisPtr + mapSpotOffUsageId);
            return (byte)((usage != 0 && (uint)usage != mapAvatarSelfNetId) ? 1 : 0);
        }

        private int GetGameMapSpotUsageId(GameObject marker, string label, Vector3 pos)
        {
            // Tracked, persistent targets (players/birds/insects/meteors) keep a stable id across radar
            // rescans; markers rebuilt every scan would otherwise churn add/remove.
            if (this.TryGetRadarMarkerTrackedTarget(marker, out GameObject target) && target != null)
            {
                int targetId = target.GetInstanceID();
                return targetId != 0 ? targetId : 1;
            }

            int px = Mathf.RoundToInt(pos.x * 2f);
            int pz = Mathf.RoundToInt(pos.z * 2f);
            int hash;
            unchecked
            {
                hash = ((label != null ? label.GetHashCode() : 0) * 397) ^ (px * 73856093) ^ (pz * 19349663);
            }
            return hash != 0 ? hash : 1;
        }

        private unsafe bool DispatchStartTrack(ulong token, Vector3 position, byte trackType, int staticId, uint targetNetId = 0u)
        {
            if (this.mapTrackDispatchStartMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            byte* buf = stackalloc byte[64];
            for (int i = 0; i < 64; i++) buf[i] = 0;
            float* posPtr = (float*)(buf + this.offTdPosition);
            posPtr[0] = position.x;
            posPtr[1] = position.y;
            posPtr[2] = position.z;
            *(ulong*)(buf + this.offTdToken) = token;
            *(uint*)(buf + this.offTdTargetNetId) = targetNetId;
            *(int*)(buf + this.offTdStaticId) = staticId;
            *(buf + this.offTdTrackType) = trackType;
            *(buf + this.offTdTrackReason) = MapTrackReasonLocal;

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)buf;
            auraMonoRuntimeInvoke(this.mapTrackDispatchStartMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        private unsafe bool DispatchStopTrack(ulong token)
        {
            if (this.mapTrackDispatchStopMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            byte* buf = stackalloc byte[32];
            for (int i = 0; i < 32; i++) buf[i] = 0;
            *(ulong*)(buf + this.offStToken) = token;

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)buf;
            auraMonoRuntimeInvoke(this.mapTrackDispatchStopMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        // Kept name so OnUpdate / Cleanup wiring is unchanged.
        private void ClearInjectedGameMapSpots()
        {
            if (this.mapTrackInjected.Count == 0 && this.mapBigSpotInjected.Count == 0)
            {
                return;
            }
            this.AttachAuraMonoThread();
            this.ClearInjectedBigMapSpots();

            int total = this.mapTrackInjected.Count, removed = 0, removeFail = 0;
            if (this.mapTrackDispatchStopMethod != IntPtr.Zero && auraMonoRuntimeInvoke != null && this.AttachAuraMonoThread())
            {
                this.mapTrackRemoveBuffer.Clear();
                foreach (KeyValuePair<ulong, Vector3> entry in this.mapTrackInjected)
                {
                    this.mapTrackRemoveBuffer.Add(entry.Key);
                }
                for (int i = 0; i < this.mapTrackRemoveBuffer.Count; i++)
                {
                    if (this.DispatchStopTrack(this.mapTrackRemoveBuffer[i]))
                    {
                        removed++;
                    }
                    else
                    {
                        removeFail++;
                    }
                }
            }

            this.mapTrackInjected.Clear();
            this.mapTrackInjectedType.Clear();
            this.mapTrackInjectedStaticId.Clear();
            this.mapTrackInjectedTargetNet.Clear();
            // Keep mapTrackLabelIcon warm so re-enabling the radar shows resolved icons immediately.
            ModLogger.Msg("[MapSpots] clear tracks: total=" + total + " removed=" + removed + " removeFail=" + removeFail);
        }
    }
}
