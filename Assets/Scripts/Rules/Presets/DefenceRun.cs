using System.Collections.Generic;
using RithmaticDuels.Rules.Geometry;

namespace RithmaticDuels.Rules.Presets
{
    public enum DefenceRunState
    {
        /// <summary>Still building: the next action is Continue (or Cancel).</summary>
        Running,
        /// <summary>Every part is built.</summary>
        Finished,
        /// <summary>The player cancelled it. It can't be restarted this duel.</summary>
        Cancelled,
        /// <summary>The main LoW couldn't be drawn; the preset ends. (Other parts that don't fit are skipped.)</summary>
        Blocked,
    }

    /// <summary>
    /// A saved defence being built in a duel (DESIGN.md section 6.8). While it runs, the player only chooses
    /// Continue or Cancel: <see cref="ContinueAction"/> starts the next part (its placement already chosen) or
    /// continues the one in progress. Each part costs its normal drawing time. Call <see cref="Observe"/> with
    /// every turn report so it knows when a part is finished.
    /// </summary>
    public sealed class DefenceRun
    {
        private readonly Dictionary<int, int> _lineIds = new Dictionary<int, int>();

        public DefencePreset Preset { get; private set; }
        public Player Player { get; private set; }
        public DefenceRunState State { get; private set; }
        /// <summary>The part being built or next to build: -1 is the main LoW, then indices into the preset's parts.</summary>
        public int CurrentIndex { get; private set; }
        /// <summary>Why the preset stopped, when it is <see cref="DefenceRunState.Blocked"/>.</summary>
        public string BlockedReason { get; private set; }

        public bool IsRunning { get { return State == DefenceRunState.Running; } }
        public int PartCount { get { return Preset.Components.Count + 1; } }
        /// <summary>Parts finished so far, counting the main LoW.</summary>
        public int PartsDone { get { return CurrentIndex + 1; } }

        public DefenceRun(DefencePreset preset, Player player)
        {
            Preset = preset.Clone();
            Player = player;
            CurrentIndex = -1;
        }

        /// <summary>
        /// Presets are offered on turn 1 to a player who hasn't started anything, and again whenever the main LoW and
        /// its bind points are finished and nothing is in progress (Amelia, v0.32; DESIGN.md section 6.8). Only a
        /// preset that <see cref="Matches"/> the main LoW can then be picked.
        /// </summary>
        public static bool CanOffer(Duel duel, Player player)
        {
            var state = duel.GetPlayer(player);
            if (state.HasWorkInProgress) return false;
            if (state.MainWard == null) return duel.Turn == 0;
            return !state.NeedsBindPoints && state.MainWard.IsActive;
        }

        /// <summary>
        /// Null if the preset can be built on this player's board now, otherwise why not. With the main LoW already
        /// drawn, the preset's bind point layout must give exactly the bind points it has (same shape, same spots).
        /// </summary>
        public static string MatchProblem(DefencePreset preset, Duel duel, ChalkBoard board, Player player)
        {
            var main = board.MainWardOf(player);
            if (main == null) return null;
            if (preset.Components.Count == 0) return "This defence has nothing to add to your main Line of Warding.";
            bool ellipse = !main.Shape.IsCircle;
            if (preset.Layout.IsEllipse != ellipse)
                return "This defence needs " + (preset.Layout.IsEllipse ? "an ellipse" : "a circle") + " for its main Line of Warding.";
            var expected = board.PreviewBindPoints(player, preset.MainWardSpec());
            var actual = main.BindPoints;
            bool same = expected.Count == actual.Count;
            for (int i = 0; same && i < actual.Count; i++) same = Vec2.Distance(expected[i], actual[i]) < 0.02f;
            return same ? null : "This defence needs the " + preset.Layout.Name + " bind points on your main Line of Warding.";
        }

        /// <summary>
        /// Starts a preset for this player. If their main LoW is already drawn (it must match, see
        /// <see cref="MatchProblem"/>), it stands in for the preset's main LoW and building starts at the first part.
        /// Parts that clash with lines already on the board are skipped when their turn comes, as usual.
        /// </summary>
        public static DefenceRun Begin(DefencePreset preset, Player player, ChalkBoard board)
        {
            var run = new DefenceRun(preset, player);
            var main = board.MainWardOf(player);
            if (main != null)
            {
                run._lineIds[DefencePreset.MainWardId] = main.Line.Id;
                run.CurrentIndex = 0;
                if (run.Preset.Components.Count == 0) run.State = DefenceRunState.Finished;
            }
            return run;
        }

        /// <summary>What the decision timer does when it runs out mid-defence: presses Continue (Amelia, v0.32).</summary>
        public PlayerAction TimeoutAction(Duel duel, ChalkBoard board, out Placement placement)
        {
            return ContinueAction(duel, board, out placement) ?? duel.TimeoutAction(Player);
        }

        /// <summary>The line built for a part (0 for the main LoW), or -1 if it isn't built.</summary>
        public int LineOf(int componentId)
        {
            int id;
            return _lineIds.TryGetValue(componentId, out id) ? id : -1;
        }

        /// <summary>
        /// The current part's spec and placement, measured on the board, for the preview and for starting it.
        /// False if it can't be built now (then <paramref name="problem"/> says why).
        /// </summary>
        public bool CurrentDrawing(Duel duel, ChalkBoard board, out DrawingSpec spec, out Placement placement, out DrawingPace pace, out string problem)
        {
            // A part that no longer fits (say the LoW it binds to was destroyed) is skipped, and the next part that
            // can be built is loaded instead (Amelia, 2026-10-31). Only a main LoW that can't be drawn stops the run.
            while (true)
            {
                bool ok = TryCurrent(duel, board, out spec, out placement, out pace, out problem);
                if (ok || !IsRunning || CurrentIndex < 0) return ok;
                if (duel.GetPlayer(Player).HasWorkInProgress) return false;
                Skipped.Add(Preset.Components[CurrentIndex].Id);
                CurrentIndex++;
                if (CurrentIndex >= Preset.Components.Count) { State = DefenceRunState.Finished; return false; }
            }
        }

        /// <summary>Parts skipped because they no longer fitted when their turn came.</summary>
        public readonly List<int> Skipped = new List<int>();

        private bool TryCurrent(Duel duel, ChalkBoard board, out DrawingSpec spec, out Placement placement, out DrawingPace pace, out string problem)
        {
            spec = null; placement = null; pace = DrawingPace.Normal; problem = null;
            if (!IsRunning) { problem = "The defence isn't being built."; return false; }
            if (CurrentIndex < 0)
            {
                spec = Preset.MainWardSpec();
                placement = Placement.MainWard(board.Home(Player), Preset.MainSemiMinor);
            }
            else
            {
                var part = Preset.Components[CurrentIndex];
                pace = part.Pace;
                placement = ToPlacement(part, duel, board, out problem);
                if (placement == null) return false;
                spec = board.Measure(Player, part.Spec(duel.Config), placement);
            }
            problem = board.ValidatePlacement(Player, spec, placement);
            return problem == null;
        }

        /// <summary>
        /// The action for Continue: carry on with the part in progress, or start the next one. Null if the preset
        /// isn't running or the next part no longer fits (the run is then <see cref="DefenceRunState.Blocked"/>).
        /// The placement to hand to <see cref="ChalkBoard.SetPendingPlacement"/> comes out in <paramref name="placement"/>.
        /// </summary>
        public PlayerAction ContinueAction(Duel duel, ChalkBoard board, out Placement placement)
        {
            placement = null;
            if (!IsRunning) return null;
            var state = duel.GetPlayer(Player);
            if (state.HasWorkInProgress) return PlayerAction.Continue();

            DrawingSpec spec; DrawingPace pace; string problem;
            if (!CurrentDrawing(duel, board, out spec, out placement, out pace, out problem))
            {
                placement = null;
                if (!IsRunning) return null;
                State = DefenceRunState.Blocked;
                BlockedReason = problem;
                placement = null;
                return null;
            }
            return PlayerAction.Start(spec, pace);
        }

        /// <summary>Cancel the preset: scraps the part in progress (if any) and ends the run for good.</summary>
        public PlayerAction Cancel(Duel duel)
        {
            if (!IsRunning) return null;
            State = DefenceRunState.Cancelled;
            return duel.GetPlayer(Player).HasWorkInProgress ? PlayerAction.Cancel() : PlayerAction.Pass();
        }

        /// <summary>
        /// Follow what happened in a turn: when the part in progress finishes, the next one is loaded. While the run
        /// is going, the player draws nothing else, so any drawing they finish is the current part.
        /// </summary>
        public void Observe(Duel duel, TurnReport report)
        {
            if (!IsRunning) return;
            foreach (var e in report.Events)
            {
                if (e.Player != Player) continue;
                if (e.Type == TurnEventType.WorkCancelled) { State = DefenceRunState.Cancelled; return; }
                if (e.Type == TurnEventType.DrawingCompleted)
                {
                    int componentId = CurrentIndex < 0 ? DefencePreset.MainWardId : Preset.Components[CurrentIndex].Id;
                    _lineIds[componentId] = e.LineId;
                }
            }
            // A part is done once it is on the board and nothing is left in progress (the main LoW also needs its bind points).
            int currentId = CurrentIndex < 0 ? DefencePreset.MainWardId : Preset.Components[CurrentIndex].Id;
            if (_lineIds.ContainsKey(currentId) && !duel.GetPlayer(Player).HasWorkInProgress)
            {
                CurrentIndex++;
                if (CurrentIndex >= Preset.Components.Count) State = DefenceRunState.Finished;
            }
        }

        private Placement ToPlacement(DefenceComponent part, Duel duel, ChalkBoard board, out string problem)
        {
            problem = null;
            int a = -1, b = -1;
            if (part.UsesA && (a = GlobalBindPoint(part.A, duel, board)) < 0) { problem = "The line this is bound to is gone."; return null; }
            if (part.UsesB && (b = GlobalBindPoint(part.B, duel, board)) < 0) { problem = "The line this is bound to is gone."; return null; }
            switch (part.Kind)
            {
                case PlacementKind.AtBindPoint:
                    return part.Type == LineType.Making
                        ? new Placement { Kind = PlacementKind.AtBindPoint, BindPointA = a }
                        : Placement.AtBindPoint(a, part.Size, part.Scale, part.Join);
                case PlacementKind.BetweenBindPoints: return Placement.BetweenBindPoints(a, b);
                case PlacementKind.TangentAtBindPoint: return Placement.TangentAt(a, part.Length);
                case PlacementKind.FreeSegment: return Placement.Segment(ToBoard(part.PointA, duel, board), ToBoard(part.PointB, duel, board));
                case PlacementKind.FreePoint: return Placement.At(ToBoard(part.PointA, duel, board));
                default: problem = "This part can't be placed."; return null;
            }
        }

        /// <summary>A point in the preset's frame (origin at the main LoW's centre, +X toward the opponent) on the board.</summary>
        public Vec2 ToBoard(Vec2 local, Duel duel, ChalkBoard board)
        {
            var main = board.MainWardOf(Player);
            Vec2 centre = main != null ? main.Shape.Centre : board.Home(Player);
            return centre + local.Rotated(board.Facing(Player).Angle);
        }

        private int GlobalBindPoint(BindRef point, Duel duel, ChalkBoard board)
        {
            int lineId = LineOf(point.Component);
            if (lineId < 0) return -1;
            var ward = board.BodyOf(lineId) as WardBody;
            if (ward == null || !ward.Line.IsActive || point.Index < 0 || point.Index >= ward.BindPoints.Count) return -1;
            Vec2 spot = ward.BindPoints[point.Index];
            var all = board.BindPointsOf(Player);
            for (int i = 0; i < all.Count; i++)
                if (Vec2.Distance(all[i], spot) < 0.02f) return i;
            return -1;
        }
    }
}
