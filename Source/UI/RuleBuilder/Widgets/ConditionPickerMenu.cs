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

            if (categoryConditions.Count == 1)
            {
                AddSingleConditionOption(options, categoryConditions[0], parameters, state);
                return;
            }

            AddCategorySubmenuOption(options, category, categoryConditions, parameters, state);
        }

        private static bool ShouldShowCategory(string category, bool hasSkill)
        {
            return hasSkill || category != "BWT_Category_Skill";
        }

        private static void AddSingleConditionOption(
            List<FloatMenuOption> options,
            ConditionDefinition def,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            if (!ConditionRegistry.IsActive(def.Key, parameters))
            {
                AddConditionOption(options, def, parameters, state);
            }
        }

        private static void AddCategorySubmenuOption(
            List<FloatMenuOption> options,
            string category,
            List<ConditionDefinition> categoryConditions,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            var submenu = new List<FloatMenuOption>();
            foreach (var def in categoryConditions)
            {
                if (!ConditionRegistry.IsActive(def.Key, parameters))
                {
                    AddConditionOption(submenu, def, parameters, state);
                }
            }

            if (submenu.Any())
            {
                options.Add(new FloatMenuOption(
                    GetCategoryLabel(category),
                    () => Find.WindowStack.Add(new FloatMenu(submenu))));
            }
        }

        private static string GetCategoryLabel(string category)
        {
            return category.CanTranslate() ? category.Translate() : category;
        }

        private static void AddConditionOption(
            List<FloatMenuOption> options,
            ConditionDefinition def,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            string label = def.Label ?? $"BWT_{def.Key}".Translate();

            options.Add(new FloatMenuOption(label, () =>
            {
                // Set to default value
                ConditionRegistry.SetValue(def.Key, parameters, def.DefaultValue);
                state.NotifyRulesModified();
            }));
        }
    }
}
