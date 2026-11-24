namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// The story of The Drowned Choir, kept in one place: who the drowned are, what each item is called, what the
    /// objective says in each act, which voice line answers which moment, and the endings. Voice line ids match
    /// <c>Audio/Voice/Source~/lines.tsv</c> and <see cref="CH_VoiceBank"/>.
    /// </summary>
    /// <remarks>
    /// In the autumn of 1962 the Hollowmere dam failed. Father Silas Crane told his flock that only the chapel would
    /// stay dry, barred the doors with them inside, and rang his bell while the water rose: his choir. His daughter Lily
    /// slipped out through the vestry window and ran for the ferry; she drowned on the jetty steps. Crane drowned in the
    /// tower, still ringing. Every year on that night he hunts for new voices.
    /// </remarks>
    public static class CH_Story {
        public const string Title = "The Drowned Choir";

        /// <summary>The five drowned singers whose belongings can turn up; three are freed each night.</summary>
        public static readonly string[] Singers = { "Agnes Pell", "Thomas Hale", "Ruth Ellery", "Elias Grey", "Margaret Lowe" };
        /// <summary>Voice line keys per singer, in <see cref="Singers"/> order (relic_*, freed_*).</summary>
        public static readonly string[] SingerKeys = { "agnes", "thomas", "ruth", "elias", "margaret" };
        public const int SingersPerNight = 3;
        public const int SingerCount = 5;

        /// <summary>Item names, in <see cref="CH_ItemKind"/> order.</summary>
        public static readonly string[] ItemNames = { "Fuel can", "Ferryman's toolbox", "Ferry lantern", "Hymnal", "Photograph", "Vigil candle", "Lily's music box", "Crane's bell" };
        /// <summary>Each item as it reads mid-sentence ("Take ..."): an article where it needs one, names kept capitalised.</summary>
        public static readonly string[] ItemsInSentence = { "the fuel can", "the ferryman's toolbox", "the ferry lantern", "the hymnal", "the photograph", "the vigil candle", "Lily's music box", "Crane's bell" };

        /// <summary>The three boat parts, in the order the boat's slots are authored.</summary>
        public static readonly CH_ItemKind[] BoatParts = { CH_ItemKind.Fuel, CH_ItemKind.Toolbox, CH_ItemKind.Lantern };
        /// <summary>The three relics a night hands out; each belongs to one of that night's singers.</summary>
        public static readonly CH_ItemKind[] Relics = { CH_ItemKind.Hymnal, CH_ItemKind.Photograph, CH_ItemKind.Candle };

        /// <summary>Indices into <see cref="Endings"/>.</summary>
        public const byte EndingNone = 0, EndingTrue = 1, EndingEscaped = 2, EndingDrowned = 3;
        public const int EndingCount = 4;

        /// <summary>Ending card text: title, then the epilogue.</summary>
        public static readonly string[] Endings = {
            "",
            "SHE CAME HOME\nYou carried Mara back across the black water, and Lily's song went with you. When the sun came up over the reservoir the chapel bell was silent for the first time in sixty years.",
            "YOU ESCAPED\nThe boat cut through the fog with Mara shaking at the wheel. Behind you the bell started ringing again. Somewhere in Hollowmere a little girl is still waiting on the jetty steps.",
            "THE CHOIR SINGS ON\nNobody left Hollowmere. On still nights fishermen on the reservoir hear new voices in the chapel, singing slightly out of tune."
        };

        public static string ItemName(CH_ItemKind kind) => ItemNames[(int)kind];

        public static string ItemInSentence(CH_ItemKind kind) => ItemsInSentence[(int)kind];

        public static string SingerName(int singer) => singer >= 0 && singer < Singers.Length ? Singers[singer] : "the drowned";

        /// <summary>The voice line that plays when an item is first picked up, or null.</summary>
        public static string PickUpLine(CH_ItemKind kind, int singer) => kind switch {
            CH_ItemKind.Fuel => "pick_fuel",
            CH_ItemKind.Toolbox => "pick_toolbox",
            CH_ItemKind.Lantern => "pick_lantern",
            CH_ItemKind.MusicBox => "musicbox_pick",
            CH_ItemKind.Bell => "bell_take",
            _ when singer >= 0 && singer < SingerKeys.Length => "relic_" + SingerKeys[singer],
            _ => null
        };

        /// <summary>The voice line that plays when an item is set where it belongs, or null.</summary>
        public static string PlaceLine(CH_ItemKind kind, int singer) => kind switch {
            CH_ItemKind.Fuel => "place_fuel",
            CH_ItemKind.Toolbox => "place_toolbox",
            CH_ItemKind.Lantern => "place_lantern",
            CH_ItemKind.MusicBox => "musicbox_place",
            CH_ItemKind.Bell => "bell_aboard",
            _ when singer >= 0 && singer < SingerKeys.Length => "freed_" + SingerKeys[singer],
            _ => null
        };

        public static string ActName(CH_Phase phase) => phase switch {
            CH_Phase.Prologue or CH_Phase.Arrival => "Prologue",
            CH_Phase.Stranded => "Act I  ·  Stranded",
            CH_Phase.Taking => "Act I  ·  Stranded",
            CH_Phase.Choir => "Act II  ·  The choir",
            CH_Phase.ChapelOpens => "Act II  ·  The choir",
            CH_Phase.Sermon => "Act III  ·  The last sermon",
            CH_Phase.Boarding => "Act III  ·  The last sermon",
            CH_Phase.Departure => "Epilogue",
            CH_Phase.Complete => "Night over",
            CH_Phase.Failed => "Night over",
            _ => CH_Story.Title
        };

        /// <summary>The big chapter card when an act begins, or null.</summary>
        public static string ActCard(CH_Phase phase) => phase switch {
            CH_Phase.Stranded => "ACT I\nSTRANDED",
            CH_Phase.Choir => "ACT II\nTHE CHOIR",
            CH_Phase.Sermon => "ACT III\nTHE LAST SERMON",
            _ => null
        };

        /// <summary>The cutscene phases, in the order a night can play them.</summary>
        public static readonly CH_Phase[] Cutscenes = { CH_Phase.Prologue, CH_Phase.Arrival, CH_Phase.Taking, CH_Phase.ChapelOpens, CH_Phase.Departure };

        /// <summary>How long (seconds) each cutscene phase holds the night.</summary>
        public static float CutsceneSeconds(CH_Phase phase) => phase switch {
            CH_Phase.Prologue => 37f,
            CH_Phase.Arrival => 18.6f,
            CH_Phase.Taking => 29f,
            CH_Phase.ChapelOpens => 10f,
            CH_Phase.Departure => 16f,
            _ => 0f
        };

        public static bool IsCutscene(CH_Phase phase) => CutsceneSeconds(phase) > 0f;

        /// <summary>The investigators can act: an act is under way.</summary>
        public static bool IsPlayable(CH_Phase phase) => phase is CH_Phase.Stranded or CH_Phase.Choir or CH_Phase.Sermon or CH_Phase.Boarding;

        /// <summary>Line cues for each cutscene: (seconds into the scene, voice line id). Spaced so no two lines overlap (CH_StoryTests checks against the clip lengths).</summary>
        public static (float at, string line)[] CutsceneLines(CH_Phase phase) => phase switch {
            CH_Phase.Prologue => new[] { (1.5f, "prologue_1"), (8.2f, "prologue_2"), (15.4f, "prologue_3"), (21.6f, "prologue_4"), (27.8f, "prologue_5") },
            // Back again: the crash, his face on the lake, and Mara's orders.
            CH_Phase.Arrival => new[] { (0.4f, "prologue_3"), (9.6f, "prologue_5") },
            CH_Phase.Taking => new[] { (0.6f, "taking_1"), (6.0f, "taking_2"), (11.2f, "taking_3"), (15.0f, "taking_4"), (21.0f, "taking_5") },
            CH_Phase.ChapelOpens => new[] { (2.2f, "chapel_open") },
            CH_Phase.Departure => new[] { (0.8f, "escape_1"), (3.6f, "escape_2") },
            _ => new (float, string)[0]
        };

        /// <summary>Taunts the priest speaks while hunting, in rotation.</summary>
        public static readonly string[] Taunts = { "taunt_1", "taunt_2", "taunt_3", "taunt_4", "taunt_5", "taunt_6", "taunt_7" };
        public static readonly string[] LostLines = { "lost_1", "lost_2" };
        public static readonly string[] CatchLines = { "catch_1", "catch_2" };
        public static readonly string[] Whispers = { "whisper_behind", "whisper_dont", "whisper_sees", "whisper_join" };
    }
}
