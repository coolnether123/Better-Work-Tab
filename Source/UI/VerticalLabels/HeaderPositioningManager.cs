using UnityEngine;
using System.Collections.Generic;
using Verse;

namespace Better_Work_Tab.UI
{
    public static class HeaderPositioningManager
    {
        private static float[] _standardHeights;

        static HeaderPositioningManager()
        {
            InitializeHeights();
        }

        private static void InitializeHeights()
        {
            _standardHeights = new float[3];
            GameFont oldFont = Text.Font;
            
            Text.Font = GameFont.Small;
            float rowHeight = Text.LineHeight + 2f;
            
            // Offsets from baseline
            // Priority: Stay at baseline first, then move DOWN (positive Y)
            _standardHeights[0] = 0f;              // Baseline (highest priority - stay here if possible)
            _standardHeights[1] = rowHeight;       // One row DOWN
            _standardHeights[2] = rowHeight * 2f;  // Two rows DOWN
            
            Text.Font = oldFont;
        }

        public static float[] GetThreeStandardHeights() => _standardHeights;

        public static bool DetectBoundingBoxOverlap(Rect a, Rect b)
        {
            return a.Overlaps(b);
        }

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
            return _standardHeights[2];
        }

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
            worstCase.y += _standardHeights[2];
            return worstCase;
        }
    }
}
