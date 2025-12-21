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
        private readonly PawnColumnDef _primaryColumn;
        private readonly List<PawnColumnDef> _draggedColumns = new List<PawnColumnDef>();
        private readonly List<WorkTabLayoutColumn> _workColumns;
        private Rect _originRect;
        public PawnColumnDef ColumnDef => _primaryColumn;

        public ColumnDragHandler(IWorkTabLayoutController layout, WorkTabLayoutColumn col)
            : base(layout)
        {
            _primaryColumn = col.Column;
            _originRect = col.HeaderRect;

            _workColumns = Layout.Columns
                .Where(c => c.Column.Worker is PawnColumnWorker_WorkPriority)
                .ToList();

            if (ColumnSelectionManager.IsSelected(_primaryColumn))
            {
                // Drag the whole selection
                var allWorkColDefs = _workColumns.Select(c => c.Column);
                _draggedColumns.AddRange(ColumnSelectionManager.GetSelectedInOrder(allWorkColDefs));
            }
            else
            {
                // Drag only this column and clear selection
                _draggedColumns.Add(_primaryColumn);
                ColumnSelectionManager.Clear();
            }

            // TargetIndex is relative to _workColumns (excluding columns being dragged if we use the same logic as rows, 
            // but column dragging currently uses a simple insertion line based on visual overlaps).
            TargetIndex = _workColumns.FindIndex(c => c.Column == _primaryColumn);
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

                ListDragVisuals.DrawGhost(ghost, _primaryColumn.defName);
            }

            DrawBaselineLineIfNeeded();

            if (TargetIndex >= 0 && showLine)
            {
                float lineX;
                if (_workColumns.Count == 0) lineX = _originRect.x;
                else if (TargetIndex >= _workColumns.Count) lineX = _workColumns.Last().HeaderRect.xMax;
                else lineX = _workColumns[TargetIndex].HeaderRect.xMin;

                int insetSetting = BetterWorkTabMod.Settings?.columnInsertionLineInset ?? DefaultSettings.columnInsertionLineInset;
                int inset = Mathf.Clamp(insetSetting, 0, Mathf.RoundToInt(Layout.HeaderHeight));
                // Treat inset as distance upward from the header bottom so 0 = start at content, max = include full header.
                float lineY = Layout.TableOrigin.y + (Layout.HeaderHeight - inset);
                float lineHeight = Mathf.Max(0f, Layout.ContentHeight + inset);

                Widgets.DrawBoxSolid(new Rect(lineX - 1f, lineY, 2f, lineHeight), Color.white);
            }
        }

        private void DrawBaselineLineIfNeeded()
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.showColumnBaselineLine ?? true))
            {
                return;
            }

            var workType = _primaryColumn?.workType;
            if (workType?.defName == null)
            {
                return;
            }

            if (!MainTabWindow_BetterWork.IsColumnOutOfBaselinePosition(workType))
            {
                return;
            }

            if (_workColumns.Count == 0)
            {
                return;
            }

            var baselineOrder = WorkColumnOrderManager.GetBaselineOrder();
            if (baselineOrder == null || baselineOrder.Count == 0)
            {
                return;
            }

            var currentDefs = new HashSet<string>();
            for (int i = 0; i < _workColumns.Count; i++)
            {
                var defName = _workColumns[i].Column?.workType?.defName;
                if (!string.IsNullOrEmpty(defName))
                {
                    currentDefs.Add(defName);
                }
            }

            var filteredBaseline = new List<string>(baselineOrder.Count);
            for (int i = 0; i < baselineOrder.Count; i++)
            {
                var defName = baselineOrder[i];
                if (currentDefs.Contains(defName))
                {
                    filteredBaseline.Add(defName);
                }
            }

            int baselineIndex = filteredBaseline.IndexOf(workType.defName);
            if (baselineIndex < 0)
            {
                return;
            }

            var baselineIndexByDef = new Dictionary<string, int>(filteredBaseline.Count);
            for (int i = 0; i < filteredBaseline.Count; i++)
            {
                baselineIndexByDef[filteredBaseline[i]] = i;
            }

            int targetIndex = 0;
            for (int i = 0; i < _workColumns.Count; i++)
            {
                var defName = _workColumns[i].Column?.workType?.defName;
                if (string.IsNullOrEmpty(defName) || _draggedColumns.Any(dc => dc.workType?.defName == defName))
                {
                    continue;
                }

                int otherIndex = baselineIndexByDef.TryGetValue(defName, out var idx) ? idx : int.MaxValue;
                if (otherIndex < baselineIndex)
                {
                    targetIndex++;
                }
            }

            float lineX = _workColumns[0].HeaderRect.xMin;
            int seen = 0;
            bool positioned = false;
            for (int i = 0; i < _workColumns.Count; i++)
            {
                var defName = _workColumns[i].Column?.workType?.defName;
                if (string.IsNullOrEmpty(defName) || _draggedColumns.Any(dc => dc.workType?.defName == defName))
                {
                    continue;
                }

                if (seen == targetIndex)
                {
                    lineX = _workColumns[i].HeaderRect.xMin;
                    positioned = true;
                    break;
                }

                lineX = _workColumns[i].HeaderRect.xMax;
                seen++;
            }

            if (!positioned && _workColumns.Count > 0)
            {
                lineX = _workColumns[_workColumns.Count - 1].HeaderRect.xMax;
            }

            int insetSetting = settings?.columnInsertionLineInset ?? DefaultSettings.columnInsertionLineInset;
            int inset = Mathf.Clamp(insetSetting, 0, Mathf.RoundToInt(Layout.HeaderHeight));
            float lineY = Layout.TableOrigin.y + (Layout.HeaderHeight - inset);
            float lineHeight = Mathf.Max(0f, Layout.ContentHeight + inset);

            var baselineColor = new Color(1f, 0.85f, 0.2f, 1f);
            Widgets.DrawBoxSolid(new Rect(lineX - 1f, lineY, 2f, lineHeight), baselineColor);
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

            // Find all columns in workCols that are in our dragged group
            var toRemove = workCols.Where(c => _draggedColumns.Contains(c)).ToList();
            if (toRemove.Count == 0)
            {
                IsDragging = false;
                return;
            }

            // Record original index of the group (usually the first one)
            int firstOriginalIndex = workCols.IndexOf(toRemove[0]);

            // Remove all from the list
            foreach (var col in toRemove)
            {
                workCols.Remove(col);
            }

            // Adjust BEFORE clamping
            int adjustedTarget = TargetIndex;
            if (firstOriginalIndex < TargetIndex)
            {
                adjustedTarget -= toRemove.Count;
            }

            int insertIndex = Mathf.Clamp(adjustedTarget, 0, workCols.Count);

            // === Only reorder if actually moving to different position ===
            if (firstOriginalIndex == insertIndex)
            {
                // No actual move - don't mark as moved
                IsDragging = false;
                return;
            }

            if (MultiplayerBridge.Active)
            {
                // Multiplayer support for group drag would need a new sync method
                // For now, we sync the primary one
                WorkColumnOrderSync.ApplyWorkColumnMove(_primaryColumn.workType?.defName, insertIndex);
                IsDragging = false;
                return;
            }

            // Actually move all dragged columns
            for (int i = 0; i < _draggedColumns.Count; i++)
            {
                workCols.Insert(insertIndex + i, _draggedColumns[i]);
            }

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
            MainTabWindow_BetterWork.MarkColumnMoved(_primaryColumn.workType);

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

            // Clear selection after successful drop
            ColumnSelectionManager.Clear();
            IsDragging = false;
        }
    }
}
