using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Verse;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    /// <summary>
    /// Describes the boundary between Rule Builder's persisted BWT priority
    /// values and Sleek's 0-5 runtime model. The ruleset is never rewritten by
    /// this class; callers translate only the value being displayed, compared,
    /// or applied to the active Work tab.
    /// </summary>
    internal static class RuleBuilder2SleekPriorityTranslation
    {
        internal static bool IsActive => SleekWorkTabGateway.SleekCodeRuns;

        internal static int TranslatePriority(int priority)
        {
            return IsActive
                ? RuleBuilder2PriorityRange.ClampForActiveWorkTab(priority)
                : RuleBuilder2PriorityRange.Clamp(priority);
        }

        internal static bool RequiresDisclaimer(RuleBuilder2Ruleset ruleset)
        {
            if (!IsActive || ruleset?.Cards == null)
            {
                return false;
            }

            foreach (RuleBuilder2Card card in ruleset.Cards.Where(IsApplicableCard))
            {
                if (HasPriorityOutsideSleekRange(card) ||
                    (ruleset.Source != RuleBuilder2SourceType.DefaultCopy &&
                     card.Target?.IsSubWorkTarget == true))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void AddApplyWarningIfNeeded(
            RuleBuilder2Ruleset ruleset,
            List<string> warnings)
        {
            if (RequiresDisclaimer(ruleset) && warnings != null)
            {
                warnings.Add(
                    "BWT_RuleBuilder2_SleekTranslation_ApplyWarning".CanTranslate()
                        ? "BWT_RuleBuilder2_SleekTranslation_ApplyWarning".Translate().ToString()
                        : "Sleek Work Priorities uses priorities 1-5. Rule Builder 2.0 kept the saved BWT rules and applied a best-effort Sleek translation.");
            }
        }

        internal static string DisclaimerText()
        {
            const string key = "BWT_RuleBuilder2_SleekTranslation_Disclaimer";
            return key.CanTranslate()
                ? key.Translate().ToString()
                : "Sleek Work Priorities uses priorities 1-5. Your saved BWT rules are kept unchanged; values outside that range and sub-work assignments are translated with BWT's best-effort mapping for this apply.";
        }

        private static bool IsApplicableCard(RuleBuilder2Card card)
        {
            return card != null && card.Enabled && card.IsConfirmed && card.Target?.HasTarget == true;
        }

        private static bool HasPriorityOutsideSleekRange(RuleBuilder2Card card)
        {
            RuleBuilder2Action action = card.Action;
            if (action != null &&
                (Outside(action.Priority) ||
                 Outside(action.ElsePriority) ||
                 (action.HourlyPriorities?.Any(Outside) ?? false)))
            {
                return true;
            }

            return card.Conditions?.Conditions?.Any(condition =>
                       condition != null &&
                       RuleBuilder2PriorityRange.IsPriorityCondition(condition.Kind) &&
                       Outside(condition.IntValue)) == true;
        }

        private static bool Outside(int priority)
        {
            return priority > RuleBuilder2PriorityRange.SleekMaxPriority;
        }
    }
}
