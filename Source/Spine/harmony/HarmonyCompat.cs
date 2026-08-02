using Verse;

namespace Spine.Harmony.Infrastructure
{
    internal static class ModPrefs
    {
        internal static bool DebugTranspilers => Prefs.DevMode;
        internal static bool TranspilerSafeMode => true;
        internal static bool TranspilerForcePreserveInstructionCount => true;
        internal static bool TranspilerFailFastCritical => true;
        internal static bool TranspilerLogValidationWarnings => false;
    }
}
