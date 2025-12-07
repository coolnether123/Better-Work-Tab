using Better_Work_Tab.Features;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using RimWorld;
using Spine.DragDropApi.Util;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    public class ColumnDragHandler
    {
        private readonly IWorkTabLayoutController _layout;
        private readonly PawnColumnDef _column;
        private readonly List<WorkTabLayoutColumn> _workColumns;
        private int _targetIndex;
        private Rect _originRect;
        private bool _isDragging = false;

        public int TargetIndex => _targetIndex;

        public ColumnDragHandler(IWorkTabLayoutController layout, WorkTabLayoutColumn col)
        {
            _layout = layout;
            _column = col.Column;
            _originRect = col.HeaderRect;

            _workColumns = _layout.Columns
                .Where(c => c.Column.Worker is PawnColumnWorker_WorkPriority)
                .ToList();

            _targetIndex = _workColumns.FindIndex(c => c.Column == _column);
            _isDragging = true;
        }

        public bool IsDragging => _isDragging;

        public void OnDragUpdate(Vector2 mousePos)
        {
            int index = _workColumns.Count;
            for (int i = 0; i < _workColumns.Count; i++)
            {
                if (mousePos.x < _workColumns[i].HeaderRect.center.x)
                {
                    index = i;
                    break;
                }
            }
            _targetIndex = Mathf.Clamp(index, 0, _workColumns.Count);
        }

        public void OnDrawOverlay()
        {
            if (!_isDragging) return;

            float fullHeight = _layout.HeaderHeight + _layout.ContentHeight;
            bool lineOnly = BetterWorkTabMod.Settings?.showOnlyLineDragIndicatorColumns ?? false;

            if (!lineOnly)
            {
                Rect ghost = new Rect(
                    Event.current.mousePosition.x - (_originRect.width / 2f),
                    _layout.TableOrigin.y,
                    _originRect.width,
                    fullHeight);

                ListDragVisuals.DrawGhost(ghost, _column.defName);
            }

            if (_targetIndex >= 0)
            {
                float lineX;
                if (_workColumns.Count == 0) lineX = _originRect.x;
                else if (_targetIndex >= _workColumns.Count) lineX = _workColumns.Last().HeaderRect.xMax;
                else lineX = _workColumns[_targetIndex].HeaderRect.xMin;

                Widgets.DrawBoxSolid(new Rect(lineX - 1f, _layout.TableOrigin.y, 2f, fullHeight), Color.white);
            }
        }

        public void OnDrop()
        {
            CommitReorder();
        }

        public void OnCancel()
        {
            _isDragging = false;
        }

        private void CommitReorder()
        {
            if (!_isDragging) return;

            PawnTableDef def = PawnTableDefOf.Work;
            if (def?.columns == null)
            {
                _isDragging = false;
                return;
            }

            var workCols = def.columns
                .Where(c => c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                .ToList();

            var current = workCols.FirstOrDefault(c => c == _column);

            if (current != null)
            {
                int currentIndex = workCols.IndexOf(current);
                workCols.Remove(current);

                // Adjust BEFORE clamping
                int adjustedTarget = _targetIndex;
                if (currentIndex < _targetIndex)
                {
                    adjustedTarget--;
                }

                int insert = Mathf.Clamp(adjustedTarget, 0, workCols.Count);
                workCols.Insert(insert, current);

                // Reconstruct table def columns
                var original = def.columns.ToList();
                var pre = new List<PawnColumnDef>();
                var post = new List<PawnColumnDef>();
                bool inWork = false;

                foreach (var col in original)
                {
                    bool isWork = col.Worker is PawnColumnWorker_WorkPriority && col.workType != null;
                    if (isWork) inWork = true;
                    else if (!inWork) pre.Add(col);
                    else post.Add(col);
                }

                def.columns.Clear();
                def.columns.AddRange(pre);
                def.columns.AddRange(workCols);
                def.columns.AddRange(post);

                WorkColumnOrderManager.CaptureCurrent(def);

                // Record that this column was directly dragged by the player
                MainTabWindow_BetterWork.MarkColumnMoved(_column.workType);

                WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            }

            _isDragging = false;
        }
    }
}
