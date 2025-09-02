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
    public class AssignWorkParams
    {
        public AssignWorkParams(
            int priority,
            bool allowOverwritingHigherPriority = false,
            bool hasHighestSkill = false,
            bool skipIfAnotherPawnAssigned = false,
            bool isPregnant = false,
            bool isCapableOfViolence = false,
            bool assignToPawnWithFewestWorkPriorities = false,
            bool hasChildOnMap = false,

        int skipIfPriorityForThisWorktypeAreadyAssigned = -1,
            int passionLevel = -1,
            int skillLevelGreaterThan = -1,
            int skillLevelLessThan = -1,
            float moveSpeedGreaterThan = -1,
            float moveSpeedLessThan = -1,
            Gender? gender = null,
            XenotypeDef xenotype = null,
            TraitDef trait = null,

            AssignWorkParams assignSimilarWorktypes = null,
            AssignWorkParams failedToApplyFallback = null,

            WorkTypeDef worktype = null
            )
        {
            AllowOverwritingHigherPriority = allowOverwritingHigherPriority;
            HasHighestSkill = hasHighestSkill;
            SkipIfAnotherPawnAssigned = skipIfAnotherPawnAssigned;
            SkipIfPriorityForThisWorktypeAreadyAssigned = skipIfPriorityForThisWorktypeAreadyAssigned;
            AssignSimilarWorktypes = assignSimilarWorktypes;
            FailedToApplyFallback = failedToApplyFallback;
            HasChildOnMap = hasChildOnMap;

            IsPregnant = isPregnant;
            IsCapableOfViolence = isCapableOfViolence;
            AssignToPawnWithFewestWorkPriorities = assignToPawnWithFewestWorkPriorities;
            PassionLevel = (int)Mathf.Clamp(passionLevel, -1, 2);
            SkillLevelGreaterThan = skillLevelGreaterThan;
            SkillLevelLessThan = skillLevelLessThan;
            MoveSpeedGreaterThan = moveSpeedGreaterThan;
            MoveSpeedLessThan = moveSpeedLessThan;
            Gender = gender;
            Xenotype = xenotype;
            RequiredTrait = trait;
            Priority = Mathf.Clamp(priority, -1, 4);
            Worktype = worktype;
        }

        public int Priority;// (-1 to ignore, 0 to disable, 1-4 for priorities)

        public bool AllowOverwritingHigherPriority; //Implimented
        public bool HasHighestSkill; //Implimented
        public bool SkipIfAnotherPawnAssigned; //Implimented
        public bool IsPregnant; //Implimented
        public bool IsCapableOfViolence; //Implimented
        public bool AssignToPawnWithFewestWorkPriorities; //Implimented
        public bool HasChildOnMap;
        //public bool MustBeAssigned = false;

        public int SkipIfPriorityForThisWorktypeAreadyAssigned; //Implimented
        public int PassionLevel; //Implimented (-1 to ignore, 0 = none, 1 = minor, 2 = major)
        public int SkillLevelGreaterThan; 
        public int SkillLevelLessThan;
        
        public float MoveSpeedGreaterThan;
        public float MoveSpeedLessThan;
        
        public Gender? Gender;
        
        public XenotypeDef Xenotype;
        public TraitDef RequiredTrait;

        public AssignWorkParams AssignSimilarWorktypes; //implimented
        public AssignWorkParams FailedToApplyFallback;

        public WorkTypeDef Worktype;

    }

    internal class AssignWorkRule
    {
        // Optional cached work type for this rule. This can be null if the rule applies to multiple work types.
        public WorkTypeDef CachedWorktype { get; }

        // The priority level to assign (0 to disable, 1-4 for priorities)
        // Additional parameters to customize the assignment logic
        public AssignWorkParams Parameters;

        public static List<WorkTypeDef> AllWorkTypes { get
            {
                var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading.OrderBy(wt => wt.naturalPriority).Reverse().ToList();
                allWorkTypes.RemoveDuplicates();
                return allWorkTypes;
            } }

        public AssignWorkRule(AssignWorkParams parameters, WorkTypeDef worktype = null)
        {
            CachedWorktype = worktype;
            Parameters = parameters;

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
            var Worktype = Parameters.Worktype;
            // if there is no worktype in the params, use the worktype provided to the constructor
            if (Worktype == null)
            {

                Worktype = CachedWorktype;
            }

            // if there is no worktype in the constructor, use the worktype provided to this method
            if (Worktype == null)
            {

                Worktype = worktype;
            }

            // If Worktype is STILL null, log an error and skip
            if (Worktype == null)
            {
                Log.Error("[BWT] Trying to apply priority to a null worktype.");
                //true means skip this worktype for remaining pawns
                return true;
            }

            // If the pawn cannot perform this work type skip assignment
            if (pawn.WorkTypeIsDisabled(Worktype)) { 
                //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn cannot perform this work type.");
                return false;
            }

            //If we do not want to overwrite higher priority
            if (!Parameters.AllowOverwritingHigherPriority)
            {

            // If the current priority is higher than the rule priority and the current priority is not 0 and the rule priority is not 0
                if (pawn.workSettings.GetPriority(Worktype) < Parameters.Priority && pawn.workSettings.GetPriority(Worktype) != 0 && Parameters.Priority != 0)
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

                    if (pawn.skills.AverageOfRelevantSkillsFor(Worktype).CompareTo(p.skills.AverageOfRelevantSkillsFor(Worktype)) < 0)
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
                    if (p.workSettings.GetPriority(Worktype) > 0)
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


            if(Parameters.AssignSimilarWorktypes != null)
            {
                foreach (var relevantSkill in Worktype.relevantSkills)
                {
                    foreach(var wt in AllWorkTypes)
                    {
                        if (wt.relevantSkills.Contains(relevantSkill))
                        {
                            new AssignWorkRule(Parameters.AssignSimilarWorktypes, wt).Apply(pawn, currentPawns);
                        }
                    }
                }
                //Log.Warning("Not Implimented: AssignSimilarWorktypes");
            }

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
                    if (worktypeCount < fewestCount && !p.WorkTypeIsDisabled(Worktype))
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
                    if (p.workSettings.GetPriority(Worktype) == Parameters.SkipIfPriorityForThisWorktypeAreadyAssigned)
                    {
                        //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because another pawn ({p.Name}) is already assigned to top priority for this work type.");
                        return false;
                    }
                }
            }
            // Skips if pawn does not have required passion level for this work type
            if (Parameters.PassionLevel > -1)
            {
                if(pawn.skills.MaxPassionOfRelevantSkillsFor(Worktype) < (Passion)Parameters.PassionLevel)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's passion level {pawn.skills.MaxPassionOfRelevantSkillsFor(Worktype)} is less than required passion level {(Passion)Parameters.PassionLevel}");
                    return false;
                }
            }

            if(Parameters.SkillLevelGreaterThan > -1)
            {
                if(pawn.skills.AverageOfRelevantSkillsFor(Worktype) <= Parameters.SkillLevelGreaterThan)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's skill level {pawn.skills.AverageOfRelevantSkillsFor(Worktype)} is less than required skill level {Parameters.SkillLevelGreaterThan}");
                    return false;
                }
            }


            if (Parameters.SkillLevelLessThan > -1)
            {
                if (pawn.skills.AverageOfRelevantSkillsFor(Worktype) >= Parameters.SkillLevelLessThan)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's skill level {pawn.skills.AverageOfRelevantSkillsFor(Worktype)} is greater than required skill level {Parameters.SkillLevelGreaterThan}");
                    return false;
                }
            }

            if(Parameters.MoveSpeedGreaterThan > -1)
            {
                if(pawn.GetStatValue(StatDefOf.MoveSpeed, true) <= Parameters.MoveSpeedGreaterThan)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's move speed {pawn.GetStatValue(StatDefOf.MoveSpeed, true)} is less than required move speed {Parameters.MoveSpeedGreaterThan}");
                    return false;
                }
            }

            if(Parameters.MoveSpeedLessThan > -1)
            {
                if (pawn.GetStatValue(StatDefOf.MoveSpeed, true) >= Parameters.MoveSpeedLessThan)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's move speed {pawn.GetStatValue(StatDefOf.MoveSpeed, true)} is greater than required move speed {Parameters.MoveSpeedLessThan}");
                    return false;
                }
            }

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
                if (!pawn.story?.traits.HasTrait(Parameters.RequiredTrait) ?? true)
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

                    Log.Message($"[BWT] Checking if parent {pawn.Name} has child {p.Name}.");

                    if (p.GetFather() == null)
                    {
                        Log.Message($"[BWT] Child {p.Name} has no recorded father.");
                    }
                    if (p.GetMother() == null)
                    {
                        Log.Message($"[BWT] Child {p.Name} has no recorded mother.");
                    }
                    //if the child is a birth child of the current pawn
                    if (p.GetFather() == pawn || p.GetMother() == pawn)
                    {
                        Log.Message($"[BWT] {pawn.Name} has a child on the map ({p.Name}).");
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

            // If we reach here, all conditions are met - assign the priority
            //Log.Message($"[BWT] Assigning {Worktype.defName} to {pawn.Name} with priority {Priority}.");
            pawn.workSettings.SetPriority(Worktype, Mathf.Clamp(Parameters.Priority, 0, 4));
            return skipRemainingPawns;
        }
    }
}