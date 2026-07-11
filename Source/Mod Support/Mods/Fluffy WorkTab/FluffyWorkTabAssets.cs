using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    internal enum FluffyWorkTabIcon
    {
        ManualPriorityToggle,
        PrioritiesDetailed,
        PrioritiesSimple,
        PrioritiesTimed,
        PrioritiesWholeDay,
        Now,
        PinEye,
        PinClock,
        Expand,
        Collapse
    }

    [StaticConstructorOnStartup]
    internal static class FluffyWorkTabAssets
    {
        private static readonly Dictionary<FluffyWorkTabIcon, string> IconPaths =
            new Dictionary<FluffyWorkTabIcon, string>
            {
                { FluffyWorkTabIcon.ManualPriorityToggle, "UI/Icons/numbers" },
                { FluffyWorkTabIcon.PrioritiesDetailed, "UI/Icons/numbers" },
                { FluffyWorkTabIcon.PrioritiesSimple, "UI/Icons/checks" },
                { FluffyWorkTabIcon.PrioritiesTimed, "UI/Icons/clock-scheduler" },
                { FluffyWorkTabIcon.PrioritiesWholeDay, "UI/Icons/whole-day" },
                { FluffyWorkTabIcon.Now, "UI/Icons/now" },
                { FluffyWorkTabIcon.PinEye, "UI/Icons/pin-eye" },
                { FluffyWorkTabIcon.PinClock, "UI/Icons/pin-clock" },
                { FluffyWorkTabIcon.Expand, "UI/Icons/expand" },
                { FluffyWorkTabIcon.Collapse, "UI/Icons/collapse" }
            };

        private static readonly Dictionary<FluffyWorkTabIcon, Texture2D> ResolvedIcons =
            new Dictionary<FluffyWorkTabIcon, Texture2D>();
        private static readonly HashSet<FluffyWorkTabIcon> ResolvedKeys =
            new HashSet<FluffyWorkTabIcon>();

        internal static bool TryGetManualPriorityToggleIcon(out Texture2D texture)
        {
            return TryGetIcon(FluffyWorkTabIcon.ManualPriorityToggle, out texture);
        }

        internal static bool TryGetIcon(FluffyWorkTabIcon icon, out Texture2D texture)
        {
            texture = null;
            if (!FluffyWorkTabGateway.IsPresent || !IconPaths.TryGetValue(icon, out string path))
            {
                return false;
            }

            if (!ResolvedKeys.Contains(icon))
            {
                ResolvedKeys.Add(icon);
                Texture2D resolved = ContentFinder<Texture2D>.Get(path, reportFailure: false);
                if (resolved != null)
                {
                    ResolvedIcons[icon] = resolved;
                }
            }

            return ResolvedIcons.TryGetValue(icon, out texture) && texture != null;
        }
    }
}
