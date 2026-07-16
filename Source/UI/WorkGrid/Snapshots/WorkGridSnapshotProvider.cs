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
using RimWorld;
using Spine.Api;
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
        private IWorkTabLayoutController _layout;
        private int _layoutSignature;
        private bool _hasLayoutSignature;
        private WorkGridRevisionSet _revisions;
        private long _snapshotRevision;

        internal WorkGridSnapshotProvider()
        {
            _active = this;
        }

        internal static void ClearActive()
        {
            _active?.Clear();
        }

        internal WorkGridSnapshot Current => _slot.Current;

        internal WorkGridSnapshot Prepare(
            IWorkTabLayoutController layout,
            PawnTable table,
            WorkTabInvalidationVersion versions)
        {
            if (layout == null || table == null || layout.GeometrySnapshot == null)
            {
                return null;
            }

            WorkGridRevisionSet current = versions.CategoryRevisions;
            if (_slot.Current != null &&
                ReferenceEquals(_layout, layout) &&
                _slot.Current.LayoutRevision == layout.LayoutRevision &&
                EqualConsumedRevisions(_revisions, current))
            {
                return _slot.Current;
            }

            int layoutSignature = ComputeLayoutSignature(layout);
            if (_slot.Current != null &&
                _hasLayoutSignature &&
                _layoutSignature == layoutSignature &&
                EqualConsumedRevisions(_revisions, current))
            {
                return _slot.Current;
            }

            var timer = Stopwatch.StartNew();
            Build(layout, table, current, layoutSignature);
            timer.Stop();
            WorkTabInvalidationHub.ClearConsumedPriorityKeys();
            WorkGridSnapshot snapshot = _slot.Current;
            WorkGridRendererDiagnostics.RecordSnapshotBuild(
                snapshot?.Revision ?? 0,
                timer.ElapsedTicks,
                snapshot?.RetainedCapacityBytes ?? 0,
                versions.PriorityDirtyCount,
                layout.GeometrySnapshot.RetainedCapacityBytes);
            return snapshot;
        }

        internal void Clear()
        {
            _slot.Clear();
            _rows.Clear();
            _columns.Clear();
            _cells.Clear();
            _layout = null;
            _layoutSignature = 0;
            _hasLayoutSignature = false;
            _revisions = default;
            WorkGridRendererDiagnostics.RecordSnapshotCleared();
        }

        private void Build(
            IWorkTabLayoutController layout,
            PawnTable table,
            WorkGridRevisionSet revisions,
            int layoutSignature)
        {
            _rows.Clear();
            _columns.Clear();
            _cells.Clear();

            IList<WorkTabLayoutRow> layoutRows = layout.Rows;
            for (int i = 0; i < layoutRows.Count; i++)
            {
                WorkTabLayoutRow row = layoutRows[i];
                if (row.IsDivider)
                {
                    var divider = row.Divider;
                    Color color = divider?.DividerColor ?? Color.clear;
                    BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                    if (!(settings?.allowCustomDividerColors ?? true))
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

            IList<WorkTabLayoutColumn> layoutColumns = layout.Columns;
            bool canSnapshotVanillaPriorityCells =
                      WorkGridVanillaCompatibilityPolicy.CanSnapshotVanillaPriorityCells();
            var bestPawnIds = new Dictionary<ushort, int>();
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
                    !bestPawnIds.ContainsKey(workType.shortHash))
                {
                    bestPawnIds[workType.shortHash] = FindBestPawnId(table, workType, priorityWorker);
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
                    _cells.Add(BuildCell(
                        pawn,
                        workType,
                        column.SubWorkGiver,
                        (ushort)columnIndex,
                        bestPawnId,
                        cellRevision));
                }
            }

            _layoutSignature = layoutSignature;
            _layout = layout;
            _hasLayoutSignature = true;
            _revisions = revisions;
            _snapshotRevision++;
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
                Find.PlaySettings?.useWorkPriorities ?? true,
                maxPriority,
                Mathf.RoundToInt(Prefs.UIScale * 1000f),
                presentationRevision,
                unchecked((presentationRevision * 397) ^ maxPriority)));
        }

        private static int ComputeLayoutSignature(IWorkTabLayoutController layout)
        {
            unchecked
            {
                int hash = 17;
                IList<WorkTabLayoutRow> rows = layout.Rows;
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

                IList<WorkTabLayoutColumn> columns = layout.Columns;
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
            bool ageDisabled = WorkGiverPriorityBoxCompatibility.IsWorkTypeDisabledByAge(pawn, workType, out _);
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
                priority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
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
            if (pawn.thingIDNumber == bestPawnId) flags |= WorkCellVisualFlags.BestPawn;
#if !v1_2 && !v1_1 && !v1_0 && !v0_19 && !v0_18 && !v0_17 && !v0_16 && !v0_15 && !v0_14 && !v0_13 && !vAlpha4
            if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
                flags |= WorkCellVisualFlags.IdeologyWarning;
#endif
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

        private static bool IsIncapable(Pawn pawn, WorkTypeDef workType)
        {
            var workGivers = WorkGiverReassignmentManager.GetOrderedWorkGiversForWorkType(workType);
            for (int i = 0; i < workGivers.Count; i++)
            {
                bool capable = true;
                var capacities = workGivers[i]?.def?.requiredCapacities;
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
            var cachedPawns = PawnTableCompat.GetCachedPawns(table);
            if (workType == null || worker == null || cachedPawns == null)
            {
                return -1;
            }

            Pawn bestPawn = null;
            for (int i = 0; i < cachedPawns.Count; i++)
            {
                Pawn candidate = cachedPawns[i];
                if (candidate == null || candidate.Dead ||
                    candidate.workSettings == null || !candidate.workSettings.EverWork ||
                    candidate.WorkTypeIsDisabled(workType) || IsIncapable(candidate, workType))
                {
                    continue;
                }

                if (bestPawn == null || worker.Compare(candidate, bestPawn) > 0)
                {
                    bestPawn = candidate;
                }
            }
            return bestPawn?.thingIDNumber ?? -1;
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
    }
}
