using System.Linq;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    public class RowDragController
    {
        private const float DragStartThreshold = 5f;
        private readonly IWorkTabLayoutController _layout;
        private DisplayElement _draggedElement;
        private WorkTabLayoutRow _rowSnapshot;
        private WorkTabLayoutRow _pendingRow;
        private bool _hasPendingRow;
        private Rect _pendingBounds;
        private float _dragOffsetY;
        private Vector2 _mouse;
        private Vector2 _initialMouse;
        private int _targetIndex = -1;

        public RowDragController(IWorkTabLayoutController layout)
        {
            _layout = layout;
        }

        public bool IsDragging => _draggedElement != null;

        public void HandleInput(Event evt)
        {
            if (_layout?.Table == null || evt == null)
            {
                return;
            }

            bool requireCtrl = BetterWorkTabMod.Settings?.requireCtrlForDrag ?? true;
            bool ctrlSatisfied = !requireCtrl || evt.control;

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (ctrlSatisfied && _layout.TryGetRowAt(evt.mousePosition, out var row))
                {
                    _pendingRow = row;
                    _hasPendingRow = true;
                    _initialMouse = evt.mousePosition;
                    _pendingBounds = _layout.GetScreenRect(row);
                }
                else
                {
                    _hasPendingRow = false;
                    _pendingRow = default;
                    _pendingBounds = Rect.zero;
                }
                return;
            }

            if (_hasPendingRow && evt.type == EventType.MouseDrag)
            {
                if (MouseLeftPendingRow(evt.mousePosition))
                {
                    BeginDrag(_pendingRow, evt.mousePosition);
                    _hasPendingRow = false;
                    _pendingRow = default;
                    _pendingBounds = Rect.zero;
                    evt.Use();
                    return;
                }
            }

            if (_hasPendingRow && evt.type == EventType.MouseUp)
            {
                _hasPendingRow = false;
                _pendingRow = default;
                _pendingBounds = Rect.zero;
            }

            if (!IsDragging)
            {
                return;
            }

            switch (evt.type)
            {
                case EventType.MouseDrag:
                    _mouse = evt.mousePosition;
                    UpdateInsertionIndex(evt.mousePosition);
                    evt.Use();
                    break;
                case EventType.MouseUp:
                    Log.Message("[BWT_RowDrag] MouseUp detected. Calling Commit().");
                    Commit();
                    Reset();
                    evt.Use();
                    break;
                case EventType.KeyDown when evt.keyCode == KeyCode.Escape:
                    Log.Message("[BWT_RowDrag] Escape key pressed. Cancelling drag.");
                    Reset();
                    evt.Use();
                    break;
            }
        }

        public void DrawOverlay(IWorkTabLayoutController layout)
        {
            if (!IsDragging || layout?.Table == null) return;

            bool lineOnly = BetterWorkTabMod.Settings?.showOnlyLineDragIndicatorRows == true;
            if (!lineOnly)
            {
                var baseRect = layout.GetScreenRect(_rowSnapshot);
                var ghostRect = baseRect;
                ghostRect.y = _mouse.y - _dragOffsetY;
                Widgets.DrawBoxSolid(ghostRect, new Color(0f, 0f, 0f, 0.25f));
                Widgets.DrawBox(ghostRect, 1);
                if (_rowSnapshot.IsDivider && _rowSnapshot.Divider != null) DrawDividerGhostLabel(ghostRect, _rowSnapshot.Divider);
                else if (_rowSnapshot.Pawn != null) DrawPawnGhostLabel(ghostRect, _rowSnapshot.Pawn);
            }

            if (_targetIndex >= 0)
            {
                float lineY = CalculateInsertionY(layout, _targetIndex);
                var lineRect = new Rect(layout.TableOrigin.x, lineY - 1f, layout.Table.Size.x - 16f, 2f);
                Widgets.DrawBoxSolid(lineRect, Color.white);
            }
        }

        private bool MouseLeftPendingRow(Vector2 mousePosition)
        {
            if (_pendingBounds.width <= 0f || _pendingBounds.height <= 0f)
            {
                float sqrThreshold = DragStartThreshold * DragStartThreshold;
                return (mousePosition - _initialMouse).sqrMagnitude >= sqrThreshold;
            }

            return !_pendingBounds.Contains(mousePosition);
        }

        private void BeginDrag(WorkTabLayoutRow row, Vector2 mousePosition)
        {
            _draggedElement = row.Element;
            _rowSnapshot = row;
            _mouse = mousePosition;
            var rect = _layout.GetScreenRect(row);
            _dragOffsetY = mousePosition.y - rect.y;
            _targetIndex = row.VisualIndex;

            string elementName = row.Pawn?.LabelShort ?? row.Divider?.DividerName ?? "Unknown";
            Log.Message($"[BWT_RowDrag] BeginDrag: Dragging '{elementName}' from index {row.VisualIndex}. Stored reference to DisplayElement.");
        }

        private void UpdateInsertionIndex(Vector2 mousePosition)
        {
            float headerTop = _layout.TableOrigin.y + _layout.HeaderHeight;
            float contentY = mousePosition.y - headerTop + _layout.Table.scrollPosition.y;
            int newIndex = _layout.Rows.Count;
            for (int i = 0; i < _layout.Rows.Count; i++)
            {
                var candidate = _layout.Rows[i];
                if (contentY < candidate.OffsetY + candidate.Height * 0.5f)
                {
                    newIndex = i;
                    break;
                }
            }
            newIndex = Mathf.Clamp(newIndex, 0, _layout.Rows.Count);
            if (newIndex != _targetIndex)
            {
                _targetIndex = newIndex;
                Log.Message($"[BWT_RowDrag] UpdateInsertionIndex: Target index changed to {_targetIndex}.");
            }
        }

        private float CalculateInsertionY(IWorkTabLayoutController layout, int targetIndex)
        {
            if (layout.Rows.Count == 0) return layout.TableOrigin.y + layout.HeaderHeight;
            if (targetIndex >= layout.Rows.Count) return layout.GetScreenRect(layout.Rows.Last()).yMax;
            return layout.GetScreenRect(layout.Rows[targetIndex]).y;
        }

        private void Commit()
        {
            if (!IsDragging || _draggedElement == null)
            {
                Log.Error($"[BWT_RowDrag] Commit called without an active element. Element={_draggedElement?.GetType().Name ?? "null"}");
                return;
            }

            Log.Message($"[BWT_RowDrag] Commit: Starting commit for target index {_targetIndex}.");

            var ordered = _layout.Rows.OrderBy(r => r.VisualIndex).ToList();

            int currentIndex = -1;
            if (_draggedElement is PawnElement draggedPawnElement)
            {
                // Find the row corresponding to the dragged PAWN.
                currentIndex = ordered.FindIndex(r => (r.Element as PawnElement)?.Pawn == draggedPawnElement.Pawn);
            }
            else if (_draggedElement is DividerElement draggedDividerElement)
            {
                // Find the row corresponding to the dragged DIVIDER.
                currentIndex = ordered.FindIndex(r => (r.Element as DividerElement)?.Divider == draggedDividerElement.Divider);
            }

            if (currentIndex < 0)
            {
                Log.Error("[BWT_RowDrag] Commit FAILED: Could not find the dragged element in the current row list. This can happen if the list was refreshed mid-drag. Aborting.");
                return;
            }
            Log.Message($"[BWT_RowDrag] Commit: Found dragged element at current index {currentIndex}.");

            var rowToMove = ordered[currentIndex];
            ordered.RemoveAt(currentIndex);

            int insertIndex = Mathf.Clamp(_targetIndex, 0, ordered.Count);
            ordered.Insert(insertIndex, rowToMove);
            Log.Message($"[BWT_RowDrag] Commit: Moved element from {currentIndex} to {insertIndex}. List now has {ordered.Count} items.");

            // Only update displayOrder for the affected range
            int minIndex = Mathf.Min(currentIndex, insertIndex);
            int maxIndex = Mathf.Max(currentIndex, insertIndex);

            Log.Message($"[BWT_RowDrag] Commit: Applying new displayOrder values for range {minIndex} to {maxIndex}...");
            
            for (int i = minIndex; i <= maxIndex && i < ordered.Count; i++)
            {
                if (ordered[i].Pawn != null)
                {
                    var settings = ordered[i].Pawn.playerSettings;
                    if (settings != null)
                    {
                        settings.displayOrder = i;
                    }
                }
                else if (ordered[i].Divider != null)
                {
                    ordered[i].Divider.DisplayOrder = i;
                }
            }
            Log.Message("[BWT_RowDrag] Commit: New displayOrder values applied (Optimized).");

            Log.Message("[BWT_RowDrag] Commit: Calling NotifyAllPawnTables_PawnsChanged() to trigger table rebuild.");
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            if (Find.ColonistBar != null)
            {
                Find.ColonistBar.MarkColonistsDirty();
            }
            UI.MainTabWindow_BetterWork.FlagWindowSnap();
            Log.Message("[BWT_RowDrag] Commit: FINISHED.");
        }

        private static void DrawDividerGhostLabel(Rect rect, PawnDivider divider)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect labelRect = rect.ContractedBy(4f);
            Widgets.Label(labelRect, divider.DividerName ?? "Divider");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void DrawPawnGhostLabel(Rect rect, Pawn pawn)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect labelRect = rect.ContractedBy(4f);
            Widgets.Label(labelRect, pawn.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void Reset()
        {
            _draggedElement = null;
            _rowSnapshot = default;
            _targetIndex = -1;
            _dragOffsetY = 0f;
            _mouse = Vector2.zero;
            _hasPendingRow = false;
            _pendingRow = default;
            _pendingBounds = Rect.zero;
            _initialMouse = Vector2.zero;
        }
    }
}
