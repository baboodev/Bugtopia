using System;
using UnityEngine;

namespace HeartopiaMod
{
    // "Compare Game Track" — диагностический режим для Walk to Nodes.
    //
    // Заставляет игру построить СВОЙ маршрут к тому же узлу, к которому идёт мод, и печатает оба
    // в лог. Это единственный способ увидеть, где расходятся наш A* и родной Track: мод считает
    // путь по снимку графа (TrackPathGraphFeature), игра — по своему живому AStar со своим
    // сглаживанием, и до сих пор сравнивать их было не с чем.
    //
    // Как игра узнаёт, куда вести (TrackingSystem.cs:464):
    //     MapSpotProtocolManager.AddSpot(SpotEnum.Custom, useId, pos, SpotReason.TrackMap)
    //     TrackProtocolManager.StartLocalTrackMapSign((uint)useId)
    // Точка карты создаётся событием EventCenter — сервера это не касается.
    //
    // ⚠️ StartLocalTrackMapSign внутри себя вызывает StopAllLocalTrack(), то есть СБИВАЕТ ручной
    // трек игрока. Поэтому режим за отдельным тумблером и по умолчанию выключен.
    //
    // Результат игры читается из приватных полей живого TrackingPathModule:
    //     _path         — углы после сглаживания (аналог наших farmWalkCorners)
    //     realPathList  — точки звёздочек через perInstanceDis (то, что видно на экране)
    public partial class HeartopiaComplete
    {
        private bool farmWalkTrackCompareEnabled;

        // Свой usageId для точки карты — лишь бы не пересечься с игровыми.
        private const int FarmWalkTrackSpotUseId = 990117;
        private const int SpotEnumCustom = 7;      // SpotEnum.Custom
        private const int SpotReasonTrackMap = 1;  // SpotReason.TrackMap

        // Игре нужно время посчитать путь: TrackPathConditionConfig.tickFrame = 30 кадров.
        private const float FarmWalkTrackCompareDelay = 1.5f;

        private float farmWalkTrackCompareAt = -1f;
        private Vector3 farmWalkTrackCompareTarget;
        private IntPtr farmWalkAddSpotMethod;
        private IntPtr farmWalkStartTrackMapSignMethod;
        private IntPtr farmWalkTrackModuleClass;
        private bool farmWalkTrackApiTried;

        private bool EnsureFarmWalkTrackApi()
        {
            if (this.farmWalkTrackApiTried)
            {
                return this.farmWalkAddSpotMethod != IntPtr.Zero && this.farmWalkStartTrackMapSignMethod != IntPtr.Zero;
            }

            this.farmWalkTrackApiTried = true;
            try
            {
                IntPtr spotClass = this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.MapSpot.MapSpotProtocolManager");
                if (spotClass != IntPtr.Zero)
                {
                    // AddSpot(SpotEnum, int, Vector3, SpotReason, GameSceneId) — 5 параметров,
                    // последний с значением по умолчанию, но в Mono сигнатура полная.
                    this.farmWalkAddSpotMethod = this.FindAuraMonoMethodOnHierarchy(spotClass, "AddSpot", 5);
                }

                IntPtr trackClass = this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.Track.TrackProtocolManager");
                if (trackClass != IntPtr.Zero)
                {
                    this.farmWalkStartTrackMapSignMethod = this.FindAuraMonoMethodOnHierarchy(trackClass, "StartLocalTrackMapSign", 1);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[TrackCompare] resolve threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            bool ready = this.farmWalkAddSpotMethod != IntPtr.Zero && this.farmWalkStartTrackMapSignMethod != IntPtr.Zero;
            ModLogger.Msg("[TrackCompare] " + (ready ? "ready" : "UNAVAILABLE")
                + " (AddSpot=" + (this.farmWalkAddSpotMethod != IntPtr.Zero)
                + ", StartLocalTrackMapSign=" + (this.farmWalkStartTrackMapSignMethod != IntPtr.Zero) + ").");
            return ready;
        }

        // Просим игру проложить маршрут к тому же узлу. Вызывается на старте каждого прохода.
        internal unsafe void RequestGameTrackForWalk(Vector3 node)
        {
            if (!this.farmWalkTrackCompareEnabled || !this.EnsureFarmWalkTrackApi())
            {
                return;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                int category = SpotEnumCustom;
                int useId = FarmWalkTrackSpotUseId;
                Vector3 pos = node;
                int reason = SpotReasonTrackMap;
                int sceneId = 0; // GameSceneId.StarTown — значение по умолчанию сигнатуры
                IntPtr* addArgs = stackalloc IntPtr[5];
                addArgs[0] = (IntPtr)(&category);
                addArgs[1] = (IntPtr)(&useId);
                addArgs[2] = (IntPtr)(&pos);
                addArgs[3] = (IntPtr)(&reason);
                addArgs[4] = (IntPtr)(&sceneId);
                auraMonoRuntimeInvoke(this.farmWalkAddSpotMethod, IntPtr.Zero, (IntPtr)addArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    ModLogger.Msg("[TrackCompare] AddSpot threw inside mono.");
                    return;
                }

                uint signId = FarmWalkTrackSpotUseId;
                IntPtr* trackArgs = stackalloc IntPtr[1];
                trackArgs[0] = (IntPtr)(&signId);
                auraMonoRuntimeInvoke(this.farmWalkStartTrackMapSignMethod, IntPtr.Zero, (IntPtr)trackArgs, ref exc);

                this.farmWalkTrackCompareTarget = node;
                this.farmWalkTrackCompareAt = Time.unscaledTime + FarmWalkTrackCompareDelay;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[TrackCompare] request threw: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Тикает из фермы. Через задержку читает путь игры и печатает сравнение.
        internal void ProcessFarmWalkTrackCompare()
        {
            if (this.farmWalkTrackCompareAt < 0f || Time.unscaledTime < this.farmWalkTrackCompareAt)
            {
                return;
            }

            this.farmWalkTrackCompareAt = -1f;
            try
            {
                this.LogGameTrackPath();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[TrackCompare] read threw: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private unsafe void LogGameTrackPath()
        {
            if (auraMonoClassGetType == null || auraMonoTypeGetObject == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return;
            }

            if (this.farmWalkTrackModuleClass == IntPtr.Zero)
            {
                this.farmWalkTrackModuleClass = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.GameplaySystem.TrackingPoint", "TrackingPathModule", TrackPathModuleImageNames);
            }

            if (this.farmWalkTrackModuleClass == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackCompare] TrackingPathModule class not found.");
                return;
            }

            // ViewModule без статического Instance — только безопасный маршрут через
            // Managers.GetModule(Type); Type.GetType(string) здесь роняет рантайм.
            IntPtr monoType = auraMonoClassGetType(this.farmWalkTrackModuleClass);
            IntPtr typeObj = monoType != IntPtr.Zero ? auraMonoTypeGetObject(this.auraMonoRootDomain, monoType) : IntPtr.Zero;
            IntPtr managersClass = this.FindAuraMonoClassInImages("XDTGame.Framework", "Managers", TrackPathManagersImageNames);
            IntPtr getModule = managersClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(managersClass, "GetModule", 1)
                : IntPtr.Zero;
            if (typeObj == IntPtr.Zero || getModule == IntPtr.Zero)
            {
                return;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = typeObj;
            IntPtr moduleObj = auraMonoRuntimeInvoke(getModule, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || moduleObj == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackCompare] TrackingPathModule not active.");
                return;
            }

            this.TryGetMonoBoolMember(moduleObj, "isNavigating", out bool navigating);
            if (!this.TryGetMonoObjectMember(moduleObj, "_path", out IntPtr pathObj) || pathObj == IntPtr.Zero
                || !this.TryGetMonoIntMember(pathObj, "Count", out int gameCount))
            {
                ModLogger.Msg("[TrackCompare] _path unreadable (navigating=" + navigating + ").");
                return;
            }

            // Длина игрового маршрута — прямое сравнение с нашим.
            float gameLength = 0f;
            Vector3 prev = Vector3.zero;
            bool havePrev = false;
            string firstCorner = "-";
            for (int i = 0; i < gameCount; i++)
            {
                if (!this.TryGetAuraMonoListVector3Item(pathObj, i, out Vector3 corner))
                {
                    continue;
                }

                if (i == 0)
                {
                    firstCorner = FormatNavMeshVector(corner);
                }

                if (havePrev)
                {
                    gameLength += Vector3.Distance(prev, corner);
                }

                prev = corner;
                havePrev = true;
            }

            // Наш маршрут считаем ОТ ТЕКУЩЕЙ ПОЗИЦИИ по оставшимся углам: полная длина включает
            // уже пройденный префикс, и сравнение с игровым путём (он всегда начинается у игрока)
            // получалось не в те ворота.
            bool haveSelf = this.TryGetNavMeshSelfPosition(out Vector3 selfPos, out _);
            float modLength = 0f;
            Vector3 modPrev = haveSelf ? selfPos : Vector3.zero;
            bool modHavePrev = haveSelf;
            int modRemaining = 0;
            for (int i = Mathf.Max(0, this.farmWalkCornerIndex); i < this.farmWalkCorners.Count; i++)
            {
                Vector3 corner = this.farmWalkCorners[i];
                if (modHavePrev)
                {
                    modLength += Vector3.Distance(modPrev, corner);
                }

                modPrev = corner;
                modHavePrev = true;
                modRemaining++;
            }

            float straight = haveSelf ? Vector3.Distance(selfPos, this.farmWalkTrackCompareTarget) : -1f;

            // gameCount — НЕ число углов: GetPath ресемплит ломаную сплайном Catmull-Rom в
            // фиксированные PointCount/2 + 1 точек (обычно 51). Сравнивать имеет смысл только длину.
            ModLogger.Msg("[TrackCompare] target=" + FormatNavMeshVector(this.farmWalkTrackCompareTarget)
                + " straight=" + (straight >= 0f ? straight.ToString("F1") + "m" : "?")
                + " | GAME: " + gameLength.ToString("F1") + "m over " + gameCount + " spline pts, start=" + firstCorner
                + " (navigating=" + navigating + ")"
                + " | MOD: " + modLength.ToString("F1") + "m over " + modRemaining + " remaining corners"
                + " (route " + this.farmWalkCorners.Count + " total, at " + this.farmWalkCornerIndex + ")"
                + " | game/mod=" + (modLength > 0.01f ? (gameLength / modLength).ToString("F1") + "x" : "?"));
        }
    }
}
