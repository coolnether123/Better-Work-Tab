using System.Collections.Generic;
using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.Data
{
    /// <summary>
    /// GameComponent responsible for saving and loading the custom pawn background colors.
    /// It interacts with the PawnColorDatabase to persist the data.
    /// </summary>
    public class GameComponent_PawnColors : GameComponent
    {
        private Dictionary<string, Color> _pawnColorsToSave;

        public GameComponent_PawnColors(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // When saving, get the current colors from the database
                _pawnColorsToSave = PawnColorDatabase.GetColors();
            }

            Scribe_Collections.Look(ref _pawnColorsToSave, "pawnRowColors", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // When loading is finished, load the data into the static database
                PawnColorDatabase.LoadColors(_pawnColorsToSave);
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // On game load, after all data is exposed, ensure the database is populated.
            // This is a fallback for older saves or different load orders.
            PawnColorDatabase.LoadColors(_pawnColorsToSave);
        }
    }
}
