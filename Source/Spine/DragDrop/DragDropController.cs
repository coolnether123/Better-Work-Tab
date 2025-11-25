using Spine.DragDropApi.Util;
using Spine.DragDropApi;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spine.DragDropApi
{
    /// <summary>
    /// High-level controller that coordinates drag-drop operations for a list.
    /// Wraps DragDropManager and ListDragHelpers, so caller code stays small.
    /// </summary>
    public class DragDropController<T>
    {
        private readonly DragDropManager<T> _manager;
        private readonly Func<Vector2, int> _targetIndexCalculator;

        public DragDropController(Func<Vector2, int> targetIndexCalculator)
        {
            _manager = new DragDropManager<T>();
            _targetIndexCalculator = targetIndexCalculator ?? throw new ArgumentNullException(nameof(targetIndexCalculator));
        }

        /// <summary>
        /// True if a drag is active.
        /// </summary>
        public bool IsActive => _manager.IsActive;

        /// <summary>
        /// Current drag session, or null if none.
        /// </summary>
        public ListDragSession<T> CurrentSession => _manager.CurrentSession;

        /// <summary>
        /// Forwarded events from the inner manager.
        /// </summary>
        public event Action<ListDragSession<T>> OnDragStarted
        {
            add => _manager.OnDragStarted += value;
            remove => _manager.OnDragStarted -= value;
        }

        public event Action<Vector2> OnDragPositionChanged
        {
            add => _manager.OnDragPositionChanged += value;
            remove => _manager.OnDragPositionChanged -= value;
        }

        public event Action<int> OnInsertionIndexChanged
        {
            add => _manager.OnInsertionIndexChanged += value;
            remove => _manager.OnInsertionIndexChanged -= value;
        }

        public event Action<DragEndReason> OnDragEnded
        {
            add => _manager.OnDragEnded += value;
            remove => _manager.OnDragEnded -= value;
        }

        /// <summary>
        /// Try to start a drag for the item at itemIndex.
        /// Returns true if drag started, false if validation failed.
        /// </summary>
        /// <param name="item">Item to drag.</param>
        /// <param name="itemIndex">Index in the backing list.</param>
        /// <param name="listCount">Total count of items in the list.</param>
        /// <param name="startPos">Mouse position at drag start.</param>
        public bool TryStartDrag(T item, int itemIndex, int listCount, Vector2 startPos)
        {
            try
            {
                _manager.StartDrag(item, itemIndex, startPos, listCount);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Update the current drag based on mouse position.
        /// Safe to call every frame; does nothing if no drag is active.
        /// </summary>
        public void UpdateDrag(Vector2 mousePos)
        {
            if (!IsActive)
                return;

            _manager.UpdateDrag(mousePos, _targetIndexCalculator);
        }

        /// <summary>
        /// Apply auto-scroll for the list while dragging.
        /// This calls ListDragHelpers.ApplyAutoScroll internally.
        /// Safe to call every frame.
        /// </summary>
        public void ApplyAutoScroll(
            ref Vector2 scrollPosition,
            Vector2 mousePos,
            Rect listRect,
            float contentHeight,
            float deltaTime,
            float scrollSpeed = 300f)
        {
            if (!IsActive)
                return;

            ListDragHelpers.ApplyAutoScroll(
                ref scrollPosition,
                mousePos,
                listRect,
                deltaTime,
                contentHeight,
                scrollSpeed);
        }

        /// <summary>
        /// Finalize the current drag by reordering the given list.
        /// Returns the DragEndReason (Success / Cancelled).
        /// </summary>
        /// <param name="items">Backing list to reorder.</param>
        /// <param name="visualToSourceMapping">
        /// Optional mapping from visual index to source index.
        /// Needed if the visual order differs from the backing list.
        /// </param>
        public DragEndReason FinalizeDrag(
            List<T> items,
            Func<int, int> visualToSourceMapping = null)
        {
            if (!IsActive)
                return DragEndReason.Cancelled;

            var session = _manager.CurrentSession;

            bool moved = ListDragHelpers.TryFinalizeListReorder(
                items,
                session.DraggedItem,
                session.SourceIndex,
                session.TargetIndex,
                visualToSourceMapping);

            var reason = moved ? DragEndReason.Success : DragEndReason.Cancelled;
            _manager.EndDrag(reason);

            return reason;
        }

        /// <summary>
        /// Cancel the current drag without changing the list.
        /// </summary>
        public void CancelDrag()
        {
            if (IsActive)
                _manager.CancelDrag();
        }
    }
}