using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads.V2;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Projection
{
    /// <summary>
    /// Converts live RimWorld objects into the stable identifiers used by the
    /// projection contract. Workload keys remain the semantic boundary; these
    /// helpers do not retain the supplied game objects.
    /// </summary>
    public static class WorkTabEffectiveStateIds
    {
        public static PawnKey ForPawn(Pawn pawn)
        {
            return new PawnKey(pawn == null || pawn.thingIDNumber <= 0
                ? null
                : pawn.thingIDNumber.ToString(CultureInfo.InvariantCulture));
        }

        public static WorkTypeKey ForWorkType(WorkTypeDef workType)
        {
            return new WorkTypeKey(workType?.defName);
        }

        public static WorkGiverKey ForWorkGiver(WorkGiverDef workGiver)
        {
            return new WorkGiverKey(workGiver?.defName);
        }

        public static WorkloadParentPriorityKey ForParentPriority(
            Pawn pawn,
            WorkTypeDef workType)
        {
            return new WorkloadParentPriorityKey(ForPawn(pawn), ForWorkType(workType));
        }

        public static WorkloadSpecificJobKey ForSpecificJob(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver)
        {
            return new WorkloadSpecificJobKey(
                ForPawn(pawn),
                ForWorkType(workType),
                ForWorkGiver(workGiver));
        }
    }

    /// <summary>
    /// Object-based read helpers for vanilla Harmony callers. The helpers use
    /// the provider currently pushed for the UI pass and preserve the supplied
    /// live fallback when no projection owns a value.
    /// </summary>
    public static class WorkTabEffectiveStateRuntime
    {
        private static readonly HashSet<string> ReportedBlockedOperations =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly AsyncLocal<EffectiveStateRenderPass> RenderPass =
            new AsyncLocal<EffectiveStateRenderPass>();
        private static readonly AsyncLocal<long> PreparingRenderPass =
            new AsyncLocal<long>();

        public static IWorkTabEffectiveStateProvider CurrentProvider =>
            WorkTabEffectiveStateScope.CurrentOrDefault;

        /// <summary>
        /// True only while a preview provider is explicitly scoped around the
        /// Work-tab pass. The empty provider is deliberately live, so ordinary
        /// BWT rendering keeps its existing fallback behavior.
        /// </summary>
        public static bool IsPreviewActive =>
            WorkTabEffectiveStateScope.Current?.IsPreview == true;

        /// <summary>
        /// The preview can own the semantic ordering dimension without yet
        /// having a projected child-column sequence. Presentation consumers
        /// must then suppress live reassignment lookups rather than allowing
        /// the visible order to disagree with the staged workload.
        /// </summary>
        public static bool IsPreviewSpecificJobOrderingBlocked =>
            IsPreviewActive &&
            IsPreviewDimensionOwned(WorkTabEffectiveStateDimension.SpecificJobOrder);

        public static WorkTabEffectiveStateRevision CurrentRevision
        {
            get
            {
                return EnsureRenderPass();
            }
        }

        public static long CurrentRenderPassId => RenderPass.Value?.Id ?? 0L;

        /// <summary>
        /// The id assigned while a provider is being prepared for a new pass.
        /// Projected providers use this to reuse the one live dependency read
        /// captured before their revision token is requested.
        /// </summary>
        internal static long PreparingRenderPassId => PreparingRenderPass.Value;

        /// <summary>
        /// Captures one effective-state token for a complete UI render pass.
        /// Cell-level consumers can then compare the cached token without
        /// repeatedly asking the live authority to resolve its revision.
        /// </summary>
        public static WorkTabEffectiveStateRevision BeginRenderPass()
        {
            return EnsureRenderPass();
        }

        public static void InvalidateRenderPass()
        {
            EffectiveStateRenderPass previous = RenderPass.Value;
            RenderPass.Value = new EffectiveStateRenderPass(
                NextRenderPassId(previous),
                Time.frameCount,
                null,
                default(WorkTabEffectiveStateRevision));
        }

        private static WorkTabEffectiveStateRevision EnsureRenderPass()
        {
            IWorkTabEffectiveStateProvider provider = CurrentProvider;
            EffectiveStateRenderPass previous = RenderPass.Value;
            int frameNumber = Time.frameCount;
            if (previous != null &&
                ReferenceEquals(previous.Provider, provider) &&
                previous.FrameNumber == frameNumber)
            {
                return previous.Revision;
            }

            long passId = NextRenderPassId(previous);
            WorkTabEffectiveStateRevision revision;
            PreparingRenderPass.Value = passId;
            try
            {
                if (provider is ProjectedWorkTabEffectiveStateProvider projected)
                {
                    projected.CaptureBaseRevisionForRenderPass(passId);
                }

                revision = provider.RevisionToken;
            }
            finally
            {
                PreparingRenderPass.Value = 0L;
            }

            RenderPass.Value = new EffectiveStateRenderPass(
                passId,
                frameNumber,
                provider,
                revision);
            return revision;
        }

        private static long NextRenderPassId(EffectiveStateRenderPass previous)
        {
            return unchecked((previous?.Id ?? 0L) + 1L);
        }

        private sealed class EffectiveStateRenderPass
        {
            internal EffectiveStateRenderPass(
                long id,
                int frameNumber,
                IWorkTabEffectiveStateProvider provider,
                WorkTabEffectiveStateRevision revision)
            {
                Id = id;
                FrameNumber = frameNumber;
                Provider = provider;
                Revision = revision;
            }

            internal long Id { get; }
            internal int FrameNumber { get; }
            internal IWorkTabEffectiveStateProvider Provider { get; }
            internal WorkTabEffectiveStateRevision Revision { get; }
        }

        public static bool IsCurrent(WorkTabEffectiveStateRevision revision)
        {
            return revision.IsCurrent(CurrentProvider);
        }

        public static bool TryGetPreviewEditor(
            out IWorkTabEffectiveStateEditor editor)
        {
            editor = null;
            IWorkTabEffectiveStateProvider provider = WorkTabEffectiveStateScope.Current;
            if (provider == null || !provider.IsPreview)
            {
                return false;
            }

            editor = provider as IWorkTabEffectiveStateEditor;
            if (editor != null)
            {
                return true;
            }

            ReportBlocked(
                WorkTabEffectiveStateDimension.ParentPriority,
                "The active preview provider has no effective-state editor.");
            return false;
        }

        /// <summary>
        /// Reports whether a preview provider owns a particular dimension. A
        /// projected provider can deliberately own only part of the effective
        /// state; an unknown preview implementation is treated as owning the
        /// dimension so consumers fail closed instead of falling through to a
        /// live service it cannot safely compose with.
        /// </summary>
        public static bool IsPreviewDimensionOwned(
            WorkTabEffectiveStateDimension dimension)
        {
            if (!IsPreviewActive)
            {
                return false;
            }

            if (!(CurrentProvider is ProjectedWorkTabEffectiveStateProvider projected))
            {
                return true;
            }

            WorkloadOwnershipDimensions ownership;
            switch (dimension)
            {
                case WorkTabEffectiveStateDimension.ParentPriority:
                    ownership = WorkloadOwnershipDimensions.ParentPriorities;
                    break;
                case WorkTabEffectiveStateDimension.ManualMode:
                    ownership = WorkloadOwnershipDimensions.ManualModes;
                    break;
                case WorkTabEffectiveStateDimension.Schedule:
                    ownership = WorkloadOwnershipDimensions.Schedules;
                    break;
                case WorkTabEffectiveStateDimension.SpecificJobOverride:
                    ownership = WorkloadOwnershipDimensions.SpecificJobOverrides;
                    break;
                case WorkTabEffectiveStateDimension.SpecificJobOrder:
                    ownership = WorkloadOwnershipDimensions.SpecificJobOrder;
                    break;
                case WorkTabEffectiveStateDimension.PresentationSetting:
                    ownership = WorkloadOwnershipDimensions.PresentationSettings;
                    break;
                default:
                    return true;
            }

            return (projected.OwnedDimensions & ownership) == ownership;
        }

        public static bool TryGetSpecificJobPriority(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            out int priority)
        {
            priority = 0;
            if (pawn == null || workType == null || workGiver == null)
            {
                return false;
            }

            return CurrentProvider.TryGetSpecificJobIntegerOverride(
                WorkTabEffectiveStateIds.ForSpecificJob(pawn, workType, workGiver),
                out priority);
        }

        public static bool TrySetParentPriority(
            Pawn pawn,
            WorkTypeDef workType,
            int priority,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewEditor(out IWorkTabEffectiveStateEditor editor))
            {
                return false;
            }

            result = editor.SetParentPriority(
                WorkTabEffectiveStateIds.ForParentPriority(pawn, workType),
                priority);
            return AcceptPreviewMutation(result);
        }

        public static bool TrySetSpecificJobPriority(
            int pawnId,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int priority,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (pawnId < 0 || workType == null || workGiver == null ||
                !TryGetPreviewEditor(out IWorkTabEffectiveStateEditor editor))
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOverride,
                        "A pawn-scoped specific-job key is required.");
                }

                return false;
            }

            var key = new WorkloadSpecificJobKey(
                new PawnKey(pawnId.ToString(CultureInfo.InvariantCulture)),
                WorkTabEffectiveStateIds.ForWorkType(workType),
                WorkTabEffectiveStateIds.ForWorkGiver(workGiver));
            result = editor.SetSpecificJobOverride(
                key,
                WorkloadScalarValue.FromInteger(priority));
            return AcceptPreviewMutation(result);
        }

        public static bool TryClearSpecificJobPriority(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewEditor(out IWorkTabEffectiveStateEditor editor) ||
                pawn == null || workType == null || workGiver == null)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOverride,
                        "A pawn-scoped specific-job key is required.");
                }

                return false;
            }

            var key = WorkTabEffectiveStateIds.ForSpecificJob(pawn, workType, workGiver);
            if (CurrentProvider is ProjectedWorkTabEffectiveStateProvider projected &&
                !HasProjectedSpecificJobOverride(projected, key) &&
                projected.BaseProvider != null &&
                projected.BaseProvider.TryGetSpecificJobOverride(key, out _))
            {
                result = Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOverride,
                    "The preview cannot mask an existing authoritative specific-job override.");
                return false;
            }

            result = editor.ClearSpecificJobOverride(key);
            return AcceptPreviewMutation(result);
        }

        private static bool HasProjectedSpecificJobOverride(
            ProjectedWorkTabEffectiveStateProvider projected,
            WorkloadSpecificJobKey key)
        {
            if (projected == null || key == null || !key.IsValid)
            {
                return false;
            }

            IReadOnlyList<WorkloadSpecificJobOverrideEntry> entries =
                projected.ProjectedState?.SpecificJobOverrides;
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                if (entries[i]?.Key != null && entries[i].Key.Equals(key))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TrySetSpecificJobOrder(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int order,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (pawn == null || workType == null || workGiver == null ||
                !TryGetPreviewEditor(out IWorkTabEffectiveStateEditor editor))
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        "A pawn-scoped specific-job key is required.");
                }

                return false;
            }

            result = editor.SetSpecificJobOrder(
                WorkTabEffectiveStateIds.ForSpecificJob(pawn, workType, workGiver),
                order);
            return AcceptPreviewMutation(result);
        }

        public static bool TrySetManualMode(bool manualMode)
        {
            if (!IsPreviewActive)
            {
                WorkPrioritySystem.SetManualPriorities(manualMode);
                return true;
            }

            if (!TryGetPreviewEditor(out IWorkTabEffectiveStateEditor editor))
            {
                return false;
            }

            if (CurrentProvider is ProjectedWorkTabEffectiveStateProvider projected &&
                (projected.OwnedDimensions & WorkloadOwnershipDimensions.ManualModes) == 0)
            {
                ReportBlocked(
                    WorkTabEffectiveStateDimension.ManualMode,
                    "The active preview provider does not own manual mode.");
                return false;
            }

            bool attempted = false;
            IReadOnlyList<WorkTypeDef> workTypes =
                DefDatabase<WorkTypeDef>.AllDefsListForReading;
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (pawn?.workSettings == null || !pawn.workSettings.EverWork)
                {
                    continue;
                }

                PawnKey pawnKey = WorkTabEffectiveStateIds.ForPawn(pawn);
                if (CurrentProvider is ProjectedWorkTabEffectiveStateProvider projectedProvider &&
                    !projectedProvider.CanEditPawn(pawnKey))
                {
                    // Manual priority mode is global in RimWorld, but the
                    // workload editor still records it through represented
                    // pawn/work-type keys. Never manufacture entries for a
                    // pawn outside the active workload scope or for an
                    // excluded session pawn.
                    continue;
                }

                for (int i = 0; i < workTypes.Count; i++)
                {
                    WorkTypeDef workType = workTypes[i];
                    if (workType == null)
                    {
                        continue;
                    }

                    attempted = true;
                    WorkTabEffectiveStateMutationResult result = editor.SetManualMode(
                        WorkTabEffectiveStateIds.ForParentPriority(pawn, workType),
                        manualMode);
                    if (!AcceptPreviewMutation(result))
                    {
                        return false;
                    }
                }
            }

            if (!attempted)
            {
                ReportBlocked(
                    WorkTabEffectiveStateDimension.ManualMode,
                    "No live pawn/work-type keys were available for the preview manual-mode edit.");
                return false;
            }

            return true;
        }

        public static void ReportBlocked(
            WorkTabEffectiveStateDimension dimension,
            string reason)
        {
            string safeReason = reason ?? "unsupported effective-state operation.";
            IWorkTabEffectiveStateProvider provider = CurrentProvider;
            string diagnosticKey = provider.ProviderId + "@" + provider.Revision + "|" +
                                   dimension + "|" + safeReason;
            if (!ReportedBlockedOperations.Add(diagnosticKey))
            {
                return;
            }

            Log.Warning("[BWT] Work-tab preview blocked " + dimension + ": " + safeReason);
        }

        public static bool AcceptPreviewMutation(
            WorkTabEffectiveStateMutationResult result)
        {
            if (!result.IsBlocked)
            {
                return result.Accepted || result.IsNoOp;
            }

            ReportBlocked(result.Dimension, result.Reason);
            return false;
        }

        public static WorkTabEffectiveStateMutationResult Blocked(
            WorkTabEffectiveStateDimension dimension,
            string reason)
        {
            return WorkTabEffectiveStateMutationResult.Blocked(
                dimension,
                CurrentProvider.Revision,
                reason);
        }

        public static int GetParentPriority(
            Pawn pawn,
            WorkTypeDef workType,
            int fallbackPriority)
        {
            if (pawn == null || workType == null)
            {
                return fallbackPriority;
            }

            return CurrentProvider.GetParentPriority(
                WorkTabEffectiveStateIds.ForParentPriority(pawn, workType),
                fallbackPriority);
        }

        public static bool IsManualMode(
            Pawn pawn,
            WorkTypeDef workType,
            bool fallbackManualMode)
        {
            if (pawn == null || workType == null)
            {
                return fallbackManualMode;
            }

            return CurrentProvider.IsManualMode(
                WorkTabEffectiveStateIds.ForParentPriority(pawn, workType),
                fallbackManualMode);
        }

        /// <summary>
        /// Resolves the global manual-mode display value without making the
        /// projection pretend that the vanilla flag is its authority. The
        /// effective-state contract keys manual mode by parent so a preview
        /// stores the same value for the visible pawn/work-type keys; the
        /// first valid key supplies the Work-tab-wide display value.
        /// </summary>
        public static bool GetManualModeForDisplay(bool fallbackManualMode)
        {
            if (!IsPreviewActive)
            {
                return fallbackManualMode;
            }

            IReadOnlyList<WorkTypeDef> workTypes =
                DefDatabase<WorkTypeDef>.AllDefsListForReading;
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (pawn?.workSettings == null || !pawn.workSettings.EverWork)
                {
                    continue;
                }

                for (int i = 0; i < workTypes.Count; i++)
                {
                    WorkTypeDef workType = workTypes[i];
                    if (workType != null &&
                        CurrentProvider.TryGetManualMode(
                            WorkTabEffectiveStateIds.ForParentPriority(pawn, workType),
                            out bool manualMode))
                    {
                        return manualMode;
                    }
                }
            }

            return fallbackManualMode;
        }

        public static ScheduleKey GetSchedule(
            Pawn pawn,
            ScheduleKey fallbackSchedule = null)
        {
            if (pawn == null)
            {
                return fallbackSchedule;
            }

            return CurrentProvider.GetSchedule(
                WorkTabEffectiveStateIds.ForPawn(pawn),
                fallbackSchedule);
        }

        public static WorkloadScalarValue GetSpecificJobOverride(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            WorkloadScalarValue fallbackValue)
        {
            if (pawn == null || workType == null || workGiver == null)
            {
                return fallbackValue;
            }

            return CurrentProvider.GetSpecificJobOverride(
                WorkTabEffectiveStateIds.ForSpecificJob(pawn, workType, workGiver),
                fallbackValue);
        }

        public static int GetSpecificJobPriority(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int fallbackPriority)
        {
            return CurrentProvider.GetSpecificJobIntegerOverride(
                WorkTabEffectiveStateIds.ForSpecificJob(pawn, workType, workGiver),
                fallbackPriority);
        }

        public static int GetSpecificJobOrder(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int fallbackOrder)
        {
            if (pawn == null || workType == null || workGiver == null)
            {
                return fallbackOrder;
            }

            return CurrentProvider.GetSpecificJobOrder(
                WorkTabEffectiveStateIds.ForSpecificJob(pawn, workType, workGiver),
                fallbackOrder);
        }

        public static WorkloadScalarValue GetPresentationSetting(
            string key,
            WorkloadScalarValue fallbackValue)
        {
            return CurrentProvider.GetPresentationSetting(key, fallbackValue);
        }
    }
}
