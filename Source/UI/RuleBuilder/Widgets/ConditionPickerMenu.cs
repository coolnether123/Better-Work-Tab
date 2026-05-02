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

            // RimWorld's FloatMenu sorts null-action options to the bottom, so we cannot
            // use disabled headers as visual separators. Instead present conditions as a
            // flat list sorted by category — grouping is implicit from the ordering.
            foreach (var category in byCategory.Keys)
            {
                if (!hasSkill && category == "BWT_Category_Skill")
                    continue;

                string categoryLabel = category.CanTranslate() ? category.Translate() : category;

                foreach (var def in byCategory[category])
                {
                    if (ConditionRegistry.IsActive(def.Key, parameters))
                        continue;

                    string label = def.Label ?? $"BWT_{def.Key}".Translate();
                    string fullLabel = $"[{categoryLabel}] {label}";
                    var captured = def;

                    options.Add(new FloatMenuOption(fullLabel, () =>
                    {
                        ConditionRegistry.SetValue(captured.Key, parameters, captured.DefaultValue);
                        state.NotifyRulesModified();
                    }));
                }
            }

            if (!options.Any())
                options.Add(new FloatMenuOption("BWT_NoConditionsAvailable".Translate(), null));

            Find.WindowStack.Add(new FloatMenu(options));
        }

    }
}
