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
        private readonly RuleBuilder2Evaluator evaluator = new RuleBuilder2Evaluator();

        public int Apply(RuleBuilder2Ruleset ruleset, out List<string> warnings, bool persistRuleset = true)
        {
            warnings = new List<string>();
            if (ruleset?.Cards == null)
            {
                warnings.Add("No Rule Builder 2.0 ruleset is loaded.");
                return 0;
            }

            RuleBuilder2SleekPriorityTranslation.AddApplyWarningIfNeeded(ruleset, warnings);

            WorkPrioritySystem.SetManualPriorities(true);
            List<Pawn> pawns = RuleBuilder2Evaluator.GetCurrentPawns();
            int changed = 0;

            if (ruleset.ResetBeforeApplying)
            {
                WorkAssignmentRuleset.SetAllToZero();
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

                        if (matched && ApplyCardToPawn(card, pawn, workType, workGiver, currentPriority, warnings))
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

        private static bool ApplyCardToPawn(
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
                return false;
            }

            bool changed = false;

            switch (card.Action.Kind)
            {
                case RuleBuilder2ActionKind.FollowGlobal:
                    warnings.Add("Follow global/default priority is preview-only for now. Clear sub-work overrides from the Work tab if needed.");
                    return false;
                case RuleBuilder2ActionKind.SetTimeSchedule:
                case RuleBuilder2ActionKind.SetSubWorkSchedule:
                    changed |= ApplyBasePriority(pawn, workType, workGiver, targetPriority, currentPriority, warnings);
                    card.Action.EnsureSchedule(targetPriority);
                    TimePriorityTarget scheduleTarget = workGiver == null
                        ? TimePriorityTarget.ForRuntimeWorkType(pawn, workType)
                        : TimePriorityTarget.ForRuntimeWorkGiver(pawn, workType, workGiver);
                    TimePriorityService.SetScheduleSynced(
                        scheduleTarget,
                        card.Action.HourlyPriorities.ToArray(),
                        TimePriorityService.CreateAllHoursPinnedState(),
                        targetPriority);
                    return true;
                case RuleBuilder2ActionKind.Disable:
                case RuleBuilder2ActionKind.SetPriority:
                default:
                    return ApplyBasePriority(pawn, workType, workGiver, targetPriority, currentPriority, warnings);
            }
        }

        private static bool ApplyBasePriority(
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
                return false;
            }

            if (workGiver != null)
            {
                if (SleekWorkTabGateway.SleekCodeRuns)
                {
                    if (!SleekWorkTabGateway.TrySetSleekWorkGiverOverride(pawn, workGiver, targetPriority))
                    {
                        AddWarningOnce(
                            warnings,
                            "Sleek Work Priorities' per-job store was unavailable; this sub-work rule was kept in BWT's fallback store.");
                        WorkGiverReassignmentManager.SetPawnOverrideSynced(
                            pawn.thingIDNumber,
                            workGiver.defName,
                            targetPriority);
                    }
                }
                else
                {
                    WorkGiverReassignmentManager.SetPawnOverrideSynced(
                        pawn.thingIDNumber,
                        workGiver.defName,
                        targetPriority);
                }
            }
            else
            {
                WorkPrioritySystem.SetPriority(pawn.workSettings, workType, targetPriority);
            }

            return true;
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
