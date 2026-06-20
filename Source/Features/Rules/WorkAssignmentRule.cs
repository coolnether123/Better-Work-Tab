using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Better_Work_Tab.Features.Rules.Validators;

namespace Better_Work_Tab.Features.Rules
{
    public class WorkAssignmentRule : IExposable
    {
        public string Name;
        public WorkTypeDef CachedWorktype;
        public WorkAssignmentParameters Parameters;

        private static List<WorkTypeDef> _cachedAllWorkTypes = null;

        public static List<WorkTypeDef> AllWorkTypes
        {
            get
            {
                if (_cachedAllWorkTypes == null)
                {
                    _cachedAllWorkTypes = DefDatabase<WorkTypeDef>
                        .AllDefsListForReading
                        .OrderBy(wt => wt.naturalPriority)
                        .Reverse()
                        .ToList();
                    _cachedAllWorkTypes.RemoveDuplicates();
                }
                return _cachedAllWorkTypes;
            }
        }

        public WorkAssignmentRule() { }

        public WorkAssignmentRule(
            WorkAssignmentParameters parameters,
            WorkTypeDef worktype = null
        )
        {
            CachedWorktype = worktype;
            Parameters = parameters;
            Name = parameters.RuleName;
        }

        public WorkAssignmentRule(
            string name,
            WorkAssignmentParameters parameters,
            WorkTypeDef worktype = null
        )
        {
            CachedWorktype = worktype;
            Parameters = parameters;
            parameters.RuleName = name;
            Name = name;
        }

        public bool Apply(
            Pawn pawn,
            List<Pawn> currentPawns,
            WorkTypeDef worktype = null
        )
        {
            // Determine which worktype this rule applies to
            var assigningWorktype = DetermineWorktype(worktype);
            if (assigningWorktype == null)
            {
                return true;
            }

            // Run validators in sequence
            if (!EligibilityValidator.Validate(pawn, assigningWorktype, Parameters))
                return false;

            if (!PriorityValidator.Validate(pawn, assigningWorktype, Parameters))
                return false;

            if (!SocialValidator.Validate(pawn, Parameters))
                return false;

            if (!BiologicalValidator.Validate(pawn, Parameters))
                return false;

            if (!SkillValidator.Validate(
                pawn, assigningWorktype, currentPawns, Parameters))
                return false;

            var validationResult = AssignmentConstraintsValidator.Validate(
                pawn, assigningWorktype, currentPawns, Parameters);

            if (!validationResult.IsValid)
                return false;

            // All checks passed – assign the priority
            pawn.workSettings.SetPriority(
                assigningWorktype,
                Mathf.Clamp(Parameters.Priority, 0, Mathf.Max(1, BetterWorkTabMod.Settings?.maxPriorityInt ?? 4))
            );

            return validationResult.ShouldSkipRemainingPawns;
        }

        private void EnsureNonWorktypeDefsCached()
        {
            if (Parameters.RequiredTrait == null && !string.IsNullOrEmpty(Parameters.TraitString) && Parameters.TraitDegree != null)
            {
                var traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(Parameters.TraitString);
                if (traitDef != null)
                {
                    Parameters.RequiredTrait = new Tuple<TraitDef, int>(traitDef, (int)Parameters.TraitDegree);
                }
            }

#if !v1_3 && !v1_2
            if (Parameters.Xenotype == null && !string.IsNullOrEmpty(Parameters.XenotypeString))
            {
                Parameters.Xenotype = DefDatabase<XenotypeDef>.GetNamedSilentFail(Parameters.XenotypeString);
            }
#endif
        }

        private WorkTypeDef DetermineWorktype(WorkTypeDef callSiteWorktype)
        {
            EnsureNonWorktypeDefsCached();

            bool hasExplicitWorktypeString = !string.IsNullOrEmpty(Parameters.WorktypeString);

            if (Parameters.Worktype == null && hasExplicitWorktypeString)
            {
                Parameters.Worktype = DefDatabase<WorkTypeDef>.GetNamedSilentFail(Parameters.WorktypeString);
                if (Parameters.Worktype == null && Parameters.IgnoreIfWorktypeNonexistent)
                {
                    return null;
                }
            }

            WorkTypeDef resolved = Parameters.Worktype ?? CachedWorktype ?? callSiteWorktype;

            if (resolved == null && hasExplicitWorktypeString)
            {
            int key = $"BWTMissingWorktype_{Parameters.WorktypeString}".GetHashCode();
#if v1_2
                LogCompat.WarningOnce($"[BWT] Unable to resolve worktype \"{Parameters.WorktypeString}\" for rule \"{Name}\".", key);
#elif v1_3
                Log.Warning($"[BWT] Unable to resolve worktype \"{Parameters.WorktypeString}\" for rule \"{Name}\".");
#else
                Log.WarningOnce($"[BWT] Unable to resolve worktype \"{Parameters.WorktypeString}\" for rule \"{Name}\".", key);
#endif
            }
            else if (resolved != null && hasExplicitWorktypeString && Parameters.WorktypeString != resolved.defName)
            {
                Parameters.WorktypeString = resolved.defName;
            }

            return resolved;
        }

        public WorkAssignmentRule Copy()
        {
            return new WorkAssignmentRule(
                Name + " (Copy)",
                Parameters.Copy(),
                CachedWorktype
            );
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Name, "Name");
            Scribe_Defs.Look(ref CachedWorktype, "Worktype");
            Scribe_Deep.Look(ref Parameters, "Parameters");
        }
    }
}
