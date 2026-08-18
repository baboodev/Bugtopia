using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    // Сборщик координат ресурсов из живого скана.
    //
    // ЗАЧЕМ ИМЕННО ТАК. Просили снять координаты из расшифрованных таблиц — их там нет. Проверены
    // все 913 таблиц на массивы из трёх float: единственная с координатами это WorldMapEventPos
    // (106 точек интерфейса карты — магазины, здания), остальное хитбоксы, границы участков и
    // векторы камеры. Дизайн-таблицы описывают, ЧТО за ресурс (`Dynamicbush`, `Entity`), а ГДЕ он
    // стоит — данные сцены Unity. Тот же случай, что с водоёмами рыбы.
    //
    // Живой скан знает позицию каждого ресурса, поэтому источник берётся оттуда. Накопление идёт
    // по мере того, как игрок ездит: всё, что подгрузилось, попадает в набор и остаётся там.
    //
    // ⚠️ ЭТО ПРЕВОСХОДИТ ЗАШИТЫЕ МАССИВЫ ПО ОХВАТУ. В них 238 точек восьми видов, собранных
    // вручную. Скан видит КАЖДЫЙ ресурс, включая те, которых в массивах не было вовсе — бамбук,
    // причудливые варианты грибов, событийные растения. Файл заодно и есть ответ на вопрос
    // «каких типов не хватало».
    public partial class HeartopiaComplete
    {
        // Отдельный тумблер: сбор идёт в файл и растёт весь сеанс, включать это по умолчанию
        // незачем.
        internal static bool MasterLogGatherHarvest = false;

        private const string GatherHarvestFileName = "gathered-coordinates.tsv";

        // Две точки ближе этого — один и тот же ресурс. Тот же порог, которым ферма считает узлы
        // совпадающими, чтобы наборы не разъезжались между системами.
        private const float GatherHarvestSameSpotDistance = 1.5f;

        // Пишем не на каждый скан: файл переписывается целиком, а сканы идут раз в 2 секунды.
        private const int GatherHarvestFlushEvery = 25;

        private readonly Dictionary<string, GatherHarvestEntry> gatherHarvest =
            new Dictionary<string, GatherHarvestEntry>();

        private int gatherHarvestSinceFlush;
        private bool gatherHarvestPathLogged;

        private struct GatherHarvestEntry
        {
            public int ItemId;      // разрешённый товарный id, 0 если ресурс его не несёт
            public int ProduceId;
            public int StaticId;    // entity staticId — единственная опора для грибов
            public Vector3 Position;
            public string Scene;    // активная сцена Unity — см. NoteGatherHarvest
        }

        // Зовётся из скана на каждую найденную сущность.
        internal void NoteGatherHarvest(Vector3 position, int produceId, int staticId, int itemId)
        {
            if (!MasterLogGatherHarvest)
            {
                return;
            }

            this.EnsureGatherHarvestLoaded();

            // ⚠️ СЦЕНА В КЛЮЧЕ. Без неё подводные зоны, микро-дом и суша сложились бы в один
            // список, а координаты у них в своих системах: запасной путь предложил бы донный
            // камень игроку, стоящему на берегу. Плюс два разных объекта из разных сцен могли бы
            // совпасть по клетке и затереть друг друга.
            string scene = string.Empty;
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? string.Empty; } catch { }

            // Ключ по КЛЕТКЕ, а не по точным координатам: позиция сущности слегка гуляет между
            // прогрузками, и без округления один куст дал бы десятки строк.
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


        // Загрузка ранее собранного набора — ОБЯЗАТЕЛЬНА, а не удобство.
        //
        // ⚠️ ГРИБЫ (и всё динамическое) СПАВНЯТСЯ НЕ ВЕЗДЕ ОДНОВРЕМЕННО. Точек спавна больше, чем
        // грибов в мире в любой момент: один обход карты дал 64 гриба на пять видов, и это ВЫБОРКА,
        // а не полный список мест. Полный набор набирается только повторными обходами.
        //
        // Без этой загрузки сборщик начинал с пустого словаря и переписывал файл целиком, то есть
        // ВТОРАЯ прогулка стирала результат первой — и набор не мог сойтись в принципе. Теперь
        // прошлое содержимое поднимается в память и новые точки добавляются к нему.
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

        // Один расчёт ключа для записи и для чтения: разойдись они, загруженная точка не совпала бы
        // с той же точкой из скана и набор бы удваивался с каждым запуском.
        private static string BuildGatherHarvestKey(string scene, int staticId, int produceId, Vector3 position)
        {
            int cx = Mathf.RoundToInt(position.x / GatherHarvestSameSpotDistance);
            int cy = Mathf.RoundToInt(position.y / GatherHarvestSameSpotDistance);
            int cz = Mathf.RoundToInt(position.z / GatherHarvestSameSpotDistance);
            return (scene ?? string.Empty) + ":" + staticId + ":" + produceId + ":" + cx + ":" + cy + ":" + cz;
        }

        // Полная перезапись — набор целиком живёт в памяти, дописывать нечего, а перезапись
        // переживает вылет игры лучше, чем незакрытый поток.
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
