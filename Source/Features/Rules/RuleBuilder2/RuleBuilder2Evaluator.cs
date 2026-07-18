using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal sealed class RuleBuilder2Evaluator
    {
        private static Func<List<Pawn>> currentPawnOrderProvider;

        internal static void RegisterCurrentPawnOrderProvider(Func<List<Pawn>> provider)
        {
            currentPawnOrderProvider = provider;
        }

        public List<RuleBuilder2PreviewResult> Preview(RuleBuilder2Ruleset ruleset, RuleBuilder2Card focusCard = null)
        {
            var pawns = GetCurrentPawns();
            var cards = ruleset?.Cards?
                .Where(card => card != null && card.Enabled && card.Target?.HasTarget == true)
                .OrderBy(card => card.SortOrder)
                .ToList() ?? new List<RuleBuilder2Card>();

            if (focusCard != null && !cards.Contains(focusCard))
            {
                cards = new List<RuleBuilder2Card> { focusCard };
            }

            var results = new List<RuleBuilder2PreviewResult>();
            foreach (var card in cards)
            {
                WorkTypeDef explicitWorkType = card.Target.ResolveWorkType();
                if (!card.Target.AllWorkTypes && explicitWorkType == null)
                {
                    continue;
                }

                WorkGiverDef explicitWorkGiver = card.Target.ResolveWorkGiver();
                IEnumerable<WorkTypeDef> workTypes = card.Target.AllWorkTypes
                    ? DefDatabase<WorkTypeDef>.AllDefsListForReading
                    : new[] { explicitWorkType };
                foreach (WorkTypeDef workType in workTypes.Where(candidate => candidate != null))
                {
                    WorkGiverDef workGiver = card.Target.AllWorkTypes ? null : explicitWorkGiver;
                    foreach (Pawn pawn in pawns)
                    {
                        results.Add(PreviewPawn(card, pawn, workType, workGiver, cards));
                    }
                }
            }

            return results;
        }

        public RuleBuilder2PreviewResult PreviewPawn(
            RuleBuilder2Card card,
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            List<RuleBuilder2Card> allCards = null)
        {
            var result = new RuleBuilder2PreviewResult
            {
                Pawn = pawn,
                Target = card.Target,
                CurrentPriority = GetCurrentPriority(pawn, workType, workGiver),
                NewPriority = GetActionPriority(card.Action),
                ActionText = GetActionText(card, workType, workGiver)
            };

            if (pawn == null || workType == null)
            {
                result.Warning = Tr("BWT_RuleBuilder2_WarningMissingPawnOrTarget");
                return result;
            }

            bool matched = true;
            var conditions = card.Conditions?.Conditions ?? new List<RuleBuilder2Condition>();
            foreach (var condition in conditions.Where(c => c != null && c.Enabled))
            {
                string text = RuleBuilder2ConditionCatalog.GetConditionText(condition, workType);
                if (EvaluateCondition(condition, pawn, workType, workGiver, result.CurrentPriority))
                {
                    result.ConditionsMet.Add(text);
                }
                else
                {
                    result.ConditionsFailed.Add(text);
                    matched = false;
                }
            }

            result.Matched = matched;
            result.Warning = FindConflictWarning(card, allCards);
            return result;
        }

        public bool EvaluateCondition(
            RuleBuilder2Condition condition,
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int currentPriority)
        {
            if (condition == null || !condition.Enabled)
            {
                return true;
            }

            switch (condition.Kind)
            {
                case RuleBuilder2ConditionKind.SkillMinimum:
                    return GetSkillLevel(pawn, workType, condition.DefName) >= condition.IntValue;
                case RuleBuilder2ConditionKind.SkillMaximum:
                    return GetSkillLevel(pawn, workType, condition.DefName) <= condition.IntValue;
                case RuleBuilder2ConditionKind.PassionAtLeast:
                    return GetPassionLevel(pawn, workType, condition.DefName) >= condition.IntValue;
                case RuleBuilder2ConditionKind.Trait:
                    return HasTrait(pawn, condition.DefName);
                case RuleBuilder2ConditionKind.CapacityMinimum:
                    return GetCapacityLevel(pawn, condition.DefName) >= condition.FloatValue;
                case RuleBuilder2ConditionKind.Xenotype:
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19 && !v0_18 && !v0_17 && !v0_16 && !v0_15 && !v0_14 && !v0_13 && !vAlpha4
                    return pawn.genes?.Xenotype?.defName == condition.DefName;
#else
                    return false;
#endif
                case RuleBuilder2ConditionKind.Gender:
                    return string.Equals(pawn.gender.ToString(), condition.TextValue, System.StringComparison.OrdinalIgnoreCase);
                case RuleBuilder2ConditionKind.ExistingPriorityAtLeast:
                    return currentPriority >= condition.IntValue;
                case RuleBuilder2ConditionKind.ExistingPriorityEquals:
                    return currentPriority == condition.IntValue;
                case RuleBuilder2ConditionKind.CurrentAssignedWork:
                    return (currentPriority > 0) == condition.BoolValue;
                case RuleBuilder2ConditionKind.HighestSkillAmongColonists:
                    return HasHighestRelevantSkill(pawn, workType);
                case RuleBuilder2ConditionKind.TopWorkTypesBySkill:
                    return IsAmongTopWorkTypes(pawn, workType, condition.IntValue);
                case RuleBuilder2ConditionKind.NaturalAlwaysActiveWork:
                    return workType?.alwaysStartActive == true;
                case RuleBuilder2ConditionKind.ParentHasChildOnMap:
                    return HasChildOnCurrentMap(pawn);
                default:
                    return true;
            }
        }

        internal static List<Pawn> GetCurrentPawns()
        {
            List<Pawn> fallback = GetFallbackCurrentPawns();
            List<Pawn> provided = null;
            try
            {
                provided = currentPawnOrderProvider?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Warning("[BWT] Rule Builder 2.0 pawn order provider failed; using default pawn order.\n" + ex);
            }

            if (provided == null || provided.Count == 0)
            {
                return fallback;
            }

            var seen = new HashSet<Pawn>();
            var ordered = new List<Pawn>(provided.Count + fallback.Count);
            foreach (Pawn pawn in provided)
            {
                if (pawn != null && !pawn.Dead && seen.Add(pawn))
                {
                    ordered.Add(pawn);
                }
            }

            foreach (Pawn pawn in fallback)
            {
                if (pawn != null && seen.Add(pawn))
                {
                    ordered.Add(pawn);
                }
            }

            return ordered.Count > 0 ? ordered : fallback;
        }

        private static List<Pawn> GetFallbackCurrentPawns()
        {
            return Find.CurrentMap?.mapPawns?.FreeColonists?
                .Where(pawn => pawn != null && !pawn.Dead)
                .OrderBy(pawn => pawn.playerSettings?.displayOrder ?? 0)
                .ThenBy(pawn => pawn.LabelShortCap)
                .ToList() ?? new List<Pawn>();
        }

        internal static int GetCurrentPriority(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            if (pawn == null || workType == null)
            {
                return WorkPrioritySystem.DisabledPriority;
            }

            if (workGiver != null)
            {
                int parentPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
                return WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);
            }

            return WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
        }

        internal static int GetActionPriority(RuleBuilder2Action action)
        {
            if (action == null)
            {
                return WorkPrioritySystem.DisabledPriority;
            }

            switch (action.Kind)
            {
                case RuleBuilder2ActionKind.Disable:
                    return WorkPrioritySystem.DisabledPriority;
                case RuleBuilder2ActionKind.FollowGlobal:
                    return WorkPrioritySystem.GetDefaultEnabledPriority();
                case RuleBuilder2ActionKind.SetTimeSchedule:
                case RuleBuilder2ActionKind.SetSubWorkSchedule:
                case RuleBuilder2ActionKind.SetPriority:
                default:
                    return WorkPrioritySystem.ClampPriority(action.Priority);
            }
        }

        internal static string GetActionText(RuleBuilder2Card card, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            string targetLabel = workGiver?.LabelCap.ToString() ?? workType?.LabelCap.ToString() ?? Tr("BWT_RuleBuilder2_TargetFallback");
            RuleBuilder2Action action = card?.Action;
            if (action == null)
            {
                return Tr("BWT_RuleBuilder2_ActionText_NoAction");
            }

            switch (action.Kind)
            {
                case RuleBuilder2ActionKind.Disable:
                    return Tr("BWT_RuleBuilder2_ActionText_Disable", targetLabel);
                case RuleBuilder2ActionKind.FollowGlobal:
                    return Tr("BWT_RuleBuilder2_ActionText_FollowGlobal", targetLabel);
                case RuleBuilder2ActionKind.SetTimeSchedule:
                case RuleBuilder2ActionKind.SetSubWorkSchedule:
                    return Tr("BWT_RuleBuilder2_ActionText_Schedule", targetLabel);
                default:
                    return Tr("BWT_RuleBuilder2_ActionText_SetPriority", targetLabel, action.Priority);
            }
        }

        private static string Tr(string key, params object[] args)
        {
            if (!key.CanTranslate())
            {
                return key;
            }

            TaggedString translated = key.Translate();
            return args != null && args.Length > 0
                ? string.Format(translated.ToString(), args)
                : translated.ToString();
        }

        private static float GetSkillLevel(Pawn pawn, WorkTypeDef workType, string skillDefName)
        {
            if (pawn?.skills == null)
            {
                return 0f;
            }

            SkillDef skill = string.IsNullOrEmpty(skillDefName)
                ? workType?.relevantSkills?.FirstOrDefault()
                : DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);

            if (skill != null)
            {
                return pawn.skills.GetSkill(skill)?.Level ?? 0f;
            }

            return workType == null ? 0f : pawn.skills.AverageOfRelevantSkillsFor(workType);
        }

        private static int GetPassionLevel(Pawn pawn, WorkTypeDef workType, string skillDefName)
        {
            if (pawn?.skills == null)
            {
                return 0;
            }

            SkillDef skill = string.IsNullOrEmpty(skillDefName)
                ? workType?.relevantSkills?.FirstOrDefault()
                : DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);

            if (skill != null)
            {
                return (int)(pawn.skills.GetSkill(skill)?.passion ?? Passion.None);
            }

            if (workType == null)
            {
                return 0;
            }

            int max = 0;
            foreach (SkillDef relevant in workType.relevantSkills ?? Enumerable.Empty<SkillDef>())
            {
                max = Mathf.Max(max, (int)(pawn.skills.GetSkill(relevant)?.passion ?? Passion.None));
            }

            return max;
        }

        private static bool HasTrait(Pawn pawn, string traitDefName)
        {
            if (pawn?.story?.traits == null || string.IsNullOrEmpty(traitDefName))
            {
                return false;
            }

            TraitDef trait = DefDatabase<TraitDef>.GetNamedSilentFail(traitDefName);
            return trait != null && pawn.story.traits.HasTrait(trait);
        }

        private static bool HasHighestRelevantSkill(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.skills == null || workType == null)
            {
                return false;
            }

            float value = pawn.skills.AverageOfRelevantSkillsFor(workType);
            return GetCurrentPawns().Where(other => other?.skills != null)
                .All(other => other.skills.AverageOfRelevantSkillsFor(workType) <= value);
        }

        private static bool IsAmongTopWorkTypes(Pawn pawn, WorkTypeDef workType, int count)
        {
            if (pawn?.skills == null || workType == null || count <= 0)
            {
                return false;
            }

            return WorkAssignmentRule.AllWorkTypes
                .Where(candidate => !candidate.alwaysStartActive && !pawn.WorkTypeIsDisabled(candidate))
                .OrderByDescending(candidate => pawn.skills.AverageOfRelevantSkillsFor(candidate))
                .Take(count)
                .Contains(workType);
        }

        private static bool HasChildOnCurrentMap(Pawn pawn)
        {
#if v1_3 || v1_2 || v1_1 || v1_0 || v0_19 || v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
            return false;
#else
            return pawn != null && Find.CurrentMap?.mapPawns?.FreeColonists
                .Where(child => child != pawn && (int)child.DevelopmentalStage < (int)DevelopmentalStage.Adult)
                .Any(child => child.GetFather() == pawn || child.GetMother() == pawn) == true;
#endif
        }

        private static float GetCapacityLevel(Pawn pawn, string capacityDefName)
        {
            if (pawn?.health?.capacities == null || string.IsNullOrEmpty(capacityDefName))
            {
                return 0f;
            }

            PawnCapacityDef capacity = DefDatabase<PawnCapacityDef>.GetNamedSilentFail(capacityDefName);
            return capacity == null ? 0f : pawn.health.capacities.GetLevel(capacity);
        }

        private static string FindConflictWarning(RuleBuilder2Card card, List<RuleBuilder2Card> allCards)
        {
            if (card?.Target == null || allCards == null)
            {
                return "";
            }

            bool conflict = allCards.Any(other =>
                other != null &&
                other != card &&
                other.Enabled &&
                other.SortOrder <= card.SortOrder &&
                other.Target?.WorkTypeDefName == card.Target.WorkTypeDefName &&
                other.Target?.WorkGiverDefName == card.Target.WorkGiverDefName);

            return conflict
                ? "Another enabled card targets this work item earlier in the ruleset."
                : "";
        }
    }
}
