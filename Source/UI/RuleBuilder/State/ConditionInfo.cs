using RimWorld;
using System;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.State
{
    /// <summary>
    /// Metadata about a single condition type.
    /// Used for rendering condition pills and the add-condition menu.
    /// </summary>
    public enum ConditionType
    {
        Bool,
        Int,
        IntRange,
        Passion,
        Gender,
        Trait,
#if !v1_3 && !v1_2
        Xenotype
#endif
    }

    /// <summary>
    /// Describes a single condition that can be part of a rule.
    /// Each condition has a key (field name), type, current value, and metadata.
    /// </summary>
    public class ConditionInfo
    {
        /// <summary>Parameter field name (e.g., "HasHighestSkill").</summary>
        public string Key { get; }

        /// <summary>Type of condition for rendering purposes.</summary>
        public ConditionType Type { get; }

        /// <summary>Current value of this condition.</summary>
        public object Value { get; set; }

        /// <summary>Minimum value for int types.</summary>
        public int MinValue { get; set; } = 0;

        /// <summary>Maximum value for int types.</summary>
        public int MaxValue { get; set; } = 20;

        /// <summary>Default value when condition is first added.</summary>
        public object DefaultValue { get; set; }

        /// <summary>Translated label for display.</summary>
        public string Label => TryTranslate($"BWT_{Key}");

        /// <summary>Translated tooltip description.</summary>
        public string Tooltip => TryTranslate($"BWT_{Key}_Desc");

        /// <summary>Short label for condition pill display.</summary>
        public string ShortLabel { get; set; }

        /// <summary>Category for grouping in add menu.</summary>
        public string Category { get; set; } = "General";

        public ConditionInfo(string key, ConditionType type, object value)
        {
            Key = key;
            Type = type;
            Value = value;
        }

        /// <summary>
        /// Creates a formatted display string for this condition's current value.
        /// Used in condition pills and summaries.
        /// </summary>
        public string GetValueDisplay()
        {
            switch (Type)
            {
                case ConditionType.Bool:
                    return (bool)Value ? "✓" : "✗";

                case ConditionType.Int:
                case ConditionType.IntRange:
                    return Value?.ToString() ?? "0";

                case ConditionType.Passion:
                    int passionLevel = (int)Value;
                    return passionLevel switch
                    {
                        0 => "BWT_None".Translate(),
                        1 => "BWT_Minor".Translate(),
                        2 => "BWT_Major".Translate(),
                        _ => passionLevel.ToString()
                    };

                case ConditionType.Gender:
                    var gender = (Gender?)Value;
                    return gender?.ToString() ?? "Any";

                case ConditionType.Trait:
                    var trait = Value as Tuple<TraitDef, int>;
                    if (trait?.Item1 != null)
                    {
                        return trait.Item1.DataAtDegree(trait.Item2)?.LabelCap ?? trait.Item1.defName;
                    }
                    return "None";

#if !v1_3 && !v1_2
                case ConditionType.Xenotype:
                    var xeno = Value as XenotypeDef;
                    return xeno?.LabelCap ?? "None";
#endif

                default:
                    return Value?.ToString() ?? "-";
            }
        }

        private string TryTranslate(string key)
        {
            if (key.CanTranslate())
                return key.Translate();
            return key;
        }
    }
}
