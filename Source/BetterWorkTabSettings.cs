using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SocialPlatforms.Impl;
using Verse;
using LudeonTK;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Spine.UI.SettingsFramework;

namespace Better_Work_Tab
{
    public enum DebugFeature
    {
        General,
        DragDrop,
        Layout,
        SkillOverlay,
        Rules,
        Workloads,
        Performance,
        ModSupport,
        SubWork,
        AngledHeaders
    }

    public enum PriorityMode
    {
        Vanilla,
        Auto,
        ExternalProvider,
        BetterWorkTab
    }

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

        public static float workTabMaxHeight = -1f; // Legacy pixel cap; replaced by workTabMaxVisiblePawns.
        public static int workTabMaxVisiblePawns = -1; // -1 = use vanilla default (fill screen)
        public static float workTabTopSpace = 40f; // Vanilla MainTabWindow_Work.ExtraTopSpace

        public static bool enableSkillOverlayFeature = true;
        public static bool enableAutoAssignFeature = true;
        public static bool enableDragDropReordering = true;
        public static bool enableDividers = true;
        public static bool enableWorkloads = true;
        public static bool enableSubWorkDrilldown = true;
        public static bool showSubWorkHeaderBadge = true;
        public static bool enableSubWorkCrossWorkDragDrop = true;
        public static bool enableCustomWorkLabels = true;
        public static BetterWorkTabSettings.SubWorkDrilldownModifier subWorkDrilldownModifier = BetterWorkTabSettings.SubWorkDrilldownModifier.Ctrl;
        public static BetterWorkTabSettings.SubWorkDrilldownButton subWorkDrilldownButton = BetterWorkTabSettings.SubWorkDrilldownButton.Left;
        public static bool useVanillaSubWorkGlobalPriorityBoxes = false;
        public static bool restoreCursorOnSubWorkExit = true;
        public static bool restoreCursorOnSubWorkPawnCellExit = false;
        public static bool enableSubWorkOverrideBreakAnimation = true;
        public static bool enableSubWorkTransitionAnimation = true;
        public static BetterWorkTabSettings.SubWorkTransitionStyle subWorkTransitionStyle =
            BetterWorkTabSettings.SubWorkTransitionStyle.ClassicGlideFlash;
        public static float subWorkTransitionSeconds = 0.48f;
        public static BetterWorkTabSettings.SubWorkDisabledParentMode subWorkDisabledParentMode =
            BetterWorkTabSettings.SubWorkDisabledParentMode.ParentWorkDisablesSubWork;
        public static bool subWorkAutoExpandColumns = true;
        public static bool subWorkEvenlyExpandColumns = true;
        public static bool enableColumnOrderSaving = true;
        public static bool enableUIElements = true;
        public static bool enablePerformanceOptimizations = true;
        public static bool enableMultiplayerSync = true;
        public static bool hideSettingResetIcons = false;
        public static bool mpShowOtherPlayersHover = false;
        public static bool mpAllowOthersToRequestLayout = true;
        public static bool mpAllowPresenceBroadcast = false;
        public static bool mpShowLinkedIndicator = true;
        public static List<string> workColumnOrderDefNames = new List<string>();
        public static bool firstTimeSetupDone = false;
        public static float dividerHeight = 18f;
        public static bool showOnlyLineDragIndicatorRows = true;
        public static bool showOnlyLineDragIndicatorColumns = true;
        public static bool showGhostDragIndicator = true;
        public static bool showInsertionLineIndicator = true;
        public static bool rowDraggingEnabled = true;
        public static bool columnDraggingEnabled = true;
        public static int columnInsertionLineInset = 5;
        public static int dragThreshold = 5;
        public static float dragHoverDelay = 0.5f;
        public static bool requireCtrlForDrag = false;
        public static bool disableLeftClickClose = true;
        public static bool closeOnMapClick = true;
        public static bool enableContextMenuOnRightClick = true;
        public static bool persistColumnOrder = true;
        public static bool persistColumnWidths = true;
        public static bool showColumnMovedMarker = true;
        public static bool showColumnBaselineLine = true;
        public static bool showMovedColumnColorTint = true;
        public static Color Color_MovedMarkerColor = new Color(1f, 0.85f, 0.2f, 1f);
        public static bool showPawnCountAtBottom = true;
        public static bool showBedCountAtBottom = true;
        public static bool showPriorityLegend = true;
        public static bool showDragInstructions = true;
        public static bool showContextSettingsHint = true;
        public static bool showGeneralTutorial = true;
        public static bool showBetaTutorial = false;
        public static bool useRuleBuilder2 = true;
        public static bool showRuleBuilder2Tutorial = true;
        public static bool ruleBuilder2ShowWorkTabHighlights = true;
        public static bool ruleBuilder2EnableAnimations = true;
        public static bool ruleBuilder2UseDraftSuggestions = true;
        public static bool ruleBuilder2ShowAdvancedConditions = false;
        public static bool ruleBuilder2ShowMatchedPanel = true;
        public static bool showManualPrioritiesCheckbox = true;
        public static bool enableTimePriorityPlannerPrototype = true;
        public static bool showTimePriorityCopyPasteButtons = true;
        public static bool enableChronosPointerTimePriorityIntegration = true;
        public static bool showTimePriorityHourDivider = true;
        public static bool keepTimePrioritySourceColumnHighlighted = true;
        public static bool chronosPointerTimePriorityIncidentOverlay = true;
        public static bool showDividers = true;
        public static bool allowCustomDividerColors = true;
        public static bool showDividerLabels = true;
        public static bool allowDividerCollapse = true;
        public static bool enableDividerAnimations = true;
        public static bool enableRowColumnHighlights = true;
        public static float dividerMinAlpha = 0.35f;
        public static bool showHoverCellOverlay = true;
        public static BetterWorkTabSettings.SkillViewHoverMode skillViewHoverMode = BetterWorkTabSettings.SkillViewHoverMode.Standard;
        public static BetterWorkTabSettings.HoverEffectScope hoverEffectScope = BetterWorkTabSettings.HoverEffectScope.CellOnly;

        // Behavior Templates
        public enum BehaviorTemplate { Standard, Conservative, Aggressive, Custom }
        public static BehaviorTemplate behaviorTemplate = BehaviorTemplate.Standard;

        // Color Templates
        public enum ColorScheme { RimWorldDefault, Colorblind_Deuteranopia, Colorblind_Protanopia, HighContrast, Custom }
        public static ColorScheme colorScheme = ColorScheme.RimWorldDefault;

        // UI Visibility
        public static bool showWorkloadButton = true;
        public static bool showRulesetButton = true;
        public static bool hideWorkloadButton = false;
        public static bool hideAutoAssignButton = false;
        public static bool showAutoAssignConfirmation = true;
        public static bool resetWorkBeforeAutoAssign = false;
        public static bool showAutoAssignVisualFeedback = true;
        public static bool showWorkloadButtonFooter = true;
        public static bool enableWorkloadSaving = true;
        public static bool enableWorkloadLoading = true;
        public static bool persistDividersInWorkloads = true;
        public static bool alwaysShowConditionEditors = true;

        // Template Color Placeholders (for future features)
        public static Color Color_WorktypeIndicator = new Color(0.7f, 0.7f, 0.7f);
        public static Color Color_PriorityLevel1 = new Color(0.2f, 0.8f, 0.2f);
        public static Color Color_PriorityLevel2 = new Color(0.8f, 0.8f, 0.2f);
        public static Color Color_PriorityLevel3 = new Color(0.8f, 0.5f, 0.2f);
        public static Color Color_PriorityLevel4 = new Color(0.8f, 0.2f, 0.2f);
        public static Color Color_StatusEffect_Sick = new Color(0.6f, 0.4f, 0.8f);
        public static Color Color_StatusEffect_Injured = new Color(0.8f, 0.4f, 0.2f);
        public static Color Color_CustomCategory1 = new Color(0.5f, 0.7f, 0.9f);
        public static Color Color_CustomCategory2 = new Color(0.9f, 0.7f, 0.5f);
        public static Color Color_HeaderText = Color.white;
        public static Color Color_HeaderUnderline = Color.white;
        public static Color Color_DividerText = Color.white;
        public static Color Color_Borders = Color.gray;
        public static Color Color_SettingFocusHighlight = new Color(1f, 0.78f, 0.18f, 1f);

        public static Color Color_AngledHeaderText = Color.white;

        // Behavior Template Placeholders (for future features)
        public static float dragSnapThreshold = 5f; // Future: adjust snap distance
        public static float dragStartThreshold = 5f; // Future: adjust drag sensitivity
        public static float scrollSpeed = 15f; // Future: auto-scroll speed while dragging
        public static float highlightOpacity = 0.5f; // Future: opacity slider
        public static float rowSpacingScale = 1.0f; // Future: row density
        public static bool autoAssignRequireConfirmation = true; // Future: safety toggle
        public static bool confirmRulesetApplication = true; // Future: confirmation before applying ruleset
        public static int autoAssignMaxPriorityChange = 0; // Future: change limiter (0 = unlimited)

        // This rule ensures at least one colonist is assigned to a specific work type at a given priority
        public static Dictionary<WorkTypeDef, int> rule_AlwaysHaveOneByWorkType = new Dictionary<WorkTypeDef, int>();

        // This rule assigns all available colonists to a specific work type at a given priority
        public static Dictionary<WorkTypeDef, int> rule_AlwaysAssignAllByWorkType = new Dictionary<WorkTypeDef, int>();

        // Skill level colors used on the work tab
        public static Color Color_VeryLowSkill = new Color(0.82f, 0.25f, 0.25f);
        public static Color Color_LowSkill = new Color(0.95f, 0.75f, 0.20f);
        public static Color Color_GoodLowSkill = new Color(0.95f, 0.95f, 0.95f);
        public static Color Color_ExcellentSkill = new Color(0.35f, 0.85f, 0.35f);

        // New Refinement Settings
        public static bool disableBestPawnHighlight = false;
        public static int bestPawnHighlightThickness = 1;
        public static bool enableColumnGrouping = true;
        public static List<string> hiddenWorktypes = new List<string>();
        public static bool warnOnApplyRuleset = true;
        public static bool warnOnApplyWorkload = true;
        public static bool removeHeaderUnderline = false;
        public static bool enableScrollWheelPriority = true;
        public static bool enableAngledHeaders = true;
        public static int angledHeaderRotation = -60;
        public static int angledHeaderHorizontalOffset = 10;
        public static bool useVerticalStackingForCJK = true;
        public static float cjkVerticalKerning = 0.75f;
        public static bool autoEnableManualPriorities = false;
        public static WorkTabOwnerPreference preferredWorkTabOwner = WorkTabOwnerPreference.BetterWorkTab;
        public static PriorityMode priorityMode = PriorityMode.Auto;
        public static bool enableExtendedPriorities = false;
        public static bool delegateToExternalPriorityMods = true;
        public static string selectedPriorityProviderId = PriorityConstants.AutoProviderId;
        public static int autoMaxPriority = 9;
        public static BetterWorkTabSettings.AutoDisabledPriorityMode autoDisabledPriorityMode =
            BetterWorkTabSettings.AutoDisabledPriorityMode.EveryMultipleOfFour;
        public static int autoDisabledPriorityFixedValue = PriorityConstants.VanillaMax;

        // Pawn/worktype highlight visibility settings
        public static bool ShowPawnAndWorktypeHighlights = true;
        public static bool ShowCursorPawnAndWorktypeHighlight = true;
        public static bool ShowFloatMenuPawnAndWorktypeHighlight = true;
        public static bool DoSelectedPawnHighlight = true;

        // Custom highlight color settings
        public static bool UseCustomMouseHoverHighlight = false;
        public static bool ShowSimilarWorktypeHighlight = true;
        public static float SelectedPawnHighlightOpacity = 0.3f;
        public static float SimilarWorktypeHighlightOpacity = 0.4f;

        // Highlight colors (RGBA)
        public static Color Color_CursorHighlight = new Color(0.5568628f, 0.5529412f, 0.5529412f, 0.5803922f);
        public static Color Color_FloatMenuHighlight = new Color(0.5568628f, 0.5529412f, 0.5529412f, 0.5803922f);
        public static Color Color_CustomMouseHighlight = new Color(0.5568628f, 0.5529412f, 0.5529412f, 0.5803922f);
        public static Color Color_CustomSimilarWorktypeHighlight = new Color(0.5568628f, 0.5529412f, 0.5529412f, 0.5803922f);
        public static Color Color_IncapableBecauseOfCapacities = new Color(1f, 0.3f, 0.3f);
        public static Color Color_BestPawnForSkillSquare = new Color(0.35f, 0.85f, 0.35f);
        public static Color Color_RowHoverHighlight = Color_CursorHighlight;
        public static Color Color_ColumnHoverHighlight = Color_CursorHighlight;
        public static bool useRowHoverOverride = false;
        public static bool useColumnHoverOverride = false;
        public static Color Color_SelectedPawnHighlight = new Color(0.5568628f, 0.5529412f, 0.5529412f, 0.5803922f);

        // Default rulesets - all marked as isDefault: true to prevent deletion
        public static List<WorkAssignmentRuleset> SavedRulesets = new List<WorkAssignmentRuleset>
        {
            new WorkAssignmentRuleset("Vanilla Starting Pawn", new List<WorkAssignmentParameters>()
            {
                new WorkAssignmentParameters("Top 6", 3, isTopXSkill: 6),
                new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),
            }, resetBeforeApplying: true, isDefault: true),

            //new WorkAssignmentRuleset("Vanilla New Pawn", new List<WorkAssignmentParameters>()
            //{
            //    new WorkAssignmentParameters("Top 6", 3, isTopXSkill: 6),
            //    new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),
            //}, resetBeforeApplying: false, isDefault: true),

            new WorkAssignmentRuleset("BWT Default", new List<WorkAssignmentParameters>()
            {
                new WorkAssignmentParameters("Always Firefight", 1, worktypeString: "Firefighter"),
                new WorkAssignmentParameters("Best Doc", 1, worktypeString: "Doctor", hasHighestSkill: true),
                new WorkAssignmentParameters("HaulUrg if able", 2, worktypeString: "HaulUrgently", ignoreIfWorktypeNonexistent: true),
                new WorkAssignmentParameters("Childcare", 2, worktypeString: "Childcare", hasChildOnMap: true, ignoreIfWorktypeNonexistent: true),
                new WorkAssignmentParameters("Passion 2", 2, passionLevel: 2),
                new WorkAssignmentParameters("Always haul", 3, worktypeString: "Hauling"),
                new WorkAssignmentParameters("Passion 1", 3, passionLevel: 1),
                new WorkAssignmentParameters("Top 6", 3, isTopXSkill: 6),
                new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),
            }, resetBeforeApplying: true, isDefault: true),

            new WorkAssignmentRuleset("Best Pawn to 1", new List<WorkAssignmentParameters>()
            {
                new WorkAssignmentParameters("Best to 1", 1, hasHighestSkill: true),
            }, resetBeforeApplying: false, isDefault: true),

            new WorkAssignmentRuleset("Set all to 0", new List<WorkAssignmentParameters>()
            {
                new WorkAssignmentParameters("Reset", 0, allowOverwritingHigherPriority: true),
            }, resetBeforeApplying: false, isDefault: true)
        };

        public static WorkAssignmentRuleset CurrentAutoAssignRuleset = SavedRulesets[0];

        // UI mode settings for work tab visibility
        public static BetterWorkTabSettings.ShowUIMode ShowUIMode_ShowSmallSkillNumbers = BetterWorkTabSettings.ShowUIMode.Unshifted;
        public static BetterWorkTabSettings.ShowUIMode ShowUIMode_ShowPawnForSkillSquare = BetterWorkTabSettings.ShowUIMode.Shifted;

        public static int maxPriority = 9;
        public static int priorityColorPercentage_Green = 10;
        public static int priorityColorPercentage_Yellow = 50;
        public static int priorityColorPercentage_Tan = 75;
    }

    // Contains all configurable settings for Better Work Tab mod
    public class BetterWorkTabSettings : ModSettings
    {
        public BetterWorkTabSettings()
        {
            // Initialize rulesets immediately on construction
            //InitializeRulesets();
        }

        public enum SettingsViewMode
        {
            Simple,
            Advanced
        }

        public SettingsViewMode settingsViewMode = SettingsViewMode.Simple;
        public bool useOutlineHighlights = false;
        public bool enableScrollWheelPriority = DefaultSettings.enableScrollWheelPriority;
        public WorkTabOwnerPreference preferredWorkTabOwner = DefaultSettings.preferredWorkTabOwner;

        public bool firstTimeSetupDone = DefaultSettings.firstTimeSetupDone;

        // Master feature toggles
        public bool enableSkillOverlayFeature = true;
        public bool enableAutoAssignFeature = true;
        public bool enableDragDropReordering = DefaultSettings.enableDragDropReordering;
        public bool enableDividers = DefaultSettings.enableDividers;
        public bool enableWorkloads = DefaultSettings.enableWorkloads;
        public bool enableSubWorkDrilldown = DefaultSettings.enableSubWorkDrilldown;
        public bool showSubWorkHeaderBadge = DefaultSettings.showSubWorkHeaderBadge;
        public bool enableSubWorkCrossWorkDragDrop = DefaultSettings.enableSubWorkCrossWorkDragDrop;
        public bool enableCustomWorkLabels = DefaultSettings.enableCustomWorkLabels;
        public SubWorkDrilldownModifier subWorkDrilldownModifier = DefaultSettings.subWorkDrilldownModifier;
        public SubWorkDrilldownButton subWorkDrilldownButton = DefaultSettings.subWorkDrilldownButton;
        public bool useVanillaSubWorkGlobalPriorityBoxes = DefaultSettings.useVanillaSubWorkGlobalPriorityBoxes;
        public bool restoreCursorOnSubWorkExit = DefaultSettings.restoreCursorOnSubWorkExit;
        public bool restoreCursorOnSubWorkPawnCellExit = DefaultSettings.restoreCursorOnSubWorkPawnCellExit;
        public bool enableSubWorkOverrideBreakAnimation = DefaultSettings.enableSubWorkOverrideBreakAnimation;
        public bool enableSubWorkTransitionAnimation = DefaultSettings.enableSubWorkTransitionAnimation;
        public SubWorkTransitionStyle subWorkTransitionStyle = DefaultSettings.subWorkTransitionStyle;
        public float subWorkTransitionSeconds = DefaultSettings.subWorkTransitionSeconds;
        public SubWorkDisabledParentMode subWorkDisabledParentMode = DefaultSettings.subWorkDisabledParentMode;
        public bool subWorkAutoExpandColumns = DefaultSettings.subWorkAutoExpandColumns;
        public bool subWorkEvenlyExpandColumns = DefaultSettings.subWorkEvenlyExpandColumns;
        public bool enableColumnOrderSaving = DefaultSettings.enableColumnOrderSaving;
        public bool enableUIElements = DefaultSettings.enableUIElements;
        public bool enablePerformanceOptimizations = DefaultSettings.enablePerformanceOptimizations;
        public bool enableMultiplayerSync = DefaultSettings.enableMultiplayerSync;
        public bool hideSettingResetIcons = DefaultSettings.hideSettingResetIcons;
        public float dividerHeight = DefaultSettings.dividerHeight;
        public bool showOnlyLineDragIndicatorRows = DefaultSettings.showOnlyLineDragIndicatorRows;
        public bool showOnlyLineDragIndicatorColumns = DefaultSettings.showOnlyLineDragIndicatorColumns;
        public bool showGhostDragIndicator = false;
        public bool showInsertionLineIndicator = true;
        public bool rowDraggingEnabled = DefaultSettings.rowDraggingEnabled;
        public bool columnDraggingEnabled = DefaultSettings.columnDraggingEnabled;
        public int columnInsertionLineInset = DefaultSettings.columnInsertionLineInset;
        public int dragThreshold = DefaultSettings.dragThreshold;
        public float dragHoverDelay = DefaultSettings.dragHoverDelay;
        public bool requireCtrlForDrag = DefaultSettings.requireCtrlForDrag;
        public bool disableLeftClickClose = DefaultSettings.disableLeftClickClose;
        public bool closeOnMapClick = DefaultSettings.closeOnMapClick;
        public bool enableContextMenuOnRightClick = DefaultSettings.enableContextMenuOnRightClick;
        public bool showPawnCountAtBottom = DefaultSettings.showPawnCountAtBottom;
        public bool showBedCountAtBottom = DefaultSettings.showBedCountAtBottom;
        public bool showPriorityLegend = DefaultSettings.showPriorityLegend;
        public bool showDragInstructions = DefaultSettings.showDragInstructions;
        public bool showContextSettingsHint = DefaultSettings.showContextSettingsHint;
        public bool showGeneralTutorial = DefaultSettings.showGeneralTutorial;
        public int generalTutorialStep = 0;
        public bool showBetaTutorial = DefaultSettings.showBetaTutorial;
        public int betaTutorialStep = 0;
        public bool useRuleBuilder2 = DefaultSettings.useRuleBuilder2;
        public bool showRuleBuilder2Tutorial = DefaultSettings.showRuleBuilder2Tutorial;
        public int ruleBuilder2TutorialStep = 0;
        public bool ruleBuilder2ShowWorkTabHighlights = DefaultSettings.ruleBuilder2ShowWorkTabHighlights;
        public bool ruleBuilder2EnableAnimations = DefaultSettings.ruleBuilder2EnableAnimations;
        public bool ruleBuilder2UseDraftSuggestions = DefaultSettings.ruleBuilder2UseDraftSuggestions;
        public bool ruleBuilder2ShowAdvancedConditions = DefaultSettings.ruleBuilder2ShowAdvancedConditions;
        public bool ruleBuilder2ShowMatchedPanel = DefaultSettings.ruleBuilder2ShowMatchedPanel;
        public bool showManualPrioritiesCheckbox = DefaultSettings.showManualPrioritiesCheckbox;
        public bool enableTimePriorityPlannerPrototype = DefaultSettings.enableTimePriorityPlannerPrototype;
        public bool showTimePriorityCopyPasteButtons = DefaultSettings.showTimePriorityCopyPasteButtons;
        public bool enableChronosPointerTimePriorityIntegration = DefaultSettings.enableChronosPointerTimePriorityIntegration;
        public bool showTimePriorityHourDivider = DefaultSettings.showTimePriorityHourDivider;
        public bool keepTimePrioritySourceColumnHighlighted = DefaultSettings.keepTimePrioritySourceColumnHighlighted;
        public bool chronosPointerTimePriorityIncidentOverlay = DefaultSettings.chronosPointerTimePriorityIncidentOverlay;
        public bool showDividers = DefaultSettings.showDividers;
        public bool allowCustomDividerColors = DefaultSettings.allowCustomDividerColors;
        public bool showDividerLabels = DefaultSettings.showDividerLabels;
        public bool allowDividerCollapse = DefaultSettings.allowDividerCollapse;
        public bool enableDividerAnimations = DefaultSettings.enableDividerAnimations;
        public bool hideWorkloadButton = DefaultSettings.hideWorkloadButton;
        public bool hideAutoAssignButton = DefaultSettings.hideAutoAssignButton;
        public bool persistColumnOrder = DefaultSettings.persistColumnOrder;
        public bool persistColumnWidths = DefaultSettings.persistColumnWidths;
        public bool showColumnMovedMarker = DefaultSettings.showColumnMovedMarker;
        public bool showColumnBaselineLine = DefaultSettings.showColumnBaselineLine;
        public bool showMovedColumnColorTint = DefaultSettings.showMovedColumnColorTint;
        public Color movedMarkerColor = DefaultSettings.Color_MovedMarkerColor;
        public bool enableRowColumnHighlights = DefaultSettings.enableRowColumnHighlights;
        public float dividerMinAlpha = DefaultSettings.dividerMinAlpha;
        public bool showHoverCellOverlay = DefaultSettings.showHoverCellOverlay;
        public bool enableDebugLogging = false;
        public Dictionary<DebugFeature, bool> debugFeatureToggles = new Dictionary<DebugFeature, bool>
        {
            { DebugFeature.General, false },
            { DebugFeature.DragDrop, false },
            { DebugFeature.Layout, false },
            { DebugFeature.SkillOverlay, false },
            { DebugFeature.Rules, false },
            { DebugFeature.Workloads, false },
            { DebugFeature.Performance, false },
            { DebugFeature.ModSupport, false },
            { DebugFeature.SubWork, false }
        };
        public List<string> workColumnOrderDefNames = new List<string>();
        public Dictionary<string, float> storedColumnWidths = new Dictionary<string, float>();
        public WorkGiverReassignmentData LegacyWorkGiverReassignments;
        public List<string> viewedSettingIds = new List<string>();
        
        public bool debugPrintLayout = false; // Added to fix CS1061

        // Tracks which columns the player has directly dragged. Only columns in this list
        // that are also currently out of their vanilla position will show the yellow asterisk.
        // This distinguishes player-dragged columns from columns that merely shifted as a side effect.
        public List<string> playerDraggedColumns = new List<string>();

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
        public Color Color_CursorHighlight = DefaultSettings.Color_CursorHighlight;
        public Color Color_FloatMenuHighlight = new Color(0.114f, 0.737f, 0.737f, 0.5f);
        public Color Color_CustomMouseHighlight = new Color(0.737f, 0.737f, 0.114f, 0.25f);
        public Color Color_CustomSimilarWorktypeHighlight = DefaultSettings.Color_CustomSimilarWorktypeHighlight;
        public Color Color_RowHoverHighlight = DefaultSettings.Color_RowHoverHighlight;
        public Color Color_ColumnHoverHighlight = DefaultSettings.Color_ColumnHoverHighlight;
        public bool useRowHoverOverride = DefaultSettings.useRowHoverOverride;
        public bool useColumnHoverOverride = DefaultSettings.useColumnHoverOverride;
        public Color Color_SelectedPawnHighlight = DefaultSettings.Color_SelectedPawnHighlight;
        public Color Color_HeaderText = Color.white;
        public Color Color_DividerText = Color.white;
        public Color Color_Borders = Color.gray;
        public Color Color_SettingFocusHighlight = DefaultSettings.Color_SettingFocusHighlight;
        public bool ShowSimilarWorktypeHighlight = DefaultSettings.ShowSimilarWorktypeHighlight;
        public float SelectedPawnHighlightOpacity = DefaultSettings.SelectedPawnHighlightOpacity;
        public float SimilarWorktypeHighlightOpacity = DefaultSettings.SimilarWorktypeHighlightOpacity;

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
                return Color_CustomSimilarWorktypeHighlight;
            }
        }

        // Status indicator colors
        public Color Color_IncapableBecauseOfCapacities = new Color(1f, 0.3f, 0.3f);
        public Color Color_BestPawnForSkillSquare = new Color(0.35f, 0.85f, 0.35f);

        // Ruleset management
        public List<WorkAssignmentRuleset> SavedRulesets;
        public List<RuleBuilder2Ruleset> SavedRuleBuilder2Rulesets = new List<RuleBuilder2Ruleset>();
        public RuleBuilder2Ruleset CurrentRuleBuilder2Ruleset = null;
        public WorkAssignmentRuleset CurrentRuleset = null;
        public string currentRulesetName = "";
        public string currentRuleBuilder2RulesetStableId = "";

        // Auto-assign
        public bool showAutoAssignConfirmation = DefaultSettings.showAutoAssignConfirmation;
        public bool resetWorkBeforeAutoAssign = DefaultSettings.resetWorkBeforeAutoAssign;
        public bool showAutoAssignVisualFeedback = DefaultSettings.showAutoAssignVisualFeedback;
        public string defaultAutoAssignRuleset = "BWT Default";

        // Workloads
        public bool showWorkloadButtonFooter = DefaultSettings.showWorkloadButtonFooter;
        public bool enableWorkloadSaving = DefaultSettings.enableWorkloadSaving;
        public bool enableWorkloadLoading = DefaultSettings.enableWorkloadLoading;
        public bool persistDividersInWorkloads = DefaultSettings.persistDividersInWorkloads;
        public bool alwaysShowConditionEditors = DefaultSettings.alwaysShowConditionEditors;

        // Performance
        public bool cacheBedCounts = true;
        public bool cacheSkillLevels = true;
        public bool cacheRowDescriptors = true;
        public bool cacheIncapabilityChecks = true;
        public bool useElementPooling = true;
        public bool viewportCulling = true;

        // Debug and profiling
        public bool enableProfiler = false;
        public bool logDebugToFile = false;

        // Multiplayer
        public bool mpSyncColumnOrder = true;
        public bool mpSyncWorkloads = true;
        public bool mpSyncRulesets = true;
        public enum MpConflictMode { PlayerPriority, HostPriority, AskPlayer }
        public MpConflictMode mpConflictMode = MpConflictMode.PlayerPriority;

        // Max Priority Int Settings
        public const int MAX_PRIORITY_HARD_LIMIT = 99;
        public const int MAX_PRIORITY_MINIMUM = 4;

        public PriorityMode priorityMode = DefaultSettings.priorityMode;
        public bool enableExtendedPriorities = DefaultSettings.enableExtendedPriorities;
        public bool delegateToExternalPriorityMods = DefaultSettings.delegateToExternalPriorityMods;
        public string selectedPriorityProviderId = DefaultSettings.selectedPriorityProviderId;
        public int autoMaxPriorityInt = DefaultSettings.autoMaxPriority;
        public int maxPriorityInt = DefaultSettings.maxPriority;
        public AutoDisabledPriorityMode autoDisabledPriorityMode = DefaultSettings.autoDisabledPriorityMode;
        public int autoDisabledPriorityFixedValue = DefaultSettings.autoDisabledPriorityFixedValue;
        public int priorityColorPercentage_Green = 10;
        public int priorityColorPercentage_Yellow = 50;
        public int priorityColorPercentage_Tan = 75;

        public int EffectiveMaxPriority => WorkPrioritySystem.GetMaxPriority();

        public static int NormalizeMaxPriority(int value)
        {
            return Mathf.Clamp(value, MAX_PRIORITY_MINIMUM, MAX_PRIORITY_HARD_LIMIT);
        }

        public bool mpShowOtherPlayersHover = DefaultSettings.mpShowOtherPlayersHover;
        public bool mpAllowOthersToRequestLayout = DefaultSettings.mpAllowOthersToRequestLayout;
        public bool mpAllowPresenceBroadcast = DefaultSettings.mpAllowPresenceBroadcast;
        public bool mpShowLinkedIndicator = DefaultSettings.mpShowLinkedIndicator;

        /// <summary>
        /// Unique identifier for this BWT installation in multiplayer
        /// Auto-generated but user-configurable
        /// </summary>
        public string bwtPlayerIdentifier = "";

        // Future behavior templates
        public bool confirmRulesetApplication = true;
        public float dragStartThreshold = 5f;
        public float scrollSpeed = 15f;

        // Dividers
        public bool highlightDividersOnHover = true;

        // UI mode settings
        public enum ShowUIMode { Always, Never, Shifted, Unshifted }
        public enum SubWorkDrilldownModifier { Ctrl, Shift }
        public enum SubWorkDrilldownButton { Left, Right }
        public enum SubWorkDisabledParentMode
        {
            ParentWorkDisablesSubWork,
            LockedSubWorkOverridesParent
        }
        public enum SubWorkTransitionStyle
        {
            ClassicGlideFlash,
            PixelWaveFlip
        }
        public enum AutoDisabledPriorityMode
        {
            EveryMultipleOfFour,
            FixedPriority
        }
        public enum SkillViewHoverMode
        {
            Standard,      // Vanilla: interactive priority box with small skill number
            SkillFocused,  // Big skill number with small priority in corner
            None           // Hover does nothing special
        }
        public enum HoverEffectScope
        {
            CellOnly,
            ColumnWide
        }
        public ShowUIMode ShowUIMode_ShowSmallSkillNumbers = ShowUIMode.Unshifted;
        public ShowUIMode ShowUIMode_ShowPawnForSkillSquare = ShowUIMode.Shifted;
        public SkillViewHoverMode skillViewHoverMode = DefaultSettings.skillViewHoverMode;
        public HoverEffectScope hoverEffectScope = DefaultSettings.hoverEffectScope;
        public bool disableBestPawnHighlight = DefaultSettings.disableBestPawnHighlight;
        public int bestPawnHighlightThickness = DefaultSettings.bestPawnHighlightThickness;
        public bool enableColumnGrouping = DefaultSettings.enableColumnGrouping;
        public List<string> hiddenWorktypes = new List<string>(DefaultSettings.hiddenWorktypes);
        public bool warnOnApplyRuleset = DefaultSettings.warnOnApplyRuleset;
        public bool warnOnApplyWorkload = DefaultSettings.warnOnApplyWorkload;
        public bool removeHeaderUnderline = DefaultSettings.removeHeaderUnderline;
        public bool enableAngledHeaders = DefaultSettings.enableAngledHeaders;
        public int angledHeaderRotation = (int)DefaultSettings.angledHeaderRotation;
        public int angledHeaderHorizontalOffset = DefaultSettings.angledHeaderHorizontalOffset;
        public bool useVerticalStackingForCJK = DefaultSettings.useVerticalStackingForCJK;
        public float cjkVerticalKerning = DefaultSettings.cjkVerticalKerning;
        public Color angledHeaderColor = DefaultSettings.Color_AngledHeaderText;
        public Color headerUnderlineColor = DefaultSettings.Color_HeaderUnderline;
        public bool autoEnableManualPriorities = DefaultSettings.autoEnableManualPriorities;


        public float workTabMaxHeight = DefaultSettings.workTabMaxHeight;
        public int workTabMaxVisiblePawns = DefaultSettings.workTabMaxVisiblePawns;
        public float workTabTopSpace = DefaultSettings.workTabTopSpace;

        public enum RulesetViewMode
        {
            Raw,
            Regular,
            Both
        }
        public RulesetViewMode rulesetViewMode = RulesetViewMode.Regular;

        /// <summary>
        /// Creates/restores all default rulesets from the static defaults.
        /// </summary>
        public void CreateDefaultRulesets()
        {
            SavedRulesets = CloneDefaultRulesets();
            SyncWorktypeReferences(SavedRulesets);
            CurrentRuleset = SelectPreferredRuleset();
            currentRulesetName = CurrentRuleset?.Name ?? "";
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

            foreach (var ruleset in CloneDefaultRulesets())
            {
                if (!SavedRulesets.Any(rs => string.Equals(rs.Name, ruleset.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    SavedRulesets.Add(ruleset);
                }
            }
        }

        private static List<WorkAssignmentRuleset> CloneDefaultRulesets()
        {
            return DefaultSettings.SavedRulesets
                .Select(CloneRulesetTemplate)
                .ToList();
        }

        private static WorkAssignmentRuleset CloneRulesetTemplate(WorkAssignmentRuleset template)
        {
            var clonedRules = template.Rules?
                .Select(rule =>
                {
                    var clonedRule = new WorkAssignmentRule(
                        rule.Name,
                        rule.Parameters?.Copy() ?? new WorkAssignmentParameters(),
                        rule.CachedWorktype);
                    clonedRule.CachedWorktypeString = rule.CachedWorktypeString;
                    return clonedRule;
                })
                .ToList() ?? new List<WorkAssignmentRule>();

            return new WorkAssignmentRuleset(
                template.Name,
                clonedRules,
                template.ResetBeforeApplying,
                template.IsDefault);
        }

        private static void SyncWorktypeReferences(IEnumerable<WorkAssignmentRuleset> rulesets)
        {
            if (rulesets == null)
            {
                return;
            }

            foreach (var ruleset in rulesets)
            {
                foreach (var rule in ruleset.Rules)
                {
                    var parameters = rule.Parameters;
                    if (parameters == null)
                    {
                        continue;
                    }

                    if (parameters.Worktype == null && !string.IsNullOrEmpty(parameters.WorktypeString))
                    {
                        parameters.Worktype = DefDatabase<WorkTypeDef>.GetNamedSilentFail(parameters.WorktypeString);
                    }
                    else if (parameters.Worktype != null && string.IsNullOrEmpty(parameters.WorktypeString))
                    {
                        parameters.WorktypeString = parameters.Worktype.defName;
                    }

                    if (rule.CachedWorktype == null && !string.IsNullOrEmpty(rule.CachedWorktypeString))
                    {
                        rule.CachedWorktype = DefDatabase<WorkTypeDef>.GetNamedSilentFail(rule.CachedWorktypeString);
                    }
                    else if (rule.CachedWorktype != null && string.IsNullOrEmpty(rule.CachedWorktypeString))
                    {
                        rule.CachedWorktypeString = rule.CachedWorktype.defName;
                    }
                }
            }
        }

        private WorkAssignmentRuleset SelectPreferredRuleset()
        {
            if (SavedRulesets == null || !SavedRulesets.Any())
            {
                return null;
            }

            if (!string.IsNullOrEmpty(currentRulesetName))
            {
                var selected = SavedRulesets.FirstOrDefault(rs =>
                    string.Equals(rs.Name, currentRulesetName, StringComparison.OrdinalIgnoreCase));

                if (selected != null)
                {
                    return selected;
                }
            }

            var preferred = SavedRulesets.FirstOrDefault(rs =>
                string.Equals(rs.Name, defaultAutoAssignRuleset, StringComparison.OrdinalIgnoreCase));

            return preferred ?? SavedRulesets.First();
        }

        public void SetCurrentRuleset(WorkAssignmentRuleset ruleset, bool writeSettings = true)
        {
            CurrentRuleset = ruleset;
            currentRulesetName = ruleset?.Name ?? "";

            if (writeSettings)
            {
                Write();
            }
        }

        public void SaveOrReplaceRuleBuilder2Ruleset(RuleBuilder2Ruleset ruleset, bool makeCurrent = true, bool writeSettings = true)
        {
            RuleBuilder2RulesetStore.SaveOrReplace(this, ruleset, makeCurrent, writeSettings);
        }

        public void SetCurrentRuleBuilder2Ruleset(RuleBuilder2Ruleset ruleset, bool writeSettings = true)
        {
            RuleBuilder2RulesetStore.SetCurrent(this, ruleset, writeSettings);
        }

        public void SetPriorityMode(PriorityMode mode)
        {
            priorityMode = mode;
            SyncProviderSelectionFieldsFromMode();
        }

        public void SetExternalPriorityProvider(string providerId)
        {
            priorityMode = PriorityMode.ExternalProvider;
            selectedPriorityProviderId = string.IsNullOrWhiteSpace(providerId)
                ? DefaultSettings.selectedPriorityProviderId
                : providerId.Trim();
            SyncProviderSelectionFieldsFromMode();
        }

        public void NormalizePrioritySettings()
        {
            if (!Enum.IsDefined(typeof(PriorityMode), priorityMode))
            {
                priorityMode = InferPriorityModeFromProviderSelectionFields();
            }

            if (string.IsNullOrWhiteSpace(selectedPriorityProviderId))
            {
                selectedPriorityProviderId = DefaultSettings.selectedPriorityProviderId;
            }

            autoMaxPriorityInt = Math.Min(
                Math.Max(autoMaxPriorityInt, PriorityConstants.VanillaMax),
                MAX_PRIORITY_HARD_LIMIT);
            maxPriorityInt = NormalizeMaxPriority(maxPriorityInt);

            if (!Enum.IsDefined(typeof(AutoDisabledPriorityMode), autoDisabledPriorityMode))
            {
                autoDisabledPriorityMode = DefaultSettings.autoDisabledPriorityMode;
            }

            autoDisabledPriorityFixedValue = Math.Min(
                Math.Max(autoDisabledPriorityFixedValue, 1),
                MAX_PRIORITY_HARD_LIMIT);

            SyncProviderSelectionFieldsFromMode();
        }

        private PriorityMode InferPriorityModeFromProviderSelectionFields()
        {
            if (enableExtendedPriorities ||
                IsPriorityProviderId(selectedPriorityProviderId, PriorityConstants.BwtProviderId))
            {
                return PriorityMode.BetterWorkTab;
            }

            if (!delegateToExternalPriorityMods ||
                IsPriorityProviderId(selectedPriorityProviderId, PriorityConstants.VanillaProviderId))
            {
                return PriorityMode.Vanilla;
            }

            if (string.IsNullOrWhiteSpace(selectedPriorityProviderId) ||
                IsPriorityProviderId(selectedPriorityProviderId, PriorityConstants.AutoProviderId))
            {
                return PriorityMode.Auto;
            }

            return PriorityMode.ExternalProvider;
        }

        private void SyncProviderSelectionFieldsFromMode()
        {
            switch (priorityMode)
            {
                case PriorityMode.Vanilla:
                    enableExtendedPriorities = false;
                    delegateToExternalPriorityMods = false;
                    selectedPriorityProviderId = PriorityConstants.VanillaProviderId;
                    break;
                case PriorityMode.Auto:
                    enableExtendedPriorities = false;
                    delegateToExternalPriorityMods = true;
                    selectedPriorityProviderId = PriorityConstants.AutoProviderId;
                    break;
                case PriorityMode.ExternalProvider:
                    enableExtendedPriorities = false;
                    delegateToExternalPriorityMods = true;
                    selectedPriorityProviderId = string.IsNullOrWhiteSpace(selectedPriorityProviderId)
                        ? DefaultSettings.selectedPriorityProviderId
                        : selectedPriorityProviderId.Trim();
                    break;
                case PriorityMode.BetterWorkTab:
                    enableExtendedPriorities = true;
                    delegateToExternalPriorityMods = true;
                    selectedPriorityProviderId = PriorityConstants.BwtProviderId;
                    break;
            }
        }

        private static bool IsPriorityProviderId(string providerId, string expectedProviderId)
        {
            return string.Equals(
                providerId?.Trim(),
                expectedProviderId,
                StringComparison.OrdinalIgnoreCase);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref workTabMaxHeight, "workTabMaxHeight", DefaultSettings.workTabMaxHeight);
            Scribe_Values.Look(ref workTabMaxVisiblePawns, "workTabMaxVisiblePawns", DefaultSettings.workTabMaxVisiblePawns);
            Scribe_Values.Look(ref workTabTopSpace, "workTabTopSpace", DefaultSettings.workTabTopSpace);


            // Core feature toggles
            Scribe_Values.Look(ref firstTimeSetupDone, "firstTimeSetupDone", DefaultSettings.firstTimeSetupDone);
            Scribe_Values.Look(ref enableSkillOverlayFeature, "enableSkillOverlayFeature", DefaultSettings.enableSkillOverlayFeature);
            Scribe_Values.Look(ref enableAutoAssignFeature, "enableAutoAssignFeature", DefaultSettings.enableAutoAssignFeature);
            Scribe_Values.Look(ref enableDragDropReordering, "enableDragDropReordering", DefaultSettings.enableDragDropReordering);
            Scribe_Values.Look(ref enableDividers, "enableDividers", DefaultSettings.enableDividers);
            Scribe_Values.Look(ref enableWorkloads, "enableWorkloads", DefaultSettings.enableWorkloads);
            Scribe_Values.Look(ref enableSubWorkDrilldown, "enableSubWorkDrilldown", DefaultSettings.enableSubWorkDrilldown);
            Scribe_Values.Look(ref showSubWorkHeaderBadge, "showSubWorkHeaderBadge", DefaultSettings.showSubWorkHeaderBadge);
            Scribe_Values.Look(ref enableSubWorkCrossWorkDragDrop, "enableSubWorkCrossWorkDragDrop", DefaultSettings.enableSubWorkCrossWorkDragDrop);
            Scribe_Values.Look(ref enableCustomWorkLabels, "enableCustomWorkLabels", DefaultSettings.enableCustomWorkLabels);
            Scribe_Values.Look(ref subWorkDrilldownModifier, "subWorkDrilldownModifier", DefaultSettings.subWorkDrilldownModifier);
            Scribe_Values.Look(ref subWorkDrilldownButton, "subWorkDrilldownButton", DefaultSettings.subWorkDrilldownButton);
            Scribe_Values.Look(ref useVanillaSubWorkGlobalPriorityBoxes, "useVanillaSubWorkGlobalPriorityBoxes", DefaultSettings.useVanillaSubWorkGlobalPriorityBoxes);
            Scribe_Values.Look(ref restoreCursorOnSubWorkExit, "restoreCursorOnSubWorkExit", DefaultSettings.restoreCursorOnSubWorkExit);
            Scribe_Values.Look(ref restoreCursorOnSubWorkPawnCellExit, "restoreCursorOnSubWorkPawnCellExit", DefaultSettings.restoreCursorOnSubWorkPawnCellExit);
            Scribe_Values.Look(ref enableSubWorkOverrideBreakAnimation, "enableSubWorkOverrideBreakAnimation", DefaultSettings.enableSubWorkOverrideBreakAnimation);
            Scribe_Values.Look(ref enableSubWorkTransitionAnimation, "enableSubWorkTransitionAnimation", DefaultSettings.enableSubWorkTransitionAnimation);
            Scribe_Values.Look(ref subWorkTransitionStyle, "subWorkTransitionStyle", DefaultSettings.subWorkTransitionStyle);
            Scribe_Values.Look(ref subWorkTransitionSeconds, "subWorkTransitionSeconds", DefaultSettings.subWorkTransitionSeconds);
            subWorkTransitionSeconds = ClampSubWorkTransitionSeconds(subWorkTransitionSeconds);
            Scribe_Values.Look(ref subWorkDisabledParentMode, "subWorkDisabledParentMode", DefaultSettings.subWorkDisabledParentMode);
            Scribe_Values.Look(ref subWorkAutoExpandColumns, "subWorkAutoExpandColumns", DefaultSettings.subWorkAutoExpandColumns);
            Scribe_Values.Look(ref subWorkEvenlyExpandColumns, "subWorkEvenlyExpandColumns", DefaultSettings.subWorkEvenlyExpandColumns);
            Scribe_Values.Look(ref enableColumnOrderSaving, "enableColumnOrderSaving", DefaultSettings.enableColumnOrderSaving);
            Scribe_Values.Look(ref enableUIElements, "enableUIElements", DefaultSettings.enableUIElements);
            Scribe_Values.Look(ref enableTimePriorityPlannerPrototype, "enableTimePriorityPlannerPrototype", DefaultSettings.enableTimePriorityPlannerPrototype);
            Scribe_Values.Look(ref showTimePriorityCopyPasteButtons, "showTimePriorityCopyPasteButtons", DefaultSettings.showTimePriorityCopyPasteButtons);
            Scribe_Values.Look(ref enableChronosPointerTimePriorityIntegration, "enableChronosPointerTimePriorityIntegration", DefaultSettings.enableChronosPointerTimePriorityIntegration);
            Scribe_Values.Look(ref showTimePriorityHourDivider, "showTimePriorityHourDivider", DefaultSettings.showTimePriorityHourDivider);
            Scribe_Values.Look(ref keepTimePrioritySourceColumnHighlighted, "keepTimePrioritySourceColumnHighlighted", DefaultSettings.keepTimePrioritySourceColumnHighlighted);
            Scribe_Values.Look(ref chronosPointerTimePriorityIncidentOverlay, "chronosPointerTimePriorityIncidentOverlay", DefaultSettings.chronosPointerTimePriorityIncidentOverlay);
            Scribe_Values.Look(ref enablePerformanceOptimizations, "enablePerformanceOptimizations", DefaultSettings.enablePerformanceOptimizations);
            Scribe_Values.Look(ref enableMultiplayerSync, "enableMultiplayerSync", DefaultSettings.enableMultiplayerSync);
            Scribe_Values.Look(ref hideSettingResetIcons, "hideSettingResetIcons", DefaultSettings.hideSettingResetIcons);
            Scribe_Values.Look(ref settingsViewMode, "settingsViewMode", SettingsViewMode.Simple);

            // Highlight settings
            Scribe_Values.Look(ref ShowPawnAndWorktypeHighlights, "ShowPawnAndWorktypeHighlights", DefaultSettings.ShowPawnAndWorktypeHighlights);
            Scribe_Values.Look(ref ShowCursorPawnAndWorktypeHighlight, "ShowCursorPawnAndWorktypeHighlight", DefaultSettings.ShowCursorPawnAndWorktypeHighlight);
            Scribe_Values.Look(ref ShowFloatMenuPawnAndWorktypeHighlight, "ShowFloatMenuPawnAndWorktypeHighlight", DefaultSettings.ShowFloatMenuPawnAndWorktypeHighlight);
            Scribe_Values.Look(ref DoSelectedPawnHighlight, "DoSelectedPawnHighlight", DefaultSettings.DoSelectedPawnHighlight);
            Scribe_Values.Look(ref UseCustomMouseHoverHighlight, "UseCustomMouseHoverHighlight", DefaultSettings.UseCustomMouseHoverHighlight);
            Scribe_Values.Look(ref enableRowColumnHighlights, "enableRowColumnHighlights", DefaultSettings.enableRowColumnHighlights);
            Scribe_Values.Look(ref ShowSimilarWorktypeHighlight, "ShowSimilarWorktypeHighlight", DefaultSettings.ShowSimilarWorktypeHighlight);
            Scribe_Values.Look(ref useOutlineHighlights, "useOutlineHighlights", false);
            Scribe_Values.Look(ref useRowHoverOverride, "useRowHoverOverride", DefaultSettings.useRowHoverOverride);
            Scribe_Values.Look(ref useColumnHoverOverride, "useColumnHoverOverride", DefaultSettings.useColumnHoverOverride);
            Scribe_Values.Look(ref SelectedPawnHighlightOpacity, "SelectedPawnHighlightOpacity", DefaultSettings.SelectedPawnHighlightOpacity);
            Scribe_Values.Look(ref SimilarWorktypeHighlightOpacity, "SimilarWorktypeHighlightOpacity", DefaultSettings.SimilarWorktypeHighlightOpacity);

            // UI display settings
            Scribe_Values.Look(ref showPawnCountAtBottom, "showPawnCountAtBottom", DefaultSettings.showPawnCountAtBottom);
            Scribe_Values.Look(ref showBedCountAtBottom, "showBedCountAtBottom", DefaultSettings.showBedCountAtBottom);
            Scribe_Values.Look(ref disableLeftClickClose, "disableLeftClickClose", DefaultSettings.disableLeftClickClose);
            Scribe_Values.Look(ref closeOnMapClick, "closeOnMapClick", DefaultSettings.closeOnMapClick);
            Scribe_Values.Look(ref enableContextMenuOnRightClick, "enableContextMenuOnRightClick", DefaultSettings.enableContextMenuOnRightClick);
            Scribe_Values.Look(ref requireCtrlForDrag, "requireCtrlForDrag", DefaultSettings.requireCtrlForDrag);
            Scribe_Values.Look(ref showPriorityLegend, "showPriorityLegend", DefaultSettings.showPriorityLegend);
            Scribe_Values.Look(ref showDragInstructions, "showDragInstructions", DefaultSettings.showDragInstructions);
            Scribe_Values.Look(ref showContextSettingsHint, "showContextSettingsHint", DefaultSettings.showContextSettingsHint);
            Scribe_Values.Look(ref showGeneralTutorial, "showGeneralTutorial", DefaultSettings.showGeneralTutorial);
            Scribe_Values.Look(ref generalTutorialStep, "generalTutorialStep", 0);
            Scribe_Values.Look(ref showBetaTutorial, "showBetaTutorial", DefaultSettings.showBetaTutorial);
            Scribe_Values.Look(ref betaTutorialStep, "betaTutorialStep", 0);
            Scribe_Values.Look(ref useRuleBuilder2, "useRuleBuilder2", DefaultSettings.useRuleBuilder2);
            Scribe_Values.Look(ref showRuleBuilder2Tutorial, "showRuleBuilder2Tutorial", DefaultSettings.showRuleBuilder2Tutorial);
            Scribe_Values.Look(ref ruleBuilder2TutorialStep, "ruleBuilder2TutorialStep", 0);
            Scribe_Values.Look(ref ruleBuilder2ShowWorkTabHighlights, "ruleBuilder2ShowWorkTabHighlights", DefaultSettings.ruleBuilder2ShowWorkTabHighlights);
            Scribe_Values.Look(ref ruleBuilder2EnableAnimations, "ruleBuilder2EnableAnimations", DefaultSettings.ruleBuilder2EnableAnimations);
            Scribe_Values.Look(ref ruleBuilder2UseDraftSuggestions, "ruleBuilder2UseDraftSuggestions", DefaultSettings.ruleBuilder2UseDraftSuggestions);
            Scribe_Values.Look(ref ruleBuilder2ShowAdvancedConditions, "ruleBuilder2ShowAdvancedConditions", DefaultSettings.ruleBuilder2ShowAdvancedConditions);
            Scribe_Values.Look(ref ruleBuilder2ShowMatchedPanel, "ruleBuilder2ShowMatchedPanel", DefaultSettings.ruleBuilder2ShowMatchedPanel);
            Scribe_Values.Look(ref showManualPrioritiesCheckbox, "showManualPrioritiesCheckbox", DefaultSettings.showManualPrioritiesCheckbox);
            Scribe_Values.Look(ref showDividers, "showDividers", DefaultSettings.showDividers);
            Scribe_Values.Look(ref allowCustomDividerColors, "allowCustomDividerColors", DefaultSettings.allowCustomDividerColors);
            Scribe_Values.Look(ref showDividerLabels, "showDividerLabels", DefaultSettings.showDividerLabels);
            Scribe_Values.Look(ref allowDividerCollapse, "allowDividerCollapse", DefaultSettings.allowDividerCollapse);
            Scribe_Values.Look(ref enableDividerAnimations, "enableDividerAnimations", DefaultSettings.enableDividerAnimations);
            Scribe_Values.Look(ref showOnlyLineDragIndicatorRows, "showOnlyLineDragIndicatorRows", true);
            Scribe_Values.Look(ref showOnlyLineDragIndicatorColumns, "showOnlyLineDragIndicatorColumns", true);
            Scribe_Values.Look(ref showGhostDragIndicator, "showGhostDragIndicator", false);
            Scribe_Values.Look(ref showInsertionLineIndicator, "showInsertionLineIndicator", true);
            Scribe_Values.Look(ref rowDraggingEnabled, "rowDraggingEnabled", DefaultSettings.rowDraggingEnabled);
            Scribe_Values.Look(ref columnDraggingEnabled, "columnDraggingEnabled", DefaultSettings.columnDraggingEnabled);
            Scribe_Values.Look(ref columnInsertionLineInset, "columnInsertionLineInset", DefaultSettings.columnInsertionLineInset);
            Scribe_Values.Look(ref dragThreshold, "dragThreshold", DefaultSettings.dragThreshold);
            Scribe_Values.Look(ref dragHoverDelay, "dragHoverDelay", DefaultSettings.dragHoverDelay);
            Scribe_Values.Look(ref hideWorkloadButton, "hideWorkloadButton", DefaultSettings.hideWorkloadButton);
            Scribe_Values.Look(ref hideAutoAssignButton, "hideAutoAssignButton", DefaultSettings.hideAutoAssignButton);
            Scribe_Values.Look(ref persistColumnOrder, "persistColumnOrder", DefaultSettings.persistColumnOrder);
            Scribe_Values.Look(ref persistColumnWidths, "persistColumnWidths", DefaultSettings.persistColumnWidths);
            Scribe_Values.Look(ref showColumnMovedMarker, "showColumnMovedMarker", DefaultSettings.showColumnMovedMarker);
            Scribe_Values.Look(ref showColumnBaselineLine, "showColumnBaselineLine", DefaultSettings.showColumnBaselineLine);
            Scribe_Values.Look(ref showMovedColumnColorTint, "showMovedColumnColorTint", DefaultSettings.showMovedColumnColorTint);
            Scribe_Values.Look(ref movedMarkerColor, "movedMarkerColor", DefaultSettings.Color_MovedMarkerColor);
            Scribe_Values.Look(ref dividerMinAlpha, "dividerMinAlpha", DefaultSettings.dividerMinAlpha);
            Scribe_Values.Look(ref disableBestPawnHighlight, "disableBestPawnHighlight", false);
            Scribe_Values.Look(ref bestPawnHighlightThickness, "bestPawnHighlightThickness", 1);
            Scribe_Values.Look(ref enableColumnGrouping, "enableColumnGrouping", false);
            Scribe_Collections.Look(ref hiddenWorktypes, "hiddenWorktypes", LookMode.Value);
            Scribe_Values.Look(ref warnOnApplyRuleset, "warnOnApplyRuleset", true);
            Scribe_Values.Look(ref warnOnApplyWorkload, "warnOnApplyWorkload", true);
            Scribe_Values.Look(ref removeHeaderUnderline, "removeHeaderUnderline", false);
            Scribe_Values.Look(ref enableScrollWheelPriority, "enableScrollWheelPriority", DefaultSettings.enableScrollWheelPriority);
            Scribe_Values.Look(ref enableAngledHeaders, "enableAngledHeaders", DefaultSettings.enableAngledHeaders);
            Scribe_Values.Look(ref angledHeaderRotation, "angledHeaderRotation", (int)DefaultSettings.angledHeaderRotation);
            Scribe_Values.Look(ref angledHeaderHorizontalOffset, "angledHeaderHorizontalOffset", DefaultSettings.angledHeaderHorizontalOffset);
            Scribe_Values.Look(ref useVerticalStackingForCJK, "useVerticalStackingForCJK", true);
            Scribe_Values.Look(ref cjkVerticalKerning, "cjkVerticalKerning", 0.75f);
            Scribe_Values.Look(ref angledHeaderColor, "angledHeaderColor", DefaultSettings.Color_AngledHeaderText);
            Scribe_Values.Look(ref headerUnderlineColor, "headerUnderlineColor", DefaultSettings.Color_HeaderUnderline);
            Scribe_Values.Look(ref autoEnableManualPriorities, "autoEnableManualPriorities", DefaultSettings.autoEnableManualPriorities);
            Scribe_Values.Look(ref preferredWorkTabOwner, "preferredWorkTabOwner", DefaultSettings.preferredWorkTabOwner);
            Scribe_Values.Look(ref enableExtendedPriorities, "enableExtendedPriorities", DefaultSettings.enableExtendedPriorities);
            Scribe_Values.Look(ref delegateToExternalPriorityMods, "delegateToExternalPriorityMods", DefaultSettings.delegateToExternalPriorityMods);
            Scribe_Values.Look(ref selectedPriorityProviderId, "selectedPriorityProviderId", DefaultSettings.selectedPriorityProviderId);
            PriorityMode inferredPriorityMode = InferPriorityModeFromProviderSelectionFields();
            Scribe_Values.Look(ref priorityMode, "priorityMode", inferredPriorityMode);
            Scribe_Values.Look(ref autoMaxPriorityInt, "autoMaxPriorityInt", DefaultSettings.autoMaxPriority);
            Scribe_Values.Look(ref maxPriorityInt, "maxPriorityInt", DefaultSettings.maxPriority);
            Scribe_Values.Look(ref autoDisabledPriorityMode, "autoDisabledPriorityMode", DefaultSettings.autoDisabledPriorityMode);
            Scribe_Values.Look(ref autoDisabledPriorityFixedValue, "autoDisabledPriorityFixedValue", DefaultSettings.autoDisabledPriorityFixedValue);
            Scribe_Values.Look(ref priorityColorPercentage_Green, "priorityColorPercentage_Green", DefaultSettings.priorityColorPercentage_Green);
            Scribe_Values.Look(ref priorityColorPercentage_Yellow , "priorityColorPercentage_Yellow", DefaultSettings.priorityColorPercentage_Yellow );
            Scribe_Values.Look(ref priorityColorPercentage_Tan , "priorityColorPercentage_Tan", DefaultSettings.priorityColorPercentage_Tan );
            NormalizePrioritySettings();

            if (hiddenWorktypes == null) hiddenWorktypes = new List<string>();

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
            Scribe_Values.Look(ref Color_RowHoverHighlight, "Color_RowHoverHighlight", DefaultSettings.Color_RowHoverHighlight);
            Scribe_Values.Look(ref Color_ColumnHoverHighlight, "Color_ColumnHoverHighlight", DefaultSettings.Color_ColumnHoverHighlight);
            Scribe_Values.Look(ref Color_SelectedPawnHighlight, "Color_SelectedPawnHighlight", DefaultSettings.Color_SelectedPawnHighlight);
            Scribe_Values.Look(ref Color_HeaderText, "Color_HeaderText", DefaultSettings.Color_HeaderText);
            Scribe_Values.Look(ref Color_DividerText, "Color_DividerText", DefaultSettings.Color_DividerText);
            Scribe_Values.Look(ref Color_Borders, "Color_Borders", DefaultSettings.Color_Borders);
            Scribe_Values.Look(ref Color_SettingFocusHighlight, "Color_SettingFocusHighlight", DefaultSettings.Color_SettingFocusHighlight);

            // UI modes
            Scribe_Values.Look(ref ShowUIMode_ShowSmallSkillNumbers, "ShowUIMode_ShowSmallSkillNumbers", DefaultSettings.ShowUIMode_ShowSmallSkillNumbers);
            Scribe_Values.Look(ref ShowUIMode_ShowPawnForSkillSquare, "ShowUIMode_ShowPawnForSkillSquare", DefaultSettings.ShowUIMode_ShowPawnForSkillSquare);
            Scribe_Values.Look(ref hoverEffectScope, "hoverEffectScope", DefaultSettings.hoverEffectScope);

            // Future behavior templates
            Scribe_Values.Look(ref confirmRulesetApplication, "confirmRulesetApplication", DefaultSettings.confirmRulesetApplication);
            Scribe_Values.Look(ref dragStartThreshold, "dragStartThreshold", DefaultSettings.dragStartThreshold);
            Scribe_Values.Look(ref scrollSpeed, "scrollSpeed", DefaultSettings.scrollSpeed);
            Scribe_Values.Look(ref showAutoAssignConfirmation, "showAutoAssignConfirmation", DefaultSettings.showAutoAssignConfirmation);
            Scribe_Values.Look(ref resetWorkBeforeAutoAssign, "resetWorkBeforeAutoAssign", DefaultSettings.resetWorkBeforeAutoAssign);
            Scribe_Values.Look(ref showAutoAssignVisualFeedback, "showAutoAssignVisualFeedback", DefaultSettings.showAutoAssignVisualFeedback);
            if (Scribe.mode == LoadSaveMode.Saving && CurrentRuleset != null)
            {
                currentRulesetName = CurrentRuleset.Name;
            }

            if (Scribe.mode == LoadSaveMode.Saving && CurrentRuleBuilder2Ruleset != null)
            {
                currentRuleBuilder2RulesetStableId = CurrentRuleBuilder2Ruleset.StableId;
            }

            Scribe_Values.Look(ref defaultAutoAssignRuleset, "defaultAutoAssignRuleset", "BWT Default");
            Scribe_Values.Look(ref currentRulesetName, "currentRulesetName", "");
            Scribe_Values.Look(ref currentRuleBuilder2RulesetStableId, "currentRuleBuilder2RulesetStableId", "");
            Scribe_Values.Look(ref showWorkloadButtonFooter, "showWorkloadButtonFooter", DefaultSettings.showWorkloadButtonFooter);
            Scribe_Values.Look(ref enableWorkloadSaving, "enableWorkloadSaving", DefaultSettings.enableWorkloadSaving);
            Scribe_Values.Look(ref enableWorkloadLoading, "enableWorkloadLoading", DefaultSettings.enableWorkloadLoading);
            Scribe_Values.Look(ref persistDividersInWorkloads, "persistDividersInWorkloads", DefaultSettings.persistDividersInWorkloads);
            Scribe_Values.Look(ref alwaysShowConditionEditors, "alwaysShowConditionEditors", DefaultSettings.alwaysShowConditionEditors);
            Scribe_Values.Look(ref cacheBedCounts, "cacheBedCounts", true);
            Scribe_Values.Look(ref cacheSkillLevels, "cacheSkillLevels", true);
            Scribe_Values.Look(ref cacheRowDescriptors, "cacheRowDescriptors", true);
            Scribe_Values.Look(ref cacheIncapabilityChecks, "cacheIncapabilityChecks", true);
            Scribe_Values.Look(ref useElementPooling, "useElementPooling", true);
            Scribe_Values.Look(ref viewportCulling, "viewportCulling", true);
            Scribe_Values.Look(ref enableProfiler, "enableProfiler", false);
            Scribe_Values.Look(ref logDebugToFile, "logDebugToFile", false);
            Scribe_Values.Look(ref mpSyncColumnOrder, "mpSyncColumnOrder", true);
            Scribe_Values.Look(ref mpSyncWorkloads, "mpSyncWorkloads", true);
            Scribe_Values.Look(ref mpSyncRulesets, "mpSyncRulesets", true);
            Scribe_Values.Look(ref mpConflictMode, "mpConflictMode", MpConflictMode.PlayerPriority);
            Scribe_Values.Look(ref rulesetViewMode, "rulesetViewMode", RulesetViewMode.Regular);

            //Scribe_Values.Look(ref maxPriorityInt, "maxPriorityInt", 4);

            // Divider settings
            Scribe_Values.Look(ref dividerHeight, "dividerHeight", DefaultSettings.dividerHeight);

            // Load rulesets from save file
            Scribe_Collections.Look(ref SavedRulesets, "SavedRulesets", LookMode.Deep);
            Scribe_Collections.Look(ref SavedRuleBuilder2Rulesets, "SavedRuleBuilder2Rulesets", LookMode.Deep);

            // Reinitialize rulesets after load (restores defaults if missing)
            //InitializeRulesets();

            if (Scribe.mode != LoadSaveMode.Saving)
            {
                Scribe_Deep.Look(ref LegacyWorkGiverReassignments, "workGiverReassignments");
            }

            // Column order and widths persistence
            Scribe_Collections.Look(ref workColumnOrderDefNames, "workColumnOrderDefNames", LookMode.Value);
            Scribe_Collections.Look(ref storedColumnWidths, "storedColumnWidths", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref debugFeatureToggles, "debugFeatureToggles", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref viewedSettingIds, "viewedSettingIds", LookMode.Value);

            // Save/load the list of columns the player has directly dragged
            Scribe_Collections.Look(ref playerDraggedColumns, "playerDraggedColumns", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                WorkGiverReassignmentManager.OnSettingsLoaded();
            }

            if (storedColumnWidths == null)
            {
                storedColumnWidths = new Dictionary<string, float>();
            }

            if (playerDraggedColumns == null)
            {
                playerDraggedColumns = new List<string>();
            }

            if (viewedSettingIds == null)
            {
                viewedSettingIds = new List<string>();
            }

            EnsureRuleBuilder2Rulesets();
            NormalizePrioritySettings();
            NormalizeWorkTabHeightSettings();

            EnsureDebugFeatureTogglesInitialized();
        }

        /// <summary>
        /// Restores all settings to their default values.
        /// </summary>
        public void RestoreDefaults()
        {
            ApplyRegisteredDefaults();

            workTabMaxHeight = DefaultSettings.workTabMaxHeight;
            workTabMaxVisiblePawns = DefaultSettings.workTabMaxVisiblePawns;
            workTabTopSpace = DefaultSettings.workTabTopSpace;
            settingsViewMode = SettingsViewMode.Simple;
            enableSubWorkTransitionAnimation = DefaultSettings.enableSubWorkTransitionAnimation;
            subWorkTransitionStyle = DefaultSettings.subWorkTransitionStyle;
            subWorkTransitionSeconds = DefaultSettings.subWorkTransitionSeconds;
            preferredWorkTabOwner = DefaultSettings.preferredWorkTabOwner;
            workColumnOrderDefNames.Clear();
            storedColumnWidths.Clear();
            NormalizePrioritySettings();

            EnsureDebugFeatureTogglesInitialized();
            foreach (var feature in debugFeatureToggles.Keys.ToList())
            {
                debugFeatureToggles[feature] = false;
            }
        }

        private void NormalizeWorkTabHeightSettings()
        {
            if (workTabMaxVisiblePawns == 0 || workTabMaxVisiblePawns < -1)
            {
                workTabMaxVisiblePawns = DefaultSettings.workTabMaxVisiblePawns;
            }

            if (workTabMaxVisiblePawns < 0 && workTabMaxHeight > 0f)
            {
                workTabMaxVisiblePawns = Mathf.Clamp(
                    Mathf.RoundToInt(workTabMaxHeight / 30f),
                    1,
                    200);
            }

            workTabMaxHeight = DefaultSettings.workTabMaxHeight;
        }

        public static float ClampSubWorkTransitionSeconds(float value)
        {
            return Mathf.Clamp(value, 0.2f, 0.9f);
        }

        /// <summary>
        /// Applies default values declared in the settings registry to matching fields.
        /// </summary>
        private void ApplyRegisteredDefaults()
        {
            BWTSettingsRegistry.EnsureInitialized();

            foreach (var def in BWTSettingsRegistry.Definitions)
            {
                if (def.DefaultValue == null || string.IsNullOrEmpty(def.FieldName))
                {
                    continue;
                }

                var field = GetType().GetField(def.FieldName);
                if (field == null)
                {
                    continue;
                }

                try
                {
                    field.SetValue(this, def.DefaultValue);
                }
                catch
                {
                    // Ignore assignment issues so one bad field does not break resets.
                }
            }
        }

        /// <summary>
        /// Initializes/recovers rulesets. Called on construction and after loading saves.
        /// Automatically restores defaults if no rulesets exist (recovery from accidental deletion).
        /// </summary>
        public void InitializeRulesets()
        {
            if (SavedRulesets == null)
            {
                SavedRulesets = new List<WorkAssignmentRuleset>();
            }

            var defaultRules = CloneDefaultRulesets();

            if (!SavedRulesets.Any())
            {
                SavedRulesets.AddRange(defaultRules);
            }
            else
            {
                foreach (var template in defaultRules)
                {
                    bool alreadyExists = SavedRulesets.Any(rs =>
                        string.Equals(rs.Name, template.Name, StringComparison.OrdinalIgnoreCase));

                    if (!alreadyExists)
                    {
                        SavedRulesets.Add(template);
                    }
                }
            }

            SyncWorktypeReferences(SavedRulesets);

            if (CurrentRuleset != null && !SavedRulesets.Contains(CurrentRuleset))
            {
                SetCurrentRuleset(
                    SavedRulesets.FirstOrDefault(rs =>
                        string.Equals(rs.Name, CurrentRuleset.Name, StringComparison.OrdinalIgnoreCase)),
                    writeSettings: false);
            }

            if (CurrentRuleset == null && SavedRulesets.Any())
            {
                SetCurrentRuleset(SelectPreferredRuleset(), writeSettings: false);
            }

            EnsureRuleBuilder2Rulesets();
        }

        public void EnsureRuleBuilder2Rulesets()
        {
            RuleBuilder2RulesetStore.Ensure(this);
        }

        /// <summary>
        /// Records that the player directly dragged a column. Called when the drag operation completes.
        /// We only add to the list if not already present to avoid duplicates.
        /// </summary>
        public void RecordPlayerDraggedColumn(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return;

            if (!playerDraggedColumns.Contains(defName))
            {
                playerDraggedColumns.Add(defName);
            }
        }

        /// <summary>
        /// Clears all player-dragged column records. Called when resetting columns to vanilla order.
        /// </summary>
        public void ClearPlayerDraggedColumns()
        {
            playerDraggedColumns.Clear();
        }

        public bool HasViewedSetting(string settingId)
        {
            return !string.IsNullOrEmpty(settingId) &&
                viewedSettingIds != null &&
                viewedSettingIds.Contains(settingId);
        }

        public bool RecordViewedSetting(string settingId)
        {
            if (string.IsNullOrEmpty(settingId))
            {
                return false;
            }

            if (viewedSettingIds == null)
            {
                viewedSettingIds = new List<string>();
            }

            if (viewedSettingIds.Contains(settingId))
            {
                return false;
            }

            viewedSettingIds.Add(settingId);
            return true;
        }

        /// <summary>
        /// Checks if a column was directly dragged by the player (as opposed to just shifting
        /// as a side effect of another column being dragged).
        /// </summary>
        public bool WasColumnDraggedByPlayer(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return false;

            return playerDraggedColumns.Contains(defName);
        }

        public void EnsureDebugFeatureTogglesInitialized()
        {
            if (debugFeatureToggles == null)
            {
                debugFeatureToggles = new Dictionary<DebugFeature, bool>();
            }

            foreach (DebugFeature feature in System.Enum.GetValues(typeof(DebugFeature)))
            {
                if (!debugFeatureToggles.ContainsKey(feature))
                {
                    debugFeatureToggles[feature] = false;
                }
            }
        }
    }
}
