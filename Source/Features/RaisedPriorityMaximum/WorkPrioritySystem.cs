using System;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.ModSupport;
using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    /// <summary>
    /// Central rules for manual work priorities, including extended priority ranges.
    /// </summary>
    internal static class WorkPrioritySystem
    {
        internal const int DisabledPriority = 0;
        private static readonly Color ExtendedPriorityGreen = new Color(0.2f, 0.8f, 0.2f);
        private static readonly Color ExtendedPriorityYellow = new Color(0.9f, 0.82f, 0.42f);
        private static readonly Color ExtendedPriorityTan = new Color(0.74f, 0.62f, 0.43f);
        private static readonly Color ExtendedPriorityGrey = new Color(0.74f, 0.74f, 0.74f);
        private static readonly FieldInfo PawnField =
            typeof(Pawn_WorkSettings).GetField("pawn", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static int NormalizeMaxPriority(int value)
        {
            return PriorityAuthorityBroker.ClampMaxPriority(value);
        }

        /// <summary>
        /// Turns manual priorities on or off, and tells everything that cares.
        ///
        /// The flag is a plain public field on PlaySettings, so it was being
        /// assigned directly from several places -- applying a ruleset, applying
        /// a Rule Builder 2.0 ruleset, restoring a workload, and opening the tab
        /// with auto-enable on. Each of those left the pawns un-notified, so
        /// their priorities were never converted between on/off and numbered,
        /// and left the Work grid's snapshot holding the old mode, so it kept
        /// drawing checkboxes while the header said priorities were on.
        /// </summary>
        internal static bool SetManualPriorities(bool enabled)
        {
            if (Find.PlaySettings == null)
            {
                return false;
            }

            if (Find.PlaySettings.useWorkPriorities == enabled)
            {
                return true;
            }

            if (!TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                return false;
            }

            if (!IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                return false;
            }

            Find.PlaySettings.useWorkPriorities = enabled;
            NotifyManualPrioritiesChanged();
            return true;
        }

        /// <summary>
        /// Republishes the manual-priority mode after the flag has already been
        /// written. Separate from <see cref="SetManualPriorities"/> for the
        /// checkbox, which hands the field to RimWorld by reference and can only
        /// find out afterwards.
        /// </summary>
        internal static void NotifyManualPrioritiesChanged()
        {
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (pawn.Faction == Faction.OfPlayer && pawn.workSettings != null)
                {
                    pawn.workSettings.Notify_UseWorkPrioritiesChanged();
                }
            }

            UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.Priority |
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.Presentation);
        }

        internal static int GetMaxPriority()
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return PriorityConstants.VanillaMax;
            }

            return PriorityAuthorityBroker.GetEffectiveMaxPriority();
        }

        internal static int GetRequestableMaxPriority()
        {
            return PriorityAuthorityBroker.GetRequestableMaxPriority();
        }

        internal static int ClampPriority(int priority)
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return Mathf.Clamp(priority, DisabledPriority, PriorityConstants.VanillaMax);
            }

            return PriorityAuthorityBroker.ClampStoredPriorityForRuntime(priority);
        }

        internal static int ClampPriority(int priority, int maxPriority)
        {
            return Mathf.Clamp(priority, DisabledPriority, NormalizeMaxPriority(maxPriority));
        }

        internal static int OffsetPriorityNumber(int priority, int amount)
        {
            return ClampPriority(priority + amount);
        }

        internal static int GetDefaultEnabledPriority()
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return PriorityConstants.VanillaDefaultEnabled;
            }

            return PriorityAuthorityBroker.GetDefaultEnabledPriority();
        }

        internal static int GetPriorityForPawnWorkType(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings == null || workType == null)
            {
                return GetDefaultEnabledPriority();
            }

            return PriorityAuthorityBroker.GetEffectivePriority(pawn, workType);
        }

        internal static bool TryGetRawStoredPriority(
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType,
            out int priority)
        {
            priority = DisabledPriority;
            if (workSettings?.priorities == null || workType == null) return false;
            priority = workSettings.priorities[workType];
            return true;
        }

        internal static int GetCurrentPriorityForPawnWorkType(Pawn pawn, WorkTypeDef workType)
        {
            if (PriorityAuthorityBroker.ExternalWorkTabHasPriorityAuthority)
            {
                return GetPriorityForPawnWorkType(pawn, workType);
            }

            int basePriority = GetPriorityForPawnWorkType(pawn, workType);
            return TimePriorityService.GetEffectiveWorkTypePriority(pawn, workType, basePriority);
        }

        /// <summary>
        /// Captures the authority revision that protects a Better Work Tab mutation.
        ///
        /// The normal path requires a coherent resolver snapshot owned by BWT. The only
        /// exception is the existing external-import boundary: an importer suspends mirroring
        /// while the authority transition service temporarily exposes BWT as the write owner.
        /// That exception is deliberately observable and cannot be entered by ordinary UI code.
        /// </summary>
        internal static bool TryCaptureBwtMutationAuthority(out long revision)
        {
            revision = 0L;
            PriorityAuthoritySnapshot snapshot = PriorityAuthorityResolver.Resolve();
            bool privilegedImport = ExternalPriorityMirror.IsSuspended &&
                PriorityAuthorityBroker.CurrentAuthority == PriorityAuthorityOwner.BetterWorkTab;
            if ((!snapshot.IsCoherent ||
                 snapshot.Owner != PriorityAuthorityOwner.BetterWorkTab) &&
                !privilegedImport)
            {
                return false;
            }

            revision = PriorityAuthorityResolver.CurrentAuthorityRevision;
            return IsBwtMutationAuthorityCurrent(revision);
        }

        /// <summary>
        /// Revalidates the exact authority revision captured before a mutation.
        /// </summary>
        internal static bool IsBwtMutationAuthorityCurrent(long revision)
        {
            PriorityAuthoritySnapshot snapshot = PriorityAuthorityResolver.Resolve();
            bool privilegedImport = ExternalPriorityMirror.IsSuspended &&
                PriorityAuthorityBroker.CurrentAuthority == PriorityAuthorityOwner.BetterWorkTab;
            return (privilegedImport ||
                    (snapshot.IsCoherent &&
                     snapshot.Owner == PriorityAuthorityOwner.BetterWorkTab)) &&
                PriorityAuthorityResolver.CurrentAuthorityRevision == revision;
        }

        internal static bool TryCaptureExpectedStoredState(
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType,
            ParentPriorityCommandExpectation expectation,
            out long authorityRevision)
        {
            authorityRevision = 0L;
            return expectation != null &&
                   TryCaptureBwtMutationAuthority(out authorityRevision) &&
                   TryGetRawStoredPriority(workSettings, workType, out int storedPriority) &&
                   expectation.MatchesStored(authorityRevision, storedPriority) &&
                   IsBwtMutationAuthorityCurrent(authorityRevision);
        }

        /// <summary>
        /// Writes a work-type priority while Better Work Tab owns a coherent authority snapshot,
        /// then mirrors it to any external work-tab mod that is observing BWT.
        /// </summary>
        /// <remarks>
        /// An external owner is never silently shadow-written by this boundary. External adapters may
        /// still perform their own writes through their own authority contract.
        /// </remarks>
        internal static bool SetPriority(Pawn_WorkSettings workSettings, WorkTypeDef workType, int priority) =>
            ApplyPriority(workSettings, workType, priority, null) != PriorityMutationOutcome.Rejected;

        internal static PriorityMutationOutcome ApplyPriority(
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType,
            int priority,
            ParentPriorityCommandExpectation expectation)
        {
            if (workSettings == null || workType == null)
            {
                return PriorityMutationOutcome.Rejected;
            }

            Pawn pawn = GetPawn(workSettings);
            if (pawn?.Dead == true || !workSettings.EverWork ||
                (pawn != null && pawn.WorkTypeIsDisabled(workType)))
            {
                return PriorityMutationOutcome.Rejected;
            }

            if (!TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                return PriorityMutationOutcome.Rejected;
            }

            workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            if (workSettings.priorities == null)
            {
                return PriorityMutationOutcome.Rejected;
            }

            if (!PriorityMutationTransaction.TryPrepareFinalWrite(
                    () => ClampPriority(priority),
                    () => expectation != null
                        ? TryCaptureExpectedStoredState(
                            workSettings,
                            workType,
                            expectation,
                            out authorityRevision)
                        : IsBwtMutationAuthorityCurrent(authorityRevision),
                    out int clampedPriority))
            {
                return PriorityMutationOutcome.Rejected;
            }

            workSettings.SetPriority(workType, clampedPriority);
            PriorityRangePolicy.InvalidateCache();
            if (IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                ExternalPriorityMirror.NotifyWorkTypeChanged(pawn, workType);
            }

            return PriorityMutationOutcomePolicy.AfterWrite(
                IsBwtMutationAuthorityCurrent(authorityRevision));
        }

        /// <summary>
        /// Writes a work-type priority straight into Better Work Tab's store, bypassing both
        /// <see cref="Pawn_WorkSettings.SetPriority"/> and the Fluffy mirror.
        /// </summary>
        /// <remarks>
        /// Only for the existing authority-handoff import path. Going through the vanilla setter can
        /// run that mod's own prefix, which may cascade the value over every work giver of the work type
        /// and so destroy the very data the import is reading. Pair it with
        /// <see cref="ModSupport.ExternalPriorityMirror.Suspend"/>.
        /// </remarks>
        internal static bool SetStoredPriorityWithoutMirroring(
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType,
            int priority)
        {
            if (workSettings == null || workType == null)
            {
                return false;
            }

            Pawn pawn = GetPawn(workSettings);
            if (pawn?.Dead == true || !workSettings.EverWork)
            {
                return false;
            }

            if (!TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                return false;
            }

            workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            if (workSettings.priorities == null)
            {
                return false;
            }

            int clamped = PriorityAuthorityBroker.ClampPriorityForRequest(priority);
            if (clamped != 0 && pawn != null && pawn.WorkTypeIsDisabled(workType))
            {
                return false;
            }

            if (!IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                return false;
            }

            workSettings.priorities[workType] = clamped;
            PriorityRangePolicy.InvalidateCache();
            workSettings.Notify_UseWorkPrioritiesChanged();
            return true;
        }

        internal static int GetPriorityAfterMouseButton(int currentPriority, int button)
        {
            if (button == 0)
            {
                return PriorityAuthorityBroker.GetPriorityAfterClick(currentPriority, -1);
            }

            if (button == 1)
            {
                return PriorityAuthorityBroker.GetPriorityAfterClick(currentPriority, 1);
            }

            return ClampPriority(currentPriority);
        }

        internal static int GetPriorityAfterCellClick(
            int currentPriority,
            int button,
            bool manualPriorities)
        {
            if ((manualPriorities && button != 0 && button != 1) ||
                (!manualPriorities && button != 0))
            {
                return ClampPriority(currentPriority);
            }

            if (manualPriorities)
            {
                return GetPriorityAfterMouseButton(currentPriority, button);
            }

            return currentPriority > DisabledPriority
                ? DisabledPriority
                : GetDefaultEnabledPriority();
        }

        internal static int GetPriorityAfterBoundedStep(int currentPriority, int direction)
        {
            return PriorityAuthorityBroker.GetNextManualPriority(currentPriority, direction);
        }

        internal static int MapPriorityToVanillaDisplay(int priority)
        {
            if (priority <= DisabledPriority)
            {
                return DisabledPriority;
            }

            int maxPriority = GetMaxPriority();
            if (maxPriority <= 1)
            {
                return 1;
            }

            return Mathf.Clamp((int)Math.Round(Spine.Utils.SpineUtils.Remap(priority, 1, maxPriority, 1, 4)), 1, 4);
        }

        internal static int GetTooltipPriority(Pawn_WorkSettings workSettings, WorkTypeDef workType)
        {
            if (workSettings == null || workType == null)
            {
                return DisabledPriority;
            }

            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabPriorityFeatures)
            {
                return workSettings.GetPriority(workType);
            }

            return MapPriorityToVanillaDisplay(workSettings.GetPriority(workType));
        }

        internal static Color GetPriorityColor(int priority)
        {
            if (priority <= DisabledPriority)
            {
                return Color.grey;
            }

            var settings = BetterWorkTabMod.Settings;
            int maxPriority = GetMaxPriority();
            if (maxPriority <= 4)
            {
                return GetVanillaPriorityColor(priority);
            }

            int clampedPriority = ClampPriority(priority, maxPriority);

            int greenThreshold = settings?.priorityColorPercentage_Green ?? DefaultSettings.priorityColorPercentage_Green;
            int yellowThreshold = settings?.priorityColorPercentage_Yellow ?? DefaultSettings.priorityColorPercentage_Yellow;
            int tanThreshold = settings?.priorityColorPercentage_Tan ?? DefaultSettings.priorityColorPercentage_Tan;

            if (clampedPriority <= GetPriorityColorCutoff(maxPriority, greenThreshold))
            {
                return ExtendedPriorityGreen;
            }

            if (clampedPriority <= GetPriorityColorCutoff(maxPriority, yellowThreshold))
            {
                return ExtendedPriorityYellow;
            }

            if (clampedPriority <= GetPriorityColorCutoff(maxPriority, tanThreshold))
            {
                return ExtendedPriorityTan;
            }

            return ExtendedPriorityGrey;
        }

        private static int GetPriorityColorCutoff(int maxPriority, int percentageThreshold)
        {
            int threshold = Mathf.Clamp(percentageThreshold, 1, 100);
            return Mathf.Clamp(Mathf.CeilToInt(maxPriority * (threshold / 100f)), 1, maxPriority);
        }

        private static Color GetVanillaPriorityColor(int priority)
        {
            switch (priority)
            {
                case 1:
                    return new Color(0f, 1f, 0f);
                case 2:
                    return new Color(1f, 0.9f, 0.5f);
                case 3:
                    return new Color(0.8f, 0.7f, 0.5f);
                case 4:
                    return new Color(0.74f, 0.74f, 0.74f);
                default:
                    return Color.grey;
            }
        }


        internal static Pawn GetPawn(Pawn_WorkSettings workSettings)
        {
            return PawnField?.GetValue(workSettings) as Pawn;
        }
    }
}
