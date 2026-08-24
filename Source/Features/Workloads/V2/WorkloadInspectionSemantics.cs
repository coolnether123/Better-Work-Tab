using System;
using System.Collections.Generic;

namespace Better_Work_Tab.Features.Workloads.V2
{
    [Flags]
    internal enum WorkloadInspectionCellKind
    {
        None = 0,
        ParentPriority = 1,
        SpecificPriority = 2,
        Schedule = 4,
        Ordering = 8
    }

    /// <summary>
    /// Stable semantic-to-layout lookup for inspection rendering. The layout
    /// owns the column indices; this type only indexes canonical defName keys
    /// and deliberately preserves duplicate projected columns.
    /// </summary>
    internal sealed class WorkloadInspectionColumnIndex
    {
        private static readonly IReadOnlyList<int> EmptyColumns = new int[0];
        private readonly Dictionary<string, List<int>> _parentColumns =
            new Dictionary<string, List<int>>(StringComparer.Ordinal);
        private readonly Dictionary<WorkloadInspectionSpecificColumnKey, List<int>> _specificColumns =
            new Dictionary<WorkloadInspectionSpecificColumnKey, List<int>>();
        private readonly Dictionary<string, List<int>> _orderingColumns =
            new Dictionary<string, List<int>>(StringComparer.Ordinal);

        public void Clear()
        {
            _parentColumns.Clear();
            _specificColumns.Clear();
            _orderingColumns.Clear();
        }

        public void Add(
            int columnIndex,
            string workTypeDefName,
            string workGiverDefName,
            bool isPriorityCell)
        {
            if (!isPriorityCell || string.IsNullOrEmpty(workTypeDefName))
            {
                return;
            }

            AddValue(_orderingColumns, workTypeDefName, columnIndex);
            if (string.IsNullOrEmpty(workGiverDefName))
            {
                AddValue(_parentColumns, workTypeDefName, columnIndex);
            }
            else
            {
                AddValue(
                    _specificColumns,
                    new WorkloadInspectionSpecificColumnKey(
                        workTypeDefName,
                        workGiverDefName),
                    columnIndex);
            }
        }

        public IReadOnlyList<int> GetParentColumns(string workTypeDefName)
        {
            return GetValues(_parentColumns, workTypeDefName);
        }

        public IReadOnlyList<int> GetSpecificColumns(
            string workTypeDefName,
            string workGiverDefName)
        {
            if (string.IsNullOrEmpty(workTypeDefName) ||
                string.IsNullOrEmpty(workGiverDefName))
            {
                return EmptyColumns;
            }

            return GetValues(
                _specificColumns,
                new WorkloadInspectionSpecificColumnKey(
                    workTypeDefName,
                    workGiverDefName));
        }

        public IReadOnlyList<int> GetOrderingColumns(string workTypeDefName)
        {
            return GetValues(_orderingColumns, workTypeDefName);
        }

        private static IReadOnlyList<int> GetValues(
            Dictionary<string, List<int>> values,
            string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return EmptyColumns;
            }

            List<int> result;
            return values.TryGetValue(key, out result) ? result : EmptyColumns;
        }

        private static void AddValue(
            Dictionary<string, List<int>> values,
            string key,
            int columnIndex)
        {
            List<int> indices;
            if (!values.TryGetValue(key, out indices))
            {
                indices = new List<int>(1);
                values.Add(key, indices);
            }

            indices.Add(columnIndex);
        }

        private static IReadOnlyList<int> GetValues(
            Dictionary<WorkloadInspectionSpecificColumnKey, List<int>> values,
            WorkloadInspectionSpecificColumnKey key)
        {
            List<int> result;
            return values.TryGetValue(key, out result) ? result : EmptyColumns;
        }

        private static void AddValue(
            Dictionary<WorkloadInspectionSpecificColumnKey, List<int>> values,
            WorkloadInspectionSpecificColumnKey key,
            int columnIndex)
        {
            List<int> indices;
            if (!values.TryGetValue(key, out indices))
            {
                indices = new List<int>(1);
                values.Add(key, indices);
            }

            indices.Add(columnIndex);
        }
    }

    internal struct WorkloadInspectionSpecificColumnKey :
        IEquatable<WorkloadInspectionSpecificColumnKey>
    {
        internal WorkloadInspectionSpecificColumnKey(
            string workTypeDefName,
            string workGiverDefName)
        {
            WorkTypeDefName = workTypeDefName ?? string.Empty;
            WorkGiverDefName = workGiverDefName ?? string.Empty;
        }

        private string WorkTypeDefName { get; }
        private string WorkGiverDefName { get; }

        public bool Equals(WorkloadInspectionSpecificColumnKey other)
        {
            return StringComparer.Ordinal.Equals(
                    WorkTypeDefName,
                    other.WorkTypeDefName) &&
                StringComparer.Ordinal.Equals(
                    WorkGiverDefName,
                    other.WorkGiverDefName);
        }

        public override bool Equals(object obj)
        {
            return obj is WorkloadInspectionSpecificColumnKey &&
                Equals((WorkloadInspectionSpecificColumnKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(WorkTypeDefName) * 397) ^
                    StringComparer.Ordinal.GetHashCode(WorkGiverDefName);
            }
        }
    }

    internal struct WorkloadInspectionCellKey : IEquatable<WorkloadInspectionCellKey>
    {
        public WorkloadInspectionCellKey(int rowIndex, int columnIndex)
        {
            RowIndex = rowIndex;
            ColumnIndex = columnIndex;
        }

        public int RowIndex { get; private set; }
        public int ColumnIndex { get; private set; }

        public bool Equals(WorkloadInspectionCellKey other)
        {
            return RowIndex == other.RowIndex && ColumnIndex == other.ColumnIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is WorkloadInspectionCellKey &&
                Equals((WorkloadInspectionCellKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (RowIndex * 397) ^ ColumnIndex;
            }
        }
    }

    /// <summary>
    /// Aggregates semantic flags before drawing. Multiple semantic changes may
    /// intentionally address the same visible cell and must be composed once.
    /// </summary>
    internal sealed class WorkloadInspectionCellMasks
    {
        private readonly Dictionary<WorkloadInspectionCellKey, WorkloadInspectionCellKind> _values =
            new Dictionary<WorkloadInspectionCellKey, WorkloadInspectionCellKind>();

        public int Count => _values.Count;
        internal Dictionary<WorkloadInspectionCellKey, WorkloadInspectionCellKind> Entries =>
            _values;

        public void Clear()
        {
            _values.Clear();
        }

        public void Add(int rowIndex, int columnIndex, WorkloadInspectionCellKind kind)
        {
            if (kind == WorkloadInspectionCellKind.None)
            {
                return;
            }

            WorkloadInspectionCellKey key =
                new WorkloadInspectionCellKey(rowIndex, columnIndex);
            WorkloadInspectionCellKind existing;
            _values.TryGetValue(key, out existing);
            _values[key] = existing | kind;
        }

        public bool TryGet(
            int rowIndex,
            int columnIndex,
            out WorkloadInspectionCellKind kind)
        {
            return _values.TryGetValue(
                new WorkloadInspectionCellKey(rowIndex, columnIndex),
                out kind);
        }
    }

    internal static class WorkloadInspectionSemantics
    {
        /// <summary>
        /// Manual mode is globally effective even though the compatibility
        /// storage contains one entry per represented pawn/work type. Missing
        /// keys caused only by membership churn do not constitute a global
        /// mode change.
        /// </summary>
        public static bool HasEffectiveManualModeChange(
            WorkloadProjectedState before,
            WorkloadProjectedState after)
        {
            bool beforeMode;
            bool afterMode;
            if (!WorkloadManualModeSemantics.TryGetGlobalMode(before, out beforeMode, out _, out _) ||
                !WorkloadManualModeSemantics.TryGetGlobalMode(after, out afterMode, out _, out _))
            {
                return false;
            }

            return beforeMode != afterMode;
        }
    }
}
