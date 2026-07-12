using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.WorkGiverReassignments;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Better_Work_Tab.Patches
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), nameof(FloatMenuOptionProvider_WorkGivers.GetWorkGiverOption))]
    public static class Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOption
    {
        public static FloatMenuOption Postfix(FloatMenuOption value, Pawn pawn, WorkGiverDef workGiver, LocalTargetInfo target, FloatMenuContext context)
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return value;
            }

            DoOnceSupport.EnsureBwtOwnsUnassignedWorkMenu();

            if (value == null)
            {
                return value;
            }

            if (workGiver.Worker is not WorkGiver_Scanner workGiverScanner)
            {
                return value;
            }

            WorkTypeDef workType = WorkGiverReassignmentManager.GetTargetWorkType(workGiverScanner.def)
                ?? workGiverScanner.def.workType;
            if (workType == null || pawn == null || context == null)
            {
                return value;
            }

            if (TimePriorityService.TryGetDisabledByTime(pawn, workType, workGiver, out string timeReason, out _))
            {
                Job timeBlockedJob = target.HasThing
                    ? (workGiverScanner.HasJobOnThing(pawn, target.Thing, true) ? workGiverScanner.JobOnThing(pawn, target.Thing, true) : null)
                    : (workGiverScanner.HasJobOnCell(pawn, target.Cell, true) ? workGiverScanner.JobOnCell(pawn, target.Cell, true) : null);

                AddOpenPriorityScheduleOption(pawn, workType, workGiver);

                if (timeBlockedJob != null)
                {
                    timeBlockedJob.workGiverDef = workGiverScanner.def;
                    Job forcedTimeJob = timeBlockedJob;
                    WorkGiver_Scanner forcedTimeScanner = workGiverScanner;
                    WorkGiverDef forcedTimeGiver = workGiver;
                    Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor.AdditionalOptions.Add(
                        FloatMenuUtility.DecoratePrioritizedTask(
                            new FloatMenuOption(
                                WorkGiverActionLabel(forcedTimeGiver, workType) + " Once",
                                () => TakePrioritizedJobOnce(pawn, forcedTimeJob, forcedTimeScanner, forcedTimeGiver, context),
                                orderInPriority: -1),
                            pawn,
                            target));
                }

                string disabledLabel = value.Label + ": " + timeReason.CapitalizeFirst();
                return new FloatMenuOption(disabledLabel, null);
            }

            // Check if work TYPE is disabled (vanilla) or this BWT sub-work giver is not assigned.
            int parentPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
            if (parentPriority != WorkPrioritySystem.DisabledPriority)
            {
                if (TryGetBwtSubWorkDisabledReason(pawn, workType, workGiver, parentPriority, out string subWorkReason))
                {
                    return BuildOnceOnlyWorkOption(
                        value,
                        pawn,
                        workType,
                        workGiver,
                        workGiverScanner,
                        target,
                        context,
                        subWorkReason);
                }

                return value;
            }

            if (pawn.WorkTypeIsDisabled(workType))
            {
                return value;
            }

            Job job = target.HasThing
                ? (workGiverScanner.HasJobOnThing(pawn, target.Thing, true) ? workGiverScanner.JobOnThing(pawn, target.Thing, true) : null)
                : (workGiverScanner.HasJobOnCell(pawn, target.Cell, true) ? workGiverScanner.JobOnCell(pawn, target.Cell, true) : null);

            if (job == null)
            {
                return value;
            }

            job.workGiverDef = workGiverScanner.def;
            Job localJob = job;
            WorkGiver_Scanner localScanner = workGiverScanner;
            WorkGiverDef giver = workGiver;

            void AssignOnce()
            {
                TakePrioritizedJobOnce(pawn, localJob, localScanner, giver, context);
            }

            string workLabel = WorkTypeMenuLabel(workType);
            var text = "BWTNotAssignedDoOnce".Translate(WorkGiverActionLabel(giver, workType));

            AddOpenWorkTabOption(pawn, workType, workLabel);

            // BWT: Check if specific work giver is disabled (not just the whole work type)
            var targetWorkType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
            if (targetWorkType != null)
            {
                int targetParentPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, targetWorkType);
                int baseWorkGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, targetParentPriority);
                int wgPriority = TimePriorityService.GetEffectiveWorkGiverPriority(
                    pawn,
                    targetWorkType,
                    workGiver,
                    baseWorkGiverPriority);
                
                // If this specific work giver is disabled in BWT, add "Go to Work Giver Sub-Menu" option
                if (wgPriority == WorkPrioritySystem.DisabledPriority)
                {
                    Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor.AdditionalOptions.Add(
                        new FloatMenuOption(
                            "BWTManageWorkGivers".Translate(
                                WorkTypeMenuLabel(targetWorkType),
                                WorkGiverDisplayNameService.HeaderLabel(workGiver)),
                            () =>
                            {
                                OpenWorkGiverManagement(pawn, targetWorkType, workGiver);
                            },
                            orderInPriority: -1));
                }
            }

            Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor.AdditionalOptions.Add(value);

            return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(text, AssignOnce, orderInPriority: -1),
                pawn,
                target);
        }

        private static FloatMenuOption BuildOnceOnlyWorkOption(
            FloatMenuOption value,
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            WorkGiver_Scanner scanner,
            LocalTargetInfo target,
            FloatMenuContext context,
            string disabledReason)
        {
            Job job = target.HasThing
                ? (scanner.HasJobOnThing(pawn, target.Thing, true) ? scanner.JobOnThing(pawn, target.Thing, true) : null)
                : (scanner.HasJobOnCell(pawn, target.Cell, true) ? scanner.JobOnCell(pawn, target.Cell, true) : null);

            AddOpenWorkTabOption(pawn, workType, WorkTypeMenuLabel(workType));
            AddOpenSubWorkOption(pawn, workType, workGiver);

            if (job == null)
            {
                return new FloatMenuOption(value.Label + ": " + disabledReason, null);
            }

            job.workGiverDef = scanner.def;
            Job localJob = job;
            WorkGiver_Scanner localScanner = scanner;
            WorkGiverDef localGiver = workGiver;
            return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(
                    "BWTNotAssignedDoOnce".Translate(WorkGiverActionLabel(localGiver, workType)),
                    () => TakePrioritizedJobOnce(pawn, localJob, localScanner, localGiver, context),
                    orderInPriority: -1),
                pawn,
                target);
        }

        private static bool TryGetBwtSubWorkDisabledReason(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int parentPriority,
            out string disabledReason)
        {
            disabledReason = null;
            if (pawn == null || workType == null || workGiver == null)
            {
                return false;
            }

            WorkTypeDef targetWorkType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
            if (targetWorkType != workType)
            {
                return false;
            }

            int baseWorkGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(
                pawn,
                workGiver,
                parentPriority);
            int effectivePriority = TimePriorityService.GetEffectiveWorkGiverPriority(
                pawn,
                workType,
                workGiver,
                baseWorkGiverPriority);
            if (effectivePriority > WorkPrioritySystem.DisabledPriority)
            {
                return false;
            }

            disabledReason = "Not assigned to " + WorkGiverDisplayNameService.HeaderLabel(workGiver);
            return true;
        }

        private static void TakePrioritizedJobOnce(
            Pawn pawn,
            Job job,
            WorkGiver_Scanner scanner,
            WorkGiverDef giver,
            FloatMenuContext context)
        {
            if (pawn?.jobs == null ||
                job == null ||
                scanner == null ||
                context == null ||
                !pawn.jobs.TryTakeOrderedJobPrioritizedWork(job, scanner, context.ClickedCell))
            {
                return;
            }

            if (giver?.forceMote != null)
            {
                MoteMaker.MakeStaticMote(context.ClickedCell, pawn.Map, giver.forceMote);
            }

            if (giver?.forceFleck != null)
            {
                FleckMaker.Static(context.ClickedCell, pawn.Map, giver.forceFleck);
            }
        }

        private static void AddOpenWorkTabOption(Pawn pawn, WorkTypeDef workType, string workLabel)
        {
            Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor.AdditionalOptions.Add(
                new FloatMenuOption(
                    "BWTNotAssignedAssignWork".Translate(workLabel),
                    () =>
                    {
                        HighlightState.SetWorktypeToHighlight(pawn, workType);
                        Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Work);
                    },
                    orderInPriority: -1));
        }

        private static void AddOpenSubWorkOption(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor.AdditionalOptions.Add(
                new FloatMenuOption(
                    "BWTManageWorkGivers".Translate(
                        WorkTypeMenuLabel(workType),
                        WorkGiverDisplayNameService.HeaderLabel(workGiver)),
                    () => OpenWorkGiverManagement(pawn, workType, workGiver),
                    orderInPriority: -1));
        }

        private static void AddOpenPriorityScheduleOption(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor.AdditionalOptions.Add(
                new FloatMenuOption(
                    "Open " + WorkTypeMenuLabel(workType) + " priority schedule",
                    () => TimePriorityScheduleEditor.OpenForFloatMenu(pawn, workType, workGiver),
                    orderInPriority: -1));
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
                TimePriorityScheduleEditor.CloseForWorkModeTransition();
                SubWorkDrilldownState.Enter(
                    targetWorkType,
                    baseHeaderDrawWidth: SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(null, -1f));
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Work);
                HighlightState.SetSubWorkGiverToHighlight(pawn, targetWorkType, workGiver);
                UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                    UI.WorkGrid.Contracts.WorkTabDirtyFlags.HeaderText |
                    UI.WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry |
                    UI.WorkGrid.Contracts.WorkTabDirtyFlags.RenderResources);
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                return;
            }

            var screenPos = new UnityEngine.Vector2(Verse.UI.screenWidth / 2f, Verse.UI.screenHeight / 2f);
            bool hasOverride = WorkGiverReassignmentManager.HasAnyPawnOverride(targetWorkType, pawn) ||
                               WorkGiverReassignmentManager.HasPawnOrdering(pawn, targetWorkType);
            Pawn windowPawn = hasOverride ? pawn : null;
            Find.WindowStack.Add(new Window_WorkGiverSubMenu(targetWorkType, screenPos, windowPawn));
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), nameof(FloatMenuOptionProvider_WorkGivers.GetWorkGiversOptionsFor))]
    public static class Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor
    {
        public static readonly List<FloatMenuOption> AdditionalOptions = new List<FloatMenuOption>();

        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> value, Pawn pawn, LocalTargetInfo target, FloatMenuContext context)
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                foreach (var option in value)
                {
                    yield return option;
                }

                AdditionalOptions.Clear();
                yield break;
            }

            DoOnceSupport.EnsureBwtOwnsUnassignedWorkMenu();

            foreach (var option in value)
            {
                yield return option;
            }

            foreach (var option in AdditionalOptions)
            {
                yield return option;
            }

            AdditionalOptions.Clear();
        }
    }

    [DefOf]
    public static class MainButtonDefOf
    {
        public static MainButtonDef Work;
    }
}
