using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.GameBrowser {
    /// <summary>One game in the browser grid: thumbnail, title and how many people are playing.</summary>
    public class GameCardView : MonoBehaviour {
        [SerializeField] Image thumbnail;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text playerCount;

        public void Bind(UniverseStruct universe, ulong activePlayers) {
            name = universe.id;
            thumbnail.sprite = universe.LoadSprite();
            title.text = universe.title;
            playerCount.text = FormatPlaying(activePlayers);
        }

        public static string FormatPlaying(ulong players) =>
            players == 1 ? "1 playing" : $"{players.ToString("N0", CultureInfo.InvariantCulture)} playing";
    }
}
