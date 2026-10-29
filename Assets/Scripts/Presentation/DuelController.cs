using System.Collections.Generic;
using RithmaticDuels.Rules;
using RithmaticDuels.Rules.Geometry;
using UnityEngine;

namespace RithmaticDuels.Presentation
{
    /// <summary>
    /// Scene entry point for a duel. Presentation only: it gathers player input and
    /// draws the board, and calls into RithmaticDuels.Rules for every game rule.
    ///
    /// A hot-seat duel: two players share one screen and choose in turn while the other looks
    /// away. Menus and the log use Unity's immediate-mode GUI (OnGUI), the board is drawn by
    /// <see cref="BoardRenderer"/>, and new drawings are placed by clicking the board. No scene
    /// setup is needed.
    /// </summary>
    public sealed class DuelController : MonoBehaviour
    {
        [Tooltip("Every rules number. All placeholders: change them here to playtest balance.")]
        [SerializeField] private RulesConfig _rules = new RulesConfig();

        [Tooltip("Seed for modifier rolls, so a duel can be replayed. 0 picks a new seed each duel.")]
        [SerializeField] private int _seed = 0;

        [Tooltip("Frames per second to aim for. Without a cap Unity redraws the game as fast as it can, which slows the editor down.")]
        [SerializeField] private int _targetFrameRate = 60;

        private enum Phase
        {
            /// <summary>Waiting for the next player to take the screen, so nobody sees the other's choice.</summary>
            Handover,
            Choosing,
            Results,
        }

        private static readonly Color Chalkboard = new Color(0.12f, 0.2f, 0.16f);

        // Screen layout: menus down the whole left side, board on the right, log below the board. The menu is a fixed
        // width in GUI pixels, so its text wraps the same way on any window shape; LogTop is a fraction of the height.
        private const float MenuWidth = 470f;
        private const float LogTop = 0.72f;
        private const float HeaderHeight = 34f;

        private Duel _duel;
        private ChalkBoard _board;
        private Camera _camera;
        private BoardRenderer _renderer;
        private GUIStyle _statsStyle;
        private ulong _currentSeed;
        private Phase _phase;
        private Player _chooser;
        private LockedChoice _lockedA;
        // Replaced with a fresh picker each time a player starts choosing, so no choice carries over.
        private readonly ActionPicker[] _pickers = { new ActionPicker(), new ActionPicker() };
        private readonly List<string> _log = new List<string>();
        private Vector2 _logScroll;
        // Each log line's height, measured once for the width they were measured at (see DrawLog).
        private readonly List<float> _logHeights = new List<float>();
        private float _logHeightsWidth = -1f;
        private Vector2 _menuScroll;
        /// <summary>The menu area's width this frame, so the picker can wrap its button rows to fit.</summary>
        private float _menuContentWidth = 400f;
        private string _error;
        /// <summary>Tab shows every current line of both players over the menu, at any point, until Tab, Esc or a right-click closes it (Tab while typing in a LoM stat box moves between boxes instead).</summary>
        private bool _showLines;
        private Vector2 _linesScroll;
        /// <summary>The line under the mouse on the board, whose entry the lines list picks out. -1 for none.</summary>
        private int _boardHoverLineId = -1;
        /// <summary>The line whose entry in a lines list is under the mouse, highlighted on the board. -1 for none.</summary>
        private int _listHoverLineId = -1;
        /// <summary>Whether the line info key is held, read each OnGUI so LateUpdate can light up the hovered line.</summary>
        private bool _lineInfoHeld;

        /// <summary>
        /// The line info key: hold it and hover a line to see its stats box and highlight. Kept here alone so it
        /// can be rebound in settings later. Alt is the Option key on macOS, which Unity also reports as alt.
        /// </summary>
        private static bool LineInfoHeld(Event e) { return e.alt; }

        /// <summary>
        /// Lets you press Play in any scene, including a new empty one. To change numbers in the
        /// Inspector instead, add a Duel Controller component to an object in a scene yourself.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfMissing()
        {
            if (FindAnyObjectByType<DuelController>() == null)
                new GameObject("Duel").AddComponent<DuelController>();
        }

        private void Start()
        {
            // The editor ignores vSync in the Game view, so cap the frame rate directly.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = _targetFrameRate;

            _camera = Camera.main;
            if (_camera == null) _camera = new GameObject("Main Camera").AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Chalkboard;
            _camera.depth = 0f;

            // The board camera only covers part of the screen, so a second camera clears the rest.
            var backdrop = new GameObject("Backdrop Camera").AddComponent<Camera>();
            backdrop.clearFlags = CameraClearFlags.SolidColor;
            backdrop.backgroundColor = Chalkboard * 0.8f;
            backdrop.cullingMask = 0;
            backdrop.depth = -10f;

            _renderer = gameObject.AddComponent<BoardRenderer>();
            NewDuel();
        }

        private void NewDuel()
        {
            // The hot-seat duel has no decision timer (DESIGN.md section 2).
            _rules.DecisionTimerSeconds = 0f;
            _currentSeed = _seed != 0 ? (ulong)_seed : (ulong)UnityEngine.Random.Range(1, int.MaxValue);
            _board = new ChalkBoard(_rules);
            _error = null;
            try
            {
                _duel = new Duel(_rules, _currentSeed, _board);
            }
            catch (System.ArgumentException e)
            {
                _duel = null;
                _error = e.Message;
                return;
            }
            _log.Clear();
            _log.Add("New duel (seed " + _currentSeed + "). Both players start by setting up a main Line of Warding.");
            _chooser = Player.A;
            _phase = Phase.Handover;
        }

        private void LateUpdate()
        {
            if (_camera == null || _renderer == null) return;
            PlaceCamera();
            _renderer.Board = _board;
            _renderer.ChalklingRadius = _rules.Making.Radius;
            _renderer.BoardCamera = _camera;
            _renderer.Preview = _phase == Phase.Choosing && _duel != null ? _pickers[(int)_chooser].Preview : null;
            int dismissHover = _phase == Phase.Choosing && _duel != null && !_showLines ? _pickers[(int)_chooser].HoveredLineId : -1;
            // While the list is open, a line hovered on the board lights up too, as well as its entry.
            // Holding the line info key lights up a hovered line the same way.
            int boardHover = _showLines || _lineInfoHeld ? _boardHoverLineId : -1;
            _renderer.HighlightLineId = _listHoverLineId >= 0 ? _listHoverLineId : boardHover >= 0 ? boardHover : dismissHover;
        }

        /// <summary>Fit the whole board into the area right of the menus and above the log.</summary>
        private void PlaceCamera()
        {
            float left = MenuFraction();
            float bottom = 1f - LogTop;
            float top = HeaderHeight * GuiScale() / Mathf.Max(1, Screen.height);
            _camera.rect = new Rect(left, bottom, 1f - left, Mathf.Max(0.1f, 1f - bottom - top));

            float width = _rules.Board.Width + 1f;
            float height = _rules.Board.Height + 1f;
            float aspect = _camera.pixelRect.width / Mathf.Max(1f, _camera.pixelRect.height);
            _camera.orthographicSize = Mathf.Max(height / 2f, width / 2f / Mathf.Max(0.01f, aspect));
            _camera.transform.position = new Vector3(_rules.Board.Width / 2f, _rules.Board.Height / 2f, -10f);
        }

        private static float GuiScale()
        {
            return Mathf.Max(1f, Screen.height / 720f);
        }

        /// <summary>The menu's share of the screen width. On a very narrow window it gives up some width to the board.</summary>
        private static float MenuFraction()
        {
            float guiWidth = Screen.width / GuiScale();
            return Mathf.Min(0.5f, MenuWidth / Mathf.Max(1f, guiWidth));
        }

        private void OnGUI()
        {
            // Unity resets the skin every OnGUI, so set ours each time (it restyles the scroll bars).
            GUI.skin = MenuSkin.Get();
            // Read the click in screen pixels before scaling the GUI.
            GUI.matrix = Matrix4x4.identity;
            var current = Event.current;
            // Unity reports mousePosition in the current GUI.matrix space, so save it now, in real pixels.
            Vector2 mousePixels = current.mousePosition;
            Vec2? boardPoint = BoardPoint(mousePixels);
            // Lists find the hovered entry while painting, so start each paint with none.
            if (current.type == EventType.Repaint) _listHoverLineId = -1;
            var boardHovered = boardPoint.HasValue && _board != null ? _board.BodyAt(boardPoint.Value, 0.3f) : null;
            _boardHoverLineId = boardHovered != null ? boardHovered.LineId : -1;
            _lineInfoHeld = LineInfoHeld(current);
            bool editingStat = GUI.GetNameOfFocusedControl().StartsWith("stat-");
            // Unity sends a key press twice, once with its key code and once with its character. Its own Tab
            // handling moves focus into a stat box on the '\t' character event, so swallow that too unless the
            // player is typing in a box, where Tab and Shift+Tab move between the boxes.
            if (_duel != null && current.type == EventType.KeyDown && current.keyCode == KeyCode.None && current.character == '\t' && !editingStat)
                current.Use();
            if (_duel != null && current.type == EventType.KeyDown && current.keyCode == KeyCode.Tab && !editingStat)
            {
                _showLines = !_showLines;
                current.Use();
                GUIUtility.ExitGUI();
            }
            // Esc closes the lines list.
            if (_duel != null && _showLines && current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                _showLines = false;
                current.Use();
                GUIUtility.ExitGUI();
            }
            // Clicking the board leaves a LoM stat box, so Tab goes back to opening the lines list.
            if (boardPoint.HasValue && current.type == EventType.MouseDown && GUI.GetNameOfFocusedControl().StartsWith("stat-")) GUI.FocusControl(null);
            if (_duel != null && _showLines)
            {
                // The list covers the menu, so board clicks and scrolls do nothing; a right-click closes the list.
                if (current.type == EventType.MouseDown && current.button == 1)
                {
                    _showLines = false;
                    current.Use();
                    GUIUtility.ExitGUI();
                }
                if (boardPoint.HasValue && (current.type == EventType.MouseDown || current.type == EventType.ScrollWheel)) current.Use();
            }
            else if (_duel != null && _phase == Phase.Choosing)
            {
                var picker = _pickers[(int)_chooser];
                // Holding the free hand key (Ctrl, or Cmd on macOS) stops snapping to bind points (DESIGN.md 1.1).
                picker.SetHover(boardPoint, ActionPicker.FreeHandIsCommand ? current.command : current.control);
                bool changed = false;
                if (boardPoint.HasValue && current.type == EventType.MouseDown && current.button == 0)
                    changed = picker.HandleBoardClick(_board, _chooser, _rules, boardPoint.Value);
                else if (current.type == EventType.MouseDown && current.button == 1)
                {
                    picker.HandleRightClick(_board, _chooser, _rules);
                    // A right-click anywhere steps back. Always take it, so no button treats it as a left-click.
                    changed = true;
                }
                else if (boardPoint.HasValue && current.type == EventType.ScrollWheel)
                {
                    // Shift + scroll changes an attached ellipse's roundness or turns a circle. Some platforms turn Shift + scroll into
                    // a sideways scroll, so fall back to the horizontal delta.
                    float delta = current.shift && Mathf.Abs(current.delta.y) < 1e-6f ? current.delta.x : current.delta.y;
                    changed = picker.HandleScroll(_board, _chooser, _rules, delta, current.shift);
                }
                if (changed)
                {
                    current.Use();
                    GUIUtility.ExitGUI();
                }
            }

            // Scale everything so the GUI is readable at any resolution; lay out in a 1280x720-ish space.
            float scale = GuiScale();
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            GUILayout.BeginArea(new Rect(10, 6, width - 20, HeaderHeight - 6));
            // Ended in finally, like the menu below, since New duel ends the pass with ExitGUI.
            try
            {
                DrawHeader();
            }
            finally
            {
                GUILayout.EndArea();
            }

            if (_duel == null)
            {
                GUILayout.BeginArea(new Rect(10, HeaderHeight, width - 20, height - HeaderHeight));
                GUILayout.Label("Can't start a duel: " + _error);
                GUILayout.EndArea();
                return;
            }

            float logTop = height * LogTop;
            float menuWidth = width * MenuFraction();
            _menuContentWidth = menuWidth - 20;
            var menuRect = new Rect(10, HeaderHeight, menuWidth - 20, height - HeaderHeight - 10);
            // Under the lines list the menu shows through but takes no mouse input, so a click can't reach a hidden button.
            // The mouse is also moved off screen for the menu, so its buttons don't light up on hover.
            EventType realType = current.type;
            bool blocked = _showLines && (current.isMouse || realType == EventType.ScrollWheel);
            if (blocked) current.type = EventType.Used;
            Vector2 realMouse = current.mousePosition;
            if (_showLines) current.mousePosition = new Vector2(-10000f, -10000f);
            // Unity lights buttons up from its own record of the mouse, so also draw them with no hover look.
            // Assigning a GUIStyleState copies its contents rather than the reference, so save the hover looks' values to put back.
            var buttonStyle = GUI.skin.button;
            Texture2D hoverBack = buttonStyle.hover.background, onHoverBack = buttonStyle.onHover.background;
            Color hoverText = buttonStyle.hover.textColor, onHoverText = buttonStyle.onHover.textColor;
            if (_showLines)
            {
                buttonStyle.hover.background = buttonStyle.normal.background;
                buttonStyle.hover.textColor = buttonStyle.normal.textColor;
                buttonStyle.onHover.background = buttonStyle.onNormal.background;
                buttonStyle.onHover.textColor = buttonStyle.onNormal.textColor;
            }
            GUILayout.BeginArea(menuRect);
            // Ended in finally so a click's ExitGUI can't leave the area's clip pushed.
            try
            {
                if (_phase == Phase.Choosing)
                {
                    // The picker scrolls its own options (only if they don't fit) and keeps Lock in at the bottom.
                    DrawChoosing();
                }
                else
                {
                    _menuScroll = GUILayout.BeginScrollView(_menuScroll);
                    // Ended in finally: the buttons here end the pass with ExitGUI.
                    try
                    {
                        if (_phase == Phase.Handover) DrawHandover();
                        else DrawResults();
                    }
                    finally
                    {
                        GUILayout.EndScrollView();
                    }
                }
                if (_error != null) GUILayout.Label(_error);
            }
            finally
            {
                GUILayout.EndArea();
            }
            // Restore only what was blocked here: a menu button that used the click must leave it used.
            if (blocked) current.type = realType;
            if (_showLines) current.mousePosition = realMouse;
            buttonStyle.hover.background = hoverBack;
            buttonStyle.hover.textColor = hoverText;
            buttonStyle.onHover.background = onHoverBack;
            buttonStyle.onHover.textColor = onHoverText;
            if (_showLines) DrawLinesOverlay(menuRect);

            GUILayout.BeginArea(new Rect(menuWidth, HeaderHeight, width - menuWidth - 10, 24));
            DrawWardStatus();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(menuWidth, logTop + 4, width - menuWidth - 10, height - logTop - 10));
            DrawLog(width - menuWidth - 10, height - logTop - 10);
            GUILayout.EndArea();

            if (_renderer != null) { _renderer.HoverWardPoint = null; _renderer.HoverAimFrom = null; _renderer.HoverAimDirection = null; _renderer.HoverChalkling = null; }
            // Stats for a line on the board only show while the line info key is held.
            if (boardPoint.HasValue && _lineInfoHeld)
            {
                var hovered = _board.BodyAt(boardPoint.Value, 0.3f);
                if (hovered != null) DrawLineStats(hovered, boardPoint, mousePixels / scale, width, height);
                else
                {
                    var sketch = SketchAt(boardPoint.Value, 0.3f);
                    if (sketch != null) DrawSketchStats(sketch, mousePixels / scale, width, height);
                }
            }
            else if (_listHoverLineId >= 0)
            {
                // Hovering an entry in a lines list shows the same stats box as hovering the line itself.
                var listed = _board.BodyOf(_listHoverLineId);
                if (listed != null) DrawLineStats(listed, null, mousePixels / scale, width, height);
            }
            if (current.type == EventType.Repaint) UpdateInvalidCursor();
        }

        private Texture2D _crossCursor;
        private int _crossSize;
        private bool _crossShown;

        /// <summary>How wide the red cross is, in board units, so it matches marks drawn on the board (a chalkling's diamond).</summary>
        private const float CrossBoardSize = 0.5f;
        /// <summary>Pointer sizes the operating system reliably accepts; outside these the cross stops scaling.</summary>
        private const int MinCrossPixels = 16;
        private const int MaxCrossPixels = 128;

        /// <summary>
        /// Where nothing can be placed (DESIGN.md section 1.1), the mouse pointer itself turns into a red cross. Using
        /// the system cursor means it moves exactly with the mouse, with no lag or jitter at any frame rate.
        /// </summary>
        private void UpdateInvalidCursor()
        {
            var preview = _phase == Phase.Choosing && _duel != null ? _pickers[(int)_chooser].Preview : null;
            bool show = !_showLines && preview != null && preview.InvalidHover.HasValue;
            // The cross is sized to the board, so rebuild it when the window (and so the board's scale) changes.
            int size = show ? CrossPixels() : _crossSize;
            if (show == _crossShown && size == _crossSize) return;
            _crossShown = show;
            if (show && (_crossCursor == null || size != _crossSize))
            {
                if (_crossCursor != null) Destroy(_crossCursor);
                _crossCursor = MakeCrossCursor(size);
                _crossSize = size;
            }
            Cursor.SetCursor(show ? _crossCursor : null, new Vector2(_crossSize / 2f, _crossSize / 2f), CursorMode.Auto);
        }

        /// <summary>The cross's width in screen pixels: <see cref="CrossBoardSize"/> board units at the board's current scale.</summary>
        private int CrossPixels()
        {
            if (_camera == null || _camera.orthographicSize <= 0f) return 32;
            float pixelsPerUnit = _camera.pixelRect.height / (2f * _camera.orthographicSize);
            int size = Mathf.RoundToInt(CrossBoardSize * pixelsPerUnit);
            // Even sizes keep the hotspot on the cross's centre.
            size += size % 2;
            return Mathf.Clamp(size, MinCrossPixels, MaxCrossPixels);
        }

        private void OnDisable()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            _crossShown = false;
        }

        /// <summary>A red cross with a dark outline, so it shows on both the board and the menus.</summary>
        private static Texture2D MakeCrossCursor(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var red = new Color(0.95f, 0.3f, 0.25f, 1f);
            var outline = new Color(0f, 0f, 0f, 0.8f);
            // Stroke and margins scale with the size; at 32 pixels they are 1.6 and 2.8 pixels wide, 4 and 3 in from the edge.
            float stroke = size * 0.05f;
            float edge = size * 0.0875f;
            int margin = Mathf.Max(1, Mathf.RoundToInt(size * 0.125f));
            int outerMargin = Mathf.Max(0, margin - Mathf.Max(1, Mathf.RoundToInt(size / 32f)));
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance from either diagonal, in pixels.
                    float d = Mathf.Min(Mathf.Abs(x - y), Mathf.Abs(x + y - (size - 1))) / 1.41421f;
                    bool inside = x >= margin && x < size - margin && y >= margin && y < size - margin;
                    Color c = Color.clear;
                    if (inside && d <= stroke) c = red;
                    else if (x >= outerMargin && x < size - outerMargin && y >= outerMargin && y < size - outerMargin && d <= edge) c = outline;
                    texture.SetPixel(x, y, c);
                }
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Hovering over a line on the board shows its stats next to the mouse, on an opaque panel. It is drawn last
        /// in OnGUI so it sits on top of the menus. Near the edge of the screen it flips to the other side of the pointer.
        /// </summary>
        /// <remarks><paramref name="boardPoint"/> is null when the line is hovered in a list rather than on the board.</remarks>
        private void DrawLineStats(LineBody body, Vec2? boardPoint, Vector2 mouse, float screenWidth, float screenHeight)
        {
            var line = body.Line;
            string text = TurnNarrator.LineName(line);
            var ward = body as WardBody;
            if (ward != null && boardPoint.HasValue)
            {
                // A LoW's durability varies around its outline, so show the point nearest the mouse.
                int nearest = NearestSample(ward, boardPoint.Value);
                if (_renderer != null) _renderer.HoverWardPoint = ward.SamplePoint(nearest);
                text += "\nDurability here " + ward.SampleDurability(nearest) + "/" + ward.SampleMax(nearest);
            }
            else if (line.HasDurability) text += (ward != null ? "\nLowest durability " : "\nDurability ") + line.Durability + "/" + line.MaxDurability;
            var vigor = body as VigorBody;
            if (_renderer != null && boardPoint.HasValue && vigor != null)
            {
                _renderer.HoverAimFrom = vigor.Position;
                _renderer.HoverAimDirection = vigor.Direction;
                _renderer.HoverAimOwner = vigor.Owner;
            }
            if (line.Type == LineType.Vigor) text += "\n" + line.Tier + " tier, speed " + line.Speed + "\nDamage on a hit: " + line.Damage;
            if (_renderer != null && body is ChalklingBody) _renderer.HoverChalkling = (ChalklingBody)body;
            if (line.Type == LineType.Making) text += "\nDamage " + line.Damage + ", speed " + line.Speed + ", " + line.Glyph + " glyph";
            if (line.ModifierSteps != 0) text += "\nModifier " + (line.ModifierSteps > 0 ? "+" : "") + line.ModifierSteps;
            if (ward != null && ward.BindPoints.Count > 0) text += "\n" + ward.BindPoints.Count + " bind points";
            DrawStatsBox(text, mouse, screenWidth, screenHeight);
        }

        /// <summary>
        /// Hovering a drawing still in progress shows what its owner may see of it: everything for the player
        /// choosing now, and for the other player only what the board shows (DESIGN.md section 2, Information).
        /// </summary>
        private void DrawSketchStats(WorkSketch sketch, Vector2 mouse, float screenWidth, float screenHeight)
        {
            string what = sketch.Kind == WorkKind.BindPoints ? "bind points" : TurnNarrator.LineTypeName(sketch.Type);
            string text = TurnNarrator.PlayerName(sketch.Owner) + "'s " + what + " (unfinished)";
            bool own = _phase == Phase.Choosing && sketch.Owner == _chooser;
            text += "\n" + DescribeWork(sketch.Owner, own);
            if (_renderer != null && sketch.Type == LineType.Vigor && sketch.Point.HasValue && sketch.Direction.HasValue)
            {
                _renderer.HoverAimFrom = sketch.Point.Value;
                _renderer.HoverAimDirection = sketch.Direction.Value.Normalized;
                _renderer.HoverAimOwner = sketch.Owner;
            }
            DrawStatsBox(text, mouse, screenWidth, screenHeight);
        }

        /// <summary>The drawing in progress whose picture is within <paramref name="within"/> of a point, if any.</summary>
        private WorkSketch SketchAt(Vec2 point, float within)
        {
            for (int p = 0; p < 2; p++)
            {
                var sketch = _board.WorkInProgressOf((Player)p);
                if (sketch == null) continue;
                float distance = float.MaxValue;
                if (sketch.Kind == WorkKind.BindPoints || (sketch.Kind == WorkKind.Drawing && sketch.Type == LineType.Warding))
                    if (sketch.BindPoints != null)
                        foreach (var bind in sketch.BindPoints) distance = Mathf.Min(distance, Vec2.Distance(bind, point));
                if (sketch.Kind == WorkKind.Drawing)
                {
                    if (sketch.Ward != null) distance = Mathf.Min(distance, Vec2.Distance(sketch.Ward.PointAt(sketch.Ward.ParamOf(point)), point));
                    if (sketch.Segment.HasValue) distance = Mathf.Min(distance, DistanceToSegment(point, sketch.Segment.Value));
                    if (sketch.Point.HasValue)
                    {
                        if (sketch.Type == LineType.Vigor && sketch.Direction.HasValue)
                        {
                            // The wave is drawn behind its start point.
                            Vec2 back = sketch.Point.Value - sketch.Direction.Value.Normalized * _rules.Geometry.VigorDrawnLength;
                            distance = Mathf.Min(distance, DistanceToSegment(point, new Segment(back, sketch.Point.Value)));
                        }
                        else distance = Mathf.Min(distance, Vec2.Distance(sketch.Point.Value, point) - 0.25f);
                    }
                }
                if (distance <= within) return sketch;
            }
            return null;
        }

        private static float DistanceToSegment(Vec2 point, Segment segment)
        {
            Vec2 along = segment.B - segment.A;
            float lengthSquared = along.LengthSquared;
            float t = lengthSquared <= 0f ? 0f : Mathf.Clamp01(Vec2.Dot(point - segment.A, along) / lengthSquared);
            return Vec2.Distance(segment.A + along * t, point);
        }

        /// <summary>
        /// The hover readout: an opaque panel next to the mouse, drawn last in OnGUI so it sits on top of the menus.
        /// Near the edge of the screen it flips to the other side of the pointer.
        /// </summary>
        private void DrawStatsBox(string text, Vector2 mouse, float screenWidth, float screenHeight)
        {
            if (_statsStyle == null)
            {
                _statsStyle = new GUIStyle(GUI.skin.label) { wordWrap = false, padding = new RectOffset(8, 8, 6, 6) };
                _statsStyle.normal.textColor = Color.white;
            }
            var content = new GUIContent(text);
            Vector2 size = _statsStyle.CalcSize(content);

            // Just below and right of the pointer, flipped left or up if it would leave the screen.
            float x = mouse.x + 14f;
            float y = mouse.y + 14f;
            if (x + size.x > screenWidth - 4f) x = mouse.x - 10f - size.x;
            if (y + size.y > screenHeight - 4f) y = mouse.y - 10f - size.y;
            var rect = new Rect(Mathf.Max(4f, x), Mathf.Max(4f, y), size.x, size.y);

            Color oldColour = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.06f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            DrawOutline(rect);
            GUI.color = oldColour;
            GUI.Label(rect, content, _statsStyle);
        }

        private static int NearestSample(WardBody ward, Vec2 point)
        {
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < ward.SampleCount; i++)
            {
                float distance = Vec2.Distance(ward.SamplePoint(i), point);
                if (distance < best) { best = distance; nearest = i; }
            }
            return nearest;
        }

        private static void DrawOutline(Rect rect)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), Texture2D.whiteTexture);
        }

        /// <summary>The board point under a position in GUI pixels (y down), or null if it is off the board view.</summary>
        private Vec2? BoardPoint(Vector2 guiPixels)
        {
            if (_camera == null) return null;
            var screen = new Vector3(guiPixels.x, Screen.height - guiPixels.y, 0f);
            if (!_camera.pixelRect.Contains(screen)) return null;
            var world = _camera.ScreenToWorldPoint(screen);
            return new Vec2(world.x, world.y);
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            if (_duel != null)
            {
                string wall = _board.CentreWallUp ? "   ·   centre wall up until turn " + _duel.Config.CentreWall.Turns : "";
                GUILayout.Label("Turn " + (_duel.Turn + 1) + " of " + _duel.Config.TurnLimit + wall
                    + (_duel.ChalkFatigueActive ? "   ·   CHALK FATIGUE" : "") + "   ·   seed " + _currentSeed);
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(GameVersion.Label);
            if (GUILayout.Button("New duel", GUILayout.Width(100))) Then(NewDuel);
            GUILayout.EndHorizontal();
        }

        private void DrawWardStatus()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("A (white, left): " + WardStatus(Player.A));
            GUILayout.FlexibleSpace();
            GUILayout.Label("B (yellow, right): " + WardStatus(Player.B));
            GUILayout.EndHorizontal();
        }

        private string WardStatus(Player player)
        {
            var ward = _duel.GetPlayer(player).MainWard;
            if (ward == null) return "no main LoW yet";
            return ward.IsActive ? "main LoW " + ward.Durability + "/" + ward.MaxDurability : "BREACHED";
        }

        private void DrawHandover()
        {
            GUILayout.Label(TurnNarrator.PlayerName(_chooser) + ", it's your turn to choose. The other player should look away.");
            if (GUILayout.Button("I'm " + TurnNarrator.PlayerName(_chooser) + ", show my choices", GUILayout.Height(50)))
                Then(() =>
                {
                    _pickers[(int)_chooser] = new ActionPicker();
                    _showLines = false;
                    _menuScroll = Vector2.zero;
                    _phase = Phase.Choosing;
                });
        }

        private void DrawChoosing()
        {
            GUILayout.Label(TurnNarrator.PlayerName(_chooser) + ". " + DescribeWork(_chooser, true));
            GUILayout.Label("Opponent: " + DescribeWork(_chooser.Opponent(), false));
            GUILayout.Label("Tab: show all current lines. Hold Alt over a line for its stats.");
            var choice = _pickers[(int)_chooser].Draw(_duel, _board, _chooser, ref _menuScroll, _menuContentWidth);
            if (choice != null) Then(() => Lock(choice));
        }

        private void Lock(LockedChoice choice)
        {
            if (choice.Action.Kind == ActionKind.StartDrawing)
                _board.SetPendingPlacement(_chooser, choice.Action.Spec, choice.Placement);

            if (_chooser == Player.A)
            {
                _lockedA = choice;
                _chooser = Player.B;
                _phase = Phase.Handover;
                return;
            }

            try
            {
                var report = _duel.ResolveTurn(_lockedA.Action, choice.Action);
                _error = null;
                _log.Add("— Turn " + report.Turn + " —");
                _log.AddRange(TurnNarrator.Describe(_duel, report, _board.LastTickNotes));
                _logScroll.y = float.MaxValue;
            }
            catch (System.InvalidOperationException e)
            {
                // Shouldn't happen, since the picker only locks legal actions; start the turn over if it does.
                _error = e.Message;
                _chooser = Player.A;
                _phase = Phase.Handover;
                return;
            }
            _menuScroll = Vector2.zero;
            _phase = Phase.Results;
        }

        private void DrawResults()
        {
            if (_duel.Outcome != DuelOutcome.InProgress)
            {
                GUILayout.Label(TurnNarrator.OutcomeName(_duel.Outcome));
                if (GUILayout.Button("Play again", GUILayout.Height(40))) Then(NewDuel);
            }
            else if (GUILayout.Button("Next turn", GUILayout.Height(40)))
            {
                Then(() =>
                {
                    _chooser = Player.A;
                    _phase = Phase.Handover;
                });
            }

            GUILayout.Space(6);
            GUILayout.Label("Trails show where each Line of Vigor went this turn.");
            GUILayout.Label("Tab: show all current lines. Hold Alt over a line for its stats.");
        }

        /// <summary>The lines list, on a translucent panel over the menu. It scrolls like the log if it gets long.</summary>
        private void DrawLinesOverlay(Rect area)
        {
            Color oldColour = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.06f, 0.9f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            DrawOutline(area);
            GUI.color = oldColour;
            GUILayout.BeginArea(new Rect(area.x + 6, area.y + 4, area.width - 12, area.height - 8));
            GUILayout.Label("All current lines. Hover one to find it on the board. Tab or right-click to close.");
            _linesScroll = GUILayout.BeginScrollView(_linesScroll);
            DrawLinesList();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Every active line of both players by name, with what each is drawing. Hovering an entry highlights that line
        /// on the board and shows its stats box, the same way as hovering a target in the Dismiss LoF menu.
        /// </summary>
        private void DrawLinesList()
        {
            foreach (var player in new[] { Player.A, Player.B })
            {
                GUILayout.Label(TurnNarrator.PlayerName(player) + ": " + DescribeWork(player, false));
                foreach (var line in _duel.Lines)
                {
                    if (line.Owner != player || !line.IsActive) continue;
                    // The hovered entry, or the entry for the line hovered on the board, gets the same light grey
                    // background as a hovered menu button, so the highlight works both ways.
                    var content = new GUIContent("    " + TurnNarrator.LineName(line));
                    Rect rect = GUILayoutUtility.GetRect(content, GUI.skin.label);
                    if (Event.current.type == EventType.Repaint && (line.Id == _boardHoverLineId || rect.Contains(Event.current.mousePosition)))
                    {
                        var hover = GUI.skin.button.hover.background;
                        if (hover != null) GUI.DrawTexture(rect, hover);
                    }
                    GUI.Label(rect, content);
                    if (Event.current.type == EventType.Repaint && rect.Contains(Event.current.mousePosition))
                        _listHoverLineId = line.Id;
                }
            }
        }

        /// <summary>A player's work in progress. Only shown in full to the player doing it.</summary>
        private string DescribeWork(Player player, bool showDetails)
        {
            var work = _duel.GetPlayer(player).Work;
            if (work == null) return "Working on: nothing";

            if (!showDetails)
            {
                var view = _duel.ViewWorkOf(player);
                switch (view.Kind)
                {
                    case WorkKind.Drawing: return "Drawing: a " + TurnNarrator.LineTypeName(view.LineType);
                    case WorkKind.BindPoints: return "Drawing bind points";
                    default: return view.Kind + ": " + TurnNarrator.LineName(_duel.FindLine(view.TargetLineId));
                }
            }

            string what = work.Kind == WorkKind.BindPoints ? work.Amount + " bind points"
                : work.Kind == WorkKind.Drawing
                ? TurnNarrator.LineTypeName(work.Spec.Type) + " (" + SpecSummary(work.Spec) + ", " + work.Pace + ")"
                : work.Kind + " " + TurnNarrator.LineName(_duel.FindLine(work.TargetLineId)) + " by " + work.Amount;
            return "Working on: " + what + ", " + work.Progress + " of " + work.TotalTurns + " turns done";
        }

        private static string SpecSummary(DrawingSpec spec)
        {
            switch (spec.Type)
            {
                case LineType.Warding: return spec.IsMarksCross ? "Mark's Cross" : spec.BindPoints + " bind points";
                case LineType.Forbiddance: return spec.BindPoints + " bind points";
                case LineType.Vigor: return spec.Tier + " tier";
                default:
                    return spec.DurabilityBuffs + "/" + spec.DamageBuffs + "/" + spec.SpeedBuffs
                        + " durability/damage/speed buffs, " + spec.Glyph + " glyph";
            }
        }

        /// <summary>
        /// Applies a click that changes what is on screen, then stops this GUI pass, since the rest of
        /// it was laid out for the old screen (see <see cref="ActionPicker"/>).
        /// </summary>
        private static void Then(System.Action change)
        {
            change();
            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// The log of every turn so far. It grows all duel, and laying out every line on every GUI event got slow,
        /// so each line is measured once and only the lines in view are drawn.
        /// </summary>
        private void DrawLog(float width, float height)
        {
            var style = GUI.skin.label;
            // Room for the box's padding and the scroll bar.
            float textWidth = Mathf.Max(50f, width - 40f);
            if (Mathf.Abs(textWidth - _logHeightsWidth) > 0.5f)
            {
                _logHeights.Clear();
                _logHeightsWidth = textWidth;
            }
            if (_logHeights.Count > _log.Count) _logHeights.Clear(); // a new duel cleared the log
            for (int i = _logHeights.Count; i < _log.Count; i++) _logHeights.Add(style.CalcHeight(new GUIContent(_log[i]), textWidth));

            float total = 0f;
            foreach (float lineHeight in _logHeights) total += lineHeight + LogLineGap;

            _logScroll = GUILayout.BeginScrollView(_logScroll, "box");
            Rect content = GUILayoutUtility.GetRect(0f, total, GUILayout.ExpandWidth(true));
            // Scrolling to the end sets the position past it, so clamp it here to find the lines in view.
            float viewHeight = height - 8f;
            float top = Mathf.Min(_logScroll.y, Mathf.Max(0f, total - viewHeight));
            float y = 0f;
            for (int i = 0; i < _log.Count && y < top + viewHeight; i++)
            {
                float lineHeight = _logHeights[i];
                if (y + lineHeight >= top)
                    GUI.Label(new Rect(content.x, content.y + y, content.width, lineHeight), _log[i], style);
                y += lineHeight + LogLineGap;
            }
            GUILayout.EndScrollView();
        }

        /// <summary>Space between log lines, in GUI pixels.</summary>
        private const float LogLineGap = 4f;
    }
}
