using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.UI.RuleBuilder.Services;
using Better_Work_Tab.UI.RuleBuilder.State;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.Widgets
{
    /// <summary>
    /// Displays a float menu for adding conditions to a rule.
    /// Organizes conditions by category.
    /// </summary>
    public static class ConditionPickerMenu
    {
        /// <summary>
        /// Shows the add condition menu.
        /// </summary>
        public static void Show(WorkAssignmentParameters parameters, RuleBuilderState state)
        {
            var options = new List<FloatMenuOption>();
            var byCategory = ConditionRegistry.GetByCategory();
            bool hasSkill = state?.SelectedWorkType?.relevantSkills?.Count > 0;

            foreach (var entry in byCategory)
            {
                AddCategoryOptions(options, entry.Key, entry.Value, parameters, state, hasSkill);
            }

            if (!options.Any())
            {
                options.Add(new FloatMenuOption("BWT_NoConditionsAvailable".Translate(), null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void AddCategoryOptions(
            List<FloatMenuOption> options,
            string category,
            List<ConditionDefinition> categoryConditions,
            WorkAssignmentParameters parameters,
            RuleBuilderState state,
            bool hasSkill)
        {
            if (!ShouldShowCategory(category, hasSkill))
            {
                return;
            }

            string categoryLabel = categoryConditions.Count == 1 ? null : GetCategoryLabel(category);
            foreach (var def in categoryConditions)
            {
                AddConditionOption(options, def, parameters, state, categoryLabel);
            }
        }

        private static bool ShouldShowCategory(string category, bool hasSkill)
        {
            return hasSkill || category != "BWT_Category_Skill";
        }

        private static string GetCategoryLabel(string category)
        {
            return category.CanTranslate() ? category.Translate() : category;
        }

        private static void AddConditionOption(
            List<FloatMenuOption> options,
            ConditionDefinition def,
            WorkAssignmentParameters parameters,
            RuleBuilderState state,
            string categoryLabel = null)
        {
            if (ConditionRegistry.IsActive(def.Key, parameters))
            {
                return;
            }

            string label = def.Label ?? $"BWT_{def.Key}".Translate();
            if (!string.IsNullOrEmpty(categoryLabel))
            {
                label = $"[{categoryLabel}] {label}";
            }

            options.Add(new FloatMenuOption(label, () =>
            {
                // Set to default value
                ConditionRegistry.SetValue(def.Key, parameters, def.DefaultValue);
                state.NotifyRulesModified();
            }));
        }
    }
}
