using Better_Work_Tab.PawnOrganizer;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.VisualFeedback
{
    /// <summary>
    /// Renders visual hover effects for interactive elements.
    /// Single responsibility: Draw hover highlights.
    /// </summary>
    public static class HoverEffectManager
    {
        private static readonly Color HeaderHoverColor = new Color(1f, 1f, 1f, 0.1f);
        private static readonly Color RowHoverColor = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color ResizeHandleColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);

        public static void DrawHeaderHover(WorkTabLayoutColumn column, Vector2 mousePosition)
        {
            if (Mouse.IsOver(column.HeaderRect))
            {
                Widgets.DrawBoxSolid(column.HeaderRect, HeaderHoverColor);
            }
        }

        public static void DrawRowHover(WorkTabLayoutRow row, Rect rowRect)
        {
            if (Mouse.IsOver(rowRect))
            {
                Widgets.DrawBoxSolid(rowRect, RowHoverColor);
            }
        }

        public static void DrawResizeHandle(Rect handleRect, bool isHovered)
        {
            if (isHovered)
            {
                Widgets.DrawBoxSolid(handleRect, ResizeHandleColor);
            }
        }
    }
}
