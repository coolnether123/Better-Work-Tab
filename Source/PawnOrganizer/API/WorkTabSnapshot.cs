using Better_Work_Tab.PawnOrganizer.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.API
{
    public class WorkTabSnapshot : IPawnOrganizerSnapshot
    {
        public IList<Pawn> Pawns { get; }
        public IList<PawnDivider> Dividers { get; }

        public WorkTabSnapshot(IList<Pawn> pawns, IList<PawnDivider> dividers)
        {
            Pawns = pawns ?? (IList<Pawn>)new Pawn[0];
            Dividers = dividers ?? (IList<PawnDivider>)new PawnDivider[0];
        }
    }
}
