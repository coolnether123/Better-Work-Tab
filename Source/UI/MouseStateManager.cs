using Better_Work_Tab.PawnOrganizer;
using UnityEngine;

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

            _lastUpdatedFrame = currentFrame;
            _hoveredColumn = newHovered;
        }

        public static void ClearHover()
        {
            _hoveredColumn = null;
            _lastUpdatedFrame = -1;
        }
    }
}
