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
            // If the original method returned no option, we do nothing.
            if (value == null)
            {
                return value;
            }

            // This patch only applies to work givers that are scanners.
            if (!(workGiver.Worker is WorkGiver_Scanner workGiver_Scanner))
            {
                return value;
            }

            WorkTypeDef workType = workGiver_Scanner.def.workType;

            // Corrected null checks. It's crucial to ensure all objects are valid before proceeding.
            if (workType == null || pawn == null || context == null)
            {
                return value;
            }

            // This logic only applies if the pawn is capable of the work type but currently has its priority set to 0.
            if (pawn.workSettings.GetPriority(workType) == 0 && !pawn.WorkTypeIsDisabled(workType))
            {
                Action action = null;
                Job job = target.HasThing
                    ? (workGiver_Scanner.HasJobOnThing(pawn, target.Thing, true) ? workGiver_Scanner.JobOnThing(pawn, target.Thing, true) : null)
                    : (workGiver_Scanner.HasJobOnCell(pawn, target.Cell, true) ? workGiver_Scanner.JobOnCell(pawn, target.Cell, true) : null);

                // If no valid job can be created, do nothing.
                if (job == null) return value;

                Job localJob = job;
                WorkGiver_Scanner localScanner = workGiver_Scanner;
                job.workGiverDef = workGiver_Scanner.def;
                WorkGiverDef giver = workGiver;

                action = delegate
                {
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
                };

                var text = "BWTNotAssignedDoOnce".Translate(workType.gerundLabel);

                // Add a new option to quickly navigate to the work tab to assign the work type.
                Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor.AdditionalOptions.Add(new FloatMenuOption("BWTNotAssignedAssignWork".Translate(workType.gerundLabel), () =>
                {
                    // Highlight the pawn and work type when opening the work tab
                    PawnTable_HighlightRowAndColumn.SetWorktypeToHighlight(pawn, workType);
                    Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Work);
                }, orderInPriority: (int)MenuOptionPriority.VeryLow));

                // Add the original option to the list to be re-added later.
                Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor.AdditionalOptions.Add(value);

                // Return a new "Do once" option.
                return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(text, action, orderInPriority: -1), pawn, target);
            }
            else
            {
                // If the work is already prioritized, return the original option.
                return value;
            }
        }
    }
    }
    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), nameof(FloatMenuOptionProvider_WorkGivers.GetWorkGiversOptionsFor))]
    public static class Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOptionFor
    {
        public static List<FloatMenuOption> AdditionalOptions = new List<FloatMenuOption>();



        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> value, Pawn pawn, LocalTargetInfo target, FloatMenuContext context)
        {

            //Log.openOnMessage = true;
            //Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            foreach (var option in value)
            {
                yield return option;
            }
            foreach (var option in AdditionalOptions)
            {
                yield return option;
            }
            //yield return ;
            AdditionalOptions.Clear();
            //yield return new FloatMenuOption("-----", null);

        }
    }
    
    [DefOf]
    public static class MainButtonDefOf
    {
        public static MainButtonDef Work;

    }

