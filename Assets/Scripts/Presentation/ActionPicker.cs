using System.Collections.Generic;
using RithmaticDuels.Rules;
using RithmaticDuels.Rules.Geometry;
using UnityEngine;

namespace RithmaticDuels.Presentation
{
    /// <summary>What the player has chosen so far for a new drawing, drawn faintly on the board.</summary>
    public sealed class PlacementPreview
    {
        public Player Player;
        public WardShape Ward;
        /// <summary>Bind points the previewed LoW would have.</summary>
        public IReadOnlyList<Vec2> WardBindPoints;
        public Segment? Segment;
        public Vec2? From;
        public Vec2? To;
        public Vec2? Point;
        /// <summary>Bind points of the main LoW the player has picked.</summary>
        public readonly List<int> BindPoints = new List<int>();
    }

    /// <summary>A locked-in choice: the action, and for Start drawing, where it goes.</summary>
    public sealed class LockedChoice
    {
        public PlayerAction Action;
        public Placement Placement;
    }

    /// <summary>
    /// The action menu for one player in the hot-seat duel, drawn with Unity's immediate-mode GUI.
    /// It builds a <see cref="PlayerAction"/> and, for new drawings, a <see cref="Placement"/> from clicks
    /// and the scroll wheel on the board. The rules engine and the board decide whether they are legal.
    /// The duel controller makes a fresh picker at the start of each player's turn, so nothing is
    /// selected until the player selects it.
    ///
    /// Immediate-mode GUI lays a window out once and then replays that layout for clicks, so any
    /// click that changes what is shown stops the current pass with <see cref="GUIUtility.ExitGUI"/>.
    /// The helpers at the bottom do this, which is why they take the field to change by ref.
    /// </summary>
    public sealed class ActionPicker
    {
        private static readonly string[] LineTypeLabels = { "Warding", "Forbiddance", "Vigor", "Making" };
        private static readonly string[] PaceLabels = { "Normal", "Rush", "Steady" };
        private static readonly string[] TierLabels = { "Low", "Medium", "High" };
        private static readonly string[] StatLabels = { "Durability", "Damage", "Speed" };
        private static readonly string[] ShapeLabels = { "Circle", "Ellipse" };
        private static readonly string[] CircleBindPointLabels = { "4", "6", "9" };
        private static readonly int[] CircleBindPointCounts = { 4, 6, 9 };
        private static readonly string[] AttachedBindPointLabels = { "2", "4", "6", "9" };
        private static readonly int[] AttachedBindPointCounts = { 2, 4, 6, 9 };
        private static readonly string[] ForbiddanceLabels = { "Between 2 bind points", "Tangent at 1", "Free line" };
        private static readonly string[] ChalklingLabels = { "Bound to a bind point", "Free" };

        /// <summary>How close a click must be to a bind point to pick it.</summary>
        private const float BindPointPickRadius = 0.75f;

        private enum MainShape { Circle, Ellipse }
        private enum ForbiddanceMode { Between, Tangent, Free }
        private enum ChalklingMode { Bound, Free }

        // Every choice starts unselected (null or -1). Pace defaults to Normal.
        private ActionKind? _kind;
        private LineType? _lineType;
        private DrawingPace _pace;
        private MainShape? _mainShape;
        private int _bindPointChoice = -1;
        private bool _marksCross;
        private ForbiddanceMode? _forbiddanceMode;
        private ChalklingMode? _chalklingMode;
        private VigorTier? _tier;
        private int _durabilityBuffs = 1;
        private int _damageBuffs;
        private int _speedBuffs;
        private int _glyphs;
        private ChalklingStat? _modifierStat;
        private int _targetLineId = -1;
        private int _amount = 1;
        private ChalklingStat? _buffStat;

        // Sizes set with the scroll wheel. Negative means "not touched yet", so the config default is used.
        private float _semiMinor = -1f;
        private float _length = -1f;

        // Placement clicks: up to two bind points or two board points, whichever the mode uses.
        private int _bindA = -1;
        private int _bindB = -1;
        private Vec2? _pointA;
        private Vec2? _pointB;
        private Vec2? _hover;
        private string _clickKey;

        /// <summary>What to draw on the board for the choice in progress. Null when there is nothing to show.</summary>
        public PlacementPreview Preview { get; private set; }

        /// <summary>Where the mouse is over the board, in board units, or null when it is elsewhere.</summary>
        public void SetHover(Vec2? point)
        {
            _hover = point;
        }

        /// <summary>Draws the menu. Returns the choice once the player locks it in, otherwise null.</summary>
        public LockedChoice Draw(Duel duel, ChalkBoard board, Player player)
        {
            Preview = null;
            var available = duel.AvailableActions(player);
            if (available.Count == 0) return null;
            if (_kind.HasValue && !available.Contains(_kind.Value)) _kind = null;

            if (available.Count == 1 && available[0] == ActionKind.StartDrawing && board.MainWardOf(player) == null)
            {
                // Setting up the main LoW is the only thing to do, so there is no action to pick.
                _kind = ActionKind.StartDrawing;
            }
            else
            {
                var labels = new string[available.Count];
                for (int i = 0; i < available.Count; i++) labels[i] = ActionLabel(available[i]);
                int index = _kind.HasValue ? available.IndexOf(_kind.Value) : -1;
                int chosen = GUILayout.SelectionGrid(index, labels, 3);
                if (chosen != index && chosen >= 0)
                {
                    _kind = available[chosen];
                    GUIUtility.ExitGUI();
                }
                GUILayout.Space(8);
            }

            PlayerAction action = null;
            Placement placement = null;
            string hint = "Choose an action.";
            switch (_kind)
            {
                case ActionKind.StartDrawing:
                    action = board.MainWardOf(player) == null
                        ? DrawMainWardSetup(duel.Config, board, player, out placement, out hint)
                        : DrawStart(duel.Config, board, player, out placement, out hint);
                    break;
                case ActionKind.ContinueDrawing:
                    GUILayout.Label("Keep going. " + duel.GetPlayer(player).Work.TurnsRemaining + " turn(s) left, including this one.");
                    action = PlayerAction.Continue();
                    break;
                case ActionKind.CancelDrawing:
                    GUILayout.Label("Abandon your current work. All progress on it is lost.");
                    action = PlayerAction.Cancel();
                    break;
                case ActionKind.AddToDrawing:
                    action = DrawAddTo(duel, player, out hint);
                    break;
                case ActionKind.DismissForbiddance:
                    action = DrawDismiss(duel, player, out hint);
                    break;
                case ActionKind.Pass:
                    GUILayout.Label("Do nothing this turn.");
                    action = PlayerAction.Pass();
                    break;
            }

            GUILayout.FlexibleSpace();
            string problem = action == null ? hint : duel.Validate(player, action);
            if (problem == null && action.Kind == ActionKind.StartDrawing)
                problem = placement == null ? "Finish placing it on the board." : board.ValidatePlacement(player, action.Spec, placement);
            if (problem != null) GUILayout.Label(problem);

            GUI.enabled = problem == null;
            bool locked = GUILayout.Button("Lock in", GUILayout.Height(40));
            GUI.enabled = true;
            if (!locked) return null;
            return new LockedChoice { Action = action, Placement = placement };
        }

        /// <summary>A click on the board, in board units. Returns true if it changed the choice.</summary>
        public bool HandleBoardClick(ChalkBoard board, Player player, Vec2 point)
        {
            if (_kind != ActionKind.StartDrawing) return false;

            if (board.MainWardOf(player) == null)
            {
                if (!_mainShape.HasValue) return false;
                _pointA = point;
                return true;
            }

            switch (_lineType)
            {
                case LineType.Warding:
                    return PickBindPoint(board, player, point, false);
                case LineType.Forbiddance:
                    if (!_forbiddanceMode.HasValue) return false;
                    if (_forbiddanceMode == ForbiddanceMode.Free) return PickPoints(point);
                    return PickBindPoint(board, player, point, _forbiddanceMode == ForbiddanceMode.Between);
                case LineType.Vigor:
                    return _tier.HasValue && PickPoints(point);
                case LineType.Making:
                    if (!_chalklingMode.HasValue) return false;
                    if (_chalklingMode == ChalklingMode.Free)
                    {
                        _pointA = point;
                        return true;
                    }
                    return PickBindPoint(board, player, point, false);
                default:
                    return false;
            }
        }

        /// <summary>
        /// The scroll wheel over the board: makes a main LoW ellipse flatter or rounder, or a LoF not bound at
        /// two points shorter or longer. Returns true if it changed the choice.
        /// </summary>
        public bool HandleScroll(ChalkBoard board, Player player, RulesConfig config, float delta)
        {
            if (_kind != ActionKind.StartDrawing || Mathf.Abs(delta) < 1e-6f) return false;
            var geometry = config.Geometry;
            // Scrolling up (negative delta) makes things bigger.
            float step = delta < 0f ? geometry.ScrollStep : -geometry.ScrollStep;

            if (board.MainWardOf(player) == null)
            {
                if (_mainShape != MainShape.Ellipse) return false;
                _semiMinor = Mathf.Clamp(SemiMinor(config) + step, geometry.EllipseMinSemiMinor, geometry.EllipseMaxSemiMinor);
                return true;
            }

            if (_lineType == LineType.Forbiddance && (_forbiddanceMode == ForbiddanceMode.Tangent || _forbiddanceMode == ForbiddanceMode.Free))
            {
                _length = Mathf.Clamp(Length(config) + step, geometry.MinForbiddanceLength, geometry.MaxForbiddanceLength);
                return true;
            }
            return false;
        }

        /// <summary>
        /// The first thing each player does (DESIGN.md section 2): choose the main LoW's shape, place it on
        /// their half, then choose its bind points. Each step unlocks once the one before is done.
        /// </summary>
        private PlayerAction DrawMainWardSetup(RulesConfig config, ChalkBoard board, Player player, out Placement placement, out string hint)
        {
            ResetClicksIfChanged("main");
            placement = null;
            GUILayout.Label("Set up your main Line of Warding. You lose if it is breached.");

            GUILayout.Label("1. Shape:");
            Choose(ref _mainShape, ShapeLabels);
            if (!_mainShape.HasValue)
            {
                hint = "Choose a shape.";
                return null;
            }
            bool ellipse = _mainShape == MainShape.Ellipse;
            if (ellipse)
                GUILayout.Label("Scroll over the board to make it flatter or rounder (" + (2f * SemiMinor(config)).ToString("0.00")
                    + " across, " + (2f * config.Geometry.EllipseSemiMajor).ToString("0.0") + " long). Flatter ellipses are weaker along their sides.");

            GUILayout.Label("2. Click on your half of the board to place it" + (_pointA.HasValue ? ". Click again to move it." : "."));

            GUILayout.Label("3. Bind points:");
            int bindPoints = -1;
            if (ellipse)
            {
                GUILayout.Label("    2, at the ends of its long axis.");
                if (_pointA.HasValue) bindPoints = 2;
            }
            else
            {
                GUI.enabled = _pointA.HasValue;
                Choose(ref _bindPointChoice, CircleBindPointLabels);
                GUI.enabled = true;
                if (_pointA.HasValue && _bindPointChoice >= 0) bindPoints = CircleBindPointCounts[_bindPointChoice];
            }

            // Preview at the click, or following the mouse until then.
            Vec2? centre = _pointA ?? _hover;
            var preview = new PlacementPreview { Player = player };
            if (centre.HasValue)
            {
                var shapeSpec = DrawingSpec.Warding(ellipse ? 2 : Mathf.Max(4, bindPoints));
                var shapePlacement = Placement.MainWard(centre.Value, ellipse ? SemiMinor(config) : 0f);
                IReadOnlyList<Vec2> points;
                preview.Ward = board.PreviewWard(player, shapeSpec, shapePlacement, out points);
                if (bindPoints > 0) preview.WardBindPoints = points;
            }
            Preview = preview;

            if (!_pointA.HasValue)
            {
                hint = "Click the board to place it.";
                return null;
            }
            if (bindPoints < 0)
            {
                hint = "Choose how many bind points it has.";
                return null;
            }

            var spec = DrawingSpec.Warding(bindPoints);
            placement = Placement.MainWard(_pointA.Value, ellipse ? SemiMinor(config) : 0f);
            GUILayout.Space(8);
            DrawPaceAndTiming(config, spec);
            hint = null;
            return PlayerAction.Start(spec, _pace);
        }

        private PlayerAction DrawStart(RulesConfig config, ChalkBoard board, Player player, out Placement placement, out string hint)
        {
            // Clicks belong to one line type and placement mode; switching either starts placement over.
            ResetClicksIfChanged(_lineType + "/" + _forbiddanceMode + "/" + _chalklingMode + "/" + _marksCross);
            placement = null;
            Choose(ref _lineType, LineTypeLabels);
            if (!_lineType.HasValue)
            {
                hint = "Choose a line type.";
                return null;
            }

            var preview = new PlacementPreview { Player = player };
            Preview = preview;
            DrawingSpec spec = null;
            hint = null;
            switch (_lineType)
            {
                case LineType.Warding:
                    Toggle(ref _marksCross, " Mark's Cross (a small brace)");
                    if (_marksCross) spec = DrawingSpec.MarksCross(config);
                    else
                    {
                        GUILayout.Label("Bind points:");
                        Choose(ref _bindPointChoice, AttachedBindPointLabels);
                        if (_bindPointChoice >= 0) spec = DrawingSpec.Warding(AttachedBindPointCounts[_bindPointChoice]);
                        else hint = "Choose how many bind points it has.";
                    }
                    GUILayout.Label("Click a bind point on your main Line of Warding to attach this circle there.");
                    if (_bindA >= 0) placement = Placement.AtBindPoint(_bindA);
                    if (spec != null && placement != null)
                    {
                        IReadOnlyList<Vec2> points;
                        preview.Ward = board.PreviewWard(player, spec, placement, out points);
                        preview.WardBindPoints = points;
                    }
                    break;

                case LineType.Forbiddance:
                    Choose(ref _forbiddanceMode, ForbiddanceLabels);
                    switch (_forbiddanceMode)
                    {
                        case ForbiddanceMode.Between:
                            GUILayout.Label("Click two bind points on your main Line of Warding.");
                            spec = DrawingSpec.Forbiddance(2);
                            if (_bindA >= 0 && _bindB >= 0) placement = Placement.BetweenBindPoints(_bindA, _bindB);
                            break;
                        case ForbiddanceMode.Tangent:
                            GUILayout.Label("Click a bind point. The line lies along the circle there.");
                            GUILayout.Label("Scroll over the board to change its length (" + Length(config).ToString("0.00") + ").");
                            spec = DrawingSpec.Forbiddance(1);
                            if (_bindA >= 0) placement = Placement.TangentAt(_bindA, Length(config));
                            break;
                        case ForbiddanceMode.Free:
                            GUILayout.Label("Click where the line starts, then click to set its direction. Draw on your half.");
                            GUILayout.Label("Scroll over the board to change its length (" + Length(config).ToString("0.00") + ").");
                            spec = DrawingSpec.Forbiddance(0);
                            Vec2? toward = _pointB ?? _hover;
                            if (_pointA.HasValue && toward.HasValue && Vec2.Distance(_pointA.Value, toward.Value) > 1e-3f)
                            {
                                Vec2 end = _pointA.Value + (toward.Value - _pointA.Value).Normalized * Length(config);
                                preview.Segment = new Segment(_pointA.Value, end);
                                if (_pointB.HasValue) placement = Placement.Segment(_pointA.Value, end);
                            }
                            preview.From = _pointA;
                            break;
                        default:
                            hint = "Choose how to place it.";
                            break;
                    }
                    if (placement != null) preview.Segment = board.PreviewForbiddance(player, placement);
                    break;

                case LineType.Vigor:
                    Choose(ref _tier, TierLabels);
                    if (!_tier.HasValue)
                    {
                        hint = "Choose a tier.";
                        break;
                    }
                    var tier = config.Vigor.Tier(_tier.Value);
                    GUILayout.Label("Speed " + tier.Speed + ", damage " + tier.Damage + " before its modifier.");
                    GUILayout.Label("Click where to draw it on your half, then click where to aim.");
                    spec = DrawingSpec.Vigor(_tier.Value);
                    if (_pointA.HasValue && _pointB.HasValue) placement = Placement.Aimed(_pointA.Value, _pointB.Value);
                    preview.From = _pointA;
                    preview.To = _pointB ?? (_pointA.HasValue ? _hover : null);
                    break;

                case LineType.Making:
                    Stepper("Durability buffs", ref _durabilityBuffs, 0, 12);
                    Stepper("Damage buffs", ref _damageBuffs, 0, 12);
                    Stepper("Speed buffs", ref _speedBuffs, 0, 12);
                    int maxGlyphs = DrawTimes.MaxGlyphs(_durabilityBuffs + _damageBuffs + _speedBuffs, config);
                    Stepper("Glyphs (max " + maxGlyphs + ")", ref _glyphs, 0, maxGlyphs);
                    GUILayout.Label("A positive modifier goes to:");
                    Choose(ref _modifierStat, StatLabels);
                    Choose(ref _chalklingMode, ChalklingLabels);
                    GUILayout.Label("Chalklings stand still until chalkling behaviour is built.");
                    if (!_modifierStat.HasValue || !_chalklingMode.HasValue)
                    {
                        hint = !_modifierStat.HasValue ? "Choose where a positive modifier goes." : "Choose whether it is bound.";
                        break;
                    }
                    bool bound = _chalklingMode == ChalklingMode.Bound;
                    GUILayout.Label(bound ? "Click a bind point to tether it there." : "Click where to draw it on your half.");
                    spec = DrawingSpec.Making(_durabilityBuffs, _damageBuffs, _speedBuffs, _glyphs, bound ? 1 : 0, _modifierStat.Value);
                    if (bound && _bindA >= 0) placement = Placement.AtBindPoint(_bindA);
                    if (!bound && _pointA.HasValue) placement = Placement.At(_pointA.Value);
                    preview.Point = bound ? null : _pointA;
                    break;
            }
            if (_bindA >= 0) preview.BindPoints.Add(_bindA);
            if (_bindB >= 0) preview.BindPoints.Add(_bindB);
            if (spec == null) return null;

            if (placement != null)
            {
                int travel = board.TravelTurns(player, placement);
                spec = spec.WithTravel(travel);
                if (travel > 0)
                    GUILayout.Label("This is " + board.TravelDistance(player, placement).ToString("0.0")
                        + " units from your main Line of Warding: +" + travel + " turn(s) to get there.");
            }

            GUILayout.Space(8);
            DrawPaceAndTiming(config, spec);
            return PlayerAction.Start(spec, _pace);
        }

        private void DrawPaceAndTiming(RulesConfig config, DrawingSpec spec)
        {
            Choose(ref _pace, PaceLabels);
            if (DrawTimes.Validate(spec, config) != null) return;

            int total = DrawTimes.Turns(spec, _pace, config);
            bool instant = _pace == DrawingPace.Rush && config.RushedOneTurnDrawingsCompleteImmediately
                && DrawTimes.BaseTurns(spec, config) == 1 && spec.TravelTurns == 0;
            int more = instant ? 0 : total - (config.StartActionCountsAsProgress ? 1 : 0);
            GUILayout.Label(more <= 0
                ? "Completes this turn."
                : "Takes " + total + " turn(s): this one and " + more + " more of Continue drawing.");
        }

        private PlayerAction DrawAddTo(Duel duel, Player player, out string hint)
        {
            var targets = new List<Line>();
            foreach (var line in duel.Lines)
            {
                if (line.Owner != player || !line.IsActive) continue;
                if (line.Type == LineType.Making || line.Type == LineType.Warding || line.Type == LineType.Forbiddance)
                    targets.Add(line);
            }

            hint = "Pick a line to add to.";
            var target = PickTarget(targets);
            if (target == null) return null;

            var config = duel.Config;
            if (target.Type == LineType.Making)
            {
                Choose(ref _buffStat, StatLabels);
                Stepper("Stat buffs", ref _amount, 1, 12);
                GUILayout.Label("Takes " + _amount * config.AddToDrawing.TurnsPerStatBuff + " turn(s).");
                if (!_buffStat.HasValue)
                {
                    hint = "Choose a stat to buff.";
                    return null;
                }
                return PlayerAction.Buff(target.Id, _buffStat.Value, _amount);
            }

            int missing = Mathf.Max(1, target.MaxDurability - target.Durability);
            Stepper("Durability to repair", ref _amount, 1, missing);
            GUILayout.Label("Takes " + _amount * config.AddToDrawing.TurnsPerDurabilityPoint + " turn(s).");
            return PlayerAction.Repair(target.Id, _amount);
        }

        private PlayerAction DrawDismiss(Duel duel, Player player, out string hint)
        {
            var targets = new List<Line>();
            foreach (var line in duel.Lines)
                if (line.Owner == player && line.IsActive && line.Type == LineType.Forbiddance) targets.Add(line);

            hint = "Pick a Line of Forbiddance to dismiss.";
            var target = PickTarget(targets);
            if (target == null) return null;
            GUILayout.Label("Takes " + duel.Config.Forbiddance.DismissTurns + " turn(s).");
            return PlayerAction.Dismiss(target.Id);
        }

        /// <summary>A list of lines to pick one from. Nothing is picked to begin with.</summary>
        private Line PickTarget(List<Line> targets)
        {
            if (targets.Count == 0) return null;
            int index = targets.FindIndex(line => line.Id == _targetLineId);

            var labels = new string[targets.Count];
            for (int i = 0; i < targets.Count; i++)
                labels[i] = TurnNarrator.LineName(targets[i]) + "  " + targets[i].Durability + "/" + targets[i].MaxDurability;
            int chosen = GUILayout.SelectionGrid(index, labels, 1);
            if (chosen != index && chosen >= 0)
            {
                _targetLineId = targets[chosen].Id;
                GUIUtility.ExitGUI();
            }
            return index >= 0 ? targets[index] : null;
        }

        private float SemiMinor(RulesConfig config)
        {
            return _semiMinor > 0f ? _semiMinor : config.Geometry.EllipseSemiMinor;
        }

        private float Length(RulesConfig config)
        {
            return _length > 0f ? _length : config.Geometry.TangentForbiddanceLength;
        }

        /// <summary>Select the bind point nearest the click. With <paramref name="two"/>, alternate between two picks.</summary>
        private bool PickBindPoint(ChalkBoard board, Player player, Vec2 point, bool two)
        {
            var bindPoints = board.BindPointsOf(player);
            int nearest = -1;
            float best = BindPointPickRadius;
            for (int i = 0; i < bindPoints.Count; i++)
            {
                float distance = Vec2.Distance(bindPoints[i], point);
                if (distance < best) { best = distance; nearest = i; }
            }
            if (nearest < 0) return false;

            if (!two || _bindA < 0 || _bindB >= 0)
            {
                _bindA = nearest;
                _bindB = -1;
            }
            else
            {
                _bindB = nearest;
            }
            return true;
        }

        /// <summary>First click sets the start, second the end; a third starts over.</summary>
        private bool PickPoints(Vec2 point)
        {
            if (!_pointA.HasValue || _pointB.HasValue)
            {
                _pointA = point;
                _pointB = null;
            }
            else
            {
                _pointB = point;
            }
            return true;
        }

        private void ResetClicksIfChanged(string clickKey)
        {
            if (clickKey == _clickKey) return;
            _bindA = -1;
            _bindB = -1;
            _pointA = null;
            _pointB = null;
            _clickKey = clickKey;
        }

        private static void Stepper(string label, ref int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180));
            int step = 0;
            if (GUILayout.Button("-", GUILayout.Width(30))) step = -1;
            GUILayout.Label(value.ToString(), GUILayout.Width(30));
            if (GUILayout.Button("+", GUILayout.Width(30))) step = 1;
            GUILayout.EndHorizontal();

            int next = Mathf.Clamp(value + step, min, max);
            bool changed = next != value;
            value = next;
            if (changed && step != 0) GUIUtility.ExitGUI();
        }

        private static void Choose<T>(ref T value, string[] labels) where T : struct, System.Enum
        {
            int index = System.Convert.ToInt32(value);
            int chosen = GUILayout.Toolbar(index, labels);
            if (chosen == index) return;
            value = (T)System.Enum.ToObject(typeof(T), chosen);
            GUIUtility.ExitGUI();
        }

        /// <summary>A row of buttons with nothing selected until one is clicked.</summary>
        private static void Choose<T>(ref T? value, string[] labels) where T : struct, System.Enum
        {
            int index = value.HasValue ? System.Convert.ToInt32(value.Value) : -1;
            int chosen = GUILayout.Toolbar(index, labels);
            if (chosen == index || chosen < 0) return;
            value = (T)System.Enum.ToObject(typeof(T), chosen);
            GUIUtility.ExitGUI();
        }

        private static void Choose(ref int value, string[] labels)
        {
            int chosen = GUILayout.Toolbar(value, labels);
            if (chosen == value || chosen < 0) return;
            value = chosen;
            GUIUtility.ExitGUI();
        }

        private static void Toggle(ref bool value, string label)
        {
            bool next = GUILayout.Toggle(value, label);
            if (next == value) return;
            value = next;
            GUIUtility.ExitGUI();
        }

        private static string ActionLabel(ActionKind kind)
        {
            switch (kind)
            {
                case ActionKind.StartDrawing: return "Start drawing";
                case ActionKind.ContinueDrawing: return "Continue";
                case ActionKind.CancelDrawing: return "Cancel";
                case ActionKind.AddToDrawing: return "Add to drawing";
                case ActionKind.DismissForbiddance: return "Dismiss LoF";
                default: return "Pass";
            }
        }
    }
}
