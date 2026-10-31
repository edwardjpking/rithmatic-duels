using System;
using System.Collections.Generic;
using RithmaticDuels.Rules.Geometry;

namespace RithmaticDuels.Rules.Presets
{
    /// <summary>
    /// A bind point layout for a main LoW (DESIGN.md section 6.1). A circle's layout is a triangle construction:
    /// three vertices, stored for a circle of radius 1 centred at the origin with +X facing the opponent. Its
    /// bind points are where the edges and the extended altitudes cross the circle; every crossing is kept and
    /// crossings that sit on top of each other count once. An ellipse layout has no triangle and always 2 points.
    /// Plain data so it can be saved as JSON.
    /// </summary>
    [Serializable]
    public sealed class BindPointLayout
    {
        public string Name = "";
        public bool IsEllipse;
        /// <summary>Triangle vertices on a unit circle's frame (circle layouts only). Always 3.</summary>
        public Vec2[] Vertices = new Vec2[3];
        /// <summary>True for the layouts that ship with the game; they can't be edited or deleted.</summary>
        public bool IsDefault;

        /// <summary>Two bind points closer than this (as a fraction of the radius) count as one.</summary>
        public const float SamePointFraction = 1e-3f;

        public int BindPointCount { get { return IsEllipse ? 2 : PointsOnUnitCircle().Count; } }

        /// <summary>The bind points on a unit circle at the origin, in the order the board lists them (front first, anticlockwise).</summary>
        public List<Vec2> PointsOnUnitCircle()
        {
            if (IsEllipse) return new List<Vec2> { new Vec2(1f, 0f), new Vec2(-1f, 0f) };
            return Points(Vec2.Zero, 1f, new Vec2(1f, 0f));
        }

        /// <summary>The bind points on a real circle, turned so the layout's front faces <paramref name="facing"/>.</summary>
        public List<Vec2> Points(Vec2 centre, float radius, Vec2 facing)
        {
            if (IsEllipse) throw new InvalidOperationException("An ellipse's bind points come from its shape.");
            var raw = BindPoints.FromTriangle(Vec2.Zero, 1f, Vertices[0], Vertices[1], Vertices[2], SamePointFraction);
            var placed = new List<Vec2>();
            foreach (var p in raw) placed.Add(centre + p.Rotated(facing.Angle) * radius);
            return BindPoints.Sorted(placed, centre, facing);
        }

        /// <summary>Null if the layout can be saved, otherwise why not.</summary>
        public string Problem(RulesConfig config)
        {
            if (string.IsNullOrWhiteSpace(Name)) return "Give the layout a name.";
            if (IsEllipse) return null;
            if (Vertices == null || Vertices.Length != 3) return "A layout needs three triangle vertices.";
            // Vertices in a straight line are fine (Amelia, v0.31): the heights are then the lines through each
            // vertex perpendicular to that line, which is what the construction gives anyway.
            int count = BindPointCount;
            if (count < config.Presets.MinLayoutBindPoints) return "The layout needs at least " + config.Presets.MinLayoutBindPoints + " bind points.";
            return null;
        }

        /// <summary>
        /// Where vertex <paramref name="index"/> should go when dragged to <paramref name="desired"/>: if two bind
        /// points would come within <paramref name="snapDistance"/> (unit-circle units) of each other, the nearest
        /// spot (within that distance of the desired one) where they sit exactly on top of each other; otherwise
        /// <paramref name="desired"/> unchanged (DESIGN.md section 6.1).
        /// </summary>
        public static Vec2 SnapVertex(Vec2[] vertices, int index, Vec2 desired, float snapDistance)
        {
            var trial = (Vec2[])vertices.Clone();
            trial[index] = desired;

            // The closest pair of crossings that are near each other but not already the same point.
            var slots = Crossings(trial);
            int p = -1, q = -1;
            float gap = snapDistance;
            for (int i = 0; i < slots.Length; i++)
                for (int j = i + 1; j < slots.Length; j++)
                {
                    if (!slots[i].HasValue || !slots[j].HasValue) continue;
                    float d = Vec2.Distance(slots[i].Value, slots[j].Value);
                    if (d > Merged && d < gap) { gap = d; p = i; q = j; }
                }
            if (p < 0) return desired;

            // Move the vertex (pattern search) until that pair sits on top of each other.
            Vec2 best = desired;
            float step = snapDistance;
            for (int iteration = 0; iteration < 400 && gap > Merged * 0.1f && step > 1e-7f; iteration++)
            {
                bool improved = false;
                for (int s = 0; s < 8; s++)
                {
                    trial[index] = best + Vec2.FromAngle((float)Math.PI * s / 4f) * step;
                    float d = PairGap(trial, p, q);
                    if (d < gap) { gap = d; best = trial[index]; improved = true; }
                }
                if (!improved) step *= 0.5f;
            }
            return gap <= Merged && Vec2.Distance(best, desired) <= 3f * snapDistance ? best : desired;
        }

        /// <summary>
        /// Whether vertex <paramref name="index"/> may go to <paramref name="spot"/> in the bind point editor: within
        /// LayoutMaxVertexRadius of the centre, and at least LayoutMinVertexGap from the other two vertices. Vertices
        /// in a straight line are allowed (DESIGN.md sections 1.1 and 6.1).
        /// </summary>
        public static bool VertexAllowed(Vec2[] vertices, int index, Vec2 spot, PresetsConfig config)
        {
            if (spot.Length > config.LayoutMaxVertexRadius + 1e-5f) return false;
            Vec2 a = vertices[(index + 1) % 3], b = vertices[(index + 2) % 3];
            float gap = config.LayoutMinVertexGap;
            return Vec2.Distance(spot, a) >= gap && Vec2.Distance(spot, b) >= gap;
        }

        /// <summary>Two crossings this close count as the same bind point when snapping.</summary>
        private const float Merged = 1e-4f;

        private static float PairGap(Vec2[] vertices, int p, int q)
        {
            var slots = Crossings(vertices);
            if (!slots[p].HasValue || !slots[q].HasValue) return float.MaxValue;
            return Vec2.Distance(slots[p].Value, slots[q].Value);
        }

        /// <summary>
        /// Every crossing with the unit circle, unmerged, in fixed slots (two per edge, then two per altitude line) so
        /// a crossing keeps its slot while a vertex moves a little. Empty slots are null.
        /// </summary>
        private static Vec2?[] Crossings(Vec2[] v)
        {
            var slots = new Vec2?[12];
            for (int i = 0; i < 3; i++)
            {
                Vec2 from = v[i], to = v[(i + 1) % 3], opposite = v[(i + 2) % 3];
                var edge = Intersect.LineCircle(from, to - from, Vec2.Zero, 1f);
                for (int k = 0; k < edge.Length && k < 2; k++)
                    if (edge[k] >= -1e-5f && edge[k] <= 1f + 1e-5f) slots[i * 2 + k] = from + (to - from) * edge[k];
                Vec2 altitude = (to - from).Perp;
                var alt = Intersect.LineCircle(opposite, altitude, Vec2.Zero, 1f);
                for (int k = 0; k < alt.Length && k < 2; k++) slots[6 + i * 2 + k] = opposite + altitude * alt[k];
            }
            return slots;
        }

        public static BindPointLayout Triangle(string name, Vec2 a, Vec2 b, Vec2 c, bool isDefault = false)
        {
            return new BindPointLayout { Name = name, Vertices = new[] { a, b, c }, IsDefault = isDefault };
        }

        public static BindPointLayout Ellipse(string name, bool isDefault = false)
        {
            return new BindPointLayout { Name = name, IsEllipse = true, IsDefault = isDefault };
        }

        private static Vec2 OnUnit(float degrees) { return Vec2.FromAngle(degrees * (float)Math.PI / 180f); }

        /// <summary>The standard layouts (DESIGN.md sections 6.4 to 6.7), the same constructions BindPoints.ForCircle uses.</summary>
        public static List<BindPointLayout> Defaults()
        {
            var nine = BindPoints.NinePointTriangle(Vec2.Zero, 1f);
            // Turn it the way BindPoints.ForCircle does, so a point faces the opponent.
            var raw = BindPoints.FromTriangle(Vec2.Zero, 1f, nine[0], nine[1], nine[2], SamePointFraction);
            float turn = float.MaxValue;
            foreach (var p in raw) { float d = BindPoints.WrapAngle(-p.Angle); if (System.Math.Abs(d) < System.Math.Abs(turn)) turn = d; }
            for (int i = 0; i < 3; i++) nine[i] = nine[i].Rotated(turn);
            return new List<BindPointLayout>
            {
                Ellipse("Ellipse", true),
                Triangle("Four-point circle", OnUnit(0f), OnUnit(90f), OnUnit(180f), true),
                Triangle("Six-point circle", OnUnit(0f), OnUnit(120f), OnUnit(240f), true),
                Triangle("Nine-point circle", nine[0], nine[1], nine[2], true),
            };
        }

        /// <summary>The vertices turned by <paramref name="radians"/> about the circle's centre (anticlockwise).</summary>
        public static Vec2[] Rotated(Vec2[] vertices, float radians)
        {
            var turned = new Vec2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) turned[i] = vertices[i].Rotated(radians);
            return turned;
        }

        public BindPointLayout Clone()
        {
            var copy = (BindPointLayout)MemberwiseClone();
            copy.Vertices = Vertices == null ? null : (Vec2[])Vertices.Clone();
            return copy;
        }
    }
}
