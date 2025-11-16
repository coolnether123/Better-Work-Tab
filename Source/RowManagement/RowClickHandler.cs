using Better_Work_Tab.Input;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.Selection;
using System.Linq;

namespace Better_Work_Tab.RowManagement
{
    /// <summary>
    /// Handles single-click and double-click events on rows.
    /// Single responsibility: Process row click events.
    /// </summary>
    public class RowClickHandler
    {
        private readonly PawnSelectionManager _selectionManager;

        public RowClickHandler(PawnSelectionManager selectionManager)
        {
            _selectionManager = selectionManager;
        }

        public bool HandleClick(InputState input, WorkTabLayoutRow row, IWorkTabLayoutController layout)
        {
            if (row.Pawn == null) return false;

            if (input.IsDoubleClick)
            {
                _selectionManager.HandleDoubleClick(row.Pawn);
                return true;
            }

            if (input.IsLeftClick)
            {
                var allPawns = layout.Rows
                    .Where(r => !r.IsDivider && r.Pawn != null)
                    .Select(r => r.Pawn);

                _selectionManager.HandleRowClick(
                    row.Pawn, 
                    input.Shift, 
                    input.Control, 
                    allPawns);

                return true;
            }

            return false;
        }
    }
}
