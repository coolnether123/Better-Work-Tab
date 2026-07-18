using System.Collections.Generic;
using Better_Work_Tab.PawnOrganizer.Data;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.API
{
    public interface IPawnOrganizerSnapshot
    {
        IReadOnlyList<Pawn> Pawns { get; }
        IReadOnlyList<PawnDivider> Dividers { get; }
    }
}
