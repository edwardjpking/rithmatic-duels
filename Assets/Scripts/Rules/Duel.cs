using System;
using System.Collections.Generic;
using System.Linq;

namespace RithmaticDuels.Rules
{
    /// <summary>
    /// The rules engine for one duel. Each turn both players lock in one action without seeing the
    /// other's, then <see cref="ResolveTurn"/> runs one tick in the DESIGN.md section 3 order:
    /// completion and dismissal, then movement and attacks, then damage.
    /// </summary>
    public sealed class Duel
    {
        private readonly IRandomSource _random;
        private readonly IMovementResolver _movement;
        private readonly PlayerState[] _players;
        private readonly List<Line> _lines = new List<Line>();
        private int _nextLineId = 1;

        public RulesConfig Config { get; private set; }

        /// <summary>Number of turns resolved so far.</summary>
        public int Turn { get; private set; }

        /// <summary>Turns that count toward <see cref="RulesConfig.TurnLimit"/>.</summary>
        public int CountedTurns { get; private set; }

        public DuelOutcome Outcome { get; private set; }

        /// <summary>True when the next turn to resolve falls in the last turns of the duel (DESIGN.md section 2.1).</summary>
        public bool ChalkFatigueActive { get { return FatigueTurnIndex(NextTurnCounts()) > 0; } }

        public IReadOnlyList<Line> Lines { get { return _lines; } }

        public Duel(RulesConfig config, IRandomSource random, IMovementResolver movement = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (random == null) throw new ArgumentNullException(nameof(random));
            string invalid = config.Validate();
            if (invalid != null) throw new ArgumentException(invalid, nameof(config));

            Config = config;
            _random = random;
            _movement = movement ?? new NoMovementResolver();
            _players = new[] { new PlayerState(Player.A), new PlayerState(Player.B) };
            Outcome = DuelOutcome.InProgress;
        }

        public Duel(RulesConfig config, ulong seed, IMovementResolver movement = null)
            : this(config, new SeededRandom(seed), movement)
        {
        }

        public PlayerState GetPlayer(Player player)
        {
            return _players[(int)player];
        }

        public Line FindLine(int lineId)
        {
            foreach (var line in _lines)
                if (line.Id == lineId) return line;
            return null;
        }

        /// <summary>What anyone (including the opponent) can see of a player's work in progress. Null if none.</summary>
        public VisibleWork ViewWorkOf(Player player)
        {
            var work = GetPlayer(player).Work;
            if (work == null) return null;

            var view = new VisibleWork { Kind = work.Kind, TargetLineId = work.TargetLineId };
            if (work.Kind == WorkKind.Drawing)
            {
                view.LineType = work.Spec.Type;
            }
            else
            {
                var target = FindLine(work.TargetLineId);
                if (target != null) view.LineType = target.Type;
            }
            return view;
        }

        /// <summary>The action menu for this player this turn.</summary>
        public List<ActionKind> AvailableActions(Player player)
        {
            var actions = new List<ActionKind>();
            if (Outcome != DuelOutcome.InProgress) return actions;

            var state = GetPlayer(player);
            if (MustStartMainWard(state))
            {
                actions.Add(ActionKind.StartDrawing);
                return actions;
            }
            if (state.NeedsBindPoints && !state.HasWorkInProgress)
            {
                actions.Add(ActionKind.DrawBindPoints);
                actions.Add(ActionKind.Pass);
                return actions;
            }
            if (state.HasWorkInProgress)
            {
                actions.Add(ActionKind.ContinueDrawing);
                actions.Add(ActionKind.CancelDrawing);
                if (!Config.PassAllowedWhileDrawing) return actions;
            }
            else
            {
                actions.Add(ActionKind.StartDrawing);
                if (HasDismissTarget(player)) actions.Add(ActionKind.DismissForbiddance);
            }
            actions.Add(ActionKind.Pass);
            return actions;
        }

        /// <summary>What a player does when the decision timer runs out: continue a drawing in progress, otherwise pass.</summary>
        public PlayerAction TimeoutAction(Player player)
        {
            var state = GetPlayer(player);
            if (MustStartMainWard(state))
                return PlayerAction.Start(DrawingSpec.Warding(Config.Warding.TimeoutMainWardBindPoints));
            if (state.NeedsBindPoints && !state.HasWorkInProgress)
                return PlayerAction.DrawBindPoints(state.MainWard.Spec.BindPoints, state.MainWard.Spec.Layout);
            return state.HasWorkInProgress ? PlayerAction.Continue() : PlayerAction.Pass();
        }

        /// <summary>Returns null if the action is legal for this player now, otherwise why not.</summary>
        public string Validate(Player player, PlayerAction action)
        {
            if (Outcome != DuelOutcome.InProgress) return "The duel is over.";
            if (action == null) return "No action chosen.";

            var state = GetPlayer(player);
            if (action.CancelsCurrentWork)
            {
                if (!state.HasWorkInProgress) return "There is no drawing in progress to cancel.";
                if (action.Kind == ActionKind.ContinueDrawing || action.Kind == ActionKind.CancelDrawing)
                    return "Cancelling first only goes with starting something new or passing.";
            }
            // What the player is still working on once any cancel in this action has happened.
            bool busy = state.HasWorkInProgress && !action.CancelsCurrentWork;

            if (MustStartMainWard(state, busy))
            {
                if (action.Kind != ActionKind.StartDrawing || action.Spec == null
                    || action.Spec.Type != LineType.Warding || action.Spec.IsMarksCross)
                    return "Start by drawing your main Line of Warding.";
                if (action.Pace != DrawingPace.Normal) return "Your main Line of Warding can't be rushed or steadied.";
            }
            else if (state.NeedsBindPoints && !busy)
            {
                if (action.Kind != ActionKind.DrawBindPoints && action.Kind != ActionKind.Pass)
                    return "Nothing can be drawn until your main Line of Warding has its bind points.";
            }

            switch (action.Kind)
            {
                case ActionKind.StartDrawing:
                    if (busy) return "Only one drawing can be in progress at a time.";
                    return DrawTimes.Validate(action.Spec, Config);

                case ActionKind.ContinueDrawing:
                case ActionKind.CancelDrawing:
                    return state.HasWorkInProgress ? null : "There is no drawing in progress.";

                case ActionKind.DismissForbiddance:
                    if (busy) return "Only one drawing can be in progress at a time.";
                    var target = FindLine(action.TargetLineId);
                    if (target == null || target.Owner != player || !target.IsActive || target.Type != LineType.Forbiddance)
                        return "Only one of your own Lines of Forbiddance can be dismissed.";
                    return null;

                case ActionKind.DrawBindPoints:
                    if (busy || !state.NeedsBindPoints) return "Bind points are drawn on a main Line of Warding that has none yet.";
                    if (action.Amount < 1 || action.Amount > Config.Warding.MaxBindPoints)
                        return "A Line of Warding has 1 to " + Config.Warding.MaxBindPoints + " bind points.";
                    return null;

                case ActionKind.Pass:
                    if (busy && !Config.PassAllowedWhileDrawing) return "While drawing you can only continue or cancel.";
                    return null;

                default:
                    return "Unknown action.";
            }
        }

        /// <summary>True while this player has no main LoW, drawn or in progress, and the rules make them start one first.</summary>
        private bool MustStartMainWard(PlayerState state)
        {
            return MustStartMainWard(state, state.HasWorkInProgress);
        }

        private bool MustStartMainWard(PlayerState state, bool busy)
        {
            return Config.MainWardFirst && state.MainWard == null && !busy;
        }

        /// <summary>Lock in both players' actions and resolve one tick.</summary>
        public TurnReport ResolveTurn(PlayerAction actionA, PlayerAction actionB)
        {
            string reasonA = Validate(Player.A, actionA);
            if (reasonA != null) throw new InvalidOperationException("Player A: " + reasonA);
            string reasonB = Validate(Player.B, actionB);
            if (reasonB != null) throw new InvalidOperationException("Player B: " + reasonB);

            bool countsTowardLimit = NextTurnCounts();
            int fatigueTurn = FatigueTurnIndex(countsTowardLimit);

            Turn++;
            var report = new TurnReport { Turn = Turn };

            // Both choices were validated against the same frozen state, and each only touches its
            // own player's work, so applying them in turn is equivalent to applying them together.
            ApplyChoice(Player.A, actionA, report);
            ApplyChoice(Player.B, actionB, report);

            // Tick resolution (DESIGN.md section 3). Within each stage player A goes first, so random
            // rolls happen in a fixed order.
            // 1. LoF are dismissed.
            CompleteWhere(w => w.Kind == WorkKind.Dismiss, report);
            // 2. Rushed drawings that finish this tick go down, if they would have taken 1 turn at normal pace
            // (longer drawings already get their time cut by rushing).
            CompleteWhere(w => w.Kind == WorkKind.Drawing && w.Pace == DrawingPace.Rush
                && DrawTimes.Turns(w.Spec, DrawingPace.Normal, Config) <= Config.RushedEarlyMaxNormalTurns, report);
            // Optionally (LinesFinishBeforeMovement) everything else finishes before movement too.
            if (Config.LinesFinishBeforeMovement) CompleteWhere(w => true, report);

            // 3. Animated lines take actions (geometry layer), and their hits do damage.
            // Earliest breach time within the tick for each player. The earlier breach loses;
            // only breaches at exactly the same moment are a draw (DESIGN.md section 1).
            var movement = new MovementResult();
            _movement.ResolveMovementAndAttacks(this, movement);
            var breachTime = new[] { NoBreach, NoBreach };
            ApplyMovementResult(movement, report, breachTime);
            DestroyUnboundLines(report, breachTime);

            // 4. Every other line that finishes this tick goes down, and the geometry layer places it.
            CompleteWhere(w => true, report);
            var placed = new MovementResult();
            _movement.PlaceNewLines(this, placed);
            ApplyMovementResult(placed, report, breachTime);
            // A line that finished bound to a bind point broken earlier this tick falls straight away (DESIGN.md section 4.1).
            DestroyUnboundLines(report, breachTime);

            if (fatigueTurn > 0 && fatigueTurn % Config.ChalkFatigue.IntervalTurns == 0)
            {
                ApplyChalkFatigue(report, breachTime);
                DestroyUnboundLines(report, breachTime);
            }

            if (breachTime[0] != NoBreach || breachTime[1] != NoBreach)
            {
                if (breachTime[0] == breachTime[1]) Outcome = DuelOutcome.Draw;
                else Outcome = breachTime[0] < breachTime[1] ? DuelOutcome.PlayerBWins : DuelOutcome.PlayerAWins;
            }
            else
            {
                if (countsTowardLimit) CountedTurns++;
                if (CountedTurns >= Config.TurnLimit) Outcome = Tiebreak();
            }

            if (Outcome != DuelOutcome.InProgress)
                report.Events.Add(new TurnEvent(TurnEventType.DuelEnded, Player.A, -1, (int)Outcome));

            report.Outcome = Outcome;
            return report;
        }

        private void ApplyChoice(Player player, PlayerAction action, TurnReport report)
        {
            var state = GetPlayer(player);
            int startProgress = Config.StartActionCountsAsProgress ? 1 : 0;

            if (action.CancelsCurrentWork)
            {
                report.Events.Add(new TurnEvent(TurnEventType.WorkCancelled, player, state.Work.TargetLineId, state.Work.Progress));
                state.Work = null;
            }

            switch (action.Kind)
            {
                case ActionKind.StartDrawing when Config.MainWardFirst && state.MainWard == null:
                    // Setup: the outline goes down at once with no modifier, and its bind points start.
                    var outline = new Line(_nextLineId++, player, action.Spec, 0, Config) { BindPointsDrawn = 0 };
                    outline.MakeMainWard(Config);
                    _lines.Add(outline);
                    state.MainWard = outline;
                    report.Events.Add(new TurnEvent(TurnEventType.DrawingCompleted, player, outline.Id, 0));
                    StartBindPoints(state, action.Spec.BindPoints, startProgress, report);
                    break;

                case ActionKind.DrawBindPoints:
                    // The redrawn points follow the layout picked now (a saved one, or the standard spots).
                    state.MainWard.Spec.Layout = action.Layout != null && action.Layout.BindPointCount == action.Amount ? action.Layout.Clone() : null;
                    state.MainWard.Spec.BindPoints = action.Amount;
                    StartBindPoints(state, action.Amount, startProgress, report);
                    break;

                case ActionKind.StartDrawing:
                    state.Work = WorkInProgress.NewDrawing(action.Spec, action.Pace, DrawTimes.Turns(action.Spec, action.Pace, Config));
                    bool instant = action.Pace == DrawingPace.Rush && Config.RushedOneTurnDrawingsCompleteImmediately
                        && DrawTimes.BaseTurns(action.Spec, Config) == 1 && action.Spec.TravelTurns == 0;
                    state.Work.Progress = instant ? state.Work.TotalTurns : startProgress;
                    report.Events.Add(new TurnEvent(TurnEventType.WorkStarted, player, -1, state.Work.TotalTurns));
                    break;

                case ActionKind.ContinueDrawing:
                    state.Work.Progress++;
                    report.Events.Add(new TurnEvent(TurnEventType.WorkProgressed, player, state.Work.TargetLineId, state.Work.Progress));
                    break;

                case ActionKind.CancelDrawing:
                    report.Events.Add(new TurnEvent(TurnEventType.WorkCancelled, player, state.Work.TargetLineId, state.Work.Progress));
                    state.Work = null;
                    break;

                case ActionKind.DismissForbiddance:
                    int dismissTurns = Math.Max(Config.MinimumWorkTurns, Config.Forbiddance.DismissTurns);
                    state.Work = WorkInProgress.NewJob(WorkKind.Dismiss, action.TargetLineId, 0, dismissTurns);
                    state.Work.Progress = startProgress;
                    report.Events.Add(new TurnEvent(TurnEventType.WorkStarted, player, action.TargetLineId, dismissTurns));
                    break;
            }
        }

        private void CompleteWhere(Func<WorkInProgress, bool> stage, TurnReport report)
        {
            foreach (var state in _players)
            {
                if (Outcome != DuelOutcome.InProgress) return;
                if (state.Work != null && state.Work.IsComplete && stage(state.Work)) Complete(state, report);
            }
        }

        private void StartBindPoints(PlayerState state, int count, int startProgress, TurnReport report)
        {
            int turns = DrawTimes.BindPointTurns(count, Config);
            state.Work = WorkInProgress.NewJob(WorkKind.BindPoints, state.MainWard.Id, count, turns);
            state.Work.Progress = startProgress;
            report.Events.Add(new TurnEvent(TurnEventType.WorkStarted, state.Player, state.MainWard.Id, turns));
        }

        private void Complete(PlayerState state, TurnReport report)
        {
            var work = state.Work;
            state.Work = null;
            var player = state.Player;

            if (work.Kind == WorkKind.Drawing)
            {
                int steps = Config.OddsFor(work.Pace).Roll(_random);
                var line = new Line(_nextLineId++, player, work.Spec, steps, Config);
                _lines.Add(line);
                report.Events.Add(new TurnEvent(TurnEventType.DrawingCompleted, player, line.Id, steps));
                if (line.Type == LineType.Vigor && line.Damage <= 0)
                {
                    // A 0-damage LoV fizzles out before it reaches anything (DESIGN.md section 4.3).
                    line.Status = LineStatus.Destroyed;
                    report.Events.Add(new TurnEvent(TurnEventType.VigorFizzled, player, line.Id));
                }
                if (line.Spec.IsMarksCross)
                {
                    // Its crossed LoF are drawn with it, one joining each pair of opposite bind points. They share its
                    // modifier roll (PROVISIONAL), and work like any other LoF (DESIGN.md section 4.1).
                    int braces = line.Spec.BindPoints / 2;
                    for (int i = 0; i < braces; i++)
                    {
                        var brace = new Line(_nextLineId++, player, DrawingSpec.Forbiddance(2), steps, Config) { BraceOf = line.Id, BraceIndex = i };
                        _lines.Add(brace);
                    }
                }
                if (line.Type == LineType.Warding && !line.Spec.IsMarksCross && state.MainWard == null)
                {
                    // PROVISIONAL: a player's first completed LoW (not a Mark's Cross) becomes their main LoW.
                    line.MakeMainWard(Config);
                    state.MainWard = line;
                }
                return;
            }

            var target = FindLine(work.TargetLineId);
            if (target == null || !target.IsActive)
            {
                report.Events.Add(new TurnEvent(TurnEventType.WorkFizzled, player, work.TargetLineId));
                return;
            }

            switch (work.Kind)
            {
                case WorkKind.Dismiss:
                    target.Status = LineStatus.Dismissed;
                    report.Events.Add(new TurnEvent(TurnEventType.DismissCompleted, player, target.Id));
                    break;
                case WorkKind.BindPoints:
                    target.BindPointsDrawn = work.Amount;
                    report.Events.Add(new TurnEvent(TurnEventType.BindPointsCompleted, player, target.Id, work.Amount));
                    break;
            }
        }

        private const float NoBreach = float.PositiveInfinity;
        private const float EndOfTick = 1f;

        private void ApplyMovementResult(MovementResult movement, TurnReport report, float[] breachTime)
        {
            foreach (var setting in movement.DurabilityOverrides)
            {
                var line = FindLine(setting.LineId);
                if (line != null && line.IsActive && line.HasDurability) line.SetDurability(setting.Durability, setting.MaxDurability);
            }

            foreach (int lineId in movement.DestroyedLineIds)
            {
                var line = FindLine(lineId);
                if (line == null || !line.IsActive) continue;
                line.Status = LineStatus.Destroyed;
                report.Events.Add(new TurnEvent(TurnEventType.LineDestroyed, line.Owner, line.Id));
                if (line.IsMainWard) MarkBreached(line, report, breachTime, EndOfTick);
            }

            // Stable sort, so hits at the same moment keep the order the geometry layer reported them in.
            foreach (var hit in movement.Damage.OrderBy(h => h.TimeOfImpact))
            {
                var line = FindLine(hit.TargetLineId);
                if (line == null || !line.IsActive || !line.HasDurability || hit.Amount <= 0) continue;

                // A LoV's reduced damage to a LoF it reflects off is worked out by the geometry layer (DESIGN.md section 4.2).
                line.Durability = Math.Max(0, line.Durability - hit.Amount);
                report.Events.Add(new TurnEvent(TurnEventType.LineDamaged, line.Owner, line.Id, hit.Amount));

                if (line.Durability == 0)
                {
                    // "Any line left with no durability loses effect."
                    line.Status = LineStatus.Broken;
                    report.Events.Add(new TurnEvent(TurnEventType.LineBroken, line.Owner, line.Id));
                    if (line.IsMainWard) MarkBreached(line, report, breachTime, hit.TimeOfImpact);
                }
            }
        }

        /// <summary>
        /// Once lines have broken, everything bound to them goes too, and what was bound to that, and so on
        /// (DESIGN.md section 4.1). Each is logged after the break that took it.
        /// </summary>
        private void DestroyUnboundLines(TurnReport report, float[] breachTime)
        {
            var lost = new List<int>();
            _movement.FindUnboundLines(this, lost);
            foreach (int lineId in lost)
            {
                var line = FindLine(lineId);
                if (line == null || !line.IsActive) continue;
                line.Status = LineStatus.Destroyed;
                report.Events.Add(new TurnEvent(TurnEventType.LineDestroyed, line.Owner, line.Id, TurnEvent.BindPointGone));
                if (line.IsMainWard) MarkBreached(line, report, breachTime, EndOfTick);
            }
        }

        /// <summary>Fatigue lands at the end of the tick, after every collision.</summary>
        private void ApplyChalkFatigue(TurnReport report, float[] breachTime)
        {
            foreach (var state in _players)
            {
                var ward = state.MainWard;
                if (ward == null || !ward.IsActive) continue;
                ward.Durability = Math.Max(0, ward.Durability - Config.ChalkFatigue.DurabilityLoss);
                report.Events.Add(new TurnEvent(TurnEventType.ChalkFatigue, ward.Owner, ward.Id, Config.ChalkFatigue.DurabilityLoss));
                if (ward.Durability == 0)
                {
                    ward.Status = LineStatus.Broken;
                    report.Events.Add(new TurnEvent(TurnEventType.LineBroken, ward.Owner, ward.Id));
                    MarkBreached(ward, report, breachTime, EndOfTick);
                }
            }
        }

        /// <summary>At the turn limit the higher share of main LoW durability left wins; an exact tie is a draw.</summary>
        private DuelOutcome Tiebreak()
        {
            var a = _players[0].MainWard;
            var b = _players[1].MainWard;
            if (a == null || b == null)
            {
                if (a == b) return DuelOutcome.Draw;
                return a != null ? DuelOutcome.PlayerAWins : DuelOutcome.PlayerBWins;
            }

            // Compare a.Durability / a.Max with b.Durability / b.Max without floating point.
            long shareA = (long)a.Durability * b.MaxDurability;
            long shareB = (long)b.Durability * a.MaxDurability;
            if (shareA == shareB) return DuelOutcome.Draw;
            return shareA > shareB ? DuelOutcome.PlayerAWins : DuelOutcome.PlayerBWins;
        }

        private bool NextTurnCounts()
        {
            return !Config.TurnLimitStartsAfterBothMainWards
                || (_players[0].MainWard != null && _players[1].MainWard != null);
        }

        /// <summary>1-based position of the next turn within the chalk fatigue window, or 0 if it is outside it.</summary>
        private int FatigueTurnIndex(bool nextTurnCounts)
        {
            if (!nextTurnCounts) return 0;
            int index = CountedTurns + 1 - (Config.TurnLimit - Config.ChalkFatigue.LastTurns);
            return index > 0 ? index : 0;
        }

        private static void MarkBreached(Line line, TurnReport report, float[] breachTime, float time)
        {
            int owner = (int)line.Owner;
            breachTime[owner] = Math.Min(breachTime[owner], time);
            report.Events.Add(new TurnEvent(TurnEventType.MainWardBreached, line.Owner, line.Id));
        }

        private bool HasDismissTarget(Player player)
        {
            foreach (var line in _lines)
                if (line.Owner == player && line.IsActive && line.Type == LineType.Forbiddance) return true;
            return false;
        }
    }
}
