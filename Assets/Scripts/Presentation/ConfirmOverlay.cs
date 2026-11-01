using UnityEngine;

namespace RithmaticDuels.Presentation
{
    /// <summary>
    /// The one confirm box of the game (DESIGN.md 1.1, consistency): a panel over the arena half that asks a yes/no
    /// question, used for every step that can't be undone. It works like the action menu: click an answer or press
    /// 1 (yes) or 2 (no) to pick it, then Lock in (or Space) carries it out. Right-click, Backspace or Esc cancel,
    /// like every other back step. While it is open the rest of the screen takes no input.
    /// </summary>
    public sealed class ConfirmOverlay
    {
        /// <summary>Dims the arena under the panel, so the question is hard to miss.</summary>
        private static readonly Color Dim = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color PanelFill = new Color(0.10f, 0.13f, 0.11f, 0.97f);
        private const float PanelWidth = 380f;

        private string _question;
        private string _yes;
        private string _no;
        private System.Action _onYes;
        private System.Action _onNo;
        /// <summary>The picked answer: 0 yes, 1 no, -1 none yet.</summary>
        private int _picked = -1;
        private GUIStyle _keyHint;
        private GUIStyle _questionStyle;

        public bool Open { get { return _question != null; } }

        /// <summary>Where the panel was last drawn, so clicks elsewhere can be held back.</summary>
        private Rect _panel;

        /// <summary>
        /// While the box is open, uses up mouse events outside its panel so the screen under it can't be clicked.
        /// Call with the same GUI matrix as <see cref="Draw"/>, before anything else is drawn.
        /// </summary>
        public void BlockOutside(Event e)
        {
            if (!Open) return;
            bool mouse = e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.MouseDrag
                || e.type == EventType.ScrollWheel;
            if (mouse && !_panel.Contains(e.mousePosition)) e.Use();
        }

        /// <summary>Opens the box. <paramref name="onNo"/> also runs when it is cancelled.</summary>
        public void Ask(string question, string yes, string no, System.Action onYes, System.Action onNo = null)
        {
            _question = question;
            _yes = yes;
            _no = no;
            _onYes = onYes;
            _onNo = onNo;
            _picked = -1;
        }

        /// <summary>Closes the box without running either answer.</summary>
        public void Close() { _question = null; _picked = -1; }

        /// <summary>
        /// Reads the keys and right-click for this GUI event. Call after <see cref="MenuKeys.Track"/> and before the
        /// rest of the screen handles input; returns true while the box is open, so the caller skips its own input.
        /// </summary>
        public bool HandleInput(Event e)
        {
            if (!Open) return false;
            int option = MenuKeys.PeekOption();
            if (option == 0 || option == 1) { MenuKeys.Use(); _picked = option; }
            else if (MenuKeys.TakeNext()) { if (_picked >= 0) Finish(_picked); }
            else if (MenuKeys.TakeBack() || e.type == EventType.MouseDown && e.button == 1) { bool click = e.type == EventType.MouseDown; Finish(1); if (click) e.Use(); }
            return true;
        }

        /// <summary>Draws the box over <paramref name="arena"/> (in the current GUI coordinates). Call last, so it sits on top.</summary>
        public void Draw(Rect arena)
        {
            if (!Open) return;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = true;
            GUI.DrawTexture(arena, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, Dim, 0, 0);
            if (_questionStyle == null) _questionStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            float width = Mathf.Min(PanelWidth, arena.width - 20f);
            float textHeight = _questionStyle.CalcHeight(new GUIContent(_question), width - 24f);
            float height = textHeight + 110f;
            var panel = new Rect(arena.center.x - width / 2f, arena.center.y - height / 2f, width, height);
            _panel = panel;
            GUI.DrawTexture(panel, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, PanelFill, 0, 0);

            var before = GUI.contentColor;
            GUI.contentColor = MenuSkin.Refused; // the red of things that can't be undone or picked
            GUI.Label(new Rect(panel.x + 12, panel.y + 10, width - 24, textHeight), _question, _questionStyle);
            GUI.contentColor = before;

            float buttonWidth = (width - 36f) / 2f;
            float row = panel.y + textHeight + 22f;
            for (int i = 0; i < 2; i++)
            {
                var rect = new Rect(panel.x + 12 + i * (buttonWidth + 12), row, buttonWidth, 32);
                if (GUI.Toggle(rect, _picked == i, i == 0 ? _yes : _no, GUI.skin.button) != (_picked == i)) _picked = i;
                if (Event.current.type == EventType.Repaint) KeyHint(rect, (i + 1).ToString());
            }
            GUI.enabled = _picked >= 0;
            if (GUI.Button(new Rect(panel.x + 12, row + 42, width - 24, 32), "Lock in")) { int picked = _picked; GUI.enabled = wasEnabled; Finish(picked); GUIUtility.ExitGUI(); }
            GUI.enabled = wasEnabled;
        }

        private void Finish(int picked)
        {
            var action = picked == 0 ? _onYes : _onNo;
            Close();
            if (action != null) action();
        }

        /// <summary>The key's number in the button's corner, as on the action menu's options.</summary>
        private void KeyHint(Rect rect, string name)
        {
            if (_keyHint == null)
            {
                _keyHint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(4, 0, 1, 0) };
                _keyHint.normal.textColor = MenuSkin.KeyHintText;
            }
            GUI.Label(rect, name, _keyHint);
        }
    }
}
