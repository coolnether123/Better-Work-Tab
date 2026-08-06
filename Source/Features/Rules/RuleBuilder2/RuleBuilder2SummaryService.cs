using System.Linq;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal static class RuleBuilder2SummaryService
    {
        internal static string BuildSummary(RuleBuilder2Card card)
        {
            if (card == null)
            {
                return "Empty rule";
            }

            string target = card.Target?.DisplayLabel;
            if (string.IsNullOrEmpty(target))
            {
                target = card.Target?.ResolveWorkGiver()?.LabelCap.ToString()
                    ?? card.Target?.ResolveWorkType()?.LabelCap.ToString()
                    ?? "No target";
            }

            // Counted in runs, because that is the number that has to hold. A
            // rule whose only two conditions are alternatives to each other is
            // one requirement, and calling it two would overstate it.
            int conditionCount = RuleBuilder2ConditionGrouping.BuildRuns(card.Conditions?.Conditions).Count;
            string action = card.Action?.Kind == RuleBuilder2ActionKind.Disable
                ? "disable"
                : "priority " + (card.Action?.Priority ?? 0);
            return target + " -> " + action + " when " + conditionCount + " condition(s) match";
        }
    }
}
