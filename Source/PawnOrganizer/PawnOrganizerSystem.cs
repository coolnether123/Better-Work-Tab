using Better_Work_Tab;
using Better_Work_Tab.Features;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers.Angled;
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
        private float DragThreshold
        {
            get
            {
                int v = BetterWorkTabMod.Settings?.dragThreshold ?? DefaultSettings.dragThreshold;
                return Mathf.Max(1f, v);
            }
        }

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

                // Detect new drags
                HandleDragDetection(evt);

                // === PRESENCE FEATURE DISABLED ===
                /*
                // Update presence (called from input context where syncing works)
                if (Mod_Support.Multiplayer.MultiplayerBridge.Active && 
                    (evt.type == EventType.MouseMove || evt.type == EventType.MouseDrag))
                {
                    Pawn pawn = null;
                    WorkTypeDef workType = null;
                    
                    if (_layoutController.TryGetVisibleRowAt(evt.mousePosition, out var row))
                        pawn = row.Pawn;
                    
                    if (_layoutController.TryGetColumnAt(evt.mousePosition, out var col))
                        workType = col.Column?.workType;
                    
                    Mod_Support.Multiplayer.Features.Presence.WorkTabPresenceRegistry.UpdatePresence(true, pawn, workType);
                }
                */

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
                    var completedDrag = _activeColumnDrag;
                    completedDrag?.OnDrop();
                    _activeColumnDrag = null;
                    Better_Work_Tab.UI.Headers.Angled.AngledHeaderInteraction.ClearPendingHeaderClick(completedDrag?.ColumnDef);
                    evt.Use();
                    break;

                case EventType.KeyDown when evt.keyCode == KeyCode.Escape:
                    var cancelledDrag = _activeColumnDrag;
                    cancelledDrag?.OnCancel();
                    _activeColumnDrag = null;
                    AngledHeaderInteraction.ClearPendingHeaderClick(cancelledDrag?.ColumnDef);
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
            AngledHeaderInteraction.NotifyColumnDragStarted(_pendingColumn);
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

        public void CancelActiveDrag()
        {
            _activeRowDrag?.OnCancel();
            _activeColumnDrag?.OnCancel();
            ClearAllDragState();
        }

        private void ClearAllDragState()
        {
            ClearPendingDrag();
            _activeRowDrag = null;
            _activeColumnDrag = null;
            BetterWorkTabLocalState.IsHeaderDragging = false;
        }

        public void DrawDragOverlays()
        {
            try
            {
                // === PRESENCE FEATURE DISABLED ===
                // Mod_Support.Multiplayer.Features.Presence.PresenceOverlay.Draw(Layout);
                
                _activeRowDrag?.OnDrawOverlay();
                _activeColumnDrag?.OnDrawOverlay();
                
                /*
                // Multiplayer presence: just record state, don't sync yet (we're in Draw context)
                if (Mod_Support.Multiplayer.MultiplayerBridge.Active)
                {
                    Pawn hoveredPawn = null;
                    WorkTypeDef hoveredWorkType = null;
                    
                    if (_layoutController != null)
                    {
                        var mousePos = Event.current.mousePosition;
                        
                        if (_layoutController.TryGetVisibleRowAt(mousePos, out var row))
                            hoveredPawn = row.Pawn;
                        
                        if (_layoutController.TryGetColumnAt(mousePos, out var col))
                            hoveredWorkType = col.Column?.workType;
                    }
                    
                    // Just record - don't sync yet (we're in Draw context)
                    Mod_Support.Multiplayer.Features.Presence.PresenceManager.RecordHoverState(true, hoveredPawn, hoveredWorkType);
                }
                */
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
            
#if !v1_2 && !v1_1
            if (Mod_Support.Multiplayer.MultiplayerBridge.Active)
            {
               Mod_Support.Multiplayer.Features.Layouts.LayoutSharingManager.NotifyLayoutChanged();
            }
#endif
        }
    }
}
