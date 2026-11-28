#if !UNITY_SERVER
using System.Collections.Generic;
using RyanAssets.Authoring;
using RyanAssets.GameFeel;
using RyanAssets.UI.Editor;
using UnityEditor;
using UnityEngine;
using Universes.UniverseData.capture_the_flag.Client;

namespace Universes.UniverseData.capture_the_flag.Editor {
    /// <summary>
    /// The four guns' looks (<see cref="CTF_GunLooks"/>): a held model per gun built from the shared gun
    /// pack in com.ryan.gameframework (PolyOne Studio: M16A1, HK MP5, Barrett M82A1), posed in the pistol
    /// tool's weapon-root space so it sits where the pistol sat, plus each gun's shot pitch, tracer and
    /// flash. The pack has no shotgun: the Scattergun is the MP5 with an authored wide muzzle (the missing
    /// art is a pump or double-barrel shotgun model). The Sidearm keeps the pistol itself.
    /// </summary>
    internal static class CTF_GunAuthoring {
        const string Pack = "Packages/com.ryan.gameframework/Runtime/Tools/Guns/PolyOne Studio/Prefabs/";
        /// <summary>
        /// Top of the pistol's grip in its body space (the tool's weapon root, Tools/Implemented/FN_Five_Seven:
        /// barrel +z, up +y, muzzle at (0, 0.05, 0.10), magazine at (0, -0.17, -0.03)): where a long gun's grip goes.
        /// </summary>
        static readonly Vector3 PistolGripTop = new(0f, -0.02f, -0.03f);
        /// <summary>How far behind its magazine a rifle's grip is, as a share of the gun's length.</summary>
        const float GripBehindMagazine = 0.09f;

        sealed class Spec {
            public CTF_Weapon weapon;
            public string prefab;
            /// <summary>Length of the held model in the pistol body's space (the pistol is about 0.21).</summary>
            public float length;
            /// <summary>Muzzle end: null = the thinner end of the barrel axis; true = +axis, false = -axis.</summary>
            public bool? muzzleAtMax = null;
            public bool wideMuzzle;
        }

        static readonly Spec[] Specs = {
            new() { weapon = CTF_Weapon.BurstRifle, prefab = Pack + "SM_M16A1.prefab", length = 0.78f },
            new() { weapon = CTF_Weapon.Scattergun, prefab = Pack + "SM_HK_MP5.prefab", length = 0.6f, wideMuzzle = true },
            new() { weapon = CTF_Weapon.Marksman, prefab = Pack + "SM_Barrett_M82A1.prefab", length = 0.98f },
        };

        public static CTF_GunLooks AddTo(GameObject hud) {
            var looks = hud.AddComponent<CTF_GunLooks>();
            CTF_Authoring.Kit.EnsureFolders("Prefabs/Guns");
            var rows = new List<CTF_GunLook>();
            foreach (var definition in CTF_Loadouts.Weapons) {
                var look = DefaultLook(definition.weapon);
                look.icon = HudKit.Icon(definition.icon);
                foreach (var spec in Specs) {
                    if (spec.weapon != definition.weapon) continue;
                    look.model = BuildModel(spec, out look.muzzle);
                    if (look.model == null) Debug.LogWarning($"[FlagWars] gun model for {definition.title} not built ({spec.prefab} missing): it keeps the pistol.");
                }
                rows.Add(look);
            }
            looks.looks = rows.ToArray();
            return looks;
        }

        /// <summary>Each gun's sound, tracer, flash and first-person pose.</summary>
        static CTF_GunLook DefaultLook(CTF_Weapon weapon) => weapon switch {
            CTF_Weapon.BurstRifle => new CTF_GunLook {
                weapon = weapon, shotPitch = 1.32f, shotVolume = 0.8f,
                tracerColor = new Color(0.45f, 0.9f, 1f, 0.95f), tracerWidth = 0.025f, tracerSeconds = 0.07f,
                flashTint = new Color(0.6f, 0.9f, 1f), flashScale = 0.45f,
                firstPersonOffset = new Vector3(0.26f, -0.27f, 0.42f), firstPersonScale = 0.62f,
            },
            CTF_Weapon.Scattergun => new CTF_GunLook {
                weapon = weapon, shotPitch = 0.7f, shotVolume = 1.25f, ownShotLayer = true, ownShotCue = SfxCue.Hit, ownShotCueVolume = 0.35f,
                tracerColor = new Color(1f, 0.6f, 0.25f, 0.85f), tracerWidth = 0.02f, tracerSeconds = 0.06f,
                flashTint = new Color(1f, 0.6f, 0.25f), flashScale = 1.1f, flashSmoke = true,
                firstPersonOffset = new Vector3(0.27f, -0.27f, 0.45f), firstPersonScale = 0.62f,
            },
            CTF_Weapon.Marksman => new CTF_GunLook {
                weapon = weapon, shotPitch = 0.58f, shotVolume = 1.35f, ownShotLayer = true, ownShotCue = SfxCue.Explosion, ownShotCueVolume = 0.18f,
                tracerColor = new Color(1f, 0.3f, 0.95f, 1f), tracerWidth = 0.06f, tracerSeconds = 0.22f,
                flashTint = new Color(1f, 0.5f, 0.95f), flashScale = 0.9f, flashSmoke = true,
                firstPersonOffset = new Vector3(0.25f, -0.26f, 0.4f), firstPersonScale = 0.55f,
            },
            _ => new CTF_GunLook {
                weapon = weapon, shotPitch = 1f, shotVolume = 1f,
                tracerColor = new Color(1f, 0.88f, 0.5f, 0.9f), tracerWidth = 0.03f, tracerSeconds = 0.08f,
                flashTint = new Color(1f, 0.82f, 0.45f), flashScale = 0.6f,
            },
        };

        /// <summary>
        /// Saves <c>Prefabs/Guns/CTF_Gun_&lt;weapon&gt;.prefab</c>: the pack model turned so its barrel runs
        /// down the pistol's (+z) with its top up, scaled to <see cref="Spec.length"/>, its grip on the
        /// pistol's grip. <paramref name="muzzle"/> is its barrel tip in the same space.
        /// </summary>
        static GameObject BuildModel(Spec spec, out Vector3 muzzle) {
            muzzle = default;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.prefab);
            if (source == null) return null;
            var points = new List<Vector3>();
            Bounds magazine = default;
            bool hasMagazine = false;
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true)) {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                Matrix4x4 toRoot = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Bounds b = Transformed(mesh.bounds, toRoot);
                if (filter.name.Contains("Magazine")) {
                    magazine = b;
                    hasMagazine = true;
                }
                if (filter.name.Contains("Bullet")) continue;
                var verts = mesh.vertices;
                if (verts == null || verts.Length == 0) {
                    // Not readable here: the box corners still give the length and the axis.
                    Corners(b, points);
                    continue;
                }
                foreach (var v in verts) points.Add(toRoot.MultiplyPoint3x4(v));
            }
            if (points.Count == 0) return null;
            var all = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) all.Encapsulate(p);

            // The barrel runs along the longer of x and z (y is up: the magazines hang below).
            bool alongX = all.size.x >= all.size.z;
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            float min = Vector3.Dot(all.min, axis), max = Vector3.Dot(all.max, axis), length = max - min;
            bool atMax = spec.muzzleAtMax ?? ThinnerAtMax(points, axis, min, max);
            Vector3 forward = atMax ? axis : -axis;
            float tipAlong = atMax ? max : min;
            float tipHeight = SliceHeight(points, axis, atMax ? max - length * 0.04f : min, atMax ? max : min + length * 0.04f);
            Vector3 side = alongX ? Vector3.forward : Vector3.right;
            float sideCentre = Vector3.Dot(all.center, side);
            Vector3 tip = axis * tipAlong + Vector3.up * tipHeight + side * sideCentre;
            // The grip: just behind the magazine (toward the stock), at the magazine's top.
            float magAlong = hasMagazine ? Vector3.Dot(magazine.center, axis) : (min + max) * 0.5f;
            float magTop = hasMagazine ? magazine.max.y : all.center.y;
            Vector3 grip = axis * magAlong - forward * (GripBehindMagazine * length) + Vector3.up * magTop + side * sideCentre;

            float scale = spec.length / Mathf.Max(0.001f, length);
            Quaternion turn = Quaternion.LookRotation(Vector3.forward, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(forward, Vector3.up));
            Vector3 offset = PistolGripTop - turn * (grip * scale);
            muzzle = offset + turn * (tip * scale) + Vector3.forward * 0.01f;

            var root = new GameObject($"CTF_Gun_{spec.weapon}");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            model.transform.localPosition = offset;
            model.transform.localRotation = turn;
            model.transform.localScale = Vector3.one * scale;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            // The magazine's loose round is a separate part: hidden, as the pistol's is behind the slide.
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) if (r.name.Contains("Bullet")) r.enabled = false;
            if (spec.wideMuzzle) WideMuzzle(root.transform, muzzle);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return CTF_Authoring.Kit.Save(root, "Guns");
        }

        /// <summary>A short flared barrel and a pump grip on the MP5, so it reads as a scattergun.</summary>
        static void WideMuzzle(Transform root, Vector3 muzzle) {
            var metal = CTF_Authoring.Kit.Mat("GunMetal", new Color(0.16f, 0.17f, 0.19f), smoothness: 0.55f, metallic: 0.7f);
            var wood = CTF_Authoring.Kit.Mat("GunWood", new Color(0.45f, 0.27f, 0.13f), smoothness: 0.3f);
            // A cylinder's axis is its y: turned onto the barrel (+z). Scale y is half the length.
            var flare = UniverseAuthoringKit.Primitive(PrimitiveType.Cylinder, "WideBarrel", root, muzzle - Vector3.forward * 0.04f, new Vector3(0.055f, 0.045f, 0.055f), metal, false);
            flare.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var pump = UniverseAuthoringKit.Primitive(PrimitiveType.Cylinder, "Pump", root, muzzle - Vector3.forward * 0.2f + Vector3.down * 0.035f, new Vector3(0.05f, 0.06f, 0.05f), wood, false);
            pump.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>Which end of the barrel axis is thinner (the muzzle; the other end is the stock).</summary>
        static bool ThinnerAtMax(List<Vector3> points, Vector3 axis, float min, float max) {
            float slice = (max - min) * 0.12f;
            float atMin = SliceSpan(points, axis, min, min + slice), atMax = SliceSpan(points, axis, max - slice, max);
            return atMax <= atMin;
        }

        /// <summary>Height span of the points whose position along <paramref name="axis"/> is within [from, to].</summary>
        static float SliceSpan(List<Vector3> points, Vector3 axis, float from, float to) {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in points) {
                float a = Vector3.Dot(p, axis);
                if (a < from || a > to) continue;
                lo = Mathf.Min(lo, p.y);
                hi = Mathf.Max(hi, p.y);
            }
            return hi >= lo ? hi - lo : float.MaxValue;
        }

        /// <summary>Middle height of the points in the slice (the barrel's height at its tip).</summary>
        static float SliceHeight(List<Vector3> points, Vector3 axis, float from, float to) {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in points) {
                float a = Vector3.Dot(p, axis);
                if (a < from || a > to) continue;
                lo = Mathf.Min(lo, p.y);
                hi = Mathf.Max(hi, p.y);
            }
            return hi >= lo ? (lo + hi) * 0.5f : 0f;
        }

        static Bounds Transformed(Bounds b, Matrix4x4 m) {
            var corners = new List<Vector3>(8);
            Corners(b, corners);
            var result = new Bounds(m.MultiplyPoint3x4(corners[0]), Vector3.zero);
            foreach (var c in corners) result.Encapsulate(m.MultiplyPoint3x4(c));
            return result;
        }

        static void Corners(Bounds b, List<Vector3> into) {
            Vector3 lo = b.min, hi = b.max;
            for (int i = 0; i < 8; i++)
                into.Add(new Vector3((i & 1) == 0 ? lo.x : hi.x, (i & 2) == 0 ? lo.y : hi.y, (i & 4) == 0 ? lo.z : hi.z));
        }
    }
}
#endif
