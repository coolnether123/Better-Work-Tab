using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Base class for column drag handler.
    /// 
    /// NOTE: RowDragHandler no longer uses this base class.
    /// It was separated because rows need special handling for
    /// stable references that columns don't require.
    /// </summary>
    public abstract class DragHandler<T>
    {
        protected readonly IWorkTabLayoutController Layout;

        public int TargetIndex { get; protected set; }
        public bool IsDragging { get; protected set; }

        protected DragHandler(IWorkTabLayoutController layout)
        {
            Layout = layout;
            IsDragging = true;
        }

        public abstract void OnDragUpdate(Vector2 mousePos);
        public abstract void OnDrawOverlay();

        public void OnDrop()
        {
            if (!IsDragging) return;
            CommitReorder();
            IsDragging = false;
        }

        public virtual void OnCancel()
        {
            IsDragging = false;
        }

        protected abstract void CommitReorder();
    }
}
