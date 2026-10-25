using UnityEngine;

namespace RithmaticDuels.Presentation
{
    /// <summary>
    /// The look of the menus. It starts from Unity's built-in GUI skin and replaces the scroll bars, which by
    /// default are chunky light-grey bars with arrow buttons, with thin ones that match the dark panels: a dark
    /// track with a thin outline, and a grey-green handle that brightens when hovered over and again while dragged.
    ///
    /// Unity looks scroll bar parts up by name in the active skin (a scroll bar called "verticalscrollbar" finds
    /// "verticalscrollbarthumb" and so on), so they can't be swapped one style at a time. Instead this keeps its own
    /// copy of the skin, and <see cref="DuelController"/> makes it the active skin at the start of every OnGUI.
    /// </summary>
    public static class MenuSkin
    {
        // How wide the scroll bars are, and the shortest the handle can get, in GUI pixels (before GUI scaling).
        private const int BarWidth = 10;
        private const int MinThumbLength = 24;

        // Fill and outline colours. The track matches the hover stats panel; the handle matches the buttons.
        private static readonly Color TrackFill = new Color(0.05f, 0.07f, 0.06f, 1f);
        private static readonly Color TrackOutline = new Color(0.35f, 0.40f, 0.37f, 1f);
        private static readonly Color TrackOutlineHover = new Color(0.55f, 0.60f, 0.57f, 1f);
        private static readonly Color ThumbFill = new Color(0.24f, 0.28f, 0.26f, 1f);
        private static readonly Color ThumbOutline = new Color(0.50f, 0.55f, 0.52f, 1f);
        private static readonly Color ThumbFillHover = new Color(0.38f, 0.43f, 0.40f, 1f);
        private static readonly Color ThumbOutlineHover = new Color(0.85f, 0.88f, 0.86f, 1f);
        private static readonly Color ThumbFillDragged = new Color(0.48f, 0.53f, 0.50f, 1f);

        private static GUISkin _skin;

        /// <summary>The menu skin, built the first time it is asked for. Only call this from inside OnGUI.</summary>
        public static GUISkin Get()
        {
            if (_skin != null) return _skin;
            _skin = Object.Instantiate(GUI.skin);
            _skin.hideFlags = HideFlags.HideAndDontSave;
            _skin.name = "Rithmatic menu skin";

            StyleTrack(_skin.verticalScrollbar, true);
            StyleTrack(_skin.horizontalScrollbar, false);
            StyleThumb(_skin.verticalScrollbarThumb, true);
            StyleThumb(_skin.horizontalScrollbarThumb, false);
            HideButton(_skin.verticalScrollbarUpButton);
            HideButton(_skin.verticalScrollbarDownButton);
            HideButton(_skin.horizontalScrollbarLeftButton);
            HideButton(_skin.horizontalScrollbarRightButton);
            return _skin;
        }

        private static void StyleTrack(GUIStyle style, bool vertical)
        {
            ClearStates(style);
            style.normal.background = Panel(TrackFill, TrackOutline);
            style.hover.background = Panel(TrackFill, TrackOutlineHover);
            style.active.background = style.hover.background;
            style.border = new RectOffset(1, 1, 1, 1);
            // The handle sits 2 pixels inside the outline.
            style.padding = new RectOffset(2, 2, 2, 2);
            style.overflow = new RectOffset();
            if (vertical)
            {
                style.fixedWidth = BarWidth;
                style.fixedHeight = 0;
                style.margin = new RectOffset(4, 0, 0, 0);
            }
            else
            {
                style.fixedHeight = BarWidth;
                style.fixedWidth = 0;
                style.margin = new RectOffset(0, 0, 4, 0);
            }
        }

        private static void StyleThumb(GUIStyle style, bool vertical)
        {
            ClearStates(style);
            style.normal.background = Panel(ThumbFill, ThumbOutline);
            style.hover.background = Panel(ThumbFillHover, ThumbOutlineHover);
            style.active.background = Panel(ThumbFillDragged, ThumbOutlineHover);
            style.border = new RectOffset(1, 1, 1, 1);
            style.margin = new RectOffset();
            style.overflow = new RectOffset();
            style.fixedWidth = 0;
            style.fixedHeight = 0;
            // Unity uses the handle's padding as its shortest length.
            int half = MinThumbLength / 2;
            style.padding = vertical ? new RectOffset(0, 0, half, half) : new RectOffset(half, half, 0, 0);
        }

        /// <summary>The arrow buttons at the ends of a scroll bar: drawn with no size, so they take no space.</summary>
        private static void HideButton(GUIStyle style)
        {
            ClearStates(style);
            style.fixedWidth = 0;
            style.fixedHeight = 0;
            style.margin = new RectOffset();
            style.padding = new RectOffset();
            style.border = new RectOffset();
            style.overflow = new RectOffset();
        }

        private static void ClearStates(GUIStyle style)
        {
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.focused.background = null;
            style.onNormal.background = null;
            style.onHover.background = null;
            style.onActive.background = null;
            style.onFocused.background = null;
        }

        /// <summary>
        /// A 3x3 texture: a one-pixel outline round a filled middle. With a style border of 1 Unity stretches
        /// only the middle, so it draws as a filled box with a thin outline at any size.
        /// </summary>
        private static Texture2D Panel(Color fill, Color outline)
        {
            var texture = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color[9];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = outline;
            pixels[4] = fill;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
