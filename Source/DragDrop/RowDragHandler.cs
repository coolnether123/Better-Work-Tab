using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI;
using RimWorld;
using Spine.DragDropApi;
using Spine.DragDropApi.Util;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Handles row (Pawn/Divider) dragging within the work tab.
    /// Uses the refactored DragDropApi for visuals and midpoint snapping.
    /// </summary>
    public class RowDragHandler
    {
        private readonly IWorkTabLayoutController _layout;
        private readonly WorkTabLayoutRow _draggedRow;
        private readonly Rect _originalRect;
        private readonly float _dragOffsetY;

        private int _targetIndex = -1;
        private bool _isDragging = false;

        public int TargetIndex => _targetIndex;

        public RowDragHandler(IWorkTabLayoutController layout, WorkTabLayoutRow row, Vector2 startMouse)
        {
            _layout = layout;
            _draggedRow = row;
            _originalRect = layout.GetScreenRect(row);
            _dragOffsetY = startMouse.y - _originalRect.y;
            _targetIndex = row.VisualIndex;
            _isDragging = true;
        }

        public bool IsDragging => _isDragging;

        public void OnDragUpdate(Vector2 mousePos)
        {
            // Convert mouse Y into "content space" below the header, including scroll.
            float headerBottom = _layout.TableOrigin.y + _layout.HeaderHeight;
            float contentY = mousePos.y - headerBottom + _layout.Table.scrollPosition.y;

            int newIndex = _layout.Rows.Count;

            // Midpoint semantics: if we're above the midpoint of row i,
            // we insert at i; otherwise we keep walking.
            for (int i = 0; i < _layout.Rows.Count; i++)
            {
                var r = _layout.Rows[i];
                float mid = r.OffsetY + (r.Height * 0.5f);

                if (contentY < mid)
                {
                    newIndex = i;
                    break;
                }
            }

            _targetIndex = Mathf.Clamp(newIndex, 0, _layout.Rows.Count);
        }

        public void OnDrawOverlay()
        {
            if (!_isDragging) return;

            bool lineOnly = BetterWorkTabMod.Settings?.showOnlyLineDragIndicatorRows ?? false;

            if (!lineOnly)
            {
                Rect ghostRect = _originalRect;
                ghostRect.y = Event.current.mousePosition.y - _dragOffsetY;

                string label = _draggedRow.Pawn?.LabelCap ?? _draggedRow.Divider?.DividerName ?? "";
                ListDragVisuals.DrawGhost(ghostRect, label);
            }

            if (_targetIndex >= 0)
            {
                // Use ListDragVisuals + per-row heights so the insertion line is consistent
                var heights = _layout.Rows.Select(r => r.Height).ToList();
                float headerBottom = _layout.TableOrigin.y + _layout.HeaderHeight;

                float lineY = ListDragVisuals.GetInsertionLineY(
                    _targetIndex,
                    heights,
                    headerBottom,
                    _layout.Table.scrollPosition.y);

                ListDragVisuals.DrawInsertionLine(
                    _layout.TableOrigin.x,
                    lineY,
                    _layout.Table.Size.x - 16f);
            }
        }

        public void OnDrop()
        {
            CommitReorder();
        }

        public void OnCancel()
        {
            _isDragging = false;
        }

        private void CommitReorder()
        {
            if (!_isDragging) return;

            var ordered = _layout.Rows.OrderBy(r => r.VisualIndex).ToList();

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
                _isDragging = false;
                return;
            }

            var rowToMove = ordered[currentIndex];
            ordered.RemoveAt(currentIndex);

            // Adjust target for removal
            int finalTargetIndex = _targetIndex;
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


            _isDragging = false;
        }
    }
}
