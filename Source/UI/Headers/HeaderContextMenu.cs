using System.Collections.Generic;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGiverReassignments;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.Headers
{
    internal static class HeaderContextMenu
    {
        internal static void ShowForWorkType(PawnColumnWorker_WorkPriority worker, PawnTable table)
        {
            WorkTypeDef workType = worker?.def?.workType;
            if (workType == null)
            {
                return;
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("BWT_HeaderMenu_SortDescending".Translate(), () => SortDescending(worker, table))
            };

            if (CustomLabelStore.CustomLabelsEnabled)
            {
                options.Add(new FloatMenuOption("BWT_HeaderMenu_Rename".Translate(), () => OpenRenameDialog(workType)));
            }

            if (CustomLabelStore.CustomLabelsEnabled && CustomLabelStore.HasCustomLabel(workType))
            {
                options.Add(new FloatMenuOption("BWT_HeaderMenu_ResetName".Translate(), () =>
                {
                    CustomLabelStore.SetWorkTypeLabel(workType, null);
                    NotifyLabelsChanged();
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        internal static void ShowForWorkGiver(PawnColumnWorker_WorkPriority worker, PawnTable table, WorkGiverDef workGiver)
        {
            if (workGiver == null)
            {
                return;
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("BWT_HeaderMenu_SortDescending".Translate(), () => SortDescending(worker, table))
            };

            if (CustomLabelStore.CustomLabelsEnabled)
            {
                options.Add(new FloatMenuOption("BWT_HeaderMenu_Rename".Translate(), () => OpenRenameDialog(workGiver)));
            }

            if (CustomLabelStore.CustomLabelsEnabled && CustomLabelStore.HasCustomLabel(workGiver))
            {
                options.Add(new FloatMenuOption("BWT_HeaderMenu_ResetName".Translate(), () =>
                {
                    CustomLabelStore.SetWorkGiverLabel(workGiver, null);
                    NotifyLabelsChanged();
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void OpenRenameDialog(WorkTypeDef workType)
        {
            Find.WindowStack.Add(new Dialog_RenameGeneric(
                WorkTypeDisplayNameService.RenameDialogLabel(workType),
                label =>
                {
                    CustomLabelStore.SetWorkTypeLabel(workType, label);
                    NotifyLabelsChanged();
                }));
        }

        private static void OpenRenameDialog(WorkGiverDef workGiver)
        {
            Find.WindowStack.Add(new Dialog_RenameGeneric(
                WorkGiverDisplayNameService.RenameDialogLabel(workGiver),
                label =>
                {
                    CustomLabelStore.SetWorkGiverLabel(workGiver, label);
                    NotifyLabelsChanged();
                }));
        }

        private static void SortDescending(PawnColumnWorker_WorkPriority worker, PawnTable table)
        {
            if (worker?.def == null || table == null)
            {
                return;
            }

            table.SortBy(worker.def, true);
            table.SetDirty();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static void NotifyLabelsChanged()
        {
            Spine.RimWorld.WorkTab.Rendering.WorkTabInvalidationHub.Invalidate(Spine.RimWorld.WorkTab.Rendering.WorkTabDirtyFlags.HeaderText | Spine.RimWorld.WorkTab.Rendering.WorkTabDirtyFlags.HeaderGeometry | Spine.RimWorld.WorkTab.Rendering.WorkTabDirtyFlags.RenderResources);
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }
    }
}
