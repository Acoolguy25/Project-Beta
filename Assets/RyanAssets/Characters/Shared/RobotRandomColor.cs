using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Characters.Shared
{
    public class RobotRandomColor : RobotColorController
    {
        [SerializeField] private int colorCount = 200;

        [SerializeField] private Color[] primaryColors;
        [SerializeField] private Color[] secondaryColors;

        private void Awake()
        {
            primaryColors = GenerateDistinctColors(colorCount, 0.45f, 0.75f); // suits (muted)
            secondaryColors = GenerateDistinctColors(colorCount, 0.95f, 1.0f); // eyes (neon)
        }

        private void Start()
        {
            Color newPrimaryColor = primaryColors[Random.Range(0, primaryColors.Length)];
            Color newSecondaryColor = secondaryColors[Random.Range(0, secondaryColors.Length)];
            SetColors(newPrimaryColor, newSecondaryColor);
        }

        private Color[] GenerateDistinctColors(int count, float saturation, float value)
        {
            List<Color> colors = new List<Color>(count);

            float goldenRatio = 0.61803398875f;
            float hue = Random.value;

            int attempts = 0;

            while (colors.Count < count && attempts < count * 50)
            {
                attempts++;

                hue = (hue + goldenRatio) % 1f;

                Color c = Color.HSVToRGB(hue, saturation, value);

                if (IsDistinct(c, colors, 0.18f))
                    colors.Add(c);
            }

            return colors.ToArray();
        }

        private bool IsDistinct(Color c, List<Color> list, float minDistance)
        {
            foreach (var other in list)
            {
                if (ColorDistance(c, other) < minDistance)
                    return false;
            }
            return true;
        }

        // perceptual-ish distance (good enough for games)
        private float ColorDistance(Color a, Color b)
        {
            float r = a.r - b.r;
            float g = a.g - b.g;
            float bl = a.b - b.b;
            return r * r + g * g + bl * bl;
        }
    }
}