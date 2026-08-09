using System.Collections.Generic;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Local-only visual interpolation after a column reorder. The data model updates immediately;
    /// rendered headers and cells glide from their previous x positions to the new layout positions.
    /// </summary>
    internal static class ColumnReorderAnimationState
    {
        private const float DurationSeconds = 0.24f;

        private static readonly Dictionary<string, PositionSnapshot> FromPositions =
            new Dictionary<string, PositionSnapshot>();
        private static float _startedAt;

        internal static bool IsActive => FromPositions.Count > 0 && UseAnimation;

        private static bool UseAnimation =>
            BetterWorkTabMod.Settings?.enableSubWorkTransitionAnimation ??
            DefaultSettings.enableSubWorkTransitionAnimation;

        internal static void Start(IReadOnlyList<WorkTabLayoutColumn> columns)
        {
            FromPositions.Clear();
            if (!UseAnimation || columns == null)
            {
                return;
            }

            for (int i = 0; i < columns.Count; i++)
            {
                WorkTabLayoutColumn column = columns[i];
                string key = GetColumnKey(column);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                FromPositions[key] = new PositionSnapshot(column.HeaderContentRect.x, column.OffsetX);
            }

            _startedAt = Time.realtimeSinceStartup;
        }

        internal static void Clear()
        {
            FromPositions.Clear();
        }

        internal static void GetOffsets(
            WorkTabLayoutColumn column,
            out float headerOffset,
            out float cellOffset)
        {
            Tick();
            headerOffset = GetOffsetAfterTick(column, header: true);
            cellOffset = GetOffsetAfterTick(column, header: false);
        }

        internal static void Tick()
        {
            if (FromPositions.Count == 0)
            {
                return;
            }

            if (!UseAnimation || Time.realtimeSinceStartup - _startedAt >= DurationSeconds)
            {
                FromPositions.Clear();
            }
        }

        private static float GetOffsetAfterTick(WorkTabLayoutColumn column, bool header)
        {
            if (FromPositions.Count == 0)
            {
                return 0f;
            }

            string key = GetColumnKey(column);
            if (string.IsNullOrEmpty(key) || !FromPositions.TryGetValue(key, out var from))
            {
                return 0f;
            }

            float current = header ? column.HeaderContentRect.x : column.OffsetX;
            float previous = header ? from.HeaderX : from.CellOffsetX;
            float rawProgress = Mathf.Clamp01((Time.realtimeSinceStartup - _startedAt) / DurationSeconds);
            float eased = Mathf.SmoothStep(0f, 1f, rawProgress);
            return (previous - current) * (1f - eased);
        }

        private static string GetColumnKey(WorkTabLayoutColumn column)
        {
            if (column.Column == null)
            {
                return null;
            }

            if (SubWorkDrilldownState.TryGetWorkGiverForColumn(
                    column,
                    out WorkGiver workGiver,
                    out WorkTypeDef parentWorkType,
                    out _) &&
                workGiver?.def != null)
            {
                return "sub:" + (parentWorkType?.defName ?? "") + ":" + workGiver.def.defName;
            }

            return "column:" + column.Column.defName;
        }

        private readonly struct PositionSnapshot
        {
            public readonly float HeaderX;
            public readonly float CellOffsetX;

            public PositionSnapshot(float headerX, float cellOffsetX)
            {
                HeaderX = headerX;
                CellOffsetX = cellOffsetX;
            }
        }
    }
}
