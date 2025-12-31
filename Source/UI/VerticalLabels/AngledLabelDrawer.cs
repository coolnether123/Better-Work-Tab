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
        public static float GetNeededHeight(PawnTable table)
        {
            if (table == null) return 0f;

            float maxH = 0f;
            var tableDef = PawnTableDefOf.Work;
            if (tableDef?.columns == null) return 0f;

            float absSin = Mathf.Abs(Mathf.Sin(CurrentRotation * Mathf.Deg2Rad));
            float absCos = Mathf.Abs(Mathf.Cos(CurrentRotation * Mathf.Deg2Rad));

            // Set font to match drawing for accurate measurement
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;

            foreach (var col in tableDef.columns)
            {
                // We only care about work priority columns which are the ones we angle
                if (col.Worker is PawnColumnWorker_WorkPriority && col.workType != null)
                {
                    string label = col.workType.labelShort;
                    if (string.IsNullOrEmpty(label))
                        label = col.workType.label;
                    if (string.IsNullOrEmpty(label))
                        label = col.workType.defName;
                    
                    // ALWAYS reserve space for the marker to prevent height flickering when columns are moved
                    label += "*";

                    Vector2 size = Text.CalcSize(label);
                    
                    // Height calculation for a rotated rectangle: width*sin(theta) + height*cos(theta)
                    float h = (size.x * absSin) + (size.y * absCos);
                    if (h > maxH) maxH = h;
                }
            }

            Text.Font = oldFont;
            return maxH + STEM_BOTTOM_GAP;
        }
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

                // 5. Draw highlights using the transformed matrix.
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

                // 6. Draw the text with user-defined or default colors.
                GUI.color = layout.ShowMarker ? new Color(1f, 0.85f, 0.2f, 1f) : BetterWorkTabMod.Settings.angledHeaderColor;
                Widgets.Label(rotatedRect, layout.Text);

                // 7. Draw the underline.
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
            // Move it to the bottom of the header area, centered horizontally
            Rect sortRect = new Rect(headerRect.x + (headerRect.width - 6f) / 2f + 5f, headerRect.yMax - 9f, 12f, 12f);
            Widgets.Label(sortRect, descending ? "▼" : "▲");
            GUI.color = Color.white;
        }
    }
}