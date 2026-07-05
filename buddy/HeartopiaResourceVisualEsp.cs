using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private sealed class ResourceVisualEspItem
        {
            public string Label;
            public string Badge;
            public Color Accent;
            public RadarMarkerMetadata Metadata;
            public Vector3 WorldPosition;
            public Vector3 ScreenPoint;
            public Vector2 OffscreenAnchor;
            public float Distance;
            public bool IsCooldown;
            public bool IsOffscreen;
        }

        private bool resourceVisualEspEnabled = true;
        private int resourceVisualEspStyle = 0; // 0 = Beacon, 1 = Card, 2 = Minimal
        private bool resourceVisualEspShowDistance = true;
        private bool resourceVisualEspShowConnector = true;
        private bool resourceVisualEspShowOffscreen = true;
        private bool resourceVisualEspShowGroundRing = false;
        private const string RadarGroundRingChildName = "GroundRing";
        private const int RadarGroundRingSegments = 48;
        private readonly Vector3[] radarGroundRingPositions = new Vector3[RadarGroundRingSegments + 1];
        private float resourceVisualEspScale = 1f;
        private float resourceVisualEspOpacity = 0.92f;
        private int resourceVisualEspMaxMarkers = 120;
        private readonly List<ResourceVisualEspItem> resourceVisualEspItems = new List<ResourceVisualEspItem>(64);
        private readonly List<Rect> resourceVisualEspPlacedRects = new List<Rect>(64);

        private void DrawResourceVisualEspOverlay()
        {
            if (this.radarDisplayMode != 0 || !this.resourceVisualEspEnabled || !this.isRadarActive || this.radarContainer == null)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            this.resourceVisualEspItems.Clear();
            Vector3 cameraPos = cam.transform.position;
            float maxDistance = Mathf.Max(25f, this.radarMaxDistance);

            for (int i = 0; i < this.radarContainer.transform.childCount; i++)
            {
                Transform child = this.radarContainer.transform.GetChild(i);
                if (child == null || child.gameObject == null)
                {
                    continue;
                }

                RadarMarkerMetadata metadata = this.GetMarkerMetadata(child.gameObject);
                if (metadata == null || string.IsNullOrWhiteSpace(metadata.CanonicalLabel))
                {
                    continue;
                }

                if (string.Equals(metadata.CanonicalLabel, "Player", StringComparison.Ordinal)
                    && this.TryGetRadarMarkerTrackedTarget(child.gameObject, out GameObject trackedPlayer)
                    && this.IsLocalPlayerSkeletonGameObject(trackedPlayer))
                {
                    continue;
                }

                if (!this.TryBuildResourceVisualEspItem(metadata, child.position, cameraPos, maxDistance, cam, out ResourceVisualEspItem item))
                {
                    continue;
                }

                this.resourceVisualEspItems.Add(item);
            }

            if (this.resourceVisualEspItems.Count <= 0)
            {
                return;
            }

            this.resourceVisualEspItems.Sort((a, b) =>
            {
                int aPriority = string.Equals(a.Label, "Bubble", StringComparison.Ordinal) ? 0 : 1;
                int bPriority = string.Equals(b.Label, "Bubble", StringComparison.Ordinal) ? 0 : 1;
                int priorityCompare = aPriority.CompareTo(bPriority);
                return priorityCompare != 0 ? priorityCompare : a.Distance.CompareTo(b.Distance);
            });
            int effectiveMarkerLimit = this.GetEffectiveResourceVisualEspMarkerLimit();
            if (this.resourceVisualEspItems.Count > effectiveMarkerLimit)
            {
                this.resourceVisualEspItems.RemoveRange(effectiveMarkerLimit, this.resourceVisualEspItems.Count - effectiveMarkerLimit);
            }

            Color previousColor = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;
            this.resourceVisualEspPlacedRects.Clear();

            for (int i = 0; i < this.resourceVisualEspItems.Count; i++)
            {
                ResourceVisualEspItem item = this.resourceVisualEspItems[i];
                if (item == null)
                {
                    continue;
                }

                if (item.IsOffscreen)
                {
                    if (this.resourceVisualEspShowOffscreen)
                    {
                        this.DrawResourceVisualEspEdgeChip(item);
                    }
                    continue;
                }

                Rect tagRect = this.GetResourceVisualEspTagRect(item);
                tagRect = this.ResolveResourceVisualEspTagOverlap(tagRect);

                if (this.resourceVisualEspShowConnector)
                {
                    Vector2 lineStart = new Vector2(item.ScreenPoint.x, Screen.height - item.ScreenPoint.y);
                    Vector2 lineEnd = this.GetResourceVisualEspConnectorEnd(tagRect);
                    float lineAlpha = this.resourceVisualEspStyle == 1 ? 0.3f : 0.42f;
                    float lineThickness = this.resourceVisualEspStyle == 1 ? 1.2f : 1.6f;
                    this.DrawResourceVisualEspLine(lineStart, lineEnd, new Color(item.Accent.r, item.Accent.g, item.Accent.b, lineAlpha * this.resourceVisualEspOpacity), lineThickness);
                    if (this.resourceVisualEspStyle != 1)
                    {
                        this.DrawResourceVisualEspDot(lineStart, 5f * this.resourceVisualEspScale, item.Accent, 0.95f);
                    }
                }

                if (this.resourceVisualEspStyle == 2)
                {
                    this.DrawResourceVisualEspMinimalTag(tagRect, item);
                }
                else if (this.resourceVisualEspStyle == 1)
                {
                    this.DrawResourceVisualEspCardTag(tagRect, item);
                }
                else
                {
                    this.DrawResourceVisualEspBeaconTag(tagRect, item);
                }

                this.resourceVisualEspPlacedRects.Add(tagRect);
            }

            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private void UpdateRadarGroundRings()
        {
            if (!this.isRadarActive || this.radarContainer == null)
            {
                return;
            }

            if (this.radarDisplayMode != 0 || !this.resourceVisualEspEnabled || !this.resourceVisualEspShowGroundRing)
            {
                for (int i = 0; i < this.radarContainer.transform.childCount; i++)
                {
                    Transform child = this.radarContainer.transform.GetChild(i);
                    if (child != null)
                    {
                        this.RemoveRadarGroundRing(child.gameObject);
                    }
                }

                return;
            }

            this.EnsureRadarMaterials();
            if (this.radarLineMaterial == null)
            {
                return;
            }

            for (int i = 0; i < this.radarContainer.transform.childCount; i++)
            {
                Transform child = this.radarContainer.transform.GetChild(i);
                if (child == null || child.gameObject == null)
                {
                    continue;
                }

                RadarMarkerMetadata metadata = this.GetMarkerMetadata(child.gameObject);
                if (metadata == null || string.IsNullOrWhiteSpace(metadata.CanonicalLabel) || !this.IsResourceVisualEspLabel(metadata.CanonicalLabel))
                {
                    this.RemoveRadarGroundRing(child.gameObject);
                    continue;
                }

                if (string.Equals(metadata.CanonicalLabel, "Player", StringComparison.Ordinal)
                    && this.TryGetRadarMarkerTrackedTarget(child.gameObject, out GameObject trackedPlayer)
                    && this.IsLocalPlayerSkeletonGameObject(trackedPlayer))
                {
                    this.RemoveRadarGroundRing(child.gameObject);
                    continue;
                }

                this.EnsureRadarGroundRing(child.gameObject, metadata.CanonicalLabel, metadata.IsCooldown);
            }
        }

        private float GetRadarGroundRingRadius(string label)
        {
            switch (label)
            {
                case "Tree":
                case "Rare Tree":
                case "Apple Tree":
                case "Mandarin Tree":
                    return 0.725f;
                case "Meteor":
                    return 0.8f;
                default:
                    return 0.425f;
            }
        }

        private bool ShouldIgnoreRadarGroundRingHit(RaycastHit hit, GameObject trackedTarget)
        {
            if (hit.collider == null)
            {
                return true;
            }

            if (trackedTarget != null)
            {
                Transform hitTransform = hit.collider.transform;
                Transform trackedTransform = trackedTarget.transform;
                if (hitTransform != null && trackedTransform != null
                    && (hitTransform == trackedTransform || hitTransform.IsChildOf(trackedTransform)))
                {
                    return true;
                }
            }

            string colliderName = hit.collider.gameObject.name;
            return !string.IsNullOrEmpty(colliderName) && colliderName.Contains("p_player_skeleton");
        }

        private Vector3 GetRadarGroundRingCenter(Vector3 anchorPosition, string label, GameObject trackedTarget)
        {
            bool isCharacter = string.Equals(label, "Player", StringComparison.Ordinal)
                || string.Equals(label, "Morph", StringComparison.Ordinal);
            if (isCharacter)
            {
                Vector3 origin = new Vector3(anchorPosition.x, anchorPosition.y + 2.5f, anchorPosition.z);
                RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 20f);
                if (hits != null && hits.Length > 1)
                {
                    System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                }

                if (hits != null)
                {
                    for (int i = 0; i < hits.Length; i++)
                    {
                        if (this.ShouldIgnoreRadarGroundRingHit(hits[i], trackedTarget))
                        {
                            continue;
                        }

                        return hits[i].point;
                    }
                }

                return new Vector3(anchorPosition.x, anchorPosition.y, anchorPosition.z);
            }

            float castStartY = anchorPosition.y;
            Camera cam = Camera.main;
            if (cam != null)
            {
                castStartY = Mathf.Max(anchorPosition.y, cam.transform.position.y) + 2f;
            }
            else
            {
                castStartY += 2f;
            }

            Vector3 resourceOrigin = new Vector3(anchorPosition.x, castStartY, anchorPosition.z);
            RaycastHit hit;
            if (Physics.Raycast(resourceOrigin, Vector3.down, out hit, 250f))
            {
                return hit.point;
            }

            return new Vector3(anchorPosition.x, anchorPosition.y, anchorPosition.z);
        }

        private void BuildRadarGroundRingPositions(Vector3 anchorPosition, float radius, string label, GameObject trackedTarget)
        {
            Vector3 center = this.GetRadarGroundRingCenter(anchorPosition, label, trackedTarget);
            for (int segment = 0; segment <= RadarGroundRingSegments; segment++)
            {
                float angle = (float)segment / RadarGroundRingSegments * Mathf.PI * 2f;
                this.radarGroundRingPositions[segment] = center + new Vector3(Mathf.Cos(angle) * radius, 0.02f, Mathf.Sin(angle) * radius);
            }
        }

        private void EnsureRadarGroundRing(GameObject marker, string label, bool isCooldown)
        {
            if (marker == null)
            {
                return;
            }

            Transform ringTransform = marker.transform.Find(RadarGroundRingChildName);
            LineRenderer circle;
            if (ringTransform == null)
            {
                GameObject ringObject = new GameObject(RadarGroundRingChildName);
                ringObject.transform.SetParent(marker.transform, false);
                circle = ringObject.AddComponent<LineRenderer>();
                circle.useWorldSpace = true;
                circle.loop = true;
                circle.material = this.radarLineMaterial;
                circle.startWidth = 0.027f;
                circle.endWidth = 0.027f;
                circle.positionCount = RadarGroundRingSegments + 1;
            }
            else
            {
                circle = ringTransform.GetComponent<LineRenderer>();
                if (circle == null)
                {
                    return;
                }
            }

            Color accent = this.GetResourceVisualEspColor(label);
            float alpha = this.resourceVisualEspOpacity * (isCooldown ? 0.45f : 0.85f);
            accent.a = alpha;
            circle.startColor = accent;
            circle.endColor = accent;
            if (circle.material != this.radarLineMaterial)
            {
                circle.material = this.radarLineMaterial;
            }

            float radius = this.GetRadarGroundRingRadius(label);
            Vector3 anchorPosition = marker.transform.position;
            GameObject trackedTarget = null;
            if (this.TryGetRadarMarkerTrackedTarget(marker, out GameObject resolvedTarget) && resolvedTarget != null)
            {
                trackedTarget = resolvedTarget;
                anchorPosition = trackedTarget.transform.position;
            }

            this.BuildRadarGroundRingPositions(anchorPosition, radius, label, trackedTarget);
            for (int segment = 0; segment <= RadarGroundRingSegments; segment++)
            {
                circle.SetPosition(segment, this.radarGroundRingPositions[segment]);
            }
        }

        private void RemoveRadarGroundRing(GameObject marker)
        {
            if (marker == null)
            {
                return;
            }

            Transform ringTransform = marker.transform.Find(RadarGroundRingChildName);
            if (ringTransform != null)
            {
                UnityEngine.Object.Destroy(ringTransform.gameObject);
            }
        }

        private bool TryBuildResourceVisualEspItem(RadarMarkerMetadata metadata, Vector3 worldPosition, Vector3 cameraPosition, float maxDistance, Camera cam, out ResourceVisualEspItem item)
        {
            item = null;
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.CanonicalLabel))
            {
                return false;
            }

            string label = metadata.CanonicalLabel.Trim();
            if (!this.IsResourceVisualEspLabel(label))
            {
                return false;
            }

            float distance = Vector3.Distance(cameraPosition, worldPosition);
            float itemMaxDistance = string.Equals(label, "Bubble", StringComparison.Ordinal)
                ? Mathf.Max(BubbleRadarMaxDistance, maxDistance)
                : maxDistance;
            if (distance > itemMaxDistance)
            {
                return false;
            }

            Vector3 worldAnchor = worldPosition + new Vector3(0f, this.GetResourceVisualEspHeightOffset(label), 0f);
            Vector3 screenPoint = cam.WorldToScreenPoint(worldAnchor);
            Vector3 viewportPoint = cam.WorldToViewportPoint(worldAnchor);
            bool offscreen = screenPoint.z <= 0f
                || screenPoint.x < 10f
                || screenPoint.x > Screen.width - 10f
                || screenPoint.y < 10f
                || screenPoint.y > Screen.height - 10f;

            item = new ResourceVisualEspItem
            {
                Label = label,
                Badge = this.GetResourceVisualEspBadge(label),
                Accent = this.GetResourceVisualEspColor(label),
                Metadata = metadata,
                WorldPosition = worldAnchor,
                ScreenPoint = screenPoint,
                OffscreenAnchor = this.GetResourceVisualEspOffscreenAnchor(viewportPoint),
                Distance = distance,
                IsCooldown = metadata.IsCooldown,
                IsOffscreen = offscreen
            };
            return true;
        }

        private bool IsResourceVisualEspLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

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
                case "Stone":
                case "Ore":
                case "Tree":
                case "Rare Tree":
                case "Apple Tree":
                case "Mandarin Tree":
                case "Bubble":
                case "Bird":
                case "Player":
                case "Morph":
                case "Insect":
                case "Meteor":
                case "Fish Shadow":
                    return true;
            }

            return false;
        }

        private float GetResourceVisualEspHeightOffset(string label)
        {
            switch (label)
            {
                case "Tree":
                case "Rare Tree":
                case "Apple Tree":
                case "Mandarin Tree":
                    return 2.8f;
                case "Bird":
                    return 1.9f;
                case "Player":
                    return 2f;
                case "Morph":
                    return 1.35f;
                case "Bubble":
                    return 1.75f;
                case "Stone":
                case "Ore":
                    return 1.5f;
                default:
                    return 1.15f;
            }
        }

        private string GetResourceVisualEspBadge(string label)
        {
            switch (label)
            {
                case "Blueberry": return "BB";
                case "Raspberry": return "RB";
                case "Stone": return "ST";
                case "Ore": return "OR";
                case "Rare Tree": return "RT";
                case "Apple Tree": return "AP";
                case "Mandarin Tree": return "MD";
                case "Tree": return "TR";
                case "Fiddlehead": return "FD";
                case "Tall Mustard": return "TM";
                case "Mustard Greens": return "MG";
                case "Burdock": return "BD";
                case "Oyster": return "OY";
                case "Button": return "BT";
                case "Penny Bun": return "PB";
                case "Shiitake": return "SH";
                case "Truffle": return "TF";
                case "Bubble": return "BP";
                case "Bird": return "BR";
                case "Player": return "PL";
                case "Morph": return "MF";
                case "Insect": return "IN";
                case "Meteor": return "MT";
                case "Fish Shadow": return "FS";
                default: return "RS";
            }
        }

        private Color GetResourceVisualEspColor(string label)
        {
            switch (label)
            {
                case "Blueberry": return new Color(0.42f, 0.72f, 1f);
                case "Raspberry": return new Color(1f, 0.45f, 0.58f);
                case "Stone": return new Color(0.72f, 0.76f, 0.82f);
                case "Ore": return new Color(0.95f, 0.72f, 0.44f);
                case "Tree": return new Color(0.58f, 0.92f, 0.78f);
                case "Rare Tree": return new Color(1f, 0.83f, 0.38f);
                case "Apple Tree": return new Color(1f, 0.56f, 0.48f);
                case "Mandarin Tree": return new Color(1f, 0.72f, 0.42f);
                case "Fiddlehead": return new Color(0.64f, 0.96f, 0.62f);
                case "Tall Mustard": return new Color(0.82f, 0.98f, 0.52f);
                case "Mustard Greens": return new Color(0.62f, 0.95f, 0.58f);
                case "Burdock": return new Color(0.9f, 0.76f, 0.56f);
                case "Oyster": return new Color(0.58f, 0.92f, 0.95f);
                case "Button": return new Color(0.63f, 0.94f, 0.68f);
                case "Penny Bun": return new Color(0.86f, 0.72f, 1f);
                case "Shiitake": return new Color(1f, 0.72f, 0.56f);
                case "Truffle": return new Color(0.98f, 0.93f, 0.58f);
                case "Bubble": return new Color(0.9f, 0.56f, 1f);
                case "Bird": return new Color(0.98f, 0.92f, 0.52f);
                case "Player": return new Color(0.45f, 0.88f, 1f);
                case "Morph": return new Color(1f, 0.72f, 0.38f);
                case "Insect": return new Color(1f, 0.78f, 0.42f);
                case "Meteor": return new Color(1f, 0.62f, 0.32f);
                case "Fish Shadow": return new Color(0.42f, 0.78f, 1f);
                default: return new Color(0.82f, 0.9f, 1f);
            }
        }

        private bool ShouldUseModernRadarVisualEsp()
        {
            return true;
        }

        private int GetEffectiveResourceVisualEspMarkerLimit()
        {
            // The slider is authoritative. The old radar-distance floors (500m→60, 700m→90,
            // 900m→120) silently overrode small explicit values — a user setting 20 still saw
            // 120 tags on a long-range radar.
            return Mathf.Clamp(this.resourceVisualEspMaxMarkers, 20, 200);
        }

        private GameObject CreateModernRadarMarkerAnchor(Vector3 pos, string canonicalLabel, string icon, string specificIconKey, bool isCooldown, GameObject targetObject)
        {
            GameObject marker = new GameObject("ItemMarker");
            marker.transform.position = pos;
            marker.transform.SetParent(this.radarContainer.transform);
            if (targetObject != null)
            {
                marker.name = "TrackedMarker_" + targetObject.GetInstanceID().ToString();
                this.markerToTarget[marker] = targetObject;
            }

            RadarMarkerMetadata metadata = new RadarMarkerMetadata
            {
                CanonicalLabel = canonicalLabel,
                Icon = icon,
                SpecificIconKey = specificIconKey,
                IsCooldown = isCooldown
            };
            this.SetMarkerMetadata(marker, metadata);
            return marker;
        }

        private Rect GetResourceVisualEspTagRect(ResourceVisualEspItem item)
        {
            float scale = Mathf.Clamp(this.resourceVisualEspScale, 0.8f, 1.5f);
            float width = this.resourceVisualEspStyle == 2 ? 86f * scale : (this.resourceVisualEspStyle == 1 ? 150f * scale : 154f * scale);
            float height = this.resourceVisualEspStyle == 2 ? 28f * scale : (this.resourceVisualEspStyle == 1 ? 28f * scale : 44f * scale);
            float screenX = item.ScreenPoint.x - width * 0.5f;
            float screenY = Screen.height - item.ScreenPoint.y - height - 20f * scale;
            return new Rect(screenX, screenY, width, height);
        }

        private Vector2 GetResourceVisualEspConnectorEnd(Rect rect)
        {
            if (this.resourceVisualEspStyle == 1)
            {
                return new Vector2(rect.x + 18f * this.resourceVisualEspScale, rect.yMax - 4f * this.resourceVisualEspScale);
            }

            return new Vector2(rect.center.x, rect.yMax - 2f);
        }

        private Rect ResolveResourceVisualEspTagOverlap(Rect rect)
        {
            Rect adjusted = rect;
            for (int pass = 0; pass < 8; pass++)
            {
                bool intersects = false;
                for (int i = 0; i < this.resourceVisualEspPlacedRects.Count; i++)
                {
                    Rect existing = this.resourceVisualEspPlacedRects[i];
                    if (existing.Overlaps(adjusted))
                    {
                        adjusted.y = existing.yMax + 6f * this.resourceVisualEspScale;
                        intersects = true;
                    }
                }

                if (!intersects)
                {
                    break;
                }
            }

            adjusted.x = Mathf.Clamp(adjusted.x, 6f, Screen.width - adjusted.width - 6f);
            adjusted.y = Mathf.Clamp(adjusted.y, 6f, Screen.height - adjusted.height - 6f);
            return adjusted;
        }

        private Vector2 GetResourceVisualEspOffscreenAnchor(Vector3 viewportPoint)
        {
            Vector2 centered = new Vector2((viewportPoint.x - 0.5f) * 2f, (viewportPoint.y - 0.5f) * 2f);
            if (viewportPoint.z < 0f)
            {
                centered = -centered;
            }

            if (centered.sqrMagnitude < 0.0001f)
            {
                centered = Vector2.up;
            }

            float scale = 1f / Mathf.Max(Mathf.Abs(centered.x), Mathf.Abs(centered.y));
            centered *= scale;

            float marginX = 68f;
            float marginY = 34f;
            float screenX = Screen.width * 0.5f + centered.x * (Screen.width * 0.5f - marginX);
            float screenY = Screen.height * 0.5f - centered.y * (Screen.height * 0.5f - marginY);
            return new Vector2(
                Mathf.Clamp(screenX, marginX, Screen.width - marginX),
                Mathf.Clamp(screenY, marginY, Screen.height - marginY));
        }

        private void DrawResourceVisualEspBeaconTag(Rect rect, ResourceVisualEspItem item)
        {
            float alpha = this.resourceVisualEspOpacity * (item.IsCooldown ? 0.5f : 1f);
            Color bg = new Color(0.05f, 0.07f, 0.1f, 0.82f * alpha);
            Color shadow = new Color(0f, 0f, 0f, 0.22f * alpha);
            Color accent = new Color(item.Accent.r, item.Accent.g, item.Accent.b, alpha);

            GUI.color = shadow;
            GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 3f, rect.width, rect.height), Texture2D.whiteTexture);
            GUI.color = bg;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = accent;
            GUI.DrawTexture(new Rect(rect.x, rect.y, 4f * this.resourceVisualEspScale, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x + 9f * this.resourceVisualEspScale, rect.y + 7f * this.resourceVisualEspScale, 32f * this.resourceVisualEspScale, 28f * this.resourceVisualEspScale), Texture2D.whiteTexture);
            Rect iconRect = new Rect(rect.x + 10f * this.resourceVisualEspScale, rect.y + 8f * this.resourceVisualEspScale, 30f * this.resourceVisualEspScale, 26f * this.resourceVisualEspScale);
            Texture2D iconTexture = this.GetResourceVisualEspBeaconIconTexture(item);
            if (iconTexture != null)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(iconRect, iconTexture, ScaleMode.ScaleToFit, true);
            }
            else
            {
                GUIStyle badgeStyle = new GUIStyle(GUI.skin.label);
                badgeStyle.alignment = TextAnchor.MiddleCenter;
                badgeStyle.fontSize = Mathf.RoundToInt(11f * this.resourceVisualEspScale);
                badgeStyle.fontStyle = FontStyle.Bold;
                badgeStyle.normal.textColor = new Color(0.04f, 0.06f, 0.08f, alpha);
                GUI.Label(new Rect(rect.x + 9f * this.resourceVisualEspScale, rect.y + 7f * this.resourceVisualEspScale, 32f * this.resourceVisualEspScale, 28f * this.resourceVisualEspScale), item.Badge, badgeStyle);
            }

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.alignment = TextAnchor.UpperLeft;
            titleStyle.fontSize = Mathf.RoundToInt(12f * this.resourceVisualEspScale);
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = new Color(0.95f, 0.98f, 1f, alpha);
            GUI.Label(new Rect(rect.x + 50f * this.resourceVisualEspScale, rect.y + 7f * this.resourceVisualEspScale, rect.width - 56f * this.resourceVisualEspScale, 18f * this.resourceVisualEspScale), item.Label, titleStyle);

            GUIStyle subStyle = new GUIStyle(GUI.skin.label);
            subStyle.alignment = TextAnchor.UpperLeft;
            subStyle.fontSize = Mathf.RoundToInt(10f * this.resourceVisualEspScale);
            subStyle.normal.textColor = new Color(0.76f, 0.84f, 0.92f, alpha * 0.95f);
            string subLine = item.IsCooldown ? "cooldown" : string.Empty;
            if (this.resourceVisualEspShowDistance)
            {
                subLine = string.IsNullOrEmpty(subLine)
                    ? item.Distance.ToString("F0") + "m"
                    : subLine + "  " + item.Distance.ToString("F0") + "m";
            }
            GUI.Label(new Rect(rect.x + 50f * this.resourceVisualEspScale, rect.y + 22f * this.resourceVisualEspScale, rect.width - 56f * this.resourceVisualEspScale, 16f * this.resourceVisualEspScale), subLine, subStyle);
        }

        private Texture2D GetResourceVisualEspBeaconIconTexture(ResourceVisualEspItem item)
        {
            if (item == null || item.Metadata == null)
            {
                return null;
            }

            RadarMarkerMetadata metadata = item.Metadata;
            if (metadata.ResourceVisualEspIconTexture != null)
            {
                return metadata.ResourceVisualEspIconTexture;
            }

            if (Time.unscaledTime < metadata.ResourceVisualEspNextIconResolveAt)
            {
                return null;
            }

            if (this.TryGetRadarIconTexture(item.Label, metadata.SpecificIconKey, out Texture2D iconTexture) && iconTexture != null)
            {
                metadata.ResourceVisualEspIconTexture = iconTexture;
                metadata.ResourceVisualEspNextIconResolveAt = 0f;
                return iconTexture;
            }

            metadata.ResourceVisualEspNextIconResolveAt = Time.unscaledTime + 5f;
            return null;
        }

        private void DrawResourceVisualEspCardTag(Rect rect, ResourceVisualEspItem item)
        {
            float alpha = this.resourceVisualEspOpacity * (item.IsCooldown ? 0.48f : 1f);
            Color outline = new Color(
                Mathf.Lerp(item.Accent.r, 1f, 0.2f),
                Mathf.Lerp(item.Accent.g, 1f, 0.2f),
                Mathf.Lerp(item.Accent.b, 1f, 0.2f),
                alpha);
            Color fill = new Color(0.02f, 0.028f, 0.04f, alpha);
            float scale = Mathf.Clamp(this.resourceVisualEspScale, 0.8f, 1.5f);

            GUI.color = new Color(0f, 0f, 0f, 0.34f * alpha);
            GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 3f, rect.width, rect.height), Texture2D.whiteTexture);
            GUI.color = new Color(outline.r, outline.g, outline.b, 0.24f * alpha);
            GUI.DrawTexture(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(outline.r, outline.g, outline.b, 0.82f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), Texture2D.whiteTexture);

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.alignment = TextAnchor.MiddleLeft;
            titleStyle.fontSize = Mathf.RoundToInt(10.5f * scale);
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = new Color(0.98f, 0.99f, 1f, alpha);
            titleStyle.richText = false;

            GUIStyle metaStyle = new GUIStyle(GUI.skin.label);
            metaStyle.alignment = TextAnchor.MiddleRight;
            metaStyle.fontSize = Mathf.RoundToInt(9.5f * scale);
            metaStyle.fontStyle = FontStyle.Bold;
            metaStyle.normal.textColor = new Color(0.92f, 0.95f, 0.99f, alpha);

            float contentX = rect.x + 10f * scale;
            float contentY = rect.y + 3f * scale;
            float contentWidth = rect.width - 20f * scale;
            float contentHeight = rect.height - 6f * scale;

            string meta = item.IsCooldown ? "cooldown" : string.Empty;
            if (this.resourceVisualEspShowDistance)
            {
                meta = string.IsNullOrEmpty(meta)
                    ? item.Distance.ToString("F0") + "m"
                    : meta + "  " + item.Distance.ToString("F0") + "m";
            }

            float metaWidth = this.resourceVisualEspShowDistance || item.IsCooldown ? 42f * scale : 0f;
            float gap = metaWidth > 0f ? 8f * scale : 0f;
            GUI.Label(new Rect(contentX, contentY, Mathf.Max(30f * scale, contentWidth - metaWidth - gap), contentHeight), item.Label, titleStyle);
            if (metaWidth > 0f)
            {
                GUI.Label(new Rect(rect.xMax - 10f * scale - metaWidth, contentY, metaWidth, contentHeight), meta, metaStyle);
            }
        }

        private void DrawResourceVisualEspMinimalTag(Rect rect, ResourceVisualEspItem item)
        {
            float alpha = this.resourceVisualEspOpacity * (item.IsCooldown ? 0.45f : 1f);
            Color accent = new Color(item.Accent.r, item.Accent.g, item.Accent.b, alpha);
            GUI.color = new Color(0.04f, 0.06f, 0.08f, 0.68f * alpha);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = accent;
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), Texture2D.whiteTexture);

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.alignment = TextAnchor.MiddleCenter;
            style.fontSize = Mathf.RoundToInt(10f * this.resourceVisualEspScale);
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = new Color(0.95f, 0.98f, 1f, alpha);
            string text = item.Badge;
            if (this.resourceVisualEspShowDistance)
            {
                text += "  " + item.Distance.ToString("F0") + "m";
            }
            GUI.Label(rect, text, style);
        }

        private void DrawResourceVisualEspEdgeChip(ResourceVisualEspItem item)
        {
            float scale = Mathf.Clamp(this.resourceVisualEspScale, 0.8f, 1.5f);
            float width = 84f * scale;
            float height = 22f * scale;
            Rect rect = new Rect(item.OffscreenAnchor.x - width * 0.5f, item.OffscreenAnchor.y - height * 0.5f, width, height);
            float alpha = this.resourceVisualEspOpacity * (item.IsCooldown ? 0.42f : 0.9f);
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.75f * alpha);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(item.Accent.r, item.Accent.g, item.Accent.b, alpha);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3f * scale, rect.height), Texture2D.whiteTexture);

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.alignment = TextAnchor.MiddleCenter;
            style.fontSize = Mathf.RoundToInt(9f * this.resourceVisualEspScale);
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = new Color(0.95f, 0.98f, 1f, alpha);
            string text = item.Label;
            if (this.resourceVisualEspShowDistance)
            {
                text += " " + item.Distance.ToString("F0") + "m";
            }
            GUI.Label(rect, text, style);
        }

        private void DrawResourceVisualEspDot(Vector2 center, float size, Color color, float alpha)
        {
            Color previous = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, alpha * this.resourceVisualEspOpacity);
            GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void DrawResourceVisualEspLine(Vector2 from, Vector2 to, Color color, float thickness)
        {
            Matrix4x4 previous = GUI.matrix;
            Color previousColor = GUI.color;
            float angle = Vector3.Angle(to - from, Vector2.right);
            if (from.y > to.y)
            {
                angle = -angle;
            }

            float length = (to - from).magnitude;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.DrawTexture(new Rect(from.x, from.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
            GUI.matrix = previous;
            GUI.color = previousColor;
        }
    }
}
