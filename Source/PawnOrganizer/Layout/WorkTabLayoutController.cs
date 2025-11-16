using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer
{
    /// <summary>
    /// Concrete implementation that owns the Work tab layout snapshot and ordering logic.
    /// </summary>
    public class WorkTabLayoutController : IWorkTabLayoutController
    {
        private static readonly MethodInfo RecacheIfDirty =
            AccessTools.Method(typeof(PawnTable), "RecacheIfDirty");

        private readonly List<WorkTabLayoutRow> _rows = new List<WorkTabLayoutRow>();
        private readonly List<WorkTabLayoutColumn> _columns = new List<WorkTabLayoutColumn>();
        private readonly List<DisplayElement> _workingElements = new List<DisplayElement>();

        private Worklist _worklist;
        private PawnTable _table;
        private Vector2 _origin;
        private float _contentHeight;
        private float _rowWidth;

        public IReadOnlyList<WorkTabLayoutRow> Rows => _rows;
        public IReadOnlyList<WorkTabLayoutColumn> Columns => _columns;
        public float ContentHeight => _contentHeight;
        public Vector2 TableOrigin => _origin;
        public float HeaderHeight => _table?.cachedHeaderHeight ?? 0f;
        public float DividerHeight => BetterWorkTabMod.Settings.dividerHeight;
        public PawnTable Table => _table;

        public void Rebuild(PawnTable table, Worklist worklist, Vector2 origin)
        {
            _table = table;
            _worklist = worklist;
            _origin = origin;

            _rows.Clear();
            _columns.Clear();
            _contentHeight = 0f;
            _rowWidth = 0f;

            if (_table == null || _worklist == null)
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

        public PawnDivider InsertDividerAfter(Pawn pawn, string label)
        {
            if (_worklist == null || pawn == null)
            {
                return null;
            }

            var divider = new PawnDivider
            {
                DividerName = label ?? "Divider",
                DividerColor = Color.gray,
                DisplayOrder = (pawn.playerSettings?.displayOrder ?? _rows.Count) + 1
            };
            _worklist.Dividers.Add(divider);
            return divider;
        }

        public void RemoveDivider(PawnDivider divider)
        {
            if (divider == null || _worklist == null)
            {
                return;
            }

            _worklist.Dividers.Remove(divider);
        }

        public void RenameDivider(PawnDivider divider, string newLabel)
        {
            if (divider == null || newLabel == null)
            {
                return;
            }
            divider.DividerName = newLabel.Trim();
        }

        public void MoveElement(DisplayElement element, int targetIndex)
        {
            if (_table == null || element == null)
            {
                return;
            }

            var ordered = BuildOrderedElements();
            int currentIndex = ordered.FindIndex(e => IsSameElement(e, element));
            if (currentIndex == -1)
            {
                return;
            }

            targetIndex = Mathf.Clamp(targetIndex, 0, ordered.Count);
            var item = ordered[currentIndex];
            ordered.RemoveAt(currentIndex);
            if (targetIndex >= ordered.Count)
            {
                ordered.Add(item);
            }
            else
            {
                ordered.Insert(targetIndex, item);
            }

            ApplyDisplayOrder(ordered);
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
                float width = (i == columns.Count - 1)
                    ? Mathf.Max(0f, _rowWidth - usedWidth)
                    : _table.cachedColumnWidths[i];

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

                var pawnElement = element as PawnElement;
                if (pawnElement != null)
                {
                    float rowHeight;
                    if (!pawnHeights.TryGetValue(pawnElement.Pawn, out rowHeight))
                    {
                        rowHeight = 30f;
                    }
                    height = rowHeight;
                }
                else
                {
                    height = DividerHeight;
                }

                _rows.Add(new WorkTabLayoutRow(element, _contentHeight, height, i));
                _contentHeight += height;
            }
        }

        private Dictionary<Pawn, float> CachePawnRowHeights()
        {
            var dict = new Dictionary<Pawn, float>();
            for (int i = 0; i < _table.cachedPawns.Count; i++)
            {
                float height = (i < _table.cachedRowHeights.Count)
                    ? _table.cachedRowHeights[i]
                    : 30f;
                dict[_table.cachedPawns[i]] = height;
            }
            return dict;
        }

        private List<DisplayElement> BuildOrderedElements()
        {
            _workingElements.Clear();
            _workingElements.AddRange(_table.cachedPawns.Select(p => new PawnElement(p)));
            if (_worklist != null)
            {
                _workingElements.AddRange(_worklist.Dividers.Select(d => new DividerElement(d)));
            }

            return _workingElements
                .OrderBy(e => e.DisplayOrder)
                .ThenBy(e => e.IsDivider ? 1 : 0)
                .ToList();
        }

        private static bool IsSameElement(DisplayElement a, DisplayElement b)
        {
            if (a.IsDivider && b.IsDivider)
            {
                return ReferenceEquals(((DividerElement)a).Divider, ((DividerElement)b).Divider);
            }

            if (!a.IsDivider && !b.IsDivider)
            {
                return ReferenceEquals(((PawnElement)a).Pawn, ((PawnElement)b).Pawn);
            }

            return false;
        }

        private void ApplyDisplayOrder(List<DisplayElement> ordered)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i] is PawnElement pawnElement)
                {
                    var settings = pawnElement.Pawn.playerSettings;
                    if (settings != null)
                    {
                        settings.displayOrder = i;
                    }
                }
                else if (ordered[i] is DividerElement dividerElement)
                {
                    dividerElement.Divider.DisplayOrder = i;
                }
            }

            if (_worklist != null)
            {
                _worklist.Dividers.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
            }

            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }
    }
}
