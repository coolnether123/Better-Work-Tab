using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using System.Collections.Generic;
using Better_Work_Tab.Features.Testing;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGiverReassignments;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Shared utility methods and constants for header rendering and logic.
    /// Minimizes code duplication across vanilla and angled implementations.
    /// </summary>
    public static class HeaderUtility
    {
        private static readonly Dictionary<int, string> HeaderTextCache = new Dictionary<int, string>();

        /// <summary>
        /// Suffix used to indicate a column has been moved from its baseline position.
        /// </summary>
        public const string MovedMarker = "*";

        /// <summary>
        /// Default text to show if a work type label cannot be determined.
        /// </summary>
        public const string DefaultHeaderText = "Work";

        /// <summary>
        /// Padding used for collision detection in staggered headers.
        /// </summary>
        public const float CollisionPadding = 1f;

        public static IList<PawnColumnDef> GetTableColumns(PawnTable table)
        {
            if (table == null) return null;
#if v1_3 || v1_2 || v1_1 || (v1_0 || v0_19)
            return table.ColumnsListForReading;
#else
            return table.Columns;
#endif
        }

        /// <summary>
        /// Formats the header text for a given work type, applying capitalization
        /// and adding the moved marker if specified.
        /// </summary>
        /// <param name="workType">The work type to get the label for.</param>
        /// <param name="isMoved">Whether to append the moved marker (*).</param>
        /// <returns>A formatted and capitalized header label.</returns>
        public static string GetHeaderText(
            WorkTypeDef workType,
            bool isMoved = false,
            WorkGiverHeaderLabelStyle subWorkLabelStyle = WorkGiverHeaderLabelStyle.Standard)
        {
            int key = ComputeHeaderTextKey(workType, isMoved, subWorkLabelStyle, parentOnly: false);
            if (HeaderTextCache.TryGetValue(key, out string cached))
            {
                return cached;
            }

            SubWorkTransitionPerfDiagnostics.CountHeaderTextBuild();
            string label;
            if (workType == null)
            {
                label = DefaultHeaderText;
            }
            else if (SubWorkDrilldownState.TryGetCurrentDrawingWorkGiver(
                         null,
                         out var drawingWorkGiver,
                         out _,
                         out _) &&
                     drawingWorkGiver?.def != null)
            {
                label = WorkGiverDisplayNameService.HeaderLabel(drawingWorkGiver.def, subWorkLabelStyle);
                if (isMoved && BetterWorkTabMod.Settings != null && BetterWorkTabMod.Settings.showColumnMovedMarker && !label.EndsWith(MovedMarker))
                {
                    label += MovedMarker;
                }
                return label;
            }
            else if (SubWorkDrilldownState.IsActive &&
                TryGetSubWorkHeaderText(workType, isMoved, subWorkLabelStyle, out var subWorkText))
            {
                label = subWorkText;
            }
            else
            {
                label = BuildParentHeaderText(workType, isMoved);
            }

            HeaderTextCache[key] = label;
            return label;
        }

        public static string GetParentHeaderText(WorkTypeDef workType, bool isMoved = false)
        {
            int key = ComputeHeaderTextKey(workType, isMoved, WorkGiverHeaderLabelStyle.Standard, parentOnly: true);
            if (HeaderTextCache.TryGetValue(key, out string cached))
            {
                return cached;
            }

            SubWorkTransitionPerfDiagnostics.CountHeaderTextBuild();
            string label = BuildParentHeaderText(workType, isMoved);
            HeaderTextCache[key] = label;
            return label;
        }

        private static string BuildParentHeaderText(WorkTypeDef workType, bool isMoved)
        {
            if (workType == null) return DefaultHeaderText;

            string label = WorkTypeDisplayNameService.HeaderLabel(workType);

            var settings = BetterWorkTabMod.Settings;
            if (isMoved && settings != null && settings.showColumnMovedMarker && !label.EndsWith(MovedMarker))
            {
                label += MovedMarker;
            }

            return label;
        }

        private static int ComputeHeaderTextKey(
            WorkTypeDef workType,
            bool isMoved,
            WorkGiverHeaderLabelStyle labelStyle,
            bool parentOnly)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (workType?.shortHash ?? 0);
                hash = hash * 23 + (isMoved ? 1 : 0);
                hash = hash * 23 + (int)labelStyle;
                hash = hash * 23 + (parentOnly ? 1 : 0);
                if (!parentOnly)
                {
                    hash = hash * 23 + SubWorkDrilldownState.CurrentDrawingHeaderSignature;
                }
                hash = hash * 23 + CustomLabelStore.Version;
                hash = hash * 23 + (BetterWorkTabMod.Settings?.showColumnMovedMarker ?? true ? 1 : 0);
                if (!parentOnly && SubWorkDrilldownState.IsActive)
                {
                    hash = hash * 23 + SubWorkDrilldownState.MeasurementSignature;
                }

                return hash;
            }
        }

        private static bool TryGetSubWorkHeaderText(
            WorkTypeDef workType,
            bool isMoved,
            WorkGiverHeaderLabelStyle labelStyle,
            out string label)
        {
            label = string.Empty;
            if (workType == null)
            {
                return false;
            }

            var tableDef = PawnTableDefOf.Work;
            if (tableDef?.columns == null)
            {
                return false;
            }

            for (int i = 0; i < tableDef.columns.Count; i++)
            {
                var column = tableDef.columns[i];
                if (column?.workType != workType || !(column.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                if (!SubWorkDrilldownState.TryGetWorkGiverForColumn(column, out var workGiver, out _))
                {
                    label = string.Empty;
                    return true;
                }

                label = WorkGiverDisplayNameService.HeaderLabel(workGiver.def, labelStyle);
                var settings = BetterWorkTabMod.Settings;
                if (isMoved && settings != null && settings.showColumnMovedMarker && !label.EndsWith(MovedMarker))
                {
                    label += MovedMarker;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Detects if a string contains CJK (Chinese, Japanese, Korean) characters by checking specific Unicode ranges.
        /// Coverage includes CJK Unified Ideographs, Hangul Syllables, and Hiragana/Katakana.
        /// </summary>
        /// <param name="text">The string to analyze.</param>
        /// <returns>True if at least one CJK character is found.</returns>
        public static bool IsCJK(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                // Check ranges for Korean, Chinese, and Japanese scripts
                if ((c >= 0x4E00 && c <= 0x9FFF) || // CJK Ideographs
                    (c >= 0xAC00 && c <= 0xD7AF) || // Hangul Syllables
                    (c >= 0x3040 && c <= 0x30FF))   // Hiragana/Katakana
                    return true;
            }
            return false;
        }

        public static bool ShouldUseCJKVerticalLabel(string text)
        {
            var settings = BetterWorkTabMod.Settings;
            return settings != null && settings.useVerticalStackingForCJK && IsCJK(text);
        }

        /// <summary>
        /// Determines if any column in the provided table is eligible for specialized CJK vertical stacking.
        /// This depends on the 'useVerticalStackingForCJK' setting and the presence of CJK characters in the column labels.
        /// </summary>
        /// <param name="table">The pawn table to inspect.</param>
        /// <returns>True if specialized vertical stacking logic should be applied to the header area.</returns>
        public static bool IsAnyCJKVertical(PawnTable table)
        {
            var settings = BetterWorkTabMod.Settings;
            var columns = GetTableColumns(table);
            if (settings == null || !settings.useVerticalStackingForCJK || columns == null)
                return false;

            foreach (var col in columns)
            {
                string headerText = col.workType != null ? GetHeaderText(col.workType, false) : null;
                if (!headerText.NullOrEmpty() && ShouldUseCJKVerticalLabel(headerText))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if ANY work priority column in the table has been moved from its baseline position.
        /// Used to determine if the custom header system should take over rendering.
        /// </summary>
        /// <param name="table">The pawn table to check.</param>
        /// <returns>True if at least one work column is moved.</returns>
        public static bool CheckIfAnyColumnsAreMoved(PawnTable table)
        {
            var tableCols = PawnTableCompat.GetColumnsListForReading(table);
            if (tableCols == null) return false;

            foreach (var col in tableCols)
            {
                if (col?.workType != null && MainTabWindow_BetterWork.IsColumnOutOfBaselinePosition(col.workType))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Shared colors for header highlights and text.
        /// </summary>
        public static class Colors
        {
            /// <summary>
            /// Color for the yellow marker indicating a moved column.
            /// Reads from settings to allow user customization.
            /// </summary>
            public static Color MovedMarkerColor => BetterWorkTabMod.Settings?.movedMarkerColor ?? new Color(1f, 0.85f, 0.2f, 1f);

            /// <summary>
            /// Color for the highlight box when a column is selected.
            /// </summary>
            public static readonly Color SelectedHighlight = new Color(1f, 0.92f, 0.4f, 0.4f);

            /// <summary>
            /// Color for the highlight box when a column is hovered.
            /// </summary>
            public static readonly Color HoverHighlight = new Color(1f, 1f, 1f, 0.25f);

            /// <summary>
            /// Dim color for the sort indicator.
            /// </summary>
            public static readonly Color SortIndicatorColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);

            /// <summary>
            /// Default color of the stem line in vanilla staggering (#5b6064).
            /// </summary>
            public static readonly Color DefaultVanillaStemColor = new Color(91f / 255f, 96f / 255f, 100f / 255f, 1f);

            /// <summary>
            /// User-configurable underline color for angled headers.
            /// </summary>
            public static Color HeaderUnderlineColor =>
                BetterWorkTabMod.Settings?.headerUnderlineColor ?? DefaultSettings.Color_HeaderUnderline;

            /// <summary>
            /// User-configurable stem color for vanilla-style headers. The default preserves RimWorld's grey.
            /// </summary>
            public static Color VanillaStemColor
            {
                get
                {
                    var settings = BetterWorkTabMod.Settings;
                    if (settings == null ||
                        Approximately(settings.headerUnderlineColor, DefaultSettings.Color_HeaderUnderline))
                    {
                        return DefaultVanillaStemColor;
                    }

                    return settings.headerUnderlineColor;
                }
            }

            private static bool Approximately(Color a, Color b)
            {
                return Mathf.Approximately(a.r, b.r) &&
                       Mathf.Approximately(a.g, b.g) &&
                       Mathf.Approximately(a.b, b.b) &&
                       Mathf.Approximately(a.a, b.a);
            }
        }

        /// <summary>
        /// Shared method to draw the sorting indicator (up/down arrow) at the bottom of a header.
        /// </summary>
        /// <param name="headerRect">The base header rect.</param>
        /// <param name="descending">Whether sorting is descending.</param>
        public static void DrawSortIndicator(Rect headerRect, bool descending)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;

            Color color = Colors.SortIndicatorColor;
            float transitionAlpha = SubWorkDrilldownState.IsTransitioning
                ? Mathf.Max(SubWorkDrilldownState.HeaderFlipAlpha, SubWorkDrilldownState.ParentWorkContentAlpha)
                : 1f;
            color.a *= transitionAlpha;
            GUI.color = color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;

            // Position to match angled headers (9px from bottom, centered)
            const float indicatorSize = 12f;
            const float yOffsetFromBottom = 9f;

            Rect sortRect = new Rect(
                headerRect.center.x - (indicatorSize / 2f) + 9f, 
                headerRect.yMax - yOffsetFromBottom, 
                indicatorSize, 
                indicatorSize
            );

            Widgets.Label(sortRect, descending ? "\u25BC" : "\u25B2");
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
        }

        /// <summary>
        /// Determines if the current event is one that headers should respond to.
        /// Consolidates event type checks across various header controllers.
        /// </summary>
        public static bool ShouldHandleHeader(EventType eventType)
        {
            return eventType == EventType.Repaint 
                || eventType == EventType.MouseDown
                || eventType == EventType.MouseMove
                || eventType == EventType.MouseDrag
                || eventType == EventType.MouseUp;
        }
    }

    /// <summary>
    /// Base class for systems that cache data on a per-frame basis.
    /// Ensures consistent naming and logic for frame-level invalidation.
    /// </summary>
    public abstract class FrameCachedSystem
    {
        private int _lastCacheFrame = -1;

        /// <summary>
        /// Checks if the current frame is different from the last time this method was called.
        /// If so, updates the internal frame tracker.
        /// </summary>
        /// <returns>True if this is the first time calling this in the current frame.</returns>
        protected bool IsFrameNew()
        {
            int current = Time.frameCount;
            if (_lastCacheFrame != current)
            {
                _lastCacheFrame = current;
                return true;
            }
            return false;
        }
    }
}
