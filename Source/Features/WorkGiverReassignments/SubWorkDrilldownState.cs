using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    /// <summary>
    /// Local view state for showing one work type's WorkGivers in the main Work tab grid.
    /// The real PawnColumnDefs stay in place; visible work columns become stable slots.
    /// </summary>
    internal static class SubWorkDrilldownState
    {
        private const float TransitionSeconds = 0.18f;
        internal const float GlobalRowHeight = 30f;
        internal const float GlobalPriorityBoxSize = 25f;

        private static readonly List<WorkGiver> ActiveWorkGiversBuffer = new List<WorkGiver>();
        private static readonly Dictionary<PawnColumnDef, int> VisibleColumnSlots = new Dictionary<PawnColumnDef, int>();
        private static readonly Dictionary<WorkTypeDef, int> VisibleWorkTypeSlots = new Dictionary<WorkTypeDef, int>();
        private static readonly HashSet<WorkGiverDef> MovedFromBaseline = new HashSet<WorkGiverDef>();
        private static WorkTypeDef _activeWorkType;
        private static string _cachedWorkTypeDefName;
        private static int _cachedSyncVersion = -1;
        private static int _cachedSlotSignature = int.MinValue;
        private static float _enteredAt;
        private static float _baseHeaderDrawWidth;
        private static bool _layoutRefreshPending;
        private static Vector2? _returnMousePosition;

        internal static bool IsActive => _activeWorkType != null;

        internal static WorkTypeDef ActiveWorkType => _activeWorkType;

        internal static float BaseHeaderDrawWidth => _baseHeaderDrawWidth;

        internal static int LayoutSignature
        {
            get
            {
                EnsureSlotCache();
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (_activeWorkType?.shortHash ?? 0);
                    hash = hash * 31 + WorkGiverReassignmentManager.CurrentSyncVersion;
                    hash = hash * 31 + _cachedSlotSignature;
                    return hash;
                }
            }
        }

        internal static List<WorkGiver> ActiveWorkGivers
        {
            get
            {
                RefreshIfNeeded();
                return ActiveWorkGiversBuffer;
            }
        }

        internal static float TransitionAlpha
        {
            get
            {
                if (!IsActive)
                {
                    return 0f;
                }

                return Mathf.Clamp01((Time.realtimeSinceStartup - _enteredAt) / TransitionSeconds);
            }
        }

        internal static bool TryGetReturnMousePosition(out Vector2 position)
        {
            if (_returnMousePosition.HasValue)
            {
                position = _returnMousePosition.Value;
                return true;
            }

            position = Vector2.zero;
            return false;
        }

        internal static void Enter(WorkTypeDef workType, Vector2? returnMousePosition = null, float baseHeaderDrawWidth = -1f)
        {
            if (workType == null)
            {
                Exit();
                return;
            }

            _activeWorkType = workType;
            _cachedWorkTypeDefName = null;
            _cachedSyncVersion = -1;
            _enteredAt = Time.realtimeSinceStartup;
            _baseHeaderDrawWidth = baseHeaderDrawWidth > 0f ? baseHeaderDrawWidth : 0f;
            _returnMousePosition = returnMousePosition;
            _layoutRefreshPending = true;
            RefreshIfNeeded();
        }

        internal static void Exit()
        {
            _activeWorkType = null;
            _cachedWorkTypeDefName = null;
            _cachedSyncVersion = -1;
            _baseHeaderDrawWidth = 0f;
            _returnMousePosition = null;
            ActiveWorkGiversBuffer.Clear();
            MovedFromBaseline.Clear();
            _layoutRefreshPending = true;
        }

        internal static bool ConsumeLayoutRefresh()
        {
            bool pending = _layoutRefreshPending;
            _layoutRefreshPending = false;
            return pending;
        }

        internal static bool TryGetWorkGiverForColumn(PawnColumnDef column, out WorkGiver workGiver, out int slotIndex)
        {
            workGiver = null;
            slotIndex = GetVisibleWorkColumnSlot(column);
            if (!IsActive || slotIndex < 0)
            {
                return false;
            }

            RefreshIfNeeded();
            if (slotIndex >= ActiveWorkGiversBuffer.Count)
            {
                return false;
            }

            workGiver = ActiveWorkGiversBuffer[slotIndex];
            return workGiver?.def != null;
        }

        internal static bool TryGetWorkGiverForWorkTypeSlot(WorkTypeDef slotWorkType, out WorkGiver workGiver, out int slotIndex)
        {
            workGiver = null;
            slotIndex = -1;
            if (!IsActive || slotWorkType == null)
            {
                return false;
            }

            var tableDef = PawnTableDefOf.Work;
            if (tableDef?.columns == null)
            {
                return false;
            }

            for (int i = 0; i < tableDef.columns.Count; i++)
            {
                var column = tableDef.columns[i];
                if (column?.workType == slotWorkType && column.Worker is PawnColumnWorker_WorkPriority)
                {
                    return TryGetWorkGiverForColumn(column, out workGiver, out slotIndex);
                }
            }

            return false;
        }

        internal static bool IsWorkGiverMovedFromBaseline(WorkGiverDef workGiverDef)
        {
            if (!IsActive || workGiverDef == null)
            {
                return false;
            }

            RefreshIfNeeded();
            return MovedFromBaseline.Contains(workGiverDef);
        }

        internal static bool IsBlankWorkColumn(PawnColumnDef column)
        {
            return IsActive && GetVisibleWorkColumnSlot(column) >= 0 && !TryGetWorkGiverForColumn(column, out _, out _);
        }

        internal static int GetVisibleWorkColumnSlot(PawnColumnDef column)
        {
            if (column == null || !(column.Worker is PawnColumnWorker_WorkPriority) || column.workType == null)
            {
                return -1;
            }

            EnsureSlotCache();
            if (VisibleColumnSlots.TryGetValue(column, out int slot))
            {
                return slot;
            }

            return VisibleWorkTypeSlots.TryGetValue(column.workType, out slot) ? slot : -1;
        }

        internal static int ComparePawnsForColumn(PawnColumnDef column, Pawn a, Pawn b)
        {
            if (!TryGetWorkGiverForColumn(column, out var workGiver, out _))
            {
                return 0;
            }

            float valueA = GetPrioritySortValue(a, workGiver.def);
            float valueB = GetPrioritySortValue(b, workGiver.def);
            return valueA.CompareTo(valueB);
        }

        private static float GetPrioritySortValue(Pawn pawn, WorkGiverDef workGiverDef)
        {
            if (pawn?.workSettings == null || !pawn.workSettings.EverWork || workGiverDef == null)
            {
                return -2f;
            }

            if (_activeWorkType != null && pawn.WorkTypeIsDisabled(_activeWorkType))
            {
                return -1f;
            }

            int defaultPriority = Better_Work_Tab.Features.RaisedPriorityMaximum.WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, _activeWorkType);
            int priority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiverDef, defaultPriority);
            if (priority <= Better_Work_Tab.Features.RaisedPriorityMaximum.WorkPrioritySystem.DisabledPriority)
            {
                return -1f;
            }

            return Better_Work_Tab.Features.RaisedPriorityMaximum.WorkPrioritySystem.GetMaxPriority() + 1 - priority;
        }

        private static void RefreshIfNeeded()
        {
            if (_activeWorkType == null)
            {
                ActiveWorkGiversBuffer.Clear();
                return;
            }

            int syncVersion = WorkGiverReassignmentManager.CurrentSyncVersion;
            if (_cachedWorkTypeDefName == _activeWorkType.defName && _cachedSyncVersion == syncVersion)
            {
                return;
            }

            ActiveWorkGiversBuffer.Clear();
            ActiveWorkGiversBuffer.AddRange(WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(_activeWorkType));
            RebuildMovedBaselineCache();
            _cachedWorkTypeDefName = _activeWorkType.defName;
            _cachedSyncVersion = syncVersion;
        }

        private static void EnsureSlotCache()
        {
            int signature = ComputeSlotSignature();
            if (signature == _cachedSlotSignature)
            {
                return;
            }

            VisibleColumnSlots.Clear();
            VisibleWorkTypeSlots.Clear();

            var columns = PawnTableDefOf.Work?.columns;
            if (columns != null)
            {
                int slot = 0;
                var hidden = BetterWorkTabMod.Settings?.hiddenWorktypes;
                for (int i = 0; i < columns.Count; i++)
                {
                    var candidate = columns[i];
                    if (candidate?.workType == null || !(candidate.Worker is PawnColumnWorker_WorkPriority))
                    {
                        continue;
                    }

                    if (hidden != null && hidden.Contains(candidate.workType.defName))
                    {
                        continue;
                    }

                    VisibleColumnSlots[candidate] = slot;
                    VisibleWorkTypeSlots[candidate.workType] = slot;
                    slot++;
                }
            }

            _cachedSlotSignature = signature;
        }

        private static int ComputeSlotSignature()
        {
            unchecked
            {
                int hash = 17;
                var columns = PawnTableDefOf.Work?.columns;
                if (columns != null)
                {
                    hash = hash * 31 + columns.Count;
                    for (int i = 0; i < columns.Count; i++)
                    {
                        var column = columns[i];
                        if (column?.workType == null || !(column.Worker is PawnColumnWorker_WorkPriority))
                        {
                            continue;
                        }

                        hash = hash * 31 + column.shortHash;
                        hash = hash * 31 + column.workType.shortHash;
                    }
                }

                var hidden = BetterWorkTabMod.Settings?.hiddenWorktypes;
                if (hidden != null)
                {
                    hash = hash * 31 + hidden.Count;
                    for (int i = 0; i < hidden.Count; i++)
                    {
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(hidden[i] ?? string.Empty);
                    }
                }

                return hash;
            }
        }

        private static void RebuildMovedBaselineCache()
        {
            MovedFromBaseline.Clear();

            if (_activeWorkType == null)
            {
                return;
            }

            for (int i = 0; i < ActiveWorkGiversBuffer.Count; i++)
            {
                var def = ActiveWorkGiversBuffer[i]?.def;
                if (def != null && WorkGiverReassignmentManager.ShouldShowMovedWorkGiverMarker(_activeWorkType, def))
                {
                    MovedFromBaseline.Add(def);
                }
            }
        }
    }
}
