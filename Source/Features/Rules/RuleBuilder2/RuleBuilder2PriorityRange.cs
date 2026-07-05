using Better_Work_Tab.Features.RaisedPriorityMaximum;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal static class RuleBuilder2PriorityRange
    {
        internal static int Max => WorkPrioritySystem.GetRequestableMaxPriority();

        internal static int Clamp(int priority)
        {
            return WorkPrioritySystem.ClampPriority(priority);
        }

        internal static int Step(int priority, int direction)
        {
            return WorkPrioritySystem.GetPriorityAfterBoundedStep(priority, direction);
        }

        internal static void NormalizeAction(RuleBuilder2Action action)
        {
            if (action == null)
            {
                return;
            }

            action.Priority = Clamp(action.Priority);
            action.ElsePriority = Clamp(action.ElsePriority);
            action.EnsureSchedule(action.Priority);
        }

        internal static void NormalizeCondition(RuleBuilder2Condition condition)
        {
            if (condition == null)
            {
                return;
            }

            if (IsPriorityCondition(condition.Kind))
            {
                condition.IntValue = Clamp(condition.IntValue);
            }
        }

        internal static bool IsPriorityCondition(RuleBuilder2ConditionKind kind)
        {
            return kind == RuleBuilder2ConditionKind.ExistingPriorityAtLeast ||
                   kind == RuleBuilder2ConditionKind.ExistingPriorityEquals;
        }
    }
}
