using Better_Work_Tab.Features;
using Better_Work_Tab.UI.RuleBuilder.State;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.Services
{
    /// <summary>
    /// Simulates a ruleset's effects on the current colony without permanently changing pawn data.
    /// Uses a snapshot → apply → capture → restore cycle so all validators see realistic pawn state.
    /// </summary>
    public class RulesetPreviewCalculator
    {
        public RulesetPreviewResult Calculate(WorkAssignmentRuleset ruleset)
        {
            if (ruleset == null) return RulesetPreviewResult.Empty;

            var map = Find.CurrentMap;
            if (map == null) return RulesetPreviewResult.Empty;

            var pawns = map.mapPawns.FreeColonists.ToList();
            if (pawns.Count == 0) return RulesetPreviewResult.Empty;

            var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .OrderByDescending(wt => wt.naturalPriority)
                .ToList();

            var before = TakeSnapshot(pawns, allWorkTypes);

            // Use a fixed seed so RandomIfMultiple rules produce a stable preview
            // rather than flickering on every recalculation.
            var savedRandomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(42);

            try
            {
                if (ruleset.ResetBeforeApplying)
                    ZeroPriorities(pawns, allWorkTypes);

                // Snapshot after any reset so HasChanged compares rule assignments
                // to the post-reset baseline, not to the pre-reset colony state.
                var baseline = TakeSnapshot(pawns, allWorkTypes);

                ruleset.ApplyToList(pawns);

                var after = TakeSnapshot(pawns, allWorkTypes);
                UnityEngine.Random.state = savedRandomState;
                RestoreSnapshot(pawns, before);

                return new RulesetPreviewResult(pawns, allWorkTypes, before, baseline, after);
            }
            catch
            {
                UnityEngine.Random.state = savedRandomState;
                RestoreSnapshot(pawns, before);
                return RulesetPreviewResult.Empty;
            }
        }

        private static Dictionary<Pawn, Dictionary<WorkTypeDef, int>> TakeSnapshot(
            List<Pawn> pawns,
            List<WorkTypeDef> workTypes)
        {
            var snap = new Dictionary<Pawn, Dictionary<WorkTypeDef, int>>(pawns.Count);
            foreach (var pawn in pawns)
            {
                var priorities = new Dictionary<WorkTypeDef, int>(workTypes.Count);
                foreach (var wt in workTypes)
                    priorities[wt] = pawn.workSettings?.GetPriority(wt) ?? 0;
                snap[pawn] = priorities;
            }
            return snap;
        }

        private static void RestoreSnapshot(
            List<Pawn> pawns,
            Dictionary<Pawn, Dictionary<WorkTypeDef, int>> snapshot)
        {
            foreach (var pawn in pawns)
            {
                if (!snapshot.TryGetValue(pawn, out var priorities)) continue;
                foreach (var kvp in priorities)
                    pawn.workSettings?.SetPriority(kvp.Key, kvp.Value);
            }
        }

        private static void ZeroPriorities(List<Pawn> pawns, List<WorkTypeDef> workTypes)
        {
            foreach (var pawn in pawns)
            {
                if (pawn.workSettings == null) continue;
                foreach (var wt in workTypes)
                    pawn.workSettings.SetPriority(wt, 0);
            }
        }
    }
}
