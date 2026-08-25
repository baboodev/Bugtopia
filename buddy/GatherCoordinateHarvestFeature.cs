using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    // Harvests resource coordinates out of the live scan.
    //
    // WHY THIS WAY. The request was to take coordinates from the decrypted tables — they are not
    // there. All 913 tables were checked for arrays of three floats: the only one carrying
    // coordinates is WorldMapEventPos (106 map-UI points — shops, buildings); the rest are
    // hitboxes, plot boundaries and camera vectors. The design tables describe WHAT a resource is
    // (`Dynamicbush`, `Entity`); WHERE it stands is Unity scene data. Same story as with the fish
    // water bodies.
    //
    // The live scan knows every resource's position, so that is the source. The set accumulates as
    // the player travels: whatever streamed in joins the set and stays there.
    //
    // ⚠️ THIS BEATS THE HARDCODED ARRAYS ON COVERAGE. Those hold 238 points of eight kinds,
    // collected by hand. The scan sees EVERY resource, including kinds that were not in the arrays
    // at all — bamboo, the odd mushroom variants, event plants. The file is also the answer to
    // "which types were missing".
    public partial class HeartopiaComplete
    {
        // Its own switch: the harvest goes to a file and grows all session, so there is no reason
        // for it to be on by default.
        internal static bool MasterLogGatherHarvest = false;

        private const string GatherHarvestFileName = "gathered-coordinates.tsv";

        // Two points closer than this are the same resource. The same threshold the farm uses to
        // call nodes identical, so the two sets cannot drift apart.
        private const float GatherHarvestSameSpotDistance = 1.5f;

        // Not written on every scan: the file is rewritten whole, and scans run every 2 seconds.
        private const int GatherHarvestFlushEvery = 25;

        private readonly Dictionary<string, GatherHarvestEntry> gatherHarvest =
            new Dictionary<string, GatherHarvestEntry>();

        private int gatherHarvestSinceFlush;
        private bool gatherHarvestPathLogged;

        private struct GatherHarvestEntry
        {
            public int ItemId;      // resolved item id, 0 when the resource carries none
            public int ProduceId;
            public int StaticId;    // entity staticId — the only handle mushrooms give us
            public Vector3 Position;
            public string Scene;    // the active Unity scene — see NoteGatherHarvest
        }

        // Called from the scan for every entity it finds.
        internal void NoteGatherHarvest(Vector3 position, int produceId, int staticId, int itemId)
        {
            if (!MasterLogGatherHarvest)
            {
                return;
            }

            this.EnsureGatherHarvestLoaded();

            // ⚠️ THE SCENE IS PART OF THE KEY. Without it the underwater areas, the micro-home and
            // the surface would fold into one list, and their coordinates live in separate systems:
            // the fallback would offer a seabed rock to a player standing on the shore. Two
            // different objects from different scenes could also land on the same cell and
            // overwrite each other.
            string scene = string.Empty;
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? string.Empty; } catch { }

            // Keyed by CELL rather than exact coordinates: an entity's position drifts slightly
            // between streams, and without rounding a single bush would produce dozens of rows.
            string key = BuildGatherHarvestKey(scene, staticId, produceId, position);
            if (this.gatherHarvest.ContainsKey(key))
            {
                return;
            }

            this.gatherHarvest[key] = new GatherHarvestEntry
            {
                ItemId = itemId,
                ProduceId = produceId,
                StaticId = staticId,
                Position = position,
                Scene = scene,
            };

            if (++this.gatherHarvestSinceFlush >= GatherHarvestFlushEvery)
            {
                this.gatherHarvestSinceFlush = 0;
                this.FlushGatherHarvest();
            }
        }


        // Loading the previously harvested set is MANDATORY, not a convenience.
        //
        // ⚠️ MUSHROOMS (and everything dynamic) DO NOT ALL SPAWN AT ONCE. There are more spawn
        // points than there are mushrooms in the world at any moment: one lap of the map produced
        // 64 mushrooms across five kinds, and that is a SAMPLE, not the full list of places. The
        // complete set only accumulates over repeated laps.
        //
        // Without this load the harvester started from an empty dictionary and rewrote the file
        // whole, so the SECOND walk erased the first one's result — the set could never converge.
        // Now the previous contents are read back into memory and new points are added to them.
        private bool gatherHarvestLoaded;

        private void EnsureGatherHarvestLoaded()
        {
            if (this.gatherHarvestLoaded)
            {
                return;
            }

            this.gatherHarvestLoaded = true;
            try
            {
                string path = HelperPaths.GetFile(GatherHarvestFileName);
                if (!File.Exists(path))
                {
                    return;
                }

                int loaded = 0;
                foreach (string raw in File.ReadAllLines(path))
                {
                    if (string.IsNullOrEmpty(raw) || raw[0] == '#' || raw.StartsWith("scene\t", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string[] p = raw.Split('\t');
                    if (p.Length < 7)
                    {
                        continue;
                    }

                    if (!int.TryParse(p[1], out int itemId)
                        || !int.TryParse(p[2], out int produceId)
                        || !int.TryParse(p[3], out int staticId)
                        || !float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                        || !float.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                        || !float.TryParse(p[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        continue;
                    }

                    Vector3 pos = new Vector3(x, y, z);
                    string key = BuildGatherHarvestKey(p[0], staticId, produceId, pos);
                    this.gatherHarvest[key] = new GatherHarvestEntry
                    {
                        ItemId = itemId,
                        ProduceId = produceId,
                        StaticId = staticId,
                        Position = pos,
                        Scene = p[0],
                    };
                    loaded++;
                }

                ModLogger.Msg("[GatherHarvest] carried " + loaded + " point(s) forward from the previous run"
                    + " — dynamic resources need several passes to cover every spawn point.");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[GatherHarvest] load failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // One key computation for both writing and reading: were they to diverge, a loaded point
        // would not match the same point from the scan and the set would double on every launch.
        private static string BuildGatherHarvestKey(string scene, int staticId, int produceId, Vector3 position)
        {
            int cx = Mathf.RoundToInt(position.x / GatherHarvestSameSpotDistance);
            int cy = Mathf.RoundToInt(position.y / GatherHarvestSameSpotDistance);
            int cz = Mathf.RoundToInt(position.z / GatherHarvestSameSpotDistance);
            return (scene ?? string.Empty) + ":" + staticId + ":" + produceId + ":" + cx + ":" + cy + ":" + cz;
        }

        // A full rewrite — the whole set lives in memory so there is nothing to append, and a
        // rewrite survives a game crash better than a stream left open.
        internal void FlushGatherHarvest()
        {
            if (this.gatherHarvest.Count == 0)
            {
                return;
            }

            try
            {
                string path = HelperPaths.GetFile(GatherHarvestFileName);
                StringBuilder sb = new StringBuilder(this.gatherHarvest.Count * 48);
                sb.Append("# Bugtopia gather-coordinate harvest — collected from the LIVE component scan.\n");
                sb.Append("# The design tables carry no resource placement; this is the only accurate source.\n");
                sb.Append("scene\titemId\tproduceId\tstaticId\tx\ty\tz\n");

                foreach (KeyValuePair<string, GatherHarvestEntry> kv in this.gatherHarvest)
                {
                    GatherHarvestEntry e = kv.Value;
                    sb.Append(e.Scene ?? string.Empty).Append('\t')
                      .Append(e.ItemId).Append('\t')
                      .Append(e.ProduceId).Append('\t')
                      .Append(e.StaticId).Append('\t')
                      .Append(e.Position.x.ToString("F3", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(e.Position.y.ToString("F3", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(e.Position.z.ToString("F3", CultureInfo.InvariantCulture)).Append('\n');
                }

                File.WriteAllText(path, sb.ToString());

                if (!this.gatherHarvestPathLogged)
                {
                    this.gatherHarvestPathLogged = true;
                    ModLogger.Msg("[GatherHarvest] writing to " + path);
                }
            }
            catch (Exception ex)
            {
                // Never let a disk problem take the scan down with it.
                ModLogger.Msg("[GatherHarvest] write failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
