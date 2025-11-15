using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer
{
    public class PawnOrganizerSystem
    {
        public static PawnOrganizerSystem Instance { get; private set; }

        private Worklist _currentWorklist;
        private List<DisplayElement> _displayElements = new List<DisplayElement>();
        private PawnDivider _draggedDivider = null;
        private Vector2 _dragOffset = Vector2.zero;

        private const float DividerHeight = 18f;

        public PawnOrganizerSystem()
        {
            Instance = this;
            Log.Message("[BetterWorkTab] PawnOrganizerSystem initialized.");
        }

        public void OnWorkTabGUI(PawnTable table, IWorkTabRowColumnAPI api)
        {
            _currentWorklist = Current.Game.GetComponent<GameComponent_WorkloadSaver>()?.CurrentWorklist;
            if (_currentWorklist == null) return;

            HandleDividerCreation(api);
            HandleDividerDrag(api);
        }

        public void DrawOrganization(PawnTable table, IWorkTabRowColumnAPI api)
        {
            if (_currentWorklist == null) return;

            RebuildDisplayElements(api);
            DrawDividers(api);
            DrawDraggedDivider(api);
        }

        private void RebuildDisplayElements(IWorkTabRowColumnAPI api)
        {
            _displayElements.Clear();

            foreach (var pawn in api.GetVisiblePawns())
            {
                _displayElements.Add(new PawnElement(pawn));
            }

            foreach (var divider in _currentWorklist.Dividers)
            {
                _displayElements.Add(new DividerElement(divider));
            }

            _displayElements = _displayElements.OrderBy(e => e.DisplayOrder).ToList();
        }

        private void DrawDividers(IWorkTabRowColumnAPI api)
        {
            float currentY = api.GetTableOrigin().y + api.GetHeaderHeight() - api.GetScrollPosition().y;
            int dividerCount = 0;

            foreach (var element in _displayElements)
            {
                if (element.IsDivider && element is DividerElement divElem)
                {
                    if (_draggedDivider == divElem.Divider)
                    {
                        // Draw background to cover underlying pawn row
                        var backgroundRect = new Rect(
                            api.GetTableOrigin().x,
                            currentY,
                            api.GetTableSize().x - 16f,
                            DividerHeight
                        );
                        Widgets.DrawBoxSolid(backgroundRect, new Color(0.2f, 0.2f, 0.2f, 1f)); // Dark background

                        currentY += DividerHeight;
                        continue;
                    }

                    // Draw background to cover underlying pawn row
                    var bgRect = new Rect(
                        api.GetTableOrigin().x,
                        currentY,
                        api.GetTableSize().x - 16f,
                        DividerHeight
                    );
                    Widgets.DrawBoxSolid(bgRect, new Color(0.2f, 0.2f, 0.2f, 1f)); // Dark background to create gap

                    DrawDivider(bgRect, divElem.Divider, api);
                    currentY += DividerHeight;
                    dividerCount++;
                }
                else if (element is PawnElement pe)
                {
                    var rowRect = api.GetPawnRowRect(pe.Pawn);
                    if (rowRect.HasValue)
                    {
                        // Shift this pawn row down if any dividers came before it
                        int dividersBeforeThisPawn = _displayElements
                            .Take(_displayElements.IndexOf(element))
                            .Count(e => e.IsDivider);

                        if (dividersBeforeThisPawn > 0)
                        {
                            // Draw a covering rect to "move" the pawn row visually
                            float offsetY = dividersBeforeThisPawn * DividerHeight;
                            var shiftedRect = new Rect(
                                rowRect.Value.x,
                                rowRect.Value.y + offsetY,
                                rowRect.Value.width,
                                rowRect.Value.height
                            );

                            // Only proceed if we're not trying to shift beyond vanilla's rendering
                            // (This is a visual-only hack; actual row positions are unchanged)
                        }

                        currentY = rowRect.Value.y + rowRect.Value.height;
                    }
                }
            }

            Log.Message($"[BetterWorkTab] Drew {dividerCount} dividers.");
        }

        private void DrawDivider(Rect rect, PawnDivider divider, IWorkTabRowColumnAPI api)
        {
            // Draw divider background
            Widgets.DrawBoxSolid(rect, divider.DividerColor * 0.3f);
            Widgets.DrawBox(rect, 1);

            // Drag handle (left side, scaled to fit)
            float handleSize = Mathf.Min(rect.height - 2f, 16f);
            Rect handleRect = new Rect(rect.x + 2f, rect.y + (rect.height - handleSize) / 2f, handleSize, handleSize);
            GUI.DrawTexture(handleRect, TexUI.GrayBg);

            // Find the "Name" column's X position for proper alignment
            float nameColumnX = GetNameColumnX(api);

            // Draw name (left-aligned, starting at name column position)
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect nameRect = new Rect(nameColumnX, rect.y, rect.width - (nameColumnX - rect.x) - 24f, rect.height);
            Widgets.Label(nameRect, divider.DividerName);
            Text.Anchor = TextAnchor.UpperLeft;

            // Delete button (right side)
            Rect deleteRect = new Rect(rect.xMax - handleSize - 2f, rect.y + (rect.height - handleSize) / 2f, handleSize, handleSize);
            if (Widgets.ButtonImage(deleteRect, TexButton.Delete))
            {
                Log.Message($"[BetterWorkTab] Deleting divider: {divider.DividerName}");
                _currentWorklist.Dividers.Remove(divider);
                Messages.Message($"Deleted {divider.DividerName}", MessageTypeDefOf.NeutralEvent, false);
                return; // Exit to avoid processing more events
            }

            // Drag start
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && handleRect.Contains(Event.current.mousePosition))
            {
                Log.Message($"[BetterWorkTab] Started dragging divider: {divider.DividerName}");
                _draggedDivider = divider;
                _dragOffset = Event.current.mousePosition - rect.position;
                Event.current.Use();
                return;
            }

            // Rename on click (outside handle and delete button)
            Rect clickableArea = new Rect(rect.x + handleSize + 4f, rect.y, rect.width - handleSize - deleteRect.width - 8f, rect.height);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && clickableArea.Contains(Event.current.mousePosition))
            {
                Log.Message($"[BetterWorkTab] Opening rename dialog for divider: {divider.DividerName}");
                Find.WindowStack.Add(new Dialog_RenameGeneric(divider.DividerName, newName =>
                {
                    divider.DividerName = newName;
                    Log.Message($"[BetterWorkTab] Renamed divider to: {newName}");
                }));
                Event.current.Use();
            }
        }

        /// <summary>
        /// Get the X position where the "Name" column starts (for left-aligning divider names).
        /// </summary>
        private float GetNameColumnX(IWorkTabRowColumnAPI api)
        {
            var table = api.GetTable();
            float xAccum = api.GetTableOrigin().x;

            foreach (var col in table.Columns)
            {
                if (col.Worker is PawnColumnWorker_Label) // "Name" column
                {
                    return xAccum;
                }

                int colIndex = table.Columns.IndexOf(col);
                float colWidth = (colIndex == table.Columns.Count - 1)
                    ? (api.GetTableSize().x - 16f - (xAccum - api.GetTableOrigin().x))
                    : table.cachedColumnWidths[colIndex];

                xAccum += colWidth;
            }

            // Fallback: return a reasonable offset if Name column not found
            return api.GetTableOrigin().x + 60f;
        }

        private void DrawDraggedDivider(IWorkTabRowColumnAPI api)
        {
            if (_draggedDivider == null) return;

            Vector2 mousePos = Event.current.mousePosition;
            Rect ghostRect = new Rect(
                api.GetTableOrigin().x,
                mousePos.y - _dragOffset.y,
                api.GetTableSize().x - 16f,
                DividerHeight
            );

            // Draw ghost
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Widgets.DrawBoxSolid(ghostRect, _draggedDivider.DividerColor * 0.3f);
            Widgets.DrawBox(ghostRect, 1);

            float nameColumnX = GetNameColumnX(api);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect nameRect = new Rect(nameColumnX, ghostRect.y, ghostRect.width - (nameColumnX - ghostRect.x), ghostRect.height);
            Widgets.Label(nameRect, _draggedDivider.DividerName);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // Draw insertion line
            int targetIndex = CalculateInsertionIndex(mousePos.y, api);
            float lineY = CalculateLineYForIndex(targetIndex, api);
            Rect lineRect = new Rect(
                api.GetTableOrigin().x,
                lineY - 1f,
                api.GetTableSize().x - 16f,
                2f
            );
            Widgets.DrawBoxSolid(lineRect, Color.white);
        }

        private void HandleDividerCreation(IWorkTabRowColumnAPI api)
        {
            if (!Input.GetKey(KeyCode.LeftShift)) return;
            if (Event.current.type != EventType.MouseDown || Event.current.button != 0) return;

            var clickedPawn = api.GetPawnAtMousePosition(Event.current.mousePosition);
            if (clickedPawn == null)
            {
                Log.Message("[BetterWorkTab] Shift+Click: No pawn at mouse position.");
                return;
            }

            Log.Message($"[BetterWorkTab] Shift+Click detected on pawn: {clickedPawn.LabelShort}");

            int pawnOrder = clickedPawn.playerSettings?.displayOrder ?? 0;

            var newDivider = new PawnDivider
            {
                DividerName = $"Divider {_currentWorklist.Dividers.Count + 1}",
                DisplayOrder = pawnOrder + 1
            };

            _currentWorklist.Dividers.Add(newDivider);
            Log.Message($"[BetterWorkTab] Created divider '{newDivider.DividerName}' at order {newDivider.DisplayOrder}. Total dividers: {_currentWorklist.Dividers.Count}");
            Messages.Message($"Created {newDivider.DividerName}", MessageTypeDefOf.NeutralEvent, false);
            Event.current.Use();
        }

        private void HandleDividerDrag(IWorkTabRowColumnAPI api)
        {
            if (_draggedDivider == null) return;

            if (Event.current.type == EventType.MouseDrag)
            {
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseUp)
            {
                FinalizeDividerDrag(api);
                Event.current.Use();
            }
        }

        private void FinalizeDividerDrag(IWorkTabRowColumnAPI api)
        {
            if (_draggedDivider == null) return;

            Log.Message($"[BetterWorkTab] Finalizing drag for divider: {_draggedDivider.DividerName}");

            int targetIndex = CalculateInsertionIndex(Event.current.mousePosition.y, api);
            var elements = _displayElements.OrderBy(e => e.DisplayOrder).ToList();
            int currentIndex = elements.FindIndex(e =>
                e.IsDivider && ((DividerElement)e).Divider == _draggedDivider);

            if (currentIndex != targetIndex)
            {
                Log.Message($"[BetterWorkTab] Moving divider from index {currentIndex} to {targetIndex}");
                elements.RemoveAt(currentIndex);

                int insertIndex = targetIndex;
                if (currentIndex < targetIndex)
                    insertIndex--;

                elements.Insert(insertIndex, new DividerElement(_draggedDivider));

                for (int i = 0; i < elements.Count; i++)
                {
                    if (elements[i].IsDivider)
                    {
                        ((DividerElement)elements[i]).Divider.DisplayOrder = i;
                    }
                    else
                    {
                        var pawn = ((PawnElement)elements[i]).Pawn;
                        if (pawn.playerSettings != null)
                            pawn.playerSettings.displayOrder = i;
                    }
                }

                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            }
            else
            {
                Log.Message("[BetterWorkTab] Divider drag ended, but position did not change.");
            }

            _draggedDivider = null;
        }

        private int CalculateInsertionIndex(float mouseY, IWorkTabRowColumnAPI api)
        {
            float currentY = api.GetTableOrigin().y + api.GetHeaderHeight() - api.GetScrollPosition().y;

            for (int i = 0; i < _displayElements.Count; i++)
            {
                float elementHeight = _displayElements[i].IsDivider ? DividerHeight :
                    (api.GetPawnRowRect(((PawnElement)_displayElements[i]).Pawn)?.height ?? 30f);

                if (mouseY < currentY + elementHeight / 2f)
                    return i;

                currentY += elementHeight;
            }

            return _displayElements.Count;
        }

        private float CalculateLineYForIndex(int index, IWorkTabRowColumnAPI api)
        {
            float currentY = api.GetTableOrigin().y + api.GetHeaderHeight() - api.GetScrollPosition().y;

            for (int i = 0; i < index; i++)
            {
                if (i >= _displayElements.Count) break;

                float elementHeight = _displayElements[i].IsDivider ? DividerHeight :
                    (api.GetPawnRowRect(((PawnElement)_displayElements[i]).Pawn)?.height ?? 30f);

                currentY += elementHeight;
            }

            return currentY;
        }
    }
}