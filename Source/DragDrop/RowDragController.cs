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
        private readonly IWorkTabLayoutController _layout;
        private DisplayElement _draggedElement;
        private WorkTabLayoutRow _rowSnapshot;
        private float _dragOffsetY;
        private Vector2 _mouse;
        private int _targetIndex = -1;

        public RowDragController(IWorkTabLayoutController layout)
        {
            _layout = layout;
        }

        public bool IsDragging => _draggedElement != null;

        public void HandleInput(Event evt)
        {
            if (_layout?.Table == null || evt == null) return;

            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.control)
            {
                if (_layout.TryGetRowAt(evt.mousePosition, out var row))
                {
                    BeginDrag(row, evt.mousePosition);
                    evt.Use();
                }
                return;
            }

            if (!IsDragging) return;

            if (evt.type == EventType.MouseDrag)
            {
                _mouse = evt.mousePosition;
                UpdateInsertionIndex(evt.mousePosition);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp)
            {
                Log.Message("[BWT_RowDrag] MouseUp detected. Calling Commit().");
                Commit();
                Reset();
                evt.Use();
            }
            else if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                Log.Message("[BWT_RowDrag] Escape key pressed. Cancelling drag.");
                Reset();
                evt.Use();
            }
        }

        public void DrawOverlay(IWorkTabLayoutController layout)
        {
            if (!IsDragging || layout?.Table == null) return;
            var baseRect = layout.GetScreenRect(_rowSnapshot);
            var ghostRect = baseRect;
            ghostRect.y = _mouse.y - _dragOffsetY;
            Widgets.DrawBoxSolid(ghostRect, new Color(0f, 0f, 0f, 0.25f));
            Widgets.DrawBox(ghostRect, 1);
            if (_rowSnapshot.IsDivider && _rowSnapshot.Divider != null) DrawDividerGhostLabel(ghostRect, _rowSnapshot.Divider);
            else if (_rowSnapshot.Pawn != null) DrawPawnGhostLabel(ghostRect, _rowSnapshot.Pawn);
            if (_targetIndex >= 0)
            {
                float lineY = CalculateInsertionY(layout, _targetIndex);
                var lineRect = new Rect(layout.TableOrigin.x, lineY - 1f, layout.Table.Size.x - 16f, 2f);
                Widgets.DrawBoxSolid(lineRect, Color.white);
            }
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
            if (!IsDragging)
            {
                Log.Error("[BWT_RowDrag] Commit called but IsDragging is false. Aborting.");
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

            Log.Message("[BWT_RowDrag] Commit: Applying new displayOrder values...");
            for (int i = 0; i < ordered.Count; i++)
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
            Log.Message("[BWT_RowDrag] Commit: New displayOrder values applied.");

            Log.Message("[BWT_RowDrag] Commit: Calling NotifyAllPawnTables_PawnsChanged() to trigger table rebuild.");
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            if (Find.ColonistBar != null)
            {
                Find.ColonistBar.MarkColonistsDirty();
            }
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
        }
    }
}