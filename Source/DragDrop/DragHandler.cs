using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;

namespace Better_Work_Tab.DragDrop
{
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
            if (!IsDragging)
                return;

            CommitReorder();
            IsDragging = false;
        }

        public void OnCancel()
        {
            IsDragging = false;
            OnCancelled();
        }

        protected abstract void CommitReorder();

        protected virtual void OnCancelled()
        {
        }
    }
}
