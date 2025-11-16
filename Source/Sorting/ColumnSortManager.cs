using Better_Work_Tab.Input;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Sorting
{
    /// <summary>
    /// Manages column sorting when headers are clicked.
    /// Single responsibility: Apply sort logic to pawn table.
    /// </summary>
    public class ColumnSortManager
    {
        private readonly SortState _state = new SortState();

        public SortState State => _state;

        public bool HandleHeaderClick(InputState input, WorkTabLayoutColumn column, IWorkTabLayoutController layout)
        {
            if (column.Column == null || layout?.Table == null) return false;

            // Toggle sort
            _state.ToggleSort(column.Column);

            // Apply sort to table
            ApplySort(layout.Table);

            // Play audio feedback
            VisualFeedback.AudioManager.PlayClick();

            // Trigger table refresh
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();

            return true;
        }

        private void ApplySort(PawnTable table)
        {
            if (_state.SortColumn == null || table == null) return;

            // Get current pawns
            var pawns = table.PawnsListForReading.ToList();

            // Sort using column's comparator
            pawns.Sort((a, b) =>
            {
                int comparison = _state.SortColumn.Worker.Compare(a, b);
                return _state.Descending ? -comparison : comparison;
            });

            // Rebuild table with new order (requires reflection or custom table implementation)
            // For now, we update displayOrder to force re-sort
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i]?.playerSettings != null)
                {
                    pawns[i].playerSettings.displayOrder = i;
                }
            }

            table.SetDirty();
        }

        public void ClearSort()
        {
            _state.ClearSort();
        }
    }
}
