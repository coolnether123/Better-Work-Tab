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
            in PreparedWorkRowBuildRequest request,
            PreparedWorkRowCommand[] commands,
            PreparedWorkRowRun[] runs,
            PreparedWorkRowCell[] slots,
            PreparedPawnLabelCell pawnLabel,
            int[] slotByColumn,
            int[] runByColumn)
        {
            RowIndex = request.RowIndex;
            PawnId = request.Snapshot.Rows[request.RowIndex].PawnId;
            PreparedRevision = request.Span.Revision;
            TopologyRevision = request.Snapshot.TopologyRevision;
            GeometryRevision = request.Snapshot.LayoutRevision;
            VisibleColumns = request.VisibleColumns;
            RowHeight = request.RowHeight;
            ModeSignature = request.Mode.Signature;
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

        internal bool Matches(in PreparedWorkRowBuildRequest request)
        {
            return RowIndex == request.RowIndex &&
                   PawnId == request.Snapshot.Rows[request.RowIndex].PawnId &&
                   PreparedRevision == request.Span.Revision &&
                   TopologyRevision == request.Snapshot.TopologyRevision &&
                   GeometryRevision == request.Snapshot.LayoutRevision &&
                   VisibleColumns.Start == request.VisibleColumns.Start &&
                   VisibleColumns.Count == request.VisibleColumns.Count &&
                   Mathf.Abs(RowHeight - request.RowHeight) < 0.001f &&
                   ModeSignature == request.Mode.Signature;
        }
    }

    /// <summary>
    /// Describes the presentation decisions that can change packet topology.
    /// The mode owns its cache signature so packet matching cannot drift from
    /// the decisions used during compilation.
    /// </summary>
    internal readonly struct PreparedWorkRowMode
    {
        internal PreparedWorkRowMode(
            bool focusViewActive,
            bool hideParentWork,
            bool delegateShiftedSkillOverlay,
            bool delegateScheduleCells)
        {
            FocusViewActive = focusViewActive;
            HideParentWork = focusViewActive && hideParentWork;
            DelegateShiftedSkillOverlay = delegateShiftedSkillOverlay;
            DelegateScheduleCells = delegateScheduleCells;
            Signature =
                (delegateShiftedSkillOverlay ? 1 : 0) |
                (delegateScheduleCells ? 2 : 0) |
                (focusViewActive ? 4 : 0) |
                (HideParentWork ? 8 : 0);
        }

        internal bool FocusViewActive { get; }
        internal bool HideParentWork { get; }
        internal bool DelegateShiftedSkillOverlay { get; }
        internal bool DelegateScheduleCells { get; }
        internal int Signature { get; }
    }

    /// <summary>
    /// Carries one packet lookup or compilation. The snapshot, lookup, layout,
    /// and row span must come from the same prepared pass; transitional modes
    /// are rejected before this boundary.
    /// </summary>
    internal readonly struct PreparedWorkRowBuildRequest
    {
        internal PreparedWorkRowBuildRequest(
            WorkGridSnapshot snapshot,
            int[] cellLookup,
            IReadOnlyList<WorkTabLayoutColumn> layoutColumns,
            WorkGridIndexRange visibleColumns,
            int rowIndex,
            float rowHeight,
            WorkGridPreparedRowSpan span,
            PreparedWorkRowMode mode)
        {
            Snapshot = snapshot;
            CellLookup = cellLookup;
            LayoutColumns = layoutColumns;
            VisibleColumns = visibleColumns;
            RowIndex = rowIndex;
            RowHeight = rowHeight;
            Span = span;
            Mode = mode;
        }

        internal WorkGridSnapshot Snapshot { get; }
        internal int[] CellLookup { get; }
        internal IReadOnlyList<WorkTabLayoutColumn> LayoutColumns { get; }
        internal WorkGridIndexRange VisibleColumns { get; }
        internal int RowIndex { get; }
        internal float RowHeight { get; }
        internal WorkGridPreparedRowSpan Span { get; }
        internal PreparedWorkRowMode Mode { get; }
        internal int ColumnCount => Snapshot.Columns.Count;
        internal bool HasMatchingColumnTopology =>
            LayoutColumns.Count == Snapshot.Columns.Count;
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
            in PreparedWorkRowBuildRequest request)
        {
            if (!request.HasMatchingColumnTopology)
            {
                return null;
            }

            var assembly = new RowPacketAssembly(
                request.ColumnCount,
                request.VisibleColumns.Count);
            int visibleEnd = Math.Min(
                request.ColumnCount,
                request.VisibleColumns.EndExclusive);
            for (int columnIndex = Math.Max(0, request.VisibleColumns.Start);
                 columnIndex < visibleEnd;
                 columnIndex++)
            {
                if (!assembly.HasPawnLabel &&
                    TryPreparePawnLabel(in request, columnIndex, out PreparedPawnLabelCell pawnLabel))
                {
                    assembly.AppendPawnLabel(pawnLabel);
                    continue;
                }

                PreparedColumn prepared = PrepareColumn(in request, columnIndex);
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

            return assembly.Complete(in request);
        }

        private static bool TryPreparePawnLabel(
            in PreparedWorkRowBuildRequest request,
            int columnIndex,
            out PreparedPawnLabelCell pawnLabel)
        {
            pawnLabel = null;
            if (request.Snapshot.Columns[columnIndex].WorkerKind !=
                    WorkGridColumnWorkerKind.PawnLabel ||
                request.RowIndex >= request.Snapshot.PawnLabels.Count)
            {
                return false;
            }

            PreparedPawnLabelPresentation presentation =
                request.Snapshot.PawnLabels[request.RowIndex];
            if (!presentation.IsPrepared)
            {
                return false;
            }

            pawnLabel = BuildPawnLabel(
                presentation,
                request.LayoutColumns[columnIndex],
                columnIndex,
                request.RowHeight);
            return true;
        }

        private static PreparedColumn PrepareColumn(
            in PreparedWorkRowBuildRequest request,
            int columnIndex)
        {
            WorkGridColumnEntry column = request.Snapshot.Columns[columnIndex];
            bool subWorkCell = column.WorkerKind ==
                WorkGridColumnWorkerKind.SubWorkPriority;
            bool preparedKind = column.WorkerKind ==
                    WorkGridColumnWorkerKind.WorkPriority ||
                subWorkCell;
            if (!preparedKind ||
                !TryGetPreparedCell(in request, columnIndex, out WorkCellVisualState cell) ||
                MustDelegatePreparedCell(
                    subWorkCell,
                    request.Mode.FocusViewActive,
                    request.Mode.DelegateShiftedSkillOverlay,
                    request.Mode.DelegateScheduleCells))
            {
                return PreparedColumn.Native;
            }

            if (!subWorkCell &&
                request.Mode.FocusViewActive &&
                request.Mode.HideParentWork)
            {
                return PreparedColumn.Hidden;
            }

            Rect cellRect = WorkGridInteractionGeometry.GetAnimatedBodyContentRect(
                request.LayoutColumns[columnIndex],
                new Rect(0f, 0f, 0f, request.RowHeight));
            return subWorkCell
                ? PrepareSubWorkColumn(column, cell, columnIndex, cellRect)
                : PrepareParentColumn(cell, columnIndex, cellRect);
        }

        private static bool TryGetPreparedCell(
            in PreparedWorkRowBuildRequest request,
            int columnIndex,
            out WorkCellVisualState cell)
        {
            int lookupIndex = (request.RowIndex * request.ColumnCount) + columnIndex;
            int cellIndex = lookupIndex >= 0 && lookupIndex < request.CellLookup.Length
                ? request.CellLookup[lookupIndex]
                : -1;
            if (cellIndex < request.Span.FirstCellIndex ||
                cellIndex >= request.Span.FirstCellIndex + request.Span.CellCount)
            {
                cell = default;
                return false;
            }

            cell = request.Snapshot.Cells[cellIndex];
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
            if (!cell.TryGetSubWorkPresentation(out var presentation) ||
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

            internal PreparedWorkRowPacket Complete(
                in PreparedWorkRowBuildRequest request)
            {
                EndRetainedRun();
                if (_runs.Count == 0 && _pawnLabel == null)
                {
                    // A packet with only native commands cannot optimize the row.
                    // Returning null gives the legacy renderer complete ownership.
                    return null;
                }

                PreparedWorkRowCommand[] preparedCommands = _commands.ToArray();
                PreparedWorkRowRun[] preparedRuns = _runs.ToArray();
                PreparedWorkRowCell[] preparedSlots = _slots.ToArray();
                if (!HasValidCommandTopology(
                        preparedCommands,
                        request.ColumnCount,
                        preparedRuns.Length,
                        _pawnLabel != null))
                {
                    return null;
                }

                int[] runByColumn = CreateRunLookup(
                    request.ColumnCount,
                    preparedRuns,
                    preparedSlots);
                return new PreparedWorkRowPacket(
                    in request,
                    preparedCommands,
                    preparedRuns,
                    preparedSlots,
                    _pawnLabel,
                    _slotByColumn,
                    runByColumn);
            }

            /// <summary>
            /// Validates the producer-owned command stream once, before it can be
            /// cached. Retained hits can then trust every index without repeating
            /// defensive checks or risking a partially drawn row.
            /// </summary>
            private static bool HasValidCommandTopology(
                PreparedWorkRowCommand[] commands,
                int columnCount,
                int runCount,
                bool hasPawnLabel)
            {
                for (int index = 0; index < commands.Length; index++)
                {
                    PreparedWorkRowCommand command = commands[index];
                    switch (command.Kind)
                    {
                        case PreparedWorkRowCommandKind.NativeColumn:
                            if (command.Index < 0 || command.Index >= columnCount)
                            {
                                return false;
                            }
                            break;
                        case PreparedWorkRowCommandKind.RetainedRun:
                            if (command.Index < 0 || command.Index >= runCount)
                            {
                                return false;
                            }
                            break;
                        case PreparedWorkRowCommandKind.PreparedPawnLabel:
                            if (!hasPawnLabel ||
                                command.Index < 0 ||
                                command.Index >= columnCount)
                            {
                                return false;
                            }
                            break;
                        default:
                            return false;
                    }
                }

                return true;
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
