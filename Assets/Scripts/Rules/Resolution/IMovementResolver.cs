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

    public sealed class MovementResult
    {
        public List<DamageEvent> Damage { get; private set; }
        public List<int> DestroyedLineIds { get; private set; }

        public MovementResult()
        {
            Damage = new List<DamageEvent>();
            DestroyedLineIds = new List<int>();
        }
    }

    /// <summary>
    /// Step 2 of tick resolution (movement and attacks) needs geometry: where LoV travel, reflections
    /// off LoF, chalkling pathing. The rules core is geometry-free, so it hands that step to this
    /// interface and applies the collisions it reports in step 3. Rules that need no geometry, such as
    /// LoV-on-LoV collisions and how far a chalkling is pushed, live in <see cref="VigorRules"/>.
    /// </summary>
    public interface IMovementResolver
    {
        void ResolveMovementAndAttacks(Duel duel, MovementResult result);
    }

    public sealed class NoMovementResolver : IMovementResolver
    {
        public void ResolveMovementAndAttacks(Duel duel, MovementResult result) { }
    }
}
