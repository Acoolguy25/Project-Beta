using System;
using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>Silhouette of a tier's distant horizon rings (see OB_HorizonLayers).</summary>
    public enum OB_HorizonShape : byte { CloudBanks, Hills, Dunes, Peaks, Ridges, Shards }

    /// <summary>
    /// Big set-pieces floating far from the course. Values index authored prefabs; append new kinds at the end.
    /// </summary>
    public enum OB_LandmarkKind : byte {
        SkyCastle, WindmillIsle, BalloonFleet, WorldTree, WaterfallCliffs, Pyramids, MesaArch,
        IceMountain, GlacierSpires, Volcano, BasaltSpires, ShatteredPlanet, PortalRings
    }

    /// <summary>Ambient particles drifting around the camera.</summary>
    public enum OB_WeatherKind : byte { Pollen, Leaves, Sand, Snow, Embers, VoidMotes }

    /// <summary>What the course is built from: the rock under platforms, its supports, arches and lamps.</summary>
    public enum OB_BuildStyle : byte { Marble, Earth, Sandstone, Ice, Basalt, Crystal }

    /// <summary>What pours off the tier's islands and landmarks.</summary>
    public enum OB_FallKind : byte { None, Water, Sand, Ice, Lava, Void }

    /// <summary>A tier's sky: sun and moon placement, stars, aurora, nebula and high cirrus.</summary>
    [Serializable]
    public struct OB_SkyStyle {
        [Tooltip("Sun elevation and azimuth (degrees). Azimuth 0 looks down the course's start direction (+Z).")]
        public Vector2 sun;
        [Tooltip("Angular radius of the sun disc (degrees).")]
        [Min(0f)] public float sunRadius;
        [Min(0f)] public float sunHalo;
        [Tooltip("Moon or planet tint; alpha is how visible it is (0 hides it).")]
        public Color moon;
        [Tooltip("Moon elevation and azimuth (degrees).")]
        public Vector2 moonAt;
        [Min(0f)] public float moonRadius;
        [Tooltip("Planet ring colour; alpha is its strength (0 for a plain moon).")]
        public Color ring;
        [Tooltip("0..1 share of the sky that holds a star.")]
        [Range(0f, 1f)] public float stars;
        public Color auroraA, auroraB;
        [Min(0f)] public float aurora;
        public Color nebulaA, nebulaB;
        [Min(0f)] public float nebula;
        [Tooltip("High wispy cloud colour; alpha is coverage.")]
        public Color cirrus;

        /// <summary>World direction towards a point given as elevation and azimuth in degrees.</summary>
        public static Vector3 Direction(Vector2 elevationAzimuth) {
            float el = elevationAzimuth.x * Mathf.Deg2Rad, az = elevationAzimuth.y * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
        }
    }

    /// <summary>The three silhouette rings on a tier's horizon, nearest first.</summary>
    [Serializable]
    public struct OB_HorizonStyle {
        public OB_HorizonShape shape;
        [Tooltip("Colour at the top of each ring, nearest first.")]
        public Color near, mid, far;
        [Tooltip("Colour at the foot of the rings (they fade into it).")]
        public Color foot;
        [Tooltip("Emissive rim along the silhouette's crest (volcanic seams, neon edges); alpha is strength.")]
        public Color crestGlow;
        [Tooltip("Height of the tallest crests (m, on the nearest ring).")]
        [Min(0f)] public float height;
    }

    /// <summary>How the course is dressed: rock under platforms, supports, arches, lamps, banners and ropes.</summary>
    [Serializable]
    public struct OB_DressingStyle {
        public OB_BuildStyle build;
        [Tooltip("Main rock or stone, and its darker strata.")]
        public Color rock, rockDark;
        [Tooltip("Cap band right under the platform (grass, snow, gold trim).")]
        public Color cap;
        [Tooltip("Posts, arches and pillars.")]
        public Color frame, frameDark;
        [Tooltip("Cloth: banners and pennants, cycled.")]
        public Color[] cloth;
        [Tooltip("Lamp light, crack and crystal glow.")]
        public Color glow;
        [Min(0f)] public float glowStrength;
        [Tooltip("Ropes and chains between platforms.")]
        public Color rope;
    }

    /// <summary>Particles drifting around the camera.</summary>
    [Serializable]
    public struct OB_WeatherStyle {
        public OB_WeatherKind kind;
        public Color a, b;
        [Tooltip("Particles per second around the camera.")]
        [Min(0f)] public float rate;
    }

    /// <summary>
    /// A tier's world around the course: landmarks, horizon, sky, scenery, course dressing, weather,
    /// birds and falls. Colours here are part of the art bible (<see cref="OB_Themes"/>).
    /// </summary>
    public sealed class OB_WorldStyle {
        public OB_LandmarkKind[] landmarks;
        public OB_SkyStyle sky;
        public OB_HorizonStyle horizon;
        public OB_DressingStyle dressing;
        public OB_WeatherStyle weather;
        public OB_FallKind fall;
        public Color fallColor, fallHighlight;
        /// <summary>Glow of the falls (0 for water and sand).</summary>
        public float fallGlow;
        /// <summary>Floating rocks: lit top and shaded side.</summary>
        public Color rockTop, rockSide;
        /// <summary>Scenery per stage: floating rocks and small islands (multiplies the backdrop's base counts).</summary>
        public float density = 1f;
        /// <summary>Bird flocks per tier, and their colour (alpha 0 hides birds); glow for the void's wisps.</summary>
        public int flocks;
        public Color birds;
        public float birdGlow;
        /// <summary>Share of the planned cloud clusters this tier keeps (0 = no clouds, as in the void).</summary>
        public float cloudCover = 1f;
        /// <summary>Height of its cloud clusters against their width (1 = round cumulus; low values give flat ash or smoke banks).</summary>
        public float cloudSquash = 1f;
        /// <summary>Gravity fields: colour of their edge lines and faint faces, picked to stand out against this tier's sky.</summary>
        public Color field = new(0.26f, 0.88f, 1f);
    }
}
