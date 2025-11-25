namespace Spine.DragDropApi
{
    /// <summary>
    /// Outcome of a drag operation.
    /// </summary>
    public enum DragEndReason
    {
        /// <summary>
        /// Item was reordered successfully.
        /// </summary>
        Success,

        /// <summary>
        /// Drag ended without a reorder (e.g. dropped back on original spot, or explicitly cancelled).
        /// </summary>
        Cancelled,

        /// <summary>
        /// Drag ended due to a failed precondition or validation error.
        /// </summary>
        Failed
    }
}