using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.Workloads;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SocialPlatforms.Impl;
using Verse;

namespace Better_Work_Tab
{
    static class DefaultSettings
    {
        public static bool enableSkillOverlayFeature = true;
        public static bool enableAutoAssignFeature = true;

        // This rule ensures at least one colonist is assigned to a specific work type at a given priority
        public static Dictionary<WorkTypeDef, int> rule_AlwaysHaveOneByWorkType = new Dictionary<WorkTypeDef, int>();

        // This rule assigns all available colonists to a specific work type at a given priority
        public static Dictionary<WorkTypeDef, int> rule_AlwaysAssignAllByWorkType = new Dictionary<WorkTypeDef, int>();

        //These colors are used on skill numbers in the work tab
        public static Color Color_VeryLowSkill = new Color(0.82f, 0.25f, 0.25f);
        public static Color Color_LowSkill = new Color(0.95f, 0.75f, 0.20f);
        public static Color Color_GoodLowSkill = new Color(0.95f, 0.95f, 0.95f);
        public static Color Color_ExcellentSkill = new Color(0.35f, 0.85f, 0.35f);

        // These control which pawn/worktype highlights are shown on the work tab
        public static bool ShowPawnAndWorktypeHighlights = true; //disable all highlights
        public static bool ShowCursorPawnAndWorktypeHighlight = true; //disable cursor highlight
        public static bool ShowFloatMenuPawnAndWorktypeHighlight = true; //disable higlight open-from-float-menu highlight
        public static bool DoSelectedPawnHighlight = true;

        // This allows custom color for mouse hover highlight instead of reusing cursor highlight color
        public static bool UseCustomMouseHoverHighlight = false;

        // These colors are used for pawn/worktype highlights on the work tab
        public static Color Color_CursorHighlight = new Color(0.737f, 0.737f, 0.114f, 0.5f); // Yellow with 50% alpha
        public static Color Color_FloatMenuHighlight = new Color(0.114f, 0.737f, 0.737f, 0.5f);// Blue with 50% alpha
        public static Color Color_CustomMouseHighlight = new Color(0.737f, 0.737f, 0.114f, 0.25f); // Yellow with 25% alpha
        public static Color Color_CustomSimilarWorktypeHighlight = new Color(0.737f, 0.737f, 0.114f, 0.125f); // Yellow with 25% alpha
        // This color is used to indicate a pawn is incapable of a work type due to health conditions. Vanilla red is Color(1,0.3,0.3)
        public static Color Color_IncapableBecauseOfCapacities = new Color(1f, 0.3f, 0.3f); // Red

        //This color is used to indicate the best pawn for a skill in the work tab
        public static Color Color_BestPawnForSkillSquare = new Color(0.35f, 0.85f, 0.35f);


        public static List<WorkAssignmentRuleset> SavedRulesets = new List<WorkAssignmentRuleset>{

                new WorkAssignmentRuleset("Vanilla Starting Pawn", new List<WorkAssignmentParameters>()
                {
                   new WorkAssignmentParameters("Highest Skill", 3, hasHighestSkill: true, randomIfMultiple: true),
                   new WorkAssignmentParameters("Skills > 5", 3, skillLevelGreaterThan: 5),
                   new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),

                }),


                new WorkAssignmentRuleset("Vanilla New Pawn", new List<WorkAssignmentParameters>()
                {
                   new WorkAssignmentParameters("Top 6", 3, isTopXSkill: 6),
                   new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),

                }),


                new WorkAssignmentRuleset("BWT Default", new List<WorkAssignmentParameters>()
                {
                    new WorkAssignmentParameters("Always Firefight", 1, worktype: WorkTypeDefOf.Firefighter),
                    new WorkAssignmentParameters("Best Doc", 1, worktype: WorkTypeDefOf.Doctor, hasHighestSkill: true),

                    new WorkAssignmentParameters("HaulUrg if able", 2, worktypeDefNameIgnoreIfNonexistant:"HaulUrgently"),
                    new WorkAssignmentParameters("Childcare", 2, worktype: WorkTypeDefOf.Childcare, hasChildOnMap: true),
                    new WorkAssignmentParameters("Passion 2", 2, passionLevel: 2),

                    new WorkAssignmentParameters("Always haul", 3, worktype: WorkTypeDefOf.Hauling),
                    new WorkAssignmentParameters("Passion 1", 3, passionLevel: 1),
                    new WorkAssignmentParameters("Top 6", 3, isTopXSkill: 6),
                    new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),
                }),

                new WorkAssignmentRuleset("Best Pawn to 1", new List<WorkAssignmentParameters>()
                {
                    new WorkAssignmentParameters("Best to 1", 1, hasHighestSkill: true),
                }),

                new WorkAssignmentRuleset("Set all to 0", new List<WorkAssignmentParameters>()
                {
                    new WorkAssignmentParameters("Reset", 0),
                })
            };

        public static WorkAssignmentRuleset CurrentAutoAssignRuleset = SavedRulesets[0];

        //Worklists are stored in a GameComponent


        // These control when various UI elements are shown on the work tab
        public static BetterWorkTabSettings.ShowUIMode ShowUIMode_ShowSmallSkillNumbers = BetterWorkTabSettings.ShowUIMode.Unshifted;
        public static BetterWorkTabSettings.ShowUIMode ShowUIMode_ShowPawnForSkillSquare = BetterWorkTabSettings.ShowUIMode.Shifted;
    }

    // This contains all configurable settings for the Better Work Tab mod with reasonable defaults
    public class BetterWorkTabSettings : ModSettings
    {
        public BetterWorkTabSettings() { }

        // This provides master toggles for major features so users can disable parts they don't want
        public bool enableSkillOverlayFeature = true;
        public bool enableAutoAssignFeature = true;

        // This sets the baseline priority for work types not handled by specific rules (0 = leave unchanged)

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
                   new WorkAssignmentParameters("Highest Skill", 3, hasHighestSkill: true, randomIfMultiple: true),
                   new WorkAssignmentParameters("Skills > 5", 3, skillLevelGreaterThan: 5),
                   new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),

                }),


                new WorkAssignmentRuleset("Vanilla New Pawn", new List<WorkAssignmentParameters>()
                {
                   new WorkAssignmentParameters("Top 6", 3, isTopXSkill: 6),
                   new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),

                }),


                new WorkAssignmentRuleset("BWT Default", new List<WorkAssignmentParameters>()
                {
                    new WorkAssignmentParameters("Always Firefight", 1, worktype: WorkTypeDefOf.Firefighter),
                     new WorkAssignmentParameters("Always Patient", 1, worktypeDefNameIgnoreIfNonexistant:"Patient"),
                    new WorkAssignmentParameters("Always Bed Rest", 1, worktypeDefNameIgnoreIfNonexistant:"PatientBedRest"),
                    new WorkAssignmentParameters("Always Basic", 1, worktypeDefNameIgnoreIfNonexistant:"BasicWorker"),
                    new WorkAssignmentParameters("Best Doc", 1, worktype: WorkTypeDefOf.Doctor, hasHighestSkill: true),

                    new WorkAssignmentParameters("HaulUrg if able", 2, worktypeDefNameIgnoreIfNonexistant:"HaulUrgently"),
                    new WorkAssignmentParameters("Childcare", 2, worktype: WorkTypeDefOf.Childcare, hasChildOnMap: true),
                    new WorkAssignmentParameters("Passion 2", 2, passionLevel: 2),

                    new WorkAssignmentParameters("Always haul", 3, worktype: WorkTypeDefOf.Hauling),
                    new WorkAssignmentParameters("Always clean", 3, worktype: WorkTypeDefOf.Cleaning),
                    new WorkAssignmentParameters("Passion 1", 3, passionLevel: 1),
                    new WorkAssignmentParameters("Top 6", 3, isTopXSkill: 6),
                    //new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),
                }),

                new WorkAssignmentRuleset("Best Pawn to 1", new List<WorkAssignmentParameters>()
                {
                    new WorkAssignmentParameters("Best to 1", 1, hasHighestSkill: true),
                }),

                new WorkAssignmentRuleset("Set all to 0", new List<WorkAssignmentParameters>()
                {
                    new WorkAssignmentParameters("Reset", 0),
                })
            };

            CurrentRuleset = SavedRulesets[0];
        }
        public WorkAssignmentRuleset CurrentRuleset = null;

        //Worklists are stored in a GameComponent


        // These control when various UI elements are shown on the work tab
        public enum ShowUIMode { Always, Never, Shifted, Unshifted}
        public ShowUIMode ShowUIMode_ShowSmallSkillNumbers = ShowUIMode.Unshifted;
        public ShowUIMode ShowUIMode_ShowPawnForSkillSquare = ShowUIMode.Shifted;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableSkillOverlayFeature, "enableSkillOverlayFeature", DefaultSettings.enableSkillOverlayFeature);
            Scribe_Values.Look(ref enableAutoAssignFeature, "enableAutoAssignFeature", DefaultSettings.enableAutoAssignFeature);
            Scribe_Values.Look(ref ShowPawnAndWorktypeHighlights, "ShowPawnAndWorktypeHighlights", DefaultSettings.ShowPawnAndWorktypeHighlights);
            Scribe_Values.Look(ref ShowCursorPawnAndWorktypeHighlight, "ShowCursorPawnAndWorktypeHighlight", DefaultSettings.ShowCursorPawnAndWorktypeHighlight);
            Scribe_Values.Look(ref ShowFloatMenuPawnAndWorktypeHighlight, "ShowFloatMenuPawnAndWorktypeHighlight", DefaultSettings.ShowFloatMenuPawnAndWorktypeHighlight);
            Scribe_Values.Look(ref DoSelectedPawnHighlight, "DoSelectedPawnHighlight", DefaultSettings.DoSelectedPawnHighlight);
            Scribe_Values.Look(ref UseCustomMouseHoverHighlight, "UseCustomMouseHoverHighlight", DefaultSettings.UseCustomMouseHoverHighlight);
            
            
            Scribe_Values.Look(ref Color_CursorHighlight, "Color_CursorHighlight", DefaultSettings.Color_CursorHighlight);
            Scribe_Values.Look(ref Color_FloatMenuHighlight, "Color_FloatMenuHighlight", DefaultSettings.Color_FloatMenuHighlight);
            Scribe_Values.Look(ref Color_CustomMouseHighlight, "Color_CustomMouseHighlight", DefaultSettings.Color_CustomMouseHighlight);
            Scribe_Values.Look(ref Color_CustomSimilarWorktypeHighlight, "Color_CustomSimilarWorktypeHighlight", DefaultSettings.Color_CustomSimilarWorktypeHighlight);
            Scribe_Values.Look(ref Color_IncapableBecauseOfCapacities, "Color_IncapableBecauseOfCapacities", DefaultSettings.Color_IncapableBecauseOfCapacities);
            Scribe_Values.Look(ref Color_BestPawnForSkillSquare, "Color_BestPawnForSkillSquare", DefaultSettings.Color_BestPawnForSkillSquare);
            Scribe_Values.Look(ref Color_VeryLowSkill, "Color_VeryLowSkill", DefaultSettings.Color_VeryLowSkill);
            Scribe_Values.Look(ref Color_LowSkill, "Color_LowSkill", DefaultSettings.Color_LowSkill);
            Scribe_Values.Look(ref Color_GoodLowSkill, "Color_GoodLowSkill", DefaultSettings.Color_GoodLowSkill);
            Scribe_Values.Look(ref Color_ExcellentSkill, "Color_ExcellentSkill", DefaultSettings.Color_ExcellentSkill);

            Scribe_Values.Look(ref ShowUIMode_ShowSmallSkillNumbers, "CurreShowUIMode_ShowSmallSkillNumbersntAutoAssignRuleset", DefaultSettings.ShowUIMode_ShowSmallSkillNumbers);
            Scribe_Values.Look(ref ShowUIMode_ShowPawnForSkillSquare, "ShowUIMode_ShowPawnForSkillSquare", DefaultSettings.ShowUIMode_ShowPawnForSkillSquare);

        }

    }
}