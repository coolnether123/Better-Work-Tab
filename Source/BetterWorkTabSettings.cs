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
    /// <summary>
    /// Centralized configuration class for the Better Work Tab mod, inheriting from Verse.ModSettings for automatic serialization and deserialization during game saves.
    /// Manages toggles for UI features like skill overlays and pawn highlights, auto-assignment rules for priorities and expert selection (e.g., best doctors, passion-based),
    /// debug logging levels, custom colors for visual feedback in the work tab, and collections for rulesets and workload data.
    /// Default values are set to sensible starting points for survival gameplay, such as ensuring essential work types (firefighting, doctor) have at least priority 1.
    /// Dictionaries using Def references (e.g., WorkTypeDef) are reconstructed post-load via temporary lists to resolve Defs after database initialization.
    /// Instance is accessed statically via BetterWorkTabMod.Settings and rendered in the mod's options UI.
    /// </summary>
    public class BetterWorkTabSettings : ModSettings
    {
        /// <summary>
        /// Default constructor. No initialization logic here; defaults are set via field initializers and CreateDefaultRulesets.
        /// Called implicitly when the settings instance is created during mod load.
        /// </summary>
        public BetterWorkTabSettings() { }

        /// <summary>
        /// Master toggle to enable or disable the skill overlay feature, which displays skill levels and best pawn indicators on the work tab.
        /// Defaults to true for enhanced UI feedback.
        /// </summary>
        public bool enableSkillOverlayFeature = true;

        /// <summary>
        /// Master toggle to enable or disable the auto-assignment features, which apply rulesets to automatically set work priorities for pawns.
        /// Defaults to true for automated colony management.
        /// </summary>
        public bool enableAutoAssignFeature = true;

        /// <summary>
        /// List of Def names defining the custom order of work type columns in the work tab UI.
        /// Allows reordering columns via drag-and-drop; persists across saves. Initialized empty; populated from defaults or user changes.
        /// </summary>
        public List<string> workColumnOrderDefNames = new List<string>();

        /// <summary>
        /// Debug logging verbosity level (0 = off, higher = more detailed). Controls general mod logs for features like rules and UI updates.
        /// Defaults to 0 to minimize log spam.
        /// </summary>
        public int debugLogLevel = 5;

        /// <summary>
        /// Toggle to enable debug logging specifically for column drag-and-drop operations in the work tab.
        /// Defaults to false; use for troubleshooting reordering issues.
        /// </summary>
        public bool debugLogDragColumns = true;

        /// <summary>
        /// Toggle to enable debug logging specifically for row (pawn) drag-and-drop operations in the work tab.
        /// Defaults to false; use for troubleshooting pawn reordering issues.
        /// </summary>
        public bool debugLogDragRows = true;

        /// <summary>
        /// Maximum number of debug logs per second to prevent flooding the log file.
        /// Defaults to 10; adjustable for performance during heavy testing.
        /// </summary>
        public int debugLogMaxPerSecond = 10;

        /// <summary>
        /// Default priority level applied to work types not explicitly handled by auto-assignment rules.
        /// 0 means no change (leave as is); other values (1-4) set a baseline. Defaults to 0 for minimal interference.
        /// </summary>
        public int defaultStartingPriority = 0;

        /// <summary>
        /// Toggle to enable the core "always priority X" rule, which ensures essential survival work types get a minimum priority.
        /// Defaults to true for colony safety.
        /// </summary>
        public bool rule_CoreAlwaysPriorityEnabled = true;

        /// <summary>
        /// The priority value (1-4) assigned by the core always-priority rule to enabled essential work types.
        /// Defaults to 1 (low but guaranteed).
        /// </summary>
        public int rule_CoreAlwaysPriorityValue = 1; // 1..4

        /// <summary>
        /// Toggle to apply the core always-priority rule specifically to Firefighter work type.
        /// Defaults to true; ensures at least one pawn fights fires.
        /// </summary>
        public bool core_Firefighter = true;

        /// <summary>
        /// Toggle to apply the core always-priority rule specifically to Patient work type.
        /// Defaults to true; ensures medical care for patients.
        /// </summary>
        public bool core_Patient = true;

        /// <summary>
        /// Toggle to apply the core always-priority rule specifically to BedRest work type.
        /// Defaults to true; ensures tending to bedridden pawns.
        /// </summary>
        public bool core_BedRest = true;

        /// <summary>
        /// Toggle to apply the core always-priority rule specifically to BasicWorker work type.
        /// Defaults to true; ensures basic tasks like cleaning/rescuing.
        /// </summary>
        public bool core_Basic = true;

        /// <summary>
        /// Toggle to enable the best-doctors rule, which automatically assigns Doctor work to the pawn with the highest medical skill.
        /// Defaults to true for efficient healthcare.
        /// </summary>
        public bool rule_BestDoctorsEnabled = true;

        /// <summary>
        /// Toggle to enable passion-based priority overrides, adjusting assignments based on pawn passions for work types.
        /// Defaults to true to leverage passions for better efficiency and mood.
        /// </summary>
        public bool rule_PassionOverrideEnabled = true;

        /// <summary>
        /// Priority assigned to work types where the pawn has no passion. Defaults to 0 (no override).
        /// </summary>
        public int passion_None = 0;   // No passion gets default treatment

        /// <summary>
        /// Priority assigned to work types where the pawn has minor passion. Defaults to 3 (medium).
        /// </summary>
        public int passion_Minor = 3;  // Minor passion gets medium priority

        /// <summary>
        /// Priority assigned to work types where the pawn has major passion. Defaults to 2 (high, but below critical).
        /// Allows specialization without overriding survival essentials.
        /// </summary>
        public int passion_Major = 2;  // Major passion gets high priority (but not highest to allow core work)

        /// <summary>
        /// Toggle to enable the childcare rule, which assigns parents of young children to Childcare work.
        /// Defaults to true for family management.
        /// </summary>
        public bool rule_ChildcareEnabled = true;

        /// <summary>
        /// The priority level assigned by the childcare rule to eligible parents. Defaults to 1 (low but ensured).
        /// </summary>
        public int rule_ChildcarePriority = 1;

        /// <summary>
        /// Dictionary mapping WorkTypeDefs to minimum priority, ensuring at least one pawn is assigned to each key work type at that priority.
        /// Populated via UI; reconstructed from temp lists post-load. Defaults empty.
        /// </summary>
        public Dictionary<WorkTypeDef, int> rule_AlwaysHaveOneByWorkType = new Dictionary<WorkTypeDef, int>();

        /// <summary>
        /// Dictionary mapping WorkTypeDefs to priority, assigning all capable pawns to each key work type at that priority.
        /// Populated via UI; reconstructed from temp lists post-load. Defaults empty.
        /// </summary>
        public Dictionary<WorkTypeDef, int> rule_AlwaysAssignAllByWorkType = new Dictionary<WorkTypeDef, int>();

        /// <summary>
        /// Color used to tint skill numbers in the work tab for very low skill levels (e.g., 0-2).
        /// Reddish hue (RGBA: 0.82, 0.25, 0.25, 1) to indicate poor performance.
        /// </summary>
        public Color Color_VeryLowSkill = new Color(0.82f, 0.25f, 0.25f);

        /// <summary>
        /// Color used to tint skill numbers for low skill levels (e.g., 3-5).
        /// Yellowish (RGBA: 0.95, 0.75, 0.20, 1) to show room for improvement.
        /// </summary>
        public Color Color_LowSkill = new Color(0.95f, 0.75f, 0.20f);

        /// <summary>
        /// Color used for good-low skill numbers (e.g., 6-7), near average.
        /// Neutral grayish-white (RGBA: 0.95, 0.95, 0.95, 1) for adequate performance.
        /// </summary>
        public Color Color_GoodLowSkill = new Color(0.95f, 0.95f, 0.95f);

        /// <summary>
        /// Color used for excellent skill numbers (e.g., 15+).
        /// Green (RGBA: 0.35, 0.85, 0.35, 1) to highlight expertise.
        /// </summary>
        public Color Color_ExcellentSkill = new Color(0.35f, 0.85f, 0.35f);

        /// <summary>
        /// Toggle to show general pawn and work type highlights on the work tab.
        /// Defaults to true; false disables all highlighting.
        /// </summary>
        public bool ShowPawnAndWorktypeHighlights = true; //disable all highlights

        /// <summary>
        /// Toggle to show highlights when hovering the cursor over pawns or work types.
        /// Defaults to true for interactive feedback.
        /// </summary>
        public bool ShowCursorPawnAndWorktypeHighlight = true; //disable cursor highlight

        /// <summary>
        /// Toggle to show highlights for pawns/work types opened from the float menu.
        /// Defaults to true for navigation aids.
        /// </summary>
        public bool ShowFloatMenuPawnAndWorktypeHighlight = true; //disable higlight open-from-float-menu highlight

        /// <summary>
        /// Toggle to highlight the currently selected pawn in the work tab.
        /// Defaults to true for focus.
        /// </summary>
        public bool DoSelectedPawnHighlight = true;

        /// <summary>
        /// Toggle to use a custom color for mouse hover highlights instead of deriving from cursor color.
        /// Defaults to false; enables more granular UI customization.
        /// </summary>
        public bool UseCustomMouseHoverHighlight = false;

        /// <summary>
        /// Base color for cursor hover highlights on pawns and work types (yellowish with 50% alpha for visibility over UI).
        /// RGBA: 0.737, 0.737, 0.114, 0.5.
        /// </summary>
        public Color Color_CursorHighlight = new Color(0.737f, 0.737f, 0.114f, 0.5f); // Yellow with 50% alpha

        /// <summary>
        /// Color for highlights triggered from float menu interactions (bluish with 50% alpha).
        /// RGBA: 0.114, 0.737, 0.737, 0.5.
        /// </summary>
        public Color Color_FloatMenuHighlight = new Color(0.114f, 0.737f, 0.737f, 0.5f);// Blue with 50% alpha

        /// <summary>
        /// Custom color for mouse hover highlights when UseCustomMouseHoverHighlight is true (yellow with 25% alpha).
        /// RGBA: 0.737, 0.737, 0.114, 0.25.
        /// </summary>
        public Color Color_CustomMouseHighlight = new Color(0.737f, 0.737f, 0.114f, 0.25f); // Yellow with 25% alpha

        /// <summary>
        /// Custom color for similar work type highlights during mouse over (yellow with 12.5% alpha).
        /// RGBA: 0.737, 0.737, 0.114, 0.125.
        /// </summary>
        public Color Color_CustomSimilarWorktypeHighlight = new Color(0.737f, 0.737f, 0.114f, 0.125f); // Yellow with 25% alpha

        /// <summary>
        /// Read-only property returning the color for mouse hover highlights.
        /// If UseCustomMouseHoverHighlight is true, uses Color_CustomMouseHighlight; otherwise, halves the alpha of Color_CursorHighlight.
        /// Used in UI rendering for subtle feedback on pawn/work type interactions.
        /// </summary>
        public Color Color_MouseHoverHighlight // This returns either the custom color or a halved alpha version of the cursor color
        {
            get 
            {
                Color halvedAlpha = Color_CursorHighlight;
                halvedAlpha.a = halvedAlpha.a / 2f;
                return UseCustomMouseHoverHighlight ? Color_CustomMouseHighlight : halvedAlpha; 
            }
        } 

        /// <summary>
        /// Read-only property returning the color for similar work type highlights on mouse over.
        /// If UseCustomMouseHoverHighlight is true, uses Color_CustomSimilarWorktypeHighlight; otherwise, halves the alpha of Color_MouseHoverHighlight.
        /// Provides layered highlighting for related work types.
        /// </summary>
        public Color Color_SimilarWorktypeMouseOver
        {
            get
            {
                Color halvedAlpha = Color_MouseHoverHighlight;
                halvedAlpha.a = halvedAlpha.a / 2f;
                return UseCustomMouseHoverHighlight ? Color_CustomSimilarWorktypeHighlight: halvedAlpha;
            }
        }

        /// <summary>
        /// Color used to indicate a pawn is incapable of a work type due to health/incapacity (e.g., missing limbs).
        /// Matches vanilla red (RGBA: 1, 0.3, 0.3, 1) for consistency.
        /// </summary>
        public Color Color_IncapableBecauseOfCapacities = new Color(1f, 0.3f, 0.3f); // Red

        /// <summary>
        /// Color used to highlight the best pawn square for a skill in the work tab (green, RGBA: 0.35, 0.85, 0.35, 1).
        /// Indicates optimal assignments visually.
        /// </summary>
        public Color Color_BestPawnForSkillSquare = new Color(0.35f, 0.85f, 0.35f);

        /// <summary>
        /// Collection of saved auto-assignment rulesets, each defining a strategy for setting work priorities (e.g., "BWT Default" for balanced starts).
        /// Populated with defaults in CreateDefaultRulesets; user can add/edit via UI. Serialized for persistence.
        /// </summary>
        public List<WorkAssignmentRuleset> SavedRulesets = new List<WorkAssignmentRuleset> ();

        /// <summary>
        /// Initializes the SavedRulesets with predefined rulesets for common scenarios (e.g., vanilla-like, best-pawn focused, all-zero).
        /// Called post-mod load via LongEventHandler to ensure Defs are available. Sets CurrentAutoAssignRuleset to the first (Vanilla Starting Pawn).
        /// Rulesets include parameters for priorities, skills, passions, and specific work types like Doctor or Firefighter.
        /// </summary>
        public void CreateDefaultRulesets()
        {
            SavedRulesets = new List<WorkAssignmentRuleset>{

                new WorkAssignmentRuleset("Vanilla Starting Pawn", new List<WorkAssignmentParameters>()
                {
                   new WorkAssignmentParameters(3, hasHighestSkill: true, randomIfMultiple: true),
                   new WorkAssignmentParameters(3, skillLevelGreaterThan: 5),
                   new WorkAssignmentParameters(3, isNaturalAlwaysAssign: true),

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
                    
                    new WorkAssignmentParameters(3, worktype: WorkTypeDefOf.Hauling),
                    new WorkAssignmentParameters(3, passionLevel: 1),
                    new WorkAssignmentParameters(3, isTopXSkill: 6),
                    new WorkAssignmentParameters(3, isNaturalAlwaysAssign: true),
                }),

                new WorkAssignmentRuleset("Best Pawn to 1", new List<WorkAssignmentParameters>()
                {
                    new WorkAssignmentParameters(1, hasHighestSkill: true),
                }),
                
                new WorkAssignmentRuleset("Set all to 0", new List<WorkAssignmentParameters>()
                {
                    new WorkAssignmentParameters(0),
                })
            };

            CurrentAutoAssignRuleset = SavedRulesets[0];
        }

        /// <summary>
        /// The currently active auto-assignment ruleset used for applying priorities to pawns.
        /// Defaults to null; set to SavedRulesets[0] after CreateDefaultRulesets. Can be changed via UI.
        /// </summary>
        public WorkAssignmentRuleset CurrentAutoAssignRuleset = null;

        /// <summary>
        /// Note: Worklists (legacy or related workload data) are managed separately in a Verse.GameComponent for runtime tracking,
        /// rather than here, to avoid serialization issues with dynamic data.
        /// </summary>


        /// <summary>
        /// Enum defining visibility modes for UI elements in the work tab, based on shift key state or always/never.
        /// Used to control display of compact skill numbers and best-pawn squares without cluttering the base UI.
        /// </summary>
        public enum ShowUIMode { Always, Never, Shifted, Unshifted}

        /// <summary>
        /// Visibility mode for small skill number overlays. Defaults to Unshifted (show when shift is not held).
        /// Allows toggle between full and compact views.
        /// </summary>
        public ShowUIMode ShowUIMode_ShowSmallSkillNumbers = ShowUIMode.Unshifted;

        /// <summary>
        /// Visibility mode for best-pawn skill square highlights. Defaults to Shifted (show only when shift is held).
        /// Prevents visual overload in normal use.
        /// </summary>
        public ShowUIMode ShowUIMode_ShowPawnForSkillSquare = ShowUIMode.Shifted;



        // Temporary storage for dictionary data to avoid DefOf issues during loading
        private List<string> tempAlwaysHaveOneKeys = new List<string>();
        private List<int> tempAlwaysHaveOneValues = new List<int>();
        private List<string> tempAlwaysAssignAllKeys = new List<string>();
        private List<int> tempAlwaysAssignAllValues = new List<int>();




        //public override void ExposeData()
        //{
        //    base.ExposeData();
        //    //Scribe_Values.Look(ref CurrentWorklist, "currentWorklist");
        //    //if (SavedWorklists == null) SavedWorklists = new List<Worklist>();
        //    //Scribe_Collections.Look(ref SavedWorklists, "savedWorklists");
        //}

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
