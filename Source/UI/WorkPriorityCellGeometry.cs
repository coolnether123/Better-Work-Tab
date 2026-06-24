using UnityEngine;

namespace Better_Work_Tab.UI
{
    internal static class WorkPriorityCellGeometry
    {
        internal const float BoxSize = 25f;
        internal const float BoxTopPadding = 2.5f;

        internal static Rect GetPriorityBoxRect(Rect cellRect)
        {
            return new Rect(
                cellRect.x + (cellRect.width - BoxSize) / 2f,
                cellRect.y + BoxTopPadding,
                BoxSize,
                BoxSize);
        }

        internal static Rect GetCenteredBoxRect(Rect cellRect, float boxSize)
        {
            return new Rect(
                cellRect.x + (cellRect.width - boxSize) / 2f,
                cellRect.y + (cellRect.height - boxSize) / 2f,
                boxSize,
                boxSize);
        }
    }
}
