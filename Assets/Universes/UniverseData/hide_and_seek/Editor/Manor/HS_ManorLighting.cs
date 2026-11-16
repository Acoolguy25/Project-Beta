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
        /// <summary>Diameter of the soft glow sprite at the light (plan metres): the shade, flame or pumpkin face looks lit.</summary>
        public readonly float glow;

        public PropLight(float height, Color color, float range, float intensity, bool flicker, float glow) {
            this.height = height; this.color = color; this.range = range; this.intensity = intensity; this.flicker = flicker; this.glow = glow;
        }
    }

    /// <summary>
    /// How Pumpkin Hollow Manor is lit: an autumn dusk outside (low warm sun, dim violet sky, light fog)
    /// and, inside, warm pools of light from the lamps, candles and lanterns marked <c>lit</c> in
    /// <see cref="HS_ManorPlan"/>, over a dark ambient. All lights are shadowless point lights with
    /// ranges that keep them in their room; the builder checks that no surface is reached by more than
    /// the renderer's per-object limit (<see cref="MaxLightsPerObject"/>) and fails the build if one is.
    /// Every lit prop also gets an HDR glow sprite at its light, so the shade or flame itself glows
    /// (bloom picks it up). The sun stays high enough that it only reaches just inside the windows.
    /// Tune here, then run Ryan > Hide and Seek > Build Client Assets.
    /// </summary>
    public static class HS_ManorLighting {
        // ---- dusk outside
        public static readonly Color SkyTint = new(0.58f, 0.44f, 0.56f);
        /// <summary>The procedural sky's brightness (the kit's default is 1.25): a dusk sky, not daylight.</summary>
        public const float SkyExposure = 0.7f;
        /// <summary>Fog matches the dusk horizon, so the far edge of the ground melts into it.</summary>
        public static readonly Color FogColor = new(0.3f, 0.25f, 0.31f);
        /// <summary>Fog distances in world units (the map is about 210 units across, the ground ends about 125 from its centre).</summary>
        public const float FogStart = 45f, FogEnd = 240f;
        /// <summary>
        /// High enough (38 degrees) that sunlight through a window lands on the floor near it instead of
        /// raking across the room onto inside walls, and soft: the lamps light the interior.
        /// </summary>
        public static readonly Vector3 SunEuler = new(38f, -62f, 0f);
        public const float SunIntensity = 0.5f;
        public static readonly Color SunColor = new(1f, 0.7f, 0.5f);

        /// <summary>The URP asset's additional-lights-per-object limit (StarterAssetsURPAsset: 8).</summary>
        public const int MaxLightsPerObject = 8;
        /// <summary>Floors, ceilings and roof slopes are cut into tiles no longer than this (plan metres) to stay under that limit.</summary>
        public const float MaxTile = 4f;
        /// <summary>The same for the lawn, whose lanterns are further apart.</summary>
        public const float MaxLawnTile = 20f;
        /// <summary>HDR multiplier of the glow sprites (bloom threshold is 0.9).</summary>
        public const float GlowIntensity = 1.6f;
        /// <summary>The glow sprite sits this many times the lit model's radius towards the camera, in front of the shade or glass around the light...</summary>
        public const float GlowPullFactor = 1.6f;
        /// <summary>...clamped to this range (plan metres).</summary>
        public static readonly Vector2 GlowPull = new(0.1f, 1.8f);
        /// <summary>Largest share of the screen height a glow sprite covers, so a lamp beside the camera stays a lamp.</summary>
        public const float GlowMaxScreenHeight = 0.12f;

        // ---- ambient: low, so the lamps make the pools of light; warm near the floor like bounced lamplight.
        public static readonly Color AmbientSky = new(0.34f, 0.34f, 0.45f);
        public static readonly Color AmbientEquator = new(0.33f, 0.28f, 0.25f);
        public static readonly Color AmbientGround = new(0.17f, 0.14f, 0.12f);

        // ---- room fill lights (ceiling, for rooms without a lamp of their own)
        public static readonly Color FillColor = new(1f, 0.84f, 0.64f);
        public const float FillIntensity = 1.4f;
        /// <summary>Fill range is this times the room's longer side, clamped to <see cref="FillRange"/> (plan metres).</summary>
        public const float FillRangeFactor = 0.8f;
        public static readonly Vector2 FillRange = new(3.5f, 6f);
        /// <summary>How far below the ceiling the fill light hangs (plan metres).</summary>
        public const float FillDrop = 0.6f;

        static readonly Color Lamp = new(1f, 0.76f, 0.48f);
        static readonly Color Flame = new(1f, 0.6f, 0.26f);
        static readonly Color Pumpkin = new(1f, 0.48f, 0.12f);
        static readonly Color StreetLamp = new(1f, 0.82f, 0.56f);

        /// <summary>The light a model gives off when it is placed with <c>lit: true</c>.</summary>
        public static bool TryGetPropLight(string model, out PropLight light) {
            light = model switch {
                // Ranges in plan metres: rooms are 4-8 m across, so a lamp lights its own room, not the next one.
                "furniture/lamp_standing" => new PropLight(0.82f, Lamp, 5f, 2.6f, false, 1.2f),
                "furniture/lamp_table" => new PropLight(0.75f, Lamp, 3.6f, 2.1f, false, 0.9f),
                "halloween/lantern_standing" => new PropLight(0.75f, Flame, 4f, 2.2f, true, 0.6f),
                "halloween/lantern_hanging" => new PropLight(0.4f, Flame, 4.5f, 2.4f, true, 0.45f),
                "halloween/post_lantern" => new PropLight(0.9f, Flame, 4.5f, 2.6f, true, 0.6f),
                "city/streetlight" => new PropLight(0.92f, StreetLamp, 7f, 3f, false, 0.9f),
                "halloween/shrine_candles" or "halloween/plaque_candles" or "halloween/candle" or "halloween/candle_thin"
                    or "halloween/candle_triple" or "halloween/candle_melted" or "halloween/skull_candle"
                    or "dungeon/candle" or "dungeon/candle_thin" or "dungeon/candle_triple" or "dungeon/candle_melted" or "dungeon/shelf_small_candles"
                    => new PropLight(0.95f, Flame, 3f, 1.7f, true, 0.3f),
                "halloween/pumpkin_orange_jackolantern" or "halloween/pumpkin_yellow_jackolantern" => new PropLight(0.45f, Pumpkin, 3f, 2f, true, 0.7f),
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
