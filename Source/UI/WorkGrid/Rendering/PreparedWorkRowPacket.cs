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
            if (snapshot == null || cellLookup == null || layoutColumns == null ||
                layoutColumns.Count != snapshot.Columns.Count)
            {
                return null;
            }

            int columnCount = snapshot.Columns.Count;
            var commands = new List<PreparedWorkRowCommand>(8);
            var runs = new List<PreparedWorkRowRun>(2);
            var slots = new List<PreparedWorkRowCell>(visibleColumns.Count);
            var slotByColumn = new int[columnCount];
            for (int index = 0; index < slotByColumn.Length; index++)
            {
                slotByColumn[index] = -1;
            }

            var runCells = new List<RetainedWorkBoxRowCache.Cell>(visibleColumns.Count);
            var runSlotIndexes = new List<int>(visibleColumns.Count);
            var parentDynamicSlotIndexes = new List<int>(4);
            var subWorkRingSlotIndexes = new List<int>(4);
            var subWorkSlotIndexes = new List<int>(4);
            PreparedPawnLabelCell pawnLabel = null;
            int visibleEnd = Math.Min(columnCount, visibleColumns.EndExclusive);
            for (int columnIndex = Math.Max(0, visibleColumns.Start);
                 columnIndex < visibleEnd;
                 columnIndex++)
            {
                WorkGridColumnEntry column = snapshot.Columns[columnIndex];
                if (column.WorkerKind == WorkGridColumnWorkerKind.PawnLabel &&
                    pawnLabel == null &&
                    rowIndex < snapshot.PawnLabels.Count &&
                    snapshot.PawnLabels[rowIndex].IsPrepared)
                {
                    FlushRun(
                        commands, runs, runCells, runSlotIndexes,
                        parentDynamicSlotIndexes, subWorkRingSlotIndexes, subWorkSlotIndexes);
                    pawnLabel = BuildPawnLabel(
                        snapshot.PawnLabels[rowIndex],
                        layoutColumns[columnIndex],
                        columnIndex,
                        rowHeight);
                    if (pawnLabel != null)
                    {
                        commands.Add(new PreparedWorkRowCommand(
                            PreparedWorkRowCommandKind.PreparedPawnLabel,
                            columnIndex));
                        continue;
                    }
                }
                bool preparedKind = column.WorkerKind == WorkGridColumnWorkerKind.WorkPriority ||
                    column.WorkerKind == WorkGridColumnWorkerKind.SubWorkPriority;
                int lookupIndex = (rowIndex * columnCount) + columnIndex;
                int cellIndex = lookupIndex >= 0 && lookupIndex < cellLookup.Length
                    ? cellLookup[lookupIndex]
                    : -1;
                if (!preparedKind || cellIndex < span.FirstCellIndex ||
                    cellIndex >= span.FirstCellIndex + span.CellCount)
                {
                    AppendNativeColumn(
                        columnIndex,
                        commands, runs, runCells, runSlotIndexes,
                        parentDynamicSlotIndexes, subWorkRingSlotIndexes, subWorkSlotIndexes);
                    continue;
                }

                WorkCellVisualState cell = snapshot.Cells[cellIndex];
                bool subWorkCell = column.WorkerKind == WorkGridColumnWorkerKind.SubWorkPriority;
                if (MustDelegatePreparedCell(
                        subWorkCell,
                        focusViewActive,
                        delegateShiftedSkillOverlay,
                        delegateScheduleCells))
                {
                    AppendNativeColumn(
                        columnIndex,
                        commands, runs, runCells, runSlotIndexes,
                        parentDynamicSlotIndexes, subWorkRingSlotIndexes, subWorkSlotIndexes);
                    continue;
                }

                if (!subWorkCell && focusViewActive && parentAlpha <= 0.001f)
                {
                    FlushRun(
                        commands, runs, runCells, runSlotIndexes,
                        parentDynamicSlotIndexes, subWorkRingSlotIndexes, subWorkSlotIndexes);
                    continue;
                }

                WorkTabLayoutColumn layoutColumn = layoutColumns[columnIndex];
                Rect cellRect = WorkGridInteractionGeometry.GetAnimatedBodyContentRect(
                    layoutColumn,
                    new Rect(0f, 0f, 0f, rowHeight));
                Rect boxRect;
                WorkBoxVisualState visual;
                int displayPriority;
                bool compactText;
                WorkGiver workGiver = null;
                WorkGiverCellPresentationCache.CellPresentation subWorkPresentation = null;
                if (subWorkCell)
                {
                    workGiver = column.SubWorkGiver;
                    if (workGiver?.def == null ||
                        cell.Pawn == null ||
                        !cell.TryGetSubWorkPresentation(out subWorkPresentation) ||
                        subWorkPresentation.WorkTypeDisabled ||
                        subWorkPresentation.ParentPriority <= WorkPrioritySystem.DisabledPriority)
                    {
                        AppendNativeColumn(
                            columnIndex,
                            commands, runs, runCells, runSlotIndexes,
                            parentDynamicSlotIndexes, subWorkRingSlotIndexes, subWorkSlotIndexes);
                        continue;
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
                        AppendNativeColumn(
                            columnIndex,
                            commands, runs, runCells, runSlotIndexes,
                            parentDynamicSlotIndexes, subWorkRingSlotIndexes, subWorkSlotIndexes);
                        continue;
                    }

                    boxRect = column.IsExpandBesideChild
                        ? WorkPriorityCellGeometry.GetFluffyStyleSubWorkPriorityBoxRect(cellRect)
                        : WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
                    visual = subWorkPresentation.WorkBoxVisual;
                    displayPriority = subWorkPresentation.EffectivePriority;
                    compactText = boxRect.width <=
                        WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f;
                }
                else
                {
                    boxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
                    visual = CreateVisual(cell);
                    displayPriority = cell.Priority;
                    compactText = false;
                }

                int slotIndex = slots.Count;
                slots.Add(new PreparedWorkRowCell(
                    columnIndex,
                    cellRect,
                    boxRect,
                    cell,
                    visual,
                    workGiver,
                    subWorkPresentation));
                slotByColumn[columnIndex] = slotIndex;
                runSlotIndexes.Add(slotIndex);
                runCells.Add(new RetainedWorkBoxRowCache.Cell(
                    cell.PawnId,
                    columnIndex,
                    boxRect,
                    visual,
                    displayPriority,
                    compactText));

                if (subWorkCell)
                {
                    subWorkSlotIndexes.Add(slotIndex);
                    if (subWorkPresentation.HasPawnOverride ||
                        subWorkPresentation.HasScheduleIndicator)
                    {
                        subWorkRingSlotIndexes.Add(slotIndex);
                    }
                }
                else if ((cell.Flags &
                          (WorkCellVisualFlags.BestPawn | WorkCellVisualFlags.OverrideRing)) != 0)
                {
                    parentDynamicSlotIndexes.Add(slotIndex);
                }
            }

            FlushRun(
                commands, runs, runCells, runSlotIndexes,
                parentDynamicSlotIndexes, subWorkRingSlotIndexes, subWorkSlotIndexes);
            if (runs.Count == 0 && pawnLabel == null)
            {
                return null;
            }

            PreparedWorkRowRun[] preparedRuns = runs.ToArray();
            PreparedWorkRowCell[] preparedSlots = slots.ToArray();
            var runByColumn = new int[columnCount];
            for (int columnIndex = 0; columnIndex < runByColumn.Length; columnIndex++)
            {
                runByColumn[columnIndex] = -1;
            }
            for (int runIndex = 0; runIndex < preparedRuns.Length; runIndex++)
            {
                int[] runSlots = preparedRuns[runIndex].SlotIndexes;
                for (int slotOffset = 0; slotOffset < runSlots.Length; slotOffset++)
                {
                    runByColumn[preparedSlots[runSlots[slotOffset]].ColumnIndex] = runIndex;
                }
            }

            return new PreparedWorkRowPacket(
                rowIndex,
                snapshot.Rows[rowIndex].PawnId,
                span.Revision,
                snapshot.TopologyRevision,
                snapshot.LayoutRevision,
                visibleColumns,
                rowHeight,
                modeSignature,
                commands.ToArray(),
                preparedRuns,
                preparedSlots,
                pawnLabel,
                slotByColumn,
                runByColumn);
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

        private static void FlushRun(
            List<PreparedWorkRowCommand> commands,
            List<PreparedWorkRowRun> runs,
            List<RetainedWorkBoxRowCache.Cell> cells,
            List<int> slotIndexes,
            List<int> parentDynamicSlotIndexes,
            List<int> subWorkRingSlotIndexes,
            List<int> subWorkSlotIndexes)
        {
            if (cells.Count == 0)
            {
                return;
            }

            int runIndex = runs.Count;
            runs.Add(new PreparedWorkRowRun(
                new RetainedWorkBoxRowCache.PreparedRun(cells.ToArray()),
                slotIndexes.ToArray(),
                parentDynamicSlotIndexes.ToArray(),
                subWorkRingSlotIndexes.ToArray(),
                subWorkSlotIndexes.ToArray()));
            commands.Add(new PreparedWorkRowCommand(
                PreparedWorkRowCommandKind.RetainedRun,
                runIndex));
            cells.Clear();
            slotIndexes.Clear();
            parentDynamicSlotIndexes.Clear();
            subWorkRingSlotIndexes.Clear();
            subWorkSlotIndexes.Clear();
        }

        private static void AppendNativeColumn(
            int columnIndex,
            List<PreparedWorkRowCommand> commands,
            List<PreparedWorkRowRun> runs,
            List<RetainedWorkBoxRowCache.Cell> cells,
            List<int> slotIndexes,
            List<int> parentDynamicSlotIndexes,
            List<int> subWorkRingSlotIndexes,
            List<int> subWorkSlotIndexes)
        {
            FlushRun(
                commands, runs, cells, slotIndexes,
                parentDynamicSlotIndexes, subWorkRingSlotIndexes, subWorkSlotIndexes);
            commands.Add(new PreparedWorkRowCommand(
                PreparedWorkRowCommandKind.NativeColumn,
                columnIndex));
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
