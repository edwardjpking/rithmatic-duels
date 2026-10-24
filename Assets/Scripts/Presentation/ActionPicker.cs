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
        public Segment? Segment;
        public Vec2? From;
        public Vec2? To;
        public Vec2? Point;
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
    /// on the board. The rules engine and the board decide whether they are legal.
    /// Each player keeps their own picker, so their last choices are remembered between turns.
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
        private static readonly string[] BindPointLabels = { "2 (ellipse)", "4", "6", "9" };
        private static readonly string[] AttachedBindPointLabels = { "2", "4", "6", "9" };
        private static readonly int[] BindPointCounts = { 2, 4, 6, 9 };
        private static readonly string[] ForbiddanceLabels = { "Between 2 bind points", "Tangent at 1", "Free line" };
        private static readonly string[] ChalklingLabels = { "Bound to a bind point", "Free" };

        /// <summary>How close a click must be to a bind point to pick it.</summary>
        private const float BindPointPickRadius = 0.75f;

        private enum ForbiddanceMode { Between, Tangent, Free }
        private enum ChalklingMode { Bound, Free }

        private ActionKind _kind;
        private LineType _lineType;
        private DrawingPace _pace;
        private int _wardChoice = 1;
        private bool _marksCross;
        private ForbiddanceMode _forbiddanceMode;
        private ChalklingMode _chalklingMode;
        private VigorTier _tier;
        private int _durabilityBuffs = 1;
        private int _damageBuffs;
        private int _speedBuffs;
        private int _glyphs;
        private ChalklingStat _modifierStat;
        private int _targetLineId = -1;
        private int _amount = 1;
        private ChalklingStat _buffStat;

        // Placement clicks: up to two bind points or two board points, whichever the mode uses.
        private int _bindA = -1;
        private int _bindB = -1;
        private Vec2? _pointA;
        private Vec2? _pointB;
        private string _clickKey;

        /// <summary>What to draw on the board for the choice in progress. Null when there is nothing to show.</summary>
        public PlacementPreview Preview { get; private set; }

        /// <summary>Draws the menu. Returns the choice once the player locks it in, otherwise null.</summary>
        public LockedChoice Draw(Duel duel, ChalkBoard board, Player player)
        {
            Preview = null;
            var available = duel.AvailableActions(player);
            if (available.Count == 0) return null;
            if (!available.Contains(_kind)) _kind = available[0];

            var labels = new string[available.Count];
            for (int i = 0; i < available.Count; i++) labels[i] = ActionLabel(available[i]);
            int index = available.IndexOf(_kind);
            int chosen = GUILayout.SelectionGrid(index, labels, 3);
            if (chosen != index)
            {
                _kind = available[chosen];
                GUIUtility.ExitGUI();
            }
            GUILayout.Space(8);

            PlayerAction action = null;
            Placement placement = null;
            switch (_kind)
            {
                case ActionKind.StartDrawing:
                    action = DrawStart(duel.Config, board, player, out placement);
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
                    action = DrawAddTo(duel, player);
                    break;
                case ActionKind.DismissForbiddance:
                    action = DrawDismiss(duel, player);
                    break;
                default:
                    GUILayout.Label("Do nothing this turn.");
                    action = PlayerAction.Pass();
                    break;
            }

            GUILayout.FlexibleSpace();
            string problem = action == null ? "Pick a target." : duel.Validate(player, action);
            if (problem == null && action.Kind == ActionKind.StartDrawing)
                problem = board.ValidatePlacement(player, action.Spec, placement);
            if (problem != null) GUILayout.Label(problem);

            GUI.enabled = problem == null;
            bool locked = GUILayout.Button("Lock in", GUILayout.Height(40));
            GUI.enabled = true;
            if (!locked) return null;
            ClearClicks();
            return new LockedChoice { Action = action, Placement = placement };
        }

        /// <summary>A click on the board, in board units. Returns true if it changed the choice.</summary>
        public bool HandleBoardClick(ChalkBoard board, Player player, Vec2 point)
        {
            if (_kind != ActionKind.StartDrawing) return false;
            bool needsMain = board.MainWardOf(player) != null;

            switch (_lineType)
            {
                case LineType.Warding:
                    return needsMain && PickBindPoint(board, player, point, false);
                case LineType.Forbiddance:
                    if (_forbiddanceMode == ForbiddanceMode.Free) return PickPoints(point);
                    return PickBindPoint(board, player, point, _forbiddanceMode == ForbiddanceMode.Between);
                case LineType.Vigor:
                    return PickPoints(point);
                default:
                    if (_chalklingMode == ChalklingMode.Free)
                    {
                        _pointA = point;
                        return true;
                    }
                    return PickBindPoint(board, player, point, false);
            }
        }

        private PlayerAction DrawStart(RulesConfig config, ChalkBoard board, Player player, out Placement placement)
        {
            // Clicks belong to one line type and placement mode; switching either starts placement over.
            string clickKey = _lineType + "/" + _forbiddanceMode + "/" + _chalklingMode + "/" + _marksCross;
            if (clickKey != _clickKey)
            {
                ClearClicks();
                _clickKey = clickKey;
            }
            Choose(ref _lineType, LineTypeLabels);

            var preview = new PlacementPreview { Player = player };
            bool hasMain = board.MainWardOf(player) != null;
            DrawingSpec spec;
            switch (_lineType)
            {
                case LineType.Warding:
                    if (hasMain) Toggle(ref _marksCross, " Mark's Cross (a small brace)");
                    if (hasMain && _marksCross) spec = DrawingSpec.MarksCross(config);
                    else
                    {
                        GUILayout.Label("Bind points:");
                        Choose(ref _wardChoice, hasMain ? AttachedBindPointLabels : BindPointLabels);
                        spec = DrawingSpec.Warding(BindPointCounts[_wardChoice]);
                    }
                    if (!hasMain)
                    {
                        GUILayout.Label("This is your main Line of Warding. It goes in your home spot.");
                        placement = Placement.Default();
                    }
                    else
                    {
                        GUILayout.Label("Click a bind point on your main Line of Warding to attach this circle there.");
                        placement = _bindA >= 0 ? Placement.AtBindPoint(_bindA) : null;
                    }
                    preview.Ward = placement != null ? board.PreviewWard(player, spec, placement) : null;
                    break;

                case LineType.Forbiddance:
                    Choose(ref _forbiddanceMode, ForbiddanceLabels);
                    switch (_forbiddanceMode)
                    {
                        case ForbiddanceMode.Between:
                            GUILayout.Label("Click two bind points on your main Line of Warding.");
                            spec = DrawingSpec.Forbiddance(2);
                            placement = _bindA >= 0 && _bindB >= 0 ? Placement.BetweenBindPoints(_bindA, _bindB) : null;
                            break;
                        case ForbiddanceMode.Tangent:
                            GUILayout.Label("Click a bind point. The line lies along the circle there.");
                            spec = DrawingSpec.Forbiddance(1);
                            placement = _bindA >= 0 ? Placement.TangentAt(_bindA) : null;
                            break;
                        default:
                            GUILayout.Label("Click where the line starts, then where it ends, on your half.");
                            spec = DrawingSpec.Forbiddance(0);
                            placement = _pointA.HasValue && _pointB.HasValue ? Placement.Segment(_pointA.Value, _pointB.Value) : null;
                            preview.From = _pointA;
                            break;
                    }
                    if (placement != null) preview.Segment = board.PreviewForbiddance(player, placement);
                    break;

                case LineType.Vigor:
                    Choose(ref _tier, TierLabels);
                    var tier = config.Vigor.Tier(_tier);
                    GUILayout.Label("Speed " + tier.Speed + ", damage " + tier.Damage + " before its modifier.");
                    GUILayout.Label("Click where to draw it on your half, then click where to aim.");
                    spec = DrawingSpec.Vigor(_tier);
                    placement = _pointA.HasValue && _pointB.HasValue ? Placement.Aimed(_pointA.Value, _pointB.Value) : null;
                    preview.From = _pointA;
                    preview.To = _pointB;
                    break;

                default:
                    Stepper("Durability buffs", ref _durabilityBuffs, 0, 12);
                    Stepper("Damage buffs", ref _damageBuffs, 0, 12);
                    Stepper("Speed buffs", ref _speedBuffs, 0, 12);
                    int maxGlyphs = DrawTimes.MaxGlyphs(_durabilityBuffs + _damageBuffs + _speedBuffs, config);
                    Stepper("Glyphs (max " + maxGlyphs + ")", ref _glyphs, 0, maxGlyphs);
                    GUILayout.Label("A positive modifier goes to:");
                    Choose(ref _modifierStat, StatLabels);
                    Choose(ref _chalklingMode, ChalklingLabels);
                    bool bound = _chalklingMode == ChalklingMode.Bound;
                    GUILayout.Label(bound ? "Click a bind point to tether it there." : "Click where to draw it on your half.");
                    GUILayout.Label("Chalklings stand still until chalkling behaviour is built.");
                    spec = DrawingSpec.Making(_durabilityBuffs, _damageBuffs, _speedBuffs, _glyphs, bound ? 1 : 0, _modifierStat);
                    placement = bound
                        ? (_bindA >= 0 ? Placement.AtBindPoint(_bindA) : null)
                        : (_pointA.HasValue ? Placement.At(_pointA.Value) : null);
                    preview.Point = bound ? null : _pointA;
                    break;
            }
            if (_bindA >= 0) preview.BindPoints.Add(_bindA);
            if (_bindB >= 0) preview.BindPoints.Add(_bindB);
            Preview = preview;

            GUILayout.Space(8);
            Choose(ref _pace, PaceLabels);
            if (DrawTimes.Validate(spec, config) == null)
            {
                int baseTurns = DrawTimes.BaseTurns(spec, config);
                bool instant = _pace == DrawingPace.Rush && config.RushedOneTurnDrawingsCompleteImmediately && baseTurns == 1;
                GUILayout.Label(instant
                    ? "Completes this tick."
                    : "Takes " + DrawTimes.Turns(spec, _pace, config) + " more turn(s) of Continue drawing after this one.");
            }
            if (placement == null) GUILayout.Label("Finish placing it on the board.");
            return PlayerAction.Start(spec, _pace);
        }

        private PlayerAction DrawAddTo(Duel duel, Player player)
        {
            var targets = new List<Line>();
            foreach (var line in duel.Lines)
            {
                if (line.Owner != player || !line.IsActive) continue;
                if (line.Type == LineType.Making || line.Type == LineType.Warding || line.Type == LineType.Forbiddance)
                    targets.Add(line);
            }

            var target = PickTarget(targets);
            if (target == null) return null;

            var config = duel.Config;
            if (target.Type == LineType.Making)
            {
                Choose(ref _buffStat, StatLabels);
                Stepper("Stat buffs", ref _amount, 1, 12);
                GUILayout.Label("Takes " + _amount * config.AddToDrawing.TurnsPerStatBuff + " turn(s).");
                return PlayerAction.Buff(target.Id, _buffStat, _amount);
            }

            int missing = Mathf.Max(1, target.MaxDurability - target.Durability);
            Stepper("Durability to repair", ref _amount, 1, missing);
            GUILayout.Label("Takes " + _amount * config.AddToDrawing.TurnsPerDurabilityPoint + " turn(s).");
            return PlayerAction.Repair(target.Id, _amount);
        }

        private PlayerAction DrawDismiss(Duel duel, Player player)
        {
            var targets = new List<Line>();
            foreach (var line in duel.Lines)
                if (line.Owner == player && line.IsActive && line.Type == LineType.Forbiddance) targets.Add(line);

            var target = PickTarget(targets);
            if (target == null) return null;
            GUILayout.Label("Takes " + duel.Config.Forbiddance.DismissTurns + " turn(s).");
            return PlayerAction.Dismiss(target.Id);
        }

        private Line PickTarget(List<Line> targets)
        {
            if (targets.Count == 0) return null;
            int index = targets.FindIndex(line => line.Id == _targetLineId);
            if (index < 0) index = 0;

            var labels = new string[targets.Count];
            for (int i = 0; i < targets.Count; i++)
                labels[i] = TurnNarrator.LineName(targets[i]) + "  " + targets[i].Durability + "/" + targets[i].MaxDurability;
            int chosen = GUILayout.SelectionGrid(index, labels, 1);
            _targetLineId = targets[chosen].Id;
            if (chosen != index) GUIUtility.ExitGUI();
            return targets[chosen];
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

        private void ClearClicks()
        {
            _bindA = -1;
            _bindB = -1;
            _pointA = null;
            _pointB = null;
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

        private static void Choose(ref int value, string[] labels)
        {
            int chosen = GUILayout.Toolbar(value, labels);
            if (chosen == value) return;
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
