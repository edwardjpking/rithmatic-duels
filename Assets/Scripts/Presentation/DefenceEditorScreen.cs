using System.Collections.Generic;
using RithmaticDuels.Rules;
using RithmaticDuels.Rules.Geometry;
using RithmaticDuels.Rules.Presets;
using UnityEngine;

namespace RithmaticDuels.Presentation
{
    /// <summary>
    /// The defence editor (DESIGN.md section 6.8): a screen of its own where a player builds a saved defence on a
    /// practice board with the duel's placement menu, drags its parts into build order (never above their parent)
    /// and saves it. Until the main menu exists (build step 6) it is opened from the duel screen's top bar and
    /// "Back to duel" returns there. It borrows the duel's camera and board renderer while open.
    /// </summary>
    public sealed class DefenceEditorScreen : MonoBehaviour
    {
        // The same layout as the duel screen: menu on the left, board on the right.
        private const float MenuWidth = 470f;
        private const float HeaderHeight = 34f;
        private const float RowHeight = 26f;
        /// <summary>How far the mouse must move with the button down before a press on a part becomes a drag, in GUI pixels.</summary>
        private const float DragStart = 4f;
        private const string NameField = "defence-name";

        private DuelController _duelScreen;
        private RulesConfig _rules;
        private Camera _camera;
        private BoardRenderer _renderer;

        private PresetLibrary _library;
        private DefenceEditorSession _session;
        /// <summary>The saved defence being edited (replaced on save), or null for a new one.</summary>
        private DefencePreset _editing;
        private ActionPicker _picker;
        private Vector2 _menuScroll;
        private Vector2 _partsScroll;
        private string _message;
        private bool _freeHandHeld;
        private int _hoverPartLine = -1;

        // Dragging a part in the build order.
        private int _pressIndex = -1;
        private Vector2 _pressMouse;
        private bool _dragging;
        private int _dropIndex = -1;
        private readonly List<Rect> _rowRects = new List<Rect>();

        private Texture2D _crossCursor;
        private int _crossSize;
        private bool _crossShown;

        /// <summary>Opens the editor in place of the duel screen.</summary>
        public void Open(DuelController duelScreen, RulesConfig rules, Camera camera, BoardRenderer renderer, PresetLibrary library)
        {
            _duelScreen = duelScreen;
            _rules = rules;
            _camera = camera;
            _renderer = renderer;
            _library = library;
            _message = null;
            NewDefence(_library.Layouts.Count > 0 ? _library.Layouts[0] : new BindPointLayout());
            enabled = true;
        }

        private void NewDefence(BindPointLayout layout)
        {
            _editing = null;
            _session = new DefenceEditorSession(_rules, new DefencePreset { Name = "", Layout = layout.Clone() });
            _picker = NewPicker();
        }

        private void Edit(DefencePreset preset)
        {
            _editing = preset.IsDefault ? null : preset;
            var copy = preset.Clone();
            // A shipped defence can't be changed, so editing one starts a copy of it.
            if (preset.IsDefault) { copy.IsDefault = false; copy.Name = preset.Name + " copy"; }
            _session = new DefenceEditorSession(_rules, copy);
            _picker = NewPicker();
        }

        private static ActionPicker NewPicker()
        {
            return new ActionPicker { DefenceEditor = true, LockLabel = "Add to defence" };
        }

        private void Close()
        {
            enabled = false;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            _crossShown = false;
            if (_duelScreen != null) _duelScreen.enabled = true;
        }

        private void OnDisable()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            _crossShown = false;
        }

        private static float GuiScale() { return Mathf.Max(1f, Screen.height / 720f); }

        private static float MenuFraction()
        {
            float guiWidth = Screen.width / GuiScale();
            return Mathf.Min(0.5f, MenuWidth / Mathf.Max(1f, guiWidth));
        }

        private void LateUpdate()
        {
            if (_camera == null || _renderer == null || _session == null) return;
            float left = MenuFraction();
            float top = HeaderHeight * GuiScale() / Mathf.Max(1, Screen.height);
            _camera.rect = new Rect(left, 0f, 1f - left, Mathf.Max(0.1f, 1f - top));
            // Only the player's own half matters, so the board view shows that half.
            float width = _rules.Board.Width / 2f + 1f;
            float height = _rules.Board.Height + 1f;
            float aspect = _camera.pixelRect.width / Mathf.Max(1f, _camera.pixelRect.height);
            _camera.orthographicSize = Mathf.Max(height / 2f, width / 2f / Mathf.Max(0.01f, aspect));
            _camera.transform.position = new Vector3(_rules.Board.Width / 4f, _rules.Board.Height / 2f, -10f);
            _renderer.Board = _session.Board;
            _renderer.ChalklingRadius = _rules.Making.Radius;
            _renderer.BoardCamera = _camera;
            _renderer.Preview = _session.CanAdd && !_dragging ? _picker.Preview : null;
            _renderer.HighlightLineId = _hoverPartLine;
        }

        private Vec2? BoardPoint(Vector2 guiPixels)
        {
            if (_camera == null) return null;
            var screen = new Vector3(guiPixels.x, Screen.height - guiPixels.y, 0f);
            if (!_camera.pixelRect.Contains(screen)) return null;
            var world = _camera.ScreenToWorldPoint(screen);
            var point = new Vec2(world.x, world.y);
            return _session.Board.OnBoard(point) ? point : (Vec2?)null;
        }

        private void OnGUI()
        {
            if (_session == null) return;
            GUI.skin = MenuSkin.Get();
            GUI.matrix = Matrix4x4.identity;
            var current = Event.current;
            Vec2? boardPoint = BoardPoint(current.mousePosition);
            // Typing in a stat box or the name box types there, rather than picking options.
            string focused = GUI.GetNameOfFocusedControl();
            bool editingStat = focused.StartsWith("stat-") || focused == NameField;
            MenuKeys.Track(current, !editingStat && !_dragging);
            if (boardPoint.HasValue && current.type == EventType.MouseDown && editingStat) GUI.FocusControl(null);
            if (current.type != EventType.Repaint && current.type != EventType.Layout)
            {
                bool mac = ActionPicker.FreeHandIsCommand;
                bool isKey = mac ? current.keyCode == KeyCode.LeftCommand || current.keyCode == KeyCode.RightCommand
                    : current.keyCode == KeyCode.LeftControl || current.keyCode == KeyCode.RightControl;
                if (current.type == EventType.KeyDown && isKey) _freeHandHeld = true;
                else if (current.type == EventType.KeyUp && isKey) _freeHandHeld = false;
                else _freeHandHeld = mac ? current.command : current.control;
            }

            // Placing a new part works exactly as in a duel (DESIGN.md 1.1).
            if (_session.CanAdd && !_dragging)
            {
                var board = _session.Board;
                var player = _session.Player;
                _picker.SetHover(boardPoint, _freeHandHeld);
                bool changed = false;
                if (boardPoint.HasValue && current.type == EventType.MouseDown && current.button == 0)
                    changed = _picker.HandleBoardClick(board, player, _rules, boardPoint.Value);
                else if (current.type == EventType.MouseDown && current.button == 1)
                {
                    _picker.HandleRightClick(board, player, _rules);
                    changed = true;
                }
                else if (MenuKeys.TakeBack())
                {
                    _picker.HandleRightClick(board, player, _rules);
                    changed = true;
                }
                else if (boardPoint.HasValue && current.type == EventType.ScrollWheel)
                {
                    float delta = current.shift && Mathf.Abs(current.delta.y) < 1e-6f ? current.delta.x : current.delta.y;
                    changed = _picker.HandleScroll(board, player, _rules, delta, current.shift);
                }
                if (changed)
                {
                    current.Use();
                    GUIUtility.ExitGUI();
                }
            }
            else _picker.SetHover(null);

            float scale = GuiScale();
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            GUILayout.BeginArea(new Rect(10, 6, width - 20, HeaderHeight - 6));
            try
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Defence editor");
                GUILayout.FlexibleSpace();
                GUILayout.Label(GameVersion.Label);
                if (GUILayout.Button("Back to duel", GUILayout.Width(120))) Then(Close);
                GUILayout.EndHorizontal();
            }
            finally { GUILayout.EndArea(); }

            float menuWidth = width * MenuFraction();
            if (current.type == EventType.Repaint) _hoverPartLine = -1;
            GUILayout.BeginArea(new Rect(10, HeaderHeight, menuWidth - 20, height - HeaderHeight - 10));
            try { DrawMenu(menuWidth - 20, height - HeaderHeight - 10); }
            finally { GUILayout.EndArea(); }

            if (current.type == EventType.Repaint) UpdateInvalidCursor();
        }

        private void DrawMenu(float width, float height)
        {
            var preset = _session.Preset;

            // Saved defences: pick one to edit, or start a new one.
            GUILayout.Label("Defences:");
            int shown = _editing != null ? _library.Presets.IndexOf(_editing) : -1;
            var names = new List<string>();
            foreach (var p in _library.Presets) names.Add(p.IsDefault ? p.Name + " (default)" : p.Name);
            int picked = ButtonGrid(names, shown, width);
            if (picked >= 0) { _message = null; Then(() => Edit(_library.Presets[picked])); }
            if (GUILayout.Button("New defence")) { _message = null; Then(() => NewDefence(preset.Layout)); }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", GUILayout.Width(100));
            GUI.SetNextControlName(NameField);
            preset.Name = GUILayout.TextField(preset.Name ?? "", 40);
            GUILayout.EndHorizontal();

            // Changing the layout would strand every part's bind points, so it is only offered with no parts.
            GUILayout.Label("Bind point layout:");
            var layoutNames = new List<string>();
            int layoutShown = -1;
            for (int i = 0; i < _library.Layouts.Count; i++)
            {
                layoutNames.Add(_library.Layouts[i].Name);
                if (_library.Layouts[i].Name == preset.Layout.Name) layoutShown = i;
            }
            bool layoutLocked = preset.Components.Count > 0;
            GUI.enabled = !layoutLocked;
            int layoutPicked = ButtonGrid(layoutNames, layoutShown, width);
            GUI.enabled = true;
            if (layoutLocked) GUILayout.Label("Remove every part to change the layout.");
            if (layoutPicked >= 0 && layoutPicked != layoutShown && !layoutLocked) Then(() => _session.SetLayout(_library.Layouts[layoutPicked]));

            GUILayout.Space(4);
            GUILayout.Label("Build order (drag to reorder; a part can't go above the line it is bound to):");
            DrawParts(width, Mathf.Min(220f, RowHeight * (preset.Components.Count + 1) + 8f));

            GUILayout.Space(4);
            if (_session.CanAdd)
            {
                GUILayout.Label("Add a part:");
                // The picker scrolls its own options and keeps its button at the bottom.
                var choice = _picker.Draw(_session.Duel, _session.Board, _session.Player, ref _menuScroll, width);
                if (choice != null)
                {
                    string problem = _session.Add(choice.Action.Spec, choice.Placement, choice.Action.Pace);
                    _message = problem;
                    Then(() => _picker = NewPicker());
                }
            }
            else
            {
                Colored(MenuSkin.Refused, "Part " + (_session.FailedIndex + 1) + " can't be built: " + _session.BuildProblem
                    + " Move or remove it to keep adding parts.");
                GUILayout.FlexibleSpace();
            }

            if (_message != null) Colored(MenuSkin.Refused, _message);
            string saveProblem = SaveProblem();
            if (saveProblem != null) Colored(MenuSkin.Refused, "Can't save yet: " + saveProblem);
            GUI.enabled = saveProblem == null;
            if (GUILayout.Button("Save defence", GUILayout.Height(30))) Then(Save);
            GUI.enabled = true;
        }

        // Checking a defence builds it on a fresh board, so the answer is kept until the defence or its name changes.
        private DefenceEditorSession _checkedSession;
        private int _checkedVersion = -1;
        private string _checkedName;
        private string _checkedProblem;

        private string SaveProblem()
        {
            if (_checkedSession != _session || _checkedVersion != _session.Version || _checkedName != _session.Preset.Name)
            {
                _checkedSession = _session;
                _checkedVersion = _session.Version;
                _checkedName = _session.Preset.Name;
                _checkedProblem = _session.SaveProblem(_library, _editing);
            }
            return _checkedProblem;
        }

        private void Save()
        {
            var preset = _session.Preset;
            if (!_library.SavePreset(preset, _rules, _editing)) { _message = SaveProblem(); return; }
            PresetStore.Save(_library);
            _editing = _library.FindPreset(preset.Name);
            _session = new DefenceEditorSession(_rules, _editing.Clone());
            _message = null;
        }

        /// <summary>
        /// The parts in build order. Press and drag a part to move it: while dragging, the places it may go are
        /// marked by a line where it would drop, and places above its parent (or below a part built on it) show red.
        /// </summary>
        private void DrawParts(float width, float height)
        {
            var preset = _session.Preset;
            var e = Event.current;
            _partsScroll = GUILayout.BeginScrollView(_partsScroll, GUILayout.Height(height));
            try
            {
                int min = 0, max = preset.Components.Count - 1;
                if (_dragging) preset.MoveRange(_pressIndex, out min, out max);
                if (e.type == EventType.Repaint) _rowRects.Clear();

                // The main LoW is always built first and can't move.
                GUILayout.Label("1. Main LoW (" + preset.Layout.Name + ")", GUILayout.Height(RowHeight));
                for (int i = 0; i < preset.Components.Count; i++)
                {
                    var part = preset.Components[i];
                    // Press on a part to start dragging it (checked against where it was last drawn).
                    if (e.type == EventType.MouseDown && e.button == 0 && i < _rowRects.Count && _rowRects[i].Contains(e.mousePosition))
                    {
                        _pressIndex = i;
                        _pressMouse = e.mousePosition;
                        _dragging = false;
                        e.Use();
                    }
                    bool failed = i == _session.FailedIndex;
                    bool notBuilt = _session.FailedIndex != -2 && i > _session.FailedIndex;
                    bool outOfRange = _dragging && (i < min || i > max);
                    GUILayout.BeginHorizontal(GUILayout.Height(RowHeight));
                    string label = (i + 2) + ". " + _session.Describe(part) + (failed ? "  (can't be built)" : notBuilt ? "  (not built)" : "");
                    var style = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
                    var colour = GUI.backgroundColor;
                    if (outOfRange || failed) GUI.backgroundColor = MenuSkin.Refused;
                    // The part being dragged looks selected.
                    // Drawn as a label in the button look (the selected look while dragged), so it never takes the press itself.
                    GUILayout.Label(label, _dragging && i == _pressIndex ? SelectedRow(style) : style, GUILayout.Height(RowHeight), GUILayout.ExpandWidth(true));
                    GUI.backgroundColor = colour;
                    var rowRect = GUILayoutUtility.GetLastRect();
                    if (e.type == EventType.Repaint)
                    {
                        _rowRects.Add(rowRect);
                        if (rowRect.Contains(e.mousePosition) && !_dragging) _hoverPartLine = _session.LineOf(part.Id);
                    }
                    if (GUILayout.Button("X", GUILayout.Width(28), GUILayout.Height(RowHeight)))
                    {
                        int id = part.Id;
                        _message = null;
                        Then(() => _session.Remove(id));
                    }
                    GUILayout.EndHorizontal();
                }

                if (_pressIndex >= 0 && e.type == EventType.MouseDrag)
                {
                    if (!_dragging && (e.mousePosition - _pressMouse).magnitude > DragStart) _dragging = true;
                    if (_dragging) _dropIndex = DropIndexAt(e.mousePosition.y);
                    e.Use();
                }
                if (_dragging && e.type == EventType.Repaint && _dropIndex >= 0 && _dropIndex < _rowRects.Count)
                {
                    // Where the part would land: a line above or below the row it would take the place of.
                    var target = _rowRects[_dropIndex];
                    bool allowed = _dropIndex >= min && _dropIndex <= max;
                    float y = _dropIndex <= _pressIndex ? target.y - 1f : target.yMax - 1f;
                    var colour = GUI.color;
                    GUI.color = allowed ? Color.white : MenuSkin.Refused;
                    GUI.DrawTexture(new Rect(target.x, y, target.width, 3f), Texture2D.whiteTexture);
                    GUI.color = colour;
                }
            }
            finally { GUILayout.EndScrollView(); }

            if (_pressIndex >= 0 && (e.rawType == EventType.MouseUp || e.type == EventType.MouseUp))
            {
                int from = _pressIndex, to = _dropIndex;
                bool wasDragging = _dragging;
                _pressIndex = -1;
                _dragging = false;
                _dropIndex = -1;
                // A drop on a place it can't go does nothing; the red showed why.
                if (wasDragging && to >= 0 && to != from) { _message = null; Then(() => _session.Move(from, to)); }
            }
        }

        private static GUIStyle SelectedRow(GUIStyle style)
        {
            return new GUIStyle(style) { normal = style.onNormal };
        }

        /// <summary>The part row nearest the mouse height, in the parts list's own coordinates.</summary>
        private int DropIndexAt(float y)
        {
            if (_rowRects.Count == 0) return -1;
            for (int i = 0; i < _rowRects.Count; i++)
                if (y < _rowRects[i].yMax) return i;
            return _rowRects.Count - 1;
        }

        /// <summary>A wrapping grid of buttons; returns the one clicked, or -1.</summary>
        private static int ButtonGrid(List<string> labels, int selected, float width)
        {
            int clicked = -1;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(width / 150f));
            for (int i = 0; i < labels.Count; i += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(labels.Count, i + perRow); j++)
                    if (GUILayout.Toggle(j == selected, labels[j], GUI.skin.button) != (j == selected)) clicked = j;
                GUILayout.EndHorizontal();
            }
            return clicked;
        }

        private static void Colored(Color colour, string text)
        {
            var before = GUI.contentColor;
            GUI.contentColor = colour;
            GUILayout.Label(text);
            GUI.contentColor = before;
        }

        private static void Then(System.Action change)
        {
            change();
            GUIUtility.ExitGUI();
        }

        /// <summary>The red cross under the mouse where nothing can be placed, as on the duel screen (DESIGN.md 1.1).</summary>
        private void UpdateInvalidCursor()
        {
            var preview = _session.CanAdd ? _picker.Preview : null;
            bool show = preview != null && preview.InvalidHover.HasValue && !_dragging;
            int size = 32;
            if (_camera != null && _camera.orthographicSize > 0f)
            {
                float pixelsPerUnit = _camera.pixelRect.height / (2f * _camera.orthographicSize);
                size = Mathf.Clamp(Mathf.RoundToInt(0.5f * pixelsPerUnit) / 2 * 2, 16, 128);
            }
            if (show == _crossShown && (!show || size == _crossSize)) return;
            _crossShown = show;
            if (show && (_crossCursor == null || size != _crossSize))
            {
                if (_crossCursor != null) Destroy(_crossCursor);
                _crossCursor = DuelController.MakeCrossCursor(size);
                _crossSize = size;
            }
            Cursor.SetCursor(show ? _crossCursor : null, new Vector2(_crossSize / 2f, _crossSize / 2f), CursorMode.Auto);
        }
    }
}
