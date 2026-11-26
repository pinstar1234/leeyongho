using UnityEngine;
using UnityEngine.UI;

namespace InfraTown.UI
{
    public sealed class TitleSceneryMotion : MaskableGraphic
    {
        private const float ArtworkWidth = 1672f;
        private const float ArtworkHeight = 941f;
        private const int CloudSegments = 12;

        // Each point is the river center, measured in background image pixels, and its safe half-width.
        // The bridge splits the visible river into two stretches.
        private static readonly Vector3[] UpperRiver =
        {
            new Vector3(706, 623, 13), new Vector3(747, 644, 20), new Vector3(766, 669, 21),
            new Vector3(789, 692, 23), new Vector3(820, 722, 36), new Vector3(818, 745, 37),
            new Vector3(791, 768, 37), new Vector3(759, 794, 29), new Vector3(737, 808, 19)
        };

        private static readonly Vector3[] LowerRiver =
        {
            new Vector3(858, 874, 32), new Vector3(902, 890, 60),
            new Vector3(945, 912, 96), new Vector3(971, 938, 126)
        };

        private float _startTime;

        protected override void OnEnable()
        {
            base.OnEnable();
            _startTime = Time.unscaledTime;
        }

        private void Update() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float seconds = Application.isPlaying ? Time.unscaledTime - _startTime : 0f;
            DrawRiver(mesh, rect, UpperRiver, seconds, 18, 5.4f, 0.11f);
            DrawRiver(mesh, rect, LowerRiver, seconds, 13, 3.9f, 0.37f);
            DrawCloud(mesh, rect, seconds, 42f, 0.27f, 66f, 1f);
            DrawCloud(mesh, rect, seconds, 36f, 0.71f, 183f, 0.72f);
        }

        private static void DrawRiver(VertexHelper mesh, Rect rect, Vector3[] river, float seconds,
            int count, float travelSeconds, float offset)
        {
            for (int i = 0; i < count; i++)
            {
                float progress = Mathf.Repeat(i / (float)count + seconds / travelSeconds + offset, 1f);
                Vector3 span = SampleRiver(river, progress);
                float lateral = Mathf.Lerp(-0.65f, 0.65f, Mathf.Repeat(i * 0.618f + offset, 1f));
                float width = span.z * Mathf.Lerp(0.14f, 0.30f, Mathf.Repeat(i * 0.413f, 1f));
                float sparkle = 0.65f + 0.35f * Mathf.Sin(seconds * 2.1f + i * 1.9f);
                float envelope = Mathf.SmoothStep(0f, 1f, progress * 6f)
                    * Mathf.SmoothStep(0f, 1f, (1f - progress) * 6f);

                Color reflection = Color.Lerp(new Color32(174, 232, 225, 255),
                    new Color32(255, 218, 152, 255), Mathf.Repeat(i * 0.271f, 1f) * 0.7f);
                reflection.a = envelope * sparkle * 0.23f;
                AddGlint(mesh, rect, new Vector2(span.x + lateral * span.z, span.y),
                    width, Mathf.Lerp(1.1f, 2.5f, Mathf.Repeat(i * 0.371f, 1f)), reflection);
            }
        }

        private static Vector3 SampleRiver(Vector3[] river, float progress)
        {
            float y = Mathf.Lerp(river[0].y, river[river.Length - 1].y, progress);
            for (int i = 1; i < river.Length; i++)
            {
                if (y <= river[i].y)
                    return Vector3.Lerp(river[i - 1], river[i], Mathf.InverseLerp(river[i - 1].y, river[i].y, y));
            }

            return river[river.Length - 1];
        }

        private static void AddGlint(VertexHelper mesh, Rect rect, Vector2 center, float width, float height, Color color)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(ArtworkPoint(rect, center), color, Vector2.zero);
            color.a = 0f;
            mesh.AddVert(ArtworkPoint(rect, center + new Vector2(-width, height * 0.25f)), color, Vector2.zero);
            mesh.AddVert(ArtworkPoint(rect, center + new Vector2(-width * 0.3f, -height)), color, Vector2.zero);
            mesh.AddVert(ArtworkPoint(rect, center + new Vector2(width, -height * 0.3f)), color, Vector2.zero);
            mesh.AddVert(ArtworkPoint(rect, center + new Vector2(width * 0.35f, height)), color, Vector2.zero);
            for (int i = 1; i <= 4; i++)
                mesh.AddTriangle(first, first + i, first + (i % 4) + 1);
        }

        private static void DrawCloud(VertexHelper mesh, Rect rect, float seconds, float duration,
            float offset, float y, float scale)
        {
            float progress = Mathf.Repeat(seconds / duration + offset, 1f);
            float x = Mathf.Lerp(1200f, 1540f, progress);
            float fade = Mathf.SmoothStep(0f, 1f, progress * 4f)
                * Mathf.SmoothStep(0f, 1f, (1f - progress) * 4f);
            var color = new Color(1f, 0.89f, 0.76f, fade * 0.12f);

            AddMist(mesh, rect, new Vector2(x, y), 116f * scale, 12f * scale, color);
            AddMist(mesh, rect, new Vector2(x - 49f * scale, y - 4f * scale),
                60f * scale, 9f * scale, color);
            color.a *= 0.72f;
            AddMist(mesh, rect, new Vector2(x + 52f * scale, y + 5f * scale),
                77f * scale, 7f * scale, color);
        }

        private static void AddMist(VertexHelper mesh, Rect rect, Vector2 center, float width, float height, Color color)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(ArtworkPoint(rect, center), color, Vector2.zero);
            color.a = 0f;
            for (int i = 0; i < CloudSegments; i++)
            {
                float angle = i * (2f * Mathf.PI / CloudSegments);
                var edge = new Vector2(Mathf.Cos(angle) * width, Mathf.Sin(angle) * height);
                mesh.AddVert(ArtworkPoint(rect, center + edge), color, Vector2.zero);
                mesh.AddTriangle(first, first + i + 1, first + ((i + 1) % CloudSegments) + 1);
            }
        }

        internal static Vector2 ArtworkPoint(Rect rect, Vector2 point) => new Vector2(
            rect.xMin + point.x * rect.width / ArtworkWidth,
            rect.yMax - point.y * rect.height / ArtworkHeight);
    }
}
