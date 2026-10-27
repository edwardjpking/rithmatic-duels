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

        private static GUISkin _skin;

        /// <summary>
        /// Play Mode starts without reloading scripts (Enter Play Mode Options, for faster loading), so statics keep
        /// their values from the last run. Start each run with no skin, so it is built afresh.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            _skin = null;
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
