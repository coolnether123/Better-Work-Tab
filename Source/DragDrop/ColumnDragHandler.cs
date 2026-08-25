using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Foundation.GameState;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Columns;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Spine.DragDropApi.Util;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Projection;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Handles dragging single or grouped work columns to reorder them.
    /// Supports multi-column selection via ColumnSelectionManager.
    /// </summary>
    public class ColumnDragHandler : DragHandler<WorkTabLayoutColumn>
    {
        private readonly PawnColumnDef _primaryColumn;
        private readonly List<PawnColumnDef> _draggedColumns = new List<PawnColumnDef>();
        private readonly List<WorkTabLayoutColumn> _workColumns;
        private readonly bool _subWorkDrilldownDrag;
        private readonly WorkTypeDef _subWorkType;
        private readonly WorkGiverDef _subWorkGiver;
        private readonly int _subWorkOriginalIndex = -1;
        private Rect _originRect;
        private Vector2 _lastMousePos;
        private WorkTypeDef _crossWorkDropTarget;
        private Rect _crossWorkDropTargetRect;
        private bool _crossWorkDropTargetValid;
        public PawnColumnDef ColumnDef => _primaryColumn;

        public ColumnDragHandler(IWorkTabLayoutController layout, WorkTabLayoutColumn col)
            : base(layout)
        {
            _primaryColumn = col.Column;
            _originRect = WorkGridInteractionGeometry.GetAnimatedHeaderRect(col);

            _workColumns = Layout.Columns
                .Where(c => c.Column.Worker is PawnColumnWorker_WorkPriority)
                .ToList();

            if (SubWorkDrilldownState.TryGetWorkGiverForColumn(
                    col,
                    out var workGiver,
                    out var parentWorkType,
                    out var slotIndex))
            {
                _subWorkDrilldownDrag = true;
                _subWorkType = parentWorkType;
                _subWorkGiver = workGiver.def;
                _subWorkOriginalIndex = slotIndex;
                _draggedColumns.Add(_primaryColumn);
                ColumnSelectionManager.Clear();
                BetterWorkTabMod.DebugLog($"[BWT] Dragging sub-work job: {_subWorkGiver.defName}", DebugFeature.DragDrop);
            }
            else if (ColumnSelectionManager.IsSelected(_primaryColumn))
            {
                // Drag the whole selection
                var allWorkColDefs = _workColumns.Select(c => c.Column);
                _draggedColumns.AddRange(ColumnSelectionManager.GetSelectedInOrder(allWorkColDefs));
                BetterWorkTabMod.DebugLog($"[BWT] Dragging selection: {string.Join(", ", _draggedColumns.Select(d => d.defName))}", DebugFeature.DragDrop);
            }
            else
            {
                // Drag only this column and clear selection
                _draggedColumns.Add(_primaryColumn);
                ColumnSelectionManager.Clear();
                BetterWorkTabMod.DebugLog($"[BWT] Dragging single column: {_primaryColumn.defName}", DebugFeature.DragDrop);
            }

            // TargetIndex is relative to _workColumns (excluding columns being dragged if we use the same logic as rows, 
            // but column dragging currently uses a simple insertion line based on visual overlaps).
            TargetIndex = _subWorkDrilldownDrag
                ? _subWorkOriginalIndex
                : _workColumns.FindIndex(c => c.Column == _primaryColumn);
            
            // Set local flag to prevent priority edits during drag
            BetterWorkTabLocalState.IsHeaderDragging = true;
        }

        /// <summary>
        /// Updates the target insertion index based on mouse position.
        /// </summary>
        public override void OnDragUpdate(Vector2 mousePos)
        {
            _lastMousePos = mousePos;
            var targetColumns = GetInsertionTargetColumns();
            int index = targetColumns.Count;
            for (int i = 0; i < targetColumns.Count; i++)
            {
                if (mousePos.x < WorkGridInteractionGeometry.GetAnimatedHeaderRect(targetColumns[i]).center.x)
                {
                    index = i;
                    break;
                }
            }
            TargetIndex = Mathf.Clamp(index, 0, targetColumns.Count);
            UpdateCrossWorkDropTarget(mousePos);
        }

        /// <summary>
        /// Draws the insertion line and baseline indicator during drag.
        /// </summary>
        public override void OnDrawOverlay()
        {
            if (!IsDragging) return;

            float fullHeight = Layout.HeaderHeight + Layout.ContentHeight;
            bool showGhost = false; // default to line-only for columns
            bool showLine = true;
            bool lineOnly = true;
            bool pointerBeyondSubWorkStrip = _subWorkDrilldownDrag &&
                SubWorkCrossWorkDropTargetRenderer.IsEnabled &&
                SubWorkCrossWorkDropTargetRenderer.IsPointerBeyondSubWorkStrip(
                    Layout,
                    _lastMousePos,
                    _subWorkType);

            if (!lineOnly && showGhost)
            {
                Rect ghost = new Rect(
                    Event.current.mousePosition.x - (_originRect.width / 2f),
                    Layout.TableOrigin.y,
                    _originRect.width,
                    fullHeight);

                ListDragVisuals.DrawGhost(ghost, _primaryColumn.defName);
            }

            if (!pointerBeyondSubWorkStrip)
            {
                DrawBaselineLineIfNeeded();
            }

            if (TargetIndex >= 0 && showLine && !pointerBeyondSubWorkStrip)
            {
                var targetColumns = GetInsertionTargetColumns();
                float lineX = GetInsertionLineX(targetColumns, TargetIndex, _originRect.x);

                int insetSetting = BetterWorkTabMod.Settings?.columnInsertionLineInset ?? DefaultSettings.columnInsertionLineInset;
                int inset = Mathf.Clamp(insetSetting, 0, Mathf.RoundToInt(Layout.HeaderHeight));
                Rect lineRect = GetColumnGuideRect(Layout, lineX, inset);

                Widgets.DrawBoxSolid(lineRect, Color.white);
            }
        }

        private void DrawBaselineLineIfNeeded()
        {
            if (_subWorkDrilldownDrag)
            {
                DrawSubWorkBaselineLine();
                return;
            }

            var settings = BetterWorkTabMod.Settings;
            if (!BWTWorkTabEffectiveSettings.GetBool(SettingIDs.ColumnsShowBaselineLine))
            {
                return;
            }

            var workType = _primaryColumn?.workType;
            if (workType?.defName == null)
            {
                return;
            }

            if (!WorkColumnCustomizationService.IsColumnOutOfBaselinePosition(workType))
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

            float lineX = WorkGridInteractionGeometry.GetAnimatedHeaderRect(_workColumns[0]).xMin;
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
                    lineX = WorkGridInteractionGeometry.GetAnimatedHeaderRect(_workColumns[i]).xMin;
                    positioned = true;
                    break;
                }

                lineX = WorkGridInteractionGeometry.GetAnimatedHeaderRect(_workColumns[i]).xMax;
                seen++;
            }

            if (!positioned && _workColumns.Count > 0)
            {
                lineX = WorkGridInteractionGeometry.GetAnimatedHeaderRect(
                    _workColumns[_workColumns.Count - 1]).xMax;
            }

            int insetSetting = settings?.columnInsertionLineInset ?? DefaultSettings.columnInsertionLineInset;
            int inset = Mathf.Clamp(insetSetting, 0, Mathf.RoundToInt(Layout.HeaderHeight));
            Rect lineRect = GetColumnGuideRect(Layout, lineX, inset);

            var baselineColor = new Color(1f, 0.85f, 0.2f, 1f);
            Widgets.DrawBoxSolid(lineRect, baselineColor);
        }

        private void DrawSubWorkBaselineLine()
        {
            if (_subWorkGiver == null || _subWorkType == null)
            {
                return;
            }

            int baselineIndex = WorkGiverReassignmentManager.CalculateBaselineTargetIndex(_subWorkType, _subWorkGiver);
            int targetIndex = baselineIndex >= 0 ? baselineIndex : _subWorkOriginalIndex;
            if (targetIndex < 0)
            {
                return;
            }

            var targetColumns = GetInsertionTargetColumns();
            float lineX = GetInsertionLineX(targetColumns, targetIndex, _originRect.x);

            int insetSetting = BetterWorkTabMod.Settings?.columnInsertionLineInset ?? DefaultSettings.columnInsertionLineInset;
            int inset = Mathf.Clamp(insetSetting, 0, Mathf.RoundToInt(Layout.HeaderHeight));
            Rect lineRect = GetColumnGuideRect(Layout, lineX, inset);

            Widgets.DrawBoxSolid(lineRect, HeaderUtility.Colors.MovedMarkerColor);
        }

        internal static Rect GetColumnGuideRect(
            IWorkTabLayoutController layout,
            float lineX,
            int headerInset)
        {
            float headerBottom = layout.TableOrigin.y + layout.HeaderHeight;
            float lineY = headerBottom - headerInset;
            float lineBottom = GetVisibleRowStackBottom(layout, headerBottom);
            float lineHeight = Mathf.Max(0f, lineBottom - lineY);
            return new Rect(lineX - 1f, lineY, 2f, lineHeight);
        }

        private static float GetVisibleRowStackBottom(
            IWorkTabLayoutController layout,
            float headerBottom)
        {
            float rowStackHeight = GetVisibleRowStackHeight(layout);
            float scrollY = layout.Table?.scrollPosition.y ?? 0f;
            float pinnedRowsHeight = WorkGridLayoutMetrics.GetPinnedRowsHeight();
            float bottom = headerBottom + pinnedRowsHeight + Mathf.Max(0f, rowStackHeight - scrollY);

            if (layout.Table != null)
            {
                float viewportBottom = layout.TableOrigin.y +
                    layout.HeaderHeight +
                    pinnedRowsHeight +
                    layout.ContentHeight;
                if (viewportBottom > headerBottom)
                {
                    bottom = Mathf.Min(bottom, viewportBottom);
                }
            }

            return Mathf.Max(headerBottom, bottom);
        }

        private static float GetVisibleRowStackHeight(IWorkTabLayoutController layout)
        {
            float total = 0f;
            var descriptors = layout.GetRowDescriptors();
            if (descriptors != null && descriptors.Count > 0)
            {
                for (int i = 0; i < descriptors.Count; i++)
                {
                    total += descriptors[i].Height;
                }

                return total;
            }

            if (layout.Rows != null && layout.Rows.Count > 0)
            {
                for (int i = 0; i < layout.Rows.Count; i++)
                {
                    var row = layout.Rows[i];
                    total = Mathf.Max(total, row.OffsetY + row.Height);
                }
            }

            return total > 0f ? total : layout.ContentHeight;
        }

        public override void OnCancel()
        {
            BetterWorkTabLocalState.IsHeaderDragging = false;
            ClearCrossWorkDropTarget();
            base.OnCancel();
        }

        protected override void CommitReorder()
        {
            if (!IsDragging) return;

            if (_subWorkDrilldownDrag)
            {
                CommitSubWorkReorder();
                return;
            }

            try
            {
                PawnTableDef def = PawnTableDefOf.Work;
                if (def?.columns == null)
                {
                    return;
                }

                var workCols = def.columns
                    .Where(c => c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                    .ToList();

                // Find all columns in workCols that are in our dragged group
                var toRemove = workCols.Where(c => _draggedColumns.Contains(c)).ToList();
                if (toRemove.Count == 0)
                {
                    return;
                }

                // Record the visual target column we are dropping at.
                // When dragging a group, if the drop point is inside the group, we need to find 
                // the first column to the right that ISN'T being moved to determine the true insertion point.
                // This prevents the group from "disappearing" or jumping to the end when dropped on itself.
                PawnColumnDef targetAnchor = null;
                for (int i = TargetIndex; i < _workColumns.Count; i++)
                {
                    if (!_draggedColumns.Contains(_workColumns[i].Column))
                    {
                        targetAnchor = _workColumns[i].Column;
                        break;
                    }
                }

                var originalWorkCols = workCols.ToList();

                // Remove all dragged columns from the temporary list.
                foreach (var col in toRemove)
                {
                    workCols.Remove(col);
                }

                // Determine true insertion index in the absolute list (after removals).
                // This is the index of the stationary column we found earlier.
                int insertIndex;
                if (targetAnchor != null)
                {
                    insertIndex = workCols.IndexOf(targetAnchor);
                    if (insertIndex < 0) insertIndex = workCols.Count; // Fallback to end if anchor lost
                }
                else
                {
                    insertIndex = workCols.Count; // Dropped after the last non-dragged column
                }

                insertIndex = Mathf.Clamp(insertIndex, 0, workCols.Count);

                // Build the final order once and use it for both local and multiplayer paths.
                // Multiplayer sync must receive the same post-drop order the local path applies.
                var reorderedWorkCols = workCols.ToList();
                for (int i = 0; i < toRemove.Count; i++)
                {
                    reorderedWorkCols.Insert(insertIndex + i, toRemove[i]);
                }

                BetterWorkTabMod.DebugLog($"[BWT] Reorder Group Map: target={TargetIndex}, insertIndex={insertIndex}", DebugFeature.DragDrop);

                // Re-check for no-op against the final order rather than the insertion index.
                if (originalWorkCols.SequenceEqual(reorderedWorkCols))
                {
                    BetterWorkTabMod.DebugLog("[BWT] Reorder Group No-Op detected. Original position maintained.", DebugFeature.DragDrop);
                    return;
                }

                ColumnReorderAnimationState.Start(Layout.Columns);

                var finalOrder = reorderedWorkCols
                    .Where(c => c.workType != null)
                    .Select(c => c.workType.defName)
                    .ToList();
                var movedNames = toRemove
                    .Where(c => c.workType != null)
                    .Select(c => c.workType.defName)
                    .ToList();
                WorkTabApplicationResult result = WorkTabGameRoots.For(Current.Game)?.Application?.SubmitColumnOrder(
                    finalOrder,
                    movedNames) ?? default;
                if (!result.Accepted)
                {
                    return;
                }

                // Clear selection after successful drop unless Shift is still held
                if (!Event.current.shift || !BetterWorkTabMod.Settings.enableColumnGrouping)
                {
                    ColumnSelectionManager.Clear();
                }
            }
            finally
            {
                // Clear the dragging flag
                BetterWorkTabLocalState.IsHeaderDragging = false;
            }
        }

        private List<WorkTabLayoutColumn> GetVisualTargetColumns()
        {
            if (!_subWorkDrilldownDrag)
            {
                return _workColumns;
            }

            return _workColumns
                .Where(c =>
                    SubWorkDrilldownState.TryGetWorkGiverForColumn(
                        c,
                        out _,
                        out var parentWorkType,
                        out _) &&
                    parentWorkType == _subWorkType)
                .ToList();
        }

        private List<WorkTabLayoutColumn> GetInsertionTargetColumns()
        {
            var columns = GetVisualTargetColumns();
            if (!_subWorkDrilldownDrag)
            {
                return columns;
            }

            return columns
                .Where(c => !IsDraggedSubWorkColumn(c))
                .ToList();
        }

        private bool IsDraggedSubWorkColumn(WorkTabLayoutColumn column)
        {
            if (!_subWorkDrilldownDrag || _subWorkGiver == null)
            {
                return column.Column == _primaryColumn;
            }

            return SubWorkDrilldownState.TryGetWorkGiverForColumn(
                       column,
                       out var workGiver,
                       out var parentWorkType,
                       out _) &&
                   workGiver?.def == _subWorkGiver &&
                   parentWorkType == _subWorkType;
        }

        private static float GetInsertionLineX(List<WorkTabLayoutColumn> targetColumns, int targetIndex, float fallbackX)
        {
            if (targetColumns == null || targetColumns.Count == 0)
            {
                return fallbackX;
            }

            if (targetIndex >= targetColumns.Count)
            {
                return WorkGridInteractionGeometry.GetAnimatedHeaderRect(
                    targetColumns[targetColumns.Count - 1]).xMax;
            }

            if (targetIndex <= 0)
            {
                return WorkGridInteractionGeometry.GetAnimatedHeaderRect(targetColumns[0]).xMin;
            }

            return WorkGridInteractionGeometry.GetAnimatedHeaderRect(targetColumns[targetIndex]).xMin;
        }

        private void CommitSubWorkReorder()
        {
            try
            {
                if (_subWorkType == null || _subWorkGiver == null)
                {
                    return;
                }

                if (TryCommitCrossWorkReassignment())
                {
                    return;
                }

                var current = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(_subWorkType);
                int maxIndex = Mathf.Max(0, (current?.Count ?? 0) - 1);
                int insertIndex = Mathf.Clamp(TargetIndex, 0, maxIndex);

                if (WorkTabEffectiveStateRuntime.IsPreviewActive)
                {
                    WorkTabEffectiveStateRuntime.ReportBlocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        "BWT_Workload_SharedSpecificJobOrderUnavailable".Translate());
                    return;
                }

                ColumnReorderAnimationState.Start(Layout.Columns);
                WorkGiverReassignmentManager.TryMoveWorkGiverLayout(
                    _subWorkGiver.defName,
                    _subWorkType.defName,
                    insertIndex,
                    out _);

            }
            finally
            {
                BetterWorkTabLocalState.IsHeaderDragging = false;
                ClearCrossWorkDropTarget();
            }
        }

        private void UpdateCrossWorkDropTarget(Vector2 mousePos)
        {
            ClearCrossWorkDropTarget();
            if (!_subWorkDrilldownDrag ||
                _subWorkType == null ||
                !SubWorkCrossWorkDropTargetRenderer.IsEnabled ||
                !SubWorkCrossWorkDropTargetRenderer.IsPointerBeyondSubWorkStrip(
                    Layout,
                    mousePos,
                    _subWorkType))
            {
                return;
            }

            WorkTypeDef targetWorkType = null;
            bool valid = false;
            if (SubWorkCrossWorkDropTargetRenderer.TryGetDropTargetAt(
                Layout,
                mousePos,
                _subWorkType,
                out targetWorkType,
                out Rect targetRect,
                out valid))
            {
                _crossWorkDropTarget = targetWorkType;
                _crossWorkDropTargetRect = targetRect;
                _crossWorkDropTargetValid = valid;
            }

        }

        private void ClearCrossWorkDropTarget()
        {
            _crossWorkDropTarget = null;
            _crossWorkDropTargetRect = Rect.zero;
            _crossWorkDropTargetValid = false;
        }

        private bool TryCommitCrossWorkReassignment()
        {
            if (!SubWorkCrossWorkDropTargetRenderer.IsEnabled)
            {
                return false;
            }

            if (_crossWorkDropTarget == null)
            {
                return false;
            }

            if (!_crossWorkDropTargetValid)
            {
                return true;
            }

            if (WorkTabEffectiveStateRuntime.IsPreviewActive)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.SpecificJobOrder,
                    "BWT_Workload_MoveSpecificJobUnavailable".Translate());
                return true;
            }

            int targetIndex = WorkGiverReassignmentManager
                .GetDisplayWorkGiversForWorkType(_crossWorkDropTarget).Count;
            if (!WorkGiverReassignmentManager.TryMoveWorkGiverLayout(
                _subWorkGiver.defName,
                _crossWorkDropTarget.defName,
                targetIndex,
                out string errorMsg))
            {
                string message = errorMsg.NullOrEmpty()
                    ? TranslateOrFallback("BWT_SubWork_MoveSpecificJobFailedNoReason", "Could not move specific job.")
                    : TranslateOrFallback("BWT_SubWork_MoveSpecificJobFailed", "Could not move specific job: {0}", errorMsg);
                Messages.Message(message, MessageTypeDefOf.RejectInput, false);
                return true;
            }

            ColumnReorderAnimationState.Start(Layout.Columns);
            SubWorkCrossWorkDropTargetRenderer.StartSettleAnimation(
                _subWorkGiver,
                _crossWorkDropTarget,
                _originRect,
                _crossWorkDropTargetRect);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            return true;
        }

        private static string TranslateOrFallback(string key, string fallback, params object[] args)
        {
            return key.CanTranslate()
                ? string.Format(key.Translate().ToString(), args)
                : string.Format(fallback, args);
        }
    }
}
