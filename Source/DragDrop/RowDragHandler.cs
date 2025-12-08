using Better_Work_Tab.Features;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI;
using RimWorld;
using Spine.DragDropApi;
using Spine.DragDropApi.Util;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Handles row (Pawn/Divider) dragging within the work tab.
    /// Uses the refactored DragDropApi for visuals and midpoint snapping.
    /// </summary>
    public class RowDragHandler : DragHandler<WorkTabLayoutRow>
    {
        private readonly WorkTabLayoutRow _draggedRow;
        private readonly Rect _originalRect;
        private readonly float _dragOffsetY;

        private List<float> _cachedDescriptorHeights;
        private int _lastDescriptorCount = -1;

        public RowDragHandler(IWorkTabLayoutController layout, WorkTabLayoutRow row, Vector2 startMouse)
            : base(layout)
        {
            _draggedRow = row;
            _originalRect = layout.GetScreenRect(row);
            _dragOffsetY = startMouse.y - _originalRect.y;
            TargetIndex = row.VisualIndex;

            RefreshHeightCache();
        }

        public override void OnDragUpdate(Vector2 mousePos)
        {
            // Convert mouse Y into "content space" below the header, including scroll.
            float headerBottom = Layout.TableOrigin.y + Layout.HeaderHeight;
            float contentY = mousePos.y - headerBottom + Layout.Table.scrollPosition.y;

            var descriptors = Layout.GetRowDescriptors();
            int newIndex = descriptors.Count;

            // Midpoint semantics: if we're above the midpoint of row i,
            // we insert at i; otherwise we keep walking.
            float cumulativeY = 0f;
            for (int i = 0; i < descriptors.Count; i++)
            {
                float mid = cumulativeY + (descriptors[i].Height * 0.5f);

                if (contentY < mid)
                {
                    newIndex = i;
                    break;
                }
                cumulativeY += descriptors[i].Height;
            }

            TargetIndex = Mathf.Clamp(newIndex, 0, descriptors.Count);
        }

        public override void OnDrawOverlay()
        {
            if (!IsDragging) return;

            bool lineOnly = BetterWorkTabMod.Settings?.showOnlyLineDragIndicatorRows ?? false;

            if (!lineOnly)
            {
                Rect ghostRect = _originalRect;
                ghostRect.y = Event.current.mousePosition.y - _dragOffsetY;

                // Handle potential null divider name
                string label = _draggedRow.Pawn?.LabelCap
                    ?? _draggedRow.Divider?.DividerName
                    ?? "Divider";  // Fallback 
                ListDragVisuals.DrawGhost(ghostRect, label);
            }

            if (TargetIndex >= 0)
            {
                // Use ListDragVisuals + per-row heights so the insertion line is consistent
                var descriptors = Layout.GetRowDescriptors();
                var heights = descriptors.Select(r => r.Height).ToList();

                float headerBottom = Layout.TableOrigin.y + Layout.HeaderHeight;

                float lineY = ListDragVisuals.GetInsertionLineY(
                    TargetIndex,
                    _cachedDescriptorHeights,
                    headerBottom,
                    Layout.Table.scrollPosition.y);

                ListDragVisuals.DrawInsertionLine(
                    Layout.TableOrigin.x,
                    lineY,
                    Layout.Table.Size.x - 16f);
            }
        }

        protected override void CommitReorder()
        {
            if (!IsDragging) return;

            var ordered = Layout.Rows.OrderBy(r => r.VisualIndex).ToList();

            int currentIndex = -1;
            if (_draggedRow.Element is PawnElement draggedPawnElement)
            {
                currentIndex = ordered.FindIndex(r => (r.Element as PawnElement)?.Pawn == draggedPawnElement.Pawn);
            }
            else if (_draggedRow.Element is DividerElement draggedDividerElement)
            {
                currentIndex = ordered.FindIndex(r => (r.Element as DividerElement)?.Divider == draggedDividerElement.Divider);
            }

            if (currentIndex < 0)
            {
                IsDragging = false;
                return;
            }

            var rowToMove = ordered[currentIndex];
            ordered.RemoveAt(currentIndex);

            // Adjust target for removal
            int finalTargetIndex = TargetIndex;
            if (currentIndex < finalTargetIndex)
                finalTargetIndex--;

            int insertIndex = Mathf.Clamp(finalTargetIndex, 0, ordered.Count);
            ordered.Insert(insertIndex, rowToMove);

            // Update display order
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Pawn?.playerSettings != null)
                    ordered[i].Pawn.playerSettings.displayOrder = i;
                else if (ordered[i].Divider != null)
                    ordered[i].Divider.DisplayOrder = i;
            }

            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            if (Find.ColonistBar != null) Find.ColonistBar.MarkColonistsDirty();
        }

        private void RefreshHeightCache()
        {
            var descriptors = Layout.GetRowDescriptors();
            if (_cachedDescriptorHeights == null || descriptors.Count != _lastDescriptorCount)
            {
                _cachedDescriptorHeights = new List<float>(descriptors.Count);
                _lastDescriptorCount = descriptors.Count;
            }
            else
            {
                _cachedDescriptorHeights.Clear();
            }

            for (int i = 0; i < descriptors.Count; i++)
            {
                _cachedDescriptorHeights.Add(descriptors[i].Height);
            }
        }
    }
}
