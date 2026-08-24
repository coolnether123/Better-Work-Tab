using System;
using System.Collections.Generic;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    [Flags]
    internal enum WorkGridInspectionCellKind
    {
        None = 0,
        ParentPriority = 1,
        SpecificPriority = 2,
        Schedule = 4,
        Ordering = 8
    }

    /// <summary>Indexes finished WorkGrid columns by their semantic targets.</summary>
    internal sealed class WorkGridInspectionColumnIndex
    {
        private static readonly IReadOnlyList<int> EmptyColumns = new int[0];
        private readonly Dictionary<string, List<int>> _parentColumns =
            new Dictionary<string, List<int>>(StringComparer.Ordinal);
        private readonly Dictionary<SpecificColumnKey, List<int>> _specificColumns =
            new Dictionary<SpecificColumnKey, List<int>>();
        private readonly Dictionary<string, List<int>> _orderingColumns =
            new Dictionary<string, List<int>>(StringComparer.Ordinal);

        internal void Clear()
        {
            _parentColumns.Clear();
            _specificColumns.Clear();
            _orderingColumns.Clear();
        }

        internal void Add(
            int columnIndex,
            string workTypeDefName,
            string workGiverDefName,
            bool isPriorityCell)
        {
            if (!isPriorityCell || string.IsNullOrEmpty(workTypeDefName)) return;

            AddValue(_orderingColumns, workTypeDefName, columnIndex);
            if (string.IsNullOrEmpty(workGiverDefName))
            {
                AddValue(_parentColumns, workTypeDefName, columnIndex);
            }
            else
            {
                AddValue(
                    _specificColumns,
                    new SpecificColumnKey(workTypeDefName, workGiverDefName),
                    columnIndex);
            }
        }

        internal IReadOnlyList<int> GetParentColumns(string workTypeDefName) =>
            GetValues(_parentColumns, workTypeDefName);

        internal IReadOnlyList<int> GetSpecificColumns(
            string workTypeDefName,
            string workGiverDefName)
        {
            return string.IsNullOrEmpty(workTypeDefName) ||
                   string.IsNullOrEmpty(workGiverDefName)
                ? EmptyColumns
                : GetValues(
                    _specificColumns,
                    new SpecificColumnKey(workTypeDefName, workGiverDefName));
        }

        internal IReadOnlyList<int> GetOrderingColumns(string workTypeDefName) =>
            GetValues(_orderingColumns, workTypeDefName);

        private static IReadOnlyList<int> GetValues(
            Dictionary<string, List<int>> values,
            string key)
        {
            return !string.IsNullOrEmpty(key) &&
                   values.TryGetValue(key, out List<int> result)
                ? result
                : EmptyColumns;
        }

        private static void AddValue(
            Dictionary<string, List<int>> values,
            string key,
            int columnIndex)
        {
            if (!values.TryGetValue(key, out List<int> indices))
            {
                indices = new List<int>(1);
                values.Add(key, indices);
            }
            indices.Add(columnIndex);
        }

        private static IReadOnlyList<int> GetValues(
            Dictionary<SpecificColumnKey, List<int>> values,
            SpecificColumnKey key)
        {
            return values.TryGetValue(key, out List<int> result)
                ? result
                : EmptyColumns;
        }

        private static void AddValue(
            Dictionary<SpecificColumnKey, List<int>> values,
            SpecificColumnKey key,
            int columnIndex)
        {
            if (!values.TryGetValue(key, out List<int> indices))
            {
                indices = new List<int>(1);
                values.Add(key, indices);
            }
            indices.Add(columnIndex);
        }

        private readonly struct SpecificColumnKey : IEquatable<SpecificColumnKey>
        {
            internal SpecificColumnKey(string workTypeDefName, string workGiverDefName)
            {
                WorkTypeDefName = workTypeDefName ?? string.Empty;
                WorkGiverDefName = workGiverDefName ?? string.Empty;
            }

            private string WorkTypeDefName { get; }
            private string WorkGiverDefName { get; }

            public bool Equals(SpecificColumnKey other) =>
                StringComparer.Ordinal.Equals(WorkTypeDefName, other.WorkTypeDefName) &&
                StringComparer.Ordinal.Equals(WorkGiverDefName, other.WorkGiverDefName);

            public override bool Equals(object obj) =>
                obj is SpecificColumnKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (StringComparer.Ordinal.GetHashCode(WorkTypeDefName) * 397) ^
                           StringComparer.Ordinal.GetHashCode(WorkGiverDefName);
                }
            }
        }
    }

    internal readonly struct WorkGridInspectionCellKey : IEquatable<WorkGridInspectionCellKey>
    {
        internal WorkGridInspectionCellKey(int rowIndex, int columnIndex)
        {
            RowIndex = rowIndex;
            ColumnIndex = columnIndex;
        }

        internal int RowIndex { get; }
        internal int ColumnIndex { get; }

        public bool Equals(WorkGridInspectionCellKey other) =>
            RowIndex == other.RowIndex && ColumnIndex == other.ColumnIndex;

        public override bool Equals(object obj) =>
            obj is WorkGridInspectionCellKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (RowIndex * 397) ^ ColumnIndex;
            }
        }
    }

    /// <summary>Composes multiple inspection meanings that address the same visible cell.</summary>
    internal sealed class WorkGridInspectionCellMasks
    {
        private readonly Dictionary<WorkGridInspectionCellKey, WorkGridInspectionCellKind> _values =
            new Dictionary<WorkGridInspectionCellKey, WorkGridInspectionCellKind>();

        internal int Count => _values.Count;
        internal Dictionary<WorkGridInspectionCellKey, WorkGridInspectionCellKind> Entries =>
            _values;

        internal void Clear() => _values.Clear();

        internal void Add(int rowIndex, int columnIndex, WorkGridInspectionCellKind kind)
        {
            if (kind == WorkGridInspectionCellKind.None) return;

            var key = new WorkGridInspectionCellKey(rowIndex, columnIndex);
            _values.TryGetValue(key, out WorkGridInspectionCellKind existing);
            _values[key] = existing | kind;
        }

        internal bool TryGet(
            int rowIndex,
            int columnIndex,
            out WorkGridInspectionCellKind kind)
        {
            return _values.TryGetValue(
                new WorkGridInspectionCellKey(rowIndex, columnIndex),
                out kind);
        }
    }
}
