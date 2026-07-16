using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Owns event-driven versions for presentation state that is not covered by
    /// priority, schedule, or sub-work data versions.
    /// </summary>
    internal static class WorkGiverPresentationInvalidation
    {
        private static readonly Dictionary<int, int> PawnDynamicVersions = new Dictionary<int, int>(64);
        private static readonly ConditionalWeakTable<PawnCapacitiesHandler, PawnReference> CapacityOwners =
            new ConditionalWeakTable<PawnCapacitiesHandler, PawnReference>();
#if !v1_2 && !v1_1 && !v1_0 && !v0_19 && !v0_18 && !v0_17 && !v0_16 && !v0_15 && !v0_14 && !v0_13 && !vAlpha4
        private static readonly ConditionalWeakTable<Pawn_IdeoTracker, PawnReference> IdeologyOwners =
            new ConditionalWeakTable<Pawn_IdeoTracker, PawnReference>();
#endif
        private static Game _currentGame;

        internal static void RegisterCapacityOwner(PawnCapacitiesHandler handler, Pawn pawn)
        {
            RegisterOwner(CapacityOwners, handler, pawn);
        }

        internal static void NotifyCapacityStateChanged(PawnCapacitiesHandler handler)
        {
            NotifyRegisteredOwner(
                CapacityOwners,
                handler,
                "capacity handler",
                0x42575431);
        }

#if !v1_2 && !v1_1 && !v1_0 && !v0_19 && !v0_18 && !v0_17 && !v0_16 && !v0_15 && !v0_14 && !v0_13 && !vAlpha4
        internal static void RegisterIdeologyOwner(Pawn_IdeoTracker tracker, Pawn pawn)
        {
            RegisterOwner(IdeologyOwners, tracker, pawn);
        }

        internal static void NotifyIdeologyChanged(Pawn_IdeoTracker tracker)
        {
            NotifyRegisteredOwner(
                IdeologyOwners,
                tracker,
                "ideology tracker",
                0x42575432);
        }
#endif

        internal static int GetPawnDynamicVersion(Pawn pawn)
        {
            if (pawn == null)
            {
                return 0;
            }

            EnsureCurrentGame();
            return PawnDynamicVersions.TryGetValue(pawn.thingIDNumber, out int version) ? version : 0;
        }

        internal static void NotifyPawnDynamicStateChanged(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            EnsureCurrentGame();
            int pawnId = pawn.thingIDNumber;
            PawnDynamicVersions[pawnId] = PawnDynamicVersions.TryGetValue(pawnId, out int version)
                ? unchecked(version + 1)
                : 1;
            WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.CapabilitySkill);
        }

        private static void EnsureCurrentGame()
        {
            Game game = Current.Game;
            if (ReferenceEquals(game, _currentGame))
            {
                return;
            }

            _currentGame = game;
            PawnDynamicVersions.Clear();
        }

        private static void RegisterOwner<TOwner>(
            ConditionalWeakTable<TOwner, PawnReference> owners,
            TOwner owner,
            Pawn pawn)
            where TOwner : class
        {
            if (owner == null || pawn == null)
            {
                return;
            }

            owners.Remove(owner);
            owners.Add(owner, new PawnReference(pawn));
        }

        private static void NotifyRegisteredOwner<TOwner>(
            ConditionalWeakTable<TOwner, PawnReference> owners,
            TOwner owner,
            string ownerKind,
            int warningKey)
            where TOwner : class
        {
            if (owner != null && owners.TryGetValue(owner, out PawnReference reference))
            {
                NotifyPawnDynamicStateChanged(reference.Pawn);
                return;
            }

            Spine.Harmony.Infrastructure.MMLog.WarnOnce(
                "WorkGiverPresentationInvalidation." + warningKey,
                "[BWT] Could not resolve the pawn for a WorkGiver presentation " + ownerKind +
                " notification. A compatibility adapter should call WorkGiverApi.NotifyPawnPresentationStateChanged.");
        }

        private sealed class PawnReference
        {
            internal PawnReference(Pawn pawn)
            {
                Pawn = pawn;
            }

            internal Pawn Pawn { get; }
        }
    }
}
