using System.Collections.Generic;
using RithmaticDuels.Rules.Geometry;

namespace RithmaticDuels.Rules.Presets
{
    /// <summary>
    /// The defence being edited in the defence editor (DESIGN.md section 6.8), with a practice board that shows it
    /// built. After every change the defence is built again, in order, on a fresh board for player A (with no
    /// modifiers), so the board always shows what the saved defence would build. New parts are placed on that
    /// board with the duel's own placement menu and turned into preset parts here.
    /// </summary>
    public sealed class DefenceEditorSession
    {
        private readonly RulesConfig _config;

        public DefencePreset Preset { get; private set; }
        public ChalkBoard Board { get; private set; }
        public Duel Duel { get; private set; }
        /// <summary>The build of the current defence, to find each part's line on <see cref="Board"/>.</summary>
        public DefenceRun Run { get; private set; }
        /// <summary>The player the defence is built for on the practice board.</summary>
        public Player Player { get { return Player.A; } }
        /// <summary>
        /// Index of the first part that couldn't be built (-1 for the main LoW), or -2 if everything was built.
        /// Parts after it aren't on the board.
        /// </summary>
        public int FailedIndex { get; private set; }
        /// <summary>Why the failing part couldn't be built, or null.</summary>
        public string BuildProblem { get; private set; }

        public DefenceEditorSession(RulesConfig config, DefencePreset preset)
        {
            _config = config;
            Preset = preset;
            Rebuild();
        }

        /// <summary>Goes up every time the defence is rebuilt, so callers can cache what they work out from it.</summary>
        public int Version { get; private set; }

        public RulesConfig Config { get { return _config; } }

        /// <summary>True when every part is on the board, so a new one can be added.</summary>
        public bool CanAdd { get { return FailedIndex == -2 && Duel.AvailableActions(Player).Contains(ActionKind.StartDrawing); } }

        /// <summary>Builds the defence again from scratch. Call after any change to <see cref="Preset"/>.</summary>
        public void Rebuild()
        {
            Version++;
            Board = new ChalkBoard(_config);
            Duel = new Duel(_config, new NoModifiers(), Board);
            Run = new DefenceRun(Preset, Player);
            FailedIndex = -2;
            BuildProblem = null;
            int guard = 0;
            while (Run.IsRunning && guard++ < 1000)
            {
                Placement placement;
                var action = Run.ContinueAction(Duel, Board, out placement);
                if (action == null) break;
                if (placement != null) Board.SetPendingPlacement(Player, action.Spec, placement);
                string problem = Duel.Validate(Player, action);
                if (problem != null) { FailedIndex = Run.CurrentIndex; BuildProblem = problem; return; }
                var report = Duel.ResolveTurn(action, DefenceCheck.StandIn(Duel, Player.Opponent()));
                Run.Observe(Duel, report);
                if (Duel.Outcome != DuelOutcome.InProgress) break;
            }
            if (Run.State != DefenceRunState.Finished)
            {
                FailedIndex = Run.CurrentIndex;
                BuildProblem = Run.BlockedReason ?? "This part couldn't be built.";
            }
        }

        /// <summary>
        /// Adds a part placed on the practice board (a Start drawing choice from the placement menu) to the end of
        /// the build order. Null if it was added; otherwise why not, and nothing changes.
        /// </summary>
        public string Add(DrawingSpec spec, Placement placement, DrawingPace pace)
        {
            if (!CanAdd) return "Fix the part that can't be built first.";
            string problem;
            var part = ToComponent(spec, placement, pace, out problem);
            if (part == null) return problem;
            Preset.Components.Add(part);
            Rebuild();
            if (FailedIndex != -2)
            {
                // Shouldn't happen, since the menu only offers placements that fit; undo it if it does.
                string why = BuildProblem;
                Preset.Components.RemoveAt(Preset.Components.Count - 1);
                Rebuild();
                return why;
            }
            return null;
        }

        /// <summary>Moves a part in the build order, never above its parent. Returns whether it moved.</summary>
        public bool Move(int from, int to)
        {
            if (!Preset.Move(from, to)) return false;
            Rebuild();
            return true;
        }

        /// <summary>Removes a part and every part built on it. Returns the ids removed.</summary>
        public List<int> Remove(int id)
        {
            var removed = Preset.RemoveWithChildren(id);
            Rebuild();
            return removed;
        }

        /// <summary>Changes the bind point layout. Every part is removed, since its bind points may not exist any more.</summary>
        public void SetLayout(BindPointLayout layout)
        {
            Preset.Layout = layout.Clone();
            Preset.Components.Clear();
            Rebuild();
        }

        /// <summary>The line built for a part on the practice board (0 for the main LoW), or -1.</summary>
        public int LineOf(int componentId) { return Run.LineOf(componentId); }

        /// <summary>Null if the defence can be saved in <paramref name="library"/>, otherwise why not.</summary>
        public string SaveProblem(PresetLibrary library, DefencePreset replacing = null)
        {
            return library.PresetSaveProblem(Preset, _config, replacing);
        }

        /// <summary>
        /// Turns a placement on the practice board into a preset part: bind points become references to the part
        /// that owns them, free points move into the preset's frame.
        /// </summary>
        public DefenceComponent ToComponent(DrawingSpec spec, Placement placement, DrawingPace pace, out string problem)
        {
            problem = null;
            if (spec == null || placement == null) { problem = "Place it on the board first."; return null; }
            if (spec.Type == LineType.Vigor) { problem = "A defence can't hold a Line of Vigor."; return null; }
            var part = new DefenceComponent { Id = Preset.NextId(), Type = spec.Type, Kind = placement.Kind, Pace = pace };
            if (part.UsesA && !ToBindRef(placement.BindPointA, out part.A)) { problem = "That bind point isn't part of this defence."; return null; }
            if (part.UsesB && !ToBindRef(placement.BindPointB, out part.B)) { problem = "That bind point isn't part of this defence."; return null; }
            part.PointA = ToLocal(placement.PointA);
            part.PointB = ToLocal(placement.PointB);
            switch (spec.Type)
            {
                case LineType.Warding:
                    part.BindPoints = spec.BindPoints;
                    part.IsMarksCross = spec.IsMarksCross;
                    part.Size = placement.Size;
                    part.Scale = placement.Scale;
                    part.Join = placement.Join;
                    break;
                case LineType.Forbiddance:
                    part.Length = placement.Length;
                    break;
                case LineType.Making:
                    part.DurabilityBuffs = spec.DurabilityBuffs;
                    part.DamageBuffs = spec.DamageBuffs;
                    part.SpeedBuffs = spec.SpeedBuffs;
                    part.Glyph = spec.Glyph;
                    part.PositiveModifierStat = spec.PositiveModifierStat;
                    break;
            }
            return part;
        }

        /// <summary>A bind point on the board (by its index in BindPointsOf) as a reference to the earliest part that owns it.</summary>
        private bool ToBindRef(int globalIndex, out BindRef result)
        {
            result = default(BindRef);
            var all = Board.BindPointsOf(Player);
            if (globalIndex < 0 || globalIndex >= all.Count) return false;
            Vec2 spot = all[globalIndex];
            // The main LoW first, then the parts in build order, so a shared point goes to the earliest (the parent).
            var owners = new List<int> { DefencePreset.MainWardId };
            foreach (var c in Preset.Components) if (c.Type == LineType.Warding) owners.Add(c.Id);
            foreach (int id in owners)
            {
                var ward = Board.BodyOf(LineOf(id)) as WardBody;
                if (ward == null) continue;
                for (int i = 0; i < ward.BindPoints.Count; i++)
                {
                    if (Vec2.Distance(ward.BindPoints[i], spot) < 0.02f)
                    {
                        result = new BindRef(id, i);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>A board point in the preset's frame (origin at the main LoW's centre, +X toward the opponent).</summary>
        private Vec2 ToLocal(Vec2 boardPoint)
        {
            var main = Board.MainWardOf(Player);
            Vec2 centre = main != null ? main.Shape.Centre : Board.Home(Player);
            return (boardPoint - centre).Rotated(-Board.Facing(Player).Angle);
        }

        /// <summary>A one-line name for a part, for the editor's list.</summary>
        public string Describe(DefenceComponent part)
        {
            switch (part.Type)
            {
                case LineType.Warding:
                    string what = part.IsMarksCross ? "Mark's Cross" : part.BindPoints + "-point LoW";
                    return what + " at " + Where(part.A);
                case LineType.Forbiddance:
                    if (part.Kind == PlacementKind.BetweenBindPoints) return "LoF from " + Where(part.A) + " to " + Where(part.B);
                    if (part.Kind == PlacementKind.TangentAtBindPoint) return "LoF tangent at " + Where(part.A);
                    return "Free LoF";
                case LineType.Making:
                    string glyph = part.Glyph == Glyph.Defend ? "defending" : "attacking";
                    string stats = " (" + part.DurabilityBuffs + "/" + part.DamageBuffs + "/" + part.SpeedBuffs + ")";
                    return part.Kind == PlacementKind.AtBindPoint ? "Chalkling bound at " + Where(part.A) + stats : "Free " + glyph + " chalkling" + stats;
                default:
                    return part.Type.ToString();
            }
        }

        private string Where(BindRef point)
        {
            if (point.Component == DefencePreset.MainWardId) return "main LoW point " + (point.Index + 1);
            int index = Preset.IndexOf(point.Component);
            return "part " + (index + 1) + " point " + (point.Index + 1);
        }

        /// <summary>Rolls that always give no modifier, so the practice board matches the save check.</summary>
        private sealed class NoModifiers : IRandomSource
        {
            public int NextInt(int maxExclusive) { return maxExclusive - 1; }
        }
    }
}
