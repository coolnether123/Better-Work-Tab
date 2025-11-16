using System.Collections.Generic;
using Verse;

namespace Better_Work_Tab.Persistence
{
    /// <summary>
    /// Persists custom row heights (for dividers primarily).
    /// </summary>
    public class RowHeightPersistence : IExposable
    {
        private Dictionary<string, float> _rowHeights = new Dictionary<string, float>();

        public float GetHeight(string rowId, float defaultHeight)
        {
            return _rowHeights.TryGetValue(rowId, out float height) ? height : defaultHeight;
        }

        public void SetHeight(string rowId, float height)
        {
            _rowHeights[rowId] = height;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref _rowHeights, "rowHeights", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && _rowHeights == null)
            {
                _rowHeights = new Dictionary<string, float>();
            }
        }
    }
}
