using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Rules
{
    public class WorkAssignmentParameters : IExposable
    {
        public WorkAssignmentParameters(
            string ruleName,
            int priority,
            WorkTypeDef worktype = null,
            bool allowOverwritingHigherPriority = false,
            bool hasHighestSkill = false,
            bool skipIfAnotherPawnAssigned = false,
            bool isPregnant = false,
            bool isCapableOfViolence = false,
            bool assignToPawnWithFewestWorkPriorities = false,
            bool hasChildOnMap = false,
            bool isNaturalAlwaysAssign = false,
            bool randomIfMultiple = false,
            //string worktypeDefNameIgnoreIfNonexistant = "",
            int isTopXSkill = 0,
            int isNthBestPawn = 0,
            int isNthBestSkill = 0,
            int limitNumberOfWorktypes = 0,
            int skipIfPriorityForThisWorktypeAreadyAssigned = -1,
            int passionLevel = -1,
            int skillLevelGreaterThan = -1,
            int skillLevelLessThan = -1,
            //float moveSpeedGreaterThan = -1,
            //float moveSpeedLessThan = -1,
            Gender? gender = null,
            XenotypeDef xenotype = null,
            Tuple<TraitDef, int> requiredTrait = null
            //WorkAssignmentParameters assignSimilarWorktypes = null,
            //WorkAssignmentParameters failedToApplyFallback = null
            )
        {
            AllowOverwritingHigherPriority = allowOverwritingHigherPriority;
            HasHighestSkill = hasHighestSkill;
            SkipIfAnotherPawnAssigned = skipIfAnotherPawnAssigned;
            SkipIfPriorityForThisWorktypeAreadyAssigned = skipIfPriorityForThisWorktypeAreadyAssigned;
            //AssignSimilarWorktypes = assignSimilarWorktypes;
            //FailedToApplyFallback = failedToApplyFallback;
            HasChildOnMap = hasChildOnMap;

            IsPregnant = isPregnant;
            IsCapableOfViolence = isCapableOfViolence;
            AssignToPawnWithFewestWorkPriorities = assignToPawnWithFewestWorkPriorities;
            PassionLevel = (int)Mathf.Clamp(passionLevel, -1, 2);
            SkillLevelGreaterThan = skillLevelGreaterThan;
            SkillLevelLessThan = skillLevelLessThan;
            //MoveSpeedGreaterThan = moveSpeedGreaterThan;
            //MoveSpeedLessThan = moveSpeedLessThan;
            Gender = gender;
            Xenotype = xenotype;
            RequiredTrait = requiredTrait;
            Priority = Mathf.Clamp(priority, -1, 4);
            Worktype = worktype;
            LimitNumberOfWorktypes = limitNumberOfWorktypes;
            IsTopXSkill = isTopXSkill;
            IsNaturalAlwaysAssign = isNaturalAlwaysAssign;
            RandomIfMultiple = randomIfMultiple;
            //WorktypeDefNameIgnoreIfNonexistant = worktypeDefNameIgnoreIfNonexistant;
            IsNthBestPawn = isNthBestPawn;
            IsNthBestSkill = isNthBestSkill;
            RuleName = ruleName;
        }

        public int Priority;// (-1 to ignore, 0 to disable, 1-4 for priorities)

        public bool AllowOverwritingHigherPriority; //Implimented
        public bool HasHighestSkill; //Implimented
        public bool SkipIfAnotherPawnAssigned; //Implimented
        public bool IsPregnant; //Implimented
        public bool IsCapableOfViolence; //Implimented
        public bool AssignToPawnWithFewestWorkPriorities; //Implimented
        public bool HasChildOnMap;
        public bool IsNaturalAlwaysAssign;
        public bool RandomIfMultiple;

        public int SkipIfPriorityForThisWorktypeAreadyAssigned; //Implimented
        public int PassionLevel; //Implimented (-1 to ignore, 0 = none, 1 = minor, 2 = major)
        public int SkillLevelGreaterThan;
        public int SkillLevelLessThan;
        public int LimitNumberOfWorktypes;
        public int IsTopXSkill;
        public int IsNthBestPawn;
        public int IsNthBestSkill;

        //public string WorktypeDefNameIgnoreIfNonexistant;
        public string RuleName;
        //public float MoveSpeedGreaterThan;
        //public float MoveSpeedLessThan;

        public Gender? Gender;

        public XenotypeDef Xenotype;
        public Tuple<TraitDef, int> RequiredTrait;

        //public WorkAssignmentParameters AssignSimilarWorktypes; //implimented
        //public WorkAssignmentParameters FailedToApplyFallback;

        public WorkTypeDef Worktype;

        WorkAssignmentParameters() { }

        public WorkAssignmentParameters Copy()
        {
            return new WorkAssignmentParameters(RuleName, Priority, Worktype, AllowOverwritingHigherPriority, HasHighestSkill, SkipIfAnotherPawnAssigned, IsPregnant, IsCapableOfViolence, AssignToPawnWithFewestWorkPriorities, HasChildOnMap, IsNaturalAlwaysAssign, RandomIfMultiple, IsTopXSkill, IsNthBestPawn, IsNthBestSkill, LimitNumberOfWorktypes, SkipIfPriorityForThisWorktypeAreadyAssigned, PassionLevel, SkillLevelGreaterThan, SkillLevelLessThan, Gender, Xenotype, RequiredTrait);
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Priority, "Priority");// (-1 to ignore, 0 to disable, 1-4 for priorities)
            Scribe_Values.Look(ref AllowOverwritingHigherPriority, "AllowOverwritingHigherPriority"); //Implimented
            Scribe_Values.Look(ref HasHighestSkill, "HasHighestSkill"); //Implimented
            Scribe_Values.Look(ref SkipIfAnotherPawnAssigned, "SkipIfAnotherPawnAssigned"); //Implimented
            Scribe_Values.Look(ref IsPregnant, "IsPregnant"); //Implimented
            Scribe_Values.Look(ref IsCapableOfViolence, "IsCapableOfViolence"); //Implimented
            Scribe_Values.Look(ref AssignToPawnWithFewestWorkPriorities, "AssignToPawnWithFewestWorkPriorities"); //Implimented
            Scribe_Values.Look(ref HasChildOnMap, "HasChildOnMap");
            Scribe_Values.Look(ref IsNaturalAlwaysAssign, "IsNaturalAlwaysAssign");
            Scribe_Values.Look(ref RandomIfMultiple, "RandomIfMultiple");
            Scribe_Values.Look(ref SkipIfPriorityForThisWorktypeAreadyAssigned, "SkipIfPriorityForThisWorktypeAreadyAssigned"); //Implimented
            Scribe_Values.Look(ref PassionLevel, "PassionLevel"); //Implimented (-1 to ignore, 0 = none, 1 = minor, 2 = major)
            Scribe_Values.Look(ref SkillLevelGreaterThan, "SkillLevelGreaterThan");
            Scribe_Values.Look(ref SkillLevelLessThan, "SkillLevelLessThan");
            Scribe_Values.Look(ref LimitNumberOfWorktypes, "LimitNumberOfWorktypes");
            Scribe_Values.Look(ref IsTopXSkill, "IsTopXSkill");
            Scribe_Values.Look(ref IsNthBestPawn, "IsNthBestPawn");
            Scribe_Values.Look(ref IsNthBestSkill, "IsNthBestSkill");
            //Scribe_Values.Look(ref WorktypeDefNameIgnoreIfNonexistant, "WorktypeDefNameIgnoreIfNonexistant");
            Scribe_Values.Look(ref RuleName, "RuleName");


            Scribe_Values.Look(ref Gender, "Gender");

            Scribe_Defs.Look(ref Xenotype, "Xenotype");

            Scribe_Values.Look(ref RequiredTrait, "RequiredTrait");
            
            
            Scribe_Defs.Look(ref Worktype, "Worktype");
        }

    }

    public class WorkAssignmentRule : IExposable
    {
        public string Name;

        // Optional cached work type for this rule. This can be null if the rule applies to multiple work types.
        public WorkTypeDef CachedWorktype;

        // The priority level to assign (0 to disable, 1-4 for priorities)
        // Additional parameters to customize the assignment logic
        public WorkAssignmentParameters Parameters;

        public static List<WorkTypeDef> AllWorkTypes { get
            {
                var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading.OrderBy(wt => wt.naturalPriority).Reverse().ToList();
                allWorkTypes.RemoveDuplicates();
                return allWorkTypes;
            } }
        public WorkAssignmentRule() { }


        public WorkAssignmentRule(WorkAssignmentParameters parameters, WorkTypeDef worktype = null)
        {
            CachedWorktype = worktype;
            Parameters = parameters;
            Name = parameters.RuleName;
        }
        public WorkAssignmentRule(string name, WorkAssignmentParameters parameters, WorkTypeDef worktype = null)
        {
            CachedWorktype = worktype;
            Parameters = parameters;
            parameters.RuleName = name;
            Name = name;
        }
        /// <summary>
        /// Applies the assignment rule to a given pawn if all conditions are met and returns returns whether the provided worktype should be skipped for remaining pawns.
        /// </summary>
        /// <param name="pawn"></param>
        /// <param name="currentPawns"></param>
        /// <param name="worktype"></param>
        /// <returns></returns>
        public bool Apply(Pawn pawn, List<Pawn> currentPawns, WorkTypeDef worktype = null)
        {
            bool skipRemainingPawns = false;

            // Determine the work type to apply this rule to
            //starting with the worktype in the params
            var assigningWorktype = Parameters.Worktype;
            // if there is no worktype in the params, use the worktype provided to the constructor
            if (assigningWorktype == null)
            {

                assigningWorktype = CachedWorktype;
            }

            // if there is no worktype in the constructor, use the worktype provided to this method
            if (assigningWorktype == null)
            {

                assigningWorktype = worktype;
            }

            // If Worktype is STILL null, log an error and skip
            if (assigningWorktype == null)
            {
                //Log.Error("[BWT] Trying to apply priority to a null worktype.");
                //true means skip this worktype for remaining pawns
                return true;
            }

            if (Parameters.IsNaturalAlwaysAssign)
            {
                if (!worktype.alwaysStartActive)
                    return false;
            }

            // If the pawn cannot perform this work type skip assignment
            if (pawn.WorkTypeIsDisabled(assigningWorktype)) { 
                //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn cannot perform this work type.");
                return false;
            }

            //If we do not want to overwrite higher priority
            if (!Parameters.AllowOverwritingHigherPriority)
            {

            // If the current priority is higher than the rule priority and the current priority is not 0 and the rule priority is not 0
                if (pawn.workSettings.GetPriority(assigningWorktype) < Parameters.Priority && pawn.workSettings.GetPriority(assigningWorktype) != 0 && Parameters.Priority != 0)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because current priority {pawn.workSettings.GetPriority(Worktype)} is lower than rule priority {Priority}");
                    return false;
                }
            }

            //If the rule sets based on highest skill
            if (Parameters.HasHighestSkill)
            {
                foreach (Pawn p in currentPawns)
                {
                    // Skip self
                    if (p == pawn) continue;

                    if (pawn.skills.AverageOfRelevantSkillsFor(assigningWorktype).CompareTo(p.skills.AverageOfRelevantSkillsFor(assigningWorktype)) < 0)
                    {
                        //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because another pawn ({p.Name}) has a higher skill {p.workSettings.GetPriority(Worktype)}");
                        return false;
                    }
                }
            }

            // Skips if another pawn is already assigned to this work type
            if (Parameters.SkipIfAnotherPawnAssigned)
            {
                foreach (Pawn p in currentPawns)
                {
                    // Skip self
                    if (p == pawn) continue;
                    if (p.workSettings.GetPriority(assigningWorktype) > 0)
                    {
                        //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because another pawn ({p.Name}) is already assigned to this work type.");
                        return false;
                    }
                }
            }

            // Skips if pawn is not pregnant
            if (Parameters.IsPregnant)
            {
                if(!(pawn.health?.hediffSet?.HasHediff(HediffDefOf.PregnantHuman) ?? false))
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn is not pregnant.");
                    return false;
                }
            }

            //Skips if pawn is not capable of violence
            if (Parameters.IsCapableOfViolence)
            {
                if(pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn is not capable of violence.");
                    return false;
                }
            }


            //if(Parameters.AssignSimilarWorktypes != null)
            //{
            //    foreach (var relevantSkill in assigningWorktype.relevantSkills)
            //    {
            //        foreach(var wt in AllWorkTypes)
            //        {
            //            if (wt.relevantSkills.Contains(relevantSkill))
            //            {
            //                new WorkAssignmentRule(Parameters.AssignSimilarWorktypes, wt).Apply(pawn, currentPawns);
            //            }
            //        }
            //    }
            //    //Log.Warning("Not Implimented: AssignSimilarWorktypes");
            //}

            // Assigns to the pawn with the fewest work priorities assigned
            if (Parameters.AssignToPawnWithFewestWorkPriorities)
            {
                //store the pawn with the fewest work priorities
                Pawn pawnToAssign = null;
                int fewestCount = int.MaxValue;

                //loop through all pawns to find the one with the fewest work priorities
                foreach (Pawn p in currentPawns)
                {
                    //get all work types and remove duplicates

                    //count the number of work types with a priority greater than 0
                    int worktypeCount = 0;// allWorkTypes.Where((a, b) => { return p.workSettings.GetPriority(a) > 0; }).Count();
                    foreach (var wt in AllWorkTypes)
                    {
                        if (p.workSettings.GetPriority(wt) > 0)
                        {
                            worktypeCount++;
                        }
                    }
                    //if this pawn has fewer work priorities than the current highest and the worktype is not , store it
                    if (worktypeCount < fewestCount && !p.WorkTypeIsDisabled(assigningWorktype))
                    {
                        pawnToAssign = p;
                        fewestCount = worktypeCount;
                    }

                }

                //if this is not the pawn with the fewest work priorities, skip assignment
                if (pawnToAssign != pawn)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because another pawn ({pawnToAssign.Name}) has fewer work priorities ({highestCount}).");
                    //true means skip this worktype for remaining pawns
                    return false;
                }
                else
                {
                    skipRemainingPawns = true;
                }

            }

            // Skips if another pawn is already assigned to the indicated priority for this work type
            if (Parameters.SkipIfPriorityForThisWorktypeAreadyAssigned > -1)
            {
                foreach (Pawn p in currentPawns)
                {
                    // Skip self
                    if (p == pawn) continue;
                    if (p.workSettings.GetPriority(assigningWorktype) == Parameters.SkipIfPriorityForThisWorktypeAreadyAssigned)
                    {
                        //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because another pawn ({p.Name}) is already assigned to top priority for this work type.");
                        return false;
                    }
                }
            }
            // Skips if pawn does not have required passion level for this work type
            if (Parameters.PassionLevel > -1)
            {
                if(pawn.skills.MaxPassionOfRelevantSkillsFor(assigningWorktype) != (Passion)Parameters.PassionLevel)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's passion level {pawn.skills.MaxPassionOfRelevantSkillsFor(Worktype)} is less than required passion level {(Passion)Parameters.PassionLevel}");
                    return false;
                }
            }

            if(Parameters.SkillLevelGreaterThan > -1)
            {
                if(pawn.skills.AverageOfRelevantSkillsFor(assigningWorktype) <= Parameters.SkillLevelGreaterThan)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's skill level {pawn.skills.AverageOfRelevantSkillsFor(Worktype)} is less than required skill level {Parameters.SkillLevelGreaterThan}");
                    return false;
                }
            }


            if (Parameters.SkillLevelLessThan > -1)
            {
                if (pawn.skills.AverageOfRelevantSkillsFor(assigningWorktype) >= Parameters.SkillLevelLessThan)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's skill level {pawn.skills.AverageOfRelevantSkillsFor(Worktype)} is greater than required skill level {Parameters.SkillLevelGreaterThan}");
                    return false;
                }
            }

            //if(Parameters.MoveSpeedGreaterThan > -1)
            //{
            //    if(pawn.GetStatValue(StatDefOf.MoveSpeed, true) <= Parameters.MoveSpeedGreaterThan)
            //    {
            //        //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's move speed {pawn.GetStatValue(StatDefOf.MoveSpeed, true)} is less than required move speed {Parameters.MoveSpeedGreaterThan}");
            //        return false;
            //    }
            //}

            //if(Parameters.MoveSpeedLessThan > -1)
            //{
            //    if (pawn.GetStatValue(StatDefOf.MoveSpeed, true) >= Parameters.MoveSpeedLessThan)
            //    {
            //        //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's move speed {pawn.GetStatValue(StatDefOf.MoveSpeed, true)} is greater than required move speed {Parameters.MoveSpeedLessThan}");
            //        return false;
            //    }
            //}

            if (Parameters.Gender != null)
            { 
                if(pawn.gender != Parameters.Gender)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn is not the required gender {Parameters.Gender}");
                    return false;
                }
            }

            if (Parameters.Xenotype != null)
            {
                if (pawn.genes?.Xenotype != Parameters.Xenotype)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn is not the required xenotype {Parameters.Xenotype}");
                    return false;
                }
            }

            if (Parameters.RequiredTrait != null)
            {
                if (!pawn.story?.traits.HasTrait((TraitDef)Parameters.RequiredTrait.Item1, Parameters.RequiredTrait.Item2) ?? true)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn does not have the required trait {Parameters.RequiredTrait}");
                    return false;
                }
            }

            if(Parameters.HasChildOnMap)
            {
                bool hasChild = false;
                foreach (var p in Find.CurrentMap.mapPawns.FreeColonists.Where(p=>(int)p.DevelopmentalStage < (int)DevelopmentalStage.Adult))
                {
                    // Skip self (you're not your own child)
                    if (p == pawn) continue;
                    // Skip adults (they're not children)
                    if (p.DevelopmentalStage == DevelopmentalStage.Adult) continue;

                    //Log.Message($"[BWT] Checking if parent {pawn.Name} has child {p.Name}.");

                    if (p.GetFather() == null)
                    {
                        //Log.Message($"[BWT] Child {p.Name} has no recorded father.");
                    }
                    if (p.GetMother() == null)
                    {
                        //Log.Message($"[BWT] Child {p.Name} has no recorded mother.");
                    }
                    //if the child is a birth child of the current pawn
                    if (p.GetFather() == pawn || p.GetMother() == pawn)
                    {
                        //Log.Message($"[BWT] {pawn.Name} has a child on the map ({p.Name}).");
                        hasChild = true;
                        break;
                    }
                }
                if (!hasChild)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn does not have a child on the map.");
                    return false;
                }
            }

            if (Parameters.LimitNumberOfWorktypes > 0)
            {
                if (pawn.workSettings.priorities.Where((kvp) => { return kvp.Value > 0; }).Count() >= Parameters.LimitNumberOfWorktypes)
                {
                    //Log.Message($"[BWT] Skipping because this pawn is full");
                    return false;
                }
            }

            if (Parameters.IsTopXSkill > 0)
            {
                //Get all non-disabled worktypes
                var wts = DefDatabase<WorkTypeDef>.AllDefs.Where((WorkTypeDef w) => !w.alwaysStartActive && !pawn.WorkTypeIsDisabled(w)).OrderByDescending(delegate (WorkTypeDef w)
                {
                    Pawn_SkillTracker skills = pawn.skills;
                    return (skills == null) ? 1f : skills.AverageOfRelevantSkillsFor(w);
                });

                if (!wts.Take(Parameters.IsTopXSkill).Contains(assigningWorktype))
                {
                    return false;
                }


            }




            //I need to make this choose all pawns with the same skill level if there's a tie. Also I need to reorder the rules to make the one that chooses whether a random one is assigned goes last.
            if (Parameters.IsNthBestPawn > 0)
            {
                List<Pawn> sortedPawns = currentPawns.Where(p => !p.WorkTypeIsDisabled(assigningWorktype)).OrderByDescending(p => p.skills.AverageOfRelevantSkillsFor(assigningWorktype)).ToList();
                if (sortedPawns.Count >= Parameters.IsNthBestPawn)
                {
                    sortedPawns = sortedPawns.Where(p => p.skills.AverageOfRelevantSkillsFor(assigningWorktype) == sortedPawns[Parameters.IsNthBestPawn - 1].skills.AverageOfRelevantSkillsFor(assigningWorktype)).ToList();

                    //foreach (var p in sortedPawns)
                    //{
                    //    Log.Message("- " + p.Name + "'s " +assigningWorktype.defName + " skill: " + p.skills.AverageOfRelevantSkillsFor(assigningWorktype));
                    //    //Log.Error(p.Name + " - " + p.skills.AverageOfRelevantSkillsFor(assigningWorktype));
                    //}



                    if (!sortedPawns.Contains(pawn))
                    {
                        //if (Parameters.RandomIfTied)
                        {
                            //directly ripped straight out of rimworld but what can I do? it's a mod lol.
                            //currentPawns.Where(p => !p.WorkTypeIsDisabled(assigningWorktype)).InRandomOrder().MaxBy((Pawn c) => c.skills.AverageOfRelevantSkillsFor(assigningWorktype)).workSettings.SetPriority(assigningWorktype, Parameters.Priority);
                            //return true;
                        }
                        //else
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    //Log.Message($"[BWT] Not enough pawns to assign {Parameters.IsNthBestPawn}st/nd/th best to {assigningWorktype.defName}. Only {sortedPawns.Count} pawns available. Skipping this step.");
                    return false;
                }

            }

            if (Parameters.IsNthBestSkill > 0)
            {
                List<WorkTypeDef> bestWorkInOrder = AllWorkTypes.Where(wt => !pawn.WorkTypeIsDisabled(wt)).OrderByDescending(wt => pawn.skills.AverageOfRelevantSkillsFor(wt)).ToList();

                if(bestWorkInOrder.Count >= Parameters.IsNthBestSkill)
                {
                    if(bestWorkInOrder[Parameters.IsNthBestSkill - 1] != assigningWorktype)
                    {
                        return false;
                    }
                }
                else
                {
                    //Log.Message($"[BWT] Not enough worktypes to assign {Parameters.IsNthBestSkill}st/nd/th best to {assigningWorktype.defName}. Only {bestWorkInOrder.Count} worktypes available. Skipping this step.");
                    return false;
                }
            }


          
            // If we reach here, all conditions are met - assign the priority
            //Log.Message($"[BWT] Assigning {Worktype.defName} to {pawn.Name} with priority {Priority}.");
            pawn.workSettings.SetPriority(assigningWorktype, Mathf.Clamp(Parameters.Priority, 0, 4));
            return skipRemainingPawns;
        }

        public WorkAssignmentRule Copy()
        {
            return new WorkAssignmentRule(Name + " (Copy)", Parameters.Copy(), CachedWorktype);
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Name, "Name");
            Scribe_Defs.Look(ref CachedWorktype, "Worktype");
            Scribe_Deep.Look(ref Parameters, "Parameters");
        }

    }
}
