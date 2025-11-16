using RimWorld;
using Verse;

namespace Better_Work_Tab.Sorting
{
    /// <summary>
    /// Tracks the current sort column and direction.
    /// </summary>
    public class SortState : IExposable
    {
        public PawnColumnDef SortColumn;
        public bool Descending;

        public void SetSort(PawnColumnDef column, bool descending)
        {
            SortColumn = column;
            Descending = descending;
        }

        public void ToggleSort(PawnColumnDef column)
        {
            if (SortColumn == column)
            {
                Descending = !Descending;
            }
            else
            {
                SortColumn = column;
                Descending = false;
            }
        }

        public void ClearSort()
        {
            SortColumn = null;
            Descending = false;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref SortColumn, "sortColumn");
            Scribe_Values.Look(ref Descending, "descending", false);
        }
    }
}
