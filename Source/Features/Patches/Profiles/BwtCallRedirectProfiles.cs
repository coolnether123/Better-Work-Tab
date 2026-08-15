using HarmonyLib;
using RimWorld;
using Better_Work_Tab.PawnOrganizer.Patches;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Spine.Harmony.Transpilers.VNext;

namespace Better_Work_Tab.Features.Patches.Profiles
{
    internal static class BwtCallRedirectProfiles
    {
        internal static readonly BwtCallRedirectProfile TipPriorityLookup = Create(
            "BWT.TipPriorityLookup",
            AccessTools.Method(typeof(WidgetsWork), nameof(WidgetsWork.TipForPawnWorker)),
            new BwtCallBinding(PriorityIl.GetPriority),
            new BwtCallBinding(PriorityIl.GetTooltipPriority));

        internal static readonly BwtCallRedirectProfile PawnLabelCloseWorkTab = Create(
            "BWT.PawnColumnWorkerLabel.DoCell.CloseWorkTab",
            AccessTools.Method(typeof(PawnColumnWorker_Label), nameof(PawnColumnWorker_Label.DoCell)),
            new BwtCallBinding(
                AccessTools.Method(
                    typeof(MainTabsRoot),
                    nameof(MainTabsRoot.EscapeCurrentTab),
                    new[] { typeof(bool) })),
            new BwtCallBinding(
                AccessTools.Method(
                    typeof(PawnLabelCloseAdapter),
                    nameof(PawnLabelCloseAdapter.MaybeCloseWorkTab),
                    new[] { typeof(MainTabsRoot), typeof(bool) })));

        private static BwtCallRedirectProfile Create(
            string id, System.Reflection.MethodBase target,
            BwtCallBinding source, BwtCallBinding replacement)
        {
            return new BwtCallRedirectProfile(
                id,
                BwtTargetIdentity.ForMethod(target, BwtBuildIdentity.RimWorld16),
                source,
                replacement,
                MatchMode.ExactlyOne);
        }

    }
}
