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
                // Respect configured alpha so transparency adjustments apply to outline mode too.
#if v1_2 || v1_1
                Verse.Widgets.DrawBoxSolid(rect, Color.clear);
                Color old = GUI.color;
                GUI.color = color;
                Verse.Widgets.DrawBox(rect, (int)OutlineThickness);
                GUI.color = old;
#else
                Widgets.DrawBoxSolidWithOutline(rect, Color.clear, color, (int)OutlineThickness);
#endif
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
            var settings = BetterWorkTabMod.Settings;
            var color = settings?.Color_SelectedPawnHighlight ?? Color.yellow;
            if (settings != null)
            {
                color.a = Mathf.Clamp01(color.a * settings.SelectedPawnHighlightOpacity);
            }
            return color;
        }

        public static Color GetSimilarWorktypeColor()
        {
            var settings = BetterWorkTabMod.Settings;
            var color = settings?.Color_SimilarWorktypeMouseOver ?? new Color(1f, 1f, 1f, 0.1f);
            if (settings != null)
            {
                color.a = Mathf.Clamp01(color.a * settings.SimilarWorktypeHighlightOpacity);
            }
            return color;
        }
    }
}
