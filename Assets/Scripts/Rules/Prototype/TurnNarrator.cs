using System.Collections.Generic;

namespace RithmaticDuels.Rules.Prototype
{
    /// <summary>
    /// Turns a <see cref="TurnReport"/> into plain sentences for the hot-seat duel log. Both players
    /// read the log, so it only says what the opponent could see (DESIGN.md section 2, Information):
    /// that work was started or continued and on which line type, never its spec, pace or progress.
    /// </summary>
    public static class TurnNarrator
    {
        public static List<string> Describe(Duel duel, TurnReport report, IReadOnlyList<string> movementNotes = null)
        {
            var lines = new List<string>();
            bool notesAdded = movementNotes == null;

            foreach (var e in report.Events)
            {
                // Movement happens between completion and damage (DESIGN.md section 3).
                if (!notesAdded && IsDamagePhase(e.Type))
                {
                    lines.AddRange(movementNotes);
                    notesAdded = true;
                }
                string text = Describe(duel, report, e);
                if (text != null) lines.Add(text);
            }
            if (!notesAdded) lines.AddRange(movementNotes);
            return lines;
        }

        public static string PlayerName(Player player)
        {
            return "Player " + player;
        }

        public static string LineTypeName(LineType type)
        {
            switch (type)
            {
                case LineType.Warding: return "Line of Warding";
                case LineType.Forbiddance: return "Line of Forbiddance";
                case LineType.Vigor: return "Line of Vigor";
                default: return "Line of Making";
            }
        }

        /// <summary>For example "A's Line of Vigor #5".</summary>
        public static string LineName(Line line)
        {
            if (line == null) return "a line that is gone";
            string kind = line.IsMainWard ? "main Line of Warding"
                : line.Spec.IsMarksCross ? "Mark's Cross"
                : line.Type == LineType.Making ? "chalkling"
                : LineTypeName(line.Type);
            return line.Owner + "'s " + kind + " #" + line.Id;
        }

        public static string ModifierName(int steps)
        {
            switch (steps)
            {
                case 2: return "double positive modifier";
                case 1: return "positive modifier";
                case -1: return "negative modifier";
                case -2: return "double negative modifier";
                default: return "no modifier";
            }
        }

        public static string OutcomeName(DuelOutcome outcome)
        {
            switch (outcome)
            {
                case DuelOutcome.PlayerAWins: return "Player A wins!";
                case DuelOutcome.PlayerBWins: return "Player B wins!";
                case DuelOutcome.Draw: return "The duel is a draw.";
                default: return "The duel goes on.";
            }
        }

        private static bool IsDamagePhase(TurnEventType type)
        {
            switch (type)
            {
                case TurnEventType.LineDamaged:
                case TurnEventType.LineBroken:
                case TurnEventType.LineDestroyed:
                case TurnEventType.ChalkFatigue:
                case TurnEventType.MainWardBreached:
                case TurnEventType.DuelEnded:
                    return true;
                default:
                    return false;
            }
        }

        private static string Describe(Duel duel, TurnReport report, TurnEvent e)
        {
            string who = PlayerName(e.Player);
            var line = e.LineId >= 0 ? duel.FindLine(e.LineId) : null;

            switch (e.Type)
            {
                case TurnEventType.WorkStarted:
                    return who + " started " + VisibleWorkName(duel, e) + ".";
                case TurnEventType.WorkProgressed:
                    return who + " kept working.";
                case TurnEventType.WorkCancelled:
                    return who + " cancelled their work and lost its progress.";
                case TurnEventType.DrawingCompleted:
                    return who + " completed " + LineName(line) + " with " + ModifierName(e.Value) + ".";
                case TurnEventType.RepairCompleted:
                    return who + " repaired " + LineName(line) + " by " + e.Value + ".";
                case TurnEventType.BuffCompleted:
                    return who + " buffed " + LineName(line) + " by " + e.Value + ".";
                case TurnEventType.DismissCompleted:
                    return who + " dismissed " + LineName(line) + ".";
                case TurnEventType.WorkFizzled:
                    return who + "'s work came to nothing: its target was gone.";
                case TurnEventType.LineDamaged:
                    return LineName(line) + " took " + e.Value + " damage.";
                case TurnEventType.ChalkFatigue:
                    return "Chalk fatigue: " + LineName(line) + " lost " + e.Value + " durability.";
                case TurnEventType.LineBroken:
                    return LineName(line) + " broke.";
                case TurnEventType.LineDestroyed:
                    // A LoV that hit or was reflected is already covered by the movement notes.
                    return line != null && line.Type == LineType.Vigor ? null : LineName(line) + " was destroyed.";
                case TurnEventType.VigorFizzled:
                    return LineName(line) + " fizzled out with 0 damage.";
                case TurnEventType.MainWardBreached:
                    return who + "'s main Line of Warding was breached!";
                case TurnEventType.DuelEnded:
                    return OutcomeName(report.Outcome);
                default:
                    return e.ToString();
            }
        }

        private static string VisibleWorkName(Duel duel, TurnEvent e)
        {
            var view = duel.ViewWorkOf(e.Player);
            if (view == null)
            {
                // Already finished in the same tick; the completion line says what it was.
                return e.LineId >= 0 ? "work on " + LineName(duel.FindLine(e.LineId)) : "a drawing";
            }

            switch (view.Kind)
            {
                case WorkKind.Repair: return "repairing " + LineName(duel.FindLine(view.TargetLineId));
                case WorkKind.Buff: return "buffing " + LineName(duel.FindLine(view.TargetLineId));
                case WorkKind.Dismiss: return "dismissing " + LineName(duel.FindLine(view.TargetLineId));
                default: return "drawing a " + LineTypeName(view.LineType);
            }
        }
    }
}
