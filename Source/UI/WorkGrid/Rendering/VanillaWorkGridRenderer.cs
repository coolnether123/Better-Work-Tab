using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.WorkGrid.Contracts;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    /// <summary>
    /// Permanent correctness floor that delegates Work-grid drawing to RimWorld's native IMGUI path.
    /// </summary>
    internal sealed class VanillaWorkGridRenderer : IWorkGridRenderer
    {
        internal const string RendererId = "bwt.native-imgui";

        private readonly IWorkGridDrawingSurface _drawingSurface;
        private WorkTabInvalidationVersion _lastVersions;

        internal VanillaWorkGridRenderer(IWorkGridDrawingSurface drawingSurface)
        {
            _drawingSurface = drawingSurface;
        }

        public string Id => RendererId;
        public int Priority => int.MinValue;

        public bool IsAvailable(in WorkGridRenderContext context) => true;

        public void Prepare(in WorkGridRenderContext context)
        {
            PrepareInvalidation(context.InvalidationVersions);
        }

        public void Draw(in WorkGridRenderContext context)
        {
            _drawingSurface.DrawNativeWorkTable(
                context.Presentation.Table,
                context.Layout,
                context.WindowRect);
        }

        public void HandleEvent(in WorkGridRenderContext context)
        {
        }

        public void ReleaseTransient(in WorkGridRenderContext context)
        {
        }

        internal void PrepareInvalidation(WorkTabInvalidationVersion current)
        {
            bool headerTextChanged = current.HeaderText != _lastVersions.HeaderText ||
                                     current.RenderResources != _lastVersions.RenderResources;
            bool headerGeometryChanged = current.HeaderGeometry != _lastVersions.HeaderGeometry ||
                                         current.Columns != _lastVersions.Columns;

            if (headerTextChanged)
            {
                HeaderDrawingCoordinator.InvalidateCaches();
            }
            else if (headerGeometryChanged)
            {
                HeaderDrawingCoordinator.InvalidateAnimatedLayout();
            }

            _lastVersions = current;
        }
    }
}
