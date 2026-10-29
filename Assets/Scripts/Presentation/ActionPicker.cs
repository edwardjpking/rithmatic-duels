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
        /// <summary>Where the mouse is, if a click there would be ignored because nothing can be placed there: marked with a red cross.</summary>
        public Vec2? InvalidHover;
        /// <summary>Preset spots for the main LoW, drawn as markers that can be clicked.</summary>
        public IReadOnlyList<Vec2> Presets;
        /// <summary>Show the midline of the player's half, which the main LoW snaps to.</summary>
        public bool ShowMidline;
        public Segment? Segment;
        /// <summary>Where a LoF would go if the player clicked now: follows the mouse, drawn fainter than <see cref="Segment"/>.</summary>
        public Segment? GhostSegment;
        public Vec2? From;
        /// <summary>True if <see cref="From"/> only follows the mouse (not yet clicked), drawn in the darker hover blue.</summary>
        public bool FromHovered;
        public Vec2? To;
        public Vec2? Point;
        /// <summary>
        /// A chalkling being placed: its glyph and where it will stand, for its guard ring or attack path. With no
        /// glyph chosen yet, it is a plain circle with no ring or path.
        /// </summary>
        public Glyph? ChalklingGlyph;
        public Vec2? ChalklingAnchor;
        public float ChalklingGuardDistance;
        /// <summary>True if the chalkling only follows the mouse (not yet clicked), drawn in the darker hover blue.</summary>
        public bool ChalklingHovered;
        /// <summary>A bind point under the mouse that a click would pick, ringed faintly.</summary>
        public Vec2? HoverBindPoint;
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
    ///
    /// The menu is laid out to fit the panel without scrolling: short labels sit on the same row as their
    /// buttons, and only the options that apply are shown. If a small window still can't fit it, the
    /// options scroll, while the problem line and Lock in stay at the bottom of the panel.
    ///
    /// The keyboard works it too (<see cref="MenuKeys"/>, DESIGN.md section 5.1): the number keys pick an option in
    /// the first row of buttons with nothing chosen (rows with a default, such as Pace, count as chosen, and steps
    /// that need a board click are skipped), and Space locks in. A key for a greyed-out option, or Space before the
    /// choice is complete, picks nothing: the button or the problem line flashes red and the problem line says why.
    /// </summary>
    public sealed class ActionPicker
    {
        private static readonly string[] LineTypeLabels = { "Warding", "Forbiddance", "Vigor", "Making" };
        private static readonly string[] PaceLabels = { "Normal", "Rush", "Steady" };
        private static readonly string[] TierLabels = { "Low", "Medium", "High" };
        private static readonly string[] StatLabels = { "Durability", "Damage", "Speed" };
        private static readonly string[] GlyphLabels = { "Attack", "Defend" };
        private static readonly string[] ShapeLabels = { "Circle", "Ellipse" };
        private static readonly string[] CircleBindPointLabels = { "4", "6", "9" };
        private static readonly int[] CircleBindPointCounts = { 4, 6, 9 };
        // A LoW added during the duel: a Mark's Cross, or a LoW with this many bind points (0 means Mark's Cross).
        private static readonly string[] AttachedWardLabels = { "Mark's Cross", "2 (ellipse)", "4", "6", "9" };
        private static readonly int[] AttachedWardBindPoints = { 0, 2, 4, 6, 9 };
        private static readonly string[] ForbiddanceLabels = { "Between 2 bind points", "Tangent at 1", "Free line" };
        private static readonly string[] ChalklingLabels = { "Bound to a bind point", "Free" };

        /// <summary>How wide the label in front of a row of buttons is, in GUI pixels.</summary>
        private const float RowLabelWidth = 100f;
        /// <summary>Room kept free for the menu's vertical scroll bar and its margins.</summary>
        private const float ScrollBarAllowance = 24f;

        /// <summary>The width the menu's contents have this frame, set at the start of Draw.</summary>
        private static float _width = 400f;

        /// <summary>The width left for buttons after a row's label.</summary>
        private static float RowWidth { get { return Mathf.Max(60f, _width - RowLabelWidth - 4f); } }

        /// <summary>True between BeginRow and the next button row, which then shares its line with the label.</summary>
        private static bool _afterRowLabel;

        /// <summary>The width the next button row has: the whole menu, or what is left beside a row label.</summary>
        private static float NextRowWidth()
        {
            float width = _afterRowLabel ? RowWidth : _width;
            _afterRowLabel = false;
            return width;
        }

        // Keyboard rows: each pass counts the rows of options down the menu, and the first with nothing chosen
        // is the one the number keys pick in.
        private static int _rowOrdinal;
        private static bool _keyRowFound;
        // A refused key press: which option flashes red (row and option; -1 for the problem line alone), when, and why.
        private static int _flashRow = -1;
        private static int _flashOption = -1;
        private static float _flashStart = -100f;
        private static string _flashReason;
        // Whether the reason shows this frame, settled on the layout pass so every pass of a frame lays out the same lines.
        private static bool _reasonShown;
        /// <summary>How long a refused option or the problem line flashes red, fading out, in seconds.</summary>
        private const float FlashSeconds = 0.6f;
        /// <summary>How long the reason a key press was refused stays in the problem line, in seconds.</summary>
        private const float ReasonSeconds = 3f;

        public ActionPicker()
        {
            // A new player's menu starts with nothing flashing.
            _flashRow = -1;
            _flashOption = -1;
            _flashStart = -100f;
            _flashReason = null;
            _reasonShown = false;
        }

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

        // What the player has typed into each chalkling stat box (see StatEntry).
        private readonly TypedStat _durabilityEntry = new TypedStat();
        private readonly TypedStat _damageEntry = new TypedStat();
        private readonly TypedStat _speedEntry = new TypedStat();

        // Sizes set with the scroll wheel. Negative means "not touched yet", so the config default is used.
        private float _semiMinor = -1f;
        private float _length = -1f;
        private float _attachedSize = -1f;
        private float _attachedScale = -1f;
        // Which of a new circle's bind points joins the LoW it is attached to (Shift + scroll), for sets that aren't symmetric.
        private int _join;

        // Placement clicks: up to two bind points or two board points, whichever the mode uses.
        private int _bindA = -1;
        private int _bindB = -1;
        // With no LoF mode chosen: the first bind point was clicked twice, making a tangent there.
        private bool _tangentPicked;
        private Vec2? _pointA;
        private Vec2? _pointB;
        private Vec2? _hover;
        private string _clickKey;

        /// <summary>What to draw on the board for the choice in progress. Null when there is nothing to show.</summary>
        public PlacementPreview Preview { get; private set; }

        /// <summary>The line whose button in the dismissal list is under the mouse, to highlight on the board. -1 for none.</summary>
        public int HoveredLineId { get; private set; } = -1;

        /// <summary>
        /// Where the mouse is over the board, in board units, or null when it is elsewhere, and whether the free hand
        /// key (Ctrl, or Cmd on macOS) is held, which stops a LoF or chalkling with no mode chosen snapping to bind points (DESIGN.md 1.1).
        /// </summary>
        public void SetHover(Vec2? point, bool freeHand = false)
        {
            _hover = point;
            _freeHand = freeHand;
        }

        private bool _freeHand;

        /// <summary>True on macOS, where the free hand key is Cmd (Amelia, 2026-10-29); Ctrl everywhere else.</summary>
        public static bool FreeHandIsCommand { get { return SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX; } }

        /// <summary>The free hand key's name, for the menu's instructions.</summary>
        private static string FreeHandKey { get { return FreeHandIsCommand ? "Cmd" : "Ctrl"; } }

        /// <summary>The action being built: after Cancel, the one chosen to follow it.</summary>
        private ActionKind? Effective { get { return _kind == ActionKind.CancelDrawing ? _afterCancel : _kind; } }

        /// <summary>
        /// Draws the menu. Returns the choice once the player locks it in, otherwise null. The options scroll
        /// with <paramref name="scroll"/> if they don't fit; Lock in stays at the bottom either way.
        /// </summary>
        public LockedChoice Draw(Duel duel, ChalkBoard board, Player player, ref Vector2 scroll, float width)
        {
            // A pass cut short by ExitGUI (e.g. a stat change) keeps the last preview, so the board doesn't flicker.
            var lastPreview = Preview;
            Preview = null;
            if (Event.current.type == EventType.Repaint) HoveredLineId = -1;
            // Leave room for the vertical scroll bar; button rows wrap to fit, so nothing scrolls sideways.
            _width = Mathf.Max(100f, width - ScrollBarAllowance);
            _afterRowLabel = false;
            _rowOrdinal = 0;
            _keyRowFound = false;
            if (Event.current.type == EventType.Layout) _reasonShown = _flashReason != null && Time.realtimeSinceStartup - _flashStart < ReasonSeconds;
            var available = duel.AvailableActions(player);
            if (available.Count == 0) return null;
            // The scroll view fills the panel down to the footer, so Lock in sits at the bottom.
            scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandHeight(true));
            PlayerAction action = null;
            Placement placement = null;
            string hint = null;
            // A click ends the GUI pass early with GUIUtility.ExitGUI; ending the group in finally keeps
            // Unity's clip stack balanced when it does (otherwise: "pushing more GUIClips than popping").
            try
            {
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
                    int chosen = Buttons(index, labels, _width);
                    if (chosen != index && chosen >= 0)
                    {
                        _kind = available[chosen];
                        GUIUtility.ExitGUI();
                    }
                    GUILayout.Space(4);
                }

                bool cancelling = _kind == ActionKind.CancelDrawing;
                if (cancelling)
                {
                    GUILayout.Label("Abandon your current work (all progress is lost), then:");
                    var after = ActionsAfterCancel(duel, board, player);
                    var labels = new string[after.Count];
                    for (int i = 0; i < after.Count; i++) labels[i] = ActionLabel(after[i]);
                    int index = _afterCancel.HasValue ? after.IndexOf(_afterCancel.Value) : -1;
                    int chosen = Buttons(index, labels, _width);
                    if (chosen != index && chosen >= 0)
                    {
                        _afterCancel = after[chosen];
                        GUIUtility.ExitGUI();
                    }
                    GUILayout.Space(4);
                }

                hint = cancelling ? "Choose what to do instead." : "Choose an action.";
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

            }
            catch (ExitGUIException)
            {
                Preview = lastPreview ?? Preview;
                throw;
            }
            finally
            {
                GUILayout.EndScrollView();
            }
            string problem = action == null ? hint : duel.Validate(player, action);
            if (problem == null && action.Kind == ActionKind.StartDrawing)
                problem = placement == null ? "Finish placing it on the board." : board.ValidatePlacement(player, action.Spec, placement);
            // A refused key press says why here for a moment, in place of the problem.
            string shown = _reasonShown ? _flashReason : problem;
            if (shown != null)
            {
                var colour = GUI.contentColor;
                GUI.contentColor = Color.Lerp(colour, MenuSkin.Refused, FlashStrength());
                GUILayout.Label(shown);
                GUI.contentColor = colour;
            }

            GUI.enabled = problem == null;
            bool locked = GUILayout.Button("Lock in", GUILayout.Height(34));
            GUI.enabled = true;
            // Space locks in, or, before the choice is complete, flashes the problem line.
            if (!locked && MenuKeys.TakeNext())
            {
                if (problem == null) locked = true;
                else
                {
                    Flash(-1, -1, null);
                    GUIUtility.ExitGUI();
                }
            }
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
                    if (_forbiddanceMode == ForbiddanceMode.Free) return ClickAllowed(board, player, point) && PickPoints(point);
                    if (_forbiddanceMode.HasValue) return PickBindPoint(board, player, point, _forbiddanceMode == ForbiddanceMode.Between);
                    return PickForbiddance(board, player, point);
                case LineType.Vigor:
                    return ClickAllowed(board, player, point) && PickPoints(point);
                case LineType.Making:
                    if (_chalklingMode == ChalklingMode.Bound) return PickBindPoint(board, player, point, false);
                    if (ChalklingSnaps)
                    {
                        int near = NearBindPoint(board, player, point);
                        if (near >= 0)
                        {
                            ClearClicks();
                            _bindA = near;
                            return true;
                        }
                    }
                    if (!ClickAllowed(board, player, point)) return false;
                    ClearClicks();
                    _pointA = point;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A right-click anywhere while choosing: undoes the last choice made, one layer of the menu at a time,
        /// so repeated right-clicks walk all the way back to no action. Layers, last first: pace; the placement
        /// (positions, bind points and scroll sizes together); the placement or tier choice; for a chalkling its
        /// glyph, tether, + modifier stat and stat values; the line type; the action. Returns true if anything changed.
        /// </summary>
        public bool HandleRightClick(ChalkBoard board, Player player, RulesConfig config)
        {
            bool needsMain = board.MainWardOf(player) == null && config.MainWardFirst;
            if (Effective == ActionKind.StartDrawing)
            {
                if (needsMain ? StepBackMainWard(config.Geometry.MainWardAtHome) : StepBackDrawing()) return true;
            }
            else if (Effective == ActionKind.DismissForbiddance && _targetLineId >= 0)
            {
                _targetLineId = -1;
                return true;
            }
            else if (Effective == ActionKind.DrawBindPoints && _bindPointChoice >= 0)
            {
                _bindPointChoice = -1;
                return true;
            }

            if (_kind == ActionKind.CancelDrawing && _afterCancel.HasValue) { _afterCancel = null; return true; }
            if (_kind.HasValue) { _kind = null; return true; }
            return false;
        }

        /// <summary>The main LoW setup, last first: bind points, position (unless it sits at home), flatness, shape.</summary>
        private bool StepBackMainWard(bool atHome)
        {
            if (_bindPointChoice >= 0) { _bindPointChoice = -1; return true; }
            // At home the position is filled in for the player, so it isn't a choice to undo.
            if (_pointA.HasValue && !atHome) { ClearClicks(); return true; }
            if (_semiMinor > 0f) { _semiMinor = -1f; return true; }
            if (_mainShape.HasValue) { _mainShape = null; _pointA = null; return true; }
            return false;
        }

        /// <summary>A drawing during the duel, last first. Returns false once only the action is left to undo.</summary>
        private bool StepBackDrawing()
        {
            if (_pace != DrawingPace.Normal) { _pace = DrawingPace.Normal; return true; }
            if (HasPlacement()) { ClearPlacement(); return true; }
            switch (_lineType)
            {
                case LineType.Warding:
                    if (_attachedWardChoice >= 0) { _attachedWardChoice = -1; return true; }
                    break;
                case LineType.Forbiddance:
                    if (_forbiddanceMode.HasValue) { _forbiddanceMode = null; return true; }
                    break;
                case LineType.Vigor:
                    if (_tier.HasValue) { _tier = null; return true; }
                    break;
                case LineType.Making:
                    if (_glyph.HasValue) { _glyph = null; return true; }
                    if (_chalklingMode.HasValue) { _chalklingMode = null; return true; }
                    if (_modifierStat.HasValue) { _modifierStat = null; return true; }
                    if (_durabilityBuffs != 0 || _damageBuffs != 0 || _speedBuffs != 0) { ClearStats(); return true; }
                    break;
            }
            if (_lineType.HasValue) { _lineType = null; return true; }
            return false;
        }

        /// <summary>True if anything has been placed: a position or bind point clicked, or a size scrolled.</summary>
        private bool HasPlacement()
        {
            return _bindA >= 0 || _bindB >= 0 || _pointA.HasValue || _pointB.HasValue
                || _length > 0f || _attachedSize > 0f || _attachedScale > 0f || _join != 0;
        }

        private void ClearPlacement()
        {
            ClearClicks();
            _length = -1f;
            _attachedSize = -1f;
            _attachedScale = -1f;
            _join = 0;
        }

        private void ClearStats()
        {
            _durabilityBuffs = 0;
            _damageBuffs = 0;
            _speedBuffs = 0;
            foreach (var entry in new[] { _durabilityEntry, _damageEntry, _speedEntry })
            {
                entry.Text = "";
                entry.ShownBuffs = -1;
            }
        }

        /// <summary>
        /// The scroll wheel over the board: makes a LoW ellipse flatter or rounder, an attached LoW circle
        /// bigger or smaller, or a LoF not bound at two points shorter or longer. For an attached LoW ellipse, plain
        /// scroll scales its overall size and <paramref name="overall"/> (Shift held) makes it rounder or flatter. Returns true if it changed the choice.
        /// </summary>
        public bool HandleScroll(ChalkBoard board, Player player, RulesConfig config, float delta, bool overall = false)
        {
            if (Effective != ActionKind.StartDrawing || Mathf.Abs(delta) < 1e-6f) return false;
            var geometry = config.Geometry;
            // Scrolling up (negative delta) makes things bigger.
            float step = delta < 0f ? geometry.ScrollStep : -geometry.ScrollStep;

            if (board.MainWardOf(player) == null && config.MainWardFirst)
            {
                if (_mainShape != MainShape.Ellipse) return false;
                // Plain scroll and Shift + scroll both change its roundness (it has no overall size).
                // Each notch moves to the next whole-number durability of the ellipse's weakest point.
                _semiMinor = StepAlong(MainSteps(board, player), SemiMinor(board, player), delta < 0f ? 1 : -1);
                return true;
            }

            if (_lineType == LineType.Warding && _attachedWardChoice > 0)
            {
                var spec = DrawingSpec.Warding(AttachedWardBindPoints[_attachedWardChoice]);
                if (overall && board.JoinChoices(spec) > 1)
                {
                    // Turn the circle so the next of its bind points joins. Scrolling up turns it anticlockwise
                    // (changed 2026-10-27; to become a player setting, docs/FUTURE_IDEAS.md).
                    int choices = board.JoinChoices(spec);
                    _join = ((_join + (delta < 0f ? -1 : 1)) % choices + choices) % choices;
                    return true;
                }
                if (spec.BindPoints == 2 && !overall)
                {
                    // Plain scroll sizes an ellipse overall, like other sizes; Shift + scroll changes how round it is.
                    // Each notch moves to the next whole-number durability, never bigger than fits on the player's half.
                    _attachedScale = StepAlong(AttachedScaleSteps(board, player, spec), AttachedScale(board, player, spec), delta < 0f ? 1 : -1);
                    return true;
                }
                if (overall && spec.BindPoints != 2) return false;
                // Each notch moves to the next whole-number durability, never bigger than fits on the player's half.
                _attachedSize = StepAlong(AttachedSteps(board, player, spec), AttachedSize(board, player, spec), delta < 0f ? 1 : -1);
                return true;
            }

            var lofMode = _lineType == LineType.Forbiddance ? ForbiddanceModeAt(board, player) : null;
            if (lofMode == ForbiddanceMode.Tangent || lofMode == ForbiddanceMode.Free)
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

            BeginRow("1. Shape:");
            Choose(ref _mainShape, ShapeLabels);
            GUILayout.EndHorizontal();
            if (!_mainShape.HasValue)
            {
                hint = "Choose a shape.";
                return null;
            }
            bool ellipse = _mainShape == MainShape.Ellipse;
            if (ellipse) GUILayout.Label("Scroll (or Shift + scroll) over the board to make it flatter or rounder. Flatter ellipses are weaker along their sides.");
            bool atHome = config.Geometry.MainWardAtHome;
            if (atHome) _pointA = board.Home(player);
            else GUILayout.Label("2. Click on your half of the board to place it.");

            BeginRow((atHome ? "2" : "3") + ". Bind points:");
            int bindPoints = -1;
            if (ellipse)
            {
                GUILayout.Label("2, at the ends of its long axis.");
                _afterRowLabel = false;
                if (_pointA.HasValue) bindPoints = 2;
            }
            else
            {
                GUI.enabled = _pointA.HasValue;
                Choose(ref _bindPointChoice, CircleBindPointLabels, 0, "Place it on the board first.");
                GUI.enabled = true;
                if (_pointA.HasValue && _bindPointChoice >= 0) bindPoints = CircleBindPointCounts[_bindPointChoice];
            }
            GUILayout.EndHorizontal();

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
            GUILayout.Space(4);
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
            // Clicks belong to one line type; switching it starts placement over. Other settings can be chosen before
            // or after placing, and a setting that wouldn't fit what is already placed is greyed out.
            ResetClicksIfChanged(_lineType.ToString());
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
                {
                    // Five buttons need the full width, so the label goes above them.
                    GUILayout.Label("Bind points:");
                    int before = _attachedWardChoice;
                    Choose(ref _attachedWardChoice, AttachedWardLabels, WardChoiceMask(board, player), "That doesn't fit at the bind point you picked.");
                    if (_attachedWardChoice != before) { _attachedSize = -1f; _attachedScale = -1f; _join = 0; }
                    int anchor = WardAnchor(board, player);
                    var bindSpots = board.BindPointsOf(player);
                    if (_attachedWardChoice < 0)
                    {
                        GUILayout.Label("Click a bind point on one of your Lines of Warding to attach it there.");
                        hint = "Choose a Mark's Cross or how many bind points.";
                        // Until a kind is chosen no shape is shown, only the bind point picked or under the mouse.
                        if (_bindA < 0 && anchor >= 0 && anchor < bindSpots.Count) preview.HoverBindPoint = bindSpots[anchor];
                        preview.BlockedBindPoints = BlockedBindPoints(board, player);
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
                            ? "An ellipse sticking out from the bind point. Scroll to make it bigger or smaller, Shift + scroll to make it rounder or flatter."
                            : board.JoinChoices(spec) > 1
                                ? "A circle outside the bind point. Scroll to make it bigger or smaller, Shift + scroll to turn it so a different one of its bind points joins."
                                : "A circle outside the bind point. Scroll to make it bigger or smaller.");
                    }
                    GUILayout.Label("Click a bind point on one of your Lines of Warding to attach it there.");
                    if (anchor >= 0 && anchor < bindSpots.Count)
                    {
                        var at = Placement.AtBindPoint(anchor, count == 0 ? 0f : AttachedSize(board, player, spec),
                            count == 2 ? AttachedScale(board, player, spec) : 0f, _join % board.JoinChoices(spec));
                        if (anchor == _bindA)
                        {
                            placement = at;
                            IReadOnlyList<Vec2> points;
                            preview.Ward = board.PreviewWard(player, spec, placement, out points);
                            preview.WardBindPoints = points;
                            if (preview.Ward != null && count == 0) preview.Braces = ChalkBoard.MarksCrossBraces(preview.Ward, points);
                            if (preview.Ward != null) GUILayout.Label("Weakest point: " + board.WeakestDurability(preview.Ward) + " durability, before its modifier.");
                        }
                        else preview.GhostWard = board.PreviewWard(player, spec, at);
                    }
                    preview.BlockedBindPoints = BlockedBindPoints(board, player);
                    break;
                }

                case LineType.Forbiddance:
                {
                    var placed = PlacedForbiddanceMode(board, player);
                    int chosenMode = ChooseShown(placed.HasValue ? (int)placed.Value : -1, ForbiddanceLabels, ForbiddanceModeMask(board, player), StepBackReason);
                    if (chosenMode >= 0)
                    {
                        _forbiddanceMode = (ForbiddanceMode)chosenMode;
                        GUIUtility.ExitGUI();
                    }
                    // A bind point near the mouse, where a click would go (only while bind points can be clicked).
                    int hoverBind = _forbiddanceMode == ForbiddanceMode.Free || _pointA.HasValue && !_pointB.HasValue || FreeHandLof ? -1 : HoverBindPoint(board, player);
                    switch (placed)
                    {
                        case ForbiddanceMode.Between:
                            GUILayout.Label("Click two bind points on your Lines of Warding.");
                            spec = DrawingSpec.Forbiddance(2);
                            if (_bindA >= 0 && _bindB >= 0) placement = Placement.BetweenBindPoints(_bindA, _bindB);
                            else if (_forbiddanceMode.HasValue && _bindA >= 0 && hoverBind >= 0 && hoverBind != _bindA && CanJoin(board, player, _bindA, hoverBind))
                                preview.GhostSegment = board.PreviewForbiddance(player, Placement.BetweenBindPoints(_bindA, hoverBind));
                            break;
                        case ForbiddanceMode.Tangent:
                            GUILayout.Label(_forbiddanceMode.HasValue
                                ? "Click a bind point. The line lies along the circle there. Scroll to change its length."
                                : "A tangent: the line lies along the circle there. Scroll to change its length.");
                            spec = DrawingSpec.Forbiddance(1);
                            if (_bindA >= 0) placement = Placement.TangentAt(_bindA, FittedLength(config, board, player));
                            // With the mode chosen, the next click on a bind point starts a tangent there.
                            if (_forbiddanceMode.HasValue && hoverBind >= 0 && hoverBind != _bindA)
                                preview.GhostSegment = TangentGhost(config, board, player, hoverBind);
                            break;
                        case ForbiddanceMode.Free:
                            GUILayout.Label("Click where the line starts, then click to set its direction. Scroll to change its length.");
                            spec = DrawingSpec.Forbiddance(0);
                            Vec2? toward = _pointB ?? _hover;
                            if (_pointA.HasValue && toward.HasValue && Vec2.Distance(_pointA.Value, toward.Value) > 1e-3f)
                            {
                                Vec2 end = _pointA.Value + (toward.Value - _pointA.Value).Normalized * FittedLength(config, board, player);
                                // Where even the shortest line wouldn't fit (it would overlap the centre line or a LoW), show none.
                                if (ForbiddanceFits(board, player, spec, Placement.Segment(_pointA.Value, end)))
                                {
                                    preview.Segment = new Segment(_pointA.Value, end);
                                    if (_pointB.HasValue) placement = Placement.Segment(_pointA.Value, end);
                                }
                                else hint = "No Line of Forbiddance fits here. Start it somewhere else.";
                            }
                            preview.From = _pointA;
                            break;
                        default:
                            // No line is shown until it is clear which: the same bind point twice for a tangent, or a second one to join them.
                            GUILayout.Label(_bindA >= 0
                                ? "Click the same bind point again for a tangent, or another bind point to join the two."
                                : "Click a bind point (twice for a tangent, or then another to join the two), or anywhere else on your half to start a free line. Hold " + FreeHandKey + " to start a free line next to a bind point.");
                            hint = "Click the board to place it.";
                            break;
                    }
                    if (_forbiddanceMode != ForbiddanceMode.Between && _forbiddanceMode != ForbiddanceMode.Tangent)
                    {
                        preview.InvalidHover = InvalidHover(board, player);
                        // Where a free line would start: hidden over a spot where nothing can be placed.
                        if (!_pointA.HasValue && hoverBind < 0 && !preview.InvalidHover.HasValue)
                        {
                            preview.From = _hover;
                            preview.FromHovered = true;
                        }
                    }
                    if (_forbiddanceMode != ForbiddanceMode.Free) preview.BlockedBindPoints = BlockedBindPoints(board, player);
                    // The bind point a click would pick is ringed in the hover blue, whatever the mode.
                    if (hoverBind >= 0 && hoverBind != _bindA)
                        preview.HoverBindPoint = board.BindPointsOf(player)[hoverBind];
                    if (placement != null) preview.Segment = board.PreviewForbiddance(player, placement);
                    if (spec != null && placement != null)
                        GUILayout.Label("Length " + board.Measure(player, spec, placement).Length.ToString("0.0")
                            + ": longer lines take longer to draw.");
                    break;
                }

                case LineType.Vigor:
                    Choose(ref _tier, TierLabels);
                    if (_tier.HasValue)
                    {
                        var tier = config.Vigor.Tier(_tier.Value);
                        GUILayout.Label("Speed " + tier.Speed + ", damage " + tier.Damage + " before its modifier.");
                    }
                    GUILayout.Label("Click where to draw it on your half, then click where to aim.");
                    preview.InvalidHover = InvalidHover(board, player);
                    // Before the first click, the starting point follows the mouse, except over a spot where it can't go.
                    preview.From = _pointA ?? (preview.InvalidHover.HasValue ? null : _hover);
                    preview.FromHovered = !_pointA.HasValue;
                    preview.To = _pointB ?? (_pointA.HasValue ? _hover : null);
                    if (!_tier.HasValue)
                    {
                        hint = "Choose a tier.";
                        break;
                    }
                    spec = DrawingSpec.Vigor(_tier.Value);
                    if (_pointA.HasValue && _pointB.HasValue) placement = Placement.Aimed(_pointA.Value, _pointB.Value);
                    break;

                case LineType.Making:
                {
                    spec = DrawMakingPointBuy(config, out hint);
                    var placedTether = PlacedChalklingMode;
                    bool snaps = ChalklingSnaps;
                    GUILayout.Label(_chalklingMode == ChalklingMode.Bound ? "Click a bind point to tether it there."
                        : snaps ? "Click where to draw it on your half. A click near a bind point tethers it there (hold " + FreeHandKey + " not to)."
                        : "Click where to draw it on your half.");
                    var bindSpots = board.BindPointsOf(player);
                    if (_chalklingMode != ChalklingMode.Bound) preview.InvalidHover = InvalidHover(board, player);
                    // Where it would go: the spot placed, or else where the mouse is (snapped to a bind point near it).
                    int anchorBind = _bindA;
                    Vec2? anchor = null;
                    if (_bindA < 0 && !_pointA.HasValue && (snaps || _chalklingMode == ChalklingMode.Bound)) anchorBind = HoverBindPoint(board, player);
                    if (anchorBind >= 0 && anchorBind < bindSpots.Count) anchor = board.BoundChalklingSpot(player, anchorBind);
                    else if (_pointA.HasValue) anchor = _pointA;
                    else if (_chalklingMode != ChalklingMode.Bound && !preview.InvalidHover.HasValue) anchor = _hover;
                    bool boundAt = anchorBind >= 0;
                    preview.ChalklingAnchor = anchor;
                    preview.ChalklingHovered = _bindA < 0 && !_pointA.HasValue;
                    // A bound chalkling always defends. Until a glyph is chosen, it shows no glyph, ring or path.
                    preview.ChalklingGlyph = boundAt ? Glyph.Defend : placedTether == ChalklingMode.Bound ? (Glyph?)null : _glyph;
                    preview.ChalklingGuardDistance = boundAt ? config.Making.TetherDistance : config.Making.GuardDistance;
                    if (snaps || _chalklingMode == ChalklingMode.Bound) preview.BlockedBindPoints = BlockedBindPoints(board, player);
                    if (spec == null) break;
                    if (_bindA >= 0) placement = Placement.AtBindPoint(_bindA);
                    else if (_pointA.HasValue) placement = Placement.At(_pointA.Value);
                    break;
                }
            }
            if (_bindA >= 0) preview.BindPoints.Add(_bindA);
            if (_bindB >= 0) preview.BindPoints.Add(_bindB);
            if (spec == null) return null;

            if (placement != null)
            {
                spec = board.Measure(player, spec, placement);
                if (spec.TravelTurns > 0)
                    GUILayout.Label("+" + spec.TravelTurns + " turn(s) to get there: it is "
                        + board.TravelDistance(player, placement).ToString("0.0") + " units from your main LoW.");
            }

            GUILayout.Space(4);
            BeginRow("Pace:");
            Choose(ref _pace, PaceLabels);
            GUILayout.EndHorizontal();
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
            BeginRow("Bind points:");
            Choose(ref _bindPointChoice, labels);
            GUILayout.EndHorizontal();
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
            GUILayout.Label("Type each stat or use - and +. Every " + making.StatBuffsPerDrawTurn + " points take a turn, and a chalkling takes at least "
                + making.MinimumDrawTurns + " turns unless rushed.");
            StatEntry("Durability", _durabilityEntry, ref _durabilityBuffs, making.BaseDurability, making.DurabilityPerBuff, "Damage");
            StatEntry("Damage", _damageEntry, ref _damageBuffs, making.BaseDamage, making.DamagePerBuff, "Speed");
            StatEntry("Speed", _speedEntry, ref _speedBuffs, making.BaseSpeed, making.SpeedPerBuff, null);
            BeginRow("+ modifier to:");
            Choose(ref _modifierStat, StatLabels);
            GUILayout.EndHorizontal();
            // The tether can be chosen here, or follows from where it is placed (a click near a bind point binds it).
            // Once placed, the other choice is greyed out.
            var placedTether = PlacedChalklingMode;
            BeginRow("Tether:");
            int tetherMask = _bindA >= 0 ? 1 << (int)ChalklingMode.Free : _pointA.HasValue ? 1 << (int)ChalklingMode.Bound : 0;
            int chosenTether = ChooseShown(placedTether.HasValue ? (int)placedTether.Value : -1, ChalklingLabels, tetherMask, StepBackReason);
            if (chosenTether >= 0)
            {
                _chalklingMode = (ChalklingMode)chosenTether;
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();
            // One glyph, Attack or Defend; a bound chalkling must Defend.
            bool bound = placedTether == ChalklingMode.Bound;
            if (bound) GUILayout.Label("Glyph: Defend, since it is bound.");
            else
            {
                // Attack is greyed out while the rules would refuse it (0 speed), and a chosen Attack is dropped.
                string attackProblem = DrawTimes.Validate(
                    DrawingSpec.Making(_durabilityBuffs, _damageBuffs, _speedBuffs, Glyph.Attack, 0, _modifierStat ?? default), config);
                if (attackProblem != null && _glyph == Glyph.Attack) _glyph = null;
                BeginRow("Glyph:");
                Choose(ref _glyph, GlyphLabels, attackProblem != null ? 1 << (int)Glyph.Attack : 0, "Attack: " + attackProblem);
                GUILayout.EndHorizontal();
                if (attackProblem != null) GUILayout.Label("Attack: " + attackProblem);
            }
            if (!_modifierStat.HasValue || !placedTether.HasValue || (!bound && !_glyph.HasValue))
            {
                hint = !_modifierStat.HasValue ? "Choose where a positive modifier goes."
                    : !bound && !_glyph.HasValue ? "Choose its glyph." : "Click the board to place it.";
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
                return text + " Rushing saves no time, but it goes down before lines move"
                    + (spec.Type == LineType.Vigor ? " and flies straight away" : "") + ".";
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
            bool keyRow;
            int row = KeyRow(index, out keyRow);
            int keyed = keyRow ? KeyPick(row, targets.Count, 0, null) : -1;
            if (keyed >= 0)
            {
                _targetLineId = targets[keyed].Id;
                GUIUtility.ExitGUI();
            }

            // One button per line. A right-click never reaches them: DuelController takes it first (HandleRightClick).
            for (int i = 0; i < targets.Count; i++)
            {
                var label = new GUIContent(TurnNarrator.LineName(targets[i]) + "  " + targets[i].Durability + "/" + targets[i].MaxDurability);
                Rect rect = GUILayoutUtility.GetRect(label, GUI.skin.button);
                bool selected = i == index;
                if (Event.current.type == EventType.Repaint && rect.Contains(Event.current.mousePosition)) HoveredLineId = targets[i].Id;
                bool toggled = GUI.Toggle(rect, selected, label, GUI.skin.button) != selected;
                if (Event.current.type == EventType.Repaint) DecorateOption(rect, row, i, keyRow);
                if (toggled && !selected)
                {
                    _targetLineId = targets[i].Id;
                    GUIUtility.ExitGUI();
                }
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
        private List<float> _scaleSteps;
        private string _scaleStepsKey;
        private bool[] _blocked;
        private string _blockedKey;

        private List<float> MainSteps(ChalkBoard board, Player player)
        {
            if (_mainSteps == null) _mainSteps = board.MainEllipseSizes(player);
            return _mainSteps;
        }

        private List<float> AttachedSteps(ChalkBoard board, Player player, DrawingSpec spec)
        {
            float scale = spec.BindPoints == 2 ? ChosenScale(board) : 0f;
            int anchor = WardAnchor(board, player);
            string key = spec.BindPoints + "/" + anchor + "/" + board.BindPointsOf(player).Count + "/" + scale;
            if (_attachedSteps == null || key != _attachedStepsKey)
            {
                _attachedSteps = board.AttachedSizes(player, spec, anchor, scale);
                _attachedStepsKey = key;
            }
            return _attachedSteps;
        }

        private List<float> AttachedScaleSteps(ChalkBoard board, Player player, DrawingSpec spec)
        {
            float size = AttachedSize(board, player, spec);
            int anchor = WardAnchor(board, player);
            string key = anchor + "/" + board.BindPointsOf(player).Count + "/" + size;
            if (_scaleSteps == null || key != _scaleStepsKey)
            {
                _scaleSteps = board.AttachedScales(player, spec, anchor, size);
                _scaleStepsKey = key;
            }
            return _scaleSteps;
        }

        // The scale as last set, before snapping to a step (the steps themselves depend on the flatness).
        private float ChosenScale(ChalkBoard board)
        {
            return board.AttachedScale(Placement.AtBindPoint(0, 0f, _attachedScale));
        }

        /// <summary>
        /// The chosen overall scale of an attached ellipse, on one of its whole-number durability steps and cut
        /// back if it would reach off the player's half.
        /// </summary>
        private float AttachedScale(ChalkBoard board, Player player, DrawingSpec spec)
        {
            return Nearest(AttachedScaleSteps(board, player, spec), ChosenScale(board));
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
            var mode = ForbiddanceModeAt(board, player);
            int bind = _bindA >= 0 ? _bindA : HoverBindPoint(board, player);
            if (mode == ForbiddanceMode.Tangent && bind >= 0)
                return board.MaxForbiddanceLength(player, Placement.TangentAt(bind), Vec2.Zero);
            Vec2? toward = _pointB ?? _hover;
            if (mode == ForbiddanceMode.Free && _pointA.HasValue && toward.HasValue && Vec2.Distance(_pointA.Value, toward.Value) > 1e-3f)
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
            // Bind points filled in red can't be picked.
            int nearest = NearBindPoint(board, player, point);
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
            string key = _lineType + "/" + _forbiddanceMode + "/" + _attachedWardChoice + "/" + _bindA + "/" + _bindB + "/" + _tangentPicked + "/" + count + "/" + player;
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
        /// a LoW can't bind at a point that already has its LoW, or where even the smallest LoW of the chosen kind
        /// (of any kind, before one is chosen) wouldn't fit;
        /// a tangent LoF can't go where even the shortest one wouldn't fit (it would cross a LoW or leave your half);
        /// a LoF between two bind points can't join two points whose line wouldn't fit. Before the first pick, a
        /// point is blocked if no other point can join it; after it, a point is blocked if it can't join the first.
        /// With no LoF mode chosen, a point is blocked if it can take neither a tangent nor a LoF to another point;
        /// a chalkling can't be bound where the rules wouldn't allow it.
        /// </summary>
        private bool BindPointBlocked(ChalkBoard board, Player player, int index)
        {
            switch (_lineType)
            {
                case LineType.Warding:
                    if (board.BindPointFull(player, index)) return true;
                    if (_attachedWardChoice >= 0) return !WardFits(board, player, _attachedWardChoice, index);
                    for (int choice = 0; choice < AttachedWardBindPoints.Length; choice++)
                        if (WardFits(board, player, choice, index)) return false;
                    return true;
                case LineType.Forbiddance:
                    if (_forbiddanceMode == ForbiddanceMode.Tangent) return !TangentFits(board, player, index);
                    if (_forbiddanceMode == ForbiddanceMode.Free) return false;
                    if (_forbiddanceMode == ForbiddanceMode.Between && _bindA >= 0 && _bindB < 0)
                        return index != _bindA && !CanJoin(board, player, _bindA, index);
                    if (_forbiddanceMode == ForbiddanceMode.Between) return !HasPartner(board, player, index);
                    // No mode chosen, one bind point picked: the same one again for a tangent, or another that can join it.
                    if (_bindA >= 0 && _bindB < 0 && !_tangentPicked)
                        return index == _bindA ? !TangentFits(board, player, index) : !CanJoin(board, player, _bindA, index);
                    return !TangentFits(board, player, index) && !HasPartner(board, player, index);
                case LineType.Making:
                    return !Fits(board, player, DrawingSpec.Making(glyph: Glyph.Defend, boundBindPoints: 1), Placement.AtBindPoint(index));
                default:
                    return false;
            }
        }

        /// <summary>True if even the smallest LoW of this kind (an index into the bind point choices) fits at this bind point.</summary>
        private static bool WardFits(ChalkBoard board, Player player, int choice, int index)
        {
            int bindPoints = AttachedWardBindPoints[choice];
            var ward = bindPoints == 0 ? DrawingSpec.MarksCross(board.Config) : DrawingSpec.Warding(bindPoints);
            float min = 0f, max, start;
            if (bindPoints > 0) board.AttachedSizeRange(ward, out min, out max, out start);
            // An ellipse is smallest at its smallest overall scale (scroll) too.
            float scale = bindPoints == 2 ? board.Config.Geometry.AttachedEllipseMinScale : 0f;
            return Fits(board, player, ward, Placement.AtBindPoint(index, min, scale));
        }

        /// <summary>Where a new LoW goes, or would go: the bind point picked, or else the one under the mouse. -1 for neither.</summary>
        private int WardAnchor(ChalkBoard board, Player player)
        {
            return _bindA >= 0 ? _bindA : HoverBindPoint(board, player);
        }

        /// <summary>
        /// The kinds of LoW greyed out once a bind point is picked: those that wouldn't fit there (one bit per choice).
        /// Worked out again only when the pick changes.
        /// </summary>
        private int WardChoiceMask(ChalkBoard board, Player player)
        {
            if (_bindA < 0) return 0;
            string key = "ward/" + _bindA + "/" + player;
            if (key != _maskKey)
            {
                _mask = 0;
                for (int choice = 0; choice < AttachedWardBindPoints.Length; choice++)
                    if (!WardFits(board, player, choice, _bindA)) _mask |= 1 << choice;
                _maskKey = key;
            }
            return _mask;
        }

        private string _maskKey;
        private int _mask;

        private static bool TangentFits(ChalkBoard board, Player player, int index)
        {
            return ForbiddanceFits(board, player, DrawingSpec.Forbiddance(1), Placement.TangentAt(index, board.Config.Geometry.MinForbiddanceLength));
        }

        private static bool CanJoin(ChalkBoard board, Player player, int a, int b)
        {
            return a != b && ForbiddanceFits(board, player, DrawingSpec.Forbiddance(2), Placement.BetweenBindPoints(a, b));
        }

        /// <summary>True if a LoF could join this bind point to some other one.</summary>
        private static bool HasPartner(ChalkBoard board, Player player, int index)
        {
            int count = board.BindPointsOf(player).Count;
            for (int other = 0; other < count; other++)
                if (CanJoin(board, player, index, other)) return true;
            return false;
        }

        /// <summary>
        /// How the LoF being placed is bound, as far as the clicks so far say: the mode the player chose, or else
        /// the same bind point twice means a tangent, two bind points a LoF between them, and a spot away from bind
        /// points a free line. Null before anything is placed, and while one bind point waits for the next click.
        /// </summary>
        private ForbiddanceMode? PlacedForbiddanceMode(ChalkBoard board, Player player)
        {
            if (_forbiddanceMode.HasValue) return _forbiddanceMode;
            if (_pointA.HasValue) return ForbiddanceMode.Free;
            if (_bindB >= 0) return ForbiddanceMode.Between;
            if (_bindA >= 0 && _tangentPicked) return ForbiddanceMode.Tangent;
            return null;
        }

        /// <summary>
        /// <see cref="PlacedForbiddanceMode"/>, or before anything is placed, a free line where a click at the mouse
        /// would start one (near a bind point it is not yet known).
        /// </summary>
        private ForbiddanceMode? ForbiddanceModeAt(ChalkBoard board, Player player)
        {
            var placed = PlacedForbiddanceMode(board, player);
            if (placed.HasValue || !_hover.HasValue || _bindA >= 0) return placed;
            return _freeHand || HoverBindPoint(board, player) < 0 ? ForbiddanceMode.Free : (ForbiddanceMode?)null;
        }

        /// <summary>
        /// The LoF modes greyed out because they don't fit what is already placed (one bit per mode): a free line's
        /// start can't become bound, two bind points can only be joined, and one bind point can't be free, nor a
        /// tangent or a join where none fits there.
        /// </summary>
        private int ForbiddanceModeMask(ChalkBoard board, Player player)
        {
            const int between = 1 << (int)ForbiddanceMode.Between, tangent = 1 << (int)ForbiddanceMode.Tangent, free = 1 << (int)ForbiddanceMode.Free;
            if (_pointA.HasValue) return between | tangent;
            if (_bindB >= 0) return tangent | free;
            if (_bindA < 0) return 0;
            if (_tangentPicked && !_forbiddanceMode.HasValue) return between | free;
            string key = "lof/" + _bindA + "/" + player;
            if (key != _maskKey)
            {
                _mask = free;
                if (!TangentFits(board, player, _bindA)) _mask |= tangent;
                if (!HasPartner(board, player, _bindA)) _mask |= between;
                _maskKey = key;
            }
            return _mask;
        }

        /// <summary>
        /// A click for a LoF with no mode chosen (Amelia, 2026-10-29): after a first bind point, clicking it again
        /// makes a tangent there and clicking another joins the two (bind points that can do neither are red); with
        /// nothing picked, or a line already placed, a bind point starts over from it; a spot away from bind points
        /// starts a free line, and once started, the next click sets its direction.
        /// </summary>
        private bool PickForbiddance(ChalkBoard board, Player player, Vec2 point)
        {
            if (_pointA.HasValue && !_pointB.HasValue) return PickPoints(point);
            int near = _freeHand ? -1 : NearBindPoint(board, player, point);
            if (near >= 0 && _bindA >= 0 && _bindB < 0 && !_tangentPicked)
            {
                if (near == _bindA) _tangentPicked = true;
                else _bindB = near;
                return true;
            }
            if (near >= 0)
            {
                ClearClicks();
                _bindA = near;
                return true;
            }
            if (!ClickAllowed(board, player, point)) return false;
            ClearClicks();
            _pointA = point;
            return true;
        }

        /// <summary>A faint tangent at this bind point, at the chosen length, for hovering. Null where none fits.</summary>
        private Segment? TangentGhost(RulesConfig config, ChalkBoard board, Player player, int index)
        {
            if (!TangentFits(board, player, index)) return null;
            float length = Mathf.Clamp(Length(config), config.Geometry.MinForbiddanceLength,
                board.MaxForbiddanceLength(player, Placement.TangentAt(index), Vec2.Zero));
            return board.PreviewForbiddance(player, Placement.TangentAt(index, length));
        }

        /// <summary>How the chalkling being placed is tethered: as chosen, or else as placed (on a bind point or not). Null before either.</summary>
        private ChalklingMode? PlacedChalklingMode
        {
            get
            {
                if (_chalklingMode.HasValue) return _chalklingMode;
                if (_bindA >= 0) return ChalklingMode.Bound;
                if (_pointA.HasValue) return ChalklingMode.Free;
                return null;
            }
        }

        /// <summary>
        /// True if a click near a bind point binds the chalkling there: when no tether is chosen and it isn't set to
        /// Attack, since a bound chalkling must defend.
        /// </summary>
        private bool ChalklingSnaps { get { return !_chalklingMode.HasValue && _glyph != Glyph.Attack && !_freeHand; } }

        /// <summary>True while the free hand key keeps a LoF with no mode chosen from snapping to bind points.</summary>
        private bool FreeHandLof { get { return _freeHand && !_forbiddanceMode.HasValue; } }

        /// <summary>The bind point that can be picked nearest the mouse, within picking distance, or -1.</summary>
        private int HoverBindPoint(ChalkBoard board, Player player)
        {
            return _hover.HasValue ? NearBindPoint(board, player, _hover.Value) : -1;
        }

        /// <summary>The bind point that can be picked (not filled in red) nearest this point, within picking distance, or -1.</summary>
        private int NearBindPoint(ChalkBoard board, Player player, Vec2 point)
        {
            var bindPoints = board.BindPointsOf(player);
            var blocked = Blocked(board, player);
            int nearest = -1;
            float best = PickRadius;
            for (int i = 0; i < bindPoints.Count; i++)
            {
                if (blocked[i]) continue;
                float distance = Vec2.Distance(bindPoints[i], point);
                if (distance < best) { best = distance; nearest = i; }
            }
            return nearest;
        }

        private static bool ForbiddanceFits(ChalkBoard board, Player player, DrawingSpec spec, Placement placement)
        {
            return Fits(board, player, spec, placement);
        }

        private static bool Fits(ChalkBoard board, Player player, DrawingSpec spec, Placement placement)
        {
            return board.ValidatePlacement(player, board.Measure(player, spec, placement), placement) == null;
        }

        /// <summary>
        /// Whether a click on the board at this point can be taken as the next pick (DESIGN.md section 1.1: anything that
        /// isn't allowed can't be picked). Only the starting point of a free LoF or a LoV, and an unbound chalkling's
        /// spot, are checked here: bind points that can't be used are blocked instead, and a LoV may aim anywhere.
        /// </summary>
        private bool ClickAllowed(ChalkBoard board, Player player, Vec2 point)
        {
            switch (_lineType)
            {
                case LineType.Forbiddance:
                    if (_pointA.HasValue && !_pointB.HasValue) return true; // the second click sets the direction
                    // With no mode chosen, a click near a bind point that can be picked starts a tangent or joins the two.
                    if (!_forbiddanceMode.HasValue && !_freeHand && NearBindPoint(board, player, point) >= 0) return true;
                    // A start is allowed if the shortest line fits in some direction from it.
                    float length = board.Config.Geometry.MinForbiddanceLength;
                    const int directions = 16;
                    for (int i = 0; i < directions; i++)
                    {
                        float angle = 2f * Mathf.PI * i / directions;
                        var end = point + new Vec2(Mathf.Cos(angle), Mathf.Sin(angle)) * length;
                        if (Fits(board, player, DrawingSpec.Forbiddance(0), Placement.Segment(point, end))) return true;
                    }
                    return false;
                case LineType.Vigor:
                    if (_pointA.HasValue && !_pointB.HasValue) return true; // aiming
                    return Fits(board, player, DrawingSpec.Vigor(_tier ?? VigorTier.Low), Placement.Aimed(point, point + board.Facing(player)));
                case LineType.Making:
                    if (ChalklingSnaps && NearBindPoint(board, player, point) >= 0) return true;
                    return Fits(board, player, DrawingSpec.Making(), Placement.At(point));
                default:
                    return true;
            }
        }

        /// <summary>The mouse position, if a click there would be ignored (see <see cref="ClickAllowed"/>).</summary>
        private Vec2? InvalidHover(ChalkBoard board, Player player)
        {
            if (!_hover.HasValue) return null;
            // The menu is laid out several times a frame and the free LoF check tries 16 directions, so keep the answer
            // until the mouse moves or the choice changes.
            Vec2 at = _hover.Value;
            string key = _lineType + "/" + _forbiddanceMode + "/" + _chalklingMode + "/" + _glyph + "/" + _freeHand + "/" + _tier + "/" + _pointA + "/" + _pointB
                + "/" + _bindA + "/" + player + "/" + at.X + "," + at.Y;
            if (key != _invalidHoverKey)
            {
                _invalidHoverKey = key;
                _invalidHoverCached = !ClickAllowed(board, player, at);
            }
            return _invalidHoverCached ? _hover : null;
        }

        private string _invalidHoverKey;
        private bool _invalidHoverCached;

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
            ClearClicks();
            _clickKey = clickKey;
        }

        private void ClearClicks()
        {
            _bindA = -1;
            _bindB = -1;
            _tangentPicked = false;
            _pointA = null;
            _pointB = null;
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
        // The stat box that had keyboard focus last pass, to spot a box just arrived at.
        private static string _focusedStat;

        private static void StatEntry(string name, TypedStat entry, ref int buffs, int baseValue, int perBuff, string nextBox)
        {
            int value = baseValue + buffs * perBuff;
            string control = "stat-" + name;
            bool focused = GUI.GetNameOfFocusedControl() == control;
            if (focused && Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter
                    || Event.current.keyCode == KeyCode.Escape))
            {
                // Enter moves on to the next box; on the last box, or with Esc, it leaves keyboard entry.
                bool enter = Event.current.keyCode != KeyCode.Escape;
                GUI.FocusControl(enter && nextBox != null ? "stat-" + nextBox : null);
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
            if (focused && _focusedStat != control)
            {
                // Unity keeps each box's text scroll from when it was last clicked into; on arriving by Tab that stale
                // offset drew the number outside the box for a frame. Start every visit scrolled to the start.
                var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                editor.scrollOffset = Vector2.zero;
            }
            if (focused) _focusedStat = control;
            else if (_focusedStat == control) _focusedStat = null;
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
            // Scrolling anywhere on the row changes the stat by one step, like scroll changes a drawing's size.
            int scrollStep = 0;
            if (Event.current.type == EventType.ScrollWheel && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            {
                scrollStep = Event.current.delta.y < 0f ? 1 : Event.current.delta.y > 0f ? -1 : 0;
                Event.current.Use();
            }

            int next = Mathf.Clamp(buffs + step + scrollStep, 0, MaxStatBuffs);
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

        /// <summary>
        /// Starts a row with a short label in front, for a row of buttons. The caller draws the buttons and
        /// then calls GUILayout.EndHorizontal.
        /// </summary>
        private static void BeginRow(string label)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(RowLabelWidth));
            _afterRowLabel = true;
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
            int chosen = Buttons(index, labels, NextRowWidth());
            if (chosen == index) return;
            value = (T)System.Enum.ToObject(typeof(T), chosen);
            GUIUtility.ExitGUI();
        }

        /// <summary>A row of buttons with nothing selected until one is clicked.</summary>
        private static void Choose<T>(ref T? value, string[] labels, int unusable = 0, string why = null) where T : struct, System.Enum
        {
            int index = value.HasValue ? System.Convert.ToInt32(value.Value) : -1;
            int chosen = Buttons(index, labels, NextRowWidth(), unusable, why);
            if (chosen == index || chosen < 0 || Unusable(unusable, chosen)) return;
            value = (T)System.Enum.ToObject(typeof(T), chosen);
            GUIUtility.ExitGUI();
        }

        private static void Choose(ref int value, string[] labels, int unusable = 0, string why = null)
        {
            int chosen = Buttons(value, labels, NextRowWidth(), unusable, why);
            if (chosen == value || chosen < 0 || Unusable(unusable, chosen)) return;
            value = chosen;
            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// A row of buttons that shows <paramref name="shown"/> as selected, which may be a choice worked out from the
        /// placement rather than one the player made. Returns the button clicked, or -1 if none (or a greyed-out one).
        /// The caller stores the choice and calls GUIUtility.ExitGUI.
        /// </summary>
        private static int ChooseShown(int shown, string[] labels, int unusable, string why)
        {
            int chosen = Buttons(shown, labels, NextRowWidth(), unusable, why);
            return chosen == shown || chosen < 0 || Unusable(unusable, chosen) ? -1 : chosen;
        }

        /// <summary>Why an option that clashes with what is already placed on the board is greyed out.</summary>
        private const string StepBackReason = "That doesn't fit what you've placed. Right-click or Backspace to place it again.";

        /// <summary>True if button <paramref name="index"/> is greyed out in the <paramref name="unusable"/> mask (one bit per button).</summary>
        private static bool Unusable(int unusable, int index)
        {
            return (unusable & (1 << index)) != 0;
        }

        /// <summary>The smallest font size the button rows shrink to so that they fit on one line.</summary>
        private const int MinButtonFontSize = 8;
        private static GUIStyle _smallButton;
        private static GUIStyle _smallButtonSource;

        /// <summary>
        /// A row of buttons, one selected. The row always stays on one line, at the largest font size that fits in
        /// <paramref name="width"/> (never above the skin's own size). Every button in the row
        /// has the same width, and together they fill it. Only below
        /// MinButtonFontSize does the row wrap onto more lines. Buttons whose bit is set in <paramref name="unusable"/> are greyed out,
        /// and <paramref name="why"/> says why, if a number key tries one. With nothing selected it may be the row the number keys pick in.
        /// </summary>
        private static int Buttons(int selected, string[] labels, float width, int unusable = 0, string why = null)
        {
            bool keyRow;
            int row = KeyRow(selected, out keyRow);
            int keyed = keyRow ? KeyPick(row, labels.Length, unusable, why) : -1;
            if (keyed >= 0) return keyed;

            var style = GUI.skin.button;
            // Copied afresh whenever the skin's button style changes (a new run builds a new skin).
            if (_smallButtonSource != style) { _smallButton = new GUIStyle(style); _smallButtonSource = style; }
            _smallButton.fontSize = style.fontSize;
            // Follow the skin's hover look, which the duel controller turns off while the lines list covers the menu.
            _smallButton.hover.background = style.hover.background;
            _smallButton.hover.textColor = style.hover.textColor;
            _smallButton.onHover.background = style.onHover.background;
            _smallButton.onHover.textColor = style.onHover.textColor;
            float gap = Mathf.Max(style.margin.left, style.margin.right);
            float available = width - gap * (labels.Length - 1);
            if (TotalWidth(_smallButton, labels) > available)
            {
                // The skin's size doesn't fit; its line height bounds the font size from above.
                int top = style.fontSize > 0 ? style.fontSize : Mathf.CeilToInt(style.lineHeight);
                int size = top;
                for (; size > MinButtonFontSize; size--)
                {
                    _smallButton.fontSize = size;
                    if (TotalWidth(_smallButton, labels) <= available) break;
                }
                _smallButton.fontSize = size;
                if (TotalWidth(_smallButton, labels) > available)
                {
                    float widest = 0f;
                    foreach (var label in labels) widest = Mathf.Max(widest, _smallButton.CalcSize(new GUIContent(label)).x);
                    int perRow = Mathf.Clamp(Mathf.FloorToInt((width + gap) / (widest + gap)), 1, labels.Length);
                    int picked = GUILayout.SelectionGrid(selected, labels, perRow, _smallButton, GUILayout.MaxWidth(width));
                    if (Event.current.type == EventType.Repaint)
                    {
                        Rect grid = GUILayoutUtility.GetLastRect();
                        int rows = (labels.Length + perRow - 1) / perRow;
                        float cellWidth = (grid.width - gap * (perRow - 1)) / perRow;
                        float cellHeight = (grid.height - gap * (rows - 1)) / rows;
                        for (int i = 0; i < labels.Length; i++)
                        {
                            var cell = new Rect(grid.x + (i % perRow) * (cellWidth + gap), grid.y + (i / perRow) * (cellHeight + gap), cellWidth, cellHeight);
                            DecorateOption(cell, row, i, keyRow);
                        }
                    }
                    return picked;
                }
            }

            // Every button in the row gets the same share of the width.
            float each = available / labels.Length;
            int chosen = selected;
            GUILayout.BeginHorizontal(GUILayout.MaxWidth(width));
            for (int i = 0; i < labels.Length; i++)
            {
                float w = each;
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && !Unusable(unusable, i);
                bool on = GUILayout.Toggle(i == selected, labels[i], _smallButton, GUILayout.Width(w));
                GUI.enabled = enabled;
                if (Event.current.type == EventType.Repaint) DecorateOption(GUILayoutUtility.GetLastRect(), row, i, keyRow);
                if (on && i != selected) chosen = i;
            }
            GUILayout.EndHorizontal();
            return chosen;
        }

        /// <summary>
        /// Counts a row of options down the menu. Returns its place, and sets <paramref name="keyRow"/> if it is the
        /// row the number keys pick in: the first one this pass with nothing <paramref name="selected"/>.
        /// </summary>
        private static int KeyRow(int selected, out bool keyRow)
        {
            keyRow = !_keyRowFound && selected < 0;
            if (keyRow) _keyRowFound = true;
            return _rowOrdinal++;
        }

        /// <summary>
        /// The option a number key just pressed picks in the key row, or -1. A key for a greyed-out option (or any
        /// option of a greyed-out row) picks nothing: the button flashes red and the problem line says why.
        /// </summary>
        private static int KeyPick(int row, int count, int unusable, string why)
        {
            int option = MenuKeys.PeekOption();
            if (option < 0) return -1;
            MenuKeys.Use();
            if (option >= count) return -1;
            if (GUI.enabled && !Unusable(unusable, option)) return option;
            Flash(row, option, why ?? "That can't be picked right now.");
            GUIUtility.ExitGUI();
            return -1;
        }

        /// <summary>Starts a red flash on an option (row -1: the problem line alone), with the reason to show, if any.</summary>
        private static void Flash(int row, int option, string reason)
        {
            _flashRow = row;
            _flashOption = option;
            _flashStart = Time.realtimeSinceStartup;
            _flashReason = reason;
        }

        /// <summary>How strong the red flash is now: 1 when it starts, fading to 0.</summary>
        private static float FlashStrength()
        {
            return Mathf.Clamp01(1f - (Time.realtimeSinceStartup - _flashStart) / FlashSeconds);
        }

        private static GUIStyle _keyHint;

        /// <summary>
        /// Paints over an option's button: its key's number in the corner if its row is the key row (while the
        /// menu takes keys), and the red flash if a key press for it was just refused. Only call it while repainting.
        /// </summary>
        private static void DecorateOption(Rect rect, int row, int option, bool keyRow)
        {
            if (row == _flashRow && option == _flashOption)
            {
                float strength = FlashStrength();
                if (strength > 0f)
                {
                    var colour = GUI.color;
                    var red = MenuSkin.Refused;
                    GUI.color = new Color(red.r, red.g, red.b, red.a * MenuSkin.RefusedFlashAlpha * strength);
                    MenuSkin.FlashBox.Draw(rect, false, false, false, false);
                    GUI.color = colour;
                }
            }
            string name = keyRow && MenuKeys.Enabled ? MenuKeys.OptionName(option) : null;
            if (name == null) return;
            if (_keyHint == null)
            {
                _keyHint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(4, 0, 1, 0) };
                _keyHint.normal.textColor = MenuSkin.KeyHintText;
            }
            _keyHint.fontSize = Mathf.Max(MinButtonFontSize, Mathf.RoundToInt(GUI.skin.button.fontSize > 0 ? GUI.skin.button.fontSize * 0.7f : GUI.skin.label.lineHeight * 0.7f));
            GUI.Label(rect, name, _keyHint);
        }

        /// <summary>
        /// The width these labels need side by side in this style, gaps not included. The buttons share one
        /// width, so it is the widest label times the count.
        /// </summary>
        private static float TotalWidth(GUIStyle style, string[] labels)
        {
            float widest = 0f;
            foreach (var label in labels) widest = Mathf.Max(widest, style.CalcSize(new GUIContent(label)).x);
            return widest * labels.Length;
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
