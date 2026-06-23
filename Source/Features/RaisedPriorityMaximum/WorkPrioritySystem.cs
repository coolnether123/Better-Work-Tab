using System;
using Better_Work_Tab.Features.TimePriority;
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
        private static readonly Color ExtendedPriorityGreen = new Color(0.2f, 0.8f, 0.2f);
        private static readonly Color ExtendedPriorityYellow = new Color(0.9f, 0.82f, 0.42f);
        private static readonly Color ExtendedPriorityTan = new Color(0.74f, 0.62f, 0.43f);
        private static readonly Color ExtendedPriorityGrey = new Color(0.74f, 0.74f, 0.74f);

        internal static int NormalizeMaxPriority(int value)
        {
            return PriorityAuthorityBroker.ClampMaxPriority(value);
        }

        internal static int GetMaxPriority()
        {
            return PriorityAuthorityBroker.GetEffectiveMaxPriority();
        }

        internal static int GetRequestableMaxPriority()
        {
            return PriorityAuthorityBroker.GetRequestableMaxPriority();
        }

        internal static int ClampPriority(int priority)
        {
            return PriorityAuthorityBroker.ClampPriorityForRequest(priority);
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
            return PriorityAuthorityBroker.GetDefaultEnabledPriority();
        }

        internal static int GetPriorityForPawnWorkType(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings == null || workType == null)
            {
                return GetDefaultEnabledPriority();
            }

            return ClampPriority(pawn.workSettings.GetPriority(workType));
        }

        internal static int GetCurrentPriorityForPawnWorkType(Pawn pawn, WorkTypeDef workType)
        {
            int basePriority = GetPriorityForPawnWorkType(pawn, workType);
            return TimePriorityService.GetEffectiveWorkTypePriority(pawn, workType, basePriority);
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
            int maxPriority = PriorityAuthorityBroker.GetSnapshotForPriority(currentPriority).MaxPriority;
            int normalized = ClampPriority(currentPriority, maxPriority);

            if (direction > 0)
            {
                if (normalized == DisabledPriority)
                {
                    return PriorityAuthorityBroker.GetNextManualPriority(DisabledPriority, 1);
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
            if (maxPriority <= 4)
            {
                return GetVanillaPriorityColor(priority);
            }

            int percentage = (int)(((float)ClampPriority(priority, maxPriority) / maxPriority) * 100f);

            int greenThreshold = settings?.priorityColorPercentage_Green ?? DefaultSettings.priorityColorPercentage_Green;
            int yellowThreshold = settings?.priorityColorPercentage_Yellow ?? DefaultSettings.priorityColorPercentage_Yellow;
            int tanThreshold = settings?.priorityColorPercentage_Tan ?? DefaultSettings.priorityColorPercentage_Tan;

            if (percentage < greenThreshold)
            {
                return ExtendedPriorityGreen;
            }

            if (percentage < yellowThreshold)
            {
                return ExtendedPriorityYellow;
            }

            if (percentage < tanThreshold)
            {
                return ExtendedPriorityTan;
            }

            return ExtendedPriorityGrey;
        }

        private static Color GetVanillaPriorityColor(int priority)
        {
            switch (priority)
            {
                case 1:
                    return new Color(0f, 1f, 0f);
                case 2:
                    return new Color(1f, 0.9f, 0.5f);
                case 3:
                    return new Color(0.8f, 0.7f, 0.5f);
                case 4:
                    return new Color(0.74f, 0.74f, 0.74f);
                default:
                    return Color.grey;
            }
        }

        private static int CycleTowardHigherPriority(int currentPriority)
        {
            return PriorityAuthorityBroker.GetNextManualPriority(currentPriority, 1);
        }

        private static int CycleTowardLowerPriority(int currentPriority)
        {
            return PriorityAuthorityBroker.GetNextManualPriority(currentPriority, -1);
        }
    }
}
