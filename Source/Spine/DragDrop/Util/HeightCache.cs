using System;
using UnityEngine;

namespace Spine.DragDropApi.Util
{
    /// <summary>
    /// Height cache used for fast index lookup in lists with variable item heights.
    /// Precomputes cumulative heights and uses binary search for O(log n) insertion lookups.
    /// </summary>
    public struct HeightCache
    {
        /// <summary>
        /// Per-item heights, copied from the source list at build time.
        /// </summary>
        public float[] Heights { get; private set; }

        /// <summary>
        /// Cumulative heights. CumulativeHeights[i] is the top of item i in local coordinates.
        /// Length is Heights.Length + 1. The last value is total height.
        /// </summary>
        public float[] CumulativeHeights { get; private set; }

        /// <summary>
        /// Total height of all items combined.
        /// </summary>
        public float TotalHeight { get; private set; }

        /// <summary>
        /// Build a height cache from a list of item heights.
        /// Call this when the list changes (items added/removed or heights changed),
        /// not every frame.
        /// </summary>
        public static HeightCache Build(System.Collections.Generic.IReadOnlyList<float> heights)
        {
            if (heights == null)
                throw new ArgumentNullException(nameof(heights));

            int count = heights.Count;

            var cumulative = new float[count + 1];
            var heightsCopy = new float[count];

            cumulative[0] = 0f;

            for (int i = 0; i < count; i++)
            {
                float h = heights[i];
                heightsCopy[i] = h;
                cumulative[i + 1] = cumulative[i] + h;
            }

            return new HeightCache
            {
                Heights = heightsCopy,
                CumulativeHeights = cumulative,
                TotalHeight = cumulative[count]
            };
        }

        /// <summary>
        /// Returns the insertion index for a given Y position in local coordinates.
        /// localY = 0 at the top, increases downward.
        /// Semantics: returns the segment index containing localY.
        /// </summary>
        /// <param name="localY">Y coordinate relative to the top of the list, after scroll offset is applied.</param>
        public int GetInsertionIndex(float localY)
        {
            if (Heights == null || CumulativeHeights == null || Heights.Length == 0)
                return 0;

            // Clamp outside bounds.
            if (localY <= 0f)
                return 0;

            if (localY >= TotalHeight)
                return Heights.Length;

            // BinarySearch on cumulative heights.
            // CumulativeHeights = [0, h0, h0+h1, ..., total]
            int index = System.Array.BinarySearch(CumulativeHeights, localY);

            if (index < 0)
            {
                // ~index is the insertion point where localY would be inserted
                // to keep the array sorted. The item segment index is insertionPoint - 1.
                int insertionPoint = ~index;
                index = insertionPoint - 1;
            }

            // index is the item segment we are over, which works as insertion index.
            return Mathf.Clamp(index, 0, Heights.Length);
        }
    }
}