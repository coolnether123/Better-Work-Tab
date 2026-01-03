using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;
using Verse.Sound;

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
            // We use MouseUp for sorting to distinguish between a click and the start of a drag.
            // If a drag starts, the drag handler will consume the MouseUp event, preventing sorting.
            if (evt.type == EventType.MouseDown)
            {
                if (evt.button == 0 || evt.button == 1)
                {
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
            // Reconstruct the vanilla tooltip but for the specific work type
            string text = worker.def.LabelCap;
            if (!worker.def.headerTip.NullOrEmpty())
            {
                text = text + "\n\n" + worker.def.headerTip;
            }
            // Add instructions
            text += "\n\n" + "ClickToSortByThisColumn".Translate();
            text += "\n" + "RightClickToOpenOptions".Translate();
            
            return text;
        }

        private static void HandleLeftClick(PawnColumnWorker_WorkPriority worker, PawnTable table, Event evt)
        {
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
            // Open column options menu (vanilla-like)
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            
            options.Add(new FloatMenuOption("SortDescending".Translate(), () => 
            {
                table.SortBy(worker.def, true);
                table.SetDirty();
            }));

            options.Add(new FloatMenuOption("SortAscending".Translate(), () => 
            {
                table.SortBy(worker.def, false);
                table.SetDirty();
            }));

            if (table.SortingBy != null)
            {
                options.Add(new FloatMenuOption("Clear sorting", () =>
                {
                    table.SortBy(null, false);
                    table.SetDirty();
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
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