using Better_Work_Tab.Features;
using Better_Work_Tab.DragDrop;
using HarmonyLib;
using RimWorld;
using Spine.DragDropApi.Util;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    public static class AngledHeaderInteraction
    {
        private static readonly ClickOrDragGate<PawnColumnDef> ClickTracker = new ClickOrDragGate<PawnColumnDef>();
        private static MethodInfo _baseHeaderClicked;

        public static void HandleInteractions(PawnColumnWorker_WorkPriority worker, PawnTable table, AngledLabelDrawer.AngledLabelLayout layout, Rect bounds, Vector2[] quad, bool isMouseOver, bool shouldDraw, Rect headerRect)
        {
            if (shouldDraw) AngledLabelDrawer.Draw(layout, isMouseOver, table?.SortingBy == worker?.def, table?.SortingDescending ?? false, headerRect, worker?.def);
            if (isMouseOver) TooltipHandler.TipRegion(bounds, AngledHeaderCache.GetTooltip(worker));

            var evt = Event.current;
            if (evt == null) return;

            bool hit = isMouseOver || (headerRect != default && headerRect.Contains(evt.mousePosition));
            if (!hit) return;

            if (evt.type == EventType.MouseDown && evt.shift)
            {
                HandleShiftClick(worker, table, evt.button);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDown && evt.control && BetterWorkTabMod.Settings.enableColumnGrouping)
            {
                ColumnSelectionManager.ToggleSelection(worker.def);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDown)
            {
                ClickTracker.Begin(worker.def, evt.button, evt.mousePosition);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag)
            {
                ClickTracker.RegisterDrag(worker.def, evt.mousePosition, 5f);
            }
            else if (evt.type == EventType.MouseUp)
            {
                if (ClickTracker.TryComplete(worker.def, evt.button, true))
                {
                    _baseHeaderClicked ??= AccessTools.Method(typeof(PawnColumnWorker), "HeaderClicked");
                    _baseHeaderClicked?.Invoke(worker, new object[] { bounds, table });
                }
                evt.Use();
            }
        }

        private static void HandleShiftClick(PawnColumnWorker_WorkPriority worker, PawnTable table, int button)
        {
            var pawns = table?.PawnsListForReading;
            if (pawns == null) return;
            foreach (var p in pawns)
            {
                if (p.workSettings == null || p.WorkTypeIsDisabled(worker.def.workType)) continue;
                int cur = p.workSettings.GetPriority(worker.def.workType);
                if (Find.PlaySettings.useWorkPriorities)
                {
                    int next = (button == 0) ? (cur - 1 < 0 ? 4 : cur - 1) : (cur + 1 > 4 ? 0 : cur + 1);
                    p.workSettings.SetPriority(worker.def.workType, next);
                }
                else
                {
                    p.workSettings.SetPriority(worker.def.workType, (button == 0) ? 3 : 0);
                }
            }
        }

        internal static void NotifyColumnDragStarted(PawnColumnDef col) => ClickTracker.MarkDragStarted(col);
        internal static void ClearPendingHeaderClick(PawnColumnDef col) => ClickTracker.ClearIfTracking(col);
    }
}