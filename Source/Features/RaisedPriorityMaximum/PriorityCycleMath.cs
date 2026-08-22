using System;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal static class PriorityCycleMath
    {
        internal static int StepEnabled(int currentPriority, int maxPriority, bool decrease)
        {
            if (currentPriority < 1 || currentPriority > maxPriority)
            {
                throw new ArgumentOutOfRangeException(nameof(currentPriority));
            }

            if (decrease)
            {
                return currentPriority - 1;
            }

            return currentPriority == maxPriority
                ? PriorityConstants.Disabled
                : currentPriority + 1;
        }
    }
}
