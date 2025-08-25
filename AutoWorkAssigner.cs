using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab
{
    public static class AutoWorkAssigner
    {
        private const int MIN_PRIORITY = 1;
        private const int MAX_PRIORITY = 4;

        // WorkTypeDef constants for core work types
        private static readonly WorkTypeDef WorkTypeDef_Patient = DefDatabase<WorkTypeDef>.GetNamed("Patient");
        private static readonly WorkTypeDef WorkTypeDef_PatientBedRest = DefDatabase<WorkTypeDef>.GetNamed("PatientBedRest");
        private static readonly WorkTypeDef WorkTypeDef_BasicWorker = DefDatabase<WorkTypeDef>.GetNamed("BasicWorker");


        // This draws the "Auto Assign Work" button in the work table header area to provide easy access to automatic assignment functionality
        public static void DrawButton(Rect headerRect)
        {
            var size = new Vector2(150f, 28f);
            var btn = new Rect(headerRect.xMax - size.x - 6f, headerRect.y + 2f, size.x, size.y);

            if (Widgets.ButtonText(btn, "Auto Assign Work"))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                ApplyAutoAssignments();
            }
        }

        /// <summary>
        /// Applies the auto assignment rules to all pawns in the current map.
        /// </summary>
        public static void ApplyAutoAssignments()
        {
            var map = Find.CurrentMap;
            if (map == null) return;

            // This enables manual priorities mode because the system assigns specific numeric priorities (1-4) rather than just on/off
            Find.PlaySettings.useWorkPriorities = true;

            var pawns = map.mapPawns.FreeColonistsSpawned.ToList();
            if (pawns.Count == 0) return;

            // This caches the work type definitions once to avoid repeated database lookups during assignment
            var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

            // This defines the core work types that should always be assigned priority 1 for survival and basic functionality
            WorkTypeDef[] coreDefs = {
                WorkTypeDefOf.Firefighter,
                WorkTypeDef_Patient,
                WorkTypeDef_PatientBedRest,
                WorkTypeDef_BasicWorker
            };

            // This identifies the best medical pawn(s) by finding the highest medicine skill level across all colonists
            int bestMed = pawns.Any(p => p.skills != null)
                ? pawns.Max(p => p.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0)
                : 0;
            var bestDoctors = new HashSet<Pawn>(
                pawns.Where(p =>
                    p.skills != null &&
                    (p.skills.GetSkill(SkillDefOf.Medicine)?.Level ?? 0) == bestMed));

            // This reads the default starting priority from mod settings to apply to unspecialized work types
            int defaultPri = BetterWorkTabConfig.DefaultStartingPriority();

            foreach (var pawn in pawns)
            {
                if (pawn.workSettings == null) continue;

                // This ensures the pawn's work settings are properly initialized before the system modifies them
                pawn.workSettings.EnableAndInitialize();

                // This creates a snapshot of current priorities to track what the system has changed vs what was already set
                // var before = new Dictionary<WorkTypeDef, int>(allWorkTypes.Count);
                // foreach (var wt in allWorkTypes)
                //     before[wt] = pawn.workSettings.GetPriority(wt);

                // This tracks which work types the rules have touched so the system doesn't override them with defaults
                var touched = new HashSet<WorkTypeDef>();

                // This helper function safely sets work priority while respecting disability and tracking changes
                System.Action<WorkTypeDef, int> setPrioritySafe = (wt, pri) =>
                {
                    if (pawn.WorkTypeIsDisabled(wt)) return;
                    pawn.workSettings.SetPriority(wt, Mathf.Clamp(pri, MIN_PRIORITY, MAX_PRIORITY));
                    touched.Add(wt);
                };

                ApplyCoreWorkTypePriorities(pawn, coreDefs, setPrioritySafe);
                ApplyDoctorPriorities(pawn, bestDoctors, setPrioritySafe);
                ApplyPassionPriorities(pawn, allWorkTypes, setPrioritySafe);
                ApplyChildcarePriorities(pawn, setPrioritySafe, BetterWorkTabConfig.S.rule_ChildcareEnabled, BetterWorkTabConfig.S.rule_ChildcarePriority);
                ApplyDefaultPriorities(pawn, allWorkTypes, touched, defaultPri);
            }
        }

        /// <summary>
        /// Applies highest priority to essential survival work types that every colonist should do when needed.
        /// </summary>
        private static void ApplyCoreWorkTypePriorities(Pawn pawn, IEnumerable<WorkTypeDef> coreDefs, System.Action<WorkTypeDef, int> setPrioritySafe)
        {
            foreach (var def in coreDefs)
            {
                if (!pawn.WorkTypeIsDisabled(def))
                    setPrioritySafe(def, 1);
            }
        }

        /// <summary>
        /// Assigns medical work to the colonist(s) with the highest medicine skill to ensure competent healthcare.
        /// </summary>
        private static void ApplyDoctorPriorities(Pawn pawn, HashSet<Pawn> bestDoctors, System.Action<WorkTypeDef, int> setPrioritySafe)
        {
            if (bestDoctors.Contains(pawn))
            {
                if (!pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
                    setPrioritySafe(WorkTypeDefOf.Doctor, 1);
            }
        }

        /// <summary>
        /// Prioritizes work types where the pawn has burning passion to leverage their natural aptitude.
        /// </summary>
        private static void ApplyPassionPriorities(Pawn pawn, IEnumerable<WorkTypeDef> allWorkTypes, System.Action<WorkTypeDef, int> setPrioritySafe)
        {
            if (pawn.skills != null)
            {
                foreach (var wt in allWorkTypes)
                {
                    if (pawn.WorkTypeIsDisabled(wt)) continue;

                    bool hasBurning = wt.relevantSkills.Any(skill =>
                        pawn.skills.GetSkill(skill).passion == Passion.Major);

                    if (hasBurning)
                        setPrioritySafe(wt, 2);
                }
            }
        }

        /// <summary>
        /// Assigns top childcare priority to pawns who are currently pregnant.
        /// </summary>
        private static void ApplyChildcarePriorities(Pawn pawn, System.Action<WorkTypeDef, int> setPrioritySafe, bool childcareEnabled, int childcarePriority)
        {
            bool isPregnant = pawn.health?.hediffSet?.HasHediff(HediffDefOf.PregnantHuman) ?? false;

            if (isPregnant && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Childcare))
            {
                int pri = childcareEnabled
                    ? childcarePriority
                    : 1;

                setPrioritySafe(WorkTypeDefOf.Childcare, pri);
            }
        }

        /// <summary>
        /// Applies the configured default priority to work types that weren't specifically assigned by the rules.
        /// </summary>
        private static void ApplyDefaultPriorities(Pawn pawn, IEnumerable<WorkTypeDef> allWorkTypes, HashSet<WorkTypeDef> touched, int defaultPriority)
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