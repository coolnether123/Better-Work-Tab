using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Better_Work_Tab.Persistence
{
    /// <summary>
    /// Persists column widths and visibility state.
    /// </summary>
    public class ColumnState : IExposable
    {
        private Dictionary<string, float> _columnWidths = new Dictionary<string, float>();
        private HashSet<string> _hiddenColumns = new HashSet<string>();

        public float GetWidth(PawnColumnDef column, float defaultWidth)
        {
            if (column == null) return defaultWidth;
            return _columnWidths.TryGetValue(column.defName, out float width) ? width : defaultWidth;
        }

        public void SetWidth(PawnColumnDef column, float width)
        {
            if (column == null) return;
            _columnWidths[column.defName] = width;
        }

        public bool IsVisible(PawnColumnDef column)
        {
            return column != null && !_hiddenColumns.Contains(column.defName);
        }

        public void SetVisible(PawnColumnDef column, bool visible)
        {
            if (column == null) return;

            if (visible)
                _hiddenColumns.Remove(column.defName);
            else
                _hiddenColumns.Add(column.defName);
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref _columnWidths, "columnWidths", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref _hiddenColumns, "hiddenColumns", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                _columnWidths ??= new Dictionary<string, float>();
                _hiddenColumns ??= new HashSet<string>();
            }
        }
    }
}
