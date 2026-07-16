using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Tutorial;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Spine.UI.SettingsFramework;
using Spine.UI.Tutorial;

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
        public static bool keepVanillaWorkTabMinimumWidth = false;
        public static float workTabTopSpace = 40f; // Vanilla MainTabWindow_Work.ExtraTopSpace

        public static bool enableSkillOverlayFeature = true;
        public static bool enableAutoAssignFeature = true;
        public static bool enableDragDropReordering = true;
        public static bool enableDividers = true;
        public static bool enableWorkloads = true;
        public static bool enableSubWorkDrilldown = true;
        public static bool enableFluffyStyleFeatures = true;
        public static bool showFluffyStyleTopButtons = true;
        public static bool showStandaloneFluffyStyleTopButtons = false;
        public static bool enableFluffyScheduleAssigner = true;
        public static bool showSubWorkHeaderBadge = true;
        public static bool enableSubWorkCrossWorkDragDrop = true;
        public static bool enableCustomWorkLabels = true;
        public static BetterWorkTabSettings.SubWorkDrilldownModifier subWorkDrilldownModifier = BetterWorkTabSettings.SubWorkDrilldownModifier.Ctrl;
        public static BetterWorkTabSettings.SubWorkDrilldownButton subWorkDrilldownButton = BetterWorkTabSettings.SubWorkDrilldownButton.Left;
        public static BetterWorkTabSettings.SubWorkDrilldownStyle subWorkDrilldownStyle = BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen;
        public static bool subWorkCtrlClickNoticeDismissed = false;
        public static bool useVanillaSubWorkGlobalPriorityBoxes = false;
        public static bool useCompactSubWorkPriorityBoxes = true;
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
        public static WorkGridRendererMode workGridRendererMode = WorkGridRendererMode.Auto;
#if v1_2 || v1_1 || v1_0 || v0_19
        public static bool enableMultiplayerSync = false;
#else
        public static bool enableMultiplayerSync = true;
#endif
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
        public static bool showGhostDragIndicator = false;
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
        public static bool useRuleBuilder2 = true;
        public static bool showRuleBuilder2Tutorial = true;
        public static bool ruleBuilder2ShowWorkTabHighlights = true;
        public static bool ruleBuilder2EnableAnimations = true;
        public static bool ruleBuilder2UseDraftSuggestions = true;
        public static bool ruleBuilder2ShowAdvancedConditions = false;
        public static bool ruleBuilder2ShowMatchedPanel = true;
        public static bool showManualPrioritiesCheckbox = true;
        public static bool enableTimePrioritySchedules = true;
        public static bool showTimePriorityCopyPasteButtons = true;
        public static bool enableChronosPointerTimePriorityIntegration = true;
        public static bool showTimePriorityHourDivider = true;
        public static bool keepTimePrioritySourceColumnHighlighted = true;
        public static bool enableFluffyTimePriorityMirroring = true;
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

        public static Color Color_WorktypeIndicator = new Color(0.7f, 0.7f, 0.7f);
        public static Color Color_HeaderText = Color.white;
        public static Color Color_HeaderUnderline = Color.white;
        public static Color Color_DividerText = Color.white;
        public static Color Color_Borders = Color.gray;
        public static Color Color_SettingFocusHighlight = new Color(1f, 0.78f, 0.18f, 1f);

        public static Color Color_AngledHeaderText = Color.white;

        public static bool autoAssignRequireConfirmation = true; // Future: safety toggle

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
        public static bool showExternalWorkTabColumns = true;
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
        public static Color Color_FloatMenuHighlight = new Color(0.114f, 0.737f, 0.737f, 0.5f);
        public static Color Color_CustomMouseHighlight = new Color(0.737f, 0.737f, 0.114f, 0.25f);
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

            new WorkAssignmentRuleset("Vanilla New Pawn", new List<WorkAssignmentParameters>()
            {
                new WorkAssignmentParameters("Top 6", 3, isTopXSkill: 6),
                new WorkAssignmentParameters("Always Assigns", 3, isNaturalAlwaysAssign: true),
            }, resetBeforeApplying: false, isDefault: true),

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
        public static BetterWorkTabSettings.SettingsViewMode settingsViewMode = BetterWorkTabSettings.SettingsViewMode.Simple;
        public static bool useOutlineHighlights = false;
        public static int ruleBuilder2TutorialStep = 0;
        public static bool enableDebugLogging = false;
        public static bool debugPrintLayout = false;
        public static string defaultAutoAssignRuleset = "BWT Default";
        public static bool cacheBedCounts = true;
        public static bool cacheSkillLevels = true;
        public static bool cacheRowDescriptors = true;
        public static bool cacheIncapabilityChecks = true;
        public static bool useElementPooling = true;
        public static bool viewportCulling = true;
        public static bool enableProfiler = false;
        public static bool logDebugToFile = false;
        public static bool mpSyncColumnOrder = true;
        public static bool mpSyncWorkloads = true;
        public static bool mpSyncRulesets = true;
        public static BetterWorkTabSettings.MpConflictMode mpConflictMode = BetterWorkTabSettings.MpConflictMode.PlayerPriority;
        public static string bwtPlayerIdentifier = "";
        public static bool highlightDividersOnHover = true;
        public static BetterWorkTabSettings.RulesetViewMode rulesetViewMode = BetterWorkTabSettings.RulesetViewMode.Regular;
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

        public SettingsViewMode settingsViewMode = DefaultSettings.settingsViewMode;
        public bool useOutlineHighlights = DefaultSettings.useOutlineHighlights;
        public bool enableScrollWheelPriority = DefaultSettings.enableScrollWheelPriority;
        public WorkTabOwnerPreference preferredWorkTabOwner = DefaultSettings.preferredWorkTabOwner;
        public bool showExternalWorkTabColumns = DefaultSettings.showExternalWorkTabColumns;

        public bool firstTimeSetupDone = DefaultSettings.firstTimeSetupDone;

        // Master feature toggles
        public bool enableSkillOverlayFeature = DefaultSettings.enableSkillOverlayFeature;
        public bool enableAutoAssignFeature = DefaultSettings.enableAutoAssignFeature;
        public bool enableDragDropReordering = DefaultSettings.enableDragDropReordering;
        public bool enableDividers = DefaultSettings.enableDividers;
        public bool enableWorkloads = DefaultSettings.enableWorkloads;
        public bool enableSubWorkDrilldown = DefaultSettings.enableSubWorkDrilldown;
        public bool enableFluffyStyleFeatures = DefaultSettings.enableFluffyStyleFeatures;
        public bool showFluffyStyleTopButtons = DefaultSettings.showFluffyStyleTopButtons;
        public bool showStandaloneFluffyStyleTopButtons = DefaultSettings.showStandaloneFluffyStyleTopButtons;
        public bool enableFluffyScheduleAssigner = DefaultSettings.enableFluffyScheduleAssigner;
        public bool showSubWorkHeaderBadge = DefaultSettings.showSubWorkHeaderBadge;
        public bool enableSubWorkCrossWorkDragDrop = DefaultSettings.enableSubWorkCrossWorkDragDrop;
        public bool enableCustomWorkLabels = DefaultSettings.enableCustomWorkLabels;
        public SubWorkDrilldownModifier subWorkDrilldownModifier = DefaultSettings.subWorkDrilldownModifier;
        public SubWorkDrilldownButton subWorkDrilldownButton = DefaultSettings.subWorkDrilldownButton;
        public SubWorkDrilldownStyle subWorkDrilldownStyle = DefaultSettings.subWorkDrilldownStyle;
        public bool subWorkCtrlClickNoticeDismissed = DefaultSettings.subWorkCtrlClickNoticeDismissed;
        public bool useVanillaSubWorkGlobalPriorityBoxes = DefaultSettings.useVanillaSubWorkGlobalPriorityBoxes;
        public bool useCompactSubWorkPriorityBoxes = DefaultSettings.useCompactSubWorkPriorityBoxes;
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
        public WorkGridRendererMode workGridRendererMode = DefaultSettings.workGridRendererMode;
        public bool enableMultiplayerSync = DefaultSettings.enableMultiplayerSync;
        public bool hideSettingResetIcons = DefaultSettings.hideSettingResetIcons;
        public float dividerHeight = DefaultSettings.dividerHeight;
        public bool showOnlyLineDragIndicatorRows = DefaultSettings.showOnlyLineDragIndicatorRows;
        public bool showOnlyLineDragIndicatorColumns = DefaultSettings.showOnlyLineDragIndicatorColumns;
        public bool showGhostDragIndicator = DefaultSettings.showGhostDragIndicator;
        public bool showInsertionLineIndicator = DefaultSettings.showInsertionLineIndicator;
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
        public int tutorialFlowVersion;
        public bool tutorialWelcomeCompleted;
        public string activeTutorialLessonId = string.Empty;
        public int tutorialLessonPhase;
        public List<string> completedTutorialLessonIds = new List<string>();
        public bool useRuleBuilder2 = DefaultSettings.useRuleBuilder2;
        public bool showRuleBuilder2Tutorial = DefaultSettings.showRuleBuilder2Tutorial;
        public int ruleBuilder2TutorialStep = DefaultSettings.ruleBuilder2TutorialStep;
        public bool ruleBuilder2ShowWorkTabHighlights = DefaultSettings.ruleBuilder2ShowWorkTabHighlights;
        public bool ruleBuilder2EnableAnimations = DefaultSettings.ruleBuilder2EnableAnimations;
        public bool ruleBuilder2UseDraftSuggestions = DefaultSettings.ruleBuilder2UseDraftSuggestions;
        public bool ruleBuilder2ShowAdvancedConditions = DefaultSettings.ruleBuilder2ShowAdvancedConditions;
        public bool ruleBuilder2ShowMatchedPanel = DefaultSettings.ruleBuilder2ShowMatchedPanel;
        public bool showManualPrioritiesCheckbox = DefaultSettings.showManualPrioritiesCheckbox;
        public bool enableTimePrioritySchedules = DefaultSettings.enableTimePrioritySchedules;
        public bool showTimePriorityCopyPasteButtons = DefaultSettings.showTimePriorityCopyPasteButtons;
        public bool enableChronosPointerTimePriorityIntegration = DefaultSettings.enableChronosPointerTimePriorityIntegration;
        public bool showTimePriorityHourDivider = DefaultSettings.showTimePriorityHourDivider;
        public bool keepTimePrioritySourceColumnHighlighted = DefaultSettings.keepTimePrioritySourceColumnHighlighted;
        public bool enableFluffyTimePriorityMirroring = DefaultSettings.enableFluffyTimePriorityMirroring;
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
        public bool enableDebugLogging = DefaultSettings.enableDebugLogging;
        public Dictionary<DebugFeature, bool> debugFeatureToggles = Enum.GetValues(typeof(DebugFeature))
            .Cast<DebugFeature>()
            .ToDictionary(feature => feature, _ => false);
        public List<string> workColumnOrderDefNames = new List<string>();
        public Dictionary<string, float> storedColumnWidths = new Dictionary<string, float>();
        public WorkGiverReassignmentData LegacyWorkGiverReassignments;
        public List<string> viewedSettingIds = new List<string>();
        
        public bool debugPrintLayout = DefaultSettings.debugPrintLayout; // Added to fix CS1061

        // Tracks which columns the player has directly dragged. Only columns in this list
        // that are also currently out of their vanilla position will show the yellow asterisk.
        // This distinguishes player-dragged columns from columns that merely shifted as a side effect.
        public List<string> playerDraggedColumns = new List<string>();

        // Skill level colors
        public Color Color_VeryLowSkill = DefaultSettings.Color_VeryLowSkill;
        public Color Color_LowSkill = DefaultSettings.Color_LowSkill;
        public Color Color_GoodLowSkill = DefaultSettings.Color_GoodLowSkill;
        public Color Color_ExcellentSkill = DefaultSettings.Color_ExcellentSkill;

        // Highlight visibility settings
        public bool ShowPawnAndWorktypeHighlights = DefaultSettings.ShowPawnAndWorktypeHighlights;
        public bool ShowCursorPawnAndWorktypeHighlight = DefaultSettings.ShowCursorPawnAndWorktypeHighlight;
        public bool ShowFloatMenuPawnAndWorktypeHighlight = DefaultSettings.ShowFloatMenuPawnAndWorktypeHighlight;
        public bool DoSelectedPawnHighlight = DefaultSettings.DoSelectedPawnHighlight;

        // Custom highlight colors
        public bool UseCustomMouseHoverHighlight = DefaultSettings.UseCustomMouseHoverHighlight;
        public Color Color_CursorHighlight = DefaultSettings.Color_CursorHighlight;
        public Color Color_FloatMenuHighlight = DefaultSettings.Color_FloatMenuHighlight;
        public Color Color_CustomMouseHighlight = DefaultSettings.Color_CustomMouseHighlight;
        public Color Color_CustomSimilarWorktypeHighlight = DefaultSettings.Color_CustomSimilarWorktypeHighlight;
        public Color Color_RowHoverHighlight = DefaultSettings.Color_RowHoverHighlight;
        public Color Color_ColumnHoverHighlight = DefaultSettings.Color_ColumnHoverHighlight;
        public bool useRowHoverOverride = DefaultSettings.useRowHoverOverride;
        public bool useColumnHoverOverride = DefaultSettings.useColumnHoverOverride;
        public Color Color_SelectedPawnHighlight = DefaultSettings.Color_SelectedPawnHighlight;
        public Color Color_HeaderText = DefaultSettings.Color_HeaderText;
        public Color Color_DividerText = DefaultSettings.Color_DividerText;
        public Color Color_Borders = DefaultSettings.Color_Borders;
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
        public Color Color_IncapableBecauseOfCapacities = DefaultSettings.Color_IncapableBecauseOfCapacities;
        public Color Color_BestPawnForSkillSquare = DefaultSettings.Color_BestPawnForSkillSquare;

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
        public string defaultAutoAssignRuleset = DefaultSettings.defaultAutoAssignRuleset;

        // Workloads
        public bool showWorkloadButtonFooter = DefaultSettings.showWorkloadButtonFooter;
        public bool enableWorkloadSaving = DefaultSettings.enableWorkloadSaving;
        public bool enableWorkloadLoading = DefaultSettings.enableWorkloadLoading;
        public bool persistDividersInWorkloads = DefaultSettings.persistDividersInWorkloads;
        public bool alwaysShowConditionEditors = DefaultSettings.alwaysShowConditionEditors;

        // Performance
        public bool cacheBedCounts = DefaultSettings.cacheBedCounts;
        public bool cacheSkillLevels = DefaultSettings.cacheSkillLevels;
        public bool cacheRowDescriptors = DefaultSettings.cacheRowDescriptors;
        public bool cacheIncapabilityChecks = DefaultSettings.cacheIncapabilityChecks;
        public bool useElementPooling = DefaultSettings.useElementPooling;
        public bool viewportCulling = DefaultSettings.viewportCulling;

        // Debug and profiling
        public bool enableProfiler = DefaultSettings.enableProfiler;
        public bool logDebugToFile = DefaultSettings.logDebugToFile;

        // Multiplayer
        public bool mpSyncColumnOrder = DefaultSettings.mpSyncColumnOrder;
        public bool mpSyncWorkloads = DefaultSettings.mpSyncWorkloads;
        public bool mpSyncRulesets = DefaultSettings.mpSyncRulesets;
        public enum MpConflictMode { PlayerPriority, HostPriority, AskPlayer }
        public MpConflictMode mpConflictMode = DefaultSettings.mpConflictMode;

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
        public int priorityColorPercentage_Green = DefaultSettings.priorityColorPercentage_Green;
        public int priorityColorPercentage_Yellow = DefaultSettings.priorityColorPercentage_Yellow;
        public int priorityColorPercentage_Tan = DefaultSettings.priorityColorPercentage_Tan;

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
        public string bwtPlayerIdentifier = DefaultSettings.bwtPlayerIdentifier;

        // Dividers
        public bool highlightDividersOnHover = DefaultSettings.highlightDividersOnHover;

        // UI mode settings
        public enum ShowUIMode { Always, Never, Shifted, Unshifted }
        public enum SubWorkDrilldownModifier { Ctrl, Shift }
        public enum SubWorkDrilldownButton { Left, Right }
        public enum SubWorkDrilldownStyle
        {
            NotChosen,
            FocusView,
            ExpandBeside
        }
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
        public ShowUIMode ShowUIMode_ShowSmallSkillNumbers = DefaultSettings.ShowUIMode_ShowSmallSkillNumbers;
        public ShowUIMode ShowUIMode_ShowPawnForSkillSquare = DefaultSettings.ShowUIMode_ShowPawnForSkillSquare;
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
        public bool keepVanillaWorkTabMinimumWidth = DefaultSettings.keepVanillaWorkTabMinimumWidth;
        public float workTabTopSpace = DefaultSettings.workTabTopSpace;

        public enum RulesetViewMode
        {
            Raw,
            Regular,
            Both
        }
        public RulesetViewMode rulesetViewMode = DefaultSettings.rulesetViewMode;

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
            selectedPriorityProviderId = string.IsNullOrEmpty(providerId)
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

            if (string.IsNullOrEmpty(selectedPriorityProviderId))
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

            if (string.IsNullOrEmpty(selectedPriorityProviderId) ||
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
                    selectedPriorityProviderId = string.IsNullOrEmpty(selectedPriorityProviderId)
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
            // Add new settings in BWTSettingsRegistry's HOW TO ADD A SETTING block.
            BWTSettingsRegistry.EnsureInitialized();
            SettingsScribe.ScribeAll(this, BWTSettingsRegistry.Definitions);

            // REGISTERED PREFERENCES WITH MIGRATION BEHAVIOR
            subWorkTransitionSeconds = ClampSubWorkTransitionSeconds(subWorkTransitionSeconds);
            ScribeCompat.LookCollection(ref hiddenWorktypes, "hiddenWorktypes", LookMode.Value);
            if (hiddenWorktypes == null)
            {
                hiddenWorktypes = new List<string>();
            }

            // Legacy inputs to priorityMode inference; re-synced from priorityMode after load.
            ScribeCompat.LookValue(ref enableExtendedPriorities, "enableExtendedPriorities", DefaultSettings.enableExtendedPriorities);
            ScribeCompat.LookValue(ref delegateToExternalPriorityMods, "delegateToExternalPriorityMods", DefaultSettings.delegateToExternalPriorityMods);
            ScribeCompat.LookValue(ref selectedPriorityProviderId, "selectedPriorityProviderId", DefaultSettings.selectedPriorityProviderId);

            // priorityMode is registered for UI/reset, but uses a dynamic 1.0.5 migration default.
            PriorityMode inferredPriorityMode = InferPriorityModeFromProviderSelectionFields();
            ScribeCompat.LookValue(ref priorityMode, "priorityMode", inferredPriorityMode);
            NormalizePrioritySettings();

            // STATE AND COMPLEX DATA: intentionally not reset by registry defaults.
            ScribeCompat.LookValue(ref firstTimeSetupDone, "firstTimeSetupDone", DefaultSettings.firstTimeSetupDone);
            ScribeCompat.LookValue(ref subWorkCtrlClickNoticeDismissed, "subWorkCtrlClickNoticeDismissed", DefaultSettings.subWorkCtrlClickNoticeDismissed);
            ScribeCompat.LookValue(ref tutorialFlowVersion, "tutorialFlowVersion", 0);
            ScribeCompat.LookValue(ref tutorialWelcomeCompleted, "tutorialWelcomeCompleted", false);
            ScribeCompat.LookValue(ref activeTutorialLessonId, "activeTutorialLessonId", string.Empty);
            ScribeCompat.LookValue(ref tutorialLessonPhase, "tutorialLessonPhase", 0);
            ScribeCompat.LookCollection(ref completedTutorialLessonIds, "completedTutorialLessonIds", LookMode.Value);
            if (completedTutorialLessonIds == null)
            {
                completedTutorialLessonIds = new List<string>();
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                int legacyGeneralStep = 0;
                bool legacyShowBetaTutorial = false;
                int legacyBetaStep = 0;
                ScribeCompat.LookValue(ref legacyGeneralStep, "generalTutorialStep", 0);
                ScribeCompat.LookValue(ref legacyShowBetaTutorial, "showBetaTutorial", false);
                ScribeCompat.LookValue(ref legacyBetaStep, "betaTutorialStep", 0);
                MigrateLegacyTutorialState(legacyGeneralStep, legacyShowBetaTutorial, legacyBetaStep);
                if (tutorialFlowVersion < BWTGeneralTutorial.CurrentFlowVersion)
                {
                    // Present the corrected welcome once for saves created by an
                    // earlier tutorial flow. Preserve completed lessons, but return
                    // transient lesson ownership to the map after the welcome.
                    tutorialFlowVersion = BWTGeneralTutorial.CurrentFlowVersion;
                    tutorialWelcomeCompleted = false;
                    activeTutorialLessonId = string.Empty;
                    tutorialLessonPhase = 0;
                }
            }
            ScribeCompat.LookValue(ref ruleBuilder2TutorialStep, "ruleBuilder2TutorialStep", DefaultSettings.ruleBuilder2TutorialStep);

            if (Scribe.mode == LoadSaveMode.Saving && CurrentRuleset != null)
            {
                currentRulesetName = CurrentRuleset.Name;
            }

            if (Scribe.mode == LoadSaveMode.Saving && CurrentRuleBuilder2Ruleset != null)
            {
                currentRuleBuilder2RulesetStableId = CurrentRuleBuilder2Ruleset.StableId;
            }

            ScribeCompat.LookValue(ref defaultAutoAssignRuleset, "defaultAutoAssignRuleset", DefaultSettings.defaultAutoAssignRuleset);
            ScribeCompat.LookValue(ref currentRulesetName, "currentRulesetName", "");
            ScribeCompat.LookValue(ref currentRuleBuilder2RulesetStableId, "currentRuleBuilder2RulesetStableId", "");
            ScribeCompat.LookCollection(ref SavedRulesets, "SavedRulesets", LookMode.Deep);
            ScribeCompat.LookCollection(ref SavedRuleBuilder2Rulesets, "SavedRuleBuilder2Rulesets", LookMode.Deep);

            if (Scribe.mode != LoadSaveMode.Saving)
            {
                ScribeCompat.LookDeep(ref LegacyWorkGiverReassignments, "workGiverReassignments");
            }

            ScribeCompat.LookCollection(ref workColumnOrderDefNames, "workColumnOrderDefNames", LookMode.Value);
            ScribeCompat.LookStringDictionary(ref storedColumnWidths, "storedColumnWidths", LookMode.Value);
            ScribeCompat.LookCollection(ref debugFeatureToggles, "debugFeatureToggles", LookMode.Value, LookMode.Value);
            ScribeCompat.LookCollection(ref viewedSettingIds, "viewedSettingIds", LookMode.Value);
            ScribeCompat.LookCollection(ref playerDraggedColumns, "playerDraggedColumns", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                WorkGiverReassignmentManager.OnSettingsLoaded();
            }

            EnsureLayoutPersistenceStateInitialized();

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
            ICollection<string> changedPreferenceFields = ApplyRegisteredDefaults();

            workTabMaxHeight = DefaultSettings.workTabMaxHeight;
            workTabMaxVisiblePawns = DefaultSettings.workTabMaxVisiblePawns;
            workTabTopSpace = DefaultSettings.workTabTopSpace;
            settingsViewMode = SettingsViewMode.Simple;
            enableSubWorkTransitionAnimation = DefaultSettings.enableSubWorkTransitionAnimation;
            subWorkDrilldownStyle = DefaultSettings.subWorkDrilldownStyle;
            subWorkCtrlClickNoticeDismissed = DefaultSettings.subWorkCtrlClickNoticeDismissed;
            subWorkTransitionStyle = DefaultSettings.subWorkTransitionStyle;
            subWorkTransitionSeconds = DefaultSettings.subWorkTransitionSeconds;
            preferredWorkTabOwner = DefaultSettings.preferredWorkTabOwner;
            showExternalWorkTabColumns = DefaultSettings.showExternalWorkTabColumns;

            // STATE RESET: preserve RestoreDefaults' historical behavior for layout caches.
            EnsureLayoutPersistenceStateInitialized();
            playerDraggedColumns ??= new List<string>();
            workColumnOrderDefNames.Clear();
            storedColumnWidths.Clear();
            playerDraggedColumns.Clear();

            // COLLECTION PREFERENCE RESET: hidden Work types are stored separately because
            // registry auto-scribing intentionally handles scalar preferences only.
            hiddenWorktypes ??= new List<string>();
            hiddenWorktypes.Clear();
            NormalizePrioritySettings();

            // STATE RESET: debug feature toggles are runtime state, not user preferences.
            EnsureDebugFeatureTogglesInitialized();
            foreach (var feature in debugFeatureToggles.Keys.ToList())
            {
                debugFeatureToggles[feature] = false;
            }

            SettingsScribe.NotifyPreferenceChanges(
                this,
                BWTSettingsRegistry.Definitions,
                changedPreferenceFields);
            BetterWorkTabSettingsUI.NotifySettingsChanged();
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
        private ICollection<string> ApplyRegisteredDefaults()
        {
            BWTSettingsRegistry.EnsureInitialized();
            return SettingsScribe.ApplyPreferenceDefaults(this, BWTSettingsRegistry.Definitions);
        }

        private void MigrateLegacyTutorialState(
            int legacyGeneralStep,
            bool legacyShowBetaTutorial,
            int legacyBetaStep)
        {
            if (!string.IsNullOrEmpty(activeTutorialLessonId) || completedTutorialLessonIds.Count > 0)
            {
                return;
            }

            if (legacyShowBetaTutorial)
            {
                showGeneralTutorial = true;
                if (legacyBetaStep >= 85)
                {
                    TutorialProgressTransitions.Complete(
                        completedTutorialLessonIds,
                        BWTGeneralTutorial.HeaderSubWorkLesson);
                }
                if (legacyBetaStep >= 115)
                {
                    TutorialProgressTransitions.Complete(
                        completedTutorialLessonIds,
                        BWTGeneralTutorial.PriorityScheduleLesson);
                }

                activeTutorialLessonId = legacyBetaStep < 90
                    ? BWTGeneralTutorial.HeaderSubWorkLesson
                    : legacyBetaStep < 130
                        ? BWTGeneralTutorial.PriorityScheduleLesson
                        : string.Empty;
                tutorialLessonPhase = 0;
                return;
            }

            if (!showGeneralTutorial || legacyGeneralStep <= 5)
            {
                return;
            }

            if (legacyGeneralStep <= 20)
                activeTutorialLessonId = BWTGeneralTutorial.PrioritySkillLesson;
            else if (legacyGeneralStep <= 30)
                activeTutorialLessonId = BWTGeneralTutorial.PawnMenuLesson;
            else if (legacyGeneralStep <= 40)
                activeTutorialLessonId = BWTGeneralTutorial.PawnDividerLesson;
            else if (legacyGeneralStep <= 50)
                activeTutorialLessonId = BWTGeneralTutorial.PawnAppearanceLesson;
            else if (legacyGeneralStep <= 80)
                activeTutorialLessonId = BWTGeneralTutorial.HeaderReorderLesson;
            else if (legacyGeneralStep <= 90)
                activeTutorialLessonId = BWTGeneralTutorial.HeaderGroupLesson;
            else if (legacyGeneralStep <= 115)
                activeTutorialLessonId = BWTGeneralTutorial.HeaderSubWorkLesson;
            else if (legacyGeneralStep <= 125)
                activeTutorialLessonId = BWTGeneralTutorial.PriorityScheduleLesson;
            else
                activeTutorialLessonId = string.Empty;

            tutorialLessonPhase = 0;
        }

        private void EnsureLayoutPersistenceStateInitialized()
        {
            workColumnOrderDefNames ??= new List<string>();
            storedColumnWidths ??= new Dictionary<string, float>();
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
