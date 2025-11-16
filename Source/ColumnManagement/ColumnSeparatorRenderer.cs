using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ColumnManagement
{
    /// <summary>
    /// Renders visual separators between columns.
    /// Single responsibility: Draw column dividers.
    /// </summary>
    public static class ColumnSeparatorRenderer
    {
        private static readonly Color SeparatorColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
        private const float SeparatorWidth = 1f;

        public static void DrawSeparators(IWorkTabLayoutController layout)
        {
            if (layout == null) return;

            foreach (var column in layout.Columns)
            {
                // Draw separator on right edge
                Rect separatorRect = new Rect(
                    column.HeaderRect.xMax - 1f,
                    layout.TableOrigin.y,
                    SeparatorWidth,
                    layout.HeaderHeight + layout.ContentHeight);

                Widgets.DrawBoxSolid(separatorRect, SeparatorColor);
            }
        }
    }
}
