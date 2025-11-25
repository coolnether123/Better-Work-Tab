using UnityEngine;

namespace Spine.DragDropApi
{
    /// <summary>
    /// Represents the state of a single active drag session.
    /// This object is created when dragging starts and cleared when dragging ends.
    /// </summary>
    public sealed class ListDragSession<T>
    {
        /// <summary>
        /// The item being dragged.
        /// </summary>
        public T DraggedItem { get; internal set; }

        /// <summary>
        /// Original index in the backing list when the drag started.
        /// </summary>
        public int SourceIndex { get; internal set; }

        /// <summary>
        /// Current target index for insertion, as calculated by the target index strategy.
        /// </summary>
        public int TargetIndex { get; internal set; }

        /// <summary>
        /// Current mouse position in screen space.
        /// </summary>
        public Vector2 MousePosition { get; internal set; }
    }
}