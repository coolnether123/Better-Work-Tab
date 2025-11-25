using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
using RimWorld;
using Spine.DragDropApi;
using Spine.DragDropApi.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Drag-drop UI for reordering rulesets with search filtering.
    /// Uses the refactored DragDropApi for better state management and events.
    /// </summary>
    public class RulesetListDragUI
    {
        private readonly DragDropController<WorkAssignmentRuleset> _dragController;

        // Data
        private readonly List<WorkAssignmentRuleset> _items;
        private readonly QuickSearchWidget _search = new QuickSearchWidget();
        private readonly Action<WorkAssignmentRuleset> _onSelect;

        // UI State
        private Vector2 _scrollPosition = Vector2.zero;
        private readonly List<WorkAssignmentRuleset> _filteredItems;
        private const float ItemHeight = 32f;

        // Track list screen rect for drag calculations
        private Rect _listScreenRect = Rect.zero;

        // Pending drag detection
        private bool _isMouseDown;
        private Vector2 _mouseDownPos;
        private WorkAssignmentRuleset _pendingDragItem;

        public WorkAssignmentRuleset SelectedRuleset { get; private set; }

        public RulesetListDragUI(List<WorkAssignmentRuleset> items,
            Action<WorkAssignmentRuleset> onSelect = null)
        {
            _items = items;
            _onSelect = onSelect;
            _filteredItems = new List<WorkAssignmentRuleset>(items);

            // Create controller with a calculation lambda that uses the stored list rect
            _dragController = new DragDropController<WorkAssignmentRuleset>(
                mousePos => CalculateTargetIndex(mousePos)
            );
        }

        /// <summary>
        /// Calculate target insertion index using current list screen rect.
        /// We bias the mouse Y by half an item height so the insertion index
        /// reflects the closest gap to the cursor, not just the row it is inside.
        /// </summary>
        private int CalculateTargetIndex(Vector2 mousePos)
        {
            float biasedMouseY = mousePos.y + (ItemHeight * 0.5f);

            return ListDragCalculator.CalculateInsertionIndex(
                _filteredItems.Count,
                biasedMouseY,
                _listScreenRect.y,        // Actual list screen Y
                _scrollPosition.y,        // Current scroll offset
                _ => ItemHeight           // All items same height
            );
        }

        public void DoListUI(Rect rect)
        {
            // 1. Search Bar
            Rect searchRect = new Rect(rect.x, rect.y, rect.width, 24f);
            _search.OnGUI(searchRect);

            // 2. Update filtered list based on search
            _filteredItems.Clear();
            foreach (var item in _items)
            {
                if (_search.filter.Matches(item.Name))
                    _filteredItems.Add(item);
            }

            // 3. Buttons (New/Duplicate)
            Rect buttonsRect = new Rect(rect.x, searchRect.yMax + 4f, rect.width, 32f);
            DrawButtons(buttonsRect);

            // 4. List Rect (remaining vertical space)
            Rect listRect = new Rect(
                rect.x,
                buttonsRect.yMax + 4f,
                rect.width,
                rect.height - searchRect.height - buttonsRect.height - 8f);

            DrawList(listRect);

            // 5. Drag Overlay
            if (_dragController.IsActive)
            {
                DrawDragOverlay();
            }
        }

        private void DrawList(Rect listRect)
        {
            Widgets.DrawMenuSection(listRect);

            float contentHeight = _filteredItems.Count * ItemHeight;
            Rect scrollViewScreenRect = listRect.ContractedBy(2f);
            Rect viewRect = new Rect(0f, 0f, scrollViewScreenRect.width - 16f, contentHeight);

            // Store the list screen rect so drag calculations use the correct Y
            _listScreenRect = scrollViewScreenRect;

            Widgets.BeginScrollView(scrollViewScreenRect, ref _scrollPosition, viewRect);

            float yPos = 0f;
            for (int i = 0; i < _filteredItems.Count; i++)
            {
                var item = _filteredItems[i];
                Rect itemRect = new Rect(0f, yPos, viewRect.width, ItemHeight);
                yPos += ItemHeight;

                // Highlight
                if (SelectedRuleset == item)
                    Widgets.DrawHighlightSelected(itemRect);
                else if (Mouse.IsOver(itemRect))
                    Widgets.DrawHighlight(itemRect);

                DrawRulesetRow(itemRect, item);
            }

            Widgets.EndScrollView();

            // Handle input after drawing
            HandleInput(scrollViewScreenRect);
        }

        private void HandleInput(Rect listScreenRect)
        {
            Event evt = Event.current;
            if (evt == null) return;

            // ===== ACTIVE DRAG HANDLING =====
            if (_dragController.IsActive)
            {
                // UpdateDrag recalculates using CalculateTargetIndex, which uses _listScreenRect
                _dragController.UpdateDrag(evt.mousePosition);

                // Auto-scroll near edges
                float contentHeight = _filteredItems.Count * ItemHeight;
                _dragController.ApplyAutoScroll(
                    ref _scrollPosition,
                    evt.mousePosition,
                    listScreenRect,
                    contentHeight,
                    Time.deltaTime
                );

                if (evt.type == EventType.MouseUp)
                {
                    FinalizeDrop();
                    evt.Use();
                }

                return;
            }

            // ===== DRAG / CLICK DETECTION =====
            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button == 0 && listScreenRect.Contains(evt.mousePosition))
                    {
                        _isMouseDown = true;
                        _mouseDownPos = evt.mousePosition;

                        // Find item under mouse using proper local coordinates
                        float localY = evt.mousePosition.y - listScreenRect.y + _scrollPosition.y;
                        int index = Mathf.FloorToInt(localY / ItemHeight);

                        _pendingDragItem = (index >= 0 && index < _filteredItems.Count)
                            ? _filteredItems[index]
                            : null;
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isMouseDown && _pendingDragItem != null)
                    {
                        if ((evt.mousePosition - _mouseDownPos).magnitude > 5f)
                        {
                            int visualIndex = _filteredItems.IndexOf(_pendingDragItem);

                            if (visualIndex >= 0)
                            {
                                bool started = _dragController.TryStartDrag(
                                    _pendingDragItem,
                                    visualIndex,
                                    _filteredItems.Count,
                                    evt.mousePosition
                                );

                                if (started)
                                {
                                    evt.Use();
                                }
                            }

                            _isMouseDown = false;
                            _pendingDragItem = null;
                        }
                    }
                    break;

                case EventType.MouseUp:
                    if (_isMouseDown && _pendingDragItem != null &&
                        (evt.mousePosition - _mouseDownPos).magnitude <= 5f)
                    {
                        SelectedRuleset = _pendingDragItem;
                        _onSelect?.Invoke(_pendingDragItem);
                        evt.Use();
                    }

                    _isMouseDown = false;
                    _pendingDragItem = null;
                    break;
            }
        }

        private void DrawDragOverlay()
        {
            var session = _dragController.CurrentSession;
            if (session == null) return;

            // Draw insertion line at the correct position
            float targetY = _listScreenRect.y - _scrollPosition.y + (session.TargetIndex * ItemHeight);

            if (targetY >= _listScreenRect.y && targetY <= _listScreenRect.yMax)
            {
                ListDragVisuals.DrawInsertionLine(
                    _listScreenRect.x,
                    targetY,
                    _listScreenRect.width);
            }
        }

        private void FinalizeDrop()
        {
            var session = _dragController.CurrentSession;
            if (session == null) return;

            var dragged = session.DraggedItem;
            int targetVisualIndex = session.TargetIndex;

            if (dragged == null || !_items.Contains(dragged))
            {
                _dragController.CancelDrag();
                return;
            }

            // Map visual index to main list index
            Func<int, int> visualToSourceMapping = (visualIdx) =>
            {
                if (visualIdx >= _filteredItems.Count)
                    return _items.Count;

                var targetItem = _filteredItems[visualIdx];
                int mainIdx = _items.IndexOf(targetItem);
                return mainIdx >= 0 ? mainIdx : _items.Count;
            };

            // Finalize the drag (handles reordering and event firing)
            var reason = _dragController.FinalizeDrag(_items, visualToSourceMapping);

            if (reason == DragEndReason.Success)
            {
                BetterWorkTabMod.Settings.Write();
            }
        }

        private void DrawButtons(Rect rect)
        {
            rect.SplitHorizontally(rect.height * 0.5f, out Rect topBtn, out Rect _);

            if (Widgets.ButtonText(topBtn.LeftPart(0.48f), "New Ruleset"))
            {
                var newSet = new WorkAssignmentRuleset(
                    "New Ruleset " + (_items.Count + 1),
                    new List<WorkAssignmentParameters>());

                _items.Add(newSet);
                SelectedRuleset = newSet;
                _onSelect?.Invoke(newSet);
            }

            if (SelectedRuleset != null &&
                Widgets.ButtonText(topBtn.RightPart(0.48f), "Duplicate"))
            {
                var copied = SelectedRuleset.Copy();
                _items.Add(copied);
                SelectedRuleset = copied;
                _onSelect?.Invoke(copied);
            }
        }

        private void DrawRulesetRow(Rect rect, WorkAssignmentRuleset ruleset)
        {
            Rect labelRect = rect.ContractedBy(4f);

            bool isBeingDragged = _dragController.IsActive &&
                                 _dragController.CurrentSession?.DraggedItem == ruleset;

            if (ruleset.IsDefault)
                GUI.color = Color.gray;
            else if (isBeingDragged)
                GUI.color = new Color(0.4f, 1f, 0.4f);

            string text = ruleset.Name + (ruleset.IsDefault ? " *" : "");

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            if (!ruleset.IsDefault)
            {
                Rect delRect = new Rect(rect.xMax - 24f, rect.y + 4f, 24f, 24f);
                if (Widgets.ButtonImage(delRect, TexButton.Delete, Color.white, GenUI.MouseoverColor))
                {
                    Find.WindowStack.Add(new Dialog_Confirm(
                        $"Delete {ruleset.Name}?",
                        () =>
                        {
                            _items.Remove(ruleset);
                            if (SelectedRuleset == ruleset)
                                SelectedRuleset = null;
                            BetterWorkTabMod.Settings.Write();
                        }));
                }
            }
        }
    }
}
