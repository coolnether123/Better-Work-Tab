using UnityEngine;
using RimWorld;
using Verse;
using Better_Work_Tab.DragDrop;

namespace Better_Work_Tab.UI.Headers.Vanilla
{
    /// <summary>
    /// Renders vanilla-style headers with exact vanilla positioning rules.
    /// Includes the vertical stem lines for staggered labels.
    /// </summary>
    public class VanillaHeaderRenderer : IHeaderRenderer
    {
        private const float Level0Baseline = 19f;
        private const float LevelStepSize = 20f;
        private const float StemBaseHeight = 11f;
        private const float StemWidth = 2f;
        private const float StemYAdjustment = -3f; // font compensation + gap

        private VanillaHeaderLayoutSolver _solver;

        public VanillaHeaderRenderer(VanillaHeaderLayoutSolver solver)
        {
            _solver = solver;
        }

        /// <summary>
        /// Draws a vanilla-style header for a work priority column.
        /// </summary>
        public void DrawHeader(Angled.AngledLabelDrawer.AngledLabelLayout layout, bool isMouseOver,
                                bool isSorted, bool sortDescending, Rect headerRect,
                                PawnColumnDef column, bool showMarker)
        {
            if (column == null || _solver == null)
                return;

            float yOffset = _solver.GetOffset(column);
            string displayText = layout.Text;

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Vector2 textSize = Text.CalcSize(displayText);

            // === POSITIONING MATH ===
            // yOffset = distance from headerBottom (pawn box) to TEXT MIDDLE
            float headerBottom = headerRect.yMax;
            float textY = headerBottom - yOffset - (textSize.y / 2f);

            Rect textRect = new Rect(
                headerRect.center.x - (textSize.x / 2f),
                textY,
                textSize.x,
                textSize.y
            );

            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            try
            {
                Text.Anchor = TextAnchor.MiddleCenter;

                // Highlights
                if (isMouseOver)
                {
                    GUI.color = HeaderUtility.Colors.HoverHighlight;
                    Rect highlightRect = new Rect(headerRect.x, textY, headerRect.width, headerRect.yMax - textY);
                    Widgets.DrawHighlight(highlightRect);
                }

                if (column != null && Better_Work_Tab.DragDrop.ColumnSelectionManager.IsSelected(column))
                {
                    GUI.color = HeaderUtility.Colors.SelectedHighlight;
                    Rect highlightRect = new Rect(headerRect.x, textY, headerRect.width, headerRect.yMax - textY);
                    Widgets.DrawHighlight(highlightRect);
                }

                // Text Color
                GUI.color = showMarker
                    ? HeaderUtility.Colors.MovedMarkerColor 
                    : BetterWorkTabMod.Settings.angledHeaderColor;

                Widgets.Label(textRect, displayText);

                // Stem Line
                DrawStemLine(textRect, headerBottom);

                // Sort Indicator
                if (isSorted)
                {
                    HeaderUtility.DrawSortIndicator(headerRect, sortDescending);
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                GUI.color = oldColor;
            }
        }

        /// <summary>
        /// Draws the vertical stem line connecting the text to the pawn table row.
        /// Logic: specific height based on stagger level to match vanilla visuals.
        /// </summary>
        private void DrawStemLine(Rect textRect, float headerBottom)
        {
            if (BetterWorkTabMod.Settings.removeHeaderUnderline)
                return;

            // Calculate which level this is based on distance from headerBottom
            float textMiddle = textRect.center.y;
            float distanceFromBottom = headerBottom - textMiddle;
            int level = Mathf.RoundToInt((distanceFromBottom - Level0Baseline) / LevelStepSize);
            level = Mathf.Max(0, level); // Ensure non-negative
            
            // Fixed stem heights: 11px for level 0, 31px for level 1, etc.
            float stemHeight = StemBaseHeight + (level * LevelStepSize);

            float centerX = textRect.center.x;
            float stemTop = textRect.center.y + (textRect.height / 2f) + StemYAdjustment;

            if (stemHeight > 0.5f)
            {
                // Draw the vanilla grey stem (2px wide)
                Rect stemRect = new Rect(centerX, stemTop, StemWidth, stemHeight);
                GUI.color = HeaderUtility.Colors.VanillaStemColor;
                Widgets.DrawBoxSolid(stemRect, GUI.color);
            }
        }
    }
}
