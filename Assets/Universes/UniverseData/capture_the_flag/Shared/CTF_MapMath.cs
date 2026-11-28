using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>
    /// Pure maths behind the tactical minimap: where a world point lands on the map, which way your
    /// arrow points, and when an enemy shows. The map is drawn north-up for Red; Blue sees it turned
    /// half a circle, so every player has their own base on the left and attacks to the right.
    /// </summary>
    public static class CTF_MapMath {
        /// <summary>Whether <paramref name="team"/> sees the map turned half a circle (its base is on the world's right).</summary>
        public static bool Flipped(CTF_Team team) => team == CTF_Team.Blue;

        /// <summary>
        /// A world point as a position on the map, 0..1 on each side (0,0 bottom left), clamped to the
        /// edge so someone just outside the fence still shows on the rim. <paramref name="field"/> is the
        /// arena on the ground plane: x along the world's x, y along the world's z.
        /// </summary>
        public static Vector2 ToMap(Vector3 world, Rect field, bool flipped) {
            if (field.width <= 0f || field.height <= 0f) return new Vector2(0.5f, 0.5f);
            var p = new Vector2(Mathf.Clamp01((world.x - field.xMin) / field.width), Mathf.Clamp01((world.z - field.yMin) / field.height));
            return flipped ? Vector2.one - p : p;
        }

        /// <summary>
        /// The UI rotation (degrees about z, counter-clockwise) that turns an up-pointing arrow to face
        /// <paramref name="forward"/> on the map. Looking straight up or down keeps the last heading: 0 here.
        /// </summary>
        public static float ArrowDegrees(Vector3 forward, bool flipped) {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return flipped ? 180f : 0f;
            // Compass heading: 0 = world +z (map up), 90 = world +x (map right), clockwise.
            float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            return Mathf.Repeat(-heading + (flipped ? 180f : 0f), 360f);
        }

        /// <summary>
        /// How strongly a spotted enemy shows (0..1): fully until the last <paramref name="fadeSeconds"/>
        /// before <paramref name="until"/>, then fading out, and 0 once it has passed.
        /// </summary>
        public static float SpotAlpha(float now, float until, float fadeSeconds) {
            if (now >= until) return 0f;
            if (fadeSeconds <= 0f) return 1f;
            return Mathf.Clamp01((until - now) / fadeSeconds);
        }

        /// <summary>Extends a spot to <paramref name="now"/> + <paramref name="seconds"/>, never shortening one already longer (a radar sweep outlasts a shot).</summary>
        public static float Spot(float current, float now, float seconds) => Mathf.Max(current, now + Mathf.Max(0f, seconds));

        /// <summary>
        /// Whether an enemy shows on your map: always while carrying your flag (the danger is the point),
        /// while your team's radar sweep runs, or for a moment after they fire or kill you.
        /// </summary>
        public static bool EnemyShown(bool carriesYourFlag, bool radarOn, float spotAlpha) =>
            carriesYourFlag || radarOn || spotAlpha > 0f;
    }
}
