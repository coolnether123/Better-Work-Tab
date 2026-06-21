using Better_Work_Tab.PawnOrganizer.Data;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Represents the state of an active row drag operation.
    /// 
    /// This class holds only stable game object references (Pawn/Divider), never UI wrappers,
    /// so drag state survives layout rebuilds that recreate UI rows each frame.
    /// </summary>
    public sealed class RowDragSession
    {
        /// <summary>
        /// The pawn being dragged. Null when dragging a divider.
        /// </summary>
        public Pawn DraggedPawn { get; }
        
        /// <summary>
        /// The divider being dragged. Null when dragging a pawn.
        /// </summary>
        public PawnDivider DraggedDivider { get; }

        /// <summary>
        /// Mouse position when the drag started, used for ghost offset calculation.
        /// </summary>
        public Vector2 StartMousePosition { get; }
        
        /// <summary>
        /// Visual index where the item started, used for no-op detection.
        /// </summary>
        public int StartVisualIndex { get; }
        
        /// <summary>
        /// Current target insertion index, updated each frame during drag.
        /// </summary>
        public int TargetIndex { get; set; }
        
        /// <summary>
        /// Original screen rect of the dragged row, used for ghost rendering.
        /// </summary>
        public Rect OriginalRect { get; }
        
        /// <summary>
        /// Y offset from mouse to top of original rect so the ghost stays anchored under the cursor.
        /// </summary>
        public float DragOffsetY { get; }

        /// <summary>
        /// Create a drag session for a pawn.
        /// </summary>
        public RowDragSession(Pawn pawn, int visualIndex, Vector2 startMouse, Rect originalRect)
        {
            DraggedPawn = pawn;
            DraggedDivider = null;
            StartVisualIndex = visualIndex;
            TargetIndex = visualIndex;
            StartMousePosition = startMouse;
            OriginalRect = originalRect;
            DragOffsetY = startMouse.y - originalRect.y;
        }

        /// <summary>
        /// Create a drag session for a divider.
        /// </summary>
        public RowDragSession(PawnDivider divider, int visualIndex, Vector2 startMouse, Rect originalRect)
        {
            DraggedPawn = null;
            DraggedDivider = divider;
            StartVisualIndex = visualIndex;
            TargetIndex = visualIndex;
            StartMousePosition = startMouse;
            OriginalRect = originalRect;
            DragOffsetY = startMouse.y - originalRect.y;
        }

        /// <summary>
        /// Returns true if the dragged item still exists and is valid. Pawns can be destroyed mid-drag.
        /// </summary>
        public bool IsValid()
        {
            if (DraggedPawn != null)
            {
#if vAlpha4
                return true;
#else
                return !DraggedPawn.Destroyed;
#endif
            }
            
            // Dividers stay valid unless the reference is null (they are owned by the worklist).
            return DraggedDivider != null;
        }

        /// <summary>
        /// True when dragging a pawn instead of a divider.
        /// </summary>
        public bool IsDraggingPawn => DraggedPawn != null;
        
        /// <summary>
        /// True when dragging a divider instead of a pawn.
        /// </summary>
        public bool IsDraggingDivider => DraggedDivider != null;
        
        /// <summary>
        /// Description for debug logging.
        /// </summary>
        public string Describe()
        {
            if (DraggedPawn != null)
            {
                return $"Pawn '{PawnCompat.LabelShortCap(DraggedPawn)}'";
            }
            
            if (DraggedDivider != null)
            {
                return $"Divider '{DraggedDivider.DividerName ?? "Unnamed"}'";
            }
            
            return "Invalid session";
        }
        
        /// <summary>
        /// Get the display label for ghost rendering.
        /// </summary>
        public string GetDisplayLabel()
        {
            if (DraggedPawn != null)
            {
                return PawnCompat.LabelShortCap(DraggedPawn);
            }
            
            if (DraggedDivider != null)
            {
                return DraggedDivider.DividerName ?? "Divider";
            }
            
            return "Unknown";
        }
    }
}
