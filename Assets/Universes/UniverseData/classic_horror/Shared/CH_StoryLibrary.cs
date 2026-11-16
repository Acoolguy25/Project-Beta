using System;
using System.Collections.Generic;
using RyanAssets.Core;
using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// Authored, composable story templates. Every case draws a witness, a missing person, a cause, a
    /// priest and one variant of each text, then expands the tokens <c>{witness}</c>, <c>{missing}</c>,
    /// <c>{Missing}</c> (capitalised), <c>{cause}</c>, <c>{priest}</c>, <c>{first}</c>, <c>{second}</c>,
    /// <c>{third}</c> and <c>{order}</c>. <c>{source}</c> is bound later by the server from the map
    /// (<see cref="CH_Case.BindSource"/>). Lines that start with "SPEAKER / " show the speaker on the
    /// radio card and as the journal entry's heading (<see cref="CH_StoryText"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Universes/Classic Horror/Story Library")]
    public sealed class CH_StoryLibrary : ScriptableObject {
        [Header("Cast")]
        public string[] witnesses = {
            "Mara Vale", "Elias Finch", "Ada Wren", "Thomas Reed", "Iris Bell", "Jonah Pike", "Nell Ashdown", "Owen Marsh"
        };
        public string[] missingPeople = {
            "the ferryman's daughter", "a night watchman", "a visiting surveyor", "the last schoolteacher",
            "a railway porter", "the chapel keeper", "the lock-keeper's son", "a district nurse"
        };
        public string[] causes = {
            "the council drained a sealed grave to make room for the flood pumps",
            "a salvage crew brought a bell up from a house that predates the village",
            "the chapel keeper answered a voice beneath the floorboards",
            "the night watch played a waterlogged recording on the emergency radio",
            "a surveyor removed three seals from the old sluice",
            "the children dared each other to read the drowned names aloud at the jetty",
            "someone rang the chapel bell after midnight, the one night it was forbidden"
        };
        [Tooltip("Case title prefix for each temperament: light seeker, light shy, listener.")]
        public string[] caseTitles = { "The Lantern Beneath", "A Face in Still Water", "The Listening Flood" };

        [Header("Chapter I")]
        [TextArea] public string[] dispatch = {
            "DISPATCH / {witness} reported {missing} missing. Their last call came from this settlement. Find four records. Learn what is hunting here before you approach its source.",
            "DISPATCH / The water rose without rain. {witness} says {missing} was still inside. Four surviving records may tell us why. Keep your light close.",
            "DISPATCH / We received a distress call in your voice, timestamped tomorrow. {witness} was the only witness. Trace four records before you touch anything that answers back.",
            "DISPATCH / Nobody has answered the settlement's phone in nine days, but it still calls us every night at 3:12. {witness} asked for help finding {missing}. Four records, then come home.",
            "DISPATCH / The tide charts say this place should be dry. It is not. {witness} last saw {missing} walking into the water with their eyes closed. Find the records. Do not follow the singing."
        };
        [TextArea] public string[] confession = {
            "{witness}'s confession / We told ourselves it was an accident. It began when {cause}. {missing} tried to warn us. We heard knocking long after the water covered the door.",
            "Interview transcript / {witness}: It was not the flood. It was what we did before it. {cause}. After that, {missing} stopped casting a reflection.",
            "Recovered letter / My name is {witness}. If you find this, remember that {cause}. We left {missing} behind. The thing in the water learned how to wait.",
            "Parish register, margin note / The week {cause}, every candle in the chapel burned blue. {missing} wrote down our names in this book so we would not forget them. Someone has crossed them all out.",
            "Answering machine tape / {witness}, crying: \"I know why it came. {cause}. I told {missing} to stay home. Why did they go down there? Why did I let them?\""
        };
        [TextArea] public string[] ritualTemplates = {
            "Keeper's instructions / Three offerings, in this order: {order}. The wrong order wakes it. Recover the salt, bell and lantern before returning to the source.",
            "Scorched liturgy / Start with {first}. Follow with {second}. End with {third}. Carry all three offerings. Do not trust yesterday's instructions.",
            "Wax-cylinder transcript / {order}. Say it until you remember. The keeper changed the order after each flood. All three objects must reach the source.",
            "Chalk on a cellar door / FIRST THE {first}. THEN THE {second}. LAST THE {third}. Underneath, smaller, in a child's hand: it counts. it always counts."
        };
        [TextArea] public string[] memories = {
            "A child's drawing / {missing} stands on the bank with {witness}. On the reverse: 'Please bring us both home.' You preserve the drawing.",
            "Unsent postcard / {witness} promised to return before the water froze. The postmark is tomorrow. You preserve the name written beneath it.",
            "Memorial photograph / Every face has been rubbed away except {witness}'s. Someone has written 'We were people before we were a story.' You preserve the photograph.",
            "A recorded lullaby / Beneath the static, {witness} says the name of {missing}. For a moment the knocking falls silent. You preserve the recording.",
            "A wedding ring on a bootlace / Engraved inside: 'For {missing}, from the shore.' It is warm, as if someone was holding it a moment ago. You preserve the ring.",
            "A school register / Every child is marked absent except one: {missing}, present, every day, in handwriting that gets shakier by the week. You preserve the page."
        };
        [Tooltip("Radio replies after each required record, indexed by evidence 0-3.")]
        [TextArea] public string[] evidenceReplies = {
            "{witness} / So now you know. I kept that secret for years. Please... keep looking. {Missing} deserves better than me.",
            "{witness} / That is how it hunted us. Every night the same. Do not let it learn your habits too.",
            "{witness} / The keeper wrote those instructions with shaking hands. Get the order right. It does not forgive mistakes.",
            "{witness} / That map. The chapel never had a basement, but the priest dug one anyway. Whatever answered him is down there."
        };
        [TextArea] public string[] warningLines = {
            "RADIO / Those footsteps stopped when you stopped. Keep moving between cover.",
            "RADIO / Something is using the water to listen. Break its line of sight.",
            "RADIO / That is not another investigator. Remember the behavior in your notes.",
            "RADIO / The lights are failing ahead of you. Do not let it close the distance.",
            "DISPATCH / Your heart monitors just spiked. All of you. What are you looking at?",
            "RADIO / A wet hand on the antenna. The signal bends towards you. Move."
        };

        [Header("The priest")]
        public string[] priests = { "Father Aldous Crane", "Father Silas Merrow", "Father Edmund Vey", "Father Ambrose Hale" };
        [Tooltip("The three diary pages, read in any order but always told in this order.")]
        [TextArea] public string[] diaryPages = {
            "{priest}'s diary, page one / The river came up the chapel steps tonight. Something beneath the water spoke to me in my mother's voice. It offered to hold the flood back. It only wanted names: the names nobody would miss.",
            "{priest}'s diary, page two / I gave it the drowned first, then the strangers, then the poor. The water stayed low. I told the village it was prayer. When {cause}, the bargain broke, and it asked for {missing}.",
            "{priest}'s diary, final page / I tried to undo it and walked into the water with the bell. I am still walking. If anyone reads this, say my name at the source. {priest}. Let it be the last name I give."
        };
        [TextArea] public string[] priestWhispers = {
            "RADIO / A sermon in reverse, coming through the static. It is getting closer.",
            "RADIO / \"Every name is an offering,\" the voice says. It knows yours now.",
            "RADIO / Footsteps in water, slow and patient, right behind the signal.",
            "RADIO / \"I kept them safe,\" the voice insists. \"I kept them all safe. Come and see.\"",
            "RADIO / Somebody is reading your names off a list. Slowly. Checking each one."
        };

        [Header("Radio story beats")]
        [Tooltip("Index 0: early static, 1: halfway, 2: Chapter II, 3: escape.")]
        [TextArea] public string[] storyBeats = {
            "RADIO / ...static... a voice that is not dispatch: \"{witness}? Is that you? The water is warm down here.\"",
            "RADIO / The chapel bell rang once. No one is near it. Under the static, {missing} is whispering your names, one for every record you found.",
            "{priest} / You have seen what this place gave away. Come down to {source}, and give me one more.",
            "{Missing} / Don't leave me in the water again. Please. I can hear you running."
        };
        [TextArea] public string[] chapterTwoLines = {
            "{witness}'s final transmission / You know what woke it now. Take the salt, bell and lantern to {source}. Follow the order in your journal. {priest} is waiting there; he was the first one to answer it. Bring the lost memories too, if you can.",
            "{witness} / That's all of it. The salt, the bell, the lantern: find them, take them to {source}, and give them back in the keeper's order. {priest} will try to talk to you. Don't answer him.",
            "DISPATCH / Records received. {witness} is off the line; we think they went back in after you. Recover the three offerings and seal {source} before {priest} finishes what he started. The order is in your journal. Do not improvise."
        };
        [Tooltip("Radio line when each offering is accepted, indexed by offering: salt, bell, lantern.")]
        [TextArea] public string[] offeringLines = {
            "The salt hisses where it touches the water. Something underneath pulls back.",
            "The bell rings once without being struck. Every window in the settlement answers it.",
            "The lantern flame leans towards the source, then stands perfectly still."
        };
        [TextArea] public string[] mistakeLines = {
            "{priest} / Wrong. You were never listening, were you? Begin again.",
            "RADIO / The seal breaks with a sound like a held breath let go. The keeper's order. Read it again.",
            "{Missing} / No, no, not that one first! It's awake. It's awake!"
        };
        [Tooltip("Radio reaction after an ambient scare, indexed by CH_AmbientScare: door slam, distant cry, lights out, chapel bell.")]
        [TextArea] public string[] ambientLines = {
            "RADIO / Was that a door? Nobody else should be out there.",
            "{witness} / That's {missing}. That's their voice. Please tell me you heard it too.",
            "DISPATCH / We lost your lights on the feed for three seconds. Report. Are you all still there?",
            "RADIO / The chapel bell again. Someone is counting."
        };

        [Header("Endings")]
        [Tooltip("Endings: title, a line break, then the epilogue.")]
        [TextArea] public string lastRitesEnding =
            "LAST RITES\nYou brought {missing} home and spoke {priest}'s name at the source. The water gives back every name it took. At dawn the chapel bell rings once, and never again.";
        [TextArea] public string waterRemembersEnding =
            "THE WATER REMEMBERS\n{Missing} is remembered. At dawn, the settlement's windows reflect the sky again. Somewhere below, {priest} is still walking.";
        [TextArea] public string nameForNameEnding =
            "A NAME FOR A NAME\n{priest} finally rests. But the memories of {missing} stayed under the water, and {witness} still leaves the porch light on.";
        [TextArea] public string borrowedSilenceEnding =
            "BORROWED SILENCE\nYou escaped and sealed the source. But the names you left behind still whisper beneath the water, and it will be hungry again.";
        [TextArea] public string stillMissingEnding =
            "STILL MISSING\nThe radio repeats your last call. {witness} waits on the jetty until dark. The settlement keeps another name.";

        static readonly string[] TemperamentRules = {
            "Light observation / It follows a lit flashlight from far away. Switch it off with a left click to slip past; walls break its sight. A light helps you search, but also tells it where you are.",
            "Light observation / A beam aimed directly at it drives it away. Keep the flashlight on and face it to force a retreat; looking away lets it hunt again. Walls still block the beam.",
            "Sound observation / It hunts hurried footsteps. WALK near it; sprint only to escape. A flashlight will not stop it. Break sight and move quietly to lose it."
        };
        const string SurveyMap = "Survey map / The source is marked at {source}. The final page reads: 'Once the three offerings are accepted, get back to the extraction radio. It will not stay quiet for long.'";

        public CH_Case Generate(int seed, int locationCount, int sourceCount) {
            if (locationCount < 9 || sourceCount < 1) throw new ArgumentException("A case needs nine search locations and a source.");
            var random = new SeededStoryRandom(seed);
            // Layout draws come first and consume a fixed amount of randomness, so editing or
            // extending the text arrays never moves a seed's evidence, source or ritual order.
            var result = new CH_Case { Seed = seed, Temperament = (CH_Temperament)random.Next(3), SourceIndex = random.Next(sourceCount) };
            string witness = random.Pick(witnesses), missing = random.Pick(missingPeople), cause = random.Pick(causes);
            result.Order = new[] { 0, 1, 2 };
            random.Shuffle(result.Order);
            var locations = new List<int>();
            for (int i = 0; i < locationCount; i++) locations.Add(i);
            random.Shuffle(locations);
            result.LocationIndices = locations.GetRange(0, Mathf.Min(locationCount, CH_Case.RequiredLocations + CH_Case.DiaryPageCount)).ToArray();

            string priest = priests != null && priests.Length > 0 ? random.Pick(priests) : "the chapel priest";
            var tokens = new Dictionary<string, string> {
                ["witness"] = witness, ["missing"] = missing, ["Missing"] = CH_StoryText.Capitalize(missing), ["cause"] = cause,
                ["priest"] = priest,
                ["first"] = CH_Case.Offerings[result.Order[0]], ["second"] = CH_Case.Offerings[result.Order[1]],
                ["third"] = CH_Case.Offerings[result.Order[2]],
                ["order"] = string.Join(" -> ", Array.ConvertAll(result.Order, n => CH_Case.Offerings[n]))
            };
            string Expand(string template) => string.IsNullOrEmpty(template) ? string.Empty : SeededStoryRandom.Expand(template, tokens);
            string PickExpanded(string[] options) => options != null && options.Length > 0 ? Expand(random.Pick(options)) : string.Empty;
            string[] ExpandAll(string[] templates, int count) {
                var lines = new string[count];
                for (int i = 0; i < count; i++) lines[i] = templates != null && i < templates.Length ? Expand(templates[i]) : string.Empty;
                return lines;
            }

            result.Witness = witness;
            result.Missing = missing;
            result.Priest = priest;
            int temperament = (int)result.Temperament;
            string titlePrefix = caseTitles != null && temperament < caseTitles.Length ? caseTitles[temperament] : "The Last Call";
            result.Title = titlePrefix + " / " + witness;
            result.Introduction = PickExpanded(dispatch);

            var memoryOrder = new List<string>(memories);
            random.Shuffle(memoryOrder);
            result.Evidence = new[] {
                PickExpanded(confession),
                TemperamentRules[temperament],
                PickExpanded(ritualTemplates),
                SurveyMap,
                memoryOrder.Count > 0 ? Expand(memoryOrder[0]) : string.Empty,
                memoryOrder.Count > 1 ? Expand(memoryOrder[1]) : string.Empty
            };
            result.ChapterTwoLine = PickExpanded(chapterTwoLines);

            var warnings = new List<string>(warningLines);
            if (priestWhispers != null) warnings.AddRange(priestWhispers);
            result.Warnings = warnings.ConvertAll(Expand).ToArray();
            random.Shuffle(result.Warnings);

            int pages = Mathf.Min(CH_Case.DiaryPageCount, result.LocationIndices.Length - CH_Case.RequiredLocations, diaryPages?.Length ?? 0);
            result.Diary = ExpandAll(diaryPages, pages);
            result.EvidenceReplies = ExpandAll(evidenceReplies, CH_Case.RequiredEvidence);
            result.StoryBeats = ExpandAll(storyBeats, CH_Case.StoryBeatCount);
            result.OfferingLines = ExpandAll(offeringLines, CH_Case.Offerings.Length);
            result.MistakeLines = ExpandAll(mistakeLines, mistakeLines?.Length ?? 0);
            result.AmbientLines = ExpandAll(ambientLines, CH_Case.AmbientScareCount);
            result.Endings = new[] {
                Expand(lastRitesEnding), Expand(waterRemembersEnding), Expand(nameForNameEnding),
                Expand(borrowedSilenceEnding), Expand(stillMissingEnding)
            };
            return result;
        }
    }
}
