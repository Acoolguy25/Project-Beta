using System.Collections.Generic;
using System.IO;
using System.Linq;
using RyanAssets.Authoring;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static RyanAssets.Authoring.UniverseAuthoringKit;
#if !UNITY_SERVER
using Universes.UniverseData.hide_and_seek.Client;
#endif

namespace Universes.UniverseData.hide_and_seek.Editor {
    /// <summary>
    /// Builds Hide and Seek's start scene: the murder mystery facility (copied, recentred), crate
    /// cover, hide points probed from its floors, ping marker and HUD. Safe to re-run. Build the
    /// server runner from the Linux Server profile with
    /// <c>-executeMethod Universes.UniverseData.hide_and_seek.Editor.HS_Authoring.BuildServerAssets</c>.
    /// </summary>
    public static class HS_Authoring {
        public const string Universe = "hide_and_seek";
        public const string Root = "Assets/Universes/UniverseData/hide_and_seek";
        const string SourceScene = "Assets/Universes/UniverseData/murder_mystery/murder_mystery_start.unity";
        const string SourceRoot = "Map";
        static readonly UniverseAuthoringKit Kit = new(Root);
        const string ServerPrefab = Root + "/Server/HS_ServerRunner.prefab";

        [MenuItem("Ryan/Hide and Seek/Build Client Assets")]
        public static void BuildClientAssets() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            Kit.EnsureFolders("Prefabs", "Materials", "Textures", "Server");
            BuildTextures();
            BuildStartScene();
            RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));
            AssetDatabase.SaveAssets();
            Debug.Log("[HideAndSeek] Client assets built.");
        }

        public static void BuildClientAssetsBatch() {
            BuildClientAssets();
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) RenderPreviews();
        }

        [MenuItem("Ryan/Hide and Seek/Build Server Assets (Linux Server profile)")]
        public static void BuildServerAssets() {
#if UNITY_SERVER
            Kit.EnsureFolders("Server");
            Kit.BuildServerRunner<Universes.UniverseData.hide_and_seek.Server.HS_ServerRunner>(ServerPrefab, Kit.ServerScene(Universe));
            RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));
            Debug.Log("[HideAndSeek] Server assets built.");
#else
            throw new System.InvalidOperationException("Switch to the Linux Server build profile (UNITY_SERVER) to build the server runner prefab.");
#endif
        }

        static void BuildTextures() {
            Kit.SaveTexture("Crate", (u, v) => {
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float plank = Mathf.Repeat(v * 5f, 1f);
                float grain = 0.85f + 0.15f * Hash(Mathf.Floor(u * 48f), Mathf.Floor(v * 6f));
                bool brace = Mathf.Abs(u - v) < 0.06f || edge < 0.08f;
                Color wood = new Color(0.62f, 0.43f, 0.24f) * grain;
                return brace ? new Color(0.42f, 0.27f, 0.14f) : plank < 0.06f ? wood * 0.7f : wood;
            });
            Kit.SaveTexture("Stripes", (u, v) => Mathf.Repeat(u + v, 0.5f) < 0.25f ? new Color(1f, 0.82f, 0.2f) : new Color(0.12f, 0.12f, 0.14f));
        }

        // ---------------------------------------------------------------- prefabs
        static GameObject BuildPing() {
            var root = new GameObject("PingMarker");
            var beam = Kit.Mat("PingBeam", new Color(1f, 0.25f, 0.2f), emission: 2.5f, alpha: 0.35f);
            var core = Kit.Mat("PingCore", new Color(1f, 0.3f, 0.25f), emission: 3f);
            NoShadows(Primitive(PrimitiveType.Cylinder, "Beam", root.transform, new Vector3(0f, 12f, 0f), new Vector3(1.2f, 12f, 1.2f), beam, false));
            NoShadows(Primitive(PrimitiveType.Cylinder, "Ring", root.transform, new Vector3(0f, 0.05f, 0f), new Vector3(HS_Rules.PingScatter * 2f, 0.04f, HS_Rules.PingScatter * 2f), beam, false));
            NoShadows(Primitive(PrimitiveType.Sphere, "Core", root.transform, new Vector3(0f, 2f, 0f), Vector3.one * 1.2f, core, false));
            return Kit.Save(root, "", "PingMarker");
        }

        static GameObject BuildHud() {
#if UNITY_SERVER
            return AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/HideAndSeekHUD.prefab");
#else
            var root = Canvas("HideAndSeekHUD");
            var hud = root.AddComponent<HS_ClientController>();
            Color white = new(0.97f, 0.98f, 1f), yellow = new(1f, 0.85f, 0.25f);
            var blind = Panel("Blindfold", root.transform, Vector2.zero, Vector2.one, new Color(0.02f, 0.02f, 0.04f, 1f));
            hud.blindfold = blind.gameObject;
            hud.blindfoldLabel = Text("BlindfoldText", blind.transform, new Vector2(0.1f, 0.35f), new Vector2(0.9f, 0.65f), 54, white, TextAlignmentOptions.Center, true);
            var top = Panel("RolePanel", root.transform, new Vector2(0.33f, 0.8f), new Vector2(0.67f, 0.87f), Ink);
            hud.roleLabel = Text("Role", top.transform, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), 34, white, TextAlignmentOptions.Center, true);
            var timer = Panel("TimerPanel", root.transform, new Vector2(0.78f, 0.8f), new Vector2(0.988f, 0.87f), Ink);
            hud.timerLabel = Text("Timer", timer.transform, new Vector2(0.05f, 0f), new Vector2(0.95f, 1f), 28, yellow, TextAlignmentOptions.Center, true);
            hud.headlineLabel = Text("Headline", root.transform, new Vector2(0.3f, 0.76f), new Vector2(0.7f, 0.8f), 22, white, TextAlignmentOptions.Center);
            hud.feedLabel = Text("Feed", root.transform, new Vector2(0.68f, 0.72f), new Vector2(0.988f, 0.78f), 22, new Color(1f, 0.7f, 0.5f), TextAlignmentOptions.Right, true);
            hud.toastLabel = Text("Toast", root.transform, new Vector2(0.15f, 0.6f), new Vector2(0.85f, 0.7f), 38, yellow, TextAlignmentOptions.Center, true);
            var prompt = Panel("PromptPanel", root.transform, new Vector2(0.36f, 0.16f), new Vector2(0.64f, 0.22f), Ink);
            hud.promptLabel = Text("Prompt", prompt.transform, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), 28, new Color(1f, 0.55f, 0.45f), TextAlignmentOptions.Center, true);
            hud.sfx = Sfx(root.transform);
            hud.tagClip = Clip(LandClip);
            hud.pingClip = hud.startClip = Clip(CoinClip);
            return Kit.Save(root, "", "HideAndSeekHUD");
#endif
        }

        // ---------------------------------------------------------------- scene
        static void BuildStartScene() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var hud = BuildHud();
            var ping = BuildPing();
            var crate = Tiled(Kit.Mat("Crate", Color.white, Kit.Tex("Crate"), smoothness: 0.1f), 1f, 1f);
            var stripes = Kit.Mat("Stripes", Color.white, Kit.Tex("Stripes"));
            var sun = Kit.NewLitScene(new Color(0.55f, 0.62f, 0.8f), new Color(0.55f, 0.6f, 0.68f), 120f, 520f, new Vector3(55f, -35f, 0f), 1.2f);
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.8f, 0.88f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.62f, 0.66f);
            RenderSettings.ambientGroundColor = new Color(0.45f, 0.45f, 0.48f);
            var scene = EditorSceneManager.GetActiveScene();

            // ---- copy the facility out of the murder mystery scene
            var source = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
            var original = source.GetRootGameObjects().FirstOrDefault(g => g.name == SourceRoot)
                ?? throw new System.InvalidOperationException($"No '{SourceRoot}' root in {SourceScene}.");
            var facility = Object.Instantiate(original);
            EditorSceneManager.MoveGameObjectToScene(facility, scene);
            EditorSceneManager.CloseScene(source, true);
            facility.name = "Facility";
            foreach (var behaviour in facility.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
            foreach (var filter in facility.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && filter.GetComponent<Collider>() == null) filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            var bounds = RendererBounds(facility);
            facility.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            bounds = RendererBounds(facility);
            Physics.SyncTransforms();

            var world = new GameObject("World");
            Physics.SyncTransforms();

            // ---- probe floors for hide points
            var candidates = ProbeFloors(facility.transform, bounds, 4f);
            var random = new System.Random(1234);
            // Crates for extra cover, beside some floor points (never on the seeker start).
            // Seekers wait on the busiest storey, as close to the middle as possible.
            int storey = candidates.GroupBy(p => Mathf.RoundToInt(p.y)).OrderByDescending(g => g.Count()).First().Key;
            Vector3 centre = candidates.Where(p => Mathf.Abs(p.y - storey) < 1.5f).OrderBy(p => new Vector2(p.x, p.z).sqrMagnitude).First();
            var props = Empty("Cover", world.transform);
            int crates = 0;
            foreach (var p in candidates.OrderBy(_ => random.Next())) {
                if (crates >= 130) break;
                if (Vector3.Distance(p, centre) < 12f) continue;
                float s = 1.4f + (float)random.NextDouble() * 1.2f;
                Vector3 offset = new((float)random.NextDouble() * 3f - 1.5f, 0f, (float)random.NextDouble() * 3f - 1.5f);
                Vector3 at = p + offset + Vector3.up * (s * 0.5f + 0.02f);
                if (Physics.CheckBox(at + Vector3.up * 0.1f, new Vector3(s * 0.5f + 0.3f, s * 0.5f, s * 0.5f + 0.3f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(at, Vector3.down, s * 0.5f + 0.3f)) continue;
                var box = Cube($"Crate{crates}", props.transform, at, Vector3.one * s, crate);
                box.transform.rotation = Quaternion.Euler(0f, random.Next(0, 90), 0f);
                if (random.NextDouble() < 0.35 && !Physics.CheckBox(at + Vector3.up * s * 0.95f, Vector3.one * s * 0.36f)) {
                    var top = Cube($"Crate{crates}b", props.transform, at + Vector3.up * s * 0.85f, Vector3.one * s * 0.7f, crate);
                    top.transform.rotation = Quaternion.Euler(0f, random.Next(0, 90), 0f);
                }
                crates++;
                Physics.SyncTransforms();
            }
            // Re-probe so no hide point ends up inside a crate.
            candidates = ProbeFloors(facility.transform, bounds, 4f);
            var hidePoints = Spread(candidates, 64, centre);

            var mapGo = new GameObject("HideAndSeekMap");
            var map = mapGo.AddComponent<HS_Map>();
            map.hidePoints = hidePoints.ToArray();
            var seekerSpawn = Empty("SeekerSpawn", mapGo.transform, centre);
            var lobby = Empty("LobbySpawn", mapGo.transform, centre);
            map.seekerSpawn = seekerSpawn.transform;
            map.lobbySpawn = lobby.transform;
            map.spawnScatter = 2.5f;
            map.killY = bounds.min.y - 8f;
            map.pingPrefab = ping;
            map.clientPrefab = hud;
            // A striped ring marks where seekers wait.
            NoShadows(Primitive(PrimitiveType.Cylinder, "SeekerPad", world.transform, centre + Vector3.up * 0.03f, new Vector3(7f, 0.03f, 7f), stripes, false));
            EditorUtility.SetDirty(map);
            Debug.Log($"[HideAndSeek] Facility {bounds.size}, {candidates.Count} floor points, {hidePoints.Count} hide points, {crates} crates.");
            Kit.SaveActiveScene(Kit.StartScene(Universe));
        }

        /// <summary>Flat facility floors with headroom for a standing player, on every storey.</summary>
        static List<Vector3> ProbeFloors(Transform facility, Bounds bounds, float step) {
            var points = new List<Vector3>();
            for (float x = bounds.min.x + step * 0.5f; x < bounds.max.x; x += step)
            for (float z = bounds.min.z + step * 0.5f; z < bounds.max.z; z += step) {
                var hits = Physics.RaycastAll(new Vector3(x, bounds.max.y + 5f, z), Vector3.down, bounds.size.y + 10f, ~0, QueryTriggerInteraction.Ignore);
                foreach (var hit in hits) {
                    if (hit.normal.y < 0.8f || !hit.collider.transform.IsChildOf(facility)) continue;
                    Vector3 p = hit.point;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.9f, 0.45f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    // Real floor, not the top of a wall: flat ground all around the point.
                    bool flat = true;
                    foreach (var o in new[] { new Vector3(1.2f, 0f, 0f), new Vector3(-1.2f, 0f, 0f), new Vector3(0f, 0f, 1.2f), new Vector3(0f, 0f, -1.2f) })
                        if (!Physics.Raycast(p + o + Vector3.up * 0.5f, Vector3.down, out var h, 1f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(h.point.y - p.y) > 0.3f) { flat = false; break; }
                    if (flat) points.Add(p);
                }
            }
            return points;
        }

        /// <summary>Farthest-point sampling, so hide points cover the whole facility.</summary>
        static List<Vector3> Spread(List<Vector3> points, int count, Vector3 avoid) {
            var picked = new List<Vector3>();
            var pool = points.Where(p => Vector3.Distance(p, avoid) > 15f).ToList();
            if (pool.Count == 0) pool = points.ToList();
            if (pool.Count == 0) return picked;
            picked.Add(pool.OrderByDescending(p => Vector3.Distance(p, avoid)).First());
            var nearest = pool.Select(p => Vector3.Distance(p, picked[0])).ToArray();
            while (picked.Count < count) {
                int best = 0;
                for (int i = 1; i < pool.Count; i++) if (nearest[i] > nearest[best]) best = i;
                if (nearest[best] < 3f) break;
                picked.Add(pool[best]);
                for (int i = 0; i < pool.Count; i++) nearest[i] = Mathf.Min(nearest[i], Vector3.Distance(pool[i], pool[best]));
            }
            return picked;
        }

        [MenuItem("Ryan/Hide and Seek/Register Scenes")]
        public static void RegisterScenesMenu() => RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));

        // ---------------------------------------------------------------- previews
        [MenuItem("Ryan/Hide and Seek/Render Previews")]
        public static void RenderPreviews() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
            var map = Object.FindAnyObjectByType<HS_Map>();
            var bounds = RendererBounds(GameObject.Find("Facility"));
            string dir = PreviewDir(Universe);
            var dots = Kit.Mat("PreviewDot", new Color(0.3f, 1f, 0.4f), emission: 2f);
            var marks = new GameObject("PreviewMarks");
            foreach (var p in map.hidePoints) NoShadows(Primitive(PrimitiveType.Sphere, "Dot", marks.transform, p + Vector3.up, Vector3.one * 1.5f, dots, false));
            var cam = PreviewCamera();
            float span = Mathf.Max(bounds.size.x, bounds.size.z);
            Shoot(cam, new Vector3(0f, span * 1.05f, -span * 0.25f), Vector3.zero, Path.Combine(dir, "top.png"), 1600, 900);
            Object.DestroyImmediate(marks);
            Shoot(cam, new Vector3(-span * 0.55f, span * 0.35f, -span * 0.55f), Vector3.zero, Path.Combine(dir, "overview.png"), 1600, 900);
            Vector3 c = map.seekerSpawn.position;
            Shoot(cam, c + new Vector3(-10f, 5f, -10f), c + Vector3.up * 2f, Path.Combine(dir, "inside.png"), 1600, 900);
            Vector3 h = map.hidePoints[map.hidePoints.Length / 2];
            Shoot(cam, h + new Vector3(-6f, 3f, -6f), h + Vector3.up, Path.Combine(dir, "hidepoint.png"), 1600, 900);
            Shoot(cam, new Vector3(-span * 0.45f, span * 0.3f, -span * 0.45f), Vector3.zero, Path.Combine(dir, "thumbnail.png"), 640, 360);
            Object.DestroyImmediate(cam.gameObject);
            Kit.PublishThumbnail(Universe, Path.Combine(dir, "thumbnail.png"));
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
        }
    }
}
