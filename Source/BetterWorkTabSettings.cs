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
using LudeonTK;
using Better_Work_Tab.UI;

namespace Better_Work_Tab
{
    [StaticConstructorOnStartup]
    static class DefaultSettings
    {
        static DefaultSettings()
        {
            //Log.Message(BetterWorkTabMod.Settings == null ? "Settings is null" : "Settings is NOT null");
            //if(BetterWorkTabMod.Settings != null)
            //    BetterWorkTabMod.Settings.InitializeRulesets();

            //DefOfHelper.EnsureInitializedInCtor(typeof(WorkTypeDefOf));
        }

        public static bool enableSkillOverlayFeature = true;
        public static bool enableAutoAssignFeature = true;
        public static List<string> workColumnOrderDefNames = new List<string>();
        public static bool firstTimeSetupDone = false;
        public static float dividerHeight = 18f;
        public static bool drawDividerHighlight = true;
        public static bool showOnlyLineDragIndicatorRows = false;
        public static bool showOnlyLineDragIndicatorColumns = false;
        public static bool requireCtrlForDrag = true;
        public static bool disableLeftClickClose = false;
        public static bool showPawnCountAtBottom = true;
        public static bool showBedCountAtBottom = false;
        public static bool enableRowColumnHighlights = true;

        // This rule ensures at least one colonist is assigned to a specific work type at a given priority
        public static Dictionary<WorkTypeDef, int> rule_AlwaysHaveOneByWorkType = new Dictionary<WorkTypeDef, int>();

        // This rule assigns all available colonists to a specific work type at a given priority
        public static Dictionary<WorkTypeDef, int> rule_AlwaysAssignAllByWorkType = new Dictionary<WorkTypeDef, int>();

        // Skill level colors used on the work tab
        public static Color Color_VeryLowSkill = new Color(0.82f, 0.25f, 0.25f);
        public static Color Color_LowSkill = new Color(0.95f, 0.75f, 0.20f);
        public static Color Color_GoodLowSkill = new Color(0.95f, 0.95f, 0.95f);
        public static Color Color_ExcellentSkill = new Color(0.35f, 0.85f, 0.35f);

        // Pawn/worktype highlight visibility settings
        public static bool ShowPawnAndWorktypeHighlights = true;
        public static bool ShowCursorPawnAndWorktypeHighlight = true;
        public static bool ShowFloatMenuPawnAndWorktypeHighlight = true;
        public static bool DoSelectedPawnHighlight = true;

        // Custom highlight color settings
        public static bool UseCustomMouseHoverHighlight = false;

        // Highlight colors (RGBA)
        public static Color Color_CursorHighlight = new Color(0.737f, 0.737f, 0.114f, 0.5f);
        public static Color Color_FloatMenuHighlight = new Color(0.114f, 0.737f, 0.737f, 0.5f);
        public static Color Color_CustomMouseHighlight = new Color(0.737f, 0.737f, 0.114f, 0.25f);
        public static Color Color_CustomSimilarWorktypeHighlight = new Color(0.737f, 0.737f, 0.114f, 0.125f);
        public static Color Color_IncapableBecauseOfCapacities = new Color(1f, 0.3f, 0.3f);
        public static Color Color_BestPawnForSkillSquare = new Color(0.35f, 0.85f, 0.35f);

        // Default rulesets - all marked as isDefault: true to prevent deletion
        public static List<WorkAssignmentRuleset> SavedRulesets = new List<WorkAssignmentRuleset>
        {
            new WorkAssignmentRuleset("Vanilla Starting Pawn", new List<WorkAssignmentParameters>()
            {
                new WorkAssignmentParameters("Highest Skill", 3, hasHighestSkill: true, randomIfMultiple: true),
                new WorkAssignmentParameters("Skills > 5", 3, skillLevelGreaterThan: 5),
                new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),
            }, isDefault: true),

            new WorkAssignmentRuleset("Vanilla New Pawn", new List<WorkAssignmentParameters>()
            {
                new WorkAssignmentParameters("Top 6", 3, isTopXSkill: 6),
                new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),
            }, isDefault: true),

            new WorkAssignmentRuleset("BWT Default", new List<WorkAssignmentParameters>()
            {
                new WorkAssignmentParameters("Always Firefight", 1, worktypeString: "Firefighter"),
                new WorkAssignmentParameters("Best Doc", 1, worktypeString: "Doctor", hasHighestSkill: true),
                new WorkAssignmentParameters("HaulUrg if able", 2, worktypeString: "HaulUrgently", ignoreIfWorktypeNonexistent: true),
                new WorkAssignmentParameters("Childcare", 2, worktypeString: "Childcare", hasChildOnMap: true),
                new WorkAssignmentParameters("Passion 2", 2, passionLevel: 2),
                new WorkAssignmentParameters("Always haul", 3, worktypeString: "Hauling"),
                new WorkAssignmentParameters("Passion 1", 3, passionLevel: 1),
                new WorkAssignmentParameters("Top 6", 3, isTopXSkill: 6),
                new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),
            }, isDefault: true),

            new WorkAssignmentRuleset("Best Pawn to 1", new List<WorkAssignmentParameters>()
            {
                new WorkAssignmentParameters("Best to 1", 1, hasHighestSkill: true),
            }, isDefault: true),

            new WorkAssignmentRuleset("Set all to 0", new List<WorkAssignmentParameters>()
            {
                new WorkAssignmentParameters("Reset", 0),
            }, isDefault: true)
        };

        public static WorkAssignmentRuleset CurrentAutoAssignRuleset = SavedRulesets[0];

        // UI mode settings for work tab visibility
        public static BetterWorkTabSettings.ShowUIMode ShowUIMode_ShowSmallSkillNumbers = BetterWorkTabSettings.ShowUIMode.Unshifted;
        public static BetterWorkTabSettings.ShowUIMode ShowUIMode_ShowPawnForSkillSquare = BetterWorkTabSettings.ShowUIMode.Shifted;
    }

    // Contains all configurable settings for Better Work Tab mod
    public class BetterWorkTabSettings : ModSettings
    {
        public BetterWorkTabSettings()
        {
            // Initialize rulesets immediately on construction
            //InitializeRulesets();
        }

        public bool firstTimeSetupDone = DefaultSettings.firstTimeSetupDone;

        // Master feature toggles
        public bool enableSkillOverlayFeature = true;
        public bool enableAutoAssignFeature = true;
        public float dividerHeight = DefaultSettings.dividerHeight;
        public bool drawDividerHighlight = DefaultSettings.drawDividerHighlight;
        public bool showOnlyLineDragIndicatorRows = DefaultSettings.showOnlyLineDragIndicatorRows;
        public bool showOnlyLineDragIndicatorColumns = DefaultSettings.showOnlyLineDragIndicatorColumns;
        public bool requireCtrlForDrag = DefaultSettings.requireCtrlForDrag;
        public bool disableLeftClickClose = DefaultSettings.disableLeftClickClose;
        public bool showPawnCountAtBottom = DefaultSettings.showPawnCountAtBottom;
        public bool showBedCountAtBottom = DefaultSettings.showBedCountAtBottom;
        public bool enableRowColumnHighlights = DefaultSettings.enableRowColumnHighlights;
        public List<string> workColumnOrderDefNames = new List<string>();
        public Dictionary<string, float> storedColumnWidths = new Dictionary<string, float>();

        // Skill level colors
        public Color Color_VeryLowSkill = new Color(0.82f, 0.25f, 0.25f);
        public Color Color_LowSkill = new Color(0.95f, 0.75f, 0.20f);
        public Color Color_GoodLowSkill = new Color(0.95f, 0.95f, 0.95f);
        public Color Color_ExcellentSkill = new Color(0.35f, 0.85f, 0.35f);

        // Highlight visibility settings
        public bool ShowPawnAndWorktypeHighlights = true;
        public bool ShowCursorPawnAndWorktypeHighlight = true;
        public bool ShowFloatMenuPawnAndWorktypeHighlight = true;
        public bool DoSelectedPawnHighlight = true;

        // Custom highlight colors
        public bool UseCustomMouseHoverHighlight = false;
        public Color Color_CursorHighlight = new Color(0.737f, 0.737f, 0.114f, 0.5f);
        public Color Color_FloatMenuHighlight = new Color(0.114f, 0.737f, 0.737f, 0.5f);
        public Color Color_CustomMouseHighlight = new Color(0.737f, 0.737f, 0.114f, 0.25f);
        public Color Color_CustomSimilarWorktypeHighlight = new Color(0.737f, 0.737f, 0.114f, 0.125f);

        // Derived colors (calculated from above)
        public Color Color_MouseHoverHighlight
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
                return UseCustomMouseHoverHighlight ? Color_CustomSimilarWorktypeHighlight : halvedAlpha;
            }
        }

        // Status indicator colors
        public Color Color_IncapableBecauseOfCapacities = new Color(1f, 0.3f, 0.3f);
        public Color Color_BestPawnForSkillSquare = new Color(0.35f, 0.85f, 0.35f);

        // Ruleset management
        public List<WorkAssignmentRuleset> SavedRulesets;
        public WorkAssignmentRuleset CurrentRuleset = null;

        // UI mode settings
        public enum ShowUIMode { Always, Never, Shifted, Unshifted }
        public ShowUIMode ShowUIMode_ShowSmallSkillNumbers = ShowUIMode.Unshifted;
        public ShowUIMode ShowUIMode_ShowPawnForSkillSquare = ShowUIMode.Shifted;

        /// <summary>
        /// Creates/restores all default rulesets from the static defaults.
        /// </summary>
        public void CreateDefaultRulesets()
        {
            SavedRulesets = new List<WorkAssignmentRuleset>(DefaultSettings.SavedRulesets);
            CurrentRuleset = SavedRulesets.FirstOrDefault();
            BetterWorkTabMod.Settings.Write();
        }

        /// <summary>
        /// Adds all default rulesets to the current saved rulesets list (without replacing).
        /// </summary>
        public void AddDefaultRules()
        {
            if (SavedRulesets == null)
            {
                SavedRulesets = new List<WorkAssignmentRuleset>();
            }
            SavedRulesets.AddRange(DefaultSettings.SavedRulesets);
        }

        public override void ExposeData()
        {
            // Core feature toggles
            Scribe_Values.Look(ref firstTimeSetupDone, "firstTimeSetupDone", DefaultSettings.firstTimeSetupDone);
            Scribe_Values.Look(ref enableSkillOverlayFeature, "enableSkillOverlayFeature", DefaultSettings.enableSkillOverlayFeature);
            Scribe_Values.Look(ref enableAutoAssignFeature, "enableAutoAssignFeature", DefaultSettings.enableAutoAssignFeature);

            // Highlight settings
            Scribe_Values.Look(ref ShowPawnAndWorktypeHighlights, "ShowPawnAndWorktypeHighlights", DefaultSettings.ShowPawnAndWorktypeHighlights);
            Scribe_Values.Look(ref ShowCursorPawnAndWorktypeHighlight, "ShowCursorPawnAndWorktypeHighlight", DefaultSettings.ShowCursorPawnAndWorktypeHighlight);
            Scribe_Values.Look(ref ShowFloatMenuPawnAndWorktypeHighlight, "ShowFloatMenuPawnAndWorktypeHighlight", DefaultSettings.ShowFloatMenuPawnAndWorktypeHighlight);
            Scribe_Values.Look(ref DoSelectedPawnHighlight, "DoSelectedPawnHighlight", DefaultSettings.DoSelectedPawnHighlight);
            Scribe_Values.Look(ref UseCustomMouseHoverHighlight, "UseCustomMouseHoverHighlight", DefaultSettings.UseCustomMouseHoverHighlight);
            Scribe_Values.Look(ref enableRowColumnHighlights, "enableRowColumnHighlights", DefaultSettings.enableRowColumnHighlights);

            // UI display settings
            Scribe_Values.Look(ref showPawnCountAtBottom, "showPawnCountAtBottom", DefaultSettings.showPawnCountAtBottom);
            Scribe_Values.Look(ref showBedCountAtBottom, "showBedCountAtBottom", DefaultSettings.showBedCountAtBottom);
            Scribe_Values.Look(ref disableLeftClickClose, "disableLeftClickClose", DefaultSettings.disableLeftClickClose);
            Scribe_Values.Look(ref requireCtrlForDrag, "requireCtrlForDrag", DefaultSettings.requireCtrlForDrag);
            Scribe_Values.Look(ref showOnlyLineDragIndicatorRows, "showOnlyLineDragIndicatorRows", DefaultSettings.showOnlyLineDragIndicatorRows);
            Scribe_Values.Look(ref showOnlyLineDragIndicatorColumns, "showOnlyLineDragIndicatorColumns", DefaultSettings.showOnlyLineDragIndicatorColumns);

            // Colors
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

            // UI modes
            Scribe_Values.Look(ref ShowUIMode_ShowSmallSkillNumbers, "ShowUIMode_ShowSmallSkillNumbers", DefaultSettings.ShowUIMode_ShowSmallSkillNumbers);
            Scribe_Values.Look(ref ShowUIMode_ShowPawnForSkillSquare, "ShowUIMode_ShowPawnForSkillSquare", DefaultSettings.ShowUIMode_ShowPawnForSkillSquare);

            // Divider settings
            Scribe_Values.Look(ref dividerHeight, "dividerHeight", DefaultSettings.dividerHeight);
            Scribe_Values.Look(ref drawDividerHighlight, "drawDividerHighlight", DefaultSettings.drawDividerHighlight);

            // Load rulesets from save file
            Scribe_Collections.Look(ref SavedRulesets, "SavedRulesets", LookMode.Deep);

            // Reinitialize rulesets after load (restores defaults if missing)
            //InitializeRulesets();

            // Column order and widths persistence
            Scribe_Collections.Look(ref workColumnOrderDefNames, "workColumnOrderDefNames", LookMode.Value);
            Scribe_Collections.Look(ref storedColumnWidths, "storedColumnWidths", LookMode.Value, LookMode.Value);

            if (storedColumnWidths == null)
            {
                storedColumnWidths = new Dictionary<string, float>();
            }
        }

        /// <summary>
        /// Restores all settings to their default values.
        /// </summary>
        public void RestoreDefaults()
        {
            // Features
            enableSkillOverlayFeature = DefaultSettings.enableSkillOverlayFeature;
            enableAutoAssignFeature = DefaultSettings.enableAutoAssignFeature;

            // Highlights
            ShowPawnAndWorktypeHighlights = DefaultSettings.ShowPawnAndWorktypeHighlights;
            ShowCursorPawnAndWorktypeHighlight = DefaultSettings.ShowCursorPawnAndWorktypeHighlight;
            ShowFloatMenuPawnAndWorktypeHighlight = DefaultSettings.ShowFloatMenuPawnAndWorktypeHighlight;
            DoSelectedPawnHighlight = DefaultSettings.DoSelectedPawnHighlight;
            UseCustomMouseHoverHighlight = DefaultSettings.UseCustomMouseHoverHighlight;
            enableRowColumnHighlights = DefaultSettings.enableRowColumnHighlights;

            // UI display
            showPawnCountAtBottom = DefaultSettings.showPawnCountAtBottom;
            showBedCountAtBottom = DefaultSettings.showBedCountAtBottom;
            disableLeftClickClose = DefaultSettings.disableLeftClickClose;
            requireCtrlForDrag = DefaultSettings.requireCtrlForDrag;
            showOnlyLineDragIndicatorRows = DefaultSettings.showOnlyLineDragIndicatorRows;
            showOnlyLineDragIndicatorColumns = DefaultSettings.showOnlyLineDragIndicatorColumns;

            // Colors
            Color_CursorHighlight = DefaultSettings.Color_CursorHighlight;
            Color_FloatMenuHighlight = DefaultSettings.Color_FloatMenuHighlight;
            Color_CustomMouseHighlight = DefaultSettings.Color_CustomMouseHighlight;
            Color_CustomSimilarWorktypeHighlight = DefaultSettings.Color_CustomSimilarWorktypeHighlight;
            Color_IncapableBecauseOfCapacities = DefaultSettings.Color_IncapableBecauseOfCapacities;
            Color_BestPawnForSkillSquare = DefaultSettings.Color_BestPawnForSkillSquare;
            Color_VeryLowSkill = DefaultSettings.Color_VeryLowSkill;
            Color_LowSkill = DefaultSettings.Color_LowSkill;
            Color_GoodLowSkill = DefaultSettings.Color_GoodLowSkill;
            Color_ExcellentSkill = DefaultSettings.Color_ExcellentSkill;

            // UI modes
            ShowUIMode_ShowSmallSkillNumbers = DefaultSettings.ShowUIMode_ShowSmallSkillNumbers;
            ShowUIMode_ShowPawnForSkillSquare = DefaultSettings.ShowUIMode_ShowPawnForSkillSquare;

            // Dividers
            dividerHeight = DefaultSettings.dividerHeight;
            drawDividerHighlight = DefaultSettings.drawDividerHighlight;
        }

        /// <summary>
        /// Initializes/recovers rulesets. Called on construction and after loading saves.
        /// Automatically restores defaults if no rulesets exist (recovery from accidental deletion).
        /// </summary>
        public void InitializeRulesets()
        {

            // Create the list if it doesn't exist
            if (SavedRulesets == null)
            {
                SavedRulesets = new List<WorkAssignmentRuleset>();
            }

            // If there are no saved rulesets, restore from defaults
            if (!SavedRulesets.Any())
            {
                SavedRulesets.AddRange(DefaultSettings.SavedRulesets); // Moved from the foreach loop since that would apply it 5 times.

                // Ensure worktypes and strings are synchronized
                foreach (var ruleset in SavedRulesets)
                {
                    foreach (var rule in ruleset.Rules)
                    {
                        var parameters = rule.Parameters;

                        // Sync worktype <-> worktype string
                        if (parameters.Worktype == null && !string.IsNullOrEmpty(parameters.WorktypeString))
                        {
                            parameters.Worktype = DefDatabase<WorkTypeDef>.GetNamedSilentFail(parameters.WorktypeString);
                        }
                        else if (parameters.Worktype != null && string.IsNullOrEmpty(parameters.WorktypeString))
                        {
                            parameters.WorktypeString = parameters.Worktype.defName;
                        }
                    }
                }
            }

            // Set current ruleset to first available if none selected
            if (CurrentRuleset == null && SavedRulesets.Any())
            {
                CurrentRuleset = SavedRulesets.FirstOrDefault();
            }

        }
    }
}