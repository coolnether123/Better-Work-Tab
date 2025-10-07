using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.Features.Rules;

/// <summary>
/// Central class for managing auto-assignment rulesets in Better Work Tab. A ruleset defines priority assignments
/// for pawns based on conditions (e.g., highest skill, passion). Integrates with vanilla work systems:
/// - Orders WorkTypeDefs by naturalPriority (descending, core tasks first).
/// - Applies to map.mapPawns.FreeColonists, respecting workSettings.GetPriority/SetPriority.
/// - Supports reset before apply, randomization among qualifiers, and fallbacks.
/// Usage: Loaded from settings (e.g., "BWT Default"); triggered via "Automatically Assign" button.
/// See linked rules (implements IAssignmentRule) for conditions like BestPawnRule, DoctorRule.
/// </summary>
/// <seealso cref="WorkAssignmentRule"/>
/// <seealso cref="WorkAssignmentParameters"/>
namespace Better_Work_Tab.Features
{
    public class WorkAssignmentRuleset
    {
        /// <summary>
        /// Unused reference to global settings; could be used for dynamic validation (e.g., core toggles).
        /// Currently commented; rules hard-coded in params for simplicity.
        /// </summary>
        //private readonly BetterWorkTabSettings _settings; // Unused; could link to global Settings for validation.

        /// <summary>
        /// Human-readable name of the ruleset (e.g., "Vanilla Starting Pawn", "BWT Default").
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// If true, resets all priorities to 0 before applying rules (clears prior assignments).
        /// Default: true; configurable per ruleset in settings.
        /// </summary>
        public bool ResetBeforeApplying { get; private set; } = true;

        /// <summary>
        /// Internal list of rules derived from parameters; each rule handles application logic.
        /// Built from List<WorkAssignmentParameters> in ctor.
        /// </summary>
        private readonly List<WorkAssignmentRule> _rules = new List<WorkAssignmentRule>();

        /// <summary>
        /// Primary constructor: Creates ruleset from parameters (e.g., priority=1 for core types).
        /// </summary>
        /// <param name="rulesetName">Display name.</param>
        /// <param name="parameters">List of params defining rules (e.g., hasHighestSkill=true).</param>
        /// <param name="resetBeforeApplying">Reset flag.</param>
        public WorkAssignmentRuleset(string rulesetName, List<WorkAssignmentParameters> parameters, bool resetBeforeApplying = true)
        {
            Name = rulesetName;
            ResetBeforeApplying = resetBeforeApplying;

            foreach (var p in parameters)
            {
                _rules.Add(new WorkAssignmentRule(p));
            }
        }

        /// <summary>
        /// Alternate constructor: Directly from pre-built rules (for advanced/custom setups).
        /// </summary>
        public WorkAssignmentRuleset(string rulesetName, List<WorkAssignmentRule> rules, bool resetBeforeApplying = true)
        {
            Name = rulesetName;
            _rules = rules;
            ResetBeforeApplying = resetBeforeApplying;
        }

        /// <summary>
        /// Static utility: Resets all pawn priorities to 0 for a work type (used before rules if ResetBeforeApplying=true).
        /// Applies a single "zero priority" rule to all free colonists.
        /// </summary>
        public static void SetAllToZero()
        {
            new WorkAssignmentRuleset("Reset", new List<WorkAssignmentRule> {
                        new WorkAssignmentRule(new WorkAssignmentParameters(0, allowOverwritingHigherPriority: true))
                    }).ApplyAutoAssignments();
        }

        /// <summary>
        /// Core method: Applies rules to current map's free colonists. Steps:
        /// 1. Enable vanilla work priorities.
        /// 2. Get ordered WorkTypeDefs (by naturalPriority desc, no duplicates).
        /// 3. For each rule: Filter relevant types; apply to pawns (break early if assigned; randomize if multiple).
        /// 4. Fallback if no assignment.
        /// Triggers PawnTable refresh via indirect SetDirty() in UI patches.
        /// </summary>
        public void ApplyAutoAssignments()
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            Find.PlaySettings.useWorkPriorities = true;

            var pawns = map.mapPawns.FreeColonists.ToList();
            if (pawns.Count == 0) return;

            var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading.OrderBy(wt => wt.naturalPriority).Reverse().ToList();
            allWorkTypes.RemoveDuplicates(); // Vanilla def list may have extras from mods.

            foreach (var rule in _rules)
            {
                foreach (var worktype in allWorkTypes)
                {
                    // Skip if rule targets specific WorkTypeDef.
                    if (rule.Parameters.Worktype != null && rule.Parameters.Worktype != worktype)
                        continue;

                    // Skip if ignorable named type missing (e.g., modded types like PatientBedRest).
                    if (!string.IsNullOrEmpty(rule.Parameters.WorktypeNamedIgnoreIfNonexistant))
                    {
                        if (!DefDatabase<WorkTypeDef>.AllDefs.Contains(DefDatabase<WorkTypeDef>.GetNamedSilentFail(rule.Parameters.WorktypeNamedIgnoreIfNonexistant)))
                            continue;
                    }

                    List<Pawn> pawnsForThisWorktype = new List<Pawn>();

                    foreach (var pawn in pawns)
                    {
                        if (pawn.workSettings == null) continue;
                        bool pawnAlreadyAssigned = pawn.workSettings.GetPriority(worktype) > 0;

                        // Apply rule; true if skip remaining pawns (e.g., fully assigned).
                        if (rule.Apply(pawn, pawns, worktype))
                            break;

                        // Track for randomization.
                        if (rule.Parameters.RandomIfMultiple && pawn.workSettings.GetPriority(worktype) > 0 && !pawnAlreadyAssigned)
                            pawnsForThisWorktype.Add(pawn);
                    }

                    // Randomize if multiple qualifiers (vanilla-inspired: InRandomOrder.First).
                    if (rule.Parameters.RandomIfMultiple && pawnsForThisWorktype.Count > 0)
                    {
                        foreach (var p in pawnsForThisWorktype)
                        {
                            //Log.Message($"Resetting {worktype.defName} for {p.NameShortColored} before random assignment.");
                            p.workSettings.SetPriority(worktype, 0);
                        }
                        pawnsForThisWorktype.Where(p => !p.WorkTypeIsDisabled(worktype)).InRandomOrder().First().
                            workSettings.SetPriority(worktype, rule.Parameters.Priority);
                    }

                    // Fallback rule if no assignments.
                    if (rule.Parameters.FailedToApplyFallback != null && !pawns.Any(p => p.workSettings.GetPriority(worktype) > 0))
                    {
                        Log.Message($"No pawn could be assigned to work type: {worktype.defName}. Applying fallback.");
                        foreach (var pawn in pawns)
                            new WorkAssignmentRule(rule.Parameters.FailedToApplyFallback).Apply(pawn, pawns, worktype);
                    }
                }
            }
        }
    }
}
