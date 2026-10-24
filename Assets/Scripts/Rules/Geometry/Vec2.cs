using System;

namespace RithmaticDuels.Rules.Geometry
{
    /// <summary>A 2D point or direction in board units. The rules core has no UnityEngine, so it has its own.</summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero { get { return new Vec2(0f, 0f); } }

        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y); } }
        public float LengthSquared { get { return X * X + Y * Y; } }

        /// <summary>Angle from the +X axis, in radians.</summary>
        public float Angle { get { return (float)Math.Atan2(Y, X); } }

        /// <summary>This vector turned 90 degrees anticlockwise.</summary>
        public Vec2 Perp { get { return new Vec2(-Y, X); } }

        public Vec2 Normalized
        {
            get
            {
                float length = Length;
                return length > 1e-9f ? new Vec2(X / length, Y / length) : Zero;
            }
        }

        public Vec2 Rotated(float radians)
        {
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);
            return new Vec2(X * cos - Y * sin, X * sin + Y * cos);
        }

        public static Vec2 FromAngle(float radians)
        {
            return new Vec2((float)Math.Cos(radians), (float)Math.Sin(radians));
        }

        public static float Dot(Vec2 a, Vec2 b) { return a.X * b.X + a.Y * b.Y; }
        public static float Cross(Vec2 a, Vec2 b) { return a.X * b.Y - a.Y * b.X; }
        public static float Distance(Vec2 a, Vec2 b) { return (a - b).Length; }

        /// <summary>Mirror a direction in a surface with this unit normal (angle of incidence = angle of reflection).</summary>
        public static Vec2 Reflect(Vec2 direction, Vec2 normal)
        {
            return direction - normal * (2f * Dot(direction, normal));
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.X + b.X, a.Y + b.Y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.X - b.X, a.Y - b.Y); }
        public static Vec2 operator -(Vec2 a) { return new Vec2(-a.X, -a.Y); }
        public static Vec2 operator *(Vec2 a, float s) { return new Vec2(a.X * s, a.Y * s); }
        public static Vec2 operator *(float s, Vec2 a) { return new Vec2(a.X * s, a.Y * s); }
        public static Vec2 operator /(Vec2 a, float s) { return new Vec2(a.X / s, a.Y / s); }

        public bool Equals(Vec2 other) { return X == other.X && Y == other.Y; }
        public override bool Equals(object obj) { return obj is Vec2 && Equals((Vec2)obj); }
        public override int GetHashCode() { return X.GetHashCode() * 397 ^ Y.GetHashCode(); }
        public override string ToString() { return "(" + X.ToString("0.##") + ", " + Y.ToString("0.##") + ")"; }
    }
}
