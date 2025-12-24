using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Better_Work_Tab.Features;
using Better_Work_Tab.Mod_Support.LocalProfiles;
using Better_Work_Tab.Mod_Support.Multiplayer;
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
        private List<float> _cachedDescriptorHeights;
        private bool _rowDescriptorsDirty = true;
        private const float PawnRowHeight = 30f;
        private readonly object _stateLock = new object();


        private readonly IColumnWidthStore _columnWidthStore;
        private readonly List<WorkTabLayoutRow> _rows = new List<WorkTabLayoutRow>();
        private readonly List<WorkTabLayoutColumn> _columns = new List<WorkTabLayoutColumn>();
        private readonly List<DisplayElement> _workingElements = new List<DisplayElement>();
        private readonly List<PawnDivider> _dividerBuffer = new List<PawnDivider>();
        private readonly List<DisplayElement> _orderedBuffer = new List<DisplayElement>();
        private readonly List<DisplayElement> _sortingSectionBuffer = new List<DisplayElement>();
        private readonly List<DisplayElement> _filteringBuffer = new List<DisplayElement>();
        private readonly List<DisplayElement> _filteringSectionBuffer = new List<DisplayElement>();

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

                int currentOrder = RowOrderUtility.GetPawnRowOrder(pawn);
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
                        _lastDisplayOrders[pawn.thingIDNumber] = RowOrderUtility.GetPawnRowOrder(pawn);
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

        private static void ReleaseDisplayElement(DisplayElement element)
        {
            if (element is PawnElement pawnElement)
            {
                DisplayElementPool.ReleasePawnElement(pawnElement);
            }
            else if (element is DividerElement dividerElement)
            {
                DisplayElementPool.ReleaseDividerElement(dividerElement);
            }
        }

        private void ReleaseRowsToPool()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                ReleaseDisplayElement(_rows[i].Element);
            }
        }

        private static void ReleaseElements(IList<DisplayElement> elements)
        {
            if (elements == null)
            {
                return;
            }

            for (int i = 0; i < elements.Count; i++)
            {
                ReleaseDisplayElement(elements[i]);
            }
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
        /// 
        /// IMPORTANT: Descriptors use RENDERING heights (PawnRowHeight constant for pawns,
        /// raw Height for dividers). This differs from _rows which uses actual cached 
        /// PawnTable heights. All hit-testing and visual line positioning must use 
        /// GetRowDescriptors() heights to match what's actually drawn on screen.
        /// </summary>
        public List<RowDescriptor> GetRowDescriptors()
        {
            var settings = BetterWorkTabMod.Settings;
            if (!((settings?.enablePerformanceOptimizations ?? true) && (settings?.cacheRowDescriptors ?? true)))
            {
                _rowDescriptorsDirty = true;
            }

            if (!_rowDescriptorsDirty && _cachedRowDescriptors != null)
                return _cachedRowDescriptors;

            lock (_stateLock)
            {
                if (!_rowDescriptorsDirty && _cachedRowDescriptors != null)
                    return _cachedRowDescriptors;

                return GetRowDescriptorsLocked();
            }
        }

        private List<RowDescriptor> GetRowDescriptorsLocked()
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
            lock (_stateLock)
            {
                _rowDescriptorsDirty = true;
                _isDirty = true; // Also mark layout as dirty
            }
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
                    float height = Mathf.Clamp(divEl.Divider.Height, 10f, 80f);
                    descriptors.Add(new RowDescriptor(divEl.Divider, height));
                }
                else if (element is PawnElement pawnEl)
                {
                    descriptors.Add(new RowDescriptor(pawnEl.Pawn, PawnRowHeight)); 
                }
            }

            ReleaseElements(elements);
            _workingElements.Clear();
            _orderedBuffer.Clear();
            _filteringBuffer.Clear();
            _sortingSectionBuffer.Clear();
            _filteringSectionBuffer.Clear();

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
            lock (_stateLock)
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
                    ReleaseRowsToPool();
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

                    _rowDescriptorsDirty = true;
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
        }

        public bool TryGetRowAt(Vector2 mousePosition, out WorkTabLayoutRow row)
        {
            row = default;

            // Check if mouse is in the row content area (below header)
            float headerBottom = TableOrigin.y + HeaderHeight;
            if (mousePosition.y < headerBottom)
                return false;

            // Convert to local Y within scrolled content
            float localY = mousePosition.y - headerBottom + Table.scrollPosition.y;

            // Use the VISIBLE row descriptors (respects collapsed dividers)
            var descriptors = GetRowDescriptors();
            float cumulativeY = 0f;

            for (int i = 0; i < descriptors.Count; i++)
            {
                float rowBottom = cumulativeY + descriptors[i].Height;

                if (localY < rowBottom)
                {
                    // Found the row - return the corresponding WorkTabLayoutRow from Rows
                    if (i < Rows.Count)
                    {
                        row = Rows[i];
                        return true;
                    }
                    return false;
                }

                cumulativeY = rowBottom;
            }

            return false;
        }

        /// <summary>
        /// Gets a row at the given mouse position, but ONLY if it's a visible row.
        /// This respects collapsed dividers - hidden rows are never returned.
        /// </summary>
        public bool TryGetVisibleRowAt(Vector2 mousePosition, out WorkTabLayoutRow row)
        {
            row = default;

            float headerBottom = TableOrigin.y + HeaderHeight;
            if (mousePosition.y < headerBottom)
                return false;

            float localY = mousePosition.y - headerBottom + Table.scrollPosition.y;
            var descriptors = GetRowDescriptors(); // ONLY visible rows

            float cumulativeY = 0f;
            for (int i = 0; i < descriptors.Count; i++)
            {
                float rowBottom = cumulativeY + descriptors[i].Height;

                if (localY < rowBottom)
                {
                    // Map descriptor index to actual Rows index
                    if (i < Rows.Count)
                    {
                        row = Rows[i];
                        return true;
                    }
                    return false;
                }

                cumulativeY = rowBottom;
            }

            return false;
        }

        public bool TryGetColumnAt(Vector2 mousePosition, out WorkTabLayoutColumn column)
        {
            column = default;
            lock (_stateLock)
            {
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

            int baseOrder = RowOrderUtility.GetPawnRowOrder(pawn);
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

            int targetOrder = RowOrderUtility.GetPawnRowOrder(pawn);
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
            int newDisplayOrder = RowOrderUtility.GetPawnRowOrder(pawn) + 1;
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


            // Recalculate displayOrder to match current visual order
            RecalculateDisplayOrderFromVisualOrder();

            // Now add the divider using the updated displayOrder
            int newDisplayOrder = RowOrderUtility.GetPawnRowOrder(pawn);
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
                    RowOrderUtility.SetPawnRowOrder(element.Pawn, i);
                    BetterWorkTabMod.DebugLog($"  Row {i}: {element.Pawn.LabelShort} → displayOrder {i}", DebugFeature.DragDrop);
                }
                else if (element.Divider != null)
                {
                    element.Divider.DisplayOrder = i;
                    BetterWorkTabMod.DebugLog($"  Row {i}: Divider '{element.Divider.DividerName}' → displayOrder {i}", DebugFeature.DragDrop);
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
#if v1_3
            var allColumns = _table.ColumnsListForReading;
#else
            var allColumns = _table.Columns;
#endif
            var hiddenWorktypes = BetterWorkTabMod.Settings?.hiddenWorktypes;

            var visibleColumns = new List<(PawnColumnDef def, int originalIndex)>();
            for (int i = 0; i < allColumns.Count; i++)
            {
                var def = allColumns[i];
                if (def.workType != null && hiddenWorktypes != null && hiddenWorktypes.Contains(def.workType.defName))
                {
                    continue;
                }
                visibleColumns.Add((def, i));
            }

            if (visibleColumns.Count == 0)
            {
                return;
            }

            // 1. Identify the best 'fill' column. In RimWorld work tabs, we typically want 
            // one stretchable column (usually Pawn Label) while keeping work priorities 
            // at their small, fixed widths so they remain equidistant and visually aligned.
            int fillerIndex = -1;
            for (int i = 0; i < visibleColumns.Count; i++)
            {
                if (visibleColumns[i].def.Worker is PawnColumnWorker_Label)
                {
                    fillerIndex = i;
                    break;
                }
            }

            // Fallback: If no Label column, prefer the first non-work-priority column.
            if (fillerIndex == -1)
            {
                for (int i = 0; i < visibleColumns.Count; i++)
                {
                    if (!(visibleColumns[i].def.Worker is PawnColumnWorker_WorkPriority))
                    {
                        fillerIndex = i;
                        break;
                    }
                }
            }

            // Ultimate fallback (e.g. if the tab ONLY has work columns): use the last one.
            if (fillerIndex == -1)
            {
                fillerIndex = visibleColumns.Count - 1;
            }

            // 2. Calculate natural widths and identify surplus/deficit relative to _rowWidth.
            float totalNaturalWidth = 0f;
            float[] widths = new float[visibleColumns.Count];
            const float spacing = 0f;

            for (int i = 0; i < visibleColumns.Count; i++)
            {
                var (columnDef, originalIndex) = visibleColumns[i];
                float w = _columnWidthStore?.GetWidth(columnDef, -1f) ?? -1f;
                
                if (w < 0f)
                {
                    w = (originalIndex < _table.cachedColumnWidths.Count) 
                        ? _table.cachedColumnWidths[originalIndex] 
                        : 30f;
                }
                
                widths[i] = w;
                totalNaturalWidth += w;
                if (i < visibleColumns.Count - 1) totalNaturalWidth += spacing;
            }

            // Distribute any remainder to our designated filler.
            float surplus = _rowWidth - totalNaturalWidth;
            widths[fillerIndex] = Mathf.Max(widths[fillerIndex] + surplus, 10f);

            // 3. Build the final column layouts.
            float currentX = _origin.x;
            for (int i = 0; i < visibleColumns.Count; i++)
            {
                var (columnDef, _) = visibleColumns[i];
                float width = widths[i];

                // Ensure the absolute last column hits the edge perfectly to avoid rounding gaps.
                if (i == visibleColumns.Count - 1)
                {
                    width = Mathf.Max(0f, (_origin.x + _rowWidth) - currentX);
                }

                var headerRect = new Rect(currentX, _origin.y, width, HeaderHeight);
                _columns.Add(new WorkTabLayoutColumn(columnDef, headerRect, currentX - _origin.x, width));

                currentX += width;
                if (i < visibleColumns.Count - 1)
                {
                    currentX += spacing;
                }
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
                    height = PawnRowHeight;
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

            if (_snapshotPawns != null)
            {
                for (int i = 0; i < _snapshotPawns.Count; i++)
                {
                    if (_snapshotPawns[i] != null) // Defensive check
                        _workingElements.Add(DisplayElementPool.GetPawnElement(_snapshotPawns[i]));
                }
            }

            if (_snapshotDividers != null)
            {
                for (int i = 0; i < _snapshotDividers.Count; i++)
                {
                    if (_snapshotDividers[i] != null) // Defensive check
                        _workingElements.Add(DisplayElementPool.GetDividerElement(_snapshotDividers[i]));
                }
            }

            List<DisplayElement> ordered;

            // If no sorting, use manual order
            if (_table.SortingBy == null)
            {
                _orderedBuffer.Clear();
                foreach (var element in _workingElements.OrderBy(e => e.DisplayOrder))
                {
                    _orderedBuffer.Add(element);
                }

                ordered = _orderedBuffer;
            }
            else
            {
                // When sorting, dividers act as immovable barriers
                // Pawns can only sort WITHIN sections between dividers

                var manuallyOrdered = _workingElements.OrderBy(e => e.DisplayOrder);
                _orderedBuffer.Clear();
                _sortingSectionBuffer.Clear();

                void FlushSortingSection()
                {
                    if (_sortingSectionBuffer.Count == 0)
                    {
                        return;
                    }

                    _sortingSectionBuffer.SortStable((a, b) =>
                    {
                        var pawnA = (a as PawnElement)?.Pawn;
                        var pawnB = (b as PawnElement)?.Pawn;
                        return _table.SortingDescending
                            ? _table.SortingBy.Worker.Compare(pawnB, pawnA)
                            : _table.SortingBy.Worker.Compare(pawnA, pawnB);
                    });

                    _orderedBuffer.AddRange(_sortingSectionBuffer);
                    _sortingSectionBuffer.Clear();
                }

                // Partition the list by dividers and sort each section independently
                foreach (var element in manuallyOrdered)
                {
                    if (element.IsDivider)
                    {
                        // Add the divider as an immovable barrier
                        FlushSortingSection();
                        _orderedBuffer.Add(element);
                    }
                    else if (element is PawnElement pawnElement)
                    {
                        _sortingSectionBuffer.Add(pawnElement);
                    }
                }

                FlushSortingSection();

                ordered = _orderedBuffer;
            }

            // Check for collapsed dividers ONCE
            bool hasCollapsedDividers = false;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i] is DividerElement dividerElement && dividerElement.Divider?.IsCollapsed == true)
                {
                    hasCollapsedDividers = true;
                    break;
                }
            }

            if (!hasCollapsedDividers)
            {
                return ordered;
            }

            // Partition by dividers and filter pawns based on preceding divider state
            _filteringBuffer.Clear();
            _filteringSectionBuffer.Clear();
            var filtered = _filteringBuffer;
            var filteringSection = _filteringSectionBuffer;
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
            SyncDividersToProfile();
            InvalidateRowDescriptors();
            return divider;
        }

        private void ShiftDisplayOrdersFrom(int targetOrder)
        {
            if (_snapshotPawns != null)
            {
                RowOrderUtility.ShiftPawnRowOrdersFrom(_snapshotPawns.ToList(), targetOrder);
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

            SyncDividersToProfile();
        }

        private void SyncDividersToProfile()
        {
            if (!MultiplayerBridge.Active)
            {
                return;
            }

            var profile = BWTLocalProfileStore.Current;
            if (profile == null)
            {
                return;
            }

            if (_snapshotDividers != null)
            {
                profile.ActiveDividers = _snapshotDividers
                    .Where(div => div != null)
                    .Select(div => div.Copy())
                    .ToList();
            }
            else
            {
                profile.ActiveDividers = new List<PawnDivider>();
            }

            BWTLocalProfileStore.MarkDirty();
        }
    }
}
