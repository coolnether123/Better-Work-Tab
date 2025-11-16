using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Selection
{
    /// <summary>
    /// Manages pawn selection logic including multi-select (Shift/Ctrl).
    /// Single responsibility: Handle selection state changes.
    /// </summary>
    public class PawnSelectionManager
    {
        private readonly SelectionState _state = new SelectionState();
        private Pawn _lastClickedPawn;

        public SelectionState State => _state;

        /// <summary>
        /// Handle a click on a pawn row with modifier keys.
        /// </summary>
        public void HandleRowClick(Pawn pawn, bool shiftHeld, bool controlHeld, IEnumerable<Pawn> allPawns)
        {
            if (pawn == null) return;

            if (controlHeld)
            {
                // Ctrl+Click: Toggle individual selection
                bool currentlySelected = _state.IsSelected(pawn);
                _state.SetSelected(pawn, !currentlySelected);
                _lastClickedPawn = pawn;
            }
            else if (shiftHeld && _lastClickedPawn != null)
            {
                // Shift+Click: Range select
                HandleRangeSelect(pawn, allPawns);
            }
            else
            {
                // Normal click: Single select
                _state.ClearSelection();
                _state.SetSelected(pawn, true);
                _lastClickedPawn = pawn;
            }

            // Sync with vanilla selection system
            SyncToVanillaSelector();
        }

        /// <summary>
        /// Handle double-click to select and jump to pawn.
        /// </summary>
        public void HandleDoubleClick(Pawn pawn)
        {
            if (pawn == null) return;

            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn);

            // Play audio feedback
            VisualFeedback.AudioManager.PlayClick();
        }

        private void HandleRangeSelect(Pawn targetPawn, IEnumerable<Pawn> allPawns)
        {
            var pawnList = allPawns.ToList();
            int lastIndex = pawnList.IndexOf(_lastClickedPawn);
            int targetIndex = pawnList.IndexOf(targetPawn);

            if (lastIndex < 0 || targetIndex < 0) return;

            int start = System.Math.Min(lastIndex, targetIndex);
            int end = System.Math.Max(lastIndex, targetIndex);

            for (int i = start; i <= end; i++)
            {
                _state.SetSelected(pawnList[i], true);
            }

            _lastClickedPawn = targetPawn;
        }

        private void SyncToVanillaSelector()
        {
            // Optional: sync internal state with vanilla Find.Selector
            // This ensures compatibility with other mods
            Find.Selector.ClearSelection();
            foreach (var pawnId in _state.SelectedPawnIds)
            {
                var pawn = Find.CurrentMap?.mapPawns?.FreeColonists?.FirstOrDefault(p => p.ThingID == pawnId);
                if (pawn != null)
                {
                    Find.Selector.Select(pawn, playSound: false);
                }
            }
        }
    }
}
