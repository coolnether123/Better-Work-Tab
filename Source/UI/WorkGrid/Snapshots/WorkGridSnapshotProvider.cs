using System;
using System.Collections.Generic;
using System.Diagnostics;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WorkGrid.Compatibility;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.Settings;
using RimWorld;
using Spine.Api;
using Better_Work_Tab.Foundation;
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
            bool effectiveStateCurrent = _hasEffectiveStateRevision &&
                _effectiveStateRevision == effectiveStateRevision;
            if (_slot.Current != null &&
                ReferenceEquals(_layout, layout) &&
                _slot.Current.LayoutRevision == layout.LayoutRevision &&
                effectiveStateCurrent &&
                EqualConsumedRevisions(_revisions, current))
            {
                return _slot.Current;
            }

            int layoutSignature = ComputeLayoutSignature(layout);
            var timer = Stopwatch.StartNew();
            if (CanApplySparsePriorityUpdate(layout, current, layoutSignature, versions.PriorityDirtyKeys) &&
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
                versions.PriorityDirtyKeys);
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

            var affectedWorkTypes = new HashSet<ushort>();
            for (int i = 0; i < dirtyKeys.Count; i++)
            {
                affectedWorkTypes.Add(dirtyKeys[i].WorkTypeId);
            }

            var affectedParentWorkTypes = new HashSet<ushort>();
            IReadOnlyList<WorkTabLayoutColumn> columns = layout.Columns;
            for (int i = 0; i < previous.Cells.Count; i++)
            {
                WorkCellVisualState cell = previous.Cells[i];
                ushort workTypeId = cell.WorkType?.shortHash ?? 0;
                if (!affectedWorkTypes.Contains(workTypeId) ||
                    cell.ColumnIndex >= columns.Count)
                {
                    continue;
                }

                WorkTabLayoutColumn column = columns[cell.ColumnIndex];
                if (column.SubWorkGiver == null)
                {
                    affectedParentWorkTypes.Add(workTypeId);
                }
            }

            var bestPawnIds = new Dictionary<ushort, int>();
            for (int i = 0; i < columns.Count; i++)
            {
                WorkTabLayoutColumn column = columns[i];
                WorkTypeDef workType = column.SubWorkParent ?? column.Column?.workType;
                if (column.SubWorkGiver != null || workType == null ||
                    !affectedParentWorkTypes.Contains(workType.shortHash) ||
                    FluffyWorkTabGateway.IsFluffyWorkGiverColumn(column.Column) ||
                    !WorkGridVanillaCompatibilityPolicy.CanSnapshotVanillaPriorityCells() ||
                    !WorkGridVanillaCompatibilityPolicy.CanSnapshotPriorityColumn(column.Column) ||
                    !(column.Column?.Worker is PawnColumnWorker_WorkPriority priorityWorker))
                {
                    continue;
                }

                if (!bestPawnIds.ContainsKey(workType.shortHash))
                {
                    bestPawnIds[workType.shortHash] = FindBestPawnId(table, workType, priorityWorker);
                }
            }

            foreach (ushort workTypeId in affectedParentWorkTypes)
            {
                if (!bestPawnIds.ContainsKey(workTypeId))
                {
                    return false;
                }
            }

            var replacements = new Dictionary<int, WorkCellVisualState>();
            uint cellRevision = unchecked((uint)(_snapshotRevision + 1));
            for (int i = 0; i < previous.Cells.Count; i++)
            {
                WorkCellVisualState cell = previous.Cells[i];
                ushort workTypeId = cell.WorkType?.shortHash ?? 0;
                bool affected = affectedWorkTypes.Contains(workTypeId);
                bool dirtyCell = dirty.Contains(
                    new WorkGridPriorityKey(cell.PawnId, workTypeId));
                if (!affected && !dirtyCell)
                {
                    continue;
                }

                if (cell.Pawn == null || cell.WorkType == null || cell.ColumnIndex >= columns.Count)
                {
                    return false;
                }

                WorkTabLayoutColumn column = columns[cell.ColumnIndex];
                WorkTypeDef columnWorkType = column.SubWorkParent ?? column.Column?.workType;
                if (columnWorkType != cell.WorkType)
                {
                    return false;
                }

                int bestPawnId = -1;
                if (column.SubWorkGiver == null &&
                    !bestPawnIds.TryGetValue(workTypeId, out bestPawnId))
                {
                    return false;
                }

                replacements[i] = BuildCell(
                    cell.Pawn,
                    cell.WorkType,
                    column.SubWorkGiver,
                    cell.ColumnIndex,
                    bestPawnId,
                    cellRevision);
                updatedCellCount++;
            }

            _revisions = revisions;
            _effectiveStateRevision = effectiveStateRevision;
            _hasEffectiveStateRevision = true;
            _snapshotRevision++;
            _slot.Publish(new WorkGridSnapshot(
                _snapshotRevision,
                layout.LayoutRevision,
                revisions,
                previous.Rows,
                previous.Columns,
                previous.Cells.WithReplacements(replacements),
                previous.RetainedCapacityBytes,
                WorkTabEffectiveStateRuntime.GetManualModeForDisplay(
                    Find.PlaySettings?.useWorkPriorities ?? previous.ManualPriorities),
                previous.MaxPriority,
                previous.UiScaleRevision,
                previous.FontThemeRevision,
                previous.PriorityRangeRevision));
            return true;
        }

        internal void Clear()
        {
            _slot.Clear();
            _rows.Clear();
            _columns.Clear();
            _cells.Clear();
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
            WorkGridRendererDiagnostics.RecordSnapshotCleared();
        }

        private void Build(
            IWorkTabLayoutController layout,
            PawnTable table,
            WorkGridRevisionSet revisions,
            int layoutSignature,
            WorkTabEffectiveStateRevision effectiveStateRevision,
            IReadOnlyList<WorkGridPriorityKey> priorityDirtyKeys)
        {
            WorkGridSnapshot previous = _slot.Current;
            bool canReuseRosterCells = CanReuseRosterCellVisuals(
                previous,
                table,
                revisions);
            _rows.Clear();
            _columns.Clear();
            _cells.Clear();

            IReadOnlyList<WorkTabLayoutRow> layoutRows = layout.Rows;
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

            IReadOnlyList<WorkTabLayoutColumn> layoutColumns = layout.Columns;
            bool canSnapshotVanillaPriorityCells =
                      WorkGridVanillaCompatibilityPolicy.CanSnapshotVanillaPriorityCells() &&
                      !PriorityAuthorityBroker.ExternalWorkTabHasPriorityAuthority;
            var bestPawnIds = new Dictionary<ushort, int>();
            var priorityWorkers = new Dictionary<ushort, PawnColumnWorker_WorkPriority>();
            var priorityWorkTypes = new Dictionary<ushort, WorkTypeDef>();
            for (int i = 0; i < layoutColumns.Count; i++)
            {
                WorkTabLayoutColumn column = layoutColumns[i];
                PawnColumnDef def = column.Column;
                WorkTypeDef workType = column.SubWorkParent ?? def?.workType;
                WorkGiverDef workGiver = column.SubWorkGiver;
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
                    workGiver?.shortHash ?? 0,
                    workType?.defName,
                    workGiver?.defName,
                    worker?.GetType().FullName,
                    workerKind));
                if (workGiver == null &&
                    !externalFluffyWorkGiver &&
                    workType != null &&
                    canSnapshotVanillaPriorityCells &&
                    WorkGridVanillaCompatibilityPolicy.CanSnapshotPriorityColumn(def) &&
                    worker is PawnColumnWorker_WorkPriority priorityWorker &&
                    !priorityWorkers.ContainsKey(workType.shortHash))
                {
                    priorityWorkers[workType.shortHash] = priorityWorker;
                    priorityWorkTypes[workType.shortHash] = workType;
                }
            }

            bool sameSnapshotColumns = canReuseRosterCells &&
                HaveSameSnapshotColumns(previous, _columns);
            bool prioritiesCompatibleWithAddition =
                _revisions.Priority == revisions.Priority ||
                PriorityChangesOnlyAffectNewPawns(priorityDirtyKeys);
            List<Pawn> addedPawns = sameSnapshotColumns && prioritiesCompatibleWithAddition
                ? FindPureRosterAdditions(table)
                : null;
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

            var reusableCells = new Dictionary<long, WorkCellVisualState>();
            if (sameSnapshotColumns)
            {
                for (int i = 0; i < previous.Cells.Count; i++)
                {
                    WorkCellVisualState cell = previous.Cells[i];
                    reusableCells[ComposeCellKey(cell.PawnId, cell.ColumnIndex)] = cell;
                }
            }

            uint cellRevision = unchecked((uint)(_snapshotRevision + 1));
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
                    WorkTypeDef workType = column.SubWorkParent ?? column.Column?.workType;
                    if (workType == null ||
                        FluffyWorkTabGateway.IsFluffyWorkGiverColumn(column.Column) ||
                        (column.SubWorkGiver == null &&
                         (!canSnapshotVanillaPriorityCells ||
                          !WorkGridVanillaCompatibilityPolicy.CanSnapshotPriorityColumn(column.Column))))
                    {
                        continue;
                    }

                    int bestPawnId = bestPawnIds.TryGetValue(workType.shortHash, out int resolvedBestPawnId)
                        ? resolvedBestPawnId
                        : -1;
                    ushort snapshotColumnIndex = (ushort)columnIndex;
                    if (column.SubWorkGiver == null &&
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
                            column.SubWorkGiver,
                            snapshotColumnIndex,
                            bestPawnId,
                            cellRevision));
                    }
                }
            }

            _layoutSignature = layoutSignature;
            _layout = layout;
            _hasLayoutSignature = true;
            _revisions = revisions;
            _effectiveStateRevision = effectiveStateRevision;
            _hasEffectiveStateRevision = true;
            _snapshotRevision++;
            _bestPawnIds.Clear();
            foreach (KeyValuePair<ushort, int> entry in bestPawnIds)
            {
                _bestPawnIds[entry.Key] = entry.Value;
            }
            CaptureSnapshotPawnIds(table);
            CapturePawnPresentationVersions(table);
            int retainedBytes = (_rows.Capacity * 40) + (_columns.Capacity * 40) + (_cells.Capacity * 28);
            int maxPriority = WorkPrioritySystem.GetMaxPriority();
            int presentationRevision = unchecked((int)revisions.SettingsThemeLanguageScale);
            _slot.Publish(new WorkGridSnapshot(
                _snapshotRevision,
                layout.LayoutRevision,
                revisions,
                _rows.ToSnapshot(),
                _columns.ToSnapshot(),
                _cells.ToSnapshot(),
                retainedBytes,
                WorkTabEffectiveStateRuntime.GetManualModeForDisplay(
                    Find.PlaySettings?.useWorkPriorities ?? true),
                maxPriority,
                Mathf.RoundToInt(Prefs.UIScale * 1000f),
                presentationRevision,
                unchecked((presentationRevision * 397) ^ maxPriority)));
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
                    left.WorkerKind != right.WorkerKind)
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

            int priority = WorkTabEffectiveStateRuntime.GetParentPriority(
                pawn,
                workType,
                WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType));
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
            if (WorkTabEffectiveStateRuntime.IsManualMode(
                    pawn,
                    workType,
                    Find.PlaySettings?.useWorkPriorities ?? true))
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
                    hash = (hash * 31) + (column.Column?.shortHash ?? 0);
                    hash = (hash * 31) + (column.SubWorkParent?.shortHash ?? 0);
                    hash = (hash * 31) + (column.SubWorkGiver?.shortHash ?? 0);
                }

                return hash;
            }
        }

        private static WorkCellVisualState BuildCell(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            ushort columnIndex,
            int bestPawnId,
            uint revision)
        {
            int priority;
            bool incapable;
            bool ageDisabled = pawn.IsWorkTypeDisabledByAge(workType, out _);
            bool disabled = pawn.WorkTypeIsDisabled(workType);
            bool overrideRing;
            if (workGiver != null)
            {
                WorkGiverCellPresentationCache.CellPresentation presentation =
                    WorkGiverCellPresentationCache.Resolve(workGiver.Worker, workType, pawn);
                priority = presentation.EffectivePriority;
                incapable = presentation.Incapable;
                ageDisabled = presentation.DisabledByAge;
                disabled = presentation.WorkTypeDisabled;
                overrideRing = presentation.HasPawnOverride;
            }
            else
            {
                priority = WorkTabEffectiveStateRuntime.GetParentPriority(
                    pawn,
                    workType,
                    WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType));
                incapable = IsIncapable(pawn, workType);
                overrideRing = WorkGiverReassignmentManager.LockedSubWorkOverridesDisabledParent() &&
                               !disabled &&
                               priority <= WorkPrioritySystem.DisabledPriority &&
                               WorkGiverReassignmentManager.HasEnabledPawnOverrideForWorkType(pawn, workType);
            }

            float skill = Mathf.Clamp(pawn.skills.AverageOfRelevantSkillsFor(workType), 0f, 20f);
            byte skillBand;
            float skillBlend;
            if (skill < 4f)
            {
                skillBand = 0;
                skillBlend = skill / 4f;
            }
            else if (skill <= 14f)
            {
                skillBand = 1;
                skillBlend = (skill - 4f) / 10f;
            }
            else
            {
                skillBand = 2;
                skillBlend = (skill - 14f) / 6f;
            }
            int passion = (int)pawn.skills.MaxPassionOfRelevantSkillsFor(workType);
            WorkCellVisualFlags flags = WorkCellVisualFlags.None;
            if (disabled) flags |= WorkCellVisualFlags.Disabled;
            if (incapable) flags |= WorkCellVisualFlags.Incapable;
            if (ageDisabled) flags |= WorkCellVisualFlags.AgeDisabled;
            if (overrideRing) flags |= WorkCellVisualFlags.OverrideRing;
            if (passion > 0) flags |= WorkCellVisualFlags.HasPassion;
            if (WorkTabEffectiveStateRuntime.IsManualMode(
                    pawn,
                    workType,
                    Find.PlaySettings?.useWorkPriorities ?? true))
                flags |= WorkCellVisualFlags.ManualPriorityMode;
            if (pawn.thingIDNumber == bestPawnId) flags |= WorkCellVisualFlags.BestPawn;
            if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
                flags |= WorkCellVisualFlags.IdeologyWarning;
            if (workType.relevantSkills != null && workType.relevantSkills.Count > 0 && skill <= 2f && priority > 0)
                flags |= WorkCellVisualFlags.LowSkillWarning;

            return new WorkCellVisualState(
                pawn,
                workType,
                pawn.thingIDNumber,
                columnIndex,
                (byte)Mathf.Clamp(priority, 0, byte.MaxValue),
                skillBand,
                skillBlend,
                (byte)Mathf.Clamp(passion, 0, byte.MaxValue),
                PackColor(WorkPrioritySystem.GetPriorityColor(priority)),
                flags,
                revision);
        }

        /// <summary>
        /// Whether the pawn's body rules out every giver in this work type, as
        /// vanilla's PawnColumnWorker_WorkPriority.IsIncapableOfWholeWorkType
        /// decides it.
        ///
        /// Read from the work type's own giver list, not BWT's reassigned and
        /// ordered one. That list describes how the player has arranged the
        /// columns: a giver moved to another work type leaves it, and only defs
        /// whose Worker instantiates appear in it. Since "no giver I can do" and
        /// "no givers listed" both fall out of this loop as incapable, a short
        /// list marked healthy colonists incapable, and vanilla's own renderer
        /// then tinted those cells red over the skill band.
        /// </summary>
        private static bool IsIncapable(Pawn pawn, WorkTypeDef workType)
        {
            List<WorkGiverDef> workGivers = workType?.workGiversByPriority;
            for (int i = 0; workGivers != null && i < workGivers.Count; i++)
            {
                bool capable = true;
                var capacities = workGivers[i]?.requiredCapacities;
                for (int j = 0; capacities != null && j < capacities.Count; j++)
                {
                    if (!pawn.health.capacities.CapableOf(capacities[j]))
                    {
                        capable = false;
                        break;
                    }
                }
                if (capable) return false;
            }
            return true;
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
                   !IsIncapable(pawn, workType);
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

            int candidatePriority = WorkTabEffectiveStateRuntime.GetParentPriority(
                candidate,
                workType,
                WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(candidate, workType));
            int bestPriority = WorkTabEffectiveStateRuntime.GetParentPriority(
                bestPawn,
                workType,
                WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(bestPawn, workType));
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
