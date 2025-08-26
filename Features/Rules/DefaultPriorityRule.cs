using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules
{
    /// <summary>
    /// This rule handles setting a default priority for work types not covered by other specific rules.
    /// </summary>
    public class DefaultPriorityRule : IAssignmentRule
    {
        public void Apply(Pawn pawn, System.Action<WorkTypeDef, int> setPrioritySafe, AutoAssignmentContext context)
        {
            foreach (var wt in context.AllWorkTypes)
            {
                if (pawn.WorkTypeIsDisabled(wt)) continue;
                if (context.TouchedWorkTypes.Contains(wt)) continue;

                if (context.Settings.defaultStartingPriority >= 1 && context.Settings.defaultStartingPriority <= 4)
                {
                    pawn.workSettings.SetPriority(wt, context.Settings.defaultStartingPriority);
                }
            }
        }
    }
}
