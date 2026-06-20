using System.Collections.Generic;
using Better_Work_Tab.PawnOrganizer.Data;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.API
{
    public interface IPawnOrganizerSnapshot
    {
        IList<Pawn> Pawns { get; }
        IList<PawnDivider> Dividers { get; }
    }
}
