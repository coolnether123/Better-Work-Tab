using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    [StaticConstructorOnStartup]
    internal static class FluffyWorkTabAssets
    {
        private const string ManualPriorityToggleIconPath = "UI/Icons/numbers";

        private static Texture2D _manualPriorityToggleIcon;
        private static bool _manualPriorityToggleIconResolved;

        internal static bool TryGetManualPriorityToggleIcon(out Texture2D texture)
        {
            if (!FluffyWorkTabGateway.IsPresent)
            {
                texture = null;
                return false;
            }

            if (!_manualPriorityToggleIconResolved)
            {
                _manualPriorityToggleIconResolved = true;
                _manualPriorityToggleIcon = ContentFinder<Texture2D>.Get(ManualPriorityToggleIconPath, reportFailure: false);
            }

            texture = _manualPriorityToggleIcon;
            return texture != null;
        }
    }
}
