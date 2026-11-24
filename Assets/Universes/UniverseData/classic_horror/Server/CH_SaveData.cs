using System;
using Newtonsoft.Json.Linq;
using RyanAssets.Progress;

namespace Universes.UniverseData.classic_horror.Server {
    /// <summary>What The Drowned Choir remembers about an investigator between sessions. Written only by the server.</summary>
    public sealed class CH_SaveData : ProgressData {
        public const string Universe = "classic_horror";
        public override int LatestVersion => 2;

        /// <summary>Nights the player saw through to an ending (escaped or drowned).</summary>
        public int nightsPlayed;
        /// <summary>Nights the player escaped Hollowmere on the boat.</summary>
        public int escapes;
        /// <summary>Escapes with Lily's music box returned (the "She came home" ending).</summary>
        public int trueEndings;
        /// <summary>Bit per ending (<see cref="CH_Story"/> Ending* index) the player has seen.</summary>
        public int endingsSeen;
        /// <summary>Bit per drowned singer (<see cref="CH_Story.Singers"/>) whose relic the player laid on their grave.</summary>
        public int singersMet;
        /// <summary>Boat parts, relics, music boxes and bells the player delivered themselves.</summary>
        public int itemsDelivered;
        /// <summary>Chases the player got away from.</summary>
        public int chasesEscaped;
        /// <summary>Times the priest caught the player.</summary>
        public int timesCaught;
        /// <summary>Quickest escape in seconds; 0 until the first one.</summary>
        public int fastestEscapeSeconds;

        public void RecordEnding(byte ending, int nightSeconds) {
            nightsPlayed++;
            if (ending < CH_Story.EndingCount) endingsSeen |= 1 << ending;
            if (ending is not (CH_Story.EndingTrue or CH_Story.EndingEscaped)) return;
            escapes++;
            if (ending == CH_Story.EndingTrue) trueEndings++;
            if (nightSeconds > 0 && (fastestEscapeSeconds == 0 || nightSeconds < fastestEscapeSeconds)) fastestEscapeSeconds = nightSeconds;
        }

        /// <summary>The record as the ending card shows it.</summary>
        public CH_ProgressBroadcast ToBroadcast() => new() {
            nightsPlayed = nightsPlayed, escapes = escapes, trueEndings = trueEndings,
            endingsSeen = endingsSeen, singersMet = singersMet, bestSeconds = fastestEscapeSeconds
        };

        /// <summary>Version 1 was the investigation game: its closed cases count as escapes, its fastest case as the record.</summary>
        public override void Migrate(int fromVersion, JObject stored) {
            if (fromVersion >= 2 || stored == null) return;
            escapes = Math.Max(escapes, stored.Value<int?>("casesCompleted") ?? 0);
            nightsPlayed = Math.Max(nightsPlayed, escapes);
            fastestEscapeSeconds = stored.Value<int?>("fastestCaseSeconds") ?? 0;
            itemsDelivered = Math.Max(itemsDelivered, stored.Value<int?>("evidenceFound") ?? 0);
        }

        public override void Validate() {
            nightsPlayed = Math.Max(0, nightsPlayed);
            escapes = Math.Clamp(escapes, 0, nightsPlayed);
            trueEndings = Math.Clamp(trueEndings, 0, escapes);
            endingsSeen &= (1 << CH_Story.EndingCount) - 1;
            singersMet &= (1 << CH_Story.SingerCount) - 1;
            itemsDelivered = Math.Max(0, itemsDelivered);
            chasesEscaped = Math.Max(0, chasesEscaped);
            timesCaught = Math.Max(0, timesCaught);
            fastestEscapeSeconds = Math.Max(0, fastestEscapeSeconds);
        }
    }
}
