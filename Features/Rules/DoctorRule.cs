using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules
{
    /// <summary>
    /// This rule handles assigning high priority to the best doctors in the colony.
    /// </summary>
    public class DoctorRule : IAssignmentRule
    {
        public void Apply(Pawn pawn, System.Action<WorkTypeDef, int> setPrioritySafe, AutoAssignmentContext context)
        {
            if (!context.Settings.rule_BestDoctorsEnabled) return;

            // Doctor should only be 1 for the best doctor(s)
            if (context.BestDoctors.Contains(pawn) && WorkTypeDefOf.Doctor != null)
            {
                if (!pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
                    setPrioritySafe(WorkTypeDefOf.Doctor, context.Settings.rule_BestDoctorsPriority);
            }
        }
    }
}