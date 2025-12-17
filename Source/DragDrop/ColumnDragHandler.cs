using Better_Work_Tab.Features;
using Better_Work_Tab.Mod_Support.Multiplayer.Sync;
using Better_Work_Tab.Mod_Support.Multiplayer;
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
    public class ColumnDragHandler : DragHandler<WorkTabLayoutColumn>
    {
        private readonly PawnColumnDef _column;
        private readonly List<WorkTabLayoutColumn> _workColumns;
        private Rect _originRect;
        public PawnColumnDef ColumnDef => _column;

        public ColumnDragHandler(IWorkTabLayoutController layout, WorkTabLayoutColumn col)
            : base(layout)
        {
            _column = col.Column;
            _originRect = col.HeaderRect;

            _workColumns = Layout.Columns
                .Where(c => c.Column.Worker is PawnColumnWorker_WorkPriority)
                .ToList();

            TargetIndex = _workColumns.FindIndex(c => c.Column == _column);
        }

        public override void OnDragUpdate(Vector2 mousePos)
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
            TargetIndex = Mathf.Clamp(index, 0, _workColumns.Count);
        }

        public override void OnDrawOverlay()
        {
            if (!IsDragging) return;

            float fullHeight = Layout.HeaderHeight + Layout.ContentHeight;
            var settings = BetterWorkTabMod.Settings;
            bool showGhost = false; // default to line-only for columns
            bool showLine = true;
            bool lineOnly = true;

            if (!lineOnly && showGhost)
            {
                Rect ghost = new Rect(
                    Event.current.mousePosition.x - (_originRect.width / 2f),
                    Layout.TableOrigin.y,
                    _originRect.width,
                    fullHeight);

                ListDragVisuals.DrawGhost(ghost, _column.defName);
            }

            if (TargetIndex >= 0 && showLine)
            {
                float lineX;
                if (_workColumns.Count == 0) lineX = _originRect.x;
                else if (TargetIndex >= _workColumns.Count) lineX = _workColumns.Last().HeaderRect.xMax;
                else lineX = _workColumns[TargetIndex].HeaderRect.xMin;

                Widgets.DrawBoxSolid(new Rect(lineX - 1f, Layout.TableOrigin.y, 2f, fullHeight), Color.white);
            }
        }

        protected override void CommitReorder()
        {
            if (!IsDragging) return;

            PawnTableDef def = PawnTableDefOf.Work;
            if (def?.columns == null)
            {
                IsDragging = false;
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
                int adjustedTarget = TargetIndex;
                if (currentIndex < TargetIndex)
                {
                    adjustedTarget--;
                }

                int insertIndex = Mathf.Clamp(adjustedTarget, 0, workCols.Count);

                // === Only reorder if actually moving to different position ===
                if (currentIndex == insertIndex)
                {
                    // No actual move - don't mark as moved
                    IsDragging = false;
                    return;
                }

                if (MultiplayerBridge.Active)
                {
                    WorkColumnOrderSync.ApplyWorkColumnMove(_column.workType?.defName, insertIndex);
                    IsDragging = false;
                    return;
                }

                workCols.Insert(insertIndex, current);

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

                // Force layout to rebuild with new column order
                var layout = PawnOrganizerSystem.Instance?.Layout;
                if (layout is WorkTabLayoutController workLayout)
                {
                    workLayout.InvalidateRowDescriptors();
                }

                WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();

                if (MultiplayerBridge.Active)
                {
                    Better_Work_Tab.Mod_Support.Multiplayer.Features.Layouts.LayoutSharingManager.NotifyLayoutChanged();
                }
            }
            IsDragging = false;
        }
    }
}
