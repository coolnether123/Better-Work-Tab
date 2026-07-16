using System;

namespace Better_Work_Tab.UI.WorkGrid.Commands
{
    /// <summary>Pure validation and cycle rules shared by commands and drawing-free tests.</summary>
    internal static class WorkGridCommandMath
    {
        internal static bool IsValidPriority(int priority, int maxPriority)
        {
            return maxPriority >= 1 && priority >= 0 && priority <= maxPriority;
        }

        internal static int ClampPriority(int priority, int maxPriority)
        {
            if (maxPriority < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPriority));
            }

            return Math.Max(0, Math.Min(priority, maxPriority));
        }

        internal static int StepPriority(int currentPriority, int direction, int maxPriority)
        {
            int current = ClampPriority(currentPriority, maxPriority);
            if (direction == 0)
            {
                return current;
            }

            int next = current + Math.Sign(direction);
            if (next < 0)
            {
                return maxPriority;
            }

            return next > maxPriority ? 0 : next;
        }

        internal static int TogglePriority(int currentPriority, int defaultEnabledPriority, int maxPriority)
        {
            return ClampPriority(currentPriority, maxPriority) > 0
                ? 0
                : ClampPriority(defaultEnabledPriority, maxPriority);
        }

        internal static bool IsValidHour(int hour)
        {
            return hour >= 0 && hour < 24;
        }

        internal static bool IsValidMove(string workGiverDefName, string workTypeDefName, int insertIndex)
        {
            return !System.StringCompat.IsNullOrWhiteSpace(workGiverDefName) &&
                   !System.StringCompat.IsNullOrWhiteSpace(workTypeDefName) &&
                   insertIndex >= 0;
        }
    }
}
