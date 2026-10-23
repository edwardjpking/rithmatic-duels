using System.Collections.Generic;
using RithmaticDuels.Rules;
using RithmaticDuels.Rules.Prototype;
using UnityEngine;

namespace RithmaticDuels.Presentation
{
    /// <summary>
    /// The action menu for one player in the hot-seat duel, drawn with Unity's immediate-mode GUI.
    /// It only builds a <see cref="PlayerAction"/>; the rules engine decides whether it is legal.
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

        private ActionKind _kind;
        private LineType _lineType;
        private DrawingPace _pace;
        private int _wardBindPoints = 2;
        private bool _marksCross;
        private int _forbiddanceBindPoints = 1;
        private VigorTier _tier;
        private int _durabilityBuffs = 1;
        private int _damageBuffs;
        private int _speedBuffs;
        private int _glyphs;
        private int _boundBindPoints;
        private ChalklingStat _modifierStat;
        private int _targetLineId = -1;
        private int _amount = 1;
        private ChalklingStat _buffStat;

        /// <summary>Draws the menu. Returns the action once the player locks it in, otherwise null.</summary>
        public PlayerAction Draw(Duel duel, Player player)
        {
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
            switch (_kind)
            {
                case ActionKind.StartDrawing: action = DrawStart(duel.Config); break;
                case ActionKind.ContinueDrawing: action = DrawContinue(duel, player); break;
                case ActionKind.CancelDrawing:
                    GUILayout.Label("Abandon your current work. All progress on it is lost.");
                    action = PlayerAction.Cancel();
                    break;
                case ActionKind.AddToDrawing: action = DrawAddTo(duel, player); break;
                case ActionKind.DismissForbiddance: action = DrawDismiss(duel, player); break;
                default:
                    GUILayout.Label("Do nothing this turn.");
                    action = PlayerAction.Pass();
                    break;
            }

            GUILayout.FlexibleSpace();
            string problem = action == null ? "Pick a target." : duel.Validate(player, action);
            if (problem != null) GUILayout.Label(problem);

            GUI.enabled = problem == null;
            bool locked = GUILayout.Button("Lock in", GUILayout.Height(40));
            GUI.enabled = true;
            return locked ? action : null;
        }

        private PlayerAction DrawStart(RulesConfig config)
        {
            Choose(ref _lineType, LineTypeLabels);
            DrawingSpec spec;
            switch (_lineType)
            {
                case LineType.Warding:
                    Toggle(ref _marksCross, " Mark's Cross (a small brace, not a main circle)");
                    if (_marksCross) spec = DrawingSpec.MarksCross(config);
                    else
                    {
                        Stepper("Bind points", ref _wardBindPoints, 2, config.Warding.MaxBindPoints);
                        spec = DrawingSpec.Warding(_wardBindPoints);
                    }
                    GUILayout.Label("Your first completed Line of Warding becomes your main one.");
                    break;
                case LineType.Forbiddance:
                    Stepper("Bind points", ref _forbiddanceBindPoints, 0, config.Warding.MaxBindPoints);
                    spec = DrawingSpec.Forbiddance(_forbiddanceBindPoints);
                    break;
                case LineType.Vigor:
                    Choose(ref _tier, TierLabels);
                    var tier = config.Vigor.Tier(_tier);
                    GUILayout.Label("Speed " + tier.Speed + ", damage " + tier.Damage + " before its modifier.");
                    spec = DrawingSpec.Vigor(_tier);
                    break;
                default:
                    Stepper("Durability buffs", ref _durabilityBuffs, 0, 12);
                    Stepper("Damage buffs", ref _damageBuffs, 0, 12);
                    Stepper("Speed buffs", ref _speedBuffs, 0, 12);
                    int maxGlyphs = DrawTimes.MaxGlyphs(_durabilityBuffs + _damageBuffs + _speedBuffs, config);
                    Stepper("Glyphs (max " + maxGlyphs + ")", ref _glyphs, 0, maxGlyphs);
                    Stepper("Bound to bind points", ref _boundBindPoints, 0, config.Warding.MaxBindPoints);
                    GUILayout.Label("A positive modifier goes to:");
                    Choose(ref _modifierStat, StatLabels);
                    GUILayout.Label("Chalklings stand idle until the real board exists.");
                    spec = DrawingSpec.Making(_durabilityBuffs, _damageBuffs, _speedBuffs, _glyphs, _boundBindPoints, _modifierStat);
                    break;
            }

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
            return PlayerAction.Start(spec, _pace);
        }

        private static PlayerAction DrawContinue(Duel duel, Player player)
        {
            var work = duel.GetPlayer(player).Work;
            GUILayout.Label("Keep going. " + work.TurnsRemaining + " turn(s) left, including this one.");
            return PlayerAction.Continue();
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
