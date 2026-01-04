using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Shared utility methods and constants for header rendering and logic.
    /// Ensures DRY (Don't Repeat Yourself) practices across vanilla and angled implementations.
    /// </summary>
    public static class HeaderUtility
    {
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

        /// <summary>
        /// Formats the header text for a given work type, applying capitalization
        /// and adding the moved marker if specified.
        /// </summary>
        /// <param name="workType">The work type to get the label for.</param>
        /// <param name="isMoved">Whether to append the moved marker (*).</param>
        /// <returns>A formatted and capitalized header label.</returns>
        public static string GetHeaderText(WorkTypeDef workType, bool isMoved = false)
        {
            if (workType == null) return DefaultHeaderText;

            // Use the shortest available valid label
            string baseText = workType.labelShort;
            if (baseText.NullOrEmpty()) baseText = workType.label;
            if (baseText.NullOrEmpty()) baseText = workType.defName;

            string label = (baseText.NullOrEmpty() ? DefaultHeaderText : baseText).CapitalizeFirst();

            if (isMoved && !label.EndsWith(MovedMarker))
            {
                label += MovedMarker;
            }

            return label;
        }

        /// <summary>
        /// Checks if ANY work priority column in the table has been moved from its baseline position.
        /// Used to determine if the custom header system should take over rendering.
        /// </summary>
        /// <param name="table">The pawn table to check.</param>
        /// <returns>True if at least one work column is moved.</returns>
        public static bool CheckIfAnyColumnsAreMoved(PawnTable table)
        {
            var tableCols = table?.def?.columns;
            if (tableCols == null) return false;

            foreach (var col in tableCols)
            {
                if (col?.workType != null && MainTabWindow_BetterWork.ShouldShowColumnMarker(col.workType))
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
            /// </summary>
            public static readonly Color MovedMarkerColor = new Color(1f, 0.85f, 0.2f, 1f);

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
            /// Color of the stem line in vanilla staggering (#5b6064).
            /// </summary>
            public static readonly Color VanillaStemColor = new Color(91f / 255f, 96f / 255f, 100f / 255f, 1f);
        }

        /// <summary>
        /// Shared method to draw the sorting indicator (up/down arrow) at the bottom of a header.
        /// </summary>
        /// <param name="headerRect">The base header rect.</param>
        /// <param name="descending">Whether sorting is descending.</param>
        public static void DrawSortIndicator(Rect headerRect, bool descending)
        {
            GUI.color = Colors.SortIndicatorColor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            // Position to match angled headers (9px from bottom, centered)
            const float indicatorSize = 12f;
            const float yOffsetFromBottom = 9f;

            Rect sortRect = new Rect(
                headerRect.center.x - (indicatorSize / 2f) + 9f, 
                headerRect.yMax - yOffsetFromBottom, 
                indicatorSize, 
                indicatorSize
            );

            Widgets.Label(sortRect, descending ? "▼" : "▲");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft; // Reset anchor
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
