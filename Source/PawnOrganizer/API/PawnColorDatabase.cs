using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.API
{
    /// <summary>
    /// A static database to hold custom color information for pawns.
    /// This allows other parts of the mod to access pawn colors without needing
    /// to know about the underlying save/load mechanism.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PawnColorDatabase
    {
        private static Dictionary<string, Color> _pawnColors;

        static PawnColorDatabase()
        {
            _pawnColors = new Dictionary<string, Color>();
            Log.Message("[BetterWorkTab] PawnColorDatabase initialized.");
        }

        /// <summary>
        /// Sets a custom background color for a specific pawn.
        /// </summary>
        public static void SetColor(Pawn pawn, Color color)
        {
            if (pawn == null) return;
            _pawnColors[pawn.ThingID] = color;
            Log.Message($"[BetterWorkTab] Set color for {pawn.LabelShort}: {color}");
        }

        /// <summary>
        /// Tries to get the custom background color for a pawn.
        /// </summary>
        public static bool TryGetColor(Pawn pawn, out Color color)
        {
            if (pawn != null && _pawnColors.TryGetValue(pawn.ThingID, out color))
            {
                return true;
            }
            color = default;
            return false;
        }

        /// <summary>
        /// Removes the custom background color for a pawn.
        /// </summary>
        public static void ClearColor(Pawn pawn)
        {
            if (pawn != null && _pawnColors.ContainsKey(pawn.ThingID))
            {
                _pawnColors.Remove(pawn.ThingID);
                Log.Message($"[BetterWorkTab] Cleared color for {pawn.LabelShort}");
            }
        }

        /// <summary>
        /// Gets the entire color dictionary, intended for saving.
        /// </summary>
        public static Dictionary<string, Color> GetColors()
        {
            return _pawnColors;
        }

        /// <summary>
        /// Loads the entire color dictionary, intended for loading from a save.
        /// </summary>
        public static void LoadColors(Dictionary<string, Color> loadedColors)
        {
            _pawnColors = loadedColors != null
                ? new Dictionary<string, Color>(loadedColors)
                : new Dictionary<string, Color>();
            Log.Message($"[BetterWorkTab] Loaded {_pawnColors.Count} pawn colors from save.");
        }

        /// <summary>
        /// Clears all cached pawn colors. Use when a save is unloaded to avoid leaking references.
        /// </summary>
        public static void Clear()
        {
            _pawnColors.Clear();
        }
    }
}
