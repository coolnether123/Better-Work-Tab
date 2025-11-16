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

        public PawnOrganizerSystem(IColumnWidthStore columnWidthStore)
        {
            _instance = this;
            _layoutController = new WorkTabLayoutController(columnWidthStore);
            _rowDragController = new RowDragController(_layoutController);
            _columnDragController = new ColumnDragController(_layoutController);
        }

        public void Update(PawnTable table, Vector2 tableOrigin, IPawnOrganizerSnapshot snapshot)
        {
            _layoutController.Rebuild(table, snapshot, tableOrigin);
        }

        public void HandleInput(Event evt)
        {
            _columnDragController.HandleInput(evt);
            _rowDragController.HandleInput(evt);
        }

        public void DrawDragOverlays()
        {
            _columnDragController.DrawOverlay(_layoutController);
            _rowDragController.DrawOverlay(_layoutController);
        }

        public void SetPawnBackgroundColor(Pawn pawn, Color color)
        {
            API.PawnColorDatabase.SetColor(pawn, color);
        }
    }
}
