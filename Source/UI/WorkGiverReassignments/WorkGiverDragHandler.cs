using System.Collections.Generic;
using Better_Work_Tab.Features.WorkGiverReassignments;
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

        public void BeginDrag(int index, Vector2 mousePos)
        {
            _draggedIndex = index;
            _dragStartPos = mousePos;
            _dragStartFrame = Time.frameCount;
            _isDragging = false;
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
                    // Update target index based on mouse position
                    _targetIndex = workGivers.Count;
                    for (int i = 0; i < workGivers.Count; i++)
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
                if (_isDragging && _targetIndex >= 0 && _targetIndex != _draggedIndex)
                {
                    CommitReorder(workGivers);
                }

                CancelDrag();
                evt.Use();
            }
        }

        private void CommitReorder(List<WorkGiver> workGivers)
        {
            if (_draggedIndex < 0 || _draggedIndex >= workGivers.Count) return;
            if (_targetIndex < 0 || _targetIndex > workGivers.Count) return;

            var temp = workGivers[_draggedIndex];
            workGivers.RemoveAt(_draggedIndex);
            _targetIndex = Mathf.Clamp(_targetIndex, 0, workGivers.Count);
            workGivers.Insert(_targetIndex, temp);

            WorkGiverReassignmentManager.MoveWithinWorkType(_workType.defName, temp.def.defName, _targetIndex);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        public void CancelDrag()
        {
            _draggedIndex = -1;
            _isDragging = false;
            _targetIndex = -1;
        }

        public void DrawDragOverlay(float margin, float columnWidth, float headerY, float lineHeight, 
            List<WorkGiver> workGivers, WorkGiverBaselineTracker baselineTracker)
        {
            if (!_isDragging || _targetIndex < 0) return;

            // Draw white insertion line
            float lineX = _targetIndex >= workGivers.Count 
                ? margin + workGivers.Count * columnWidth 
                : margin + _targetIndex * columnWidth;

            Widgets.DrawBoxSolid(new Rect(lineX - 1f, headerY, 2f, lineHeight), Color.white);

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
                    Widgets.DrawBoxSolid(new Rect(baselineX - 1f, headerY, 2f, lineHeight), baselineColor);
                }
            }
        }
    }
}
