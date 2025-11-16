using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using System.Collections.Generic;
using UnityEngine;

namespace Better_Work_Tab.RowManagement
{
    /// <summary>
    /// Performs off-screen culling to improve performance.
    /// Single responsibility: Determine which rows should be rendered.
    /// </summary>
    public static class RowCullingManager
    {
        public static IEnumerable<WorkTabLayoutRow> GetVisibleRows(
            IWorkTabLayoutController layout, 
            Rect viewportRect, 
            Vector2 scrollPosition)
        {
            if (layout?.Rows == null) yield break;

            float viewportTop = scrollPosition.y;
            float viewportBottom = scrollPosition.y + viewportRect.height;

            foreach (var row in layout.Rows)
            {
                float rowTop = row.OffsetY;
                float rowBottom = row.OffsetY + row.Height;

                // Check if row intersects viewport
                if (rowBottom >= viewportTop && rowTop <= viewportBottom)
                {
                    yield return row;
                }
            }
        }
    }
}
