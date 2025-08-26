using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features
{
    public class AutoWorkAssigner
    {
        private const int MIN_PRIORITY = 1;
        private const int MAX_PRIORITY = 4;

        private readonly BetterWorkTabSettings _settings;

        public AutoWorkAssigner(BetterWorkTabSettings settings)
        {
            _settings = settings;
        }

        public void ApplyAutoAssignments()
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            Find.PlaySettings.useWorkPriorities = true;

            var pawns = map.mapPawns.FreeColonistsSpawned.ToList();
            if (pawns.Count == 0) return;

            var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

            var coreWorkTypes = new List<WorkTypeDef>();
            if (_settings.core_Firefighter) coreWorkTypes.Add(WorkTypeDefOf.Firefighter);
            if (_settings.core_Patient) coreWorkTypes.Add(DefDatabase<WorkTypeDef>.GetNamed("Patient"));
            if (_settings.core_BedRest) coreWorkTypes.Add(DefDatabase<WorkTypeDef>.GetNamed("PatientBedRest"));
            if (_settings.core_Basic) coreWorkTypes.Add(DefDatabase<WorkTypeDef>.GetNamed("BasicWorker"));

            int bestMed = pawns.Any(p => p.skills != null)
                ? pawns.Max(p => p.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0)
                : 0;
            var bestDoctors = new HashSet<Pawn>(
                pawns.Where(p =>
                    p.skills != null &&
                    (p.skills.GetSkill(SkillDefOf.Medicine)?.Level ?? 0) == bestMed));

            int defaultPri = _settings.defaultStartingPriority;

            foreach (var pawn in pawns)
            {
                if (pawn.workSettings == null) continue;

                pawn.workSettings.EnableAndInitialize();

                var touched = new HashSet<WorkTypeDef>();

                System.Action<WorkTypeDef, int> setPrioritySafe = (wt, pri) =>
                {
                    if (pawn.WorkTypeIsDisabled(wt)) return;
                    pawn.workSettings.SetPriority(wt, Mathf.Clamp(pri, MIN_PRIORITY, MAX_PRIORITY));
                    touched.Add(wt);
                };

                ApplyCoreWorkTypePriorities(pawn, coreWorkTypes, setPrioritySafe);
                ApplyDoctorPriorities(pawn, bestDoctors, setPrioritySafe);
                ApplyPassionPriorities(pawn, allWorkTypes, setPrioritySafe);
                ApplyChildcarePriorities(pawn, setPrioritySafe, _settings.rule_ChildcareEnabled, _settings.rule_ChildcarePriority);
                ApplyDefaultPriorities(pawn, allWorkTypes, touched, defaultPri);
            }
        }

        private void ApplyCoreWorkTypePriorities(Pawn pawn, IEnumerable<WorkTypeDef> coreWorkTypes, System.Action<WorkTypeDef, int> setPrioritySafe)
        {
            if (!_settings.rule_CoreAlwaysPriorityEnabled) return;

            foreach (var def in coreWorkTypes)
            {
                if (!pawn.WorkTypeIsDisabled(def))
                    setPrioritySafe(def, _settings.rule_CoreAlwaysPriorityValue);
            }
        }

        private void ApplyDoctorPriorities(Pawn pawn, HashSet<Pawn> bestDoctors, System.Action<WorkTypeDef, int> setPrioritySafe)
        {
            if (!_settings.rule_BestDoctorsEnabled) return;

            if (bestDoctors.Contains(pawn))
            {
                if (!pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
                    setPrioritySafe(WorkTypeDefOf.Doctor, _settings.rule_BestDoctorsPriority);
            }
        }

        private void ApplyPassionPriorities(Pawn pawn, IEnumerable<WorkTypeDef> allWorkTypes, System.Action<WorkTypeDef, int> setPrioritySafe)
        {
            if (!_settings.rule_PassionOverrideEnabled || pawn.skills == null) return;

            foreach (var wt in allWorkTypes)
            {
                if (pawn.WorkTypeIsDisabled(wt) || !wt.relevantSkills.Any()) continue;

                Passion passion = wt.relevantSkills.Max(skill => pawn.skills.GetSkill(skill).passion);

                if (passion == Passion.Major && _settings.passion_Major > 0)
                {
                    setPrioritySafe(wt, _settings.passion_Major);
                }
                else if (passion == Passion.Minor && _settings.passion_Minor > 0)
                {
                    setPrioritySafe(wt, _settings.passion_Minor);
                }
            }
        }

        private void ApplyChildcarePriorities(Pawn pawn, System.Action<WorkTypeDef, int> setPrioritySafe, bool childcareEnabled, int childcarePriority)
        {
            if (!childcareEnabled) return;

            bool isPregnant = pawn.health?.hediffSet?.HasHediff(HediffDefOf.PregnantHuman) ?? false;

            if (isPregnant && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Childcare))
            {
                setPrioritySafe(WorkTypeDefOf.Childcare, childcarePriority);
            }
        }

        private void ApplyDefaultPriorities(Pawn pawn, IEnumerable<WorkTypeDef> allWorkTypes, HashSet<WorkTypeDef> touched, int defaultPriority)
        {
            foreach (var wt in allWorkTypes)
            {
                if (pawn.WorkTypeIsDisabled(wt)) continue;
                if (touched.Contains(wt)) continue;

                if (defaultPriority >= MIN_PRIORITY && defaultPriority <= MAX_PRIORITY)
                {
                    pawn.workSettings.SetPriority(wt, defaultPriority);
                }
            }
        }
    }
}
