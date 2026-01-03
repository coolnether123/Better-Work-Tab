using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.DragDrop;

namespace Better_Work_Tab.UI.Headers.Angled
{
    /// <summary>
    /// Static utility for drawing rotated (angled) text labels with underlines and highlights.
    /// Handles the matrix transformations required for rotation.
    /// </summary>
    public static class AngledLabelDrawer
    {
        /// <summary>
        /// Default rotation angle if the mod setting is somehow invalid.
        /// </summary>
        public const float DefaultRotationAngle = -60f;

        /// <summary>
        /// The gap between the bottom of the stem and the start of the rotated label.
        /// </summary>
        public const float STEM_BOTTOM_GAP = 2f;

        /// <summary>
        /// Returns the current rotation angle from settings or default.
        /// </summary>
        public static float CurrentRotation => BetterWorkTabMod.Settings.enableAngledHeaders ? BetterWorkTabMod.Settings.angledHeaderRotation : DefaultRotationAngle;
        
        /// <summary>
        /// Returns the cosine of the current rotation angle.
        /// </summary>
        public static float CurrentRotCos => Mathf.Cos(CurrentRotation * Mathf.Deg2Rad);
        
        /// <summary>
        /// Returns the sine of the current rotation angle.
        /// </summary>
        public static float CurrentRotSin => Mathf.Sin(CurrentRotation * Mathf.Deg2Rad);

        /// <summary>
        /// Calculates the total vertical height needed for the header area to fit all angled labels.
        /// </summary>
        /// <param name="table">The pawn table to measure.</param>
        /// <returns>The maximum required vertical height.</returns>
        public static float GetNeededHeight(PawnTable table)
        {
            if (table == null) return 0f;

            float maxH = 0f;
            var columns = table.Columns;
            if (columns == null) return 0f;

            float absSin = Mathf.Abs(Mathf.Sin(CurrentRotation * Mathf.Deg2Rad));
            float absCos = Mathf.Abs(Mathf.Cos(CurrentRotation * Mathf.Deg2Rad));

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;

            foreach (var col in columns)
            {
                if (col.Worker is PawnColumnWorker_WorkPriority && col.workType != null)
                {
                    // For height calculation, we always include the moved marker to ensure stability.
                    string labelText = HeaderUtility.GetHeaderText(col.workType, true);

                    Vector2 size = Text.CalcSize(labelText);
                    
                    // Height of a rotated rectangle: width*sin(theta) + height*cos(theta)
                    float h = (size.x * absSin) + (size.y * absCos);
                    if (h > maxH) maxH = h;
                }
            }

            Text.Font = oldFont;
            return maxH + STEM_BOTTOM_GAP;
        }

        /// <summary>
        /// Defines the pre-calculated layout data for an angled label.
        /// </summary>
        public readonly struct AngledLabelLayout
        {
            public readonly string Text;
            public readonly Vector2 Size;
            public readonly Vector2 Pivot;
            public readonly bool ShowMarker;

            public AngledLabelLayout(string text, Vector2 size, Vector2 pivot, bool showMarker)
            {
                Text = text;
                Size = size;
                Pivot = pivot;
                ShowMarker = showMarker;
            }
        }

        /// <summary>
        /// Core drawing method for an angled header.
        /// </summary>
        public static void Draw(AngledLabelLayout layout, bool isMouseOver, bool isSorted = false, bool sortDescending = false, Rect headerRect = default, PawnColumnDef column = null)
        {
            float rotation = CurrentRotation;
            Vector2 labelSize = layout.Size;
            float horizontalOffset = BetterWorkTabMod.Settings.angledHeaderHorizontalOffset;

            // Create a centered rotated rectangle with horizontal offset applied
            Rect rotatedRect = new Rect(0f, 0f, headerRect.height, labelSize.y) { center = headerRect.center };
            rotatedRect.x += horizontalOffset;

            Matrix4x4 originalMatrix = GUI.matrix;
            TextAnchor savedAnchor = Text.Anchor;
            GameFont savedFont = Text.Font;
            Color savedColor = GUI.color;
            bool savedWordWrap = Text.WordWrap;

            try
            {
                // Reset to identity matrix and unclip the pivot for screen-space rendering
                GUI.matrix = Matrix4x4.identity;
                Vector2 pivotPoint = GUIClipUtility.Unclip(rotatedRect.center);

                // Build transformation matrix: Translate to pivot -> Rotate -> Translate back
                Matrix4x4 transformationMatrix = originalMatrix;
                transformationMatrix *= Matrix4x4.TRS(pivotPoint, Quaternion.identity, Vector3.one);
                transformationMatrix *= Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, rotation), Vector3.one);
                transformationMatrix *= Matrix4x4.TRS(-pivotPoint, Quaternion.identity, Vector3.one);

                GUI.matrix = transformationMatrix;

                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;
                Text.WordWrap = false;

                // Highlights
                if (column != null && ColumnSelectionManager.IsSelected(column))
                {
                    GUI.color = HeaderUtility.Colors.SelectedHighlight;
                    GUI.DrawTexture(rotatedRect.ExpandedBy(2f), TexUI.HighlightTex);
                }

                if (isMouseOver)
                {
                    GUI.color = HeaderUtility.Colors.HoverHighlight;
                    GUI.DrawTexture(rotatedRect.ExpandedBy(2f), TexUI.HighlightTex);
                }

                // Text
                GUI.color = layout.ShowMarker ? HeaderUtility.Colors.MovedMarkerColor : BetterWorkTabMod.Settings.angledHeaderColor;
                Widgets.Label(rotatedRect, layout.Text);

                // Underline
                if (!BetterWorkTabMod.Settings.removeHeaderUnderline)
                {
                    float textWidth = labelSize.x;
                    Vector2 underlineStart = new Vector2(rotatedRect.xMin, rotatedRect.yMax);
                    Vector2 underlineEnd = new Vector2(rotatedRect.xMin + textWidth, rotatedRect.yMax);
                    Widgets.DrawLine(underlineStart, underlineEnd, Color.white, 1f);
                }
            }
            finally
            {
                GUI.matrix = originalMatrix;
                Text.Anchor = savedAnchor;
                Text.Font = savedFont;
                GUI.color = savedColor;
                Text.WordWrap = savedWordWrap;
            }

            if (isSorted && headerRect != default)
            {
                DrawSortIndicator(headerRect, sortDescending);
            }
        }

        private static void DrawSortIndicator(Rect headerRect, bool descending)
        {
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            // Move it to the bottom of the header area, centered horizontally (Old working coordinates)
            Rect sortRect = new Rect(headerRect.x + (headerRect.width - 6f) / 2f + 5f, headerRect.yMax - 9f, 12f, 12f);
            Widgets.Label(sortRect, descending ? "▼" : "▲");
            GUI.color = Color.white;
        }
    }
}