using System;
using System.Collections.Generic;

namespace HeartopiaMod
{
    internal static class SandModelTie
    {
        internal const int Count = 14;

        internal static int Pick(
            int[] options,
            int[] collectById,
            bool[] readOk,
            int[] rarityById,
            string cursor,
            bool preferRare,
            out string tieKey,
            out bool advance)
        {
            tieKey = null;
            advance = false;
            if (options == null || options.Length == 0)
            {
                return 0;
            }

            int bestAny = 0;
            int bestValid = -1;
            int bestValidId = int.MaxValue;
            int validCount = 0;
            for (int i = 0; i < options.Length; i++)
            {
                int id = options[i];
                if (id < options[bestAny])
                {
                    bestAny = i;
                }
                if (id >= 1 && id <= Count)
                {
                    validCount++;
                    if (id < bestValidId)
                    {
                        bestValidId = id;
                        bestValid = i;
                    }
                }
            }

            if (validCount == 0)
            {
                return bestAny;
            }
            if (validCount == 1 || !preferRare)
            {
                return bestValid;
            }

            int bestCollect = -1;
            int bestRarity = -1;
            for (int i = 0; i < options.Length; i++)
            {
                int id = options[i];
                if (id < 1 || id > Count)
                {
                    continue;
                }
                int collect = ReadCollect(collectById, readOk, id);
                int rarity = RarityOf(rarityById, id);
                if (collect > bestCollect || (collect == bestCollect && rarity > bestRarity))
                {
                    bestCollect = collect;
                    bestRarity = rarity;
                }
            }

            var band = new List<int>();
            bool allRead = true;
            for (int i = 0; i < options.Length; i++)
            {
                int id = options[i];
                if (id < 1 || id > Count || band.Contains(id))
                {
                    continue;
                }
                if (ReadCollect(collectById, readOk, id) != bestCollect || RarityOf(rarityById, id) != bestRarity)
                {
                    continue;
                }
                band.Add(id);
                if (readOk == null || id >= readOk.Length || !readOk[id])
                {
                    allRead = false;
                }
            }

            band.Sort();
            int chosenId = band[0];
            if (band.Count >= 2 && allRead)
            {
                tieKey = string.Join("+", band);
                int last = LastOf(cursor, tieKey);
                int pos = band.IndexOf(last);
                chosenId = pos < 0 ? band[0] : band[(pos + 1) % band.Count];
                advance = true;
            }

            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] == chosenId)
                {
                    return i;
                }
            }
            return bestValid;
        }

        internal static string Remember(string cursor, string tieKey, int itemId)
        {
            if (string.IsNullOrEmpty(tieKey) || itemId < 1 || itemId > Count)
            {
                return cursor ?? string.Empty;
            }

            var map = Parse(cursor);
            map[tieKey] = itemId;
            var keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                parts.Add(keys[i] + "=" + map[keys[i]]);
            }
            return string.Join(",", parts);
        }

        private static int ReadCollect(int[] collectById, bool[] readOk, int id)
        {
            if (readOk == null || id >= readOk.Length || !readOk[id])
            {
                return 0;
            }
            if (collectById == null || id >= collectById.Length)
            {
                return 0;
            }
            int collect = collectById[id];
            if (collect < 0)
            {
                return 0;
            }
            return collect > 2 ? 2 : collect;
        }

        private static int RarityOf(int[] rarityById, int id)
        {
            if (rarityById == null || id >= rarityById.Length)
            {
                return 0;
            }
            return rarityById[id];
        }

        private static Dictionary<string, int> Parse(string cursor)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(cursor))
            {
                return map;
            }
            string[] parts = cursor.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                int eq = part.IndexOf('=');
                if (eq <= 0 || eq == part.Length - 1)
                {
                    continue;
                }
                string key = part.Substring(0, eq);
                if (!int.TryParse(part.Substring(eq + 1), out int id) || id < 1 || id > Count)
                {
                    continue;
                }
                if (!BandKeyMatches(key, id))
                {
                    continue;
                }
                map[key] = id;
            }
            return map;
        }

        private static bool BandKeyMatches(string key, int rememberedId)
        {
            string[] ids = key.Split('+');
            if (ids.Length < 2)
            {
                return false;
            }
            int previous = 0;
            bool seen = false;
            for (int i = 0; i < ids.Length; i++)
            {
                if (!int.TryParse(ids[i], out int id) || id < 1 || id > Count || id <= previous)
                {
                    return false;
                }
                previous = id;
                if (id == rememberedId)
                {
                    seen = true;
                }
            }
            return seen;
        }

        private static int LastOf(string cursor, string tieKey)
        {
            Dictionary<string, int> map = Parse(cursor);
            int id;
            return map.TryGetValue(tieKey, out id) ? id : 0;
        }
    }
}