using System;
using System.Collections.Generic;
using RithmaticDuels.Rules.Geometry;

namespace RithmaticDuels.Rules.Presets
{
    /// <summary>
    /// A bind point of a line in a preset: <see cref="Index"/> into that LoW's own bind points (in the order the
    /// board gives them), on the part with id <see cref="Component"/>. Component 0 is the main LoW.
    /// </summary>
    [Serializable]
    public struct BindRef
    {
        public int Component;
        public int Index;

        public BindRef(int component, int index) { Component = component; Index = index; }
        public static BindRef Main(int index) { return new BindRef(DefencePreset.MainWardId, index); }
    }

    /// <summary>
    /// One part of a saved defence (DESIGN.md section 6.8): a LoW, LoF or LoM and where it goes. Bind points are
    /// <see cref="BindRef"/>s, so a part never depends on the board's global numbering. Free positions are in the
    /// preset's frame: origin at the main LoW's centre, +X facing the opponent, in board units.
    /// Its parents are the parts whose bind points it uses (and the main LoW, always).
    /// </summary>
    [Serializable]
    public sealed class DefenceComponent
    {
        /// <summary>Unique within the preset and never 0 (the main LoW). Kept when parts are reordered.</summary>
        public int Id;
        public LineType Type;
        /// <summary>
        /// Where it goes. LoW: AtBindPoint. LoF: BetweenBindPoints, TangentAtBindPoint or FreeSegment.
        /// LoM: AtBindPoint (bound, Defend) or FreePoint.
        /// </summary>
        public PlacementKind Kind;
        public BindRef A;
        public BindRef B;
        public Vec2 PointA;
        public Vec2 PointB;

        // LoW
        public int BindPoints;
        public bool IsMarksCross;
        public float Size;
        public float Scale;
        public int Join;
        // LoF tangent
        public float Length;
        // LoM
        public int DurabilityBuffs;
        public int DamageBuffs;
        public int SpeedBuffs;
        public Glyph Glyph;
        public ChalklingStat PositiveModifierStat;

        public DrawingPace Pace = DrawingPace.Normal;

        public bool UsesA { get { return Kind == PlacementKind.AtBindPoint || Kind == PlacementKind.BetweenBindPoints || Kind == PlacementKind.TangentAtBindPoint; } }
        public bool UsesB { get { return Kind == PlacementKind.BetweenBindPoints; } }

        /// <summary>The ids of the parts this one must be built after.</summary>
        public List<int> Parents()
        {
            var parents = new List<int> { DefencePreset.MainWardId };
            if (UsesA && !parents.Contains(A.Component)) parents.Add(A.Component);
            if (UsesB && !parents.Contains(B.Component)) parents.Add(B.Component);
            return parents;
        }

        /// <summary>The drawing spec, before the board measures travel time and length.</summary>
        public DrawingSpec Spec(RulesConfig config)
        {
            switch (Type)
            {
                case LineType.Warding:
                    return IsMarksCross ? DrawingSpec.MarksCross(config) : DrawingSpec.Warding(BindPoints);
                case LineType.Forbiddance:
                    return DrawingSpec.Forbiddance(Kind == PlacementKind.BetweenBindPoints ? 2 : Kind == PlacementKind.TangentAtBindPoint ? 1 : 0);
                case LineType.Making:
                    return DrawingSpec.Making(DurabilityBuffs, DamageBuffs, SpeedBuffs, Glyph,
                        Kind == PlacementKind.AtBindPoint ? 1 : 0, PositiveModifierStat);
                default:
                    throw new InvalidOperationException("A preset can't hold a " + Type + ".");
            }
        }

        public DefenceComponent Clone() { return (DefenceComponent)MemberwiseClone(); }

        public static DefenceComponent Ward(int id, BindRef at, int bindPoints, float size = 0f, int join = 0)
        {
            return new DefenceComponent { Id = id, Type = LineType.Warding, Kind = PlacementKind.AtBindPoint, A = at, BindPoints = bindPoints, Size = size, Join = join };
        }

        public static DefenceComponent MarksCross(int id, BindRef at)
        {
            return new DefenceComponent { Id = id, Type = LineType.Warding, Kind = PlacementKind.AtBindPoint, A = at, IsMarksCross = true };
        }

        public static DefenceComponent ForbiddanceBetween(int id, BindRef a, BindRef b)
        {
            return new DefenceComponent { Id = id, Type = LineType.Forbiddance, Kind = PlacementKind.BetweenBindPoints, A = a, B = b };
        }

        public static DefenceComponent ForbiddanceTangent(int id, BindRef at, float length = 0f)
        {
            return new DefenceComponent { Id = id, Type = LineType.Forbiddance, Kind = PlacementKind.TangentAtBindPoint, A = at, Length = length };
        }

        public static DefenceComponent ForbiddanceFree(int id, Vec2 a, Vec2 b)
        {
            return new DefenceComponent { Id = id, Type = LineType.Forbiddance, Kind = PlacementKind.FreeSegment, PointA = a, PointB = b };
        }

        public static DefenceComponent BoundChalkling(int id, BindRef at, int durability = 0, int damage = 0, int speed = 0)
        {
            return new DefenceComponent { Id = id, Type = LineType.Making, Kind = PlacementKind.AtBindPoint, A = at, Glyph = Glyph.Defend, DurabilityBuffs = durability, DamageBuffs = damage, SpeedBuffs = speed };
        }

        public static DefenceComponent FreeChalkling(int id, Vec2 at, Glyph glyph = Glyph.Attack, int durability = 0, int damage = 0, int speed = 0)
        {
            return new DefenceComponent { Id = id, Type = LineType.Making, Kind = PlacementKind.FreePoint, PointA = at, Glyph = glyph, DurabilityBuffs = durability, DamageBuffs = damage, SpeedBuffs = speed };
        }
    }

    /// <summary>
    /// A saved defence (DESIGN.md section 6.8): a main LoW on a bind point layout, then the parts in build order.
    /// The main LoW is always built first and is not in <see cref="Components"/>; its id is <see cref="MainWardId"/>.
    /// Plain data so it can be saved as JSON.
    /// </summary>
    [Serializable]
    public sealed class DefencePreset
    {
        public const int MainWardId = 0;

        public string Name = "";
        /// <summary>The main LoW's bind points. A copy is kept so the preset still works if the layout is deleted.</summary>
        public BindPointLayout Layout = new BindPointLayout();
        /// <summary>Main ellipse semi-minor axis; 0 for the default.</summary>
        public float MainSemiMinor;
        /// <summary>The parts, in build order.</summary>
        public List<DefenceComponent> Components = new List<DefenceComponent>();
        public bool IsDefault;

        public int IndexOf(int id)
        {
            for (int i = 0; i < Components.Count; i++) if (Components[i].Id == id) return i;
            return -1;
        }

        public DefenceComponent Find(int id)
        {
            int index = IndexOf(id);
            return index >= 0 ? Components[index] : null;
        }

        /// <summary>A free id for a new part.</summary>
        public int NextId()
        {
            int max = MainWardId;
            foreach (var c in Components) max = Math.Max(max, c.Id);
            return max + 1;
        }

        /// <summary>The main LoW's spec, with the layout if it is a custom one.</summary>
        public DrawingSpec MainWardSpec()
        {
            var spec = DrawingSpec.Warding(Layout.BindPointCount);
            // The shipped layouts are the board's standard ones, so a default layout is drawn exactly as by hand.
            if (!Layout.IsEllipse && !Layout.IsDefault) spec.Layout = Layout.Clone();
            return spec;
        }

        /// <summary>
        /// The range of positions the part at <paramref name="index"/> may be dragged to (DESIGN.md section 6.8):
        /// never above any of its parents, never below any part that uses it.
        /// </summary>
        public void MoveRange(int index, out int min, out int max)
        {
            var moving = Components[index];
            min = 0;
            max = Components.Count - 1;
            var parents = moving.Parents();
            for (int i = 0; i < Components.Count; i++)
            {
                if (i == index) continue;
                if (i < index && parents.Contains(Components[i].Id)) min = Math.Max(min, i + 1);
                if (i > index && Components[i].Parents().Contains(moving.Id)) max = Math.Min(max, i - 1);
            }
        }

        public bool CanMove(int from, int to)
        {
            if (from < 0 || from >= Components.Count || to < 0 || to >= Components.Count) return false;
            int min, max;
            MoveRange(from, out min, out max);
            return to >= min && to <= max;
        }

        /// <summary>Move a part in the build order, if allowed. Returns whether it moved.</summary>
        public bool Move(int from, int to)
        {
            if (!CanMove(from, to)) return false;
            var part = Components[from];
            Components.RemoveAt(from);
            Components.Insert(to, part);
            return true;
        }

        /// <summary>
        /// Removes a part and every part built on it (like destroying a LoW destroys its children). Returns the
        /// ids removed.
        /// </summary>
        public List<int> RemoveWithChildren(int id)
        {
            var removed = new List<int> { id };
            for (int i = 0; i < Components.Count; i++)
                foreach (var parent in Components[i].Parents())
                    if (removed.Contains(parent) && !removed.Contains(Components[i].Id)) { removed.Add(Components[i].Id); break; }
            Components.RemoveAll(c => removed.Contains(c.Id));
            return removed;
        }

        /// <summary>
        /// Null if the preset's structure is sound, otherwise why not. Doesn't check that the parts fit on the
        /// board; <see cref="DefenceCheck.Problem"/> does both.
        /// </summary>
        public string StructureProblem(RulesConfig config)
        {
            if (string.IsNullOrWhiteSpace(Name)) return "Give the defence a name.";
            if (Layout == null) return "Pick a bind point layout.";
            string layoutProblem = Layout.Problem(config);
            if (layoutProblem != null) return layoutProblem;

            var seen = new HashSet<int> { MainWardId };
            var wardBindPoints = new Dictionary<int, int> { { MainWardId, Layout.BindPointCount } };
            for (int i = 0; i < Components.Count; i++)
            {
                var c = Components[i];
                string where = "Part " + (i + 1);
                if (c == null) return where + " is empty.";
                if (seen.Contains(c.Id)) return where + " has the same id as another part.";
                if (c.Type == LineType.Vigor) return where + ": a defence can't hold a Line of Vigor.";
                if (!KindFits(c)) return where + " can't be placed that way.";
                foreach (var parent in c.Parents())
                    if (!seen.Contains(parent)) return where + " is built before the line it is bound to.";
                if (c.UsesA && !BindPointExists(c.A, wardBindPoints)) return where + " is bound to a bind point that doesn't exist.";
                if (c.UsesB && !BindPointExists(c.B, wardBindPoints)) return where + " is bound to a bind point that doesn't exist.";
                if (c.Type == LineType.Making && c.Kind == PlacementKind.AtBindPoint && c.Glyph != Glyph.Defend)
                    return where + ": a bound chalkling must carry the Defend glyph.";
                seen.Add(c.Id);
                if (c.Type == LineType.Warding)
                    wardBindPoints[c.Id] = c.IsMarksCross ? config.Warding.MarksCrossBindPoints : c.BindPoints;
            }
            return null;
        }

        private static bool KindFits(DefenceComponent c)
        {
            switch (c.Type)
            {
                case LineType.Warding: return c.Kind == PlacementKind.AtBindPoint;
                case LineType.Forbiddance:
                    return c.Kind == PlacementKind.BetweenBindPoints || c.Kind == PlacementKind.TangentAtBindPoint || c.Kind == PlacementKind.FreeSegment;
                case LineType.Making: return c.Kind == PlacementKind.AtBindPoint || c.Kind == PlacementKind.FreePoint;
                default: return false;
            }
        }

        private static bool BindPointExists(BindRef point, Dictionary<int, int> wardBindPoints)
        {
            int count;
            return wardBindPoints.TryGetValue(point.Component, out count) && point.Index >= 0 && point.Index < count;
        }

        public DefencePreset Clone()
        {
            var copy = (DefencePreset)MemberwiseClone();
            copy.Layout = Layout == null ? null : Layout.Clone();
            copy.Components = new List<DefenceComponent>();
            foreach (var c in Components) copy.Components.Add(c.Clone());
            return copy;
        }
    }
}
