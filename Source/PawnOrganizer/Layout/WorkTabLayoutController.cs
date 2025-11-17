using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Better_Work_Tab.Features;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer
{
    /// <summary>
    /// Pure layout controller that converts the current pawn/divider snapshot into rows and columns.
    /// </summary>
    public class WorkTabLayoutController : IWorkTabLayoutController
    {
        private static readonly MethodInfo RecacheIfDirty =
            AccessTools.Method(typeof(PawnTable), "RecacheIfDirty");

        private const float DefaultDividerHeight = 18f;

        private readonly IColumnWidthStore _columnWidthStore;
        private readonly List<WorkTabLayoutRow> _rows = new List<WorkTabLayoutRow>();
        private readonly List<WorkTabLayoutColumn> _columns = new List<WorkTabLayoutColumn>();
        private readonly List<DisplayElement> _workingElements = new List<DisplayElement>();
        private readonly List<PawnDivider> _dividerBuffer = new List<PawnDivider>();

        private IReadOnlyList<Pawn> _snapshotPawns = Array.Empty<Pawn>();
        private IList<PawnDivider> _snapshotDividers = Array.Empty<PawnDivider>();

        private PawnTable _table;
        private Vector2 _origin;
        private float _contentHeight;
        private float _rowWidth;
        private float _dividerHeight = DefaultDividerHeight;

        public WorkTabLayoutController(IColumnWidthStore columnWidthStore)
        {
            _columnWidthStore = columnWidthStore;
        }

        public IReadOnlyList<WorkTabLayoutRow> Rows => _rows;
        public IReadOnlyList<WorkTabLayoutColumn> Columns => _columns;
        public float ContentHeight => _contentHeight;
        public float HeaderHeight => _table?.cachedHeaderHeight ?? 0f;
        public Vector2 TableOrigin => _origin;
        public PawnTable Table => _table;

        public void Rebuild(PawnTable table, IPawnOrganizerSnapshot snapshot, Vector2 origin)
        {
            _table = table;
            _origin = origin;
            _rows.Clear();
            _columns.Clear();
            _contentHeight = 0f;
            _rowWidth = 0f;
            _dividerHeight = BetterWorkTabMod.Settings?.dividerHeight ?? DefaultDividerHeight;

            _snapshotPawns = snapshot?.Pawns
                             ?? (table != null ? (IReadOnlyList<Pawn>)table.PawnsListForReading : Array.Empty<Pawn>())
                             ?? Array.Empty<Pawn>();

            if (snapshot?.Dividers is IList<PawnDivider> dividerList && !dividerList.IsReadOnly)
            {
                _snapshotDividers = dividerList;
            }
            else
            {
                _dividerBuffer.Clear();
                if (snapshot?.Dividers != null)
                {
                    _dividerBuffer.AddRange(snapshot.Dividers);
                }
                _snapshotDividers = _dividerBuffer;
            }

            if (_table == null)
            {
                return;
            }

            EnsureTableFresh();
            _rowWidth = Mathf.Max(0f, _table.Size.x - 16f);

            BuildColumns();
            BuildRows();
        }

        public bool TryGetRowAt(Vector2 mousePosition, out WorkTabLayoutRow row)
        {
            row = default;
            if (_table == null)
            {
                return false;
            }

            float headerTop = _origin.y + HeaderHeight;
            if (mousePosition.x < _origin.x || mousePosition.x > _origin.x + _rowWidth)
            {
                return false;
            }

            float contentY = mousePosition.y - headerTop + _table.scrollPosition.y;
            if (contentY < 0f)
            {
                return false;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                var candidate = _rows[i];
                if (contentY >= candidate.OffsetY && contentY < candidate.OffsetY + candidate.Height)
                {
                    row = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetColumnAt(Vector2 mousePosition, out WorkTabLayoutColumn column)
        {
            column = default;
            if (_columns.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < _columns.Count; i++)
            {
                if (_columns[i].HeaderRect.Contains(mousePosition))
                {
                    column = _columns[i];
                    return true;
                }
            }

            return false;
        }

        public PawnDivider AddDividerAfterPawn(Pawn pawn, string label, Color color)
        {
            if (pawn == null)
            {
                return null;
            }

            int baseOrder = pawn.playerSettings?.displayOrder ?? Rows.Count;
            int targetOrder = baseOrder + 1;
            ShiftDisplayOrdersFrom(targetOrder);
            return CreateDivider(label, color, targetOrder);
        }

        public PawnDivider AddDividerBeforePawn(Pawn pawn, string label, Color color)
        {
            if (pawn == null)
            {
                return null;
            }

            int targetOrder = pawn.playerSettings?.displayOrder ?? 0;
            ShiftDisplayOrdersFrom(targetOrder);
            return CreateDivider(label, color, targetOrder);
        }

        public void RemoveDivider(PawnDivider divider)
        {
            if (divider == null || _snapshotDividers == null)
            {
                return;
            }

            if (_snapshotDividers.IsReadOnly)
            {
                return;
            }

            for (int i = _snapshotDividers.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_snapshotDividers[i], divider))
                {
                    _snapshotDividers.RemoveAt(i);
                    break;
                }
            }
        }

        public Rect GetScreenRect(WorkTabLayoutRow row)
        {
            if (_table == null)
            {
                return Rect.zero;
            }

            float screenY = _origin.y + HeaderHeight + row.OffsetY - _table.scrollPosition.y;
            return new Rect(_origin.x, screenY, _rowWidth, row.Height);
        }

        private void EnsureTableFresh()
        {
            RecacheIfDirty?.Invoke(_table, null);
        }

        private void BuildColumns()
        {
            var columns = _table.Columns;
            float currentX = _origin.x;
            float usedWidth = 0f;

            for (int i = 0; i < columns.Count; i++)
            {
                float defaultWidth = (i == columns.Count - 1)
                    ? Mathf.Max(0f, _rowWidth - usedWidth)
                    : _table.cachedColumnWidths[i];

                float width = _columnWidthStore?.GetWidth(columns[i], defaultWidth) ?? defaultWidth;

                var headerRect = new Rect(currentX, _origin.y, width, HeaderHeight);
                _columns.Add(new WorkTabLayoutColumn(columns[i], headerRect, currentX - _origin.x, width));

                currentX += width;
                usedWidth += width;
            }
        }

        private void BuildRows()
        {
            _contentHeight = 0f;
            var pawnHeights = CachePawnRowHeights();
            var ordered = BuildOrderedElements();

            for (int i = 0; i < ordered.Count; i++)
            {
                var element = ordered[i];
                float height;

                if (element is PawnElement pawnElement)
                {
                    if (!pawnHeights.TryGetValue(pawnElement.Pawn, out height))
                    {
                        height = 30f;
                    }
                }
                else
                {
                    height = _dividerHeight;
                }

                _rows.Add(new WorkTabLayoutRow(element, _contentHeight, height, i));
                _contentHeight += height;
            }
        }

        private Dictionary<Pawn, float> CachePawnRowHeights()
        {
            var dict = new Dictionary<Pawn, float>();
            if (_table?.cachedPawns == null)
            {
                return dict;
            }

            var cachedHeights = _table.cachedRowHeights;
            for (int i = 0; i < _table.cachedPawns.Count; i++)
            {
                float height = (cachedHeights != null && i < cachedHeights.Count)
                    ? cachedHeights[i]
                    : 30f;
                dict[_table.cachedPawns[i]] = height;
            }

            return dict;
        }

        private List<DisplayElement> BuildOrderedElements()
        {
            _workingElements.Clear();
            _workingElements.AddRange(_snapshotPawns.Select(p => new PawnElement(p)));
            if (_snapshotDividers != null)
            {
                _workingElements.AddRange(_snapshotDividers.Select(d => new DividerElement(d)));
            }

            if (_table.SortingBy == null)
            {
                return _workingElements
                    .OrderBy(e => e.DisplayOrder)
                    .ThenBy(e => e.IsDivider ? 1 : 0)
                    .ToList();
            }

            var finalSortedList = new List<DisplayElement>();
            var manuallyOrdered = _workingElements.OrderBy(e => e.DisplayOrder).ToList();
            var currentPawnGroup = new List<Pawn>();
            Func<Pawn, Pawn, int> comparator = (a, b) =>
            {
                if (_table.SortingDescending)
                {
                    return _table.SortingBy.Worker.Compare(b, a);
                }

                return _table.SortingBy.Worker.Compare(a, b);
            };

            void SortAndAddCurrentGroup()
            {
                if (!currentPawnGroup.Any())
                {
                    return;
                }

                currentPawnGroup.SortStable(comparator);
                for (int i = 0; i < currentPawnGroup.Count; i++)
                {
                    finalSortedList.Add(new PawnElement(currentPawnGroup[i]));
                }
                currentPawnGroup.Clear();
            }

            foreach (var element in manuallyOrdered)
            {
                if (element.IsDivider)
                {
                    SortAndAddCurrentGroup();
                    finalSortedList.Add(element);
                    continue;
                }

                if (element is PawnElement pawnElement)
                {
                    currentPawnGroup.Add(pawnElement.Pawn);
                }
            }

            SortAndAddCurrentGroup();

            return finalSortedList;
        }

        private PawnDivider CreateDivider(string label, Color color, int displayOrder)
        {
            if (_snapshotDividers == null || _snapshotDividers.IsReadOnly)
            {
                return null;
            }

            var divider = new PawnDivider
            {
                DividerName = string.IsNullOrWhiteSpace(label) ? "Divider" : label.Trim(),
                DividerColor = color,
                DisplayOrder = displayOrder
            };

            _snapshotDividers.Add(divider);
            return divider;
        }

        private void ShiftDisplayOrdersFrom(int targetOrder)
        {
            if (_snapshotPawns != null)
            {
                for (int i = 0; i < _snapshotPawns.Count; i++)
                {
                    var pawn = _snapshotPawns[i];
                    var settings = pawn?.playerSettings;
                    if (settings != null && settings.displayOrder >= targetOrder)
                    {
                        settings.displayOrder++;
                    }
                }
            }

            if (_snapshotDividers == null)
            {
                return;
            }

            for (int i = 0; i < _snapshotDividers.Count; i++)
            {
                var divider = _snapshotDividers[i];
                if (divider.DisplayOrder >= targetOrder)
                {
                    divider.DisplayOrder++;
                }
            }
        }
    }
}
