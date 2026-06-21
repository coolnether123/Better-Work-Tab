using RimWorld;
using Better_Work_Tab.Features.Rules;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.Rules.Validators
{
    /// <summary>
    /// Ensures work is assigned based on constraints relative to other pawns.
    /// Checks:
    /// - If a specific priority is already taken (SkipIfPriorityForThisWorktypeAreadyAssigned)
    /// - If any other pawn is already assigned (SkipIfAnotherPawnAssigned)
    /// - If this pawn has the fewest work priorities (AssignToPawnWithFewestWorkPriorities)
    /// </summary>
    public static class AssignmentConstraintsValidator
    {
        public static AssignmentValidationResult Validate(
            Pawn pawn,
            WorkTypeDef wt,
            List<Pawn> allPawns,
            WorkAssignmentParameters p
        )
        {
            if (Better_Work_Tab.PawnCompat.WorkSettings(pawn) == null)
                return new AssignmentValidationResult(false, false);

            bool isValid = true;
            bool shouldSkipRemainingPawns = false;

            // If "someone must have this priority" is set, reject if already taken
            if (p.SkipIfPriorityForThisWorktypeAreadyAssigned > -1)
            {
                foreach (var other in allPawns)
                {
                    if (other == pawn || Better_Work_Tab.PawnCompat.WorkSettings(other) == null)
                        continue;

                    if (Better_Work_Tab.PawnCompat.WorkSettings(other).GetPriority(wt) == p.SkipIfPriorityForThisWorktypeAreadyAssigned)
                        return new AssignmentValidationResult(false, false);
                }
            }

            // If any pawn already assigned this worktype, skip this one
            if (p.SkipIfAnotherPawnAssigned)
            {
                foreach (var other in allPawns)
                {
                    if (other == pawn || Better_Work_Tab.PawnCompat.WorkSettings(other) == null)
                        continue;

                    if (Better_Work_Tab.PawnCompat.WorkSettings(other).GetPriority(wt) > 0)
                        return new AssignmentValidationResult(false, false);
                }
            }

            // Assign only to the pawn with the fewest active worktypes
            if (p.AssignToPawnWithFewestWorkPriorities)
            {
                var allWorkTypes = WorkAssignmentRule.AllWorkTypes;

                Pawn chosen = null;
                int fewest = int.MaxValue;

                foreach (var pp in allPawns)
                {
                    if (Better_Work_Tab.PawnCompat.WorkSettings(pp) == null)
                        continue;

                    int count = allWorkTypes
                        .Count(w => Better_Work_Tab.PawnCompat.WorkSettings(pp).GetPriority(w) > 0);

                    if (count < fewest && !pp.WorkTypeIsDisabled(wt))
                    {
                        chosen = pp;
                        fewest = count;
                    }
                }

                if (chosen != pawn)
                    return new AssignmentValidationResult(false, false);

                // Signal to skip remaining pawns for this worktype
                shouldSkipRemainingPawns = true;
            }

            return new AssignmentValidationResult(isValid, shouldSkipRemainingPawns);
        }
    }
}
