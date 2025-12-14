using Better_Work_Tab.Features.Rules;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.State
{
    /// <summary>
    /// Encapsulates the result of evaluating a rule against a pawn for preview flows.
    /// </summary>
    public class RuleMatchResult
    {
        public Pawn Pawn { get; set; }
        public WorkAssignmentRule Rule { get; set; }
        public WorkTypeDef WorkType { get; set; }
        public bool IsMatch { get; set; }
        public int AssignedPriority { get; set; }
        public string MatchExplanation { get; set; }
        public Dictionary<string, (bool Passed, string Detail)> ConditionResults { get; set; }

        public RuleMatchResult()
        {
            ConditionResults = new Dictionary<string, (bool, string)>();
        }

        public static RuleMatchResult Success(
            Pawn pawn,
            WorkAssignmentRule rule,
            WorkTypeDef workType,
            int priority,
            string explanation)
        {
            return new RuleMatchResult
            {
                Pawn = pawn,
                Rule = rule,
                WorkType = workType,
                IsMatch = true,
                AssignedPriority = priority,
                MatchExplanation = explanation
            };
        }

        public static RuleMatchResult Failure(
            Pawn pawn,
            WorkAssignmentRule rule,
            WorkTypeDef workType,
            string reason)
        {
            return new RuleMatchResult
            {
                Pawn = pawn,
                Rule = rule,
                WorkType = workType,
                IsMatch = false,
                AssignedPriority = 0,
                MatchExplanation = reason
            };
        }
    }
}
