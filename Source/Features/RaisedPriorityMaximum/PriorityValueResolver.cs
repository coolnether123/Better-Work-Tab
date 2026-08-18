using Better_Work_Tab.API;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal static class PriorityValueResolver
    {
        internal static int GetEffectivePriority(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings == null || workType == null)
            {
                return PriorityRangePolicy.GetDefaultEnabledPriority();
            }

            IExternalWorkTabStore authoritativeStore =
                PriorityAuthorityTransitionService.GetAuthoritativeStore();
            if (authoritativeStore != null &&
                ExternalWorkTabRegistry.TryGetWorkTypePriority(
                    authoritativeStore,
                    pawn,
                    workType,
                    TimePriorityService.GetCurrentHour(pawn),
                    out int externalPriority))
            {
                return ClampRuntimePriority(externalPriority);
            }

            return GetBetterWorkTabEffectiveStoredPriority(pawn, workType);
        }

        internal static int GetEffectivePriorityAtHour(Pawn pawn, WorkTypeDef workType, int hour)
        {
            if (pawn?.workSettings == null || workType == null)
            {
                return PriorityRangePolicy.GetDefaultEnabledPriority();
            }

            IExternalWorkTabStore authoritativeStore =
                PriorityAuthorityTransitionService.GetAuthoritativeStore();
            if (authoritativeStore != null &&
                ExternalWorkTabRegistry.TryGetWorkTypePriority(
                    authoritativeStore,
                    pawn,
                    workType,
                    hour,
                    out int externalPriority))
            {
                return ClampRuntimePriority(externalPriority);
            }

            int basePriority = GetBetterWorkTabEffectiveStoredPriority(pawn, workType);
            if (!TimePriorityService.IsRuntimeActive)
            {
                return basePriority;
            }

            return TimePriorityService.GetPriorityAtHour(
                TimePriorityTarget.ForRuntimeWorkType(pawn, workType),
                basePriority,
                hour);
        }

        internal static int GetBetterWorkTabStoredPriority(
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType)
        {
            if (workSettings?.priorities == null || workType == null)
            {
                return PriorityRangePolicy.GetDefaultEnabledPriority();
            }

            return PriorityRangePolicy.ClampStoredPriorityForRuntime(workSettings.priorities[workType]);
        }

        private static int GetBetterWorkTabEffectiveStoredPriority(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings?.priorities == null || workType == null)
            {
                return PriorityRangePolicy.GetDefaultEnabledPriority();
            }

            int storedPriority = GetVanillaCompatibleStoredPriority(pawn, pawn.workSettings, workType);
            return PriorityRangePolicy.ClampStoredPriorityForRuntime(storedPriority);
        }

        internal static int GetVanillaCompatibleStoredPriority(
            Pawn pawn,
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType)
        {
            if (pawn?.RaceProps == null || workSettings?.priorities == null || workType == null)
            {
                return PriorityRangePolicy.GetDefaultEnabledPriority();
            }

            int storedPriority = workSettings.priorities[workType];
            return pawn.RaceProps.Humanlike &&
                   storedPriority > PriorityConstants.Disabled &&
                   Find.PlaySettings != null &&
                   !Find.PlaySettings.useWorkPriorities
                ? PriorityConstants.VanillaDefaultEnabled
                : storedPriority;
        }

        internal static int GetBetterWorkTabEffectiveWorkGiverPriorityAtHour(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int hour)
        {
            int parentPriority = GetEffectivePriorityAtHour(pawn, workType, hour);
            int fallback = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);
            if (!TimePriorityService.IsRuntimeActive)
            {
                return fallback;
            }

            return TimePriorityService.GetPriorityAtHour(
                TimePriorityTarget.ForRuntimeWorkGiver(pawn, workType, workGiver),
                fallback,
                hour);
        }

        private static int ClampRuntimePriority(int priority)
        {
            if (priority < PriorityConstants.Disabled)
            {
                return PriorityConstants.Disabled;
            }

            return priority > PriorityConstants.ExtendedHardMax
                ? PriorityConstants.ExtendedHardMax
                : priority;
        }
    }
}
