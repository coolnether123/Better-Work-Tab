using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.DragDrop;

namespace Better_Work_Tab.UI
{
    public static class AngledLabelDrawer
    {
        public const float ROTATION_ANGLE = -60f;
        public static readonly float RotCos = Mathf.Cos(ROTATION_ANGLE * Mathf.Deg2Rad);
        public static readonly float RotSin = Mathf.Sin(ROTATION_ANGLE * Mathf.Deg2Rad);
        public const float STEM_BOTTOM_GAP = 2f;

        // Cached colors to avoid allocations every frame
        private static readonly Color SelectedColor = new Color(1f, 0.92f, 0.4f, 0.4f);
        private static readonly Color HoverColor = new Color(1f, 1f, 1f, 0.25f);
        private static readonly Color MarkerColor = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color SortColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);

        public readonly struct AngledLabelLayout
        {
            public readonly string Text;
            public readonly Vector2 Size;
            public readonly bool ShowMarker;

            public AngledLabelLayout(string text, Vector2 size, bool showMarker)
            {
                Text = text;
                Size = size;
                ShowMarker = showMarker;
            }
        }

        /// <summary>
        /// Snaps a logical pivot to physical pixel boundaries to prevent sub-pixel drift at fractional UI scales.
        /// </summary>
        private static Vector2 SnapPivotToPhysical(Vector2 logical)
        {
            float s = Prefs.UIScale;
            return new Vector2(
                Mathf.Round(logical.x * s) / s,
                Mathf.Round(logical.y * s) / s
            );
        }

        public static void Draw(AngledLabelLayout layout, bool isMouseOver, bool isSorted = false, bool sortDescending = false, Rect headerRect = default, PawnColumnDef column = null)
        {
            // 1. Start with the deterministic anchor: bottom-left of the header cell
            // This ensures we're always anchored to the same point within the header rect,
            // preventing accumulated drift across columns
            Vector2 basePivot = new Vector2(headerRect.xMin, headerRect.yMax);
            
            // 2. Add small design offsets in logical space (not scale-specific hacks)
            // These are intentional layout adjustments for aesthetics and spacing
            basePivot += new Vector2(2f, -STEM_BOTTOM_GAP);
            
            // 3. Snap the pivot in physical pixel space to prevent sub-pixel drift
            Vector2 snappedPivot = SnapPivotToPhysical(basePivot);

            // 4. Save State
            Matrix4x4 savedMatrix = GUI.matrix;
            TextAnchor savedAnchor = Text.Anchor;
            GameFont savedFont = Text.Font;
            Color savedColor = GUI.color;

            // 5. Apply Rotation
            Verse.UI.RotateAroundPivot(ROTATION_ANGLE, snappedPivot);

            try
            {
                // 6. Build label rect from the snapped pivot
                // Use the actual measured text width plus small padding (not hardcoded 200f)
                float w = layout.Size.x + 4f;  // Small padding for visual breathing room
                float h = layout.Size.y;
                
                // Bottom-left anchored at the snapped pivot
                Rect labelRect = new Rect(snappedPivot.x, snappedPivot.y - h, w, h);

                if (column != null && ColumnSelectionManager.IsSelected(column))
                {
                    Rect highlight = new Rect(labelRect.x, labelRect.y, layout.Size.x, layout.Size.y).ExpandedBy(2f);
                    // Distinct yellow highlight for selected columns
                    GUI.color = SelectedColor;
                    GUI.DrawTexture(highlight, TexUI.HighlightTex);
                    GUI.color = Color.white;
                }

                if (isMouseOver)
                {
                    Rect highlight = new Rect(labelRect.x, labelRect.y, layout.Size.x, layout.Size.y).ExpandedBy(2f);
                    GUI.color = HoverColor;
                    GUI.DrawTexture(highlight, TexUI.HighlightTex);
                    GUI.color = Color.white;
                }

                // Underline
                if (!BetterWorkTabMod.Settings.removeHeaderUnderline)
                {
                    Widgets.DrawLine(new Vector2(snappedPivot.x, snappedPivot.y), new Vector2(snappedPivot.x + layout.Size.x, snappedPivot.y), Color.white, 1f);
                }

                Text.Anchor = TextAnchor.LowerLeft;
                Text.Font = GameFont.Small;
                GUI.color = layout.ShowMarker ? MarkerColor : Color.white;

                // Use GUI.Label directly instead of Widgets.Label.
                // Widgets.Label performs its own pixel-snapping which assumes axis-alignment.
                // Since we are inside a rotated matrix, axis-aligned snapping causes jitter.
                GUI.Label(labelRect, layout.Text, Text.CurFontStyle);
            }
            finally
            {
                // 5. Restore State
                GUI.matrix = savedMatrix;
                Text.Anchor = savedAnchor;
                Text.Font = savedFont;
                GUI.color = savedColor;
            }

            if (isSorted && headerRect.width > 0f && headerRect.height > 0f)
            {
                DrawSortIndicator(headerRect, sortDescending);
            }
        }

        private static void DrawSortIndicator(Rect headerRect, bool descending)
        {
            Color savedColor = GUI.color;
            GameFont savedFont = Text.Font;
            TextAnchor savedAnchor = Text.Anchor;

            try
            {
                GUI.color = SortColor;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;

                // Move it to the bottom of the header area, centered horizontally
                Rect sortRect = new Rect(
                    headerRect.x + (headerRect.width - 6f) / 2f + 5f,
                    headerRect.yMax - 9f,
                    12f,
                    12f
                );

                Widgets.Label(sortRect, descending ? "▼" : "▲");
            }
            finally
            {
                GUI.color = savedColor;
                Text.Font = savedFont;
                Text.Anchor = savedAnchor;
            }
        }
    }
}