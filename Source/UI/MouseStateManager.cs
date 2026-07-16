using Better_Work_Tab.PawnOrganizer;
using UnityEngine;
using Better_Work_Tab.UI.WorkGrid.Invalidation;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Tracks hovered work columns once per frame to avoid redundant hit tests.
    /// </summary>
    public static class MouseStateManager
    {
        private static WorkTabLayoutColumn? _hoveredColumn;
        private static int _lastUpdatedFrame = -1;

        public static WorkTabLayoutColumn? HoveredColumn => _hoveredColumn;

        public static void UpdateHoverState(WorkTabLayoutColumn? newHovered)
        {
            int currentFrame = Time.frameCount;
            if (_lastUpdatedFrame == currentFrame)
            {
                return;
            }

            bool changed = !_hoveredColumn.Equals(newHovered);
            _lastUpdatedFrame = currentFrame;
            _hoveredColumn = newHovered;
            if (changed)
            {
                WorkTabInvalidationHub.InvalidateCategory(WorkGridInvalidationCategory.HoverInteraction);
            }
        }

        public static void ClearHover()
        {
            bool changed = _hoveredColumn.HasValue;
            _hoveredColumn = null;
            _lastUpdatedFrame = -1;
            if (changed)
            {
                WorkTabInvalidationHub.InvalidateCategory(WorkGridInvalidationCategory.HoverInteraction);
            }
        }
    }
}
