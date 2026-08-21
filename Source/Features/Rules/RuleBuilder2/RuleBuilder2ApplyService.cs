using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
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

            if (!WorkPrioritySystem.SetManualPriorities(true))
            {
                warnings.Add(WorkAssignmentRuleset.LiveRulesetManualPriorityBlockedReason);
                WorkAssignmentRuleset.RejectLiveRulesetApplication(
                    WorkAssignmentRuleset.LiveRulesetManualPriorityBlockedReason);
                return 0;
            }
            List<Pawn> pawns = RuleBuilder2Evaluator.GetCurrentPawns();
            int changed = 0;

            if (ruleset.ResetBeforeApplying)
            {
                if (!WorkAssignmentRuleset.SetAllToZero())
                {
                    return 0;
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

                        int currentPriority = RuleBuilder2Evaluator.GetCurrentPriority(pawn, workType, workGiver);
                        bool matched = evaluator.MatchesConditions(card, pawn, workType, workGiver, currentPriority);

                        if (!matched)
                        {
                            continue;
                        }

                        ApplyResult result = ApplyCardToPawn(
                            card,
                            pawn,
                            workType,
                            workGiver,
                            currentPriority,
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

            if (persistRuleset)
            {
                RuleBuilder2RulesetStore.SaveOrReplace(BetterWorkTabMod.Settings, ruleset, makeCurrent: true, writeSettings: false);
                LoadedModManager.GetMod<BetterWorkTabMod>()?.WriteSettings();
            }

            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            return changed;
        }

        private static ApplyResult ApplyCardToPawn(
            RuleBuilder2Card card,
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int currentPriority,
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
                    ApplyResult baseResult = ApplyBasePriority(
                        pawn,
                        workType,
                        workGiver,
                        targetPriority,
                        currentPriority,
                        warnings);
                    if (baseResult == ApplyResult.Failed || !TryRecheckWriterAuthority(warnings))
                    {
                        return ApplyResult.Failed;
                    }

                    card.Action.EnsureSchedule(targetPriority);
                    TimePriorityTarget scheduleTarget = workGiver == null
                        ? TimePriorityTarget.ForRuntimeWorkType(pawn, workType)
                        : TimePriorityTarget.ForRuntimeWorkGiver(pawn, workType, workGiver);
                    if (!TimePriorityService.SetScheduleSynced(
                            scheduleTarget,
                            card.Action.HourlyPriorities.ToArray(),
                            TimePriorityService.CreateAllHoursPinnedState(),
                            targetPriority))
                    {
                        return RejectWriteFailure(
                            warnings,
                            "Rule Builder 2.0 could not write a work-priority schedule.");
                    }

                    if (!TryRecheckWriterAuthority(warnings))
                    {
                        return ApplyResult.Failed;
                    }

                    return ApplyResult.Changed;
                case RuleBuilder2ActionKind.Disable:
                case RuleBuilder2ActionKind.SetPriority:
                    return ApplyBasePriority(pawn, workType, workGiver, targetPriority, currentPriority, warnings);
                default:
                    AddWarningOnce(
                        warnings,
                        "Skipped unsupported Rule Builder 2.0 action kind: " + (int)card.Action.Kind + ".");
                    return ApplyResult.NoChange;
            }
        }

        private static ApplyResult ApplyBasePriority(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int targetPriority,
            int currentPriority,
            List<string> warnings)
        {
            targetPriority = RuleBuilder2SleekPriorityTranslation.TranslatePriority(targetPriority);
            if (targetPriority == currentPriority)
            {
                return ApplyResult.NoChange;
            }

            if (!TryRecheckWriterAuthority(warnings))
            {
                return ApplyResult.Failed;
            }

            if (workGiver != null)
            {
                if (SleekWorkTabGateway.SleekCodeRuns)
                {
                    if (!SleekWorkTabGateway.TrySetSleekWorkGiverOverride(pawn, workGiver, targetPriority))
                    {
                        AddWarningOnce(
                            warnings,
                            "Sleek Work Priorities' per-job store was unavailable; this sub-work rule was not applied.");
                        return RejectWriteFailure(
                            warnings,
                            "Rule Builder 2.0 could not write a specific-job priority.");
                    }
                }
                else
                {
                    WorkGiverReassignmentManager.SetPawnOverrideSynced(
                        pawn.thingIDNumber,
                        workGiver.defName,
                        targetPriority);
                }

                if (!TryRecheckWriterAuthority(warnings))
                {
                    return ApplyResult.Failed;
                }
            }
            else
            {
                if (!WorkPrioritySystem.SetPriority(pawn.workSettings, workType, targetPriority))
                {
                    return RejectWriteFailure(
                        warnings,
                        "Rule Builder 2.0 could not write a work-type priority.");
                }
            }

            return ApplyResult.Changed;
        }

        private static bool TryRecheckWriterAuthority(List<string> warnings)
        {
            if (WorkAssignmentRuleset.CanApplyLiveRuleset(out string rejectionReason))
            {
                return true;
            }

            AddWarningOnce(warnings, rejectionReason);
            WorkAssignmentRuleset.RejectLiveRulesetApplication(rejectionReason);
            return false;
        }

        private static ApplyResult RejectWriteFailure(List<string> warnings, string fallbackReason)
        {
            string rejectionReason = WorkAssignmentRuleset.CanApplyLiveRuleset(out string authorityReason)
                ? fallbackReason
                : authorityReason;
            AddWarningOnce(warnings, rejectionReason);
            WorkAssignmentRuleset.RejectLiveRulesetApplication(rejectionReason);
            return ApplyResult.Failed;
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
