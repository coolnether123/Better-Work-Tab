using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace Better_Work_Tab.Patches
{

    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), nameof(FloatMenuOptionProvider_WorkGivers.GetWorkGiverOption))]
    public static class Patch_FloatMenuOptionProvider_WorkGivers_GetWorkGiverOption
    {
        public static FloatMenuOption Postfix(FloatMenuOption value, Pawn pawn, WorkGiverDef workGiver, LocalTargetInfo target, FloatMenuContext context)
        {
            if(value == null)
            {
                return value;
            }

            WorkGiver_Scanner workGiver_Scanner = workGiver.Worker as WorkGiver_Scanner;

            WorkTypeDef workType = workGiver_Scanner.def.workType;

            Log.Message("worktype: " + workType.defName + "| Disabled: " + (pawn.workSettings.GetPriority(workType) == 0));

            if(workType == null || pawn == null || context == null || context == null)
            {
                return value;
            }

            if (pawn.workSettings.GetPriority(workType) == 0 && !pawn.WorkTypeIsDisabled(workType))
            {

                //return new FloatMenuOption("TESTING VALUE", () => { Log.Message("Works!"); });
                Log.Message("Gets here 1");
                Action action = null;
                Job job = (target.HasThing ? (workGiver_Scanner.HasJobOnThing(pawn, target.Thing, true) ? workGiver_Scanner.JobOnThing(pawn, target.Thing, true) : null) : (workGiver_Scanner.HasJobOnCell(pawn, target.Cell, true) ? workGiver_Scanner.JobOnCell(pawn, target.Cell, true) : null));
                Log.Message("Gets here 2");

                Job localJob = job;
                WorkGiver_Scanner localScanner = workGiver_Scanner;
                job.workGiverDef = workGiver_Scanner.def;
                WorkGiverDef giver = workGiver;
                Log.Message("Gets here 3");

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
                Log.Message("Gets here 4");

                var text = value.Label + " (DO ANYWAY)";

                return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(text, action, orderInPriority: -1), pawn, target);

            }
            else
            {
                return value;
            }
        }
    }
}
