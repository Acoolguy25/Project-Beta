using System.Collections.Generic;
using System.IO;
using System.Linq;
using RyanAssets.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Universes.UniverseData.hide_and_seek.Editor.Manor;
using static RyanAssets.Authoring.UniverseAuthoringKit;

namespace Universes.UniverseData.hide_and_seek.Editor {
    /// <summary>
    /// Builds Hide and Seek's start scene: "Pumpkin Hollow Manor" (<see cref="HS_ManorPlan"/>, built by
    /// <see cref="HS_ManorBuilder"/>), hide points probed from its floors, the ping marker and the HUD
    /// (<see cref="HS_HudAuthoring"/>). Safe to re-run. Build the server runner from the Linux Server
    /// profile with <c>-executeMethod Universes.UniverseData.hide_and_seek.Editor.HS_Authoring.BuildServerAssets</c>.
    /// </summary>
    public static class HS_Authoring {
        public const string Universe = "hide_and_seek";
        public const string Root = "Assets/Universes/UniverseData/hide_and_seek";
        static readonly UniverseAuthoringKit Kit = new(Root);
        const string ServerPrefab = Root + "/Server/HS_ServerRunner.prefab";
        const string ManorMaterials = Root + "/Materials/Manor";
        const string ManorPostProfile = Root + "/Materials/ManorPost.asset";

        /// <summary>Height of the player character in world units (RobotPlayer's body).</summary>
        public const float CharacterHeight = 5.6f;
        /// <summary>Height of a person in the plan (see <see cref="HS_ManorPlan"/>).</summary>
        const float PlanPersonHeight = 1.8f;
        /// <summary>World units per plan metre, so a plan person is as tall as the game's character.</summary>
        public const float MapScale = CharacterHeight / PlanPersonHeight;
        const int HidePointCount = 72;

        [MenuItem("Ryan/Hide and Seek/Build Client Assets")]
        public static void BuildClientAssets() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            Kit.EnsureFolders("Prefabs", "Materials", "Materials/Manor", "Server");
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

        // ---------------------------------------------------------------- scene
        static void BuildStartScene() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var hud = HS_HudAuthoring.Build();
            var ping = BuildPing();
            // Autumn dusk: soft warm sun, dim ambient, fog that hides the edge of the ground; the lamps do the rest (HS_ManorLighting).
            Light sun = Kit.NewLitScene(HS_ManorLighting.SkyTint, HS_ManorLighting.FogColor, HS_ManorLighting.FogStart, HS_ManorLighting.FogEnd,
                HS_ManorLighting.SunEuler, HS_ManorLighting.SunIntensity);
            sun.color = HS_ManorLighting.SunColor;
            sun.shadows = LightShadows.Soft;
            RenderSettings.skybox.SetFloat("_Exposure", HS_ManorLighting.SkyExposure);
            RenderSettings.skybox.SetColor("_GroundColor", HS_ManorLighting.FogColor);
            EditorUtility.SetDirty(RenderSettings.skybox);
            RenderSettings.ambientSkyColor = HS_ManorLighting.AmbientSky;
            RenderSettings.ambientEquatorColor = HS_ManorLighting.AmbientEquator;
            RenderSettings.ambientGroundColor = HS_ManorLighting.AmbientGround;
            HS_ManorPost.AddVolume(HS_ManorPost.BuildProfile(ManorPostProfile));

            var builder = new HS_ManorBuilder(new HS_ManorMaterials(ManorMaterials), MapScale, "PumpkinHollowManor");
            Transform manor = builder.Build();
            Physics.SyncTransforms();

            var mapGo = new GameObject("HideAndSeekMap");
            var map = mapGo.AddComponent<HS_Map>();
            map.mapName = HS_ManorPlan.MapName;
            map.areas = builder.Areas();
            Vector3 seeker = HS_ManorPlan.SeekerSpawn * MapScale, lobby = HS_ManorPlan.LobbySpawn * MapScale;
            var candidates = ProbeFloors(manor, PlotBounds(), MapScale * 1.4f);
            map.hidePoints = Spread(candidates, HidePointCount, seeker, minSpacing: MapScale * 1.6f).ToArray();
            map.seekerSpawn = Empty("SeekerSpawn", mapGo.transform, seeker).transform;
            map.lobbySpawn = Empty("LobbySpawn", mapGo.transform, lobby).transform;
            map.spawnScatter = MapScale * 0.8f;
            map.killY = -20f;
            map.pingPrefab = ping;
            map.clientPrefab = hud;
            map.flickerLights = builder.FlickerLights.ToArray();
            map.flickerGlows = builder.FlickerGlows.ToArray();
            (int reach, string busiest) = builder.CheckLightReach();
            if (reach > HS_ManorLighting.MaxLightsPerObject)
                throw new System.InvalidOperationException($"[HideAndSeek] {reach} lights reach {busiest}; URP draws at most {HS_ManorLighting.MaxLightsPerObject} per object. Shorten ranges in HS_ManorLighting or split that surface.");
            map.clockPosition = HS_ManorPlan.ClockAt * MapScale;
            EditorUtility.SetDirty(map);
            Debug.Log($"[HideAndSeek] {HS_ManorPlan.MapName}: scale {MapScale:0.##}, {candidates.Count} floor points, {map.hidePoints.Length} hide points, {map.areas.Length} areas, {builder.LightCount} lights ({map.flickerLights.Length} flicker), at most {reach} reach one surface ({busiest}).");
            Kit.SaveActiveScene(Kit.StartScene(Universe));
        }

        static Bounds PlotBounds() {
            Rect plot = HS_ManorPlan.Plot;
            var b = new Bounds();
            b.SetMinMax(new Vector3(plot.xMin, -1f, plot.yMin) * MapScale, new Vector3(plot.xMax, HS_ManorPlan.StoreyHeight * 2f, plot.yMax) * MapScale);
            return b;
        }

        /// <summary>Floor points (every storey) with headroom and flat ground all around for a standing character.</summary>
        static List<Vector3> ProbeFloors(Transform map, Bounds bounds, float step) {
            var points = new List<Vector3>();
            float radius = CharacterHeight * 0.18f, reach = CharacterHeight * 0.22f;
            for (float x = bounds.min.x + step * 0.5f; x < bounds.max.x; x += step)
            for (float z = bounds.min.z + step * 0.5f; z < bounds.max.z; z += step) {
                var hits = Physics.RaycastAll(new Vector3(x, bounds.max.y + 5f, z), Vector3.down, bounds.size.y + 10f, ~0, QueryTriggerInteraction.Ignore);
                foreach (var hit in hits) {
                    if (hit.normal.y < 0.9f || !hit.collider.transform.IsChildOf(map)) continue;
                    Vector3 p = hit.point;
                    if (Physics.CheckCapsule(p + Vector3.up * (radius + 0.1f), p + Vector3.up * (CharacterHeight - radius), radius, ~0, QueryTriggerInteraction.Ignore)) continue;
                    bool flat = true;
                    foreach (var o in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                        if (!Physics.Raycast(p + o * reach + Vector3.up * 0.5f, Vector3.down, out var h, 1.2f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(h.point.y - p.y) > 0.3f) { flat = false; break; }
                    if (flat) points.Add(p);
                }
            }
            return points;
        }

        /// <summary>Farthest-point sampling, so hide points cover the whole map (indoors and out).</summary>
        static List<Vector3> Spread(List<Vector3> points, int count, Vector3 avoid, float minSpacing) {
            var picked = new List<Vector3>();
            var pool = points.Where(p => Vector3.Distance(p, avoid) > MapScale * 5f).ToList();
            if (pool.Count == 0) pool = points.ToList();
            if (pool.Count == 0) return picked;
            picked.Add(pool.OrderByDescending(p => Vector3.Distance(p, avoid)).First());
            var nearest = pool.Select(p => Vector3.Distance(p, picked[0])).ToArray();
            while (picked.Count < count) {
                int best = 0;
                for (int i = 1; i < pool.Count; i++) if (nearest[i] > nearest[best]) best = i;
                if (nearest[best] < minSpacing) break;
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
            string dir = PreviewDir(Universe);
            var cam = PreviewCamera();
            float s = MapScale;
            Vector3 house = new Vector3(HS_ManorPlan.House.center.x, 3f, HS_ManorPlan.House.center.y) * s;
            Shoot(cam, house + new Vector3(-30f, 22f, -40f) * s, house, Path.Combine(dir, "overview.png"), 1600, 900);
            Shoot(cam, new Vector3(0f, 60f, -10f) * s, new Vector3(0f, 0f, 0f), Path.Combine(dir, "top.png"), 1600, 900);
            Shoot(cam, new Vector3(-9f, 2.2f, -1f) * s, new Vector3(-7f, 0.8f, -5f) * s, Path.Combine(dir, "living-room.png"), 1600, 900);
            Shoot(cam, new Vector3(10f, 5.4f, 3.8f) * s, new Vector3(6f, 3.6f, 6.5f) * s, Path.Combine(dir, "attic.png"), 1600, 900);
            Shoot(cam, new Vector3(14f, 4f, 10f) * s, new Vector3(24f, 1f, 22f) * s, Path.Combine(dir, "graveyard.png"), 1600, 900);
            Shoot(cam, new Vector3(1.5f, 2.2f, -7.2f) * s, new Vector3(-2f, 1.4f, 3.5f) * s, Path.Combine(dir, "foyer.png"), 1600, 900);
            Shoot(cam, new Vector3(7.2f, 2.2f, 3.9f) * s, new Vector3(10f, 0.8f, 7f) * s, Path.Combine(dir, "kitchen.png"), 1600, 900);
            Shoot(cam, new Vector3(-3.4f, 5.6f, 0.4f) * s, new Vector3(-9f, 3.6f, -5f) * s, Path.Combine(dir, "master-bedroom.png"), 1600, 900);
            Shoot(cam, new Vector3(-8f, 6f, -32f) * s, new Vector3(-20f, 1f, -16f) * s, Path.Combine(dir, "garden.png"), 1600, 900);
            Shoot(cam, house + new Vector3(-26f, 16f, -34f) * s, house, Path.Combine(dir, "thumbnail.png"), 640, 360);
            // Floor plans: an orthographic camera just under each storey's ceiling, looking straight down.
            cam.orthographic = true;
            cam.orthographicSize = 9f * s;
            for (int level = 0; level < 2; level++) {
                float eye = level * HS_ManorPlan.StoreyHeight + HS_ManorPlan.StoreyHeight - HS_ManorPlan.SlabThickness - 0.05f;
                Shoot(cam, new Vector3(0f, eye, 0f) * s, new Vector3(0f, 0f, 0.01f) * s, Path.Combine(dir, $"plan-level{level}.png"), 1600, 900);
            }
            cam.orthographic = false;
            Object.DestroyImmediate(cam.gameObject);
            Kit.PublishThumbnail(Universe, Path.Combine(dir, "thumbnail.png"));
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
            Debug.Log($"[HideAndSeek] Previews in {dir} ({map.hidePoints.Length} hide points).");
        }
    }
}
