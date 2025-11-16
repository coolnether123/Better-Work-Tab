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
                _pawnColorsToSave = PawnColorDatabase.GetColors();
            }

            Scribe_Collections.Look(ref _pawnColorsToSave, "pawnRowColors", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                PawnColorDatabase.LoadColors(_pawnColorsToSave);
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureDatabaseSync();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            EnsureDatabaseSync();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            _pawnColorsToSave = new Dictionary<string, Color>();
            PawnColorDatabase.Clear();
        }

        private void EnsureDatabaseSync()
        {
            if (_pawnColorsToSave == null)
            {
                PawnColorDatabase.Clear();
            }

            PawnColorDatabase.LoadColors(_pawnColorsToSave);
        }
    }
}
