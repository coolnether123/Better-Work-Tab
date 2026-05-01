using System.Collections.Generic;

namespace Better_Work_Tab.UI.RuleBuilder.State
{
    /// <summary>
    /// Generic before/after priority map that can be tested without RimWorld types.
    /// Concrete usage: PrioritySnapshot&lt;Pawn, WorkTypeDef&gt; via RulesetPreviewResult.
    /// Tests use PrioritySnapshot&lt;string, string&gt;.
    /// </summary>
    public class PrioritySnapshot<TPawn, TWorkType>
    {
        public IList<TPawn> Pawns { get; }
        public IList<TWorkType> WorkTypes { get; }

        /// <summary>Actual colony state before anything is touched.</summary>
        public Dictionary<TPawn, Dictionary<TWorkType, int>> Before { get; }

        /// <summary>
        /// State after any reset but before rules are applied.
        /// Used as the baseline for HasChanged so that reset-before-apply rulesets
        /// only highlight cells the rules actively assign, not what the reset wiped.
        /// </summary>
        public Dictionary<TPawn, Dictionary<TWorkType, int>> Baseline { get; }

        /// <summary>State after the full ruleset application.</summary>
        public Dictionary<TPawn, Dictionary<TWorkType, int>> After { get; }

        public HashSet<TPawn> MatchedPawns { get; }

        public bool HasData => Pawns != null && Pawns.Count > 0;

        public PrioritySnapshot()
        {
            Pawns = new List<TPawn>();
            WorkTypes = new List<TWorkType>();
            Before = new Dictionary<TPawn, Dictionary<TWorkType, int>>();
            Baseline = new Dictionary<TPawn, Dictionary<TWorkType, int>>();
            After = new Dictionary<TPawn, Dictionary<TWorkType, int>>();
            MatchedPawns = new HashSet<TPawn>();
        }

        public PrioritySnapshot(
            IList<TPawn> pawns,
            IList<TWorkType> workTypes,
            Dictionary<TPawn, Dictionary<TWorkType, int>> before,
            Dictionary<TPawn, Dictionary<TWorkType, int>> baseline,
            Dictionary<TPawn, Dictionary<TWorkType, int>> after)
        {
            Pawns = pawns;
            WorkTypes = workTypes;
            Before = before;
            Baseline = baseline;
            After = after;
            MatchedPawns = new HashSet<TPawn>();

            foreach (var pawn in pawns)
            {
                if (!after.TryGetValue(pawn, out var afterPriorities)) continue;
                foreach (var wt in workTypes)
                {
                    if (afterPriorities.TryGetValue(wt, out int p) && p > 0)
                    {
                        MatchedPawns.Add(pawn);
                        break;
                    }
                }
            }
        }

        public int GetAfterPriority(TPawn pawn, TWorkType workType)
        {
            if (After.TryGetValue(pawn, out var wts) && wts.TryGetValue(workType, out int p))
                return p;
            return 0;
        }

        public int GetBeforePriority(TPawn pawn, TWorkType workType)
        {
            if (Before.TryGetValue(pawn, out var wts) && wts.TryGetValue(workType, out int p))
                return p;
            return 0;
        }

        /// <summary>
        /// True when the ruleset's rules actively change this cell from the post-reset baseline.
        /// For reset-before-apply rulesets this means "the rule assigned something here";
        /// for additive rulesets it means "the rule changed the current priority".
        /// </summary>
        public bool HasChanged(TPawn pawn, TWorkType workType)
        {
            int baselinePriority = Baseline.TryGetValue(pawn, out var bwts) && bwts.TryGetValue(workType, out int b) ? b : 0;
            return baselinePriority != GetAfterPriority(pawn, workType);
        }
    }
}
