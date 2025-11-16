using UnityEngine;
using Verse;

namespace Better_Work_Tab.Input
{
    /// <summary>
    /// Central coordinator for all input handling.
    /// Delegates to specialized handlers and ensures proper event consumption.
    /// </summary>
    public class InputManager
    {
        private readonly MouseInputHandler _mouseHandler;
        private readonly KeyboardInputHandler _keyboardHandler;

        public InputManager(
            Selection.PawnSelectionManager selectionManager,
            Sorting.ColumnSortManager sortManager,
            ColumnManagement.ColumnResizeHandler resizeHandler,
            RowManagement.RowClickHandler rowClickHandler,
            RowManagement.RowNavigationHandler navigationHandler,
            ColumnManagement.ColumnVisibilityManager visibilityManager,
            ContextMenu.RowContextMenuManager rowContextMenu,
            ContextMenu.HeaderContextMenuManager headerContextMenu,
            DragDrop.RowDragController rowDragController,
            DragDrop.ColumnDragController columnDragController)
        {
            _mouseHandler = new MouseInputHandler(
                selectionManager,
                sortManager,
                resizeHandler,
                rowClickHandler,
                rowContextMenu,
                headerContextMenu,
                rowDragController,
                columnDragController);

            _keyboardHandler = new KeyboardInputHandler(
                navigationHandler,
                visibilityManager);
        }

        /// <summary>
        /// Process input. Returns true only if event should be consumed.
        /// Does NOT consume events when drag controllers or modifier keys are active.
        /// </summary>
        public bool ProcessInput(Event evt, PawnOrganizer.API.IWorkTabLayoutController layout)
        {
            if (evt == null || layout == null) return false;

            var input = new InputState(evt);
            bool handled = false;

            // Process keyboard input (doesn't interfere with drag)
            if (input.IsKeyDown)
            {
                handled = _keyboardHandler.HandleInput(input, layout);
            }

            // Process mouse input (respects drag priority)
            if (input.IsMouseDown || input.IsMouseUp || input.IsMouseDrag || input.IsScroll)
            {
                handled |= _mouseHandler.HandleInput(input, layout);
            }

            // Only consume if we actually handled it AND drag isn't active
            if (handled)
            {
                evt.Use();
            }

            return handled;
        }
    }
}
