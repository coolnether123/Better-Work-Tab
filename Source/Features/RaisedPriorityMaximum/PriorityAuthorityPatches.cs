using Better_Work_Tab.Features;
using Better_Work_Tab.ModSupport;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal static class Patch_Pawn_WorkSettings_GetPriority_PriorityAuthority
    {
        private static void Postfix(
            Pawn_WorkSettings __instance,
            WorkTypeDef w,
            Pawn ___pawn,
            ref int __result)
        {
            // RimWorld 1.6 already returns the stored value, including the vanilla manual-off
            // conversion.  Until an external store is registered, authority cannot differ from
            // Better Work Tab, so avoid the authority broker and its compatibility probes on the
            // hot AI getter. Registration/unregistration changes this count synchronously.
            if (!DynamicGameplayPatchController.IsPriorityReadPatchActive ||
                ExternalWorkTabRegistry.RegisteredStoreCount == 0)
            {
                return;
            }

            if (!PriorityAuthorityBroker.BetterWorkTabHasPriorityAuthority)
            {
                return;
            }

            if (__instance?.priorities == null || ___pawn == null || w == null)
            {
                return;
            }

            // This is deliberately a vanilla-compatible presentation read, not BWT's clamped
            // runtime-priority read. A registered store can be unavailable or have no opinion;
            // in that case BWT remains responsible for the stored value but must preserve the
            // exact humanlike/manual-priorities-off conversion performed by vanilla.
            __result = PriorityAuthorityBroker.GetVanillaCompatibleStoredPriority(___pawn, __instance, w);
        }
    }
}
