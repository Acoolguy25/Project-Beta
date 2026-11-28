#if !UNITY_SERVER
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// Paints a small top-down picture of a board layout (roads, bases, special bases) for the board vote.
    /// Every match rolls a fresh board from its layout, so the picture is one sample of the layout's
    /// style, generated with a fixed seed: the same layout always shows the same picture. Pictures are
    /// cached for the session.
    /// </summary>
    public static class DI_BoardPreview {
        /// <summary>Seed of the sample board each picture shows.</summary>
        public const int PreviewSeed = 20261128;

        static readonly Color Background = new(0.035f, 0.045f, 0.085f, 1f);
        static readonly Color GridLine = new(0.11f, 0.15f, 0.26f, 1f);
        static readonly Color Border = new(0.25f, 0.55f, 1f, 1f);
        static readonly Color Road = new(0.42f, 0.55f, 0.78f, 1f);
        static readonly Color Plain = new(0.88f, 0.9f, 0.96f, 1f);
        static readonly Color Turret = new(0.62f, 0.66f, 0.74f, 1f);
        static readonly Color TurretRange = new(0.62f, 0.66f, 0.74f, 0.25f);
        static readonly Color Super = new(0.4f, 0.92f, 1f, 1f);
        static readonly Color Speed = new(0.35f, 1f, 0.75f, 1f);
        static readonly Color Factory = new(1f, 0.72f, 0.22f, 1f);
        static readonly Color Fortress = new(0.78f, 0.86f, 1f, 1f);
        const float GridStep = 20f;

        static readonly Dictionary<string, Texture2D> cache = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => cache.Clear();

        /// <summary>The picture of <paramref name="layout"/>, <paramref name="width"/> pixels wide (height keeps the arena's shape).</summary>
        public static Texture2D For(DI_BoardLayout layout, int width) {
            if (layout == null) return null;
            string key = layout.id + "@" + width;
            if (cache.TryGetValue(key, out Texture2D cached) && cached != null)
                return cached;
            Texture2D texture = Paint(DI_BoardGenerator.Generate(layout, PreviewSeed), width);
            texture.name = "DI_BoardPreview_" + layout.id;
            cache[key] = texture;
            return texture;
        }

        static Texture2D Paint(DI_BoardPlan plan, int width) {
            width = Mathf.Max(32, width);
            int height = Mathf.RoundToInt(width * DI_Arena.HalfHeight / DI_Arena.HalfWidth);
            var canvas = new PreviewCanvas(width, height);
            canvas.Fill(Background);

            for (float x = -DI_Arena.HalfWidth; x <= DI_Arena.HalfWidth; x += GridStep)
                canvas.Line(new Vector2(x, -DI_Arena.HalfHeight), new Vector2(x, DI_Arena.HalfHeight), GridLine, 0.5f);
            for (float y = -DI_Arena.HalfHeight; y <= DI_Arena.HalfHeight; y += GridStep)
                canvas.Line(new Vector2(-DI_Arena.HalfWidth, y), new Vector2(DI_Arena.HalfWidth, y), GridLine, 0.5f);

            float baseRadius = Mathf.Max(1.6f, width / 90f);
            for (int i = 0; i < plan.Count; i++)
                if (plan.kinds[i] == DI_BaseKind.Turret)
                    canvas.Ring(plan.positions[i], DI_Rules.DefaultTurretRange, TurretRange, 0.8f);
            foreach (Vector2Int link in plan.links)
                canvas.Line(plan.positions[link.x], plan.positions[link.y], Road, 0.8f);

            for (int i = 0; i < plan.Count; i++) {
                Vector2 position = plan.positions[i];
                switch (plan.kinds[i]) {
                    case DI_BaseKind.Fortress:
                        canvas.Square(position, baseRadius * 1.25f, Fortress);
                        canvas.Square(position, baseRadius * 0.6f, Background);
                        break;
                    case DI_BaseKind.Factory:
                        canvas.Disc(position, baseRadius * 1.5f, Factory * new Color(1f, 1f, 1f, 0.35f));
                        canvas.Disc(position, baseRadius, Factory);
                        break;
                    case DI_BaseKind.Super:
                        canvas.Disc(position, baseRadius * 1.7f, Super * new Color(1f, 1f, 1f, 0.3f));
                        canvas.Disc(position, baseRadius * 1.2f, Super);
                        break;
                    case DI_BaseKind.Speed: canvas.Disc(position, baseRadius, Speed); break;
                    case DI_BaseKind.Turret: canvas.Disc(position, baseRadius * 1.1f, Turret); break;
                    default: canvas.Disc(position, baseRadius, Plain); break;
                }
            }

            canvas.Frame(Border);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels(canvas.Pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>A tiny software canvas in board units: soft-edged lines, discs and squares.</summary>
        sealed class PreviewCanvas {
            readonly int width, height;
            readonly float scale;
            public readonly Color[] Pixels;

            public PreviewCanvas(int width, int height) {
                this.width = width;
                this.height = height;
                scale = width / (2f * DI_Arena.HalfWidth);
                Pixels = new Color[width * height];
            }

            Vector2 ToPixels(Vector2 board) => new(
                (board.x + DI_Arena.HalfWidth) * scale, (board.y + DI_Arena.HalfHeight) * scale);

            public void Fill(Color color) {
                for (int i = 0; i < Pixels.Length; i++) Pixels[i] = color;
            }

            void Blend(int x, int y, Color color, float coverage) {
                if (x < 0 || y < 0 || x >= width || y >= height || coverage <= 0f) return;
                int index = y * width + x;
                float alpha = Mathf.Clamp01(coverage) * color.a;
                Color under = Pixels[index];
                Pixels[index] = new Color(
                    Mathf.Lerp(under.r, color.r, alpha), Mathf.Lerp(under.g, color.g, alpha),
                    Mathf.Lerp(under.b, color.b, alpha), 1f);
            }

            /// <summary>A line <paramref name="halfWidth"/> pixels either side of its centre, softened over one pixel.</summary>
            public void Line(Vector2 from, Vector2 to, Color color, float halfWidth) {
                Vector2 a = ToPixels(from), b = ToPixels(to);
                int minX = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - halfWidth - 1f), maxX = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + halfWidth + 1f);
                int minY = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - halfWidth - 1f), maxY = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + halfWidth + 1f);
                Vector2 segment = b - a;
                float lengthSquared = Mathf.Max(1e-4f, segment.sqrMagnitude);
                for (int y = Mathf.Max(0, minY); y <= Mathf.Min(height - 1, maxY); y++)
                    for (int x = Mathf.Max(0, minX); x <= Mathf.Min(width - 1, maxX); x++) {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, segment) / lengthSquared);
                        float distance = Vector2.Distance(p, a + segment * t);
                        Blend(x, y, color, halfWidth + 0.5f - distance);
                    }
            }

            public void Disc(Vector2 centre, float radius, Color color) {
                Vector2 c = ToPixels(centre);
                for (int y = Mathf.FloorToInt(c.y - radius - 1f); y <= Mathf.CeilToInt(c.y + radius + 1f); y++)
                    for (int x = Mathf.FloorToInt(c.x - radius - 1f); x <= Mathf.CeilToInt(c.x + radius + 1f); x++)
                        Blend(x, y, color, radius + 0.5f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c));
            }

            public void Ring(Vector2 centre, float boardRadius, Color color, float halfWidth) {
                Vector2 c = ToPixels(centre);
                float radius = boardRadius * scale;
                for (int y = Mathf.FloorToInt(c.y - radius - 2f); y <= Mathf.CeilToInt(c.y + radius + 2f); y++)
                    for (int x = Mathf.FloorToInt(c.x - radius - 2f); x <= Mathf.CeilToInt(c.x + radius + 2f); x++) {
                        float distance = Mathf.Abs(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) - radius);
                        Blend(x, y, color, halfWidth + 0.5f - distance);
                    }
            }

            public void Square(Vector2 centre, float halfSize, Color color) {
                Vector2 c = ToPixels(centre);
                for (int y = Mathf.FloorToInt(c.y - halfSize); y <= Mathf.CeilToInt(c.y + halfSize); y++)
                    for (int x = Mathf.FloorToInt(c.x - halfSize); x <= Mathf.CeilToInt(c.x + halfSize); x++) {
                        float edge = Mathf.Max(Mathf.Abs(x + 0.5f - c.x), Mathf.Abs(y + 0.5f - c.y));
                        Blend(x, y, color, halfSize + 0.5f - edge);
                    }
            }

            /// <summary>A one-pixel outline round the whole picture.</summary>
            public void Frame(Color color) {
                for (int x = 0; x < width; x++) { Blend(x, 0, color, 1f); Blend(x, height - 1, color, 1f); }
                for (int y = 0; y < height; y++) { Blend(0, y, color, 1f); Blend(width - 1, y, color, 1f); }
            }
        }
    }
}
#endif
