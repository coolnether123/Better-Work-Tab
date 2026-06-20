using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;
using Verse.Sound;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.Features.RaisedPriorityMaximum;

namespace Better_Work_Tab.UI.Headers.Angled
{
    /// <summary>
    /// Handles mouse interactions (clicks, tooltips) for complex header shapes.
    /// This logic is shared between angled and vanilla headers via the IHeaderRenderer abstraction.
    /// </summary>
    /// <summary>
    /// Encapsulates the context required for header interactions to reduce parameter count.
    /// </summary>
    public struct HeaderInteractionContext
    {
        public PawnColumnWorker_WorkPriority Worker;
        public PawnTable Table;
        public AngledLabelDrawer.AngledLabelLayout Layout;
        public Rect Bounds;
        public Vector2[] Quad;
        public bool IsMouseOver;
        public bool ShouldDraw;
        public Rect HeaderRect;
        public IHeaderRenderer Renderer;
    }

    public static class AngledHeaderInteraction
    {
        private static PawnColumnDef _columnSuppressingClicks = null;
        private static PawnColumnDef _pendingClickColumn = null;

        /// <summary>
        /// Orchestrates the drawing and interaction logic for a header.
        /// </summary>
        public static void HandleInteractions(HeaderInteractionContext ctx)
        {
            var evt = Event.current;
            var workType = ctx.Worker.def.workType;

            // Handle Repaint
            bool isSorted = ctx.Table.SortingBy == ctx.Worker.def;
            bool sortDescending = ctx.Table.SortingDescending;

            if (ctx.ShouldDraw)
            {
                ctx.Renderer.DrawHeader(ctx.Layout, ctx.IsMouseOver, isSorted, sortDescending, ctx.HeaderRect, ctx.Worker.def, ctx.Layout.ShowMarker);
            }

            // Early exit for interaction if not over the header or event is irrelevant
            if (!ctx.IsMouseOver || !HeaderUtility.ShouldHandleHeader(evt.type))
            {
                // If the mouse left the header, clear any pending click for this column
                if (evt.type == EventType.Repaint && !ctx.IsMouseOver && _pendingClickColumn == ctx.Worker.def)
                {
                    _pendingClickColumn = null;
                }
                return;
            }

            // --- Tooltips ---
            if (evt.type == EventType.Repaint)
            {
                string tooltip = GetTooltip(ctx.Worker, ctx.Table);
                if (!tooltip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(ctx.Bounds, tooltip);
                }
            }

            // --- Click Interactions ---
            // MouseUp is utilized for sorting to differentiate between a standard click and the initiation of a drag operation.
            // If a drag commences, the drag handler consumes the MouseUp event, preventing sorting.
            if (evt.type == EventType.MouseDown)
            {
                // Ctrl + Right Click: Open WorkGiver sub-menu (global, header-based)
                if (evt.button == 1 && evt.control)
                {
                    Vector2 screenPos = Verse.UI.GUIToScreenPoint(evt.mousePosition) / Prefs.UIScale;
                    Find.WindowStack.Add(new Window_WorkGiverSubMenu(workType, screenPos, pawn: null));
                    evt.Use();
                    return;
                }

                if (evt.button == 0 || evt.button == 1)
                {
                    // Handle Shift+Click immediately on MouseDown to prevent it from reaching sorting (on MouseUp) or dragging.
                    if (evt.shift || (evt.modifiers & EventModifiers.Shift) != 0)
                    {
                        HandleShiftClick(ctx.Worker, ctx.Table, evt.button);
                        evt.Use();
                        return;
                    }

                    // Handle Multi-Selection (Ctrl+Click) immediately on MouseDown if dragging isn't starting
                    if (evt.control && BetterWorkTabMod.Settings.enableColumnGrouping)
                    {
                        Better_Work_Tab.DragDrop.ColumnSelectionManager.ToggleSelection(ctx.Worker.def);
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        evt.Use();
                        return;
                    }

                    _pendingClickColumn = ctx.Worker.def;
                }
            }
            else if (evt.type == EventType.MouseUp)
            {
                // Only trigger if we released on the same column we pressed down on
                if (_pendingClickColumn != ctx.Worker.def)
                {
                    return;
                }
                _pendingClickColumn = null;

                // Suppress click if this column is currently being dragged or just finished dragging
                if (_columnSuppressingClicks == ctx.Worker.def)
                {
                    return;
                }

                if (evt.button == 0) // Left click
                {
                    HandleLeftClick(ctx.Worker, ctx.Table, evt);
                    evt.Use();
                }
                else if (evt.button == 1) // Right click
                {
                    HandleRightClick(ctx.Worker, ctx.Table, evt);
                    evt.Use();
                }
            }
        }

        private static string GetTooltip(PawnColumnWorker_WorkPriority worker, PawnTable table)
        {
            // Replicate vanilla GetHeaderTip from PawnColumnWorker_WorkPriority
            var workType = worker.def.workType;
            
            TaggedString tooltip = workType.gerundLabel.CapitalizeFirst().Colorize(ColoredText.TipSectionTitleColor) 
                + "\n\n" + workType.description 
                + "\n\n" + SpecificWorkListString(workType) 
                + "\n";
            
            if (worker.def.sortable)
            {
                tooltip += "\n" + "ClickToSortByThisColumn".Translate().Colorize(ColoredText.SubtleGrayColor);
            }
            
            if (!Verse.Steam.SteamDeck.IsSteamDeckInNonKeyboardMode)
            {
                if (Find.PlaySettings.useWorkPriorities)
                {
                    tooltip += "\n" + "WorkPriorityShiftClickTip".Translate().Colorize(ColoredText.SubtleGrayColor);
                }
                else
                {
                    tooltip += "\n" + "WorkPriorityShiftClickEnableDisableTip".Translate().Colorize(ColoredText.SubtleGrayColor);
                }
            }
            
            return tooltip.Resolve();
        }
        
        /// <summary>
        /// Builds the list of specific work givers for the work type.
        /// Exact implementation from vanilla PawnColumnWorker_WorkPriority.
        /// </summary>
        private static string SpecificWorkListString(WorkTypeDef def)
        {
            System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder();
            for (int i = 0; i < def.workGiversByPriority.Count; i++)
            {
                stringBuilder.Append(" - " + def.workGiversByPriority[i].LabelCap);
                if (def.workGiversByPriority[i].emergency)
                {
                    stringBuilder.Append(" (" + "EmergencyWorkMarker".Translate() + ")");
                }
                if (i < def.workGiversByPriority.Count - 1)
                {
                    stringBuilder.AppendLine();
                }
            }
            return stringBuilder.ToString();
        }

        private static void HandleLeftClick(PawnColumnWorker_WorkPriority worker, PawnTable table, Event evt)
        {
            // Normal Left Click: Clear selection and handle sorting
            if (Better_Work_Tab.DragDrop.ColumnSelectionManager.HasSelection)
            {
                Better_Work_Tab.DragDrop.ColumnSelectionManager.Clear();
            }

            // Standard Vanilla behavior: Sort by this column (3-state cycle)
            if (table.SortingBy != worker.def)
            {
                // State 1: Sort Ascending
                table.SortBy(worker.def, false);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            else if (!table.SortingDescending)
            {
                // State 2: Sort Descending
                table.SortBy(worker.def, true);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            else
            {
                // State 3: Clear Sorting (Return to vanilla/default)
                table.SortBy(null, false);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            
            table.SetDirty();
        }

        private static void HandleRightClick(PawnColumnWorker_WorkPriority worker, PawnTable table, Event evt)
        {
            // Vanilla behavior: Right-click sorts descending immediately
            table.SortBy(worker.def, true);
            table.SetDirty();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static void HandleShiftClick(PawnColumnWorker_WorkPriority worker, PawnTable table, int button)
        {
            var workType = worker.def.workType;
            List<Pawn> pawns = table.PawnsListForReading;
            bool useWorkPriorities = Find.PlaySettings.useWorkPriorities;

            bool changed = false;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(workType))
                    continue;

                int curPriority = pawn.workSettings.GetPriority(workType);

                if (useWorkPriorities)
                {
                    int maxPriority = BetterWorkTabMod.Settings.EffectiveMaxPriority;
                    // Manual priorities cycle through the configured range.
                    if (button == 0) // Left click (Increase priority / Decrement number)
                    {
                        // Cycle: 0 -> max -> ... -> 1 (stays at 1)
                        if (curPriority == 0) pawn.workSettings.SetPriority(workType, maxPriority);
                        else if (curPriority > 1) pawn.workSettings.SetPriority(workType, curPriority - 1);
                    }
                    else // Right click (Decrease priority / Increment number)
                    {
                        // Cycle: 1 -> ... -> max -> 0 (stays at 0)
                        if (curPriority == maxPriority) pawn.workSettings.SetPriority(workType, 0);
                        else if (curPriority > 0) pawn.workSettings.SetPriority(workType, curPriority + 1);
                    }
                }
                else
                {
                    // Vanilla Priorities (On/Off)
                    if (button == 0) pawn.workSettings.SetPriority(workType, MaxPriorityLogic.GetDefaultEnabledPriority());
                    else pawn.workSettings.SetPriority(workType, 0);
                }
                changed = true;
            }

            if (changed)
            {
                if (useWorkPriorities)
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                else if (button == 0)
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                else
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();

                table.SetDirty();
            }
        }

        /// <summary>
        /// Signal that a column drag has started, to suppress normal click behavior.
        /// </summary>
        public static void NotifyColumnDragStarted(PawnColumnDef column)
        {
            _columnSuppressingClicks = column;
        }

        /// <summary>
        /// Clears any pending click state for a column.
        /// </summary>
        public static void ClearPendingHeaderClick(PawnColumnDef column)
        {
            if (_columnSuppressingClicks == column)
            {
                _columnSuppressingClicks = null;
            }
        }
    }
}
