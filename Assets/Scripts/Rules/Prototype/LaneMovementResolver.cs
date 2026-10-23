using System;
using System.Collections.Generic;

namespace RithmaticDuels.Rules.Prototype
{
    /// <summary>Settings for the prototype lane. Placeholders, like every other number.</summary>
    [Serializable]
    public sealed class LaneConfig
    {
        /// <summary>
        /// Distance a LoV travels from its owner's main LoW to the opponent's. 0 means derive it from
        /// the board: width minus both main LoW insets and radii.
        /// </summary>
        public float Length = 0f;

        /// <summary>A LoV reaching a player who has any LoF in effect is reflected away and does no damage.</summary>
        public bool ForbiddanceReflectsVigor = true;
    }

    /// <summary>
    /// PROTOTYPE stand-in for the geometry layer, so the hot-seat duel can be played before the real
    /// board exists (build plan step 2). It is not a rule from DESIGN.md and will be replaced.
    ///
    /// The board is collapsed to one straight lane between the two main LoW. Each completed LoV flies
    /// down the lane at its speed. Two opposing LoV that meet collide using <see cref="VigorRules"/>.
    /// A LoV that reaches the far end is reflected away if the opponent has any LoF in effect,
    /// otherwise it hits the opponent's main LoW (anywhere on it; there are no hit points yet).
    /// Chalklings sit idle, since they need real positions to move and attack.
    /// </summary>
    public sealed class LaneMovementResolver : IMovementResolver
    {
        private readonly LaneConfig _lane;
        private readonly Dictionary<int, float> _travelled = new Dictionary<int, float>();
        private readonly List<string> _notes = new List<string>();

        /// <summary>What happened on the lane during the last tick, in order, for the duel log.</summary>
        public IReadOnlyList<string> LastTickNotes { get { return _notes; } }

        public LaneMovementResolver(LaneConfig lane = null)
        {
            _lane = lane ?? new LaneConfig();
        }

        public float LaneLength(RulesConfig config)
        {
            if (_lane.Length > 0f) return _lane.Length;
            return config.Board.Width - 2f * (config.Board.MainWardInset + config.Warding.DefaultMainRadius);
        }

        /// <summary>How far a LoV has flown from its owner's end of the lane.</summary>
        public float DistanceTravelled(int lineId)
        {
            float distance;
            return _travelled.TryGetValue(lineId, out distance) ? distance : 0f;
        }

        public void ResolveMovementAndAttacks(Duel duel, MovementResult result)
        {
            _notes.Clear();
            var config = duel.Config;
            float length = LaneLength(config);

            var flying = new List<Line>();
            foreach (var line in duel.Lines)
            {
                if (line.Type != LineType.Vigor || !line.IsActive) continue;
                flying.Add(line);
                if (!_travelled.ContainsKey(line.Id)) _travelled[line.Id] = 0f;
            }

            // Step through the tick one event at a time, earliest first, so a collision can change a
            // LoV's speed before it arrives and same-tick hits keep their time of impact.
            float now = 0f;
            while (true)
            {
                float next = float.PositiveInfinity;
                Line first = null;
                Line second = null;

                foreach (var line in flying)
                {
                    if (line.Speed <= 0) continue;
                    float at = now + (length - _travelled[line.Id]) / line.Speed;
                    if (at <= 1f && at < next) { next = at; first = line; second = null; }
                }

                foreach (var a in flying)
                {
                    if (a.Owner != Player.A) continue;
                    foreach (var b in flying)
                    {
                        if (b.Owner != Player.B) continue;
                        int closing = a.Speed + b.Speed;
                        float gap = length - _travelled[a.Id] - _travelled[b.Id];
                        if (closing <= 0 || gap < 0f) continue;
                        float at = now + gap / closing;
                        if (at <= 1f && at < next) { next = at; first = a; second = b; }
                    }
                }

                if (first == null)
                {
                    Advance(flying, 1f - now);
                    break;
                }

                Advance(flying, next - now);
                now = next;

                if (second != null) Collide(first, second, flying, config);
                else Arrive(duel, first, now, result, flying);
            }

            var gone = new List<int>();
            foreach (var id in _travelled.Keys)
            {
                var line = duel.FindLine(id);
                if (line == null || !line.IsActive || result.DestroyedLineIds.Contains(id)) gone.Add(id);
            }
            foreach (var id in gone) _travelled.Remove(id);
        }

        private void Advance(List<Line> flying, float time)
        {
            foreach (var line in flying)
                _travelled[line.Id] += line.Speed * time;
        }

        private void Collide(Line a, Line b, List<Line> flying, RulesConfig config)
        {
            string names = TurnNarrator.LineName(a) + " and " + TurnNarrator.LineName(b);
            var destroyed = VigorRules.Collide(a, b, config);
            foreach (var id in destroyed) flying.RemoveAll(line => line.Id == id);

            if (destroyed.Length == 2) _notes.Add(names + " collided and cancelled each other out.");
            else
            {
                var survivor = destroyed[0] == a.Id ? b : a;
                _notes.Add(names + " collided. " + TurnNarrator.LineName(survivor) + " survived, weakened to "
                    + survivor.Tier + " (" + survivor.Damage + " damage).");
            }
        }

        private void Arrive(Duel duel, Line vigor, float time, MovementResult result, List<Line> flying)
        {
            flying.Remove(vigor);
            result.DestroyedLineIds.Add(vigor.Id);

            var target = duel.GetPlayer(vigor.Owner.Opponent());
            if (_lane.ForbiddanceReflectsVigor)
            {
                foreach (var line in duel.Lines)
                {
                    if (line.Owner != target.Player || !line.IsActive || line.Type != LineType.Forbiddance) continue;
                    _notes.Add(TurnNarrator.LineName(vigor) + " was reflected away by " + TurnNarrator.LineName(line) + ".");
                    return;
                }
            }

            var ward = target.MainWard;
            if (ward == null || !ward.IsActive)
            {
                _notes.Add(TurnNarrator.LineName(vigor) + " reached the far end and hit nothing.");
                return;
            }

            result.Damage.Add(new DamageEvent(ward.Id, vigor.Damage, vigor.Id, time));
            _notes.Add(TurnNarrator.LineName(vigor) + " hit " + TurnNarrator.LineName(ward) + ".");
        }
    }
}
