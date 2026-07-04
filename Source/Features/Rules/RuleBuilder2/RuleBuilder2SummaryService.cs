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

            int conditionCount = card.Conditions?.Conditions?.Count(c => c != null && c.Enabled) ?? 0;
            string action = card.Action?.Kind == RuleBuilder2ActionKind.Disable
                ? "disable"
                : "priority " + (card.Action?.Priority ?? 0);
            return target + " -> " + action + " when " + conditionCount + " condition(s) match";
        }
    }
}
