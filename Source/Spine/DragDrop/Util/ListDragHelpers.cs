using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spine.DragDropApi.Util
{
    /// <summary>
    /// Helpers for list reordering and auto-scroll.
    /// These operate on data and scroll positions, not rendering.
    /// </summary>
    public static class ListDragHelpers
    {
        /// <summary>
        /// Reorders items based on the result of a drag.
        /// Returns true if the item moved to a new index, false if nothing changed.
        ///
        /// This version uses the known source index to avoid list scans for performance.
        /// </summary>
        /// <typeparam name="T">Item type in the list.</typeparam>
        /// <param name="items">Backing list to reorder.</param>
        /// <param name="draggedItem">Item that was dragged (for sanity check and clarity).</param>
        /// <param name="sourceIndex">Original index of the dragged item in the list.</param>
        /// <param name="targetIndexInVisualList">Target index from the visual list (insertion slot).</param>
        /// <param name="visualToSourceMapping">
        /// Optional mapping from visual index to source index.
        /// Use this if your visual order is different from the backing list order.
        /// </param>
        public static bool TryFinalizeListReorder<T>(
            List<T> items,
            T draggedItem,
            int sourceIndex,
            int targetIndexInVisualList,
            Func<int, int> visualToSourceMapping = null)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            if (sourceIndex < 0 || sourceIndex >= items.Count)
                throw new ArgumentOutOfRangeException(nameof(sourceIndex));

            // Optional sanity check: the dragged item should match the source index.
            // You can comment this out if you want the last bit of speed and you trust callers.
            if (!EqualityComparer<T>.Default.Equals(items[sourceIndex], draggedItem))
            {
                // Caller passed inconsistent data.
                return false;
            }

            int finalTargetIndex;

            if (visualToSourceMapping != null)
            {
                if (targetIndexInVisualList >= items.Count)
                {
                    finalTargetIndex = items.Count;
                }
                else if (targetIndexInVisualList < 0)
                {
                    finalTargetIndex = 0;
                }
                else
                {
                    finalTargetIndex = visualToSourceMapping(targetIndexInVisualList);
                }
            }
            else
            {
                finalTargetIndex = Mathf.Clamp(targetIndexInVisualList, 0, items.Count);
            }

            // No move: target is the same slot as the source.
            if (finalTargetIndex == sourceIndex)
                return false;

            // Pull the item out and reinsert it.
            T item = items[sourceIndex];
            items.RemoveAt(sourceIndex);

            // If we removed an element before the target, the target shifts down by one.
            if (sourceIndex < finalTargetIndex)
                finalTargetIndex--;

            finalTargetIndex = Mathf.Clamp(finalTargetIndex, 0, items.Count);
            items.Insert(finalTargetIndex, item);

            return true;
        }

        /// <summary>
        /// Applies vertical auto-scroll when the mouse is near the top or bottom of the list rect.
        /// Modifies scrollPos in-place.
        /// </summary>
        /// <param name="scrollPos">Current scroll position (modified by ref).</param>
        /// <param name="mousePos">Mouse position in screen space.</param>
        /// <param name="listScreenRect">Screen-space rect of the list.</param>
        /// <param name="deltaTime">Time since last frame.</param>
        /// <param name="contentHeight">Total height of the scroll content.</param>
        /// <param name="scrollSpeed">Scroll speed in pixels per second.</param>
        public static void ApplyAutoScroll(
            ref Vector2 scrollPos,
            Vector2 mousePos,
            Rect listScreenRect,
            float deltaTime,
            float contentHeight,
            float scrollSpeed = 300f)
        {
            if (contentHeight <= listScreenRect.height)
                return;

            float zoneSize = 25f; // Height of the "scroll zone" at top and bottom.
            float maxY = Mathf.Max(0f, contentHeight - listScreenRect.height);

            // Top zone
            if (mousePos.y < listScreenRect.y + zoneSize)
            {
                scrollPos.y -= deltaTime * scrollSpeed;
            }
            // Bottom zone
            else if (mousePos.y > listScreenRect.yMax - zoneSize)
            {
                scrollPos.y += deltaTime * scrollSpeed;
            }

            scrollPos.y = Mathf.Clamp(scrollPos.y, 0f, maxY);
        }
    }
}