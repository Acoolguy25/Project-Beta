namespace Universes.UniverseData.classic_horror {
    /// <summary>Pure case state: authored clues guarantee a solvable, seed-specific ritual.</summary>
    public sealed class CH_Case {
        public static readonly string[] Offerings = { "salt", "bell", "lantern" }; // OfferingCount entries
        /// <summary>Search locations a case needs for its evidence (0-3), memories (4-5) and offerings (6-8).</summary>
        public const int RequiredLocations = 9;
        /// <summary>Records that close Chapter I.</summary>
        public const int RequiredEvidence = 4;
        /// <summary>Optional memories, evidence ids 4 and 5.</summary>
        public const int OptionalMemories = 2;
        /// <summary>Optional priest's diary pages, interactable as ids <see cref="DiaryFirstId"/>+.</summary>
        public const int DiaryPageCount = 3;
        public const int DiaryFirstId = 14;
        /// <summary>Evidence and memory records: ids 0 to EvidenceRecordCount - 1; the offerings' search spots follow.</summary>
        public const int EvidenceRecordCount = RequiredEvidence + OptionalMemories;
        /// <summary>Interaction ids of the source, the extraction radio and the first offering placed at the source.</summary>
        public const int SourceId = 9, ExtractionId = 10, OfferingFirstId = 11;
        public const int OfferingCount = 3;
        public static bool IsOfferingId(int id) => id >= OfferingFirstId && id < OfferingFirstId + OfferingCount;
        public const int StoryBeatCount = 4;
        public const int AmbientScareCount = 4;
        /// <summary>Indices into <see cref="Endings"/>.</summary>
        public const int EndingLastRites = 0, EndingWaterRemembers = 1, EndingNameForName = 2, EndingBorrowedSilence = 3, EndingStillMissing = 4;

        public int Seed, SourceIndex;
        public CH_Temperament Temperament;
        public string Title, Introduction, ChapterTwoLine, Priest, Witness, Missing;
        public string[] Evidence, Warnings;
        /// <summary>Diary page text; may be shorter than <see cref="DiaryPageCount"/> on small maps.</summary>
        public string[] Diary = new string[0];
        public string[] EvidenceReplies = new string[RequiredEvidence], StoryBeats = new string[StoryBeatCount];
        public string[] OfferingLines = new string[OfferingCount], MistakeLines = new string[0], AmbientLines = new string[AmbientScareCount];
        /// <summary>Ending texts ("TITLE\nepilogue"), indexed by the Ending* constants.</summary>
        public string[] Endings = new string[5];
        public int[] LocationIndices, Order;

        public CH_Phase Phase { get; private set; } = CH_Phase.Investigation;
        public int EvidenceMask { get; private set; }
        public int RelicMask { get; private set; }
        public int RitualStep { get; private set; }
        public int Mistakes { get; private set; }
        public int DiaryMask { get; private set; }
        public int EvidenceCount => CountBits(EvidenceMask & 15);
        public int MemoryCount => CountBits(EvidenceMask & 48);
        public int RelicCount => CountBits(RelicMask);
        public int DiaryCount => CountBits(DiaryMask);
        public bool DiaryComplete => Diary.Length > 0 && DiaryCount == Diary.Length;
        public bool AllMemories => MemoryCount == OptionalMemories;

        public bool Collected(int id) => id < 6 ? (EvidenceMask & (1 << id)) != 0 : (RelicMask & (1 << (id - 6))) != 0;
        public bool DiaryCollected(int page) => (DiaryMask & (1 << page)) != 0;

        public bool Collect(int id) {
            if (id < 0 || id > 8 || IsOver || Collected(id)) return false;
            if (id < 6) EvidenceMask |= 1 << id;
            else {
                if (Phase != CH_Phase.Descent) return false;
                RelicMask |= 1 << (id - 6);
            }
            if (Phase == CH_Phase.Investigation && EvidenceCount == RequiredEvidence) Phase = CH_Phase.Descent;
            return true;
        }

        public bool CollectDiary(int page) {
            if (page < 0 || page >= Diary.Length || IsOver || DiaryCollected(page)) return false;
            DiaryMask |= 1 << page;
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

        bool IsOver => Phase is CH_Phase.Complete or CH_Phase.Failed;

        /// <summary>The ending this case has earned so far (an Ending* index): memories and the whole diary pick the epilogue.</summary>
        public int EndingIndex() {
            if (Phase == CH_Phase.Failed) return EndingStillMissing;
            if (AllMemories && DiaryComplete) return EndingLastRites;
            if (AllMemories) return EndingWaterRemembers;
            if (DiaryComplete) return EndingNameForName;
            return EndingBorrowedSilence;
        }

        /// <summary>The earned ending's text, "TITLE\nepilogue".</summary>
        public string Ending() => Endings[EndingIndex()];

        /// <summary>Replaces <c>{source}</c> in every case text with the map's name for the haunting's source.</summary>
        public void BindSource(string sourceName) {
            Introduction = Bind(Introduction, sourceName);
            ChapterTwoLine = Bind(ChapterTwoLine, sourceName);
            BindAll(Evidence, sourceName);
            BindAll(Warnings, sourceName);
            BindAll(Diary, sourceName);
            BindAll(EvidenceReplies, sourceName);
            BindAll(StoryBeats, sourceName);
            BindAll(OfferingLines, sourceName);
            BindAll(MistakeLines, sourceName);
            BindAll(AmbientLines, sourceName);
            BindAll(Endings, sourceName);
        }

        static string Bind(string text, string sourceName) => string.IsNullOrEmpty(text) ? text : text.Replace("{source}", sourceName);
        static void BindAll(string[] texts, string sourceName) {
            if (texts == null) return;
            for (int i = 0; i < texts.Length; i++) texts[i] = Bind(texts[i], sourceName);
        }

        public static int CountBits(int mask) { int count = 0; while (mask != 0) { count += mask & 1; mask >>= 1; } return count; }
    }
}
