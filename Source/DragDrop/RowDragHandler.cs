using Better_Work_Tab;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using RimWorld;
using Spine.DragDropApi.Util;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Handles row (Pawn/Divider) dragging within the work tab.
    /// Uses RowDragSession so only stable Pawn/Divider references are stored; row wrappers can be rebuilt freely.
    /// </summary>
    public sealed class RowDragHandler
    {
        private readonly IWorkTabLayoutController _layout;
        private readonly RowDragSession _session;

        /// <summary>
        /// True while this drag operation is active.
        /// Set to false when dropped or cancelled.
        /// </summary>
        public bool IsDragging { get; private set; }

        /// <summary>
        /// Create a new row drag handler for an active drag session.
        /// </summary>
        /// <param name="layout">Layout controller for position calculations.</param>
        /// <param name="session">The drag session containing stable references.</param>
        public RowDragHandler(IWorkTabLayoutController layout, RowDragSession session)
        {
            _layout = layout;
            _session = session;
            IsDragging = true;

            BetterWorkTabMod.DebugLog(
                $"Row drag started for {_session.Describe()} at visual index {_session.StartVisualIndex}.",
                DebugFeature.DragDrop);
        }

        /// <summary>
        /// Update the target insertion index based on current mouse position.
        /// Called each frame while dragging.
        /// </summary>
        public void OnDragUpdate(Vector2 mousePos)
        {
            if (!IsDragging) return;

            // Validate the session is still valid (pawn not destroyed, etc.)
            if (!_session.IsValid())
            {
                BetterWorkTabMod.DebugLog(
                    $"Row drag cancelled: {_session.Describe()} is no longer valid.",
                    DebugFeature.DragDrop);
                IsDragging = false;
                return;
            }

            // Calculate target index from mouse position
            _session.TargetIndex = CalculateTargetIndex(mousePos);
        }

        /// <summary>
        /// Calculate which row index the mouse is currently over.
        /// Uses midpoint semantics: if above midpoint of row i, insert at i.
        /// </summary>
        private int CalculateTargetIndex(Vector2 mousePos)
        {
            float headerBottom = _layout.TableOrigin.y + _layout.HeaderHeight;
            float contentY = mousePos.y - headerBottom + _layout.Table.scrollPosition.y;

            var descriptors = _layout.GetRowDescriptors();
            int newIndex = descriptors.Count;

            float cumulativeY = 0f;
            for (int i = 0; i < descriptors.Count; i++)
            {
                float midpoint = cumulativeY + (descriptors[i].Height * 0.5f);
                if (contentY < midpoint)
                {
                    newIndex = i;
                    break;
                }
                cumulativeY += descriptors[i].Height;
            }

            return Mathf.Clamp(newIndex, 0, descriptors.Count);
        }

        /// <summary>
        /// Draw the drag ghost and insertion line.
        /// </summary>
        public void OnDrawOverlay()
        {
            if (!IsDragging) return;

            var settings = BetterWorkTabMod.Settings;
            bool showGhost = false; // default to line-only by design
            bool showLine = true;
            bool lineOnly = true;

            // Draw ghost rectangle following the mouse
            if (!lineOnly && showGhost)
            {
                Rect ghostRect = _session.OriginalRect;
                ghostRect.y = Event.current.mousePosition.y - _session.DragOffsetY;
                ListDragVisuals.DrawGhost(ghostRect, _session.GetDisplayLabel());
            }

            // Draw insertion line at target position
            if (_session.TargetIndex >= 0 && showLine)
            {
                var descriptors = _layout.GetRowDescriptors();
                var heights = descriptors.Select(d => d.Height).ToList();

                float headerBottom = _layout.TableOrigin.y + _layout.HeaderHeight;
                float lineY = ListDragVisuals.GetInsertionLineY(
                    _session.TargetIndex,
                    heights,
                    headerBottom,
                    _layout.Table.scrollPosition.y);

                ListDragVisuals.DrawInsertionLine(
                    _layout.TableOrigin.x,
                    lineY,
                    _layout.Table.Size.x - 16f);
            }
        }

        /// <summary>
        /// Finalize the drag: reorder the pawn/divider list.
        /// </summary>
        public void OnDrop()
        {
            if (!IsDragging) return;

            CommitReorder();
            IsDragging = false;
        }

        /// <summary>
        /// Cancel the drag without making changes.
        /// </summary>
        public void OnCancel()
        {
            BetterWorkTabMod.DebugLog(
                $"Row drag cancelled for {_session.Describe()}.",
                DebugFeature.DragDrop);
            IsDragging = false;
        }

        /// <summary>
        /// Commit the reorder by updating displayOrder values.
        /// Looks up the current row list fresh so we act on the latest layout state.
        /// </summary>
        private void CommitReorder()
        {
            // Get current layout rows, sorted by visual index
            var orderedRows = _layout.Rows.OrderBy(r => r.VisualIndex).ToList();

            // Find the dragged item in the current layout using stable references
            int currentIndex = FindCurrentIndex(orderedRows);
            
            if (currentIndex < 0)
            {
                BetterWorkTabMod.DebugLog(
                    $"Row drag commit failed: {_session.Describe()} not found in current layout. " +
                    $"Row count: {orderedRows.Count}",
                    DebugFeature.DragDrop);
                return;
            }

            // Calculate final insertion position
            int targetIndex = _session.TargetIndex;
            
            // Remove the item from its current position
            var rowToMove = orderedRows[currentIndex];
            orderedRows.RemoveAt(currentIndex);

            // Adjust target if we removed an item before it
            int finalTargetIndex = targetIndex;
            if (currentIndex < targetIndex)
            {
                finalTargetIndex--;
            }

            // Clamp and insert
            int insertIndex = Mathf.Clamp(finalTargetIndex, 0, orderedRows.Count);
            orderedRows.Insert(insertIndex, rowToMove);

            BetterWorkTabMod.DebugLog(
                $"Row drag commit: {_session.Describe()} from index {currentIndex} -> {insertIndex} " +
                $"(requested {targetIndex}, start {_session.StartVisualIndex}).",
                DebugFeature.DragDrop);

            // Update all display orders to match new visual order
            UpdateDisplayOrders(orderedRows);

            // Notify the game to refresh
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            Find.ColonistBar?.MarkColonistsDirty();

            // Notify multiplayer followers
            if (Better_Work_Tab.Mod_Support.Multiplayer.MultiplayerBridge.Active)
            {
               Better_Work_Tab.Mod_Support.Multiplayer.Features.Layouts.LayoutSharingManager.NotifyLayoutChanged();
            }
        }

        /// <summary>
        /// Find the current index of our dragged item using stable game object references.
        /// </summary>
        private int FindCurrentIndex(List<WorkTabLayoutRow> rows)
        {
            if (_session.IsDraggingPawn)
            {
                // Find by pawn reference (stable)
                return rows.FindIndex(r => r.Pawn == _session.DraggedPawn);
            }
            
            if (_session.IsDraggingDivider)
            {
                // Find by divider reference (stable)
                return rows.FindIndex(r => r.Divider == _session.DraggedDivider);
            }

            return -1;
        }

        /// <summary>
        /// Update displayOrder values to match the new visual order.
        /// </summary>
        private void UpdateDisplayOrders(List<WorkTabLayoutRow> orderedRows)
        {
            for (int i = 0; i < orderedRows.Count; i++)
            {
                var row = orderedRows[i];
                
                if (row.Pawn?.playerSettings != null)
                {
                    RowOrderUtility.SetPawnRowOrder(row.Pawn, i);
                }
                else if (row.Divider != null)
                {
                    row.Divider.DisplayOrder = i;
                }
            }
        }
    }
}
