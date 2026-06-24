using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.WorkGiverReassignments;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Better_Work_Tab.Patches
{
    /// <summary>
    /// Backport of the 1.6 "do once / open work tab" float-menu options to 1.5.
    /// The 1.5 API builds options in FloatMenuMakerMap.AddJobGiverWorkOrders, so we
    /// add our extras in a postfix while keeping vanilla options intact.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuMakerMap), "AddJobGiverWorkOrders")]
    public static class Patch_FloatMenuMakerMap_AddJobGiverWorkOrders
    {
        public static void Postfix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts, bool drafted)
        {
            DoOnceSupport.EnsureBwtOwnsUnassignedWorkMenu();

            if (pawn?.workSettings == null)
            {
                return;
            }

            IntVec3 clickCell = IntVec3.FromVector3(clickPos);
            if (pawn.Map == null || !clickCell.InBounds(pawn.Map))
            {
                return;
            }

            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (pawn.WorkTypeIsDisabled(workType))
                {
                    continue;
                }

                int parentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
                bool parentDisabled = parentPriority == WorkPrioritySystem.DisabledPriority;
                foreach (WorkGiverDef workGiver in workType.workGiversByPriority)
                {
                    if (drafted && !workGiver.canBeDoneWhileDrafted)
                    {
                        continue;
                    }

                    if (workGiver.Worker is not WorkGiver_Scanner scanner || !scanner.def.directOrderable)
                    {
                        continue;
                    }

                    int baseWorkGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);
                    int effectiveWorkGiverPriority = TimePriorityService.GetEffectiveWorkGiverPriority(
                        pawn,
                        workType,
                        workGiver,
                        baseWorkGiverPriority);
                    bool timeDisabled = TimePriorityService.TryGetDisabledByTime(
                        pawn,
                        workType,
                        workGiver,
                        out string timeReason,
                        out _);

                    if (!parentDisabled &&
                        effectiveWorkGiverPriority != WorkPrioritySystem.DisabledPriority &&
                        !timeDisabled)
                    {
                        continue;
                    }

                    TryAddThingOption(pawn, clickCell, workGiver, scanner, opts, timeReason);
                    TryAddCellOption(pawn, clickCell, workGiver, scanner, opts, drafted, timeReason);
                }
            }
        }

        private static void TryAddThingOption(
            Pawn pawn,
            IntVec3 clickCell,
            WorkGiverDef workGiver,
            WorkGiver_Scanner scanner,
            List<FloatMenuOption> opts,
            string timeReason)
        {
            Map map = pawn.Map;
            foreach (Thing thing in map.thingGrid.ThingsAt(clickCell))
            {
                if (!scanner.PotentialWorkThingRequest.Accepts(thing))
                {
                    continue;
                }

                if (scanner.ShouldSkip(pawn, true) || !scanner.HasJobOnThing(pawn, thing, true))
                {
                    continue;
                }

                Job job = scanner.JobOnThing(pawn, thing, true);
                if (job == null)
                {
                    continue;
                }

                job.workGiverDef = workGiver;
                AddNotAssignedOptions(pawn, workGiver, scanner, opts, thing, clickCell, job, timeReason);
            }
        }

        private static void TryAddCellOption(
            Pawn pawn,
            IntVec3 clickCell,
            WorkGiverDef workGiver,
            WorkGiver_Scanner scanner,
            List<FloatMenuOption> opts,
            bool drafted,
            string timeReason)
        {
            if (drafted && !workGiver.canBeDoneWhileDrafted)
            {
                return;
            }

            var potentialCells = scanner.PotentialWorkCellsGlobal(pawn);
            if (potentialCells == null || !potentialCells.Contains(clickCell) || scanner.ShouldSkip(pawn, true))
            {
                return;
            }

            Job job = scanner.HasJobOnCell(pawn, clickCell, true) ? scanner.JobOnCell(pawn, clickCell, true) : null;
            if (job == null)
            {
                return;
            }

            job.workGiverDef = workGiver;
            AddNotAssignedOptions(pawn, workGiver, scanner, opts, clickCell, clickCell, job, timeReason);
        }

        private static void AddNotAssignedOptions(
            Pawn pawn,
            WorkGiverDef workGiver,
            WorkGiver_Scanner scanner,
            List<FloatMenuOption> opts,
            LocalTargetInfo target,
            IntVec3 clickedCell,
            Job job,
            string timeReason)
        {
            WorkTypeDef workType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver)
                ?? scanner.def.workType;
            if (workType == null)
            {
                return;
            }

            string doOnceLabel = "BWTNotAssignedDoOnce".Translate(WorkGiverActionLabel(workGiver, workType));
            string openTabLabel = "BWTNotAssignedAssignWork".Translate(WorkTypeMenuLabel(workType));
            string manageWorkGiversLabel = "BWTManageWorkGivers".Translate(
                WorkTypeMenuLabel(workType),
                WorkGiverDisplayNameService.HeaderLabel(workGiver));
            string openScheduleLabel = "Open " + WorkTypeMenuLabel(workType) + " priority schedule";
            int parentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            int baseWorkGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);
            int effectiveWorkGiverPriority = TimePriorityService.GetEffectiveWorkGiverPriority(
                pawn,
                workType,
                workGiver,
                baseWorkGiverPriority);

            if (!timeReason.NullOrEmpty())
            {
                string disabledLabel = WorkGiverActionLabel(workGiver, workType) + ": " + timeReason.CapitalizeFirst();
                if (!opts.Any(o => o.Label == disabledLabel))
                {
                    opts.Add(new FloatMenuOption(disabledLabel, null, orderInPriority: -1));
                }

                if (!opts.Any(o => o.Label == openScheduleLabel))
                {
                    opts.Add(new FloatMenuOption(
                        openScheduleLabel,
                        () => TimePriorityPlannerPrototype.OpenForFloatMenu(pawn, workType, workGiver),
                        orderInPriority: -1));
                }
            }

            if (effectiveWorkGiverPriority == WorkPrioritySystem.DisabledPriority &&
                !opts.Any(o => o.Label == manageWorkGiversLabel))
            {
                opts.Add(new FloatMenuOption(
                    manageWorkGiversLabel,
                    () => OpenWorkGiverManagement(pawn, workType, workGiver),
                    orderInPriority: -1));
            }

            if (!opts.Any(o => o.Label == openTabLabel))
            {
                opts.Add(new FloatMenuOption(
                    openTabLabel,
                    () =>
                    {
                        HighlightState.SetWorktypeToHighlight(pawn, workType);
                        Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Work);
                    },
                    orderInPriority: -1));
            }

            if (opts.Any(o => o.Label == doOnceLabel))
            {
                return;
            }

            void AssignOnce()
            {
                int currentParentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
                int currentWorkGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, currentParentPriority);
                if (currentWorkGiverPriority == WorkPrioritySystem.DisabledPriority)
                {
                    int enabledPriority = currentParentPriority > WorkPrioritySystem.DisabledPriority
                        ? currentParentPriority
                        : WorkPrioritySystem.GetDefaultEnabledPriority();
                    WorkGiverReassignmentManager.SetPawnOverrideSynced(pawn.thingIDNumber, workGiver.defName, enabledPriority);
                }

                if (pawn.jobs.TryTakeOrderedJobPrioritizedWork(job, scanner, clickedCell))
                {
                    if (workGiver.forceMote != null)
                    {
                        MoteMaker.MakeStaticMote(clickedCell, pawn.Map, workGiver.forceMote);
                    }

                    if (workGiver.forceFleck != null)
                    {
                        FleckMaker.Static(clickedCell, pawn.Map, workGiver.forceFleck);
                    }
                }
            }

#if v1_3
            var option = FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(doOnceLabel, AssignOnce, orderInPriority: -1),
                pawn,
                target);
#else
            var option = FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(doOnceLabel, AssignOnce, orderInPriority: -1),
                pawn,
                target,
                layer: scanner.GetReservationLayer(pawn, target));
#endif

            opts.Add(option);
        }

        private static string WorkTypeMenuLabel(WorkTypeDef workType)
        {
            string label = workType?.gerundLabel;
            if (label.NullOrEmpty())
            {
                label = workType?.labelShort ?? workType?.label;
            }

            return label.NullOrEmpty() ? "Work" : label.CapitalizeFirst();
        }

        private static string WorkGiverActionLabel(WorkGiverDef workGiver, WorkTypeDef fallbackWorkType)
        {
            string label = workGiver?.verb;
            if (label.NullOrEmpty())
            {
                label = fallbackWorkType?.labelShort ?? fallbackWorkType?.label;
            }

            return label.NullOrEmpty() ? "Work" : label.CapitalizeFirst();
        }

        private static void OpenWorkGiverManagement(Pawn pawn, WorkTypeDef targetWorkType, WorkGiverDef workGiver)
        {
            if (targetWorkType == null)
            {
                return;
            }

            if (BetterWorkTabMod.Settings?.enableSubWorkDrilldown ?? false)
            {
                TimePriorityPlannerPrototype.CloseForWorkModeTransition();
                SubWorkDrilldownState.Enter(
                    targetWorkType,
                    baseHeaderDrawWidth: SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(null, -1f));
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Work);
                HighlightState.SetSubWorkGiverToHighlight(pawn, targetWorkType, workGiver);
                HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                return;
            }

            var screenPos = new Vector2(Verse.UI.screenWidth / 2f, Verse.UI.screenHeight / 2f);
            bool hasOverride = WorkGiverReassignmentManager.HasAnyPawnOverride(targetWorkType, pawn) ||
                               WorkGiverReassignmentManager.HasPawnOrdering(pawn, targetWorkType);
            Pawn windowPawn = hasOverride ? pawn : null;
            Find.WindowStack.Add(new Window_WorkGiverSubMenu(targetWorkType, screenPos, windowPawn));
        }
    }

    [DefOf]
    public static class MainButtonDefOf
    {
        public static MainButtonDef Work;
    }
}
