using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    internal static class BWTWindowSizeUtility
    {
        private const float ScreenPaddingX = 40f;
        private const float ScreenPaddingY = 80f;

        public static Vector2 FitToScreen(Vector2 preferred, Vector2 minimum)
        {
            float maxWidth = Mathf.Max(1f, Verse.UI.screenWidth - ScreenPaddingX);
            float maxHeight = Mathf.Max(1f, Verse.UI.screenHeight - ScreenPaddingY);
            float minWidth = Mathf.Min(minimum.x, maxWidth);
            float minHeight = Mathf.Min(minimum.y, maxHeight);
            return new Vector2(
                Mathf.Clamp(preferred.x, minWidth, maxWidth),
                Mathf.Clamp(preferred.y, minHeight, maxHeight));
        }
    }
}
