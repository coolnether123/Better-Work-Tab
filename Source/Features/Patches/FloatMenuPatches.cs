using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
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
#if v1_2 || v1_3
        public static void Postfix(IntVec3 clickCell, Pawn pawn, List<FloatMenuOption> opts, bool drafted)
        {
            // Only relevant if work settings exist.
            if (pawn?.workSettings == null)
            {
                return;
            }

            if (pawn.Map == null || !clickCell.InBounds(pawn.Map))
            {
                return;
            }
#else
        public static void Postfix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts, bool drafted)
        {
            if (pawn?.workSettings == null)
            {
                return;
            }

            IntVec3 clickCell = IntVec3.FromVector3(clickPos);
            if (pawn.Map == null || !clickCell.InBounds(pawn.Map))
            {
                return;
            }
#endif

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

                    int workGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);
                    if (!parentDisabled && workGiverPriority != WorkPrioritySystem.DisabledPriority)
                    {
                        continue;
                    }

                    TryAddThingOption(pawn, clickCell, workGiver, scanner, opts);
                    TryAddCellOption(pawn, clickCell, workGiver, scanner, opts, drafted);
                }
            }
        }

        private static void TryAddThingOption(Pawn pawn, IntVec3 clickCell, WorkGiverDef workGiver, WorkGiver_Scanner scanner, List<FloatMenuOption> opts)
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
                AddNotAssignedOptions(pawn, workGiver, scanner, opts, thing, clickCell, job);
            }
        }

        private static void TryAddCellOption(Pawn pawn, IntVec3 clickCell, WorkGiverDef workGiver, WorkGiver_Scanner scanner, List<FloatMenuOption> opts, bool drafted)
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
            AddNotAssignedOptions(pawn, workGiver, scanner, opts, clickCell, clickCell, job);
        }

        private static void AddNotAssignedOptions(Pawn pawn, WorkGiverDef workGiver, WorkGiver_Scanner scanner, List<FloatMenuOption> opts, LocalTargetInfo target, IntVec3 clickedCell, Job job)
        {
            WorkTypeDef workType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver)
                ?? scanner.def.workType;
            if (workType == null)
            {
                return;
            }

            string doOnceLabel = "BWTNotAssignedDoOnce".Translate(workType.gerundLabel);
            string openTabLabel = "BWTNotAssignedAssignWork".Translate(workType.gerundLabel);
            string manageWorkGiversLabel = "BWTManageWorkGivers".Translate(workType.labelShort);
            int parentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            int workGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);

            if (workGiverPriority == WorkPrioritySystem.DisabledPriority &&
                !opts.Any(o => o.Label == manageWorkGiversLabel))
            {
#if v1_2 || v1_1 || v1_0 || v0_19 || v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
                opts.Add(new FloatMenuOption(
                    manageWorkGiversLabel,
                    () =>
                    {
                        var screenPos = new Vector2(Verse.UI.screenWidth / 2f, Verse.UI.screenHeight / 2f);
                        bool hasOverride = WorkGiverReassignmentManager.HasAnyPawnOverride(workType, pawn) ||
                                           WorkGiverReassignmentManager.HasPawnOrdering(pawn, workType);
                        Pawn windowPawn = hasOverride ? pawn : null;
                        Find.WindowStack.Add(new UI.WorkGiverReassignments.Window_WorkGiverSubMenu(workType, screenPos, windowPawn));
                    },
                    priority: MenuOptionPriority.VeryLow));
#else
                opts.Add(new FloatMenuOption(
                    manageWorkGiversLabel,
                    () =>
                    {
                        var screenPos = new Vector2(Verse.UI.screenWidth / 2f, Verse.UI.screenHeight / 2f);
                        bool hasOverride = WorkGiverReassignmentManager.HasAnyPawnOverride(workType, pawn) ||
                                           WorkGiverReassignmentManager.HasPawnOrdering(pawn, workType);
                        Pawn windowPawn = hasOverride ? pawn : null;
                        Find.WindowStack.Add(new UI.WorkGiverReassignments.Window_WorkGiverSubMenu(workType, screenPos, windowPawn));
                    },
                    orderInPriority: (int)MenuOptionPriority.VeryLow));
#endif
            }

            if (!opts.Any(o => o.Label == openTabLabel))
            {
#if v1_2 || v1_1 || v1_0 || v0_19 || v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
                opts.Add(new FloatMenuOption(
                    openTabLabel,
                    () =>
                    {
                        HighlightState.SetWorktypeToHighlight(pawn, workType);
                        Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Work);
                    },
                    priority: MenuOptionPriority.VeryLow));
#else
                opts.Add(new FloatMenuOption(
                    openTabLabel,
                    () =>
                    {
                        HighlightState.SetWorktypeToHighlight(pawn, workType);
                        Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Work);
                    },
                    orderInPriority: (int)MenuOptionPriority.VeryLow));
#endif
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
                    WorkGiverReassignmentManager.SyncSetPawnOverride(pawn.thingIDNumber, workGiver.defName, enabledPriority);
                }

                if (pawn.jobs.TryTakeOrderedJobPrioritizedWork(job, scanner, clickedCell))
                {
                    if (workGiver.forceMote != null)
                    {
                        MoteMaker.MakeStaticMote(clickedCell, pawn.Map, workGiver.forceMote);
                    }

#if !v1_2 && !v1_1 && !v1_0 && !v0_19
                    if (workGiver.forceFleck != null)
                    {
                        FleckMaker.Static(clickedCell, pawn.Map, workGiver.forceFleck);
                    }
#endif
                }
            }

#if v1_2
            var option = FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(doOnceLabel, AssignOnce),
                pawn,
                target);
#elif v1_3
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
    }


    [DefOf]
    public static class MainButtonDefOf
    {
        public static MainButtonDef Work;
    }
}
