using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.ModSupport;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal sealed class RuleBuilder2ConditionDefinition
    {
        public RuleBuilder2ConditionKind Kind;
        public string CategoryKey;
        public string LabelKey;
        public string TooltipKey;
        public int DefaultInt;
        public float DefaultFloat;
        public bool Advanced;

        public RuleBuilder2Condition Create()
        {
            return new RuleBuilder2Condition
            {
                Kind = Kind,
                IntValue = DefaultInt,
                FloatValue = DefaultFloat,
                DisplayText = Label
            };
        }

        public string Category => TranslateOrFallback(CategoryKey, CategoryKey);
        public string Label => TranslateOrFallback(LabelKey, LabelKey);
        public string Tooltip => TranslateOrFallback(TooltipKey, TooltipKey);

        private static string TranslateOrFallback(string key, string fallback)
        {
            return !string.IsNullOrEmpty(key) && key.CanTranslate()
                ? key.Translate().ToString()
                : fallback;
        }
    }

    internal static class RuleBuilder2ConditionCatalog
    {
        private static List<RuleBuilder2ConditionDefinition> _definitions;

        internal static List<RuleBuilder2ConditionDefinition> Definitions
        {
            get
            {
                _definitions ??= BuildDefinitions();
                return _definitions;
            }
        }

        internal static IEnumerable<IGrouping<string, RuleBuilder2ConditionDefinition>> Grouped(bool showAdvanced, string search)
        {
            string needle = (search ?? string.Empty).Trim().ToLowerInvariant();
            return Definitions
                .Where(def => showAdvanced || !def.Advanced)
                .Where(def => string.IsNullOrEmpty(needle) ||
                              def.Label.ToLowerInvariant().Contains(needle) ||
                              def.Category.ToLowerInvariant().Contains(needle))
                .GroupBy(def => def.Category)
                .OrderBy(group => group.Key);
        }

        internal static string GetConditionText(RuleBuilder2Condition condition, WorkTypeDef targetWorkType = null)
        {
            if (condition == null)
            {
                return Tr("BWT_RuleBuilder2_NoCondition");
            }

            switch (condition.Kind)
            {
                case RuleBuilder2ConditionKind.SkillMinimum:
                    return Tr("BWT_RuleBuilder2_ConditionText_SkillMinimum", GetSkillLabel(condition, targetWorkType), condition.IntValue);
                case RuleBuilder2ConditionKind.SkillMaximum:
                    return Tr("BWT_RuleBuilder2_ConditionText_SkillMaximum", GetSkillLabel(condition, targetWorkType), condition.IntValue);
                case RuleBuilder2ConditionKind.PassionAtLeast:
                    return Tr("BWT_RuleBuilder2_ConditionText_Passion", VanillaSkillsExpandedSupport.GetPassionLabel(condition.IntValue), GetSkillLabel(condition, targetWorkType));
                case RuleBuilder2ConditionKind.Trait:
                    return Tr("BWT_RuleBuilder2_ConditionText_Trait", ResolveLabel<TraitDef>(condition.DefName, Tr("BWT_RuleBuilder2_TraitFallback")));
                case RuleBuilder2ConditionKind.CapacityMinimum:
                    return Tr("BWT_RuleBuilder2_ConditionText_CapacityMinimum", ResolveLabel<PawnCapacityDef>(condition.DefName, Tr("BWT_RuleBuilder2_CapacityFallback")), condition.FloatValue.ToString("0.##"));
                case RuleBuilder2ConditionKind.Xenotype:
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19 && !v0_18 && !v0_17 && !v0_16 && !v0_15 && !v0_14 && !v0_13 && !vAlpha4
                    return Tr("BWT_RuleBuilder2_ConditionText_Xenotype", ResolveLabel<XenotypeDef>(condition.DefName, Tr("BWT_RuleBuilder2_XenotypeFallback")));
#else
                    return Tr("BWT_RuleBuilder2_ConditionText_Xenotype", string.IsNullOrEmpty(condition.DefName) ? Tr("BWT_RuleBuilder2_XenotypeFallback") : condition.DefName);
#endif
                case RuleBuilder2ConditionKind.Gender:
                    return Tr("BWT_RuleBuilder2_ConditionText_Gender", condition.TextValue);
                case RuleBuilder2ConditionKind.ExistingPriorityAtLeast:
                    return Tr("BWT_RuleBuilder2_ConditionText_ExistingPriorityAtLeast", condition.IntValue);
                case RuleBuilder2ConditionKind.ExistingPriorityEquals:
                    return Tr("BWT_RuleBuilder2_ConditionText_ExistingPriorityEquals", condition.IntValue);
                case RuleBuilder2ConditionKind.CurrentAssignedWork:
                    return condition.BoolValue ? Tr("BWT_RuleBuilder2_ConditionText_Assigned") : Tr("BWT_RuleBuilder2_ConditionText_NotAssigned");
                case RuleBuilder2ConditionKind.HighestSkillAmongColonists:
                    return Tr("BWT_RuleBuilder2_ConditionText_HighestSkill");
                case RuleBuilder2ConditionKind.TopWorkTypesBySkill:
                    return Tr("BWT_RuleBuilder2_ConditionText_TopWorkTypes", condition.IntValue);
                case RuleBuilder2ConditionKind.NaturalAlwaysActiveWork:
                    return Tr("BWT_RuleBuilder2_ConditionText_NaturalAlways");
                case RuleBuilder2ConditionKind.ParentHasChildOnMap:
                    return Tr("BWT_RuleBuilder2_ConditionText_HasChild");
                default:
                    return string.IsNullOrEmpty(condition.DisplayText) ? Tr("BWT_RuleBuilder2_ReviewCondition") : condition.DisplayText;
            }
        }

        internal static List<SkillDef> GetSkillOptions(WorkTypeDef workType)
        {
            var skills = new List<SkillDef>();
            if (workType?.relevantSkills != null)
            {
                skills.AddRange(workType.relevantSkills.Where(skill => skill != null));
            }

            foreach (var skill in DefDatabase<SkillDef>.AllDefsListForReading.OrderBy(def => def.label))
            {
                if (!skills.Contains(skill))
                {
                    skills.Add(skill);
                }
            }

            return skills;
        }

        private static string GetSkillLabel(RuleBuilder2Condition condition, WorkTypeDef targetWorkType)
        {
            SkillDef skill = string.IsNullOrEmpty(condition.DefName)
                ? targetWorkType?.relevantSkills?.FirstOrDefault()
                : DefDatabase<SkillDef>.GetNamedSilentFail(condition.DefName);

            if (skill != null)
            {
                return skill.LabelCap.ToString();
            }

            return targetWorkType?.LabelCap.ToString() ?? Tr("BWT_RuleBuilder2_RelevantFallback");
        }

        private static string ResolveLabel<T>(string defName, string fallback) where T : Def, new()
        {
            if (string.IsNullOrEmpty(defName))
            {
                return fallback;
            }

            T def = DefDatabase<T>.GetNamedSilentFail(defName);
            return def?.LabelCap.ToString() ?? defName;
        }

        private static string Tr(string key, params object[] args)
        {
            if (!key.CanTranslate())
            {
                return key;
            }

            string translated = key.Translate().ToString();
            return args != null && args.Length > 0
                ? string.Format(translated, args)
                : translated;
        }

        private static List<RuleBuilder2ConditionDefinition> BuildDefinitions()
        {
            return new List<RuleBuilder2ConditionDefinition>
            {
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.SkillMinimum,
                    CategoryKey = "BWT_RuleBuilder2_Category_Skills",
                    LabelKey = "BWT_RuleBuilder2_Condition_SkillMinimum",
                    TooltipKey = "BWT_RuleBuilder2_Condition_SkillMinimum_Tooltip",
                    DefaultInt = 10
                },
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.SkillMaximum,
                    CategoryKey = "BWT_RuleBuilder2_Category_Skills",
                    LabelKey = "BWT_RuleBuilder2_Condition_SkillMaximum",
                    TooltipKey = "BWT_RuleBuilder2_Condition_SkillMaximum_Tooltip",
                    DefaultInt = 5,
                    Advanced = true
                },
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.PassionAtLeast,
                    CategoryKey = "BWT_RuleBuilder2_Category_Passions",
                    LabelKey = "BWT_RuleBuilder2_Condition_Passion",
                    TooltipKey = "BWT_RuleBuilder2_Condition_Passion_Tooltip",
                    DefaultInt = 1
                },
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.Trait,
                    CategoryKey = "BWT_RuleBuilder2_Category_Traits",
                    LabelKey = "BWT_RuleBuilder2_Condition_Trait",
                    TooltipKey = "BWT_RuleBuilder2_Condition_Trait_Tooltip"
                },
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.CapacityMinimum,
                    CategoryKey = "BWT_RuleBuilder2_Category_Health",
                    LabelKey = "BWT_RuleBuilder2_Condition_Capacity",
                    TooltipKey = "BWT_RuleBuilder2_Condition_Capacity_Tooltip",
                    DefaultFloat = 0.8f,
                    Advanced = true
                },
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19 && !v0_18 && !v0_17 && !v0_16 && !v0_15 && !v0_14 && !v0_13 && !vAlpha4
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.Xenotype,
                    CategoryKey = "BWT_RuleBuilder2_Category_Xenotype",
                    LabelKey = "BWT_RuleBuilder2_Condition_Xenotype",
                    TooltipKey = "BWT_RuleBuilder2_Condition_Xenotype_Tooltip"
                },
#endif
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.Gender,
                    CategoryKey = "BWT_RuleBuilder2_Category_AgeGender",
                    LabelKey = "BWT_RuleBuilder2_Condition_Gender",
                    TooltipKey = "BWT_RuleBuilder2_Condition_Gender_Tooltip"
                },
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.ExistingPriorityAtLeast,
                    CategoryKey = "BWT_RuleBuilder2_Category_ExistingPriority",
                    LabelKey = "BWT_RuleBuilder2_Condition_ExistingPriorityAtLeast",
                    TooltipKey = "BWT_RuleBuilder2_Condition_ExistingPriorityAtLeast_Tooltip",
                    DefaultInt = 1
                },
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.CurrentAssignedWork,
                    CategoryKey = "BWT_RuleBuilder2_Category_AssignedWork",
                    LabelKey = "BWT_RuleBuilder2_Condition_CurrentAssigned",
                    TooltipKey = "BWT_RuleBuilder2_Condition_CurrentAssigned_Tooltip",
                    Advanced = true
                },
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.HighestSkillAmongColonists,
                    CategoryKey = "BWT_RuleBuilder2_Category_Skills",
                    LabelKey = "BWT_RuleBuilder2_Condition_HighestSkill",
                    TooltipKey = "BWT_RuleBuilder2_Condition_HighestSkill_Tooltip"
                },
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.TopWorkTypesBySkill,
                    CategoryKey = "BWT_RuleBuilder2_Category_Skills",
                    LabelKey = "BWT_RuleBuilder2_Condition_TopWorkTypes",
                    TooltipKey = "BWT_RuleBuilder2_Condition_TopWorkTypes_Tooltip",
                    DefaultInt = 6
                },
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.NaturalAlwaysActiveWork,
                    CategoryKey = "BWT_RuleBuilder2_Category_AssignedWork",
                    LabelKey = "BWT_RuleBuilder2_Condition_NaturalAlways",
                    TooltipKey = "BWT_RuleBuilder2_Condition_NaturalAlways_Tooltip"
                },
                new RuleBuilder2ConditionDefinition
                {
                    Kind = RuleBuilder2ConditionKind.ParentHasChildOnMap,
                    CategoryKey = "BWT_RuleBuilder2_Category_AgeGender",
                    LabelKey = "BWT_RuleBuilder2_Condition_HasChild",
                    TooltipKey = "BWT_RuleBuilder2_Condition_HasChild_Tooltip"
                }
            };
        }
    }
}
