using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.Rules;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    /// <summary>
    /// Owns Rule Builder 2 ruleset persistence semantics.
    /// 
    /// BetterWorkTabSettings still contains the serialized fields because RimWorld
    /// saves mod settings from that object. This store owns the behavior around
    /// those fields: removing null entries, normalizing cards after load, choosing
    /// the current ruleset, and replacing saved rulesets by StableId.
    /// </summary>
    internal static class RuleBuilder2RulesetStore
    {
        internal static List<RuleBuilder2Ruleset> Saved(BetterWorkTabSettings settings)
        {
            Ensure(settings);
            return settings?.SavedRuleBuilder2Rulesets ?? new List<RuleBuilder2Ruleset>();
        }

        internal static RuleBuilder2Ruleset Current(BetterWorkTabSettings settings)
        {
            Ensure(settings);
            return settings?.CurrentRuleBuilder2Ruleset;
        }

        internal static void SaveOrReplace(
            BetterWorkTabSettings settings,
            RuleBuilder2Ruleset ruleset,
            bool makeCurrent = true,
            bool writeSettings = true)
        {
            if (settings == null || ruleset == null)
            {
                return;
            }

            ruleset.EnsureOpenBlankCard();
            Ensure(settings);

            int index = settings.SavedRuleBuilder2Rulesets.FindIndex(existing => existing?.StableId == ruleset.StableId);
            if (index >= 0)
            {
                settings.SavedRuleBuilder2Rulesets[index] = ruleset;
            }
            else
            {
                settings.SavedRuleBuilder2Rulesets.Add(ruleset);
            }

            if (makeCurrent)
            {
                SetCurrent(settings, ruleset, writeSettings: false);
            }

            if (writeSettings)
            {
                settings.Write();
            }
        }

        internal static void SetCurrent(BetterWorkTabSettings settings, RuleBuilder2Ruleset ruleset, bool writeSettings = true)
        {
            if (settings == null)
            {
                return;
            }

            settings.CurrentRuleBuilder2Ruleset = ruleset;
            settings.currentRuleBuilder2RulesetStableId = ruleset?.StableId ?? "";
            if (writeSettings)
            {
                settings.Write();
            }
        }

        internal static void Ensure(BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            if (settings.SavedRuleBuilder2Rulesets == null)
            {
                settings.SavedRuleBuilder2Rulesets = new List<RuleBuilder2Ruleset>();
            }

            for (int i = settings.SavedRuleBuilder2Rulesets.Count - 1; i >= 0; i--)
            {
                RuleBuilder2Ruleset ruleset = settings.SavedRuleBuilder2Rulesets[i];
                if (ruleset == null)
                {
                    settings.SavedRuleBuilder2Rulesets.RemoveAt(i);
                    continue;
                }

                // Saved rulesets can come from older builds. Normalize each card as
                // the collection is loaded so UI/apply code can rely on stable IDs,
                // sort order, action buffers, and a valid open-card shape.
                ruleset.Cards ??= new List<RuleBuilder2Card>();
                bool upgradeClassicDefaults = ruleset.DataVersion < 3;
                for (int j = 0; j < ruleset.Cards.Count; j++)
                {
                    RuleBuilder2Card card = ruleset.Cards[j];
                    card?.EnsureStableState(j);
                    RestoreClassicTargetSemantics(settings, card, upgradeClassicDefaults);
                }

                ruleset.DataVersion = 3;
            }

            SeedFromPreferredClassicRulesetIfNeeded(settings);

            RuleBuilder2Ruleset selected = ResolveSelected(settings);
            SetCurrent(settings, selected, writeSettings: false);
        }

        private static void RestoreClassicTargetSemantics(
            BetterWorkTabSettings settings,
            RuleBuilder2Card card,
            bool refreshFromClassic)
        {
            if (card?.Target == null || card.Notes?.Contains("Migrated from classic ruleset data.") != true)
            {
                return;
            }

            if (!card.Target.HasTarget)
            {
                card.Target.AllWorkTypes = true;
                card.Target.DisplayLabel = "All work types";
            }

            string targetDefName = card.Target.WorkTypeDefName ?? "";
            WorkAssignmentRule classicRule = settings.SavedRulesets?
                .Where(ruleset => ruleset?.Rules != null)
                .SelectMany(ruleset => ruleset.Rules)
                .FirstOrDefault(rule =>
                    string.Equals(rule?.Name, card.Name, StringComparison.Ordinal) &&
                    string.Equals(
                        rule?.Parameters?.WorktypeString ?? rule?.CachedWorktypeString ?? "",
                        targetDefName,
                        StringComparison.Ordinal));
            if (refreshFromClassic && classicRule != null)
            {
                RuleBuilder2ClassicRulesetTranslator.RefreshMigratedCard(card, classicRule);
            }

            card.Target.IgnoreIfMissing = classicRule?.Parameters?.IgnoreIfWorktypeNonexistent == true;
        }

        private static void SeedFromPreferredClassicRulesetIfNeeded(BetterWorkTabSettings settings)
        {
            bool useRuleBuilder2 = settings.useRuleBuilder2;
            if (!useRuleBuilder2 || settings.SavedRuleBuilder2Rulesets.Count > 0)
            {
                return;
            }

            WorkAssignmentRuleset classicRuleset = ResolveClassicSeedSource(settings);
            if (classicRuleset == null)
            {
                return;
            }

            RuleBuilder2Ruleset seeded = RuleBuilder2ClassicRulesetTranslator.FromClassic(classicRuleset);
            seeded.Source = RuleBuilder2SourceType.DefaultCopy;
            settings.SavedRuleBuilder2Rulesets.Add(seeded);
        }

        private static WorkAssignmentRuleset ResolveClassicSeedSource(BetterWorkTabSettings settings)
        {
            if (settings?.CurrentRuleset != null)
            {
                return settings.CurrentRuleset;
            }

            if (settings?.SavedRulesets == null || settings.SavedRulesets.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(settings.defaultAutoAssignRuleset))
            {
                WorkAssignmentRuleset namedDefault = settings.SavedRulesets.FirstOrDefault(ruleset =>
                    string.Equals(ruleset?.Name, settings.defaultAutoAssignRuleset, StringComparison.OrdinalIgnoreCase));
                if (namedDefault != null)
                {
                    return namedDefault;
                }
            }

            return settings.SavedRulesets.FirstOrDefault(ruleset => ruleset?.IsDefault == true)
                   ?? settings.SavedRulesets.FirstOrDefault(ruleset => ruleset != null);
        }

        private static RuleBuilder2Ruleset ResolveSelected(BetterWorkTabSettings settings)
        {
            // Prefer the stable saved identifier because object references can change
            // after Scribe reloads settings.
            if (!string.IsNullOrEmpty(settings.currentRuleBuilder2RulesetStableId))
            {
                RuleBuilder2Ruleset selected = settings.SavedRuleBuilder2Rulesets.FirstOrDefault(ruleset =>
                    ruleset?.StableId == settings.currentRuleBuilder2RulesetStableId);
                if (selected != null)
                {
                    return selected;
                }
            }

            if (settings.CurrentRuleBuilder2Ruleset != null)
            {
                RuleBuilder2Ruleset selected = settings.SavedRuleBuilder2Rulesets.FirstOrDefault(ruleset =>
                    ruleset == settings.CurrentRuleBuilder2Ruleset ||
                    ruleset?.StableId == settings.CurrentRuleBuilder2Ruleset.StableId);
                if (selected != null)
                {
                    return selected;
                }
            }

            return settings.SavedRuleBuilder2Rulesets.Count > 0
                ? settings.SavedRuleBuilder2Rulesets[0]
                : null;
        }
    }
}
