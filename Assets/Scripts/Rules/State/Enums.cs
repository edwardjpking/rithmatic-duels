using System;

namespace RithmaticDuels.Rules
{
    public enum Player
    {
        A = 0,
        B = 1,
    }

    public enum LineType
    {
        Warding,
        Forbiddance,
        Vigor,
        Making,
    }

    public enum DrawingPace
    {
        Normal,
        Rush,
        Steady,
    }

    /// <summary>LoV frequency tier: higher is faster, slower to draw and hits harder (DESIGN.md section 4.3).</summary>
    public enum VigorTier
    {
        Low,
        Medium,
        High,
    }

    public enum ChalklingStat
    {
        Durability,
        Damage,
        Speed,
    }

    /// <summary>The glyphs a chalkling can carry (DESIGN.md section 4.4). A chalkling may carry both.</summary>
    [Flags]
    public enum Glyph
    {
        None = 0,
        Attack = 1,
        Defend = 2,
    }

    public enum DuelOutcome
    {
        InProgress,
        PlayerAWins,
        PlayerBWins,
        Draw,
    }

    public static class PlayerExtensions
    {
        public static Player Opponent(this Player player)
        {
            return player == Player.A ? Player.B : Player.A;
        }
    }
}
