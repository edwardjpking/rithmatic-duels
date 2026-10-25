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

        private enum Phase
        {
            /// <summary>Waiting for the next player to take the screen, so nobody sees the other's choice.</summary>
            Handover,
            Choosing,
            Results,
        }

        private static readonly Color Chalkboard = new Color(0.12f, 0.2f, 0.16f);

        // Screen layout, as fractions of the GUI area: menus on the left, board on the right, log below.
        private const float MenuWidth = 0.38f;
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
        private Vector2 _menuScroll;
        private string _error;

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
            _renderer.BoardCamera = _camera;
            _renderer.Preview = _phase == Phase.Choosing && _duel != null ? _pickers[(int)_chooser].Preview : null;
        }

        /// <summary>Fit the whole board into the area right of the menus and above the log.</summary>
        private void PlaceCamera()
        {
            float left = MenuWidth;
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
            if (_duel != null && _phase == Phase.Choosing)
            {
                var picker = _pickers[(int)_chooser];
                picker.SetHover(boardPoint);
                bool changed = false;
                if (boardPoint.HasValue && current.type == EventType.MouseDown && current.button == 0)
                    changed = picker.HandleBoardClick(_board, _chooser, _rules, boardPoint.Value);
                else if (boardPoint.HasValue && current.type == EventType.ScrollWheel)
                    changed = picker.HandleScroll(_board, _chooser, _rules, current.delta.y);
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
            DrawHeader();
            GUILayout.EndArea();

            if (_duel == null)
            {
                GUILayout.BeginArea(new Rect(10, HeaderHeight, width - 20, height - HeaderHeight));
                GUILayout.Label("Can't start a duel: " + _error);
                GUILayout.EndArea();
                return;
            }

            float logTop = height * LogTop;
            GUILayout.BeginArea(new Rect(10, HeaderHeight, width * MenuWidth - 20, logTop - HeaderHeight - 6));
            _menuScroll = GUILayout.BeginScrollView(_menuScroll);
            switch (_phase)
            {
                case Phase.Handover: DrawHandover(); break;
                case Phase.Choosing: DrawChoosing(); break;
                default: DrawResults(); break;
            }
            if (_error != null) GUILayout.Label(_error);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(width * MenuWidth, HeaderHeight, width * (1f - MenuWidth) - 10, 24));
            DrawWardStatus();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(10, logTop + 4, width - 20, height - logTop - 10));
            DrawLog();
            GUILayout.EndArea();

            if (boardPoint.HasValue) DrawLineStats(boardPoint.Value, mousePixels / scale, width, height);
        }

        /// <summary>
        /// Hovering over a line on the board shows its stats next to the mouse, on an opaque panel. It is drawn last
        /// in OnGUI so it sits on top of the menus. Near the edge of the screen it flips to the other side of the pointer.
        /// </summary>
        private void DrawLineStats(Vec2 boardPoint, Vector2 mouse, float screenWidth, float screenHeight)
        {
            var body = _board.BodyAt(boardPoint, 0.3f);
            if (body == null) return;
            var line = body.Line;
            string text = TurnNarrator.LineName(line);
            if (line.HasDurability) text += "\nDurability " + line.Durability + "/" + line.MaxDurability + (line.Type == LineType.Warding ? " (weakest point)" : "");
            if (line.Type == LineType.Vigor) text += "\n" + line.Tier + " tier, speed " + line.Speed + "\nDamage on a hit: " + line.Damage;
            if (line.Type == LineType.Making) text += "\nDamage " + line.Damage + ", speed " + line.Speed + ", " + line.Glyph + " glyph";
            if (line.ModifierSteps != 0) text += "\nModifier " + (line.ModifierSteps > 0 ? "+" : "") + line.ModifierSteps;
            var ward = body as WardBody;
            if (ward != null && ward.BindPoints.Count > 0) text += "\n" + ward.BindPoints.Count + " bind points";

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
                    _phase = Phase.Choosing;
                });
        }

        private void DrawChoosing()
        {
            GUILayout.Label(TurnNarrator.PlayerName(_chooser) + ": choose your action");
            GUILayout.Label(DescribeWork(_chooser, true));
            GUILayout.Label("Opponent: " + DescribeWork(_chooser.Opponent(), false));
            var choice = _pickers[(int)_chooser].Draw(_duel, _board, _chooser);
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
            foreach (var player in new[] { Player.A, Player.B })
            {
                GUILayout.Label(TurnNarrator.PlayerName(player) + ": " + DescribeWork(player, false));
                foreach (var line in _duel.Lines)
                {
                    if (line.Owner != player || !line.IsActive || line.Type == LineType.Vigor) continue;
                    GUILayout.Label("    " + TurnNarrator.LineName(line) + "  " + line.Durability + "/" + line.MaxDurability);
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

        private void DrawLog()
        {
            _logScroll = GUILayout.BeginScrollView(_logScroll, "box");
            foreach (var entry in _log) GUILayout.Label(entry);
            GUILayout.EndScrollView();
        }
    }
}
