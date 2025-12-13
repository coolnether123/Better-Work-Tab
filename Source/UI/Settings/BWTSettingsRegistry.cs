using System.Collections.Generic;
using Better_Work_Tab;
using Better_Work_Tab.Features;
using Better_Work_Tab.Patches;
using Spine.UI.SettingsFramework;
using UnityEngine;
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

            // Feature master toggles (Simple view anchors)
            Register(new SettingDefinition
            {
                Id = FeaturesHeader,
                Label = "Features",
                Type = SettingType.Header,
                Tooltip = "Master feature toggles.",
                HeaderColor = new Color(0.55f, 0.75f, 0.95f),
                ShowInSimpleView = true,
                SortOrder = -50
            });

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
                SortOrder = -49
            });

            Register(new SettingDefinition
            {
                Id = FeaturesDragdrop,
                FieldName = "enableDragDropReordering",
                Label = "Drag & Drop Reordering",
                Tooltip = "Allow dragging rows/columns to reorder.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableDragDropReordering,
                ControlsChildVisibility = true,
                ShowInSimpleView = true,
                SortOrder = -48
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
                SortOrder = -47
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
                SortOrder = -46
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
                SortOrder = -45
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
                SortOrder = -44
            });

            Register(new SettingDefinition
            {
                Id = FeaturesUiElements,
                Label = "UI Display",
                Type = SettingType.Header,
                Tooltip = "Work tab UI display elements.",
                HeaderColor = new Color(0.8f, 0.8f, 0.6f),
                ShowInSimpleView = true,
                SortOrder = -43
            });


            Register(new SettingDefinition
            {
                Id = FeaturesPerformance,
                FieldName = "enablePerformanceOptimizations",
                Label = "Performance",
                Tooltip = "Enable caching and optimizations.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enablePerformanceOptimizations,
                ControlsChildVisibility = true,
                ShowInSimpleView = false,
                SortOrder = -41
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
                SortOrder = -40
            });

            // Highlights
            Register(new SettingDefinition
            {
                Id = HighlightsHeader,
                Label = "Highlights",
                Type = SettingType.Header,
                Tooltip = "Row and column highlighting behavior.",
                HeaderColor = new Color(0.4f, 0.6f, 0.9f),
                ShowInSimpleView = true,
                SortOrder = 0,
                ParentId = FeaturesHighlights
            });

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
                Id = HighlightsHoverColor,
                ParentId = HighlightsHover,
                FieldName = "Color_CursorHighlight",
                Label = "Hover Highlight Color",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_CursorHighlight,
                ShowInSimpleView = true,
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
                Label = "Selected Highlight Opacity",
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
                ParentId = FeaturesHighlights,
                FieldName = "useOutlineHighlights",
                Label = "Use Outline Highlights",
                Tooltip = "Draw highlights as outlines instead of solid boxes.",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = true,
                SortOrder = 4
            });

            Register(new SettingDefinition
            {
                Id = HighlightsSimilar,
                ParentId = FeaturesHighlights,
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
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.dragThreshold,
                MinValue = 1f,
                MaxValue = 20f,
                ShowInSimpleView = false,
                SortOrder = 1013
            });

            Register(new SettingDefinition
            {
                Id = LayoutClickClose,
                FieldName = "disableLeftClickClose",
                Label = "Keep Tab Open When Selecting Pawn",
                Tooltip = "Left-clicking a pawn jumps to and selects it but keeps the Work tab open instead of closing (also stops closing on map clicks).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.disableLeftClickClose,
                ShowInSimpleView = true,
                SortOrder = 102,
                ParentId = FeaturesClicks
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
                Id = DividersHighlight,
                FieldName = "drawDividerHighlight",
                Label = "Highlight Dividers",
                Tooltip = "Highlight divider rows when hovering.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.drawDividerHighlight,
                ShowInSimpleView = false,
                SortOrder = 108,
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
                SortOrder = 109,
                ParentId = FeaturesDividers
            });

            Register(new SettingDefinition
            {
                Id = DividersCustomFonts,
                FieldName = "allowCustomDividerFonts",
                Label = "Allow Custom Fonts",
                Tooltip = "Allow per-divider custom fonts/sizes.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.allowCustomDividerFonts,
                ShowInSimpleView = false,
                SortOrder = 110,
                ParentId = FeaturesDividers
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
                Id = LayoutResetColumns,
                Label = "Reset Columns to Vanilla",
                Tooltip = "Restore all work columns to their default order.",
                Type = SettingType.Button,
                ShowInSimpleView = true,
                SortOrder = 110,
                ParentId = FeaturesDragdrop,
                OnChanged = _ => WorkColumnOrderManager.ResetToVanilla()
            });

            Register(new SettingDefinition
            {
                Id = ColumnsHeader,
                ParentId = FeaturesDragdrop,
                Label = "Column Management",
                Tooltip = "Column order and width persistence.",
                Type = SettingType.Header,
                ShowInSimpleView = false,
                SortOrder = 111
            });

            Register(new SettingDefinition
            {
                Id = ColumnsEnable,
                ParentId = ColumnsHeader,
                FieldName = "enableColumnOrderSaving",
                Label = "Enable Column Saving",
                Tooltip = "Enable saving custom column order/widths.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableColumnOrderSaving,
                ControlsChildVisibility = true,
                ShowInSimpleView = false,
                SortOrder = 112
            });

            Register(new SettingDefinition
            {
                Id = ColumnsSaveOrder,
                ParentId = ColumnsEnable,
                FieldName = "persistColumnOrder",
                Label = "Save Column Order",
                Tooltip = "Remember custom column order between sessions.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.persistColumnOrder,
                ShowInSimpleView = false,
                SortOrder = 113
            });

            Register(new SettingDefinition
            {
                Id = ColumnsSaveWidths,
                ParentId = ColumnsEnable,
                FieldName = "persistColumnWidths",
                Label = "Save Column Widths",
                Tooltip = "Remember custom column widths between sessions.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.persistColumnWidths,
                ShowInSimpleView = false,
                SortOrder = 114
            });

            Register(new SettingDefinition
            {
                Id = ColumnsShowMovedIndicator,
                ParentId = ColumnsEnable,
                FieldName = "showColumnMovedMarker",
                Label = "Show Moved Indicator",
                Tooltip = "Show indicator on manually moved columns.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showColumnMovedMarker,
                ShowInSimpleView = false,
                SortOrder = 115
            });

            Register(new SettingDefinition
            {
                Id = ColumnsResetWidths,
                ParentId = ColumnsEnable,
                Label = "Reset Column Widths",
                Tooltip = "Clear saved column widths.",
                Type = SettingType.Button,
                ShowInSimpleView = false,
                SortOrder = 116,
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
                HeaderColor = new Color(0.9f, 0.7f, 0.4f),
                ShowInSimpleView = false,
                SortOrder = 200,
                ParentId = FeaturesOverlay
            });

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
                Label = "Hover Behavior (Skill/priority focus)",
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

            // Auto-assign settings
            Register(new SettingDefinition
            {
                Id = AutoassignConfirm,
                ParentId = FeaturesAutoassign,
                FieldName = "showAutoAssignConfirmation",
                Label = "Confirm on Apply",
                Tooltip = "Show confirmation before applying a ruleset.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showAutoAssignConfirmation,
                ShowInSimpleView = true,
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
                ShowInSimpleView = true,
                SortOrder = 403
            });

            Register(new SettingDefinition
            {
                Id = WorkloadsAutoSave,
                ParentId = FeaturesWorkloads,
                FieldName = "autoSaveCurrentWorkload",
                Label = "Auto-Save Current",
                Tooltip = "Automatically update the current workload with changes.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.autoSaveCurrentWorkload,
                ShowInSimpleView = false,
                SortOrder = 4023
            });

            Register(new SettingDefinition
            {
                Id = WorkloadsConfirmOnLoad,
                ParentId = FeaturesWorkloads,
                FieldName = "confirmWorkloadLoad",
                Label = "Confirm on Load",
                Tooltip = "Show confirmation before loading a workload.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.confirmWorkloadLoad,
                ShowInSimpleView = true,
                SortOrder = 4024
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
                ShowInSimpleView = true,
                SortOrder = 404
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
                SortOrder = 410
            });

            Register(new SettingDefinition
            {
                Id = AdvancedDebugLogging,
                FieldName = "enableDebugLogging",
                Label = "Enable Debug Logging",
                Tooltip = "Output detailed debug messages to the log.",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = false,
                SortOrder = 500
            });

            Register(new SettingDefinition
            {
                Id = AdvancedProfiler,
                FieldName = "enableProfiler",
                Label = "Enable Profiler",
                Tooltip = "Enable in-game profiler (1 to report, Shift+1 to clear).",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = false,
                SortOrder = 411
            });

            Register(new SettingDefinition
            {
                Id = AdvancedLogToFile,
                FieldName = "logDebugToFile",
                Label = "Log to File",
                Tooltip = "Write debug logs to file in addition to console.",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = false,
                SortOrder = 412
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
                        settings.RestoreDefaults();
                        settings.Write();
                    }
                }
            });
        }
    }
}
