using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Foundation.GameState;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.Workloads;

namespace Better_Work_Tab.Features
{
    /// <summary>
    /// This class handles the automatic work assignment process for pawns based on a set of defined rules.
    /// </summary>
    public class WorkAssignmentRuleset : IExposable
    {
        //private readonly BetterWorkTabSettings _settings;

        internal const string LiveRulesetApplicationBlockedReason =
            "Ruleset application is blocked while a V2 workload preview is active.";

        internal const string LiveRulesetApplicationAuthorityBlockedReason =
            "Ruleset application requires coherent Better Work Tab priority authority. Better Work Tab is read-only while an external priority authority is active or authority is changing.";

        internal const string LiveRulesetManualPriorityBlockedReason =
            "Ruleset application could not enable manual work priorities.";

        internal const string LiveRulesetPriorityWriteFailedReason =
            "Ruleset application could not write a work priority.";

        internal static bool CanApplyLiveRuleset(out string rejectionReason)
        {
            if (WorkTabEffectiveStateRuntime.IsPreviewActive ||
                WorkloadGateway.IsV2PreviewSessionActive)
            {
                rejectionReason = LiveRulesetApplicationBlockedReason;
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.ParentPriority,
                    rejectionReason);
                return false;
            }

            if (!PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                rejectionReason = LiveRulesetApplicationAuthorityBlockedReason;
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.ParentPriority,
                    rejectionReason);
                return false;
            }

            rejectionReason = null;
            return true;
        }

        internal static void RejectLiveRulesetApplication(string rejectionReason)
        {
            if (!rejectionReason.NullOrEmpty())
            {
                Messages.Message(rejectionReason, MessageTypeDefOf.RejectInput, false);
            }
        }

        internal static void RejectLiveRulesetWriteFailure()
        {
            string rejectionReason = CanApplyLiveRuleset(out string authorityReason)
                ? LiveRulesetPriorityWriteFailedReason
                : authorityReason;
            RejectLiveRulesetApplication(rejectionReason);
        }

        public string Name;
        public bool ResetBeforeApplying = true;
        public List<WorkAssignmentRule> Rules = new List<WorkAssignmentRule>();
        public bool IsDefault = false;
        public List<int> PriorityOrder = new List<int>();
        private static List<WorkTypeDef> cachedWorkTypes;

        private static List<WorkTypeDef> CachedWorkTypes
        {
            get
            {
                if (cachedWorkTypes == null || cachedWorkTypes.Count == 0)
                {
                    cachedWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading
                        .OrderByDescending(wt => wt.naturalPriority)
                        .ToList();
                    cachedWorkTypes.RemoveDuplicates();
                }

                return cachedWorkTypes;
            }
        }
        public WorkAssignmentRuleset() { }

        public WorkAssignmentRuleset(string rulesetName, List<WorkAssignmentParameters> parameters, bool resetBeforeApplying = true, bool isDefault = false)
        {
            Name = rulesetName;
            ResetBeforeApplying = resetBeforeApplying;
            EnsurePriorityOrder();

            foreach (var p in parameters)
            {
                Rules.Add(new WorkAssignmentRule(p));
            }
            IsDefault = isDefault;
        }

        public WorkAssignmentRuleset(string rulesetName, List<WorkAssignmentRule> rules, bool resetBeforeApplying = true, bool isDefault = false)
        {
            Name = rulesetName;
            Rules = rules;
            ResetBeforeApplying = resetBeforeApplying;
            IsDefault = isDefault;
            EnsurePriorityOrder();
        }

        /// <summary>
        /// Applies all configured auto-assignment rules to the free colonists on the current map.
        /// </summary>
        public bool ApplyAutoAssignments()
        {
            if (!CanApplyLiveRuleset(out string rejectionReason))
            {
                RejectLiveRulesetApplication(rejectionReason);
                return false;
            }

            var map = Find.CurrentMap;
            if (map == null) return false;

            var pawns = map.mapPawns.FreeColonists.ToList();
            var allWorkTypes = CachedWorkTypes;
            WorkTabAtomicMutationPlan mutation = WorkTabAtomicMutationPlan.Capture(pawns, allWorkTypes);
            mutation.RequiresManualPriorities = true;

            using (new RuleApplicationPlanningScope(mutation))
            {
                if (ResetBeforeApplying)
                {
                    foreach (Pawn pawn in pawns)
                    {
                        foreach (WorkTypeDef workType in allWorkTypes)
                        {
                            mutation.SetPriority(pawn, workType, WorkPrioritySystem.DisabledPriority);
                        }
                    }
                }

                foreach (var rule in GetRulesInPriorityOrder())
                {
                    foreach (var worktype in allWorkTypes)
                    {

                        if (rule.Parameters.Worktype != null)
                        {

                            //if there's a rule that applies to only one worktype, skip all others.
                            if (rule.Parameters.Worktype != worktype)
                                continue;
                        }
                        if (rule.Parameters.IgnoreIfWorktypeNonexistent)
                        {
                            //if there's a rule that applies to only one ignorable worktype, skip all others.
                            if (!DefDatabase<WorkTypeDef>.AllDefs.Contains(DefDatabase<WorkTypeDef>.GetNamedSilentFail(rule.Parameters.Worktype?.defName ?? rule.Parameters.WorktypeString)))
                                continue;
                        }
                        List<Pawn> pawnsForThisWorktype = new List<Pawn>();

                    //Log.Message($"Auto-assigning work type: {worktype.defName}");
                        foreach (var pawn in pawns)
                        {
                            if (pawn.workSettings == null) continue;
                            int originalPriority = RuleApplicationPlanningScope.GetPriority(pawn, worktype);
                            bool pawnAlreadyAssigned = originalPriority > 0;
                        //// Apply all rules
                            bool shouldSkipRemainingPawns = rule.Apply(
                                pawn,
                                pawns,
                                worktype,
                                out bool mutationFailed);
                            if (mutationFailed)
                            {
                                RejectLiveRulesetWriteFailure();
                                return false;
                            }

                            if (shouldSkipRemainingPawns)
                            {
                            //Log.Message("assigned " + worktype.defName +" to " + pawn.NameShortColored +". Skipping remaining pawns.");
                            //Apply returns true if the rest of the pawns should be skipped for this worktype
                                break;
                            }
                            if (rule.Parameters.RandomIfMultiple)
                            {
                                int updatedPriority = RuleApplicationPlanningScope.GetPriority(pawn, worktype);
                                if (updatedPriority > 0 && !pawnAlreadyAssigned)
                                {
                                    //this was assigned. add to list for potential randomization later.
                                    pawnsForThisWorktype.Add(pawn);
                                }
                            }
                        }

                        if (rule.Parameters.RandomIfMultiple && pawnsForThisWorktype.Count > 0)
                        {
                        //copy the list so we can sort it
                        //filter to only those with a priority that has been set

                        //reset before reassigning for randomization
                            foreach (var p in pawnsForThisWorktype)
                            {
                            BetterWorkTabMod.DebugLog($"Resetting {worktype.defName} for {p.NameShortColored} before random assignment.", DebugFeature.Rules);
                            //if (p.workSettings.GetPriority(worktype) == rule.Parameters.Priority)
                                if (!RuleApplicationPlanningScope.SetPriority(
                                        p,
                                        worktype,
                                        WorkPrioritySystem.DisabledPriority))
                                {
                                    RejectLiveRulesetWriteFailure();
                                    return false;
                                }
                            }
                            // Mirrors vanilla RimWorld's selection logic for picking one pawn among eligible candidates.
                            List<Pawn> eligiblePawns = new List<Pawn>();
                            foreach (var pawn in pawnsForThisWorktype)
                            {
                                if (!pawn.WorkTypeIsDisabled(worktype))
                                {
                                    eligiblePawns.Add(pawn);
                                }
                            }

                            if (eligiblePawns.Count > 0 &&
                                !RuleApplicationPlanningScope.SetPriority(
                                    eligiblePawns.RandomElement(),
                                    worktype,
                                    rule.Parameters.Priority))
                            {
                                RejectLiveRulesetWriteFailure();
                                return false;
                            }
                        }

                    //if (rule.Parameters.FailedToApplyFallback != null && !pawns.Where(p => { return p.workSettings.GetPriority(worktype) > 0; }).Any())
                    //{
                    //    Log.Message($"No pawn could be assigned to work type: {worktype.defName}. Applying fallback.");
                    //    foreach (var pawn in pawns)
                    //        new WorkAssignmentRule(rule.Parameters.FailedToApplyFallback).Apply(pawn, pawns, worktype);
                    //}

                    }
                }
            }

            WorkTabApplicationResult result = WorkTabGameRoots.For(Current.Game)?.Application?
                .ApplyAtomicMutationPlan(mutation) ??
                WorkTabApplicationResult.Rejected(LiveRulesetPriorityWriteFailedReason, default);
            if (!result.Accepted)
            {
                RejectLiveRulesetApplication(result.Reason ?? LiveRulesetPriorityWriteFailedReason);
            }
            return result.Accepted;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref IsDefault, "IsDefault", false);
            Scribe_Values.Look(ref Name, "Name");
            Scribe_Values.Look(ref ResetBeforeApplying, "ResetBeforeApplying");
            Scribe_Collections.Look(ref Rules, "Rules", LookMode.Deep);
            Scribe_Collections.Look(ref PriorityOrder, "PriorityOrder", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
            {
                EnsurePriorityOrder();
            }
        }

        public WorkAssignmentRuleset Copy()
        {
            //                                                                                              VVV Never let this be true for a copy because it will be impossible to delete!
            var copy = new WorkAssignmentRuleset((Name + " (Copy)"), Rules.ListFullCopy(), ResetBeforeApplying, false);
            copy.PriorityOrder = PriorityOrder?.ToList() ?? new List<int>();
            copy.EnsurePriorityOrder();
            return copy;
        }

        /// <summary>
        /// Ensures the priority order list exists and contains all priorities 0..MaxPriority.
        /// Disabled priority 0 is always last so extended enabled priorities run before reset/disabled rules.
        /// </summary>
        public void EnsurePriorityOrder(int maxPriority = 0)
        {
            maxPriority = maxPriority <= 0
                ? WorkPrioritySystem.GetRequestableMaxPriority()
                : WorkPrioritySystem.NormalizeMaxPriority(maxPriority);

            if (PriorityOrder == null || PriorityOrder.Count == 0)
            {
                PriorityOrder = BuildDefaultPriorityOrder(maxPriority);
                return;
            }

            var normalized = new List<int>(maxPriority + 1);
            var seen = new HashSet<int>();

            for (int i = 0; i < PriorityOrder.Count; i++)
            {
                int priority = WorkPrioritySystem.ClampPriority(PriorityOrder[i], maxPriority);
                if (priority == 0 || !seen.Add(priority))
                {
                    continue;
                }

                normalized.Add(priority);
            }

            for (int priority = 1; priority <= maxPriority; priority++)
            {
                if (seen.Add(priority))
                {
                    normalized.Add(priority);
                }
            }

            normalized.Add(0);
            PriorityOrder = normalized;
        }

        private static List<int> BuildDefaultPriorityOrder(int maxPriority)
        {
            var order = Enumerable.Range(1, maxPriority).ToList();
            order.Add(0);
            return order;
        }

        /// <summary>
        /// Yields rules grouped in the current priority order, preserving their original relative order.
        /// </summary>
        private IEnumerable<WorkAssignmentRule> GetRulesInPriorityOrder()
        {
            EnsurePriorityOrder();
            var priorityIndex = PriorityOrder
                .Select((p, idx) => (priority: p, idx))
                .ToDictionary(x => x.priority, x => x.idx);

            // Preserve original order within each priority bucket.
            var indexedRules = Rules.Select((rule, idx) => (rule, idx)).ToList();

            foreach (var p in PriorityOrder)
            {
                foreach (var entry in indexedRules.Where(r => (r.rule?.Parameters?.Priority ?? -1) == p)
                                                  .OrderBy(r => r.idx))
                {
                    yield return entry.rule;
                }
            }

            // Any rules with priorities not present in the order list are appended at the end in original order.
            foreach (var entry in indexedRules.Where(r => !priorityIndex.ContainsKey(r.rule?.Parameters?.Priority ?? -1))
                                              .OrderBy(r => r.idx))
            {
                yield return entry.rule;
            }
        }
    }
}
