using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.UI.RuleBuilder.Services;
using Better_Work_Tab.UI.RuleBuilder.State;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;

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

            // Group by category
            foreach (var category in byCategory.Keys)
            {
                if (!hasSkill && category == "BWT_Category_Skill")
                {
                    continue;
                }

                var categoryConditions = byCategory[category];

                if (categoryConditions.Count == 1)
                {
                    // Single item, no submenu needed
                    var def = categoryConditions[0];
                    if (!ConditionRegistry.IsActive(def.Key, parameters))
                    {
                        AddConditionOption(options, def, parameters, state);
                    }
                }
                else
                {
                    // Multiple items, create submenu
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
                        string categoryLabel = category.CanTranslate()
                            ? category.Translate()
                            : category;

                        options.Add(new FloatMenuOption(
                            categoryLabel,
                            () => Find.WindowStack.Add(new FloatMenu(submenu))));
                    }
                }
            }

            if (!options.Any())
            {
                options.Add(new FloatMenuOption("BWT_NoConditionsAvailable".Translate(), null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
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
