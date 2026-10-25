namespace RithmaticDuels.Rules
{
    public enum WorkKind
    {
        Drawing,
        Repair,
        Buff,
        Dismiss,
        /// <summary>Drawing the bind points of a main LoW whose outline is already down (DESIGN.md section 2).</summary>
        BindPoints,
    }

    /// <summary>
    /// The single piece of work a player has under way.
    /// PROVISIONAL: Add to drawing (repair/buff) and Dismiss LoF occupy the same one-at-a-time slot as a
    /// new drawing, and advance through Continue drawing like one.
    /// </summary>
    public sealed class WorkInProgress
    {
        public WorkKind Kind { get; private set; }
        public DrawingSpec Spec { get; private set; }
        public DrawingPace Pace { get; private set; }
        public int TargetLineId { get; private set; }
        public int Amount { get; private set; }
        public ChalklingStat Stat { get; private set; }
        public int TotalTurns { get; private set; }
        public int Progress { get; internal set; }

        public int TurnsRemaining { get { return TotalTurns - Progress; } }
        public bool IsComplete { get { return Progress >= TotalTurns; } }

        internal static WorkInProgress NewDrawing(DrawingSpec spec, DrawingPace pace, int totalTurns)
        {
            return new WorkInProgress { Kind = WorkKind.Drawing, Spec = spec.Clone(), Pace = pace, TargetLineId = -1, TotalTurns = totalTurns };
        }

        internal static WorkInProgress NewJob(WorkKind kind, int targetLineId, int amount, int totalTurns, ChalklingStat stat = ChalklingStat.Durability)
        {
            return new WorkInProgress { Kind = kind, TargetLineId = targetLineId, Amount = amount, TotalTurns = totalTurns, Stat = stat };
        }
    }

    /// <summary>
    /// What the opponent can see of a player's work in progress: what kind of line it is, but not its
    /// specification, pace or how far along it is.
    /// </summary>
    public sealed class VisibleWork
    {
        public WorkKind Kind { get; internal set; }
        public LineType LineType { get; internal set; }
        /// <summary>For repair, buff and dismiss: the line being worked on, which is already on the ground.</summary>
        public int TargetLineId { get; internal set; }
    }

    public sealed class PlayerState
    {
        public Player Player { get; private set; }
        public WorkInProgress Work { get; internal set; }
        public Line MainWard { get; internal set; }

        public bool HasWorkInProgress { get { return Work != null; } }

        /// <summary>The main LoW's outline is down but its bind points aren't drawn yet, so nothing else can be drawn.</summary>
        public bool NeedsBindPoints { get { return MainWard != null && MainWard.IsActive && MainWard.BindPointsDrawn == 0; } }

        internal PlayerState(Player player)
        {
            Player = player;
        }
    }
}
