using System;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.Features;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer
{
    /// <summary>
    /// Central orchestrator for Work Tab layout and drag-drop interactions.
    /// Manages the layout controller and initiates specific drag handlers for rows/columns.
    /// </summary>
    public sealed class PawnOrganizerSystem
    {
        private static PawnOrganizerSystem _instance;
        public static PawnOrganizerSystem Instance => _instance;

        private readonly WorkTabLayoutController _layoutController;

        // Active drag handlers
        private RowDragHandler _activeRowDrag;
        private ColumnDragHandler _activeColumnDrag;

        // Pending drag detection state - for threshold-based drag initiation
        private bool _hasPendingDrag;
        private Vector2 _pendingStartMouse;
        private object _pendingDragTarget; // WorkTabLayoutRow or WorkTabLayoutColumn

        public IWorkTabLayoutController Layout => _layoutController;

        /// <summary>
        /// Returns true if any drag operation is currently active (row or column).
        /// </summary>
        public bool IsDragging => _activeRowDrag != null || _activeColumnDrag != null;

        /// <summary>
        /// Returns true if a row (pawn or divider) is currently being dragged.
        /// </summary>
        public bool IsDraggingRow => _activeRowDrag != null;

        /// <summary>
        /// Returns true if a column is currently being dragged.
        /// </summary>
        public bool IsDraggingColumn => _activeColumnDrag != null;

        public PawnOrganizerSystem(IColumnWidthStore columnWidthStore)
        {
            _instance = this;
            _layoutController = new WorkTabLayoutController(columnWidthStore);
        }

        /// <summary>
        /// Updates the layout controller with the current PawnTable snapshot.
        /// Must be called before drawing or processing input.
        /// </summary>
        public void Update(PawnTable table, Vector2 tableOrigin, IPawnOrganizerSnapshot snapshot)
        {
            if (_layoutController == null)
            {
                Log.Error("[BWT] PawnOrganizerSystem.Update aborted: layout controller missing.");
                return;
            }

            if (table == null)
            {
                Log.Error("[BWT] PawnOrganizerSystem.Update aborted: table is null.");
                return;
            }

            if (snapshot == null)
            {
                Log.Error("[BWT] PawnOrganizerSystem.Update aborted: snapshot is null.");
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
        /// Main input handler for drag-drop. Detects new drags and delegates active drags
        /// to the appropriate row/column drag handler.
        /// </summary>
        public void HandleInput(Event evt)
        {
            if (_layoutController == null || evt == null)
            {
                return;
            }

            try
            {
                // Phase 1: update active drag, if any
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

                // Phase 2: detect and start a new drag.
                HandleDragDetection(evt);
            }
            catch (Exception ex)
            {
                Log.Error($"[BWT] PawnOrganizerSystem.HandleInput failed: {ex}");
            }
        }

        /// <summary>
        /// Handles updates for an active row drag.
        /// </summary>
        private void HandleActiveRowDrag(Event evt)
        {
            if (_activeRowDrag == null)
                return;

            switch (evt.type)
            {
                case EventType.MouseDrag:
                    _activeRowDrag.OnDragUpdate(evt.mousePosition);
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

        /// <summary>
        /// Handles updates for an active column drag.
        /// </summary>
        private void HandleActiveColumnDrag(Event evt)
        {
            if (_activeColumnDrag == null)
                return;

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
        /// Detects mouse down, checks for Ctrl requirement, and tracks pending drag.
        /// When mouse moves far enough, initiates the actual drag via the appropriate handler.
        /// </summary>
        private void HandleDragDetection(Event evt)
        {
            bool requireCtrl = BetterWorkTabMod.Settings?.requireCtrlForDrag ?? true;
            bool ctrlSatisfied = !requireCtrl || evt.control;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button != 0) return;

                    if (!ctrlSatisfied) return;

                    // Check if mouse is over a column header
                    if (_layoutController.TryGetColumnAt(evt.mousePosition, out var column))
                    {
                        if (column.Column.Worker is PawnColumnWorker_WorkPriority)
                        {
                            _pendingDragTarget = column;
                            _hasPendingDrag = true;
                            _pendingStartMouse = evt.mousePosition;
                        }
                        return;
                    }

                    // Check if mouse is over a row
                    if (_layoutController.TryGetRowAt(evt.mousePosition, out var row))
                    {
                        _pendingDragTarget = row;
                        _hasPendingDrag = true;
                        _pendingStartMouse = evt.mousePosition;
                        return;
                    }

                    break;

                case EventType.MouseDrag:
                    if (!_hasPendingDrag) return;

                    // Check if we've moved far enough to initiate the drag
                    float dragDistance = (evt.mousePosition - _pendingStartMouse).magnitude;
                    if (dragDistance >= 5f)
                    {
                        InitiateDrag();
                        evt.Use();
                    }

                    break;

                case EventType.MouseUp:
                    _hasPendingDrag = false;
                    _pendingDragTarget = null;
                    break;

                case EventType.KeyDown:
                    if (evt.keyCode == KeyCode.Escape && _hasPendingDrag)
                    {
                        _hasPendingDrag = false;
                        _pendingDragTarget = null;
                        evt.Use();
                    }
                    break;
            }
        }

        /// <summary>
        /// Creates the appropriate handler and starts a drag operation
        /// once the drag threshold has been exceeded.
        /// </summary>
        private void InitiateDrag()
        {
            if (_pendingDragTarget is WorkTabLayoutColumn column)
            {
                _activeColumnDrag = new ColumnDragHandler(_layoutController, column);
            }
            else if (_pendingDragTarget is WorkTabLayoutRow row)
            {
                _activeRowDrag = new RowDragHandler(_layoutController, row, _pendingStartMouse);
            }

            _hasPendingDrag = false;
            _pendingDragTarget = null;
        }

        /// <summary>
        /// Draws all active drag overlays (ghosts, insertion lines) for
        /// the current row/column drag handlers.
        /// </summary>
        public void DrawDragOverlays()
        {
            if (_layoutController == null) return;

            try
            {
                _activeRowDrag?.OnDrawOverlay();
                _activeColumnDrag?.OnDrawOverlay();
            }
            catch (Exception ex)
            {
                Log.Error($"[BWT] PawnOrganizerSystem.DrawDragOverlays failed: {ex}");
            }
        }

        /// <summary>
        /// Sets a custom background color for a pawn.
        /// </summary>
        public void SetPawnBackgroundColor(Pawn pawn, Color color)
        {
            if (pawn == null)
            {
                Log.Warning("[BWT] Attempted to set background color for a null pawn.");
                return;
            }

            API.PawnColorDatabase.SetColor(pawn, color);
        }
    }
}
