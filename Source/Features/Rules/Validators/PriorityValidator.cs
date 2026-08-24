using RimWorld;
using Better_Work_Tab.Features.Rules;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.Rules.Validators
{
    /// <summary>
    /// Ensures work is assigned based on priority and worktype limits.
    /// Checks:
    /// - If overwriting a higher priority is allowed.
    /// - If the pawn has reached their worktype limit.
    /// </summary>
    public static class PriorityValidator
    {
        public static bool Validate(
            Pawn pawn,
            WorkTypeDef wt,
            WorkAssignmentParameters p
        )
        {
            // Overwrite protection: don't replace a higher (numerically lower) priority
            if (!p.AllowOverwritingHigherPriority)
            {
                int current = RuleApplicationPlanningScope.GetPriority(pawn, wt);
                if (current < p.Priority && current != 0 && p.Priority != 0)
                    return false;
            }

            // Limit the number of active worktypes for this pawn (if configured)
            if (p.LimitNumberOfWorktypes > 0)
            {
                var allWorkTypes = WorkAssignmentRule.AllWorkTypes;

                int activeCount = 0;
                foreach (var w in allWorkTypes)
                {
                    if (RuleApplicationPlanningScope.GetPriority(pawn, w) > 0)
                        activeCount++;
                }

                if (activeCount >= p.LimitNumberOfWorktypes)
                    return false;
            }

            return true;
        }
    }
}
