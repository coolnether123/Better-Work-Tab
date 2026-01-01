using UnityEngine;
using RimWorld;

namespace Better_Work_Tab.UI
{
    public static class HeaderDrawingCoordinator
    {
        private static IHeaderRenderer _angledRenderer;
        private static IHeaderRenderer _vanillaRenderer;

        static HeaderDrawingCoordinator()
        {
            _angledRenderer = new AngledHeaderRenderer();
            _vanillaRenderer = new VanillaHeaderRenderer();
        }

        public static IHeaderRenderer GetActiveRenderer()
        {
            return BetterWorkTabMod.Settings.enableAngledHeaders 
                ? _angledRenderer 
                : _vanillaRenderer;
        }
    }
}
