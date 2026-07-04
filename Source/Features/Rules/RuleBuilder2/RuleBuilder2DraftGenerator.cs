using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal sealed class RuleBuilder2DraftGenerator
    {
        public RuleBuilder2Ruleset GenerateFromCurrentWorkTab()
        {
            var pawns = RuleBuilder2Evaluator.GetCurrentPawns();
            var ruleset = new RuleBuilder2Ruleset
            {
                Name = "Draft from current Work tab",
                Description = "Generated draft. Review each card before applying.",
                Source = RuleBuilder2SourceType.GeneratedDraft,
                Cards = new List<RuleBuilder2Card>()
            };

            if (pawns.Count == 0)
            {
                ruleset.Description = "No colonists were available to inspect.";
                ruleset.EnsureOpenBlankCard();
                return ruleset;
            }

            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading
                         .Where(workType => workType != null)
                         .OrderByDescending(workType => workType.naturalPriority))
            {
                AddWorkTypeDraftIfPatternFound(ruleset, pawns, workType);
                AddSubWorkDraftsIfPatternFound(ruleset, pawns, workType);
            }

            ruleset.EnsureOpenBlankCard();
            return ruleset;
        }

        private static void AddWorkTypeDraftIfPatternFound(RuleBuilder2Ruleset ruleset, List<Pawn> pawns, WorkTypeDef workType)
        {
            var assigned = pawns
                .Where(pawn => pawn.workSettings != null && WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType) > 0)
                .OrderBy(pawn => WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType))
                .ThenByDescending(pawn => GetAverageSkill(pawn, workType))
                .ToList();

            if (assigned.Count == 0)
            {
                return;
            }

            int priority = WorkPrioritySystem.GetPriorityForPawnWorkType(assigned[0], workType);
            float minSkill = assigned.Min(pawn => GetAverageSkill(pawn, workType));
            bool hasSkillPattern = workType.relevantSkills != null &&
                                   workType.relevantSkills.Count > 0 &&
                                   minSkill >= 4f &&
                                   assigned.Count < pawns.Count;

            var card = new RuleBuilder2Card
            {
                StableId = System.Guid.NewGuid().ToString("N"),
                Name = GetWorkTypeLabel(workType) + " draft",
                SortOrder = ruleset.Cards.Count,
                Enabled = true,
                IsConfirmed = false,
                IsCollapsed = false,
                Notes = hasSkillPattern
                    ? "High confidence: assigned pawns share a skill floor."
                    : "Needs review: priorities exist but no clear skill pattern was found.",
                Target = new RuleBuilder2Target
                {
                    WorkTypeDefName = workType.defName,
                    DisplayLabel = GetWorkTypeLabel(workType),
                    Source = RuleBuilder2TargetSource.Generated
                },
                Action = new RuleBuilder2Action
                {
                    Kind = RuleBuilder2ActionKind.SetPriority,
                    Priority = priority
                }
            };

            if (hasSkillPattern)
            {
                SkillDef skill = workType.relevantSkills.FirstOrDefault();
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.SkillMinimum,
                    DefName = skill?.defName ?? "",
                    IntValue = Mathf.Clamp(Mathf.FloorToInt(minSkill), 0, 20)
                });
            }
            else
            {
                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.CurrentAssignedWork,
                    BoolValue = true,
                    DisplayText = "Review currently assigned pawns"
                });
            }

            if (TimePriorityService.HasAnySchedule())
            {
                TimePriorityTarget target = TimePriorityTarget.ForWorkType(null, workType);
                int fallback = WorkPrioritySystem.GetDefaultEnabledPriority();
                if (TimePriorityService.HasCustomSchedule(target, fallback))
                {
                    card.Action.Kind = RuleBuilder2ActionKind.SetTimeSchedule;
                    card.Action.HourlyPriorities = TimePriorityService.GetPrioritiesForDisplay(target, fallback).ToList();
                    card.Notes += " Schedule data was detected for this work type.";
                }
            }

            card.Summary = RuleBuilder2SummaryService.BuildSummary(card);
            ruleset.Cards.Add(card);
        }

        private static void AddSubWorkDraftsIfPatternFound(RuleBuilder2Ruleset ruleset, List<Pawn> pawns, WorkTypeDef workType)
        {
            var workGivers = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType);
            for (int i = 0; i < workGivers.Count; i++)
            {
                WorkGiverDef workGiver = workGivers[i]?.def;
                if (workGiver == null)
                {
                    continue;
                }

                int fallback = WorkPrioritySystem.GetDefaultEnabledPriority();
                int globalPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(null, workGiver, fallback);
                TimePriorityTarget globalTarget = TimePriorityTarget.ForWorkGiver(null, workType, workGiver);
                bool hasSchedule = TimePriorityService.HasCustomSchedule(globalTarget, globalPriority);
                bool hasOverrides = pawns.Any(pawn =>
                    WorkGiverReassignmentManager.HasPawnWorkGiverOverride(pawn, workGiver));

                if (!hasSchedule && !hasOverrides && globalPriority == fallback)
                {
                    continue;
                }

                var card = new RuleBuilder2Card
                {
                    StableId = System.Guid.NewGuid().ToString("N"),
                    Name = GetWorkGiverLabel(workGiver) + " draft",
                    SortOrder = ruleset.Cards.Count,
                    Notes = hasSchedule
                        ? "Medium confidence: sub-work schedule data was detected."
                        : "Needs review: sub-work priority differs from the inherited priority.",
                    Target = new RuleBuilder2Target
                    {
                        WorkTypeDefName = workType.defName,
                        WorkGiverDefName = workGiver.defName,
                        DisplayLabel = GetWorkTypeLabel(workType) + " -> " + GetWorkGiverLabel(workGiver),
                        Source = RuleBuilder2TargetSource.Generated
                    },
                    Action = new RuleBuilder2Action
                    {
                        Kind = hasSchedule ? RuleBuilder2ActionKind.SetSubWorkSchedule : RuleBuilder2ActionKind.SetPriority,
                        Priority = globalPriority
                    }
                };

                if (hasSchedule)
                {
                    card.Action.HourlyPriorities = TimePriorityService.GetPrioritiesForDisplay(globalTarget, globalPriority).ToList();
                }

                card.Conditions.Conditions.Add(new RuleBuilder2Condition
                {
                    Kind = RuleBuilder2ConditionKind.CurrentAssignedWork,
                    BoolValue = true,
                    DisplayText = "Review pawns already assigned to this parent work type"
                });

                card.Summary = RuleBuilder2SummaryService.BuildSummary(card);
                ruleset.Cards.Add(card);
            }
        }

        private static float GetAverageSkill(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.skills == null || workType == null)
            {
                return 0f;
            }

            return pawn.skills.AverageOfRelevantSkillsFor(workType);
        }

        private static string GetWorkTypeLabel(WorkTypeDef workType)
        {
            string label = workType?.LabelCap.ToString();
            return string.IsNullOrEmpty(label) ? workType?.defName ?? "Work" : label;
        }

        private static string GetWorkGiverLabel(WorkGiverDef workGiver)
        {
            string label = workGiver?.LabelCap.ToString();
            return string.IsNullOrEmpty(label) ? workGiver?.defName ?? "Sub-work" : label;
        }
    }
}
