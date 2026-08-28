using System;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Reserves a lane for the fixed controls used by the neutral numeric
    /// setting widget before adding BWT's ownership action.
    /// </summary>
    internal static class BWTWorkloadPresentationNumericLayout
    {
        internal const float OwnershipActionGap = 6f;
        // SettingWidgets.DrawNumericInt owns this 48% right-hand lane.
        internal const float NumericControlFraction = 0.48f;
        internal const float NumericControlWidth = 98f;
        private const float LayoutPrecisionTolerance = 0.001f;

        internal static BWTWorkloadPresentationNumericOwnershipLayout Create(
            float rowX,
            float rowWidth,
            float fullActionWidth,
            float compactActionWidth)
        {
            float safeRowWidth = Math.Max(0f, rowWidth);
            float safeFullActionWidth = Math.Max(0f, fullActionWidth);
            float safeCompactActionWidth = Math.Max(0f, compactActionWidth);
            bool useCompactActionLabel = safeRowWidth <
                GetMinimumRowWidth(safeFullActionWidth);
            float actionWidth = useCompactActionLabel
                ? safeCompactActionWidth
                : safeFullActionWidth;
            float actionX = rowX + safeRowWidth - actionWidth;
            float availableNumericInputWidth = Math.Max(
                0f,
                actionX - OwnershipActionGap - rowX);
            float numericInputWidth = availableNumericInputWidth;

            // DrawNumericInt places its controls in rect.RightPart(0.48f),
            // but the minus, plus, and numeric field have a fixed 98px
            // footprint.  Reserving only the old 48px input minimum let that
            // live widget paint through the ownership action on narrow rows.
            float minimumNumericInputWidth = NumericControlWidth /
                NumericControlFraction;
            bool canFit = numericInputWidth >=
                minimumNumericInputWidth - LayoutPrecisionTolerance;

            // RightPart consumes the remainder of the passed row, so its
            // actual control lane ends at the numeric row's xMax.
            float numericControlEnd = rowX + numericInputWidth;
            return new BWTWorkloadPresentationNumericOwnershipLayout(
                actionX,
                actionWidth,
                numericInputWidth,
                numericControlEnd,
                useCompactActionLabel,
                canFit);
        }

        internal static float GetMinimumRowWidth(float actionWidth)
        {
            return Math.Max(0f, actionWidth) + OwnershipActionGap +
                (NumericControlWidth / NumericControlFraction);
        }
    }

    internal struct BWTWorkloadPresentationNumericOwnershipLayout
    {
        internal BWTWorkloadPresentationNumericOwnershipLayout(
            float actionX,
            float actionWidth,
            float numericInputWidth,
            float numericControlEnd,
            bool useCompactActionLabel,
            bool canFit)
        {
            ActionX = actionX;
            ActionWidth = actionWidth;
            NumericInputWidth = numericInputWidth;
            NumericControlEnd = numericControlEnd;
            UseCompactActionLabel = useCompactActionLabel;
            CanFit = canFit;
        }

        internal float ActionX { get; }
        internal float ActionWidth { get; }
        internal float NumericInputWidth { get; }
        internal float NumericControlEnd { get; }
        internal bool UseCompactActionLabel { get; }
        internal bool CanFit { get; }
    }
}
