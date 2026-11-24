using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// The night at Hollowmere, in order. Prologue, Taking, ChapelOpens and Departure are cutscenes: the server holds
    /// the Presence and protects everyone while every client plays the same scene.
    /// </summary>
    public enum CH_Phase : byte {
        Waiting = 0,
        /// <summary>Cutscene: the boat docks, something smashes the engine, the priest welcomes you.</summary>
        Prologue = 1,
        /// <summary>Act I: find fuel, a toolbox and a lantern and bring them to the boat.</summary>
        Stranded = 2,
        /// <summary>Cutscene: the engine starts, the priest rises from the water and drags Mara away.</summary>
        Taking = 3,
        /// <summary>Act II: lay three of the drowned choir's belongings on their graves.</summary>
        Choir = 4,
        /// <summary>Cutscene: the chapel doors open.</summary>
        ChapelOpens = 5,
        /// <summary>Act III: take the priest's bell from the altar and carry it aboard.</summary>
        Sermon = 6,
        /// <summary>The bell is aboard: everyone still ashore has a few seconds to reach the boat.</summary>
        Boarding = 7,
        /// <summary>Cutscene: the boat leaves Hollowmere.</summary>
        Departure = 8,
        Complete = 9,
        Failed = 10,
        /// <summary>Cutscene: the short prologue for a crew who have all been to Hollowmere before.</summary>
        Arrival = 11
    }

    /// <summary>Everything an investigator can pick up. Items never vanish: they rest, are carried, or are placed.</summary>
    public enum CH_ItemKind : byte { Fuel = 0, Toolbox = 1, Lantern = 2, Hymnal = 3, Photograph = 4, Candle = 5, MusicBox = 6, Bell = 7 }

    public enum CH_ItemState : byte {
        /// <summary>Not part of the night yet (a later act's item).</summary>
        Hidden = 0,
        /// <summary>Lying in the world where it was found or dropped.</summary>
        Resting = 1,
        /// <summary>In an investigator's hands.</summary>
        Carried = 2,
        /// <summary>Set down where it belongs (on the boat, on a grave); it stays there.</summary>
        Placed = 3
    }

    /// <summary>Where an item belongs.</summary>
    public enum CH_SlotKind : byte { BoatFuel = 0, BoatToolbox = 1, BoatLantern = 2, Grave = 3, LilyMarker = 4, BoatBell = 5 }

    /// <summary>Scares shown to one investigator (<see cref="CH_ScareBroadcast.kind"/>).</summary>
    public enum CH_ScareKind : byte {
        /// <summary>Face to face with the hunting priest: his face fills the screen.</summary>
        Warning = 0,
        /// <summary>He caught you.</summary>
        Fatal = 2,
        /// <summary>A whisper, and he is standing right behind you when you turn round.</summary>
        Apparition = 3,
        /// <summary>He stands far ahead in the dark, watching, and is gone when the light finds him.</summary>
        Watcher = 4,
        /// <summary>The bell is lifted from the altar: the lights die and he is in your face.</summary>
        BellGrab = 5,
        /// <summary>He tore your hiding place open: his face, then a heartbeat to run before he strikes.</summary>
        DraggedOut = 6
    }

    /// <summary>Atmospheric events the server schedules for everyone (<see cref="CH_AmbientScareBroadcast"/>).</summary>
    public enum CH_AmbientScare : byte { DoorSlam = 0, DistantCry = 1, LightsOut = 2, ChapelBell = 3 }

    /// <summary>How a voice line is heard.</summary>
    public enum CH_VoiceMode : byte {
        /// <summary>In your ear: Mara's radio, Lily's whisper, narration in a cutscene.</summary>
        Ear = 0,
        /// <summary>From the priest himself, wherever he is.</summary>
        Monster = 1,
        /// <summary>From a fixed place in the world (a freed singer at their grave).</summary>
        World = 2
    }

    /// <summary>What an interaction targets. <see cref="Drop"/> sets down whatever the investigator holds (its id is unused).</summary>
    public enum CH_Target : byte { Item = 0, Slot = 1, Hide = 2, Drop = 3 }

    public struct CH_InteractRequest : IBroadcast {
        public int seed;
        public CH_Target target;
        public int id;
    }

    public struct CH_InteractionResult : IBroadcast {
        public int seed;
        public bool accepted;
        public CH_Target target;
        public string message;
    }

    public struct CH_StateRequest : IBroadcast { public byte version; }

    public struct CH_ItemView {
        public int id;
        public CH_ItemKind kind;
        public CH_ItemState state;
        public Vector3 position;
        public float yaw;
        /// <summary>The carrying investigator's character (network object id), or -1.</summary>
        public int carrierId;
        /// <summary>Whether the HUD should point at it now.</summary>
        public bool objective;
        /// <summary>The drowned singer a relic belonged to (<see cref="CH_Story.Singers"/>), or -1.</summary>
        public sbyte singer;
        public string title;
    }

    public struct CH_SlotView {
        public int id;
        public CH_SlotKind kind;
        public CH_ItemKind accepts;
        public Vector3 position;
        public float yaw;
        public bool filled;
        /// <summary>Accepting its item right now.</summary>
        public bool open;
        /// <summary>For a grave, whose it is (<see cref="CH_Story.Singers"/>); otherwise -1.</summary>
        public sbyte singer;
        public string title;
    }

    public struct CH_HideView {
        public int id;
        /// <summary>The hidden investigator's character (network object id), or -1.</summary>
        public int occupantId;
    }

    public struct CH_StateBroadcast : IBroadcast {
        public int seed;
        public CH_Phase phase;
        /// <summary>Seconds since the phase began, so late joiners pick a cutscene up where it is.</summary>
        public float phaseSeconds;
        public string objective;
        public string detail;
        public CH_ItemView[] items;
        public CH_SlotView[] slots;
        public CH_HideView[] hides;
        public int progress, progressTotal;
        /// <summary>Seconds left on the boarding call, or 0.</summary>
        public int countdown;
        public int monsterId;
        public int livesPerPlayer;
        public int livesLost;
        public int nightSeconds;
        /// <summary>Which ending played (<see cref="CH_Story"/> Ending* constants), once the night is over.</summary>
        public byte ending;
        public int escapes;
        /// <summary>Seconds until the next night begins, on the ending card.</summary>
        public int nextNightSeconds;
    }

    /// <summary>A spoken line from <see cref="CH_VoiceBank"/>.</summary>
    public struct CH_VoiceBroadcast : IBroadcast {
        public int seed;
        public string line;
        public CH_VoiceMode mode;
        public Vector3 position;
    }

    public struct CH_ScareBroadcast : IBroadcast {
        public int seed, sequence;
        public CH_ScareKind kind;
    }

    /// <summary>A scripted scare everyone hears: a door slamming, a cry, the lights failing, the chapel bell.</summary>
    public struct CH_AmbientScareBroadcast : IBroadcast {
        public int seed;
        public CH_AmbientScare kind;
        public Vector3 position;
        public float seconds;
    }

    /// <summary>One investigator's own record across nights, sent to them alone when they join and when a night ends.</summary>
    public struct CH_ProgressBroadcast : IBroadcast {
        public int nightsPlayed, escapes, trueEndings;
        /// <summary>Bit per ending (<see cref="CH_Story"/> Ending* index) they have seen.</summary>
        public int endingsSeen;
        /// <summary>Bit per drowned singer (<see cref="CH_Story.Singers"/>) they have laid to rest.</summary>
        public int singersMet;
        /// <summary>Quickest escape in seconds, or 0.</summary>
        public int bestSeconds;
    }
}
