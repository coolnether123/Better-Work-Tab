using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer
{
    /// <summary>
    /// Immutable description of a column header generated during layout rebuild.
    /// </summary>
    public readonly struct WorkTabLayoutColumn
    {
        public WorkTabLayoutColumn(PawnColumnDef column, Rect headerRect, float offsetX, float width)
            : this(column, headerRect, offsetX, width, null, null, -1, false)
        {
        }

        public WorkTabLayoutColumn(
            PawnColumnDef column,
            Rect headerRect,
            float offsetX,
            float width,
            WorkTypeDef subWorkParent,
            WorkGiverDef subWorkGiver,
            int subWorkSlot,
            bool isExpandBesideChild)
        {
            Column = column;
            HeaderRect = headerRect;
            OffsetX = offsetX;
            Width = width;
            SubWorkParent = subWorkParent;
            SubWorkGiver = subWorkGiver;
            SubWorkSlot = subWorkSlot;
            IsExpandBesideChild = isExpandBesideChild;
        }

        public PawnColumnDef Column { get; }
        public Rect HeaderRect { get; }
        public float OffsetX { get; }
        public float Width { get; }
        public WorkTypeDef SubWorkParent { get; }
        public WorkGiverDef SubWorkGiver { get; }
        public int SubWorkSlot { get; }
        public bool IsExpandBesideChild { get; }
    }
}
