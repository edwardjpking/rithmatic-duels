using System.Linq;
using NUnit.Framework;

namespace RithmaticDuels.Rules.Tests
{
    public class TurnNarratorTests
    {
        [Test]
        public void StartingADrawingRevealsOnlyItsLineType()
        {
            var duel = new Duel(new RulesConfig { MainWardFirst = false }, new ScriptedRandom(Rolls.NoModifier));
            var report = duel.ResolveTurn(
                PlayerAction.Start(DrawingSpec.Warding(9), DrawingPace.Steady),
                PlayerAction.Pass());

            var text = TurnNarrator.Describe(duel, report).Single();
            Assert.AreEqual("Player A started drawing a Line of Warding.", text);
        }

        [Test]
        public void CompletionNamesTheLineAndItsModifier()
        {
            // Roll 3 is the first +1 outcome with the default odds.
            var duel = new Duel(new RulesConfig { MainWardFirst = false }, new ScriptedRandom(Rolls.NoModifier, 3));
            var report = duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(2)), PlayerAction.Pass());

            var lines = TurnNarrator.Describe(duel, report);
            CollectionAssert.Contains(lines, "Player A completed A's main Line of Warding #1 with positive modifier.");
        }

        [Test]
        public void MovementNotesGoBeforeDamage()
        {
            var config = new RulesConfig();
            var movement = new ScriptedMovement();
            var duel = new Duel(config, new ScriptedRandom(Rolls.NoModifier), movement);
            duel.ResolveTurn(PlayerAction.Start(DrawingSpec.Warding(2)), PlayerAction.Start(DrawingSpec.Warding(2)));

            movement.NextDamage.Add(new DamageEvent(duel.GetPlayer(Player.B).MainWard.Id, 2));
            var report = duel.ResolveTurn(PlayerAction.Pass(), PlayerAction.Pass());
            var lines = TurnNarrator.Describe(duel, report, new[] { "Something flew." });

            Assert.AreEqual("Something flew.", lines[0]);
            Assert.AreEqual("B's main Line of Warding #2 took 2 damage.", lines[1]);
        }
    }
}
