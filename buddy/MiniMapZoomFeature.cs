using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Minimap Zoom — HUD minimap scale, fixed or speed-driven (Self -> Minimap tab).
    //
    // HOW THE VANILLA MINIMAP IS BUILT (XDTGame.UI.Widget.CommonMapBar, ilspy-dumps/XDTGameUI):
    //   TriggerByMe writes, EVERY frame, anchoredPosition = -pos.xz * MapSystem.MapRatio(5) * 0.55 into
    //     map_bar@go@w/realmask_map/maproot@t/dec_sketch@t@img   (picture, prefab localScale 0.55)
    //     map_bar@go@w/map_sketch@t                              (spot layer, prefab localScale 0.55)
    //   Spots sit in map_sketch@t/map_spots@t at pos * 5 (MiniMapSpotWidget.RefreshMiniMapPosition).
    //   → 2.75 px per metre; the 224 px circular mask shows ~40 m of radius.
    //   The spot layer is NOT under the mask: MiniMapSystem.distance (40) hides spots and
    //   MiniMapSystem.trackDistance (40) clamps tracked pins to the edge.
    //
    // ZOOM k (verified live 2026-09-20 with tools/MiniMapZoomProbe):
    //   maproot@t.localScale = k             — picture and its offset live in one space, nothing else
    //   map_sketch@t.localScale = 0.55k       — plus anchoredPosition = dec_sketch.anchoredPosition * k,
    //                                           rewritten every frame after the game's own write
    //                                           (OnUpdate runs after CommonMapBar.Update)
    //   spot widgets (map_spots@t children)   — counter-scaled by 1/k so icons keep their size;
    //                                           SetData re-sets their base scale (1.15/1.3), detected by
    //                                           comparing against the value we last wrote
    //   MiniMapSystem.distance/trackDistance  — original / k, so nothing spills past the circle and
    //                                           tracked pins still clamp to its edge
    //   MapSystem.MapRatio is deliberately NOT touched: it is global, MapPanel/BusPanel read it and
    //   MapPanel resets it to 5.
    //
    // TWO minimaps exist: StatusPanel and VehicleStatusPanel (shown while riding, created on demand).
    // Every map_bar@go@w under XDUIRoot/Status is handled; the list is rescanned every 0.5 s.
    //
    // SPEED (auto-zoom; sources measured live, see project memory minimap-zoom-model):
    //   on foot / swim / skate  -> self MovementComponent._realSpeed (run = 3.5 m/s)
    //   driving (VehicleLocomotionNormal) -> vehicle controller.moveSpeed (0 -> 8 m/s, 0 on stop)
    //   passenger (VehicleLocomotionRemote) -> NOT currSpeed: it holds the last synced value (stuck at
    //     1.73-8.0 while parked). Uses the visible minimap's offset delta instead (matches real speed
    //     within ~3%).
    //   Curve: 0 m/s -> "Zoom at rest", top speed -> "Zoom at top speed", log-interpolated. Top speed
    //   = the current car's RunForwardMaxSpeed (VehicleComponent), 8 m/s (fastest Car row) on foot.
    //
    // LOOK-AHEAD (arrow sits off-centre while moving so more map shows ahead; verified live
    // 2026-09-20 with the probe):
    //   offset o (bar space) is added to the arrow (map_spot_me@img@t - the game only rotates it), to
    //   maproot@t (the game only rewrites it on a level change) and to the spot layer offset.
    //   Rotating-map mode (PlayerPrefs "MapRotation"): o = screen-down; north-up: o = -heading.
    //   ! DefaultModule._playerMapPos (the spot cut-off / clamp centre) cannot be moved: the game
    //   rewrites it before the spot widgets tick. So while look-ahead is on:
    //     trackDistance = huge  -> tracked pins arrive at their TRUE position; the mod then clamps any
    //                              pin outside the circle onto the circle edge along the ray from the
    //                              arrow (true direction from the player), every frame after the game
    //     distance = R + |o|    -> ordinary spots out to the far edge ahead
    //   and the mod decides by the icon CENTRE, as the game does: a centre outside the circle means
    //   tracked pin -> onto the rim (110 px) along the ray from the arrow, ordinary spot -> hidden
    //   (normal@go off). Icons whose centre is inside may overhang the rim, exactly like vanilla.
    //   Which icons are tracked: MiniMapSystem.GetMiniMapSpots() every 0.5 s, entries with
    //   isTrackedPoint || isNotification (the ones the game clamps instead of hiding), matched to icons
    //   by position (no GameObject -> MiniMapSpotWidget link exists: UIWidget.GetUIWidget builds a
    //   new wrapper each call).
    //   (Earlier versions: distance = R - |o| cancelled zooming out while moving; a circular mask on
    //   the spot layer clipped icons at the rim, which vanilla never does.)
    //   A pin's true position is re-learned whenever its anchoredPosition differs from what the mod
    //   last wrote (the game refreshes positions ~6x/s).
    public partial class HeartopiaComplete
    {
        private const float MiniMapZoomMin = 0.5f;
        private const float MiniMapZoomMax = 3f;
        private const float MiniMapZoomTopMin = 0.3f;
        private const float MiniMapZoomTopMax = 1.5f;
        private const float MiniMapZoomDefaultRest = 1.5f;
        private const float MiniMapZoomDefaultTop = 0.6f;
        private const float MiniMapLookAheadMin = 0.1f;          // fraction of the circle radius
        private const float MiniMapLookAheadMax = 0.6f;
        private const float MiniMapLookAheadDefault = 0.4f;
        // The offset uses the SAME speed scale as the zoom curve (the current car's
        // RunForwardMaxSpeed, 8 m/s on foot), so running (3.5 m/s) offsets about half as far as a car
        // at full speed. Per-mode references made running and driving look identical, and a fixed
        // 3.5 m/s reference made everything above walking look identical.
        private const float MiniMapTrackDistanceUnbounded = 100000f;
        private const float MiniMapTrackedRefreshInterval = 0.5f;
        private const float MiniMapTrackedMatchMetres = 2f;     // icon <-> GetMiniMapSpots entry

        // CommonMapBar constants (TriggerByMe's literal 0.55, MapSystem.MapRatio 5).
        private const float MiniMapVanillaSketchScale = 0.55f;
        private const float MiniMapPixelsPerMetre = 5f * 0.55f;
        private const float MiniMapDefaultTopSpeed = 8f;   // Car.runForwardMaxSpeed max (81051+)
        private const float MiniMapSpeedSampleInterval = 0.1f;
        private const float MiniMapBarRescanInterval = 0.5f;
        private const float MiniMapDistanceApplyInterval = 0.25f;
        private const float MiniMapTeleportJumpMetres = 30f;
        private const string MiniMapStatusRootPath = "GameApp/startup_root(Clone)/XDUIRoot/Status";
        private const string MiniMapBarPath = "AniRoot@ani@queueanimation/top_left_layout@go/map_bar@go@w";
        private const string MiniMapSystemTypeName = "XDTGameSystem.GameplaySystem.MapSpots.MiniMapSystem";

        internal static readonly string[] MiniMapZoomReactionNames = { "Smooth", "Normal", "Fast" };

        // Seconds to (roughly) settle when speeding up / slowing down, and how long a slowdown is
        // ignored before the map starts zooming back in. Index = miniMapZoomReaction.
        private static readonly float[] MiniMapReactionUp = { 1.0f, 0.5f, 0.25f };
        private static readonly float[] MiniMapReactionDown = { 3.0f, 1.5f, 0.7f };
        private static readonly float[] MiniMapReactionHold = { 2.0f, 1.0f, 0.3f };

        // --- persisted settings ---
        private bool miniMapZoomEnabled;
        private float miniMapZoomRest = MiniMapZoomDefaultRest;
        private bool miniMapAutoZoomEnabled;
        private float miniMapZoomTop = MiniMapZoomDefaultTop;
        private int miniMapZoomReaction = 1;
        private bool miniMapLookAheadEnabled;
        private float miniMapLookAheadAmount = MiniMapLookAheadDefault;

        // --- runtime ---
        private sealed class MiniMapBar
        {
            public RectTransform Root;
            public RectTransform Maproot;
            public RectTransform Sketch;
            public RectTransform Dec;
            public RectTransform Me;
            public bool IsVehiclePanel;
            public bool Alive => this.Maproot != null && this.Sketch != null && this.Dec != null
                && this.Me != null && this.Root != null;
        }

        // A position the game owns but the mod offsets: the base is re-learned whenever the live value
        // is not the one the mod wrote last (a level change moves maproot, a refresh moves a spot).
        private struct MiniMapOwnedPos
        {
            public Vector2 Base;
            public Vector2 Written;
        }

        private readonly List<MiniMapBar> miniMapBars = new List<MiniMapBar>();
        private readonly Dictionary<int, float> miniMapIconBase = new Dictionary<int, float>();
        private readonly Dictionary<int, float> miniMapIconWritten = new Dictionary<int, float>();
        private readonly Dictionary<int, MiniMapOwnedPos> miniMapOwnedPos = new Dictionary<int, MiniMapOwnedPos>();
        private readonly Dictionary<int, RectTransform> miniMapSpotRects = new Dictionary<int, RectTransform>();
        private Vector2 miniMapLookOffset;      // smoothed, screen-aligned (bar space in north-up mode)
        private bool miniMapRotatingMap;
        private float miniMapNextPrefsAt;
        private readonly List<Vector2> miniMapTrackedPositions = new List<Vector2>();   // world x,z
        private readonly Dictionary<int, GameObject> miniMapSpotNormals = new Dictionary<int, GameObject>();
        private readonly HashSet<int> miniMapHiddenSpots = new HashSet<int>();
        private float miniMapNextTrackedRefreshAt;
        private IntPtr miniMapGetSpotsMethod;
        private Transform miniMapStatusRoot;
        private int miniMapLoggedBarCount = -1;
        private float miniMapNextRescanAt;
        private int miniMapEpoch = -1;
        private bool miniMapApplied;            // our scales are on screen (restore needed on disable)
        private float miniMapCurrentK = 1f;

        private float miniMapNextSampleAt;
        private float miniMapRawSpeed;
        private float miniMapSmoothSpeed;
        private float miniMapLastFastAt;
        private float miniMapTopSpeed = MiniMapDefaultTopSpeed;
        private string miniMapSpeedSource = "-";
        private Vector2 miniMapLastMapPos;
        private float miniMapLastMapAt = -1f;
        private bool miniMapLastMapVehicle;

        private IntPtr miniMapSystemClass;
        private IntPtr miniMapDistanceField;
        private IntPtr miniMapTrackDistanceField;
        private IntPtr miniMapFieldsClass;
        private float miniMapOrigDistance = 40f;
        private float miniMapOrigTrackDistance = 40f;
        private bool miniMapOrigCaptured;
        private float miniMapWrittenDistance = 40f;
        private float miniMapWrittenTrackDistance = 40f;
        private int miniMapDistanceEpoch = -1;
        private float miniMapNextDistanceAt;

        private string miniMapZoomStatus = "Idle.";
        private string miniMapZoomLastLoggedStatus;
        private FeatureBreakerState miniMapZoomBreaker;

        private void ProcessMiniMapZoomOnUpdate()
        {
            bool active = this.miniMapZoomEnabled && this.IsWorldReady;
            if (!active && !this.miniMapApplied)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (!this.miniMapZoomBreaker.ShouldRun(now))
            {
                return;
            }

            try
            {
                if (!active)
                {
                    this.RestoreMiniMapZoom();
                    this.miniMapZoomBreaker.Success();
                    return;
                }

                if (this.miniMapEpoch != AuraMonoWorldEpoch)
                {
                    this.miniMapEpoch = AuraMonoWorldEpoch;
                    this.miniMapBars.Clear();
                    this.miniMapIconBase.Clear();
                    this.miniMapIconWritten.Clear();
                    this.miniMapOwnedPos.Clear();
                    this.miniMapSpotRects.Clear();
                    this.miniMapSpotNormals.Clear();
                    this.miniMapHiddenSpots.Clear();
                    this.miniMapTrackedPositions.Clear();
                    this.miniMapLookOffset = Vector2.zero;
                    this.miniMapStatusRoot = null;
                    this.miniMapLastMapAt = -1f;
                    this.miniMapSmoothSpeed = 0f;
                    this.miniMapRawSpeed = 0f;
                    this.miniMapNextRescanAt = 0f;
                }

                if (now >= this.miniMapNextRescanAt || this.MiniMapAnyBarDead())
                {
                    this.miniMapNextRescanAt = now + MiniMapBarRescanInterval;
                    this.RescanMiniMapBars();
                }

                float k = this.miniMapZoomRest;
                if (this.miniMapAutoZoomEnabled || this.miniMapLookAheadEnabled)
                {
                    if (now >= this.miniMapNextSampleAt)
                    {
                        this.miniMapNextSampleAt = now + MiniMapSpeedSampleInterval;
                        this.SampleMiniMapSpeed(now);
                    }

                    this.SmoothMiniMapSpeed(now, Time.unscaledDeltaTime);
                    if (this.miniMapAutoZoomEnabled)
                    {
                        k = this.MiniMapZoomForSpeed(this.miniMapSmoothSpeed);
                    }
                }

                this.miniMapCurrentK = Mathf.Clamp(k, MiniMapZoomTopMin, MiniMapZoomMax);
                if (this.miniMapBars.Count == 0)
                {
                    // No minimap on screen yet: widening the spot cut-off without scaling the map
                    // would just scatter icons outside the circle.
                    this.MiniMapZoomSetStatus("Waiting for the HUD minimap.", log: true);
                    this.miniMapZoomBreaker.Success();
                    return;
                }

                this.UpdateMiniMapLookOffset(now, Time.unscaledDeltaTime);
                this.ApplyMiniMapZoom(this.miniMapCurrentK);

                if (now >= this.miniMapNextDistanceAt)
                {
                    this.miniMapNextDistanceAt = now + MiniMapDistanceApplyInterval;
                    float kd = this.miniMapCurrentK;
                    if (this.miniMapLookAheadEnabled)
                    {
                        // Ordinary spots: out to the far edge ahead; the mask clips the rest.
                        float edgePx = this.miniMapOrigTrackDistance * MiniMapPixelsPerMetre;
                        float reach = 1f + this.miniMapLookOffset.magnitude / edgePx;
                        this.ApplyMiniMapDistances(this.miniMapOrigDistance * reach / kd, MiniMapTrackDistanceUnbounded, force: false);
                    }
                    else
                    {
                        this.ApplyMiniMapDistances(this.miniMapOrigDistance / kd, this.miniMapOrigTrackDistance / kd, force: false);
                    }
                }

                this.MiniMapZoomSetStatus(this.miniMapAutoZoomEnabled || this.miniMapLookAheadEnabled
                    ? this.LF("{0:F1} m/s ({1}), zoom {2:F2}x", this.miniMapRawSpeed, this.miniMapSpeedSource, this.miniMapCurrentK)
                    : this.LF("Zoom {0:F2}x", this.miniMapCurrentK), log: false);
                this.miniMapZoomBreaker.Success();
            }
            catch (Exception ex)
            {
                this.miniMapZoomBreaker.Failure("MiniMapZoom", ex, now);
                this.miniMapZoomStatus = "Error: " + ex.Message;
            }
        }

        // --- minimap instances ---------------------------------------------------------------

        private bool MiniMapAnyBarDead()
        {
            for (int i = 0; i < this.miniMapBars.Count; i++)
            {
                if (!this.miniMapBars[i].Alive)
                {
                    return true;
                }
            }
            return false;
        }

        // XDUIRoot/Status holds a handful of HUD panels; only the ones with a map bar matter. A full
        // FindObjectsOfType<RectTransform> would walk thousands of UI nodes every rescan.
        private void RescanMiniMapBars()
        {
            if (this.miniMapStatusRoot == null)
            {
                GameObject statusGo = GameObject.Find(MiniMapStatusRootPath);
                this.miniMapStatusRoot = statusGo != null ? statusGo.transform : null;
            }

            this.miniMapBars.Clear();
            if (this.miniMapStatusRoot == null)
            {
                return;
            }

            for (int i = 0; i < this.miniMapStatusRoot.childCount; i++)
            {
                Transform panel = this.miniMapStatusRoot.GetChild(i);
                Transform barT = panel != null ? panel.Find(MiniMapBarPath) : null;
                if (barT == null)
                {
                    continue;
                }

                // NOT `Find(...) as RectTransform`: Il2CppInterop wraps the pointer as the method's
                // declared return type (Transform), so the C# cast is null unless an earlier
                // FindObjectsOfType<RectTransform> happened to cache a RectTransform wrapper for it.
                MiniMapBar bar = new MiniMapBar
                {
                    Root = barT.GetComponent<RectTransform>(),
                    Maproot = MiniMapFindRect(barT, "realmask_map/maproot@t"),
                    Sketch = MiniMapFindRect(barT, "map_sketch@t"),
                    Me = MiniMapFindRect(barT, "map_spot_me@img@t"),
                    IsVehiclePanel = panel.name.StartsWith("VehicleStatusPanel", StringComparison.Ordinal),
                };
                if (bar.Maproot != null)
                {
                    bar.Dec = MiniMapFindRect(bar.Maproot, "dec_sketch@t@img");
                }

                if (bar.Alive)
                {
                    this.miniMapBars.Add(bar);
                }
            }

            // Nothing found under a cached root: the root may be a leftover from the loading UI, or
            // the HUD is not built yet. Drop it so the next rescan resolves the path again.
            if (this.miniMapBars.Count == 0)
            {
                this.miniMapStatusRoot = null;
            }

            if (this.miniMapBars.Count != this.miniMapLoggedBarCount)
            {
                this.miniMapLoggedBarCount = this.miniMapBars.Count;
                ModLogger.Msg("[MiniMapZoom] minimaps found: " + this.miniMapBars.Count);
            }
        }

        private static RectTransform MiniMapFindRect(Transform parent, string path)
        {
            Transform child = parent.Find(path);
            return child != null ? child.GetComponent<RectTransform>() : null;
        }

        private void ApplyMiniMapZoom(float k)
        {
            if (this.miniMapLookAheadEnabled && Time.unscaledTime >= this.miniMapNextTrackedRefreshAt)
            {
                this.miniMapNextTrackedRefreshAt = Time.unscaledTime + MiniMapTrackedRefreshInterval;
                this.RefreshMiniMapTrackedPositions();
            }

            float sketchScale = MiniMapVanillaSketchScale * k;
            for (int i = 0; i < this.miniMapBars.Count; i++)
            {
                MiniMapBar bar = this.miniMapBars[i];
                if (!bar.Alive)
                {
                    continue;
                }

                if (Mathf.Abs(bar.Maproot.localScale.x - k) > 1e-4f)
                {
                    bar.Maproot.localScale = new Vector3(k, k, 1f);
                }

                if (Mathf.Abs(bar.Sketch.localScale.x - sketchScale) > 1e-4f)
                {
                    bar.Sketch.localScale = new Vector3(sketchScale, sketchScale, sketchScale);
                }

                // Look-ahead offset in this bar's own (camera-rotated in rotating-map mode) space.
                Vector2 o = Vector2.zero;
                if (this.miniMapLookOffset.sqrMagnitude > 0.01f)
                {
                    Vector3 local = new Vector3(this.miniMapLookOffset.x, this.miniMapLookOffset.y, 0f);
                    if (this.miniMapRotatingMap)
                    {
                        local = Quaternion.Inverse(bar.Root.rotation) * local;
                    }
                    o = new Vector2(local.x, local.y);
                }

                this.SetMiniMapOwnedPos(bar.Me, o);
                this.SetMiniMapOwnedPos(bar.Maproot, o);

                // The game rewrote the spot layer offset this frame with its 0.55 literal; the picture
                // (dec_sketch) got the same vector inside the now-scaled maproot.
                bar.Sketch.anchoredPosition = bar.Dec.anchoredPosition * k + o;
                this.CounterScaleMiniMapIcons(bar, 1f / k);
                if (this.miniMapLookAheadEnabled)
                {
                    this.PlaceMiniMapSpotsForLookAhead(bar, o, sketchScale);
                }
            }

            if (!this.miniMapLookAheadEnabled && this.miniMapHiddenSpots.Count > 0)
            {
                this.UnhideMiniMapSpots();
            }

            this.miniMapApplied = true;
        }

        // Adds `offset` to a game-owned anchoredPosition (zero restores it).
        private void SetMiniMapOwnedPos(RectTransform rt, Vector2 offset)
        {
            int id = rt.GetInstanceID();
            Vector2 current = rt.anchoredPosition;
            if (!this.miniMapOwnedPos.TryGetValue(id, out MiniMapOwnedPos owned) || (current - owned.Written).sqrMagnitude > 1e-4f)
            {
                owned.Base = current; // first sight, or the game moved it
            }

            owned.Written = owned.Base + offset;
            if ((current - owned.Written).sqrMagnitude > 1e-4f)
            {
                rt.anchoredPosition = owned.Written;
            }
            this.miniMapOwnedPos[id] = owned;
        }

        // Positions (world x,z) of the spots the game CLAMPS rather than hides (MiniMapSpotWidget:
        // isTrackedPoint || isNotification). GetMiniMapSpots is what CommonMapBar itself calls on a
        // refresh; it only rebuilds MiniMapSystem's private scratch list.
        private void RefreshMiniMapTrackedPositions()
        {
            if (!this.EnsureAuraMonoApiReady() || !AuraMonoPinningAvailable || auraMonoRuntimeInvoke == null
                || auraMonoObjectGetClass == null)
            {
                return;
            }

            if (this.miniMapSystemClass == IntPtr.Zero)
            {
                this.miniMapSystemClass = this.FindAuraMonoClassByFullName(MiniMapSystemTypeName);
            }
            if (this.miniMapSystemClass == IntPtr.Zero)
            {
                return;
            }

            IntPtr instance = this.TryGetAuraMonoDataModuleInstance(this.miniMapSystemClass);
            if (instance == IntPtr.Zero)
            {
                return;
            }

            List<IntPtr> items = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            uint instancePin = AuraMonoPinNew(instance);
            uint listPin = 0U;
            try
            {
                if (this.miniMapGetSpotsMethod == IntPtr.Zero)
                {
                    this.miniMapGetSpotsMethod = this.FindAuraMonoMethodOnHierarchy(
                        auraMonoObjectGetClass(instance), "GetMiniMapSpots", 0);
                    if (this.miniMapGetSpotsMethod == IntPtr.Zero)
                    {
                        this.MiniMapZoomSetStatus("MiniMapSystem.GetMiniMapSpots unresolved (game update?).", log: true);
                        return;
                    }
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr list = auraMonoRuntimeInvoke(this.miniMapGetSpotsMethod, instance, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || list == IntPtr.Zero)
                {
                    return;
                }

                listPin = AuraMonoPinNew(list);
                if (!this.TryEnumerateAuraMonoCollectionItems(list, items, pins))
                {
                    return;
                }

                this.miniMapTrackedPositions.Clear();
                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr spot = items[i]; // boxed MiniMapSpot, pinned
                    bool tracked = this.TryGetMonoBoolMember(spot, "isTrackedPoint", out bool isTracked) && isTracked;
                    bool notification = this.TryGetMonoBoolMember(spot, "isNotification", out bool isNote) && isNote;
                    if ((tracked || notification) && this.TryGetMonoVector3Member(spot, "position", out Vector3 pos))
                    {
                        this.miniMapTrackedPositions.Add(new Vector2(pos.x, pos.z));
                    }
                }
            }
            finally
            {
                this.FreeMiniMapPins(pins);
                if (listPin != 0U)
                {
                    AuraMonoPinFree(listPin);
                }
                AuraMonoPinFree(instancePin);
            }
        }

        private void FreeMiniMapPins(List<uint> pins)
        {
            for (int i = 0; i < pins.Count; i++)
            {
                if (pins[i] != 0U)
                {
                    AuraMonoPinFree(pins[i]);
                }
            }
            pins.Clear();
        }

        private bool IsMiniMapTrackedPosition(Vector2 world)
        {
            float tolSqr = MiniMapTrackedMatchMetres * MiniMapTrackedMatchMetres;
            for (int i = 0; i < this.miniMapTrackedPositions.Count; i++)
            {
                if ((this.miniMapTrackedPositions[i] - world).sqrMagnitude <= tolSqr)
                {
                    return true;
                }
            }
            return false;
        }

        // Vanilla rule, applied to the shifted circle: an icon whose CENTRE falls outside the circle
        // is clamped onto the rim if the game would clamp it (tracked / notification) and hidden
        // otherwise. trackDistance is unbounded, so every icon arrives at its true position.
        private void PlaceMiniMapSpotsForLookAhead(MiniMapBar bar, Vector2 arrow, float sketchScale)
        {
            if (bar.Sketch.childCount == 0 || sketchScale <= 0f)
            {
                return;
            }

            float edge = this.miniMapOrigTrackDistance * MiniMapPixelsPerMetre; // vanilla rim (110 px)
            Vector2 layerOrigin = bar.Sketch.anchoredPosition;
            Transform spots = bar.Sketch.GetChild(0); // map_spots@t
            for (int i = 0; i < spots.childCount; i++)
            {
                Transform child = spots.GetChild(i);
                if (!child.gameObject.activeSelf)
                {
                    continue;
                }

                int id = child.GetInstanceID();
                if (!this.miniMapSpotRects.TryGetValue(id, out RectTransform rt) || rt == null)
                {
                    rt = child.GetComponent<RectTransform>();
                    this.miniMapSpotRects[id] = rt;
                }

                Vector2 current = rt.anchoredPosition;
                if (!this.miniMapOwnedPos.TryGetValue(id, out MiniMapOwnedPos owned) || (current - owned.Written).sqrMagnitude > 1e-4f)
                {
                    owned.Base = current; // the game's fresh (true, unclamped) position
                }

                Vector2 onBar = layerOrigin + owned.Base * sketchScale;
                Vector2 target = owned.Base;
                bool hide = false;
                if (onBar.sqrMagnitude > edge * edge)
                {
                    if (this.IsMiniMapTrackedPosition(owned.Base / 5f))
                    {
                        Vector2 dir = onBar - arrow;
                        float len = dir.magnitude;
                        if (len > 1e-3f)
                        {
                            dir /= len;
                            // |arrow + t*dir| = edge, t > 0 (the arrow is always inside the circle)
                            float b = Vector2.Dot(arrow, dir);
                            float c = arrow.sqrMagnitude - edge * edge;
                            float t = -b + Mathf.Sqrt(Mathf.Max(0f, b * b - c));
                            target = (arrow + dir * t - layerOrigin) / sketchScale;
                        }
                    }
                    else
                    {
                        hide = true;
                    }
                }

                owned.Written = target;
                if ((current - target).sqrMagnitude > 1e-4f)
                {
                    rt.anchoredPosition = target;
                }
                this.miniMapOwnedPos[id] = owned;
                this.SetMiniMapSpotHidden(child, id, hide);
            }
        }

        // normal@go is what the game itself toggles for out-of-range spots; it turns it back on at its
        // next refresh while in range, so the mod re-applies every frame (after the game) and undoes
        // only what it hid.
        private void SetMiniMapSpotHidden(Transform spot, int id, bool hide)
        {
            if (!this.miniMapSpotNormals.TryGetValue(id, out GameObject normal) || normal == null)
            {
                Transform normalT = spot.Find("AniRoot/normal@go");
                normal = normalT != null ? normalT.gameObject : null;
                this.miniMapSpotNormals[id] = normal;
            }
            if (normal == null)
            {
                return;
            }

            if (hide)
            {
                if (normal.activeSelf)
                {
                    normal.SetActive(false);
                }
                this.miniMapHiddenSpots.Add(id);
            }
            else if (this.miniMapHiddenSpots.Remove(id) && !normal.activeSelf)
            {
                normal.SetActive(true);
            }
        }

        // Look-ahead switched off (or the feature restored): give back what the mod hid.
        private void UnhideMiniMapSpots()
        {
            foreach (int id in this.miniMapHiddenSpots)
            {
                if (this.miniMapSpotNormals.TryGetValue(id, out GameObject normal) && normal != null && !normal.activeSelf)
                {
                    normal.SetActive(true);
                }
            }
            this.miniMapHiddenSpots.Clear();
        }

        // Target offset from the smoothed speed, full from running speed up. Rotating map: straight
        // down the screen (camera-forward is up). North-up: behind the arrow's heading.
        private void UpdateMiniMapLookOffset(float now, float dt)
        {
            if (now >= this.miniMapNextPrefsAt)
            {
                this.miniMapNextPrefsAt = now + 1f;
                this.miniMapRotatingMap = PlayerPrefs.GetInt("MapRotation", 0) >= 1;
            }

            Vector2 target = Vector2.zero;
            if (this.miniMapLookAheadEnabled && this.miniMapBars.Count > 0 && this.miniMapBars[0].Alive)
            {
                float edgePx = this.miniMapOrigTrackDistance * MiniMapPixelsPerMetre;
                float d = edgePx * this.miniMapLookAheadAmount
                    * Mathf.Clamp01(this.miniMapSmoothSpeed / Mathf.Max(0.5f, this.miniMapTopSpeed));
                if (this.miniMapRotatingMap)
                {
                    target = new Vector2(0f, -d);
                }
                else
                {
                    // The game sets the arrow's localEulerAngles = back * yaw, i.e. z = -yaw.
                    float yaw = -this.miniMapBars[0].Me.localEulerAngles.z * Mathf.Deg2Rad;
                    target = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw)) * d;
                }
            }

            if (dt > 0f)
            {
                int r = Mathf.Clamp(this.miniMapZoomReaction, 0, MiniMapReactionUp.Length - 1);
                float settle = target.sqrMagnitude > this.miniMapLookOffset.sqrMagnitude
                    ? MiniMapReactionUp[r]
                    : MiniMapReactionDown[r];
                float alpha = 1f - Mathf.Exp(-dt * 3f / Mathf.Max(0.05f, settle));
                this.miniMapLookOffset += (target - this.miniMapLookOffset) * alpha;
            }
        }

        private void CounterScaleMiniMapIcons(MiniMapBar bar, float factor)
        {
            if (bar.Sketch.childCount == 0)
            {
                return;
            }

            Transform spots = bar.Sketch.GetChild(0); // map_spots@t
            for (int i = 0; i < spots.childCount; i++)
            {
                Transform icon = spots.GetChild(i);
                int id = icon.GetInstanceID();
                float current = icon.localScale.x;
                if (!this.miniMapIconWritten.TryGetValue(id, out float written) || Mathf.Abs(written - current) > 1e-4f)
                {
                    this.miniMapIconBase[id] = current; // SetData (re)set the base scale
                }

                float target = this.miniMapIconBase[id] * factor;
                if (Mathf.Abs(current - target) > 1e-4f)
                {
                    icon.localScale = new Vector3(target, target, 1f);
                }
                this.miniMapIconWritten[id] = target;
            }
        }

        private void RestoreMiniMapZoom()
        {
            if (this.miniMapBars.Count == 0 || this.MiniMapAnyBarDead())
            {
                this.RescanMiniMapBars();
            }

            for (int i = 0; i < this.miniMapBars.Count; i++)
            {
                MiniMapBar bar = this.miniMapBars[i];
                if (!bar.Alive)
                {
                    continue;
                }

                bar.Maproot.localScale = Vector3.one;
                bar.Sketch.localScale = new Vector3(MiniMapVanillaSketchScale, MiniMapVanillaSketchScale, MiniMapVanillaSketchScale);
                this.CounterScaleMiniMapIcons(bar, 1f);
                this.SetMiniMapOwnedPos(bar.Me, Vector2.zero);
                this.SetMiniMapOwnedPos(bar.Maproot, Vector2.zero);
                // The spot layer and the pins are rewritten by the game on its next refresh.
            }

            // MiniMapSystem is a world-scoped module; only worth restoring while a world exists (a
            // new world builds a fresh instance with vanilla values anyway).
            if (this.IsWorldReady)
            {
                this.ApplyMiniMapDistances(this.miniMapOrigDistance, this.miniMapOrigTrackDistance, force: true);
            }

            this.miniMapApplied = false;
            this.miniMapCurrentK = 1f;
            this.miniMapLookOffset = Vector2.zero;
            this.UnhideMiniMapSpots();
            this.miniMapSpotNormals.Clear();
            this.miniMapTrackedPositions.Clear();
            this.miniMapIconBase.Clear();
            this.miniMapIconWritten.Clear();
            this.miniMapOwnedPos.Clear();
            this.miniMapSpotRects.Clear();
            this.MiniMapZoomSetStatus("Restored.", log: true);
        }

        // --- speed -----------------------------------------------------------------------------

        private void SampleMiniMapSpeed(float now)
        {
            bool inVehicle = false;
            bool remote = false;
            float speed = 0f;
            string source = "foot";

            if (this.EnsureAuraMonoApiReady() && AuraMonoPinningAvailable)
            {
                IntPtr vehicleObj = this.TryGetSelfEntityVehicleComponentMono();
                if (vehicleObj != IntPtr.Zero)
                {
                    inVehicle = true;
                    uint vehiclePin = AuraMonoPinNew(vehicleObj);
                    try
                    {
                        if (this.TryGetMonoSingleMember(vehicleObj, "RunForwardMaxSpeed", out float top) && top > 0.5f)
                        {
                            this.miniMapTopSpeed = top;
                        }

                        if (this.TryGetMonoObjectMember(vehicleObj, "controller", out IntPtr controllerObj) && controllerObj != IntPtr.Zero)
                        {
                            uint controllerPin = AuraMonoPinNew(controllerObj);
                            try
                            {
                                remote = this.IsMiniMapRemoteVehicleLocomotion(controllerObj);
                                if (!remote && this.TryGetMonoSingleMember(controllerObj, "moveSpeed", out float vehicleSpeed))
                                {
                                    speed = Mathf.Abs(vehicleSpeed);
                                    source = "car";
                                }
                            }
                            finally
                            {
                                AuraMonoPinFree(controllerPin);
                            }
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(vehiclePin);
                    }
                }
                else
                {
                    this.miniMapTopSpeed = MiniMapDefaultTopSpeed;
                    if (this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) && playerObj != IntPtr.Zero)
                    {
                        uint playerPin = AuraMonoPinNew(playerObj);
                        try
                        {
                            if (this.TryGetBunnyHopMonoMoveComponent(playerObj, out IntPtr moveObj) && moveObj != IntPtr.Zero
                                && this.TryGetMonoSingleMember(moveObj, "_realSpeed", out float realSpeed))
                            {
                                speed = Mathf.Abs(realSpeed);
                            }
                        }
                        finally
                        {
                            AuraMonoPinFree(playerPin);
                        }
                    }
                }
            }

            // The visible minimap's offset delta: the passenger's source, and kept primed otherwise so
            // switching to it never starts from a stale position.
            float mapSpeed = this.SampleMiniMapOffsetSpeed(now, inVehicle, out bool mapValid);
            if (inVehicle && (remote || source != "car"))
            {
                speed = mapValid ? mapSpeed : 0f;
                source = "passenger";
            }

            this.miniMapRawSpeed = speed;
            this.miniMapSpeedSource = source;
        }

        // VehicleLocomotionRemote = someone else drives and currSpeed only mirrors network messages.
        private bool IsMiniMapRemoteVehicleLocomotion(IntPtr controllerObj)
        {
            if (auraMonoObjectGetClass == null || auraMonoClassGetName == null
                || !this.TryGetMonoObjectMember(controllerObj, "_locomotion", out IntPtr locomotionObj) || locomotionObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr klass = auraMonoObjectGetClass(locomotionObj);
            string name = klass != IntPtr.Zero ? Marshal.PtrToStringAnsi(auraMonoClassGetName(klass)) : null;
            return name != null && name.IndexOf("Remote", StringComparison.Ordinal) >= 0;
        }

        private float SampleMiniMapOffsetSpeed(float now, bool inVehicle, out bool valid)
        {
            valid = false;
            MiniMapBar bar = null;
            for (int i = 0; i < this.miniMapBars.Count; i++)
            {
                if (this.miniMapBars[i].Alive && this.miniMapBars[i].IsVehiclePanel == inVehicle)
                {
                    bar = this.miniMapBars[i];
                    break;
                }
            }

            if (bar == null)
            {
                this.miniMapLastMapAt = -1f;
                return 0f;
            }

            Vector2 pos = bar.Dec.anchoredPosition / -MiniMapPixelsPerMetre;
            float speed = 0f;
            if (this.miniMapLastMapAt > 0f && now > this.miniMapLastMapAt && this.miniMapLastMapVehicle == inVehicle)
            {
                float metres = (pos - this.miniMapLastMapPos).magnitude;
                // A teleport or a panel handover looks like a huge jump — ignore that sample.
                if (metres < MiniMapTeleportJumpMetres)
                {
                    speed = metres / (now - this.miniMapLastMapAt);
                    valid = true;
                }
            }

            this.miniMapLastMapPos = pos;
            this.miniMapLastMapAt = now;
            this.miniMapLastMapVehicle = inVehicle;
            return speed;
        }

        // Fast when speeding up, slow (and after a hold) when slowing down — stops and turns must not
        // make the map breathe. Settle times are ~3 time constants.
        private void SmoothMiniMapSpeed(float now, float dt)
        {
            if (dt <= 0f)
            {
                return;
            }

            int r = Mathf.Clamp(this.miniMapZoomReaction, 0, MiniMapReactionUp.Length - 1);
            float target = this.miniMapRawSpeed;
            if (target >= this.miniMapSmoothSpeed * 0.9f)
            {
                this.miniMapLastFastAt = now;
            }

            float settle;
            if (target > this.miniMapSmoothSpeed)
            {
                settle = MiniMapReactionUp[r];
            }
            else if (now - this.miniMapLastFastAt < MiniMapReactionHold[r])
            {
                return;
            }
            else
            {
                settle = MiniMapReactionDown[r];
            }

            float alpha = 1f - Mathf.Exp(-dt * 3f / Mathf.Max(0.05f, settle));
            this.miniMapSmoothSpeed += (target - this.miniMapSmoothSpeed) * alpha;
        }

        private float MiniMapZoomForSpeed(float speed)
        {
            float t = Mathf.Clamp01(speed / Mathf.Max(0.5f, this.miniMapTopSpeed));
            float logRest = Mathf.Log(Mathf.Max(0.05f, this.miniMapZoomRest));
            float logTop = Mathf.Log(Mathf.Max(0.05f, this.miniMapZoomTop));
            return Mathf.Exp(Mathf.Lerp(logRest, logTop, t));
        }

        // --- MiniMapSystem.distance / trackDistance --------------------------------------------

        private unsafe void ApplyMiniMapDistances(float distanceMetres, float trackMetres, bool force)
        {
            bool newWorld = this.miniMapDistanceEpoch != AuraMonoWorldEpoch;
            if (!force && !newWorld
                && Mathf.Abs(distanceMetres - this.miniMapWrittenDistance) <= this.miniMapWrittenDistance * 0.03f
                && Mathf.Abs(trackMetres - this.miniMapWrittenTrackDistance) <= this.miniMapWrittenTrackDistance * 0.03f)
            {
                return;
            }

            if (!this.EnsureAuraMonoApiReady() || !AuraMonoPinningAvailable
                || auraMonoFieldSetValue == null || auraMonoObjectGetClass == null)
            {
                return;
            }

            if (this.miniMapSystemClass == IntPtr.Zero)
            {
                this.miniMapSystemClass = this.FindAuraMonoClassByFullName(MiniMapSystemTypeName);
                if (this.miniMapSystemClass == IntPtr.Zero)
                {
                    this.MiniMapZoomSetStatus("MiniMapSystem class unresolved (game update?).", log: true);
                    return;
                }
            }

            IntPtr instance = this.TryGetAuraMonoDataModuleInstance(this.miniMapSystemClass);
            if (instance == IntPtr.Zero)
            {
                return; // not in a main-world level; retried on the next interval
            }

            uint pin = AuraMonoPinNew(instance);
            try
            {
                IntPtr klass = auraMonoObjectGetClass(instance);
                if (klass != this.miniMapFieldsClass)
                {
                    this.miniMapFieldsClass = klass;
                    this.miniMapDistanceField = this.FindAuraMonoFieldOnHierarchy(klass, "distance");
                    this.miniMapTrackDistanceField = this.FindAuraMonoFieldOnHierarchy(klass, "trackDistance");
                }

                if (this.miniMapDistanceField == IntPtr.Zero || this.miniMapTrackDistanceField == IntPtr.Zero)
                {
                    this.MiniMapZoomSetStatus("MiniMapSystem distance fields unresolved (game update?).", log: true);
                    return;
                }

                // Originals captured once per process, BEFORE the first write (the module may outlive
                // a world epoch, so a later read could return our own value). Every new instance
                // starts from the same field initializers, so restoring these stays correct.
                // Never write values we could not learn how to undo.
                if (!this.miniMapOrigCaptured)
                {
                    if (!this.TryGetMonoSingleMember(instance, "distance", out float dist)
                        || !this.TryGetMonoSingleMember(instance, "trackDistance", out float track)
                        || dist <= 0f || track <= 0f)
                    {
                        return;
                    }

                    this.miniMapOrigDistance = dist;
                    this.miniMapOrigTrackDistance = track;
                    this.miniMapOrigCaptured = true;
                    ModLogger.Msg("[MiniMapZoom] originals captured: distance=" + dist.ToString("F1")
                        + " trackDistance=" + track.ToString("F1"));
                }

                this.miniMapDistanceEpoch = AuraMonoWorldEpoch;

                // Value-type float fields: mono_field_set_value takes a pointer TO the value.
                float distance = distanceMetres;
                float trackDistance = trackMetres;
                auraMonoFieldSetValue(instance, this.miniMapDistanceField, (IntPtr)(&distance));
                auraMonoFieldSetValue(instance, this.miniMapTrackDistanceField, (IntPtr)(&trackDistance));
                this.miniMapWrittenDistance = distance;
                this.miniMapWrittenTrackDistance = trackDistance;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private void MiniMapZoomSetStatus(string status, bool log)
        {
            this.miniMapZoomStatus = status;
            if (log && !string.Equals(status, this.miniMapZoomLastLoggedStatus, StringComparison.Ordinal))
            {
                this.miniMapZoomLastLoggedStatus = status;
                ModLogger.Msg("[MiniMapZoom] " + status);
            }
        }

        // --- Config bridge (called from PopulateKeybindConfig / ApplyKeybindConfig) --------------

        private void SaveMiniMapZoomToConfig(KeybindConfigData data)
        {
            data.miniMapZoomEnabled = this.miniMapZoomEnabled;
            data.miniMapZoomRest = this.miniMapZoomRest;
            data.miniMapAutoZoomEnabled = this.miniMapAutoZoomEnabled;
            data.miniMapZoomTop = this.miniMapZoomTop;
            data.miniMapZoomReaction = this.miniMapZoomReaction;
            data.miniMapLookAheadEnabled = this.miniMapLookAheadEnabled;
            data.miniMapLookAheadAmount = this.miniMapLookAheadAmount;
        }

        private void LoadMiniMapZoomFromConfig(KeybindConfigData data)
        {
            this.miniMapZoomEnabled = data.miniMapZoomEnabled;
            this.miniMapZoomRest = data.miniMapZoomRest <= 0f
                ? MiniMapZoomDefaultRest
                : Mathf.Clamp(data.miniMapZoomRest, MiniMapZoomMin, MiniMapZoomMax);
            this.miniMapAutoZoomEnabled = data.miniMapAutoZoomEnabled;
            this.miniMapZoomTop = data.miniMapZoomTop <= 0f
                ? MiniMapZoomDefaultTop
                : Mathf.Clamp(data.miniMapZoomTop, MiniMapZoomTopMin, MiniMapZoomTopMax);
            this.miniMapZoomReaction = Mathf.Clamp(data.miniMapZoomReaction, 0, MiniMapZoomReactionNames.Length - 1);
            this.miniMapLookAheadEnabled = data.miniMapLookAheadEnabled;
            this.miniMapLookAheadAmount = data.miniMapLookAheadAmount <= 0f
                ? MiniMapLookAheadDefault
                : Mathf.Clamp(data.miniMapLookAheadAmount, MiniMapLookAheadMin, MiniMapLookAheadMax);
        }
    }
}
