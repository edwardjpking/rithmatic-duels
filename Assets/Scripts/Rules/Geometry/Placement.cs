namespace RithmaticDuels.Rules.Geometry
{
    public enum PlacementKind
    {
        /// <summary>Let the board choose a sensible default (used by tests and anything that skips placement).</summary>
        Default,
        /// <summary>The main LoW, centred at PointA. An ellipse (2 bind points) has semi-minor axis SemiMinor.</summary>
        MainWard,
        /// <summary>
        /// LoW (not the main one) or Mark's Cross touching the main LoW at a bind point, outside it. A LoW
        /// with 2 bind points is an ellipse with semi-minor axis Size; any other is a circle of radius Size
        /// (0 for the default). A Mark's Cross has a fixed size.
        /// </summary>
        AtBindPoint,
        /// <summary>LoF joining two bind points of the main LoW.</summary>
        BetweenBindPoints,
        /// <summary>LoF lying tangent to the main LoW at one bind point, Length long (0 for the default).</summary>
        TangentAtBindPoint,
        /// <summary>LoF from PointA to PointB.</summary>
        FreeSegment,
        /// <summary>LoV drawn at PointA, travelling toward PointB.</summary>
        Aimed,
        /// <summary>Unbound chalkling at PointA.</summary>
        FreePoint,
    }

    /// <summary>
    /// Where a drawing will go, chosen when it is started and kept secret like the rest of its spec.
    /// Bind points are indices into the bind points of all the owner's LoW, in the order
    /// <see cref="ChalkBoard.BindPointsOf"/> returns them.
    /// </summary>
    public sealed class Placement
    {
        public PlacementKind Kind;
        public int BindPointA = -1;
        public int BindPointB = -1;
        public Vec2 PointA;
        public Vec2 PointB;
        public float SemiMinor;
        public float Length;
        public float Size;
        /// <summary>Overall scale of an attached ellipse (both axes). 0 means 1.</summary>
        public float Scale;
        /// <summary>Where BindPointA and BindPointB were when the drawing was started (set by the board).</summary>
        public Vec2? BindPositionA;
        public Vec2? BindPositionB;

        public static Placement Default() { return new Placement { Kind = PlacementKind.Default }; }

        public static Placement MainWard(Vec2 centre, float semiMinor = 0f)
        {
            return new Placement { Kind = PlacementKind.MainWard, PointA = centre, SemiMinor = semiMinor };
        }

        public static Placement AtBindPoint(int bindPoint, float size = 0f, float scale = 0f)
        {
            return new Placement { Kind = PlacementKind.AtBindPoint, BindPointA = bindPoint, Size = size, Scale = scale };
        }

        public static Placement BetweenBindPoints(int a, int b)
        {
            return new Placement { Kind = PlacementKind.BetweenBindPoints, BindPointA = a, BindPointB = b };
        }

        public static Placement TangentAt(int bindPoint, float length = 0f)
        {
            return new Placement { Kind = PlacementKind.TangentAtBindPoint, BindPointA = bindPoint, Length = length };
        }

        public static Placement Segment(Vec2 a, Vec2 b)
        {
            return new Placement { Kind = PlacementKind.FreeSegment, PointA = a, PointB = b };
        }

        public static Placement Aimed(Vec2 from, Vec2 toward)
        {
            return new Placement { Kind = PlacementKind.Aimed, PointA = from, PointB = toward };
        }

        public static Placement At(Vec2 point)
        {
            return new Placement { Kind = PlacementKind.FreePoint, PointA = point };
        }

        public Placement Clone() { return (Placement)MemberwiseClone(); }
    }
}
