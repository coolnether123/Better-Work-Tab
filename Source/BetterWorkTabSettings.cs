using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
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
        public static bool enableColumnOrderSaving = true;
        public static bool enableUIElements = true;
        public static bool enablePerformanceOptimizations = true;
        public static bool enableMultiplayerSync = true;
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
        public static bool showPawnCountAtBottom = true;
        public static bool showBedCountAtBottom = true;
        public static bool showPriorityLegend = true;
        public static bool showDragInstructions = true;
        public static bool showManualPrioritiesCheckbox = true;
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
        public static bool enableColumnGrouping = false;
        public static List<string> hiddenWorktypes = new List<string>();
        public static bool warnOnApplyRuleset = true;
        public static bool warnOnApplyWorkload = true;
        public static bool removeHeaderUnderline = false;
        public static bool enableScrollWheelPriority = false;
        public static bool enableAngledHeaders = true;
        public static float angledHeaderRotation = -60f;
        public static BetterWorkTabSettings.ScaleFixMode scaleFixMode = BetterWorkTabSettings.ScaleFixMode.Auto;
        public static bool showRedCenterLine = true;
        public static int angledHeaderXOffset = 0;
        public static int angledHeaderYOffset = 0;
        public static Dictionary<float, Vector2> knownScaleFixes = new Dictionary<float, Vector2>
        {
            { 0.9f, new Vector2(77f, -45f) },
            { 1f, Vector2.zero },
            { 1.25f, new Vector2(-85f, 49f) }
        };
        public static bool autoEnableManualPriorities = false;

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
        public enum ScaleFixMode { Auto, Manual }
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
        public float angledHeaderRotation = DefaultSettings.angledHeaderRotation;
        public ScaleFixMode scaleFixMode = DefaultSettings.scaleFixMode;
        public bool showRedCenterLine = DefaultSettings.showRedCenterLine;
        public int angledHeaderXOffset = DefaultSettings.angledHeaderXOffset;
        public int angledHeaderYOffset = DefaultSettings.angledHeaderYOffset;
        public Dictionary<float, Vector2> knownScaleFixes = new Dictionary<float, Vector2>(DefaultSettings.knownScaleFixes);
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

            var preferred = SavedRulesets.FirstOrDefault(rs =>
                string.Equals(rs.Name, defaultAutoAssignRuleset, StringComparison.OrdinalIgnoreCase));

            return preferred ?? SavedRulesets.First();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref workTabMaxHeight, "workTabMaxHeight", DefaultSettings.workTabMaxHeight);


            // Core feature toggles
            Scribe_Values.Look(ref firstTimeSetupDone, "firstTimeSetupDone", DefaultSettings.firstTimeSetupDone);
            Scribe_Values.Look(ref enableSkillOverlayFeature, "enableSkillOverlayFeature", DefaultSettings.enableSkillOverlayFeature);
            Scribe_Values.Look(ref enableAutoAssignFeature, "enableAutoAssignFeature", DefaultSettings.enableAutoAssignFeature);
            Scribe_Values.Look(ref enableDragDropReordering, "enableDragDropReordering", DefaultSettings.enableDragDropReordering);
            Scribe_Values.Look(ref enableDividers, "enableDividers", DefaultSettings.enableDividers);
            Scribe_Values.Look(ref enableWorkloads, "enableWorkloads", DefaultSettings.enableWorkloads);
            Scribe_Values.Look(ref enableColumnOrderSaving, "enableColumnOrderSaving", DefaultSettings.enableColumnOrderSaving);
            Scribe_Values.Look(ref enableUIElements, "enableUIElements", DefaultSettings.enableUIElements);
            Scribe_Values.Look(ref enablePerformanceOptimizations, "enablePerformanceOptimizations", DefaultSettings.enablePerformanceOptimizations);
            Scribe_Values.Look(ref enableMultiplayerSync, "enableMultiplayerSync", DefaultSettings.enableMultiplayerSync);
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
            Scribe_Values.Look(ref showManualPrioritiesCheckbox, "showManualPrioritiesCheckbox", DefaultSettings.showManualPrioritiesCheckbox);
            Scribe_Values.Look(ref showDividers, "showDividers", DefaultSettings.showDividers);
            Scribe_Values.Look(ref allowCustomDividerColors, "allowCustomDividerColors", DefaultSettings.allowCustomDividerColors);
            Scribe_Values.Look(ref showDividerLabels, "showDividerLabels", DefaultSettings.showDividerLabels);
            Scribe_Values.Look(ref allowDividerCollapse, "allowDividerCollapse", DefaultSettings.allowDividerCollapse);
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
            Scribe_Values.Look(ref dividerMinAlpha, "dividerMinAlpha", DefaultSettings.dividerMinAlpha);
            Scribe_Values.Look(ref disableBestPawnHighlight, "disableBestPawnHighlight", false);
            Scribe_Values.Look(ref bestPawnHighlightThickness, "bestPawnHighlightThickness", 1);
            Scribe_Values.Look(ref enableColumnGrouping, "enableColumnGrouping", false);
            Scribe_Collections.Look(ref hiddenWorktypes, "hiddenWorktypes", LookMode.Value);
            Scribe_Values.Look(ref warnOnApplyRuleset, "warnOnApplyRuleset", true);
            Scribe_Values.Look(ref warnOnApplyWorkload, "warnOnApplyWorkload", true);
            Scribe_Values.Look(ref removeHeaderUnderline, "removeHeaderUnderline", false);
            Scribe_Values.Look(ref enableScrollWheelPriority, "enableScrollWheelPriority", false);
            Scribe_Values.Look(ref enableAngledHeaders, "enableAngledHeaders", true);
            Scribe_Values.Look(ref angledHeaderRotation, "angledHeaderRotation", -60f);
            Scribe_Values.Look(ref scaleFixMode, "scaleFixMode", ScaleFixMode.Auto);
            Scribe_Values.Look(ref showRedCenterLine, "showRedCenterLine", true);
            Scribe_Values.Look(ref angledHeaderXOffset, "angledHeaderXOffset", 0);
            Scribe_Values.Look(ref angledHeaderYOffset, "angledHeaderYOffset", 0);
            Scribe_Collections.Look(ref knownScaleFixes, "knownScaleFixes", LookMode.Value, LookMode.Value);
            if (knownScaleFixes == null) knownScaleFixes = new Dictionary<float, Vector2>(DefaultSettings.knownScaleFixes);
            Scribe_Values.Look(ref autoEnableManualPriorities, "autoEnableManualPriorities", false);

            if (hiddenWorktypes == null) hiddenWorktypes = new List<string>();

            Scribe_Collections.Look(ref SavedRulesets, "SavedRulesets", LookMode.Deep);

            // Reinitialize rulesets after load (restores defaults if missing)
            //InitializeRulesets();

            // Column order and widths persistence
            Scribe_Collections.Look(ref workColumnOrderDefNames, "workColumnOrderDefNames", LookMode.Value);
            Scribe_Collections.Look(ref storedColumnWidths, "storedColumnWidths", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref debugFeatureToggles, "debugFeatureToggles", LookMode.Value, LookMode.Value);

            // Save/load the list of columns the player has directly dragged
            Scribe_Collections.Look(ref playerDraggedColumns, "playerDraggedColumns", LookMode.Value);

            if (storedColumnWidths == null)
            {
                storedColumnWidths = new Dictionary<string, float>();
            }

            if (playerDraggedColumns == null)
            {
                playerDraggedColumns = new List<string>();
            }

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
                    int existingIndex = SavedRulesets.FindIndex(rs =>
                        string.Equals(rs.Name, template.Name, StringComparison.OrdinalIgnoreCase));

                    if (existingIndex >= 0)
                    {
                        SavedRulesets[existingIndex] = template;
                    }
                    else
                    {
                        SavedRulesets.Add(template);
                    }
                }
            }

            SyncWorktypeReferences(SavedRulesets);

            // Re-select by name if the old reference was replaced by a fresh template.
            if (CurrentRuleset != null && !SavedRulesets.Contains(CurrentRuleset))
            {
                CurrentRuleset = SavedRulesets.FirstOrDefault(rs =>
                    string.Equals(rs.Name, CurrentRuleset.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (CurrentRuleset == null && SavedRulesets.Any())
            {
                CurrentRuleset = SelectPreferredRuleset();
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
