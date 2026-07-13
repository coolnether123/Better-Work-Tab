using Verse;

namespace Spine.RimWorld.Serialization
{
    /// <summary>
    /// Protects standalone mod documents from overlapping RimWorld's process-wide Scribe state.
    /// Scribe is not reentrant: starting a second operation calls ForceStop and can clear the
    /// active game's cross-reference and PostLoadIniter collections.
    /// </summary>
    internal static class ScribeIsolationGuard
    {
        internal static bool CanStart(string owner, string operation, int warningKey)
        {
            if (Scribe.mode == LoadSaveMode.Inactive)
            {
                return true;
            }

            Log.WarningOnce(
                $"[{owner}] Blocked standalone {operation} because Scribe is currently {Scribe.mode}. " +
                "Standalone mod serialization must never overlap RimWorld or Multiplayer serialization.",
                warningKey);
            return false;
        }
    }
}
