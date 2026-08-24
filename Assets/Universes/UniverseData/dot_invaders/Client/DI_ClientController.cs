using UnityEngine;
#if !UNITY_SERVER
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Transporting;
using TMPro;
using UnityEngine.InputSystem;
#endif

namespace Universes.UniverseData.dot_invaders {
#if !UNITY_SERVER
    public struct DI_SendRequest : IBroadcast {
        public int sourceBaseId;
        public int targetBaseId;
    }

    public struct DI_StateBroadcast : IBroadcast {
        public int revision;
        public int yourClientId;
        public int yourTeamId;
        public int secondsRemaining;
        public bool matchEnded;
        public int winningTeamId;
        public Vector2[] basePositions;
        public int[] baseTroops;
        public int[] baseOwners;
        public int[] baseTeams;
        public int[] basePendingTroops;
        public int[] linkSources;
        public int[] linkTargets;
        public int[] dotIds;
        public Vector2[] dotPositions;
        public int[] dotTeams;
    }
#endif

    public sealed class DI_ClientController : MonoBehaviour {
#if !UNITY_SERVER
        static readonly Color[] TeamColors = {
            new(0.20f, 0.62f, 1f),
            new(1f, 0.29f, 0.34f),
            new(0.27f, 0.90f, 0.48f),
            new(1f, 0.72f, 0.20f),
            new(0.72f, 0.38f, 1f),
            new(0.10f, 0.90f, 0.88f),
            new(1f, 0.36f, 0.76f),
            new(0.72f, 0.82f, 0.20f)
        };

        [Header("View Prefabs")]
        [SerializeField] GameObject basePrefab;
        [SerializeField] GameObject dotPrefab;
        [SerializeField] GameObject linkPrefab;

        readonly List<DI_BaseView> baseViews = new();
        readonly List<DI_LinkView> linkViews = new();
        readonly Dictionary<int, DI_DotView> dotViews = new();
        readonly HashSet<int> activeDotIds = new();
        readonly List<int> removedDotIds = new();

        DI_StateBroadcast state;
        Transform runtimeRoot;
        DI_LinkView dragLink;
        TextMeshPro boardTitle;
        DI_HomeBaseTeleporter homeBaseTeleporter;
        int dragSource = -1;
        bool registered;
        bool focusedOnHome;

        void Awake() {
            runtimeRoot = new GameObject("Runtime Views").transform;
            runtimeRoot.SetParent(transform, false);
            boardTitle = transform.Find("Title")?.GetComponent<TextMeshPro>();
            homeBaseTeleporter = GetComponent<DI_HomeBaseTeleporter>();

            if (linkPrefab != null) {
                dragLink = Instantiate(linkPrefab, runtimeRoot).GetComponent<DI_LinkView>();
                if (dragLink != null)
                    dragLink.gameObject.SetActive(false);
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
            SynchronizeViews();

            if (dragSource >= 0 &&
                (dragSource >= state.baseOwners.Length || state.baseOwners[dragSource] != state.yourClientId))
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
                    GetTeamColor(state.baseTeams[i]));
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
                    new Color(0.24f, 0.31f, 0.43f, 1f),
                    0.16f);
            }

            int dotCount = state.dotIds == null || state.dotPositions == null || state.dotTeams == null
                ? 0
                : Mathf.Min(state.dotIds.Length, Mathf.Min(state.dotPositions.Length, state.dotTeams.Length));
            SynchronizeDots(dotCount);
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
            boardTitle.text = $"DOT INVADERS  -  {minutes}:{seconds:00}  -  DRAG TO RETARGET / RMB TO STOP";
        }

        static string GetTeamName(int teamId) {
            string color = GetTeamColorName(teamId);
            return teamId >= 100 ? $"{color} NPC TEAM" : $"{color} TEAM";
        }

        static string GetTeamColorName(int teamId) {
            string[] names = { "BLUE", "RED", "GREEN", "ORANGE", "PURPLE", "CYAN", "PINK", "LIME" };
            return names[Mathf.Abs(teamId) % names.Length];
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
                if (dragSource >= 0)
                    CancelDrag();
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || state.basePositions == null || Camera.main == null)
                return;

            Vector2 screenPosition = mouse.position.ReadValue();
            if (mouse.rightButton.wasPressedThisFrame && TryFindBase(screenPosition, out DI_BaseView stoppedBase) &&
                stoppedBase.BaseId >= 0 && stoppedBase.BaseId < state.baseOwners.Length &&
                state.baseOwners[stoppedBase.BaseId] == state.yourClientId &&
                state.basePendingTroops != null && stoppedBase.BaseId < state.basePendingTroops.Length &&
                state.basePendingTroops[stoppedBase.BaseId] > 0) {
                SendRequest(stoppedBase.BaseId, -1);
                if (dragSource == stoppedBase.BaseId)
                    CancelDrag();
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && TryFindBase(screenPosition, out DI_BaseView source) &&
                source.BaseId >= 0 && source.BaseId < state.baseOwners.Length &&
                state.baseOwners[source.BaseId] == state.yourClientId &&
                (state.baseTroops[source.BaseId] > 0 ||
                 state.basePendingTroops != null && source.BaseId < state.basePendingTroops.Length &&
                 state.basePendingTroops[source.BaseId] > 0)) {
                dragSource = source.BaseId;
                source.SetSelected(true);
                if (dragLink != null)
                    dragLink.gameObject.SetActive(true);
            }

            if (dragSource < 0)
                return;

            Vector3 start = ToWorld(state.basePositions[dragSource], 0.2f);
            Vector3 end = TryGetBoardPoint(screenPosition, out Vector3 boardPoint) ? boardPoint : start;
            bool validTarget = TryFindBase(screenPosition, out DI_BaseView hovered) &&
                               hovered.BaseId != dragSource && AreNeighbors(dragSource, hovered.BaseId);
            if (validTarget)
                end = ToWorld(state.basePositions[hovered.BaseId], 0.2f);

            dragLink?.SetLine(start, end,
                validTarget ? GetTeamColor(state.baseTeams[dragSource]) : new Color(0.75f, 0.8f, 0.9f),
                0.28f);

            if (!mouse.leftButton.wasReleasedThisFrame)
                return;

            if (validTarget) {
                SendRequest(dragSource, hovered.BaseId);
            }
            CancelDrag();
        }

        static void SendRequest(int sourceBaseId, int targetBaseId) {
            InstanceFinder.ClientManager.Broadcast(new DI_SendRequest {
                sourceBaseId = sourceBaseId,
                targetBaseId = targetBaseId
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

        bool TryFindBase(Vector2 screenPosition, out DI_BaseView baseView) {
            baseView = null;
            Ray ray = Camera.main.ScreenPointToRay(screenPosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 500f);
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < hits.Length; i++) {
                DI_BaseView candidate = hits[i].collider.GetComponentInParent<DI_BaseView>();
                if (candidate != null && hits[i].distance < nearestDistance) {
                    nearestDistance = hits[i].distance;
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
            if (dragSource >= 0 && dragSource < baseViews.Count)
                baseViews[dragSource].SetSelected(false);
            dragSource = -1;
            if (dragLink != null)
                dragLink.gameObject.SetActive(false);
        }

        bool IsValidBase(int baseId) {
            return state.basePositions != null && baseId >= 0 && baseId < state.basePositions.Length;
        }

        static Vector3 ToWorld(Vector2 point, float height) {
            return new Vector3(point.x, height, point.y);
        }

        static Color GetTeamColor(int teamId) {
            return teamId < 0
                ? new Color(0.42f, 0.46f, 0.54f)
                : TeamColors[teamId % TeamColors.Length];
        }

        void OnDestroy() {
            if (registered && InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.UnregisterBroadcast<DI_StateBroadcast>(OnState);
        }
#endif
    }

}
