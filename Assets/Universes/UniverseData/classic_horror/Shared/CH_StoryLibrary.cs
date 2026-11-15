using System;
using System.Collections.Generic;
using RyanAssets.Core;
using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    [CreateAssetMenu(menuName = "Universes/Classic Horror/Story Library")]
    public sealed class CH_StoryLibrary : ScriptableObject {
        public string[] witnesses = { "Mara Vale", "Elias Finch", "Ada Wren", "Thomas Reed", "Iris Bell", "Jonah Pike" };
        public string[] missingPeople = { "the ferryman's daughter", "a night watchman", "a visiting surveyor", "the last schoolteacher", "a railway porter", "the chapel keeper" };
        public string[] causes = {
            "the council drained a sealed grave to make room for the flood pumps",
            "a salvage crew brought a bell up from a house that predates the village",
            "the chapel keeper answered a voice beneath the floorboards",
            "the night watch played a waterlogged recording on the emergency radio",
            "a surveyor removed three seals from the old sluice"
        };
        [TextArea] public string[] dispatch = {
            "DISPATCH / {witness} reported {missing} missing. Their last call came from this settlement. Find four records. Learn what is hunting here before you approach its source.",
            "DISPATCH / The water rose without rain. {witness} says {missing} was still inside. Four surviving records may tell us why. Keep your light close.",
            "DISPATCH / We received a distress call in your voice. {witness} was the only witness. Trace four records before you touch anything that answers back."
        };
        [TextArea] public string[] confession = {
            "{witness}'s confession / We told ourselves it was an accident. It began when {cause}. {missing} tried to warn us. We heard knocking long after the water covered the door.",
            "Interview transcript / {witness}: It was not the flood. It was what we did before it. {cause}. After that, {missing} stopped casting a reflection.",
            "Recovered letter / My name is {witness}. If you find this, remember that {cause}. We left {missing} behind. The thing in the water learned how to wait."
        };
        [TextArea] public string[] ritualTemplates = {
            "Keeper's instructions / Three offerings, in this order: {order}. The wrong order wakes it. Recover the salt, bell and lantern before returning to the source.",
            "Scorched liturgy / Start with {first}. Follow with {second}. End with {third}. Carry all three offerings. Do not trust yesterday's instructions.",
            "Wax-cylinder transcript / {order}. Say it until you remember. The keeper changed the order after each flood. All three objects must reach the source."
        };
        [TextArea] public string[] memories = {
            "A child's drawing / {missing} stands on the bank with {witness}. On the reverse: 'Please bring us both home.' You preserve the drawing.",
            "Unsent postcard / {witness} promised to return before the water froze. The postmark is tomorrow. You preserve the name written beneath it.",
            "Memorial photograph / Every face has been rubbed away except {witness}'s. Someone has written 'We were people before we were a story.' You preserve the photograph.",
            "A recorded lullaby / Beneath the static, {witness} says the name of {missing}. For a moment the knocking falls silent. You preserve the recording."
        };
        public string[] warningLines = {
            "RADIO / Those footsteps stopped when you stopped. Keep moving between cover.",
            "RADIO / Something is using the water to listen. Break its line of sight.",
            "RADIO / That is not another investigator. Remember the behavior in your notes.",
            "RADIO / The lights are failing ahead of you. Do not let it close the distance."
        };

        [Header("The priest")]
        public string[] priests = { "Father Aldous Crane", "Father Silas Merrow", "Father Edmund Vey" };
        [TextArea] public string[] diaryPages = {
            "{priest}'s diary, page one / The river came up the chapel steps tonight. Something beneath the water spoke to me in my mother's voice. It offered to hold the flood back. It only wanted names: the names nobody would miss.",
            "{priest}'s diary, page two / I gave it the drowned first, then the strangers, then the poor. The water stayed low. I told the village it was prayer. When {cause}, the bargain broke, and it asked for {missing}.",
            "{priest}'s diary, final page / I tried to undo it and walked into the water with the bell. I am still walking. If anyone reads this, say my name at the source. {priest}. Let it be the last name I give."
        };
        [Tooltip("Radio replies after each required record, indexed by evidence 0-3.")]
        [TextArea] public string[] evidenceReplies = {
            "{witness} / So now you know. I kept that secret for years. Please... keep looking. {missing} deserves better than me.",
            "{witness} / That is how it hunted us. Every night the same. Do not let it learn your habits too.",
            "{witness} / The keeper wrote those instructions with shaking hands. Get the order right. It does not forgive mistakes.",
            "{witness} / That map. The chapel never had a basement, but the priest dug one anyway. Whatever answered him is down there."
        };
        [Tooltip("Lines heard on the radio as the case progresses. Index 0: early static, 1: halfway, 2: Chapter II, 3: escape.")]
        [TextArea] public string[] storyBeats = {
            "RADIO / ...static... a voice that is not dispatch: \"{witness}? Is that you? The water is warm down here.\"",
            "RADIO / The chapel bell rang once. No one is near it. Under the static, {missing} is whispering your names, one for every record you found.",
            "RADIO / A man's voice, very calm: \"You have seen what this place gave away. Come down to {source}, and give me one more.\"",
            "RADIO / The voice of {missing}: \"Don't leave me in the water again. Please. I can hear you running.\""
        };
        public string[] priestWhispers = {
            "RADIO / A sermon in reverse, coming through the static. It is getting closer.",
            "RADIO / \"Every name is an offering,\" the voice says. It knows yours now.",
            "RADIO / Footsteps in water, slow and patient, right behind the signal."
        };

        public CH_Case Generate(int seed, int locationCount, int sourceCount) {
            if (locationCount < 9 || sourceCount < 1) throw new ArgumentException("A case needs nine search locations and a source.");
            var random = new SeededStoryRandom(seed);
            var result = new CH_Case { Seed = seed, Temperament = (CH_Temperament)random.Next(3), SourceIndex = random.Next(sourceCount) };
            string witness = random.Pick(witnesses), missing = random.Pick(missingPeople), cause = random.Pick(causes);
            result.Order = new[] { 0, 1, 2 };
            random.Shuffle(result.Order);
            var locations = new List<int>();
            for (int i = 0; i < locationCount; i++) locations.Add(i);
            random.Shuffle(locations);
            result.LocationIndices = locations.GetRange(0, 9).ToArray();
            var tokens = new Dictionary<string, string> {
                ["witness"] = witness, ["missing"] = missing, ["cause"] = cause,
                ["first"] = CH_Case.Offerings[result.Order[0]], ["second"] = CH_Case.Offerings[result.Order[1]],
                ["third"] = CH_Case.Offerings[result.Order[2]],
                ["order"] = string.Join(" -> ", Array.ConvertAll(result.Order, n => CH_Case.Offerings[n]))
            };
            string Expand(string template) => SeededStoryRandom.Expand(template, tokens);
            result.Title = new[] { "The Lantern Beneath", "A Face in Still Water", "The Listening Flood" }[(int)result.Temperament] + " / " + witness;
            result.Introduction = Expand(random.Pick(dispatch));
            string[] rules = {
                "Light observation / It follows a lit flashlight from far away. Switch it off with a left click to slip past; walls break its sight. A light helps you search, but also tells it where you are.",
                "Light observation / A beam aimed directly at it drives it away. Keep the flashlight on and face it to force a retreat; looking away lets it hunt again. Walls still block the beam.",
                "Sound observation / It hunts hurried footsteps. WALK near it; sprint only to escape. A flashlight will not stop it. Break sight and move quietly to lose it."
            };
            result.Evidence = new[] {
                Expand(random.Pick(confession)),
                rules[(int)result.Temperament],
                Expand(random.Pick(ritualTemplates)),
                "Survey map / The source is marked at {source}. The final page reads: 'Once the three offerings are accepted, get back to the extraction radio. It will not stay quiet for long.'",
                "", ""
            };
            var memoryOrder = new List<string>(memories);
            random.Shuffle(memoryOrder);
            result.Evidence[4] = Expand(memoryOrder[0]);
            result.Evidence[5] = Expand(memoryOrder[1]);
            result.ChapterTwoLine = Expand("{witness}'s final transmission / You know what woke it now. Take the salt, bell and lantern to {source}. Follow the order in your journal. {priest} is waiting there; he was the first one to answer it. Bring the lost memories too, if you can.");
            var warnings = new List<string>(warningLines);
            if (priestWhispers != null) warnings.AddRange(priestWhispers);
            result.Warnings = warnings.ToArray();
            random.Shuffle(result.Warnings);

            // The priest's story is drawn after every original pick so existing seeds keep their layout.
            string priest = priests != null && priests.Length > 0 ? random.Pick(priests) : "the chapel priest";
            tokens["priest"] = priest;
            result.Priest = priest;
            result.ChapterTwoLine = result.ChapterTwoLine.Replace("{priest}", priest);
            result.LocationIndices = locations.GetRange(0, Mathf.Min(locationCount, 9 + CH_Case.DiaryPageCount)).ToArray();
            int pages = Mathf.Min(CH_Case.DiaryPageCount, result.LocationIndices.Length - 9, diaryPages?.Length ?? 0);
            result.Diary = new string[pages];
            for (int i = 0; i < pages; i++) result.Diary[i] = Expand(diaryPages[i]);
            result.EvidenceReplies = new string[4];
            for (int i = 0; i < 4; i++)
                result.EvidenceReplies[i] = evidenceReplies != null && i < evidenceReplies.Length ? Expand(evidenceReplies[i]) : "";
            result.StoryBeats = new string[4];
            for (int i = 0; i < 4; i++)
                result.StoryBeats[i] = storyBeats != null && i < storyBeats.Length ? Expand(storyBeats[i]) : "";
            result.Witness = witness;
            result.Missing = missing;
            return result;
        }
    }

    /// <summary>Pure case state: authored clues guarantee a solvable, seed-specific ritual.</summary>
    public sealed class CH_Case {
        public static readonly string[] Offerings = { "salt", "bell", "lantern" };
        /// <summary>Optional priest's diary pages, interactable as ids <see cref="DiaryFirstId"/>+.</summary>
        public const int DiaryPageCount = 3;
        public const int DiaryFirstId = 14;
        public int Seed, SourceIndex;
        public CH_Temperament Temperament;
        public string Title, Introduction, ChapterTwoLine, Priest, Witness, Missing;
        public string[] Evidence, Warnings;
        /// <summary>Diary page text; may be shorter than <see cref="DiaryPageCount"/> on small maps.</summary>
        public string[] Diary = new string[0];
        public string[] EvidenceReplies = new string[4], StoryBeats = new string[4];
        public int DiaryMask { get; private set; }
        public int DiaryCount => CountBits(DiaryMask);
        public bool DiaryComplete => Diary.Length > 0 && DiaryCount == Diary.Length;
        public bool DiaryCollected(int page) => (DiaryMask & (1 << page)) != 0;
        public bool CollectDiary(int page) {
            if (page < 0 || page >= Diary.Length || Phase is CH_Phase.Complete or CH_Phase.Failed || DiaryCollected(page)) return false;
            DiaryMask |= 1 << page;
            return true;
        }
        public int[] LocationIndices, Order;
        public CH_Phase Phase { get; private set; } = CH_Phase.Investigation;
        public int EvidenceMask { get; private set; }
        public int RelicMask { get; private set; }
        public int RitualStep { get; private set; }
        public int Mistakes { get; private set; }
        public int EvidenceCount => CountBits(EvidenceMask & 15);
        public int MemoryCount => CountBits(EvidenceMask & 48);
        public int RelicCount => CountBits(RelicMask);
        public bool Collected(int id) => id < 6 ? (EvidenceMask & (1 << id)) != 0 : (RelicMask & (1 << (id - 6))) != 0;
        public bool Collect(int id) {
            if (id < 0 || id > 8 || Phase is CH_Phase.Complete or CH_Phase.Failed || Collected(id)) return false;
            if (id < 6) EvidenceMask |= 1 << id;
            else {
                if (Phase != CH_Phase.Descent) return false;
                RelicMask |= 1 << (id - 6);
            }
            if (Phase == CH_Phase.Investigation && EvidenceCount == 4) Phase = CH_Phase.Descent;
            return true;
        }
        public bool Offer(int offering) {
            if (Phase != CH_Phase.Descent || RelicCount != 3 || offering < 0 || offering > 2) return false;
            if (offering != Order[RitualStep]) { RitualStep = 0; Mistakes++; return false; }
            RitualStep++;
            if (RitualStep == 3) Phase = CH_Phase.Escape;
            return true;
        }
        public bool Extract() {
            if (Phase != CH_Phase.Escape) return false;
            Phase = CH_Phase.Complete;
            return true;
        }
        public void Fail() { if (Phase != CH_Phase.Complete) Phase = CH_Phase.Failed; }
        public static int CountBits(int mask) { int count = 0; while (mask != 0) { count += mask & 1; mask >>= 1; } return count; }
    }
}
