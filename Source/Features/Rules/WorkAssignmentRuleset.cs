using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.Features.Rules;

namespace Better_Work_Tab.Features
{
    /// <summary>
    /// This class handles the automatic work assignment process for pawns based on a set of defined rules.
    /// </summary>
    public class WorkAssignmentRuleset : IExposable
    {
        //private readonly BetterWorkTabSettings _settings;

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

        public static void SetAllToZero()
        {
            new WorkAssignmentRuleset("Reset", new List<WorkAssignmentRule> {
                        new WorkAssignmentRule(new WorkAssignmentParameters("Reset", 0, allowOverwritingHigherPriority: true))
                    }).ApplyAutoAssignments();
        }

        /// <summary>
        /// Applies all configured auto-assignment rules to the free colonists on the current map.
        /// </summary>
        public void ApplyAutoAssignments()
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            Find.PlaySettings.useWorkPriorities = true;

            var pawns = map.mapPawns.FreeColonists.ToList();
            if (pawns.Count == 0) return;

            ApplyToList(pawns);
        }

        /// <summary>
        /// Applies all rules to an explicit pawn list without touching map or play settings.
        /// Used by the preview calculator to simulate assignments without side effects.
        /// </summary>
        public void ApplyToList(List<Pawn> pawns)
        {
            if (pawns == null || pawns.Count == 0) return;

            var allWorkTypes = CachedWorkTypes;

            foreach (var rule in GetRulesInPriorityOrder())
            {
                foreach (var worktype in allWorkTypes)
                {

                    if (rule.Parameters.Worktype != null)
                    {
                        if (rule.Parameters.Worktype != worktype)
                            continue;
                    }
                    if (rule.Parameters.IgnoreIfWorktypeNonexistent)
                    {
                        if (!DefDatabase<WorkTypeDef>.AllDefs.Contains(DefDatabase<WorkTypeDef>.GetNamedSilentFail(rule.Parameters.Worktype?.defName ?? rule.Parameters.WorktypeString)))
                            continue;
                    }
                    List<Pawn> pawnsForThisWorktype = new List<Pawn>();

                    foreach (var pawn in pawns)
                    {
                        if (pawn.workSettings == null) continue;
                        int originalPriority = pawn.workSettings.GetPriority(worktype);
                        bool pawnAlreadyAssigned = originalPriority > 0;
                        if (rule.Apply(pawn, pawns, worktype))
                            break;
                        if (rule.Parameters.RandomIfMultiple)
                        {
                            int updatedPriority = pawn.workSettings.GetPriority(worktype);
                            if (updatedPriority > 0 && !pawnAlreadyAssigned)
                                pawnsForThisWorktype.Add(pawn);
                        }
                    }

                    if (rule.Parameters.RandomIfMultiple && pawnsForThisWorktype.Count > 0)
                    {
                        foreach (var p in pawnsForThisWorktype)
                        {
                            BetterWorkTabMod.DebugLog($"Resetting {worktype.defName} for {p.NameShortColored} before random assignment.", DebugFeature.Rules);
                            p.workSettings.SetPriority(worktype, 0);
                        }
                        List<Pawn> eligiblePawns = new List<Pawn>();
                        foreach (var pawn in pawnsForThisWorktype)
                        {
                            if (!pawn.WorkTypeIsDisabled(worktype))
                                eligiblePawns.Add(pawn);
                        }

                        if (eligiblePawns.Count > 0)
                            eligiblePawns.RandomElement().workSettings.SetPriority(worktype, rule.Parameters.Priority);
                    }
                }
            }
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
        /// Ensures the priority order list exists and contains all priorities 0..MaxPriority (default 4).
        /// </summary>
        public void EnsurePriorityOrder(int maxPriority = 4)
        {
            if (PriorityOrder == null || PriorityOrder.Count == 0)
            {
                PriorityOrder = Enumerable.Range(1, maxPriority).ToList();
                if (!PriorityOrder.Contains(0))
                {
                    PriorityOrder.Add(0);
                }
            }

            for (int p = 0; p <= maxPriority; p++)
            {
                if (!PriorityOrder.Contains(p))
                {
                    PriorityOrder.Add(p);
                }
            }
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
