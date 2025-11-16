using UnityEngine;
using Verse;

namespace Better_Work_Tab.Input
{
    /// <summary>
    /// Handles keyboard input events for navigation and shortcuts.
    /// Single responsibility: Process keyboard events only.
    /// </summary>
    public class KeyboardInputHandler
    {
        private readonly RowManagement.RowNavigationHandler _navigationHandler;
        private readonly ColumnManagement.ColumnVisibilityManager _visibilityManager;

        public KeyboardInputHandler(
            RowManagement.RowNavigationHandler navigationHandler,
            ColumnManagement.ColumnVisibilityManager visibilityManager)
        {
            _navigationHandler = navigationHandler;
            _visibilityManager = visibilityManager;
        }

        public bool HandleInput(InputState input, PawnOrganizer.API.IWorkTabLayoutController layout)
        {
            if (!input.IsKeyDown || layout == null) return false;

            bool handled = false;

            // Navigation (Up/Down arrows)
            if (input.KeyCode == KeyCode.UpArrow || input.KeyCode == KeyCode.DownArrow)
            {
                handled = _navigationHandler.NavigateRows(input, layout);
            }

            // Column visibility toggle (Ctrl+H example)
            if (input.Control && input.KeyCode == KeyCode.H)
            {
                handled = _visibilityManager.ToggleVisibility(layout);
            }

            return handled;
        }
    }
}
