using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal static class RuleBuilder2MigrationService
    {
        internal static RuleBuilder2Ruleset FromLegacy(WorkAssignmentRuleset legacy)
        {
            var result = new RuleBuilder2Ruleset
            {
                Name = legacy?.Name == null ? "Migrated ruleset" : legacy.Name + " (Rule Builder 2.0)",
                Description = "Migrated from the classic Better Work Tab ruleset format. Review generated cards before applying.",
                Source = RuleBuilder2SourceType.Migrated,
                Cards = new List<RuleBuilder2Card>()
            };

            if (legacy?.Rules != null)
            {
                for (int i = 0; i < legacy.Rules.Count; i++)
                {
                    WorkAssignmentRule rule = legacy.Rules[i];
                    RuleBuilder2Card card = FromLegacyRule(rule, i);
                    if (card != null)
                    {
                        result.Cards.Add(card);
                    }
                }
            }

            result.EnsureOpenBlankCard();
            return result;
        }

        internal static WorkAssignmentRuleset TryCreateLegacyRuleset(RuleBuilder2Ruleset ruleset, out List<string> warnings)
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
                WorkAssignmentRule legacy = TryCreateLegacyRule(card, out string warning);
                if (legacy != null)
                {
                    rules.Add(legacy);
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
                (ruleset.Name ?? "Rule Builder 2.0") + " (Legacy Apply)",
                rules,
                BetterWorkTabMod.Settings?.resetWorkBeforeAutoAssign ?? DefaultSettings.resetWorkBeforeAutoAssign,
                isDefault: false);
        }

        private static RuleBuilder2Card FromLegacyRule(WorkAssignmentRule rule, int sortOrder)
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

            var card = new RuleBuilder2Card
            {
                StableId = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrEmpty(rule.Name) ? p.RuleName : rule.Name,
                Enabled = true,
                IsConfirmed = true,
                IsCollapsed = true,
                SortOrder = sortOrder,
                Notes = "Migrated from classic ruleset data.",
                Target = new RuleBuilder2Target
                {
                    WorkTypeDefName = workType?.defName ?? p.WorktypeString ?? rule.CachedWorktypeString ?? "",
                    DisplayLabel = workType?.LabelCap.ToString() ?? p.WorktypeString ?? rule.CachedWorktypeString ?? "Missing work type",
                    Source = RuleBuilder2TargetSource.Generated
                },
                Action = new RuleBuilder2Action
                {
                    Kind = p.Priority <= 0 ? RuleBuilder2ActionKind.Disable : RuleBuilder2ActionKind.SetPriority,
                    Priority = WorkPrioritySystem.ClampPriority(p.Priority)
                }
            };

            AddMigratedConditions(card, p);
            card.Summary = BuildSummary(card);
            return card;
        }

        private static void AddMigratedConditions(RuleBuilder2Card card, WorkAssignmentParameters p)
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
                    DisplayText = "Classic skip-if-priority condition migrated disabled for review."
                });
                card.Warnings.Add("Review migrated skip-if-priority behavior.");
            }
        }

        private static WorkAssignmentRule TryCreateLegacyRule(RuleBuilder2Card card, out string warning)
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

            WorkTypeDef workType = card.Target.ResolveWorkType();
            if (workType == null)
            {
                warning = "Card \"" + card.Name + "\" has an unresolved work type.";
                return null;
            }

            var parameters = new WorkAssignmentParameters(
                string.IsNullOrEmpty(card.Name) ? "Rule Builder 2.0 card" : card.Name,
                card.Action.Kind == RuleBuilder2ActionKind.Disable ? 0 : card.Action.Priority,
                workType);

            foreach (RuleBuilder2Condition condition in card.Conditions?.Conditions ?? new List<RuleBuilder2Condition>())
            {
                if (condition == null || !condition.Enabled)
                {
                    continue;
                }

                if (!TryApplyLegacyCondition(condition, parameters, out warning))
                {
                    warning = "Card \"" + card.Name + "\" has a condition that needs Rule Builder 2.0: " + warning;
                    return null;
                }
            }

            return new WorkAssignmentRule(card.Name, parameters, workType);
        }

        private static bool TryApplyLegacyCondition(RuleBuilder2Condition condition, WorkAssignmentParameters parameters, out string warning)
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
                case RuleBuilder2ConditionKind.CurrentAssignedWork:
                case RuleBuilder2ConditionKind.ExistingPriorityAtLeast:
                case RuleBuilder2ConditionKind.CapacityMinimum:
                    warning = condition.Kind.ToString();
                    return false;
                default:
                    return true;
            }
        }

        internal static string BuildSummary(RuleBuilder2Card card)
        {
            if (card == null)
            {
                return "Empty rule";
            }

            string target = card.Target?.DisplayLabel;
            if (string.IsNullOrEmpty(target))
            {
                target = card.Target?.ResolveWorkGiver()?.LabelCap.ToString()
                    ?? card.Target?.ResolveWorkType()?.LabelCap.ToString()
                    ?? "No target";
            }

            int conditionCount = card.Conditions?.Conditions?.Count(c => c != null && c.Enabled) ?? 0;
            string action = card.Action?.Kind == RuleBuilder2ActionKind.Disable
                ? "disable"
                : "priority " + (card.Action?.Priority ?? 0);
            return target + " -> " + action + " when " + conditionCount + " condition(s) match";
        }
    }
}
