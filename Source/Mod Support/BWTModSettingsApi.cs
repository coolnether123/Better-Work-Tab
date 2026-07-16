using System;
using System.Collections.Generic;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// Public API for registering settings sections that render in Better Work Tab's settings UI.
    /// Register contributors before the settings hierarchy is first drawn when possible.
    /// </summary>
    public static class BWTModSettingsApi
    {
        private static readonly List<IModSettingsContributor> Contributors =
            new List<IModSettingsContributor>();

        internal static event Action ContributorsChanged;

        public static void RegisterContributor(IModSettingsContributor contributor)
        {
            if (contributor == null)
            {
                return;
            }

            if (Contributors.Contains(contributor))
            {
                return;
            }

            Contributors.Add(contributor);
            ContributorsChanged?.Invoke();
        }

        public static IList<IModSettingsContributor> GetContributors()
        {
            return Contributors;
        }
    }
}
