using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>The three minigames that stand between the investigators and the boat.</summary>
    public enum CH_PuzzleKind : byte {
        /// <summary>Act I: prime the ferry's engine and pull the cord with the needle in the green, three times.</summary>
        Engine = 0,
        /// <summary>Act II: a drowned singer plays their hymn; sing it back on the choir's four keys.</summary>
        Hymn = 1,
        /// <summary>Act III: the bell is chained with three locks; turn each dial until it runs hot, then set it.</summary>
        BellLock = 2
    }

    /// <summary>What an investigator does at a station (<see cref="CH_PuzzleRequest"/>).</summary>
    public enum CH_PuzzleAction : byte {
        Open = 0,
        Close = 1,
        /// <summary>Engine: pull the cord now.</summary>
        Strike = 2,
        /// <summary>Hymn: play a note (the request's value).</summary>
        Press = 3,
        /// <summary>Lock: ask how warm the dial is at an angle (the request's value).</summary>
        Probe = 4,
        /// <summary>Lock: try to set the ring at an angle.</summary>
        Set = 5
    }

    /// <summary>What the server tells the investigator who has a station open (<see cref="CH_PuzzleBroadcast"/>).</summary>
    public enum CH_PuzzleEvent : byte {
        Opened = 0,
        /// <summary>A hit, a right note or a ring set: on to the next step.</summary>
        Progress = 1,
        /// <summary>A missed strike, a wrong note, a wrong lock: he heard it.</summary>
        Mistake = 2,
        Solved = 3,
        Closed = 4,
        /// <summary>He is coming: the investigator is pulled away from the station.</summary>
        Interrupted = 5,
        /// <summary>A reading of the lock's dial.</summary>
        Warmth = 6
    }

    /// <summary>One answer from a <see cref="CH_PuzzleRun"/>: everything the panel needs to draw the next moment.</summary>
    public struct CH_PuzzleStep {
        public CH_PuzzleEvent evt;
        public int progress, total;
        /// <summary>Lock: how warm the dial is, 0 cold to 1 hot.</summary>
        public float warmth;
        /// <summary>Engine: where the green zone is on the gauge (0 to 1), how wide, and how fast the needle sweeps.</summary>
        public float zoneCenter, zoneWidth, sweepSpeed;
        /// <summary>Hymn: the notes, to be played back in order.</summary>
        public int[] sequence;
        public bool mistake;
    }

    /// <summary>
    /// One investigator working one station, as plain rules with no networking or physics: the engine's needle, a hymn's
    /// notes, the bell's dial angles. Where it has got to lives on the <see cref="CH_Night.Station"/> so it survives the
    /// investigator stepping away (or being chased off) and coming back. The server drives one run per open station and
    /// tells the client only what the panel shows: a hymn's notes (it plays them), the engine's zone (it draws it), a
    /// dial's warmth, never its angle.
    /// </summary>
    public sealed class CH_PuzzleRun {
        readonly CH_Night.Station station;
        readonly CH_PuzzleTuning tuning;
        readonly int seed;
        readonly int[] sequence;
        readonly float[] ringAngles;
        float sweepStart;

        public CH_PuzzleKind Kind => station.Kind;
        public int StationId => station.Id;
        public int Progress => station.Progress;
        public int Total { get; }
        public bool Solved => station.Done;
        public int Mistakes { get; private set; }
        /// <summary>The hymn's notes (empty for the other minigames).</summary>
        public int[] Sequence => sequence;

        /// <param name="hymnOrder">For a hymn: how many singers were freed before this one, which makes it longer.</param>
        public CH_PuzzleRun(CH_Night.Station station, int seed, CH_PuzzleTuning tuning, float now, int hymnOrder = 0) {
            this.station = station;
            this.tuning = tuning;
            this.seed = seed;
            sweepStart = now;
            switch (station.Kind) {
                case CH_PuzzleKind.Engine:
                    Total = tuning.engineStrikes;
                    break;
                case CH_PuzzleKind.Hymn:
                    Total = tuning.hymnBaseLength + Mathf.Max(0, hymnOrder);
                    sequence = HymnSequence(seed, station.Singer, Total, tuning.hymnNotes);
                    break;
                default:
                    Total = tuning.lockRings;
                    ringAngles = LockAngles(seed, Total, tuning.lockMinimumSeparation);
                    break;
            }
        }

        /// <summary>The hymn a singer plays on a given night: the same notes every time that night, never one note three times running.</summary>
        public static int[] HymnSequence(int seed, int singer, int length, int notes) {
            var random = new System.Random(unchecked(seed * 7919 + singer * 104729 + 17));
            var result = new int[length];
            for (int i = 0; i < length; i++) {
                int note = random.Next(notes);
                if (i >= 2 && result[i - 1] == note && result[i - 2] == note) note = (note + 1 + random.Next(notes - 1)) % notes;
                result[i] = note;
            }
            return result;
        }

        /// <summary>The true angles of the bell's rings (degrees, 0 to 360), each at least <paramref name="separation"/> from the one before.</summary>
        public static float[] LockAngles(int seed, int rings, float separation) {
            var random = new System.Random(unchecked(seed * 15485863 + 29));
            var angles = new float[rings];
            for (int i = 0; i < rings; i++) {
                float angle;
                int tries = 0;
                do { angle = (float)(random.NextDouble() * 360.0); tries++; }
                while (i > 0 && CircularDistance(angle, angles[i - 1]) < separation && tries < 64);
                angles[i] = angle;
            }
            return angles;
        }

        /// <summary>The shortest way round between two angles, in degrees (0 to 180).</summary>
        public static float CircularDistance(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a, b));

        /// <summary>The needle's position on the gauge (0 to 1) <paramref name="elapsed"/> seconds into a sweep at <paramref name="speed"/>.</summary>
        public static float NeedleAt(float elapsed, float speed) => Mathf.PingPong(Mathf.Max(0f, elapsed) * speed, 1f);

        /// <summary>Where strike <paramref name="index"/>'s green zone sits, away from the gauge's ends.</summary>
        public static float ZoneCenter(int seed, int index, float width) {
            var random = new System.Random(unchecked(seed * 31 + index * 977 + 5));
            float margin = 0.08f + width * 0.5f;
            return margin + (float)random.NextDouble() * (1f - 2f * margin);
        }

        /// <summary>How warm a dial at <paramref name="angle"/> is for a ring whose true angle is <paramref name="target"/>.</summary>
        public static float Warmth(float angle, float target, float warmRange) {
            float cold = Mathf.Clamp01(CircularDistance(angle, target) / Mathf.Max(1f, warmRange));
            return 1f - cold * cold;
        }

        /// <summary>The state to show when the station opens.</summary>
        public CH_PuzzleStep Opening(float now) {
            sweepStart = now;
            return Describe(CH_PuzzleEvent.Opened, false);
        }

        /// <summary>Engine: the cord is pulled at <paramref name="now"/>.</summary>
        public CH_PuzzleStep Strike(float now) {
            if (Kind != CH_PuzzleKind.Engine || station.Done) return Describe(CH_PuzzleEvent.Closed, false);
            int index = station.Progress;
            float speed = tuning.SweepSpeed(index), width = tuning.ZoneWidth(index), center = ZoneCenter(seed, index, width);
            float elapsed = now - sweepStart;
            bool hit = Mathf.Abs(NeedleAt(elapsed, speed) - center) <= width * 0.5f
                || Mathf.Abs(NeedleAt(elapsed - tuning.engineForgiveness, speed) - center) <= width * 0.5f;
            sweepStart = now;
            return hit ? Advance() : Miss(); // a miss costs a hit, and the needle starts again
        }

        /// <summary>Hymn: note <paramref name="note"/> is played.</summary>
        public CH_PuzzleStep Press(int note) {
            if (Kind != CH_PuzzleKind.Hymn || station.Done) return Describe(CH_PuzzleEvent.Closed, false);
            if (note == sequence[station.Progress]) return Advance();
            station.Progress = 0; // the hymn starts again
            return Miss();
        }

        /// <summary>Lock: how warm the dial is at <paramref name="angle"/> for the ring being worked.</summary>
        public CH_PuzzleStep Probe(float angle) {
            if (Kind != CH_PuzzleKind.BellLock || station.Done) return Describe(CH_PuzzleEvent.Closed, false);
            CH_PuzzleStep step = Describe(CH_PuzzleEvent.Warmth, false);
            step.warmth = Warmth(angle, ringAngles[station.Progress], tuning.lockWarmRange);
            return step;
        }

        /// <summary>Lock: the ring is set at <paramref name="angle"/>; it takes only if the dial is on its true angle.</summary>
        public CH_PuzzleStep Set(float angle) {
            if (Kind != CH_PuzzleKind.BellLock || station.Done) return Describe(CH_PuzzleEvent.Closed, false);
            return CircularDistance(angle, ringAngles[station.Progress]) <= tuning.lockTolerance ? Advance() : Miss();
        }

        CH_PuzzleStep Advance() {
            station.Progress++;
            if (station.Progress >= Total) {
                station.Done = true;
                return Describe(CH_PuzzleEvent.Solved, false);
            }
            return Describe(CH_PuzzleEvent.Progress, false);
        }

        CH_PuzzleStep Miss() {
            Mistakes++;
            // The engine's miss costs the last hit (the cord frays); a wrong note or lock has already been dealt with above.
            if (Kind == CH_PuzzleKind.Engine) station.Progress = Mathf.Max(0, station.Progress - 1);
            return Describe(CH_PuzzleEvent.Mistake, true);
        }

        CH_PuzzleStep Describe(CH_PuzzleEvent evt, bool mistake) {
            var step = new CH_PuzzleStep { evt = evt, progress = station.Progress, total = Total, mistake = mistake, sequence = sequence };
            if (Kind == CH_PuzzleKind.Engine && !station.Done) {
                int index = Mathf.Min(station.Progress, Total - 1);
                step.sweepSpeed = tuning.SweepSpeed(index);
                step.zoneWidth = tuning.ZoneWidth(index);
                step.zoneCenter = ZoneCenter(seed, index, step.zoneWidth);
            }
            return step;
        }
    }
}
