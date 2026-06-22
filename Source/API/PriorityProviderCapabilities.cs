namespace Better_Work_Tab.API
{
    [System.Flags]
    public enum PriorityProviderCapabilities
    {
        None = 0,
        OwnsPriorityRange = 1 << 0,
        OwnsVanillaPriorityRange = 1 << 1,
        OwnsPriorityDisplay = 1 << 2,
        ProvidesPriorityColors = 1 << 3,
        ProvidesClickCycle = 1 << 4,
        ProvidesDefaultEnabledPriority = 1 << 5,
        AllowsBwtUiIntegration = 1 << 6,
        PreservesUnknownExternalPriorities = 1 << 7
    }
}
