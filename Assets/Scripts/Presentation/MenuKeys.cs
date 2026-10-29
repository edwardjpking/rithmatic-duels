using System.Collections.Generic;
using UnityEngine;

namespace RithmaticDuels.Presentation
{
    /// <summary>
    /// The keys that work the action menu (DESIGN.md section 5.1): the number keys pick an option in the first row
    /// with nothing chosen (or only a default), Space presses the page's main button (Lock in, show my choices, Next
    /// turn), and Backspace or Esc steps back like a right-click. The keys are defined here alone so they can be rebound in settings
    /// later (docs/FUTURE_IDEAS.md, Settings).
    ///
    /// <see cref="DuelController"/> calls <see cref="Track"/> once per GUI event, before anything is drawn; the
    /// menu then asks for the key pressed in that event. Holding a key down doesn't repeat it, so holding Space
    /// can't lock in and then rush through the next pages, and holding a number can't pick in row after row.
    /// </summary>
    public static class MenuKeys
    {
        /// <summary>The most options a row can be given keys for: 1 to 9, then 0 for the tenth.</summary>
        public const int MaxOptions = 10;

        private static readonly HashSet<KeyCode> Held = new HashSet<KeyCode>();

        /// <summary>The menu key newly pressed in this GUI event, or KeyCode.None.</summary>
        private static KeyCode _pressed;

        /// <summary>
        /// True while the menu takes keys: not while the player types in a LoM stat box (the keys type there) or
        /// while the lines list covers the menu. Number hints only show while it is true.
        /// </summary>
        public static bool Enabled { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Held.Clear();
            _pressed = KeyCode.None;
            Enabled = false;
        }

        /// <summary>Space: the page's main button.</summary>
        private static bool IsNext(KeyCode key) { return key == KeyCode.Space; }

        /// <summary>
        /// Backspace or Esc: back one step, like a right-click. While the lines list is open or a LoM stat box is
        /// being typed in, the menu takes no keys, so Esc closes the list or leaves the box first.
        /// </summary>
        private static bool IsBack(KeyCode key) { return key == KeyCode.Backspace || key == KeyCode.Escape; }

        /// <summary>The option a key picks in a row, counted from 0 (the 1 key), with the 0 key for the tenth. -1 for other keys.</summary>
        private static int OptionOf(KeyCode key)
        {
            if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9) return key - KeyCode.Alpha1;
            if (key >= KeyCode.Keypad1 && key <= KeyCode.Keypad9) return key - KeyCode.Keypad1;
            if (key == KeyCode.Alpha0 || key == KeyCode.Keypad0) return 9;
            return -1;
        }

        /// <summary>The key's name shown on option <paramref name="index"/> of the row the keys point at, or null if it has none.</summary>
        public static string OptionName(int index)
        {
            if (index < 0 || index >= MaxOptions) return null;
            return index == 9 ? "0" : (index + 1).ToString();
        }

        private static bool IsMenuKey(KeyCode key) { return IsNext(key) || IsBack(key) || OptionOf(key) >= 0; }

        /// <summary>
        /// Reads this GUI event. A menu key counts only on its first key down (not while held), with no Shift, Ctrl,
        /// Alt or Cmd, and only while <paramref name="enabled"/>.
        /// </summary>
        public static void Track(Event e, bool enabled)
        {
            Enabled = enabled;
            _pressed = KeyCode.None;
            if (e.type == EventType.KeyUp) { Held.Remove(e.keyCode); return; }
            if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None || !IsMenuKey(e.keyCode)) return;
            // Key repeat sends more key downs before the key up.
            if (!Held.Add(e.keyCode)) return;
            if (!enabled || e.shift || e.control || e.alt || e.command) return;
            _pressed = e.keyCode;
        }

        /// <summary>Key ups are lost while the game is in the background, so forget what was held.</summary>
        public static void ReleaseAll()
        {
            Held.Clear();
            _pressed = KeyCode.None;
        }

        /// <summary>True, once, if Space was just pressed. The key press is used up.</summary>
        public static bool TakeNext()
        {
            if (!IsNext(_pressed)) return false;
            Use();
            return true;
        }

        /// <summary>True, once, if Backspace or Esc was just pressed. The key press is used up.</summary>
        public static bool TakeBack()
        {
            if (!IsBack(_pressed)) return false;
            Use();
            return true;
        }

        /// <summary>The option a number key just pressed picks (from 0), without using it up; -1 if none was.</summary>
        public static int PeekOption()
        {
            return OptionOf(_pressed);
        }

        /// <summary>Uses up the key just pressed, so nothing else acts on it.</summary>
        public static void Use()
        {
            _pressed = KeyCode.None;
            if (Event.current != null && Event.current.type == EventType.KeyDown) Event.current.Use();
        }
    }
}
