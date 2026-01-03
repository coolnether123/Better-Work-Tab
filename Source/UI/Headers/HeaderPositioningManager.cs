using UnityEngine;
using System.Collections.Generic;
using Verse;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Manages standard vertical offsets for staggered headers.
    /// Provides utilities for overlap detection and snapping.
    /// </summary>
    public static class HeaderPositioningManager
    {
        private const int MaxStaggerLevels = 3;
        private const float VerticalPadding = 2f;
        private const float StemChannelWidth = 10f;

        private static float[] _standardHeights;

        static HeaderPositioningManager()
        {
            InitializeHeights();
        }

        private static void InitializeHeights()
        {
            _standardHeights = new float[MaxStaggerLevels];
            GameFont oldFont = Text.Font;
            
            Text.Font = GameFont.Small;
            float rowHeight = Text.LineHeight + VerticalPadding;
            
            // Offsets from baseline
            // Priority: Stay at baseline first, then move DOWN (positive Y)
            _standardHeights[0] = 0f;              // Baseline (highest priority - stay here if possible)
            _standardHeights[1] = rowHeight;       // One row DOWN
            _standardHeights[2] = rowHeight * 2f;  // Two rows DOWN
            
            Text.Font = oldFont;
        }

        /// <summary>
        /// Returns the three standard vertical offsets used for staggering.
        /// </summary>
        /// <returns>An array of float offsets.</returns>
        public static float[] GetThreeStandardHeights() => _standardHeights;

        /// <summary>
        /// Basic AABB overlap detection between two rectangles.
        /// </summary>
        public static bool DetectBoundingBoxOverlap(Rect a, Rect b)
        {
            return a.Overlaps(b);
        }

        /// <summary>
        /// Calculates the best vertical offset for a header to avoid overlapping with adjacent headers.
        /// </summary>
        /// <param name="headerRect">The original rect of the header.</param>
        /// <param name="adjacentHeaderBounds">A list of bounding boxes of already placed neighbors.</param>
        /// <returns>The chosen vertical offset.</returns>
        public static float CalculateValidHeaderHeight(Rect headerRect, List<Rect> adjacentHeaderBounds)
        {
            foreach (float offset in _standardHeights)
            {
                Rect testRect = headerRect;
                testRect.y += offset;

                bool overlaps = false;
                foreach (var other in adjacentHeaderBounds)
                {
                    if (DetectBoundingBoxOverlap(testRect, other))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps) return offset;
            }
            return _standardHeights[MaxStaggerLevels - 1];
        }

        /// <summary>
        /// Snaps a header rect to the first available vertical level that doesn't overlap its neighbors.
        /// </summary>
        /// <param name="headerRect">The original rect.</param>
        /// <param name="leftNeighbor">Optional bounds of the left neighbor.</param>
        /// <param name="rightNeighbor">Optional bounds of the right neighbor.</param>
        /// <returns>A new Rect adjusted for staggering.</returns>
        public static Rect SnapToNonOverlappingHeight(Rect headerRect, Rect? leftNeighbor, Rect? rightNeighbor)
        {
            // Try each offset in priority order (baseline first, then progressively lower)
            foreach (float offset in _standardHeights)
            {
                Rect testRect = headerRect;
                testRect.y += offset;

                bool overlaps = false;
                if (leftNeighbor.HasValue && DetectBoundingBoxOverlap(testRect, leftNeighbor.Value))
                    overlaps = true;
                if (!overlaps && rightNeighbor.HasValue && DetectBoundingBoxOverlap(testRect, rightNeighbor.Value))
                    overlaps = true;

                if (!overlaps) return testRect;
            }
            
            // Worst case: use the lowest position
            Rect worstCase = headerRect;
            worstCase.y += _standardHeights[MaxStaggerLevels - 1];
            return worstCase;
        }
    }
}
