using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Manages multi-column selection for drag operations (Shift+Click).
    /// Uses a HashSet to track which work columns are currently part of a selected group.
    /// Selection is primarily used to move blocks of columns together in the work tab.
    /// </summary>
    public static class ColumnSelectionManager
    {
        private static readonly HashSet<PawnColumnDef> _selectedColumns = new HashSet<PawnColumnDef>();

        /// <summary>
        /// Gets whether any columns are currently selected.
        /// </summary>
        public static bool HasSelection => _selectedColumns.Count > 0;

        /// <summary>
        /// Gets the number of columns currently selected.
        /// </summary>
        public static int SelectionCount => _selectedColumns.Count;

        /// <summary>
        /// Clears the selection entirely.
        /// </summary>
        public static void Clear()
        {
            _selectedColumns.Clear();
        }

        /// <summary>
        /// Toggles the selection state of a specific column.
        /// </summary>
        /// <param name="col">The column definition to toggle.</param>
        public static void ToggleSelection(PawnColumnDef col)
        {
            if (col == null) return;
            if (_selectedColumns.Contains(col))
            {
                _selectedColumns.Remove(col);
            }
            else
            {
                _selectedColumns.Add(col);
            }
        }

        /// <summary>
        /// Forces a column to be selected.
        /// </summary>
        /// <param name="col">The column definition to select.</param>
        public static void Select(PawnColumnDef col)
        {
            if (col == null) return;
            _selectedColumns.Add(col);
        }

        /// <summary>
        /// Checks if a column is currently part of the selection.
        /// </summary>
        public static bool IsSelected(PawnColumnDef col)
        {
            return col != null && _selectedColumns.Contains(col);
        }

        /// <summary>
        /// Returns the currently selected columns in their visual order.
        /// </summary>
        /// <param name="allColumns">The full list of columns in their current order.</param>
        /// <returns>A list of selected columns sorted by their position in <paramref name="allColumns"/>.</returns>
        public static List<PawnColumnDef> GetSelectedInOrder(IEnumerable<PawnColumnDef> allColumns)
        {
            var result = new List<PawnColumnDef>();
            foreach (var col in allColumns)
            {
                if (IsSelected(col))
                {
                    result.Add(col);
                }
            }
            return result;
        }
    }
}
