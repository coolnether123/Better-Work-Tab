using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Renders marking/favoriting indicators for pawns.
    /// Single responsibility: Draw favorite/marked pawn indicators.
    /// </summary>
    public static class PawnMarkingRenderer
    {
        private static readonly Color FavoriteColor = new Color(1f, 0.84f, 0f); // Gold
        private const float StarSize = 16f;

        public static void DrawFavoriteIndicator(Rect rowRect, Pawn pawn)
        {
            if (pawn == null || !IsFavorite(pawn)) return;

            Rect starRect = new Rect(
                rowRect.x + 2f,
                rowRect.y + (rowRect.height - StarSize) / 2f,
                StarSize,
                StarSize);

            GUI.color = FavoriteColor;
            Widgets.Label(starRect, "★");
            GUI.color = Color.white;

            TooltipHandler.TipRegion(starRect, "Favorite pawn");
        }

        private static bool IsFavorite(Pawn pawn)
        {
            // Implement favorite tracking system
            // This would need a separate manager to track favorite pawns
            return false; // Placeholder
        }

        public static void ToggleFavorite(Pawn pawn)
        {
            // Toggle favorite state
            // This would update the favorite tracking system
        }
    }
}
