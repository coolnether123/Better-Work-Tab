#if v0_16
using System;
using System.Reflection;
using Better_Work_Tab.UI;
using RimWorld;
using Verse;

namespace Better_Work_Tab
{
    internal static class LegacyMainTabPatcher
    {
        public static void ReplaceWorkTabWindow()
        {
            MainTabDef workTab = DefDatabase<MainTabDef>.GetNamed("Work", false);
            if (workTab == null)
            {
                Log.Error("[Better Work Tab] Could not find legacy Work MainTabDef.");
                return;
            }

            try
            {
                workTab.windowClass = typeof(MainTabWindow_BetterWork);
                ClearCachedWindow(workTab);
            }
            catch (Exception ex)
            {
                Log.Error("[Better Work Tab] Failed to replace legacy Work tab window: " + ex);
            }
        }

        private static void ClearCachedWindow(MainTabDef tab)
        {
            FieldInfo windowField = typeof(MainTabDef).GetField(
                "windowInt",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (windowField != null)
            {
                windowField.SetValue(tab, null);
            }
        }
    }
}
#endif
