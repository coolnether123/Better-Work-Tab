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
        private readonly List<IAssignmentRule> _rules;

        public AutoWorkAssigner(BetterWorkTabSettings settings)
        {
            _settings = settings;

            _rules = new List<IAssignmentRule>
            {
                new CoreWorkTypeRule(),
                new DoctorRule(),
                new PassionRule(),
                new ChildcareRule(),
                // TODO: Implement UI for rule_AlwaysHaveOneByWorkType and rule_AlwaysAssignAllByWorkType
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

            var pawns = map.mapPawns.FreeColonistsSpawned.ToList();
            if (pawns.Count == 0) return;

            var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

            int bestMed = pawns.Any(p => p.skills != null)
                ? pawns.Max(p => p.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0)
                : 0;
            var bestDoctors = new HashSet<Pawn>(
                pawns.Where(p =>
                    p.skills != null &&
                    (p.skills.GetSkill(SkillDefOf.Medicine)?.Level ?? 0) == bestMed));

            foreach (var pawn in pawns)
            {
                if (pawn.workSettings == null) continue;

                pawn.workSettings.EnableAndInitialize();

                var touched = new HashSet<WorkTypeDef>();

                System.Action<WorkTypeDef, int> setPrioritySafe = (wt, pri) =>
                {
                    if (pawn.WorkTypeIsDisabled(wt)) return;
                    pawn.workSettings.SetPriority(wt, Mathf.Clamp(pri, 1, 4));
                    touched.Add(wt);
                };

                var context = new AutoAssignmentContext(_settings, allWorkTypes, bestDoctors, touched);

                // Apply all rules
                foreach (var rule in _rules)
                {
                    rule.Apply(pawn, setPrioritySafe, context);
                }

                // Apply DefaultPriorityRule last
                new DefaultPriorityRule().Apply(pawn, setPrioritySafe, context);
            }
        }
    }
}