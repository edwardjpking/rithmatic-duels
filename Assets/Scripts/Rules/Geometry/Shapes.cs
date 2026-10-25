using System;

namespace RithmaticDuels.Rules.Geometry
{
    /// <summary>Ray and line intersection helpers. Distances are along a unit direction.</summary>
    public static class Intersect
    {
        /// <summary>Hits closer than this are ignored, so a line that just bounced doesn't hit the same surface again.</summary>
        public const float Epsilon = 1e-4f;

        /// <summary>Nearest hit of a ray on a segment, or false. Normal faces back toward the ray.</summary>
        public static bool RaySegment(Vec2 origin, Vec2 direction, Segment segment, out float distance, out Vec2 normal)
        {
            distance = 0f;
            normal = Vec2.Zero;
            Vec2 edge = segment.B - segment.A;
            float denominator = Vec2.Cross(direction, edge);
            if (Math.Abs(denominator) < 1e-9f) return false; // parallel

            Vec2 toStart = segment.A - origin;
            float t = Vec2.Cross(toStart, edge) / denominator;
            float u = Vec2.Cross(toStart, direction) / denominator;
            if (t < Epsilon || u < 0f || u > 1f) return false;

            distance = t;
            normal = edge.Perp.Normalized;
            if (Vec2.Dot(normal, direction) > 0f) normal = -normal;
            return true;
        }

        /// <summary>Nearest hit of a ray on a circle's outline (from outside or inside), or false.</summary>
        public static bool RayCircle(Vec2 origin, Vec2 direction, Vec2 centre, float radius, out float distance)
        {
            return RayEllipseLocal(origin - centre, direction, radius, radius, out distance);
        }

        /// <summary>Ray against an axis-aligned ellipse centred on the origin, with semi-axes a (x) and b (y).</summary>
        public static bool RayEllipseLocal(Vec2 origin, Vec2 direction, float a, float b, out float distance)
        {
            distance = 0f;
            float ia = 1f / (a * a);
            float ib = 1f / (b * b);
            double qa = direction.X * direction.X * ia + direction.Y * direction.Y * ib;
            double qb = 2.0 * (origin.X * direction.X * ia + origin.Y * direction.Y * ib);
            double qc = origin.X * origin.X * ia + origin.Y * origin.Y * ib - 1.0;
            double discriminant = qb * qb - 4.0 * qa * qc;
            if (qa < 1e-12 || discriminant < 0.0) return false;

            double root = Math.Sqrt(discriminant);
            double near = (-qb - root) / (2.0 * qa);
            double far = (-qb + root) / (2.0 * qa);
            if (near >= Epsilon) distance = (float)near;
            else if (far >= Epsilon) distance = (float)far;
            else return false;
            return true;
        }

        /// <summary>
        /// Parameters t where the line p + t·d meets a circle, in increasing order. 0, 1 or 2 values.
        /// Used for bind points, where d is a triangle edge (t in [0, 1]) or a full altitude line.
        /// </summary>
        public static float[] LineCircle(Vec2 p, Vec2 d, Vec2 centre, float radius)
        {
            Vec2 f = p - centre;
            double qa = Vec2.Dot(d, d);
            double qb = 2.0 * Vec2.Dot(f, d);
            double qc = Vec2.Dot(f, f) - radius * radius;
            double discriminant = qb * qb - 4.0 * qa * qc;
            if (qa < 1e-12 || discriminant < -1e-9 * qa) return new float[0];
            if (discriminant < 0.0) discriminant = 0.0;
            double root = Math.Sqrt(discriminant);
            if (root < 1e-9) return new[] { (float)(-qb / (2.0 * qa)) };
            return new[] { (float)((-qb - root) / (2.0 * qa)), (float)((-qb + root) / (2.0 * qa)) };
        }
    }

    [Serializable]
    public struct Segment
    {
        public Vec2 A;
        public Vec2 B;

        public Segment(Vec2 a, Vec2 b)
        {
            A = a;
            B = b;
        }

        public float Length { get { return Vec2.Distance(A, B); } }
    }

    /// <summary>
    /// The outline of a Line of Warding: an ellipse, or a circle when both semi-axes are equal.
    /// Points on it are addressed by the angle parameter t in [0, 2π): (a cos t, b sin t) before rotation.
    /// </summary>
    public sealed class WardShape
    {
        public Vec2 Centre { get; private set; }
        /// <summary>Semi-axis along <see cref="Rotation"/>.</summary>
        public float A { get; private set; }
        public float B { get; private set; }
        /// <summary>Direction of the A axis, in radians.</summary>
        public float Rotation { get; private set; }

        public bool IsCircle { get { return Math.Abs(A - B) < 1e-6f; } }

        public static WardShape Circle(Vec2 centre, float radius)
        {
            return new WardShape { Centre = centre, A = radius, B = radius };
        }

        public static WardShape Ellipse(Vec2 centre, float semiMajor, float semiMinor, float rotation)
        {
            return new WardShape { Centre = centre, A = semiMajor, B = semiMinor, Rotation = rotation };
        }

        public Vec2 PointAt(float t)
        {
            var local = new Vec2(A * (float)Math.Cos(t), B * (float)Math.Sin(t));
            return Centre + local.Rotated(Rotation);
        }

        /// <summary>Length of the outline, measured along 128 straight pieces.</summary>
        public float Perimeter()
        {
            const int steps = 128;
            float length = 0f;
            Vec2 previous = PointAt(0f);
            for (int i = 1; i <= steps; i++)
            {
                Vec2 point = PointAt(2f * (float)Math.PI * i / steps);
                length += Vec2.Distance(previous, point);
                previous = point;
            }
            return length;
        }

        public Vec2 OutwardNormalAt(float t)
        {
            var local = new Vec2((float)Math.Cos(t) / A, (float)Math.Sin(t) / B);
            return local.Rotated(Rotation).Normalized;
        }

        /// <summary>Curvature (1 / radius of curvature) at t. A circle's is 1 / r everywhere.</summary>
        public float CurvatureAt(float t)
        {
            double sin = Math.Sin(t);
            double cos = Math.Cos(t);
            double denominator = Math.Pow(A * A * sin * sin + B * B * cos * cos, 1.5);
            return (float)(A * B / denominator);
        }

        /// <summary>The parameter of the outline point in the direction of <paramref name="point"/> from the centre.</summary>
        public float ParamOf(Vec2 point)
        {
            var local = (point - Centre).Rotated(-Rotation);
            double t = Math.Atan2(local.Y / B, local.X / A);
            return (float)(t < 0 ? t + 2.0 * Math.PI : t);
        }

        public bool Raycast(Vec2 origin, Vec2 direction, out float distance)
        {
            var localOrigin = (origin - Centre).Rotated(-Rotation);
            var localDirection = direction.Rotated(-Rotation);
            return Intersect.RayEllipseLocal(localOrigin, localDirection, A, B, out distance);
        }

        public bool Contains(Vec2 point)
        {
            var local = (point - Centre).Rotated(-Rotation);
            return local.X * local.X / (A * A) + local.Y * local.Y / (B * B) <= 1f;
        }
    }
}
