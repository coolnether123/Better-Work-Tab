using Better_Work_Tab.Input;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.VisualFeedback;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ColumnManagement
{
    /// <summary>
    /// Handles column resizing via draggable separators.
    /// Single responsibility: Column width adjustment logic.
    /// </summary>
    public class ColumnResizeHandler
    {
        private const float HandleWidth = 6f;
        private const float MinColumnWidth = 30f;
        private const float MaxColumnWidth = 300f;

        private PawnColumnDef _resizingColumn;
        private float _resizeStartWidth;
        private float _resizeStartMouseX;

        private readonly Persistence.ColumnStateManager _columnStateManager;

        public ColumnResizeHandler(Persistence.ColumnStateManager columnStateManager)
        {
            _columnStateManager = columnStateManager;
        }

        public bool HandleInput(InputState input, IWorkTabLayoutController layout)
        {
            if (layout == null) return false;

            // Check if we're currently resizing
            if (_resizingColumn != null)
            {
                if (input.IsMouseDrag)
                {
                    HandleResize(input, layout);
                    return true;
                }
                else if (input.IsMouseUp)
                {
                    EndResize();
                    return true;
                }
            }

            // Check if we're starting a resize
            if (input.IsMouseDown && input.Button == 0)
            {
                foreach (var column in layout.Columns)
                {
                    Rect handleRect = GetResizeHandleRect(column);
                    if (handleRect.Contains(input.MousePosition))
                    {
                        StartResize(column, input.MousePosition.x);
                        return true;
                    }
                }
            }

            return false;
        }

        public void DrawResizeHandles(IWorkTabLayoutController layout, Vector2 mousePosition)
        {
            if (layout == null) return;

            foreach (var column in layout.Columns)
            {
                Rect handleRect = GetResizeHandleRect(column);
                bool isHovered = handleRect.Contains(mousePosition);

                HoverEffectManager.DrawResizeHandle(handleRect, isHovered);

                // Change cursor on hover
                if (isHovered || _resizingColumn == column.Column)
                {
                    // MouseoverSounds.DoRegion(handleRect);
                    // Note: RimWorld doesn't support custom cursors easily
                }
            }
        }

        private Rect GetResizeHandleRect(WorkTabLayoutColumn column)
        {
            float handleX = column.HeaderRect.xMax - (HandleWidth / 2f);
            return new Rect(
                handleX,
                column.HeaderRect.y,
                HandleWidth,
                column.HeaderRect.height);
        }

        private void StartResize(WorkTabLayoutColumn column, float mouseX)
        {
            _resizingColumn = column.Column;
            _resizeStartWidth = column.Width;
            _resizeStartMouseX = mouseX;
            AudioManager.PlayColumnResize();
        }

        private void HandleResize(InputState input, IWorkTabLayoutController layout)
        {
            if (_resizingColumn == null) return;

            float delta = input.MousePosition.x - _resizeStartMouseX;
            float newWidth = Mathf.Clamp(_resizeStartWidth + delta, MinColumnWidth, MaxColumnWidth);

            _columnStateManager.SaveColumnWidth(_resizingColumn, newWidth);

            // Trigger layout rebuild
                            layout.Rebuild(layout.Table, 
                                Verse.Current.Game.GetComponent<Features.Workloads.GameComponent_BWTWorldSettings>()?.CurrentWorklist, 
                                layout.TableOrigin);        }

        private void EndResize()
        {
            _resizingColumn = null;
            AudioManager.PlayClick();
        }
    }
}
