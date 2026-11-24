#if !UNITY_SERVER
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Universes.UniverseData.obby.Client;

namespace Universes.UniverseData.obby.Editor {
    public static partial class OB_EnvironmentAuthoring {
        /// <summary>The box around the camera that weather fills (m).</summary>
        static readonly Vector3 WeatherVolume = new(70f, 40f, 70f);
        /// <summary>Weather fades out this close to the camera (m), so flakes never cover the screen.</summary>
        const float WeatherFadeNear = 1.2f, WeatherFadeFar = 4f;
        /// <summary>Largest a weather particle may grow on screen (fraction of the view).</summary>
        const float WeatherMaxScreenSize = 0.04f;
        const int TextureSize = 64;

        delegate Color TexturePixel(float u, float v);

        sealed class FxMaterials {
            public Material glow, soft, leaf, smoke;
        }

        static FxMaterials fx;

        static FxMaterials Effects() {
            if (fx != null && fx.glow != null) return fx;
            var softTexture = EnvTexture("EnvSoft", (u, v) => {
                float r = Mathf.Clamp01(new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f);
                return new Color(1f, 1f, 1f, Mathf.SmoothStep(1f, 0f, r));
            });
            var glowTexture = EnvTexture("EnvGlow", (u, v) => {
                float r = Mathf.Clamp01(new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f);
                return new Color(1f, 1f, 1f, Mathf.Pow(1f - r, 2.4f));
            });
            // A leaf: an almond with a midrib, white so the particle colour tints it.
            var leafTexture = EnvTexture("EnvLeaf", (u, v) => {
                float x = (u - 0.5f) * 2f, y = (v - 0.5f) * 2f;
                float width = 0.55f * (1f - y * y);
                float inside = Mathf.Clamp01((width - Mathf.Abs(x)) * 12f);
                float rib = Mathf.Abs(x) < 0.04f ? 0.75f : 1f;
                return new Color(rib, rib, rib, inside);
            });
            // Smoke: a lumpy soft blob.
            var smokeTexture = EnvTexture("EnvSmoke", (u, v) => {
                Vector2 d = new(u - 0.5f, v - 0.5f);
                float angle = Mathf.Atan2(d.y, d.x);
                float edge = 0.42f + 0.05f * Mathf.Sin(angle * 5f) + 0.03f * Mathf.Sin(angle * 11f + 1.3f);
                float a = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01((d.magnitude - edge * 0.4f) / (edge * 0.6f)));
                float shade = 0.8f + 0.2f * (0.5f - d.y);
                return new Color(shade, shade, shade, a);
            });
            fx = new FxMaterials {
                glow = ParticleMaterial("FxEnvGlow", glowTexture, true),
                soft = ParticleMaterial("FxEnvSoft", softTexture, false),
                leaf = ParticleMaterial("FxEnvLeaf", leafTexture, false),
                smoke = ParticleMaterial("FxEnvSmoke", smokeTexture, false)
            };
            return fx;
        }

        static Texture2D EnvTexture(string name, TexturePixel pixel) {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            for (int y = 0; y < TextureSize; y++)
                for (int x = 0; x < TextureSize; x++)
                    texture.SetPixel(x, y, pixel((x + 0.5f) / TextureSize, (y + 0.5f) / TextureSize));
            texture.Apply();
            string path = $"{TextureFolder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>A URP particle material, alpha blended or additive, fading out right in front of the camera.</summary>
        static Material ParticleMaterial(string name, Texture2D texture, bool additive) =>
            SimpleMat($"{MaterialFolder}/{name}.mat", "Universal Render Pipeline/Particles/Unlit", m => {
                m.SetTexture("_BaseMap", texture);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", additive ? 2f : 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.SetFloat("_CameraFadingEnabled", 1f);
                m.SetFloat("_CameraNearFadeDistance", WeatherFadeNear);
                m.SetFloat("_CameraFarFadeDistance", WeatherFadeFar);
                m.SetVector("_CameraFadeParams", new Vector4(WeatherFadeNear, 1f / (WeatherFadeFar - WeatherFadeNear), 0f, 0f));
                m.EnableKeyword("_FADING_ON");
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            });

        // ---------------------------------------------------------------- weather around the camera
        static OB_AmbientWeather BuildWeather(Transform parent) {
            var effects = Effects();
            var weather = new GameObject("Weather").AddComponent<OB_AmbientWeather>();
            weather.transform.SetParent(parent, false);
            weather.tierSystems = new ParticleSystem[OB_Tiers.Count];
            for (int t = 0; t < OB_Tiers.Count; t++) {
                var tier = (OB_Tier)t;
                OB_WeatherStyle style = OB_Themes.Get(tier).world.weather;
                weather.tierSystems[t] = WeatherSystem(weather.transform, $"{tier} {style.kind}", style, effects);
            }
            return weather;
        }

        static ParticleSystem WeatherSystem(Transform parent, string name, in OB_WeatherStyle style, FxMaterials effects) {
            var look = style.kind switch {
                OB_WeatherKind.Pollen => new WeatherLook(effects.glow, lifetime: 9f, size: new Vector2(0.12f, 0.3f), velocity: new Vector3(0.4f, 0.25f, 0.2f), noise: 0.6f),
                OB_WeatherKind.Leaves => new WeatherLook(effects.leaf, lifetime: 10f, size: new Vector2(0.35f, 0.6f), velocity: new Vector3(1.6f, -0.9f, 0.6f), noise: 1.2f, spin: 160f),
                OB_WeatherKind.Sand => new WeatherLook(effects.soft, lifetime: 5f, size: new Vector2(0.06f, 0.14f), velocity: new Vector3(11f, -0.3f, 2f), noise: 0.8f, stretch: 0.25f, alpha: 0.55f),
                OB_WeatherKind.Snow => new WeatherLook(effects.soft, lifetime: 10f, size: new Vector2(0.12f, 0.32f), velocity: new Vector3(0.8f, -2.2f, 0.3f), noise: 0.7f),
                OB_WeatherKind.Embers => new WeatherLook(effects.glow, lifetime: 6f, size: new Vector2(0.1f, 0.28f), velocity: new Vector3(0.6f, 2.6f, 0.2f), noise: 1.4f),
                _ => new WeatherLook(effects.glow, lifetime: 8f, size: new Vector2(0.15f, 0.45f), velocity: new Vector3(0f, 0.4f, 0f), noise: 0.9f, pulse: true)
            };
            int capacity = Mathf.CeilToInt(style.rate * look.lifetime * 1.3f) + 16;
            var ps = NewEffect(name, parent, look.material, ParticleSystemSimulationSpace.World, capacity);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(look.lifetime * 0.7f, look.lifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(look.size.x, look.size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(style.a.WithAlpha(look.alpha), style.b.WithAlpha(look.alpha));
            if (look.spin > 0f) {
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var rotation = ps.rotationOverLifetime;
                rotation.enabled = true;
                rotation.z = new ParticleSystem.MinMaxCurve(-look.spin * Mathf.Deg2Rad, look.spin * Mathf.Deg2Rad);
            }
            var emission = ps.emission;
            emission.rateOverTime = style.rate;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = WeatherVolume;
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(look.velocity.x * 0.7f, look.velocity.x * 1.3f);
            velocity.y = new ParticleSystem.MinMaxCurve(look.velocity.y * 0.7f, look.velocity.y * 1.3f);
            velocity.z = new ParticleSystem.MinMaxCurve(look.velocity.z * 0.7f, look.velocity.z * 1.3f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = look.noise;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.2f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            FadeInOut(ps, look.pulse);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.maxParticleSize = WeatherMaxScreenSize;
            if (look.stretch > 0f) {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = look.stretch;
                renderer.lengthScale = 1f;
            }
            return ps;
        }

        readonly struct WeatherLook {
            public readonly Material material;
            public readonly float lifetime, noise, spin, stretch, alpha;
            public readonly Vector2 size;
            public readonly Vector3 velocity;
            public readonly bool pulse;

            public WeatherLook(Material material, float lifetime, Vector2 size, Vector3 velocity, float noise, float spin = 0f, float stretch = 0f,
                float alpha = 1f, bool pulse = false) {
                this.material = material;
                this.lifetime = lifetime;
                this.size = size;
                this.velocity = velocity;
                this.noise = noise;
                this.spin = spin;
                this.stretch = stretch;
                this.alpha = alpha;
                this.pulse = pulse;
            }
        }

        // ---------------------------------------------------------------- landmark effects
        /// <summary>A volcano's plume: big puffs rising from <paramref name="at"/>, growing and fading as they climb.</summary>
        static ParticleSystem PlumeSmoke(Transform parent, Vector3 at, float radius, Color color) {
            const float lifetime = 16f;
            var ps = NewEffect("Smoke", parent, Effects().smoke, ParticleSystemSimulationSpace.Local, 90, at);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.75f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.9f, radius * 1.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(color.WithAlpha(0.85f), Darker(color, 0.7f).WithAlpha(0.85f));
            var emission = ps.emission;
            emission.rateOverTime = 5f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = radius * 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var wind = ps.velocityOverLifetime;
            wind.enabled = true;
            wind.x = new ParticleSystem.MinMaxCurve(2f, 4f);
            wind.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            wind.z = new ParticleSystem.MinMaxCurve(-1f, 1f);
            FadeInOut(ps, false);
            Prewarm(ps);
            return ps;
        }

        /// <summary>Sparks streaming up from a disc of <paramref name="radius"/> to about <paramref name="height"/> above it.</summary>
        static ParticleSystem RisingSparks(Transform parent, Vector3 at, float radius, float height, Color a, Color b) {
            const float lifetime = 7f;
            var ps = NewEffect("Sparks", parent, Effects().glow, ParticleSystemSimulationSpace.Local, 200, at);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(height / lifetime * 0.8f, height / lifetime * 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.6f, 3.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            var emission = ps.emission;
            emission.rateOverTime = 24f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 4f;
            noise.frequency = 0.05f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            FadeInOut(ps, false);
            Prewarm(ps);
            return ps;
        }

        /// <summary>Motes drifting in a sphere around <paramref name="at"/>, pulsing as they go.</summary>
        static ParticleSystem Motes(Transform parent, Vector3 at, float radius, Color a, Color b) {
            const float lifetime = 10f;
            var ps = NewEffect("Motes", parent, Effects().glow, ParticleSystemSimulationSpace.Local, 160, at);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            var emission = ps.emission;
            emission.rateOverTime = 14f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 3f;
            noise.frequency = 0.04f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            FadeInOut(ps, true);
            Prewarm(ps);
            return ps;
        }

        // ---------------------------------------------------------------- particle helpers
        /// <summary>A looping particle system with no shadows, under <paramref name="parent"/> at <paramref name="at"/>.</summary>
        static ParticleSystem NewEffect(string name, Transform parent, Material material, ParticleSystemSimulationSpace space, int capacity, Vector3 at = default) {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = space;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = capacity;
            main.startSpeed = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return ps;
        }

        static void Prewarm(ParticleSystem ps) {
            var main = ps.main;
            main.prewarm = true;
        }

        /// <summary>Fades each particle in and out over its life; <paramref name="pulse"/> also breathes its size.</summary>
        static void FadeInOut(ParticleSystem ps, bool pulse) {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = gradient;
            if (!pulse) return;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(0.5f, 0.7f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0.5f));
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        static Color Darker(Color color, float factor) => OB_EnvMeshBuilder.Shade(color, factor);
    }
}
#endif
