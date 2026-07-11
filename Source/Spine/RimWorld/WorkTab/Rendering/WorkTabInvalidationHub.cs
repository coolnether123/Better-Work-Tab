using System.Threading;

namespace Spine.RimWorld.WorkTab.Rendering
{
    /// <summary>
    /// Renderer-neutral invalidation ledger. Modules compare immutable versions when active.
    /// </summary>
    public static class WorkTabInvalidationHub
    {
        private static int _presentation;
        private static int _rows;
        private static int _columns;
        private static int _headerGeometry;
        private static int _headerText;
        private static int _viewport;
        private static int _windowSize;
        private static int _renderResources;

        public static WorkTabInvalidationVersion Current => new WorkTabInvalidationVersion(
            Volatile.Read(ref _presentation),
            Volatile.Read(ref _rows),
            Volatile.Read(ref _columns),
            Volatile.Read(ref _headerGeometry),
            Volatile.Read(ref _headerText),
            Volatile.Read(ref _viewport),
            Volatile.Read(ref _windowSize),
            Volatile.Read(ref _renderResources));

        public static void Invalidate(WorkTabDirtyFlags flags)
        {
            if ((flags & WorkTabDirtyFlags.Presentation) != 0) Interlocked.Increment(ref _presentation);
            if ((flags & WorkTabDirtyFlags.Rows) != 0) Interlocked.Increment(ref _rows);
            if ((flags & WorkTabDirtyFlags.Columns) != 0) Interlocked.Increment(ref _columns);
            if ((flags & WorkTabDirtyFlags.HeaderGeometry) != 0) Interlocked.Increment(ref _headerGeometry);
            if ((flags & WorkTabDirtyFlags.HeaderText) != 0) Interlocked.Increment(ref _headerText);
            if ((flags & WorkTabDirtyFlags.Viewport) != 0) Interlocked.Increment(ref _viewport);
            if ((flags & WorkTabDirtyFlags.WindowSize) != 0) Interlocked.Increment(ref _windowSize);
            if ((flags & WorkTabDirtyFlags.RenderResources) != 0) Interlocked.Increment(ref _renderResources);
        }
    }
}
