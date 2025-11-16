using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.Sorting;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Renders sort indicators (up/down arrows) on column headers.
    /// Single responsibility: Draw sort direction indicators.
    /// </summary>
    public static class SortIndicatorRenderer
    {
        private const float IconSize = 12f;
        private static readonly Texture2D ArrowUp = ContentFinder<Texture2D>.Get("UI/Icons/Sorting");
        private static readonly Texture2D ArrowDown = ContentFinder<Texture2D>.Get("UI/Icons/Sorting");

        public static void DrawSortIndicator(WorkTabLayoutColumn column, SortState sortState)
        {
            if (sortState.SortColumn != column.Column) return;

            Rect iconRect = new Rect(
                column.HeaderRect.xMax - IconSize - 2f,
                column.HeaderRect.y + (column.HeaderRect.height - IconSize) / 2f,
                IconSize,
                IconSize);

            // Note: RimWorld may not have built-in sort arrow textures
            // You'd need to create these or use text arrows
            GUI.color = Color.white;
            if (sortState.Descending)
            {
                // Draw down arrow
                Widgets.Label(iconRect, "▼");
            }
            else
            {
                // Draw up arrow
                Widgets.Label(iconRect, "▲");
            }
            GUI.color = Color.white;
        }
    }
}
