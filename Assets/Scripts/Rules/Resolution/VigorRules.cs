using System;

namespace RithmaticDuels.Rules
{
    /// <summary>
    /// LoV rules that need no geometry (DESIGN.md section 4.3). The geometry layer detects the
    /// collision and calls these to find out what happens.
    /// </summary>
    public static class VigorRules
    {
        /// <summary>
        /// Two LoV collide and absorb each other's energy. The lower tier line is destroyed and the
        /// higher drops by the lower's tier (High hitting Low becomes Medium); equal tiers destroy each
        /// other. A survivor left at 0 damage fizzles too. Returns the ids of the lines destroyed.
        /// </summary>
        public static int[] Collide(Line a, Line b, RulesConfig config)
        {
            if (a.Type != LineType.Vigor || b.Type != LineType.Vigor)
                throw new ArgumentException("Both lines must be Lines of Vigor.");

            int rankA = Rank(a.Tier);
            int rankB = Rank(b.Tier);
            if (rankA == rankB)
            {
                a.Status = LineStatus.Destroyed;
                b.Status = LineStatus.Destroyed;
                return new[] { a.Id, b.Id };
            }

            var higher = rankA > rankB ? a : b;
            var lower = rankA > rankB ? b : a;
            lower.Status = LineStatus.Destroyed;
            higher.SetVigorTier(FromRank(Math.Abs(rankA - rankB)), config);
            if (higher.Damage <= 0)
            {
                higher.Status = LineStatus.Destroyed;
                return new[] { lower.Id, higher.Id };
            }
            return new[] { lower.Id };
        }

        /// <summary>How far a LoV pushes a chalkling that survives its hit: 2 × damage / max durability, capped at 3.</summary>
        public static float PushDistance(int damage, int chalklingMaxDurability, RulesConfig config)
        {
            if (damage <= 0 || chalklingMaxDurability <= 0) return 0f;
            float push = config.Vigor.PushPerDamage * damage / chalklingMaxDurability;
            return Math.Min(push, config.Vigor.MaxPush);
        }

        private static int Rank(VigorTier tier)
        {
            return (int)tier + 1;
        }

        private static VigorTier FromRank(int rank)
        {
            return (VigorTier)(rank - 1);
        }
    }
}
