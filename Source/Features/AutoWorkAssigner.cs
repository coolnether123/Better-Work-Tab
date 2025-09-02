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
            var parameterses = new List<AssignWorkParams>
            {
                new AssignWorkParams(1, worktype: WorkTypeDefOf.Firefighter),
                new AssignWorkParams(1, worktype: DefDatabase<WorkTypeDef>.GetNamed("Patient")),
                new AssignWorkParams(1, worktype: DefDatabase<WorkTypeDef>.GetNamed("PatientBedRest")),
                new AssignWorkParams(1, worktype: DefDatabase<WorkTypeDef>.GetNamed("BasicWorker")),
                new AssignWorkParams(1, worktype: WorkTypeDefOf.Doctor, hasHighestSkill: true),
                new AssignWorkParams(2, worktype: WorkTypeDefOf.Childcare, hasChildOnMap: true),
                new AssignWorkParams(2, passionLevel: 2),
                new AssignWorkParams(3, passionLevel: 1),
            };


            _rules = new List<AssignWorkRule>();
            foreach (var p in parameterses)
            {
                _rules.Add(new AssignWorkRule(p));
            }

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

            var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading.OrderBy(wt => wt.naturalPriority).Reverse().ToList();
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

                        ////pawn.workSettings.EnableAndInitialize();
                        //// Apply all rules
                        //// If the rule is specific to a work type and it doesn't match the current work type, skip it
                        //if (rule.CachedWorktype != null && worktype != rule.CachedWorktype)
                        //{
                        //    continue;
                        //}
                        if (rule.Apply(pawn, pawns, worktype))
                        {
                            //Log.Message("assigned " + worktype.defName +" to " + pawn.NameShortColored +". Skipping remaining pawns.");
                            //Apply returns true if the rest of the pawns should be skipped for this worktype
                            break;
                        }

                    }

                    if (rule.Parameters.FailedToApplyFallback != null && !pawns.Where(p => { return p.workSettings.GetPriority(worktype) > 0; }).Any())
                    {
                        Log.Message($"No pawn could be assigned to work type: {worktype.defName}. Applying fallback.");
                        foreach (var pawn in pawns)
                            new AssignWorkRule(rule.Parameters.FailedToApplyFallback).Apply(pawn, pawns, worktype);
                    }
                }
            }
        }


    }
}