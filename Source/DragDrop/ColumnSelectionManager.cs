using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    public static class ColumnSelectionManager
    {
        private static readonly HashSet<PawnColumnDef> _selectedColumns = new HashSet<PawnColumnDef>();

        public static bool HasSelection => _selectedColumns.Count > 0;
        public static int SelectionCount => _selectedColumns.Count;

        public static void Clear()
        {
            _selectedColumns.Clear();
        }

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

        public static void Select(PawnColumnDef col)
        {
            if (col == null) return;
            _selectedColumns.Add(col);
        }

        public static bool IsSelected(PawnColumnDef col)
        {
            return col != null && _selectedColumns.Contains(col);
        }

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
