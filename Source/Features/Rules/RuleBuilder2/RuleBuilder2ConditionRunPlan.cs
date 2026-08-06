using System.Collections.Generic;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    /// <summary>
    /// Where a list of conditions breaks into runs, decided from nothing but the
    /// enabled and "or" flags.
    ///
    /// Split out from the conditions themselves so it can be tested. This is the
    /// rule that decides which colonists a ruleset touches: read one row into
    /// the wrong run and a rule quietly starts matching people it should not,
    /// which is not a thing to discover in somebody's colony. Everything here is
    /// plain data, so the test project can link the file directly rather than
    /// standing up a game.
    /// </summary>
    public static class RuleBuilder2ConditionRunPlan
    {
        /// <summary>
        /// Returns, for each input position, the index of the run it belongs to,
        /// or -1 when the condition is disabled and takes no part.
        ///
        /// A run opens at the first enabled condition and at every enabled
        /// condition not marked as an alternative. Marking the first one as an
        /// alternative is meaningless -- there is nothing above it -- so it
        /// opens a run regardless, and a rule can never collapse into a single
        /// "or" chain that matches almost everybody.
        ///
        /// Disabled rows are skipped rather than ending a run, so switching one
        /// off cannot promote the row beneath it out of its alternative and into
        /// a requirement of its own.
        /// </summary>
        public static List<int> AssignRunIndexes(IList<bool> enabled, IList<bool> orWithPrevious)
        {
            var result = new List<int>();
            if (enabled == null)
            {
                return result;
            }

            int runIndex = -1;
            for (int i = 0; i < enabled.Count; i++)
            {
                if (!enabled[i])
                {
                    result.Add(-1);
                    continue;
                }

                bool isAlternative = orWithPrevious != null &&
                                     i < orWithPrevious.Count &&
                                     orWithPrevious[i];
                if (!isAlternative || runIndex < 0)
                {
                    runIndex++;
                }

                result.Add(runIndex);
            }

            return result;
        }

        /// <summary>The number of runs a plan produced.</summary>
        public static int CountRuns(IList<int> runIndexes)
        {
            int highest = -1;
            for (int i = 0; i < (runIndexes?.Count ?? 0); i++)
            {
                if (runIndexes[i] > highest)
                {
                    highest = runIndexes[i];
                }
            }

            return highest + 1;
        }
    }
}
