using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace HeartopiaMod
{
    // "Gather Locations": rotates the player through categories of saved spots (flattened in
    // list order, enabled categories only), while the already-enabled Fish / Insect / Bird farms
    // work each stop. Hops to the next spot when every enabled farm has reported an empty range
    // for EmptyWaitSeconds. Start does not touch any farm setting. Travel goes through
    // RouteTravelHelper.
    public static class GatherLocationsFeature
    {
        private const float WorldUnavailableStopSeconds = 30f;
        private const float DefaultEmptyWaitSeconds = 10f;
        private const float MinEmptyWaitSeconds = 0f;
        private const float MaxEmptyWaitSeconds = 120f;
        private const float DefaultSettleGraceSeconds = 3f;
        private const float MinSettleGraceSeconds = 0f;
        private const float MaxSettleGraceSeconds = 10f;
        // Solo farms publish LastInRangeCount only while they are scanning. Same window
        // CombinedFarmFeature.StepFishCensus uses before a published scan counts as current.
        private const float SoloScanFreshSeconds = 5f;

        private const int FarmFish = 0;
        private const int FarmInsect = 1;
        private const int FarmBird = 2;
        private const int FarmCount = 3;

        private struct FixedSpot
        {
            public string name;
            public Vector3 pos;
            public FixedSpot(string name, float x, float y, float z)
            {
                this.name = name;
                this.pos = new Vector3(x, y, z);
            }
        }

        private struct FlatSpot
        {
            public string categoryId;
            public string categoryName;
            public string spotName;
            public Vector3 pos;
        }

        // Measured cycle (shortest round over the walker's own route lengths); the route starts at the nearest spot, so the first entry is arbitrary.
        private static readonly FixedSpot[] DefaultFishingSpots = new FixedSpot[]
        {
            new FixedSpot("Rosy River 1", -132.044f, 20.728f, 123.636f),
            new FixedSpot("Old Sea 1", -182.468f, 10.563f, 201.788f),
            new FixedSpot("Old Sea 2", -157.400f, 10.841f, 250.438f),
            new FixedSpot("Old Sea 3", -138.933f, 10.926f, 303.255f),
            new FixedSpot("Old Sea 4", -94.604f, 10.557f, 267.479f),
            new FixedSpot("Old Sea 5", 2.903f, 10.833f, 250.543f),
            new FixedSpot("Onsen Mountain Lake 2", -68.906f, 28.009f, 199.926f),
            new FixedSpot("Onsen Mountain Lake 1", 7.902f, 20.045f, 166.517f),
            new FixedSpot("Suburban Lake 2", -15.798f, 23.880f, 86.701f),
            new FixedSpot("Shallow River 2", 77.957f, 20.506f, 97.549f),
            new FixedSpot("Shallow River 1", 109.264f, 19.823f, 107.453f),
            new FixedSpot("Forest Lake 1", 169.729f, 31.086f, 79.796f),
            new FixedSpot("East Sea 5", 250.926f, 13.364f, 94.227f),
            new FixedSpot("East Sea 4", 245.587f, 10.670f, 33.395f),
            new FixedSpot("East Sea 3", 249.190f, 10.640f, -4.454f),
            new FixedSpot("East Sea 1", 266.370f, 11.274f, -105.900f),
            new FixedSpot("East Sea 2", 222.824f, 10.609f, -61.501f),
            new FixedSpot("Forest Lake 3", 150.090f, 21.495f, -52.907f),
            new FixedSpot("Forest Lake 2", 156.001f, 21.425f, 8.155f),
            new FixedSpot("Suburban Lake 3", 81.600f, 22.236f, 38.265f),
            new FixedSpot("Suburban Lake 4", 84.599f, 21.996f, 11.957f),
            new FixedSpot("Suburban Lake 5", 63.881f, 22.350f, -34.296f),
            new FixedSpot("Giantwood River 1", 68.196f, 19.838f, -95.980f),
            new FixedSpot("Giantwood River 2", 89.264f, 18.126f, -114.870f),
            new FixedSpot("Zephyr Sea 2", 40.823f, 11.601f, -158.479f),
            new FixedSpot("Zephyr Sea 1", 46.434f, 11.500f, -180.964f),
            new FixedSpot("Zephyr Sea 3", 0.809f, 11.698f, -142.726f),
            new FixedSpot("Suburban Lake 6", -9.485f, 15.069f, -72.374f),
            new FixedSpot("Zephyr Sea 4", -40.134f, 11.298f, -145.853f),
            new FixedSpot("Zephyr Sea 5", -114.150f, 11.407f, -185.543f),
            new FixedSpot("Tranquil River 1", -112.565f, 15.698f, -138.189f),
            new FixedSpot("Tranquil River 2", -91.925f, 19.004f, -97.962f),
            new FixedSpot("Suburban Lake 7", -99.859f, 19.652f, -53.989f),
            new FixedSpot("Whale Sea 1", -226.625f, 11.911f, -56.772f),
            new FixedSpot("Whale Sea 3", -251.356f, 10.776f, 8.765f),
            new FixedSpot("Whale Sea 2", -223.321f, 10.472f, -13.959f),
            new FixedSpot("Meadow Lake", -188.604f, 19.717f, -21.203f),
            new FixedSpot("Suburban Lake 8", -91.586f, 25.915f, 60.682f),
            new FixedSpot("Suburban Lake 1", -77.757f, 20.781f, 91.570f),
            new FixedSpot("Rosy River 2", -110.786f, 20.096f, 121.876f),
        };

        private static readonly List<HeartopiaComplete.LocationCategoryEntry> categories = new List<HeartopiaComplete.LocationCategoryEntry>();
        private static readonly List<FlatSpot> flattened = new List<FlatSpot>();
        private static readonly TravelCb travelCb = new TravelCb();
        private static readonly OneShotTravelCb oneShotCb = new OneShotTravelCb();

        private static bool active;
        private static int routeIndex;
        private static float emptyWaitSeconds = DefaultEmptyWaitSeconds;
        private static float settleGraceSeconds = DefaultSettleGraceSeconds;
        private static bool startNearest = true;
        private static bool legacyMigrated;
        private static float spotArrivedAt = -999f;
        private static float graceUntil = -999f;
        private static float emptySinceAt = -1f;
        private static float worldNotReadySince = -1f;
        private static bool retargetAfterWorld;
        private static bool pendingRetarget;
        private static string lastStatus = "Idle";
        // One-off Go button: walk/teleport to a listed spot without starting the route cycle.
        private static bool oneShotActive;
        private static Vector3 oneShotPos;
        private static string oneShotName = "";
        // Per farm: the scan cursor (a scan counts once) and whether the latest scan since arrival was empty.
        private static readonly float[] lastHandledScanAt = { -999f, -999f, -999f };
        private static readonly bool[] scannedSinceArrival = new bool[FarmCount];
        private static readonly bool[] emptyAtLastScan = new bool[FarmCount];

        public static bool Active => active;
        public static bool Walking => RouteTravelHelper.Walking;
        public static string LastStatus =>
            (oneShotActive || (active && RouteTravelHelper.Travelling))
                ? RouteTravelHelper.LastStatus
                : lastStatus;
        public static int RouteIndex => routeIndex;
        public static int RouteCount => flattened.Count;
        public static int CategoryCount => categories.Count;
        public static float EmptyWaitSeconds => emptyWaitSeconds;
        public static float SettleGraceSeconds => settleGraceSeconds;
        // Seconds the current spot has been reported empty; -1 when inactive or not counting.
        public static float EmptyElapsedSeconds => active && emptySinceAt >= 0f ? Time.unscaledTime - emptySinceAt : -1f;
        public static bool StartNearest => startNearest;
        public static bool LegacyMigrated => legacyMigrated;

        public static void SetEmptyWaitSeconds(float seconds)
        {
            emptyWaitSeconds = Mathf.Clamp(seconds, MinEmptyWaitSeconds, MaxEmptyWaitSeconds);
        }

        public static void SetSettleGraceSeconds(float seconds)
        {
            settleGraceSeconds = Mathf.Clamp(seconds, MinSettleGraceSeconds, MaxSettleGraceSeconds);
        }

        public static void SetStartNearest(bool value)
        {
            startNearest = value;
        }

        public static void SetLegacyMigrated(bool value)
        {
            legacyMigrated = value;
        }

        public static string CurrentCategoryName =>
            routeIndex >= 0 && routeIndex < flattened.Count ? flattened[routeIndex].categoryName : "";

        public static string CurrentSpotName =>
            routeIndex >= 0 && routeIndex < flattened.Count ? flattened[routeIndex].spotName : "";

        // ---- Category store -------------------------------------------------------------------

        public static IReadOnlyList<HeartopiaComplete.LocationCategoryEntry> Categories => categories;

        public static void ImportCategories(List<HeartopiaComplete.LocationCategoryEntry> entries)
        {
            categories.Clear();
            if (entries != null)
            {
                foreach (HeartopiaComplete.LocationCategoryEntry entry in entries)
                {
                    HeartopiaComplete.LocationCategoryEntry clone = CloneCategory(entry, false);
                    if (clone != null)
                    {
                        categories.Add(clone);
                    }
                }
            }

            // A non-empty saved list means the legacy fishing migrate already happened (or the
            // user built categories by hand) — never re-seed defaults over an intentional empty list.
            if (categories.Count > 0)
            {
                legacyMigrated = true;
            }

            RebuildFlattened();
        }

        public static List<HeartopiaComplete.LocationCategoryEntry> ExportCategories()
        {
            var result = new List<HeartopiaComplete.LocationCategoryEntry>(categories.Count);
            foreach (HeartopiaComplete.LocationCategoryEntry entry in categories)
            {
                HeartopiaComplete.LocationCategoryEntry clone = CloneCategory(entry, false);
                if (clone != null)
                {
                    result.Add(clone);
                }
            }

            return result;
        }

        // Clones of the enabled categories only, for a later file export (Task 7).
        public static List<HeartopiaComplete.LocationCategoryEntry> ExportEnabledCategories()
        {
            var result = new List<HeartopiaComplete.LocationCategoryEntry>();
            foreach (HeartopiaComplete.LocationCategoryEntry entry in categories)
            {
                if (entry != null && entry.enabledOnRoute)
                {
                    HeartopiaComplete.LocationCategoryEntry clone = CloneCategory(entry, false);
                    if (clone != null)
                    {
                        result.Add(clone);
                    }
                }
            }

            return result;
        }

        // Merge by id: an imported category with an existing id replaces that category in place
        // (name, enabled flag and spots); an unknown or empty id is appended, and an appended name
        // that collides with an existing one (case-insensitive) gets the " (imported)" suffix.
        public static void MergeImported(List<HeartopiaComplete.LocationCategoryEntry> imported, out int replaced, out int added)
        {
            replaced = 0;
            added = 0;
            if (imported != null)
            {
                foreach (HeartopiaComplete.LocationCategoryEntry entry in imported)
                {
                    HeartopiaComplete.LocationCategoryEntry clone = CloneCategory(entry, false);
                    if (clone == null)
                    {
                        continue;
                    }

                    int index = FindCategoryIndex(clone.id);
                    if (index >= 0)
                    {
                        categories[index] = clone;
                        replaced++;
                        continue;
                    }

                    clone.name = UniqueImportedName(clone.name);
                    categories.Add(clone);
                    added++;
                }
            }

            RebuildFlattened();
        }

        private static string UniqueImportedName(string baseName)
        {
            string candidate = baseName ?? "";
            if (!CategoryNameExists(candidate))
            {
                return candidate;
            }

            string withSuffix = candidate + ImportedNameSuffix;
            if (!CategoryNameExists(withSuffix))
            {
                return withSuffix;
            }

            int n = 2;
            while (CategoryNameExists(withSuffix + " " + n))
            {
                n++;
            }

            return withSuffix + " " + n;
        }

        // ---- Export / import ------------------------------------------------------------------

        private const int PayloadVersion = 1;
        private const string ImportedNameSuffix = " (imported)";
        private const string ExportFileName = "location-categories-export.json";

        // JsonUtility cannot serialize List<> and, under IL2CPP interop, not managed types at all,
        // so the payload goes through System.Text.Json with array-based mirror types.
        private sealed class JsonSpot
        {
            public string name;
            public float x;
            public float y;
            public float z;
        }

        private sealed class JsonCategory
        {
            public string id;
            public string name;
            public bool enabledOnRoute;
            public JsonSpot[] spots;
        }

        private sealed class ExportPayload
        {
            public int bugtopiaLocationCategories;
            public string exportedAt;
            public JsonCategory[] categories;
        }

        private static readonly JsonSerializerOptions payloadOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,
        };

        // Enabled-on-route categories only -> clipboard and %LocalLow%\Bugtopia\location-categories-export.json.
        public static bool TryExportSelected(HeartopiaComplete host, out string error)
        {
            error = null;
            try
            {
                List<HeartopiaComplete.LocationCategoryEntry> selected = ExportEnabledCategories();
                if (selected.Count == 0)
                {
                    error = "No categories enabled on route";
                    return false;
                }

                var payload = new ExportPayload
                {
                    bugtopiaLocationCategories = PayloadVersion,
                    exportedAt = DateTime.UtcNow.ToString("o"),
                    categories = new JsonCategory[selected.Count],
                };
                for (int i = 0; i < selected.Count; i++)
                {
                    payload.categories[i] = ToJson(selected[i]);
                }

                string json = JsonSerializer.Serialize(payload, payloadOptions);

                bool clipboardOk = false;
                bool fileOk = false;
                string failure = null;
                try
                {
                    GUIUtility.systemCopyBuffer = json;
                    clipboardOk = true;
                }
                catch (Exception ex)
                {
                    failure = "clipboard: " + ex.Message;
                }

                try
                {
                    File.WriteAllText(HelperPaths.GetFile(ExportFileName), json);
                    fileOk = true;
                }
                catch (Exception ex)
                {
                    failure = (failure == null ? "" : failure + "; ") + "file: " + ex.Message;
                }

                if (!clipboardOk && !fileOk)
                {
                    error = failure ?? "Export failed";
                    return false;
                }

                if (clipboardOk && fileOk)
                {
                    Notify(host, "Exported " + selected.Count + " categories (clipboard + file)", new Color(0.45f, 1f, 0.55f));
                }
                else
                {
                    Notify(host,
                        "Exported " + selected.Count + " categories ("
                        + (clipboardOk ? "clipboard only" : "file only")
                        + "; other failed" + (failure != null ? ": " + failure : "") + ")",
                        new Color(1f, 0.75f, 0.35f));
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[GatherLocations] export failed: " + ex.Message);
                error = ex.Message;
                return false;
            }
        }

        // Clipboard when it holds a valid versioned payload, otherwise the export file; merged by id.
        public static bool TryImport(HeartopiaComplete host, out string error)
        {
            error = null;
            try
            {
                string clipboard = null;
                try { clipboard = GUIUtility.systemCopyBuffer; } catch { }

                ExportPayload payload = null;
                string source = null;
                if (TryParsePayload(clipboard, out payload))
                {
                    source = "clipboard";
                }
                else
                {
                    string path = HelperPaths.GetFile(ExportFileName);
                    if (!File.Exists(path))
                    {
                        error = "No valid payload in clipboard and no export file";
                        return false;
                    }

                    if (!TryParsePayload(File.ReadAllText(path), out payload))
                    {
                        error = "Invalid payload in clipboard and export file";
                        return false;
                    }

                    source = "file";
                }

                var entries = new List<HeartopiaComplete.LocationCategoryEntry>(payload.categories.Length);
                foreach (JsonCategory category in payload.categories)
                {
                    HeartopiaComplete.LocationCategoryEntry entry = FromJson(category);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }

                MergeImported(entries, out int replaced, out int added);
                // UI calls SaveKeybinds after a successful import — do not save twice here.
                Notify(host, "Imported from " + source + ": " + added + " added, " + replaced + " replaced", new Color(0.45f, 1f, 0.55f));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[GatherLocations] import failed: " + ex.Message);
                error = ex.Message;
                return false;
            }
        }

        private static bool TryParsePayload(string text, out ExportPayload payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                ExportPayload parsed = JsonSerializer.Deserialize<ExportPayload>(text, payloadOptions);
                if (parsed == null || parsed.bugtopiaLocationCategories != PayloadVersion || parsed.categories == null)
                {
                    return false;
                }

                payload = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static JsonCategory ToJson(HeartopiaComplete.LocationCategoryEntry source)
        {
            int count = source.spots != null ? source.spots.Count : 0;
            var result = new JsonCategory
            {
                id = source.id,
                name = source.name,
                enabledOnRoute = source.enabledOnRoute,
                spots = new JsonSpot[count],
            };
            for (int i = 0; i < count; i++)
            {
                HeartopiaComplete.LocationSpotEntry spot = source.spots[i];
                result.spots[i] = new JsonSpot { name = spot.name, x = spot.x, y = spot.y, z = spot.z };
            }

            return result;
        }

        private static HeartopiaComplete.LocationCategoryEntry FromJson(JsonCategory source)
        {
            if (source == null)
            {
                return null;
            }

            var entry = new HeartopiaComplete.LocationCategoryEntry
            {
                id = source.id ?? "",
                name = source.name ?? "",
                enabledOnRoute = source.enabledOnRoute,
            };
            if (source.spots != null)
            {
                foreach (JsonSpot spot in source.spots)
                {
                    if (spot != null)
                    {
                        entry.spots.Add(new HeartopiaComplete.LocationSpotEntry { name = spot.name ?? "", x = spot.x, y = spot.y, z = spot.z });
                    }
                }
            }

            return entry;
        }

        private static bool CategoryNameExists(string name)
        {
            foreach (HeartopiaComplete.LocationCategoryEntry category in categories)
            {
                if (category != null && string.Equals(category.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // First-run seed: the built-in fishing list plus the user's old custom fishing spots.
        public static void SeedFromLegacyFishing(List<HeartopiaComplete.CustomTeleportEntry> customOrNull)
        {
            categories.Clear();

            var fishing = new HeartopiaComplete.LocationCategoryEntry
            {
                id = Guid.NewGuid().ToString("N"),
                name = "Fishing",
                enabledOnRoute = true,
            };
            foreach (FixedSpot spot in DefaultFishingSpots)
            {
                fishing.spots.Add(MakeSpot(spot.name, spot.pos));
            }

            categories.Add(fishing);

            if (customOrNull != null && customOrNull.Count > 0)
            {
                var custom = new HeartopiaComplete.LocationCategoryEntry
                {
                    id = Guid.NewGuid().ToString("N"),
                    name = "Custom Fishing",
                    enabledOnRoute = true,
                };
                foreach (HeartopiaComplete.CustomTeleportEntry entry in customOrNull)
                {
                    if (entry != null)
                    {
                        custom.spots.Add(MakeSpot(SanitizeName(entry.name, "Custom " + (custom.spots.Count + 1)), entry.position));
                    }
                }

                if (custom.spots.Count > 0)
                {
                    categories.Add(custom);
                }
            }

            legacyMigrated = true;
            RebuildFlattened();
        }

        public static string AddCategory(string name)
        {
            string id = Guid.NewGuid().ToString("N");
            categories.Add(new HeartopiaComplete.LocationCategoryEntry
            {
                id = id,
                name = SanitizeName(name, "Category " + (categories.Count + 1)),
                enabledOnRoute = true,
            });
            RebuildFlattened();
            return id;
        }

        public static void RenameCategory(string id, string name)
        {
            HeartopiaComplete.LocationCategoryEntry category = FindCategory(id);
            if (category == null)
            {
                return;
            }

            category.name = SanitizeName(name, category.name);
            RebuildFlattened();
        }

        public static void DeleteCategory(string id)
        {
            int index = FindCategoryIndex(id);
            if (index < 0)
            {
                return;
            }

            categories.RemoveAt(index);
            RebuildFlattened();
        }

        public static void SetCategoryEnabled(string id, bool enabled)
        {
            HeartopiaComplete.LocationCategoryEntry category = FindCategory(id);
            if (category == null || category.enabledOnRoute == enabled)
            {
                return;
            }

            category.enabledOnRoute = enabled;
            RebuildFlattened();
        }

        public static void MoveCategory(string id, int delta)
        {
            int index = FindCategoryIndex(id);
            int target = index + (delta < 0 ? -1 : delta > 0 ? 1 : 0);
            if (index < 0 || target == index || target < 0 || target >= categories.Count)
            {
                return;
            }

            HeartopiaComplete.LocationCategoryEntry moved = categories[index];
            categories[index] = categories[target];
            categories[target] = moved;
            RebuildFlattened();
        }

        public static bool AddSpotAtPlayer(HeartopiaComplete host, string categoryId)
        {
            HeartopiaComplete.LocationCategoryEntry category = FindCategory(categoryId);
            if (category == null || host == null)
            {
                return false;
            }

            if (!host.TryGetLocalPlayerPosition(out Vector3 pos))
            {
                try { host.UI_AddMenuNotification(host.UI_Localize("Player unavailable"), new Color(1f, 0.55f, 0.55f)); } catch { }
                return false;
            }

            string name = "Spot " + (category.spots.Count + 1);
            category.spots.Add(MakeSpot(name, pos));
            RebuildFlattened();
            try { host.UI_AddMenuNotification(host.UI_Localize("Spot saved: ") + name, new Color(0.45f, 1f, 0.55f)); } catch { }
            return true;
        }

        public static void RemoveSpot(string categoryId, int spotIndex)
        {
            HeartopiaComplete.LocationCategoryEntry category = FindCategory(categoryId);
            if (category == null || spotIndex < 0 || spotIndex >= category.spots.Count)
            {
                return;
            }

            category.spots.RemoveAt(spotIndex);
            RebuildFlattened();
        }

        public static void MoveSpot(string categoryId, int spotIndex, int delta)
        {
            HeartopiaComplete.LocationCategoryEntry category = FindCategory(categoryId);
            if (category == null)
            {
                return;
            }

            int target = spotIndex + (delta < 0 ? -1 : delta > 0 ? 1 : 0);
            if (spotIndex < 0 || spotIndex >= category.spots.Count || target == spotIndex || target < 0 || target >= category.spots.Count)
            {
                return;
            }

            HeartopiaComplete.LocationSpotEntry moved = category.spots[spotIndex];
            category.spots[spotIndex] = category.spots[target];
            category.spots[target] = moved;
            RebuildFlattened();
        }

        // Enabled categories in list order, their spots in order. Keeps the route pointer on the
        // same spot when the list changes under an active route (falls back to a clamp).
        public static void RebuildFlattened()
        {
            string keepCategory = null;
            string keepSpot = null;
            Vector3 keepPos = Vector3.zero;
            bool hadCurrent = active && routeIndex >= 0 && routeIndex < flattened.Count;
            if (hadCurrent)
            {
                FlatSpot cur = flattened[routeIndex];
                keepCategory = cur.categoryId;
                keepSpot = cur.spotName;
                keepPos = cur.pos;
            }

            flattened.Clear();
            foreach (HeartopiaComplete.LocationCategoryEntry category in categories)
            {
                if (category == null || !category.enabledOnRoute || category.spots == null)
                {
                    continue;
                }

                foreach (HeartopiaComplete.LocationSpotEntry spot in category.spots)
                {
                    if (spot == null)
                    {
                        continue;
                    }

                    flattened.Add(new FlatSpot
                    {
                        categoryId = category.id,
                        categoryName = category.name ?? "",
                        spotName = spot.name ?? "",
                        pos = new Vector3(spot.x, spot.y, spot.z),
                    });
                }
            }

            if (hadCurrent)
            {
                int found = -1;
                for (int i = 0; i < flattened.Count; i++)
                {
                    if (flattened[i].categoryId == keepCategory && flattened[i].spotName == keepSpot && flattened[i].pos == keepPos)
                    {
                        found = i;
                        break;
                    }
                }

                if (found >= 0)
                {
                    routeIndex = found;
                }
                else
                {
                    // Current spot vanished (disabled/deleted/reordered mid-run) — retarget next tick.
                    pendingRetarget = true;
                }
            }

            if (routeIndex >= flattened.Count)
            {
                routeIndex = 0;
                if (active && flattened.Count > 0)
                {
                    pendingRetarget = true;
                }
            }
        }

        // ---- Lifecycle ------------------------------------------------------------------------

        public static void Start(HeartopiaComplete host)
        {
            if (active || host == null)
            {
                return;
            }

            CancelOneShot(host, "route start");
            RebuildFlattened();
            if (flattened.Count == 0)
            {
                Notify(host, "Gather Locations: no spots enabled", new Color(1f, 0.65f, 0.45f));
                return;
            }

            if (!AnyFarmEnabled())
            {
                Notify(host, "Gather Locations: enable Fish, Insect or Bird farming first", new Color(1f, 0.65f, 0.45f));
                return;
            }

            active = true;
            worldNotReadySince = -1f;
            emptySinceAt = -1f;
            retargetAfterWorld = false;
            pendingRetarget = false;
            routeIndex = startNearest ? FindNearestSpotIndex(host) : 0;
            ResetScanCursors();
            lastStatus = "Starting";
            RouteTravelHelper.GoTo(host, travelCb, routeIndex, flattened[routeIndex].pos);
            FeatureLog.Life("GatherLocations", "route started: " + flattened.Count + " spot(s)");
            Log("Route started: " + flattened.Count + " spots");
        }

        public static void Stop(HeartopiaComplete host)
        {
            CancelOneShot(host, "route stopped");
            if (!active)
            {
                return;
            }

            active = false;
            emptySinceAt = -1f;
            worldNotReadySince = -1f;
            retargetAfterWorld = false;
            pendingRetarget = false;
            lastStatus = "Idle";
            try { RouteTravelHelper.End(host, travelCb, "route stopped"); } catch (Exception ex) { ModLogger.Msg("[GatherLocations] travel end threw: " + ex.Message); }
            FeatureLog.Life("GatherLocations", "route stopped");
        }

        // Used by disable-all / stop-all paths.
        public static void ForceStop(HeartopiaComplete host)
        {
            CancelOneShot(host, "force stop");
            Stop(host);
        }

        // UI "Go": one walk/teleport to that spot. Does not start or stop the route cycle.
        public static bool TryGoToSpot(HeartopiaComplete host, string categoryId, int spotIndex)
        {
            if (host == null)
            {
                return false;
            }

            HeartopiaComplete.LocationCategoryEntry category = FindCategory(categoryId);
            if (category == null || category.spots == null || spotIndex < 0 || spotIndex >= category.spots.Count)
            {
                return false;
            }

            HeartopiaComplete.LocationSpotEntry spot = category.spots[spotIndex];
            if (spot == null)
            {
                return false;
            }

            if (!host.IsFishingAutomationWorldReady())
            {
                Notify(host, "Gather Locations: world not ready", new Color(1f, 0.65f, 0.45f));
                return false;
            }

            // Interrupt whatever travel is in flight (route hop or a previous Go).
            if (RouteTravelHelper.Travelling)
            {
                try { RouteTravelHelper.End(host, oneShotActive ? (RouteTravelHelper.ICallbacks)oneShotCb : travelCb, "go to spot"); }
                catch (Exception ex) { ModLogger.Msg("[GatherLocations] travel end threw: " + ex.Message); }
            }

            oneShotActive = true;
            oneShotPos = new Vector3(spot.x, spot.y, spot.z);
            oneShotName = string.IsNullOrEmpty(spot.name) ? ("Spot " + (spotIndex + 1)) : spot.name;
            lastStatus = "Going to " + oneShotName;
            RouteTravelHelper.GoOnce(host, oneShotCb, oneShotPos);
            FeatureLog.Life("GatherLocations", "go once to '" + oneShotName + "'");
            return true;
        }

        private static void CancelOneShot(HeartopiaComplete host, string why)
        {
            if (!oneShotActive)
            {
                return;
            }

            oneShotActive = false;
            try { RouteTravelHelper.End(host, oneShotCb, why); } catch { }
        }

        private static void FinishOneShot(string status)
        {
            oneShotActive = false;
            lastStatus = status;
        }

        public static void Update(HeartopiaComplete host)
        {
            if (host == null)
            {
                return;
            }

            float now = Time.unscaledTime;

            if (oneShotActive)
            {
                if (RouteTravelHelper.Travelling)
                {
                    RouteTravelHelper.Tick(host, oneShotCb, now);
                }
                else
                {
                    // Arrived / ended without the callback (defensive).
                    oneShotActive = false;
                }

                // Route cycle waits while a manual Go is in flight.
                return;
            }

            if (!active)
            {
                return;
            }

            if (!host.IsFishingAutomationWorldReady())
            {
                if (worldNotReadySince < 0f)
                {
                    worldNotReadySince = now;
                }
                else if (now - worldNotReadySince >= WorldUnavailableStopSeconds)
                {
                    Stop(host);
                    Notify(host, "Gather Locations stopped (world unavailable)", new Color(1f, 0.65f, 0.45f));
                    return;
                }

                // Cached positions belong to the map that is going away.
                if (RouteTravelHelper.Travelling)
                {
                    RouteTravelHelper.End(host, travelCb, "world is changing");
                    retargetAfterWorld = true;
                }

                lastStatus = "Waiting for world";
                return;
            }

            worldNotReadySince = -1f;

            if (retargetAfterWorld || pendingRetarget)
            {
                retargetAfterWorld = false;
                pendingRetarget = false;
                if (flattened.Count == 0)
                {
                    Stop(host);
                    Notify(host, "Gather Locations stopped (no spots)", new Color(1f, 0.65f, 0.45f));
                    return;
                }

                if (routeIndex >= flattened.Count)
                {
                    routeIndex = startNearest ? FindNearestSpotIndex(host) : 0;
                }

                RouteTravelHelper.GoTo(host, travelCb, routeIndex, flattened[routeIndex].pos);
                return;
            }

            if (flattened.Count == 0)
            {
                Stop(host);
                Notify(host, "Gather Locations stopped (no spots)", new Color(1f, 0.65f, 0.45f));
                return;
            }

            if (!AnyFarmEnabled())
            {
                Stop(host);
                Notify(host, "Gather Locations stopped (no farm enabled)", new Color(1f, 0.65f, 0.45f));
                return;
            }

            if (routeIndex >= flattened.Count)
            {
                routeIndex = 0;
            }

            if (RouteTravelHelper.Travelling)
            {
                RouteTravelHelper.Tick(host, travelCb, now);
                return;
            }

            // Another farm slice owns the tool: freeze (the empty clock keeps its value).
            if (!CombinedFarmFeature.AllowsRouteHop)
            {
                lastStatus = "Paused (another farm is running)";
                return;
            }

            if (now < graceUntil)
            {
                lastStatus = "Arriving at spot";
                return;
            }

            PollScans();

            // Never hop mid-session; the empty window keeps whatever value it had.
            if (IsSessionBusy())
            {
                lastStatus = "Working";
                return;
            }

            // Wait until every currently-scanning farm scanned this spot at least once.
            if (!AllWatchedFarmsScanned())
            {
                lastStatus = "Waiting for first scan";
                return;
            }

            if (emptySinceAt < 0f)
            {
                lastStatus = "Working";
                return;
            }

            if (now - emptySinceAt < emptyWaitSeconds)
            {
                lastStatus = "Empty " + (now - emptySinceAt).ToString("F0") + "s / " + emptyWaitSeconds.ToString("F0") + "s";
                return;
            }

            // The fishing scan stops while energy-held, so the empty clock would run on wall time.
            if (AutoFishingFarm.IsEnabled && AutoFishingFarm.IsHeldForEnergy)
            {
                lastStatus = "Paused (out of energy)";
                return;
            }

            if (host.IsAutoRepairBusy())
            {
                lastStatus = "Paused (repair in progress)";
                return;
            }

            if (flattened.Count < 2)
            {
                // Keep the sticky status — clearing emptySinceAt made this flicker to "Working".
                lastStatus = "Only one spot";
                return;
            }

            routeIndex = (routeIndex + 1) % flattened.Count;
            emptySinceAt = -1f;
            RouteTravelHelper.GoTo(host, travelCb, routeIndex, flattened[routeIndex].pos);
        }

        // ---- Internals ------------------------------------------------------------------------

        private static bool IsFarmEnabled(int farm)
        {
            switch (farm)
            {
                case FarmFish: return AutoFishingFarm.IsEnabled;
                case FarmInsect: return InsectNetFarm.IsEnabled;
                case FarmBird: return BirdNetFarm.IsEnabled;
                default: return false;
            }
        }

        // Combined Farm suspends the farms that do not own the tool — those ticks never write
        // LastScanAt. Waiting on them stalls the route on "Waiting for first scan". Only farms
        // that are actually scanning this frame gate the empty window.
        private static bool IsFarmWatched(int farm)
        {
            switch (farm)
            {
                case FarmFish: return AutoFishingFarm.IsEnabled;
                case FarmInsect: return InsectNetFarm.IsEnabled && !InsectNetFarm.IsSuspended;
                case FarmBird: return BirdNetFarm.IsEnabled && !BirdNetFarm.IsSuspended;
                default: return false;
            }
        }

        private static float FarmLastScanAt(int farm)
        {
            switch (farm)
            {
                case FarmFish: return AutoFishingFarm.LastScanAt;
                case FarmInsect: return InsectNetFarm.LastScanAt;
                default: return BirdNetFarm.LastScanAt;
            }
        }

        private static int FarmLastInRangeCount(int farm)
        {
            switch (farm)
            {
                case FarmFish: return AutoFishingFarm.LastInRangeCount;
                case FarmInsect: return InsectNetFarm.LastInRangeCount;
                default: return BirdNetFarm.LastInRangeCount;
            }
        }

        private static bool AnyFarmEnabled()
        {
            for (int i = 0; i < FarmCount; i++)
            {
                if (IsFarmEnabled(i))
                {
                    return true;
                }
            }

            return false;
        }

        // Enabled Fish / Bird hold the route (mid-hop and at the spot). Insects do not.
        // Gated on IsEnabled, not on who holds the tool: a suspended slice still counts when the
        // coordinator census is fresh. A solo farm uses its own recent scan instead.
        private static bool IsSessionBusy()
        {
            return IsFishRouteBusy() || IsBirdRouteBusy();
        }

        private static bool IsFishRouteBusy()
        {
            if (!AutoFishingFarm.IsEnabled)
            {
                return false;
            }

            return AutoFishingFarm.IsInFishingSession || HasFreshInRangeTargets(FarmFish);
        }

        private static bool IsBirdRouteBusy()
        {
            if (!BirdNetFarm.IsEnabled)
            {
                return false;
            }

            return BirdNetFarm.HasPendingConfirms || HasFreshInRangeTargets(FarmBird);
        }

        private static bool HasFreshInRangeTargets(int farm)
        {
            if (CombinedFarmFeature.IsCoordinating)
            {
                CombinedFarmFeature.TryGetGatherRoutePresence(
                    out int fishInRange, out int birdsInRange, out bool fishFresh, out bool birdsFresh);
                if (farm == FarmFish)
                {
                    return fishFresh && fishInRange > 0;
                }

                if (farm == FarmBird)
                {
                    return birdsFresh && birdsInRange > 0;
                }

                return false;
            }

            if (farm == FarmFish)
            {
                return IsSoloScanFresh(AutoFishingFarm.IsSuspended, AutoFishingFarm.LastScanAt)
                    && AutoFishingFarm.LastInRangeCount > 0;
            }

            if (farm == FarmBird)
            {
                return IsSoloScanFresh(BirdNetFarm.IsSuspended, BirdNetFarm.LastScanAt)
                    && BirdNetFarm.LastInRangeCount > 0;
            }

            return false;
        }

        private static bool IsSoloScanFresh(bool suspended, float lastScanAt)
        {
            return !suspended
                && lastScanAt > 0f
                && Time.unscaledTime - lastScanAt <= SoloScanFreshSeconds;
        }

        private static void ResetScanCursors()
        {
            for (int i = 0; i < FarmCount; i++)
            {
                lastHandledScanAt[i] = -999f;
                scannedSinceArrival[i] = false;
                emptyAtLastScan[i] = false;
            }
        }

        // A scan counts once, and only when it ran after arrival. The window is "every watched
        // farm's latest scan was empty": any in-range target on any watched farm resets it; it
        // starts at wall-clock once grace has ended and every watched farm reports empty.
        private static void PollScans()
        {
            bool anyNew = false;
            for (int i = 0; i < FarmCount; i++)
            {
                if (!IsFarmWatched(i))
                {
                    continue;
                }

                float scanAt = FarmLastScanAt(i);
                if (scanAt < spotArrivedAt || scanAt <= lastHandledScanAt[i])
                {
                    continue;
                }

                lastHandledScanAt[i] = scanAt;
                scannedSinceArrival[i] = true;
                emptyAtLastScan[i] = FarmLastInRangeCount(i) <= 0;
                anyNew = true;
            }

            if (!anyNew)
            {
                return;
            }

            bool allEmpty = true;
            for (int i = 0; i < FarmCount; i++)
            {
                if (!IsFarmWatched(i))
                {
                    continue;
                }

                if (scannedSinceArrival[i] && !emptyAtLastScan[i])
                {
                    allEmpty = false;
                    break;
                }
            }

            if (!allEmpty)
            {
                emptySinceAt = -1f;
            }
            else if (emptySinceAt < 0f && AllWatchedFarmsScanned())
            {
                // Wall clock — not the scan timestamp (that often sits inside the settle grace).
                emptySinceAt = Time.unscaledTime;
            }
        }

        private static bool AllWatchedFarmsScanned()
        {
            bool anyWatched = false;
            for (int i = 0; i < FarmCount; i++)
            {
                if (!IsFarmWatched(i))
                {
                    continue;
                }

                anyWatched = true;
                if (!scannedSinceArrival[i])
                {
                    return false;
                }
            }

            // No watched farm this frame (all enabled farms suspended): do not hop yet.
            return anyWatched;
        }

        private static void MarkArrived(int index, string status)
        {
            float now = Time.unscaledTime;
            routeIndex = index;
            spotArrivedAt = now;
            graceUntil = now + settleGraceSeconds;
            emptySinceAt = -1f;
            ResetScanCursors();
            lastStatus = status;
        }

        // Opens at the spot nearest the player; flat distance, unknown position = index 0.
        private static int FindNearestSpotIndex(HeartopiaComplete host)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < flattened.Count; i++)
            {
                float d = host.FishingRouteDistanceTo(flattened[i].pos);
                if (d >= 0f && d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            if (bestDist < float.MaxValue)
            {
                ModLogger.Msg("[GatherLocations] starting at the nearest spot " + (best + 1) + "/" + flattened.Count + " '"
                    + flattened[best].spotName + "' (" + bestDist.ToString("F0") + "m away).");
            }

            return best;
        }

        private static HeartopiaComplete.LocationCategoryEntry FindCategory(string id)
        {
            int index = FindCategoryIndex(id);
            return index >= 0 ? categories[index] : null;
        }

        private static int FindCategoryIndex(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return -1;
            }

            for (int i = 0; i < categories.Count; i++)
            {
                if (categories[i] != null && categories[i].id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private static HeartopiaComplete.LocationSpotEntry MakeSpot(string name, Vector3 pos)
        {
            return new HeartopiaComplete.LocationSpotEntry { name = SanitizeName(name, "Spot"), x = pos.x, y = pos.y, z = pos.z };
        }

        // Config is hand-written XML/JSON elsewhere; keep quotes and backslashes out of names.
        private static string SanitizeName(string name, string fallback)
        {
            string clean = (name ?? "").Replace("\"", "").Replace("\\", "").Trim();
            return clean.Length > 0 ? clean : fallback;
        }

        private static HeartopiaComplete.LocationCategoryEntry CloneCategory(HeartopiaComplete.LocationCategoryEntry source, bool freshId)
        {
            if (source == null)
            {
                return null;
            }

            var clone = new HeartopiaComplete.LocationCategoryEntry
            {
                id = freshId || string.IsNullOrEmpty(source.id) ? Guid.NewGuid().ToString("N") : source.id,
                name = SanitizeName(source.name, "Category"),
                enabledOnRoute = source.enabledOnRoute,
            };
            if (source.spots != null)
            {
                foreach (HeartopiaComplete.LocationSpotEntry spot in source.spots)
                {
                    if (spot != null)
                    {
                        clone.spots.Add(new HeartopiaComplete.LocationSpotEntry { name = SanitizeName(spot.name, "Spot"), x = spot.x, y = spot.y, z = spot.z });
                    }
                }
            }

            return clone;
        }

        private static void Notify(HeartopiaComplete host, string message, Color color)
        {
            try { host?.UI_AddMenuNotification(host.UI_Localize(message), color); } catch { }
        }

        private static void Log(string message)
        {
            if (!HeartopiaComplete.MasterLogAutoFish)
            {
                return;
            }

            ModLogger.Msg("[GatherLocations] " + message);
        }

        private sealed class TravelCb : RouteTravelHelper.ICallbacks
        {
            public bool IsSessionBusy() => GatherLocationsFeature.IsSessionBusy();

            public bool AllowsHop() => CombinedFarmFeature.AllowsRouteHop;

            public void OnArrived(int index, string status) => MarkArrived(index, status);

            // Notification only: the helper advances to (failedIndex + 1) % total itself and
            // calls OnArrived / the walker for it. Sync the pointer, never call GoTo from here.
            public void OnSkipToNext(int failedIndex, string why)
            {
                int total = flattened.Count;
                if (total > 0)
                {
                    routeIndex = (failedIndex + 1) % total;
                }
            }

            public string SpotName(int index) =>
                index >= 0 && index < flattened.Count ? flattened[index].spotName : "?";

            public Vector3 SpotPos(int index) =>
                index >= 0 && index < flattened.Count ? flattened[index].pos : Vector3.zero;

            public int TotalSpotCount() => flattened.Count;

            public bool WalkAvailable(HeartopiaComplete host) => host.FishingRouteWalkAvailable;

            public bool PrepareWalk(HeartopiaComplete host, out string why) => host.FishingRoutePrepareWalk(out why);

            public bool BeginWalk(HeartopiaComplete host, Vector3 target, string name) => host.FishingRouteBeginWalk(target, name);

            public void AbortWalk(HeartopiaComplete host) => host.FishingRouteAbortWalk();

            public bool TickWalk(HeartopiaComplete host) => host.FishingRouteTickWalk();

            public float DistanceTo(HeartopiaComplete host, Vector3 to) => host.FishingRouteDistanceTo(to);

            public void TeleportTo(HeartopiaComplete host, Vector3 pos) => host.TeleportToLocationWithOffset(pos, 0f);

            public bool IsRepairBusy(HeartopiaComplete host) => host.IsAutoRepairBusy();
        }

        // Single-spot Go button: no session/hop freezes, no route index updates.
        private sealed class OneShotTravelCb : RouteTravelHelper.ICallbacks
        {
            public bool IsSessionBusy() => false;

            public bool AllowsHop() => true;

            public void OnArrived(int index, string status)
            {
                FinishOneShot(status);
                ModLogger.Msg("[GatherLocations] arrived at '" + oneShotName + "' (go once).");
            }

            public void OnSkipToNext(int failedIndex, string why)
            {
                FinishOneShot("Go failed: " + why);
                ModLogger.Msg("[GatherLocations] go once failed for '" + oneShotName + "': " + why);
            }

            public string SpotName(int index) => oneShotName;

            public Vector3 SpotPos(int index) => oneShotPos;

            public int TotalSpotCount() => 1;

            public bool WalkAvailable(HeartopiaComplete host) => host.FishingRouteWalkAvailable;

            public bool PrepareWalk(HeartopiaComplete host, out string why) => host.FishingRoutePrepareWalk(out why);

            public bool BeginWalk(HeartopiaComplete host, Vector3 target, string name) => host.FishingRouteBeginWalk(target, name);

            public void AbortWalk(HeartopiaComplete host) => host.FishingRouteAbortWalk();

            public bool TickWalk(HeartopiaComplete host) => host.FishingRouteTickWalk();

            public float DistanceTo(HeartopiaComplete host, Vector3 to) => host.FishingRouteDistanceTo(to);

            public void TeleportTo(HeartopiaComplete host, Vector3 pos) => host.TeleportToLocationWithOffset(pos, 0f);

            public bool IsRepairBusy(HeartopiaComplete host) => host.IsAutoRepairBusy();
        }
    }
}
