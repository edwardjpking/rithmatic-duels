namespace RithmaticDuels.Rules.Geometry
{
    public enum PlacementKind
    {
        /// <summary>Let the board choose a sensible default (used by tests and anything that skips placement).</summary>
        Default,
        /// <summary>LoW (not the main one) or Mark's Cross: a circle touching the main LoW at a bind point, outside it.</summary>
        AtBindPoint,
        /// <summary>LoF joining two bind points of the main LoW.</summary>
        BetweenBindPoints,
        /// <summary>LoF lying tangent to the main LoW at one bind point.</summary>
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
    /// Bind points are indices into the owner's main LoW bind points, in the order
    /// <see cref="ChalkBoard.BindPointsOf"/> returns them.
    /// </summary>
    public sealed class Placement
    {
        public PlacementKind Kind;
        public int BindPointA = -1;
        public int BindPointB = -1;
        public Vec2 PointA;
        public Vec2 PointB;

        public static Placement Default() { return new Placement { Kind = PlacementKind.Default }; }

        public static Placement AtBindPoint(int bindPoint)
        {
            return new Placement { Kind = PlacementKind.AtBindPoint, BindPointA = bindPoint };
        }

        public static Placement BetweenBindPoints(int a, int b)
        {
            return new Placement { Kind = PlacementKind.BetweenBindPoints, BindPointA = a, BindPointB = b };
        }

        public static Placement TangentAt(int bindPoint)
        {
            return new Placement { Kind = PlacementKind.TangentAtBindPoint, BindPointA = bindPoint };
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
