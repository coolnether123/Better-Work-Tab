using System.Collections.Generic;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.PawnOrganizer.Data;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.API
{
    /// <summary>
    /// Central contract for managing the Work tab layout. Implementations own the
    /// pawn/divider ordering, column layout, row hit-testing and drag persistence.
    /// </summary>
    public interface IWorkTabLayoutController
    {
        /// <summary>
        /// Latest snapshot of rows (pawns + dividers) in draw order.
        /// </summary>
        IReadOnlyList<WorkTabLayoutRow> Rows { get; }

        /// <summary>
        /// Latest snapshot of column geometry in draw order.
        /// </summary>
        IReadOnlyList<WorkTabLayoutColumn> Columns { get; }

        /// <summary>
        /// Height of the content region (without headers) after layout.
        /// Used for scroll view sizing.
        /// </summary>
        float ContentHeight { get; }

        /// <summary>
        /// Origin of the PawnTable on screen. Required when converting into screen rects.
        /// </summary>
        Vector2 TableOrigin { get; }

        /// <summary>
        /// Cached header height retrieved from the PawnTable.
        /// </summary>
        float HeaderHeight { get; }

        /// <summary>
        /// Divider row height in pixels.
        /// </summary>
        float DividerHeight { get; }

        /// <summary>
        /// Latest PawnTable instance that backed the rebuild.
        /// </summary>
        PawnTable Table { get; }

        /// <summary>
        /// Rebuild row + column snapshots for the provided PawnTable/worklist combination.
        /// </summary>
        void Rebuild(PawnTable table, Worklist worklist, Vector2 origin);

        /// <summary>
        /// Hit test helper for rows in screen space.
        /// </summary>
        bool TryGetRowAt(Vector2 mousePosition, out WorkTabLayoutRow row);

        /// <summary>
        /// Hit test helper for columns in screen space (header area only).
        /// </summary>
        bool TryGetColumnAt(Vector2 mousePosition, out WorkTabLayoutColumn column);

        /// <summary>
        /// Insert a new divider and rebuild layout on next frame.
        /// </summary>
        PawnDivider InsertDividerAfter(Pawn pawn, string label);

        /// <summary>
        /// Remove the provided divider from the worklist.
        /// </summary>
        void RemoveDivider(PawnDivider divider);

        /// <summary>
        /// Update divider display name.
        /// </summary>
        void RenameDivider(PawnDivider divider, string newLabel);

        /// <summary>
        /// Move the provided element (pawn or divider) to a visual slot.
        /// Used by drag-and-drop handlers prior to calling Rebuild.
        /// </summary>
        void MoveElement(DisplayElement element, int targetIndex);

        /// <summary>
        /// Convert a row snapshot into a screen-space rect (after scroll).
        /// </summary>
        Rect GetScreenRect(WorkTabLayoutRow row);
    }
}
