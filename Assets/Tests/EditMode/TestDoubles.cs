using System;
using System.Collections.Generic;

namespace RithmaticDuels.Rules.Tests
{
    /// <summary>Returns queued values, then a fallback, so tests can force specific modifier rolls.</summary>
    internal sealed class ScriptedRandom : IRandomSource
    {
        private readonly Queue<int> _values;
        private readonly int _fallback;

        public ScriptedRandom(int fallback, params int[] values)
        {
            _fallback = fallback;
            _values = new Queue<int>(values);
        }

        public int NextInt(int maxExclusive)
        {
            int value = _values.Count > 0 ? _values.Dequeue() : _fallback;
            if (value < 0 || value >= maxExclusive) throw new InvalidOperationException("Scripted value out of range.");
            return value;
        }
    }

    /// <summary>Stands in for the geometry layer: reports whatever collisions the test queues for the next tick.</summary>
    internal sealed class ScriptedMovement : IMovementResolver
    {
        public readonly List<DamageEvent> NextDamage = new List<DamageEvent>();
        public readonly List<int> NextDestroyed = new List<int>();
        public int Calls;

        public void ResolveMovementAndAttacks(Duel duel, MovementResult result)
        {
            Calls++;
            result.Damage.AddRange(NextDamage);
            result.DestroyedLineIds.AddRange(NextDestroyed);
            NextDamage.Clear();
            NextDestroyed.Clear();
        }

        public void PlaceNewLines(Duel duel, MovementResult result)
        {
        }

        public void FindUnboundLines(Duel duel, System.Collections.Generic.List<int> lost)
        {
        }
    }

    internal static class Rolls
    {
        // With the default odds over 24: [0,3) is -1, [3,6) is +1, the rest is no modifier.
        public const int NoModifier = 23;
    }
}
