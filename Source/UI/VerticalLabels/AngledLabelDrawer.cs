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
            
            // Calculate the size of the label
            Vector2 labelSize = layout.Size;
            
            // Create a rectangle for the rotated label centered on the original header rectangle
            Rect rotatedRect = new Rect(0f, 0f, headerRect.height, labelSize.y) { center = headerRect.center };
            
            // Save state
            Matrix4x4 originalMatrix = GUI.matrix;
            TextAnchor savedAnchor = Text.Anchor;
            GameFont savedFont = Text.Font;
            Color savedColor = GUI.color;
            bool savedWordWrap = Text.WordWrap;
            
            try
            {
                // Reset GUI matrix to identity
                GUI.matrix = Matrix4x4.identity;
                
                // Set the pivot point for rotation using Unclip
                Vector2 pivotPoint = GUIClipUtility.Unclip(rotatedRect.center);
                
                // Build transformation matrix
                Matrix4x4 transformationMatrix = originalMatrix;
                transformationMatrix *= Matrix4x4.TRS(pivotPoint, Quaternion.identity, Vector3.one);
                transformationMatrix *= Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, rotation), Vector3.one);
                transformationMatrix *= Matrix4x4.TRS(-pivotPoint, Quaternion.identity, Vector3.one);
                
                // Apply the transformation
                GUI.matrix = transformationMatrix;
                
                // Set drawing properties
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                
                // Draw selection highlight
                if (column != null && ColumnSelectionManager.IsSelected(column))
                {
                    GUI.color = new Color(1f, 0.92f, 0.4f, 0.4f);
                    GUI.DrawTexture(rotatedRect.ExpandedBy(2f), TexUI.HighlightTex);
                }
                
                // Draw mouse-over highlight
                if (isMouseOver)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.25f);
                    GUI.DrawTexture(rotatedRect.ExpandedBy(2f), TexUI.HighlightTex);
                }
                
                // Set text color
                GUI.color = layout.ShowMarker ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(0.8f, 0.8f, 0.8f);
                
                // Draw the label
                Widgets.Label(rotatedRect, layout.Text);
                
                // Draw underline (text-width length, white color)
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
                // Restore state
                GUI.matrix = originalMatrix;
                Text.Anchor = savedAnchor;
                Text.Font = savedFont;
                GUI.color = savedColor;
                Text.WordWrap = savedWordWrap;
            }
            
            // Draw sorting indicators
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