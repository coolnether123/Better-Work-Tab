using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    /// <summary>
    /// Central rules for manual work priorities, including extended priority ranges.
    /// </summary>
    internal static class WorkPrioritySystem
    {
        internal const int DisabledPriority = 0;
        private const int VanillaDefaultEnabledPriority = 3;

        internal static int NormalizeMaxPriority(int value)
        {
            return BetterWorkTabSettings.NormalizeMaxPriority(value);
        }

        internal static int GetMaxPriority()
        {
            return BetterWorkTabMod.Settings?.EffectiveMaxPriority
                ?? NormalizeMaxPriority(DefaultSettings.maxPriority);
        }

        internal static int ClampPriority(int priority)
        {
            return ClampPriority(priority, GetMaxPriority());
        }

        internal static int ClampPriority(int priority, int maxPriority)
        {
            return Mathf.Clamp(priority, DisabledPriority, NormalizeMaxPriority(maxPriority));
        }

        internal static int OffsetPriorityNumber(int priority, int amount)
        {
            return ClampPriority(priority + amount);
        }

        internal static int GetDefaultEnabledPriority()
        {
            return Mathf.Clamp(VanillaDefaultEnabledPriority, 1, GetMaxPriority());
        }

        internal static int GetPriorityForPawnWorkType(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings == null || workType == null)
            {
                return GetDefaultEnabledPriority();
            }

            return ClampPriority(pawn.workSettings.GetPriority(workType));
        }

        internal static void SetPriority(Pawn_WorkSettings workSettings, WorkTypeDef workType, int priority)
        {
            if (workSettings == null || workType == null)
            {
                return;
            }

            workSettings.SetPriority(workType, ClampPriority(priority));
        }

        internal static int GetPriorityAfterMouseButton(int currentPriority, int button)
        {
            if (button == 0)
            {
                return CycleTowardHigherPriority(currentPriority);
            }

            if (button == 1)
            {
                return CycleTowardLowerPriority(currentPriority);
            }

            return ClampPriority(currentPriority);
        }

        internal static int GetPriorityAfterBoundedStep(int currentPriority, int direction)
        {
            int maxPriority = GetMaxPriority();
            int normalized = ClampPriority(currentPriority, maxPriority);

            if (direction > 0)
            {
                if (normalized == DisabledPriority)
                {
                    return maxPriority;
                }

                return normalized > 1 ? normalized - 1 : normalized;
            }

            if (direction < 0)
            {
                if (normalized == maxPriority)
                {
                    return DisabledPriority;
                }

                return normalized > DisabledPriority ? normalized + 1 : normalized;
            }

            return normalized;
        }

        internal static int MapPriorityToVanillaDisplay(int priority)
        {
            if (priority <= DisabledPriority)
            {
                return DisabledPriority;
            }

            int maxPriority = GetMaxPriority();
            if (maxPriority <= 1)
            {
                return 1;
            }

            return Mathf.Clamp((int)Math.Round(Spine.Utils.SpineUtils.Remap(priority, 1, maxPriority, 1, 4)), 1, 4);
        }

        internal static int GetTooltipPriority(Pawn_WorkSettings workSettings, WorkTypeDef workType)
        {
            if (workSettings == null || workType == null)
            {
                return DisabledPriority;
            }

            return MapPriorityToVanillaDisplay(workSettings.GetPriority(workType));
        }

        internal static Color GetPriorityColor(int priority)
        {
            if (priority <= DisabledPriority)
            {
                return Color.grey;
            }

            var settings = BetterWorkTabMod.Settings;
            int maxPriority = GetMaxPriority();
            int percentage = (int)(((float)ClampPriority(priority, maxPriority) / maxPriority) * 100f);

            int greenThreshold = settings?.priorityColorPercentage_Green ?? DefaultSettings.priorityColorPercentage_Green;
            int yellowThreshold = settings?.priorityColorPercentage_Yellow ?? DefaultSettings.priorityColorPercentage_Yellow;
            int tanThreshold = settings?.priorityColorPercentage_Tan ?? DefaultSettings.priorityColorPercentage_Tan;

            if (percentage < greenThreshold)
            {
                return new Color(0f, 1f, 0f);
            }

            if (percentage < yellowThreshold)
            {
                return new Color(1f, 0.9f, 0.5f);
            }

            if (percentage < tanThreshold)
            {
                return new Color(0.8f, 0.7f, 0.5f);
            }

            return new Color(0.74f, 0.74f, 0.74f);
        }

        private static int CycleTowardHigherPriority(int currentPriority)
        {
            int maxPriority = GetMaxPriority();
            int nextPriority = ClampPriority(currentPriority, maxPriority) - 1;
            return nextPriority < DisabledPriority ? maxPriority : nextPriority;
        }

        private static int CycleTowardLowerPriority(int currentPriority)
        {
            int maxPriority = GetMaxPriority();
            int nextPriority = ClampPriority(currentPriority, maxPriority) + 1;
            return nextPriority > maxPriority ? DisabledPriority : nextPriority;
        }
    }
}
