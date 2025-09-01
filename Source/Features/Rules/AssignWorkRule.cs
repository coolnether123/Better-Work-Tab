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
            bool allowOverwritingHigherPriority = false,
            bool hasHighestSkill = false,
            bool skipIfAnotherPawnAssigned = false,
            bool assignAllReleventWorktypesForSkill = false,
            bool isPregnant = false,
            bool isCapableOfViolence = false,
            bool isDedicatedPawn = false,
            bool assignToPawnWithFewestWorkPriorities = false,
            int skipIfPriorityForThisWorktypeAreadyAssigned = -1,
            int passionLevel = -1,
            int skillLevelGreaterThan = -1,
            int skillLevelLessThan = -1,
            float moveSpeedGreaterThan = -1,
            float moveSpeedLessThan = -1,
            Gender? gender = null,
            XenotypeDef xenotype = null,
            TraitDef trait = null
            )
        {
            AllowOverwritingHigherPriority = allowOverwritingHigherPriority;
            HasHighestSkill = hasHighestSkill;
            SkipIfAnotherPawnAssigned = skipIfAnotherPawnAssigned;
            SkipIfPriorityForThisWorktypeAreadyAssigned = skipIfPriorityForThisWorktypeAreadyAssigned;
            AssignAllReleventWorktypesForSkill = assignAllReleventWorktypesForSkill;
            IsPregnant = isPregnant;
            IsCapableOfViolence = isCapableOfViolence;
            MustHaveDedicatedPawn = isDedicatedPawn;
            AssignToPawnWithFewestWorkPriorities = assignToPawnWithFewestWorkPriorities;
            PassionLevel = (int)Mathf.Clamp(passionLevel, -1, 2);
            SkillLevelGreaterThan = skillLevelGreaterThan;
            SkillLevelLessThan = skillLevelLessThan;
            MoveSpeedGreaterThan = moveSpeedGreaterThan;
            MoveSpeedLessThan = moveSpeedLessThan;
            Gender = gender;
            Xenotype = xenotype;
            RequiredTrait = trait;
        }

        public bool AllowOverwritingHigherPriority; //Implimented
        public bool HasHighestSkill; //Implimented
        public bool SkipIfAnotherPawnAssigned; //Implimented
        public bool AssignAllReleventWorktypesForSkill; //implimented
        public bool IsPregnant; //Implimented
        public bool IsCapableOfViolence; //Implimented
        /*TODO: */public bool MustHaveDedicatedPawn;
        public bool AssignToPawnWithFewestWorkPriorities; //Implimented

        public int SkipIfPriorityForThisWorktypeAreadyAssigned; //Implimented
        public int PassionLevel; //Implimented (-1 to ignore, 0 = none, 1 = minor, 2 = major)
        public int SkillLevelGreaterThan; 
        public int SkillLevelLessThan;
        
        public float MoveSpeedGreaterThan;
        public float MoveSpeedLessThan;
        
        public Gender? Gender;
        
        public XenotypeDef Xenotype;
        public TraitDef RequiredTrait;

    }

    internal class AssignWorkRule
    {
        // Optional cached work type for this rule. This can be null if the rule applies to multiple work types.
        public WorkTypeDef CachedWorktype { get; }

        // The priority level to assign (0 to disable, 1-4 for priorities)
        int Priority;

        // Additional parameters to customize the assignment logic
        AssignWorkParams Parameters;

        public AssignWorkRule(  int priority, AssignWorkParams parameters, WorkTypeDef worktype = null)
        {
            CachedWorktype = worktype;
            Priority = priority;
            Parameters = parameters;

        }

        public void Apply(Pawn pawn, List<Pawn> currentPawns, WorkTypeDef worktype = null)
        {

            // Use the cached work type if available
            var Worktype = CachedWorktype;
            // If no specific work type is set for this rule, use the provided work type
            if (Worktype == null)
            {
                Worktype = worktype;
                if (Worktype == null)
                {
                    Log.Error("[BWT] Trying to apply priority to a null worktype.");
                    return;
                }
            }

            // If the pawn cannot perform this work type skip assignment
            if (pawn.WorkTypeIsDisabled(Worktype)) { 
                //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn cannot perform this work type.");
                return;
            }

            //If we do not want to overwrite higher priority
            if (!Parameters.AllowOverwritingHigherPriority)
                Log.Message("We are not allowing overwriting higher priority");
            // If the current priority is higher than the rule priority and the current priority is not 0 and the rule priority is not 0
            if (pawn.workSettings.GetPriority(Worktype) < Priority && pawn.workSettings.GetPriority(Worktype) != 0 && Priority != 0)
                {
                Log.Message("Skip because the current priority is higher than potential");
                //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because current priority {pawn.workSettings.GetPriority(Worktype)} is lower than rule priority {Priority}");
                return;
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
                        return;
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
                        Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because another pawn ({p.Name}) is already assigned to this work type.");
                        return;
                    }
                }
            }

            // Skips if pawn is not pregnant
            if (Parameters.IsPregnant)
            {
                if(!(pawn.health?.hediffSet?.HasHediff(HediffDefOf.PregnantHuman) ?? false))
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn is not pregnant.");
                    return;
                }
            }

            //Skips if pawn is not capable of violence
            if (Parameters.IsCapableOfViolence)
            {
                if(pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn is not capable of violence.");
                    return;
                }
            }

            if (Parameters.MustHaveDedicatedPawn)
            {
                Log.Warning("Not Implimented: MustHaveDedicatedPawn");
            }

            if(Parameters.AssignToPawnWithFewestWorkPriorities)
            {
                Pawn pawnToAssign = null;
                int highestCount = -1;
                foreach (Pawn p in currentPawns)
                {
                    var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
                    allWorkTypes.RemoveDuplicates();
                    
                    
                    int worktypeCount = 0;
                    foreach (var wt in allWorkTypes)
                    {
                        if (p.workSettings.GetPriority(wt) > 0)
                        {
                            worktypeCount++;
                        }
                    }

                    if(worktypeCount > highestCount)
                    {
                        pawnToAssign = p;
                        highestCount = worktypeCount;
                    }

                }

                if(pawnToAssign != pawn)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because another pawn ({pawnToAssign.Name}) has fewer work priorities ({highestCount}).");
                    return;
                }

            }



            // Skips if another pawn is already assigned to the indicated priority for this work type
            if (Parameters.SkipIfPriorityForThisWorktypeAreadyAssigned > -1)
            {
                Log.Message("Checking if another pawn is already assigned to this priority for this work type: " + Parameters.SkipIfPriorityForThisWorktypeAreadyAssigned);
                foreach (Pawn p in currentPawns)
                {
                    // Skip self
                    if (p == pawn) continue;
                    if (p.workSettings.GetPriority(Worktype) == Parameters.SkipIfPriorityForThisWorktypeAreadyAssigned)
                    {
                        Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because another pawn ({p.Name}) is already assigned to top priority for this work type.");
                        return;
                    }
                }
            }
            // Skips if pawn does not have required passion level for this work type
            if (Parameters.PassionLevel > -1)
            {
                if(pawn.skills.MaxPassionOfRelevantSkillsFor(Worktype) < (Passion)Parameters.PassionLevel)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's passion level {pawn.skills.MaxPassionOfRelevantSkillsFor(Worktype)} is less than required passion level {(Passion)Parameters.PassionLevel}");
                    return;
                }
            }

            if(Parameters.SkillLevelGreaterThan > -1)
            {
                if(pawn.skills.AverageOfRelevantSkillsFor(Worktype) <= Parameters.SkillLevelGreaterThan)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's skill level {pawn.skills.AverageOfRelevantSkillsFor(Worktype)} is less than required skill level {Parameters.SkillLevelGreaterThan}");
                    return;
                }
            }


            if (Parameters.SkillLevelLessThan > -1)
            {
                if (pawn.skills.AverageOfRelevantSkillsFor(Worktype) >= Parameters.SkillLevelLessThan)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's skill level {pawn.skills.AverageOfRelevantSkillsFor(Worktype)} is greater than required skill level {Parameters.SkillLevelGreaterThan}");
                    return;
                }
            }

            if(Parameters.MoveSpeedGreaterThan > -1)
            {
                if(pawn.GetStatValue(StatDefOf.MoveSpeed, true) <= Parameters.MoveSpeedGreaterThan)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's move speed {pawn.GetStatValue(StatDefOf.MoveSpeed, true)} is less than required move speed {Parameters.MoveSpeedGreaterThan}");
                    return;
                }
            }

            if(Parameters.MoveSpeedLessThan > -1)
            {
                if (pawn.GetStatValue(StatDefOf.MoveSpeed, true) >= Parameters.MoveSpeedLessThan)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn's move speed {pawn.GetStatValue(StatDefOf.MoveSpeed, true)} is greater than required move speed {Parameters.MoveSpeedLessThan}");
                    return;
                }
            }

            if (Parameters.Gender != null)
            { 
                if(pawn.gender != Parameters.Gender)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn is not the required gender {Parameters.Gender}");
                    return;
                }
            }

            if (Parameters.Xenotype != null)
            {
                if (pawn.genes?.Xenotype != Parameters.Xenotype)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn is not the required xenotype {Parameters.Xenotype}");
                    return;
                }
            }

            if (Parameters.RequiredTrait != null)
            {
                if (!pawn.story?.traits.HasTrait(Parameters.RequiredTrait) ?? true)
                {
                    //Log.Message($"[BWT] Skipping {Worktype.defName} for {pawn.Name} because pawn does not have the required trait {Parameters.RequiredTrait}");
                    return;
                }
            }

                pawn.workSettings.SetPriority(Worktype, Mathf.Clamp(Priority, 0, 4));
        }
    }
}