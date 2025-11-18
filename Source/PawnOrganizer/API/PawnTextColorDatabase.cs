using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer.API
{
    /// <summary>
    /// Static database to hold custom text color information for pawns.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PawnTextColorDatabase
    {
        private static Dictionary<string, Color> _textColors;

        static PawnTextColorDatabase()
        {
            _textColors = new Dictionary<string, Color>();
        }

        public static void SetColor(Pawn pawn, Color color)
        {
            if (pawn == null) return;
            _textColors[pawn.ThingID] = color;
        }

        public static bool TryGetColor(Pawn pawn, out Color color)
        {
            if (pawn != null && _textColors.TryGetValue(pawn.ThingID, out color))
            {
                return true;
            }
            color = Color.white;
            return false;
        }

        public static void ClearColor(Pawn pawn)
        {
            if (pawn != null && _textColors.ContainsKey(pawn.ThingID))
            {
                _textColors.Remove(pawn.ThingID);
            }
        }

        public static Dictionary<string, Color> GetColors()
        {
            return _textColors;
        }

        public static void LoadColors(Dictionary<string, Color> loadedColors)
        {
            _textColors = loadedColors != null
                ? new Dictionary<string, Color>(loadedColors)
                : new Dictionary<string, Color>();
        }

        public static void Clear()
        {
            _textColors.Clear();
        }
    }
}
