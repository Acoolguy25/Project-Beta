using UnityEditor;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Editor.Manor {
    /// <summary>
    /// Resolves the plan's "pack/model" names to the third-party models under Assets/ThirdParty, and
    /// says how big each pack is drawn. All packs are CC0 (see each pack's LICENSE.txt and credits.md).
    /// </summary>
    public static class HS_PropCatalog {
        /// <summary>
        /// KayKit models are built at about 1.33x real size (a door is 2.8 units tall); this brings them
        /// to plan metres (a 2.1 m door).
        /// </summary>
        public const float KayKitToPlan = 0.75f;

        const string KayKit = "Assets/ThirdParty/KayKit";

        static string Folder(string pack) => pack switch {
            "furniture" => $"{KayKit}/FurnitureBits/Models",
            "restaurant" => $"{KayKit}/RestaurantBits/Models",
            "halloween" => $"{KayKit}/HalloweenBits/Models",
            "dungeon" => $"{KayKit}/DungeonRemastered/Models",
            "city" => $"{KayKit}/CityBuilderBits/Models",
            _ => throw new System.ArgumentException($"Unknown model pack '{pack}'. Add it to {nameof(HS_PropCatalog)}."),
        };

        public static string PathOf(string model) {
            int slash = model.IndexOf('/');
            if (slash <= 0) throw new System.ArgumentException($"Model '{model}' must be written as pack/name.");
            return $"{Folder(model[..slash])}/{model[(slash + 1)..]}.fbx";
        }

        /// <summary>Loads a model, failing loudly so a typo in the plan never silently drops furniture.</summary>
        public static GameObject Load(string model) {
            string path = PathOf(model);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path)
                ?? throw new System.IO.FileNotFoundException($"[HideAndSeek] Missing model {path} (from '{model}').");
        }

        /// <summary>
        /// City Builder Bits is modelled at street-map scale (a car is under 1 unit long); this makes a
        /// car about 4.5 m long.
        /// </summary>
        public const float CityToPlan = 4.8f;

        /// <summary>Plan-metre scale for one model of a pack.</summary>
        public static float PackScale(string model) => model.StartsWith("city/") ? CityToPlan : KayKitToPlan;
    }
}
