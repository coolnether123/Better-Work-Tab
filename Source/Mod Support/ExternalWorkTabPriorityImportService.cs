using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.API;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Foundation.GameState;
using Better_Work_Tab.Mod_Support.Multiplayer;
using RimWorld;
using Verse;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// Converts external per-pawn/per-work-giver 24-hour arrays into Better Work Tab's hierarchy.
    /// </summary>
    /// <remarks>
    /// External importers supply leaves only. BWT derives the parent work-type schedule from the best
    /// enabled child priority for each hour, stores the modal fallback priority, and preserves child
    /// schedules only where they differ from that derived parent. Mirroring is suspended throughout so
    /// BWT cannot echo half-imported state back into the external store.
    /// </remarks>
    internal static class ExternalWorkTabPriorityImportService
    {
        internal static int Import(IEnumerable<ExternalPawnWorkGiverPriorityRecord> records)
        {
            WorkTabApplication application = WorkTabGameRoots.For(Current.Game)?.Application;
            return application == null ? 0 : Import(application, records);
        }

        internal static int Import(
            GameComponent_BWTWorldSettings component,
            IEnumerable<ExternalPawnWorkGiverPriorityRecord> records)
        {
            return TryImport(component, records, out int changed) ? changed : 0;
        }

        internal static bool TryImport(
            GameComponent_BWTWorldSettings component,
            IEnumerable<ExternalPawnWorkGiverPriorityRecord> records,
            out int changed)
        {
            return TryImport(component?.Root?.Application, records, out changed);
        }

        private static int Import(
            WorkTabApplication application,
            IEnumerable<ExternalPawnWorkGiverPriorityRecord> records)
        {
            return TryImport(application, records, out int changed) ? changed : 0;
        }

        private static bool TryImport(
            WorkTabApplication application,
            IEnumerable<ExternalPawnWorkGiverPriorityRecord> records,
            out int changed)
        {
            changed = 0;
            if (application == null || MultiplayerBridge.Active)
            {
                return false;
            }

            var import = new WorkTabTrustedImport
            {
                RequiredPriorityMaximum = PriorityAuthorityBroker.ClampMaxPriority(
                    (records ?? Enumerable.Empty<ExternalPawnWorkGiverPriorityRecord>())
                        .SelectMany(record => record.WorkGivers)
                        .SelectMany(record => ExternalWorkTabPriorityArrayNormalizer.Normalize(
                            record.Priorities,
                            WorkPrioritySystem.DisabledPriority))
                        .DefaultIfEmpty(PriorityConstants.VanillaMax)
                        .Max())
            };
            BuildImport(
                (records ?? Enumerable.Empty<ExternalPawnWorkGiverPriorityRecord>()).ToList(),
                import);
            changed = application.ApplyTrustedCompatibilityImport(import);
            return changed >= 0;
        }

        private static void BuildImport(
            List<ExternalPawnWorkGiverPriorityRecord> records,
            WorkTabTrustedImport import)
        {
            foreach (ExternalPawnWorkGiverPriorityRecord pawnRecord in records)
            {
                Pawn pawn = pawnRecord.Pawn;
                if (pawn?.workSettings == null)
                {
                    continue;
                }

                foreach (IGrouping<WorkTypeDef, ExternalWorkGiverPriorityRecord> group in pawnRecord.WorkGivers
                             .Where(record => record.WorkGiver?.workType != null)
                             .GroupBy(record => record.WorkGiver.workType))
                {
                    WorkTypeDef workType = group.Key;
                    if (workType == null || pawn.WorkTypeIsDisabled(workType))
                    {
                        continue;
                    }

                    List<ExternalWorkGiverPriorityRecord> workGiverPriorities = group.ToList();
                    int[] parentPriorities = BuildWorkTypePriorities(workGiverPriorities);
                    int parentFallback = ChooseFallbackPriority(parentPriorities);

                    import.ParentPriorities.Add(
                        new WorkTabTrustedParentPriority(pawn, workType, parentFallback));

                    TimePriorityTarget workTypeTarget = TimePriorityTarget.ForWorkType(pawn, workType);
                    import.Schedules.Add(CreateScheduleCommand(workTypeTarget, parentPriorities, parentFallback));

                    foreach (ExternalWorkGiverPriorityRecord workGiverPriority in workGiverPriorities)
                    {
                        WorkGiverDef workGiver = workGiverPriority.WorkGiver;
                        int[] normalizedPriorities = ExternalWorkTabPriorityArrayNormalizer.Normalize(
                            workGiverPriority.Priorities,
                            parentFallback);
                        if (workGiver == null)
                        {
                            continue;
                        }

                        TimePriorityTarget workGiverTarget =
                            TimePriorityTarget.ForWorkGiver(pawn, workGiver);
                        if (normalizedPriorities.SequenceEqual(parentPriorities))
                        {
                            import.SpecificPriorities.Add(
                                new WorkTabTrustedSpecificPriority(pawn, workGiver, null));
                            import.Schedules.Add(new WorkTabScheduleCommand(
                                workGiverTarget, TimePriorityScheduleValue.AllLinked, parentFallback));

                            continue;
                        }

                        int workGiverFallback = ChooseFallbackPriority(normalizedPriorities);
                        import.SpecificPriorities.Add(
                            new WorkTabTrustedSpecificPriority(pawn, workGiver, workGiverFallback));
                        import.Schedules.Add(CreateScheduleCommand(
                            workGiverTarget, normalizedPriorities, workGiverFallback));
                    }
                }
            }

        }

        private static WorkTabScheduleCommand CreateScheduleCommand(
            TimePriorityTarget target,
            int[] priorities,
            int fallbackPriority)
        {
            int[] normalized = ExternalWorkTabPriorityArrayNormalizer.Normalize(priorities, fallbackPriority);
            return new WorkTabScheduleCommand(
                target,
                TimePriorityService.CreateAllHoursPinnedValue(normalized, fallbackPriority),
                fallbackPriority);
        }

        private static int[] BuildWorkTypePriorities(List<ExternalWorkGiverPriorityRecord> workGiverPriorities)
        {
            List<int[]> normalizedPriorities = workGiverPriorities
                .Select(record => ExternalWorkTabPriorityArrayNormalizer.Normalize(
                    record.Priorities,
                    WorkPrioritySystem.DisabledPriority))
                .ToList();
            var result = new int[TimePriorityService.HoursPerDay];
            for (int hour = 0; hour < result.Length; hour++)
            {
                int best = 0;
                for (int i = 0; i < normalizedPriorities.Count; i++)
                {
                    int priority = normalizedPriorities[i][hour];
                    if (priority <= WorkPrioritySystem.DisabledPriority)
                    {
                        continue;
                    }

                    if (best == 0 || priority < best)
                    {
                        best = priority;
                    }
                }

                result[hour] = best;
            }

            return result;
        }

        private static int ChooseFallbackPriority(int[] priorities)
        {
            return ExternalWorkTabPriorityArrayNormalizer.Normalize(priorities, WorkPrioritySystem.DisabledPriority)
                .Where(priority => priority > WorkPrioritySystem.DisabledPriority)
                .GroupBy(priority => priority)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => group.Key)
                .DefaultIfEmpty(WorkPrioritySystem.DisabledPriority)
                .First();
        }

    }
}
