using System.Collections.Generic;
using Better_Work_Tab.PawnOrganizer.Data;
using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.UI.WorkGrid.Snapshots;

namespace Better_Work_Tab.PawnOrganizer.API
{
    /// <summary>
    /// Central contract for managing the Work tab layout. Implementations own the
    /// pawn/divider ordering, column layout, row hit-testing and drag persistence.
    /// </summary>
    public interface IWorkTabLayoutController
    {
        /// <summary>
        /// Cached row descriptors (pawns + dividers). Rebuilds on demand if marked invalid.
        /// </summary>
        List<RowDescriptor> GetRowDescriptors();

        /// <summary>
        /// Mark row descriptors as stale; they will rebuild on next GetRowDescriptors() call.
        /// </summary>
        void InvalidateRowDescriptors();

        /// <summary>
        /// Latest snapshot of rows (pawns + dividers) in draw order.
        /// </summary>
        IList<WorkTabLayoutRow> Rows { get; }

        /// <summary>
        /// Latest snapshot of column geometry in draw order.
        /// </summary>
        IList<WorkTabLayoutColumn> Columns { get; }

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
        /// Latest PawnTable instance that backed the rebuild.
        /// </summary>
        PawnTable Table { get; }

        /// <summary>
        /// Monotonically increases whenever row or column geometry is rebuilt.
        /// Consumers can retain derived geometry until this value changes.
        /// </summary>
        int LayoutRevision { get; }

        WorkGridGeometrySnapshot GeometrySnapshot { get; }

        /// <summary>
        /// Rebuild row + column snapshots for the provided PawnTable snapshot combination.
        /// </summary>
        void Rebuild(PawnTable table, IPawnOrganizerSnapshot snapshot, Vector2 origin);

        /// <summary>
        /// Hit test helper for rows in screen space.
        /// </summary>
        bool TryGetRowAt(Vector2 mousePosition, out WorkTabLayoutRow row);

        bool TryGetVisibleRowAt(Vector2 mousePosition, out WorkTabLayoutRow row);

        /// <summary>
        /// Hit test helper for columns in screen space (header area only).
        /// </summary>
        bool TryGetColumnAt(Vector2 mousePosition, out WorkTabLayoutColumn column);

        bool TryGetBodyColumnAt(Vector2 mousePosition, out WorkTabLayoutColumn column);

        float GetPinnedRowsHeight();

        void ClearGeometrySnapshot();

        /// <summary>
        /// Insert a new divider below the provided pawn inside the cached snapshot.
        /// </summary>
        PawnDivider AddDividerAfterPawn(Pawn pawn, string label, Color color);

        /// <summary>
        /// Insert a new divider above the provided pawn inside the cached snapshot.
        /// </summary>
        PawnDivider AddDividerBeforePawn(Pawn pawn, string label, Color color);

        /// <summary>
        /// Remove the provided divider from the cached snapshot.
        /// </summary>
        void RemoveDivider(PawnDivider divider);

        /// <summary>
        /// Convert a row snapshot into a screen-space rect (after scroll).
        /// </summary>
        Rect GetScreenRect(WorkTabLayoutRow row);
    }
}
