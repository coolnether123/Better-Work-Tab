using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Utility for drawing highlights as either solid boxes or outlines based on settings.
    /// </summary>
    public static class HighlightDrawer
    {
        private const float OutlineThickness = 2f;

        /// <summary>
        /// Draws a highlight rect, either as a solid box or outline depending on settings.
        /// </summary>
        public static void DrawHighlight(Rect rect, Color color)
        {
            if (BetterWorkTabMod.Settings?.useOutlineHighlights ?? false)
            {
                DrawOutlineHighlight(rect, color);
            }
            else
            {
                Widgets.DrawBoxSolid(rect, color);
            }
        }

        /// <summary>
        /// Draws a highlight as an outline with transparent center.
        /// </summary>
        private static void DrawOutlineHighlight(Rect rect, Color color)
        {
            Widgets.DrawBoxSolidWithOutline(
                rect,
                Color.clear,
                color,
                (int)OutlineThickness);
        }
    }
}