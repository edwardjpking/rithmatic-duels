using RithmaticDuels.Rules.Geometry;

namespace RithmaticDuels.Rules.Presets
{
    /// <summary>
    /// Whether a defence can be saved (DESIGN.md section 6.8): its structure is sound and every part fits when
    /// built in order on an empty board. The editor uses this so an invalid defence can never be saved.
    /// </summary>
    /// <summary>A defence built on a scratch board, for previews (see <see cref="DefenceCheck.Trial"/>).</summary>
    public sealed class DefenceTrial
    {
        public ChalkBoard Board;
        public Duel Duel;
        /// <summary>Turns taken to build it, with no modifiers.</summary>
        public int Turns;
        /// <summary>False if it stopped before every part was built.</summary>
        public bool Complete;
    }

    public static class DefenceCheck
    {
        /// <summary>Null if the defence can be saved, otherwise why not.</summary>
        public static string Problem(DefencePreset preset, RulesConfig config)
        {
            string structure = preset.StructureProblem(config);
            if (structure != null) return structure;
            int failedIndex;
            return BuildProblem(preset, config, out failedIndex);
        }

        /// <summary>
        /// Builds the defence for player A on an empty board, with no modifiers and no opponent. Null if every
        /// part fits; otherwise why not, and the index of the part that didn't (-1 for the main LoW).
        /// </summary>
        public static string BuildProblem(DefencePreset preset, RulesConfig config, out int failedIndex)
        {
            failedIndex = -2;
            var board = new ChalkBoard(config);
            var duel = new Duel(config, new FixedRandom(), board);
            var run = new DefenceRun(preset, Player.A);
            int guard = 0;
            while (run.IsRunning && guard++ < 1000)
            {
                Placement placement;
                var action = run.ContinueAction(duel, board, out placement);
                if (action == null) break;
                if (placement != null) board.SetPendingPlacement(Player.A, action.Spec, placement);
                string problem = duel.Validate(Player.A, action);
                if (problem != null) { failedIndex = run.CurrentIndex; return problem; }
                var report = duel.ResolveTurn(action, StandIn(duel, Player.B));
                run.Observe(duel, report);
            }
            if (run.Skipped.Count > 0)
            {
                failedIndex = run.Preset.IndexOf(run.Skipped[0]);
                return "Part " + (failedIndex + 1) + " doesn't fit.";
            }
            if (run.State == DefenceRunState.Finished) return null;
            failedIndex = run.CurrentIndex;
            return run.BlockedReason ?? "Part of the defence couldn't be built.";
        }

        /// <summary>
        /// Builds the defence for <paramref name="player"/> on an empty board, as <see cref="BuildProblem"/> does, and
        /// keeps the result: the scratch board shows the whole template where it would go, and <see cref="DefenceTrial.Turns"/>
        /// is how many turns it takes with no modifiers (DESIGN.md section 6.8).
        /// </summary>
        public static DefenceTrial Trial(DefencePreset preset, RulesConfig config, Player player)
        {
            var board = new ChalkBoard(config);
            var duel = new Duel(config, new FixedRandom(), board);
            var run = new DefenceRun(preset, player);
            var other = player == Player.A ? Player.B : Player.A;
            int turns = 0;
            while (run.IsRunning && turns < 1000)
            {
                Placement placement;
                var action = run.ContinueAction(duel, board, out placement);
                if (action == null || duel.Validate(player, action) != null) break;
                if (placement != null) board.SetPendingPlacement(player, action.Spec, placement);
                var otherAction = StandIn(duel, other);
                var report = player == Player.A ? duel.ResolveTurn(action, otherAction) : duel.ResolveTurn(otherAction, action);
                turns++;
                run.Observe(duel, report);
            }
            return new DefenceTrial { Board = board, Duel = duel, Turns = turns, Complete = run.State == DefenceRunState.Finished };
        }

        /// <summary>
        /// What a stand-in opponent does while a defence is checked or tried out: it starts a plain main LoW (the
        /// rules require one first) and otherwise passes.
        /// </summary>
        public static PlayerAction StandIn(Duel duel, Player player)
        {
            var state = duel.GetPlayer(player);
            if (state.HasWorkInProgress) return PlayerAction.Continue();
            if (state.MainWard == null) return PlayerAction.Start(DrawingSpec.Warding(duel.Config.Warding.TimeoutMainWardBindPoints));
            return PlayerAction.Pass();
        }

        /// <summary>Rolls that always give no modifier, so the check is the same every time.</summary>
        private sealed class FixedRandom : IRandomSource
        {
            // The top of the odds range is "no modifier" (see ModifierOdds).
            public int NextInt(int maxExclusive) { return maxExclusive - 1; }
        }
    }
}
