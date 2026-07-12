using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Draws a semantic preview over representative geometry from the live Work table.
    /// It does not mutate settings or layout state.
    /// </summary>
    internal static class WorkTabColorPreviewRenderer
    {
        internal static void Draw(IWorkTabLayoutController layout, Rect windowRect)
        {
            if (Event.current.type != EventType.Repaint ||
                layout?.Columns == null ||
                layout.Rows == null ||
                !WorkTabColorPreviewController.Instance.TryGetPreview(out WorkTabColorPreview preview) ||
                !TryGetPreviewGeometry(layout, windowRect, out Rect rowRect, out Rect columnRect, out Rect cellRect, out Rect headerRect, out Rect dividerRect))
            {
                return;
            }

            switch (preview.Target)
            {
                case WorkTabColorPreviewTarget.Row:
                    DrawHighlight(rowRect, preview.Color);
                    break;
                case WorkTabColorPreviewTarget.Column:
                    DrawHighlight(columnRect, preview.Color);
                    break;
                case WorkTabColorPreviewTarget.RowAndColumn:
                    DrawHighlight(rowRect, preview.Color);
                    DrawHighlight(columnRect, preview.Color);
                    break;
                case WorkTabColorPreviewTarget.Header:
                    DrawHighlight(headerRect, preview.Color);
                    break;
                case WorkTabColorPreviewTarget.Divider:
                    DrawHighlight(dividerRect, preview.Color);
                    break;
                case WorkTabColorPreviewTarget.CellText:
                    DrawCellTextPreview(cellRect, preview.Color);
                    break;
                case WorkTabColorPreviewTarget.CellIndicator:
                    DrawCellIndicatorPreview(cellRect, preview.Color);
                    break;
                case WorkTabColorPreviewTarget.LegendOnly:
                    break;
                default:
                    DrawHighlight(cellRect, preview.Color);
                    break;
            }

            DrawLegend(windowRect, preview);
        }

        private static bool TryGetPreviewGeometry(
            IWorkTabLayoutController layout,
            Rect windowRect,
            out Rect rowRect,
            out Rect columnRect,
            out Rect cellRect,
            out Rect headerRect,
            out Rect dividerRect)
        {
            rowRect = default;
            columnRect = default;
            cellRect = default;
            headerRect = default;
            dividerRect = default;

            WorkTabLayoutColumn? workColumn = null;
            float gridXMin = float.MaxValue;
            float gridXMax = float.MinValue;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn candidate = layout.Columns[i];
                if (candidate.HeaderRect.xMax < windowRect.xMin || candidate.HeaderRect.xMin > windowRect.xMax)
                {
                    continue;
                }

                // Horizontal previews represent a pawn row, so include the pawn-name,
                // utility, root Work, and hosted Fluffy sub-work columns in the grid span.
                gridXMin = Mathf.Min(gridXMin, candidate.HeaderRect.xMin);
                gridXMax = Mathf.Max(gridXMax, candidate.HeaderRect.xMax);

                if (WorkTabColumnHighlightUtility.IsHighlightableWorkColumn(candidate))
                {
                    if (!workColumn.HasValue)
                    {
                        workColumn = candidate;
                    }
                }
            }

            WorkTabLayoutRow? pawnRow = null;
            WorkTabLayoutRow? dividerRow = null;
            float gridYMin = float.MaxValue;
            float gridYMax = float.MinValue;
            for (int i = 0; i < layout.Rows.Count; i++)
            {
                WorkTabLayoutRow candidate = layout.Rows[i];
                Rect candidateRect = layout.GetScreenRect(candidate);
                if (candidateRect.yMax < windowRect.yMin || candidateRect.yMin > windowRect.yMax)
                {
                    continue;
                }

                if (candidate.IsDivider && !dividerRow.HasValue)
                {
                    dividerRow = candidate;
                }
                else if (candidate.Pawn != null && !pawnRow.HasValue)
                {
                    pawnRow = candidate;
                }

                if (candidate.Pawn != null)
                {
                    gridYMin = Mathf.Min(gridYMin, candidateRect.yMin);
                    gridYMax = Mathf.Max(gridYMax, candidateRect.yMax);
                }
            }

            if (!workColumn.HasValue || !pawnRow.HasValue)
            {
                return false;
            }

            headerRect = workColumn.Value.HeaderRect;
            Rect pawnScreenRect = layout.GetScreenRect(pawnRow.Value);
            rowRect = Rect.MinMaxRect(gridXMin, pawnScreenRect.y, gridXMax, pawnScreenRect.yMax);
            columnRect = new Rect(
                headerRect.x,
                gridYMin,
                workColumn.Value.Width,
                Mathf.Max(0f, gridYMax - gridYMin));
            cellRect = new Rect(columnRect.x, rowRect.y, columnRect.width, rowRect.height);
            dividerRect = dividerRow.HasValue
                ? new Rect(gridXMin, layout.GetScreenRect(dividerRow.Value).y, gridXMax - gridXMin, layout.GetScreenRect(dividerRow.Value).height)
                : new Rect(gridXMin, rowRect.yMax - 4f, gridXMax - gridXMin, 4f);

            rowRect = ClipToWindow(rowRect, windowRect);
            columnRect = ClipToWindow(columnRect, windowRect);
            cellRect = ClipToWindow(cellRect, windowRect);
            headerRect = ClipToWindow(headerRect, windowRect);
            dividerRect = ClipToWindow(dividerRect, windowRect);
            return true;
        }

        private static Rect ClipToWindow(Rect rect, Rect windowRect)
        {
            float xMin = Mathf.Max(rect.xMin, windowRect.xMin);
            float yMin = Mathf.Max(rect.yMin, windowRect.yMin);
            float xMax = Mathf.Min(rect.xMax, windowRect.xMax);
            float yMax = Mathf.Min(rect.yMax, windowRect.yMax);
            return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
        }

        private static void DrawHighlight(Rect rect, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Color fill = color;
            fill.a = Mathf.Min(fill.a, 0.42f);
            Widgets.DrawBoxSolid(rect, fill);
            DrawBorder(rect, color, 2f);
        }

        private static void DrawCellTextPreview(Rect rect, Color color)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.035f, 0.04f, 0.045f, 0.92f));
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = color;
            Widgets.Label(rect, "12");
            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private static void DrawCellIndicatorPreview(Rect rect, Color color)
        {
            Rect indicator = rect.ContractedBy(3f);
            DrawBorder(indicator, color, 2f);
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            Widgets.DrawBoxSolid(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            Widgets.DrawBoxSolid(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
            Widgets.DrawBoxSolid(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
        }

        private static void DrawLegend(Rect windowRect, WorkTabColorPreview preview)
        {
            const float width = 300f;
            Rect legend = new Rect(windowRect.xMax - width - 8f, windowRect.y + 8f, width, 30f);
            Widgets.DrawBoxSolid(legend, new Color(0.035f, 0.04f, 0.045f, 0.96f));
            DrawBorder(legend, preview.Color, 2f);

            Rect swatch = new Rect(legend.x + 7f, legend.y + 6f, 18f, 18f);
            Widgets.DrawBoxSolid(swatch, preview.Color);
            Widgets.DrawBox(swatch);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            GUI.color = Color.white;
            Widgets.Label(
                new Rect(swatch.xMax + 7f, legend.y, legend.width - 40f, legend.height),
                "Live preview: " + preview.Label);
            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            Text.WordWrap = oldWordWrap;
        }
    }
}
