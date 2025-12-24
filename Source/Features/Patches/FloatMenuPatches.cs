using Better_Work_Tab.Features;
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
            if (value == null)
            {
                return value;
            }

            if (workGiver.Worker is not WorkGiver_Scanner workGiverScanner)
            {
                return value;
            }

            WorkTypeDef workType = workGiverScanner.def.workType;
            if (workType == null || pawn == null || context == null)
            {
                return value;
            }

            // Check if work TYPE is disabled (vanilla)
            if (pawn.workSettings.GetPriority(workType) != 0 || pawn.WorkTypeIsDisabled(workType))
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
                // If it was disabled (0), enable it (1) so it can be done
                int wgPriority = Features.WorkGiverReassignments.WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, giver, 3);
                if (wgPriority == 0)
                {
                    Features.WorkGiverReassignments.WorkGiverReassignmentManager.SyncSetPawnOverride(pawn.thingIDNumber, giver.defName, 1);
                }

                if (pawn.jobs.TryTakeOrderedJobPrioritizedWork(localJob, localScanner, context.ClickedCell))
                {
                    if (giver.forceMote != null)
                    {
                        MoteMaker.MakeStaticMote(context.ClickedCell, pawn.Map, giver.forceMote);
                    }

                    if (giver.forceFleck != null)
                    {
                        FleckMaker.Static(context.ClickedCell, pawn.Map, giver.forceFleck);
                    }
                }
            }

            var text = "BWTNotAssignedDoOnce".Translate(workType.gerundLabel);

            // BWT: Check if specific work giver is disabled (not just the whole work type)
            var targetWorkType = Features.WorkGiverReassignments.WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
            if (targetWorkType != null)
            {
                int wgPriority = Features.WorkGiverReassignments.WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, 3);
                
                // If this specific work giver is disabled in BWT, add "Go to Work Giver Sub-Menu" option
                if (wgPriority == 0)
                {
                    Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor.AdditionalOptions.Add(
                        new FloatMenuOption(
                            "BWTManageWorkGivers".Translate(targetWorkType.labelShort),
                            () =>
                            {
                                var screenPos = new UnityEngine.Vector2(Verse.UI.screenWidth / 2f, Verse.UI.screenHeight / 2f);
                                bool hasOverride = Features.WorkGiverReassignments.WorkGiverReassignmentManager.HasAnyPawnOverride(targetWorkType, pawn) ||
                                                   Features.WorkGiverReassignments.WorkGiverReassignmentManager.HasPawnOrdering(pawn, targetWorkType);
                                Pawn windowPawn = hasOverride ? pawn : null;
                                Find.WindowStack.Add(new UI.WorkGiverReassignments.Window_WorkGiverSubMenu(targetWorkType, screenPos, windowPawn));
                            },
                            orderInPriority: (int)MenuOptionPriority.VeryLow));
                }
            }

            Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor.AdditionalOptions.Add(
                new FloatMenuOption(
                    "BWTNotAssignedAssignWork".Translate(workType.gerundLabel),
                    () =>
                    {
                        HighlightState.SetWorktypeToHighlight(pawn, workType);
                        Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Work);
                    },
                    orderInPriority: (int)MenuOptionPriority.VeryLow));

            Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor.AdditionalOptions.Add(value);

            return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(text, AssignOnce, orderInPriority: -1),
                pawn,
                target);
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), nameof(FloatMenuOptionProvider_WorkGivers.GetWorkGiversOptionsFor))]
    public static class Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor
    {
        public static readonly List<FloatMenuOption> AdditionalOptions = new List<FloatMenuOption>();

        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> value, Pawn pawn, LocalTargetInfo target, FloatMenuContext context)
        {
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
