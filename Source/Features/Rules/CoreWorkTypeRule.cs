using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules
{
    /// <summary>
    /// This rule handles setting high priority for core work types.
    /// </summary>
    public class CoreWorkTypeRule : IAssignmentRule
    {
        public void Apply(Pawn pawn, System.Action<WorkTypeDef, int> setPrioritySafe, AutoAssignmentContext context)
        {
            if (!context.Settings.rule_CoreAlwaysPriorityEnabled) return;

            // Core work types (Firefighter, Patient, BedRest, Basic) should always be priority 1
            if (context.Settings.core_Firefighter && WorkTypeDefOf.Firefighter != null)
            {
                setPrioritySafe(WorkTypeDefOf.Firefighter, context.Settings.rule_CoreAlwaysPriorityValue);
            }

            if (context.Settings.core_Patient && TryGetWorkTypeDef("Patient", out WorkTypeDef patientDef))
            {
                setPrioritySafe(patientDef, context.Settings.rule_CoreAlwaysPriorityValue);
            }

            if (context.Settings.core_BedRest && TryGetWorkTypeDef("PatientBedRest", out WorkTypeDef bedRestDef))
            {
                setPrioritySafe(bedRestDef, context.Settings.rule_CoreAlwaysPriorityValue);
            }

            if (context.Settings.core_Basic && TryGetWorkTypeDef("BasicWorker", out WorkTypeDef basicDef))
            {
                setPrioritySafe(basicDef, context.Settings.rule_CoreAlwaysPriorityValue);
            }
        }

        private bool TryGetWorkTypeDef(string defName, out WorkTypeDef workTypeDef)
        {
            workTypeDef = null;
            try
            {
                workTypeDef = DefDatabase<WorkTypeDef>.GetNamedSilentFail(defName);
                return workTypeDef != null;
            }
            catch
            {
                return false;
            }
        }
    }
}