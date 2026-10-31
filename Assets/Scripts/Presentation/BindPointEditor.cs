using System.Collections.Generic;
using RithmaticDuels.Rules;
using RithmaticDuels.Rules.Geometry;
using RithmaticDuels.Rules.Presets;
using UnityEngine;

namespace RithmaticDuels.Presentation
{
    /// <summary>
    /// The bind point editor (DESIGN.md section 6.1): drag a triangle's three vertices around a main LoW circle; bind
    /// points appear where its edges and extended altitudes cross the circle. Saved layouts can be picked as a main
    /// LoW's bind points in a duel. Opened from the top bar for now; it moves to the main menu in build step 6.
    /// Drawn with IMGUI and GL by <see cref="DuelController"/>, in GUI units (the controller's GUI scale applies).
    /// </summary>
    public sealed class BindPointEditor
    {
        // Colours. Bind points, the circle and the triangle use player A's chalk, as on the board. The vertices you drag
        // are light blue, the colour of a picked placement you can still change. Red marks a spot you can't use.
        private static readonly Color Chalk = new Color(0.95f, 0.95f, 0.9f);
        private static readonly Color Construction = new Color(0.95f, 0.95f, 0.9f, 0.35f);
        private static readonly Color VertexColour = new Color(0.6f, 0.9f, 1f, 0.9f);
        private static readonly Color Backdrop = new Color(0.12f, 0.2f, 0.16f);

        private const float PanelWidth = 300f;
        /// <summary>How close (GUI units) the mouse must be to a vertex to grab it.</summary>
        private const float GrabRadius = 14f;

        private readonly RulesConfig _config;
        private PresetLibrary _library;
        private Vec2[] _vertices;
        private string _name = "";
        /// <summary>The saved layout being edited, replaced on save; null for a new one.</summary>
        private BindPointLayout _editing;
        private int _dragging = -1;
        private Vec2 _dragStart;
        private readonly Stack<Vec2[]> _undo = new Stack<Vec2[]>();
        /// <summary>Where the mouse is, in GUI units, if it is over a spot the dragged vertex can't go.</summary>
        private Vector2? _refusedAt;
        private int _hovered = -1;
        private string _status;
        private Material _material;
        private Rect _canvas;
        /// <summary>Zoom (1 = the whole area fits) and pan (GUI units) of the canvas view.</summary>
        private float _zoom = 1f;
        private Vector2 _pan;
        private bool _panning;
        private bool _showCentre;
        /// <summary>Whether the free hand key (Ctrl, or Cmd on macOS) is held: it stops snapping, as in a duel.</summary>
        private bool _freeHandHeld;
        /// <summary>The saved layout waiting for its delete to be confirmed, if any.</summary>
        private BindPointLayout _confirmDelete;
        /// <summary>The side picked for the lock options (side i joins vertices i and i+1), or -1.</summary>
        private int _selectedEdge = -1;
        /// <summary>The one locked side (only one at a time, since every two sides share a vertex), or -1.</summary>
        private int _lockedEdge = -1;
        private EdgeLock _lock = EdgeLock.None;

        public BindPointEditor(RulesConfig config, PresetLibrary library)
        {
            _config = config;
            _library = library;
            StartFrom(_library.FindLayout("Six-point circle"), false);
        }

        /// <summary>True when the player closes the editor.</summary>
        public bool Closed { get; private set; }
        public PresetLibrary Library { get { return _library; } }

        private void StartFrom(BindPointLayout layout, bool edit)
        {
            _vertices = layout != null && !layout.IsEllipse
                ? (Vec2[])layout.Vertices.Clone()
                : new[] { new Vec2(1f, 0f), new Vec2(-0.5f, 0.87f), new Vec2(-0.5f, -0.87f) };
            _editing = edit ? layout : null;
            _name = edit ? layout.Name : "";
            _undo.Clear();
            _dragging = -1;
            _confirmDelete = null;
            _selectedEdge = -1;
            _lockedEdge = -1;
            _lock = EdgeLock.None;
            _status = null;
        }

        private BindPointLayout Current()
        {
            return BindPointLayout.Triangle(_name.Trim(), _vertices[0], _vertices[1], _vertices[2]);
        }

        /// <summary>Call from OnGUI with the GUI matrix already scaled; width and height are in GUI units, scale is GUI units to pixels.</summary>
        public void Draw(float top, float width, float height, float scale)
        {
            var e = Event.current;
            GUI.DrawTexture(new Rect(0, top, width, height - top), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, Backdrop, 0, 0);
            _canvas = new Rect(PanelWidth + 20, top + 10, width - PanelWidth - 30, height - top - 20);
            HandleInput(e);
            DrawPanel(new Rect(10, top + 10, PanelWidth, height - top - 20));
            if (e.type == EventType.Repaint) DrawCanvas(scale);
        }

        // ---- Input ----

        private float CanvasScale { get { return _zoom * Mathf.Min(_canvas.width, _canvas.height) / (2f * _config.Presets.LayoutMaxVertexRadius + 0.4f); } }

        private Vector2 Origin { get { return _canvas.center + _pan; } }

        private Vector2 ToGui(Vec2 p)
        {
            return new Vector2(Origin.x + p.X * CanvasScale, Origin.y - p.Y * CanvasScale);
        }

        private Vec2 ToUnit(Vector2 gui)
        {
            return new Vec2((gui.x - Origin.x) / CanvasScale, (Origin.y - gui.y) / CanvasScale);
        }

        /// <summary>Zooms by <paramref name="factor"/>, keeping the spot under the mouse where it is.</summary>
        private void ZoomAt(Vector2 mouse, float factor)
        {
            var presets = _config.Presets;
            Vec2 under = ToUnit(mouse);
            _zoom = Mathf.Clamp(_zoom * factor, presets.LayoutMinZoom, presets.LayoutMaxZoom);
            _pan = mouse - _canvas.center - new Vector2(under.X * CanvasScale, -under.Y * CanvasScale);
        }

        private int VertexAt(Vector2 mouse)
        {
            int best = -1;
            float bestDistance = GrabRadius;
            for (int i = 0; i < 3; i++)
            {
                float d = Vector2.Distance(ToGui(_vertices[i]), mouse);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        private int EdgeAt(Vector2 mouse)
        {
            int best = -1;
            float bestDistance = GrabRadius;
            for (int i = 0; i < 3; i++)
            {
                Vector2 a = ToGui(_vertices[i]), b = ToGui(_vertices[(i + 1) % 3]);
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(mouse - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float d = Vector2.Distance(mouse, a + ab * t);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        /// <summary>The vertex the lock moves when <paramref name="moved"/> moves, or -1 if none.</summary>
        private int LockPartner(int moved)
        {
            if (_lockedEdge < 0 || _lock == EdgeLock.None) return -1;
            if (moved == _lockedEdge) return (_lockedEdge + 1) % 3;
            if (moved == (_lockedEdge + 1) % 3) return _lockedEdge;
            return -1;
        }

        /// <summary>Puts a lock on the selected side, moving its second vertex to match the first if that spot is allowed.</summary>
        private void SetLock(EdgeLock edgeLock)
        {
            if (edgeLock == EdgeLock.None) { _lockedEdge = -1; _lock = EdgeLock.None; return; }
            int first = _selectedEdge, second = (_selectedEdge + 1) % 3;
            var trial = (Vec2[])_vertices.Clone();
            trial[second] = BindPointLayout.LockedPartner(edgeLock, trial[first], trial[second]);
            if (!BindPointLayout.VertexAllowed(_vertices, second, trial[second], _config.Presets)) return;
            _undo.Push((Vec2[])_vertices.Clone());
            _vertices = trial;
            _lockedEdge = first;
            _lock = edgeLock;
        }

        /// <summary>Whether the lock can be put on the selected side (its second vertex has an allowed spot to go to).</summary>
        private bool LockPossible(EdgeLock edgeLock)
        {
            int first = _selectedEdge, second = (_selectedEdge + 1) % 3;
            var spot = BindPointLayout.LockedPartner(edgeLock, _vertices[first], _vertices[second]);
            return BindPointLayout.VertexAllowed(_vertices, second, spot, _config.Presets);
        }

        private void HandleInput(Event e)
        {
            bool inCanvas = _canvas.Contains(e.mousePosition);
            if (e.type != EventType.Repaint && e.type != EventType.Layout)
            {
                bool mac = ActionPicker.FreeHandIsCommand;
                bool isKey = mac ? e.keyCode == KeyCode.LeftCommand || e.keyCode == KeyCode.RightCommand
                    : e.keyCode == KeyCode.LeftControl || e.keyCode == KeyCode.RightControl;
                if (e.type == EventType.KeyDown && isKey) _freeHandHeld = true;
                else if (e.type == EventType.KeyUp && isKey) _freeHandHeld = false;
                else _freeHandHeld = mac ? e.command : e.control;
            }
            _hovered = _dragging >= 0 ? _dragging : (inCanvas ? VertexAt(e.mousePosition) : -1);

            if (e.type == EventType.MouseDown && e.button == 0 && inCanvas)
            {
                int vertex = VertexAt(e.mousePosition);
                if (vertex >= 0)
                {
                    _dragging = vertex;
                    _dragStart = _vertices[vertex];
                    _undo.Push((Vec2[])_vertices.Clone());
                    _status = null;
                    e.Use();
                }
                else if (EdgeAt(e.mousePosition) >= 0)
                {
                    // Clicking a side picks it for the lock options (clicking it again lets it go).
                    int edge = EdgeAt(e.mousePosition);
                    _selectedEdge = _selectedEdge == edge ? -1 : edge;
                    e.Use();
                }
                else
                {
                    // Dragging empty space moves the view.
                    _panning = true;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && _panning)
            {
                _pan += e.delta;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && _panning)
            {
                _panning = false;
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && inCanvas && _dragging < 0)
            {
                // Scroll zooms; Shift + scroll turns the layout round the circle (as it turns a circle in a duel).
                // Some platforms turn Shift + scroll into a sideways scroll, so fall back to the horizontal delta.
                float delta = e.shift && Mathf.Abs(e.delta.y) < 1e-6f ? e.delta.x : e.delta.y;
                if (Mathf.Abs(delta) > 1e-6f)
                {
                    if (e.shift)
                    {
                        _undo.Push((Vec2[])_vertices.Clone());
                        float step = _config.Presets.LayoutRotateStepDegrees * Mathf.Deg2Rad;
                        _vertices = BindPointLayout.Rotated(_vertices, delta > 0 ? -step : step);
                        _status = null;
                    }
                    else ZoomAt(e.mousePosition, delta > 0 ? 1f / _config.Presets.LayoutZoomStep : _config.Presets.LayoutZoomStep);
                }
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _dragging >= 0)
            {
                MoveDragged(e.mousePosition);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && _dragging >= 0)
            {
                // A drag that ended where it started isn't a step to undo.
                if (_undo.Count > 0 && Vec2.Distance(_vertices[_dragging], _dragStart) < 1e-6f) _undo.Pop();
                _dragging = -1;
                _refusedAt = null;
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 1 || MenuKeys.TakeBack())
            {
                // Right-click (or Backspace / Esc) goes back one step: cancel a delete, cancel the drag, or undo the last move.
                if (_confirmDelete != null) _confirmDelete = null;
                else if (_dragging < 0 && _selectedEdge >= 0) _selectedEdge = -1;
                else if (_dragging >= 0) { _vertices[_dragging] = _dragStart; if (_undo.Count > 0) _undo.Pop(); _dragging = -1; }
                else if (_undo.Count > 0) _vertices = _undo.Pop();
                _refusedAt = null;
                if (e.type == EventType.MouseDown) e.Use();
            }
        }

        private void MoveDragged(Vector2 mouse)
        {
            var presets = _config.Presets;
            Vec2 desired = ToUnit(mouse);
            // Like a size that is capped to what fits, a vertex dragged past the edge stays on the edge.
            if (desired.Length > presets.LayoutMaxVertexRadius) desired = desired.Normalized * presets.LayoutMaxVertexRadius;
            int partner = LockPartner(_dragging);
            // Holding the free hand key stops snapping, as in a duel (DESIGN.md 1.1). A locked side doesn't snap
            // either, since snapping one vertex would pull its partner off the lock.
            Vec2 snapped = _freeHandHeld || partner >= 0 ? desired : BindPointLayout.SnapVertex(_vertices, _dragging, desired, presets.LayoutSnapDistance);
            if (!BindPointLayout.VertexAllowed(_vertices, _dragging, snapped, presets)) snapped = desired;
            var trial = (Vec2[])_vertices.Clone();
            trial[_dragging] = snapped;
            bool ok = BindPointLayout.VertexAllowed(_vertices, _dragging, snapped, presets);
            if (ok && partner >= 0)
            {
                // The other vertex of the locked side follows, and must land somewhere allowed too.
                trial[partner] = BindPointLayout.LockedPartner(_lock, snapped, _vertices[partner]);
                ok = BindPointLayout.VertexAllowed(trial, partner, trial[partner], presets)
                    && BindPointLayout.VertexAllowed(trial, _dragging, snapped, presets);
            }
            if (ok)
            {
                _vertices = trial;
                _refusedAt = null;
            }
            else _refusedAt = mouse; // the vertex stays at its last allowed spot
        }

        // ---- Panel ----

        private void DrawPanel(Rect area)
        {
            GUILayout.BeginArea(area);
            GUILayout.Label("Bind points editor");
            GUILayout.Label("Drag the triangle's corners. Bind points appear where its sides and extended heights cross the circle. Points that come close snap together; hold Ctrl (Cmd on Mac) to stop snapping. Shift + scroll turns the layout round the circle. Scroll zooms and dragging empty space moves the view. Right-click undoes a move.");
            GUILayout.Space(6);

            var layout = Current();
            GUILayout.Label(layout.BindPointCount + " bind points");
            _showCentre = GUILayout.Toggle(_showCentre, " Show the circle's centre");
            GUI.enabled = _pan.sqrMagnitude > 0.01f;
            if (GUILayout.Button("Recentre view")) _pan = Vector2.zero;
            GUI.enabled = true;
            GUILayout.Space(6);
            DrawEdgeOptions();
            GUILayout.Space(6);

            GUILayout.Label("Name:");
            _name = GUILayout.TextField(_name, 40);
            string problem = _library.LayoutSaveProblem(layout, _config, _editing);
            if (problem == null && _editing != null && _editing.IsDefault) problem = "The standard layouts can't be changed.";
            GUI.enabled = problem == null;
            if (GUILayout.Button(_editing != null ? "Save changes" : "Save layout", GUILayout.Height(32)))
            {
                if (_library.SaveLayout(layout, _config, _editing))
                {
                    _editing = _library.FindLayout(layout.Name);
                    PresetStore.Save(_library);
                    _status = "Saved \"" + layout.Name + "\". It can now be picked for a main LoW's bind points.";
                }
            }
            GUI.enabled = true;
            if (problem != null) { var old = GUI.contentColor; GUI.contentColor = MenuSkin.Refused; GUILayout.Label(problem); GUI.contentColor = old; }
            if (_status != null) GUILayout.Label(_status);

            GUILayout.Space(10);
            GUILayout.Label("Start from a layout:");
            foreach (var saved in _library.Layouts)
            {
                if (saved.IsEllipse) continue;
                GUILayout.BeginHorizontal();
                if (_confirmDelete == saved)
                {
                    // Deleting can't be undone, so it asks first. Right-click, Backspace or Esc also keeps it.
                    GUILayout.Label("Delete " + saved.Name + "?");
                    if (GUILayout.Button("Delete", GUILayout.Width(60)))
                    {
                        _library.Layouts.Remove(saved);
                        PresetStore.Save(_library);
                        if (_editing == saved) _editing = null;
                        _confirmDelete = null;
                        _status = "Deleted \"" + saved.Name + "\".";
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button("Keep", GUILayout.Width(60))) { _confirmDelete = null; GUIUtility.ExitGUI(); }
                    GUILayout.EndHorizontal();
                    continue;
                }
                GUILayout.Label(saved.Name + " (" + saved.BindPointCount + ")");
                if (GUILayout.Button(saved.IsDefault ? "Copy" : "Edit", GUILayout.Width(60))) { StartFrom(saved, !saved.IsDefault); GUIUtility.ExitGUI(); }
                GUI.enabled = !saved.IsDefault;
                if (GUILayout.Button("Delete", GUILayout.Width(60))) { _confirmDelete = saved; GUI.enabled = true; GUIUtility.ExitGUI(); }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Back to the duel", GUILayout.Height(32))) Closed = true;
            GUILayout.EndArea();
        }

        private static readonly string[] LockNames =
        {
            "Free",
            "Mirror across horizontal",
            "Mirror across vertical",
            "Side horizontal",
            "Side vertical",
        };

        private void DrawEdgeOptions()
        {
            if (_selectedEdge < 0)
            {
                GUILayout.Label(_lockedEdge >= 0 ? "One side is locked (" + LockNames[(int)_lock].ToLower() + "). Click a side for its options."
                    : "Click a side of the triangle to lock it.");
                return;
            }
            GUILayout.Label("Selected side: lock it so dragging either end moves the other. Only one side can be locked; locking another frees the first.");
            EdgeLock current = _lockedEdge == _selectedEdge ? _lock : EdgeLock.None;
            for (int i = 0; i < LockNames.Length; i++)
            {
                var option = (EdgeLock)i;
                bool possible = option == EdgeLock.None || LockPossible(option);
                GUI.enabled = possible;
                bool on = GUILayout.Toggle(current == option, " " + LockNames[i] + (possible ? "" : " (no room)"));
                GUI.enabled = true;
                if (on && current != option) { SetLock(option); GUIUtility.ExitGUI(); }
            }
        }

        // ---- Canvas ----

        private void DrawCanvas(float scale)
        {
            EnsureMaterial();
            _material.SetPass(0);
            GL.PushMatrix();
            // Draw only inside the canvas, so a zoomed or moved view doesn't spill over the panel.
            GL.Viewport(new Rect(_canvas.x * scale, Screen.height - _canvas.yMax * scale, _canvas.width * scale, _canvas.height * scale));
            GL.LoadPixelMatrix(_canvas.x * scale, _canvas.xMax * scale, _canvas.yMax * scale, _canvas.y * scale);
            GL.Begin(GL.QUADS);
            _pixels = scale;

            float r = CanvasScale;
            Ring(ToGui(Vec2.Zero), r, 3f, Chalk);
            // Which way the LoW faces: the opponent is to the right.
            var front = ToGui(new Vec2(_config.Presets.LayoutMaxVertexRadius, 0f));
            Line(ToGui(new Vec2(1.15f, 0f)), front, 1.5f, Construction, true);

            for (int i = 0; i < 3; i++)
            {
                Vec2 from = _vertices[i], to = _vertices[(i + 1) % 3], opposite = _vertices[(i + 2) % 3];
                // Each side solid (only its own length counts); each height dashed and extended to a full line.
                // The picked side is light blue and thicker, like a picked placement; a locked side has two ticks across its middle.
                bool picked = i == _selectedEdge;
                Line(ToGui(from), ToGui(to), picked ? 4f : 2f, picked ? VertexColour : Construction, false);
                if (i == _lockedEdge)
                {
                    Vector2 a = ToGui(from), b = ToGui(to), mid = (a + b) / 2f;
                    Vector2 along = (b - a).normalized, across = new Vector2(-along.y, along.x) * 7f;
                    foreach (float off in new[] { -3f, 3f })
                        Line(mid + along * off - across, mid + along * off + across, 2f, Chalk, false);
                }
                Vec2 foot = FootOn(opposite, from, to);
                Vec2 dir = (foot - opposite).Length > 1e-5f ? (foot - opposite).Normalized : (to - from).Perp.Normalized;
                float reach = 2f * _config.Presets.LayoutMaxVertexRadius;
                Line(ToGui(opposite - dir * reach), ToGui(opposite + dir * reach), 1.5f, Construction, true);
            }

            if (_showCentre)
            {
                // A small cross, so it can't be mistaken for a bind point.
                var o = ToGui(Vec2.Zero);
                Line(o + new Vector2(-6, 0), o + new Vector2(6, 0), 2f, Chalk, false);
                Line(o + new Vector2(0, -6), o + new Vector2(0, 6), 2f, Chalk, false);
            }

            foreach (var p in Current().PointsOnUnitCircle())
            {
                Ring(ToGui(p), 9f, 2.5f, Chalk);
                Disc(ToGui(p), 3f, Chalk);
            }

            for (int i = 0; i < 3; i++)
            {
                var c = ToGui(_vertices[i]);
                Ring(c, 10f, 2.5f, VertexColour);
                // Filled while under the mouse or being dragged, so the difference doesn't rest on colour.
                if (i == _hovered || i == _dragging) Disc(c, 6f, VertexColour);
            }

            if (_refusedAt.HasValue)
            {
                var m = _refusedAt.Value;
                Line(m + new Vector2(-8, -8), m + new Vector2(8, 8), 3f, MenuSkin.Refused, false);
                Line(m + new Vector2(-8, 8), m + new Vector2(8, -8), 3f, MenuSkin.Refused, false);
            }
            GL.End();
            GL.PopMatrix();
            GL.Viewport(new Rect(0, 0, Screen.width, Screen.height));
        }

        private static Vec2 FootOn(Vec2 point, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            float t = Vec2.Dot(point - a, ab) / Mathf.Max(1e-8f, ab.LengthSquared);
            return a + ab * t;
        }

        // GL helpers in GUI units; _pixels converts to screen pixels.
        private float _pixels = 1f;

        private void V(Vector2 gui) { GL.Vertex3(gui.x * _pixels, gui.y * _pixels, 0f); }

        private void Line(Vector2 a, Vector2 b, float width, Color colour, bool dashed)
        {
            GL.Color(colour);
            float length = Vector2.Distance(a, b);
            if (length < 1e-4f) return;
            Vector2 dir = (b - a) / length;
            Vector2 side = new Vector2(-dir.y, dir.x) * (width / 2f);
            const float dash = 8f, gap = 6f;
            float step = dashed ? dash + gap : length;
            for (float s = 0f; s < length; s += step)
            {
                Vector2 p = a + dir * s, q = a + dir * Mathf.Min(length, s + (dashed ? dash : length));
                V(p - side); V(p + side); V(q + side); V(q - side);
            }
        }

        private const int Steps = 48;

        private static Vector2 Around(Vector2 centre, float radius, int step)
        {
            float angle = 2f * Mathf.PI * step / Steps;
            return centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        private void Ring(Vector2 centre, float radius, float width, Color colour)
        {
            GL.Color(colour);
            float inner = radius - width / 2f, outer = radius + width / 2f;
            for (int i = 0; i < Steps; i++)
            {
                V(Around(centre, inner, i)); V(Around(centre, outer, i)); V(Around(centre, outer, i + 1)); V(Around(centre, inner, i + 1));
            }
        }

        private void Disc(Vector2 centre, float radius, Color colour)
        {
            GL.Color(colour);
            for (int i = 0; i < Steps; i++)
            {
                V(centre); V(Around(centre, radius, i)); V(Around(centre, radius, i + 1)); V(Around(centre, radius, i + 1));
            }
        }

        private void EnsureMaterial()
        {
            if (_material != null) return;
            _material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        public void Dispose()
        {
            if (_material != null) Object.Destroy(_material);
        }
    }
}
