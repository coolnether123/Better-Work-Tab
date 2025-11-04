using RimWorld;
using Better_Work_Tab.Features.Rules;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.Rules.Validators
{
    /// <summary>
    /// Checks assignment constraints relative to other pawns:
    /// - Someone must always be assigned (skip if taken)
    /// - Only assign if no one else is
    /// - Assign to pawn with fewest worktypes
    /// - RandomIfMultiple handling
    /// </summary>
    public static class AssignmentConstraintsValidator
    {
        public static bool Validate(
            Pawn pawn,
            WorkTypeDef wt,
            List<Pawn> allPawns,
            WorkAssignmentParameters p,
            ref bool skipRemaining
        )
        {
            // If "someone must have this priority" is set, reject if already taken
            if (p.SkipIfPriorityForThisWorktypeAreadyAssigned > -1)
            {
                foreach (var other in allPawns)
                {
                    if (other == pawn)
                        continue;

                    if (other.workSettings.GetPriority(wt) == p.SkipIfPriorityForThisWorktypeAreadyAssigned)
                        return false;
                }
            }

            // If any pawn already assigned this worktype, skip this one
            if (p.SkipIfAnotherPawnAssigned)
            {
                foreach (var other in allPawns)
                {
                    if (other == pawn)
                        continue;

                    if (other.workSettings.GetPriority(wt) > 0)
                        return false;
                }
            }

            // Assign only to the pawn with the fewest active worktypes
            if (p.AssignToPawnWithFewestWorkPriorities)
            {
                var allWorkTypes = DefDatabase<WorkTypeDef>
                    .AllDefsListForReading
                    .OrderBy(w => w.naturalPriority)  
                    .Reverse()
                    .ToList();
                allWorkTypes.RemoveDuplicates();

                Pawn chosen = null;
                int fewest = int.MaxValue;

                foreach (var pp in allPawns)
                {
                    int count = allWorkTypes
                        .Count(w => pp.workSettings.GetPriority(w) > 0);  

                    if (count < fewest && !pp.WorkTypeIsDisabled(wt))
                    {
                        chosen = pp;
                        fewest = count;
                    }
                }

                if (chosen != pawn)
                    return false;

                // Signal to skip remaining pawns for this worktype
                skipRemaining = true;
            }

            return true;
        }
    }
}