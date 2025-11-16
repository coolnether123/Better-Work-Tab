using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.Persistence;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Better_Work_Tab.ColumnManagement
{
    /// <summary>
    /// Manages showing/hiding columns.
    /// Single responsibility: Column visibility toggling.
    /// </summary>
    public class ColumnVisibilityManager
    {
        private readonly ColumnStateManager _stateManager;

        public ColumnVisibilityManager(ColumnStateManager stateManager)
        {
            _stateManager = stateManager;
        }

        public bool ToggleVisibility(IWorkTabLayoutController layout)
        {
            // Show a menu to toggle column visibility
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (var column in layout.Columns)
            {
                if (column.Column?.Worker is PawnColumnWorker_WorkPriority)
                {
                    bool isVisible = _stateManager.IsColumnVisible(column.Column);
                    string label = (isVisible ? "☑ " : "☐ ") + column.Column.LabelCap;

                    options.Add(new FloatMenuOption(label, () =>
                    {
                        _stateManager.ToggleColumnVisibility(column.Column);
                        layout.Rebuild(layout.Table, 
                            Verse.Current.Game.GetComponent<Features.Workloads.GameComponent_BWTWorldSettings>()?.CurrentWorklist,
                            layout.TableOrigin);
                    }));
                }
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
                return true;
            }

            return false;
        }

        public bool IsVisible(PawnColumnDef column)
        {
            return _stateManager.IsColumnVisible(column);
        }
    }
}
