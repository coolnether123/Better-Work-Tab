using UnityEngine;

namespace Better_Work_Tab.UI.RuleBuilder
{
    /// <summary>
    /// Centralized constants for the rule builder UI.
    /// Ensures visual consistency across all components.
    /// </summary>
    public static class RuleBuilderConstants
    {
        // ═══════════════════════════════════════════════════════════════
        // LAYOUT DIMENSIONS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Height of the top navigation bar.</summary>
        public const float NavBarHeight = 40f;

        /// <summary>Standard row height for lists.</summary>
        public const float RowHeight = 28f;

        /// <summary>Standard padding inside panels.</summary>
        public const float PanelPadding = 12f;

        /// <summary>Background for panel chrome and headers.</summary>
        public static readonly Color PanelBackground = new Color(0.14f, 0.14f, 0.14f, 0.95f);

        /// <summary>Width of priority selector column (middle).</summary>
        public const float PrioritySelectorColumnWidth = 240f;

        /// <summary>Width of work type list column (left).</summary>
        public const float WorkTypeListColumnWidth = 180f;

        /// <summary>Gap between columns.</summary>
        public const float ColumnGap = 12f;

        /// <summary>Height of priority button.</summary>
        public const float PriorityButtonHeight = 40f;

        /// <summary>Height of condition pill.</summary>
        public const float ConditionPillHeight = 24f;

        /// <summary>Height of condition set card header.</summary>
        public const float ConditionSetHeaderHeight = 32f;

        /// <summary>Row height for inline condition editors.</summary>
        public const float ConditionRowHeight = 28f;

        /// <summary>Maximum characters allowed for rule names.</summary>
        public const int MaxRuleNameLength = 64;

        /// <summary>Width of a work type card in the selector grid.</summary>
        public const float WorkTypeCardWidth = 170f;

        /// <summary>Height of a work type card in the selector grid.</summary>
        public const float WorkTypeCardHeight = 120f;

        /// <summary>Spacing between cards in grids and rows.</summary>
        public const float CardSpacing = 8f;

        // ── Preview panel ────────────────────────────────────────────────────

        /// <summary>Total height of the preview panel (header + grid).</summary>
        public const float PreviewPanelHeight = 260f;

        /// <summary>Height of each pawn row in the preview grid.</summary>
        public const float PreviewRowHeight = 22f;

        /// <summary>Width of each priority cell in the preview grid.</summary>
        public const float PreviewCellWidth = 34f;

        /// <summary>Width of the pawn-name column in the preview grid.</summary>
        public const float PreviewNameColumnWidth = 128f;

        // ═══════════════════════════════════════════════════════════════
        // COLORS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Background for unselected cards.</summary>
        public static readonly Color CardBackground = new Color(0.18f, 0.18f, 0.18f, 0.95f);

        /// <summary>Background for hovered cards.</summary>
        public static readonly Color CardBackgroundHover = new Color(0.25f, 0.25f, 0.25f, 0.95f);

        /// <summary>Background for selected cards.</summary>
        public static readonly Color CardBackgroundSelected = new Color(0.2f, 0.35f, 0.2f, 0.95f);

        /// <summary>Light background for panel sections.</summary>
        public static readonly Color PanelBackgroundLight = new Color(0.16f, 0.16f, 0.16f, 0.95f);

        /// <summary>Border color for configured items.</summary>
        public static readonly Color CardBorderConfigured = new Color(0.45f, 0.9f, 0.45f);

        /// <summary>Color for section headers.</summary>
        public static readonly Color HeaderColor = new Color(0.9f, 0.85f, 0.7f);

        /// <summary>Standard label text color.</summary>
        public static readonly Color LabelColor = new Color(0.85f, 0.85f, 0.85f);

        /// <summary>Subtle/secondary text color.</summary>
        public static readonly Color SubtleTextColor = new Color(0.6f, 0.6f, 0.6f);

        /// <summary>Color for success indicators.</summary>
        public static readonly Color SuccessColor = new Color(0.4f, 0.8f, 0.4f);

        /// <summary>Color for failure/error indicators.</summary>
        public static readonly Color FailureColor = new Color(0.8f, 0.4f, 0.4f);

        /// <summary>Color for disabled elements.</summary>
        public static readonly Color DisabledColor = new Color(0.5f, 0.5f, 0.5f);

        /// <summary>Priority level colors indexed 0-4.</summary>
        public static readonly Color[] PriorityColors = new Color[]
        {
            new Color(0.4f, 0.4f, 0.4f),     // 0 - Disabled (gray)
            new Color(0.2f, 0.8f, 0.2f),     // 1 - Highest (bright green)
            new Color(0.5f, 0.75f, 0.2f),    // 2 - High (yellow-green)
            new Color(0.85f, 0.65f, 0.15f),  // 3 - Normal (yellow)
            new Color(0.75f, 0.35f, 0.15f)   // 4 - Low (orange)
        };
    }
}
