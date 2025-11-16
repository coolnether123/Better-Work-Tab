using System.Collections.Generic;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Verse;

namespace Better_Work_Tab.UI
{
    public sealed class WorkTabSnapshot : IPawnOrganizerSnapshot
    {
        public IReadOnlyList<Pawn> Pawns { get; }
        public IReadOnlyList<PawnDivider> Dividers { get; }

        public WorkTabSnapshot(List<Pawn> pawns, List<PawnDivider> dividers)
        {
            Pawns = pawns ?? new List<Pawn>();
            Dividers = dividers ?? new List<PawnDivider>();
        }
    }
}
