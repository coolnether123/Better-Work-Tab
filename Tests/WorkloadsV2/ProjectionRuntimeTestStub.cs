namespace Better_Work_Tab.UI.WorkGrid.Projection
{
    // The production runtime bridge is RimWorld-bound. The linked projected
    // provider only needs these invalidation/pass tokens for pure tests.
    internal static class WorkTabEffectiveStateRuntime
    {
        internal static long PreparingRenderPassId => 0L;
        internal static long CurrentRenderPassId => 0L;

        internal static void InvalidateRenderPass()
        {
        }
    }
}
