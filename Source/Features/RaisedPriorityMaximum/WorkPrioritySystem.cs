using System;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.ModSupport;
using RimWorld;
using System.Reflection;
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
        private static readonly FieldInfo PawnField =
            typeof(Pawn_WorkSettings).GetField("pawn", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static int NormalizeMaxPriority(int value)
        {
            return PriorityAuthorityBroker.ClampMaxPriority(value);
        }

        internal static int GetMaxPriority()
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return PriorityConstants.VanillaMax;
            }

            return PriorityAuthorityBroker.GetEffectiveMaxPriority();
        }

        internal static int GetRequestableMaxPriority()
        {
            return PriorityAuthorityBroker.GetRequestableMaxPriority();
        }

        internal static int ClampPriority(int priority)
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return Mathf.Clamp(priority, DisabledPriority, PriorityConstants.VanillaMax);
            }

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
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return PriorityConstants.VanillaDefaultEnabled;
            }

            return PriorityAuthorityBroker.GetDefaultEnabledPriority();
        }

        internal static int GetPriorityForPawnWorkType(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings == null || workType == null)
            {
                return GetDefaultEnabledPriority();
            }

            return PriorityAuthorityBroker.GetEffectivePriority(pawn, workType);
        }

        internal static int GetCurrentPriorityForPawnWorkType(Pawn pawn, WorkTypeDef workType)
        {
            if (PriorityAuthorityBroker.FluffyWorkTabHasPriorityAuthority)
            {
                return GetPriorityForPawnWorkType(pawn, workType);
            }

            int basePriority = GetPriorityForPawnWorkType(pawn, workType);
            return TimePriorityService.GetEffectiveWorkTypePriority(pawn, workType, basePriority);
        }

        /// <summary>
        /// Writes a work-type priority to Better Work Tab's store, then mirrors it to any external
        /// work-tab mod.
        /// </summary>
        /// <remarks>
        /// Better Work Tab's store is always written, even when an external mod holds authority, so the
        /// extended priority survives whatever narrower range that mod supports. The mirror afterwards
        /// re-states the value within the external mod's limits and repairs any work-giver detail its
        /// own work-type handling flattened.
        /// </remarks>
        internal static void SetPriority(Pawn_WorkSettings workSettings, WorkTypeDef workType, int priority)
        {
            if (workSettings == null || workType == null)
            {
                return;
            }

            Pawn pawn = GetPawn(workSettings);
            if (pawn?.Dead == true || !workSettings.EverWork ||
                (pawn != null && pawn.WorkTypeIsDisabled(workType)))
            {
                return;
            }

            workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            workSettings.SetPriority(workType, ClampPriority(priority));
            ExternalPriorityMirror.NotifyWorkTypeChanged(pawn, workType);
        }

        /// <summary>
        /// Writes a work-type priority straight into Better Work Tab's store, bypassing both
        /// <see cref="Pawn_WorkSettings.SetPriority"/> and the Fluffy mirror.
        /// </summary>
        /// <remarks>
        /// Only for importing <em>out of</em> another work-tab mod. Going through the vanilla setter can
        /// run that mod's own prefix, which may cascade the value over every work giver of the work type
        /// and so destroy the very data the import is reading. Pair it with
        /// <see cref="ModSupport.ExternalPriorityMirror.Suspend"/>.
        /// </remarks>
        internal static void SetStoredPriorityWithoutMirroring(
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType,
            int priority)
        {
            if (workSettings == null || workType == null)
            {
                return;
            }

            Pawn pawn = GetPawn(workSettings);
            if (pawn?.Dead == true || !workSettings.EverWork)
            {
                return;
            }

            workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            int clamped = PriorityAuthorityBroker.ClampPriorityForRequest(priority);
            if (clamped != 0 && pawn != null && pawn.WorkTypeIsDisabled(workType))
            {
                return;
            }

            workSettings.SetPriority(workType, clamped);
            workSettings.Notify_UseWorkPrioritiesChanged();
        }

        internal static int GetPriorityAfterMouseButton(int currentPriority, int button)
        {
            if (button == 0)
            {
                return PriorityAuthorityBroker.GetPriorityAfterClick(currentPriority, -1);
            }

            if (button == 1)
            {
                return PriorityAuthorityBroker.GetPriorityAfterClick(currentPriority, 1);
            }

            return ClampPriority(currentPriority);
        }

        internal static int GetPriorityAfterBoundedStep(int currentPriority, int direction)
        {
            return PriorityAuthorityBroker.GetNextManualPriority(currentPriority, direction);
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

            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return workSettings.GetPriority(workType);
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

            int clampedPriority = ClampPriority(priority, maxPriority);

            int greenThreshold = settings?.priorityColorPercentage_Green ?? DefaultSettings.priorityColorPercentage_Green;
            int yellowThreshold = settings?.priorityColorPercentage_Yellow ?? DefaultSettings.priorityColorPercentage_Yellow;
            int tanThreshold = settings?.priorityColorPercentage_Tan ?? DefaultSettings.priorityColorPercentage_Tan;

            if (clampedPriority <= GetPriorityColorCutoff(maxPriority, greenThreshold))
            {
                return ExtendedPriorityGreen;
            }

            if (clampedPriority <= GetPriorityColorCutoff(maxPriority, yellowThreshold))
            {
                return ExtendedPriorityYellow;
            }

            if (clampedPriority <= GetPriorityColorCutoff(maxPriority, tanThreshold))
            {
                return ExtendedPriorityTan;
            }

            return ExtendedPriorityGrey;
        }

        private static int GetPriorityColorCutoff(int maxPriority, int percentageThreshold)
        {
            int threshold = Mathf.Clamp(percentageThreshold, 1, 100);
            return Mathf.Clamp(Mathf.CeilToInt(maxPriority * (threshold / 100f)), 1, maxPriority);
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


        internal static Pawn GetPawn(Pawn_WorkSettings workSettings)
        {
            return PawnField?.GetValue(workSettings) as Pawn;
        }
    }
}
