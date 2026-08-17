using UnityEngine;
using Better_Work_Tab.UI.Settings;

namespace Better_Work_Tab.UI
{
    internal static class WorkPriorityCellGeometry
    {
        internal const float BoxSize = 25f;
        internal const float CompactSubWorkBoxSize = 20f;
        internal const float BoxTopPadding = 2.5f;

        internal static Rect GetPriorityBoxRect(Rect cellRect)
        {
            return new Rect(
                cellRect.x + (cellRect.width - BoxSize) / 2f,
                cellRect.y + BoxTopPadding,
                BoxSize,
                BoxSize);
        }

        /// <summary>
        /// The box a cell actually draws, given whether it is a sub-work column
        /// expanded beside its parent.
        ///
        /// Which of the two shapes applies was being decided independently by
        /// the renderer, the geometry validator and the tutorial's anchors. The
        /// tutorial got it wrong: it always assumed the top-padded 25px box, so
        /// in expand-beside mode its highlight framed a rectangle that was not
        /// where the box had been drawn. One rule, read by all three.
        /// </summary>
        internal static Rect GetDrawnPriorityBoxRect(Rect cellRect, bool expandBesideChild)
        {
            return expandBesideChild
                ? GetFluffyStyleSubWorkPriorityBoxRect(cellRect)
                : GetPriorityBoxRect(cellRect);
        }

        internal static Rect GetCenteredBoxRect(Rect cellRect, float boxSize)
        {
            return new Rect(
                cellRect.x + (cellRect.width - boxSize) / 2f,
                cellRect.y + (cellRect.height - boxSize) / 2f,
                boxSize,
                boxSize);
        }

        internal static Rect GetFluffyStyleSubWorkPriorityBoxRect(Rect cellRect)
        {
            bool compact = BWTWorkTabEffectiveSettings.GetBool(
                SettingIDs.SubWorkCompactPriorityBoxes,
                BetterWorkTabMod.Settings?.useCompactSubWorkPriorityBoxes ??
                    DefaultSettings.useCompactSubWorkPriorityBoxes);
            return GetCenteredBoxRect(cellRect, compact ? CompactSubWorkBoxSize : BoxSize);
        }
    }
}
