using System;
using System.Collections.Generic;

namespace RithmaticDuels.Rules.Geometry
{
    /// <summary>
    /// Chalkling behaviour in stage 3 of the tick (DESIGN.md section 4.4): after the LoV fly, every chalkling moves at
    /// once, then every chalkling attacks at once, so the order they are processed in never matters.
    /// </summary>
    public sealed partial class ChalkBoard
    {
        // How close to a line a chalkling's edge stops, so it never ends up inside it.
        private const float ChalklingGap = 0.01f;

        /// <summary>What stands in a chalkling's way along a straight line.</summary>
        private struct ChalklingBlock
        {
            /// <summary>How far its centre can go before its edge touches the blocker.</summary>
            public float Distance;
            /// <summary>A LoW, LoF or enemy chalkling; null for the centre LoF or nothing.</summary>
            public LineBody Body;
            public bool CentreWall;
            /// <summary>Where on the blocker the chalkling's path meets it.</summary>
            public Vec2 Contact;
        }

        /// <summary>
        /// The straight path an Attack chalkling of this player would take from here, for the preview (DESIGN.md section
        /// 4.4): towards the nearest point of the opponent's main LoW, stopping at the first thing that blocks it. The first
        /// point is the start. Just the start if the opponent has no main LoW.
        /// </summary>
        public List<Vec2> ChalklingAttackPath(Player owner, Vec2 from)
        {
            var path = new List<Vec2> { from };
            Vec2 target;
            if (!AttackTarget(owner, from, out target)) return path;
            Vec2 direction = (target - from).Normalized;
            var block = FirstChalklingBlock(owner, from, direction, null, new Dictionary<int, int>(), -1);
            float distance = Math.Min(Vec2.Distance(from, target), Math.Max(0f, block.Distance));
            path.Add(from + direction * distance);
            return path;
        }

        private void ActChalklings(MovementResult result, Dictionary<int, int> used)
        {
            var chalklings = new List<ChalklingBody>();
            foreach (var body in _bodies.Values)
            {
                var chalkling = body as ChalklingBody;
                if (chalkling == null) continue;
                chalkling.LastPath.Clear();
                chalkling.LastPath.Add(chalkling.Position);
                if (chalkling.Line.IsActive && RemainingDurability(chalkling, used) > 0) chalklings.Add(chalkling);
            }

            // Everyone decides where to go from where everyone stands, then all move together.
            var destinations = new Dictionary<int, Vec2>();
            foreach (var chalkling in chalklings) destinations[chalkling.LineId] = Destination(chalkling, chalklings, used);
            foreach (var chalkling in chalklings)
            {
                Vec2 to = destinations[chalkling.LineId];
                if (Vec2.Distance(to, chalkling.Position) > 1e-5f)
                {
                    chalkling.Position = to;
                    chalkling.LastPath.Add(to);
                }
            }

            foreach (var chalkling in chalklings)
                ReleaseBehind(chalkling, chalkling.Position, _config.Making.Radius + _config.Geometry.ForbiddanceWidth / 2f);

            // Everyone picks a target from where they now stand, then all strike together, so two can destroy each other.
            var strikes = new List<KeyValuePair<ChalklingBody, ChalklingBlock>>();
            foreach (var chalkling in chalklings)
            {
                if (chalkling.Line.Damage <= 0) continue;
                ChalklingBlock target;
                if (PickTarget(chalkling, chalklings, used, out target))
                    strikes.Add(new KeyValuePair<ChalklingBody, ChalklingBlock>(chalkling, target));
            }
            foreach (var strike in strikes) Strike(strike.Key, strike.Value, used, result);
        }

        /// <summary>Where a chalkling ends this tick's move.</summary>
        private Vec2 Destination(ChalklingBody chalkling, List<ChalklingBody> all, Dictionary<int, int> used)
        {
            float speed = chalkling.Line.Speed;
            if (speed <= 0f) return chalkling.Position;
            Vec2 from = chalkling.Position;
            Vec2 goal;

            if (chalkling.Line.Glyph == Glyph.Attack)
            {
                // It stops to fight an enemy chalkling in contact, otherwise heads for the opponent's main LoW.
                if (NearestEnemyChalkling(chalkling, from, all, used, ContactDistance) != null) return from;
                if (!AttackTarget(chalkling.Owner, from, out goal)) return from;
            }
            else
            {
                // Defend: meet an enemy chalkling within its guard distance plus reach, else go back to its anchor.
                var enemy = NearestEnemyChalkling(chalkling, chalkling.Anchor, all, used, chalkling.GuardDistance + ContactDistance);
                if (enemy != null)
                {
                    Vec2 towards = enemy.Position - from;
                    float gap = towards.Length - 2f * _config.Making.Radius;
                    goal = gap > 0f ? from + towards.Normalized * gap : from;
                    Vec2 fromAnchor = goal - chalkling.Anchor;
                    if (fromAnchor.Length > chalkling.GuardDistance)
                        goal = chalkling.Anchor + fromAnchor.Normalized * chalkling.GuardDistance;
                }
                else goal = chalkling.Anchor;
            }

            Vec2 step = goal - from;
            float distance = Math.Min(step.Length, speed);
            if (distance <= 1e-5f) return from;
            Vec2 direction = step.Normalized;
            var block = FirstChalklingBlock(chalkling.Owner, from, direction, chalkling, used, distance);
            return from + direction * Math.Max(0f, Math.Min(distance, block.Distance));
        }

        /// <summary>What a chalkling attacks this tick, if anything in reach.</summary>
        private bool PickTarget(ChalklingBody chalkling, List<ChalklingBody> all, Dictionary<int, int> used, out ChalklingBlock target)
        {
            target = new ChalklingBlock();
            var enemy = NearestEnemyChalkling(chalkling, chalkling.Position, all, used, ContactDistance);
            if (enemy != null)
            {
                target.Body = enemy;
                return true;
            }
            if (chalkling.Line.Glyph != Glyph.Attack) return false;

            // An Attack chalkling scratches at whatever blocks its way to the opponent's main LoW, but never its own lines.
            Vec2 goal;
            if (!AttackTarget(chalkling.Owner, chalkling.Position, out goal)) return false;
            Vec2 direction = (goal - chalkling.Position).Normalized;
            var block = FirstChalklingBlock(chalkling.Owner, chalkling.Position, direction, chalkling, used, -1);
            if (block.Body == null || block.Body is ChalklingBody || block.Body.Owner == chalkling.Owner
                || block.Distance > _config.Making.AttackRange) return false;
            target = block;
            return true;
        }

        private void Strike(ChalklingBody attacker, ChalklingBlock target, Dictionary<int, int> used, MovementResult result)
        {
            int damage = attacker.Line.Damage;
            var ward = target.Body as WardBody;
            if (ward != null)
            {
                // A LoF toughening the arc it attacks is damaged first (DESIGN.md section 4.2).
                var shields = ShieldingChords(ward, target.Contact, used);
                if (shields.Count > 0)
                {
                    StrikeLine(attacker, shields[0], damage, used, result);
                    return;
                }
                int before = ward.Weakest;
                ward.ApplyHit(target.Contact, damage, _config.Warding.HitHalfWidth);
                result.Damage.Add(new DamageEvent(ward.LineId, before - ward.Weakest, attacker.LineId, 1f));
                _notes.Add(Name(attacker) + " attacked " + Name(ward) + " for " + damage + ".");
                return;
            }
            StrikeLine(attacker, target.Body, damage, used, result);
        }

        private void StrikeLine(ChalklingBody attacker, LineBody target, int damage, Dictionary<int, int> used, MovementResult result)
        {
            used[target.LineId] = (used.ContainsKey(target.LineId) ? used[target.LineId] : 0) + damage;
            result.Damage.Add(new DamageEvent(target.LineId, damage, attacker.LineId, 1f));
            _notes.Add(Name(attacker) + " attacked " + Name(target) + " for " + damage + ".");
        }

        /// <summary>Centre-to-centre distance at which two chalklings can strike each other.</summary>
        private float ContactDistance { get { return 2f * _config.Making.Radius + _config.Making.AttackRange; } }

        private ChalklingBody NearestEnemyChalkling(ChalklingBody self, Vec2 around, List<ChalklingBody> all, Dictionary<int, int> used, float within)
        {
            ChalklingBody nearest = null;
            float best = within + 1e-4f;
            foreach (var other in all)
            {
                if (other.Owner == self.Owner || RemainingDurability(other, used) <= 0) continue;
                float distance = Vec2.Distance(around, other.Position);
                if (distance <= best)
                {
                    best = distance;
                    nearest = other;
                }
            }
            return nearest;
        }

        /// <summary>The nearest point of the opponent's main LoW, worked out afresh from where the chalkling stands.</summary>
        private bool AttackTarget(Player owner, Vec2 from, out Vec2 target)
        {
            target = from;
            var ward = MainWardOf(owner.Opponent());
            if (ward == null || !ward.Line.IsActive) return false;
            float best = float.PositiveInfinity;
            for (int i = 0; i < ward.SampleCount; i++)
            {
                Vec2 point = ward.SamplePoint(i);
                float distance = Vec2.Distance(from, point);
                if (distance < best)
                {
                    best = distance;
                    target = point;
                }
            }
            return true;
        }

        /// <summary>
        /// The first thing that blocks a chalkling of this player going this way: an enemy LoW, any LoF (its own player's
        /// too), the centre LoF while it stands, or an enemy chalkling. Its own LoW and friendly chalklings let it through.
        /// <paramref name="reach"/> below 0 means no limit.
        /// </summary>
        private ChalklingBlock FirstChalklingBlock(Player owner, Vec2 from, Vec2 direction, ChalklingBody self, Dictionary<int, int> used, float reach)
        {
            float radius = _config.Making.Radius;
            var best = new ChalklingBlock { Distance = float.PositiveInfinity };
            float distance;
            Vec2 normal;

            if (CentreWallUp && Intersect.RaySegment(from, direction, CentreWall, out distance, out normal))
                Closer(ref best, distance - radius, null, true, from + direction * distance);

            foreach (var body in _bodies.Values)
            {
                if (!body.Line.IsActive || body == self) continue;

                var ward = body as WardBody;
                if (ward != null)
                {
                    if (ward.Owner != owner && !ward.IsBroken && ward.Shape.Raycast(from, direction, out distance))
                        Closer(ref best, distance - radius, ward, false, from + direction * distance);
                    continue;
                }

                var forbiddance = body as ForbiddanceBody;
                if (forbiddance != null)
                {
                    float side;
                    if (self != null && self.BehindForbiddance.TryGetValue(forbiddance.LineId, out side))
                    {
                        // Finished on top of it: it blocks at once if this way leads across it from its side, else not at all.
                        if (RemainingDurability(forbiddance, used) > 0 && Vec2.Dot(direction, LeftNormal(forbiddance.Segment)) * side < 0f)
                            Closer(ref best, 0f, forbiddance, false, ClosestOnSegment(from, forbiddance.Segment));
                        continue;
                    }
                    if (RemainingDurability(forbiddance, used) > 0 && Intersect.RaySegment(from, direction, forbiddance.Segment, out distance, out normal))
                        Closer(ref best, distance - radius, forbiddance, false, from + direction * distance);
                    continue;
                }

                var chalkling = body as ChalklingBody;
                if (chalkling != null && chalkling.Owner != owner && RemainingDurability(chalkling, used) > 0
                    && Intersect.RayCircle(from, direction, chalkling.Position, 2f * radius, out distance))
                    Closer(ref best, distance, chalkling, false, from + direction * distance);
            }

            best.Distance = Math.Max(0f, best.Distance - ChalklingGap);
            if (reach >= 0f && best.Distance > reach) return new ChalklingBlock { Distance = float.PositiveInfinity };
            return best;
        }

        private static Vec2 ClosestOnSegment(Vec2 point, Segment segment)
        {
            Vec2 along = segment.B - segment.A;
            float lengthSquared = Vec2.Dot(along, along);
            float t = lengthSquared < 1e-9f ? 0f : Math.Max(0f, Math.Min(1f, Vec2.Dot(point - segment.A, along) / lengthSquared));
            return segment.A + along * t;
        }

        private static void Closer(ref ChalklingBlock best, float distance, LineBody body, bool centreWall, Vec2 contact)
        {
            if (distance >= best.Distance) return;
            best = new ChalklingBlock { Distance = distance, Body = body, CentreWall = centreWall, Contact = contact };
        }
    }
}
