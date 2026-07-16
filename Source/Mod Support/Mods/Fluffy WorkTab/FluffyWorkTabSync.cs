using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    /// <summary>
    /// Keeps Fluffy Work Tab's priority tracker in step with Better Work Tab's stores.
    /// </summary>
    /// <remarks>
    /// Fluffy has no work-type storage: <c>WorkTab.PriorityTracker.SetPriority(WorkTypeDef, ...)</c>
    /// simply writes the same value into every work giver of that work type. So any work-type write
    /// destroys per-work-giver detail, and the only safe pattern is:
    /// <list type="number">
    /// <item>write the work-type parent (which cascades), then</item>
    /// <item>re-apply every affected work giver's own schedule.</item>
    /// </list>
    /// Every push here follows that order. Work givers reassigned to another work type are re-applied
    /// from their <em>target</em> work type, so a cascade can never strand them on the wrong value.
    /// <para>
    /// Reads stay on Better Work Tab's own stores rather than
    /// <see cref="PriorityAuthorityBroker.GetEffectivePriority(Pawn, WorkTypeDef)"/>, because that
    /// method reads back out of Fluffy whenever Fluffy holds authority, which would make a push a no-op
    /// that laundered stale data.
    /// </para>
    /// </remarks>
    internal static class FluffyWorkTabSync
    {
        private static int _suspendDepth;

        /// <summary>
        /// True while pushes are suppressed, e.g. during an import that reads <em>out of</em> Fluffy.
        /// </summary>
        internal static bool IsSuspended => _suspendDepth > 0;

        /// <summary>
        /// Suppresses mirroring for the lifetime of the returned scope. Use it around any operation
        /// that treats Fluffy as the source of truth, so Better Work Tab cannot echo half-built state
        /// back over the data it is still reading.
        /// </summary>
        internal static IDisposable Suspend()
        {
            return new SuspendScope();
        }

        /// <summary>
        /// Mirrors one work type, and every work giver a cascade would touch, into Fluffy.
        /// </summary>
        internal static void PushWorkType(Pawn pawn, WorkTypeDef workType)
        {
            if (!CanPush(pawn) || workType == null)
            {
                return;
            }

            try
            {
                PushWorkTypeParent(pawn, workType);
                foreach (WorkGiverDef workGiver in CollectAffectedWorkGivers(workType))
                {
                    PushWorkGiverSchedule(pawn, workGiver);
                }
            }
            catch (Exception ex)
            {
                WarnPushFailed("work type " + workType.defName, ex);
            }
        }

        /// <summary>
        /// Mirrors a single work giver into Fluffy without disturbing its siblings.
        /// </summary>
        internal static void PushWorkGiver(Pawn pawn, WorkGiverDef workGiver)
        {
            if (!CanPush(pawn) || workGiver == null)
            {
                return;
            }

            try
            {
                PushWorkGiverSchedule(pawn, workGiver);
            }
            catch (Exception ex)
            {
                WarnPushFailed("work giver " + workGiver.defName, ex);
            }
        }

        /// <summary>
        /// Mirrors every work type and work giver for one pawn.
        /// </summary>
        internal static void PushPawn(Pawn pawn)
        {
            if (!CanPush(pawn))
            {
                return;
            }

            using (Suspend())
            {
                PushPawnUnsuspended(pawn);
            }
        }

        /// <summary>
        /// Mirrors every pawn. Used by the authority handoff and after a full import.
        /// </summary>
        internal static int PushAllPawns()
        {
            if (!FluffyWorkTabGateway.IsPresent || IsSuspended)
            {
                return 0;
            }

            int pushed = 0;
            using (Suspend())
            {
                foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
                {
                    if (!CanPushIgnoringSuspend(pawn))
                    {
                        continue;
                    }

                    PushPawnUnsuspended(pawn);
                    pushed++;
                }
            }

            return pushed;
        }

        /// <summary>
        /// Mirrors one work type across every pawn. Used when a global schedule changes.
        /// </summary>
        internal static void PushWorkTypeForAllPawns(WorkTypeDef workType)
        {
            if (!FluffyWorkTabGateway.IsPresent || IsSuspended || workType == null)
            {
                return;
            }

            foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
            {
                PushWorkType(pawn, workType);
            }
        }

        /// <summary>
        /// Mirrors one work giver across every pawn. Used when a global schedule changes.
        /// </summary>
        internal static void PushWorkGiverForAllPawns(WorkGiverDef workGiver)
        {
            if (!FluffyWorkTabGateway.IsPresent || IsSuspended || workGiver == null)
            {
                return;
            }

            foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
            {
                PushWorkGiver(pawn, workGiver);
            }
        }

        /// <summary>
        /// Builds the 24-hour work-type schedule from Better Work Tab's own stores.
        /// </summary>
        internal static int[] BuildWorkTypeSchedule(Pawn pawn, WorkTypeDef workType, int fallbackPriority)
        {
            var priorities = new int[TimePriorityService.HoursPerDay];
            TimePriorityTarget target = TimePriorityTarget.ForWorkType(pawn, workType);
            for (int hour = 0; hour < priorities.Length; hour++)
            {
                priorities[hour] = TimePriorityService.GetPriorityAtHour(target, fallbackPriority, hour);
            }

            return priorities;
        }

        /// <summary>
        /// Builds the 24-hour work-giver schedule from Better Work Tab's own stores, layering the
        /// pawn's work-giver override on top of the parent work type's hourly values.
        /// </summary>
        internal static int[] BuildWorkGiverSchedule(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int[] parentSchedule)
        {
            var priorities = new int[TimePriorityService.HoursPerDay];
            TimePriorityTarget target = TimePriorityTarget.ForWorkGiver(pawn, workType, workGiver);
            for (int hour = 0; hour < priorities.Length; hour++)
            {
                int parentPriority = parentSchedule != null && hour < parentSchedule.Length
                    ? parentSchedule[hour]
                    : PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(pawn?.workSettings, workType);
                int fallback = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);
                priorities[hour] = TimePriorityService.GetPriorityAtHour(target, fallback, hour);
            }

            return priorities;
        }

        private static void PushPawnUnsuspended(Pawn pawn)
        {
            List<WorkTypeDef> workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

            // Phase 1: every parent. Each write cascades over that work type's vanilla work givers.
            for (int i = 0; i < workTypes.Count; i++)
            {
                WorkTypeDef workType = workTypes[i];
                if (workType == null || pawn.WorkTypeIsDisabled(workType))
                {
                    continue;
                }

                PushWorkTypeParent(pawn, workType);
            }

            // Phase 2: every work giver, after all cascades have landed. Doing this in a second pass
            // is what stops one work type's cascade from overwriting another's work givers.
            for (int i = 0; i < workTypes.Count; i++)
            {
                WorkTypeDef workType = workTypes[i];
                if (workType == null || pawn.WorkTypeIsDisabled(workType))
                {
                    continue;
                }

                foreach (WorkGiverDef workGiver in CollectAffectedWorkGivers(workType))
                {
                    PushWorkGiverSchedule(pawn, workGiver);
                }
            }
        }

        private static void PushWorkTypeParent(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn.WorkTypeIsDisabled(workType))
            {
                return;
            }

            int fallback = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(pawn.workSettings, workType);
            FluffyWorkTabGateway.TrySetWorkTypePriorities(pawn, workType, BuildWorkTypeSchedule(pawn, workType, fallback));
        }

        private static void PushWorkGiverSchedule(Pawn pawn, WorkGiverDef workGiver)
        {
            // Resolve through the reassignment map: a work giver moved to another work type must take
            // its priorities from where it now lives, not from the column it was cascaded by.
            WorkTypeDef target = WorkGiverReassignmentManager.GetTargetWorkType(workGiver) ?? workGiver.workType;
            if (target == null || pawn.WorkTypeIsDisabled(target))
            {
                return;
            }

            int parentFallback = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(pawn.workSettings, target);
            int[] parentSchedule = BuildWorkTypeSchedule(pawn, target, parentFallback);
            FluffyWorkTabGateway.TrySetWorkGiverPriorities(
                pawn,
                workGiver,
                BuildWorkGiverSchedule(pawn, target, workGiver, parentSchedule));
        }

        /// <summary>
        /// Every work giver a work-type cascade can touch: the vanilla members of the work type, plus
        /// anything Better Work Tab has reassigned into it.
        /// </summary>
        internal static IEnumerable<WorkGiverDef> GetWorkGiversAffectedByWorkTypeCascade(WorkTypeDef workType)
        {
            return CollectAffectedWorkGivers(workType).ToList();
        }

        private static IEnumerable<WorkGiverDef> CollectAffectedWorkGivers(WorkTypeDef workType)
        {
            var seen = new HashSet<WorkGiverDef>();

            List<WorkGiverDef> vanillaMembers = workType.workGiversByPriority;
            for (int i = 0; vanillaMembers != null && i < vanillaMembers.Count; i++)
            {
                if (vanillaMembers[i] != null && seen.Add(vanillaMembers[i]))
                {
                    yield return vanillaMembers[i];
                }
            }

            IReadOnlyList<WorkGiver> displayed = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType);
            for (int i = 0; displayed != null && i < displayed.Count; i++)
            {
                WorkGiverDef def = displayed[i]?.def;
                if (def != null && seen.Add(def))
                {
                    yield return def;
                }
            }
        }

        private static bool CanPush(Pawn pawn)
        {
            return !IsSuspended && CanPushIgnoringSuspend(pawn);
        }

        private static bool CanPushIgnoringSuspend(Pawn pawn)
        {
            return FluffyWorkTabGateway.IsPresent &&
                   pawn != null &&
                   !pawn.Dead &&
                   pawn.workSettings != null &&
                   pawn.workSettings.EverWork &&
                   pawn.workSettings.priorities != null;
        }

        private static void WarnPushFailed(string what, Exception ex)
        {
            BetterWorkTabMod.DebugLog(
                "[FluffyWorkTab] Failed to mirror " + what + " into Fluffy Work Tab: " + ex.Message,
                DebugFeature.ModSupport);
        }

        private sealed class SuspendScope : IDisposable
        {
            private bool _disposed;

            internal SuspendScope()
            {
                _suspendDepth++;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _suspendDepth = Math.Max(0, _suspendDepth - 1);
            }
        }
    }
}
