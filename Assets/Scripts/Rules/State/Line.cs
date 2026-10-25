using System;

namespace RithmaticDuels.Rules
{
    public enum LineStatus
    {
        /// <summary>On the ground and in effect.</summary>
        Active,
        /// <summary>Durability reached zero, so the line has lost its effect.</summary>
        Broken,
        /// <summary>A LoF whose dismissal completed.</summary>
        Dismissed,
        /// <summary>Removed by the movement layer, e.g. a LoV that hit something or a destroyed chalkling.</summary>
        Destroyed,
    }

    /// <summary>A completed drawing on the ground.</summary>
    public sealed class Line
    {
        public int Id { get; private set; }
        public Player Owner { get; private set; }
        public LineType Type { get; private set; }
        public DrawingSpec Spec { get; private set; }
        public bool IsMainWard { get; internal set; }
        public LineStatus Status { get; internal set; }

        /// <summary>Signed result of the completion roll: -2, -1, 0, +1 or +2.</summary>
        public int ModifierSteps { get; private set; }

        /// <summary>LoW, LoF and LoM only. LoV has no durability.</summary>
        public bool HasDurability { get { return Type != LineType.Vigor; } }
        public int MaxDurability { get; private set; }
        public int Durability { get; internal set; }

        /// <summary>LoV only. Can drop after hitting another LoV.</summary>
        public VigorTier Tier { get; private set; }

        /// <summary>LoV and LoM.</summary>
        public int Damage { get; private set; }
        public int Speed { get; private set; }

        /// <summary>LoM only: stat buffs spent on each stat, including any added later with Add to drawing.</summary>
        public int DurabilityBuffs { get; private set; }
        public int DamageBuffs { get; private set; }
        public int SpeedBuffs { get; private set; }
        public Glyph Glyphs { get; private set; }
        public int TotalStatBuffs { get { return DurabilityBuffs + DamageBuffs + SpeedBuffs; } }

        public bool IsActive { get { return Status == LineStatus.Active; } }

        /// <summary>
        /// LoW only: how many bind points have been drawn on it. A main LoW from the setup starts at 0 and
        /// gets its bind points when that work completes; any other LoW has them from the start.
        /// </summary>
        public int BindPointsDrawn { get; internal set; }

        internal Line(int id, Player owner, DrawingSpec spec, int modifierSteps, RulesConfig config)
        {
            Id = id;
            Owner = owner;
            Type = spec.Type;
            Spec = spec.Clone();
            ModifierSteps = modifierSteps;
            Status = LineStatus.Active;

            switch (spec.Type)
            {
                case LineType.Warding:
                    MaxDurability = Modified(config.Warding.Durability, config.Warding.DurabilityPerModifierStep, config);
                    BindPointsDrawn = spec.BindPoints;
                    break;
                case LineType.Forbiddance:
                    int baseForbiddance = config.Forbiddance.BaseDurability + spec.BindPoints * config.Forbiddance.DurabilityPerBindPoint;
                    MaxDurability = Modified(baseForbiddance, config.Forbiddance.DurabilityPerModifierStep, config);
                    break;
                case LineType.Vigor:
                    SetVigorTier(spec.Tier, config);
                    break;
                case LineType.Making:
                    var making = config.Making;
                    DurabilityBuffs = spec.DurabilityBuffs;
                    DamageBuffs = spec.DamageBuffs;
                    SpeedBuffs = spec.SpeedBuffs;
                    Glyphs = spec.Glyphs;
                    MaxDurability = making.BaseDurability + spec.DurabilityBuffs * making.DurabilityPerBuff
                        + spec.BindPoints * making.DurabilityPerBoundBindPoint;
                    Damage = making.BaseDamage + spec.DamageBuffs * making.DamagePerBuff;
                    Speed = making.BaseSpeed + spec.SpeedBuffs * making.SpeedPerBuff;
                    ApplyChalklingModifier(spec.PositiveModifierStat, config);
                    break;
            }
            Durability = MaxDurability;
        }

        /// <summary>Takes on the speed and base damage of a tier; the modifier stays with the line.</summary>
        internal void SetVigorTier(VigorTier tier, RulesConfig config)
        {
            var settings = config.Vigor.Tier(tier);
            Tier = tier;
            Speed = settings.Speed;
            Damage = Math.Max(config.Vigor.MinimumDamage, settings.Damage + ModifierSteps * config.Vigor.DamagePerModifierStep);
        }

        /// <summary>"+1 to a stat of choice / -1 to the highest stat", once per modifier step.</summary>
        private void ApplyChalklingModifier(ChalklingStat chosen, RulesConfig config)
        {
            int amount = config.Making.StatPerModifierStep;
            for (int i = 0; i < Math.Abs(ModifierSteps); i++)
            {
                if (ModifierSteps > 0) AdjustStat(chosen, amount);
                else AdjustStat(HighestStat(), -amount);
            }
            MaxDurability = Math.Max(config.MinimumDurabilityAfterModifier, MaxDurability);
            Damage = Math.Max(0, Damage);
            Speed = Math.Max(0, Speed);
        }

        /// <summary>PROVISIONAL tie order when stats are equal: durability, then damage, then speed.</summary>
        private ChalklingStat HighestStat()
        {
            if (MaxDurability >= Damage && MaxDurability >= Speed) return ChalklingStat.Durability;
            return Damage >= Speed ? ChalklingStat.Damage : ChalklingStat.Speed;
        }

        private void AdjustStat(ChalklingStat stat, int amount)
        {
            switch (stat)
            {
                case ChalklingStat.Durability: MaxDurability += amount; break;
                case ChalklingStat.Damage: Damage += amount; break;
                case ChalklingStat.Speed: Speed += amount; break;
            }
        }

        /// <summary>Add to drawing on a chalkling: one stat buff per point, as if drawn in at the start.</summary>
        internal void AddStatBuffs(ChalklingStat stat, int buffs, RulesConfig config)
        {
            switch (stat)
            {
                case ChalklingStat.Durability:
                    DurabilityBuffs += buffs;
                    MaxDurability += buffs * config.Making.DurabilityPerBuff;
                    Durability += buffs * config.Making.DurabilityPerBuff;
                    break;
                case ChalklingStat.Damage:
                    DamageBuffs += buffs;
                    Damage += buffs * config.Making.DamagePerBuff;
                    break;
                case ChalklingStat.Speed:
                    SpeedBuffs += buffs;
                    Speed += buffs * config.Making.SpeedPerBuff;
                    break;
            }
        }

        /// <summary>Set by the geometry layer, which tracks durability per point (see <see cref="DurabilityOverride"/>).</summary>
        internal void SetDurability(int durability, int maxDurability)
        {
            MaxDurability = Math.Max(1, maxDurability);
            Durability = Math.Max(0, Math.Min(durability, MaxDurability));
        }

        private int Modified(int baseValue, int perStep, RulesConfig config)
        {
            return Math.Max(config.MinimumDurabilityAfterModifier, baseValue + ModifierSteps * perStep);
        }
    }
}
