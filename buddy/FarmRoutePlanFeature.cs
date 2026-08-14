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

        // Size of the tour at the last full ordering pass — the yardstick the re-plan trigger uses.
        private int farmTourPlannedCount;

        // Below this a re-plan is not worth turning the player around for.
        private const int FarmTourReplanMinStops = 8;

        // Refreshed once per plan / top-up: is the farm currently swimming?
        private bool farmTourVerticalCost;

        // ⚠️ Горизонтально НА СУШЕ, полностью в 3-D ПОД ВОДОЙ.
        //
        // На суше ходьба не меняет высоту, и учёт Y отталкивал соседние по земле точки только
        // потому, что одна из них на скале, — это была верная причина считать плоско.
        //
        // Под водой всё наоборот: погружение И ЕСТЬ перемещение, а разброс глубин там больше
        // разброса по горизонтали (в прогоне −36…−74 м). Плоская метрика объявляла соседями точки,
        // между которыми двадцать метров вертикали, и порядок обхода выглядел случайным. В логе это
        // видно буквально: `walking 9,7m` — и тут же `diving 19,6m`, `wedged at 20,6m`.
        private float FarmTourDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            float planar = dx * dx + dz * dz;
            if (!this.farmTourVerticalCost)
            {
                return Mathf.Sqrt(planar);
            }

            float dy = a.y - b.y;
            return Mathf.Sqrt(planar + dy * dy);
        }

        private void RefreshFarmTourCostModel()
        {
            this.farmTourVerticalCost = this.TryGetFarmWalkSwimLocomotion(out _);
        }

        // Откуда меряем маршрут. Камера — не игрок: под водой она висит позади и выше, и с 3-D
        // метрикой этот сдвиг искажает выбор первой точки сильнее всего, ровно там, где цена
        // ошибки максимальна. Камера остаётся запасным вариантом на случай, если позиция игрока
        // не разрешилась.
        internal Vector3 ResolveFarmTourOrigin()
        {
            if (this.TryGetNavMeshSelfPosition(out Vector3 selfPos, out _))
            {
                return selfPos;
            }

            Camera cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        private static bool IsSameFarmTourStop(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude < FarmTourSameStopDistance * FarmTourSameStopDistance;
        }

        // Собрать кандидатов через обычный скан. Возвращает false, если радар не готов.
        private bool TryCollectFarmTourCandidates(Vector3 origin)
        {
            // Every ordering decision below depends on this, so resolve it before any of them.
            this.RefreshFarmTourCostModel();
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
            this.farmTourPlannedCount = this.farmTourStops.Count;
            ModLogger.Msg("[FarmTour] planned " + this.farmTourStops.Count + " stops, "
                + after.ToString("F0") + "m total"
                + (this.farmTourVerticalCost ? " [3-D cost]" : " [planar cost]")
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

            // One deliberate re-plan when the tour has outgrown the plan it was built from.
            //
            // The radar only ever sees what has streamed in, so the first plan is built from
            // whatever is in range at that moment — the previous run started from TWO stops and
            // reached twenty-three purely by insertion. Insertion never reverses direction (which
            // is what keeps the route stable) but it also never fixes a seed that small.
            //
            // Rare and loud, not per-scan: doubling is a real change of the picture, and the log
            // says so, whereas re-optimising every two seconds is what made the player oscillate.
            if (this.farmTourStops.Count >= FarmTourReplanMinStops
                && this.farmTourStops.Count >= this.farmTourPlannedCount * 2)
            {
                int grewFrom = this.farmTourPlannedCount;
                float beforeLength = this.MeasureFarmTour(origin);
                this.ImproveFarmTourWithTwoOpt(origin, 0);
                this.farmTourPlannedCount = this.farmTourStops.Count;
                ModLogger.Msg("[FarmTour] re-planned: grew from " + grewFrom + " to "
                    + this.farmTourStops.Count + " stops, " + beforeLength.ToString("F0") + "m -> "
                    + this.MeasureFarmTour(origin).ToString("F0") + "m.");
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
                // ⚠️ НИКАКОГО 2-opt при пополнении — только вставка.
                //
                // Я его здесь запускал, рассуждая «голова заперта, хвост трогать можно». Голова
                // действительно не двигалась, но 2-opt решает ОТКРЫТУЮ задачу, и её оптимум резко
                // зависит от точки старта. Точка старта — игрок, а он смещается после каждого
                // сбора. Поэтому на каждом пополнении оптимизатор законно находил другой ответ и
                // разворачивал остаток маршрута целиком.
                //
                // В логе это читается по z: 51 → 41 → 16 → 6 → 1, потом обратно 16 → 24 → 40 → 66,
                // потом снова назад 73 → 51 → 21. Игрок вычёсывал зону в одну сторону, разворачивался
                // и шёл обратно — «плавает туда-сюда».
                //
                // Вставка сама по себе порядок не переставляет, поэтому маршрут остаётся связным.
                // Полный 2-opt делается один раз, при построении плана. Это ровно то, что просил
                // пользователь: отсортировать заранее, дальше только пополнять.
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

            // ПОД ВОДОЙ — всегда ближайшая, а не следующая по плану.
            //
            // Планировать обход имеет смысл там, где леги предсказуемы. Под водой они не такие:
            // граф путевых точек здесь 86 узлов против 1745 на суше, перегоны между ними по
            // 20-30 м прямой, и каждый третий упирается в рельеф. Порядок, посчитанный по
            // расстояниям, ничего не стоит, если половина переходов в нём не проходима, а цена
            // ошибки — заклинивший проход с четырьмя отступами.
            //
            // Ближайшая точка почти всегда достижима просто потому, что она рядом. План при этом
            // никуда не девается: список тот же, пополняется так же, чистится так же — меняется
            // только правило выбора головы.
            int pick = 0;
            if (this.farmTourVerticalCost)
            {
                float bestDist = FarmTourDistance(origin, this.farmTourStops[0].Position);
                for (int i = 1; i < this.farmTourStops.Count; i++)
                {
                    float d = FarmTourDistance(origin, this.farmTourStops[i].Position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        pick = i;
                    }
                }
            }

            position = this.farmTourStops[pick].Position;
            label = this.farmTourStops[pick].Label;
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
            this.farmTourPlannedCount = 0;
        }
    }
}
