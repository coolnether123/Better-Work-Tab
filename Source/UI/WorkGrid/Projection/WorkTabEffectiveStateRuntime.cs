using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Projection
{
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
        private static Action _previewCacheClearer;

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
        /// Registers the optimized snapshot owner's cache-clear callback
        /// without making the projection contract depend on the snapshot
        /// implementation. The callback is renderer-neutral and process-local.
        /// </summary>
        internal static void RegisterPreviewCacheClearer(Action clearer)
        {
            Interlocked.Exchange(ref _previewCacheClearer, clearer);
        }

        internal static void ClearPreviewCacheResidue()
        {
            Action clearer = Volatile.Read(ref _previewCacheClearer);
            clearer?.Invoke();
        }

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

        /// <summary>
        /// Captures the active provider after its render-pass token has been
        /// prepared. Providers without the optional capability remain their
        /// own stable read source.
        /// </summary>
        public static IWorkTabEffectiveStateProvider CaptureCurrentView(
            WorkTabEffectiveStateRevision revision)
        {
            IWorkTabEffectiveStateProvider provider = CurrentProvider;
            if (provider is IWorkTabEffectiveStateViewSource source)
            {
                IWorkTabEffectiveStateProvider captured =
                    source.CaptureEffectiveStateView(revision);
                if (captured != null)
                {
                    return captured;
                }
            }

            return provider;
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
                if (provider is IWorkTabEffectiveStatePassParticipant participant)
                {
                    participant.PrepareRenderPass(passId);
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

        public static bool TryGetPreviewV2Editor(
            out IWorkTabEffectiveStateV2Editor editor)
        {
            editor = null;
            IWorkTabEffectiveStateProvider provider = WorkTabEffectiveStateScope.Current;
            if (provider == null || !provider.IsPreview)
            {
                return false;
            }

            editor = provider as IWorkTabEffectiveStateV2Editor;
            if (editor != null)
            {
                return true;
            }

            ReportBlocked(
                WorkTabEffectiveStateDimension.Schedule,
                "BWT_Workload_HourlyPriorityUnavailable".Translate());
            return false;
        }

        public static WorkTabEffectiveStateResolution<WorkloadSchedulePayload>
            ResolveSchedule(WorkloadScheduleTargetKey key)
        {
            if (CurrentProvider is IWorkTabComposedEffectiveStateProvider composed)
            {
                return composed.ResolveEffectiveSchedule(key);
            }

            return CurrentProvider is IWorkTabEffectiveStateV2Provider v2
                ? v2.ResolveSchedule(key)
                : WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
        }

        /// <summary>
        /// Reads the active preview layer without composing a lower provider.
        /// This is used only to restore an exact preview intent after a paired
        /// operation rejects.
        /// </summary>
        internal static WorkTabEffectiveStateResolution<WorkloadSchedulePayload>
            ResolvePreviewScheduleIntent(WorkloadScheduleTargetKey key)
        {
            return CurrentProvider is IWorkTabEffectiveStateV2Provider v2
                ? v2.ResolveSchedule(key)
                : WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
        }

        public static WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>
            ResolveSpecificJobPriority(WorkloadSpecificJobTargetKey key)
        {
            if (CurrentProvider is IWorkTabComposedEffectiveStateProvider composed)
            {
                return composed.ResolveEffectiveSpecificJobPriority(key);
            }

            return CurrentProvider is IWorkTabEffectiveStateV2Provider v2
                ? v2.ResolveSpecificJobPriority(key)
                : WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
        }

        public static WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>
            ResolveWorkTypeOrder(WorkloadWorkTypeOrderKey key)
        {
            if (CurrentProvider is IWorkTabComposedEffectiveStateProvider composed)
            {
                return composed.ResolveEffectiveWorkTypeOrder(key);
            }

            return CurrentProvider is IWorkTabEffectiveStateV2Provider v2
                ? v2.ResolveWorkTypeOrder(key)
                : WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
        }

        public static WorkTabEffectiveStateResolution<WorkloadSettingValue>
            ResolvePresentationSettingV2(string key)
        {
            if (CurrentProvider is IWorkTabComposedEffectiveStateProvider composed)
            {
                return composed.ResolveEffectivePresentationSetting(key);
            }

            return CurrentProvider is IWorkTabEffectiveStateV2Provider v2
                ? v2.ResolvePresentationSetting(key)
                : WorkTabEffectiveStateResolution<WorkloadSettingValue>.NoOpinion;
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

            if (!(CurrentProvider is IWorkTabPreviewOwnership ownership))
            {
                return true;
            }

            return ownership.OwnsDimension(dimension);
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
                !IsPreviewActive)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOverride,
                        "A pawn-scoped specific-job key is required.");
                }

                return false;
            }

            var key = WorkloadSpecificJobTargetKey.ForPawn(
                new PawnKey(pawnId.ToString(CultureInfo.InvariantCulture)),
                WorkTabEffectiveStateIds.ForWorkType(workType),
                WorkTabEffectiveStateIds.ForWorkGiver(workGiver));
            if (TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor v2Editor))
            {
                result = v2Editor.SetSpecificJobPriority(
                    key,
                    new WorkloadSpecificPriorityPayload(priority));
            }
            else
            {
                result = Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOverride,
                    "The active preview provider has no specific-job editor.");
            }

            return AcceptPreviewMutation(result);
        }

        public static bool TrySetSpecificJobPriority(
            WorkloadSpecificJobTargetKey key,
            int priority,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!IsPreviewActive || key == null || !key.IsValid)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOverride,
                        "A valid typed specific-job key is required.");
                }

                return false;
            }

            if (TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor v2Editor))
            {
                result = v2Editor.SetSpecificJobPriority(
                    key,
                    new WorkloadSpecificPriorityPayload(priority));
                return AcceptPreviewMutation(result);
            }

            result = Blocked(
                WorkTabEffectiveStateDimension.SpecificJobOverride,
                "The active preview provider does not support typed specific-job state.");
            return false;
        }

        public static bool TryClearSpecificJobPriority(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!IsPreviewActive || pawn == null || workType == null || workGiver == null)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOverride,
                        "A pawn-scoped specific-job key is required.");
                }

                return false;
            }

            return TryClearSpecificJobPriority(
                WorkTabEffectiveStateIds.ForSpecificJobTarget(pawn, workType, workGiver),
                out result);
        }

        public static bool TryClearSpecificJobPriority(
            WorkloadSpecificJobTargetKey key,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!IsPreviewActive || key == null || !key.IsValid)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOverride,
                        "A valid typed specific-job key is required.");
                }

                return false;
            }

            if (TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor v2Editor))
            {
                result = v2Editor.ClearSpecificJobPriority(key);
                return AcceptPreviewMutation(result);
            }

            result = Blocked(
                WorkTabEffectiveStateDimension.SpecificJobOverride,
                "The active preview provider does not support typed specific-job tombstones.");
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
                !IsPreviewActive)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        "A pawn-scoped specific-job key is required.");
                }

                return false;
            }

            if (TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor v2Editor))
            {
                WorkloadWorkTypeOrderKey key =
                    WorkTabEffectiveStateIds.ForWorkTypeOrder(pawn, workType);
                WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> resolution =
                    ResolveWorkTypeOrder(key);
                WorkloadWorkTypeOrderPayload payload = resolution.IsSet
                    ? resolution.Value
                    : null;
                if (payload == null || !payload.IsValid)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        "No complete WorkGiver order is available for this preview target.");
                    return false;
                }

                var ordered = new List<WorkGiverKey>(payload.OrderedWorkGivers);
                WorkGiverKey target = WorkTabEffectiveStateIds.ForWorkGiver(workGiver);
                if (!ordered.Remove(target))
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        "The WorkGiver is not present in the complete order.");
                    return false;
                }

                int targetIndex = Math.Max(0, Math.Min(order, ordered.Count));
                ordered.Insert(targetIndex, target);
                result = v2Editor.SetWorkTypeOrder(
                    key,
                    new WorkloadWorkTypeOrderPayload(ordered));
            }
            else
            {
                result = Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOrder,
                    "The active preview provider has no specific-job order editor.");
            }

            return AcceptPreviewMutation(result);
        }

        public static bool TrySetManualMode(bool manualMode)
        {
            if (!IsPreviewActive)
            {
                return WorkTabApplication.Current?.SetManualPriorityMode(manualMode) == true;
            }

            IWorkGridPreviewPort preview = WorkTabEffectiveStateScope.CurrentPreview;
            if (preview == null || !preview.TrySetManualMode(manualMode))
            {
                return false;
            }
            return true;
        }

        public static void ReportBlocked(
            WorkTabEffectiveStateDimension dimension,
            string reason)
        {
            string safeReason = reason ?? "BWT_Workload_OperationFailed".Translate();
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

        /// <summary>Replays one exact preview-layer schedule intent.</summary>
        internal static bool TrySetScheduleIntent(
            WorkloadScheduleTargetKey key,
            WorkTabEffectiveStateResolution<WorkloadSchedulePayload> intent,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor) ||
                key == null || !key.IsValid ||
                (intent.IsSet && (intent.Value == null || !intent.Value.IsValid)))
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.Schedule,
                        "A valid typed schedule intent is required.");
                }

                return false;
            }

            result = intent.IsSet
                ? editor.SetSchedule(key, intent.Value)
                : intent.IsClear
                    ? editor.ClearSchedule(key)
                    : editor.SetScheduleNoOpinion(key);
            return AcceptPreviewMutation(result);
        }

        public static bool TryGetSpecificJobPriority(
            WorkloadSpecificJobTargetKey key,
            out int priority)
        {
            WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> resolution =
                ResolveSpecificJobPriority(key);
            priority = resolution.IsSet ? resolution.Value.Priority : 0;
            return resolution.IsSet;
        }

        public static bool TryGetWorkTypeOrder(
            WorkloadWorkTypeOrderKey key,
            out WorkloadWorkTypeOrderPayload payload)
        {
            WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> resolution =
                ResolveWorkTypeOrder(key);
            payload = resolution.IsSet ? resolution.Value : null;
            return resolution.IsSet;
        }

        public static bool TrySetWorkTypeOrder(
            WorkloadWorkTypeOrderKey key,
            WorkloadWorkTypeOrderPayload payload,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor) ||
                key == null || !key.IsValid || payload == null || !payload.IsValid)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        "A valid complete WorkType order target and payload are required.");
                }

                return false;
            }

            result = editor.SetWorkTypeOrder(key, payload);
            return AcceptPreviewMutation(result);
        }

        public static bool TryClearWorkTypeOrder(
            WorkloadWorkTypeOrderKey key,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor) ||
                key == null || !key.IsValid)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        "A valid typed WorkType order target is required.");
                }

                return false;
            }

            result = editor.ClearWorkTypeOrder(key);
            return AcceptPreviewMutation(result);
        }

        public static bool TryGetPresentationSettingV2(
            string key,
            out WorkloadSettingValue value)
        {
            WorkTabEffectiveStateResolution<WorkloadSettingValue> resolution =
                ResolvePresentationSettingV2(key);
            value = resolution.IsSet ? resolution.Value : default(WorkloadSettingValue);
            return resolution.IsSet;
        }

        public static bool TrySetPresentationSettingV2(
            string key,
            WorkloadSettingValue value,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor) ||
                string.IsNullOrWhiteSpace(key) || !value.IsValid)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.PresentationSetting,
                        "A valid owned presentation-setting key and value are required.");
                }

                return false;
            }

            result = editor.SetPresentationSetting(key, value);
            return AcceptPreviewMutation(result);
        }

        public static bool TryClearPresentationSettingV2(
            string key,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor) ||
                string.IsNullOrWhiteSpace(key))
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.PresentationSetting,
                        "A non-empty presentation-setting key is required.");
                }

                return false;
            }

            result = editor.ClearPresentationSettingV2(key);
            return AcceptPreviewMutation(result);
        }
    }
}
