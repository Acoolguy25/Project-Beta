using System.Collections.Generic;
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
        public bool disableDefaultCommands;
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
        public string LoadRandomText(string localPath) {
            TextAsset textAsset = Resources.Load<TextAsset>(GetResourcePath(localPath));
            Assert.IsNotNull(textAsset, $"{id} TextAsset Not Found: {localPath}");
            string[] lines = textAsset.text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            return lines[Random.Range(0, lines.Length)];
        }
    };
    /// <summary>
    /// The universes (games) this build knows about. Packages only see this registry; the game
    /// project owns the actual list (Assets/Universes/UniverseCfg.cs) and registers it at startup.
    /// </summary>
    public static class UniverseRegistry {
        static UniverseStruct[] universes = System.Array.Empty<UniverseStruct>();

        public static IReadOnlyList<UniverseStruct> All => universes;

        public static void Register(UniverseStruct[] activeUniverses) {
            universes = activeUniverses ?? System.Array.Empty<UniverseStruct>();
        }

        public static UniverseStruct GetUniverseFromId(string id) {
            foreach (UniverseStruct universe in universes) {
                if (universe.id == id)
                    return universe;
            }
            Debug.LogError(universes.Length == 0
                ? $"Universe id lookup failed: {id}! No universes are registered (UniverseRegistry.Register was never called)."
                : $"Universe id lookup failed: {id}!");
            return default;
        }
    }
}
