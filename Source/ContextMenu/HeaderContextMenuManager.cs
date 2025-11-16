using Better_Work_Tab.Input;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Better_Work_Tab.ContextMenu
{
    /// <summary>
    /// Manages right-click context menus for column headers.
    /// Single responsibility: Build and display header context menus.
    /// </summary>
    public class HeaderContextMenuManager
    {
        private readonly ColumnManagement.ColumnVisibilityManager _visibilityManager;
        private readonly Sorting.ColumnSortManager _sortManager;

        public HeaderContextMenuManager(
            ColumnManagement.ColumnVisibilityManager visibilityManager,
            Sorting.ColumnSortManager sortManager)
        {
            _visibilityManager = visibilityManager;
            _sortManager = sortManager;
        }

        public bool ShowMenu(InputState input, WorkTabLayoutColumn column, IWorkTabLayoutController layout)
        {
            if (column.Column == null) return false;

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // Sort ascending
            options.Add(new FloatMenuOption("Sort ascending", () =>
            {
                _sortManager.State.SetSort(column.Column, false);
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            }));

            // Sort descending
            options.Add(new FloatMenuOption("Sort descending", () =>
            {
                _sortManager.State.SetSort(column.Column, true);
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            }));

            // Clear sort
            if (_sortManager.State.SortColumn != null)
            {
                options.Add(new FloatMenuOption("Clear sort", () =>
                {
                    _sortManager.ClearSort();
                    MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                }));
            }



            // Reset column width
            options.Add(new FloatMenuOption("Reset column width", () =>
            {
                // Reset to default (would need to store default widths)
                VisualFeedback.AudioManager.PlayClick();
            }));

            Find.WindowStack.Add(new FloatMenu(options));
            return true;
        }
    }
}
