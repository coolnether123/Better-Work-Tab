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

        private static readonly List<WorkGiver> ActiveWorkGiversBuffer = new List<WorkGiver>();
        private static WorkTypeDef _activeWorkType;
        private static string _cachedWorkTypeDefName;
        private static int _cachedSyncVersion = -1;
        private static float _enteredAt;
        private static bool _layoutRefreshPending;
        private static Vector2? _returnMousePosition;

        internal static bool IsActive => _activeWorkType != null;

        internal static WorkTypeDef ActiveWorkType => _activeWorkType;

        internal static IReadOnlyList<WorkGiver> ActiveWorkGivers
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

        internal static void Enter(WorkTypeDef workType, Vector2? returnMousePosition = null)
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
            _returnMousePosition = returnMousePosition;
            _layoutRefreshPending = true;
            RefreshIfNeeded();
        }

        internal static void Exit()
        {
            _activeWorkType = null;
            _cachedWorkTypeDefName = null;
            _cachedSyncVersion = -1;
            _returnMousePosition = null;
            ActiveWorkGiversBuffer.Clear();
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
            if (!IsActive || workGiverDef == null || _activeWorkType == null)
            {
                return false;
            }

            RefreshIfNeeded();

            int currentIndex = -1;
            for (int i = 0; i < ActiveWorkGiversBuffer.Count; i++)
            {
                if (ActiveWorkGiversBuffer[i]?.def == workGiverDef)
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                return false;
            }

            var baseline = new List<WorkGiverDef>();
            var allDefs = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                var def = allDefs[i];
                if (WorkGiverReassignmentManager.GetTargetWorkType(def) == _activeWorkType)
                {
                    baseline.Add(def);
                }
            }

            baseline.Sort((a, b) => b.priorityInType.CompareTo(a.priorityInType));
            for (int i = 0; i < baseline.Count; i++)
            {
                if (baseline[i] == workGiverDef)
                {
                    return i != currentIndex;
                }
            }

            return false;
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

            var tableDef = PawnTableDefOf.Work;
            var columns = tableDef?.columns;
            if (columns == null)
            {
                return -1;
            }

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

                if (ReferenceEquals(candidate, column) || candidate.workType == column.workType)
                {
                    return slot;
                }

                slot++;
            }

            return -1;
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
            _cachedWorkTypeDefName = _activeWorkType.defName;
            _cachedSyncVersion = syncVersion;
        }
    }
}
