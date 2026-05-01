using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.State
{
    /// <summary>
    /// Concrete specialization of PrioritySnapshot for Pawn/WorkTypeDef.
    /// Produced by RulesetPreviewCalculator; never mutates pawn data.
    /// </summary>
    public class RulesetPreviewResult : PrioritySnapshot<Pawn, WorkTypeDef>
    {
        public static readonly RulesetPreviewResult Empty = new RulesetPreviewResult();

        public RulesetPreviewResult() : base() { }

        public RulesetPreviewResult(
            List<Pawn> pawns,
            List<WorkTypeDef> workTypes,
            Dictionary<Pawn, Dictionary<WorkTypeDef, int>> before,
            Dictionary<Pawn, Dictionary<WorkTypeDef, int>> after)
            : base(pawns, workTypes, before, after)
        { }
    }
}
