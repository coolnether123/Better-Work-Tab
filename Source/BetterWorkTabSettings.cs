using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Settings;
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

        public static float workTabMaxHeight = -1f; // -1 = use vanilla default (fill screen)

        public static bool enableSkillOverlayFeature = true;
        public static bool enableAutoAssignFeature = true;
        public static bool enableDragDropReordering = true;
        public static bool enableDividers = true;
        public static bool enableWorkloads = true;
        public static bool enableSubWorkDrilldown = false;
        public static BetterWorkTabSettings.SubWorkDrilldownModifier subWorkDrilldownModifier = BetterWorkTabSettings.SubWorkDrilldownModifier.Ctrl;
        public static BetterWorkTabSettings.SubWorkDrilldownButton subWorkDrilldownButton = BetterWorkTabSettings.SubWorkDrilldownButton.Left;
        public static bool useVanillaSubWorkGlobalPriorityBoxes = false;
        public static bool restoreCursorOnSubWorkExit = true;
        public static bool restoreCursorOnSubWorkPawnCellExit = false;
        public static bool subWorkAutoExpandColumns = true;
        public static bool subWorkEvenlyExpandColumns = true;
        public static bool enableColumnOrderSaving = true;
        public static bool enableUIElements = true;
        public static bool enablePerformanceOptimizations = true;
#if v1_2 || v1_1 || v1_0 || v0_19
        public static bool enableMultiplayerSync = false;
#else
        public static bool enableMultiplayerSync = true;
#endif
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
        public static bool showManualPrioritiesCheckbox = true;
#if v0_16
#if vAlpha4
        public static bool useModernLegacyPriorityCells = false;
#else
        public static bool useModernLegacyPriorityCells = true;
#endif
#endif
        public static bool showDividers = true;
        public static bool allowCustomDividerColors = true;
        public static bool showDividerLabels = true;
        public static bool allowDividerCollapse = true;
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
        public static Color Color_DividerText = Color.white;
        public static Color Color_Borders = Color.gray;

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
        public static bool enableScrollWheelPriority = false;
        public static bool enableAngledHeaders = true;
        public static int angledHeaderRotation = -60;
        public static int angledHeaderHorizontalOffset = 10;
        public static bool useVerticalStackingForCJK = true;
        public static float cjkVerticalKerning = 0.75f;
        public static bool autoEnableManualPriorities = false;
        public static PriorityMode priorityMode = PriorityMode.Auto;
        public static bool enableExtendedPriorities = false;
        public static bool delegateToExternalPriorityMods = true;
        public static string selectedPriorityProviderId = PriorityConstants.AutoProviderId;
        public static int autoMaxPriority = PriorityConstants.ExtendedHardMax;
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

        public static int maxPriority = PriorityConstants.ExtendedHardMax;
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

        public bool firstTimeSetupDone = DefaultSettings.firstTimeSetupDone;

        // Master feature toggles
        public bool enableSkillOverlayFeature = true;
        public bool enableAutoAssignFeature = true;
        public bool enableDragDropReordering = DefaultSettings.enableDragDropReordering;
        public bool enableDividers = DefaultSettings.enableDividers;
        public bool enableWorkloads = DefaultSettings.enableWorkloads;
        public bool enableSubWorkDrilldown = DefaultSettings.enableSubWorkDrilldown;
        public SubWorkDrilldownModifier subWorkDrilldownModifier = DefaultSettings.subWorkDrilldownModifier;
        public SubWorkDrilldownButton subWorkDrilldownButton = DefaultSettings.subWorkDrilldownButton;
        public bool useVanillaSubWorkGlobalPriorityBoxes = DefaultSettings.useVanillaSubWorkGlobalPriorityBoxes;
        public bool restoreCursorOnSubWorkExit = DefaultSettings.restoreCursorOnSubWorkExit;
        public bool restoreCursorOnSubWorkPawnCellExit = DefaultSettings.restoreCursorOnSubWorkPawnCellExit;
        public bool subWorkAutoExpandColumns = DefaultSettings.subWorkAutoExpandColumns;
        public bool subWorkEvenlyExpandColumns = DefaultSettings.subWorkEvenlyExpandColumns;
        public bool enableColumnOrderSaving = DefaultSettings.enableColumnOrderSaving;
        public bool enableUIElements = DefaultSettings.enableUIElements;
        public bool enablePerformanceOptimizations = DefaultSettings.enablePerformanceOptimizations;
        public bool enableMultiplayerSync = DefaultSettings.enableMultiplayerSync;
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
        public bool showManualPrioritiesCheckbox = DefaultSettings.showManualPrioritiesCheckbox;
#if v0_16
        public bool useModernLegacyPriorityCells = DefaultSettings.useModernLegacyPriorityCells;
#endif
        public bool showDividers = DefaultSettings.showDividers;
        public bool allowCustomDividerColors = DefaultSettings.allowCustomDividerColors;
        public bool showDividerLabels = DefaultSettings.showDividerLabels;
        public bool allowDividerCollapse = DefaultSettings.allowDividerCollapse;
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
            { DebugFeature.ModSupport, false }
        };
        public List<string> workColumnOrderDefNames = new List<string>();
        public Dictionary<string, float> storedColumnWidths = new Dictionary<string, float>();
        public WorkGiverReassignmentData LegacyWorkGiverReassignments;
        
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
        public WorkAssignmentRuleset CurrentRuleset = null;
        public string currentRulesetName = "";

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
        public bool autoEnableManualPriorities = DefaultSettings.autoEnableManualPriorities;


        public float workTabMaxHeight = DefaultSettings.workTabMaxHeight;

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
                .Select(rule => new WorkAssignmentRule(
                    rule.Name,
                    rule.Parameters?.Copy() ?? new WorkAssignmentParameters(),
                    rule.CachedWorktype))
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
            Better_Work_Tab.ScribeCompat.LookValue(ref workTabMaxHeight, "workTabMaxHeight", DefaultSettings.workTabMaxHeight);


            // Core feature toggles
            Better_Work_Tab.ScribeCompat.LookValue(ref firstTimeSetupDone, "firstTimeSetupDone", DefaultSettings.firstTimeSetupDone);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableSkillOverlayFeature, "enableSkillOverlayFeature", DefaultSettings.enableSkillOverlayFeature);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableAutoAssignFeature, "enableAutoAssignFeature", DefaultSettings.enableAutoAssignFeature);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableDragDropReordering, "enableDragDropReordering", DefaultSettings.enableDragDropReordering);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableDividers, "enableDividers", DefaultSettings.enableDividers);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableWorkloads, "enableWorkloads", DefaultSettings.enableWorkloads);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableSubWorkDrilldown, "enableSubWorkDrilldown", DefaultSettings.enableSubWorkDrilldown);
            Better_Work_Tab.ScribeCompat.LookValue(ref subWorkDrilldownModifier, "subWorkDrilldownModifier", DefaultSettings.subWorkDrilldownModifier);
            Better_Work_Tab.ScribeCompat.LookValue(ref subWorkDrilldownButton, "subWorkDrilldownButton", DefaultSettings.subWorkDrilldownButton);
            Better_Work_Tab.ScribeCompat.LookValue(ref useVanillaSubWorkGlobalPriorityBoxes, "useVanillaSubWorkGlobalPriorityBoxes", DefaultSettings.useVanillaSubWorkGlobalPriorityBoxes);
            Better_Work_Tab.ScribeCompat.LookValue(ref restoreCursorOnSubWorkExit, "restoreCursorOnSubWorkExit", DefaultSettings.restoreCursorOnSubWorkExit);
            Better_Work_Tab.ScribeCompat.LookValue(ref restoreCursorOnSubWorkPawnCellExit, "restoreCursorOnSubWorkPawnCellExit", DefaultSettings.restoreCursorOnSubWorkPawnCellExit);
            Better_Work_Tab.ScribeCompat.LookValue(ref subWorkAutoExpandColumns, "subWorkAutoExpandColumns", DefaultSettings.subWorkAutoExpandColumns);
            Better_Work_Tab.ScribeCompat.LookValue(ref subWorkEvenlyExpandColumns, "subWorkEvenlyExpandColumns", DefaultSettings.subWorkEvenlyExpandColumns);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableColumnOrderSaving, "enableColumnOrderSaving", DefaultSettings.enableColumnOrderSaving);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableUIElements, "enableUIElements", DefaultSettings.enableUIElements);
            Better_Work_Tab.ScribeCompat.LookValue(ref enablePerformanceOptimizations, "enablePerformanceOptimizations", DefaultSettings.enablePerformanceOptimizations);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableMultiplayerSync, "enableMultiplayerSync", DefaultSettings.enableMultiplayerSync);
            Better_Work_Tab.ScribeCompat.LookValue(ref settingsViewMode, "settingsViewMode", SettingsViewMode.Simple);

            // Highlight settings
            Better_Work_Tab.ScribeCompat.LookValue(ref ShowPawnAndWorktypeHighlights, "ShowPawnAndWorktypeHighlights", DefaultSettings.ShowPawnAndWorktypeHighlights);
            Better_Work_Tab.ScribeCompat.LookValue(ref ShowCursorPawnAndWorktypeHighlight, "ShowCursorPawnAndWorktypeHighlight", DefaultSettings.ShowCursorPawnAndWorktypeHighlight);
            Better_Work_Tab.ScribeCompat.LookValue(ref ShowFloatMenuPawnAndWorktypeHighlight, "ShowFloatMenuPawnAndWorktypeHighlight", DefaultSettings.ShowFloatMenuPawnAndWorktypeHighlight);
            Better_Work_Tab.ScribeCompat.LookValue(ref DoSelectedPawnHighlight, "DoSelectedPawnHighlight", DefaultSettings.DoSelectedPawnHighlight);
            Better_Work_Tab.ScribeCompat.LookValue(ref UseCustomMouseHoverHighlight, "UseCustomMouseHoverHighlight", DefaultSettings.UseCustomMouseHoverHighlight);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableRowColumnHighlights, "enableRowColumnHighlights", DefaultSettings.enableRowColumnHighlights);
            Better_Work_Tab.ScribeCompat.LookValue(ref ShowSimilarWorktypeHighlight, "ShowSimilarWorktypeHighlight", DefaultSettings.ShowSimilarWorktypeHighlight);
            Better_Work_Tab.ScribeCompat.LookValue(ref useOutlineHighlights, "useOutlineHighlights", false);
            Better_Work_Tab.ScribeCompat.LookValue(ref useRowHoverOverride, "useRowHoverOverride", DefaultSettings.useRowHoverOverride);
            Better_Work_Tab.ScribeCompat.LookValue(ref useColumnHoverOverride, "useColumnHoverOverride", DefaultSettings.useColumnHoverOverride);
            Better_Work_Tab.ScribeCompat.LookValue(ref SelectedPawnHighlightOpacity, "SelectedPawnHighlightOpacity", DefaultSettings.SelectedPawnHighlightOpacity);
            Better_Work_Tab.ScribeCompat.LookValue(ref SimilarWorktypeHighlightOpacity, "SimilarWorktypeHighlightOpacity", DefaultSettings.SimilarWorktypeHighlightOpacity);

            // UI display settings
            Better_Work_Tab.ScribeCompat.LookValue(ref showPawnCountAtBottom, "showPawnCountAtBottom", DefaultSettings.showPawnCountAtBottom);
            Better_Work_Tab.ScribeCompat.LookValue(ref showBedCountAtBottom, "showBedCountAtBottom", DefaultSettings.showBedCountAtBottom);
            Better_Work_Tab.ScribeCompat.LookValue(ref disableLeftClickClose, "disableLeftClickClose", DefaultSettings.disableLeftClickClose);
            Better_Work_Tab.ScribeCompat.LookValue(ref closeOnMapClick, "closeOnMapClick", DefaultSettings.closeOnMapClick);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableContextMenuOnRightClick, "enableContextMenuOnRightClick", DefaultSettings.enableContextMenuOnRightClick);
            Better_Work_Tab.ScribeCompat.LookValue(ref requireCtrlForDrag, "requireCtrlForDrag", DefaultSettings.requireCtrlForDrag);
            Better_Work_Tab.ScribeCompat.LookValue(ref showPriorityLegend, "showPriorityLegend", DefaultSettings.showPriorityLegend);
            Better_Work_Tab.ScribeCompat.LookValue(ref showDragInstructions, "showDragInstructions", DefaultSettings.showDragInstructions);
            Better_Work_Tab.ScribeCompat.LookValue(ref showManualPrioritiesCheckbox, "showManualPrioritiesCheckbox", DefaultSettings.showManualPrioritiesCheckbox);
#if v0_16
            Better_Work_Tab.ScribeCompat.LookValue(ref useModernLegacyPriorityCells, "useModernLegacyPriorityCells", DefaultSettings.useModernLegacyPriorityCells);
#endif
            Better_Work_Tab.ScribeCompat.LookValue(ref showDividers, "showDividers", DefaultSettings.showDividers);
            Better_Work_Tab.ScribeCompat.LookValue(ref allowCustomDividerColors, "allowCustomDividerColors", DefaultSettings.allowCustomDividerColors);
            Better_Work_Tab.ScribeCompat.LookValue(ref showDividerLabels, "showDividerLabels", DefaultSettings.showDividerLabels);
            Better_Work_Tab.ScribeCompat.LookValue(ref allowDividerCollapse, "allowDividerCollapse", DefaultSettings.allowDividerCollapse);
            Better_Work_Tab.ScribeCompat.LookValue(ref showOnlyLineDragIndicatorRows, "showOnlyLineDragIndicatorRows", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref showOnlyLineDragIndicatorColumns, "showOnlyLineDragIndicatorColumns", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref showGhostDragIndicator, "showGhostDragIndicator", false);
            Better_Work_Tab.ScribeCompat.LookValue(ref showInsertionLineIndicator, "showInsertionLineIndicator", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref rowDraggingEnabled, "rowDraggingEnabled", DefaultSettings.rowDraggingEnabled);
            Better_Work_Tab.ScribeCompat.LookValue(ref columnDraggingEnabled, "columnDraggingEnabled", DefaultSettings.columnDraggingEnabled);
            Better_Work_Tab.ScribeCompat.LookValue(ref columnInsertionLineInset, "columnInsertionLineInset", DefaultSettings.columnInsertionLineInset);
            Better_Work_Tab.ScribeCompat.LookValue(ref dragThreshold, "dragThreshold", DefaultSettings.dragThreshold);
            Better_Work_Tab.ScribeCompat.LookValue(ref dragHoverDelay, "dragHoverDelay", DefaultSettings.dragHoverDelay);
            Better_Work_Tab.ScribeCompat.LookValue(ref hideWorkloadButton, "hideWorkloadButton", DefaultSettings.hideWorkloadButton);
            Better_Work_Tab.ScribeCompat.LookValue(ref hideAutoAssignButton, "hideAutoAssignButton", DefaultSettings.hideAutoAssignButton);
            Better_Work_Tab.ScribeCompat.LookValue(ref persistColumnOrder, "persistColumnOrder", DefaultSettings.persistColumnOrder);
            Better_Work_Tab.ScribeCompat.LookValue(ref persistColumnWidths, "persistColumnWidths", DefaultSettings.persistColumnWidths);
            Better_Work_Tab.ScribeCompat.LookValue(ref showColumnMovedMarker, "showColumnMovedMarker", DefaultSettings.showColumnMovedMarker);
            Better_Work_Tab.ScribeCompat.LookValue(ref showColumnBaselineLine, "showColumnBaselineLine", DefaultSettings.showColumnBaselineLine);
            Better_Work_Tab.ScribeCompat.LookValue(ref showMovedColumnColorTint, "showMovedColumnColorTint", DefaultSettings.showMovedColumnColorTint);
            Better_Work_Tab.ScribeCompat.LookValue(ref movedMarkerColor, "movedMarkerColor", DefaultSettings.Color_MovedMarkerColor);
            Better_Work_Tab.ScribeCompat.LookValue(ref dividerMinAlpha, "dividerMinAlpha", DefaultSettings.dividerMinAlpha);
            Better_Work_Tab.ScribeCompat.LookValue(ref disableBestPawnHighlight, "disableBestPawnHighlight", false);
            Better_Work_Tab.ScribeCompat.LookValue(ref bestPawnHighlightThickness, "bestPawnHighlightThickness", 1);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableColumnGrouping, "enableColumnGrouping", false);
            Better_Work_Tab.ScribeCompat.LookCollection(ref hiddenWorktypes, "hiddenWorktypes", LookMode.Value);
            Better_Work_Tab.ScribeCompat.LookValue(ref warnOnApplyRuleset, "warnOnApplyRuleset", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref warnOnApplyWorkload, "warnOnApplyWorkload", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref removeHeaderUnderline, "removeHeaderUnderline", false);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableScrollWheelPriority, "enableScrollWheelPriority", false);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableAngledHeaders, "enableAngledHeaders", DefaultSettings.enableAngledHeaders);
            Better_Work_Tab.ScribeCompat.LookValue(ref angledHeaderRotation, "angledHeaderRotation", (int)DefaultSettings.angledHeaderRotation);
            Better_Work_Tab.ScribeCompat.LookValue(ref angledHeaderHorizontalOffset, "angledHeaderHorizontalOffset", DefaultSettings.angledHeaderHorizontalOffset);
            Better_Work_Tab.ScribeCompat.LookValue(ref useVerticalStackingForCJK, "useVerticalStackingForCJK", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref cjkVerticalKerning, "cjkVerticalKerning", 0.75f);
            Better_Work_Tab.ScribeCompat.LookValue(ref angledHeaderColor, "angledHeaderColor", DefaultSettings.Color_AngledHeaderText);
            Better_Work_Tab.ScribeCompat.LookValue(ref autoEnableManualPriorities, "autoEnableManualPriorities", DefaultSettings.autoEnableManualPriorities);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableExtendedPriorities, "enableExtendedPriorities", DefaultSettings.enableExtendedPriorities);
            Better_Work_Tab.ScribeCompat.LookValue(ref delegateToExternalPriorityMods, "delegateToExternalPriorityMods", DefaultSettings.delegateToExternalPriorityMods);
            Better_Work_Tab.ScribeCompat.LookValue(ref selectedPriorityProviderId, "selectedPriorityProviderId", DefaultSettings.selectedPriorityProviderId);
            PriorityMode inferredPriorityMode = InferPriorityModeFromProviderSelectionFields();
            Better_Work_Tab.ScribeCompat.LookValue(ref priorityMode, "priorityMode", inferredPriorityMode);
            Better_Work_Tab.ScribeCompat.LookValue(ref autoMaxPriorityInt, "autoMaxPriorityInt", DefaultSettings.autoMaxPriority);
            Better_Work_Tab.ScribeCompat.LookValue(ref maxPriorityInt, "maxPriorityInt", DefaultSettings.maxPriority);
            Better_Work_Tab.ScribeCompat.LookValue(ref autoDisabledPriorityMode, "autoDisabledPriorityMode", DefaultSettings.autoDisabledPriorityMode);
            Better_Work_Tab.ScribeCompat.LookValue(ref autoDisabledPriorityFixedValue, "autoDisabledPriorityFixedValue", DefaultSettings.autoDisabledPriorityFixedValue);
            Better_Work_Tab.ScribeCompat.LookValue(ref priorityColorPercentage_Green, "priorityColorPercentage_Green", DefaultSettings.priorityColorPercentage_Green);
            Better_Work_Tab.ScribeCompat.LookValue(ref priorityColorPercentage_Yellow , "priorityColorPercentage_Yellow", DefaultSettings.priorityColorPercentage_Yellow );
            Better_Work_Tab.ScribeCompat.LookValue(ref priorityColorPercentage_Tan , "priorityColorPercentage_Tan", DefaultSettings.priorityColorPercentage_Tan );
            NormalizePrioritySettings();

            if (hiddenWorktypes == null) hiddenWorktypes = new List<string>();

            // Colors
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_CursorHighlight, "Color_CursorHighlight", DefaultSettings.Color_CursorHighlight);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_FloatMenuHighlight, "Color_FloatMenuHighlight", DefaultSettings.Color_FloatMenuHighlight);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_CustomMouseHighlight, "Color_CustomMouseHighlight", DefaultSettings.Color_CustomMouseHighlight);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_CustomSimilarWorktypeHighlight, "Color_CustomSimilarWorktypeHighlight", DefaultSettings.Color_CustomSimilarWorktypeHighlight);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_IncapableBecauseOfCapacities, "Color_IncapableBecauseOfCapacities", DefaultSettings.Color_IncapableBecauseOfCapacities);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_BestPawnForSkillSquare, "Color_BestPawnForSkillSquare", DefaultSettings.Color_BestPawnForSkillSquare);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_VeryLowSkill, "Color_VeryLowSkill", DefaultSettings.Color_VeryLowSkill);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_LowSkill, "Color_LowSkill", DefaultSettings.Color_LowSkill);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_GoodLowSkill, "Color_GoodLowSkill", DefaultSettings.Color_GoodLowSkill);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_ExcellentSkill, "Color_ExcellentSkill", DefaultSettings.Color_ExcellentSkill);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_RowHoverHighlight, "Color_RowHoverHighlight", DefaultSettings.Color_RowHoverHighlight);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_ColumnHoverHighlight, "Color_ColumnHoverHighlight", DefaultSettings.Color_ColumnHoverHighlight);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_SelectedPawnHighlight, "Color_SelectedPawnHighlight", DefaultSettings.Color_SelectedPawnHighlight);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_HeaderText, "Color_HeaderText", DefaultSettings.Color_HeaderText);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_DividerText, "Color_DividerText", DefaultSettings.Color_DividerText);
            Better_Work_Tab.ScribeCompat.LookValue(ref Color_Borders, "Color_Borders", DefaultSettings.Color_Borders);

            // UI modes
            Better_Work_Tab.ScribeCompat.LookValue(ref ShowUIMode_ShowSmallSkillNumbers, "ShowUIMode_ShowSmallSkillNumbers", DefaultSettings.ShowUIMode_ShowSmallSkillNumbers);
            Better_Work_Tab.ScribeCompat.LookValue(ref ShowUIMode_ShowPawnForSkillSquare, "ShowUIMode_ShowPawnForSkillSquare", DefaultSettings.ShowUIMode_ShowPawnForSkillSquare);
            Better_Work_Tab.ScribeCompat.LookValue(ref hoverEffectScope, "hoverEffectScope", DefaultSettings.hoverEffectScope);

            // Future behavior templates
            Better_Work_Tab.ScribeCompat.LookValue(ref confirmRulesetApplication, "confirmRulesetApplication", DefaultSettings.confirmRulesetApplication);
            Better_Work_Tab.ScribeCompat.LookValue(ref dragStartThreshold, "dragStartThreshold", DefaultSettings.dragStartThreshold);
            Better_Work_Tab.ScribeCompat.LookValue(ref scrollSpeed, "scrollSpeed", DefaultSettings.scrollSpeed);
            Better_Work_Tab.ScribeCompat.LookValue(ref showAutoAssignConfirmation, "showAutoAssignConfirmation", DefaultSettings.showAutoAssignConfirmation);
            Better_Work_Tab.ScribeCompat.LookValue(ref resetWorkBeforeAutoAssign, "resetWorkBeforeAutoAssign", DefaultSettings.resetWorkBeforeAutoAssign);
            Better_Work_Tab.ScribeCompat.LookValue(ref showAutoAssignVisualFeedback, "showAutoAssignVisualFeedback", DefaultSettings.showAutoAssignVisualFeedback);
            if (Scribe.mode == LoadSaveMode.Saving && CurrentRuleset != null)
            {
                currentRulesetName = CurrentRuleset.Name;
            }

            Better_Work_Tab.ScribeCompat.LookValue(ref defaultAutoAssignRuleset, "defaultAutoAssignRuleset", "BWT Default");
            Better_Work_Tab.ScribeCompat.LookValue(ref currentRulesetName, "currentRulesetName", "");
            Better_Work_Tab.ScribeCompat.LookValue(ref showWorkloadButtonFooter, "showWorkloadButtonFooter", DefaultSettings.showWorkloadButtonFooter);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableWorkloadSaving, "enableWorkloadSaving", DefaultSettings.enableWorkloadSaving);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableWorkloadLoading, "enableWorkloadLoading", DefaultSettings.enableWorkloadLoading);
            Better_Work_Tab.ScribeCompat.LookValue(ref persistDividersInWorkloads, "persistDividersInWorkloads", DefaultSettings.persistDividersInWorkloads);
            Better_Work_Tab.ScribeCompat.LookValue(ref alwaysShowConditionEditors, "alwaysShowConditionEditors", DefaultSettings.alwaysShowConditionEditors);
            Better_Work_Tab.ScribeCompat.LookValue(ref cacheBedCounts, "cacheBedCounts", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref cacheSkillLevels, "cacheSkillLevels", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref cacheRowDescriptors, "cacheRowDescriptors", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref cacheIncapabilityChecks, "cacheIncapabilityChecks", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref useElementPooling, "useElementPooling", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref viewportCulling, "viewportCulling", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref enableProfiler, "enableProfiler", false);
            Better_Work_Tab.ScribeCompat.LookValue(ref logDebugToFile, "logDebugToFile", false);
            Better_Work_Tab.ScribeCompat.LookValue(ref mpSyncColumnOrder, "mpSyncColumnOrder", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref mpSyncWorkloads, "mpSyncWorkloads", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref mpSyncRulesets, "mpSyncRulesets", true);
            Better_Work_Tab.ScribeCompat.LookValue(ref mpConflictMode, "mpConflictMode", MpConflictMode.PlayerPriority);
            Better_Work_Tab.ScribeCompat.LookValue(ref rulesetViewMode, "rulesetViewMode", RulesetViewMode.Regular);

            //Better_Work_Tab.ScribeCompat.LookValue(ref maxPriorityInt, "maxPriorityInt", 4);

            // Divider settings
            Better_Work_Tab.ScribeCompat.LookValue(ref dividerHeight, "dividerHeight", DefaultSettings.dividerHeight);

            // Load rulesets from save file
            Better_Work_Tab.ScribeCompat.LookCollection(ref SavedRulesets, "SavedRulesets", LookMode.Deep);

            // Reinitialize rulesets after load (restores defaults if missing)
            //InitializeRulesets();

            if (Scribe.mode != LoadSaveMode.Saving)
            {
                Better_Work_Tab.ScribeCompat.LookDeep(ref LegacyWorkGiverReassignments, "workGiverReassignments");
            }

            // Column order and widths persistence
            Better_Work_Tab.ScribeCompat.LookCollection(ref workColumnOrderDefNames, "workColumnOrderDefNames", LookMode.Value);
            ScribeCompat.LookStringDictionary(ref storedColumnWidths, "storedColumnWidths", LookMode.Value);
            Better_Work_Tab.ScribeCompat.LookCollection(ref debugFeatureToggles, "debugFeatureToggles", LookMode.Value, LookMode.Value);

            // Save/load the list of columns the player has directly dragged
            Better_Work_Tab.ScribeCompat.LookCollection(ref playerDraggedColumns, "playerDraggedColumns", LookMode.Value);

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

            NormalizePrioritySettings();

            EnsureDebugFeatureTogglesInitialized();
        }

        /// <summary>
        /// Restores all settings to their default values.
        /// </summary>
        public void RestoreDefaults()
        {
            ApplyRegisteredDefaults();

            workTabMaxHeight = DefaultSettings.workTabMaxHeight;
            settingsViewMode = SettingsViewMode.Simple;
            workColumnOrderDefNames.Clear();
            storedColumnWidths.Clear();
            NormalizePrioritySettings();

            EnsureDebugFeatureTogglesInitialized();
            foreach (var feature in debugFeatureToggles.Keys.ToList())
            {
                debugFeatureToggles[feature] = false;
            }
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
