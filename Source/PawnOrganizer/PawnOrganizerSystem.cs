using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
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
        private List<PawnTableElement> _displayElements = new List<PawnTableElement>();

        public PawnOrganizerSystem()
        {
            Instance = this;
        }

        /// <summary>
        /// Called from Harmony patch during OnGUI phase.
        /// Allows system to respond to input (group creation, drag, etc.).
        /// </summary>
        public void OnWorkTabGUI(PawnTable table, IWorkTabRowColumnAPI api)
        {
            _currentWorklist = Current.Game.GetComponent<GameComponent_WorkloadSaver>()?.CurrentWorklist;
            if (_currentWorklist == null) return;

            HandleGroupCreation(api);
            HandleGroupDragDrop(api);
            HandleColoringInput(api);
        }

        /// <summary>
        /// Called from Harmony patch during Repaint phase.
        /// Renders divider rows, pawn colors, group indicators.
        /// </summary>
        public void DrawOrganization(PawnTable table, IWorkTabRowColumnAPI api)
        {
            if (_currentWorklist == null) return;

            // Rebuild display list (includes dividers + pawns)
            RebuildDisplayElements(api);

            // Draw dividers and color backgrounds
            DrawDividers(api);
            DrawPawnColors(api);
        }

        private void RebuildDisplayElements(IWorkTabRowColumnAPI api)
        {
            _displayElements.Clear();
            var visiblePawns = new HashSet<Pawn>(api.GetVisiblePawns());

            foreach (var group in _currentWorklist.PawnGroups)
            {
                _displayElements.Add(new DividerElement(group));
                
                foreach (var pawn in group.Members.Where(p => visiblePawns.Contains(p)))
                {
                    _displayElements.Add(new PawnElement(pawn));
                }
            }

            // Add ungrouped pawns
            var grouped = new HashSet<Pawn>(_currentWorklist.PawnGroups.SelectMany(g => g.Members));
            foreach (var pawn in api.GetVisiblePawns().Where(p => !grouped.Contains(p)))
            {
                _displayElements.Add(new PawnElement(pawn));
            }
        }

        private void DrawDividers(IWorkTabRowColumnAPI api)
        {
            float currentY = api.GetTableOrigin().y + api.GetHeaderHeight() - api.GetScrollPosition().y;

            foreach (var element in _displayElements)
            {
                if (element.IsDivider)
                {
                    var divider = element as DividerElement;
                    var rect = new Rect(api.GetTableOrigin().x, currentY, api.GetTableSize().x - 16f, 24f);

                    // Draw divider background
                    Widgets.DrawBoxSolid(rect, divider.Group.GroupColor * 0.3f);
                    Widgets.DrawBox(rect, 1);

                    // Draw group name
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(rect.ContractedBy(4f), divider.Group.GroupName);
                    Text.Anchor = TextAnchor.UpperLeft;

                    // Collapse/expand button
                    Rect toggleRect = new Rect(rect.xMax - 24f, rect.y, 24f, 24f);
                    if (Widgets.ButtonImage(toggleRect, divider.Group.IsCollapsed ? TexButton.Plus : TexButton.Minus))
                    {
                        divider.Group.IsCollapsed = !divider.Group.IsCollapsed;
                    }

                    currentY += 24f;
                }
                else if (element is PawnElement pe)
                {
                    // This pawn's row is drawn normally by vanilla PawnTable
                    var rowRect = api.GetPawnRowRect(pe.Pawn);
                    if (rowRect.HasValue)
                        currentY += rowRect.Value.height;
                }
            }
        }

        private void DrawPawnColors(IWorkTabRowColumnAPI api)
        {
            foreach (var element in _displayElements.OfType<PawnElement>())
            {
                var rect = api.GetPawnRowRect(element.Pawn);
                if (!rect.HasValue) continue;

                // Find which group this pawn belongs to
                var group = _currentWorklist.PawnGroups.FirstOrDefault(g => g.Members.Contains(element.Pawn));
                if (group != null)
                {
                    Widgets.DrawBoxSolid(rect.Value, group.GroupColor * 0.15f);
                }
            }
        }

        private void HandleGroupCreation(IWorkTabRowColumnAPI api)
        {
            if (!Input.GetKeyDown(KeyCode.LeftShift)) return;

            var pawn = api.GetPawnAtMousePosition(Event.current.mousePosition);
            if (pawn == null) return;

            if (Widgets.ButtonInvisible(api.GetPawnRowRect(pawn) ?? Rect.zero))
            {
                var newGroup = new PawnGroup { GroupName = $"Group {_currentWorklist.PawnGroups.Count + 1}" };
                newGroup.Members.Add(pawn);
                _currentWorklist.PawnGroups.Add(newGroup);
            }
        }

        private void HandleGroupDragDrop(IWorkTabRowColumnAPI api)
        {
            // TODO: Implement Ctrl+Drag to add to group
        }

        private void HandleColoringInput(IWorkTabRowColumnAPI api)
        {
            // TODO: Implement color painting via hotkeys or UI button
        }
    }
}
