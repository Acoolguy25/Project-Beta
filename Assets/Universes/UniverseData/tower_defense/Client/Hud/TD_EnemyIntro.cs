using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// The new-enemy card at the left edge: from the build phase before the wave that brings a new enemy
    /// until a few seconds into it, one row per new kind with its portrait, name, trait and what beats it
    /// ("Weak hits and Frost bounce off its shield. Use Cannons, Snipers or Tesla.").
    /// </summary>
    public sealed class TD_EnemyIntro : MonoBehaviour {
        [Serializable]
        public sealed class Row {
            public GameObject root;
            public Image portrait;
            public TMP_Text title, counter;
        }

        public TD_Fader fader;
        public TMP_Text heading;
        public Row[] rows = Array.Empty<Row>();

        int shownWave = -1;

        /// <summary>Shows the enemies <paramref name="wave"/> brings for the first time; false (and hidden) when it brings none.</summary>
        public bool Show(int wave, Func<TD_EnemyKind, Sprite> portraitOf) {
            if (wave == shownWave) return fader.Visible;
            shownWave = wave;
            int row = 0;
            for (int i = 0; i < TD_Rules.Enemies.Length && row < rows.Length; i++) {
                var kind = (TD_EnemyKind)i;
                if (!TD_Rules.IsNew(kind, wave)) continue;
                var stats = TD_Rules.Enemy(kind);
                var r = rows[row++];
                r.root.SetActive(true);
                if (r.portrait != null) {
                    r.portrait.sprite = portraitOf?.Invoke(kind);
                    r.portrait.enabled = r.portrait.sprite != null;
                }
                string color = ColorUtility.ToHtmlStringRGB(TD_Palette.Enemy(kind));
                r.title.text = $"{stats.name}  <color=#{color}>{stats.trait}</color>";
                r.counter.text = stats.counter;
            }
            for (int i = row; i < rows.Length; i++) rows[i].root.SetActive(false);
            if (heading != null) heading.text = row > 1 ? $"NEW ENEMIES IN WAVE {wave}" : $"NEW ENEMY IN WAVE {wave}";
            fader.Set(row > 0);
            return row > 0;
        }

        public void Hide() {
            fader.Set(false);
        }

        /// <summary>Forgets the wave shown, so a new game introduces its enemies again.</summary>
        public void ResetWave() {
            shownWave = -1;
            fader.Set(false);
        }
    }
}
