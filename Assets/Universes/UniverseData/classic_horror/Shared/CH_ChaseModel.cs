using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// A straight-line model of a chase, used to keep Father Crane's numbers honest: an investigator runs for a goal while he
    /// follows at a fixed pace. They sprint until their stamina is gone, walk until it is back to
    /// <see cref="SprintAgainAt"/>, and sprint again, with the shared character's stamina rules
    /// (<see cref="CH_MonsterTuning.InvestigatorStamina"/> and friends). He catches them when he is within striking
    /// distance, or within lunging range while they walk. Unless told otherwise he closes in as the
    /// gap opens (<see cref="CH_MonsterTuning.ClosingPace"/>), as he does in the game. The real village has corners that favour neither side; the
    /// model answers "can a good player make it, and by how much".
    /// </summary>
    public static class CH_ChaseModel {
        /// <summary>The fraction of full stamina at which a tired investigator sprints again.</summary>
        public const float SprintAgainAt = 0.35f;
        const float Step = 0.02f;

        public struct Result {
            /// <summary>They reached the goal.</summary>
            public bool escaped;
            /// <summary>The gap (metres) when they reached the goal, or 0 if caught.</summary>
            public float finalGap;
            /// <summary>How far they got (metres).</summary>
            public float distanceCovered;
            public float seconds;
        }

        /// <param name="pursuerSpeed">His pace (m/s), from <see cref="CH_MonsterTuning.ChaseSpeed"/>.</param>
        /// <param name="holdSeconds">How long he stands still before he follows (the bell grab's stare, a drag-out's stagger).</param>
        /// <param name="distance">Metres to the goal.</param>
        /// <param name="startGap">Metres between them when the chase begins.</param>
        /// <param name="fixedSpeed">A fixed pace for an investigator who walks or cannot sprint (the fuel can), or 0 to sprint.</param>
        /// <param name="closesIn">Whether he presses harder as the gap opens; false for the bell, which he chases flat out.</param>
        public static Result Run(CH_MonsterTuning tuning, float pursuerSpeed, float holdSeconds, float distance, float startGap,
            float fixedSpeed = 0f, bool closesIn = true) {
            float covered = 0f, pursuer = -startGap, stamina = CH_MonsterTuning.InvestigatorStamina, regenDelay = 0f, time = 0f;
            bool sprinting = fixedSpeed <= 0f;
            while (covered < distance) {
                float speed;
                if (fixedSpeed > 0f) speed = fixedSpeed;
                else {
                    if (sprinting && stamina <= 0f) sprinting = false;
                    else if (!sprinting && stamina >= CH_MonsterTuning.InvestigatorStamina * SprintAgainAt) sprinting = true;
                    speed = sprinting ? CH_MonsterTuning.InvestigatorSprintSpeed : CH_MonsterTuning.InvestigatorWalkSpeed;
                    if (sprinting) {
                        stamina -= CH_MonsterTuning.SprintStaminaPerSecond * Step;
                        regenDelay = CH_MonsterTuning.StaminaRegenDelay;
                    } else if ((regenDelay -= Step) <= 0f) {
                        stamina = Mathf.Min(CH_MonsterTuning.InvestigatorStamina, stamina + CH_MonsterTuning.StaminaRegenPerSecond * Step);
                    }
                }
                covered += speed * Step;
                time += Step;
                if (time > holdSeconds) pursuer += (closesIn ? tuning.ClosingPace(pursuerSpeed, covered - pursuer) : pursuerSpeed) * Step;
                float gap = covered - pursuer;
                bool running = speed >= CH_MonsterTuning.InvestigatorSprintSpeed;
                if (time > holdSeconds && (gap < tuning.strikeDistance || (!running && gap < tuning.lungeRange)))
                    return new Result { escaped = false, finalGap = 0f, distanceCovered = covered, seconds = time };
            }
            return new Result { escaped = true, finalGap = covered - pursuer, distanceCovered = covered, seconds = time };
        }
    }
}
