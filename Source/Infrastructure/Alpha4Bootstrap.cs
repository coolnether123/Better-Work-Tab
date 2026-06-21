#if vAlpha4
using System;
using Better_Work_Tab.UI;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Better_Work_Tab
{
    public class Alpha4BootstrapDef : Def
    {
        public Alpha4BootstrapDef()
        {
            Legacy016Bootstrap.Initialize();
        }
    }

    [HarmonyPatch(typeof(Tab_Overview_Work), "PanelOnGUI")]
    internal static class Alpha4Patch_TabOverviewWork_PanelOnGUI
    {
        private static readonly MainTabWindow_BetterWork Renderer = new MainTabWindow_BetterWork();
        private static bool loggedFallback;

        private static bool Prefix(Rect fillRect)
        {
            try
            {
                Legacy016Bootstrap.Initialize();

                Rect innerRect = fillRect.GetInnerRect(10f);
                GUI.BeginGroup(innerRect);
                try
                {
                    Renderer.DoAlpha4PanelContents(new Rect(0f, 0f, innerRect.width, innerRect.height));
                }
                finally
                {
                    GUI.EndGroup();
                }

                return false;
            }
            catch (Exception ex)
            {
                if (!loggedFallback)
                {
                    loggedFallback = true;
                    Log.Error($"[Better Work Tab] Alpha4 Work tab renderer failed; falling back to vanilla. Exception: {ex}");
                }

                return true;
            }
        }
    }
}
#endif
