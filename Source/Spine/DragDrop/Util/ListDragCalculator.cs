using System;
using System.Collections.Generic;

namespace Spine.DragDropApi.Util
{
    /// <summary>
    /// Pure math utilities for drag calculations.
    /// Does not touch Unity / RimWorld APIs.
    /// </summary>
    public static class ListDragCalculator
    {
        /// <summary>
        /// Calculate insertion index using a HeightCache for best performance.
        /// Semantics: items are vertical segments stacked from top to bottom,
        /// and the returned index is the index of the segment containing localY.
        /// </summary>
        public static int CalculateInsertionIndex(
            HeightCache cache,
            float mouseY,
            float listScreenY,
            float scrollOffsetY)
        {
            float localY = mouseY - listScreenY + scrollOffsetY;
            return cache.GetInsertionIndex(localY);
        }

        /// <summary>
        /// Calculate insertion index using a simple per-item height list.
        /// Semantics match HeightCache: we treat items as stacked segments and
        /// return the segment index containing localY.
        /// </summary>
        public static int CalculateInsertionIndex(
            IList<float> itemHeights,
            float mouseY,
            float listScreenY,
            float scrollOffsetY)
        {
            if (itemHeights == null || itemHeights.Count == 0)
                return 0;

            float localY = mouseY - listScreenY + scrollOffsetY;
            float accumulated = 0f;

            for (int i = 0; i < itemHeights.Count; i++)
            {
                float nextEdge = accumulated + itemHeights[i];

                // If we're above the bottom of this segment, we're inside this item.
                if (localY < nextEdge)
                    return i;

                accumulated = nextEdge;
            }

            // Below all items: insertion after the last.
            return itemHeights.Count;
        }

        /// <summary>
        /// Calculate insertion index using a dynamic height provider.
        /// Semantics match the HeightCache and per-list overload.
        /// </summary>
        public static int CalculateInsertionIndex(
            int itemCount,
            float mouseY,
            float listScreenY,
            float scrollOffsetY,
            Func<int, float> getItemHeight)
        {
            if (itemCount <= 0)
                return 0;

            if (getItemHeight == null)
                throw new ArgumentNullException(nameof(getItemHeight));

            float localY = mouseY - listScreenY + scrollOffsetY;
            float accumulated = 0f;

            for (int i = 0; i < itemCount; i++)
            {
                float h = getItemHeight(i);
                float nextEdge = accumulated + h;

                if (localY < nextEdge)
                    return i;

                accumulated = nextEdge;
            }

            return itemCount;
        }
    }
}