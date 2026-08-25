using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.Migration;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SocialPlatforms.Impl;
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

        public static int workTabMaxVisiblePawns = -1; // -1 = use vanilla default (fill screen)
        public static bool keepVanillaWorkTabMinimumWidth = true;
        public static float workTabTopSpace = 40f; // Vanilla MainTabWindow_Work.ExtraTopSpace

        public static bool enableSkillOverlayFeature = true;
        public static bool enableAutoAssignFeature = true;
        public static bool enableDragDropReordering = true;
        public static bool enableDividers = true;
        public static bool showDividerRows = true;
        public static bool enableWorkloads = true;
        public static bool enableWorkloadPreviewRevealAnimation = true;
        public static int workloadPreviewRevealSpeed = 100;
        public static bool enableWorkloadInspectionHighlights = true;
        public static int workloadInspectionOpacity = 100;
        public static bool useLegacyWorkloads = false;
        public static bool enableSubWorkDrilldown = BWT20CohortPolicy.FreshInstall.EnableSubWorkDrilldown;
        public static bool enableFluffyStyleFeatures = BWT20CohortPolicy.FreshInstall.EnableFluffyStyleFeatures;
        public static bool showFluffyStyleTopButtons = false;
        public static bool showStandaloneFluffyStyleTopButtons = false;
        public static bool enableFluffyScheduleAssigner = false;
        public static bool enableSubWorkCrossWorkDragDrop = true;
        public static bool enableCustomWorkLabels = true;
        public static BetterWorkTabSettings.SubWorkDrilldownModifier subWorkDrilldownModifier = BetterWorkTabSettings.SubWorkDrilldownModifier.Ctrl;
        public static BetterWorkTabSettings.SubWorkDrilldownButton subWorkDrilldownButton = BetterWorkTabSettings.SubWorkDrilldownButton.Left;
        public static BetterWorkTabSettings.SubWorkDrilldownStyle subWorkDrilldownStyle = BetterWorkTabSettings.SubWorkDrilldownStyle.FocusView;
        public static bool subWorkCtrlClickNoticeDismissed = false;
        public static bool useVanillaSubWorkGlobalPriorityBoxes = false;
        public static bool useCompactSubWorkPriorityBoxes = true;
        public static bool restoreCursorOnSubWorkExit = true;
        public static bool restoreCursorOnSubWorkPawnCellExit = false;
        public static bool enableSubWorkOverrideBreakAnimation = true;
        public static bool enableSubWorkTransitionAnimation = true;
        public static BetterWorkTabSettings.SubWorkTransitionStyle subWorkTransitionStyle =
            BetterWorkTabSettings.SubWorkTransitionStyle.ClassicGlideFlash;
        public static float subWorkTransitionSeconds = 0.22f;
        public static BetterWorkTabSettings.SubWorkDisabledParentMode subWorkDisabledParentMode =
            BetterWorkTabSettings.SubWorkDisabledParentMode.ParentWorkDisablesSubWork;
        public static bool subWorkAutoExpandColumns = true;
        public static bool subWorkEvenlyExpandColumns = true;
        public static WorkGridRendererMode workGridRendererMode = WorkGridRendererMode.Auto;
        public static bool enableMultiplayerSync = true;
        public static bool hideSettingResetIcons = false;
        public static bool mpShowOtherPlayersHover = false;
        public static bool mpAllowOthersToRequestLayout = true;
        public static bool mpAllowPresenceBroadcast = false;
        public static bool mpShowLinkedIndicator = true;
        public static List<string> workColumnOrderDefNames = new List<string>();
        public static bool firstTimeSetupDone = false;
        public static float dividerHeight = 18f;
        public static bool rowDraggingEnabled = true;
        public static bool columnDraggingEnabled = true;
        public static int columnInsertionLineInset = 5;
        public static int dragThreshold = 5;
        public static float dragHoverDelay = 0.5f;
        public static bool requireCtrlForDrag = false;
        public static bool disableLeftClickClose = true;
        public static bool closeOnMapClick = true;
        public static bool enableContextMenuOnRightClick = true;
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
        public static bool showGeneralTutorial = BWT20CohortPolicy.FreshInstall.ShowGeneralTutorial;
        public static bool useRuleBuilder2 = BWT20CohortPolicy.FreshInstall.UseRuleBuilder2;
        public static bool ruleBuilder2ShowWorkTabHighlights = true;
        public static bool ruleBuilder2EnableAnimations = true;
        public static bool ruleBuilder2ShowAdvancedConditions = false;
        public static bool ruleBuilder2ShowMatchedPanel = true;
        public static bool showManualPrioritiesCheckbox = true;
        public static bool enableTimePrioritySchedules = BWT20CohortPolicy.FreshInstall.EnableTimePrioritySchedules;
        public static bool showTimePriorityCopyPasteButtons = true;
        public static bool enableChronosPointerTimePriorityIntegration = false;
        public static bool showTimePriorityHourDivider = true;
        public static bool keepTimePrioritySourceColumnHighlighted = true;
        public static bool enableFluffyTimePriorityMirroring = false;
        public static bool chronosPointerTimePriorityIncidentOverlay = false;
        public static bool allowCustomDividerColors = true;
        public static bool showDividerLabels = true;
        public static bool allowDividerCollapse = true;
        public static bool enableDividerAnimations = true;
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
        public static PriorityDataAuthorityPreference priorityDataAuthority = PriorityDataAuthorityPreference.Automatic;
        public static bool showExternalWorkTabColumns = false;
        public static PriorityMode priorityMode = PriorityMode.Auto;
        public static bool enableExtendedPriorities = false;
        public static bool delegateToExternalPriorityMods = true;
        public static string selectedPriorityProviderId = PriorityConstants.AutoProviderId;
        public static BetterWorkTabSettings.AutoDisabledPriorityMode autoDisabledPriorityMode =
            BetterWorkTabSettings.AutoDisabledPriorityMode.EveryMultipleOfFour;
        public static int autoDisabledPriorityFixedValue = PriorityConstants.VanillaMax;

        // Pawn/worktype highlight visibility settings
        public static bool ShowPawnAndWorktypeHighlights = true;
        public static bool ShowCursorPawnAndWorktypeHighlight = true;
        public static bool ShowFloatMenuPawnAndWorktypeHighlight = true;
        public static bool DoSelectedPawnHighlight = true;

        // Custom highlight color settings
        public static bool ShowSimilarWorktypeHighlight = true;
        public static float SelectedPawnHighlightOpacity = 0.3f;
        public static float SimilarWorktypeHighlightOpacity = 0.4f;

        // Highlight colors (RGBA)
        public static Color Color_CursorHighlight = new Color(0.5568628f, 0.5529412f, 0.5529412f, 0.5803922f);
        public static Color Color_FloatMenuHighlight = new Color(0.114f, 0.737f, 0.737f, 0.5f);
        public static Color Color_CustomSimilarWorktypeHighlight = new Color(0.5568628f, 0.5529412f, 0.5529412f, 0.5803922f);
        public static Color Color_IncapableBecauseOfCapacities = new Color(1f, 0.3f, 0.3f);
        public static Color Color_BestPawnForSkillSquare = new Color(0.35f, 0.85f, 0.35f);
        public static Color Color_RowHoverHighlight = Color_CursorHighlight;
        public static Color Color_ColumnHoverHighlight = Color_CursorHighlight;
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
        public static bool enableDebugLogging = false;
        public static bool debugPrintLayout = false;
        public static string defaultAutoAssignRuleset = "BWT Default";
        public static bool enableProfiler = false;
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
        private const int CurrentSettingsSchemaVersion = BWT20UpgradePolicy.CurrentSettingsSchemaVersion;
        private static readonly HashSet<string> AllowedSettingsEnvelopeAttributes =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Class",
                "IsNull",
                "MayRequire",
                "MayRequireAnyOf",
                "MayRequireAllOf"
            };

        [NonSerialized]
        private bool _settingsPersistenceReadOnly;

        [NonSerialized]
        private string _settingsPersistenceDiagnostic = string.Empty;

        public int settingsSchemaVersion = BWT20UpgradePolicy.CurrentSettingsSchemaVersion;
        public bool v2UpgradePromptPending;
        public int fluffyWorkTabActivePromptVersion;
        // Compatibility state is kept separate from the owner enum so a newly
        // detected Sleek installation can prefer the mixed host without
        // overriding a deliberate owner choice later.
        public bool workTabOwnerSelectionMade;
        public bool sleekWorkTabChoicePromptDismissed;
        public bool sleekWorkTabUseMixedByDefault = true;

        public BetterWorkTabSettings()
        {
            // Initialize rulesets immediately on construction
            //InitializeRulesets();
        }

        /// <summary>
        /// True when the settings document contained a schema or root shape
        /// this build cannot prove it can preserve. The settings may still be
        /// inspected in memory, but all subsequent Scribe writes fail closed.
        /// </summary>
        internal bool IsPersistenceReadOnly => _settingsPersistenceReadOnly;

        internal string PersistenceDiagnostic => _settingsPersistenceDiagnostic;

        public enum SettingsViewMode
        {
            Simple,
            Advanced
        }

        public SettingsViewMode settingsViewMode = DefaultSettings.settingsViewMode;
        public bool useOutlineHighlights = DefaultSettings.useOutlineHighlights;
        public bool enableScrollWheelPriority = DefaultSettings.enableScrollWheelPriority;
        public WorkTabOwnerPreference preferredWorkTabOwner = DefaultSettings.preferredWorkTabOwner;
        public PriorityDataAuthorityPreference priorityDataAuthority = DefaultSettings.priorityDataAuthority;
        public bool showExternalWorkTabColumns = DefaultSettings.showExternalWorkTabColumns;

        public bool firstTimeSetupDone = DefaultSettings.firstTimeSetupDone;

        // Master feature toggles
        public bool enableSkillOverlayFeature = DefaultSettings.enableSkillOverlayFeature;
        public bool enableAutoAssignFeature = DefaultSettings.enableAutoAssignFeature;
        public bool enableDragDropReordering = DefaultSettings.enableDragDropReordering;
        public bool enableDividers = DefaultSettings.enableDividers;
        public bool showDividerRows = DefaultSettings.showDividerRows;
        public bool enableWorkloads = DefaultSettings.enableWorkloads;
        public bool enableWorkloadPreviewRevealAnimation = DefaultSettings.enableWorkloadPreviewRevealAnimation;
        public int workloadPreviewRevealSpeed = DefaultSettings.workloadPreviewRevealSpeed;
        public bool enableWorkloadInspectionHighlights = DefaultSettings.enableWorkloadInspectionHighlights;
        public int workloadInspectionOpacity = DefaultSettings.workloadInspectionOpacity;
        public bool useLegacyWorkloads = DefaultSettings.useLegacyWorkloads;
        public bool enableSubWorkDrilldown = DefaultSettings.enableSubWorkDrilldown;
        public bool enableFluffyStyleFeatures = DefaultSettings.enableFluffyStyleFeatures;
        public bool showFluffyStyleTopButtons = DefaultSettings.showFluffyStyleTopButtons;
        public bool showStandaloneFluffyStyleTopButtons = DefaultSettings.showStandaloneFluffyStyleTopButtons;
        public bool enableFluffyScheduleAssigner = DefaultSettings.enableFluffyScheduleAssigner;
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
        public WorkGridRendererMode workGridRendererMode = DefaultSettings.workGridRendererMode;
        public bool enableMultiplayerSync = DefaultSettings.enableMultiplayerSync;
        public bool hideSettingResetIcons = DefaultSettings.hideSettingResetIcons;
        public float dividerHeight = DefaultSettings.dividerHeight;
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
        internal int tutorialProgressSchemaVersion;
        internal BWTTutorialCourse selectedTutorialCourse;
        internal bool tutorialMigratedFromPublic105;
        internal List<string> skippedTutorialLessonIds = new List<string>();

        // Lessons whose feature the player was already using when the tour
        // looked. Kept apart from completed IDs because it is a different claim:
        // completed means the tour taught it, this means the colony showed it
        // had been used. The tour treats both as settled, and says which is
        // which, rather than telling somebody they finished a lesson they never
        // opened. Recorded rather than recomputed, so it survives a new colony.
        internal List<string> tutorialLessonIdsAlreadyUsed = new List<string>();

        // Whether the player has been shown what they have switched off. The
        // tour holds itself open the first time it runs out of lessons while
        // features remain disabled, so the offer is actually seen rather than
        // closed over. Once it has been made, the tour is free to finish.
        internal bool tutorialDiscoveryOfferAcknowledged;
        public bool useRuleBuilder2 = DefaultSettings.useRuleBuilder2;
        public bool ruleBuilder2ShowWorkTabHighlights = DefaultSettings.ruleBuilder2ShowWorkTabHighlights;
        public bool ruleBuilder2EnableAnimations = DefaultSettings.ruleBuilder2EnableAnimations;
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
        public bool allowCustomDividerColors = DefaultSettings.allowCustomDividerColors;
        public bool showDividerLabels = DefaultSettings.showDividerLabels;
        public bool allowDividerCollapse = DefaultSettings.allowDividerCollapse;
        public bool enableDividerAnimations = DefaultSettings.enableDividerAnimations;
        public bool persistColumnWidths = DefaultSettings.persistColumnWidths;
        public bool showColumnMovedMarker = DefaultSettings.showColumnMovedMarker;
        public bool showColumnBaselineLine = DefaultSettings.showColumnBaselineLine;
        public bool showMovedColumnColorTint = DefaultSettings.showMovedColumnColorTint;
        public Color movedMarkerColor = DefaultSettings.Color_MovedMarkerColor;
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
        public Color Color_CursorHighlight = DefaultSettings.Color_CursorHighlight;
        public Color Color_FloatMenuHighlight = DefaultSettings.Color_FloatMenuHighlight;
        public Color Color_CustomSimilarWorktypeHighlight = DefaultSettings.Color_CustomSimilarWorktypeHighlight;
        public Color Color_RowHoverHighlight = DefaultSettings.Color_RowHoverHighlight;
        public Color Color_ColumnHoverHighlight = DefaultSettings.Color_ColumnHoverHighlight;
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
                return halvedAlpha;
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
        public string defaultAutoAssignRuleset = DefaultSettings.defaultAutoAssignRuleset;

        // Workloads
        public bool alwaysShowConditionEditors = DefaultSettings.alwaysShowConditionEditors;

        // Performance

        // Debug and profiling
        public bool enableProfiler = DefaultSettings.enableProfiler;

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
            selectedPriorityProviderId = string.IsNullOrWhiteSpace(providerId)
                ? DefaultSettings.selectedPriorityProviderId
                : providerId.Trim();
            SyncProviderSelectionFieldsFromMode();
        }

        public void NormalizePrioritySettings()
        {
            if (!Enum.IsDefined(typeof(PriorityDataAuthorityPreference), priorityDataAuthority))
            {
                priorityDataAuthority = DefaultSettings.priorityDataAuthority;
            }

            if (!Enum.IsDefined(typeof(PriorityMode), priorityMode))
            {
                priorityMode = InferPriorityModeFromProviderSelectionFields();
            }

            if (string.IsNullOrWhiteSpace(selectedPriorityProviderId))
            {
                selectedPriorityProviderId = DefaultSettings.selectedPriorityProviderId;
            }

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
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (_settingsPersistenceReadOnly)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrEmpty(_settingsPersistenceDiagnostic)
                            ? "The Better Work Tab settings document is read-only for diagnostics."
                            : _settingsPersistenceDiagnostic);
                }

                if (settingsSchemaVersion < 0 || settingsSchemaVersion > CurrentSettingsSchemaVersion)
                {
                    throw new InvalidOperationException(
                        "The Better Work Tab settings document uses an unsupported schema version and cannot be rewritten.");
                }
            }

            XmlNode settingsXml = Scribe.mode == LoadSaveMode.LoadingVars
                ? Scribe.loader?.curXmlParent
                : null;
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                _settingsPersistenceReadOnly = false;
                _settingsPersistenceDiagnostic = string.Empty;
            }

            HashSet<string> persistedKeys = BWT20SettingsMigration.CapturePersistedKeys();
            Scribe_Values.Look(ref settingsSchemaVersion, "settingsSchemaVersion", 0);
            Scribe_Values.Look(ref v2UpgradePromptPending, "v2UpgradePromptPending", false);
            Scribe_Values.Look(ref fluffyWorkTabActivePromptVersion, "fluffyWorkTabActivePromptVersion", 0);
            Scribe_Values.Look(ref workTabOwnerSelectionMade, "workTabOwnerSelectionMade", false);
            Scribe_Values.Look(ref sleekWorkTabChoicePromptDismissed, "sleekWorkTabChoicePromptDismissed", false);
            Scribe_Values.Look(ref sleekWorkTabUseMixedByDefault, "sleekWorkTabUseMixedByDefault", true);

            // Add new settings in BWTSettingsRegistry's HOW TO ADD A SETTING block.
            BWTSettingsRegistry.EnsureInitialized();

            bool unsafeSettingsEnvelope = Scribe.mode == LoadSaveMode.LoadingVars &&
                (settingsSchemaVersion < 0 ||
                 settingsSchemaVersion > CurrentSettingsSchemaVersion ||
                 HasUnknownSettingsEnvelopeShape(settingsXml));
            if (unsafeSettingsEnvelope)
            {
                _settingsPersistenceReadOnly = true;
                _settingsPersistenceDiagnostic = settingsSchemaVersion > CurrentSettingsSchemaVersion
                    ? "The Better Work Tab settings document uses a newer schema and cannot be safely rewritten."
                    : settingsSchemaVersion < 0
                        ? "The Better Work Tab settings document uses an unsupported schema version and cannot be safely rewritten."
                        : "The Better Work Tab settings document contains fields this build cannot preserve.";
            }

            BWTSettingsRegistry.Schema.Scribe(this);

            if (Scribe.mode == LoadSaveMode.LoadingVars && !_settingsPersistenceReadOnly)
            {
                MigrateLegacySettings();
            }

            // REGISTERED PREFERENCES WITH MIGRATION BEHAVIOR
            subWorkTransitionSeconds = ClampSubWorkTransitionSeconds(subWorkTransitionSeconds);
            NormalizeWorkloadPresentationSettings();
            Scribe_Collections.Look(ref hiddenWorktypes, "hiddenWorktypes", LookMode.Value);
            if (hiddenWorktypes == null)
            {
                hiddenWorktypes = new List<string>();
            }

            // Legacy inputs to priorityMode inference; re-synced from priorityMode after load.
            Scribe_Values.Look(ref enableExtendedPriorities, "enableExtendedPriorities", DefaultSettings.enableExtendedPriorities);
            Scribe_Values.Look(ref delegateToExternalPriorityMods, "delegateToExternalPriorityMods", DefaultSettings.delegateToExternalPriorityMods);
            Scribe_Values.Look(ref selectedPriorityProviderId, "selectedPriorityProviderId", DefaultSettings.selectedPriorityProviderId);

            // priorityMode is registered for UI/reset, but uses a dynamic 1.0.5 migration default.
            PriorityMode inferredPriorityMode = InferPriorityModeFromProviderSelectionFields();
            Scribe_Values.Look(ref priorityMode, "priorityMode", inferredPriorityMode);
            NormalizePrioritySettings();

            // STATE AND COMPLEX DATA: intentionally not reset by registry defaults.
            Scribe_Values.Look(ref firstTimeSetupDone, "firstTimeSetupDone", DefaultSettings.firstTimeSetupDone);
            Scribe_Values.Look(ref subWorkCtrlClickNoticeDismissed, "subWorkCtrlClickNoticeDismissed", DefaultSettings.subWorkCtrlClickNoticeDismissed);
            Scribe_Values.Look(ref tutorialFlowVersion, "tutorialFlowVersion", 0);
            Scribe_Values.Look(ref tutorialWelcomeCompleted, "tutorialWelcomeCompleted", false);
            Scribe_Values.Look(ref activeTutorialLessonId, "activeTutorialLessonId", string.Empty);
            Scribe_Values.Look(ref tutorialLessonPhase, "tutorialLessonPhase", 0);
            Scribe_Collections.Look(ref completedTutorialLessonIds, "completedTutorialLessonIds", LookMode.Value);
            Scribe_Values.Look(ref tutorialProgressSchemaVersion, "tutorialProgressSchemaVersion", 0);
            Scribe_Values.Look(ref selectedTutorialCourse, "selectedTutorialCourse", BWTTutorialCourse.None);
            Scribe_Values.Look(ref tutorialMigratedFromPublic105, "tutorialMigratedFromPublic105", false);
            Scribe_Collections.Look(ref skippedTutorialLessonIds, "skippedTutorialLessonIds", LookMode.Value);
            Scribe_Collections.Look(ref tutorialLessonIdsAlreadyUsed, "tutorialLessonIdsAlreadyUsed", LookMode.Value);
            Scribe_Values.Look(ref tutorialDiscoveryOfferAcknowledged, "tutorialDiscoveryOfferAcknowledged", false);
            if (completedTutorialLessonIds == null)
            {
                completedTutorialLessonIds = new List<string>();
            }
            if (Scribe.mode == LoadSaveMode.Saving && CurrentRuleset != null)
            {
                currentRulesetName = CurrentRuleset.Name;
            }

            if (Scribe.mode == LoadSaveMode.Saving && CurrentRuleBuilder2Ruleset != null)
            {
                currentRuleBuilder2RulesetStableId = CurrentRuleBuilder2Ruleset.StableId;
            }

            Scribe_Values.Look(ref defaultAutoAssignRuleset, "defaultAutoAssignRuleset", DefaultSettings.defaultAutoAssignRuleset);
            Scribe_Values.Look(ref currentRulesetName, "currentRulesetName", "");
            Scribe_Values.Look(ref currentRuleBuilder2RulesetStableId, "currentRuleBuilder2RulesetStableId", "");
            Scribe_Collections.Look(ref SavedRulesets, "SavedRulesets", LookMode.Deep);
            Scribe_Collections.Look(ref SavedRuleBuilder2Rulesets, "SavedRuleBuilder2Rulesets", LookMode.Deep);

            if (Scribe.mode != LoadSaveMode.Saving)
            {
                Scribe_Deep.Look(ref LegacyWorkGiverReassignments, "workGiverReassignments");
            }

            Scribe_Collections.Look(ref workColumnOrderDefNames, "workColumnOrderDefNames", LookMode.Value);
            Scribe_Collections.Look(ref storedColumnWidths, "storedColumnWidths", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref debugFeatureToggles, "debugFeatureToggles", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref viewedSettingIds, "viewedSettingIds", LookMode.Value);
            Scribe_Collections.Look(ref playerDraggedColumns, "playerDraggedColumns", LookMode.Value);

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
            bool migratedSettings = !_settingsPersistenceReadOnly &&
                                     BWT20SettingsMigration.ApplyIfNeeded(this, persistedKeys);
            bool migratedFromPublic105 = migratedSettings &&
                                          BWT20UpgradePolicy.IsPublic105SettingsDocument(persistedKeys);
            if (Scribe.mode == LoadSaveMode.LoadingVars && !_settingsPersistenceReadOnly)
            {
                BWTTutorialProgressMigration.Apply(this, migratedFromPublic105);
            }
            NormalizePrioritySettings();
            NormalizeWorkTabHeightSettings();

            EnsureDebugFeatureTogglesInitialized();
        }

        private static bool HasUnknownSettingsEnvelopeShape(XmlNode settingsXml)
        {
            if (settingsXml == null)
            {
                return false;
            }

            if (settingsXml.Attributes != null)
            {
                foreach (XmlAttribute attribute in settingsXml.Attributes)
                {
                    if (attribute == null || AllowedSettingsEnvelopeAttributes.Contains(attribute.Name))
                    {
                        continue;
                    }

                    return true;
                }
            }

            var knownKeys = new HashSet<string>(StringComparer.Ordinal)
            {
                "settingsSchemaVersion",
                "v2UpgradePromptPending",
                "fluffyWorkTabActivePromptVersion",
                "workTabOwnerSelectionMade",
                "sleekWorkTabChoicePromptDismissed",
                "sleekWorkTabUseMixedByDefault",
                "hiddenWorktypes",
                "enableExtendedPriorities",
                "delegateToExternalPriorityMods",
                "selectedPriorityProviderId",
                "priorityMode",
                "firstTimeSetupDone",
                "subWorkCtrlClickNoticeDismissed",
                "tutorialFlowVersion",
                "tutorialWelcomeCompleted",
                "activeTutorialLessonId",
                "tutorialLessonPhase",
                "completedTutorialLessonIds",
                "tutorialProgressSchemaVersion",
                "selectedTutorialCourse",
                "tutorialMigratedFromPublic105",
                "skippedTutorialLessonIds",
                "tutorialLessonIdsAlreadyUsed",
                "tutorialDiscoveryOfferAcknowledged",
                "defaultAutoAssignRuleset",
                "currentRulesetName",
                "currentRuleBuilder2RulesetStableId",
                "SavedRulesets",
                "SavedRuleBuilder2Rulesets",
                "workGiverReassignments",
                "workColumnOrderDefNames",
                "storedColumnWidths",
                "debugFeatureToggles",
                "viewedSettingIds",
                "playerDraggedColumns",
                // Retired 1.x keys are still read by MigrateLegacySettings.
                "disableBestPawnHighlight",
                "workTabMaxHeight",
                "enableRowColumnHighlights",
                "showDividers",
                "enableUIElements",
                "UseCustomMouseHoverHighlight",
                "Color_CustomMouseHighlight"
            };

            foreach (FieldInfo field in typeof(BetterWorkTabSettings).GetFields(
                         BindingFlags.Instance | BindingFlags.Public))
            {
                knownKeys.Add(field.Name);
            }

            foreach (SettingDefinition definition in BWTSettingsRegistry.Schema.Definitions)
            {
                string key = BWTSettingsRegistry.Schema.EffectiveScribeKey(definition);
                if (!string.IsNullOrEmpty(key))
                {
                    knownKeys.Add(key);
                }
            }

            foreach (XmlNode child in settingsXml.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (!knownKeys.Contains(child.Name))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Restores all settings to their default values.
        /// </summary>
        public void RestoreDefaults()
        {
            // RestoreDefaults mutates this object in place. Do not let a
            // global reset tear down the presentation baseline underneath an
            // active workload projection; the settings ownership policy is
            // also used by JSON import and the settings drawer.
            if (BWTWorkloadSettingsOwnershipPolicy.IsBulkSettingsOperationBlocked(
                    out string blockedReason))
            {
                Messages.Message(
                    string.IsNullOrEmpty(blockedReason)
                        ? "Restore defaults is disabled while a workload preview owns presentation settings."
                        : blockedReason,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            IReadOnlyCollection<string> changedPreferenceFields = ApplyRegisteredDefaults();

            workTabMaxVisiblePawns = DefaultSettings.workTabMaxVisiblePawns;
            workTabTopSpace = DefaultSettings.workTabTopSpace;
            settingsViewMode = SettingsViewMode.Simple;
            enableSubWorkTransitionAnimation = DefaultSettings.enableSubWorkTransitionAnimation;
            subWorkDrilldownStyle = DefaultSettings.subWorkDrilldownStyle;
            subWorkCtrlClickNoticeDismissed = DefaultSettings.subWorkCtrlClickNoticeDismissed;
            subWorkTransitionStyle = DefaultSettings.subWorkTransitionStyle;
            subWorkTransitionSeconds = DefaultSettings.subWorkTransitionSeconds;
            preferredWorkTabOwner = DefaultSettings.preferredWorkTabOwner;
            priorityDataAuthority = DefaultSettings.priorityDataAuthority;
            showExternalWorkTabColumns = DefaultSettings.showExternalWorkTabColumns;
            workTabOwnerSelectionMade = false;
            sleekWorkTabChoicePromptDismissed = false;
            sleekWorkTabUseMixedByDefault = true;

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

            // STATE RESET: the tutorial is switched back on by the registered
            // defaults above, so where it had got to has to go with it. Left
            // behind, it resumes mid-course on whichever lesson was open and
            // keeps counting the ones already finished.
            BWTGeneralTutorial.ResetToFirstRun(this);

            // STATE RESET: debug feature toggles are runtime state, not user preferences.
            EnsureDebugFeatureTogglesInitialized();
            foreach (var feature in debugFeatureToggles.Keys.ToList())
            {
                debugFeatureToggles[feature] = false;
            }

            BWTSettingsRegistry.Schema.NotifyPreferenceChanges(
                this,
                changedPreferenceFields);
            BetterWorkTabSettingsUI.NotifySettingsChanged();
        }

        /// <summary>
        /// Reads retired XML keys once and folds their values into current settings.
        /// </summary>
        private void MigrateLegacySettings()
        {
            bool disableBestPawnHighlight = false;
            float workTabMaxHeight = -1f;
            bool enableRowColumnHighlights = true;
            bool showDividers = true;
            bool enableUIElements = true;
            bool useCustomMouseHoverHighlight = false;
            // Historical default used when old saves omitted the optional color key.
            Color customMouseHighlight = new Color(0.737f, 0.737f, 0.114f, 0.25f);

            Scribe_Values.Look(ref disableBestPawnHighlight, "disableBestPawnHighlight", false);
            Scribe_Values.Look(ref workTabMaxHeight, "workTabMaxHeight", -1f);
            Scribe_Values.Look(ref enableRowColumnHighlights, "enableRowColumnHighlights", true);
            Scribe_Values.Look(ref showDividers, "showDividers", true);
            Scribe_Values.Look(ref enableUIElements, "enableUIElements", true);
            Scribe_Values.Look(ref useCustomMouseHoverHighlight, "UseCustomMouseHoverHighlight", false);
            Scribe_Values.Look(
                ref customMouseHighlight,
                "Color_CustomMouseHighlight",
                new Color(0.737f, 0.737f, 0.114f, 0.25f));

            if (disableBestPawnHighlight)
            {
                ShowUIMode_ShowPawnForSkillSquare = ShowUIMode.Never;
            }

            if (workTabMaxVisiblePawns < 0 && workTabMaxHeight > 0f)
            {
                workTabMaxVisiblePawns = Mathf.Clamp(Mathf.RoundToInt(workTabMaxHeight / 30f), 1, 200);
            }

            if (!enableRowColumnHighlights)
            {
                ShowPawnAndWorktypeHighlights = false;
            }

            if (!showDividers)
            {
                enableDividers = false;
                showDividerRows = false;
            }

            if (!enableUIElements)
            {
                showPawnCountAtBottom = false;
                showBedCountAtBottom = false;
                showContextSettingsHint = false;
                showManualPrioritiesCheckbox = false;
                showPriorityLegend = false;
                showDragInstructions = false;
            }

            if (useCustomMouseHoverHighlight)
            {
                Color_CursorHighlight = customMouseHighlight;
                Color_RowHoverHighlight = customMouseHighlight;
                Color_ColumnHoverHighlight = customMouseHighlight;
            }
        }

        private void NormalizeWorkTabHeightSettings()
        {
            if (workTabMaxVisiblePawns == 0 || workTabMaxVisiblePawns < -1)
            {
                workTabMaxVisiblePawns = DefaultSettings.workTabMaxVisiblePawns;
            }

        }

        public static float ClampSubWorkTransitionSeconds(float value)
        {
            return Mathf.Clamp(value, 0.2f, 0.9f);
        }

        public static int ClampWorkloadPreviewRevealSpeed(int value)
        {
            return Mathf.Clamp(value, 25, 200);
        }

        public static int ClampWorkloadInspectionOpacity(int value)
        {
            return Mathf.Clamp(value, 0, 100);
        }

        private void NormalizeWorkloadPresentationSettings()
        {
            workloadPreviewRevealSpeed = ClampWorkloadPreviewRevealSpeed(workloadPreviewRevealSpeed);
            workloadInspectionOpacity = ClampWorkloadInspectionOpacity(workloadInspectionOpacity);
        }

        /// <summary>
        /// Applies default values declared in the settings registry to matching fields.
        /// </summary>
        private IReadOnlyCollection<string> ApplyRegisteredDefaults()
        {
            BWTSettingsRegistry.EnsureInitialized();
            return BWTSettingsRegistry.Schema.ApplyPreferenceDefaults(this);
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
