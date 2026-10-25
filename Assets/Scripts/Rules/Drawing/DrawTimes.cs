using System;

namespace RithmaticDuels.Rules
{
    /// <summary>Draw-time and validity formulas from DESIGN.md sections 4 and 5.</summary>
    public static class DrawTimes
    {
        /// <summary>Turns a drawing takes before rush/steady is applied.</summary>
        public static int BaseTurns(DrawingSpec spec, RulesConfig config)
        {
            int turns;
            switch (spec.Type)
            {
                case LineType.Warding:
                    turns = spec.IsMarksCross
                        ? config.Warding.MarksCrossDrawTurns
                        : spec.BindPoints / config.Warding.BindPointsPerDrawTurn;
                    break;
                case LineType.Forbiddance:
                    float length = spec.Length > 0f ? spec.Length : config.Geometry.TangentForbiddanceLength;
                    turns = (int)Math.Ceiling(length / config.Forbiddance.LengthPerDrawTurn - 1e-4)
                        - spec.BindPoints * config.Forbiddance.DrawTurnsSavedPerBindPoint;
                    break;
                case LineType.Vigor:
                    turns = config.Vigor.Tier(spec.Tier).DrawTurns;
                    break;
                case LineType.Making:
                    int perTurn = Math.Max(1, config.Making.StatBuffsPerDrawTurn);
                    turns = (spec.TotalStatBuffs + perTurn - 1) / perTurn
                        + Math.Max(0, spec.Glyphs - config.Making.FreeGlyphs) * config.Making.DrawTurnsPerGlyph;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(spec));
            }
            return Math.Max(config.MinimumWorkTurns, turns);
        }

        /// <summary>Turns a drawing takes once rush (-50%, rounded up) or steady (+50%) is applied, plus travel.</summary>
        public static int Turns(DrawingSpec spec, DrawingPace pace, RulesConfig config)
        {
            return ApplyPace(BaseTurns(spec, config), pace, config) + spec.TravelTurns;
        }

        public static int ApplyPace(int baseTurns, DrawingPace pace, RulesConfig config)
        {
            var settings = config.PaceSettings(pace);
            if (settings == null) return baseTurns;
            return Math.Max(config.MinimumWorkTurns, Round(baseTurns * settings.TimeMultiplier, settings.Rounding));
        }

        /// <summary>Most glyphs a chalkling with this many stat buffs may carry.</summary>
        public static int MaxGlyphs(int statBuffs, RulesConfig config)
        {
            return config.Making.FreeGlyphs + statBuffs / config.Making.StatBuffsPerExtraGlyph;
        }

        /// <summary>Returns null when the spec is valid, otherwise a reason it is not.</summary>
        public static string Validate(DrawingSpec spec, RulesConfig config)
        {
            if (spec == null) return "No drawing spec given.";
            if (spec.BindPoints < 0) return "Bind points cannot be negative.";
            if (spec.TravelTurns < 0) return "Travel turns cannot be negative.";
            if (spec.Length < 0f) return "Length cannot be negative.";

            switch (spec.Type)
            {
                case LineType.Warding:
                    if (spec.BindPoints > config.Warding.MaxBindPoints)
                        return "A Line of Warding has at most " + config.Warding.MaxBindPoints + " bind points.";
                    break;
                case LineType.Making:
                    if (spec.DurabilityBuffs < 0 || spec.DamageBuffs < 0 || spec.SpeedBuffs < 0 || spec.Glyphs < 0)
                        return "Stat buffs and glyphs cannot be negative.";
                    if (spec.Glyphs > MaxGlyphs(spec.TotalStatBuffs, config))
                        return "A chalkling may carry " + config.Making.FreeGlyphs + " glyph plus one per "
                            + config.Making.StatBuffsPerExtraGlyph + " stat buffs.";
                    break;
            }
            return null;
        }

        private static int Round(double value, RoundingMode mode)
        {
            // Small epsilon so 3 × 1.5 = 4.5 style products don't drift across a boundary.
            const double epsilon = 1e-9;
            switch (mode)
            {
                case RoundingMode.Down: return (int)Math.Floor(value + epsilon);
                case RoundingMode.Nearest: return (int)Math.Floor(value + 0.5 + epsilon);
                default: return (int)Math.Ceiling(value - epsilon);
            }
        }
    }
}
