using System.Collections.Generic;
using System.Linq;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    /// <summary>
    /// One run of conditions joined by "or". A run is satisfied when any member
    /// is; a rule is satisfied when every run is.
    /// </summary>
    public sealed class RuleBuilder2ConditionRun
    {
        public readonly List<RuleBuilder2Condition> Members = new List<RuleBuilder2Condition>();

        public bool IsAlternative => Members.Count > 1;
    }

    /// <summary>
    /// Turns the flat condition list into the runs the rule actually means.
    ///
    /// Evaluation, the rule summary, the Map check panel and the editor all have
    /// to agree about where one alternative ends and the next requirement
    /// begins. They agree by asking here rather than each re-deriving it from
    /// the flags, because four readings of the same list is three too many.
    /// </summary>
    public static class RuleBuilder2ConditionGrouping
    {
        /// <summary>
        /// Groups the enabled conditions. Disabled rows are skipped entirely, so
        /// switching one off cannot silently promote the row beneath it into a
        /// requirement of its own.
        /// </summary>
        public static List<RuleBuilder2ConditionRun> BuildRuns(IEnumerable<RuleBuilder2Condition> conditions)
        {
            var ordered = (conditions ?? Enumerable.Empty<RuleBuilder2Condition>())
                .Where(condition => condition != null)
                .ToList();

            // The decision itself lives in RuleBuilder2ConditionRunPlan, which
            // knows only about flags and is covered by tests. This method only
            // hangs the conditions off the plan it returns.
            List<int> plan = RuleBuilder2ConditionRunPlan.AssignRunIndexes(
                ordered.Select(condition => condition.Enabled).ToList(),
                ordered.Select(condition => condition.OrWithPrevious).ToList());

            var runs = new List<RuleBuilder2ConditionRun>();
            for (int i = 0; i < ordered.Count; i++)
            {
                int runIndex = plan[i];
                if (runIndex < 0)
                {
                    continue;
                }

                while (runs.Count <= runIndex)
                {
                    runs.Add(new RuleBuilder2ConditionRun());
                }

                runs[runIndex].Members.Add(ordered[i]);
            }

            return runs;
        }

        /// <summary>
        /// Clears the flag on the first row so the stored list matches how it is
        /// read. Call after anything that can move rows around.
        /// </summary>
        public static void NormalizeFirstCondition(List<RuleBuilder2Condition> conditions)
        {
            if (conditions == null || conditions.Count == 0)
            {
                return;
            }

            RuleBuilder2Condition first = conditions.FirstOrDefault(condition => condition != null);
            if (first != null)
            {
                first.OrWithPrevious = false;
            }
        }

        /// <summary>True when any enabled condition is an alternative to another.</summary>
        public static bool HasAlternatives(IEnumerable<RuleBuilder2Condition> conditions)
        {
            return BuildRuns(conditions).Any(run => run.IsAlternative);
        }
    }
}
