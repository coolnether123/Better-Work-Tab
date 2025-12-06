using Better_Work_Tab.PawnOrganizer.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.API
{
    public class WorkTabSnapshot : IPawnOrganizerSnapshot
    {
        public IReadOnlyList<Pawn> Pawns { get; }
        public IReadOnlyList<PawnDivider> Dividers { get; }

        public WorkTabSnapshot(IReadOnlyList<Pawn> pawns, IReadOnlyList<PawnDivider> dividers)
        {
            Pawns = pawns ?? (IReadOnlyList<Pawn>)Array.Empty<Pawn>();
            Dividers = dividers ?? (IReadOnlyList<PawnDivider>)Array.Empty<PawnDivider>();
        }
    }
}