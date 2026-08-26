using System;
using System.Collections.Generic;
using System.Diagnostics;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Commands;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WorkGrid.Compatibility;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.WorkGrid.Rendering;
using Better_Work_Tab.UI.Settings;
using RimWorld;
using Spine.Api;
using Better_Work_Tab.Foundation;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Spine.Collections;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Snapshots
{
    /// <summary>Builds and owns immutable presentation snapshots without retaining domain objects.</summary>
    internal sealed class WorkGridSnapshotProvider
    {
        private static WorkGridSnapshotProvider _active;
        private readonly ContiguousBuffer<WorkGridRowEntry> _rows = new ContiguousBuffer<WorkGridRowEntry>(64);
        private readonly ContiguousBuffer<WorkGridColumnEntry> _columns = new ContiguousBuffer<WorkGridColumnEntry>(32);
        private readonly ContiguousBuffer<WorkCellVisualState> _cells = new ContiguousBuffer<WorkCellVisualState>(512);
        private readonly ContiguousBuffer<WorkGridPreparedRowSpan> _preparedRows =
            new ContiguousBuffer<WorkGridPreparedRowSpan>(64);
        private readonly ContiguousBuffer<PreparedPawnLabelPresentation> _pawnLabels =
            new ContiguousBuffer<PreparedPawnLabelPresentation>(64);
        private readonly SnapshotSlot<WorkGridSnapshot> _slot = new SnapshotSlot<WorkGridSnapshot>();
        private readonly Dictionary<ushort, int> _bestPawnIds = new Dictionary<ushort, int>();
        private readonly HashSet<int> _snapshotPawnIds = new HashSet<int>();
        private readonly Dictionary<int, int> _pawnDynamicVersions = new Dictionary<int, int>();
        private int _skillRevision;
        private IWorkTabLayoutController _layout;
        private int _layoutSignature;
        private bool _hasLayoutSignature;
        private WorkGridRevisionSet _revisions;
        private long _snapshotRevision;
        private long _topologyRevision;
        private int _pawnLabelSourceSignature;
        private PawnColumnWorker_Label _labelWorker;
        private int _labelWorkerLayoutRevision = int.MinValue;
        private WorkTabEffectiveStateRevision _effectiveStateRevision;
        private bool _hasEffectiveStateRevision;

        internal WorkGridSnapshotProvider()
        {
            _active = this;
            WorkTabEffectiveStateRuntime.RegisterPreviewCacheClearer(ClearActive);
        }

        internal static void ClearActive()
        {
            _active?.Clear();
        }

        internal static bool IsActiveEffectiveStateCurrent()
        {
            return _active == null ||
                   (_active._hasEffectiveStateRevision &&
                    _active._effectiveStateRevision == WorkTabEffectiveStateRuntime.CurrentRevision);
        }

        internal WorkGridSnapshot Current => _slot.Current;

        internal WorkGridSnapshot Prepare(
            IWorkTabLayoutController layout,
            PawnTable table,
            WorkTabInvalidationVersion versions)
        {
            WorkTabEffectiveStateRevision effectiveStateRevision =
                WorkTabEffectiveStateRuntime.BeginRenderPass();
            if (layout == null || table == null || layout.GeometrySnapshot == null)
            {
                return null;
            }

            // External priority stores do not expose a safe content revision
            // for this snapshot. Keep the native/Harmony path authoritative
            // instead of retaining a potentially stale optimized frame.
            if (PriorityAuthorityBroker.ExternalWorkTabHasPriorityAuthority)
            {
                Clear();
                return null;
            }

            WorkGridRevisionSet current = versions.CategoryRevisions;
            PawnColumnWorker_Label labelWorker = ReferenceEquals(_layout, layout) &&
                _labelWorkerLayoutRevision == layout.LayoutRevision
                    ? _labelWorker
                    : FindExactLabelWorker(layout.Columns);
            int pawnLabelSourceSignature = PreparedPawnLabelCapture.ComputeSourceSignature(
                labelWorker,
                table);
            unchecked
            {
                pawnLabelSourceSignature = (pawnLabelSourceSignature * 397) ^
                    (BwtRaisedPriorityFeatureInstaller.IsFeatureActive ? 1 : 0);
                pawnLabelSourceSignature = (pawnLabelSourceSignature * 397) ^
                    (PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures ? 1 : 0);
                pawnLabelSourceSignature = (pawnLabelSourceSignature * 397) ^
                    (SleekWorkTabGateway.SleekOwnsWorkTab ? 1 : 0);
            }
            bool effectiveStateCurrent = _hasEffectiveStateRevision &&
                _effectiveStateRevision == effectiveStateRevision;
            if (_slot.Current != null &&
                ReferenceEquals(_layout, layout) &&
                _slot.Current.LayoutRevision == layout.LayoutRevision &&
                _pawnLabelSourceSignature == pawnLabelSourceSignature &&
                effectiveStateCurrent &&
                EqualConsumedRevisions(_revisions, current))
            {
                return _slot.Current;
            }

            int layoutSignature = ComputeLayoutSignature(layout);
            var timer = Stopwatch.StartNew();
            if (_pawnLabelSourceSignature == pawnLabelSourceSignature &&
                CanApplySparsePriorityUpdate(layout, current, layoutSignature, versions.PriorityDirtyKeys) &&
                TryApplySparsePriorityUpdate(
                    table,
                    layout,
                    current,
                    effectiveStateRevision,
                    versions.PriorityDirtyKeys,
                    out int updatedCellCount))
            {
                timer.Stop();
                WorkTabInvalidationHub.ClearConsumedPriorityKeys();
                WorkGridSnapshot incrementalSnapshot = _slot.Current;
                WorkGridRendererDiagnostics.RecordSnapshotBuild(
                    incrementalSnapshot?.Revision ?? 0,
                    timer.ElapsedTicks,
                    incrementalSnapshot?.RetainedCapacityBytes ?? 0,
                    versions.PriorityDirtyCount,
                    layout.GeometrySnapshot.RetainedCapacityBytes,
                    incremental: true,
                    updatedCellCount: updatedCellCount);
                return incrementalSnapshot;
            }

            if (_slot.Current != null &&
                _hasLayoutSignature &&
                _layoutSignature == layoutSignature &&
                _pawnLabelSourceSignature == pawnLabelSourceSignature &&
                effectiveStateCurrent &&
                EqualConsumedRevisions(_revisions, current))
            {
                return _slot.Current;
            }

            timer.Restart();
            Build(
                layout,
                table,
                current,
                layoutSignature,
                effectiveStateRevision,
                versions.PriorityDirtyKeys,
                labelWorker,
                pawnLabelSourceSignature);
            timer.Stop();
            WorkTabInvalidationHub.ClearConsumedPriorityKeys();
            WorkGridSnapshot snapshot = _slot.Current;
            WorkGridRendererDiagnostics.RecordSnapshotBuild(
                snapshot?.Revision ?? 0,
                timer.ElapsedTicks,
                snapshot?.RetainedCapacityBytes ?? 0,
                versions.PriorityDirtyCount,
                layout.GeometrySnapshot.RetainedCapacityBytes,
                incremental: false,
                updatedCellCount: snapshot?.Cells.Count ?? 0);
            return snapshot;
        }

        private bool CanApplySparsePriorityUpdate(
            IWorkTabLayoutController layout,
            WorkGridRevisionSet current,
            int layoutSignature,
            IReadOnlyList<WorkGridPriorityKey> dirtyKeys)
        {
            return _slot.Current != null &&
                   WorkTabEffectiveStateRuntime.CurrentRevision.IsLive &&
                   ReferenceEquals(_layout, layout) &&
                   _hasLayoutSignature &&
                   _layoutSignature == layoutSignature &&
                   dirtyKeys != null &&
                   dirtyKeys.Count > 0 &&
                   _revisions.Priority != current.Priority &&
                   EqualNonPriorityConsumedRevisions(_revisions, current);
        }

        private bool TryApplySparsePriorityUpdate(
            PawnTable table,
            IWorkTabLayoutController layout,
            WorkGridRevisionSet revisions,
            WorkTabEffectiveStateRevision effectiveStateRevision,
            IReadOnlyList<WorkGridPriorityKey> dirtyKeys,
            out int updatedCellCount)
        {
            updatedCellCount = 0;
            WorkGridSnapshot previous = _slot.Current;
            if (previous == null || previous.Cells.Count == 0)
            {
                return false;
            }

            var dirty = new HashSet<WorkGridPriorityKey>();
            for (int i = 0; i < dirtyKeys.Count; i++)
            {
                dirty.Add(dirtyKeys[i]);
            }

            var affectedWorkTypeIds = new HashSet<ushort>();
            foreach (WorkGridPriorityKey key in dirty)
            {
                affectedWorkTypeIds.Add(key.WorkTypeId);
            }

            IReadOnlyList<WorkTabLayoutColumn> columns = layout.Columns;
            var bestPawnIds = new Dictionary<ushort, int>(_bestPawnIds);
            var changedBestPawnIds = new Dictionary<ushort, BestPawnChange>();
            foreach (ushort workTypeId in affectedWorkTypeIds)
            {
                if (!TryResolvePriorityWorker(
                        columns,
                        workTypeId,
                        out WorkTypeDef workType,
                        out PawnColumnWorker_WorkPriority worker))
                {
                    return false;
                }

                int previousBestPawnId = bestPawnIds.TryGetValue(workTypeId, out int resolved)
                    ? resolved
                    : -1;
                int currentBestPawnId = FindBestPawnId(table, workType, worker);
                bestPawnIds[workTypeId] = currentBestPawnId;
                if (previousBestPawnId != currentBestPawnId)
                {
                    changedBestPawnIds[workTypeId] = new BestPawnChange(
                        previousBestPawnId,
                        currentBestPawnId);
                }
            }

            var replacements = new Dictionary<int, WorkCellVisualState>();
            var preparedRowReplacements = new Dictionary<int, WorkGridPreparedRowSpan>();
            var rowIndexByPawnId = new Dictionary<int, int>();
            for (int rowIndex = 0; rowIndex < previous.Rows.Count; rowIndex++)
            {
                int pawnId = previous.Rows[rowIndex].PawnId;
                if (pawnId >= 0)
                {
                    rowIndexByPawnId[pawnId] = rowIndex;
                }
            }
            uint cellRevision = unchecked((uint)(_snapshotRevision + 1));
            for (int i = 0; i < previous.Cells.Count; i++)
            {
                WorkCellVisualState cell = previous.Cells[i];
                ushort workTypeId = cell.WorkType?.shortHash ?? 0;
                bool dirtyCell = dirty.Contains(
                    new WorkGridPriorityKey(cell.PawnId, workTypeId));
                bool bestPawnChanged = changedBestPawnIds.TryGetValue(
                    workTypeId,
                    out BestPawnChange bestPawnChange) &&
                    (cell.PawnId == bestPawnChange.PreviousPawnId ||
                     cell.PawnId == bestPawnChange.CurrentPawnId);
                if (!dirtyCell && !bestPawnChanged)
                {
                    continue;
                }

                if (cell.ColumnIndex >= columns.Count ||
                    cell.ColumnIndex >= previous.Columns.Count)
                {
                    return false;
                }

                WorkGridColumnEntry snapshotColumn = previous.Columns[cell.ColumnIndex];
                if (snapshotColumn.WorkerKind != WorkGridColumnWorkerKind.WorkPriority &&
                    snapshotColumn.WorkerKind != WorkGridColumnWorkerKind.SubWorkPriority)
                {
                    continue;
                }

                if (cell.Pawn == null || cell.WorkType == null)
                {
                    return false;
                }

                WorkTabLayoutColumn column = columns[cell.ColumnIndex];
                if (snapshotColumn.WorkType != cell.WorkType)
                {
                    return false;
                }

                WorkGiver subWorkGiver = snapshotColumn.SubWorkGiver;
                WorkTypeDef parentVisualWorkType = subWorkGiver == null ||
                    !snapshotColumn.IsExpandBesideChild
                        ? column.Column?.workType
                        : null;
                int bestPawnId = parentVisualWorkType != null &&
                    bestPawnIds.TryGetValue(
                        parentVisualWorkType.shortHash,
                        out int resolvedBestPawnId)
                        ? resolvedBestPawnId
                        : -1;

                replacements[i] = BuildCell(
                    cell.Pawn,
                    cell.WorkType,
                    parentVisualWorkType,
                    subWorkGiver,
                    cell.ColumnIndex,
                    bestPawnId,
                    cellRevision);
                updatedCellCount++;
                if (rowIndexByPawnId.TryGetValue(cell.PawnId, out int preparedRowIndex) &&
                    preparedRowIndex < previous.PreparedRows.Count)
                {
                    preparedRowReplacements[preparedRowIndex] =
                        previous.PreparedRows[preparedRowIndex].WithRevision(cellRevision);
                }
            }

            _revisions = revisions;
            foreach (ushort workTypeId in affectedWorkTypeIds)
            {
                _bestPawnIds[workTypeId] = bestPawnIds[workTypeId];
            }
            _effectiveStateRevision = effectiveStateRevision;
            _hasEffectiveStateRevision = true;
            _snapshotRevision++;
            _slot.Publish(new WorkGridSnapshot(
                _snapshotRevision,
                previous.TopologyRevision,
                layout.LayoutRevision,
                revisions,
                previous.Rows,
                previous.Columns,
                previous.Cells.WithReplacements(replacements),
                previous.PreparedRows.WithReplacements(preparedRowReplacements),
                previous.PawnLabels,
                previous.RetainedCapacityBytes,
                ParentPriorityRead.GetObservedManualModeForDisplay(
                    previous.ManualPriorities),
                previous.MaxPriority,
                previous.UiScaleRevision,
                previous.FontThemeRevision,
                previous.PriorityRangeRevision));
            return true;
        }

        private static bool TryResolvePriorityWorker(
            IReadOnlyList<WorkTabLayoutColumn> columns,
            ushort workTypeId,
            out WorkTypeDef workType,
            out PawnColumnWorker_WorkPriority worker)
        {
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                WorkTabLayoutColumn column = columns[columnIndex];
                WorkTypeDef candidate = column.Column?.workType;
                if (!column.IsExpandBesideChild &&
                    candidate?.shortHash == workTypeId &&
                    column.Column?.Worker is PawnColumnWorker_WorkPriority candidateWorker)
                {
                    workType = candidate;
                    worker = candidateWorker;
                    return true;
                }
            }

            workType = null;
            worker = null;
            return false;
        }

        private readonly struct BestPawnChange
        {
            internal BestPawnChange(int previousPawnId, int currentPawnId)
            {
                PreviousPawnId = previousPawnId;
                CurrentPawnId = currentPawnId;
            }

            internal int PreviousPawnId { get; }
            internal int CurrentPawnId { get; }
        }

        internal void Clear()
        {
            _slot.Clear();
            _rows.Clear();
            _columns.Clear();
            _cells.Clear();
            _preparedRows.Clear();
            _pawnLabels.Clear();
            _bestPawnIds.Clear();
            _snapshotPawnIds.Clear();
            _pawnDynamicVersions.Clear();
            _skillRevision = 0;
            _layout = null;
            _layoutSignature = 0;
            _hasLayoutSignature = false;
            _revisions = default;
            _effectiveStateRevision = default;
            _hasEffectiveStateRevision = false;
            _pawnLabelSourceSignature = 0;
            _labelWorker = null;
            _labelWorkerLayoutRevision = int.MinValue;
            WorkGridRendererDiagnostics.RecordSnapshotCleared();
        }

        private void Build(
            IWorkTabLayoutController layout,
            PawnTable table,
            WorkGridRevisionSet revisions,
            int layoutSignature,
            WorkTabEffectiveStateRevision effectiveStateRevision,
            IReadOnlyList<WorkGridPriorityKey> priorityDirtyKeys,
            PawnColumnWorker_Label labelWorker,
            int pawnLabelSourceSignature)
        {
            WorkGridSnapshot previous = _slot.Current;
            bool canReuseRosterCells = CanReuseRosterCellVisuals(
                previous,
                table,
                revisions);
            _rows.Clear();
            _columns.Clear();
            _cells.Clear();
            _preparedRows.Clear();
            _pawnLabels.Clear();

            IReadOnlyList<WorkTabLayoutRow> layoutRows = layout.Rows;
            BuildRows(layoutRows);

            IReadOnlyList<WorkTabLayoutColumn> layoutColumns = layout.Columns;
            bool canSnapshotVanillaPriorityCells =
                      WorkGridVanillaCompatibilityPolicy.CanSnapshotVanillaPriorityCells() &&
                      !PriorityAuthorityBroker.ExternalWorkTabHasPriorityAuthority;
            BuildColumns(
                layoutColumns,
                canSnapshotVanillaPriorityCells,
                out Dictionary<ushort, PawnColumnWorker_WorkPriority> priorityWorkers,
                out Dictionary<ushort, WorkTypeDef> priorityWorkTypes);

            bool sameSnapshotColumns = canReuseRosterCells &&
                HaveSameSnapshotColumns(previous, _columns);
            bool prioritiesCompatibleWithAddition =
                _revisions.Priority == revisions.Priority ||
                PriorityChangesOnlyAffectNewPawns(priorityDirtyKeys);
            List<Pawn> addedPawns = sameSnapshotColumns && prioritiesCompatibleWithAddition
                ? FindPureRosterAdditions(table)
                : null;
            Dictionary<ushort, int> bestPawnIds = ResolveBestPawnIds(
                table,
                priorityWorkers,
                priorityWorkTypes,
                addedPawns);

            uint cellRevision = unchecked((uint)(_snapshotRevision + 1));
            BuildCells(
                layoutRows,
                layoutColumns,
                canSnapshotVanillaPriorityCells,
                sameSnapshotColumns,
                previous,
                bestPawnIds,
                cellRevision);

            BuildPawnLabels(layoutRows, labelWorker);
            BuildPreparedRowSpans(cellRevision);
            PublishSnapshot(
                layout,
                table,
                revisions,
                layoutSignature,
                effectiveStateRevision,
                labelWorker,
                pawnLabelSourceSignature,
                bestPawnIds);
        }

        private Dictionary<ushort, int> ResolveBestPawnIds(
            PawnTable table,
            Dictionary<ushort, PawnColumnWorker_WorkPriority> priorityWorkers,
            Dictionary<ushort, WorkTypeDef> priorityWorkTypes,
            List<Pawn> addedPawns)
        {
            var bestPawnIds = new Dictionary<ushort, int>();
            foreach (KeyValuePair<ushort, PawnColumnWorker_WorkPriority> entry in priorityWorkers)
            {
                WorkTypeDef workType = priorityWorkTypes[entry.Key];
                if (addedPawns != null &&
                    _bestPawnIds.TryGetValue(entry.Key, out int previousBestPawnId) &&
                    TryFindBestPawnIdAfterAdditions(
                        table,
                        workType,
                        entry.Value,
                        previousBestPawnId,
                        addedPawns,
                        out int incrementalBestPawnId))
                {
                    bestPawnIds[entry.Key] = incrementalBestPawnId;
                }
                else
                {
                    bestPawnIds[entry.Key] = FindBestPawnId(table, workType, entry.Value);
                }
            }
            return bestPawnIds;
        }

        private void BuildPawnLabels(
            IReadOnlyList<WorkTabLayoutRow> layoutRows,
            PawnColumnWorker_Label labelWorker)
        {
            bool canPreparePawnLabels = CanPrepareLabelWorker(labelWorker);
            for (int rowIndex = 0; rowIndex < layoutRows.Count; rowIndex++)
            {
                Pawn pawn = layoutRows[rowIndex].Pawn;
                _pawnLabels.Add(canPreparePawnLabels && pawn != null
                    ? PreparedPawnLabelCapture.Capture(labelWorker, pawn)
                    : default);
            }
        }

        /// <summary>
        /// Commits the completed snapshot and its cache baselines together. No
        /// caller may observe the new revision before all reuse state is current.
        /// </summary>
        private void PublishSnapshot(
            IWorkTabLayoutController layout,
            PawnTable table,
            WorkGridRevisionSet revisions,
            int layoutSignature,
            WorkTabEffectiveStateRevision effectiveStateRevision,
            PawnColumnWorker_Label labelWorker,
            int pawnLabelSourceSignature,
            Dictionary<ushort, int> bestPawnIds)
        {
            _layoutSignature = layoutSignature;
            _layout = layout;
            _hasLayoutSignature = true;
            _revisions = revisions;
            _effectiveStateRevision = effectiveStateRevision;
            _pawnLabelSourceSignature = pawnLabelSourceSignature;
            _labelWorker = labelWorker;
            _labelWorkerLayoutRevision = layout.LayoutRevision;
            _hasEffectiveStateRevision = true;
            _snapshotRevision++;
            _topologyRevision++;
            _bestPawnIds.Clear();
            foreach (KeyValuePair<ushort, int> entry in bestPawnIds)
            {
                _bestPawnIds[entry.Key] = entry.Value;
            }
            CaptureSnapshotPawnIds(table);
            CapturePawnPresentationVersions(table);
            int retainedBytes = (_rows.Capacity * 40) + (_columns.Capacity * 56) +
                (_cells.Capacity * 56) + (_preparedRows.Capacity * 12) +
                (_pawnLabels.Capacity * 40);
            int maxPriority = WorkPrioritySystem.GetMaxPriority();
            int presentationRevision = unchecked((int)revisions.SettingsThemeLanguageScale);
            _slot.Publish(new WorkGridSnapshot(
                _snapshotRevision,
                _topologyRevision,
                layout.LayoutRevision,
                revisions,
                _rows.ToSnapshot(),
                _columns.ToSnapshot(),
                _cells.ToSnapshot(),
                _preparedRows.ToSnapshot(),
                _pawnLabels.ToSnapshot(),
                retainedBytes,
                ParentPriorityRead.GetObservedManualModeForDisplay(
                    true),
                maxPriority,
                Mathf.RoundToInt(Prefs.UIScale * 1000f),
                presentationRevision,
                unchecked((presentationRevision * 397) ^ maxPriority)));
        }

        private void BuildRows(IReadOnlyList<WorkTabLayoutRow> layoutRows)
        {
            for (int i = 0; i < layoutRows.Count; i++)
            {
                WorkTabLayoutRow row = layoutRows[i];
                if (row.IsDivider)
                {
                    var divider = row.Divider;
                    Color color = divider?.DividerColor ?? Color.clear;
                    BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                    if (!BWTWorkTabEffectiveSettings.GetBool(SettingIDs.DividersCustomColors))
                    {
                        color = Color.gray;
                    }
                    color.a = Mathf.Max(color.a, settings?.dividerMinAlpha ?? 0.35f);
                    if (divider?.IsCollapsed ?? false)
                    {
                        color.a = Mathf.Clamp01(color.a * 0.6f);
                    }
                    _rows.Add(new WorkGridRowEntry(
                        WorkGridRowKind.Divider,
                        -1,
                        divider?.DividerName,
                        PackColor(color),
                        divider?.DisplayOrder ?? 0,
                        divider?.IsCollapsed ?? false,
                        PackColor(color),
                        WorkGridRowVisualFlags.HasBackground));
                }
                else
                {
                    Color background = Color.clear;
                    WorkGridRowVisualFlags rowFlags = WorkGridRowVisualFlags.None;
                    if (row.Pawn != null &&
                        PawnOrganizer.API.PawnColorDatabase.TryGetColor(row.Pawn, out Color pawnColor) &&
                        pawnColor.a > 0f)
                    {
                        background = new Color(
                            pawnColor.r,
                            pawnColor.g,
                            pawnColor.b,
                            Mathf.Clamp(pawnColor.a, 0.08f, 0.6f));
                        rowFlags = WorkGridRowVisualFlags.HasBackground;
                    }
                    _rows.Add(new WorkGridRowEntry(
                        WorkGridRowKind.Pawn,
                        row.Pawn?.thingIDNumber ?? -1,
                        null,
                        0,
                        0,
                        false,
                        PackColor(background),
                        rowFlags));
                }
            }
        }

        private void BuildColumns(
            IReadOnlyList<WorkTabLayoutColumn> layoutColumns,
            bool canSnapshotVanillaPriorityCells,
            out Dictionary<ushort, PawnColumnWorker_WorkPriority> priorityWorkers,
            out Dictionary<ushort, WorkTypeDef> priorityWorkTypes)
        {
            priorityWorkers = new Dictionary<ushort, PawnColumnWorker_WorkPriority>();
            priorityWorkTypes = new Dictionary<ushort, WorkTypeDef>();
            for (int i = 0; i < layoutColumns.Count; i++)
            {
                WorkTabLayoutColumn column = layoutColumns[i];
                PawnColumnDef def = column.Column;
                WorkTypeDef sourceWorkType = def?.workType;
                WorkTypeDef workType = column.SubWorkParent ?? sourceWorkType;
                WorkGiver workGiver = null;
                if (TryResolveSubWorkColumn(
                        column,
                        out WorkGiver resolvedWorkGiver,
                        out WorkTypeDef resolvedParentWorkType))
                {
                    workGiver = resolvedWorkGiver;
                    workType = resolvedParentWorkType ?? workType;
                }
                object worker = def?.Worker;
                bool externalFluffyWorkGiver = FluffyWorkTabGateway.IsFluffyWorkGiverColumn(def);
                WorkGridColumnWorkerKind workerKind = workGiver != null
                    ? WorkGridColumnWorkerKind.SubWorkPriority
                    : canSnapshotVanillaPriorityCells &&
                      WorkGridVanillaCompatibilityPolicy.CanSnapshotPriorityColumn(def) &&
                      !externalFluffyWorkGiver
                        ? WorkGridColumnWorkerKind.WorkPriority
                        : worker is PawnColumnWorker_Label
                            ? WorkGridColumnWorkerKind.PawnLabel
                            : WorkGridColumnWorkerKind.Other;
                _columns.Add(new WorkGridColumnEntry(
                    (ushort)i,
                    workType?.shortHash ?? 0,
                    workGiver?.def?.shortHash ?? 0,
                    workType?.defName,
                    workGiver?.def?.defName,
                    worker?.GetType().FullName,
                    workerKind,
                    column.IsExpandBesideChild,
                    workType,
                    workGiver));
                if (!column.IsExpandBesideChild &&
                    !externalFluffyWorkGiver &&
                    sourceWorkType != null &&
                    canSnapshotVanillaPriorityCells &&
                    WorkGridVanillaCompatibilityPolicy.CanSnapshotPriorityColumn(def) &&
                    worker is PawnColumnWorker_WorkPriority priorityWorker &&
                    !priorityWorkers.ContainsKey(sourceWorkType.shortHash))
                {
                    priorityWorkers[sourceWorkType.shortHash] = priorityWorker;
                    priorityWorkTypes[sourceWorkType.shortHash] = sourceWorkType;
                }
            }
        }

        private void BuildCells(
            IReadOnlyList<WorkTabLayoutRow> layoutRows,
            IReadOnlyList<WorkTabLayoutColumn> layoutColumns,
            bool canSnapshotVanillaPriorityCells,
            bool sameSnapshotColumns,
            WorkGridSnapshot previous,
            Dictionary<ushort, int> bestPawnIds,
            uint cellRevision)
        {
            var reusableCells = new Dictionary<long, WorkCellVisualState>();
            if (sameSnapshotColumns)
            {
                for (int i = 0; i < previous.Cells.Count; i++)
                {
                    WorkCellVisualState cell = previous.Cells[i];
                    reusableCells[ComposeCellKey(cell.PawnId, cell.ColumnIndex)] = cell;
                }
            }

            for (int rowIndex = 0; rowIndex < layoutRows.Count; rowIndex++)
            {
                Pawn pawn = layoutRows[rowIndex].Pawn;
                if (pawn == null)
                {
                    continue;
                }

                for (int columnIndex = 0; columnIndex < layoutColumns.Count; columnIndex++)
                {
                    WorkTabLayoutColumn column = layoutColumns[columnIndex];
                    WorkGridColumnEntry snapshotColumn = _columns[columnIndex];
                    WorkTypeDef workType = snapshotColumn.WorkType;
                    WorkGiver subWorkGiver = snapshotColumn.SubWorkGiver;
                    WorkTypeDef parentVisualWorkType = column.IsExpandBesideChild
                        ? null
                        : column.Column?.workType;
                    if (workType == null ||
                        FluffyWorkTabGateway.IsFluffyWorkGiverColumn(column.Column) ||
                        (subWorkGiver == null &&
                         (!canSnapshotVanillaPriorityCells ||
                          !WorkGridVanillaCompatibilityPolicy.CanSnapshotPriorityColumn(column.Column))))
                    {
                        continue;
                    }

                    int bestPawnId = parentVisualWorkType != null &&
                        bestPawnIds.TryGetValue(parentVisualWorkType.shortHash, out int resolvedBestPawnId)
                        ? resolvedBestPawnId
                        : -1;
                    ushort snapshotColumnIndex = (ushort)columnIndex;
                    if (subWorkGiver == null &&
                        reusableCells.TryGetValue(
                            ComposeCellKey(pawn.thingIDNumber, snapshotColumnIndex),
                            out WorkCellVisualState reusable) &&
                        TryReuseParentCell(
                            reusable,
                            pawn,
                            workType,
                            snapshotColumnIndex,
                            bestPawnId,
                            cellRevision,
                            out WorkCellVisualState reused))
                    {
                        _cells.Add(reused);
                    }
                    else
                    {
                        _cells.Add(BuildCell(
                            pawn,
                            workType,
                            parentVisualWorkType,
                            subWorkGiver,
                            snapshotColumnIndex,
                            bestPawnId,
                            cellRevision));
                    }
                }
            }
        }

        private void BuildPreparedRowSpans(uint revision)
        {
            int cellIndex = 0;
            for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
            {
                int firstCellIndex = cellIndex;
                int pawnId = _rows[rowIndex].PawnId;
                while (cellIndex < _cells.Count && _cells[cellIndex].PawnId == pawnId)
                {
                    cellIndex++;
                }

                _preparedRows.Add(new WorkGridPreparedRowSpan(
                    firstCellIndex,
                    cellIndex - firstCellIndex,
                    pawnId >= 0 ? revision : 0U));
            }
        }

        private static PawnColumnWorker_Label FindExactLabelWorker(
            IReadOnlyList<WorkTabLayoutColumn> columns)
        {
            for (int index = 0; index < columns.Count; index++)
            {
                PawnColumnDef column = columns[index].Column;
                if (column?.Worker?.GetType() == typeof(PawnColumnWorker_Label))
                {
                    return column.Worker as PawnColumnWorker_Label;
                }
            }
            return null;
        }

        private static bool CanPrepareLabelWorker(PawnColumnWorker_Label worker)
        {
            return worker != null &&
                   BwtRaisedPriorityFeatureInstaller.IsFeatureActive &&
                   PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures &&
                   !SleekWorkTabGateway.SleekOwnsWorkTab &&
                   WorkGridVanillaCompatibilityPolicy.CanPrepareVanillaLabelCells(worker.def);
        }

        private bool CanReuseRosterCellVisuals(
            WorkGridSnapshot previous,
            PawnTable table,
            WorkGridRevisionSet revisions)
        {
            return previous != null &&
                   (_revisions.CapabilitySkill == revisions.CapabilitySkill ||
                    AreCachedPawnPresentationVersionsCurrent(table)) &&
                   _revisions.ScheduleHour == revisions.ScheduleHour &&
                   _revisions.SubWorkOverride == revisions.SubWorkOverride &&
                   _revisions.SettingsThemeLanguageScale == revisions.SettingsThemeLanguageScale;
        }

        private bool PriorityChangesOnlyAffectNewPawns(
            IReadOnlyList<WorkGridPriorityKey> priorityDirtyKeys)
        {
            if (priorityDirtyKeys == null || priorityDirtyKeys.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < priorityDirtyKeys.Count; i++)
            {
                if (_snapshotPawnIds.Contains(priorityDirtyKeys[i].PawnId))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HaveSameSnapshotColumns(
            WorkGridSnapshot previous,
            ContiguousBuffer<WorkGridColumnEntry> columns)
        {
            if (previous == null || previous.Columns.Count != columns.Count)
            {
                return false;
            }

            for (int i = 0; i < columns.Count; i++)
            {
                WorkGridColumnEntry left = previous.Columns[i];
                WorkGridColumnEntry right = columns[i];
                if (left.WorkTypeId != right.WorkTypeId ||
                    left.WorkGiverId != right.WorkGiverId ||
                    left.WorkerKind != right.WorkerKind ||
                    left.IsExpandBesideChild != right.IsExpandBesideChild)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryReuseParentCell(
            WorkCellVisualState previous,
            Pawn pawn,
            WorkTypeDef workType,
            ushort columnIndex,
            int bestPawnId,
            uint revision,
            out WorkCellVisualState reused)
        {
            reused = default;
            if (previous.PawnId != pawn.thingIDNumber ||
                previous.WorkType != workType ||
                previous.ColumnIndex != columnIndex)
            {
                return false;
            }

            int priority = ParentPriorityRead.GetObserved(pawn, workType);
            if (previous.Priority != priority)
            {
                return false;
            }

            WorkCellVisualFlags flags = previous.Flags &
                ~(WorkCellVisualFlags.BestPawn | WorkCellVisualFlags.ManualPriorityMode);
            if (pawn.thingIDNumber == bestPawnId)
            {
                flags |= WorkCellVisualFlags.BestPawn;
            }
            if (ParentPriorityRead.GetObservedManualMode(
                    pawn,
                    workType,
                    true))
            {
                flags |= WorkCellVisualFlags.ManualPriorityMode;
            }

            reused = new WorkCellVisualState(
                pawn,
                workType,
                pawn.thingIDNumber,
                columnIndex,
                previous.Priority,
                previous.SkillBand,
                previous.SkillBlend,
                previous.Passion,
                previous.PriorityColor,
                flags,
                revision);
            return true;
        }

        private static long ComposeCellKey(int pawnId, ushort columnIndex)
        {
            return ((long)pawnId << 16) | columnIndex;
        }

        private static int ComputeLayoutSignature(IWorkTabLayoutController layout)
        {
            unchecked
            {
                int hash = 17;
                IReadOnlyList<WorkTabLayoutRow> rows = layout.Rows;
                hash = (hash * 31) + rows.Count;
                for (int i = 0; i < rows.Count; i++)
                {
                    WorkTabLayoutRow row = rows[i];
                    hash = (hash * 31) + (row.Pawn?.thingIDNumber ?? 0);
                    hash = (hash * 31) + (row.Divider?.DisplayOrder ?? 0);
                    hash = (hash * 31) + ((row.Divider?.IsCollapsed ?? false) ? 1 : 0);
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(row.Divider?.DividerName ?? string.Empty);
                    hash = (hash * 31) + unchecked((int)PackColor(row.Divider?.DividerColor ?? Color.clear));
                }

                IReadOnlyList<WorkTabLayoutColumn> columns = layout.Columns;
                hash = (hash * 31) + columns.Count;
                for (int i = 0; i < columns.Count; i++)
                {
                    WorkTabLayoutColumn column = columns[i];
                    TryResolveSubWorkColumn(
                        column,
                        out WorkGiver workGiver,
                        out WorkTypeDef parentWorkType);
                    hash = (hash * 31) + (column.Column?.shortHash ?? 0);
                    hash = (hash * 31) + ((parentWorkType ?? column.SubWorkParent)?.shortHash ?? 0);
                    hash = (hash * 31) + (workGiver?.def?.shortHash ?? 0);
                    hash = (hash * 31) + (column.IsExpandBesideChild ? 1 : 0);
                }

                return hash;
            }
        }

        private static bool TryResolveSubWorkColumn(
            WorkTabLayoutColumn column,
            out WorkGiver workGiver,
            out WorkTypeDef parentWorkType)
        {
            workGiver = null;
            parentWorkType = null;
            if (!SubWorkDrilldownState.TryGetWorkGiverForColumn(
                column,
                out WorkGiver resolvedWorkGiver,
                out WorkTypeDef resolvedParentWorkType,
                out _)
                || resolvedWorkGiver?.def == null)
            {
                return false;
            }

            workGiver = resolvedWorkGiver;
            parentWorkType = resolvedParentWorkType;
            return true;
        }

        private static WorkCellVisualState BuildCell(
            Pawn pawn,
            WorkTypeDef workType,
            WorkTypeDef parentVisualWorkType,
            WorkGiver workGiver,
            ushort columnIndex,
            int bestPawnId,
            uint revision)
        {
            WorkGiverCellPresentationCache.CellPresentation subWorkPresentation = null;
            if (workGiver != null)
            {
                subWorkPresentation = WorkGiverCellPresentationCache.Resolve(
                    workGiver,
                    workType,
                    pawn);
            }

            WorkBoxVisualState visual;
            if (parentVisualWorkType == null && workGiver != null)
            {
                // Expand-beside draws only the prepared child. Do not compute a
                // parent visual that this snapshot cell will never consume.
                visual = subWorkPresentation.WorkBoxVisual;
            }
            else
            {
                // Focus view reuses the original work-type column and crossfades
                // its parent box behind the prepared child box.
                WorkTypeDef visualWorkType = parentVisualWorkType ?? workType;
                int priority = ParentPriorityRead.GetObserved(pawn, visualWorkType);
                bool disabled = pawn.WorkTypeIsDisabled(visualWorkType);
                bool ageDisabled = pawn.IsWorkTypeDisabledByAge(visualWorkType, out _);
                bool incapable = !WorkTabActionability.CanApplyAnyWorkGiver(pawn, visualWorkType);
                bool overrideRing = WorkGiverReassignmentManager.LockedSubWorkOverridesDisabledParent() &&
                                    !disabled &&
                                    priority <= WorkPrioritySystem.DisabledPriority &&
                                    WorkGiverReassignmentManager.HasEnabledPawnOverrideForWorkType(
                                        pawn,
                                        visualWorkType);
                visual = PreparedWorkBoxRenderer.Capture(
                    pawn,
                    visualWorkType,
                    priority,
                    disabled,
                    incapable,
                    ageDisabled,
                    overrideRing,
                    priority > WorkPrioritySystem.DisabledPriority,
                    bestPawnId);
            }

            return new WorkCellVisualState(
                pawn,
                workType,
                pawn.thingIDNumber,
                columnIndex,
                visual.Priority,
                visual.SkillBand,
                visual.SkillBlend,
                visual.Passion,
                visual.PriorityColor,
                visual.Flags,
                revision,
                subWorkPresentation);
        }

        private static int FindBestPawnId(
            PawnTable table,
            WorkTypeDef workType,
            PawnColumnWorker_WorkPriority worker)
        {
            if (workType == null || worker == null || table?.cachedPawns == null)
            {
                return -1;
            }

            Pawn bestPawn = null;
            for (int i = 0; i < table.cachedPawns.Count; i++)
            {
                Pawn candidate = table.cachedPawns[i];
                if (!IsEligibleBestPawn(candidate, workType))
                {
                    continue;
                }

                if (bestPawn == null || IsBetterPawn(candidate, bestPawn, workType, worker))
                {
                    bestPawn = candidate;
                }
            }
            return bestPawn?.thingIDNumber ?? -1;
        }

        private List<Pawn> FindPureRosterAdditions(PawnTable table)
        {
            if (table?.cachedPawns == null || _snapshotPawnIds.Count == 0)
            {
                return null;
            }

            var currentIds = new HashSet<int>();
            var additions = new List<Pawn>();
            for (int i = 0; i < table.cachedPawns.Count; i++)
            {
                Pawn pawn = table.cachedPawns[i];
                if (pawn == null || !currentIds.Add(pawn.thingIDNumber))
                {
                    continue;
                }

                if (!_snapshotPawnIds.Contains(pawn.thingIDNumber))
                {
                    additions.Add(pawn);
                }
            }

            if (currentIds.Count < _snapshotPawnIds.Count)
            {
                return null;
            }
            foreach (int pawnId in _snapshotPawnIds)
            {
                if (!currentIds.Contains(pawnId))
                {
                    return null;
                }
            }

            return additions;
        }

        private static bool TryFindBestPawnIdAfterAdditions(
            PawnTable table,
            WorkTypeDef workType,
            PawnColumnWorker_WorkPriority worker,
            int previousBestPawnId,
            List<Pawn> additions,
            out int bestPawnId)
        {
            bestPawnId = -1;
            if (table?.cachedPawns == null || workType == null || worker == null || additions == null)
            {
                return false;
            }

            Pawn bestPawn = null;
            if (previousBestPawnId >= 0)
            {
                for (int i = 0; i < table.cachedPawns.Count; i++)
                {
                    Pawn pawn = table.cachedPawns[i];
                    if (pawn?.thingIDNumber == previousBestPawnId)
                    {
                        bestPawn = pawn;
                        break;
                    }
                }

                if (!IsEligibleBestPawn(bestPawn, workType))
                {
                    return false;
                }
            }

            for (int i = 0; i < additions.Count; i++)
            {
                Pawn candidate = additions[i];
                if (!IsEligibleBestPawn(candidate, workType))
                {
                    continue;
                }

                if (bestPawn == null || IsBetterPawn(candidate, bestPawn, workType, worker))
                {
                    bestPawn = candidate;
                }
            }

            bestPawnId = bestPawn?.thingIDNumber ?? -1;
            return true;
        }

        private void CaptureSnapshotPawnIds(PawnTable table)
        {
            _snapshotPawnIds.Clear();
            for (int i = 0; table?.cachedPawns != null && i < table.cachedPawns.Count; i++)
            {
                Pawn pawn = table.cachedPawns[i];
                if (pawn != null)
                {
                    _snapshotPawnIds.Add(pawn.thingIDNumber);
                }
            }
        }

        private void CapturePawnPresentationVersions(PawnTable table)
        {
            _pawnDynamicVersions.Clear();
            _skillRevision = WorkGiverPresentationInvalidation.SkillRevision;
            for (int i = 0; table?.cachedPawns != null && i < table.cachedPawns.Count; i++)
            {
                Pawn pawn = table.cachedPawns[i];
                if (pawn != null)
                {
                    _pawnDynamicVersions[pawn.thingIDNumber] =
                        WorkGiverPresentationInvalidation.GetPawnDynamicVersion(pawn);
                }
            }
        }

        private bool AreCachedPawnPresentationVersionsCurrent(PawnTable table)
        {
            if (table?.cachedPawns == null ||
                _skillRevision != WorkGiverPresentationInvalidation.SkillRevision)
            {
                return false;
            }

            int matched = 0;
            for (int i = 0; i < table.cachedPawns.Count; i++)
            {
                Pawn pawn = table.cachedPawns[i];
                if (pawn == null ||
                    !_pawnDynamicVersions.TryGetValue(pawn.thingIDNumber, out int previousVersion))
                {
                    continue;
                }

                if (previousVersion != WorkGiverPresentationInvalidation.GetPawnDynamicVersion(pawn))
                {
                    return false;
                }
                matched++;
            }

            return matched == _pawnDynamicVersions.Count;
        }

        private static bool IsEligibleBestPawn(Pawn pawn, WorkTypeDef workType)
        {
            return pawn != null &&
                   !pawn.Dead &&
                   pawn.workSettings != null &&
                   pawn.workSettings.EverWork &&
                   !pawn.WorkTypeIsDisabled(workType) &&
                   WorkTabActionability.CanApplyAnyWorkGiver(pawn, workType);
        }

        private static bool IsBetterPawn(
            Pawn candidate,
            Pawn bestPawn,
            WorkTypeDef workType,
            PawnColumnWorker_WorkPriority worker)
        {
            if (!WorkTabEffectiveStateRuntime.IsPreviewActive)
            {
                return worker.Compare(candidate, bestPawn) > 0;
            }

            int candidatePriority = ParentPriorityRead.GetObserved(candidate, workType);
            int bestPriority = ParentPriorityRead.GetObserved(bestPawn, workType);
            if (candidatePriority != bestPriority)
            {
                // RimWorld's Work-priority comparison treats the smallest
                // enabled number as the preferred assignment.
                return candidatePriority < bestPriority;
            }

            return worker.Compare(candidate, bestPawn) > 0;
        }

        private static uint PackColor(Color color)
        {
            Color32 packed = color;
            return (uint)(packed.r | (packed.g << 8) | (packed.b << 16) | (packed.a << 24));
        }

        private static bool EqualConsumedRevisions(WorkGridRevisionSet left, WorkGridRevisionSet right)
        {
            return left.GameState == right.GameState &&
                   left.Priority == right.Priority &&
                   left.CapabilitySkill == right.CapabilitySkill &&
                   left.ScheduleHour == right.ScheduleHour &&
                   left.SubWorkOverride == right.SubWorkOverride &&
                   left.SettingsThemeLanguageScale == right.SettingsThemeLanguageScale;
        }

        private static bool EqualNonPriorityConsumedRevisions(WorkGridRevisionSet left, WorkGridRevisionSet right)
        {
            return left.GameState == right.GameState &&
                   left.CapabilitySkill == right.CapabilitySkill &&
                   left.ScheduleHour == right.ScheduleHour &&
                   left.SubWorkOverride == right.SubWorkOverride &&
                   left.SettingsThemeLanguageScale == right.SettingsThemeLanguageScale;
        }
    }
}
