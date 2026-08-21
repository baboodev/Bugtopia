using System;
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
        //
        // Было 120 м — и стояло это ради переезда между зонами: после телепорта старый план описывал
        // место, где нас больше нет, и тянул игрока обратно. С тех пор эту задачу закрыли точно, в
        // трёх местах сразу — сброс тура на любом `area:*` телепорте (единственная точка, через
        // которую проходят все телепорты фермы), на приходе в зону пешком и на переключении фермы.
        // Отсечка по расстоянию осталась вторым, грубым рубежом за ту же самую оборону.
        //
        // Стоила она при этом реального: с одним включённым видом ресурса и ближайшим маркером в
        // 135 м ферма вставала в «Scanning for nodes» навсегда и молчала об этом (замер 2026-08-20:
        // две метки Bubble на 146 и 135 м, кандидатов 0). Плюс асимметрия — у телепортного режима
        // предела нет вообще, так что ходячий был строже без причины.
        //
        // 300 м — это примерно минута плавания: всё ещё зона, а не карта. Дедлайн прохода это
        // выдерживает: clamp(прямая × 3 + 15, 20, 300) даёт на таком плече полные 300 с. Число
        // точек ограничено отдельно (FarmTourMaxStops), так что на стоимость 2-opt это не влияет.
        //
        // ⚠️ Для ДРЕЙФУЮЩЕЙ цели такое плечо бессмысленно по существу: бабл идёт около 1.5 м/с и за
        // минуту уходит метров на девяносто или лопается. Отдельного, короткого предела для
        // расходных целей здесь пока нет — если погоня начнёт уходить в никуда, ограничивать надо
        // по природе цели, а не по расстоянию.
        private const float FarmTourMaxStopRange = 300f;

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
            // ⚠️ TWO SOURCES, EITHER WILL DO. Resolving the swim locomotion is a Mono call that can
            // come back empty for a frame — and when it did, the whole underwater rule below
            // silently reverted to "take the head of the plan", which is how the farm ended up
            // swimming to a stop 38 m away while nearer ones sat unvisited. The walker's own
            // swimming flag is the cheap second opinion and is right whenever it is set.
            this.farmTourVerticalCost = this.farmWalkIsSwimming || this.TryGetFarmWalkSwimLocomotion(out _);
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

        // ⚠️ ПУЗЫРЬ НЕЛЬЗЯ ПЛАНИРОВАТЬ — ЕГО МОЖНО ТОЛЬКО ВЗЯТЬ СЕЙЧАС.
        //
        // Тур — это маршрут: точка встаёт туда, где она дешевле всего вписывается между соседями,
        // и до неё доходит очередь. Для гриба это правильно, он никуда не денется. Пузырь же
        // дрейфует и лопается сам по себе, а в план его вставляло ровно так же — в середину
        // сорока восьми точек. Пока обход доходил до этого места, пузыря давно не было, и со
        // стороны это выглядело так, будто ходячий режим пузыри вообще не замечает. Режим с
        // телепортом их брал: там цель выбирает FindClosestAvailableNode, то есть всегда БЛИЖАЙШУЮ.
        //
        // Отсюда правило: расходная цель не встаёт в очередь, она берётся головой.
        private static bool IsTransientFarmTourStop(string label)
        {
            return string.Equals(label, "Bubble", StringComparison.Ordinal);
        }

        // Есть ли этот пункт всё ещё среди кандидатов последнего скана.
        private bool HasFreshFarmTourCandidateAt(Vector3 stop)
        {
            for (int i = 0; i < this.farmTourCandidates.Count; i++)
            {
                if (IsSameFarmTourStop(stop, this.farmTourCandidates[i].Position))
                {
                    return true;
                }
            }

            return false;
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
            float nearestDropped = float.MaxValue;
            string nearestDroppedLabel = null;
            for (int i = this.farmTourCandidates.Count - 1; i >= 0; i--)
            {
                Vector3 pos = this.farmTourCandidates[i].Position;
                float range = FarmTourDistance(origin, pos);
                bool drop = range > FarmTourMaxStopRange;
                if (drop && range < nearestDropped)
                {
                    nearestDropped = range;
                    nearestDroppedLabel = this.farmTourCandidates[i].Label;
                }

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

            // ⚠️ AN IDLE FARM MUST SAY WHY IT IS IDLE.
            //
            // With only one resource kind enabled and every one of them out of range, the farm sits
            // in "Scanning for nodes..." indefinitely and the log says NOTHING — there is no line
            // for a candidate that was found and then discarded. Measured 2026-08-20: two Bubble
            // markers at 146 m and 135 m, tour range 120 m, candidates 0, and no way to tell that
            // apart from "the radar sees nothing" without attaching a debugger.
            if (this.farmTourCandidates.Count == 0 && nearestDroppedLabel != null)
            {
                float now = Time.unscaledTime;
                if (now >= this.farmTourRangeComplaintAt)
                {
                    this.farmTourRangeComplaintAt = now + FarmTourRangeComplaintInterval;
                    ModLogger.Msg("[FarmTour] nothing in range: nearest is " + nearestDroppedLabel
                        + " at " + nearestDropped.ToString("F0") + "m, and the tour only takes stops within "
                        + FarmTourMaxStopRange.ToString("F0") + "m. Move closer or enable another resource.");
                }
            }

            return this.farmTourCandidates.Count > 0;
        }

        private const float FarmTourRangeComplaintInterval = 20f;
        private float farmTourRangeComplaintAt;

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
            // Расходные цели — вперёд всей очереди, ближайшая из них.
            int transientPick = -1;
            float transientBest = float.MaxValue;
            for (int i = 0; i < this.farmTourStops.Count; i++)
            {
                if (!IsTransientFarmTourStop(this.farmTourStops[i].Label))
                {
                    continue;
                }

                float d = FarmTourDistance(origin, this.farmTourStops[i].Position);
                if (d < transientBest)
                {
                    transientBest = d;
                    transientPick = i;
                }
            }

            if (transientPick >= 0)
            {
                position = this.farmTourStops[transientPick].Position;
                label = this.farmTourStops[transientPick].Label;
                return true;
            }

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

            // Walking pays for distance; a teleport does not. So only walk mode measures routes —
            // and only over the few nearest, ordered by the straight line that is already known.
            if (this.farmWalkToNodeEnabled)
            {
                // ⚠️ THE NEAREST FOUR, NOT THE FIRST FOUR. This used to take the tour head plus
                // stops 0, 1, 2 IN PLAN ORDER and only then sort them by distance — so on land,
                // where the head is index 0, the "shortlist of the nearest" was simply the first
                // four stops of the planned circuit. After nearest-neighbour and 2-opt that order
                // is a route, not a distance ranking, and its fourth entry can be across the map.
                // The ranking then chose the best of four arbitrary stops and called it nearest.
                this.farmRouteShortlist.Clear();
                for (int i = 0; i < this.farmTourStops.Count; i++)
                {
                    this.farmRouteShortlist.Add(i);
                }

                this.farmRouteShortlist.Sort((x, y) =>
                    FarmTourDistance(origin, this.farmTourStops[x].Position)
                        .CompareTo(FarmTourDistance(origin, this.farmTourStops[y].Position)));

                if (this.farmRouteShortlist.Count > FarmRouteRankMaxMeasured)
                {
                    this.farmRouteShortlist.RemoveRange(FarmRouteRankMaxMeasured,
                        this.farmRouteShortlist.Count - FarmRouteRankMaxMeasured);
                }

                int byRoute = this.PickFarmTourStopByRoute(origin, this.farmRouteShortlist);
                if (byRoute >= 0)
                {
                    pick = byRoute;
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

                // ⚠️ УЖЕ СОБРАН. План строится по маркерам радара в момент планирования, а узел
                // остывает мгновенно — и до этой проверки тур продолжал выдавать точку, с которой
                // ресурс давно снят. Ферма шла к собранному грибу, и единственным, что её
                // разворачивало, была проверка уже В ПУТИ (TryAbandonDrainedFarmWalkTarget), то
                // есть после того, как игрок туда пошёл.
                //
                // Живой скан — тот же авторитет, которым пользуются сбор и FindClosestAvailableNode.
                // Найдено и cold => снимаем из плана и штампуем НАСТОЯЩИМ остатком кулдауна, иначе
                // TopUpFarmTour вернёт точку обратно на следующем же пополнении.
                //
                // Не найдено => никакого вывода: узел может быть просто вне зоны стрима.
                if (this.TryGetLiveNodeColdState(stop, 0f, out bool stopCold, out long stopColdEndMs) && stopCold)
                {
                    this.StampVisitedNode(stop, now + this.GetVisitedColdStampSeconds(stopColdEndMs));
                    this.farmTourStops.RemoveAt(i);
                    continue;
                }

                // ⚠️ ЛОПНУВШИЙ ПУЗЫРЬ ОСТАЁТСЯ В ПЛАНЕ НАВСЕГДА, если его отсюда не убрать.
                //
                // Остальные пункты снимает живой скан коллектаблов, но пузырь в него не попадает
                // вовсе (он не коллектабл), так что для него «остыл» не наступает никогда. Пункт
                // от исчезнувшего пузыря переживал бы весь тур: ходок дошёл бы до пустого места,
                // выждал таймаут и только тогда отметил его посещённым — по одному проходу
                // впустую на каждый лопнувший пузырь.
                //
                // Судим по кандидатам последнего скана, и только когда они есть: пустой список
                // означает «скан ещё не собран», а не «маркеров нет».
                if (this.farmTourCandidates.Count > 0
                    && IsTransientFarmTourStop(this.farmTourStops[i].Label)
                    && !this.HasFreshFarmTourCandidateAt(stop))
                {
                    this.farmTourStops.RemoveAt(i);
                    continue;
                }

                // ⚠️ НЕПОДТВЕРЖДЁННЫЙ ДИНАМИЧЕСКИЙ КУСТ — не цель. Гриб на этом месте мог быть
                // собран, и вместо него растёт новая сущность, чей компонент неотличим от спелой;
                // знает об этом только вердикт клиента. Точка не штампуется как посещённая — она не
                // занята, она неизвестна, и вернуться к ней надо сразу, как вердикт придёт (свип
                // запускается по появлению незнакомого netId, то есть через секунду-другую).
                if (this.IsFarmTargetUnconfirmed(stop, out _))
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

        // ⚠️ UNDERWATER, "NEAREST" HAS TO BE RE-ASKED WHILE SWIMMING, NOT ONLY AT THE START.
        //
        // The head of the tour is chosen when a walk begins and then left alone on purpose — a
        // target that moves under the walker is what made routes thrash. On land that is right: the
        // walker follows corners and passes nothing it could have taken instead.
        //
        // A swim is different. It is a straight line through open water, thirty or forty metres of
        // it, and the plan was made before any of it happened — so the player sails past resources
        // that were not the nearest when they set off and are now five metres away.
        //
        // The switch is deliberately hard to trigger: a candidate has to be BOTH a good few metres
        // nearer AND a fraction of what is left, and only while there is real distance still to
        // swim. Anything looser turns a swim into a tour of second thoughts.
        private const float FarmSwimRetargetInterval = 2f;
        private const float FarmSwimRetargetMinRemaining = 12f;
        private const float FarmSwimRetargetMinGain = 6f;
        private const float FarmSwimRetargetFraction = 0.6f;
        private float farmSwimRetargetNextAt;

        private bool TryRetargetNearerSwimNode(Vector3 selfPos, float now)
        {
            if (!this.farmWalkIsSwimming
                || !this.farmWalkLabel.StartsWith("node:", StringComparison.Ordinal)
                || this.farmWalkUnstickPhase != FarmWalkUnstickIdle
                || this.farmTourStops.Count == 0)
            {
                return false;
            }

            if (now < this.farmSwimRetargetNextAt)
            {
                return false;
            }

            this.farmSwimRetargetNextAt = now + FarmSwimRetargetInterval;

            float remaining = Distance3D(selfPos, this.farmWalkTrueTarget);
            if (remaining < FarmSwimRetargetMinRemaining)
            {
                return false;   // close enough that switching would just waste the approach
            }

            float bestDistance = float.MaxValue;
            Vector3 best = Vector3.zero;
            bool found = false;
            for (int i = 0; i < this.farmTourStops.Count; i++)
            {
                Vector3 stop = this.farmTourStops[i].Position;
                if (IsSameFarmTourStop(stop, this.farmWalkTrueTarget))
                {
                    continue;
                }

                float d = Distance3D(selfPos, stop);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = stop;
                    found = true;
                }
            }

            if (!found
                || bestDistance > remaining * FarmSwimRetargetFraction
                || remaining - bestDistance < FarmSwimRetargetMinGain)
            {
                return false;
            }

            ModLogger.Msg("[FarmWalk] " + this.farmWalkLabel + ": passing a nearer node — "
                + bestDistance.ToString("F1") + "m away against " + remaining.ToString("F1")
                + "m still to swim; taking that one instead.");

            // Not stamped as visited: the target we are leaving is perfectly good, it is simply
            // further. It goes back into the plan and gets collected on the way through.
            this.farmWalkSkipToScan = true;
            this.FinishFarmWalk("a nearer node is " + bestDistance.ToString("F1")
                + "m away — switching to it", false);
            _ = best;
            return true;
        }

        // ⚠️ NEAREST BY ROUTE, NOT BY COORDINATES.
        //
        // Every pick in this file ranked candidates with FarmTourDistance — a straight line between
        // two points. That is the right metric for a teleport, which costs the same from anywhere,
        // and the wrong one for walking: a node ten metres away on the far side of a rock loses to
        // one twenty metres away across open ground, and the straight line cannot tell them apart.
        // It also cannot tell that a candidate is unreachable at all, which is how the farm kept
        // selecting nodes whose route build then failed.
        //
        // So the shortlist is still made by straight line — it is free, and a candidate cannot be
        // near by route while being far in space — but the WINNER is chosen by measuring the route
        // to each of the few nearest. Bounded to FarmRouteRankCandidates: this runs on every scan,
        // and measuring forty nodes to pick one would cost more than the walk saves.
        // ⚠️ NOT A SHORTLIST ANY MORE — A BOUND.
        //
        // This was 4 "to keep the cost sane", a number picked without measuring the cost and with no
        // reason to believe the fifth candidate could not win. It can: on land the fifth-nearest is
        // routinely the one across an open field while the first four sit behind a fence.
        //
        // A route is never SHORTER than its straight line. So once the best measured route is no
        // longer than the straight-line distance to the next candidate, nothing further can beat it
        // — every remaining candidate is already further away in a straight line than the winner is
        // by road. Candidates are walked in straight-line order, so that test ends the search
        // correctly rather than arbitrarily, and usually after two or three measurements.
        //
        // The cap that remains is a backstop against a pathological field, not the policy.
        private const int FarmRouteRankMaxMeasured = 12;
        private readonly List<Vector3> farmRouteMeasureCorners = new List<Vector3>();
        private readonly List<int> farmRouteShortlist = new List<int>();

        // Length of the route the walker would actually drive, or false when there is none.
        //
        // ⚠️ MEASURE WHAT THE BUILDER BUILDS. An earlier version snapped with the cheap nearest-node
        // rule and summed the RAW A* output — which is not a route the walk would ever drive. The
        // builder snaps to the nearest REACHABLE node and then SHORTCUTS the path, and shortcutting
        // is exactly what removes the graph's zig-zag. Skipping both overestimated every candidate,
        // unevenly, and produced numbers that were not comparable to each other:
        //     34m away costs 266m to travel   /   35m away costing 261m
        // Seven times the straight line, for a swim the walker would have taken in one leg.
        //
        // The start snap is resolved ONCE per pick and passed in: every candidate starts from the
        // same place, so paying for it four times bought nothing but log noise.
        private bool TryMeasureFarmRouteLength(Vector3 from, int startIndex, Vector3 to, out float length)
        {
            length = 0f;

            // Underwater a clear line is the route — no graph involved, so its length is the line.
            if (this.farmWalkIsSwimming && this.IsFarmWalkDirectSwimClear(from, to, out _))
            {
                length = Distance3D(from, to);
                return true;
            }

            if (startIndex < 0
                || !this.TryFindNearestTrackGraphNode(to, FarmWalkGraphSnapRadius, out int endIndex,
                    this.farmWalkExcludedEndNodes)
                || !this.TryComputeTrackGraphPath(startIndex, endIndex, this.farmRouteMeasureCorners,
                    this.farmWalkExcludedNodes))
            {
                return false;
            }

            // The same two steps the builder takes before committing: the target becomes the final
            // corner, then the path is straightened.
            this.farmRouteMeasureCorners.Add(to);
            this.ShortcutFarmWalkRoute(from, this.farmRouteMeasureCorners);

            Vector3 previous = from;
            for (int i = 0; i < this.farmRouteMeasureCorners.Count; i++)
            {
                length += Distance3D(previous, this.farmRouteMeasureCorners[i]);
                previous = this.farmRouteMeasureCorners[i];
            }

            return true;
        }

        // The shortlist's best by measured route. Returns the index into farmTourStops, or -1 when
        // nothing could be measured — in which case the caller keeps its straight-line pick rather
        // than refusing to walk.
        private int PickFarmTourStopByRoute(Vector3 origin, List<int> shortlist)
        {
            int best = -1;
            float bestLength = float.MaxValue;
            float bestStraight = float.MaxValue;

            // One reachable snap for the whole pick — the builder's rule, paid for once.
            if (!this.TryFindReachableTrackGraphNode(origin, FarmWalkGraphSnapRadius,
                    out int startIndex, this.farmWalkExcludedNodes, "start"))
            {
                startIndex = -1;
            }

            // ⚠️ AN UNMEASURABLE CANDIDATE IS NOT AN UNREACHABLE ONE.
            //
            // This used to DROP any candidate whose route would not measure. Underwater that is most
            // of them: the direct-swim check is capped at 50 m, past which it falls to a graph of 86
            // nodes that frequently has no path — and a near node whose straight line happens to be
            // blocked lands in the same bucket. The farm then picked the far node that happened to
            // measure, and called it "nearest by route":
            //     taking a stop 48m away (48m to walk) over one 30m away
            //     taking a stop 31m away (48m to walk) over one 13m away
            // Thirteen metres discarded for forty-eight. That is not a routing decision, it is a
            // measurement gap wearing one.
            //
            // So an unmeasured candidate keeps its place, ranked by straight line with a penalty —
            // it may need to go round, and that costs something, but not its whole candidacy. If it
            // really is unreachable the walk fails and rule 0.6 parks it, which is exactly the
            // mechanism that exists for that.
            // ⚠️ A ROUTE SEVERAL TIMES THE STRAIGHT LINE IS NOT A ROUTE, IT IS A SPARSE GRAPH.
            //
            // Measured 2026-08-22 underwater: a stop 34 m away "costs 266 m to travel", another 35 m
            // away "261 m". The walker will never drive those — underwater it swims the straight
            // line and lets the escape ladder deal with whatever is in the way; the 260 m is what
            // A* makes of 86 nodes strung 20-30 m apart. Believing it hands the pick to whichever
            // far node happens to sit near a waypoint.
            //
            // So an implausible measurement is treated as no measurement at all.
            const float implausibleFactor = 3f;

            // ⚠️ AND A MARGIN, or the winner is decided by noise. The same run picked a stop 48 m
            // away over one 39 m away to save TWO metres of route — inside the error of a ranking
            // that snaps to the nearest node rather than the reachable one. Overriding "nearest"
            // has to be worth something.
            const float routeMarginFactor = 0.8f;
            const float routeMarginMinimum = 5f;

            const float unmeasuredPenalty = 1.5f;
            float runnerUpLength = float.MaxValue;
            bool bestMeasured = false;

            int measuredCount = 0;
            for (int i = 0; i < shortlist.Count; i++)
            {
                Vector3 stop = this.farmTourStops[shortlist[i]].Position;
                float straight = Distance3D(origin, stop);

                // The bound: nothing from here on can beat what we already have, because a route is
                // never shorter than its straight line and the list is in straight-line order.
                if (best >= 0 && bestLength <= straight)
                {
                    break;
                }

                measuredCount++;
                bool measured = this.TryMeasureFarmRouteLength(origin, startIndex, stop, out float routeLength);
                if (measured && routeLength > straight * implausibleFactor)
                {
                    measured = false;
                }

                if (!measured)
                {
                    routeLength = straight * unmeasuredPenalty;
                }

                if (i == 0)
                {
                    runnerUpLength = routeLength;
                }

                if (i == 0 || routeLength < bestLength)
                {
                    bestLength = routeLength;
                    bestStraight = straight;
                    bestMeasured = measured;
                    best = shortlist[i];
                }
            }

            // The nearest keeps the pick unless the winner is better by a real margin.
            if (best >= 0 && best != shortlist[0]
                && bestLength > runnerUpLength * routeMarginFactor - routeMarginMinimum)
            {
                best = shortlist[0];
            }

            if (best >= 0 && best != shortlist[0])
            {
                // Both sides of the comparison, or the line is unreadable: the version that printed
                // only the winner's route against the runner-up's STRAIGHT distance read as nonsense
                // ("31m away, 48m to walk, beat one 13m away") and hid the real cause for a day.
                ModLogger.Msg("[FarmTour] nearest by route, not by line: "
                    + bestStraight.ToString("F0") + "m away costs " + bestLength.ToString("F0")
                    + "m to travel" + (bestMeasured ? string.Empty : " (estimated)")
                    + ", against " + Distance3D(origin, this.farmTourStops[shortlist[0]].Position)
                        .ToString("F0") + "m away costing " + runnerUpLength.ToString("F0")
                    + "m (" + measuredCount + " of " + shortlist.Count + " measured).");
            }

            return best;
        }

        // Is there anything else worth going to? Rule 0.2: a teleport is allowed only when the walk
        // has spent its budget AND this is the only target — if the plan still holds another stop,
        // switching to it is both cheaper and quieter than a warp.
        private bool HasAnotherFarmTourStop(Vector3 exceptThis)
        {
            for (int i = 0; i < this.farmTourStops.Count; i++)
            {
                if (!IsSameFarmTourStop(this.farmTourStops[i].Position, exceptThis))
                {
                    return true;
                }
            }

            return false;
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
