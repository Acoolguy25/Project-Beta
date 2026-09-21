using UnityEngine;
using RyanAssets.Shared.Declarations;
#if !UNITY_SERVER
using System.Collections.Generic;
using FishNet;
using FishNet.Transporting;
using TMPro;
using UnityEngine.InputSystem;
using RyanAssets.Input;
#endif

namespace Universes.UniverseData.dot_invaders.Client {
    public sealed class DI_ClientController : MonoBehaviour {
#if !UNITY_SERVER
        [Header("View Prefabs")]
        [SerializeField] GameObject basePrefab;
        [SerializeField] GameObject dotPrefab;
        [SerializeField] GameObject linkPrefab;

        [Header("Pointer Assistance")]
        [SerializeField, Min(1f)] float minimumPickRadiusPixels = 24f;
        [SerializeField, Min(0f)] float pickPaddingPixels = 8f;
        [SerializeField, Min(1f)] float neighborSnapRadiusPixels = 38f;
        [SerializeField, Min(1f)] float dragSelectMinimumPixels = 8f;

        [Header("Road Visibility")]
        [SerializeField] Color roadColor = new Color(0.35f, 0.45f, 0.6f);
        [SerializeField, Min(0.05f)] float roadWidth = 0.3f;

        readonly List<DI_BaseView> baseViews = new();
        readonly List<DI_LinkView> linkViews = new();
        readonly Dictionary<int, DI_DotView> dotViews = new();
        readonly HashSet<int> activeDotIds = new();
        readonly List<int> removedDotIds = new();
        readonly HashSet<int> selectedBases = new();
        readonly List<int> prunedSelection = new();
        readonly Vector3[] marqueeCorners = new Vector3[4];

        DI_StateBroadcast state;
        Transform runtimeRoot;
        DI_LinkView dragLink;
        DI_LinkView marqueeView;
        Vector2 marqueeStart;
        bool marqueeActive;
        TextMeshPro boardTitle;
        DI_HomeBaseTeleporter homeBaseTeleporter;
        DI_PowerBar powerBar;
        int dragSource = -1;
        int hoveredBase = -1;
        int[] previewRoute = System.Array.Empty<int>();
        int routeTarget = -1;
        bool manualRoute;
        bool registered;
        bool focusedOnHome;

        void Awake() {
            runtimeRoot = new GameObject("Runtime Views").transform;
            runtimeRoot.SetParent(transform, false);
            boardTitle = transform.Find("Title")?.GetComponent<TextMeshPro>();
            homeBaseTeleporter = GetComponent<DI_HomeBaseTeleporter>();
            powerBar = new GameObject("Power HUD").AddComponent<DI_PowerBar>();
            powerBar.transform.SetParent(transform, false);

            if (linkPrefab != null) {
                dragLink = Instantiate(linkPrefab, runtimeRoot).GetComponent<DI_LinkView>();
                if (dragLink != null)
                    dragLink.gameObject.SetActive(false);
                marqueeView = Instantiate(linkPrefab, runtimeRoot).GetComponent<DI_LinkView>();
                if (marqueeView != null)
                    marqueeView.gameObject.SetActive(false);
            }

            if (basePrefab == null || dotPrefab == null || linkPrefab == null) {
                Debug.LogError("Dot Invaders board is missing one or more view prefabs.", this);
                enabled = false;
            }
        }

        void Update() {
            TryRegister();
            TryFocusHomeBase();
            HandlePointer();
        }

        void TryRegister() {
            if (registered || InstanceFinder.ClientManager == null)
                return;

            InstanceFinder.ClientManager.RegisterBroadcast<DI_StateBroadcast>(OnState);
            registered = true;
        }

        void OnState(DI_StateBroadcast next, Channel channel) {
            if (!IsValidState(next))
                return;

            if (state.matchEnded && !next.matchEnded) {
                focusedOnHome = false;
                homeBaseTeleporter?.BeginMatch();
            }

            state = next;
            routeTarget = -1;
            SynchronizeViews();
            powerBar.SetState(state, GetTeamColor, GetTeamName);

            PruneSelection();

            if (dragSource >= 0 &&
                (state.matchEnded || dragSource >= state.baseOwners.Length || state.baseOwners[dragSource] != state.yourClientId))
                CancelDrag();
        }

        static bool IsValidState(DI_StateBroadcast next) {
            return next.basePositions != null && next.baseTroops != null &&
                   next.baseOwners != null && next.baseTeams != null &&
                   next.basePositions.Length == next.baseTroops.Length &&
                   next.basePositions.Length == next.baseOwners.Length &&
                   next.basePositions.Length == next.baseTeams.Length;
        }

        void SynchronizeViews() {
            ResizeViews(baseViews, state.basePositions.Length, basePrefab);
            for (int i = 0; i < baseViews.Count; i++) {
                int pending = state.basePendingTroops != null && i < state.basePendingTroops.Length
                    ? state.basePendingTroops[i]
                    : 0;
                baseViews[i].SetState(
                    i,
                    ToWorld(state.basePositions[i], 0f),
                    state.baseTroops[i],
                    pending,
                    state.baseOwners[i] == state.yourClientId,
                    GetTeamColor(state.baseTeams[i]),
                    state.baseSpeedBases != null && i < state.baseSpeedBases.Length && state.baseSpeedBases[i]);
                bool isTurret = state.baseTurrets != null && i < state.baseTurrets.Length && state.baseTurrets[i];
                int shot = state.turretShotSequences != null && i < state.turretShotSequences.Length
                    ? state.turretShotSequences[i] : 0;
                Vector3 shotPosition = state.turretShotPositions != null && i < state.turretShotPositions.Length
                    ? ToWorld(state.turretShotPositions[i], 0.8f) : Vector3.zero;
                bool isSuper = state.baseSuperProducers != null && i < state.baseSuperProducers.Length && state.baseSuperProducers[i];
                float charge = state.baseProductionCharge != null && i < state.baseProductionCharge.Length
                    ? state.baseProductionCharge[i] : 0f;
                float rate = state.baseProductionRates != null && i < state.baseProductionRates.Length
                    ? state.baseProductionRates[i] : 0f;
                baseViews[i].SetProduction(isSuper, charge, rate,
                    state.baseTeams[i] >= 0, state.baseTroops[i] >= state.superMaxCapacity);
                baseViews[i].SetDefense(isTurret, state.baseTroops[i], shot, shotPosition,
                    state.turretRange, isSuper ? state.superMaxCapacity : state.maxCapacity);
            }

            int linkCount = state.linkSources == null || state.linkTargets == null
                ? 0
                : Mathf.Min(state.linkSources.Length, state.linkTargets.Length);
            ResizeViews(linkViews, linkCount, linkPrefab);
            for (int i = 0; i < linkCount; i++) {
                int source = state.linkSources[i];
                int target = state.linkTargets[i];
                if (!IsValidBase(source) || !IsValidBase(target)) {
                    linkViews[i].gameObject.SetActive(false);
                    continue;
                }

                linkViews[i].gameObject.SetActive(true);
                linkViews[i].SetLine(
                    ToWorld(state.basePositions[source], 0.08f),
                    ToWorld(state.basePositions[target], 0.08f),
                    roadColor,
                    roadWidth);
            }

            int dotCount = state.dotIds == null || state.dotPositions == null || state.dotTeams == null
                ? 0
                : Mathf.Min(state.dotIds.Length, Mathf.Min(state.dotPositions.Length, state.dotTeams.Length));
            SynchronizeDots(dotCount);
            RefreshInteraction();
            UpdateBoardTitle();
        }

        void SynchronizeDots(int dotCount) {
            activeDotIds.Clear();
            for (int i = 0; i < dotCount; i++) {
                int dotId = state.dotIds[i];
                activeDotIds.Add(dotId);
                if (!dotViews.TryGetValue(dotId, out DI_DotView view)) {
                    view = Instantiate(dotPrefab, runtimeRoot).GetComponent<DI_DotView>();
                    if (view == null) {
                        Debug.LogError($"Prefab '{dotPrefab.name}' does not contain {nameof(DI_DotView)}.", dotPrefab);
                        return;
                    }
                    dotViews.Add(dotId, view);
                }
                view.SetState(ToWorld(state.dotPositions[i], 0.8f), GetTeamColor(state.dotTeams[i]));
            }

            removedDotIds.Clear();
            foreach (KeyValuePair<int, DI_DotView> entry in dotViews) {
                if (!activeDotIds.Contains(entry.Key))
                    removedDotIds.Add(entry.Key);
            }
            for (int i = 0; i < removedDotIds.Count; i++) {
                int dotId = removedDotIds[i];
                Destroy(dotViews[dotId].gameObject);
                dotViews.Remove(dotId);
            }
        }

        void UpdateBoardTitle() {
            if (boardTitle == null)
                return;

            if (state.matchEnded) {
                if (state.winningTeamId < 0)
                    boardTitle.text = "DOT INVADERS  -  DRAW";
                else if (state.winningTeamId == state.yourTeamId)
                    boardTitle.text = $"DOT INVADERS  -  {GetTeamName(state.winningTeamId)} WINS  -  VICTORY";
                else
                    boardTitle.text = $"DOT INVADERS  -  {GetTeamName(state.winningTeamId)} WINS";
                return;
            }

            int minutes = Mathf.Max(0, state.secondsRemaining) / 60;
            int seconds = Mathf.Max(0, state.secondsRemaining) % 60;
            boardTitle.text = $"DOT INVADERS  -  {minutes}:{seconds:00}  -  DRAG TO ROUTE / DRAG EMPTY SPACE TO SELECT / ESC CANCEL / RMB STOP";
        }

        static string GetTeamName(int teamId) {
            string name = DI_Rules.GetTeamColor(teamId).ToString().ToUpperInvariant();
            return teamId >= 100 ? $"{name} NPC TEAM" : $"{name} TEAM";
        }

        void ResizeViews<T>(List<T> views, int count, GameObject prefab) where T : Component {
            while (views.Count < count) {
                T view = Instantiate(prefab, runtimeRoot).GetComponent<T>();
                if (view == null) {
                    Debug.LogError($"Prefab '{prefab.name}' does not contain {typeof(T).Name}.", prefab);
                    return;
                }
                views.Add(view);
            }

            while (views.Count > count) {
                int last = views.Count - 1;
                Destroy(views[last].gameObject);
                views.RemoveAt(last);
            }
        }

        void HandlePointer() {
            if (state.matchEnded) {
                if (dragSource >= 0 || hoveredBase >= 0 || selectedBases.Count > 0 || marqueeActive)
                    ClearSelection();
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || state.basePositions == null || Camera.main == null || !Application.isFocused ||
                InputService.characterControls == null || !InputService.characterControls.GameplayInputActive) {
                CancelDrag();
                return;
            }

            Vector2 screenPosition = mouse.position.ReadValue();
            if (powerBar != null && powerBar.ContainsPointer(screenPosition)) {
                CancelDrag();
                return;
            }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) {
                ClearSelection();
                return;
            }
            if (mouse.rightButton.wasPressedThisFrame && dragSource >= 0) {
                CancelDrag();
                return;
            }
            if (mouse.rightButton.wasPressedThisFrame && TryFindBase(screenPosition, out DI_BaseView stoppedBase, true) &&
                stoppedBase.BaseId >= 0 && stoppedBase.BaseId < state.baseOwners.Length &&
                state.baseOwners[stoppedBase.BaseId] == state.yourClientId) {
                // Stopping a base that belongs to a selection stops the whole group.
                if (selectedBases.Contains(stoppedBase.BaseId))
                    foreach (int baseId in selectedBases)
                        SendRequest(baseId, -1);
                else
                    SendRequest(stoppedBase.BaseId, -1);
                if (dragSource == stoppedBase.BaseId)
                    CancelDrag();
                return;
            }

            if (UpdateMarquee(mouse, screenPosition))
                return;

            if (mouse.leftButton.wasPressedThisFrame && TryFindBase(screenPosition, out DI_BaseView source, true) &&
                source.BaseId >= 0 && source.BaseId < state.baseOwners.Length &&
                state.baseOwners[source.BaseId] == state.yourClientId &&
                (state.baseTroops[source.BaseId] > 0 ||
                 state.basePendingTroops != null && source.BaseId < state.basePendingTroops.Length &&
                 state.basePendingTroops[source.BaseId] > 0)) {
                dragSource = source.BaseId;
                // Dragging from outside the group replaces the group with that base.
                if (!selectedBases.Contains(dragSource)) {
                    selectedBases.Clear();
                    selectedBases.Add(dragSource);
                }
                manualRoute = InputService.characterControls.RouteModifier;
                previewRoute = new[] { dragSource };
                routeTarget = -1;
                source.SetSelected(true);
                if (dragLink != null)
                    dragLink.gameObject.SetActive(true);
            }

            if (dragSource < 0) {
                hoveredBase = TryFindBase(screenPosition, out DI_BaseView idleHover, true) ? idleHover.BaseId : -1;
                RefreshInteraction();
                return;
            }

            Vector3 start = ToWorld(state.basePositions[dragSource], 0.2f);
            Vector3 end = TryGetBoardPoint(screenPosition, out Vector3 boardPoint) ? boardPoint : start;
            if (InputService.characterControls.RouteModifier && !manualRoute) {
                manualRoute = true;
                previewRoute = new[] { dragSource };
                routeTarget = -1;
            }
            bool validTarget = TryFindBase(screenPosition, out DI_BaseView hovered, false, !manualRoute);
            int targetId = validTarget ? hovered.BaseId : -1;
            if (routeTarget != targetId) {
                routeTarget = targetId;
                previewRoute = manualRoute
                    ? DI_Rules.AppendRouteWaypoint(state.basePositions, state.linkSources, state.linkTargets, previewRoute, targetId)
                    : DI_Rules.FindRoute(state.basePositions, state.linkSources, state.linkTargets, dragSource, targetId);
            }
            validTarget = previewRoute.Length >= 2 && (manualRoute || validTarget);
            if (validTarget)
                end = ToWorld(state.basePositions[previewRoute[previewRoute.Length - 1]], 0.2f);
            hoveredBase = validTarget ? previewRoute[previewRoute.Length - 1] : -1;
            RefreshInteraction();

            if (validTarget)
                dragLink?.SetRoute(state.basePositions, previewRoute, Color.Lerp(GetTeamColor(state.baseTeams[dragSource]), Color.white, 0.4f), 0.3f);
            else
                dragLink?.SetLine(start, end, new Color(0.75f, 0.8f, 0.9f), 0.18f);

            if (!mouse.leftButton.wasReleasedThisFrame) {
                if (!mouse.leftButton.isPressed)
                    CancelDrag();
                return;
            }

            if (validTarget)
                SendGroupRequest(previewRoute[previewRoute.Length - 1]);
            CancelDrag();
        }

        /// <summary>
        /// Drag-selection on empty board space. Returns true while the marquee owns the pointer.
        /// </summary>
        bool UpdateMarquee(Mouse mouse, Vector2 screenPosition) {
            if (!marqueeActive) {
                if (dragSource >= 0 || !mouse.leftButton.wasPressedThisFrame ||
                    TryFindBase(screenPosition, out DI_BaseView _))
                    return false;

                marqueeActive = true;
                marqueeStart = screenPosition;
                if (marqueeView != null)
                    marqueeView.gameObject.SetActive(true);
                return true;
            }

            Rect rect = ScreenRect(marqueeStart, screenPosition);
            if (mouse.leftButton.isPressed && !mouse.leftButton.wasReleasedThisFrame) {
                DrawMarquee(rect);
                return true;
            }

            ApplyMarqueeSelection(rect);
            EndMarquee();
            return true;
        }

        void DrawMarquee(Rect rect) {
            if (marqueeView == null)
                return;

            if (!TryGetBoardPoint(new Vector2(rect.xMin, rect.yMin), out marqueeCorners[0]) ||
                !TryGetBoardPoint(new Vector2(rect.xMax, rect.yMin), out marqueeCorners[1]) ||
                !TryGetBoardPoint(new Vector2(rect.xMax, rect.yMax), out marqueeCorners[2]) ||
                !TryGetBoardPoint(new Vector2(rect.xMin, rect.yMax), out marqueeCorners[3]))
                return;

            marqueeView.SetLoop(marqueeCorners, new Color(0.75f, 0.9f, 1f), 0.12f);
        }

        void ApplyMarqueeSelection(Rect rect) {
            selectedBases.Clear();
            Camera camera = Camera.main;
            // A click without a meaningful drag is a deselect, not a zero-size box.
            if (camera == null || rect.width < dragSelectMinimumPixels && rect.height < dragSelectMinimumPixels) {
                RefreshInteraction();
                return;
            }

            foreach (DI_BaseView candidate in baseViews) {
                int id = candidate.BaseId;
                if (!IsValidBase(id) || id >= state.baseOwners.Length || state.baseOwners[id] != state.yourClientId)
                    continue;
                Vector3 center = camera.WorldToScreenPoint(candidate.transform.position);
                if (center.z > 0f && rect.Contains(center))
                    selectedBases.Add(id);
            }
            RefreshInteraction();
        }

        void EndMarquee() {
            marqueeActive = false;
            if (marqueeView != null)
                marqueeView.gameObject.SetActive(false);
        }

        static Rect ScreenRect(Vector2 a, Vector2 b) {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>Routes every selected base to the target; the drag source keeps its previewed route.</summary>
        void SendGroupRequest(int targetBaseId) {
            SendRequest(dragSource, targetBaseId, previewRoute);
            foreach (int baseId in selectedBases) {
                if (baseId == dragSource || !IsValidBase(baseId) || baseId >= state.baseOwners.Length ||
                    state.baseOwners[baseId] != state.yourClientId)
                    continue;
                int[] route = DI_Rules.FindRoute(state.basePositions, state.linkSources, state.linkTargets, baseId, targetBaseId);
                if (route.Length >= 2)
                    SendRequest(baseId, targetBaseId, route);
            }
        }

        void PruneSelection() {
            if (selectedBases.Count == 0)
                return;

            prunedSelection.Clear();
            foreach (int baseId in selectedBases)
                if (!IsValidBase(baseId) || baseId >= state.baseOwners.Length ||
                    state.baseOwners[baseId] != state.yourClientId)
                    prunedSelection.Add(baseId);
            for (int i = 0; i < prunedSelection.Count; i++)
                selectedBases.Remove(prunedSelection[i]);
        }

        void ClearSelection() {
            selectedBases.Clear();
            EndMarquee();
            CancelDrag();
        }

        static void SendRequest(int sourceBaseId, int targetBaseId, int[] route = null) {
            InstanceFinder.ClientManager.Broadcast(new DI_SendRequest {
                sourceBaseId = sourceBaseId,
                targetBaseId = targetBaseId,
                route = route
            });
        }

        void TryFocusHomeBase() {
            if (focusedOnHome || state.basePositions == null || state.baseOwners == null || Camera.main == null)
                return;

            Component twoDimController = Camera.main.GetComponent("TwoDimController");
            if (twoDimController == null)
                return;

            int count = Mathf.Min(state.basePositions.Length, state.baseOwners.Length);
            for (int i = 0; i < count; i++) {
                if (state.baseOwners[i] != state.yourClientId)
                    continue;

                twoDimController.SendMessage("SetFocusPoint", ToWorld(state.basePositions[i], 0f),
                    SendMessageOptions.RequireReceiver);
                homeBaseTeleporter?.SetHomeBase(ToWorld(state.basePositions[i], 0f));
                focusedOnHome = true;
                return;
            }
        }

        bool TryFindBase(Vector2 screenPosition, out DI_BaseView baseView, bool ownedOnly = false, bool targetOnly = false) {
            baseView = null;
            Camera camera = Camera.main;
            if (camera == null)
                return false;
            float nearestDistance = float.MaxValue;
            foreach (DI_BaseView candidate in baseViews) {
                int id = candidate.BaseId;
                if (!IsValidBase(id) || ownedOnly && state.baseOwners[id] != state.yourClientId ||
                    targetOnly && id == dragSource)
                    continue;
                Vector3 center = camera.WorldToScreenPoint(candidate.transform.position);
                if (center.z <= 0f)
                    continue;
                Vector3 edge = camera.WorldToScreenPoint(candidate.transform.position + camera.transform.right * candidate.PickRadius);
                float radius = Mathf.Max(minimumPickRadiusPixels, Vector2.Distance(center, edge) + pickPaddingPixels);
                if (targetOnly)
                    radius = Mathf.Max(radius, neighborSnapRadiusPixels);
                float distance = (screenPosition - (Vector2)center).sqrMagnitude;
                if (distance <= radius * radius && distance < nearestDistance) {
                    nearestDistance = distance;
                    baseView = candidate;
                }
            }
            return baseView != null;
        }

        static bool TryGetBoardPoint(Vector2 screenPosition, out Vector3 point) {
            Ray ray = Camera.main.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, 0.2f, 0f));
            if (plane.Raycast(ray, out float distance)) {
                point = ray.GetPoint(distance);
                return true;
            }

            point = default;
            return false;
        }

        bool AreNeighbors(int source, int target) {
            if (state.linkSources == null || state.linkTargets == null)
                return false;

            int count = Mathf.Min(state.linkSources.Length, state.linkTargets.Length);
            for (int i = 0; i < count; i++) {
                if ((state.linkSources[i] == source && state.linkTargets[i] == target) ||
                    (state.linkSources[i] == target && state.linkTargets[i] == source))
                    return true;
            }
            return false;
        }

        void CancelDrag() {
            EndMarquee();
            if (dragSource >= 0 && dragSource < baseViews.Count)
                baseViews[dragSource].SetSelected(false);
            dragSource = -1;
            hoveredBase = -1;
            routeTarget = -1;
            manualRoute = false;
            previewRoute = System.Array.Empty<int>();
            if (dragLink != null)
                dragLink.gameObject.SetActive(false);
            RefreshInteraction();
        }

        void RefreshInteraction() {
            for (int i = 0; i < baseViews.Count; i++)
                baseViews[i].SetInteraction(i == dragSource || selectedBases.Contains(i),
                    dragSource >= 0 && (AreNeighbors(dragSource, i) || System.Array.IndexOf(previewRoute, i) >= 0), i == hoveredBase);

            for (int i = 0; i < linkViews.Count; i++) {
                int source = state.linkSources[i], target = state.linkTargets[i];
                if (!IsValidBase(source) || !IsValidBase(target))
                    continue;
                bool highlighted = dragSource >= 0 && (source == dragSource || target == dragSource);
                bool onRoute = RouteContainsLink(previewRoute, source, target);
                bool queued = false;
                if (dragSource < 0 && hoveredBase >= 0 && state.baseRoutes != null && hoveredBase < state.baseRoutes.Length)
                    queued = RouteContainsLink(state.baseRoutes[hoveredBase].baseIds, source, target);
                Vector3 start = ToWorld(state.basePositions[source], 0.12f);
                Vector3 end = ToWorld(state.basePositions[target], 0.12f);
                Vector3 direction = (end - start).normalized;
                // End roads at the edge of the enlarged base, keeping the labels clear.
                start += direction * baseViews[source].PickRadius;
                end -= direction * baseViews[target].PickRadius;
                linkViews[i].SetLine(start, end,
                    onRoute ? Color.white : queued ? new Color(0.3f, 0.85f, 1f) :
                        highlighted ? new Color(0.9f, 0.7f, 0.2f) : roadColor,
                    roadWidth * (onRoute || queued ? 1.6f : highlighted ? 1.3f : 1f));
            }
        }

        static bool RouteContainsLink(int[] route, int source, int target) {
            if (route == null)
                return false;
            for (int i = 1; i < route.Length; i++)
                if (route[i - 1] == source && route[i] == target || route[i - 1] == target && route[i] == source)
                    return true;
            return false;
        }

        void OnDisable() => ClearSelection();

        bool IsValidBase(int baseId) {
            return state.basePositions != null && baseId >= 0 && baseId < state.basePositions.Length;
        }

        static Vector3 ToWorld(Vector2 point, float height) {
            return new Vector3(point.x, height, point.y);
        }

        static Color GetTeamColor(int teamId) {
            return TeamConfig.TeamToColor(DI_Rules.GetTeamColor(teamId));
        }

        void OnDestroy() {
            if (registered && InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.UnregisterBroadcast<DI_StateBroadcast>(OnState);
        }
#endif
    }

}
