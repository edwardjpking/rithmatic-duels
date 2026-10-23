using System;

namespace RithmaticDuels.Rules
{
    /// <summary>
    /// What a player commits to when starting a drawing. Geometry-free: bind points are counts
    /// only, and where things sit on the board is left to the geometry layer.
    /// The opponent sees the line type of a drawing in progress but never this spec.
    /// </summary>
    [Serializable]
    public sealed class DrawingSpec
    {
        public LineType Type;

        /// <summary>LoW and LoF: bind points on the line. LoM: bind points the chalkling is bound to.</summary>
        public int BindPoints;

        /// <summary>LoW only: a Mark's Cross rather than a full circle or ellipse.</summary>
        public bool IsMarksCross;

        /// <summary>LoV only.</summary>
        public VigorTier Tier;

        /// <summary>LoM only: stat buffs spent on each chalkling stat, and glyphs drawn.</summary>
        public int DurabilityBuffs;
        public int DamageBuffs;
        public int SpeedBuffs;
        public int Glyphs;

        /// <summary>
        /// LoM only. PROVISIONAL: the stat a positive modifier ("+1 to a stat of choice") goes to is
        /// chosen up front with the spec, since the roll happens when nobody is choosing.
        /// </summary>
        public ChalklingStat PositiveModifierStat;

        public int TotalStatBuffs { get { return DurabilityBuffs + DamageBuffs + SpeedBuffs; } }

        public static DrawingSpec Warding(int bindPoints)
        {
            return new DrawingSpec { Type = LineType.Warding, BindPoints = bindPoints };
        }

        public static DrawingSpec MarksCross(RulesConfig config)
        {
            return new DrawingSpec { Type = LineType.Warding, BindPoints = config.Warding.MarksCrossBindPoints, IsMarksCross = true };
        }

        public static DrawingSpec Forbiddance(int bindPoints)
        {
            return new DrawingSpec { Type = LineType.Forbiddance, BindPoints = bindPoints };
        }

        public static DrawingSpec Vigor(VigorTier tier)
        {
            return new DrawingSpec { Type = LineType.Vigor, Tier = tier };
        }

        public static DrawingSpec Making(
            int durabilityBuffs = 0, int damageBuffs = 0, int speedBuffs = 0, int glyphs = 0,
            int boundBindPoints = 0, ChalklingStat positiveModifierStat = ChalklingStat.Durability)
        {
            return new DrawingSpec
            {
                Type = LineType.Making,
                DurabilityBuffs = durabilityBuffs,
                DamageBuffs = damageBuffs,
                SpeedBuffs = speedBuffs,
                Glyphs = glyphs,
                BindPoints = boundBindPoints,
                PositiveModifierStat = positiveModifierStat,
            };
        }

        public DrawingSpec Clone()
        {
            return (DrawingSpec)MemberwiseClone();
        }
    }
}
