using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.UI.RuleBuilder.State;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.Services
{
    /// <summary>
    /// Central registry of all available conditions.
    /// Provides metadata for rendering and creating conditions.
    /// </summary>
    public static class ConditionRegistry
    {
        private static List<ConditionDefinition> _definitions;

        /// <summary>
        /// Gets all available condition definitions.
        /// </summary>
        public static List<ConditionDefinition> Definitions
        {
            get
            {
                if (_definitions == null)
                {
                    _definitions = BuildDefinitions();
                }
                return _definitions;
            }
        }

        /// <summary>
        /// Gets conditions grouped by category for the add menu.
        /// </summary>
        public static Dictionary<string, List<ConditionDefinition>> GetByCategory()
        {
            var result = new Dictionary<string, List<ConditionDefinition>>();

            foreach (var def in Definitions)
            {
                if (!result.ContainsKey(def.Category))
                {
                    result[def.Category] = new List<ConditionDefinition>();
                }
                result[def.Category].Add(def);
            }

            return result;
        }

        /// <summary>
        /// Checks if a condition is currently active on the parameters.
        /// </summary>
        public static bool IsActive(string key, WorkAssignmentParameters p)
        {
            // If explicitly activated, keep it active regardless of value (so toggling off doesn't remove it).
            if (p.ActiveConditions != null && p.ActiveConditions.Contains(key))
                return true;

            // Backward compatibility for rules without ActiveConditions set.
            if (p.ActiveConditions == null || p.ActiveConditions.Count == 0)
            {
                return key switch
                {
                    "HasHighestSkill" => p.HasHighestSkill,
                    "IsTopXSkill" => p.IsTopXSkill > 0,
                    "PassionLevel" => p.PassionLevel >= 0,
                    "SkillLevelGreaterThan" => p.SkillLevelGreaterThan >= 0,
                    "SkillLevelLessThan" => p.SkillLevelLessThan >= 0,
                    "IsNaturalAlwaysAssign" => p.IsNaturalAlwaysAssign,
                    "RequiredTrait" => p.RequiredTrait != null,
                    "Gender" => p.Gender != null,
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19
                    "Xenotype" => p.Xenotype != null,
#endif
                    "IsCapableOfViolence" => p.IsCapableOfViolence,
                    "HasChildOnMap" => p.HasChildOnMap,
                    "IsPregnant" => p.IsPregnant,
                    "RandomIfMultiple" => p.RandomIfMultiple,
                    "AllowOverwritingHigherPriority" => p.AllowOverwritingHigherPriority,
                    "SkipIfAnotherPawnAssigned" => p.SkipIfAnotherPawnAssigned,
                    "AssignToPawnWithFewestWorkPriorities" => p.AssignToPawnWithFewestWorkPriorities,
                    "IsNthBestPawn" => p.IsNthBestPawn > 0,
                    "IsNthBestSkill" => p.IsNthBestSkill > 0,
                    "LimitNumberOfWorktypes" => p.LimitNumberOfWorktypes > 0,
                    _ => false
                };
            }

            return false;
        }

        /// <summary>
        /// Gets the current value of a condition from parameters.
        /// </summary>
        public static object GetValue(string key, WorkAssignmentParameters p)
        {
            return key switch
            {
                "HasHighestSkill" => p.HasHighestSkill,
                "IsTopXSkill" => p.IsTopXSkill,
                "PassionLevel" => p.PassionLevel,
                "SkillLevelGreaterThan" => p.SkillLevelGreaterThan,
                "SkillLevelLessThan" => p.SkillLevelLessThan,
                "IsNaturalAlwaysAssign" => p.IsNaturalAlwaysAssign,
                "RequiredTrait" => p.RequiredTrait,
                "Gender" => p.Gender,
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19
                "Xenotype" => p.Xenotype,
#endif
                "IsCapableOfViolence" => p.IsCapableOfViolence,
                "HasChildOnMap" => p.HasChildOnMap,
                "IsPregnant" => p.IsPregnant,
                "RandomIfMultiple" => p.RandomIfMultiple,
                "AllowOverwritingHigherPriority" => p.AllowOverwritingHigherPriority,
                "SkipIfAnotherPawnAssigned" => p.SkipIfAnotherPawnAssigned,
                "AssignToPawnWithFewestWorkPriorities" => p.AssignToPawnWithFewestWorkPriorities,
                "IsNthBestPawn" => p.IsNthBestPawn,
                "IsNthBestSkill" => p.IsNthBestSkill,
                "LimitNumberOfWorktypes" => p.LimitNumberOfWorktypes,
                _ => null
            };
        }

        /// <summary>
        /// Sets a condition value on parameters.
        /// </summary>
        public static void SetValue(string key, WorkAssignmentParameters p, object value)
        {
            EnsureActive(key, p);
            switch (key)
            {
                case "HasHighestSkill":
                    p.HasHighestSkill = (bool)value;
                    break;
                case "IsTopXSkill":
                    p.IsTopXSkill = (int)value;
                    break;
                case "PassionLevel":
                    p.PassionLevel = (int)value;
                    break;
                case "SkillLevelGreaterThan":
                    p.SkillLevelGreaterThan = (int)value;
                    break;
                case "SkillLevelLessThan":
                    p.SkillLevelLessThan = (int)value;
                    break;
                case "IsNaturalAlwaysAssign":
                    p.IsNaturalAlwaysAssign = (bool)value;
                    break;
                case "RequiredTrait":
                    p.RequiredTrait = value as Tuple<TraitDef, int>;
                    p.TraitString = p.RequiredTrait?.Item1?.defName ?? "";
                    p.TraitDegree = p.RequiredTrait?.Item2;
                    break;
                case "Gender":
                    p.Gender = value as Gender?;
                    break;
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19
                case "Xenotype":
                    p.Xenotype = value as XenotypeDef;
                    p.XenotypeString = p.Xenotype?.defName ?? "";
                    break;
#endif
                case "IsCapableOfViolence":
                    p.IsCapableOfViolence = (bool)value;
                    break;
                case "HasChildOnMap":
                    p.HasChildOnMap = (bool)value;
                    break;
                case "IsPregnant":
                    p.IsPregnant = (bool)value;
                    break;
                case "RandomIfMultiple":
                    p.RandomIfMultiple = (bool)value;
                    break;
                case "AllowOverwritingHigherPriority":
                    p.AllowOverwritingHigherPriority = (bool)value;
                    break;
                case "SkipIfAnotherPawnAssigned":
                    p.SkipIfAnotherPawnAssigned = (bool)value;
                    break;
                case "AssignToPawnWithFewestWorkPriorities":
                    p.AssignToPawnWithFewestWorkPriorities = (bool)value;
                    break;
                case "IsNthBestPawn":
                    p.IsNthBestPawn = (int)value;
                    break;
                case "IsNthBestSkill":
                    p.IsNthBestSkill = (int)value;
                    break;
                case "LimitNumberOfWorktypes":
                    p.LimitNumberOfWorktypes = (int)value;
                    break;
            }
        }

        /// <summary>
        /// Clears/resets a condition to its inactive state.
        /// </summary>
        public static void Clear(string key, WorkAssignmentParameters p)
        {
            switch (key)
            {
                case "HasHighestSkill":
                    p.HasHighestSkill = false;
                    break;
                case "IsTopXSkill":
                    p.IsTopXSkill = 0;
                    break;
                case "PassionLevel":
                    p.PassionLevel = -1;
                    break;
                case "SkillLevelGreaterThan":
                    p.SkillLevelGreaterThan = -1;
                    break;
                case "SkillLevelLessThan":
                    p.SkillLevelLessThan = -1;
                    break;
                case "IsNaturalAlwaysAssign":
                    p.IsNaturalAlwaysAssign = false;
                    break;
                case "RequiredTrait":
                    p.RequiredTrait = null;
                    p.TraitString = "";
                    p.TraitDegree = null;
                    break;
                case "Gender":
                    p.Gender = null;
                    break;
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19
                case "Xenotype":
                    p.Xenotype = null;
                    p.XenotypeString = "";
                    break;
#endif
                case "IsCapableOfViolence":
                    p.IsCapableOfViolence = false;
                    break;
                case "HasChildOnMap":
                    p.HasChildOnMap = false;
                    break;
                case "IsPregnant":
                    p.IsPregnant = false;
                    break;
                case "RandomIfMultiple":
                    p.RandomIfMultiple = false;
                    break;
                case "AllowOverwritingHigherPriority":
                    p.AllowOverwritingHigherPriority = false;
                    break;
                case "SkipIfAnotherPawnAssigned":
                    p.SkipIfAnotherPawnAssigned = false;
                    break;
                case "AssignToPawnWithFewestWorkPriorities":
                    p.AssignToPawnWithFewestWorkPriorities = false;
                    break;
                case "IsNthBestPawn":
                    p.IsNthBestPawn = 0;
                    break;
                case "IsNthBestSkill":
                    p.IsNthBestSkill = 0;
                    break;
                case "LimitNumberOfWorktypes":
                    p.LimitNumberOfWorktypes = 0;
                    break;
            }

            if (p.ActiveConditions != null)
            {
                p.ActiveConditions.Remove(key);
            }
        }

        /// <summary>
        /// Gets all active conditions from parameters as ConditionInfo objects.
        /// </summary>
        public static List<ConditionInfo> GetActiveConditions(WorkAssignmentParameters p)
        {
            var result = new List<ConditionInfo>();
            var activeKeys = ResolveActiveKeys(p);

            foreach (var key in activeKeys)
            {
                var def = Definitions.FirstOrDefault(d => d.Key == key);
                if (def == null)
                {
                    continue;
                }

                result.Add(new ConditionInfo(def.Key, def.Type, GetValue(def.Key, p))
                {
                    MinValue = def.MinValue,
                    MaxValue = def.MaxValue,
                    DefaultValue = def.DefaultValue,
                    Category = def.Category,
                    ShortLabel = def.ShortLabel
                });
            }

            p.ActiveConditions = activeKeys;
            return result;
        }

        private static void EnsureActive(string key, WorkAssignmentParameters p)
        {
            p.ActiveConditions ??= new List<string>();
            if (!p.ActiveConditions.Contains(key))
            {
                p.ActiveConditions.Add(key);
            }
        }

        private static Tuple<TraitDef, int> GetDefaultTrait()
        {
            var trait = DefDatabase<TraitDef>.AllDefs
                .OrderBy(t => t.defName)
                .FirstOrDefault();

            if (trait == null)
                return null;

            int degree = 0;
            if (trait.degreeDatas != null && trait.degreeDatas.Count > 0)
            {
                degree = trait.degreeDatas[0].degree;
            }

            return new Tuple<TraitDef, int>(trait, degree);
        }

        private static List<ConditionDefinition> BuildDefinitions()
        {
            var definitions = new List<ConditionDefinition>
            {
                // Skill category
                new ConditionDefinition
                {
                    Key = "HasHighestSkill",
                    Type = ConditionType.Bool,
                    Category = "BWT_Category_Skill",
                    DefaultValue = true,
                    ShortLabel = "Best"
                },
                new ConditionDefinition
                {
                    Key = "IsTopXSkill",
                    Type = ConditionType.Int,
                    Category = "BWT_Category_Skill",
                    MinValue = 1,
                    MaxValue = 12,
                    DefaultValue = 6,
                    ShortLabel = "Top"
                },
                new ConditionDefinition
                {
                    Key = "PassionLevel",
                    Type = ConditionType.Passion,
                    Category = "BWT_Category_Skill",
                    MinValue = 0,
                    MaxValue = 2,
                    DefaultValue = 1,
                    ShortLabel = "Passion"
                },
                new ConditionDefinition
                {
                    Key = "SkillLevelGreaterThan",
                    Type = ConditionType.Int,
                    Category = "BWT_Category_Skill",
                    MinValue = 0,
                    MaxValue = 20,
                    DefaultValue = 5,
                    ShortLabel = "Skill >"
                },
                new ConditionDefinition
                {
                    Key = "SkillLevelLessThan",
                    Type = ConditionType.Int,
                    Category = "BWT_Category_Skill",
                    MinValue = 1,
                    MaxValue = 20,
                    DefaultValue = 10,
                    ShortLabel = "Skill <"
                },
                new ConditionDefinition
                {
                    Key = "IsNthBestPawn",
                    Type = ConditionType.Int,
                    Category = "BWT_Category_Skill",
                    MinValue = 1,
                    MaxValue = 20,
                    DefaultValue = 1,
                    ShortLabel = "Rank"
                },
                new ConditionDefinition
                {
                    Key = "IsNthBestSkill",
                    Type = ConditionType.Int,
                    Category = "BWT_Category_Skill",
                    MinValue = 1,
                    MaxValue = 12,
                    DefaultValue = 1,
                    ShortLabel = "Pawn Rank"
                },

                // Biological category
                new ConditionDefinition
                {
                    Key = "Gender",
                    Type = ConditionType.Gender,
                    Category = "BWT_Category_Biological",
                    DefaultValue = Gender.Female,
                    ShortLabel = "Gender"
                },
                new ConditionDefinition
                {
                    Key = "IsPregnant",
                    Type = ConditionType.Bool,
                    Category = "BWT_Category_Biological",
                    DefaultValue = true,
                    ShortLabel = "Pregnant"
                },
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19
                new ConditionDefinition
                {
                    Key = "Xenotype",
                    Type = ConditionType.Xenotype,
                    Category = "BWT_Category_Biological",
                    DefaultValue = null,
                    ShortLabel = "Xeno"
                },
#endif

                // Social category
                new ConditionDefinition
                {
                    Key = "HasChildOnMap",
                    Type = ConditionType.Bool,
                    Category = "BWT_Category_Social",
                    DefaultValue = true,
                    ShortLabel = "Parent"
                },
                new ConditionDefinition
                {
                    Key = "RequiredTrait",
                    Type = ConditionType.Trait,
                    Category = "BWT_Category_Social",
                    DefaultValue = GetDefaultTrait(),
                    ShortLabel = "Trait"
                },

                // Capability category
                new ConditionDefinition
                {
                    Key = "IsCapableOfViolence",
                    Type = ConditionType.Bool,
                    Category = "BWT_Category_Capability",
                    DefaultValue = true,
                    ShortLabel = "Fighter"
                },
                new ConditionDefinition
                {
                    Key = "IsNaturalAlwaysAssign",
                    Type = ConditionType.Bool,
                    Category = "BWT_Category_Capability",
                    DefaultValue = true,
                    ShortLabel = "Always"
                },

                // Behavior category
                new ConditionDefinition
                {
                    Key = "RandomIfMultiple",
                    Type = ConditionType.Bool,
                    Category = "BWT_Category_Behavior",
                    DefaultValue = true,
                    ShortLabel = "Random"
                },
                new ConditionDefinition
                {
                    Key = "AllowOverwritingHigherPriority",
                    Type = ConditionType.Bool,
                    Category = "BWT_Category_Behavior",
                    DefaultValue = true,
                    ShortLabel = "Overwrite"
                },
                new ConditionDefinition
                {
                    Key = "SkipIfAnotherPawnAssigned",
                    Type = ConditionType.Bool,
                    Category = "BWT_Category_Behavior",
                    DefaultValue = true,
                    ShortLabel = "Skip if assigned"
                },
                new ConditionDefinition
                {
                    Key = "AssignToPawnWithFewestWorkPriorities",
                    Type = ConditionType.Bool,
                    Category = "BWT_Category_Behavior",
                    DefaultValue = true,
                    ShortLabel = "Least busy"
                },
                new ConditionDefinition
                {
                    Key = "LimitNumberOfWorktypes",
                    Type = ConditionType.Int,
                    Category = "BWT_Category_Behavior",
                    MinValue = 1,
                    MaxValue = 20,
                    DefaultValue = 5,
                    ShortLabel = "Max jobs"
                }
            };

            var validFieldNames = RuleParameterRegistry.FieldNames;
            var filtered = definitions.Where(def => validFieldNames.Contains(def.Key)).ToList();

            var missing = definitions
                .Where(def => !validFieldNames.Contains(def.Key))
                .Select(def => def.Key)
                .ToList();

            if (missing.Count > 0)
            {
                var joined = string.Join(", ", missing.ToArray());
                Log.Warning($"[BWT] Ignoring unknown condition definitions: {joined}");
            }

            return filtered;
        }

        private static List<string> ResolveActiveKeys(WorkAssignmentParameters p)
        {
            var orderedActive = p.ActiveConditions != null && p.ActiveConditions.Count > 0
                ? p.ActiveConditions.Where(key => IsActive(key, p)).ToList()
                : Definitions.Where(def => IsActive(def.Key, p)).Select(def => def.Key).ToList();

            // Preserve only known keys; add any missing active ones in definition order.
            var known = new HashSet<string>(Definitions.Select(d => d.Key));
            orderedActive = orderedActive.Where(known.Contains).ToList();

            foreach (var def in Definitions)
            {
                if (IsActive(def.Key, p) && !orderedActive.Contains(def.Key))
                {
                    orderedActive.Add(def.Key);
                }
            }

            return orderedActive;
        }
    }

    /// <summary>
    /// Definition of a condition type for the registry.
    /// </summary>
    public class ConditionDefinition
    {
        public string Key { get; set; }
        public ConditionType Type { get; set; }
        public string Category { get; set; }
        public int MinValue { get; set; } = 0;
        public int MaxValue { get; set; } = 20;
        public object DefaultValue { get; set; }
        public string ShortLabel { get; set; }

        public string Label => TryTranslate($"BWT_{Key}");
        public string Tooltip => TryTranslate($"BWT_{Key}_Desc");

        private string TryTranslate(string key)
        {
            if (key.CanTranslate())
                return key.Translate();
            return key;
        }
    }
}
