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
    /// Backport of the 1.6 "do once / open work tab" float-menu options.
    /// Older APIs build options in different FloatMenuMakerMap methods, so we
    /// add our extras in a postfix while keeping vanilla options intact.
    /// </summary>
#if vAlpha4
    [HarmonyPatch(typeof(FloatMenuMaker), "ChoicesAtFor")]
    public static class Patch_FloatMenuMakerMap_AddJobGiverWorkOrders
    {
        public static void Postfix(IntVec3 clickSq, Pawn myPawn, List<FloatMenuOption> __result)
        {
            // Alpha4 builds work options directly from JobGiver_WorkRoot inside Verse.FloatMenuMaker.
            // BWT's work-tab integration is still applied through the tab and priority shims.
        }
    }
#else
#if !v0_15
#if v0_18 || v0_17 || v0_16
    [HarmonyPatch(typeof(FloatMenuMakerMap), "ChoicesAtFor")]
#else
    [HarmonyPatch(typeof(FloatMenuMakerMap), "AddJobGiverWorkOrders")]
#endif
    public static class Patch_FloatMenuMakerMap_AddJobGiverWorkOrders
    {
#if v0_18 || v0_17 || v0_16
        public static void Postfix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> __result)
        {
            AddNotAssignedWorkOptions(IntVec3.FromVector3(clickPos), pawn, __result, pawn?.Drafted ?? false);
        }
#elif v1_3 || v1_2 || v1_1 || (v1_0 || v0_19)
        public static void Postfix(IntVec3 clickCell, Pawn pawn, List<FloatMenuOption> opts, bool drafted)
        {
            AddNotAssignedWorkOptions(clickCell, pawn, opts, drafted);
        }
#else
        public static void Postfix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts, bool drafted)
        {
            AddNotAssignedWorkOptions(IntVec3.FromVector3(clickPos), pawn, opts, drafted);
        }
#endif

        private static void AddNotAssignedWorkOptions(IntVec3 clickCell, Pawn pawn, List<FloatMenuOption> opts, bool drafted)
        {
            DoOnceSupport.EnsureBwtOwnsUnassignedWorkMenu();

            // Only relevant if work settings exist.
            if (Better_Work_Tab.PawnCompat.WorkSettings(pawn) == null)
            {
                return;
            }

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
                foreach (WorkGiver workGiver in WorkGiverReassignmentManager.GetOrderedWorkGiversForWorkType(workType, pawn))
                {
                    WorkGiverDef workGiverDef = workGiver?.def;
                    if (workGiverDef == null)
                    {
                        continue;
                    }

                    if (drafted && !WorkGiverCompat.CanBeDoneWhileDrafted(workGiverDef))
                    {
                        continue;
                    }

                    if (workGiver is not WorkGiver_Scanner scanner || !scanner.def.directOrderable)
                    {
                        continue;
                    }

                    int baseWorkGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiverDef, parentPriority);
                    int effectiveWorkGiverPriority = TimePriorityService.GetEffectiveWorkGiverPriority(
                        pawn,
                        workType,
                        workGiverDef,
                        baseWorkGiverPriority);
                    bool timeDisabled = TimePriorityService.TryGetDisabledByTime(
                        pawn,
                        workType,
                        workGiverDef,
                        out string timeReason,
                        out _);

                    if (!parentDisabled &&
                        effectiveWorkGiverPriority != WorkPrioritySystem.DisabledPriority &&
                        !timeDisabled)
                    {
                        continue;
                    }

                    TryAddThingOption(pawn, clickCell, workGiverDef, scanner, opts, timeReason);
                    TryAddCellOption(pawn, clickCell, workGiverDef, scanner, opts, drafted, timeReason);
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

                if (WorkGiverCompat.ShouldSkip(scanner, pawn, true) || !WorkGiverCompat.HasJobOnThing(scanner, pawn, thing, true))
                {
                    continue;
                }

                Job job = WorkGiverCompat.JobOnThing(scanner, pawn, thing, true);
                if (job == null)
                {
                    continue;
                }

                JobCompat.SetWorkGiverDef(job, workGiver);
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
            if (drafted && !WorkGiverCompat.CanBeDoneWhileDrafted(workGiver))
            {
                return;
            }

            var potentialCells = scanner.PotentialWorkCellsGlobal(pawn);
            if (potentialCells == null || !potentialCells.Contains(clickCell) || WorkGiverCompat.ShouldSkip(scanner, pawn, true))
            {
                return;
            }

            Job job = WorkGiverCompat.HasJobOnCell(scanner, pawn, clickCell, true)
                ? WorkGiverCompat.JobOnCell(scanner, pawn, clickCell, true)
                : null;
            if (job == null)
            {
                return;
            }

            JobCompat.SetWorkGiverDef(job, workGiver);
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
#if v1_2 || v1_1 || v1_0 || v0_19 || v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
                    opts.Add(new FloatMenuOption(disabledLabel, null, priority: MenuOptionPriority.VeryLow));
#else
                    opts.Add(new FloatMenuOption(disabledLabel, null, orderInPriority: -1));
#endif
                }

                if (!opts.Any(o => o.Label == openScheduleLabel))
                {
#if v1_2 || v1_1 || v1_0 || v0_19 || v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
                    opts.Add(new FloatMenuOption(
                        openScheduleLabel,
                        () => TimePriorityPlannerPrototype.OpenForFloatMenu(pawn, workType, workGiver),
                        priority: MenuOptionPriority.VeryLow));
#else
                    opts.Add(new FloatMenuOption(
                        openScheduleLabel,
                        () => TimePriorityPlannerPrototype.OpenForFloatMenu(pawn, workType, workGiver),
                        orderInPriority: -1));
#endif
                }
            }

            if (effectiveWorkGiverPriority == WorkPrioritySystem.DisabledPriority &&
                !opts.Any(o => o.Label == manageWorkGiversLabel))
            {
#if v1_2 || v1_1 || v1_0 || v0_19 || v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
                opts.Add(new FloatMenuOption(
                    manageWorkGiversLabel,
                    () => OpenWorkGiverManagement(pawn, workType, workGiver),
                    priority: MenuOptionPriority.VeryLow));
#else
                opts.Add(new FloatMenuOption(
                    manageWorkGiversLabel,
                    () => OpenWorkGiverManagement(pawn, workType, workGiver),
                    orderInPriority: -1));
#endif
            }

            if (!opts.Any(o => o.Label == openTabLabel))
            {
#if v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
                opts.Add(new FloatMenuOption(
                    openTabLabel,
                    () =>
                    {
                        HighlightState.SetWorktypeToHighlight(pawn, workType);
                        Find.MainTabsRoot.SetCurrentTab(DefDatabase<MainTabDef>.GetNamed("Work", false));
                    },
                    MenuOptionPriority.Low));
#elif v1_2 || v1_1 || (v1_0 || v0_19)
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
                    orderInPriority: -1));
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
                    WorkGiverReassignmentManager.SetPawnOverrideSynced(pawn.thingIDNumber, workGiver.defName, enabledPriority);
                }

                if (pawn.jobs.TryTakeOrderedJobPrioritizedWork(job, scanner, clickedCell))
                {
                    WorkGiverCompat.TryPlaceForceFeedback(workGiver, clickedCell, pawn.Map);

#if !v1_2 && !v1_1 && !(v1_0 || v0_19)
                    if (workGiver.forceFleck != null)
                    {
                        FleckMaker.Static(clickedCell, pawn.Map, workGiver.forceFleck);
                    }
#endif
                }
            }

#if v0_15 || v0_16
            var option = new FloatMenuOption(doOnceLabel, AssignOnce, MenuOptionPriority.Low);
#elif v1_2 || v1_1 || (v1_0 || v0_19)
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
#endif


#if !v0_16
    [DefOf]
    public static class MainButtonDefOf
    {
        public static MainButtonDef Work;
    }
#endif
#endif
}
