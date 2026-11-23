using System;
using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// The authored cutscenes, as camera shots placed in the map. Each shot moves the camera from one mark to another
    /// while it looks at a target, and may stand the priest (a client-only actor) on a mark. Clients play the scene that
    /// matches the server's cutscene phase; the server holds the night for <see cref="CH_Story.CutsceneSeconds"/>.
    /// Built by <b>Ryan > Classic Horror > Build Story Set</b>.
    /// </summary>
    public sealed class CH_CutsceneSet : MonoBehaviour {
        public enum ActorPose : byte { Hidden = 0, Stand = 1, Walk = 2, Rise = 3, Lunge = 4 }

        [Serializable]
        public struct Shot {
            [Tooltip("Where the camera starts this shot.")]
            public Transform from;
            [Tooltip("Where it ends (leave empty to hold still).")]
            public Transform to;
            [Tooltip("What it looks at (leave empty to keep the marks' own rotation).")]
            public Transform lookAt;
            [Min(0.1f)] public float seconds;
            [Range(20f, 100f)] public float fieldOfView;
            [Tooltip("Camera shake at the start of the shot (0 to 1).")]
            [Range(0f, 1f)] public float shake;
            [Tooltip("Where the priest stands in this shot (empty: not in it).")]
            public Transform actorMark;
            [Tooltip("Where he moves to over the shot (empty: he stays on his mark).")]
            public Transform actorTarget;
            public ActorPose actorPose;
            [Tooltip("A sound cue at the start of the shot: thud, bell, scream, doors, engine or splash.")]
            public string cue;
        }

        [Serializable]
        public struct Scene {
            public CH_Phase phase;
            public Shot[] shots;
            [Tooltip("A title card over the last shot, \"TITLE\\nsubtitle\".")]
            public string titleCard;
        }

        public Scene[] scenes = new Scene[0];

        public bool TryGet(CH_Phase phase, out Scene scene) {
            foreach (Scene candidate in scenes)
                if (candidate.phase == phase && candidate.shots != null && candidate.shots.Length > 0) { scene = candidate; return true; }
            scene = default;
            return false;
        }

        /// <summary>The shot playing <paramref name="seconds"/> into the scene, and how far through it (0 to 1).</summary>
        public static int ShotAt(Scene scene, float seconds, out float progress) {
            float start = 0f;
            for (int i = 0; i < scene.shots.Length; i++) {
                float length = Mathf.Max(0.1f, scene.shots[i].seconds);
                if (seconds < start + length || i == scene.shots.Length - 1) {
                    progress = Mathf.Clamp01((seconds - start) / length);
                    return i;
                }
                start += length;
            }
            progress = 1f;
            return scene.shots.Length - 1;
        }
    }
}
