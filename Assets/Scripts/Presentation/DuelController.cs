using System.Collections.Generic;
using RithmaticDuels.Rules;
using RithmaticDuels.Rules.Prototype;
using UnityEngine;

namespace RithmaticDuels.Presentation
{
    /// <summary>
    /// Scene entry point for a duel. Presentation only: it gathers player input and
    /// draws the board, and calls into RithmaticDuels.Rules for every game rule.
    ///
    /// For now this is the rough hot-seat duel from build plan step 2: two players share one
    /// screen, choose in turn while the other looks away, and read what happened in a text log.
    /// It uses Unity's immediate-mode GUI (OnGUI) so it needs no scene setup, and the
    /// <see cref="LaneMovementResolver"/> prototype stands in for the real board.
    /// </summary>
    public sealed class DuelController : MonoBehaviour
    {
        [Tooltip("Every rules number. All placeholders: change them here to playtest balance.")]
        [SerializeField] private RulesConfig _rules = new RulesConfig();

        [Tooltip("Settings for the prototype lane that stands in for the board.")]
        [SerializeField] private LaneConfig _lane = new LaneConfig();

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

        private Duel _duel;
        private LaneMovementResolver _movement;
        private ulong _currentSeed;
        private Phase _phase;
        private Player _chooser;
        private PlayerAction _lockedA;
        private readonly ActionPicker[] _pickers = { new ActionPicker(), new ActionPicker() };
        private readonly List<string> _log = new List<string>();
        private Vector2 _logScroll;
        private Vector2 _boardScroll;
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
            var camera = Camera.main;
            if (camera == null) camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Chalkboard;
            NewDuel();
        }

        private void NewDuel()
        {
            // The hot-seat duel has no decision timer (DESIGN.md section 2).
            _rules.DecisionTimerSeconds = 0f;
            _currentSeed = _seed != 0 ? (ulong)_seed : (ulong)UnityEngine.Random.Range(1, int.MaxValue);
            _movement = new LaneMovementResolver(_lane);
            _error = null;
            try
            {
                _duel = new Duel(_rules, _currentSeed, _movement);
            }
            catch (System.ArgumentException e)
            {
                _duel = null;
                _error = e.Message;
                return;
            }
            _log.Clear();
            _log.Add("New duel (seed " + _currentSeed + "). Both players start by drawing a main Line of Warding.");
            _chooser = Player.A;
            _phase = Phase.Handover;
        }

        private void OnGUI()
        {
            // Scale everything so the GUI is readable at any resolution; lay out in a 1280x720-ish space.
            float scale = Mathf.Max(1f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            GUILayout.BeginArea(new Rect(10, 10, width - 20, height - 20));
            DrawHeader();

            if (_duel == null)
            {
                GUILayout.Label("Can't start a duel: " + _error);
            }
            else
            {
                switch (_phase)
                {
                    case Phase.Handover: DrawHandover(); break;
                    case Phase.Choosing: DrawChoosing(width); break;
                    default: DrawResults(); break;
                }
                if (_error != null) GUILayout.Label(_error);
                DrawLog(height * 0.3f);
            }
            GUILayout.EndArea();
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            if (_duel != null)
            {
                string limit = _duel.CountedTurns + " of " + _duel.Config.TurnLimit + " counted";
                if (_duel.CountedTurns == 0) limit += " (the count starts once both main LoW are drawn)";
                GUILayout.Label("Turn " + (_duel.Turn + 1) + "   ·   " + limit
                    + (_duel.ChalkFatigueActive ? "   ·   CHALK FATIGUE" : "") + "   ·   seed " + _currentSeed);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("New duel", GUILayout.Width(100))) Then(NewDuel);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        private void DrawHandover()
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label(TurnNarrator.PlayerName(_chooser) + ", it's your turn to choose. The other player should look away.");
            if (GUILayout.Button("I'm " + TurnNarrator.PlayerName(_chooser) + ", show my choices", GUILayout.Height(50)))
                Then(() => _phase = Phase.Choosing);
            GUILayout.FlexibleSpace();
        }

        private void DrawChoosing(float width)
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(width * 0.48f));
            _boardScroll = GUILayout.BeginScrollView(_boardScroll);
            DrawBoard(_chooser);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical("box");
            GUILayout.Label(TurnNarrator.PlayerName(_chooser) + ": choose your action");
            var action = _pickers[(int)_chooser].Draw(_duel, _chooser);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            if (action != null) Then(() => Lock(action));
        }

        private void Lock(PlayerAction action)
        {
            if (_chooser == Player.A)
            {
                _lockedA = action;
                _chooser = Player.B;
                _phase = Phase.Handover;
                return;
            }

            try
            {
                var report = _duel.ResolveTurn(_lockedA, action);
                _error = null;
                _log.Add("— Turn " + report.Turn + " —");
                _log.AddRange(TurnNarrator.Describe(_duel, report, _movement.LastTickNotes));
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
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            _boardScroll = GUILayout.BeginScrollView(_boardScroll);
            DrawBoard(null);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

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
        }

        /// <summary>The board as text. Only the viewer's own work in progress is shown in full.</summary>
        private void DrawBoard(Player? viewer)
        {
            foreach (var player in new[] { Player.A, Player.B })
            {
                GUILayout.BeginVertical("box");
                var state = _duel.GetPlayer(player);
                var ward = state.MainWard;
                GUILayout.Label(TurnNarrator.PlayerName(player) + (viewer == player ? " (you)" : "") + "   ·   main LoW: "
                    + (ward == null ? "not drawn yet" : ward.IsActive ? ward.Durability + "/" + ward.MaxDurability : "BREACHED"));
                GUILayout.Label(DescribeWork(player, viewer == player));

                foreach (var line in _duel.Lines)
                {
                    if (line.Owner != player || !line.IsActive || line.IsMainWard) continue;
                    GUILayout.Label("    " + DescribeLine(line));
                }
                GUILayout.EndVertical();
            }
        }

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
                    default: return view.Kind + ": " + TurnNarrator.LineName(_duel.FindLine(view.TargetLineId));
                }
            }

            string what = work.Kind == WorkKind.Drawing
                ? TurnNarrator.LineTypeName(work.Spec.Type) + " (" + SpecSummary(work.Spec) + ", " + work.Pace + ")"
                : work.Kind + " " + TurnNarrator.LineName(_duel.FindLine(work.TargetLineId)) + " by " + work.Amount;
            return "Working on: " + what + ", " + work.Progress + " of " + work.TotalTurns + " turns done";
        }

        private string DescribeLine(Line line)
        {
            string name = TurnNarrator.LineName(line);
            switch (line.Type)
            {
                case LineType.Vigor:
                    return name + "  " + line.Tier + ", " + line.Damage + " damage, "
                        + _movement.DistanceTravelled(line.Id).ToString("0.#") + " of "
                        + _movement.LaneLength(_duel.Config).ToString("0.#") + " along the lane";
                case LineType.Making:
                    return name + "  " + line.Durability + "/" + line.MaxDurability + " durability, "
                        + line.Damage + " damage, " + line.Speed + " speed, " + line.Glyphs + " glyph(s)";
                default:
                    return name + "  " + line.Durability + "/" + line.MaxDurability;
            }
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
                        + " durability/damage/speed buffs, " + spec.Glyphs + " glyph(s)";
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

        private void DrawLog(float height)
        {
            GUILayout.Space(6);
            _logScroll = GUILayout.BeginScrollView(_logScroll, "box", GUILayout.Height(height));
            foreach (var entry in _log) GUILayout.Label(entry);
            GUILayout.EndScrollView();
        }
    }
}
