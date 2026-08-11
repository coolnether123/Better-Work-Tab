using System;
using System.Collections.Generic;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI.Settings;
using RimWorld;
using Verse;

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

        /// <summary>
        /// Resolves the real Work column used by settings previews. Keeping target selection here
        /// lets the header and body invoke their normal highlight renderers for the same column.
        /// </summary>
        internal static bool TryGetSettingsPreviewColumn(
            IReadOnlyList<WorkTabLayoutColumn> columns,
            out WorkTabLayoutColumn previewColumn)
        {
            previewColumn = default;
            if (columns == null ||
                !WorkTabColorPreviewController.Instance.TryGetPreview(out WorkTabColorPreview preview) ||
                !preview.IncludesColumn)
            {
                return false;
            }

            bool foundFallback = false;
            WorkTypeDef firefighter = WorkTypeDefOf.Firefighter;
            for (int i = 0; i < columns.Count; i++)
            {
                WorkTabLayoutColumn candidate = columns[i];
                if (!IsHighlightableWorkColumn(candidate))
                {
                    continue;
                }

                if (!foundFallback)
                {
                    previewColumn = candidate;
                    foundFallback = true;
                }

                WorkTypeDef workType = candidate.Column?.workType;
                if (workType == firefighter ||
                    string.Equals(workType?.defName, "Firefighter", StringComparison.OrdinalIgnoreCase))
                {
                    previewColumn = candidate;
                    return true;
                }
            }

            return foundFallback;
        }
    }
}
