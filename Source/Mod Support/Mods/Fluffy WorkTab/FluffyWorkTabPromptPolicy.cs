namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    /// <summary>Pure policy for the global active-mod and per-save historical prompts.</summary>
    internal static class FluffyWorkTabPromptPolicy
    {
        internal const int CurrentPromptVersion = 1;

        internal static bool ShouldPrompt(
            bool isCurrentlyActive,
            bool hasSaveEvidence,
            int activeModPromptVersion,
            int savePromptVersion)
        {
            return (isCurrentlyActive && activeModPromptVersion < CurrentPromptVersion) ||
                (hasSaveEvidence && savePromptVersion < CurrentPromptVersion);
        }
    }
}
