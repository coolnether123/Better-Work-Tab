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

        public Dictionary<TPawn, Dictionary<TWorkType, int>> Before { get; }
        public Dictionary<TPawn, Dictionary<TWorkType, int>> After { get; }
        public HashSet<TPawn> MatchedPawns { get; }

        public bool HasData => Pawns != null && Pawns.Count > 0;

        public PrioritySnapshot()
        {
            Pawns = new List<TPawn>();
            WorkTypes = new List<TWorkType>();
            Before = new Dictionary<TPawn, Dictionary<TWorkType, int>>();
            After = new Dictionary<TPawn, Dictionary<TWorkType, int>>();
            MatchedPawns = new HashSet<TPawn>();
        }

        public PrioritySnapshot(
            IList<TPawn> pawns,
            IList<TWorkType> workTypes,
            Dictionary<TPawn, Dictionary<TWorkType, int>> before,
            Dictionary<TPawn, Dictionary<TWorkType, int>> after)
        {
            Pawns = pawns;
            WorkTypes = workTypes;
            Before = before;
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

        public bool HasChanged(TPawn pawn, TWorkType workType)
        {
            return GetBeforePriority(pawn, workType) != GetAfterPriority(pawn, workType);
        }
    }
}
