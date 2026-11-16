using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RyanAssets.GameFeel.Editor {
    /// <summary>
    /// Builds the <see cref="FxKind"/> prefabs. Every system uses URP's Particles/Unlit shader (which
    /// also renders on the cloud's software GPU), world simulation and hierarchy scaling, so
    /// <see cref="GameFx"/> can scale, tilt and tint (through <c>_BaseColor</c>) any of them.
    /// Colours are near-white where a tint is expected to set them.
    /// </summary>
    internal static class GameFeelParticles {
        public const string PrefabFolder = GameFeelAuthoring.Generated + "/Prefabs";
        public const string MaterialFolder = GameFeelAuthoring.Generated + "/Materials";

        // Additive materials are only for glows layered on top of something else: additive alone vanishes
        // on bright scenes (white obby floors, snow, sky), so every effect's body is alpha blended.
        static Material dotAdd, dotAlpha, smokeAlpha, fireAlpha, ringAlpha, crossAlpha, paperAlpha, coinAlpha, beamAlpha;

        public static Dictionary<FxKind, ParticleSystem> BuildAll() {
            System.IO.Directory.CreateDirectory(PrefabFolder);
            System.IO.Directory.CreateDirectory(MaterialFolder);
            dotAdd = FxMaterial("FxDotAdd", GameFeelTextures.SoftDot, true);
            dotAlpha = FxMaterial("FxDotAlpha", GameFeelTextures.SoftDot, false);
            smokeAlpha = FxMaterial("FxSmokeAlpha", GameFeelTextures.Smoke, false);
            fireAlpha = FxMaterial("FxFireAlpha", GameFeelTextures.Smoke, false);
            ringAlpha = FxMaterial("FxRingAlpha", GameFeelTextures.Ring, false);
            crossAlpha = FxMaterial("FxCrossAlpha", GameFeelTextures.Cross, false);
            paperAlpha = FxMaterial("FxPaperAlpha", GameFeelTextures.Paper, false);
            coinAlpha = FxMaterial("FxCoinAlpha", GameFeelTextures.Coin, false);
            beamAlpha = FxMaterial("FxBeamAlpha", GameFeelTextures.Beam, false);

            return new Dictionary<FxKind, ParticleSystem> {
                [FxKind.DustPuff] = Save(DustPuff()),
                [FxKind.Sparks] = Save(Sparks("FxSparks", null, 18, 1f)),
                [FxKind.Confetti] = Save(Confetti()),
                [FxKind.CoinBurst] = Save(CoinBurst()),
                [FxKind.ExplosionSmall] = Save(Explosion("FxExplosionSmall", 1f, false)),
                [FxKind.ExplosionLarge] = Save(Explosion("FxExplosionLarge", 2.4f, true)),
                [FxKind.Smoke] = Save(Smoke()),
                [FxKind.Heal] = Save(Heal()),
                [FxKind.ShockwaveRing] = Save(Shockwave("FxShockwaveRing", null, 7f, 0.5f)),
                [FxKind.BeaconBeam] = Save(Beacon()),
                [FxKind.Trail] = Save(Trail()),
            };
        }

        // --- Effects -----------------------------------------------------------------

        static ParticleSystem DustPuff() {
            ParticleSystem ps = New("FxDustPuff", null, smokeAlpha, 0.3f, false, 24);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.95f, 0.93f, 0.9f, 0.85f);
            main.gravityModifier = -0.04f;
            Burst(ps, (0f, 10));
            Cone(ps, 80f, 0.3f);
            Drag(ps, 0.6f, 0.25f);
            Spin(ps, -1.2f, 1.2f);
            SizeOver(ps, Curve(0f, 0.55f, 1f, 1.5f));
            FadeOver(ps, Fade(Color.white, 0.08f, 0.35f));
            return ps;
        }

        static ParticleSystem Sparks(string name, Transform parent, int count, float scale) {
            ParticleSystem ps = New(name, parent, dotAlpha, 0.2f, false, count + 8);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f * scale, 14f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f * scale, 0.22f * scale);
            main.startColor = new Color(1f, 0.68f, 0.18f);
            main.gravityModifier = 1.4f;
            Burst(ps, (0f, count));
            Cone(ps, 60f, 0.05f * scale);
            Drag(ps, 0.1f, 0.05f);
            SizeOver(ps, Curve(0f, 1f, 1f, 0.2f));
            FadeOver(ps, Fade(Color.white, 0.01f, 0.5f));
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.045f;
            renderer.lengthScale = 1.2f;
            return ps;
        }

        static readonly Color[] ConfettiColors = {
            new(0.98f, 0.33f, 0.36f), new(1f, 0.79f, 0.24f), new(0.35f, 0.85f, 0.45f),
            new(0.25f, 0.65f, 1f), new(0.72f, 0.45f, 1f), new(1f, 0.55f, 0.2f), new(0.3f, 0.9f, 0.9f),
        };

        static ParticleSystem Confetti() {
            ParticleSystem ps = New("FxConfetti", null, paperAlpha, 0.5f, false, 140);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.4f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var gradient = new Gradient { mode = GradientMode.Fixed };
            var keys = new GradientColorKey[ConfettiColors.Length];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = new GradientColorKey(ConfettiColors[i], (i + 1f) / keys.Length);
            gradient.colorKeys = keys;
            main.startColor = new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
            main.gravityModifier = 0.5f;
            Burst(ps, (0f, 70), (0.25f, 40));
            Cone(ps, 28f, 0.6f);
            Drag(ps, 3.2f, 0.08f);
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-7f, 7f);
            spin.y = new ParticleSystem.MinMaxCurve(-5f, 5f);
            spin.z = new ParticleSystem.MinMaxCurve(-7f, 7f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 1.3f;
            noise.frequency = 0.6f;
            FadeOver(ps, Fade(Color.white, 0.01f, 0.8f));
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            renderer.alignment = ParticleSystemRenderSpace.Local;
            return ps;
        }

        static ParticleSystem CoinBurst() {
            ParticleSystem ps = New("FxCoinBurst", null, coinAlpha, 0.2f, false, 24);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
            main.startColor = new Color(1f, 0.8f, 0.22f);
            main.gravityModifier = 1.5f;
            Burst(ps, (0f, 12));
            Cone(ps, 35f, 0.2f);
            SizeOver(ps, Curve(0f, 0.6f, 0.15f, 1f, 1f, 0.5f));
            FadeOver(ps, Fade(Color.white, 0.01f, 0.7f));

            ParticleSystem glints = New("Glints", ps.transform, dotAdd, 0.2f, false, 24);
            var g = glints.main;
            g.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            g.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            g.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            g.startColor = new Color(1f, 0.95f, 0.7f);
            Burst(glints, (0f, 14));
            Sphere(glints, 0.4f);
            SizeOver(glints, Curve(0f, 0.2f, 0.3f, 1f, 1f, 0f));
            FadeOver(glints, Fade(Color.white, 0.05f, 0.4f));
            Flash(ps.transform, new Color(1f, 0.9f, 0.5f, 0.8f), 1.4f, 0.18f);
            return ps;
        }

        static ParticleSystem Explosion(string name, float scale, bool large) {
            ParticleSystem root = New(name, null, dotAdd, 0.2f, false, 4);
            // The root is the bright core flash.
            var main = root.main;
            main.startLifetime = 0.16f * Mathf.Sqrt(scale);
            main.startSize = 3.8f * scale;
            main.startColor = new Color(1f, 0.92f, 0.75f);
            Burst(root, (0f, 1));
            SizeOver(root, Curve(0f, 0.6f, 0.4f, 1f, 1f, 1.1f));
            FadeOver(root, Fade(Color.white, 0.01f, 0.3f));

            ParticleSystem fire = New("Fire", root.transform, fireAlpha, 0.2f, false, 40);
            var f = fire.main;
            f.startLifetime = new ParticleSystem.MinMaxCurve(0.35f * scale, 0.6f * Mathf.Sqrt(scale));
            f.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * scale, 5f * scale);
            f.startSize = new ParticleSystem.MinMaxCurve(1.3f * scale, 2.3f * scale);
            f.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            f.startColor = Color.white;
            Burst(fire, (0f, large ? 26 : 14));
            Sphere(fire, 0.5f * scale);
            Drag(fire, 0.5f * scale, 0.2f);
            Spin(fire, -1.5f, 1.5f);
            SizeOver(fire, Curve(0f, 0.5f, 0.3f, 1f, 1f, 1.3f));
            var fireColor = new Gradient {
                colorKeys = new[] {
                    new GradientColorKey(new Color(1f, 0.9f, 0.55f), 0f),
                    new GradientColorKey(new Color(1f, 0.72f, 0.25f), 0.25f),
                    new GradientColorKey(new Color(0.95f, 0.32f, 0.08f), 0.6f),
                    new GradientColorKey(new Color(0.35f, 0.08f, 0.03f), 1f),
                },
                alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) },
            };
            FadeOver(fire, fireColor);

            ParticleSystem smoke = New("Smoke", root.transform, smokeAlpha, 0.3f, false, 40);
            var s = smoke.main;
            s.startDelay = 0.06f;
            s.startLifetime = new ParticleSystem.MinMaxCurve(1.2f * Mathf.Sqrt(scale), 2.2f * Mathf.Sqrt(scale));
            s.startSpeed = new ParticleSystem.MinMaxCurve(0.8f * scale, 2.4f * scale);
            s.startSize = new ParticleSystem.MinMaxCurve(1.6f * scale, 2.6f * scale);
            s.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            s.startColor = new Color(0.3f, 0.29f, 0.28f, 0.75f);
            s.gravityModifier = -0.06f;
            Burst(smoke, (0f, large ? 18 : 9));
            Sphere(smoke, 0.6f * scale);
            Drag(smoke, 0.4f * scale, 0.15f);
            Spin(smoke, -0.6f, 0.6f);
            SizeOver(smoke, Curve(0f, 0.6f, 1f, 1.8f));
            FadeOver(smoke, Fade(Color.white, 0.1f, 0.4f));

            Sparks("Sparks", root.transform, large ? 30 : 16, Mathf.Sqrt(scale));
            if (large)
                Shockwave("Ring", root.transform, 5f * scale, 0.55f);
            return root;
        }

        static ParticleSystem Smoke() {
            ParticleSystem ps = New("FxSmoke", null, smokeAlpha, 0.6f, false, 24);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.62f, 0.62f, 0.62f, 0.6f);
            main.gravityModifier = -0.05f;
            Burst(ps, (0f, 3), (0.2f, 3), (0.4f, 3));
            Cone(ps, 15f, 0.4f);
            Spin(ps, -0.5f, 0.5f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.3f;
            SizeOver(ps, Curve(0f, 0.6f, 1f, 2f));
            FadeOver(ps, Fade(Color.white, 0.15f, 0.45f));
            return ps;
        }

        static ParticleSystem Heal() {
            ParticleSystem ps = New("FxHeal", null, crossAlpha, 0.7f, false, 30);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startColor = new Color(0.25f, 0.88f, 0.42f);
            Burst(ps, (0f, 5), (0.2f, 5), (0.45f, 5));
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.9f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            SizeOver(ps, Curve(0f, 0.3f, 0.25f, 1f, 1f, 0.6f));
            FadeOver(ps, Fade(Color.white, 0.1f, 0.6f));
            Flash(ps.transform, new Color(0.45f, 1f, 0.55f, 0.5f), 3.2f, 0.5f);
            return ps;
        }

        const float RingLift = 0.25f;

        static ParticleSystem Shockwave(string name, Transform parent, float size, float life) {
            ParticleSystem ps = New(name, parent, ringAlpha, 0.2f, false, 2);
            var main = ps.main;
            main.startLifetime = life;
            main.startSize = size;
            main.startColor = new Color(0.55f, 0.78f, 1f, 0.95f);
            main.startRotation3D = true;
            main.startRotationX = Mathf.PI * 0.5f;
            Burst(ps, (0f, 1));
            // Lifted off the ground so a flat ring on a floor does not z-fight with it.
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.0001f;
            shape.position = new Vector3(0f, RingLift, 0f);
            SizeOver(ps, Curve(0f, 0.15f, 0.4f, 0.8f, 1f, 1f));
            FadeOver(ps, Fade(Color.white, 0.01f, 0.25f));
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            renderer.alignment = ParticleSystemRenderSpace.Local;
            return ps;
        }

        const float BeamHeight = 26f;

        static ParticleSystem Beacon() {
            ParticleSystem ps = New("FxBeaconBeam", null, beamAlpha, 1f, true, 12);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // the pillar moves with the handle
            main.startLifetime = 1.6f;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(1.6f, 2.2f);
            main.startSizeY = BeamHeight;
            main.startSizeZ = 1f;
            main.startColor = new Color(1f, 1f, 1f, 0.55f);
            var emission = ps.emission;
            emission.rateOverTime = 3f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = Vector3.zero;
            shape.position = new Vector3(0f, BeamHeight * 0.5f, 0f);
            FadeOver(ps, Fade(Color.white, 0.3f, 0.6f));
            var beamRenderer = ps.GetComponent<ParticleSystemRenderer>();
            beamRenderer.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            // The default cap (half the screen) would shrink the tall pillar to a stub when seen up close.
            beamRenderer.maxParticleSize = 100f;
            // Prewarm so the beam is already full when it appears.
            main.prewarm = true;

            ParticleSystem motes = New("Motes", ps.transform, dotAlpha, 1f, true, 60);
            var m = motes.main;
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            m.startColor = new Color(1f, 1f, 1f, 0.9f);
            m.prewarm = true;
            var me = motes.emission;
            me.rateOverTime = 14f;
            var mshape = motes.shape;
            mshape.enabled = true;
            mshape.shapeType = ParticleSystemShapeType.Circle;
            mshape.radius = 0.9f;
            mshape.rotation = new Vector3(-90f, 0f, 0f);
            FadeOver(motes, Fade(Color.white, 0.2f, 0.6f));

            ParticleSystem glow = New("BaseGlow", ps.transform, ringAlpha, 1f, true, 4);
            var gm = glow.main;
            gm.simulationSpace = ParticleSystemSimulationSpace.Local;
            gm.startLifetime = 1.2f;
            gm.startSize = 3.5f;
            gm.startColor = new Color(1f, 1f, 1f, 0.6f);
            gm.startRotation3D = true;
            gm.startRotationX = Mathf.PI * 0.5f;
            gm.prewarm = true;
            var ge = glow.emission;
            ge.rateOverTime = 1.5f;
            var gs = glow.shape;
            gs.enabled = true;
            gs.shapeType = ParticleSystemShapeType.Sphere;
            gs.radius = 0.0001f;
            gs.position = new Vector3(0f, RingLift, 0f);
            SizeOver(glow, Curve(0f, 0.5f, 1f, 1f));
            FadeOver(glow, Fade(Color.white, 0.2f, 0.4f));
            var gr = glow.GetComponent<ParticleSystemRenderer>();
            gr.renderMode = ParticleSystemRenderMode.Mesh;
            gr.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            gr.alignment = ParticleSystemRenderSpace.Local;
            return ps;
        }

        static ParticleSystem Trail() {
            ParticleSystem ps = New("FxTrail", null, dotAlpha, 1f, true, 200);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.6f);
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            var emission = ps.emission;
            emission.rateOverTime = 12f;
            emission.rateOverDistance = 8f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;
            SizeOver(ps, Curve(0f, 1f, 1f, 0f));
            FadeOver(ps, Fade(Color.white, 0.02f, 0.3f));
            return ps;
        }

        // --- Building blocks ---------------------------------------------------------

        /// <summary>A world-space, hierarchy-scaled system with no emission; callers add bursts or a rate.</summary>
        static ParticleSystem New(string name, Transform parent, Material material, float duration, bool loop, int maxParticles) {
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = duration;
            main.loop = loop;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = maxParticles;
            main.startSpeed = 0f;
            main.stopAction = ParticleSystemStopAction.None;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return ps;
        }

        static void Burst(ParticleSystem ps, params (float time, int count)[] bursts) {
            var list = new ParticleSystem.Burst[bursts.Length];
            for (int i = 0; i < bursts.Length; i++)
                list[i] = new ParticleSystem.Burst(bursts[i].time, (short)bursts[i].count);
            var emission = ps.emission;
            emission.SetBursts(list);
        }

        /// <summary>An upward cone (the effect's local +Y).</summary>
        static void Cone(ParticleSystem ps, float angle, float radius) {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }

        static void Sphere(ParticleSystem ps, float radius) {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        static void Drag(ParticleSystem ps, float limit, float dampen) {
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = limit;
            drag.dampen = dampen;
        }

        static void Spin(ParticleSystem ps, float min, float max) {
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(min, max);
        }

        /// <summary>One bright soft flash at the effect origin.</summary>
        static void Flash(Transform parent, Color color, float size, float life) {
            ParticleSystem ps = New("Flash", parent, dotAdd, 0.2f, false, 2);
            var main = ps.main;
            main.startLifetime = life;
            main.startSize = size;
            main.startColor = color;
            Burst(ps, (0f, 1));
            SizeOver(ps, Curve(0f, 0.5f, 0.3f, 1f, 1f, 1.2f));
            FadeOver(ps, Fade(Color.white, 0.02f, 0.3f));
        }

        static void SizeOver(ParticleSystem ps, AnimationCurve curve) {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        static void FadeOver(ParticleSystem ps, Gradient gradient) {
            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = gradient;
        }

        static Gradient Fade(Color color, float fadeInEnd, float fadeOutStart) => new() {
            colorKeys = new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeInEnd), new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, 1f) },
        };

        static AnimationCurve Curve(params float[] timeValue) {
            var keys = new Keyframe[timeValue.Length / 2];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = new Keyframe(timeValue[i * 2], timeValue[i * 2 + 1]);
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            return curve;
        }

        static ParticleSystem Save(ParticleSystem ps) {
            string path = PrefabFolder + "/" + ps.name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(ps.gameObject, path);
            Object.DestroyImmediate(ps.gameObject);
            return prefab.GetComponent<ParticleSystem>();
        }

        /// <summary>A URP Particles/Unlit material: soft alpha blend, or additive for glows.</summary>
        static Material FxMaterial(string name, Texture2D texture, bool additive) {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? throw new System.InvalidOperationException("URP Particles/Unlit shader not found");
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            } else if (material.shader != shader) {
                material.shader = shader;
            }
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
