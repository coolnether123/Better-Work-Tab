using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.Features.Rules;

namespace Better_Work_Tab.Features
{
    /// <summary>
    /// This class handles the automatic work assignment process for pawns based on a set of defined rules.
    /// </summary>
    public class AutoWorkAssigner
    {
        private readonly BetterWorkTabSettings _settings;
        private readonly List<AssignWorkRule> _rules;

        public AutoWorkAssigner(BetterWorkTabSettings settings)
        {
            _settings = settings;
            var paramas = new AssignWorkParams();
            Log.Message("Skip if: "+paramas.SkipIfPriorityForThisWorktypeAreadyAssigned);
            _rules = new List<AssignWorkRule>
            {
                new AssignWorkRule(0, paramas),
                //new AssignWorkRule(2, new AssignWorkParams(skillLevelGreaterThan: 5), WorkTypeDefOf.Doctor),
                //new AssignWorkRule(4, new AssignWorkParams(skillLevelLessThan: 6), WorkTypeDefOf.Doctor),
            };

        }

        /// <summary>
        /// Applies all configured auto-assignment rules to the free colonists on the current map.
        /// </summary>
        public void ApplyAutoAssignments()
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            Find.PlaySettings.useWorkPriorities = true;

            var pawns = map.mapPawns.FreeColonists.ToList();
            if (pawns.Count == 0) return;

            var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            allWorkTypes.RemoveDuplicates();
            //int bestMed = pawns.Any(p => p.skills != null)
            //    ? pawns.Max(p => p.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0)
            //    : 0;
            //var bestDoctors = new HashSet<Pawn>(
            //    pawns.Where(p =>
            //        p.skills != null &&
            //        (p.skills.GetSkill(SkillDefOf.Medicine)?.Level ?? 0) == bestMed));



            foreach (var rule in _rules)
            {
                foreach (var worktype in allWorkTypes)
                {
                    //Log.Message($"Auto-assigning work type: {worktype.defName}");
                    foreach (var pawn in pawns)
                    {
                        if (pawn.workSettings == null) continue;

                        //pawn.workSettings.EnableAndInitialize();
                        // Apply all rules
                        // If the rule is specific to a work type and it doesn't match the current work type, skip it
                        if (rule.CachedWorktype != null && worktype != rule.CachedWorktype)
                        {
                            continue;
                        }
                        rule.Apply(pawn, pawns, worktype);
                    }
                }
            }
        }
    }
}