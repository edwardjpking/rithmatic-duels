using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace RithmaticDuels.Rules.Tests
{
    public class DuelTests
    {
        private RulesConfig _config;
        private ScriptedMovement _movement;

        [SetUp]
        public void SetUp()
        {
            // Most tests here exercise one rule at a time, so they don't have to draw main LoW first.
            // The main-LoW-first rule has its own tests below.
            _config = new RulesConfig { MainWardFirst = false };
            _config.Warding.MainWardDurabilityMultiplier = 1f; // most tests use plain 12-durability LoW; the doubling has its own test
            _movement = new ScriptedMovement();
        }

        private Duel NewDuel(params int[] rolls)
        {
            return new Duel(_config, new ScriptedRandom(Rolls.NoModifier, rolls), _movement);
        }

        private static readonly PlayerAction Pass = PlayerAction.Pass();
        private static readonly PlayerAction Continue = PlayerAction.Continue();

        /// <summary>Both players draw a main LoW with the same number of bind points.</summary>
        private static void DrawMainWards(Duel duel, int bindPoints = 2)
        {
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(bindPoints)), PlayerAction.Start(DrawingSpec.Warding(bindPoints)));
            while (duel.GetPlayer(Player.A).HasWorkInProgress)
                duel.ResolveTurn(Continue, Continue);
        }

        /// <summary>Start a drawing for player A and continue it until it completes. Returns the new line.</summary>
        private static Line DrawForA(Duel duel, DrawingSpec spec)
        {
            duel.ResolveTurn(PlayerAction.Start(spec), Pass);
            while (duel.GetPlayer(Player.A).HasWorkInProgress)
                duel.ResolveTurn(Continue, Pass);
            return duel.Lines.Last(l => l.BraceOf < 0);
        }

        private static Line DrawForB(Duel duel, DrawingSpec spec)
        {
            duel.ResolveTurn(Pass, PlayerAction.Start(spec));
            while (duel.GetPlayer(Player.B).HasWorkInProgress)
                duel.ResolveTurn(Pass, Continue);
            return duel.Lines.Last(l => l.BraceOf < 0);
        }

        private static void FinishWorkForA(Duel duel)
        {
            while (duel.GetPlayer(Player.A).HasWorkInProgress)
                duel.ResolveTurn(Continue, Pass);
        }

        // ---- Action menu and the one-drawing rule ----

        [Test]
        public void OpeningMenuIsStartOrPass()
        {
            var duel = NewDuel();
            CollectionAssert.AreEqual(new[] { ActionKind.StartDrawing, ActionKind.Pass }, duel.AvailableActions(Player.A));
        }

        [Test]
        public void WithMainWardFirstTheOpeningMenuIsOnlyStart()
        {
            _config.MainWardFirst = true;
            var duel = NewDuel();
            CollectionAssert.AreEqual(new[] { ActionKind.StartDrawing }, duel.AvailableActions(Player.A));
            Assert.IsNotNull(duel.Validate(Player.A, Pass));
            Assert.IsNotNull(duel.Validate(Player.A, PlayerAction.Start(DrawingSpec.Vigor(VigorTier.Low))));
            Assert.IsNotNull(duel.Validate(Player.A, PlayerAction.Start(DrawingSpec.MarksCross(_config))));
            Assert.IsNull(duel.Validate(Player.A, PlayerAction.Start(DrawingSpec.Warding(6))));
        }

        [Test]
        public void SetupDrawsTheOutlineAtOnceWithNoModifier()
        {
            _config.MainWardFirst = true;
            var duel = NewDuel(0, 0); // roll 0 would be a negative modifier if anything rolled
            var report = duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6)), PlayerAction.Start(DrawingSpec.Warding(2)));

            var a = duel.GetPlayer(Player.A).MainWard;
            Assert.IsNotNull(a);
            Assert.AreEqual(0, a.ModifierSteps);
            Assert.AreEqual(12, a.Durability);
            Assert.AreEqual(0, a.BindPointsDrawn, "6 bind points take 3 turns: this one and 2 more");
            Assert.AreEqual(WorkKind.BindPoints, duel.GetPlayer(Player.A).Work.Kind);
            Assert.AreEqual(2, duel.GetPlayer(Player.B).MainWard.BindPointsDrawn, "2 bind points take 1 turn");
            Assert.IsTrue(report.Events.Any(e => e.Type == TurnEventType.BindPointsCompleted && e.Player == Player.B));
            Assert.AreEqual(1, duel.CountedTurns, "every turn counts, the setup turn included");
        }

        [Test]
        public void NothingCanBeDrawnUntilTheBindPointsAreDone()
        {
            _config.MainWardFirst = true;
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6)), PlayerAction.Start(DrawingSpec.Warding(2)));
            CollectionAssert.AreEqual(
                new[] { ActionKind.ContinueDrawing, ActionKind.CancelDrawing },
                duel.AvailableActions(Player.A));
            Assert.IsNotNull(duel.Validate(Player.A, PlayerAction.Start(DrawingSpec.Vigor(VigorTier.Low)).AfterCancelling()));
            CollectionAssert.AreEqual(new[] { ActionKind.StartDrawing, ActionKind.Pass }, duel.AvailableActions(Player.B));

            duel.ResolveTurn(Continue, Pass);
            duel.ResolveTurn(Continue, Pass);
            Assert.AreEqual(6, duel.GetPlayer(Player.A).MainWard.BindPointsDrawn);
            Assert.Contains(ActionKind.StartDrawing, duel.AvailableActions(Player.A));
        }

        [Test]
        public void CancellingBindPointsKeepsTheOutline()
        {
            _config.MainWardFirst = true;
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6)), PlayerAction.Start(DrawingSpec.Warding(2)));
            var outline = duel.GetPlayer(Player.A).MainWard;

            duel.ResolveTurn(PlayerAction.Cancel(), Pass);
            Assert.AreSame(outline, duel.GetPlayer(Player.A).MainWard);
            Assert.IsFalse(duel.GetPlayer(Player.A).HasWorkInProgress);
            CollectionAssert.AreEqual(new[] { ActionKind.DrawBindPoints, ActionKind.Pass }, duel.AvailableActions(Player.A));
            Assert.IsNotNull(duel.Validate(Player.A, PlayerAction.Start(DrawingSpec.Vigor(VigorTier.Low))));

            // Restarting straight after a cancel, in the same turn, with a different count.
            duel.ResolveTurn(PlayerAction.DrawBindPoints(4), Pass);
            duel.ResolveTurn(PlayerAction.DrawBindPoints(4).AfterCancelling(), Pass);
            Assert.AreEqual(1, duel.GetPlayer(Player.A).Work.Progress, "the restart counts its first turn");
            duel.ResolveTurn(Continue, Pass);
            Assert.AreEqual(4, outline.BindPointsDrawn);
        }

        [Test]
        public void TimeoutRestartsBindPointsWhenNoneAreUnderWay()
        {
            _config.MainWardFirst = true;
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6)), PlayerAction.Start(DrawingSpec.Warding(2)));
            duel.ResolveTurn(PlayerAction.Cancel(), Pass);
            var timeout = duel.TimeoutAction(Player.A);
            Assert.AreEqual(ActionKind.DrawBindPoints, timeout.Kind);
            Assert.AreEqual(6, timeout.Amount);
        }

        [Test]
        public void WithMainWardFirstTimeoutStartsADefaultMainWard()
        {
            _config.MainWardFirst = true;
            var duel = NewDuel();
            var action = duel.TimeoutAction(Player.A);
            Assert.AreEqual(ActionKind.StartDrawing, action.Kind);
            Assert.AreEqual(LineType.Warding, action.Spec.Type);
            Assert.AreEqual(_config.Warding.TimeoutMainWardBindPoints, action.Spec.BindPoints);
        }

        [Test]
        public void TravelTurnsAddToTheDrawTimeAfterPace()
        {
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6).WithTravel(2), DrawingPace.Rush), Pass);
            Assert.AreEqual(2 + 2, duel.GetPlayer(Player.A).Work.TotalTurns);
            Assert.IsNotNull(duel.Validate(Player.B, PlayerAction.Start(DrawingSpec.Warding(6).WithTravel(-1))));
        }

        [Test]
        public void MenuWhileDrawingIsContinueOrCancel()
        {
            // No Pass mid-drawing (decided 2026-10-25): cancelling first (Cancel is cancel-and-pass) brings it back.
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6)), Pass);
            CollectionAssert.AreEqual(
                new[] { ActionKind.ContinueDrawing, ActionKind.CancelDrawing },
                duel.AvailableActions(Player.A));
            Assert.IsNotNull(duel.Validate(Player.A, Pass));
            Assert.IsNull(duel.Validate(Player.A, PlayerAction.Cancel()));
            CollectionAssert.AreEqual(new[] { ActionKind.StartDrawing, ActionKind.Pass }, duel.AvailableActions(Player.B));
        }

        [Test]
        public void OnlyOneDrawingAtATime()
        {
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6)), Pass);
            var second = PlayerAction.Start(DrawingSpec.Forbiddance(1));
            Assert.IsNotNull(duel.Validate(Player.A, second));
            Assert.Throws<InvalidOperationException>(() => duel.ResolveTurn(second, Pass));
        }

        [Test]
        public void ContinueWithNothingInProgressIsIllegal()
        {
            var duel = NewDuel();
            Assert.IsNotNull(duel.Validate(Player.A, Continue));
        }

        // ---- Draw times, rush and steady ----

        [Test]
        public void OneTurnDrawingCompletesTheTickItStarts()
        {
            var duel = NewDuel();
            var report = duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(2)), Pass);
            Assert.IsTrue(report.Events.Any(e => e.Type == TurnEventType.DrawingCompleted && e.Player == Player.A));
            Assert.AreEqual(1, duel.Lines.Count);
        }

        [Test]
        public void TwoTurnDrawingNeedsOneContinue()
        {
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Forbiddance(0)), Pass); // 3 units long: 2 turns
            Assert.AreEqual(0, duel.Lines.Count);
            Assert.AreEqual(1, duel.GetPlayer(Player.A).Work.TurnsRemaining);

            duel.ResolveTurn(Continue, Pass);
            Assert.AreEqual(1, duel.Lines.Count);
        }

        [Test]
        public void MultiTurnDrawingNeedsAContinuePerTurn()
        {
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6)), Pass); // 3 turns
            duel.ResolveTurn(Continue, Pass);
            Assert.AreEqual(0, duel.Lines.Count);
            duel.ResolveTurn(Continue, Pass);
            Assert.AreEqual(1, duel.Lines.Count);
            Assert.IsFalse(duel.GetPlayer(Player.A).HasWorkInProgress);
        }

        [Test]
        public void RushedOneTurnDrawingCompletesTheTickItStarts()
        {
            var duel = NewDuel();
            var report = duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(2), DrawingPace.Rush), Pass);
            Assert.IsTrue(report.Events.Any(e => e.Type == TurnEventType.DrawingCompleted));
            Assert.IsNotNull(duel.GetPlayer(Player.A).MainWard);
        }

        [Test]
        public void RushingATwoTurnDrawingCompletesTheTickItStarts()
        {
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Forbiddance(2), DrawingPace.Rush), Pass);
            Assert.AreEqual(1, duel.Lines.Count);
        }

        [Test]
        public void StartingCanBeConfiguredNotToCountAsProgress()
        {
            _config.StartActionCountsAsProgress = false;
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(2)), Pass);
            Assert.AreEqual(0, duel.Lines.Count);
            duel.ResolveTurn(Continue, Pass);
            Assert.AreEqual(1, duel.Lines.Count);
        }

        [Test]
        public void RushAndSteadyChangeTheDrawingTime()
        {
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6), DrawingPace.Rush), PlayerAction.Start(DrawingSpec.Warding(6), DrawingPace.Steady));
            Assert.AreEqual(2, duel.GetPlayer(Player.A).Work.TotalTurns);
            Assert.AreEqual(5, duel.GetPlayer(Player.B).Work.TotalTurns);
        }

        [Test]
        public void PassingKeepsProgressWhenAllowed()
        {
            _config.PassAllowedWhileDrawing = true;
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6)), Pass);
            duel.ResolveTurn(Continue, Pass);
            duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(2, duel.GetPlayer(Player.A).Work.Progress);
        }

        [Test]
        public void CancelLosesAllProgress()
        {
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6)), Pass);
            duel.ResolveTurn(Continue, Pass);
            duel.ResolveTurn(PlayerAction.Cancel(), Pass);
            Assert.IsFalse(duel.GetPlayer(Player.A).HasWorkInProgress);
            Assert.AreEqual(0, duel.Lines.Count);
            Assert.Contains(ActionKind.StartDrawing, duel.AvailableActions(Player.A));
        }

        [Test]
        public void CancellingThenStartingHappensInOneTurn()
        {
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6)), Pass);
            var report = duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Vigor(VigorTier.Low)).AfterCancelling(), Pass);

            Assert.IsTrue(report.Events.Any(e => e.Type == TurnEventType.WorkCancelled));
            Assert.IsTrue(report.Events.Any(e => e.Type == TurnEventType.DrawingCompleted), "the new 1-turn drawing completes the same turn");
            Assert.AreEqual(LineType.Vigor, duel.Lines.Single().Type);
        }

        [Test]
        public void CancellingFirstNeedsSomethingToCancelAndANewAction()
        {
            var duel = NewDuel();
            Assert.IsNotNull(duel.Validate(Player.A, Pass.AfterCancelling()));
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6)), Pass);
            Assert.IsNull(duel.Validate(Player.A, Pass.AfterCancelling()));
            Assert.IsNotNull(duel.Validate(Player.A, Continue.AfterCancelling()));
        }

        [Test]
        public void CompletionRollUsesThePaceOdds()
        {
            // Roll value 7 is no modifier under normal odds, -1 under rush ([4,10)) and +1 under steady ([2,10)).
            var duel = NewDuel(7, 7);
            duel.ResolveTurn(
                PlayerAction.Start(DrawingSpec.Warding(2), DrawingPace.Rush),
                PlayerAction.Start(DrawingSpec.Warding(2), DrawingPace.Steady));
            duel.ResolveTurn(Pass, Continue);

            Assert.AreEqual(-1, duel.GetPlayer(Player.A).MainWard.ModifierSteps);
            Assert.AreEqual(10, duel.GetPlayer(Player.A).MainWard.MaxDurability);
            Assert.AreEqual(1, duel.GetPlayer(Player.B).MainWard.ModifierSteps);
            Assert.AreEqual(14, duel.GetPlayer(Player.B).MainWard.MaxDurability);
        }

        [Test]
        public void FirstWardIsMainAndMarksCrossIsNot()
        {
            var duel = NewDuel();
            DrawForA(duel, DrawingSpec.MarksCross(_config));
            Assert.IsNull(duel.GetPlayer(Player.A).MainWard);
            var first = DrawForA(duel, DrawingSpec.Warding(2));
            DrawForA(duel, DrawingSpec.Warding(2));

            Assert.AreSame(first, duel.GetPlayer(Player.A).MainWard);
            Assert.AreEqual(1, duel.Lines.Count(l => l.IsMainWard));
        }

        [Test]
        public void AnyLineOfWardingCanBeAddedAfterTheMainWard()
        {
            var duel = NewDuel();
            DrawMainWards(duel);
            Assert.IsNull(duel.Validate(Player.A, PlayerAction.Start(DrawingSpec.Warding(4))));
            Assert.IsNull(duel.Validate(Player.A, PlayerAction.Start(DrawingSpec.Warding(2))));
            Assert.IsNull(duel.Validate(Player.A, PlayerAction.Start(DrawingSpec.MarksCross(_config))));
        }

        [Test]
        public void TheMainWardCannotBeRushedOrSteadied()
        {
            _config.MainWardFirst = true;
            var duel = NewDuel();
            Assert.IsNotNull(duel.Validate(Player.A, PlayerAction.Start(DrawingSpec.Warding(6), DrawingPace.Rush)));
            Assert.IsNotNull(duel.Validate(Player.A, PlayerAction.Start(DrawingSpec.Warding(6), DrawingPace.Steady)));
        }

        [Test]
        public void OpponentSeesLineTypeButNotSpec()
        {
            var duel = NewDuel();
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Making(durabilityBuffs: 6, boundBindPoints: 1), DrawingPace.Steady), Pass);
            var view = duel.ViewWorkOf(Player.A);
            Assert.AreEqual(WorkKind.Drawing, view.Kind);
            Assert.AreEqual(LineType.Making, view.LineType);
            Assert.IsNull(duel.ViewWorkOf(Player.B));
            // VisibleWork deliberately has no spec, pace or progress to read.
            Assert.IsNull(typeof(VisibleWork).GetProperty("Spec"));
            Assert.IsNull(typeof(VisibleWork).GetProperty("Pace"));
            Assert.IsNull(typeof(VisibleWork).GetProperty("Progress"));
        }

        // ---- Damage, breach and the turn limit ----

        [Test]
        public void BreachingMainWardLosesTheDuel()
        {
            var duel = NewDuel();
            DrawMainWards(duel);
            var wardB = duel.GetPlayer(Player.B).MainWard;

            _movement.NextDamage.Add(new DamageEvent(wardB.Id, 10));
            var report = duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(DuelOutcome.InProgress, report.Outcome);
            Assert.AreEqual(2, wardB.Durability);

            _movement.NextDamage.Add(new DamageEvent(wardB.Id, 2));
            report = duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(DuelOutcome.PlayerAWins, report.Outcome);
            Assert.AreEqual(LineStatus.Broken, wardB.Status);
            Assert.IsTrue(report.Events.Any(e => e.Type == TurnEventType.MainWardBreached && e.Player == Player.B));
            Assert.IsEmpty(duel.AvailableActions(Player.A));
            Assert.Throws<InvalidOperationException>(() => duel.ResolveTurn(Pass, Pass));
        }

        [Test]
        public void BothBreachedOnTheSameTickIsADraw()
        {
            var duel = NewDuel();
            DrawMainWards(duel);
            _movement.NextDamage.Add(new DamageEvent(duel.GetPlayer(Player.A).MainWard.Id, 99));
            _movement.NextDamage.Add(new DamageEvent(duel.GetPlayer(Player.B).MainWard.Id, 99));
            Assert.AreEqual(DuelOutcome.Draw, duel.ResolveTurn(Pass, Pass).Outcome);
        }

        [Test]
        public void BrokenSideLineLosesEffectWithoutEndingTheDuel()
        {
            var duel = NewDuel();
            DrawMainWards(duel);
            var lof = DrawForA(duel, DrawingSpec.Forbiddance(3)); // 12 durability
            _movement.NextDamage.Add(new DamageEvent(lof.Id, 12));
            var report = duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(LineStatus.Broken, lof.Status);
            Assert.AreEqual(DuelOutcome.InProgress, report.Outcome);
        }

        [Test]
        public void EarlierBreachInTheTickWins()
        {
            var duel = NewDuel();
            DrawMainWards(duel);
            _movement.NextDamage.Add(new DamageEvent(duel.GetPlayer(Player.A).MainWard.Id, 99, timeOfImpact: 0.6f));
            _movement.NextDamage.Add(new DamageEvent(duel.GetPlayer(Player.B).MainWard.Id, 99, timeOfImpact: 0.4f));
            Assert.AreEqual(DuelOutcome.PlayerAWins, duel.ResolveTurn(Pass, Pass).Outcome);
        }

        [Test]
        public void LovAndChalklingsCanDamageForbiddance()
        {
            var duel = NewDuel();
            DrawMainWards(duel);
            var lof = DrawForA(duel, DrawingSpec.Forbiddance(3));
            var lov = DrawForB(duel, DrawingSpec.Vigor(VigorTier.High));
            var chalkling = DrawForB(duel, DrawingSpec.Making(damageBuffs: 2, speedBuffs: 1));

            // The geometry layer works out a LoV's reduced damage; the rules core applies what it reports.
            _movement.NextDamage.Add(new DamageEvent(lof.Id, 2, lov.Id));
            duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(10, lof.Durability);

            _movement.NextDamage.Add(new DamageEvent(lof.Id, 2, chalkling.Id));
            duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(8, lof.Durability);
        }

        [Test]
        public void ZeroDamageVigorFizzlesOnCompletion()
        {
            // Roll 0 is -1 under normal odds, so a Low LoV (damage 1) completes with 0 damage.
            var duel = NewDuel(0);
            var lov = DrawForA(duel, DrawingSpec.Vigor(VigorTier.Low));
            Assert.AreEqual(0, lov.Damage);
            Assert.AreEqual(LineStatus.Destroyed, lov.Status);
        }

        [Test]
        public void TurnLimitOutsideTheAllowedRangeIsRejected()
        {
            _config.TurnLimit = 29;
            Assert.Throws<ArgumentException>(() => NewDuel());
            _config.TurnLimit = 121;
            Assert.Throws<ArgumentException>(() => NewDuel());
        }

        [Test]
        public void DecisionTimerMustBeOffOrInRange()
        {
            _config.DecisionTimerSeconds = 0f;
            Assert.DoesNotThrow(() => NewDuel());
            _config.DecisionTimerSeconds = 5f;
            Assert.Throws<ArgumentException>(() => NewDuel());
        }

        [Test]
        public void EveryTurnCountsByDefault()
        {
            var duel = NewDuel();
            for (int i = 0; i < 5; i++) duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(5, duel.CountedTurns);
            Assert.AreEqual(duel.Turn, duel.CountedTurns);
        }

        [Test]
        public void TurnCountCanStartAfterBothMainWards()
        {
            _config.TurnLimitStartsAfterBothMainWards = true;
            var duel = NewDuel();
            for (int i = 0; i < 5; i++) duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(0, duel.CountedTurns);

            DrawMainWards(duel);
            Assert.AreEqual(0, duel.CountedTurns, "the turn the second main LoW completes is not counted");
            duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(1, duel.CountedTurns);
        }

        [Test]
        public void EqualWardsAtTheTurnLimitIsADraw()
        {
            _config.TurnLimitStartsAfterBothMainWards = true; // count from here, so the main LoW turns don't
            _config.TurnLimit = 30;
            var duel = NewDuel();
            DrawMainWards(duel);
            for (int i = 0; i < 29; i++) duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(DuelOutcome.InProgress, duel.Outcome);
            Assert.AreEqual(DuelOutcome.Draw, duel.ResolveTurn(Pass, Pass).Outcome);
        }

        [Test]
        public void TiebreakGoesToTheHigherShareOfMainWardLeft()
        {
            _config.TurnLimit = 30;
            _config.ChalkFatigue.LastTurns = 0;
            // A gets a +1 modifier (14 max), B none (12 max).
            var duel = NewDuel(3, Rolls.NoModifier);
            DrawMainWards(duel);
            var wardA = duel.GetPlayer(Player.A).MainWard;
            var wardB = duel.GetPlayer(Player.B).MainWard;
            Assert.AreEqual(14, wardA.MaxDurability);

            // A: 7/14 = 0.5 left. B: 7/12 left. B has the higher share despite equal durability.
            _movement.NextDamage.Add(new DamageEvent(wardA.Id, 7));
            _movement.NextDamage.Add(new DamageEvent(wardB.Id, 5));
            for (int i = 0; i < 30 && duel.Outcome == DuelOutcome.InProgress; i++) duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(DuelOutcome.PlayerBWins, duel.Outcome);
        }

        [Test]
        public void TurnLimitCanCountFromTheFirstTurn()
        {
            _config.TurnLimit = 30;
            _config.TurnLimitStartsAfterBothMainWards = false;
            var duel = NewDuel();
            for (int i = 0; i < 29; i++) duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(DuelOutcome.Draw, duel.ResolveTurn(Pass, Pass).Outcome);
        }

        // ---- Chalk fatigue ----

        [Test]
        public void ChalkFatigueWearsMainWardsEveryThirdTurnOfTheLastFifteen()
        {
            _config.TurnLimitStartsAfterBothMainWards = true; // count from here, so the main LoW turns don't
            _config.TurnLimit = 30;
            var duel = NewDuel();
            DrawMainWards(duel);
            var wardA = duel.GetPlayer(Player.A).MainWard;

            for (int i = 0; i < 15; i++) duel.ResolveTurn(Pass, Pass);
            Assert.IsTrue(duel.ChalkFatigueActive);
            Assert.AreEqual(12, wardA.Durability, "no fatigue before the last 15 turns");

            duel.ResolveTurn(Pass, Pass); // fatigue turn 1
            duel.ResolveTurn(Pass, Pass); // 2
            Assert.AreEqual(12, wardA.Durability);
            var report = duel.ResolveTurn(Pass, Pass); // 3
            Assert.AreEqual(11, wardA.Durability);
            Assert.AreEqual(2, report.Events.Count(e => e.Type == TurnEventType.ChalkFatigue));

            for (int i = 0; i < 12; i++) duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(7, wardA.Durability);
            Assert.AreEqual(DuelOutcome.Draw, duel.Outcome);
        }

        [Test]
        public void ChalkFatigueCanBreachAMainWard()
        {
            _config.TurnLimit = 30;
            var duel = NewDuel();
            DrawMainWards(duel);
            var wardB = duel.GetPlayer(Player.B).MainWard;
            _movement.NextDamage.Add(new DamageEvent(wardB.Id, 11));
            for (int i = 0; i < 18 && duel.Outcome == DuelOutcome.InProgress; i++) duel.ResolveTurn(Pass, Pass);
            Assert.AreEqual(DuelOutcome.PlayerAWins, duel.Outcome);
        }

        // ---- Decision timer ----

        [Test]
        public void TimeoutContinuesADrawingOrPasses()
        {
            var duel = NewDuel();
            Assert.AreEqual(ActionKind.Pass, duel.TimeoutAction(Player.A).Kind);
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(6)), Pass);
            Assert.AreEqual(ActionKind.ContinueDrawing, duel.TimeoutAction(Player.A).Kind);
        }

        // ---- Dismiss ----

        [Test]
        public void DismissingForbiddanceTakesOneTurn()
        {
            var duel = NewDuel();
            DrawMainWards(duel);
            var lof = DrawForA(duel, DrawingSpec.Forbiddance(3));
            Assert.Contains(ActionKind.DismissForbiddance, duel.AvailableActions(Player.A));

            var report = duel.ResolveTurn(PlayerAction.Dismiss(lof.Id), Pass);
            Assert.AreEqual(LineStatus.Dismissed, lof.Status);
            Assert.IsFalse(duel.GetPlayer(Player.A).HasWorkInProgress);
            Assert.IsTrue(report.Events.Any(e => e.Type == TurnEventType.DismissCompleted));
        }

        [Test]
        public void DismissalCompletesBeforeMovement()
        {
            // DESIGN.md section 3: a LoF dismissed this tick no longer blocks a LoV arriving that tick.
            Line lof = null;
            var seen = new List<LineStatus>();
            var probe = new ProbeMovement(d => { if (lof != null) seen.Add(lof.Status); });
            var duel = new Duel(_config, new SeededRandom(1), probe);
            DrawMainWards(duel);
            lof = DrawForA(duel, DrawingSpec.Forbiddance(3));

            duel.ResolveTurn(PlayerAction.Dismiss(lof.Id), Pass);
            CollectionAssert.AreEqual(new[] { LineStatus.Dismissed }, seen);
        }

        [Test]
        public void OnlyOwnActiveForbiddanceCanBeDismissed()
        {
            var duel = NewDuel();
            DrawMainWards(duel);
            var lof = DrawForA(duel, DrawingSpec.Forbiddance(3));
            Assert.IsNotNull(duel.Validate(Player.B, PlayerAction.Dismiss(lof.Id)));
            Assert.IsNotNull(duel.Validate(Player.A, PlayerAction.Dismiss(duel.GetPlayer(Player.A).MainWard.Id)));
        }

        // ---- Reproducibility ----

        [Test]
        public void SameSeedAndActionsGiveTheSameDuel()
        {
            Assert.AreEqual(PlayScript(99), PlayScript(99));
        }

        private string PlayScript(ulong seed)
        {
            var duel = new Duel(_config, seed);
            var log = new List<string>();
            for (int i = 0; i < 30; i++)
            {
                var a = duel.GetPlayer(Player.A).HasWorkInProgress ? Continue : PlayerAction.Start(DrawingSpec.Forbiddance(i % 4), DrawingPace.Rush);
                var b = duel.GetPlayer(Player.B).HasWorkInProgress ? Continue : PlayerAction.Start(DrawingSpec.Warding(4), DrawingPace.Steady);
                foreach (var e in duel.ResolveTurn(a, b).Events) log.Add(e.ToString());
                if (duel.Outcome != DuelOutcome.InProgress) break;
            }
            foreach (var line in duel.Lines) log.Add(line.Id + ":" + line.ModifierSteps);
            return string.Join("\n", log);
        }

        private sealed class ProbeMovement : IMovementResolver
        {
            private readonly Action<Duel> _onMove;

            public ProbeMovement(Action<Duel> onMove)
            {
                _onMove = onMove;
            }

            public void ResolveMovementAndAttacks(Duel duel, MovementResult result)
            {
                _onMove(duel);
            }

            public void PlaceNewLines(Duel duel, MovementResult result)
            {
            }

            public void FindUnboundLines(Duel duel, System.Collections.Generic.List<int> lost)
            {
            }
        }
    }
}
