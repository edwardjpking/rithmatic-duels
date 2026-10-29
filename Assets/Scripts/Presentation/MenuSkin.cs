using UnityEngine;

namespace RithmaticDuels.Presentation
{
    /// <summary>
    /// The look of the menus. It starts from Unity's built-in GUI skin and replaces the scroll bars, which by
    /// default are chunky light-grey bars with arrow buttons, with thin rounded ones that match the dark panels: a
    /// dark track with a thin outline, and a grey-green handle that brightens when hovered over and again while
    /// dragged. Only the handle's fill changes; the outlines stay the same.
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
        private static readonly Color ThumbFill = new Color(0.24f, 0.28f, 0.26f, 1f);
        private static readonly Color ThumbOutline = new Color(0.50f, 0.55f, 0.52f, 1f);
        private static readonly Color ThumbFillHover = new Color(0.38f, 0.43f, 0.40f, 1f);
        private static readonly Color ThumbFillDragged = new Color(0.48f, 0.53f, 0.50f, 1f);

        // Buttons: every state is drawn by this skin, so every button, wide or narrow, selected or not, lights up
        // the same way under the mouse. Selected ("on") buttons are lighter than unselected ones.
        private const int ButtonRadius = 4;
        private static readonly Color ButtonFill = new Color(0.13f, 0.16f, 0.14f, 1f);
        private static readonly Color ButtonFillHover = new Color(0.22f, 0.27f, 0.24f, 1f);
        private static readonly Color ButtonFillPressed = new Color(0.30f, 0.35f, 0.32f, 1f);
        private static readonly Color ButtonOnFill = new Color(0.36f, 0.41f, 0.38f, 1f);
        private static readonly Color ButtonOnFillHover = new Color(0.45f, 0.50f, 0.47f, 1f);
        private static readonly Color ButtonOutline = new Color(0.40f, 0.45f, 0.42f, 1f);
        private static readonly Color ButtonText = new Color(0.92f, 0.93f, 0.92f, 1f);

        /// <summary>
        /// The red of things that can't be picked (DESIGN.md 1.1), the same as blocked bind points and the cross under
        /// the mouse on the board: a greyed-out option or the problem line flashes it when a key press is refused.
        /// </summary>
        public static readonly Color Refused = new Color(0.95f, 0.3f, 0.25f, 1f);
        /// <summary>How opaque the red flash over a refused option is at its strongest.</summary>
        public const float RefusedFlashAlpha = 0.6f;
        /// <summary>The number in the corner of each option in the row the number keys pick in: the button text, dimmed.</summary>
        public static readonly Color KeyHintText = new Color(ButtonText.r, ButtonText.g, ButtonText.b, 0.6f);

        private static GUIStyle _flashBox;

        /// <summary>A plain white box with a button's rounded corners, tinted with GUI.color to flash over a button.</summary>
        public static GUIStyle FlashBox
        {
            get
            {
                if (_flashBox != null && _flashBox.normal.background != null) return _flashBox;
                _flashBox = new GUIStyle();
                _flashBox.normal.background = Rounded(ButtonRadius, Color.white, Color.white);
                _flashBox.border = new RectOffset(ButtonRadius, ButtonRadius, ButtonRadius, ButtonRadius);
                return _flashBox;
            }
        }

        private static GUISkin _skin;

        /// <summary>
        /// Play Mode starts without reloading scripts (Enter Play Mode Options, for faster loading), so statics keep
        /// their values from the last run. Start each run with no skin, so it is built afresh.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            _skin = null;
            _flashBox = null;
        }

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
            StyleButton(_skin.button);
            return _skin;
        }

        private static void StyleTrack(GUIStyle style, bool vertical)
        {
            ClearStates(style);
            // Round ends: the corner radius is half the bar's width.
            int radius = BarWidth / 2;
            style.normal.background = Rounded(radius, TrackFill, TrackOutline);
            style.border = new RectOffset(radius, radius, radius, radius);
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

        private static void StyleButton(GUIStyle style)
        {
            ClearStates(style);
            var normal = Rounded(ButtonRadius, ButtonFill, ButtonOutline);
            var hover = Rounded(ButtonRadius, ButtonFillHover, ButtonOutline);
            var pressed = Rounded(ButtonRadius, ButtonFillPressed, ButtonOutline);
            var on = Rounded(ButtonRadius, ButtonOnFill, ButtonOutline);
            var onHover = Rounded(ButtonRadius, ButtonOnFillHover, ButtonOutline);
            style.normal.background = normal;
            style.focused.background = normal;
            style.hover.background = hover;
            style.active.background = pressed;
            style.onNormal.background = on;
            style.onFocused.background = on;
            style.onHover.background = onHover;
            style.onActive.background = pressed;
            foreach (var state in new[] { style.normal, style.focused, style.hover, style.active,
                                          style.onNormal, style.onFocused, style.onHover, style.onActive })
                state.textColor = ButtonText;
            style.border = new RectOffset(ButtonRadius, ButtonRadius, ButtonRadius, ButtonRadius);
            style.overflow = new RectOffset();
        }

        private static void StyleThumb(GUIStyle style, bool vertical)
        {
            ClearStates(style);
            // The handle sits inside the track's padding, so it is that much narrower; round its ends too.
            int radius = (BarWidth - 4) / 2;
            style.normal.background = Rounded(radius, ThumbFill, ThumbOutline);
            style.hover.background = Rounded(radius, ThumbFillHover, ThumbOutline);
            style.active.background = Rounded(radius, ThumbFillDragged, ThumbOutline);
            style.border = new RectOffset(radius, radius, radius, radius);
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
        /// A small rounded box: a one-pixel outline round a filled middle, with corners of the given radius and
        /// see-through outside them. The texture is (2 × radius + 1) pixels square. With a style border of radius
        /// on every side Unity keeps the corners as they are and stretches only the middle row and column, so it
        /// draws as a rounded box at any size (a pill shape when the box is exactly 2 × radius across).
        /// </summary>
        private static Texture2D Rounded(int radius, Color fill, Color outline)
        {
            int size = radius * 2 + 1;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color[size * size];
            float centre = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // How far this pixel's centre is outside the rounded box (negative inside), in pixels.
                    float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - centre) - 0.5f);
                    float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - centre) - 0.5f);
                    float outside = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    // Soft edges, half a pixel each way: the outer edge fades to clear, the inner one to the fill.
                    float coverage = Mathf.Clamp01(0.5f - outside);
                    float filled = Mathf.Clamp01(-0.5f - outside);
                    var colour = Color.Lerp(outline, fill, filled);
                    colour.a *= coverage;
                    pixels[y * size + x] = colour;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
