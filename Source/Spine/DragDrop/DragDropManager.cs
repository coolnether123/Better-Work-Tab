using System;
using UnityEngine;

namespace Spine.DragDropApi
{
    /// <summary>
    /// Core drag-drop state manager.
    /// Tracks the active drag session and exposes events for changes.
    /// Does not reorder lists by itself.
    /// </summary>
    public class DragDropManager<T>
    {
        private ListDragSession<T> _session;
        private DragEndReason _lastEndReason;

        /// <summary>
        /// Current active drag session, or null if no drag is active.
        /// </summary>
        public ListDragSession<T> CurrentSession => _session;

        /// <summary>
        /// True if a drag session is active.
        /// </summary>
        public bool IsActive => _session != null;

        /// <summary>
        /// Reason for the last drag end.
        /// </summary>
        public DragEndReason LastEndReason => _lastEndReason;

        // Events for observers.
        public event Action<ListDragSession<T>> OnDragStarted;
        public event Action<Vector2> OnDragPositionChanged;
        public event Action<int> OnInsertionIndexChanged;
        public event Action<DragEndReason> OnDragEnded;

        /// <summary>
        /// Start dragging an item.
        /// </summary>
        /// <param name="item">Item to drag. Must not be null for reference types.</param>
        /// <param name="sourceIndex">Index of the item in the backing list.</param>
        /// <param name="startPos">Mouse position at drag start (screen space).</param>
        /// <param name="itemCount">Total number of items in the backing list.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if sourceIndex is outside [0, itemCount).</exception>
        /// <exception cref="ArgumentNullException">Thrown if item is null (for reference types).</exception>
        public void StartDrag(T item, int sourceIndex, Vector2 startPos, int itemCount)
        {
            if (sourceIndex < 0 || sourceIndex >= itemCount)
                throw new ArgumentOutOfRangeException(nameof(sourceIndex), "Source index is out of range.");

            if (itemCount < 0)
                throw new ArgumentOutOfRangeException(nameof(itemCount));

            if (item is null)
                throw new ArgumentNullException(nameof(item));

            _session = new ListDragSession<T>
            {
                DraggedItem = item,
                SourceIndex = sourceIndex,
                TargetIndex = sourceIndex,
                MousePosition = startPos
            };

            OnDragStarted?.Invoke(_session);
        }

        /// <summary>
        /// Update drag position and target index.
        /// This should be called every frame while dragging.
        /// </summary>
        /// <param name="mousePos">Current mouse position in screen space.</param>
        /// <param name="calculateTargetIndex">
        /// Function that takes the current mouse position and returns the target insertion index.
        /// This function should clamp the index to a valid range.
        /// </param>
        /// <exception cref="InvalidOperationException">Thrown if no drag is active.</exception>
        /// <exception cref="ArgumentNullException">Thrown if calculateTargetIndex is null.</exception>
        public void UpdateDrag(Vector2 mousePos, Func<Vector2, int> calculateTargetIndex)
        {
            if (!IsActive)
                throw new InvalidOperationException("Cannot update drag: no active drag session.");

            if (calculateTargetIndex == null)
                throw new ArgumentNullException(nameof(calculateTargetIndex));

            _session.MousePosition = mousePos;
            OnDragPositionChanged?.Invoke(mousePos);

            int oldTarget = _session.TargetIndex;
            int newTarget = calculateTargetIndex(mousePos);

            if (newTarget != oldTarget)
            {
                _session.TargetIndex = newTarget;
                OnInsertionIndexChanged?.Invoke(newTarget);
            }
        }

        /// <summary>
        /// End the current drag session and record the reason.
        /// Raises OnDragEnded.
        /// </summary>
        /// <param name="reason">Outcome of the drag.</param>
        public void EndDrag(DragEndReason reason = DragEndReason.Cancelled)
        {
            _lastEndReason = reason;

            if (_session != null)
            {
                _session = null;
            }

            OnDragEnded?.Invoke(reason);
        }

        /// <summary>
        /// Cancel the current drag session without reordering.
        /// Throws if no drag is active.
        /// </summary>
        public void CancelDrag()
        {
            if (!IsActive)
                throw new InvalidOperationException("Cannot cancel drag: no active drag session.");

            EndDrag(DragEndReason.Cancelled);
        }
    }
}