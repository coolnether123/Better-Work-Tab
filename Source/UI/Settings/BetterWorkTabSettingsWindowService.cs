using System.Reflection;
using Better_Work_Tab;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// Owns opening and toggling Better Work Tab's mod-settings window.
    /// </summary>
    internal static class BetterWorkTabSettingsWindowService
    {
        internal static bool Open(bool toggleExisting = true)
        {
            if (toggleExisting && Find.WindowStack != null && Find.WindowStack.TryRemove(typeof(Dialog_ModSettings)))
            {
                return false;
            }

#if v0_16
            Find.WindowStack.Add(new Dialog_ModSettings());
            return true;
#else
            var mod = LoadedModManager.GetMod<BetterWorkTabMod>();
            if (mod != null)
            {
#if v1_3 || v1_2 || v1_1 || (v1_0 || v0_19)
                var dialog = new Dialog_ModSettings();
                typeof(Dialog_ModSettings).GetField("selMod", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(dialog, mod);
                Find.WindowStack.Add(dialog);
#else
                Find.WindowStack.Add(new Dialog_ModSettings(mod));
#endif
                return true;
            }
#endif

            return false;
        }
    }
}
