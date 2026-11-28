using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// Every number a designer would move in the three escape minigames (the engine, the hymns, the bell's locks) and in
    /// the Hush, serialized on the server runner. <see cref="CH_PuzzleRun"/> and the server read nothing else.
    /// </summary>
    [System.Serializable]
    public sealed class CH_PuzzleTuning {
        [Header("Reach")]
        [Tooltip("How close an investigator must stand to start a minigame (metres, eye to the station).")]
        [Min(1f)] public float reach = 3.4f;
        [Tooltip("A minigame closes when the investigator is pushed or drawn further than this from its station.")]
        [Min(1f)] public float walkAwayDistance = 5f;
        [Tooltip("Shortest gap between two inputs from one investigator (seconds); the lock's dial is probed more often.")]
        [Min(0.01f)] public float inputGap = 0.07f;

        [Header("Engine: turn her over")]
        [Tooltip("Hits needed to start the engine.")]
        [Range(1, 6)] public int engineStrikes = 3;
        [Tooltip("How fast the needle sweeps the gauge on each strike (full sweeps per second).")]
        public Vector3 engineSweepSpeeds = new(0.85f, 1.15f, 1.5f);
        [Tooltip("How wide the green zone is on each strike (fraction of the gauge).")]
        public Vector3 engineZoneWidths = new(0.24f, 0.17f, 0.12f);
        [Tooltip("The needle position is read this far back (seconds), so a laggy connection is not punished.")]
        [Min(0f)] public float engineForgiveness = 0.15f;

        [Header("Hymns: sing it back")]
        [Tooltip("Notes in the first hymn of a night; each further singer's hymn is one note longer.")]
        [Range(2, 8)] public int hymnBaseLength = 3;
        [Tooltip("Different notes on the choir's keyboard.")]
        [Range(2, 6)] public int hymnNotes = 4;

        [Header("Bell: three locks")]
        [Range(1, 5)] public int lockRings = 3;
        [Tooltip("Degrees either side of a ring's true angle that still opens it.")]
        [Min(1f)] public float lockTolerance = 8f;
        [Tooltip("The dial is cold this many degrees from the true angle and warms to hot as it closes.")]
        [Min(10f)] public float lockWarmRange = 110f;
        [Tooltip("Rings sit at least this many degrees from the one before.")]
        [Min(0f)] public float lockMinimumSeparation = 50f;

        [Header("Noise: he hears the work and the mistakes")]
        [Tooltip("While someone works at the engine, he hears them this far (metres), through walls.")]
        [Min(0f)] public float engineWorkNoise = 26f;
        [Min(0f)] public float hymnWorkNoise = 13f;
        [Min(0f)] public float lockWorkNoise = 15f;
        [Tooltip("A missed engine strike roars across the water this far.")]
        [Min(0f)] public float engineMissNoise = 60f;
        [Tooltip("A wrong note wails through the graveyard this far.")]
        [Min(0f)] public float hymnWrongNoise = 45f;
        [Tooltip("A wrong lock clanks the chain this far.")]
        [Min(0f)] public float lockClankNoise = 50f;
        [Tooltip("Seconds he searches the spot a noise came from.")]
        [Min(1f)] public float noiseSearchSeconds = 12f;

        [Header("The Hush")]
        [Tooltip("Seconds between Hushes in Acts II and III (a random time in this range).")]
        public Vector2 hushInterval = new(75f, 130f);
        [Min(1f)] public float hushSeconds = 9f;
        [Tooltip("During a Hush he hears a walker this far, and someone standing still this far (metres).")]
        [Min(0f)] public float hushWalkHearing = 24f;
        [Min(0f)] public float hushStillHearing = 9f;
        [Min(0f)] public float hushSprintHearing = 55f;

        /// <summary>The needle's sweep speed on strike <paramref name="index"/> (0-based; the last entry repeats).</summary>
        public float SweepSpeed(int index) => Pick(engineSweepSpeeds, index);

        /// <summary>The green zone's width on strike <paramref name="index"/>.</summary>
        public float ZoneWidth(int index) => Pick(engineZoneWidths, index);

        static float Pick(Vector3 values, int index) => index <= 0 ? values.x : index == 1 ? values.y : values.z;

        /// <summary>How far he hears someone moving at <paramref name="speed"/> during a Hush, given the walk and sprint thresholds.</summary>
        public float HushHearing(float speed, float walkSpeedThreshold, float sprintSpeedThreshold) =>
            speed > sprintSpeedThreshold ? hushSprintHearing : speed > walkSpeedThreshold ? hushWalkHearing : hushStillHearing;
    }
}
