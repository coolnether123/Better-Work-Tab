using Better_Work_Tab.UI.Headers;
using Spine.RimWorld.WorkTab.Rendering;

namespace Better_Work_Tab.UI.Rendering
{
    internal sealed class NativeImGuiWorkTabRenderModule : IWorkTabRenderModule
    {
        private readonly MainTabWindow_BetterWork _host;
        private WorkTabInvalidationVersion _lastVersions;

        internal NativeImGuiWorkTabRenderModule(MainTabWindow_BetterWork host)
        {
            _host = host;
        }

        public string Id => "bwt.native-imgui";
        public int Priority => int.MinValue;
        public bool IsAvailable => true;

        public void Render(in WorkTabFrameContext context)
        {
            PrepareFrame(context.Versions);
            _host.DrawNativeWorkTable(context.Table, context.Layout, context.WindowRect);
        }

        internal void PrepareFrame(WorkTabInvalidationVersion current)
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
