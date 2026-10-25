using System;
using System.Collections.Generic;

namespace RithmaticDuels.Rules
{
    /// <summary>
    /// Every tunable number in the rules lives here. All values are placeholders (DESIGN.md).
    /// Public fields keep this serializable by Unity, so a ScriptableObject in the presentation
    /// layer can wrap it and designers can rebalance without touching code.
    ///
    /// Fields tagged PROVISIONAL implement a default for a rule DESIGN.md has not settled. They are
    /// listed under "Rules core defaults" in DESIGN.md section 7 and can be flipped here once decided.
    /// </summary>
    [Serializable]
    public sealed class RulesConfig
    {
        /// <summary>Turns before the tiebreak decides the duel (DESIGN.md section 1). Chosen per duel within the range below.</summary>
        public int TurnLimit = 60;
        public int MinTurnLimit = 30;
        public int MaxTurnLimit = 120;

        /// <summary>The turn count starts on the first turn after both main LoW are complete.</summary>
        public bool TurnLimitStartsAfterBothMainWards = true;

        /// <summary>Per-turn decision time, enforced by the presentation layer. 0 means off (hotseat and AI practice only).</summary>
        public float DecisionTimerSeconds = 30f;
        public float MinDecisionTimerSeconds = 10f;
        public float MaxDecisionTimerSeconds = 120f;

        /// <summary>No drawing or job ever takes fewer turns than this.</summary>
        public int MinimumWorkTurns = 1;

        /// <summary>
        /// The turn spent on Start drawing counts as the first turn of progress, so a 2-turn drawing needs
        /// one Continue and a 1-turn drawing completes in the tick it is started (DESIGN.md section 5.1).
        /// PROVISIONAL: the same applies to Add to drawing and Dismiss LoF.
        /// </summary>
        public bool StartActionCountsAsProgress = true;

        /// <summary>
        /// Until a player's main LoW is drawn or being drawn, the only thing they can do is start it
        /// (DESIGN.md section 2). On timeout they start a default one (TimeoutMainWardBindPoints).
        /// </summary>
        public bool MainWardFirst = true;

        /// <summary>Once a player has a main LoW, the only further LoW they can start is a Mark's Cross (DESIGN.md section 4.1).</summary>
        public bool OnlyMarksCrossAfterMainWard = true;

        /// <summary>Rushing a 1-turn drawing makes it complete in the same tick it is started.</summary>
        public bool RushedOneTurnDrawingsCompleteImmediately = true;

        /// <summary>PROVISIONAL: a negative modifier can never take a fresh line below this durability.</summary>
        public int MinimumDurabilityAfterModifier = 1;

        /// <summary>PROVISIONAL: repairs cannot take a line above its durability at completion (including its modifier).</summary>
        public bool RepairCappedAtMaxDurability = true;

        public ChalkFatigueConfig ChalkFatigue = new ChalkFatigueConfig();
        public BoardConfig Board = new BoardConfig();
        public GeometryConfig Geometry = new GeometryConfig();

        public WardingConfig Warding = new WardingConfig();
        public ForbiddanceConfig Forbiddance = new ForbiddanceConfig();
        public VigorConfig Vigor = new VigorConfig();
        public MakingConfig Making = new MakingConfig();
        public AddToDrawingConfig AddToDrawing = new AddToDrawingConfig();

        public PaceConfig Rush = new PaceConfig { TimeMultiplier = 0.5, Rounding = RoundingMode.Up };
        public PaceConfig Steady = new PaceConfig { TimeMultiplier = 1.5, Rounding = RoundingMode.Up };

        // Odds are integer weights over a shared denominator so rolls are exact. 24 fits 1/8, 1/4, 1/6, 1/12 and 1/3.
        // Each entry is a separate outcome; whatever weight is left over is "no modifier" (DESIGN.md section 5.2).
        public ModifierOdds NormalOdds = ModifierOdds.Over(24, Outcome(-1, 3), Outcome(+1, 3));
        public ModifierOdds RushOdds = ModifierOdds.Over(24, Outcome(-2, 4), Outcome(-1, 6));
        public ModifierOdds SteadyOdds = ModifierOdds.Over(24, Outcome(-1, 2), Outcome(+1, 8), Outcome(+2, 4));

        public PaceConfig PaceSettings(DrawingPace pace)
        {
            switch (pace)
            {
                case DrawingPace.Rush: return Rush;
                case DrawingPace.Steady: return Steady;
                default: return null;
            }
        }

        public ModifierOdds OddsFor(DrawingPace pace)
        {
            switch (pace)
            {
                case DrawingPace.Rush: return RushOdds;
                case DrawingPace.Steady: return SteadyOdds;
                default: return NormalOdds;
            }
        }

        /// <summary>Returns null when the per-duel settings are in range, otherwise what is wrong.</summary>
        public string Validate()
        {
            if (TurnLimit < MinTurnLimit || TurnLimit > MaxTurnLimit)
                return "Turn limit must be between " + MinTurnLimit + " and " + MaxTurnLimit + ".";
            if (DecisionTimerSeconds != 0f && (DecisionTimerSeconds < MinDecisionTimerSeconds || DecisionTimerSeconds > MaxDecisionTimerSeconds))
                return "Decision timer must be off or between " + MinDecisionTimerSeconds + " and " + MaxDecisionTimerSeconds + " seconds.";
            return null;
        }

        private static ModifierOutcome Outcome(int steps, int weight)
        {
            return new ModifierOutcome { Steps = steps, Weight = weight };
        }
    }

    /// <summary>Board scale placeholders for the geometry layer (DESIGN.md section 1.1).</summary>
    [Serializable]
    public sealed class BoardConfig
    {
        public float Width = 32f;
        public float Height = 20f;
        /// <summary>Distance from a player's own short edge to the centre of their main LoW.</summary>
        public float MainWardInset = 6f;
    }

    /// <summary>
    /// Placeholders the geometry layer needs that DESIGN.md does not give yet. PROVISIONAL: each is listed
    /// under "Rules core defaults" in DESIGN.md section 7.
    /// </summary>
    [Serializable]
    public sealed class GeometryConfig
    {
        /// <summary>A main LoW with 2 bind points is an ellipse with these semi-axes, long axis toward the opponent.</summary>
        public float EllipseSemiMajor = 3.5f;
        public float EllipseSemiMinor = 2.5f;
        /// <summary>Radius of a LoW (other than a Mark's Cross) attached at a bind point of the main LoW.</summary>
        public float AttachedWardRadius = 1.5f;
        /// <summary>Starting length of a LoF that is not bound at two points (tangent or free). The scroll wheel changes it within the range below.</summary>
        public float TangentForbiddanceLength = 3f;
        public float MinForbiddanceLength = 1f;
        public float MaxForbiddanceLength = 8f;
        /// <summary>The scroll wheel changes ellipse flatness and LoF length by this much per notch.</summary>
        public float ScrollStep = 0.25f;
        /// <summary>A main LoW ellipse keeps its semi-major axis; the player sets its semi-minor axis within this range.</summary>
        public float EllipseMinSemiMinor = 1.5f;
        public float EllipseMaxSemiMinor = 3.25f;
        /// <summary>
        /// Travel cost (DESIGN.md section 5.1): each threshold the drawing lies beyond, measured from the
        /// outline of the main LoW, adds one turn to its draw time. Lines on bind points and anything
        /// inside the main LoW cost nothing extra.
        /// </summary>
        public float[] TravelTurnThresholds = { 2f, 4f, 7f };
        /// <summary>While placing the main LoW, the mouse snaps to a preset spot or the midline of the board when this close.</summary>
        public float MainWardSnapDistance = 0.6f;
        /// <summary>Two LoV collide when they come this close.</summary>
        public float VigorContactDistance = 0.3f;
        /// <summary>Free placements (LoF, LoV, unbound chalklings) must be on the player's own half of the board (DESIGN.md section 1.1).</summary>
        public bool DrawOnOwnHalfOnly = true;
        /// <summary>Per-point durability is tracked at this many points around each LoW.</summary>
        public int WardSamples = 120;
    }

    /// <summary>End-game pressure over the last turns of the duel (DESIGN.md section 2.1).</summary>
    [Serializable]
    public sealed class ChalkFatigueConfig
    {
        public int LastTurns = 15;
        /// <summary>Every main LoW loses DurabilityLoss once every this many fatigue turns.</summary>
        public int IntervalTurns = 3;
        public int DurabilityLoss = 1;
        public bool RepairsAllowed = false;
    }

    [Serializable]
    public sealed class WardingConfig
    {
        public int Durability = 12;
        public int DurabilityPerModifierStep = 2;
        /// <summary>Main LoW draw time is (bind points) div this.</summary>
        public int BindPointsPerDrawTurn = 2;
        /// <summary>A Mark's Cross is drawn whole: its small circle and the crossed LoF inside it.</summary>
        public int MarksCrossDrawTurns = 2;
        public int MarksCrossBindPoints = 4;
        public int MaxBindPoints = 12;
        /// <summary>PROVISIONAL: the main LoW a player starts if their decision timer runs out before they have one.</summary>
        public int TimeoutMainWardBindPoints = 4;

        // Geometry-layer placeholders (DESIGN.md section 4.1). Durability is per point, scaled by
        // clamp(sqrt(curvature / ReferenceCurvature), MinCurvatureScale, MaxCurvatureScale).
        public float ReferenceCurvature = 1f / 3f;
        public float MinCurvatureScale = 0.5f;
        public float MaxCurvatureScale = 2f;
        /// <summary>A hit damages the LoW within this distance either side of the impact.</summary>
        public float HitHalfWidth = 0.5f;
        public float DefaultMainRadius = 3f;
        public float MarksCrossRadius = 1f;
    }

    [Serializable]
    public sealed class ForbiddanceConfig
    {
        public int BaseDurability = 3;
        public int DurabilityPerBindPoint = 3;
        public int DurabilityPerModifierStep = 2;
        /// <summary>
        /// Draw time is ceil(length / LengthPerDrawTurn) minus (bind points × DrawTurnsSavedPerBindPoint),
        /// floored at the minimum (DESIGN.md section 4.2). A spec with no length uses Geometry.TangentForbiddanceLength.
        /// </summary>
        public float LengthPerDrawTurn = 2f;
        public int DrawTurnsSavedPerBindPoint = 1;
        public int DismissTurns = 2;
        /// <summary>A LoV hitting a LoF reflects and deals this share of its damage to it, rounded up (DESIGN.md section 4.2).</summary>
        public float VigorDamageMultiplier = 0.5f;
    }

    [Serializable]
    public sealed class VigorTierConfig
    {
        public int DrawTurns;
        public int Speed;
        public int Damage;
    }

    [Serializable]
    public sealed class VigorConfig
    {
        public VigorTierConfig Low = new VigorTierConfig { DrawTurns = 1, Speed = 3, Damage = 1 };
        public VigorTierConfig Medium = new VigorTierConfig { DrawTurns = 2, Speed = 5, Damage = 2 };
        public VigorTierConfig High = new VigorTierConfig { DrawTurns = 3, Speed = 7, Damage = 3 };
        public int DamagePerModifierStep = 1;
        /// <summary>Damage never goes below this. A LoV left at 0 damage fizzles out.</summary>
        public int MinimumDamage = 0;
        /// <summary>A surviving chalkling is pushed PushPerDamage × damage / its max durability units, capped.</summary>
        public float PushPerDamage = 2f;
        public float MaxPush = 3f;

        public VigorTierConfig Tier(VigorTier tier)
        {
            switch (tier)
            {
                case VigorTier.Low: return Low;
                case VigorTier.High: return High;
                default: return Medium;
            }
        }
    }

    [Serializable]
    public sealed class MakingConfig
    {
        public int BaseDurability = 1;
        public int DurabilityPerBuff = 2;
        public int BaseDamage = 0;
        public int DamagePerBuff = 1;
        public int BaseSpeed = 0;
        public int SpeedPerBuff = 1;
        /// <summary>Free durability per bind point the chalkling is bound to.</summary>
        public int DurabilityPerBoundBindPoint = 2;
        // Geometry-layer placeholders (DESIGN.md section 4.4), kept here so all numbers live in one place.
        public float TetherDistance = 2f;
        public float Radius = 0.25f;
        public float AttackRange = 0.5f;
        public int AttacksPerTick = 1;
        /// <summary>"+1 to a stat of choice / -1 to the highest stat", per modifier step.</summary>
        public int StatPerModifierStep = 1;
        /// <summary>Draw time is ceil(stat buffs / StatBuffsPerDrawTurn) plus DrawTurnsPerGlyph for each glyph beyond the free ones.</summary>
        public int StatBuffsPerDrawTurn = 4;
        public int DrawTurnsPerGlyph = 1;
        /// <summary>A chalkling carries this many glyphs for free, plus one more per StatBuffsPerExtraGlyph buffs.</summary>
        public int FreeGlyphs = 1;
        public int StatBuffsPerExtraGlyph = 3;
    }

    [Serializable]
    public sealed class AddToDrawingConfig
    {
        public int TurnsPerStatBuff = 2;
        public int TurnsPerDurabilityPoint = 2;
    }

    public enum RoundingMode
    {
        Up,
        Down,
        Nearest,
    }

    [Serializable]
    public sealed class PaceConfig
    {
        public double TimeMultiplier = 1.0;
        public RoundingMode Rounding = RoundingMode.Up;
    }

    [Serializable]
    public sealed class ModifierOutcome
    {
        /// <summary>Signed modifier strength: -2 is "2x -ve", +1 is "+ve".</summary>
        public int Steps;
        public int Weight;
    }

    [Serializable]
    public sealed class ModifierOdds
    {
        public int Denominator = 24;
        public List<ModifierOutcome> Outcomes = new List<ModifierOutcome>();

        public static ModifierOdds Over(int denominator, params ModifierOutcome[] outcomes)
        {
            return new ModifierOdds { Denominator = denominator, Outcomes = new List<ModifierOutcome>(outcomes) };
        }

        /// <summary>Weight left for "no modifier".</summary>
        public int NoModifierWeight
        {
            get
            {
                int used = 0;
                foreach (var outcome in Outcomes) used += outcome.Weight;
                return Denominator - used;
            }
        }

        public int Roll(IRandomSource random)
        {
            if (Denominator <= 0 || NoModifierWeight < 0)
                throw new InvalidOperationException("Modifier odds weights must be non-negative and sum to at most the denominator.");

            int roll = random.NextInt(Denominator);
            int cumulative = 0;
            foreach (var outcome in Outcomes)
            {
                cumulative += outcome.Weight;
                if (roll < cumulative) return outcome.Steps;
            }
            return 0;
        }
    }
}
