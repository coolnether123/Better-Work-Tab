using Better_Work_Tab.API;
using Better_Work_Tab.Features.RaisedPriorityMaximum;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    /// <summary>
    /// Presents Fluffy Work Tab to Better Work Tab's priority registry as an ordinary max-priority mod.
    /// </summary>
    /// <remarks>
    /// Fluffy prefixes <c>Pawn_WorkSettings.GetPriority</c> and <c>SetPriority</c>, so whenever it is
    /// loaded it is the real backing store and its <c>Settings.maxPriority</c> is the real ceiling.
    /// Reading those values through <see cref="FluffyWorkTabGateway"/> keeps type detection inside the
    /// compatibility module.
    /// </remarks>
    internal sealed class FluffyWorkTabPriorityProvider : IMaxPriorityProvider
    {
        public string ProviderId => PriorityProviderIntegrationCatalog.FluffyWorkTabProviderId;

        public string DisplayName => PriorityProviderIntegrationCatalog.FluffyWorkTabDisplayName;

        public int SortOrder => 60;

        public bool IsAvailable => FluffyWorkTabGateway.IsPresent;

        public bool TryGetMaxPriority(out int maxPriority)
        {
            maxPriority = FluffyWorkTabGateway.MaxPriority;
            return IsAvailable;
        }

        public bool TryGetDefaultEnabledPriority(out int defaultEnabledPriority)
        {
            defaultEnabledPriority = FluffyWorkTabGateway.DefaultPriority;
            return IsAvailable;
        }
    }
}
