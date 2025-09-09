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

                    if (rule.Parameters.Worktype != null)
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
                    List<Pawn> pawnsForThisWorktype = new List<Pawn>();

                    //Log.Message($"Auto-assigning work type: {worktype.defName}");
                    foreach (var pawn in pawns)
                    {
                        if (pawn.workSettings == null) continue;
                        bool pawnAlreadyAssigned = pawn.workSettings.GetPriority(worktype) > 0;
                        //// Apply all rules
                        if (rule.Apply(pawn, pawns, worktype))
                        {
                            //Log.Message("assigned " + worktype.defName +" to " + pawn.NameShortColored +". Skipping remaining pawns.");
                            //Apply returns true if the rest of the pawns should be skipped for this worktype
                            break;
                        }
                        if(rule.Parameters.RandomIfMultiple && pawn.workSettings.GetPriority(worktype) > 0 && !pawnAlreadyAssigned)
                        {
                            //this was assigned. add to list for potential randomization later.
                            pawnsForThisWorktype.Add(pawn);
                        }
                    }

                    if (rule.Parameters.RandomIfMultiple && pawnsForThisWorktype.Count > 0)
                    {
                        //copy the list so we can sort it
                        //filter to only those with a priority that has been set

                        //reset before reassigning for randomization
                        foreach (var p in pawnsForThisWorktype)
                        {
                            Log.Message($"Resetting {worktype.defName} for {p.NameShortColored} before random assignment.");
                            //if (p.workSettings.GetPriority(worktype) == rule.Parameters.Priority)
                            p.workSettings.SetPriority(worktype, 0);
                        }
                        //directly ripped straight out of rimworld but what can I do? it's a mod lol.
                        pawnsForThisWorktype.Where(p => !p.WorkTypeIsDisabled(worktype)).InRandomOrder().First().workSettings.SetPriority(worktype, rule.Parameters.Priority);
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