using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab
{
    // This contains all configurable settings for the Better Work Tab mod with reasonable defaults
    public class BetterWorkTabSettings : ModSettings
    {
        public BetterWorkTabSettings() { }

        // This provides master toggles for major features so users can disable parts they don't want
        public bool enableSkillOverlayFeature = true;
        public bool enableAutoAssignFeature = true;

        // This sets the baseline priority for work types not handled by specific rules (0 = leave unchanged)
        public int defaultStartingPriority = 0;

        // This controls the "always priority X" rule for essential survival work types
        public bool rule_CoreAlwaysPriorityEnabled = true;
        public int rule_CoreAlwaysPriorityValue = 1; // 1..4
        public string additionalAlwaysX_CSV = "";

        // This provides individual toggles for each built-in core work type in case users want to customize
        public bool core_Firefighter = true;
        public bool core_Patient = true;
        public bool core_BedRest = true;
        public bool core_Basic = true;

        // This controls automatic assignment of medical work to the most skilled colonists
        public bool rule_BestDoctorsEnabled = true;
        public int rule_BestDoctorsPriority = 1;

        // This allows passion levels to override normal priority assignment for specialized roles
        public bool rule_PassionOverrideEnabled = true;
        public int passion_None = 0;   // No passion gets default treatment
        public int passion_Minor = 3;  // Minor passion gets medium priority
        public int passion_Major = 2;  // Major passion gets high priority (but not highest to allow core work)

        // This handles childcare assignment for parents with young children on the map
        public bool rule_ChildcareEnabled = true;
        public int rule_ChildcarePriority = 1;

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref enableSkillOverlayFeature, "enableSkillOverlayFeature", true);
            Scribe_Values.Look(ref enableAutoAssignFeature, "enableAutoAssignFeature", true);

            Scribe_Values.Look(ref defaultStartingPriority, "defaultStartingPriority", 0);

            Scribe_Values.Look(ref rule_CoreAlwaysPriorityEnabled, "rule_CoreAlwaysPriorityEnabled", true);
            Scribe_Values.Look(ref rule_CoreAlwaysPriorityValue, "rule_CoreAlwaysPriorityValue", 1);
            Scribe_Values.Look(ref additionalAlwaysX_CSV, "additionalAlwaysX_CSV", "");

            Scribe_Values.Look(ref core_Firefighter, "core_Firefighter", true);
            Scribe_Values.Look(ref core_Patient, "core_Patient", true);
            Scribe_Values.Look(ref core_BedRest, "core_BedRest", true);
            Scribe_Values.Look(ref core_Basic, "core_Basic", true);

            Scribe_Values.Look(ref rule_BestDoctorsEnabled, "rule_BestDoctorsEnabled", true);
            Scribe_Values.Look(ref rule_BestDoctorsPriority, "rule_BestDoctorsPriority", 1);

            Scribe_Values.Look(ref rule_PassionOverrideEnabled, "rule_PassionOverrideEnabled", true);
            Scribe_Values.Look(ref passion_None, "passion_None", 0);
            Scribe_Values.Look(ref passion_Minor, "passion_Minor", 3);
            Scribe_Values.Look(ref passion_Major, "passion_Major", 2);

            Scribe_Values.Look(ref rule_ChildcareEnabled, "rule_ChildcareEnabled", true);
            Scribe_Values.Look(ref rule_ChildcarePriority, "rule_ChildcarePriority", 1);
        }

        // This parses the CSV string of additional work type def names and resolves them to actual WorkTypeDef objects
        public IEnumerable<WorkTypeDef> AdditionalAlwaysDefs()
        {
            if (string.IsNullOrWhiteSpace(additionalAlwaysX_CSV))
                yield break;

            var parts = additionalAlwaysX_CSV.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in parts)
            {
                var key = raw.Trim();
                if (key.Length == 0) continue;

                WorkTypeDef def = null;
                try { def = DefDatabase<WorkTypeDef>.GetNamedSilentFail(key); } catch { }
                if (def != null) yield return def;
            }
        }
    }

    // This provides static helper methods for other parts of the mod to access configuration values safely
    public static class BetterWorkTabConfig
    {
        public static BetterWorkTabSettings S { get { return BetterWorkTabMod.Settings; } }

        // This converts a pawn's passion level into a priority number according to the configured mapping
        public static int PriorityFromPassion(Passion passion)
        {
            if (!S.rule_PassionOverrideEnabled) return 0;

            int v;
            switch (passion)
            {
                case Passion.None: v = S.passion_None; break;
                case Passion.Minor: v = S.passion_Minor; break;
                case Passion.Major: v = S.passion_Major; break;
                default: v = 0; break;
            }
            return (v >= 1 && v <= 4) ? v : 0;
        }

        // This returns the configured default priority, validating it's in the valid range (1-4) or 0 for "don't change"
        public static int DefaultStartingPriority()
        {
            return (S.defaultStartingPriority >= 1 && S.defaultStartingPriority <= 4)
                ? S.defaultStartingPriority
                : 0;
        }

        // This builds the complete list of work types that should get the core "always priority X" treatment
        public static IEnumerable<WorkTypeDef> CoreAlwaysDefsResolved()
        {
            if (!S.rule_CoreAlwaysPriorityEnabled) yield break;

            if (S.core_Firefighter) yield return WorkTypeDefOf.Firefighter;
            if (S.core_Patient) yield return DefDatabase<WorkTypeDef>.GetNamedSilentFail("Patient");
            if (S.core_BedRest) yield return DefDatabase<WorkTypeDef>.GetNamedSilentFail("PatientBedRest");
            if (S.core_Basic) yield return DefDatabase<WorkTypeDef>.GetNamedSilentFail("BasicWorker");

            // This adds any custom work types the user specified in the CSV field
            foreach (var extra in S.AdditionalAlwaysDefs())
                if (extra != null) yield return extra;
        }
    }
}