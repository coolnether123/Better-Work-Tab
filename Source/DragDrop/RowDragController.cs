using System.Linq;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Handles ctrl+drag row reordering against the layout controller.
    /// </summary>
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
            if (_layout?.Table == null || evt == null)
            {
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.control)
            {
                if (_layout.TryGetRowAt(evt.mousePosition, out var row))
                {
                    BeginDrag(row, evt.mousePosition);
                    evt.Use();
                }
                return;
            }

            if (!IsDragging)
            {
                return;
            }

            if (evt.type == EventType.MouseDrag)
            {
                _mouse = evt.mousePosition;
                UpdateInsertionIndex(evt.mousePosition);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp)
            {
                Commit();
                Reset();
                evt.Use();
            }
            else if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                Reset();
                evt.Use();
            }
        }

        public void DrawOverlay(IWorkTabLayoutController layout)
        {
            if (!IsDragging || layout?.Table == null)
            {
                return;
            }

            var baseRect = layout.GetScreenRect(_rowSnapshot);
            var ghostRect = baseRect;
            ghostRect.y = _mouse.y - _dragOffsetY;
            Widgets.DrawBoxSolid(ghostRect, new Color(0f, 0f, 0f, 0.25f));
            Widgets.DrawBox(ghostRect, 1);

            if (_rowSnapshot.IsDivider && _rowSnapshot.Divider != null)
            {
                DrawDividerGhostLabel(ghostRect, _rowSnapshot.Divider);
            }
            else if (_rowSnapshot.Pawn != null)
            {
                DrawPawnGhostLabel(ghostRect, _rowSnapshot.Pawn);
            }

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
            if (newIndex < 0)
            {
                newIndex = 0;
            }
            else if (newIndex > _layout.Rows.Count)
            {
                newIndex = _layout.Rows.Count;
            }
            _targetIndex = newIndex;
        }

        private float CalculateInsertionY(IWorkTabLayoutController layout, int targetIndex)
        {
            if (layout.Rows.Count == 0)
            {
                return layout.TableOrigin.y + layout.HeaderHeight;
            }

            if (targetIndex >= layout.Rows.Count)
            {
                var lastRect = layout.GetScreenRect(layout.Rows[layout.Rows.Count - 1]);
                return lastRect.yMax;
            }

            return layout.GetScreenRect(layout.Rows[targetIndex]).y;
        }

        private void Commit()
        {
            if (!IsDragging || _layout?.Rows == null)
            {
                return;
            }

            var ordered = _layout.Rows.OrderBy(r => r.VisualIndex).ToList();
            int currentIndex = ordered.FindIndex(r => ReferenceEquals(r.Element, _draggedElement));
            if (currentIndex < 0)
            {
                return;
            }

            var row = ordered[currentIndex];
            ordered.RemoveAt(currentIndex);

            int insertIndex = Mathf.Clamp(_targetIndex, 0, ordered.Count);
            if (insertIndex >= ordered.Count)
            {
                ordered.Add(row);
            }
            else
            {
                ordered.Insert(insertIndex, row);
            }

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

            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            if (Find.ColonistBar != null)
            {
                Find.ColonistBar.MarkColonistsDirty();
            }
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
