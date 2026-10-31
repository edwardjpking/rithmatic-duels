using System;
using System.Collections.Generic;

namespace RithmaticDuels.Rules.Geometry
{
    /// <summary>Bind point construction (DESIGN.md section 6.1).</summary>
    public static class BindPoints
    {
        /// <summary>
        /// The bind points a triangle gives a circular LoW: where the triangle's edges cross the circle,
        /// plus where each altitude, extended to a full line, crosses it. At most 12. Points closer
        /// together than <paramref name="mergeDistance"/> count once.
        /// </summary>
        public static List<Vec2> FromTriangle(Vec2 centre, float radius, Vec2 a, Vec2 b, Vec2 c, float mergeDistance = 1e-3f)
        {
            var points = new List<Vec2>();
            var vertices = new[] { a, b, c };
            for (int i = 0; i < 3; i++)
            {
                Vec2 from = vertices[i];
                Vec2 to = vertices[(i + 1) % 3];
                Vec2 opposite = vertices[(i + 2) % 3];

                // Edge from -> to, as a segment.
                foreach (float t in Intersect.LineCircle(from, to - from, centre, radius))
                    if (t >= -1e-5f && t <= 1f + 1e-5f) Add(points, from + (to - from) * t, mergeDistance);

                // Altitude through the opposite vertex: perpendicular to this edge, as a full line.
                Vec2 altitude = (to - from).Perp;
                foreach (float t in Intersect.LineCircle(opposite, altitude, centre, radius))
                    Add(points, opposite + altitude * t, mergeDistance);
            }
            return points;
        }

        /// <summary>
        /// Default bind points for a circle with this many points, one of them facing <paramref name="facing"/>.
        /// 4, 6 and 9 come from the standard triangle constructions; any other count is spaced evenly.
        /// </summary>
        public static List<Vec2> ForCircle(int count, Vec2 centre, float radius, Vec2 facing)
        {
            List<Vec2> points;
            switch (count)
            {
                case 4:
                    // Right isosceles triangle inscribed in the circle: its vertices plus the far end of
                    // the altitude from the right angle, through the centre.
                    points = FromTriangle(centre, radius, OnCircle(centre, radius, 0f), OnCircle(centre, radius, 90f), OnCircle(centre, radius, 180f));
                    break;
                case 6:
                    // Inscribed equilateral triangle: its vertices plus the far ends of its altitudes.
                    points = FromTriangle(centre, radius, OnCircle(centre, radius, 0f), OnCircle(centre, radius, 120f), OnCircle(centre, radius, 240f));
                    break;
                case 9:
                    points = NinePointCircle(centre, radius);
                    break;
                default:
                    points = new List<Vec2>();
                    for (int i = 0; i < Math.Max(0, count); i++) points.Add(OnCircle(centre, radius, 360f * i / count));
                    break;
            }
            return FaceAndSort(points, centre, facing);
        }

        /// <summary>
        /// The bind points of a circle attached to another LoW, with one of its own bind points where it touches,
        /// in the direction <paramref name="toward"/>. <paramref name="join"/> picks which of its points that is
        /// (DESIGN.md section 4.1): 0 is the default, and each further choice turns the set to a different-looking
        /// arrangement. Choices wrap round; a symmetric set (4 or 6 points) has only one.
        /// </summary>
        public static List<Vec2> ForAttachedCircle(int count, Vec2 centre, float radius, Vec2 toward, int join)
        {
            var options = JoinOptions(count, centre, radius, toward);
            if (options.Count == 0) return new List<Vec2>();
            int index = ((join % options.Count) + options.Count) % options.Count;
            return options[index];
        }

        /// <summary>How many different arrangements an attached circle with this many bind points can be turned to.</summary>
        public static int JoinChoices(int count)
        {
            return Math.Max(1, JoinOptions(count, Vec2.Zero, 1f, new Vec2(1f, 0f)).Count);
        }

        /// <summary>Each of the set's points turned to the joining spot in turn, leaving out arrangements that look the same.</summary>
        private static List<List<Vec2>> JoinOptions(int count, Vec2 centre, float radius, Vec2 toward)
        {
            var basic = ForCircle(count, centre, radius, toward);
            var options = new List<List<Vec2>>();
            float target = toward.Angle;
            foreach (var point in basic)
            {
                float turn = WrapAngle(target - (point - centre).Angle);
                var turned = new List<Vec2>();
                foreach (var other in basic) turned.Add(centre + (other - centre).Rotated(turn));
                turned = FaceAndSort(turned, centre, toward);
                bool seen = false;
                foreach (var option in options) seen |= SameSet(option, turned, radius * 1e-3f);
                if (!seen) options.Add(turned);
            }
            return options;
        }

        private static bool SameSet(List<Vec2> a, List<Vec2> b, float tolerance)
        {
            if (a.Count != b.Count) return false;
            foreach (var p in a)
            {
                bool found = false;
                foreach (var q in b) found |= Vec2.Distance(p, q) < tolerance;
                if (!found) return false;
            }
            return true;
        }

        /// <summary>An ellipse's two bind points: the ends of its major axis, where curvature is greatest.</summary>
        public static List<Vec2> ForEllipse(WardShape ellipse)
        {
            return new List<Vec2> { ellipse.PointAt(0f), ellipse.PointAt((float)Math.PI) };
        }

        /// <summary>
        /// Nine bind points: a scalene acute triangle drawn so the LoW is its nine-point circle. Each edge
        /// crosses the circle at its midpoint and the foot of an altitude, and each altitude line crosses it
        /// again halfway from its vertex to the orthocentre.
        /// </summary>
        private static List<Vec2> NinePointCircle(Vec2 centre, float radius)
        {
            var v = NinePointTriangle(centre, radius);
            return FromTriangle(centre, radius, v[0], v[1], v[2]);
        }

        /// <summary>The vertices of the triangle whose nine-point circle is this circle (see <see cref="NinePointCircle"/>).</summary>
        public static Vec2[] NinePointTriangle(Vec2 centre, float radius)
        {
            // Angles of the triangle, in degrees. Any acute, scalene choice works.
            const float angleA = 50f, angleB = 60f, angleC = 70f;
            // Circumradius is twice the nine-point radius. Vertex positions on the circumcircle are set
            // by the central angles, which are twice the opposite triangle angles.
            float circumradius = 2f * radius;
            Vec2 a = OnCircle(Vec2.Zero, circumradius, 0f);
            Vec2 b = OnCircle(Vec2.Zero, circumradius, 2f * angleC);
            Vec2 c = OnCircle(Vec2.Zero, circumradius, 2f * (angleC + angleA));
            // With the circumcentre at the origin, the orthocentre is a + b + c and the nine-point
            // centre is halfway to it.
            Vec2 shift = centre - (a + b + c) * 0.5f;
            if (Math.Abs(angleA + angleB + angleC - 180f) > 1e-3f) throw new InvalidOperationException("Triangle angles must sum to 180.");
            return new[] { a + shift, b + shift, c + shift };
        }

        /// <summary>Sort points anticlockwise round the centre, starting from the one at (or just after) <paramref name="facing"/>. Doesn't move them.</summary>
        public static List<Vec2> Sorted(List<Vec2> points, Vec2 centre, Vec2 facing)
        {
            var sorted = new List<Vec2>(points);
            float facingAngle = facing.Angle;
            sorted.Sort((p, q) => Positive(WrapAngle((p - centre).Angle - facingAngle)).CompareTo(Positive(WrapAngle((q - centre).Angle - facingAngle))));
            return sorted;
        }

        /// <summary>Rotate the set about the centre so the point nearest <paramref name="facing"/> faces it exactly, then sort anticlockwise from there.</summary>
        private static List<Vec2> FaceAndSort(List<Vec2> points, Vec2 centre, Vec2 facing)
        {
            if (points.Count == 0) return points;
            float facingAngle = facing.Angle;
            float best = float.MaxValue;
            float turn = 0f;
            foreach (var point in points)
            {
                float delta = WrapAngle(facingAngle - (point - centre).Angle);
                if (Math.Abs(delta) < best) { best = Math.Abs(delta); turn = delta; }
            }

            var rotated = new List<Vec2>();
            foreach (var point in points) rotated.Add(centre + (point - centre).Rotated(turn));
            rotated.Sort((p, q) => Positive(WrapAngle((p - centre).Angle - facingAngle)).CompareTo(Positive(WrapAngle((q - centre).Angle - facingAngle))));
            return rotated;
        }

        private static void Add(List<Vec2> points, Vec2 point, float mergeDistance)
        {
            foreach (var existing in points)
                if (Vec2.Distance(existing, point) < mergeDistance) return;
            points.Add(point);
        }

        private static Vec2 OnCircle(Vec2 centre, float radius, float degrees)
        {
            return centre + Vec2.FromAngle(degrees * (float)Math.PI / 180f) * radius;
        }

        /// <summary>Wrap to (-π, π].</summary>
        public static float WrapAngle(float radians)
        {
            double twoPi = 2.0 * Math.PI;
            double wrapped = radians % twoPi;
            if (wrapped <= -Math.PI) wrapped += twoPi;
            if (wrapped > Math.PI) wrapped -= twoPi;
            return (float)wrapped;
        }

        private static float Positive(float radians)
        {
            // Treat tiny negatives as 0 so the facing point sorts first.
            return radians < -1e-4f ? radians + 2f * (float)Math.PI : Math.Max(0f, radians);
        }
    }
}
