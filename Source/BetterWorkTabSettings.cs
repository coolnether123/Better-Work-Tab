using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
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

        // This rule ensures at least one colonist is assigned to a specific work type at a given priority
        public Dictionary<WorkTypeDef, int> rule_AlwaysHaveOneByWorkType = new Dictionary<WorkTypeDef, int>();

        // This rule assigns all available colonists to a specific work type at a given priority
        public Dictionary<WorkTypeDef, int> rule_AlwaysAssignAllByWorkType = new Dictionary<WorkTypeDef, int>();

        //These colors are used on skill numbers in the work tab
        public Color Color_VeryLowSkill = new Color(0.82f, 0.25f, 0.25f);
        public Color Color_LowSkill = new Color(0.95f, 0.75f, 0.20f);
        public Color Color_GoodLowSkill = new Color(0.95f, 0.95f, 0.95f);
        public Color Color_ExcellentSkill = new Color(0.35f, 0.85f, 0.35f);

        // These control which pawn/worktype highlights are shown on the work tab
        public bool ShowPawnAndWorktypeHighlights = true; //disable all highlights
        public bool ShowCursorPawnAndWorktypeHighlight = true; //disable cursor highlight
        public bool ShowFloatMenuPawnAndWorktypeHighlight = true; //disable higlight open-from-float-menu highlight
        public bool DoSelectedPawnHighlight = true;

        // This allows custom color for mouse hover highlight instead of reusing cursor highlight color
        public bool UseCustomMouseHoverHighlight = false;

        // These colors are used for pawn/worktype highlights on the work tab
        public Color Color_CursorHighlight = new Color(0.737f, 0.737f, 0.114f, 0.5f); // Yellow with 50% alpha
        public Color Color_FloatMenuHighlight = new Color(0.114f, 0.737f, 0.737f, 0.5f);// Blue with 50% alpha
        public Color Color_CustomMouseHighlight = new Color(0.737f, 0.737f, 0.114f, 0.25f); // Yellow with 25% alpha
        public Color Color_CustomSimilarWorktypeHighlight = new Color(0.737f, 0.737f, 0.114f, 0.125f); // Yellow with 25% alpha
        public Color Color_MouseHoverHighlight // This returns either the custom color or a halved alpha version of the cursor color
        {
            get 
            {
                Color halvedAlpha = Color_CursorHighlight;
                halvedAlpha.a = halvedAlpha.a / 2f;
                return UseCustomMouseHoverHighlight ? Color_CustomMouseHighlight : halvedAlpha; 
            }
        }
        public Color Color_SimilarWorktypeMouseOver
        {
            get
            {
                Color halvedAlpha = Color_CursorHighlight;
                halvedAlpha.a = halvedAlpha.a / 2f;
                return UseCustomMouseHoverHighlight ? Color_CustomSimilarWorktypeHighlight: halvedAlpha;
            }
        }

        // This color is used to indicate a pawn is incapable of a work type due to health conditions. Vanilla red is Color(1,0.3,0.3)
        public Color Color_IncapableBecauseOfCapacities = new Color(1f, 0.3f, 0.3f); // Red

        //This color is used to indicate the best pawn for a skill in the work tab
        public Color Color_BestPawnForSkillSquare = new Color(0.35f, 0.85f, 0.35f);

        // These control when various UI elements are shown on the work tab
        public enum ShowUIMode { Always, Never, Shifted, Unshifted}
        public ShowUIMode ShowUIMode_ShowSmallSkillNumbers = ShowUIMode.Always;
        public ShowUIMode ShowUIMode_ShowPawnForSkillSquare = ShowUIMode.Shifted;



        // Temporary storage for dictionary data to avoid DefOf issues during loading
        private List<string> tempAlwaysHaveOneKeys = new List<string>();
        private List<int> tempAlwaysHaveOneValues = new List<int>();
        private List<string> tempAlwaysAssignAllKeys = new List<string>();
        private List<int> tempAlwaysAssignAllValues = new List<int>();





        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref enableSkillOverlayFeature, "enableSkillOverlayFeature", true);
            Scribe_Values.Look(ref enableAutoAssignFeature, "enableAutoAssignFeature", true);

            Scribe_Values.Look(ref defaultStartingPriority, "defaultStartingPriority", 0);

            Scribe_Values.Look(ref rule_CoreAlwaysPriorityEnabled, "rule_CoreAlwaysPriorityEnabled", true);
            Scribe_Values.Look(ref rule_CoreAlwaysPriorityValue, "rule_CoreAlwaysPriorityValue", 1);

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

            // Handle dictionaries using string keys to avoid DefOf issues during loading
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Convert dictionaries to string lists for saving
                tempAlwaysHaveOneKeys = rule_AlwaysHaveOneByWorkType.Keys.Where(k => k != null).Select(k => k.defName).ToList();
                tempAlwaysHaveOneValues = rule_AlwaysHaveOneByWorkType.Where(kvp => kvp.Key != null).Select(kvp => kvp.Value).ToList();
                tempAlwaysAssignAllKeys = rule_AlwaysAssignAllByWorkType.Keys.Where(k => k != null).Select(k => k.defName).ToList();
                tempAlwaysAssignAllValues = rule_AlwaysAssignAllByWorkType.Where(kvp => kvp.Key != null).Select(kvp => kvp.Value).ToList();
            }

            // Save/load as string lists to avoid DefOf issues
            Scribe_Collections.Look(ref tempAlwaysHaveOneKeys, "rule_AlwaysHaveOneByWorkType_Keys");
            Scribe_Collections.Look(ref tempAlwaysHaveOneValues, "rule_AlwaysHaveOneByWorkType_Values");
            Scribe_Collections.Look(ref tempAlwaysAssignAllKeys, "rule_AlwaysAssignAllByWorkType_Keys");
            Scribe_Collections.Look(ref tempAlwaysAssignAllValues, "rule_AlwaysAssignAllByWorkType_Values");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Initialize dictionaries
                if (rule_AlwaysHaveOneByWorkType == null)
                    rule_AlwaysHaveOneByWorkType = new Dictionary<WorkTypeDef, int>();
                if (rule_AlwaysAssignAllByWorkType == null)
                    rule_AlwaysAssignAllByWorkType = new Dictionary<WorkTypeDef, int>();

                // Clear existing data
                rule_AlwaysHaveOneByWorkType.Clear();
                rule_AlwaysAssignAllByWorkType.Clear();

                // Initialize temp lists if null
                if (tempAlwaysHaveOneKeys == null) tempAlwaysHaveOneKeys = new List<string>();
                if (tempAlwaysHaveOneValues == null) tempAlwaysHaveOneValues = new List<int>();
                if (tempAlwaysAssignAllKeys == null) tempAlwaysAssignAllKeys = new List<string>();
                if (tempAlwaysAssignAllValues == null) tempAlwaysAssignAllValues = new List<int>();

                // Reconstruct dictionaries from string lists, skipping invalid entries
                ReconstructDictionary(tempAlwaysHaveOneKeys, tempAlwaysHaveOneValues, rule_AlwaysHaveOneByWorkType);
                ReconstructDictionary(tempAlwaysAssignAllKeys, tempAlwaysAssignAllValues, rule_AlwaysAssignAllByWorkType);

                // Add default entry for Doctor if dictionary is empty and Doctor WorkType exists
                if (rule_AlwaysHaveOneByWorkType.Count == 0)
                {
                    var doctorWorkType = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Doctor");
                    if (doctorWorkType != null)
                    {
                        rule_AlwaysHaveOneByWorkType.Add(doctorWorkType, 2);
                    }
                }
            }
        }

        private void ReconstructDictionary(List<string> keys, List<int> values, Dictionary<WorkTypeDef, int> targetDict)
        {
            if (keys == null || values == null || keys.Count != values.Count) return;

            for (int i = 0; i < keys.Count; i++)
            {
                if (string.IsNullOrEmpty(keys[i])) continue;

                var workTypeDef = DefDatabase<WorkTypeDef>.GetNamedSilentFail(keys[i]);
                if (workTypeDef != null && !targetDict.ContainsKey(workTypeDef))
                {
                    targetDict.Add(workTypeDef, values[i]);
                }
            }
        }
    }
}