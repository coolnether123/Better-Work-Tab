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

        public static List<WorkTypeDef> AllWorkTypes
        {
            get
            {
                var all = DefDatabase<WorkTypeDef>
                    .AllDefsListForReading
                    .OrderBy(wt => wt.naturalPriority)
                    .Reverse()
                    .ToList();
                all.RemoveDuplicates();
                return all;
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

            bool skipRemaining = false;
            if (!AssignmentConstraintsValidator.Validate(
                pawn, assigningWorktype, currentPawns, Parameters, ref skipRemaining))
                return false;

            // All checks passed – assign the priority
            pawn.workSettings.SetPriority(
                assigningWorktype,
                Mathf.Clamp(Parameters.Priority, 0, 4)
            );

            return skipRemaining;
        }

        private WorkTypeDef DetermineWorktype(WorkTypeDef callSiteWorktype)
        {
            return Parameters.Worktype ?? CachedWorktype ?? callSiteWorktype;
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