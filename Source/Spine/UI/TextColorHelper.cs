using UnityEngine;

namespace Spine.UI
{
    /// <summary>
    /// Helper class for determining appropriate text colors based on background brightness.
    /// Uses luminance calculation to ensure readable contrast between text and background.
    /// </summary>
    public static class TextColorHelper
    {
        /// <summary>
        /// Determines if a color is considered "bright" based on its luminance.
        /// Uses the relative luminance formula: 0.299*R + 0.587*G + 0.114*B
        /// </summary>
        /// <param name="backgroundColor">The background color to check.</param>
        /// <param name="threshold">The luminance threshold (0-1). Default is 0.5.</param>
        /// <returns>True if the color is bright, false if dark.</returns>
        public static bool IsBright(Color backgroundColor, float threshold = 0.5f)
        {
            float luminance = GetLuminance(backgroundColor);
            return luminance > threshold;
        }

        /// <summary>
        /// Gets the relative luminance of a color (0 = black, 1 = white).
        /// Uses the relative luminance formula: 0.299*R + 0.587*G + 0.114*B
        /// </summary>
        /// <param name="color">The color to calculate luminance for.</param>
        /// <returns>A value between 0 and 1 representing the luminance.</returns>
        public static float GetLuminance(Color color)
        {
            return 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
        }

        /// <summary>
        /// Returns the appropriate text color (dark or white) based on background brightness.
        /// </summary>
        /// <param name="backgroundColor">The background color.</param>
        /// <param name="darkTextColor">The color to use for text on bright backgrounds. Default is black.</param>
        /// <param name="lightTextColor">The color to use for text on dark backgrounds. Default is white.</param>
        /// <param name="threshold">The luminance threshold (0-1). Default is 0.5.</param>
        /// <returns>Either darkTextColor or lightTextColor depending on background brightness.</returns>
        public static Color GetContrastingTextColor(
            Color backgroundColor,
            Color? darkTextColor = null,
            Color? lightTextColor = null,
            float threshold = 0.5f)
        {
            Color dark = darkTextColor ?? Color.black;
            Color light = lightTextColor ?? Color.white;

            return IsBright(backgroundColor, threshold) ? dark : light;
        }
    }
}