namespace RithmaticDuels.Rules
{
    public enum ActionKind
    {
        StartDrawing,
        ContinueDrawing,
        CancelDrawing,
        AddToDrawing,
        DismissForbiddance,
        /// <summary>Draw the bind points of the main LoW (DESIGN.md section 2). Amount is how many.</summary>
        DrawBindPoints,
        Pass,
    }

    /// <summary>The one action a player locks in for a turn (DESIGN.md section 5.1).</summary>
    public sealed class PlayerAction
    {
        public ActionKind Kind { get; private set; }
        public DrawingSpec Spec { get; private set; }
        public DrawingPace Pace { get; private set; }
        public int TargetLineId { get; private set; }
        /// <summary>Add to drawing: stat buffs to add (LoM) or durability points to repair (LoW / LoF).</summary>
        public int Amount { get; private set; }
        /// <summary>Add to drawing on a LoM: which chalkling stat the buffs go to.</summary>
        public ChalklingStat Stat { get; private set; }
        /// <summary>
        /// Cancel the drawing in progress first, then do this action in the same turn (DESIGN.md section 5.1).
        /// Cancelling on its own is <see cref="Cancel"/>, which is the same as cancelling and passing.
        /// </summary>
        public bool CancelsCurrentWork { get; private set; }

        private PlayerAction(ActionKind kind)
        {
            Kind = kind;
            TargetLineId = -1;
        }

        public static PlayerAction Start(DrawingSpec spec, DrawingPace pace = DrawingPace.Normal)
        {
            return new PlayerAction(ActionKind.StartDrawing) { Spec = spec, Pace = pace };
        }

        public static PlayerAction Continue() { return new PlayerAction(ActionKind.ContinueDrawing); }
        public static PlayerAction Cancel() { return new PlayerAction(ActionKind.CancelDrawing); }
        public static PlayerAction Pass() { return new PlayerAction(ActionKind.Pass); }

        /// <summary>Repair a LoW / LoF by this many durability points.</summary>
        public static PlayerAction Repair(int targetLineId, int durability)
        {
            return new PlayerAction(ActionKind.AddToDrawing) { TargetLineId = targetLineId, Amount = durability };
        }

        /// <summary>Buff a chalkling (LoM) with this many stat buffs on one stat.</summary>
        public static PlayerAction Buff(int chalklingLineId, ChalklingStat stat, int buffs)
        {
            return new PlayerAction(ActionKind.AddToDrawing) { TargetLineId = chalklingLineId, Amount = buffs, Stat = stat };
        }

        public static PlayerAction Dismiss(int forbiddanceLineId)
        {
            return new PlayerAction(ActionKind.DismissForbiddance) { TargetLineId = forbiddanceLineId };
        }

        public static PlayerAction DrawBindPoints(int count)
        {
            return new PlayerAction(ActionKind.DrawBindPoints) { Amount = count };
        }

        /// <summary>This action, done straight after cancelling the drawing in progress.</summary>
        public PlayerAction AfterCancelling()
        {
            var copy = (PlayerAction)MemberwiseClone();
            copy.CancelsCurrentWork = true;
            return copy;
        }
    }
}
