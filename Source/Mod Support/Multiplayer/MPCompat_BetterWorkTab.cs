#if !v1_2 && !v1_1
using Multiplayer.API;
using Verse;

namespace Better_Work_Tab.Mod_Support.Multiplayer
{
    [StaticConstructorOnStartup]
    internal static class MPCompat_BetterWorkTab
    {
        static MPCompat_BetterWorkTab()
        {
            if (!MP.enabled)
                return;

            MP.RegisterAll();
        }
    }
}
#endif
