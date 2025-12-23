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
        private static PawnColumnDef _pendingCtrlDeselect;

        public static void HandleInteractions(PawnColumnWorker_WorkPriority worker, PawnTable table, AngledLabelDrawer.AngledLabelLayout layout, Rect bounds, Vector2[] quad, bool isMouseOver, bool shouldDraw, Rect headerRect)
        {
            if (shouldDraw) AngledLabelDrawer.Draw(layout, isMouseOver, table?.SortingBy == worker?.def, table?.SortingDescending ?? false, headerRect, worker?.def);
            if (isMouseOver) TooltipHandler.TipRegion(bounds, AngledHeaderCache.GetTooltip(worker));

            var evt = Event.current;
            if (evt == null) return;

            bool hit = isMouseOver || (headerRect != default && headerRect.Contains(evt.mousePosition));
            if (!hit) return;

            if (evt.type == EventType.MouseDown && evt.button == 1 && evt.control)
            {
                // Convert GUI coordinates to logical UI space (account for scale and groups)
                Vector2 localPos = new Vector2(headerRect.center.x, headerRect.y);
                Vector2 screenPos = Verse.UI.GUIToScreenPoint(localPos) / Prefs.UIScale;
                
                Find.WindowStack.Add(new UI.WorkGiverReassignments.Window_WorkGiverSubMenu(worker.def.workType, screenPos));
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseDown && evt.shift)
            {
                BetterWorkTabMod.DebugLog($"[BWT] Shift-Click on {worker.def.defName}", DebugFeature.DragDrop);
                HandleShiftClick(worker, table, evt.button);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDown && evt.control && BetterWorkTabMod.Settings.enableColumnGrouping)
            {
                // If not selected, select immediately so we can drag the selection
                if (!ColumnSelectionManager.IsSelected(worker.def))
                {
                    ColumnSelectionManager.ToggleSelection(worker.def);
                    BetterWorkTabMod.DebugLog($"[BWT] Ctrl-Click select on {worker.def.defName}", DebugFeature.DragDrop);
                    _pendingCtrlDeselect = null;
                }
                else
                {
                    // Already selected - wait for MouseUp to deselect, so we can drag if desired
                    _pendingCtrlDeselect = worker.def;
                    BetterWorkTabMod.DebugLog($"[BWT] Ctrl-Click on ALREADY selected {worker.def.defName}. Delaying potential deselect.", DebugFeature.DragDrop);
                }
            }
            
            if (evt.type == EventType.MouseDown)
            {
                BetterWorkTabMod.DebugLog($"[BWT] MouseDown on {worker.def.defName}. Pending drag start.", DebugFeature.DragDrop);
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
                    if (evt.control && BetterWorkTabMod.Settings.enableColumnGrouping)
                    {
                        if (_pendingCtrlDeselect == worker.def)
                        {
                            ColumnSelectionManager.ToggleSelection(worker.def);
                            BetterWorkTabMod.DebugLog($"[BWT] Ctrl-Click deselect on {worker.def.defName}", DebugFeature.DragDrop);
                        }
                        // Sort is prevented when Ctrl is held for selection
                    }
                    else
                    {
                        _baseHeaderClicked ??= AccessTools.Method(typeof(PawnColumnWorker), "HeaderClicked");
                        _baseHeaderClicked?.Invoke(worker, new object[] { bounds, table });
                    }
                }
                _pendingCtrlDeselect = null;
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
