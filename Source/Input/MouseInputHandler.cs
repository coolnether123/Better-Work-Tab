using UnityEngine;
using Verse;

namespace Better_Work_Tab.Input
{
    /// <summary>
    /// Handles mouse input events with proper priority for drag operations.
    /// Drag controllers get first priority, then other interactions.
    /// </summary>
    public class MouseInputHandler
    {
        private readonly Selection.PawnSelectionManager _selectionManager;
        private readonly Sorting.ColumnSortManager _sortManager;
        private readonly ColumnManagement.ColumnResizeHandler _resizeHandler;
        private readonly RowManagement.RowClickHandler _rowClickHandler;
        private readonly ContextMenu.RowContextMenuManager _rowContextMenu;
        private readonly ContextMenu.HeaderContextMenuManager _headerContextMenu;

        // References to existing drag controllers
        private readonly DragDrop.RowDragController _rowDragController;
        private readonly DragDrop.ColumnDragController _columnDragController;

        public MouseInputHandler(
            Selection.PawnSelectionManager selectionManager,
            Sorting.ColumnSortManager sortManager,
            ColumnManagement.ColumnResizeHandler resizeHandler,
            RowManagement.RowClickHandler rowClickHandler,
            ContextMenu.RowContextMenuManager rowContextMenu,
            ContextMenu.HeaderContextMenuManager headerContextMenu,
            DragDrop.RowDragController rowDragController,
            DragDrop.ColumnDragController columnDragController)
        {
            _selectionManager = selectionManager;
            _sortManager = sortManager;
            _resizeHandler = resizeHandler;
            _rowClickHandler = rowClickHandler;
            _rowContextMenu = rowContextMenu;
            _headerContextMenu = headerContextMenu;
            _rowDragController = rowDragController;
            _columnDragController = columnDragController;
        }

        public bool HandleInput(InputState input, PawnOrganizer.API.IWorkTabLayoutController layout)
        {
            if (layout == null) return false;

            // PRIORITY 0: Check if drag controllers are active (they handle their own input)
            if (_rowDragController?.IsDragging == true || _columnDragController?.IsDragging == true)
            {
                return false; // Let drag controllers handle everything
            }

            // Don't intercept Ctrl or Shift held events - let drag system see them
            if (input.Control || input.Shift)
            {
                return false; // Let existing drag system handle modifier key interactions
            }

            bool handled = false;

            // Priority 1: Handle resize (captures all input when active)
            if (input.IsMouseDown || input.IsMouseDrag || input.IsMouseUp)
            {
                handled |= _resizeHandler.HandleInput(input, layout);
                if (handled) return true;
            }

            // Priority 2: Header clicks (sorting, context menu)
            if (layout.TryGetColumnAt(input.MousePosition, out var column))
            {
                if (input.IsRightClick)
                {
                    handled |= _headerContextMenu.ShowMenu(input, column, layout);
                }
                else if (input.IsLeftClick && !input.Control && !input.Shift)
                {
                    // Only handle sort if no modifiers (let drag system handle Ctrl+click)
                    handled |= _sortManager.HandleHeaderClick(input, column, layout);
                }
            }

            // Priority 3: Row clicks (selection, context menu)
            if (!handled && layout.TryGetRowAt(input.MousePosition, out var row))
            {
                if (input.IsRightClick)
                {
                    handled |= _rowContextMenu.ShowMenu(input, row, layout);
                }
                else if (input.IsLeftClick && !input.Control && !input.Shift)
                {
                    // Only handle normal clicks (let drag system handle Ctrl+click)
                    handled |= _rowClickHandler.HandleClick(input, row, layout);
                }
            }

            return handled;
        }
    }
}
