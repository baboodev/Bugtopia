using System;
using UnityEngine;

namespace HeartopiaMod
{
    // "Compare Game Track" — a diagnostic mode for Walk to Nodes.
    //
    // It makes the game route to the SAME node the mod is walking to, and prints both to the log.
    // This is the only way to see where our A* and the native Track diverge: the mod computes its
    // path over a snapshot of the graph (TrackPathGraphFeature) while the game uses its own live
    // AStar with its own smoothing, and until now there was nothing to compare them against.
    //
    // How the game learns where to lead (TrackingSystem.cs:464):
    //     MapSpotProtocolManager.AddSpot(SpotEnum.Custom, useId, pos, SpotReason.TrackMap)
    //     TrackProtocolManager.StartLocalTrackMapSign((uint)useId)
    // The map spot is created by an EventCenter event — the server is not involved.
    //
    // ⚠️ StartLocalTrackMapSign internally calls StopAllLocalTrack(), i.e. it CLEARS the player's
    // own manual track. That is why this mode has its own switch and is off by default.
    //
    // The game's result is read from private fields of the live TrackingPathModule:
    //     _path         — corners after smoothing (our farmWalkCorners equivalent)
    //     realPathList  — the star positions spaced by perInstanceDis (what is on screen)
    public partial class HeartopiaComplete
    {
        private bool farmWalkTrackCompareEnabled;

        // Our own usageId for the map spot — anything that will not collide with the game's.
        private const int FarmWalkTrackSpotUseId = 990117;
        private const int SpotEnumCustom = 7;      // SpotEnum.Custom
        private const int SpotReasonTrackMap = 1;  // SpotReason.TrackMap

        // The game needs time to compute the path: TrackPathConditionConfig.tickFrame = 30 frames.
        private const float FarmWalkTrackCompareDelay = 1.5f;

        private float farmWalkTrackCompareAt = -1f;
        private Vector3 farmWalkTrackCompareTarget;
        private IntPtr farmWalkAddSpotMethod;
        private IntPtr farmWalkStartTrackMapSignMethod;
        private IntPtr farmWalkTrackModuleClass;
        private bool farmWalkTrackApiTried;

        private bool EnsureFarmWalkTrackApi()
        {
            if (this.farmWalkTrackApiTried)
            {
                return this.farmWalkAddSpotMethod != IntPtr.Zero && this.farmWalkStartTrackMapSignMethod != IntPtr.Zero;
            }

            this.farmWalkTrackApiTried = true;
            try
            {
                IntPtr spotClass = this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.MapSpot.MapSpotProtocolManager");
                if (spotClass != IntPtr.Zero)
                {
                    // AddSpot(SpotEnum, int, Vector3, SpotReason, GameSceneId) — 5 parameters; the
                    // last one has a default, but in Mono the signature is the full one.
                    this.farmWalkAddSpotMethod = this.FindAuraMonoMethodOnHierarchy(spotClass, "AddSpot", 5);
                }

                IntPtr trackClass = this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.Track.TrackProtocolManager");
                if (trackClass != IntPtr.Zero)
                {
                    this.farmWalkStartTrackMapSignMethod = this.FindAuraMonoMethodOnHierarchy(trackClass, "StartLocalTrackMapSign", 1);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[TrackCompare] resolve threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            bool ready = this.farmWalkAddSpotMethod != IntPtr.Zero && this.farmWalkStartTrackMapSignMethod != IntPtr.Zero;
            ModLogger.Msg("[TrackCompare] " + (ready ? "ready" : "UNAVAILABLE")
                + " (AddSpot=" + (this.farmWalkAddSpotMethod != IntPtr.Zero)
                + ", StartLocalTrackMapSign=" + (this.farmWalkStartTrackMapSignMethod != IntPtr.Zero) + ").");
            return ready;
        }

        // Ask the game to route to the same node. Called at the start of every walk.
        internal unsafe void RequestGameTrackForWalk(Vector3 node)
        {
            if (!this.farmWalkTrackCompareEnabled || !this.EnsureFarmWalkTrackApi())
            {
                return;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                int category = SpotEnumCustom;
                int useId = FarmWalkTrackSpotUseId;
                Vector3 pos = node;
                int reason = SpotReasonTrackMap;
                int sceneId = 0; // GameSceneId.StarTown — the signature's default value
                IntPtr* addArgs = stackalloc IntPtr[5];
                addArgs[0] = (IntPtr)(&category);
                addArgs[1] = (IntPtr)(&useId);
                addArgs[2] = (IntPtr)(&pos);
                addArgs[3] = (IntPtr)(&reason);
                addArgs[4] = (IntPtr)(&sceneId);
                auraMonoRuntimeInvoke(this.farmWalkAddSpotMethod, IntPtr.Zero, (IntPtr)addArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    ModLogger.Msg("[TrackCompare] AddSpot threw inside mono.");
                    return;
                }

                uint signId = FarmWalkTrackSpotUseId;
                IntPtr* trackArgs = stackalloc IntPtr[1];
                trackArgs[0] = (IntPtr)(&signId);
                auraMonoRuntimeInvoke(this.farmWalkStartTrackMapSignMethod, IntPtr.Zero, (IntPtr)trackArgs, ref exc);

                this.farmWalkTrackCompareTarget = node;
                this.farmWalkTrackCompareAt = Time.unscaledTime + FarmWalkTrackCompareDelay;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[TrackCompare] request threw: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Ticks from the farm. After the delay it reads the game's path and prints the comparison.
        internal void ProcessFarmWalkTrackCompare()
        {
            if (this.farmWalkTrackCompareAt < 0f || Time.unscaledTime < this.farmWalkTrackCompareAt)
            {
                return;
            }

            this.farmWalkTrackCompareAt = -1f;
            try
            {
                this.LogGameTrackPath();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[TrackCompare] read threw: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private unsafe void LogGameTrackPath()
        {
            if (auraMonoClassGetType == null || auraMonoTypeGetObject == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return;
            }

            if (this.farmWalkTrackModuleClass == IntPtr.Zero)
            {
                this.farmWalkTrackModuleClass = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.GameplaySystem.TrackingPoint", "TrackingPathModule", TrackPathModuleImageNames);
            }

            if (this.farmWalkTrackModuleClass == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackCompare] TrackingPathModule class not found.");
                return;
            }

            // ViewModule has no static Instance — only the safe route through Managers.GetModule(Type);
            // Type.GetType(string) crashes the runtime here.
            IntPtr monoType = auraMonoClassGetType(this.farmWalkTrackModuleClass);
            IntPtr typeObj = monoType != IntPtr.Zero ? auraMonoTypeGetObject(this.auraMonoRootDomain, monoType) : IntPtr.Zero;
            IntPtr managersClass = this.FindAuraMonoClassInImages("XDTGame.Framework", "Managers", TrackPathManagersImageNames);
            IntPtr getModule = managersClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(managersClass, "GetModule", 1)
                : IntPtr.Zero;
            if (typeObj == IntPtr.Zero || getModule == IntPtr.Zero)
            {
                return;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = typeObj;
            IntPtr moduleObj = auraMonoRuntimeInvoke(getModule, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || moduleObj == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackCompare] TrackingPathModule not active.");
                return;
            }

            this.TryGetMonoBoolMember(moduleObj, "isNavigating", out bool navigating);
            if (!this.TryGetMonoObjectMember(moduleObj, "_path", out IntPtr pathObj) || pathObj == IntPtr.Zero
                || !this.TryGetMonoIntMember(pathObj, "Count", out int gameCount))
            {
                ModLogger.Msg("[TrackCompare] _path unreadable (navigating=" + navigating + ").");
                return;
            }

            // The game route's length — the direct comparison against ours.
            float gameLength = 0f;
            Vector3 prev = Vector3.zero;
            bool havePrev = false;
            string firstCorner = "-";
            for (int i = 0; i < gameCount; i++)
            {
                if (!this.TryGetAuraMonoListVector3Item(pathObj, i, out Vector3 corner))
                {
                    continue;
                }

                if (i == 0)
                {
                    firstCorner = FormatNavMeshVector(corner);
                }

                if (havePrev)
                {
                    gameLength += Vector3.Distance(prev, corner);
                }

                prev = corner;
                havePrev = true;
            }

            // Measure our route FROM THE CURRENT POSITION over the corners that are left: the full
            // length includes the prefix already walked, and comparing that against the game's path
            // (which always starts at the player) was comparing two different things.
            bool haveSelf = this.TryGetNavMeshSelfPosition(out Vector3 selfPos, out _);
            float modLength = 0f;
            Vector3 modPrev = haveSelf ? selfPos : Vector3.zero;
            bool modHavePrev = haveSelf;
            int modRemaining = 0;
            for (int i = Mathf.Max(0, this.farmWalkCornerIndex); i < this.farmWalkCorners.Count; i++)
            {
                Vector3 corner = this.farmWalkCorners[i];
                if (modHavePrev)
                {
                    modLength += Vector3.Distance(modPrev, corner);
                }

                modPrev = corner;
                modHavePrev = true;
                modRemaining++;
            }

            float straight = haveSelf ? Vector3.Distance(selfPos, this.farmWalkTrackCompareTarget) : -1f;

            // gameCount is NOT a corner count: GetPath resamples the polyline with a Catmull-Rom
            // spline into a fixed PointCount/2 + 1 points (usually 51). Only the length is worth
            // comparing.
            ModLogger.Msg("[TrackCompare] target=" + FormatNavMeshVector(this.farmWalkTrackCompareTarget)
                + " straight=" + (straight >= 0f ? straight.ToString("F1") + "m" : "?")
                + " | GAME: " + gameLength.ToString("F1") + "m over " + gameCount + " spline pts, start=" + firstCorner
                + " (navigating=" + navigating + ")"
                + " | MOD: " + modLength.ToString("F1") + "m over " + modRemaining + " remaining corners"
                + " (route " + this.farmWalkCorners.Count + " total, at " + this.farmWalkCornerIndex + ")"
                + " | game/mod=" + (modLength > 0.01f ? (gameLength / modLength).ToString("F1") + "x" : "?"));
        }
    }
}
