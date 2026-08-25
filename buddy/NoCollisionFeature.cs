using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Проход сквозь невидимые преграды — переключением МАТРИЦЫ СТОЛКНОВЕНИЙ движка.
    //
    // ЧТО ЭТО. В игре есть барьерные объёмы, которых игрок не видит: тонкие высокие плиты и прочая
    // геометрия, лежащая на слое Passable(10). Название слоя означает ПРОТИВОПОЛОЖНОЕ содержимому —
    // замерено парными пробами, 12 из 12 сплошные. Именно они дают «тут не пройти, хотя пусто».
    //
    // ЧЕМ СНИМАЕТСЯ. Одним вызовом:
    //
    //     XDT.Physics.PhysicsManager.IgnoreLayerCollision(наш слой, 10, true)
    //
    // Меняется ОДНА ЯЧЕЙКА матрицы столкновений. Мир не трогается вовсе: ни один коллайдер не
    // удалён, не выключен, не переведён в триггер, не сдвинут. Барьеры стоят где стояли — они просто
    // перестают относиться к нам.
    //
    // ⭐ Путь штатный, а не найденная дыра: игра пользуется тем же рычагом сама. VehicleManager
    // гасит пару Water↔Vehicle по слоям, VehicleResHandle — контроллер против кузова попарно, чтобы
    // водитель не бился о собственную машину.
    //
    // ⭐ ПЕШКОМ И ЗА РУЛЁМ — РАЗНЫЕ ПАРЫ, и это измерено, а не выведено. Проезд сквозь барьер
    // получился при ВКЛЮЧЁННОЙ паре игрока: значит во время езды в стену бьётся кузов (слой 16), а
    // контроллер персонажа в этом не участвует. Одной парой обойтись нельзя.
    //
    // ⭐ СМЕНУ МИРА ПЕРЕЖИВАЕТ. Погружение под воду и возврат подняли world epoch 3 → 5 (две смены
    // сцены, коллайдеры пересобрались полностью), проход продолжал работать без переустановки. Это
    // глобальная настройка движка, а не свойство сцены, поэтому мирового гейта здесь нет — редкое
    // исключение из общего правила мода.
    //
    // ⚠️ ГЕТТЕРА НЕТ. Спросить движок «выключено ли сейчас» невозможно: в обёртках только сеттеры.
    // Значит ЕДИНСТВЕННЫЙ источник правды — поля ниже, и они обязаны быть точными: соврав один раз,
    // мы навсегда потеряем возможность вернуть коллизию обратно. Отсюда две вещи:
    //   * применяем только по РАЗНИЦЕ желаемого и применённого, а не «на всякий случай» каждый кадр;
    //   * восстанавливаем ТЕМ ЖЕ номером слоя, которым выключали (см. noCollisionPlayerLayerApplied).
    //
    // ⚠️ ОРАКУЛЫ МАТРИЦУ НЕ ВИДЯТ. Весь LevelLayerManager построен на кастах, а каст фильтрует
    // только по layerMask. После включения игрок проходит, а CanPlayerMoveUseSphere по-прежнему
    // отвечает BLOCKED. Для ходока это значит, что его аудит ног продолжит отвергать ноги, которые
    // стали проходимыми — то есть переключатель НЕ открывает ему новые маршруты. Польза в другом и
    // она реальная: когда ходок всё-таки идёт ногой, где аудит преграду пропустил (а он пропускает —
    // ради этого и существует лестница побега), он теперь ПРОДАВЛИВАЕТ её вместо того, чтобы
    // заклинить и потратить весь бюджет отрезка.
    public partial class HeartopiaComplete
    {
        // Барьерный слой. См. docs — «Passable» блокирует, «Wall» нет.
        private const int NoCollisionBarrierLayer = 10;

        // Запасной номер слоя игрока: столько показал живой контроллер. Читаем всё равно вживую —
        // константа только на случай, если контроллер не резолвится.
        private const int NoCollisionPlayerLayerFallback = 8;

        // Слой кузова. Резолвить его вживую нечем — у нас нет ссылки на коллайдер машины, в которой
        // сидим, — поэтому число измеренное: перепись дала 32 коллайдера на слое 16, и проезд
        // сквозь барьер подтвердил именно его.
        private const int NoCollisionVehicleLayer = 16;

        // Пользовательские переключатели (сохраняются).
        private bool noCollisionPlayerEnabled;
        private bool noCollisionVehicleEnabled;

        // Держатель от ходока: пока walk to nodes ведёт персонажа, столкновение снято независимо от
        // переключателей. Не сохраняется — это состояние прогона, а не настройка.
        private bool noCollisionWalkerHold;

        private IntPtr noCollisionMethod;
        private bool noCollisionResolveTried;
        private bool noCollisionResolveFailedLogged;

        // Что РЕАЛЬНО выключено в движке прямо сейчас. Единственный источник правды, см. шапку.
        private bool noCollisionPlayerApplied;
        private bool noCollisionVehicleApplied;

        // Слой, которым выключали. Восстанавливать надо им же: если контроллер в момент отката
        // отвечает другим числом (или не отвечает вовсе), пара останется выключенной навсегда.
        private int noCollisionPlayerLayerApplied = -1;

        internal bool NoCollisionActive => this.noCollisionPlayerApplied || this.noCollisionVehicleApplied;

        private bool EnsureNoCollisionResolved()
        {
            if (this.noCollisionMethod != IntPtr.Zero)
            {
                return true;
            }

            if (this.noCollisionResolveTried)
            {
                return false;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;   // AuraMono ещё не поднялась — это НЕ повод сжигать попытку.
                }

                // Две обёртки над ОДНИМИ И ТЕМИ ЖЕ нативными icall'ами (сверено по идентификаторам в
                // декомпиляции), поэтому берём ту, что нашлась.
                IntPtr cls = this.FindAuraMonoClassInImages(
                    "XDT.Physics", "PhysicsManager",
                    new[] { "EngineWrapper", "EngineWrapper.dll" });
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassInImages(
                        "MonoGame.ScriptFramework", "PhysicsExtension",
                        new[] { "EngineWrapper", "EngineWrapper.dll" });
                }

                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassInAllLoadedImages("PhysicsManager", "XDT.Physics");
                }

                if (cls == IntPtr.Zero)
                {
                    return false;   // образ мог ещё не загрузиться — пробуем позже.
                }

                this.noCollisionMethod = this.FindAuraMonoMethodOnHierarchy(cls, "IgnoreLayerCollision", 3);
                if (this.noCollisionMethod == IntPtr.Zero)
                {
                    // Класс есть, метода нет — это уже навсегда, повторять нечего.
                    this.noCollisionResolveTried = true;
                    ModLogger.Msg("[NoCollision] IgnoreLayerCollision(int,int,bool) не найден — "
                        + "проход сквозь преграды недоступен в этой сессии.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                this.noCollisionResolveTried = true;
                ModLogger.Msg("[NoCollision] resolve threw: " + ex.Message);
                return false;
            }
        }

        // Все три аргумента — значимые типы, поэтому каждый слот это указатель на НАШИ СОБСТВЕННЫЕ
        // байты в стеке. Входные параметры — безопасное направление через mono_runtime_invoke;
        // смертельная форма (out-слот структуры) здесь не встречается ни разу.
        private unsafe bool TryNoCollisionIgnoreLayer(int layerA, int layerB, bool ignore)
        {
            if (this.noCollisionMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                int a = layerA;
                int b = layerB;
                byte flag = (byte)(ignore ? 1 : 0);
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&a);
                args[1] = (IntPtr)(&b);
                args[2] = (IntPtr)(&flag);

                // ⚠️ Метод возвращает void: судить об успехе по result здесь НЕЛЬЗЯ, он всегда ноль.
                // Единственный признак — пустое исключение.
                auraMonoRuntimeInvoke(this.noCollisionMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    ModLogger.Msg("[NoCollision] IgnoreLayerCollision(" + layerA + ", " + layerB
                        + ", " + ignore + ") бросил исключение.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[NoCollision] invoke threw: " + ex.Message);
                return false;
            }
        }

        // Живой слой контроллера персонажа. Не константа: слои раздаёт LayerMask.NameToLayer, и
        // хардкод пережил бы ровно до первой перенумерации.
        private int ResolveNoCollisionPlayerLayer()
        {
            try
            {
                if (this.TryGetFarmWalkSweepController(out IntPtr ctrl) && ctrl != IntPtr.Zero
                    && this.TryInvokeAuraMonoZeroArgInt(ctrl, out int layer, "get_layer")
                    && layer >= 0 && layer <= 31)
                {
                    return layer;
                }
            }
            catch
            {
            }

            return NoCollisionPlayerLayerFallback;
        }

        private void ProcessNoCollisionOnUpdate()
        {
            // Ходок ведёт персонажа — снимаем столкновение на время прогона, независимо от галок.
            bool hold = this.FarmWalkRunActive;
            if (hold != this.noCollisionWalkerHold)
            {
                this.noCollisionWalkerHold = hold;
                if (hold)
                {
                    ModLogger.Msg("[NoCollision] walk to nodes пошёл — снимаю столкновение с "
                        + "барьерным слоем на время прогона.");
                }
                else
                {
                    ModLogger.Msg("[NoCollision] walk to nodes закончил — столкновение возвращается "
                        + "к состоянию переключателей.");
                }
            }

            this.ApplyNoCollisionState();
        }

        // Применяем ТОЛЬКО по разнице желаемого и применённого. Дёргать матрицу каждый кадр было бы
        // и лишней работой, и потерей единственного следа того, что мы вообще что-то меняли.
        private void ApplyNoCollisionState()
        {
            bool wantPlayer = this.noCollisionPlayerEnabled || this.noCollisionWalkerHold;
            bool wantVehicle = this.noCollisionVehicleEnabled || this.noCollisionWalkerHold;

            if (wantPlayer == this.noCollisionPlayerApplied && wantVehicle == this.noCollisionVehicleApplied)
            {
                return;
            }

            if (!this.EnsureNoCollisionResolved())
            {
                // Молчать нельзя: снаружи это выглядит как «галка стоит, а ничего не происходит».
                if ((wantPlayer || wantVehicle) && !this.noCollisionResolveFailedLogged)
                {
                    this.noCollisionResolveFailedLogged = true;
                    ModLogger.Msg("[NoCollision] включить нечем — IgnoreLayerCollision пока не "
                        + "зарезолвился (AuraMono не поднялась или образ не загружен).");
                }

                return;
            }

            this.noCollisionResolveFailedLogged = false;

            if (wantPlayer != this.noCollisionPlayerApplied)
            {
                // Выключаем ЖИВЫМ слоем, возвращаем — ЗАПОМНЕННЫМ.
                int layer = wantPlayer ? this.ResolveNoCollisionPlayerLayer() : this.noCollisionPlayerLayerApplied;
                if (layer >= 0 && this.TryNoCollisionIgnoreLayer(layer, NoCollisionBarrierLayer, wantPlayer))
                {
                    this.noCollisionPlayerApplied = wantPlayer;
                    this.noCollisionPlayerLayerApplied = wantPlayer ? layer : -1;
                    ModLogger.Msg("[NoCollision] игрок (слой " + layer + ") "
                        + (wantPlayer ? "ИГНОРИРУЕТ" : "снова сталкивается с") + " барьерный слой "
                        + NoCollisionBarrierLayer + ".");
                }
            }

            if (wantVehicle != this.noCollisionVehicleApplied)
            {
                if (this.TryNoCollisionIgnoreLayer(NoCollisionVehicleLayer, NoCollisionBarrierLayer, wantVehicle))
                {
                    this.noCollisionVehicleApplied = wantVehicle;
                    ModLogger.Msg("[NoCollision] транспорт (слой " + NoCollisionVehicleLayer + ") "
                        + (wantVehicle ? "ИГНОРИРУЕТ" : "снова сталкивается с") + " барьерный слой "
                        + NoCollisionBarrierLayer + ".");
                }
            }
        }

        // Вернуть всё, что мы выключили. Зовётся на выгрузке мода: матрица переживает смену мира, а
        // значит пережила бы и выгрузку — оставленная выключенной, она осталась бы такой до конца
        // игровой сессии, и вернуть её было бы уже некому.
        internal void ReleaseNoCollision()
        {
            if (!this.noCollisionPlayerApplied && !this.noCollisionVehicleApplied)
            {
                return;
            }

            this.noCollisionPlayerEnabled = false;
            this.noCollisionVehicleEnabled = false;
            this.noCollisionWalkerHold = false;
            this.ApplyNoCollisionState();
        }
    }
}
