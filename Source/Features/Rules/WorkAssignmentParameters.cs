
using RimWorld;
using System;
using Verse;

namespace Better_Work_Tab.Features.Rules
{
    public class WorkAssignmentParameters : IExposable
    {
        public string RuleName;
        public WorkTypeDef Worktype;
        public int Priority;
        public int SkipIfPriorityForThisWorktypeAreadyAssigned = -1;
        public bool SkipIfAnotherPawnAssigned;
        public bool AssignToPawnWithFewestWorkPriorities;
        public Gender? Gender;
        public bool IsPregnant;
        public XenotypeDef Xenotype;
        public Tuple<TraitDef, int> RequiredTrait;
        public bool IsNaturalAlwaysAssign;
        public bool IsCapableOfViolence;
        public bool AllowOverwritingHigherPriority;
        public int LimitNumberOfWorktypes;
        public int PassionLevel = -1;
        public int SkillLevelGreaterThan = -1;
        public int SkillLevelLessThan = -1;
        public bool HasHighestSkill;
        public int IsTopXSkill;
        public int IsNthBestPawn;
        public int IsNthBestSkill;
        public bool HasChildOnMap;
        public bool RandomIfMultiple;
        public string WorktypeNamedIgnoreIfNonexistant;
        public float MoveSpeedGreaterThan = -1;
        public float MoveSpeedLessThan = -1;

        public WorkAssignmentParameters() { }

        public WorkAssignmentParameters(string ruleName = "", int priority = 0, WorkTypeDef worktype = null, int skipIfPriorityForThisWorktypeAreadyAssigned = -1, bool skipIfAnotherPawnAssigned = false, bool assignToPawnWithFewestWorkPriorities = false, Gender? gender = null, bool isPregnant = false, XenotypeDef xenotype = null, Tuple<TraitDef, int> requiredTrait = null, bool isNaturalAlwaysAssign = false, bool isCapableOfViolence = false, bool allowOverwritingHigherPriority = false, int limitNumberOfWorktypes = 0, int passionLevel = -1, int skillLevelGreaterThan = -1, int skillLevelLessThan = -1, bool hasHighestSkill = false, int isTopXSkill = 0, int isNthBestPawn = 0, int isNthBestSkill = 0, bool hasChildOnMap = false, bool randomIfMultiple = false, string worktypeNamedIgnoreIfNonexistant = "", float moveSpeedGreaterThan = -1, float moveSpeedLessThan = -1)
        {
            RuleName = ruleName;
            Priority = priority;
            Worktype = worktype;
            SkipIfPriorityForThisWorktypeAreadyAssigned = skipIfPriorityForThisWorktypeAreadyAssigned;
            SkipIfAnotherPawnAssigned = skipIfAnotherPawnAssigned;
            AssignToPawnWithFewestWorkPriorities = assignToPawnWithFewestWorkPriorities;
            Gender = gender;
            IsPregnant = isPregnant;
            Xenotype = xenotype;
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
            WorktypeNamedIgnoreIfNonexistant = worktypeNamedIgnoreIfNonexistant;
            MoveSpeedGreaterThan = moveSpeedGreaterThan;
            MoveSpeedLessThan = moveSpeedLessThan;
        }

        public WorkAssignmentParameters Copy()
        {
            return (WorkAssignmentParameters)this.MemberwiseClone();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref RuleName, "RuleName");
            Scribe_Defs.Look(ref Worktype, "Worktype");
            Scribe_Values.Look(ref Priority, "Priority");
            Scribe_Values.Look(ref SkipIfPriorityForThisWorktypeAreadyAssigned, "SkipIfPriorityForThisWorktypeAreadyAssigned", -1);
            Scribe_Values.Look(ref SkipIfAnotherPawnAssigned, "SkipIfAnotherPawnAssigned");
            Scribe_Values.Look(ref AssignToPawnWithFewestWorkPriorities, "AssignToPawnWithFewestWorkPriorities");
            Scribe_Values.Look(ref Gender, "Gender");
            Scribe_Values.Look(ref IsPregnant, "IsPregnant");
            Scribe_Defs.Look(ref Xenotype, "Xenotype");
            Scribe_Values.Look(ref RequiredTrait, "RequiredTrait");
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
            Scribe_Values.Look(ref WorktypeNamedIgnoreIfNonexistant, "WorktypeNamedIgnoreIfNonexistant");
            Scribe_Values.Look(ref MoveSpeedGreaterThan, "MoveSpeedGreaterThan", -1f);
            Scribe_Values.Look(ref MoveSpeedLessThan, "MoveSpeedLessThan", -1f);
        }
    }
}
