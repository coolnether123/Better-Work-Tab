using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Holds all state for a single drag-and-drop operation on a specific PawnTable.
    /// An instance of this class is created for each table that supports dragging.
    /// This replaces the old global static WorkTabDragState.
    /// </summary>
    internal class DragSession
    {
        internal enum DragKind { None, Column, Row }

        // Core State
        internal DragKind Kind = DragKind.None;
        internal Vector2 MouseStart;

        // Item-specific state
        internal int FromIndex = -1;
        internal int ToIndex = -1;
        internal object DraggedItem = null; // Can be PawnColumnDef or Pawn

        // Original geometry for drawing the "gap"
        internal Rect OriginRect;
        internal Vector2 DragOffset = Vector2.zero;

        // --- PERFORMANCE CACHE ---
        // This geometry is calculated ONCE when the drag begins, not every frame.
        public List<Rect> CachedTargetRects;
        public List<float> CachedTargetBoundaries; // For fast insertion index lookup

        /// <summary>
        /// Resets the session to its default state, ready for a new drag.
        /// </summary>
        internal void Reset()
        {
            Kind = DragKind.None;
            FromIndex = -1;
            ToIndex = -1;
            DraggedItem = null;
            OriginRect = default;
            DragOffset = Vector2.zero;
            CachedTargetRects?.Clear();
            CachedTargetBoundaries?.Clear();
        }

        internal bool IsDragging() => Kind != DragKind.None;
    }
}