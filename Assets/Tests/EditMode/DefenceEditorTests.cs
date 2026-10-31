using NUnit.Framework;
using RithmaticDuels.Rules.Geometry;
using RithmaticDuels.Rules.Presets;

namespace RithmaticDuels.Rules.Tests
{
    /// <summary>The defence editor's practice board and part conversion (DESIGN.md section 6.8).</summary>
    public class DefenceEditorTests
    {
        private RulesConfig _config;

        [SetUp]
        public void SetUp() { _config = new RulesConfig(); }

        private DefenceEditorSession NewSession(string layout = "Four-point circle")
        {
            var preset = new DefencePreset { Name = "Test", Layout = BindPointLayout.Defaults().Find(l => l.Name == layout).Clone() };
            return new DefenceEditorSession(_config, preset);
        }

        [Test]
        public void EmptyDefence_BuildsTheMainWardAndCanAdd()
        {
            var session = NewSession();
            Assert.AreEqual(-2, session.FailedIndex, session.BuildProblem);
            Assert.IsNotNull(session.Board.MainWardOf(Player.A));
            Assert.IsTrue(session.CanAdd);
        }

        [Test]
        public void AddedParts_RoundTripToTheSameBoard()
        {
            var session = NewSession();
            Assert.IsNull(session.Add(DrawingSpec.Forbiddance(2), Placement.BetweenBindPoints(0, 1), DrawingPace.Normal));
            Assert.IsNull(session.Add(DrawingSpec.Warding(4), Placement.AtBindPoint(2), DrawingPace.Normal));
            Assert.AreEqual(2, session.Preset.Components.Count);
            var ward = session.Preset.Components[1];
            Assert.AreEqual(DefencePreset.MainWardId, ward.A.Component);
            Assert.AreEqual(-2, session.FailedIndex, session.BuildProblem);
            Assert.IsNull(DefenceCheck.Problem(session.Preset, _config));
        }

        [Test]
        public void PartOnAChildWard_ReferencesTheChild_AndCantMoveAboveIt()
        {
            var session = NewSession();
            Assert.IsNull(session.Add(DrawingSpec.Warding(4), Placement.AtBindPoint(0), DrawingPace.Normal));
            int wardId = session.Preset.Components[0].Id;
            var wardBody = (WardBody)session.Board.BodyOf(session.LineOf(wardId));
            // A bind point of the new LoW that isn't shared with the main LoW.
            var all = session.Board.BindPointsOf(Player.A);
            int global = -1;
            for (int i = 4; i < all.Count && global < 0; i++)
                foreach (var p in wardBody.BindPoints) if (Vec2.Distance(p, all[i]) < 0.02f) global = i;
            Assert.GreaterOrEqual(global, 0);
            Assert.IsNull(session.Add(DrawingSpec.Making(0, 0, 0, Glyph.Defend, 1, ChalklingStat.Durability), new Placement { Kind = PlacementKind.AtBindPoint, BindPointA = global }, DrawingPace.Normal));
            Assert.AreEqual(wardId, session.Preset.Components[1].A.Component);
            Assert.IsFalse(session.Move(1, 0));
        }

        [Test]
        public void FreeLine_IsStoredInThePresetFrame()
        {
            var session = NewSession();
            var centre = session.Board.MainWardOf(Player.A).Shape.Centre;
            var spec = session.Board.Measure(Player.A, DrawingSpec.Forbiddance(0), Placement.Segment(centre + new Vec2(4f, -1f), centre + new Vec2(4f, 1f)));
            Assert.IsNull(session.Add(spec, Placement.Segment(centre + new Vec2(4f, -1f), centre + new Vec2(4f, 1f)), DrawingPace.Normal));
            var part = session.Preset.Components[0];
            Assert.Less(Vec2.Distance(part.PointA, new Vec2(4f, -1f)), 1e-3f);
        }

        [Test]
        public void RemovingAWard_RemovesWhatIsBuiltOnIt()
        {
            var session = NewSession();
            Assert.IsNull(session.Add(DrawingSpec.Warding(4), Placement.AtBindPoint(0), DrawingPace.Normal));
            int wardId = session.Preset.Components[0].Id;
            var all = session.Board.BindPointsOf(Player.A);
            Assert.IsNull(session.Add(DrawingSpec.Forbiddance(1), Placement.TangentAt(all.Count - 1), DrawingPace.Normal));
            session.Remove(wardId);
            Assert.AreEqual(0, session.Preset.Components.Count);
        }
    }
}
