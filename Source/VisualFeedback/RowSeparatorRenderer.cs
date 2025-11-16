using Better_Work_Tab.PawnOrganizer;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.VisualFeedback
{
    /// <summary>
    /// Draws subtle separator lines between pawn rows.
    /// Single responsibility: Visual row separation.
    /// </summary>
    public static class RowSeparatorRenderer
    {
        private static readonly Color SeparatorColor = new Color(0.3f, 0.3f, 0.3f, 0.4f);
        private const float SeparatorHeight = 1f;

        public static void DrawSeparator(Rect rowRect, float contentWidth)
        {
            // Draw line at bottom of row
            Rect lineRect = new Rect(
                rowRect.x,
                rowRect.yMax - SeparatorHeight,
                contentWidth,
                SeparatorHeight);

            Widgets.DrawBoxSolid(lineRect, SeparatorColor);
        }

        public static void DrawAllSeparators(
            System.Collections.Generic.IEnumerable<WorkTabLayoutRow> rows, 
            float contentWidth)
        {
            foreach (var row in rows)
            {
                if (row.IsDivider) continue; // Dividers handle their own borders

                Rect rowRect = new Rect(0f, row.OffsetY, contentWidth, row.Height);
                DrawSeparator(rowRect, contentWidth);
            }
        }
    }
}
