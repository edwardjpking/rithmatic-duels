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
        /// <summary>A LoW the player has placed (drawn solid when <see cref="WardChosen"/>, dashed otherwise).</summary>
        public WardShape Ward;
        public bool WardChosen;
        /// <summary>Where a main LoW would go if the player clicked now: follows the mouse, dashed.</summary>
        public WardShape GhostWard;
        /// <summary>Bind points the previewed LoW would have.</summary>
        public IReadOnlyList<Vec2> WardBindPoints;
        /// <summary>A previewed Mark's Cross's crossed LoF.</summary>
        public IReadOnlyList<Segment> Braces;
        /// <summary>Bind points that can't be picked for this drawing, filled in red.</summary>
        public IReadOnlyList<Vec2> BlockedBindPoints;
        /// <summary>Preset spots for the main LoW, drawn as markers that can be clicked.</summary>
        public IReadOnlyList<Vec2> Presets;
        /// <summary>Show the midline of the player's half, which the main LoW snaps to.</summary>
        public bool ShowMidline;
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
    /// and the scroll wheel on the board. The rules engine and the board decide whether they are legal,
    /// and the menu greys out or snaps away anything they would refuse, so rules show rather than tell.
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
        private static readonly string[] GlyphLabels = { "Attack glyph", "Defend glyph" };
        private static readonly string[] ShapeLabels = { "Circle", "Ellipse" };
        private static readonly string[] CircleBindPointLabels = { "4", "6", "9" };
        private static readonly int[] CircleBindPointCounts = { 4, 6, 9 };
        // A LoW added during the duel: a Mark's Cross, or a LoW with this many bind points (0 means Mark's Cross).
        private static readonly string[] AttachedWardLabels = { "Mark's Cross", "2 (ellipse)", "4", "6", "9" };
        private static readonly int[] AttachedWardBindPoints = { 0, 2, 4, 6, 9 };
        private static readonly string[] ForbiddanceLabels = { "Between 2 bind points", "Tangent at 1", "Free line" };
        private static readonly string[] ChalklingLabels = { "Bound to a bind point", "Free" };

        /// <summary>How close a click must be to a bind point, or a LoF to dismiss, to pick it.</summary>
        private const float PickRadius = 0.75f;

        private enum MainShape { Circle, Ellipse }
        private enum ForbiddanceMode { Between, Tangent, Free }
        private enum ChalklingMode { Bound, Free }

        // Every choice starts unselected (null or -1). Pace defaults to Normal.
        private ActionKind? _kind;
        private bool _defaultChosen;
        private ActionKind? _afterCancel;
        private LineType? _lineType;
        private DrawingPace _pace;
        private MainShape? _mainShape;
        private int _bindPointChoice = -1;
        private int _attachedWardChoice = -1;
        private ForbiddanceMode? _forbiddanceMode;
        private ChalklingMode? _chalklingMode;
        private VigorTier? _tier;
        private int _durabilityBuffs;
        private int _damageBuffs;
        private int _speedBuffs;
        private Glyph? _glyph;
        private ChalklingStat? _modifierStat;
        private int _targetLineId = -1;
        private int _amount = 1;

        // What the player has typed into each chalkling stat box (see StatEntry).
        private readonly TypedStat _durabilityEntry = new TypedStat();
        private readonly TypedStat _damageEntry = new TypedStat();
        private readonly TypedStat _speedEntry = new TypedStat();

        // Sizes set with the scroll wheel. Negative means "not touched yet", so the config default is used.
        private float _semiMinor = -1f;
        private float _length = -1f;
        private float _attachedSize = -1f;

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

        /// <summary>The action being built: after Cancel, the one chosen to follow it.</summary>
        private ActionKind? Effective { get { return _kind == ActionKind.CancelDrawing ? _afterCancel : _kind; } }

        /// <summary>Draws the menu. Returns the choice once the player locks it in, otherwise null.</summary>
        public LockedChoice Draw(Duel duel, ChalkBoard board, Player player)
        {
            Preview = null;
            var available = duel.AvailableActions(player);
            if (available.Count == 0) return null;
            if (_kind.HasValue && !available.Contains(_kind.Value)) _kind = null;
            // Mid-drawing, carrying on is the usual choice, so it starts selected.
            if (!_kind.HasValue && !_defaultChosen && available.Contains(ActionKind.ContinueDrawing)) _kind = ActionKind.ContinueDrawing;
            _defaultChosen = true;

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

            bool cancelling = _kind == ActionKind.CancelDrawing;
            if (cancelling)
            {
                GUILayout.Label("Abandon your current work (all progress on it is lost), then this turn:");
                var after = ActionsAfterCancel(duel, board, player);
                var labels = new string[after.Count];
                for (int i = 0; i < after.Count; i++) labels[i] = ActionLabel(after[i]);
                int index = _afterCancel.HasValue ? after.IndexOf(_afterCancel.Value) : -1;
                int chosen = GUILayout.Toolbar(index, labels);
                if (chosen != index && chosen >= 0)
                {
                    _afterCancel = after[chosen];
                    GUIUtility.ExitGUI();
                }
                GUILayout.Space(8);
            }

            PlayerAction action = null;
            Placement placement = null;
            string hint = cancelling ? "Choose what to do instead." : "Choose an action.";
            // After a cancel the main LoW may need starting again, so check what the board has, not the menu.
            bool needsMain = board.MainWardOf(player) == null && duel.Config.MainWardFirst;
            switch (Effective)
            {
                case ActionKind.StartDrawing:
                    action = needsMain
                        ? DrawMainWardSetup(duel.Config, board, player, out placement, out hint)
                        : DrawStart(duel.Config, board, player, out placement, out hint);
                    break;
                case ActionKind.ContinueDrawing:
                    GUILayout.Label("Keep going. " + duel.GetPlayer(player).Work.TurnsRemaining + " turn(s) left, including this one.");
                    action = PlayerAction.Continue();
                    break;
                case ActionKind.DismissForbiddance:
                    action = DrawDismiss(duel, board, player, out hint);
                    break;
                case ActionKind.DrawBindPoints:
                    action = DrawBindPointsMenu(duel, board, player, out hint);
                    break;
                case ActionKind.Pass:
                    if (!cancelling) GUILayout.Label("Do nothing this turn.");
                    action = PlayerAction.Pass();
                    break;
            }
            if (cancelling && action != null)
                action = action.Kind == ActionKind.Pass ? PlayerAction.Cancel() : action.AfterCancelling();

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

        /// <summary>What can follow a cancel: the menu as it would be with nothing in progress.</summary>
        private static List<ActionKind> ActionsAfterCancel(Duel duel, ChalkBoard board, Player player)
        {
            if (duel.GetPlayer(player).NeedsBindPoints) return new List<ActionKind> { ActionKind.DrawBindPoints, ActionKind.Pass };
            var after = new List<ActionKind> { ActionKind.StartDrawing };
            if (board.MainWardOf(player) == null && duel.Config.MainWardFirst) return after;
            bool dismissable = false;
            foreach (var line in duel.Lines)
            {
                if (line.Owner != player || !line.IsActive) continue;
                if (line.Type == LineType.Forbiddance) dismissable = true;
            }
            if (dismissable) after.Add(ActionKind.DismissForbiddance);
            after.Add(ActionKind.Pass);
            return after;
        }

        /// <summary>A click on the board, in board units. Returns true if it changed the choice.</summary>
        public bool HandleBoardClick(ChalkBoard board, Player player, RulesConfig config, Vec2 point)
        {
            if (Effective == ActionKind.DismissForbiddance)
            {
                var forbiddance = board.ForbiddanceAt(player, point, PickRadius);
                if (forbiddance == null) return false;
                _targetLineId = forbiddance.LineId;
                return true;
            }
            if (Effective != ActionKind.StartDrawing) return false;

            if (board.MainWardOf(player) == null && config.MainWardFirst)
            {
                if (!_mainShape.HasValue || config.Geometry.MainWardAtHome) return false;
                _pointA = board.SnapMainWard(player, MainShapeSpec(), point, MainSemiMinor(board, player));
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
        /// The scroll wheel over the board: makes a LoW ellipse flatter or rounder, an attached LoW circle
        /// bigger or smaller, or a LoF not bound at two points shorter or longer. Returns true if it changed the choice.
        /// </summary>
        public bool HandleScroll(ChalkBoard board, Player player, RulesConfig config, float delta)
        {
            if (Effective != ActionKind.StartDrawing || Mathf.Abs(delta) < 1e-6f) return false;
            var geometry = config.Geometry;
            // Scrolling up (negative delta) makes things bigger.
            float step = delta < 0f ? geometry.ScrollStep : -geometry.ScrollStep;

            if (board.MainWardOf(player) == null && config.MainWardFirst)
            {
                if (_mainShape != MainShape.Ellipse) return false;
                // Each notch moves to the next whole-number durability of the ellipse's weakest point.
                _semiMinor = StepAlong(MainSteps(board, player), SemiMinor(board, player), delta < 0f ? 1 : -1);
                return true;
            }

            if (_lineType == LineType.Warding && _attachedWardChoice > 0)
            {
                var spec = DrawingSpec.Warding(AttachedWardBindPoints[_attachedWardChoice]);
                // Each notch moves to the next whole-number durability, never bigger than fits on the player's half.
                _attachedSize = StepAlong(AttachedSteps(board, player, spec), AttachedSize(board, player, spec), delta < 0f ? 1 : -1);
                return true;
            }

            if (_lineType == LineType.Forbiddance && (_forbiddanceMode == ForbiddanceMode.Tangent || _forbiddanceMode == ForbiddanceMode.Free))
            {
                _length = Mathf.Clamp(FittedLength(config, board, player) + step, geometry.MinForbiddanceLength, MaxLength(config, board, player));
                return true;
            }
            return false;
        }

        /// <summary>
        /// The first thing each player does (DESIGN.md section 2): choose the main LoW's shape, then its
        /// bind points. It goes at the player's home spot. (With GeometryConfig.MainWardAtHome off, the
        /// player places it too: the outline follows the mouse but never leaves their half, and snaps to
        /// the preset spots and the midline.)
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
            if (ellipse) GUILayout.Label("Scroll over the board to make it flatter or rounder. Flatter ellipses are weaker along their sides.");
            bool atHome = config.Geometry.MainWardAtHome;
            if (atHome) _pointA = board.Home(player);
            else GUILayout.Label("2. Click on your half of the board to place it.");

            GUILayout.Label((atHome ? "2" : "3") + ". Bind points:");
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

            var shapeSpec = MainShapeSpec();
            float semiMinor = MainSemiMinor(board, player);
            // A shape or size change can push a placed outline off its half, so keep it inside.
            if (_pointA.HasValue) _pointA = board.ClampMainWard(player, shapeSpec, _pointA.Value, semiMinor);

            var preview = new PlacementPreview
            {
                Player = player,
                Presets = atHome ? null : board.MainWardPresets(player, shapeSpec, semiMinor),
                ShowMidline = !atHome,
            };
            if (_hover.HasValue && !atHome)
                preview.GhostWard = board.PreviewWard(player, shapeSpec, Placement.MainWard(board.SnapMainWard(player, shapeSpec, _hover.Value, semiMinor), semiMinor));
            if (_pointA.HasValue)
            {
                IReadOnlyList<Vec2> points;
                preview.Ward = board.PreviewWard(player, bindPoints > 0 ? DrawingSpec.Warding(bindPoints) : shapeSpec, Placement.MainWard(_pointA.Value, semiMinor), out points);
                preview.WardChosen = true;
                if (bindPoints > 0) preview.WardBindPoints = points;
                GUILayout.Label("Weakest point: " + board.WeakestDurability(preview.Ward, true) + " durability (the main LoW has double).");
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
            placement = Placement.MainWard(_pointA.Value, semiMinor);
            GUILayout.Space(8);
            _pace = DrawingPace.Normal; // the main LoW can't be rushed or steadied
            GUILayout.Label("Both outlines are drawn as soon as both players lock in. " + BindPointTimingText(config, bindPoints)
                + " Nothing else can be drawn until they are done.");
            hint = null;
            return PlayerAction.Start(spec, _pace);
        }

        private DrawingSpec MainShapeSpec()
        {
            return DrawingSpec.Warding(_mainShape == MainShape.Ellipse ? 2 : 4);
        }

        private float MainSemiMinor(ChalkBoard board, Player player)
        {
            return _mainShape == MainShape.Ellipse ? SemiMinor(board, player) : 0f;
        }

        private PlayerAction DrawStart(RulesConfig config, ChalkBoard board, Player player, out Placement placement, out string hint)
        {
            // Clicks belong to one line type and placement mode; switching either starts placement over.
            ResetClicksIfChanged(_lineType + "/" + _forbiddanceMode + "/" + _chalklingMode + "/" + _attachedWardChoice);
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
                    GUILayout.Label("Bind points:");
                    int before = _attachedWardChoice;
                    Choose(ref _attachedWardChoice, AttachedWardLabels);
                    if (_attachedWardChoice != before) _attachedSize = -1f;
                    if (_attachedWardChoice < 0)
                    {
                        hint = "Choose a Mark's Cross or how many bind points.";
                        break;
                    }
                    int count = AttachedWardBindPoints[_attachedWardChoice];
                    if (count == 0)
                    {
                        GUILayout.Label("A Mark's Cross: a small circle braced by two crossed Lines of Forbiddance.");
                        spec = DrawingSpec.MarksCross(config);
                    }
                    else
                    {
                        spec = DrawingSpec.Warding(count);
                        GUILayout.Label(count == 2
                            ? "An ellipse sticking out from the bind point. Scroll to make it rounder or flatter."
                            : "A circle outside the bind point. Scroll to make it bigger or smaller.");
                    }
                    GUILayout.Label("Click a bind point on your main Line of Warding to attach it there.");
                    if (_bindA >= 0)
                    {
                        placement = Placement.AtBindPoint(_bindA, count == 0 ? 0f : AttachedSize(board, player, spec));
                        IReadOnlyList<Vec2> points;
                        preview.Ward = board.PreviewWard(player, spec, placement, out points);
                        preview.WardBindPoints = points;
                        if (preview.Ward != null && count == 0) preview.Braces = ChalkBoard.MarksCrossBraces(preview.Ward, points);
                        if (preview.Ward != null) GUILayout.Label("Weakest point: " + board.WeakestDurability(preview.Ward) + " durability, before its modifier.");
                    }
                    preview.BlockedBindPoints = BlockedBindPoints(board, player);
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
                            GUILayout.Label("Click a bind point. The line lies along the circle there. Scroll to change its length.");
                            spec = DrawingSpec.Forbiddance(1);
                            if (_bindA >= 0) placement = Placement.TangentAt(_bindA, FittedLength(config, board, player));
                            break;
                        case ForbiddanceMode.Free:
                            GUILayout.Label("Click where the line starts, then click to set its direction. Scroll to change its length.");
                            spec = DrawingSpec.Forbiddance(0);
                            Vec2? toward = _pointB ?? _hover;
                            if (_pointA.HasValue && toward.HasValue && Vec2.Distance(_pointA.Value, toward.Value) > 1e-3f)
                            {
                                Vec2 end = _pointA.Value + (toward.Value - _pointA.Value).Normalized * FittedLength(config, board, player);
                                preview.Segment = new Segment(_pointA.Value, end);
                                if (_pointB.HasValue) placement = Placement.Segment(_pointA.Value, end);
                            }
                            preview.From = _pointA;
                            break;
                        default:
                            hint = "Choose how to place it.";
                            break;
                    }
                    if (_forbiddanceMode == ForbiddanceMode.Between || _forbiddanceMode == ForbiddanceMode.Tangent)
                        preview.BlockedBindPoints = BlockedBindPoints(board, player);
                    if (placement != null) preview.Segment = board.PreviewForbiddance(player, placement);
                    if (spec != null && placement != null)
                        GUILayout.Label("Length " + board.Measure(player, spec, placement).Length.ToString("0.0")
                            + ": longer lines take longer to draw.");
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
                    spec = DrawMakingPointBuy(config, out hint);
                    if (spec == null) break;
                    bool bound = _chalklingMode == ChalklingMode.Bound;
                    GUILayout.Label(bound ? "Click a bind point to tether it there." : "Click where to draw it on your half.");
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
                spec = board.Measure(player, spec, placement);
                if (spec.TravelTurns > 0)
                    GUILayout.Label("This is " + board.TravelDistance(player, placement).ToString("0.0")
                        + " units from your main Line of Warding: +" + spec.TravelTurns + " turn(s) to get there.");
            }

            GUILayout.Space(8);
            Choose(ref _pace, PaceLabels);
            DrawTiming(config, spec);
            return PlayerAction.Start(spec, _pace);
        }

        /// <summary>Redrawing the main LoW's bind points after a cancel. The count is offered as the shape allows.</summary>
        private PlayerAction DrawBindPointsMenu(Duel duel, ChalkBoard board, Player player, out string hint)
        {
            var choices = board.BindPointChoices(player);
            var labels = new string[choices.Length];
            for (int i = 0; i < choices.Length; i++) labels[i] = choices[i].ToString();
            if (choices.Length == 1) _bindPointChoice = 0;
            GUILayout.Label("Bind points:");
            Choose(ref _bindPointChoice, labels);
            if (_bindPointChoice < 0 || _bindPointChoice >= choices.Length)
            {
                hint = "Choose how many bind points to draw.";
                return null;
            }
            int count = choices[_bindPointChoice];
            Preview = new PlacementPreview { Player = player, WardBindPoints = board.PreviewBindPoints(player, count) };
            GUILayout.Label(BindPointTimingText(duel.Config, count) + " Nothing else can be drawn until they are done.");
            hint = null;
            return PlayerAction.DrawBindPoints(count);
        }

        private static string BindPointTimingText(RulesConfig config, int count)
        {
            int total = DrawTimes.BindPointTurns(count, config);
            int more = total - (config.StartActionCountsAsProgress ? 1 : 0);
            return more <= 0
                ? "The bind points are done this turn."
                : "The bind points take " + total + " turn(s): this one and " + more + " more of Continue.";
        }

        /// <summary>The chalkling point buy: each stat shows its value, and buttons that can't be used are greyed out.</summary>
        private DrawingSpec DrawMakingPointBuy(RulesConfig config, out string hint)
        {
            var making = config.Making;
            GUILayout.Label("Spend points on stats. Every " + making.StatBuffsPerDrawTurn + " points take a turn to draw.");
            GUILayout.Label("Type the stat you want, or use - and +.");
            StatEntry("Durability", _durabilityEntry, ref _durabilityBuffs, making.BaseDurability, making.DurabilityPerBuff);
            StatEntry("Damage", _damageEntry, ref _damageBuffs, making.BaseDamage, making.DamagePerBuff);
            StatEntry("Speed", _speedEntry, ref _speedBuffs, making.BaseSpeed, making.SpeedPerBuff);
            GUILayout.Label("A positive modifier goes to:");
            Choose(ref _modifierStat, StatLabels);
            Choose(ref _chalklingMode, ChalklingLabels);
            // One glyph, Attack or Defend; a bound chalkling must Defend.
            bool bound = _chalklingMode == ChalklingMode.Bound;
            if (bound) GUILayout.Label("Bound, so it carries the Defend glyph.");
            else if (_chalklingMode.HasValue) Choose(ref _glyph, GlyphLabels);
            GUILayout.Label("Chalklings stand still until chalkling behaviour is built.");
            if (!_modifierStat.HasValue || !_chalklingMode.HasValue || (!bound && !_glyph.HasValue))
            {
                hint = !_modifierStat.HasValue ? "Choose where a positive modifier goes."
                    : !_chalklingMode.HasValue ? "Choose whether it is bound." : "Choose its glyph.";
                return null;
            }
            hint = null;
            var glyph = bound ? Glyph.Defend : _glyph.Value;
            return DrawingSpec.Making(_durabilityBuffs, _damageBuffs, _speedBuffs, glyph, bound ? 1 : 0, _modifierStat.Value);
        }

        private void DrawTiming(RulesConfig config, DrawingSpec spec)
        {
            if (DrawTimes.Validate(spec, config) != null) return;
            GUILayout.Label(TimingText(config, spec, _pace));
        }

        private static string TimingText(RulesConfig config, DrawingSpec spec, DrawingPace pace)
        {
            int total = DrawTimes.Turns(spec, pace, config);
            bool instant = pace == DrawingPace.Rush && config.RushedOneTurnDrawingsCompleteImmediately
                && DrawTimes.BaseTurns(spec, config) == 1 && spec.TravelTurns == 0;
            int more = instant ? 0 : total - (config.StartActionCountsAsProgress ? 1 : 0);
            string text = more <= 0 ? "Completes this turn." : "Takes " + total + " turn(s): this one and " + more + " more of Continue drawing.";
            if (pace != DrawingPace.Rush) return text;

            // Rushing a drawing that takes 1 turn anyway saves no time; its only gain is finishing before lines move (DESIGN.md section 3).
            int normal = DrawTimes.Turns(spec, DrawingPace.Normal, config);
            if (normal <= config.RushedEarlyMaxNormalTurns)
                return text + " Rushing saves no time here. Its only benefit is that the line goes down before lines move this turn"
                    + (spec.Type == LineType.Vigor ? ", so it flies straight away" : "") + ".";
            return text + " Rushing saves " + (normal - total) + " turn(s).";
        }

        private PlayerAction DrawDismiss(Duel duel, ChalkBoard board, Player player, out string hint)
        {
            var targets = new List<Line>();
            foreach (var line in duel.Lines)
                if (line.Owner == player && line.IsActive && line.Type == LineType.Forbiddance) targets.Add(line);

            hint = "Click one of your Lines of Forbiddance on the board, or pick it here.";
            GUILayout.Label("Click the Line of Forbiddance to dismiss.");
            var target = PickTarget(targets);
            if (target == null) return null;

            var body = board.BodyOf(target.Id) as ForbiddanceBody;
            if (body != null) Preview = new PlacementPreview { Player = player, Segment = body.Segment };
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

        /// <summary>The main ellipse's chosen semi-minor axis, on one of its whole-number durability steps.</summary>
        private float SemiMinor(ChalkBoard board, Player player)
        {
            return Nearest(MainSteps(board, player), _semiMinor > 0f ? _semiMinor : board.Config.Geometry.EllipseSemiMinor);
        }

        // Size steps are slow to work out, so each is kept until what it depends on changes.
        private List<float> _mainSteps;
        private List<float> _attachedSteps;
        private string _attachedStepsKey;
        private bool[] _blocked;
        private string _blockedKey;

        private List<float> MainSteps(ChalkBoard board, Player player)
        {
            if (_mainSteps == null) _mainSteps = board.MainEllipseSizes(player);
            return _mainSteps;
        }

        private List<float> AttachedSteps(ChalkBoard board, Player player, DrawingSpec spec)
        {
            string key = spec.BindPoints + "/" + _bindA + "/" + board.BindPointsOf(player).Count;
            if (_attachedSteps == null || key != _attachedStepsKey)
            {
                _attachedSteps = board.AttachedSizes(player, spec, _bindA);
                _attachedStepsKey = key;
            }
            return _attachedSteps;
        }

        private static float Nearest(List<float> steps, float value)
        {
            float best = steps[0];
            foreach (float step in steps) if (Mathf.Abs(step - value) < Mathf.Abs(best - value)) best = step;
            return best;
        }

        /// <summary>The step after (direction 1) or before (-1) the one nearest the value, stopping at the ends.</summary>
        private static float StepAlong(List<float> steps, float value, int direction)
        {
            int index = steps.IndexOf(Nearest(steps, value)) + direction;
            return steps[index < 0 ? 0 : index >= steps.Count ? steps.Count - 1 : index];
        }

        private float Length(RulesConfig config)
        {
            return _length > 0f ? _length : config.Geometry.TangentForbiddanceLength;
        }

        /// <summary>The longest a tangent or free LoF being placed can be and stay on the player's half.</summary>
        private float MaxLength(RulesConfig config, ChalkBoard board, Player player)
        {
            if (_forbiddanceMode == ForbiddanceMode.Tangent && _bindA >= 0)
                return board.MaxForbiddanceLength(player, Placement.TangentAt(_bindA), Vec2.Zero);
            Vec2? toward = _pointB ?? _hover;
            if (_forbiddanceMode == ForbiddanceMode.Free && _pointA.HasValue && toward.HasValue && Vec2.Distance(_pointA.Value, toward.Value) > 1e-3f)
                return board.MaxForbiddanceLength(player, Placement.Segment(_pointA.Value, _pointA.Value), toward.Value - _pointA.Value);
            return config.Geometry.MaxForbiddanceLength;
        }

        /// <summary>The chosen LoF length, cut back if it would reach off the player's half.</summary>
        private float FittedLength(RulesConfig config, ChalkBoard board, Player player)
        {
            return Mathf.Clamp(Length(config), config.Geometry.MinForbiddanceLength, MaxLength(config, board, player));
        }

        /// <summary>
        /// The chosen size of an attached LoW, on one of its whole-number durability steps and cut back if it
        /// would reach off the player's half.
        /// </summary>
        private float AttachedSize(ChalkBoard board, Player player, DrawingSpec spec)
        {
            return Nearest(AttachedSteps(board, player, spec), board.AttachedSize(spec, Placement.AtBindPoint(0, _attachedSize)));
        }

        /// <summary>Select the bind point nearest the click. With <paramref name="two"/>, alternate between two picks.</summary>
        private bool PickBindPoint(ChalkBoard board, Player player, Vec2 point, bool two)
        {
            var bindPoints = board.BindPointsOf(player);
            int nearest = -1;
            float best = PickRadius;
            for (int i = 0; i < bindPoints.Count; i++)
            {
                // Bind points filled in red can't be picked.
                if (Blocked(board, player)[i]) continue;
                float distance = Vec2.Distance(bindPoints[i], point);
                if (distance < best) { best = distance; nearest = i; }
            }
            if (nearest < 0) return false;

            if (two && nearest == _bindA && _bindB < 0)
            {
                // Clicking the first pick again lets go of it.
                _bindA = -1;
            }
            else if (!two || _bindA < 0 || _bindB >= 0)
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

        /// <summary>The bind points that can't be picked right now (see <see cref="BindPointBlocked"/>).</summary>
        private List<Vec2> BlockedBindPoints(ChalkBoard board, Player player)
        {
            var blocked = new List<Vec2>();
            var points = board.BindPointsOf(player);
            var isBlocked = Blocked(board, player);
            for (int i = 0; i < points.Count; i++) if (isBlocked[i]) blocked.Add(points[i]);
            return blocked;
        }

        /// <summary>
        /// <see cref="BindPointBlocked"/> for every bind point, worked out again only when the choice changes,
        /// since checking every pair of points each frame is slow. The board can't change while choosing,
        /// because each turn starts with a fresh picker.
        /// </summary>
        private bool[] Blocked(ChalkBoard board, Player player)
        {
            int count = board.BindPointsOf(player).Count;
            string key = _lineType + "/" + _forbiddanceMode + "/" + _bindA + "/" + _bindB + "/" + count;
            if (_blocked == null || key != _blockedKey)
            {
                _blocked = new bool[count];
                for (int i = 0; i < count; i++) _blocked[i] = BindPointBlocked(board, player, i);
                _blockedKey = key;
            }
            return _blocked;
        }

        /// <summary>
        /// True if this bind point can't be picked for the drawing being placed, so it is filled in red and
        /// clicks on it are ignored:
        /// a LoW can't bind at a point that already has its LoW;
        /// a tangent LoF can't go where even the shortest one wouldn't fit (it would cross a LoW or leave your half);
        /// a LoF between two bind points can't join two points whose line wouldn't fit. Before the first pick, a
        /// point is blocked if no other point can join it; after it, a point is blocked if it can't join the first.
        /// </summary>
        private bool BindPointBlocked(ChalkBoard board, Player player, int index)
        {
            switch (_lineType)
            {
                case LineType.Warding:
                    return board.BindPointFull(player, index);
                case LineType.Forbiddance:
                    if (_forbiddanceMode == ForbiddanceMode.Tangent)
                        return !ForbiddanceFits(board, player, DrawingSpec.Forbiddance(1),
                            Placement.TangentAt(index, board.Config.Geometry.MinForbiddanceLength));
                    if (_forbiddanceMode != ForbiddanceMode.Between) return false;
                    var spec = DrawingSpec.Forbiddance(2);
                    if (_bindA >= 0 && _bindB < 0)
                        return index != _bindA && !ForbiddanceFits(board, player, spec, Placement.BetweenBindPoints(_bindA, index));
                    int count = board.BindPointsOf(player).Count;
                    for (int other = 0; other < count; other++)
                        if (other != index && ForbiddanceFits(board, player, spec, Placement.BetweenBindPoints(index, other))) return false;
                    return true;
                default:
                    return false;
            }
        }

        private static bool ForbiddanceFits(ChalkBoard board, Player player, DrawingSpec spec, Placement placement)
        {
            return board.ValidatePlacement(player, board.Measure(player, spec, placement), placement) == null;
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

        /// <summary>A label with - and + buttons. A button that would go past the limits is greyed out.</summary>
        /// <summary>The most buffs one chalkling stat can take in the point buy.</summary>
        private const int MaxStatBuffs = 12;

        /// <summary>The text in one stat box, kept between frames so a half-typed number isn't wiped.</summary>
        private sealed class TypedStat
        {
            public string Text = "";
            // The buffs the box last showed, so a change from the - and + buttons refreshes it.
            public int ShownBuffs = -1;
        }

        /// <summary>
        /// One chalkling stat: a box the player can type the stat they want into, and - and + buttons.
        /// Both go through the same rule: the stat is base + buffs × perBuff, with 0 to MaxStatBuffs buffs.
        /// A typed number moves to the nearest value the point buy can make (for example 4 becomes 5 when
        /// durability goes 1, 3, 5...), and the box shows that value once the player presses Enter or clicks away.
        /// </summary>
        private static void StatEntry(string name, TypedStat entry, ref int buffs, int baseValue, int perBuff)
        {
            int value = baseValue + buffs * perBuff;
            string control = "stat-" + name;
            bool focused = GUI.GetNameOfFocusedControl() == control;
            if (focused && Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                GUI.FocusControl(null);
                focused = false;
                Event.current.Use();
            }
            // Show the real value unless the player is typing, or the buttons changed it since they typed.
            if (!focused || entry.ShownBuffs != buffs)
            {
                entry.Text = value.ToString();
                entry.ShownBuffs = buffs;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(name, GUILayout.Width(100));
            GUI.SetNextControlName(control);
            string typed = GUILayout.TextField(entry.Text, 3, GUILayout.Width(50));
            if (typed != entry.Text)
            {
                entry.Text = FilterDigits(typed);
                int wanted;
                if (int.TryParse(entry.Text, out wanted))
                {
                    buffs = BuffsFor(wanted, baseValue, perBuff, 0, MaxStatBuffs);
                    entry.ShownBuffs = buffs;
                }
            }
            string note = perBuff > 1 ? "steps of " + perBuff : "";
            GUILayout.Label(note, GUILayout.Width(70));
            int step = 0;
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && buffs > 0;
            if (GUILayout.Button("-", GUILayout.Width(30))) step = -1;
            GUI.enabled = enabled && buffs < MaxStatBuffs;
            if (GUILayout.Button("+", GUILayout.Width(30))) step = 1;
            GUI.enabled = enabled;
            GUILayout.EndHorizontal();

            int next = Mathf.Clamp(buffs + step, 0, MaxStatBuffs);
            bool changed = next != buffs;
            buffs = next;
            if (changed) GUIUtility.ExitGUI();
        }

        /// <summary>
        /// The number of buffs whose stat (base + buffs × perBuff) is nearest the wanted value, kept between
        /// min and max. A tie goes to the higher value.
        /// </summary>
        private static int BuffsFor(int wanted, int baseValue, int perBuff, int min, int max)
        {
            if (perBuff <= 0) return min;
            int above = wanted - baseValue;
            int buffs = (above * 2 + perBuff) / (perBuff * 2);
            if (above < 0) buffs = min;
            return Mathf.Clamp(buffs, min, max);
        }

        private static string FilterDigits(string text)
        {
            var kept = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (c >= '0' && c <= '9') kept.Append(c);
            }
            return kept.ToString();
        }

        private static void Stepper(string label, ref int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(220));
            int step = 0;
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && value > min;
            if (GUILayout.Button("-", GUILayout.Width(30))) step = -1;
            GUI.enabled = enabled && value < max;
            if (GUILayout.Button("+", GUILayout.Width(30))) step = 1;
            GUI.enabled = enabled;
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

        private static string ActionLabel(ActionKind kind)
        {
            switch (kind)
            {
                case ActionKind.StartDrawing: return "Start drawing";
                case ActionKind.ContinueDrawing: return "Continue";
                case ActionKind.CancelDrawing: return "Cancel";
                case ActionKind.DismissForbiddance: return "Dismiss LoF";
                case ActionKind.DrawBindPoints: return "Draw bind points";
                default: return "Pass";
            }
        }
    }
}
