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
            if (state.HasWorkInProgress)
            {
                actions.Add(ActionKind.ContinueDrawing);
                actions.Add(ActionKind.CancelDrawing);
            }
            else
            {
                actions.Add(ActionKind.StartDrawing);
                if (HasAddTarget(player)) actions.Add(ActionKind.AddToDrawing);
                if (HasDismissTarget(player)) actions.Add(ActionKind.DismissForbiddance);
            }
            actions.Add(ActionKind.Pass);
            return actions;
        }

        /// <summary>What a player does when the decision timer runs out: continue a drawing in progress, otherwise pass.</summary>
        public PlayerAction TimeoutAction(Player player)
        {
            return GetPlayer(player).HasWorkInProgress ? PlayerAction.Continue() : PlayerAction.Pass();
        }

        /// <summary>Returns null if the action is legal for this player now, otherwise why not.</summary>
        public string Validate(Player player, PlayerAction action)
        {
            if (Outcome != DuelOutcome.InProgress) return "The duel is over.";
            if (action == null) return "No action chosen.";

            var state = GetPlayer(player);
            switch (action.Kind)
            {
                case ActionKind.StartDrawing:
                    if (state.HasWorkInProgress) return "Only one drawing can be in progress at a time.";
                    return DrawTimes.Validate(action.Spec, Config);

                case ActionKind.ContinueDrawing:
                case ActionKind.CancelDrawing:
                    return state.HasWorkInProgress ? null : "There is no drawing in progress.";

                case ActionKind.AddToDrawing:
                    if (state.HasWorkInProgress) return "Only one drawing can be in progress at a time.";
                    return ValidateAddTo(player, action);

                case ActionKind.DismissForbiddance:
                    if (state.HasWorkInProgress) return "Only one drawing can be in progress at a time.";
                    var target = FindLine(action.TargetLineId);
                    if (target == null || target.Owner != player || !target.IsActive || target.Type != LineType.Forbiddance)
                        return "Only one of your own Lines of Forbiddance can be dismissed.";
                    return null;

                case ActionKind.Pass:
                    return null;

                default:
                    return "Unknown action.";
            }
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

            // 1. Completion and dismissal. Player A first, so random rolls happen in a fixed order.
            foreach (var state in _players)
            {
                if (state.Work != null && state.Work.IsComplete) Complete(state, report, fatigueTurn > 0);
            }

            // 2. Movement and attacks (geometry layer).
            var movement = new MovementResult();
            _movement.ResolveMovementAndAttacks(this, movement);

            // 3. Damage.
            // Earliest breach time within the tick for each player. The earlier breach loses;
            // only breaches at exactly the same moment are a draw (DESIGN.md section 1).
            var breachTime = new[] { NoBreach, NoBreach };
            ApplyMovementResult(movement, report, breachTime);
            if (fatigueTurn > 0 && fatigueTurn % Config.ChalkFatigue.IntervalTurns == 0)
                ApplyChalkFatigue(report, breachTime);

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

            switch (action.Kind)
            {
                case ActionKind.StartDrawing:
                    state.Work = WorkInProgress.NewDrawing(action.Spec, action.Pace, DrawTimes.Turns(action.Spec, action.Pace, Config));
                    bool instant = action.Pace == DrawingPace.Rush && Config.RushedOneTurnDrawingsCompleteImmediately
                        && DrawTimes.BaseTurns(action.Spec, Config) == 1;
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

                case ActionKind.AddToDrawing:
                    var target = FindLine(action.TargetLineId);
                    bool isBuff = target.Type == LineType.Making;
                    int perUnit = isBuff ? Config.AddToDrawing.TurnsPerStatBuff : Config.AddToDrawing.TurnsPerDurabilityPoint;
                    int turns = Math.Max(Config.MinimumWorkTurns, action.Amount * perUnit);
                    state.Work = WorkInProgress.NewJob(isBuff ? WorkKind.Buff : WorkKind.Repair, target.Id, action.Amount, turns, action.Stat);
                    state.Work.Progress = startProgress;
                    report.Events.Add(new TurnEvent(TurnEventType.WorkStarted, player, target.Id, turns));
                    break;

                case ActionKind.DismissForbiddance:
                    int dismissTurns = Math.Max(Config.MinimumWorkTurns, Config.Forbiddance.DismissTurns);
                    state.Work = WorkInProgress.NewJob(WorkKind.Dismiss, action.TargetLineId, 0, dismissTurns);
                    state.Work.Progress = startProgress;
                    report.Events.Add(new TurnEvent(TurnEventType.WorkStarted, player, action.TargetLineId, dismissTurns));
                    break;
            }
        }

        private void Complete(PlayerState state, TurnReport report, bool fatigued)
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
                if (line.Type == LineType.Warding && !line.Spec.IsMarksCross && state.MainWard == null)
                {
                    // PROVISIONAL: a player's first completed LoW (not a Mark's Cross) becomes their main LoW.
                    line.IsMainWard = true;
                    state.MainWard = line;
                }
                return;
            }

            var target = FindLine(work.TargetLineId);
            bool repairBlocked = work.Kind == WorkKind.Repair && fatigued && !Config.ChalkFatigue.RepairsAllowed;
            if (target == null || !target.IsActive || repairBlocked)
            {
                report.Events.Add(new TurnEvent(TurnEventType.WorkFizzled, player, work.TargetLineId));
                return;
            }

            switch (work.Kind)
            {
                case WorkKind.Repair:
                    int repaired = work.Amount;
                    if (Config.RepairCappedAtMaxDurability)
                        repaired = Math.Min(repaired, target.MaxDurability - target.Durability);
                    target.Durability += repaired;
                    report.Events.Add(new TurnEvent(TurnEventType.RepairCompleted, player, target.Id, repaired));
                    break;
                case WorkKind.Buff:
                    target.AddStatBuffs(work.Stat, work.Amount, Config);
                    report.Events.Add(new TurnEvent(TurnEventType.BuffCompleted, player, target.Id, work.Amount));
                    break;
                case WorkKind.Dismiss:
                    target.Status = LineStatus.Dismissed;
                    report.Events.Add(new TurnEvent(TurnEventType.DismissCompleted, player, target.Id));
                    break;
            }
        }

        private const float NoBreach = float.PositiveInfinity;
        private const float EndOfTick = 1f;

        private void ApplyMovementResult(MovementResult movement, TurnReport report, float[] breachTime)
        {
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

                // LoF reflect LoV and take no damage from them; only chalklings can damage a LoF.
                var source = hit.SourceLineId >= 0 ? FindLine(hit.SourceLineId) : null;
                if (line.Type == LineType.Forbiddance && source != null && source.Type == LineType.Vigor) continue;

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

        private string ValidateAddTo(Player player, PlayerAction action)
        {
            var target = FindLine(action.TargetLineId);
            if (target == null || target.Owner != player || !target.IsActive)
                return "You can only add to one of your own lines that is still in effect.";
            if (action.Amount <= 0) return "Add at least one point.";

            switch (target.Type)
            {
                case LineType.Making:
                    return null;
                case LineType.Warding:
                case LineType.Forbiddance:
                    if (ChalkFatigueActive && !Config.ChalkFatigue.RepairsAllowed)
                        return "Repairs are not allowed during chalk fatigue.";
                    if (Config.RepairCappedAtMaxDurability && action.Amount > target.MaxDurability - target.Durability)
                        return "That would repair the line past its full durability.";
                    return null;
                default:
                    return "Lines of Vigor cannot be added to.";
            }
        }

        private bool HasAddTarget(Player player)
        {
            foreach (var line in _lines)
            {
                if (line.Owner != player || !line.IsActive) continue;
                if (line.Type == LineType.Making) return true;
                if (line.Type == LineType.Warding || line.Type == LineType.Forbiddance)
                {
                    if (ChalkFatigueActive && !Config.ChalkFatigue.RepairsAllowed) continue;
                    if (!Config.RepairCappedAtMaxDurability || line.Durability < line.MaxDurability) return true;
                }
            }
            return false;
        }

        private bool HasDismissTarget(Player player)
        {
            foreach (var line in _lines)
                if (line.Owner == player && line.IsActive && line.Type == LineType.Forbiddance) return true;
            return false;
        }
    }
}
