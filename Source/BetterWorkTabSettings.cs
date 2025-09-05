using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.Workloads;
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
                Color halvedAlpha = Color_MouseHoverHighlight;
                halvedAlpha.a = halvedAlpha.a / 2f;
                return UseCustomMouseHoverHighlight ? Color_CustomSimilarWorktypeHighlight: halvedAlpha;
            }
        }

       

        // This color is used to indicate a pawn is incapable of a work type due to health conditions. Vanilla red is Color(1,0.3,0.3)
        public Color Color_IncapableBecauseOfCapacities = new Color(1f, 0.3f, 0.3f); // Red

        //This color is used to indicate the best pawn for a skill in the work tab
        public Color Color_BestPawnForSkillSquare = new Color(0.35f, 0.85f, 0.35f);

        
        public List<WorkAssignmentRuleset> SavedRulesets = new List<WorkAssignmentRuleset> ();
        public void CreateDefaultRulesets()
        {
            SavedRulesets = new List<WorkAssignmentRuleset>{

                new WorkAssignmentRuleset("Vanilla Starting Pawn", new List<WorkAssignmentParameters>()
                {
                   new WorkAssignmentParameters(3, isNaturalAlwaysAssign: true),
                   new WorkAssignmentParameters(3, skillLevelGreaterThan: 5),
                   new WorkAssignmentParameters(3, randomIfTied: true, hasHighestSkill: true)

                }),


                new WorkAssignmentRuleset("Vanilla New Pawn", new List<WorkAssignmentParameters>()
                {
                   new WorkAssignmentParameters(3, isTopXSkill: 6),
                   new WorkAssignmentParameters(3, isNaturalAlwaysAssign: true),
                
                }),


                new WorkAssignmentRuleset("BWT Default", new List<WorkAssignmentParameters>()
                {
                    new WorkAssignmentParameters(1, worktype: WorkTypeDefOf.Firefighter),
                    new WorkAssignmentParameters(1, worktypeNamedIgnoreIfNonexistant:"Patient"),
                    new WorkAssignmentParameters(1, worktypeNamedIgnoreIfNonexistant:"PatientBedRest"),
                    new WorkAssignmentParameters(1, worktypeNamedIgnoreIfNonexistant:"BasicWorker"),
                    new WorkAssignmentParameters(1, worktype: WorkTypeDefOf.Doctor, hasHighestSkill: true),
                    
                    new WorkAssignmentParameters(2, worktypeNamedIgnoreIfNonexistant:"HaulUrgently"),
                    new WorkAssignmentParameters(2, worktype: WorkTypeDefOf.Childcare, hasChildOnMap: true),
                    
                    new WorkAssignmentParameters(2, passionLevel: 2),
                    new WorkAssignmentParameters(3, passionLevel: 1),
                    new WorkAssignmentParameters(3, isTopXSkill: 6),
                    new WorkAssignmentParameters(3, isNaturalAlwaysAssign: true),
                }),

                new WorkAssignmentRuleset("Best Pawn to 1", new List<WorkAssignmentParameters>()
                {
                    new WorkAssignmentParameters(1, hasHighestSkill: true),
                })
            };

            CurrentAutoAssignRuleset = SavedRulesets[0];
        }
        public WorkAssignmentRuleset CurrentAutoAssignRuleset = null;

        // This stores saved workloads for easy switching between different work setups
        public List<Workload> SavedWorkloads = new List<Workload>();
        public Workload CurrentWorkload = null;

        // These control when various UI elements are shown on the work tab
        public enum ShowUIMode { Always, Never, Shifted, Unshifted}
        public ShowUIMode ShowUIMode_ShowSmallSkillNumbers = ShowUIMode.Unshifted;
        public ShowUIMode ShowUIMode_ShowPawnForSkillSquare = ShowUIMode.Shifted;



        // Temporary storage for dictionary data to avoid DefOf issues during loading
        private List<string> tempAlwaysHaveOneKeys = new List<string>();
        private List<int> tempAlwaysHaveOneValues = new List<int>();
        private List<string> tempAlwaysAssignAllKeys = new List<string>();
        private List<int> tempAlwaysAssignAllValues = new List<int>();




        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref CurrentWorkload, "currentWorklist");
            if(SavedWorkloads == null)
            {
                SavedWorkloads = new List<Workload>();
            }
            Scribe_Collections.Look(ref SavedWorkloads, "savedWorklists");
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