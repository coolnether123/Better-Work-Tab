using Better_Work_Tab;
using Better_Work_Tab.Features;
using Better_Work_Tab.Patches;
using Better_Work_Tab.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Data-driven definition for a single Better Work Tab setting.
    /// </summary>
    public class SettingDefinition
    {
        // Identity
        public string Id;
        public string FieldName;

        // Display
        public string Label;
        public string Tooltip;
        public string CategoryId;
        public int SortOrder;

        // Type info
        public SettingType Type;
        public object DefaultValue;

        // Slider metadata
        public float? MinValue;
        public float? MaxValue;
        public string MinLabel;
        public string MaxLabel;

        // Enum metadata
        public Type EnumType;

        // Visibility
        public bool ShowInSimpleView;
        public bool ShowInAdvancedView = true;
        public Func<BetterWorkTabSettings, bool> VisibleWhen;

        // Behavior
        public bool IsFavoritable;
        public bool RequiresRestart;
        public Action<BetterWorkTabSettings> OnChanged;
    }

    /// <summary>
    /// Describes a category shown in the settings UI.
    /// </summary>
    public class SettingsCategoryDefinition
    {
        public string Id;
        public string Label;
        public string Description;
        public Color HeaderColor;
        public int SortOrder;
        public string IconPath;
    }

    /// <summary>
    /// Supported setting widget types.
    /// </summary>
    public enum SettingType
    {
        Bool,
        Int,
        Float,
        Color,
        Enum,
        Button,
        Header,
        Spacer
    }

    /// <summary>
    /// Central registry for categories and settings used by both views.
    /// </summary>
    public static class SettingsRegistry
    {
        private static readonly List<SettingsCategoryDefinition> _categories = new List<SettingsCategoryDefinition>();
        private static readonly List<SettingDefinition> _settings = new List<SettingDefinition>();
        private static bool _initialized = false;

        public static IReadOnlyList<SettingsCategoryDefinition> Categories => _categories;
        public static IReadOnlyList<SettingDefinition> Settings => _settings;

        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            RegisterCategories();
            RegisterAllSettings();
            _initialized = true;
        }

        public static IEnumerable<SettingDefinition> GetByCategory(string categoryId)
        {
            return _settings
                .Where(s => s.CategoryId == categoryId)
                .OrderBy(s => s.SortOrder);
        }

        public static IEnumerable<SettingDefinition> GetForSimpleView()
        {
            return _settings
                .Where(s => s.ShowInSimpleView)
                .OrderBy(s => GetCategorySortOrder(s.CategoryId))
                .ThenBy(s => s.SortOrder);
        }

        /// <summary>
        /// Searches settings using translated labels and tooltips.
        /// </summary>
        public static IEnumerable<SettingDefinition> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return _settings;
            }

            return _settings.Where(s => SettingsTranslation.MatchesSearch(s, query));
        }

        private static int GetCategorySortOrder(string categoryId)
        {
            var category = _categories.FirstOrDefault(c => c.Id == categoryId);
            return category?.SortOrder ?? int.MaxValue;
        }

        private static void Register(SettingDefinition definition)
        {
            _settings.Add(definition);
        }

        private static void RegisterCategories()
        {
            _categories.Add(new SettingsCategoryDefinition
            {
                Id = "highlights",
                Label = "Highlights",
                Description = "Row and column highlighting behavior",
                HeaderColor = new Color(0.4f, 0.6f, 0.9f),
                SortOrder = 0
            });

            _categories.Add(new SettingsCategoryDefinition
            {
                Id = "layout",
                Label = "Layout & Behavior",
                Description = "Drag-drop, dividers, counters, columns",
                HeaderColor = new Color(0.5f, 0.8f, 0.5f),
                SortOrder = 1
            });

            _categories.Add(new SettingsCategoryDefinition
            {
                Id = "skillView",
                Label = "Skill View",
                Description = "Shift behavior and skill display",
                HeaderColor = new Color(0.9f, 0.7f, 0.4f),
                SortOrder = 2
            });

            _categories.Add(new SettingsCategoryDefinition
            {
                Id = "advanced",
                Label = "Advanced & Maintenance",
                Description = "Debug, reset, experimental options",
                HeaderColor = new Color(0.6f, 0.6f, 0.6f),
                SortOrder = 3
            });
        }

        private static void RegisterAllSettings()
        {
            // ═══════════════════════════════════════════
            // HIGHLIGHTS
            // ═══════════════════════════════════════════

            Register(new SettingDefinition
            {
                Id = "highlights.masterToggle",
                FieldName = "ShowPawnAndWorktypeHighlights",
                Label = "Enable All Highlights",
                Tooltip = "Master switch for all row and column highlighting. " +
                          "Turn off for a cleaner look or better performance.",
                CategoryId = "highlights",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = true,
                SortOrder = 0,
                IsFavoritable = true
            });

            Register(new SettingDefinition
            {
                Id = "highlights.hoverHighlight",
                FieldName = "ShowCursorPawnAndWorktypeHighlight",
                Label = "Highlight on Hover",
                Tooltip = "Tint the row and column under your cursor.",
                CategoryId = "highlights",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = true,
                SortOrder = 1,
                VisibleWhen = s => s.ShowPawnAndWorktypeHighlights
            });

            Register(new SettingDefinition
            {
                Id = "highlights.hoverColor",
                FieldName = "Color_CursorHighlight",
                Label = "Hover Highlight Color",
                Tooltip = "Color for row/column highlight on hover.",
                CategoryId = "highlights",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_CursorHighlight,
                ShowInSimpleView = true,
                SortOrder = 2,
                VisibleWhen = s => s.ShowCursorPawnAndWorktypeHighlight,
                OnChanged = _ => { }
            });

            Register(new SettingDefinition
            {
                Id = "highlights.selectedPawn",
                FieldName = "DoSelectedPawnHighlight",
                Label = "Highlight Selected Pawn",
                Tooltip = "Always highlight the currently selected pawn's row.",
                CategoryId = "highlights",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = true,
                SortOrder = 3,
                VisibleWhen = s => s.ShowPawnAndWorktypeHighlights
            });

            Register(new SettingDefinition
            {
                Id = "highlights.floatMenu",
                FieldName = "ShowFloatMenuPawnAndWorktypeHighlight",
                Label = "Highlight Context Source",
                Tooltip = "Highlight row/column when right-click menu is open.",
                CategoryId = "highlights",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = true,
                SortOrder = 4,
                VisibleWhen = s => s.ShowPawnAndWorktypeHighlights
            });

            Register(new SettingDefinition
            {
                Id = "highlights.outlineMode",
                FieldName = "useOutlineHighlights",
                Label = "Use Outline Highlights",
                Tooltip = "Draw highlights as outlines instead of solid boxes.",
                CategoryId = "highlights",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = true,
                SortOrder = 5,
                VisibleWhen = s => s.ShowPawnAndWorktypeHighlights
            });

            // ═══════════════════════════════════════════
            // LAYOUT & BEHAVIOR
            // ═══════════════════════════════════════════

            Register(new SettingDefinition
            {
                Id = "layout.ctrlDrag",
                FieldName = "requireCtrlForDrag",
                Label = "Require Ctrl for Dragging",
                Tooltip = "Hold Ctrl to drag rows/columns. Prevents accidental reordering.",
                CategoryId = "layout",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = true,
                SortOrder = 0,
                IsFavoritable = true
            });

            Register(new SettingDefinition
            {
                Id = "layout.clickClose",
                FieldName = "disableLeftClickClose",
                Label = "Prevent Click-Off Close",
                Tooltip = "Keep the Work tab open when clicking the map.",
                CategoryId = "layout",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = "layout.pawnCount",
                FieldName = "showPawnCountAtBottom",
                Label = "Show Colonist Count",
                Tooltip = "Display colonist count in the bottom-left corner.",
                CategoryId = "layout",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = true,
                SortOrder = 2
            });

            Register(new SettingDefinition
            {
                Id = "layout.bedCount",
                FieldName = "showBedCountAtBottom",
                Label = "Show Bed Count",
                Tooltip = "Display bed count (red if insufficient).",
                CategoryId = "layout",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = true,
                SortOrder = 3
            });

            Register(new SettingDefinition
            {
                Id = "layout.dividerHeight",
                FieldName = "dividerHeight",
                Label = "Divider Default Height",
                Tooltip = "Default height of divider rows in pixels.",
                CategoryId = "layout",
                Type = SettingType.Float,
                DefaultValue = 18f,
                MinValue = 10f,
                MaxValue = 50f,
                MinLabel = "Thin",
                MaxLabel = "Thick",
                ShowInSimpleView = true,
                SortOrder = 4
            });

            Register(new SettingDefinition
            {
                Id = "layout.dividerAlpha",
                FieldName = "dividerMinAlpha",
                Label = "Divider Minimum Opacity",
                Tooltip = "Minimum background opacity for dividers.",
                CategoryId = "layout",
                Type = SettingType.Float,
                DefaultValue = 0.35f,
                MinValue = 0f,
                MaxValue = 1f,
                ShowInSimpleView = true,
                SortOrder = 5
            });

            Register(new SettingDefinition
            {
                Id = "layout.resetColumns",
                Label = "Reset Columns to Vanilla",
                Tooltip = "Restore all work columns to their default order.",
                CategoryId = "layout",
                Type = SettingType.Button,
                ShowInSimpleView = true,
                SortOrder = 10,
                OnChanged = _ => WorkColumnOrderManager.ResetToVanilla()
            });

            // ═══════════════════════════════════════════
            // SKILL VIEW
            // ═══════════════════════════════════════════

            Register(new SettingDefinition
            {
                Id = "overlay.enable",
                FieldName = "enableSkillOverlayFeature",
                Label = "Enable Skill Overlay",
                Tooltip = "Show skill information when holding Shift in the Work tab.",
                CategoryId = "skillView",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = true,
                SortOrder = 0,
                IsFavoritable = true
            });

            Register(new SettingDefinition
            {
                Id = "overlay.numbersMode",
                FieldName = "ShowUIMode_ShowSmallSkillNumbers",
                Label = "Skill Numbers Display",
                Tooltip = "When to show small skill numbers in cells.",
                CategoryId = "skillView",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.ShowUIMode),
                DefaultValue = BetterWorkTabSettings.ShowUIMode.Unshifted,
                ShowInSimpleView = true,
                SortOrder = 1,
                VisibleWhen = s => s.enableSkillOverlayFeature
            });

            Register(new SettingDefinition
            {
                Id = "overlay.bestPawnMode",
                FieldName = "ShowUIMode_ShowPawnForSkillSquare",
                Label = "Best Pawn Indicator",
                Tooltip = "When to highlight the pawn with highest skill.",
                CategoryId = "skillView",
                Type = SettingType.Enum,
                EnumType = typeof(BetterWorkTabSettings.ShowUIMode),
                DefaultValue = BetterWorkTabSettings.ShowUIMode.Shifted,
                ShowInSimpleView = true,
                SortOrder = 2,
                VisibleWhen = s => s.enableSkillOverlayFeature
            });

            Register(new SettingDefinition
            {
                Id = "overlay.hoverCellOverlay",
                FieldName = "showHoverCellOverlay",
                Label = "Show Hover Cell Overlay",
                Tooltip = "When holding Shift, show skill/priority info when hovering a cell. Disable to keep cells unchanged on hover.",
                CategoryId = "skillView",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = true,
                SortOrder = 3,
                VisibleWhen = s => s.enableSkillOverlayFeature
            });

            Register(new SettingDefinition
            {
                Id = "colors.skillVeryLow",
                FieldName = "Color_VeryLowSkill",
                Label = "Very Low Skill (0-3)",
                Tooltip = "Color for skills at level 0-3.",
                CategoryId = "advanced",
                Type = SettingType.Color,
                DefaultValue = new Color(0.82f, 0.25f, 0.25f),
                ShowInSimpleView = false,
                SortOrder = 3,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            Register(new SettingDefinition
            {
                Id = "colors.skillLow",
                FieldName = "Color_LowSkill",
                Label = "Low Skill (4-9)",
                Tooltip = "Color for skills at level 4-9.",
                CategoryId = "advanced",
                Type = SettingType.Color,
                DefaultValue = new Color(0.95f, 0.75f, 0.20f),
                ShowInSimpleView = false,
                SortOrder = 4,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            Register(new SettingDefinition
            {
                Id = "colors.skillGood",
                FieldName = "Color_GoodLowSkill",
                Label = "Good Skill (10-15)",
                Tooltip = "Color for skills at level 10-15.",
                CategoryId = "advanced",
                Type = SettingType.Color,
                DefaultValue = new Color(0.95f, 0.95f, 0.95f),
                ShowInSimpleView = false,
                SortOrder = 5,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            Register(new SettingDefinition
            {
                Id = "colors.skillExcellent",
                FieldName = "Color_ExcellentSkill",
                Label = "Excellent Skill (16+)",
                Tooltip = "Color for skills at level 16+.",
                CategoryId = "advanced",
                Type = SettingType.Color,
                DefaultValue = new Color(0.35f, 0.85f, 0.35f),
                ShowInSimpleView = false,
                SortOrder = 6,
                OnChanged = _ => Patch_WorkPriority_DoCell_Unified.ClearColorCache()
            });

            Register(new SettingDefinition
            {
                Id = "highlights.rowHoverColor",
                FieldName = "Color_RowHoverHighlight",
                Label = "Row Hover Color",
                Tooltip = "Override color for row highlight on hover.",
                CategoryId = "advanced",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_RowHoverHighlight,
                ShowInSimpleView = false,
                SortOrder = 7,
                VisibleWhen = s => s.ShowCursorPawnAndWorktypeHighlight,
            });

            Register(new SettingDefinition
            {
                Id = "highlights.columnHoverColor",
                FieldName = "Color_ColumnHoverHighlight",
                Label = "Column Hover Color",
                Tooltip = "Override color for column highlight on hover.",
                CategoryId = "advanced",
                Type = SettingType.Color,
                DefaultValue = DefaultSettings.Color_ColumnHoverHighlight,
                ShowInSimpleView = false,
                SortOrder = 8,
                VisibleWhen = s => s.ShowCursorPawnAndWorktypeHighlight,
            });

            Register(new SettingDefinition
            {
                Id = "highlights.resetRowHoverColor",
                Label = "Reset Row Hover Color",
                Tooltip = "Reset row hover highlight to the general hover color.",
                CategoryId = "advanced",
                Type = SettingType.Button,
                ShowInSimpleView = false,
                SortOrder = 9,
                VisibleWhen = s => s.ShowCursorPawnAndWorktypeHighlight,
                OnChanged = s =>
                {
                    s.useRowHoverOverride = false;
                    s.Color_RowHoverHighlight = s.Color_MouseHoverHighlight;
                    s.Write();
                }
            });

            Register(new SettingDefinition
            {
                Id = "highlights.resetColumnHoverColor",
                Label = "Reset Column Hover Color",
                Tooltip = "Reset column hover highlight to the general hover color.",
                CategoryId = "advanced",
                Type = SettingType.Button,
                ShowInSimpleView = false,
                SortOrder = 10,
                VisibleWhen = s => s.ShowCursorPawnAndWorktypeHighlight,
                OnChanged = s =>
                {
                    s.useColumnHoverOverride = false;
                    s.Color_ColumnHoverHighlight = s.Color_MouseHoverHighlight;
                    s.Write();
                }
            });

            // ═══════════════════════════════════════════
            // ADVANCED & MAINTENANCE
            // ═══════════════════════════════════════════

            Register(new SettingDefinition
            {
                Id = "advanced.autoAssign",
                FieldName = "enableAutoAssignFeature",
                Label = "Enable Auto-Assign System",
                Tooltip = "Show auto-assign buttons and enable ruleset logic.",
                CategoryId = "advanced",
                Type = SettingType.Bool,
                DefaultValue = true,
                ShowInSimpleView = true,
                SortOrder = 0
            });

            Register(new SettingDefinition
            {
                Id = "advanced.hideWorkloadBtn",
                FieldName = "hideWorkloadButton",
                Label = "Hide Workloads Button",
                Tooltip = "Hide the Workload button from the Work tab footer.",
                CategoryId = "advanced",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = true,
                SortOrder = 1
            });

            Register(new SettingDefinition
            {
                Id = "advanced.hideAutoAssignBtn",
                FieldName = "hideAutoAssignButton",
                Label = "Hide Auto-Assign Button",
                Tooltip = "Hide the ruleset button (still accessible via Manager).",
                CategoryId = "advanced",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = true,
                SortOrder = 2
            });

            Register(new SettingDefinition
            {
                Id = "advanced.debugLogging",
                FieldName = "enableDebugLogging",
                Label = "Enable Debug Logging",
                Tooltip = "Output detailed debug messages to the log.",
                CategoryId = "advanced",
                Type = SettingType.Bool,
                DefaultValue = false,
                ShowInSimpleView = true,
                SortOrder = 10
            });

            Register(new SettingDefinition
            {
                Id = "advanced.restoreDefaults",
                Label = "Restore Factory Defaults",
                Tooltip = "Reset ALL settings to default values.",
                CategoryId = "advanced",
                Type = SettingType.Button,
                ShowInSimpleView = true,
                SortOrder = 20,
                OnChanged = s => s.RestoreDefaults()
            });
        }
    }
}
