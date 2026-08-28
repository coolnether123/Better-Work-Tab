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
        internal const float NumericControlStartFraction = 0.52f;
        internal const float NumericControlWidth = 98f;
        internal const float PreferredNumericInputWidth = 48f;
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
            float controlLimitedNumericInputWidth = Math.Max(
                0f,
                (safeRowWidth - actionWidth - OwnershipActionGap -
                    NumericControlWidth) / NumericControlStartFraction);
            float numericInputWidth = Math.Min(
                availableNumericInputWidth,
                controlLimitedNumericInputWidth);
            bool canFit = numericInputWidth >=
                PreferredNumericInputWidth - LayoutPrecisionTolerance;

            float numericControlEnd = rowX +
                (numericInputWidth * NumericControlStartFraction) +
                NumericControlWidth;
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
                NumericControlWidth +
                (PreferredNumericInputWidth * NumericControlStartFraction);
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
