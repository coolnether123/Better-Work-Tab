using HarmonyLib;
using RimWorld;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Transpilers.BwtExactProfile;

namespace Better_Work_Tab.Features.Patches.Profiles
{
    internal static class BwtPriorityProfiles
    {
        internal static readonly BwtPriorityConstantProfile DrawWorkBoxPriorityConstants =
            Create(
                "BWT.DrawWorkBoxPriorityConstants",
                AccessTools.Method(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxFor)),
                BwtPriorityProfileShape.Standard,
                new BwtPriorityConstantEdit(
                    "underflow-limit", BwtPriorityConstantContext.WrapUnderflow,
                    PriorityIl.GetPriority, PriorityConstants.VanillaMax, PriorityIl.GetMaxPriority,
                    PatchRequirement.Required, requiresLocalRelationship: true),
                new BwtPriorityConstantEdit(
                    "overflow-limit", BwtPriorityConstantContext.WrapOverflow,
                    PriorityIl.GetPriority, PriorityConstants.VanillaMax, PriorityIl.GetMaxPriority,
                    PatchRequirement.Required, requiresLocalRelationship: true),
                new BwtPriorityConstantEdit(
                    "default-enabled", BwtPriorityConstantContext.BeforeSetPriority,
                    PriorityIl.SetPriority, PriorityConstants.VanillaDefaultEnabled,
                    PriorityIl.GetDefaultEnabledPriority, PatchRequirement.Optional));

        internal static readonly BwtPriorityConstantProfile HeaderClickedPriorityConstants =
            Create(
                "BWT.HeaderClickedPriorityConstants",
                AccessTools.Method(
                    typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.HeaderClicked),
                    new[] { typeof(UnityEngine.Rect), typeof(PawnTable) }),
                BwtPriorityProfileShape.HeaderClicked,
                new BwtPriorityConstantEdit(
                    "underflow-limit", BwtPriorityConstantContext.WrapUnderflow,
                    PriorityIl.GetPriority, PriorityConstants.VanillaMax, PriorityIl.GetMaxPriority,
                    PatchRequirement.Required, requiresLocalRelationship: true),
                new BwtPriorityConstantEdit(
                    "overflow-limit", BwtPriorityConstantContext.CachedWrapOverflow,
                    null, PriorityConstants.VanillaMax, PriorityIl.GetMaxPriority,
                    PatchRequirement.Required, requiresLocalRelationship: true));

        internal static readonly BwtPriorityConstantProfile SetPriorityUpperBound = Create(
            "BWT.SetPriorityUpperBound",
            AccessTools.Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority)),
            BwtPriorityProfileShape.Standard,
            new BwtPriorityConstantEdit(
                "upper-bound", BwtPriorityConstantContext.SetPriorityUpperBound,
                null, PriorityConstants.VanillaMax, PriorityIl.GetMaxPriority,
                PatchRequirement.Required, externalProviders: KnownExternalProviders()));

        private static BwtPriorityConstantProfile Create(
            string id, System.Reflection.MethodBase target, BwtPriorityProfileShape shape,
            params BwtPriorityConstantEdit[] edits)
        {
            return new BwtPriorityConstantProfile(
                id, BwtTargetIdentity.ForMethod(target, BwtBuildIdentity.RimWorld16),
                shape,
                edits);
        }

        private static BwtExternalProviderBinding[] KnownExternalProviders()
        {
            return new[]
            {
                new BwtExternalProviderBinding(
                    "PriorityMod.Tools.PatchHook", "GetMaximumPriority",
                    typeof(int).FullName, new string[0]),
                new BwtExternalProviderBinding(
                    "PriorityMod.Tools.PatchHook", "GetMaxPriority",
                    typeof(int).FullName, new string[0])
            };
        }
    }
}
