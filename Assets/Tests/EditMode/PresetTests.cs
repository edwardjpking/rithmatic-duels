using System.Linq;
using NUnit.Framework;
using RithmaticDuels.Rules.Geometry;
using RithmaticDuels.Rules.Presets;

namespace RithmaticDuels.Rules.Tests
{
    /// <summary>Saved defences and bind point layouts (DESIGN.md sections 6.1 and 6.8).</summary>
    public class PresetTests
    {
        private RulesConfig _config;

        [SetUp]
        public void SetUp() { _config = new RulesConfig(); }

        private BindPointLayout Layout(string name) { return BindPointLayout.Defaults().Find(l => l.Name == name); }

        [TestCase("Ellipse", 2)]
        [TestCase("Four-point circle", 4)]
        [TestCase("Six-point circle", 6)]
        [TestCase("Nine-point circle", 9)]
        public void DefaultLayouts_HaveStandardCounts(string name, int count)
        {
            Assert.AreEqual(count, Layout(name).BindPointCount);
        }

        [Test]
        public void DefaultCircleLayouts_MatchTheBoardsStandardPoints()
        {
            foreach (int count in new[] { 4, 6, 9 })
            {
                var layout = BindPointLayout.Defaults().Find(l => !l.IsEllipse && l.BindPointCount == count);
                var facing = new Vec2(1f, 0f);
                var mine = layout.Points(new Vec2(5f, 5f), 3f, facing);
                var standard = BindPoints.ForCircle(count, new Vec2(5f, 5f), 3f, facing);
                for (int i = 0; i < count; i++)
                    Assert.Less(Vec2.Distance(mine[i], standard[i]), 1e-3f, count + "-point, point " + i);
            }
        }

        [Test]
        public void ScaleneTriangle_KeepsEveryCrossing_UpTo12()
        {
            var layout = BindPointLayout.Triangle("Odd", Vec2.FromAngle(0.3f) * 1.4f, Vec2.FromAngle(2.2f) * 1.3f, Vec2.FromAngle(4.0f) * 1.5f);
            Assert.LessOrEqual(layout.BindPointCount, 12);
            Assert.Greater(layout.BindPointCount, 9);
        }

        [Test]
        public void Layout_CollinearVerticesCantBeSaved()
        {
            var layout = BindPointLayout.Triangle("Flat", new Vec2(-1f, 0f), new Vec2(0f, 0f), new Vec2(1f, 0f));
            Assert.IsNotNull(layout.Problem(_config));
        }

        [Test]
        public void SnapVertex_MergesNearbyBindPoints()
        {
            // Equilateral triangle on the circle: altitudes' far ends are distinct from the vertices. Nudging one
            // vertex slightly off the circle makes the edge crossing and the vertex nearly coincide.
            var v = new[] { Vec2.FromAngle(0f), Vec2.FromAngle(2.0944f), Vec2.FromAngle(4.18879f) };
            Vec2 desired = v[0] * 1.02f;
            Vec2 snapped = BindPointLayout.SnapVertex(v, 0, desired, _config.Presets.LayoutSnapDistance);
            var after = BindPointLayout.Triangle("x", snapped, v[1], v[2]);
            var before = BindPointLayout.Triangle("x", desired, v[1], v[2]);
            Assert.Less(after.BindPointCount, before.BindPointCount);
        }

        [Test]
        public void SnapVertex_LeavesFarApartPointsAlone()
        {
            var v = new[] { Vec2.FromAngle(0f), Vec2.FromAngle(2.0944f), Vec2.FromAngle(4.18879f) };
            Vec2 desired = new Vec2(1.6f, 0.4f);
            var trial = BindPointLayout.Triangle("x", desired, v[1], v[2]);
            var points = trial.PointsOnUnitCircle();
            float closest = float.MaxValue;
            for (int i = 0; i < points.Count; i++) for (int j = i + 1; j < points.Count; j++) closest = System.Math.Min(closest, Vec2.Distance(points[i], points[j]));
            Assume.That(closest > _config.Presets.LayoutSnapDistance);
            Assert.AreEqual(desired, BindPointLayout.SnapVertex(v, 0, desired, _config.Presets.LayoutSnapDistance));
        }

        [Test]
        public void EveryDefaultPreset_CanBeBuilt()
        {
            foreach (var preset in DefaultPresets.All(_config))
                Assert.IsNull(DefenceCheck.Problem(preset, _config), preset.Name);
        }

        [Test]
        public void Order_ChildCantMoveAboveParent()
        {
            var p = DefaultPresets.Eskridge(Layout("Six-point circle"), _config);
            int ward = p.Components.FindIndex(c => c.Type == LineType.Warding);
            int child = ward + 1; // its LoF across it
            Assert.IsTrue(p.Components[child].Parents().Contains(p.Components[ward].Id));
            Assert.IsFalse(p.CanMove(child, ward));
            Assert.IsFalse(p.CanMove(ward, child));
            Assert.IsTrue(p.CanMove(child, p.Components.Count - 1));
            Assert.IsTrue(p.CanMove(ward, 0));
        }

        [Test]
        public void StructureProblem_ChildBeforeParent()
        {
            var p = DefaultPresets.Eskridge(Layout("Six-point circle"), _config);
            int ward = p.Components.FindIndex(c => c.Type == LineType.Warding);
            var child = p.Components[ward + 1];
            p.Components.RemoveAt(ward + 1);
            p.Components.Insert(ward, child);
            Assert.IsNotNull(p.StructureProblem(_config));
        }

        [Test]
        public void StructureProblem_BoundChalklingMustDefend()
        {
            var p = DefaultPresets.Ballintain(Layout("Four-point circle"));
            p.Components.Find(c => c.Type == LineType.Making).Glyph = Glyph.Attack;
            Assert.IsNotNull(p.StructureProblem(_config));
        }

        [Test]
        public void StructureProblem_MissingBindPoint()
        {
            var p = DefaultPresets.Ballintain(Layout("Four-point circle"));
            p.Components[0].B = BindRef.Main(7);
            Assert.IsNotNull(p.StructureProblem(_config));
        }

        [Test]
        public void Check_PartThatDoesntFit_CantBeSaved()
        {
            var p = DefaultPresets.Ballintain(Layout("Four-point circle"));
            p.Components.Add(DefenceComponent.ForbiddanceBetween(p.NextId(), BindRef.Main(0), BindRef.Main(2))); // a diameter
            Assert.IsNotNull(DefenceCheck.Problem(p, _config));
            var library = new PresetLibrary();
            var custom = p.Clone(); custom.IsDefault = false; custom.Name = "Mine";
            Assert.IsFalse(library.SavePreset(custom, _config));
        }

        [Test]
        public void RemoveWithChildren_RemovesDependants()
        {
            var p = DefaultPresets.Eskridge(Layout("Six-point circle"), _config);
            var ward = p.Components.First(c => c.Type == LineType.Warding);
            var removed = p.RemoveWithChildren(ward.Id);
            Assert.AreEqual(2, removed.Count);
            Assert.IsNull(p.StructureProblem(_config));
        }

        [Test]
        public void Library_SavesOnlyCustom_AndRejectsDuplicateNames()
        {
            var library = new PresetLibrary().WithDefaults(_config);
            var mine = library.FindPreset("Ballintain").Clone();
            mine.IsDefault = false;
            Assert.IsFalse(library.SavePreset(mine, _config), "name clash");
            mine.Name = "My Ballintain";
            Assert.IsTrue(library.SavePreset(mine, _config));
            var own = library.CustomOnly();
            Assert.AreEqual(1, own.Presets.Count);
            Assert.AreEqual(0, own.Layouts.Count);
            Assert.AreEqual(5, own.WithDefaults(_config).Presets.Count);
        }

        [Test]
        public void CustomLayout_UsedForMainWard()
        {
            var layout = BindPointLayout.Triangle("Odd", Vec2.FromAngle(0.3f) * 1.4f, Vec2.FromAngle(2.2f) * 1.3f, Vec2.FromAngle(4.0f) * 1.5f);
            var preset = new DefencePreset { Name = "Odd one", Layout = layout };
            preset.Components.Add(DefenceComponent.Ward(1, BindRef.Main(layout.BindPointCount - 1), 4));
            Assert.IsNull(DefenceCheck.Problem(preset, _config));

            var board = new ChalkBoard(_config);
            var duel = new Duel(_config, new ScriptedRandom(Rolls.NoModifier), board);
            Build(duel, board, new DefenceRun(preset, Player.A), new DefenceRun(preset, Player.B));
            Assert.AreEqual(layout.BindPointCount + 3, board.BindPointsOf(Player.A).Count);
        }

        [Test]
        public void Run_BuildsEveryPartInTurn_WithNormalDrawTime()
        {
            var preset = DefaultPresets.Ballintain(Layout("Four-point circle"));
            var board = new ChalkBoard(_config);
            var duel = new Duel(_config, new ScriptedRandom(Rolls.NoModifier), board);
            Assert.IsTrue(DefenceRun.CanOffer(duel, Player.A));
            var run = new DefenceRun(preset, Player.A);
            var other = new DefenceRun(preset, Player.B);
            int turns = Build(duel, board, run, other);
            Assert.AreEqual(DefenceRunState.Finished, run.State);
            Assert.AreEqual(1 + preset.Components.Count, duel.Lines.Count(l => l.Owner == Player.A && l.IsActive));
            Assert.IsFalse(DefenceRun.CanOffer(duel, Player.A));
            Assert.Greater(turns, preset.Components.Count, "no time saved");
        }

        [Test]
        public void Run_Cancel_EndsForGood_AndKeepsFinishedParts()
        {
            var preset = DefaultPresets.Ballintain(Layout("Four-point circle"));
            var board = new ChalkBoard(_config);
            var duel = new Duel(_config, new ScriptedRandom(Rolls.NoModifier), board);
            var run = new DefenceRun(preset, Player.A);
            while (run.IsRunning && run.CurrentIndex < 1) Step(duel, board, run, null);
            Step(duel, board, run, null); // start part 2
            Assert.IsTrue(duel.GetPlayer(Player.A).HasWorkInProgress);
            var cancel = run.Cancel(duel);
            duel.ResolveTurn(cancel, DefenceCheck.StandIn(duel, Player.B));
            Assert.AreEqual(DefenceRunState.Cancelled, run.State);
            Assert.IsFalse(duel.GetPlayer(Player.A).HasWorkInProgress);
            Placement unused;
            Assert.IsNull(run.ContinueAction(duel, board, out unused));
            Assert.AreEqual(2, duel.Lines.Count(l => l.Owner == Player.A && l.IsActive));
        }

        [Test]
        public void Run_PartThatNoLongerFits_IsSkipped()
        {
            var preset = DefaultPresets.Eskridge(Layout("Six-point circle"), _config);
            var board = new ChalkBoard(_config);
            var duel = new Duel(_config, new ScriptedRandom(Rolls.NoModifier), board);
            var run = new DefenceRun(preset, Player.A);
            int ward = preset.Components.FindIndex(c => c.Type == LineType.Warding);
            // Swap the outer LoW for one at a bind point it can't use, so it and the LoF across it can't be built.
            preset = run.Preset;
            preset.Components[ward].A = BindRef.Main(1);
            preset.Components.Insert(ward, DefenceComponent.Ward(99, BindRef.Main(1), 4, _config.Geometry.AttachedWardMinRadius));
            int guard = 0;
            while (run.IsRunning && guard++ < 300) Step(duel, board, run, null);
            Assert.AreEqual(DefenceRunState.Finished, run.State);
            Assert.Greater(run.Skipped.Count, 0);
            Assert.IsNotNull(DefenceCheck.Problem(preset, _config));
        }

        private static void Step(Duel duel, ChalkBoard board, DefenceRun a, DefenceRun b)
        {
            Placement pa = null, pb = null;
            var actionA = a != null && a.IsRunning ? a.ContinueAction(duel, board, out pa) : null;
            var actionB = b != null && b.IsRunning ? b.ContinueAction(duel, board, out pb) : null;
            if (pa != null) board.SetPendingPlacement(Player.A, actionA.Spec, pa);
            if (pb != null) board.SetPendingPlacement(Player.B, actionB.Spec, pb);
            actionA = actionA ?? (a != null ? PlayerAction.Pass() : DefenceCheck.StandIn(duel, Player.A));
            actionB = actionB ?? (b != null ? PlayerAction.Pass() : DefenceCheck.StandIn(duel, Player.B));
            Assert.IsNull(duel.Validate(Player.A, actionA));
            Assert.IsNull(duel.Validate(Player.B, actionB));
            var report = duel.ResolveTurn(actionA, actionB);
            if (a != null) a.Observe(duel, report);
            if (b != null) b.Observe(duel, report);
        }

        private static int Build(Duel duel, ChalkBoard board, DefenceRun a, DefenceRun b)
        {
            int turns = 0;
            while ((a.IsRunning || b.IsRunning) && turns < 200) { Step(duel, board, a, b); turns++; }
            Assert.AreNotEqual(DefenceRunState.Blocked, a.State, a.BlockedReason);
            return turns;
        }
    
        [Test]
        public void EditorVertex_RefusesFlatTriangleAndFarSpots()
        {
            var v = new[] { new Vec2(1f, 0f), new Vec2(-0.5f, 0.87f), new Vec2(-0.5f, -0.87f) };
            Assert.IsTrue(BindPointLayout.VertexAllowed(v, 0, new Vec2(0.9f, 0.1f), _config.Presets));
            // On the line through the other two vertices.
            Assert.IsFalse(BindPointLayout.VertexAllowed(v, 0, new Vec2(-0.5f, 0.3f), _config.Presets));
            // On top of another vertex.
            Assert.IsFalse(BindPointLayout.VertexAllowed(v, 0, new Vec2(-0.5f, 0.88f), _config.Presets));
            // Too far out.
            Assert.IsFalse(BindPointLayout.VertexAllowed(v, 0, new Vec2(_config.Presets.LayoutMaxVertexRadius + 0.5f, 0f), _config.Presets));
        }
    }
}
