using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Handles ctrl+drag column reordering using the layout controller.
    /// </summary>
    public class ColumnDragController
    {
        private PawnColumnDef _draggedColumn;
        private Rect _originRect;
        private float _dragOffsetX;
        private Vector2 _mouse;
        private int _targetIndex = -1;
        private readonly List<WorkTabLayoutColumn> _workColumns = new List<WorkTabLayoutColumn>();

        public bool IsDragging => _draggedColumn != null;

        public void HandleInput(Event evt, IWorkTabLayoutController layout)
        {
            if (layout?.Table == null || evt == null)
            {
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.control)
            {
                if (layout.TryGetColumnAt(evt.mousePosition, out var column) &&
                    column.Column.Worker is PawnColumnWorker_WorkPriority)
                {
                    BeginDrag(layout, column, evt.mousePosition);
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
                UpdateInsertionIndex(evt.mousePosition);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp)
            {
                Commit();
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
            if (!IsDragging || layout == null)
            {
                return;
            }

            float fullHeight = layout.HeaderHeight + layout.ContentHeight;
            var ghostRect = new Rect(
                _mouse.x - _dragOffsetX,
                layout.TableOrigin.y,
                _originRect.width,
                fullHeight);

            Widgets.DrawBoxSolid(ghostRect, new Color(0f, 0f, 0f, 0.15f));
            Widgets.DrawBox(ghostRect, 1);

            Rect headerLabelRect = new Rect(
                ghostRect.x,
                layout.TableOrigin.y,
                ghostRect.width,
                layout.HeaderHeight);

            if (_draggedColumn != null)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(headerLabelRect.ContractedBy(2f), _draggedColumn.defName);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }

            if (_targetIndex >= 0)
            {
                float lineX = CalculateInsertionX(_targetIndex);
                var lineRect = new Rect(lineX - 1f, layout.TableOrigin.y, 2f, fullHeight);
                Widgets.DrawBoxSolid(lineRect, Color.white);
            }
        }

        private void BeginDrag(IWorkTabLayoutController layout, WorkTabLayoutColumn column, Vector2 mouse)
        {
            _draggedColumn = column.Column;
            _originRect = column.HeaderRect;
            _dragOffsetX = mouse.x - column.HeaderRect.x;
            _mouse = mouse;

            _workColumns.Clear();
            _workColumns.AddRange(
                layout.Columns.Where(c => c.Column.Worker is PawnColumnWorker_WorkPriority));
            _targetIndex = _workColumns.FindIndex(c => c.Column == _draggedColumn);
        }

        private void UpdateInsertionIndex(Vector2 mousePosition)
        {
            if (_workColumns.Count == 0)
            {
                _targetIndex = -1;
                return;
            }

            int newIndex = _workColumns.Count;
            for (int i = 0; i < _workColumns.Count; i++)
            {
                var rect = _workColumns[i].HeaderRect;
                float boundary = rect.x + rect.width * 0.5f;
                if (mousePosition.x < boundary)
                {
                    newIndex = i;
                    break;
                }
            }
            if (newIndex < 0)
            {
                newIndex = 0;
            }
            else if (newIndex > _workColumns.Count)
            {
                newIndex = _workColumns.Count;
            }
            _targetIndex = newIndex;
        }

        private float CalculateInsertionX(int targetIndex)
        {
            if (_workColumns.Count == 0)
            {
                return _originRect.x;
            }

            if (targetIndex >= _workColumns.Count)
            {
                return _workColumns.Last().HeaderRect.xMax;
            }

            return _workColumns[targetIndex].HeaderRect.xMin;
        }

        private void Commit()
        {
            if (_draggedColumn == null || _targetIndex < 0)
            {
                return;
            }

            WorkColumnOrderSync.ApplyWorkColumnMove(_draggedColumn.defName, _targetIndex);
        }

        private void Reset()
        {
            _draggedColumn = null;
            _originRect = Rect.zero;
            _dragOffsetX = 0f;
            _targetIndex = -1;
            _workColumns.Clear();
        }
    }
}
