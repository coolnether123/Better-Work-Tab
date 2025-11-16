using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Handles ctrl+drag row reordering against the layout controller.
    /// </summary>
    public class RowDragController
    {
        private DisplayElement _draggedElement;
        private WorkTabLayoutRow _rowSnapshot;
        private float _dragOffsetY;
        private Vector2 _mouse;
        private int _targetIndex = -1;

        public bool IsDragging => _draggedElement != null;

        public void HandleInput(Event evt, IWorkTabLayoutController layout)
        {
            if (layout?.Table == null || evt == null)
            {
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.control)
            {
                if (layout.TryGetRowAt(evt.mousePosition, out var row))
                {
                    BeginDrag(layout, row, evt.mousePosition);
                    evt.Use();
                }
                return;
            }

            if (!IsDragging)
            {
                return;
            }

            if (evt.type == EventType.MouseDrag)
            {
                _mouse = evt.mousePosition;
                UpdateInsertionIndex(layout, evt.mousePosition);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp)
            {
                layout.MoveElement(_draggedElement, _targetIndex >= 0 ? _targetIndex : _rowSnapshot.VisualIndex);
                Reset();
                evt.Use();
            }
            else if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                Reset();
                evt.Use();
            }
        }

        public void DrawOverlay(IWorkTabLayoutController layout)
        {
            if (!IsDragging || layout?.Table == null)
            {
                return;
            }

            var baseRect = layout.GetScreenRect(_rowSnapshot);
            var ghostRect = baseRect;
            ghostRect.y = _mouse.y - _dragOffsetY;
            Widgets.DrawBoxSolid(ghostRect, new Color(0f, 0f, 0f, 0.25f));
            Widgets.DrawBox(ghostRect, 1);

            if (_rowSnapshot.IsDivider && _rowSnapshot.Divider != null)
            {
                DrawDividerGhostLabel(ghostRect, _rowSnapshot.Divider);
            }
            else if (_rowSnapshot.Pawn != null)
            {
                DrawPawnGhostLabel(ghostRect, _rowSnapshot.Pawn);
            }

            if (_targetIndex >= 0)
            {
                float lineY = CalculateInsertionY(layout, _targetIndex);
                var lineRect = new Rect(layout.TableOrigin.x, lineY - 1f, layout.Table.Size.x - 16f, 2f);
                Widgets.DrawBoxSolid(lineRect, Color.white);
            }
        }

        private void BeginDrag(IWorkTabLayoutController layout, WorkTabLayoutRow row, Vector2 mousePosition)
        {
            _draggedElement = row.Element;
            _rowSnapshot = row;
            _mouse = mousePosition;
            var rect = layout.GetScreenRect(row);
            _dragOffsetY = mousePosition.y - rect.y;
            _targetIndex = row.VisualIndex;
        }

        private void UpdateInsertionIndex(IWorkTabLayoutController layout, Vector2 mousePosition)
        {
            float headerTop = layout.TableOrigin.y + layout.HeaderHeight;
            float contentY = mousePosition.y - headerTop + layout.Table.scrollPosition.y;

            int newIndex = layout.Rows.Count;
            for (int i = 0; i < layout.Rows.Count; i++)
            {
                var candidate = layout.Rows[i];
                if (contentY < candidate.OffsetY + candidate.Height * 0.5f)
                {
                    newIndex = i;
                    break;
                }
            }
            if (newIndex < 0)
            {
                newIndex = 0;
            }
            else if (newIndex > layout.Rows.Count)
            {
                newIndex = layout.Rows.Count;
            }
            _targetIndex = newIndex;
        }

        private float CalculateInsertionY(IWorkTabLayoutController layout, int targetIndex)
        {
            if (layout.Rows.Count == 0)
            {
                return layout.TableOrigin.y + layout.HeaderHeight;
            }

            if (targetIndex >= layout.Rows.Count)
            {
                var lastRect = layout.GetScreenRect(layout.Rows[layout.Rows.Count - 1]);
                return lastRect.yMax;
            }

            return layout.GetScreenRect(layout.Rows[targetIndex]).y;
        }

        private static void DrawDividerGhostLabel(Rect rect, PawnDivider divider)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect labelRect = rect.ContractedBy(4f);
            Widgets.Label(labelRect, divider.DividerName ?? "Divider");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void DrawPawnGhostLabel(Rect rect, Pawn pawn)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect labelRect = rect.ContractedBy(4f);
            Widgets.Label(labelRect, pawn.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void Reset()
        {
            _draggedElement = null;
            _rowSnapshot = default;
            _targetIndex = -1;
            _dragOffsetY = 0f;
            _mouse = Vector2.zero;
        }
    }
}
