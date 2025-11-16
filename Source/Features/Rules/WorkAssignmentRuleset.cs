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
    public class WorkAssignmentRuleset : IExposable
    {
        //private readonly BetterWorkTabSettings _settings;

        public string Name;
        public bool ResetBeforeApplying = true;
        public List<WorkAssignmentRule> Rules = new List<WorkAssignmentRule>();
        public bool IsDefault = false;
        private static List<WorkTypeDef> cachedWorkTypes;

        private static List<WorkTypeDef> CachedWorkTypes
        {
            get
            {
                if (cachedWorkTypes == null || cachedWorkTypes.Count == 0)
                {
                    cachedWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading
                        .OrderByDescending(wt => wt.naturalPriority)
                        .ToList();
                    cachedWorkTypes.RemoveDuplicates();
                }

                return cachedWorkTypes;
            }
        }
        public WorkAssignmentRuleset() { }

        public WorkAssignmentRuleset(string rulesetName, List<WorkAssignmentParameters> parameters, bool resetBeforeApplying = true, bool isDefault = false)
        {
            Name = rulesetName;
            ResetBeforeApplying = resetBeforeApplying;

            foreach (var p in parameters)
            {
                Rules.Add(new WorkAssignmentRule(p));
            }
            IsDefault = isDefault;
        }

        public WorkAssignmentRuleset(string rulesetName, List<WorkAssignmentRule> rules, bool resetBeforeApplying = true, bool isDefault = false)
        {
            Name = rulesetName;
            Rules = rules;
            ResetBeforeApplying = resetBeforeApplying;
            IsDefault = isDefault;
        }

        public static void SetAllToZero()
        {
            new WorkAssignmentRuleset("Reset", new List<WorkAssignmentRule> {
                        new WorkAssignmentRule(new WorkAssignmentParameters("Reset", 0, allowOverwritingHigherPriority: true))
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

            var allWorkTypes = CachedWorkTypes;


            foreach (var rule in Rules)
            {
                foreach (var worktype in allWorkTypes)
                {

                    if (rule.Parameters.Worktype != null)
                    {

                        //if there's a rule that applies to only one worktype, skip all others.
                        if (rule.Parameters.Worktype != worktype)
                            continue;
                    }
                    //if (rule.Parameters.WorktypeDefNameIgnoreIfNonexistant != "")
                    //{
                    //    //if there's a rule that applies to only one ignorable worktype, skip all others.
                    //    if (!DefDatabase<WorkTypeDef>.AllDefs.Contains(DefDatabase<WorkTypeDef>.GetNamedSilentFail(rule.Parameters.WorktypeDefNameIgnoreIfNonexistant)))
                    //        continue;
                    //}
                    List<Pawn> pawnsForThisWorktype = new List<Pawn>();

                    //Log.Message($"Auto-assigning work type: {worktype.defName}");
                    foreach (var pawn in pawns)
                    {
                        if (pawn.workSettings == null) continue;
                        int originalPriority = pawn.workSettings.GetPriority(worktype);
                        bool pawnAlreadyAssigned = originalPriority > 0;
                        //// Apply all rules
                        if (rule.Apply(pawn, pawns, worktype))
                        {
                            //Log.Message("assigned " + worktype.defName +" to " + pawn.NameShortColored +". Skipping remaining pawns.");
                            //Apply returns true if the rest of the pawns should be skipped for this worktype
                            break;
                        }
                        if (rule.Parameters.RandomIfMultiple)
                        {
                            int updatedPriority = pawn.workSettings.GetPriority(worktype);
                            if (updatedPriority > 0 && !pawnAlreadyAssigned)
                            {
                                //this was assigned. add to list for potential randomization later.
                                pawnsForThisWorktype.Add(pawn);
                            }
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
                        List<Pawn> eligiblePawns = new List<Pawn>();
                        foreach (var pawn in pawnsForThisWorktype)
                        {
                            if (!pawn.WorkTypeIsDisabled(worktype))
                            {
                                eligiblePawns.Add(pawn);
                            }
                        }

                        if (eligiblePawns.Count > 0)
                        {
                            eligiblePawns.RandomElement().workSettings.SetPriority(worktype, rule.Parameters.Priority);
                        }
                    }

                    //if (rule.Parameters.FailedToApplyFallback != null && !pawns.Where(p => { return p.workSettings.GetPriority(worktype) > 0; }).Any())
                    //{
                    //    Log.Message($"No pawn could be assigned to work type: {worktype.defName}. Applying fallback.");
                    //    foreach (var pawn in pawns)
                    //        new WorkAssignmentRule(rule.Parameters.FailedToApplyFallback).Apply(pawn, pawns, worktype);
                    //}

                }
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref IsDefault, "IsDefault", false);
            Scribe_Values.Look(ref Name, "Name");
            Scribe_Values.Look(ref ResetBeforeApplying, "ResetBeforeApplying");
            Scribe_Collections.Look(ref Rules, "Rules", LookMode.Deep);
        }

        public WorkAssignmentRuleset Copy()
        {
            //                                                                                              VVV Never let this be true for a copy because it will be impossible to delete!
            return new WorkAssignmentRuleset((Name + " (Copy)"), Rules.ListFullCopy(), ResetBeforeApplying, false );
        }
    }
}
