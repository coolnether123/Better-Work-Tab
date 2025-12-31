using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.DragDrop;

namespace Better_Work_Tab.UI
{
    public static class AngledLabelDrawer
    {
        public const float ROTATION_ANGLE = -60f;
        public static float CurrentRotation => BetterWorkTabMod.Settings.enableAngledHeaders ? BetterWorkTabMod.Settings.angledHeaderRotation : 0f;
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



        public static void Draw(AngledLabelLayout layout, bool isMouseOver, bool isSorted = false, bool sortDescending = false, Rect headerRect = default, PawnColumnDef column = null)
        {
            float rotation = CurrentRotation;
            
            // 1. Calculate the spatial anchor: The horizontal center of the column + user-defined offset, 
            // pinned to the bottom of the header area with a small vertical gap.
            float anchorX = headerRect.center.x + BetterWorkTabMod.Settings.angledHeaderHorizontalOffset;
            float anchorY = headerRect.yMax - STEM_BOTTOM_GAP;
            
            // 2. Define the label dimensions. We orient the rectangle so that its bottom-left corner 
            // aligns with the anchor point before rotation is applied.
            Rect rotatedRect = new Rect(anchorX, anchorY - layout.Size.y, layout.Size.x, layout.Size.y);
            
            // 3. Persist current GUI state to ensure restoration after custom transformation.
            Matrix4x4 originalMatrix = GUI.matrix;
            TextAnchor savedAnchor = Text.Anchor;
            GameFont savedFont = Text.Font;
            Color savedColor = GUI.color;
            bool savedWordWrap = Text.WordWrap;
            
            try
            {
                // 4. Apply transformation: Pivot rotation around the anchor point (bottom-left of the text).
                Vector2 pivotPoint = new Vector2(rotatedRect.xMin, rotatedRect.yMax);
                GUIUtility.RotateAroundPivot(rotation, pivotPoint);
                
                // 5. Configure text rendering properties.
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                
                // 6. Project selection and interaction highlights using the transformed matrix.
                if (column != null && ColumnSelectionManager.IsSelected(column))
                {
                    GUI.color = new Color(1f, 0.92f, 0.4f, 0.4f);
                    GUI.DrawTexture(rotatedRect.ExpandedBy(2f), TexUI.HighlightTex);
                }
                
                if (isMouseOver)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.25f);
                    GUI.DrawTexture(rotatedRect.ExpandedBy(2f), TexUI.HighlightTex);
                }
                
                // 7. Resolve the final text color: prioritizing the Column Marker (gold) or the user's custom setting.
                GUI.color = layout.ShowMarker ? new Color(1f, 0.85f, 0.2f, 1f) : BetterWorkTabMod.Settings.angledHeaderColor;
                
                // 8. Execute final draw calls for text and visual indicators.
                Widgets.Label(rotatedRect, layout.Text);
                
                // Draw a stylistic underline that follows the rotation of the label.
                if (!BetterWorkTabMod.Settings.removeHeaderUnderline)
                {
                    float textWidth = layout.Size.x;
                    Vector2 underlineStart = new Vector2(rotatedRect.xMin, rotatedRect.yMax);
                    Vector2 underlineEnd = new Vector2(rotatedRect.xMin + textWidth, rotatedRect.yMax);
                    Widgets.DrawLine(underlineStart, underlineEnd, Color.white, 1f);
                }
            }
            finally
            {
                // 9. Revert GUI state to prevent layout contamination.
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