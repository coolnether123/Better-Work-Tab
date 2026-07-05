using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal sealed class RuleBuilder2ApplyService
    {
        private readonly RuleBuilder2Evaluator evaluator = new RuleBuilder2Evaluator();

        public int Apply(RuleBuilder2Ruleset ruleset, out List<string> warnings, bool persistRuleset = true)
        {
            warnings = new List<string>();
            if (ruleset?.Cards == null)
            {
                warnings.Add("No Rule Builder 2.0 ruleset is loaded.");
                return 0;
            }

            Find.PlaySettings.useWorkPriorities = true;
            List<Pawn> pawns = RuleBuilder2Evaluator.GetCurrentPawns();
            int changed = 0;

            foreach (RuleBuilder2Card card in ruleset.Cards
                         .Where(card => card != null && card.Enabled && card.IsConfirmed)
                         .OrderBy(card => card.SortOrder))
            {
                WorkTypeDef workType = card.Target.ResolveWorkType();
                WorkGiverDef workGiver = card.Target.ResolveWorkGiver();
                if (workType == null)
                {
                    warnings.Add("Skipped unresolved target on card: " + (card.Name ?? card.StableId));
                    continue;
                }

                foreach (Pawn pawn in pawns)
                {
                    int currentPriority = RuleBuilder2Evaluator.GetCurrentPriority(pawn, workType, workGiver);
                    bool matched = (card.Conditions?.Conditions ?? new List<RuleBuilder2Condition>())
                        .Where(condition => condition != null && condition.Enabled)
                        .All(condition => evaluator.EvaluateCondition(condition, pawn, workType, workGiver, currentPriority));

                    if (!matched)
                    {
                        continue;
                    }

                    if (ApplyCardToPawn(card, pawn, workType, workGiver, currentPriority, warnings))
                    {
                        changed++;
                    }
                }
            }

            if (persistRuleset)
            {
                RuleBuilder2RulesetStore.SaveOrReplace(BetterWorkTabMod.Settings, ruleset, makeCurrent: true, writeSettings: false);
                LoadedModManager.GetMod<BetterWorkTabMod>()?.WriteSettings();
            }

            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            return changed;
        }

        private static bool ApplyCardToPawn(
            RuleBuilder2Card card,
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int currentPriority,
            List<string> warnings)
        {
            int targetPriority = RuleBuilder2Evaluator.GetActionPriority(card.Action);
            bool changed = false;

            switch (card.Action.Kind)
            {
                case RuleBuilder2ActionKind.FollowGlobal:
                    warnings.Add("Follow global/default priority is preview-only for now. Clear sub-work overrides from the Work tab if needed.");
                    return false;
                case RuleBuilder2ActionKind.SetTimeSchedule:
                case RuleBuilder2ActionKind.SetSubWorkSchedule:
                    changed |= ApplyBasePriority(pawn, workType, workGiver, targetPriority, currentPriority);
                    card.Action.EnsureSchedule(targetPriority);
                    TimePriorityTarget scheduleTarget = workGiver == null
                        ? TimePriorityTarget.ForWorkType(pawn, workType)
                        : TimePriorityTarget.ForWorkGiver(pawn, workType, workGiver);
                    TimePriorityService.SetPrioritiesSynced(scheduleTarget, card.Action.HourlyPriorities.ToArray(), targetPriority);
                    return true;
                case RuleBuilder2ActionKind.Disable:
                case RuleBuilder2ActionKind.SetPriority:
                default:
                    return ApplyBasePriority(pawn, workType, workGiver, targetPriority, currentPriority);
            }
        }

        private static bool ApplyBasePriority(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver, int targetPriority, int currentPriority)
        {
            targetPriority = WorkPrioritySystem.ClampPriority(targetPriority);
            if (targetPriority == currentPriority)
            {
                return false;
            }

            if (workGiver != null)
            {
                WorkGiverReassignmentManager.SetPawnOverrideSynced(pawn.thingIDNumber, workGiver.defName, targetPriority);
            }
            else
            {
                WorkPrioritySystem.SetPriority(pawn.workSettings, workType, targetPriority);
            }

            return true;
        }
    }
}
