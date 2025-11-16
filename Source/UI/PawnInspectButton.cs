using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Renders inspect buttons on pawn rows.
    /// Single responsibility: Draw and handle inspect buttons.
    /// </summary>
    public static class PawnInspectButton
    {
        private const float ButtonSize = 20f;
        private static readonly Texture2D InspectIcon = TexButton.Info;

        public static void DrawInspectButton(Rect rowRect, Pawn pawn)
        {
            if (pawn == null) return;

            Rect buttonRect = new Rect(
                rowRect.xMax - ButtonSize - 2f,
                rowRect.y + (rowRect.height - ButtonSize) / 2f,
                ButtonSize,
                ButtonSize);

            if (Widgets.ButtonImage(buttonRect, InspectIcon))
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(pawn);
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect);
                VisualFeedback.AudioManager.PlayClick();
            }

            TooltipHandler.TipRegion(buttonRect, "Inspect " + pawn.LabelCap);
        }
    }
}
