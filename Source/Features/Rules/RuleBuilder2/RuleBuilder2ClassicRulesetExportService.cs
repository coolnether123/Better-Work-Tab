using System.Collections.Generic;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal sealed class RuleBuilder2ClassicRulesetExportResult
    {
        internal bool Exported;
        internal int RuleCount;
        internal List<string> Warnings = new List<string>();
    }

    internal sealed class RuleBuilder2ClassicRulesetExportService
    {
        internal RuleBuilder2ClassicRulesetExportResult Export(
            RuleBuilder2Ruleset source,
            BetterWorkTabSettings settings)
        {
            var result = new RuleBuilder2ClassicRulesetExportResult();
            if (settings == null)
            {
                result.Warnings.Add("Better Work Tab settings are unavailable.");
                return result;
            }

            var classicRuleset = RuleBuilder2ClassicRulesetTranslator.TryCreateClassicRuleset(source, out List<string> warnings);
            result.Warnings = warnings ?? new List<string>();
            if (classicRuleset == null)
            {
                return result;
            }

            settings.SavedRulesets ??= new List<WorkAssignmentRuleset>();
            int existingIndex = settings.SavedRulesets.FindIndex(existing =>
                existing != null &&
                !existing.IsDefault &&
                existing.Name == classicRuleset.Name);

            if (existingIndex >= 0)
            {
                settings.SavedRulesets[existingIndex] = classicRuleset;
            }
            else
            {
                settings.SavedRulesets.Add(classicRuleset);
            }

            settings.Write();
            result.Exported = true;
            result.RuleCount = classicRuleset.Rules?.Count ?? 0;
            return result;
        }
    }
}
