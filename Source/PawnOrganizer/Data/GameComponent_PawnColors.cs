using System.Collections.Generic;
using Better_Work_Tab.Mod_Support.LocalProfiles;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.Data
{
    /// <summary>
    /// GameComponent responsible for saving and loading custom pawn colors.
    /// Now handles both background and text colors.
    /// </summary>
    public class GameComponent_PawnColors : GameComponent
    {
        private Dictionary<string, Color> _backgroundColors;
        private Dictionary<string, Color> _textColors;

        public GameComponent_PawnColors(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();

            if (MultiplayerBridge.Active)
            {
                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    LoadColorsFromProfile();
                }

                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    SaveColorsToProfile();
                }

                return;
            }

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                _backgroundColors = PawnColorDatabase.GetColors();
                _textColors = PawnTextColorDatabase.GetColors();
            }

            Scribe_Collections.Look(ref _backgroundColors, "pawnBackgroundColors", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref _textColors, "pawnTextColors", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                PawnColorDatabase.LoadColors(_backgroundColors);
                PawnTextColorDatabase.LoadColors(_textColors);
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
            _backgroundColors = new Dictionary<string, Color>();
            _textColors = new Dictionary<string, Color>();
            PawnColorDatabase.Clear();
            PawnTextColorDatabase.Clear();
        }

        private void EnsureDatabaseSync()
        {
            if (MultiplayerBridge.Active)
            {
                LoadColorsFromProfile();
                return;
            }

            if (_backgroundColors == null)
            {
                PawnColorDatabase.Clear();
            }
            else
            {
                PawnColorDatabase.LoadColors(_backgroundColors);
            }

            if (_textColors == null)
            {
                PawnTextColorDatabase.Clear();
            }
            else
            {
                PawnTextColorDatabase.LoadColors(_textColors);
            }
        }

        private void LoadColorsFromProfile()
        {
            var profile = BWTLocalProfileStore.Current;
            PawnColorDatabase.LoadColors(profile?.PawnBackgroundColors);
            PawnTextColorDatabase.LoadColors(profile?.PawnTextColors);
        }

        private void SaveColorsToProfile()
        {
            var profile = BWTLocalProfileStore.Current;
            if (profile == null)
                return;

            profile.PawnBackgroundColors = new Dictionary<string, Color>(PawnColorDatabase.GetColors());
            profile.PawnTextColors = new Dictionary<string, Color>(PawnTextColorDatabase.GetColors());
            BWTLocalProfileStore.MarkDirty();
            // Don't save immediately - let the timer handle it
        }
    }
}
