using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal static class RuleBuilder2PriorityRange
    {
        internal const int SleekMaxPriority = 5;

        /// <summary>
        /// The editor's target range follows the active Work-tab owner. This is
        /// deliberately separate from the stored BWT range: changing owners
        /// must not rewrite values already saved in a ruleset.
        /// </summary>
        internal static int Max => SleekWorkTabGateway.SleekCodeRuns
            ? SleekMaxPriority
            : WorkPrioritySystem.GetRequestableMaxPriority();

        internal static int Clamp(int priority)
        {
            // Storage/model normalization always uses BWT's native range. The
            // Sleek adapter translates only at the UI/evaluation/apply edges.
            return WorkPrioritySystem.ClampPriority(priority);
        }

        internal static int ClampForActiveWorkTab(int priority)
        {
            if (!SleekWorkTabGateway.SleekCodeRuns)
            {
                return Clamp(priority);
            }

            return priority <= 0
                ? WorkPrioritySystem.DisabledPriority
                : priority > SleekMaxPriority
                    ? SleekMaxPriority
                    : priority;
        }

        internal static int Step(int priority, int direction)
        {
            if (!SleekWorkTabGateway.SleekCodeRuns)
            {
                return WorkPrioritySystem.GetPriorityAfterBoundedStep(priority, direction);
            }

            int normalized = ClampForActiveWorkTab(priority);
            if (direction == 0)
            {
                return normalized;
            }

            if (normalized <= WorkPrioritySystem.DisabledPriority)
            {
                return direction > 0 ? 1 : WorkPrioritySystem.DisabledPriority;
            }

            int stepped = normalized + (direction > 0 ? 1 : -1);
            return stepped <= WorkPrioritySystem.DisabledPriority
                ? WorkPrioritySystem.DisabledPriority
                : stepped > SleekMaxPriority
                    ? WorkPrioritySystem.DisabledPriority
                    : stepped;
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
