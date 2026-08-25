using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Drawing the mod's route over the world.
    //
    // The game draws its own Track as a chain of little stars; our polyline over the route corners
    // lies beside it — that is the path comparison, right there in the frame and not in a log.
    //
    // ⚠️ THE RADAR FILTER IS GONE. It left only the current target on the radar, and that broke the
    // farm: FindClosestAvailableNode enumerates PRECISELY the radar markers, so the filter took
    // every candidate but one away from the scan — the mod stopped knowing where to go next and
    // drove off to change areas. Do not bring it back. If "show only the target" is ever wanted, it
    // has to be a separate visual layer that does not touch the markers the scan reads.
    public partial class HeartopiaComplete
    {
        // The line object's name. RunRadar wipes every child of the container except the markers it
        // tracks — the line has to survive that sweep or it will blink once every 2 seconds.
        internal const string FarmWalkRouteLineName = "FarmWalkRouteLine";

        private const float FarmWalkRouteLineWidth = 0.14f;
        private const float FarmWalkRouteLineLift = 0.4f;

        private GameObject farmWalkRouteLineObject;
        private LineRenderer farmWalkRouteLine;
        private readonly List<Vector3> farmWalkRoutePoints = new List<Vector3>();

        // The line hangs off the Compare Game Track switch rather than Walk to Nodes itself: it
        // exists precisely to be checked against the game's stars, so one switch turns both
        // diagnostic traces off. With the switch off the run is silent.
        private bool IsFarmWalkRadarFocusActive(out Vector3 target)
        {
            target = Vector3.zero;
            if (!this.farmWalkToNodeEnabled || !this.farmWalkTrackCompareEnabled || !this.farmWalkActive)
            {
                return false;
            }

            target = this.farmWalkTarget;
            return true;
        }

        // Ticks every frame from UpdateMarkers. Keeps the polyline running from the player through
        // the corners that are left.
        internal void SyncFarmWalkRouteLine(Material lineMaterial)
        {
            if (!this.IsFarmWalkRadarFocusActive(out Vector3 target) || this.radarContainer == null)
            {
                this.ClearFarmWalkRouteLine();
                return;
            }

            if (!this.TryGetNavMeshSelfPosition(out Vector3 selfPos, out _))
            {
                this.ClearFarmWalkRouteLine();
                return;
            }

            this.farmWalkRoutePoints.Clear();
            this.farmWalkRoutePoints.Add(selfPos + new Vector3(0f, FarmWalkRouteLineLift, 0f));
            for (int i = this.farmWalkCornerIndex; i < this.farmWalkCorners.Count; i++)
            {
                this.farmWalkRoutePoints.Add(this.farmWalkCorners[i] + new Vector3(0f, FarmWalkRouteLineLift, 0f));
            }

            // Append the target only when the last corner is not already it.
            Vector3 lifted = target + new Vector3(0f, FarmWalkRouteLineLift, 0f);
            if ((this.farmWalkRoutePoints[this.farmWalkRoutePoints.Count - 1] - lifted).sqrMagnitude > 0.01f)
            {
                this.farmWalkRoutePoints.Add(lifted);
            }

            if (this.farmWalkRoutePoints.Count < 2)
            {
                this.ClearFarmWalkRouteLine();
                return;
            }

            if (this.farmWalkRouteLineObject == null)
            {
                this.farmWalkRouteLineObject = new GameObject(FarmWalkRouteLineName);
                this.farmWalkRouteLineObject.transform.SetParent(this.radarContainer.transform);
                this.farmWalkRouteLineObject.transform.position = Vector3.zero;
                this.farmWalkRouteLine = this.farmWalkRouteLineObject.AddComponent<LineRenderer>();
                this.farmWalkRouteLine.useWorldSpace = true;
                this.farmWalkRouteLine.startWidth = (this.farmWalkRouteLine.endWidth = FarmWalkRouteLineWidth);
            }

            if (this.farmWalkRouteLine == null)
            {
                return;
            }

            if (lineMaterial != null && this.farmWalkRouteLine.material != lineMaterial)
            {
                this.farmWalkRouteLine.material = lineMaterial;
            }

            // Bright green: the game's stars are golden, so the two cannot be confused.
            Color routeColor = new Color(0.25f, 1f, 0.4f, 0.95f);
            this.farmWalkRouteLine.startColor = (this.farmWalkRouteLine.endColor = routeColor);
            this.farmWalkRouteLine.positionCount = this.farmWalkRoutePoints.Count;
            for (int i = 0; i < this.farmWalkRoutePoints.Count; i++)
            {
                this.farmWalkRouteLine.SetPosition(i, this.farmWalkRoutePoints[i]);
            }
        }

        internal void ClearFarmWalkRouteLine()
        {
            if (this.farmWalkRouteLineObject != null)
            {
                Object.Destroy(this.farmWalkRouteLineObject);
            }

            this.farmWalkRouteLineObject = null;
            this.farmWalkRouteLine = null;
        }
    }
}
