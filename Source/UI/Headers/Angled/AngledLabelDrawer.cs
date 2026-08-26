using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Projection;

namespace Better_Work_Tab.UI.Headers.Angled
{
    /// <summary>
    /// Static utility for drawing rotated (angled) text labels with underlines and highlights.
    /// Handles the matrix transformations required for rotation.
    /// </summary>
    public static class AngledLabelDrawer
    {
        private const int TransformCacheLimit = 256;
        private static readonly Dictionary<HeaderTransformKey, Matrix4x4> TransformCache =
            new Dictionary<HeaderTransformKey, Matrix4x4>();

        /// <summary>
        /// Default rotation angle if the mod setting is somehow invalid.
        /// </summary>
        public const float DefaultRotationAngle = -60f;

        /// <summary>
        /// The gap between the bottom of the stem and the start of the rotated label.
        /// </summary>
        public const float STEM_BOTTOM_GAP = 2f;

        /// <summary>
        /// Returns the prepared rotation angle; the packet is cached across frames.
        /// </summary>
        public static float CurrentRotation => HeaderDrawingCoordinator.CapturePresentation().Rotation;
        
        /// <summary>
        /// Returns the prepared rotation cosine.
        /// </summary>
        public static float CurrentRotCos => HeaderDrawingCoordinator.CapturePresentation().RotationCos;
        
        /// <summary>
        /// Returns the prepared rotation sine.
        /// </summary>
        public static float CurrentRotSin => HeaderDrawingCoordinator.CapturePresentation().RotationSin;

        /// <summary>
        /// Returns the prepared horizontal offset. At -90 degrees it is forced
        /// to zero for vertical stacking; the setting is preserved for restore.
        /// </summary>
        public static float EffectiveHorizontalOffset
        {
            get
            {
                return HeaderDrawingCoordinator.CapturePresentation().EffectiveHorizontalOffset;
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
            var columns = table.Columns;
            if (columns == null) return 0f;

            HeaderPresentationPacket presentation = HeaderDrawingCoordinator.CapturePresentation();
            float rotation = presentation.Rotation;
            float absSin = Mathf.Abs(presentation.RotationSin);
            float absCos = Mathf.Abs(presentation.RotationCos);

            foreach (var col in columns)
            {
                if (col.Worker is PawnColumnWorker_WorkPriority && col.workType != null)
                {
                    // For height calculation, the moved indicator is incorporated to maintain layout stability.
                    AngledHeaderCache.CachedTextMetrics metrics = AngledHeaderCache.GetHeaderTextMetrics(col.workType, true, WorkGiverHeaderLabelStyle.Standard, in presentation);

                    float h;
                    if (metrics.IsCJKVertical)
                    {
                        h = metrics.Size.y;
                    }
                    else
                    {
                        // Height of a rotated rectangle: width*sin(theta) + height*cos(theta)
                        h = (metrics.Size.x * absSin) + (metrics.Size.y * absCos);
                    }

                    if (h > maxH) maxH = h;
                }
            }

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
            public readonly float UnderlineWidth;
            public readonly bool HasCustomDrawRect;
            public readonly Rect CustomDrawRect;
            public readonly float Alpha;
            /// <summary>
            /// Indicates if this label should be drawn using character-by-character vertical stacking 
            /// instead of standard matrix rotation.
            /// </summary>
            public readonly bool IsCJKVertical;

            public AngledLabelLayout(string text, Vector2 size, Vector2 pivot, bool showMarker, bool isCJKVertical = false)
                : this(text, size, pivot, showMarker, size.x, isCJKVertical, false, default, 1f)
            {
            }

            public AngledLabelLayout(string text, Vector2 size, Vector2 pivot, bool showMarker, bool isCJKVertical, Rect customDrawRect)
                : this(text, size, pivot, showMarker, size.x, isCJKVertical, true, customDrawRect, 1f)
            {
            }

            public AngledLabelLayout(string text, Vector2 size, Vector2 pivot, bool showMarker, bool isCJKVertical, Rect customDrawRect, float underlineWidth)
                : this(text, size, pivot, showMarker, underlineWidth, isCJKVertical, true, customDrawRect, 1f)
            {
            }

            private AngledLabelLayout(
                string text,
                Vector2 size,
                Vector2 pivot,
                bool showMarker,
                float underlineWidth,
                bool isCJKVertical,
                bool hasCustomDrawRect,
                Rect customDrawRect,
                float alpha)
            {
                Text = text;
                Size = size;
                Pivot = pivot;
                ShowMarker = showMarker;
                UnderlineWidth = underlineWidth;
                IsCJKVertical = isCJKVertical;
                HasCustomDrawRect = hasCustomDrawRect;
                CustomDrawRect = customDrawRect;
                Alpha = Mathf.Clamp01(alpha);
            }

            public AngledLabelLayout WithAlpha(float alpha)
            {
                return new AngledLabelLayout(
                    Text,
                    Size,
                    Pivot,
                    ShowMarker,
                    UnderlineWidth,
                    IsCJKVertical,
                    HasCustomDrawRect,
                    CustomDrawRect,
                    alpha);
            }
        }

        /// <summary>
        /// Core drawing method for an angled header.
        /// </summary>
        public static void Draw(AngledLabelLayout layout, bool isMouseOver, bool isSorted = false, bool sortDescending = false, Rect headerRect = default, PawnColumnDef column = null)
        {
            HeaderPresentationPacket presentation = HeaderDrawingCoordinator.CapturePresentation();
            Draw(layout, isMouseOver, isSorted, sortDescending, headerRect, column, in presentation);
        }

        internal static void Draw(
            AngledLabelLayout layout,
            bool isMouseOver,
            bool isSorted,
            bool sortDescending,
            Rect headerRect,
            PawnColumnDef column,
            in HeaderPresentationPacket presentation)
        {
            bool isCJKVertical = layout.IsCJKVertical;
            float rotation = isCJKVertical ? 0f : presentation.Rotation;
            Vector2 labelSize = layout.Size;
            float horizontalOffset = presentation.EffectiveHorizontalOffset;

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

            bool subWorkOrderingAvailable = !WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked;
            if (subWorkOrderingAvailable &&
                SubWorkDrilldownState.TryGetHeaderTransitionOffset(column, headerRect.width, out float transitionOffsetX))
            {
                drawRect.x += transitionOffsetX;
            }

            if (subWorkOrderingAvailable && SubWorkDrilldownState.IsActive && !isCJKVertical)
            {
                drawRect.y += SubWorkDrilldownState.HeaderAnchorVisualOffsetY;
            }

            Matrix4x4 originalMatrix = GUI.matrix;
            TextAnchor savedAnchor = Text.Anchor;
            GameFont savedFont = Text.Font;
            Color savedColor = GUI.color;
            bool savedWordWrap = Text.WordWrap;
            float flipScale = subWorkOrderingAvailable ? SubWorkDrilldownState.HeaderFlipScale : 1f;
            float flipAlpha = subWorkOrderingAvailable ? SubWorkDrilldownState.HeaderFlipAlpha : 1f;
            float parentAlpha = subWorkOrderingAvailable ? SubWorkDrilldownState.ParentWorkContentAlpha : 1f;
            if (subWorkOrderingAvailable &&
                SubWorkDrilldownState.TryGetHeaderTransitionVisuals(column, out float transitionFlipScale, out float transitionSubAlpha, out float transitionParentAlpha))
            {
                flipScale = transitionFlipScale;
                flipAlpha = transitionSubAlpha;
                parentAlpha = transitionParentAlpha;
            }

            float labelAlpha = Mathf.Clamp01(layout.Alpha);
            DrawParentHeaderGhost(layout, headerRect, column, rotation, horizontalOffset, originalMatrix, parentAlpha * labelAlpha, in presentation);

            try
            {
                // Reset to identity matrix and unclip the pivot for screen-space rendering
                GUI.matrix = Matrix4x4.identity;
                Vector2 pivotPoint = GUIClipUtility.Unclip(drawRect.center);

                Vector2 scale = flipScale < 0.999f
                    ? subWorkOrderingAvailable && SubWorkDrilldownState.UsePixelWaveTransition
                        ? new Vector2(flipScale, 1f)
                        : new Vector2(1f, flipScale)
                    : Vector2.one;
                GUI.matrix = GetTransformMatrix(originalMatrix, pivotPoint, rotation, scale);

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
                GUI.color = (layout.ShowMarker && presentation.ShowMovedColorTint)
                    ? presentation.MovedMarkerColor
                    : presentation.AngledColor;
                float visibleAlpha = flipAlpha * labelAlpha;
                GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, GUI.color.a * visibleAlpha);

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
                if (!presentation.RemoveUnderline &&
                    !isCJKVertical)
                {
                    float textWidth = Mathf.Min(layout.UnderlineWidth, drawRect.width);
                    Color underlineColor = presentation.UnderlineColor;
                    underlineColor.a *= visibleAlpha;
                    DrawHorizontalUnderline(drawRect.xMin, drawRect.yMax, textWidth, underlineColor);
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

        private static void DrawParentHeaderGhost(
            AngledLabelLayout currentLayout,
            Rect headerRect,
            PawnColumnDef column,
            float currentRotation,
            float horizontalOffset,
            Matrix4x4 originalMatrix,
            float alpha,
            in HeaderPresentationPacket presentation)
        {
            if (alpha <= 0.001f ||
                headerRect.width <= 0f ||
                headerRect.height <= 0f ||
                column?.workType == null)
            {
                return;
            }

            AngledHeaderCache.CachedTextMetrics parentMetrics =
                AngledHeaderCache.GetParentTextMetrics(column.workType, false, in presentation);
            string parentText = parentMetrics.Label;
            if (parentText.NullOrEmpty() ||
                parentText == HeaderUtility.RemoveMovedMarker(currentLayout.Text))
            {
                return;
            }

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            bool oldWordWrap = Text.WordWrap;
            Matrix4x4 oldMatrix = GUI.matrix;

            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;

                bool isCJKVertical = parentMetrics.IsCJKVertical;
                float rotation = isCJKVertical ? 0f : currentRotation;
                Vector2 size = parentMetrics.Size;

                Rect drawRect;
                if (isCJKVertical)
                {
                    drawRect = new Rect(0f, 0f, size.x, size.y);
                    drawRect.x = headerRect.center.x - drawRect.width / 2f + horizontalOffset;
                    drawRect.y = headerRect.yMax - size.y - STEM_BOTTOM_GAP;
                }
                else
                {
                    drawRect = new Rect(0f, 0f, headerRect.height, size.y) { center = headerRect.center };
                    drawRect.x += horizontalOffset;
                    if (!WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked &&
                        SubWorkDrilldownState.IsActive)
                    {
                        drawRect.y += SubWorkDrilldownState.HeaderAnchorVisualOffsetY;
                    }
                }

                GUI.matrix = Matrix4x4.identity;
                Vector2 pivotPoint = GUIClipUtility.Unclip(drawRect.center);
                GUI.matrix = GetTransformMatrix(originalMatrix, pivotPoint, rotation, Vector2.one);

                Text.Anchor = isCJKVertical ? TextAnchor.UpperCenter : TextAnchor.MiddleLeft;
                GUI.color = presentation.AngledColor;
                GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, GUI.color.a * alpha);

                if (isCJKVertical)
                {
                    float curY = drawRect.y;
                    float charH = Text.LineHeight * BetterWorkTabMod.Settings.cjkVerticalKerning;
                    for (int i = 0; i < parentText.Length; i++)
                    {
                        Rect charRect = new Rect(drawRect.x, curY, drawRect.width, charH + 2f);
                        Widgets.Label(charRect, parentText[i].ToString());
                        curY += charH;
                    }
                }
                else
                {
                    Widgets.Label(drawRect, parentText);
                }

                if (!presentation.RemoveUnderline &&
                    !isCJKVertical)
                {
                    float underlineWidth = Mathf.Min(size.x, drawRect.width);
                    Color underlineColor = presentation.UnderlineColor;
                    underlineColor.a *= alpha;
                    DrawHorizontalUnderline(drawRect.xMin, drawRect.yMax, underlineWidth, underlineColor);
                }
            }
            finally
            {
                Text.Anchor = oldAnchor;
                Text.Font = oldFont;
                GUI.color = oldColor;
                Text.WordWrap = oldWordWrap;
                GUI.matrix = oldMatrix;
            }
        }

        internal static Matrix4x4 GetTransformMatrix(
            Matrix4x4 originalMatrix,
            Vector2 pivot,
            float rotation,
            Vector2 scale)
        {
            var key = new HeaderTransformKey(originalMatrix, pivot, rotation, scale);
            if (TransformCache.TryGetValue(key, out Matrix4x4 cached))
            {
                return cached;
            }

            Matrix4x4 result = originalMatrix;
            result *= Matrix4x4.TRS(pivot, Quaternion.identity, Vector3.one);
            result *= Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, rotation), Vector3.one);
            if (scale != Vector2.one)
            {
                result *= Matrix4x4.TRS(
                    Vector3.zero,
                    Quaternion.identity,
                    new Vector3(scale.x, scale.y, 1f));
            }
            result *= Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);

            if (TransformCache.Count >= TransformCacheLimit)
            {
                TransformCache.Clear();
            }
            TransformCache[key] = result;
            return result;
        }

        private static void DrawHorizontalUnderline(float x, float y, float width, Color color)
        {
            if (width < 0.01f)
            {
                return;
            }

            // Equivalent to Widgets.DrawLine for a horizontal one-pixel request: that helper
            // expands to this three-pixel AA texture but also computes a general line rotation.
            GUI.DrawTexture(
                new Rect(x, y - 1.5f, width, 3f),
                Widgets.LineTexAA,
                ScaleMode.StretchToFill,
                true,
                0f,
                color,
                0f,
                0f);
        }

        private readonly struct HeaderTransformKey : IEquatable<HeaderTransformKey>
        {
            private readonly Matrix4x4 _originalMatrix;
            private readonly Vector2 _pivot;
            private readonly float _rotation;
            private readonly Vector2 _scale;

            internal HeaderTransformKey(
                Matrix4x4 originalMatrix,
                Vector2 pivot,
                float rotation,
                Vector2 scale)
            {
                _originalMatrix = originalMatrix;
                _pivot = pivot;
                _rotation = rotation;
                _scale = scale;
            }

            public bool Equals(HeaderTransformKey other)
            {
                return _originalMatrix.Equals(other._originalMatrix) &&
                       _pivot.Equals(other._pivot) &&
                       _rotation.Equals(other._rotation) &&
                       _scale.Equals(other._scale);
            }

            public override bool Equals(object obj)
            {
                return obj is HeaderTransformKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _originalMatrix.GetHashCode();
                    hash = (hash * 397) ^ _pivot.GetHashCode();
                    hash = (hash * 397) ^ _rotation.GetHashCode();
                    return (hash * 397) ^ _scale.GetHashCode();
                }
            }
        }
    }
}
