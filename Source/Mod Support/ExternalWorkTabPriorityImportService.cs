using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.API;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.UI.WorkGiverReassignments;
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
            var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            return component == null ? 0 : Import(component, records);
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
            changed = 0;
            if (component == null || MultiplayerBridge.Active)
            {
                return false;
            }

            component.EnsureWorkGiverReassignmentData();
            bool subWorkDataChanged = false;
            var scheduleCommands = new List<WorkTabScheduleCommand>();
            using (ExternalPriorityMirror.Suspend())
            {
                try
                {
                    using (WorkGiverReassignmentManager.BeginMutationBatch())
                    {
                        changed = ImportSuspended(
                            component,
                            (records ?? Enumerable.Empty<ExternalPawnWorkGiverPriorityRecord>()).ToList(),
                            scheduleCommands);
                    }
                }
                finally
                {
                    subWorkDataChanged = WorkGiverReassignmentManager.CommitMutationBatch();
                    WorkTabApplicationDimensions dimensions = changed > 0
                        ? WorkTabApplicationDimensions.ParentPriority
                        : WorkTabApplicationDimensions.None;
                    if (subWorkDataChanged) dimensions |= WorkTabApplicationDimensions.SpecificPriority;
                    changed += component.Application.ApplyImportedScheduleBatch(
                        scheduleCommands, dimensions, broadScope: true);
                }
            }

            return true;
        }

        private static int ImportSuspended(
            GameComponent_BWTWorldSettings component,
            List<ExternalPawnWorkGiverPriorityRecord> records,
            List<WorkTabScheduleCommand> scheduleCommands)
        {
            int importedMax = records
                .SelectMany(record => record.WorkGivers)
                .SelectMany(record => ExternalWorkTabPriorityArrayNormalizer.Normalize(
                    record.Priorities,
                    WorkPrioritySystem.DisabledPriority))
                .DefaultIfEmpty(PriorityConstants.VanillaMax)
                .Max();
            int changed = EnsurePriorityRangeForImport(importedMax) ? 1 : 0;

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

                    int normalizedParentFallback = WorkPrioritySystem.ClampPriority(parentFallback);
                    if (PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                            pawn.workSettings,
                            workType) != normalizedParentFallback)
                    {
                        if (WorkPrioritySystem.SetStoredPriorityWithoutMirroring(
                                pawn.workSettings, workType, parentFallback)) changed++;
                    }

                    TimePriorityTarget workTypeTarget = TimePriorityTarget.ForWorkType(pawn, workType);
                    scheduleCommands.Add(CreateScheduleCommand(workTypeTarget, parentPriorities, parentFallback));

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
                            if (WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                                    pawn,
                                    workGiver,
                                    out _))
                            {
                                WorkGiverReassignmentManager.ClearPawnOverrideSynced(
                                    pawn.thingIDNumber,
                                    workGiver.defName);
                                changed++;
                            }

                            scheduleCommands.Add(new WorkTabScheduleCommand(
                                workGiverTarget, TimePriorityScheduleValue.AllLinked, parentFallback));

                            continue;
                        }

                        int workGiverFallback = ChooseFallbackPriority(normalizedPriorities);
                        int normalizedWorkGiverFallback = WorkPrioritySystem.ClampPriority(workGiverFallback);
                        if (!WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                                pawn,
                                workGiver,
                                out int currentWorkGiverFallback) ||
                            currentWorkGiverFallback != normalizedWorkGiverFallback)
                        {
                            if (WorkGiverReassignmentManager.SetPawnOverrideFromTrustedImport(
                                    pawn, workGiver, workGiverFallback)) changed++;
                        }

                        scheduleCommands.Add(CreateScheduleCommand(
                            workGiverTarget, normalizedPriorities, workGiverFallback));
                    }
                }
            }

            return changed;
        }

        private static bool EnsurePriorityRangeForImport(int importedMax)
        {
            importedMax = PriorityAuthorityBroker.ClampMaxPriority(importedMax);
            if (importedMax <= PriorityConstants.VanillaMax)
            {
                return false;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return false;
            }

            bool changed = false;
            if (settings.maxPriorityInt < importedMax)
            {
                settings.maxPriorityInt = importedMax;
                changed = true;
            }

            if (settings.priorityMode == PriorityMode.Vanilla)
            {
                settings.SetPriorityMode(PriorityMode.Auto);
                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            settings.NormalizePrioritySettings();
            PriorityAuthorityBroker.InvalidateCaches();
            return true;
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
