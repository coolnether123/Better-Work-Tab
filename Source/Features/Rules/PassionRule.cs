using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules
{
    /// <summary>
    /// This rule handles adjusting work priorities based on a pawn's passion for relevant skills.
    /// </summary>
    public class PassionRule : IAssignmentRule
    {
        public void Apply(Pawn pawn, System.Action<WorkTypeDef, int> setPrioritySafe, AutoAssignmentContext context)
        {
            if (!context.Settings.rule_PassionOverrideEnabled || pawn.skills == null) return;

            foreach (var wt in context.AllWorkTypes)
            {
                if (pawn.WorkTypeIsDisabled(wt) || !wt.relevantSkills.Any()) continue;

                Passion passion = wt.relevantSkills.Max(skill => pawn.skills.GetSkill(skill).passion);

                // All other skills should only be set if the pawn has a burning passion (Major) and gets set to 2
                if (passion == Passion.Major && context.Settings.passion_Major > 0)
                {
                    setPrioritySafe(wt, context.Settings.passion_Major);
                }
                else if (passion == Passion.Minor && context.Settings.passion_Minor > 0)
                {
                    setPrioritySafe(wt, context.Settings.passion_Minor);
                }
            }
        }
    }
}
