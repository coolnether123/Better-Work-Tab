using RimWorld;
using Better_Work_Tab.Features.Rules;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.Rules.Validators
{
    /// <summary>
    /// Checks priority constraints:
    /// - Don't overwrite higher priorities (if configured)
    /// - Respect worktype limits per pawn
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
                int current = pawn.workSettings.GetPriority(wt);
                if (current < p.Priority && current != 0 && p.Priority != 0)
                    return false;
            }

            // Limit the number of active worktypes for this pawn (if configured)
            if (p.LimitNumberOfWorktypes > 0)
            {
                var allWorkTypes = DefDatabase<WorkTypeDef>
                    .AllDefsListForReading
                    .OrderBy(w => w.naturalPriority)  
                    .Reverse()
                    .ToList();
                allWorkTypes.RemoveDuplicates();

                int activeCount = 0;
                foreach (var w in allWorkTypes)
                {
                    if (pawn.workSettings.GetPriority(w) > 0)
                        activeCount++;
                }

                if (activeCount >= p.LimitNumberOfWorktypes)
                    return false;
            }

            return true;
        }
    }
}