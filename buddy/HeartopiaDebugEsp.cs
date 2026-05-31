using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private sealed class VisualDebugEspEntry
        {
            public string Key;
            public string Group;
            public string Label;
            public Vector3 Position;
            public GameObject Target;
            public Color Color;
            public float ExpireAt;
            public bool ShowDistance;
        }

        private readonly Dictionary<string, VisualDebugEspEntry> visualDebugEspEntries = new Dictionary<string, VisualDebugEspEntry>(StringComparer.Ordinal);
        private bool visualDebugEspOwnerChecked = false;
        private bool visualDebugEspOwnerAllowed = false;

        public static bool IsVisualDebugEspAvailable()
        {
            HeartopiaComplete instance = HeartopiaComplete.Instance;
            return instance != null && instance.IsVisualDebugEspOwnerAllowed();
        }

        public static void DebugEspUpsert(string key, Vector3 position, string label = null, string group = "global", float durationSeconds = 0f)
        {
            DebugEspUpsert(key, position, label, Color.cyan, group, durationSeconds, true);
        }

        public static void DebugEspUpsert(string key, Vector3 position, string label, Color color, string group = "global", float durationSeconds = 0f, bool showDistance = true)
        {
            HeartopiaComplete instance = HeartopiaComplete.Instance;
            if (instance == null || !instance.IsVisualDebugEspOwnerAllowed() || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            instance.UpsertVisualDebugEspEntry(key, position, null, label, color, group, durationSeconds, showDistance);
        }

        public static void DebugEspTrack(string key, GameObject target, string label = null, string group = "global", float durationSeconds = 0f)
        {
            DebugEspTrack(key, target, label, Color.cyan, group, durationSeconds, true);
        }

        public static void DebugEspTrack(string key, GameObject target, string label, Color color, string group = "global", float durationSeconds = 0f, bool showDistance = true)
        {
            HeartopiaComplete instance = HeartopiaComplete.Instance;
            if (instance == null || !instance.IsVisualDebugEspOwnerAllowed() || string.IsNullOrWhiteSpace(key) || target == null)
            {
                return;
            }

            instance.UpsertVisualDebugEspEntry(key, target.transform.position, target, label, color, group, durationSeconds, showDistance);
        }

        public static void DebugEspRemove(string key)
        {
            HeartopiaComplete instance = HeartopiaComplete.Instance;
            if (instance == null || !instance.IsVisualDebugEspOwnerAllowed() || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            instance.RemoveVisualDebugEspEntry(key);
        }

        public static void DebugEspClearGroup(string group)
        {
            HeartopiaComplete instance = HeartopiaComplete.Instance;
            if (instance == null || !instance.IsVisualDebugEspOwnerAllowed() || string.IsNullOrWhiteSpace(group))
            {
                return;
            }

            instance.ClearVisualDebugEspGroup(group);
        }

        public static void DebugEspClearAll()
        {
            HeartopiaComplete instance = HeartopiaComplete.Instance;
            if (instance == null || !instance.IsVisualDebugEspOwnerAllowed())
            {
                return;
            }

            instance.ClearAllVisualDebugEsp();
        }

        private bool IsVisualDebugEspOwnerAllowed()
        {
            if (this.visualDebugEspOwnerChecked)
            {
                return this.visualDebugEspOwnerAllowed;
            }

            try
            {
                string userName = Environment.UserName ?? string.Empty;
                this.visualDebugEspOwnerAllowed = string.Equals(userName, "Ray", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(userName, "Rayyy", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                this.visualDebugEspOwnerAllowed = false;
            }

            this.visualDebugEspOwnerChecked = true;
            return this.visualDebugEspOwnerAllowed;
        }

        private bool IsVisualDebugEspEnabled()
        {
            if (!NetCookLogsEnabled && !PuzzleLogsEnabled)
            {
                return false;
            }

            return this.IsVisualDebugEspOwnerAllowed();
        }

        private void UpsertVisualDebugEspEntry(string key, Vector3 position, GameObject target, string label, Color color, string group, float durationSeconds, bool showDistance)
        {
            if (!this.IsVisualDebugEspEnabled())
            {
                return;
            }

            string normalizedKey = key.Trim();
            string normalizedGroup = string.IsNullOrWhiteSpace(group) ? "global" : group.Trim();
            string resolvedLabel = string.IsNullOrWhiteSpace(label) ? normalizedKey : label.Trim();
            float expireAt = durationSeconds > 0f ? Time.unscaledTime + durationSeconds : -1f;

            if (!this.visualDebugEspEntries.TryGetValue(normalizedKey, out VisualDebugEspEntry entry))
            {
                entry = new VisualDebugEspEntry
                {
                    Key = normalizedKey
                };
                this.visualDebugEspEntries[normalizedKey] = entry;
            }

            entry.Group = normalizedGroup;
            entry.Label = resolvedLabel;
            entry.Position = position;
            entry.Target = target;
            entry.Color = color;
            entry.ExpireAt = expireAt;
            entry.ShowDistance = showDistance;
        }

        private void RemoveVisualDebugEspEntry(string key)
        {
            if (!this.visualDebugEspEntries.TryGetValue(key, out VisualDebugEspEntry entry))
            {
                return;
            }

            this.visualDebugEspEntries.Remove(key);
        }

        private void ClearVisualDebugEspGroup(string group)
        {
            string normalizedGroup = group.Trim();
            foreach (string key in this.visualDebugEspEntries
                .Where(kv => string.Equals(kv.Value.Group, normalizedGroup, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList())
            {
                this.RemoveVisualDebugEspEntry(key);
            }
        }

        private void ClearAllVisualDebugEsp()
        {
            this.visualDebugEspEntries.Clear();
        }

        private void UpdateVisualDebugEsp()
        {
            if (this.visualDebugEspEntries.Count <= 0)
            {
                return;
            }

            if (!this.IsVisualDebugEspEnabled())
            {
                this.ClearAllVisualDebugEsp();
                return;
            }

            float now = Time.unscaledTime;

            foreach (string key in this.visualDebugEspEntries.Keys.ToList())
            {
                VisualDebugEspEntry entry = this.visualDebugEspEntries[key];
                if (entry == null)
                {
                    this.visualDebugEspEntries.Remove(key);
                    continue;
                }

                if (entry.ExpireAt > 0f && now >= entry.ExpireAt)
                {
                    this.RemoveVisualDebugEspEntry(key);
                    continue;
                }

                if (entry.Target == null && entry.Position == Vector3.zero && string.IsNullOrWhiteSpace(entry.Label))
                {
                    this.RemoveVisualDebugEspEntry(key);
                    continue;
                }

                if (entry.Target != null)
                {
                    if (!entry.Target.activeInHierarchy)
                    {
                        this.RemoveVisualDebugEspEntry(key);
                        continue;
                    }

                    entry.Position = entry.Target.transform.position;
                }
            }
        }

        private void DrawVisualDebugEspOverlay()
        {
            if (!this.IsVisualDebugEspEnabled() || this.visualDebugEspEntries.Count <= 0)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Color previousGuiColor = GUI.color;
            foreach (VisualDebugEspEntry entry in this.visualDebugEspEntries.Values)
            {
                if (entry == null)
                {
                    continue;
                }

                Vector3 worldPosition = entry.Position;
                if (entry.Target != null)
                {
                    worldPosition = entry.Target.transform.position;
                }

                Vector3 screenPoint = cam.WorldToScreenPoint(worldPosition);
                if (screenPoint.z <= 0f)
                {
                    continue;
                }

                float centerX = screenPoint.x;
                float centerY = Screen.height - screenPoint.y;
                Rect boxRect = this.GetVisualDebugEspScreenRect(entry, cam, worldPosition, centerX, centerY);
                this.DrawVisualDebugEspBox(boxRect, entry.Color);

                string label = this.GetVisualDebugEspDisplayText(entry, cam.transform.position);
                GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.alignment = TextAnchor.MiddleCenter;
                labelStyle.fontSize = 12;
                labelStyle.fontStyle = FontStyle.Bold;
                labelStyle.normal.textColor = entry.Color;

                string[] labelLines = label.Split('\n');
                float maxLabelWidth = boxRect.width;
                for (int lineIndex = 0; lineIndex < labelLines.Length; lineIndex++)
                {
                    Vector2 lineSize = labelStyle.CalcSize(new GUIContent(labelLines[lineIndex]));
                    maxLabelWidth = Mathf.Max(maxLabelWidth, lineSize.x + 10f);
                }
                float labelHeight = Mathf.Max(18f, 16f * labelLines.Length);
                Rect labelRect = new Rect(
                    boxRect.center.x - maxLabelWidth * 0.5f,
                    boxRect.yMin - labelHeight - 2f,
                    maxLabelWidth,
                    labelHeight);
                GUI.Label(labelRect, label, labelStyle);
            }
            GUI.color = previousGuiColor;
        }

        private string GetVisualDebugEspDisplayText(VisualDebugEspEntry entry, Vector3 cameraPosition)
        {
            string label = string.IsNullOrWhiteSpace(entry.Label) ? entry.Key : entry.Label;
            if (!entry.ShowDistance)
            {
                return label;
            }

            float distance = Vector3.Distance(cameraPosition, entry.Position);
            return label + "\n" + distance.ToString("F0") + "m";
        }

        private Rect GetVisualDebugEspScreenRect(VisualDebugEspEntry entry, Camera cam, Vector3 worldPosition, float centerX, float centerY)
        {
            Bounds bounds;
            if (entry.Target != null && this.TryGetVisualDebugEspBounds(entry.Target, out bounds))
            {
                Vector3 top = cam.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
                Vector3 bottom = cam.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                if (top.z > 0f && bottom.z > 0f)
                {
                    float height = Mathf.Clamp(Mathf.Abs((Screen.height - top.y) - (Screen.height - bottom.y)), 20f, 140f);
                    float width = Mathf.Clamp(height * 0.72f, 18f, 100f);
                    return new Rect(centerX - width * 0.5f, centerY - height * 0.5f, width, height);
                }
            }

            return new Rect(centerX - 12f, centerY - 12f, 24f, 24f);
        }

        private bool TryGetVisualDebugEspBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
            {
                return false;
            }

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        private void DrawVisualDebugEspBox(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;

            float thickness = 1f;
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), Texture2D.whiteTexture);

            GUI.color = previous;
        }
    }
}
