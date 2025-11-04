
using RimWorld;
using System;
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
        public int Priority;
        [RuleParameter]
        public int SkipIfPriorityForThisWorktypeAreadyAssigned = -1;
        [RuleParameter]
        public bool SkipIfAnotherPawnAssigned;
        [RuleParameter]
        public bool AssignToPawnWithFewestWorkPriorities;
        [RuleParameter]
        public Gender? Gender;
        [RuleParameter]
        public bool IsPregnant;
        [RuleParameter]
        public XenotypeDef Xenotype;
        [RuleParameter]
        public Tuple<TraitDef, int> RequiredTrait;
        [RuleParameter]
        public bool IsNaturalAlwaysAssign;
        [RuleParameter]
        public bool IsCapableOfViolence;
        [RuleParameter]
        public bool AllowOverwritingHigherPriority;
        [RuleParameter]
        public int LimitNumberOfWorktypes;
        [RuleParameter]
        public int PassionLevel = -1;
        [RuleParameter]
        public int SkillLevelGreaterThan = -1;
        [RuleParameter]
        public int SkillLevelLessThan = -1;
        [RuleParameter]
        public bool HasHighestSkill;
        [RuleParameter]
        public int IsTopXSkill;
        [RuleParameter]
        public int IsNthBestPawn;
        [RuleParameter]
        public int IsNthBestSkill;
        [RuleParameter]
        public bool HasChildOnMap;
        [RuleParameter]
        public bool RandomIfMultiple;
        [RuleParameter]
        public string WorktypeNamedIgnoreIfNonexistant;
        [RuleParameter]
        public float MoveSpeedGreaterThan = -1;
        [RuleParameter]
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
