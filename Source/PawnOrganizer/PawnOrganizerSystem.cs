using Better_Work_Tab.DragDrop;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.PawnOrganizer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer
{
    /// <summary>
    /// Central access point for the new layout controller and drag handlers.
    /// </summary>
    public class PawnOrganizerSystem
    {
        public static PawnOrganizerSystem Instance { get; private set; }

        private readonly WorkTabLayoutController _layout = new WorkTabLayoutController();
        private readonly RowDragController _rowDrag = new RowDragController();
        private readonly ColumnDragController _columnDrag = new ColumnDragController();

        private Worklist _currentWorklist;

        public IWorkTabLayoutController Layout => _layout;
        public Worklist CurrentWorklist => _currentWorklist;
        public RowDragController RowDrag => _rowDrag;
        public ColumnDragController ColumnDrag => _columnDrag;

        public PawnOrganizerSystem()
        {
            Instance = this;
        }

        /// <summary>
        /// Refresh layout snapshot for the provided PawnTable/origin pair.
        /// </summary>
        public void UpdateState(PawnTable table, Vector2 origin)
        {
            if (Current.Game == null)
            {
                _currentWorklist = null;
                return;
            }

            _currentWorklist = Current.Game.GetComponent<GameComponent_BWTWorldSettings>()?.CurrentWorklist;
            if (_currentWorklist == null)
            {
                _layout.Rebuild(table, null, origin);
                return;
            }

            _layout.Rebuild(table, _currentWorklist, origin);
        }

        /// <summary>
        /// Handle mouse/keyboard input routed from the custom Work tab window.
        /// </summary>
        public void HandleInput(Event evt)
        {
            if (_currentWorklist == null || evt == null)
            {
                return;
            }

            _columnDrag.HandleInput(evt, _layout);
            _rowDrag.HandleInput(evt, _layout);

            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.shift)
            {
                TryCreateDivider(evt.mousePosition);
                evt.Use();
            }
        }

        /// <summary>
        /// Draw drag overlays (ghost row/column, insertion guides).
        /// </summary>
        public void DrawDragOverlays()
        {
            _columnDrag.DrawOverlay(_layout);
            _rowDrag.DrawOverlay(_layout);
        }

        private void TryCreateDivider(Vector2 mousePosition)
        {
            if (!_layout.TryGetRowAt(mousePosition, out var row) || row.Pawn == null)
            {
                return;
            }

            string label = $"Divider {_currentWorklist.Dividers.Count + 1}";
            var divider = _layout.InsertDividerAfter(row.Pawn, label);
            if (divider != null)
            {
                Messages.Message($"Created {divider.DividerName}", MessageTypeDefOf.NeutralEvent, false);
                _layout.Rebuild(_layout.Table, _currentWorklist, _layout.TableOrigin);
            }
        }
    }
}
