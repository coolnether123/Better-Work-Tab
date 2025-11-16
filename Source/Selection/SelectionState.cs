using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Selection
{
    /// <summary>
    /// Tracks currently selected pawns in the work tab.
    /// Immutable snapshot approach for thread safety.
    /// </summary>
    public class SelectionState : IExposable
    {
        private HashSet<string> _selectedPawnIds = new HashSet<string>();

        public IReadOnlyCollection<string> SelectedPawnIds => _selectedPawnIds;

        public bool IsSelected(Pawn pawn)
        {
            return pawn != null && _selectedPawnIds.Contains(pawn.ThingID);
        }

        public void SetSelected(Pawn pawn, bool selected)
        {
            if (pawn == null) return;

            if (selected)
                _selectedPawnIds.Add(pawn.ThingID);
            else
                _selectedPawnIds.Remove(pawn.ThingID);
        }

        public void ClearSelection()
        {
            _selectedPawnIds.Clear();
        }

        public void SelectAll(IEnumerable<Pawn> pawns)
        {
            _selectedPawnIds.Clear();
            foreach (var pawn in pawns)
            {
                if (pawn != null)
                    _selectedPawnIds.Add(pawn.ThingID);
            }
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref _selectedPawnIds, "selectedPawnIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && _selectedPawnIds == null)
            {
                _selectedPawnIds = new HashSet<string>();
            }
        }
    }
}
