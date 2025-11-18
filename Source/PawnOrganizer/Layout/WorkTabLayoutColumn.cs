using RimWorld;
using UnityEngine;

namespace Better_Work_Tab.PawnOrganizer
{
    /// <summary>
    /// Immutable description of a column header generated during layout rebuild.
    /// </summary>
    public readonly struct WorkTabLayoutColumn
    {
        public WorkTabLayoutColumn(PawnColumnDef column, Rect headerRect, float offsetX, float width)
        {
            Column = column;
            HeaderRect = headerRect;
            OffsetX = offsetX;
            Width = width;
        }

        public PawnColumnDef Column { get; }
        public Rect HeaderRect { get; }
        public float OffsetX { get; }
        public float Width { get; }
    }
}
