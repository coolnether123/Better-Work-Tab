using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    internal sealed class RuleBuilder2ApplyService
    {
        private enum ApplyResult
        {
            NoChange,
            Changed,
            Failed
        }

        private readonly RuleBuilder2Evaluator evaluator = new RuleBuilder2Evaluator();

        public int Apply(RuleBuilder2Ruleset ruleset, out List<string> warnings, bool persistRuleset = true)
        {
            warnings = new List<string>();
            if (ruleset?.Cards == null)
            {
                warnings.Add("No Rule Builder 2.0 ruleset is loaded.");
                return 0;
            }

            if (!WorkAssignmentRuleset.CanApplyLiveRuleset(out string rejectionReason))
            {
                warnings.Add(rejectionReason);
                WorkAssignmentRuleset.RejectLiveRulesetApplication(rejectionReason);
                return 0;
            }

            RuleBuilder2SleekPriorityTranslation.AddApplyWarningIfNeeded(ruleset, warnings);

            List<Pawn> pawns = RuleBuilder2Evaluator.GetCurrentPawns();
            WorkTabAtomicMutationPlan mutation = WorkTabAtomicMutationPlan.Capture(
                pawns,
                DefDatabase<WorkTypeDef>.AllDefsListForReading);
            mutation.RequiresManualPriorities = true;
            int changed = 0;

            if (ruleset.ResetBeforeApplying)
            {
                foreach (Pawn pawn in pawns)
                {
                    foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    {
                        mutation.SetPriority(pawn, workType, WorkPrioritySystem.DisabledPriority);
                    }
                }
            }

            foreach (RuleBuilder2Card card in ruleset.Cards
                         .Where(card => card != null && card.Enabled && card.IsConfirmed)
                         .OrderBy(card => card.SortOrder))
            {
                WorkGiverDef explicitWorkGiver = card.Target.ResolveWorkGiver();
                WorkTypeDef explicitWorkType = card.Target.ResolveWorkType();
                if (!card.Target.AllWorkTypes && explicitWorkType == null)
                {
                    if (!card.Target.IgnoreIfMissing)
                    {
                        warnings.Add("Skipped unresolved target on card: " + (card.Name ?? card.StableId));
                    }

                    continue;
                }

                IEnumerable<WorkTypeDef> workTypes = card.Target.AllWorkTypes
                    ? DefDatabase<WorkTypeDef>.AllDefsListForReading
                    : new[] { explicitWorkType };
                foreach (WorkTypeDef workType in workTypes.Where(candidate => candidate != null))
                {
                    WorkGiverDef workGiver = card.Target.AllWorkTypes ? null : explicitWorkGiver;
                    foreach (Pawn pawn in pawns)
                    {
                        if (pawn?.workSettings == null || pawn.WorkTypeIsDisabled(workType))
                        {
                            continue;
                        }

                        int currentPriority = workGiver == null
                            ? mutation.GetPriority(pawn, workType)
                            : mutation.GetSpecificPriority(
                                pawn,
                                workGiver,
                                RuleBuilder2Evaluator.GetCurrentPriority(pawn, workType, workGiver));
                        bool matched = evaluator.MatchesConditions(card, pawn, workType, workGiver, currentPriority);

                        if (!matched)
                        {
                            continue;
                        }

                        ApplyResult result = CompileCardToMutation(
                            card,
                            pawn,
                            workType,
                            workGiver,
                            currentPriority,
                            mutation,
                            warnings);
                        if (result == ApplyResult.Failed)
                        {
                            return 0;
                        }

                        if (result == ApplyResult.Changed)
                        {
                            changed++;
                        }
                    }
                }
            }

            WorkTabApplicationResult applicationResult =
                WorkTabApplication.Current?.ApplyAtomicMutationPlan(mutation) ??
                WorkTabApplicationResult.Rejected(
                    "Rule Builder 2.0 could not apply its atomic ruleset mutation.", default);
            if (!applicationResult.Accepted)
            {
                AddWarningOnce(warnings, applicationResult.Reason ??
                    "Rule Builder 2.0 could not apply its atomic ruleset mutation.");
                WorkAssignmentRuleset.RejectLiveRulesetApplication(warnings.Last());
                return 0;
            }

            if (persistRuleset)
            {
                RuleBuilder2RulesetStore.SaveOrReplace(BetterWorkTabMod.Settings, ruleset, makeCurrent: true, writeSettings: false);
                LoadedModManager.GetMod<BetterWorkTabMod>()?.WriteSettings();
            }

            return changed;
        }

        private static ApplyResult CompileCardToMutation(
            RuleBuilder2Card card,
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int currentPriority,
            WorkTabAtomicMutationPlan mutation,
            List<string> warnings)
        {
            int targetPriority = RuleBuilder2Evaluator.GetActionPriority(card.Action);

            // Leave a colonist who is already better at this alone, when the
            // rule asks for that. Disabling work and enabling from nothing both
            // still go through: this only guards a real demotion.
            if (!card.AllowOverwritingHigherPriority &&
                currentPriority != 0 &&
                targetPriority != 0 &&
                currentPriority < targetPriority)
            {
                return ApplyResult.NoChange;
            }

            switch (card.Action.Kind)
            {
                case RuleBuilder2ActionKind.FollowGlobal:
                    warnings.Add("Follow global/default priority is preview-only for now. Clear sub-work overrides from the Work tab if needed.");
                    return ApplyResult.NoChange;
                case RuleBuilder2ActionKind.SetTimeSchedule:
                case RuleBuilder2ActionKind.SetSubWorkSchedule:
                    ApplyResult baseResult = CompileBasePriority(
                        pawn,
                        workType,
                        workGiver,
                        targetPriority,
                        currentPriority,
                        mutation,
                        warnings);
                    if (baseResult == ApplyResult.Failed)
                    {
                        return ApplyResult.Failed;
                    }

                    card.Action.EnsureSchedule(targetPriority);
                    TimePriorityTarget scheduleTarget = workGiver == null
                        ? TimePriorityTarget.ForWorkType(pawn, workType)
                        : TimePriorityTarget.ForWorkGiver(pawn, workGiver);
                    if (!mutation.SetSchedule(
                            scheduleTarget,
                            TimePriorityService.CreateAllHoursPinnedValue(
                                card.Action.HourlyPriorities.ToArray(),
                                targetPriority),
                            targetPriority))
                    {
                        AddWarningOnce(
                            warnings,
                            "Rule Builder 2.0 could not capture a work-priority schedule baseline.");
                        return ApplyResult.Failed;
                    }

                    return ApplyResult.Changed;
                case RuleBuilder2ActionKind.Disable:
                case RuleBuilder2ActionKind.SetPriority:
                    return CompileBasePriority(
                        pawn, workType, workGiver, targetPriority, currentPriority, mutation, warnings);
                default:
                    AddWarningOnce(
                        warnings,
                        "Skipped unsupported Rule Builder 2.0 action kind: " + (int)card.Action.Kind + ".");
                    return ApplyResult.NoChange;
            }
        }

        private static ApplyResult CompileBasePriority(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int targetPriority,
            int currentPriority,
            WorkTabAtomicMutationPlan mutation,
            List<string> warnings)
        {
            targetPriority = RuleBuilder2SleekPriorityTranslation.TranslatePriority(targetPriority);
            if (targetPriority == currentPriority)
            {
                return ApplyResult.NoChange;
            }

            if (workGiver != null)
            {
                if (!mutation.SetSpecificPriority(pawn, workGiver, targetPriority))
                {
                    AddWarningOnce(warnings,
                        "Rule Builder 2.0 could not capture a specific-job priority baseline.");
                    return ApplyResult.Failed;
                }
            }
            else
            {
                if (!mutation.SetPriority(pawn, workType, targetPriority))
                {
                    AddWarningOnce(warnings,
                        "Rule Builder 2.0 could not capture a work-type priority baseline.");
                    return ApplyResult.Failed;
                }
            }

            return ApplyResult.Changed;
        }

        private static void AddWarningOnce(List<string> warnings, string warning)
        {
            if (warnings != null && !warnings.Contains(warning))
            {
                warnings.Add(warning);
            }
        }
    }
}
