using RimWorld;
using Verse;

namespace Better_Work_Tab.Persistence
{
    /// <summary>
    /// Manages saving and loading of column state.
    /// Single responsibility: Persistence of column configuration.
    /// </summary>
    public class ColumnStateManager
    {
        private ColumnState _state = new ColumnState();

        public ColumnState State => _state;

        public void SaveColumnWidth(PawnColumnDef column, float width)
        {
            _state.SetWidth(column, width);
            Save();
        }

        public float GetColumnWidth(PawnColumnDef column, float defaultWidth)
        {
            return _state.GetWidth(column, defaultWidth);
        }

        public void ToggleColumnVisibility(PawnColumnDef column)
        {
            bool currentlyVisible = _state.IsVisible(column);
            _state.SetVisible(column, !currentlyVisible);
            Save();
        }

        public bool IsColumnVisible(PawnColumnDef column)
        {
            return _state.IsVisible(column);
        }

        private void Save()
        {
            BetterWorkTabMod.Settings.Write();
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref _state, "columnState");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && _state == null)
            {
                _state = new ColumnState();
            }
        }
    }
}
