using System;

namespace RithmaticDuels.Rules
{
    /// <summary>
    /// SplitMix64 generator. Written out by hand rather than using System.Random so the same seed
    /// gives the same duel on every runtime and platform (Mono, IL2CPP, .NET).
    /// </summary>
    public sealed class SeededRandom : IRandomSource
    {
        private ulong _state;

        public SeededRandom(ulong seed)
        {
            _state = seed;
        }

        public ulong NextULong()
        {
            ulong z = (_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));

            // Rejection sampling avoids modulo bias.
            ulong bound = (ulong)maxExclusive;
            ulong limit = ulong.MaxValue - (ulong.MaxValue % bound);
            ulong value;
            do
            {
                value = NextULong();
            } while (value >= limit);
            return (int)(value % bound);
        }
    }
}
