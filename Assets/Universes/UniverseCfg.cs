using UnityEngine;
using UnityEngine.Assertions;

namespace Universes {
    public enum UniverseAccess : sbyte {
        Private = 0,
        Protected = 1,
        Public = 2
    }
    public struct UniverseStruct {
        public string id;
        public string title;
        public string description;
        public string creator_playerid;
        public UniverseAccess access;
        public string GetResourcePath(string localPath) {
            return $"UniverseDataPub/{id}/{localPath}";
        }
        public Sprite LoadSprite() {
            Sprite thumbnail = Resources.Load<Sprite>(GetResourcePath("thumbnail"));
            Assert.IsNotNull(thumbnail, $"{id} Thumnail Not Found!");
            return thumbnail;
        }
        public string LoadText(string localPath) {
            TextAsset textAsset = Resources.Load<TextAsset>(GetResourcePath(localPath));
            Assert.IsNotNull(textAsset, $"{id} TextAsset Not Found: {localPath}");
            return textAsset.text;
        }
    };
    public static class UniverseCfg {
        public readonly static UniverseStruct[] ActiveUniverses = {
            new(){
                id = "empty_baseplate",
                title = "Empty Baseplate",
                description = "This is your very first creation. Check it out, then make it your own with Ryan's help!",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Public
            },
            new(){
                id = "murder_mystery",
                title = "Murder In Plain Sight",
                description = "As the murderer, kill everyone else to win. As a sheriff, fend off the murderer. As an survivor, try to survive to win",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Public
            },
            new(){
                id = "dot_invaders",
                title = "Dot Invaders",
                description = "Grow your bases, send dot armies to neighboring bases, and conquer the board.",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Public
            },
            new(){
                id = "war_valley",
                title = "War Valley",
                description = "A strategic battle for control of the territory.",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Protected
            }
        };
        public static UniverseStruct GetUniverseFromId(string id) {
            foreach (UniverseStruct universe in ActiveUniverses) {
                if (universe.id == id)
                    return universe;
            }
            Debug.LogError($"Universe id lookup failed: {id}!");
            return default;
        }
    }
}
