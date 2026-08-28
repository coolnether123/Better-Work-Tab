using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    /// <summary>
    /// BWT-owned transient columns for the right-expanding specific-job presentation.
    /// These columns never depend on the optional Fluffy Work Tab assembly.
    /// </summary>
    internal static class BwtExpandBesideColumns
    {
        private static bool _columnsDisabled;
        private static readonly Dictionary<string, PawnColumnDef> WorkTypeColumns =
            new Dictionary<string, PawnColumnDef>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PawnColumnDef> WorkGiverColumns =
            new Dictionary<string, PawnColumnDef>(StringComparer.Ordinal);
        private static readonly Dictionary<PawnColumnDef, WorkGiverDef> WorkGivers =
            new Dictionary<PawnColumnDef, WorkGiverDef>();
        // Header passes classify every visible column, so keep native ownership as a
        // direct identity lookup instead of scanning the two definition dictionaries.
        private static readonly HashSet<PawnColumnDef> NativeColumns =
            new HashSet<PawnColumnDef>();

        internal static bool CanBuild => !_columnsDisabled;

        internal static bool IsNativeColumn(PawnColumnDef column)
        {
            return column != null && NativeColumns.Contains(column);
        }

        internal static bool IsNativeChildColumn(PawnColumnDef column)
        {
            return column != null && WorkGivers.ContainsKey(column);
        }

        internal static bool TryBuildColumnSpecs(
            PawnColumnDef sourceWorkColumn,
            WorkTypeDef workType,
            out PawnColumnDef workTypeColumn,
            out List<PawnColumnDef> workGiverColumns)
        {
            workTypeColumn = null;
            workGiverColumns = null;
            if (!CanBuild || workType == null)
            {
                return false;
            }

            workTypeColumn = GetOrCreateWorkTypeColumn(sourceWorkColumn, workType);
            if (workTypeColumn == null)
            {
                return false;
            }

            workGiverColumns = new List<PawnColumnDef>();
            IReadOnlyList<WorkGiver> workGivers =
                WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType);
            for (int i = 0; workGivers != null && i < workGivers.Count; i++)
            {
                PawnColumnDef child = GetOrCreateWorkGiverColumn(workGivers[i]?.def);
                if (child != null)
                {
                    workGiverColumns.Add(child);
                }
            }

            return workGiverColumns.Count > 0;
        }

        internal static WorkGiverDef TryGetWorkGiver(PawnColumnDef column)
        {
            return column != null && WorkGivers.TryGetValue(column, out WorkGiverDef workGiver)
                ? workGiver
                : null;
        }

        internal static float GetColumnWidth(PawnColumnDef column, PawnTable table, float fallback)
        {
            if (!IsNativeColumn(column))
            {
                return fallback;
            }

            try
            {
                return Mathf.Max(1f, column.Worker.GetMinWidth(table));
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog(
                    "[BWT] Failed to query expand-beside column width for " + column.defName + ": " + ex.Message,
                    DebugFeature.SubWork);
                return fallback;
            }
        }

        internal static Rect GetHeaderLaneRect(PawnColumnDef column, PawnTable table, Rect headerRect)
        {
            if (!IsNativeColumn(column) || table == null || headerRect.height <= 1f)
            {
                return headerRect;
            }

            if (IsNativeChildColumn(column))
            {
                return headerRect;
            }

            try
            {
                float laneHeight = Mathf.Clamp(column.Worker.GetMinHeaderHeight(table), 1f, headerRect.height);
                return new Rect(headerRect.x, headerRect.yMax - laneHeight, headerRect.width, laneHeight);
            }
            catch (Exception ex)
            {
                BetterWorkTabMod.DebugLog(
                    "[BWT] Failed to query expand-beside header height for " + column.defName + ": " + ex.Message,
                    DebugFeature.SubWork);
                return headerRect;
            }
        }

        private static PawnColumnDef GetOrCreateWorkTypeColumn(
            PawnColumnDef sourceWorkColumn,
            WorkTypeDef workType)
        {
            string key = workType.defName ?? string.Empty;
            if (WorkTypeColumns.TryGetValue(key, out PawnColumnDef existing))
            {
                return existing;
            }

            try
            {
                var column = new PawnColumnDef
                {
                    defName = "BWT_ExpandBeside_WorkType_" + key,
                    workerClass = typeof(PawnColumnWorker_BwtSubWorkPriority),
                    workType = workType,
                    sortable = sourceWorkColumn?.sortable ?? true,
                    moveWorkTypeLabelDown = sourceWorkColumn?.moveWorkTypeLabelDown ?? false
                };
                column.PostLoad();
                WorkTypeColumns[key] = column;
                NativeColumns.Add(column);
                return column;
            }
            catch (Exception ex)
            {
                DisableColumns("creating expand-beside work-type column", ex);
                return null;
            }
        }

        private static PawnColumnDef GetOrCreateWorkGiverColumn(WorkGiverDef workGiver)
        {
            if (workGiver?.defName == null || workGiver.workType == null || !CanBuild)
            {
                return null;
            }

            string key = workGiver.defName;
            if (WorkGiverColumns.TryGetValue(key, out PawnColumnDef existing))
            {
                return existing;
            }

            try
            {
                var column = new PawnColumnDef
                {
                    defName = "BWT_ExpandBeside_WorkGiver_" + key,
                    workerClass = typeof(PawnColumnWorker_BwtSubWorkPriority),
                    description = workGiver.description,
                    workType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver) ?? workGiver.workType,
                    sortable = true
                };
                column.PostLoad();
                WorkGivers[column] = workGiver;
                WorkGiverColumns[key] = column;
                NativeColumns.Add(column);
                return column;
            }
            catch (Exception ex)
            {
                DisableColumns("creating expand-beside work-giver column", ex);
                return null;
            }
        }

        private static void DisableColumns(string operation, Exception ex)
        {
            if (_columnsDisabled)
            {
                return;
            }

            _columnsDisabled = true;
            SubWorkDrilldownState.CollapseAllExpandBeside();
            string detail = ex == null ? string.Empty : ": " + ex.Message;
            BetterWorkTabMod.DebugLog(
                "[BWT] Disabling native expand-beside columns after " + operation + detail,
                DebugFeature.SubWork);
        }
    }
}
