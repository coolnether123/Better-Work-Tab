using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    internal class Window_WorkGiverSubMenu : Window
    {
        private readonly WorkTypeDef _workType;
        private readonly Pawn _pawn;
        private readonly Vector2 _triggerPos;
        
        private List<WorkGiver> _workGivers;
        private const float ColumnWidth = 32f;
        private const float HeaderHeight = 110f; 
        private const float PriorityRowHeight = 32f;
        private const float FooterHeight = 35f;
        private const float Margin = 10f;

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
        }

        public override Vector2 InitialSize
        {
            get
            {
                float width = Math.Max(220f, _workGivers.Count * ColumnWidth + Margin * 2);
                float height = HeaderHeight + PriorityRowHeight + FooterHeight + Margin * 2;
                return new Vector2(width, height);
            }
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            
            // Trigger position is already in screen coordinates from Event.current.mousePosition
            // Position the window above and centered on the trigger point
            windowRect.x = _triggerPos.x - (windowRect.width / 2f);
            windowRect.y = _triggerPos.y - windowRect.height - 5f;
            
            // Keep on screen with better boundary checks
            if (windowRect.x < 10f) windowRect.x = 10f;
            if (windowRect.xMax > Verse.UI.screenWidth - 10f) windowRect.x = Verse.UI.screenWidth - windowRect.width - 10f;
            if (windowRect.y < 10f) windowRect.y = _triggerPos.y + 5f; // If it doesn't fit above, show below
            if (windowRect.yMax > Verse.UI.screenHeight - 10f) windowRect.y = Verse.UI.screenHeight - windowRect.height - 10f;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (_workType == null)
            {
                Close();
                return;
            }

            // Title/Header Info
            Text.Font = GameFont.Small;
            string titleText = _pawn == null 
                ? $"Global: {_workType.labelShort.CapitalizeFirst()}" 
                : $"{_pawn.LabelShortCap}: {_workType.labelShort.CapitalizeFirst()}";
            Widgets.Label(new Rect(0, 0, inRect.width, 24f), titleText.Colorize(Color.gray));

            float curX = Margin;
            float baseY = HeaderHeight;
            
            for (int i = 0; i < _workGivers.Count; i++)
            {
                var wg = _workGivers[i];
                Rect cellRect = new Rect(curX, baseY, ColumnWidth, PriorityRowHeight);
                
                // 1. Draw Angled Header (stems from cellRect top)
                DrawAngledHeaderFor(wg, cellRect);
                
                // 2. Draw Priority Box
                DrawPriorityBoxFor(wg, cellRect, i);
                
                curX += ColumnWidth;
            }
            
            // 3. Footer
            Rect footerRect = new Rect(0, baseY + PriorityRowHeight + 10f, inRect.width, FooterHeight);
            DrawFooter(footerRect);
            
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
            }
        }

        private void DrawAngledHeaderFor(WorkGiver wg, Rect cellRect)
        {
            string label = wg.def.label.CapitalizeFirst();
            Vector2 textSize = Text.CalcSize(label);
            
            // Pivot at the top-center of the cell
            Vector2 pivot = new Vector2(cellRect.center.x, cellRect.y - AngledLabelDrawer.STEM_BOTTOM_GAP);
            
            // We need to bypass the 1.25x scale hardcoded compensation in AngledLabelDrawer if we want it to work here,
            // but for now let's see how it looks. If it drifts, we'll need to adjust the pivot we pass.
            
            var layout = new AngledLabelDrawer.AngledLabelLayout(label, textSize, pivot, false);
            
            // Better Work Tab's AngledLabelDrawer.Draw is static and takes layout.
            // We pass applyCompensation: false because we don't need the main tab's hardcoded offsets here.
            AngledLabelDrawer.Draw(layout, Mouse.IsOver(cellRect), applyCompensation: false);
        }

        private void DrawPriorityBoxFor(WorkGiver wg, Rect rect, int index)
        {
            // Use same box size as Skill Overlay for consistency
            float boxSize = 25f;
            float x = rect.x + (rect.width - boxSize) / 2f;
            float y = rect.y + (rect.height - boxSize) / 2f;
            Rect boxRect = new Rect(x, y, boxSize, boxSize);

            int priority = WorkGiverReassignmentManager.GetWorkGiverPriority(_pawn, wg.def, 3);


            // Draw vanilla-style work box
            if (_pawn != null)
            {
                // Check incapability
                bool incapable = false;
                if (wg.def.requiredCapacities != null)
                {
                    foreach (var cap in wg.def.requiredCapacities)
                    {
                        if (!_pawn.health.capacities.CapableOf(cap))
                        {
                            incapable = true;
                            break;
                        }
                    }
                }

                if (incapable) GUI.color = new Color(1f, 0.3f, 0.3f);
                WidgetsWork.DrawWorkBoxBackground(boxRect, _pawn, _workType);
                GUI.color = Color.white;
            }
            else
            {
                // Global mode - draw vanilla background with a dummy pawn if possible or just a clean box
                // Since DrawWorkBoxBackground requires a pawn, let's just use highlight + box for now 
                // but ensure it looks premium.
                Widgets.DrawHighlight(boxRect);
                Widgets.DrawBox(boxRect);
            }

            // Click handling
            if (Widgets.ButtonInvisible(boxRect))
            {
                CyclePriority(wg, priority);
                Event.current.Use();
            }

            // Highlight
            Widgets.DrawHighlightIfMouseover(boxRect);

            // Draw priority number (vanilla style)
            if (priority > 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                GUI.color = WidgetsWork.ColorOfPriority(priority);
                // Expand rect slightly for label to match vanilla behavior
                Widgets.Label(boxRect.ContractedBy(-3f), priority.ToString());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            
            TooltipHandler.TipRegion(boxRect, wg.def.LabelCap);
        }

        private void CyclePriority(WorkGiver wg, int current)
        {
            int next = (current + 1) % 5;
            if (Find.PlaySettings.useWorkPriorities == false && next > 0) next = 3; // checkbox mode
            
            // Set override (handles global if _pawn is null)
            WorkGiverReassignmentManager.SetPawnOverride(_pawn, wg.def, next);
            
            RefreshWorkGivers();
        }

        private void DrawFooter(Rect rect)
        {
            List<Pawn> overrides = WorkGiverReassignmentManager.GetPawnsWithOverrides(_workType);
            int count = overrides.Count;
            
            Rect textRect = new Rect(rect.x + 5f, rect.y + 5f, rect.width - 40f, 24f);
            Widgets.Label(textRect, "Pawns not following this: " + count);
            
            Rect btnRect = new Rect(rect.xMax - 30f, rect.y + 5f, 24f, 24f);
            if (Widgets.ButtonText(btnRect, "v"))
            {
                ShowPawnDropdown(overrides);
            }
            
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);
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
                        // Switch this sub-menu to this pawn
                        Find.WindowStack.Add(new Window_WorkGiverSubMenu(_workType, _triggerPos, localP));
                        this.Close();
                    }));
                }
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
