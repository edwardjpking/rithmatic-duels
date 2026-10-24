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
        }

        public float Width { get { return _config.Board.Width; } }
        public float Height { get { return _config.Board.Height; } }

        /// <summary>Every line on the board, in the order they were completed.</summary>
        public IEnumerable<LineBody> Bodies { get { return _bodies.Values; } }

        /// <summary>What happened on the board during the last tick, in order, for the duel log.</summary>
        public IReadOnlyList<string> LastTickNotes { get { return _notes; } }

        public IReadOnlyList<VigorTrail> LastTickTrails { get { return _trails; } }

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

        /// <summary>Bind points of a player's main LoW, or none if it isn't drawn yet.</summary>
        public IReadOnlyList<Vec2> BindPointsOf(Player player)
        {
            var main = MainWardOf(player);
            return main != null ? main.BindPoints : new List<Vec2>();
        }

        public LineBody BodyOf(int lineId)
        {
            LineBody body;
            return _bodies.TryGetValue(lineId, out body) ? body : null;
        }

        /// <summary>Record where the drawing a player is starting will go. Call it with every Start drawing.</summary>
        public void SetPendingPlacement(Player player, DrawingSpec spec, Placement placement)
        {
            _pending[(int)player] = new PendingPlacement
            {
                IsSet = true,
                Type = spec.Type,
                Placement = (placement ?? Placement.Default()).Clone(),
            };
        }

        /// <summary>
        /// Returns null if this placement works for this drawing now, otherwise why not. The spec's bind
        /// point count must match the placement, since it sets draw time and durability.
        /// </summary>
        public string ValidatePlacement(Player player, DrawingSpec spec, Placement placement)
        {
            if (spec == null) return "No drawing spec given.";
            if (placement == null || placement.Kind == PlacementKind.Default) return null;
            var bindPoints = BindPointsOf(player);
            bool halfOnly = _config.Geometry.DrawOnOwnHalfOnly;

            switch (spec.Type)
            {
                case LineType.Warding:
                    if (MainWardOf(player) == null)
                        return spec.IsMarksCross ? "Draw your main Line of Warding first." : null;
                    if (placement.Kind != PlacementKind.AtBindPoint) return "Pick a bind point on your main Line of Warding.";
                    return ValidBindPoint(placement.BindPointA, bindPoints);

                case LineType.Forbiddance:
                    switch (placement.Kind)
                    {
                        case PlacementKind.BetweenBindPoints:
                            if (spec.BindPoints != 2) return "A line between two bind points has 2 bind points.";
                            if (placement.BindPointA == placement.BindPointB) return "Pick two different bind points.";
                            return ValidBindPoint(placement.BindPointA, bindPoints) ?? ValidBindPoint(placement.BindPointB, bindPoints);
                        case PlacementKind.TangentAtBindPoint:
                            if (spec.BindPoints != 1) return "A tangent at one bind point has 1 bind point.";
                            return ValidBindPoint(placement.BindPointA, bindPoints);
                        case PlacementKind.FreeSegment:
                            if (spec.BindPoints != 0) return "A free-standing line has no bind points.";
                            if (Vec2.Distance(placement.PointA, placement.PointB) < 0.5f) return "Make the line longer.";
                            return CheckPoint(player, placement.PointA, halfOnly) ?? CheckPoint(player, placement.PointB, halfOnly);
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

        /// <summary>The segment a LoF would occupy with this placement, for previews. Null if it can't be placed.</summary>
        public Segment? PreviewForbiddance(Player player, Placement placement)
        {
            return ForbiddanceGeometry(player, placement);
        }

        public void ResolveMovementAndAttacks(Duel duel, MovementResult result)
        {
            _notes.Clear();
            _trails.Clear();
            Sync(duel, result);
            FlyVigor(result);
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
                    body = CreateBody(line, TakePending(line));
                    _bodies[line.Id] = body;
                    var newWard = body as WardBody;
                    if (newWard != null)
                        result.DurabilityOverrides.Add(new DurabilityOverride(line.Id, newWard.Weakest, newWard.WeakestMax));
                    continue;
                }

                // Repairs and chalk fatigue change the rules-side durability; spread them over the whole line.
                var ward = body as WardBody;
                if (ward == null) continue;
                int delta = line.Durability - ward.Weakest;
                if (delta != 0) ward.ApplyEverywhere(delta);
                if (ward.Weakest != line.Durability || ward.WeakestMax != line.MaxDurability)
                    result.DurabilityOverrides.Add(new DurabilityOverride(line.Id, ward.Weakest, ward.WeakestMax));
            }
        }

        private Placement TakePending(Line line)
        {
            var pending = _pending[(int)line.Owner];
            _pending[(int)line.Owner] = default(PendingPlacement);
            if (!pending.IsSet || pending.Type != line.Type) return Placement.Default();
            return pending.Placement;
        }

        private LineBody CreateBody(Line line, Placement placement)
        {
            var owner = line.Owner;
            switch (line.Type)
            {
                case LineType.Warding:
                    IReadOnlyList<Vec2> bindPoints;
                    var shape = WardGeometry(owner, line.Spec, placement, out bindPoints, line.IsMainWard);
                    return new WardBody(line, shape, bindPoints, _config);

                case LineType.Forbiddance:
                    var segment = ForbiddanceGeometry(owner, placement) ?? ForbiddanceGeometry(owner, Placement.Default()).Value;
                    return new ForbiddanceBody(line, segment);

                case LineType.Vigor:
                    if (placement.Kind == PlacementKind.Aimed)
                        return new VigorBody(line, placement.PointA, placement.PointB - placement.PointA);
                    return new VigorBody(line, Home(owner) + Facing(owner) * (FrontDistance(owner) + 1f), Facing(owner));

                default:
                    var main = MainWardOf(owner);
                    int bindPoint = placement.Kind == PlacementKind.AtBindPoint ? placement.BindPointA
                        : placement.Kind == PlacementKind.Default && line.Spec.BindPoints > 0 ? 0 : -1;
                    if (main != null && bindPoint >= 0 && bindPoint < main.BindPoints.Count)
                    {
                        Vec2 point = main.BindPoints[bindPoint];
                        Vec2 normal = main.Shape.OutwardNormalAt(main.Shape.ParamOf(point));
                        return new ChalklingBody(line, point + normal * (_config.Making.Radius + 0.05f), point);
                    }
                    if (placement.Kind == PlacementKind.FreePoint) return new ChalklingBody(line, placement.PointA, null);
                    return new ChalklingBody(line, Home(owner) + Facing(owner) * (FrontDistance(owner) + 1f), null);
            }
        }

        private WardShape WardGeometry(Player owner, DrawingSpec spec, Placement placement, out IReadOnlyList<Vec2> bindPoints, bool? isMain = null)
        {
            var main = MainWardOf(owner);
            bool becomesMain = isMain ?? (main == null && !spec.IsMarksCross);
            Vec2 facing = Facing(owner);

            if (becomesMain)
            {
                Vec2 home = Home(owner);
                if (spec.BindPoints == 2)
                {
                    var ellipse = WardShape.Ellipse(home, _config.Geometry.EllipseSemiMajor, _config.Geometry.EllipseSemiMinor, facing.Angle);
                    bindPoints = BindPoints.ForEllipse(ellipse);
                    return ellipse;
                }
                float radius = _config.Warding.DefaultMainRadius;
                bindPoints = BindPoints.ForCircle(spec.BindPoints, home, radius, facing);
                return WardShape.Circle(home, radius);
            }

            float attachedRadius = spec.IsMarksCross ? _config.Warding.MarksCrossRadius : _config.Geometry.AttachedWardRadius;
            int count = spec.IsMarksCross ? _config.Warding.MarksCrossBindPoints : spec.BindPoints;
            Vec2 centre;
            Vec2 outward;
            if (main == null)
            {
                // Nothing to attach to (only reachable without the UI): sit in front of home.
                outward = facing;
                centre = Home(owner) + facing * (_config.Warding.DefaultMainRadius + attachedRadius);
            }
            else
            {
                int index = placement != null && placement.Kind == PlacementKind.AtBindPoint ? placement.BindPointA : 0;
                if (index < 0 || index >= main.BindPoints.Count)
                {
                    if (placement != null && placement.Kind == PlacementKind.AtBindPoint) { bindPoints = null; return null; }
                    index = 0;
                }
                Vec2 point = main.BindPoints[index];
                outward = main.Shape.OutwardNormalAt(main.Shape.ParamOf(point));
                centre = point + outward * attachedRadius;
            }
            bindPoints = BindPoints.ForCircle(count, centre, attachedRadius, outward);
            return WardShape.Circle(centre, attachedRadius);
        }

        private Segment? ForbiddanceGeometry(Player owner, Placement placement)
        {
            var main = MainWardOf(owner);
            var bindPoints = BindPointsOf(owner);
            float length = _config.Geometry.TangentForbiddanceLength;

            switch (placement.Kind)
            {
                case PlacementKind.BetweenBindPoints:
                    if (!InRange(placement.BindPointA, bindPoints) || !InRange(placement.BindPointB, bindPoints)) return null;
                    return new Segment(bindPoints[placement.BindPointA], bindPoints[placement.BindPointB]);
                case PlacementKind.TangentAtBindPoint:
                    if (!InRange(placement.BindPointA, bindPoints)) return null;
                    return Tangent(main, placement.BindPointA, length);
                case PlacementKind.FreeSegment:
                    return new Segment(placement.PointA, placement.PointB);
                default:
                    if (main != null && main.BindPoints.Count > 0) return Tangent(main, 0, length);
                    Vec2 front = Home(owner) + Facing(owner) * (FrontDistance(owner) + 1f);
                    Vec2 across = Facing(owner).Perp * (length / 2f);
                    return new Segment(front - across, front + across);
            }
        }

        private static Segment Tangent(WardBody ward, int bindPoint, float length)
        {
            Vec2 point = ward.BindPoints[bindPoint];
            Vec2 along = ward.Shape.OutwardNormalAt(ward.Shape.ParamOf(point)).Perp * (length / 2f);
            return new Segment(point - along, point + along);
        }

        /// <summary>How far in front of home the main LoW reaches.</summary>
        private float FrontDistance(Player owner)
        {
            var main = MainWardOf(owner);
            return main != null ? main.Shape.A : _config.Warding.DefaultMainRadius;
        }

        // ---- Lines of Vigor in flight ----

        private enum HitKind { None, Ward, Forbiddance, Chalkling, Edge, Vigor }

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

            // Chalkling durability used up so far this tick; the rules core applies the damage afterwards.
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
                        hit.Vigor.Direction = Vec2.Reflect(hit.Vigor.Direction, hit.Normal).Normalized;
                        hit.Vigor.Position += hit.Vigor.Direction * Intersect.Epsilon;
                        _notes.Add(Name(hit.Vigor) + " was reflected by " + Name(hit.Other) + ".");
                        break;

                    case HitKind.Ward:
                        HitWard(hit.Vigor, (WardBody)hit.Other, now, result);
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
            int damage = vigor.Line.Damage;
            int before = ward.Weakest;
            ward.ApplyHit(vigor.Position, damage, _config.Warding.HitHalfWidth);
            int drop = before - ward.Weakest;

            // The rules core tracks the weakest point, so report how far that fell.
            result.Damage.Add(new DamageEvent(ward.LineId, drop, vigor.LineId, time));
            _notes.Add(Name(vigor) + " hit " + Name(ward) + " for " + damage + (drop == damage ? "." : " at a point stronger than its weakest."));
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

        private static int RemainingDurability(ChalklingBody chalkling, Dictionary<int, int> used)
        {
            int spent;
            used.TryGetValue(chalkling.LineId, out spent);
            return chalkling.Line.Durability - spent;
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
            return InRange(index, bindPoints) ? null : "Pick a bind point on your main Line of Warding.";
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
