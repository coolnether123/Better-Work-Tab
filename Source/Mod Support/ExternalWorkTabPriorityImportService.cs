using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.API;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.UI.WorkGiverReassignments;
using RimWorld;
using Verse;
using Current = Verse.Current;

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
            if (component == null)
            {
                return 0;
            }

            component.EnsureWorkGiverReassignmentData();
            using (ExternalPriorityMirror.Suspend())
            {
                int changed = ImportSuspended(
                    component,
                    (records ?? Enumerable.Empty<ExternalPawnWorkGiverPriorityRecord>()).ToList());
                if (changed > 0)
                {
                    TimePriorityService.NotifyLoaded();
                    WorkGiverReassignmentManager.InvalidateCaches();
                    WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
                    MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                }

                return changed;
            }
        }

        private static int ImportSuspended(
            GameComponent_BWTWorldSettings component,
            List<ExternalPawnWorkGiverPriorityRecord> records)
        {
            int changed = 0;
            int importedMax = records
                .SelectMany(record => record.WorkGivers)
                .SelectMany(record => NormalizePriorities(record.Priorities, WorkPrioritySystem.DisabledPriority))
                .DefaultIfEmpty(PriorityConstants.VanillaMax)
                .Max();
            EnsurePriorityRangeForImport(importedMax);

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

                    WorkPrioritySystem.SetStoredPriorityWithoutMirroring(pawn.workSettings, workType, parentFallback);
                    changed++;

                    TimePriorityTarget workTypeTarget = TimePriorityTarget.ForWorkType(pawn, workType);
                    changed += SetSchedule(workTypeTarget, parentPriorities, parentFallback);

                    foreach (ExternalWorkGiverPriorityRecord workGiverPriority in workGiverPriorities)
                    {
                        WorkGiverDef workGiver = workGiverPriority.WorkGiver;
                        int[] normalizedPriorities = NormalizePriorities(
                            workGiverPriority.Priorities,
                            parentFallback);
                        if (workGiver == null || ArraysEqual(normalizedPriorities, parentPriorities))
                        {
                            continue;
                        }

                        int workGiverFallback = ChooseFallbackPriority(normalizedPriorities);
                        WorkGiverReassignmentManager.SetPawnOverrideSynced(
                            pawn.thingIDNumber,
                            workGiver.defName,
                            workGiverFallback);
                        changed++;

                        TimePriorityTarget workGiverTarget = TimePriorityTarget.ForWorkGiver(
                            pawn,
                            workType,
                            workGiver,
                            WorkGiverDisplayNameService.HeaderLabel(workGiver));
                        changed += SetSchedule(workGiverTarget, normalizedPriorities, workGiverFallback);
                    }
                }
            }

            return changed;
        }

        private static void EnsurePriorityRangeForImport(int importedMax)
        {
            importedMax = PriorityAuthorityBroker.ClampMaxPriority(importedMax);
            if (importedMax <= PriorityConstants.VanillaMax)
            {
                return;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            if (settings.maxPriorityInt < importedMax)
            {
                settings.maxPriorityInt = importedMax;
            }

            if (settings.priorityMode == PriorityMode.Vanilla)
            {
                settings.SetPriorityMode(PriorityMode.Auto);
            }

            settings.NormalizePrioritySettings();
            PriorityAuthorityBroker.InvalidateCaches();
        }

        private static int SetSchedule(
            TimePriorityTarget target,
            int[] priorities,
            int fallbackPriority)
        {
            int[] normalized = NormalizePriorities(priorities, fallbackPriority);
            if (normalized.All(priority => priority == WorkPrioritySystem.ClampPriority(fallbackPriority)))
            {
                TimePriorityService.ClearSchedule(target);
                return 0;
            }

            TimePriorityService.SetPrioritiesSynced(target, normalized, fallbackPriority);
            return 1;
        }

        private static int[] BuildWorkTypePriorities(List<ExternalWorkGiverPriorityRecord> workGiverPriorities)
        {
            List<int[]> normalizedPriorities = workGiverPriorities
                .Select(record => NormalizePriorities(record.Priorities, WorkPrioritySystem.DisabledPriority))
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
            return NormalizePriorities(priorities, WorkPrioritySystem.DisabledPriority)
                .Where(priority => priority > WorkPrioritySystem.DisabledPriority)
                .GroupBy(priority => priority)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => group.Key)
                .DefaultIfEmpty(WorkPrioritySystem.DisabledPriority)
                .First();
        }

        private static int[] NormalizePriorities(int[] priorities, int fallbackPriority)
        {
            var normalized = new int[TimePriorityService.HoursPerDay];
            fallbackPriority = ClampImportedPriority(fallbackPriority);
            for (int i = 0; i < normalized.Length; i++)
            {
                normalized[i] = ClampImportedPriority(
                    priorities != null && i < priorities.Length
                        ? priorities[i]
                        : fallbackPriority);
            }

            return normalized;
        }

        private static int ClampImportedPriority(int priority)
        {
            return Math.Max(
                WorkPrioritySystem.DisabledPriority,
                Math.Min(PriorityConstants.ExtendedHardMax, priority));
        }

        private static bool ArraysEqual(int[] left, int[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
