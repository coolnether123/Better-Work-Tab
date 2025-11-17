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
    /// Thin orchestrator that wires the layout controller to drag handlers.
    /// </summary>
    public sealed class PawnOrganizerSystem
    {
        private static PawnOrganizerSystem _instance;
        public static PawnOrganizerSystem Instance => _instance;

        private readonly WorkTabLayoutController _layoutController;
        private readonly RowDragController _rowDragController;
        private readonly ColumnDragController _columnDragController;

        public IWorkTabLayoutController Layout => _layoutController;
        public bool IsDraggingRow => _rowDragController?.IsDragging ?? false;

        public PawnOrganizerSystem(IColumnWidthStore columnWidthStore)
        {
            _instance = this;
            _layoutController = new WorkTabLayoutController(columnWidthStore);
            _rowDragController = new RowDragController(_layoutController);
            _columnDragController = new ColumnDragController(_layoutController);
        }

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

            try
            {
                _layoutController.Rebuild(table, snapshot, tableOrigin);
            }
            catch (Exception ex)
            {
                Log.Error($"[BWT] PawnOrganizerSystem.Update failed: {ex}");
            }
        }

        public void HandleInput(Event evt)
        {
            if (_layoutController == null || evt == null)
            {
                return;
            }

            try
            {
                _columnDragController.HandleInput(evt);
                _rowDragController.HandleInput(evt);
            }
            catch (Exception ex)
            {
                Log.Error($"[BWT] PawnOrganizerSystem.HandleInput failed: {ex}");
            }
        }

        public void DrawDragOverlays()
        {
            if (_layoutController == null)
            {
                return;
            }

            try
            {
                _columnDragController.DrawOverlay(_layoutController);
                _rowDragController.DrawOverlay(_layoutController);
            }
            catch (Exception ex)
            {
                Log.Error($"[BWT] PawnOrganizerSystem.DrawDragOverlays failed: {ex}");
            }
        }

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
