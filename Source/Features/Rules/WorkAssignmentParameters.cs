
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using System.Reflection;

namespace Better_Work_Tab.Features.Rules
{
    [AttributeUsage(AttributeTargets.Field)]
    public class RuleParameterAttribute : Attribute { }

    public class WorkAssignmentParameters : IExposable
    {
        [RuleParameter]
        public string RuleName;
        
        [RuleParameter]
        public WorkTypeDef Worktype;

        [RuleParameter]
        public bool IgnoreIfWorktypeNonexistent;

        [RuleParameter]
        public int Priority;

        [RuleParameter]
        public bool AllowOverwritingHigherPriority;

        [RuleParameter]
        public int PassionLevel = -1;

        [RuleParameter]
        public XenotypeDef Xenotype;

        [RuleParameter]
        public Tuple<TraitDef, int> RequiredTrait;

        [RuleParameter]
        public Gender? Gender;

        [RuleParameter]
        public bool HasHighestSkill;

        [RuleParameter]
        public int IsTopXSkill;

        [RuleParameter]
        public int IsNthBestPawn;

        [RuleParameter]
        public int IsNthBestSkill;

        [RuleParameter]
        public bool RandomIfMultiple;
        
        [RuleParameter]
        public bool IsNaturalAlwaysAssign;
        
        [RuleParameter]
        public bool AssignToPawnWithFewestWorkPriorities;

        [RuleParameter]
        public bool SkipIfAnotherPawnAssigned;
        
        [RuleParameter]
        public int SkipIfPriorityForThisWorktypeAreadyAssigned = -1;
        
        [RuleParameter]
        public int LimitNumberOfWorktypes;
        
        [RuleParameter]
        public int SkillLevelGreaterThan = -1;
        
        [RuleParameter]
        public int SkillLevelLessThan = -1;
        
        [RuleParameter]
        public bool IsCapableOfViolence;
        
        [RuleParameter]
        public bool IsPregnant;

        [RuleParameter]
        public bool HasChildOnMap;

        [RuleParameter]
        public float MoveSpeedGreaterThan = -1;
        
        [RuleParameter]
        public float MoveSpeedLessThan = -1;

        public string WorktypeString = "";
        public string XenotypeString = "";
        public string TraitString = "";
        public int? TraitDegree = null;
        public List<string> ActiveConditions = new List<string>();


        public WorkAssignmentParameters(string ruleName = "", 
            int priority = 0, 
            WorkTypeDef worktype = null, 
            int skipIfPriorityForThisWorktypeAreadyAssigned = -1, 
            bool skipIfAnotherPawnAssigned = false, 
            bool assignToPawnWithFewestWorkPriorities = false, 
            Gender? gender = null, 
            bool isPregnant = false, 
            XenotypeDef xenotype = null, 
            Tuple<TraitDef, int> requiredTrait = null, 
            bool isNaturalAlwaysAssign = false, 
            bool isCapableOfViolence = false, 
            bool allowOverwritingHigherPriority = false, 
            int limitNumberOfWorktypes = 0, 
            int passionLevel = -1, 
            int skillLevelGreaterThan = -1, 
            int skillLevelLessThan = -1, 
            bool hasHighestSkill = false, 
            int isTopXSkill = 0, 
            int isNthBestPawn = 0, 
            int isNthBestSkill = 0, 
            bool hasChildOnMap = false, 
            bool randomIfMultiple = false, 
            bool ignoreIfWorktypeNonexistent = false, 
            float moveSpeedGreaterThan = -1, 
            float moveSpeedLessThan = -1, 
            string worktypeString = "")
        {
            RuleName = ruleName;
            Priority = priority;

            if (worktype != null)
            {
                Worktype = worktype;
                WorktypeString = worktype.defName;
            }
            else if (worktypeString != "")
            {

                WorktypeString = worktypeString;
                //Worktype = DefDatabase<WorkTypeDef>.GetNamed(WorktypeString);
            }

            SkipIfPriorityForThisWorktypeAreadyAssigned = skipIfPriorityForThisWorktypeAreadyAssigned;
            SkipIfAnotherPawnAssigned = skipIfAnotherPawnAssigned;
            AssignToPawnWithFewestWorkPriorities = assignToPawnWithFewestWorkPriorities;
            Gender = gender;
            IsPregnant = isPregnant;

            Xenotype = xenotype;
            if(xenotype != null)
            {
                XenotypeString = xenotype.defName;
            }

            if(requiredTrait != null)
            {
                TraitString = requiredTrait.Item1.defName;
                TraitDegree = requiredTrait.Item2;
            }
            RequiredTrait = requiredTrait;
            

            IsNaturalAlwaysAssign = isNaturalAlwaysAssign;
            IsCapableOfViolence = isCapableOfViolence;
            AllowOverwritingHigherPriority = allowOverwritingHigherPriority;
            LimitNumberOfWorktypes = limitNumberOfWorktypes;
            PassionLevel = passionLevel;
            SkillLevelGreaterThan = skillLevelGreaterThan;
            SkillLevelLessThan = skillLevelLessThan;
            HasHighestSkill = hasHighestSkill;
            IsTopXSkill = isTopXSkill;
            IsNthBestPawn = isNthBestPawn;
            IsNthBestSkill = isNthBestSkill;
            HasChildOnMap = hasChildOnMap;
            RandomIfMultiple = randomIfMultiple;
            IgnoreIfWorktypeNonexistent = ignoreIfWorktypeNonexistent;
            MoveSpeedGreaterThan = moveSpeedGreaterThan;
            MoveSpeedLessThan = moveSpeedLessThan;
        }
        public WorkAssignmentParameters() { }

        public WorkAssignmentParameters Copy()
        {
            var copy = (WorkAssignmentParameters)this.MemberwiseClone();
            copy.ActiveConditions = ActiveConditions?.ToList() ?? new List<string>();
            return copy;
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                SyncBackingStringsFromDefs();
            }

            Scribe_Values.Look(ref RuleName, "RuleName");
            Scribe_Values.Look(ref WorktypeString, "WorktypeString");
            Scribe_Values.Look(ref XenotypeString, "XenotypeString");
            Scribe_Values.Look(ref TraitString, "TraitString");
            Scribe_Values.Look(ref TraitDegree, "TraitDegree");
            Scribe_Values.Look(ref Priority, "Priority");
            Scribe_Values.Look(ref SkipIfPriorityForThisWorktypeAreadyAssigned, "SkipIfPriorityForThisWorktypeAreadyAssigned", -1);
            Scribe_Values.Look(ref SkipIfAnotherPawnAssigned, "SkipIfAnotherPawnAssigned");
            Scribe_Values.Look(ref AssignToPawnWithFewestWorkPriorities, "AssignToPawnWithFewestWorkPriorities");
            Scribe_Values.Look(ref Gender, "Gender");
            Scribe_Values.Look(ref IsPregnant, "IsPregnant");
            Scribe_Values.Look(ref IsNaturalAlwaysAssign, "IsNaturalAlwaysAssign");
            Scribe_Values.Look(ref IsCapableOfViolence, "IsCapableOfViolence");
            Scribe_Values.Look(ref AllowOverwritingHigherPriority, "AllowOverwritingHigherPriority");
            Scribe_Values.Look(ref LimitNumberOfWorktypes, "LimitNumberOfWorktypes");
            Scribe_Values.Look(ref PassionLevel, "PassionLevel", -1);
            Scribe_Values.Look(ref SkillLevelGreaterThan, "SkillLevelGreaterThan", -1);
            Scribe_Values.Look(ref SkillLevelLessThan, "SkillLevelLessThan", -1);
            Scribe_Values.Look(ref HasHighestSkill, "HasHighestSkill");
            Scribe_Values.Look(ref IsTopXSkill, "IsTopXSkill");
            Scribe_Values.Look(ref IsNthBestPawn, "IsNthBestPawn");
            Scribe_Values.Look(ref IsNthBestSkill, "IsNthBestSkill");
            Scribe_Values.Look(ref HasChildOnMap, "HasChildOnMap");
            Scribe_Values.Look(ref RandomIfMultiple, "RandomIfMultiple");
            Scribe_Values.Look(ref IgnoreIfWorktypeNonexistent, "IgnoreIfWorktypeNonexistent");
            Scribe_Values.Look(ref MoveSpeedGreaterThan, "MoveSpeedGreaterThan", -1f);
            Scribe_Values.Look(ref MoveSpeedLessThan, "MoveSpeedLessThan", -1f);
            Scribe_Collections.Look(ref ActiveConditions, "ActiveConditions", LookMode.Value);

            Scribe_Defs.Look(ref Worktype, "Worktype");
            Scribe_Defs.Look(ref Xenotype, "Xenotype");

            TraitDef requiredTraitDef = RequiredTrait?.Item1;
            int requiredTraitDegree = RequiredTrait?.Item2 ?? -1;
            Scribe_Defs.Look(ref requiredTraitDef, "RequiredTraitDef");
            Scribe_Values.Look(ref requiredTraitDegree, "RequiredTraitDegree", -1);

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                WorktypeString ??= string.Empty;
                XenotypeString ??= string.Empty;
                TraitString ??= string.Empty;
                ActiveConditions ??= new List<string>();

                ResolveWorktypeFromString();
                ResolveXenotypeFromString();
                ResolveTraitRequirement(requiredTraitDef, requiredTraitDegree);
            }
        }

        private void SyncBackingStringsFromDefs()
        {
            WorktypeString = Worktype?.defName ?? WorktypeString ?? "";
            XenotypeString = Xenotype?.defName ?? XenotypeString ?? "";
            TraitString = RequiredTrait?.Item1?.defName ?? TraitString ?? "";
            TraitDegree = RequiredTrait?.Item2 ?? TraitDegree;
        }

        private void ResolveWorktypeFromString()
        {
            if (Worktype == null && !string.IsNullOrEmpty(WorktypeString))
            {
                Worktype = DefDatabase<WorkTypeDef>.GetNamedSilentFail(WorktypeString);

                if (Worktype == null && IgnoreIfWorktypeNonexistent)
                {
                    Log.Warning($"[BWT] Worktype \"{WorktypeString}\" referenced by rule \"{RuleName}\" is missing; rule will be skipped.");
                }
            }
        }

        private void ResolveXenotypeFromString()
        {
            if (Xenotype == null && !string.IsNullOrEmpty(XenotypeString))
            {
                Xenotype = DefDatabase<XenotypeDef>.GetNamedSilentFail(XenotypeString);
            }
        }

        private void ResolveTraitRequirement(TraitDef loadedTrait, int loadedDegree)
        {
            if (loadedTrait != null)
            {
                RequiredTrait = new Tuple<TraitDef, int>(loadedTrait, loadedDegree);
                TraitString = loadedTrait.defName;
                TraitDegree = loadedDegree;
                return;
            }

            if (!string.IsNullOrEmpty(TraitString))
            {
                var resolved = DefDatabase<TraitDef>.GetNamedSilentFail(TraitString);
                if (resolved != null)
                {
                    int degree = loadedDegree >= 0 ? loadedDegree : (TraitDegree ?? 0);
                    RequiredTrait = new Tuple<TraitDef, int>(resolved, degree);
                    TraitDegree = degree;
                }
                else
                {
                    RequiredTrait = null;
                    Log.Warning($"[BWT] Trait \"{TraitString}\" referenced by rule \"{RuleName}\" no longer exists; clearing requirement.");
                }
            }
            else
            {
                RequiredTrait = null;
            }
        }
    }
}
