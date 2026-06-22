using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Patches;
using Better_Work_Tab.UI;
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
using Multiplayer.API;
#endif
using RimWorld;
using Spine.UI.ColourPicker;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Verse;
using static Better_Work_Tab.UI.Settings.SettingIDs;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Registers Better Work Tab settings and builds the hierarchy used by the UI.
    /// </summary>
    public static class BWTSettingsRegistry
    {
        private static List<SettingDefinition> _settings;
        private static SettingsHierarchy _hierarchy;
        private static bool _initialized;

        /// <summary>
        /// Gets the built hierarchy for all settings.
        /// </summary>
        public static SettingsHierarchy Hierarchy
        {
            get
            {
                EnsureInitialized();
                return _hierarchy;
            }
        }

        /// <summary>
        /// Gets the raw definitions list.
        /// </summary>
        public static IReadOnlyList<SettingDefinition> Definitions
        {
            get
            {
                EnsureInitialized();
                return _settings;
            }
        }

        /// <summary>
        /// Ensures registration happens exactly once.
        /// </summary>
        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            RegisterAllSettings();
            _hierarchy = new SettingsHierarchy(_settings);
            _initialized = true;
        }

        private static void Register(SettingDefinition def)
        {
            _settings.Add(def);
        }

        /// <summary>
        /// Adds all setting definitions with hierarchy relationships.
        /// </summary>
        private static void RegisterAllSettings()
        {
            _settings = new List<SettingDefinition>();

            Register(new SettingDefinition
            {
                Id = FeaturesOverlay,
                FieldName = "enableSkillOverlayFeature",
                Label = "Skill Overlay",
                Tooltip = "Enable skill overlay visuals (configurable for shifted or unshifted).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableSkillOverlayFeature,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -49,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.9f, 0.7f, 0.4f)
            });

            Register(new SettingDefinition
            {
                Id = FeaturesDragdrop,
                FieldName = "enableDragDropReordering",
                Label = "Drag & Drop Reordering",
                Tooltip = "Disabling prevents reordering but keeps saved order",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableDragDropReordering,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -48,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.5f, 0.8f, 0.5f)
            });

            Register(new SettingDefinition
            {
                Id = FeaturesHighlights,
                FieldName = "ShowPawnAndWorktypeHighlights",
                Label = "Highlighting",
                Tooltip = "Enable row/column highlights.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ShowPawnAndWorktypeHighlights,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -47,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.4f, 0.6f, 0.9f)
            });

            Register(new SettingDefinition
            {
                Id = FeaturesDividers,
                FieldName = "enableDividers",
                Label = "Dividers",
                Tooltip = "Enable divider rows between pawns.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableDividers,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -46,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.8f, 0.8f, 0.6f)
            });

            Register(new SettingDefinition
            {
                Id = FeaturesAutoassign,
                FieldName = "enableAutoAssignFeature",
                Label = "Auto-Assign Rules",
                Tooltip = "Enable the auto-assign ruleset system (buttons hidden when disabled).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableAutoAssignFeature,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -45,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.6f, 0.6f, 0.6f)
            });

            Register(new SettingDefinition
            {
                Id = AutoassignWarnOnApply,
                ParentId = FeaturesAutoassign,
                FieldName = "warnOnApplyRuleset",
                Label = "Warn before applying Ruleset",
                Tooltip = "Show a confirmation warning before applying a ruleset to all colonists.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = FeaturesWorkloads,
                FieldName = "enableWorkloads",
                Label = "Workloads",
                Tooltip = "Enable workload controls (buttons hidden when disabled).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableWorkloads,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -44,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.6f, 0.6f, 0.6f)
            });

            Register(new SettingDefinition
            {
                Id = WorkloadsWarnOnApply,
                ParentId = FeaturesWorkloads,
                FieldName = "warnOnApplyWorkload",
                Label = "Warn before applying Workload",
                Tooltip = "Show a confirmation warning before applying a workload to all colonists.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = FeaturesSubWorkJobs,
                FieldName = "enableSubWorkDrilldown",
                Label = "Sub-work Jobs",
                Tooltip = "Open a work type into its individual jobs. Use the configured shortcut on a work header or cell. Use it again, or press Escape, to return.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableSubWorkDrilldown,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -43,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.55f, 0.75f, 0.9f)
            });

            Register(new SettingDefinition
            {
                Id = SubWorkOpenModifier,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "subWorkDrilldownModifier",
                Label = "Open modifier",
                Tooltip = "Modifier key required to open or leave a sub-work job view.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.SubWorkDrilldownModifier),
                DefaultValue = DefaultSettings.subWorkDrilldownModifier,
                ShowInSimpleView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = SubWorkOpenButton,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "subWorkDrilldownButton",
                Label = "Open mouse button",
                Tooltip = "Mouse button used with the modifier key to open or leave a sub-work job view.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.SubWorkDrilldownButton),
                DefaultValue = DefaultSettings.subWorkDrilldownButton,
                ShowInSimpleView = true,
                SortOrder = 2
            });

            Register(new SettingDefinition
            {
                Id = SubWorkGlobalVanillaPriorityBoxes,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "useVanillaSubWorkGlobalPriorityBoxes",
                Label = "Vanilla global priority boxes",
                Tooltip = "Render the global sub-work priority row with vanilla-style work priority boxes. Off keeps BWT's custom global row render.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.useVanillaSubWorkGlobalPriorityBoxes,
                ShowInSimpleView = true,
                SortOrder = 3
            });

            Register(new SettingDefinition
            {
                Id = SubWorkRestoreCursor,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "restoreCursorOnSubWorkExit",
                Label = "Restore cursor from headers",
                Tooltip = "When leaving from a sub-work header, move the cursor back to the work type header used to enter the sub-work job view.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.restoreCursorOnSubWorkExit,
                ShowInSimpleView = true,
                SortOrder = 4
            });

            Register(new SettingDefinition
            {
                Id = SubWorkRestoreCursorFromPawnCells,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "restoreCursorOnSubWorkPawnCellExit",
                Label = "Restore cursor from pawn cells",
                Tooltip = "When leaving from a pawn priority cell, move the cursor back to the work type header used to enter the sub-work job view. Off keeps the cursor where you clicked.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.restoreCursorOnSubWorkPawnCellExit,
                ShowInSimpleView = true,
                SortOrder = 5
            });

            Register(new SettingDefinition
            {
                Id = SubWorkAutoExpandColumns,
                ParentId = FeaturesSubWorkJobs,
                FieldName = "subWorkAutoExpandColumns",
                Label = "Auto width expansion",
                Tooltip = "Allow sub-work priority columns to use empty table width so long labels have room and the pawn name column stays unchanged.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.subWorkAutoExpandColumns,
                ControlsChildVisibility = true,
                OnChanged = _ => MainTabWindow_BetterWork.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 6
            });

            Register(new SettingDefinition
            {
                Id = SubWorkEvenlyExpandColumns,
                ParentId = SubWorkAutoExpandColumns,
                FieldName = "subWorkEvenlyExpandColumns",
                Label = "Even width expansion",
                Tooltip = "Spread expanded sub-work priority columns evenly. Turn this off to widen only the columns that need more label room.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.subWorkEvenlyExpandColumns,
                OnChanged = _ => MainTabWindow_BetterWork.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = FeaturesUiElements,
                Label = "UI Display",
                Type = SettingType.Header,
                Tooltip = "Work tab UI display elements.",
                HeaderColor = new Color(0.8f, 0.8f, 0.6f),
                ShowInSimpleView = true,
                SortOrder = -42
            });

            Register(new SettingDefinition
            {
                Id = PriorityHeader,
                ParentId = FeaturesUiElements,
                Label = "Priority Range",
                Tooltip = "Controls which mod owns the manual priority range.",
                Type = SettingType.Header,
                HeaderColor = new Color(0.8f, 0.7f, 0.45f),
                ShowInSimpleView = true,
                SortOrder = 42,
            });

            Register(new SettingDefinition
            {
                Id = PriorityModeSetting,
                ParentId = PriorityHeader,
                FieldName = "priorityMode",
                Label = "Priority mode",
                Tooltip = "Auto keeps vanilla priorities unless a compatible max-priority mod or existing high priorities are detected. BetterWorkTab makes BWT own the expanded range.",
                Type = SettingType.Enum,
                EnumType = typeof(PriorityMode),
                DefaultValue = DefaultSettings.priorityMode,
                ShowInSimpleView = true,
                SortOrder = 0,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.NormalizePrioritySettings();
                    }

                    PriorityAuthorityBroker.InvalidateCaches();
                    Patch_WorkPriority_DoCell_Unified.ClearColorCache();
                }
            });

            Register(new SettingDefinition
            {
                Id = UiAutoMaxPriority,
                ParentId = PriorityHeader,
                FieldName = "autoMaxPriorityInt",
                Label = "Auto max priority",
                Tooltip = "Maximum priority Auto mode is allowed to expose while following compatible external providers or preserving already-expanded priorities.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.autoMaxPriority,
                MinValue = PriorityConstants.VanillaMax,
                MaxValue = BetterWorkTabSettings.MAX_PRIORITY_HARD_LIMIT,
                VisibleWhen = s => ((BetterWorkTabSettings)s).priorityMode == PriorityMode.Auto,
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 1,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.NormalizePrioritySettings();
                    }

                    PriorityAuthorityBroker.InvalidateCaches();
                }
            });

            Register(new SettingDefinition
            {
                Id = UiMaxPriority,
                ParentId = PriorityHeader,
                FieldName = "maxPriorityInt",
                Label = "BWT max priority",
                Tooltip = "Maximum priority when Better Work Tab is selected as the priority owner.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.maxPriority,
                MinValue = BetterWorkTabSettings.MAX_PRIORITY_MINIMUM,
                MaxValue = BetterWorkTabSettings.MAX_PRIORITY_HARD_LIMIT,
                VisibleWhen = s => ((BetterWorkTabSettings)s).priorityMode == PriorityMode.BetterWorkTab,
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 2,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.NormalizePrioritySettings();
                    }

                    PriorityAuthorityBroker.InvalidateCaches();
                    Patch_WorkPriority_DoCell_Unified.ClearColorCache();
                }
            });

            Register(new SettingDefinition
            {
                Id = UiAutoDisabledPriorityMode,
                ParentId = PriorityHeader,
                FieldName = "autoDisabledPriorityMode",
                Label = "Disabled work click",
                Tooltip = "Priority Auto mode assigns when disabled work is turned back on.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.AutoDisabledPriorityMode),
                DefaultValue = DefaultSettings.autoDisabledPriorityMode,
                VisibleWhen = s => ((BetterWorkTabSettings)s).priorityMode == PriorityMode.Auto,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 3,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.NormalizePrioritySettings();
                    }
                }
            });

            Register(new SettingDefinition
            {
                Id = UiAutoDisabledPriorityFixedValue,
                ParentId = PriorityHeader,
                FieldName = "autoDisabledPriorityFixedValue",
                Label = "Fixed click priority",
                Tooltip = "Priority assigned when Auto disabled-work click behavior is fixed priority.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.autoDisabledPriorityFixedValue,
                MinValue = 1,
                MaxValue = BetterWorkTabSettings.MAX_PRIORITY_HARD_LIMIT,
                VisibleWhen = s =>
                {
                    var settings = (BetterWorkTabSettings)s;
                    return settings.priorityMode == PriorityMode.Auto &&
                           settings.autoDisabledPriorityMode == BetterWorkTabSettings.AutoDisabledPriorityMode.FixedPriority;
                },
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 4,
            });

            Register(new SettingDefinition
            {
                Id = UiPriorityColorPercentageGreen,
                ParentId = FeaturesUiElements,
                FieldName = "priorityColorPercentage_Green",
                Label = "Color Percentage - Green",
                Tooltip = "The percentage at which numbers will be green when displayed on the work tab.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.priorityColorPercentage_Green,
                MinValue = 1,
                MaxValue = 100,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 43,
            });

            Register(new SettingDefinition
            {
                Id = UiPriorityColorPercentageYellow,
                ParentId = FeaturesUiElements,
                FieldName = "priorityColorPercentage_Yellow",
                Label = "Color Percentage - Yellow",
                Tooltip = "The percentage at which numbers will be yellow when displayed on the work tab.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.priorityColorPercentage_Yellow,
                MinValue = 1,
                MaxValue = 100,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 44,
            });

            Register(new SettingDefinition
            {
                Id = UiPriorityColorPercentageTan,
                ParentId = FeaturesUiElements,
                FieldName = "priorityColorPercentage_Tan",
                Label = "Color Percentage - Tan",
                Tooltip = "The percentage at which numbers will be tan when displayed on the work tab.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.priorityColorPercentage_Tan,
                MinValue = 1,
                MaxValue = 100,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 45,
            });

            Register(new SettingDefinition
            {
                Id = FeaturesPerformance,
                FieldName = "enablePerformanceOptimizations",
                Label = "Performance",
                Tooltip = "Disabling may reduce performance on large colonies",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enablePerformanceOptimizations,
                ControlsChildVisibility = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                VisibleWhen = _ => false,
                SortOrder = -41,
                EmphasizeAsHeader = true,
                HeaderColor = new Color(0.6f, 0.8f, 0.8f)
            });

            Register(new SettingDefinition
            {
                Id = FeaturesMultiplayer,
                FieldName = "enableMultiplayerSync",
                Label = "Multiplayer Sync",
                Tooltip = "Enable multiplayer synchronization options.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableMultiplayerSync,
                ControlsChildVisibility = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
                VisibleWhen = _ => MP.enabled && MP.IsInMultiplayer,
#else
                VisibleWhen = _ => false,
#endif
                SortOrder = -40
            });

            // Highlights
            Register(new SettingDefinition
            {
                Id = HighlightsHover,
                ParentId = FeaturesHighlights,
                FieldName = "ShowCursorPawnAndWorktypeHighlight",
                Label = "Highlight on Hover",
                Tooltip = "Tint the row and column under your cursor.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ShowCursorPawnAndWorktypeHighlight,
                ShowInSimpleView = true,
                ControlsChildVisibility = true,
                SortOrder = 0
            });

            Register(new SettingDefinition
            {
                Id = "highlights.masterColor",
                ParentId = HighlightsHover,
                Label = "Set Master Highlight Color",
                Tooltip = "Select a color to apply to ALL highlight settings (Hover, Selected, etc).",
                Type = SettingType.Button,
                ShowInSimpleView = true,
                SortOrder = 0, 
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings s)
                    {
                         Find.WindowStack.Add(new Dialog_ColourPicker(s.Color_CursorHighlight, (picked, closing) =>
                         {
                             s.Color_CursorHighlight = picked;
                             s.Color_RowHoverHighlight = picked;
                             s.Color_ColumnHoverHighlight = picked;
                             s.Color_SelectedPawnHighlight = picked;
                             s.Color_FloatMenuHighlight = picked;
                             s.Color_CustomMouseHighlight = picked;
                             s.Color_CustomSimilarWorktypeHighlight = picked;
                             s.Write();
                             Messages.Message("Master color applied to all highlight settings.", MessageTypeDefOf.PositiveEvent, false);
                         }));
                    }
                }
            });

            Register(new SettingDefinition
            {
                Id = HighlightsHoverColor,
                ParentId = HighlightsHover,
                FieldName = "Color_CursorHighlight",
                Label = "Hover Highlight Color",
                Tooltip = "Color used to highlight the row and column when hovering over cells.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_CursorHighlight,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = HighlightsRowHoverColor,
                ParentId = HighlightsHover,
                FieldName = "Color_RowHoverHighlight",
                Label = "Row Hover Color",
                Tooltip = "Override color for row highlight on hover.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_RowHoverHighlight,
                ShowInSimpleView = false,
                SortOrder = 2
            });

            Register(new SettingDefinition
            {
                Id = HighlightsColumnHoverColor,
                ParentId = HighlightsHover,
                FieldName = "Color_ColumnHoverHighlight",
                Label = "Column Hover Color",
                Tooltip = "Override color for column highlight on hover.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_ColumnHoverHighlight,
                ShowInSimpleView = false,
                SortOrder = 3
            });

            Register(new SettingDefinition
            {
                Id = HighlightsResetRowHoverColor,
                ParentId = HighlightsHover,
                Label = "Reset Row Hover Color",
                Tooltip = "Reset row hover highlight to the general hover color.",
                Type = SettingType.Button,
                ShowInSimpleView = false,
                SortOrder = 4,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.useRowHoverOverride = false;
                        settings.Color_RowHoverHighlight = settings.Color_MouseHoverHighlight;
                        settings.Write();
                    }
                }
            });

            Register(new SettingDefinition
            {
                Id = HighlightsResetColumnHoverColor,
                ParentId = HighlightsHover,
                Label = "Reset Column Hover Color",
                Tooltip = "Reset column hover highlight to the general hover color.",
                Type = SettingType.Button,
                ShowInSimpleView = false,
                SortOrder = 5,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        settings.useColumnHoverOverride = false;
                        settings.Color_ColumnHoverHighlight = settings.Color_MouseHoverHighlight;
                        settings.Write();
                    }
                }
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSelected,
                ParentId = FeaturesHighlights,
                FieldName = "DoSelectedPawnHighlight",
                Label = "Highlight Selected Pawn",
                Tooltip = "Always highlight the currently selected pawn's row.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.DoSelectedPawnHighlight,
                ShowInSimpleView = true,
                SortOrder = 2
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSelectedColor,
                ParentId = HighlightsSelected,
                FieldName = "Color_SelectedPawnHighlight",
                Label = "Selected Pawn Color",
                Tooltip = "Color used for selected pawn highlight.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_SelectedPawnHighlight,
                ShowInSimpleView = false,
                SortOrder = 2_1
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSelectedOpacity,
                ParentId = HighlightsSelected,
                FieldName = "SelectedPawnHighlightOpacity",
                Label = "Selected Pawn Highlight Opacity",
                Tooltip = "Opacity for selected pawn highlight.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.SelectedPawnHighlightOpacity,
                MinValue = 0f,
                MaxValue = 1f,
                ShowInSimpleView = false,
                SortOrder = 2_2
            });

            Register(new SettingDefinition
            {
                Id = HighlightsFloatMenu,
                ParentId = FeaturesHighlights,
                FieldName = "ShowFloatMenuPawnAndWorktypeHighlight",
                Label = "Highlight when opened from Float Menu",
                Tooltip = "When a float menu opens the Work tab, highlight the related pawn/work type.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ShowFloatMenuPawnAndWorktypeHighlight,
                ShowInSimpleView = true,
                SortOrder = 3
            });

            Register(new SettingDefinition
            {
                Id = HighlightsFloatMenuColor,
                ParentId = HighlightsFloatMenu,
                FieldName = "Color_FloatMenuHighlight",
                Label = "Context Highlight Color",
                Tooltip = "Color used when a context menu is open.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_FloatMenuHighlight,
                ShowInSimpleView = false,
                SortOrder = 3_1
            });

            Register(new SettingDefinition
            {
                Id = HighlightsOutlineMode,
                ParentId = HighlightsHover,
                FieldName = "useOutlineHighlights",
                Label = "Use Outline Highlights",
                Tooltip = "Draw highlights as outlines instead of solid boxes.",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = true,
                SortOrder = 4 // Placed after colors in HighlightHover
            });

            Register(new SettingDefinition
            {
                Id = HighlightsDisableBestPawn,
                ParentId = FeaturesOverlay,
                FieldName = "disableBestPawnHighlight",
                Label = "Disable Best Pawn Highlight",
                Tooltip = "Disable the green highlight for the best pawn in a work type.",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 41
            });

            Register(new SettingDefinition
            {
                Id = HighlightsBestPawnBackground,
                ParentId = FeaturesOverlay,
                FieldName = "bestPawnHighlightThickness",
                Label = "Best Pawn Outline Thickness",
                Tooltip = "Adjust the thickness of the green outline for the best pawn in a work type.",
                Type = SettingType.Int,
                DefaultValue = 1,
                MinValue = 1f,
                MaxValue = 4f,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 42,
                VisibleWhen = s => !((BetterWorkTabSettings)s).disableBestPawnHighlight
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSimilar,
                ParentId = HighlightsHover,
                FieldName = "ShowSimilarWorktypeHighlight",
                Label = "Similar Worktypes",
                Tooltip = "Dimly highlight work types sharing relevant skills.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ShowSimilarWorktypeHighlight,
                ShowInSimpleView = false,
                SortOrder = 5
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSimilarColor,
                ParentId = HighlightsSimilar,
                FieldName = "Color_CustomSimilarWorktypeHighlight",
                Label = "Similar Worktype Color",
                Tooltip = "Color for similar worktype highlight.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_CustomSimilarWorktypeHighlight,
                ShowInSimpleView = false,
                SortOrder = 5_1
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSimilarOpacity,
                ParentId = HighlightsSimilar,
                FieldName = "SimilarWorktypeHighlightOpacity",
                Label = "Similar Highlight Opacity",
                Tooltip = "Opacity for similar worktype highlight.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.SimilarWorktypeHighlightOpacity,
                MinValue = 0f,
                MaxValue = 1f,
                ShowInSimpleView = false,
                SortOrder = 5_2
            });

            // Layout
            Register(new SettingDefinition
            {
                Id = LayoutCtrlDrag,
                FieldName = "requireCtrlForDrag",
                Label = "Require Ctrl for Dragging",
                Tooltip = "Hold Ctrl to drag rows/columns. Prevents accidental reordering.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.requireCtrlForDrag,
                ShowInSimpleView = true,
                SortOrder = 95,
                ParentId = FeaturesDragdrop
            });

            Register(new SettingDefinition
            {
                Id = DragdropEnableGrouping,
                ParentId = FeaturesDragdrop,
                FieldName = "enableColumnGrouping",
                Label = "Enable Column Grouping (Ctrl+Click)",
                Tooltip = "Allows selecting multiple columns with Ctrl+Click to drag them together.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableColumnGrouping,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 96
            });


            Register(new SettingDefinition
            {
                Id = LayoutDragRows,
                ParentId = FeaturesDragdrop,
                FieldName = "rowDraggingEnabled",
                Label = "Enable Row Dragging",
                Tooltip = "Allow dragging pawn rows to reorder.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.rowDraggingEnabled,
                ShowInSimpleView = false,
                SortOrder = 1011
            });

            Register(new SettingDefinition
            {
                Id = LayoutDragColumns,
                ParentId = FeaturesDragdrop,
                FieldName = "columnDraggingEnabled",
                Label = "Enable Column Dragging",
                Tooltip = "Allow dragging work type columns to reorder.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.columnDraggingEnabled,
                ShowInSimpleView = false,
                SortOrder = 1012
            });

            Register(new SettingDefinition
            {
                Id = LayoutDragThreshold,
                ParentId = FeaturesDragdrop,
                FieldName = "dragThreshold",
                Label = "Drag Threshold (px)",
                Tooltip = "Minimum mouse movement before a drag begins.",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.dragThreshold,
                MinValue = 1f,
                MaxValue = 20f,
                ShowInSimpleView = false,
                SortOrder = 1013
            });

            Register(new SettingDefinition
            {
                Id = LayoutDragColumnLineInset,
                ParentId = FeaturesDragdrop,
                FieldName = "columnInsertionLineInset",
                Label = "Column Drag Line Top Offset (px)",
                Tooltip = "Vertical offset measured up from the header bottom for the column insertion line. 0 = start at the content; increasing moves the line upward into the header (up to full header height).",
                Type = SettingType.Int,
                DefaultValue = DefaultSettings.columnInsertionLineInset,
                MinValue = 0f,
                MaxValue = 64f,
                ShowInSimpleView = false,
                SortOrder = 1014
            });

            Register(new SettingDefinition
            {
                Id = LayoutDragHoverDelay,
                ParentId = FeaturesAutoassign,
                FieldName = "dragHoverDelay",
                Label = "Rule Builder: Drag Hover Delay (s)",
                Tooltip = "Time in seconds to hover before switching categories while dragging rules.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.dragHoverDelay,
                MinValue = 0.2f,
                MaxValue = 2.0f,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 430
            });

            Register(new SettingDefinition
            {
                Id = LayoutClickClose,
                ParentId = AdvancedHeader,
                FieldName = "disableLeftClickClose",
                Label = "Keep Tab Open When Selecting Pawn",
                Tooltip = "Left-clicking a pawn jumps to and selects it but keeps the Work tab open instead of closing (also stops closing on map clicks).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.disableLeftClickClose,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 405
            });

            Register(new SettingDefinition
            {
                Id = LayoutCloseOnMapClick,
                FieldName = "closeOnMapClick",
                Label = "Close on Map Click",
                Tooltip = "Close the Work tab when clicking on the map.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.closeOnMapClick,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 1021,
                ParentId = FeaturesClicks
            });

            Register(new SettingDefinition
            {
                Id = LayoutContextMenu,
                FieldName = "enableContextMenuOnRightClick",
                Label = "Enable Right-Click Menu",
                Tooltip = "Enable context menu on right-clicking pawn names.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableContextMenuOnRightClick,
                ShowInSimpleView = true,
                SortOrder = 1022,
                ParentId = FeaturesClicks
            });

            Register(new SettingDefinition
            {
                Id = LayoutPawnCount,
                FieldName = "showPawnCountAtBottom",
                Label = "Show Colonist Count",
                Tooltip = "Display colonist count in the bottom-left corner.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showPawnCountAtBottom,
                ShowInSimpleView = true,
                SortOrder = 103,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = LayoutBedCount,
                FieldName = "showBedCountAtBottom",
                Label = "Show Bed Count",
                Tooltip = "Display bed count (red if insufficient).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showBedCountAtBottom,
                ShowInSimpleView = true,
                SortOrder = 104,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = UiPriorityLegend,
                FieldName = "showPriorityLegend",
                Label = "Show Priority Legend",
                Tooltip = "Display priority direction text above work columns.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showPriorityLegend,
                ShowInSimpleView = false,
                SortOrder = 1041,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = UiDragInstructions,
                FieldName = "showDragInstructions",
                Label = "Show Drag Instructions",
                Tooltip = "Show overlay instructions at the bottom of the tab.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showDragInstructions,
                ShowInSimpleView = true,
                SortOrder = 1042,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = UiManualPriorities,
                FieldName = "showManualPrioritiesCheckbox",
                Label = "Show Manual Priorities Checkbox",
                Tooltip = "Show the Manual Priorities checkbox in the header. Disabling hides the checkbox and prevents switching between checkmarks and manual priority numbers.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showManualPrioritiesCheckbox,
                ShowInSimpleView = true,
                SortOrder = 1043,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = "ui.autoEnableManualPriorities",
                FieldName = "autoEnableManualPriorities",
                Label = "Auto-Enable Manual Priorities",
                Tooltip = "Automatically check the Manual Priorities checkbox when opening the Work tab.",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = true,
                SortOrder = 1044,
                ParentId = FeaturesUiElements
            });

            Register(new SettingDefinition
            {
                Id = LayoutDividerHeight,
                FieldName = "dividerHeight",
                Label = "Divider Default Height",
                Tooltip = "Default height of divider rows in pixels.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.dividerHeight,
                MinValue = 10f,
                MaxValue = 50f,
                MinLabel = "Thin",
                MaxLabel = "Thick",
                ShowInSimpleView = true,
                SortOrder = 105,
                ParentId = FeaturesDividers
            });

            Register(new SettingDefinition
            {
                Id = LayoutDividerAlpha,
                FieldName = "dividerMinAlpha",
                Label = "Divider Minimum Opacity",
                Tooltip = "Minimum background opacity for dividers.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.dividerMinAlpha,
                MinValue = 0f,
                MaxValue = 1f,
                ShowInSimpleView = false,
                SortOrder = 106,
                ParentId = FeaturesDividers
            });

            Register(new SettingDefinition
            {
                Id = DividersShow,
                FieldName = "showDividers",
                Label = "Show Dividers",
                Tooltip = "Toggle visibility of divider rows.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showDividers,
                ShowInSimpleView = true,
                SortOrder = 107,
                ParentId = FeaturesDividers
            });

            Register(new SettingDefinition
            {
                Id = DividersCustomColors,
                FieldName = "allowCustomDividerColors",
                Label = "Allow Custom Colors",
                Tooltip = "Allow per-divider custom colors.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.allowCustomDividerColors,
                ShowInSimpleView = false,
                SortOrder = 108,
                ParentId = FeaturesDividers
            });

            Register(new SettingDefinition
            {
                Id = "dividers.highlight",
                ParentId = FeaturesDividers,
                FieldName = "highlightDividersOnHover",
                Label = "Highlight on Hover",
                Tooltip = "Highlight dividers when hovering over them using the hover highlight color.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = false,
                SortOrder = 109
            });

            Register(new SettingDefinition
            {
                Id = DividersLabels,
                FieldName = "showDividerLabels",
                Label = "Show Divider Labels",
                Tooltip = "Show divider labels by default.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showDividerLabels,
                ShowInSimpleView = true,
                SortOrder = 111,
                ParentId = FeaturesDividers
            });


            Register(new SettingDefinition
            {
                Id = DividersCollapse,
                FieldName = "allowDividerCollapse",
                Label = "Allow Collapse",
                Tooltip = "Allow dividers to collapse/expand.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.allowDividerCollapse,
                ShowInSimpleView = true,
                SortOrder = 112,
                ParentId = FeaturesDividers
            });

            Register(new SettingDefinition
            {
                Id = "dividers.resetHeight",
                Label = "Reset All Dividers Height",
                Tooltip = "Reset the height of all dividers to the default value.",
                Type = SettingType.Button,
                ShowInSimpleView = true,
                SortOrder = 113,
                ParentId = FeaturesDividers,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings && Current.Game?.GetComponent<GameComponent_BWTWorldSettings>() is GameComponent_BWTWorldSettings worldSettings)
                    {
                        if (worldSettings.ActiveDividers != null)
                        {
                            foreach (var div in worldSettings.ActiveDividers)
                            {
                                div.Height = settings.dividerHeight;
                            }
                            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                            Messages.Message("Dividers reset to default height.", MessageTypeDefOf.PositiveEvent, false);
                        }
                    }
                }
            });

            Register(new SettingDefinition
            {
                Id = LayoutResetColumns,
                Label = "Reset Columns to Vanilla",
                Tooltip = "Restore all work columns to their default order.",
                Type = SettingType.Button,
                ShowInSimpleView = true,
                SortOrder = 1015,
                ParentId = FeaturesDragdrop,
                OnChanged = _ => WorkColumnOrderManager.ResetToVanilla()
            });

            Register(new SettingDefinition
            {
                Id = ColumnsShowMovedIndicator,
                ParentId = FeaturesDragdrop,
                FieldName = "showColumnMovedMarker",
                Label = "Show Moved Indicator",
                Tooltip = "Show indicator on manually moved columns.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showColumnMovedMarker,
                ShowInSimpleView = true,
                SortOrder = 115
            });

            Register(new SettingDefinition
            {
                Id = ColumnsShowBaselineLine,
                ParentId = FeaturesDragdrop,
                FieldName = "showColumnBaselineLine",
                Label = "Show Baseline Line While Dragging",
                Tooltip = "Show a line at the column's vanilla position while dragging moved columns.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showColumnBaselineLine,
                ShowInSimpleView = true,
                SortOrder = 116
            });

            Register(new SettingDefinition
            {
                Id = "columns.showMovedColorTint",
                ParentId = FeaturesDragdrop,
                FieldName = "showMovedColumnColorTint",
                Label = "Color tint for reordered columns",
                Tooltip = "Apply a color tint to the text of manually reordered columns.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showMovedColumnColorTint,
                ShowInSimpleView = true,
                SortOrder = 117
            });

            Register(new SettingDefinition
            {
                Id = "columns.movedMarkerColor",
                ParentId = FeaturesDragdrop,
                FieldName = "movedMarkerColor",
                Label = "Moved Column Marker Color",
                Tooltip = "Color used for the moved column indicator (*) and yellow tint.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_MovedMarkerColor,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 118
            });

            Register(new SettingDefinition
            {
                Id = ColumnsResetWidths,
                ParentId = FeaturesDragdrop,
                Label = "Reset Column Widths",
                Tooltip = "Clear saved column widths.",
                Type = SettingType.Button,
                ShowInSimpleView = false,
                SortOrder = 1016,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings s)
                    {
                        s.storedColumnWidths.Clear();
                        s.Write();
                    }
                }
            });

            // Skill colors
            Register(new SettingDefinition
            {
                Id = OverlayHeader,
                Label = "Skill Colors",
                Type = SettingType.Header,
                Tooltip = "Skill overlay behaviors when using Shift and hover.",
                HeaderColor = new Color(0.8f, 0.8f, 0.6f),
                ShowInSimpleView = false,
                SortOrder = 104,
                ParentId = FeaturesOverlay
            });

            var settings = BetterWorkTabMod.Settings;
            if (settings != null)
            {
                // Dynamic settings generation: The DropdownListAdder lets users add hidden work types,
                // and for each hidden type, we create a removable button tag below.
                Register(new SettingDefinition
                {
                    Id = HideWorktypes,
                    ParentId = FeaturesUiElements,
                    FieldName = "hiddenWorktypes",
                    Label = "Hidden Work Types",
                    Tooltip = "Select work types to hide from the work tab. (Beta Testing Phase. Please reach out to discord with ideas for improving)",
                    Type = SettingType.DropdownListAdder,
                    DropdownOptionsProvider = () => DefDatabase<WorkTypeDef>.AllDefsListForReading
                        .Where(wt => !settings.hiddenWorktypes.Contains(wt.defName))
                        .Select(w => w.labelShort.CapitalizeFirst())
                        .OrderBy(l => l),
                    OnOptionAdded = (option) =>
                    {
                        var wt = DefDatabase<WorkTypeDef>.AllDefsListForReading.FirstOrDefault(w => w.labelShort.CapitalizeFirst() == option);
                        if (wt != null && !settings.hiddenWorktypes.Contains(wt.defName))
                        {
                            settings.hiddenWorktypes.Add(wt.defName);
                            settings.Write();
                            _initialized = false;
                            BetterWorkTabSettingsUI.NotifySettingsChanged();
                            EnsureInitialized();
                            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                        }
                    },
                    ShowInSimpleView = false,
                    ShowInAdvancedView = true,
                    SortOrder = 105
                });

                foreach (var hiddenDefName in settings.hiddenWorktypes)
                {
                    var wt = DefDatabase<WorkTypeDef>.GetNamedSilentFail(hiddenDefName);
                    if (wt == null) continue;

                    string localHiddenDefName = hiddenDefName;
                    Register(new SettingDefinition
                    {
                        Id = "hide.wt." + hiddenDefName,
                        ParentId = HideWorktypes,
                        Label = "  - " + wt.labelShort.CapitalizeFirst(),
                        Tooltip = "Click to unhide this work type.",
                        Type = SettingType.Button,
                        OnChanged = (s) =>
                        {
                            var settingsObj = (BetterWorkTabSettings)s;
                            settingsObj.hiddenWorktypes.Remove(localHiddenDefName);
                            settingsObj.Write();
                            _initialized = false;
                            BetterWorkTabSettingsUI.NotifySettingsChanged();
                            EnsureInitialized();
                            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                        },
                        ShowInSimpleView = false,
                        ShowInAdvancedView = true,
                        SortOrder = 106
                    });
                }
            }

            Register(new SettingDefinition
            {
                Id = OverlayNumbersMode,
                ParentId = FeaturesOverlay,
                FieldName = "ShowUIMode_ShowSmallSkillNumbers",
                Label = "Tiny Skill Numbers",
                Tooltip = "When to show the small skill numbers in cells.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.ShowUIMode),
                DefaultValue = BetterWorkTabSettings.ShowUIMode.Unshifted,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 0
            });

            Register(new SettingDefinition
            {
                Id = OverlayBestPawnMode,
                ParentId = FeaturesOverlay,
                FieldName = "ShowUIMode_ShowPawnForSkillSquare",
                Label = "Best Pawn Indicator",
                Tooltip = "When to highlight the pawn with highest skill.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.ShowUIMode),
                DefaultValue = BetterWorkTabSettings.ShowUIMode.Shifted,
                ShowInSimpleView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = OverlayHoverCellOverlay,
                ParentId = FeaturesOverlay,
                FieldName = "showHoverCellOverlay",
                Label = "Show Hover Cell Overlay",
                Tooltip = "Show skill/priority info when hovering a cell (Shifted/Unshifted per above).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showHoverCellOverlay,
                ShowInSimpleView = true,
                SortOrder = 2
            });

            Register(new SettingDefinition
            {
                Id = OverlayHoverMode,
                ParentId = OverlayHoverCellOverlay,
                FieldName = "skillViewHoverMode",
                Label = "Hover Behavior (Priority/Skill)",
                Tooltip = "Choose hover visuals: Skill focused (big skill number + small priority) or Priority focused (vanilla box + tiny skill).",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.SkillViewHoverMode),
                DefaultValue = DefaultSettings.skillViewHoverMode,
                ShowInSimpleView = true,
                SortOrder = 3
            });

            Register(new SettingDefinition
            {
                Id = OverlayHoverScope,
                ParentId = OverlayHoverCellOverlay,
                FieldName = "hoverEffectScope",
                Label = "Hover Effect Scope",
                Tooltip = "Controls whether hover overlays are shown only on the hovered cell or across the whole column.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.HoverEffectScope),
                DefaultValue = DefaultSettings.hoverEffectScope,
                ShowInSimpleView = false,
                SortOrder = 4
            });

            Register(new SettingDefinition
            {
                Id = ColorsSkillVeryLow,
                ParentId = OverlayHeader,
                FieldName = "Color_VeryLowSkill",
                Label = "Very Low Skill (0-3)",
                Tooltip = "Color for skills at level 0-3.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_VeryLowSkill,
                ShowInSimpleView = false,
                SortOrder = 301,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            Register(new SettingDefinition
            {
                Id = ColorsSkillLow,
                ParentId = OverlayHeader,
                FieldName = "Color_LowSkill",
                Label = "Low Skill (4-9)",
                Tooltip = "Color for skills at level 4-9.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_LowSkill,
                ShowInSimpleView = false,
                SortOrder = 302,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            Register(new SettingDefinition
            {
                Id = ColorsSkillGood,
                ParentId = OverlayHeader,
                FieldName = "Color_GoodLowSkill",
                Label = "Good Skill (10-15)",
                Tooltip = "Color for skills at level 10-15.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_GoodLowSkill,
                ShowInSimpleView = false,
                SortOrder = 303,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            Register(new SettingDefinition
            {
                Id = ColorsSkillExcellent,
                ParentId = OverlayHeader,
                FieldName = "Color_ExcellentSkill",
                Label = "Excellent Skill (16+)",
                Tooltip = "Color for skills at level 16+.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_ExcellentSkill,
                ShowInSimpleView = false,
                SortOrder = 304,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            Register(new SettingDefinition
            {
                Id = ColorsBestPawnOutline,
                ParentId = OverlayHeader,
                FieldName = "Color_BestPawnForSkillSquare",
                Label = "Best Pawn Outline",
                Tooltip = "Outline color for the best pawn indicator.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_BestPawnForSkillSquare,
                ShowInSimpleView = false,
                SortOrder = 305
            });

            // Advanced
            Register(new SettingDefinition
            {
                Id = AdvancedHeader,
                Label = "Advanced & Maintenance",
                Type = SettingType.Header,
                Tooltip = "Advanced toggles and maintenance/reset options.",
                HeaderColor = new Color(0.6f, 0.6f, 0.6f),
                ShowInSimpleView = true,
                SortOrder = 400
            });

            Register(new SettingDefinition
            {
                Id = LayoutWorkTabMaxHeight,
                ParentId = AdvancedHeader,
                FieldName = "workTabMaxHeight",
                Label = "Work Tab Max Height",
                Tooltip = "Caps the Work tab window height in pixels and scrolls extra rows. -1 keeps RimWorld's default full-screen-height behavior.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.workTabMaxHeight,
                MinValue = -1f,
                MaxValue = 1200f,
                MinLabel = "Default",
                MaxLabel = "1200px",
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 401,
                OnChanged = _ => MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged()
            });

            Register(new SettingDefinition
            {
                Id = AdvancedHideSettingResetIcons,
                ParentId = AdvancedHeader,
                FieldName = nameof(BetterWorkTabSettings.hideSettingResetIcons),
                Label = "Hide setting reset icons",
                Tooltip = "Hide the per-setting reset buttons shown beside settings that differ from their defaults.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.hideSettingResetIcons,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 402
            });

            // Auto-assign settings
            Register(new SettingDefinition
            {
                Id = AutoassignViewMode,
                ParentId = FeaturesAutoassign,
                FieldName = "rulesetViewMode",
                Label = "Ruleset Interface Mode",
                Tooltip = "Choose between the new visual builder (Regular), the classic list (Raw), or show both options.",
                Type = SettingType.Enum,
                DefaultValue = BetterWorkTabSettings.RulesetViewMode.Regular,
                EnumType = typeof(BetterWorkTabSettings.RulesetViewMode),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 405
            });

            Register(new SettingDefinition
            {
                Id = AutoassignConfirm,
                ParentId = FeaturesAutoassign,
                FieldName = "showAutoAssignConfirmation",
                Label = "Confirm on Apply",
                Tooltip = "Show confirmation before applying a ruleset.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showAutoAssignConfirmation,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 401
            });

            Register(new SettingDefinition
            {
                Id = AutoassignResetBefore,
                ParentId = FeaturesAutoassign,
                FieldName = "resetWorkBeforeAutoAssign",
                Label = "Reset Before Apply",
                Tooltip = "Clear assignments before applying a ruleset.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.resetWorkBeforeAutoAssign,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 402
            });

            Register(new SettingDefinition
            {
                Id = AutoassignVisual,
                ParentId = FeaturesAutoassign,
                FieldName = "showAutoAssignVisualFeedback",
                Label = "Visual Feedback",
                Tooltip = "Highlight affected pawns/work types after applying.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showAutoAssignVisualFeedback,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 403
            });

            Register(new SettingDefinition
            {
                Id = WorkloadsPersistDividers,
                ParentId = FeaturesWorkloads,
                FieldName = "persistDividersInWorkloads",
                Label = "Include Dividers",
                Tooltip = "Include divider positions when saving workloads.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.persistDividersInWorkloads,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 4025
            });

            Register(new SettingDefinition
            {
                Id = AdvancedHideAutoAssignBtn,
                ParentId = FeaturesAutoassign,
                FieldName = "hideAutoAssignButton",
                Label = "Hide Auto-Assign Button",
                Tooltip = "Hide the ruleset button (still accessible via Manager).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.hideAutoAssignButton,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 404
            });
            Register(new SettingDefinition
            {
                Id = AdvancedAlwaysShowConditionEditors,
                ParentId = FeaturesAutoassign,
                FieldName = nameof(BetterWorkTabSettings.alwaysShowConditionEditors),
                Label = "BWT_Settings_autoAssign.alwaysShowConditionEditors".Translate(),
                Tooltip = "BWT_Settings_autoAssign.alwaysShowConditionEditors_Tooltip".Translate(),
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.alwaysShowConditionEditors,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 420 // After persistDividersInWorkloads (4025), before perf toggles
            });

            // Workloads are above; Performance toggles
            Register(new SettingDefinition
            {
                Id = PerfCacheBedCounts,
                ParentId = FeaturesPerformance,
                FieldName = "cacheBedCounts",
                Label = "Cache Bed Counts",
                Tooltip = "Cache bed counts to reduce stutter.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 405
            });

            Register(new SettingDefinition
            {
                Id = PerfCacheSkillLevels,
                ParentId = FeaturesPerformance,
                FieldName = "cacheSkillLevels",
                Label = "Cache Skill Levels",
                Tooltip = "Cache skill calculations to reduce redundant work.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 406
            });

            Register(new SettingDefinition
            {
                Id = PerfCacheRowDescriptors,
                ParentId = FeaturesPerformance,
                FieldName = "cacheRowDescriptors",
                Label = "Cache Row Descriptors",
                Tooltip = "Cache row descriptor calculations for layout.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 407
            });

            Register(new SettingDefinition
            {
                Id = PerfCacheIncapability,
                ParentId = FeaturesPerformance,
                FieldName = "cacheIncapabilityChecks",
                Label = "Cache Incapability Checks",
                Tooltip = "Cache incapability checks for work types.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 408
            });

            Register(new SettingDefinition
            {
                Id = PerfUseElementPooling,
                ParentId = FeaturesPerformance,
                FieldName = "useElementPooling",
                Label = "Use Element Pooling",
                Tooltip = "Reuse UI elements instead of allocating each frame.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 409
            });

            Register(new SettingDefinition
            {
                Id = PerfViewportCulling,
                ParentId = FeaturesPerformance,
                FieldName = "viewportCulling",
                Label = "Viewport Culling",
                Tooltip = "Only render visible rows in the scroll area.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 410
            });

            Register(new SettingDefinition
            {
                Id = AdvancedScrollWheelPriority,
                ParentId = AdvancedHeader,
                FieldName = "enableScrollWheelPriority",
                Label = "Enable Scroll Wheel Priority",
                Tooltip = "Allows you to change a pawn's work priority by scrolling the mouse wheel while hovering over a work cell.",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 490
            });

            // Master toggle for debug logging. When false, no BWT debug messages (except errors) will fire.
            Register(new SettingDefinition
            {
                Id = AdvancedDebugLogging,
                ParentId = AdvancedHeader,
                FieldName = "enableDebugLogging",
                Label = "Enable Debug Logging",
                Tooltip = "Output detailed debug messages to the log.",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                ControlsChildVisibility = true,
                SortOrder = 500
            });

            if (settings != null)
            {
                // Dynamic list of debug features. Uses DropdownListAdder to let users pick specific sub-systems to log.
                Register(new SettingDefinition
                {
                    Id = "debug.features",
                    ParentId = AdvancedDebugLogging,
                    Label = "Debug Features",
                    Tooltip = "Select which debug features to enable logging for.",
                    Type = SettingType.DropdownListAdder,
                    DropdownOptionsProvider = () => Enum.GetValues(typeof(DebugFeature))
                        .Cast<DebugFeature>()
                        .Where(f => !settings.debugFeatureToggles.ContainsKey(f) || !settings.debugFeatureToggles[f])
                        .Select(f => f.ToString())
                        .OrderBy(l => l),
                    OnOptionAdded = (option) =>
                    {
                        if (Enum.TryParse<DebugFeature>(option, out var feature))
                        {
                            settings.debugFeatureToggles[feature] = true;
                            settings.Write();
                            _initialized = false;
                            BetterWorkTabSettingsUI.NotifySettingsChanged();
                            EnsureInitialized();
                        }
                    },
                    ShowInSimpleView = false,
                    ShowInAdvancedView = false,
                    SortOrder = 501
                });

                // Display each currently enabled debug feature as a removable button tag.
                var enabledFeatures = settings.debugFeatureToggles.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
                foreach (var feature in enabledFeatures)
                {
                    var localFeature = feature;
                    Register(new SettingDefinition
                    {
                        Id = "debug.feature." + feature.ToString(),
                        ParentId = "debug.features",
                        Label = "  - " + feature.ToString(),
                        Tooltip = "Click to disable logging for this feature.",
                        Type = SettingType.Button,
                        OnChanged = (s) =>
                        {
                            var settingsObj = (BetterWorkTabSettings)s;
                            settingsObj.debugFeatureToggles[localFeature] = false;
                            settingsObj.Write();
                            _initialized = false;
                            BetterWorkTabSettingsUI.NotifySettingsChanged();
                            EnsureInitialized();
                        },
                        ShowInSimpleView = false,
                        ShowInAdvancedView = false,
                        SortOrder = 502
                    });
                }
            }

            // Controls whether BWT tracks performance metrics (visible via debug commands).
            Register(new SettingDefinition
            {
                Id = AdvancedProfiler,
                ParentId = AdvancedHeader,
                FieldName = "enableProfiler",
                Label = "Enable Profiler",
                Tooltip = "Enable in-game profiler (1 to report, Shift+1 to clear).",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 503
            });

            // If enabled, debug messages are mirrored to a dedicated .txt file in the mod folder.
            Register(new SettingDefinition
            {
                Id = AdvancedLogToFile,
                ParentId = AdvancedHeader,
                FieldName = "logDebugToFile",
                Label = "Log to File",
                Tooltip = "Write debug logs to file in addition to console.",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 504
            });

            // Multiplayer sync
            Register(new SettingDefinition
            {
                Id = MpSyncColumnOrder,
                ParentId = FeaturesMultiplayer,
                FieldName = "mpSyncColumnOrder",
                Label = "Sync Column Order",
                Tooltip = "Synchronize column order across multiplayer.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 411
            });

            Register(new SettingDefinition
            {
                Id = MpSyncWorkloads,
                ParentId = FeaturesMultiplayer,
                FieldName = "mpSyncWorkloads",
                Label = "Sync Workloads",
                Tooltip = "Synchronize workload save/load.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 412
            });

            Register(new SettingDefinition
            {
                Id = MpSyncRulesets,
                ParentId = FeaturesMultiplayer,
                FieldName = "mpSyncRulesets",
                Label = "Sync Rulesets",
                Tooltip = "Synchronize ruleset applications.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 413
            });

            Register(new SettingDefinition
            {
                Id = MpConflictMode,
                ParentId = FeaturesMultiplayer,
                FieldName = "mpConflictMode",
                Label = "Conflict Mode",
                Tooltip = "How to resolve multiplayer conflicts.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.MpConflictMode),
                DefaultValue = BetterWorkTabSettings.MpConflictMode.PlayerPriority,
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 414
            });

            Register(new SettingDefinition
            {
                Id = MpShowOtherPlayersHover,
                ParentId = FeaturesMultiplayer,
                FieldName = nameof(BetterWorkTabSettings.mpShowOtherPlayersHover),
                Label = "Show other players' hovered cell",
                Tooltip = "Render hover indicators shared by other players.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.mpShowOtherPlayersHover,
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
                VisibleWhen = _ => MP.enabled && MP.IsInMultiplayer,
#else
                VisibleWhen = _ => false,
#endif
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 415
            });

            Register(new SettingDefinition
            {
                Id = MpAllowPresenceBroadcast,
                ParentId = FeaturesMultiplayer,
                FieldName = nameof(BetterWorkTabSettings.mpAllowPresenceBroadcast),
                Label = "Broadcast my hovered cell",
                Tooltip = "Share the hovered cell you are looking at with your peers.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.mpAllowPresenceBroadcast,
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
                VisibleWhen = _ => MP.enabled && MP.IsInMultiplayer,
#else
                VisibleWhen = _ => false,
#endif
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 416
            });

            Register(new SettingDefinition
            {
                Id = MpAllowOthersToRequestLayout,
                ParentId = FeaturesMultiplayer,
                FieldName = nameof(BetterWorkTabSettings.mpAllowOthersToRequestLayout),
                Label = "Allow layout requests",
                Tooltip = "Permit other players to request snapshots of your layout.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.mpAllowOthersToRequestLayout,
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
                VisibleWhen = _ => MP.enabled && MP.IsInMultiplayer,
#else
                VisibleWhen = _ => false,
#endif
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 417
            });

            Register(new SettingDefinition
            {
                Id = MpShowLinkedIndicator,
                ParentId = FeaturesMultiplayer,
                FieldName = nameof(BetterWorkTabSettings.mpShowLinkedIndicator),
                Label = "Show linked indicator",
                Tooltip = "Display a linked/peered indicator when viewing another player's layout.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.mpShowLinkedIndicator,
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
                VisibleWhen = _ => MP.enabled && MP.IsInMultiplayer,
#else
                VisibleWhen = _ => false,
#endif
                ShowInSimpleView = false,
                ShowInAdvancedView = false,
                SortOrder = 418
            });

            Register(new SettingDefinition
            {
                Id = HeadersHeader,
                Label = "Headers",
                Type = SettingType.Header,
                Tooltip = "Angled header settings.",
                HeaderColor = new Color(0.7f, 0.7f, 0.9f),
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 350
            });

            Register(new SettingDefinition
            {
                Id = HeadersAngled,
                ParentId = HeadersHeader,
                FieldName = "enableAngledHeaders",
                Label = "Angled headers",
                Tooltip = "Toggle angled column headers. Disabling allows vanilla headers to work. Note: Vanilla headers will not look right with drag and drop. The yellow indicator for out-of-place columns won't work with vanilla headers.",
                Type = SettingType.Bool,
                DefaultValue = true,
                ControlsChildVisibility = true,
                OnChanged = s => MainTabWindow_BetterWork.NotifyAngledHeadersChanged(),
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 506
            });

            Register(new SettingDefinition
            {
                Id = DragdropRemoveHeaderUnderline,
                ParentId = HeadersAngled,
                FieldName = "removeHeaderUnderline",
                Label = "Hide Header Underline",
                Tooltip = "Remove the underline from work tab header labels.",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 5061 // Immediately after HeadersAngled
            });

            Register(new SettingDefinition
            {
                Id = HeadersAngleRotation,
                ParentId = HeadersAngled,
                FieldName = "angledHeaderRotation",
                Label = "Angle rotation",
                Tooltip = "Rotate the angled headers (-90 to 90 degrees). Snaps to 5-degree increments.",
                Type = SettingType.Int,
                DefaultValue = -60,
                MinValue = -90f,
                MaxValue = 90f,
                OnChanged = s => 
                {
                    var bSettings = (BetterWorkTabSettings)s;
                    bSettings.angledHeaderRotation = Mathf.RoundToInt(bSettings.angledHeaderRotation / 5f) * 5;
                    MainTabWindow_BetterWork.NotifyAngledHeadersChanged();
                },
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 507
            });

            Register(new SettingDefinition
            {
                Id = "headers.useVerticalStackingForCJK",
                ParentId = HeadersAngled,
                FieldName = "useVerticalStackingForCJK",
                Label = "Vertical stacking for CJK",
                Tooltip = "Draw East Asian characters (Korean, Chinese, Japanese) vertically when angled headers are enabled. This is much more legible than rotated text.",
                Type = SettingType.Bool,
                DefaultValue = true,
                OnChanged = s => MainTabWindow_BetterWork.NotifyAngledHeadersChanged(),
                ShowInSimpleView = true,
                ShowInAdvancedView = true,
                SortOrder = 5072
            });

            Register(new SettingDefinition
            {
                Id = "headers.cjkVerticalKerning",
                ParentId = "headers.useVerticalStackingForCJK", // Nest under the toggle
                FieldName = "cjkVerticalKerning",
                Label = "CJK vertical kerning",
                Tooltip = "Adjust the vertical spacing between characters in Asian vertical stacking. Lower values mean tighter spacing.",
                Type = SettingType.Float,
                DefaultValue = 0.75f,
                MinValue = 0.5f,
                MaxValue = 1.5f,
                OnChanged = s => MainTabWindow_BetterWork.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 5073
            });

            Register(new SettingDefinition
            {
                Id = "headers.angledColor",
                ParentId = HeadersAngled,
                FieldName = "angledHeaderColor",
                Label = "Header text color",
                Tooltip = "Custom color for the angled header text.",
                Type = SettingType.Color,
                DefaultValue = Color.white,
                OnChanged = s => MainTabWindow_BetterWork.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 5071
            });

            Register(new SettingDefinition
            {
                Id = "headers.horizontalOffset",
                ParentId = HeadersAngled,
                FieldName = "angledHeaderHorizontalOffset",
                Label = "Horizontal offset",
                Tooltip = "Adjust the horizontal position of the angled headers. 0 = centered, 10 = Default. (Automatically forced to 0 at -90Â° for perfect alignment).",
                Type = SettingType.NumericInt,
                DefaultValue = 10,
                MinValue = -100f,
                MaxValue = 100f,
                OnChanged = s => MainTabWindow_BetterWork.NotifyAngledHeadersChanged(),
                ShowInSimpleView = false,
                ShowInAdvancedView = true,
                SortOrder = 508
            });

            Register(new SettingDefinition
            {
                Id = AdvancedRestoreDefaults,
                Label = "Restore Factory Defaults",
                Tooltip = "Reset all settings to default values.",
                Type = SettingType.Button,
                ShowInSimpleView = true,
                SortOrder = 420,
                OnChanged = settingsObj =>
                {
                    if (settingsObj is BetterWorkTabSettings settings)
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Are you sure you want to restore factory defaults? current settings will be lost.", () =>
                        {
                            settings.RestoreDefaults();
                            settings.Write();
                            Messages.Message("Factory defaults restored.", MessageTypeDefOf.PositiveEvent, false);
                        }, true, "Confirm Restore"));
                    }
                }
            });
        }
    }
}
