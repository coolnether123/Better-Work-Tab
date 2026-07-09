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
        private readonly Rect _headerContentRect;
        private readonly PawnTable _table;

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
            bool isExpandBesideChild,
            PawnTable table = null)
        {
            Column = column;
            _headerContentRect = headerRect;
            _table = table;
            OffsetX = offsetX;
            Width = width;
            SubWorkParent = subWorkParent;
            SubWorkGiver = subWorkGiver;
            SubWorkSlot = subWorkSlot;
            IsExpandBesideChild = isExpandBesideChild;
        }

        public PawnColumnDef Column { get; }
        /// <summary>
        /// Header position in the current window coordinate space. Headers are drawn outside
        /// PawnTable's scroll group, so they must explicitly follow its horizontal offset.
        /// </summary>
        public Rect HeaderRect
        {
            get
            {
                Rect rect = _headerContentRect;
                if (_table != null)
                {
                    rect.x -= _table.scrollPosition.x;
                }

                return rect;
            }
        }

        /// <summary>Stable unscrolled header position used by reorder animation snapshots.</summary>
        internal Rect HeaderContentRect => _headerContentRect;
        public float OffsetX { get; }
        public float Width { get; }
        public WorkTypeDef SubWorkParent { get; }
        public WorkGiverDef SubWorkGiver { get; }
        public int SubWorkSlot { get; }
        public bool IsExpandBesideChild { get; }
    }
}
