using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    internal enum PreparedWorkRowCommandKind : byte
    {
        NativeColumn,
        RetainedRun,
        PreparedPawnLabel
    }

    internal sealed class PreparedPawnLabelCell
    {
        internal PreparedPawnLabelCell(
            int columnIndex,
            Rect cellRect,
            Rect iconRect,
            Rect textRect,
            PreparedPawnLabelPresentation presentation,
            RetainedWorkBoxRowCache.PreparedRun retained)
        {
            ColumnIndex = columnIndex;
            CellRect = cellRect;
            IconRect = iconRect;
            TextRect = textRect;
            Presentation = presentation;
            Retained = retained;
        }

        internal int ColumnIndex { get; }
        internal Rect CellRect { get; }
        internal Rect IconRect { get; }
        internal Rect TextRect { get; }
        internal PreparedPawnLabelPresentation Presentation { get; }
        internal RetainedWorkBoxRowCache.PreparedRun Retained { get; }
    }

    internal readonly struct PreparedWorkRowCommand
    {
        internal PreparedWorkRowCommand(PreparedWorkRowCommandKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        internal PreparedWorkRowCommandKind Kind { get; }
        internal int Index { get; }
    }

    internal readonly struct PreparedWorkRowCell
    {
        internal PreparedWorkRowCell(
            int columnIndex,
            Rect cellRect,
            Rect boxRect,
            WorkCellVisualState cell,
            WorkBoxVisualState visual,
            WorkGiver workGiver,
            WorkGiverCellPresentationCache.CellPresentation subWorkPresentation)
        {
            ColumnIndex = columnIndex;
            CellRect = cellRect;
            BoxRect = boxRect;
            Cell = cell;
            Visual = visual;
            WorkGiver = workGiver;
            SubWorkPresentation = subWorkPresentation;
        }

        internal int ColumnIndex { get; }
        internal Rect CellRect { get; }
        internal Rect BoxRect { get; }
        internal WorkCellVisualState Cell { get; }
        internal WorkBoxVisualState Visual { get; }
        internal WorkGiver WorkGiver { get; }
        internal WorkGiverCellPresentationCache.CellPresentation SubWorkPresentation { get; }
        internal bool IsSubWork => WorkGiver != null;
    }

    internal sealed class PreparedWorkRowRun
    {
        internal PreparedWorkRowRun(
            RetainedWorkBoxRowCache.PreparedRun retained,
            int[] slotIndexes,
            int[] parentDynamicSlotIndexes,
            int[] subWorkRingSlotIndexes,
            int[] subWorkSlotIndexes)
        {
            Retained = retained;
            SlotIndexes = slotIndexes;
            ParentDynamicSlotIndexes = parentDynamicSlotIndexes;
            SubWorkRingSlotIndexes = subWorkRingSlotIndexes;
            SubWorkSlotIndexes = subWorkSlotIndexes;
        }

        internal RetainedWorkBoxRowCache.PreparedRun Retained { get; }
        internal int[] SlotIndexes { get; }
        internal int[] ParentDynamicSlotIndexes { get; }
        internal int[] SubWorkRingSlotIndexes { get; }
        internal int[] SubWorkSlotIndexes { get; }
    }

    internal sealed class PreparedWorkRowPacket
    {
        internal PreparedWorkRowPacket(
            int rowIndex,
            int pawnId,
            uint preparedRevision,
            long topologyRevision,
            int geometryRevision,
            WorkGridIndexRange visibleColumns,
            float rowHeight,
            int modeSignature,
            PreparedWorkRowCommand[] commands,
            PreparedWorkRowRun[] runs,
            PreparedWorkRowCell[] slots,
            PreparedPawnLabelCell pawnLabel,
            int[] slotByColumn,
            int[] runByColumn)
        {
            RowIndex = rowIndex;
            PawnId = pawnId;
            PreparedRevision = preparedRevision;
            TopologyRevision = topologyRevision;
            GeometryRevision = geometryRevision;
            VisibleColumns = visibleColumns;
            RowHeight = rowHeight;
            ModeSignature = modeSignature;
            Commands = commands;
            Runs = runs;
            Slots = slots;
            PawnLabel = pawnLabel;
            SlotByColumn = slotByColumn;
            RunByColumn = runByColumn;
        }

        internal int RowIndex { get; }
        internal int PawnId { get; }
        internal uint PreparedRevision { get; }
        internal long TopologyRevision { get; }
        internal int GeometryRevision { get; }
        internal WorkGridIndexRange VisibleColumns { get; }
        internal float RowHeight { get; }
        internal int ModeSignature { get; }
        internal PreparedWorkRowCommand[] Commands { get; }
        internal PreparedWorkRowRun[] Runs { get; }
        internal PreparedWorkRowCell[] Slots { get; }
        internal PreparedPawnLabelCell PawnLabel { get; }
        internal int[] SlotByColumn { get; }
        internal int[] RunByColumn { get; }

        internal bool Matches(
            int rowIndex,
            int pawnId,
            uint preparedRevision,
            long topologyRevision,
            int geometryRevision,
            WorkGridIndexRange visibleColumns,
            float rowHeight,
            int modeSignature)
        {
            return RowIndex == rowIndex &&
                   PawnId == pawnId &&
                   PreparedRevision == preparedRevision &&
                   TopologyRevision == topologyRevision &&
                   GeometryRevision == geometryRevision &&
                   VisibleColumns.Start == visibleColumns.Start &&
                   VisibleColumns.Count == visibleColumns.Count &&
                   Mathf.Abs(RowHeight - rowHeight) < 0.001f &&
                   ModeSignature == modeSignature;
        }
    }

    /// <summary>
    /// Compiles pass-stable snapshot data into retained runs and explicit native
    /// fallbacks. Callers own the non-null prepared inputs. A topology mismatch or
    /// unsupported cell returns to native drawing instead of guessing at geometry.
    /// This boundary must not resolve live domain state.
    /// </summary>
    internal static class PreparedWorkRowPacketBuilder
    {
        internal static PreparedWorkRowPacket Build(
            WorkGridSnapshot snapshot,
            int[] cellLookup,
            IReadOnlyList<WorkTabLayoutColumn> layoutColumns,
            WorkGridIndexRange visibleColumns,
            int rowIndex,
            float rowHeight,
            WorkGridPreparedRowSpan span,
            int modeSignature,
            bool focusViewActive,
            float parentAlpha,
            bool delegateShiftedSkillOverlay,
            bool delegateScheduleCells)
        {
            var context = new BuildContext(
                snapshot,
                cellLookup,
                layoutColumns,
                visibleColumns,
                rowIndex,
                rowHeight,
                span,
                modeSignature,
                focusViewActive,
                parentAlpha,
                delegateShiftedSkillOverlay,
                delegateScheduleCells);
            if (!context.HasMatchingColumnTopology)
            {
                return null;
            }

            var assembly = new RowPacketAssembly(
                context.ColumnCount,
                visibleColumns.Count);
            int visibleEnd = Math.Min(context.ColumnCount, visibleColumns.EndExclusive);
            for (int columnIndex = Math.Max(0, visibleColumns.Start);
                 columnIndex < visibleEnd;
                 columnIndex++)
            {
                if (!assembly.HasPawnLabel &&
                    TryPreparePawnLabel(in context, columnIndex, out PreparedPawnLabelCell pawnLabel))
                {
                    assembly.AppendPawnLabel(pawnLabel);
                    continue;
                }

                PreparedColumn prepared = PrepareColumn(in context, columnIndex);
                switch (prepared.Disposition)
                {
                    case PreparedColumnDisposition.Retained:
                        assembly.AppendRetained(in prepared);
                        break;
                    case PreparedColumnDisposition.Hidden:
                        assembly.EndRetainedRun();
                        break;
                    default:
                        assembly.AppendNativeColumn(columnIndex);
                        break;
                }
            }

            return assembly.Complete(in context);
        }

        private static bool TryPreparePawnLabel(
            in BuildContext context,
            int columnIndex,
            out PreparedPawnLabelCell pawnLabel)
        {
            pawnLabel = null;
            if (context.Snapshot.Columns[columnIndex].WorkerKind !=
                    WorkGridColumnWorkerKind.PawnLabel ||
                context.RowIndex >= context.Snapshot.PawnLabels.Count)
            {
                return false;
            }

            PreparedPawnLabelPresentation presentation =
                context.Snapshot.PawnLabels[context.RowIndex];
            if (!presentation.IsPrepared)
            {
                return false;
            }

            pawnLabel = BuildPawnLabel(
                presentation,
                context.LayoutColumns[columnIndex],
                columnIndex,
                context.RowHeight);
            return true;
        }

        private static PreparedColumn PrepareColumn(
            in BuildContext context,
            int columnIndex)
        {
            WorkGridColumnEntry column = context.Snapshot.Columns[columnIndex];
            bool subWorkCell = column.WorkerKind ==
                WorkGridColumnWorkerKind.SubWorkPriority;
            bool preparedKind = column.WorkerKind ==
                    WorkGridColumnWorkerKind.WorkPriority ||
                subWorkCell;
            if (!preparedKind ||
                !TryGetPreparedCell(in context, columnIndex, out WorkCellVisualState cell) ||
                MustDelegatePreparedCell(
                    subWorkCell,
                    context.FocusViewActive,
                    context.DelegateShiftedSkillOverlay,
                    context.DelegateScheduleCells))
            {
                return PreparedColumn.Native;
            }

            if (!subWorkCell &&
                context.FocusViewActive &&
                context.ParentAlpha <= 0.001f)
            {
                return PreparedColumn.Hidden;
            }

            Rect cellRect = WorkGridInteractionGeometry.GetAnimatedBodyContentRect(
                context.LayoutColumns[columnIndex],
                new Rect(0f, 0f, 0f, context.RowHeight));
            return subWorkCell
                ? PrepareSubWorkColumn(column, cell, columnIndex, cellRect)
                : PrepareParentColumn(cell, columnIndex, cellRect);
        }

        private static bool TryGetPreparedCell(
            in BuildContext context,
            int columnIndex,
            out WorkCellVisualState cell)
        {
            int lookupIndex = (context.RowIndex * context.ColumnCount) + columnIndex;
            int cellIndex = lookupIndex >= 0 && lookupIndex < context.CellLookup.Length
                ? context.CellLookup[lookupIndex]
                : -1;
            if (cellIndex < context.Span.FirstCellIndex ||
                cellIndex >= context.Span.FirstCellIndex + context.Span.CellCount)
            {
                cell = default;
                return false;
            }

            cell = context.Snapshot.Cells[cellIndex];
            return true;
        }

        private static PreparedColumn PrepareParentColumn(
            WorkCellVisualState cell,
            int columnIndex,
            Rect cellRect)
        {
            Rect boxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            WorkBoxVisualState visual = CreateVisual(cell);
            bool hasDynamicOverlay = (cell.Flags &
                (WorkCellVisualFlags.BestPawn | WorkCellVisualFlags.OverrideRing)) != 0;
            return PreparedColumn.Retain(
                new PreparedWorkRowCell(
                    columnIndex,
                    cellRect,
                    boxRect,
                    cell,
                    visual,
                    null,
                    null),
                new RetainedWorkBoxRowCache.Cell(
                    cell.PawnId,
                    columnIndex,
                    boxRect,
                    visual,
                    cell.Priority,
                    compactText: false),
                parentDynamicOverlay: hasDynamicOverlay,
                subWorkRingOverlay: false,
                subWork: false);
        }

        private static PreparedColumn PrepareSubWorkColumn(
            WorkGridColumnEntry column,
            WorkCellVisualState cell,
            int columnIndex,
            Rect cellRect)
        {
            WorkGiver workGiver = column.SubWorkGiver;
            if (workGiver?.def == null ||
                cell.Pawn == null ||
                !cell.TryGetSubWorkPresentation(out var presentation) ||
                presentation.WorkTypeDisabled ||
                presentation.ParentPriority <= WorkPrioritySystem.DisabledPriority)
            {
                return PreparedColumn.Native;
            }

            float visualAlpha = 1f;
            float visualScale = 1f;
            if (!column.IsExpandBesideChild)
            {
                SubWorkDrilldownState.TryGetSubWorkContentTransitionVisuals(
                    workGiver,
                    out visualAlpha,
                    out visualScale);
            }
            if (visualAlpha <= 0.999f || Mathf.Abs(visualScale - 1f) >= 0.001f)
            {
                return PreparedColumn.Native;
            }

            Rect boxRect = column.IsExpandBesideChild
                ? WorkPriorityCellGeometry.GetFluffyStyleSubWorkPriorityBoxRect(cellRect)
                : WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            WorkBoxVisualState visual = presentation.WorkBoxVisual;
            bool compactText = boxRect.width <=
                WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f;
            return PreparedColumn.Retain(
                new PreparedWorkRowCell(
                    columnIndex,
                    cellRect,
                    boxRect,
                    cell,
                    visual,
                    workGiver,
                    presentation),
                new RetainedWorkBoxRowCache.Cell(
                    cell.PawnId,
                    columnIndex,
                    boxRect,
                    visual,
                    presentation.EffectivePriority,
                    compactText),
                parentDynamicOverlay: false,
                subWorkRingOverlay: presentation.HasPawnOverride ||
                    presentation.HasScheduleIndicator,
                subWork: true);
        }

        private static PreparedPawnLabelCell BuildPawnLabel(
            PreparedPawnLabelPresentation presentation,
            WorkTabLayoutColumn column,
            int columnIndex,
            float rowHeight)
        {
            Rect cellRect = WorkGridInteractionGeometry.GetAnimatedBodyContentRect(
                column,
                new Rect(0f, 0f, 0f, rowHeight));
            cellRect.height = Mathf.Min(cellRect.height, presentation.MaximumContentHeight);
            Rect textRect = cellRect;
            textRect.xMin += 3f;
            Rect iconRect = default;
            if (presentation.ShowIcon)
            {
                iconRect = new Rect(cellRect.x, cellRect.y, cellRect.height, cellRect.height);
                textRect.xMin += cellRect.height;
            }

            string text = presentation.RichText;
            GameFont previousFont = Text.Font;
            bool previousWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                if (Text.CalcSize(text).x > textRect.width)
                {
                    text = text.Truncate(textRect.width);
                }
            }
            finally
            {
                Text.Font = previousFont;
                Text.WordWrap = previousWrap;
            }

            var retained = new RetainedWorkBoxRowCache.PreparedRun(new[]
            {
                RetainedWorkBoxRowCache.Cell.PawnLabelText(
                    presentation.Pawn.thingIDNumber,
                    columnIndex,
                    textRect,
                    text,
                    presentation.BaseTextColor)
            });
            return new PreparedPawnLabelCell(
                columnIndex,
                cellRect,
                iconRect,
                textRect,
                presentation,
                retained);
        }

        private enum PreparedColumnDisposition : byte
        {
            Native,
            Hidden,
            Retained
        }

        private readonly struct PreparedColumn
        {
            private PreparedColumn(
                PreparedColumnDisposition disposition,
                PreparedWorkRowCell slot,
                RetainedWorkBoxRowCache.Cell retainedCell,
                bool parentDynamicOverlay,
                bool subWorkRingOverlay,
                bool subWork)
            {
                Disposition = disposition;
                Slot = slot;
                RetainedCell = retainedCell;
                ParentDynamicOverlay = parentDynamicOverlay;
                SubWorkRingOverlay = subWorkRingOverlay;
                SubWork = subWork;
            }

            internal static PreparedColumn Native => new PreparedColumn(
                PreparedColumnDisposition.Native,
                default,
                default,
                false,
                false,
                false);

            internal static PreparedColumn Hidden => new PreparedColumn(
                PreparedColumnDisposition.Hidden,
                default,
                default,
                false,
                false,
                false);

            internal static PreparedColumn Retain(
                PreparedWorkRowCell slot,
                RetainedWorkBoxRowCache.Cell retainedCell,
                bool parentDynamicOverlay,
                bool subWorkRingOverlay,
                bool subWork)
            {
                return new PreparedColumn(
                    PreparedColumnDisposition.Retained,
                    slot,
                    retainedCell,
                    parentDynamicOverlay,
                    subWorkRingOverlay,
                    subWork);
            }

            internal PreparedColumnDisposition Disposition { get; }
            internal PreparedWorkRowCell Slot { get; }
            internal RetainedWorkBoxRowCache.Cell RetainedCell { get; }
            internal bool ParentDynamicOverlay { get; }
            internal bool SubWorkRingOverlay { get; }
            internal bool SubWork { get; }
        }

        private readonly struct BuildContext
        {
            internal BuildContext(
                WorkGridSnapshot snapshot,
                int[] cellLookup,
                IReadOnlyList<WorkTabLayoutColumn> layoutColumns,
                WorkGridIndexRange visibleColumns,
                int rowIndex,
                float rowHeight,
                WorkGridPreparedRowSpan span,
                int modeSignature,
                bool focusViewActive,
                float parentAlpha,
                bool delegateShiftedSkillOverlay,
                bool delegateScheduleCells)
            {
                Snapshot = snapshot;
                CellLookup = cellLookup;
                LayoutColumns = layoutColumns;
                VisibleColumns = visibleColumns;
                RowIndex = rowIndex;
                RowHeight = rowHeight;
                Span = span;
                ModeSignature = modeSignature;
                FocusViewActive = focusViewActive;
                ParentAlpha = parentAlpha;
                DelegateShiftedSkillOverlay = delegateShiftedSkillOverlay;
                DelegateScheduleCells = delegateScheduleCells;
            }

            internal WorkGridSnapshot Snapshot { get; }
            internal int[] CellLookup { get; }
            internal IReadOnlyList<WorkTabLayoutColumn> LayoutColumns { get; }
            internal WorkGridIndexRange VisibleColumns { get; }
            internal int RowIndex { get; }
            internal float RowHeight { get; }
            internal WorkGridPreparedRowSpan Span { get; }
            internal int ModeSignature { get; }
            internal bool FocusViewActive { get; }
            internal float ParentAlpha { get; }
            internal bool DelegateShiftedSkillOverlay { get; }
            internal bool DelegateScheduleCells { get; }
            internal int ColumnCount => Snapshot.Columns.Count;
            internal bool HasMatchingColumnTopology =>
                LayoutColumns.Count == Snapshot.Columns.Count;
        }

        /// <summary>
        /// Groups consecutive retained cells into one presentation command and
        /// builds reverse lookups for live input and sparse overlays. Its arrays
        /// are allocated only when a packet is rebuilt, never on a retained hit.
        /// </summary>
        private struct RowPacketAssembly
        {
            private readonly List<PreparedWorkRowCommand> _commands =
                new List<PreparedWorkRowCommand>(8);
            private readonly List<PreparedWorkRowRun> _runs =
                new List<PreparedWorkRowRun>(2);
            private readonly List<PreparedWorkRowCell> _slots;
            private readonly int[] _slotByColumn;
            private readonly List<RetainedWorkBoxRowCache.Cell> _runCells;
            private readonly List<int> _runSlotIndexes;
            private readonly List<int> _parentDynamicSlotIndexes = new List<int>(4);
            private readonly List<int> _subWorkRingSlotIndexes = new List<int>(4);
            private readonly List<int> _subWorkSlotIndexes = new List<int>(4);
            private PreparedPawnLabelCell _pawnLabel;

            internal RowPacketAssembly(int columnCount, int visibleColumnCount)
            {
                _slots = new List<PreparedWorkRowCell>(visibleColumnCount);
                _runCells = new List<RetainedWorkBoxRowCache.Cell>(visibleColumnCount);
                _runSlotIndexes = new List<int>(visibleColumnCount);
                _slotByColumn = CreateEmptyColumnLookup(columnCount);
            }

            internal bool HasPawnLabel => _pawnLabel != null;

            internal void AppendPawnLabel(PreparedPawnLabelCell pawnLabel)
            {
                EndRetainedRun();
                _pawnLabel = pawnLabel;
                _commands.Add(new PreparedWorkRowCommand(
                    PreparedWorkRowCommandKind.PreparedPawnLabel,
                    pawnLabel.ColumnIndex));
            }

            internal void AppendNativeColumn(int columnIndex)
            {
                EndRetainedRun();
                _commands.Add(new PreparedWorkRowCommand(
                    PreparedWorkRowCommandKind.NativeColumn,
                    columnIndex));
            }

            internal void AppendRetained(in PreparedColumn prepared)
            {
                int slotIndex = _slots.Count;
                _slots.Add(prepared.Slot);
                _slotByColumn[prepared.Slot.ColumnIndex] = slotIndex;
                _runSlotIndexes.Add(slotIndex);
                _runCells.Add(prepared.RetainedCell);
                if (prepared.ParentDynamicOverlay)
                {
                    _parentDynamicSlotIndexes.Add(slotIndex);
                }
                if (prepared.SubWork)
                {
                    _subWorkSlotIndexes.Add(slotIndex);
                }
                if (prepared.SubWorkRingOverlay)
                {
                    _subWorkRingSlotIndexes.Add(slotIndex);
                }
            }

            internal void EndRetainedRun()
            {
                if (_runCells.Count == 0)
                {
                    return;
                }

                int runIndex = _runs.Count;
                _runs.Add(new PreparedWorkRowRun(
                    new RetainedWorkBoxRowCache.PreparedRun(_runCells.ToArray()),
                    _runSlotIndexes.ToArray(),
                    _parentDynamicSlotIndexes.ToArray(),
                    _subWorkRingSlotIndexes.ToArray(),
                    _subWorkSlotIndexes.ToArray()));
                _commands.Add(new PreparedWorkRowCommand(
                    PreparedWorkRowCommandKind.RetainedRun,
                    runIndex));
                _runCells.Clear();
                _runSlotIndexes.Clear();
                _parentDynamicSlotIndexes.Clear();
                _subWorkRingSlotIndexes.Clear();
                _subWorkSlotIndexes.Clear();
            }

            internal PreparedWorkRowPacket Complete(in BuildContext context)
            {
                EndRetainedRun();
                if (_runs.Count == 0 && _pawnLabel == null)
                {
                    return null;
                }

                PreparedWorkRowRun[] preparedRuns = _runs.ToArray();
                PreparedWorkRowCell[] preparedSlots = _slots.ToArray();
                int[] runByColumn = CreateRunLookup(
                    context.ColumnCount,
                    preparedRuns,
                    preparedSlots);
                return new PreparedWorkRowPacket(
                    context.RowIndex,
                    context.Snapshot.Rows[context.RowIndex].PawnId,
                    context.Span.Revision,
                    context.Snapshot.TopologyRevision,
                    context.Snapshot.LayoutRevision,
                    context.VisibleColumns,
                    context.RowHeight,
                    context.ModeSignature,
                    _commands.ToArray(),
                    preparedRuns,
                    preparedSlots,
                    _pawnLabel,
                    _slotByColumn,
                    runByColumn);
            }

            private static int[] CreateRunLookup(
                int columnCount,
                PreparedWorkRowRun[] runs,
                PreparedWorkRowCell[] slots)
            {
                int[] runByColumn = CreateEmptyColumnLookup(columnCount);
                for (int runIndex = 0; runIndex < runs.Length; runIndex++)
                {
                    int[] runSlots = runs[runIndex].SlotIndexes;
                    for (int slotOffset = 0; slotOffset < runSlots.Length; slotOffset++)
                    {
                        runByColumn[slots[runSlots[slotOffset]].ColumnIndex] = runIndex;
                    }
                }
                return runByColumn;
            }

            private static int[] CreateEmptyColumnLookup(int columnCount)
            {
                var lookup = new int[columnCount];
                for (int index = 0; index < lookup.Length; index++)
                {
                    lookup[index] = -1;
                }
                return lookup;
            }
        }

        private static bool MustDelegatePreparedCell(
            bool subWorkCell,
            bool focusViewActive,
            bool delegateShiftedSkillOverlay,
            bool delegateScheduleCells)
        {
            if (subWorkCell)
            {
                // The schedule owner computes a live per-job display priority.
                return delegateScheduleCells;
            }

            return !focusViewActive &&
                (delegateShiftedSkillOverlay || delegateScheduleCells);
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
    }
}
