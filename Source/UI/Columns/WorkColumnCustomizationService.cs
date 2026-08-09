using Better_Work_Tab.Features;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Columns
{
    /// <summary>
    /// Owns persisted player column customization state and moved-column presentation state.
    /// </summary>
    internal static class WorkColumnCustomizationService
    {
        private static readonly Dictionary<WorkTypeDef, bool> ColumnMarkerCache =
            new Dictionary<WorkTypeDef, bool>();
        private static BetterWorkTabSettings _columnMarkerCacheSettings;
        private static Game _columnMarkerCacheGame;
        private static int _columnMarkerCacheColumnsRevision = -1;
        private static int _columnMarkerCacheDraggedCount = -1;
        private static bool _columnMarkerCacheEnabled;
        private static int _columnMarkerCacheValidationFrame = -1;
        private static int _draggedColumnsSyncSignature = int.MinValue;

        /// <summary>
        /// Synchronizes the player-dragged columns list with the current column order.
        /// Removes any columns from the dragged list that are now back in their baseline position.
        /// This handles the case where saved settings had dragged columns but they've since been reset.
        /// </summary>
        internal static void SyncDraggedColumnsWithCurrentOrder()
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings?.playerDraggedColumns == null)
                return;

            var baselineOrder = WorkColumnOrderManager.GetBaselineOrder();
            if (baselineOrder == null || baselineOrder.Count == 0)
                return;

            var def = PawnTableDefOf.Work;
            if (def?.columns == null)
                return;

            int inputSignature = ComputeDraggedColumnsSyncSignature(
                def.columns,
                baselineOrder,
                settings.playerDraggedColumns);
            if (_draggedColumnsSyncSignature == inputSignature)
            {
                return;
            }

            var currentOrder = def.columns
                .Where(c => c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                .Select(c => c.workType.defName)
                .ToList();

            var filteredBaseline = baselineOrder.Where(b => currentOrder.Contains(b)).ToList();

            // Remove any dragged columns that are now back in vanilla RELATIVE position
            var toRemove = new List<string>();
            foreach (var defName in settings.playerDraggedColumns)
            {
                int relVanillaPos = filteredBaseline.IndexOf(defName);
                int currentPos = currentOrder.IndexOf(defName);

                // If the column is back in its relative baseline spot, unmark it
                if (relVanillaPos >= 0 && relVanillaPos == currentPos)
                {
                    toRemove.Add(defName);
                }
            }

            // Perform removals
            foreach (var defName in toRemove)
            {
                settings.playerDraggedColumns.Remove(defName);
            }

            // HEAL PHASE: If there is a custom order but NO columns are marked as dragged,
            // then the intent data is lost. Use the Longest Increasing Subsequence (LIS)
            // approach to find the minimum number of 'moves' to explain the current table.
            if (settings.playerDraggedColumns.Count == 0)
            {
                var currentIndices = currentOrder.Select(c => filteredBaseline.IndexOf(c)).ToList();

                // Simple LIS (Patient Sorting style)
                var tails = new List<int>();
                var prev = new int[currentIndices.Count];
                var tailIdx = new List<int>();

                for (int i = 0; i < currentIndices.Count; i++) {
                    int val = currentIndices[i];
                    int pos = tails.BinarySearch(val);
                    if (pos < 0) pos = ~pos;

                    if (pos < tails.Count) {
                        tails[pos] = val;
                        tailIdx[pos] = i;
                    } else {
                        tails.Add(val);
                        tailIdx.Add(i);
                    }
                    prev[i] = (pos > 0) ? tailIdx[pos-1] : -1;
                }

                // Reconstruct LIS indices
                var lisIndices = new HashSet<int>();
                if (tailIdx.Count > 0) {
                    int curr = tailIdx.Last();
                    while (curr != -1) {
                        lisIndices.Add(curr);
                        curr = prev[curr];
                    }
                }

                // Mark elements NOT in LIS as moved
                for (int i = 0; i < currentOrder.Count; i++) {
                    if (!lisIndices.Contains(i)) {
                        string defName = currentOrder[i];
                        settings.playerDraggedColumns.Add(defName);
                        toRemove.Add(defName); // Trigger save
                    }
                }
            }

            if (toRemove.Count > 0)
            {
                settings.Write();
            }

            _draggedColumnsSyncSignature = ComputeDraggedColumnsSyncSignature(
                def.columns,
                baselineOrder,
                settings.playerDraggedColumns);
        }

        private static int ComputeDraggedColumnsSyncSignature(
            IReadOnlyList<PawnColumnDef> columns,
            IReadOnlyList<string> baselineOrder,
            IEnumerable<string> draggedColumns)
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (columns?.Count ?? 0);
                if (columns != null)
                {
                    for (int i = 0; i < columns.Count; i++)
                    {
                        PawnColumnDef column = columns[i];
                        if (column?.Worker is PawnColumnWorker_WorkPriority && column.workType != null)
                        {
                            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(column.workType.defName ?? string.Empty);
                        }
                    }
                }

                hash = (hash * 31) + (baselineOrder?.Count ?? 0);
                if (baselineOrder != null)
                {
                    for (int i = 0; i < baselineOrder.Count; i++)
                    {
                        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(baselineOrder[i] ?? string.Empty);
                    }
                }

                if (draggedColumns != null)
                {
                    int draggedCount = 0;
                    int draggedSum = 0;
                    int draggedXor = 0;
                    foreach (string defName in draggedColumns)
                    {
                        int nameHash = StringComparer.Ordinal.GetHashCode(defName ?? string.Empty);
                        draggedCount++;
                        draggedSum += nameHash;
                        draggedXor ^= nameHash;
                    }

                    hash = (hash * 31) + draggedCount;
                    hash = (hash * 31) + draggedSum;
                    hash = (hash * 31) + draggedXor;
                }

                return hash;
            }
        }

        /// <summary>
        /// Checks if a column should show the yellow asterisk marker.
        /// A column is marked only if:
        /// 1. The player directly dragged it (recorded in playerDraggedColumns), AND
        /// 2. It is currently out of its baseline position
        ///
        /// Columns that shifted as a side effect of another drag are NOT marked.
        /// </summary>
        internal static bool ShouldShowColumnMarker(WorkTypeDef workType)
        {
            if (workType?.defName == null)
                return false;

            var settings = BetterWorkTabMod.Settings;
            if (settings == null)
                return false;

            if (!settings.showColumnMovedMarker)
                return false;

            if (SubWorkDrilldownState.IsActive)
            {
                return SubWorkDrilldownState.TryGetWorkGiverForWorkTypeSlot(workType, out var workGiver, out _) &&
                       SubWorkDrilldownState.IsWorkGiverMovedFromBaseline(workGiver.def);
            }

            if (_columnMarkerCacheValidationFrame != Time.frameCount)
            {
                WorkTabInvalidationVersion invalidation = WorkTabInvalidationHub.Current;
                int draggedCount = settings.playerDraggedColumns?.Count ?? 0;
                if (!ReferenceEquals(_columnMarkerCacheSettings, settings) ||
                    !ReferenceEquals(_columnMarkerCacheGame, Current.Game) ||
                    _columnMarkerCacheColumnsRevision != invalidation.Columns ||
                    _columnMarkerCacheDraggedCount != draggedCount ||
                    _columnMarkerCacheEnabled != settings.showColumnMovedMarker)
                {
                    ClearColumnMarkerCache();
                    _columnMarkerCacheSettings = settings;
                    _columnMarkerCacheGame = Current.Game;
                    _columnMarkerCacheColumnsRevision = invalidation.Columns;
                    _columnMarkerCacheDraggedCount = draggedCount;
                    _columnMarkerCacheEnabled = settings.showColumnMovedMarker;
                }
                _columnMarkerCacheValidationFrame = Time.frameCount;
            }

            if (ColumnMarkerCache.TryGetValue(workType, out bool cachedResult))
            {
                return cachedResult;
            }

            // First check: was this column directly dragged by the player?
            if (!settings.WasColumnDraggedByPlayer(workType.defName))
            {
                ColumnMarkerCache[workType] = false;
                return false;
            }

            // Second check: is it currently out of baseline position?
            bool result = IsColumnOutOfBaselinePosition(workType);
            ColumnMarkerCache[workType] = result;
            return result;
        }

        private static void ClearColumnMarkerCache()
        {
            ColumnMarkerCache.Clear();
            _columnMarkerCacheValidationFrame = -1;
        }

        /// <summary>
        /// Checks if a column's current position differs from its baseline position.
        /// This is a pure position check with no marking logic.
        /// </summary>
        private static bool IsColumnInBaselinePosition(WorkTypeDef workType)
        {
            if (workType?.defName == null) return true;

            var baselineOrder = WorkColumnOrderManager.GetBaselineOrder();
            if (baselineOrder?.Count == 0) return true;

            var def = PawnTableDefOf.Work;
            if (def?.columns == null) return true;

            // Compare relative positions without rebuilding three LINQ lists for every header.
            // The baseline remains filtered to work types that are present in the live table,
            // matching the previous behavior when another mod adds or removes a column.
            int currentPos = -1;
            int currentWorkIndex = 0;
            for (int i = 0; i < def.columns.Count; i++)
            {
                PawnColumnDef column = def.columns[i];
                if (!(column?.Worker is PawnColumnWorker_WorkPriority) || column.workType?.defName == null)
                {
                    continue;
                }

                if (column.workType.defName == workType.defName)
                {
                    currentPos = currentWorkIndex;
                    break;
                }

                currentWorkIndex++;
            }

            int relVanillaPos = -1;
            int filteredBaselineIndex = 0;
            for (int baselineIndex = 0; baselineIndex < baselineOrder.Count; baselineIndex++)
            {
                string baselineDefName = baselineOrder[baselineIndex];
                bool present = false;
                for (int columnIndex = 0; columnIndex < def.columns.Count; columnIndex++)
                {
                    PawnColumnDef column = def.columns[columnIndex];
                    if (column?.Worker is PawnColumnWorker_WorkPriority &&
                        column.workType?.defName == baselineDefName)
                    {
                        present = true;
                        break;
                    }
                }

                if (!present)
                {
                    continue;
                }

                if (baselineDefName == workType.defName)
                {
                    relVanillaPos = filteredBaselineIndex;
                    break;
                }

                filteredBaselineIndex++;
            }

            if (relVanillaPos < 0 || currentPos < 0) return true;

            return relVanillaPos == currentPos;
        }

        /// <summary>
        /// Returns true if the column is NOT in its baseline position.
        /// </summary>
        internal static bool IsColumnOutOfBaselinePosition(WorkTypeDef workType)
        {
            return !IsColumnInBaselinePosition(workType);
        }

        /// <summary>
        /// Called after a column drag completes. Records that this specific column was
        /// directly dragged by the player, then updates its marking status based on
        /// whether it ended up out of vanilla position.
        /// </summary>
        [SyncMethod]
        internal static void MarkColumnMoved(WorkTypeDef workType)
        {
            if (workType?.defName == null)
                return;

            var settings = BetterWorkTabMod.Settings;
            if (settings == null)
                return;

            // Record that the player dragged this column
            settings.RecordPlayerDraggedColumn(workType.defName);

            // If the column ended up back in baseline position, remove it from the dragged list
            if (IsColumnInBaselinePosition(workType))
            {
                settings.playerDraggedColumns.Remove(workType.defName);
            }

            settings.Write();
            ClearColumnMarkerCache();
        }

        /// <summary>
        /// Clears all column markers. Called when resetting to vanilla order.
        /// </summary>
        internal static void ClearAllColumnMarkers()
        {
            var settings = BetterWorkTabMod.Settings;
            settings?.ClearPlayerDraggedColumns();
            settings?.Write();
            ClearColumnMarkerCache();
        }
    }
}
