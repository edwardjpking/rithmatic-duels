using System;
using System.Collections.Generic;

namespace RithmaticDuels.Rules.Geometry
{
    /// <summary>The path one LoV took during the last tick, for drawing its trail.</summary>
    public sealed class VigorTrail
    {
        public Player Owner;
        public List<Vec2> Points = new List<Vec2>();
    }

    /// <summary>
    /// The continuous 2D board (DESIGN.md section 1.1) and step 2 of tick resolution: where every
    /// completed line sits, how Lines of Vigor fly, reflect off Forbiddance and hit Warding,
    /// chalklings and each other, and per-point Warding durability.
    ///
    /// The rules core stays geometry-free: this class only reads the duel's lines and reports
    /// collisions through <see cref="MovementResult"/>. Where each drawing goes is chosen when it is
    /// started (<see cref="SetPendingPlacement"/>) and kept here until the line completes.
    /// </summary>
    public sealed class ChalkBoard : IMovementResolver
    {
        private const int MaxEventsPerTick = 1000;

        private readonly RulesConfig _config;
        private readonly SortedDictionary<int, LineBody> _bodies = new SortedDictionary<int, LineBody>();
        private readonly PendingPlacement[] _pending = new PendingPlacement[2];
        private readonly List<string> _notes = new List<string>();
        private readonly List<VigorTrail> _trails = new List<VigorTrail>();
        private readonly WorkSketch[] _sketches = new WorkSketch[2];

        private struct PendingPlacement
        {
            public bool IsSet;
            public LineType Type;
            public Placement Placement;
        }

        public ChalkBoard(RulesConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            _config = config;
            CentreWallUp = CentreWallStandsOn(1);
        }

        /// <summary>
        /// Whether the centre LoF is up (DESIGN.md section 1.1): during a tick, for that tick; between ticks,
        /// for the turn being chosen. It belongs to neither player, reflects LoV and can't be damaged.
        /// </summary>
        public bool CentreWallUp { get; private set; }

        /// <summary>The centre LoF, along the line between the two halves.</summary>
        public Segment CentreWall { get { return new Segment(new Vec2(Width / 2f, 0f), new Vec2(Width / 2f, Height)); } }

        private bool CentreWallStandsOn(int turn)
        {
            return _config.CentreWall.Enabled && turn <= _config.CentreWall.Turns;
        }

        public RulesConfig Config { get { return _config; } }
        public float Width { get { return _config.Board.Width; } }
        public float Height { get { return _config.Board.Height; } }

        /// <summary>Every line on the board, in the order they were completed.</summary>
        public IEnumerable<LineBody> Bodies { get { return _bodies.Values; } }

        /// <summary>What happened on the board during the last tick, in order, for the duel log.</summary>
        public IReadOnlyList<string> LastTickNotes { get { return _notes; } }

        public IReadOnlyList<VigorTrail> LastTickTrails { get { return _trails; } }

        /// <summary>
        /// What a player has under way, as everyone sees it on the board. Null if nothing. Taken at the end
        /// of each tick, so a choice made for the coming turn doesn't show until that turn resolves.
        /// </summary>
        public WorkSketch WorkInProgressOf(Player player) { return _sketches[(int)player]; }

        /// <summary>Centre of a player's main LoW: inset from their own short edge (A on the left).</summary>
        public Vec2 Home(Player player)
        {
            float x = player == Player.A ? _config.Board.MainWardInset : Width - _config.Board.MainWardInset;
            return new Vec2(x, Height / 2f);
        }

        /// <summary>Unit direction from a player's side toward the opponent's.</summary>
        public Vec2 Facing(Player player)
        {
            return player == Player.A ? new Vec2(1f, 0f) : new Vec2(-1f, 0f);
        }

        public bool OnBoard(Vec2 point)
        {
            return point.X >= 0f && point.X <= Width && point.Y >= 0f && point.Y <= Height;
        }

        public bool OnOwnHalf(Player player, Vec2 point)
        {
            if (!OnBoard(point)) return false;
            return player == Player.A ? point.X <= Width / 2f : point.X >= Width / 2f;
        }

        public WardBody MainWardOf(Player player)
        {
            foreach (var body in _bodies.Values)
            {
                var ward = body as WardBody;
                if (ward != null && ward.Owner == player && ward.Line.IsMainWard) return ward;
            }
            return null;
        }

        /// <summary>A player's LoW, main first, then the others in the order they were completed.</summary>
        private List<WardBody> WardsOf(Player player)
        {
            var wards = new List<WardBody>();
            var main = MainWardOf(player);
            if (main != null) wards.Add(main);
            var others = new List<WardBody>();
            foreach (var body in _bodies.Values)
            {
                var ward = body as WardBody;
                if (ward != null && ward.Owner == player && ward.Line.IsActive && !ward.Line.IsMainWard) others.Add(ward);
            }
            others.Sort((x, y) => x.LineId.CompareTo(y.LineId));
            wards.AddRange(others);
            return wards;
        }

        /// <summary>Two bind points this close together are the same point (where one LoW touches another).</summary>
        private const float SameBindPoint = 0.02f;

        /// <summary>
        /// Every bind point of all a player's LoW (DESIGN.md section 4.1): the main LoW's first, then each
        /// other LoW's in the order they were completed. A point two LoW share is listed once. Placements
        /// name bind points by their index here.
        /// </summary>
        public IReadOnlyList<Vec2> BindPointsOf(Player player)
        {
            var points = new List<Vec2>();
            foreach (var ward in WardsOf(player))
            {
                foreach (var point in ward.BindPoints)
                {
                    bool seen = false;
                    foreach (var existing in points) seen |= Vec2.Distance(existing, point) < SameBindPoint;
                    if (!seen) points.Add(point);
                }
            }
            return points;
        }

        /// <summary>The first of the player's LoW (main first) with a bind point at this point, or null.</summary>
        private WardBody WardWithBindPoint(Player player, Vec2 point)
        {
            foreach (var ward in WardsOf(player))
                foreach (var own in ward.BindPoints)
                    if (Vec2.Distance(own, point) < SameBindPoint) return ward;
            return null;
        }

        private static bool OnOutline(WardBody ward, Vec2 point)
        {
            return Vec2.Distance(ward.Shape.PointAt(ward.Shape.ParamOf(point)), point) < SameBindPoint;
        }

        /// <summary>How many of the player's LoW pass through this bind point (the LoW it belongs to included).</summary>
        public int WardsThrough(Player player, int bindPoint)
        {
            var points = BindPointsOf(player);
            if (bindPoint < 0 || bindPoint >= points.Count) return 0;
            int count = 0;
            foreach (var ward in WardsOf(player)) if (OnOutline(ward, points[bindPoint])) count++;
            return count;
        }

        /// <summary>True if no more LoW can be bound at this bind point (Warding.MaxWardsPerBindPoint).</summary>
        public bool BindPointFull(Player player, int bindPoint)
        {
            return WardsThrough(player, bindPoint) >= _config.Warding.MaxWardsPerBindPoint;
        }

        /// <summary>Direction pointing away from the LoW a bind point belongs to, at that point.</summary>
        public Vec2 BindPointNormal(Player player, Vec2 point)
        {
            var ward = WardWithBindPoint(player, point);
            if (ward == null) return Facing(player);
            return ward.Shape.OutwardNormalAt(ward.Shape.ParamOf(point));
        }

        public LineBody BodyOf(int lineId)
        {
            LineBody body;
            return _bodies.TryGetValue(lineId, out body) ? body : null;
        }

        /// <summary>Record where the drawing a player is starting will go. Call it with every Start drawing.</summary>
        public void SetPendingPlacement(Player player, DrawingSpec spec, Placement placement)
        {
            var copy = (placement ?? Placement.Default()).Clone();
            // Remember where the chosen bind points are, so the drawing still finds them if a LoW listed
            // before them is destroyed while it is drawn.
            var points = BindPointsOf(player);
            if (copy.BindPointA >= 0 && copy.BindPointA < points.Count) copy.BindPositionA = points[copy.BindPointA];
            if (copy.BindPointB >= 0 && copy.BindPointB < points.Count) copy.BindPositionB = points[copy.BindPointB];
            _pending[(int)player] = new PendingPlacement { IsSet = true, Type = spec.Type, Placement = copy };
        }

        /// <summary>
        /// Returns null if this placement works for this drawing now, otherwise why not. The spec's bind
        /// point count must match the placement, since it sets draw time and durability.
        /// </summary>
        public string ValidatePlacement(Player player, DrawingSpec spec, Placement placement)
        {
            if (spec == null) return "No drawing spec given.";
            if (placement == null) return "Choose where to draw it.";
            if (placement.Kind == PlacementKind.Default) return null;
            string problem = PlacementProblem(player, spec, placement);
            if (problem != null) return problem;
            if (spec.TravelTurns != TravelTurns(player, placement))
                return "The travel time doesn't match how far this is from your main Line of Warding.";
            if ((spec.Type == LineType.Forbiddance || spec.Type == LineType.Warding)
                && Math.Abs(spec.Length - Measure(player, spec, placement).Length) > 1e-3f)
                return "The length doesn't match where the line goes.";
            return null;
        }

        private string PlacementProblem(Player player, DrawingSpec spec, Placement placement)
        {
            var bindPoints = BindPointsOf(player);
            bool halfOnly = _config.Geometry.DrawOnOwnHalfOnly;

            switch (spec.Type)
            {
                case LineType.Warding:
                    if (MainWardOf(player) == null)
                    {
                        if (spec.IsMarksCross) return "Draw your main Line of Warding first.";
                        if (placement.Kind != PlacementKind.MainWard) return "Choose where to put your main Line of Warding.";
                        return CheckMainWard(player, spec, placement, halfOnly);
                    }
                    if (placement.Kind != PlacementKind.AtBindPoint) return "Pick a bind point on your main Line of Warding.";
                    if (!spec.IsMarksCross && placement.Size > 0f)
                    {
                        float min, max, start;
                        AttachedSizeRange(spec, out min, out max, out start);
                        if (placement.Size < min - 1e-4f || placement.Size > max + 1e-4f)
                            return spec.BindPoints == 2 ? "That ellipse is too flat or too round." : "That circle is too small or too big.";
                    }
                    if (!spec.IsMarksCross && spec.BindPoints == 2 && placement.Scale > 0f)
                    {
                        var geometry = _config.Geometry;
                        if (placement.Scale < geometry.AttachedEllipseMinScale - 1e-4f || placement.Scale > geometry.AttachedEllipseMaxScale + 1e-4f)
                            return "That ellipse is too small or too big.";
                    }
                    string bindPoint = ValidBindPoint(placement.BindPointA, bindPoints);
                    if (bindPoint != null) return bindPoint;
                    if (placement.Join < 0 || placement.Join >= JoinChoices(spec)) return "Choose which of its bind points joins.";
                    if (BindPointFull(player, placement.BindPointA))
                        return "That bind point already has a Line of Warding bound to it.";
                    IReadOnlyList<Vec2> unused;
                    return CheckOutline(player, WardGeometry(player, spec, placement, out unused), halfOnly, bindPoints[placement.BindPointA]);

                case LineType.Forbiddance:
                    switch (placement.Kind)
                    {
                        case PlacementKind.BetweenBindPoints:
                            if (spec.BindPoints != 2) return "A line between two bind points has 2 bind points.";
                            if (placement.BindPointA == placement.BindPointB) return "Pick two different bind points.";
                            return ValidBindPoint(placement.BindPointA, bindPoints) ?? ValidBindPoint(placement.BindPointB, bindPoints)
                                ?? CheckRectangle(player, ForbiddanceGeometry(player, placement).Value, halfOnly);
                        case PlacementKind.TangentAtBindPoint:
                            if (spec.BindPoints != 1) return "A tangent at one bind point has 1 bind point.";
                            if (placement.Length != 0f) { string length = CheckLength(placement.Length); if (length != null) return length; }
                            string tangentAt = ValidBindPoint(placement.BindPointA, bindPoints);
                            if (tangentAt != null) return tangentAt;
                            var tangent = ForbiddanceGeometry(player, placement).Value;
                            return CheckRectangle(player, tangent, halfOnly);
                        case PlacementKind.FreeSegment:
                            if (spec.BindPoints != 0) return "A free-standing line has no bind points.";
                            return CheckLength(Vec2.Distance(placement.PointA, placement.PointB))
                                ?? CheckRectangle(player, new Segment(placement.PointA, placement.PointB), halfOnly, true);
                        default:
                            return "Choose how to place the Line of Forbiddance.";
                    }

                case LineType.Vigor:
                    if (placement.Kind != PlacementKind.Aimed) return "Choose where to draw the Line of Vigor and where to aim it.";
                    if (Vec2.Distance(placement.PointA, placement.PointB) < 1e-3f) return "Aim somewhere other than the starting point.";
                    return CheckPoint(player, placement.PointA, halfOnly);

                default:
                    switch (placement.Kind)
                    {
                        case PlacementKind.AtBindPoint:
                            if (spec.BindPoints != 1) return "A chalkling bound to one bind point has 1 bind point.";
                            return ValidBindPoint(placement.BindPointA, bindPoints);
                        case PlacementKind.FreePoint:
                            if (spec.BindPoints != 0) return "An unbound chalkling has no bind points.";
                            return CheckPoint(player, placement.PointA, halfOnly);
                        default:
                            return "Choose where to draw the chalkling.";
                    }
            }
        }

        /// <summary>The outline a LoW would have with this spec and placement, for previews. Null if it can't be placed.</summary>
        public WardShape PreviewWard(Player player, DrawingSpec spec, Placement placement)
        {
            IReadOnlyList<Vec2> bindPoints;
            return WardGeometry(player, spec, placement, out bindPoints);
        }

        /// <summary>As <see cref="PreviewWard(Player, DrawingSpec, Placement)"/>, also giving the bind points it would have.</summary>
        public WardShape PreviewWard(Player player, DrawingSpec spec, Placement placement, out IReadOnlyList<Vec2> bindPoints)
        {
            return WardGeometry(player, spec, placement, out bindPoints);
        }

        /// <summary>
        /// How far a drawing with this placement is from the outline of the owner's main LoW: the
        /// nearest point of it, and 0 inside the main LoW or on its bind points (DESIGN.md section 5.1).
        /// </summary>
        public float TravelDistance(Player player, Placement placement)
        {
            var main = MainWardOf(player);
            if (main == null || placement == null) return 0f;
            switch (placement.Kind)
            {
                case PlacementKind.Aimed:
                case PlacementKind.FreePoint:
                    return DistanceOutside(main, placement.PointA);
                case PlacementKind.FreeSegment:
                    // The nearest the line comes to the outline; 0 if any of it is inside.
                    if (main.Shape.Contains(placement.PointA) || main.Shape.Contains(placement.PointB)) return 0f;
                    float nearest = float.PositiveInfinity;
                    int count = main.SampleCount;
                    for (int i = 0; i < count; i++)
                        nearest = Math.Min(nearest, SegmentDistance(placement.PointA, placement.PointB, main.SamplePoint(i), main.SamplePoint((i + 1) % count)));
                    return nearest;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// The spec with everything the board works out from the placement filled in: travel turns, and a
        /// LoF's length. The UI calls this before locking in; <see cref="ValidatePlacement"/> checks it.
        /// </summary>
        public DrawingSpec Measure(Player player, DrawingSpec spec, Placement placement)
        {
            var measured = spec.WithTravel(TravelTurns(player, placement));
            if (spec.Type == LineType.Forbiddance && placement != null)
            {
                var segment = ForbiddanceGeometry(player, placement);
                if (segment.HasValue) measured.Length = segment.Value.Length;
            }
            if (spec.Type == LineType.Warding && !spec.IsMarksCross && MainWardOf(player) != null && placement != null)
            {
                IReadOnlyList<Vec2> unused;
                var shape = WardGeometry(player, spec, placement, out unused);
                if (shape != null) measured.Length = shape.Perimeter();
            }
            return measured;
        }

        /// <summary>
        /// A Mark's Cross's two crossed LoF: each joins a bind point to the one opposite it, through the
        /// centre of the circle (DESIGN.md section 4.1).
        /// </summary>
        public static Segment[] MarksCrossBraces(WardShape shape, IReadOnlyList<Vec2> bindPoints)
        {
            var braces = new List<Segment>();
            var used = new bool[bindPoints.Count];
            for (int i = 0; i < bindPoints.Count; i++)
            {
                if (used[i]) continue;
                Vec2 opposite = shape.Centre * 2f - bindPoints[i];
                int best = -1;
                for (int j = 0; j < bindPoints.Count; j++)
                    if (j != i && !used[j] && (best < 0 || Vec2.Distance(bindPoints[j], opposite) < Vec2.Distance(bindPoints[best], opposite))) best = j;
                if (best < 0) break;
                used[i] = used[best] = true;
                braces.Add(new Segment(bindPoints[i], bindPoints[best]));
            }
            return braces.ToArray();
        }

        /// <summary>
        /// The biggest size (radius, or an ellipse's semi-minor axis) an attached LoW at this bind point can
        /// have and still fit on the board and the player's half, within its configured range.
        /// </summary>
        public float MaxAttachedSize(Player player, DrawingSpec spec, int bindPoint, float scale = 0f)
        {
            float min, max, start;
            AttachedSizeRange(spec, out min, out max, out start);
            return LargestThatFits(min, max, size => AttachedFits(player, spec, Placement.AtBindPoint(bindPoint, size, scale)));
        }

        /// <summary>
        /// The biggest overall scale an attached ellipse at this bind point, with this semi-minor axis (before
        /// scaling), can have and still fit on the board and the player's half, within its configured range.
        /// </summary>
        public float MaxAttachedScale(Player player, DrawingSpec spec, int bindPoint, float size)
        {
            var geometry = _config.Geometry;
            return LargestThatFits(geometry.AttachedEllipseMinScale, geometry.AttachedEllipseMaxScale,
                scale => AttachedFits(player, spec, Placement.AtBindPoint(bindPoint, size, scale)));
        }

        /// <summary>
        /// The overall scales an attached ellipse at this bind point can be given, one for each whole-number
        /// durability of its weakest point, as far as it fits on the player's half.
        /// </summary>
        public List<float> AttachedScales(Player player, DrawingSpec spec, int bindPoint, float size)
        {
            var geometry = _config.Geometry;
            float max = bindPoint >= 0 ? MaxAttachedScale(player, spec, bindPoint, size) : geometry.AttachedEllipseMaxScale;
            int at = Math.Max(0, bindPoint);
            return HealthSteps(geometry.AttachedEllipseMinScale, max, scale =>
            {
                IReadOnlyList<Vec2> unused;
                return WardGeometry(player, spec, Placement.AtBindPoint(at, size, scale), out unused);
            });
        }

        private bool AttachedFits(Player player, DrawingSpec spec, Placement placement)
        {
            IReadOnlyList<Vec2> unused;
            var shape = WardGeometry(player, spec, placement, out unused);
            var points = BindPointsOf(player);
            return shape != null && InRange(placement.BindPointA, points)
                && CheckOutline(player, shape, _config.Geometry.DrawOnOwnHalfOnly, points[placement.BindPointA]) == null;
        }

        /// <summary>
        /// The longest a tangent or free LoF with this placement (its length or end ignored) can be and still
        /// fit on the board and the player's half, within the configured range.
        /// </summary>
        public float MaxForbiddanceLength(Player player, Placement placement, Vec2 direction)
        {
            var geometry = _config.Geometry;
            bool halfOnly = geometry.DrawOnOwnHalfOnly;
            return LargestThatFits(geometry.MinForbiddanceLength, geometry.MaxForbiddanceLength, length =>
            {
                Segment segment;
                if (placement.Kind == PlacementKind.TangentAtBindPoint)
                {
                    var tangent = ForbiddanceGeometry(player, Placement.TangentAt(placement.BindPointA, length));
                    if (!tangent.HasValue) return false;
                    segment = tangent.Value;
                }
                else segment = new Segment(placement.PointA, placement.PointA + direction.Normalized * length);
                return CheckRectangle(player, segment, halfOnly, placement.Kind != PlacementKind.TangentAtBindPoint) == null;
            });
        }

        /// <summary>
        /// The semi-minor axes a main LoW ellipse can be given, one for each whole-number durability of its
        /// weakest point, so each scroll notch changes it by 1 (DESIGN.md section 4.1).
        /// </summary>
        public List<float> MainEllipseSizes(Player player)
        {
            var geometry = _config.Geometry;
            return HealthSteps(geometry.EllipseMinSemiMinor, geometry.EllipseMaxSemiMinor,
                size => WardShape.Ellipse(Home(player), geometry.EllipseSemiMajor, size, 0f), true);
        }

        /// <summary>
        /// The sizes an attached LoW at this bind point can be given (radius, or an ellipse's semi-minor axis),
        /// one for each whole-number durability of its weakest point, as far as it fits on the player's half.
        /// </summary>
        public List<float> AttachedSizes(Player player, DrawingSpec spec, int bindPoint, float scale = 0f)
        {
            float min, max, start;
            AttachedSizeRange(spec, out min, out max, out start);
            if (bindPoint >= 0) max = MaxAttachedSize(player, spec, bindPoint, scale);
            int at = Math.Max(0, bindPoint);
            return HealthSteps(min, max, size =>
            {
                IReadOnlyList<Vec2> unused;
                return WardGeometry(player, spec, Placement.AtBindPoint(at, size, scale), out unused);
            });
        }

        /// <summary>The durability the weakest point of a LoW with this shape would have, before any modifier.</summary>
        public int WeakestDurability(WardShape shape, bool mainWard = false)
        {
            return shape == null ? 0 : WardBody.WeakestDurability(shape, _config, mainWard);
        }

        /// <summary>
        /// Walks sizes from min to max and keeps one for each run with the same weakest-point durability:
        /// the middle of the run, or the end of the range for the runs at either end.
        /// </summary>
        private List<float> HealthSteps(float min, float max, Func<float, WardShape> shapeFor, bool mainWard = false)
        {
            var steps = new List<float>();
            if (max <= min) { steps.Add(min); return steps; }
            const int samples = 200;
            float runStart = min;
            int runValue = WeakestDurability(shapeFor(min), mainWard);
            for (int i = 1; i <= samples; i++)
            {
                float size = min + (max - min) * i / samples;
                int value = WeakestDurability(shapeFor(size), mainWard);
                if (value == runValue && i < samples) continue;
                float runEnd = value == runValue ? size : min + (max - min) * (i - 1) / samples;
                steps.Add(runStart <= min ? min : runEnd >= max ? max : (runStart + runEnd) / 2f);
                if (value != runValue && i == samples) steps.Add(max);
                runStart = size;
                runValue = value;
            }
            return steps;
        }

        /// <summary>The largest value in [min, max] that fits, assuming bigger never fits better; min if none does.</summary>
        private static float LargestThatFits(float min, float max, Func<float, bool> fits)
        {
            if (fits(max)) return max;
            if (!fits(min)) return min;
            float low = min, high = max;
            for (int i = 0; i < 24; i++)
            {
                float mid = (low + high) / 2f;
                if (fits(mid)) low = mid; else high = mid;
            }
            return low;
        }

        /// <summary>Spots offered for the main LoW, all on the midline of the player's half: back (the default spot), centre and front.</summary>
        public List<Vec2> MainWardPresets(Player player, DrawingSpec spec, float semiMinor)
        {
            float quarter = Width / 4f;
            float centreX = player == Player.A ? quarter : Width - quarter;
            var spots = new List<Vec2> { Home(player), new Vec2(centreX, Height / 2f), new Vec2(Width / 2f, Height / 2f) };
            var presets = new List<Vec2>();
            foreach (var spot in spots)
            {
                Vec2 clamped = ClampMainWard(player, spec, spot, semiMinor);
                bool duplicate = false;
                foreach (var existing in presets) duplicate |= Vec2.Distance(existing, clamped) < 0.1f;
                if (!duplicate) presets.Add(clamped);
            }
            return presets;
        }

        /// <summary>
        /// Where the main LoW goes for a mouse position: snapped to a preset or the board's midline when
        /// close, then moved to the nearest spot where the whole shape fits (on the player's own half).
        /// </summary>
        public Vec2 SnapMainWard(Player player, DrawingSpec spec, Vec2 desired, float semiMinor)
        {
            float snap = _config.Geometry.MainWardSnapDistance;
            Vec2 clamped = ClampMainWard(player, spec, desired, semiMinor);
            foreach (var preset in MainWardPresets(player, spec, semiMinor))
                if (Vec2.Distance(preset, clamped) <= snap) return preset;
            if (Math.Abs(clamped.Y - Height / 2f) <= snap) clamped = ClampMainWard(player, spec, new Vec2(clamped.X, Height / 2f), semiMinor);
            return clamped;
        }

        /// <summary>The nearest centre to <paramref name="desired"/> where the main LoW fits wholly on the board (and the player's half).</summary>
        public Vec2 ClampMainWard(Player player, DrawingSpec spec, Vec2 desired, float semiMinor)
        {
            IReadOnlyList<Vec2> bindPoints;
            var shape = WardGeometry(player, spec, Placement.MainWard(desired, semiMinor), out bindPoints, true);
            // Half-extents of the shape around its centre.
            float ex = 0f, ey = 0f;
            const int steps = 64;
            for (int i = 0; i < steps; i++)
            {
                Vec2 offset = shape.PointAt(2f * (float)Math.PI * i / steps) - shape.Centre;
                ex = Math.Max(ex, Math.Abs(offset.X));
                ey = Math.Max(ey, Math.Abs(offset.Y));
            }
            const float margin = 1e-3f;
            float minX = 0f, maxX = Width;
            if (_config.Geometry.DrawOnOwnHalfOnly)
            {
                if (player == Player.A) maxX = Width / 2f; else minX = Width / 2f;
            }
            float x = Clamp(desired.X, minX + ex + margin, maxX - ex - margin);
            float y = Clamp(desired.Y, ey + margin, Height - ey - margin);
            return new Vec2(x, y);
        }

        /// <summary>One of this player's LoF within <paramref name="within"/> of the point, nearest first; null if none.</summary>
        public ForbiddanceBody ForbiddanceAt(Player owner, Vec2 point, float within)
        {
            ForbiddanceBody best = null;
            float bestDistance = within;
            foreach (var body in _bodies.Values)
            {
                var forbiddance = body as ForbiddanceBody;
                if (forbiddance == null || forbiddance.Owner != owner || !forbiddance.Line.IsActive) continue;
                float distance = DistanceToSegment(point, forbiddance.Segment);
                if (distance <= bestDistance) { bestDistance = distance; best = forbiddance; }
            }
            return best;
        }

        /// <summary>What a LoV with this damage deals to a LoF it reflects off: half, rounded up (DESIGN.md section 4.2).</summary>
        public int ReflectDamage(int vigorDamage)
        {
            return (int)Math.Ceiling(vigorDamage * _config.Forbiddance.VigorDamageMultiplier - 1e-6);
        }

        /// <summary>What a LoV with this damage deals to a LoW arc toughened by a LoF, and to that LoF (DESIGN.md section 4.2).</summary>
        public int ToughenedArcDamage(int vigorDamage)
        {
            return (int)Math.Ceiling(vigorDamage * _config.Forbiddance.ChordArcDamageMultiplier - 1e-6);
        }

        /// <summary>
        /// Which line wins when the mouse is near several at once, first to last. Smaller and moving lines come
        /// first, since they are the hardest to reach otherwise.
        /// </summary>
        private static readonly LineType[] HoverPriority = { LineType.Making, LineType.Vigor, LineType.Warding, LineType.Forbiddance };

        /// <summary>
        /// The line under the point, if any is within <paramref name="within"/>: for showing its stats. When several
        /// are in reach, the one first in <see cref="HoverPriority"/> wins, and the nearest of that type.
        /// </summary>
        public LineBody BodyAt(Vec2 point, float within)
        {
            LineBody best = null;
            int bestRank = int.MaxValue;
            float bestDistance = within;
            foreach (var body in _bodies.Values)
            {
                if (!body.Line.IsActive) continue;
                float distance = float.PositiveInfinity;
                var ward = body as WardBody;
                var forbiddance = body as ForbiddanceBody;
                var vigor = body as VigorBody;
                var chalkling = body as ChalklingBody;
                if (ward != null)
                    for (int i = 0; i < ward.SampleCount; i++) distance = Math.Min(distance, Vec2.Distance(ward.SamplePoint(i), point));
                else if (forbiddance != null) distance = DistanceToSegment(point, forbiddance.Segment);
                else if (vigor != null)
                    distance = DistanceToSegment(point, new Segment(vigor.Position - vigor.Direction * _config.Geometry.VigorDrawnLength, vigor.Position));
                else if (chalkling != null) distance = Math.Max(0f, Vec2.Distance(chalkling.Position, point) - _config.Making.Radius);
                if (distance > within) continue;
                int rank = Array.IndexOf(HoverPriority, body.Line.Type);
                if (rank < 0) rank = HoverPriority.Length;
                if (rank < bestRank || (rank == bestRank && distance <= bestDistance))
                {
                    bestRank = rank;
                    bestDistance = distance;
                    best = body;
                }
            }
            return best;
        }

        private static float DistanceToSegment(Vec2 point, Segment segment)
        {
            Vec2 along = segment.B - segment.A;
            float lengthSquared = Vec2.Dot(along, along);
            float t = lengthSquared < 1e-9f ? 0f : Clamp(Vec2.Dot(point - segment.A, along) / lengthSquared, 0f, 1f);
            return Vec2.Distance(point, segment.A + along * t);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (min > max) return (min + max) / 2f;
            return Math.Max(min, Math.Min(max, value));
        }

        /// <summary>The bind points a main LoW gets once that many are drawn: an ellipse's two ends, or a circle's preset.</summary>
        private IReadOnlyList<Vec2> MainBindPoints(Player owner, WardShape shape, int count)
        {
            if (count <= 0) return new List<Vec2>();
            if (!shape.IsCircle) return BindPoints.ForEllipse(shape);
            return BindPoints.ForCircle(count, shape.Centre, shape.A, Facing(owner));
        }

        /// <summary>Where a player's main LoW bind points would go if they drew this many, for previews.</summary>
        public IReadOnlyList<Vec2> PreviewBindPoints(Player player, int count)
        {
            var main = MainWardOf(player);
            return main != null ? MainBindPoints(player, main.Shape, count) : new List<Vec2>();
        }

        /// <summary>The bind point counts a player's main LoW can take: 2 on an ellipse; 4, 6 or 9 on a circle.</summary>
        public int[] BindPointChoices(Player player)
        {
            var main = MainWardOf(player);
            if (main == null) return new int[0];
            return main.Shape.IsCircle ? new[] { 4, 6, 9 } : new[] { 2 };
        }

        /// <summary>Extra turns for drawing this far from the main LoW: one per threshold passed.</summary>
        public int TravelTurns(Player player, Placement placement)
        {
            float distance = TravelDistance(player, placement);
            int turns = 0;
            foreach (float threshold in _config.Geometry.TravelTurnThresholds)
                if (distance > threshold) turns++;
            return turns;
        }

        /// <summary>The ellipse's semi-minor axis actually used for this placement, within the configured range.</summary>
        public float EllipseSemiMinor(Placement placement)
        {
            var geometry = _config.Geometry;
            float wanted = placement != null && placement.SemiMinor > 0f ? placement.SemiMinor : geometry.EllipseSemiMinor;
            return Math.Max(geometry.EllipseMinSemiMinor, Math.Min(geometry.EllipseMaxSemiMinor, wanted));
        }

        /// <summary>The length a tangent LoF with this placement has.</summary>
        public float ForbiddanceLength(Placement placement)
        {
            return placement != null && placement.Length > 0f ? placement.Length : _config.Geometry.TangentForbiddanceLength;
        }

        /// <summary>The segment a LoF would occupy with this placement, for previews. Null if it can't be placed.</summary>
        public Segment? PreviewForbiddance(Player player, Placement placement)
        {
            return ForbiddanceGeometry(player, placement);
        }

        public void ResolveMovementAndAttacks(Duel duel, MovementResult result)
        {
            _notes.Clear();
            _trails.Clear();
            bool wasUp = CentreWallUp;
            CentreWallUp = CentreWallStandsOn(duel.Turn);
            if (wasUp && !CentreWallUp) _notes.Add("The centre Line of Forbiddance is gone.");
            Sync(duel, result);
            FlyVigor(result);
        }

        public void PlaceNewLines(Duel duel, MovementResult result)
        {
            Sync(duel, result);
            CentreWallUp = CentreWallStandsOn(duel.Turn + 1);
            foreach (Player player in new[] { Player.A, Player.B })
                _sketches[(int)player] = Sketch(player, duel.GetPlayer(player).Work);
        }

        /// <summary>Where a player's work in progress will go, using the same geometry as the finished line.</summary>
        private WorkSketch Sketch(Player owner, WorkInProgress work)
        {
            if (work == null) return null;
            var sketch = new WorkSketch { Owner = owner, Kind = work.Kind };
            switch (work.Kind)
            {
                case WorkKind.BindPoints:
                    var main = MainWardOf(owner);
                    sketch.Type = LineType.Warding;
                    if (main != null) sketch.BindPoints = MainBindPoints(owner, main.Shape, main.Line.BindPointsDrawn + work.Amount);
                    return sketch;
                case WorkKind.Drawing:
                    break;
                default:
                    // Work on a line already down (dismissing a LoF) finishes in the tick it starts, so is never seen.
                    return null;
            }

            var spec = work.Spec;
            sketch.Type = spec.Type;
            var pending = _pending[(int)owner];
            var placement = pending.IsSet && pending.Type == spec.Type ? ResolvePending(owner, pending) : Placement.Default();
            switch (spec.Type)
            {
                case LineType.Warding:
                    IReadOnlyList<Vec2> bindPoints;
                    sketch.Ward = WardGeometry(owner, spec, placement, out bindPoints);
                    if (sketch.Ward == null) return null;
                    sketch.BindPoints = bindPoints;
                    if (spec.IsMarksCross) sketch.Braces = MarksCrossBraces(sketch.Ward, bindPoints);
                    break;
                case LineType.Forbiddance:
                    sketch.Segment = ForbiddanceGeometry(owner, placement) ?? ForbiddanceGeometry(owner, Placement.Default());
                    break;
                case LineType.Vigor:
                    Vec2 direction;
                    sketch.Point = VigorStart(owner, placement, out direction);
                    sketch.Direction = direction;
                    break;
                default:
                    Vec2? tether;
                    sketch.Point = ChalklingSpot(owner, spec, placement, out tether);
                    sketch.Tether = tether;
                    break;
            }
            return sketch;
        }

        // ---- Keeping bodies in step with the duel's lines ----

        private void Sync(Duel duel, MovementResult result)
        {
            foreach (var line in duel.Lines)
            {
                if (!line.IsActive)
                {
                    _bodies.Remove(line.Id);
                    continue;
                }

                LineBody body;
                if (!_bodies.TryGetValue(line.Id, out body))
                {
                    // A Mark's Cross's crossed LoF go where its bind points are, so they have no placement of their own.
                    body = CreateBody(line, line.BraceOf >= 0 ? Placement.Default() : TakePending(line));
                    _bodies[line.Id] = body;
                    var newWard = body as WardBody;
                    if (newWard != null)
                        result.DurabilityOverrides.Add(new DurabilityOverride(line.Id, newWard.Weakest, newWard.WeakestMax));
                    continue;
                }

                // Chalk fatigue changes the rules-side durability; spread it over the whole line.
                var ward = body as WardBody;
                if (ward == null) continue;
                if (line.IsMainWard && ward.BindPoints.Count != line.BindPointsDrawn)
                    ward.BindPoints = MainBindPoints(line.Owner, ward.Shape, line.BindPointsDrawn);
                int delta = line.Durability - ward.Weakest;
                if (delta != 0) ward.ApplyEverywhere(delta);
                if (ward.Weakest != line.Durability || ward.WeakestMax != line.MaxDurability)
                    result.DurabilityOverrides.Add(new DurabilityOverride(line.Id, ward.Weakest, ward.WeakestMax));
            }
            DestroyUnboundForbiddance(result);
        }

        /// <summary>
        /// A LoF bound at a bind point is destroyed once no LoW passes through that point any more: when the LoW it
        /// is bound to breaks, unless another LoW still shares the point (DESIGN.md section 4.2).
        /// </summary>
        private void DestroyUnboundForbiddance(MovementResult result)
        {
            var lost = new List<LineBody>();
            foreach (var body in _bodies.Values)
            {
                var forbiddance = body as ForbiddanceBody;
                if (forbiddance == null) continue;
                foreach (var anchor in forbiddance.Anchors)
                {
                    bool held = false;
                    foreach (var ward in WardsOf(forbiddance.Owner)) held |= OnOutline(ward, anchor);
                    if (!held) { lost.Add(forbiddance); break; }
                }
            }
            foreach (var body in lost)
            {
                _bodies.Remove(body.LineId);
                result.DestroyedLineIds.Add(body.LineId);
                _notes.Add(Name(body) + " is destroyed: the Line of Warding it was bound to is gone.");
            }
        }

        private Placement TakePending(Line line)
        {
            var pending = _pending[(int)line.Owner];
            _pending[(int)line.Owner] = default(PendingPlacement);
            if (!pending.IsSet || pending.Type != line.Type) return Placement.Default();
            return ResolvePending(line.Owner, pending);
        }

        /// <summary>The pending placement with its bind points found again where they were when it was started.</summary>
        private Placement ResolvePending(Player owner, PendingPlacement pending)
        {
            var placement = pending.Placement.Clone();
            var points = BindPointsOf(owner);
            if (placement.BindPositionA.HasValue) placement.BindPointA = NearestIndex(points, placement.BindPositionA.Value, placement.BindPointA);
            if (placement.BindPositionB.HasValue) placement.BindPointB = NearestIndex(points, placement.BindPositionB.Value, placement.BindPointB);
            return placement;
        }

        private static int NearestIndex(IReadOnlyList<Vec2> points, Vec2 position, int fallback)
        {
            for (int i = 0; i < points.Count; i++)
                if (Vec2.Distance(points[i], position) < SameBindPoint) return i;
            return fallback;
        }

        private LineBody CreateBody(Line line, Placement placement)
        {
            var owner = line.Owner;
            switch (line.Type)
            {
                case LineType.Warding:
                    IReadOnlyList<Vec2> bindPoints;
                    var shape = WardGeometry(owner, line.Spec, placement, out bindPoints, line.IsMainWard);
                    var wardBody = new WardBody(line, shape, bindPoints, _config);
                    if (line.IsMainWard) wardBody.BindPoints = MainBindPoints(owner, shape, line.BindPointsDrawn);
                    return wardBody;

                case LineType.Forbiddance:
                    if (line.BraceOf >= 0)
                    {
                        // One of a Mark's Cross's crossed LoF: a chord of its circle like any LoF joining two bind points
                        // (decided 2026-10-27, on the balancing watchlist).
                        var cross = BodyOf(line.BraceOf) as WardBody;
                        if (cross != null)
                        {
                            var braces = MarksCrossBraces(cross.Shape, cross.BindPoints);
                            if (line.BraceIndex < braces.Length)
                            {
                                var brace = braces[line.BraceIndex];
                                return new ForbiddanceBody(line, brace, cross.LineId, new[] { brace.A, brace.B });
                            }
                        }
                    }
                    var segment = ForbiddanceGeometry(owner, placement) ?? ForbiddanceGeometry(owner, Placement.Default()).Value;
                    int chordOf = -1;
                    Vec2[] anchors = null;
                    if (placement.Kind == PlacementKind.BetweenBindPoints)
                    {
                        anchors = new[] { segment.A, segment.B };
                        foreach (var ward in WardsOf(owner))
                            if (chordOf < 0 && OnOutline(ward, segment.A) && OnOutline(ward, segment.B)) chordOf = ward.LineId;
                    }
                    else if (placement.Kind == PlacementKind.TangentAtBindPoint)
                    {
                        var points = BindPointsOf(owner);
                        if (InRange(placement.BindPointA, points)) anchors = new[] { points[placement.BindPointA] };
                    }
                    return new ForbiddanceBody(line, segment, chordOf, anchors);

                case LineType.Vigor:
                    Vec2 direction;
                    Vec2 start = VigorStart(owner, placement, out direction);
                    return new VigorBody(line, start, direction);

                default:
                    Vec2? tether;
                    Vec2 spot = ChalklingSpot(owner, line.Spec, placement, out tether);
                    return new ChalklingBody(line, spot, tether);
            }
        }

        private Vec2 VigorStart(Player owner, Placement placement, out Vec2 direction)
        {
            if (placement.Kind == PlacementKind.Aimed)
            {
                direction = placement.PointB - placement.PointA;
                return placement.PointA;
            }
            direction = Facing(owner);
            return Home(owner) + Facing(owner) * (FrontDistance(owner) + 1f);
        }

        /// <summary>Where a chalkling stands, and the bind point it is tethered to if it is bound.</summary>
        private Vec2 ChalklingSpot(Player owner, DrawingSpec spec, Placement placement, out Vec2? tether)
        {
            var allPoints = BindPointsOf(owner);
            int bindPoint = placement.Kind == PlacementKind.AtBindPoint ? placement.BindPointA
                : placement.Kind == PlacementKind.Default && spec.BindPoints > 0 ? 0 : -1;
            if (bindPoint >= 0 && bindPoint < allPoints.Count)
            {
                Vec2 point = allPoints[bindPoint];
                tether = point;
                return point + BindPointNormal(owner, point) * (_config.Making.Radius + 0.05f);
            }
            tether = null;
            if (placement.Kind == PlacementKind.FreePoint) return placement.PointA;
            return Home(owner) + Facing(owner) * (FrontDistance(owner) + 1f);
        }

        private WardShape WardGeometry(Player owner, DrawingSpec spec, Placement placement, out IReadOnlyList<Vec2> bindPoints, bool? isMain = null)
        {
            var main = MainWardOf(owner);
            bool becomesMain = isMain ?? (main == null && !spec.IsMarksCross);
            Vec2 facing = Facing(owner);

            if (becomesMain)
            {
                bool placed = placement != null && placement.Kind == PlacementKind.MainWard;
                Vec2 home = placed && !_config.Geometry.MainWardAtHome ? placement.PointA : Home(owner);
                if (spec.BindPoints == 2)
                {
                    float semiMinor = placed ? EllipseSemiMinor(placement) : _config.Geometry.EllipseSemiMinor;
                    var ellipse = WardShape.Ellipse(home, _config.Geometry.EllipseSemiMajor, semiMinor, facing.Angle);
                    bindPoints = BindPoints.ForEllipse(ellipse);
                    return ellipse;
                }
                float radius = _config.Warding.DefaultMainRadius;
                bindPoints = BindPoints.ForCircle(spec.BindPoints, home, radius, facing);
                return WardShape.Circle(home, radius);
            }

            // An attached ellipse sticks straight out from the bind point, so it reaches as far as its long axis.
            bool attachedEllipse = !spec.IsMarksCross && spec.BindPoints == 2;
            float scale = attachedEllipse ? AttachedScale(placement) : 1f;
            float reach = spec.IsMarksCross ? _config.Warding.MarksCrossRadius
                : attachedEllipse ? _config.Geometry.AttachedEllipseSemiMajor * scale
                : AttachedSize(spec, placement);
            int count = spec.IsMarksCross ? _config.Warding.MarksCrossBindPoints : spec.BindPoints;
            Vec2 centre;
            Vec2 outward;
            if (main == null)
            {
                // Nothing to attach to (only reachable without the UI): sit in front of home.
                outward = facing;
                centre = Home(owner) + facing * (_config.Warding.DefaultMainRadius + reach);
            }
            else
            {
                var allPoints = BindPointsOf(owner);
                int index = placement != null && placement.Kind == PlacementKind.AtBindPoint ? placement.BindPointA : 0;
                if (index < 0 || index >= allPoints.Count)
                {
                    if (placement != null && placement.Kind == PlacementKind.AtBindPoint) { bindPoints = null; return null; }
                    index = 0;
                }
                Vec2 point = allPoints[index];
                outward = BindPointNormal(owner, point);
                centre = point + outward * reach;
            }
            if (attachedEllipse)
            {
                var shape = WardShape.Ellipse(centre, reach, AttachedSize(spec, placement) * scale, outward.Angle);
                bindPoints = BindPoints.ForEllipse(shape);
                return shape;
            }
            // One of its bind points sits where it touches the LoW it is attached to: the player picks which.
            int join = placement != null && placement.Kind == PlacementKind.AtBindPoint ? placement.Join : 0;
            bindPoints = BindPoints.ForAttachedCircle(count, centre, reach, outward * -1f, join);
            return WardShape.Circle(centre, reach);
        }

        /// <summary>
        /// How many choices there are for which of a new LoW's bind points joins the LoW it is attached to. Only a
        /// circle whose bind points aren't symmetric (9) has more than one; a main LoW's are fixed by its preset.
        /// </summary>
        public int JoinChoices(DrawingSpec spec)
        {
            if (spec == null || spec.Type != LineType.Warding || spec.IsMarksCross || spec.BindPoints == 2) return 1;
            return BindPoints.JoinChoices(spec.BindPoints);
        }

        /// <summary>
        /// The size actually used for an attached LoW with this placement, within the configured range:
        /// the semi-minor axis of an ellipse (2 bind points), otherwise the radius of a circle.
        /// </summary>
        public float AttachedSize(DrawingSpec spec, Placement placement)
        {
            float min, max, start;
            AttachedSizeRange(spec, out min, out max, out start);
            float wanted = placement != null && placement.Size > 0f ? placement.Size : start;
            return Math.Max(min, Math.Min(max, wanted));
        }

        /// <summary>The overall scale actually used for an attached ellipse with this placement, within the configured range.</summary>
        public float AttachedScale(Placement placement)
        {
            var geometry = _config.Geometry;
            float wanted = placement != null && placement.Scale > 0f ? placement.Scale : 1f;
            return Math.Max(geometry.AttachedEllipseMinScale, Math.Min(geometry.AttachedEllipseMaxScale, wanted));
        }

        /// <summary>The range the scroll wheel can set an attached LoW's size in, and where it starts.</summary>
        public void AttachedSizeRange(DrawingSpec spec, out float min, out float max, out float start)
        {
            var geometry = _config.Geometry;
            if (spec.BindPoints == 2)
            {
                min = geometry.AttachedEllipseMinSemiMinor;
                max = geometry.AttachedEllipseMaxSemiMinor;
                start = geometry.AttachedEllipseSemiMinor;
            }
            else
            {
                min = geometry.AttachedWardMinRadius;
                max = geometry.AttachedWardMaxRadius;
                start = geometry.AttachedWardRadius;
            }
        }

        private Segment? ForbiddanceGeometry(Player owner, Placement placement)
        {
            var main = MainWardOf(owner);
            var bindPoints = BindPointsOf(owner);
            float length = ForbiddanceLength(placement);

            switch (placement.Kind)
            {
                case PlacementKind.BetweenBindPoints:
                    if (!InRange(placement.BindPointA, bindPoints) || !InRange(placement.BindPointB, bindPoints)) return null;
                    return new Segment(bindPoints[placement.BindPointA], bindPoints[placement.BindPointB]);
                case PlacementKind.TangentAtBindPoint:
                    if (!InRange(placement.BindPointA, bindPoints)) return null;
                    return Tangent(bindPoints[placement.BindPointA], BindPointNormal(owner, bindPoints[placement.BindPointA]), length);
                case PlacementKind.FreeSegment:
                    return new Segment(placement.PointA, placement.PointB);
                default:
                    if (main != null && main.BindPoints.Count > 0) return Tangent(main.BindPoints[0], BindPointNormal(owner, main.BindPoints[0]), length);
                    Vec2 front = Home(owner) + Facing(owner) * (FrontDistance(owner) + 1f);
                    Vec2 across = Facing(owner).Perp * (length / 2f);
                    return new Segment(front - across, front + across);
            }
        }

        private static Segment Tangent(Vec2 point, Vec2 normal, float length)
        {
            Vec2 along = normal.Perp * (length / 2f);
            return new Segment(point - along, point + along);
        }

        /// <summary>How far in front of home the main LoW reaches.</summary>
        private float FrontDistance(Player owner)
        {
            var main = MainWardOf(owner);
            return main != null ? main.Shape.A : _config.Warding.DefaultMainRadius;
        }

        // ---- Lines of Vigor in flight ----

        private enum HitKind { None, Ward, Forbiddance, CentreWall, Chalkling, Edge, Vigor }

        private struct Hit
        {
            public HitKind Kind;
            public float Time;
            public VigorBody Vigor;
            public LineBody Other;
            public Vec2 Normal;
        }

        private void FlyVigor(MovementResult result)
        {
            var flying = new List<VigorBody>();
            var trails = new Dictionary<int, VigorTrail>();
            foreach (var body in _bodies.Values)
            {
                var vigor = body as VigorBody;
                if (vigor == null || !vigor.Line.IsActive) continue;
                flying.Add(vigor);
                var trail = new VigorTrail { Owner = vigor.Owner };
                trail.Points.Add(vigor.Position);
                trails[vigor.LineId] = trail;
                _trails.Add(trail);
            }

            // Chalkling and LoF durability used up so far this tick; the rules core applies the damage afterwards.
            var chalklingDamage = new Dictionary<int, int>();

            // Step through the tick one collision at a time, earliest first, so each LoV resolves what it
            // crosses in order along its path and same-tick hits keep their time of impact.
            float now = 0f;
            for (int events = 0; events < MaxEventsPerTick && flying.Count > 0; events++)
            {
                var hit = EarliestHit(flying, chalklingDamage, now);
                if (hit.Kind == HitKind.None) break;

                Advance(flying, hit.Time - now);
                now = hit.Time;
                trails[hit.Vigor.LineId].Points.Add(hit.Vigor.Position);

                switch (hit.Kind)
                {
                    case HitKind.Forbiddance:
                        ReflectOffForbiddance(hit.Vigor, (ForbiddanceBody)hit.Other, hit.Normal, now, chalklingDamage, result);
                        break;

                    case HitKind.CentreWall:
                        hit.Vigor.Direction = Vec2.Reflect(hit.Vigor.Direction, hit.Normal).Normalized;
                        hit.Vigor.Position += hit.Vigor.Direction * Intersect.Epsilon;
                        _notes.Add(Name(hit.Vigor) + " was reflected by the centre Line of Forbiddance.");
                        break;

                    case HitKind.Ward:
                        var ward = (WardBody)hit.Other;
                        var shield = ShieldingChord(ward, hit.Vigor.Position, chalklingDamage);
                        if (shield != null) HitShieldedArc(hit.Vigor, shield, ward, now, chalklingDamage, result);
                        else HitWard(hit.Vigor, ward, now, result);
                        Absorb(hit.Vigor, flying, result);
                        break;

                    case HitKind.Chalkling:
                        HitChalkling(hit.Vigor, (ChalklingBody)hit.Other, now, chalklingDamage, result);
                        Absorb(hit.Vigor, flying, result);
                        break;

                    case HitKind.Edge:
                        _notes.Add(Name(hit.Vigor) + " flew off the board.");
                        Absorb(hit.Vigor, flying, result);
                        break;

                    case HitKind.Vigor:
                        var other = (VigorBody)hit.Other;
                        trails[other.LineId].Points.Add(other.Position);
                        CollideVigor(hit.Vigor, other, flying);
                        break;
                }
            }
            Advance(flying, 1f - now);
            foreach (var vigor in flying) trails[vigor.LineId].Points.Add(vigor.Position);
        }

        private Hit EarliestHit(List<VigorBody> flying, Dictionary<int, int> chalklingDamage, float now)
        {
            var best = new Hit { Kind = HitKind.None, Time = float.PositiveInfinity };

            foreach (var vigor in flying)
            {
                float speed = vigor.Line.Speed;
                if (speed <= 0f) continue;
                float reach = (1f - now) * speed;
                Vec2 from = vigor.Position;
                Vec2 direction = vigor.Direction;

                if (!OnBoard(from)) { Consider(ref best, HitKind.Edge, now, vigor, null, Vec2.Zero); continue; }
                Consider(ref best, HitKind.Edge, now + EdgeDistance(from, direction) / speed, vigor, null, Vec2.Zero);
                if (CentreWallUp)
                {
                    float wallDistance;
                    Vec2 wallNormal;
                    if (Intersect.RaySegment(from, direction, CentreWall, out wallDistance, out wallNormal) && wallDistance <= reach)
                        Consider(ref best, HitKind.CentreWall, now + wallDistance / speed, vigor, null, wallNormal);
                }

                foreach (var body in _bodies.Values)
                {
                    if (!body.Line.IsActive) continue;
                    float distance;

                    var ward = body as WardBody;
                    if (ward != null)
                    {
                        if (!ward.IsBroken && ward.Shape.Raycast(from, direction, out distance) && distance <= reach)
                            Consider(ref best, HitKind.Ward, now + distance / speed, vigor, ward, Vec2.Zero);
                        continue;
                    }

                    var forbiddance = body as ForbiddanceBody;
                    if (forbiddance != null)
                    {
                        if (RemainingDurability(forbiddance, chalklingDamage) <= 0) continue;
                        Vec2 normal;
                        if (Intersect.RaySegment(from, direction, forbiddance.Segment, out distance, out normal) && distance <= reach)
                            Consider(ref best, HitKind.Forbiddance, now + distance / speed, vigor, forbiddance, normal);
                        continue;
                    }

                    var chalkling = body as ChalklingBody;
                    if (chalkling != null)
                    {
                        if (RemainingDurability(chalkling, chalklingDamage) <= 0) continue;
                        if (Intersect.RayCircle(from, direction, chalkling.Position, _config.Making.Radius, out distance) && distance <= reach)
                            Consider(ref best, HitKind.Chalkling, now + distance / speed, vigor, chalkling, Vec2.Zero);
                    }
                }
            }

            // LoV meeting LoV: both move in straight lines until the next event, so solve for contact.
            float contact = _config.Geometry.VigorContactDistance;
            for (int i = 0; i < flying.Count; i++)
            {
                for (int j = i + 1; j < flying.Count; j++)
                {
                    var a = flying[i];
                    var b = flying[j];
                    Vec2 gap = b.Position - a.Position;
                    Vec2 closing = b.Direction * b.Line.Speed - a.Direction * a.Line.Speed;
                    if (Vec2.Dot(gap, closing) >= 0f) continue; // not getting closer

                    float qa = Vec2.Dot(closing, closing);
                    float qb = 2f * Vec2.Dot(gap, closing);
                    float qc = Vec2.Dot(gap, gap) - contact * contact;
                    float discriminant = qb * qb - 4f * qa * qc;
                    if (qa < 1e-9f || discriminant < 0f) continue;
                    float tau = qc <= 0f ? 0f : (-qb - (float)Math.Sqrt(discriminant)) / (2f * qa);
                    if (tau < 0f) continue;
                    Consider(ref best, HitKind.Vigor, now + tau, a, b, Vec2.Zero);
                }
            }

            return best.Time <= 1f ? best : new Hit { Kind = HitKind.None };
        }

        private static void Consider(ref Hit best, HitKind kind, float time, VigorBody vigor, LineBody other, Vec2 normal)
        {
            if (time >= best.Time) return;
            best = new Hit { Kind = kind, Time = time, Vigor = vigor, Other = other, Normal = normal };
        }

        private float EdgeDistance(Vec2 from, Vec2 direction)
        {
            float distance = float.PositiveInfinity;
            if (direction.X > 1e-9f) distance = Math.Min(distance, (Width - from.X) / direction.X);
            if (direction.X < -1e-9f) distance = Math.Min(distance, -from.X / direction.X);
            if (direction.Y > 1e-9f) distance = Math.Min(distance, (Height - from.Y) / direction.Y);
            if (direction.Y < -1e-9f) distance = Math.Min(distance, -from.Y / direction.Y);
            return Math.Max(0f, distance);
        }

        private static void Advance(List<VigorBody> flying, float time)
        {
            if (time <= 0f) return;
            foreach (var vigor in flying)
                vigor.Position += vigor.Direction * (vigor.Line.Speed * time);
        }

        private void Absorb(VigorBody vigor, List<VigorBody> flying, MovementResult result)
        {
            flying.Remove(vigor);
            _bodies.Remove(vigor.LineId);
            result.DestroyedLineIds.Add(vigor.LineId);
        }

        private void HitWard(VigorBody vigor, WardBody ward, float time, MovementResult result)
        {
            HitWard(vigor, ward, vigor.Line.Damage, time, result, "");
        }

        private void HitWard(VigorBody vigor, WardBody ward, int damage, float time, MovementResult result, string because)
        {
            int before = ward.Weakest;
            ward.ApplyHit(vigor.Position, damage, _config.Warding.HitHalfWidth);
            int drop = before - ward.Weakest;

            // The rules core tracks the weakest point, so report how far that fell.
            result.Damage.Add(new DamageEvent(ward.LineId, drop, vigor.LineId, time));
            _notes.Add(Name(vigor) + " hit " + Name(ward) + " for " + damage + because + (drop == damage ? "." : " at a point stronger than its weakest."));
        }

        private void HitChalkling(VigorBody vigor, ChalklingBody chalkling, float time, Dictionary<int, int> used, MovementResult result)
        {
            int damage = vigor.Line.Damage;
            int left = RemainingDurability(chalkling, used) - damage;
            used[chalkling.LineId] = (used.ContainsKey(chalkling.LineId) ? used[chalkling.LineId] : 0) + damage;
            result.Damage.Add(new DamageEvent(chalkling.LineId, damage, vigor.LineId, time));
            _notes.Add(Name(vigor) + " hit " + Name(chalkling) + " for " + damage + ".");
            if (left <= 0) return;

            // A chalkling that survives is pushed along the LoV's direction, but never past its tether.
            float push = VigorRules.PushDistance(damage, chalkling.Line.MaxDurability, _config);
            Vec2 moved = chalkling.Position + vigor.Direction * push;
            if (chalkling.Tether.HasValue)
            {
                Vec2 fromTether = moved - chalkling.Tether.Value;
                float tether = _config.Making.TetherDistance;
                if (fromTether.Length > tether) moved = chalkling.Tether.Value + fromTether.Normalized * tether;
            }
            chalkling.Position = moved;
        }

        private void CollideVigor(VigorBody a, VigorBody b, List<VigorBody> flying)
        {
            string names = Name(a) + " and " + Name(b);
            var destroyed = VigorRules.Collide(a.Line, b.Line, _config);
            foreach (var id in destroyed)
            {
                flying.RemoveAll(v => v.LineId == id);
                _bodies.Remove(id);
            }

            if (destroyed.Length == 2)
            {
                _notes.Add(names + " collided and cancelled each other out.");
                return;
            }
            var survivor = destroyed[0] == a.LineId ? b : a;
            _notes.Add(names + " collided. " + Name(survivor) + " carries on as " + survivor.Line.Tier
                + " (" + survivor.Line.Damage + " damage).");
        }

        /// <summary>
        /// A LoF reflects a LoV and takes a reduced share of its damage (DESIGN.md section 4.2). A LoF
        /// broken by the hit no longer blocks anything later in the tick, so the LoV still reflects.
        /// </summary>
        private void ReflectOffForbiddance(VigorBody vigor, ForbiddanceBody forbiddance, Vec2 normal, float time, Dictionary<int, int> used, MovementResult result)
        {
            vigor.Direction = Vec2.Reflect(vigor.Direction, normal).Normalized;
            vigor.Position += vigor.Direction * Intersect.Epsilon;
            int damage = ReflectDamage(vigor.Line.Damage);
            if (damage <= 0)
            {
                _notes.Add(Name(vigor) + " was reflected by " + Name(forbiddance) + ".");
                return;
            }
            DamageForbiddance(forbiddance, damage, vigor, time, used, result);
            _notes.Add(Name(vigor) + " was reflected by " + Name(forbiddance) + ", dealing it " + damage + ".");
        }

        /// <summary>
        /// A LoF joining two bind points of one LoW toughens the smaller arc of that LoW between them
        /// (DESIGN.md section 4.2). Returns the first such LoF that is still standing, or null.
        /// </summary>
        private ForbiddanceBody ShieldingChord(WardBody ward, Vec2 impact, Dictionary<int, int> used)
        {
            foreach (var body in _bodies.Values)
            {
                var forbiddance = body as ForbiddanceBody;
                if (forbiddance == null || forbiddance.ChordOf != ward.LineId) continue;
                if (!forbiddance.Line.IsActive || RemainingDurability(forbiddance, used) <= 0) continue;
                if (ChordShields(forbiddance.Segment, ward, impact)) return forbiddance;
            }
            return null;
        }

        /// <summary>
        /// True if the impact is on the arc cut off by the chord, on the far side from the centre. A chord
        /// through the centre shields the half facing the opponent (PROVISIONAL).
        /// </summary>
        private bool ChordShields(Segment chord, WardBody ward, Vec2 impact)
        {
            Vec2 along = chord.B - chord.A;
            float centreSide = Vec2.Cross(along, ward.Shape.Centre - chord.A);
            if (Math.Abs(centreSide) < 1e-3f * along.Length)
                return Vec2.Cross(along, impact - chord.A) * Vec2.Cross(along, ward.Shape.Centre + Facing(ward.Owner) - chord.A) > 0f;
            return Vec2.Cross(along, impact - chord.A) * centreSide < 0f;
        }

        /// <summary>
        /// A LoF joining two bind points of a LoW toughens the smaller arc it cuts off: a LoV hitting that arc
        /// deals only Forbiddance.ChordArcDamageMultiplier of its damage, rounded up, and the LoF takes the same
        /// (DESIGN.md section 4.2). Once the LoF is broken, the arc is no longer toughened.
        /// </summary>
        private void HitShieldedArc(VigorBody vigor, ForbiddanceBody forbiddance, WardBody ward, float time, Dictionary<int, int> used, MovementResult result)
        {
            int damage = ToughenedArcDamage(vigor.Line.Damage);
            HitWard(vigor, ward, damage, time, result, " (reduced by " + Name(forbiddance) + ")");
            DamageForbiddance(forbiddance, damage, vigor, time, used, result);
            _notes.Add(Name(forbiddance) + " took " + damage + " for toughening the arc.");
        }

        private static void DamageForbiddance(ForbiddanceBody forbiddance, int damage, VigorBody vigor, float time, Dictionary<int, int> used, MovementResult result)
        {
            used[forbiddance.LineId] = (used.ContainsKey(forbiddance.LineId) ? used[forbiddance.LineId] : 0) + damage;
            result.Damage.Add(new DamageEvent(forbiddance.LineId, damage, vigor.LineId, time));
        }

        private static int RemainingDurability(LineBody body, Dictionary<int, int> used)
        {
            int spent;
            used.TryGetValue(body.LineId, out spent);
            return body.Line.Durability - spent;
        }

        private static float DistanceOutside(WardBody ward, Vec2 point)
        {
            if (ward.Shape.Contains(point)) return 0f;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < ward.SampleCount; i++) nearest = Math.Min(nearest, Vec2.Distance(ward.SamplePoint(i), point));
            return nearest;
        }

        /// <summary>A main LoW must lie wholly on the board (and on the player's own half), with a valid shape.</summary>
        private string CheckMainWard(Player player, DrawingSpec spec, Placement placement, bool halfOnly)
        {
            if (spec.BindPoints == 2 && placement.SemiMinor > 0f
                && (placement.SemiMinor < _config.Geometry.EllipseMinSemiMinor - 1e-4f || placement.SemiMinor > _config.Geometry.EllipseMaxSemiMinor + 1e-4f))
                return "That ellipse is too flat or too round.";
            IReadOnlyList<Vec2> bindPoints;
            var shape = WardGeometry(player, spec, placement, out bindPoints, true);
            const int steps = 64;
            for (int i = 0; i < steps; i++)
            {
                Vec2 point = shape.PointAt(2f * (float)Math.PI * i / steps);
                if (!OnBoard(point)) return "Your main Line of Warding must fit on the board.";
                if (halfOnly && !OnOwnHalf(player, point)) return "Your main Line of Warding must fit on your own half.";
            }
            return null;
        }

        /// <summary>
        /// Null if the whole outline is on the board (and the player's half) and clear of the player's other lines,
        /// touching only the LoW through the bind point it is bound at, otherwise why not.
        /// </summary>
        private string CheckOutline(Player player, WardShape shape, bool halfOnly, Vec2 boundAt)
        {
            if (shape == null) return "Pick a bind point on your main Line of Warding.";
            const int steps = 64;
            for (int i = 0; i < steps; i++)
            {
                Vec2 point = shape.PointAt(2f * (float)Math.PI * i / steps);
                if (!OnBoard(point)) return "It must fit on the board.";
                if (halfOnly && !OnOwnHalf(player, point)) return "It must fit on your own half of the board.";
            }
            if (OverlapsLines(player, shape, boundAt)) return "It can't overlap or touch your other lines, except at the bind point it is bound to.";
            return null;
        }

        private string CheckLength(float length)
        {
            var geometry = _config.Geometry;
            if (length < geometry.MinForbiddanceLength - 1e-4f) return "Make the line longer (at least " + geometry.MinForbiddanceLength + ").";
            if (length > geometry.MaxForbiddanceLength + 1e-4f) return "Make the line shorter (at most " + geometry.MaxForbiddanceLength + ").";
            return null;
        }

        /// <summary>
        /// A LoF is a rectangle as wide as <see cref="GeometryConfig.ForbiddanceWidth"/>: all four corners
        /// must be on the board (and the player's half, clear of the centre line, which is drawn as wide as a LoF).
        /// </summary>
        private string CheckRectangle(Player player, Segment segment, bool halfOnly, bool free = false)
        {
            float halfWidth = _config.Geometry.ForbiddanceWidth / 2f;
            Vec2 along = segment.B - segment.A;
            Vec2 side = along.Length > 1e-6f ? along.Perp.Normalized * halfWidth : Vec2.Zero;
            return CheckLoFCorner(player, segment.A + side, halfOnly, halfWidth) ?? CheckLoFCorner(player, segment.A - side, halfOnly, halfWidth)
                ?? CheckLoFCorner(player, segment.B + side, halfOnly, halfWidth) ?? CheckLoFCorner(player, segment.B - side, halfOnly, halfWidth)
                ?? (CrossesAWard(player, segment) || !ClearOfWards(player, segment, halfWidth, !free)
                    ? (free ? "A free Line of Forbiddance can't touch or overlap a Line of Warding."
                        : "A Line of Forbiddance can't overlap a Line of Warding. It can only touch one at a bind point: as a tangent there, or joining two bind points.") : null);
        }

        /// <summary>Half the thickness a LoW is drawn with.</summary>
        private float LineHalfWidth(WardBody ward)
        {
            var geometry = _config.Geometry;
            return (ward.Line.IsMainWard ? geometry.MainWardLineWidth : geometry.WardLineWidth) / 2f;
        }

        /// <summary>
        /// True if no part of the LoF, drawn <paramref name="halfWidth"/> either side of the segment, overlaps a LoW
        /// as drawn. With <paramref name="bound"/>, a LoW the segment meets at one of its bind points is exempt: there
        /// the two touch, as a tangent or a chord does, and <see cref="CrossesAWard"/> checks they only touch. A free
        /// LoF is never bound, so it may not touch any LoW, even at a bind point (DESIGN.md section 4.2).
        /// </summary>
        private bool ClearOfWards(Player player, Segment segment, float halfWidth, bool bound)
        {
            foreach (var ward in WardsOf(player))
            {
                bool touches = false;
                if (bound)
                    foreach (var point in ward.BindPoints) touches |= DistanceToSegment(point, segment) < SameBindPoint;
                if (touches) continue;
                float clearance = halfWidth + LineHalfWidth(ward);
                if (DistanceToSegment(ward.Shape.Centre, segment) > Extent(ward.Shape) + clearance) continue;
                int count = ward.SampleCount;
                for (int i = 0; i < count; i++)
                    if (SegmentDistance(segment.A, segment.B, ward.SamplePoint(i), ward.SamplePoint((i + 1) % count)) < clearance - 1e-4f)
                        return false;
            }
            return true;
        }

        /// <summary>The shortest distance between segments ab and cd: 0 if they cross.</summary>
        private static float SegmentDistance(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            float d1 = Cross(b - a, c - a), d2 = Cross(b - a, d - a), d3 = Cross(d - c, a - c), d4 = Cross(d - c, b - c);
            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f))) return 0f;
            return Math.Min(Math.Min(DistanceToSegment(a, new Segment(c, d)), DistanceToSegment(b, new Segment(c, d))),
                Math.Min(DistanceToSegment(c, new Segment(a, b)), DistanceToSegment(d, new Segment(a, b))));
        }

        private static float Cross(Vec2 u, Vec2 v)
        {
            return u.X * v.Y - u.Y * v.X;
        }

        /// <summary>Points this close to an outline (as a share of the shape's size) count as touching it, not crossing.</summary>
        private const float TouchTolerance = 0.005f;

        /// <summary>Inside (-1), outside (1), or touching the outline (0).</summary>
        private static int Side(WardShape shape, Vec2 point)
        {
            float level = shape.Level(point);
            if (level < 1f - TouchTolerance) return -1;
            if (level > 1f + TouchTolerance) return 1;
            return 0;
        }

        /// <summary>
        /// True if the segment goes through the outline of one of the player's LoW: part of it is inside
        /// and part outside. A tangent only touches, and a chord stays inside (DESIGN.md section 4.2).
        /// </summary>
        private bool CrossesAWard(Player player, Segment segment)
        {
            const int steps = 64;
            foreach (var ward in WardsOf(player))
            {
                bool inside = false, outside = false;
                // The ends count too: a tangent's end must not poke into a neighbouring LoW. A chord's ends sit on
                // the outline, which counts as touching.
                for (int i = 0; i <= steps; i++)
                {
                    int side = Side(ward.Shape, segment.A + (segment.B - segment.A) * ((float)i / steps));
                    inside |= side < 0;
                    outside |= side > 0;
                }
                if (inside && outside) return true;
            }
            return false;
        }

        /// <summary>
        /// True if the shape overlaps one of the player's LoW or crosses one of their LoF. It may touch the LoW
        /// through the bind point it is bound at, there; every other LoW it must keep clear of, even where a bind
        /// point of each would meet (decided 2026-10-27, DESIGN.md section 4.1).
        /// </summary>
        private bool OverlapsLines(Player player, WardShape shape, Vec2 boundAt)
        {
            const int steps = 64;
            foreach (var ward in WardsOf(player))
            {
                for (int i = 0; i < steps; i++)
                {
                    float t = 2f * (float)Math.PI * i / steps;
                    if (Side(ward.Shape, shape.PointAt(t)) < 0 || Side(shape, ward.Shape.PointAt(t)) < 0) return true;
                }
                bool boundToIt = OnOutline(ward, boundAt) && Side(shape, boundAt) == 0;
                if (!boundToIt && TooClose(shape, ward, LineHalfWidth(ward))) return true;
            }
            foreach (var body in _bodies.Values)
            {
                var forbiddance = body as ForbiddanceBody;
                if (forbiddance == null || forbiddance.Owner != player || !forbiddance.Line.IsActive) continue;
                for (int i = 0; i <= steps; i++)
                    if (Side(shape, forbiddance.Segment.A + (forbiddance.Segment.B - forbiddance.Segment.A) * ((float)i / steps)) < 0) return true;
                // A bound LoF that meets the new LoW at a bind point (a tangent there, say) touches it; any other,
                // free LoF included, must keep clear.
                bool touches = false;
                if (forbiddance.Line.Spec.BindPoints > 0)
                    foreach (var point in BindPointsOf(player))
                        touches |= Side(shape, point) == 0 && DistanceToSegment(point, forbiddance.Segment) < SameBindPoint;
                if (touches) continue;
                float clearance = _config.Geometry.ForbiddanceWidth / 2f + _config.Geometry.WardLineWidth / 2f;
                for (int i = 0; i < steps; i++)
                    if (SegmentDistance(forbiddance.Segment.A, forbiddance.Segment.B, shape.PointAt(2f * (float)Math.PI * i / steps),
                        shape.PointAt(2f * (float)Math.PI * (i + 1) / steps)) < clearance - 1e-4f) return true;
            }
            return false;
        }

        /// <summary>True if a new LoW with this shape, drawn as thick as any LoW but the main one, would overlap the outline of this one as drawn.</summary>
        private bool TooClose(WardShape shape, WardBody ward, float wardHalfWidth)
        {
            const int steps = 64;
            float clearance = _config.Geometry.WardLineWidth / 2f + wardHalfWidth;
            if (Vec2.Distance(shape.Centre, ward.Shape.Centre) > Extent(shape) + Extent(ward.Shape) + clearance) return false;
            int count = ward.SampleCount;
            var outline = new Vec2[count];
            for (int j = 0; j < count; j++) outline[j] = ward.SamplePoint(j);
            Vec2 a = shape.PointAt(0f);
            for (int i = 1; i <= steps; i++)
            {
                Vec2 b = shape.PointAt(2f * (float)Math.PI * i / steps);
                for (int j = 0; j < count; j++)
                    if (SegmentDistance(a, b, outline[j], outline[(j + 1) % count]) < clearance - 1e-4f) return true;
                a = b;
            }
            return false;
        }

        /// <summary>How far the shape's outline reaches from its centre, at most.</summary>
        private static float Extent(WardShape shape)
        {
            return Math.Max(shape.A, shape.B);
        }

        private string CheckLoFCorner(Player player, Vec2 point, bool halfOnly, float halfWidth)
        {
            string error = CheckPoint(player, point, halfOnly);
            if (error != null || !halfOnly) return error;
            return Math.Abs(point.X - Width / 2f) < halfWidth - 1e-4f ? "A Line of Forbiddance can't touch the centre line." : null;
        }

        private string CheckPoint(Player player, Vec2 point, bool halfOnly)
        {
            if (!OnBoard(point)) return "That point is off the board.";
            if (halfOnly && !OnOwnHalf(player, point)) return "Draw on your own half of the board.";
            return null;
        }

        private static string ValidBindPoint(int index, IReadOnlyList<Vec2> bindPoints)
        {
            if (bindPoints.Count == 0) return "Draw your main Line of Warding first.";
            return InRange(index, bindPoints) ? null : "Pick a bind point on one of your Lines of Warding.";
        }

        private static bool InRange(int index, IReadOnlyList<Vec2> list)
        {
            return index >= 0 && index < list.Count;
        }

        private static string Name(LineBody body)
        {
            return TurnNarrator.LineName(body.Line);
        }
    }
}
