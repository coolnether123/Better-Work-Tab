using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.DragDrop;

namespace Better_Work_Tab.UI
{
    public static class AngledLabelDrawer
    {
        public const float ROTATION_ANGLE = -60f;
        public static float CurrentRotation => BetterWorkTabMod.Settings.enableAngledHeaders ? BetterWorkTabMod.Settings.angledHeaderRotation : ROTATION_ANGLE;
        public static float CurrentRotCos => Mathf.Cos(CurrentRotation * Mathf.Deg2Rad);
        public static float CurrentRotSin => Mathf.Sin(CurrentRotation * Mathf.Deg2Rad);
        public const float STEM_BOTTOM_GAP = 2f;

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
        /// Snaps a logical UI coordinate to the nearest physical monitor pixel.
        /// This prevents the "staircase" drift at 1.25x or 1.5x scales.
        /// </summary>
        private static float SnapToPhysical(float coord)
        {
            float scale = Prefs.UIScale;
        // Formula: floor(coord * scale) / scale
            return Mathf.Floor(coord * scale + 0.001f) / scale;
        }

        public static Vector2 GetOffset(float scale)
        {
            if (BetterWorkTabMod.Settings.scaleFixMode == BetterWorkTabSettings.ScaleFixMode.Manual)
            {
                return new Vector2(BetterWorkTabMod.Settings.angledHeaderXOffset, BetterWorkTabMod.Settings.angledHeaderYOffset);
            }
            else
            {
                if (!Mathf.Approximately(scale, 1f))
                {
                    float factor = (scale - 1f) / 0.25f;
                    return new Vector2(factor * -85f, factor * 49f);
                }
                return Vector2.zero;
            }
        }

        public static void Draw(AngledLabelLayout layout, bool isMouseOver, bool isSorted = false, bool sortDescending = false, Rect headerRect = default, PawnColumnDef column = null)
        {
            float rotation = CurrentRotation;
            float scale = Prefs.UIScale;

            // 1. Calculate the Snapped Pivot
            Vector2 snappedPivot = new Vector2(
                SnapToPhysical(layout.Pivot.x),
                SnapToPhysical(layout.Pivot.y)
            ) + GetOffset(scale);

            // 2. Save State
            Matrix4x4 savedMatrix = GUI.matrix;
            TextAnchor savedAnchor = Text.Anchor;
            GameFont savedFont = Text.Font;
            Color savedColor = GUI.color;

            // 3. Apply Rotation
            Verse.UI.RotateAroundPivot(rotation, snappedPivot);

            try
            {
                // 4. Draw Relative to Snapped Pivot
                float textHeight = layout.Size.y;
                Rect labelRect = new Rect(snappedPivot.x, snappedPivot.y - textHeight, 200f, textHeight);

                if (column != null && ColumnSelectionManager.IsSelected(column))
                {
                    Rect highlight = new Rect(labelRect.x, labelRect.y, layout.Size.x, layout.Size.y).ExpandedBy(2f);
                    GUI.color = new Color(1f, 0.92f, 0.4f, 0.4f);
                    GUI.DrawTexture(highlight, TexUI.HighlightTex);
                    GUI.color = Color.white;
                }

                if (isMouseOver)
                {
                    Rect highlight = new Rect(labelRect.x, labelRect.y, layout.Size.x, layout.Size.y).ExpandedBy(2f);
                    GUI.color = new Color(1f, 1f, 1f, 0.25f);
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
                GUI.color = layout.ShowMarker ? new Color(1f, 0.85f, 0.2f, 1f) : Color.white;

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

            // Draw column center indicator at bottom of header if enabled
            if (BetterWorkTabMod.Settings.scaleFixMode == BetterWorkTabSettings.ScaleFixMode.Manual && BetterWorkTabMod.Settings.showRedCenterLine && headerRect != default)
            {
                float centerX = headerRect.x + (headerRect.width * 0.5f);
                float bottomY = headerRect.yMax;

                GUI.color = Color.red;
                Widgets.DrawLine(
                    new Vector2(centerX, bottomY),
                    new Vector2(centerX, bottomY + 3f),
                    Color.red,
                    2f); // 2f thickness makes it visible
                GUI.color = Color.white;
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
            // Move it to the bottom of the header area, centered horizontally
            Rect sortRect = new Rect(headerRect.x + (headerRect.width - 6f) / 2f + 5f, headerRect.yMax - 9f, 12f, 12f);
            Widgets.Label(sortRect, descending ? "▼" : "▲");
            GUI.color = Color.white;
        }
    }
}