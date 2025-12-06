using RimWorld;
using Better_Work_Tab.Features.Rules;
using Verse;

namespace Better_Work_Tab.Features.Rules.Validators
{
    /// <summary>
    /// Ensures the pawn can physically perform this worktype.
    /// Checks:
    /// - Worktype not disabled by age/injury
    /// - Violence capability (if required)
    /// - "Always active" restrictions
    /// </summary>
    public static class EligibilityValidator
    {
        public static bool Validate(
            Pawn pawn,
            WorkTypeDef wt,
            WorkAssignmentParameters p
        )
        {
            if (pawn == null) 
                return false;

            // Restrict to natural "always active" worktypes if requested
            if (p.IsNaturalAlwaysAssign && !wt.alwaysStartActive)
                return false;

            // Pawn must be able to perform the worktype
            if (pawn.WorkTypeIsDisabled(wt))
                return false;

            // If explicitly requiring violence capability, ensure they have it
            if (p.IsCapableOfViolence && pawn.WorkTagIsDisabled(WorkTags.Violent))
                return false;

            return true;
        }
    }
}