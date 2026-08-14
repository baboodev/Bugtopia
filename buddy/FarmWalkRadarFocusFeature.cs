using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Отрисовка маршрута мода поверх мира.
    //
    // Игра рисует свой Track цепочкой звёздочек; здесь рядом ложится наша ломаная по углам
    // маршрута — это и есть сравнение путей прямо в кадре, без лога.
    //
    // ⚠️ ФИЛЬТР РАДАРА УДАЛЁН. Он оставлял на радаре только текущую цель, и это ломало ферму:
    // FindClosestAvailableNode перечисляет ИМЕННО маркеры радара, так что фильтр отбирал у
    // сканера всех кандидатов кроме одного — мод переставал знать, куда идти дальше, и уезжал
    // менять зону. Не возвращать. Если понадобится «показывать только цель», это должен быть
    // отдельный визуальный слой, не влияющий на маркеры, которые читает скан.
    public partial class HeartopiaComplete
    {
        // Имя объекта линии. RunRadar сносит всех детей контейнера кроме отслеживаемых маркеров —
        // линия должна пережить эту зачистку, иначе будет мигать раз в 2 секунды.
        internal const string FarmWalkRouteLineName = "FarmWalkRouteLine";

        private const float FarmWalkRouteLineWidth = 0.14f;
        private const float FarmWalkRouteLineLift = 0.4f;

        private GameObject farmWalkRouteLineObject;
        private LineRenderer farmWalkRouteLine;
        private readonly List<Vector3> farmWalkRoutePoints = new List<Vector3>();

        // Линия висит на тумблере Compare Game Track, а не на самом Walk to Nodes: она существует
        // ровно для сверки с игровыми звёздочками, так что оба диагностических следа выключаются
        // одним переключателем. При выключенном тумблере проход идёт молча.
        private bool IsFarmWalkRadarFocusActive(out Vector3 target)
        {
            target = Vector3.zero;
            if (!this.farmWalkToNodeEnabled || !this.farmWalkTrackCompareEnabled || !this.farmWalkActive)
            {
                return false;
            }

            target = this.farmWalkTarget;
            return true;
        }

        // Тикает каждый кадр из UpdateMarkers. Держит ломаную от игрока по оставшимся углам.
        internal void SyncFarmWalkRouteLine(Material lineMaterial)
        {
            if (!this.IsFarmWalkRadarFocusActive(out Vector3 target) || this.radarContainer == null)
            {
                this.ClearFarmWalkRouteLine();
                return;
            }

            if (!this.TryGetNavMeshSelfPosition(out Vector3 selfPos, out _))
            {
                this.ClearFarmWalkRouteLine();
                return;
            }

            this.farmWalkRoutePoints.Clear();
            this.farmWalkRoutePoints.Add(selfPos + new Vector3(0f, FarmWalkRouteLineLift, 0f));
            for (int i = this.farmWalkCornerIndex; i < this.farmWalkCorners.Count; i++)
            {
                this.farmWalkRoutePoints.Add(this.farmWalkCorners[i] + new Vector3(0f, FarmWalkRouteLineLift, 0f));
            }

            // Цель добавляем, только если последний угол — не она сама.
            Vector3 lifted = target + new Vector3(0f, FarmWalkRouteLineLift, 0f);
            if ((this.farmWalkRoutePoints[this.farmWalkRoutePoints.Count - 1] - lifted).sqrMagnitude > 0.01f)
            {
                this.farmWalkRoutePoints.Add(lifted);
            }

            if (this.farmWalkRoutePoints.Count < 2)
            {
                this.ClearFarmWalkRouteLine();
                return;
            }

            if (this.farmWalkRouteLineObject == null)
            {
                this.farmWalkRouteLineObject = new GameObject(FarmWalkRouteLineName);
                this.farmWalkRouteLineObject.transform.SetParent(this.radarContainer.transform);
                this.farmWalkRouteLineObject.transform.position = Vector3.zero;
                this.farmWalkRouteLine = this.farmWalkRouteLineObject.AddComponent<LineRenderer>();
                this.farmWalkRouteLine.useWorldSpace = true;
                this.farmWalkRouteLine.startWidth = (this.farmWalkRouteLine.endWidth = FarmWalkRouteLineWidth);
            }

            if (this.farmWalkRouteLine == null)
            {
                return;
            }

            if (lineMaterial != null && this.farmWalkRouteLine.material != lineMaterial)
            {
                this.farmWalkRouteLine.material = lineMaterial;
            }

            // Ярко-зелёный: игровые звёздочки золотистые, спутать нельзя.
            Color routeColor = new Color(0.25f, 1f, 0.4f, 0.95f);
            this.farmWalkRouteLine.startColor = (this.farmWalkRouteLine.endColor = routeColor);
            this.farmWalkRouteLine.positionCount = this.farmWalkRoutePoints.Count;
            for (int i = 0; i < this.farmWalkRoutePoints.Count; i++)
            {
                this.farmWalkRouteLine.SetPosition(i, this.farmWalkRoutePoints[i]);
            }
        }

        internal void ClearFarmWalkRouteLine()
        {
            if (this.farmWalkRouteLineObject != null)
            {
                Object.Destroy(this.farmWalkRouteLineObject);
            }

            this.farmWalkRouteLineObject = null;
            this.farmWalkRouteLine = null;
        }
    }
}
