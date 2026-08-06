using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal static class RuleBuilder2ClassicRulesetTranslator
    {
        internal static RuleBuilder2Ruleset FromClassic(
            WorkAssignmentRuleset classicRuleset,
            bool deterministicStableIds = false)
        {
            string stableSeed = deterministicStableIds
                ? "classic|" + (classicRuleset?.Name ?? string.Empty) + "|" +
                  (classicRuleset?.ResetBeforeApplying ?? true) + "|" +
                  (classicRuleset?.Rules?.Count ?? 0)
                : null;
            var result = new RuleBuilder2Ruleset
            {
                StableId = deterministicStableIds
                    ? RuleBuilder2StableIdentity.FromSeed(stableSeed + "|ruleset")
                    : Guid.NewGuid().ToString("N"),
                Name = classicRuleset?.Name == null ? "Migrated ruleset" : classicRuleset.Name + " (Rule Builder 2.0)",
                Description = "Migrated from the classic Better Work Tab ruleset format.",
                ResetBeforeApplying = classicRuleset?.ResetBeforeApplying ?? true,
                Source = RuleBuilder2SourceType.Migrated,
                Cards = new List<RuleBuilder2Card>()
            };

            if (classicRuleset?.Rules != null)
            {
                for (int i = 0; i < classicRuleset.Rules.Count; i++)
                {
                    WorkAssignmentRule rule = classicRuleset.Rules[i];
                    RuleBuilder2Card card = FromClassicRule(rule, i, stableSeed);
                    if (card != null)
                    {
                        result.Cards.Add(card);
                    }
                }
            }

            result.EnsureOpenBlankCard();
            if (deterministicStableIds && result.Cards.Count > 0)
            {
                RuleBuilder2Card openCard = result.Cards[result.Cards.Count - 1];
                if (openCard != null && !openCard.IsConfirmed)
                {
                    openCard.StableId = RuleBuilder2StableIdentity.FromSeed(
                        stableSeed + "|open-card");
                }
            }
            return result;
        }

        internal static WorkAssignmentRuleset TryCreateClassicRuleset(RuleBuilder2Ruleset ruleset, out List<string> warnings)
        {
            warnings = new List<string>();
            if (ruleset?.Cards == null)
            {
                return null;
            }

            var rules = new List<WorkAssignmentRule>();
            foreach (RuleBuilder2Card card in ruleset.Cards
                         .Where(card => card != null && card.Enabled && card.IsConfirmed)
                         .OrderBy(card => card.SortOrder))
            {
                WorkAssignmentRule classicRule = TryCreateClassicRule(card, out string warning);
                if (classicRule != null)
                {
                    rules.Add(classicRule);
                }
                else if (!string.IsNullOrEmpty(warning))
                {
                    warnings.Add(warning);
                }
            }

            if (rules.Count == 0)
            {
                return null;
            }

            return new WorkAssignmentRuleset(
                (ruleset.Name ?? "Rule Builder 2.0") + " (Classic Export)",
                rules,
                ruleset.ResetBeforeApplying,
                isDefault: false);
        }

        private static RuleBuilder2Card FromClassicRule(
            WorkAssignmentRule rule,
            int sortOrder,
            string stableSeed = null)
        {
            if (rule?.Parameters == null)
            {
                return null;
            }

            WorkAssignmentParameters p = rule.Parameters;
            WorkTypeDef workType = p.Worktype ??
                                   rule.CachedWorktype ??
                                   DefDatabase<WorkTypeDef>.GetNamedSilentFail(p.WorktypeString) ??
                                   DefDatabase<WorkTypeDef>.GetNamedSilentFail(rule.CachedWorktypeString);
            string workTypeDefName = workType?.defName ?? p.WorktypeString ?? rule.CachedWorktypeString ?? "";
            bool allWorkTypes = string.IsNullOrEmpty(workTypeDefName);

            var card = new RuleBuilder2Card
            {
                StableId = stableSeed == null
                    ? Guid.NewGuid().ToString("N")
                    : RuleBuilder2StableIdentity.FromSeed(stableSeed + "|card|" + sortOrder),
                Name = string.IsNullOrEmpty(rule.Name) ? p.RuleName : rule.Name,
                Enabled = true,
                IsConfirmed = true,
                IsCollapsed = true,
                SortOrder = sortOrder,
                Notes = "Migrated from classic ruleset data.",
                Target = new RuleBuilder2Target
                {
                    WorkTypeDefName = workTypeDefName,
                    DisplayLabel = allWorkTypes
                        ? "All work types"
                        : workType?.LabelCap.ToString() ?? p.WorktypeString ?? rule.CachedWorktypeString ?? "Missing work type",
                    Source = RuleBuilder2TargetSource.Generated,
                    AllWorkTypes = allWorkTypes,
                    IgnoreIfMissing = p.IgnoreIfWorktypeNonexistent
                },
                Action = new RuleBuilder2Action
                {
                    Kind = p.Priority <= 0 ? RuleBuilder2ActionKind.Disable : RuleBuilder2ActionKind.SetPriority,
                    Priority = RuleBuilder2PriorityRange.Clamp(p.Priority)
                }
            };

            AddClassicConditions(card, p);
            if (stableSeed != null)
            {
                for (int i = 0; i < card.Conditions.Conditions.Count; i++)
                {
                    RuleBuilder2Condition condition = card.Conditions.Conditions[i];
                    if (condition != null)
                    {
                        condition.StableId = RuleBuilder2StableIdentity.FromSeed(
                            stableSeed + "|card|" + sortOrder + "|condition|" + i);
                    }
                }
            }
            card.EnsureStableState(sortOrder);
            card.Summary = RuleBuilder2SummaryService.BuildSummary(card);
            return card;
        }

        internal static void RefreshMigratedCard(RuleBuilder2Card card, WorkAssignmentRule classicRule)
        {
            if (card == null || classicRule == null)
            {
                return;
            }

            RuleBuilder2Card refreshed = FromClassicRule(classicRule, card.SortOrder);
            if (refreshed == null)
            {
                return;
            }

            card.Target = refreshed.Target;
            card.Conditions = refreshed.Conditions;
            card.Action = refreshed.Action;
            card.Summary = refreshed.Summary;
            card.Warnings = refreshed.Warnings;
            card.NormalizeActionForTarget();
        }

        private static void AddClassicConditions(RuleBuilder2Card card, WorkAssignmentParameters p)
        {
            if (p.SkillLevelGreaterThan >= 0)
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.SkillMinimum,
                    IntValue = p.SkillLevelGreaterThan + 1
                });
            }

            if (p.SkillLevelLessThan >= 0)
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.SkillMaximum,
                    IntValue = p.SkillLevelLessThan - 1
                });
            }

            if (p.PassionLevel >= 0)
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.PassionAtLeast,
                    IntValue = p.PassionLevel
                });
            }

            if (p.HasHighestSkill)
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.HighestSkillAmongColonists
                });
            }

            if (p.IsTopXSkill > 0)
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.TopWorkTypesBySkill,
                    IntValue = p.IsTopXSkill
                });
            }

            if (p.IsNaturalAlwaysAssign)
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.NaturalAlwaysActiveWork
                });
            }

            if (p.HasChildOnMap)
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.ParentHasChildOnMap
                });
            }

            if (p.IsCapableOfViolence)
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.CapableOfViolence
                });
            }

            if (p.IsPregnant)
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.IsPregnant
                });
            }

            if (!string.IsNullOrEmpty(p.TraitString))
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.Trait,
                    DefName = p.TraitString
                });
            }

            if (!string.IsNullOrEmpty(p.XenotypeString))
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.Xenotype,
                    DefName = p.XenotypeString
                });
            }

            if (p.Gender.HasValue)
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.Gender,
                    TextValue = p.Gender.Value.ToString()
                });
            }

            if (p.SkipIfPriorityForThisWorktypeAreadyAssigned >= 0)
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.ExistingPriorityEquals,
                    IntValue = p.SkipIfPriorityForThisWorktypeAreadyAssigned,
                    Enabled = false,
                    DisplayText = "Classic skip-if-priority condition migrated disabled."
                });
                card.Warnings.Add("Check migrated skip-if-priority behavior.");
            }

            WarnAboutUntranslatedClassicParameters(card, p);
        }

        /// <summary>
        /// Names every classic setting this card could not carry across.
        ///
        /// Silence was the old behaviour and it is the worst one available: a
        /// rule that said "only pawns capable of violence" imported as a rule
        /// that said nothing of the kind, kept its name, and looked migrated.
        /// The player found out when somebody who should not have been hunting
        /// went hunting. Rule Builder 2.0 still cannot express these, but it can
        /// refuse to pretend otherwise.
        /// </summary>
        private static void WarnAboutUntranslatedClassicParameters(RuleBuilder2Card card, WorkAssignmentParameters p)
        {
            var dropped = new List<string>();

            // Per-pawn tests with no Rule Builder 2.0 equivalent yet.
            if (p.IsNthBestSkill > 0) dropped.Add("nth-best skill (" + p.IsNthBestSkill + ")");

            // Allocation strategies. These decide between pawns rather than
            // testing one, so they are not conditions at all and would need a
            // mechanism Rule Builder 2.0 does not have.
            if (p.IsNthBestPawn > 0) dropped.Add("nth-best pawn (" + p.IsNthBestPawn + ")");
            if (p.RandomIfMultiple) dropped.Add("pick at random when several match");
            if (p.AssignToPawnWithFewestWorkPriorities) dropped.Add("prefer the least-assigned pawn");
            if (p.SkipIfAnotherPawnAssigned) dropped.Add("skip if another pawn is assigned");
            if (p.LimitNumberOfWorktypes > 0) dropped.Add("limit of " + p.LimitNumberOfWorktypes + " Work types");
            if (p.AllowOverwritingHigherPriority) dropped.Add("allow overwriting a higher priority");

            // A trait degree is part of which trait is meant -- "very neurotic"
            // and "neurotic" are degrees of one TraitDef -- so dropping it
            // widens the rule to everyone with any degree of that trait.
            if (p.TraitDegree.HasValue && !string.IsNullOrEmpty(p.TraitString))
            {
                dropped.Add("trait degree " + p.TraitDegree.Value + " (imported as any degree of " + p.TraitString + ")");
            }

            if (dropped.Count > 0)
            {
                card.Warnings.Add(
                    "Classic settings that did not carry across: " + string.Join(", ", dropped.ToArray()) + ".");
            }
        }

        private static WorkAssignmentRule TryCreateClassicRule(RuleBuilder2Card card, out string warning)
        {
            warning = "";
            if (card.Target == null || card.Target.IsSubWorkTarget)
            {
                warning = "Card \"" + card.Name + "\" targets sub-work and cannot be represented in classic rules.";
                return null;
            }

            if (card.Action.Kind == RuleBuilder2ActionKind.SetTimeSchedule ||
                card.Action.Kind == RuleBuilder2ActionKind.SetSubWorkSchedule ||
                card.Action.Kind == RuleBuilder2ActionKind.FollowGlobal)
            {
                warning = "Card \"" + card.Name + "\" uses a Rule Builder 2.0-only action.";
                return null;
            }

            // A classic rule is one set of parameters, all of which must hold,
            // so it has nowhere to put an alternative. Exporting one anyway
            // would quietly turn "or" into "and" and hand back a rule that
            // matches far fewer pawns than the one it came from.
            if (RuleBuilder2ConditionGrouping.HasAlternatives(card.Conditions?.Conditions))
            {
                warning = "Card \"" + card.Name + "\" has conditions joined by \"or\", which classic rules cannot express.";
                return null;
            }

            WorkTypeDef workType = card.Target.ResolveWorkType();
            bool unresolvedOptionalTarget = workType == null &&
                                            card.Target.IgnoreIfMissing &&
                                            !string.IsNullOrEmpty(card.Target.WorkTypeDefName);
            if (!card.Target.AllWorkTypes && workType == null && !unresolvedOptionalTarget)
            {
                warning = "Card \"" + card.Name + "\" has an unresolved work type.";
                return null;
            }

            var parameters = new WorkAssignmentParameters(
                string.IsNullOrEmpty(card.Name) ? "Rule Builder 2.0 card" : card.Name,
                card.Action.Kind == RuleBuilder2ActionKind.Disable ? 0 : card.Action.Priority,
                workType,
                ignoreIfWorktypeNonexistent: card.Target.IgnoreIfMissing,
                worktypeString: card.Target.WorkTypeDefName ?? "");

            foreach (RuleBuilder2Condition condition in card.Conditions?.Conditions ?? new List<RuleBuilder2Condition>())
            {
                if (condition == null || !condition.Enabled)
                {
                    continue;
                }

                if (!TryApplyClassicCondition(condition, parameters, out warning))
                {
                    warning = "Card \"" + card.Name + "\" has a condition that needs Rule Builder 2.0: " + warning;
                    return null;
                }
            }

            return new WorkAssignmentRule(card.Name, parameters, workType);
        }

        private static bool TryApplyClassicCondition(RuleBuilder2Condition condition, WorkAssignmentParameters parameters, out string warning)
        {
            warning = "";
            switch (condition.Kind)
            {
                case RuleBuilder2ConditionKind.SkillMinimum:
                    parameters.SkillLevelGreaterThan = Math.Max(0, condition.IntValue - 1);
                    parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.SkillLevelGreaterThan));
                    return true;
                case RuleBuilder2ConditionKind.SkillMaximum:
                    parameters.SkillLevelLessThan = condition.IntValue + 1;
                    parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.SkillLevelLessThan));
                    return true;
                case RuleBuilder2ConditionKind.PassionAtLeast:
                    parameters.PassionLevel = condition.IntValue;
                    parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.PassionLevel));
                    return true;
                case RuleBuilder2ConditionKind.Trait:
                    TraitDef trait = DefDatabase<TraitDef>.GetNamedSilentFail(condition.DefName);
                    if (trait == null)
                    {
                        warning = "missing trait " + condition.DefName;
                        return false;
                    }

                    parameters.RequiredTrait = new Tuple<TraitDef, int>(trait, 0);
                    parameters.TraitString = trait.defName;
                    parameters.TraitDegree = 0;
                    parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.RequiredTrait));
                    return true;
                case RuleBuilder2ConditionKind.Xenotype:
                    parameters.Xenotype = DefDatabase<XenotypeDef>.GetNamedSilentFail(condition.DefName);
                    parameters.XenotypeString = condition.DefName;
                    parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.Xenotype));
                    return true;
                case RuleBuilder2ConditionKind.Gender:
                    if (Enum.TryParse(condition.TextValue, out Gender gender))
                    {
                        parameters.Gender = gender;
                        parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.Gender));
                        return true;
                    }

                    warning = "unknown gender " + condition.TextValue;
                    return false;
                case RuleBuilder2ConditionKind.ExistingPriorityEquals:
                    parameters.SkipIfPriorityForThisWorktypeAreadyAssigned = condition.IntValue;
                    parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.SkipIfPriorityForThisWorktypeAreadyAssigned));
                    return true;
                case RuleBuilder2ConditionKind.HighestSkillAmongColonists:
                    parameters.HasHighestSkill = true;
                    parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.HasHighestSkill));
                    return true;
                case RuleBuilder2ConditionKind.TopWorkTypesBySkill:
                    parameters.IsTopXSkill = condition.IntValue;
                    parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.IsTopXSkill));
                    return true;
                case RuleBuilder2ConditionKind.NaturalAlwaysActiveWork:
                    parameters.IsNaturalAlwaysAssign = true;
                    parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.IsNaturalAlwaysAssign));
                    return true;
                case RuleBuilder2ConditionKind.ParentHasChildOnMap:
                    parameters.HasChildOnMap = true;
                    parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.HasChildOnMap));
                    return true;
                case RuleBuilder2ConditionKind.CapableOfViolence:
                    parameters.IsCapableOfViolence = true;
                    parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.IsCapableOfViolence));
                    return true;
                case RuleBuilder2ConditionKind.IsPregnant:
                    parameters.IsPregnant = true;
                    parameters.ActiveConditions.Add(nameof(WorkAssignmentParameters.IsPregnant));
                    return true;
                case RuleBuilder2ConditionKind.CurrentAssignedWork:
                case RuleBuilder2ConditionKind.ExistingPriorityAtLeast:
                case RuleBuilder2ConditionKind.CapacityMinimum:
                    warning = condition.Kind.ToString();
                    return false;
                default:
                    return true;
            }
        }
    }
}
