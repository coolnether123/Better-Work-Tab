using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.UI.RuleBuilder.State;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.Widgets
{
    /// <summary>
    /// Simple drag-and-drop controller for reordering active conditions within a rule.
    /// Maintains order by shuffling WorkAssignmentParameters.ActiveConditions.
    /// </summary>
    public class ConditionDragController
    {
        private string _dragKey;
        private float _grabOffsetY;
        private float? _previewLineY;

        public bool IsDragging => !string.IsNullOrEmpty(_dragKey);
        public float? PreviewLineY => _previewLineY;

        /// <summary>
        /// Handles drag interactions and updates the active condition order.
        /// </summary>
        /// <param name="rows">Row bounds keyed by condition field name.</param>
        /// <param name="parameters">Rule parameters whose ActiveConditions will be reordered.</param>
        /// <param name="state">Rule builder state for change notifications.</param>
        /// <returns>True if the UI should redraw due to a change.</returns>
        public bool HandleDrag(List<(string key, Rect rect)> rows, WorkAssignmentParameters parameters, RuleBuilderState state)
        {
            var evt = Event.current;
            if (evt == null || rows == null || rows.Count <= 1 || parameters == null)
                return false;

            // Begin drag
            if (!IsDragging && evt.type == EventType.MouseDown && evt.button == 0)
            {
                foreach (var row in rows)
                {
                    if (row.rect.Contains(evt.mousePosition))
                    {
                        _dragKey = row.key;
                        _grabOffsetY = evt.mousePosition.y - row.rect.y;
                        evt.Use();
                        return true;
                    }
                }
            }

            // Update drag
            if (IsDragging && (evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove))
            {
                float adjustedY = evt.mousePosition.y - _grabOffsetY;
                int targetIndex = CalculateTargetIndex(rows, adjustedY);
                _previewLineY = CalculatePreviewLineY(rows, targetIndex);
                if (Reorder(parameters, _dragKey, targetIndex))
                {
                    state?.NotifyRulesModified();
                }
                evt.Use();
                return true;
            }

            // End drag
            if (IsDragging && (evt.type == EventType.MouseUp || EventCompat.IsMouseLeaveWindow(evt.type)))
            {
                _dragKey = null;
                _grabOffsetY = 0f;
                _previewLineY = null;
                evt.Use();
                return true;
            }

            return false;
        }

        private static int CalculateTargetIndex(List<(string key, Rect rect)> rows, float yPos)
        {
            var ordered = rows.OrderBy(r => r.rect.y).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                if (yPos < ordered[i].rect.center.y)
                {
                    return i;
                }
            }
            return ordered.Count;
        }

        private static bool Reorder(WorkAssignmentParameters parameters, string key, int targetIndex)
        {
            if (string.IsNullOrEmpty(key) || targetIndex < 0)
                return false;

            parameters.ActiveConditions ??= new List<string>();

            // Seed the order with current active conditions; append missing keys from row set.
            if (!parameters.ActiveConditions.Contains(key))
            {
                parameters.ActiveConditions.Add(key);
            }

            int currentIndex = parameters.ActiveConditions.IndexOf(key);
            if (currentIndex == targetIndex || currentIndex < 0)
                return false;

            parameters.ActiveConditions.RemoveAt(currentIndex);
            targetIndex = Mathf.Clamp(targetIndex, 0, parameters.ActiveConditions.Count);
            parameters.ActiveConditions.Insert(targetIndex, key);
            return true;
        }

        private static float CalculatePreviewLineY(List<(string key, Rect rect)> rows, int targetIndex)
        {
            var ordered = rows.OrderBy(r => r.rect.y).ToList();
            if (ordered.Count == 0)
                return 0f;

            if (targetIndex <= 0)
            {
                return ordered.First().rect.y;
            }

            if (targetIndex >= ordered.Count)
            {
                return ordered.Last().rect.yMax;
            }

            return ordered[targetIndex - 1].rect.yMax;
        }
    }
}
