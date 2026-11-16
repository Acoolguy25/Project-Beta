using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Editor.Manor {
    /// <summary>The light a lit prop gives off, placed inside the model (plan metres before the map scale).</summary>
    public readonly struct PropLight {
        /// <summary>Height of the light as a fraction of the model's height (inside the shade, flame or pumpkin).</summary>
        public readonly float height;
        public readonly Color color;
        public readonly float range, intensity;
        /// <summary>Candles and lanterns flicker on clients (<c>HS_LightFlicker</c>); lamps burn steady.</summary>
        public readonly bool flicker;

        public PropLight(float height, Color color, float range, float intensity, bool flicker) {
            this.height = height; this.color = color; this.range = range; this.intensity = intensity; this.flicker = flicker;
        }
    }

    /// <summary>
    /// How Pumpkin Hollow Manor is lit: an autumn dusk outside (low warm sun, dim violet sky, light fog)
    /// and, inside, warm pools of light from the lamps, candles and lanterns marked <c>lit</c> in
    /// <see cref="HS_ManorPlan"/>, over a dark ambient. All lights are shadowless point lights and there
    /// are about thirty, each with a short range, so the forward renderer's per-object light
    /// limit is never the bottleneck. Tune here, then run Ryan > Hide and Seek > Build Client Assets.
    /// </summary>
    public static class HS_ManorLighting {
        // ---- dusk outside
        public static readonly Color SkyTint = new(0.66f, 0.52f, 0.6f);
        public static readonly Color FogColor = new(0.36f, 0.31f, 0.36f);
        /// <summary>Fog distances in world units (the map is about 210 units across).</summary>
        public const float FogStart = 110f, FogEnd = 620f;
        public static readonly Vector3 SunEuler = new(16f, -62f, 0f);
        public const float SunIntensity = 0.95f;
        public static readonly Color SunColor = new(1f, 0.72f, 0.48f);

        // ---- ambient: low, so the lamps make the pools of light; warm near the floor like bounced lamplight.
        public static readonly Color AmbientSky = new(0.34f, 0.34f, 0.45f);
        public static readonly Color AmbientEquator = new(0.33f, 0.28f, 0.25f);
        public static readonly Color AmbientGround = new(0.17f, 0.14f, 0.12f);

        // ---- room fill lights (ceiling, for rooms without a lamp of their own)
        public static readonly Color FillColor = new(1f, 0.84f, 0.64f);
        public const float FillIntensity = 1.4f;
        /// <summary>Fill range is this times the room's longer side, clamped to <see cref="FillRange"/> (plan metres).</summary>
        public const float FillRangeFactor = 0.8f;
        public static readonly Vector2 FillRange = new(4f, 9f);
        /// <summary>How far below the ceiling the fill light hangs (plan metres).</summary>
        public const float FillDrop = 0.6f;

        static readonly Color Lamp = new(1f, 0.76f, 0.48f);
        static readonly Color Flame = new(1f, 0.6f, 0.26f);
        static readonly Color Pumpkin = new(1f, 0.48f, 0.12f);
        static readonly Color StreetLamp = new(1f, 0.82f, 0.56f);

        /// <summary>The light a model gives off when it is placed with <c>lit: true</c>.</summary>
        public static bool TryGetPropLight(string model, out PropLight light) {
            light = model switch {
                "furniture/lamp_standing" => new PropLight(0.82f, Lamp, 6.5f, 2.4f, false),
                "furniture/lamp_table" => new PropLight(0.75f, Lamp, 4.5f, 2f, false),
                "halloween/lantern_standing" => new PropLight(0.75f, Flame, 4.5f, 2.2f, true),
                "halloween/lantern_hanging" => new PropLight(0.4f, Flame, 5f, 2.2f, true),
                "halloween/post_lantern" => new PropLight(0.9f, Flame, 7f, 2.6f, true),
                "city/streetlight" => new PropLight(0.92f, StreetLamp, 9f, 2.8f, false),
                "halloween/shrine_candles" or "halloween/plaque_candles" or "halloween/candle" or "halloween/candle_thin"
                    or "halloween/candle_triple" or "halloween/candle_melted" or "halloween/skull_candle"
                    or "dungeon/candle" or "dungeon/candle_thin" or "dungeon/candle_triple" or "dungeon/candle_melted" or "dungeon/shelf_small_candles"
                    => new PropLight(0.95f, Flame, 3.4f, 1.7f, true),
                "halloween/pumpkin_orange_jackolantern" or "halloween/pumpkin_yellow_jackolantern" => new PropLight(0.45f, Pumpkin, 3.2f, 1.9f, true),
                _ => default,
            };
            return light.range > 0f;
        }

        /// <summary>Like <see cref="TryGetPropLight"/>, but a plan that marks an unlightable model as lit is a mistake worth stopping for.</summary>
        public static PropLight PropLightFor(string model) =>
            TryGetPropLight(model, out PropLight light) ? light
                : throw new System.ArgumentException($"[HideAndSeek] '{model}' is marked lit but has no light in {nameof(HS_ManorLighting)}.{nameof(TryGetPropLight)}.");
    }
}
