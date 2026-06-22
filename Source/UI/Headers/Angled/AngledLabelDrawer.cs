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
        /// Returns the horizontal offset to use. At -90 degrees, the offset is forced to 0
        /// for perfect centering, but the original setting is preserved when switching back.
        /// </summary>
        public static float EffectiveHorizontalOffset
        {
            get
            {
                float rotation = CurrentRotation;
                // Force 0 offset at -90 degrees for perfect vertical stacking alignment.
                if (Mathf.Abs(rotation + 90f) < 0.1f)
                {
                    return 0f;
                }
                return BetterWorkTabMod.Settings.angledHeaderHorizontalOffset;
            }
        }

        /// <summary>
        /// Calculates the total vertical height needed for the header area to fit all angled labels.
        /// </summary>
        /// <param name="table">The pawn table to measure.</param>
        /// <returns>The maximum required vertical height.</returns>
        public static float GetNeededHeight(PawnTable table)
        {
            if (table == null) return 0f;

            float maxH = 0f;
            var columns = HeaderUtility.GetTableColumns(table);
            if (columns == null) return 0f;

            float rotation = CurrentRotation;
            float absSin = Mathf.Abs(Mathf.Sin(rotation * Mathf.Deg2Rad));
            float absCos = Mathf.Abs(Mathf.Cos(rotation * Mathf.Deg2Rad));
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;
            float lineHeightCJK = Text.LineHeight * BetterWorkTabMod.Settings.cjkVerticalKerning;

            foreach (var col in columns)
            {
                if (col.Worker is PawnColumnWorker_WorkPriority && col.workType != null)
                {
                    // For height calculation, the moved indicator is incorporated to maintain layout stability.
                    string labelText = HeaderUtility.GetHeaderText(col.workType, true);

                    float h;
                    if (HeaderUtility.ShouldUseCJKVerticalLabel(labelText))
                    {
                        // Stacked Vertical height: characters * line height
                        h = labelText.Length * lineHeightCJK;
                    }
                    else
                    {
                        Vector2 size = Text.CalcSize(labelText);
                        // Height of a rotated rectangle: width*sin(theta) + height*cos(theta)
                        h = (size.x * absSin) + (size.y * absCos);
                    }

                    if (h > maxH) maxH = h;
                }
            }

            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
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
            public readonly bool HasCustomDrawRect;
            public readonly Rect CustomDrawRect;
            /// <summary>
            /// Indicates if this label should be drawn using character-by-character vertical stacking 
            /// instead of standard matrix rotation.
            /// </summary>
            public readonly bool IsCJKVertical;

            public AngledLabelLayout(string text, Vector2 size, Vector2 pivot, bool showMarker, bool isCJKVertical = false)
                : this(text, size, pivot, showMarker, isCJKVertical, false, default)
            {
            }

            public AngledLabelLayout(string text, Vector2 size, Vector2 pivot, bool showMarker, bool isCJKVertical, Rect customDrawRect)
                : this(text, size, pivot, showMarker, isCJKVertical, true, customDrawRect)
            {
            }

            private AngledLabelLayout(string text, Vector2 size, Vector2 pivot, bool showMarker, bool isCJKVertical, bool hasCustomDrawRect, Rect customDrawRect)
            {
                Text = text;
                Size = size;
                Pivot = pivot;
                ShowMarker = showMarker;
                IsCJKVertical = isCJKVertical;
                HasCustomDrawRect = hasCustomDrawRect;
                CustomDrawRect = customDrawRect;
            }
        }

        /// <summary>
        /// Core drawing method for an angled header.
        /// </summary>
        public static void Draw(AngledLabelLayout layout, bool isMouseOver, bool isSorted = false, bool sortDescending = false, Rect headerRect = default, PawnColumnDef column = null)
        {
            bool isCJKVertical = layout.IsCJKVertical;
            float rotation = isCJKVertical ? 0f : CurrentRotation;
            Vector2 labelSize = layout.Size;
            float horizontalOffset = EffectiveHorizontalOffset;

            // Center horizontally, and either bottom-anchor (CJK) or center-anchor (Standard) vertically.
            Rect drawRect;
            if (layout.HasCustomDrawRect)
            {
                drawRect = layout.CustomDrawRect;
            }
            else if (isCJKVertical)
            {
                drawRect = new Rect(0f, 0f, labelSize.x, labelSize.y);
                drawRect.x = headerRect.center.x - drawRect.width / 2f + horizontalOffset;
                drawRect.y = headerRect.yMax - labelSize.y - STEM_BOTTOM_GAP;
            }
            else
            {
                drawRect = new Rect(0f, 0f, headerRect.height, labelSize.y) { center = headerRect.center };
                drawRect.x += horizontalOffset;
            }

            Matrix4x4 originalMatrix = GUI.matrix;
            TextAnchor savedAnchor = Text.Anchor;
            GameFont savedFont = Text.Font;
            Color savedColor = GUI.color;
            bool savedWordWrap = Text.WordWrap;

            try
            {
                // Reset to identity matrix and unclip the pivot for screen-space rendering
                GUI.matrix = Matrix4x4.identity;
                Vector2 pivotPoint = GUIClipUtility.Unclip(drawRect.center);

                // Build transformation matrix: Translate to pivot -> Rotate -> Translate back
                Matrix4x4 transformationMatrix = originalMatrix;
                transformationMatrix *= Matrix4x4.TRS(pivotPoint, Quaternion.identity, Vector3.one);
                transformationMatrix *= Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, rotation), Vector3.one);
                transformationMatrix *= Matrix4x4.TRS(-pivotPoint, Quaternion.identity, Vector3.one);

                GUI.matrix = transformationMatrix;

                Text.Anchor = isCJKVertical ? TextAnchor.UpperCenter : TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;
                Text.WordWrap = false;

                // Highlights
                if (column != null && ColumnSelectionManager.IsSelected(column))
                {
                    GUI.color = HeaderUtility.Colors.SelectedHighlight;
                    GUI.DrawTexture(drawRect.ExpandedBy(2f), TexUI.HighlightTex);
                }

                if (isMouseOver)
                {
                    GUI.color = HeaderUtility.Colors.HoverHighlight;
                    GUI.DrawTexture(drawRect.ExpandedBy(2f), TexUI.HighlightTex);
                }

                // Text: Apply moved marker color only if color tint is enabled
                GUI.color = (layout.ShowMarker && BetterWorkTabMod.Settings.showMovedColumnColorTint) 
                    ? HeaderUtility.Colors.MovedMarkerColor 
                    : BetterWorkTabMod.Settings.angledHeaderColor;

                if (isCJKVertical)
                {
                    // East Asian Vertical Stacking: Draw characters one by one to avoid sideways characters.
                    // Sub-centering within the stack ensures characters are aligned regardless of glyph width variations.
                    float curY = drawRect.y;
                    float charH = Text.LineHeight * BetterWorkTabMod.Settings.cjkVerticalKerning; // Use user-configurable kerning
                    string text = layout.Text;
                    for (int i = 0; i < text.Length; i++)
                    {
                        Rect charRect = new Rect(drawRect.x, curY, drawRect.width, charH + 2f);
                        Widgets.Label(charRect, text[i].ToString());
                        curY += charH;
                    }
                }
                else
                {
                    Widgets.Label(drawRect, layout.Text);
                }

                // Underline: Traditionally vertical CJK text does not use work-tab-style underlines as they conflict with legibility.
                if (!BetterWorkTabMod.Settings.removeHeaderUnderline && !isCJKVertical)
                {
                    float textWidth = labelSize.x;
                    Vector2 underlineStart = new Vector2(drawRect.xMin, drawRect.yMax);
                    Vector2 underlineEnd = new Vector2(drawRect.xMin + textWidth, drawRect.yMax);
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
                HeaderUtility.DrawSortIndicator(headerRect, sortDescending);
            }
        }
    }
}
