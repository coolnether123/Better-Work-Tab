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

        internal VanillaWorkGridRenderer(IWorkGridDrawingSurface drawingSurface)
        {
            _drawingSurface = drawingSurface;
        }

        public string Id => RendererId;
        public int Priority => int.MinValue;

        public bool IsAvailable(in WorkTabView context) => true;

        public void Prepare(in WorkTabView context)
        {
        }

        public void Draw(in WorkTabView context)
        {
            _drawingSurface.DrawBody(in context, null);
        }

        public void HandleEvent(in WorkTabView context)
        {
        }

        public void ReleaseTransient(in WorkTabView context)
        {
        }
    }
}
