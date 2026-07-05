using Better_Work_Tab;
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
                target = WorkTypeCompat.WorkGiverLabelCap(card.Target?.ResolveWorkGiver())
                    ?? string.Empty;
                if (string.IsNullOrEmpty(target))
                {
                    target = WorkTypeCompat.LabelShort(card.Target?.ResolveWorkType());
                }
                if (string.IsNullOrEmpty(target))
                {
                    target = "No target";
                }
            }

            int conditionCount = card.Conditions?.Conditions?.Count(c => c != null && c.Enabled) ?? 0;
            string action = card.Action?.Kind == RuleBuilder2ActionKind.Disable
                ? "disable"
                : "priority " + (card.Action?.Priority ?? 0);
            return target + " -> " + action + " when " + conditionCount + " condition(s) match";
        }
    }
}
