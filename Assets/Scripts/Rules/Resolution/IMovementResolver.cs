using System.Collections.Generic;

namespace RithmaticDuels.Rules
{
    public struct DamageEvent
    {
        public int TargetLineId;
        public int Amount;
        /// <summary>The line that dealt the damage (a LoV or chalkling), or -1 if none.</summary>
        public int SourceLineId;
        /// <summary>When within the tick the hit happened, from 0 (start) to 1 (end). Orders same-tick breaches.</summary>
        public float TimeOfImpact;

        public DamageEvent(int targetLineId, int amount, int sourceLineId = -1, float timeOfImpact = 0f)
        {
            TargetLineId = targetLineId;
            Amount = amount;
            SourceLineId = sourceLineId;
            TimeOfImpact = timeOfImpact;
        }
    }

    /// <summary>
    /// Sets a line's durability and maximum outright, before any damage this tick. The geometry layer uses
    /// it for a LoW, whose durability is per point: the rules core keeps the weakest point's values.
    /// </summary>
    public struct DurabilityOverride
    {
        public int LineId;
        public int Durability;
        public int MaxDurability;

        public DurabilityOverride(int lineId, int durability, int maxDurability)
        {
            LineId = lineId;
            Durability = durability;
            MaxDurability = maxDurability;
        }
    }

    public sealed class MovementResult
    {
        public List<DamageEvent> Damage { get; private set; }
        public List<int> DestroyedLineIds { get; private set; }
        public List<DurabilityOverride> DurabilityOverrides { get; private set; }

        public MovementResult()
        {
            Damage = new List<DamageEvent>();
            DestroyedLineIds = new List<int>();
            DurabilityOverrides = new List<DurabilityOverride>();
        }
    }

    /// <summary>
    /// Stage 3 of tick resolution (animated lines take actions) needs geometry: where LoV travel,
    /// reflections off LoF, chalkling pathing. The rules core is geometry-free, so it hands that stage to
    /// this interface and applies the collisions it reports. Rules that need no geometry, such as
    /// LoV-on-LoV collisions and how far a chalkling is pushed, live in <see cref="VigorRules"/>.
    /// </summary>
    public interface IMovementResolver
    {
        void ResolveMovementAndAttacks(Duel duel, MovementResult result);

        /// <summary>
        /// Called once lines that finish after movement (stage 4) are on the ground, so the geometry layer
        /// can place them. Only <see cref="MovementResult.DurabilityOverrides"/> is read from the result.
        /// </summary>
        void PlaceNewLines(Duel duel, MovementResult result);
    }

    public sealed class NoMovementResolver : IMovementResolver
    {
        public void ResolveMovementAndAttacks(Duel duel, MovementResult result) { }
        public void PlaceNewLines(Duel duel, MovementResult result) { }
    }
}
