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
    public class WorkAssignmentRuleset
    {
        //private readonly BetterWorkTabSettings _settings;

        public string Name { get; private set; }
        public bool ResetBeforeApplying { get; private set; } = true;
        private readonly List<WorkAssignmentRule> _rules= new List<WorkAssignmentRule>();

        public WorkAssignmentRuleset(string rulesetName, List<WorkAssignmentParameters> parameters, bool resetBeforeApplying = true)
        {
            Name = rulesetName;
            ResetBeforeApplying = resetBeforeApplying;

            foreach (var p in parameters)
            {
                _rules.Add(new WorkAssignmentRule(p));
            }

        }

        public WorkAssignmentRuleset(string rulesetName, List<WorkAssignmentRule> rules, bool resetBeforeApplying = true)
        {
            Name = rulesetName;
            _rules = rules;
            ResetBeforeApplying = resetBeforeApplying;
        }

        public static void SetAllToZero()
        {
            new WorkAssignmentRuleset("Reset", new List<WorkAssignmentRule> {
                        new WorkAssignmentRule(new WorkAssignmentParameters(0, allowOverwritingHigherPriority: true))
                    }).ApplyAutoAssignments();
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

            
            foreach (var rule in _rules)
            {
                foreach (var worktype in allWorkTypes)
                {
                    if(rule.Parameters.Worktype != null)
                    {

                        //if there's a rule that applies to only one worktype, skip all others.
                        if (rule.Parameters.Worktype != worktype)
                            continue;
                    }
                    if(rule.Parameters.WorktypeNamedIgnoreIfNonexistant != "")
                    {
                        //if there's a rule that applies to only one ignorable worktype, skip all others.
                        if (!DefDatabase<WorkTypeDef>.AllDefs.Contains(DefDatabase<WorkTypeDef>.GetNamedSilentFail(rule.Parameters.WorktypeNamedIgnoreIfNonexistant)))
                            continue;
                    }

                    //Log.Message($"Auto-assigning work type: {worktype.defName}");
                    foreach (var pawn in pawns)
                    {
                        if (pawn.workSettings == null) continue;

                        //// Apply all rules
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
                            new WorkAssignmentRule(rule.Parameters.FailedToApplyFallback).Apply(pawn, pawns, worktype);
                    }
                }
            }
        }


    }
}