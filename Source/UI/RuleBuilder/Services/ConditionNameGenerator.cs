using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.ModSupport;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.Services
{
    /// <summary>
    /// Generates human-readable names for rules based on their conditions.
    /// Used when a rule doesn't have a custom name set.
    /// </summary>
    public static class ConditionNameGenerator
    {
        /// <summary>
        /// Generates a descriptive name from the rule's parameters.
        /// </summary>
        public static string Generate(WorkAssignmentParameters p)
        {
            if (p == null)
                return "Empty Rule";

            var parts = new List<string>();

            // Skill-based conditions
            if (p.HasHighestSkill)
                parts.Add("BWT_CondName_BestSkill".Translate());

            if (p.IsTopXSkill > 0)
                parts.Add("BWT_CondName_TopX".Translate(p.IsTopXSkill));

            if (p.SkillLevelGreaterThan > -1)
                parts.Add("BWT_CondName_SkillAbove".Translate(p.SkillLevelGreaterThan));

            if (p.SkillLevelLessThan > -1)
                parts.Add("BWT_CondName_SkillBelow".Translate(p.SkillLevelLessThan));

            // Passion
            if (p.PassionLevel >= 0)
            {
                string passionName = VanillaSkillsExpandedSupport.GetPassionLabel(p.PassionLevel);
                parts.Add("BWT_CondName_Passion".Translate(passionName));
            }

            // Social/biological
            if (p.HasChildOnMap)
                parts.Add("BWT_CondName_HasChild".Translate());

            if (p.IsPregnant)
                parts.Add("BWT_CondName_Pregnant".Translate());

            if (p.Gender != null)
                parts.Add(p.Gender.Value.ToString());

            if (p.IsCapableOfViolence)
                parts.Add("BWT_CondName_CanFight".Translate());

            // Trait
            if (p.RequiredTrait != null)
            {
                string traitName = p.RequiredTrait.Item1?.DataAtDegree(p.RequiredTrait.Item2)?.LabelCap
                    ?? "Trait";
                parts.Add(traitName);
            }

            // Xenotype
#if !v1_3
            if (p.Xenotype != null)
                parts.Add(p.Xenotype.LabelCap);
#endif

            // Assignment behavior
            if (p.IsNaturalAlwaysAssign)
                parts.Add("BWT_CondName_AlwaysAssign".Translate());

            if (p.RandomIfMultiple)
                parts.Add("BWT_CondName_Random".Translate());

            // Build final name
            if (parts.Count == 0)
            {
                return "BWT_CondName_NoConditions".Translate();
            }

            if (parts.Count == 1)
            {
                return parts[0];
            }

            if (parts.Count <= 3)
            {
                return string.Join(" + ", parts);
            }

            // Truncate if too many
            return string.Join(" + ", parts.GetRange(0, 2)) + $" (+{parts.Count - 2})";
        }

        /// <summary>
        /// Gets a short one-line summary of conditions.
        /// </summary>
        public static string GetShortSummary(WorkAssignmentParameters p, int maxLength = 40)
        {
            string full = Generate(p);

            if (full.Length <= maxLength)
                return full;

            return full.Substring(0, maxLength - 3) + "...";
        }
    }
}
