using Better_Work_Tab.Input;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.Selection;
using System.Linq;
using Verse;

namespace Better_Work_Tab.RowManagement
{
    /// <summary>
    /// Handles keyboard navigation through rows (Up/Down arrows).
    /// Single responsibility: Row navigation logic.
    /// </summary>
    public class RowNavigationHandler
    {
        private readonly PawnSelectionManager _selectionManager;

        public RowNavigationHandler(PawnSelectionManager selectionManager)
        {
            _selectionManager = selectionManager;
        }

        public bool NavigateRows(InputState input, IWorkTabLayoutController layout)
        {
            if (layout?.Rows == null || layout.Rows.Count == 0) return false;

            var pawnRows = layout.Rows.Where(r => !r.IsDivider && r.Pawn != null).ToList();
            if (pawnRows.Count == 0) return false;

            // Find currently selected pawn
            var selectedPawns = _selectionManager.State.SelectedPawnIds;
            var currentRow = pawnRows.FirstOrDefault(r => selectedPawns.Contains(r.Pawn.ThingID));

            int currentIndex = currentRow.Pawn != null ? pawnRows.IndexOf(currentRow) : -1;
            int newIndex = currentIndex;

            if (input.KeyCode == UnityEngine.KeyCode.UpArrow)
            {
                newIndex = currentIndex > 0 ? currentIndex - 1 : pawnRows.Count - 1;
            }
            else if (input.KeyCode == UnityEngine.KeyCode.DownArrow)
            {
                newIndex = currentIndex < pawnRows.Count - 1 ? currentIndex + 1 : 0;
            }

            if (newIndex != currentIndex && newIndex >= 0 && newIndex < pawnRows.Count)
            {
                var newPawn = pawnRows[newIndex].Pawn;
                _selectionManager.State.ClearSelection();
                _selectionManager.State.SetSelected(newPawn, true);

                VisualFeedback.AudioManager.PlayTick();
                return true;
            }

            return false;
        }
    }
}
