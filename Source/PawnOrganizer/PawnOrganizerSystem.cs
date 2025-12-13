using Better_Work_Tab;
using Better_Work_Tab.Features;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer
{
    /// <summary>
    /// Central orchestrator for Work Tab layout and drag-drop interactions.
    /// Owns the layout controller, detects drags, and hands off to row/column drag handlers.
    /// Drag state always stores Pawn/Divider references instead of UI wrappers so it survives rebuilds.
    /// </summary>
    public sealed class PawnOrganizerSystem
    {
        private static PawnOrganizerSystem _instance;
        public static PawnOrganizerSystem Instance => _instance;

        private readonly WorkTabLayoutController _layoutController;
        
        // These are set when a drag is actually in progress (after threshold).
        private RowDragHandler _activeRowDrag;
        private ColumnDragHandler _activeColumnDrag;

        // These track potential drags before the mouse moves enough to start.
        // Store stable references (Pawn/Divider), not UI wrappers.
        private bool _hasPendingDrag;
        private Vector2 _pendingStartMouse;
        
        /// <summary>
        /// Pending pawn reference. Stable across layout rebuilds.
        /// </summary>
        private Pawn _pendingPawn;
        
        /// <summary>
        /// Pending divider reference. Stable across layout rebuilds.
        /// </summary>
        private PawnDivider _pendingDivider;
        
        /// <summary>
        /// Pending column (for column drags). Columns are looked up fresh when needed.
        /// </summary>
        private PawnColumnDef _pendingColumn;

        /// <summary>
        /// Minimum mouse movement before a drag starts.
        /// Prevents accidental drags from clicks.
        /// </summary>
        private float DragThreshold =>
            BetterWorkTabMod.Settings?.dragThreshold is float v && v > 0f ? v : 5f;

        public IWorkTabLayoutController Layout => _layoutController;
        
        /// <summary>
        /// True if any drag operation is currently active.
        /// Used by MainTabWindow to skip layout updates during drag.
        /// </summary>
        public bool IsDragging => _activeRowDrag != null || _activeColumnDrag != null;
        
        public bool IsDraggingRow => _activeRowDrag != null;
        public bool IsDraggingColumn => _activeColumnDrag != null;

        public PawnOrganizerSystem(IColumnWidthStore columnWidthStore)
        {
            _instance = this;
            _layoutController = new WorkTabLayoutController(columnWidthStore);
        }

        /// <summary>
        /// Updates the layout controller with the current snapshot.
        /// Should NOT be called while dragging (checked by caller).
        /// </summary>
        public void Update(PawnTable table, Vector2 tableOrigin, IPawnOrganizerSnapshot snapshot)
        {
            if (_layoutController == null || table == null || snapshot == null)
            {
                return;
            }

            try
            {
                _layoutController.Rebuild(table, snapshot, tableOrigin);
            }
            catch (Exception ex)
            {
                Log.Error($"[BWT] PawnOrganizerSystem.Update failed: {ex}");
            }
        }

        /// <summary>
        /// Main input handler. Routes to active drag or detection.
        /// </summary>
        public void HandleInput(Event evt)
        {
            if (_layoutController == null || evt == null) return;

            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.enableDragDropReordering ?? true))
            {
                return;
            }

            try
            {
                // If we have an active drag, let it handle input
                if (_activeRowDrag != null)
                {
                    HandleActiveRowDrag(evt);
                    return;
                }

                if (_activeColumnDrag != null)
                {
                    HandleActiveColumnDrag(evt);
                    return;
                }

                // Otherwise, detect new drags
                HandleDragDetection(evt);
            }
            catch (Exception ex)
            {
                Log.Error($"[BWT] PawnOrganizerSystem.HandleInput failed: {ex}");
                ClearAllDragState();
            }
        }

        private void HandleActiveRowDrag(Event evt)
        {
            switch (evt.type)
            {
                case EventType.MouseDrag:
                    _activeRowDrag.OnDragUpdate(evt.mousePosition);
                    
                    // Check if handler cancelled itself (e.g., pawn destroyed)
                    if (!_activeRowDrag.IsDragging)
                    {
                        _activeRowDrag = null;
                    }
                    evt.Use();
                    break;

                case EventType.MouseUp:
                    _activeRowDrag.OnDrop();
                    _activeRowDrag = null;
                    evt.Use();
                    break;

                case EventType.KeyDown when evt.keyCode == KeyCode.Escape:
                    _activeRowDrag.OnCancel();
                    _activeRowDrag = null;
                    evt.Use();
                    break;
            }
        }

        private void HandleActiveColumnDrag(Event evt)
        {
            switch (evt.type)
            {
                case EventType.MouseDrag:
                    _activeColumnDrag.OnDragUpdate(evt.mousePosition);
                    evt.Use();
                    break;

                case EventType.MouseUp:
                    _activeColumnDrag.OnDrop();
                    _activeColumnDrag = null;
                    evt.Use();
                    break;

                case EventType.KeyDown when evt.keyCode == KeyCode.Escape:
                    _activeColumnDrag.OnCancel();
                    _activeColumnDrag = null;
                    evt.Use();
                    break;
            }
        }

        /// <summary>
        /// Detect potential drags and track pending state.
        /// 
        /// FLOW:
        /// 1. MouseDown on a row/column: store stable reference, set pending
        /// 2. MouseDrag past threshold: create handler, start drag
        /// 3. MouseUp before threshold: cancel pending (it was just a click)
        /// </summary>
        private void HandleDragDetection(Event evt)
        {
            var settings = BetterWorkTabMod.Settings;
            bool requireCtrl = BetterWorkTabMod.Settings?.requireCtrlForDrag ?? true;
            bool ctrlSatisfied = !requireCtrl || evt.control;
            bool allowRows = settings?.rowDraggingEnabled ?? true;
            bool allowColumns = settings?.columnDraggingEnabled ?? true;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button != 0) return;
                    if (!ctrlSatisfied) return;
                    
                    TryStartPendingDrag(evt.mousePosition, allowColumns, allowRows);
                    break;

                case EventType.MouseDrag:
                    if (!_hasPendingDrag) return;
                    
                    float distance = (evt.mousePosition - _pendingStartMouse).magnitude;
                    if (distance >= DragThreshold)
                    {
                        InitiateDrag();
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    ClearPendingDrag();
                    break;

                case EventType.KeyDown when evt.keyCode == KeyCode.Escape:
                    if (_hasPendingDrag)
                    {
                        ClearPendingDrag();
                        evt.Use();
                    }
                    break;
            }
        }

        /// <summary>
        /// Check if mouse is over a draggable item and start tracking.
        /// 
        /// IMPORTANT: We store Pawn/Divider references directly, not WorkTabLayoutRow.
        /// This ensures the pending drag survives any layout rebuilds between
        /// MouseDown and when the drag actually starts.
        /// </summary>
        private void TryStartPendingDrag(Vector2 mousePos, bool allowColumns, bool allowRows)
        {
            // Check for column drag first (headers are above rows)
            if (allowColumns && _layoutController.TryGetColumnAt(mousePos, out var column))
            {
                if (column.Column?.Worker is PawnColumnWorker_WorkPriority)
                {
                    _hasPendingDrag = true;
                    _pendingStartMouse = mousePos;
                    _pendingColumn = column.Column;
                    _pendingPawn = null;
                    _pendingDivider = null;
                }
                return;
            }

            // Check for row drag
            if (allowRows && _layoutController.TryGetVisibleRowAt(mousePos, out var row))
            {
                _hasPendingDrag = true;
                _pendingStartMouse = mousePos;
                _pendingColumn = null;
                
                // Store STABLE references, not the row wrapper
                _pendingPawn = row.Pawn;
                _pendingDivider = row.Divider;
            }
        }

        /// <summary>
        /// Create the appropriate drag handler and start the actual drag.
        /// 
        /// For rows: Look up the pawn/divider in the CURRENT layout to get fresh position data.
        /// This handles the case where layout rebuilt between MouseDown and InitiateDrag.
        /// </summary>
        private void InitiateDrag()
        {
            if (_pendingColumn != null)
            {
                InitiateColumnDrag();
            }
            else if (_pendingPawn != null)
            {
                InitiateRowDrag(_pendingPawn, null);
            }
            else if (_pendingDivider != null)
            {
                InitiateRowDrag(null, _pendingDivider);
            }

            ClearPendingDrag();
        }

        private void InitiateColumnDrag()
        {
            // Find the column in current layout
            if (!TryFindColumn(_pendingColumn, out var column))
            {
                BetterWorkTabMod.DebugLog(
                    $"Column drag cancelled: could not find column {_pendingColumn?.defName}",
                    DebugFeature.DragDrop);
                return;
            }

            _activeColumnDrag = new ColumnDragHandler(_layoutController, column);
        }

        private void InitiateRowDrag(Pawn pawn, PawnDivider divider)
        {
            // Find the row in the CURRENT layout using stable reference
            if (!TryFindRow(pawn, divider, out var row))
            {
                string itemDesc = pawn != null 
                    ? $"Pawn '{pawn.LabelShortCap}'" 
                    : $"Divider '{divider?.DividerName}'";
                    
                BetterWorkTabMod.DebugLog(
                    $"Row drag cancelled: {itemDesc} not found in current layout.",
                    DebugFeature.DragDrop);
                return;
            }

            // Create drag session with stable references and current position data
            var rect = _layoutController.GetScreenRect(row);
            RowDragSession session;
            
            if (pawn != null)
            {
                session = new RowDragSession(pawn, row.VisualIndex, _pendingStartMouse, rect);
            }
            else
            {
                session = new RowDragSession(divider, row.VisualIndex, _pendingStartMouse, rect);
            }

            _activeRowDrag = new RowDragHandler(_layoutController, session);
        }

        /// <summary>
        /// Find a row in the current layout by pawn or divider reference.
        /// </summary>
        private bool TryFindRow(Pawn pawn, PawnDivider divider, out WorkTabLayoutRow row)
        {
            row = default;
            var rows = _layoutController?.Rows;
            if (rows == null) return false;

            for (int i = 0; i < rows.Count; i++)
            {
                if (pawn != null && rows[i].Pawn == pawn)
                {
                    row = rows[i];
                    return true;
                }
                
                if (divider != null && rows[i].Divider == divider)
                {
                    row = rows[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Find a column in the current layout by column def.
        /// </summary>
        private bool TryFindColumn(PawnColumnDef columnDef, out WorkTabLayoutColumn column)
        {
            column = default;
            var columns = _layoutController?.Columns;
            if (columns == null || columnDef == null) return false;

            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].Column == columnDef)
                {
                    column = columns[i];
                    return true;
                }
            }

            return false;
        }

        private void ClearPendingDrag()
        {
            _hasPendingDrag = false;
            _pendingPawn = null;
            _pendingDivider = null;
            _pendingColumn = null;
        }

        private void ClearAllDragState()
        {
            ClearPendingDrag();
            _activeRowDrag = null;
            _activeColumnDrag = null;
        }

        public void DrawDragOverlays()
        {
            try
            {
                _activeRowDrag?.OnDrawOverlay();
                _activeColumnDrag?.OnDrawOverlay();
            }
            catch (Exception ex)
            {
                Log.Error($"[BWT] DrawDragOverlays failed: {ex}");
            }
        }

        /// <summary>
        /// Sets a custom background color for a pawn.
        /// </summary>
        public void SetPawnBackgroundColor(Pawn pawn, Color color)
        {
            if (pawn == null) return;
            API.PawnColorDatabase.SetColor(pawn, color);
        }
    }
}
