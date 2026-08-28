using System;
using System.Collections.Generic;
using Better_Work_Tab.PawnOrganizer;
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

        internal static bool CanBuild => !_columnsDisabled;

        internal static bool IsNativeColumn(PawnColumnDef column)
        {
            return column != null &&
                (WorkTypeColumns.ContainsValue(column) || WorkGiverColumns.ContainsValue(column));
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

        internal static bool TryBuildPreviewColumns(
            PawnColumnDef sourceWorkColumn,
            WorkTypeDef workType,
            float startX,
            float parentWidth,
            float childWidth,
            out List<WorkTabLayoutColumn> columns)
        {
            columns = null;
            if (!TryBuildColumnSpecs(
                    sourceWorkColumn,
                    workType,
                    out PawnColumnDef parentColumn,
                    out List<PawnColumnDef> childColumns))
            {
                return false;
            }

            columns = new List<WorkTabLayoutColumn>();
            float x = startX;
            columns.Add(new WorkTabLayoutColumn(
                parentColumn,
                new Rect(x, 0f, parentWidth, 1f),
                x,
                parentWidth));
            x += parentWidth;

            for (int i = 0; i < childColumns.Count; i++)
            {
                WorkGiverDef workGiver = TryGetWorkGiver(childColumns[i]);
                if (workGiver == null)
                {
                    continue;
                }

                columns.Add(new WorkTabLayoutColumn(
                    childColumns[i],
                    new Rect(x, 0f, childWidth, 1f),
                    x,
                    childWidth,
                    workType,
                    workGiver,
                    i,
                    isExpandBesideChild: true));
                x += childWidth;
            }

            return columns.Count > 1;
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
                WorkGivers[column] = workGiver;
                column.PostLoad();
                WorkGiverColumns[key] = column;
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
