using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules
{
    /// <summary>
    /// This rule handles assigning high priority to the best doctors in the colony.
    /// </summary>
    public class BestPawnRule : IAssignmentRule
    {
        WorkTypeDef Worktype;
        int Priority = 1;

        public BestPawnRule(WorkTypeDef worktype, int priority)
        {
            Worktype = worktype;
            Priority = priority;
        }

        public void Apply(Pawn pawn, System.Action<WorkTypeDef, int> setPrioritySafe, AutoAssignmentContext context)
        {
            if (!context.Settings.rule_BestDoctorsEnabled) return;

            // Doctor should only be 1 for the best doctor(s)
            if (context.BestDoctors.Contains(pawn) && Worktype != null)
            {
                if (!pawn.WorkTypeIsDisabled(Worktype))
                    setPrioritySafe(Worktype, Priority);
            }



            //assign best pawn if all are tied choose the one with the fewest assigned works.
        }
    }
}