using System;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;

namespace Better_Work_Tab.ModSupport
{
    internal static class ExternalWorkTabPriorityArrayNormalizer
    {
        internal static int[] Normalize(int[] priorities, int fallbackPriority)
        {
            var normalized = new int[TimePriorityService.HoursPerDay];
            fallbackPriority = Math.Max(
                WorkPrioritySystem.DisabledPriority,
                Math.Min(PriorityConstants.ExtendedHardMax, fallbackPriority));
            for (int i = 0; i < normalized.Length; i++)
            {
                int priority = priorities != null && i < priorities.Length
                    ? priorities[i]
                    : fallbackPriority;
                normalized[i] = Math.Max(
                    WorkPrioritySystem.DisabledPriority,
                    Math.Min(PriorityConstants.ExtendedHardMax, priority));
            }

            return normalized;
        }
    }
}
