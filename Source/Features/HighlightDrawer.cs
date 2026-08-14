using UnityEngine;
using Verse;
using Better_Work_Tab.UI.Settings;

namespace Better_Work_Tab.UI
{
    public static class HighlightDrawer
    {
        private const float OutlineThickness = 2f;
        private const string MasterHighlightPreviewField = "preview.masterHighlight";

        public static void DrawHighlight(Rect rect, Color color)
        {
            if (BetterWorkTabMod.Settings?.useOutlineHighlights ?? false)
            {
                // Respect configured alpha so transparency adjustments apply to outline mode too.
                Widgets.DrawBoxSolidWithOutline(rect, Color.clear, color, (int)OutlineThickness);
            }
            else
            {
                Widgets.DrawBoxSolid(rect, color);
            }
        }

        public static Color GetRowHoverColor() => GetHoverColor(false);

        public static Color GetColumnHoverColor() => GetHoverColor(true);

        private static Color GetHoverColor(bool column)
        {
            if (WorkTabColorPreviewController.Instance.TryGetPreview(out WorkTabColorPreview preview) &&
                (preview.FieldName == MasterHighlightPreviewField ||
                 (column ? preview.IncludesColumn : preview.IncludesRow)))
            {
                return preview.Color;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            return column
                ? settings?.Color_ColumnHoverHighlight ?? Color.white
                : settings?.Color_RowHoverHighlight ?? Color.white;
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
