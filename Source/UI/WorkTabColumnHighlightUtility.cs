using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer;
using RimWorld;

namespace Better_Work_Tab.UI
{
    internal static class WorkTabColumnHighlightUtility
    {
        internal static bool IsHighlightableWorkColumn(WorkTabLayoutColumn column)
        {
            return column.Column?.Worker is PawnColumnWorker_WorkPriority ||
                   column.IsExpandBesideChild ||
                   FluffyWorkTabGateway.IsFluffyColumn(column.Column);
        }
    }
}
