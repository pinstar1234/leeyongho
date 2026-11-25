using UnityEngine;
using UnityEngine.UI;

namespace InfraTown.UI
{
    public sealed class TitleSceneryActors : MaskableGraphic
    {
        [SerializeField] private Texture2D actorAtlas;

        private readonly struct Actor
        {
            public readonly RectInt Original;
            public readonly RectInt Cutout;
            public readonly RectInt CleanPlate;

            public Actor(int x, int y, int width, int height, int atlasX, int cutoutY, int plateY)
            {
                Original = new RectInt(x, y, width, height);
                Cutout = new RectInt(atlasX, cutoutY, width, height);
                CleanPlate = new RectInt(atlasX, plateY, width, height);
            }
        }

        private readonly struct ForegroundPatch
        {
            public readonly RectInt Original;
            public readonly RectInt Tile;

            public ForegroundPatch(int x, int y, int width, int height, int atlasX, int atlasY)
            {
                Original = new RectInt(x, y, width, height);
                Tile = new RectInt(atlasX, atlasY, width, height);
            }
        }

        // Artwork-space positions and matching entries in the alpha-padded atlas.
        private static readonly Actor Train = new Actor(682, 476, 309, 65, 4, 4, 74);
        private static readonly Actor Van = new Actor(841, 590, 65, 49, 456, 4, 60);
        private static readonly Actor Truck = new Actor(1142, 688, 125, 77, 320, 4, 90);
        private static readonly Actor Tractor = new Actor(266, 772, 164, 127, 536, 4, 136);
        private static readonly Actor Airplane = new Actor(1256, 96, 211, 57, 710, 4, 64);
        private static readonly Actor[] Cattle =
        {
            new Actor(80, 753, 70, 45, 4, 155, 214),
            new Actor(113, 721, 62, 51, 81, 155, 214),
            new Actor(216, 711, 58, 50, 149, 155, 214),
            new Actor(272, 704, 55, 47, 214, 155, 214),
            new Actor(310, 736, 58, 44, 276, 155, 214)
        };

        // The original roadside silhouettes are drawn over road traffic for foreground occlusion.
        private static readonly ForegroundPatch[] RoadsideTrees =
        {
            new ForegroundPatch(971, 654, 88, 123, 710, 143),
            new ForegroundPatch(1083, 734, 81, 98, 809, 143),
            new ForegroundPatch(1158, 752, 176, 124, 4, 280),
            new ForegroundPatch(1308, 762, 193, 130, 188, 280),
            new ForegroundPatch(1492, 822, 66, 68, 390, 280)
        };

        private static readonly RectInt GroundShadow = new RectInt(346, 203, 60, 22);
        private static readonly RectInt AirplaneTrail = new RectInt(710, 125, 108, 12);

        private float _startTime;

        public override Texture mainTexture => actorAtlas != null ? actorAtlas : base.mainTexture;

        protected override void OnEnable()
        {
            base.OnEnable();
            _startTime = Time.unscaledTime;
        }

        private void Update() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (actorAtlas == null) return;

            Rect rect = GetPixelAdjustedRect();
            float seconds = Application.isPlaying ? Time.unscaledTime - _startTime : 0f;

            DrawCleanPlate(mesh, rect, Train);
            DrawCleanPlate(mesh, rect, Van);
            DrawCleanPlate(mesh, rect, Truck);
            DrawCleanPlate(mesh, rect, Tractor);
            DrawCleanPlate(mesh, rect, Airplane);
            foreach (Actor cow in Cattle)
                DrawCleanPlate(mesh, rect, cow);

            DrawAirplane(mesh, rect, seconds);

            float trainPhase = Mathf.Repeat(seconds / 22f + 0.36f, 1f);
            float trainOffset = Mathf.Lerp(156f, -280f, trainPhase);
            Vector2 trainPosition = Train.Original.center + new Vector2(trainOffset, -trainOffset * 0.085f);
            DrawActor(mesh, rect, Train, trainPosition, 1f, 0f, PassageOpacity(trainPhase));

            DrawVehicle(mesh, rect, Van, Mathf.Repeat(seconds / 12f + 0.26f, 1f),
                new Vector2(774f, 579f), new Vector2(1154f, 714f), 0.81f, 1.14f);
            DrawVehicle(mesh, rect, Truck, Mathf.Repeat(seconds / 15f + 0.37f, 1f),
                new Vector2(1055f, 677f), new Vector2(1465f, 820f), 0.86f, 1.16f);

            foreach (ForegroundPatch tree in RoadsideTrees)
                DrawTile(mesh, rect, tree.Tile, tree.Original.center, tree.Original.size, 0f, 1f);

            DrawFarm(mesh, rect, seconds);
        }

        private void DrawAirplane(VertexHelper mesh, Rect rect, float seconds)
        {
            float phase = Mathf.Repeat(seconds / 27f + 0.31f, 1f);
            Vector2 position = Airplane.Original.center + new Vector2(
                Mathf.Lerp(195f, -440f, phase), Mathf.Lerp(-10f, -43f, phase));
            float opacity = PassageOpacity(phase);
            float tilt = Mathf.Sin(seconds * 0.7f) * 0.65f;
            DrawTile(mesh, rect, AirplaneTrail, position + new Vector2(43f, 1.5f),
                new Vector2(108f, 12f), -2f + tilt, opacity);
            DrawActor(mesh, rect, Airplane, position, 1f, tilt, opacity);
        }

        private void DrawCleanPlate(VertexHelper mesh, Rect rect, Actor actor) =>
            DrawTile(mesh, rect, actor.CleanPlate, actor.Original.center, actor.Original.size, 0f, 1f);

        private void DrawActor(VertexHelper mesh, Rect rect, Actor actor, Vector2 center, float scale,
            float tilt, float opacity) =>
            DrawTile(mesh, rect, actor.Cutout, center, (Vector2)actor.Original.size * scale, tilt, opacity);

        private void DrawVehicle(VertexHelper mesh, Rect rect, Actor actor, float phase,
            Vector2 roadEntry, Vector2 roadExit, float entryScale, float exitScale)
        {
            Vector2 position = Vector2.Lerp(roadEntry, roadExit, phase);
            float scale = Mathf.Lerp(entryScale, exitScale, phase);
            float opacity = PassageOpacity(phase);
            Vector2 shadow = position + new Vector2(6f, actor.Original.height * 0.30f) * scale;
            DrawTile(mesh, rect, GroundShadow, shadow,
                new Vector2(actor.Original.width * 0.75f, 13f) * scale, 15f, opacity * 0.44f);
            DrawActor(mesh, rect, actor, position, scale, 0f, opacity);
        }

        private void DrawFarm(VertexHelper mesh, Rect rect, float seconds)
        {
            for (int i = 0; i < Cattle.Length; i++)
            {
                Actor cow = Cattle[i];
                float pace = seconds * (0.40f + i * 0.025f) + i * 1.73f;
                float distance = i == Cattle.Length - 1 ? 2.5f : 7f;
                float graze = Mathf.Sin(pace * 2.4f - i);
                Vector2 position = cow.Original.center + new Vector2(
                    Mathf.Sin(pace) * distance, Mathf.Sin(pace * 0.71f) * distance * 0.28f + graze * 0.8f);
                DrawTile(mesh, rect, GroundShadow, position + new Vector2(8f, 13f),
                    new Vector2(cow.Original.width * 0.73f, 11f), 7f, 0.43f);
                DrawActor(mesh, rect, cow, position, 1f, graze * 2.1f, 1f);
            }

            float tractorOffset = -16f + Mathf.Sin(seconds * 0.36f + 1.1f) * 17f;
            Vector2 tractorPosition = Tractor.Original.center + new Vector2(tractorOffset, -tractorOffset * 0.40f);
            DrawTile(mesh, rect, GroundShadow, tractorPosition + new Vector2(6f, 36f),
                new Vector2(134f, 30f), -14f, 0.46f);
            DrawActor(mesh, rect, Tractor, tractorPosition, 1f, Mathf.Sin(seconds * 3.6f) * 0.45f, 1f);
        }

        private static float PassageOpacity(float phase) =>
            Mathf.SmoothStep(0f, 1f, phase / 0.14f) * Mathf.SmoothStep(0f, 1f, (1f - phase) / 0.14f);

        private void DrawTile(VertexHelper mesh, Rect rect, RectInt tile, Vector2 center,
            Vector2 size, float tilt, float opacity)
        {
            if (opacity <= 0f) return;

            float left = (float)tile.xMin / actorAtlas.width;
            float right = (float)tile.xMax / actorAtlas.width;
            float top = 1f - (float)tile.yMin / actorAtlas.height;
            float bottom = 1f - (float)tile.yMax / actorAtlas.height;
            float radians = tilt * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            Vector2 horizontal = new Vector2(cos, sin) * (size.x * 0.5f);
            Vector2 vertical = new Vector2(-sin, cos) * (size.y * 0.5f);
            Color32 tint = new Color(1f, 1f, 1f, opacity);
            int first = mesh.currentVertCount;

            mesh.AddVert(TitleSceneryMotion.ArtworkPoint(rect, center - horizontal - vertical), tint, new Vector2(left, top));
            mesh.AddVert(TitleSceneryMotion.ArtworkPoint(rect, center + horizontal - vertical), tint, new Vector2(right, top));
            mesh.AddVert(TitleSceneryMotion.ArtworkPoint(rect, center + horizontal + vertical), tint, new Vector2(right, bottom));
            mesh.AddVert(TitleSceneryMotion.ArtworkPoint(rect, center - horizontal + vertical), tint, new Vector2(left, bottom));
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
