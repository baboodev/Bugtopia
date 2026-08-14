using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Планировщик обхода узлов ("тур").
    //
    // ЗАЧЕМ. Раньше ферма на каждом скане брала БЛИЖАЙШИЙ узел. Для телепорта это было неважно —
    // прыжок стоит одинаково откуда угодно. Для ходьбы жадный выбор разваливается: ближайший узел
    // уводит в тупик, обратный путь считается заново, и маршрут дёргается. Плюс список кандидатов
    // пересобирался с нуля каждые 2 секунды, поэтому цель могла смениться на середине прохода.
    //
    // ЧТО ДЕЛАЕМ. Один раз строим последовательность точек, дальше только:
    //   * идём к ГОЛОВЕ списка,
    //   * собранную голову удаляем,
    //   * новые точки ВСТАВЛЯЕМ, ничего не пересобирая.
    //
    // АЛГОРИТМ. Это открытый коммивояжёр (маршрут без возврата в начало). Классическая пара для
    // задач такого размера:
    //   1. Ближайший сосед — быстрая начальная последовательность (в среднем ~25% хуже оптимума);
    //   2. 2-opt — разворот отрезка, если это укорачивает маршрут (доводит примерно до ~5%).
    // Обе O(n²) на проход, при n ≤ FarmTourMaxStops это десятки микросекунд.
    //
    // Новые точки добавляются ДЕШЁВОЙ ВСТАВКОЙ: ищем позицию i, минимизирующую
    //     d(t[i-1], p) + d(p, t[i]) − d(t[i-1], t[i])
    // — то есть куда точку впихнуть, чтобы маршрут вырос меньше всего. Существующий порядок при
    // этом не меняется, а значит цель прохода не может «переехать» под ногами.
    //
    // ⚠️ Голова тура НЕ ТРОГАЕТСЯ, пока идёт проход. 2-opt работает только с хвостом (индексы ≥ 1),
    // иначе оптимизация переставит текущую цель и ходок развернётся — ровно та болезнь, из-за
    // которой маршрут «перестраивался туда-сюда».
    //
    // ⚠️ Расстояния горизонтальные. Ходьба не меняет высоту, и учёт Y делал соседние по земле точки
    // «далёкими» только потому, что одна из них на скале.
    public partial class HeartopiaComplete
    {
        internal readonly struct FarmTourStop
        {
            internal readonly Vector3 Position;
            internal readonly string Label;

            internal FarmTourStop(Vector3 position, string label)
            {
                this.Position = position;
                this.Label = label;
            }
        }

        // Столько точек хватает на любую зону сбора, и 2-opt на них считается мгновенно.
        private const int FarmTourMaxStops = 48;

        // Две точки ближе этого — один и тот же ресурс. Тот же порог, что у скана
        // (recentlyVisitedNodes сверяется по 2 м), чтобы дубли не расходились между системами.
        private const float FarmTourSameStopDistance = 2f;

        // Сколько проходов 2-opt делать. Улучшение почти всё выбирается за первые два.
        private const int FarmTourTwoOptPasses = 4;

        // Точки дальше этого от игрока в тур не берём: тур должен покрывать зону, а не карту.
        private const float FarmTourMaxStopRange = 120f;

        private readonly List<FarmTourStop> farmTourStops = new List<FarmTourStop>();
        private readonly List<FarmTourStop> farmTourCandidates = new List<FarmTourStop>();

        // Приёмник для FindClosestAvailableNode: пока он не null, скан складывает СЮДА каждого
        // подходящего кандидата. Так фильтр меток/кулдаунов остаётся ровно в одном месте.
        private List<FarmTourStop> farmCandidateSink;

        private bool farmTourBuilt;

        private static float FarmTourDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static bool IsSameFarmTourStop(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude < FarmTourSameStopDistance * FarmTourSameStopDistance;
        }

        // Собрать кандидатов через обычный скан. Возвращает false, если радар не готов.
        private bool TryCollectFarmTourCandidates(Vector3 origin)
        {
            this.farmTourCandidates.Clear();
            this.farmCandidateSink = this.farmTourCandidates;
            try
            {
                this.FindClosestAvailableNode(out _);
            }
            finally
            {
                this.farmCandidateSink = null;
            }

            // Отсеиваем дальние и дубли внутри самой выборки: один ресурс может дать несколько
            // маркеров (радар и подводный скан рисуют своё), а тур должен видеть его один раз.
            for (int i = this.farmTourCandidates.Count - 1; i >= 0; i--)
            {
                Vector3 pos = this.farmTourCandidates[i].Position;
                bool drop = FarmTourDistance(origin, pos) > FarmTourMaxStopRange;
                if (!drop)
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (IsSameFarmTourStop(pos, this.farmTourCandidates[j].Position))
                        {
                            drop = true;
                            break;
                        }
                    }
                }

                if (drop)
                {
                    this.farmTourCandidates.RemoveAt(i);
                }
            }

            return this.farmTourCandidates.Count > 0;
        }

        // Полная перестройка. Вызывается на старте сбора и когда тур опустел.
        private bool RebuildFarmTour(Vector3 origin)
        {
            if (!this.TryCollectFarmTourCandidates(origin))
            {
                return false;
            }

            this.farmTourStops.Clear();

            // 1. Ближайший сосед от игрока.
            List<FarmTourStop> pool = new List<FarmTourStop>(this.farmTourCandidates);
            Vector3 cursor = origin;
            while (pool.Count > 0 && this.farmTourStops.Count < FarmTourMaxStops)
            {
                int best = 0;
                float bestDist = FarmTourDistance(cursor, pool[0].Position);
                for (int i = 1; i < pool.Count; i++)
                {
                    float d = FarmTourDistance(cursor, pool[i].Position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = i;
                    }
                }

                cursor = pool[best].Position;
                this.farmTourStops.Add(pool[best]);
                pool.RemoveAt(best);
            }

            // 2. 2-opt по всему туру — прохода ещё нет, голову двигать можно.
            float before = this.MeasureFarmTour(origin);
            this.ImproveFarmTourWithTwoOpt(origin, 0);
            float after = this.MeasureFarmTour(origin);

            this.farmTourBuilt = true;
            ModLogger.Msg("[FarmTour] planned " + this.farmTourStops.Count + " stops, "
                + after.ToString("F0") + "m total"
                + (before - after > 0.5f ? " (2-opt saved " + (before - after).ToString("F0") + "m)" : string.Empty)
                + (pool.Count > 0 ? " — " + pool.Count + " candidate(s) over the " + FarmTourMaxStops + " cap" : string.Empty)
                + ".");
            return this.farmTourStops.Count > 0;
        }

        // Длина открытого маршрута: игрок -> первая -> ... -> последняя.
        private float MeasureFarmTour(Vector3 origin)
        {
            if (this.farmTourStops.Count == 0)
            {
                return 0f;
            }

            float total = FarmTourDistance(origin, this.farmTourStops[0].Position);
            for (int i = 1; i < this.farmTourStops.Count; i++)
            {
                total += FarmTourDistance(this.farmTourStops[i - 1].Position, this.farmTourStops[i].Position);
            }

            return total;
        }

        // 2-opt: разворачиваем отрезок [i..j], если так короче. lockedPrefix задаёт, сколько точек
        // с начала трогать нельзя (1, пока к голове идёт проход).
        private void ImproveFarmTourWithTwoOpt(Vector3 origin, int lockedPrefix)
        {
            int n = this.farmTourStops.Count;
            if (n - lockedPrefix < 3)
            {
                return;
            }

            for (int pass = 0; pass < FarmTourTwoOptPasses; pass++)
            {
                bool improved = false;
                for (int i = Mathf.Max(lockedPrefix, 0); i < n - 1; i++)
                {
                    Vector3 a = i == 0 ? origin : this.farmTourStops[i - 1].Position;
                    Vector3 b = this.farmTourStops[i].Position;
                    for (int j = i + 1; j < n; j++)
                    {
                        Vector3 c = this.farmTourStops[j].Position;

                        // Открытый маршрут: у последней точки нет преемника, разворот хвоста
                        // оценивается только по входящему ребру.
                        float delta;
                        if (j == n - 1)
                        {
                            delta = FarmTourDistance(a, c) - FarmTourDistance(a, b);
                        }
                        else
                        {
                            Vector3 d = this.farmTourStops[j + 1].Position;
                            delta = FarmTourDistance(a, c) + FarmTourDistance(b, d)
                                - FarmTourDistance(a, b) - FarmTourDistance(c, d);
                        }

                        if (delta < -0.01f)
                        {
                            this.farmTourStops.Reverse(i, j - i + 1);
                            improved = true;

                            // ⚠️ b теперь протух: после разворота на позиции i лежит бывшая t[j].
                            // Продолжать внутренний цикл со старым b — считать дельты по маршруту,
                            // которого больше нет, и 2-opt начнёт УХУДШАТЬ тур. Выходим и берём
                            // свежие a/b на следующем i.
                            break;
                        }
                    }
                }

                if (!improved)
                {
                    return;
                }
            }
        }

        // Пополнение: новые кандидаты вставляются дешёвой вставкой, порядок существующих сохраняется.
        // lockedPrefix защищает точку, к которой уже идёт проход.
        private void TopUpFarmTour(Vector3 origin, int lockedPrefix)
        {
            if (!this.farmTourBuilt || !this.TryCollectFarmTourCandidates(origin))
            {
                return;
            }

            int added = 0;
            for (int c = 0; c < this.farmTourCandidates.Count; c++)
            {
                if (this.farmTourStops.Count >= FarmTourMaxStops)
                {
                    break;
                }

                FarmTourStop candidate = this.farmTourCandidates[c];
                bool known = false;
                for (int i = 0; i < this.farmTourStops.Count; i++)
                {
                    if (IsSameFarmTourStop(candidate.Position, this.farmTourStops[i].Position))
                    {
                        known = true;
                        break;
                    }
                }

                if (known)
                {
                    continue;
                }

                int bestIndex = this.farmTourStops.Count;
                float bestCost = float.MaxValue;
                for (int i = Mathf.Max(lockedPrefix, 0); i <= this.farmTourStops.Count; i++)
                {
                    Vector3 prev = i == 0 ? origin : this.farmTourStops[i - 1].Position;
                    float cost;
                    if (i == this.farmTourStops.Count)
                    {
                        cost = FarmTourDistance(prev, candidate.Position);
                    }
                    else
                    {
                        Vector3 next = this.farmTourStops[i].Position;
                        cost = FarmTourDistance(prev, candidate.Position)
                            + FarmTourDistance(candidate.Position, next)
                            - FarmTourDistance(prev, next);
                    }

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestIndex = i;
                    }
                }

                this.farmTourStops.Insert(bestIndex, candidate);
                added++;
            }

            if (added > 0)
            {
                // Хвост переоптимизировать можно: голова заперта lockedPrefix.
                this.ImproveFarmTourWithTwoOpt(origin, Mathf.Max(lockedPrefix, 1));
                ModLogger.Msg("[FarmTour] +" + added + " new stop(s), " + this.farmTourStops.Count
                    + " pending, " + this.MeasureFarmTour(origin).ToString("F0") + "m total.");
            }
        }

        // Голова тура. Строит план, если его ещё нет.
        private bool TryGetNextFarmTourStop(Vector3 origin, out Vector3 position, out string label)
        {
            position = Vector3.zero;
            label = string.Empty;

            // Единственное место, где точки уходят из плана. Любая система, отметившая узел в
            // recentlyVisitedNodes — сбор, пропуск, парковка на 5 минут, спасательный телепорт —
            // тем самым автоматически вычёркивает его отсюда. Развешивать вызовы удаления по всем
            // этим веткам значило бы гарантированно забыть одну.
            this.PruneFarmTourStops(origin);

            if (!this.farmTourBuilt || this.farmTourStops.Count == 0)
            {
                if (!this.RebuildFarmTour(origin))
                {
                    return false;
                }
            }

            if (this.farmTourStops.Count == 0)
            {
                return false;
            }

            position = this.farmTourStops[0].Position;
            label = this.farmTourStops[0].Label;
            return true;
        }

        private void PruneFarmTourStops(Vector3 origin)
        {
            if (this.farmTourStops.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            for (int i = this.farmTourStops.Count - 1; i >= 0; i--)
            {
                Vector3 stop = this.farmTourStops[i].Position;

                // Вне зоны. Ферма переезжает между зонами телепортом (area:*), и без этого тур
                // после переезда продолжал бы тянуть игрока обратно к устрицам за 100+ метров.
                if (FarmTourDistance(origin, stop) > FarmTourMaxStopRange)
                {
                    this.farmTourStops.RemoveAt(i);
                    continue;
                }

                foreach (KeyValuePair<Vector3, float> visited in this.recentlyVisitedNodes)
                {
                    if (now < visited.Value && IsSameFarmTourStop(stop, visited.Key))
                    {
                        this.farmTourStops.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        internal void ResetFarmTour()
        {
            this.farmTourStops.Clear();
            this.farmTourCandidates.Clear();
            this.farmCandidateSink = null;
            this.farmTourBuilt = false;
        }
    }
}
