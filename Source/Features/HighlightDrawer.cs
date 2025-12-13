using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    public static class HighlightDrawer
    {
        private const float OutlineThickness = 2f;

        public static void DrawHighlight(Rect rect, Color color)
        {
            if (BetterWorkTabMod.Settings?.useOutlineHighlights ?? false)
            {
                Widgets.DrawBoxSolidWithOutline(rect, Color.clear, color, (int)OutlineThickness);
            }
            else
            {
                Widgets.DrawBoxSolid(rect, color);
            }
        }

        public static Color GetRowHoverColor()
        {
            return BetterWorkTabMod.Settings?.Color_RowHoverHighlight ?? Color.white;
        }

        public static Color GetColumnHoverColor()
        {
            return BetterWorkTabMod.Settings?.Color_ColumnHoverHighlight ?? Color.white;
        }

        public static Color GetFloatMenuColor()
        {
            return BetterWorkTabMod.Settings?.Color_FloatMenuHighlight ?? Color.cyan;
        }

        public static Color GetSelectedPawnColor()
        {
            return BetterWorkTabMod.Settings?.Color_CursorHighlight ?? Color.yellow;
        }

        public static Color GetSimilarWorktypeColor()
        {
            return BetterWorkTabMod.Settings?.Color_SimilarWorktypeMouseOver ?? new Color(1f, 1f, 1f, 0.1f);
        }
    }
}
