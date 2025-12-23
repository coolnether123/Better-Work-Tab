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
        
        private List<WorkGiver> _workGivers;
        private WorkGiverBaselineTracker _baselineTracker;
        private WorkGiverDragHandler _dragHandler;
        
        private const float ColumnWidth = 35f;
        private const float HeaderHeight = 120f; 
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

        private void RefreshWorkGivers()
        {
            _workGivers = WorkGiverReassignmentManager.GetOrderedWorkGiversForWorkType(_workType, _pawn).ToList();
            _baselineTracker = new WorkGiverBaselineTracker(_workType, _workGivers);
            _dragHandler = new WorkGiverDragHandler(this, _workType);
        }

        public override Vector2 InitialSize
        {
            get
            {
                float width = Math.Max(250f, _workGivers.Count * ColumnWidth + Margin * 2);
                float height = HeaderHeight + PriorityRowHeight + FooterHeight + Margin * 2;
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

            DrawTitle(inRect);
            
            float headerY = 30f;
            float boxY = HeaderHeight;
            
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
                Rect headerRect = new Rect(curX, headerY, ColumnWidth, HeaderHeight - headerY);
                Rect cellRect = new Rect(curX, boxY, ColumnWidth, PriorityRowHeight);
                
                bool isMovedFromBaseline = _baselineTracker.IsMovedFromBaseline(wg.def.defName);
                DrawAngledHeader(wg, headerRect, i, isMovedFromBaseline);
                DrawPriorityBox(wg, cellRect);
                
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
            
            // Handle drag initiation from header ONLY
            if (isHovered && !_dragHandler.IsDragging)
            {
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    _dragHandler.BeginDrag(index, Event.current.mousePosition);
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

            // Notify renderer if priority changed (for refresh)
            int oldPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(_pawn, wg.def, 0);
            WorkGiverPriorityBoxRenderer.DrawPriorityBox(wg, _workType, _pawn, boxRect);
            int newPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(_pawn, wg.def, 0);
            
            if (oldPriority != newPriority)
            {
                RefreshWorkGivers();
            }
        }

        private void DrawDragOverlay(float headerY, float boxY)
        {
            float lineHeight = HeaderHeight - headerY + PriorityRowHeight;
            _dragHandler.DrawDragOverlay(Margin, ColumnWidth, headerY, lineHeight, _workGivers, _baselineTracker);
        }

        private void DrawFooter(float boxY)
        {
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
