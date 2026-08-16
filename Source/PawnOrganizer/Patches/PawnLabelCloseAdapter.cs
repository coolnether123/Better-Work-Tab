using RimWorld;

namespace Better_Work_Tab.PawnOrganizer.Patches
{
    internal static class PawnLabelCloseAdapter
    {
        internal static void MaybeCloseWorkTab(MainTabsRoot root, bool playSound)
        {
            if (ShouldCloseWorkTab()) root?.EscapeCurrentTab(playSound);
        }

        internal static bool ShouldCloseWorkTab()
        {
            return !(BetterWorkTabMod.Settings?.disableLeftClickClose ?? false);
        }
    }
}
