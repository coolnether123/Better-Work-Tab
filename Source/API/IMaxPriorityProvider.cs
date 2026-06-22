namespace Better_Work_Tab.API
{
    public interface IMaxPriorityProvider
    {
        string ProviderId { get; }
        string DisplayName { get; }
        bool IsAvailable { get; }
        int SortOrder { get; }

        bool TryGetMaxPriority(out int maxPriority);
        bool TryGetDefaultEnabledPriority(out int defaultEnabledPriority);
    }
}
