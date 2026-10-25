using System.Collections.Generic;

namespace RithmaticDuels.Rules
{
    public enum TurnEventType
    {
        WorkStarted,
        WorkProgressed,
        WorkCancelled,
        DrawingCompleted,
        BuffCompleted,
        DismissCompleted,
        /// <summary>A main LoW's bind points are drawn. Value is how many.</summary>
        BindPointsCompleted,
        /// <summary>A buff or dismissal finished but its target was gone.</summary>
        WorkFizzled,
        LineDamaged,
        /// <summary>Chalk fatigue wore down a main LoW (DESIGN.md section 2.1).</summary>
        ChalkFatigue,
        LineBroken,
        LineDestroyed,
        /// <summary>A LoV completed with 0 damage and was removed at once.</summary>
        VigorFizzled,
        MainWardBreached,
        DuelEnded,
    }

    public sealed class TurnEvent
    {
        public TurnEventType Type { get; private set; }
        public Player Player { get; private set; }
        public int LineId { get; private set; }
        /// <summary>Damage dealt, modifier steps rolled, buffs added and so on, depending on Type.</summary>
        public int Value { get; private set; }

        public TurnEvent(TurnEventType type, Player player, int lineId = -1, int value = 0)
        {
            Type = type;
            Player = player;
            LineId = lineId;
            Value = value;
        }

        public override string ToString()
        {
            return Type + " " + Player + " line=" + LineId + " value=" + Value;
        }
    }

    public sealed class TurnReport
    {
        public int Turn { get; internal set; }
        public List<TurnEvent> Events { get; private set; }
        public DuelOutcome Outcome { get; internal set; }

        internal TurnReport()
        {
            Events = new List<TurnEvent>();
        }
    }
}
