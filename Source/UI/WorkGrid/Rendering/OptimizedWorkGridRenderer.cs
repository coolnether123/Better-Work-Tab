using System;
using System.Collections.Generic;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Patches;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Better_Work_Tab.DragDrop;
using RimWorld;
using Spine.RimWorld.Rendering;
using Spine.RimWorld.Rendering.GuiState;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    internal sealed class OptimizedWorkGridRenderer : IWorkGridRenderer, IWorkGridSnapshotLayer, IWorkGridVisibleColumnRangeProvider, IDisposable
    {
        internal const string RendererId = "bwt.optimized-layered";
        private readonly IWorkGridDrawingSurface _drawingSurface;
        private WorkGridSnapshot _snapshot;
        private int[] _cellLookup = Array.Empty<int>();
        private WorkGridIndexRange _visibleRows;
        private WorkGridIndexRange _visibleColumns;
        private ImGuiEventPhase _eventPhase;
        private bool _delegateShiftedSkillOverlay;
        private bool _delegateScheduleCells;
        private bool _delegateSleekPriorityCells;
        private GuiStateScope _cellBatchState;
        private Color _cellBatchColor;
        private bool _cellBatchActive;
        private readonly RetainedWorkBoxRowCache _retainedRows = new RetainedWorkBoxRowCache();
        private readonly List<RetainedWorkBoxRowCache.Cell> _retainedCells =
            new List<RetainedWorkBoxRowCache.Cell>(32);
        private readonly List<PendingParentCell> _pendingParentCells =
            new List<PendingParentCell>(32);
        private readonly List<PendingSubWorkCell> _pendingSubWorkCells =
            new List<PendingSubWorkCell>(32);

        internal OptimizedWorkGridRenderer(IWorkGridDrawingSurface drawingSurface)
        {
            _drawingSurface = drawingSurface ?? throw new ArgumentNullException(nameof(drawingSurface));
        }

        public string Id => RendererId;
        public int Priority => 100;

        public WorkGridIndexRange VisibleColumnRange => _visibleColumns;

        public bool IsAvailable(in WorkTabView context)
        {
            // The snapshot is built inside the effective-state scope, so a
            // workload presentation captures projected parent priorities,
            // specific-job state, and its validated global manual mode. An
            // external priority owner still lacks a safe content revision and
            // therefore remains on the native correctness path.
            return !PriorityAuthorityBroker.ExternalWorkTabHasPriorityAuthority &&
                   WorkGridSnapshotProvider.IsActiveEffectiveStateCurrent() &&
                   context.Snapshot != null &&
                   context.Geometry != null &&
                   context.Layout != null &&
                   context.Table != null;
        }

        public void Prepare(in WorkTabView context)
        {
            _eventPhase = context.EventPhase;
            WorkGridSnapshot snapshot = context.Snapshot;
            if (!ReferenceEquals(snapshot, _snapshot))
            {
                _snapshot = snapshot;
                BuildCellLookup(snapshot);
            }

            if (snapshot == null)
            {
                return;
            }

            bool skillOverlayEnabled = BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FeaturesOverlay);
            _delegateShiftedSkillOverlay = skillOverlayEnabled &&
                ShiftHelper.State == BetterWorkTabSettings.ShowUIMode.Shifted;
            _delegateScheduleCells = FluffyTimeScheduleAssigner.IsOpen;
            _delegateSleekPriorityCells = SleekWorkTabGateway.BetterWorkTabHostsSleek;

            if (context.EventPhase == ImGuiEventPhase.Repaint)
            {
                Vector2 scroll = context.Table.scrollPosition;
                _visibleRows = context.Geometry.GetVisibleRowRange(context.Viewport, scroll.y);
                _visibleColumns = context.Geometry.GetVisibleColumnRange(context.Viewport, scroll.x);

                // The body renderer already culls with live animated geometry.
                // Avoid applying a second, stable snapshot filter while columns move.
                if (ColumnReorderAnimationState.IsActive)
                {
                    _visibleColumns = new WorkGridIndexRange(0, _snapshot.Columns.Count);
                }
            }
        }

        public void Draw(in WorkTabView context)
        {
            _drawingSurface.DrawBody(in context, this);
        }

        public void HandleEvent(in WorkTabView context)
        {
        }

        public void ReleaseTransient(in WorkTabView context)
        {
        }

        public bool TryDrawRowBackground(int rowIndex, Rect rowRect, out Color textColor)
        {
            textColor = Color.white;
            if (_snapshot == null || rowIndex < 0 || rowIndex >= _snapshot.Rows.Count)
            {
                return false;
            }

            WorkGridRowEntry row = _snapshot.Rows[rowIndex];
            if ((row.VisualFlags & WorkGridRowVisualFlags.HasBackground) == 0)
            {
                return false;
            }

            Color color = UnpackColor(row.BackgroundColor);
            Widgets.DrawBoxSolid(rowRect, color);
            if (row.Kind == WorkGridRowKind.Pawn)
            {
                textColor = Spine.UI.TextColorHelper.GetContrastingTextColor(color);
            }
            return true;
        }

        public void BeginRow()
        {
            EndCellBatch();
            _retainedCells.Clear();
            _pendingParentCells.Clear();
            _pendingSubWorkCells.Clear();
        }

        public void EndRow()
        {
            EndCellBatch();
        }

        public bool TryDrawCell(int rowIndex, int columnIndex, Rect cellRect)
        {
            if (_snapshot == null ||
                rowIndex < 0 || columnIndex < 0 ||
                rowIndex >= _snapshot.Rows.Count || columnIndex >= _snapshot.Columns.Count)
            {
                EndCellBatch();
                return false;
            }

            WorkGridColumnEntry column = _snapshot.Columns[columnIndex];
            if ((column.WorkerKind != WorkGridColumnWorkerKind.WorkPriority &&
                 column.WorkerKind != WorkGridColumnWorkerKind.SubWorkPriority) ||
                _delegateSleekPriorityCells)
            {
                EndCellBatch();
                return false;
            }

            int lookupIndex = (rowIndex * _snapshot.Columns.Count) + columnIndex;
            if (lookupIndex < 0 || lookupIndex >= _cellLookup.Length)
            {
                EndCellBatch();
                return false;
            }

            int cellIndex = _cellLookup[lookupIndex];
            if (cellIndex < 0)
            {
                EndCellBatch();
                return false;
            }

            WorkCellVisualState cell = _snapshot.Cells[cellIndex];
            bool focusViewActive = SubWorkDrilldownState.IsActive &&
                !SubWorkDrilldownState.IsExpandBesideActive;
            bool subWorkCell = column.WorkerKind == WorkGridColumnWorkerKind.SubWorkPriority;
            if (!subWorkCell && !focusViewActive &&
                (_delegateShiftedSkillOverlay || _delegateScheduleCells))
            {
                EndCellBatch();
                return false;
            }

            if (subWorkCell)
            {
                return TryDrawSubWorkCell(cellRect, column, cell);
            }

            EnsureCellBatch(GameFont.Medium);

            float visualAlpha = focusViewActive
                ? SubWorkDrilldownState.ParentWorkContentAlpha
                : 1f;
            if (_eventPhase == ImGuiEventPhase.Repaint &&
                visualAlpha > 0.999f &&
                !ColumnReorderAnimationState.IsActive)
            {
                WorkBoxVisualState visual = CreateVisual(cell);
                Rect boxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
                _retainedCells.Add(new RetainedWorkBoxRowCache.Cell(
                    cell.PawnId,
                    columnIndex,
                    boxRect,
                    visual,
                    cell.Priority,
                    compactText: false));
                _pendingParentCells.Add(new PendingParentCell(cellRect, boxRect, cell, visual));
            }
            else if (_eventPhase == ImGuiEventPhase.Repaint)
            {
                DrawCellInBatch(
                    cellRect,
                    cell.Priority,
                    cell.PriorityColor,
                    cell.Flags,
                    cell.SkillBand,
                    cell.SkillBlend,
                    cell.Passion,
                    visualAlpha);
                if (!focusViewActive)
                {
                    DrawParentHover(cellRect, cell);
                }
            }
            return true;
        }

        public bool ShouldVisitCell(int rowIndex, int columnIndex)
        {
            return rowIndex >= _visibleRows.Start && rowIndex < _visibleRows.EndExclusive &&
                   columnIndex >= _visibleColumns.Start && columnIndex < _visibleColumns.EndExclusive;
        }

        public void Dispose()
        {
            EndCellBatch();
            _retainedRows.Dispose();
            _snapshot = null;
            _cellLookup = Array.Empty<int>();
        }

        private void EndCellBatch()
        {
            if (!_cellBatchActive)
            {
                return;
            }

            try
            {
                FlushRetainedCells();
            }
            finally
            {
                _retainedCells.Clear();
                _pendingParentCells.Clear();
                _pendingSubWorkCells.Clear();
                _cellBatchState.Dispose();
                _cellBatchActive = false;
            }
        }

        private void EnsureCellBatch(GameFont font)
        {
            if (!_cellBatchActive)
            {
                _cellBatchState = GuiStateScope.Capture();
                _cellBatchColor = GUI.color;
                _cellBatchActive = true;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
            }
            if (Text.Font != font)
            {
                Text.Font = font;
            }
        }

        private void FlushRetainedCells()
        {
            if (_pendingParentCells.Count == 0 && _pendingSubWorkCells.Count == 0)
            {
                return;
            }

            bool retained = _retainedRows.TryDraw(_retainedCells, _cellBatchColor, _snapshot);
            for (int index = 0; index < _pendingParentCells.Count; index++)
            {
                PendingParentCell pending = _pendingParentCells[index];
                if (!retained)
                {
                    Text.Font = GameFont.Medium;
                    PreparedWorkBoxRenderer.DrawInBatch(
                        pending.BoxRect,
                        pending.Visual,
                        pending.Cell.Priority,
                        1f,
                        _cellBatchColor);
                }

                PreparedWorkBoxRenderer.DrawDynamicOverlays(
                    pending.BoxRect,
                    pending.Visual,
                    _cellBatchColor);
                DrawParentHover(pending.CellRect, pending.Cell);
            }

            for (int index = 0; index < _pendingSubWorkCells.Count; index++)
            {
                PendingSubWorkCell pending = _pendingSubWorkCells[index];
                if (!retained)
                {
                    Text.Font = pending.BoxRect.width <=
                        WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f
                            ? GameFont.Tiny
                            : GameFont.Medium;
                    PreparedWorkBoxRenderer.DrawInBatch(
                        pending.BoxRect,
                        pending.Presentation.WorkBoxVisual,
                        pending.DisplayPriority,
                        1f,
                        _cellBatchColor);
                }

                WorkGiverPriorityBoxRenderer.DrawPreparedPriorityOverlay(
                    pending.WorkGiver,
                    pending.Pawn,
                    pending.BoxRect,
                    pending.Presentation);
            }
        }

        private void BuildCellLookup(WorkGridSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Rows.Count == 0 || snapshot.Columns.Count == 0)
            {
                _cellLookup = Array.Empty<int>();
                return;
            }

            int length = checked(snapshot.Rows.Count * snapshot.Columns.Count);
            _cellLookup = new int[length];
            for (int index = 0; index < length; index++)
            {
                _cellLookup[index] = -1;
            }

            int rowIndex = 0;
            for (int cellIndex = 0; cellIndex < snapshot.Cells.Count; cellIndex++)
            {
                WorkCellVisualState cell = snapshot.Cells[cellIndex];
                while (rowIndex < snapshot.Rows.Count && snapshot.Rows[rowIndex].PawnId != cell.PawnId)
                {
                    rowIndex++;
                }
                if (rowIndex >= snapshot.Rows.Count)
                {
                    break;
                }

                _cellLookup[(rowIndex * snapshot.Columns.Count) + cell.ColumnIndex] = cellIndex;
            }
        }

        private bool TryDrawSubWorkCell(
            Rect cellRect,
            WorkGridColumnEntry column,
            WorkCellVisualState cell)
        {
            WorkGiver workGiver = column.SubWorkGiver;
            if (workGiver?.def == null ||
                !cell.TryGetSubWorkPresentation(
                    out WorkGiverCellPresentationCache.CellPresentation presentation))
            {
                EndCellBatch();
                return false;
            }

            Rect priorityBoxRect = column.IsExpandBesideChild
                ? WorkPriorityCellGeometry.GetFluffyStyleSubWorkPriorityBoxRect(cellRect)
                : WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            float visualAlpha = 1f;
            float visualScale = 1f;
            if (!column.IsExpandBesideChild)
            {
                SubWorkDrilldownState.TryGetSubWorkContentTransitionVisuals(
                    workGiver,
                    out visualAlpha,
                    out visualScale);
            }

            bool stablePawnBox = _eventPhase == ImGuiEventPhase.Repaint &&
                cell.Pawn != null &&
                !presentation.WorkTypeDisabled &&
                presentation.ParentPriority > WorkPrioritySystem.DisabledPriority &&
                visualAlpha > 0.999f &&
                Mathf.Abs(visualScale - 1f) < 0.001f &&
                !SubWorkDrilldownState.IsTransitioning &&
                !ColumnReorderAnimationState.IsActive;
            if (stablePawnBox)
            {
                int displayPriority = presentation.EffectivePriority;
                bool compactText = priorityBoxRect.width <=
                    WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f;
                EnsureCellBatch(compactText ? GameFont.Tiny : GameFont.Medium);
                _retainedCells.Add(new RetainedWorkBoxRowCache.Cell(
                    cell.PawnId,
                    column.ColumnIndex,
                    priorityBoxRect,
                    presentation.WorkBoxVisual,
                    displayPriority,
                    compactText));
                _pendingSubWorkCells.Add(new PendingSubWorkCell(
                    workGiver,
                    cell.Pawn,
                    priorityBoxRect,
                    presentation,
                    displayPriority));
                return true;
            }

            EndCellBatch();

            if (!column.IsExpandBesideChild)
            {
                DrawCellStandalone(
                    cellRect,
                    cell.Priority,
                    cell.PriorityColor,
                    cell.Flags,
                    cell.SkillBand,
                    cell.SkillBlend,
                    cell.Passion,
                    SubWorkDrilldownState.ParentWorkContentAlpha);
            }

            WorkGiverPriorityBoxRenderer.DrawPreparedPriorityBox(
                workGiver,
                cell.WorkType,
                cell.Pawn,
                priorityBoxRect,
                presentation,
                visualAlpha,
                visualScale);
            return true;
        }

        private static void DrawCellStandalone(
            Rect cellRect,
            byte priority,
            uint priorityColor,
            WorkCellVisualFlags flags,
            byte skillBand,
            float skillBlend,
            byte passion,
            float visualAlpha)
        {
            Rect boxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            var visual = new WorkBoxVisualState(
                priority,
                skillBand,
                skillBlend,
                passion,
                priorityColor,
                flags);
            PreparedWorkBoxRenderer.Draw(boxRect, visual, priority, visualAlpha);
        }

        private void DrawCellInBatch(
            Rect cellRect,
            byte priority,
            uint priorityColor,
            WorkCellVisualFlags flags,
            byte skillBand,
            float skillBlend,
            byte passion,
            float visualAlpha)
        {
            Rect boxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            var visual = new WorkBoxVisualState(
                priority,
                skillBand,
                skillBlend,
                passion,
                priorityColor,
                flags);
            PreparedWorkBoxRenderer.DrawInBatch(
                boxRect,
                visual,
                priority,
                visualAlpha,
                _cellBatchColor);
        }

        private static WorkBoxVisualState CreateVisual(WorkCellVisualState cell)
        {
            return new WorkBoxVisualState(
                cell.Priority,
                cell.SkillBand,
                cell.SkillBlend,
                cell.Passion,
                cell.PriorityColor,
                cell.Flags);
        }

        private static void DrawParentHover(Rect cellRect, WorkCellVisualState cell)
        {
            if (TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                BWTWorkTabTutorial.OwnsCurrentPointer)
            {
                return;
            }

            Pawn pawn = cell.Pawn;
            WorkTypeDef workType = cell.WorkType;
            if (workType != null &&
                PawnColumnWorker_WorkPriority_DoHeader_Patch.HoveredWorkType == workType)
            {
                Widgets.DrawHighlight(cellRect);
            }

            Rect boxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            Vector2 mousePosition = Event.current.mousePosition;
            if (pawn != null && workType != null &&
                boxRect.Contains(mousePosition) && Mouse.IsOver(boxRect))
            {
                bool incapable = (cell.Flags & WorkCellVisualFlags.Incapable) != 0;
                TooltipHandler.TipRegion(
                    boxRect,
                    () => WidgetsWork.TipForPawnWorker(pawn, workType, incapable),
                    pawn.thingIDNumber ^ workType.GetHashCode());
            }
        }

        private static Color UnpackColor(uint packed)
        {
            return new Color32(
                (byte)packed,
                (byte)(packed >> 8),
                (byte)(packed >> 16),
                (byte)(packed >> 24));
        }

        private readonly struct PendingParentCell
        {
            internal PendingParentCell(
                Rect cellRect,
                Rect boxRect,
                WorkCellVisualState cell,
                WorkBoxVisualState visual)
            {
                CellRect = cellRect;
                BoxRect = boxRect;
                Cell = cell;
                Visual = visual;
            }

            internal Rect CellRect { get; }
            internal Rect BoxRect { get; }
            internal WorkCellVisualState Cell { get; }
            internal WorkBoxVisualState Visual { get; }
        }

        private readonly struct PendingSubWorkCell
        {
            internal PendingSubWorkCell(
                WorkGiver workGiver,
                Pawn pawn,
                Rect boxRect,
                WorkGiverCellPresentationCache.CellPresentation presentation,
                int displayPriority)
            {
                WorkGiver = workGiver;
                Pawn = pawn;
                BoxRect = boxRect;
                Presentation = presentation;
                DisplayPriority = displayPriority;
            }

            internal WorkGiver WorkGiver { get; }
            internal Pawn Pawn { get; }
            internal Rect BoxRect { get; }
            internal WorkGiverCellPresentationCache.CellPresentation Presentation { get; }
            internal int DisplayPriority { get; }
        }

    }
}
