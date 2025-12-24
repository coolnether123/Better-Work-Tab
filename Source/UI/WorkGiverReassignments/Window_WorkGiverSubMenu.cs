using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Main window for the WorkGiver sub-menu, allowing per-WorkGiver priority customization and reordering.
    /// </summary>
    internal class Window_WorkGiverSubMenu : Window
    {
        private readonly WorkTypeDef _workType;
        private readonly Pawn _pawn;
        private readonly Vector2 _triggerPos;

        public WorkTypeDef WorkType => _workType;
        public Pawn Pawn => _pawn;
        public float DynamicHeaderHeight => _dynamicHeaderHeight;

        private List<WorkGiver> _workGivers;
        private WorkGiverBaselineTracker _baselineTracker;
        private WorkGiverDragHandler _dragHandler;
        
        private bool _needsRefresh = false;
        private float _dynamicHeaderHeight = 120f;
        
        private const float ColumnWidth = 35f;
        private const float PriorityRowHeight = 45f;
        private const float FooterHeight = 50f;
        private const float Margin = 12f;

        public Window_WorkGiverSubMenu(WorkTypeDef workType, Vector2 triggerPos, Pawn pawn = null)
        {
            _workType = workType;
            _pawn = pawn;
            _triggerPos = triggerPos;
            
            RefreshWorkGivers();
            
            doCloseX = false;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true; 
            preventCameraMotion = true;
            shadowAlpha = 0.6f;
        }

        public void NotifyPriorityChanged()
        {
            _needsRefresh = true;
        }

        public void NotifyDragCompleted()
        {
            _baselineTracker.UpdateMovedStatus(_workGivers);
        }

        private void RefreshWorkGivers()
        {
            _workGivers = WorkGiverReassignmentManager.GetOrderedWorkGiversForWorkType(_workType, _pawn).ToList();
            _baselineTracker = new WorkGiverBaselineTracker(_workType, _workGivers, _pawn);
            if (_dragHandler == null)
            {
                _dragHandler = new WorkGiverDragHandler(this, _workType);
            }
            
            CalculateHeaderHeight();
            _needsRefresh = false;
        }

        private void CalculateHeaderHeight()
        {
            float maxHeight = 80f; // Minimum baseline
            Text.Font = GameFont.Small;
            
            foreach (var wg in _workGivers)
            {
                string label = wg.def.label.CapitalizeFirst();
                if (_baselineTracker.IsMovedFromBaseline(wg.def.defName))
                {
                    label += " *";
                }
                
                Vector2 size = Text.CalcSize(label);
                // For 45 degree rotation, the vertical space needed is approximately the text width
                // Add extra padding for very long labels
                float rotatedHeight = size.x * 0.85f; // Diagonal height
                if (rotatedHeight > maxHeight)
                {
                    maxHeight = rotatedHeight;
                }
            }
            
            _dynamicHeaderHeight = Mathf.Min(maxHeight + 30f, 250f); // Cap at 250px to prevent extreme cases
        }

        public override Vector2 InitialSize
        {
            get
            {
                float desiredWidth = _workGivers.Count * ColumnWidth + Margin * 2;
                float maxAllowedWidth = Verse.UI.screenWidth - 40f; // Leave 20px margin on each side
                float width = Mathf.Max(250f, Mathf.Min(desiredWidth, maxAllowedWidth));
                
                float height = _dynamicHeaderHeight + PriorityRowHeight + FooterHeight + Margin * 2;
                return new Vector2(width, height);
            }
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            
            windowRect.x = _triggerPos.x - (windowRect.width / 2f);
            windowRect.y = _triggerPos.y - windowRect.height - 5f;
            
            if (windowRect.x < 10f) windowRect.x = 10f;
            if (windowRect.xMax > Verse.UI.screenWidth - 10f) windowRect.x = Verse.UI.screenWidth - windowRect.width - 10f;
            if (windowRect.y < 10f) windowRect.y = _triggerPos.y + 5f;
            if (windowRect.yMax > Verse.UI.screenHeight - 10f) windowRect.y = Verse.UI.screenHeight - windowRect.height - 10f;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (_workType == null)
            {
                Close();
                return;
            }

            if (_needsRefresh)
            {
                RefreshWorkGivers();
            }

            DrawTitle(inRect);
            
            float headerY = 30f;
            float boxY = _dynamicHeaderHeight;
            
            _dragHandler.UpdateDrag(Event.current.mousePosition, _workGivers, Margin, ColumnWidth);
            
            DrawWorkGiverColumns(headerY, boxY);
            DrawDragOverlay(headerY, boxY);
            DrawFooter(boxY);
            
            HandleEscapeKey();
        }

        private void DrawTitle(Rect inRect)
        {
            Text.Font = GameFont.Small;
            string titleText = _pawn == null 
                ? $"Global: {_workType.labelShort.CapitalizeFirst()}" 
                : $"{_pawn.LabelShortCap}: {_workType.labelShort.CapitalizeFirst()}";
            Widgets.Label(new Rect(0, 0, inRect.width, 24f), titleText.Colorize(Color.gray));
        }

        private void DrawWorkGiverColumns(float headerY, float boxY)
        {
            float curX = Margin;
            
            for (int i = 0; i < _workGivers.Count; i++)
            {
                var wg = _workGivers[i];
                Rect headerRect = new Rect(curX, headerY, ColumnWidth, _dynamicHeaderHeight - headerY);
                Rect cellRect = new Rect(curX, boxY, ColumnWidth, PriorityRowHeight);
                
                bool isMovedFromBaseline = _baselineTracker.IsMovedFromBaseline(wg.def.defName);
                DrawAngledHeader(wg, headerRect, i, isMovedFromBaseline);
                DrawPriorityBox(wg, cellRect);
                
                // Draw standard column divider line (1px grey) to match main work tab
                if (i < _workGivers.Count - 1)
                {
                    float dividerX = curX + ColumnWidth;
                    // Draw divider from bottom of header area through priority row
                    Rect dividerRect = new Rect(dividerX, boxY, 1f, PriorityRowHeight);
                    Widgets.DrawBoxSolid(dividerRect, new Color(1f, 1f, 1f, 0.1f)); // Match vanilla/BWT subtle divider
                }

                curX += ColumnWidth;
            }
        }

        private void DrawAngledHeader(WorkGiver wg, Rect headerRect, int index, bool isMovedFromBaseline)
        {
            string label = wg.def.label.CapitalizeFirst();
            if (isMovedFromBaseline)
            {
                label += " *";
            }
            
            Vector2 textSize = Text.CalcSize(label);
            Vector2 pivot = new Vector2(headerRect.center.x, headerRect.yMax - AngledLabelDrawer.STEM_BOTTOM_GAP);
            
            var layout = new AngledLabelDrawer.AngledLabelLayout(label, textSize, pivot, isMovedFromBaseline);
            bool isHovered = Mouse.IsOver(headerRect);
            AngledLabelDrawer.Draw(layout, isHovered, applyCompensation: false);
            
            // Guard: Don't start drag if event was already consumed (e.g., by priority box click)
            if (Event.current.type == EventType.Used) return;
            
            // Handle drag initiation from header ONLY
            if (isHovered && !_dragHandler.IsDragging)
            {
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    _dragHandler.BeginDrag(index, Event.current.mousePosition, _workGivers);
                    Event.current.Use();
                }
            }
        }

        private void DrawPriorityBox(WorkGiver wg, Rect cellRect)
        {
            const float boxSize = 25f;
            float x = cellRect.x + (cellRect.width - boxSize) / 2f;
            float y = cellRect.y + (cellRect.height - boxSize) / 2f;
            Rect boxRect = new Rect(x, y, boxSize, boxSize);

            // Just draw the priority box - no need to refresh ordering when priority changes
            // The displayed priority will update via the renderer without reordering
            WorkGiverPriorityBoxRenderer.DrawPriorityBox(wg, _workType, _pawn, boxRect);
        }

        private void DrawDragOverlay(float headerY, float boxY)
        {
            float totalHeight = (boxY - headerY) + PriorityRowHeight;
            _dragHandler.DrawDragOverlay(Margin, ColumnWidth, headerY, totalHeight, _workGivers, _baselineTracker);
        }

        private void DrawFooter(float boxY)
        {
            // Only show footer in global window, not pawn-specific windows
            if (_pawn != null) return;
            
            Rect footerRect = new Rect(0, boxY + PriorityRowHeight + 5f, windowRect.width - 2 * Margin, FooterHeight);
            Widgets.DrawLineHorizontal(footerRect.x, footerRect.y, footerRect.width);
            
            List<Pawn> overrides = WorkGiverReassignmentManager.GetPawnsWithOverrides(_workType);
            int count = overrides.Count;
            
            Rect textRect = new Rect(footerRect.x + 5f, footerRect.y + 8f, footerRect.width - 40f, 24f);
            Widgets.Label(textRect, $"Pawns with overrides: {count}");
            
            Rect btnRect = new Rect(footerRect.xMax - 30f, footerRect.y + 8f, 24f, 24f);
            if (Widgets.ButtonText(btnRect, "▼"))
            {
                ShowPawnDropdown(overrides);
            }
        }

        private void ShowPawnDropdown(List<Pawn> pawns)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (pawns.Count == 0)
            {
                options.Add(new FloatMenuOption("None", null));
            }
            else
            {
                foreach (var p in pawns)
                {
                    var localP = p;
                    options.Add(new FloatMenuOption(localP.LabelShortCap, () => {
                        Find.WindowStack.Add(new Window_WorkGiverSubMenu(_workType, _triggerPos, localP));
                        this.Close();
                    }));
                }
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void HandleEscapeKey()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
            }
        }
    }
}
