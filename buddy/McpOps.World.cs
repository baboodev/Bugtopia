#if FEATURE_MCP
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // ============================================================================================
    // MCP world ops (phase 2a) — "see the game": where the player is, what is around them, and what
    // is on screen.
    //
    // ── THE SNAPSHOT RULE ────────────────────────────────────────────────────────────────────────
    // An agent polls. `entities.list` at 5 Hz must cost the same as not calling it, so every read
    // here is served from a SCALARIZED snapshot with a TTL, and every response carries `ageMs` so
    // the agent knows how stale its picture is.
    //
    // Scalarized is not an optimisation, it is a correctness requirement. The radar learned this the
    // hard way (HeartopiaComplete.Radar.cs:2084): holding an IL2CPP GameObject reference across
    // frames access-violates when Unity destroys the object underneath the managed wrapper. So the
    // scan converts to plain strings/Vector3/int inside one frame and drops the array before it
    // returns — nothing native survives into the cache.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // How long a world scan stays servable, and the floor an agent may ask for. The radar pays
        // ~2 s for the same FindObjectsOfType walk, so this matches its cadence rather than adding
        // a second, unsynchronised full-scene walk.
        private const float McpWorldSnapshotTtlSeconds = 2f;
        private const float McpWorldSnapshotMinIntervalSeconds = 1f;
        private const int McpWorldScanHardCap = 20000;

        // A struct, and deliberately WITHOUT a Kind field. Classification used to run inside the
        // scan, over every row in range (~4000 in a town) — but a query only ever shows rows that
        // already passed the radius filter, so all but a few dozen of those classifications were
        // thrown away. It now happens in the query, bounded by the radius the caller asked for.
        //
        // What this does NOT fix, and cannot: the scan's real cost is ~15k interop transitions
        // (activeInHierarchy + transform + position + name, per object). That is the floor for a
        // full-scene walk, which is why the radar throttles the same walk to 2 s. The TTL is the
        // mitigation; measured on a loaded town this is ~7.5 ms, i.e. one dropped frame per rescan.
        private struct McpWorldRow
        {
            public string Name;
            public Vector3 Pos;
            public float Dist;
            public int Id;
        }

        private readonly List<McpWorldRow> mcpWorldSnapshot = new List<McpWorldRow>(256);
        private float mcpWorldSnapshotAt = float.NegativeInfinity;
        private Vector3 mcpWorldSnapshotOrigin;
        private int mcpWorldSnapshotScanned;
        private double mcpWorldSnapshotScanMs;
        // Phase breakdown of the last scan, so the cost can be attributed instead of assumed.
        private double mcpWorldSnapshotFindMs;
        private double mcpWorldSnapshotLoopMs;
        private double mcpWorldSnapshotSortMs;
        private bool mcpWorldSnapshotValid;

        private void RegisterMcpWorldOps()
        {
            McpOps.Register(
                "player.get",
                McpOpFlags.Read | McpOpFlags.NeedsWorld,
                McpOpCost.Cheap,
                "Local player position, facing and netId. NOTE: the position anchor is resolved by "
                + "GameObject name, which remote players share — treat it as an anchor, not as proof "
                + "of identity. netId comes from the self-resolve path and is authoritative.",
                "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
                this.HandleMcpPlayerGet);

            McpOps.Register(
                "entities.list",
                McpOpFlags.Read | McpOpFlags.NeedsWorld,
                McpOpCost.Heavy,
                "World objects around the player, nearest first, from a cached scene scan. Without a "
                + "`name` filter only recognised kinds are returned (player/insect/fish/meteor/bird/"
                + "bubble); with one, ANY object whose name contains it is included — that is how you "
                + "locate a prefab by name. Every response carries ageMs and scanMs.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"radius\":{\"type\":\"number\",\"description\":\"metres from the player, default 60, max 500\"},"
                + "\"kind\":{\"type\":\"string\",\"description\":\"exact kind filter: player|insect|fish|meteor|bird|bubble|other\"},"
                + "\"name\":{\"type\":\"string\",\"description\":\"case-insensitive substring of the GameObject name; also unlocks unclassified objects\"},"
                + "\"limit\":{\"type\":\"integer\",\"description\":\"max rows, default 200, max 1000\"},"
                + "\"maxAgeMs\":{\"type\":\"integer\",\"description\":\"accept a cached scan up to this old, default 2000, floor 500\"},"
                + "\"refresh\":{\"type\":\"boolean\",\"description\":\"force a rescan (still rate-limited to 1/s)\"}},"
                + "\"additionalProperties\":false}",
                this.HandleMcpEntitiesList);

            McpOps.Register(
                "ui.tree",
                McpOpFlags.Read,
                McpOpCost.Cheap,
                "Active Unity canvases and their top-level children — what is on screen right now. "
                + "Works before a world exists, so it also describes the login and loading screens.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"children\":{\"type\":\"integer\",\"description\":\"children listed per canvas, default 12, max 50\"},"
                + "\"activeOnly\":{\"type\":\"boolean\",\"description\":\"skip inactive canvases, default true\"}},"
                + "\"additionalProperties\":false}",
                this.HandleMcpUiTree);

            McpOps.Register(
                "screenshot",
                McpOpFlags.Read,
                McpOpCost.Heavy,
                "A JPEG of the current frame, base64-encoded — the real backbuffer, so the game UI is "
                + "in it. Takes one extra frame to answer (the capture runs after rendering). "
                + "Rate-limited to one per 400 ms.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"maxWidth\":{\"type\":\"integer\",\"description\":\"downscale to this width, default 1600, 320-3840; 0 = native\"},"
                + "\"quality\":{\"type\":\"integer\",\"description\":\"JPEG quality 10-95, default 70\"}},"
                + "\"additionalProperties\":false}",
                this.HandleMcpScreenshot);

            McpOps.Register(
                "ui.find",
                McpOpFlags.Read,
                McpOpCost.Heavy,
                "Find clickable UI elements by name substring, with their full hierarchy paths. Use "
                + "this to get an exact path before ui.click — clicking by substring alone is how you "
                + "press the wrong button. Only elements that are actually ON SCREEN are returned by "
                + "default: this game keeps a closed panel's buttons active, so being active proves "
                + "nothing. Pass visibleOnly:false to see the hidden ones and why they are hidden.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"name\":{\"type\":\"string\",\"description\":\"case-insensitive substring of the object name\"},"
                + "\"limit\":{\"type\":\"integer\",\"description\":\"max rows, default 25\"},"
                + "\"interactableOnly\":{\"type\":\"boolean\",\"description\":\"only enabled, interactable buttons, default true\"},"
                + "\"visibleOnly\":{\"type\":\"boolean\",\"description\":\"only elements actually on screen, default true; false adds a hiddenBy reason to each\"}},"
                + "\"additionalProperties\":false}",
                this.HandleMcpUiFind);

            McpOps.Register(
                "ui.click",
                McpOpFlags.Write,
                McpOpCost.Heavy,
                "Click a UI element, addressed by exact hierarchy path (preferred) or by unique name. "
                + "Refuses when the name matches more than one element — an ambiguous click is worse "
                + "than no click. Drives the mod's own SimulateClick: EventTrigger, then the "
                + "ExecuteEvents cascade, then a reflected OnClick.",
                "{\"type\":\"object\",\"properties\":{"
                + "\"path\":{\"type\":\"string\",\"description\":\"exact hierarchy path from ui.find\"},"
                + "\"name\":{\"type\":\"string\",\"description\":\"exact object name; must be unique\"},"
                + "\"allowHidden\":{\"type\":\"boolean\",\"description\":\"click even if the element is active but not on screen (a closed panel keeps its buttons alive)\"}},"
                + "\"additionalProperties\":false}",
                this.HandleMcpUiClick);
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // screenshot — a two-phase op (see McpOps.Defer)
        // ────────────────────────────────────────────────────────────────────────────────────────

        private object mcpScreenshotOwner;
        private int mcpScreenshotDeferredFrames;

        private string HandleMcpScreenshot(Dictionary<string, object> args)
        {
            // The capture site is a different point in the frame than this pump, so one call spans
            // two Drain passes: request now, answer next time. `mcpScreenshotOwner` is the args
            // instance of the call that owns the in-flight capture — a second, concurrent screenshot
            // must not walk off with the first one's image.
            bool mine = this.mcpScreenshotOwner != null && ReferenceEquals(this.mcpScreenshotOwner, args);

            if (!mine)
            {
                if (this.mcpScreenshotOwner != null)
                {
                    throw new McpOpException("busy", "another screenshot is still being captured");
                }

                McpScreenshot.EnsureInstalled();

                if (McpScreenshot.TooSoon(out int waitMs))
                {
                    throw new McpOpException("busy", "rate limited — retry in " + waitMs + " ms");
                }

                int maxWidth = McpArgs.GetInt(args, "maxWidth", 1600);
                if (maxWidth != 0)
                {
                    maxWidth = Mathf.Clamp(maxWidth, 320, 3840);
                }

                int quality = Mathf.Clamp(McpArgs.GetInt(args, "quality", 70), 10, 95);

                McpScreenshot.Request(maxWidth, quality);
                this.mcpScreenshotOwner = args;
                this.mcpScreenshotDeferredFrames = 0;
                return McpOps.Defer;
            }

            if (!McpScreenshot.IsReady)
            {
                this.mcpScreenshotDeferredFrames++;
                // ~2 s of frames. If the capture site never runs — a render node that silently
                // stopped ticking — fail with a diagnosis rather than burning the socket's 5 s
                // timeout on something that will never complete.
                if (this.mcpScreenshotDeferredFrames > 120)
                {
                    this.mcpScreenshotOwner = null;
                    throw new McpOpException("internal",
                        "capture never completed after 120 frames — site: " + McpScreenshot.Status);
                }

                return McpOps.Defer;
            }

            this.mcpScreenshotOwner = null;
            if (!McpScreenshot.TryTake(out byte[] bytes, out int width, out int height,
                    out double readMs, out double encodeMs, out bool torn, out string error))
            {
                throw new McpOpException("internal", error ?? "capture produced no data");
            }

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Str("format", "jpeg");
            w.Num("width", width);
            w.Num("height", height);
            w.Num("bytes", bytes.Length);
            w.Num("readMs", readMs);
            w.Num("encodeMs", encodeMs);
            w.Num("deferredFrames", this.mcpScreenshotDeferredFrames);
            w.Bool("mayBeTorn", torn);
            w.Str("site", McpScreenshot.Status);
            w.Str("base64", Convert.ToBase64String(bytes));
            w.EndObject();
            return w.ToString();
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // player.get
        // ────────────────────────────────────────────────────────────────────────────────────────

        private string HandleMcpPlayerGet(Dictionary<string, object> args)
        {
            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();

            bool havePos = this.TryGetLocalPlayerPosition(out Vector3 pos);
            w.Bool("positionKnown", havePos);
            if (havePos)
            {
                w.BeginObject("position");
                w.Num("x", pos.x);
                w.Num("y", pos.y);
                w.Num("z", pos.z);
                w.EndObject();
            }

            // Facing is read off the same anchor object; absent rather than zeroed when unresolved,
            // so an agent never mistakes "unknown" for "facing north".
            try
            {
                GameObject anchor = GetLocalPlayer();
                if (anchor != null)
                {
                    w.Num("yaw", anchor.transform.eulerAngles.y);
                    w.Str("anchorObject", anchor.name);
                }
            }
            catch
            {
            }

            if (this.TryResolveSelfPlayerNetId(out uint selfNetId))
            {
                w.Num("netId", selfNetId);
            }

            w.Num("worldEpoch", AuraMonoWorldEpoch);
            w.EndObject();
            return w.ToString();
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // entities.list
        // ────────────────────────────────────────────────────────────────────────────────────────

        private string HandleMcpEntitiesList(Dictionary<string, object> args)
        {
            float radius = McpArgs.GetInt(args, "radius", 60);
            if (radius <= 0f)
            {
                radius = 60f;
            }
            else if (radius > 500f)
            {
                radius = 500f;
            }

            int limit = McpArgs.GetInt(args, "limit", 200);
            if (limit < 1)
            {
                limit = 1;
            }
            else if (limit > 1000)
            {
                limit = 1000;
            }

            int maxAgeMs = McpArgs.GetInt(args, "maxAgeMs", (int)(McpWorldSnapshotTtlSeconds * 1000f));
            if (maxAgeMs < 500)
            {
                maxAgeMs = 500;
            }

            string kindFilter = McpArgs.GetString(args, "kind");
            string nameFilter = McpArgs.GetString(args, "name");
            bool refresh = McpArgs.GetBool(args, "refresh", false);

            this.EnsureMcpWorldSnapshot(refresh, maxAgeMs / 1000f);

            if (!this.mcpWorldSnapshotValid)
            {
                throw new McpOpException("internal", "no world snapshot could be taken (player anchor unresolved?)");
            }

            float now = Time.unscaledTime;
            List<McpWorldRow> matched = new List<McpWorldRow>(Math.Min(limit, 128));
            List<string> matchedKinds = new List<string>(Math.Min(limit, 128));
            for (int i = 0; i < this.mcpWorldSnapshot.Count; i++)
            {
                McpWorldRow row = this.mcpWorldSnapshot[i];
                if (row.Dist > radius)
                {
                    // The snapshot is distance-sorted, so the first miss ends the scan.
                    break;
                }

                // Name filter BEFORE classification: it is an allocation-free substring test, while
                // classification lowercases the name. Ordering matters here — a `name` query in a
                // town would otherwise classify every row in radius just to discard it.
                if (!string.IsNullOrEmpty(nameFilter)
                    && row.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string kind = this.ClassifyMcpWorldObject(row.Name);

                if (!string.IsNullOrEmpty(kindFilter)
                    && !string.Equals(kind, kindFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(nameFilter) && string.Equals(kind, "other", StringComparison.Ordinal))
                {
                    // Unclassified objects are the whole scene; they are opt-in via `name`.
                    continue;
                }

                matched.Add(row);
                matchedKinds.Add(kind);
            }

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Num("ageMs", (now - this.mcpWorldSnapshotAt) * 1000f);
            w.Num("scanMs", this.mcpWorldSnapshotScanMs);
            // Attribution of scanMs: findMs = Unity's own full-scene walk, loopMs = the per-object
            // interop reads, sortMs = the distance sort.
            w.Num("findMs", this.mcpWorldSnapshotFindMs);
            w.Num("loopMs", this.mcpWorldSnapshotLoopMs);
            w.Num("sortMs", this.mcpWorldSnapshotSortMs);
            w.Num("scannedObjects", this.mcpWorldSnapshotScanned);
            w.Num("snapshotRows", this.mcpWorldSnapshot.Count);
            w.Num("matched", matched.Count);
            w.Num("returned", Math.Min(matched.Count, limit));
            w.BeginObject("origin");
            w.Num("x", this.mcpWorldSnapshotOrigin.x);
            w.Num("y", this.mcpWorldSnapshotOrigin.y);
            w.Num("z", this.mcpWorldSnapshotOrigin.z);
            w.EndObject();

            w.BeginArray("entities");
            int emitted = 0;
            for (int i = 0; i < matched.Count && emitted < limit; i++, emitted++)
            {
                McpWorldRow row = matched[i];
                w.BeginArrayObject();
                w.Str("name", row.Name);
                w.Str("kind", matchedKinds[i]);
                w.Num("dist", row.Dist);
                w.Num("x", row.Pos.x);
                w.Num("y", row.Pos.y);
                w.Num("z", row.Pos.z);
                w.Num("id", row.Id);
                w.EndObject();
            }

            w.EndArray();
            w.EndObject();
            return w.ToString();
        }

        // Rebuilds the scalarized scene snapshot when it is older than `ttlSeconds`. Rate-limited
        // independently of the TTL so `refresh:true` in a loop cannot turn into a per-frame full
        // scene walk.
        private void EnsureMcpWorldSnapshot(bool force, float ttlSeconds)
        {
            float now = Time.unscaledTime;
            float age = now - this.mcpWorldSnapshotAt;

            if (this.mcpWorldSnapshotValid && !force && age < ttlSeconds)
            {
                return;
            }

            if (this.mcpWorldSnapshotValid && age < McpWorldSnapshotMinIntervalSeconds)
            {
                return; // serve the slightly-stale one rather than pay for a second scan this second
            }

            if (!this.TryGetLocalPlayerPosition(out Vector3 origin))
            {
                return;
            }

            // Three phases, timed separately — the whole point is to stop GUESSING which one costs.
            // The claim "the interop walk dominates" is arithmetic (≈6 boundary crossings per object,
            // see the loop below), and arithmetic is not a measurement: FindObjectsOfType is itself a
            // full-scene walk Unity-side, and the sort runs a delegate ~55k times. If `findMs`
            // dominates, no loop optimisation can ever help and only the TTL matters.
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            this.mcpWorldSnapshot.Clear();
            this.mcpWorldSnapshotScanned = 0;
            double findMs;
            double loopMs;

            // The ONE native array. Everything below copies scalars out of it; no element and no
            // component reference outlives this method (Radar.cs:2084).
            GameObject[] all = UnityEngine.Object.FindObjectsOfType<GameObject>();
            findMs = sw.Elapsed.TotalMilliseconds;
            if (all != null)
            {
                int count = all.Length;
                if (count > McpWorldScanHardCap)
                {
                    count = McpWorldScanHardCap;
                }

                for (int i = 0; i < count; i++)
                {
                    GameObject go = all[i];
                    if (go == null)
                    {
                        continue;
                    }

                    // Every member access here is an interop transition, so they are ordered by how
                    // much work each one lets us skip: cheap reject first, and `name` — the only one
                    // that marshals a string — last, after the distance filter has already dropped
                    // the row or kept it.
                    string name;
                    Vector3 pos;
                    int id;
                    float dist;
                    try
                    {
                        if (!go.activeInHierarchy)
                        {
                            continue;
                        }

                        pos = go.transform.position;
                        this.mcpWorldSnapshotScanned++;

                        dist = Vector3.Distance(origin, pos);
                        if (dist > 500f)
                        {
                            continue;
                        }

                        name = go.name;
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        id = go.GetInstanceID();
                    }
                    catch
                    {
                        // Destroyed underneath us mid-walk — the radar hits this too.
                        continue;
                    }

                    this.mcpWorldSnapshot.Add(new McpWorldRow
                    {
                        Name = name,
                        Pos = pos,
                        Dist = dist,
                        Id = id,
                    });
                }
            }

            loopMs = sw.Elapsed.TotalMilliseconds - findMs;
            this.mcpWorldSnapshot.Sort((a, b) => a.Dist.CompareTo(b.Dist));
            sw.Stop();
            this.mcpWorldSnapshotFindMs = findMs;
            this.mcpWorldSnapshotLoopMs = loopMs;
            this.mcpWorldSnapshotSortMs = sw.Elapsed.TotalMilliseconds - findMs - loopMs;

            this.mcpWorldSnapshotOrigin = origin;
            this.mcpWorldSnapshotAt = now;
            this.mcpWorldSnapshotScanMs = sw.Elapsed.TotalMilliseconds;
            this.mcpWorldSnapshotValid = true;
        }

        // Reuses the farms' own name predicates so a kind here means the same thing it means to the
        // feature that acts on it — an agent reading `kind:"insect"` sees exactly what Insect Farm
        // would target.
        private string ClassifyMcpWorldObject(string name)
        {
            string lower = name.ToLowerInvariant();

            if (lower.Contains("p_player_skeleton"))
            {
                return "player";
            }

            try
            {
                if (this.ShouldTrackInsectObject(lower)) return "insect";
                if (this.ShouldTrackFishShadowObject(lower)) return "fish";
                if (this.ShouldTrackMeteorObject(lower)) return "meteor";
                if (this.ShouldTrackBirdObject(lower)) return "bird";
                if (this.ShouldTrackBubbleObject(lower)) return "bubble";
            }
            catch
            {
                // A predicate that throws must not poison the whole scan.
            }

            return "other";
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // ui.find / ui.click
        // ────────────────────────────────────────────────────────────────────────────────────────

        private sealed class McpUiHit
        {
            public string Name;
            public string Path;
            public bool Interactable;
            public string Label;
            public GameObject Target;
            public Button Button;
            public bool Visible;
            public string HiddenBy;
        }

        // ── Is this element actually ON SCREEN and clickable? ────────────────────────────────────
        // `activeInHierarchy` is NOT visibility, and assuming otherwise made ui.click willing to
        // press buttons nobody can see: after the bag closes, `close@btn` under `BagPanel(Clone)`
        // stays active. Unity UI has several ways to hide something while leaving it active, so this
        // checks each of them and REPORTS WHICH ONE fired — the mechanism a given panel uses is then
        // data rather than guesswork.
        private bool IsMcpUiElementVisible(GameObject go, out string hiddenBy)
        {
            hiddenBy = null;
            try
            {
                // Zero scale — the cheapest hide of all, and invisible to every other check.
                Vector3 scale = go.transform.lossyScale;
                if (Mathf.Abs(scale.x) < 0.0001f || Mathf.Abs(scale.y) < 0.0001f)
                {
                    hiddenBy = "zero scale";
                    return false;
                }

                // CanvasGroup chain. Walked by hand rather than with GetComponentsInParent so
                // `ignoreParentGroups` can stop the walk where Unity would stop it.
                float alpha = 1f;
                Transform t = go.transform;
                while (t != null)
                {
                    CanvasGroup group = t.gameObject.GetComponent<CanvasGroup>();
                    if (group != null)
                    {
                        alpha *= group.alpha;
                        if (alpha < 0.02f)
                        {
                            hiddenBy = "CanvasGroup alpha " + alpha.ToString("F3");
                            return false;
                        }

                        if (!group.blocksRaycasts)
                        {
                            hiddenBy = "CanvasGroup blocksRaycasts=false";
                            return false;
                        }

                        if (!group.interactable)
                        {
                            hiddenBy = "CanvasGroup interactable=false";
                            return false;
                        }

                        if (group.ignoreParentGroups)
                        {
                            break;
                        }
                    }

                    t = t.parent;
                }

                // A disabled Canvas renders nothing, children included.
                Canvas canvas = go.GetComponentInParent<Canvas>();
                if (canvas == null)
                {
                    hiddenBy = "no parent Canvas";
                    return false;
                }

                if (!canvas.enabled || !canvas.gameObject.activeInHierarchy)
                {
                    hiddenBy = "Canvas disabled";
                    return false;
                }

                // Parked off-screen. Only meaningful for overlay canvases, where a RectTransform's
                // position is already in screen pixels; for camera/world canvases the position is in
                // world units and comparing it to Screen.width would be nonsense, so skip it.
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    RectTransform rect = go.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        Vector3 p = rect.position;
                        Rect r = rect.rect;
                        float halfW = Mathf.Abs(r.width * scale.x) * 0.5f;
                        float halfH = Mathf.Abs(r.height * scale.y) * 0.5f;
                        if (p.x + halfW < 0f || p.x - halfW > Screen.width
                            || p.y + halfH < 0f || p.y - halfH > Screen.height)
                        {
                            hiddenBy = "off-screen at " + p.x.ToString("F0") + "," + p.y.ToString("F0");
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                hiddenBy = "visibility check failed: " + ex.GetType().Name;
                return false;
            }
        }

        private List<McpUiHit> CollectMcpUiHits(string nameFilter, bool interactableOnly, int cap,
                                                bool visibleOnly = false)
        {
            List<McpUiHit> hits = new List<McpUiHit>();
            Button[] buttons = UnityEngine.Object.FindObjectsOfType<Button>();
            if (buttons == null)
            {
                return hits;
            }

            for (int i = 0; i < buttons.Length && hits.Count < cap; i++)
            {
                Button button = buttons[i];
                if (button == null)
                {
                    continue;
                }

                try
                {
                    GameObject go = button.gameObject;
                    if (go == null || !go.activeInHierarchy)
                    {
                        continue;
                    }

                    if (interactableOnly && !button.interactable)
                    {
                        continue;
                    }

                    string name = go.name;
                    if (!string.IsNullOrEmpty(nameFilter)
                        && (name == null || name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        continue;
                    }

                    bool visible = this.IsMcpUiElementVisible(go, out string hiddenBy);
                    if (visibleOnly && !visible)
                    {
                        continue;
                    }

                    string label = null;
                    try
                    {
                        Text text = go.GetComponentInChildren<Text>(false);
                        if (text != null)
                        {
                            label = text.text;
                        }
                    }
                    catch
                    {
                    }

                    hits.Add(new McpUiHit
                    {
                        Name = name,
                        Path = this.GetHierarchyPath(go.transform),
                        Interactable = button.interactable,
                        Label = label,
                        Target = go,
                        Button = button,
                        Visible = visible,
                        HiddenBy = hiddenBy,
                    });
                }
                catch
                {
                    // Destroyed mid-walk.
                }
            }

            return hits;
        }

        private string HandleMcpUiFind(Dictionary<string, object> args)
        {
            string nameFilter = McpArgs.GetString(args, "name");
            int limit = McpArgs.GetInt(args, "limit", 25);
            if (limit < 1) { limit = 1; } else if (limit > 200) { limit = 200; }
            bool interactableOnly = McpArgs.GetBool(args, "interactableOnly", true);
            // Default: only what is actually on screen. The whole reason this argument exists is
            // that an active-but-invisible element is the normal state for a closed panel here.
            bool visibleOnly = McpArgs.GetBool(args, "visibleOnly", true);

            List<McpUiHit> hits = this.CollectMcpUiHits(nameFilter, interactableOnly, limit, visibleOnly);

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            w.Num("count", hits.Count);
            w.Bool("visibleOnly", visibleOnly);
            w.BeginArray("elements");
            for (int i = 0; i < hits.Count; i++)
            {
                McpUiHit hit = hits[i];
                w.BeginArrayObject();
                w.Str("name", hit.Name);
                w.Str("path", hit.Path);
                w.Bool("interactable", hit.Interactable);
                w.Bool("visible", hit.Visible);
                if (!hit.Visible && !string.IsNullOrEmpty(hit.HiddenBy))
                {
                    w.Str("hiddenBy", hit.HiddenBy);
                }

                if (!string.IsNullOrEmpty(hit.Label))
                {
                    w.Str("label", hit.Label);
                }

                w.EndObject();
            }

            w.EndArray();
            w.EndObject();
            return w.ToString();
        }

        private string HandleMcpUiClick(Dictionary<string, object> args)
        {
            string path = McpArgs.GetString(args, "path");
            string name = McpArgs.GetString(args, "name");
            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(name))
            {
                throw new McpOpException("bad_args", "one of 'path' or 'name' is required");
            }

            // Search the whole clickable set, then insist on exactly one match. Clicking the first
            // of several same-named buttons is the kind of bug that looks like it worked.
            List<McpUiHit> hits = this.CollectMcpUiHits(null, false, 4096);
            List<McpUiHit> matches = new List<McpUiHit>();
            for (int i = 0; i < hits.Count; i++)
            {
                McpUiHit hit = hits[i];
                bool match = !string.IsNullOrEmpty(path)
                    ? string.Equals(hit.Path, path, StringComparison.Ordinal)
                    : string.Equals(hit.Name, name, StringComparison.Ordinal);
                if (match)
                {
                    matches.Add(hit);
                }
            }

            if (matches.Count == 0)
            {
                throw new McpOpException("bad_args",
                    "no active UI element matches " + (path != null ? "path '" + path : "name '" + name) + "'");
            }

            if (matches.Count > 1)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(matches.Count).Append(" elements match; address one by 'path': ");
                for (int i = 0; i < matches.Count && i < 5; i++)
                {
                    sb.Append('\n').Append(matches[i].Path);
                }

                throw new McpOpException("bad_args", sb.ToString());
            }

            McpUiHit target = matches[0];
            if (!target.Interactable)
            {
                throw new McpOpException("bad_args", "'" + target.Name + "' is present but not interactable");
            }

            // The element can be active and still not be on screen — a closed panel here keeps its
            // buttons alive. Clicking one is worse than doing nothing, because the caller has no way
            // to see that it happened to something invisible.
            if (!target.Visible && !McpArgs.GetBool(args, "allowHidden", false))
            {
                throw new McpOpException("bad_args",
                    "'" + target.Name + "' is active but NOT visible (" + target.HiddenBy
                    + ") — its panel is most likely closed. Open it first, or pass allowHidden:true "
                    + "if you really mean to drive a hidden element.");
            }

            // Button.onClick.Invoke() FIRST, because it is the path the mod's own working features
            // use on this game's UI (OpenInventory, ClickFirstFriendJoinButton). SimulateClick was
            // the obvious choice and it is wrong here: its ExecuteEvents cascade counts a handled
            // pointerDown as success, and a Button consumes pointerDown without activating — so it
            // reported a click that never happened. Kept only as the fallback for elements that are
            // clickable without being a Button.
            string dispatched;
            if (target.Button != null)
            {
                target.Button.onClick.Invoke();
                dispatched = "Button.onClick";
            }
            else
            {
                dispatched = this.SimulateClick(target.Target) ? "SimulateClick" : "none";
            }

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();
            // NOT "clicked": nothing here can know whether the game ACTED on it. This says what was
            // dispatched; confirming the effect is the caller's job (ui.tree, screenshot, an op).
            w.Str("dispatched", dispatched);
            w.Str("name", target.Name);
            w.Str("path", target.Path);
            if (!string.IsNullOrEmpty(target.Label))
            {
                w.Str("label", target.Label);
            }

            w.Str("verify", "confirm the effect yourself — ui.tree or screenshot. A dispatched event "
                + "is not proof the game acted on it.");
            if (string.Equals(dispatched, "none", StringComparison.Ordinal))
            {
                w.Str("note", "no handler accepted the event: not a Button, and EventTrigger, "
                    + "ExecuteEvents and a reflected OnClick all declined");
            }

            w.EndObject();
            return w.ToString();
        }

        // ────────────────────────────────────────────────────────────────────────────────────────
        // ui.tree
        // ────────────────────────────────────────────────────────────────────────────────────────

        private string HandleMcpUiTree(Dictionary<string, object> args)
        {
            int childLimit = McpArgs.GetInt(args, "children", 12);
            if (childLimit < 0)
            {
                childLimit = 0;
            }
            else if (childLimit > 50)
            {
                childLimit = 50;
            }

            bool activeOnly = McpArgs.GetBool(args, "activeOnly", true);

            McpJsonWriter w = new McpJsonWriter();
            w.BeginObject();

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            int shown = 0;

            w.BeginArray("canvases");
            if (canvases != null)
            {
                for (int i = 0; i < canvases.Length && shown < 40; i++)
                {
                    Canvas canvas = canvases[i];
                    if (canvas == null)
                    {
                        continue;
                    }

                    try
                    {
                        GameObject go = canvas.gameObject;
                        if (go == null || (activeOnly && !go.activeInHierarchy))
                        {
                            continue;
                        }

                        shown++;
                        w.BeginArrayObject();
                        w.Str("name", go.name);
                        w.Bool("active", go.activeInHierarchy);
                        w.Num("sortingOrder", canvas.sortingOrder);
                        w.Str("renderMode", canvas.renderMode.ToString());

                        Transform t = go.transform;
                        int childCount = t.childCount;
                        w.Num("childCount", childCount);

                        if (childLimit > 0 && childCount > 0)
                        {
                            w.BeginArray("children");
                            int listed = 0;
                            for (int c = 0; c < childCount && listed < childLimit; c++)
                            {
                                Transform child = t.GetChild(c);
                                if (child == null)
                                {
                                    continue;
                                }

                                GameObject childGo = child.gameObject;
                                if (childGo == null || (activeOnly && !childGo.activeInHierarchy))
                                {
                                    continue;
                                }

                                listed++;
                                w.ArrayStr(childGo.name);
                            }

                            w.EndArray();
                        }

                        w.EndObject();
                    }
                    catch
                    {
                        // Destroyed mid-walk; skip.
                    }
                }
            }

            w.EndArray();
            sw.Stop();
            w.Num("canvasesShown", shown);
            w.Num("scanMs", sw.Elapsed.TotalMilliseconds);
            w.EndObject();
            return w.ToString();
        }
    }
}
#endif
