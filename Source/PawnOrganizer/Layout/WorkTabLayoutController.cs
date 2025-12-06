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

        private List<RowDescriptor> _cachedRowDescriptors;
        private bool _rowDescriptorsDirty = true;
        private const float PawnRowHeight = 30f;


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

        // === DIRTY STATE TRACKING ===
        private bool _isDirty = true;
        private int _cachedPawnCount = 0;
        private int _cachedDividerCount = 0;
        private PawnColumnDef _lastSortingBy = null;
        private bool _lastSortingDescending = false;
        private Dictionary<int, int> _lastDisplayOrders = new Dictionary<int, int>(); // pawn ID -> displayOrder

        private List<bool> _lastCollapsedStates = new List<bool>(); // Track divider collapse states

        /// <summary>
        /// Mark rebuild as needed only if something actually changed.
        /// </summary>
        private bool ShouldRebuild(PawnTable table, IPawnOrganizerSnapshot snapshot)
        {
            if (_isDirty) return true;
            if (table == null || snapshot == null) return true;

            // Check pawn count change
            if (snapshot.Pawns == null || snapshot.Pawns.Count != _cachedPawnCount)
                return true;

            // Check divider count change
            if (snapshot.Dividers == null || snapshot.Dividers.Count != _cachedDividerCount)
                return true;

            // Check sort state change
            if (table.SortingBy != _lastSortingBy || table.SortingDescending != _lastSortingDescending)
                return true;

            // Check pawn display order changes (after drag-reorder)
            for (int i = 0; i < snapshot.Pawns.Count; i++)
            {
                var pawn = snapshot.Pawns[i];
                if (pawn?.playerSettings == null) continue;

                int currentOrder = pawn.playerSettings.displayOrder;
                if (!_lastDisplayOrders.TryGetValue(pawn.thingIDNumber, out int lastOrder) || lastOrder != currentOrder)
                    return true;
            }

            // Check divider collapse states
            if (snapshot.Dividers != null && snapshot.Dividers.Count == _lastCollapsedStates.Count)
            {
                for (int i = 0; i < snapshot.Dividers.Count; i++)
                {
                    if (snapshot.Dividers[i].IsCollapsed != _lastCollapsedStates[i])
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Cache the current state after a rebuild.
        /// </summary>
        private void CacheState(PawnTable table, IPawnOrganizerSnapshot snapshot)
        {
            _cachedPawnCount = snapshot?.Pawns?.Count ?? 0;
            _cachedDividerCount = snapshot?.Dividers?.Count ?? 0;
            _lastSortingBy = table?.SortingBy;
            _lastSortingDescending = table?.SortingDescending ?? false;

            _lastDisplayOrders.Clear();
            if (snapshot?.Pawns != null)
            {
                foreach (var pawn in snapshot.Pawns)
                {
                    if (pawn?.playerSettings != null)
                        _lastDisplayOrders[pawn.thingIDNumber] = pawn.playerSettings.displayOrder;
                }
            }

            _lastCollapsedStates.Clear();
            if (snapshot?.Dividers != null)
            {
                foreach (var div in snapshot.Dividers)
                    _lastCollapsedStates.Add(div.IsCollapsed);
            }

            _isDirty = false;
        }


        /// <summary>
        /// Initialize the layout controller with column width storage.
        /// </summary>
        public WorkTabLayoutController(IColumnWidthStore columnWidthStore)
        {
            _columnWidthStore = columnWidthStore;
            _cachedRowDescriptors = new List<RowDescriptor>();
            _rowDescriptorsDirty = true;
        }

        /// <summary>
        /// Returns cached row descriptors; rebuilds from elements if cache is invalid.
        /// </summary>
        public List<RowDescriptor> GetRowDescriptors()
        {
            if (_rowDescriptorsDirty)
            {
                _cachedRowDescriptors = BuildRowDescriptorsInternal();
                _rowDescriptorsDirty = false;
            }
            return _cachedRowDescriptors;
        }

        /// <summary>
        /// Mark row descriptors cache as invalid; will rebuild on next GetRowDescriptors() call.
        /// </summary>
        public void InvalidateRowDescriptors()
        {
            _rowDescriptorsDirty = true;
            _isDirty = true; // Also mark layout as dirty
        }

        /// <summary>
        /// Builds row descriptors from current pawn/divider elements, preserving order and heights.
        /// </summary>
        private List<RowDescriptor> BuildRowDescriptorsInternal()
        {
            var elements = BuildOrderedElements();
            var descriptors = new List<RowDescriptor>(elements.Count);

            foreach (var element in elements)
            {
                if (element is DividerElement divEl)
                {
                    descriptors.Add(new RowDescriptor(divEl.Divider, divEl.Divider.Height));
                }
                else if (element is PawnElement pawnEl)
                {
                    descriptors.Add(new RowDescriptor(pawnEl.Pawn, PawnRowHeight));
                }
            }

            return descriptors;
        }

        public IReadOnlyList<WorkTabLayoutRow> Rows => _rows;
        public IReadOnlyList<WorkTabLayoutColumn> Columns => _columns;
        public float ContentHeight => _contentHeight;
        public float HeaderHeight => _table?.cachedHeaderHeight ?? 0f;
        public Vector2 TableOrigin => _origin;
        public PawnTable Table => _table;

        public void Rebuild(PawnTable table, IPawnOrganizerSnapshot snapshot, Vector2 origin)
        {
            // SKIP REBUILD IF NOTHING CHANGED
            if (!ShouldRebuild(table, snapshot))
            {
                return; // All cached data is still valid
            }

            if (table == null)
            {
                Log.Error("[BWT] WorkTabLayoutController.Rebuild failed: table is null.");
                _rows.Clear();
                _columns.Clear();
                _contentHeight = 0f;
                return;
            }

            try
            {
                _table = table;
                _origin = origin;
                _rows.Clear();
                _columns.Clear();
                _contentHeight = 0f;
                _rowWidth = 0f;
                _dividerHeight = BetterWorkTabMod.Settings?.dividerHeight ?? DefaultDividerHeight;

                _snapshotPawns = snapshot?.Pawns
                                 ?? (IReadOnlyList<Pawn>)table.PawnsListForReading
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

                EnsureTableFresh();
                _rowWidth = Mathf.Max(0f, _table.Size.x - 16f);

                BuildColumns();
                BuildRows();
            }
            catch (Exception ex)
            {
                Log.Error($"[BWT] WorkTabLayoutController.Rebuild encountered an error: {ex}");
                _rows.Clear();
                _columns.Clear();
                _contentHeight = 0f;
            }
            CacheState(table, snapshot);
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

            Log.Message($"[TryGetRowAt] contentY={contentY}, checking {_rows.Count} rows");
            for (int i = 0; i < _rows.Count; i++)
            {
                Log.Message($"  Row {i}: OffsetY={_rows[i].OffsetY}, Height={_rows[i].Height}, " +
                    $"Range=[{_rows[i].OffsetY}, {_rows[i].OffsetY + _rows[i].Height})");
                var candidate = _rows[i];
                float rowStart = candidate.OffsetY;
                float rowEnd = candidate.OffsetY + candidate.Height;

                if (contentY >= rowStart && contentY < rowEnd)
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

            if (_table.SortingBy != null)
            {
                return AddDividerAfterPawnWhileSorting(pawn, label, color);
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

            if (_table.SortingBy != null)
            {
                return AddDividerBeforePawnWhileSorting(pawn, label, color);
            }

            int targetOrder = pawn.playerSettings?.displayOrder ?? 0;
            ShiftDisplayOrdersFrom(targetOrder);
            return CreateDivider(label, color, targetOrder);
        }

        private PawnDivider AddDividerAfterPawnWhileSorting(Pawn pawn, string label, Color color)
        {
            // ✅ Defensive checks
            if (_table == null)
            {
                Log.Error("[AddDividerAfterPawnWhileSorting] _table is null!");
                return null;
            }

            if (pawn?.playerSettings == null)
            {
                Log.Error($"[AddDividerAfterPawnWhileSorting] Pawn {pawn?.LabelShort} has no playerSettings!");
                return null;
            }

            // Find the pawn's current visual position in the sorted rows
            int visualIndex = -1;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Pawn == pawn)
                {
                    visualIndex = i;
                    break;
                }
            }

            if (visualIndex < 0)
            {
                Log.Error($"[AddDividerAfterPawnWhileSorting] Could not find pawn {pawn.LabelShort} in current rows");
                return null;
            }

            //Log.Message($"[AddDividerAfterPawnWhileSorting] Pawn {pawn.LabelShort} is at visual index {visualIndex}");

            

            // Recalculate displayOrder to match current visual order
            RecalculateDisplayOrderFromVisualOrder();

            // Now add the divider using the updated displayOrder
            int newDisplayOrder = pawn.playerSettings.displayOrder + 1;
            ShiftDisplayOrdersFrom(newDisplayOrder);
            var divider = CreateDivider(label, color, newDisplayOrder);

            //Log.Message($"[AddDividerAfterPawnWhileSorting] Created divider with displayOrder {newDisplayOrder}");

            return divider;
        }

        private PawnDivider AddDividerBeforePawnWhileSorting(Pawn pawn, string label, Color color)
        {
            // ✅ Defensive checks
            if (_table == null)
            {
                Log.Error("[AddDividerBeforePawnWhileSorting] _table is null!");
                return null;
            }

            if (pawn?.playerSettings == null)
            {
                Log.Error($"[AddDividerBeforePawnWhileSorting] Pawn {pawn?.LabelShort} has no playerSettings!");
                return null;
            }

            // Find the pawn's current visual position in the sorted rows
            int visualIndex = -1;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Pawn == pawn)
                {
                    visualIndex = i;
                    break;
                }
            }

            if (visualIndex < 0)
            {
                Log.Error($"[AddDividerBeforePawnWhileSorting] Could not find pawn {pawn.LabelShort} in current rows");
                return null;
            }

            Log.Message($"[AddDividerBeforePawnWhileSorting] Pawn {pawn.LabelShort} is at visual index {visualIndex}");

            

            // Recalculate displayOrder to match current visual order
            RecalculateDisplayOrderFromVisualOrder();

            // Now add the divider using the updated displayOrder
            int newDisplayOrder = pawn.playerSettings.displayOrder;
            ShiftDisplayOrdersFrom(newDisplayOrder);
            var divider = CreateDivider(label, color, newDisplayOrder);

            //Log.Message($"[AddDividerBeforePawnWhileSorting] Created divider with displayOrder {newDisplayOrder}");

            return divider;
        }

        private void RecalculateDisplayOrderFromVisualOrder()
        {
            //Log.Message($"[RecalculateDisplayOrderFromVisualOrder] Recalculating displayOrder from {_rows.Count} rows");

            for (int i = 0; i < _rows.Count; i++)
            {
                var element = _rows[i];

                if (element.Pawn != null && element.Pawn.playerSettings != null)
                {
                    element.Pawn.playerSettings.displayOrder = i;
                    Log.Message($"  Row {i}: {element.Pawn.LabelShort} → displayOrder {i}");
                }
                else if (element.Divider != null)
                {
                    element.Divider.DisplayOrder = i;
                    Log.Message($"  Row {i}: Divider '{element.Divider.DividerName}' → displayOrder {i}");
                }
            }
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
                else if (element is DividerElement dividerElement)
                {
                    var divider = dividerElement.Divider;
                    float requestedHeight = divider?.Height ?? _dividerHeight;
                    height = Mathf.Clamp(requestedHeight, 10f, 80f);
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

            List<DisplayElement> ordered;

            // If no sorting, use manual order
            if (_table.SortingBy == null)
            {
                ordered = _workingElements
                    .OrderBy(e => e.DisplayOrder)
                    .ToList();
            }
            else
            {
                // When sorting, dividers act as immovable barriers
                // Pawns can only sort WITHIN sections between dividers

                var manuallyOrdered = _workingElements.OrderBy(e => e.DisplayOrder).ToList();
                var result = new List<DisplayElement>();

                Func<Pawn, Pawn, int> comparator = (a, b) =>
                {
                    if (_table.SortingDescending)
                        return _table.SortingBy.Worker.Compare(b, a);
                    return _table.SortingBy.Worker.Compare(a, b);
                };

                // Partition the list by dividers and sort each section independently
                var sortingSection = new List<Pawn>();  

                foreach (var element in manuallyOrdered)
                {
                    if (element.IsDivider)
                    {
                        // Sort and add the current section of pawns
                        if (sortingSection.Count > 0)
                        {
                            sortingSection.SortStable(comparator);
                            foreach (var pawn in sortingSection)
                            {
                                result.Add(new PawnElement(pawn));
                            }
                            sortingSection.Clear();
                        }

                        // Add the divider as an immovable barrier
                        result.Add(element);
                    }
                    else if (element is PawnElement pawnElement)
                    {
                        sortingSection.Add(pawnElement.Pawn);
                    }
                }

                // Don't forget to sort and add the final section after the last divider
                if (sortingSection.Count > 0)
                {
                    sortingSection.SortStable(comparator);
                    foreach (var pawn in sortingSection)
                    {
                        result.Add(new PawnElement(pawn));
                    }
                }

                ordered = result;
            }

            // Check for collapsed dividers ONCE
            bool hasCollapsedDividers = ordered.Any(e => e.IsDivider && (e as DividerElement)?.Divider?.IsCollapsed == true);
            if (!hasCollapsedDividers)
            {
                return ordered;
            }

            // Partition by dividers and filter pawns based on preceding divider state
            var filtered = new List<DisplayElement>(ordered.Count);
            var filteringSection = new List<DisplayElement>();  
            PawnDivider lastDivider = null;

            for (int i = 0; i < ordered.Count; i++)
            {
                var element = ordered[i];

                if (element.IsDivider)
                {
                    var divider = (element as DividerElement)?.Divider;

                    // Only add previous section if the divider wasn't collapsed
                    if (lastDivider == null || !lastDivider.IsCollapsed)
                    {
                        filtered.AddRange(filteringSection);
                    }

                    // Always add the divider itself
                    filtered.Add(element);

                    lastDivider = divider;
                    filteringSection.Clear();
                }
                else if (element is PawnElement)
                {
                    filteringSection.Add(element);
                }
            }

            // Handle final section: add if last divider wasn't collapsed (or no dividers exist)
            if (lastDivider == null || !lastDivider.IsCollapsed)
            {
                filtered.AddRange(filteringSection);
            }

            return filtered;
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
                DisplayOrder = displayOrder,
                Height = Mathf.Clamp(_dividerHeight, 10f, 80f),
                IsCollapsed = false
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
