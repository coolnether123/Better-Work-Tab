using UnityEngine;
using RimWorld;
using Verse;
using Better_Work_Tab.DragDrop;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Renders vanilla-style headers with exact vanilla positioning rules.
    /// Includes the 1px gap between text and stem line.
    /// </summary>
    public class VanillaHeaderRenderer : IHeaderRenderer
    {
        private VanillaHeaderLayoutSolver _solver;

        public VanillaHeaderRenderer(VanillaHeaderLayoutSolver solver)
        {
            _solver = solver;
        }

        public void DrawHeader(AngledLabelDrawer.AngledLabelLayout layout, bool isMouseOver,
                               bool isSorted, bool sortDescending, Rect headerRect,
                               PawnColumnDef column, bool showMarker)
        {
            if (column == null || _solver == null)
                return;

            float yOffset = _solver.GetOffset(column);

            string displayText = layout.Text;
            if (showMarker && !displayText.EndsWith("*"))
                displayText += "*";

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Vector2 textSize = Text.CalcSize(displayText);

            // === POSITIONING MATH ===
            // yOffset = distance from headerBottom (pawn box) to TEXT MIDDLE
            //   Level 0 (low):  24px from text middle to pawn box
            //   Level 1 (high): 44px from text middle to pawn box
            //   Level 2:        64px from text middle to pawn box
            // 
            // Formula: 
            //   textMiddle = headerBottom - yOffset
            //   textY (top) = textMiddle - (textSize.y / 2)
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
                    GUI.color = new Color(1f, 1f, 1f, 0.2f);
                    // Highlight the full vertical strip for this level
                    Rect highlightRect = new Rect(headerRect.x, textY, headerRect.width, headerRect.yMax - textY);
                    Widgets.DrawHighlight(highlightRect);
                }

                if (column != null && ColumnSelectionManager.IsSelected(column))
                {
                    GUI.color = new Color(1f, 0.92f, 0.4f, 0.4f);
                    Rect highlightRect = new Rect(headerRect.x, textY, headerRect.width, headerRect.yMax - textY);
                    Widgets.DrawHighlight(highlightRect);
                }

                // Text Color
                GUI.color = showMarker
                    ? new Color(1f, 0.85f, 0.2f, 1f) 
                    : BetterWorkTabMod.Settings.angledHeaderColor;

                Widgets.Label(textRect, displayText);

                // Stem Line
                DrawStemLine(textRect, headerBottom);

                // Sort Indicator
                if (isSorted)
                {
                    DrawSortIndicator(textRect, sortDescending);
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
        /// Draws the vertical stem line.
        /// Logic: 4px gap from text, then exact stem height based on level.
        /// Level 0: 11px, Level 1: 31px, Level 2: 51px, etc.
        /// </summary>
        private void DrawStemLine(Rect textRect, float headerBottom)
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings?.removeHeaderUnderline ?? false)
                return;

            const float gap = 2f; // 2px gap between text and line (vanilla spec)
            
            // Calculate which level this is based on distance from headerBottom
            // Level 0: 24px, Level 1: 44px, Level 2: 64px
            float textMiddle = textRect.center.y;
            float distanceFromBottom = headerBottom - textMiddle;
            int level = Mathf.RoundToInt((distanceFromBottom - 24f) / 20f);
            level = Mathf.Max(0, level); // Ensure non-negative
            
            // Fixed stem heights: 11px for level 0, 31px for level 1, etc.
            float stemHeight = 11f + (level * 20f);

            float centerX = textRect.center.x;
            float stemTop = textRect.yMax + gap;

            // Only draw if there's enough room
            if (stemHeight > 0.5f)
            {
                // Draw black outline first (4px wide, shifted 1px right)
                Rect outlineRect = new Rect(centerX - 1f, stemTop, 4f, stemHeight);
                GUI.color = Color.black;
                Widgets.DrawBoxSolid(outlineRect, GUI.color);
                
                // Then draw the vanilla grey stem on top (2px wide, shifted 1px right)
                Rect stemRect = new Rect(centerX, stemTop, 2f, stemHeight);
                GUI.color = new Color(1f, 1f, 1f, 0.35f); // Vanilla-like faint grey
                Widgets.DrawBoxSolid(stemRect, GUI.color);
            }
        }

        private void DrawSortIndicator(Rect textRect, bool descending)
        {
            var oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);

            Rect sortRect = new Rect(textRect.xMax + 2f, textRect.y + 1f, 10f, 10f);
            Widgets.Label(sortRect, descending ? "▼" : "▲");

            Text.Font = oldFont;
        }
    }
}
