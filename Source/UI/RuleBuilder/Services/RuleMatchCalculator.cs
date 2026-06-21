using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.Rules.Validators;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.Services
{
    /// <summary>
    /// Lightweight matcher used by the rule builder to preview which pawns
    /// satisfy a rule without mutating their priorities.
    /// </summary>
    public class RuleMatchCalculator
    {
        public int CountMatches(WorkAssignmentRule rule, WorkTypeDef workType, List<Pawn> pawns)
        {
            if (rule == null || workType == null || pawns == null)
                return 0;

            int count = 0;
            foreach (var pawn in pawns)
            {
                if (Matches(rule, workType, pawn, pawns))
                {
                    count++;
                }
            }

            return count;
        }

        private bool Matches(WorkAssignmentRule rule, WorkTypeDef workType, Pawn pawn, List<Pawn> pawns)
        {
            var p = rule.Parameters;

            if (p == null || Better_Work_Tab.PawnCompat.WorkSettings(pawn) == null)
                return false;

            if (!EligibilityValidator.Validate(pawn, workType, p))
                return false;

            if (!PriorityValidator.Validate(pawn, workType, p))
                return false;

            if (!SocialValidator.Validate(pawn, p))
                return false;

            if (!BiologicalValidator.Validate(pawn, p))
                return false;

            if (!SkillValidator.Validate(pawn, workType, pawns, p))
                return false;

            var result = AssignmentConstraintsValidator.Validate(pawn, workType, pawns, p);
            return result.IsValid;
        }
    }
}
