using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.Headers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Handles drag-and-drop reordering of WorkGivers within the sub-menu.
    /// </summary>
    internal class WorkGiverDragHandler
    {
        private readonly Window_WorkGiverSubMenu _window;
        private readonly WorkTypeDef _workType;
        
        private int _draggedIndex = -1;
        private Vector2 _dragStartPos;
        private int _dragStartFrame;
        private int _targetIndex = -1;
        private bool _isDragging = false;
        
        private const int DragDelayFrames = 3; // Small delay to prevent accidental drags
        private const float DragThreshold = 5f;

        public bool IsDragging => _isDragging;
        public int TargetIndex => _targetIndex;
        public int DraggedIndex => _draggedIndex;

        public WorkGiverDragHandler(Window_WorkGiverSubMenu window, WorkTypeDef workType)
        {
            _window = window;
            _workType = workType;
        }

        private List<WorkGiver> _originalWorkGivers;

        public void BeginDrag(int index, Vector2 mousePos, List<WorkGiver> workGivers)
        {
            _draggedIndex = index;
            _dragStartPos = mousePos;
            _dragStartFrame = Time.frameCount;
            _isDragging = false;
            _originalWorkGivers = new List<WorkGiver>(workGivers);
        }

        public void UpdateDrag(Vector2 mousePos, List<WorkGiver> workGivers, float margin, float columnWidth)
        {
            if (_draggedIndex < 0) return;

            var evt = Event.current;

            if (evt.type == EventType.MouseDrag)
            {
                // Check if enough frames have passed to start drag
                if (Time.frameCount - _dragStartFrame < DragDelayFrames)
                {
                    return;
                }

                if (!_isDragging && Vector2.Distance(mousePos, _dragStartPos) > DragThreshold)
                {
                    _isDragging = true;
                }

                if (_isDragging)
                {
                    // Update target index based on mouse position relative to ORIGINAL list
                    _targetIndex = _originalWorkGivers.Count;
                    for (int i = 0; i < _originalWorkGivers.Count; i++)
                    {
                        float x = margin + i * columnWidth;
                        if (mousePos.x < x + columnWidth / 2f)
                        {
                            _targetIndex = i;
                            break;
                        }
                    }
                }
            }
            else if (evt.type == EventType.MouseUp)
            {
                if (_isDragging && _originalWorkGivers != null && _draggedIndex >= 0 && _draggedIndex < _originalWorkGivers.Count)
                {
                    var draggedWg = _originalWorkGivers[_draggedIndex];
                    var hoveredWorkType = HeaderInputController.HoveredWorkType;

                    if (hoveredWorkType != null && hoveredWorkType != _workType)
                    {
                        // Dragged out onto another work type header: reassign to that work type.
                        if (!WorkGiverReassignmentManager.TryReassignWorkGiver(draggedWg.def.defName, hoveredWorkType.defName, null, out var error))
                        {
                            if (!error.NullOrEmpty())
                            {
                                MessageCompat.Message(error, MessageTypeDefOf.RejectInput, false);
                            }
                        }
                        else
                        {
                            UISoundCompat.TickHigh.PlayOneShotOnCamera();
                            _window.NotifyPriorityChanged(); // Refresh submenu to reflect removal
                            _window.Close(); // Close current submenu after sending the workgiver away
                        }
                    }
                    else if (_targetIndex >= 0 && _targetIndex != _draggedIndex)
                    {
                        CommitReorder(workGivers);
                    }
                }

                CancelDrag();
                evt.Use();
            }
        }

        private void CommitReorder(List<WorkGiver> workGivers)
        {
            if (_draggedIndex < 0 || _draggedIndex >= _originalWorkGivers.Count) return;
            
            var draggedWg = _originalWorkGivers[_draggedIndex];
            
            // Remove from current list if present
            workGivers.RemoveAll(wg => wg.def.defName == draggedWg.def.defName);
            
            // Insert at target index
            int insertIndex = Mathf.Clamp(_targetIndex, 0, workGivers.Count);
            workGivers.Insert(insertIndex, draggedWg);

            WorkGiverReassignmentManager.SyncSetPawnWorkGiverOrder(
                _window.Pawn?.thingIDNumber ?? -1, 
                _window.WorkType.defName, 
                workGivers.Select(wg => wg.def.defName).ToList()
            );
            
            _window.NotifyDragCompleted();
            UISoundCompat.TickHigh.PlayOneShotOnCamera();
        }

        public void CancelDrag()
        {
            _draggedIndex = -1;
            _isDragging = false;
            _targetIndex = -1;
            _originalWorkGivers = null;
        }

        public void DrawDragOverlay(float margin, float columnWidth, float headerY, float totalHeight, 
            List<WorkGiver> workGivers, WorkGiverBaselineTracker baselineTracker)
        {
            // Cross-worktype visual cue: yellow box on cursor or hovered header
            if (_isDragging)
            {
                Rect targetBox;
                var hoveredRect = HeaderInputController.HoveredWorkTypeRect;
                if (HeaderInputController.HoveredWorkType != null && hoveredRect.HasValue)
                {
                    targetBox = hoveredRect.Value.ExpandedBy(3f);
                }
                else
                {
                    Vector2 mouse = Event.current.mousePosition;
                    const float size = 26f;
                    targetBox = new Rect(mouse.x - size / 2f, mouse.y - size / 2f, size, size);
                }

                var fill = new Color(1f, 1f, 0f, 0.12f);
                var outline = new Color(1f, 0.9f, 0.2f, 0.9f);
                DrawBoxSolidWithOutline(targetBox, fill, outline);
            }

            if (!_isDragging || _targetIndex < 0) return;

            var settings = BetterWorkTabMod.Settings;
            int inset = settings != null ? Mathf.Clamp(settings.columnInsertionLineInset, 0, 50) : 5;
            
            // Adjust start position and height based on inset setting
            float lineY = headerY + (_window.DynamicHeaderHeight - headerY) - inset;
            float actualLineHeight = (totalHeight - (lineY - headerY));

            // Draw white insertion line - use 1f width to match vanilla-style dividers if that's what's meant
            // But centered on the column boundary
            float lineX = _targetIndex >= workGivers.Count 
                ? margin + workGivers.Count * columnWidth 
                : margin + _targetIndex * columnWidth;

            Widgets.DrawBoxSolid(new Rect(lineX, lineY, 1f, actualLineHeight), Color.white);

            // Draw yellow baseline line showing original position
            if (_draggedIndex >= 0 && _draggedIndex < workGivers.Count)
            {
                var draggedWg = workGivers[_draggedIndex];
                int targetBaselinePos = baselineTracker.CalculateBaselineTargetIndex(
                    draggedWg.def.defName, workGivers, _draggedIndex);

                if (targetBaselinePos >= 0)
                {
                    float baselineX = margin + targetBaselinePos * columnWidth;
                    var baselineColor = new Color(1f, 0.85f, 0.2f, 1f);
                    Widgets.DrawBoxSolid(new Rect(baselineX, lineY, 1f, actualLineHeight), baselineColor);
                }
            }
        }

        private static void DrawBoxSolidWithOutline(Rect rect, Color fill, Color outline)
        {
            Color oldColor = GUI.color;
            Widgets.DrawBoxSolid(rect, fill);
            GUI.color = outline;
            Widgets.DrawBox(rect);
            GUI.color = oldColor;
        }
    }
}
