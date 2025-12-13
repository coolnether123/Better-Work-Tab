using System.Collections.Generic;
using Better_Work_Tab;
using Better_Work_Tab.Features;
using Better_Work_Tab.Patches;
using Spine.UI.SettingsFramework;
using UnityEngine;

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

            // Highlights
            Register(new SettingDefinition
            {
                Id = "highlights.header",
                Label = "Highlights",
                Type = SettingType.Header,
                Tooltip = "Row and column highlighting behavior.",
                HeaderColor = new Color(0.4f, 0.6f, 0.9f),
                ShowInSimpleView = true,
                SortOrder = 0
            });

            Register(new SettingDefinition
            {
                Id = "highlights.master",
                FieldName = "ShowPawnAndWorktypeHighlights",
                Label = "Enable All Highlights",
                Tooltip = "Master switch for all row and column highlighting.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ShowPawnAndWorktypeHighlights,
                ShowInSimpleView = true,
                ControlsChildVisibility = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = "highlights.hover",
                ParentId = "highlights.master",
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
                Id = "highlights.hover.color",
                ParentId = "highlights.hover",
                FieldName = "Color_CursorHighlight",
                Label = "Hover Highlight Color",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_CursorHighlight,
                ShowInSimpleView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = "highlights.rowHoverColor",
                ParentId = "highlights.hover",
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
                Id = "highlights.columnHoverColor",
                ParentId = "highlights.hover",
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
                Id = "highlights.resetRowHoverColor",
                ParentId = "highlights.hover",
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
                Id = "highlights.resetColumnHoverColor",
                ParentId = "highlights.hover",
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
                Id = "highlights.selected",
                ParentId = "highlights.master",
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
                Id = "highlights.floatMenu",
                ParentId = "highlights.master",
                FieldName = "ShowFloatMenuPawnAndWorktypeHighlight",
                Label = "Highlight Context Source",
                Tooltip = "Highlight row/column when right-click menu is open.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.ShowFloatMenuPawnAndWorktypeHighlight,
                ShowInSimpleView = true,
                SortOrder = 3
            });

            Register(new SettingDefinition
            {
                Id = "highlights.outlineMode",
                ParentId = "highlights.master",
                FieldName = "useOutlineHighlights",
                Label = "Use Outline Highlights",
                Tooltip = "Draw highlights as outlines instead of solid boxes.",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = true,
                SortOrder = 4
            });

            // Layout
            Register(new SettingDefinition
            {
                Id = "layout.header",
                Label = "Layout & Behavior",
                Type = SettingType.Header,
                Tooltip = "Drag-drop, counters, dividers, and general layout behaviors.",
                HeaderColor = new Color(0.5f, 0.8f, 0.5f),
                ShowInSimpleView = true,
                SortOrder = 100
            });

            Register(new SettingDefinition
            {
                Id = "layout.ctrlDrag",
                FieldName = "requireCtrlForDrag",
                Label = "Require Ctrl for Dragging",
                Tooltip = "Hold Ctrl to drag rows/columns. Prevents accidental reordering.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.requireCtrlForDrag,
                ShowInSimpleView = true,
                SortOrder = 101
            });

            Register(new SettingDefinition
            {
                Id = "layout.clickClose",
                FieldName = "disableLeftClickClose",
                Label = "Keep Tab Open When Selecting Pawn",
                Tooltip = "Left-clicking a pawn jumps to and selects it but keeps the Work tab open instead of closing (also stops closing on map clicks).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.disableLeftClickClose,
                ShowInSimpleView = true,
                SortOrder = 102
            });

            Register(new SettingDefinition
            {
                Id = "layout.pawnCount",
                FieldName = "showPawnCountAtBottom",
                Label = "Show Colonist Count",
                Tooltip = "Display colonist count in the bottom-left corner.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showPawnCountAtBottom,
                ShowInSimpleView = true,
                SortOrder = 103
            });

            Register(new SettingDefinition
            {
                Id = "layout.bedCount",
                FieldName = "showBedCountAtBottom",
                Label = "Show Bed Count",
                Tooltip = "Display bed count (red if insufficient).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showBedCountAtBottom,
                ShowInSimpleView = true,
                SortOrder = 104
            });

            Register(new SettingDefinition
            {
                Id = "layout.dividerHeight",
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
                SortOrder = 105
            });

            Register(new SettingDefinition
            {
                Id = "layout.dividerAlpha",
                FieldName = "dividerMinAlpha",
                Label = "Divider Minimum Opacity",
                Tooltip = "Minimum background opacity for dividers.",
                Type = SettingType.Float,
                DefaultValue = DefaultSettings.dividerMinAlpha,
                MinValue = 0f,
                MaxValue = 1f,
                ShowInSimpleView = true,
                SortOrder = 106
            });

            Register(new SettingDefinition
            {
                Id = "layout.resetColumns",
                Label = "Reset Columns to Vanilla",
                Tooltip = "Restore all work columns to their default order.",
                Type = SettingType.Button,
                ShowInSimpleView = true,
                SortOrder = 110,
                OnChanged = _ => WorkColumnOrderManager.ResetToVanilla()
            });

            // Skill view
            Register(new SettingDefinition
            {
                Id = "overlay.header",
                Label = "Skill View",
                Type = SettingType.Header,
                Tooltip = "Skill overlay behaviors when using Shift and hover.",
                HeaderColor = new Color(0.9f, 0.7f, 0.4f),
                ShowInSimpleView = true,
                SortOrder = 200
            });

            Register(new SettingDefinition
            {
                Id = "overlay.enable",
                FieldName = "enableSkillOverlayFeature",
                Label = "Enable Skill Overlay",
                Tooltip = "Show skill information when holding Shift in the Work tab.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableSkillOverlayFeature,
                ShowInSimpleView = true,
                ControlsChildVisibility = true,
                SortOrder = 201
            });

            Register(new SettingDefinition
            {
                Id = "overlay.numbersMode",
                ParentId = "overlay.enable",
                FieldName = "ShowUIMode_ShowSmallSkillNumbers",
                Label = "Skill Numbers Display",
                Tooltip = "When to show small skill numbers in cells.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.ShowUIMode),
                DefaultValue = BetterWorkTabSettings.ShowUIMode.Unshifted,
                ShowInSimpleView = true,
                SortOrder = 0
            });

            Register(new SettingDefinition
            {
                Id = "overlay.bestPawnMode",
                ParentId = "overlay.enable",
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
                Id = "overlay.hoverCellOverlay",
                ParentId = "overlay.enable",
                FieldName = "showHoverCellOverlay",
                Label = "Show Hover Cell Overlay",
                Tooltip = "When holding Shift, show skill/priority info when hovering a cell.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.showHoverCellOverlay,
                ShowInSimpleView = true,
                SortOrder = 2
            });

            Register(new SettingDefinition
            {
                Id = "overlay.hoverMode",
                ParentId = "overlay.enable",
                FieldName = "skillViewHoverMode",
                Label = "Hover Behavior",
                Tooltip = "Determines what happens when hovering over a cell while holding Shift.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.SkillViewHoverMode),
                DefaultValue = DefaultSettings.skillViewHoverMode,
                ShowInSimpleView = true,
                SortOrder = 3
            });

            Register(new SettingDefinition
            {
                Id = "overlay.hoverScope",
                ParentId = "overlay.enable",
                FieldName = "hoverEffectScope",
                Label = "Hover Effect Scope",
                Tooltip = "Controls whether hover overlays are shown only on the hovered cell or across the whole column.",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.HoverEffectScope),
                DefaultValue = DefaultSettings.hoverEffectScope,
                ShowInSimpleView = false,
                SortOrder = 4
            });

            // Skill colors
            Register(new SettingDefinition
            {
                Id = "colors.header",
                Label = "Skill Colors",
                Type = SettingType.Header,
                Tooltip = "Customize colors for skill levels shown in the Work tab.",
                HeaderColor = new Color(0.6f, 0.6f, 0.6f),
                ShowInSimpleView = false,
                SortOrder = 300
            });

            Register(new SettingDefinition
            {
                Id = "colors.skillVeryLow",
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
                Id = "colors.skillLow",
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
                Id = "colors.skillGood",
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
                Id = "colors.skillExcellent",
                FieldName = "Color_ExcellentSkill",
                Label = "Excellent Skill (16+)",
                Tooltip = "Color for skills at level 16+.",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_ExcellentSkill,
                ShowInSimpleView = false,
                SortOrder = 304,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            // Advanced
            Register(new SettingDefinition
            {
                Id = "advanced.header",
                Label = "Advanced & Maintenance",
                Type = SettingType.Header,
                Tooltip = "Advanced toggles and maintenance/reset options.",
                HeaderColor = new Color(0.6f, 0.6f, 0.6f),
                ShowInSimpleView = true,
                SortOrder = 400
            });

            Register(new SettingDefinition
            {
                Id = "advanced.autoAssign",
                FieldName = "enableAutoAssignFeature",
                Label = "Enable Auto-Assign System",
                Tooltip = "Show auto-assign buttons and enable ruleset logic.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.enableAutoAssignFeature,
                ShowInSimpleView = true,
                SortOrder = 401
            });

            Register(new SettingDefinition
            {
                Id = "advanced.hideWorkloadBtn",
                FieldName = "hideWorkloadButton",
                Label = "Hide Workloads Button",
                Tooltip = "Hide the Workload button from the Work tab footer.",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.hideWorkloadButton,
                ShowInSimpleView = true,
                SortOrder = 402
            });

            Register(new SettingDefinition
            {
                Id = "advanced.hideAutoAssignBtn",
                FieldName = "hideAutoAssignButton",
                Label = "Hide Auto-Assign Button",
                Tooltip = "Hide the ruleset button (still accessible via Manager).",
                Type = SettingType.Bool,
                DefaultValue = DefaultSettings.hideAutoAssignButton,
                ShowInSimpleView = true,
                SortOrder = 403
            });

            Register(new SettingDefinition
            {
                Id = "advanced.debugLogging",
                FieldName = "enableDebugLogging",
                Label = "Enable Debug Logging",
                Tooltip = "Output detailed debug messages to the log.",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = true,
                SortOrder = 410
            });

            Register(new SettingDefinition
            {
                Id = "advanced.restoreDefaults",
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
