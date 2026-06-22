
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Better_Work_Tab;
using Tuple = System.Tuple;
using Better_Work_Tab.Features.RaisedPriorityMaximum;

namespace Better_Work_Tab.Features.Rules
{
    [AttributeUsage(AttributeTargets.Field)]
    public class RuleParameterAttribute : Attribute { }

    public class WorkAssignmentParameters : IExposable
    {
        [RuleParameter]
        public string RuleName;
        
        [RuleParameter]
#if vAlpha4
        [Unsaved]
#endif
        public WorkTypeDef Worktype;

        [RuleParameter]
        public bool IgnoreIfWorktypeNonexistent;

        [RuleParameter]
        public int Priority;

        [RuleParameter]
        public bool AllowOverwritingHigherPriority;

        [RuleParameter]
        public int PassionLevel = -1;

#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
        [RuleParameter]
#if vAlpha4
        [Unsaved]
#endif
        public XenotypeDef Xenotype;
#endif

        [RuleParameter]
#if vAlpha4
        [Unsaved]
#endif
        public System.Tuple<TraitDef, int> RequiredTrait;

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

        /// <summary>
        /// Serialization fallback when <see cref="Worktype"/> cannot be resolved.
        /// Populated before saving and used to re-resolve defs after load.
        /// </summary>
        public string WorktypeString = "";

        /// <summary>
        /// Serialization fallback for <see cref="Xenotype"/>.
        /// Preserves the xenotype defName even if the def is missing on load.
        /// </summary>
        public string XenotypeString = "";

        /// <summary>
        /// Serialization fallback for <see cref="RequiredTrait"/>.<see cref="Tuple{T1,T2}.Item1"/>.
        /// Keeps the trait defName around so we can warn if it no longer exists.
        /// </summary>
        public string TraitString = "";

        /// <summary>
        /// Serialization fallback for <see cref="RequiredTrait"/>.<see cref="Tuple{T1,T2}.Item2"/>.
        /// Stored alongside <see cref="TraitString"/> so trait requirements can be reconstructed.
        /// </summary>
        public int? TraitDegree = null;

        /// <summary>
        /// Names of condition fields that are explicitly active for this rule.
        /// Values must match <see cref="RuleParameterAttribute"/> field names on <see cref="WorkAssignmentParameters"/>.
        /// </summary>
        public List<string> ActiveConditions = new List<string>();


        public WorkAssignmentParameters(string ruleName = "",
            int priority = 0,
            WorkTypeDef worktype = null,
            int skipIfPriorityForThisWorktypeAreadyAssigned = -1,
            bool skipIfAnotherPawnAssigned = false,
            bool assignToPawnWithFewestWorkPriorities = false,
            Gender? gender = null,
            bool isPregnant = false,
#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
            XenotypeDef xenotype = null,
#endif
            System.Tuple<TraitDef, int> requiredTrait = null,
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
            Priority = WorkPrioritySystem.ClampPriority(priority);

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

#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
            Xenotype = xenotype;
            if(xenotype != null)
            {
                XenotypeString = xenotype.defName;
            }
#endif

            if(requiredTrait != null)
            {
#if vAlpha4
                TraitString = null;
#else
                TraitString = requiredTrait.Item1.defName;
#endif
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

        /// <summary>
        /// Creates a deep copy of this parameter set.
        /// Reference-type fields are cloned to avoid shared state between rules.
        /// </summary>
        public WorkAssignmentParameters Copy()
        {
            return new WorkAssignmentParameters
            {
                RuleName = RuleName,
                Worktype = Worktype,
                IgnoreIfWorktypeNonexistent = IgnoreIfWorktypeNonexistent,
                Priority = WorkPrioritySystem.ClampPriority(Priority),
                AllowOverwritingHigherPriority = AllowOverwritingHigherPriority,
                PassionLevel = PassionLevel,
#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
                Xenotype = Xenotype,
#endif
                RequiredTrait = RequiredTrait != null
                    ? new System.Tuple<TraitDef, int>(RequiredTrait.Item1, RequiredTrait.Item2)
                    : null,
                Gender = Gender,
                HasHighestSkill = HasHighestSkill,
                IsTopXSkill = IsTopXSkill,
                IsNthBestPawn = IsNthBestPawn,
                IsNthBestSkill = IsNthBestSkill,
                RandomIfMultiple = RandomIfMultiple,
                IsNaturalAlwaysAssign = IsNaturalAlwaysAssign,
                AssignToPawnWithFewestWorkPriorities = AssignToPawnWithFewestWorkPriorities,
                SkipIfAnotherPawnAssigned = SkipIfAnotherPawnAssigned,
                SkipIfPriorityForThisWorktypeAreadyAssigned = SkipIfPriorityForThisWorktypeAreadyAssigned,
                LimitNumberOfWorktypes = LimitNumberOfWorktypes,
                SkillLevelGreaterThan = SkillLevelGreaterThan,
                SkillLevelLessThan = SkillLevelLessThan,
                IsCapableOfViolence = IsCapableOfViolence,
                IsPregnant = IsPregnant,
                HasChildOnMap = HasChildOnMap,
                MoveSpeedGreaterThan = MoveSpeedGreaterThan,
                MoveSpeedLessThan = MoveSpeedLessThan,
                WorktypeString = WorktypeString,
                XenotypeString = XenotypeString,
                TraitString = TraitString,
                TraitDegree = TraitDegree,
                ActiveConditions = ActiveConditions?.ToList() ?? new List<string>()
            };
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                SyncBackingStringsFromDefs();
            }

            Better_Work_Tab.ScribeCompat.LookValue(ref RuleName, "RuleName");
            Better_Work_Tab.ScribeCompat.LookValue(ref WorktypeString, "WorktypeString");
            Better_Work_Tab.ScribeCompat.LookValue(ref XenotypeString, "XenotypeString");
            Better_Work_Tab.ScribeCompat.LookValue(ref TraitString, "TraitString");
            Better_Work_Tab.ScribeCompat.LookValue(ref TraitDegree, "TraitDegree");
            Better_Work_Tab.ScribeCompat.LookValue(ref Priority, "Priority");
            Better_Work_Tab.ScribeCompat.LookValue(ref SkipIfPriorityForThisWorktypeAreadyAssigned, "SkipIfPriorityForThisWorktypeAreadyAssigned", -1);
            Better_Work_Tab.ScribeCompat.LookValue(ref SkipIfAnotherPawnAssigned, "SkipIfAnotherPawnAssigned");
            Better_Work_Tab.ScribeCompat.LookValue(ref AssignToPawnWithFewestWorkPriorities, "AssignToPawnWithFewestWorkPriorities");
            Better_Work_Tab.ScribeCompat.LookValue(ref Gender, "Gender");
            Better_Work_Tab.ScribeCompat.LookValue(ref IsPregnant, "IsPregnant");
            Better_Work_Tab.ScribeCompat.LookValue(ref IsNaturalAlwaysAssign, "IsNaturalAlwaysAssign");
            Better_Work_Tab.ScribeCompat.LookValue(ref IsCapableOfViolence, "IsCapableOfViolence");
            Better_Work_Tab.ScribeCompat.LookValue(ref AllowOverwritingHigherPriority, "AllowOverwritingHigherPriority");
            Better_Work_Tab.ScribeCompat.LookValue(ref LimitNumberOfWorktypes, "LimitNumberOfWorktypes");
            Better_Work_Tab.ScribeCompat.LookValue(ref PassionLevel, "PassionLevel", -1);
            Better_Work_Tab.ScribeCompat.LookValue(ref SkillLevelGreaterThan, "SkillLevelGreaterThan", -1);
            Better_Work_Tab.ScribeCompat.LookValue(ref SkillLevelLessThan, "SkillLevelLessThan", -1);
            Better_Work_Tab.ScribeCompat.LookValue(ref HasHighestSkill, "HasHighestSkill");
            Better_Work_Tab.ScribeCompat.LookValue(ref IsTopXSkill, "IsTopXSkill");
            Better_Work_Tab.ScribeCompat.LookValue(ref IsNthBestPawn, "IsNthBestPawn");
            Better_Work_Tab.ScribeCompat.LookValue(ref IsNthBestSkill, "IsNthBestSkill");
            Better_Work_Tab.ScribeCompat.LookValue(ref HasChildOnMap, "HasChildOnMap");
            Better_Work_Tab.ScribeCompat.LookValue(ref RandomIfMultiple, "RandomIfMultiple");
            Better_Work_Tab.ScribeCompat.LookValue(ref IgnoreIfWorktypeNonexistent, "IgnoreIfWorktypeNonexistent");
            Better_Work_Tab.ScribeCompat.LookValue(ref MoveSpeedGreaterThan, "MoveSpeedGreaterThan", -1f);
            Better_Work_Tab.ScribeCompat.LookValue(ref MoveSpeedLessThan, "MoveSpeedLessThan", -1f);
            Better_Work_Tab.ScribeCompat.LookCollection(ref ActiveConditions, "ActiveConditions", LookMode.Value);


            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                WorktypeString ??= string.Empty;
                XenotypeString ??= string.Empty;
                TraitString ??= string.Empty;
                ActiveConditions ??= new List<string>();
                Priority = WorkPrioritySystem.ClampPriority(Priority);
                if (SkipIfPriorityForThisWorktypeAreadyAssigned >= 0)
                {
                    SkipIfPriorityForThisWorktypeAreadyAssigned = WorkPrioritySystem.ClampPriority(SkipIfPriorityForThisWorktypeAreadyAssigned);
                }

                ResolveWorktypeFromString();
                ResolveXenotypeFromString();
                ResolveTraitRequirement(null, -1);  // Resolve from strings only
                ValidateActiveConditions();
            }
        }

        private void SyncBackingStringsFromDefs()
        {
            WorktypeString = Worktype?.defName ?? WorktypeString ?? "";
#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
            XenotypeString = Xenotype?.defName ?? XenotypeString ?? "";
#endif
#if vAlpha4
            TraitString = TraitString ?? "";
#else
            TraitString = RequiredTrait?.Item1?.defName ?? TraitString ?? "";
#endif
            TraitDegree = RequiredTrait?.Item2 ?? TraitDegree;
        }

        /// <summary>
        /// Removes any condition names that no longer correspond to known parameters.
        /// Should be invoked after deserialization to catch stale or renamed fields.
        /// </summary>
        public void ValidateActiveConditions()
        {
            if (ActiveConditions == null || ActiveConditions.Count == 0)
            {
                return;
            }

            var validFieldNames = RuleParameterRegistry.FieldNames;
            var invalid = ActiveConditions
                .Where(name => !validFieldNames.Contains(name))
                .ToList();

            foreach (var orphan in invalid)
            {
                ActiveConditions.Remove(orphan);
                Log.Warning($"[BWT] Rule \"{RuleName}\" references unknown condition \"{orphan}\"; removing.");
            }
        }

        private void ResolveWorktypeFromString()
        {
            if (string.IsNullOrEmpty(WorktypeString))
            {
                Worktype = null;
                return;
            }

            Worktype = DefDatabase<WorkTypeDef>.GetNamedSilentFail(WorktypeString);
            if (Worktype == null)
            {
                // Only warn if we're NOT currently loading a save file
                if (Scribe.mode != LoadSaveMode.LoadingVars &&
                    Scribe.mode != LoadSaveMode.PostLoadInit &&
                    (BetterWorkTabMod.Settings?.enableDebugLogging ?? false))
                {
                    Log.Warning(
                        $"[BWT] Worktype \"{WorktypeString}\" referenced by rule \"{RuleName}\" is missing; rule will be skipped."
                    );
                }
            }
        }

        private void ResolveXenotypeFromString()
        {
#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
            if (Xenotype == null && !string.IsNullOrEmpty(XenotypeString))
            {
                Xenotype = DefDatabase<XenotypeDef>.GetNamedSilentFail(XenotypeString);
            }
#endif
        }

        private void ResolveTraitRequirement(TraitDef loadedTrait, int loadedDegree)
        {
#if vAlpha4
            RequiredTrait = null;
            TraitDegree = loadedDegree >= 0 ? loadedDegree : TraitDegree;
            return;
#else
            if (loadedTrait != null)
            {
                RequiredTrait = new System.Tuple<TraitDef, int>(loadedTrait, loadedDegree);
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
                    RequiredTrait = new System.Tuple<TraitDef, int>(resolved, degree);
                    TraitDegree = degree;
                }
                else
                {
                    RequiredTrait = null;
                    // Don't warn during save load; only when debug is enabled AND we're in normal gameplay
                    if (Scribe.mode == LoadSaveMode.Inactive &&
                        (BetterWorkTabMod.Settings?.enableDebugLogging ?? false))
                    {
                        Log.Warning(
                            $"[BWT] Trait \"{TraitString}\" referenced by rule \"{RuleName}\" no longer exists; clearing requirement."
                        );
                    }
                }
            }
            else
            {
                RequiredTrait = null;
            }
#endif
        }
    }
}
