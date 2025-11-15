using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.API
{
    /// <summary>
    /// Unified API for querying pawn rows and work type columns in the Work tab.
    /// Abstracts away PawnTable geometry, scrolling, and screen-space calculations.
    /// </summary>
    public interface IWorkTabRowColumnAPI
    {
        // --- ROW QUERIES ---

        /// <summary>
        /// Get all visible pawns in draw order, respecting scroll and filtering.
        /// </summary>
        List<Pawn> GetVisiblePawns();

        /// <summary>
        /// Get the screen-space rect for a pawn's row, accounting for scroll offset.
        /// Returns null if row is not currently visible.
        /// </summary>
        Rect? GetPawnRowRect(Pawn pawn);

        /// <summary>
        /// Find the pawn at screen-space mouse position, if any.
        /// Useful for click handlers.
        /// </summary>
        Pawn GetPawnAtMousePosition(Vector2 mousePos);

        /// <summary>
        /// Get the visual index (0-based draw order) of a pawn in the current table.
        /// </summary>
        int GetPawnRowIndex(Pawn pawn);

        /// <summary>
        /// Get the currently selected pawn in the game, if any.
        /// Returns null if no pawn is selected or if the single selected item is not a pawn.
        /// </summary>
        Pawn GetSelectedPawn();

        // --- COLUMN QUERIES ---

        /// <summary>
        /// Get all work type columns visible in the current table order.
        /// </summary>
        List<PawnColumnDef> GetWorkTypeColumns();

        /// <summary>
        /// Get the screen-space rect for a work type column header.
        /// </summary>
        Rect? GetWorkTypeColumnRect(WorkTypeDef wt);

        /// <summary>
        /// Find the work type at screen-space mouse position (header area).
        /// </summary>
        WorkTypeDef GetWorkTypeAtMousePosition(Vector2 mousePos);

        // --- TABLE STATE ---

        /// <summary>
        /// Get the underlying PawnTable instance.
        /// Use sparingly; prefer specific query methods above.
        /// </summary>
        PawnTable GetTable();

        /// <summary>
        /// Get the origin (top-left) position of the table in screen space.
        /// </summary>
        Vector2 GetTableOrigin();

        /// <summary>
        /// Get the total visible area (width, height) of the table.
        /// </summary>
        Vector2 GetTableSize();

        /// <summary>
        /// Get the current scroll offset (e.g., for manual geometry adjustments).
        /// </summary>
        Vector2 GetScrollPosition();

        /// <summary>
        /// Get header height (for separating header from rows).
        /// </summary>
        float GetHeaderHeight();
    }

    /// <summary>
    /// Concrete implementation bound to a specific PawnTable instance.
    /// </summary>
    public class WorkTabRowColumnAPI : IWorkTabRowColumnAPI
    {
        private readonly PawnTable _table;
        private readonly Vector2 _origin;

        public WorkTabRowColumnAPI(PawnTable table, Vector2 origin)
        {
            _table = table;
            _origin = origin;
        }

        public List<Pawn> GetVisiblePawns() => _table.cachedPawns;

        public Rect? GetPawnRowRect(Pawn pawn)
        {
            int idx = _table.cachedPawns.IndexOf(pawn);
            if (idx < 0) return null;

            float currentY = _origin.y + _table.cachedHeaderHeight - _table.scrollPosition.y;
            for (int i = 0; i < idx; i++)
                currentY += _table.cachedRowHeights[i];

            return new Rect(
                _origin.x,
                currentY,
                _table.Size.x - 16f, // Account for scrollbar
                _table.cachedRowHeights[idx]
            );
        }

        public Pawn GetPawnAtMousePosition(Vector2 mousePos)
        {
            float currentY = _origin.y + _table.cachedHeaderHeight - _table.scrollPosition.y;
            for (int i = 0; i < _table.cachedPawns.Count; i++)
            {
                float rowHeight = _table.cachedRowHeights[i];
                var rowRect = new Rect(_origin.x, currentY, _table.Size.x - 16f, rowHeight);
                
                if (rowRect.Contains(mousePos))
                    return _table.cachedPawns[i];
                
                currentY += rowHeight;
            }
            return null;
        }

        public int GetPawnRowIndex(Pawn pawn) => _table.cachedPawns.IndexOf(pawn);

        public Pawn GetSelectedPawn()
        {
            if (Find.Selector.SingleSelectedThing is Pawn pawn)
            {
                return pawn;
            }
            return null;
        }

        public List<PawnColumnDef> GetWorkTypeColumns()
            => _table.Columns.Where(c => c.Worker is PawnColumnWorker_WorkPriority).ToList();

        public Rect? GetWorkTypeColumnRect(WorkTypeDef wt)
        {
            float xAccum = _origin.x;
            foreach (var col in _table.Columns)
            {
                float width = col == _table.Columns.Last()
                    ? (_table.Size.x - 16f - (xAccum - _origin.x))
                    : _table.cachedColumnWidths[_table.Columns.IndexOf(col)];

                if (col.Worker is PawnColumnWorker_WorkPriority && col.workType == wt)
                    return new Rect(xAccum, _origin.y, width, _table.HeaderHeight);

                xAccum += width;
            }
            return null;
        }

        public WorkTypeDef GetWorkTypeAtMousePosition(Vector2 mousePos)
        {
            float xAccum = _origin.x;
            foreach (var col in _table.Columns)
            {
                float width = col == _table.Columns.Last()
                    ? (_table.Size.x - 16f - (xAccum - _origin.x))
                    : _table.cachedColumnWidths[_table.Columns.IndexOf(col)];

                var rect = new Rect(xAccum, _origin.y, width, _table.HeaderHeight);
                if (rect.Contains(mousePos) && col.Worker is PawnColumnWorker_WorkPriority)
                    return col.workType;

                xAccum += width;
            }
            return null;
        }

        public PawnTable GetTable() => _table;
        public Vector2 GetTableOrigin() => _origin;
        public Vector2 GetTableSize() => _table.Size;
        public Vector2 GetScrollPosition() => _table.scrollPosition;
        public float GetHeaderHeight() => _table.cachedHeaderHeight;
    }
}
