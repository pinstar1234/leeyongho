using UnityEngine;
using UnityEngine.UI;

namespace InfraTown.UI
{
    // 도움말 전용 시연. 실제 지도와 건설 상태에는 접근하지 않는다.
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class ConstructionHelpDemo : MaskableGraphic
    {
        private const float Duration = 4.6f;
        private const float PlacementTime = 2.6f;
        private const float YardWidth = 760f;
        private const float YardHeight = 330f;
        private const float TileWidth = 132f;
        private const float TileDepth = 60f;
        private const float TileShear = 16f;
        private const float GridLeft = 15f;
        private const float RoadTop = 124f;
        private const int FirstPlacementColumn = 1;
        private float elapsed;
        private bool continuous;

        internal bool Playing { get; set; }

        internal void Restart(bool drag)
        {
            continuous = drag;
            elapsed = 0f;
            Playing = true;
            SetVerticesDirty();
        }

        private void Update()
        {
            if (!Playing) return;
            elapsed = Mathf.Repeat(elapsed + Time.unscaledDeltaTime, Duration);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            var art = new HelpIllustration(vertices, rect, YardWidth, YardHeight);
            art.Box(0, 0, YardWidth, YardHeight, new Color32(104, 126, 66, 255));
            DrawGrid(art);

            Tree(art, 81f, 245f, 1f);
            Tree(art, 682f, 237f, 1.12f);
            Tree(art, 720f, 265f, .71f);
            float roadBottom = TilePoint(0, -1f).y;
            art.Box(0, roadBottom, YardWidth, TileDepth, new Color32(170, 151, 112, 255));
            art.Box(0, roadBottom, YardWidth, 5, new Color32(210, 196, 154, 255));
            art.Box(0, RoadTop - 5f, YardWidth, 5, new Color32(210, 196, 154, 255));
            for (int x = 12; x < YardWidth - 20; x += 54)
                art.Box(x, 91, 28, 2, new Color32(220, 198, 147, 255));
            House(art, FirstPlacementColumn - 1, false);

            int count = continuous ? 4 : 1;
            float movement = Mathf.InverseLerp(.55f, 2.25f, elapsed);
            int shown = 1 + Mathf.RoundToInt(movement * (count - 1));
            bool placed = elapsed >= PlacementTime;
            Vector2 start = TilePoint(FirstPlacementColumn + .5f, .17f);
            Vector2 cursor = TilePoint(FirstPlacementColumn + .5f + (count - 1) * movement, .17f);
            for (int i = 0; i < shown; i++)
                House(art, FirstPlacementColumn + i, !placed);

            if (continuous && !placed)
            {
                for (float x = start.x + 10; x < cursor.x - 6; x += 14)
                    art.Box(x, start.y - 1.5f, 7, 3, Color.white);
                art.Disc(start, 5f, Color.white);
            }
            Cursor(art, cursor.x, placed ? RoadTop : cursor.y);

            art.Box(0, 0, YardWidth, 54, new Color32(32, 43, 26, 235));
            art.Box(164, 25, 552, 4, new Color32(174, 177, 127, 255));
            art.Box(164, 25, 552 * (elapsed / Duration), 4, new Color32(248, 223, 158, 255));
            for (int i = 0; i <= 2; i++)
                art.Disc(new Vector2(164 + i * 276, 27), 5f, new Color32(248, 223, 158, 255));
        }

        private static Vector2 TilePoint(float column, float row) =>
            new Vector2(GridLeft + column * TileWidth + row * TileShear, RoadTop + row * TileDepth);

        private static void DrawGrid(HelpIllustration art)
        {
            var color = new Color32(193, 187, 123, 160);
            float bottomRow = -RoadTop / TileDepth;
            float topRow = (YardHeight - RoadTop) / TileDepth;
            int firstColumn = Mathf.CeilToInt((-GridLeft - topRow * TileShear) / TileWidth);
            int lastColumn = Mathf.FloorToInt((YardWidth - GridLeft - bottomRow * TileShear) / TileWidth);
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                float x = TilePoint(column, 0f).x;
                float startRow = Mathf.Max(bottomRow, -x / TileShear);
                float endRow = Mathf.Min(topRow, (YardWidth - x) / TileShear);
                art.Line(TilePoint(column, startRow), TilePoint(column, endRow), 1f, color);
            }
            for (int row = Mathf.CeilToInt(bottomRow); row <= Mathf.FloorToInt(topRow); row++)
                art.Box(0, TilePoint(0, row).y, YardWidth, 1, color);
        }

        private static void Tree(HelpIllustration art, float x, float y, float scale)
        {
            art.Box(x - 4 * scale, y - 21 * scale, 8 * scale, 34 * scale, new Color32(89, 64, 42, 255));
            art.Disc(new Vector2(x, y + 12 * scale), 27 * scale, new Color32(59, 91, 49, 255));
            art.Disc(new Vector2(x - 10 * scale, y + 18 * scale), 16 * scale, new Color32(83, 115, 56, 255));
        }

        private static void House(HelpIllustration art, int column, bool preview)
        {
            var wall = preview ? new Color32(154, 222, 109, 225) : new Color32(236, 213, 159, 255);
            var side = preview ? new Color32(119, 188, 86, 230) : new Color32(201, 166, 112, 255);
            var roof = preview ? new Color32(176, 235, 131, 227) : new Color32(182, 117, 66, 255);
            var shade = preview ? new Color32(92, 154, 68, 235) : new Color32(135, 83, 44, 255);
            if (preview)
            {
                art.Quad(TilePoint(column, 0f), TilePoint(column + 1, 0f),
                    TilePoint(column + 1, 1f), TilePoint(column, 1f), new Color32(168, 230, 106, 255));
                art.Quad(TilePoint(column + .025f, .055f), TilePoint(column + .975f, .055f),
                    TilePoint(column + .975f, .945f), TilePoint(column + .025f, .945f), new Color32(104, 163, 67, 255));
            }

            Vector2 frontLeft = TilePoint(column + .25f, .31f);
            Vector2 frontRight = TilePoint(column + .75f, .31f);
            Vector2 backRight = TilePoint(column + .75f, .77f);
            Vector2 backLeft = TilePoint(column + .25f, .77f);
            var height = new Vector2(0, 43);
            art.Quad(frontLeft, frontRight, frontRight + height, frontLeft + height, wall);
            art.Quad(frontRight, backRight, backRight + height, frontRight + height, side);

            Vector2 eaveLeft = frontLeft + height + new Vector2(-6, 0);
            Vector2 eaveRight = frontRight + height + new Vector2(6, 0);
            Vector2 frontPeak = (frontLeft + frontRight) * .5f + height + new Vector2(0, 29);
            Vector2 depth = backLeft - frontLeft;
            art.Triangle(eaveLeft, eaveRight, frontPeak, shade);
            art.Quad(frontPeak, frontPeak + depth, eaveRight + depth, eaveRight, roof);
            art.Box(frontLeft.x + 13, frontLeft.y, 13, 24, shade);
            art.Box(frontRight.x - 23, frontLeft.y + 22, 11, 11, shade);
        }

        private static void Cursor(HelpIllustration art, float x, float y)
        {
            var a = new Vector2(x, y);
            var b = new Vector2(x + 3, y - 30);
            var c = new Vector2(x + 26, y - 20);
            art.Triangle(a, b, c, new Color32(33, 30, 24, 255));
            art.Triangle(a + new Vector2(3, -5), b + new Vector2(2, 5), c + new Vector2(-6, 1), Color.white);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class HelpCircleGraphic : MaskableGraphic
    {
        private const float ReferenceSize = 62f;
        private const float OuterRadius = 30f;

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            var art = new HelpIllustration(vertices, GetPixelAdjustedRect(), ReferenceSize, ReferenceSize);
            var center = Vector2.one * (ReferenceSize / 2f);
            art.Disc(center, OuterRadius, Multiply(new Color32(225, 188, 117, 255)));
            art.Disc(center, 26f, Multiply(new Color32(41, 67, 43, 255)));
        }

        public override bool Raycast(Vector2 screenPosition, Camera eventCamera)
        {
            if (!base.Raycast(screenPosition, eventCamera) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPosition, eventCamera, out var point))
                return false;
            Vector2 offset = Rect.PointToNormalized(rectTransform.rect, point) - Vector2.one * .5f;
            const float normalizedRadius = OuterRadius / ReferenceSize;
            return offset.sqrMagnitude <= normalizedRadius * normalizedRadius;
        }

        private Color Multiply(Color tint) => tint * color;
    }

    internal readonly struct HelpIllustration
    {
        private readonly VertexHelper vertices;
        private readonly Rect rect;
        private readonly float scaleX;
        private readonly float scaleY;

        internal HelpIllustration(VertexHelper vertices, Rect rect, float width, float height)
        {
            this.vertices = vertices;
            this.rect = rect;
            scaleX = rect.width / width;
            scaleY = rect.height / height;
        }

        internal void Box(float x, float y, float width, float height, Color color)
        {
            Quad(new Vector2(x, y), new Vector2(x + width, y), new Vector2(x + width, y + height), new Vector2(x, y + height), color);
        }

        internal void Line(Vector2 a, Vector2 b, float width, Color color)
        {
            var direction = (b - a).normalized;
            var perpendicular = new Vector2(-direction.y, direction.x) * (width / 2);
            Quad(a - perpendicular, b - perpendicular, b + perpendicular, a + perpendicular, color);
        }

        internal void Disc(Vector2 center, float radius, Color color)
        {
            const int segments = 32;
            Vector2 previous = center + Vector2.right * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * (2 * Mathf.PI / segments);
                Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Triangle(center, previous, next, color);
                previous = next;
            }
        }

        internal void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int index = vertices.currentVertCount;
            Add(a, color);
            Add(b, color);
            Add(c, color);
            Add(d, color);
            vertices.AddTriangle(index, index + 1, index + 2);
            vertices.AddTriangle(index, index + 2, index + 3);
        }

        internal void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int index = vertices.currentVertCount;
            Add(a, color);
            Add(b, color);
            Add(c, color);
            vertices.AddTriangle(index, index + 1, index + 2);
        }

        private void Add(Vector2 point, Color color)
        {
            vertices.AddVert(new Vector3(rect.xMin + point.x * scaleX, rect.yMin + point.y * scaleY), color, Vector2.zero);
        }
    }
}
