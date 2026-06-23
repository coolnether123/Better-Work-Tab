using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.Features;
using Better_Work_Tab.UI.RuleBuilder.State;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.RuleBuilder.Services
{
    /// <summary>
    /// Manages the drag-and-drop state for duplicating rule sets.
    /// Handles the "spring-loaded" hover logic for switching work types/priorities.
    /// </summary>
    public class RuleDragController
    {
        private readonly RuleBuilderState _state;
        
        // Drag State
        public bool IsDragging { get; private set; }
        public List<WorkAssignmentRule> DraggedRules { get; private set; }
        public WorkTypeDef SourceWorkType { get; private set; }
        public int SourcePriority { get; private set; }
        
        // Pending drag (for threshold)
        private bool _pendingDrag;
        private Vector2 _mouseDownPos;
        private Vector2 _dragVisualOffset;
        private List<WorkAssignmentRule> _pendingRules;
        private const float DragThreshold = 5f;
        
        // Hover State
        private float _hoverTimer;
        private object _lastHoveredTarget;

        // Visual colors
        private static readonly Color HandleBgColor = new Color(0.25f, 0.35f, 0.5f, 0.9f);
        private static readonly Color HandleHoverColor = new Color(0.35f, 0.5f, 0.7f, 1f);
        private static readonly Color HandleBorderColor = new Color(0.5f, 0.7f, 0.9f, 0.8f);
        private static readonly Color DragBoxBg = new Color(0.15f, 0.2f, 0.3f, 0.95f);
        private static readonly Color DragBoxBorder = new Color(0.4f, 0.6f, 0.9f, 1f);
        private static readonly Color DragBoxText = new Color(0.9f, 0.95f, 1f, 1f);

        public RuleDragController(RuleBuilderState state)
        {
            _state = state;
        }

        /// <summary>
        /// Draws a drag handle box. Returns true if drag was initiated from this handle.
        /// </summary>
        public bool DrawDragHandle(Rect rect, List<WorkAssignmentRule> rules, WorkTypeDef workType, int priority)
        {
            if (rules == null || !rules.Any()) 
            {
                // Draw disabled handle
                GUI.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                Verse.Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.3f));
                GUI.color = Color.white;
                return false;
            }

            bool isHovered = Mouse.IsOver(rect);
            
            // Draw handle background
            Color bgColor = isHovered ? HandleHoverColor : HandleBgColor;
            Verse.Widgets.DrawBoxSolid(rect, bgColor);
            
            // Draw border
            GUI.color = HandleBorderColor;
            Verse.Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;
            
            // Draw grip lines (visual indicator)
            float lineSpacing = 3f;
            float lineWidth = rect.width * 0.5f;
            float startX = rect.x + (rect.width - lineWidth) / 2f;
            // Move lines up to upper 35% to leave room for text at bottom
            float centerY = rect.y + (rect.height * 0.35f);
            
            GUI.color = new Color(1f, 1f, 1f, isHovered ? 0.8f : 0.5f);
            for (int i = -1; i <= 1; i++)
            {
                Rect lineRect = new Rect(startX, centerY + i * lineSpacing - 1f, lineWidth, 2f);
                Verse.Widgets.DrawBoxSolid(lineRect, GUI.color);
            }
            GUI.color = Color.white;
            
            // Draw icon/text
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerCenter;
            GUI.color = new Color(0.8f, 0.9f, 1f, isHovered ? 1f : 0.7f);
            // Increased height to 18f and moved up slightly to prevent clipping of descenders
            Rect labelRect = new Rect(rect.x, rect.yMax - 18f, rect.width, 18f);
            Verse.Widgets.Label(labelRect, "⇄ Drag");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            
            // Tooltip
            string tip = "BWT_DragToDuplicate".CanTranslate() 
                ? "BWT_DragToDuplicate".Translate() 
                : "Drag to duplicate these rules to another Work Type or Priority";
            TooltipHandler.TipRegion(rect, tip);
            
            // Handle mouse input
            Event evt = Event.current;
            
            if (evt.type == EventType.MouseDown && evt.button == 0 && isHovered)
            {
                _pendingDrag = true;
                _mouseDownPos = evt.mousePosition;
                _dragVisualOffset = evt.mousePosition - rect.position;
                _pendingRules = rules;
                SourceWorkType = workType;
                SourcePriority = priority;
                evt.Use();
                return false;
            }
            
            if (_pendingDrag && evt.type == EventType.MouseDrag)
            {
                if ((evt.mousePosition - _mouseDownPos).magnitude > DragThreshold)
                {
                    StartDrag(_pendingRules);
                    _pendingDrag = false;
                    _pendingRules = null;
                    return true;
                }
            }
            
            if (_pendingDrag && evt.type == EventType.MouseUp)
            {
                _pendingDrag = false;
                _dragVisualOffset = Vector2.zero;
                _pendingRules = null;
            }
            
            return false;
        }

        private void StartDrag(List<WorkAssignmentRule> rules)
        {
            if (rules == null || !rules.Any()) return;

            // Clone rules immediately to capture their state at start of drag
            DraggedRules = rules.Select(r => r.Copy()).ToList();
            IsDragging = true;
            _hoverTimer = 0f;
            _lastHoveredTarget = null;
            
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        public void EndDrag()
        {
            IsDragging = false;
            DraggedRules = null;
            _pendingDrag = false;
            _dragVisualOffset = Vector2.zero;
            _hoverTimer = 0f;
            _lastHoveredTarget = null;
            SourceWorkType = null;
            SourcePriority = -1;
        }

        /// <summary>
        /// Updates hover logic for WorkType. Switching occurs after the configured delay.
        /// </summary>
        public void NotifyWorkTypeHover(WorkTypeDef workType)
        {
            if (!IsDragging || workType == null) return;
            
            HandleHover(workType, () =>
            {
                if (_state.SelectedWorkType != workType)
                {
                    _state.SelectedWorkType = workType;
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
            });
        }

        /// <summary>
        /// Updates hover logic for Priority. Switching occurs after the configured delay.
        /// </summary>
        public void NotifyPriorityHover(int priority)
        {
            if (!IsDragging) return;

            HandleHover(priority, () =>
            {
                if (_state.SelectedPriority != priority)
                {
                    _state.SelectedPriority = priority;
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
            });
        }

        private void HandleHover(object target, System.Action onSwitch)
        {
            float delay = BetterWorkTabMod.Settings.dragHoverDelay;

            if (_lastHoveredTarget != null && _lastHoveredTarget.Equals(target))
            {
                _hoverTimer += Time.deltaTime;
                if (_hoverTimer >= delay)
                {
                    onSwitch?.Invoke();
                    _hoverTimer = 0f;
                }
            }
            else
            {
                _lastHoveredTarget = target;
                _hoverTimer = 0f;
            }
        }

        public void Update()
        {
            if (!IsDragging) return;

            // If mouse is released outside drop zone, cancel drag
            Event evt = Event.current;
            if ((evt != null && (evt.rawType == EventType.MouseUp || evt.type == EventType.MouseUp)) ||
                !UnityEngine.Input.GetMouseButton(0))
            {
                EndDrag();
            }
        }

        public void DrawDragVisual()
        {
            if (!IsDragging || DraggedRules == null) return;

            var mousePos = Verse.UI.MousePosUIInvertedUseEventIfCan;
            
            // Determine size based on content
            int rulesCount = DraggedRules.Count;
            string countText = $"{rulesCount} rule{(rulesCount != 1 ? "s" : "")}";
            string fromText = SourceWorkType != null 
                ? $"from {SourceWorkType.labelShort}" 
                : "";
            
            float boxWidth = 180f;
            float boxHeight = 50f;
            
            Rect drawRect = new Rect(
                mousePos.x - _dragVisualOffset.x,
                mousePos.y - _dragVisualOffset.y,
                boxWidth,
                boxHeight);

            // Draw floating box using ImmediateWindow for proper layering
            Find.WindowStack.ImmediateWindow(984352, drawRect, WindowLayer.Super, () =>
            {
                Rect innerRect = new Rect(0, 0, drawRect.width, drawRect.height);
                
                // Background with gradient effect (simulated)
                Verse.Widgets.DrawBoxSolid(innerRect, DragBoxBg);
                
                // Accent bar on left
                Rect accentBar = new Rect(0, 0, 4f, innerRect.height);
                Verse.Widgets.DrawBoxSolid(accentBar, DragBoxBorder);
                
                // Border
                GUI.color = DragBoxBorder;
                Verse.Widgets.DrawBox(innerRect, 2);
                
                // Icon area
                Rect iconRect = new Rect(10f, 8f, 24f, 24f);
                GUI.color = DragBoxBorder;
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Verse.Widgets.Label(iconRect, "⇄");
                
                // Main text
                GUI.color = DragBoxText;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect textRect = new Rect(38f, 6f, innerRect.width - 44f, 20f);
                Verse.Widgets.Label(textRect, "Duplicating " + countText);
                
                // Subtext
                if (!string.IsNullOrEmpty(fromText))
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(0.7f, 0.8f, 0.9f, 0.8f);
                    Rect subRect = new Rect(38f, 26f, innerRect.width - 44f, 18f);
                    Verse.Widgets.Label(subRect, fromText);
                }
                
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            });
        }
    }
}
