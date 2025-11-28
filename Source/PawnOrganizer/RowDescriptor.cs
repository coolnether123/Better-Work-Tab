using Better_Work_Tab.PawnOrganizer.Data;
using Verse;

namespace Better_Work_Tab.PawnOrganizer
{
    /// <summary>
    /// Immutable data contract representing a single row in the work table.
    /// Used by both sizing and rendering to stay in sync.
    /// </summary>
    public sealed class RowDescriptor
    {
        public Pawn Pawn { get; }
        public PawnDivider Divider { get; }
        public float Height { get; }

        public bool IsPawn => Pawn != null;
        public bool IsDivider => Divider != null;

        // Constructor for pawn rows
        public RowDescriptor(Pawn pawn, float height)
        {
            Pawn = pawn;
            Height = height;
        }

        // Constructor for divider rows
        public RowDescriptor(PawnDivider divider, float height)
        {
            Divider = divider;
            Height = height;
        }
    }
}