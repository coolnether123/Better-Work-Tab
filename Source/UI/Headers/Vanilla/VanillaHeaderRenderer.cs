using UnityEngine;
using RimWorld;
using Verse;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.Headers.Angled;

namespace Better_Work_Tab.UI.Headers.Vanilla
{
    /// <summary>
    /// Renders vanilla-style headers with exact vanilla positioning rules.
    /// Includes the vertical stem lines for staggered labels.
    /// </summary>
    public class VanillaHeaderRenderer : IHeaderRenderer
    {
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
            bool oldWordWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;
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
            if (SubWorkDrilldownState.TryGetHeaderTransitionOffset(column, headerRect.width, out float transitionOffsetX))
            {
                textRect.x += transitionOffsetX;
            }

            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Matrix4x4 oldMatrix = GUI.matrix;
            float flipScale = SubWorkDrilldownState.HeaderFlipScale;
            float flipAlpha = SubWorkDrilldownState.HeaderFlipAlpha;
            float parentAlpha = SubWorkDrilldownState.ParentWorkContentAlpha;
            string parentText = null;
            Rect parentTextRect = Rect.zero;
            if (parentAlpha > 0.001f && column?.workType != null)
            {
                parentText = HeaderUtility.GetParentHeaderText(column.workType, showMarker);
                if (!parentText.NullOrEmpty() && parentText != displayText)
                {
                    Vector2 parentSize = Text.CalcSize(parentText);
                    float parentTextY = headerBottom - yOffset - (parentSize.y / 2f);
                    parentTextRect = new Rect(
                        headerRect.center.x - (parentSize.x / 2f),
                        parentTextY,
                        parentSize.x,
                        parentSize.y);
                }
                else
                {
                    parentText = null;
                }
            }

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

                if (parentText != null)
                {
                    DrawLabel(parentTextRect, parentText, showMarker, parentAlpha);
                    DrawStemLine(parentTextRect, headerBottom, parentAlpha);
                }

                if (flipScale < 0.999f)
                {
                    Vector2 pivot = GUIClipUtility.Unclip(textRect.center);
                    GUI.matrix = oldMatrix *
                        Matrix4x4.TRS(pivot, Quaternion.identity, Vector3.one) *
                        Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, flipScale, 1f)) *
                        Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
                }

                DrawLabel(textRect, displayText, showMarker, flipAlpha);

                // Stem Line
                DrawStemLine(textRect, headerBottom, flipAlpha);
                GUI.matrix = oldMatrix;

                // Sort Indicator
                if (isSorted)
                {
                    HeaderUtility.DrawSortIndicator(headerRect, sortDescending);
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
                Text.Anchor = oldAnchor;
                GUI.color = oldColor;
                GUI.matrix = oldMatrix;
            }
        }

        /// <summary>
        /// Draws the vertical stem line connecting the text to the pawn table row.
        /// Logic: specific height based on stagger level to match vanilla visuals.
        /// </summary>
        private static void DrawLabel(Rect textRect, string text, bool showMarker, float alpha)
        {
            // Text Color: Apply moved marker color only if color tint is enabled
            GUI.color = (showMarker && BetterWorkTabMod.Settings.showMovedColumnColorTint)
                ? HeaderUtility.Colors.MovedMarkerColor
                : BetterWorkTabMod.Settings.angledHeaderColor;
            GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, GUI.color.a * Mathf.Clamp01(alpha));
            Widgets.Label(textRect, text);
        }

        private void DrawStemLine(Rect textRect, float headerBottom, float alpha = 1f)
        {
            if (BetterWorkTabMod.Settings.removeHeaderUnderline)
                return;

            // Calculate which level this is based on distance from headerBottom
            float textMiddle = textRect.center.y;
            float distanceFromBottom = headerBottom - textMiddle;
            int level = Mathf.RoundToInt((distanceFromBottom - VanillaHeaderMetrics.Level0Offset) / VanillaHeaderMetrics.LevelStepHeight);
            level = Mathf.Max(0, level); // Ensure non-negative
            
            // Fixed stem heights: 11px for level 0, 31px for level 1, etc.
            float stemHeight = StemBaseHeight + (level * VanillaHeaderMetrics.LevelStepHeight);

            float centerX = textRect.center.x;
            float stemTop = textRect.center.y + (textRect.height / 2f) + StemYAdjustment;

            if (stemHeight > 0.5f)
            {
                // Draw the vanilla grey stem (2px wide)
                Rect stemRect = new Rect(centerX, stemTop, StemWidth, stemHeight);
                Color color = HeaderUtility.Colors.VanillaStemColor;
                color.a *= Mathf.Clamp01(alpha);
                GUI.color = color;
                Widgets.DrawBoxSolid(stemRect, GUI.color);
            }
        }
    }
}
